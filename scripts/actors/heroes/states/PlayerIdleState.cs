using Godot;
using System;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.States
{
	public partial class PlayerIdleState : PlayerState
	{
		public float IdleAnimationSpeed = 1.0f;
		private float _originalSpeedScale = 1.0f;
		
		public override void Enter()
		{
			Player.NotifyMovementState(Name);
			Actor.CurrentMoveSpeed = 0f;   // 静止状态：移动速度归零（供攻击模板查询冲刺起步速度）
			Actor.CurrentMoveDirection = Vector2.Zero;

			// 使用 PlayAnimation 方法，自动适配 MainCharacter 和 SamplePlayer
			if (Player is MainCharacter mainChar)
			{
				// MainCharacter 使用 Spine 动画
				PlayAnimation(mainChar.IdleAnimationName, true, IdleAnimationSpeed);
			}
			else
			{
				// SamplePlayer 使用 AnimationPlayer
				if (Actor.AnimPlayer != null)
				{
					// Save original speed scale before modifying
					_originalSpeedScale = Actor.AnimPlayer.SpeedScale;
					
					// Reset bones first to avoid "stuck" poses from previous animations
					if (Actor.AnimPlayer.HasAnimation("RESET"))
					{
						Actor.AnimPlayer.Play("RESET");
						Actor.AnimPlayer.Advance(0); // Apply immediately
					}
					
					// 使用 PlayAnimation 方法（虽然它会再次检查，但这样可以统一接口）
					PlayAnimation("animations/Idle", true, IdleAnimationSpeed);
				}
			}
			Actor.Velocity = Vector2.Zero;
		}
		
		public override void Exit()
		{
			// Restore original animation speed when leaving idle state
			if (Actor.AnimPlayer != null)
			{
				Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
			}
		}

		public override void PhysicsUpdate(double delta)
		{
			if (HandleDialogueGating(delta)) return;
			
			// Space 按下帧 → Dash
			if (IsActionJustPressed("dash"))
			{
				ChangeState("Dash");
				return;
			}

			// 检查是否转换到 IdleHolding（持握可投掷物品）
			var selectedStack = Player.InventoryComponent?.GetSelectedQuickBarStack();
			if (selectedStack != null && !selectedStack.IsEmpty && selectedStack.Item.IsThrowable && !selectedStack.IsThrowOnCooldown)
			{
				GD.Print($"[PlayerIdleState] 检测到可投掷物品: {selectedStack.Item.ItemId}，转换到 IdleHolding");
				ChangeState("IdleHolding");
				return;
			}
			
			// Check for transitions
			if (IsAttackTriggered() && Actor.AttackTimer <= 0)
			{
				Player.RequestAttackFromState(Name);
				ChangeState("Attack");
				return;
			}
			
			Vector2 input = GetMovementInput();
			if (input != Vector2.Zero)
			{
				if (IsActionPressed("run"))
				{
					ChangeState("Run");
				}
				else if (!IsActionPressed("run"))
				{
					ChangeState("Walk");
				}
				return;
			}

			if (WasActionShortPressed("take_up"))
			{
				ChangeState("PickUp");
				return;
			}
			
			// Apply friction/stop
			Actor.Velocity = Actor.Velocity.MoveToward(Vector2.Zero, Actor.Speed * 2 * (float)delta);
			Actor.MoveAndSlide();
			Actor.ClampPositionToScreen();
		}
	}
}
