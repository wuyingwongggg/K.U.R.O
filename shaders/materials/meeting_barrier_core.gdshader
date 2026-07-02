shader_type canvas_item;
render_mode blend_mix, unshaded;

uniform float alpha : hint_range(0.0, 1.0) = 0.8;
uniform float damage_level : hint_range(0.0, 1.0) = 0.0;
uniform vec4 core_color : source_color = vec4(0.3, 0.8, 1.0, 1.0);
uniform vec4 damage_color : source_color = vec4(0.9, 0.15, 0.6, 1.0);

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	vec4 tint = mix(core_color, damage_color, damage_level);
	vec4 color = tex * tint;
	color.a = alpha * tex.a;
	COLOR = color;
}
