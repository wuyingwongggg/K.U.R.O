using Godot;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 神经稳压（BuildNormal_A_004）：受到生命值阈值比例以下的伤害时不会被打断（不进入 Hit 状态）。
    /// TierValues = 各层阈值百分比（如 10 表示最大生命的 10%）。
    /// </summary>
    [GlobalClass]
    public partial class NormalSmallHitSuppressEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 10f };

        private float _originalRatio;
        private int _tier;
        private bool _applied;

        private float CurrentPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            if (Actor == null) return;
            _originalRatio = Actor.SmallDamageHitThresholdRatio;
            _tier = 0;
            ApplyTier();
        }

        protected override void OnStackRefreshed()
        {
            if (Actor == null || !_applied) return;
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            ApplyTier();
        }

        public override void OnRemoved()
        {
            if (Actor != null && _applied)
            {
                Actor.SuppressSmallDamageHit = false;
                Actor.SmallDamageHitThresholdRatio = _originalRatio;
            }
            _applied = false;
            base.OnRemoved();
        }

        private void ApplyTier()
        {
            if (Actor == null) return;
            Actor.SuppressSmallDamageHit = true;
            Actor.SmallDamageHitThresholdRatio = CurrentPercent / 100f;
            _applied = true;
        }
    }
}
