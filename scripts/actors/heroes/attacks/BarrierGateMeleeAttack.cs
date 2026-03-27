namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 栅栏门专属近战模板。
    /// 仅承载栅栏门的行为差异；动画、冷却、连击次数等数据配置统一来自 WeaponSkillDefinition。
    /// </summary>
    public partial class BarrierGateMeleeAttack : PlayerBasicMeleeAttack
    {
        // 当前版本暂无栅栏门专属行为差异。
        // 该模板保留为专属扩展点，后续若需要实现冲撞击退、范围格挡、破防效果等逻辑，可在此覆盖。
    }
}
