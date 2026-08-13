using Godot;

namespace Kuros.Companions.States
{
    /// <summary>P2 站立状态：播 walk 循环（无 idle 动画，用户确认用 walk 代替）。</summary>
    public partial class P2IdleState : P2State
    {
        public override void Enter()
        {
            PlayAnimation("walk", loop: true);
        }
    }
}
