using Godot;

namespace Kuros.Companions.States
{
    /// <summary>P2 移动状态：播 walk 循环。位移由 P2CompanionController 驱动，本状态只负责动画。</summary>
    public partial class P2WalkState : P2State
    {
        public override void Enter()
        {
            PlayAnimation("walk", loop: true);
        }
    }
}
