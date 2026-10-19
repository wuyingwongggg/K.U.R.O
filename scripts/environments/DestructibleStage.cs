using Godot;

namespace Kuros.Environments
{
    /// <summary>
    /// 多阶段受损物的**一段**配置（供 <see cref="DestructibleStageController.Stages"/> 用）：
    /// 一段 = "常态循环 + 受击反馈 + 破壳动画 + 累计伤害阈值"。
    /// </summary>
    [GlobalClass]
    public partial class DestructibleStage : Resource
    {
        /// <summary>本段常态循环动画。</summary>
        [Export] public string IdleAnimation { get; set; } = "";

        /// <summary>本段受击一次性动画。**空 = 受击不播动画、不锁**（如"过脆的末段"：任何攻击都会直接清空，
        /// 不需要受击反馈，伤害照常累计、下一击即销毁）。</summary>
        [Export] public string HitAnimation { get; set; } = "";

        /// <summary>离开本段时播的一次性动画（累计伤害 ≥ <see cref="Threshold"/> 时）。
        /// 最后一段用不到（离开最后一段 = 终态，播控制器的 DeathAnimation）。空 = 不播动画直接进入下一段。</summary>
        [Export] public string BreakAnimation { get; set; } = "";

        /// <summary>累计伤害阈值：**从满血起累计** ≥ 本值时离开本段（进入下一段 / 最后一段则进终态）。
        /// 跨段那一击的溢出伤害作废（累计钳到本值）——单次受击最多推进一段，任何单次伤害都不可能跳过中间动画。</summary>
        [Export(PropertyHint.Range, "0,10000,1")] public float Threshold { get; set; } = 100f;
    }
}
