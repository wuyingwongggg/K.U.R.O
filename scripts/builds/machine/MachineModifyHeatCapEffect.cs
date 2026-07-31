using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 修改热量上限。正值为扩容，负值为减容。
    /// TierValues 数组长度 = 可升级次数，值 = 每层的变化百分比。
    /// 各 .tres 通过 AttackEffectEntry.PropertyOverrides 覆盖 TierValues。
    /// </summary>
    [GlobalClass]
    public partial class MachineModifyHeatCapEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 30f, 40f, 50f };

        private MachineCoreEffect? _core;
        private float _originalMaxHeat;
        private int _tier;

        private float CurrentPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _originalMaxHeat = _core.MaxHeat;
            _core.MaxHeat = _originalMaxHeat * (1f + CurrentPercent / 100f);
        }

        protected override void OnStackRefreshed()
        {
            if (_core == null) return;
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            _core.MaxHeat = _originalMaxHeat * (1f + CurrentPercent / 100f);
        }

        public override void OnRemoved()
        {
            if (_core != null)
                _core.MaxHeat = _originalMaxHeat;
            base.OnRemoved();
        }
    }
}
