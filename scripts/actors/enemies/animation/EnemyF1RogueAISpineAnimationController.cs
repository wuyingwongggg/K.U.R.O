using Godot;

namespace Kuros.Actors.Enemies.Animation
{
	/// <summary>
	/// rogueAI 本体的 Spine 动画控制器。
	/// 骨架里只有 `idle_up / walk_up / walk_down / hit_down / death_down / attack_*`（没有 idle / walk / stun），
	/// 所以只点播确实存在的动画名，其余状态保持当前姿势——原 drone 控制器会每帧重试不存在的动画名并刷报错。
	/// 常态 Idle 与 Walk **都用 `idle_up`**（本体是悬浮滑行的机械，没有走路循环）；
	/// 两者 key 都取动画名本身，Idle ⇄ Walk 切换不会把循环重启。
	/// </summary>
	public partial class EnemyF1RogueAISpineAnimationController : EnemySpineAnimationController
	{
		/// <summary>常态循环动画名（Idle/Walk 共用）。</summary>
		[Export] public string IdleAnimation { get; set; } = "idle_up";
		/// <summary>受击动画名（受击段/回正段由 EnemyHitState 的双时间轴驱动）。</summary>
		[Export] public string HitAnimation { get; set; } = "hit_down";
		/// <summary>死亡动画名。</summary>
		[Export] public string DieAnimation { get; set; } = "death_down";
		/// <summary>攻击动画槽位：攻击未实现，留空 = 不切换。（以后有 attack_up / attack_down 两套时再扩。）</summary>
		[Export] public string AttackAnimation { get; set; } = string.Empty;

		public override void _Ready()
		{
			// 基类 OnControllerReady 会播放 DefaultLoopAnimation，必须先赋值
			if (string.IsNullOrEmpty(DefaultLoopAnimation))
				DefaultLoopAnimation = IdleAnimation;
			base._Ready();
		}

		protected override float GetPreferredMixDuration() => IdleMixDuration;

		public override void _Process(double delta)
		{
			base._Process(delta);
			UpdateAnimation();
		}

		private void UpdateAnimation()
		{
			if (Enemy?.StateMachine?.CurrentState == null)
			{
				PlayIdle();
				return;
			}

			switch (Enemy.StateMachine.CurrentState.Name)
			{
				case "Walk":            // 滑行：与 Idle 同一支循环，靠 key 去重不重启
					PlayIdle();
					break;
				case "Hit":
					DriveHitPhaseAnimation(HitAnimation, HitMixDuration);
					break;
				case "Dying":
					PlayOnceIfNeeded("Die", DieAnimation, DieMixDuration);
					break;
				case "Dead":
					PlayEmptyIfNeeded();
					break;
				case "Attack":          // 攻击未接入：配了动画名才播
					PlayAttackIfConfigured();
					break;
				case "Frozen":          // 骨架没有眩晕动画：保持当前姿势，不切换
					break;
				default:
					PlayIdle();
					break;
			}
		}

		private void PlayAttackIfConfigured()
		{
			if (string.IsNullOrEmpty(AttackAnimation)) return;
			PlayOnceIfNeeded("Attack", AttackAnimation, AttackMixDuration);
		}

		private void PlayIdle() => PlayLoopIfNeeded(IdleAnimation, IdleAnimation, IdleMixDuration);
	}
}
