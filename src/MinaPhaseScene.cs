using Godot;
using System;

public partial class MinaPhaseScene : Node2D
{
    private readonly record struct Line(string Speaker, string Text, string Face);
    private const string MinaFace = "res://char/mina_worried.png";
    private const string AkariFace = "res://char/v3/akari_face.png";
    private const string KoharuFace = "res://char/v3/koharu_face.png";
    private const string ReiFace = "res://char/v3/rei_face.png";
    private static readonly Line[][] Dialogue =
    {
        new[]
        {
            new Line("ミナ", "届かなかった言葉が、まだ……わたくしの中に。", MinaFace),
            new Line("あかり", "それ、あたしが言えなかった分でしょ。", AkariFace),
            new Line("あかり", "でも今、言える。ミナ、迎えに来た。", AkariFace),
            new Line("ミナ", "……返事を、いただく側は。慣れて、おりません。", MinaFace),
        },
        new[]
        {
            new Line("ミナ", "期待に応えなければ……ここに、いられません。", MinaFace),
            new Line("こはる", "途中で休んだら、好きが消えるって思ってた。", KoharuFace),
            new Line("こはる", "消えなかったよ。何もしない日にも、好きだった。", KoharuFace),
            new Line("こはる", "ミナもそう。役に立つから来たんじゃない。", KoharuFace),
            new Line("ミナ", "……では、どうして。", MinaFace),
        },
        new[]
        {
            new Line("レイ", "助けてって書いて、また消したの？", ReiFace),
            new Line("ミナ", "……報告に、不要なことでしたので。", MinaFace),
            new Line("レイ", "仕事の話、してない。あんたの話を聞いてる。", ReiFace),
            new Line("レイ", "見られるのが怖いのは知ってる。だから、目をそらさない。", ReiFace),
            new Line("ミナ", "……消さずに、伝えても……？", "res://char/mina_tears.png"),
        },
        new[]
        {
            new Line("ミナ", "三人の声が、もう、遠くない……。", "res://char/mina_tears.png"),
            new Line("あかり", "ここにいる。返事、遅くなってごめん。", AkariFace),
            new Line("こはる", "手、離さないよ。揺れても、つかみ直す。", KoharuFace),
            new Line("レイ", "その先は、あんたの言葉で。", ReiFace),
            new Line("ミナ", "……ご主人様。あかりさん。こはるさん。レイさん。", "res://char/mina_tears.png"),
            new Line("ミナ", "わたくしを……助けて、ください。", "res://char/mina_tears.png"),
            new Line("レイ", "聞こえた。最後の壁、みんなで開けるよ。", ReiFace),
        },
    };

    private Hud _hud = null!;
    private Node _world = null!;
    private GameManager _game = null!;
    private ProcessModeEnum _worldMode, _gameMode;
    private Action _completed = null!;
    private Texture2D _background = null!, _costume = null!;
    private Texture2D? _face;
    private Texture2D[] _friends = Array.Empty<Texture2D>();
    private Line[] _lines = null!;
    private int _phase, _line;
    private double _time, _lineTime, _readTime, _exitTime;
    private bool _held, _started, _leaving, _restored, _suppressed;
    // StoryFilm と同型の問題（2026-09-22）：_Draw が全要素を alpha 一本でフェードするので、
    //   入り／明けの 0.55 秒はレターボックスの帯ごと半透明になり背後の StageBackground が透ける。
    //   一段奥に不透明の黒板を敷いて塞ぐ（CutsceneBackdrop のコメント参照）。
    private CutsceneBackdrop _backdrop = null!;
    // 一度見たフェーズ間カットシーンのスキップ（2026-09-22 ユーザー要望）。StoryFilm と同じ作法。
    //   FINAL はリトライの多い面で、4本のカットシーンを毎回読まされるのが一番きつい箇所。
    //   キーはフェーズごと（"mina_phase_1" … "mina_phase_4"）＝到達していないフェーズは飛ばせない。
    private readonly FilmSkip _skip = new();
    private string FilmId => $"mina_phase_{_phase}";

    public static void Play(Hud hud, Node world, int phase, Action completed)
        => hud.AddChild(new MinaPhaseScene
        {
            Name = "MinaPhaseScene", ZIndex = -10, _hud = hud, _world = world,
            _phase = phase, _completed = completed,
        });

