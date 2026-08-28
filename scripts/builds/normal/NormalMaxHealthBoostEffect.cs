using Godot;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 强化骨骼（BuildNormal_A_001）：血量上限提升，同时恢复等量当前血量（保持血量占比）。
    /// UsePercent = false（默认）：TierValues 为固定值（25/50 = 直接加 25/50 点）；
    /// UsePercent = true：TierValues 为百分比（25/50 = +25%/+50%），以挂载时的原始 MaxHealth 为基数。
    /// </summary>
    [GlobalClass]
    public partial class NormalMaxHealthBoostEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 25f, 50f };
        /// <summary>true = 按百分比提升（TierValues 为百分比数值）；false（默认）= 固定值直接加。</summary>
        [Export] public bool UsePercent { get; set; } = false;

        private int _originalMaxHealth;
        private int _tier;
        private bool _applied;

        private float CurrentValue => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            if (Actor == null) return;
            _originalMaxHealth = Actor.MaxHealth;
            _tier = 0;
            ApplyTier();
        }

        protected override void OnStackRefreshed()
        {
            if (Actor == null || !_applied) return;
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            // 先还原原始基数，再按新层倍率重算，避免从已提升值上重复累计
            Actor.MaxHealth = _originalMaxHealth;
            ApplyTier();
        }

        public override void OnRemoved()
        {
            if (Actor != null && _applied)
            {
                // 还原原始上限，当前血量不超出新上限
                Actor.RestoreHealth(Mathf.Min(Actor.CurrentHealth, _originalMaxHealth), _originalMaxHealth);
            }
            _applied = false;
            base.OnRemoved();
        }

        private void ApplyTier()
        {
            if (Actor == null) return;
            int bonus = UsePercent
                ? Mathf.RoundToInt(_originalMaxHealth * CurrentValue / 100f)
                : Mathf.RoundToInt(CurrentValue);
            Actor.MaxHealth = _originalMaxHealth + bonus;
            // 同步恢复等量当前血量，保持血量占比不变
            Actor.RestoreHealth(Actor.CurrentHealth + bonus);
            _applied = true;
        }
    }
}
