shader_type canvas_item;

// ═══════════════════════════════════════════════════════════════
//  卡通火焰 shader（环形/弧形火焰带）
//  技术路线：噪声滚动 → 程序化环形遮罩 → 色带分级（卡通着色）→ 火花
//  使用前需要分配 noise_tex / noise_tex2 两张噪声图（可同一张）
// ═══════════════════════════════════════════════════════════════

// ═══════════════════════════════════════════════════════════════
//  伪 3D（与 fire_effect 相同：顶点旋转 + 透视投影）
// ═══════════════════════════════════════════════════════════════
uniform bool cull_backface = true;                 // 背面剔除（旋转超过 90° 时隐藏背面）
uniform float fov : hint_range(1, 179) = 1.0;      // 透视强度（越小透视越强）
uniform float rot_x_deg : hint_range(-360, 360) = 0.0; // X 轴旋转（俯仰）
uniform float rot_y_deg : hint_range(-360, 360) = 0.0; // Y 轴旋转（水平）
uniform float inset : hint_range(0, 1) = 0.0;      // 内缩量（顶点向中心收缩）

varying vec2 offset;
varying vec3 world_pos_3d;

// ── 纹理输入 ───────────────────────────────────────────────────
uniform sampler2D noise_tex : repeat_enable;       // 噪声图 1：火焰主体翻滚
uniform sampler2D noise_tex2 : repeat_enable;      // 噪声图 2：第二层细节/内部描边/火花
uniform sampler2D formato_custom_tex : repeat_enable; // 自定义形状纹理（用纹理代替程序环时）

// ═══════════════════════════════════════════════════════════════
//  边缘与空间
// ═══════════════════════════════════════════════════════════════
uniform float margem_seguranca : hint_range(0.0, 10.0) = 0.2; // 安全边距：UV 向外扩张的比例，
                                                              // 防止火焰/火花在精灵边缘被硬切

// ═══════════════════════════════════════════════════════════════
//  颜色与风格（卡通四段色带）
// ═══════════════════════════════════════════════════════════════
uniform bool modo_rgb = false;                     // RGB 循环模式：颜色随 HSV 色相随时间旋转
uniform vec4 cor_contorno : source_color = vec4(0.0, 0.0, 0.0, 1.0); // 描边色（最外层黑圈 + 内部轮廓线）
uniform vec4 cor_externa : source_color = vec4(0.8, 0.15, 0.0, 1.0); // 外焰色（红橙）
uniform vec4 cor_media : source_color = vec4(1.0, 0.5, 0.0, 1.0);    // 中焰色（橙）
uniform vec4 cor_nucleo : source_color = vec4(1.0, 0.9, 0.2, 1.0);   // 核心色（亮黄，最热处）

uniform bool outline_interno_ativo = true;         // 内部描边：在色带边界随机画黑线（卡通轮廓感）
uniform float outline_interno_chance : hint_range(0.0, 1.0) = 0.3;    // 描边出现概率（噪声阈值）
uniform float outline_interno_velocidade : hint_range(1.0, 20.0) = 10.0; // 描边闪烁速度

// ═══════════════════════════════════════════════════════════════
//  形状与遮罩
// ═══════════════════════════════════════════════════════════════
uniform bool usar_rastro_procedural = true;        // 程序化环形轨迹：用数学计算生成环/弧形火焰带
uniform float angulo_arco : hint_range(0.0, 360.0) = 180.0;  // 弧角度（360 = 完整圆环，180 = 半环）
uniform float espessura_rastro : hint_range(0.0, 0.5) = 0.2; // 火焰带厚度（环宽）
uniform float suavizacao_borda : hint_range(0.01, 0.5) = 0.1; // 边缘柔化（越大边缘越模糊）

uniform bool usar_textura_formato = false;         // 用自定义纹理作为火焰形状（替代程序环）
uniform vec2 formato_tex_escala = vec2(1.0, 1.0);  // 形状纹理缩放
uniform float mistura_textura_procedural : hint_range(0.0, 1.0) = 0.5; // 纹理形状与程序环的混合比例

uniform bool distorcer_textura = true;             // 纹理扭曲：用噪声扰动采样坐标（火焰流动感）
uniform float distorcao_forca : hint_range(0.0, 0.5) = 0.05;   // 扭曲力度
uniform float distorcao_velocidade : hint_range(0.1, 10.0) = 2.0; // 扭曲动画速度
uniform float distorcao_escala : hint_range(0.1, 10.0) = 3.0;    // 扭曲噪声尺度

