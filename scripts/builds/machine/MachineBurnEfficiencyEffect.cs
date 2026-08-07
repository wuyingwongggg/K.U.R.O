using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 燃烧效率：释放核心技能后的攻击力加成提升（×1+50%/100%），
    /// buff 期间随热量消耗持续线性衰减，最低衰减到 ×1+10%/20%。
    /// 衰减进度以热量消耗比例衡量（热量即 buff 燃料，消耗越快衰减越快）。
    /// 通过 MachineCoreEffect.ReleaseBonusMultiplier 接管释放加成倍率。
    /// </summary>
    [GlobalClass]
    public partial class MachineBurnEfficiencyEffect : ActorEffect
    {
        [Export] public float[] BonusMultiplierValues { get; set; } = { 50f, 100f };     // 加成提升量（%，显示与描述一致）
        [Export] public float[] MinBonusMultiplierValues { get; set; } = { -100f, -100f }; // 衰减下限提升量（-100% = 加成完全消失，攻击力回落到纯基础）

        private MachineCoreEffect? _core;
        private int _tier;
        private bool _buffWasActive;

        private float CurrentBonusMult => _tier < BonusMultiplierValues.Length ? BonusMultiplierValues[_tier] : BonusMultiplierValues[^1];
        private float CurrentMinMult => _tier < MinBonusMultiplierValues.Length ? MinBonusMultiplierValues[_tier] : MinBonusMultiplierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            _buffWasActive = _core?.IsBuffActive ?? false;
            // 释放瞬间同步接管倍率（ReleaseHeat 内部触发，早于命中触发的补偿计算）
            if (_core != null)
                _core.ReleaseStarted += OnReleaseStarted;
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, BonusMultiplierValues.Length - 1);
        }

        protected override void OnTick(double delta)
        {
            if (_core == null || Actor == null) return;

            // 倍率接管由 ReleaseStarted 事件负责（释放瞬间同步执行，见 OnReleaseStarted），
            // 这里只做 buff 期间的热量衰减更新与结束还原
            if (_core.IsBuffActive)
            {
                // 随热量消耗线性衰减：buff 开始 progress=0（起始倍率），热量归零 progress=1（下限倍率）
                float progress = 1f - _core.Heat / Mathf.Max(_core.ConsumedHeat, 0.01f);
                _core.ReleaseBonusMultiplier = Mathf.Lerp(1f + CurrentBonusMult / 100f, 1f + CurrentMinMult / 100f, Mathf.Clamp(progress, 0f, 1f));
                // 每帧把衰减后的倍率写回攻击力：AttackDamage 的刷新入口只有 RefreshReleaseDamageBonus，
                // 否则伤害会停留在释放瞬间的满值，热量消耗再多也不衰减
                _core.RefreshReleaseDamageBonus();
            }
            else
            {
                // buff 结束还原原始倍率
                _core.ReleaseBonusMultiplier = 1f;
            }
            _buffWasActive = _core.IsBuffActive;
        }

        /// <summary>释放瞬间同步接管（在 ReleaseHeat 内、命中触发补偿计算前执行）：设为满倍率并写回攻击力。
        /// 同步 _buffWasActive 抑制 OnTick 的重复接管。</summary>
        private void OnReleaseStarted()
        {
            if (_core == null || Actor == null) return;
            _core.ReleaseBonusMultiplier = 1f + CurrentBonusMult / 100f;
            _core.RefreshReleaseDamageBonus();
            _buffWasActive = true;
        }

        public override void OnRemoved()
        {
            // 效果被移除（或卡牌失效）时还原倍率，防止残留加成
            if (_core != null)
            {
                _core.ReleaseStarted -= OnReleaseStarted;
                _core.ReleaseBonusMultiplier = 1f;
            }
            base.OnRemoved();
        }
    }
}
