using Godot;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 玩家死亡过渡状态，用于播放死亡动画/倒地时间。
    /// </summary>
    public partial class PlayerDyingState : PlayerState
    {
        public float DeathDuration = 1.0f;
        public bool FreezeMotion = true;
        public float DyingAnimationSpeed = 1.0f;
        private float _originalSpeedScale = 1.0f;

        private float _timer;

        public override void Enter()
        {
            _timer = DeathDuration;
            Player.AttackTimer = 0f;
            
            // Save original speed scale before modifying
            if (Actor.AnimPlayer != null)
            {
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                // Set animation playback speed only for dying animation
                Actor.AnimPlayer.SpeedScale = DyingAnimationSpeed;
            }

            if (FreezeMotion)
            {
                Player.Velocity = Vector2.Zero;
                Player.MoveAndSlide();
            }
        }
        
        public override void Exit()
        {
            // Restore original animation speed when leaving dying state
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            if (FreezeMotion)
            {
                Player.Velocity = Player.Velocity.MoveToward(Vector2.Zero, Player.Speed * 2f * (float)delta);
                Player.MoveAndSlide();
            }

            _timer -= (float)delta;
            if (_timer <= 0f)
            {
                ChangeState("Dead");
            }
        }
    }
}


