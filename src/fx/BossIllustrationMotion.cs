using Godot;
using System.Collections.Generic;

internal sealed class BossIllustrationMotion
{
    internal const string ShaderCode = """
        shader_type canvas_item;
        uniform sampler2D next_frame : source_color, filter_linear, repeat_disable;
        uniform float blend_amount = 0.0;
        uniform vec4 next_frame_uv = vec4(1.0, 1.0, 0.0, 0.0);
        uniform vec4 frame_rect = vec4(-0.06, -0.06, 1.12, 1.12);
        uniform vec4 art_bounds = vec4(0.0, 0.0, 1.0, 1.0);
        uniform float facing = 1.0;
        uniform float head_turn = 0.0;
        uniform float breath = 0.0;
        uniform float hair_sway = 0.0;
        uniform float hem_sway = 0.0;
        uniform float gesture = 0.0;
        uniform float motion_phase = 0.0;
        uniform float wing_flap = 0.0;
        uniform vec2 display_size = vec2(72.0);
        instance uniform bool readability_pass = false;
        instance uniform vec2 frame_offset = vec2(0.0);
        instance uniform vec4 portrait_clip = vec4(0.0);
        varying vec4 tint;
        varying vec2 world_position;
        void vertex() {
            tint = COLOR;
            VERTEX += (frame_rect.xy + UV * frame_rect.zw - UV) * vec2(facing, 1.0) / TEXTURE_PIXEL_SIZE;
            VERTEX += frame_offset;
            world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
        }
        void fragment() {
            if (portrait_clip.z > portrait_clip.x &&
                (world_position.x < portrait_clip.x || world_position.x > portrait_clip.z ||
                 world_position.y < portrait_clip.y || world_position.y > portrait_clip.w)) discard;
            vec2 uv = frame_rect.xy + UV * frame_rect.zw;
            vec2 original_uv = uv;
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
            vec2 displacement = (uv - original_uv) * display_size;
            displacement *= 0.65 / sqrt(0.65 * 0.65 + dot(displacement, displacement));
            uv = original_uv + displacement / display_size;
            float wings = max(1.0 - smoothstep(0.22, 0.34, original_uv.x),
                smoothstep(0.56, 0.68, original_uv.x));
            wings *= 1.0 - smoothstep(0.56, 0.66, original_uv.y);
            uv.y -= wing_flap * 0.045 * wings;
            vec4 first = texture(TEXTURE, uv);
            vec2 next_uv = uv * next_frame_uv.xy + next_frame_uv.zw;
            vec4 second = texture(next_frame, next_uv);
            first.a *= step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            second.a *= step(0.0, next_uv.x) * step(next_uv.x, 1.0) * step(0.0, next_uv.y) * step(next_uv.y, 1.0);
            float alpha = mix(first.a, second.a, blend_amount);
            vec3 rgb = mix(first.rgb * first.a, second.rgb * second.a, blend_amount);
            COLOR = vec4(rgb / max(alpha, 0.0001), alpha) * tint;
            if (readability_pass) {
                vec2 pixel = 0.9 / max(display_size, vec2(1.0));
                float outside = alpha;
                float inside = alpha;
                for (int y = -1; y <= 1; y++) {
                    for (int x = -1; x <= 1; x++) {
                        vec2 nearby_uv = uv + vec2(float(x), float(y)) * pixel;
                        vec2 nearby_next_uv = nearby_uv * next_frame_uv.xy + next_frame_uv.zw;
                        float nearby_first = texture(TEXTURE, nearby_uv).a
                            * step(0.0, nearby_uv.x) * step(nearby_uv.x, 1.0)
                            * step(0.0, nearby_uv.y) * step(nearby_uv.y, 1.0);
                        float nearby_second = texture(next_frame, nearby_next_uv).a
                            * step(0.0, nearby_next_uv.x) * step(nearby_next_uv.x, 1.0)
                            * step(0.0, nearby_next_uv.y) * step(nearby_next_uv.y, 1.0);
                        float nearby = mix(nearby_first, nearby_second, blend_amount);
                        outside = max(outside, nearby);
                        inside = min(inside, nearby);
                    }
                }
                float outer_edge = max(0.0, outside - alpha);
                float inner_edge = max(0.0, alpha - inside);
                float edge_alpha = max(outer_edge, inner_edge) * 0.75;
                float fill_alpha = alpha * 0.18;
                vec3 edge_ink = mix(vec3(0.035, 0.045, 0.07), vec3(0.93, 0.97, 1.0),
                    step(outer_edge, inner_edge));
                COLOR.rgb = mix(COLOR.rgb, edge_ink, edge_alpha / max(fill_alpha + edge_alpha, 0.0001));
                COLOR.a = max(fill_alpha, edge_alpha) * tint.a;
            }
        }
        """;

