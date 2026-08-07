using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 修改释放核心技能时的热量衰减速度（HeatDrainRate）。
    /// 正值为加快（快速放热），负值为减慢（缓速放热）。
    /// 通过 MachineCoreEffect 修改器注册，多个效果基于基础值加减，互不漂移。
    /// </summary>
    [GlobalClass]
    public partial class MachineModifyHeatDrainEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 50f, 75f, 100f };

        private MachineCoreEffect? _core;
        private int _tier;

        private float CurrentPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _core.SetStatModifier(MachineCoreEffect.HeatStat.HeatDrainRate, EffectId, CurrentPercent);
        }

        protected override void OnStackRefreshed()
        {
            if (_core == null) return;
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            _core.SetStatModifier(MachineCoreEffect.HeatStat.HeatDrainRate, EffectId, CurrentPercent);
        }

        public override void OnRemoved()
        {
            if (_core != null)
                _core.RemoveStatModifier(MachineCoreEffect.HeatStat.HeatDrainRate, EffectId);
            base.OnRemoved();
        }
    }
}
