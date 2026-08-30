using Godot;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 暴乱护臂攻击 —— 基础近战攻击 + 前冲位移。
    ///
    /// 重做版（新冲刺攻击系统）：不再依赖 Dash 状态——DashAttackOnlyFromDash=false，
    /// 任何来源（Run/Walk/Idle）都触发冲刺攻击路径（Dash 动画/阶段时长/位移模型）。
    /// 速度与衰减用新配置体系调：
    ///   - 速度来源：默认 Inherit——最近冲刺过则继承 Burst 峰值快照，否则继承实时移动速度
    ///     （技能字段 DashAttackSpeedSource=Fixed + DashAttackFixedSpeed 可钉死固定速度）
    ///   - 衰减窗口：DashAttackDecayWindow（None=匀速前冲，原 Brawl 手感；默认回退旧 Warmup 衰减）
    /// 继承 PlayerBasicMeleeAttack 的全部近战逻辑（伤害、动画、命中检测）。
    /// </summary>
    public partial class PlayerBrawlRiotBracerAttack : PlayerBasicMeleeAttack
    {
        public PlayerBrawlRiotBracerAttack()
        {
            // 任何来源都触发冲刺攻击（无需进入 Dash 状态）
            DashAttackOnlyFromDash = false;
        }
    }
}
