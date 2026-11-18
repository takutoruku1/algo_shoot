using Godot;
using System;

public partial class OpeningVideo : Node2D
{
    public const string VideoPath = "res://assets/video/opening_v08.ogv";
    public const double Duration = 72.6;
    public Action? Completed;
    public double Elapsed => _player.StreamPosition;
    public bool Finished { get; private set; }

    private static readonly (double Time, string Speaker, string Text, string Accent)[] Captions =
    {
        (9.3, "あかり", "……それしか、送れなかった。", "f0c969"),
        (14.4, "レイ", "今日はちょっと、疲れてて……。", "de91b9"),
        (19.2666667, "こはる", "明日も、学校なのに。\n……配信、最後まで見なきゃ。", "a6dac8"),
        (24.1333333, "ミナ", "……投稿できなかった言葉が、\n聞こえます。", "87d7ed"),
        (29, "ミナ", "消された言葉は、消えていない。", "87d7ed"),
        (39, "あかり", "まだ、伝えてないことがある。", "f0c969"),
        (43.9666667, "こはる", "好きなはずなのに、\n休むのが、怖い。", "a6dac8"),
        (48.8, "レイ", "笑っていないと、\n誰も聞いてくれない？", "de91b9"),
        (55.6666667, "ミナ", "ご主人様が開いた道を、\nわたくしが進みます。", "87d7ed"),
    };
    private VideoStreamPlayer _player = null!;
    private int _nextCaption;
    private bool _inputArmed, _leaving;
    private double _skipHold, _leaveTime;
    private bool SkipVisible => Elapsed > 1 && !_leaving && Elapsed < Duration - 1.3;

    public override void _Ready()
    {
        Name = "OpeningVideo";
        Scale = Vector2.One * UiKit.Scale;
        TextureFilter = TextureFilterEnum.Linear;
        Audio.Instance?.StopMusic(0);
        Audio.Instance?.StopVoice();
        _player = new VideoStreamPlayer
        {
            Name = "Movie",
            Stream = GD.Load<VideoStream>(VideoPath),
            Bus = "Music",
            Volume = Audio.Instance?.Muted == true ? 0 : 1,
            Visible = false,
        };
        AddChild(_player);
        _player.Finished += Complete;
        _inputArmed = !SkipHeld();
        _player.Play();
    }

    public override void _Process(double delta)
    {
        if (Finished) return;
        if (Pad.UiBlocked(this))
        {
            _player.Paused = true;
            _inputArmed = false;
            _skipHold = 0;
            return;
        }
        _player.Paused = false;
        Pad.ConsumeUi(this);
        if (_leaving)
        {
            _leaveTime += delta;
            if (_leaveTime >= 0.45) { Complete(); return; }
            _player.Volume = Audio.Instance?.Muted == true ? 0 : (float)(1 - _leaveTime / 0.45);
        }
        else
        {
            while (_nextCaption < Captions.Length && Elapsed >= Captions[_nextCaption].Time)
            {
                var caption = Captions[_nextCaption++];
                Hud.PushLog("オープニング", caption.Speaker == "ミナ" ? Hud.LineKind.Mina : Hud.LineKind.Other,
                    caption.Speaker == "ミナ" ? "" : caption.Speaker, caption.Text, new Color(caption.Accent));
            }
            bool held = SkipHeld();
            if (!held) _inputArmed = true;
            _skipHold = _inputArmed && held && Elapsed >= 0.6 ? _skipHold + delta : 0;
            if (_skipHold >= 0.65 || (SkipVisible
                && DialogToolbar.FilmSkipRect.HasPoint(Pad.MousePos()) && Pad.MouseClick()))
                RequestSkip();
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var screen = new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH);
        DrawRect(screen, Colors.Black);
        DrawTextureRect(_player.GetVideoTexture(), screen, false);
        if (_leaving) DrawRect(screen, new Color(0, 0, 0, (float)(_leaveTime / 0.45)));
        if (SkipVisible)
            DialogToolbar.DrawFilmSkip(this, (float)(_skipHold / 0.65),
                DialogToolbar.FilmSkipRect.HasPoint(Pad.MousePos()));
    }

    private static bool SkipHeld() => Input.IsKeyPressed(Key.Escape) || Input.IsKeyPressed(Key.X)
        || Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept")
        || Pad.Pressed(JoyButton.B) || Pad.Pressed(JoyButton.Start);

    public void RequestSkip()
    {
        if (_leaving || Finished || Elapsed < 0.6) return;
        _leaving = true;
    }

    private void Complete()
    {
        if (Finished) return;
        Finished = true;
        _player.Stop();
        Visible = false;
        FilmSkip.MarkSeen(GetNodeOrNull<GameManager>("/root/Game"), "opening");
        Completed?.Invoke();
        QueueFree();
    }

    public override void _ExitTree()
    {
        _player.Stop();
        _player.Stream = null;
    }
}
