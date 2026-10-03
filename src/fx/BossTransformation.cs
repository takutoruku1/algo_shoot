using Godot;
using System;

public partial class BossTransformation : Node2D
{
    public readonly record struct Frame(Texture2D Texture, Vector2 Position, Vector2 Scale, Vector2 Offset, bool Flip)
    {
        public static Frame Capture(Sprite2D body)
            => new(body.Texture, body.GlobalPosition, body.Scale, body.Offset, body.FlipH);
        public Sprite2D Create() => new()
        {
            Texture = Texture, Position = Position, Scale = Scale, Offset = Offset,
            FlipH = Flip, TextureFilter = TextureFilterEnum.Linear,
        };
    }

    public const double Duration = 3.0;
    public const float ImpactTime = 1.28f;
    private Enemy _boss = null!;
    private Hud _hud = null!;
    private Node _world = null!;
    private GameManager _game = null!;
    private ProcessModeEnum _worldMode, _gameMode;
    private Frame _before, _after;
    private Sprite2D _old = null!, _new = null!;
    private readonly Sprite2D[] _echoes = new Sprite2D[2];
    private Node2D _front = null!;
    private Sprite2D? _crack;
    private ShaderMaterial _oldMaterial = null!, _newMaterial = null!;
    private Texture2D _motif = null!, _detail = null!;
    private Color _color, _secondary;
    private UnfolderKind _style;
    private Action? _completed;
    private double _time;
    private bool _restored, _visible, _suppressed, _revealed, _charged;
    private readonly Vector2[] _trail = new Vector2[25];
    private readonly Color[] _trailColors = new Color[25];

    public static BossTransformation Play(Enemy boss, Frame before, Action? completed = null)
    {
        var hud = (Hud)boss.GetTree().GetFirstNodeInGroup("hud");
        var effect = new BossTransformation
        {
            Name = "BossTransformation", ZIndex = 30, ProcessMode = ProcessModeEnum.Pausable,
            _boss = boss, _hud = hud, _world = boss.GetParent(), _before = before,
            _after = Frame.Capture(boss.GetNode<Sprite2D>("Body")), _completed = completed,
        };
        hud.AddChild(effect);
        return effect;
    }

    public override void _Ready()
    {
        AddToGroup("boss_transform");
        _game = GetNode<GameManager>("/root/Game");
        _worldMode = _world.ProcessMode;
        _gameMode = _game.ProcessMode;
        _world.ProcessMode = ProcessModeEnum.Disabled;
        _game.ProcessMode = ProcessModeEnum.Disabled;
        _visible = _boss.Visible;
        _boss.Visible = false;
        _suppressed = _hud.SuppressCallouts;
        _hud.SuppressCallouts = true;
        _hud.HideSpellCard();
        _hud.SetCinematicMode(true);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        _color = UnfolderMotion.ColorFor(_boss.UnfolderStyle);
        _style = _boss.UnfolderStyle;
        (_motif, _detail, _secondary) = _style switch
        {
            UnfolderKind.Akari => (Load("akari/card_unsent_1"), Load("akari/realm/fracture_branch_v1"), new Color("76d9ec")),
            UnfolderKind.Koharu => (Load("koharu/penlight_lit"), Load("koharu/gaze_ray"), new Color("c391f4")),
            UnfolderKind.Rei => (Load("rei/frame_star"), Load("rei/star_small"), new Color("ffba77")),
            _ => (GD.Load<Texture2D>(UnfolderMotion.TexturePath(_style)), Load("akari/realm/fracture_branch_v1"), new Color("7389e0")),
        };
        var shader = new Shader { Code = """
            shader_type canvas_item;
            render_mode unshaded;
            uniform vec4 light_color : source_color;
            uniform float light_mix;
            uniform float progress = 0.0;
            uniform bool incoming = false;
            uniform int style = 1;
            uniform vec2 pivot = vec2(0.5);
            float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
            void fragment() {
                float grain = hash(floor(UV * vec2(42.0, 56.0)));
                float cut = mix(1.0 - UV.y, grain, 0.3);
                if (style == 2) cut = mix(abs(UV.x - 0.5) * 2.0, grain, 0.25);
                if (style == 3) cut = hash(vec2(floor(UV.y * 48.0), 9.0));
                if (style == 4) cut = clamp(abs(UV.x - pivot.x) / max(pivot.x, 1.0 - pivot.x), 0.0, 1.0);
                float edge = progress * 1.24 - 0.12;
                float mask = smoothstep(cut - 0.055, cut + 0.055, edge);
                float rim = (1.0 - smoothstep(0.0, 0.09, abs(cut - edge))) * 0.35;
                COLOR.rgb = mix(COLOR.rgb, light_color.rgb, light_mix) + light_color.rgb * rim;
                COLOR.a *= incoming ? mask : 1.0 - mask;
            }
            """ };
        _oldMaterial = MaterialFor(shader, _before, false, _color);
        _newMaterial = MaterialFor(shader, _after, true, _color.Lerp(Colors.White, .7f));
        _old = _before.Create();
        _new = _after.Create();
        _old.Material = _oldMaterial;
        _new.Material = _newMaterial;
        _old.ZIndex = 2;
        _new.ZIndex = 3;
        _new.Visible = false;
        for (int i = 0; i < _echoes.Length; i++)
        {
            var echo = _after.Create();
            var material = MaterialFor(shader, _after, true, i == 0 ? _color : _secondary);
            material.SetShaderParameter("progress", 1f);
            material.SetShaderParameter("light_mix", 1f);
            echo.Material = material;
            echo.ZIndex = 1;
            echo.Visible = false;
            _echoes[i] = echo;
            AddChild(echo);
        }
        AddChild(_old);
        AddChild(_new);
        _front = new Node2D { ZIndex = 4 };
        _front.Draw += DrawForeground;
        AddChild(_front);
        if (_style == UnfolderKind.Mina)
        {
            _crack = new Sprite2D
            {
                Texture = _detail, Position = _after.Position, Rotation = -.25f,
                Scale = Vector2.One * (80f / _detail.GetHeight()), ZIndex = 5,
                TextureFilter = TextureFilterEnum.Linear,
                Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
                Visible = false,
            };
            AddChild(_crack);
        }
        Audio.Instance?.Se(Audio.Instance.SfxCalm, -15, _style == UnfolderKind.Mina ? .55f : .85f);
    }

