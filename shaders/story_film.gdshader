shader_type canvas_item;
render_mode unshaded;

uniform sampler2D scene_texture : source_color, filter_linear, repeat_disable;
uniform sampler2D previous_texture : source_color, filter_linear, repeat_disable;
uniform vec4 scene_region = vec4(0.0, 0.0, 1.0, 1.0);
uniform vec4 previous_region = vec4(0.0, 0.0, 1.0, 1.0);
uniform float blend_amount : hint_range(0.0, 1.0) = 1.0;
uniform float motion_time = 0.0;
uniform bool grayscale = true;
varying vec4 vertex_tint;

void vertex() {
    vertex_tint = COLOR;
}

vec2 frame_uv(vec2 uv, vec4 region) {
    float drift = sin(motion_time * 0.045);
    vec2 local = (uv - 0.5) * 0.94 + 0.5 + vec2(drift * 0.016, 0.006);
    return region.xy + local * region.zw;
}

void fragment() {
    vec3 from = texture(previous_texture, frame_uv(UV, previous_region)).rgb;
    vec3 to = texture(scene_texture, frame_uv(UV, scene_region)).rgb;
    vec3 rgb = mix(from, to, smoothstep(0.0, 1.0, blend_amount));
    if (grayscale) {
        float luma = dot(rgb, vec3(0.2126, 0.7152, 0.0722));
        rgb = vec3(luma);
    }
    COLOR = vec4(rgb, 1.0) * vertex_tint;
}
