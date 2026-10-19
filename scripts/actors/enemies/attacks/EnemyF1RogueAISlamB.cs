using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// RogueAISlamB（普攻·潜行砸地）：与 RogueAISlamA 同参数、同权重并列（由控制器 50/50 随机），
    /// 唯一差异是可见性——**Warmup 开始即把整个敌人隐藏（Visible = false），Recovery 结束（攻击真正收尾）才恢复**：
    /// 出手不可见（配合砸地的落点特效表现"从暗中出手"），收招现身。
    /// 被打断/取消同样走 OnAttackFinished → 必定恢复可见，不会把敌人留在隐身态。
    /// </summary>
    public partial class EnemyF1RogueAISlamB : EnemySimpleMeleeAttack
    {
        protected override void OnWarmupStarted()
        {
            base.OnWarmupStarted();
            if (Enemy != null)
                Enemy.Visible = false;
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            if (Enemy != null && GodotObject.IsInstanceValid(Enemy))
                Enemy.Visible = true;
        }
    }
}
