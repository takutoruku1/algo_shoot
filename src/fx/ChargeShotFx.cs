using Godot;

public partial class ChargeShotFx : Node2D
{
    public enum Beat { Ready, Release, Impact }
    public Job Character;
    public Beat Kind;
    private float _age;
    private float Duration => Kind == Beat.Ready ? 0.22f : Kind == Beat.Release ? 0.32f : 0.42f;
    private static readonly Vector2[] Curve = new Vector2[25];
    private static readonly Vector2[] Star = new Vector2[10];

    public override void _Ready()
    {
        ZAsRelative = false;
        ZIndex = -4;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (Hud.BubblePaused || _age >= Duration) { QueueFree(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Hud.BubblePaused) return;
        float t = Mathf.Clamp(_age / Duration, 0, 1);
        float fade = (1 - t) * (1 - t);
        float spread = Mathf.Sin(t * Mathf.Pi / 2);
        var color = BulletArt.PlayerColor(Character);
        var art = BulletArt.PlayerShot(Character);
        float reach = Kind == Beat.Impact ? 40 : Kind == Beat.Release ? 29 : 17;
        float radius = 4 + reach * spread;
        DrawSetTransform(Vector2.Zero, 0, new Vector2(Kind == Beat.Release ? 0.48f : 1, 1));
        for (int i = 0; i < 3; i++)
            DrawArc(Vector2.Zero, radius - i * 2, t * 0.8f + i * 2.1f,
                t * 0.8f + i * 2.1f + 1.65f, 22, new Color(color, fade * 0.8f), 1.4f - i * 0.3f, true);
        DrawSetTransform(Vector2.Zero);
        int count = Kind == Beat.Impact ? 10 : 6;
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.Tau / count + (int)Character * 0.3f;
            var direction = Vector2.FromAngle(angle);
            var at = direction * radius;
            DrawLine(direction * radius * 0.55f, at, new Color(color, fade), 1.4f, true);
            Emblem(this, art, Character, at, 6 * (1 - t) + 2, angle, fade);
        }
        if (Kind != Beat.Ready)
        {
            DrawLine(new Vector2(-reach * spread, 0), new Vector2(reach * spread, 0),
                new Color(color, fade * 0.7f), 5 * (1 - t) + 0.5f, true);
            DrawLine(new Vector2(-reach * spread, 0), new Vector2(reach * spread, 0),
                new Color(Colors.White, fade), 1.3f, true);
        }
    }

    public static void DrawGather(Node2D canvas, Job job, Vector2 muzzle, Vector2 direction, float ratio, float time)
    {
        var art = BulletArt.PlayerShot(job);
        var color = art.Accent;
        canvas.DrawSetTransform(muzzle, direction.Angle());
        for (int i = 0; i < 7; i++)
        {
            float cycle = Mathf.PosMod(time * 1.9f + i / 7f, 1);
            float angle = i * Mathf.Tau / 7 + time * 1.4f;
            float radius = Mathf.Lerp(32, 5, cycle) * (0.55f + ratio * 0.45f);
            Vector2 at = Vector2.FromAngle(angle) * new Vector2(radius, radius * 0.72f);
            canvas.DrawLine(at * 1.15f, at, new Color(color, cycle * ratio * 0.75f), 0.9f, true);
            Emblem(canvas, art, job, at, 2 + cycle * 3, angle, cycle * ratio);
        }
        float orbit = 10 + ratio * 4;
        for (int i = 0; i < 3; i++)
        {
            float start = time * 2.2f + i * Mathf.Tau / 3;
            canvas.DrawArc(Vector2.Zero, orbit, start, start + ratio * 1.5f, 16, new Color(color, ratio * 0.8f), 0.9f, true);
        }
        DrawCore(canvas, art, Vector2.Zero, 8 + ratio * 13, ratio);
        if (ratio >= 1)
        {
            float pulse = 0.65f + Mathf.Sin(time * 9) * 0.15f;
            canvas.DrawLine(new Vector2(-19, 0), new Vector2(19, 0), new Color(color, pulse), 0.6f, true);
            canvas.DrawLine(new Vector2(0, -15), new Vector2(0, 15), new Color(Colors.White, pulse), 0.6f, true);
        }
        canvas.DrawSetTransform(Vector2.Zero);
    }

    public static void DrawProjectile(Node2D canvas, BulletArt.PlayerVisual art, Job job, float age, float radius)
    {
        Color color = art.Accent;
        float length = Mathf.Min(72, 20 + age * 650);
        for (int strand = 0; strand < 3; strand++)
        {
            for (int i = 0; i < Curve.Length; i++)
            {
                float t = (float)i / (Curve.Length - 1);
                float amplitude = job == Job.Melee ? 5 : job == Job.Heal ? 9 : 6;
                float wave = Mathf.Sin(t * 8 - age * 25 + strand * Mathf.Tau / 3);
                Curve[i] = new Vector2(-length * t, wave * amplitude * t);
            }
            canvas.DrawPolyline(Curve, new Color(color, 0.18f), 4.5f, true);
            canvas.DrawPolyline(Curve, new Color(color.Lerp(Colors.White, 0.35f), 0.65f - strand * 0.12f), 1.3f, true);
        }
        for (int i = 0; i < 4; i++)
        {
            float t = Mathf.PosMod(age * 2.4f + i * 0.25f, 1);
            var at = new Vector2(-length * t, Mathf.Sin(t * 9 + i * 1.7f) * radius * t);
            Emblem(canvas, art, job, at, 4 + 5 * (1 - t), age * 2 + i, (1 - t) * 0.85f);
        }
        DrawCore(canvas, art, new Vector2(-5, 0), radius * 5.4f, 0.18f);
        DrawCore(canvas, art, Vector2.Zero, radius * 4.4f, 1);
        canvas.DrawLine(new Vector2(radius * 0.7f, 0), new Vector2(radius * 2.6f, 0),
            new Color(Colors.White, 0.9f), 1, true);
    }

    private static void DrawCore(Node2D canvas, BulletArt.PlayerVisual art, Vector2 at, float size, float alpha)
    {
        float scale = size / Mathf.Max(art.Region.Size.X, art.Region.Size.Y);
        var rect = new Rect2((art.Region.Position - art.Pivot) * scale + at, art.Region.Size * scale);
        canvas.DrawTextureRectRegion(art.Texture, rect, art.Region, new Color(1, 1, 1, alpha));
    }

    private static void Emblem(Node2D canvas, BulletArt.PlayerVisual art, Job job, Vector2 at, float size, float angle, float alpha)
    {
        if (job == Job.Magic)
        {
            for (int i = 0; i < Star.Length; i++)
                Star[i] = at + Vector2.FromAngle(angle + i * Mathf.Pi / 5) * size * (i % 2 == 0 ? 0.55f : 0.24f);
            canvas.DrawColoredPolygon(Star, new Color(new Color("ffe8a0"), alpha));
        }
        else DrawCore(canvas, art, at, size, alpha);
    }
}
