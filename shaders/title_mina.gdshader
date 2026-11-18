shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform sampler2D original_art : source_color, filter_linear_mipmap;
uniform sampler2D cutout : source_color, filter_linear_mipmap;
uniform sampler2D expression_art : source_color, filter_linear_mipmap;
uniform sampler2D half_blink_art : source_color, filter_linear_mipmap;
uniform float blink : hint_range(0.0, 1.0) = 0.0;
uniform float smile : hint_range(0.0, 1.0) = 0.0;

float eye_mask(vec2 pixel, vec2 center, vec2 radius) {
    vec2 delta = pixel - center;
    mat2 axes = mat2(vec2(0.9487, 0.3162), vec2(-0.3162, 0.9487));
    vec2 local = axes * delta;
    return 1.0 - smoothstep(0.76, 1.08, length(local / radius));
}

void fragment() {
    vec2 pixel = UV * vec2(1672.0, 941.0);
    vec3 color = texture(original_art, UV).rgb;
    vec3 expression = texture(expression_art, UV).rgb;
    vec3 closing = mix(color, texture(half_blink_art, UV).rgb, smoothstep(0.0, 0.5, blink));
    closing = mix(closing, expression, smoothstep(0.5, 1.0, blink));
    float eyes = max(eye_mask(pixel, vec2(1084.0, 255.0), vec2(47.0, 29.0)),
        eye_mask(pixel, vec2(1187.0, 221.0), vec2(37.0, 29.0)));
    color = mix(color, closing, eyes);
    float mouth = 1.0 - smoothstep(0.65, 1.0, length((pixel - vec2(1164.0, 297.0)) / vec2(23.0, 13.0)));
    color = mix(color, expression, mouth * smile);
    ALBEDO = color;
    ALPHA = smoothstep(0.035, 0.9, texture(cutout, UV).a);
}
