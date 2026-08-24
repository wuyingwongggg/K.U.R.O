using Godot;
using System;
using Kuros.Actors.Heroes;
using Kuros.Items;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 持握可投掷物品的奔跑状态
    /// 在此状态下可以投掷物品，停止移动则返回 IdleHolding
    /// </summary>
    public partial class PlayerRunHoldingState : PlayerState
    {
        public float RunHoldingAnimationSpeed = 1.0f;
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
            //GD.Print($"[PlayerRunHoldingState] 进入持握奔跑状态");
            
            // 播放奔跑动画
            if (Player is MainCharacter mainChar)
            {
                PlayAnimation(mainChar.RunHoldingAnimationName, true, RunHoldingAnimationSpeed);
            }
            else
            {
                if (Actor.AnimPlayer != null)
                {
                    _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                    PlayAnimation("animations/run", true, RunHoldingAnimationSpeed);
                }
            }
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
                //GD.Print($"[PlayerRunHoldingState] 物品不可投掷或已消失，返回 Run");
                ChangeState("Run");
                return;
            }
            
            // 检查投掷按键
            if (Player.IsActionJustPressedArbitrated("throw"))
            {
                CaptureThrowFrameFromHeldScene();
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
            if (input == Vector2.Zero)
            {
                //GD.Print($"[PlayerRunHoldingState] 停止移动，转换到 IdleHolding");
                ChangeState("IdleHolding");
                return;
            }

            // 检查是否停止奔跑
            // if (!IsActionPressed("run"))
            // {
            //     GD.Print($"[PlayerRunHoldingState] 停止奔跑，转换到 Walk");
            //     ChangeState("Walk");
            //     return;
            // }

            // 检查攻击按键
            if (IsAttackTriggered() && Actor.AttackTimer <= 0)
            {
                CaptureThrowFrameFromHeldScene();
                Player.RequestAttackFromState(Name);
                ChangeState("Throw");
                return;
            }
            
            // 奔跑移动逻辑（x倍速度）
            Vector2 velocity = Actor.Velocity;
            velocity.X = input.X * (Actor.Speed * 2f);
            velocity.Y = input.Y * (Actor.Speed * 2f);

            Actor.Velocity = velocity;
            Actor.CurrentMoveSpeed = Actor.Speed * 2f;   // 记录当前移动速度（供投掷惯性等继承）
            Actor.CurrentMoveDirection = input.Normalized();

            if (input.X != 0)
            {
                Actor.FlipFacing(input.X > 0);
            }
            
            Actor.MoveAndSlide();
            Actor.ClampPositionToScreen();
        }

        private void CaptureThrowFrameFromHeldScene()
        {
            var sprite = Player.ItemAttachment?.GetHeldAnimatedSprite()
                ?? FindHeldAnimatedSprite(Player);
            if (_interaction != null)
                _interaction.PendingThrowFrame = sprite?.Frame ?? -1;
        }

        /// <summary>按所持物品类型判定持物闪避是否允许：投掷家具（IsFurniture）与投掷武器分别走各自构筑委托。</summary>
        private bool IsDashFromHoldingAllowedByItem(ItemDefinition? item)
        {
            if (item == null) return false;
            return item.IsFurniture
                ? Actor.IsDashFromThrowFurnitureHoldingAllowed?.Invoke() == true
                : Actor.IsDashFromThrowWeaponHoldingAllowed?.Invoke() == true;
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
