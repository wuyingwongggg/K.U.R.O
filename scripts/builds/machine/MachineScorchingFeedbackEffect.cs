using Godot;
using Godot.Collections;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 灼热反馈：热量达到阈值时，每次攻击对目标施加灼烧。
    /// BurnEffectEntries 每个 entry = 灼烧场景 + PropertyOverrides（数值重载）。
    /// TierValues = 每层的热量阈值百分比（100=满热量，90=90% MaxHeat）。
    /// </summary>
    [GlobalClass]
    public partial class MachineScorchingFeedbackEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 100f, 90f };
        [Export] public Array<AttackEffectEntry> BurnEffectEntries { get; set; } = new();

        private MachineCoreEffect? _core;
        private int _tier;
        private bool _subscribed;

        private float ThresholdPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];   // 当前层的热量阈值百分比
        private bool HeatAboveThreshold => _core != null && _core.Heat >= _core.MaxHeat * ThresholdPercent / 100f; // 当前热量是否达到当前层的阈值

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (!_subscribed)
            {
                DamageEventBus.SubscribeWithSource(OnDamageDealt);
                _subscribed = true;
            }
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageDealt);
                _subscribed = false;
            }
            base.OnRemoved();
        }

        private void OnDamageDealt(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source == DamageSource.EffectBonus) return;
            if (Actor == null || attacker != Actor) return;
            if (!HeatAboveThreshold) return;
            if (target.IsDeathSequenceActive || target.IsDead) return;

            foreach (var entry in BurnEffectEntries)
            {
                if (entry?.Scene == null) continue;
                if (entry.InstantiateEffect() is not DotBurnEffect burn) continue;
                burn.Attacker = Actor;
                target.ApplyEffect(burn);
            }
        }
    }
}
