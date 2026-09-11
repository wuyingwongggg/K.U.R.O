using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;
using Kuros.Items.World;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 故障析出（BuildThrow_B_007）：一次性道具(IsFurniture)被摧毁时,有 30%/50%(层1/层2)概率
    /// 在销毁点生成一件乱码块(经 ThrowCore 生成管线入组/标记,含 A_006 升档）。
    /// 事件源: RigidBodyWorldItemEntity.Destroyed(在 QueueFree 之前触发,件仍有效可读位置——
    /// 用内部 RigidBody2D 坐标,飞行/回弹中 wrapper 根位置不同步)。
    /// 析出件同样可被再次摧毁并掷骰(全部销毁路径一致);概率 &lt;1 时链式期望收敛,不会无限增殖。
    /// </summary>
    [GlobalClass]
    public partial class ThrowGlitchPrecipitationEffect : ActorEffect
    {
        /// <summary>各层触发概率百分比(B_007 注入 [30, 50])。</summary>
        [Export] public float[] TierValues { get; set; } = { 30f, 50f };

        private int _tier = 1;
        private ThrowCoreEffect? _core;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            RigidBodyWorldItemEntity.Destroyed += OnEntityDestroyed;
        }

        protected override void OnStackRefreshed()
        {
            _core ??= Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        public override void OnRemoved()
        {
            RigidBodyWorldItemEntity.Destroyed -= OnEntityDestroyed;
            _core = null;
            base.OnRemoved();
        }

        private void OnEntityDestroyed(RigidBodyWorldItemEntity entity)
        {
            if (_core == null || !GodotObject.IsInstanceValid(_core)) return;
            if (entity == null || !GodotObject.IsInstanceValid(entity)) return;

            // 仅一次性道具(可投掷且非投掷武器)
            if (entity.ItemDefinition?.IsFurniture != true) return;

            float chance = TierValues.Length > 0
                ? TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1]
                : 0f;
            if (chance <= 0f || GD.Randf() * 100f >= chance) return;

            _core.SpawnPieceAt(ResolveDestroyPosition(entity));
        }

        /// <summary>取实际销毁位置:飞行/回弹中 wrapper 根位置不同步(停在生成点),内部 RigidBody2D 才是真实位置。</summary>
        private static Vector2 ResolveDestroyPosition(RigidBodyWorldItemEntity entity)
        {
            var body = entity.GetNodeOrNull<RigidBody2D>("RigidBody2D");
            if (body == null)
            {
                foreach (var child in entity.GetChildren())
                {
                    if (child is RigidBody2D rb) { body = rb; break; }
                }
            }
            return body != null && GodotObject.IsInstanceValid(body)
                ? body.GlobalPosition
                : entity.GlobalPosition;
        }
    }
}
