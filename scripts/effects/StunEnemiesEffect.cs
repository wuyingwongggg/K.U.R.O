using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 持续区域眩晕效果（世界空间 Node2D 效果，非 ActorEffect）。
    /// 效果存活期间，Area2D 范围内所有 Enemies 层的敌人持续被冻结；
    /// 效果到期时自动解除全部眩晕。
    ///
    /// 使用 Node2D 而非 ActorEffect：不参与 EffectId 去重与 Actor 生命周期绑定——
    /// 多投掷（如多个烟雾弹）各自独立眩晕区域。
    /// </summary>
    [GlobalClass]
    public partial class StunEnemiesEffect : Node2D
    {
        private const uint EnemiesLayerMask = 2u;

        [Export(PropertyHint.Range, "0,600,0.1")] public float Duration { get; set; } = 5.0f;

        private Area2D? _area;
        private readonly HashSet<GameActor> _stunnedEnemies = new();
        private bool _cleaned = false;
        private float _elapsed;
        // 每个实例唯一前缀，便于精确移除 FreezeEffect
        private string _idPrefix = "";

        public override void _Ready()
        {
            _idPrefix = $"area_stun_{GetInstanceId()}";

            _area = GetNodeOrNull<Area2D>("Area2D");
            if (_area == null)
            {
                return;
            }

            _area.CollisionMask = EnemiesLayerMask;
            _area.Monitoring = true;
            _area.BodyEntered += OnBodyEntered;
            _area.BodyExited += OnBodyExited;

            // 等物理帧同步后扫描已在范围内的敌人（根已定位到投掷落点）
            CallDeferred(MethodName.InitialScan);
        }

        public override void _Process(double delta)
        {
            _elapsed += (float)delta;
            if (Duration > 0f && _elapsed >= Duration)
            {
                Cleanup();
                QueueFree();
            }
        }

        public override void _ExitTree()
        {
            Cleanup();
            base._ExitTree();
        }

        private void InitialScan()
        {
            if (_area == null || !IsInstanceValid(_area)) return;

            // GetOverlappingBodies() 依赖物理帧，移动 Area2D 后立刻调用结果为空。
            // 改用直接空间查询，立即得到落点处的所有敌人。
            var shapeNode = _area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (shapeNode?.Shape == null) return;

            var spaceState = _area.GetWorld2D().DirectSpaceState;
            if (spaceState == null) return;

            Vector2 center = _area.GlobalPosition;
            var queryParams = new PhysicsShapeQueryParameters2D
            {
                Shape = shapeNode.Shape,
                Transform = new Transform2D(0f, center),
                CollisionMask = EnemiesLayerMask,
                CollideWithBodies = true,
                CollideWithAreas = false
            };

            foreach (var result in spaceState.IntersectShape(queryParams))
            {
                if (!result.TryGetValue("collider", out var colliderVar)) continue;
                if (colliderVar.As<GodotObject>() is GameActor enemy)
                    StunEnemy(enemy);
            }
        }

        private void OnBodyEntered(Node2D body)
        {
            if (body is GameActor enemy)
                StunEnemy(enemy);
        }

        private void OnBodyExited(Node2D body)
        {
            if (_cleaned) return;
            if (body is GameActor enemy)
                UnstunEnemy(enemy);
        }

        private void UnstunEnemy(GameActor enemy)
        {
            if (_cleaned) return;
            if (!_stunnedEnemies.Contains(enemy)) return;
            _stunnedEnemies.Remove(enemy);
            enemy.RemoveEffect($"{_idPrefix}_{enemy.GetInstanceId()}");
            enemy.FrozenStateRemainingTime = 0f;
        }

        private void StunEnemy(GameActor enemy)
        {
            if (_cleaned) return;
            if (!IsInstanceValid(enemy)) return;
            if (_stunnedEnemies.Contains(enemy)) return;
            if (enemy.ActiveImmunities.HasFlag(Kuros.Core.ImmunityFlags.Stun)) return;

            _stunnedEnemies.Add(enemy);
            var freeze = new FreezeEffect
            {
                Duration = this.Duration,   // 与区域眩晕效果同步倒计时
                EffectId = $"{_idPrefix}_{enemy.GetInstanceId()}"
            };
            enemy.ApplyEffect(freeze);
        }

        private void Cleanup()
        {
            if (_cleaned) return;
            _cleaned = true;

            if (_area != null && IsInstanceValid(_area))
            {
                _area.BodyEntered -= OnBodyEntered;
                _area.BodyExited -= OnBodyExited;
            }

            foreach (var enemy in _stunnedEnemies)
            {
                if (!IsInstanceValid(enemy)) continue;

                // 移除应用的 FreezeEffect
                enemy.RemoveEffect($"{_idPrefix}_{enemy.GetInstanceId()}");

                // 清空残留的 Frozen 状态恢复时长，防止后续 Hit 状态错误恢复 Frozen
                enemy.FrozenStateRemainingTime = 0f;
            }
            _stunnedEnemies.Clear();
        }
    }
}
