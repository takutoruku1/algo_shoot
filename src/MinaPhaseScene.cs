using Godot;
using System;

public partial class MinaPhaseScene : Node2D
{
    private readonly record struct Line(Hud.LineKind Kind, string Text, string Face = "");
    private static Line M(string text, bool tears = false)
        => new(Hud.LineKind.Mina, text, tears ? "res://char/mina_tears.png" : "res://char/mina_worried.png");
    private static Line P(string text) => new(Hud.LineKind.Companion, text);

    private static Line[] Dialogue(Job job, int phase)
    {
        string[] replies = (job, phase) switch
        {
            (Job.Melee, 1) => new[] {
                "それ、あたしが言えなかった分でしょ。",
                "でも今、言える。ミナ、迎えに来た。",
            },
            (Job.Melee, 2) => new[] {
                "仕事を残して帰るなんて、あたしにはできないって思ってた。",
                "でも今は、明日でいいって言える。休んでも、あたしはあたしだった。",
                "ミナに会いたいから。働いてほしいからじゃない。",
            },
            (Job.Melee, 3) => new[] {
                "また、言いたいことを消したの？",
                "あたしも、好きって言えないまま見送った。ミナの言葉は、ここで聞きたい。",
                "うん。まとまってなくていい。待ってる。",
            },
            (Job.Melee, 4) => new[] {
                "ここにいる。返事、遅くなってごめん。",
                "うん。それでも、一緒に帰りたい。",
                "聞こえた。今度は、あたしが迎えに行く。",
            },
            (Job.Heal, 1) => new[] {
                "あたしが飲みこんだ言葉も、そこにあるんだね。",
                "ひとりで持たせて、ごめん。今度は、あたしにも聞かせて。",
            },
            (Job.Heal, 2) => new[] {
                "途中で休んだら、好きが消えるって思ってた。",
                "消えなかったよ。何もしない日にも、好きだった。",
                "ミナだから、会いたいの。何かしてくれなくても。",
            },
            (Job.Heal, 3) => new[] {
                "助けて、って。書いたままにしてみない？",
                "あたしも、分かんないって言うのが怖かった。でも、聞いてくれる人がいたよ。",
                "うん。あたし、最後まで聞くから。",
            },
            (Job.Heal, 4) => new[] {
                "ここだよ。手、離さない。揺れても、つかみ直す。",
                "いいよ。今度は、あたしの隣で休んで。",
                "聞こえたよ。待ってて。いま、そっちに行くね。",
            },
            (Job.Magic, 1) => new[] {
                "言えなかった言葉を、全部ひとりで預かってたのね。",
                "今日は、あんたの番。聞く側は、わたしがやる。",
            },
            (Job.Magic, 2) => new[] {
                "人が減るたび、わたしに価値がなくなった気がしてた。",
                "でも、好きな本の話をしたら、次に話したいことができた。",
                "何件救えたかじゃない。ミナ、あんたに会いに来たの。",
            },
            (Job.Magic, 3) => new[] {
                "助けてって書いて、また消したの？",
                "仕事の話、してない。あんたの話を聞いてる。",
                "見られるのが怖いのは知ってる。だから、目をそらさない。",
            },
            (Job.Magic, 4) => new[] {
                "ここにいるわ。目、そらしてないでしょ。",
                "できることの一覧、もう要らない。一緒に帰る話をしてるの。",
                "聞こえた。最後の壁、開けるわよ。",
            },
            (_, 1) => new[] {
                "ひとりで、ここまで抱えていたんだね。",
                "今度はミナの言葉を聞きに来た。",
            },
            (_, 2) => new[] {
                "休みたいって言っても、ここにいていい。",
                "何もできない日も、ミナと話したい。",
                "会いたかったから。報告を待っていたんじゃない。",
            },
            (_, 3) => new[] {
                "消した言葉を、もう一度聞かせて。",
                "報告じゃなくていい。ミナが言いたかったことを。",
                "うん。急がなくていい。ここで待っている。",
            },
            _ => new[] {
                "ここにいる。ちゃんと聞こえているよ。",
                "それでも、一緒に帰ろう。",
                "聞こえた。いま、迎えに行く。",
            },
        };
        return phase switch
        {
            1 => new[] {
                M("届かなかった言葉が、まだ……わたくしの中に。"),
                P(replies[0]), P(replies[1]),
                M("……返事を、いただく側は。慣れて、おりません。"),
            },
            2 => new[] {
                M("期待に応えなければ……ここに、いられません。"),
                P(replies[0]), P(replies[1]),
                M("……では、どうして。"), P(replies[2]),
            },
            3 => new[] {
                P(replies[0]),
                M("……報告に、不要なことでしたので。"),
                P(replies[1]),
                M("……消さずに、伝えても……？", true), P(replies[2]),
            },
            _ => new[] {
                M("……声が、こんなに近くに。", true), P(replies[0]),
                M("戻っても。もう、前のようには、祓えないかもしれません。", true), P(replies[1]),
                M(job == Job.Tank ? "……ご主人様。" : $"……{Jobs.Get(job).CharacterName}さん。", true),
                M("わたくしを……助けて、ください。", true), P(replies[2]),
            },
        };
    }

