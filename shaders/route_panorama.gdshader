shader_type canvas_item;

uniform float scroll_offset = 0.0;
uniform float edge_blend = 0.08;
varying vec4 vertex_tint;

void vertex() {
    vertex_tint = COLOR;
}

void fragment() {
    float period = 1.0 - edge_blend;
    vec2 uv = vec2(mod(UV.x + scroll_offset, period), UV.y);
    vec4 art = texture(TEXTURE, uv);
    if (uv.x < edge_blend) {
        vec4 previous = texture(TEXTURE, uv + vec2(period, 0.0));
        float weight = smoothstep(0.0, edge_blend, uv.x);
        float alpha = mix(previous.a, art.a, weight);
        vec3 premultiplied = mix(previous.rgb * previous.a, art.rgb * art.a, weight);
        art = vec4(premultiplied / max(alpha, 0.00001), alpha);
    }
    COLOR = art * vertex_tint;
}
