using Godot;
using Godot.Collections;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Systems
{
    /// <summary>
    /// 构筑效果定义（三选一弹窗中的可选项）。
    /// </summary>
    public enum BuildRarity { Common, Rare, Epic }

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
    }
}