// ═══════════════════════════════════════════════════════════════
//  透明度与特效
// ═══════════════════════════════════════════════════════════════
uniform float alpha_geral : hint_range(0.0, 1.0) = 1.0; // 整体透明度
uniform bool radomizar_alpha = false;              // 透明度波动（忽明忽暗）
uniform float alpha_min : hint_range(0.0, 1.0) = 0.4;    // 波动最暗值
uniform float alpha_max : hint_range(0.0, 1.0) = 1.0;    // 波动最亮值
uniform float vel_radomizacao : hint_range(1.0, 30.0) = 15.0; // 波动频率

uniform bool pixelado = true;                      // 像素化（复古风格）
uniform float qtd_pixels : hint_range(16.0, 256.0) = 64.0; // 像素分辨率（越小像素块越大）
uniform float intensidade_glow : hint_range(1.0, 5.0) = 1.5; // 发光强度（颜色整体放大）

// ═══════════════════════════════════════════════════════════════
//  风与动态火花
// ═══════════════════════════════════════════════════════════════
uniform vec2 direcao_vento = vec2(0.0, -1.0);      // 风向（火焰/火花飘动方向）
uniform float forca_fogo : hint_range(0.1, 5.0) = 1.5;    // 火势：噪声滚动速度
uniform float variacao_caos : hint_range(0.1, 3.0) = 1.0; // 混乱度：噪声采样频率

uniform bool criar_faiscas = true;                 // 生成火花（火焰带边缘/外围的小火星）
uniform float qtd_faiscas : hint_range(0.1, 1.0) = 0.5;    // 火花密度（阈值）
uniform float velocidade_puxao_faiscas : hint_range(0.1, 5.0) = 2.0; // 火花被风拖拽的速度
uniform bool faiscas_com_borda = true;             // 火花带描边（边缘火花用描边色）

// 火花闪烁时间
uniform bool faisca_tempo_radomico = true;         // 随机闪烁周期
uniform float faisca_tempo_min : hint_range(0.1, 5.0) = 0.5;  // 最小周期
uniform float faisca_tempo_max : hint_range(1.0, 10.0) = 3.0; // 最大周期

// 火花大小
uniform float faisca_tamanho_base : hint_range(0.5, 15.0) = 8.0; // 基础大小（噪声采样频率）
uniform bool faisca_tamanho_radomico = true;       // 随机大小
uniform float faisca_tamanho_min : hint_range(0.1, 5.0) = 0.5;   // 最小倍率
uniform float faisca_tamanho_max : hint_range(1.0, 10.0) = 2.0;  // 最大倍率

// ═══════════════════════════════════════════════════════════════
//  叠加层（fire_ring 技法）：卡通火焰渲染后，再叠一层
//  "噪声扭曲采样精灵纹理 + 平涂色 + 噪声调制透明度" 的纹理细节
// ═══════════════════════════════════════════════════════════════
uniform bool overlay_ring = false;                              // 开启叠加层
uniform vec3 overlay_tint : source_color = vec3(1.0, 0.5, 0.1); // 叠加层颜色（平涂）
uniform float overlay_amount : hint_range(0.0, 1.0) = 0.5;      // 叠加强度（越大纹理细节越明显）
uniform float overlay_velocidade : hint_range(0.1, 10.0) = 1.0; // 噪声滚动速度
uniform float overlay_influence : hint_range(0.0, 30.0) = 30.0; // 扭曲强度

/// HSV → RGB 转换（modo_rgb 循环变色用）
vec3 hsv2rgb(vec3 c) {
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

// 顶点阶段：旋转 + 透视投影（同 fire_effect），把精灵四角位移到伪 3D 位置
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

    VERTEX += (UV - 0.5) / TEXTURE_PIXEL_SIZE * perspective_scale * (1.0 - inset);
}

