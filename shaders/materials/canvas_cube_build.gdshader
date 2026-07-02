shader_type canvas_item;
render_mode blend_mix, unshaded;

// 与 canvas_cube_glowing 相同的几何参数
uniform vec3[8] points;
uniform int[100] edges;
uniform int edges_size = 24;
uniform float sofness = 0.1;
uniform float time_scale = 500.0;
uniform float briness_scale = 1.6;

// 构建动画参数
uniform float build_progress : hint_range(0.0, 1.0) = 0.0;
uniform float scanline_width : hint_range(0.005, 0.08) = 0.025;
uniform vec4 scanline_color : source_color = vec4(0.4, 0.9, 1, 1);
uniform float edge_glow : hint_range(0.0, 0.15) = 0.06;

float line(vec2 pa, vec2 pb, vec2 uv) {
    vec2 direction = pb - pa;
    vec2 offset = uv - pa;
    float t = clamp(dot(offset, direction) / dot(direction, direction), 0.0, 1.0);
    vec2 projection = pa + direction * t;
    return length(uv - projection);
}

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

void fragment() {
    float time = time_scale * TIME + 100.;
    float liness = 0.0;

    // 计算每个顶点投影后的 Y 坐标范围，确定 build 阈值在投影空间中的位置
    // build_progress: 0=底部未构建, 1=完全构建
    // UV.y: 0=底部, 1=顶部 → 映射到投影空间: proj_y = UV.y * 2.0 - 1.0
    // build_progress: 0=不可见, 1=完全构建 → 反转后映射到投影空间
    float reveal_proj_y = (1.0 - build_progress) * 2.0 - 1.0;

    for (int i = 0; i < edges_size / 2; i++) {
        int index_a = edges[i * 2];
        int index_b = edges[i * 2 + 1];
        vec3 pa3 = points[index_a];
        vec3 pb3 = points[index_b];
        pa3 = rotated(pa3 - vec3(.5), vec3(time) * vec3(-.11, .17, .19));
        pb3 = rotated(pb3 - vec3(.5), vec3(time) * vec3(-.11, .17, .19));
        vec2 pa = unproject(pa3);
        vec2 pb = unproject(pb3);

        // 当前像素到线段的距离
        float dist = line(pa, pb, UV * 2. - vec2(1.));
        float edge_alpha = -log(briness_scale * dist) * sofness;

        // 仅在线段两端都低于 reveal 阈值时完全显示
        float edge_max_y = max(pa.y, pb.y);
        float edge_mask = smoothstep(reveal_proj_y + 0.05, reveal_proj_y, edge_max_y);

        liness += edge_alpha * edge_mask;
    }

    // 扫描线发光
    float proj_uv_y = UV.y;
    float scan_dist = abs(proj_uv_y - build_progress);
    float scanline = 1.0 - smoothstep(0.0, scanline_width, scan_dist);
    float glow = exp(-scan_dist / max(edge_glow, 0.001)) * 0.4;

    vec4 tex = texture(TEXTURE, UV);
    COLOR.a = liness;
    COLOR.rgb = tex.rgb + scanline_color.rgb * (scanline * scanline_color.a + glow);
}
