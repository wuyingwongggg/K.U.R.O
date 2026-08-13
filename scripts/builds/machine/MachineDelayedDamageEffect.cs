using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 延迟损伤：释放核心技能后的 buff 期间，玩家受到的所有伤害在 buff 结束时统一结算。
    /// 实现：buff 期间在 GameActor.DamageIntercepted 挂 handler，把最终伤害累计并置 0
    /// （拦截发生在 IncomingDamageMultiplier 应用之后，缓存的是最终值）；
    /// buff 结束先摘 handler 再统一 TakeDamage，结算时临时置 IncomingDamageMultiplier=1，
    /// 避免最终值被二次放大。attacker 不在拦截事件参数中，结算时传 null（不发布事件）。
    /// </summary>
    [GlobalClass]
    public partial class MachineDelayedDamageEffect : ActorEffect
    {
        private MachineCoreEffect? _core;
        private bool _buffWasActive;
        private int _pendingDamage;
        private Vector2? _lastOrigin;
        private bool _subscribed;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            _buffWasActive = _core?.IsBuffActive ?? false;
        }

        protected override void OnTick(double delta)
        {
            if (_core == null || Actor == null) return;

            bool buffActive = _core.IsBuffActive;
            if (buffActive && !_buffWasActive)
            {
                EnsureSubscribed();
            }
            else if (!buffActive && _buffWasActive)
            {
                // buff 结束：先摘拦截再结算，保证累计伤害走完整结算流程
                Unsubscribe();
                SettlePending();
            }
            _buffWasActive = buffActive;
        }

        private bool OnDamageIntercepted(GameActor.DamageEventArgs args)
        {
            if (args.Target != Actor) return false;
            if (args.Damage <= 0) return false;

            _pendingDamage += args.Damage;
            if (_pendingDamage == args.Damage) // 首次累计：缓存伤害来源
            {
                _lastOrigin = args.AttackOrigin;
            }
            args.Damage = 0; // 吞掉本次伤害（不扣血、不进 Hit、不发布事件）
            return false;
        }

        private void SettlePending()
        {
            if (Actor == null || _pendingDamage <= 0) return;

            int damage = _pendingDamage;
            _pendingDamage = 0;
            Vector2? origin = _lastOrigin;
            _lastOrigin = null;

            // 拦截时已是乘过 IncomingDamageMultiplier 的最终值，结算临时置 1 防二次放大
            float originalMultiplier = Actor.IncomingDamageMultiplier;
            Actor.IncomingDamageMultiplier = 1f;
            // DamageEventArgs 不携带 DamageSource，结算统一按 DirectAttack（不影响数值）
            Actor.TakeDamage(damage, origin, null, DamageSource.DirectAttack);
            Actor.IncomingDamageMultiplier = originalMultiplier;
        }

        private void EnsureSubscribed()
        {
            if (_subscribed || Actor == null) return;
            Actor.DamageIntercepted += OnDamageIntercepted;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Actor == null) return;
            Actor.DamageIntercepted -= OnDamageIntercepted;
            _subscribed = false;
        }

        public override void OnRemoved()
        {
            Unsubscribe();
            base.OnRemoved();
        }
    }
}
