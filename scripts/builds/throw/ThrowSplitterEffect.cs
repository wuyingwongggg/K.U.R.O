using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core.Effects;
using Kuros.Fx;
using Kuros.Items.World;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 进程分叉（BuildThrow_A_008,语义 = 投掷分裂散射,落点沿 Y 轴错开）:
    /// 乱码块被投掷的瞬间分裂 —— 层1 共 2 枚(落点 ±half,无正中);层2 共 3 枚
    /// (落点 -half / 0 / +half,原件居中)。
    /// 投掷飞行是脚本化抛物线,但**落点每帧重算**,故给每枚设置 LandingOffsetYDelta
    /// (发射后改原件亦生效,平滑重定向)→ 各枚落点纵向错开呈现散射。
    /// TierValues = 各层落点纵向半错开(层1/2 → 50/100px)。
    /// </summary>
    [GlobalClass]
    public partial class ThrowSplitterEffect : ActorEffect, IThrowSplitPreview
    {
        [Export] public float[] TierValues { get; set; } = { 50f, 100f };

        private int _tier = 1;

        // ── IThrowSplitPreview(轨迹预览多弧绘制数据源)──────────────

        public bool IsSplittableForHeldItem()
        {
            var inv = (Actor as SamplePlayer)?.InventoryComponent
                ?? Actor?.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            var stack = inv?.FurnitureSlotStack;
            return stack != null
                && (stack.RuntimeSourceTag == RigidBodyWorldItemEntity.ThrowCorePieceTag
                    || stack.RuntimeIsThrowCoreCopy);
        }

        public float CenterLandingOffsetY
        {
            get
            {
                float half = HalfOffset;
                return half <= 0f || _tier == 1 ? -half : 0f; // 层1 原件改向 -half;层2 居中 0
            }
        }

        public float[] CloneLandingOffsetsY
        {
            get
            {
                float half = HalfOffset;
                if (half <= 0f) return System.Array.Empty<float>();
                return _tier == 1 ? new[] { half } : new[] { -half, half };
            }
        }

        private float HalfOffset => TierValues.Length > 0
            ? TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1]
            : 0f;

        protected override void OnApply()
        {
            PlayerItemInteractionComponent.PieceThrown += OnPieceThrown;
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        public override void OnRemoved()
        {
            PlayerItemInteractionComponent.PieceThrown -= OnPieceThrown;
            base.OnRemoved();
        }

        private void OnPieceThrown(RigidBodyWorldItemEntity entity)
        {
            if (Actor == null || !GodotObject.IsInstanceValid(entity)) return;
            if (entity.LastDroppedBy != Actor) return;
            // 仅乱码块(件)分裂:投掷/放置恢复与直接生成的件都在身份组;环境家具不分
            if (!entity.IsInGroup(RigidBodyWorldItemEntity.ThrowCorePieceIdentityTag)) return;

            float half = TierValues.Length > 0
                ? TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1]
                : 0f;
            if (half <= 0f) return;

            var sourceBody = FindBody(entity);
            if (sourceBody == null) return;
            Vector2 velocity = sourceBody.LinearVelocity;
            if (velocity.LengthSquared() < 0.01f) return;

            if (_tier == 1)
            {
                // 层1:2 枚,落点 ±half(原件改向 -half,克隆 +half)
                entity.LandingOffsetYDelta = -half;
                SpawnClone(entity, velocity, half);
            }
            else
            {
                // 层2:3 枚,落点 -half / 0 / +half(原件居中)
                entity.LandingOffsetYDelta = 0f;
                SpawnClone(entity, velocity, -half);
                SpawnClone(entity, velocity, half);
            }
        }

        private void SpawnClone(RigidBodyWorldItemEntity original, Vector2 velocity, float landingDeltaY)
        {
            if (original.ItemDefinition == null) return;
            string path = original.ItemDefinition.ResolveWorldScenePath();
            if (string.IsNullOrEmpty(path)) return;

            var scene = GD.Load<PackedScene>(path);
            if (scene == null) return;

            var node = scene.Instantiate<Node2D>();
            if (node is not RigidBodyWorldItemEntity clone)
            {
                node?.QueueFree();
                return;
            }

            clone.LastDroppedBy = Actor;
            clone.Modifiers = original.Modifiers;
            clone.IsDisposableCopy = original.IsDisposableCopy;
            clone.LandingOffsetYDelta = landingDeltaY; // 落点纵向错开(发射前设,抛物线重算落点即生效)

            if (original.GetParent() is not Node parent)
            {
                clone.QueueFree();
                return;
            }
            parent.AddChild(clone);

            var origin = FindBody(original);
            clone.GlobalPosition = origin?.GlobalPosition ?? original.GlobalPosition;
            clone.AddToGroup(WorldItemSpawner.StageWorldItemsGroup);
            clone.AddToGroup(RigidBodyWorldItemEntity.ThrowCorePieceIdentityTag); // 分裂件销毁仍算"件"(A_004 联动)
            clone.SetMeta("throwcore_born_ms", Time.GetTicksMsec()); // A_004 误爆防护

            // 原件是 A_007 复制件 → 克隆继承复制身份与乱码滤镜(A_008 与 A_007 联动)
            if (original.IsInGroup(RigidBodyWorldItemEntity.ThrowCoreCopyTag))
            {
                clone.AddToGroup(RigidBodyWorldItemEntity.ThrowCoreCopyTag);
                PieceCopyGlitchDecorator.Apply(clone);
            }

            clone.ApplyThrowImpulse(velocity);
        }

        private static RigidBody2D? FindBody(RigidBodyWorldItemEntity entity)
        {
            var body = entity.GetNodeOrNull<RigidBody2D>("RigidBody2D");
            if (body == null)
            {
                foreach (var child in entity.GetChildren())
                {
                    if (child is RigidBody2D rb) { body = rb; break; }
                }
            }
            return body;
        }
    }
}
