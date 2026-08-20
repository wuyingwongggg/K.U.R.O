using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 濒死守护（BuildNormal_A_006）：受到致死伤害时保留 1 点生命并进入短暂无敌，冷却 CooldownSeconds。
    /// 实现：DamageIntercepted 钩子在伤害结算前把致死伤害 clamp 到"剩余血量-1"（走正常结算扣到 1 血），
    /// 并启动无敌。冷却期内致死伤害正常结算（死亡）。
    /// </summary>
    [GlobalClass]
    public partial class NormalDeathDefyEffect : ActorEffect
    {
        /// <summary>触发冷却（秒）：生效后冷却期内不再保命。</summary>
        [Export(PropertyHint.Range, "5,180,5")] public float CooldownSeconds { get; set; } = 60f;
        /// <summary>保命后的无敌时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float InvincibilityDuration { get; set; } = 1f;

        private float _cooldownTimer;

        protected override void OnApply()
        {
            _cooldownTimer = 0f;
            if (Actor != null)
            {
                Actor.DamageIntercepted += OnDamageIntercepted;
            }
        }

        public override void OnRemoved()
        {
            if (Actor != null)
            {
                Actor.DamageIntercepted -= OnDamageIntercepted;
            }
            base.OnRemoved();
        }

        protected override void OnTick(double delta)
        {
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= (float)delta;
            }
        }

        private bool OnDamageIntercepted(GameActor.DamageEventArgs args)
        {
            if (Actor == null || args.Target != Actor) return false;
            if (_cooldownTimer > 0f || args.Damage <= 0) return false;

            // 非致死伤害：不干预
            if (args.Damage < Actor.CurrentHealth) return false;

            // 致死 → 伤害 clamp 到剩 1 血（走正常结算扣血到 1，不触发死亡），启动无敌，进入冷却
            args.Damage = Mathf.Max(0, Actor.CurrentHealth - 1);
            _cooldownTimer = CooldownSeconds;

            if (Actor is MainCharacter main)
            {
                main.StartHitInvincibility(InvincibilityDuration);
            }

            return false; // 不整体拦截，让 clamp 后的伤害正常结算
        }
    }
}
