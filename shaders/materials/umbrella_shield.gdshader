shader_type canvas_item;
render_mode blend_mix, unshaded;

// 雨伞护盾四阶段 shader（参考 FireWallA 的 build/core/glow 分层效果合并为单 shader）：
// - 生成：build_progress 自下而上扫描显现 + 边缘扫描线
// - 常态：呼吸微光
// - 受到攻击：hit_flash 闪红白
// - 消失：alpha 整体淡出

uniform float build_progress : hint_range(0.0, 1.0) = 1.0;
uniform float scanline_width : hint_range(0.005, 0.08) = 0.03;
uniform vec4 scanline_color : source_color = vec4(0.5, 0.9, 1.0, 1.0);

uniform float hit_flash : hint_range(0.0, 1.0) = 0.0;
uniform vec4 hit_color : source_color = vec4(1.0, 0.35, 0.3, 1.0);

uniform float alpha : hint_range(0.0, 1.0) = 1.0;

uniform float breath_speed : hint_range(0.0, 5.0) = 2.5;
uniform float breath_amplitude : hint_range(0.0, 0.3) = 0.06;
uniform vec4 glow_color : source_color = vec4(0.5, 0.9, 1.0, 1.0);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

void fragment() {
	vec4 tex = texture(TEXTURE, UV);

	// 生成：自下而上显现（进度以下的区域可见），扫描线在显现边缘
	float mask = 1.0 - smoothstep(build_progress - 0.04, build_progress, UV.y);
	float dist = abs(UV.y - build_progress);
	float line = 1.0 - smoothstep(0.0, scanline_width, dist);

	// 常态：呼吸微光 + 轻微像素闪烁
	float breath = 0.85 + breath_amplitude * sin(TIME * breath_speed);
	float sparkle = step(1.0 - 0.03, hash(floor(UV * 120.0) + floor(TIME * 1.5))) * 0.25;

	// 受到攻击：闪白泛红
	vec3 tint = mix(vec3(1.0), hit_color.rgb, hit_flash);

	vec4 color = tex;
	color.rgb *= tint * breath;
	color.rgb += scanline_color.rgb * line * 0.9;
	color.rgb += glow_color.rgb * sparkle * tex.a;
	color.a = alpha * mask * tex.a;

	COLOR = color;
}
