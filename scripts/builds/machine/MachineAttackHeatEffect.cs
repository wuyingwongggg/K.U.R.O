using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 战斗升温：攻击命中时获取热量。
    /// TierValues 数组长度 = 可升级次数，值 = 每层每次命中的热量获取量。
    /// </summary>
    [GlobalClass]
    public partial class MachineAttackHeatEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 3f, 4f, 5f };

        private MachineCoreEffect? _core;
        private int _tier;

        private float CurrentValue => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _core.EnableAttackHeatGain = true;
            _core.AttackHeatGain = CurrentValue;
        }

        protected override void OnStackRefreshed()
        {
            if (_core == null) return;
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            _core.AttackHeatGain = CurrentValue;
        }

        public override void OnRemoved()
        {
            if (_core != null)
                _core.EnableAttackHeatGain = false;
            base.OnRemoved();
        }
    }
}
