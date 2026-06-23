using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.Attacks
{
	public partial class EnemyDashSlashAttack : EnemyAttackTemplate
	{
		[ExportCategory("Areas")]
		[Export] public NodePath DetectionAreaPath = new NodePath();
		[Export] public NodePath DashStopAreaPath = new NodePath();
		[Export] public NodePath DashSlashAreaPath = new NodePath();

		[ExportCategory("Dash")]
		[Export(PropertyHint.Range, "10,2000,10")] public float DashSpeed = 600f;
		[Export] public bool LockFacingDuringDash = true;
		[Export(PropertyHint.Range, "0,5,0.1")] public float MinDashTimeBeforeAttack = 0f;
		[Export(PropertyHint.Range, "0,10,0.1")] public float DashMaxDuration = 0f;
		[Export] public bool UseNavDuringDash = true;

		[ExportCategory("Dash Curve")]
		[Export(PropertyHint.Range, "50,500,10")] public float DashCurveOffset = 200f;

		[ExportCategory("Slash")]

		private const float PostCooldownDuration = 1.0f;

		private Area2D? _detectionArea;
		private Area2D? _dashStopArea;
		private Area2D? _dashSlashArea;
		private EnemyAttackController? _controller;
		private NavigationAgent2D? _navAgent;
		private bool _playerInsideDetection;

		private Vector2 _dashDirection = Vector2.Right;
		private bool _isDashing;
		private bool _dashFinalized;
		private float _postAttackCooldown;
		private bool _pendingCooldownExit;
		private float _dashTimeElapsed;
		private bool _canAttemptStrike;

		private float _dashCurveT;
		private Vector2 _bezierP0;
		private Vector2 _bezierP1;
		private Vector2 _bezierP2;
		private float _bezierArcLength;
		private bool _useBezierCurve;

		public bool IsDashing => _isDashing;
		public bool IsDashFinished => _dashFinalized;

		protected override void OnInitialized()
		{
			base.OnInitialized();
			_controller = GetParent() as EnemyAttackController;

			_detectionArea = ResolveArea(DetectionAreaPath);
			if (_detectionArea != null)
			{
				_detectionArea.Monitoring = true;
				_detectionArea.BodyEntered += OnDetectionAreaBodyEntered;
				_detectionArea.BodyExited += OnDetectionAreaBodyExited;
			}
			else
			{
				GD.PushWarning($"[EnemyDashSlashAttack] DetectionArea not found for {Enemy?.Name ?? Name}, fallback to DetectionRange.");
			}

			_dashSlashArea = ResolveArea(DashSlashAreaPath);
			if (_dashSlashArea == null)
				_dashSlashArea = AttackArea;

			_dashStopArea = ResolveArea(DashStopAreaPath);
			if (_dashStopArea == null)
				_dashStopArea = _dashSlashArea;

			SetPhysicsProcess(true);
			_navAgent = Enemy?.GetNodeOrNull<NavigationAgent2D>("NavigationAgent2D");
		}

		public override void _ExitTree()
		{
			if (_detectionArea != null)
			{
				var entered = new Callable(this, MethodName.OnDetectionAreaBodyEntered);
				var exited = new Callable(this, MethodName.OnDetectionAreaBodyExited);
				if (_detectionArea.IsConnected(Area2D.SignalName.BodyEntered, entered))
					_detectionArea.BodyEntered -= OnDetectionAreaBodyEntered;
				if (_detectionArea.IsConnected(Area2D.SignalName.BodyExited, exited))
					_detectionArea.BodyExited -= OnDetectionAreaBodyExited;
			}
			base._ExitTree();
		}

		public override bool CanStart()
		{
			if (Enemy == null || Enemy.PlayerTarget == null) return false;
			if (IsRunning || IsOnCooldown) return false;
			if (Enemy.AttackTimer > 0) return false;
			if (_postAttackCooldown > 0f) return false;

			bool detectionSatisfied = _detectionArea != null
				? _playerInsideDetection || _detectionArea.OverlapsBody(Enemy.PlayerTarget)
				: Enemy.IsPlayerWithinDetectionRange();

			if (!detectionSatisfied) return false;

			AlignFacingWithPlayer();

			Vector2 toPlayer = Enemy.GetDirectionToPlayer();
			if (toPlayer == Vector2.Zero) return false;

			Vector2 facing = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
			float angle = Mathf.RadToDeg(facing.AngleTo(toPlayer));
			return Mathf.Abs(angle) <= MaxAllowedAngleToPlayer;
		}

		protected override void OnAttackStarted()
		{
			base.OnAttackStarted();
			_isDashing = false;
			_dashFinalized = false;
			_postAttackCooldown = 0f;
			_pendingCooldownExit = false;
			_dashTimeElapsed = 0f;
			_canAttemptStrike = MinDashTimeBeforeAttack <= 0f;
			_dashCurveT = 0f;
			_useBezierCurve = false;
		}

		protected override void OnWarmupStarted()
		{
			base.OnWarmupStarted();
			if (Enemy == null) return;
			Enemy.Velocity = Vector2.Zero;
			_isDashing = true;

			Vector2 toPlayer = Enemy.PlayerTarget != null
				? (Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition).Normalized()
				: (Enemy.FacingRight ? Vector2.Right : Vector2.Left);
			if (toPlayer == Vector2.Zero) toPlayer = Enemy.FacingRight ? Vector2.Right : Vector2.Left;

			if (MinDashTimeBeforeAttack > 0f && DashCurveOffset > 0f)
			{
				_useBezierCurve = true;
				_bezierP0 = Enemy.GlobalPosition;
				Vector2 awayDir = -toPlayer;
				Vector2 perpDir = new(-awayDir.Y, awayDir.X);
				float curveSign = GD.Randf() > 0.5f ? 1f : -1f;
				float halfDist = DashCurveOffset * 0.5f;
				_bezierP1 = _bezierP0 + awayDir * halfDist + perpDir * DashCurveOffset * curveSign;
				_bezierP2 = _bezierP0 + perpDir * DashCurveOffset * 1.5f * curveSign;
				_bezierArcLength = _bezierP0.DistanceTo(_bezierP1) + _bezierP1.DistanceTo(_bezierP2);
			}

			if (UseNavDuringDash && _navAgent != null && Enemy.PlayerTarget != null && !_useBezierCurve)
				_navAgent.TargetPosition = Enemy.PlayerTarget.GlobalPosition;

			_dashDirection = toPlayer;
			if (LockFacingDuringDash && _dashDirection.X != 0)
				Enemy.FlipFacing(_dashDirection.X > 0);
			Enemy.Velocity = _dashDirection * DashSpeed;
		}

		protected override void OnActivePhase()
		{
			if (Enemy == null) return;
			Enemy.Velocity = Vector2.Zero;
			if (RequireAnimationHitTrigger)
				_animationHitReady = true;
		}

		protected override void OnRecoveryStarted()
		{
			base.OnRecoveryStarted();
			_playerInsideDetection = false;
			_isDashing = false;
			_dashFinalized = true;
			if (Enemy != null)
				Enemy.Velocity = Vector2.Zero;
			if (RequireAnimationHitTrigger)
				_animationHitReady = true;
		}

		public override void _PhysicsProcess(double delta)
		{
			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy) || !Enemy.IsInsideTree() || Enemy.IsDeathSequenceActive || Enemy.IsDead)
				return;

			var stateName = Enemy.StateMachine?.CurrentState?.Name;
			if (stateName == "Frozen" || stateName == "CooldownFrozen"
				|| stateName == "Hit" || stateName == "Dying" || stateName == "Dead"
				|| stateName == "KeepDistance")
				return;

			if (_postAttackCooldown > 0f)
			{
				var currentStateName = Enemy?.StateMachine?.CurrentState?.Name;
				if (currentStateName == "Attack" && !IsRunning)
				{
					_postAttackCooldown = 0f;
					_pendingCooldownExit = false;
					return;
				}
				_postAttackCooldown -= (float)delta;
				if (_postAttackCooldown <= 0f)
				{
					_postAttackCooldown = 0f;
					if (_pendingCooldownExit)
					{
						FinishCooldownState();
						_pendingCooldownExit = false;
					}
				}
				return;
			}

			UpdateDashMovement(delta);
			UpdateDetectionTracking();
		}

		protected override bool ShouldHoldWarmupPhase() => _isDashing;
		protected override bool ShouldHoldRecoveryPhase() => false;

		private void UpdateDashMovement(double delta)
		{
			if (!_isDashing || Enemy == null || Enemy.IsDeathSequenceActive || Enemy.IsDead) return;

			_dashTimeElapsed += (float)delta;

			// 最长冲刺时间到期
			if (DashMaxDuration > 0f && _dashTimeElapsed >= DashMaxDuration)
			{
				FinishDash();
				return;
			}

			if (!_canAttemptStrike && _dashTimeElapsed >= MinDashTimeBeforeAttack)
				_canAttemptStrike = true;

			// 贝塞尔曲线阶段
			if (_useBezierCurve && _dashCurveT < 1f)
			{
				_dashCurveT += (float)delta * DashSpeed / Mathf.Max(_bezierArcLength, 1f);
				float t = Mathf.Clamp(_dashCurveT, 0f, 1f);
				EvaluateBezier(t, out Vector2 bezierDir);

				_dashDirection = bezierDir;
				Enemy.Velocity = _dashDirection * DashSpeed * EaseInOut(t);

				if (LockFacingDuringDash && _dashDirection.X != 0)
					Enemy.FlipFacing(_dashDirection.X > 0);
				return;
			}

			if (_useBezierCurve && _dashCurveT >= 1f)
			{
				_useBezierCurve = false;
				if (UseNavDuringDash && _navAgent != null && Enemy.PlayerTarget != null)
					_navAgent.TargetPosition = Enemy.PlayerTarget.GlobalPosition;
			}

			if (Enemy.PlayerTarget != null)
			{
				Vector2 newDir;

				if (UseNavDuringDash && _navAgent != null)
				{
					_navAgent.TargetPosition = Enemy.PlayerTarget.GlobalPosition;
					if (!_navAgent.IsNavigationFinished())
					{
						Vector2 nextPoint = _navAgent.GetNextPathPosition();
						newDir = (nextPoint - Enemy.GlobalPosition).Normalized();
					}
					else
					{
						newDir = (Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition).Normalized();
					}
					if (newDir.IsZeroApprox())
						newDir = _dashDirection;
				}
				else
				{
					Vector2 toPlayer = Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition;
					newDir = toPlayer != Vector2.Zero ? toPlayer.Normalized() : _dashDirection;
				}

				_dashDirection = newDir;
				if (LockFacingDuringDash && _dashDirection.X != 0)
					Enemy.FlipFacing(_dashDirection.X > 0);

				if (_canAttemptStrike && IsPlayerInsideDashStopArea(Enemy.PlayerTarget))
				{
					FinishDash();
					return;
				}
			}

			Enemy.Velocity = _dashDirection * DashSpeed * ApproachDecel();
		}

		private static float EaseInOut(float t)
		{
			if (t < 0.3f) return Mathf.Lerp(0.3f, 1f, t / 0.3f);
			if (t < 0.7f) return 1f;
			return Mathf.Lerp(1f, 0.4f, (t - 0.7f) / 0.3f);
		}

		private float ApproachDecel()
		{
			if (Enemy?.PlayerTarget == null) return 1f;
			float dist = Enemy.GlobalPosition.DistanceTo(Enemy.PlayerTarget.GlobalPosition);
			return Mathf.Clamp(dist / 150f, 0.4f, 1f);
		}

		private void EvaluateBezier(float t, out Vector2 direction)
		{
			float u = 1f - t;
			direction = (2f * u * (_bezierP1 - _bezierP0) + 2f * t * (_bezierP2 - _bezierP1)).Normalized();
			if (direction.IsZeroApprox())
				direction = _dashDirection;
		}

		private bool IsPlayerInsideDashStopArea(SamplePlayer player)
		{
			if (_dashStopArea != null)
				return player.IsHitByArea(_dashStopArea);
			return player.IsHitByArea(AttackArea);
		}

		private bool IsPlayerInsideDashSlashArea(SamplePlayer player)
		{
			var targetArea = _dashSlashArea ?? AttackArea;
			if (targetArea == null) return true;
			return player.IsHitByArea(targetArea);
		}

		private void UpdateDetectionTracking()
		{
			if (_detectionArea == null || Enemy?.PlayerTarget == null) return;
			if (_postAttackCooldown > 0f) return;

			bool overlaps = _detectionArea.OverlapsBody(Enemy.PlayerTarget);
			if (overlaps)
			{
				_playerInsideDetection = true;
				TryRequestAttackFromDetection("Poll");
				return;
			}
			_playerInsideDetection = false;
		}

		private void TryRequestAttackFromDetection(string reason)
		{
			if (Enemy == null) return;
			if (Enemy.IsDeathSequenceActive || Enemy.IsDead) return;
			if (IsRunning || IsOnCooldown) return;
			if (Enemy.AttackTimer > 0) return;
			if (_postAttackCooldown > 0f) return;

			var currentStateName = Enemy.StateMachine?.CurrentState?.Name;
			if (currentStateName == "Frozen" || currentStateName == "CooldownFrozen"
				|| currentStateName == "Hit" || currentStateName == "Dying" || currentStateName == "Dead")
				return;

			if (_controller != null && _controller.PeekQueuedAttack() != this) return;
			if (_controller != null && !_controller.CanStart()) return;

			if (Enemy.StateMachine?.CurrentState?.Name != "Attack")
				Enemy.StateMachine?.ChangeState("Attack");
		}

		private void FinishDash()
		{
			if (Enemy == null) return;
			_isDashing = false;
			_dashFinalized = true;
			Enemy.Velocity = Vector2.Zero;
			if (RequireAnimationHitTrigger)
				_animationHitReady = true;
		}

		protected override void OnAnimationHit()
		{
			if (Enemy == null || Enemy.IsDead || Enemy.IsDeathSequenceActive) return;
			if (Enemy.PlayerTarget == null) return;
			DamageDispatcher.DealDamageFromArea(_dashSlashArea ?? AttackArea, GetDamage(), Enemy);
			if (!IsPlayerInsideDashSlashArea(Enemy.PlayerTarget)) return;
			ExecuteStrike();
		}

		private void ExecuteStrike()
		{
			if (Enemy == null || Enemy.PlayerTarget == null) return;

			Enemy.PlayerTarget.TakeDamage(GetDamage(), Enemy.GlobalPosition, Enemy);

			float distance = Mathf.Max(0f, KnockbackDistance);
			if (distance > 0f || KnockbackSpeed > 0f)
			{
				TryApplyPlayerKnockback(
					Enemy.PlayerTarget,
					distance,
					Mathf.Max(KnockbackDuration, 0.01f),
					KnockbackSpeed,
					Enemy.FacingRight ? Vector2.Right : Vector2.Left);
			}
		}

		private void StartPostCooldown()
		{
			if (Enemy == null) return;
			_postAttackCooldown = PostCooldownDuration;
			Enemy.AttackTimer = Mathf.Max(Enemy.AttackTimer, PostCooldownDuration);
			Enemy.Velocity = Vector2.Zero;
			_pendingCooldownExit = true;
		}

		private void FinishCooldownState()
		{
			if (Enemy?.StateMachine == null) return;
			if (IsRunning)
				Cancel();
		}

		private void OnDetectionAreaBodyEntered(Node body)
		{
			if (Enemy == null || body != Enemy.PlayerTarget) return;
			_playerInsideDetection = true;
			TryRequestAttackFromDetection("SignalEntered");
		}

		private void OnDetectionAreaBodyExited(Node body)
		{
			if (Enemy == null || body != Enemy.PlayerTarget) return;
			_playerInsideDetection = false;
		}

		private Area2D? ResolveArea(NodePath path)
		{
			if (path.IsEmpty) return null;
			var area = GetNodeOrNull<Area2D>(path);
			if (area != null) return area;
			return Enemy?.GetNodeOrNull<Area2D>(path);
		}

		private void AlignFacingWithPlayer()
		{
			if (Enemy == null) return;
			Vector2 toPlayer = Enemy.GetDirectionToPlayer();
			if (Mathf.Abs(toPlayer.X) > 0.01f)
				Enemy.FlipFacing(toPlayer.X > 0f);
		}

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			_playerInsideDetection = false;
			if (_postAttackCooldown <= 0f)
				StartPostCooldown();
		}
	}
}
