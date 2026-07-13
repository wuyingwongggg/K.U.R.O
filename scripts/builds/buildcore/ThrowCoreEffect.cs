using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.BuildCore
{
    /// <summary>
    /// Throw 核心机制：家具生成。
    /// OnApply 激活投掷指示器，按下核心技能键在指示器位置生成一次性家具。
    /// </summary>
    [GlobalClass]
    public partial class ThrowCoreEffect : ActorEffect
    {
        [ExportCategory("Furniture")]
        [Export] public PackedScene? FurnitureScene { get; set; }
        [Export(PropertyHint.Range, "0.5,30,0.5")] public float SpawnCooldown = 3f;
        [Export(PropertyHint.Range, "0,200,1")] public float PlacementMargin = 16f;

        /// <summary>CD 剩余时间，HUD 绑定读取。</summary>
        public float CooldownRemaining { get; private set; }
        public float CooldownDuration => SpawnCooldown;
        public bool CanSpawn => CooldownRemaining <= 0f;

        private bool _indicatorEnabled;

        protected override void OnApply()
        {
            CooldownRemaining = 0f;
            _indicatorEnabled = true;
            GetMainCharacter()?.EnableThrowIndicator(true);
        }

        protected override void OnTick(double delta)
        {
            if (CooldownRemaining > 0f)
                CooldownRemaining -= (float)delta;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!IsInstanceValid(this) || Actor == null) return;
            if (!@event.IsActionPressed(InputActions.CoreSkill) || @event.IsEcho()) return;
            if (!CanSpawn || FurnitureScene == null) return;

            SpawnFurniture();
            GetViewport()?.SetInputAsHandled();
        }

        private void SpawnFurniture()
        {
            var mc = GetMainCharacter();
            if (mc == null) return;

            var indicator = mc.GetThrowIndicatorNode();
            Vector2 spawnPos = indicator != null && IsInstanceValid(indicator)
                ? ((Node2D)indicator).GlobalPosition
                : mc.GlobalPosition;

            var furniture = FurnitureScene!.Instantiate<Node2D>();
            mc.GetParent()?.AddChild(furniture);
            furniture.GlobalPosition = spawnPos;

            // 读取家具碰撞形状，沿朝向校准位置：Player.X + FacingSign * (半宽 + margin)
            var shape = FindFirstCollisionShape(furniture);
            if (shape != null)
            {
                float halfWidth = GetCollisionHalfWidth(shape) + PlacementMargin;
                float sign = mc.FacingRight ? 1f : -1f;
                furniture.GlobalPosition = new Vector2(
                    mc.GlobalPosition.X + sign * halfWidth,
                    spawnPos.Y);
            }

            CooldownRemaining = SpawnCooldown;
        }

        private static CollisionShape2D? FindFirstCollisionShape(Node2D root)
        {
            foreach (var child in root.GetChildren())
            {
                if (child is CollisionShape2D shape)
                    return shape;
                if (child is Node2D childNode)
                {
                    var found = FindFirstCollisionShape(childNode);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static float GetCollisionHalfWidth(CollisionShape2D shape)
        {
            if (shape?.Shape == null) return 32f;
            if (shape.Shape is RectangleShape2D rect) return rect.Size.X * 0.5f;
            if (shape.Shape is CircleShape2D circle) return circle.Radius;
            return 32f;
        }

        public override void OnRemoved()
        {
            if (_indicatorEnabled)
                GetMainCharacter()?.EnableThrowIndicator(false);
            base.OnRemoved();
        }

        private MainCharacter? GetMainCharacter()
        {
            return Actor as MainCharacter;
        }
    }
}
