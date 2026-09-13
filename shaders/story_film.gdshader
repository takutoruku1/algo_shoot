shader_type canvas_item;
render_mode unshaded;

uniform int scene_index = 0;
uniform int previous_index = 0;
uniform float blend_amount : hint_range(0.0, 1.0) = 1.0;
uniform float motion_time = 0.0;
uniform bool grayscale = true;
uniform int atlas_rows = 3;
varying vec4 vertex_tint;

void vertex() {
    vertex_tint = COLOR;
}

vec2 frame_uv(vec2 uv, int frame) {
    float drift = sin(motion_time * 0.045);
    vec2 local = (uv - 0.5) * 0.94 + 0.5 + vec2(drift * 0.016, 0.006);
    return (local + vec2(float(frame % 2), float(frame / 2))) / vec2(2.0, float(atlas_rows));
}

void fragment() {
    vec3 from = texture(TEXTURE, frame_uv(UV, previous_index)).rgb;
    vec3 to = texture(TEXTURE, frame_uv(UV, scene_index)).rgb;
    vec3 rgb = mix(from, to, smoothstep(0.0, 1.0, blend_amount));
    if (grayscale) {
        float luma = dot(rgb, vec3(0.2126, 0.7152, 0.0722));
        rgb = vec3(luma);
    }
    COLOR = vec4(rgb, 1.0) * vertex_tint;
}
