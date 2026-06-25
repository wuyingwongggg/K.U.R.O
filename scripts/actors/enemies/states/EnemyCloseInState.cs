using Godot;

namespace Kuros.Actors.Enemies.States
{
	public partial class EnemyCloseInState : EnemyState
	{
		[ExportCategory("Obstacle Avoidance")]
		[Export(PropertyHint.Range, "10,500,10")]
		public float RaycastDistance = 100f;

		private EnemyBehaviorConfig? _config;
		private float _timer;
		private Vector2 _rushDirection;

		private const string MovementSuppressMeta = "__close_in_active";

		public override void Enter()
		{
			Enemy.SetMeta(MovementSuppressMeta, true);

			_config = Enemy.BehaviorConfig;
			_timer = _config?.BurstDuration ?? 1f;
			Enemy.Velocity = Vector2.Zero;

			Enemy.AnimPlayer?.Play("animations/walk");
		}

		public override void Exit()
		{
			Enemy.RemoveMeta(MovementSuppressMeta);
			Enemy.Velocity = Vector2.Zero;
			Enemy.CloseInCooldownRemaining = _config?.BurstCooldown ?? 3f;
		}

		public override void PhysicsUpdate(double delta)
		{
			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy))
				return;

			if (Player == null) return;

			// 计时结束退出
			_timer -= (float)delta;
			if (_timer <= 0f)
			{
				ChangeState("Idle");
				return;
			}

			// 冲向玩家侧方偏移点，避免与玩家重叠
			Vector2 target = Enemy.GetApproachTarget();
			Vector2 toTarget = target - Enemy.GlobalPosition;
			if (Mathf.Abs(toTarget.X) > 0.1f)
				Enemy.FlipFacing(toTarget.X > 0);

			Vector2 preferredDirection = toTarget.LengthSquared() > 0.01f
				? toTarget.Normalized()
				: (Enemy.FacingRight ? Vector2.Right : Vector2.Left);

			_rushDirection = FindClearDirection(preferredDirection);

			float speed = Enemy.Speed;
			if (_config != null)
				speed *= _config.BurstSpeedMultiplier;

			Enemy.Velocity = _rushDirection * speed;

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
	}
}