    private static readonly Dictionary<string, Vector4> Bounds = new();
    private readonly UnfolderKind _character;
    private readonly ShaderMaterial _material = new() { Shader = new Shader { Code = ShaderCode } };
    private double _time;
    private float _release = 10;
    private const float ReleaseDuration = .72f;
    private float _wind;
    private float _charge;
    private BossCharacterTechnique? _technique;
    private int _shots, _hazardFrames;

    internal BossIllustrationMotion(UnfolderKind character) => _character = character;

    internal static void AttachReadability(Sprite2D body)
        => body.AddChild(new BossReadabilitySprite { Name = "BossReadability", Source = body });

    internal static void FitToFrame(Sprite2D body, Enemy boss)
    {
        ((ShaderMaterial)body.Material).SetShaderParameter("facing", body.FlipH ? -1f : 1f);
        var offset = Vector2.Zero;
        var clip = Vector4.Zero;
        if (Field.BossFrameActive && !boss.BossInArena && !boss.BossFrameReturning)
        {
            var frame = Field.BossFrameRect;
            var room = frame.Grow(-2);
            var face = ReadFaceClearance(body, false);
            var half = (face.Size / 2).Min(room.Size / 2);
            var center = face.GetCenter();
            var worldShift = new Vector2(
                Mathf.Clamp(center.X, room.Position.X + half.X, room.End.X - half.X) - center.X,
                Mathf.Clamp(center.Y, room.Position.Y + half.Y, room.End.Y - half.Y) - center.Y);
            offset = body.ToLocal(body.GlobalPosition + worldShift);
            clip = new Vector4(frame.Position.X, frame.Position.Y, frame.End.X, frame.End.Y);
        }
        body.SetInstanceShaderParameter("frame_offset", offset);
        body.SetInstanceShaderParameter("portrait_clip", clip);
    }

    internal static Vector2 FitShieldToFrame(Enemy boss, Vector2 size)
        => (Field.BossFrameActive || boss.BossFrameReturning) && !boss.BossInArena
            ? new Vector2(Mathf.Min(size.X, Field.BossFrameWidth - 10), Mathf.Min(size.Y, Field.Height - 16))
            : size;

    internal static Rect2 FaceClearance(Enemy boss)
    {
        var body = boss.GetNode<Sprite2D>("Body");
        body.GetNode<BossReadabilitySprite>("BossReadability").Sync();
        var clearance = FaceClearance(body);
        if (boss.FadingBody is { } fading && fading.IsVisibleInTree() && fading.SelfModulate.A > 0)
            clearance = clearance.Merge(FaceClearance(fading));
        return clearance;
    }

    private static Rect2 FaceClearance(Sprite2D body)
        => ReadFaceClearance(body, true);

