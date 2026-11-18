shader_type canvas_item;
render_mode unshaded;

uniform vec2 field_origin;
uniform vec2 cell_size;
uniform float elapsed = 0.0;
varying vec2 field_position;

void vertex() {
    field_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy - field_origin;
}

float cut_box(vec2 point, vec2 half_size, float cut) {
    vec2 edge = abs(point) - half_size;
    return max(max(edge.x, edge.y), (edge.x + edge.y + cut) * 0.7071);
}

void fragment() {
    vec2 uv = mod(field_position, cell_size) / cell_size;
    vec2 point = (uv - 0.5) * cell_size;
    float edge = cut_box(point, cell_size * 0.5 - vec2(0.7), 2.0);
    float face = 1.0 - smoothstep(-3.6, -3.0, edge);
    vec2 curved = (uv - 0.5) * vec2(1.2, 0.8);
    vec3 normal = normalize(vec3(curved, 1.0));
    vec3 light = normalize(vec3(-0.55, -0.7, 0.8));
    float diffuse = max(dot(normal, light), 0.0);
    float specular = pow(max(dot(reflect(-light, normal), vec3(0.0, 0.0, 1.0)), 0.0), 24.0);
    float depth = clamp(1.0 - length((uv - 0.5) * vec2(1.5, 1.1)), 0.0, 1.0);
    vec3 glass = mix(vec3(0.09, 0.025, 0.17), vec3(0.38, 0.12, 0.49), depth);
    glass *= 0.65 + diffuse * 0.65;
    float facet = smoothstep(-0.012, 0.012, uv.x * 0.72 + uv.y * 0.46 - 0.58);
    glass += vec3(0.055, 0.025, 0.08) * facet;
    float reflection = exp(-pow((uv.x + uv.y * 0.48 - 0.37 - sin(elapsed * 0.45) * 0.018) / 0.085, 2.0));
    glass += vec3(0.38, 0.24, 0.46) * specular + vec3(0.18, 0.11, 0.23) * reflection;
    float edge_light = clamp(0.5 - dot(normalize(point + vec2(0.001)), normalize(vec2(0.7, 1.0))) * 0.5, 0.0, 1.0);
    vec3 metal = mix(vec3(0.12, 0.035, 0.19), vec3(0.72, 0.39, 0.67), edge_light);
    float bevel = smoothstep(-3.1, -0.8, edge);
    metal *= 0.55 + bevel * 0.55;
    vec3 color = mix(metal, glass, face);
    float inner_rim = exp(-pow((edge + 3.25) / 0.18, 2.0));
    color += vec3(0.45, 0.22, 0.54) * inner_rim * (0.25 + edge_light * 0.75);
    float outer_rim = exp(-pow((edge + 0.65) / 0.20, 2.0));
    color = mix(color, vec3(1.0, 0.78, 0.91), outer_rim * (0.25 + edge_light * 0.7));
    color = mix(vec3(0.055, 0.02, 0.085), color, 1.0 - smoothstep(-0.2, 0.15, edge));
    COLOR = vec4(color, 0.96);
}
