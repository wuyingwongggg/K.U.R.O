using Godot;
using Kuros.Actors.Enemies;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyWalkState : EnemyState
    {
        public override void Enter()
        {
            Enemy.AnimPlayer?.Play("animations/Walk");
        }

        public override void PhysicsUpdate(double delta)
        {
            if (!Enemy.IsPlayerWithinDetectionRange())
            {
                ChangeState("Idle");
                return;
            }

            var config = Enemy.BehaviorConfig;

            // 远程/保持距离型敌人：玩家靠太近时触发后撤
            if (config != null && config.Positioning != EnemyBehaviorConfig.PositioningStrategy.CloseIn)
            {
                float dist = Enemy.GlobalPosition.DistanceTo(Player!.GlobalPosition);
                GD.Print($"[{Enemy.Name}] Walk: dist={dist:F0} minComfort={config.MinComfortDistance}");
                if (dist <= config.MinComfortDistance && Enemy.StateMachine.HasState("KeepDistance"))
                {
                    GD.Print($"[{Enemy.Name}] Walk → KeepDistance");
                    ChangeState("KeepDistance");
                    return;
                }
            }

            if (Enemy.CanStartAttack())
            {
                ChangeState("Attack");
                return;
            }

            // 若有外部移动组件，跳过自身移动
            if (Enemy.HasMeta("__movement_component_registered"))
                return;

            Vector2 direction = Enemy.GetDirectionToPlayer();
            Vector2 velocity = direction * Enemy.Speed;

            Enemy.Velocity = velocity;

            if (direction.X != 0)
            {
                Enemy.FlipFacing(direction.X > 0);
            }

            Enemy.MoveAndSlide();
            Enemy.ClampPositionToScreen();
        }
    }
}
