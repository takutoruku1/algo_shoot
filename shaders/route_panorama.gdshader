shader_type canvas_item;

uniform float scroll_offset = 0.0;
varying vec4 vertex_tint;

void vertex() {
    vertex_tint = COLOR;
}

void fragment() {
    vec2 uv = vec2(clamp(UV.x + scroll_offset, 0.0, 1.0), UV.y);
    vec4 art = texture(TEXTURE, uv);
    COLOR = art * vertex_tint;
}
