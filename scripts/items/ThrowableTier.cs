using System.Collections.Generic;
using System.Linq;

namespace Kuros.Items
{
    /// <summary>
    /// 构筑修饰：查询时对物品定义档位做临时偏移/参数覆盖——物品仍只有一个档位,
    /// 档位表不复制。层序:升档(改基础)→ 定义特化(raw>0 字段)→ 增幅(距离覆盖/倍率)。
    /// 持握减速随整体升档(TierShift 经档位表倍率结算),不含参数专属 shift。
    /// </summary>
    public readonly record struct ThrowableModifiers(
        int TierShift = 0,
        int AttackTierShift = 0,
        int KnockbackTierShift = 0,
        bool DistanceAsSmall = false,
        float DistanceScale = 1f,
        int DistanceTierShift = 0,
        float CarrySlowReduction = 0f,
        float AttackPowerScale = 1f,
        float FlightTimeScale = 1f,
        bool PassThroughEnemies = false)
    {
        /// <summary>整体升档：伤害/距离/时间/击退/HP 跟随(减速除外)。</summary>
        public int TierShift { get; init; } = TierShift;

        /// <summary>伤害单独升档。</summary>
        public int AttackTierShift { get; init; } = AttackTierShift;

        /// <summary>距离单独升档(B_002 重量化"距离不变"= 反向补偿用)。</summary>
        public int DistanceTierShift { get; init; } = DistanceTierShift;

        /// <summary>击退(距离+时长对)单独升档。</summary>
        public int KnockbackTierShift { get; init; } = KnockbackTierShift;

        /// <summary>距离覆盖为小型档距离(覆盖先于倍率)。</summary>
        public bool DistanceAsSmall { get; init; } = DistanceAsSmall;

        /// <summary>距离倍率(1 = 无)。</summary>
        public float DistanceScale { get; init; } = DistanceScale;

        /// <summary>持握减速幅度减免比例(B_003 负载减免;0=无,0.5=减速幅度减半)。</summary>
        public float CarrySlowReduction { get; init; } = CarrySlowReduction;

        /// <summary>撞击伤害倍率(B_006 投掷蓄力;1 = 无)。距离复用 <see cref="DistanceScale"/>。</summary>
        public float AttackPowerScale { get; init; } = AttackPowerScale;

        /// <summary>飞行时长倍率(B_006 投掷蓄力"弧线略增";1 = 无)。</summary>
        public float FlightTimeScale { get; init; } = FlightTimeScale;

        /// <summary>命中敌人不停留(B_008 延迟销毁):穿透敌人继续飞,只在落点销毁;仅一次性道具消费。</summary>
        public bool PassThroughEnemies { get; init; } = PassThroughEnemies;

        /// <summary>无修饰:显式传入倍率类默认值——结构体无参构造不应用位置参数默认值,
        /// 标量字段会落为 0(消费方有 >0?:1 守卫兜底,但做乘法的贡献者需要真实 1)。</summary>
        public static readonly ThrowableModifiers None = new ThrowableModifiers(
            DistanceScale: 1f, AttackPowerScale: 1f, FlightTimeScale: 1f);
    }

    /// <summary>投掷物档位(一次性投掷道具分类;无档=投掷武器/普通道具走原始字段)。仅 1/2/3 三档,
    /// 构筑修饰(如轻量化+2)越界时报 None,消费方(GetResolvedTierSpec)回退最近档,不新增档位。</summary>
    public enum ThrowableTier
    {
        None = 0,
        Small = 1,
        Medium = 2,
        Large = 3,
    }

    /// <summary>单档数值规格(9 参)。改档位数值只动 <see cref="ThrowableTierTable"/> 一处。</summary>
    public readonly record struct ThrowableTierSpec(
        float AttackPower,
        float ThrowDistance,
        double ThrowDuration,
        float KnockbackDistance,
        float KnockbackDuration,
        float MaxHp,
        float CarrySlowMultiplier,
        float BounceSpeed,
        float BounceHorizontalRatio);

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
                    MaxHp: 20f, CarrySlowMultiplier: 0.7f, BounceSpeed: 650f,
                    BounceHorizontalRatio: 0.3f),
                [ThrowableTier.Medium] = new ThrowableTierSpec(
                    AttackPower: 60f, ThrowDistance: 500f, ThrowDuration: 0.35,
                    KnockbackDistance: 200f, KnockbackDuration: 0.18f,
                    MaxHp: 60f, CarrySlowMultiplier: 0.5f, BounceSpeed: 550f,
                    BounceHorizontalRatio: 0.2f),
                [ThrowableTier.Large] = new ThrowableTierSpec(
                    AttackPower: 100f, ThrowDistance: 300f, ThrowDuration: 0.3,
                    KnockbackDistance: 300f, KnockbackDuration: 0.36f,
                    MaxHp: 100f, CarrySlowMultiplier: 0.3f, BounceSpeed: 450f,
                    BounceHorizontalRatio: 0.1f),
            };

        public static bool TryGetSpec(ThrowableTier tier, out ThrowableTierSpec spec)
            => Specs.TryGetValue(tier, out spec);

        /// <summary>表中最高档(动态取 Keys 最大值:再加档仅需加表行+枚举值,此处自动扩)。</summary>
        public static readonly ThrowableTier MaxTier = Specs.Keys.Max();

        /// <summary>有效档(含构筑偏移)规范化为可查档:数值 &lt;Min 或 &gt;Max 时
        /// **回退最近档**(如 1 降 2 → 取 Small;3 升 2 → 取 Large),保证任何偏移组合都有落地数值。</summary>
        public static ThrowableTier ResolveTier(int tierValue)
        {
            int min = (int)Specs.Keys.Min();
            int max = (int)MaxTier;
            if (tierValue > max) return (ThrowableTier)max;
            if (tierValue < min) return (ThrowableTier)min;
            return (ThrowableTier)tierValue;
        }
    }
}
