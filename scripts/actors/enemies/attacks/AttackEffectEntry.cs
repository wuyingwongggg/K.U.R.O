using Godot;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies.Attacks
{
    [GlobalClass]
    public partial class AttackEffectEntry : Resource
    {
        [Export] public PackedScene? Scene { get; set; }

        /// <summary>唯一性组：实例化后特效节点加入此组（供攻击选择"场上是否已有该特效"检测）。
        /// 空 = 不标记。特效销毁后组引用自动失效（检测时用 IsInstanceValid 过滤）。</summary>
        [Export] public string UniqueGroup { get; set; } = string.Empty;

        // ── 每特效独立生成配置（默认"继承"哨兵——未配置时回退模板级字段，旧场景零失效）──

        /// <summary>独立生成位置偏移。(NaN, NaN) = 继承模板 EffectOffset。</summary>
        [Export] public Vector2 EffectOffset = new(float.NaN, float.NaN);

        /// <summary>独立生成时机。Inherit = 继承模板 SpawnTiming。</summary>
        [Export] public EffectSpawnTiming SpawnTiming = EffectSpawnTiming.Inherit;

        /// <summary>独立生成锚点路径数组（NodePath 相对攻击模板节点解析，轮换；如 "../../../../Node2D/Marker2D"）。
        /// 空数组 = 继承模板 SpawnMarkers。Resource 不能 export Node 成员（GD0107），故用 NodePath 存储。</summary>
        [Export] public NodePath[] SpawnMarkerPaths = System.Array.Empty<NodePath>();

        /// <summary>独立朝向翻转。Inherit = 继承模板 FlipEffectWithFacing。</summary>
        [Export] public FacingFlipMode FlipMode = FacingFlipMode.Inherit;

        /// <summary>独立阻塞特效组。空 = 继承模板 BlockedByFxGroup。</summary>
        [Export] public string BlockedByFxGroup { get; set; } = string.Empty;

        /// <summary>特效生命周期绑定（EnemyAttackTemplate 用）：对应阶段结束时由模板销毁，替代固定 Duration
        /// （适配 Hold 挂起等阶段时长不固定的场景）。None = 特效自行管理（默认）。</summary>
        [Export] public EffectLifecycleBinding LifecycleBinding { get; set; } = EffectLifecycleBinding.None;

        public Vector2 ResolveOffset(Vector2 fallback)
            => float.IsNaN(EffectOffset.X) ? fallback : EffectOffset;

        public EffectSpawnTiming ResolveTiming(EffectSpawnTiming fallback)
            => SpawnTiming == EffectSpawnTiming.Inherit ? fallback : SpawnTiming;

        /// <summary>
        /// 解析独立锚点：先按 relativeTo（攻击模板自身/敌人根）解析，失败再按 enemyRoot（敌人根）兜底——
        /// 兼容"相对模板节点的 ../ 路径"与"相对敌人根的路径"两种配置习惯。未配置返回 fallback（模板锚点）。
        /// </summary>
        public Marker2D[] ResolveMarkers(Node relativeTo, Node? enemyRoot, Marker2D[] fallback)
        {
            if (SpawnMarkerPaths.Length == 0 || relativeTo == null) return fallback;
            var markers = new Marker2D[SpawnMarkerPaths.Length];
            for (int i = 0; i < SpawnMarkerPaths.Length; i++)
            {
                var path = SpawnMarkerPaths[i];
                markers[i] = relativeTo.GetNodeOrNull<Marker2D>(path)
                    ?? enemyRoot?.GetNodeOrNull<Marker2D>(path);
            }
            return markers;
        }

        public bool ResolveFlip(bool fallback)
            => FlipMode == FacingFlipMode.Inherit ? fallback : FlipMode == FacingFlipMode.Flip;

        public string ResolveBlockedGroup(string fallback)
            => string.IsNullOrEmpty(BlockedByFxGroup) ? fallback : BlockedByFxGroup;

        public ActorEffect? InstantiateEffect()
        {
            if (Scene == null) return null;
            var effect = Scene.Instantiate<ActorEffect>();
            if (effect != null) ApplyOverrides(effect);
            return effect;
        }

        public void ApplyOverrides(Node effect)
        {
            if (effect == null || PropertyOverrides.Count == 0) return;
            foreach (var pair in PropertyOverrides)
            {
                if (pair.Key == null) continue;
                try { effect.Set(pair.Key, pair.Value); }
                catch (System.Exception ex) { GD.PushWarning($"[AttackEffectEntry] override '{pair.Key}' failed: {ex.Message}"); }
            }
        }

        [Export]
        public Godot.Collections.Dictionary<string, Variant> PropertyOverrides { get; set; } = new();
    }
}
