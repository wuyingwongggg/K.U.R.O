shader_type canvas_item;

// ══════════════════════════════════════════════════════════════════════
// 乱码块滤镜(整件合成帧):挂在 CanvasGroup 上,采样组内合成结果(TEXTURE),
// 对整件(含描边、本体、多部件)做数字损坏效果:
//   行撕裂带水平位移 + 量化块错位 + RGB 分量错位 + 偶发亮纹闪线。
// 位移以纹理像素为单位(TEXTURE_PIXEL_SIZE),与贴图分辨率无关。
// cube 型(程序字符面、无真实家具贴图)不挂此滤镜。
// ══════════════════════════════════════════════════════════════════════

uniform float seed : hint_range(0.0, 100.0) = 0.0;
uniform float intensity : hint_range(0.0, 1.0) = 0.6;
uniform float speed : hint_range(0.0, 5.0) = 1.0;
// 以下尺寸单位 = 纹理像素(相对合成图分辨率,自动适配不同家具)
uniform float max_shift : hint_range(1.0, 60.0) = 12.0;  // 行撕裂最大位移
uniform float band_px : hint_range(4.0, 120.0) = 24.0;   // 撕裂带高
uniform float block_px : hint_range(2.0, 40.0) = 10.0;   // 量化块边长

float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float hash1(float p) { return fract(sin(p * 127.1) * 43758.5453); }

void fragment() {
    vec2 px = UV / TEXTURE_PIXEL_SIZE;      // 当前像素坐标(纹理像素)
    float t = TIME * speed;

    // ── 行撕裂带:部分带整带水平位移(低频变化,数字损坏的"条带感")──
    float band = floor(px.y / band_px);
    float bandRand = hash2(vec2(band + floor(t * 2.3), seed));
    float glitchOn = step(1.0 - intensity, bandRand);
    float dispPx = (hash2(vec2(band, seed * 7.13 + floor(t * 9.7))) - 0.5)
                 * 2.0 * max_shift * glitchOn;

    // ── 量化块错位:突发时段整块取样偏移(像素块"挪位")──
    vec2 blockCoord = floor(px / block_px);
    float burst = step(1.0 - intensity * 0.6, hash1(floor(t * 1.6) + seed * 3.1));
    vec2 jitterPx = vec2(
            (hash2(blockCoord + vec2(seed, floor(t * 6.0))) - 0.5) * 2.0,
            (hash2(blockCoord + vec2(seed * 2.7, floor(t * 4.3))) - 0.5) * 2.0)
        * block_px * burst;

    // ── RGB 错位:撕裂带内通道分离(边缘彩色拖影)──
    float rgbShiftPx = glitchOn * (hash1(px.y * 0.37 + seed + floor(t * 13.0)) - 0.5) * 5.0;

    vec2 shiftPx = vec2(dispPx + jitterPx.x, jitterPx.y);
    vec2 uvG = UV - shiftPx * TEXTURE_PIXEL_SIZE;
    vec2 uvR = uvG + vec2(rgbShiftPx, 0.0) * TEXTURE_PIXEL_SIZE;
    vec2 uvB = uvG - vec2(rgbShiftPx, 0.0) * TEXTURE_PIXEL_SIZE;

    vec3 r = texture(TEXTURE, uvR).rgb;
    vec3 g = texture(TEXTURE, uvG).rgb;
    vec3 b = texture(TEXTURE, uvB).rgb;
    vec3 col = vec3(r.r, g.g, b.b);

    // ── 偶发亮纹(细扫描线噪闪)──
    float flash = step(0.996, hash2(vec2(floor(px.y / 2.0), floor(t * 26.0) + seed)));
    col = mix(col, vec3(0.9, 1.0, 0.95), flash * 0.8);

    COLOR = vec4(col, texture(TEXTURE, uvG).a);
}
