shader_type canvas_item;

/// 半透明绿色蒸汽特效（治疗）：粒子贴图（烟）经噪音扰动翻滚 + 噪音二次调制边缘，
/// 呈现不规则蒸汽团；颜色为可配置的绿色半透明。

uniform sampler2D noise_tex : repeat_enable;          // 噪音图（蒸汽扰动/翻滚）
uniform vec4 vapor_color : source_color = vec4(0.3, 1.0, 0.45, 1.0);  // 蒸汽颜色（绿色）
uniform float distortion_strength : hint_range(0.0, 0.2) = 0.06;      // UV 扭曲强度（翻滚幅度）
uniform float distortion_speed : hint_range(0.1, 5.0) = 1.2;          // 扭曲速度
uniform float distortion_scale : hint_range(0.5, 8.0) = 2.0;          // 噪音尺度（大 = 更多细节）
uniform float alpha_mult : hint_range(0.0, 1.0) = 0.55;               // 整体半透明度

void fragment() {
	// 噪音扰动 UV——蒸汽翻滚
	vec2 n = texture(noise_tex, UV * distortion_scale + TIME * distortion_speed).rg;
	vec2 uv = UV + (n - 0.5) * distortion_strength;

	// 烟贴图形状（alpha 作为蒸汽基础形状）
	float shape = texture(TEXTURE, uv).a;

	// 噪音二次调制边缘——蒸汽边缘不规则、内部浓密
	float n2 = texture(noise_tex, uv * 3.0 + TIME * 0.4).r;
	float alpha = shape * mix(0.25, 1.0, n2) * alpha_mult;

	COLOR = vec4(vapor_color.rgb, alpha);
}
