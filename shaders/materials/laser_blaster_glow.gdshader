shader_type canvas_item;

uniform float glow_intensity : hint_range(0.0, 50.0) = 6.0;
uniform float core_width : hint_range(0.0, 1.0) = 0.5;
uniform float core_length : hint_range(0.0, 1.0) = 0.5;
uniform float fade : hint_range(0.0, 1.0) = 1.0;

void fragment() {
    float y_dist = abs(UV.y - 0.5);

    float core_y_mask = 1.0 - smoothstep(0.0, core_width, y_dist);
    float core_x_mask = 1.0 - smoothstep(core_length, 1.0, UV.x);
    float core_mask = core_y_mask * core_x_mask;
    vec4 core_color = COLOR * glow_intensity;
    core_color.a = core_mask * fade;

    COLOR = core_color;
}
