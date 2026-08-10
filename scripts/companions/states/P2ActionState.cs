using Godot;

namespace Kuros.Companions.States
{
    /// <summary>P2 动作状态（执行决策动作：治疗/护盾等）：播 action 动画，播完后回 Idle。</summary>
    public partial class P2ActionState : P2State
    {
        /// <summary>action 动画持续时长（秒），到时回 Idle。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float ActionDuration { get; set; } = 1.0f;

        private float _elapsed;

        public override void Enter()
        {
            _elapsed = 0f;
            PlayAnimation("action", loop: false);
        }

        public override void PhysicsUpdate(double delta)
        {
            _elapsed += (float)delta;
            if (_elapsed >= ActionDuration)
                ChangeState("Idle");
        }
    }
}
