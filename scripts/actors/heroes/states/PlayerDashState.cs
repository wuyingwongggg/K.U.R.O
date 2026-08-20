using Godot;
using Kuros.Actors.Heroes;
using Kuros.Builds.Machine;
using Kuros.Core;

namespace Kuros.Actors.Heroes.States
{
    public partial class PlayerDashState : PlayerState
    {
        [ExportCategory("Dash Burst")]
        [Export(PropertyHint.Range, "100,5000,10")] public float BurstSpeed = 4000f;
        [Export(PropertyHint.Range, "0.01,1,0.01")] public float BurstDuration = 0.1f;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float BurstAnimationSpeed = 2f;

        [ExportCategory("Dash Recovery")]
        [Export(PropertyHint.Range, "100,5000,10")] public float RecoverySpeed = 500f;
        [Export(PropertyHint.Range, "0.01,1,0.01")] public float RecoveryDuration = 0.57f;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float RecoveryAnimationSpeed = 1f;

        [ExportCategory("Dash")]
        [Export(PropertyHint.Range, "0.1,2,0.01")] public float InvincibilityDuration = 0.2f;

        [ExportCategory("Dash Charging")]
        [Export(PropertyHint.Range, "1,10,1")] public int MaxCharges = 1;
        [Export(PropertyHint.Range, "0.5,10,0.1")] public float RechargeTime = 2.0f;

        private int _charges;
        private float _rechargeTimer;
        private Vector2 _dashDirection;
        private float _elapsed;
        private Node? _afterimage;
        private bool _inBurst;
        private float _totalDuration;

        public int Charges => _charges;
        /// <summary>最近一次闪避是否为后撤（供 B_003 闪避缓存等效果检测前向闪避方向）。</summary>
        public bool LastDashWasBackDash { get; private set; }
        /// <summary>最近一次闪避 Enter 时刻（含 Reenter 连闪——B_003 等效果检测"闪避开始"用）。</summary>
        public ulong LastDashEnteredAtMs { get; private set; }
        /// <summary>闪避可用性：热能闪避（B_008）激活期间热量优先，热量不足时回退充能（B_002 兜底）；否则由充能判定。</summary>
        public bool CanDash
        {
            get
            {
                var heatDash = GetHeatDashEffect();
                if (heatDash != null && heatDash.IsActive)
                    return heatDash.CanConsumeForDash || _charges > 0;
                return _charges > 0;
            }
        }
        public float RechargeProgress => _charges >= MaxCharges ? 1f
            : 1f - (_rechargeTimer / RechargeTime);

        private MachineHeatDashEffect? GetHeatDashEffect()
            => Player?.EffectController?.GetEffect<MachineHeatDashEffect>();

        public override void _Ready()
        {
            base._Ready();
            _charges = MaxCharges;
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (_charges >= MaxCharges) return;
            _rechargeTimer -= (float)delta;
            if (_rechargeTimer <= 0f)
            {
                _charges++;
                if (_charges < MaxCharges)
                    _rechargeTimer = RechargeTime;
            }
        }

        public override bool CanEnterFrom(string? previousState)
        {
            // 免费窗口（B_003）：仅"后撤"闪避在窗口内无资源时放行（前向闪避仍需资源）
            if (!CanDash && !IsFreeBackDashWindow()) return false;
            return base.CanEnterFrom(previousState);
        }

        /// <summary>是否为免费后撤窗口：当前无方向输入（后撤）且 B_003 免费窗口激活。</summary>
        private bool IsFreeBackDashWindow()
        {
            if (GetMovementInput() != Vector2.Zero) return false;
            return Actor is GameActor actor && actor.IsDashBackWindowActive?.Invoke() == true;
        }

