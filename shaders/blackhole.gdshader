shader_type canvas_item;

uniform vec2 center = vec2(0.5, 0.5); // 黑洞中心（UV坐标）
uniform float strength = 0.3;         // 吸引强度
uniform float radius = 0.4;           // 影响范围
uniform float swirl = 3.0;            // 旋转强度
uniform float time_scale = 1.0;

void fragment() {
    vec2 uv = UV;
    vec2 dir = uv - center;
    float dist = length(dir);

    // 归一化方向
    vec2 norm_dir = normalize(dir);

    // 吸引（向中心拉）
    float pull = smoothstep(radius, 0.0, dist);
    uv -= norm_dir * pull * strength;

    // 旋转（黑洞扭曲）
    float angle = swirl * pull + TIME * time_scale;
    float s = sin(angle);
    float c = cos(angle);

    mat2 rot = mat2(vec2(c, -s), vec2(s, c));
    uv = center + rot * (uv - center);

    // 采样原图
    vec4 color = texture(TEXTURE, uv);

    // 中心变黑（事件视界）
    float black = smoothstep(0.1, 0.0, dist);
    color.rgb *= black;

    COLOR = color;
}
