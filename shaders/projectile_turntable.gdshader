shader_type canvas_item;
render_mode unshaded;

uniform vec2 rim_width = vec2(0.45, 0.85);
uniform vec3 rim_light = vec3(1.0, 0.97, 0.87);
uniform float shadow_floor = 0.28;
uniform float shadow_lift = 0.0;

varying vec2 world_position;
varying vec4 tint;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    tint = COLOR;
}

void fragment() {
    vec4 art = texture(TEXTURE, UV);
    float unit = max(length(dFdx(world_position)), 0.0001);
    vec2 step_uv = vec2(length(vec2(dFdx(UV.x), dFdy(UV.x))), length(vec2(dFdx(UV.y), dFdy(UV.y)))) / unit;
    vec2 frame_min = floor(UV * vec2(8.0, 6.0)) / vec2(8.0, 6.0) + TEXTURE_PIXEL_SIZE * 0.5;
    vec2 frame_max = frame_min + 1.0 / vec2(8.0, 6.0) - TEXTURE_PIXEL_SIZE;
    float inner = 0.0;
    float outer = 0.0;
    for (int i = 0; i < 8; i++) {
        float angle = float(i) * 0.7853981634;
        vec2 offset = vec2(cos(angle), sin(angle)) * step_uv;
        inner = max(inner, texture(TEXTURE, clamp(UV + offset * rim_width.x, frame_min, frame_max)).a);
        outer = max(outer, texture(TEXTURE, clamp(UV + offset * rim_width.y, frame_min, frame_max)).a);
    }
    float luminance = dot(art.rgb, vec3(0.2126, 0.7152, 0.0722));
    vec3 face = art.rgb + max(0.0, shadow_floor - luminance);
    face = mix(face, vec3(1.0), shadow_lift);
    vec3 rim = mix(vec3(0.025, 0.018, 0.045), rim_light, smoothstep(0.12, 0.6, inner));
    float rim_alpha = smoothstep(0.12, 0.6, max(inner, outer));
    float opacity = art.a + rim_alpha * (1.0 - art.a);
    COLOR = vec4((face * art.a + rim * rim_alpha * (1.0 - art.a)) / max(opacity, 0.0001), opacity) * tint;
}