    private static Rect2 ReadFaceClearance(Sprite2D body, bool applyOffset)
    {
        var face = FaceUv(body.Texture);
        var material = body.Material as ShaderMaterial;
        var next = material?.GetShaderParameter("next_frame").AsGodotObject() as Texture2D;
        if (next != null && (float)material!.GetShaderParameter("blend_amount") > .001f)
        {
            var mapping = (Vector4)material.GetShaderParameter("next_frame_uv");
            var ratio = new Vector2(mapping.X, mapping.Y);
            var nextFace = FaceUv(next);
            face = face.Merge(new Rect2((nextFace.Position - new Vector2(mapping.Z, mapping.W)) / ratio,
                nextFace.Size / ratio));
        }
        var offset = applyOffset && material != null
            ? (Vector2)body.GetInstanceShaderParameter("frame_offset") : Vector2.Zero;
        var quad = body.GetRect();
        var minimum = Vector2.Inf;
        var maximum = -Vector2.Inf;
        foreach (var corner in new[] { Vector2.Zero, Vector2.Right, Vector2.Down, Vector2.One })
        {
            var uv = face.Position + corner * face.Size;
            if (body.FlipH) uv.X = 1 - uv.X;
            if (body.FlipV) uv.Y = 1 - uv.Y;
            var vertex = quad.Position + uv * quad.Size + offset;
            var world = body.GlobalTransform * vertex;
            minimum = minimum.Min(world);
            maximum = maximum.Max(world);
        }
        // The shader's secondary motion reaches 0.65px beyond the registered pose.
        return new Rect2(minimum, maximum - minimum).Grow(2.5f);
    }

    private static Rect2 FaceUv(Texture2D texture)
    {
        if (texture.ResourcePath.Contains("/mina_dragon/"))
            return new Rect2(.25f, .25f, .32f, .43f);
        var art = ArtBounds(texture);
        var region = BossAnimalArt.Contains(texture)
            ? new Rect2(0, .10f, .56f, .66f)
            : new Rect2(.16f, .02f, .72f, .48f);
        return new Rect2(new Vector2(art.X, art.Y) + region.Position * new Vector2(art.Z, art.W),
            region.Size * new Vector2(art.Z, art.W));
    }

    private static Vector4 ArtBounds(Texture2D texture)
    {
        if (Bounds.TryGetValue(texture.ResourcePath, out var bounds)) return bounds;
        using var image = texture.GetImage();
        var rect = image.GetUsedRect();
        var size = texture.GetSize();
        bounds = new Vector4(rect.Position.X / size.X, rect.Position.Y / size.Y,
            rect.Size.X / size.X, rect.Size.Y / size.Y);
        Bounds.Add(texture.ResourcePath, bounds);
        return bounds;
    }

    internal void Attack()
    {
        // Finish the gesture before another volley can restart it.
        if (_release >= ReleaseDuration) _release = 0;
    }

    internal void Clear(Sprite2D body)
    {
        if (body.Material == _material) body.Material = null;
    }

