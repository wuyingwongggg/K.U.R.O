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
			if (Enemy.HasMeta("__close_in_active")) return;

		string currentState = Enemy.StateMachine.CurrentState?.Name ?? string.Empty;
		if (IsBlocked(currentState))
		{
			Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * (float)delta);
			Enemy.MoveAndSlide();
			return;
		}

		if (Enemy.IsPlayerWithinDetectionRange())
		{
			if (!Enemy.IsPlayerInAttackRange())
			{
				EnsureState(WalkStateName, currentState);
				Vector2 direction = GetMoveDirection();
				Vector2 desiredVelocity = direction * Enemy.Speed;

				if (NavAgent != null && NavAgent.AvoidanceEnabled)
				{
					NavAgent.SetVelocity(desiredVelocity);
					if (_hasSafeVelocity && _safeVelocity.LengthSquared() > 0.01f)
						Enemy.Velocity = _safeVelocity.Normalized() * Enemy.Speed;
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
