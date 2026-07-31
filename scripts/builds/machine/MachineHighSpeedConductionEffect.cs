using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 高速导热：所有热量积累速度提升，热量上限降低，非释放时衰减速度提高。
    /// GainValues = 每层的获取倍率（1.75 = +75%）
    /// CapValues  = 每层的容量乘数（0.7 = -30%）
    /// DecayValues = 每层的衰减倍率（1.75 = +75%）
    /// </summary>
    [GlobalClass]
    public partial class MachineHighSpeedConductionEffect : ActorEffect
    {
        [Export] public float[] GainValues { get; set; } = { 1.75f, 2.0f };
        [Export] public float[] CapValues { get; set; } = { 0.7f, 0.5f };
        [Export] public float[] DecayValues { get; set; } = { 1.75f, 2.0f };

        private MachineCoreEffect? _core;
        private float _originalMoveHeatRate;
        private float _originalAttackHeatGain;
        private float _originalMaxHeat;
        private float _originalDecayRate;
        private float _originalHeatDrainRate;
        private int _tier;

        private float CurrentGain => _tier < GainValues.Length ? GainValues[_tier] : GainValues[^1];
        private float CurrentCap => _tier < CapValues.Length ? CapValues[_tier] : CapValues[^1];
        private float CurrentDecay => _tier < DecayValues.Length ? DecayValues[_tier] : DecayValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _originalMoveHeatRate = _core.MoveHeatRate;
            _originalAttackHeatGain = _core.AttackHeatGain;
            _originalMaxHeat = _core.MaxHeat;
            _originalDecayRate = _core.DecayRate;
            _originalHeatDrainRate = _core.HeatDrainRate;
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
            _core.MoveHeatRate = _originalMoveHeatRate * CurrentGain;
            _core.AttackHeatGain = _originalAttackHeatGain * CurrentGain;
            _core.MaxHeat = _originalMaxHeat * CurrentCap;
            _core.DecayRate = _originalDecayRate * CurrentDecay;
            _core.HeatDrainRate = _originalHeatDrainRate * CurrentDecay;
        }

        public override void OnRemoved()
        {
            if (_core != null)
            {
                _core.MoveHeatRate = _originalMoveHeatRate;
                _core.AttackHeatGain = _originalAttackHeatGain;
                _core.MaxHeat = _originalMaxHeat;
                _core.DecayRate = _originalDecayRate;
                _core.HeatDrainRate = _originalHeatDrainRate;
            }
            base.OnRemoved();
        }
    }
}
