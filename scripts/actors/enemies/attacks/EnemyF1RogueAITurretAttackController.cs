namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// F1 炮台族攻击控制器（机枪台 / 炮台共用）：疲劳权重法（继承基类，与 guard4/guard5 同款）。
    /// Melee = 贴脸近战（节点 "MeleeAttack"，<see cref="EnemyF1RogueAITurretMeleeAttack"/>）；
    /// 另一招（扫射/开火 SimpleMeleeAttack）不占"技能"槽，SkillAttackName 留空。
    ///
    /// 换用疲劳基类带来的收益：连续同招递减权重（防"连招重复"）、
    /// 排队超时几何降权（选中的招够不着时自动让位——近战在远处被选中也能优雅退场）、
    /// 被打断还原权重（不长期停留在降权值）。
    /// </summary>
    public partial class EnemyF1RogueAITurretAttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyF1RogueAITurretAttackController()
        {
            MeleeAttackName = "MeleeAttack";
        }
    }
}
