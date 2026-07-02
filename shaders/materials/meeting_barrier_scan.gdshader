shader_type canvas_item;
render_mode blend_add, unshaded;

uniform float scan_speed : hint_range(0.0, 3.0) = 0.5;
uniform float scan_line_width : hint_range(0.002, 0.05) = 0.01;
uniform float digital_noise_amount : hint_range(0.0, 1.0) = 0.15;
uniform float glitch_amount : hint_range(0.0, 0.05) = 0.008;
uniform float glitch_frequency : hint_range(0.0, 10.0) = 3.0;
uniform float sparkle_density : hint_range(0.0, 1.0) = 0.08;
uniform float alpha : hint_range(0.0, 1.0) = 1.0;
uniform vec4 scan_color : source_color = vec4(0.4, 0.9, 1.0, 1.0);

// Damage corruption
uniform float damage_level : hint_range(0.0, 1.0) = 0.0;
uniform float damage_glitch_intensity : hint_range(0.0, 0.08) = 0.04;
uniform float damage_block_density : hint_range(0.0, 0.5) = 0.2;
uniform vec4 damage_color : source_color = vec4(0.9, 0.15, 0.6, 1.0);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float hash12(vec2 p) {
	float h = dot(p, vec2(127.1, 311.7));
	return fract(sin(h) * 43758.5453123);
}

void fragment() {
	vec4 tex = texture(TEXTURE, UV);

	// Moving scan line
	float scan = fract(UV.y - TIME * scan_speed);
	float scan_line = 1.0 - smoothstep(0.0, scan_line_width, abs(scan - 0.5));
	scan_line *= 0.6;

	// Digital noise blocks
	float block_size = 40.0;
	vec2 block_uv = floor(UV * block_size);
	float digital = hash(block_uv + floor(TIME * 2.0));
	digital = step(1.0 - digital_noise_amount, digital);
	digital *= hash12(block_uv + vec2(0.0, floor(TIME * 0.3)));

	// Glitch shift (base + damage-driven)
	float glitch_row = floor(UV.y * 50.0);
	float base_glitch_trigger = step(1.0 - 0.02, hash(vec2(glitch_row, floor(TIME * glitch_frequency))));
	float damage_glitch_trigger = step(1.0 - damage_level * 0.4, hash(vec2(glitch_row, floor(TIME * 5.0 + 10.0))));
	float glitch_trigger = max(base_glitch_trigger, damage_glitch_trigger);

	float base_offset = hash(vec2(glitch_row, 1.0)) * glitch_amount * glitch_trigger;
	float damage_offset = (hash(vec2(glitch_row, 7.0)) - 0.5) * damage_glitch_intensity * damage_level * damage_glitch_trigger;
	float glitch_offset = base_offset + damage_offset;
	vec2 uv_glitch = UV + vec2(glitch_offset, 0.0);
	float glitched_tex = texture(TEXTURE, uv_glitch).r;

	// RGB split on damage glitch rows
	float r_shift = (hash(vec2(glitch_row, 13.0)) - 0.5) * damage_glitch_intensity * 1.5 * damage_level * damage_glitch_trigger;
	float b_shift = (hash(vec2(glitch_row, 17.0)) - 0.5) * damage_glitch_intensity * 1.5 * damage_level * damage_glitch_trigger;
	float r_glitch = texture(TEXTURE, UV + vec2(r_shift, 0.0)).r;
	float b_glitch = texture(TEXTURE, UV + vec2(b_shift, 0.0)).b;
	vec3 rgb_split = vec3(r_glitch * 0.8, 0.0, b_glitch * 0.8) * damage_glitch_trigger * damage_level;

	// Block corruption
	float corrupt_block = hash(floor(UV * 25.0) + floor(TIME * 2.0));
	float corrupt_mask = step(1.0 - damage_level * damage_block_density, corrupt_block);
	vec3 corrupt_color = damage_color.rgb * corrupt_mask * damage_level;

	// Sparkle pixels (intensified by damage)
	float sparkle = step(1.0 - sparkle_density, hash(floor(UV * 120.0) + TIME));
	sparkle *= hash(floor(UV * 120.0) + TIME * 1.7) * 0.8;

	// Damage sparkle: red/purple sparks
	float damage_sparkle = step(1.0 - damage_level * 0.15, hash(floor(UV * 80.0) + TIME * 3.0));
	damage_sparkle *= hash(floor(UV * 80.0) + TIME * 3.3);

	// Composite
	vec4 color = vec4(0.0);
	// Base scan effects
	color.rgb += scan_line * scan_color.rgb;
	color.rgb += digital * scan_color.rgb * 0.4;
	color.rgb += glitched_tex * scan_color.rgb * glitch_trigger * 0.6;
	color.rgb += sparkle * scan_color.rgb;
	// Damage effects
	color.rgb += rgb_split * damage_level;
	color.rgb += corrupt_color;
	float effect_mask = max(max(scan_line, digital * 0.4), max(glitched_tex * glitch_trigger * 0.6, sparkle));
		effect_mask = max(effect_mask, max(corrupt_mask * damage_level * 0.5, damage_sparkle * damage_level));
		color.rgb *= tex.a;
		color.a = alpha * effect_mask * tex.a;

		COLOR = color;
}
