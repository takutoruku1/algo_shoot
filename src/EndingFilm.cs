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
    { "……覚えている声が、あります。", "返事を待つだけじゃなくて。\nあたしの明日も、決めていいんだ。", "……ひとりで、なんとかしなくても。\n少しだけ、頼ってみよう。", "ねえ。ここ、分かんないんだけど……\n聞いてもいい？", "好きなところ、まだあるの。\n……今日は、最後まで話すね。", "……ちゃんと、話せた。", "ミナ。待っててくれて、ありがと。", "次は、ミナの話も聞かせて。\n……急がなくて、いいから。", "……ええ。\nわたくしも、ここにいて、よいのですね。", "" };
    private static readonly Rect2 SkipRect = DialogToolbar.FilmSkipRect;
    private Texture2D[] _art = null!;
    private bool _inputArmed, _leaving;
    private double _skipHold, _leaveTime;
    private int _loggedShot = -1;   // 会話ログへ字幕を積んだカット（カット切替ごとに1回）
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
        var akari = GD.Load<Texture2D>(dir + "cg_ep_akari_v1.png");
        var koharu = GD.Load<Texture2D>(dir + "cg_ep_koharu_v1.png");
        var rei = GD.Load<Texture2D>(dir + "cg_ep_rei_v1.png");
        _art = new Texture2D[]
        {
            GD.Load<Texture2D>(dir + "cg_ep_mina_sky_v3.png"),
            akari,
            koharu, koharu,
            rei, rei,
            akari, rei,
            GD.Load<Texture2D>(dir + "cg_ep_goodbye_v2.png"),
            GD.Load<Texture2D>(dir + "bg_ep_dawn_v2.png"),
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
            // カットの字幕を会話ログ（L / Tab で開く Backlog）へ積む（2026-09-26）。本文・話者・色は _Draw と同じ。
            if (Shot != _loggedShot)
            {
                _loggedShot = Shot;
                string sp = Speakers[_loggedShot];
                if (Lines[_loggedShot].Length > 0)
                    Hud.PushLog(sp == "ミナ" ? Hud.LineKind.Mina : Hud.LineKind.Other, sp, Lines[_loggedShot], AccentFor(sp));
            }
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
        if (Lines[shot].Length > 0)
        {
            var box = DialogueBox.FullScreen;
            DialogueBox.DrawFrame(this, box, Speakers[shot], AccentFor(Speakers[shot]));
            var lines = UiKit.WrapLines(DialogueBox.Body.Font, Lines[shot], DialogueBox.Body.Size, DialogueBox.WrapWidth(box));
            DialogueBox.DrawBody(this, box, string.Join("\n", lines), int.MaxValue, captionAlpha);
        }
        if (Elapsed > 1 && shot < 9)
            DialogToolbar.DrawFilmSkip(this, Mathf.Clamp((float)(_skipHold / 0.65), 0, 1), SkipRect.HasPoint(Pad.MousePos()));
        if (_leaving)
            Frame(9, 1, Ease(_leaveTime / 0.7));
    }

    // 話者ごとの差し色（字幕と会話ログで共用）。
    private static Color AccentFor(string speaker) => speaker switch
    {
        "あかり" => new Color("f0c969"),
        "こはる" => new Color("a6dac8"),
        "レイ" => new Color("de91b9"),
        _ => new Color("87d7ed"),
    };


}
