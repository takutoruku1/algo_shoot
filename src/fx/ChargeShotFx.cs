using Godot;

public partial class ChargeShotFx : Node2D
{
    // Ready2 / Release2 は 2段階チャージ（2026-09-25）の2段目ぶん。1段目より一回り大きく・長く出し、
    //   逆回りの金の輪を重ねる＝「もう一段ぶん溜まった／撃った」を1段目と見間違えない形にする。
    public enum Beat { Ready, Release, Impact, Ready2, Release2 }
    public Job Character;
    public Beat Kind;
    private float _age;
    // 2段目かどうか（見た目の尺・大きさ・色に効く）。
    private bool Tier2 => Kind == Beat.Ready2 || Kind == Beat.Release2;
    // 段を落とした「素の拍」。既存の分岐（Ready / Release / Impact）はこちらを見る。
    private Beat Base => Kind == Beat.Ready2 ? Beat.Ready : Kind == Beat.Release2 ? Beat.Release : Kind;
    private float Duration => (Base == Beat.Ready ? 0.22f : Base == Beat.Release ? 0.32f : 0.42f) * (Tier2 ? 1.5f : 1f);
    private static readonly Vector2[] Curve = new Vector2[25];
    private static readonly Vector2[] Star = new Vector2[10];
    // 2段目の色（金）。キャラ色（BulletArt.PlayerColor）と必ず別の色にする＝どのジョブでも「2段目だ」と読める。
    public static readonly Color Tier2Gold = new("ffd782");

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
        // 2段目は届く距離も 1.5 倍＝画面上の面積で「重いほう」と分かる。
        float reach = (Base == Beat.Impact ? 40 : Base == Beat.Release ? 29 : 17) * (Tier2 ? 1.5f : 1f);
        float radius = 4 + reach * spread;
        DrawSetTransform(Vector2.Zero, 0, new Vector2(Base == Beat.Release ? 0.48f : 1, 1));
        for (int i = 0; i < 3; i++)
            DrawArc(Vector2.Zero, radius - i * 2, t * 0.8f + i * 2.1f,
                t * 0.8f + i * 2.1f + 1.65f, 22, new Color(color, fade * 0.8f), 1.4f - i * 0.3f, true);
        // 2段目だけの上乗せ：逆回りの金の二重輪。既存の弧と回る向きが逆＝重なっても混ざらない。
        if (Tier2)
            for (int i = 0; i < 2; i++)
                DrawArc(Vector2.Zero, radius * (1.18f + i * 0.16f), -t * 1.4f + i * Mathf.Pi,
                    -t * 1.4f + i * Mathf.Pi + 2.4f, 26, new Color(Tier2Gold, fade * 0.9f), 2.2f - i * 0.8f, true);
        DrawSetTransform(Vector2.Zero);
        int count = Base == Beat.Impact || Tier2 ? 10 : 6;
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.Tau / count + (int)Character * 0.3f;
            var direction = Vector2.FromAngle(angle);
            var at = direction * radius;
            DrawLine(direction * radius * 0.55f, at, new Color(color, fade), 1.4f, true);
            Emblem(this, art, Character, at, (6 * (1 - t) + 2) * (Tier2 ? 1.35f : 1f), angle, fade);
        }
        if (Base != Beat.Ready)
        {
            DrawLine(new Vector2(-reach * spread, 0), new Vector2(reach * spread, 0),
                new Color(color, fade * 0.7f), 5 * (1 - t) + 0.5f, true);
            DrawLine(new Vector2(-reach * spread, 0), new Vector2(reach * spread, 0),
                new Color(Colors.White, fade), 1.3f, true);
        }
    }

    // ratio2 は 2段目の充填率 0..1（0＝2段目を持っていない／まだ1段目が満ちていない）。
    public static void DrawGather(Node2D canvas, Job job, Vector2 muzzle, Vector2 direction, float ratio, float time, float ratio2 = 0f)
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
        // 2段目が溜まるあいだ、集まる光そのものが大きくなる（核の直径が 1→1.45 倍まで育つ）。
        DrawCore(canvas, art, Vector2.Zero, (8 + ratio * 13) * (1 + ratio2 * 0.45f), ratio);
        if (ratio >= 1)
        {
            float pulse = 0.65f + Mathf.Sin(time * 9) * 0.15f;
            canvas.DrawLine(new Vector2(-19, 0), new Vector2(19, 0), new Color(color, pulse), 0.6f, true);
            canvas.DrawLine(new Vector2(0, -15), new Vector2(0, 15), new Color(Colors.White, pulse), 0.6f, true);
        }
        // 2段目ぶんの金の輪。溜まるほど締まっていき、満ちると太く・速く脈打つ＝「もう一段ある／満ちた」。
        if (ratio2 > 0)
        {
            float ring = Mathf.Lerp(30, 17, ratio2);
            bool full2 = ratio2 >= 1;
            float a2 = full2 ? 0.75f + Mathf.Sin(time * 16) * 0.25f : 0.35f + ratio2 * 0.4f;
            canvas.DrawArc(Vector2.Zero, ring, 0, Mathf.Tau, 30, new Color(Tier2Gold, a2), full2 ? 2.6f : 1.4f, true);
            for (int i = 0; i < 5; i++)
            {
                float angle = -time * 2.6f + i * Mathf.Tau / 5;
                var at = Vector2.FromAngle(angle) * ring;
                canvas.DrawLine(at, at * (full2 ? 0.7f : 0.85f), new Color(Tier2Gold, a2), 1.2f, true);
            }
        }
        canvas.DrawSetTransform(Vector2.Zero);
    }

    // stage は ChargeTier.First / Second。2段目は尾を長く引き、核の外に金の輪を重ねる
    //   （弾そのものの大きさは Bullet.Radius が既に 2段目ぶん太い＝ここは「段の色」を足すだけ）。
    public static void DrawProjectile(Node2D canvas, BulletArt.PlayerVisual art, Job job, float age, float radius, int stage = ChargeTier.First)
    {
        Color color = art.Accent;
        bool tier2 = stage >= ChargeTier.Second;
        float length = Mathf.Min(tier2 ? 104 : 72, 20 + age * 650);
        if (job == Job.Heal)
        {
            // こはる：波打つ尾は付けない。ペンライトを振った残光＝まっすぐ細って消える一本の光条にする
            //   （2026-09-25 ユーザー「にょろにょろがださい」）。根元が太く白く、先端へ向けて色だけが残る。
            int segs = Curve.Length - 1;
            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs, t1 = (float)(i + 1) / segs;
                float fade = 1 - t0;
                var a = new Vector2(-length * t0, 0);
                var b = new Vector2(-length * t1, 0);
                canvas.DrawLine(a, b, new Color(color, 0.16f * fade), 7f * fade + 1.5f, true);
                canvas.DrawLine(a, b, new Color(color, 0.42f * fade), 3f * fade + 0.8f, true);
                canvas.DrawLine(a, b, new Color(color.Lerp(Colors.White, 0.6f), 0.85f * fade * fade), 1.2f * fade + 0.4f, true);
            }
            // 尾に沿って小さなハートが二列で流れていく（揺らさない＝振った光の軌跡に乗って後ろへ抜ける）。
            for (int i = 0; i < 4; i++)
            {
                float t = Mathf.PosMod(age * 2.4f + i * 0.25f, 1);
                float lane = (i % 2 == 0 ? 1 : -1) * radius * 0.45f * t;
                var at = new Vector2(-length * t, lane);
                Emblem(canvas, art, job, at, 3 + 4 * (1 - t), 0, (1 - t) * 0.8f);
            }
        }
        else
        {
            for (int strand = 0; strand < 3; strand++)
            {
                for (int i = 0; i < Curve.Length; i++)
                {
                    float t = (float)i / (Curve.Length - 1);
                    float amplitude = job == Job.Melee ? 5 : 6;
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
        }
        DrawCore(canvas, art, new Vector2(-5, 0), radius * 5.4f, 0.18f);
        DrawCore(canvas, art, Vector2.Zero, radius * 4.4f, 1);
        canvas.DrawLine(new Vector2(radius * 0.7f, 0), new Vector2(radius * 2.6f, 0),
            new Color(Colors.White, 0.9f), 1, true);
        // 2段目の徽章＝核を巻く金の二重輪。飛んでいる弾を見ただけでどちらの一発か分かる。
        if (tier2)
            for (int i = 0; i < 2; i++)
                canvas.DrawArc(Vector2.Zero, radius * (1.5f + i * 0.55f), age * 7 + i * Mathf.Pi,
                    age * 7 + i * Mathf.Pi + 4.2f, 24, new Color(Tier2Gold, 0.85f - i * 0.3f), 2f - i * 0.7f, true);
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
