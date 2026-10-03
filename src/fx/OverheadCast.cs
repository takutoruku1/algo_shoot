using Godot;

public partial class OverheadCast : Node2D
{
    public const float Windup = 0.58f;
    private const float Afterglow = 0.42f;
    private Enemy _source = null!;
    private Bullet[] _bullets = null!;
    private Vector2[] _drops = null!;
    private Texture2D? _art;
    private Color _accent;
    private bool _major, _released;
    private float _time;
    private readonly Vector2[] _path = new Vector2[21];
    private readonly Color[] _pathColors = new Color[21];
    private readonly Vector2[] _ring = new Vector2[33];

    public static void Begin(Enemy source, Bullet[] bullets, Texture2D? art, Color accent)
    {
        if (bullets.Length == 0) return;
        var cast = new OverheadCast
        {
            _source = source, _bullets = bullets, _drops = new Vector2[bullets.Length],
            _art = art, _accent = accent.Lerp(Colors.White, 0.3f), _major = source is not CameoBoss,
        };
        for (int i = 0; i < bullets.Length; i++)
        {
            cast._drops[i] = bullets[i].GlobalPosition;
            bullets[i].HoldForOverhead(cast);
        }
        source.GetParent().AddChild(cast);
    }

    public override void _Ready()
    {
        AddToGroup("overhead_casts");
        ZIndex = -13;
        ZAsRelative = false;
        TextureFilter = TextureFilterEnum.Linear;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsInstanceValid(_source) || _source.IsQueuedForDeletion() || _source.IsPurified
            || _source.GaugeVulnerable || _source.GaugeReforming)
        {
            Cancel();
            return;
        }
        if (!_released)
        {
            bool pending = false;
            foreach (var bullet in _bullets)
                if (IsInstanceValid(bullet) && bullet.IsHeldBy(this)) { pending = true; break; }
            if (!pending) { QueueFree(); return; }
        }
        if (Hud.BubblePaused) return;
        _time += (float)GameManager.EnemyDelta(delta);
        if (!_released && _time >= Windup)
        {
            _released = true;
            foreach (var bullet in _bullets)
                if (IsInstanceValid(bullet)) bullet.ReleaseFromOverhead(this);
        }
        if (_time >= Windup + Afterglow) { QueueFree(); return; }
        QueueRedraw();
    }

    private void Cancel()
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        foreach (var bullet in _bullets)
            if (IsInstanceValid(bullet) && bullet.IsHeldBy(this)) pool.Despawn(bullet);
        Hide();
        QueueFree();
    }

    public override void _ExitTree()
    {
        // The pool outlives the scene; a removed emitter must never leave invisible reserved bullets behind.
        foreach (var bullet in _bullets)
            if (IsInstanceValid(bullet) && bullet.IsHeldBy(this))
                bullet.GetParent<BulletPool>().Despawn(bullet);
    }

    public override void _Draw()
    {
        if (!IsInstanceValid(_source) || _source.IsPurified) return;
        float charge = Mathf.Clamp(_time / Windup, 0f, 1f);
        float fade = _released ? Mathf.Clamp(1f - (_time - Windup) / Afterglow, 0f, 1f) : 1f;
        var origin = ToLocal(_source.ShotCenter);
        for (int i = 0; i < _drops.Length; i++)
        {
            if (!_released && (!_bullets[i].Active || !_bullets[i].IsHeldBy(this))) continue;
            float x = _drops[i].X;
            if (x < Field.Left || x > Field.Right) continue;
            var gate = ToLocal(new Vector2(x, Field.Top + 11f));
            float send = Mathf.Clamp(_time / 0.36f, 0f, 1f);
            if (_time < Windup + 0.12f) DrawTransfer(origin, gate, send, fade);
            DrawGate(gate, charge, fade);
        }
        if (!_released)
        {
            DrawArc(origin, 8f + charge * 3f, -Mathf.Pi, 0f, 20, new Color(_accent, 0.5f), 0.8f, true);
            DrawArc(origin, 12f, -Mathf.Pi + charge * 0.7f, -0.4f + charge * 0.7f, 20,
                new Color(_accent, 0.18f), 2f, true);
        }
    }

    private void DrawTransfer(Vector2 origin, Vector2 gate, float progress, float fade)
    {
        var control = new Vector2(origin.X + (gate.X - origin.X) * 0.18f, gate.Y - 24f);
        for (int k = 0; k < _path.Length; k++)
        {
            float u = k / (float)(_path.Length - 1);
            _path[k] = origin.Lerp(control, u).Lerp(control.Lerp(gate, u), u);
            float a = u <= progress ? 0.12f * fade * (1f - u * 0.35f) : 0f;
            _pathColors[k] = new Color(_accent, a);
        }
        DrawPolylineColors(_path, _pathColors, 0.65f, true);
        if (progress >= 1f) return;
        for (int i = 2; i >= 0; i--)
        {
            float u = Mathf.Max(0f, progress - i * 0.055f);
            var p = origin.Lerp(control, u).Lerp(control.Lerp(gate, u), u);
            DrawMotif(p, i == 0 ? 7f : 4f, (i == 0 ? 0.8f : 0.2f) * fade);
        }
    }

    private void DrawGate(Vector2 p, float charge, float fade)
    {
        float open = Mathf.SmoothStep(0f, 0.72f, charge);
        float width = (_major ? 13f : 10f) * (0.35f + open * 0.65f);
        float flash = _released ? fade : 0f;
        DrawEllipse(p, new Vector2(width, 3.5f + flash * 2f), new Color(_accent, (0.45f + flash * 0.4f) * fade), 1f);
        DrawEllipse(p, new Vector2(width + 2f, 5f + flash * 2f), new Color(_accent, 0.13f * fade), 2.5f);
        if (_major)
        {
            var rect = new Rect2(p - new Vector2(10f, 6f), new Vector2(20f, 12f));
            DrawRect(rect, new Color(0.06f, 0.07f, 0.12f, 0.8f * open * fade));
            DrawRect(rect, new Color(_accent, 0.65f * open * fade), false, 0.7f);
            DrawLine(rect.Position + new Vector2(2, 3), rect.Position + new Vector2(6, 3),
                new Color(_accent, 0.75f * open * fade), 0.7f, true);
        }
        DrawMotif(p - new Vector2(0, 1f + Mathf.Sin(charge * Mathf.Pi) * 2f), 9f, open * fade);
        var danger = new Color(AreaStrike.DangerEdge, (0.28f + charge * 0.4f) * fade);
        float down = _released ? (1f - fade) * 15f : 0f;
        for (int i = 0; i < 2; i++)
        {
            var tip = p + new Vector2(0f, 9f + i * 5f + down);
            DrawLine(tip + new Vector2(-2.5f, -2.5f), tip, danger, 0.7f, true);
            DrawLine(tip, tip + new Vector2(2.5f, -2.5f), danger, 0.7f, true);
        }
        if (_released)
            DrawLine(p + new Vector2(0, 4), p + new Vector2(0, 9 + down), new Color(_accent, 0.3f * fade), 1.5f, true);
    }

    private void DrawEllipse(Vector2 p, Vector2 radius, Color color, float width)
    {
        for (int k = 0; k < _ring.Length; k++)
        {
            float a = Mathf.Tau * k / (_ring.Length - 1);
            _ring[k] = p + new Vector2(Mathf.Cos(a) * radius.X, Mathf.Sin(a) * radius.Y);
        }
        DrawPolyline(_ring, color, width, true);
    }

    private void DrawMotif(Vector2 p, float height, float alpha)
    {
        if (_art == null) return;
        var size = _art.GetSize() * (height / _art.GetHeight());
        DrawTextureRect(_art, new Rect2(p - size * 0.5f, size), false, new Color(Colors.White, alpha));
    }
}
