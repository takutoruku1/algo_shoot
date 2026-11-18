shader_type canvas_item;
render_mode unshaded;

uniform vec4 field_bounds;
uniform sampler2D rail_surface : source_color, filter_linear, repeat_disable;
uniform vec3 end_disc;
uniform sampler2D motif : source_color, filter_linear, repeat_disable;
uniform float motif_strength = 0.0;
uniform bool ribbon = false;
uniform float attack_time = 0.0;
varying vec2 world_position;
varying vec4 ink;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    ink = COLOR;
}

void fragment() {
    if (world_position.x < field_bounds.x || world_position.y < field_bounds.y
        || world_position.x > field_bounds.z || world_position.y > field_bounds.w) {
        discard;
    }
    if (end_disc.z > 0.0 && distance(world_position, end_disc.xy) < end_disc.z) {
        discard;
    }
    float across = UV.y * 2.0 - 1.0;
    float roundness = sqrt(max(0.0, 1.0 - across * across));
    float light = clamp(roundness * 0.86 - across * 0.42, 0.0, 1.0);
    vec3 body = ink.rgb * (0.30 + light * 0.66);
    float shine = exp(-pow((across + 0.24) / 0.28, 2.0));
    body = mix(body, mix(ink.rgb, vec3(1.0, 0.97, 0.96), 0.76), shine * 0.66);
    float rim = smoothstep(0.84, 0.96, abs(across));
    body = mix(body, ink.rgb * 0.32, rim * 0.72);
    vec4 pattern = texture(motif, UV);
    body = mix(body, body * 0.55 + pattern.rgb * 0.45, pattern.a * motif_strength);
    if (ribbon) {
        float bevel = smoothstep(0.68, 0.9, abs(across));
        body = ink.rgb * (0.58 + roundness * 0.28 - across * 0.1);
        body = mix(body, ink.rgb * 0.24, bevel * 0.7);
        float edge_light = exp(-pow((across + 0.73) / 0.055, 2.0));
        body = mix(body, mix(ink.rgb, vec3(1.0, 0.95, 0.97), 0.72), edge_light * 0.86);
        body = mix(body, body * 0.8 + pattern.rgb * 0.2, pattern.a * motif_strength);
    }
    float rail = texture(rail_surface, vec2(0.5, clamp(UV.y, 0.005, 0.995))).r;
    body = mix(body, ink.rgb * (0.26 + rail * 0.94), 0.55);
    float flow = pow(max(0.0, sin(UV.x * 28.0 - attack_time * 12.0)), 12.0);
    float filament = exp(-pow(across / 0.23, 2.0));
    body = mix(body, mix(ink.rgb, vec3(1.0), 0.85), flow * filament * 0.58);
    COLOR = vec4(body, ink.a);
}
