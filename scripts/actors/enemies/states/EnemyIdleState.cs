using Godot;
using Kuros.Actors.Enemies;

namespace Kuros.Actors.Enemies.States
{
	public partial class EnemyIdleState : EnemyState
	{
		public override void Enter()
		{
			Enemy.Velocity = Vector2.Zero;
			Enemy.AnimPlayer?.Play("animations/idle");
		}

		public override void PhysicsUpdate(double delta)
		{
			Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * 2.0f * (float)delta);
			Enemy.MoveAndSlide();

			bool playerDetected = Enemy.IsPlayerWithinDetectionRange();
			var config = Enemy.BehaviorConfig;

			bool playerInAttackRange = Enemy.IsPlayerInAttackRange();

			// 贴脸型敌人：玩家不在攻击范围内时，优先突进逼近
			if (config != null && config.Positioning != EnemyBehaviorConfig.PositioningStrategy.KeepDistance
				&& playerDetected)
			{
				if (!playerInAttackRange && Enemy.StateMachine.HasState("CloseIn")
					&& Enemy.CloseInCooldownRemaining <= 0f)
				{
					ChangeState("CloseIn");
					return;
				}
			}

			// 保持距离型敌人：玩家在攻击范围内且太近时，优先于攻击触发后撤
			if (config != null && config.Positioning != EnemyBehaviorConfig.PositioningStrategy.CloseIn
				&& playerDetected)
			{
				float dist = Enemy.GlobalPosition.DistanceTo(Enemy.PlayerTarget!.GlobalPosition);

				if (dist <= config.MinComfortDistance && playerInAttackRange
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

			// 保持距离型敌人在理想射程内不追
			if (config != null && config.Positioning != EnemyBehaviorConfig.PositioningStrategy.CloseIn
				&& playerDetected && !playerInAttackRange)
			{
				float dist = Enemy.GlobalPosition.DistanceTo(Enemy.PlayerTarget!.GlobalPosition);

				if (dist >= config.MinComfortDistance)
					return;
			}

			// 玩家在检测范围但不在攻击范围内 ��� 追击
			if (playerDetected && !playerInAttackRange)
			{
				ChangeState("Walk");
			}
		}
	}
}
