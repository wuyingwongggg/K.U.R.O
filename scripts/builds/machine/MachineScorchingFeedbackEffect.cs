using System.Collections.Generic;
using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 灼热反馈：热量突破阈值时，每次攻击对目标造成灼烧 DoT。
    /// 重复施加覆盖伤害和持续时间。
    /// TierValues = 每层的热量阈值百分比（100=超过100%MaxHeat, 90=超过90%）。
    /// BurnDamagePercent = 每秒灼烧伤害占攻击力的百分比。
    /// </summary>
    [GlobalClass]
    public partial class MachineScorchingFeedbackEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 100f, 90f };
        [Export(PropertyHint.Range, "5,200,5")] public float BurnDamagePercent = 20f;
        [Export(PropertyHint.Range, "1,10,0.5")] public float BurnDuration = 3f;
        [Export(PropertyHint.Range, "0.5,3,0.1")] public float BurnTickInterval = 1f;

        private MachineCoreEffect? _core;
        private int _tier;
        private bool _subscribed;

        private readonly Dictionary<GameActor, BurnState> _burns = new();

        private float ThresholdPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];
        private bool HeatAboveThreshold => _core != null && _core.Heat > _core.MaxHeat * ThresholdPercent / 100f;

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

        protected override void OnTick(double delta)
        {
            float dt = (float)delta;
            var expired = new List<GameActor>();

            foreach (var (target, state) in _burns)
            {
                if (!GodotObject.IsInstanceValid(target) || target.IsDead)
                {
                    expired.Add(target);
                    continue;
                }

                state.Remaining -= dt;
                state.TickAccum += dt;
                while (state.TickAccum >= BurnTickInterval)
                {
                    state.TickAccum -= BurnTickInterval;
                    int damage = Mathf.Max(1, Mathf.RoundToInt((Actor?.AttackDamage ?? 10f) * BurnDamagePercent / 100f * BurnTickInterval));
                    target.TakeDamage(damage, Vector2.Zero, Actor, DamageSource.EffectBonus);
                }

                if (state.Remaining <= 0f)
                    expired.Add(target);
            }

            foreach (var t in expired)
                _burns.Remove(t);
        }

        private void OnDamageDealt(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (Actor == null || attacker != Actor) return;
            if (!HeatAboveThreshold) return;
            if (target.IsDeathSequenceActive || target.IsDead) return;

            if (_burns.TryGetValue(target, out var existing))
            {
                existing.Remaining = BurnDuration;
                existing.TickAccum = 0f;
            }
            else
            {
                _burns[target] = new BurnState { Remaining = BurnDuration };
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
            _burns.Clear();
            base.OnRemoved();
        }

        private sealed class BurnState
        {
            public float Remaining;
            public float TickAccum;
        }
    }
}
