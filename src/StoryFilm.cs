using Godot;
using System;
using System.Collections.Generic;

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
    protected Dictionary<int, string> _shotImages = new();
    private Texture2D _atlas = null!;

    // 回想BGMを引くためのキー（"rei"/"akari"/"koharu"/"mina"）。_storyName の小文字＝
    //   派生側で別に持たせず、ここで一意に導出する（表示名と選曲キーがズレない）。
    private string _storyKey => _storyName.ToLowerInvariant();

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
        // 回想の曲へクロスフェードする（2026-09-14②。ユーザー実機指摘「ボス戦の回想シーンに
        //   BGM入ってないよ」への対応）。ここは以前 StopMusic(0.7f) で**無音に落とすだけ**だった。
        //   ・memory（戦闘中・モノクロ）＝過去の傷。戦闘曲から翳りのある曲へ。
        //   ・aftermath（撃破後・カラー）＝癒えた未来。温度の戻る曲へ。
        //   StoryBgm() が null を返す枠（＝ミナの aftermath）は**意図的な無音**なので、
        //   従来どおり StopMusic して沈黙のまま通す（直後の Final が「無音→挿入歌」を持つため。
        //   詳細は Audio.StoryBgm() のコメントと BGM/candidates.md ㉖）。
        var storyBgm = Audio.Instance?.StoryBgm(_storyKey, _aftermath);
        if (storyBgm != null) Audio.Instance?.Music(storyBgm, 0.7f);
        else Audio.Instance?.StopMusic(0.7f);
        _held = Pad.AdvanceHeld();
        _shot = _lines[0].Shot;
        _atlas = GD.Load<Texture2D>(_atlasPath);
        _grade = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/story_film.gdshader") };
        SetFrame("scene", _shot);
        SetFrame("previous", _shot);
        _grade.SetShaderParameter("grayscale", !_aftermath);
        AddChild(new TextureRect
        {
            Texture = _atlas,
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
            SetFrame("previous", _shot);
            _shot = line.Shot;
            SetFrame("scene", _shot);
            _blendT = 0;
            _shotT = 0;
        }
        _lineT = _readT = 0;
        if (line.Speaker.Length == 0) _hud.ShowMessage(line.Text);
        else _hud.ShowDialog(Hud.LineKind.Other, line.Text, otherName: line.Speaker);
    }

    private void SetFrame(string prefix, int shot)
    {
        bool fullFrame = _shotImages.TryGetValue(shot, out string? path);
        var texture = fullFrame ? GD.Load<Texture2D>(path!) : _atlas;
        var region = fullFrame ? new Vector4(0, 0, 1, 1)
            : new Vector4((shot % 2) / 2f, (shot / 2) / (float)_atlasRows, 0.5f, 1f / _atlasRows);
        _grade.SetShaderParameter(prefix + "_texture", texture);
        _grade.SetShaderParameter(prefix + "_region", region);
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

        // 回想明けの曲の担当（2026-09-14②）。**memory と aftermath で違う**ので、ここでは何もしない:
        //   ・memory（戦闘へ戻る）＝呼び出し側の completed: が各ボス曲を張り直している
        //     （BossRei/BossAkari/BossKoharu/BossMina の4箇所）。ここで触ると同フレーム帯で Music() が
        //     二重に走り、以前直した「ボス曲が消える」事故（_musicFadeTween の Kill 漏れ）と同型の
        //     競合を招く。**触らない**のが正しい。
        //   ・aftermath（撃破後のクリア会話へ戻る）＝**回想曲をそのまま鳴らし続ける**。
        //     クリア会話はミナが独白する感情の後日談（例: StageRei.Clear の6行）で、
        //     回想 aftermath と**同じ「癒えたあと」の一続きの場面**なので、ここで切ると
        //     会話が無音に落ちる（＝ユーザー指摘の無音がクリア会話に残る）。
        //     曲は次のシーン（Hub / Final）の _Ready が張る Music() が自然に上書きする。
    }

    public override void _ExitTree() => Restore();
}
