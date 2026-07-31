using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 热流循环：发动核心技能（Buff 期间）允许继续获取热量，倍率 100%。
    /// Epic，MaxStacks=1，不可升级。
    /// </summary>
    [GlobalClass]
    public partial class MachineHeatFlowCycleEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,2,0.1")] public float HeatGainMultiplier = 1.0f;

        private MachineCoreEffect? _core;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _core.DisableHeatGainDuringBuff = false;
        }

        public override void OnRemoved()
        {
            if (_core != null)
                _core.DisableHeatGainDuringBuff = true;
            base.OnRemoved();
        }
    }
}
