using Godot;
using System;

public partial class AkariDraftScene : Node2D
{
    private static readonly string[] Lines =
    {
        "……それ、送ってない。誰にも、見せてないのに。",
        "一緒に入った会社なのに。好きって、一度も言えないまま……。",
        "でも、おめでとうだけは……嘘に、したくなかった。",
    };
    private const string Face = "res://char/v3/akari_face_cry.png";
    private Hud _hud = null!;
    private Node _world = null!;
    private GameManager _game = null!;
    private ProcessModeEnum _worldMode, _gameMode;
    private Action _completed = null!;
    private Texture2D _background = null!, _face = null!;
    private bool _suppressed, _held, _started, _leaving, _restored;
    private int _line;
    private double _time, _lineTime, _readTime, _exitTime;

    public static void Play(Hud hud, Node world, Action completed)
        => hud.AddChild(new AkariDraftScene
        {
            Name = "AkariDraftScene", ZIndex = -10,
            _hud = hud, _world = world, _completed = completed,
        });

    public override void _Ready()
    {
        AddToGroup("akari_draft");
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
        _hud.SetCinematicMode(true);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Audio.Instance?.StopMusic(0.8f);
        _background = GD.Load<Texture2D>("res://char/bg2/boss/akari_v1.png");
        _face = GD.Load<Texture2D>(Face);
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
            if (_time < 4.8) return;
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
            if (_line == Lines.Length) { _leaving = true; _hud.HideBubble(); }
            else ShowLine();
        }
    }

    private void ShowLine()
    {
        _lineTime = _readTime = 0;
        _hud.ShowDialog(Hud.LineKind.Other, Lines[_line], Face, otherName: "あかり");
    }

    public override void _Draw()
    {
        float alpha = _leaving ? 1 - Mathf.Clamp((float)(_exitTime / 0.6), 0, 1)
            : Mathf.Clamp((float)(_time / 0.6), 0, 1);
        DrawTextureRect(_background, new Rect2(0, 0, 384, 216), false, new Color(0.28f, 0.32f, 0.36f, alpha));
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, 1280, 64), new Color(0.025f, 0.03f, 0.035f, alpha));
        DrawRect(new Rect2(0, 516, 1280, 204), new Color(0.025f, 0.03f, 0.035f, alpha));
        var faceSize = _face.GetSize() * (346f / _face.GetHeight());
        DrawTextureRect(_face, new Rect2(new Vector2(310, 328) - faceSize * 0.5f, faceSize), false,
            new Color(1, 1, 1, alpha));
        float open = Mathf.Clamp((float)((_time - 0.7) / 1.7), 0, 1);
        for (int i = 0; i < 5; i++)
        {
            float x = 588 + (i - 2) * open * 68;
            float y = 142 + i * 7 + open * open * (i % 2 == 0 ? -180 : 200);
            UiKit.Box(this, new Rect2(x, y, 524, 306), new Color(0.10f, 0.16f, 0.19f, alpha * (1 - open)), 6,
                new Color(0.7f, 0.8f, 0.9f, alpha * (1 - open) * 0.4f), 1);
        }
        var paper = new Color(0.94f, 0.96f, 0.95f, alpha * open);
        UiKit.Box(this, new Rect2(570, 144, 574, 312), paper, 6);
        UiKit.Text(this, UiKit.Zen, new Vector2(608, 164), "下書き", 22, new Color(0.22f, 0.33f, 0.37f, alpha * open));
        UiKit.Text(this, UiKit.Zen, new Vector2(1040, 166), "未送信", 19, new Color(0.38f, 0.45f, 0.46f, alpha * open));
        DrawLine(new Vector2(608, 208), new Vector2(1106, 208), new Color(0.3f, 0.45f, 0.49f, alpha * open * 0.25f));
        string[] draft = { "おめでとう", "ほんとだよ", "元気でね" };
        int remaining = Mathf.Clamp((int)((_time - 2) * 7), 0, 14);
        for (int i = 0; i < draft.Length; i++)
        {
            int count = Mathf.Clamp(remaining, 0, draft[i].Length);
            remaining -= draft[i].Length;
            UiKit.Text(this, UiKit.Zen, new Vector2(612, 232 + i * 58), draft[i][..count], 34,
                new Color(0.11f, 0.22f, 0.26f, alpha * open));
        }
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
