shader_type canvas_item;

// PI 为 Godot 4 内建常量,勿重复定义(GD3 移植时遗留)

uniform float removed_segments:hint_range(0., 1.) = 1.;
uniform vec4 ring_color : source_color = vec4(1);
uniform vec4 bg_color : source_color = vec4(0,0,0,1);
uniform vec4 bg_line_color : source_color = vec4(0.5,0.5,0.5,1);
uniform vec4 empty_cell_color : source_color = vec4(0.25,0.25,0.25,1);
// true = 自动抗锯齿(按 fwidth 屏幕导数);false = 显式软边(edge_softness 固定宽度,不依赖 fwidth,兼容渲染器稳定)
uniform bool antialias = false;
uniform float pixelFactor : hint_range(0,1) = .25;
// 显式软边宽度(UV 单位;0.02 ≈ 屏上 1~2px 过渡,值越小越锐)
uniform float edge_softness : hint_range(0.0, 0.08) = 0.02;
uniform float radius :hint_range(0, 1) = 0.7;
uniform float line_width :hint_range(0, 1) = 0.21;
uniform float bg_width :hint_range(0, 1) = 0.28;
// 内圈半径(外径固定,抬内径即收窄环带;<0 = 旧对称带宽行为 = radius - *_width)
uniform float inner_radius :hint_range(-1, 1) = -1.;
uniform float bg_inner_radius :hint_range(-1, 1) = -1.;
// 底环外径:默认 radius + bg_width(比进度环外扩,外侧露一圈描边);<0 = 回退旧行为
uniform float bg_outer_radius :hint_range(-1, 1) = -1.;
uniform float start_angle:hint_range(0., 360.) = 0.;
uniform float segment_spacing:hint_range(0., 1.) = 0.05;
uniform float segment_angle:hint_range(0., 1.) = 0.;
uniform int segment_count = 3;

mat2 tex_rotate(float _angle){
	return mat2(vec2(cos(_angle), -sin(_angle)), vec2(sin(_angle), cos(_angle)));
}

void fragment(){
	vec2 uv;
	if(pixelFactor != 0.){
		vec2 pixelNumber = vec2(textureSize(TEXTURE, 0)) * pixelFactor;
		vec2 pixelated_UV = round(UV * pixelNumber) / pixelNumber;
		uv = (pixelated_UV * 2.0 - 1.0) * tex_rotate((-radians(start_angle)));
	}else{
		uv = (UV * 2.0 - 1.0) * tex_rotate((-radians(start_angle)));
	}
	
	float angle = atan(uv.x, uv.y) + PI;
	float segment_size = (2. * PI) / max(float(segment_count), 1.);
	// 缺口 = 到最近等分线的角距离;线数 = segment_count(1 段 = 1 条,
	// 旧 abs(sin) 式周期 π,1 段时 2π 区间内会多出 π 处第二条线)
	float seg_phase = mod(angle + PI * 2. * segment_angle, segment_size);
	float segment = min(seg_phase, segment_size - seg_phase) * length(uv) - segment_spacing;
	angle -= removed_segments * 2. * PI;

	// 径向:进度环外径固定 radius、内径可独立;底环内外径均可独立
	// (未配置时回退旧对称带宽:inner = radius - width,outer = radius + width)
	float rr = length(uv);
	float ring_inner = inner_radius >= 0. ? inner_radius : radius - line_width;
	float bg_inner = bg_inner_radius >= 0. ? bg_inner_radius : radius - bg_width;
	float bg_outer = bg_outer_radius >= 0. ? bg_outer_radius : radius + bg_width;
	// 区间外部的距离(负=在带内)
	float ring = max(rr - radius, ring_inner - rr);
	float background = max(rr - bg_outer, bg_inner - rr);
	float circle = ring; // 兼容下方 fwidth 抗锯齿基准
	
	if(antialias){
		ring /= fwidth(circle);
		angle /= fwidth(angle);
		segment /= fwidth(segment);
		background /= fwidth(background);
	}else{
		// 显式软边:把距离场按固定宽度归一(替代旧 ceil 硬阈值,消除锯齿)
		ring /= edge_softness;
		angle /= edge_softness;
		segment /= edge_softness;
		background /= edge_softness;
	}
	ring = 1. - ring;
	segment = 1. - segment;
	background = 1. - background;
	
	ring = clamp(ring,0,1);
	angle = clamp(angle,0,1);
	segment = clamp(segment,0,1);
	background = clamp(background,0,1);
	float base_ring = ring;
	
	ring -= angle;
	if(segment_count >= 2) ring -= segment;
	ring = clamp(ring,0,1);
	
	if(ring > 0.){
		COLOR = ring_color;
		COLOR.a = ring;
	}else{
		if(segment >= 1. && base_ring >= 1.){
			COLOR = bg_line_color;
		}else if(base_ring >= 1.){
			COLOR = empty_cell_color;
		}else{
			COLOR = bg_color;
		}
		COLOR.a = background;
	}
}