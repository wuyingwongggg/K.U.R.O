using Godot;
using Kuros.Items.World;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 投掷状态：三阶段（Warmup 蓄力 → Active 出手 → Recovery 后摇），
    /// Warmup 结束触发投掷（TryTriggerThrowAfterAnimation），动画播完后切 IdleHolding/Idle。
    /// 闪避取消：Warmup（未出手）/Recovery（后摇）阶段可按闪避取消投掷；Active（出手中）不可打断。
    /// </summary>
    public partial class PlayerThrowState : PlayerState
    {
        private enum ThrowPhase { Warmup, Active, Recovery }

        public string ThrowAnimation = "throw_holding_item";
        public float ThrowAnimationSpeed = 1f;
        /// <summary>蓄力时长（秒）：Warmup 结束后触发投掷。</summary>
        [Export(PropertyHint.Range, "0,2,0.01")] public float ThrowWarmupDuration = 0.3f;
        /// <summary>出手时长（秒）：投掷触发后的出手保护窗口（不可闪避打断）。</summary>
        [Export(PropertyHint.Range, "0,1,0.01")] public float ThrowActiveDuration = 0.05f;
        /// <summary>后摇时长（秒）：Recovery 可闪避取消；动画播完即结束（不受此值限制）。</summary>
        [Export(PropertyHint.Range, "0,2,0.01")] public float ThrowRecoveryDuration = 0.29f;
        public float ThrowAnimationTotalTime = 0.64f;  // 动画总时长（与三阶段之和一致）

        [ExportGroup("Throw Momentum")]
        /// <summary>投掷惯性（类似攻击模板 EnableDashMovement）：进入投掷时保留玩家当前速度，Warmup 阶段线性衰减，Active 开始速度归零。</summary>
        [Export] public bool EnableThrowMomentum = true;
        /// <summary>投掷起步速度倍率（当前移动速度的 N%）。100 = 不变，0 = 无惯性。</summary>
        [Export(PropertyHint.Range, "0,300,0.01")] public float ThrowMomentumSpeedPercent = 100f;

        private PlayerItemInteractionComponent? _interaction;
        private bool _hasRequestedThrow;
        private bool _animationFinished;
        private ThrowPhase _phase;
        private float _phaseRemaining;
        private float _animRemaining;
        private float _originalSpeedScale = 1.0f;
        private float _momentumSpeed;      // 投掷起步速度（Enter 时捕获当前移动速度）
        private Vector2 _momentumDir;      // 投掷移动方向（投掷前移动方向/面朝）
        private float _momentumElapsed;    // Warmup 衰减计时

        protected override void _ReadyState()
        {
            base._ReadyState();
            _interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
        }

        public override void Enter()
        {
            if (_interaction == null)
            {
                GD.PrintErr($"[PlayerThrowState] ItemInteraction 不存在，无法进行投掷");
                ChangeState("Idle");
                return;
            }

            Player.Velocity = Vector2.Zero;
            _hasRequestedThrow = false;
            _animationFinished = false;
            _phase = ThrowPhase.Warmup;
            _phaseRemaining = ThrowWarmupDuration;
            PlayThrowAnimation();

            // 投掷开始：标记投掷物未出手（ItemHoldingAttachment 显示投掷物）
            Player.GetNodeOrNull<PlayerItemAttachment>("ItemHoldingAttachment")?.SetThrowInProgress(true);

            // 投掷惯性：保留玩家当前移动速度（CurrentMoveSpeed——移动状态写入），Warmup 内衰减到 0
            _momentumSpeed = Player.CurrentMoveSpeed * (ThrowMomentumSpeedPercent / 100f);
            _momentumDir = Player.CurrentMoveDirection != Vector2.Zero
                ? Player.CurrentMoveDirection
                : (Player.FacingRight ? Vector2.Right : Vector2.Left);
            _momentumElapsed = 0f;
        }

        public override void Exit()
        {
            base.Exit();
            _hasRequestedThrow = false;

            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        /// <summary>Warmup/Recovery 可被闪避取消（build 无关——所有投掷默认允许）；Active 出手保护不可打断。</summary>
        public override bool CanExitTo(string nextState)
        {
            if (nextState == "Dash")
            {
                return _phase != ThrowPhase.Active;
            }
            return base.CanExitTo(nextState);
        }

        public override void PhysicsUpdate(double delta)
        {
            if (_interaction == null)
            {
                ChangeState("Idle");
                return;
            }

            // 闪避取消：Warmup（未出手）/Recovery（后摇）阶段按闪避打断投掷
            if (IsActionJustPressed("dash") && _phase != ThrowPhase.Active)
            {
                ChangeState("Dash");
                return;
            }

            UpdateAnimationState();
            UpdatePhase((float)delta);
            UpdateMomentum((float)delta);

            // 动画完整播放完毕后再切换状态
            if (_animationFinished)
            {
                var selectedStack = Player.InventoryComponent?.GetSelectedQuickBarStack();
                if (selectedStack != null && !selectedStack.IsEmpty && selectedStack.Item.IsThrowable && !selectedStack.IsThrowOnCooldown)
                {
                    ChangeState("IdleHolding");
                }
                else
                {
                    ChangeState("Idle");
                }
            }
        }

        /// <summary>
        /// 投掷惯性（类似攻击模板 EnableDashMovement）：Warmup 内从起步速度线性衰减到 0（Active 前归零）——出手时已无位移惯性。
        /// Warmup 阶段跟随移动输入翻转面朝（蓄力期间可转向）——惯性方向同步跟随面朝，投掷出手自动朝新方向。
        /// </summary>
        private void UpdateMomentum(float delta)
        {
            if (!EnableThrowMomentum) return;
            if (Player == null) return;

            if (_phase == ThrowPhase.Warmup)
            {
                // Warmup：跟随移动输入翻转面朝（蓄力期间可转向）
                Vector2 moveInput = GetMovementInput();
                if (Mathf.Abs(moveInput.X) > 0.01f)
                    Player.FlipFacing(moveInput.X > 0);

                _momentumElapsed += delta;
                float t = ThrowWarmupDuration > 0f ? Mathf.Clamp(_momentumElapsed / ThrowWarmupDuration, 0f, 1f) : 1f;
                // 惯性方向跟随当前面朝（翻转后投掷/惯性向新方向）
                _momentumDir = Player.FacingRight ? Vector2.Right : Vector2.Left;
                Player.Velocity = _momentumDir * (_momentumSpeed * (1f - t));
            }
            else
            {
                // Active/Recovery：速度 0（Warmup 已衰减完）
                Player.Velocity = Vector2.Zero;
            }

            Player.MoveAndSlide();
            Player.ClampPositionToScreen();
        }

        /// <summary>阶段推进：Warmup 结束触发投掷 → Active 出手保护 → Recovery 后摇（动画播完即结束）。</summary>
        private void UpdatePhase(float delta)
        {
            _phaseRemaining -= delta;
            if (_phaseRemaining > 0f) return;

            switch (_phase)
            {
                case ThrowPhase.Warmup:
                    // 蓄力结束：真正触发投掷（出手）
                    _interaction!.TryTriggerThrowAfterAnimation();
                    _hasRequestedThrow = true;
                    _phase = ThrowPhase.Active;
                    _phaseRemaining = ThrowActiveDuration;
                    break;

                case ThrowPhase.Active:
                    // 出手完成：进入后摇（可闪避取消窗口）
                    _phase = ThrowPhase.Recovery;
                    _phaseRemaining = ThrowRecoveryDuration;
                    break;

                case ThrowPhase.Recovery:
                    // 后摇结束（动画播完由 UpdateAnimationState 置 _animationFinished）
                    _animationFinished = true;
                    break;
            }
        }

        private void PlayThrowAnimation()
        {
            if (Player is MainCharacter mainChar)
            {
                mainChar.PlaySpineAnimation(ThrowAnimation, loop: false, timeScale: ThrowAnimationSpeed);
                _animRemaining = ThrowAnimationTotalTime / ThrowAnimationSpeed;
            }
            else if (Actor.AnimPlayer != null)
            {
                if (Actor.AnimPlayer.HasAnimation(ThrowAnimation))
                {
                    _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                    Actor.AnimPlayer.Play(ThrowAnimation);
                    Actor.AnimPlayer.SpeedScale = ThrowAnimationSpeed;

                    var speed = Mathf.Max(Actor.AnimPlayer.SpeedScale, 0.0001f);
                    _animRemaining = (float)Actor.AnimPlayer.CurrentAnimationLength / speed;
                }
                else
                {
                    _animationFinished = true;
                }
            }
            else
            {
                _animationFinished = true;
            }
        }

        private void UpdateAnimationState()
        {
            float delta = (float)GetPhysicsProcessDeltaTime();

            if (!_animationFinished)
            {
                _animRemaining -= delta;

                if (_animRemaining <= 0f)
                {
                    _animationFinished = true;
                }
                else if (Actor.AnimPlayer != null && !Actor.AnimPlayer.IsPlaying())
                {
                    _animationFinished = true;
                }
            }
        }
    }
}
