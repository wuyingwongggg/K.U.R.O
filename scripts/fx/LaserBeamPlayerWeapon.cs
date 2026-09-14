using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    /// <summary>
    /// 玩家激光束（浮游炮发射）：视觉/计时继承 <see cref="LaserBeamVisualBase"/>（光束 Grow→Beam→Fade、
    /// 光点独立生命周期），本类负责：方向初始化（GlobalRotation 迁移）、伤害/击退（TargetableFactions 阵营过滤，
    /// 默认仅 Enemy，同 LaserBeamA 可配置）、**首个目标截断（不可穿透，同 LaserBeamA）**——
    /// 光束长度 = 命中带内最近可命中目标沿光束轴的近边距离；伤害仍为一次性（每发只结算一帧）。
    /// 浮游炮已瞄准，无需追踪。
    /// </summary>
    public partial class LaserBeamPlayerWeapon : LaserBeamVisualBase, IAttackerProvider
    {
        public GameActor? Attacker { get; set; }

        [ExportCategory("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Enemy;
        [Export] public bool AllowSelfDamage { get; set; } = false;
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 2;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 200f; // 原速度1000×0.2s折算(此前一直被速度覆盖)
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        public LaserBeamPlayerWeapon()
        {
            // 玩家激光场景节点在 Visual/ 子节点下（基类默认 GlowSprite 在根，兼容 LaserBeamA 场景）
            GlowSpritePath = new NodePath("Visual/GlowSprite");
            BeamSpritePath = new NodePath("Visual/BeamSprite");
            SpotlightPath = new NodePath("Visual/Spotlight");
            SpotGlowSpritePath = new NodePath("Visual/SpotGlow");
        }

        /// <summary>
        /// 首帧方向初始化：生成方（浮游炮）通过 GlobalRotation 设置方向——迁移到视觉/判定带，根复位 0。
        /// </summary>
        protected override void InitializeDirection()
        {
            float angle = Rotation;
            if (_visual != null) _visual.Rotation = angle;
            else Rotation = angle; // 兼容无 Visual 节点的旧结构
            if (_hitArea != null) _hitArea.Rotation = angle;
            Rotation = 0f;
        }

        /// <summary>单帧候选目标（阵营+方向过滤后的真实接收者 + 沿光束轴的近边距离）。</summary>
        private readonly List<(Node Receiver, float NearEdge)> _targets = new();
        /// <summary>单帧截断距离 = 首个可命中目标的近边；无目标 = 不截断。视觉与伤害共用同一判据。</summary>
        private float _stopDistance = float.MaxValue;
        /// <summary>接触线容差（像素）：近边差在此范围内的目标视为"同一排"，一并命中。</summary>
        private const float StopEdgeTolerance = 2f;

        /// <summary>单帧收集：命中带内、通过阵营/方向过滤的真实接收者 + 各自沿光束轴的近边距离，
        /// 并求出截断距离（最小近边）——本帧视觉截断与伤害筛选共用同一结果（所见即所伤）。
        /// 判定带保持全长（基类 UpdateBeam），截断只作用于视觉与伤害筛选，防反复伸缩抖动。</summary>
        private void RefreshTargets()
        {
            _targets.Clear();
            _stopDistance = float.MaxValue;
            if (_hitArea == null) return;
            if (Damage <= 0 && KnockbackDistance <= 0f) return;

            Vector2 beamDir = ResolveBeamDir();
            Vector2 origin = _hitArea.GlobalPosition;

            // Area 目标：只接受受击判定区（HitArea/TriggerArea），玩家攻击/交互 Area 探入光束不触发（同 LaserBeamA 过滤）
            foreach (var area in _hitArea.GetOverlappingAreas())
            {
                if (area.Name != "HitArea" && area.Name != "TriggerArea") continue;
                AddTarget(area, origin, beamDir);
            }
            // Body 目标（敌人 CharacterBody2D、家具 RigidBody2D 等）
            foreach (var body in _hitArea.GetOverlappingBodies())
                AddTarget(body, origin, beamDir);
        }

        private void AddTarget(Node collider, Vector2 origin, Vector2 beamDir)
        {
            if (DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions) is not Node receiver) return;
            // 方向性目标（FireWallA 等屏障）拒收本方向时视为未命中 → 穿透：不伤害、也不构成遮挡
            if (!DamageDispatcher.AcceptsAttackDirection(collider, beamDir, TargetableFactions, origin)) return;
            float nearEdge = DistanceAlongAxisToNearEdge(collider, origin, beamDir);
            if (nearEdge < _stopDistance) _stopDistance = nearEdge;
            _targets.Add((receiver, nearEdge));
        }

        /// <summary>伤害结算（一次性，同原设计：每发光束只结算一次，之后不再造成伤害）：
        /// 仅命中截断距离内的目标（首个目标及其同排）——其后的目标被遮挡，不穿透。</summary>
        private void ApplyDamage()
        {
            if (_hasDamaged) return;
            if (_hitArea == null) return;
            if (Damage <= 0 && KnockbackDistance <= 0f) return;

            float stop = Mathf.Min(_stopDistance, MaxLength);
            Vector2 beamDir = ResolveBeamDir();
            var damaged = new HashSet<ulong>();
            foreach (var (receiver, nearEdge) in _targets)
            {
                if (nearEdge > stop + StopEdgeTolerance) continue;
                TryDamageReceiver(receiver, beamDir, damaged);
            }

            _hasDamaged = true;
        }

        /// <summary>光束轴向（浮游炮在 2D 平面瞄准，方向含 Y 分量；判定带旋转 = 迁移进来的 GlobalRotation）。</summary>
        private Vector2 ResolveBeamDir()
        {
            float beamAngle = _hitArea?.Rotation ?? 0f;
            Vector2 beamDir = new(Mathf.Cos(beamAngle), Mathf.Sin(beamAngle));
            return beamDir == Vector2.Zero ? Vector2.Right : beamDir;
        }

        /// <summary>视觉截断：光束只画到截断距离（首个目标近边）——判定带保持全长，仅视觉层缩短。</summary>
        protected override void UpdateBeam()
        {
            base.UpdateBeam();
            TruncateBeamVisual(_stopDistance);
        }

        public override void _Process(double delta)
        {
            // 单次物理查询：候选目标 + 截断距离（须在基类之前——UpdateBeam 用本帧数据截断视觉）
            RefreshTargets();
            base._Process(delta);
            // Beam 持续阶段（生长完成后、淡出结束前）：首个满足时机的一帧结算伤害（一次性）
            if (_beamPhaseElapsed >= GrowDuration)
                ApplyDamage();
        }

        private void TryDamageReceiver(Node receiver, Vector2 beamDir, HashSet<ulong> damaged)
        {
            // 去重（同一角色多判定区重叠只结算一次）
            if (!damaged.Add(receiver.GetInstanceId())) return;

            bool dealt = DamageDispatcher.DealDamage(receiver, Damage, GlobalPosition, Attacker,
                DamageSource.AreaEffect, TargetableFactions, AllowSelfDamage, null, beamDir);
            if (!dealt) return;

            // 击退只对 GameActor
            if (receiver is GameActor actor && KnockbackDistance > 0f)
                actor.ApplyKnockbackDisplacement(beamDir, KnockbackDistance, KnockbackDuration);
        }
    }
}
