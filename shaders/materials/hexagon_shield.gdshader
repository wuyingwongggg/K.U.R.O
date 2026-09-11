shader_type canvas_item;

// ══════════════════════════════════════════════════════════════════════
// 蜂窝能量护盾(VisualShader 导出重写版)
// 结构:UV 球面透镜扭曲 → 动画平铺 → 采样蜂窝贴图 → 亮度/alpha 阈值出线纹
//       → 径向合成(中心淡出 + 外围高亮轮廓) → Color_Shield 上色。
// 说明:
//  - 贴图用作"蜂窝线遮罩源"(取亮度×alpha);透明底贴图也能正确出线。
//  - 默认值即可见:不挂贴图时 hint_default_white 视作全白(整片环带)。
//  - Size_Inner = 中心淡出内径(内透明、向外渐亮);Size_Outer = 外围高亮轮廓起点;
//    Opaticy = 整体不透明度;Brightness = 仅外围高亮轮廓强度(两者职责分开)。
//  - Brightness>1 用于叠加泛光语境;gl_compatibility 无 2D HDR 泛光,
//    想要发光可配 CanvasItemMaterial 加法混合或叠加发光层。
//  - 修复:上一版导出代码 n_out39p0 未初始化即自读(GPU 未定义行为 → 常黑)。
// ══════════════════════════════════════════════════════════════════════

uniform vec4 Color_Shield : source_color = vec4(0.45, 0.85, 1.0, 1.0);
// 蜂窝线阈值:贴图亮度低于该值的像素不出线(0~1)
uniform float Step : hint_range(0.0, 1.0) = 0.15;
// 平铺滚动速度(UV/秒):x 横向、y 纵向
uniform vec3 Speed = vec3(0.03, 0.02, 0.0);
uniform sampler2D ShieldTexture : hint_default_white;
// 中心淡出内径:该半径以内完全透明,向外渐亮至半径 0.5(原 VisualShader 语义)
uniform float Size_Inner : hint_range(0.0, 0.5) = 0.18;
// 外围高亮轮廓起始半径:该半径到 0.5 之间叠加实心高亮边(无蜂窝纹)
uniform float Size_Outer : hint_range(0.0, 0.5) = 0.45;
// 整体不透明度:蜂窝纹与轮廓的全局透明度
uniform float Opaticy : hint_range(0.0, 1.0) = 1.0; // 保留原参数名
// 外围高亮轮廓强度:只作用于 Size_Outer→0.5 的高亮边(不改变蜂窝纹亮度)
uniform float Brightness : hint_range(0.0, 4.0) = 1.2;

// SphericalUV:球面透镜扭曲(保留原公式)
vec2 sphericalFunc(vec2 uv, vec2 center, vec2 radius, vec2 spin, vec2 localPos) {
	vec2 p = (uv - center) * 2.0;
	float r = length((p - localPos) * radius) + 0.0001;
	float f = (1.0 - sqrt(max(1.0 - r, 0.0))) / r; // max 防 r>1 时 sqrt 负数
	return mod(vec2((p.x * 0.5 * f + spin.x) + 0.5,
					(p.y * 0.5 * f + spin.y) + 0.5), vec2(1.0));
}

// TilingAndOffsetUVAnimated:动画平铺偏移
vec2 tilingNoffsetAnimatedFunc(vec2 uv, float time, vec2 offset) {
	return mod(uv + offset * time, vec2(1.0));
}

void fragment() {
	// 1) 球面扭曲 → 动画平铺
	vec2 sphericalUV = sphericalFunc(UV, vec2(0.5, 0.5), vec2(1.0), vec2(0.0), vec2(0.0));
	vec2 animatedUV = tilingNoffsetAnimatedFunc(sphericalUV, TIME, Speed.xy);

	// 2) 贴图采样:亮度与 alpha 共同决定蜂窝线
	vec4 tex = texture(ShieldTexture, animatedUV);
	float luminance = dot(tex.rgb, vec3(0.333333));
	float pattern = step(Step, luminance) * tex.a;

	// 3) 径向分解(原 VisualShader 语义):
	//    · 中心淡出渐变:Size_Inner 以内透明,向外渐亮(蜂窝纹随该渐变显现)
	//    · 外围高亮轮廓:Size_Outer → 0.5 叠加实心亮边(不乘蜂窝纹)
	//    · 圆盘硬边界:半径 0.5 外裁掉(精灵方形的四角)
	float d = distance(UV, vec2(0.5));
	float centerFade = smoothstep(Size_Inner, 0.5, d);
	float rimHighlight = smoothstep(Size_Outer, 0.5, d);
	float diskMask = 1.0 - step(0.5, d);

	float patternBody = pattern * centerFade;              // 蜂窝纹(受中心淡出)
	float rimBody = rimHighlight * Brightness;             // 高亮轮廓(受 Brightness)

	// 4) 输出:Opaticy 控整体不透明度,叠加两种径向成分
	COLOR.rgb = Color_Shield.rgb;
	COLOR.a = Color_Shield.a * Opaticy * diskMask * (patternBody + rimBody);
}
