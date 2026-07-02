shader_type canvas_item;
render_mode blend_mix, unshaded;

uniform float build_progress : hint_range(0.0, 1.0) = 0.0;
uniform float scanline_width : hint_range(0.005, 0.08) = 0.025;
uniform vec4 scanline_color : source_color = vec4(0.4, 0.9, 1.0, 1.0);
uniform float edge_glow : hint_range(0.0, 0.15) = 0.06;

void fragment() {
	vec4 tex = texture(TEXTURE, UV);

	// Build mask: smoothstep from 0 to build_progress
	float mask = smoothstep(build_progress - 0.04, build_progress, UV.y);

	// Scanline at build edge
	float dist = abs(UV.y - build_progress);
	float line = 1.0 - smoothstep(0.0, scanline_width, dist);

	// Edge glow around the scanline
	float glow = exp(-dist / edge_glow) * 0.5;

	vec4 color = tex;
	color.a *= mask;
	color.rgb += scanline_color.rgb * line * scanline_color.a;
	color.rgb += scanline_color.rgb * glow * 0.3;

	COLOR = color;
}
