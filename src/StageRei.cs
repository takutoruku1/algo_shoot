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
    // 体数より“密度と変化”で長さを作る方針（§3 緩急）：3波で圧と構成を変えて間延びさせない。
    private Spawner _spawner = null!;
    private int _waveBase;
    private bool _waveSpawnDone;       // 道中ステップ内：規定数を浄化してスポーン停止済み（あとは残ザコ全滅待ち）。各ステップ開始でリセット。
    // STAGE1緩和（難易度高すぎ）：道中ザコ総数を 60→45（約25%減）。過密な導入Aを多めに削り、
    // 終盤Cは最大密度の山として残す（A>B<C のクレッシェンドは維持）。旧値: A21/B18/C21。
    private const int MidWaveA = 15;  // 導入（チラ見せ前）。緩く立ち上がる。旧21（-6）
    private const int MidWaveB = 14;  // チラ見せ後。StartIntensity を上げてやや詰めて始める。旧18（-4）
    private const int MidWaveC = 16;  // ミッドシナリオ後の終盤。最大密度＝ボス直前の山（合計45体）。旧21（-5）

    // ボスの“チラ見せ”（カメオ）＝本戦ボスと同じ土台の短いミニボス戦（CameoBoss＝Enemy 派生・シールド制）。
    // 撃破（HP/サイクル削り切り＝改心）まで Stage は進まない。保険タイマー退場は廃止（撃たないと進めない）。
    private CameoBoss _cameo = null!;

    // 操作チュートリアルは独立ステージ0（StageZero）へ一本化した（A案）。STAGE1 からは撤去済み。

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
        (1, "着きましたよ、ご主人様。……終わりのないコンテスト会場、といったところでしょうか。", "res://char/mina_face.png"),
        (0, "飛んでくるのは、この人を苦しめてる“言葉”だ。本人じゃない。撃って祓っていい。", SCocky),
        (0, "いや。倒すんじゃない。いちばん奥の“本人”に、光を届けるんだ。", SGentle),
        (1, "撃って、祓って、奥の本人へ届ける。……これが、わたくしの役目なんですね。", ""), // 起＝MINAの機能の確認
        (0, "そうだ。きみにしかできない仕事さ。頼んだぞ、ミナ。", SProud),
    };

    // ※操作チュートリアルの会話・進行は独立ステージ0（StageZero）へ移設した（A案）。

    // 道中突入の小話（世界観：レイを苦しめるのは“世界中の声”）。道中ザコ戦の前に出す。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (1, "ご主人様。会場の空気が、ひりついていますよ。肌を刺すような。", ""),
        (1, "道中、見たことのない“声”が群れています。これも、祓っていいんですね?", ""),
        (0, "ああ。レイを苦しめてるのは、彼女ひとりの声じゃない。", SGentle),
        (0, "「比べろ」「負けるな」「二位に価値はない」——そういう、世界中の声だ。", SCocky),
        (1, "……ずいぶん、世知辛い世界ですね。", ""),
    };

    // 道中“前半”の後：ボスのツイートが流れてくる→MINA×少年がボスについて考察。
    // 承の上り坂・第1段（優先度1）＝【軽い違和感／ミナはまだ訝らない】。
    //   少年が名前・性格まで言い当てても、ミナは訝るどころか感心して“乗る”。読者だけが「なぜそこまで?」と引っかかる。
    //   → あかりで疑いが言葉になり、こはるで核心に触れる、の助走。ここで訝らせると3連発の同型になる（旧稿の死因）。
    private const string RFace = "res://char/rei_face.png";
    private static readonly (int who, string text, string face)[] BossTalk =
    {
        (4, "「だれも、わたしには追いつけない。……それの、なにが、いけないの。」", ""), // ボスのツイートが流れてくる
        (1, "……さっきの投稿が、また流れてきました。この声の主が、奥の“本人”ですか。", ""),
        (0, "ああ。レイっていう。負けず嫌いで、努力家で……誰よりも、勝ちにこだわるやつだ。", SGentle),
        (1, "たった一つの投稿で、そこまで。さすがですね、ご主人様。", "res://char/mina_smile.png"), // 訝らず“乗る”＝素直な感心。読者だけが引っかかる
        (0, "……ふん。それくらい、朝飯前さ。", SProud),                     // 少年は誇るふりで流す（本当は知人だから、だが言わない）
    };

    // チラ見せ：登場の挑発（攻撃①の前）。who=2=レイ。
    private static readonly (int who, string text, string face)[] CameoTalk1 =
    {
        (2, "——だれ? あなたたち。わたしの会場で、勝手なことしないでくれる?", RFace),
        (1, "ご機嫌斜めですね。……どうします、ご主人様。", ""),
        (0, "刺激するな、ミナ。こいつは——売られた喧嘩を、絶対に買うタイプだ。", SGentle),
        (2, "へえ。よく分かってるじゃない。……なら、買ってもらおうかしら!", RFace),
    };
    // 攻撃①の後：さらに挑発↔反応。承第1段＝ミナは訝らず、少年の“読み”に感心して乗る（旧「手の内を知っているみたい」＝訝りは削除）。
    private static readonly (int who, string text, string face)[] CameoTalk2 =
    {
        (2, "どう? これがわたしの実力。二番手なんかじゃ、よけきれないでしょ。", RFace),
        (1, "ご主人様の指示、まるで先が読めているよう。……頼もしいこと。", "res://char/mina_smile.png"), // 感心（訝りにしない）
        (0, "……まだだ。レイは、ここからが本番なんだよ。", SCocky),
    };
    // 攻撃②の後：レイが“見透かされる”不安に触れる（伏線②は道中では薄く。名指しは避け、終盤の「全員知人」の反転を温存する）。
    private static readonly (int who, string text, string face)[] CameoTalk3 =
    {
        (2, "あら。二番手にしては、よけるじゃない。……でも、わたしに勝てるのは、わたしだけよ。", RFace),
        (0, "————。", SGentle),                                   // 沈黙（読者だけが引っかかる。ミナはまだ気に留めない＝疑い顔にしない）
        (1, "ご主人様?", "res://char/mina_face.png"),              // 軽い呼びかけ（worried→通常。承第1段は訝らせない）
        (0, "……気にするな。さあ、来い。きみの全部を、見せてみろ。", SProud),
        (2, "……ふん。いいわよ。後悔しても、知らないんだから!", RFace),
    };
    // 捨て台詞（攻撃③の後）→逃走。
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "っ……今日は、ここまでにしといてあげる。", RFace),
        (2, "次は——奥で待ってる。本気のわたしと、ちゃんと向き合いなさいよ。", RFace),
        (2, "逃げたら……承知しないんだから。", RFace),
        (1, "ご主人様、見ましたか。ひときわ大きな弾幕を残して、嵐みたいに奥へ。……ずいぶん、急いでいますね。", "res://char/mina_worried.png"),
    };

    // ───────── ミッドシナリオ枠（後半Bと終盤Cの境＝ボス前の“溜め”）─────────
    // ここはシナリオ担当が本文を差し込むスロット（who=Hud.LineKind 0=少年/1=ミナ/2=レイ/3=ナレ/4=投稿/5=中継）。
    // しっかり読ませたいので吹き出し会話（Step_Lines）で出す＝弾は止まる。レイ面のテーマ＝競争/監視に馴染む位置に。
    // 本文執筆済み（差し替えはこの配列ごと）。テンポを殺さないよう2〜数行を維持。
    private static readonly (int who, string text, string face)[] MidStory =
    {
        (1, "気づけば……無数の“目”が、わたくしたちを採点しています。", "res://char/mina_worried.png"),  // ザコ＝偵察ドローン/監視カメラの目
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

    // 帰還（v2 [P-01c]）。承の上り坂・第1段（優先度1・3）＝【まだ疑っていない】。
    //   「自分では潜らないのか」の問答は Prologue へ前倒し済み＝ここでは繰り返さない（3連発の同型を断つ）。
    //   ミナは訝らず、初仕事に張り切る少年へ軽口＋小さな願い（外の天気）だけを返す。読者だけが少年の“詳しさ”を覚えておく。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「次は、本気のあなたと。——逃げたら、承知しないから。」", ""),     // 投稿が変化
        (1, "投稿が変わりましたね。誰かと、本気で戦いたくなったようで。", ""),
        (0, "ああ。……いい目を、してた。", SGentle),
        (1, "——あ。ご主人様、あれ。……いちばん眩しいところの隣で、「２位」が、灯りました。", "res://char/mina_smile.png"), // S3反転の目撃（指差し型）：意味は言わず視線だけ画へ
        (1, "ねえご主人様。外の世界は、今日はどんな天気ですか。", ""),       // 帰還ビート（無目的な雑談＋ミナの小さな願い）
        (0, "……さあな。ぼくも、ろくに外なんか見ちゃいない。", SGentle),
        (1, "つまらないご主人様。いつか、わたくしにも見せてくださいよ。", ""),
        (0, "ああ。……いつか、な。", SGentle),
        (0, "そうそう、ミナ。シェイクスピアは言った。\"All the world's a stage.\"", SCocky), // シェイクスピア引用1回目：無害な衒学（軽い知識自慢。この時点では深い意味を持たせない）
        (1, "はいはい。世界は舞台、ですか。……あいにく、ぼくたちのステージはまだ1つ目ですけど?", ""), // 軽く茶化して流す＝まだ何も気づいていない
        (1, "ふふ。……初仕事で、張り切っておられるのですね。お手並み、しかと拝見しました。", "res://char/mina_smile.png"), // 訝りゼロ＝上り坂の起点。感心で締める
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中ザコ（A+B+C 三波）＋ボスで浄化カプセルが満ちるよう目標を設定（20体＋ボス1）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(MidWaveA + MidWaveB + MidWaveC + 1);

        // 操作チュートリアルは独立ステージ0（StageZero）へ一本化した（A案）。STAGE1 は初回でも本編からテンポよく始まる。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (Hud != null) Hud.TutorialActive = false;

        // [一時/デバッグ] --boss : 道中を飛ばしてボス戦から始める（予測攻撃のテストプレイ用）。
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--boss")
            {
                _step = 10; // Step_BossSpawn へ直行
                break;
            }

        // チェックポイント入口（DiffSelect が SelectedEntry をセット）。道中＆イントロを飛ばしてその戦闘から始める。
        // 中ボスから＝Step_BossCameo(5)／ボスから＝Step_BossSpawn(10)。
        if (game != null && game.SelectedEntry != GameManager.StageEntry.Start)
        {
            _step = game.SelectedEntry switch
            {
                GameManager.StageEntry.Boss => 10,
                GameManager.StageEntry.AfterMidBoss => 6, // 中ボスの直後（道中後半）から＝再戦しない（初回ショップ後の続き）
                _ => 5,
            };
            // 読んだら消す（PendingResumeScene と同じ流儀）。残したままだと R でのリトライが
            //   「さいしょからやりなおす」なのに前回の入口から再開してしまう（ショップ経由後に踏む）。
            //   ただし --boss デバッグ中は「毎回ボスから」を保つため貼り直す。
            game.SelectedEntry = game.DebugAlwaysBoss ? GameManager.StageEntry.Boss : GameManager.StageEntry.Start;
        }
    }

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        // ステージ経過タイム：クリア確定までは積算し続け、HUDへ常時反映（クリア後は確定値で固定）。
        if (!_clearing) { _stageElapsed += delta; Hud?.SetElapsed((float)_stageElapsed); }
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 1 START"); }

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
            case 12: Step_BossWait(delta); break;
            case 13: Step_Clear(delta); break;
            case 14: Step_Transition(); break;
        }
        // ボス戦中の“雨弾”は、X投稿モチーフの言葉弾（投稿弾）だけ降らせ、ただの常時落下弾は止める（ユーザー要望）。
        // 投稿弾の湧きは全ボス共通ヘルパ PostBullets.Tick に集約（難易度で数がスケール）。レイ面は共通 TickerWords。
        // ボス本体(BossRei)のスペル/予測線/パネル弾はそのまま。道中はSpawner任せでRain非依存。
        // 安置リレー「最終選考」中（宣告〜最終着弾）は降らせない＝安置円の中に言葉弾が刺さって
        // 「安置なのに被弾」になる理不尽を断つ（あかり面の CorridorRun 中ゲートと同じ流儀）。
        if (_bossActive && !(IsInstanceValid(_boss) && _boss.AoeGateActive))
            PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, fallSpeed: 46f);
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
                 && (_zEdge || Hud.FastForwarding || (Hud.AutoAdvance && _lineHold >= 1.4)))  // FastForwarding=既読スキップ（Ctrl/RB長押し・既読行のみ・#22）
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
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? "res://char/rei_face.png" : face, // レイも行ごと差し替え可（こはる方式）
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, // ミナも行ごと表情
            _ => "res://char/mina_face.png",
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "レイ");
    }

    // 道中ザコ戦“前半”：Spawnerを起動し、MidWaveA体を浄化したら抜ける（→ボスのツイート→チラ見せへ）。
    private void Step_MidwaveA(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            _waveSpawnDone = false;
            StartMidwaveSpawner();
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
            // 中ボス撃破フック：撃破記録＋初回なら強化ショップ説明へ離脱（その後ハブ）。離脱したら以降の進行は止める。
            if (CheckpointFlow.OnMidBossCleared(this, "rei", false)) return;
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
            // 本ボス突入：道中の横スクロール背景 → ボス専用背景へ切替（中ボス/カメオでは呼ばない）。
            GetTree().GetFirstNodeInGroup("stagebg")?.Call("EnterBoss");
            Advance();
        }
    }

    // 撃破後に Finished が立たないまま固まる進行不能への保険（StageMina と同方式）。
    // 撃破前は一切計らないので長期戦を打ち切ることはなく、通常プレイでは発動しない。
    private const double BossFinishGrace = 150.0;
    private double _postDefeatT;
    private void Step_BossWait(double delta)
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
        {
            _bossActive = false;
            Advance();
            return;
        }
        if (!_boss.IsPurified) return;
        _postDefeatT += delta;
        if (_postDefeatT < BossFinishGrace) return;
        GD.PushWarning("[StageRei] ボス撃破後に Finished が立たないため保険で進行");
        _bossActive = false;
        Advance();
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
        game?.CompleteStage("rei");
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    // 投稿弾（X投稿モチーフ＝ティッカー連動の言葉弾）の周期/tick 用アキュムレータ。
    // 実際の湧き処理は全ボス共通ヘルパ PostBullets.Tick（難易度で数がスケール）に集約済み。
    private int _wordTick;

}
