shader_type canvas_item;

uniform float time_sec = 0.0;
uniform float ambient_time_sec = 0.0;
uniform vec2 depth_offset = vec2(0.0);
uniform sampler2D clean_plate : source_color, filter_linear;

float edge_distance(vec2 p, vec2 a, vec2 b) {
    vec2 edge = b - a;
    return (edge.x * (p.y - a.y) - edge.y * (p.x - a.x)) / length(edge);
}

float post_mask(vec2 uv, vec2 a, vec2 b, vec2 c, vec2 d) {
    float edge = min(min(edge_distance(uv, a, b), edge_distance(uv, b, c)),
        min(edge_distance(uv, c, d), edge_distance(uv, d, a)));
    // The replacement plate is used only behind the cutouts, preserving the original Mina pixels.
    return smoothstep(-0.024, -0.008, edge);
}

vec2 timeline_offset(vec2 uv) {
    float t = ambient_time_sec;
    float background = smoothstep(0.04, 0.12, uv.x) * (1.0 - smoothstep(0.45, 0.515, uv.x))
        * smoothstep(0.0, 0.09, uv.y) * (1.0 - smoothstep(0.89, 1.0, uv.y));
    return (vec2(sin(t * 0.19) * 0.002, sin(t * 0.23) * 0.003)
        + depth_offset * 0.65) * background;
}

float line_distance(vec2 p, vec2 a, vec2 b) {
    vec2 edge = b - a;
    return length(p - a - edge * clamp(dot(p - a, edge) / dot(edge, edge), 0.0, 1.0));
}

float cleared_posts(vec2 uv) {
    float photo = post_mask(uv, vec2(0.304, 0.056), vec2(0.436, 0.149),
        vec2(0.436, 0.431), vec2(0.302, 0.372));
    float draft = post_mask(uv, vec2(0.274, 0.413), vec2(0.455, 0.484),
        vec2(0.455, 0.641), vec2(0.272, 0.579));
    float thread = min(line_distance(uv, vec2(0.435, 0.603), vec2(0.462, 0.629)),
        line_distance(uv, vec2(0.462, 0.629), vec2(0.491, 0.632)));
    thread = min(thread, line_distance(uv, vec2(0.491, 0.632), vec2(0.522, 0.659)));
    return max(max(photo, draft), 1.0 - smoothstep(0.01, 0.022, thread));
}

vec3 message_light(vec2 uv, vec3 color) {
    vec2 jewel = (uv - vec2(0.706, 0.439)) / vec2(0.009, 0.015);
    float glint = pow(max(0.0, sin(ambient_time_sec * 0.72 - 0.8)), 12.0);
    color += vec3(0.11, 0.2, 0.25) * exp(-dot(jewel, jewel) * 2.0) * glint;
    return color;
}

// Masks follow Mina's silhouette: wind starts beyond the face and both hands.
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
    vec2 uv = clamp(UV + timeline_offset(UV) + wind_offset(UV), vec2(0.0), vec2(1.0));
    vec4 ink = texture(TEXTURE, uv);
    ink = mix(ink, texture(clean_plate, uv), cleared_posts(uv));
    COLOR = vec4(message_light(uv, ink.rgb), ink.a);
}
