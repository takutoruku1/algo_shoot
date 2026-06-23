// title_kv.gdshader — タイトルのキービジュアル1枚専用。
//   そよ風の髪なびき：髪のある帯（画面左〜中央・上下中ほど）に限定して UV を横方向へ微小 sin 変位。
//   背景・顔・体が目立って歪まないよう、振幅はごく弱く・マスクで局所化する。
//   （※微笑み差分は不気味になるため廃止。顔には一切手を加えない。）
shader_type canvas_item;

uniform float time_sec      = 0.0;  // 経過秒（CPU から供給）
uniform float wave_amp      = 0.006; // UV 単位の最大変位（≒数px相当）
uniform float wave_speed    = 0.9;   // そよ風の速さ
uniform float wave_freq     = 5.0;   // 縦方向の波数（毛束のうねり）

// 髪マスク：ミナの長い銀髪は画面左半分へ流れる。波はその「左へ流れる毛束」に効かせ、
//   顔(x≈0.52〜0.72, y≈0.12〜0.45)・オーブ(下中央)・背景は守る。UV(0,0)=左上。
float hair_mask(vec2 uv) {
    // 横：左端=最強 → 0.50 付近でほぼ0（顔の手前で消す）。
    float hx = smoothstep(0.54, 0.02, uv.x);
    // 縦：髪のある帯（おおよそ 0.15〜0.95）を 1、上下端を 0 に。
    float top = smoothstep(0.08, 0.26, uv.y);
    float bot = smoothstep(0.99, 0.80, uv.y);
    float hy = top * bot;
    // 顔の矩形(x:0.50-0.74, y:0.10-0.48)を確実にゼロへ（表情を波で絶対に歪ませない）。
    float fx = smoothstep(0.48, 0.56, uv.x) * (1.0 - smoothstep(0.72, 0.80, uv.x));
    float fy = smoothstep(0.08, 0.16, uv.y) * (1.0 - smoothstep(0.42, 0.50, uv.y));
    float face_guard = 1.0 - clamp(fx * fy, 0.0, 1.0);
    return hx * hy * face_guard;
}

void fragment() {
    vec2 uv = UV;
    float m = hair_mask(uv);
    // 横方向の sin 変位（縦位置と時間で位相を回す＝毛束が順にうねる）。
    float phase = uv.y * wave_freq + time_sec * wave_speed;
    float disp  = sin(phase) * wave_amp * m;
    // 微弱な縦揺れも足して「そよぐ」質感に（横の 1/3 程度）。
    float dispY = cos(phase * 0.7 + 1.3) * wave_amp * 0.33 * m;
    vec2 wuv = clamp(uv + vec2(disp, dispY), vec2(0.0), vec2(1.0));
    COLOR = texture(TEXTURE, wuv);
}
