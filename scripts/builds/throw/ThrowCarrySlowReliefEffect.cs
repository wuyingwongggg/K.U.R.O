using Godot;
using Kuros.Core.Effects;
using Kuros.Items;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 负载减免（BuildThrow_B_003）：所有投掷道具的持握减速幅度降低。
    /// TierValues 为**百分比**(50/75 → 减速降低 50%/75%),经 IThrowableModifiersContributor
    /// 聚合(CarrySlowReduction)。基础倍率仍由档位表 CarrySlowMultiplier 决定(含轻量化/重量化
    /// 的档位偏移),本卡只压缩其减速幅度:新倍率 = 1 - (1 - 档位倍率) × (1 - 减免)。
    /// </summary>
    [GlobalClass]
    public partial class ThrowCarrySlowReliefEffect : ActorEffect, IThrowableModifiersContributor
    {
        /// <summary>各层的减速幅度减免百分比(B_003 注入 [50, 75])。</summary>
        [Export] public float[] TierValues { get; set; } = { 50f, 75f };

        private int _tier = 1;

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        public ThrowableModifiers ModifyThrowableModifiers(ThrowableModifiers mods)
        {
            if (TierValues.Length == 0) return mods;

            float reduction = Mathf.Clamp(
                TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1] / 100f, 0f, 1f);
            return mods with { CarrySlowReduction = Mathf.Min(mods.CarrySlowReduction + reduction, 1f) };
        }
    }
}
