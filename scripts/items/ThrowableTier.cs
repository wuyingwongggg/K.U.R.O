using System;
using System.Collections.Generic;
using System.Linq;

namespace Kuros.Items
{
    /// <summary>
    /// 构筑修饰：查询时对物品定义档位做临时偏移/参数覆盖——物品仍只有一个档位,
    /// 档位表不复制。层序:升档(改基础)→ 定义特化(raw>0 字段)→ 增幅(距离覆盖/倍率)。
    /// 持握减速永不随任何 shift(增强投掷物不让自己变慢)。
    /// </summary>
    public readonly record struct ThrowableModifiers(
        int TierShift = 0,
        int AttackTierShift = 0,
        int KnockbackTierShift = 0,
        bool DistanceAsSmall = false,
        float DistanceScale = 1f)
    {
        /// <summary>整体升档：伤害/距离/时间/击退/HP 跟随(减速除外)。</summary>
        public int TierShift { get; init; } = TierShift;

        /// <summary>伤害单独升档。</summary>
        public int AttackTierShift { get; init; } = AttackTierShift;

        /// <summary>击退(距离+时长对)单独升档。</summary>
        public int KnockbackTierShift { get; init; } = KnockbackTierShift;

        /// <summary>距离覆盖为小型档距离(覆盖先于倍率)。</summary>
        public bool DistanceAsSmall { get; init; } = DistanceAsSmall;

        /// <summary>距离倍率(1 = 无)。</summary>
        public float DistanceScale { get; init; } = DistanceScale;

        /// <summary>无修饰(注意:不能用 default——DistanceScale 会为 0,须用此实例)。</summary>
        public static readonly ThrowableModifiers None = new ThrowableModifiers();
    }

    /// <summary>投掷物档位(一次性投掷道具分类;无档=投掷武器/普通道具走原始字段)。</summary>
    public enum ThrowableTier
    {
        None = 0,
        Small = 1,
        Medium = 2,
        Large = 3,
    }

    /// <summary>单档数值规格(7 参)。改档位数值只动 <see cref="ThrowableTierTable"/> 一处。</summary>
    public readonly record struct ThrowableTierSpec(
        float AttackPower,
        float ThrowDistance,
        double ThrowDuration,
        float KnockbackDistance,
        float KnockbackDuration,
        float MaxHp,
        float CarrySlowMultiplier);

    /// <summary>
    /// 档位静态表。未来 build 修饰(如 +1 档、按档 ±% 伤害)在此单点挂载:
    /// 修改解析函数(如按 modifier 平移 tier 或乘倍率)即可,勿在消费点散加。
    /// </summary>
    public static class ThrowableTierTable
    {
        public static readonly IReadOnlyDictionary<ThrowableTier, ThrowableTierSpec> Specs =
            new Dictionary<ThrowableTier, ThrowableTierSpec>
            {
                [ThrowableTier.Small] = new ThrowableTierSpec(
                    AttackPower: 20f, ThrowDistance: 700f, ThrowDuration: 0.4,
                    KnockbackDistance: 100f, KnockbackDuration: 0.1f,
                    MaxHp: 20f, CarrySlowMultiplier: 0.7f),
                [ThrowableTier.Medium] = new ThrowableTierSpec(
                    AttackPower: 60f, ThrowDistance: 500f, ThrowDuration: 0.35,
                    KnockbackDistance: 200f, KnockbackDuration: 0.18f,
                    MaxHp: 60f, CarrySlowMultiplier: 0.5f),
                [ThrowableTier.Large] = new ThrowableTierSpec(
                    AttackPower: 100f, ThrowDistance: 300f, ThrowDuration: 0.3,
                    KnockbackDistance: 300f, KnockbackDuration: 0.36f,
                    MaxHp: 100f, CarrySlowMultiplier: 0.3f),
            };

        public static bool TryGetSpec(ThrowableTier tier, out ThrowableTierSpec spec)
            => Specs.TryGetValue(tier, out spec);

        /// <summary>表中最高档(动态取 Keys 最大值:未来加 4 档仅需加表行+枚举值,此处自动扩)。</summary>
        public static readonly ThrowableTier MaxTier = Specs.Keys.Max();

        /// <summary>档位数值 clamp:下限 Small、上限 MaxTier;入参 &lt;=0(无档)原样返回。</summary>
        public static int ClampTier(int tierValue)
        {
            if (tierValue <= 0) return tierValue;
            int max = (int)MaxTier;
            return Math.Clamp(tierValue, (int)ThrowableTier.Small, max);
        }
    }
}
