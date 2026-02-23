using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
	public partial class PlayerWalkState : PlayerState
	{
		public float WalkAnimationSpeed = 1.5f;
		private float _originalSpeedScale = 1.0f;
		
		public override void Enter()
		{
			Player.NotifyMovementState(Name);
			if (Actor.AnimPlayer != null)
			{
				// Save original speed scale before modifying
				_originalSpeedScale = Actor.AnimPlayer.SpeedScale;
				
				Actor.AnimPlayer.Play("animations/Walk");
				// Set animation playback speed only for walk animation
				Actor.AnimPlayer.SpeedScale = WalkAnimationSpeed;
				// Force loop mode just in case resource isn't set
				var anim = Actor.AnimPlayer.GetAnimation("animations/Walk");
				if (anim != null) anim.LoopMode = Animation.LoopModeEnum.Linear;
			}
		}
		
		public override void Exit()
		{
			// Restore original animation speed when leaving walk state
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
			
			// Check for run
			if (Input.IsActionPressed("run"))
			{
				ChangeState("Run");
				return;
			}
			
			Vector2 input = GetMovementInput();
			
			if (input == Vector2.Zero)
			{
				ChangeState("Idle");
				return;
			}
			
			// Movement Logic
			Vector2 velocity = Actor.Velocity;
			velocity.X = input.X * Actor.Speed;
			velocity.Y = input.Y * Actor.Speed;
			
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
