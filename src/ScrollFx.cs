using Godot;

public partial class ScrollFx : Node2D
{
    public enum StageKind { Rei, Akari, Koharu, Mina }
    public StageKind Kind = StageKind.Rei;
    public bool SkipScrollTexture;

    private const float W = 384f, H = 216f;
    private double _t;
    private float _bulletDamp = 1f;
    private float _fade = 1f;
    private GameManager _game = null!;
    private Sprite2D[] _scrollTiles = System.Array.Empty<Sprite2D>();
    private float _scrollTexW, _scrollX, _scrollBaseSpeed;
    private Layer _far = null!;
    private Layer _near = null!;

    public override void _Ready()
    {
        ZIndex = -60;
        ZAsRelative = false;
        AddToGroup("scrollfx");
        _game = GetNode<GameManager>("/root/Game");
        _fade = 1f - Mathf.Clamp(_game.Warmth, 0f, 1f);
        if (!SkipScrollTexture) SetupScrollTexture();
        _far = new Layer { Name = "Far", Owner2 = this, ZIndex = -60, ZAsRelative = false };
        _near = new Layer { Name = "Near", Owner2 = this, Near = true, ZIndex = -55, ZAsRelative = false };
        AddChild(_far);
        AddChild(_near);
    }

    private (string path, float speed) ScrollDef => Kind switch
    {
        StageKind.Rei => ("res://char/bg/rei/scroll.png", 60f),
        StageKind.Akari => ("res://char/bg/akari/scroll.png", 75f),
        StageKind.Koharu => ("res://char/bg/koharu/scroll.png", 32f),
        _ => ("", 0f),
    };

