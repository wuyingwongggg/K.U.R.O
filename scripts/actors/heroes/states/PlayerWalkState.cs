using Godot;
using System;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.States
{
	public partial class PlayerWalkState : PlayerState
	{
		public float WalkAnimationSpeed = 1.5f;
		private float _originalSpeedScale = 1.0f;
		
		public override void Enter()
		{
			Player.NotifyMovementState(Name);
			
			// 使用 PlayAnimation 方法，自动适配 MainCharacter 和 SamplePlayer
			if (Player is MainCharacter mainChar)
			{
				// MainCharacter 使用 Spine 动画
				PlayAnimation(mainChar.WalkAnimationName, true, WalkAnimationSpeed);
			}
			else
			{
				// SamplePlayer 使用 AnimationPlayer
				if (Actor.AnimPlayer != null)
				{
					// Save original speed scale before modifying
					_originalSpeedScale = Actor.AnimPlayer.SpeedScale;
					
					// 使用 PlayAnimation 方法（虽然它会再次检查，但这样可以统一接口）
					PlayAnimation("animations/Walk", true, WalkAnimationSpeed);
				}
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
			
			if (IsAttackTriggered() && Actor.AttackTimer <= 0)
			{
				Player.RequestAttackFromState(Name);
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
			
			Vector2 input = GetMovementInput();
			
			if (input == Vector2.Zero)
			{
				ChangeState("Idle");
				return;
			}
			
			// Movement Logic（含 SpeedBonusPercent 加法加成）
			Vector2 velocity = Actor.Velocity;
			float speed = Actor.Speed * (1f + Actor.SpeedBonusPercent / 100f);
			velocity.X = input.X * speed;
			velocity.Y = input.Y * speed;

			Actor.Velocity = velocity;
			Actor.CurrentMoveSpeed = speed;
			Actor.CurrentMoveDirection = input.Normalized();
			
			if (input.X != 0)
			{
				Actor.FlipFacing(input.X > 0);
			}
			
			Actor.MoveAndSlide();
			Actor.ClampPositionToScreen();
		}
	}
}
