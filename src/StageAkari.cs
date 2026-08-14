using Godot;
using System.Linq;

// StageAkari : STAGE2「あかり（雨の教室）」進行。
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
    private double _stageElapsed;   // ステージ全体の経過秒（クリア確定まで・ポーズ中は止まる）。
    private float _clearTime;       // クリア確定時の経過秒。
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
    private const string SAfraid = "res://char/shonen_afraid.png"; // 怯え・崩れ（承第2段：少年の動揺を表情で見せる）

    // 道中ザコ戦（Spawner）。三部構成で「後半ほど圧が上がる」緩急を作る。あかりは型崩し（S2）で“カメオ先出し”：
    //   肩慣らし0（圧ゼロ）→ チラ見せ（6体目の浄化で割り込み）→ 小話 → 前半A（緩い導入）→ 考察 → 後半B（やや詰める）→ ミッドシナリオ（溜め）→ 終盤C（最大密度）→ 本ボス。
    // 体数より“密度と変化”で長さを作る（§3 緩急）：3波で圧と構成を変えて間延びさせない。
    private Spawner _spawner = null!;
    private int _waveBase;
    private bool _waveSpawnDone;       // 道中ステップ内：規定数浄化でスポーン停止済み（残ザコ全滅待ち）。各ステップ開始でリセット。
    // M2バランス：道中ザコ総数を STAGE1（Rei）と同じ 60→45 に緩和（A>B<C のクレッシェンドは維持）。旧値: A21/B18/C21。
    // M3：Intro直後にいきなり中ボスの唐突さを解消するため、カメオ前に“肩慣らし”0波を挿入。総数45は維持（6+12+13+14）。
    private const int MidWave0 = 6;   // 肩慣らし（Intro直後・StartIntensity 0）。6体目の浄化でカメオが割り込む。
    private const int MidWaveA = 12;  // 導入（チラ見せ＝先出しの後）。緩く立ち上がる。旧15（-3）
    private const int MidWaveB = 13;  // 考察の後。やや詰めて始める。旧14（-1）
    private const int MidWaveC = 14;  // ミッドシナリオ後の終盤。最大密度＝ボス直前の山（合計45体）。旧16（-2）
    // ボスの“チラ見せ”（カメオ）＝本戦ボスと同じ土台の短いミニボス戦（CameoBoss＝Enemy 派生・シールド制）。
    // あかり＝怯え・自責で、攻撃も悲嘆寄り。撃破（HP/サイクル削り切り＝改心）まで Stage は進まない。保険退場は廃止。
    private CameoBoss _cameo = null!;

    // ダイブ前の会話（v2 [P-02a]）。承の上り坂・第2段（優先度1・3）＝【疑いが言葉になりかけ／少年が動揺して崩れる】。
    //   レイ面では訝らなかったミナが、この面で初めて“いつもと違う”に口をつける。少年は Stay を言い忘れ、動揺して逸らす。
    //   異変は説明せず、少年の afraid（怯え・崩れ）表情＋ミナの doubt（怪訝）で見せる（show don't tell）。
    // who: 0=少年 / 1=ミナ / 2=相手 / 3=地の文 / 4=投稿 / 5=中継。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (1, "ご主人様、いま、あくびしましたね。声が、半分とけていましたよ。", "res://char/mina_smile.png"), // 無目的な雑談（声で分かる＝視点の断絶を守る）
        (0, "……地獄耳だな、きみは。AIのくせに。", SCocky),
        (1, "ご主人様。次の“成敗”は? 今日はずいぶん静かですね。", ""),
        (0, "……ああ、悪い。ちょっと考えごとだ。", SGentle),
        (4, "「すき、すき、すき。……ひとつでいいから、本物になって。」", ""),     // 投稿
        (0, "…………この人の、ところへ行こう。", SAfraid),                    // 声の色が違う＝崩れの初出（afraid）
        (1, "おや。決めゼリフはどうしたんですか。……それに、いつもの「Stay」も。", ""),
        (0, "……いいから。行くぞ。", SGentle),
        // 承第2段：ミナの疑いが初めて“言葉”になる。ただし核心には届かない（そこはこはる面まで温存）。
        (1, "ご主人様。……あなた、今日はどこか、へん、ですよ。", "res://char/mina_doubt.png"), // 疑いが言語化（レイでは無かった一歩）
        (0, "……っ、へんじゃない。ぼくはいつも通りだ。行くぞ。", SAfraid),      // 動揺して否定＝崩れ（afraid）。読者だけが“図星”と分かる
        (1, "……ご主人様。", "res://char/mina_doubt.png"),
        (1, "ご主人様、雨が、降りやみません。机も、椅子も……天井へ、落ちていく。おかしな教室ですね。", "res://char/mina_face.png"),
    };

    // ボス登場時の説明（v2 [P-02b]。who: 0=少年 / 1=ミナ）
    private static readonly (int who, string text, string face)[] BossIntro =
    {
        (1, "黒板も、机も、窓も……ぜんぶ「すき」で、埋め尽くされていますね。", ""),
        (0, "……この人は、好きという気持ちを、持て余してる。誰にも渡せないまま。", SGentle),
        (0, "————渡せなかった想いってのは、ああやって、あふれるんだ。この世界ではね。", SGentle),
    };

    // 道中の短い掛け合い（小話集_v1.md §2 StageAkari）。1〜3行厳守・テンポ優先。
    private static readonly (int who, string text, string face)[] Chat1 = // [日常]
    {
        (1, "雨、やみませんね。……傘、お持ちですか、ご主人様。", ""),
        (0, "持ってる。折れてるけどな。", SCocky),
        (1, "折れた傘をお持ちの方は、それを持っていないと申します。", ""),
    };
    private static readonly (int who, string text, string face)[] Chat2 = // [軽口]
    {
        (0, "ミナ。ぼく、雨の音は好きだ。", SGentle),
        (1, "存じております。うるさいと言いながら、いつも消さないので。", "res://char/mina_smile.png"),
    };
    private static readonly (int who, string text, string face)[] Chat3 = // [日常]
    {
        (1, "水たまりを踏むと、なぜ少し楽しいんでしょうね。", ""),
        (0, "……大人はそれをやると怒られるからだ。", SCocky),
        (1, "では、わたくしは踏み放題ですね。", "res://char/mina_smile.png"),
    };
    private static readonly (int who, string text, string face)[] Chat4 = // [情緒]
    {
        (1, "この吹き出し、ぜんぶ「またね」と書いてあります。", "res://char/mina_worried.png"),
        (0, "————", SAfraid),
        (1, "……祓います。ご主人様は、見なくていいです。", ""),
    };
    private static readonly (int who, string text, string face)[] Chat5 = // [軽口]
    {
        (1, "ご主人様、髪。跳ねていませんか、今日。", ""),
        (0, "見えてないだろ、きみからは。", SCocky),
        (1, "声で分かります。跳ねている人の声です。", "res://char/mina_smile.png"),
    };

    // 道中突入の小話（世界観：自責の声の雨）。道中ザコ戦の前に出す。
    private static readonly (int who, string text, string face)[] Mid = new (int, string, string)[]
    {
        (1, "ご主人様、ごらんになって。雨の中を……白い吹き出しが、いくつも漂っています。", "res://char/mina_face.png"),
        (1, "ここの声は……どれも、「ねえ見て」「すき」と、すがりついてきます。", ""),
        (0, "ああ。たった一人に渡せなかったぶん、誰彼かまわず掴もうとしてる。", SGentle),
        (1, "……ご主人様は、その“たった一人”を、知っているみたいに言うんですね。", "res://char/mina_doubt.png"), // 疑いを一歩具体化（第2段）
        (0, "————行くぞ。", SAfraid),                          // 答えず逸らす＝崩れ（afraid）で一貫
    }.Concat(Chat1).ToArray();

    // 道中“前半”の後：ボスのツイートが流れてくる→考察。承第2段（優先度1・3）＝【思わず情がこぼれ、動揺で蓋をする】。
    //   少年が“見てきたような”細部（笑い方）を口走り、直後にハッと蓋をする（afraid）。ミナは「?」で追うが、核心＝“知人だ”とは
    //   まだ言い切らせない（そこはこはる面へ温存）。優先度3：説明的な「知っている人みたい」を弱め、崩れ＝表情で見せる。
    private const string AFace = "res://char/akari_face.png";
    private static readonly (int who, string text, string face)[] BossTalk = new (int, string, string)[]
    {
        (4, "「すき、すき、すき。……ひとつでいいから、本物になって。」", ""), // ボスのツイート
        (1, "……また、あの投稿が流れてきました。奥の“本人”は、ずいぶん思いつめていますね。", ""),
        (0, "……あかり、っていうんだ。やさしくて、笑うと、目が三日月になって——", SGentle), // 細部が思わずこぼれる（言いさし）
        (0, "————。……いや。", SAfraid),                                    // ハッと蓋をする＝崩れ（afraid）。言い切らない
        (1, "……ご主人様?", "res://char/mina_doubt.png"),                    // 追うが、まだ言葉にはしない（核心はこはる面へ）
        (0, "……なんでもない。行くぞ。", SGentle),
    }.Concat(Chat2).Concat(Chat3).ToArray();

    // チラ見せ：登場（あかり＝怯え・拒絶）。who=2=あかり。
    private static readonly (int who, string text, string face)[] CameoTalk1 =
    {
        (2, "あ、来た来た! ねえ、あなた、あたしのこと、見てくれるの? 見て? 見てるよね? ね?", AFace),
        (1, "ずいぶん、怯えていますね。", ""),
        (0, "……刺激するな、ミナ。この子は、自分を責めることしか、できないんだ。", SGentle),
        (2, "ごめんなさい……ごめんなさい。あたしなんかが、すきになって……ごめんなさい。", AFace),
    };
    private static readonly (int who, string text, string face)[] CameoTalk2 =
    {
        (2, "見ないで……っ。こんな、みっともないところ。", AFace),
        (1, "……ご主人様。あなた、さっきから、この子を見る目が——", "res://char/mina_doubt.png"), // 疑いの目（worried→doubt で第2段に統一）
        (0, "……黙っててくれ、ミナ。頼むから。", SAfraid),           // 懇願＝崩れ（afraid）。“頼むから”に情が漏れる
    };
    // 山：あかりが少年の声に気づきかけ、自分で否定する。
    private static readonly (int who, string text, string face)[] CameoTalk3 =
    {
        (2, "逃げないでよぉ。あたし、追いかけるの得意なの。既読も三秒でつけるし。", AFace),
        (0, "————。", SGentle),                                    // 沈黙
        (2, "ねえ、あなたの声……ちょっと好きかも。あ、いま好きって言った。責任、とってね?", AFace),
        (0, "————っ。……行くぞ、ミナ。今は、まだ。", SGentle),
    };
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "えー、もう行っちゃうの? ……ちぇっ。ぜったい、また会いに来てよね。ぜったいだよ?", AFace),
        (1, "……あの子、降りやまない雨の奥へ。逃げるみたいに、消えてしまいました。", "res://char/mina_worried.png"),
        (1, "……ご主人様。今の人を、放っておいて、いいんですか。", ""),
        (0, "……いいわけ、ないだろ。だから——奥で、ちゃんと、届けるんだ。", SGentle),
    };

    // ───────── ミッドシナリオ枠（後半Bと終盤Cの境＝ボス前の“溜め”）─────────
    // シナリオ担当が本文を差し込むスロット（who=Hud.LineKind 0=少年/1=ミナ/2=あかり/3=ナレ/4=投稿/5=中継）。
    // 吹き出し会話（Step_Lines）で出す＝弾は止まる。あかり面のテーマ＝教室/告白に馴染む位置に。
    // 本文執筆済み（差し替えはこの配列ごと）。テンポを殺さないよう2〜数行を維持。
    private static readonly (int who, string text, string face)[] MidStory =
    {
        (1, "ご主人様、これ。宙に浮いた机に、開きっぱなしのノートが一冊。……同じ三文字だけが、ずっと、並んでいます。", "res://char/mina_worried.png"),  // ザコ＝宙浮きの机/開いたノート
        (1, "「すき」「すき」「すき」……書いては、消して。一度も、渡せなかったんですね。", "res://char/mina_worried.png"),
        (0, "ああ。言えなかった言葉は、消えやしない。こうやって、教室に残り続ける。", SGentle),
        (1, "……たった一言、伝わっていれば。それだけのことが、いちばん遠い。", ""),
        (0, "だから——奥で、ちゃんと言わせてやるんだ。最後まで言いそびれた、その続きを。", SCocky),
    };

    // 道中後の小話（ボスへの引き）。
    private static readonly (int who, string text, string face)[] MidEnd = new (int, string, string)[]
    {
        (1, "黒板の奥に、あの子が。……ご主人様、ほんとうに、いいんですね?", ""),
        (0, "……ぼくが、やらなきゃいけないんだ。", SGentle),
    }.Concat(Chat4).Concat(Chat5).ToArray();

    // 帰還（v2 [P-02c]）。承第2段の締め（優先度1・3）＝【疑いを口にするが、断定はさせない】。
    //   ミナは初めて「知ってるんですか?」と問う（レイでは無かった直接の問い）。少年は動揺しつつ嘘でかわす（afraid→取り繕い）。
    //   “知人だ”の確信はここでは持たせない＝こはる面で核心に触れる余地を残す。あかり残響＝伏線③（声が似ている）は温存。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「ほんと、バカなんだから。……あたしも、だけど。」", ""),       // 投稿が変化
        (2, "……あったかい声が、した。……なんでかな、あの人の声に、似てた。", ""), // あかり残響（伏線③）
        (2, "……でも、もう、ごめんねは言わない。あたしの好きは、まちがってなかった。", ""), // 自分の意思で前を向く（P4・尊厳）
        (1, "……字が、変わっていく。——『ありがとう』。……♥も、ひとつ。", ""), // S3反転の目撃（読み上げ型）：思わず読むだけ。解釈しない
        (1, "ご主人様。あなた——この人を、知ってるんですか?", "res://char/mina_doubt.png"), // 初めての直接の問い（worried→doubt）
        (0, "————っ。……まさか。赤の他人さ。", SAfraid),               // ひるんでから嘘（afraid）。“っ”に動揺が出る
        (1, "……即答までに、二秒かかりましたね。", "res://char/mina_doubt.png"),
        (0, "ミナ。シェイクスピアは言った。\"Parting is such sweet sorrow.\"", SCocky), // 話を逸らす（取り繕い）
        (1, "はいはい、教養アピールお疲れさまですね。……で、それは誰の話ですか。", "res://char/mina_smile.png"), // 追及を軽口で受ける＝まだ断定しない
        (0, "————一般論だよ。", SGentle),
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中（肩慣らし0＋A+B+C 三波）＋ボスで浄化カプセルが満ちる（部屋が晴れる）。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        game?.SetStageTarget(MidWave0 + MidWaveA + MidWaveB + MidWaveC + 1);

        // チェックポイント入口（DiffSelect が SelectedEntry をセット）。道中＆イントロを飛ばしてその戦闘から始める。
        // 型崩し（S2）対応：中ボス（カメオ先出し）＝Step_BossCameo(3)／ボスから＝Step_BossSpawn(11)。
        if (game != null && game.SelectedEntry != GameManager.StageEntry.Start)
        {
            _step = game.SelectedEntry switch
            {
                GameManager.StageEntry.Boss => 11,
                GameManager.StageEntry.AfterMidBoss => 4, // 中ボスの直後（小話→道中A）から＝再戦しない（初回ショップ後の続き）
                _ => 3,
            };
            // 読んだら消す（PendingResumeScene と同じ流儀）。残したままだと R でのリトライが
            //   「さいしょからやりなおす」なのに前回の入口から再開してしまう（ショップ経由後に踏む）。
            //   ただし --boss デバッグ中は「毎回ボスから」を保つため貼り直す。
            game.SelectedEntry = game.DebugAlwaysBoss ? GameManager.StageEntry.Boss : GameManager.StageEntry.Start;
        }
    }

    private bool _startBannerShown;

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        // ステージ経過タイム：クリア確定まで積算しHUDへ反映。
        if (!_clearing) { _stageElapsed += delta; Hud.SetElapsed((float)_stageElapsed); }
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 2 START"); }
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        _zEdge = z && !_zHeld;
        _zHeld = z;
        // 型崩し（S2）：あかりは“カメオ先出し”。着地後の肩慣らし波（6体・圧ゼロ）を捌いていると、
        // 6体目を浄化した瞬間にあかりが割り込んで飛び出してくる（「既読3秒」の性格＝向こうから会いに来る）。
        // 3ステージ同型（小話→道中→考察→カメオ）の反復を崩す。
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;
            case 2: Step_MidWave0(delta); break;          // 肩慣らし波（6体・圧ゼロ）＝カメオへの布石
            case 3: Step_BossCameo(delta); break;         // ボスのチラ見せ（先出し＝あかりから割り込んで来る）
            case 4: Step_Lines(delta, Mid); break;        // 道中突入の小話
            case 5: Step_MidwaveA(delta); break;          // 道中ザコ戦A（導入）
            case 6: Step_Lines(delta, BossTalk); break;   // ボスのツイート→考察（会った後＝少年の詳しさが際立つ）
            case 7: Step_MidwaveB(delta); break;          // 道中ザコ戦B（やや詰める）
            case 8: Step_Lines(delta, MidStory); break;   // ★ミッドシナリオ枠（ボス前の溜め）
            case 9: Step_MidwaveC(delta); break;          // 道中ザコ戦C（終盤＝最大密度の山）
            case 10: Step_Lines(delta, MidEnd); break;    // 道中後の小話
            case 11: Step_BossSpawn(); break;
            case 12: Step_Lines(delta, BossIntro); break; // ボスは出現済みだが会話中は止まる
            case 13: Step_BossWait(delta); break;
            case 14: Step_Clear(delta); break;
            case 15: Step_Transition(); break;
        }
        // ボス戦中の ambient は、全ボス共通の投稿弾（X投稿モチーフ＝ティッカー連動の言葉弾）に統一。
        // 旧「ただの自責の雨（落下弾）」は止め、Rei と同じく投稿弾のみ降らせる（難易度で数がスケール）。
        // あかり面も共通 TickerWords を引く（下を流れるコメントがそのまま降る一体感）。
        // ボス本体(BossAkari)のスペル/予測線/パネル弾はそのまま。
        // イライラ棒「雨の帰り道」（CorridorRun 展開中）は降らせない＝通路避けに弾を重ねる理不尽を断つ。
        if (_bossActive && GetTree().GetFirstNodeInGroup("corridor") == null)
            PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, fallSpeed: 48f);
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
            Hud.LineKind.Boy => face,                       // 少年（行ごとの表情）
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? "res://char/akari_face.png" : face, // あかりも行ごと差し替え可（こはる方式）
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, // ミナも行ごと表情
            _ => "res://char/mina_face.png",                // 中継ほか
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    // ---- 肩慣らし波（Intro直後・圧ゼロ）：MidWave0体の浄化で、待ち構えていたあかりが“割り込んで”カメオ出現 ----
    // いきなり中ボスの唐突さを消しつつ、「向こうから来る」性格は保つ（会話は挟まず即カメオ＝割り込み感）。
    private void Step_MidWave0(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            _waveSpawnDone = false;
            StartMidwaveSpawner();
        }
        // 規定数浄化（or 目標到達）で残ザコ・残弾を片付け、間を置かずカメオへ＝“6体目の瞬間に割り込む”。
        if (game != null && (game.PurifiedCount - _waveBase >= MidWave0 || game.StageCleared))
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // ---- 道中ザコ戦“前半”：Spawner起動→MidWaveA体浄化で考察（BossTalk）へ（型崩し後：カメオは既に済み） ----
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
        // 規定数浄化（or 目標到達）で節目＝スポーン停止＋倒し残しの居座りザコを片付けて進む。
        // 全滅ハント（60体の中で最後の1体探し）を要求しない＝進行不能を防ぐ。戦闘中の居座りは維持。
        if (game != null && (game.PurifiedCount - _waveBase >= MidWaveA || game.StageCleared))
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll(); // 会話（考察）前に片付ける
            Advance();
        }
    }

    // ---- 道中ザコ戦“B（後半）”：考察（BossTalk）の後。やや詰めて始める。MidWaveB体でミッドシナリオへ ----
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
        // 規定数浄化（or 目標到達）で節目＝スポーン停止＋居座り片付け＋ミッドシナリオへ（全滅ハント不要＝進行不能を防ぐ）。
        if (game != null && (game.PurifiedCount - _waveBase >= MidWaveB || game.StageCleared))
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // ---- 道中ザコ戦“C（終盤）”：ミッドシナリオの後。最大密度でボス直前の山を作る。MidWaveC体で本ボスへ ----
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
        // 規定数浄化（or 目標到達）で節目＝スポーン停止＋居座り片付け＋本ボスへ（全滅ハント不要＝進行不能を防ぐ）。
        if (game != null && (game.PurifiedCount - _waveBase >= MidWaveC || game.StageCleared))
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    private void StartMidwaveSpawner(float startIntensity = 0f)
    {
        if (_spawner != null) return;
        _spawner = new Spawner { Name = "Spawner", World = World, Theme = StageTheme.Akari, StartIntensity = startIntensity };
        AddChild(_spawner);
        _spawner.Begin();
    }

    private void ClearStageEnemies()
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e) e.QueueFree();
    }

    // ---- ボスの“チラ見せ”：CameoBoss（本戦ボスと同じ Enemy 派生・シールド制・BossMover）を1体スポーン ----
    // 撃破（HP/サイクル削り切り＝改心）して捨て台詞を流し切る（Finished）まで Stage は進まない。保険退場は無し。
    private void Step_BossCameo(double delta)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _cameo = new CameoBoss
            {
                Name = "AkariCameo",
                Theme = new CameoTheme
                {
                    DisplayName = "あかり", Handle = "@akari.",
                    PreTex = "res://char/enemy_akari_pre.png",
                    CryTex = "res://char/enemy_akari_cry.png",
                    PostTex = "res://char/enemy_akari_post.png",
                    Face = AFace,
                    SpellTint = new Color("6c9cd8"), SpellShape = BulletShape.Needle,
                    Fire = CameoFireTheme.AkariGrief,
                    Aura = FxLayer.BossAura.Akari,
                    Bgm = Audio.Instance?.BgmBossAkari,
                    IntroLines = CameoTalk1, TauntLines = CameoTalk3, DefeatLines = CameoPost,
                },
            };
            World.AddChild(_cameo);
            _cameo.GlobalPosition = new Vector2(SpawnX, 70f);
        }

        // 撃破→捨て台詞を流し切ったら次フェーズへ（型崩し後：道中突入の小話 Mid へ）。
        if (!IsInstanceValid(_cameo) || _cameo.Finished)
        {
            Hud.HideBossBar();                                   // バー出っ放しにしない（後で本ボスが再表示）
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            if (IsInstanceValid(_cameo)) _cameo.QueueFree();
            // 中ボス撃破フック：撃破記録＋初回なら強化ショップ説明へ離脱（その後ハブ）。離脱したら以降の進行は止める。
            if (CheckpointFlow.OnMidBossCleared(this, "akari", false)) return;
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
            // 本ボス突入：道中の横スクロール背景 → ボス専用背景へ切替（中ボス/カメオでは呼ばない）。
            GetTree().GetFirstNodeInGroup("stagebg")?.Call("EnterBoss");
            Advance(); // 出現と同時に説明会話へ（会話中はボス停止・雨も止む）
        }
    }

    // ---- 3: ボス戦（浄化＆会話完了まで） ----
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
        GD.PushWarning("[StageAkari] ボス撃破後に Finished が立たないため保険で進行");
        _bossActive = false;
        Advance();
    }

    // ---- 5: クリア（帰還の会話を手動送り） ----
    private bool _clearBannerShown;
    private void Step_Clear(double delta)
    {
        if (!_clearBannerShown)
        {
            _clearBannerShown = true;
            _clearTime = (float)_stageElapsed;
            var game = GetNodeOrNull<GameManager>("/root/Game");
            var rec = game?.RecordClearTime("akari", game.Difficulty, _clearTime) ?? (true, (float?)null);
            Hud.ShowClearBanner("STAGE 2 CLEAR", _clearTime, rec.isBest, rec.prev);
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

    // 投稿弾（ティッカー連動の言葉弾）の周期/tick 用アキュムレータ。
    // 湧き処理は全ボス共通ヘルパ PostBullets.Tick に集約（難易度で数がスケール）。
    private int _wordTick;
}
