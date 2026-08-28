using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_Normal_guard2 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复。
    /// 技能名默认 MoveAttack（场景未配置时生效）。
    /// </summary>
    public partial class EnemyNormalGuard2AttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyNormalGuard2AttackController()
        {
            SkillAttackName = "MoveAttack";
        }
    }
}
