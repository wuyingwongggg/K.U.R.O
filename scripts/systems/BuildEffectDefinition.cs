using Godot;
using Godot.Collections;

namespace Kuros.Systems
{
    /// <summary>
    /// 构筑效果定义（三选一弹窗中的可选项）。
    /// 纯数值效果填写 StatBonuses；复杂效果额外指定 EffectScene。
    /// </summary>
    [GlobalClass]
    public partial class BuildEffectDefinition : Resource
    {
        [ExportGroup("基础信息")]
        [Export] public string EffectId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "未命名效果";
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

        [ExportGroup("构筑类型")]
        /// <summary>所属构筑类别（Machine / Waiter / Throw / Generic）。用于核心过滤。</summary>
        [Export] public string BuildClass { get; set; } = string.Empty;

        [ExportGroup("数值加成")]
        /// <summary>
        /// 选择后直接施加的属性修正（经 BuildStatBonusEffect）。
        /// Key: "attack_damage", "speed", "max_health" 等。
        /// </summary>
        [Export] public Dictionary<string, float> StatBonuses { get; set; } = new();

        [ExportGroup("表现")]
        [Export] public Texture2D? Icon { get; set; }

        [ExportGroup("复杂效果")]
        /// <summary>可选的自定义 ActorEffect 场景（PackedScene）。非空时实例化并加入 EffectController。</summary>
        [Export] public PackedScene? EffectScene { get; set; }
    }
}
