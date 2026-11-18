shader_type canvas_item;
render_mode unshaded;
uniform float alpha_floor = 0.90;
uniform float alpha_ceiling = 0.99;

void fragment() {
    COLOR.a *= smoothstep(alpha_floor, alpha_ceiling, texture(TEXTURE, UV).a);
}
