using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_Normal_guard1 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复。
    /// 技能名默认 OnePunchAttack（场景未配置时生效）。
    /// </summary>
    public partial class EnemyNormalGuard1AttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyNormalGuard1AttackController()
        {
            SkillAttackName = "OnePunchAttack";
        }
    }
}
