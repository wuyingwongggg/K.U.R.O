using Godot;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 暴乱护臂攻击 —— 基础近战攻击 + 前冲位移。
    ///
    /// 移动模式由基类 EnableDashMovement 提供（参照 PlayerDashState）：
    ///   Warmup  → 不能移动（状态机进入攻击时清零速度）
    ///   Active  → 固定 DashSpeed 匀速前冲（ShouldStartDashInWarmup=false 起步于 Active；ShouldDecayDashSpeed=false 不衰减）
    ///   Recovery → RecoverySpeed 低速滑行
    ///   碰敌归零 → 基类 ContactShapePath 统一检测（场景配置 AttackArea 形状）
    ///
    /// 继承 PlayerBasicMeleeAttack 的全部近战逻辑（伤害、动画、命中检测）。
    /// </summary>
    public partial class PlayerBrawlRiotBracerAttack : PlayerBasicMeleeAttack
    {
        /// <summary>Active 阶段固定前冲速度（像素/秒）。Brawl 原语义：冲刺始终用此固定值，不受攻击前玩家速度影响。</summary>
        [Export(PropertyHint.Range, "100,8000,10")] public float DashSpeed = 2500f;

        /// <summary>起步速度：固定 DashSpeed（Brawl 原语义——冲刺不被攻击前玩家速度重载）。</summary>
        protected override float ResolveDashStartSpeed()
        {
            return DashSpeed;
        }

        /// <summary>Brawl 原语义：Warmup 停 → Active 开始冲刺（阶段前冲模式，不走 Warmup 惯性起步）。</summary>
        protected override bool ShouldStartDashInWarmup => false;

        /// <summary>Brawl 原语义：Active 全程匀速冲刺（不衰减），Recovery 滑行。</summary>
        protected override bool ShouldDecayDashSpeed => false;
    }
}
