using Godot;
using System;

public partial class BossDraftScene : Node2D
{
    private BossPostStory _story = null!;
    private Hud _hud = null!;
    private Node _world = null!;
    private GameManager _game = null!;
    private ProcessModeEnum _worldMode, _gameMode;
    private Action _completed = null!;
    private Texture2D _background = null!, _face = null!;
    private bool _suppressed, _held, _started, _leaving, _restored;
    private int _line;
    private double _time, _lineTime, _readTime, _exitTime;

    public static void Play(Hud hud, Node world, BossPostStory story, Action completed)
        => hud.AddChild(new BossDraftScene
        {
            Name = "BossDraftScene", ZIndex = -10,
            _hud = hud, _world = world, _story = story, _completed = completed,
        });

    public override void _Ready()
    {
        AddToGroup("boss_draft");
        _worldMode = _world.ProcessMode;
        _world.ProcessMode = ProcessModeEnum.Disabled;
        _game = GetNode<GameManager>("/root/Game");
        _gameMode = _game.ProcessMode;
        _game.ProcessMode = ProcessModeEnum.Disabled;
        _suppressed = _hud.SuppressCallouts;
        _hud.SuppressCallouts = true;
        _hud.HoldBubble = true;
        _hud.HideBubble();
        _hud.HideSpellCard();
        _hud.HideBossBar();
        _hud.SetCinematicMode(true, dialogueBubble: _story.Id == "mina");
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        if (Audio.Instance is { } audio) audio.Music(audio.StoryBgm(_story.Id, aftermath: true), 3f);
        _background = GD.Load<Texture2D>(_story.RealBackground);
        _face = GD.Load<Texture2D>(_story.Id == "mina" ? BossMina.CostumePath(4, "idle") : _story.Face);
        _held = Pad.AdvanceHeld();
    }

    public override void _Process(double delta)
    {
        if (Pad.UiBlocked(this)) { _held = true; return; }
        bool held = Pad.AdvanceHeld();
        bool edge = held && !_held;
        _held = held;
        _time += delta;
        QueueRedraw();
        if (_leaving)
        {
            _exitTime += delta;
            if (_exitTime < 0.6) return;
            Restore();
            _completed();
            QueueFree();
            return;
        }
        if (!_started)
        {
            if (_time < 3.6) return;
            _started = true;
            ShowLine();
            return;
        }
        _lineTime += delta;
        if (_hud.DialogRevealed) _readTime += delta;
        if (edge && _lineTime >= 0.25 && !_hud.DialogRevealed)
        {
            _hud.RevealDialogNow();
            _readTime = 0;
        }
        else if (_lineTime >= 0.25 && _hud.DialogRevealed
                 && (edge || _hud.FastForwarding || (_hud.AutoAdvance && _readTime >= 1.4)))
        {
            _line++;
            if (_line == _story.Lines.Length) { _leaving = true; _hud.HideBubble(); }
            else ShowLine();
        }
    }

    private void ShowLine()
    {
        _lineTime = _readTime = 0;
        _hud.ShowDialog(_story.Id == "mina" ? Hud.LineKind.Mina : Hud.LineKind.Other,
            _story.Lines[_line], _story.Face, otherName: _story.Name);
    }

    public override void _Draw()
    {
        float alpha = _leaving ? 1 - Mathf.Clamp((float)(_exitTime / 0.6), 0, 1)
            : Mathf.Clamp((float)(_time / 0.6), 0, 1);
        float scale = 384f / _background.GetWidth();
        var size = _background.GetSize() * scale;
        DrawTextureRect(_background, new Rect2(new Vector2(0, 108) - new Vector2(0, size.Y * 0.5f), size), false,
            new Color(1, 1, 1, alpha));
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, 1280, 72), new Color(0.025f, 0.03f, 0.035f, alpha));
        DrawRect(new Rect2(0, 516, 1280, 204), new Color(0.025f, 0.03f, 0.035f, alpha));
        UiKit.Text(this, UiKit.Mono, new Vector2(56, 22), "REAL REALM", 24, new Color(0.91f, 0.96f, 1, alpha));
        UiKit.Text(this, UiKit.Zen, new Vector2(994, 26), $"{_story.Name} / 本当の景色", 18, new Color(0.91f, 0.96f, 1, alpha));
        float open = Mathf.Clamp((float)((_time - 0.6) / 1.4), 0, 1);
        if (_story.Id == "mina")
        {
            var bodySize = _face.GetSize() * (453f / _face.GetHeight());
            DrawTextureRect(_face, new Rect2(new Vector2(640, 290) - bodySize * 0.5f, bodySize), false,
                new Color(1, 1, 1, alpha * open));
            UiKit.EndDesign(this);
            return;
        }
        var faceSize = _face.GetSize() * (320f / _face.GetHeight());
        DrawTextureRect(_face, new Rect2(new Vector2(242, 353) - faceSize * 0.5f, faceSize), false,
            new Color(1, 1, 1, alpha * open));
        DrawRect(new Rect2(420, 213, 484, 118), new Color(0.035f, 0.065f, 0.08f, alpha * open * 0.82f));
        DrawLine(new Vector2(420, 213), new Vector2(420, 331), new Color(0.96f, 0.86f, 0.79f, alpha * open), 2);
        UiKit.Text(this, UiKit.Zen, new Vector2(448, 231), "誰にも見せなかった、最初の言葉。", 20, new Color(0.85f, 0.91f, 0.94f, alpha * open));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(448, 273), _story.Quote, 28, new Color(1, 0.93f, 0.87f, alpha * open));
        UiKit.EndDesign(this);
    }

    private void Restore()
    {
        if (_restored) return;
        _restored = true;
        if (IsInstanceValid(_world)) _world.ProcessMode = _worldMode;
        if (IsInstanceValid(_game)) _game.ProcessMode = _gameMode;
        if (IsInstanceValid(_hud))
        {
            _hud.HoldBubble = false;
            _hud.HideBubble();
            _hud.SetCinematicMode(false);
            _hud.SuppressCallouts = _suppressed;
        }
    }

    public override void _ExitTree() => Restore();
}
