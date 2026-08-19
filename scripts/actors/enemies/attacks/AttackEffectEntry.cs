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
