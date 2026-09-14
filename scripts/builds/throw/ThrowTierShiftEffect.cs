using Godot;
using Kuros.Core.Effects;
using Kuros.Items;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 投掷档位偏移（BuildThrow_B_001 轻量化 / B_002 重量化**共用脚本**）：
    /// 对玩家所有**一次性投掷道具**(有档家具)做整体档位偏移;方向由 TierValues 符号决定——
    ///   负值 = 轻量化:整体降档(距离变远/时长/击退/回弹/持握减速随档变化),**每降一级伤害 −AttackPowerPerTier**;
    ///   正值 = 重量化:整体升档(距离变短/击退更大/时长与回弹随档),**每升一级伤害 +AttackPowerPerTier**。
    /// 伤害与血量一律**从物品的基础档、基础伤害算起**：攻击档与 HP 档都做反向补偿(≠ 随档乘性改变)，
    /// 伤害增减走 AttackPowerPerTierFlat × **实际生效档数**（档位夹取 1~3：一级件再降 / 三级件再升 → 0 档，
    /// 伤害不再变化）——每帧/每次刷新都重算，两张卡互相取代时不会叠加出错。
    /// 一句话口径：**改档卡动投掷表现与"每档 ±固定点"的伤害，血量恒不变；且效果按基础档计算、越界即止**。
    /// 两卡共用同一效果场景(B_001/B_002 .tres 指向同一 .tscn)→ 同族;方向相反 →
    /// BuildSelectionManager 的反向取代机制自动使二者互斥(获得时整族替换,不叠加)。
    /// 修饰经 IThrowableModifiersContributor 聚合(投掷与轨迹预览单点消费)。
    /// </summary>
    [GlobalClass]
    public partial class ThrowTierShiftEffect : ActorEffect, IThrowableModifiersContributor
    {
        /// <summary>各层的档位偏移量(负=轻量化/正=重量化;B_001 注入 [-1,-2]、B_002 注入 [1,2])。</summary>
        [Export] public float[] TierValues { get; set; } = { 1f, 2f };

        /// <summary>每个**实际生效档**的伤害增减幅度(B_001 注入 [10]、B_002 注入 [20];符号随卡自动取)。
        /// 数组形式以便卡面描述用 {AttackPowerPerTier:0} token 显示，单一真源。</summary>
        [Export] public float[] AttackPowerPerTier { get; set; } = { 20f };

        private int _tier = 1;

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        public ThrowableModifiers ModifyThrowableModifiers(ThrowableModifiers mods)
        {
            if (TierValues.Length == 0) return mods;

            int shift = Mathf.RoundToInt(TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1]);
            if (shift == 0) return mods;

            // 方向随卡:轻量化(负)每档减伤、重量化(正)每档增伤——幅度同一导出,避免符号写反
            float magnitude = AttackPowerPerTier.Length > 0 ? Mathf.Abs(AttackPowerPerTier[0]) : 0f;
            float perTierFlat = shift < 0 ? -magnitude : magnitude;

            // 攻击档与 HP 档双双反向补偿:伤害/血量都回到基础档解析(伤害的实际增减交给 perTierFlat,
            // 且它按夹取后的"实际生效档数"计算,越界自然为 0)
            return mods with
            {
                TierShift = mods.TierShift + shift,
                AttackTierShift = mods.AttackTierShift - shift,
                HpTierShift = mods.HpTierShift - shift,
                AttackPowerPerTierFlat = mods.AttackPowerPerTierFlat + perTierFlat,
            };
        }
    }
}
