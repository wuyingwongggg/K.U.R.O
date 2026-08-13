using Godot;

namespace Kuros.Companions.States
{
    /// <summary>P2 受控（眩晕）状态：播 stun 动画。本次无触发源，状态就位供未来接入。</summary>
    public partial class P2StunState : P2State
    {
        public override void Enter()
        {
            PlayAnimation("stun", loop: true);
        }
    }
}
