shader_type canvas_item;
render_mode unshaded;
uniform vec4 field_bounds;
varying vec2 world_position;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

void fragment() {
    if (world_position.x < field_bounds.x || world_position.y < field_bounds.y
        || world_position.x > field_bounds.z || world_position.y > field_bounds.w) discard;
}
