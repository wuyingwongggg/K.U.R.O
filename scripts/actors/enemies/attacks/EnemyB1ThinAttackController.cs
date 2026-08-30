namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_B1_thin 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复。
    /// 技能名默认 KickAttack（场景未配置时生效）。
    /// </summary>
    public partial class EnemyB1ThinAttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyB1ThinAttackController()
        {
            SkillAttackName = "KickAttack";
        }
    }
}
