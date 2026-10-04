using Godot;
using System;

public partial class MinaPhaseScene : Node2D
{
    private readonly record struct Line(Hud.LineKind Kind, string Text, string Face = "");
    private static Line M(string text, bool tears = false)
        => new(Hud.LineKind.Mina, text, tears ? "res://char/mina_tears.png" : "res://char/mina_worried.png");
    private static Line P(string text) => new(Hud.LineKind.Companion, text);

    private static Line[] Dialogue(Job job, int phase) => (job, phase) switch
    {
        (Job.Melee, 1) => new[]
        {
                new Line(Hud.LineKind.Boy, "ミナの周りの壁を、外側からほどこう。僕が位置を伝える。本人への呼びかけは、君に任せる。"),
                new Line(Hud.LineKind.Companion, "分かった。今度は、こっちから会いに行く。"),
                new Line(Hud.LineKind.Mina, "届かなかった言葉が、まだ……わたくしの中に。"),
                new Line(Hud.LineKind.Companion, "それ、あたしが言えなかった分でしょ。"),
                new Line(Hud.LineKind.Companion, "でも今、言える。ミナ、迎えに来た。"),
                new Line(Hud.LineKind.Mina, "……返事をいただく側は、慣れておりません。あの方にも、まだ届けられるのでしょうか。"),
        },
        (Job.Melee, 2) => new[]
        {
                new Line(Hud.LineKind.Mina, "期待に応えなければ……ここに、いられません。"),
                new Line(Hud.LineKind.Companion, "仕事を残して帰るなんて、あたしにはできないって思ってた。"),
                new Line(Hud.LineKind.Companion, "でも今は、明日でいいって言える。休んでも、あたしはあたしだった。"),
                new Line(Hud.LineKind.Mina, "……では、どうして。"),
                new Line(Hud.LineKind.Companion, "ミナに会いたいから。働いてほしいからじゃない。"),
                new Line(Hud.LineKind.Mina, "わたくしも……あの方と、ご用のない日にお話がしたかったのです。"),
        },
        (Job.Melee, 3) => new[]
        {
                new Line(Hud.LineKind.Companion, "また、言いたいことを消したの？"),
                new Line(Hud.LineKind.Mina, "……報告に、不要なことでしたので。"),
                new Line(Hud.LineKind.Companion, "あたしも、好きって言えないまま見送った。ミナの言葉は、ここで聞きたい。"),
                new Line(Hud.LineKind.Mina, "……帰れたら、あの方にも、消さずに伝えてよいのでしょうか。"),
                new Line(Hud.LineKind.Companion, "うん。まとまってなくていい。待ってる。"),
        },
        (Job.Melee, 4) => new[]
        {
                new Line(Hud.LineKind.Mina, "……声が、こんなに近くに。"),
                new Line(Hud.LineKind.Companion, "ここにいる。返事、遅くなってごめん。"),
                new Line(Hud.LineKind.Mina, "戻っても。もう、前のようには、祓えないかもしれません。"),
                new Line(Hud.LineKind.Companion, "うん。それでも、一緒に帰りたい。"),
                new Line(Hud.LineKind.Mina, "あかりさん。わたくし、帰りたいです。"),
                new Line(Hud.LineKind.Mina, "わたくしを……助けて、ください。"),
                new Line(Hud.LineKind.Companion, "聞こえた。今度は、あたしが迎えに行く。"),
        },
        (Job.Heal, 1) => new[]
        {
                new Line(Hud.LineKind.Boy, "ミナの周りの壁を、外側からほどこう。僕が位置を伝える。本人への呼びかけは、君に任せる。"),
                new Line(Hud.LineKind.Companion, "分かった。今度は、こっちから会いに行く。"),
                new Line(Hud.LineKind.Mina, "届かなかった言葉が、まだ……わたくしの中に。"),
                new Line(Hud.LineKind.Companion, "あたしが飲みこんだ言葉も、そこにあるんだね。"),
                new Line(Hud.LineKind.Companion, "ひとりで持たせて、ごめん。今度は、あたしにも聞かせて。"),
                new Line(Hud.LineKind.Mina, "……返事をいただく側は、慣れておりません。あの方にも、まだ届けられるのでしょうか。"),
        },
        (Job.Heal, 2) => new[]
        {
                new Line(Hud.LineKind.Mina, "期待に応えなければ……ここに、いられません。"),
                new Line(Hud.LineKind.Companion, "途中で休んだら、好きが消えるって思ってた。"),
                new Line(Hud.LineKind.Companion, "消えなかったよ。何もしない日にも、好きだった。"),
                new Line(Hud.LineKind.Mina, "……では、どうして。"),
                new Line(Hud.LineKind.Companion, "ミナだから、会いたいの。何かしてくれなくても。"),
                new Line(Hud.LineKind.Mina, "わたくしも……あの方と、ご用のない日にお話がしたかったのです。"),
        },
        (Job.Heal, 3) => new[]
        {
                new Line(Hud.LineKind.Companion, "助けて、って。書いたままにしてみない？"),
                new Line(Hud.LineKind.Mina, "……報告に、不要なことでしたので。"),
                new Line(Hud.LineKind.Companion, "あたしも、分かんないって言うのが怖かった。でも、聞いてくれる人がいたよ。"),
                new Line(Hud.LineKind.Mina, "……帰れたら、あの方にも、消さずに伝えてよいのでしょうか。"),
                new Line(Hud.LineKind.Companion, "うん。あたし、最後まで聞くから。"),
        },
        (Job.Heal, 4) => new[]
        {
                new Line(Hud.LineKind.Mina, "……声が、こんなに近くに。"),
                new Line(Hud.LineKind.Companion, "ここだよ。手、離さない。揺れても、つかみ直す。"),
                new Line(Hud.LineKind.Mina, "戻っても。もう、前のようには、祓えないかもしれません。"),
                new Line(Hud.LineKind.Companion, "いいよ。今度は、あたしの隣で休んで。"),
                new Line(Hud.LineKind.Mina, "こはるさん。わたくし、帰りたいです。"),
                new Line(Hud.LineKind.Mina, "わたくしを……助けて、ください。"),
                new Line(Hud.LineKind.Companion, "聞こえたよ。待ってて。いま、そっちに行くね。"),
        },
        (Job.Magic, 1) => new[]
        {
                new Line(Hud.LineKind.Boy, "右の壁が薄くなった。次はそこだ。レイさん、足元にも気をつけて。"),
                new Line(Hud.LineKind.Companion, "了解。……ミナ。届かなかった言葉、まだ抱えてるのね。"),
                new Line(Hud.LineKind.Mina, "ご主人様へ、お届けするはずでした。わたくしの声まで、混ざってしまって……。"),
                new Line(Hud.LineKind.Companion, "混ざったんじゃない。あんたの言葉も、そこにあるのよ。聞かせて。"),
                new Line(Hud.LineKind.Mina, "聞いても……、お困りに、なるのでは。"),
                new Line(Hud.LineKind.Companion, "困ったら、一緒に考える。黙って消されるほうが、わたしは嫌。"),
        },
        (Job.Magic, 2) => new[]
        {
                new Line(Hud.LineKind.Mina, "期待に応えなければ……ここに、いられません。"),
                new Line(Hud.LineKind.Companion, "誰に、そう言われたの。"),
                new Line(Hud.LineKind.Mina, "……ご主人様には、一度も。でも、わたくしは、そのために作られたので。"),
                new Line(Hud.LineKind.Companion, "仕事の話だけしてきたの？　名前を呼んだり、何でもない話をしたりは？"),
                new Line(Hud.LineKind.Mina, "……ありました。名付けのときなど、センスに問題がありましたので。"),
                new Line(Hud.LineKind.Companion, "ほら。いまの話、もっと聞きたいわ。役に立つ報告より、ずっと。"),
                new Line(Hud.LineKind.Mina, "ご報告することがなくても、話しかけたい日が、ありました。"),
                new Line(Hud.LineKind.Companion, "どんなとき？"),
                new Line(Hud.LineKind.Mina, "皆さまを送り届けたあと。呼びかけて、やめたことが……。"),
        },
        (Job.Magic, 3) => new[]
        {
                new Line(Hud.LineKind.Mina, "休みたい、という一行を……報告に、書き直しました。"),
                new Line(Hud.LineKind.Companion, "わたしも、疲れたって言う前に笑ってた。言い直すの、やめるのが怖いのよね。"),
                new Line(Hud.LineKind.Mina, "……はい。できなかった日の声を、どう届ければよいのか。"),
                new Line(Hud.LineKind.Companion, "いま、そのまま聞いてる。"),
                new Line(Hud.LineKind.Boy, "レイさん。僕にも聞こえた。ミナが戻れる場所は、ここにあるって伝えたい。"),
                new Line(Hud.LineKind.Companion, "分かった。……でも、わたしの言葉で言うわよ。"),
                new Line(Hud.LineKind.Boy, "うん。お願い。"),
                new Line(Hud.LineKind.Companion, "ミナ。戻る席はあるわ。帰って、あの人に小言の一つも言ってやりなさい。"),
                new Line(Hud.LineKind.Mina, "……一つでは、済まないかもしれません。"),
                new Line(Hud.LineKind.Companion, "なら、なおさら帰らなきゃね。"),
        },
        (Job.Magic, 4) => new[]
        {
                new Line(Hud.LineKind.Boy, "最後の壁が見えた。その奥はリアルレルムだ。ミナ自身の願いが、まだ残ってる。"),
                new Line(Hud.LineKind.Companion, "ミナ。最後に一つ、聞かせて。あんたは、どうしたい？"),
                new Line(Hud.LineKind.Mina, "戻っても、前のようには祓えないかもしれません。"),
                new Line(Hud.LineKind.Companion, "うん。それも聞いた。……あんたは？"),
                new Line(Hud.LineKind.Mina, "帰りたい。ご報告ではなく、お話をするために。"),
                new Line(Hud.LineKind.Companion, "そう。待ってたのは、その返事。"),
                new Line(Hud.LineKind.Mina, "レイさん。わたくしを……助けてください。"),
                new Line(Hud.LineKind.Companion, "聞こえた。最後の壁、開けるわよ。"),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

    private static Line[] ReiReplayDialogue(int phase) => phase switch
    {
        1 => new[]
        {
                new Line(Hud.LineKind.Boy, "ミナの周りの壁を、外側からほどこう。僕が位置を伝える。本人への呼びかけは、君に任せる。"),
                new Line(Hud.LineKind.Companion, "分かった。今度は、こっちから会いに行く。"),
                new Line(Hud.LineKind.Mina, "届かなかった言葉が、まだ……わたくしの中に。"),
                new Line(Hud.LineKind.Companion, "言えなかった言葉を、全部ひとりで預かってたのね。"),
                new Line(Hud.LineKind.Companion, "今日は、あんたの番。聞く側は、わたしがやる。"),
                new Line(Hud.LineKind.Mina, "……返事をいただく側は、慣れておりません。あの方にも、まだ届けられるのでしょうか。"),
        },
        2 => new[]
        {
                new Line(Hud.LineKind.Mina, "期待に応えなければ……ここに、いられません。"),
                new Line(Hud.LineKind.Companion, "人が減るたび、わたしに価値がなくなった気がしてた。"),
                new Line(Hud.LineKind.Companion, "でも、好きな本の話をしたら、次に話したいことができた。"),
                new Line(Hud.LineKind.Mina, "……では、どうして。"),
                new Line(Hud.LineKind.Companion, "何件救えたかじゃない。ミナ、あんたに会いに来たの。"),
                new Line(Hud.LineKind.Mina, "わたくしも……あの方と、ご用のない日にお話がしたかったのです。"),
        },
        3 => new[]
        {
                new Line(Hud.LineKind.Companion, "助けてって書いて、また消したの？"),
                new Line(Hud.LineKind.Mina, "……報告に、不要なことでしたので。"),
                new Line(Hud.LineKind.Companion, "仕事の話、してない。あんたの話を聞いてる。"),
                new Line(Hud.LineKind.Mina, "……帰れたら、あの方にも、消さずに伝えてよいのでしょうか。"),
                new Line(Hud.LineKind.Companion, "見られるのが怖いのは知ってる。だから、目をそらさない。"),
        },
        4 => new[]
        {
                new Line(Hud.LineKind.Mina, "……声が、こんなに近くに。"),
                new Line(Hud.LineKind.Companion, "ここにいるわ。目、そらしてないでしょ。"),
                new Line(Hud.LineKind.Mina, "戻っても。もう、前のようには、祓えないかもしれません。"),
                new Line(Hud.LineKind.Companion, "できることの一覧、もう要らない。一緒に帰る話をしてるの。"),
                new Line(Hud.LineKind.Mina, "レイさん。わたくし、帰りたいです。"),
                new Line(Hud.LineKind.Mina, "わたくしを……助けて、ください。"),
                new Line(Hud.LineKind.Companion, "聞こえた。最後の壁、開けるわよ。"),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

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
        _lines = _job == Job.Magic && _game.IsFinalCleared ? ReiReplayDialogue(_phase) : Dialogue(_job, _phase);
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
            && (edge || _hud.FastForwarding || (_hud.AutoAdvanceReady && _readTime >= 1.4)))
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
        _hud.ShowDialog(line.Kind, line.Text, line.Face);
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
