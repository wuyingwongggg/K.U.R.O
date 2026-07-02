shader_type canvas_item;
render_mode blend_add, unshaded;

uniform float edge_width : hint_range(0.0, 0.3) = 0.3;
uniform float edge_power : hint_range(0.5, 4.0) = 2.0;
uniform float alpha : hint_range(0.0, 1.0) = 1.0;
uniform float glow_intensity : hint_range(0.0, 3.0) = 1.0;
uniform float breath_speed : hint_range(0.0, 5.0) = 3.0;
uniform float breath_amplitude : hint_range(0.0, 0.5) = 0.2;
uniform vec4 glow_color : source_color = vec4(0.4, 0.9, 1, 1);

void fragment() {
	vec4 tex = texture(TEXTURE, UV);

	// Fresnel edge
	float edge_x = min(UV.x, 1.0 - UV.x);
	float edge_y = min(UV.y, 1.0 - UV.y);
	float edge = min(edge_x, edge_y);
	edge = 1.0 - smoothstep(0.0, edge_width, edge);
	edge = pow(edge, edge_power);

	// Breathing
	float breath = 0.8 + breath_amplitude * sin(TIME * breath_speed);

	vec4 color = edge * glow_color * glow_intensity * breath * tex.a;
	color.rgb += tex.rgb * glow_color.rgb * edge * 0.15 * tex.a;

	color.a *= alpha;

		COLOR = color;
}
