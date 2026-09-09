shader_type canvas_item;

// ══════════════════════════════════════════════════════════════════════
// 字符伪3D立方体(几何正确相机版)
// 面内容 = canvas_cube_faces 的二进制字符雨(0/1 字形/滚动/闪烁/头部渐亮);
// 相机 = 射线-立方体求交 + 显式视角(rotation_deg) + 透视(perspective)。
// 因此可任意调整"斜度"(欧拉角)与"近大远小"(视距),画风保持数据乱码感。
// ══════════════════════════════════════════════════════════════════════

// ── 相机 / 变换 ─────────────────────────────────────────────
// 观察斜度三轴(度):分轴滑块,Inspector 中三个独立滑条可调
uniform float rotation_x_deg : hint_range(-180.0, 180.0) = 25.0;
uniform float rotation_y_deg : hint_range(-180.0, 180.0) = 45.0;
uniform float rotation_z_deg : hint_range(-180.0, 180.0) = 0.0;

// 立方体在四边形内的位置偏移(-0.5..0.5)
uniform vec2 position_offset = vec2(0.0, 0.0);

// 立方体相对四边形尺寸
uniform float cube_size : hint_range(0.05, 1.0) = 0.6;

// 相机距离:越小透视(近大远小)越强
uniform float perspective : hint_range(0.5, 10.0) = 3.0;

// 盒半尺寸(三轴独立):各 0.5 = 正方体;分轴滑块可调长/宽/高
uniform float box_half_x : hint_range(0.05, 1.0) = 0.5;
uniform float box_half_y : hint_range(0.05, 1.0) = 0.5;
uniform float box_half_z : hint_range(0.05, 1.0) = 0.5;


// ── 字符面渲染参数(与 canvas_cube_faces 一致)────────────────
uniform float face_alpha : hint_range(0.0, 1.0) = 0.9;
uniform vec4 face_color : source_color = vec4(0.02, 0.06, 0.02, 1.0);
uniform vec4 text_color : source_color = vec4(0.0, 1.0, 0.16, 1.0);
uniform vec4 head_color : source_color = vec4(0.7, 1.0, 0.8, 1.0);

uniform float text_columns = 10.0;
uniform float text_rows = 10.0;
uniform float scroll_speed = 0.6;
// 闪烁掷骰频率(次/秒):越大翻转越频繁;状态每次保持约 1/(6*rate) 秒
uniform float flicker_rate = 0.4;
uniform float rain_density : hint_range(0.0, 1.0) = 0.7;

// ── 光照 / 轮廓 ─────────────────────────────────────────────
uniform vec3 light_dir = vec3(0.4, 0.8, 0.5);
uniform float ambient : hint_range(0.0, 1.0) = 0.5;
uniform float shading_strength : hint_range(0.0, 1.0) = 0.5;

uniform bool draw_outline = false;
uniform vec4 outline_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float outline_width : hint_range(0.0, 0.05) = 0.01;

// ── 辅助 ────────────────────────────────────────────────────

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float draw_zero(vec2 uv) {
    float thick = 0.12;
    vec2 d = abs(uv - 0.5) * 2.0;
    float outer = step(max(d.x, d.y), 0.85);
    float inner = step(max(d.x, d.y), 0.85 - thick * 2.0);
    return outer - inner;
}

float draw_one(vec2 uv) {
    float v_range = step(0.08, uv.y) * step(uv.y, 0.92);
    float body = (1.0 - step(0.1, abs(uv.x - 0.5))) * v_range;
    float serif_top = 1.0 - step(0.35, abs(uv.x - 0.5));
    serif_top *= step(0.04, uv.y) * step(uv.y, 0.24);
    float serif_bot = 1.0 - step(0.25, abs(uv.x - 0.65));
    serif_bot *= step(0.8, uv.y);
    return max(max(body, serif_top), serif_bot);
}

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

