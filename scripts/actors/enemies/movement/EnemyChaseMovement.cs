using Godot;
using Godot.Collections;
using Kuros.Core.Effects;

public partial class EnemyChaseMovement : Node
{
	private const string MovementMetaKey = "__movement_component_registered";

	[Export] public string IdleStateName = "Idle";
	[Export] public string WalkStateName = "Walk";
	private static readonly StringName AttackStateName = new("Attack");
	private static readonly StringName HitStateName = new("Hit");
	private static readonly StringName FrozenStateName = new("Frozen");
	private static readonly StringName CooldownStateName = new("CooldownFrozen");
	private static readonly StringName DyingStateName = new("Dying");
	private static readonly StringName DeadStateName = new("Dead");

	[Export] public Array<StringName> BlockedStates { get; set; } = new Array<StringName>
	{
		AttackStateName,
		HitStateName,
		FrozenStateName,
		CooldownStateName,
		DyingStateName,
		DeadStateName
	};

	protected SampleEnemy? Enemy;

	protected NavigationAgent2D? NavAgent;

	private Vector2 _safeVelocity = Vector2.Zero;
	private bool _hasSafeVelocity = false;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;

		Enemy = GetParent<SampleEnemy>();
		if (Enemy == null)
		{
			GD.PushWarning($"{Name}: EnemyChaseMovement must be a child of SampleEnemy.");
			QueueFree();
			return;
		}

		if (Enemy.HasMeta(MovementMetaKey))
		{
			GD.PushWarning($"{Name}: {Enemy.Name} already has a movement component. Removing duplicate.");
			QueueFree();
			Enemy = null;
			return;
		}

		Enemy.SetMeta(MovementMetaKey, this);

		NavAgent = Enemy.GetNodeOrNull<NavigationAgent2D>("NavigationAgent2D");
		if (NavAgent != null)
		{
			NavAgent.VelocityComputed += OnVelocityComputed;
			NavAgent.PathMaxDistance = 99999f;
		}
	}

	private void OnVelocityComputed(Vector2 safeVelocity)
	{
		_safeVelocity = safeVelocity;
		_hasSafeVelocity = true;
	}

	public override void _ExitTree()
	{
		if (NavAgent != null)
		{
			NavAgent.VelocityComputed -= OnVelocityComputed;
		}

		if (Enemy != null && Enemy.HasMeta(MovementMetaKey))
		{
			var ownerVariant = Enemy.GetMeta(MovementMetaKey);
			if (ownerVariant.VariantType == Variant.Type.Object)
			{
				var owner = ownerVariant.As<Node>();
				if (owner == this)
				{
					Enemy.RemoveMeta(MovementMetaKey);
				}
			}
			else
			{
				Enemy.RemoveMeta(MovementMetaKey);
			}
		}

		base._ExitTree();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint() || Enemy == null) return;
		if (Enemy.StateMachine == null) return;

		if (Enemy.HasMeta("__keep_distance_active")) return;

		// CloseIn 期间导航保持在线:CloseIn 状态只标记加速/计时/到达判定,移动寻路全走本组件
		// (meta 只用于让 chase 在 CloseIn 期间忽略"进入攻击范围即停"并提速,CloseIn 结束自动恢复)
		bool closeInActive = Enemy.HasMeta("__close_in_active");

		string currentState = Enemy.StateMachine.CurrentState?.Name ?? string.Empty;
		if (IsBlocked(currentState))
		{
			Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * (float)delta);
			Enemy.MoveAndSlide();
			return;
		}

		if (Enemy.IsPlayerWithinDetectionRange())
		{
			// CloseIn 期间无视"进入攻击范围即停",一路冲向 GetApproachTarget(贴身点);否则维持原停点规则
			if (!Enemy.IsPlayerInAttackRange() || closeInActive)
			{
				// 不要弹走 CloseIn 状态(chase 只提供移动,状态进出/到达退出由 CloseIn 自己判定)
				if (currentState != "CloseIn")
					EnsureState(WalkStateName, currentState);

				// CloseIn = 加速贴近:移动仍走 GetMoveDirection(导航/approach),仅速度乘 BurstSpeedMultiplier
				float speedMul = closeInActive && Enemy.BehaviorConfig != null
					? Mathf.Max(1f, Enemy.BehaviorConfig.BurstSpeedMultiplier)
					: 1f;

				Vector2 direction = GetMoveDirection();
				Vector2 desiredVelocity = direction * Enemy.Speed * speedMul;

				if (NavAgent != null && NavAgent.AvoidanceEnabled)
				{
					NavAgent.SetVelocity(desiredVelocity);
					if (_hasSafeVelocity && _safeVelocity.LengthSquared() > 0.01f)
						Enemy.Velocity = _safeVelocity.Normalized() * Enemy.Speed * speedMul;
					else
						Enemy.Velocity = desiredVelocity;
					_hasSafeVelocity = false;
				}
				else
				{
					Enemy.Velocity = desiredVelocity;
				}

				if (Mathf.Abs(desiredVelocity.X) > 0.1f)
					Enemy.FlipFacing(desiredVelocity.X > 0);
			}
			else
			{
				Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * 2 * (float)delta);
			}
		}
		else
		{
			EnsureState(IdleStateName, currentState);
			Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * 2 * (float)delta);
		}

		Enemy.MoveAndSlide();
		Enemy.ClampPositionToScreen();
	}

	protected virtual Vector2 GetMoveDirection()
	{
		var player = Enemy?.PlayerTarget;
		bool hasNav = NavAgent != null;

		if (hasNav && Enemy != null && player != null)
		{
			Vector2 approachTarget = Enemy.GetApproachTarget();
			if (NavAgent.TargetPosition.DistanceSquaredTo(approachTarget) > 100f)
				NavAgent.TargetPosition = approachTarget;

			if (!NavAgent.IsNavigationFinished())
			{
				Vector2 nextPoint = NavAgent.GetNextPathPosition();
				Vector2 dir = (nextPoint - Enemy.GlobalPosition).Normalized();
				if (!dir.IsZeroApprox())
					return dir;
			}
		}

		if (Enemy != null)
		{
			Vector2 approachTarget = Enemy.GetApproachTarget();
			Vector2 toTarget = approachTarget - Enemy.GlobalPosition;
			if (!toTarget.IsZeroApprox())
				return toTarget.Normalized();
		}
		return Vector2.Zero;
	}

	private bool IsBlocked(string stateName)
	{
		foreach (var blocked in BlockedStates)
		{
			if (blocked == stateName) return true;
		}
		return false;
	}

	private void EnsureState(string targetState, string currentState)
	{
		if (string.IsNullOrEmpty(targetState)) return;
		if (currentState == targetState) return;
		Enemy?.StateMachine?.ChangeState(targetState);
	}

}
