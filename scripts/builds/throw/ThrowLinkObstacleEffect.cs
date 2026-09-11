using System;
using System.Collections.Generic;
using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 节点串联（BuildThrow_A_003）：当前层链接范围内(LinkRange)的乱码块彼此连线,
    /// 形成障碍区——每条线是一条**物理胶囊带**(半径 = LineHalfWidth,物理查询命中敌人层),
    /// 敌人穿线按其受击盒(HitArea/碰撞体)外形判定,而非中心点。
    /// 每穿过一条线,该线独立计时结算一次伤害(每线每 HitInterval);同时上减速。
    /// 集中卡驱动:OnTick 枚举 throwcore_generated_furniture 组做块对几何;
    /// 减速经 SharedSpeedSlowManager 全局叠加(50%@SlowSeconds,免疫 SpeedSlow 跳过)。
    /// TierValues = 各层链接范围(经 .tres PropertyOverrides 注入,如 400/500/600 或更高)。
    /// </summary>
    [GlobalClass]
    public partial class ThrowLinkObstacleEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 400f, 500f, 600f };

        /// <summary>穿线单次伤害。</summary>
        [Export(PropertyHint.Range, "1,100,1")] public float Damage { get; set; } = 10f;
        /// <summary>减速百分比(0-90)。</summary>
        [Export(PropertyHint.Range, "0,90,5")] public float SlowPercent { get; set; } = 50f;
        /// <summary>减速持续秒数。</summary>
        [Export(PropertyHint.Range, "0.5,10,0.5")] public float SlowSeconds { get; set; } = 3f;
        /// <summary>连线判定带宽半径(px):胶囊带半径,物理查询与敌人碰撞体求交。</summary>
        [Export(PropertyHint.Range, "4,80,1")] public float LineHalfWidth { get; set; } = 10f;
        /// <summary>同一敌人同一线的伤害周期(秒);进入该线瞬间立即结算一次。</summary>
        [Export(PropertyHint.Range, "0.2,5,0.1")] public float HitInterval { get; set; } = 1f;
        /// <summary>物理查询间隔(秒):穿线事件无需每帧判定,降频省宽相位查询与分配(10Hz 默认)。</summary>
        [Export(PropertyHint.Range, "0.03,0.5,0.01")] public float ScanInterval { get; set; } = 0.05f;

        /// <summary>连线视觉颜色。</summary>
        [Export] public Color LineColor { get; set; } = new Color(0.3f, 1f, 0.6f, 0.9f);
        /// <summary>判定带底色(半透明,呈现实际伤害宽度)。</summary>
        [Export] public Color BandColor { get; set; } = new Color(0.3f, 1f, 0.6f, 0.15f);

        /// <summary>敌人碰撞层(物理查询 mask;SlowHitArea 同约定:Layer2=enemies)。</summary>
        private const uint EnemyCollisionMask = 2u;

        private readonly List<Node2D> _pieces = new();
        private readonly List<(Vector2 A, Vector2 B)> _segmentsLocal = new(); // 效果本地系,仅供视觉
        private readonly Dictionary<LineKey, float> _lineHits = new(); // (敌人,线)独立计时
        private readonly HashSet<LineKey> _activeKeys = new(); // 每帧在线的键集合(离开即清,重进重触发)
        private readonly Dictionary<GameActor, float> _slowRemaining = new(); // 减速剩余(本效果施加,按敌人)
        private LinkObstacleOverlay? _overlay;
        private float _scanAccum;
        private int _tier = 1;

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        private float CurrentLinkRange => TierValues.Length > 0
            ? TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1]
            : 0f;

        private float SlowMultiplier => Mathf.Max(1f - SlowPercent / 100f, 0.1f);

        protected override void OnTick(double delta)
        {
            if (Actor == null || TierValues.Length == 0) return;
            float dt = (float)delta;

            CollectPiecesAndSegments();

            // 物理查询降频:穿线判定以 ScanInterval 为步长(伤害节奏 HitInterval ≫ 步长,实时性无损)
            _scanAccum += dt;
            if (_scanAccum >= ScanInterval)
            {
                _scanAccum = 0f;
                TickLinkDamage(ScanInterval);
            }

            TickSlowExpiry(dt);
            UpdateOverlay();
        }

        // ── 收集块 + 视觉线段(仅链接范围内的块对)──────────────────

        private void CollectPiecesAndSegments()
        {
            _pieces.Clear();
            foreach (Node node in GetTree().GetNodesInGroup(ThrowCoreEffect.ThrowCoreFurnitureGroup))
            {
                if (node is Node2D n2 && IsInstanceValid(n2) && n2.IsInsideTree())
                    _pieces.Add(n2);
            }

            _segmentsLocal.Clear();
            if (_pieces.Count < 2) return;

            float range = CurrentLinkRange;
            for (int i = 0; i < _pieces.Count; i++)
            {
                for (int j = i + 1; j < _pieces.Count; j++)
                {
                    Vector2 a = ToLocalPos(_pieces[i].GlobalPosition);
                    Vector2 b = ToLocalPos(_pieces[j].GlobalPosition);
                    if (a.DistanceTo(b) <= range)
                        _segmentsLocal.Add((a, b));
                }
            }
        }

        // ── 物理求交伤害:每线独立结算(以 ScanInterval 为步长触发,dt = 本次步长)──

        private void TickLinkDamage(float dt)
        {
            _activeKeys.Clear();
            if (_pieces.Count < 2) return;

            var space = Actor?.GetWorld2D()?.DirectSpaceState;
            if (space == null) return;

            float range = CurrentLinkRange;
            float radius = LineHalfWidth;

            for (int i = 0; i < _pieces.Count; i++)
            {
                for (int j = i + 1; j < _pieces.Count; j++)
                {
                    Node2D pieceA = _pieces[i];
                    Node2D pieceB = _pieces[j];
                    if (!IsInstanceValid(pieceA) || !IsInstanceValid(pieceB)) continue;
                    if (!pieceA.IsInsideTree() || !pieceB.IsInsideTree()) continue;

                    Vector2 a = pieceA.GlobalPosition;
                    Vector2 b = pieceB.GlobalPosition;
                    if (a.DistanceTo(b) > range) continue;

                    ulong keyA = pieceA.GetInstanceId();
                    ulong keyB = pieceB.GetInstanceId();
                    if (keyA > keyB) (keyA, keyB) = (keyB, keyA); // 无序对归一,线身份稳定

                    // 胶囊带:半径 = 带宽;长轴沿线段(局部 Y 轴旋转到线段方向)
                    Vector2 dir = (b - a).Normalized();
                    float len = a.DistanceTo(b);
                    var capsule = new CapsuleShape2D
                    {
                        Radius = radius,
                        Height = len + radius * 2f,
                    };
                    var query = new PhysicsShapeQueryParameters2D
                    {
                        Shape = capsule,
                        Transform = new Transform2D(Mathf.Atan2(-dir.X, dir.Y), (a + b) * 0.5f),
                        CollisionMask = EnemyCollisionMask,
                        CollideWithAreas = true,
                        CollideWithBodies = true,
                    };

                    var hitIds = new HashSet<ulong>(); // 同一线内同一敌人的多个碰撞体只计一次
                    foreach (var result in space.IntersectShape(query))
                    {
                        if (!result.TryGetValue("collider", out var collider)) continue;
                        if (collider.As<GodotObject>() is not Node hitNode) continue;

                        var receiver = DamageDispatcher.ResolveDamageReceiver(hitNode, TargetableFactions.Enemy);
                        if (receiver is not GameActor enemy
                            || !IsInstanceValid(enemy)
                            || enemy.IsDeadOrDying)
                            continue;
                        if (!hitIds.Add(enemy.GetInstanceId())) continue;

                        var key = new LineKey(enemy, keyA, keyB);
                        _activeKeys.Add(key);

                        if (!_lineHits.TryGetValue(key, out float progress))
                        {
                            _lineHits[key] = 0f;
                            DealHit(enemy); // 进入该线瞬间立即结算
                            continue;
                        }

                        progress += dt;
                        if (progress >= HitInterval)
                        {
                            _lineHits[key] = 0f;
                            DealHit(enemy);
                        }
                        else
                        {
                            _lineHits[key] = progress;
                        }
                    }
                }
            }

            PruneStaleKeys();
        }

        /// <summary>清理已不存在的键:敌人离开该线(不在 active)或敌人死亡/线消失。</summary>
        private void PruneStaleKeys()
        {
            if (_lineHits.Count == 0) return;

            var stale = new List<LineKey>();
            foreach (var key in _lineHits.Keys)
            {
                if (!_activeKeys.Contains(key)
                    || !IsInstanceValid(key.Enemy)
                    || key.Enemy.IsDeadOrDying)
                    stale.Add(key);
            }
            foreach (var key in stale)
                _lineHits.Remove(key);
        }

        private void DealHit(GameActor enemy)
        {
            DamageDispatcher.DealDamage(enemy, Damage, enemy.GlobalPosition, Actor,
                DamageSource.AreaEffect, TargetableFactions.Enemy, false);

            if (!enemy.ActiveImmunities.HasFlag(ImmunityFlags.SpeedSlow))
            {
                if (!_slowRemaining.ContainsKey(enemy))
                {
                    _slowRemaining[enemy] = SlowSeconds;
                    SharedSpeedSlowManager.Apply(enemy, SlowMultiplier);
                }
                else
                {
                    _slowRemaining[enemy] = SlowSeconds; // 仍在减速中:刷新时长
                }
            }
        }

        private void TickSlowExpiry(float dt)
        {
            if (_slowRemaining.Count == 0) return;

            foreach (var enemy in new List<GameActor>(_slowRemaining.Keys))
            {
                if (!IsInstanceValid(enemy) || enemy.IsDeadOrDying)
                {
                    _slowRemaining.Remove(enemy);
                    SharedSpeedSlowManager.Clear(enemy);
                    continue;
                }

                float remaining = _slowRemaining[enemy] - dt;
                if (remaining <= 0f)
                {
                    _slowRemaining.Remove(enemy);
                    SharedSpeedSlowManager.Remove(enemy, SlowMultiplier);
                }
                else
                {
                    _slowRemaining[enemy] = remaining;
                }
            }
        }

        // ── 视觉 ────────────────────────────────────────────────────

        private void UpdateOverlay()
        {
            if (_overlay == null || !IsInstanceValid(_overlay))
            {
                _overlay = new LinkObstacleOverlay
                {
                    Name = "LinkObstacleLines",
                    LineColor = LineColor,
                    BandColor = BandColor,
                    HalfWidth = LineHalfWidth,
                };
                AddChild(_overlay);
            }

            _overlay.Segments.Clear();
            foreach (var (a, b) in _segmentsLocal)
                _overlay.Segments.Add((a, b));
            _overlay.QueueRedraw();
        }

        private Vector2 ToLocalPos(Vector2 global)
        {
            if (_overlay != null && IsInstanceValid(_overlay)) return _overlay.ToLocal(global);
            return global;
        }

        public override void OnRemoved()
        {
            foreach (var enemy in new List<GameActor>(_slowRemaining.Keys))
            {
                SharedSpeedSlowManager.Remove(enemy, SlowMultiplier);
            }
            _slowRemaining.Clear();
            _lineHits.Clear();
            _activeKeys.Clear();

            if (_overlay != null)
            {
                _overlay.QueueFree();
                _overlay = null;
            }
            base.OnRemoved();
        }

        /// <summary>命中计时键 = (敌人, 线无序块对)。线身份用块实例 id 归一化,与帧无关。</summary>
        private readonly struct LineKey
        {
            public readonly GameActor Enemy;
            public readonly ulong A;
            public readonly ulong B;

            public LineKey(GameActor enemy, ulong a, ulong b)
            {
                Enemy = enemy;
                A = a;
                B = b;
            }

            public override bool Equals(object? obj)
            {
                return obj is LineKey other
                    && ReferenceEquals(Enemy, other.Enemy)
                    && A == other.A
                    && B == other.B;
            }

            public override int GetHashCode()
            {
                ulong hash = A * 0x9E3779B97F4A7C15UL
                           ^ (B + 0x9E3779B97F4A7C15UL + (A << 6) + (A >> 2))
                           ^ (Enemy?.GetInstanceId() ?? 0UL) * 0xBF58476D1CE4E5B9UL;
                return (int)(hash ^ (hash >> 32));
            }
        }

        // ── 连线视觉(挂效果节点下,本地系坐标)──────────────────────────

        public partial class LinkObstacleOverlay : Node2D
        {
            public Color LineColor { get; set; } = new Color(0.3f, 1f, 0.6f, 0.9f);
            public Color BandColor { get; set; } = new Color(0.3f, 1f, 0.6f, 0.15f);
            public float HalfWidth { get; set; } = 10f;
            public readonly List<(Vector2 A, Vector2 B)> Segments = new();

            public override void _Draw()
            {
                foreach (var (a, b) in Segments)
                {
                    // 半透明判定带(实际伤害宽度) + 亮芯线
                    DrawLine(a, b, BandColor, HalfWidth * 2f, true);
                    DrawLine(a, b, LineColor, 3f, true);
                }
            }
        }
    }
}
