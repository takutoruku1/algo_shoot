using Godot;
using System.Linq;

// StageKoharu : STAGE2「こはる（電気を消した部屋と、誰も見ていない教室）」進行。
//   1: 導入会話（S2-1。案C では少年は存在しない＝語り手はミナ一人）
//   2〜8: 道中（部屋 → 教室 → 部屋。中ボス／入力欄／我に返る一拍）
//   9〜11: ボス出現・ボス戦（浄化＝改心で会話完了まで）
//   12〜13: クリア（配信画面が灯る）→ ハブ
// 台詞の正典: wiki/08_仮台本/07_粗い台本_案C_2_こはるとレイ.md（ユーザー承認済み・2026-09-05）の S2-1〜S2-9。
public partial class StageKoharu : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stageElapsed;   // ステージ全体の経過秒（クリア確定まで・ポーズ中は止まる）。
    private float _clearTime;       // クリア確定時の経過秒。
    private double _lineHold;
    private int _introLine;
    private BossKoharu _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private bool _zHeld;
    private bool _zEdge;
    private bool _startBannerShown;

    private const float SpawnX = 300f;

    // ミナの表情（案C では語り手はミナ一人＝行ごとに顔を差し替える）。
    private const string MFace = "res://char/mina_face.png";
    private const string MSmile = "res://char/mina_smile.png";
    private const string MWorried = "res://char/mina_worried.png";
    private const string MDoubt = "res://char/mina_doubt.png";

    // 道中ザコ戦（Spawner）。三部構成で「後半ほど圧が上がる」緩急を作る。こはるは型崩し（S2）で
    //   前半A（緩い導入）→ チラ見せ → 後半B（やや詰める）→ 終盤C（最大密度）→ 本ボス（HP半分でミッドシナリオ割込み）。
    // 体数より“密度と変化”で長さを作る（§3 緩急）：3波で圧と構成を変えて間延びさせない。
    private Spawner _spawner = null!;
    private int _waveBase;
    // M2バランス：道中ザコ総数を レイ面と同じ 60→45 に緩和（A>B<C のクレッシェンドは維持）。旧値: A21/B18/C21。
    private const int MidWaveA = 15;  // 導入（チラ見せ前）。緩く立ち上がる。旧21（-6）
    private const int MidWaveB = 14;  // チラ見せ後。やや詰めて始める。旧18（-4）
    private const int MidWaveC = 16;  // 終盤。最大密度＝ボス直前の山（合計45体。ミッドシナリオはボス戦中に割込み）。旧21（-5）
    // ボスの“チラ見せ”（カメオ）＝本戦ボスと同じ土台の短いミニボス戦（CameoBoss＝Enemy 派生・シールド制）。
    // こはる＝無力・他責で、弾は“落ちる祈り”。撃破（HP/サイクル削り切り＝改心）まで Stage は進まない。保険退場は廃止。
    private CameoBoss _cameo = null!;

    // S2-1 部屋・導入（仮台本 07）。電気を消した部屋。配信画面の光だけ。棚のグッズ、机の下の箱。
    //   炎上はまだ無い。【濁】兆候。ミナは投稿の下の小さな声を見つけるが、中身は S2-4 まで言わない。
    // who: 0=あなた（送信された下書き） / 1=ミナ / 2=こはる / 3=システム表示 / 4=投稿。who=5（中継）は使わない。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (1, "ご主人様。……暗いですね。電気の消えた部屋に、画面の光だけ。", MFace),
        (4, "「今日の配信も最高だった。これで、明日も学校、行ける。」", ""),   // 層1。本人の主投稿。明るい
        (1, "……にぎやかな投稿ですね。——この投稿の下から、も。聞こえます。……ずいぶん、小さな声が。", MWorried),   // 中身は言わない
        (1, "壁一面が、画面。中で、笑っている人がひとり。……こちらへ向いて、笑っています。", MFace),   // 映るのはガワの笑顔だけ
        (1, "机の下に、箱が三つ。……開けられた跡は、ひとつだけ。", MFace),
        (1, "行きます。——放っておけないので。", MFace),
    };

    // S2-3 Mid（部屋）＋ Chat1（軽口）（仮台本 07）。ペンライトの光が画面に届かない。
    //   「むだだ」の合間に学校の声と家の声が同じ色で混じる。責める宛先がどこにもない。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (1, "ペンライトの光が、画面に向かって、振られています。……届いていません。画面まで。", MWorried),
        (1, "ここの声は……「むだだ」と、繰り返しています。合間に、「今日も明るいね」と、「模試、どうだった」が。同じ色の声で。", MFace),   // 学校の声と家の声
        (1, "わたくしも振ってみたいのですが。……手が、ありません。振るのは、光のほうにお願いします。", MSmile),   // Chat1（軽口）
    };

    // S2-2 中ボスの受け（仮台本 07）。CameoBoss は who=2（本人）の行だけを一行オーバーレイで流すので、
    //   本人の合間に入るミナの観測行はオーバーレイに乗らない。中ボスの直前に開くこの step が受け皿になる
    //   （step 構成は変えない前提での置き場所。あかり面と同じ流儀）。
    private const string KFace = "res://char/v3/koharu_face.png";       // 学校の明るい顔
    private const string KPale = "res://char/v3/koharu_face_pale.png";  // 途中でこぼれる蒼白
    private const string KLit = "res://char/v3/koharu_face_lit.png";    // 配信画面の光を浴びた顔
    private static readonly (int who, string text, string face)[] BossTalk =
    {
        (1, "……画面の中の人。星逢レイ、と、名前が出ています。笑顔が、こちらを向いたまま、動きません。", MFace),   // 観測のみ。裏は見せない
    };

    // S2-2 中ボス こはる（仮台本 07）。制服、片手に消えたペンライト、もう片手にスマホ。
    //   明るさと蒼白を往復する。第一声→RECLOSE（順送り）→捨て台詞、の三段で CameoBoss に渡す。
    private static readonly (int who, string text, string face)[] CameoTalk1 =
    {
        (2, "あ、来た来た。……あたし、なにしてんだろ、って顔してる? ……してないよ。してないってば。", KFace),   // 第一声
    };
    // RECLOSE（サイクルごとに順送り）。「やめないで……止まったら」の型。
    private static readonly (int who, string text, string face)[] CameoTalk3 =
    {
        (2, "見てって、これ。推しの配信。今日も来てるんだ、あたし。……", KLit),
        (2, "……何時間、見てるんだろ。塾……", KPale),   // 途中で蒼白
        (2, "——なんでもない。ね、楽しいでしょ? 楽しいってば。", KFace),   // すぐ明るく戻る
    };
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "はい、これ。ペンライト。振ってみて。楽しいから。ぜったい、楽しいから。", KFace),   // 捨て台詞
    };

    // S2-3 BossTalk（教室）＋ Chat2／Chat3（仮台本 07）。場所が変わる。席は全部埋まっているのに
    //   どの席もこちらを見ていない。視線だけがある。黒板に「期待」。文字はここ一箇所。
    private static readonly (int who, string text, string face)[] ClassTalk =
    {
        (1, "……場所が、変わりました。教室。席は、ぜんぶ埋まっているのに——どの席も、こちらを見ていません。視線だけが、あります。", MWorried),
        (1, "黒板に、二文字。「期待」。……消す人が、いないようです。", MFace),
        (4, "「今日も明るいねって言われた。……何の話してたか、覚えてない。」", ""),   // 層3
        (1, "……明るい声で、投稿しています。明るい、と、言われたことを。", MFace),
        (1, "机の列を、数えました。四十。……座っている人の顔は、ひとつも、見えません。", MFace),   // Chat2（日常）
        (1, "期待、という字は、画数が多いですね。……消すのも、手間がかかりそうです。", MSmile),   // Chat3（軽口）
    };

    // ───────── S2-4 入力欄（ミッドシナリオ枠。仮台本 07）─────────
    // 配信画面の下のコメント入力欄。「レイちゃんが」まで打たれて、一文字ずつ消える。
    // 「今日も来ました」だけが残って、送られる。ミナは消えたほうの一行を拾い、中身は本人の前まで言わない
    //（S2-8 の決定打の一段目で返す）。台本どおり選択は置かない。
    // who=3（システム表示）＝入力欄そのもの。この2行だけは Hud のナレ用中央テロップに流さず、
    //   専用オーバーレイ CommentInput（配信画面のコメント欄の姿）へ渡して打つ／消すを見せる
    //   （Step_InputField 参照）。行末の「|」は表示側がカーソルとして描くので渡す前に落とす。
    private static readonly (int who, string text, string face)[] InputField =
    {
        (1, "ご主人様、これ。配信画面の下に、コメントの入力欄が。……文字が、打たれています。", MFace),
        (3, "レイちゃんが|", ""),   // 入力欄。カーソル付き
        (1, "……消えていきます。一文字ずつ。", MWorried),
        (3, "今日も来ました|", ""),
        (1, "……「今日も来ました」。それだけが、残って——送られました。", MFace),
        (1, "消えたほうの一行は、拾っておきます。……中身は、本人の前で。", MFace),   // S2-8 まで温存
        (1, "……画面の中の笑顔は、いまの一行を、読んだでしょうか。——観測できません。向こう側ですので。", MFace),   // レイの側は言わない
    };

    // S2-5 道中C／MidEnd（仮台本 07）。投稿の直後、配信画面が消えて、黒い画面に自分の顔が映る。
    //   ペンライトを持ったまま。「むだだ」をぜんぶ祓い、ぜんぶ数えた＝S2-8 の決定打（回数）の仕込み。
    //   末尾の {n} は直前の行に留まっていた実秒（補助観測・表示専用で保存しない）。
    private static readonly (int who, string text, string face)[] MidEnd =
    {
        (4, "「配信終わった。部屋の電気つけた。……自分なにしてんだろ。」", ""),   // 層3
        (1, "……画面が、消えました。黒い画面に、顔が映っています。ペンライトを、持ったまま。", MWorried),
        (1, "「むだだ」の声。ここまでで、ぜんぶ、祓いました。——ぜんぶ、数えました。", MFace),   // 決定打（回数）の仕込み
        (1, "……光が、少し、重い。……気のせい、ということにしておきます。", MDoubt),   // 【濁】兆候
        (1, "……ちなみに、いまの間。{n}秒。……いえ、集計しただけです。", MSmile),   // 補助観測
    };

    // S2-6 ボス出現（仮台本 07）。消えた画面の前の部屋で戦う。穢れは灰色の視線の線。
    private static readonly (int who, string text, string face)[] BossIntro =
    {
        (1, "消えた画面の前に、あの人が。……顔が映るほうを、向いたまま。", MFace),
        (1, "視線が、部屋じゅうに。……どれも、顔が、ありません。", MWorried),
        (1, "——ぜんぶ、数えます。ひとつ残らず。", MFace),
    };

    // S2-9 クリア（仮台本 07）。消えていた配信画面が灯り、ペンライトの光が画面に届く。
    //   空の問い・二度目（1度目「いらない」→2度目 無言→3度目「もう聞きません」の階段の二段目）。
    //   答えの下書きは出さず、無言のまま流す。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「送れなかったコメント、送った。読まれたかは、知らない。……送った。」", ""),   // 救済後
        (1, "……画面が、灯りました。ペンライトの光が——届いています。画面まで。", MSmile),
        (1, "入力欄に、一行、増えました。……読み上げは、しません。もう、送られたものですので。", MFace),
        (1, "……ご主人様。外の世界は、今日はどんな天気ですか。", MFace),   // 空の問い・二度目
        (1, "…………。", MFace),   // 二度目は無言で流す
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中（A+B+C 三波）＋ボスで浄化カプセルが満ちる。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        game?.SetStageTarget(MidWaveA + MidWaveB + MidWaveC + 1);

        // [一時/デバッグ] --input-field : S2-4 の入力欄（step 8）から始める。コメント欄UI の確認・スクショ専用。
        //   開幕バナー（STAGE 2 START）は本来ずっと前に出ているものなので、この入口では出さない。
        if (game != null && game.DebugInputField) { _step = 8; _startBannerShown = true; }

        // チェックポイント入口（DiffSelect が SelectedEntry をセット）。道中＆イントロを飛ばしてその戦闘から始める。
        // 中ボスから＝Step_BossCameo(5)／ボスから＝Step_BossSpawn(11)。
        else if (game != null && game.SelectedEntry != GameManager.StageEntry.Start)
        {
            _step = game.SelectedEntry switch
            {
                GameManager.StageEntry.Boss => 11,
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
        _lineHold += delta;
        if (!_clearing) { _stageElapsed += delta; Hud.SetElapsed((float)_stageElapsed); }
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 2 START"); }
        // 案C の場面の並び（仮台本 07 の S2-1〜S2-6）を、step 構成を変えずにそのまま流し込む。
        //   部屋（S2-1・S2-3 Mid）→ 中ボス（S2-2）→ 教室（S2-3 BossTalk）→ 入力欄（S2-4）→
        //   我に返る一拍（S2-5）→ 消えた画面の前の部屋でボス（S2-6）。
        //   場所の入れ替えは Step_MidwaveB（部屋→教室）と Step_MidwaveC（教室→部屋）が層セットごと担う。
        // ボス戦中割り込み（会話2択）は案C ではレイ面（S3-7）へ移るため、この面では止めている
        //   （KoharuInterruptEnabled=false。コードとステップ 15〜19 はレイ面での再利用のため残す）。
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;      // S2-1 部屋・導入
            case 2: Step_Lines(delta, Mid); break;        // S2-3 Mid（部屋）＋Chat1
            case 3: Step_MidwaveA(delta); break;          // 道中ザコ戦A（部屋）
            case 4: Step_Lines(delta, BossTalk); break;   // S2-2 中ボスの受け（配信画面の中の人）
            case 5: Step_BossCameo(delta); break;         // S2-2 中ボス こはる
            case 6: Step_MidwaveB(delta); break;          // 道中ザコ戦B（部屋→教室へクロスフェード）
            case 7: Step_Lines(delta, ClassTalk); break;  // S2-3 BossTalk（教室）＋Chat2／Chat3
            case 8: Step_InputField(delta); break;        // ★S2-4 入力欄（打って、消す手。選択は置かない）
            case 9: Step_MidwaveC(delta); break;          // 道中ザコ戦C（教室→部屋へ戻る。最大密度の山）
            case 10: Step_MidEndLines(delta); break;      // S2-5 我に返る一拍（{n} 差し込みあり）
            case 11: Step_BossSpawn(); break;
            case 12: Step_Lines(delta, BossIntro); break; // S2-6 ボス出現（ボスは出現済みだが会話中は止まる）
            case 13: Step_BossWait(delta); break;         // S2-7 ボス戦
            case 14: Step_Clear(delta); break;            // S2-9 クリア
            case 15: Step_Transition(); break;
            // ★ボス戦中割込み（会話選択）の受け皿だった step 15〜19 は、案C でこの仕掛けが
            //   レイ面（S3-7「つづけて／むりしないで」）へ移るため撤去した。台詞は旧正典（兄・台所）
            //   そのものなので配列ごと落としている。機構（Step_LinesHold／Step_MidChoice／
            //   SetQuietVeil／ChoiceOverlay の呼び出し作法）はレイ面での再利用のため残してある。
        }
        // ボス戦中の ambient は、全ボス共通の投稿弾（X投稿モチーフの言葉弾）に統一。
        // 旧「言葉弾＋ただの落下弾」混在から、Rei と同じく投稿弾のみ降らせる（難易度で数がスケール）。
        // こはる面は PostPool のこはるのテーマ（09 の K09〜K38 由来の 8 文字弾）を源にする＝
        // その面のテーマ語が降る一体感。層の比率は 09 のとおり 3:6:1（層2 が最も厚い面）。
        // ボス本体(BossKoharu)のスペル/予測線/パネル弾はそのまま。
        if (_bossActive) PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, theme: PostPool.Theme.Koharu, fallSpeed: 44f,
            accent: new Color(0.85f, 0.60f, 0.44f), murkAll: true); // こはる面テーマ＝配信画面の琥珀。全語が悲鳴＝濁色チップ
    }

    private void Advance()
    {
        _step++;
        _stepStarted = false;
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

    // S2-5（MidEnd）専用の送り。末尾の「……ちなみに、いまの間。{n}秒。」へ、直前の行に留まっていた
    //   実秒を差し込んでから流す（仮台本 07 の補助観測。表示専用＝保存しない。Hub の同型と同じ流儀）。
    //   static readonly の MidEnd は書き換えず、初回に写しを作ってそちらへ差し込む。
    private (int who, string text, string face)[]? _midEndLines;
    private double _midEndPrevT;   // 直前の行の表示からの経過（{n} に入る値）
    private int _midEndPrevIdx = -1;
    private void Step_MidEndLines(double delta)
    {
        if (_midEndLines == null) _midEndLines = ((int who, string text, string face)[])MidEnd.Clone();
        // 行が変わった瞬間に、直前の行に留まっていた秒数を次行の {n} へ差し込む。
        if (_stepStarted && _introLine != _midEndPrevIdx)
        {
            if (_midEndPrevIdx >= 0 && _introLine < _midEndLines.Length && _midEndLines[_introLine].text.Contains("{n}"))
            {
                int dwell = Mathf.Max(0, Mathf.RoundToInt((float)_midEndPrevT));
                _midEndLines[_introLine].text = _midEndLines[_introLine].text.Replace("{n}", dwell.ToString());
                ShowLine(_midEndLines);   // 差し込み後の本文で出し直す（Step_Lines は差し込み前の文で出している）
            }
            _midEndPrevIdx = _introLine;
            _midEndPrevT = 0;
        }
        else _midEndPrevT += delta;
        Step_Lines(delta, _midEndLines);
    }

    // ───── S2-4 入力欄（仮台本 07）専用の送り ─────
    //   InputField の並びはそのまま流すが、who=3（システム表示＝入力欄そのもの）の行だけは
    //   Hud のナレ用中央テロップに出さず、配信画面のコメント欄の姿をした CommentInput へ渡す。
    //   ミナの観測行（who=1）は従来どおり会話バーに出し、その間もコメント欄は画面に残る＝
    //   「打たれている一行を、ミナが横から見ている」という画になる。
    //
    //   台本の流れ:
    //     [0] ミナ「……文字が、打たれています。」  → 欄が現れる
    //     [1] who=3「レイちゃんが|」               → 文字送りで打たれる
    //     [2] ミナ「……消えていきます。一文字ずつ。」→ この行の裏で末尾から消える
    //     [3] who=3「今日も来ました|」             → 打ち直して送信（送信ボタンが一度灯る）
    //     [4]〜[6] ミナの観測行                     → 最後に欄が消える
    //
    //   送り作法は Step_Lines と同じ（Z 1段目＝全文表示／2段目＝次行。既読スキップ・オートも同じ）。
    //   who=3 の行は文字送り／消しが終わるまで次へ進めない＝打つ手・消す手を必ず見せる。
    //   バブルは HoldBubble で保持したまま（BubblePaused 継続＝弾も敵も止まったまま）。
    //   自動プレイ（--qa/--demo）は BubblePaused 中に Z をパルスし続けるので、動作完了後の
    //   最初のパルスで先へ進む＝ここで詰まらない。
    private CommentInput? _input;
    private void Step_InputField(double delta)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _introLine = 0;
            _lineHold = 0;
            Hud.HoldBubble = true;
            _input = CommentInput.Show(Hud);
            BeginInputLine();
        }
        var lines = InputField;
        bool isField = lines[_introLine].who == 3;
        // 入力欄の行は、打ち／消しが終わるまで送れない（手を最後まで見せる）。
        if (isField && !(_input?.Done ?? true)) { _lineHold = 0; return; }

        if (!isField && _zEdge && _lineHold >= 0.15 && !Hud.DialogRevealed)
        {
            Hud.RevealDialogNow();   // 1段目：まず全文表示（読み飛ばし防止）
            _lineHold = 0;
        }
        else if (_lineHold >= 0.15 && (isField || Hud.DialogRevealed)
                 && (_zEdge || Hud.FastForwarding || (Hud.AutoAdvance && _lineHold >= 1.4)))
        {
            _lineHold = 0;
            _introLine++;
            if (_introLine >= lines.Length)
            {
                _input?.QueueFree();
                _input = null;
                Hud.HoldBubble = false;
                Hud.HideBubble();
                Advance();
                return;
            }
            BeginInputLine();
        }
    }

    // 現在行を「入力欄へ」か「会話バーへ」振り分ける。
    //   who=3 … 入力欄に打つ。本文からカーソル記号「|」は落とす（末尾の明滅は CommentInput が描く）。
    //           最後の who=3（「今日も来ました」）だけ打ち終わりに送信ボタンが灯る＝送られた合図。
    //   who=1 … 会話バーへ。ただし「……消えていきます。一文字ずつ。」の行が出る裏で、欄の文字を
    //           末尾から消し始める＝ミナが言っているそばから一文字ずつ削れていく（06/07 の「消した一行」）。
    private void BeginInputLine()
    {
        var (who, text, _) = InputField[_introLine];
        if (who != 3)
        {
            if (text.Contains("消えていきます")) _input?.Erase();
            ShowLine(InputField);
            return;
        }
        // この行より後ろに who=3 が無ければ＝これが送られる一行。
        bool last = true;
        for (int i = _introLine + 1; i < InputField.Length; i++)
            if (InputField[i].who == 3) { last = false; break; }
        _input?.Type(text.TrimEnd('|'), send: last);
    }

    private void ShowLine((int who, string text, string face)[] lines)
    {
        var (who, text, face) = lines[_introLine];
        var kind = (Hud.LineKind)who;
        // 案C のこの面に出るのは ミナ(1)／こはる(2)／システム表示(3＝入力欄)／投稿(4)。
        //   3 は Narration 扱いで Hud 側が立ち絵を捨て中央テロップになる＝入力欄がそのまま画面に出る。
        string portrait = kind switch
        {
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? KFace : face,   // 蒼白(KPale)・光(KLit)を行ごとに
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? MFace : face,    // ミナも行ごと表情
            _ => MFace,
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "こはる");
    }

    // Step_Lines の「最終行のバブルを閉じない」変種（会話選択・層2プロト用）。
    //   完了時に HoldBubble/HideBubble を触らず Advance だけする＝バブルが最終行のまま残り、
    //   Hud.BubblePaused（弾・敵の停止）が次のステップまで途切れない。後続の Step_Lines / ShowLine が
    //   バブル内容を差し替えるので閉じ処理は不要。他ステージへ2択を横展開するときもこの組で使う。
    private void Step_LinesHold(double delta, (int who, string text, string face)[] lines)
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
                 && (_zEdge || Hud.FastForwarding || (Hud.AutoAdvance && _lineHold >= 1.4)))
        {
            _lineHold = 0;
            _introLine++;
            if (_introLine >= lines.Length)
            {
                Advance();           // バブルは保持したまま（HoldBubble true 継続）
                return;
            }
            ShowLine(lines);
        }
    }

    // ───── ボス戦中割込みの2択（会話選択・層2プロト。docs/20260831/会話選択_層2_プロト仕様.md §2）─────
    //   Pre 最終行「……頼む。それだけは、聞かないでくれ。」のバブルを保持したまま（＝BubblePaused 継続で
    //   弾・敵は停止のまま）ChoiceOverlay を重ねる。デフォルトカーソルは B（正典側）。Xキャンセル無し。
    //   選択A確定＝疑いフラグ PressedTheQuestion を分岐再生の開始前に記録（仕様§8）。
    // 自動プレイ互換（--qa/--demo）: QaPilot/DemoPilot は BubblePaused 中 Z をパルスし続ける
    //   （QaPilot.cs DriveShootAndAdvance / DemoPilot.cs 同名）ため、既定カーソルBのまま1パルスで即決される
    //   ＝ここで詰まらない（QaPilot のドリフトで ↑↓ が入りAに動いても、A/B とも MidStoryPost へ収束する）。
    //   R長押しリトライ（KoharuRoot・Key.R ポーリング）は独立に効き、ポーズはツリーポーズで本ステップごと止まる。
    private ChoiceOverlay? _midChoice;
    private bool _midChoseA;
    private void Step_MidChoice(double delta)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            // [一時/デバッグ] --choice3 のときだけ中央に1本足して3択で出す（ChoiceOverlay の N 択レイアウト確認用）。
            //   末尾は「ひきさがる」のまま＝沈黙の自動決定の対象と既定カーソルの位置づけを変えない。
            //   選択の解釈（_midChoseA = Selected == 0）も据え置きで、増えた中央は B と同じ扱いに落ちる。
            bool three = GetNodeOrNull<GameManager>("/root/Game")?.DebugChoiceThree == true;
            string[] opts = three
                ? new[] { "もういちど、聞く", "しずかに、まつ", "ひきさがる" }
                : new[] { "もういちど、聞く", "ひきさがる" };
            _midChoice = ChoiceOverlay.Show(Hud, opts, defaultSel: opts.Length - 1); // 0=A / 末尾=B（既定=B＝正典側）
        }
        if (_midChoice == null || !_midChoice.Decided) return;
        _midChoseA = _midChoice.Selected == 0;
        _midChoice.QueueFree();
        _midChoice = null;
        if (_midChoseA)
        {
            // 選択A「もういちど、聞く」＝もう一度踏み込んだ。下流2場面（Clear の1行／Epilogue の1行）の変種に使う。
            var game = GetNodeOrNull<GameManager>("/root/Game");
            if (game != null) game.PressedTheQuestion = true;
        }
        Advance(); // → 17: 分岐A/B
    }

    // 道中ザコ戦“前半”：Spawner起動→MidWaveA体浄化でチラ見せへ。
    private void Step_MidwaveA(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            StartMidwaveSpawner();
        }
        // 規定数浄化（or 目標到達）で節目＝スポーン停止＋倒し残しの居座りザコを片付けて進む。
        // 全滅ハント（60体の中で最後の1体探し）を要求しない＝進行不能を防ぐ。戦闘中の居座りは維持。
        if (game != null && (game.PurifiedCount - _waveBase >= MidWaveA || game.StageCleared))
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // 道中ザコ戦“B（後半）”：チラ見せの後。やや詰めて始める。MidWaveB体で終盤Cへ（型崩し後：溜めはボス戦中）。
    private void Step_MidwaveB(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            StartMidwaveSpawner(0.35f);
            // 場所が変わる：配信の部屋 → 立てなかった教室へ、層セットごと 1.0 秒でクロスフェード。
            // 唐突に切らない（StageBackground.CrossfadeBossTo と同じ作法）。旧経路の面では何も起きない。
            if (GetTree().GetFirstNodeInGroup("stagebg") is StageBackground bg)
                bg.CrossfadeLayersTo(KoharuRoot.ClassLayers, 1.0f);
        }
        // 規定数浄化（or 目標到達）で節目＝スポーン停止＋居座り片付け＋終盤Cへ（全滅ハント不要＝進行不能を防ぐ）。
        if (game != null && (game.PurifiedCount - _waveBase >= MidWaveB || game.StageCleared))
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // 道中ザコ戦“C（終盤）”：Bの直後（連戦）。最大密度でボス直前の山を作る。MidWaveC体で小話→本ボスへ。
    private void Step_MidwaveC(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            StartMidwaveSpawner(0.7f);
            // S2-5〜S2-6：教室から部屋へ戻る（ボスは「消えた画面の前の部屋」で戦う）。
            // 唐突に切らず、道中Bと同じ 1.0 秒のクロスフェードで層セットごと入れ替える。
            if (GetTree().GetFirstNodeInGroup("stagebg") is StageBackground bg)
                bg.CrossfadeLayersTo(KoharuRoot.RoomLayers, 1.0f);
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
        _spawner = new Spawner { Name = "Spawner", World = World, Theme = StageTheme.Koharu, StartIntensity = startIntensity };
        AddChild(_spawner);
        _spawner.Begin();
    }

    private void ClearStageEnemies()
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e) e.QueueFree();
    }

    // ボスの“チラ見せ”：CameoBoss（本戦ボスと同じ Enemy 派生・シールド制・BossMover）を1体スポーン。
    // 撃破（HP/サイクル削り切り＝改心）して捨て台詞を流し切る（Finished）まで Stage は進まない。保険退場は無し。
    private void Step_BossCameo(double delta)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _cameo = new CameoBoss
            {
                Name = "KoharuCameo",
                Theme = new CameoTheme
                {
                    DisplayName = "こはる", Handle = "@koharu",
                    // v3 の中ボスは穢れ形態を持たない1枚絵なので Pre/Cry/Post に同じパスを入れる
                    // （50px 表示のちびなので、姿が変わらない損失はほぼ無い）。
                    PreTex = "res://char/v3/koharu_mid.png",
                    CryTex = "res://char/v3/koharu_mid.png",
                    PostTex = "res://char/v3/koharu_mid.png",
                    Face = KFace,
                    SpellTint = new Color("e8a24a"), SpellShape = BulletShape.Orb,
                    Fire = CameoFireTheme.KoharuFalling,
                    Aura = FxLayer.BossAura.Koharu,
                    Bgm = Audio.Instance?.BgmBossKoharu,
                    IntroLines = CameoTalk1, TauntLines = CameoTalk3, DefeatLines = CameoPost,
                },
            };
            World.AddChild(_cameo);
            _cameo.GlobalPosition = new Vector2(SpawnX, 70f);
        }

        // 撃破→捨て台詞を流し切ったら次フェーズへ（道中後半）。
        if (!IsInstanceValid(_cameo) || _cameo.Finished)
        {
            Hud.HideBossBar();                                   // バー出っ放しにしない（後で本ボスが再表示）
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            if (IsInstanceValid(_cameo)) _cameo.QueueFree();
            // 中ボス撃破フック：撃破記録＋初回なら強化ショップ説明へ離脱（その後ハブ）。離脱したら以降の進行は止める。
            if (CheckpointFlow.OnMidBossCleared(this, "koharu", false)) return;
            Advance();
        }
    }

    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _boss = new BossKoharu { Name = "BossKoharu" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            _bossActive = true;
            // 本ボス突入：道中の横スクロール背景 → ボス専用背景へ切替（中ボス/カメオでは呼ばない）。
            GetTree().GetFirstNodeInGroup("stagebg")?.Call("EnterBoss");
            Advance();
        }
    }

    // ボス戦中割込み（型崩し S2）：MidStory を一度だけ、ボスHPが半分を割った付近で差し込む。
    //   ・トリガ窓は 20〜50%（ボム等で一気に削られ窓を飛ばしたら、割込み無しで素直に進む＝進行不能なし）。
    //   ・Hud.BubblePaused 中（ボス自身の改心かけあい等）は発火しない＝会話の二重表示を防ぐ。
    //   ・完了後は case 16 経由で BossWait(11) へ復帰。会話中はエンジン側で弾停止＋敵弾クリア。
    // 案C：戦闘中の割り込み（Koharu interrupt。ChoiceOverlay の2択）は S3-7 のレイ面へ移った。
    //   こはる面では発火させない。機構（Step_LinesHold／Step_MidChoice／SetQuietVeil）は
    //   レイ面で再利用するためコードごと残し、ここのフラグだけで止める。
    private static readonly bool KoharuInterruptEnabled = false;
    private bool _midStoryShown;
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
        if (_boss.IsPurified)
        {
            _postDefeatT += delta;
            if (_postDefeatT >= BossFinishGrace)
            {
                GD.PushWarning("[StageKoharu] ボス撃破後に Finished が立たないため保険で進行");
                _bossActive = false;
                Advance();
            }
            return; // 撃破後は割込みの判定に入らない
        }
        // 案C では戦闘中の割り込み（会話2択）はレイ面（S3-7）の仕掛け。こはる面では止めてある。
        if (!KoharuInterruptEnabled) return;
        if (!_midStoryShown && !Hud.BubblePaused)
        {
            float frac = (_boss.CurrentBarIndex + _boss.CurrentBarFrac) / Mathf.Max(1, _boss.TotalBars);
            // --choice デバッグ起動中は HP 窓を待たずに即発火（選択シーンの確認用。一度きりの発火は _midStoryShown が保証）
            bool debugNow = GetNodeOrNull<GameManager>("/root/Game")?.DebugChoiceNow == true;
            if ((frac <= 0.5f && frac >= 0.2f) || debugNow)
            {
                _midStoryShown = true;
                _stepStarted = false;
                SetQuietVeil(true);    // 静けさの溜め＝画面をわずかに鈍色へ沈める（弾停止はエンジン側）
            }
        }
    }

    // ───── S3: ボス戦中割込み（MidStory）の「静けさの溜め」 ─────
    // 突入で画面全体をわずかに鈍色へ沈め（彩度と対比が一段引いた“息を潜める”画）、戦闘再開でそっと明ける。
    // 弾・敵の停止はエンジン側（Hud.BubblePaused）＝この膜は画の温度だけを担当。
    // HUD・会話バブルは CanvasLayer 上なので沈まない（文字の読みやすさは侵さない）。
    private ColorRect? _quiet;
    private Tween? _quietTw;
    private void SetQuietVeil(bool on)
    {
        // 割り込み区間はボス字幕・スペルカットインも一緒に鎮める（Hud 側で 0.2s フェード→消去）。
        //   カットインのセリフ（y≈348）が選択肢と、字幕（y=540）が吹き出しと重なるため。区間明けは
        //   フラグを戻すだけ＝残っていた表示は復活させない（次の台詞・次のスペルからは通常どおり）。
        Hud.SuppressCallouts = on;
        if (_quiet == null || !IsInstanceValid(_quiet))
        {
            if (!on) return; // 明ける指示だけ来た（膜が無い）＝何もしない
            _quiet = new ColorRect
            {
                Name = "QuietVeil",
                Color = new Color(0.52f, 0.55f, 0.62f, 0f), // 中明度の鈍色＝薄く重ねると彩度・対比が少し引く
                Size = new Vector2(384f, 216f),
                ZIndex = 30,                // 弾(0..)・自機(10)・FxLayer(20..21)の上、HUD(CanvasLayer)の下
                ZAsRelative = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            World.AddChild(_quiet);
        }
        _quietTw?.Kill();
        _quietTw = CreateTween();
        // 入り 0.9s（ゆっくり沈む＝溜め）／明け 1.4s（会話の余韻を残してそっと戻す）。Sine/Out で線形にしない。
        _quietTw.TweenProperty(_quiet, "color:a", on ? 0.16f : 0f, on ? 0.9 : 1.4)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    private bool _clearBannerShown;
    // クリア会話の実体（S2-9）。案C ではこの面に下流変種が無い（戦闘中の割り込み＝選択がレイ面へ移ったため）
    //   ので、Clear をそのまま流す。写しで回す形だけ残す＝差し替えを足すときの入口を潰さない。
    private (int who, string text, string face)[]? _clearLines;
    private void Step_Clear(double delta)
    {
        if (!_clearBannerShown)
        {
            _clearBannerShown = true;
            _clearTime = (float)_stageElapsed;
            var game = GetNodeOrNull<GameManager>("/root/Game");
            var rec = game?.RecordClearTime("koharu", game.Difficulty, _clearTime) ?? (true, (float?)null);
            long score = game?.Score ?? 0;
            var recScore = game?.RecordScore("koharu", game.Difficulty, score) ?? (true, (long?)null);
            Hud.ShowClearBanner("STAGE 2 CLEAR", _clearTime, rec.isBest, rec.prev, score, recScore.isBest, recScore.prev);
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll(); // クリア時に自弾・残弾を一掃(#17)
            _clearLines = (((int who, string text, string face)[])Clear.Clone());
        }
        Step_Lines(delta, _clearLines!);
    }

    private bool _clearing;
    private void Step_Transition()
    {
        if (_clearing) return;
        _clearing = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetNodeOrNull<GameManager>("/root/Game")?.CompleteStage("koharu");
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    // 投稿弾（言葉弾）の周期/tick 用アキュムレータ。湧き処理は全ボス共通ヘルパ PostBullets.Tick に集約。
    // 面固有の語プールは PostPool.Theme.Koharu（wiki/08_仮台本/09 の「言葉弾の文言リスト」こはるの行）へ移した。
    //   案C の S2-3：推し活の声の合間に、家の声と他人の目が同じ色で混じる（仮台本 07）。
    private int _wordTick;
}