    public override void _Ready()
    {
        AddToGroup("mina_phase_scene");
        _lines = Dialogue[_phase - 1];
        _background = GD.Load<Texture2D>(BossMina.PhaseBackground(_phase));
        _costume = GD.Load<Texture2D>(BossMina.CostumePath(_phase, "idle"));
        if (_phase == 4) _friends = new[] { GD.Load<Texture2D>(AkariFace), GD.Load<Texture2D>(KoharuFace), GD.Load<Texture2D>(ReiFace) };
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
        _hud.SetCinematicMode(true);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        _held = Pad.AdvanceHeld();
        // 本体より先に、その一段奥へ黒板を立ち上げる（ZIndex はこのノードの1つ下）。
        _backdrop = CutsceneBackdrop.Attach(_hud, ZIndex);
        _skip.Begin(_game, FilmId);
        GD.Print($"[MinaPhase] {_phase} start" + (_skip.Available ? " (skippable)" : ""));
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
            if (_exitTime < 0.55) return;
            Restore();
            GD.Print($"[MinaPhase] {_phase} complete");
            _completed();
            QueueFree();
            return;
        }
        // 既読カットシーンの長押しスキップ。立ち上げの 0.7 秒の間も受け付ける。
        if (_skip.Update(delta)) { BeginLeave(); return; }
        if (!_started)
        {
            if (_time < 0.7) return;
            _started = true;
            Audio.Instance?.PlaySpell();
            ShowLine();
            return;
        }
        _lineTime += delta;
        if (_hud.DialogRevealed) _readTime += delta;
        if (edge && _lineTime >= 0.2 && !_hud.DialogRevealed)
        {
            _hud.RevealDialogNow();
            _readTime = 0;
        }
        else if (_lineTime >= 0.25 && _hud.DialogRevealed
            && (edge || _hud.FastForwarding || (_hud.AutoAdvance && _readTime >= 1.4)))
        {
            _line++;
            if (_line == _lines.Length) BeginLeave();
            else ShowLine();
        }
    }

    // 畳む（最終行を送り切った／既読スキップ）。どちらも同じ明けフェードを通り、
    //   _completed（BossMina.CompletePhaseTransition＝次フェーズの武装）へ必ず戻る。
    private void BeginLeave()
    {
        if (_leaving) return;
        _leaving = true;
        FilmSkip.MarkSeen(_game, FilmId);
        _hud.HideBubble();
    }

    private void ShowLine()
    {
        var line = _lines[_line];
        _lineTime = _readTime = 0;
        _face = line.Speaker == "ミナ" ? null : GD.Load<Texture2D>(line.Face);
        _hud.ShowDialog(Hud.LineKind.Other, line.Text, line.Face, otherName: line.Speaker);
    }

    public override void _Draw()
    {
        float alpha = _leaving ? 1 - Mathf.Clamp((float)(_exitTime / 0.55), 0, 1)
            : Mathf.Clamp((float)(_time / 0.55), 0, 1);
        float scale = Mathf.Max(384f / _background.GetWidth(), 216f / _background.GetHeight());
        var size = _background.GetSize() * scale;
        DrawTextureRect(_background, new Rect2((new Vector2(384, 216) - size) * 0.5f, size), false,
            new Color(0.6f, 0.6f, 0.65f, alpha));
        float slide = 20 * (1 - Mathf.Clamp((float)(_time / 0.7), 0, 1));
        var bodySize = _costume.GetSize() * (136f / _costume.GetHeight());
        DrawTextureRect(_costume, new Rect2(new Vector2(264 + slide, 87) - bodySize * 0.5f, bodySize), false,
            new Color(1, 1, 1, alpha));
        if (_phase == 4)
        {
            for (int i = 0; i < _friends.Length; i++)
            {
                var faceSize = _friends[i].GetSize() * (64f / _friends[i].GetHeight());
                DrawTextureRect(_friends[i], new Rect2(new Vector2(44 + 56 * i, 110) - faceSize * 0.5f, faceSize), false,
                    new Color(1, 1, 1, alpha));
            }
        }
        else if (_face != null)
        {
            var faceSize = _face.GetSize() * (76f / _face.GetHeight());
            DrawTextureRect(_face, new Rect2(new Vector2(98, 100) - faceSize * 0.5f, faceSize), false,
                new Color(1, 1, 1, alpha));
        }
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, 1280, 64), new Color(0.025f, 0.025f, 0.03f, alpha));
        DrawRect(new Rect2(0, 516, 1280, 204), new Color(0.025f, 0.025f, 0.03f, alpha));
        UiKit.Text(this, UiKit.Zen, new Vector2(64, 19), BossMina.PhaseName(_phase), 22,
            new Color(1, 1, 1, alpha));
        // 既読のときだけスキップのヒント（上辺の帯の右端。左端のフェーズ名とはぶつからない）。
        if (!_leaving) _skip.Draw(this);
        UiKit.EndDesign(this);
    }

    private void Restore()
    {
        if (_restored) return;
        _restored = true;
        // 黒板は本体が消えきってから引く＝明けフェードを内側に完全に包む。
        if (IsInstanceValid(_backdrop)) _backdrop.Dismiss();
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
