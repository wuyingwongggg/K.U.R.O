using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 高速导热：所有热量积累速度提升，热量上限降低，非释放时衰减速度提升。
    /// GainValues  = 每层的获取增幅百分比（75 = +75%，100 = +100%）
    /// CapValues   = 每层的容量变化百分比（-30 = -30%，-50 = -50%）
    /// DecayValues = 每层的衰减增幅百分比（75 = +75%，100 = +100%）
    /// DecayValues 只作用于 DecayRate（热量衰减），不影响 HeatDrainRate（释放泄热）。
    /// 全部通过 MachineCoreEffect 修改器注册，基于基础值加减。
    /// </summary>
    [GlobalClass]
    public partial class MachineHighSpeedConductionEffect : ActorEffect
    {
        [Export] public float[] GainValues { get; set; } = { 75f, 100f };
        [Export] public float[] CapValues { get; set; } = { -30f, -50f };
        [Export] public float[] DecayValues { get; set; } = { 75f, 100f };

        private MachineCoreEffect? _core;
        private int _tier;

        private float CurrentGain => _tier < GainValues.Length ? GainValues[_tier] : GainValues[^1];
        private float CurrentCap => _tier < CapValues.Length ? CapValues[_tier] : CapValues[^1];
        private float CurrentDecay => _tier < DecayValues.Length ? DecayValues[_tier] : DecayValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            ApplyCurrentTier();
        }

        protected override void OnStackRefreshed()
        {
            if (_core == null) return;
            int maxLen = Mathf.Max(GainValues.Length, Mathf.Max(CapValues.Length, DecayValues.Length));
            _tier = Mathf.Min(_tier + 1, maxLen - 1);
            ApplyCurrentTier();
        }

        private void ApplyCurrentTier()
        {
            if (_core == null) return;
            _core.SetStatModifier(MachineCoreEffect.HeatStat.MoveHeatRate, EffectId, CurrentGain);
            _core.SetStatModifier(MachineCoreEffect.HeatStat.MaxHeat, EffectId, CurrentCap);
            _core.SetStatModifier(MachineCoreEffect.HeatStat.DecayRate, EffectId, CurrentDecay);
            // DecayValues 只作用于 DecayRate，不作用于 HeatDrainRate
        }

        public override void OnRemoved()
        {
            if (_core != null)
            {
                _core.RemoveStatModifier(MachineCoreEffect.HeatStat.MoveHeatRate, EffectId);
                _core.RemoveStatModifier(MachineCoreEffect.HeatStat.MaxHeat, EffectId);
                _core.RemoveStatModifier(MachineCoreEffect.HeatStat.DecayRate, EffectId);
            }
            base.OnRemoved();
        }
    }
}
