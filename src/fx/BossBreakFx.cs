using Godot;

public partial class BossBreakFx : Node2D
{
    public const float Duration = 0.72f;
    private const string Label = "BREAK";
    private const int FontSize = 27;
    private float _age;
    private readonly float[] _advances = new float[Label.Length];
    private float _textWidth;
    private static readonly Color Ink = new("111821");
    private static readonly Color Accent = new("8ae8ed");
    private static readonly Color Echo = new("e9a4c4");

    public override void _Ready()
    {
        // Keep hostile bullets (ZIndex 0) in front of the announcement. The boss body sprite shares -1,
        // so FxLayer.BossBreak adds this node to the tail of World to draw it after (= over) the body.
        ZIndex = -1;
        ZAsRelative = false;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        for (int i = 0; i < Label.Length; i++)
        {
            _advances[i] = UiKit.ZenBlack.GetStringSize(Label[i].ToString(), fontSize: FontSize).X;
            _textWidth += _advances[i];
        }
    }

    // 2026-09-22 ユーザー指示：頭上ではなくボス本体に重ねる。文字と斬線はボスの中心に置き、
    //   横は斬線の到達幅(70px)が盤面外へ出ない範囲、縦は文字の半高(25px)が盤面内に残る範囲へクランプするだけ。
    public void PlaceOn(Vector2 bossPosition, float bodyHeight)
    {
        GlobalPosition = new Vector2(Mathf.Clamp(bossPosition.X, Field.Left + 78f, Field.Right - 78f),
            Mathf.Clamp(bossPosition.Y, Field.Top + 25f, Field.Bottom - 25f));
    }

    public override void _Process(double delta)
    {
        if (Hud.BubblePaused)
        {
            Hide();
            QueueFree();
            return;
        }
        _age += (float)delta;
        if (_age >= Duration)
        {
            Hide();
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    private static float EaseOut(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp(t, 0f, 1f), 3f);

    public override void _Draw()
    {
        if (Hud.BubblePaused || _age >= Duration) return;
        float fade = 1f - Mathf.SmoothStep(0.32f, Duration, _age);
        float impact = 1f - Mathf.Clamp(_age / 0.24f, 0f, 1f);
        float spread = EaseOut(_age / 0.18f);
        float exit = EaseOut((_age - 0.38f) / 0.34f);
        DrawSetTransform(Vector2.Zero, -0.065f);

        float reach = 70f * spread;
        DrawColoredPolygon(new Vector2[]
        {
            new(-reach, 7), new(reach - 9, -3), new(reach, 1), new(-reach + 9, 12),
        }, new Color(Ink, 0.74f * fade));
        DrawLine(new Vector2(-reach, 12), new Vector2(reach, 2), new Color(Accent, fade), 0.8f, true);
        DrawLine(new Vector2(-reach + 8, -11), new Vector2(reach * 0.58f, -16),
            new Color(Echo, 0.6f * fade), 0.6f, true);

        for (int i = 0; i < 10; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float travel = 25f + (16f + i % 3 * 5f) * spread;
            var at = new Vector2(side * travel, (i / 2 - 2) * 7f);
            var end = at + new Vector2(side * (5f + i % 3 * 2f) * (1f - exit), -2f);
            DrawLine(at, end, new Color(i % 3 == 0 ? Echo : Accent, fade * (0.3f + impact * 0.6f)),
                i % 3 == 0 ? 1.1f : 0.6f, true);
        }

        if (_age < 0.18f)
        {
            float a = Mathf.Sin(Mathf.Clamp(_age / 0.18f, 0f, 1f) * Mathf.Pi);
            DrawLine(new Vector2(-72f * spread, 16), new Vector2(72f * spread, -15),
                new Color(Accent, a * 0.35f), 3.2f * a, true);
            DrawLine(new Vector2(-72f * spread, 16), new Vector2(72f * spread, -15),
                new Color(Colors.White, a), 0.8f, true);
        }

        float x = -_textWidth * 0.5f;
        for (int i = 0; i < Label.Length; i++)
        {
            float t = _age - 0.015f - i * 0.014f;
            if (t >= 0f)
            {
                float settle = EaseOut(t / 0.13f);
                float direction = i % 2 == 0 ? -1f : 1f;
                float leave = EaseOut((t - 0.36f) / 0.27f);
                float alpha = Mathf.Clamp(t / 0.025f, 0f, 1f) * (1f - leave);
                float scale = 1f + 0.18f * (1f - settle);
                var center = new Vector2(x + _advances[i] * 0.5f + (i - 2) * leave * 3f,
                    direction * ((1f - settle) * 9f + leave * 6f));
                var transform = new Transform2D(new Vector2(scale, 0), new Vector2(-0.18f * scale, scale), center);
                DrawSetTransformMatrix(new Transform2D(-0.065f, Vector2.Zero) * transform);
                var baseline = new Vector2(-_advances[i] * 0.5f,
                    (UiKit.ZenBlack.GetAscent(FontSize) - UiKit.ZenBlack.GetDescent(FontSize)) * 0.5f);
                string letter = Label[i].ToString();
                if (impact > 0f)
                    DrawStringOutline(UiKit.ZenBlack, baseline + new Vector2(-4f * impact, 2f * impact), letter,
                        fontSize: FontSize, size: 1, modulate: new Color(Echo, alpha * impact * 0.75f));
                DrawStringOutline(UiKit.ZenBlack, baseline + new Vector2(0.8f, 1.1f), letter,
                    fontSize: FontSize, size: 2, modulate: new Color(Ink, alpha));
                DrawStringOutline(UiKit.ZenBlack, baseline, letter,
                    fontSize: FontSize, size: 1, modulate: new Color(Accent, alpha));
                DrawString(UiKit.ZenBlack, baseline, letter, fontSize: FontSize,
                    modulate: new Color(Colors.White, alpha));
            }
            x += _advances[i];
        }
        DrawSetTransform(Vector2.Zero);
    }
}