    internal void Tick(Sprite2D body, double delta, bool animal, bool dragon, BossCharacterTechnique? technique, float wingPhase = 0)
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
        var bounds = ArtBounds(body.Texture);
        float t = (float)_time;
        float release = _release < .24f
            ? Ease(_release / .24f)
            : 1 - Ease((_release - .24f) / (ReleaseDuration - .24f));
        float head, breath, hair, hem, gesture, phase;
        switch (_character)
        {
            case UnfolderKind.Akari:
                phase = t * 2.1f;
                head = .012f * Mathf.Sin(t * .9f) - release * .012f + _charge * .01f;
                breath = .004f * Mathf.Sin(t * 1.8f);
                hair = .006f + Mathf.Abs(_wind) * .02f;
                hem = .006f * Mathf.Sin(phase - .9f) - _wind * .02f;
                gesture = .003f * Mathf.Sin(t * 1.8f) + release * .009f - _charge * .005f;
                break;
            case UnfolderKind.Koharu:
                phase = t * 2.6f;
                head = .016f * Mathf.Sin(t * 1.5f) + release * .016f - _charge * .012f;
                breath = .005f * Mathf.Sin(t * 2.3f);
                hair = .008f + Mathf.Abs(_wind) * .025f;
                hem = .008f * Mathf.Sin(phase - 1.2f) - _wind * .025f;
                gesture = .005f * Mathf.Sin(t * 1.5f + .6f) + release * .012f + _charge * .008f;
                break;
            case UnfolderKind.Rei:
                phase = t * 2.8f;
                head = .006f * Mathf.Sin(t * 1.2f);
                breath = .003f * Mathf.Sin(t * 2.1f);
                hair = .005f + Mathf.Abs(_wind) * .02f;
                hem = .005f * Mathf.Sin(phase - .7f) - _wind * .02f;
                gesture = release * .008f;
                break;
            default:
                phase = t * 1.7f;
                head = .009f * Mathf.Sin(t * .8f) - release * .01f + _charge * .008f;
                breath = .004f * Mathf.Sin(t * 1.7f);
                hair = .007f + Mathf.Abs(_wind) * .02f;
                hem = .006f * Mathf.Sin(phase - 1.3f) - _wind * .02f;
                gesture = .002f * Mathf.Sin(t * 1.7f - .4f) + release * .006f - _charge * .004f;
                break;
        }
        // Drawn animal poses already articulate the limbs; retain only a little secondary motion.
        float amount = animal || dragon ? .28f : 1;
        material.SetShaderParameter("art_bounds", bounds);
        material.SetShaderParameter("display_size", body.Texture.GetSize() * body.Scale.Abs());
        material.SetShaderParameter("facing", body.FlipH ? -1f : 1f);
        material.SetShaderParameter("head_turn", head * amount);
        material.SetShaderParameter("breath", breath * amount);
        material.SetShaderParameter("hair_sway", hair * amount);
        material.SetShaderParameter("hem_sway", hem * amount);
        material.SetShaderParameter("gesture", gesture * amount);
        material.SetShaderParameter("motion_phase", phase);
        material.SetShaderParameter("wing_flap", dragon && body.Texture.ResourcePath.EndsWith("mina_dragon_flap_level_v1.png")
            ? Mathf.Sin(wingPhase) : 0f);
    }

    private static float Ease(float value)
    {
        float t = Mathf.Clamp(value, 0, 1);
        return t * t * t * (t * (t * 6 - 15) + 10);
    }
}

internal partial class BossReadabilitySprite : Sprite2D
{
    internal Sprite2D Source = null!;
    private Enemy _boss = null!;
    private ShaderMaterial _stillMaterial = null!;

    public override void _Ready()
    {
        ZIndex = 4;
        ZAsRelative = false;
        TextureFilter = Source.TextureFilter;
        _boss = (Enemy)Source.GetParent();
        _stillMaterial = new ShaderMaterial { Shader = new Shader { Code = BossIllustrationMotion.ShaderCode } };
        _stillMaterial.SetShaderParameter("frame_rect", new Vector4(-.06f, -.06f, 1.12f, 1.12f));
        SetInstanceShaderParameter("readability_pass", true);
        Sync();
    }

    public override void _Process(double delta) => Sync();

    internal void Sync()
    {
        if (_boss.IsPurified)
        {
            Visible = false;
            return;
        }
        Texture = Source.Texture;
        Offset = Source.Offset;
        FlipH = Source.FlipH;
        FlipV = Source.FlipV;
        SelfModulate = Source.SelfModulate;
        if (Source.Material == null)
        {
            _stillMaterial.SetShaderParameter("display_size", Texture.GetSize() * GlobalScale.Abs());
            _stillMaterial.SetShaderParameter("facing", FlipH ? -1f : 1f);
            Source.Material = _stillMaterial;
        }
        Material = Source.Material;
        BossIllustrationMotion.FitToFrame(Source, _boss);
        if (_boss.FadingBody is { Material: ShaderMaterial } fading)
            BossIllustrationMotion.FitToFrame(fading, _boss);
        Visible = _boss.BossInArena;
    }
}
