shader_type canvas_item;
render_mode blend_mix, unshaded;

// ── 几何参数（与 canvas_cube_glowing 一致）────────────────────
uniform vec3[8] points;
uniform int edges_size = 24;
uniform float time_scale = 500.0;

// ── 面定义：6面×4顶点 ──────────────────────────────────────
// Front(z=0):0,1,2,3  Back(z=1):5,4,7,6  Right(x=1):1,5,6,2
// Left(x=0):4,0,3,7   Top(y=1):3,2,6,7    Bottom(y=0):0,4,5,1
uniform int[24] faces;

// ── 面渲染参数 ─────────────────────────────────────────────
uniform float face_alpha : hint_range(0.0, 1.0) = 0.9;
uniform vec4 face_color : source_color = vec4(0.02, 0.06, 0.02, 1.0);
uniform vec4 text_color : source_color = vec4(0.0, 1.0, 0.16, 1.0);
uniform vec4 head_color : source_color = vec4(0.7, 1.0, 0.8, 1.0);

// ── 二进制雨参数 ───────────────────────────────────────────
uniform float text_columns = 10.0;
uniform float text_rows = 10.0;
uniform float scroll_speed = 0.6;
uniform float flicker_rate = 0.4;
uniform float rain_density : hint_range(0.0, 1.0) = 0.7;

// ── 辅助函数 ───────────────────────────────────────────────

vec2 unproject(vec3 v3) {
    return vec2(v3.x, v3.y);
}

vec3 rotated(vec3 v3, vec3 rotation) {
    float radX = radians(rotation.x);
    float radY = radians(rotation.y);
    float radZ = radians(rotation.z);
    mat4 rotX = mat4(
        vec4(1., 0., 0., 0.),
        vec4(0., cos(radX), -sin(radX), 0.),
        vec4(0., sin(radX), cos(radX), 0.),
        vec4(0., 0., 0., 1.)
    );
    mat4 rotY = mat4(
        vec4(cos(radY), 0., sin(radY), 0.),
        vec4(0., 1., 0., 0.),
        vec4(-sin(radY), 0., cos(radY), 0.),
        vec4(0., 0., 0., 1.)
    );
    mat4 rotZ = mat4(
        vec4(cos(radZ), -sin(radZ), 0., 0.),
        vec4(sin(radZ), cos(radZ), 0., 0.),
        vec4(0., 0., 1., 0.),
        vec4(0., 0., 0., 1.)
    );
    mat4 rotationMatrix = rotX * rotY * rotZ;
    vec4 rotatedVec4 = rotationMatrix * vec4(v3, 1.0);
    return vec3(rotatedVec4.rgb);
}

float edge_func(vec2 a, vec2 b, vec2 p) {
    return (p.x - a.x) * (b.y - a.y) - (p.y - a.y) * (b.x - a.x);
}

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// ── 0/1 字形绘制 ──────────────────────────────────────────

float draw_zero(vec2 uv) {
    float thick = 0.12;
    vec2 d = abs(uv - 0.5) * 2.0;
    float outer = step(max(d.x, d.y), 0.85);
    float inner = step(max(d.x, d.y), 0.85 - thick * 2.0);
    return outer - inner;
}

float draw_one(vec2 uv) {
    // 竖线主体，限制纵向范围避免顶天立地
    float v_range = step(0.08, uv.y) * step(uv.y, 0.92);
    float body = (1.0 - step(0.1, abs(uv.x - 0.5))) * v_range;
    float serif_top = 1.0 - step(0.35, abs(uv.x - 0.5));
    serif_top *= step(0.04, uv.y) * step(uv.y, 0.24);
    float serif_bot = 1.0 - step(0.25, abs(uv.x - 0.65));
    serif_bot *= step(0.8, uv.y);
    return max(max(body, serif_top), serif_bot);
}

// ── 面光栅化 ──────────────────────────────────────────────

