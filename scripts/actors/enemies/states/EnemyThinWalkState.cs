using Godot;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyThinWalkState : EnemyWalkState
    {
        public override void PhysicsUpdate(double delta)
        {
            if (Enemy.StateMachine?.HasState("EnemySpawn") == true && ShouldEnterEnemySpawnState())
            {
                ChangeState("EnemySpawn");
                return;
            }

            // DashBack 冷却门:冷却期 CanEnterFrom 拒绝进入——若此处仍无条件 return,
            // 每帧"尝试切换→被拒→跳过基类",敌人卡死在检测(不移动/不进攻击)
            var dashBack = Enemy.StateMachine?.GetNodeOrNull<EnemyDashBackState>("DashBack");
            if (dashBack != null
                && dashBack.CanEnterFrom(Enemy.StateMachine?.CurrentState?.Name)
                && Enemy.IsPlayerAttacking() && Enemy.IsInsidePlayerAttackArea())
            {
                ChangeState("DashBack");
                return;
            }

            base.PhysicsUpdate(delta);
        }

        private bool ShouldEnterEnemySpawnState()
        {
            var spawnState = Enemy.StateMachine?.GetNodeOrNull<EnemySpawnState>("EnemySpawn");
            return spawnState?.ShouldTriggerOnLowHealth() == true;
        }
    }
}
