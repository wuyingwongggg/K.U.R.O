using Godot;
using Kuros.Actors.Heroes;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 堆栈扩容（BuildThrow_A_001）：投掷核心生成乱码块的充能上限 +1/+2/+3（每层）。
    /// 经 ThrowCoreEffect.RegisterChargeModifier 聚合(充能 UI/恢复自动跟随)。
    /// TierValues = 各层增加的充能数(显示层),与其它层卡一致经 PropertyOverrides 注入。
    /// </summary>
    [GlobalClass]
    public partial class ThrowStackExpandEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 1f, 2f, 3f };

        private const string ModifierId = "throw_stack_expand";
        private ThrowCoreEffect? _core;
        private int _tier;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            _tier = 1;
            ApplyTier();
        }

        protected override void OnStackRefreshed()
        {
            _core ??= Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
            ApplyTier();
        }

        private void ApplyTier()
        {
            int flat = Mathf.Max(1, Mathf.RoundToInt(TierValues[_tier - 1]));
            _core?.RegisterChargeModifier(ModifierId, flat, 1f);
        }

        public override void OnRemoved()
        {
            _core?.UnregisterChargeModifier(ModifierId);
            _core = null;
            base.OnRemoved();
        }
    }
}
