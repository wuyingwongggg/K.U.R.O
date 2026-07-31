using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 降温延迟：延长热量自动衰减前的延迟时间。
    /// TierValues 为每层的延迟倍率（1.5 = +50%，1.75 = +75%，2.0 = +100%）。
    /// </summary>
    [GlobalClass]
    public partial class MachineDecayDelayEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 1f, 1.5f, 2.0f };

        private MachineCoreEffect? _core;
        private float _originalDecayDelay;
        private int _tier;

        private float CurrentMultiplier => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _originalDecayDelay = _core.DecayDelay;
            _core.DecayDelay = _originalDecayDelay * CurrentMultiplier;
        }

        protected override void OnStackRefreshed()
        {
            if (_core == null) return;
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            _core.DecayDelay = _originalDecayDelay * CurrentMultiplier;
        }

        public override void OnRemoved()
        {
            if (_core != null)
                _core.DecayDelay = _originalDecayDelay;
            base.OnRemoved();
        }
    }
}
