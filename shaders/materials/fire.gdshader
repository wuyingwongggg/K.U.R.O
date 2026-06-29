shader_type canvas_item;

// Texturas
uniform sampler2D noise_tex : repeat_enable;
uniform sampler2D noise_tex2 : repeat_enable;
uniform sampler2D formato_custom_tex : source_color; 

// ==========================================
//  LIMITES E ESPAÇO
// ==========================================
uniform float margem_seguranca : hint_range(0.0, 0.5) = 0.2; 

// ==========================================
//  CORES E ESTILO
// ==========================================
uniform bool modo_rgb = false;
uniform vec4 cor_contorno : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 cor_externa : source_color = vec4(0.8, 0.15, 0.0, 1.0);
uniform vec4 cor_media : source_color = vec4(1.0, 0.5, 0.0, 1.0);
uniform vec4 cor_nucleo : source_color = vec4(1.0, 0.9, 0.2, 1.0);

uniform bool outline_interno_ativo = true;
uniform float outline_interno_chance : hint_range(0.0, 1.0) = 0.3;
uniform float outline_interno_velocidade : hint_range(1.0, 20.0) = 10.0;

// ==========================================
//  FORMA E MÁSCARAS (Ajustado)
// ==========================================
uniform bool usar_rastro_procedural = true; 
uniform float angulo_arco : hint_range(0.0, 360.0) = 180.0;
uniform float espessura_rastro : hint_range(0.0, 0.5) = 0.2;
uniform float suavizacao_borda : hint_range(0.01, 0.5) = 0.1;

uniform bool usar_textura_formato = false;
uniform vec2 formato_tex_escala = vec2(1.0, 1.0); // Escolher tamanho da textura
uniform float mistura_textura_procedural : hint_range(0.0, 1.0) = 0.5;

uniform bool distorcer_textura = true;
uniform float distorcao_forca : hint_range(0.0, 0.5) = 0.05;
uniform float distorcao_velocidade : hint_range(0.1, 10.0) = 2.0;
uniform float distorcao_escala : hint_range(0.1, 10.0) = 3.0;

// ==========================================
//  TRANSPARÊNCIA E EFEITOS
// ==========================================
uniform float alpha_geral : hint_range(0.0, 1.0) = 1.0;
uniform bool radomizar_alpha = false;
uniform float alpha_min : hint_range(0.0, 1.0) = 0.4;
uniform float alpha_max : hint_range(0.0, 1.0) = 1.0;
uniform float vel_radomizacao : hint_range(1.0, 30.0) = 15.0;

uniform bool pixelado = true;
uniform float qtd_pixels : hint_range(16.0, 256.0) = 64.0;
uniform float intensidade_glow : hint_range(1.0, 5.0) = 1.5;

// ==========================================
//  VENTO E FAÍSCAS DINÂMICAS
// ==========================================
uniform vec2 direcao_vento = vec2(0.0, -1.0);
uniform float forca_fogo : hint_range(0.1, 5.0) = 1.5;
uniform float variacao_caos : hint_range(0.1, 3.0) = 1.0;

uniform bool criar_faiscas = true;
uniform float qtd_faiscas : hint_range(0.1, 1.0) = 0.5;
uniform float velocidade_puxao_faiscas : hint_range(0.1, 5.0) = 2.0;
uniform bool faiscas_com_borda = true;

// Tempos de Spawn/Blink
uniform bool faisca_tempo_radomico = true;
uniform float faisca_tempo_min : hint_range(0.1, 5.0) = 0.5;
uniform float faisca_tempo_max : hint_range(1.0, 10.0) = 3.0;

// Tamanhos
uniform float faisca_tamanho_base : hint_range(0.5, 15.0) = 8.0;
uniform bool faisca_tamanho_radomico = true;
uniform float faisca_tamanho_min : hint_range(0.1, 5.0) = 0.5;
uniform float faisca_tamanho_max : hint_range(1.0, 10.0) = 2.0;

