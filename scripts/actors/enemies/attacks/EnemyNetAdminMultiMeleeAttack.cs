using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// NetAdmin 多段近战攻击。Active 挂起 Duration 秒期间追踪玩家+PartialLoop。
    /// </summary>
    public partial class EnemyNetAdminMultiMeleeAttack : EnemySimpleMeleeAttack
    {
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float MultiAttackDuration = 2.0f;
        [Export] public NodePath DetectionAreaPath = new();
        [Export] public NodePath DamageAreaPath = new();
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float TrackSpeedMultiplier = 1.0f;

        private Area2D? _detectionArea;
        private Area2D? _damageArea;
        private CollisionShape2D? _damageShape;
        private bool _isActiveHeld;
        private static int _invokeCount;
        private float _activeElapsed;

        public bool IsInMultiAttackPhase => _isActiveHeld;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _detectionArea = ResolveArea(DetectionAreaPath) ?? AttackArea;
            _damageArea = ResolveArea(DamageAreaPath) ?? AttackArea;
            _damageShape = _damageArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        }

        protected override bool ShouldHoldActivePhase() => _isActiveHeld;

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            if (Player == null) return false;
            return Player.IsHitByArea(_detectionArea);
        }

        protected override void OnWarmupStarted()
        {
            _invokeCount++;
            _isActiveHeld = true;
            _activeElapsed = 0f;
            GD.Print($"[MultiDebug] Warmup #{_invokeCount}");
            base.OnWarmupStarted();
        }

        protected override void OnActivePhase()
        {
            _activeElapsed = 0f;
            SpawnEffectAtEnemy(EffectSpawnTiming.OnActive); // entry 独立时机生效；未配置回退模板 SpawnTiming
            if (RequireAnimationHitTrigger) { _animationHitReady = true; return; }
            ApplyAttackAreaMaskOverride(_damageArea);
            DealDamage(_damageArea);
            ApplyKnockbackWithArea(_damageArea);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_isActiveHeld && Enemy != null && Player != null)
            {
                _activeElapsed += (float)delta;
                var playerShape = Player.HitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                Vector2 playerTarget = playerShape?.GlobalPosition ?? Player.GlobalPosition;
                Vector2 shapeOffset = (_damageShape?.GlobalPosition ?? Enemy.GlobalPosition) - Enemy.GlobalPosition;
                float step = Enemy.Speed * TrackSpeedMultiplier * (float)delta;
                Enemy.GlobalPosition = new Vector2(
                    Mathf.MoveToward(Enemy.GlobalPosition.X, playerTarget.X - shapeOffset.X, step),
                    Mathf.MoveToward(Enemy.GlobalPosition.Y, playerTarget.Y - shapeOffset.Y, step));

                if (_activeElapsed >= MultiAttackDuration)
                {
                    _isActiveHeld = false;
                    ForceEnterRecoveryPhase();
                }
            }
            base._PhysicsProcess(delta);
        }

        protected override void OnAnimationHit()
        {
            SpawnEffectAtEnemy(EffectSpawnTiming.OnAnimationHit); // entry 独立时机生效
            ApplyAttackAreaMaskOverride(_damageArea);
            DealDamage(_damageArea);
            ApplyKnockbackWithArea(_damageArea);
        }

        protected override void OnRecoveryStarted()
        {
            _isActiveHeld = false;
            base.OnRecoveryStarted();
        }

        protected override void OnAttackFinished()
        {
            _isActiveHeld = false;
            base.OnAttackFinished();
        }

        private void ApplyKnockbackWithArea(Area2D? area)
        {
            if (Enemy == null || Player == null) return;
            float distance = Mathf.Max(0f, KnockbackDistance);
            if (distance <= 0f) return;
            if (area != null && !Player.IsHitByArea(area)) return;
            TryApplyPlayerKnockback(Player, distance,
                Mathf.Max(KnockbackDuration, 0.01f),
                Enemy.FacingRight ? Vector2.Right : Vector2.Left, area);
        }

        private Area2D? ResolveArea(NodePath path)
        {
            if (path.IsEmpty) return null;
            return GetNodeOrNull<Area2D>(path) ?? Enemy?.GetNodeOrNull<Area2D>(path);
        }
    }
}