    private static Texture2D Load(string name) => GD.Load<Texture2D>($"res://char/v3/fx/{name}.png");

    private ShaderMaterial MaterialFor(Shader shader, Frame frame, bool incoming, Color color)
    {
        var material = new ShaderMaterial { Shader = shader };
        var offset = frame.Offset;
        if (frame.Flip) offset.X = -offset.X;
        material.SetShaderParameter("pivot", Vector2.One * .5f - offset / frame.Texture.GetSize());
        material.SetShaderParameter("style", (int)_style);
        material.SetShaderParameter("incoming", incoming);
        material.SetShaderParameter("light_color", color);
        return material;
    }

    private static float Unit(float value) => Mathf.Clamp(value, 0, 1);
    private static float Ease(float value) => Mathf.SmoothStep(0, 1, Unit(value));
    private float Envelope => Mathf.Min(Ease((float)_time / .22f), Ease(((float)Duration - (float)_time) / .55f));

    public override void _Process(double delta)
    {
        if (Pad.UiBlocked(this)) return;
        if (!IsInstanceValid(_boss) || _boss.IsQueuedForDeletion() || _boss.IsPurified)
        {
            Restore();
            QueueFree();
            return;
        }
        _time += delta;
        float t = (float)_time;
        // Hold the gathered pose briefly before the impact; no whole-body banking or stretching.
        float gather = Ease(t / (ImpactTime - .13f));
        _old.Position = _before.Position.Lerp(_after.Position, gather);
        _oldMaterial.SetShaderParameter("light_mix", gather * .8f);
        _oldMaterial.SetShaderParameter("progress", Ease((t - .74f) / .48f));
        _new.Visible = t >= ImpactTime;
        float reveal = Ease((t - ImpactTime) / .54f);
        _newMaterial.SetShaderParameter("progress", reveal);
        _newMaterial.SetShaderParameter("light_mix", (1 - reveal) * .9f);
        if (_crack != null)
        {
            _crack.Visible = t >= .94f && t < ImpactTime;
            _crack.Modulate = new Color(_color, Ease((t - .94f) / .25f) * .8f);
        }
        for (int i = 0; i < _echoes.Length; i++)
        {
            float appear = Ease((t - .76f) / .34f);
            float disappear = 1 - Ease((t - ImpactTime) / .65f);
            var echo = _echoes[i];
            echo.Visible = appear > 0 && disappear > 0;
            echo.Modulate = new Color(1, 1, 1, appear * disappear * (i == 0 ? .19f : .09f));
            echo.Scale = _after.Scale * (1 + (i + 1) * .045f * (1 - reveal));
            echo.Position = _after.Position;
            if (_style == UnfolderKind.Rei) echo.Position += new Vector2((i == 0 ? -1 : 1) * 12 * (1 - reveal), 0);
        }
        if (t >= .64f && !_charged)
        {
            _charged = true;
            PlayCharge();
        }
        if (t >= ImpactTime && !_revealed)
        {
            _revealed = true;
            PlayImpact();
            GameCamera.Instance?.Shake(_style == UnfolderKind.Mina ? 2.8f : 1.7f, .18f);
        }
        QueueRedraw();
        _front.QueueRedraw();
        if (_time < Duration) return;
        Restore();
        QueueFree();
        _completed?.Invoke();
    }

