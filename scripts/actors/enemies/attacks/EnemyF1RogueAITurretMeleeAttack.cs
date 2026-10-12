using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// F1 炮台族近战招（贴脸惩罚，机枪台 / 炮台共用）：玩家进入 <see cref="EnemyAttackTemplate.TriggerAreaPath"/>
    /// 触发区才起手——**蓄力 Warmup（时长由场景配置，当前 0.6s）到点（进 Active 的瞬间）对攻击区
    /// （AttackAreaPath）内的目标结算一次伤害**，并按 KnockbackDistance/Duration 施加击退
    /// （击退为基础模板**不自动**的部分，见 OnActivePhase 说明）。
    /// 不依赖动画事件（占位动画没有 hit 事件），因此 RequireAnimationHitTrigger 保持关闭——
    /// 基类 OnActivePhase 在该开关关闭时自动 PerformAttackNow() 结算伤害，本类在其之后追加击退。
    ///
    /// 与另一招（扫射/开火 SimpleMeleeAttack）共存的选招约定：本招的**可选中性**（IsPlayerInDetectionRange）
    /// 与起手条件都要求玩家在触发区内——玩家不在近处时本招根本不进加权随机池，避免"随机选中却永远起不了手"
    /// 卡队列（控制器还有 QueuedAttackTimeout 兜底）。
    ///
    /// 动画：两方向近战片尚未制作，暂由动画控制器用 turn / d_turn 占位
    /// （朝左 = turn、朝右 = d_turn——占位选片与骨架常规 d_ 镜像规则相反，按出招收势朝向取）。
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

        /// <summary>蓄力到点：基类先自动结算伤害（RequireAnimationHitTrigger=false），随后显式施加击退——
        /// **基础模板不会自动击退**，击退必须由子类在结算处调用（guard4/guard5 同款约定）。
        /// 门与伤害一致（玩家在伤害区内 + KnockbackDistance>0）；玩家无敌帧/格挡时由
        /// ApplyKnockbackDisplacement 内部自行拒绝（EFFECT_STANDARD 第四条），此处不重复判断。
        /// 顺序：伤害先行（受击管线置位击退放行标记）→ 再喂击退请求，同一帧内完成。</summary>
        protected override void OnActivePhase()
        {
            base.OnActivePhase();

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
