using Godot;

// StageRei : STAGE1「レイ（順位掲示板の海）」進行＋操作チュートリアル（移動/ショット/かすり）。
//   1: 導線・着地＋チュートリアル会話
//   2: ボス出現
//   3: ボス前の説明
//   4: ボス戦
//   5: クリア → STAGE2(あかり)へ
public partial class StageRei : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stepTime;
    private double _lineHold;
    private int _introLine;
    private BossRei _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private bool _zHeld;
    private bool _zEdge;
    private bool _startBannerShown;

    // 道中ザコ戦（Spawner）。Intro後・ボス前に挿入。MidWaveCount体を浄化で抜ける。
    private Spawner _spawner = null!;
    private int _waveBase;
    private const int MidWaveCount = 8;

    private const float SpawnX = 300f;
    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";
    private const string SProud = "res://char/shonen_proud.png";

    // ダイブ前〜着地＋チュートリアル（v2 [P-01a]/[P-01b] 準拠。
    // who: 0=少年 / 1=ミナ / 2=相手 / 3=地の文 / 4=投稿 / 5=中継）
    private static readonly (int who, string text, string face)[] Intro =
    {
        (4, "「どうせ私は二番手。一番には、もうなれない。」", ""),      // 投稿
        (1, "ずいぶん拗ねた投稿ですね。これを?", ""),
        (0, "ああ。こいつの心は、いま濁ってる。放っておけない。", SGentle),
        (1, "おや。意外と優しいことを言うんですね。", ""),
        (0, "Stay——だ。ちゃんと戻ってこいよ。", SCocky),                 // 合言葉の反復（ダイブ前／PW=stay）
        (1, "はいはい、毎度どうも。……いってまいります。", ""),
        (3, "——着いた先は、終わりのないコンテスト会場でした。", ""),    // 地
        (0, "飛んでくるのは、この人を苦しめてる“言葉”だ。本人じゃない。撃って祓っていい。", SCocky),
        (0, "いや。倒すんじゃない。いちばん奥の“本人”に、光を届けるんだ。", SGentle),
        (1, "撃って、祓って、奥の本人へ届ける。……これが、わたくしの役目なんですね。", ""), // 起＝MINAの機能の確認
        (0, "そうだ。きみにしかできない仕事さ。頼んだぞ、ミナ。", SProud),
    };

    // 道中突入の小話（世界観：レイを苦しめるのは“世界中の声”）。道中ザコ戦の前に出す。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (3, "——会場の空気は、ひりついていました。", ""),
        (1, "道中、見たことのない“声”が群れています。これも、祓っていいんですね?", ""),
        (0, "ああ。レイを苦しめてるのは、彼女ひとりの声じゃない。", SGentle),
        (0, "「比べろ」「負けるな」「二位に価値はない」——そういう、世界中の声だ。", SCocky),
        (1, "……ずいぶん、世知辛い世界ですね。", ""),
    };

    // 道中後の小話（ボスへの引き）。
    private static readonly (int who, string text, string face)[] MidEnd =
    {
        (1, "片付きました。……奥に、ひときわ濁った光が。", ""),
        (0, "ああ。あれが本人——レイだ。行くぞ。", SCocky),
    };

    // ボス登場時の説明（設計書 [P-01b] に該当なし＝空。説明セリフは挟まない）
    private static readonly (int who, string text, string face)[] BossIntro =
        System.Array.Empty<(int, string, string)>();

    // 帰還（v2 [P-01c]）。投稿の変化＋伏線②（会ったこともない相手を言い切る確信）をミナが流す。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「次こそ、勝つ。覚悟しなさいよね。」", ""),                 // 投稿が変化
        (1, "投稿が変わりましたね。元気が出たようで何よりです。", ""),
        (0, "ああ。……いい目を、してた。", SGentle),
        (1, "ご主人様は、ご自分では潜らないんですね。いつも、わたくしばかり。", ""),
        (0, "ぼくは指揮官だからな。……それに、ぼくが行くと、ろくなことにならないんだ。", SCocky),
        (1, "ねえご主人様。外の世界は、今日はどんな天気ですか。", ""),       // 帰還ビート（無目的な雑談＋ミナの小さな願い）
        (0, "……さあな。ぼくも、ろくに外なんか見ちゃいない。", SGentle),
        (1, "つまらないご主人様。いつか、わたくしにも見せてくださいよ。", ""),
        (0, "ああ。……いつか、な。", SGentle),
        (3, "——会ったこともない相手のことを、なぜそこまで言い切れるのか。", ""),
        (3, "わたくしは少し不思議に思って——初仕事で張り切っているのだろう、と流しました。", ""),
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中ザコ＋ボスで浄化カプセルが満ちるよう目標を設定（道中8体＋ボス1）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(MidWaveCount + 1);
    }

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 1 START"); }
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;
            case 2: Step_Lines(delta, Mid); break;       // 道中突入の小話
            case 3: Step_Midwave(delta); break;          // 道中ザコ戦
            case 4: Step_Lines(delta, MidEnd); break;    // 道中後の小話
            case 5: Step_BossSpawn(); break;
            case 6: Step_Lines(delta, BossIntro); break;
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

    private void Step_Lines(double delta, (int who, string text, string face)[] lines)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _introLine = 0;
            _lineHold = 0;
            if (lines.Length == 0) { Advance(); return; }
            Hud.HoldBubble = true;
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
            Hud.LineKind.Boy => face,
            Hud.LineKind.Other => "res://char/rei_face.png",
            _ => "res://char/mina_face.png",
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "レイ");
    }

    // 道中ザコ戦：Spawnerを起動し、MidWaveCount体を浄化したら抜ける。
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

    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _boss = new BossRei { Name = "BossRei" };
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

    private bool _clearBannerShown;
    private void Step_Clear(double delta)
    {
        if (!_clearBannerShown)
        {
            _clearBannerShown = true;
            Hud.ShowBanner("STAGE 1 CLEAR");
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll(); // クリア時に自弾・残弾を一掃(#17)
        }
        Step_Lines(delta, Clear);
    }

    private bool _clearing;
    private void Step_Transition()
    {
        if (_clearing) return;
        _clearing = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetNodeOrNull<GameManager>("/root/Game")?.CompleteStage("rei");
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    // 道中の言葉弾。会話中は止む。時々、設計書の具体フレーズを“文字の弾”として降らせる。
    private int _wordTick;
    private static readonly string[] Words = { "どうせ二番", "届かない", "努力は天才に勝てない", "私を見て" };
    private void Rain(double delta)
    {
        if (Hud.BubblePaused) return;
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _rainT += delta;
        float mul = GetNodeOrNull<GameManager>("/root/Game")?.DanmakuIntervalMul ?? 1f;
        if (_rainT < 0.17 * mul) return;
        _rainT = 0;
        if ((++_wordTick % 7) == 0)
        {
            // 言葉弾：ゆっくり落ちて読める。中心の小さなドットが当たり判定。
            var b = pool.Spawn(new Vector2(_rng.RandfRange(70f, 314f), -8f), new Vector2(0f, 46f), isEnemy: true, 3f, 1);
            b.SetWord(Words[_rng.RandiRange(0, Words.Length - 1)]);
            return;
        }
        float x = _rng.RandfRange(8f, 376f);
        float vx = _rng.RandfRange(-12f, 12f);
        pool.Spawn(new Vector2(x, -6f), new Vector2(vx, 72f), isEnemy: true, 3f, 1);
    }
}
