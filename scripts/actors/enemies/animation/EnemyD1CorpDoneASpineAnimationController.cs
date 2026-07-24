using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
	public partial class EnemyD1CorpDoneASpineAnimationController : EnemySpineAnimationController
	{
		[Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");
		[Export] public string IdleAnimation = "idle";
		[Export] public string WalkAnimation = "walk";
		[Export] public string AttackAnimation = "attack";
		[Export] public string SkillAnimation = "skill";
		[Export] public string Skill2Animation = "skill2";
		[Export] public string HitAnimation = "hit";
		[Export] public string StunAnimation = "stun";
		[Export] public string DieAnimation = "death";
        [Export(PropertyHint.Range, "0,5,0.01")] public float AttackLoopStart = 0.5f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float AttackLoopEnd = 2.7f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float AttackPartStart = 2.71f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float AttackPartEnd = 3.2f;
		private EnemyD1CorpDoneAAttackController? _attackController;
		private StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
		private Node? _spineControllerNode;
		private Callable _spineHitCallable;
		private bool _spineHitSubscribed;

		public override void _Ready()
		{
			if (string.IsNullOrEmpty(DefaultLoopAnimation))
				DefaultLoopAnimation = IdleAnimation;
			base._Ready();
		}

		public override void _ExitTree()
		{
			UnsubscribeSpineHitSignal();
			base._ExitTree();
		}

		protected override void OnControllerReady()
		{
			base.OnControllerReady();
			ResolveAttackController();
			EnsureSpineHitSupport();
		}

		protected override float GetPreferredMixDuration()
		{
			return AttackMixDuration;
		}

		public override void _Process(double delta)
		{
			base._Process(delta);
			UpdateAnimation();
			TickPartialLoop();
		}

		private void UpdateAnimation()
		{
			if (Enemy?.StateMachine?.CurrentState == null)
			{
				PlayIdle();
				return;
			}

			string stateName = Enemy.StateMachine.CurrentState.Name;
			switch (stateName)
			{
				case "Walk":
					PlayLoopIfNeeded("Walk", WalkAnimation, WalkMixDuration);
					break;
				case "Hit":
					PlayOnceIfNeeded("Hit", HitAnimation, HitMixDuration);
					break;
				case "Dying":
					PlayOnceIfNeeded("Die", DieAnimation, DieMixDuration);
					break;
				case "Frozen":
					PlayLoopIfNeeded("Frozen", StunAnimation, HitMixDuration);
					break;
				case "Dead":
					PlayEmptyIfNeeded();
					break;
				case "Attack":
					HandleAttackAnimations();
					break;
				default:
					PlayIdle();
					break;
			}
		}

		private void HandleAttackAnimations()
		{
			var controller = ResolveAttackController();
			if (controller == null)
			{
				PlayIdle();
				return;
			}

			string attackName = controller.CurrentAttackName;
			if (!string.IsNullOrEmpty(attackName))
			{
				if (attackName.Equals(controller.MeleeAttackName, _comparison))
				{
					PlayOnceIfNeeded("Skill2", Skill2Animation, AttackMixDuration);
					return;
				}

				if (attackName.Equals(controller.PinballAttackName, _comparison))
				{
					if (controller.GetNodeOrNull<EnemyPinballAttack>(controller.PinballAttackName) is { } pinball && pinball.IsStopping)
						PlayPartOnceIfNeeded("AttackPart", AttackAnimation, AttackPartStart, AttackPartEnd, SkillMixDuration);
					else
						PlayPartLoopIfNeeded("Skill", AttackAnimation, AttackLoopStart, AttackLoopEnd, SkillMixDuration);
					return;
				}

				if (attackName.Equals(controller.LockdownAttackName, _comparison))
				{
					PlayOnceIfNeeded("Skill", SkillAnimation, SkillMixDuration);
					return;
				}
			}

			PlayIdle();
		}

		private void PlayIdle()
		{
			PlayLoopIfNeeded("Idle", IdleAnimation, IdleMixDuration);
		}

		private EnemyD1CorpDoneAAttackController? ResolveAttackController()
		{
			if (_attackController != null && IsInstanceValid(_attackController))
				return _attackController;

			if (AttackControllerPath.IsEmpty || Enemy == null)
				return null;

			_attackController = GetNodeOrNull<EnemyD1CorpDoneAAttackController>(AttackControllerPath);
			if (_attackController == null)
				_attackController = Enemy.GetNodeOrNull<EnemyD1CorpDoneAAttackController>(AttackControllerPath);

			return _attackController;
		}

		private void EnsureSpineHitSupport()
		{
			if (_spineHitSubscribed) return;
			if (SpineSpritePath.IsEmpty) return;

			_spineControllerNode = GetNodeOrNull(SpineSpritePath) ?? Enemy?.GetNodeOrNull(SpineSpritePath);
			if (_spineControllerNode == null || !_spineControllerNode.HasSignal("hit_received"))
			{
				_spineControllerNode = null;
				return;
			}

			_spineHitCallable = Callable.From<int, string>(OnSpineHitReceived);
			_spineControllerNode.Connect("hit_received", _spineHitCallable);
			_spineHitSubscribed = true;
		}

		private void UnsubscribeSpineHitSignal()
		{
			if (!_spineHitSubscribed || _spineControllerNode == null)
			{
				_spineHitSubscribed = false;
				_spineControllerNode = null;
				return;
			}

			if (_spineControllerNode.IsConnected("hit_received", _spineHitCallable))
				_spineControllerNode.Disconnect("hit_received", _spineHitCallable);

			_spineHitSubscribed = false;
			_spineControllerNode = null;
		}

		private void OnSpineHitReceived(int hitStep, string animationName)
		{
			if (Enemy?.StateMachine?.CurrentState?.Name != "Attack") return;

			var controller = ResolveAttackController();
			if (controller == null || string.IsNullOrEmpty(controller.CurrentAttackName)) return;

			EnemyAttackTemplate? currentAttack = controller.GetNodeOrNull<EnemyAttackTemplate>(controller.CurrentAttackName);
			if (currentAttack == null || !currentAttack.IsRunning) return;

			if (!IsExpectedHitAnimation(controller, animationName)) return;

			if (currentAttack is EnemySimpleMeleeAttack simpleMelee && simpleMelee.RequireAnimationHitTrigger)
				currentAttack.TriggerAnimationHit();
			else
				currentAttack.TriggerAnimationHit();
		}

		private bool IsExpectedHitAnimation(EnemyD1CorpDoneAAttackController controller, string animationName)
		{
			string expectedAnimation = string.Empty;
			if (controller.CurrentAttackName.Equals(controller.MeleeAttackName, _comparison))
				expectedAnimation = Skill2Animation;   // Melee 播放的是 skill2
			else if (controller.CurrentAttackName.Equals(controller.PinballAttackName, _comparison))
				expectedAnimation = AttackAnimation;   // Pinball 播放的是 attack

			if (string.IsNullOrEmpty(expectedAnimation))
				return true;

			return string.Equals(animationName, expectedAnimation, _comparison);
		}
	}
}
