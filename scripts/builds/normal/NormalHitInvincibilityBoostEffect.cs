using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 损伤缓冲（BuildNormal_A_002）：受伤后的无敌时间按倍率提升。
    /// TierValues = 各层倍率（如 2 / 3 表示 ×2 / ×3），
    /// 最终时长 = 原始 HitInvincibilityDuration × 当前层倍率。
    /// </summary>
    [GlobalClass]
    public partial class NormalHitInvincibilityBoostEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 2f, 3f };

        private MainCharacter? _main;
        private float _originalDuration;
        private int _tier;
        private bool _applied;

        private float CurrentMultiplier => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            if (Actor is not MainCharacter main) return;
            _main = main;
            _originalDuration = main.HitInvincibilityDuration;
            _tier = 0;
            ApplyTier();
        }

        protected override void OnStackRefreshed()
        {
            if (_main == null || !_applied) return;
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            ApplyTier();
        }

        public override void OnRemoved()
        {
            if (_main != null && _applied)
            {
                _main.HitInvincibilityDuration = _originalDuration;
            }
            _applied = false;
            base.OnRemoved();
        }

        private void ApplyTier()
        {
            if (_main == null) return;
            _main.HitInvincibilityDuration = _originalDuration * CurrentMultiplier;
            _applied = true;
        }
    }
}
