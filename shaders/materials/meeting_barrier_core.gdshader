shader_type canvas_item;
render_mode blend_mix, unshaded;

uniform float alpha : hint_range(0.0, 1.0) = 0.35;
uniform float grid_x_count : hint_range(1, 80) = 20;
uniform float grid_y_count : hint_range(1, 80) = 40;
uniform float grid_thickness : hint_range(0.0, 0.05) = 0.05;
uniform float edge_width : hint_range(0.0, 0.3) = 0.08;
uniform float edge_power : hint_range(0.0, 3.0) = 1.5;
uniform float noise_amount : hint_range(0.0, 0.02) = 0.003;
uniform float noise_speed : hint_range(0.0, 5.0) = 2.0;
uniform vec4 core_color : source_color = vec4(0.2, 0.6, 1.0, 1.0);

float random(vec2 uv) {
	return fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453123);
}

void fragment() {
	vec4 tex = texture(TEXTURE, UV);

	// Grid
	float gx = 1.0 - smoothstep(0.0, grid_thickness, abs(fract(UV.x * float(grid_x_count)) - 0.5));
	float gy = 1.0 - smoothstep(0.0, grid_thickness, abs(fract(UV.y * float(grid_y_count)) - 0.5));
	float grid = max(gx, gy) * 0.4;

	// Fresnel edge
	float edge_x = min(UV.x, 1.0 - UV.x);
	float edge_y = min(UV.y, 1.0 - UV.y);
	float edge = min(edge_x, edge_y);
	edge = 1.0 - smoothstep(0.0, edge_width, edge);
	edge = pow(edge, edge_power);

	// Subtle noise warp
	float noise = random(floor(UV * 200.0 + TIME * noise_speed));
	vec2 uv_warp = UV;
	uv_warp.x += noise * noise_amount;

	vec4 warped_tex = texture(TEXTURE, uv_warp);

	// Composite
	vec4 color = warped_tex * core_color;
	color.rgb += grid * core_color.rgb * 0.6;
	color.rgb += edge * core_color.rgb * 1.2;
	color.a = alpha * (0.2 + 0.8 * max(max(grid, edge), 0.1));

	COLOR = color;
}
