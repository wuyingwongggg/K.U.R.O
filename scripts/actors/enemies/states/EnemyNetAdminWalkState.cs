using Godot;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// NetAdmin 专用 Walk 状态。
    /// 退出 Walk 时先播放 WalkPart 惯性停止收尾动画，完成后再切换状态。
    /// </summary>
    public partial class EnemyNetAdminWalkState : EnemyWalkState
    {
        [Export(PropertyHint.Range, "0.1,2,0.01")]
        public float WalkPartDuration = 0.49f;

        /// <summary>动画控制器读取此标记决定播 WalkPart 还是 WalkLoop。</summary>
        public bool IsStopping { get; private set; }

        private float _stopTimer;
        private string _pendingState = string.Empty;

        public override void Enter()
        {
            IsStopping = false;
            base.Enter();
        }

        public override void PhysicsUpdate(double delta)
        {
            if (IsStopping)
            {
                _stopTimer -= (float)delta;
                Enemy.Velocity = Vector2.Zero;
                if (_stopTimer <= 0f)
                {
                    IsStopping = false;
                    ChangeState(_pendingState);
                }
                return;
            }

            if (!Enemy.IsPlayerWithinDetectionRange())
            {
                BeginStop("Idle");
                return;
            }

            if (Enemy.CanStartAttack())
            {
                BeginStop("Attack");
                return;
            }

            base.PhysicsUpdate(delta);
        }

        private void BeginStop(string nextState)
        {
            IsStopping = true;
            _stopTimer = WalkPartDuration;
            _pendingState = nextState;
        }
    }
}
