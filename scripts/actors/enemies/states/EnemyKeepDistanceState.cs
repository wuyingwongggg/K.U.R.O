using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.States
{
	public partial class EnemyKeepDistanceState : EnemyState
	{
		[ExportCategory("Obstacle Avoidance")]
		[Export(PropertyHint.Range, "10,500,10")]
		public float RaycastDistance = 100f;

		private EnemyBehaviorConfig? _config;
		private float _timer;
		private float _immuneTimer;
		private Vector2 _fleeDirection;
		private bool _damageImmuneActive;

		private const string MovementSuppressMeta = "__keep_distance_active";

		public override void Enter()
		{
			Enemy.SetMeta(MovementSuppressMeta, true);

			_config = Enemy.BehaviorConfig;
			_timer = _config?.BurstDuration ?? 3f;
			_immuneTimer = _config?.BurstDamageImmuneDuration ?? 0f;
			Enemy.Velocity = Vector2.Zero;

			if (_immuneTimer > 0f)
			{
				Enemy.DamageIntercepted += BlockDamage;
				_damageImmuneActive = true;
			}

			Enemy.AnimPlayer?.Play("animations/walk");
		}

		public override void Exit()
		{
			Enemy.RemoveMeta(MovementSuppressMeta);
			Enemy.Velocity = Vector2.Zero;
			RemoveDamageImmunity();
			Enemy.KeepDistanceCooldownRemaining = _config?.BurstCooldown ?? 2f;
		}

		public override void PhysicsUpdate(double delta)
		{
			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy))
				return;

			if (Player == null) return;

			// 仅计时结束退出，Hit/Dying 由 GameActor 自动处理
			_timer -= (float)delta;
			if (_timer <= 0f)
			{
				ChangeState("Idle");
				return;
			}

			// 无敌计时
			if (_damageImmuneActive)
			{
				_immuneTimer -= (float)delta;
				if (_immuneTimer <= 0f)
					RemoveDamageImmunity();
			}

			// 始终背对玩家，避免墙角速度突变导致翻转抽搐
			Vector2 toPlayer = Player.GlobalPosition - Enemy.GlobalPosition;
			if (Mathf.Abs(toPlayer.X) > 0.1f)
				Enemy.FlipFacing(toPlayer.X < 0);

			Vector2 preferredDirection = toPlayer.LengthSquared() > 0.01f
				? -toPlayer.Normalized()
				: (Enemy.FacingRight ? Vector2.Left : Vector2.Right);

			_fleeDirection = FindClearDirection(preferredDirection);

			float speed = Enemy.Speed;
			if (_config != null)
				speed *= _config.BurstSpeedMultiplier;

			Enemy.Velocity = _fleeDirection * speed;

			Enemy.MoveAndSlide();
			Enemy.ClampPositionToScreen();
		}

		private void RemoveDamageImmunity()
		{
			if (!_damageImmuneActive) return;
			Enemy.DamageIntercepted -= BlockDamage;
			_damageImmuneActive = false;
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
