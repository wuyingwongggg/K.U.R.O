using Godot;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.States
{
    public partial class PlayerDashState : PlayerState
    {
        [ExportCategory("Dash")]
        [Export(PropertyHint.Range, "100,5000,10")]
        public float DashSpeed = 2000f;

        [Export(PropertyHint.Range, "0.05,1,0.01")]
        public float DashDuration = 0.2f;

        [Export(PropertyHint.Range, "0.1,2,0.01")]
        public float InvincibilityDuration = 0.35f;

        [ExportCategory("Dash Charging")]
        [Export(PropertyHint.Range, "1,10,1")]
        public int MaxCharges = 1;

        [Export(PropertyHint.Range, "0.5,10,0.1")]
        public float RechargeTime = 2.0f;

        private int _charges;
        private float _rechargeTimer;
        private Vector2 _dashDirection;
        private float _dashTimer;
        private Node? _afterimage;

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
            if (input != Vector2.Zero)
                _dashDirection = input.Normalized();
            else
                _dashDirection = new Vector2(Actor.FacingRight ? -1f : 1f, 0f);

            if (_dashDirection.X != 0)
                Actor.FlipFacing(_dashDirection.X > 0);

            _dashTimer = DashDuration;

            if (Player is MainCharacter mainChar)
                mainChar.StartHitInvincibility(InvincibilityDuration);

            _afterimage = Player.GetNodeOrNull<Node>("AfterimageController");
            _afterimage?.Call("start");

            if (Player is MainCharacter mc)
                PlayAnimation(mc.RunAnimationName, true);
            else
                PlayAnimation("animations/run", true);
        }

        public override void Exit()
        {
            _afterimage?.Call("stop");
            Actor.Velocity = Vector2.Zero;
        }

        public override void PhysicsUpdate(double delta)
        {
            _dashTimer -= (float)delta;
            if (_dashTimer <= 0f)
            {
                ChangeState("Idle");
                return;
            }

            Actor.Velocity = _dashDirection * DashSpeed;
            Actor.MoveAndSlide();
            Actor.ClampPositionToScreen();
        }

        public override bool CanExitTo(string nextState)
        {
            if (nextState == "Dying" || nextState == "Dead")
                return true;
            return _dashTimer <= 0f;
        }
    }
}