vec3 hsv2rgb(vec3 c) {
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

void fragment() {
    vec2 uv = UV;
    if (pixelado) uv = floor(uv * qtd_pixels) / qtd_pixels;

    // 1. Margem de Segurança
    vec2 uv_segura = (uv - 0.5) * (1.0 + margem_seguranca * 2.0) + 0.5;
    float bounds_mask = step(0.0, uv_segura.x) * step(uv_segura.x, 1.0) * step(0.0, uv_segura.y) * step(uv_segura.y, 1.0);

    // 2. Distorção
    vec2 uv_textura = uv_segura;
    if (distorcer_textura) {
        vec2 noise_desloc = texture(noise_tex, uv_segura * distorcao_escala + TIME * distorcao_velocidade).rg;
        uv_textura += (noise_desloc - 0.5) * distorcao_forca;
    }

    // 3. Máscaras de Formato
    float mask_proc = 0.0;
    float mask_faisca_area = 0.0;
    if (usar_rastro_procedural) {
        vec2 p = uv_segura - 0.5;
        float d = length(p);
        float a = degrees(atan(p.y, p.x)) + 180.0;
        float m_a = smoothstep(angulo_arco / 2.0 + suavizacao_borda * 10.0, angulo_arco / 2.0, abs(a - 180.0));
        float m_d = smoothstep(espessura_rastro + suavizacao_borda, espessura_rastro, abs(d - 0.3));
        mask_proc = m_a * m_d;
        mask_faisca_area = m_a * smoothstep(espessura_rastro + margem_seguranca, espessura_rastro, abs(d - 0.3));
    }

    float mask_tex = 0.0;
    if (usar_textura_formato) {
        vec2 uv_t = (uv_textura - 0.5) / formato_tex_escala + 0.5;
        mask_tex = texture(formato_custom_tex, uv_t).a;
        mask_tex *= step(0.0, uv_t.x) * step(uv_t.x, 1.0) * step(0.0, uv_t.y) * step(uv_t.y, 1.0);
    }

    float mask_final = 0.0;
    if (usar_rastro_procedural && usar_textura_formato) mask_final = mix(mask_proc, mask_tex, mistura_textura_procedural);
    else if (usar_rastro_procedural) mask_final = mask_proc;
    else if (usar_textura_formato) mask_final = mask_tex;
    else mask_final = 1.0;

    // 4. Ruído do Fogo
    vec2 scroll1 = TIME * direcao_vento * forca_fogo;
    vec2 scroll2 = TIME * direcao_vento * (forca_fogo * 1.2) + vec2(TIME * 0.1, 0.0);
    float n1 = texture(noise_tex, uv_segura * variacao_caos + scroll1).r;
    float n2 = texture(noise_tex2, uv_segura * (variacao_caos * 1.5) + scroll2).r;
    float fire_logic = (n1 * 0.6 + n2 * 0.4) * mask_final;

    // 5. Cores e Outlines
    vec4 col = vec4(0.0);
    float alpha = 0.0;
    vec4 c_core = cor_nucleo; vec4 c_mid = cor_media; vec4 c_outer = cor_externa;

    if (modo_rgb) {
        c_core.rgb = hsv2rgb(vec3(TIME * 0.5, 0.8, 1.0));
        c_mid.rgb = hsv2rgb(vec3(TIME * 0.5 - 0.1, 0.9, 0.8));
        c_outer.rgb = hsv2rgb(vec3(TIME * 0.5 - 0.2, 1.0, 0.6));
    }

    if (fire_logic > 0.55) { col = c_core; alpha = 1.0; }
    else if (fire_logic > 0.35) { col = c_mid; alpha = 1.0; }
    else if (fire_logic > 0.15) { col = c_outer; alpha = 1.0; }
    else if (fire_logic > 0.05) { col = cor_contorno; alpha = 1.0; }

    // Outline Interno
    if (outline_interno_ativo && fire_logic > 0.15) {
        float db1 = abs(fire_logic - 0.55); float db2 = abs(fire_logic - 0.35);
        if (db1 < 0.03 || db2 < 0.03) {
            float ch = texture(noise_tex2, uv_segura * 5.0 + vec2(TIME * outline_interno_velocidade)).r;
            if (ch < outline_interno_chance) col = cor_contorno;
        }
    }

    // 6. Faíscas
    if (criar_faiscas && fire_logic <= 0.05 && (mask_faisca_area > 0.01 || (usar_textura_formato && mask_tex > 0.01))) {
        vec2 uv_f = uv_segura - (direcao_vento * TIME * velocidade_puxao_faiscas);
        
        float tam = faisca_tamanho_base;
        if (faisca_tamanho_radomico) {
            float rt = texture(noise_tex2, uv_f * 3.0).r;
            tam *= mix(faisca_tamanho_min, faisca_tamanho_max, rt);
        }
        
        float sn = texture(noise_tex, uv_f * tam).r;
        float blink = 1.0;
        if (faisca_tempo_radomico) {
            float r_temp = mix(faisca_tempo_min, faisca_tempo_max, texture(noise_tex2, uv_f * 2.0).r);
            blink = sin(TIME * r_temp);
        }

        float limiar = 1.0 - (qtd_faiscas * 0.1);
        if (sn > limiar && blink > 0.0) {
            if (faiscas_com_borda && sn < limiar + 0.02) col = cor_contorno;
            else col = c_mid;
            alpha = 1.0;
        }
    }

    col.rgb *= intensidade_glow;
    float a_final = alpha;
    if (radomizar_alpha) a_final *= mix(alpha_min, alpha_max, (sin(TIME * vel_radomizacao) * 0.5 + 0.5));
    
    COLOR = vec4(col.rgb, a_final * alpha_geral * bounds_mask);
}