shader_type canvas_item;

// ═══════════════════════════════════════════════════════════════
//  伪 3D（同 fire_effect / fire_cartoon：顶点旋转 + 透视投影）
// ═══════════════════════════════════════════════════════════════
uniform bool cull_backface = true;                 // 背面剔除（旋转超过 90° 时隐藏背面）
uniform float fov : hint_range(1, 179) = 1.0;      // 透视强度（越小透视越强）
uniform float rot_x_deg : hint_range(-360, 360) = 0.0; // X 轴旋转（俯仰）
uniform float rot_y_deg : hint_range(-360, 360) = 0.0; // Y 轴旋转（水平）
uniform float inset : hint_range(0, 1) = 0.0;      // 内缩量（顶点向中心收缩）

varying vec2 proj_offset;
varying vec3 world_pos_3d;

uniform sampler2D noise1:repeat_enable;
uniform sampler2D noise2:repeat_enable;
uniform vec3 tint:source_color;
uniform float influence:hint_range(0.0,30.0, 0.1);
uniform float amount:hint_range(0.0,1.0, 0.01);

// 顶点阶段：旋转 + 透视投影（同 fire_effect），把精灵四角位移到伪 3D 位置
void vertex() {
    float rot_y_rad = radians(rot_y_deg);
    float rot_x_rad = radians(rot_x_deg);

    float sin_y = sin(rot_y_rad), cos_y = cos(rot_y_rad);
    float sin_x = sin(rot_x_rad), cos_x = cos(rot_x_rad);

    mat3 rotation_matrix;
    rotation_matrix[0] = vec3(cos_y, 0.0, -sin_y);
    rotation_matrix[1] = vec3(sin_y * sin_x, cos_x, cos_y * sin_x);
    rotation_matrix[2] = vec3(sin_y * cos_x, -sin_x, cos_y * cos_x);

    float perspective_scale = tan(radians(fov) * 0.5);
    world_pos_3d = rotation_matrix * vec3(UV - 0.5, 0.5 / perspective_scale);

    float depth_scale = (0.5 / perspective_scale) + 0.5;
    world_pos_3d.xy *= depth_scale * rotation_matrix[2].z;
    proj_offset = depth_scale * rotation_matrix[2].xy;

    VERTEX += (UV - 0.5) / TEXTURE_PIXEL_SIZE * perspective_scale * (1.0 - inset);
}

void fragment() {
    // ── 伪 3D：背面剔除 + 透视投影出当前片元对应的 UV ──
    if (cull_backface && world_pos_3d.z <= 0.0) discard;

    vec2 projected_uv = (world_pos_3d.xy / world_pos_3d.z) - proj_offset + 0.5;
    if (projected_uv.x < 0.0 || projected_uv.x > 1.0 || projected_uv.y < 0.0 || projected_uv.y > 1.0)
        discard;

    // ── 噪声扭曲（fire_ring 原逻辑，采样改在投影后的 UV 上）──
    float noise1_value = texture(noise1, projected_uv + TIME * 0.15).r - 0.5;
    float noise2_value = texture(noise2, projected_uv - TIME * 0.25).r - 0.5;
    float mixed_noise = noise1_value * noise2_value * influence;

    vec2 offset = vec2(0.02, 0.15) * mixed_noise;
    COLOR = texture(TEXTURE, projected_uv + offset);   // 精灵纹理扭曲采样（形状）
    COLOR.rgb = tint;                                   // 平涂色

    noise1_value = texture(noise1, projected_uv + TIME * 0.15).r;
    noise2_value = texture(noise2, projected_uv - TIME * 0.25).r;
    mixed_noise = noise1_value * noise2_value;

    COLOR.a *= 1.0 - mixed_noise * 6.0 * amount;        // 噪声调制透明度
    COLOR.a *= 1.0 - amount;
}