    public override void _Draw()
    {
        float t = (float)_time;
        float fade = Envelope;
        var center = _after.Position;
        float gather = Ease(t / (ImpactTime - .13f));
        float release = Unit((t - ImpactTime) / .95f);
        DrawRect(new Rect2(0, 0, Field.Right, Field.Bottom), new Color(.015f, .018f, .03f, .8f * fade));
        switch (_style)
        {
            case UnfolderKind.Akari: DrawAkari(center, t, gather, release, fade); break;
            case UnfolderKind.Koharu: DrawKoharu(center, t, gather, release, fade); break;
            case UnfolderKind.Rei: DrawRei(center, t, gather, release, fade); break;
            case UnfolderKind.Mina: DrawMina(center, t, gather, release, fade); break;
        }
    }

    private void DrawAkari(Vector2 center, float t, float gather, float release, float fade)
    {
        for (int i = 0; i < 10; i++)
        {
            float angle = i * Mathf.Tau / 10 - gather * 1.7f;
            var direction = Vector2.FromAngle(angle);
            float radius = t < ImpactTime ? Mathf.Lerp(104, 9, gather) : 9 + 150 * Ease(release);
            var p = center + direction * new Vector2(radius, radius * .65f);
            Stamp(this, _motif, p, 14 - gather * 5, angle * .3f, new Color(_color, fade * (1 - release) * .9f));
            for (int k = 0; k < _trail.Length; k++)
            {
                float u = k / (float)(_trail.Length - 1);
                float a = angle + (1 - u) * .65f;
                _trail[k] = center + Vector2.FromAngle(a) * new Vector2(radius + (1 - u) * 22, (radius + (1 - u) * 22) * .65f);
                _trailColors[k] = new Color(i % 3 == 0 ? _secondary : _color, u * .6f * fade * (1 - release));
            }
            DrawPolylineColors(_trail, _trailColors, .8f, true);
        }
        float flame = Unit((t - ImpactTime) / .45f);
        if (t >= ImpactTime && flame < 1)
            DrawColoredPolygon(new[] { center + new Vector2(-8 * (1 - flame), 34), center + new Vector2(-35 * flame, -80),
                center + new Vector2(0, -125 * flame), center + new Vector2(35 * flame, -80), center + new Vector2(8 * (1 - flame), 34) },
                new Color(_color, (1 - flame) * .4f));
    }