    private void SetupScrollTexture()
    {
        var (path, speed) = ScrollDef;
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path)) return;
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null) return;
        _scrollBaseSpeed = speed;
        float scale = H / tex.GetHeight();
        _scrollTexW = tex.GetWidth() * scale;
        int count = Mathf.Max(2, Mathf.CeilToInt(W / _scrollTexW) + 1);
        var tiles = new Sprite2D[count];
        for (int i = 0; i < count; i++)
        {
            var spr = new Sprite2D
            {
                Name = $"Scroll{i}", Texture = tex, Centered = false,
                Scale = new Vector2(scale, scale), Position = new Vector2(i * _scrollTexW, 0f),
                ZIndex = -70, ZAsRelative = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            AddChild(spr);
            tiles[i] = spr;
        }
        _scrollTiles = tiles;
    }

    public override void _Process(double delta)
    {
        // Match the panorama's dialogue/cinematic freeze; integrate speed so purification cannot jump particles.
        if (Hud.BubblePaused) return;
        float dt = (float)delta;
        float warm = Mathf.Clamp(_game.Warmth, 0f, 1f);
        int bullets = GetTree().GetNodesInGroup("enemy_bullets").Count;
        float target = Mathf.Lerp(1f, 0.38f, Mathf.Clamp((bullets - 20) / 80f, 0f, 1f));
        _bulletDamp = Mathf.Lerp(_bulletDamp, target, 1f - Mathf.Exp(-4f * dt));
        _fade = Mathf.Lerp(_fade, 1f - warm, 1f - Mathf.Exp(-2f * dt));
        _t += delta * (1f - 0.45f * warm);

        if (_scrollTiles.Length > 0)
        {
            float scrollMul = 0.65f + 0.80f * BgScroll.PlayerNx(this);
            _scrollX += _scrollBaseSpeed * (1f - 0.45f * warm) * scrollMul * dt;
            float off = _scrollX % _scrollTexW;
            for (int i = 0; i < _scrollTiles.Length; i++)
            {
                var p = _scrollTiles[i].Position;
                p.X = i * _scrollTexW - off;
                if (p.X <= -_scrollTexW) p.X += _scrollTiles.Length * _scrollTexW;
                _scrollTiles[i].Position = p;
            }
        }
        _far.QueueRedraw();
        _near.QueueRedraw();
    }

    public partial class Layer : Node2D
    {
        public ScrollFx Owner2 = null!;
        public bool Near;
        private readonly Vector2[] _curve = new Vector2[17];
        private readonly Color[] _curveColors = new Color[17];
        private readonly Vector2[] _quad = new Vector2[4];
        private readonly Color[] _colors = new Color[4];

        public override void _Draw()
        {
            float fade = Owner2._fade * Owner2._bulletDamp;
            if (fade < 0.002f) return;
            float t = (float)Owner2._t;
            switch (Owner2.Kind)
            {
                case StageKind.Akari: DrawAkari(fade, t); break;
                case StageKind.Koharu: DrawKoharu(fade, t); break;
                case StageKind.Rei: DrawRei(fade, t); break;
                case StageKind.Mina: DrawMina(fade, t); break;
            }
        }

        private static float Seed(int i, uint salt)
        {
            uint x = unchecked((uint)i * 747796405u + salt * 2891336453u + 277803737u);
            x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
            x = (x >> 22) ^ x;
            return (x & 0xffffff) / 16777216f;
        }

        private static float WrapX(float x, float margin = 32f)
            => Field.Left - margin + Mathf.PosMod(x, Field.Width + margin * 2f);

        private static float Air(Vector2 p)
        {
            float edge = Mathf.SmoothStep(0f, 12f, Mathf.Min(p.X - Field.Left, Field.Right - p.X));
            edge *= Mathf.SmoothStep(0f, 9f, Mathf.Min(p.Y, H - p.Y));
            return edge * Mathf.Lerp(0.42f, 1f, Mathf.SmoothStep(24f, 90f, Mathf.Abs(p.Y - H * 0.5f)));
        }

        private void Stroke(Vector2 from, Vector2 to, Color color, float width)
        {
            DrawLine(from, to, new Color(color, color.A * 0.18f), width * 3.5f, true);
            DrawLine(from, to, color, width, true);
        }

        private void Beam(Vector2 top, Vector2 bottom, float width, Color color)
        {
            var side = (bottom - top).Orthogonal().Normalized() * width;
            for (int half = -1; half <= 1; half += 2)
            {
                for (int k = 0; k < 8; k++)
                {
                    float u = k / 8f, v = (k + 1) / 8f;
                    _quad[0] = top.Lerp(bottom, u); _quad[1] = top.Lerp(bottom, v);
                    _quad[2] = _quad[1] + side * half * Mathf.Lerp(0.16f, 1f, v);
                    _quad[3] = _quad[0] + side * half * Mathf.Lerp(0.16f, 1f, u);
                    _colors[0] = new Color(color, color.A * (1f - u * 0.92f) * Air(_quad[0]));
                    _colors[1] = new Color(color, color.A * (1f - v * 0.92f) * Air(_quad[1]));
                    _colors[2] = _colors[3] = new Color(color, 0f);
                    DrawPolygon(_quad, _colors);
                }
            }
        }

        private void Ribbon(Color color)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                float width = pass == 0 ? 5f : pass == 1 ? 2.4f : 0.7f;
                float opacity = pass == 0 ? 0.08f : pass == 1 ? 0.16f : 0.7f;
                for (int k = 0; k < _curve.Length; k++)
                {
                    float taper = Mathf.Sin(k / (float)(_curve.Length - 1) * Mathf.Pi);
                    _curveColors[k] = new Color(color, color.A * opacity * Mathf.Max(0f, taper) * Air(_curve[k]));
                }
                DrawPolylineColors(_curve, _curveColors, width, true);
            }
        }

        private void DrawAkari(float fade, float t)
        {
            float wind = Mathf.Sin(t * 0.43f) * 6f + Mathf.Sin(t * 0.91f) * 2f;
            int count = Near ? 38 : 76;
            for (int i = 0; i < count; i++)
            {
                float sx = Seed(i, Near ? 13u : 11u), sy = Seed(i, 7);
                float depth = Seed(i, 19);
                float speed = Near ? 84f + depth * 46f : 28f + depth * 30f;
                var head = new Vector2(WrapX(sx * (Field.Width + 64f) - t * speed + wind),
                    Mathf.PosMod(sy * (H + 32f) + t * speed * 0.56f, H + 32f) - 16f);
                float length = Near ? 11f + depth * 10f : 4f + depth * 6f;
                var tail = head + new Vector2(length, -length * (0.55f + 0.08f * Mathf.Sin(t * 0.43f)));
                float a = (Near ? 0.34f : 0.17f) * fade * Air(head) * (0.65f + depth * 0.35f);
                var c = new Color(0.70f, 0.87f, 1f, a);
                DrawLine(tail, head.Lerp(tail, 0.42f), new Color(c, a * 0.3f), Near ? 0.7f : 0.4f, true);
                if (Near) Stroke(head.Lerp(tail, 0.42f), head, c, 0.65f + depth * 0.25f);
                else DrawLine(head.Lerp(tail, 0.42f), head, c, 0.45f, true);
            }
            if (!Near)
            {
                for (int i = 0; i < 3; i++)
                {
                    float x = Field.Left + 38f + i * 96f + Mathf.Sin(t * 0.22f + i) * 9f;
                    Beam(new Vector2(x, -12f), new Vector2(x - 45f, 182f), 34f,
                        new Color(0.53f, 0.72f, 0.96f, 0.06f * fade));
                }
                return;
            }
            for (int i = 0; i < 13; i++)
            {
                float life = Mathf.PosMod(t * (0.6f + Seed(i, 4) * 0.4f) + Seed(i, 9), 1f);
                var p = new Vector2(Field.Left + 14f + Seed(i, 5) * (Field.Width - 28f), 181f + Seed(i, 6) * 29f);
                p.X -= life * 8f;
                float a = Mathf.Sin(life * Mathf.Pi) * (1f - life) * 0.34f * fade * Air(p);
                for (int k = 0; k < _curve.Length; k++)
                {
                    float angle = Mathf.Tau * k / (_curve.Length - 1);
                    _curve[k] = p + new Vector2(Mathf.Cos(angle) * (2f + life * 9f), Mathf.Sin(angle) * (0.6f + life * 1.6f));
                }
                DrawPolyline(_curve, new Color(0.67f, 0.86f, 1f, a), 0.5f, true);
                if (life < 0.22f)
                    DrawLine(p, p + new Vector2(1.3f, -3.5f * (1f - life / 0.22f)), new Color(0.85f, 0.94f, 1f, a), 0.65f, true);
            }
        }

        private void DrawKoharu(float fade, float t)
        {
            if (!Near)
            {
                for (int i = 0; i < 4; i++)
                {
                    float x = Field.Left + 35f + i * 66f;
                    Beam(new Vector2(x, -10f), new Vector2(x - 22f + Mathf.Sin(t * 0.24f + i) * 15f, 190f), 26f,
                        new Color(0.93f, 0.79f, 0.54f, (0.065f + 0.015f * Mathf.Sin(t * 0.6f + i)) * fade));
                }
                for (int i = 0; i < 42; i++)
                {
                    float s = Seed(i, 2);
                    var p = new Vector2(WrapX(Seed(i, 3) * (Field.Width + 64f) - t * (5f + s * 9f)),
                        Seed(i, 4) * H + Mathf.Sin(t * 0.55f + i) * 5f);
                    float a = (0.13f + 0.09f * Mathf.Sin(t * 1.1f + i)) * fade * Air(p);
                    DrawLine(p, p + new Vector2(0.6f + s, -0.5f), new Color(1f, 0.87f, 0.64f, a), 0.65f, true);
                }
                return;
            }
            for (int i = 0; i < 9; i++)
            {
                float x = WrapX(Seed(i, 21) * (Field.Width + 112f) - t * (11f + Seed(i, 24) * 7f), 56f);
                float y = i % 3 == 0 ? 28f + Seed(i, 25) * 22f : 160f + Seed(i, 25) * 46f;
                float length = 26f + Seed(i, 22) * 24f;
                for (int k = 0; k < _curve.Length; k++)
                {
                    float u = k / (float)(_curve.Length - 1);
                    _curve[k] = new Vector2(x - length * u, y - u * 8f + Mathf.Sin(u * 4.5f + t * 0.65f + i) * (2f + u * 4f));
                }
                Ribbon(new Color(0.86f, 0.87f, 0.78f, 0.17f * fade));
            }
        }

        private void DrawRei(float fade, float t)
        {
            int count = Near ? 22 : 38;
            for (int i = 0; i < count; i++)
            {
                float s = Seed(i, 32);
                float x = WrapX(Seed(i, Near ? 30u : 31u) * (Field.Width + 64f) - t * (Near ? 36f + s * 18f : 12f + s * 12f));
                float y = 8f + Seed(i, 33) * (H - 16f);
                float length = Near ? 12f + s * 22f : 3f + s * 8f;
                float a = (Near ? 0.28f : 0.15f) * fade * Air(new Vector2(x, y));
                var c = i % 4 == 0 ? new Color(0.94f, 0.83f, 0.71f, a) : new Color(0.55f, 0.91f, 0.90f, a);
                c.A *= 0.72f + 0.28f * Mathf.Sin(t * 1.4f + i * 2f);
                DrawLine(new Vector2(x, y), new Vector2(x + length * 0.68f, y), new Color(c, c.A * 0.4f), 0.5f, true);
                Stroke(new Vector2(x + length * 0.77f, y), new Vector2(x + length, y), c, Near ? 0.8f : 0.45f);
                if (Near && i % 3 == 0)
                {
                    DrawLine(new Vector2(x, y), new Vector2(x, y + 3f), c, 0.6f, true);
                    DrawLine(new Vector2(x + 3f, y + 3f), new Vector2(x + length * 0.5f, y + 3f), new Color(c, c.A * 0.5f), 0.5f, true);
                }
            }
            if (Near) return;
            float scan = Mathf.PosMod(t * 9f, H + 60f) - 30f;
            for (int i = 0; i < 10; i++)
            {
                float x = Field.Left + 10f + i * (Field.Width - 20f) / 10f;
                float a = 0.1f * fade * Air(new Vector2(x, scan));
                DrawLine(new Vector2(x, scan), new Vector2(x + 17f, scan), new Color(0.55f, 0.92f, 0.92f, a), 0.5f, true);
            }
        }

        private void DrawMina(float fade, float t)
        {
            int count = Near ? 18 : 32;
            for (int i = 0; i < count; i++)
            {
                float s = Seed(i, 42);
                float x = WrapX(Seed(i, Near ? 41u : 40u) * (Field.Width + 64f) - t * (Near ? 15f + s * 14f : 5f + s * 9f));
                float y = Seed(i, 43) * H + Mathf.Sin(t * 0.55f + i * 1.9f) * (Near ? 11f : 5f);
                var p = new Vector2(x, y);
                float a = (Near ? 0.28f : 0.15f) * fade * Air(p) * (0.75f + 0.25f * Mathf.Sin(t * 1.2f + i));
                var c = i % 3 == 0 ? new Color(0.98f, 0.49f, 0.55f, a) : new Color(0.72f, 0.88f, 0.89f, a);
                float angle = t * (0.3f + s * 0.25f) + i;
                var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float length = Near ? 3f + s * 3f : 1f + s;
                if (Near)
                {
                    var side = axis.Orthogonal() * (0.3f + 0.6f * Mathf.Abs(Mathf.Sin(t * 0.8f + i)));
                    _quad[0] = p - axis * length;
                    _quad[1] = p + side;
                    _quad[2] = p + axis * length;
                    _quad[3] = p - side;
                    DrawColoredPolygon(_quad, new Color(c, a * 0.35f));
                    Stroke(_quad[0], _quad[2], c, 0.55f);
                }
                else DrawLine(p - axis * length, p + axis * length, c, 0.55f, true);
            }
            if (Near) return;
            for (int i = 0; i < 4; i++)
            {
                for (int k = 0; k < _curve.Length; k++)
                {
                    float u = k / (float)(_curve.Length - 1);
                    float y = i < 2 ? 16f + i * 13f : H - 16f - (i - 2) * 13f;
                    _curve[k] = new Vector2(Field.Left + u * Field.Width,
                        y + Mathf.Sin(u * 7f + t * 0.38f + i) * 6f);
                }
                Ribbon(new Color(0.69f, 0.77f, 0.85f, 0.1f * fade));
            }
        }
    }
}
