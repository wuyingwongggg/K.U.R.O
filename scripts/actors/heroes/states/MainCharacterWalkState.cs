using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
	public partial class MainCharacterWalkState : MainCharacterState
	{
		public float WalkAnimationSpeed { get; set; } = 1.5f;

		[Export(PropertyHint.Range, "200,3000,10")] public float DodgeSpeed = 1200f;
		[Export(PropertyHint.Range, "0.05,0.5,0.01")] public float DodgeDuration = 0.2f;

		private float _dodgeTimer;
		private Vector2 _dodgeDirection;

		public override void Enter()
		{
			MainCharacter.NotifyMovementState(Name);
			PlayAnimation(MainCharacter.WalkAnimationName, true, WalkAnimationSpeed);
			_dodgeTimer = 0f;
		}

		public override void PhysicsUpdate(double delta)
		{
			if (HandleDialogueGating(delta)) return;

			// Dodge active — lock input until dash completes
			if (_dodgeTimer > 0f)
			{
				_dodgeTimer -= (float)delta;
				if (_dodgeTimer <= 0f)
				{
					_dodgeTimer = 0f;
					ChangeState("Idle");
				}
				else
				{
					Actor.Velocity = _dodgeDirection * DodgeSpeed;
					Actor.MoveAndSlide();
					Actor.ClampPositionToScreen();
				}
				return;
			}

			if (IsAttackTriggered() && Actor.AttackTimer <= 0)
			{
				MainCharacter.RequestAttackFromState(Name);
				ChangeState("Attack");
				return;
			}

			// Space 按下帧 → Dash
			if (IsActionJustPressed("dash"))
			{
				ChangeState("Dash");
				return;
			}

			// Shift 按住 → Run
			if (IsActionPressed("run"))
			{
				ChangeState("Run");
				return;
			}

			Vector2 movementInput = GetMovementInput();

			if (movementInput == Vector2.Zero)
			{
				ChangeState("Idle");
				return;
			}

			Vector2 velocity = Actor.Velocity;
			velocity.X = movementInput.X * Actor.Speed;
			velocity.Y = movementInput.Y * Actor.Speed;

			Actor.Velocity = velocity;

			if (movementInput.X != 0)
				Actor.FlipFacing(movementInput.X > 0);

			Actor.MoveAndSlide();
			Actor.ClampPositionToScreen();
		}
	}
}
