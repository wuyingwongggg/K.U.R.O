using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 热能闪避：释放核心技能后的 buff 期间，闪避不再消耗充能 CD，
    /// 改为消耗热量（20/10，随层数降低）。热量不足时无法闪避
    /// （由 PlayerDashState 通过 CanConsumeForDash 判定并拦截）。
    /// </summary>
    [GlobalClass]
    public partial class MachineHeatDashEffect : ActorEffect
    {
        [Export] public float[] HeatCostValues { get; set; } = { 20f, 10f }; // 每层闪避热量消耗

        private MachineCoreEffect? _core;
        private int _tier;

        private float CurrentCost => _tier < HeatCostValues.Length ? HeatCostValues[_tier] : HeatCostValues[^1];

        /// <summary>效果生效中（核心 buff 期间）。</summary>
        public bool IsActive => _core != null && _core.IsBuffActive;

        /// <summary>热量闪避是否可用（buff 期间且热量足够）。</summary>
        public bool CanConsumeForDash => IsActive && _core!.Heat >= CurrentCost;

        /// <summary>消耗热量执行一次闪避。热量不足时返回 false。</summary>
        public bool ConsumeForDash()
        {
            if (!CanConsumeForDash) return false;
            _core!.ConsumeHeat(CurrentCost);
            return true;
        }

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, HeatCostValues.Length - 1);
        }
    }
}
