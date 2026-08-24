using Godot;
using System;
using Kuros.Actors.Heroes;
using Kuros.Items;

namespace Kuros.Actors.Heroes.States
{
	/// <summary>
	/// 持握可投掷物品的待机状态
	/// 在此状态下可以投掷物品，移动则进入 RunHolding 状态
	/// </summary>
	public partial class PlayerIdleHoldingState : PlayerState
	{
		public float IdleHoldingAnimationSpeed = 1.0f;
		private float _originalSpeedScale = 1.0f;
		private PlayerItemInteractionComponent? _interaction;
		
		protected override void _ReadyState()
		{
			base._ReadyState();
			_interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
		}
		
		public override void Enter()
		{
			Player.NotifyMovementState(Name);
			// 静止持物：移动速度归零（供投掷惯性继承）——否则从 RunHolding 回到本状态时残留奔跑速度
			Actor.CurrentMoveSpeed = 0f;
			Actor.CurrentMoveDirection = Vector2.Zero;
			//GD.Print($"[PlayerIdleHoldingState] 进入持握状态");

			// 播放持握动画
			if (Player is MainCharacter mainChar)
			{
				PlayAnimation(mainChar.IdleHoldingAnimationName, true, IdleHoldingAnimationSpeed);
			}
			else
			{
				if (Actor.AnimPlayer != null)
				{
					_originalSpeedScale = Actor.AnimPlayer.SpeedScale;
					PlayAnimation("animations/Idle", true, IdleHoldingAnimationSpeed);
				}
			}
			Actor.Velocity = Vector2.Zero;
		}
		
		public override void Exit()
		{
			if (Actor.AnimPlayer != null)
			{
				Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
			}
		}

		public override void PhysicsUpdate(double delta)
		{
			if (HandleDialogueGating(delta)) return;

			// 检查是否还有可投掷物品
			var selectedStack = Player.InventoryComponent?.GetSelectedQuickBarStack();
			if (selectedStack == null || selectedStack.IsEmpty || !selectedStack.Item.IsThrowable || selectedStack.IsThrowOnCooldown)
			{
				//GD.Print($"[PlayerIdleHoldingState] 物品不可投掷或已消失，返回 Idle");
				ChangeState("Idle");
				return;
			}
			
			// 检查投掷按键
			if (Player.IsActionJustPressedArbitrated("throw"))
			{
				CaptureThrowFrame();
				ChangeState("Throw");
				return;
			}

			// 持物闪避（构筑效果条件）：投掷武器（IsThrowWeapon）与投掷家具（一次性）分别按各自委托判定
			if (Player.IsActionJustPressedArbitrated("dash")
				&& IsDashFromHoldingAllowedByItem(selectedStack?.Item))
			{
				ChangeState("Dash");
				return;
			}

			// 检查移动输入
			Vector2 input = GetMovementInput();
			if (input != Vector2.Zero)
			{
				//GD.Print($"[PlayerIdleHoldingState] 检测到移动输入，转换到 RunHolding");
				if (IsActionPressed("run"))
				{
					ChangeState("RunHolding");
				}
				else
				{
					ChangeState("RunHolding");
				}
				return;
			}

			// 检查攻击按键
			if (IsAttackTriggered() && Actor.AttackTimer <= 0)
			{
CaptureThrowFrame();
              	Player.RequestAttackFromState(Name);
                ChangeState("Throw");
                return;
			}
			
			// 应用摩擦力
			Actor.Velocity = Actor.Velocity.MoveToward(Vector2.Zero, Actor.Speed * 2 * (float)delta);
			Actor.MoveAndSlide();
			Actor.ClampPositionToScreen();
		}

		/// <summary>按所持物品类型判定持物闪避是否允许：投掷家具（IsFurniture）与投掷武器分别走各自构筑委托。</summary>
		private bool IsDashFromHoldingAllowedByItem(ItemDefinition? item)
		{
			if (item == null) return false;
			return item.IsFurniture
				? Actor.IsDashFromThrowFurnitureHoldingAllowed?.Invoke() == true
				: Actor.IsDashFromThrowWeaponHoldingAllowed?.Invoke() == true;
		}

		private void CaptureThrowFrame()
		{
			var sprite = Player.ItemAttachment?.GetHeldAnimatedSprite()
				?? FindHeldAnimatedSprite(Player);
			int frame = sprite?.Frame ?? -1;
			if (_interaction != null)
				_interaction.PendingThrowFrame = frame;
		}

		/// <summary>按类型在玩家场景树下找第一个 AnimatedSprite2D（附件缓存失败时的回退）。</summary>
		private static AnimatedSprite2D? FindHeldAnimatedSprite(Node root)
		{
			foreach (Node child in root.FindChildren("*", recursive: true, owned: false))
			{
				if (child is AnimatedSprite2D anim)
					return anim;
			}
			return null;
		}
	}
}

