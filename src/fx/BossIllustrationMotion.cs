using Godot;
using System.Collections.Generic;

internal sealed class BossIllustrationMotion
{
    internal const string ShaderCode = """
        shader_type canvas_item;
        uniform sampler2D next_frame : source_color, filter_linear, repeat_disable;
        uniform float blend_amount = 0.0;
        uniform vec4 art_bounds = vec4(0.0, 0.0, 1.0, 1.0);
        uniform float facing = 1.0;
        uniform float head_turn = 0.0;
        uniform float breath = 0.0;
        uniform float hair_sway = 0.0;
        uniform float hem_sway = 0.0;
        uniform float gesture = 0.0;
        uniform float motion_phase = 0.0;
        varying vec4 tint;
        void vertex() {
            tint = COLOR;
            VERTEX += (UV - 0.5) * vec2(facing, 1.0) / TEXTURE_PIXEL_SIZE * 0.12;
        }
        void fragment() {
            vec2 uv = (UV - 0.5) * 1.12 + 0.5;
            vec2 p = (uv - art_bounds.xy) / art_bounds.zw;
            float head = 1.0 - smoothstep(0.42, 0.65, p.y);
            float turn = head_turn * head;
            vec2 neck = p - vec2(0.5, 0.53);
            p = vec2(cos(turn)*neck.x + sin(turn)*neck.y,
                    -sin(turn)*neck.x + cos(turn)*neck.y) + vec2(0.5, 0.53);
            p.y = 0.82 + (p.y - 0.82) / (1.0 + breath * (1.0 - smoothstep(0.55, 0.9, p.y)));
            float side = smoothstep(0.17, 0.35, abs(p.x - 0.5));
            float hair = side * smoothstep(0.22, 0.4, p.y) * (1.0 - smoothstep(0.8, 1.0, p.y));
            p.x -= hair * hair_sway * sin(motion_phase - p.y * 5.0);
            float hem = smoothstep(0.56, 0.75, p.y) * (1.0 - smoothstep(0.88, 1.0, p.y));
            p.x -= hem * hem_sway;
            float arms = side * smoothstep(0.35, 0.48, p.y) * (1.0 - smoothstep(0.64, 0.8, p.y));
            p.y -= arms * gesture;
            uv = art_bounds.xy + p * art_bounds.zw;
            vec4 first = texture(TEXTURE, uv);
            vec4 second = texture(next_frame, uv);
            float alpha = mix(first.a, second.a, blend_amount);
            vec3 rgb = mix(first.rgb * first.a, second.rgb * second.a, blend_amount);
            float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            COLOR = vec4(rgb / max(alpha, 0.0001), alpha * inside) * tint;
        }
        """;

    private static readonly Dictionary<string, Vector4> Bounds = new();
    private readonly UnfolderKind _character;
    private readonly ShaderMaterial _material = new() { Shader = new Shader { Code = ShaderCode } };
    private double _time;
    private float _release = 10;
    private float _wind;
    private float _charge;
    private BossCharacterTechnique? _technique;
    private int _shots, _hazardFrames;

    internal BossIllustrationMotion(UnfolderKind character) => _character = character;

    internal void Attack()
    {
        if (_release >= .22f) _release = 0;
    }

    internal void Clear(Sprite2D body)
    {
        if (body.Material == _material) body.Material = null;
    }

    internal void Tick(Sprite2D body, double delta, bool animal, bool dragon, BossCharacterTechnique? technique)
    {
        _time += delta;
        _release += (float)delta;
        if (_technique != technique)
        {
            _technique = technique;
            _shots = _hazardFrames = 0;
        }
        bool preparing = false;
        if (technique is { Finished: false })
        {
            if (technique.ShotsCreated > _shots || (_hazardFrames == 0 && technique.HazardFrames > 0)) Attack();
            _shots = technique.ShotsCreated;
            _hazardFrames = technique.HazardFrames;
            preparing = _shots == 0 && _hazardFrames == 0;
        }
        _charge = Mathf.Lerp(_charge, preparing ? 1 : 0, 1 - Mathf.Exp(-(float)delta * 5));
        float lean = body.Rotation * (body.FlipH ? -1 : 1);
        _wind = Mathf.Lerp(_wind, lean, 1 - Mathf.Exp(-(float)delta * 4));
        var material = body.Material as ShaderMaterial ?? _material;
        body.Material = material;
        if (!Bounds.TryGetValue(body.Texture.ResourcePath, out var bounds))
        {
            using var image = body.Texture.GetImage();
            var rect = image.GetUsedRect();
            var size = body.Texture.GetSize();
            bounds = new Vector4(rect.Position.X / size.X, rect.Position.Y / size.Y,
                rect.Size.X / size.X, rect.Size.Y / size.Y);
            Bounds.Add(body.Texture.ResourcePath, bounds);
        }
        float t = (float)_time;
        float release = Mathf.Sin(_release * 11) * Mathf.Exp(-_release * 4);
        float head, breath, hair, hem, gesture, phase;
        switch (_character)
        {
            case UnfolderKind.Akari:
                phase = t * 2.1f;
                head = .038f * Mathf.Sin(t * 1.25f) - release * .045f + _charge * .035f;
                breath = .012f * Mathf.Sin(t * 1.8f);
                hair = .017f + Mathf.Abs(_wind) * .035f;
                hem = .018f * Mathf.Sin(phase - .9f) - _wind * .055f;
                gesture = .009f * Mathf.Sin(t * 2.5f) + release * .025f - _charge * .015f;
                break;
            case UnfolderKind.Koharu:
                phase = t * 3.8f;
                head = .055f * Mathf.Sin(t * 2.3f) + release * .06f - _charge * .04f;
                breath = .018f * Mathf.Sin(t * 4.6f);
                hair = .026f + Mathf.Abs(_wind) * .05f;
                hem = .025f * Mathf.Sin(phase - 1.2f) - _wind * .08f;
                gesture = .018f * Mathf.Sin(t * 2.3f + .6f) + release * .045f + _charge * .025f;
                break;
            case UnfolderKind.Rei:
                phase = t * 2.8f;
                head = .018f * Mathf.Sin(t * 1.7f);
                breath = .008f * Mathf.Sin(t * 2.1f);
                hair = .014f + Mathf.Abs(_wind) * .04f;
                hem = .015f * Mathf.Sin(phase - .7f) - _wind * .04f;
                gesture = release * .025f;
                break;
            default:
                phase = t * 1.7f;
                head = .025f * Mathf.Sin(t * 1.05f) - release * .035f + _charge * .025f;
                breath = .014f * Mathf.Sin(t * 1.7f);
                hair = .022f + Mathf.Abs(_wind) * .045f;
                hem = .018f * Mathf.Sin(phase - 1.3f) - _wind * .06f;
                gesture = .006f * Mathf.Sin(t * 1.7f - .4f) + release * .018f - _charge * .012f;
                break;
        }
        // Drawn animal poses already articulate the limbs; retain only a little secondary motion.
        float amount = animal || dragon ? .28f : 1;
        material.SetShaderParameter("art_bounds", bounds);
        material.SetShaderParameter("facing", body.FlipH ? -1f : 1f);
        material.SetShaderParameter("head_turn", head * amount);
        material.SetShaderParameter("breath", breath * amount);
        material.SetShaderParameter("hair_sway", hair * amount);
        material.SetShaderParameter("hem_sway", hem * amount);
        material.SetShaderParameter("gesture", gesture * amount);
        material.SetShaderParameter("motion_phase", phase);
    }
}
