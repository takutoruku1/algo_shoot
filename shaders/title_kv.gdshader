shader_type canvas_item;

uniform float time_sec = 0.0;

// Masks follow title_mina_v2: wind starts beyond the face and both hands.
vec2 wind_offset(vec2 uv) {
    float upper = smoothstep(0.79, 0.98, uv.x)
        * smoothstep(0.27, 0.39, uv.y) * (1.0 - smoothstep(0.62, 0.74, uv.y));
    float lower = smoothstep(0.83, 0.99, uv.x)
        * smoothstep(0.62, 0.78, uv.y) * (1.0 - smoothstep(0.96, 1.0, uv.y));
    float hem = smoothstep(0.52, 0.65, uv.x) * (1.0 - smoothstep(0.8, 0.9, uv.x))
        * smoothstep(0.79, 1.0, uv.y);
    float breeze = sin(time_sec * 1.05 - uv.x * 5.0 + uv.y * 2.2);
    float trailing = sin(time_sec * 1.05 - uv.x * 5.0 + uv.y * 2.2 - 0.8);
    vec2 upper_shift = vec2(breeze * 0.0014, breeze * 0.0042) * upper;
    vec2 lower_shift = vec2(trailing * 0.0018, trailing * 0.0052) * lower;
    vec2 hem_shift = vec2(sin(time_sec * 1.05 - uv.y * 3.0) * 0.0014, 0.0) * hem;
    return upper_shift + lower_shift + hem_shift;
}

void fragment() {
    COLOR = texture(TEXTURE, clamp(UV + wind_offset(UV), vec2(0.0), vec2(1.0)));
}
