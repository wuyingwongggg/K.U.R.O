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
    /// </summary>
    [GlobalClass]
    public partial class ThrowSpawnUpgradeEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 2f, 3f };

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
            int target = 2 + Mathf.Clamp(offset, 0, 1); // 层1=中型(2) 层2=大型(3)
            if (_core == null) return;
            _core.SpawnTierOverride = target;
            _lastSetOverride = target;
        }

        public override void OnRemoved()
        {
            if (_core != null && _core.SpawnTierOverride == _lastSetOverride)
                _core.SpawnTierOverride = 0;
            _core = null;
            base.OnRemoved();
        }
    }
}
