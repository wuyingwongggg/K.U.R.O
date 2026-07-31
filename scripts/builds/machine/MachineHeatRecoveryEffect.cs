using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 热量回收：未发动核心技能时，若热量低于阈值则自动恢复到该阈值。
    /// TierValues 为每层的阈值/恢复速率百分比（基于 MaxHeat）。
    /// </summary>
    [GlobalClass]
    public partial class MachineHeatRecoveryEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 15f, 30f };

        private MachineCoreEffect? _core;
        private int _tier;

        private float CurrentPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            ApplyMinHeat();
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            ApplyMinHeat();
        }

        public override void OnRemoved()
        {
            if (_core != null)
                _core.MinHeat = 0f;
            base.OnRemoved();
        }

        private void ApplyMinHeat()
        {
            if (_core != null)
                _core.MinHeat = _core.MaxHeat * CurrentPercent / 100f;
        }

        protected override void OnTick(double delta)
        {
            if (_core == null) return;
            if (_core.IsReleasing || _core.IsBuffActive) return;

            float threshold = _core.MaxHeat * CurrentPercent / 100f;
            if (_core.Heat >= threshold) return;

            float regenAmount = Mathf.Min(threshold - _core.Heat, threshold * (float)delta);
            _core.AddHeat(regenAmount);
        }

    }
}
