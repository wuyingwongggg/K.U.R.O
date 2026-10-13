using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// F1 炮台族近战招（贴脸惩罚，机枪台 / 炮台共用）：玩家进入 <see cref="EnemyAttackTemplate.TriggerAreaPath"/>
    /// 触发区才起手——蓄力 Warmup（时长由场景配置）到点后对伤害区（DamageAreaPath）内的目标结算伤害，
    /// 并按 KnockbackDistance/Duration 施加击退。伤害有两种配法（场景二选一，脚本两种都支持）：
    ///   · **hit 帧驱动**（RequireAnimationHitTrigger = true + AllowMultipleAnimationHits）：spine `skill` 片的
    ///     hit 事件到达时结算（Cannon 当前配法：Warmup 0.8、事件 @0.85 落在 Active 0.8~0.95 窗口内），
    ///     可一次攻击多段命中；
    ///   · **到点结算**（开关关闭）：进 Active 的瞬间自动 PerformAttackNow()（机枪台当前配法）。
    /// 击退必须**晚于伤害**喂：受击管线的"待处理击退标记"只在伤害真落地时置位，提前喂会被
    /// TryApplyPlayerKnockback 直接拒绝——所以到点结算在 OnActivePhase 喂、hit 帧驱动在 OnAnimationHit 喂。
    /// 基础模板**不自动**击退，这是子类的职责。
    ///
    /// 与另一招（扫射/开火 SimpleMeleeAttack）共存的选招约定：本招的**可选中性**（IsPlayerInDetectionRange）
    /// 与起手条件都要求玩家在触发区内——玩家不在近处时本招根本不进加权随机池，避免"随机选中却永远起不了手"
    /// 卡队列（控制器还有 QueuedAttackTimeout 兜底）。
    ///
    /// 动画：出手播 spine `skill` 片（骨架尚无 `d_skill`，朝左由动画控制器自动回退基名）。
    /// </summary>
    public partial class EnemyF1RogueAITurretMeleeAttack : EnemyAttackTemplate
    {
        /// <summary>起手条件：基类（检测圈 + 朝向角）之上再要求玩家在触发区里。</summary>
        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            return IsPlayerInTriggerArea();
        }

        /// <summary>选招资格同绑触发区：玩家不在近战区时本招不参与加权随机（防止排队在远处永远起不了手）。</summary>
        public override bool IsPlayerInDetectionRange()
        {
            if (!base.IsPlayerInDetectionRange()) return false;
            return IsPlayerInTriggerArea();
        }

        /// <summary>蓄力到点：**到点结算**配法下基类刚在 OnActivePhase 里结算完伤害 → 此刻喂击退；
        /// **hit 帧驱动**配法下此刻还没有伤害（"待处理击退标记"未置位，喂了也会被拒）→ 击退改由
        /// <see cref="OnAnimationHit"/> 在 hit 帧补喂。</summary>
        protected override void OnActivePhase()
        {
            base.OnActivePhase();

            if (RequireAnimationHitTrigger) return;
            ApplyKnockbackIfHit();
        }

        /// <summary>hit 帧驱动配法：base.OnAnimationHit 刚结算完伤害（放行标记已置位），此刻补喂击退。</summary>
        protected override void OnAnimationHit()
        {
            base.OnAnimationHit();
            ApplyKnockbackIfHit();
        }

        /// <summary>与伤害同门的击退喂送（玩家在伤害区内 + KnockbackDistance>0）；玩家无敌帧/格挡时由
        /// 受击管线自行拒绝（EFFECT_STANDARD 第四条），此处不重复判断。必须在伤害结算之后调用。</summary>
        private void ApplyKnockbackIfHit()
        {
            float distance = Mathf.Max(0f, KnockbackDistance);
            if (distance <= 0f) return;
            if (Enemy?.PlayerTarget is not SamplePlayer player) return;

            var hitArea = DamageArea ?? AttackArea;
            if (hitArea == null || !player.IsHitByArea(hitArea)) return;

            TryApplyPlayerKnockback(player, distance, Mathf.Max(KnockbackDuration, 0.01f),
                Enemy.FacingRight ? Vector2.Right : Vector2.Left);
        }
    }
}
