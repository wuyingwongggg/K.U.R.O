using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 无人机眩晕效果：瞬间检测范围内是否存在 Boss 的 StunArea。
    /// 命中后对敌人应用 FreezeEffect，持续时间为 effect.Duration。
    /// 使用物理空间直接查询，检测完毕后立即停用 Area2D。
    /// </summary>
    [GlobalClass]
    public partial class DroneStunEffect : ActorEffect, IWorldSpawnable
    {
        public Vector2? WorldSpawnPosition { get; set; }

        private Area2D? _area;
        private bool _applied;

        protected override void OnApply()
        {
            base.OnApply();
            EffectId = $"drone_stun_{GetInstanceId()}";

            _area = GetNodeOrNull<Area2D>("Area2D");
            if (_area == null) return;

            if (WorldSpawnPosition.HasValue)
                _area.GlobalPosition = WorldSpawnPosition.Value;

            CallDeferred("InstantScan");
        }

        private void InstantScan()
        {
            if (_applied || _area == null || !IsInstanceValid(_area))
            {
                CleanupAndRemoveSelf();
                return;
            }

            var shapeNode = _area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (shapeNode?.Shape == null)
            {
                CleanupAndRemoveSelf();
                return;
            }

            var spaceState = _area.GetWorld2D().DirectSpaceState;
            if (spaceState == null)
            {
                CleanupAndRemoveSelf();
                return;
            }

            Vector2 center = WorldSpawnPosition ?? _area.GlobalPosition;
            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = shapeNode.Shape,
                Transform = new Transform2D(0f, center),
                CollisionMask = 1u,
                CollideWithAreas = true,
                CollideWithBodies = false,
            };

            foreach (var result in spaceState.IntersectShape(query))
            {
                if (!result.TryGetValue("collider", out var colliderVar)) continue;
                if (colliderVar.As<GodotObject>() is not Area2D other) continue;
                if (other.Name != "StunArea") continue;

                var enemy = other.GetParent()?.GetParent() as GameActor;
                if (enemy == null || !IsInstanceValid(enemy)) continue;
                if (enemy.ActiveImmunities.HasFlag(ImmunityFlags.Stun)) continue;

                _applied = true;

                // Remove any previous drone stun, clear stale Frozen state, apply new one
                var existing = enemy.EffectController?.GetEffect<FreezeEffect>();
                if (existing != null && existing.EffectId?.StartsWith("drone_stun_") == true)
                    enemy.RemoveEffect(existing.EffectId);
                enemy.FrozenStateRemainingTime = 0f;

                enemy.ApplyEffect(new FreezeEffect
                {
                    Duration = this.Duration,
                    EffectId = $"drone_stun_{GetInstanceId()}"
                });
                break;
            }

            CleanupAndRemoveSelf();
        }

        private void CleanupAndRemoveSelf()
        {
            if (_area != null && IsInstanceValid(_area))
            {
                _area.Monitoring = false;
                _area.Monitorable = false;
            }
            Target?.RemoveEffect(EffectId);
        }

        private GameActor? Target => (GameActor?)GetParent();
    }
}
