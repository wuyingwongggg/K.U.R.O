using Godot;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 播放拾取动画，动画结束后执行拾取逻辑。
    /// </summary>
    public partial class PlayerPickUpState : PlayerState
    {
        public string PickAnimation = "animations/pickup";
        public float PickUpAnimationSpeed = 1.0f;

        private PlayerItemInteractionComponent? _interaction;
        private float _animRemaining;
        private bool _animationFinished;
        private float _originalSpeedScale = 1.0f;

        protected override void _ReadyState()
        {
            base._ReadyState();
            _interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
        }

        public override void Enter()
        {
            Player.Velocity = Vector2.Zero;
            _animationFinished = false;

            // 无论动画是否存在，只要有 AnimPlayer 就立即缓存当前 SpeedScale，
            // 避免 Exit 时错误地恢复为硬编码的 1.0f。
            if (Actor.AnimPlayer != null)
            {
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
            }

            PlayAnimation();
        }
        
        public override void Exit()
        {
            // Restore original animation speed when leaving pick up state
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            UpdateAnimationState(delta);

            if (_animationFinished)
            {
                _interaction?.ExecutePickupAfterAnimation();
                ChangeState("Idle");
            }
        }

        private void PlayAnimation()
        {
            if (Actor.AnimPlayer != null && Actor.AnimPlayer.HasAnimation(PickAnimation))
            {
                Actor.AnimPlayer.Play(PickAnimation);
                // Set animation playback speed only for pick up animation
                Actor.AnimPlayer.SpeedScale = PickUpAnimationSpeed;
                var speed = Mathf.Max(PickUpAnimationSpeed, 0.0001f);
                _animRemaining = (float)Actor.AnimPlayer.CurrentAnimationLength / speed;
            }
            else
            {
                _animationFinished = true;
            }
        }

        private void UpdateAnimationState(double delta)
        {
            if (_animationFinished || Actor.AnimPlayer == null)
            {
                return;
            }

            _animRemaining -= (float)delta;
            if (_animRemaining <= 0f || !Actor.AnimPlayer.IsPlaying())
            {
                _animationFinished = true;
            }
        }
    }
}

