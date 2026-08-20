using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 冻结状态：玩家被控制时无法移动或执行输入，直到持续时间结束。
    /// </summary>
    public partial class PlayerFrozenState : PlayerState
    {
        /// <summary>
        /// 进入守卫：免疫眩晕（ImmunityFlags.Stun）时拒绝进入冻结状态——
        /// 统一拦截所有直接 ChangeState("Frozen") 的攻击
        /// （EnemySmashAttack / EnemyChargeGrabAttack 等不走 FreezeEffect 的路径）。
        /// </summary>
        public override bool CanEnterFrom(string? currentStateName)
        {
            if (Actor is GameActor actor && actor.ActiveImmunities.HasFlag(ImmunityFlags.Stun))
            {
                return false;
            }

            return base.CanEnterFrom(currentStateName);
        }

        public float FrozenDuration = 2.0f;
        public float FrozenAnimationSpeed = 1.0f;
        [Export] public string SpineFrozenAnimationName = "stun";
        [Export] public bool AllowExternalDisplacementWhileFrozen = true;

        private float _timer;
        private bool _externallyHeld;
        private float _originalSpeedScale = 1.0f;
        private MainCharacter? _mainCharacter;
        private bool _spineAnimationApplied;
        private bool _allowTransitionOut;
        private Vector2 _externalDisplacementVelocity = Vector2.Zero;
        private float _externalDisplacementTimer;

        public bool IsExternallyHeld => _externallyHeld;
        public float RemainingHoldRatio
        {
            get
            {
                if (FrozenDuration <= Mathf.Epsilon)
                {
                    return 0f;
                }
                return Mathf.Clamp(_timer / FrozenDuration, 0f, 1f);
            }
        }

        public override void Enter()
        {
            _timer = FrozenDuration;
            _externallyHeld = false;
            _allowTransitionOut = false; // 初始时不允许转换到其他状态。
            _externalDisplacementVelocity = Vector2.Zero;
            _externalDisplacementTimer = 0f;
            Actor.Velocity = Vector2.Zero;
            _mainCharacter = Actor as MainCharacter;
            _spineAnimationApplied = false;

            if (_mainCharacter != null)
            {
                var animName = string.IsNullOrEmpty(SpineFrozenAnimationName)
                    ? _mainCharacter.IdleAnimationName
                    : SpineFrozenAnimationName;
                _mainCharacter.PlaySpineAnimation(animName, true, FrozenAnimationSpeed);
                _spineAnimationApplied = true;
            }
            else if (Actor.AnimPlayer != null)
            {
                // Save original speed scale before modifying
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                
                if (Actor.AnimPlayer.HasAnimation("animations/hit"))
                {
                    Actor.AnimPlayer.Play("animations/hit");
                }
                else
                {
                    Actor.AnimPlayer.Play("animations/Idle");
                }
                // Set animation playback speed only for frozen animation
                Actor.AnimPlayer.SpeedScale = FrozenAnimationSpeed;
            }
        }
        
        public override void Exit()
        {
            // Restore original animation speed when leaving frozen state
            if (_spineAnimationApplied)
            {
                // State 离开时交由下一个状态控制 Spine 动画，无需额外处理
            }
            else if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            if (AllowExternalDisplacementWhileFrozen && _externalDisplacementTimer > 0f)
            {
                _externalDisplacementTimer -= (float)delta;
                Actor.Velocity = _externalDisplacementVelocity;

                if (_externalDisplacementTimer <= 0f)
                {
                    _externalDisplacementTimer = 0f;
                    _externalDisplacementVelocity = Vector2.Zero;
                    Actor.Velocity = Vector2.Zero;
                }
            }
            else
            {
                Actor.Velocity = Vector2.Zero;
            }

            Actor.MoveAndSlide();

            if (_externallyHeld)
            {
                return;
            }

            _timer -= (float)delta;
            if (_timer <= 0)
            {
                _allowTransitionOut = true; // 允许转换到其他状态（如 Idle），除非被外部控制打断。
                ChangeState("Idle");
            }
        }
    
        public override bool CanExitTo(string nextStateName)
        {
            if (nextStateName == "Dying" || nextStateName == "Dead")
            {
                return true;
            }

            if (nextStateName == "Frozen")
            {
                return true;
            }

            return _allowTransitionOut && nextStateName == "Idle";
        }

        public void BeginExternalHold()
        {
            _timer = FrozenDuration;
            _externallyHeld = true;
            _allowTransitionOut = false;
        }

        public void EndExternalHold()
        {
            _externallyHeld = false;
			_timer = 0f;
			_allowTransitionOut = true;
			ChangeState("Idle");
        }

        public void ApplyExternalDisplacement(Vector2 velocity, float duration)
        {
            if (!AllowExternalDisplacementWhileFrozen)
            {
                return;
            }

            float clampedDuration = Mathf.Max(duration, 0f);
            if (clampedDuration <= 0f || velocity == Vector2.Zero)
            {
                return;
            }

            _externalDisplacementVelocity = velocity;
            _externalDisplacementTimer = Mathf.Max(_externalDisplacementTimer, clampedDuration);
        }
    }
}

