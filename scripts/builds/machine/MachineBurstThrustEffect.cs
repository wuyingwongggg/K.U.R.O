using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 爆发推力：释放核心技能后的第一次攻击（DirectAttack 命中）造成额外击退（距离）
    /// 并提升该次伤害（追加伤害，不触发暴击等武器词条）。每个 buff 周期只触发一次。
    /// </summary>
    [GlobalClass]
    public partial class MachineBurstThrustEffect : ActorEffect
    {
        [Export] public float[] KnockbackValues { get; set; } = { 50f, 100f, 150f };   // 击退距离（px）
        [Export] public float[] DamageBonusValues { get; set; } = { 10f, 20f, 30f };    // 伤害提升（%）
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        private MachineCoreEffect? _core;
        private int _tier;
        private bool _buffWasActive;
        private bool _armed;   // 本 buff 周期内尚未触发首次攻击
        private bool _subscribed;

        private float CurrentKnockback => _tier < KnockbackValues.Length ? KnockbackValues[_tier] : KnockbackValues[^1];
        private float CurrentDamageBonus => _tier < DamageBonusValues.Length ? DamageBonusValues[_tier] : DamageBonusValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            _buffWasActive = _core?.IsBuffActive ?? false;
            if (!_subscribed)
            {
                DamageEventBus.SubscribeWithSource(OnDamageResolved);
                _subscribed = true;
            }
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, KnockbackValues.Length - 1);
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
                _subscribed = false;
            }
            base.OnRemoved();
        }

        protected override void OnTick(double delta)
        {
            if (_core == null) return;

            bool buffActive = _core.IsBuffActive;
            if (buffActive && !_buffWasActive)
                _armed = true; // buff 开始 → 武装首次攻击
            _buffWasActive = buffActive;
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (!_armed || _core == null || !_core.IsBuffActive) return;
            if (Actor == null || attacker != Actor) return;
            // 防御：当前事件流不会传出 null target，但保留判空避免未来路径变更时
            // 崩溃或"首次攻击"名额被无效目标消耗
            if (target == null) return;

            _armed = false; // 每 buff 周期只触发一次

            // 伤害提升：追加 bonus% 伤害（EffectBonus 来源，不触发暴击等武器词条）
            int bonus = Mathf.RoundToInt(damage * CurrentDamageBonus / 100f);
            if (bonus > 0)
                target.TakeDamage(bonus, Actor.GlobalPosition, Actor, DamageSource.EffectBonus);

            // 击退：距离 → 速度，沿玩家指向目标的方向（尊重 ForcedMovement 免疫）
            if (CurrentKnockback <= 0f) return;
            Vector2 dir = target.GlobalPosition - Actor.GlobalPosition;
            if (dir == Vector2.Zero) dir = Vector2.Right;
            if (!target.ActiveImmunities.HasFlag(ImmunityFlags.ForcedMovement))
                target.Velocity = dir.Normalized() * (CurrentKnockback / Mathf.Max(KnockbackDuration, 0.01f));
        }
    }
}
