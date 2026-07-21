using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Fx;

namespace Kuros.Effects
{
    /// <summary>
    /// Emoji 炸弹效果（ActorEffect + IWorldSpawnable）。
    /// 自动通过 ActorEffect.Actor 追踪攻击者，投掷落点通过 IWorldSpawnable 传递。
    ///
    /// 玩家路径：SpawnThrowDestroyEffects → set WorldSpawnPosition → LastDroppedBy.ApplyEffect → Actor=玩家
    /// 敌人路径：同样通过 ApplyEffect 绑定
    /// </summary>
    public partial class EmojiBoomEffect : ActorEffect, IWorldSpawnable
    {
        [Export] public PackedScene? BoomScene { get; set; }
        [Export] public PackedScene? BoomDmgScene { get; set; }

        [Export]
        public Godot.Collections.Dictionary<string, Variant> BoomSceneOverrides { get; set; } = new();
        [Export]
        public Godot.Collections.Dictionary<string, Variant> BoomDmgOverrides { get; set; } = new();

        public Vector2? WorldSpawnPosition { get; set; }

        private bool _spawned;

        protected override void OnApply()
        {
            // 投掷落点定位：从 EffectController 下 reparent 到世界层，放到投掷落点
            if (WorldSpawnPosition.HasValue)
            {
                var world = Actor?.GetParent();
                if (world != null)
                {
                    Reparent(world);
                    Set("global_position", WorldSpawnPosition.Value);
                }
            }
        }

        protected override void OnExpire()
        {
            SpawnChildEffects();
            base.OnExpire();
        }

        public override void OnRemoved()
        {
            SpawnChildEffects();
            base.OnRemoved();
        }

        private void SpawnChildEffects()
        {
            if (_spawned) return;
            _spawned = true;

            var world = GetParent();
            if (world == null) return;

            var pos = GetGlobalPosition(this);

            SpawnScene(BoomScene, world, pos, BoomSceneOverrides);
            SpawnScene(BoomDmgScene, world, pos, BoomDmgOverrides);
        }

        private void SpawnScene(PackedScene? scene, Node world, Vector2 pos, Godot.Collections.Dictionary<string, Variant> overrides)
        {
            if (scene == null) return;

            var node = scene.Instantiate();
            if (node is Node2D node2D)
            {
                world.AddChild(node2D);
                node2D.GlobalPosition = pos;

                if (node2D is BoomDmgEffect boom)
                    boom.Attacker = Actor;

                foreach (var pair in overrides)
                {
                    if (pair.Key == null) continue;
                    try { node2D.Set(pair.Key, pair.Value); }
                    catch (System.Exception ex) { GD.PushWarning($"[EmojiBoomEffect] override '{pair.Key}' failed: {ex.Message}"); }
                }
            }
            else
            {
                node?.QueueFree();
            }
        }

        private static Vector2 GetGlobalPosition(Node node)
        {
            return node.Get("global_position").AsVector2();
        }
    }
}
