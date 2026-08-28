using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// NetAdmin 专用 SimpleMeleeAttack。分离检测区和伤害区。
    /// Warmup 期间持续追踪玩家位置，使 DamageArea 对准目标。
    /// </summary>
    public partial class EnemyNetAdminSimpleMeleeAttack : EnemySimpleMeleeAttack
    {
        [Export] public NodePath DetectionAreaPath = new();
        [Export] public NodePath DamageAreaPath = new();
        [Export] public bool TrackTargetDuringWarmup = false;

        private Area2D? _detectionArea;
        private Area2D? _damageArea;
        private CollisionShape2D? _damageShape;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _detectionArea = ResolveArea(DetectionAreaPath) ?? AttackArea;
            _damageArea = ResolveArea(DamageAreaPath) ?? AttackArea;
            _damageShape = _damageArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        }

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            if (Player == null) return false;
            return Player.IsHitByArea(_detectionArea);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (TrackTargetDuringWarmup && IsRunning && Enemy != null && Player != null && _damageShape != null)
            {
                var playerShape = Player.HitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                Vector2 playerTarget = playerShape?.GlobalPosition ?? Player.GlobalPosition;
                Vector2 shapeOffset = _damageShape.GlobalPosition - Enemy.GlobalPosition;
                Vector2 target = playerTarget - shapeOffset;
                float step = Enemy.Speed * (float)delta;
                Enemy.GlobalPosition = new Vector2(
                    Mathf.MoveToward(Enemy.GlobalPosition.X, target.X, step),
                    Mathf.MoveToward(Enemy.GlobalPosition.Y, target.Y, step));
            }

            base._PhysicsProcess(delta);
        }

        protected override void OnActivePhase()
        {
            SpawnEffectAtEnemy(EffectSpawnTiming.OnActive); // entry 独立时机生效；未配置回退模板 SpawnTiming
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
            SpawnEffectAtEnemy(EffectSpawnTiming.OnAnimationHit); // entry 独立时机生效
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
                Enemy.FacingRight ? Vector2.Right : Vector2.Left,
                area);
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
