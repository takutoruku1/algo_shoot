using Godot;
using System;

public partial class EndingFilm : Node2D
{
    public const double Duration = 53;
    public Action? Completed;
    public double Elapsed { get; private set; }
    public bool Finished { get; private set; }

    private static readonly double[] Cuts = { 0, 4, 10, 15, 20, 26, 31, 37, 42, 49, Duration };
    private static readonly string[] Speakers = { "ミナ", "あかり", "こはる", "こはる", "レイ", "レイ", "あかり", "レイ", "ミナ", "" };
    private static readonly string[] Lines =
    {
        "……覚えている声が、あります。",
        "返事を待つだけじゃなくて。\nあたしの明日も、決めていいんだ。",
        "……ひとりで、なんとかしなくても。\n少しだけ、頼ってみよう。",
        "ねえ。ここ、分かんないんだけど……\n聞いてもいい？",
        "好きなところ、まだあるの。\n……今日は、最後まで話すね。",
        "……ちゃんと、話せた。",
        "ミナ。待っててくれて、ありがと。",
        "次は、ミナの話も聞かせて。\n……急がなくて、いいから。",
        "……ええ。\nわたくしも、ここにいて、よいのですね。",
        "",
    };
    private static readonly Rect2 SkipRect = new(1110, 665, 140, 43);
    private Texture2D[] _art = null!;
    private bool _inputArmed, _leaving;
    private double _skipHold, _leaveTime;
    private int Shot
    {
        get
        {
            int index = 0;
            while (index < Cuts.Length - 2 && Elapsed >= Cuts[index + 1]) index++;
            return index;
        }
    }

    public override void _Ready()
    {
        Name = "EndingFilm";
        Scale = Vector2.One * UiKit.Scale;
        TextureFilter = TextureFilterEnum.Linear;
        const string dir = "res://char/bg2/ending/";
        var together = GD.Load<Texture2D>(dir + "cg_ep_together_v1.png");
        var koharu = GD.Load<Texture2D>(dir + "cg_ep_koharu_v1.png");
        var rei = GD.Load<Texture2D>(dir + "cg_ep_rei_v1.png");
        _art = new Texture2D[]
        {
            GD.Load<Texture2D>(dir + "cg_ep_rest.png"),
            GD.Load<Texture2D>(dir + "cg_ep_akari_v1.png"),
            koharu, koharu,
            rei, rei,
            together, together,
            GD.Load<Texture2D>(dir + "cg_ep_goodbye.png"),
            GD.Load<Texture2D>(dir + "bg_ep_dawn.png"),
        };
        _inputArmed = !SkipHeld();
        Audio.Instance?.Music(Audio.Instance.BgmMenu, 2.5f);
    }

    public override void _Process(double delta)
    {
        if (Finished) return;
        if (Pad.UiBlocked(this)) { _inputArmed = false; _skipHold = 0; return; }
        Pad.ConsumeUi(this);
        if (_leaving)
        {
            _leaveTime += delta;
            if (_leaveTime >= 0.7) { Complete(); return; }
        }
        else
        {
            Elapsed = Math.Min(Duration, Elapsed + delta);
            bool held = SkipHeld();
            if (!held) _inputArmed = true;
            _skipHold = _inputArmed && held && Elapsed >= 0.6 ? _skipHold + delta : 0;
            if (_skipHold >= 0.65 || (Elapsed > 1 && SkipRect.HasPoint(Pad.MousePos()) && Pad.MouseClick()))
                RequestSkip();
            if (Elapsed >= Duration) { Complete(); return; }
        }
        QueueRedraw();
    }

    private static bool SkipHeld() => Input.IsKeyPressed(Key.Escape) || Input.IsKeyPressed(Key.X)
        || Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept")
        || Pad.Pressed(JoyButton.A) || Pad.Pressed(JoyButton.B) || Pad.Pressed(JoyButton.Start);

    public void RequestSkip()
    {
        if (_leaving || Finished || Elapsed < 0.6) return;
        _leaving = true;
    }

    private void Complete()
    {
        if (Finished) return;
        Finished = true;
        Visible = false;
        // 「見た」の記録（OpeningFilm と同じ扱い。このフィルムも初回からスキップできるので
        //   記録はゲートではなく台帳。Completed はスタッフロールへの引き渡しなので必ず先に呼ばれる前に打つ）。
        FilmSkip.MarkSeen(GetNodeOrNull<GameManager>("/root/Game"), "ending");
        Completed?.Invoke();
        QueueFree();
    }

    private static float Ease(double value) => Mathf.SmoothStep(0, 1, (float)value);

    private Rect2 ArtRect(int shot, float progress)
    {
        float zoom = shot switch
        {
            0 => Mathf.Lerp(1.025f, 1.045f, progress),
            3 or 5 or 7 => Mathf.Lerp(1.025f, 1.01f, progress),
            8 => Mathf.Lerp(1.01f, 1f, progress),
            9 => 1f,
            _ => Mathf.Lerp(1.055f, 1.025f, progress),
        };
        Vector2 size = _art[shot].GetSize();
        size *= Mathf.Max(1280 / size.X, 720 / size.Y) * zoom;
        Vector2 slack = size - new Vector2(1280, 720);
        float pan = shot switch
        {
            0 or >= 6 => 0.5f,
            3 or 5 => Mathf.Lerp(0.68f, 0.5f, progress),
            _ => Mathf.Lerp(0.32f, 0.68f, progress),
        };
        return new Rect2(-slack * new Vector2(pan, shot == 8 ? 0f : 0.5f), size);
    }

    private void Frame(int shot, float progress, float alpha)
        => DrawTextureRect(_art[shot], ArtRect(shot, progress), false, new Color(1, 1, 1, alpha));

