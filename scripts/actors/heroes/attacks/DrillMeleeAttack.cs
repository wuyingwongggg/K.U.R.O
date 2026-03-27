namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 钻头专属近战模板。
    /// 仅承载钻头的行为差异；动画、冷却、连击次数等数据配置统一来自 WeaponSkillDefinition。
    /// </summary>
    public partial class DrillMeleeAttack : PlayerBasicMeleeAttack
    {
        // 当前版本暂无钻头专属行为差异。
        // 该模板保留为专属扩展点，后续若需要实现旋转穿刺位移、多段命中修正、破甲加成等逻辑，可在此覆盖。
    }
}
