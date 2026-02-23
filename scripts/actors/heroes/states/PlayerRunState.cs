using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
    public partial class PlayerRunState : PlayerState
    {
        public float RunAnimationSpeed = 1.0f;
        private float _originalSpeedScale = 1.0f;
        
        public override void Enter()
        {
            Player.NotifyMovementState(Name);
            if (Actor.AnimPlayer != null)
            {
                // Save original speed scale before modifying
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                
                Actor.AnimPlayer.Play("animations/run");
                // Set animation playback speed only for run animation
                Actor.AnimPlayer.SpeedScale = RunAnimationSpeed;
                var anim = Actor.AnimPlayer.GetAnimation("animations/run");
                if (anim != null) anim.LoopMode = Animation.LoopModeEnum.Linear;
            }
            // Increase speed by changing velocity calculation, not base stat
        }
        
        public override void Exit()
        {
            // Restore original animation speed when leaving run state
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            if (HandleDialogueGating(delta)) return;
            
            if (Input.IsActionJustPressed("attack") && Actor.AttackTimer <= 0)
            {
                Player.RequestAttackFromState(Name);
                ChangeState("Attack");
                return;
            }
            
            // Stop running if shift is released
            if (!Input.IsActionPressed("run"))
            {
                ChangeState("Walk");
                return;
            }
            
            Vector2 input = GetMovementInput();
            
            if (input == Vector2.Zero)
            {
                ChangeState("Idle");
                return;
            }
            
            // Run Logic (2x Speed)
            Vector2 velocity = Actor.Velocity;
            velocity.X = input.X * (Actor.Speed * 2.0f);
            velocity.Y = input.Y * (Actor.Speed * 2.0f);
            
            Actor.Velocity = velocity;
            
            if (input.X != 0)
            {
                Actor.FlipFacing(input.X > 0);
            }
            
            Actor.MoveAndSlide();
            Actor.ClampPositionToScreen();
        }
    }
}