void fragment() {
    // ── 伪 3D：背面剔除 + 透视投影出当前片元对应的 UV ──
    if (cull_backface && world_pos_3d.z <= 0.0) discard;

    vec2 projected_uv = (world_pos_3d.xy / world_pos_3d.z) - offset + 0.5;
    if (projected_uv.x < 0.0 || projected_uv.x > 1.0 || projected_uv.y < 0.0 || projected_uv.y > 1.0)
        discard;

    // ── 0. 像素化：先把投影 UV 量化成网格（若开启） ──
    vec2 uv = projected_uv;
    if (pixelado) uv = floor(uv * qtd_pixels) / qtd_pixels;

    // ── 1. 安全边距：UV 向外扩张，让火焰贴边不被精灵边界硬切 ──
    vec2 uv_segura = (uv - 0.5) * (1.0 + margem_seguranca * 2.0) + 0.5;
    // 超出 [0,1] 的像素直接透明（bounds_mask = 0）
    float bounds_mask = step(0.0, uv_segura.x) * step(uv_segura.x, 1.0) * step(0.0, uv_segura.y) * step(uv_segura.y, 1.0);

    // ── 2. 纹理扭曲：用噪声扰动采样坐标（给形状纹理/火苗添加流动感） ──
    vec2 uv_textura = uv_segura;
    if (distorcer_textura) {
        vec2 noise_desloc = texture(noise_tex, uv_segura * distorcao_escala + TIME * distorcao_velocidade).rg;
        uv_textura += (noise_desloc - 0.5) * distorcao_forca;
    }

    // ── 3. 形状遮罩：决定"哪里是火焰" ──
    float mask_proc = 0.0;       // 程序化环形遮罩
    float mask_faisca_area = 0.0; // 火花可生成区域（比火焰带略宽）
    if (usar_rastro_procedural) {
        vec2 p = uv_segura - 0.5;
        float d = length(p);                        // 到中心的距离
        float a = degrees(atan(p.y, p.x)) + 180.0;  // 极角（0~360）
        // 角度遮罩：|a-180| 越小越接近"前方"（弧中心），smoothstep 给出软边
        float m_a = smoothstep(angulo_arco / 2.0 + suavizacao_borda * 10.0, angulo_arco / 2.0, abs(a - 180.0));
        // 径向遮罩：d 接近 0.3（环线半径）处为 1 → 甜甜圈形状
        float m_d = smoothstep(espessura_rastro + suavizacao_borda, espessura_rastro, abs(d - 0.3));
        mask_proc = m_a * m_d;
        mask_faisca_area = m_a * smoothstep(espessura_rastro + margem_seguranca, espessura_rastro, abs(d - 0.3));
    }

    float mask_tex = 0.0;        // 纹理形状遮罩
    if (usar_textura_formato) {
        vec2 uv_t = (uv_textura - 0.5) / formato_tex_escala + 0.5;
        mask_tex = texture(formato_custom_tex, uv_t).a;  // 用纹理 alpha 作遮罩
        mask_tex *= step(0.0, uv_t.x) * step(uv_t.x, 1.0) * step(0.0, uv_t.y) * step(uv_t.y, 1.0);
    }

    // 最终遮罩：程序环 / 纹理 / 两者混合 / 全开
    float mask_final = 0.0;
    if (usar_rastro_procedural && usar_textura_formato) mask_final = mix(mask_proc, mask_tex, mistura_textura_procedural);
    else if (usar_rastro_procedural) mask_final = mask_proc;
    else if (usar_textura_formato) mask_final = mask_tex;
    else mask_final = 1.0;

    // ── 4. 火焰噪声：两层滚动噪声叠加，乘遮罩得"火焰强度场" ──
    vec2 scroll1 = TIME * direcao_vento * forca_fogo;                      // 主层滚动（沿风向）
    vec2 scroll2 = TIME * direcao_vento * (forca_fogo * 1.2) + vec2(TIME * 0.1, 0.0); // 细节层（略快 + 横移）
    float n1 = texture(noise_tex, uv_segura * variacao_caos + scroll1).r;
    float n2 = texture(noise_tex2, uv_segura * (variacao_caos * 1.5) + scroll2).r;
    float fire_logic = (n1 * 0.6 + n2 * 0.4) * mask_final;  // 0~1 的火焰强度

    // ── 5. 卡通四段色带：按 fire_logic 阈值分档上色 ──
    vec4 col = vec4(0.0);
    float alpha = 0.0;
    vec4 c_core = cor_nucleo; vec4 c_mid = cor_media; vec4 c_outer = cor_externa;

    // RGB 循环模式：三层颜色按 HSV 色相旋转
    if (modo_rgb) {
        c_core.rgb = hsv2rgb(vec3(TIME * 0.5, 0.8, 1.0));
        c_mid.rgb = hsv2rgb(vec3(TIME * 0.5 - 0.1, 0.9, 0.8));
        c_outer.rgb = hsv2rgb(vec3(TIME * 0.5 - 0.2, 1.0, 0.6));
    }

    // 强度分级：核心(>0.55) → 中焰(>0.35) → 外焰(>0.15) → 描边(>0.05) → 透明
    if (fire_logic > 0.55) { col = c_core; alpha = 1.0; }
    else if (fire_logic > 0.35) { col = c_mid; alpha = 1.0; }
    else if (fire_logic > 0.15) { col = c_outer; alpha = 1.0; }
    else if (fire_logic > 0.05) { col = cor_contorno; alpha = 1.0; }

    // 内部描边：在中焰/核心的分界线上，按噪声概率随机画黑线（卡通轮廓）
    if (outline_interno_ativo && fire_logic > 0.15) {
        float db1 = abs(fire_logic - 0.55); float db2 = abs(fire_logic - 0.35); // 距分界线的距离
        if (db1 < 0.03 || db2 < 0.03) {                       // 处于分界线附近
            float ch = texture(noise_tex2, uv_segura * 5.0 + vec2(TIME * outline_interno_velocidade)).r;
            if (ch < outline_interno_chance) col = cor_contorno; // 概率性画黑线 → 闪烁轮廓
        }
    }

    // ── 6. 火花：火焰带外围的小火星，被风拖拽 + 闪烁 + 随机大小 ──
    if (criar_faiscas && fire_logic <= 0.05 && (mask_faisca_area > 0.01 || (usar_textura_formato && mask_tex > 0.01))) {
        vec2 uv_f = uv_segura - (direcao_vento * TIME * velocidade_puxao_faiscas); // 沿风向移动采样坐标

        // 火花大小（随机）：用噪声决定倍率，乘基础大小 = 采样频率
        float tam = faisca_tamanho_base;
        if (faisca_tamanho_radomico) {
            float rt = texture(noise_tex2, uv_f * 3.0).r;
            tam *= mix(faisca_tamanho_min, faisca_tamanho_max, rt);
        }

        // 火花形状：高频噪声阈值化 → 点状
        float sn = texture(noise_tex, uv_f * tam).r;
        // 闪烁：随机周期正弦波，负值隐藏火花
        float blink = 1.0;
        if (faisca_tempo_radomico) {
            float r_temp = mix(faisca_tempo_min, faisca_tempo_max, texture(noise_tex2, uv_f * 2.0).r);
            blink = sin(TIME * r_temp);
        }

        float limiar = 1.0 - (qtd_faiscas * 0.1); // 密度阈值（qtd 越大阈值越低 → 火花越多）
        if (sn > limiar && blink > 0.0) {
            if (faiscas_com_borda && sn < limiar + 0.02) col = cor_contorno; // 火花边缘用描边色
            else col = c_mid;
            alpha = 1.0;
        }
    }

    // ── 6.5 叠加层（fire_ring 技法）：卡通火焰之上再叠一层
    //     噪声扭曲采样精灵纹理 → 平涂色 → 噪声调制透明度（只在火焰 alpha 范围内）
    if (overlay_ring && alpha > 0.001) {
        float on1 = texture(noise_tex, UV + TIME * overlay_velocidade * 0.15).r - 0.5;
        float on2 = texture(noise_tex2, UV - TIME * overlay_velocidade * 0.25).r - 0.5;
        float mixed = on1 * on2 * overlay_influence;
        vec2 off = vec2(0.02, 0.15) * mixed;                       // UV 扭曲偏移
        float ring_alpha = texture(TEXTURE, UV + off).a;           // 精灵纹理形状
        ring_alpha *= 1.0 - mixed * 6.0 * overlay_amount;          // 噪声调制透明度
        ring_alpha = max(ring_alpha, 0.0) * (1.0 - overlay_amount);
        col.rgb += overlay_tint * ring_alpha * alpha;              // 加法叠加（仅火焰内）
    }

    // ── 7. 输出：发光放大 + 透明度合成 ──
    col.rgb *= intensidade_glow;                                     // 发光
    float a_final = alpha;
    if (radomizar_alpha) a_final *= mix(alpha_min, alpha_max, (sin(TIME * vel_radomizacao) * 0.5 + 0.5)); // 透明度波动
    COLOR = vec4(col.rgb, a_final * alpha_geral * bounds_mask);      // 整体透明度 × 边缘安全区
}
