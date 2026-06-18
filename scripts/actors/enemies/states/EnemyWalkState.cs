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

			// 贴脸型敌人：玩家不在攻击范围内时，优先突进逼近
			if (config != null && config.Positioning != EnemyBehaviorConfig.PositioningStrategy.KeepDistance)
			{
				bool playerInAttackRange = Enemy.IsPlayerInAttackRange();
				if (!playerInAttackRange && Enemy.StateMachine.HasState("CloseIn")
					&& Enemy.CloseInCooldownRemaining <= 0f)
				{
					ChangeState("CloseIn");
					return;
				}
			}

			// 远程/保持距离型敌人：玩家在攻击范围内且靠太近时，优先于攻击触发后撤
			if (config != null && config.Positioning != EnemyBehaviorConfig.PositioningStrategy.CloseIn)
			{
				float dist = Enemy.GlobalPosition.DistanceTo(Player!.GlobalPosition);
				if (dist <= config.MinComfortDistance && Enemy.IsPlayerInAttackRange()
					&& Enemy.StateMachine.HasState("KeepDistance")
					&& Enemy.KeepDistanceCooldownRemaining <= 0f)
				{
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

			if (Mathf.Abs(direction.X) > 0.1f)
			{
				Enemy.FlipFacing(direction.X > 0);
			}

			Enemy.MoveAndSlide();
			Enemy.ClampPositionToScreen();
		}
	}
}
