using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 爆发推力：攻击命中（DirectAttack 伤害判定）时自动释放核心技能，
    /// 该次命中即为释放后的首次攻击，造成额外击退（距离）并提升该次伤害
    /// （追加伤害，不触发暴击等武器词条）。每个 buff 周期只触发一次。
    /// 与武器前摇时长解耦：无论前摇长短，释放时机永远紧跟命中瞬间。
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

            // buff 开始（含手动按键释放）→ 武装首次攻击
            bool buffActive = _core.IsBuffActive;
            if (buffActive && !_buffWasActive)
                _armed = true;
            _buffWasActive = buffActive;
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (Actor == null || attacker != Actor) return;
            // 防御：当前事件流不会传出 null target，但保留判空避免未来路径变更时
            // 崩溃或"首次攻击"名额被无效目标消耗
            if (target == null) return;

            // 伤害判定时自动释放核心技能（受释放 CD 与热量约束）。
            // _originalAttackDamage 由机器核从每帧缓存的基础攻击力捕获，
            // 不受命中检测临时 DamageOverride 污染，因此可以同步释放。
            // 本次命中触发的新释放 → 本次命中即为释放后首次攻击，立即武装
            bool triggeredReleaseByThisHit = false;
            if (_core != null)
            {
                bool wasBuffActive = _core.IsBuffActive;
                _core.TryReleaseCoreSkill();
                if (!wasBuffActive && _core.IsBuffActive)
                {
                    _armed = true;
                    // 同步武装后立即同步 buff 状态，抑制 OnTick 的跳变检测再次武装（防双重触发）
                    _buffWasActive = true;
                    triggeredReleaseByThisHit = true;

                    // 帧末重新应用攻击力加成：命中检测的 finally 已把 AttackDamage 还原为检测开始值，
                    // 重新写回加成，确保下一次攻击的 DamageOverride 捕获到加成后的攻击力
                    var core = _core;
                    Callable.From(() =>
                    {
                        if (core != null && GodotObject.IsInstanceValid(core))
                            core.RefreshReleaseDamageBonus();
                    }).CallDeferred();
                }
            }

            if (!_armed || _core == null || !_core.IsBuffActive) return;

            _armed = false; // 每 buff 周期只触发一次

            // 伤害提升：追加 bonus% 伤害（EffectBonus 来源，不触发暴击等武器词条）。
            // 本段命中触发了释放：本段伤害基于释放前的 DamageOverride，
            // 把核心释放的攻击力加成量补回；卡牌百分比基于补回后的有效伤害计算，
            // 保证与"手动释放后攻击"的结果一致（如 10 伤害 + 10 加成 → 20×10% = 2）
            int coreBonus = triggeredReleaseByThisHit ? Mathf.RoundToInt(_core.CurrentReleaseDamageBonus) : 0;
            int bonus = Mathf.RoundToInt((damage + coreBonus) * CurrentDamageBonus / 100f) + coreBonus;
            if (bonus > 0)
                target.TakeDamage(bonus, Actor.GlobalPosition, Actor, DamageSource.EffectBonus);

            // 击退：距离 → 初速度（线性减速总位移 = v0×T/2，故 v0 = 2×距离/时长），
            // 用 KnockbackDriver 累加式驱动（与武器自带击退叠加，总位移相加），
            // 每帧 MoveAndCollide + 清零 Velocity，后续普通攻击无法打断
            if (CurrentKnockback <= 0f) return;
            if (target is not CharacterBody2D targetBody) return;
            if (target.ActiveImmunities.HasFlag(ImmunityFlags.ForcedMovement)) return;
            Vector2 dir = target.GlobalPosition - Actor.GlobalPosition;
            if (dir == Vector2.Zero) dir = Vector2.Right;
            KnockbackDriver.AttachStack(targetBody, dir.Normalized(),
                2f * CurrentKnockback / Mathf.Max(KnockbackDuration, 0.01f), KnockbackDuration);
        }
    }
}
