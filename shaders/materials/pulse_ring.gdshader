shader_type canvas_item;

// ══════════════════════════════════════════════════════════════════════
// 伪3D 脉冲扩散环
// 投影技法移植自 pseudo_3d_card(卡片透视):把环纹绘制在"卡片平面"上,
// 再用 rot_x/rot_y + fov 做透视投影 —— 与场上的伪3D 立方体同一视觉语言,
// 可通过调 rot 让环"躺"在伪3D 世界的地面上围绕块体扩散。
// progress 0→1 半径从 18% 缓扩到 100%,整段淡出;无屏幕采样。
// ══════════════════════════════════════════════════════════════════════

uniform vec4 ring_color : source_color = vec4(0.2, 1.0, 0.55, 1.0);
uniform float progress : hint_range(0.0, 1.0) = 0.0;
// 主环带宽度(相对半径比例;0.08×半径≈实际像素宽度)
uniform float thickness : hint_range(0.01, 0.25) = 0.08;

// ── 伪3D 卡片透视(同 pseudo_3d_card)──
uniform bool cull_backface = false;
uniform float fov : hint_range(1.0, 179.0) = 45.0;
uniform float rot_x_deg : hint_range(-180.0, 180.0) = 60.0;
uniform float rot_y_deg : hint_range(-180.0, 180.0) = 0.0;

varying vec2 card_offset;
varying vec3 world_pos;

void vertex() {
    float ry = radians(rot_y_deg);
    float rx = radians(rot_x_deg);
    float sy = sin(ry), cy = cos(ry);
    float sx = sin(rx), cx = cos(rx);

    mat3 rotation;
    rotation[0] = vec3(cy, 0.0, -sy);
    rotation[1] = vec3(sy * sx, cx, cy * sx);
    rotation[2] = vec3(sy * cx, -sx, cy * cx);

    float perspective_scale = tan(radians(fov) * 0.5);
    world_pos = rotation * vec3(UV - 0.5, 0.5 / perspective_scale);

    float depth_scale = (0.5 / perspective_scale) + 0.5;
    world_pos.xy *= depth_scale * rotation[2].z;
    card_offset = depth_scale * rotation[2].xy;

    // 扩大 quad 覆盖倾斜后的投影范围(同 pseudo_3d_card;环本身仍锚定原半径)
    VERTEX += (UV - 0.5) / TEXTURE_PIXEL_SIZE * perspective_scale;
}

float band(float front, float d) {
    return (1.0 - smoothstep(front - 0.1, front, d))
         * smoothstep(front - thickness - 0.1, front - thickness, d);
}

void fragment() {
    if (cull_backface && world_pos.z <= 0.0) discard;

    // 投影回"卡片平面"的采样坐标(原 pseudo_3d_card 用于纹理取样,此处用于环的径向几何)
    vec2 plane_uv = (world_pos.xy / world_pos.z) - card_offset + 0.5;
    float d = length(plane_uv - vec2(0.5)) * 2.0;

    float radius = mix(0.18, 1.0, pow(progress, 0.75));
    float main = band(radius, d);
    float trail = band(radius * 0.72, d) * 0.45;
    float fade = 1.0 - progress;
    float alpha = (main + trail) * fade * ring_color.a;
    float glow = alpha * (1.35 - 0.35 * alpha);
    COLOR = vec4(ring_color.rgb, clamp(glow, 0.0, 1.0));
}
