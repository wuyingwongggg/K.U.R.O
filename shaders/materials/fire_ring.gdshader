shader_type canvas_item;

uniform sampler2D noise1:repeat_enable;
uniform sampler2D noise2:repeat_enable;
uniform vec3 tint:source_color;
uniform float influence:hint_range(0.0,30.0, 0.1);
uniform float amount:hint_range(0.0,1.0, 0.01);

void fragment() {
    float noise1_value = texture(noise1, UV + TIME * 0.15).r - 0.5;
    float noise2_value = texture(noise2, UV - TIME * 0.25).r - 0.5;
    float mixed_noise = noise1_value * noise2_value * influence;

    vec2 offset = vec2(0.02, 0.15) * mixed_noise;
    COLOR = texture(TEXTURE, UV + offset);
    COLOR.rgb = tint;

    noise1_value = texture(noise1, UV + TIME * 0.15).r;
    noise2_value = texture(noise2, UV - TIME * 0.25).r;
    mixed_noise = noise1_value * noise2_value;

    COLOR.a *= 1.0 - mixed_noise * 6.0 * amount;
    COLOR.a *= 1.0 - amount;
}