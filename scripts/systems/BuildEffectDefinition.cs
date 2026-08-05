using Godot;
using Godot.Collections;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Systems
{
    /// <summary>
    /// 构筑效果定义（三选一弹窗中的可选项）。
    /// </summary>
    public enum BuildRarity { Common, Rare, Epic, Core }

    [GlobalClass]
    public partial class BuildEffectDefinition : Resource
    {
        [ExportGroup("Identity")]
        [Export] public string EffectId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "未命名效果";
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

        [ExportGroup("Build Class")]
        [Export] public string BuildClass { get; set; } = string.Empty;

        [ExportGroup("Rarity")]
        [Export] public BuildRarity Rarity { get; set; } = BuildRarity.Common;
        [Export(PropertyHint.Range, "0,100,1")] public int Weight { get; set; } = 10;

        [ExportGroup("Stacking")]
        /// <summary>0 = 不可重复选取。>0 = 最多可重复选取次数。</summary>
        [Export(PropertyHint.Range, "0,10,1")] public int MaxStacks { get; set; } = 1;

        [ExportGroup("Presentation")]
        [Export] public Texture2D? Icon { get; set; }

        [ExportGroup("Effects")]
        [Export] public Array<AttackEffectEntry> EffectEntries { get; set; } = new();

        /// <summary>首个 EffectEntry 的 PropertyOverrides（模板填充与层级数值的来源），无则返回 null。</summary>
        public Godot.Collections.Dictionary<string, Variant>? GetEffectOverrides()
        {
            if (EffectEntries == null || EffectEntries.Count == 0)
                return null;
            return EffectEntries[0].PropertyOverrides;
        }

        /// <summary>
        /// 从 PropertyOverrides 按名读取浮点数组（如 "TierValues"、"GainValues"）。
        /// 供描述模板 {数组名:下标} 填充与当前层高亮。无则返回 null。
        /// </summary>
        public float[]? GetOverrideFloatArray(string name)
        {
            var overrides = GetEffectOverrides();
            if (overrides == null || !overrides.TryGetValue(name, out var value))
                return null;

            if (value.Obj is Godot.Collections.Array godotArray)
            {
                var result = new float[godotArray.Count];
                for (int i = 0; i < godotArray.Count; i++)
                    result[i] = godotArray[i].AsSingle();
                return result;
            }

            if (value.Obj is System.Collections.IEnumerable enumerable)
            {
                var list = new System.Collections.Generic.List<float>();
                foreach (var item in enumerable)
                    list.Add(System.Convert.ToSingle(item));
                return list.Count > 0 ? list.ToArray() : null;
            }

            return null;
        }

        /// <summary>读取默认层级数值数组（TierValues），无则返回 null。</summary>
        public float[]? GetTierValues()
        {
            return GetOverrideFloatArray("TierValues");
        }
    }
}
