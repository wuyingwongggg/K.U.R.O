using Godot;
using Kuros.Systems.FSM;

namespace Kuros.Companions.States
{
    /// <summary>
    /// P2 状态基类（仿 EnemyState）：强转被控制节点为 P2CompanionController。
    /// 移动计算由 P2CompanionController._PhysicsProcess 唯一驱动，状态只做行为/动画层。
    /// </summary>
    public abstract partial class P2State : State
    {
        protected P2CompanionController P2 => (P2CompanionController)Owner;

        /// <summary>播放 P2 的 Spine 动画（主精灵 + outline 同步）。</summary>
        protected void PlayAnimation(string animationName, bool loop = true)
            => P2.PlaySpineAnimation(animationName, loop);
    }
}
