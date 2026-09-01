using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    /// <summary>
    /// 玩家激光束（浮游炮发射）：视觉/计时继承 <see cref="LaserBeamVisualBase"/>（光束 Grow→Beam→Fade、
    /// 光点独立生命周期），本类负责：方向初始化（GlobalRotation 迁移）、伤害/击退（Enemy 阵营）。
    /// 浮游炮已瞄准，无需追踪。
    /// </summary>
    public partial class LaserBeamPlayerWeapon : LaserBeamVisualBase, IAttackerProvider
    {
        public GameActor? Attacker { get; set; }

        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 2;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 50f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;
        [Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 1000f;

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

        protected override void OnBeamGrown()
        {
            TryDamageEnemies();
        }

        private void TryDamageEnemies()
        {
            if (_hasDamaged) return;
            if (Damage <= 0 && KnockbackSpeed <= 0f && KnockbackDistance <= 0f) return;
            if (_hitArea == null) return;

            // 俯视角地面判定（Area2D 物理重叠，与 LaserBeamA 同构）：判定带 = 光束水平段 × DetectionRadius 垂直容差
            float beamAngle = _hitArea.Rotation;
            Vector2 beamDir = new(Mathf.Cos(beamAngle), Mathf.Sin(beamAngle));
            if (beamDir == Vector2.Zero) beamDir = Vector2.Right;

            var damaged = new HashSet<ulong>();
            // Area 目标：只接受受击判定区（HitArea），玩家攻击/交互 Area 探入光束不触发（同 LaserBeamA 过滤）
            foreach (var area in _hitArea.GetOverlappingAreas())
            {
                if (area.Name != "HitArea") continue;
                TryDamageReceiver(area, beamDir, damaged);
            }
            // Body 目标（敌人 CharacterBody2D 等）
            foreach (var body in _hitArea.GetOverlappingBodies())
                TryDamageReceiver(body, beamDir, damaged);

            _hasDamaged = true;
        }

        private void TryDamageReceiver(Node collider, Vector2 beamDir, HashSet<ulong> damaged)
        {
            // 阵营过滤（ResolveDamageReceiver）→ 接收者解析 + 去重（同一角色多判定区重叠只结算一次）
            if (DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions.Enemy) is not Node receiver)
                return;
            if (!damaged.Add(receiver.GetInstanceId())) return;

            bool dealt = DamageDispatcher.DealDamage(receiver, Damage, GlobalPosition, Attacker,
                DamageSource.AreaEffect, TargetableFactions.Enemy, false, null, beamDir);
            if (!dealt) return;

            // 击退只对 GameActor
            if (receiver is GameActor actor)
            {
                float knockSpeed = KnockbackSpeed > 0f
                    ? KnockbackSpeed
                    : KnockbackDistance > 0f
                        ? KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f)
                        : 0f;
                if (knockSpeed > 0f) actor.ApplyKnockback(beamDir, knockSpeed);
            }
        }
    }
}
