shader_type canvas_item;
render_mode unshaded;

uniform sampler2D memory_art : source_color, filter_linear;
uniform vec2 frame_grid = vec2(1.0);
uniform int model_frame = 0;

void fragment() {
    if (UV.x >= 0.0) {
        vec2 cell = vec2(mod(float(model_frame), frame_grid.x), floor(float(model_frame) / frame_grid.x));
        COLOR *= texture(memory_art, (cell + UV) / frame_grid);
    }
}
