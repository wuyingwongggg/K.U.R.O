using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_B2_fat02 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复。
    /// 技能名默认 SmashAttack（场景未配置时生效）。
    /// </summary>
    public partial class EnemyB2Fat02AttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyB2Fat02AttackController()
        {
            SkillAttackName = "SmashAttack";
        }
    }
}
