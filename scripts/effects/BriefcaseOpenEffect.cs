using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Fx;

namespace Kuros.Effects
{
    /// <summary>
    /// 公文包打开效果（ActorEffect + IWorldSpawnable）。
    /// 延迟 DelaySeconds 秒后在落点生成 EffectScene，生成的特效独立存在（不随本场景销毁）。
    /// SpawnInterval > 0 时在 DelaySeconds → Duration 期间每 SpawnInterval 秒重复生成；
    /// SpawnInterval = 0 时仅生成一次。
    /// 本场景自身按 ActorEffect.Duration 到期后自我销毁。
    /// </summary>
    [GlobalClass]
    public partial class BriefcaseOpenEffect : ActorEffect, IWorldSpawnable
    {
        [Export] public PackedScene? EffectScene { get; set; }
        [Export] public Godot.Collections.Dictionary<string, Variant> EffectOverrides { get; set; } = new();
        [Export(PropertyHint.Range, "0,10,0.1")] public float DelaySeconds = 1.0f;
        [Export(PropertyHint.Range, "0,10,0.01")] public float SpawnInterval = 0f;
        /// <summary>
        /// 特效生成锚点（Marker2D 放在本场景节点下）。不为空时按数组顺序依次轮换使用
        /// Marker2D.GlobalPosition 作为生成位置，否则用当前 global_position（投掷落点）。
        /// </summary>
        [Export] public Marker2D[] SpawnMarkers = System.Array.Empty<Marker2D>();

        public Vector2? WorldSpawnPosition { get; set; }

        private float _delayElapsed;
        private float _intervalElapsed;
        private int _spawnCount;
        private int _spawnMarkerIndex;

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

            // 按投掷者朝向翻转自身场景（场景根为 Node2D，但脚本类继承 Node，用属性反射）
            ApplyFacingByReflection(this, Actor?.FacingRight ?? true);
        }

        protected override void OnTick(double delta)
        {
            float dt = (float)delta;
            _delayElapsed += dt;

            if (_delayElapsed < DelaySeconds) return;

            if (_spawnCount == 0)
            {
                // 延迟结束 → 首次生成
                SpawnEffect();
                _intervalElapsed = 0f;
            }
            else if (SpawnInterval > 0f)
            {
                // 周期性重复生成，直到 Duration 到期自身销毁
                _intervalElapsed += dt;
                while (_intervalElapsed >= SpawnInterval)
                {
                    _intervalElapsed -= SpawnInterval;
                    SpawnEffect();
                }
            }
        }

        protected override void OnExpire()
        {
            if (_spawnCount == 0) SpawnEffect();
            base.OnExpire();
        }

        public override void OnRemoved()
        {
            if (_spawnCount == 0) SpawnEffect();
            base.OnRemoved();
        }

        private void SpawnEffect()
        {
            _spawnCount++;

            var world = GetParent();
            if (world == null || EffectScene == null) return;

            var pos = Get("global_position").AsVector2();

            // 指定 SpawnMarkers 时按数组顺序轮换取 Marker2D.GlobalPosition 作为生成位置
            if (SpawnMarkers.Length > 0)
            {
                var marker = SpawnMarkers[_spawnMarkerIndex % SpawnMarkers.Length];
                _spawnMarkerIndex++;
                if (marker != null && GodotObject.IsInstanceValid(marker))
                    pos = marker.GlobalPosition;
            }

            var node = EffectScene.Instantiate();

            if (node is Node2D node2D)
            {
                // 独立加入世界层：不挂在本场景下，随本场景销毁而销毁
                world.AddChild(node2D);
                node2D.GlobalPosition = pos;

                if (node2D is BoomDmgEffect boom)
                    boom.Attacker = Actor;
                if (node2D is RotatingCube cube)
                    cube.Attacker = Actor;

                foreach (var pair in EffectOverrides)
                {
                    if (pair.Key == null) continue;
                    try { node2D.Set(pair.Key, pair.Value); }
                    catch (System.Exception ex) { GD.PushWarning($"[BriefcaseOpenEffect] override '{pair.Key}' failed: {ex.Message}"); }
                }

                // 朝向翻转放最后：确保 overrides 覆盖 scale 后仍按投掷者朝向翻转
                ApplyFacing(node2D, Actor?.FacingRight ?? true);
                // 同步飞行基准方向（RotatingCube 等 IFacingDirectional 目标在无追踪目标时按此朝向飞行）
                if (node2D is IFacingDirectional facing)
                    facing.FacingRight = Actor?.FacingRight ?? true;
            }
            else
            {
                node?.QueueFree();
            }
        }

        /// <summary>按投掷者朝向翻转节点（面向左时 Scale.X 取反）。</summary>
        private static void ApplyFacing(Node2D node, bool facingRight)
        {
            var scale = node.Scale;
            scale.X = Mathf.Abs(scale.X) * (facingRight ? 1f : -1f);
            node.Scale = scale;
        }

        /// <summary>通过属性反射翻转节点（脚本类继承 Node 时无法直接类型转换）。</summary>
        private static void ApplyFacingByReflection(Node node, bool facingRight)
        {
            if (!node.HasMethod("get_scale") || !node.HasMethod("set_scale")) return;
            var scale = (Vector2)node.Call("get_scale");
            scale.X = Mathf.Abs(scale.X) * (facingRight ? 1f : -1f);
            node.Call("set_scale", scale);
        }
    }
}
