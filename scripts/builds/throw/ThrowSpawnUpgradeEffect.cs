using Godot;
using Kuros.Actors.Heroes;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 数据升级（BuildThrow_A_006）：乱码块升级为中型/大型投掷道具——
    /// 生成时按层把 FurnitureScene 替换为该 ThrowTier 档家具池的随机一件(视觉即该家具)。
    /// 层1 → 中型(档 2);层2 → 大型(档 3)。
    /// 代价：每升一级充能恢复 CD +35%(叠加在基础值上:层1 ×1.35、层2 ×1.70)——
    /// 经 ThrowCoreEffect.RegisterChargeModifier 聚合(与 A_001 充能修饰同一通道,id 幂等)。
    /// </summary>
    [GlobalClass]
    public partial class ThrowSpawnUpgradeEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 2f, 3f };

        /// <summary>每层充能恢复 CD 增幅百分比(与 TierValues 同序:层1 +35%、层2 +70%,叠加在基础值上)。
        /// 数值同时供描述模板 {CooldownIncreasePercents:0}/{CooldownIncreasePercents:1} 显示——单一真源。</summary>
        [Export] public float[] CooldownIncreasePercents { get; set; } = { 35f, 70f };

        private const string CoreModifierId = "BuildThrow_A_006";

        private ThrowCoreEffect? _core;
        private int _lastSetOverride;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            ApplyTier(0);
        }

        protected override void OnStackRefreshed()
        {
            _core ??= Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            ApplyTier(1);
        }

        private void ApplyTier(int offset)
        {
            int level = Mathf.Clamp(offset, 0, 1) + 1; // 层1=1 层2=2
            int target = 1 + level;                    // 层1=中型(2) 层2=大型(3)
            if (_core == null) return;
            _core.SpawnTierOverride = target;
            _lastSetOverride = target;

            // 代价:每升一级充能恢复 CD 按该层百分比提升(id 幂等,升级刷新时重注册覆盖为新倍率)
            float cdPercent = CooldownIncreasePercents.Length > 0
                ? CooldownIncreasePercents[Mathf.Clamp(level - 1, 0, CooldownIncreasePercents.Length - 1)]
                : 0f;
            _core.RegisterChargeModifier(CoreModifierId, 0, 1f + cdPercent / 100f);
        }

        public override void OnRemoved()
        {
            if (_core != null)
            {
                if (_core.SpawnTierOverride == _lastSetOverride)
                    _core.SpawnTierOverride = 0;
                _core.UnregisterChargeModifier(CoreModifierId);
            }
            _core = null;
            base.OnRemoved();
        }
    }
}
