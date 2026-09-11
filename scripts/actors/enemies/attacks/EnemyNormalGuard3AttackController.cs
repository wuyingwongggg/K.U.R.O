using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_Normal_guard3 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复。
    /// 技能名默认 ThrowAttack（场景未配置时生效）。
    /// </summary>
    public partial class EnemyNormalGuard3AttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyNormalGuard3AttackController()
        {
            SkillAttackName = "ThrowAttack";
        }
    }
}