bool rasterize_face(vec2 p, int face_idx, vec3 time_rot,
                    out vec2 face_uv, out float brightness) {
    int base = face_idx * 4;
    vec3 r0 = rotated(points[faces[base + 0]] - vec3(.5), time_rot);
    vec3 r1 = rotated(points[faces[base + 1]] - vec3(.5), time_rot);
    vec3 r2 = rotated(points[faces[base + 2]] - vec3(.5), time_rot);
    vec3 r3 = rotated(points[faces[base + 3]] - vec3(.5), time_rot);
    vec2 a = unproject(r0);
    vec2 b = unproject(r1);
    vec2 c = unproject(r2);
    vec2 d = unproject(r3);

    // 背面剔除
    vec3 normal = cross(r1 - r0, r2 - r0);
    if (normal.z <= 0.0) return false;

    // 三角形1: a(u0,v0), b(u1,v0), c(u1,v1)
    float area1 = edge_func(a, b, c);
    float w_a = edge_func(b, c, p) / area1;
    float w_b = edge_func(c, a, p) / area1;
    float w_c = edge_func(a, b, p) / area1;

    if (w_a >= 0.0 && w_b >= 0.0 && w_c >= 0.0) {
        face_uv = vec2(w_b + w_c, w_c);
        brightness = normal.z / length(normal);
        return true;
    }

    // 三角形2: a(u0,v0), c(u1,v1), d(u0,v1)
    float area2 = edge_func(a, c, d);
    float w_a2 = edge_func(c, d, p) / area2;
    float w_c2 = edge_func(d, a, p) / area2;
    float w_d = edge_func(a, c, p) / area2;

    if (w_a2 >= 0.0 && w_c2 >= 0.0 && w_d >= 0.0) {
        face_uv = vec2(w_c2, w_c2 + w_d);
        brightness = normal.z / length(normal);
        return true;
    }

    return false;
}

// ── 主渲染 ─────────────────────────────────────────────────

void fragment() {
    float time = time_scale * TIME + 100.;
    vec3 time_rot = vec3(time) * vec3(-.11, .17, .19);
    vec2 p = UV * 2. - vec2(1.0);

    vec4 color = vec4(face_color.rgb, 0.0);
    float accum_alpha = 0.0;

    for (int fi = 0; fi < 6; fi++) {
        vec2 face_uv;
        float brightness;
        if (!rasterize_face(p, fi, time_rot, face_uv, brightness))
            continue;

        face_uv = vec2(1.0 - face_uv.x, 1.0 - face_uv.y);

        // 字符网格（横向滚动）
        float cols = text_columns;
        float rows = text_rows;
        float col_base = face_uv.x * cols;
        float row_idx = floor(face_uv.y * rows);
        float fi_f = float(fi);
        float scroll_offset = TIME * scroll_speed * (0.5 + hash(vec2(float(fi), row_idx)) * 1.0);
        float col_idx = floor(col_base + scroll_offset);
        vec2 cell_uv = fract(vec2(col_base + scroll_offset, face_uv.y * rows));

        // 该行是否激活
        float row_active = step(1.0 - rain_density, hash(vec2(row_idx, fi_f * 7.0)));

        // 该格是否为0或1
        float cell_hash = hash(vec2(col_idx + fi_f * 13.0, row_idx));
        float bit_val = step(0.5, cell_hash);

        // 闪烁：随时间随机翻转
        float flicker = step(flicker_rate, hash(vec2(col_idx + row_idx, TIME * 3.0)));
        bit_val = mix(bit_val, 1.0 - bit_val, flicker * row_active);

        // 头部渐变：每行左侧字符更亮
        float col_progress = fract(col_base + scroll_offset);
        float head = 1.0 - smoothstep(0.0, 0.2, col_progress);
        float tail = 1.0 - smoothstep(0.5, 1.0, col_progress);
        float char_brightness = mix(0.25, 1.0, head) * mix(0.4, 1.0, tail);

        // 绘制字形
        float glyph = mix(draw_zero(cell_uv), draw_one(cell_uv), bit_val);
        glyph *= row_active * char_brightness;

        // 面底色
        float face_base = 0.08;
        accum_alpha = max(accum_alpha, 1.0);
        color.rgb = mix(color.rgb, face_color.rgb, face_base);

        // 二进制字符颜色
        vec3 char_color = mix(text_color.rgb, head_color.rgb, head);
        color.rgb = mix(color.rgb, char_color, glyph * brightness);
    }

    color.a = accum_alpha * face_alpha;
    COLOR = color;
}
