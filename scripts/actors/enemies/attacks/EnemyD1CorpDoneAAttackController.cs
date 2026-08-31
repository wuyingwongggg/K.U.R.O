using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_D1_corpDoneA 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复；
    /// 超时降权/打断恢复由基类自动生效。三个攻击（近战/弹球/封锁）由 attack_weight 自动收集。
    /// </summary>
    public partial class EnemyD1CorpDoneAAttackController : EnemyFatigueAttackControllerBase
    {
        /// <summary>兼容动画控制器（EnemyD1CorpDoneASpineAnimationController）：弹球攻击名（动画区分用）。</summary>
        [Export] public string PinballAttackName { get; set; } = "PinballAttack";

        /// <summary>兼容动画控制器：封锁攻击名（动画区分用）。</summary>
        [Export] public string LockdownAttackName { get; set; } = "LockdownAttack";

        public EnemyD1CorpDoneAAttackController()
        {
            SkillAttackName = "PinballAttack";
        }
    }
}
