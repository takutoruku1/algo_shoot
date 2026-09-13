using Godot;
using System;

public partial class StoryFilm : Node2D
{
    protected readonly record struct Line(int Shot, string Time, string Speaker, string Text, double Hold = 0.2);

    protected Hud _hud = null!;
    protected Node _world = null!;
    protected ProcessModeEnum _worldMode;
    private GameManager _game = null!;
    private ProcessModeEnum _gameMode;
    protected Action _completed = null!;
    protected Line[] _lines = null!;
    protected ShaderMaterial _grade = null!;
    protected bool _aftermath, _held, _leaving, _restored, _suppressed;
    protected int _line, _shot;
    protected double _fadeT, _lineT, _readT, _shotT, _blendT;
    protected bool _started;
    protected const double FadeTime = 0.65;

    protected string _atlasPath = "";
    protected string _storyName = "";
    protected int _atlasRows = 3;

    public override void _Ready()
    {
        AddToGroup("storyfilm");
        _worldMode = _world.ProcessMode;
        // BubblePaused alone leaves the boss's BREAK/RECLOSE clock running.
        _world.ProcessMode = ProcessModeEnum.Disabled;
        _game = GetNode<GameManager>("/root/Game");
        _gameMode = _game.ProcessMode;
        _game.ProcessMode = ProcessModeEnum.Disabled;
        _suppressed = _hud.SuppressCallouts;
        _hud.SuppressCallouts = true;
        _hud.HideBubble();
        _hud.HoldBubble = true;
        _hud.SetCinematicMode(true);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        foreach (Node hazard in GetTree().GetNodesInGroup("aoe"))
            if (hazard is AreaStrike) hazard.QueueFree();
        Audio.Instance?.StopMusic(0.7f);
        _held = Pad.AdvanceHeld();
        _shot = _lines[0].Shot;
        _grade = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/story_film.gdshader") };
        _grade.SetShaderParameter("scene_index", _shot);
        _grade.SetShaderParameter("previous_index", _shot);
        _grade.SetShaderParameter("grayscale", !_aftermath);
        _grade.SetShaderParameter("atlas_rows", _atlasRows);
        AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>(_atlasPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Size = new Vector2(384, 216), MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = _grade, ZIndex = -1,
        });
        Modulate = new Color(1, 1, 1, 0);
        GD.Print($"[{_storyName}Story] {(_aftermath ? "aftermath" : "memory")} start");
    }

    public override void _Process(double delta)
    {
        if (Pad.UiBlocked(this)) { _held = true; return; }
        bool held = Pad.AdvanceHeld();
        bool edge = held && !_held;
        _held = held;
        _fadeT += delta;
        _shotT += delta;
        _blendT += delta;
        _grade.SetShaderParameter("motion_time", (float)_shotT);
        _grade.SetShaderParameter("blend_amount", Mathf.Clamp((float)(_blendT / 0.8), 0, 1));
        float fade = Mathf.Clamp((float)(_fadeT / FadeTime), 0, 1);
        Modulate = new Color(1, 1, 1, _leaving ? 1 - fade : fade);
        QueueRedraw();
        if (_leaving)
        {
            if (_fadeT < FadeTime) return;
            Restore();
            GD.Print($"[{_storyName}Story] {(_aftermath ? "aftermath" : "memory")} complete");
            _completed();
            QueueFree();
            return;
        }
        if (!_started)
        {
            if (_fadeT < FadeTime) return;
            _started = true;
            ShowLine();
            return;
        }
        _lineT += delta;
        if (_hud.DialogRevealed) _readT += delta;
        if (edge && _lineT >= 0.2 && !_hud.DialogRevealed)
        {
            _hud.RevealDialogNow();
            _readT = 0;
        }
        else if (_lineT >= _lines[_line].Hold && _hud.DialogRevealed
                 && (edge || _hud.FastForwarding || (_hud.AutoAdvance && _readT >= 1.4)))
        {
            _line++;
            if (_line == _lines.Length)
            {
                _leaving = true;
                _fadeT = 0;
                _hud.HideBubble();
            }
            else ShowLine();
        }
    }

    private void ShowLine()
    {
        var line = _lines[_line];
        if (line.Shot != _shot)
        {
            _grade.SetShaderParameter("previous_index", _shot);
            _shot = line.Shot;
            _grade.SetShaderParameter("scene_index", _shot);
            _blendT = 0;
            _shotT = 0;
        }
        _lineT = _readT = 0;
        if (line.Speaker.Length == 0) _hud.ShowMessage(line.Text);
        else _hud.ShowDialog(Hud.LineKind.Other, line.Text, otherName: line.Speaker);
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, 1280, 64), new Color(0.025f, 0.025f, 0.025f, 0.94f));
        DrawRect(new Rect2(0, 516, 1280, 204), new Color(0.025f, 0.025f, 0.025f, 0.94f));
        UiKit.Text(this, UiKit.Zen, new Vector2(64, 19), _lines[Math.Min(_line, _lines.Length - 1)].Time, 20, Colors.White);
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
