using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 异常超频：允许热量突破 MaxHeat，每溢出 1 点热量分别提升移动速度、攻击速度、受到的伤害。
    /// 三个增益百分比独立配置。增益由 MachineCoreEffect 每帧应用。
    /// Epic，MaxStacks=1，不可升级。
    /// </summary>
    [GlobalClass]
    public partial class MachineOverclockEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,50,0.5")] public float SpeedPercentPerHeat = 2f;
        [Export(PropertyHint.Range, "0,50,0.5")] public float AttackSpeedPercentPerHeat = 1f;
        [Export(PropertyHint.Range, "0,50,0.5")] public float DamageTakenPercentPerHeat = 2f;

        private MachineCoreEffect? _core;
        private bool _originalAllowOverflow;
        private float _originalSpeedPercent;
        private float _originalAttackSpeedPercent;
        private float _originalDamageTakenPercent;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _originalAllowOverflow = _core.AllowHeatOverflow;
            _originalSpeedPercent = _core.OverflowSpeedPercentPerHeat;
            _originalAttackSpeedPercent = _core.OverflowAttackSpeedPercentPerHeat;
            _originalDamageTakenPercent = _core.OverflowDamageTakenPercentPerHeat;
            _core.AllowHeatOverflow = true;
            _core.OverflowSpeedPercentPerHeat = SpeedPercentPerHeat;
            _core.OverflowAttackSpeedPercentPerHeat = AttackSpeedPercentPerHeat;
            _core.OverflowDamageTakenPercentPerHeat = DamageTakenPercentPerHeat;
        }

        public override void OnRemoved()
        {
            if (_core != null)
            {
                _core.AllowHeatOverflow = _originalAllowOverflow;
                _core.OverflowSpeedPercentPerHeat = _originalSpeedPercent;
                _core.OverflowAttackSpeedPercentPerHeat = _originalAttackSpeedPercent;
                _core.OverflowDamageTakenPercentPerHeat = _originalDamageTakenPercent;
            }
            base.OnRemoved();
        }
    }
}
