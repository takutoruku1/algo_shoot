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
    private double _stageElapsed;   // ステージ全体の経過秒（_Ready〜クリア確定まで。ポーズ中は止まる）。
    private float _clearTime;       // クリア確定時に確定した経過秒。
    private double _lineHold;
    private int _introLine;
    private BossRei _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private bool _zHeld;
    private bool _zEdge;
    private bool _startBannerShown;

    // 道中ザコ戦（Spawner）。Intro後・ボス前に挿入。三部構成で「後半ほど圧が上がる」緩急を作る：
    //   前半A（緩い導入）→ ボスのチラ見せ → 後半B（やや詰める）→ ミッドシナリオ（溜め）→ 終盤C（最大密度）→ 本ボス。
    // 体数より“密度と変化”で長さを作る方針（§3 緩急）：合計20体だが3波で圧と構成を変えて間延びさせない。
    private Spawner _spawner = null!;
    private int _waveBase;
    private bool _waveSpawnDone;       // 道中ステップ内：規定数を浄化してスポーン停止済み（あとは残ザコ全滅待ち）。各ステップ開始でリセット。
    private const int MidWaveA = 21;  // 導入（チラ見せ前）。緩く立ち上がる。※道中3倍（旧7）
    private const int MidWaveB = 18;  // チラ見せ後。StartIntensity を上げてやや詰めて始める。※道中3倍（旧6）
    private const int MidWaveC = 21;  // ミッドシナリオ後の終盤。最大密度＝ボス直前の山（合計60体）。※道中3倍（旧7）

    // ボスの“チラ見せ”（カメオ）＝本戦ボスと同じ土台の短いミニボス戦（CameoBoss＝Enemy 派生・シールド制）。
    // 撃破（HP/サイクル削り切り＝改心）まで Stage は進まない。保険タイマー退場は廃止（撃たないと進めない）。
    private CameoBoss _cameo = null!;

    // ───────── チュートリアル（StageRei に重ねる操作講座）─────────
    // 初回プレイ(tutorialSeen==false)で自動 ON、タイトル「あそびかた」から強制再生も可。
    // 教える順：①移動&ショット ②Focus低速 ③グレイズ（→やさしさゲージの満タン→B全開まで） ④ボム
    //   ⑤浄化（→救った証=インプレ→ショップ強化／代償=汚染ゲージ↑）⑥やさしさ全開（満ちた瞬間の告知）。
    // 各ヒントは「会話で止めて説明 → 常駐指示を残して操作させる → 能動条件で解除（FBにタイムアウト）」の3拍。
    // 教え役はミナ（who1）。会話は既存 Step_Lines を流用、指示帯は Hud.SetTutorialHint（敵/自機は止めない）。
    private bool _tutorial;          // このランがチュートリアルか
    private int _tphase;             // チュートリアルの進行（0..）。Intro 後に T1 から回す。
    private bool _tphaseStarted;
    private double _tphaseTime;
    // 操作させる区間の達成カウント用ベースライン。
    private bool _t1Moved; private bool _t1Shot;
    private double _t2FocusHeld; private bool _t2Moved;
    private int _t3GrazeBase;
    private int _t4BombBase;
    private int _t5PurifyBase;
    private bool _tutorialMidwaveTaught; // T5（最初の1体の浄化講座）を完了したか
    private bool _t6Shown;               // やさしさ全開トーストを一度出したか

    private const float SpawnX = 300f;
    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";
    private const string SProud = "res://char/shonen_proud.png";

    // ダイブ前〜着地＋チュートリアル（v2 [P-01a]/[P-01b] 準拠。
    // who: 0=少年 / 1=ミナ / 2=相手 / 3=地の文 / 4=投稿 / 5=中継）
    private static readonly (int who, string text, string face)[] Intro =
    {
        (4, "「だれも、わたしには追いつけない。……それの、なにが、いけないの。」", ""),      // 投稿
        (1, "ずいぶん、気の大きな投稿ですね。これを?", ""),
        (0, "ああ。威張ってるけどな。……この声は、濁ってる。放っておけない。", SGentle),
        (1, "おや。意外と優しいことを言うんですね。", ""),
        (0, "Stay——だ。ちゃんと戻ってこいよ。", SCocky),                 // 合言葉の反復（ダイブ前／PW=stay）
        (1, "はいはい、毎度どうも。……いってまいります。", "res://char/mina_smile.png"),
        (3, "——着いた先は、終わりのないコンテスト会場でした。", ""),    // 地
        (0, "飛んでくるのは、この人を苦しめてる“言葉”だ。本人じゃない。撃って祓っていい。", SCocky),
        (0, "いや。倒すんじゃない。いちばん奥の“本人”に、光を届けるんだ。", SGentle),
        (1, "撃って、祓って、奥の本人へ届ける。……これが、わたくしの役目なんですね。", ""), // 起＝MINAの機能の確認
        (0, "そうだ。きみにしかできない仕事さ。頼んだぞ、ミナ。", SProud),
    };

    // ───────── チュートリアル会話（ミナ＝who1。説明会話で止め、指示帯を残して操作させる）─────────
    private const string SMina = "res://char/mina_face.png";
    // T1 移動&ショット
    private static readonly (int who, string text, string face)[] TutMove =
        { (1, "まずは身体慣らしです。動いて、撃つ。それだけ。", SMina) };
    // T2 Focus低速
    private static readonly (int who, string text, string face)[] TutFocus =
        { (1, "狭いところは Shift。ゆっくり、丁寧に。", SMina) };
    // T3 グレイズ（練習弾を少数だけ手動Spawn）
    private static readonly (int who, string text, string face)[] TutGraze =
        { (1, "弾は怖いだけじゃありません。掠めるほど、左の“やさしさゲージ”が満ちる。寄って、ごらんなさい。", SMina) };
    // 「やさしさ」が満ちると何が嬉しいか（満タン→Ctrl で全開）まで言い切る。
    private static readonly (int who, string text, string face)[] TutGrazeOk =
        { (1, "お見事。", SMina),
          (1, "やさしさは、浄化でも満ちます。満タンになったら Ctrl。数秒だけ光が溢れ、弾を祓いやすくなりますよ。", SMina) };
    // T4 ボム
    private static readonly (int who, string text, string face)[] TutBomb =
        { (1, "囲まれたら X。一掃して、仕切り直す。", SMina) };
    private static readonly (int who, string text, string face)[] TutBombSkip =
        { (1, "次は、ここぞで。", SMina) };
    // T5 浄化（道中の最初の1体に同期して割り込み）
    private static readonly (int who, string text, string face)[] TutPurify =
        { (1, "あの“声”、周りの板を全部祓えば本体に光が届きます。撃ち込みなさい。", SMina) };
    // 浄化の“ごほうび”（浄化した心＝通貨→ショップ強化）と、その代償（汚染↑→やさしさが鈍る）を1概念1ビートで。
    private static readonly (int who, string text, string face)[] TutPurifyOk =
        { (1, "その調子。浄化するたび“浄化した心”が貯まって、ハブのショップで わたくしを強化できます。", SMina),
          (1, "“救った人 ◯/3”はゴールの目印。心は強化に使うお金——別物ですよ。", SMina),
          (0, "……ただ、祓うほど左下の“汚染ゲージ”がじわっと上がる。ミナの光が、少しずつ濁るんだ。", SGentle),
          (0, "濁るほど、かすりや浄化で満ちる“やさしさ”が、ほんの少しずつ届きにくくなる。", SGentle),
          (1, "ここではまだ無痛。でも奥へ行くほど重くなる。……今は気にせず行きましょう。", SMina) };

    // 道中突入の小話（世界観：レイを苦しめるのは“世界中の声”）。道中ザコ戦の前に出す。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (3, "——会場の空気は、ひりついていました。", ""),
        (1, "道中、見たことのない“声”が群れています。これも、祓っていいんですね?", ""),
        (0, "ああ。レイを苦しめてるのは、彼女ひとりの声じゃない。", SGentle),
        (0, "「比べろ」「負けるな」「二位に価値はない」——そういう、世界中の声だ。", SCocky),
        (1, "……ずいぶん、世知辛い世界ですね。", ""),
    };

    // 道中“前半”の後：ボスのツイートが流れてくる→MINA×少年がボスについて考察（伏線②補強）。
    private const string RFace = "res://char/rei_face.png";
    private static readonly (int who, string text, string face)[] BossTalk =
    {
        (4, "「だれも、わたしには追いつけない。……それの、なにが、いけないの。」", ""), // ボスのツイートが流れてくる
        (1, "……さっきの投稿が、また流れてきました。この声の主が、奥の“本人”ですか。", ""),
        (0, "ああ。レイっていう。負けず嫌いで、努力家で……誰よりも、勝ちにこだわるやつだ。", SGentle),
        (1, "ずいぶん詳しいんですね。会ったこともない相手なのに。", "res://char/mina_worried.png"),
        (0, "……っ。さあな。投稿を見てりゃ、それくらい分かる。", SCocky),
        (1, "ふぅん。", "res://char/mina_smile.png"),
    };

    // チラ見せ：登場の挑発（攻撃①の前）。who=2=レイ。
    private static readonly (int who, string text, string face)[] CameoTalk1 =
    {
        (2, "——だれ? あなたたち。わたしの会場で、勝手なことしないでくれる?", RFace),
        (1, "ご機嫌斜めですね。……どうします、ご主人様。", ""),
        (0, "刺激するな、ミナ。こいつは——売られた喧嘩を、絶対に買うタイプだ。", SGentle),
        (2, "へえ。よく分かってるじゃない。……なら、買ってもらおうかしら!", RFace),
    };
    // 攻撃①の後：さらに挑発↔反応。
    private static readonly (int who, string text, string face)[] CameoTalk2 =
    {
        (2, "どう? これがわたしの実力。二番手なんかじゃ、よけきれないでしょ。", RFace),
        (1, "……ご主人様の指示、やけに先回りしていますね。まるで手の内を知っているみたいに。", "res://char/mina_worried.png"),
        (0, "……まだだ。レイは、ここからが本番なんだよ。", SCocky),
    };
    // 攻撃②の後：少年がうっかり名を呼ぶ＝伏線②の山。
    private static readonly (int who, string text, string face)[] CameoTalk3 =
    {
        (2, "……ねえ。あなた、さっきから——どうして、わたしのことを“レイ”って呼ぶの?", RFace),
        (0, "————。", SGentle),                                   // 沈黙
        (1, "ご主人様?", "res://char/mina_worried.png"),
        (0, "……気にするな。さあ、来い。きみの全部を、見せてみろ。", SProud),
        (2, "……ふん。いいわよ。後悔しても、知らないんだから!", RFace),
    };
    // 捨て台詞（攻撃③の後）→逃走。
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "っ……今日は、ここまでにしといてあげる。", RFace),
        (2, "次は——奥で待ってる。本気のわたしと、ちゃんと向き合いなさいよ。", RFace),
        (2, "逃げたら……承知しないんだから。", RFace),
        (3, "——レイの影は、ひときわ大きな弾幕を残して、嵐のように奥へ消えていきました。", ""),
    };

    // ───────── ミッドシナリオ枠（後半Bと終盤Cの境＝ボス前の“溜め”）─────────
    // ここはシナリオ担当が本文を差し込むスロット（who=Hud.LineKind 0=少年/1=ミナ/2=レイ/3=ナレ/4=投稿/5=中継）。
    // しっかり読ませたいので吹き出し会話（Step_Lines）で出す＝弾は止まる。レイ面のテーマ＝競争/監視に馴染む位置に。
    // ※プレースホルダ。長さは2〜数行を想定（テンポを殺さない範囲）。
    private static readonly (int who, string text, string face)[] MidStory =
    {
        (3, "——気づけば、無数の“目”が、こちらを採点していました。", ""),  // ザコ＝偵察ドローン/監視カメラの目
        (1, "この目……ぜんぶ、奥の彼女を、見ているんですね。", "res://char/mina_worried.png"),
        (0, "ああ。世界中が見てる。誰も追いつけないところまで来て、それでも、誰も隣にいない。", SGentle),
        (1, "……てっぺんは、こんなに寒いんですか。", ""),
        (0, "だから——ぼくらだけは、点数をつけずに行くんだ。", SProud),
    };

    // 道中後の小話（ボスへの引き）。
    private static readonly (int who, string text, string face)[] MidEnd =
    {
        (1, "片付きました。……奥に、ひときわ濁った光が。", ""),
        (0, "ああ。さっきの子だ。今度こそ、奥まで届かせる。行くぞ。", SCocky),
    };

    // ボス登場時の説明（設計書 [P-01b] に該当なし＝空。説明セリフは挟まない）
    private static readonly (int who, string text, string face)[] BossIntro =
        System.Array.Empty<(int, string, string)>();

    // 帰還（v2 [P-01c]）。投稿の変化＋伏線②（会ったこともない相手を言い切る確信）をミナが流す。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「次は、本気のあなたと。——逃げたら、承知しないから。」", ""),     // 投稿が変化
        (1, "投稿が変わりましたね。誰かと、本気で戦いたくなったようで。", ""),
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
        // 道中ザコ（A+B+C 三波）＋ボスで浄化カプセルが満ちるよう目標を設定（20体＋ボス1）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(MidWaveA + MidWaveB + MidWaveC + 1);

        // チュートリアル発火判定：初回(tutorialSeen==false) or 任意再生(ForceTutorialReplay)。
        // ただし自動操縦（--demo/--qa）では進行を乱さないよう OFF。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        bool autoplay = false;
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { autoplay = true; break; }
        if (game != null && !autoplay && (!game.TutorialSeen || game.ForceTutorialReplay))
        {
            _tutorial = true;
            game.ForceTutorialReplay = false; // 任意再生フラグは消費（TutorialSeen は触らない）
        }
        if (Hud != null) Hud.TutorialActive = _tutorial;

        // [一時/デバッグ] --boss : 道中を飛ばしてボス戦から始める（予測攻撃のテストプレイ用）。
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--boss")
            {
                _tutorial = false;
                if (Hud != null) Hud.TutorialActive = false;
                _step = 10; // Step_BossSpawn へ直行
                break;
            }
    }

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        // ステージ経過タイム：クリア確定までは積算し続け、HUDへ常時反映（クリア後は確定値で固定）。
        if (!_clearing) { _stageElapsed += delta; Hud?.SetElapsed((float)_stageElapsed); }
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 1 START"); }

        // チュートリアル①〜④：Intro 会話の直後（BubblePaused 解除の瞬間）から、Mid 小話の前に挿入する。
        // 完了するまで本編 step を 2 へ進めず、ここで操作講座（移動&ショット/Focus/グレイズ/ボム）を回す。
        if (_tutorial && _step == 2 && _tphase < TutDone)
        {
            Tutorial_PreMid(delta);
            Tutorial_OverloadWatch(delta); // 練習中でも満ちたら全開トースト
            return;
        }

        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;
            case 2: Step_Lines(delta, Mid); break;        // 道中突入の小話
            case 3: Step_MidwaveA(delta); break;          // 道中ザコ戦A（導入）
            case 4: Step_Lines(delta, BossTalk); break;   // ボスのツイート→MINA×少年の考察
            case 5: Step_BossCameo(delta); break;         // ボスのチラ見せ（登場/挑発/攻撃/逃走）
            case 6: Step_MidwaveB(delta); break;          // 道中ザコ戦B（やや詰める）
            case 7: Step_Lines(delta, MidStory); break;   // ★ミッドシナリオ枠（ボス前の溜め）
            case 8: Step_MidwaveC(delta); break;          // 道中ザコ戦C（終盤＝最大密度の山）
            case 9: Step_Lines(delta, MidEnd); break;     // 道中後の小話
            case 10: Step_BossSpawn(); break;
            case 11: Step_Lines(delta, BossIntro); break;
            case 12: Step_BossWait(); break;
            case 13: Step_Clear(delta); break;
            case 14: Step_Transition(); break;
        }
        // ボス戦中の“雨弾”は、X投稿モチーフの言葉弾（投稿弾）だけ降らせ、ただの常時落下弾は止める（ユーザー要望）。
        // 投稿弾の湧きは全ボス共通ヘルパ PostBullets.Tick に集約（難易度で数がスケール）。レイ面は共通 TickerWords。
        // ボス本体(BossRei)のスペル/予測線/パネル弾はそのまま。道中はSpawner任せでRain非依存。
        if (_bossActive) PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, fallSpeed: 46f);
        if (_tutorial) Tutorial_OverloadWatch(delta);
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
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, // ミナも行ごと表情
            _ => "res://char/mina_face.png",
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "レイ");
    }

    // 道中ザコ戦“前半”：Spawnerを起動し、MidWaveA体を浄化したら抜ける（→ボスのツイート→チラ見せへ）。
    // チュートリアル⑤：最初の1体だけ手動で湧かせ、出た瞬間に浄化講座を割り込ませる。
    private void Step_MidwaveA(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            _waveSpawnDone = false;
            if (_tutorial && !_tutorialMidwaveTaught)
            {
                Tutorial_PurifyBegin(game);
                return; // Spawner はまだ起こさない（講座中は1体だけ）
            }
            StartMidwaveSpawner();
        }

        // チュートリアル⑤の進行（最初の1体の浄化＋締めの会話）。完了後に Spawner を起動。
        if (_tutorial && !_tutorialMidwaveTaught)
        {
            if (Tutorial_PurifyStep(delta, game)) StartMidwaveSpawner();
            return;
        }

        // ２段階ゲート：①規定数を浄化（or 目標到達でスポーナ自動停止）したらスポーンだけ止める（残ザコは消さない）→②画面のザコを全滅させてから次へ。
        // StageCleared を OR に入れるのは保険：浄化総数が目標(StageTarget)に達すると Spawner が自動停止し、
        // それ以上湧かない＝この波の規定数(MidWaveX)に届かないことがある。その場合も残ザコを倒し切って次へ進める（ソフトロック回避）。
        if (!_waveSpawnDone && game != null && (game.PurifiedCount - _waveBase >= MidWaveA || game.StageCleared))
        {
            _spawner?.Stop();
            _spawner = null!; // 後半で新規に湧かせるため解放
            _waveSpawnDone = true; // 以降は新規スポーンなし。残ザコを全部倒すまで待つ。
        }
        if (_waveSpawnDone && GetTree().GetNodesInGroup("enemies").Count == 0)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // 道中ザコ戦“B（後半）”：チラ見せの後。やや詰めて始める（StartIntensity 0.35）。MidWaveB体でミッドシナリオへ。
    private void Step_MidwaveB(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            _waveSpawnDone = false;
            StartMidwaveSpawner(0.35f);
        }
        // ２段階ゲート：①規定数浄化（or 目標到達）でスポーン停止（残ザコは消さない）→②全滅でミッドシナリオへ。
        if (!_waveSpawnDone && game != null && (game.PurifiedCount - _waveBase >= MidWaveB || game.StageCleared))
        {
            _spawner?.Stop();
            _spawner = null!;
            _waveSpawnDone = true;
        }
        if (_waveSpawnDone && GetTree().GetNodesInGroup("enemies").Count == 0)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // 道中ザコ戦“C（終盤）”：ミッドシナリオの後。最大密度（StartIntensity 0.7）でボス直前の山を作る。
    private void Step_MidwaveC(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            _waveSpawnDone = false;
            StartMidwaveSpawner(0.7f);
        }
        // ２段階ゲート：①規定数浄化（or 目標到達）でスポーン停止（残ザコは消さない）→②全滅で本ボスへ。
        if (!_waveSpawnDone && game != null && (game.PurifiedCount - _waveBase >= MidWaveC || game.StageCleared))
        {
            _spawner?.Stop();
            _spawner = null!;
            _waveSpawnDone = true;
        }
        if (_waveSpawnDone && GetTree().GetNodesInGroup("enemies").Count == 0)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // ボスの“チラ見せ”（カメオ）：CameoBoss（本戦ボスと同じ Enemy 派生・シールド制・BossMover）を1体スポーン。
    // 撃破（HP/サイクル削り切り＝改心）して捨て台詞を流し切る（Finished）まで Stage は進まない。保険退場は無し。
    private void Step_BossCameo(double delta)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _cameo = new CameoBoss
            {
                Name = "ReiCameo",
                Theme = new CameoTheme
                {
                    DisplayName = "レイ", Handle = "@rei_____",
                    PreTex = "res://char/enemy_rei_pre.png",
                    CryTex = "res://char/enemy_rei_cry.png",
                    PostTex = "res://char/enemy_rei_post.png",
                    Face = RFace,
                    SpellTint = new Color("b9c2d0"), SpellShape = BulletShape.Orb,
                    Fire = CameoFireTheme.ReiAggressive,
                    Aura = FxLayer.BossAura.Rei,
                    Bgm = Audio.Instance?.BgmBossRei,
                    IntroLines = CameoTalk1, TauntLines = CameoTalk3, DefeatLines = CameoPost,
                },
            };
            World.AddChild(_cameo);
            _cameo.GlobalPosition = new Vector2(SpawnX, 70f);
        }

        // 撃破→捨て台詞を流し切ったら次フェーズへ（本ボスへ向かう道中後半）。
        if (!IsInstanceValid(_cameo) || _cameo.Finished)
        {
            Hud.HideBossBar();                                   // バー出っ放しにしない（後で本ボスが再表示）
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            if (IsInstanceValid(_cameo)) _cameo.QueueFree();
            Advance();
        }
    }

    private void StartMidwaveSpawner(float startIntensity = 0f)
    {
        if (_spawner != null) return;
        _spawner = new Spawner { Name = "Spawner", World = World, Theme = StageTheme.Rei, StartIntensity = startIntensity };
        AddChild(_spawner);
        _spawner.Begin();
    }

    // 倒し残した“居座りザコ”を片付ける（道中の節目＝チラ見せ前／本ボス前の転換で呼ぶ）。
    private void ClearStageEnemies()
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e) e.QueueFree();
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
            // クリア確定＝この瞬間に経過秒を確定し、ベスト記録（自己ベスト更新ならバナーに NEW BEST!）。
            _clearTime = (float)_stageElapsed;
            var game = GetNodeOrNull<GameManager>("/root/Game");
            var rec = game?.RecordClearTime("rei", game.Difficulty, _clearTime) ?? (true, (float?)null);
            Hud.ShowClearBanner("STAGE 1 CLEAR", _clearTime, rec.isBest, rec.prev);
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
        var game = GetNodeOrNull<GameManager>("/root/Game");
        // チュートリアルを最後まで通せたら既読化（途中離脱なら未読のまま＝次回また出す）。
        if (_tutorial) game?.MarkTutorialSeen();
        game?.CompleteStage("rei");
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    // 投稿弾（X投稿モチーフ＝ティッカー連動の言葉弾）の周期/tick 用アキュムレータ。
    // 実際の湧き処理は全ボス共通ヘルパ PostBullets.Tick（難易度で数がスケール）に集約済み。
    private int _wordTick;

    // ════════════════════ チュートリアル本体 ════════════════════
    // _tphase で①〜④を順に回す。説明会話＝Hud(止まる)／操作させる区間＝Hud.SetTutorialHint(止めない)。
    // 解除＝能動条件（移動/ショット/Focus/グレイズ/ボム）。届かない時はタイムアウト(FB)で流す。
    private const int TutDone = 10; // ①〜④の全フェーズ完了

    // 会話用ミニプレイヤ（Step_Lines とは独立の状態を使う）。終了で true。
    private int _tLine;
    private bool _tTalkStarted;
    private bool TutTalk(double delta, (int who, string text, string face)[] lines)
    {
        if (!_tTalkStarted)
        {
            _tTalkStarted = true;
            _tLine = 0;
            _lineHold = 0;
            Hud.HoldBubble = true;
            Hud.ClearTutorialHint(); // 説明会話中は常駐指示を消す
            TutShowLine(lines);
        }
        if (_zEdge && _lineHold >= 0.15 && !Hud.DialogRevealed)
        {
            Hud.RevealDialogNow();
            _lineHold = 0;
        }
        else if (_lineHold >= 0.15 && Hud.DialogRevealed
                 && (_zEdge || (Hud.AutoAdvance && _lineHold >= 1.4)))
        {
            _lineHold = 0;
            _tLine++;
            if (_tLine >= lines.Length)
            {
                Hud.HoldBubble = false;
                Hud.HideBubble();
                _tTalkStarted = false;
                return true;
            }
            TutShowLine(lines);
        }
        return false;
    }

    private void TutShowLine((int who, string text, string face)[] lines)
    {
        var (who, text, face) = lines[_tLine];
        Hud.ShowDialog((Hud.LineKind)who, text, string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, otherName: "レイ");
    }

    // フェーズ移行ヘルパ。会話/指示の状態を初期化して次へ。
    private void TutNext()
    {
        _tphase++;
        _tphaseStarted = false;
        _tphaseTime = 0;
        Hud.ClearTutorialHint();
    }

    private bool TutMovePressed() =>
        Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down").Length() > 0.2f
        || Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.D);

    // ①〜④（Mid 小話の前）。会話→指示→能動条件解除（FBタイムアウト）の3拍を順に回す。
    private void Tutorial_PreMid(double delta)
    {
        _tphaseTime += delta;
        switch (_tphase)
        {
            // ── ① 移動&ショット ──
            case 0: // T1 説明
                if (TutTalk(delta, TutMove)) TutNext();
                break;
            case 1: // T1 操作（移動入力＋ショット）。FB6秒。
                if (!_tphaseStarted)
                {
                    _tphaseStarted = true;
                    _t1Moved = false; _t1Shot = false; _t1ShotCount = 0;
                }
                // 毎フレーム張り直す（全開トーストの自動Clearで指示が消えても復帰する）。
                Hud.SetTutorialHint("移動=WASD/方向　ショット=Z");
                if (TutMovePressed()) _t1Moved = true;
                // ショットは自弾の発生で計測（連射0.11s間隔。約12フレーム弾を見たら5発相当）。
                if (CountPlayerBullets() >= 1) _t1ShotCount++;
                if (_t1ShotCount >= 7) _t1Shot = true;
                if ((_t1Moved && _t1Shot) || _tphaseTime > 6.0) TutNext();
                break;

            // ── ② Focus 低速 ──
            case 2: // T2 説明
                if (TutTalk(delta, TutFocus)) TutNext();
                break;
            case 3: // T2 操作（Shift押下0.5秒以上＋移動）。FB5秒。
                if (!_tphaseStarted)
                {
                    _tphaseStarted = true;
                    _t2FocusHeld = 0; _t2Moved = false;
                }
                Hud.SetTutorialHint("Shift=低速移動"); // 毎フレーム張り直し（全開トースト対策）
                bool focus = Input.IsKeyPressed(Key.Shift) || Pad.Pressed(JoyButton.LeftShoulder) || Pad.Pressed(JoyButton.RightShoulder);
                if (focus) _t2FocusHeld += delta;
                if (focus && TutMovePressed()) _t2Moved = true;
                if ((_t2FocusHeld >= 0.5 && _t2Moved) || _tphaseTime > 5.0) TutNext();
                break;

            // ── ③ グレイズ（練習弾を少数だけ手動Spawn）──
            case 4: // T3 説明
                if (TutTalk(delta, TutGraze)) TutNext();
                break;
            case 5: // T3 操作（弾を数発撒く→グレイズ1回でクリア）。FB8秒。
                if (!_tphaseStarted)
                {
                    _tphaseStarted = true;
                    _t3GrazeBase = GetNodeOrNull<GameManager>("/root/Game")?.GrazeCount ?? 0;
                    _t3Refill = 0;
                    Tutorial_SpawnGrazeBullets();
                }
                Hud.SetTutorialHint("弾にかすると やさしさ↑"); // 毎フレーム張り直し（全開トースト対策）
                // 避けられて尽きたら少しずつ補充（かすれる弾を絶やさない）。
                _t3Refill += delta;
                if (_t3Refill > 1.6 && CountEnemyBullets() < 3) { _t3Refill = 0; Tutorial_SpawnGrazeBullets(); }
                int gz = GetNodeOrNull<GameManager>("/root/Game")?.GrazeCount ?? 0;
                if (gz - _t3GrazeBase >= 1 || _tphaseTime > 8.0)
                {
                    GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                    TutNext();
                }
                break;
            case 6: // T3 「お見事」
                if (TutTalk(delta, TutGrazeOk)) TutNext();
                break;

            // ── ④ ボム ──
            case 7: // T4 説明（弾を少し濃いめに撒いてから割り込み）
                if (!_tphaseStarted)
                {
                    _tphaseStarted = true;
                    Tutorial_SpawnBombBullets();
                }
                if (TutTalk(delta, TutBomb)) TutNext();
                break;
            case 8: // T4 操作（ボム1回発動）。FB6秒。
                if (!_tphaseStarted)
                {
                    _tphaseStarted = true;
                    _t4BombBase = GetNodeOrNull<GameManager>("/root/Game")?.Bombs ?? 0;
                    _t4Bombed = false;
                }
                Hud.SetTutorialHint("X=ボム"); // 毎フレーム張り直し（全開トースト対策）
                int bombs = GetNodeOrNull<GameManager>("/root/Game")?.Bombs ?? 0;
                if (bombs < _t4BombBase) _t4Bombed = true; // ボム消費＝発動
                if (_t4Bombed || _tphaseTime > 6.0)
                {
                    GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                    // 撃たずに流れたときだけ「次は、ここぞで」で締める。撃てたら締め会話は省略。
                    if (_t4Bombed) TutNext(); // case9 を飛ばす
                    TutNext();
                }
                break;
            case 9: // T4 結果会話（撃たなかった時のみ来る：「次は、ここぞで」）
                if (TutTalk(delta, TutBombSkip)) TutNext();
                break;
        }
    }

    // 自弾の本数（ショット練習の達成計測用）。enemy=false の弾を数える。
    private int _t1ShotCount;
    private int CountPlayerBullets()
    {
        int n = 0;
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return 0;
        foreach (Node c in pool.GetChildren())
            if (c is Bullet b && b.Active && !b.IsEnemy) n++;
        return n;
    }

    private int CountEnemyBullets() => GetTree().GetNodesInGroup("enemy_bullets").Count;

    // ③練習弾：少数だけ右側からゆっくり横切らせる（密度を完全制御＝かすりやすい）。
    private void Tutorial_SpawnGrazeBullets()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        float px = Player?.GlobalPosition.X ?? 60f;
        for (int i = 0; i < 4; i++)
        {
            float y = 40f + i * 36f;
            pool.Spawn(new Vector2(Mathf.Min(360f, px + 95f + i * 10f), y), new Vector2(-26f, 0f), isEnemy: true, 3f, 1);
        }
    }
    private double _t3Refill;

    // ④練習弾：少し濃いめ。囲まれ感を出してからボムを促す。
    private void Tutorial_SpawnBombBullets()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        for (int i = 0; i < 10; i++)
        {
            float x = _rng.RandfRange(40f, 360f);
            float y = _rng.RandfRange(-40f, -4f);
            pool.Spawn(new Vector2(x, y), new Vector2(_rng.RandfRange(-16f, 16f), 50f), isEnemy: true, 3f, 1);
        }
    }
    private bool _t4Bombed;

    // ── ⑤ 浄化（道中の最初の1体に同期）──
    private bool _t5TalkDone;
    private bool _t5OkStarted;
    private void Tutorial_PurifyBegin(GameManager? game)
    {
        _t5PurifyBase = game?.PurifiedCount ?? 0;
        _t5TalkDone = false; _t5OkStarted = false;
        // 最初の1体を手動で湧かせ、出た“瞬間”に会話で割り込む。
        var e = new GlyphMote();
        World.AddChild(e);
        e.GlobalPosition = new Vector2(360f, 108f);
    }

    // ⑤の進行。浄化（+1）＋締めの「その調子。」まで終えたら true（Spawner 起動へ）。
    private bool Tutorial_PurifyStep(double delta, GameManager? game)
    {
        if (!_t5TalkDone)
        {
            if (TutTalk(delta, TutPurify))
            {
                _t5TalkDone = true;
                Hud.SetTutorialHint("敵の周囲パネルを全破壊=浄化");
            }
            return false;
        }
        bool purified = (game?.PurifiedCount ?? 0) - _t5PurifyBase >= 1;
        // 浄化前は毎フレーム指示を張り直す（全開トーストの自動Clearで消えても復帰する）。
        if (!_t5OkStarted) Hud.SetTutorialHint("敵の周囲パネルを全破壊=浄化");
        // 浄化されるまで操作させる（FBなし＝浄化しないと進めない設計）。
        // ただし1体が画面外へ逃げて全滅すると詰むので、未浄化なら湧き直す（密度は1体に保つ）。
        if (!purified && GetTree().GetNodesInGroup("enemies").Count == 0)
        {
            var e = new GlyphMote();
            World.AddChild(e);
            e.GlobalPosition = new Vector2(360f, 108f);
        }
        if (purified)
        {
            if (!_t5OkStarted) { _t5OkStarted = true; Hud.ClearTutorialHint(); }
            if (TutTalk(delta, TutPurifyOk))
            {
                _tutorialMidwaveTaught = true;
                return true;
            }
        }
        return false;
    }

    // ── ⑥ やさしさ全開（表示のみ。会話で止めない）──
    // 全開が初めて発生した瞬間に Banner＋ナレ。以降はフラグで通常演出（HUD既存トースト）に任せる。
    private double _t6NarrT;
    private void Tutorial_OverloadWatch(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game == null) return;
        if (!_t6Shown && game.JustOverloaded)
        {
            _t6Shown = true;
            Hud.ShowBanner("やさしさ全開！");
            // 直後ナレは「止めない」を守るため、会話バーでなく非停止の指示帯で短く出す。
            Hud.SetTutorialHint("満ちると5秒、光が溢れる。");
            _t6NarrT = 3.0;
        }
        if (_t6NarrT > 0)
        {
            _t6NarrT -= delta;
            if (_t6NarrT <= 0) Hud.ClearTutorialHint();
        }
    }
}
