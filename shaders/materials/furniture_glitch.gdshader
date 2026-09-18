shader_type canvas_item;

// ══════════════════════════════════════════════════════════════════════
// 乱码块滤镜(整件合成帧):挂在 CanvasGroup 上,采样组内合成结果(TEXTURE),
// 对整件(含描边、本体、多部件)做数字损坏效果。各效果均有独立开关(0=关闭,可任意组合):
//   行撕裂带水平位移 → band_strength
//   量化块错位       → block_strength
//   RGB 分量错位     → rgb_shift_strength(载体 = 撕裂带∪错位块的损坏区域)
//   偶发亮纹闪线     → flash_strength
//   整体墨绿重着色   → body_strength
//   损坏区域染色     → tint_strength
// 常驻故障 → persistent_glitch:损坏层持续激活(关闭时间片切换,不再"正常↔故障"闪断)
// 位移以纹理像素为单位(TEXTURE_PIXEL_SIZE),与贴图分辨率无关。
// cube 型(程序字符面、无真实家具贴图)不挂此滤镜。
// ══════════════════════════════════════════════════════════════════════

uniform float seed : hint_range(0.0, 100.0) = 0.0;
uniform float intensity : hint_range(0.0, 1.0) = 0.6;
uniform float speed : hint_range(0.0, 5.0) = 1.0;
// 以下尺寸单位 = 纹理像素(相对合成图分辨率,自动适配不同家具)
uniform float max_shift : hint_range(1.0, 60.0) = 12.0;  // 行撕裂最大位移
uniform float band_px : hint_range(4.0, 120.0) = 24.0;   // 撕裂带高
uniform float block_px : hint_range(2.0, 40.0) = 15.0;   // 量化块边长

// ── 效果独立开关/强度(0 = 关闭,可任意组合)────────────────────────────
/// 常驻故障:true = 损坏层(撕裂带/错位块)持续激活,不做"正常↔故障"的时间片切换
/// (各效果开关仍独立生效;false = 恢复时间片掷骰节奏)
uniform bool persistent_glitch = true;
uniform float band_strength : hint_range(0.0, 1.0) = 0.0;      // 行撕裂带水平位移
uniform float block_strength : hint_range(0.0, 1.0) = 1.0;     // 量化块错位
uniform float rgb_shift_strength : hint_range(0.0, 1.0) = 1.0; // RGB 通道错位(载体 = 损坏区域:撕裂带∪错位块)
uniform float flash_strength : hint_range(0.0, 1.0) = 1.0;     // 亮纹闪线
// 整体墨绿重着色(按辉度重映射,保留贴图明暗结构;叠在损坏纹之下)
uniform vec4 body_color : source_color = vec4(0.07, 0.28, 0.12, 1.0);
uniform float body_strength : hint_range(0.0, 1.0) = 0.5;      // 0 = 原色,1 = 完全墨绿
// 损坏区域/闪线主题色(默认亮绿,与乱码块同色系;形成"墨绿面 + 亮绿损坏纹"配色)
uniform vec4 glitch_tint : source_color = vec4(0.07, 0.28, 0.12, 1.0);
uniform float tint_strength : hint_range(0.0, 1.0) = 0.5;      // 损坏区域的染色强度(0 = 关闭染色)

float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float hash1(float p) { return fract(sin(p * 127.1) * 43758.5453); }

void fragment() {
    vec2 px = UV / TEXTURE_PIXEL_SIZE;      // 当前像素坐标(纹理像素)
    float t = TIME * speed;

    // ── 行撕裂带:部分带整带水平位移(低频变化,数字损坏的"条带感";band_strength=0 关闭)──
    float band = floor(px.y / band_px);
    float bandRand = hash2(vec2(band + floor(t * 2.3), seed));
    float glitchOn = step(1.0 - intensity, bandRand) * band_strength;
    if (persistent_glitch) glitchOn = band_strength; // 常驻:开启撕裂带时全时段激活
    float dispPx = (hash2(vec2(band, seed * 7.13 + floor(t * 9.7))) - 0.5)
                 * 2.0 * max_shift * glitchOn;

    // ── 量化块错位:突发时段整块取样偏移(像素块"挪位";block_strength=0 关闭)──
    vec2 blockCoord = floor(px / block_px);
    float burst = step(1.0 - intensity * 0.6, hash1(floor(t * 1.6) + seed * 3.1)) * block_strength;
    if (persistent_glitch) burst = block_strength; // 常驻:开启错位块时全时段激活(块内偏移仍随时间滚动)
    vec2 jitterPx = vec2(
            (hash2(blockCoord + vec2(seed, floor(t * 6.0))) - 0.5) * 2.0,
            (hash2(blockCoord + vec2(seed * 2.7, floor(t * 4.3))) - 0.5) * 2.0)
        * block_px * burst;

    // ── RGB 错位:损坏区域(撕裂带∪错位块)内通道分离(边缘彩色拖影;rgb_shift_strength=0 关闭)──
    float rgbCarrier = max(glitchOn, burst);
    float rgbShiftPx = rgbCarrier * (hash1(px.y * 0.37 + seed + floor(t * 13.0)) - 0.5) * 5.0
        * rgb_shift_strength;

    vec2 shiftPx = vec2(dispPx + jitterPx.x, jitterPx.y);
    vec2 uvG = UV - shiftPx * TEXTURE_PIXEL_SIZE;
    vec2 uvR = uvG + vec2(rgbShiftPx, 0.0) * TEXTURE_PIXEL_SIZE;
    vec2 uvB = uvG - vec2(rgbShiftPx, 0.0) * TEXTURE_PIXEL_SIZE;

    vec3 r = texture(TEXTURE, uvR).rgb;
    vec3 g = texture(TEXTURE, uvG).rgb;
    vec3 b = texture(TEXTURE, uvB).rgb;
    vec3 col = vec3(r.r, g.g, b.b);

    // ── 整体墨绿重着色:按辉度重映射(保留贴图明暗结构,色相统一为墨绿)──
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    vec3 bodyCol = mix(col, body_color.rgb * (0.35 + lum * 1.6), body_strength);

    // ── 乱码主题色:损坏区域(撕裂带/错位块)向 tint 偏(亮绿,叠在墨绿体色上)──
    float glitchAmount = max(glitchOn, burst * 0.7);
    bodyCol = mix(bodyCol, glitch_tint.rgb, glitchAmount * tint_strength);

    // ── 偶发亮纹(细扫描线噪闪;混向主题色,呈亮绿闪线;flash_strength=0 关闭)──
    float flash = step(0.996, hash2(vec2(floor(px.y / 2.0), floor(t * 26.0) + seed)));
    bodyCol = mix(bodyCol, glitch_tint.rgb, flash * 0.8 * flash_strength);

    COLOR = vec4(bodyCol, texture(TEXTURE, uvG).a);
}
