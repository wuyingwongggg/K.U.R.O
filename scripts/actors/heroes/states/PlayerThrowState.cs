using Godot;
using Kuros.Items.World;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 播放投掷动画，动画结束后才真正投掷物品。
    /// </summary>
    public partial class PlayerThrowState : PlayerState
    {
        public string ThrowAnimation = "animations/throw";
        public float ThrowAnimationSpeed = 1.0f;

        private PlayerItemInteractionComponent? _interaction;
        private bool _hasRequestedThrow;
        private bool _animationFinished;
        private float _animRemaining;
        private float _originalSpeedScale = 1.0f;

        protected override void _ReadyState()
        {
            base._ReadyState();
            _interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
        }

        public override void Enter()
        {
            if (_interaction == null)
            {
                ChangeState("Idle");
                return;
            }

            Player.Velocity = Vector2.Zero;
            _hasRequestedThrow = false;
            _animationFinished = false;
            PlayAnimation();
        }

        public override void Exit()
        {
            base.Exit();
            _hasRequestedThrow = false;
            
            // Restore original animation speed when leaving throw state
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            if (_interaction == null)
            {
                ChangeState("Idle");
                return;
            }

            UpdateAnimationState();

            if (_animationFinished && !_hasRequestedThrow)
            {
                if (_interaction.TryTriggerThrowAfterAnimation())
                {
                    _hasRequestedThrow = true;
                }
            }

            if (_hasRequestedThrow)
            {
                ChangeState("Idle");
            }
        }

        private void PlayAnimation()
        {
            if (Actor.AnimPlayer != null && Actor.AnimPlayer.HasAnimation(ThrowAnimation))
            {
                // Save original speed scale before modifying
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                
                Actor.AnimPlayer.Play(ThrowAnimation);
                // Set animation playback speed only for throw animation
                Actor.AnimPlayer.SpeedScale = ThrowAnimationSpeed;

                var speed = Mathf.Max(Actor.AnimPlayer.SpeedScale, 0.0001f);
                _animRemaining = (float)Actor.AnimPlayer.CurrentAnimationLength / speed;
            }
            else
            {
                _animationFinished = true;
            }
        }

        private void UpdateAnimationState()
        {
            if (_animationFinished || Actor.AnimPlayer == null)
            {
                return;
            }

            _animRemaining -= (float)GetPhysicsProcessDeltaTime();
            if (_animRemaining <= 0f || !Actor.AnimPlayer.IsPlaying())
            {
                _animationFinished = true;
            }
        }
    }
}

