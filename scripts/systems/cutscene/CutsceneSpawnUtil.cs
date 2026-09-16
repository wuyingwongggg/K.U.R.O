using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 过场生成步骤的公共工具。目前只有"生成时属性覆盖"一件事：
    /// 让同一个通用场景（如 SlideRail.tscn）在生成多次时能带上各自不同的配置，
    /// 不必为了左右/不同参数复制出多个变体场景（与 AttackEffectEntry.PropertyOverrides 同一套语义）。
    /// </summary>
    public static class CutsceneSpawnUtil
    {
        /// <summary>
        /// 应用生成时属性覆盖。**必须在 AddChild 之前调用**——节点 _Ready 里读取的配置
        /// （例如滑槽的 FlipCarriageEnds / CarriagePrefab / 限位 Marker 路径）必须已经是覆盖后的值，
        /// 晚于入树就晚了。
        /// 属性名写错时打警告：静默失败最容易变成"配了没反应"。
        /// </summary>
        public static void ApplyPropertyOverrides(Node? target,
            Godot.Collections.Dictionary<string, Variant>? overrides, string context)
        {
            if (target == null || overrides == null || overrides.Count == 0) return;

            var known = new Godot.Collections.Array<StringName>();
            foreach (var prop in target.GetPropertyList())
            {
                if (prop.TryGetValue("name", out Variant nameVariant))
                    known.Add(nameVariant.AsStringName());
            }

            foreach (var pair in overrides)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;

                var propertyName = (StringName)pair.Key;
                if (!known.Contains(propertyName))
                {
                    GD.PushWarning($"[Cutscene] {context}: 属性覆盖失败——{target.Name} 上不存在属性 '{pair.Key}'");
                    continue;
                }

                try
                {
                    target.Set(propertyName, pair.Value);

                    // 回读校验：Godot 对类型不符的赋值可能静默忽略（给 PackedScene 属性塞错资源尤其容易），
                    // 只靠 Set 不报错会变成"配了没生效"。数值被自动转换不算错，所以只查两种情形：
                    // 回读为 nil（没写进去），或双方都是对象但不是同一个。
                    if (pair.Value.VariantType != Variant.Type.Nil)
                    {
                        Variant readBack = target.Get(propertyName);
                        bool mismatch = readBack.VariantType == Variant.Type.Nil
                            || (pair.Value.VariantType == Variant.Type.Object
                                && readBack.As<GodotObject>() != pair.Value.As<GodotObject>());
                        if (mismatch)
                            GD.PushWarning($"[Cutscene] {context}: 覆盖属性 '{pair.Key}' 似乎未生效（值类型不匹配？）");
                    }
                }
                catch (System.Exception ex)
                {
                    GD.PushWarning($"[Cutscene] {context}: 覆盖属性 '{pair.Key}' 失败: {ex.Message}");
                }
            }
        }
    }
}
