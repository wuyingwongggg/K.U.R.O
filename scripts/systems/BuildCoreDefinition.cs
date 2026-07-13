using Godot;

namespace Kuros.Systems
{
    /// <summary>
    /// 构筑核心定义（开局 N选1）。选择后决定后续所有构筑效果的 BuildClass 过滤方向。
    /// </summary>
    [GlobalClass]
    public partial class BuildCoreDefinition : Resource
    {
        [ExportGroup("基础信息")]
        [Export] public string CoreId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "未命名核心";
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

        [ExportGroup("构筑")]
        [Export] public string BuildClass { get; set; } = string.Empty;

        [ExportGroup("效果")]
        [Export] public PackedScene? CoreEffectScene { get; set; }

        [ExportGroup("表现")]
        [Export] public Texture2D? Icon { get; set; }
    }
}
