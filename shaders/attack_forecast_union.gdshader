shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform vec4 ink : source_color = vec4(0.953, 0.592, 0.675, 0.90);
uniform vec4 outline : source_color = vec4(0.09, 0.06, 0.12, 1.0);
uniform float fill_opacity = 0.085;
uniform float hatch_opacity = 0.0;
uniform bool danger_hatch = false;
uniform vec4 field_bounds = vec4(0.0, 0.0, 384.0, 216.0);

void fragment() {
    vec2 step_uv = vec2(1.0 / 384.0, 1.0 / 216.0);
    float center = textureLod(screen_texture, SCREEN_UV, 0.0).a;
    float left = textureLod(screen_texture, SCREEN_UV - vec2(step_uv.x, 0.0), 0.0).a;
    float right = textureLod(screen_texture, SCREEN_UV + vec2(step_uv.x, 0.0), 0.0).a;
    float top = textureLod(screen_texture, SCREEN_UV - vec2(0.0, step_uv.y), 0.0).a;
    float bottom = textureLod(screen_texture, SCREEN_UV + vec2(0.0, step_uv.y), 0.0).a;
    float inside_edge = center * (1.0 - min(min(left, right), min(top, bottom)));
    float outside_edge = (1.0 - center) * max(max(left, right), max(top, bottom));
    vec2 point = SCREEN_UV * vec2(384.0, 216.0);
    float hatch = 1.0 - step(1.0, mod(point.x + point.y, 7.0));
    COLOR = vec4(mix(outline.rgb, ink.rgb, center),
        ink.a * (outside_edge * 0.40 + center * (fill_opacity + hatch * hatch_opacity + inside_edge * 0.72)));
    if (danger_hatch) {
        float stripe = 1.0 - step(2.0, mod(point.x + point.y, 7.0));
        float edge = inside_edge * (1.0 - step(3.0, mod(point.x - point.y, 6.0)));
        float mark = max(stripe, edge);
        COLOR = vec4(mix(outline.rgb, ink.rgb, mark), center * ink.a * mix(fill_opacity, hatch_opacity, mark));
    }
    if (point.x < field_bounds.x || point.y < field_bounds.y || point.x > field_bounds.z || point.y > field_bounds.w)
        COLOR.a = 0.0;
}
