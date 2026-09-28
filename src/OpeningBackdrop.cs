using Godot;

public sealed class OpeningBackdrop
{
    private const string Folder = "res://char/bg2/prologue/";
    private readonly Texture2D _depth = GD.Load<Texture2D>(Folder + "timeline_depth_v2.png");
    private readonly Texture2D[] _cards =
    {
        GD.Load<Texture2D>(Folder + "timeline_empty_v2.png"),
        GD.Load<Texture2D>(Folder + "timeline_posts_v2.png"),
        GD.Load<Texture2D>(Folder + "timeline_drafts_v2.png"),
    };
    private readonly Vector2[] _thread = new Vector2[25];
    private static readonly Color[] DepthTints =
    {
        new(0.32f, 0.41f, 0.4f), new(0.65f, 0.8f, 0.83f),
        new(0.92f, 0.96f, 1f), new(0.58f, 0.6f, 0.65f),
    };
    private static readonly Color[] CardTints =
    {
        new(0.4f, 0.54f, 0.5f), new(0.85f, 0.97f, 1f),
        new(0.93f, 0.96f, 1f), new(0.94f, 0.94f, 0.95f),
    };
    private static readonly Color[] Signals =
    {
        new("79cdb3"), new("a1dfeb"), new("a4ded0"), new("dfc6a3"),
    };

    public void Draw(CanvasItem canvas, Rect2 bounds, int current, int previous, float mix, float time, float alpha = 1f)
    {
        float unit = bounds.Size.X / 1280f;
        Vector2 depthSize = bounds.Size * 1.025f;
        Vector2 depthShift = new Vector2(Mathf.Sin(time * 0.12f) * 3f, Mathf.Cos(time * 0.1f) * 2f) * unit;
        Color depthTint = DepthTints[previous].Lerp(DepthTints[current], mix);
        canvas.DrawTextureRect(_depth, new Rect2(bounds.GetCenter() - depthSize / 2f + depthShift, depthSize),
            false, new Color(depthTint, alpha));

        Color accent = Signals[previous].Lerp(Signals[current], mix);
        float activity = Mathf.Lerp(previous == 0 ? 0.25f : 1f, current == 0 ? 0.25f : 1f, mix);
        DrawSignals(canvas, bounds, time, new Color(accent, alpha * activity));

        if (CardIndex(current) == CardIndex(previous))
            DrawCards(canvas, bounds, current, time, new Color(CardTints[previous].Lerp(CardTints[current], mix), alpha));
        else
        {
            DrawCards(canvas, bounds, previous, time, new Color(CardTints[previous], alpha * (1f - mix)));
            DrawCards(canvas, bounds, current, time, new Color(CardTints[current], alpha * mix));
        }
    }

    private static int CardIndex(int phase) => phase < 2 ? 0 : phase - 1;

    private static Vector2 CardMotion(float time, int side) => new(
        Mathf.Sin(time * 0.23f + side * 2.1f) * 9f,
        Mathf.Sin(time * 0.37f + side * 1.7f) * 11f);

    private void DrawCards(CanvasItem canvas, Rect2 bounds, int phase, float time, Color tint)
    {
        if (tint.A <= 0f) return;
        var texture = _cards[CardIndex(phase)];
        Vector2 sourceSize = texture.GetSize();
        float unit = bounds.Size.X / 1280f;
        for (int side = 0; side < 2; side++)
        {
            Vector2 shift = CardMotion(time, side) * unit;
            var source = new Rect2(side * sourceSize.X / 2f, 0, sourceSize.X / 2f, sourceSize.Y);
            var target = new Rect2(bounds.Position + new Vector2(side * bounds.Size.X / 2f, 0) + shift,
                new Vector2(bounds.Size.X / 2f, bounds.Size.Y));
            canvas.DrawTextureRectRegion(texture, target, source, tint);
        }
    }

    private void DrawSignals(CanvasItem canvas, Rect2 bounds, float time, Color color)
    {
        float unit = bounds.Size.X / 1280f;
        for (int side = 0; side < 2; side++)
        {
            Vector2 shift = CardMotion(time, side);
            Vector2 start = new Vector2(side == 0 ? 340 : 948, side == 0 ? 284 : 308) + shift;
            Vector2 end = new(side == 0 ? 504 : 780, side == 0 ? 420 : 444);
            Vector2 bend = new(side == 0 ? 76 : -76, 0);
            for (int i = 0; i < _thread.Length; i++)
            {
                float p = i / (float)(_thread.Length - 1), q = 1f - p;
                Vector2 point = q * q * q * start + 3f * q * q * p * (start + bend)
                    + 3f * q * p * p * (end - bend) + p * p * p * end;
                _thread[i] = bounds.Position + point * unit;
            }
            canvas.DrawPolyline(_thread, new Color(color, color.A * 0.2f), 1f * unit, true);
            float travel = Mathf.PosMod(time * 0.16f + side * 0.5f, 1f) * (_thread.Length - 1);
            int segment = Mathf.Min((int)travel, _thread.Length - 2);
            Vector2 signal = _thread[segment].Lerp(_thread[segment + 1], travel - segment);
            Vector2 direction = (_thread[segment + 1] - _thread[segment]).Normalized();
            float envelope = Mathf.Sin(travel / (_thread.Length - 1) * Mathf.Pi);
            canvas.DrawLine(signal - direction * 10f * unit, signal, new Color(color, color.A * envelope * 0.75f), 1.8f * unit, true);
        }

        for (int i = 0; i < 18; i++)
        {
            float phase = Mathf.PosMod(time * (0.045f + i % 3 * 0.009f) + i * 0.173f, 1f);
            float x = i % 2 == 0 ? 86f + i * 15f : 970f + (i - 1) * 13f;
            float y = 695f - phase * 510f;
            float a = Mathf.Sin(phase * Mathf.Pi) * color.A * 0.34f;
            var at = bounds.Position + new Vector2(x + Mathf.Sin(time * 0.28f + i) * 8f, y) * unit;
            canvas.DrawRect(new Rect2(at, new Vector2(5f + i % 4 * 3f, 1.2f) * unit), new Color(color, a));
        }
    }
}
