using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
    public partial class PlayerHitState : PlayerState
    {
        public float HitAnimationSpeed = 1.0f;
        private float _originalSpeedScale = 1.0f;
        private float _stunTimer = 0.0f;
        
        public override void Enter()
        {
            Actor.Velocity = Vector2.Zero;
            
            if (Actor.AnimPlayer != null)
            {
                // Save original speed scale before modifying
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                
                Actor.AnimPlayer.Play("animations/hit");
                // Set animation playback speed only for hit animation
                Actor.AnimPlayer.SpeedScale = HitAnimationSpeed;
            }
            
            // Set default stun duration or calculate based on damage
            _stunTimer = 0.3f;
            
            // Optional: Add knockback force if we had access to damage source
        }
        
        public override void Exit()
        {
            // Restore original animation speed when leaving hit state
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            _stunTimer -= (float)delta;
            
            if (_stunTimer <= 0)
            {
                ChangeState("Idle");
                return;
            }
            
            // While stunned, we can still be moved by external forces (gravity, knockback)
            // but for now we just apply friction/stop
             Actor.Velocity = Actor.Velocity.MoveToward(Vector2.Zero, Actor.Speed * (float)delta);
             Actor.MoveAndSlide();
        }
    }
}

