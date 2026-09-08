shader_type canvas_item;

// 冷却扇形遮罩：从 12 点钟方向顺时针覆盖 u_progress 比例（1 = 全遮）。
// 素材经 u_mask 提供（方形图即可，如暗色圆/斜纹），换图即换表现；
// 无素材时 u_mask 默认白 → 纯 u_tint 颜色遮罩。
uniform sampler2D u_mask;
uniform float u_progress = 0.0;
uniform vec4 u_tint : source_color = vec4(0.0, 0.0, 0.0, 0.5);

void fragment() {
	vec2 centered = UV - vec2(0.5);
	if (length(centered) < 0.0001) {
		COLOR = vec4(0.0, 0.0, 0.0, 0.0);
		return;
	}

	// 12 点方向为 0°，顺时针递增
	float angle = mod(atan(centered.x, -centered.y), TAU);
	if (angle > u_progress * TAU) {
		COLOR = vec4(0.0, 0.0, 0.0, 0.0);
		return;
	}

	vec4 mask = texture(u_mask, UV);
	COLOR = vec4(mask.rgb, 1.0) * u_tint;
}
