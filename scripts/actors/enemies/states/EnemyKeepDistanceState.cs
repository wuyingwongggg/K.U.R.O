using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyKeepDistanceState : EnemyState
    {
        [Export(PropertyHint.Range, "0,10,0.5")]
        public float MaxFleeDuration = 3f;

        [Export]
        public bool AllowAttackDuringFlee = true;

        [ExportCategory("Obstacle Avoidance")]
        [Export(PropertyHint.Range, "10,500,10")]
        public float RaycastDistance = 100f;

        private EnemyBehaviorConfig? _config;
        private float _timer;
        private Vector2 _fleeDirection;

        private const string MovementSuppressMeta = "__keep_distance_active";

        public override void Enter()
        {
            Enemy.SetMeta(MovementSuppressMeta, true);

            _config = Enemy.BehaviorConfig;
            _timer = MaxFleeDuration;
            Enemy.Velocity = Vector2.Zero;

            if (_config is { FleeDamageImmune: true })
                Enemy.DamageIntercepted += BlockDamage;

            Enemy.AnimPlayer?.Play("animations/walk");
        }

        public override void Exit()
        {
            Enemy.RemoveMeta(MovementSuppressMeta);
            Enemy.Velocity = Vector2.Zero;
            Enemy.DamageIntercepted -= BlockDamage;
        }

        public override void PhysicsUpdate(double delta)
        {
            if (Enemy == null || !GodotObject.IsInstanceValid(Enemy))
                return;

            if (!Enemy.IsPlayerWithinDetectionRange())
            {
                ChangeState("Idle");
                return;
            }

            if (Player == null) return;

            if (AllowAttackDuringFlee && Enemy.CanStartAttack())
            {
                ChangeState("Attack");
                return;
            }

            float currentDist = Enemy.GlobalPosition.DistanceTo(Player.GlobalPosition);
            float targetDist = _config?.EffectiveFleeTargetDistance ?? 120f;

            if (currentDist >= targetDist)
            {
                ChangeState("Idle");
                return;
            }

            if (MaxFleeDuration > 0f)
            {
                _timer -= (float)delta;
                if (_timer <= 0f)
                {
                    ChangeState("Idle");
                    return;
                }
            }

            // 计算本帧最优后撤方向（远离玩家 + 射线避障）
            Vector2 toPlayer = Player.GlobalPosition - Enemy.GlobalPosition;
            Vector2 preferredDirection = toPlayer.LengthSquared() > 0.01f
                ? -toPlayer.Normalized()
                : (Enemy.FacingRight ? Vector2.Left : Vector2.Right);

            _fleeDirection = FindClearDirection(preferredDirection);

            float speed = Enemy.Speed;
            if (_config != null)
                speed *= _config.FleeSpeedMultiplier;

            Enemy.Velocity = _fleeDirection * speed;

            if (Enemy.Velocity.X != 0)
                Enemy.FlipFacing(Enemy.Velocity.X > 0);

            Enemy.MoveAndSlide();
            Enemy.ClampPositionToScreen();
        }

        /// <summary>
        /// 射线检测避障：主方向不通时尝试替代角度。
        /// 优先级：主方向 > 左45° > 右45° > 左90° > 右90° > 180°
        /// </summary>
        private Vector2 FindClearDirection(Vector2 preferredDirection)
        {
            if (preferredDirection == Vector2.Zero)
                return Vector2.Zero;

            preferredDirection = preferredDirection.Normalized();

            float[] directionsToTry = [0f, -45f, 45f, -90f, 90f, 180f];

            foreach (float angleDelta in directionsToTry)
            {
                Vector2 testDirection = preferredDirection.Rotated(Mathf.DegToRad(angleDelta));
                if (IsDirectionClear(testDirection))
                    return testDirection;
            }

            return preferredDirection;
        }

        private bool IsDirectionClear(Vector2 direction)
        {
            if (Enemy == null || direction == Vector2.Zero)
                return false;

            var query = PhysicsRayQueryParameters2D.Create(
                Enemy.GlobalPosition,
                Enemy.GlobalPosition + direction.Normalized() * RaycastDistance
            );

            query.CollisionMask = Enemy.CollisionMask;

            var result = Enemy.GetWorld2D().DirectSpaceState.IntersectRay(query);
            return result.Count == 0;
        }

        private bool BlockDamage(GameActor.DamageEventArgs args)
        {
            args.IsBlocked = true;
            return true;
        }
    }
}
