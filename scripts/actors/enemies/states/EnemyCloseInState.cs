using Godot;

namespace Kuros.Actors.Enemies.States
{
	public partial class EnemyCloseInState : EnemyState
	{
		[ExportCategory("Obstacle Avoidance")]
		/// <summary>避障前瞻射线长度(px):突进前沿候选方向发射射线探测此距离内是否有障碍
		/// (墙/家具等),通畅才走该方向;被挡则依次尝试 ±45°/±90°/180° 找可通行方向。</summary>
		[Export(PropertyHint.Range, "10,500,10")]
		public float RaycastDistance = 40f;

		/// <summary>到达目标范围(px)即收招退出 CloseIn 并进入 burst 冷却——不再滞留目标点,
		/// 贴点滞留期的朝向/翻转/抖动问题从根上消除;接近受阻时仍由 BurstDuration 超时兜底退出。</summary>
		[Export(PropertyHint.Range, "8,200,4")]
		public float ArriveRangeX = 40f;

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
			// 导航陈旧路径问题已随"CloseIn 期间 chase 保持在线"重构根除,无需再重置 NavAgent
		}

		public override void PhysicsUpdate(double delta)
		{
			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy))
				return;

			if (Player == null) return;

			// 计时结束退出(超时兜底:受阻/绕路到不了目标)
			_timer -= (float)delta;
			if (_timer <= 0f)
			{
				ChangeState("Idle");
				return;
			}

			Vector2 target = Enemy.GetApproachTarget();
			Vector2 toTarget = target - Enemy.GlobalPosition;

			// 到达目标范围即收招:ChangeState(Idle) 的 Exit 会设置 burst 冷却(CloseInCooldownRemaining),
			// 冷却期内 Idle/Walk 不会再次触发 CloseIn——先退出站位,不留滞目标点
			if (toTarget.Length() <= ArriveRangeX)
			{
				ChangeState("Idle");
				return;
			}

			// 有移动组件(chase/导航):移动与朝向全由 EnemyChaseMovement 驱动——
			// 本状态只保留 __close_in_active 加速标记与上面的到达/超时判定(CloseIn = 加速贴近)
			if (Enemy.HasMeta("__movement_component_registered"))
				return;

			// 无移动组件回退:原自移逻辑(直线朝目标 + 自研避障)
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
			// 排除玩家(与自身)身体:贴脸突进时玩家不是障碍——否则主方向被打中判定不通,
			// 替代方向选成 180°(背向玩家),敌人会整段 BurstDuration 朝远离玩家方向冲刺
			var exclude = new Godot.Collections.Array<Rid>();
			var player = Enemy.PlayerTarget;
			if (player != null && GodotObject.IsInstanceValid(player))
				exclude.Add(player.GetRid());
			if (Enemy.GetRid() is Rid selfRid && selfRid.IsValid)
				exclude.Add(selfRid);
			if (exclude.Count > 0)
				query.Exclude = exclude;

			var result = Enemy.GetWorld2D().DirectSpaceState.IntersectRay(query);
			return result.Count == 0;
		}
	}
}
