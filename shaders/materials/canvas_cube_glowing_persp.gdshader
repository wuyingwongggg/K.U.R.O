shader_type canvas_item;

// ══════════════════════════════════════════════════════════════════════
// 发光线框立方体(几何正确相机版)
// 与 cube_faces_ray 同相机:射线-立方体求交 + 三轴斜度滑块 + 透视。
// 线框亮度 = 命中点到最近棱的 3D 距离衰减(无需分面/UV 映射)。
// ══════════════════════════════════════════════════════════════════════

// ── 相机 / 变换(三轴独立滑块)──────────────────────────────
uniform float rotation_x_deg : hint_range(-180.0, 180.0) = 25.0;
uniform float rotation_y_deg : hint_range(-180.0, 180.0) = 45.0;
uniform float rotation_z_deg : hint_range(-180.0, 180.0) = 0.0;

uniform vec2 position_offset = vec2(0.0, 0.0);
uniform float cube_size : hint_range(0.05, 1.0) = 0.6;
uniform float perspective : hint_range(0.5, 10.0) = 3.0;

// 盒半尺寸(三轴独立):各 0.5 = 正方体;分轴滑块可调长/宽/高
uniform float box_half_x : hint_range(0.05, 1.0) = 0.5;
uniform float box_half_y : hint_range(0.05, 1.0) = 0.5;
uniform float box_half_z : hint_range(0.05, 1.0) = 0.5;


// ── 线框视觉 ───────────────────────────────────────────────
uniform vec4 edge_color : source_color = vec4(0.0, 1.0, 0.0, 1.0);

// 棱线发光衰减宽度(盒单位;越大线越粗越柔,正方体 0.5 半尺寸时可参考 0.005..0.3)
uniform float edge_softness : hint_range(0.005, 0.3) = 0.05;

// 整体透明度(淡入淡出用)
uniform float alpha : hint_range(0.0, 1.0) = 1.0;

mat3 rotate_x(float angle) {
    float s = sin(angle); float c = cos(angle);
    return mat3(vec3(1.0, 0.0, 0.0), vec3(0.0, c, -s), vec3(0.0, s, c));
}
mat3 rotate_y(float angle) {
    float s = sin(angle); float c = cos(angle);
    return mat3(vec3(c, 0.0, s), vec3(0.0, 1.0, 0.0), vec3(-s, 0.0, c));
}
mat3 rotate_z(float angle) {
    float s = sin(angle); float c = cos(angle);
    return mat3(vec3(c, -s, 0.0), vec3(s, c, 0.0), vec3(0.0, 0.0, 1.0));
}

bool intersect_box(vec3 ray_origin, vec3 ray_direction,
                   vec3 box_min, vec3 box_max, out float t_near) {
    vec3 inv_dir = 1.0 / ray_direction;
    vec3 t0 = (box_min - ray_origin) * inv_dir;
    vec3 t1 = (box_max - ray_origin) * inv_dir;
    vec3 tmin = min(t0, t1);
    vec3 tmax = max(t0, t1);
    t_near = max(max(tmin.x, tmin.y), tmin.z);
    float t_far = min(min(tmax.x, tmax.y), tmax.z);
    return t_near <= t_far && t_far > 0.0;
}

void fragment() {
    vec2 uv = (UV - 0.5) - position_offset;
    uv /= cube_size;

    vec3 ray_origin = vec3(0.0, 0.0, perspective);
    vec3 ray_direction = normalize(vec3(uv, -perspective));

    mat3 rotation =
        rotate_y(radians(rotation_y_deg))
        * rotate_x(radians(rotation_x_deg))
        * rotate_z(radians(rotation_z_deg));
    mat3 inverse_rotation = transpose(rotation);

    vec3 ro = inverse_rotation * ray_origin;
    vec3 rd = inverse_rotation * ray_direction;

    vec3 box_half = vec3(box_half_x, box_half_y, box_half_z);

    float t_near;
    if (!intersect_box(ro, rd, -box_half, box_half, t_near)) {
        COLOR = vec4(0.0);
        discard;
    }

    vec3 hp = ro + rd * t_near; // 命中点(盒局部,坐标在 ±box_half 内)

    // 命中点必在某面上:该固定轴余量 box_half_i - |hp_i| ≈ 0,不能用于判棱。
    // 距棱距离 = 另外两个自由轴各自余量的较小者 = 三个余量中的中位数
    // (面轴余量为 0,中位数即跳过面轴后的次小值,长方盒各轴半尺寸不同也正确)
    vec3 margin = box_half - abs(hp);
    float d_edge = min(max(margin.x, margin.y),
                       min(max(margin.x, margin.z), max(margin.y, margin.z)));

    // 发光衰减:棱上最亮,向内指数衰减
    float glow = exp(-d_edge / max(edge_softness, 0.001));
    glow = clamp(glow, 0.0, 1.0);
    // 二次增强让棱更锐、远处面边缘仍可见
    glow = glow * (1.35 - 0.35 * glow);

    COLOR = vec4(edge_color.rgb, glow * alpha * edge_color.a);
}
