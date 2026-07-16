using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// NetAdmin 专用 SimpleMeleeAttack。分离检测区和伤害区：
    ///   DetectionArea — CanStart 时判断目标是否在范围内
    ///   DamageArea    — 造成伤害和击退
    /// 未配置时回退到 AttackArea。
    /// </summary>
    public partial class EnemyNetAdminSimpleMeleeAttack : EnemySimpleMeleeAttack
    {
        [Export] public NodePath DetectionAreaPath = new();
        [Export] public NodePath DamageAreaPath = new();

        private Area2D? _detectionArea;
        private Area2D? _damageArea;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _detectionArea = ResolveArea(DetectionAreaPath) ?? AttackArea;
            _damageArea = ResolveArea(DamageAreaPath) ?? AttackArea;
        }

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            if (Player == null) return false;
            return Player.IsHitByArea(_detectionArea);
        }

        protected override void OnActivePhase()
        {
            if (SpawnTiming == EffectSpawnTiming.OnActive)
                SpawnEffectAtEnemy();
            if (RequireAnimationHitTrigger)
            {
                _animationHitReady = true;
                return;
            }
            ApplyAttackAreaMaskOverride(_damageArea);
            DealDamage(_damageArea);
            ApplyKnockbackWithArea(_damageArea);
        }

        protected override void OnAnimationHit()
        {
            if (SpawnTiming == EffectSpawnTiming.OnAnimationHit)
                SpawnEffectAtEnemy();
            ApplyAttackAreaMaskOverride(_damageArea);
            DealDamage(_damageArea);
            ApplyKnockbackWithArea(_damageArea);
        }

        private void ApplyKnockbackWithArea(Area2D? area)
        {
            if (Enemy == null || Player == null) return;
            float distance = Mathf.Max(0f, KnockbackDistance);
            if (distance <= 0f && KnockbackSpeed <= 0f) return;
            if (area != null && !Player.IsHitByArea(area)) return;

            TryApplyPlayerKnockback(
                Player,
                distance,
                Mathf.Max(KnockbackDuration, 0.01f),
                KnockbackSpeed,
                Enemy.FacingRight ? Vector2.Right : Vector2.Left);
        }

        private Area2D? ResolveArea(NodePath path)
        {
            if (path.IsEmpty) return null;
            var area = GetNodeOrNull<Area2D>(path);
            if (area != null) return area;
            return Enemy?.GetNodeOrNull<Area2D>(path);
        }
    }
}
