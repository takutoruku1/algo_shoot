shader_type canvas_item;

uniform sampler2D blink_texture : source_color, filter_linear;
uniform float motion_time = 0.0;
uniform float blink = 0.0;
uniform float opacity = 1.0;

void fragment() {
    vec2 uv = UV;
    // The face and hands stay fixed; wind travels outwards along the loose hair and ribbon.
    float hair = (1.0 - smoothstep(0.36, 0.59, uv.x))
        * smoothstep(0.19, 0.35, uv.y) * (1.0 - smoothstep(0.83, 0.98, uv.y));
    float ribbon = (1.0 - smoothstep(0.35, 0.55, uv.x)) * smoothstep(0.72, 0.9, uv.y);
    uv.y += sin(motion_time * 2.1 + uv.x * 9.0 + uv.y * 6.0) * 0.007 * hair;
    uv.x += sin(motion_time * 1.6 + uv.y * 12.0) * 0.0025 * hair;
    uv.y += sin(motion_time * 2.7 + uv.x * 8.0) * 0.005 * ribbon;
    vec4 base = texture(TEXTURE, uv);
    float eyes = smoothstep(0.602, 0.609, UV.x) * (1.0 - smoothstep(0.734, 0.741, UV.x))
        * smoothstep(0.197, 0.204, UV.y) * (1.0 - smoothstep(0.304, 0.311, UV.y));
    COLOR = mix(base, texture(blink_texture, uv), eyes * blink);
    COLOR.a *= opacity;
}
