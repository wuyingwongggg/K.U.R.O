shader_type canvas_item;

uniform float angle : hint_range(0.0, 6.283184);
uniform float spread_angle : hint_range(0.1, 3.141592);
uniform float progress : hint_range(0.0, 1.0);
uniform float border_energy_offset : hint_range(0.0, 1.0);

// ── 呼吸效果：无脚本，TIME 正弦驱动 border_energy_offset 上下波动 ──
uniform bool breath_active = false;                             // 开启护盾呼吸
uniform float breath_speed : hint_range(0.1, 5.0, 0.1) = 1.0;   // 呼吸频率（Hz）
uniform float breath_strength : hint_range(0.0, 1.0, 0.05) = 0.5; // 波动幅度（0=不波动，1=全幅摆动）
uniform vec4 color : source_color;
uniform vec4 edges_color : source_color;
uniform float edges_valueable : hint_range(0.0, 1.0);
uniform float edges_fade : hint_range(0.0, 1.0);

void fragment(){
	vec2 center = vec2(0.5);
	vec2 center_uv = normalize(UV - center);
	float is = 1.0 - smoothstep(0.49*progress, 0.5*progress, distance(center, UV));
	
	vec2 frst_border = vec2(cos(angle+spread_angle/2.0), sin(angle+spread_angle/2.0));
	vec2 sec_boreder = vec2(cos(angle-spread_angle/2.0), sin(angle-spread_angle/2.0));
	
	vec2 radial_center = vec2(cos(angle), sin(angle));
	
	float posx = step(0.0, radial_center.x); // is radial_center.x positive
	float posy = step(0.0, radial_center.y); // is radial_center.y positive
	
	// using different formulas for different radial_center.x signs
	float is2 = step(min(frst_border.x, sec_boreder.x), center_uv.x)*posx; 
	float is3 = step(center_uv.x, max(frst_border.x, sec_boreder.x))*(1.0 - posx);
	
	// using different formulas for different radial_center.y signs
	float is4 = step(min(frst_border.y, sec_boreder.y), center_uv.y)*posy;
	float is5 = step(center_uv.y, max(frst_border.y, sec_boreder.y))*(1.0-posy);
	
	// how close to frontier is
	float border_factor = 1.0 - distance(2.0*(UV - center), center_uv);
	// 呼吸：以 border_energy_offset 为基准，正弦在 0~1 间波动（关闭时保持原值，不改变原有行为）
	float border_energy = border_energy_offset;
	if (breath_active)
	{
		float wave = 0.5 + 0.5 * sin(TIME * breath_speed * TAU);
		border_energy = mix(border_energy_offset, wave, breath_strength);
	}
	border_factor = pow(border_factor, border_energy*50.0); // speeding up center fading
	
	// how close to radial center is
	float radial_center_factor = 1.0 - distance(center_uv, radial_center)/distance(frst_border, radial_center);
	
	// how close ro radial edges is
	float edge_radial_factor = (1.0 - radial_center_factor) * edges_valueable;
	
	// applying edge fading
	float edge_fade_factor = mix(radial_center_factor, 1.0, 1.0-edges_fade);
	
	COLOR.a = min(1.0, is2+is3)*min(1.0, is4+is5)*is*border_factor*mix(color.a, edges_color.a, edge_radial_factor)*edge_fade_factor;
	COLOR.rgb = mix(color.rgb, edges_color.rgb, edge_radial_factor);
}