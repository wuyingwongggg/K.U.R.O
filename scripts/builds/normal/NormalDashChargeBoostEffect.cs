using Godot;
using Kuros.Actors.Heroes.States;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 闪避扩容（BuildNormal_B_002）：闪避的最大充能提升 TierValues 层值。
    /// 复用 PlayerDashState.MaxCharges（重载现有值，基于挂载时原始值累加）。
    /// </summary>
    [GlobalClass]
    public partial class NormalDashChargeBoostEffect : ActorEffect
    {
        /// <summary>各层增加的充能数（如 1 / 2 表示 +1 / +2）。</summary>
        [Export] public float[] TierValues { get; set; } = { 1f, 2f };

        private PlayerDashState? _dash;
        private int _originalMax;
        private int _tier;
        private bool _applied;

        private int CurrentBonus => Mathf.RoundToInt(_tier < TierValues.Length ? TierValues[_tier] : TierValues[^1]);

        protected override void OnApply()
        {
            _dash = Actor?.StateMachine?.GetNodeOrNull<PlayerDashState>("Dash");
            if (_dash == null || Actor == null) return;
            _originalMax = _dash.MaxCharges;
            _tier = 0;
            ApplyTier();
        }

        protected override void OnStackRefreshed()
        {
            if (_dash == null || !_applied) return;
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            ApplyTier();
        }

        public override void OnRemoved()
        {
            if (_dash != null && _applied)
            {
                _dash.MaxCharges = _originalMax;
            }
            _applied = false;
            base.OnRemoved();
        }

        private void ApplyTier()
        {
            if (_dash == null) return;
            _dash.MaxCharges = _originalMax + CurrentBonus;
            _applied = true;
        }
    }
}
