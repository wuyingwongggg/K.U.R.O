using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 异常超频：允许热量突破 MaxHeat，每溢出 1 点热量使移动速度、攻击速度、受到的伤害
    /// 提升 BuffPercentPerHeat%。增益由 MachineCoreEffect 每帧应用。
    /// Epic，MaxStacks=1，不可升级。
    /// </summary>
    [GlobalClass]
    public partial class MachineOverclockEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,50,0.5")] public float BuffPercentPerHeat = 2f;

        private MachineCoreEffect? _core;
        private bool _originalAllowOverflow;
        private float _originalPercent;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _originalAllowOverflow = _core.AllowHeatOverflow;
            _originalPercent = _core.OverflowBuffPercentPerHeat;
            _core.AllowHeatOverflow = true;
            _core.OverflowBuffPercentPerHeat = BuffPercentPerHeat;
        }

        public override void OnRemoved()
        {
            if (_core != null)
            {
                _core.AllowHeatOverflow = _originalAllowOverflow;
                _core.OverflowBuffPercentPerHeat = _originalPercent;
            }
            base.OnRemoved();
        }
    }
}
