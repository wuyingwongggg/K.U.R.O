shader_type canvas_item;

// 暗层遮罩（真灯光效果）：整块暗影覆盖房间，N 个光圈位置处画透明圆——
// 光圈（灯光）覆盖的圆形区域 alpha 归零（完全露出下方场景），其余保持 dark_color 阴影。
// 光圈位置/半径由控制器每帧写入（光圈节点仅提供世界坐标，不渲染自身）。

uniform vec4 dark_color : source_color = vec4(0.0, 0.0, 0.0, 0.85);
uniform int light_count = 0;
uniform vec2 light_positions[16];
uniform float light_radii[16];
uniform float softness : hint_range(0.0, 500.0) = 80.0;

varying vec2 local_pos;

void vertex() {
    // vertex 阶段的 VERTEX 是可靠的局部坐标（相对节点原点）；fragment 不能直接用 VERTEX，经 varying 传递。
    local_pos = VERTEX;
}

void fragment() {
    float lit = 0.0;
    for (int i = 0; i < light_count; i++) {
        float dist = distance(local_pos, light_positions[i]);
        float reach = light_radii[i];
        // 圆内完全透明（露出场景），边缘按 softness 柔化；多盏灯取最大照亮
        lit = max(lit, 1.0 - smoothstep(reach - softness, reach, dist));
    }
    COLOR = vec4(dark_color.rgb, dark_color.a * (1.0 - lit));
}