    private Hud _hud = null!;
    private Node _world = null!;
    private GameManager _game = null!;
    private ProcessModeEnum _worldMode, _gameMode;
    private Action _completed = null!;
    private Texture2D _background = null!, _costume = null!;
    private Job _job;
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
    // 違うキャラクターの返答を未読のまま飛ばさないよう、既読は相手ごとに記録する。
    private readonly FilmSkip _skip = new();
    private string FilmId => $"mina_phase_{_phase}_{Jobs.Get(_job).CharacterId}";

    public static void Play(Hud hud, Node world, int phase, Action completed)
        => hud.AddChild(new MinaPhaseScene
        {
            Name = "MinaPhaseScene", ZIndex = -10, _hud = hud, _world = world,
            _phase = phase, _completed = completed,
        });

    public override void _Ready()
    {
        AddToGroup("mina_phase_scene");
        _game = GetNode<GameManager>("/root/Game");
        _job = _game.SelectedJob;
        _lines = Dialogue(_job, _phase);
        _background = GD.Load<Texture2D>(BossMina.PhaseBackground(_phase));
        _costume = GD.Load<Texture2D>(BossMina.CostumePath(_phase, "idle"));
        _worldMode = _world.ProcessMode;
        _world.ProcessMode = ProcessModeEnum.Disabled;
        _gameMode = _game.ProcessMode;
        _game.ProcessMode = ProcessModeEnum.Disabled;
        _suppressed = _hud.SuppressCallouts;
        _hud.SuppressCallouts = true;
        _hud.HoldBubble = true;
        _hud.HideBubble();
        _hud.HideSpellCard();
        _hud.SetCinematicMode(true, dialogueBubble: true);
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
        var kind = line.Kind == Hud.LineKind.Companion && _job == Job.Tank ? Hud.LineKind.Boy : line.Kind;
        _hud.ShowDialog(kind, line.Text, line.Face);
    }

    public override void _Draw()
    {
        float alpha = _leaving ? 1 - Mathf.Clamp((float)(_exitTime / 0.55), 0, 1)
            : Mathf.Clamp((float)(_time / 0.55), 0, 1);
        float scale = Mathf.Max(384f / _background.GetWidth(), 216f / _background.GetHeight());
        var size = _background.GetSize() * scale;
        DrawTextureRect(_background, new Rect2((new Vector2(384, 216) - size) * 0.5f, size), false,
            new Color(0.6f, 0.6f, 0.65f, alpha));
        var bodySize = _costume.GetSize() * (136f / _costume.GetHeight());
        DrawTextureRect(_costume, new Rect2(new Vector2(192, 87) - bodySize * 0.5f, bodySize), false,
            new Color(1, 1, 1, alpha));
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
