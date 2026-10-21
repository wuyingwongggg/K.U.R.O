shader_type canvas_item;
render_mode blend_mix, unshaded;

// 小型干扰区（世界空间）：伪 3D 平面上的**矩形局部屏幕扭曲**。
// 矩形范围内随机激活的小方块采样屏幕纹理并做独立偏移 → 方块内的画面被扭曲；
// 矩形外与未激活方块全透明。没有圆形/多边形边界、脉冲环、扫描线等其它效果。
// 多层视觉由 InterferenceZone.tscn 的 Visuals/LayerA~C 叠加（同心同倾角、不同 seed/方块参数）。
// 尺寸由 InterferenceZone 通过节点 Scale 控制（QuadMesh 2×2 → 半宽=Scale）。

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;

// ── 伪 3D 投影（ring_shockwave / pseudo_3d_card 同款）────────────
uniform bool cull_backface = true;
uniform float fov : hint_range(1.0, 179.0) = 60.0;
uniform float rot_y_deg : hint_range(-360.0, 360.0) = 0.0;
uniform float rot_x_deg : hint_range(-360.0, 360.0) = 25.0;
uniform float inset : hint_range(0.0, 1.0) = 0.0;
uniform float quad_size = 2.0;

// ── 干扰方块 ────────────────────────────────────────────────
uniform float strength : hint_range(0.0, 1.0) = 1.0;
uniform float seed = 1.0;
uniform float glitch_frequency : hint_range(1.0, 20.0) = 8.0;
uniform float block_density : hint_range(0.0, 1.0) = 0.4;
uniform float block_size : hint_range(0.03, 0.4) = 0.16;
// 矩形范围在平面 UV 内的内缩（避免压到 quad 边缘）
uniform float rect_margin : hint_range(0.0, 0.3) = 0.04;
// 方块内屏幕采样偏移（屏幕 UV 比例）
uniform float distort_distance : hint_range(0.0, 0.2) = 0.04;
uniform float rgb_shift : hint_range(0.0, 0.02) = 0.002;
uniform float green_mix : hint_range(0.0, 1.0) = 0.45;
uniform vec4 green_tint : source_color = vec4(0.05, 1.0, 0.3, 1.0);

varying vec2 offset;
varying vec3 world_pos_3d;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

void vertex() {
	float rot_y_rad = radians(rot_y_deg);
	float rot_x_rad = radians(rot_x_deg);

	float sin_y = sin(rot_y_rad), cos_y = cos(rot_y_rad);
	float sin_x = sin(rot_x_rad), cos_x = cos(rot_x_rad);

	mat3 rotation_matrix;
	rotation_matrix[0] = vec3(cos_y, 0.0, -sin_y);
	rotation_matrix[1] = vec3(sin_y * sin_x, cos_x, cos_y * sin_x);
	rotation_matrix[2] = vec3(sin_y * cos_x, -sin_x, cos_y * cos_x);

	float perspective_scale = tan(radians(fov) * 0.5);
	world_pos_3d = rotation_matrix * vec3(UV - 0.5, 0.5 / perspective_scale);

	float depth_scale = (0.5 / perspective_scale) + 0.5;
	world_pos_3d.xy *= depth_scale * rotation_matrix[2].z;
	offset = depth_scale * rotation_matrix[2].xy;

	// MeshInstance2D 的 QuadMesh 本地尺寸（当前 2×2）
	VERTEX += (UV - 0.5) * quad_size * perspective_scale * (1.0 - inset);
}

void fragment() {
	if (cull_backface && world_pos_3d.z <= 0.0) discard;

	// 屏幕像素 → 平面 UV（逆投影）
	vec2 projected_uv = (world_pos_3d.xy / world_pos_3d.z) - offset + 0.5;
	if (projected_uv.x < 0.0 || projected_uv.x > 1.0
		|| projected_uv.y < 0.0 || projected_uv.y > 1.0)
		discard;

	// QuadMesh 的 UV V 轴与世界 Y 轴相反
	vec2 plane_uv = vec2(projected_uv.x, 1.0 - projected_uv.y);

	// 矩形范围：内缩 rect_margin，四条边都是直的
	if (plane_uv.x < rect_margin || plane_uv.x > 1.0 - rect_margin
		|| plane_uv.y < rect_margin || plane_uv.y > 1.0 - rect_margin)
		discard;

	float tcell = floor(TIME * glitch_frequency);

	// 小方块：按时间片随机激活（离散跳变，不逐帧闪烁）
	vec2 cell = floor(plane_uv / block_size);
	float active = step(1.0 - block_density * strength, hash12(cell + vec2(seed * 31.0, tcell)));
	if (active < 0.5) discard;

	// 每个方块独立扭曲：偏移采样屏幕 → 方块范围内的画面错位
	vec2 rnd = hash22(cell + vec2(seed * 13.0, tcell * 1.7));
	vec2 distort = (rnd - 0.5) * 2.0 * distort_distance * strength;
	vec2 sample_uv = SCREEN_UV + distort;

	vec3 col = texture(screen_texture, sample_uv).rgb;
	col.r = texture(screen_texture, sample_uv + vec2(rgb_shift * strength, 0.0)).r;
	col.b = texture(screen_texture, sample_uv - vec2(rgb_shift * strength, 0.0)).b;

	// 数字绿色化：按亮度归一化混合，不做 ×>1 的乘法——
	// 与 RingShockwave 的教训一致：HDR 2D / Glow 下超过 1 的颜色会实际比材质预览亮很多。
	float luma = dot(col, vec3(0.299, 0.587, 0.114));
	col = mix(col, green_tint.rgb * luma, green_mix * strength);

	// 方块硬边：同样保持 ≤1 的混合
	vec2 cell_local = fract(plane_uv / block_size);
	float border = min(min(cell_local.x, 1.0 - cell_local.x), min(cell_local.y, 1.0 - cell_local.y));
	float edge = 1.0 - smoothstep(0.0, 0.035, border);
	col = mix(col, green_tint.rgb, edge * 0.45 * strength);

	COLOR = vec4(clamp(col, 0.0, 1.0), strength);
}