    private void DrawKoharu(Vector2 center, float t, float gather, float release, float fade)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < 6; i++)
            {
                float x = side * (20 + i * 17) * (1 - gather * .55f + release);
                float y = 68 - gather * (15 + i * 9) - release * 75;
                Stamp(this, _motif, center + new Vector2(x, y), 11 + gather * 5, -side * (.3f + i * .1f),
                    new Color(Colors.White, fade * (1 - release) * .8f));
            }
            DrawSetTransform(center + new Vector2(side * 48 * (1 - gather), -75), side * (.22f - gather * .1f));
            DrawTextureRect(_detail, new Rect2(-16, 0, 32, 160), false, new Color(side < 0 ? _color : _secondary, .26f * fade * (1 - release)));
            DrawSetTransform(Vector2.Zero);
            for (int k = 0; k < _trail.Length; k++)
            {
                float u = k / (float)(_trail.Length - 1);
                float width = 37 * (1 - gather * .55f) + release * 120;
                _trail[k] = center + new Vector2(side * Mathf.Sin(u * Mathf.Tau + t * 2.2f) * width, 70 - u * 142);
                _trailColors[k] = new Color(side < 0 ? _color : _secondary, Mathf.Sin(u * Mathf.Pi) * .7f * fade * (1 - release));
            }
            DrawPolylineColors(_trail, _trailColors, 2.4f, true);
            DrawPolylineColors(_trail, _trailColors, .65f, true);
        }
    }

    private void DrawRei(Vector2 center, float t, float gather, float release, float fade)
    {
        for (int i = 0; i < 3; i++)
        {
            float width = 105 - gather * (55 - i * 8) + release * (70 + i * 15);
            float height = width * .58f;
            DrawSetTransform(center, (i - 1) * .15f * (1 - gather + release));
            var rect = new Rect2(-width, -height, width * 2, height * 2);
            DrawRect(rect, new Color(i == 1 ? _secondary : _color, .5f * fade * (1 - release)), false, .7f);
            DrawLine(new Vector2(-width, -height + 5), new Vector2(width, -height + 5), new Color(_color, .24f * fade * (1 - release)), .5f, true);
            DrawSetTransform(Vector2.Zero);
        }
        var previous = Vector2.Zero;
        for (int i = 0; i <= 5; i++)
        {
            float angle = i * Mathf.Tau / 5 - Mathf.Pi / 2 - gather * .65f;
            var p = center + Vector2.FromAngle(angle) * (72 - gather * 24 + release * 98);
            if (i > 0) DrawLine(previous, p, new Color(_color, .35f * fade * (1 - release)), .6f, true);
            Stamp(this, i % 2 == 0 ? _motif : _detail, p, 10 + gather * 3, -angle * .2f, new Color(Colors.White, fade * (1 - release)));
            previous = p;
        }
        for (int i = 0; i < 7 && t < ImpactTime; i++)
        {
            float y = center.Y - 55 + i * 17 + Mathf.Sin(Mathf.Floor(t * 14) + i * 8) * 3;
            float x = center.X - 75 + Mathf.Sin(i * 23 + Mathf.Floor(t * 9)) * 22;
            DrawRect(new Rect2(x, y, 45 + i * 7, .6f), new Color(i % 2 == 0 ? _color : _secondary, .25f * gather));
        }
    }

    private void DrawMina(Vector2 center, float t, float gather, float release, float fade)
    {
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.Tau / 12;
            float radius = 106 - 80 * gather + release * 150;
            var p = center + Vector2.FromAngle(angle) * new Vector2(radius, radius * .8f);
            float length = (8 + gather * 12) * (1 - release);
            var dir = (center - p).Normalized();
            DrawLine(p - dir * length, p, new Color(_secondary, .5f * fade * (1 - release)), 1.4f, true);
            if (i % 2 == 0) Stamp(this, _motif, p, 6 + gather * 4, angle, new Color(Colors.White, .7f * fade * (1 - release)));
        }
        if (t >= ImpactTime)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < _trail.Length; k++)
                {
                    float u = k / (float)(_trail.Length - 1);
                    _trail[k] = center + new Vector2(side * (15 + u * 135 * Ease(release * 2)), -Mathf.Sin(u * Mathf.Pi) * 48 + release * 24);
                    _trailColors[k] = new Color(_color, (1 - u) * .7f * fade * (1 - release));
                }
                DrawPolylineColors(_trail, _trailColors, 1.1f, true);
            }
        }
    }

    private void DrawForeground()
    {
        float t = (float)_time;
        float release = Unit((t - ImpactTime) / .75f);
        float fade = Envelope;
        var center = _after.Position;
        if (_style == UnfolderKind.Mina)
        {
            float shell = Ease((t - .42f) / .5f) * (1 - release);
            var vertices = new[] { new Vector2(0, -66), new Vector2(32, -8), new Vector2(22, 38),
                new Vector2(0, 60), new Vector2(-22, 38), new Vector2(-32, -8) };
            for (int i = 0; i < vertices.Length; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Length];
                var shift = (a + b).Normalized() * (80 * Ease(release) + (1 - Ease((t - .42f) / .5f)) * 22);
                var triangle = new[] { center + shift, center + a + shift, center + b + shift };
                _front.DrawColoredPolygon(triangle, new Color(i % 2 == 0 ? new Color("121828") : new Color("25324a"), shell * .94f));
                _front.DrawPolyline(new[] { triangle[0], triangle[1], triangle[2], triangle[0] }, new Color(_color, shell * .75f), .7f, true);
            }
        }
        if (t >= ImpactTime && release < 1)
        {
            float flash = 1 - Unit((t - ImpactTime) / .13f);
            // One restrained exposure flash, never a repeated full-screen strobe.
            _front.DrawRect(Field.Rect, new Color(_color.Lerp(Colors.White, .8f), flash * .2f));
            for (int i = 0; i < 16; i++)
            {
                float a = i * 2.399963f;
                var dir = Vector2.FromAngle(a);
                var p = center + dir * new Vector2(12 + (80 + i * 4) * Ease(release), 8 + (50 + i * 2) * Ease(release));
                var color = new Color(i % 3 == 0 ? _secondary : _color, (1 - release) * .8f);
                if (_style == UnfolderKind.Rei)
                    Stamp(_front, _detail, p, 3 + (i % 3) * 2, release + i, color);
                else if (_style == UnfolderKind.Akari)
                    Shard(_front, _motif, p, 3 + (i % 4), a + release * 2, color);
                else
                    _front.DrawLine(p, p - dir * (4 + 12 * (1 - release)), color, i % 3 == 0 ? 1.2f : .6f, true);
            }
        }
        float bar = 7 * fade;
        _front.DrawRect(new Rect2(Field.Left, 0, Field.Width, bar), new Color(.006f, .008f, .014f, .95f));
        _front.DrawRect(new Rect2(Field.Left, Field.Bottom - bar, Field.Width, bar), new Color(.006f, .008f, .014f, .95f));
    }

    private static void Stamp(CanvasItem canvas, Texture2D texture, Vector2 position, float height, float rotation, Color color)
    {
        var size = texture.GetSize() * (height / texture.GetHeight());
        canvas.DrawSetTransform(position, rotation);
        canvas.DrawTextureRect(texture, new Rect2(-size / 2, size), false, color);
        canvas.DrawSetTransform(Vector2.Zero);
    }

    private static void Shard(CanvasItem canvas, Texture2D texture, Vector2 position, float size, float rotation, Color color)
    {
        canvas.DrawSetTransform(position, rotation);
        canvas.DrawPolygon(new[] { new Vector2(-size, -size * .6f), new Vector2(size, -size * .2f), new Vector2(-size * .25f, size) },
            new[] { color, color, color }, new[] { new Vector2(.1f, .1f), new Vector2(.9f, .3f), new Vector2(.35f, .9f) }, texture);
        canvas.DrawSetTransform(Vector2.Zero);
    }

    private void PlayCharge()
    {
        if (Audio.Instance is not { } audio) return;
        switch (_style)
        {
            case UnfolderKind.Akari: audio.Se(audio.SfxStrip, -18, .75f); break;
            case UnfolderKind.Koharu: audio.Se(audio.SfxPurify, -18, .75f); break;
            case UnfolderKind.Rei: audio.Se(audio.SfxUiConfirm, -16, .65f); break;
            case UnfolderKind.Mina: audio.Se(audio.SfxMemoryFlash, -17, .55f); break;
        }
    }

    private void PlayImpact()
    {
        if (Audio.Instance is not { } audio) return;
        switch (_style)
        {
            case UnfolderKind.Akari:
                audio.Se(audio.SfxStrip, -11, .7f); audio.Se(audio.SfxSpell, -17, .9f); break;
            case UnfolderKind.Koharu:
                audio.Se(audio.SfxPurify, -12, 1.15f); audio.Se(audio.SfxSpell, -19, .75f); break;
            case UnfolderKind.Rei:
                audio.Se(audio.SfxStrip, -15, 1.3f); audio.Se(audio.SfxSpell, -12, 1.2f); break;
            case UnfolderKind.Mina:
                audio.Se(audio.SfxBomb, -13, .65f); audio.Se(audio.SfxStrip, -16, .6f); break;
        }
    }

    private void Restore()
    {
        if (_restored) return;
        _restored = true;
        if (IsInstanceValid(_boss))
        {
            _boss.FinishFormReveal();
            _boss.Visible = _visible;
        }
        if (IsInstanceValid(_world)) _world.ProcessMode = _worldMode;
        if (IsInstanceValid(_game)) _game.ProcessMode = _gameMode;
        if (IsInstanceValid(_hud))
        {
            _hud.SetCinematicMode(false);
            _hud.SuppressCallouts = _suppressed;
        }
    }

    public override void _ExitTree() => Restore();
}