        public override void Enter()
        {
            Vector2 input = GetMovementInput();
            bool isBackDash = input == Vector2.Zero;
            LastDashWasBackDash = isBackDash;
            LastDashEnteredAtMs = Time.GetTicksMsec();

            // 闪避缓存（B_003）：前向闪避后的免费窗口内，后撤闪避不消耗任何闪避资源
            bool freeBackDash = isBackDash && IsFreeBackDashWindow();

            // 热能闪避（B_008）：buff 期间热量优先（不耗充能）；热量不足时回退充能（B_002 兜底）。
            // CanDash 前置防御：免费窗口放行的无资源前向闪避不扣成负数（白闪）
            if (!freeBackDash)
            {
                var heatDash = GetHeatDashEffect();
                if (heatDash != null && heatDash.IsActive && heatDash.CanConsumeForDash)
                {
                    heatDash.ConsumeForDash();
                }
                else if (CanDash)
                {
                    _charges--;
                    if (_charges < MaxCharges && _rechargeTimer <= 0f)
                        _rechargeTimer = RechargeTime;
                }
            }

            if (isBackDash)
                _dashDirection = new Vector2(Actor.FacingRight ? -1f : 1f, 0f);
            else
                _dashDirection = input.Normalized();

            if (!isBackDash && _dashDirection.X != 0)
                Actor.FlipFacing(_dashDirection.X > 0);

            _elapsed = 0f;
            _inBurst = true;
            _totalDuration = BurstDuration + RecoveryDuration;

            if (Player is MainCharacter mainChar)
                mainChar.StartHitInvincibility(InvincibilityDuration);

            _afterimage = Player.GetNodeOrNull<Node>("AfterimageController");
            _afterimage?.Call("start");

            if (Player is MainCharacter mc)
            {
                mc.SetSpineAnimationSpeed(BurstAnimationSpeed);
                PlayAnimation(isBackDash ? mc.DashBackAnimationName : mc.DashAnimationName, false);
            }
            else
            {
                PlayAnimation("animations/run", true);
            }
        }

        public override void Exit()
        {
            _afterimage?.Call("stop");
            Actor.Velocity = Vector2.Zero;
            if (Player is MainCharacter mc)
                mc.SetSpineAnimationSpeed(1f);
        }

        public override void PhysicsUpdate(double delta)
        {
            _elapsed += (float)delta;
            if (_elapsed >= _totalDuration)
            {
                ChangeState("Idle");
                return;
            }

            if (_inBurst && _elapsed >= BurstDuration)
            {
                _inBurst = false;
                if (Player is MainCharacter mc)
                    mc.SetSpineAnimationSpeed(RecoveryAnimationSpeed);
            }



            if (_inBurst)
            {
                if (IsActionJustPressed("attack"))
                    BufferInput("attack", AttackPriority);
            }
            else
            {
                var buffered = ConsumeBufferedInput();
                if (buffered == "dash" && CanDash)
                {
                    // 连闪：当前已是 Dash，ChangeState 同状态会被状态机忽略——必须 Reenter 重置两阶段
                    Machine.ReenterState("Dash");
                    return;
                }
                if (buffered == "attack" || IsAttackTriggered())
                {
                    Player.RequestAttackFromState(Name);
                    ChangeState("Attack");
                    return;
                }
                if (IsActionJustPressed("dash") && (CanDash || IsFreeBackDashWindow()))
                {
                    // 连闪/dashback：同上，同状态重进（免费后撤窗口内无资源也放行）
                    Machine.ReenterState("Dash");
                    return;
                }
                if (GetMovementInput() != Vector2.Zero)
                {
                    ChangeState("Walk");
                    return;
                }
            }
            float speed = _inBurst ? BurstSpeed : RecoverySpeed;
            Actor.Velocity = _dashDirection * speed;
            Actor.MoveAndSlide();
            Actor.ClampPositionToScreen();
        }

        public override bool CanExitTo(string nextState)
        {
            if (nextState == "Dying" || nextState == "Dead")
                return true;
            return !_inBurst;
        }
    }
}
