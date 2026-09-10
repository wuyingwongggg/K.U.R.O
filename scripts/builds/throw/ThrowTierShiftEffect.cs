using Godot;
using Kuros.Core.Effects;
using Kuros.Items;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 投掷档位偏移（BuildThrow_B_001 轻量化 / B_002 重量化**共用脚本**）：
    /// 对玩家所有**一次性投掷道具**(有档家具)做整体档位偏移;方向由 TierValues 符号决定——
    ///   负值 = 轻量化:整体降档(距离变远/时长/击退/HP 随档下降),**伤害补偿回原档(不变)**;
    ///   正值 = 重量化:整体升档(伤害/击退/HP/时长随档上升),**距离补偿回原档(不变)**。
    /// 两卡共用同一效果场景(B_001/B_002 .tres 指向同一 .tscn)→ 同族;方向相反 →
    /// BuildSelectionManager 的反向取代机制自动使二者互斥(获得时整族替换,不叠加)。
    /// 修饰经 IThrowableModifiersContributor 聚合(投掷与轨迹预览单点消费)。
    /// </summary>
    [GlobalClass]
    public partial class ThrowTierShiftEffect : ActorEffect, IThrowableModifiersContributor
    {
        /// <summary>各层的档位偏移量(负=轻量化/正=重量化;B_001 注入 [-1,-2]、B_002 注入 [1,2])。</summary>
        [Export] public float[] TierValues { get; set; } = { 1f, 2f };

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

            if (shift < 0)
            {
                // 轻量化:整体降档,伤害档反向补偿回原档(伤害不变)
                return mods with
                {
                    TierShift = mods.TierShift + shift,
                    AttackTierShift = mods.AttackTierShift - shift,
                };
            }

            // 重量化:整体升档,距离档反向补偿回原档(投掷距离不变)
            return mods with
            {
                TierShift = mods.TierShift + shift,
                DistanceTierShift = mods.DistanceTierShift - shift,
            };
        }
    }
}
