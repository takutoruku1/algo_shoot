using Godot;
using System;

public partial class AkariReplyVideo : Node2D
{
    public const string VideoPath = "res://assets/video/akari_reply_v04.ogv";
    public const double SendAt = 0.65, ReplyAt = 2.5, Duration = 5.08;
    public const string Message = "何度も送って消して、ごめん。\nおめでとう ほんとだよ 元気でね";
    public Action? Completed;
    public bool Finished { get; private set; }
    public double Elapsed => _player.StreamPosition;
    public bool Sent { get; private set; }
    public bool Replied { get; private set; }

    private VideoStreamPlayer _player = null!;
    private readonly FilmSkip _skip = new();
    private bool _leaving;
    private double _leaveTime;

    public override void _Ready()
    {
        Scale = Vector2.One * UiKit.Scale;
        TextureFilter = TextureFilterEnum.Linear;
        _player = new VideoStreamPlayer
        {
            Name = "Movie", Stream = GD.Load<VideoStream>(VideoPath), Visible = false, Volume = 0,
        };
        AddChild(_player);
        _player.Finished += Complete;
        _skip.Begin(GetNode<GameManager>("/root/Game"), "akari_aftermath");
        Audio.Instance?.StopVoice();
        _player.Play();
    }

    public override void _Process(double delta)
    {
        if (Finished) return;
        _player.Paused = Pad.UiBlocked(this);
        if (_player.Paused) return;
        Pad.ConsumeUi(this);
        if (_leaving)
        {
            _leaveTime += delta;
            if (_leaveTime >= 0.45 && !Pad.MouseDown()) Complete();
        }
        else if (_skip.Update(delta)) RequestSkip();
        else
        {
            if (!Sent && Elapsed >= SendAt)
            {
                Sent = true;
                Audio.Instance?.PlayUiConfirm();
                Hud.PushLog("あかり / 西野とのメッセージ", Hud.LineKind.Other, "送信したメッセージ", Message, new Color("f0c969"));
            }
            if (!Replied && Elapsed >= ReplyAt)
            {
                Replied = true;
                Audio.Instance?.PlayUiBuy();
                Hud.PushLog("あかり / 西野とのメッセージ", Hud.LineKind.Other, "西野", "新着メッセージ", new Color("87d7ed"));
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var screen = new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH);
        DrawRect(screen, Colors.Black);
        if (Finished) return;
        DrawTextureRect(_player.GetVideoTexture(), screen, false);
        double t = Elapsed;
        if (t >= 0.3 && t < 2.25)
        {
            float alpha = (float)Math.Clamp(Math.Min((t - 0.3) / 0.25, (2.25 - t) / 0.25), 0, 1);
            var box = new Rect2(54, 386, 520, 186);
            DialogueBox.DrawFrame(this, box, Sent ? "西野へ　送信済み" : "西野へ", new Color("f0c969"),
                DialogueBox.LoadPortrait(CompanionDialogue.Portrait(Job.Melee)), alpha: alpha);
            DialogueBox.DrawBody(this, box, Message, Message.Length + 1, alpha);
        }
        else if (t >= ReplyAt && t < ReplyAt + 1.2)
        {
            float alpha = (float)Math.Clamp(Math.Min((t - ReplyAt) / 0.18, (ReplyAt + 1.2 - t) / 0.3), 0, 1);
            var box = new Rect2(54, 386, 400, 116);
            DialogueBox.DrawFrame(this, box, "西野", new Color("87d7ed"), alpha: alpha);
            DialogueBox.DrawBody(this, box, "新着メッセージ", 20, alpha);
        }
        float fade = _leaving ? (float)(_leaveTime / 0.45)
            : (float)Math.Max(1 - t / 0.3, (t - (Duration - 0.6)) / 0.6);
        DrawRect(screen, new Color(0, 0, 0, Math.Clamp(fade, 0, 1)));
        if (!_leaving) _skip.Draw(this);
    }

    public void RequestSkip()
    {
        if (!_skip.Available || _leaving || Finished) return;
        _leaving = true;
    }

    private void Complete()
    {
        if (Finished) return;
        Finished = true;
        _player.Stop();
        QueueRedraw();
        Completed?.Invoke();
    }

    public override void _ExitTree()
    {
        _player.Stop();
        _player.Stream = null;
    }
}
