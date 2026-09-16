using Godot;

namespace Kuros.Actors.Enemies.Animation
{
    /// <summary>
    /// 滑槽磁铁机械臂的 Spine 动画控制器。
    /// 实际会用到的只有两个相位状态：`MagnetRetreat`（退场）与 `Attack`（进场携带罐子），
    /// 因此按相位各留一个动画槽位——留空即回退 <see cref="WalkAnimation"/>，
    /// 骨架补了新动画后在 Inspector 填名字即可，不用改代码。
    /// 同时不再触碰骨架里不存在的 hit / stun / death 等动画名（原 drone 控制器的报错刷屏来源）。
    /// </summary>
    public partial class EnemyF1RogueAIMagnetSpineAnimationController : EnemySpineAnimationController
    {
        /// <summary>通用移动动画名（同时是其它相位槽位为空时的回退）。</summary>
        [Export] public string WalkAnimation { get; set; } = "walk";
        /// <summary>退场相位（MagnetRetreat 状态）动画名；空 = 用 <see cref="WalkAnimation"/>。</summary>
        [Export] public string RetreatAnimation { get; set; } = "idle";
        /// <summary>进场携带罐子相位（Attack 状态）动画名；空 = 用 <see cref="WalkAnimation"/>。</summary>
        [Export] public string CarryAnimation { get; set; } = "walk";
        /// <summary>静止/其它状态动画名（留空 = 保持当前姿势，不切换）。</summary>
        [Export] public string IdleAnimation { get; set; } = "idle";

        public override void _Ready()
        {
            // 基类 OnControllerReady 会播放 DefaultLoopAnimation，必须先赋值
            if (string.IsNullOrEmpty(DefaultLoopAnimation))
                DefaultLoopAnimation = WalkAnimation;
            base._Ready();
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            UpdateAnimation();
        }

        private void UpdateAnimation()
        {
            if (Enemy?.StateMachine?.CurrentState == null)
            {
                PlayIdleIfConfigured();
                return;
            }

            switch (Enemy.StateMachine.CurrentState.Name)
            {
                case "MagnetRetreat":   // 步骤 1：沿轨道退到场外端
                    PlayPhaseAnimation(RetreatAnimation, WalkMixDuration);
                    break;
                case "Attack":          // 步骤 2/3：进场携带罐子 → 抵达释放
                    PlayPhaseAnimation(CarryAnimation, AttackMixDuration);
                    break;
                case "Walk":            // 过渡帧（模板结束后到相位导演切状态之间），照常播移动动画
                    PlayPhaseAnimation(WalkAnimation, WalkMixDuration);
                    break;
                default:                // Idle 及其它：不切换，保持当前姿势（配了 IdleAnimation 才切）
                    PlayIdleIfConfigured();
                    break;
            }
        }

        /// <summary>播放某相位的动画；槽位留空则回退通用移动动画。
        /// key 用动画名本身——不同相位若配了同一个动画，不会因为换了 key 而把它重启（走路循环不跳帧）。</summary>
        private void PlayPhaseAnimation(string configured, float mixDuration)
        {
            string animation = !string.IsNullOrEmpty(configured) ? configured : WalkAnimation;
            if (string.IsNullOrEmpty(animation)) return;
            PlayLoopIfNeeded(animation, animation, mixDuration);
        }

        private void PlayIdleIfConfigured()
        {
            if (!string.IsNullOrEmpty(IdleAnimation))
                PlayLoopIfNeeded(IdleAnimation, IdleAnimation, IdleMixDuration);
        }
    }
}
