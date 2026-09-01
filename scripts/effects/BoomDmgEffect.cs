using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    /// <summary>
    /// 爆炸范围伤害脚本。
    /// 生成后立即对半径内的玩家/敌人造成伤害+径向击退，完成后销毁自身。
    /// 挂载在 BoomDmgEffect.tscn 根节点上。
    /// </summary>
    public partial class BoomDmgEffect : Node2D
    {
        /// <summary>伤害来源，由实例化方在 AddChild 前设置。</summary>
        public GameActor? Attacker { get; set; }

        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,9999,1")] public int Damage { get; set; } = 5;
        [Export(PropertyHint.Range, "0,2000,1")] public float Radius { get; set; } = 400f;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance { get; set; } = 300f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration { get; set; } = 0.18f;
        /// <summary>
        /// 直接指定击退速度（像素/秒）。若 > 0 则覆盖 KnockbackDistance/KnockbackDuration 的换算结果。
        /// </summary>
        [Export(PropertyHint.Range, "0,6000,1")] public float KnockbackSpeed { get; set; } = 2000f;

        [ExportCategory("Debug")]
        [Export] public bool ShowDebugRadius { get; set; } = false;
        [Export] public Color DebugRadiusColor { get; set; } = new Color(1f, 0f, 0f, 0.5f);

        [ExportCategory("Targets")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
        [Export] public bool AllowSelfDamage { get; set; } = false;

        public override void _Ready()
        {
            if (ShowDebugRadius)
                QueueRedraw();
            Callable.From(Execute).CallDeferred();
        }

        public override void _Draw()
        {
            if (!ShowDebugRadius) return;
            DrawCircle(Vector2.Zero, Radius, DebugRadiusColor);
        }

        private async void Execute()
        {
            ApplyExplosion();
            if (ShowDebugRadius)
            {
                var timer = GetTree().CreateTimer(1.0);
                await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
            }
            QueueFree();
        }

        private void ApplyExplosion()
        {
            Vector2 origin = GlobalPosition;

            if (TargetableFactions.HasFlag(TargetableFactions.Player))
            {
                if (GetTree().GetFirstNodeInGroup("player") is GameActor playerActor
                    && IsWithinRadius(playerActor.GlobalPosition, origin))
                {
                    ApplyDamageAndKnockback(playerActor, origin);
                }
            }

            if (TargetableFactions.HasFlag(TargetableFactions.Enemy))
            {
                foreach (var node in GetTree().GetNodesInGroup("enemies"))
                {
                    if (node is GameActor enemyActor && IsWithinRadius(enemyActor.GlobalPosition, origin))
                        ApplyDamageAndKnockback(enemyActor, origin);
                }
            }

            if (TargetableFactions.HasFlag(TargetableFactions.WorldItem))
            {
                DealDamageToWorldItemsInRadius(origin);
            }
        }

        /// <summary>
        /// WorldItem 伤害：物理查询（圆，半径 Radius）——碰撞体任意部位进入爆炸圆即命中，
        /// 与视觉接触一致（"中心点距离"判定对大碰撞体（中心到边缘可达数百像素）会在
        /// 爆炸碰到边缘时漏判）。解析接收者（FireWallA/家具）后无视方向限制结算。
        /// </summary>
        private void DealDamageToWorldItemsInRadius(Vector2 origin)
        {
            var space = GetWorld2D()?.DirectSpaceState;
            if (space == null) return;

            var circle = new CircleShape2D { Radius = Radius };
            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = circle,
                Transform = new Transform2D(0f, origin),
                CollisionMask = 1u, // layer 1：barrier StaticBody2D / 家具 RigidBody2D 碰撞体
                CollideWithAreas = true,
                CollideWithBodies = true
            };

            var damaged = new HashSet<ulong>();
            foreach (var result in space.IntersectShape(query))
            {
                if (!result.TryGetValue("collider", out var collider)) continue;
                if (collider.As<GodotObject>() is not Node node) continue;

                var receiver = DamageDispatcher.ResolveDamageReceiver(node, TargetableFactions.WorldItem);
                if (receiver == null || receiver is GameActor) continue; // 敌人/玩家走各自分支
                if (!damaged.Add(receiver.GetInstanceId())) continue;

                // 爆炸是全方位区域效果：无视方向性屏障的方向限制（bypassDirectionCheck）
                DamageDispatcher.DealDamage(receiver, Damage, origin, Attacker, DamageSource.AreaEffect,
                    TargetableFactions.WorldItem, false, null, null, bypassDirectionCheck: true);
            }
        }

        private bool IsWithinRadius(Vector2 position, Vector2 origin)
            => position.DistanceTo(origin) <= Radius;

        private void ApplyDamageAndKnockback(GameActor actor, Vector2 origin)
        {
            if (!GodotObject.IsInstanceValid(actor) || actor.IsDead || actor.IsDeathSequenceActive)
                return;

            // 先造成伤害（对玩家同时设置 _pendingHitKnockback = true）
            actor.TakeDamage(Damage, origin, Attacker);

            // 计算击退速度
            float speed = KnockbackSpeed > 0f
                ? KnockbackSpeed
                : KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f);

            if (speed <= 0f) return;

            Vector2 direction = actor.GlobalPosition - origin;
            if (direction == Vector2.Zero) direction = Vector2.Up;

            Vector2 knockbackVelocity = direction.Normalized() * speed;

            // 玩家：通过 ConsumePendingHitKnockback 走标准击退路径（ApplyKnockback 内置 ForcedMovement 守门）
            if (actor is Actors.Heroes.MainCharacter mainCharacter)
            {
                if (mainCharacter.ConsumePendingHitKnockback())
                {
                    mainCharacter.ApplyKnockback(knockbackVelocity.Normalized(), speed);

                    // 若玩家处于 Frozen 状态且允许外力位移，同步通知
                    var frozenState = mainCharacter.StateMachine?
                        .GetNodeOrNull<Actors.Heroes.States.PlayerFrozenState>("Frozen");
                    if (frozenState != null
                        && mainCharacter.StateMachine?.CurrentState == frozenState
                        && frozenState.AllowExternalDisplacementWhileFrozen)
                    {
                        frozenState.ApplyExternalDisplacement(knockbackVelocity, KnockbackDuration);
                    }
                }
            }
            else
            {
                actor.ApplyKnockback(knockbackVelocity.Normalized(), speed);
            }
        }
    }
}
