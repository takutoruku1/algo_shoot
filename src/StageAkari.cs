using Godot;

// StageAkari : STAGE1「あかり（雨の教室）」進行。
//   1: 導入会話（少年＝声/テロップ、ミナ＝立ち絵で毒舌）
//   2: あかりボス出現
//   3: ボス戦（自責の弾雨＋あかりの自責弾。浄化＝改心で会話完了まで）
//   4: クリア（灯がともる）
// ボス戦中は天井の自責の雨が降り続ける（会話中は止む）。
public partial class StageAkari : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stepTime;
    private double _lineHold;   // 行表示からの経過（誤連打防止の最小表示時間用）
    private int _introLine;
    private bool _zHeld;
    private bool _zEdge;
    private BossAkari _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    private const float SpawnX = 300f;

    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";

    // 道中ザコ戦（Spawner）。Intro後・ボス前に挿入。
    private Spawner _spawner = null!;
    private int _waveBase;
    private const int MidWaveCount = 8;

    // ダイブ前の会話（v2 [P-02a]）。少年の様子が普段と違う＝核心の予兆。
    // who: 0=少年 / 1=ミナ / 2=相手 / 3=地の文 / 4=投稿 / 5=中継。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (1, "ご主人様。次の“成敗”は? 今日はずいぶん静かですね。", ""),
        (0, "……ああ、悪い。ちょっと考えごとだ。", SGentle),
        (4, "「すきになって、ごめんなさい。」", ""),                       // 投稿
        (0, "…………この人の、ところへ行こう。", SGentle),
        (1, "おや。決めゼリフはどうしたんですか。……それに、いつもの「Stay」も。", ""),
        (0, "……いいから。行くぞ。", SCocky),
        // 合言葉の“不在”＝違和感（転の予兆）。少年は動揺して、いつもの「Stay」を言い忘れる。
        (3, "——その日、ご主人様は「Stay」と言わなかった。わたくしは、なぜか少しだけ、落ち着きませんでした。", ""),
        (3, "——雨の、降りやまない教室でした。机も椅子も、天井へ落ちていく。", ""),   // 地
    };

    // ボス登場時の説明（v2 [P-02b]。who: 0=少年 / 1=ミナ）
    private static readonly (int who, string text, string face)[] BossIntro =
    {
        (1, "黒板の字。自分を責める言葉に、好意の言葉が混ざっていますね。", ""),
        (0, "……この人は、誰かを好きになったことを、罪だと思ってる。", SGentle),
        (0, "————そういう罪も、あるんだよ。この世界にはね。", SGentle),
    };

    // 道中突入の小話（世界観：自責の声の雨）。道中ザコ戦の前に出す。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (3, "——その雨の中を、白い吹き出しが、いくつも漂っていました。", ""),
        (1, "ここの声は……どれも、自分に向かっていますね。", ""),
        (0, "ああ。誰かを責めるより、自分を責めるほうが、ずっと痛い。", SGentle),
        (1, "……やけに、詳しいんですね。", ""),
        (0, "————行くぞ。", SCocky),
    };

    // 道中後の小話（ボスへの引き）。
    private static readonly (int who, string text, string face)[] MidEnd =
    {
        (1, "黒板の奥に、あの子が。……ご主人様、ほんとうに、いいんですね?", ""),
        (0, "……ぼくが、やらなきゃいけないんだ。", SGentle),
    };

    // 帰還（v2 [P-02c]）。投稿の変化＋あかりの残響＋ミナの核心の問い（伏線③）。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「ほんと、バカなんだから。……あたしも、だけど。」", ""),       // 投稿が変化
        (2, "……あったかい声が、した。……なんでかな、あの人の声に、似てた。", ""), // あかり残響
        (1, "あなた、この人を——知ってるんですか?", ""),
        (0, "…………まさか。赤の他人さ。", SCocky),
        (1, "……即答までに、二秒かかりましたね。", ""),
        (0, "ミナ。シェイクスピアは言った。\"Parting is such sweet sorrow.\"", SCocky),
        (1, "はいはい、教養アピールお疲れさまですね。……で、それは誰の話ですか。", ""),
        (0, "————一般論だよ。", SGentle),
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中8体＋ボス1で浄化カプセルが満ちる（部屋が晴れる）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(MidWaveCount + 1);
    }

    private bool _startBannerShown;

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 2 START"); }
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        _zEdge = z && !_zHeld;
        _zHeld = z;
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;
            case 2: Step_Lines(delta, Mid); break;       // 道中突入の小話
            case 3: Step_Midwave(delta); break;          // 道中ザコ戦
            case 4: Step_Lines(delta, MidEnd); break;    // 道中後の小話
            case 5: Step_BossSpawn(); break;
            case 6: Step_Lines(delta, BossIntro); break; // ボスは出現済みだが会話中は止まる
            case 7: Step_BossWait(); break;
            case 8: Step_Clear(delta); break;
            case 9: Step_Transition(); break;
        }
        if (_bossActive) Rain(delta);
    }

    private void Advance()
    {
        _step++;
        _stepStarted = false;
        _stepTime = 0;
    }

    // ---- 会話ステップ（配列を順に流す。Zで手動送り。会話中は弾が止まる） ----
    private void Step_Lines(double delta, (int who, string text, string face)[] lines)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _introLine = 0;
            _lineHold = 0;
            if (lines.Length == 0) { Advance(); return; }
            Hud.HoldBubble = true; // 自動で消えない＝手動送り
            ShowLine(lines);
        }
        if (_zEdge && _lineHold >= 0.15 && !Hud.DialogRevealed)
        {
            Hud.RevealDialogNow();   // 1段目：まず全文表示（読み飛ばし防止）
            _lineHold = 0;
        }
        else if (_lineHold >= 0.15 && Hud.DialogRevealed
                 && (_zEdge || (Hud.AutoAdvance && _lineHold >= 1.4)))
        {
            _lineHold = 0;
            _introLine++;
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
            Hud.LineKind.Boy => face,                       // 少年（行ごとの表情）
            Hud.LineKind.Other => "res://char/akari_face.png", // あかり
            _ => "res://char/mina_face.png",                // ミナ・中継
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    // ---- 道中ザコ戦：Spawner起動→MidWaveCount体浄化で抜ける ----
    private void Step_Midwave(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            _spawner = new Spawner { Name = "Spawner", World = World };
            AddChild(_spawner);
            _spawner.Begin();
        }
        if (game != null && game.PurifiedCount - _waveBase >= MidWaveCount)
        {
            _spawner.Stop();
            Advance();
        }
    }

    // ---- 2: ボス出現 ----
    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _boss = new BossAkari { Name = "BossAkari" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            _bossActive = true;
            Advance(); // 出現と同時に説明会話へ（会話中はボス停止・雨も止む）
        }
    }

    // ---- 3: ボス戦（浄化＆会話完了まで） ----
    private void Step_BossWait()
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
        {
            _bossActive = false;
            Advance();
        }
    }

    // ---- 5: クリア（帰還の会話を手動送り） ----
    private bool _clearBannerShown;
    private void Step_Clear(double delta)
    {
        if (!_clearBannerShown)
        {
            _clearBannerShown = true;
            Hud.ShowBanner("STAGE 2 CLEAR");
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll(); // クリア時に自弾・残弾を一掃(#17)
        }
        Step_Lines(delta, Clear);
    }

    // ---- 6: STAGE3（こはる）へ ----
    private bool _clearing;
    private void Step_Transition()
    {
        if (_clearing) return;
        _clearing = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetNodeOrNull<GameManager>("/root/Game")?.CompleteStage("akari");
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    // 天井から降る「自責の雨」（会話中は止む）。
    private void Rain(double delta)
    {
        if (Hud.BubblePaused) return;
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _rainT += delta;
        float mul = GetNodeOrNull<GameManager>("/root/Game")?.DanmakuIntervalMul ?? 1f;
        if (_rainT < 0.16 * mul) return;
        _rainT = 0;
        float x = _rng.RandfRange(8f, 376f);
        float vx = _rng.RandfRange(-10f, 10f);
        pool.Spawn(new Vector2(x, -6f), new Vector2(vx, 74f), isEnemy: true, 3f, 1);
    }
}
