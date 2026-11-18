shader_type canvas_item;

uniform float scroll_offset = 0.0;
uniform float loop_period = 0.0;
varying vec4 vertex_tint;

void vertex() {
    vertex_tint = COLOR;
}

void fragment() {
    vec2 uv = vec2(clamp(UV.x + scroll_offset, 0.0, 1.0), UV.y);
    vec4 art = texture(TEXTURE, uv);
    if (loop_period > 0.0) {
        float x = mod(UV.x + scroll_offset, loop_period);
        art = texture(TEXTURE, vec2(x, UV.y));
        float overlap = 1.0 - loop_period;
        if (x < overlap) {
            vec4 tail = texture(TEXTURE, vec2(x + loop_period, UV.y));
            // 透明な近景でも継ぎ目に黒い縁が出ないよう、合成時だけ乗算済みの色を使う。
            art = mix(vec4(tail.rgb * tail.a, tail.a), vec4(art.rgb * art.a, art.a), smoothstep(0.0, overlap, x));
            art.rgb /= max(art.a, 0.0001);
        }
    }
    COLOR = art * vertex_tint;
}
