using Godot;

namespace Kuros.Companions.States
{
    /// <summary>P2 受击状态：播 hit 动画 + 短暂硬直（停止移动），到时回 Idle。
    /// 受击免疫窗口在 P2CompanionController.TakeDamage 中处理。</summary>
    public partial class P2HitState : P2State
    {
        /// <summary>受击硬直时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,2,0.05")] public float HitDuration { get; set; } = 0.4f;

        private float _elapsed;

        public override void Enter()
        {
            _elapsed = 0f;
            P2.StopMoving(); // 硬直期间停止移动
            PlayAnimation("hit", loop: false);
        }

        public override void PhysicsUpdate(double delta)
        {
            _elapsed += (float)delta;
            if (_elapsed >= HitDuration)
                ChangeState("Idle");
        }
    }
}
