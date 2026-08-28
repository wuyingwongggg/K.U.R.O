using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_B1_fat 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复。
    /// 技能名默认 ChargeEscapeAttack（场景未配置时生效）。
    /// </summary>
    public partial class EnemyB1FatAttackController : EnemyFatigueAttackControllerBase
    {
        /// <summary>兼容旧动画控制器（EnemyB1FatSpineAnimationController）的 Skill1AttackName 引用：同步到基类 SkillAttackName。</summary>
        [Export] public string Skill1AttackName
        {
            get => SkillAttackName;
            set => SkillAttackName = value;
        }

        public EnemyB1FatAttackController()
        {
            Skill1AttackName = "ChargeEscapeAttack";
        }
    }
}
