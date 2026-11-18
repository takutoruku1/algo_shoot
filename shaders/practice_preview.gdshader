shader_type canvas_item;
render_mode unshaded;

uniform sampler2D battle : hint_screen_texture, repeat_disable, filter_linear;
uniform float field_left_uv;

void fragment() {
    vec2 field_uv = vec2(mix(field_left_uv, 1.0, UV.x), UV.y);
    COLOR = vec4(textureLod(battle, field_uv, 0.0).rgb, 1.0);
}
