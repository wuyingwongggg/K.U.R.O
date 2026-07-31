shader_type canvas_item;
render_mode blend_add, unshaded;

uniform vec4 particle_color : source_color = vec4(0.2, 0.4, 1.0, 1.0);
uniform float glow_scale : hint_range(0.8, 2.0) = 1.1;

// ── 浮尘粒子 ──
uniform float density : hint_range(0.1, 2.0) = 1.0;
uniform float brightness : hint_range(0.0, 2.0) = 0.6;
uniform float particle_size : hint_range(0.005, 0.08) = 0.025;
uniform float anim_speed : hint_range(0.1, 3.0) = 1.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
    vec2 uv = UV;
    float tex_alpha = texture(TEXTURE, uv).a;

    // Glow zone outside the card edge
    vec2 center_uv = (uv - 0.5) / glow_scale + 0.5;
    float inner_alpha = texture(TEXTURE, center_uv).a;
    float glow_band = inner_alpha * (1.0 - tex_alpha);

    // ── Floating dust particles ──
    float cell_count = 8.0 + density * 14.0;
    vec2 cell_id = floor(uv * cell_count);
    vec2 local_uv = fract(uv * cell_count);

    float h1 = hash(cell_id);
    float h2 = hash(cell_id + 17.0);
    float h3 = hash(cell_id + 31.0);
    float h4 = hash(cell_id + 53.0);

    vec2 particle_center = vec2(h1, h2) * 0.6 + 0.2;

    float lifetime = 1.0 + h3 * 4.0;
    float cooldown = h4;
    float total_cycle = lifetime + cooldown;
    float phase = hash(cell_id + 7.0);
    float cycle_time = mod(TIME * anim_speed + phase * total_cycle, total_cycle);

    float life = 0.0;
    if (cycle_time < lifetime) {
        float progress = cycle_time / lifetime;
        life = sin(progress * 3.14159265);
    }

    float angle = 0.174533;
    vec2 rel = local_uv - particle_center;
    float ca = cos(angle), sa = sin(angle);
    rel = vec2(rel.x * ca - rel.y * sa, rel.x * sa + rel.y * ca);
    rel.y /= 0.8 + h2 * 0.5;

    float particle = 1.0 - smoothstep(0.0, particle_size, length(rel));
    float particle_alpha = particle * life * brightness;

    // Only visible in glow band, fade outward
    float mask = smoothstep(0.0, 0.05, glow_band);

    COLOR = vec4(particle_color.rgb * particle_alpha, particle_alpha * mask);
}
