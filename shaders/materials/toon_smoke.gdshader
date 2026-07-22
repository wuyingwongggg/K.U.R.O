shader_type canvas_item;
render_mode blend_mix, unshaded;

// ── 卡通烟雾风格化 ──────────────────────────────
uniform float noise_scale : hint_range(0.5, 10.0, 0.1) = 3.0;
uniform float noise_speed : hint_range(0.01, 2.0, 0.01) = 0.3;
uniform float distortion : hint_range(0.0, 0.15, 0.005) = 0.06;
uniform int alpha_steps : hint_range(2, 8, 1) = 4;
uniform float alpha_power : hint_range(0.5, 3.0, 0.1) = 1.5;
uniform vec4 tint : source_color = vec4(0.9, 0.88, 0.85, 1.0);
uniform float fresnel_strength : hint_range(0.0, 0.5, 0.01) = 0.12;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
        mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x),
        f.y
    );
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    float s = 2.0;
    for (int i = 0; i < 3; i++) {
        v += a * noise(p * s);
        s *= 2.0;
        a *= 0.5;
    }
    return v;
}

void fragment() {
    vec4 tex = texture(TEXTURE, UV);

    // 噪声扰动 UV
    float t = TIME * noise_speed;
    float n = fbm(UV * noise_scale + vec2(t, t * 0.7));
    vec2 distorted_uv = UV + (n - 0.5) * distortion;

    // 采样扰动后的纹理
    vec4 sample = texture(TEXTURE, distorted_uv);

    // 量化 Alpha（卡通色阶）
    float a = sample.a;
    a = pow(a, alpha_power);  // 先加强对比
    a = floor(a * float(alpha_steps) + 0.5) / float(alpha_steps);  // 量化成 N 级

    // 边缘菲涅尔
    float edge = fbm(UV * 2.0 + t * 0.5);
    a *= 1.0 - edge * fresnel_strength;

    // 染色
    COLOR.rgb = tint.rgb * tex.rgb;
    COLOR.a = a * tint.a;
}
