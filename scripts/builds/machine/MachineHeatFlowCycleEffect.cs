using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 热流循环：发动核心技能（Buff 期间）允许继续获取热量，获取倍率可配置。
    /// Epic，MaxStacks=1，不可升级。
    /// </summary>
    [GlobalClass]
    public partial class MachineHeatFlowCycleEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,5,0.1")] public float HeatGainMultiplier = 1.0f;

        private MachineCoreEffect? _core;
        private bool _originalDisableHeatGain;
        private float _originalBuffMultiplier;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _originalDisableHeatGain = _core.DisableHeatGainDuringBuff;
            _originalBuffMultiplier = _core.BuffHeatGainMultiplier;
            _core.DisableHeatGainDuringBuff = false;
            _core.BuffHeatGainMultiplier = HeatGainMultiplier;
        }

        public override void OnRemoved()
        {
            if (_core != null)
            {
                _core.DisableHeatGainDuringBuff = _originalDisableHeatGain;
                _core.BuffHeatGainMultiplier = _originalBuffMultiplier;
            }
            base.OnRemoved();
        }
    }
}