/// 字符面内容:face_uv∈[0,1]^2,faceIndex 0..5 提供滚动相位差异
vec3 char_face_color(vec2 fuv, float faceIndex) {
    float cols = text_columns;
    float rows = text_rows;
    float col_base = fuv.x * cols;
    float row_idx = floor(fuv.y * rows);
    float scroll_offset = TIME * scroll_speed * (0.5 + hash(vec2(faceIndex, row_idx)) * 1.0);
    float col_idx = floor(col_base + scroll_offset);
    vec2 cell_uv = fract(vec2(col_base + scroll_offset, fuv.y * rows));

    float row_active = step(1.0 - rain_density, hash(vec2(row_idx, faceIndex * 7.0)));
    float cell_hash = hash(vec2(col_idx + faceIndex * 13.0, row_idx));
    float bit_val = step(0.5, cell_hash);
    // 时间片离散:同一 slice 内掷骰结果不变 → 状态至少保持一个时间片,肉眼可辨
    // 全格每片独立抽签(无固定掩码):任何格都可能闪,闪烁位置随时间游走
    // 翻转概率 = 1 - 0.88 = 12%/片;0.88 调大=更稀疏,调小=更密
    float slice = floor(TIME * 6.0 * flicker_rate);
    float flicker = step(0.88, hash(vec2(row_idx + col_idx * 17.3,
                                         slice * 0.713 + faceIndex * 3.7)));
    bit_val = mix(bit_val, 1.0 - bit_val, flicker * row_active);

    float col_progress = fract(col_base + scroll_offset);
    float head = 1.0 - smoothstep(0.0, 0.2, col_progress);
    float tail = 1.0 - smoothstep(0.5, 1.0, col_progress);
    float char_brightness = mix(0.25, 1.0, head) * mix(0.4, 1.0, tail);

    float glyph = mix(draw_zero(cell_uv), draw_one(cell_uv), bit_val);
    glyph *= row_active * char_brightness;

    vec3 col = face_color.rgb;
    vec3 char_color = mix(text_color.rgb, head_color.rgb, head);
    return mix(col, char_color, glyph);
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

    vec3 hit_position = ro + rd * t_near;
    float epsilon = 0.001;
    float faceIndex = 0.0;
    vec3 normal;
    vec2 fuv;

    // 面判定边界按各轴半尺寸;fuv 把自由轴坐标映射到 [-half,+half] -> [0,1],
    // 因此字符晶格随面比例拉伸(列/行数不变,格子变长方)
    if (hit_position.x > box_half.x - epsilon) {        // +X
        normal = vec3(1.0, 0.0, 0.0);
        fuv = vec2(-hit_position.z / (2.0 * box_half.z),
                   hit_position.y / (2.0 * box_half.y)) + 0.5;
        faceIndex = 2.0;
    } else if (hit_position.x < -box_half.x + epsilon) { // -X
        normal = vec3(-1.0, 0.0, 0.0);
        fuv = vec2(hit_position.z / (2.0 * box_half.z),
                   hit_position.y / (2.0 * box_half.y)) + 0.5;
        faceIndex = 3.0;
    } else if (hit_position.y > box_half.y - epsilon) {  // +Y 顶
        normal = vec3(0.0, 1.0, 0.0);
        fuv = vec2(hit_position.x / (2.0 * box_half.x),
                   -hit_position.z / (2.0 * box_half.z)) + 0.5;
        faceIndex = 4.0;
    } else if (hit_position.y < -box_half.y + epsilon) { // -Y 底
        normal = vec3(0.0, -1.0, 0.0);
        fuv = vec2(hit_position.x / (2.0 * box_half.x),
                   hit_position.z / (2.0 * box_half.z)) + 0.5;
        faceIndex = 5.0;
    } else if (hit_position.z > box_half.z - epsilon) {  // +Z 前
        normal = vec3(0.0, 0.0, 1.0);
        fuv = vec2(hit_position.x / (2.0 * box_half.x),
                   hit_position.y / (2.0 * box_half.y)) + 0.5;
        faceIndex = 0.0;
    } else {                                      // -Z 后
        normal = vec3(0.0, 0.0, -1.0);
        fuv = vec2(-hit_position.x / (2.0 * box_half.x),
                   hit_position.y / (2.0 * box_half.y)) + 0.5;
        faceIndex = 1.0;
    }

    vec3 face_rgb = char_face_color(fuv, faceIndex);

    // 假 Lambert 光照(面内容亮度随朝向变化,保留数据流明暗层次)
    vec3 world_normal = rotation * normal;
    float ndotl = max(dot(world_normal, normalize(light_dir)), 0.0);
    float light = ambient + ndotl * shading_strength;
    face_rgb *= light;

    // 可选描边(默认关,与旧字符面观感一致)
    if (draw_outline) {
        vec2 outline_uv = uv * (1.0 + outline_width * 4.0);
        vec3 outline_ray = normalize(inverse_rotation * vec3(outline_uv, -perspective));
        float t_dummy;
        if (!intersect_box(ro, outline_ray, -box_half, box_half, t_dummy)) {
            face_rgb = mix(face_rgb, outline_color.rgb, outline_color.a);
        }
    }

    COLOR = vec4(face_rgb, face_alpha);
}
