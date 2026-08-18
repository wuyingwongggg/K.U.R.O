shader_type canvas_item;
render_mode blend_add;

// 灯光光圈（Add 混合）：光斑贴图（如 spotlight_1.png）叠加在暗层之上，
// 覆盖处 = 暗层颜色 + 光色 → 被照亮。配合 scale 扩散模拟灯一盏盏亮起。

uniform vec4 light_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
    COLOR = texture(TEXTURE, UV) * light_color;
}
