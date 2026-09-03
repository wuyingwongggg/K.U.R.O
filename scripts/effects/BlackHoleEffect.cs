using Godot;
using System;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.Fx;

namespace Kuros.Effects
{
    /// <summary>
    /// 黑洞效果（世界空间 Node2D 效果，非 ActorEffect）：
    /// Area2D 为伤害区域（从 .tscn 获取），敌人进入后每隔 DamageInterval 秒受到 DamagePerTick 点伤害。
    /// AdsorbRadius 为吸附范围，范围内的敌人被物理吸附和直接位移牵引向黑洞中心。
    /// 支持多个黑洞重叠。
    ///
    /// 使用 Node2D 而非 ActorEffect：投掷落点的世界效果，不参与 EffectId 去重与
    /// Actor 生命周期绑定——多投掷各自独立。
    /// </summary>
    [GlobalClass]
    public partial class BlackHoleEffect : Node2D, IAttackerProvider
    {
        private const uint EnemiesLayerMask = 2u;

        [ExportGroup("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Enemy | TargetableFactions.WorldItem;

        /// <summary>每次造成的伤害量。</summary>
        [Export(PropertyHint.Range, "1,999,1")]
        public int DamagePerTick { get; set; } = 10;

        /// <summary>伤害间隔（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float DamageInterval { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "0,600,0.1")] public float Duration { get; set; } = 5.0f;

        [ExportGroup("Adsorb")]
        /// <summary>吸附范围半径（物理吸附）。</summary>
        [Export(PropertyHint.Range, "50,1000,10")]
        public float AdsorbRadius { get; set; } = 300f;

        /// <summary>吸附速度拉扯力量。</summary>
        [Export(PropertyHint.Range, "100,2000,50")]
        public float AdsorbForce { get; set; } = 400f;

        /// <summary>吸附时的速度衰减（<1 减速）。</summary>
        [Export(PropertyHint.Range, "0.5,1,0.01")]
        public float AdsorbVelocityDamping { get; set; } = 0.95f;

        /// <summary>直接位置牵引最小速度（像素/秒），保证可见吸附效果。</summary>
        [Export(PropertyHint.Range, "10,2000,10")]
        public float PullMinPixelsPerSecond { get; set; } = 420f;

        /// <summary>进入中心区后快速收束的半径。</summary>
        [Export(PropertyHint.Range, "0,200,1")]
        public float CenterSnapRadius { get; set; } = 72f;

        /// <summary>中心区额外速度衰减。</summary>
        [Export(PropertyHint.Range, "0.5,0.99,0.01")]
        public float CenterSnapDamping { get; set; } = 0.72f;

        /// <summary>初始强吸阶段持续时间（不造成伤害）。</summary>
        [Export(PropertyHint.Range, "0,3,0.05")]
        public float PullOnlyDuration { get; set; } = 0.0f;

        /// <summary>初始强吸阶段吸附力倍率。</summary>
        [Export(PropertyHint.Range, "1,6,0.1")]
        public float PullBurstMultiplier { get; set; } = 3.6f;

        /// <summary>投掷者（由投掷系统 IAttackerProvider 注入）。</summary>
        public GameActor? Attacker { get; set; }

        private Area2D? _damageArea;
        private double _damageTickTimer = 0.0;
        private double _elapsed = 0.0;
        // 区域内的敌人 → 独立计时器
        private readonly Dictionary<GameActor, float> _damageTimers = new();
        private readonly Dictionary<Node, float> _furnitureDamageTimers = new();
        private Vector2 _blackHoleCenter = Vector2.Zero;

        public override void _Ready()
        {
            ProcessPhysicsPriority = 100;

            _damageArea = GetNodeOrNull<Area2D>("Area2D");
            if (_damageArea == null) return;

            _damageArea.CollisionMask = EnemiesLayerMask | 1u;
            _damageArea.Monitoring = true;
            _damageArea.BodyEntered += OnBodyEntered;
            _damageArea.BodyExited += OnBodyExited;
        }

        public override void _Process(double delta)
        {
            // 位置可能在 _Ready 之后才被生成器设置（AddChild → 设 GlobalPosition 的顺序），
            // 不能缓存初始位置——伤害/吸附查询统一使用实时中心。
            _blackHoleCenter = GlobalPosition;
            _elapsed += delta;

            // 更新伤害计时
            _damageTickTimer += delta;
            if (_elapsed >= PullOnlyDuration && _damageTimers.Count > 0
                && TargetableFactions.HasFlag(TargetableFactions.Enemy))
            {
                var toRemove = new List<GameActor>();
                foreach (var kvp in _damageTimers)
                {
                    var enemy = kvp.Key;
                    if (!IsInstanceValid(enemy) || enemy!.IsDeadOrDying)
                    {
                        toRemove.Add(enemy!);
                        continue;
                    }
                    _damageTimers[enemy] = kvp.Value + (float)delta;
                    if (_damageTimers[enemy] >= DamageInterval)
                    {
                        _damageTimers[enemy] = 0f;
                        enemy.TakeDamage(DamagePerTick, _blackHoleCenter, Attacker, Kuros.Core.Events.DamageSource.AreaEffect);
                    }
                }
                foreach (var e in toRemove) RemoveEnemy(e);
            }

            if (_elapsed >= PullOnlyDuration && _furnitureDamageTimers.Count > 0
                && TargetableFactions.HasFlag(TargetableFactions.WorldItem))
            {
                var toRemoveF = new List<Node>();
                foreach (var kvp in _furnitureDamageTimers)
                {
                    var furn = kvp.Key;
                    if (!IsInstanceValid(furn))
                    {
                        toRemoveF.Add(furn);
                        continue;
                    }
                    _furnitureDamageTimers[furn] = kvp.Value + (float)delta;
                    if (_furnitureDamageTimers[furn] >= DamageInterval)
                    {
                        _furnitureDamageTimers[furn] = 0f;
                        DamageDispatcher.DealDamage(furn, DamagePerTick, _blackHoleCenter, Attacker, DamageSource.AreaEffect, TargetableFactions);
                    }
                }
                foreach (var f in toRemoveF) _furnitureDamageTimers.Remove(f);
            }

            if (Duration > 0f && _elapsed >= Duration)
            {
                Cleanup();
                QueueFree();
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            _blackHoleCenter = GlobalPosition;
            AttractNearbyActors(delta);
        }

        public override void _ExitTree()
        {
            Cleanup();
            base._ExitTree();
        }

        private void OnBodyEntered(Node2D body)
        {
            if (body is GameActor enemy
                && TargetableFactions.HasFlag(TargetableFactions.Enemy))
            {
                if (_damageTimers.ContainsKey(enemy)) return;
                _damageTimers[enemy] = 0f;

                if (_elapsed >= PullOnlyDuration && !enemy.IsDeadOrDying)
                    enemy.TakeDamage(DamagePerTick, _blackHoleCenter, Attacker, Kuros.Core.Events.DamageSource.AreaEffect);
            }
            else if (TargetableFactions.HasFlag(TargetableFactions.WorldItem))
            {
                if (_furnitureDamageTimers.ContainsKey(body)) return;
                _furnitureDamageTimers[body] = 0f;

                if (_elapsed >= PullOnlyDuration)
                    DamageDispatcher.DealDamage(body, DamagePerTick, _blackHoleCenter, Attacker, DamageSource.AreaEffect, TargetableFactions);
            }
        }

        private void OnBodyExited(Node2D body)
        {
            if (body is GameActor enemy)
            {
                RemoveEnemy(enemy);
            }
            else
            {
                _furnitureDamageTimers.Remove(body);
            }
        }

        private void RemoveEnemy(GameActor enemy)
        {
            _damageTimers.Remove(enemy);
        }

        /// <summary>
        /// 吸附 AdsorbRadius 范围内的敌人（物理帧执行，参考 GravityGrenadeBlackHole）。
        /// 同时使用直接位移牵引和速度拉扯，保证可见吸附效果。
        /// </summary>
        private void AttractNearbyActors(double delta)
        {
            var actors = CollectActorsInRadius();
            bool inPullOnlyPhase = _elapsed < PullOnlyDuration;
            float dt = (float)delta;

            foreach (var actor in actors)
            {
                if (!IsInstanceValid(actor) || actor.IsDeadOrDying) continue;
                if (actor.ActiveImmunities.HasFlag(Kuros.Core.ImmunityFlags.ForcedMovement)) continue;

                Vector2 direction = (_blackHoleCenter - actor.GlobalPosition).Normalized();
                float distance = _blackHoleCenter.DistanceTo(actor.GlobalPosition);

                float effectiveRadius = Mathf.Max(AdsorbRadius, 1f);
                float t = 1.0f - Mathf.Clamp(distance / effectiveRadius, 0, 1);
                float attractForce = Mathf.Lerp(AdsorbForce * 0.38f, AdsorbForce, t);
                if (inPullOnlyPhase) attractForce *= PullBurstMultiplier;

                // 直接位移牵引：确保可见吸附，不会被敌人 AI 的速度覆盖
                float pullSpeed = Mathf.Lerp(PullMinPixelsPerSecond, Mathf.Max(PullMinPixelsPerSecond, AdsorbForce), t);
                if (inPullOnlyPhase) pullSpeed *= PullBurstMultiplier;
                actor.GlobalPosition = actor.GlobalPosition.MoveToward(_blackHoleCenter, pullSpeed * dt);

                if (actor is CharacterBody2D characterBody)
                {
                    characterBody.Velocity += direction * attractForce * dt;
                    characterBody.Velocity *= AdsorbVelocityDamping;

                    if (distance <= CenterSnapRadius * 1.5f)
                    {
                        characterBody.GlobalPosition = characterBody.GlobalPosition.Lerp(_blackHoleCenter, 0.5f);
                        characterBody.Velocity *= CenterSnapDamping;
                    }
                }
            }
        }

        /// <summary>
        /// 收集 AdsorbRadius 范围内的敌人（组扫描 + 物理查询兜底）。
        /// </summary>
        private List<GameActor> CollectActorsInRadius()
        {
            var actors = new List<GameActor>();
            var seen = new HashSet<ulong>();

            // 先扫描 enemies 组
            var tree = GetTree();
            if (tree != null)
            {
                foreach (Node node in tree.GetNodesInGroup("enemies"))
                {
                    if (node is not GameActor actor) continue;
                    if (!IsInstanceValid(actor) || actor == Attacker || actor.IsDeadOrDying) continue;
                    if (_blackHoleCenter.DistanceTo(actor.GlobalPosition) > AdsorbRadius) continue;
                    if (seen.Add(actor.GetInstanceId())) actors.Add(actor);
                }
            }

            // 物理查询兜底：防止未加入 enemies 组的敌人漏检
            var space = GetTree()?.Root.GetWorld2D()?.DirectSpaceState;
            if (space != null)
            {
                var query = new PhysicsShapeQueryParameters2D
                {
                    Shape = new CircleShape2D { Radius = AdsorbRadius },
                    Transform = new Transform2D(0, _blackHoleCenter),
                    CollisionMask = EnemiesLayerMask,
                    CollideWithBodies = true,
                    CollideWithAreas = false
                };

                foreach (var hit in space.IntersectShape(query, 64))
                {
                    if (!hit.TryGetValue("collider", out Variant v)) continue;
                    var collider = v.As<GodotObject>();
                    GameActor? actor = collider as GameActor;
                    if (actor == null && collider is Node n)
                    {
                        Node? cur = n;
                        while (cur != null && actor == null) { actor = cur as GameActor; cur = cur.GetParent(); }
                    }
                    if (actor == null || !IsInstanceValid(actor) || actor == Attacker || actor.IsDeadOrDying) continue;
                    if (seen.Add(actor.GetInstanceId())) actors.Add(actor);
                }
            }

            return actors;
        }

        private void Cleanup()
        {
            if (_damageArea != null && IsInstanceValid(_damageArea))
            {
                _damageArea.BodyEntered -= OnBodyEntered;
                _damageArea.BodyExited -= OnBodyExited;
            }
            _damageTimers.Clear();
            _furnitureDamageTimers.Clear();
        }
    }
}
