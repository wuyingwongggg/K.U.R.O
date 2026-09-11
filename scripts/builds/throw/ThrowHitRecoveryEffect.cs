using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 即时回收（BuildThrow_A_005）：玩家每次对敌人造成伤害时,提前当前乱码块充能恢复
    /// (推进串行恢复进度,跨格提前补满),每次 1/1.5/2 秒(随层)。
    /// 实现:订阅 DamageEventBus(玩家为攻击方、目标非自身、伤害>0) → ThrowCoreEffect.ReduceRecharge。
    /// TierValues = 各层每次减少的恢复秒数(层1/2/3 → 1/1.5/2s)。
    /// </summary>
    [GlobalClass]
    public partial class ThrowHitRecoveryEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 1f, 1.5f, 2f };

        private ThrowCoreEffect? _core;
        private int _tier = 1;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            DamageEventBus.SubscribeWithSource(OnDamageResolved);
        }

        protected override void OnStackRefreshed()
        {
            _core ??= Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        public override void OnRemoved()
        {
            DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
            _core = null;
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (Actor == null || attacker != Actor || target == Actor) return;
            if (damage <= 0 || _core == null) return;

            float seconds = TierValues.Length > 0
                ? TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1]
                : 0f;
            if (seconds <= 0f) return;

            _core.ReduceRecharge(seconds); // 未在恢复中/全满时内部自动忽略
        }
    }
}