    public override void _Draw()
    {
        int shot = Shot;
        double local = Elapsed - Cuts[shot];
        double length = Cuts[shot + 1] - Cuts[shot];
        float progress = Ease(local / length);
        // Keep the previous frame opaque throughout the dissolve.
        if (shot > 0 && local < 0.9 && _art[shot] != _art[shot - 1])
        {
            Frame(shot - 1, 1, 1);
            Frame(shot, progress, Ease(local / 0.9));
        }
        else Frame(shot, progress, 1);

        float captionAlpha = Ease(local / 0.7) * (1 - Ease((local - length + 0.7) / 0.7));
        float framing = shot == 9 ? 1 - Ease(local / 2.5) : 1;
        Color accent = Speakers[shot] switch
        {
            "あかり" => new Color("f0c969"),
            "こはる" => new Color("a6dac8"),
            "レイ" => new Color("de91b9"),
            _ => new Color("87d7ed"),
        };
        if (shot > 0 && local < 0.35)
            DrawRect(new Rect2(0, 0, 1280, 720), new Color(accent, 0.045f * (1f - Ease(local / 0.35))));
        DrawFilmChrome(shot, progress, framing, captionAlpha, accent);
        DrawString(UiKit.ZenBold, new Vector2(80, 587), Speakers[shot], HorizontalAlignment.Left, -1, 22,
            new Color(accent, captionAlpha));
        string[] lines = Lines[shot].Split('\n');
        for (int i = 0; i < lines.Length; i++)
            DrawString(UiKit.Zen, new Vector2(80, 629 + i * 39), lines[i], HorizontalAlignment.Left, -1, 30,
                new Color(0.97f, 0.98f, 0.99f, captionAlpha));

        if (Elapsed > 1 && shot < 9)
        {
            bool hover = SkipRect.HasPoint(Pad.MousePos());
            DrawString(UiKit.Zen, new Vector2(1120, 694), "スキップ", HorizontalAlignment.Center, 120, 20,
                new Color(1, 1, 1, hover ? 1f : 0.64f));
            if (_skipHold > 0)
                DrawLine(new Vector2(1120, 704), new Vector2(1120 + 120 * (float)Math.Min(1, _skipHold / 0.65), 704), accent, 2);
        }
        if (_leaving)
            Frame(9, 1, Ease(_leaveTime / 0.7));
    }

    private void DrawFilmChrome(int shot, float progress, float framing, float captionAlpha, Color accent)
    {
        if (framing <= 0f) return;
        Color dark = new(0.018f, 0.022f, 0.034f, 0.88f * framing);
        if (shot != 8)
        {
            DrawRect(new Rect2(0, 0, 1280, 58), dark);
            UiKit.VGradient(this, new Rect2(0, 0, 1280, 58),
                new[] { new Color(1f, 1f, 1f, 0.055f * framing), new Color(1f, 1f, 1f, 0f) },
                new[] { 0f, 1f });
            DrawString(UiKit.ZenBold, new Vector2(80, 36), $"ENDING LOG / CUT {shot + 1:00}", HorizontalAlignment.Left, -1, 18,
                new Color(0.92f, 0.96f, 1f, 0.70f * framing));
            DrawString(UiKit.Zen, new Vector2(1078, 36), $"{Elapsed:00.0}s", HorizontalAlignment.Right, 120, 16,
                new Color(0.92f, 0.96f, 1f, 0.52f * framing));
            DrawTrack(shot, progress, accent, framing);
        }

        DrawRect(new Rect2(0, 540, 1280, 180), new Color(0.018f, 0.022f, 0.034f, 0.92f * framing));
        UiKit.VGradient(this, new Rect2(0, 540, 1280, 180),
            new[] { new Color(1f, 1f, 1f, 0.05f * framing), new Color(0f, 0f, 0f, 0.26f * framing) },
            new[] { 0f, 1f });
        UiKit.HGradient(this, new Rect2(80, 556, 560, 2), accent with { A = 0.05f * framing }, accent with { A = 0.78f * framing });
        UiKit.HGradient(this, new Rect2(640, 556, 560, 2), accent with { A = 0.78f * framing }, accent with { A = 0.05f * framing });
        DrawLine(new Vector2(62, 585), new Vector2(62, 682), new Color(accent, 0.55f * captionAlpha), 2f);
        DrawLine(new Vector2(1218, 585), new Vector2(1218, 682), new Color(accent, 0.22f * captionAlpha), 1f);
        UiKit.RadialGlow(this, new Vector2(1160, 548), 170f, accent, 0.12f * captionAlpha * framing);
    }

    private void DrawTrack(int shot, float progress, Color accent, float framing)
    {
        const float x0 = 850f, y = 28f, gap = 28f;
        for (int i = 0; i < Cuts.Length - 1; i++)
        {
            float x = x0 + i * gap;
            float a = i < shot ? 0.55f : i == shot ? 0.95f : 0.24f;
            DrawCircle(new Vector2(x, y), i == shot ? 3.2f : 2.1f, new Color(i == shot ? accent : new Color(0.86f, 0.91f, 1f), a * framing));
            if (i < Cuts.Length - 2)
            {
                float fill = i < shot ? 1f : i == shot ? progress : 0f;
                DrawLine(new Vector2(x + 5f, y), new Vector2(x + gap - 5f, y), new Color(0.86f, 0.91f, 1f, 0.18f * framing), 1f);
                if (fill > 0f)
                    DrawLine(new Vector2(x + 5f, y), new Vector2(x + 5f + (gap - 10f) * fill, y), new Color(accent, 0.70f * framing), 1.5f);
            }
        }
    }
}
