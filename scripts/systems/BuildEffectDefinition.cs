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

        private static readonly System.Text.RegularExpressions.Regex TierTokenRegex = new(
            @"{([A-Za-z_][A-Za-z0-9_]*)?:?(\d+)}");

        /// <summary>
        /// 描述模板填充：把 {数组名:下标} 占位符替换为 PropertyOverrides 中对应数组的实际数值。
        /// 简写 {i} 等价于 {TierValues:i}；多个数组（如 GainValues/HeatCostValues）共享同一当前层索引。
        /// 当前层（stacks）金色高亮，其余层暗色；找不到数组 / 下标越界 / 无占位符时原样返回。
        /// 用于三选一卡片与技能详情窗口显示效果的实际数值。
        /// </summary>
        public string BuildDescriptionWithValues(int stacks)
        {
            string template = Description;
            if (!template.Contains('{'))
                return template;

            return TierTokenRegex.Replace(template, match =>
            {
                string arrayName = match.Groups[1].Success && match.Groups[1].Value.Length > 0
                    ? match.Groups[1].Value
                    : "TierValues";
                int index = int.Parse(match.Groups[2].Value);

                var values = GetOverrideFloatArray(arrayName);
                if (values == null || index < 0 || index >= values.Length)
                    return match.Value; // 无数据 → 保留原文

                int tierIndex = Mathf.Clamp(stacks, 0, values.Length - 1);

                // 修改器百分比为负（减容/缓速类）时，描述按数值大小显示（降低 10%，而非 -10%）
                string valueText = Mathf.Abs(values[index]).ToString();
                return index == tierIndex
                    ? $"[color=#FFD700]{valueText}[/color]"
                    : $"[color=#8A8A8A]{valueText}[/color]";
            });
        }
    }
}
