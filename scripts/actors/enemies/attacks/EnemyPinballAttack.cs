using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// 弹球攻击。敌人自身化为弹球，朝玩家方向高速冲刺。
	/// 接触碰撞体后按入射角反射，每次命中造成伤害，持续时间结束后收尾。
	/// </summary>
	public partial class EnemyPinballAttack : EnemyAttackTemplate
	{
		[ExportCategory("Pinball")]
		[Export] public NodePath PinballAreaPath = new();

		[Export(PropertyHint.Range, "0,3000,10")]
		public float PinballMaxSpeed = 800f;

		[Export(PropertyHint.Range, "0,3000,10")]
		public float PinballMinSpeed = 200f;

		[Export(PropertyHint.Range, "0,3000,10")]
		public float SpeedDecayPerSecond = 300f;

		[Export(PropertyHint.Range, "0.5,60,0.1")]
		public float PinballDuration = 3.0f;

		[Export(PropertyHint.Range, "0.05,0.5,0.01")]
		public float BounceCooldown = 0.1f;

		[Export(PropertyHint.Range, "0,5,0.01")]
		public float MinBounceTimeBeforeDamage = 0f;

		[Export]
		public bool AllowWallBounce = true;

		private bool _isDashing;
		private Vector2 _dashDirection = Vector2.Right;
		private float _currentSpeed;
		private int _bounceCount;
		private float _bounceCooldownRemaining;
		private bool _canDealDamage;
		private float _dashTimeElapsed;
		private Area2D? _pinballArea;

		protected override void OnInitialized()
		{
			base.OnInitialized();
			SetPhysicsProcess(true);
		}

		protected override void OnAttackStarted()
		{
			base.OnAttackStarted();
			_isDashing = false;
			_bounceCount = 0;
			_bounceCooldownRemaining = 0f;
			_canDealDamage = MinBounceTimeBeforeDamage <= 0f;
			_dashTimeElapsed = 0f;

			ResolvePinballArea();
			ConnectAttackAreaSignals();
			GameActor.AnyDamageTaken += OnAnyDamageTaken;
		}

		protected override void OnWarmupStarted()
		{
			base.OnWarmupStarted();
			if (Enemy != null)
				Enemy.Velocity = Vector2.Zero;
		}

		protected override void OnActivePhase()
		{
			if (Enemy == null) return;

			Vector2 toPlayer = Player != null
				? (Player.GlobalPosition - Enemy.GlobalPosition).Normalized()
				: (Enemy.FacingRight ? Vector2.Right : Vector2.Left);
			if (toPlayer == Vector2.Zero)
				toPlayer = Enemy.FacingRight ? Vector2.Right : Vector2.Left;

			_dashDirection = toPlayer;
			_currentSpeed = PinballMaxSpeed;
			Enemy.Velocity = _dashDirection * _currentSpeed;
			_isDashing = true;

			ProcessInitialOverlaps();
		}

		private void ProcessInitialOverlaps()
		{
			if (_pinballArea == null || Enemy == null) return;

			foreach (var body in _pinballArea.GetOverlappingBodies())
				OnAttackAreaBodyEntered(body);

			foreach (var area in _pinballArea.GetOverlappingAreas())
				OnAttackAreaAreaEntered(area);
		}

		protected override void OnRecoveryStarted()
		{
			base.OnRecoveryStarted();
			_isDashing = false;
			if (Enemy != null)
				Enemy.Velocity = Vector2.Zero;
		}

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			GameActor.AnyDamageTaken -= OnAnyDamageTaken;
			DisconnectAttackAreaSignals();
		}

		public override void _PhysicsProcess(double delta)
		{
			if (Enemy == null || Enemy.IsDead || Enemy.IsDeathSequenceActive) return;

			var stateName = Enemy.StateMachine?.CurrentState?.Name;
			if (stateName == "Frozen" || stateName == "CooldownFrozen"
				|| stateName == "Hit" || stateName == "Dying" || stateName == "Dead")
				return;

			if (!_isDashing) return;

			float dt = (float)delta;
			_bounceCooldownRemaining -= dt;
			_dashTimeElapsed += dt;

			if (!_canDealDamage && _dashTimeElapsed >= MinBounceTimeBeforeDamage)
				_canDealDamage = true;

			if (_dashTimeElapsed >= PinballDuration)
			{
				FinishDash();
				return;
			}

			_currentSpeed = Mathf.Max(PinballMinSpeed, _currentSpeed - SpeedDecayPerSecond * dt);

			if (AllowWallBounce && _bounceCooldownRemaining <= 0f)
			{
				var collision = Enemy.GetLastSlideCollision();
				if (collision != null)
				{
					var collider = collision.GetCollider();
					if (collider is not GameActor)
						Reflect(collision.GetNormal());
				}
			}

			Enemy.Velocity = _dashDirection * _currentSpeed;
		}

		protected override bool ShouldHoldActivePhase() => _isDashing;

		private void OnAttackAreaBodyEntered(Node body)
		{
			if (!_isDashing || Enemy == null) return;

			if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, Enemy)) return;

			// GameActor 必须命中 HitArea 才弹射；非 GameActor（墙壁等）不受此限
			if (body is GameActor actor && _pinballArea != null && !actor.IsHitByArea(_pinballArea))
				return;

			Vector2 enemyPos = Enemy.GlobalPosition;
			Vector2 hitPos = body is Node2D nd ? nd.GlobalPosition : enemyPos;
			Vector2 normal = (enemyPos - hitPos).Normalized();
			if (normal == Vector2.Zero) normal = -_dashDirection;

			if (_bounceCooldownRemaining <= 0f)
				Reflect(normal);

			if (!_canDealDamage) return;

			bool dealt = DamageDispatcher.DealDamage(body, GetDamage(), enemyPos, Enemy,
				DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _pinballArea);
			if (!dealt) return;

			TryApplyKnockback(body);
		}

		private void OnAttackAreaAreaEntered(Area2D area)
		{
			if (!_isDashing || Enemy == null) return;

			var target = area.Owner ?? area;
			if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, Enemy)) return;

			// 只有命中的区域是目标的 HitArea 才弹射（跳过 GrabArea 等非受击区域）
			if (target is GameActor actor && _pinballArea != null && !actor.IsHitByArea(_pinballArea))
				return;

			Vector2 enemyPos = Enemy.GlobalPosition;
			Vector2 normal = (enemyPos - area.GlobalPosition).Normalized();
			if (normal == Vector2.Zero) normal = -_dashDirection;

			if (_bounceCooldownRemaining <= 0f)
				Reflect(normal);

			if (!_canDealDamage) return;

			bool dealt = DamageDispatcher.DealDamage(target, GetDamage(), enemyPos, Enemy,
				DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _pinballArea);
			if (!dealt) return;

			TryApplyKnockback(area.Owner);
		}

		private void OnAnyDamageTaken(GameActor victim, GameActor? attacker, int damage)
		{
			if (victim != Enemy || !_isDashing) return;
			_currentSpeed = PinballMaxSpeed;
		}

		private void Reflect(Vector2 normal)
		{
			_bounceCount++;
			_bounceCooldownRemaining = BounceCooldown;

			_dashDirection = _dashDirection - 2 * _dashDirection.Dot(normal) * normal;
			if (_dashDirection == Vector2.Zero)
				_dashDirection = -normal;

			_currentSpeed = PinballMaxSpeed;
			if (Enemy != null)
				Enemy.Velocity = _dashDirection * _currentSpeed;
		}

		private void TryApplyKnockback(Node? target)
		{
			if (Enemy == null || target is not GameActor actor) return;

			float knockSpeed = KnockbackSpeed > 0f
				? KnockbackSpeed
				: (KnockbackDistance > 0f
					? KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f)
					: 0f);

			if (knockSpeed > 0f)
				actor.Velocity = (actor.GlobalPosition - Enemy.GlobalPosition).Normalized() * knockSpeed;
		}

		private void FinishDash()
		{
			if (Enemy == null) return;
			_isDashing = false;
			Enemy.Velocity = Vector2.Zero;
			ForceEnterRecoveryPhase();
		}

		private void ResolvePinballArea()
		{
			_pinballArea = null;
			if (PinballAreaPath.IsEmpty) return;

			_pinballArea = GetNodeOrNull<Area2D>(PinballAreaPath)
				?? Enemy?.GetNodeOrNull<Area2D>(PinballAreaPath);
		}

		private void ConnectAttackAreaSignals()
		{
			if (_pinballArea == null) return;
			_pinballArea.BodyEntered += OnAttackAreaBodyEntered;
			_pinballArea.AreaEntered += OnAttackAreaAreaEntered;
		}

		private void DisconnectAttackAreaSignals()
		{
			if (_pinballArea == null) return;
			_pinballArea.BodyEntered -= OnAttackAreaBodyEntered;
			_pinballArea.AreaEntered -= OnAttackAreaAreaEntered;
		}

		public override void _ExitTree()
		{
			GameActor.AnyDamageTaken -= OnAnyDamageTaken;
			DisconnectAttackAreaSignals();
			base._ExitTree();
		}
	}
}
