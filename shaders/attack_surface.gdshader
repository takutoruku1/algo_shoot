shader_type canvas_item;
render_mode unshaded;

uniform vec4 field_bounds;
uniform vec3 safe_disc;
uniform vec4 fan;
varying vec2 world_position;
varying vec4 ink;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    ink = COLOR;
}

void fragment() {
    float boundary = distance(world_position, safe_disc.xy) - safe_disc.z;
    if (any(lessThan(world_position, field_bounds.xy)) || any(greaterThan(world_position, field_bounds.zw))
            || (safe_disc.z > 0.0 && boundary < 0.0)) discard;
    vec2 sample_uv = UV;
    float spread = length(fan.zw);
    if (spread > 0.0) {
        vec2 axis = fan.zw / spread;
        vec2 offset = world_position - fan.xy;
        float width = max(1.0, dot(offset, axis) * spread);
        sample_uv = vec2(0.5, clamp(0.5 + 0.5 * dot(offset, vec2(-axis.y, axis.x)) / width, 0.005, 0.995));
    }
    vec4 baked = texture(TEXTURE, sample_uv);
    float relief = baked.r;
    if (safe_disc.z > 0.0) relief *= mix(0.35, 1.0, smoothstep(0.0, 3.0, boundary));
    vec3 face = mix(ink.rgb * (0.22 + relief * 0.74), vec3(1.0), smoothstep(0.64, 1.0, relief) * 0.58);
    COLOR = vec4(face, ink.a * baked.a);
}
