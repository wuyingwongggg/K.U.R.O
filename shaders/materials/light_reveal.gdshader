shader_type canvas_item;

// 逐盏亮灯圆形光效（配合 Polygon2D 黑幕使用）：
// light_center 处绘制一个透明圆（透出场景），圆外为 dark_color 黑色阴影。
// reveal_progress 从 0 → 1 时，圆从中心快速扩散直到覆盖整个多边形区域（灯一盏一盏亮起）。
// 无光照系统的替代方案：圆内透明模拟灯光，圆外半透明黑模拟阴影。

uniform vec4 dark_color : source_color = vec4(0.0, 0.0, 0.0, 0.85);
// 灯光色：圆内的半透明白色光晕（alpha 越大越浓白，场景透出呈白光感）
uniform vec4 light_color : source_color = vec4(1.0, 1.0, 1.0, 0.3);
uniform vec2 light_center = vec2(0.0, 0.0);
uniform float reveal_progress : hint_range(0.0, 1.0) = 0.0;
uniform float max_radius : hint_range(0.0, 5000.0) = 2000.0;
uniform float softness : hint_range(0.0, 5000.0) = 80.0;

varying vec2 local_pos;

void vertex() {
    // vertex 阶段的 VERTEX 是可靠的局部坐标（相对节点原点，不含 position/scale）；
    // fragment 阶段不能直接用 VERTEX（已过变换），必须经 varying 插值传递。
    local_pos = VERTEX;
}

void fragment() {
    float dist = distance(local_pos, light_center);
    float reach = reveal_progress * max_radius;
    float lit = 1.0 - smoothstep(reach - softness, reach, dist);
    // 圆内 = 白色灯光（半透明，透出场景呈白光感）；圆外 = 黑色阴影；边缘按 lit 混合过渡
    COLOR = mix(dark_color, light_color, lit);
}
