using Godot;
using Kuros.Items;
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

        [ExportGroup("Throw Animation Speeds")]
        /// <summary>Warmup 阶段动画速度（参照 WeaponSkillDefinition 分阶段速度；蓄力模式下按蓄力窗自动再折算放慢）。</summary>
        [Export(PropertyHint.Range, "0.05,3,0.05")] public float WarmupAnimationSpeed = 1f;
        /// <summary>Active（出手）阶段动画速度。</summary>
        [Export(PropertyHint.Range, "0.05,3,0.05")] public float ActiveAnimationSpeed = 1f;
        /// <summary>Recovery（后摇）阶段动画速度。</summary>
        [Export(PropertyHint.Range, "0.05,3,0.05")] public float RecoveryAnimationSpeed = 1f;
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

        // B_006 投掷预载:蓄力模式(按住攻击键进入;松手/满窗出手)
        private IThrowChargeModifier? _chargeEffect;
        private bool _chargeMode;
        private float _chargeSeconds;

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

            // B_006 投掷预载:按住攻击键进入蓄力模式——warmup 段动画按蓄力窗折算放慢,
            // 松手/满窗出手时从 warmup 段末跳帧常速播放 Active 段(减慢严格只覆盖 warmup 阶段)
            // (判定用 IsActionHeldArbitrated:IsControlledActionPressed 在鼠标悬停 UI 时会误判松手)
            _chargeEffect = Player.EffectController?.GetEffectByInterface<IThrowChargeModifier>();
            _chargeSeconds = 0f;
            _chargeMode = _chargeEffect != null && Player.IsActionHeldArbitrated("attack");
            if (_chargeMode)
            {
                _chargeEffect!.Charging = true;
                _chargeEffect.ChargeSeconds = 0f;
                // warmup 段内容时长(ThrowWarmupDuration)拉伸到整个蓄力窗口:速度 = 基础 × 段时长/窗口
                float chargeWindow = Mathf.Max(_chargeEffect.MaxChargeSeconds, 0.01f);
                float chargeWarmupSpeed = Mathf.Max(WarmupAnimationSpeed * ThrowWarmupDuration / chargeWindow, 0.01f);
                PlayThrowAnimation(ThrowAnimationSpeed * chargeWarmupSpeed);
            }
            else
            {
                PlayThrowAnimation(ThrowAnimationSpeed * WarmupAnimationSpeed);
            }

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

            // B_006 蓄力状态清零(出手/取消/被打断均无残留)——贡献只在 ChargeSeconds>0 时生效
            if (_chargeEffect != null)
            {
                _chargeEffect.Charging = false;
                _chargeEffect.ChargeSeconds = 0f;
            }
            _chargeEffect = null;
            _chargeMode = false;
            _chargeSeconds = 0f;

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
        /// 投掷惯性（类似攻击模板 EnableDashMovement）：Warmup 内从起步速度沿**Enter 捕获的投掷前移动方向**
        /// 线性衰减到 0（Active 前归零）——出手时已无位移惯性。衰减窗口固定为基础 ThrowWarmupDuration，
        /// 不随蓄力窗延长（B_006 蓄力不放大冲刺惯性）。
        /// 有移动输入时随转向更新惯性方向（蓄力期间可转向，投掷出手自动朝新方向）；
        /// 无输入则保持捕获方向——后撤投掷延续后撤滑行，不按面朝强制反向（后撤不翻面，面朝≠移动方向）。
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
                {
                    Player.FlipFacing(moveInput.X > 0);
                    // 有输入:惯性方向随转向
                    _momentumDir = Player.FacingRight ? Vector2.Right : Vector2.Left;
                }
                // 无输入:保持 Enter 捕获的投掷前方向(后撤投掷=延续后撤滑行)

                _momentumElapsed += delta;
                float t = ThrowWarmupDuration > 0f
                    ? Mathf.Clamp(_momentumElapsed / ThrowWarmupDuration, 0f, 1f)
                    : 1f;
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

        /// <summary>阶段推进：Warmup 结束触发投掷 → Active 出手保护 → Recovery 后摇（动画播完即结束）。
        /// B_006 蓄力模式:Warmup 不按固定时长递减,由蓄力窗口驱动(见 UpdateChargeThrow)。</summary>
        private void UpdatePhase(float delta)
        {
            if (_phase == ThrowPhase.Warmup && _chargeMode)
            {
                UpdateChargeThrow(delta);
                return;
            }

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
                    ApplyPhaseAnimationSpeed(RecoveryAnimationSpeed);
                    break;

                case ThrowPhase.Recovery:
                    // 后摇结束（动画播完由 UpdateAnimationState 置 _animationFinished）
                    _animationFinished = true;
                    break;
            }
        }

        /// <summary>B_006 蓄力窗口推进:逐帧把蓄力秒数写入效果(预览实时增长按此值结算);
        /// 攻击键松开或满 MaxChargeSeconds → 出手。
        /// 出手 = warmup 阶段结束:立即取消减慢,从 warmup 段末(ThrowWarmupDuration)跳帧常速播放
        /// 出手动作(Active 段),剩余动画时长供 Active/Recovery 结束判定。</summary>
        private void UpdateChargeThrow(float delta)
        {
            var effect = _chargeEffect;
            if (effect == null || _interaction == null) return;

            _chargeSeconds += delta;
            // 逐帧写入(连续比例):预览/其它查询随时读到当前蓄力,出手快照即最新值
            effect.ChargeSeconds = Mathf.Min(_chargeSeconds, effect.MaxChargeSeconds);

            bool released = !Player.IsActionHeldArbitrated("attack");
            bool full = _chargeSeconds >= effect.MaxChargeSeconds;
            if (!released && !full) return;

            effect.Charging = false;

            // 出手:warmup 段结束——从 warmup 段末跳帧、按 Active 段速度播放(减慢立即取消)
            float activeSpeed = Mathf.Max(ThrowAnimationSpeed * ActiveAnimationSpeed, 0.01f);
            if (Player is MainCharacter mainChar)
                mainChar.PlaySpineAnimationFrom(ThrowAnimation, ThrowWarmupDuration,
                    loop: false, timeScale: activeSpeed);
            else if (Actor.AnimPlayer != null && Actor.AnimPlayer.HasAnimation(ThrowAnimation))
            {
                Actor.AnimPlayer.Play(ThrowAnimation);
                Actor.AnimPlayer.SpeedScale = activeSpeed;
            }
            _animRemaining = Mathf.Max((ThrowAnimationTotalTime - ThrowWarmupDuration) / activeSpeed, 0f);

            if (_interaction.TryTriggerThrowAfterAnimation())
            {
                _hasRequestedThrow = true;
                _phase = ThrowPhase.Active;
                _phaseRemaining = ThrowActiveDuration;
            }
            else
            {
                // 件已失效(转化/移除):按取消处理,直接走收尾
                _phase = ThrowPhase.Recovery;
                _phaseRemaining = ThrowRecoveryDuration;
            }
        }

        /// <summary>阶段切换时应用该阶段动画速度（参照 PlayerAttackTemplate.ApplyPhaseAnimationSpeed：
        /// 动态改速不重启动画）。</summary>
        private void ApplyPhaseAnimationSpeed(float phaseSpeed)
        {
            float speed = ThrowAnimationSpeed * phaseSpeed;
            if (Player is MainCharacter mainChar)
                mainChar.SetSpineAnimationSpeed(speed);
            else if (Actor.AnimPlayer != null)
                Actor.AnimPlayer.SpeedScale = speed;
        }

        /// <summary>播放投掷动画。speed = 绝对播放速度(ThrowAnimationSpeed × 阶段速度)。</summary>
        private void PlayThrowAnimation(float speed)
        {
            if (Player is MainCharacter mainChar)
            {
                mainChar.PlaySpineAnimation(ThrowAnimation, loop: false, timeScale: speed);
                _animRemaining = ThrowAnimationTotalTime / Mathf.Max(speed, 0.0001f);
            }
            else if (Actor.AnimPlayer != null)
            {
                if (Actor.AnimPlayer.HasAnimation(ThrowAnimation))
                {
                    _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                    Actor.AnimPlayer.Play(ThrowAnimation);
                    Actor.AnimPlayer.SpeedScale = speed;

                    var actualSpeed = Mathf.Max(Actor.AnimPlayer.SpeedScale, 0.0001f);
                    _animRemaining = (float)Actor.AnimPlayer.CurrentAnimationLength / actualSpeed;
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
            // 蓄力窗口期间动画由蓄力进度驱动(拉伸播放),不参与状态结束判定
            if (_chargeMode && _phase == ThrowPhase.Warmup) return;

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
