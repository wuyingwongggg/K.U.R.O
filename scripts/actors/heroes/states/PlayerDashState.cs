using Godot;
using Kuros.Actors.Heroes;

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
        public bool CanDash => _charges > 0;
        public float RechargeProgress => _charges >= MaxCharges ? 1f
            : 1f - (_rechargeTimer / RechargeTime);

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
            if (!CanDash) return false;
            return base.CanEnterFrom(previousState);
        }

        public override void Enter()
        {
            _charges--;
            if (_charges < MaxCharges && _rechargeTimer <= 0f)
                _rechargeTimer = RechargeTime;

            Vector2 input = GetMovementInput();
            bool isBackDash = input == Vector2.Zero;
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
                    ChangeState("Dash");
                    return;
                }
                if (buffered == "attack" || IsAttackTriggered())
                {
                    Player.RequestAttackFromState(Name);
                    ChangeState("Attack");
                    return;
                }
                if (IsActionJustPressed("dash") && CanDash)
                {
                    ChangeState("Dash");
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
