using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 损伤回收（BuildNormal_A_005）：虚血保留期间（受击后虚血未下降完，窗口自然跟随虚血延迟），
    /// 对敌人造成伤害时按比例恢复虚血保留量——复用 GhostHealthComponent.RecoverFromRetention。
    /// TierValues = 各层恢复比例百分比（如 30 / 50 表示恢复虚血保留量的 30% / 50%）。
    /// </summary>
    [GlobalClass]
    public partial class NormalDamageRecoveryEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 30f, 50f };

        private GhostHealthComponent? _ghost;
        private int _tier;

        private float CurrentPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _ghost = Actor?.GetNodeOrNull<GhostHealthComponent>("GhostHealthComponent");
            if (_ghost == null)
            {
                _ghost = Actor?.FindChild("GhostHealthComponent", recursive: true, owned: false) as GhostHealthComponent;
            }
            DamageEventBus.SubscribeWithSource(OnDamageResolved);
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
        }

        public override void OnRemoved()
        {
            DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (Actor == null || attacker != Actor || _ghost == null) return;
            if (damage <= 0 || target == Actor) return;

            // 虚血保留中才生效（窗口 = 虚血未下降完期间）；保留结束后自动失效
            _ghost.RecoverFromRetention(damage, CurrentPercent / 100f);
        }
    }
}
