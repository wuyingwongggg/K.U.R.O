using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_C2_waiterA02 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复。
    /// 技能名默认 WheelAttack（场景未配置时生效）。
    /// </summary>
    public partial class EnemyC2WaiterA02AttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyC2WaiterA02AttackController()
        {
            SkillAttackName = "WheelAttack";
        }
    }
}
