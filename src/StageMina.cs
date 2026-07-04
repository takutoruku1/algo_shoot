using Godot;

// StageMina : FINAL「暴走したミナ」進行。役割反転——少年が操作して、暴走したミナの穢れを撃ち祓う。
//   1: 導入（少年がミナの内側へ）
//   2: ボス出現（BossMina）
//   3: ボス戦（撃破＝穢れを祓う／中で短い邂逅セリフ）
//   4: Final（対話で帰還）へ
public partial class StageMina : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stepTime;
    private double _stageElapsed;   // ステージ全体の経過秒（クリア確定まで・ポーズ中は止まる）。
    private double _lineHold;
    private int _introLine;
    private BossMina _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private bool _zHeld, _zEdge, _startBannerShown;

    private const float SpawnX = 300f;
    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";
    private const string SProud = "res://char/shonen_proud.png";

    // 導入（who: 0=少年 / 1=ミナ / 3=地の文）。
    private static readonly (int who, string text, string face)[] Intro =
    {
        // ① 暴走の状況実況（銀の光が黒く溶ける／悲鳴が流れ込む）は地で説明せず、
        //    "FINAL — 暴走" バナー＋黒く渦巻く暴走ビジュアル＋無音に委ねる。
        //    ミナの声は壊れた断片1行だけ＝言葉が壊れる＝暴走、を見せる（show-don't-tell）。
        // ② 正典 v3（S2）：この声は生前に遺した archive replay の最後の一回——だが**明言しない**。
        //    種明かしは Epilogue に一元化。ここでは“兆候”だけ置く：針飛びのような同語反復（4行目）。
        //    Final の「返事は、ありませんでした。」が兆候の答え合わせになる。
        (1, "……ご主人、様……ごめん、なさい……", ""),
        (0, "……ぼくは、ずっと見てるだけだった。光を、お前に握らせて。", SGentle),
        (0, "今度は、ぼくが行く番だ。待ってろ、ミナ。", SProud),
        (0, "きみは、ひとりじゃ、ない。ぼくが——……ぼくが、ここに、いる。", SGentle), // 針飛び＝反復・途切れ（replay兆候）。Stay の意味の反復でもある
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(1);
    }

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        if (!_clearing) { _stageElapsed += delta; Hud?.SetElapsed((float)_stageElapsed); }
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("FINAL — 暴走"); }
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;
            case 2: Step_BossSpawn(); break;
            case 3: Step_BossWait(); break;
            case 4: Step_Transition(); break;
        }
        // ボス戦中の ambient は、全ボス共通の投稿弾（X投稿モチーフの言葉弾）に統一（難易度で数がスケール）。
        // FINAL は固有の悲鳴フレーズ（PostWords）を源にする＝暴走中に渦巻く声。
        // ボス本体(BossMina)のスペル/予測線/パネル弾はそのまま。
        if (_bossActive) PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, words: PostWords, fallSpeed: 56f);
    }

    private void Advance() { _step++; _stepStarted = false; _stepTime = 0; }

    private void Step_Lines(double delta, (int who, string text, string face)[] lines)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _introLine = 0; _lineHold = 0;
            if (lines.Length == 0) { Advance(); return; }
            Hud.HoldBubble = true;
            ShowLine(lines);
        }
        if (_zEdge && _lineHold >= 0.15 && !Hud.DialogRevealed)
        {
            Hud.RevealDialogNow(); _lineHold = 0;
        }
        else if (_lineHold >= 0.15 && Hud.DialogRevealed
                 && (_zEdge || (Hud.AutoAdvance && _lineHold >= 1.4)))
        {
            _lineHold = 0; _introLine++;
            if (_introLine >= lines.Length)
            {
                Hud.HoldBubble = false;
                Hud.HideBubble();
                Advance();
                return;
            }
            ShowLine(lines);
        }
    }

    private void ShowLine((int who, string text, string face)[] lines)
    {
        var (who, text, face) = lines[_introLine];
        var kind = (Hud.LineKind)who;
        string portrait = kind switch
        {
            Hud.LineKind.Boy => face,
            _ => "res://char/mina_face.png",
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "ミナ");
    }

    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _boss = new BossMina { Name = "BossMina" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            _bossActive = true;
            Advance();
        }
    }

    private void Step_BossWait()
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
        {
            _bossActive = false;
            Advance();
        }
    }

    private bool _clearing;
    private void Step_Transition()
    {
        if (_clearing) return;
        _clearing = true;
        // FINAL クリア確定＝この瞬間に経過秒を確定しベスト記録（記録画面/カードで参照）。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        game?.RecordClearTime("final", game.Difficulty, (float)_stageElapsed);
        game?.AutoSave(); // 記録を永続化（FINAL は CompleteStage を通らないためここで保存）。
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        // 撃破＝穢れを祓った。本決着（対話で帰還）は Final へ委ねる。
        GetTree().ChangeSceneToFile("res://Final.tscn");
    }

    // 投稿弾（暴走中に渦巻く悲鳴の言葉）の周期/tick 用アキュムレータ。湧き処理は PostBullets.Tick に集約。
    private int _wordTick;
    // FINAL 固有の“声”プール（投稿弾の源）。ハンドル無し（""）＝暴走したミナの内側で渦巻く声。
    private static readonly (string h, string w)[] PostWords =
    {
        ("", "むだだよ"), ("", "どうせ"), ("", "ごめんなさい"),
        ("", "とどかない"), ("", "もういない"), ("", "わたしのせいだ"),
    };
}
