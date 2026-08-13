using Godot;

namespace Kuros.Companions.States
{
    /// <summary>P2 移动状态：跟随模式播 move（跑），自由模式播 walk（走）。
    /// 位移由 P2CompanionController 驱动，本状态只负责动画；模式切换时每帧检测刷新。</summary>
    public partial class P2WalkState : P2State
    {
        private string _currentAnimation = string.Empty;

        public override void Enter()
        {
            _currentAnimation = string.Empty; // 强制重新评估
            RefreshAnimation();
        }

        public override void PhysicsUpdate(double delta)
        {
            // 模式切换不重进本状态（同状态 ChangeState 直接返回），这里每帧跟随模式刷新动画
            RefreshAnimation();
        }

        private void RefreshAnimation()
        {
            string anim = P2.IsFollowingMode ? "move" : "walk";
            if (anim == _currentAnimation) return; // 同名不重播（SpineController.play 会重启动画）
            _currentAnimation = anim;
            PlayAnimation(anim, loop: true);
        }
    }
}
