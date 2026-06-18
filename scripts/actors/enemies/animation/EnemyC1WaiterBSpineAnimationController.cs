using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
	/// <summary>
	/// Enemy_C1_WaiterB 专用 Spine 动画控制器，将动画与状态机/攻击模板绑定。
	/// </summary>
	public partial class EnemyC1WaiterBSpineAnimationController : EnemySpineAnimationController
	{
		[Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");
		[Export] public string IdleAnimation = "idle";
		[Export] public string WalkAnimation = "walk";
		[Export] public string AttackAnimation = "attack";
		[Export] public string SkillAnimation = "skill_dash";
		[Export] public string Skill2Animation = "slash";
		[Export] public string Skill3Animation = "skill_beam";
		[Export] public string HitAnimation = "hit";
		[Export] public string StunAnimation = "stun";
		[Export] public string DieAnimation = "death";
		[Export(PropertyHint.Range, "0,5,0.01")] public float Skill3LoopStart = 1.63f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float Skill3LoopEnd = 2.13f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float Skill3PartStart = 2.14f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float Skill3PartEnd = 3.33f;
		[Export(PropertyHint.Range, "0.1,3,0.1")] public float KeepDistanceTimeScale = 1f;
		[Export(PropertyHint.Range, "0.1,3,0.1")] public float SlashTimeScale = 2f;
		private EnemyC1WaiterBAttackController? _attackController;
		private EnemyUltimateBeamAttack? _ultimateBeamAttack;
		private StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
		private Node? _spineControllerNode;
		private Callable _spineHitCallable;
		private bool _spineHitSubscribed;
		// 场景挂载的 AfterimageController 节点，找不到时残影功能自动跳过
		private Node? _afterimage;
		private bool _afterimageActive;

		public override void _Ready()
		{
			if (string.IsNullOrEmpty(DefaultLoopAnimation))
			{
				DefaultLoopAnimation = IdleAnimation;
			}

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
			// 查找 AfterimageController 子节点，不存在则为 null，后续调用自动跳过
			_afterimage = Enemy?.GetNodeOrNull<Node>("AfterimageController");
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
			// 每帧同步残影开关，确保状态切换时及时启停
			UpdateAfterimage();
		}

		/// <summary>
		/// 逐帧同步残影开关：仅在状态切换边缘调用 start/stop，避免每帧重复触发。
		/// </summary>
		private void UpdateAfterimage()
		{
			if (_afterimage == null || Enemy?.StateMachine?.CurrentState == null)
				return;

			bool needsAfterimage = NeedsAfterimage();
			if (needsAfterimage && !_afterimageActive)
			{
				_afterimage.Call("start");
				_afterimageActive = true;
			}
			else if (!needsAfterimage && _afterimageActive)
			{
				_afterimage.Call("stop");
				_afterimageActive = false;
			}
		}

		/// <summary>
		/// 需要残影的状态：KeepDistance 后撤、DashSlash 冲刺阶段。
		/// </summary>
		private bool NeedsAfterimage()
		{
			string stateName = Enemy!.StateMachine!.CurrentState!.Name;

			// KeepDistance 状态全程播放残影
			if (stateName == "KeepDistance")
				return true;

			if (stateName == "Attack")
			{
				var controller = ResolveAttackController();
				if (controller == null)
					return false;

				// DashSlash 攻击：仅冲刺阶段（dash 未结束）播放残影，slash 收招阶段停止
				string attackName = controller.CurrentAttackName;
				if (attackName.Equals(controller.Skill1AttackName, _comparison))
				{
					var skill1Attack = _attackController?.GetNodeOrNull<EnemyDashSlashAttack>(controller.Skill1AttackName);
					return skill1Attack != null && !skill1Attack.IsDashFinished;
				}
			}

			return false;
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
				case "Frozen":
					PlayLoopIfNeeded("Frozen", StunAnimation, HitMixDuration);
					break;
				case "Dying":
					PlayOnceIfNeeded("Die", DieAnimation, DieMixDuration);
					break;
				case "KeepDistance":
					PlayLoopIfNeeded("KeepDistance", SkillAnimation, SkillMixDuration, KeepDistanceTimeScale);
					break;
				case "Dead":
					PlayEmptyIfNeeded();
					break;
				case "Attack":
					HandleAttackAnimations();
					break;
				case "CooldownFrozen":
					// 刺击攻击无冻结逻辑，直接播放 idle
					PlayIdle();
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
					// 用 AttackRunId 作为 key，保证连续两次 melee 能各自独立重播动画
					PlayOnceIfNeeded($"Attack_{controller.AttackRunId}", AttackAnimation, AttackMixDuration);
					return;
				}

				if (attackName.Equals(controller.Skill1AttackName, _comparison))
				{
					// 从 AttackController 节点下按名称查找 DashSlashAttack
					var skill1Attack = _attackController?.GetNodeOrNull<EnemyDashSlashAttack>(controller.Skill1AttackName);

					if (skill1Attack == null || !skill1Attack.IsDashFinished)
					{
						// 正在冲刺中，循环播放冲刺动画
						PlayLoopIfNeeded("skill_dash", SkillAnimation, SkillMixDuration);
						return;
					}

					// 冲刺完成，播放 slash 收招动画
					PlayOnceIfNeeded("skill_slash", Skill2Animation, SkillMixDuration, timeScale: SlashTimeScale);
					return;
				}

				if (attackName.Equals(controller.UltimateAttackName, _comparison))
				{
					var ultimateAttack =  ResolveUltimateBeamAttack(controller);

					if (ultimateAttack != null && !ultimateAttack.IsBeamFinished)
					{
						// 光束攻击期间循环播放 skill_beam 动画
						PlayPartLoopIfNeeded("skill_beam", Skill3Animation, Skill3LoopStart, Skill3LoopEnd, SkillMixDuration);
						return;
					}
					if (ultimateAttack != null && ultimateAttack.IsBeamFinished)
					{
						// 光束攻击结束收尾播放 skill_beam 动画
						PlayPartOnceIfNeeded("skill_beam_PartOnce", Skill3Animation, Skill3PartStart, Skill3PartEnd, SkillMixDuration);
						return;
					}
				}

			}

			PlayIdle();
		}

		private void PlayIdle()
		{
			PlayLoopIfNeeded("Idle", IdleAnimation, IdleMixDuration);
		}







		private EnemyC1WaiterBAttackController? ResolveAttackController()
		{
			if (_attackController != null && IsInstanceValid(_attackController))
			{
				return _attackController;
			}

			if (AttackControllerPath.IsEmpty || Enemy == null)
			{
				return null;
			}

			_attackController = GetNodeOrNull<EnemyC1WaiterBAttackController>(AttackControllerPath);
			if (_attackController == null)
			{
				_attackController = Enemy.GetNodeOrNull<EnemyC1WaiterBAttackController>(AttackControllerPath);
			}

			return _attackController;
		}
		private EnemyUltimateBeamAttack? ResolveUltimateBeamAttack(EnemyC1WaiterBAttackController controller)
		{
			if (_ultimateBeamAttack != null && IsInstanceValid(_ultimateBeamAttack))
			{
				return _ultimateBeamAttack;
			}

			_ultimateBeamAttack = controller.GetNodeOrNull<EnemyUltimateBeamAttack>(controller.UltimateAttackName);
			return _ultimateBeamAttack;
		}

		private void EnsureSpineHitSupport()
		{
			if (_spineHitSubscribed)
			{
				return;
			}

			if (SpineSpritePath.IsEmpty)
			{
				return;
			}

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
			{
				_spineControllerNode.Disconnect("hit_received", _spineHitCallable);
			}

			_spineHitSubscribed = false;
			_spineControllerNode = null;
		}

		private void OnSpineHitReceived(int hitStep, string animationName)
		{
			if (Enemy?.StateMachine?.CurrentState?.Name != "Attack")
			{
				return;
			}

			var controller = ResolveAttackController();
			if (controller == null || string.IsNullOrEmpty(controller.CurrentAttackName))
			{
				return;
			}

			EnemyAttackTemplate? currentAttack = controller.GetNodeOrNull<EnemyAttackTemplate>(controller.CurrentAttackName);
			if (currentAttack == null || !currentAttack.IsRunning)
			{
				return;
			}

			if (!IsExpectedHitAnimation(controller, animationName))
			{
				return;
			}

			if (currentAttack is EnemySimpleMeleeAttack simpleMelee && simpleMelee.RequireAnimationHitTrigger)
			{
				currentAttack.TriggerAnimationHit();
				return;
			}

			currentAttack.TriggerAnimationHit();
		}

		private bool IsExpectedHitAnimation(EnemyC1WaiterBAttackController controller, string animationName)
		{
			if (controller.CurrentAttackName.Equals(controller.MeleeAttackName, _comparison))
			{
				return MatchesAnimationName(animationName, AttackAnimation);
			}

			if (controller.CurrentAttackName.Equals(controller.Skill1AttackName, _comparison))
			{
				// 只有 slash 收招动画的 hit 帧才触发伤害；skill_dash 冲刺动画不触发，防止距离外命中
				return MatchesAnimationName(animationName, Skill2Animation);
			}

			if (controller.CurrentAttackName.Equals(controller.UltimateAttackName, _comparison))
			{
				// UltimateBeamAttack 不使用动画 hit 帧触发，始终放行
				return true;
			}

			return true;
		}

		private bool MatchesAnimationName(string animationName, string expectedAnimation)
		{
			if (string.IsNullOrEmpty(expectedAnimation))
			{
				return false;
			}

			if (string.Equals(animationName, expectedAnimation, _comparison))
			{
				return true;
			}

			return animationName.Contains(expectedAnimation, _comparison)
				|| expectedAnimation.Contains(animationName, _comparison);
		}

	}
}
