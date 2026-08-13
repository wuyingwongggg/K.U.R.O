using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 齿轮关节：释放核心技能后的 buff 期间，玩家受到攻击也不会被击退或打断攻击动作。
    /// 实现：buff 期间设置 IgnoreHitStateOnDamage（阻止进入 Hit 状态 = 攻击不被打断，
    /// 且 MainCharacter 同步抑制 _pendingHitKnockback 击退标记）+ ForcedMovement 免疫
    /// （拦截 KnockbackOnAttackEffect 等直接驱动击退的效果）。
    /// 暂存玩家原值，buff 结束/效果移除时还原（参考 EnemyAttackTemplate 的免疫暂存模式）。
    /// </summary>
    [GlobalClass]
    public partial class MachineGearJointEffect : ActorEffect
    {
        private MachineCoreEffect? _core;
        private bool _buffWasActive;
        private ImmunityFlags _storedImmunities;
        private bool _storedIgnoreHitState;
        private bool _applied;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            _buffWasActive = _core?.IsBuffActive ?? false;
        }

        protected override void OnTick(double delta)
        {
            if (_core == null || Actor == null) return;

            bool buffActive = _core.IsBuffActive;
            if (buffActive && !_buffWasActive)
                Apply();
            else if (!buffActive && _buffWasActive)
                Restore();
            _buffWasActive = buffActive;
        }

        private void Apply()
        {
            if (Actor == null || _applied) return;
            _storedImmunities = Actor.ActiveImmunities;
            _storedIgnoreHitState = Actor.IgnoreHitStateOnDamage;
            Actor.ActiveImmunities |= ImmunityFlags.ForcedMovement;
            Actor.IgnoreHitStateOnDamage = true;
            _applied = true;
        }

        private void Restore()
        {
            if (Actor == null || !_applied) return;
            Actor.ActiveImmunities = _storedImmunities;
            Actor.IgnoreHitStateOnDamage = _storedIgnoreHitState;
            _applied = false;
        }

        public override void OnRemoved()
        {
            Restore(); // 效果被移除（如卡牌失效/玩家死亡清理）时兜底还原
            base.OnRemoved();
        }
    }
}
