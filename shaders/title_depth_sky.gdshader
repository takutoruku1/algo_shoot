shader_type sky;

void sky() {
    float halo = pow(max(dot(EYEDIR, normalize(vec3(0.12, 0.05, -1.0))), 0.0), 32.0);
    float rim = pow(max(dot(EYEDIR, normalize(vec3(-0.7, 0.65, -0.3))), 0.0), 10.0);
    COLOR = mix(vec3(0.006, 0.010, 0.021), vec3(0.035, 0.085, 0.115), halo)
        + vec3(0.14, 0.19, 0.27) * rim;
}
