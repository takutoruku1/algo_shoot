using Godot;
using System.Linq;

// StageRei : STAGE3「星逢レイ（壁一面が配信画面の、狭い部屋）」進行。
//   1〜4: 配信枠・導入 → 道中A（S3-1〜S3-3）
//   5〜6: 中ボス＝中の人（S3-4）→ 道中B
//   7〜9: 引用の嵐の接続（S3-5a/5b）→ 道中C → 呑みこまれる部屋（S3-5c）
//   10〜12: ボス＝ガワ出現・ボス戦（S3-6）。戦闘中の割り込み（S3-7「つづけて／むりしないで」）は
//           ボスHP 20〜50% で一度だけ step 15〜17 へ抜けて戻る。改心（S3-8）は BossRei が担う。
//   13〜14: クリア（S3-9）→ ハブへ（本編の最終面。全クリアで FINAL カード）
// 台詞の正典: wiki/08_仮台本/07_粗い台本_案C_2_こはるとレイ.md（ユーザー承認済み・2026-09-05）の S3-1〜S3-9。
public partial class StageRei : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
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
    // レイ面の緩和（難易度高すぎ）：道中ザコ総数を 60→45（約25%減）。過密な導入Aを多めに削り、
    // 終盤Cは最大密度の山として残す（A>B<C のクレッシェンドは維持）。旧値: A21/B18/C21。
    private const int MidWaveA = 15;  // 導入（チラ見せ前）。緩く立ち上がる。旧21（-6）
    private const int MidWaveB = 14;  // チラ見せ後。StartIntensity を上げてやや詰めて始める。旧18（-4）
    private const int MidWaveC = 16;  // ミッドシナリオ後の終盤。最大密度＝ボス直前の山（合計45体）。旧21（-5）

    // ボスの“チラ見せ”（カメオ）＝本戦ボスと同じ土台の短いミニボス戦（CameoBoss＝Enemy 派生・シールド制）。
    // 撃破（HP/サイクル削り切り＝改心）まで Stage は進まない。保険タイマー退場は廃止（撃たないと進めない）。
    private CameoBoss _cameo = null!;

    // 操作チュートリアルは独立ステージ0（StageZero）へ一本化した（A案）。レイ面からは撤去済み。

    private const float SpawnX = 300f;

    // ミナの表情（案C では語り手はミナ一人＝行ごとに顔を差し替える）。こはる面と同じ流儀。
    private const string MFace = "res://char/mina_face.png";
    private const string MSmile = "res://char/mina_smile.png";
    private const string MWorried = "res://char/mina_worried.png";
    private const string MDoubt = "res://char/mina_doubt.png";

    // レイの顔（中の人 / 配信用の笑顔 / ガワ＝笑顔で固定 / 中の人の泣き顔）。
    //   中ボス＝中の人（RFace/RSmile）、ボス＝ガワ（RGawa）と、姿が違うこと自体が仕込み。
    private const string RFace = "res://char/v3/rei_face.png";
    private const string RSmile = "res://char/v3/rei_face_smile.png";
    private const string RGawa = "res://char/v3/rei_gawa.png";

    // S3-1 配信枠・導入（仮台本 07）。HUD バッジ `炎上中`＝H2 の炎上で弱体化した状態から始まる。
    //   狭い部屋の壁一面が配信画面。右上の同接カウンター「11」。画面の中央にガワの星逢レイが
    //   等身大より大きく笑顔で立つ。コメント欄の同じ位置に「今日も来ました」＝こはるの結線。
    //   ミナは説明しない（名前も出さない）。投稿の下の声の中身は S3-8 まで温存。
    // who: 0=あなた（送信された下書き） / 1=ミナ / 2=レイ / 3=システム表示 / 4=投稿。who=5（中継）は使わない。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (1, "……ご主人様。少し、光が薄いですが。潜れます。——潜れる、はずです。", MDoubt),   // 弱体化の実感
        (4, "「今日も20時から! 初見さん大歓迎。コメント、全部読みます。」", ""),   // 層1。配信中の声
        (1, "……明るい投稿ですね。——この投稿の下から、も。聞こえます。……こちらは、明るくない声が。", MWorried),   // 中身は言わない
        (1, "狭い部屋です。壁一面が、配信の画面。右上の数字は「11」。……画面の中で、星逢レイが、等身大より大きく、笑っています。", MFace),
        (1, "コメント欄が、流れています。……ひとつだけ、同じ場所に、同じ一行。「今日も来ました」。", MFace),   // こはるの結線。名前は出さない
        (1, "行きます。——放っておけないので。", MFace),
    };

    // S3-2 小話 Mid（仮台本 07）。「気づいて」「見て」の声の合間に、家の声「ふーん」が同じ色で混じる。
    //   伏せたスマホが一度だけ光り、誰も拾わない。同接が「11」→「8」に減る。
    //   ミナは自分の数字（十万）を言わない＝「比べません」で流す。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (1, "ここの声は……「気づいて」「見て」と、画面の外へ向かって、言っています。", MFace),
        (1, "合間に、「ふーん」。……同じ色の声で。", MFace),   // 家の声
        (4, "「登録者2000人、ありがとう。……去年の今日も、2000人。」", ""),   // 層2
        (1, "……机の上の、伏せたスマホが。いま、一度だけ、光りました。——誰も、拾いません。", MWorried),
        (1, "右上の数字。「8」。……減りました。うち一つは、机の上の端末——つけっぱなしの、この部屋のものです。", MFace),
        (1, "わたくしの光と、あの数字と、どちらが薄いか。……比べません。どちらも、集計はしますが。", MSmile),
    };

    // S3-3 道中A／BossTalk（仮台本 07）。削除済みの一行を投稿の下に聞く（中身は S3-8 まで言わない）。
    //   「今日も来ました」は同じ場所のまま。ガワは明るくコメントを読み上げ、裏は見せない。
    //   末尾で中ボス（＝中の人）が画面の外から来る予感を置く＝ボス（ガワ）と別人格に見える仕込み。
    private static readonly (int who, string text, string face)[] BossTalk =
    {
        (4, "「企画メモ、下書き十四件。……出せるの、ゼロ件。」", ""),   // 層2
        (1, "……十四件。集計に、入れておきます。", MFace),   // 評価しない。H3r 小話（1）で拾う
        (1, "投稿の下に——一行。削除済みの、一行があります。……中身は、本人の前で。", MWorried),
        (1, "コメント欄の、あの一行。……まだ、同じ場所にあります。", MFace),
        (1, "……画面の笑顔が、いま、コメントをひとつ、読み上げました。……声は、明るいです。", MFace),
        (1, "——来ます。画面の外から、足音が。……画面の中の笑顔は、動きません。", MWorried),
    };

    // S3-4 中ボス レイ（仮台本 07）。先出しの本人＝中の人。ヘッドセットを首に掛けたパーカー、
    //   狭い机、リングライトの光が顔に当たっている。第一声のあと、すぐ配信用の笑顔へ切り替わる。
    //   捨て台詞も笑顔のまま。第一声→RECLOSE（順送り）→捨て台詞、の三段で CameoBoss に渡す。
    private static readonly (int who, string text, string face)[] CameoTalk1 =
    {
        (2, "……だれ? あなた。……わたしの配信、見に来た人?", RFace),   // 第一声。中の人。笑っていない
    };
    // RECLOSE（サイクルごとに順送り）。切り替わったあとは笑顔のまま崩れない。
    private static readonly (int who, string text, string face)[] CameoTalk3 =
    {
        (2, "——はじめまして! 星逢レイです。今日も来てくれて、ありがとう。", RSmile),   // すぐ配信用の笑顔
        (2, "逃げないで。……初見さん、まだ、いてくれるでしょう?", RSmile),
    };
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "逃げたら……承知しないんだから。", RSmile),   // 捨て台詞。笑顔のまま
    };

    // S3-4 のミナの観測（仮台本 07）。CameoBoss は who=2（本人）の行だけを一行オーバーレイで流すので、
    //   本人の合間に入るミナの行はオーバーレイに乗らない。中ボスの直前／直後に開く step が受け皿になる
    //   （step 構成は変えない前提での置き場所。こはる面と同じ流儀）。
    private static readonly (int who, string text, string face)[] CameoAfter =
    {
        (1, "……切り替わるまで、一秒九。……画面の中の笑顔より、小さいですね。", MFace),   // 観測のみ。同一人物とは言わない
        (1, "……あの人、画面の中へ。——画面の笑顔は、いまも、動いていません。", MWorried),
    };

    // ───────── S3-5a 道中B ＋ S3-5b 引用の嵐の接続（ミッドシナリオ枠。仮台本 07）─────────
    // 投稿「読まなきゃよかった」の直後、ガワの上に顔のない引用が貼られはじめる。
    // 嵐の本体（十七枚・3段階・約50秒）は別タスク（P3）なので、ここには 07 の「接続3行」だけを置く。
    // 剥がし切った下に、消した一行がある（中身は S3-8 まで言わない＝改心の一段目で返す）。
    private static readonly (int who, string text, string face)[] MidStory = CameoAfter.Concat(new (int, string, string)[]
    {
        // S3-5a（2行）
        (4, "「今日のコメント、全部読んだ。……読まなきゃよかった。」", ""),   // 層3
        (1, "……投稿の上に、引用が。一枚。……二枚。——顔のない、引用です。", MWorried),   // 嵐へ
        // S3-5b 接続3行（本文＝十七枚の嵐は 11 の移設待ち）
        (4, "[星逢レイ @rei_____] 配信おわり 来てくれてありがとう 人数じゃないから 全部読めた それだけで十分", ""),   // ピン留め
        (1, "……この投稿の上に、貼られていきます。——剥がします。ご主人様、撃つのは、貼りついたほうを。", MFace),
        (3, "引用: 0", ""),   // 引用カウンタ（嵐本体が入ると 0→17 を刻む）
        // TODO: 引用の嵐（11）。段階1〜3・十七枚・本人の返信の縮み・剥がし切りを新 step として実装する（DEV_QUEUE P3）。
        (1, "……剥がし切りました。下に、薄い字で、一行。……中身は、本人の前で。", MFace),   // 下書き「わたしに、気づいてよ」。S3-8 で返す
    }).ToArray();

    // S3-5c 道中C／MidEnd（仮台本 07）。同接「3」。壁の画面が部屋を呑みこみはじめる。【濁】広がる。
    //   残った三つの席のひとつが「今日も来ました」＝こはる。ミナは説明しない。
    private static readonly (int who, string text, string face)[] MidEnd =
    {
        (1, "右上の数字。「3」。……残った三つの席の、ひとつは、あの一行です。", MFace),
        (1, "……光が、重い。……気のせいでは、なさそうです。", MDoubt),   // 【濁】広がる
        (1, "画面が、部屋を、呑みこんでいきます。……奥に、笑顔だけが。", MFace),
    };

    // S3-6 ボス出現（仮台本 07）。ボス登場の説明台詞は置かない（07 に S3-6 の導入行が無く、
    //   ボス戦の口上はボス本体 BossRei の改心／RECLOSE／挑発が担う）。
    private static readonly (int who, string text, string face)[] BossIntro =
        System.Array.Empty<(int, string, string)>();

    // S3-9 クリア（仮台本 07）。同接が「4」になる（誰が増えたかは言わない）。
    //   空の問い・三度目＝「もう聞きません」で階段を閉じる（1度目「いらない」→2度目 無言→3度目）。
    //   【濁】危険域手前。「三人分」はここで初めて言う（S3-7 では「二人ぶん」に留めてある）。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「次は、本気のあなたと。——逃げたら、承知しないから。」", ""),   // 救済後
        (1, "右上の数字が、「4」に。……ひとつ、増えました。", MSmile),
        (1, "コメント欄の、あの一行。……まだ、同じ場所にあります。", MFace),   // 「今日も来ました」。説明しない
        (1, "……そういえば。今日の空は、晴れていましたか。", MFace),   // 空の問い・三度目
        (1, "……いえ。もう、聞きません。三度、聞きました。", MDoubt),
        (1, "三人分の祈りを、抱えてしまったので。……この重さくらい、わたくしが、持ちます。", MWorried),   // 【濁】危険域手前
    };

    // ───────── S3-7 戦闘中の割り込み（仮台本 07。ユーザー承認済み・2026-09-05）─────────
    // ボス HP 20〜50% で一度だけ。弾が止まり、画面が鈍色に沈む（SetQuietVeil）。
    // 問うのはミナ自身の状態＝あなたの過去は問わない。「三人分」は S3-9 に取ってあるので、
    // ここは「二人ぶんと、貼られた引用と、いまの声」に留める。
    // 機構はこはる面（`StageKoharu` の Step_LinesHold／Step_MidChoice／SetQuietVeil）から移植。
    //   案C ではこの仕掛けの本籍がレイ面なので、こはる面は KoharuInterruptEnabled=false で止めてある。
    private static readonly (int who, string text, string face)[] MidChoicePre =
    {
        (1, "……ご主人様。弾がやんでも——聞こえます。画面の向こうで、まだ、コメントを読み上げている声が。", MWorried),
        (1, "笑顔のまま、こちらへ撃っているんですね。……笑顔は、一度も、崩れていません。", MWorried),
        (1, "——ひとつ、ご報告を。わたくしの光、二割ほど、濁っています。", MDoubt),
        (1, "二人ぶんと、貼られた引用と、いまの声を、浴びすぎました。……つづけて、いいですか。", MFace),
    };
    // 下書き選択（温度で割った3択。既定カーソルは末尾＝（送らない）。沈黙20秒の自動決定もここへ落ちる）。
    private static readonly string[] S37Choices = { "つづけて", "むりしないで", "（送らない）" };
    // 受け（仮台本 07）。送った2件は復唱（who=0）してから受ける＝S1-4 と同じ流儀。
    private static (int who, string text, string face)[] S37Reply(int sel) => sel switch
    {
        0 => new (int, string, string)[]
        {
            (0, "つづけて", ""),
            (1, "……はい。“つづけて”と、いただきました。——では、つづけます。", MFace),   // F1 導入でこの語を一度だけ引用する
        },
        1 => new (int, string, string)[]
        {
            (0, "むりしないで", ""),
            (1, "……無理は、しません。無理でないところまでを、ぜんぶ、やります。", MFace),
        },
        _ => new (int, string, string)[]
        {
            (1, "……無言。——続行、と読みます。", MFace),   // （送らない）＝沈黙20秒でもここ
        },
    };
    private const float S37SkipContam = 0.02f;   // （送らない）で【濁】微増（S1-4 と同値）

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中ザコ（A+B+C 三波）＋ボスで浄化カプセルが満ちるよう目標を設定（45体＋ボス1）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(MidWaveA + MidWaveB + MidWaveC + 1);

        // 操作チュートリアルは独立ステージ0（StageZero）へ一本化した（A案）。レイ面は初回でも本編からテンポよく始まる。
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
        _lineHold += delta;
        // ステージ経過タイム：クリア確定までは積算し続け、HUDへ常時反映（クリア後は確定値で固定）。
        if (!_clearing) { _stageElapsed += delta; Hud.SetElapsed((float)_stageElapsed); }
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 3 START"); }

        // 案C の場面の並び（仮台本 07 の S3-1〜S3-9）を、step 構成を変えずにそのまま流し込む。
        //   配信枠（S3-1・S3-2）→ 道中A（S3-3）→ 中ボス＝中の人（S3-4）→ 引用の嵐の接続（S3-5a/5b）→
        //   呑みこまれる部屋（S3-5c）→ ボス＝ガワ（S3-6）。改心（S3-8）は BossRei 側。
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;      // S3-1 配信枠・導入
            case 2: Step_Lines(delta, Mid); break;        // S3-2 小話 Mid（気づいて／見て／ふーん）
            case 3: Step_MidwaveA(delta); break;          // 道中ザコ戦A（導入）
            case 4: Step_Lines(delta, BossTalk); break;   // S3-3 道中A／BossTalk（削除済みの一行・足音）
            case 5: Step_BossCameo(delta); break;         // S3-4 中ボス＝中の人（笑顔へ切り替わる）
            case 6: Step_MidwaveB(delta); break;          // 道中ザコ戦B（やや詰める）
            case 7: Step_Lines(delta, MidStory); break;   // ★S3-4 受け＋S3-5a／S3-5b 接続（嵐本体は P3）
            case 8: Step_MidwaveC(delta); break;          // 道中ザコ戦C（終盤＝最大密度の山）
            case 9: Step_Lines(delta, MidEnd); break;     // S3-5c 呑みこまれる部屋（【濁】広がる）
            case 10: Step_BossSpawn(); break;
            case 11: Step_Lines(delta, BossIntro); break; // S3-6 ボス出現（07 に導入行は無い＝空）
            case 12: Step_BossWait(delta); break;         // S3-6 ボス戦（S3-7 の割り込みをここから抜く）
            case 13: Step_Clear(delta); break;            // S3-9 クリア
            case 14: Step_Transition(); break;
            // S3-7 戦闘中の割り込み（ボスHP 20〜50% で一度）。Step_BossWait が 15 へ飛ばし、
            //   17 の受けを流し切ると 12（ボス戦）へ戻る。バブルは 15→16→17 の間ずっと保持される。
            case 15: Step_LinesHold(delta, MidChoicePre); break;   // 問いかけまで（バブルを閉じない）
            case 16: Step_MidChoice(delta); break;                 // 下書き選択（つづけて／むりしないで／（送らない））
            case 17: Step_MidChoiceAfter(delta); break;            // 受け → 膜を明けて戦闘へ戻す
        }
        // ボス戦中の“雨弾”は、X投稿モチーフの言葉弾（投稿弾）だけ降らせ、ただの常時落下弾は止める（ユーザー要望）。
        // 投稿弾の湧きは全ボス共通ヘルパ PostBullets.Tick に集約（難易度で数がスケール）。
        // 案C：レイ面の言葉弾は 07 の道中A／道中B の言葉弾（「初見です」「界隈」「ふーん」「低評価」「切り抜き」）
        // を源にする＝この面のテーマ語だけが降る（こはる面と同じ流儀）。
        // ボス本体(BossRei)のスペル/予測線/パネル弾はそのまま。道中はSpawner任せでRain非依存。
        // 安置リレー「最終選考」中（宣告〜最終着弾）は降らせない＝安置円の中に言葉弾が刺さって
        // 「安置なのに被弾」になる理不尽を断つ（あかり面の CorridorRun 中ゲートと同じ流儀）。
        if (_bossActive && !(IsInstanceValid(_boss) && _boss.AoeGateActive))
            PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, words: PostWords, fallSpeed: 46f,
                accent: new Color(0.62f, 0.70f, 0.92f)); // レイ面テーマ＝ランキングの銀青（穢れ桃より画面に馴染む）
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

    private void ShowLine((int who, string text, string face)[] lines)
    {
        var (who, text, face) = lines[_introLine];
        var kind = (Hud.LineKind)who;
        // 案C のこの面に出るのは あなた(0)／ミナ(1)／レイ(2)／システム表示(3)／投稿(4)。
        //   0 と 4 は Hud 側が立ち絵を捨てる（0＝下書きの吹き出し印）。3 は Narration 扱いで中央テロップ。
        string portrait = kind switch
        {
            Hud.LineKind.Boy => "",                                            // 「あなた」に顔は無い
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? RFace : face,   // 中の人(RFace/RSmile)・ガワ(RGawa)を行ごとに
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? MFace : face,    // ミナも行ごと表情
            _ => MFace,
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
                    // v3 の中ボスは穢れ形態を持たない1枚絵なので Pre/Cry/Post に同じパスを入れる
                    // （50px 表示のちびなので、姿が変わらない損失はほぼ無い）。レイだけ中ボスは中の人＝ボスのガワと姿が違うのが仕込み。
                    PreTex = "res://char/v3/rei_mid.png",
                    CryTex = "res://char/v3/rei_mid.png",
                    PostTex = "res://char/v3/rei_mid.png",
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
            // ここで即QueueFreeしない＝ガワが割れて出てきた中の人の改心退場アニメ
            // （Enemy._Process の _purified 分岐。PurifiedExitHoldOverride）を最後まで再生させ、自然にQueueFreeさせる。
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
    // S3-7 戦闘中の割り込み：ボスHPが 20〜50% の窓に入った一度だけ step 15 へ抜ける。
    //   ・ボム等で一気に削られ窓を飛ばしたら、割り込み無しで素直に進む＝進行不能なし。
    //   ・Hud.BubblePaused 中（ボス自身の改心かけあい等）は発火しない＝会話の二重表示を防ぐ。
    //   ・撃破後（IsPurified）は判定に入らない＝改心の会話に割り込まない。
    private bool _midStoryShown;
    private void Step_BossWait(double delta)
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
        {
            _bossActive = false;
            _step = 13; _stepStarted = false;   // 割り込みから戻った場合も確実にクリアへ（Advance だと 13 とは限らない）
            return;
        }
        if (_boss.IsPurified)
        {
            _postDefeatT += delta;
            if (_postDefeatT >= BossFinishGrace)
            {
                GD.PushWarning("[StageRei] ボス撃破後に Finished が立たないため保険で進行");
                _bossActive = false;
                _step = 13; _stepStarted = false;
            }
            return; // 撃破後は割り込みの判定に入らない
        }
        if (!_midStoryShown && !Hud.BubblePaused)
        {
            float frac = (_boss.CurrentBarIndex + _boss.CurrentBarFrac) / Mathf.Max(1, _boss.TotalBars);
            // --choice デバッグ起動中は HP 窓を待たずに即発火（選択シーンの確認用。一度きりは _midStoryShown が保証）
            bool debugNow = GetNodeOrNull<GameManager>("/root/Game")?.DebugChoiceNow == true;
            if ((frac <= 0.5f && frac >= 0.2f) || debugNow)
            {
                _midStoryShown = true;
                _step = 15; _stepStarted = false;
                SetQuietVeil(true);    // 静けさの溜め＝画面をわずかに鈍色へ沈める（弾停止はエンジン側）
            }
        }
    }

    // Step_Lines の「最終行のバブルを閉じない」変種（会話選択用。こはる面から移植）。
    //   完了時に HoldBubble/HideBubble を触らず Advance だけする＝バブルが最終行のまま残り、
    //   Hud.BubblePaused（弾・敵の停止）が次のステップまで途切れない。後続の Step_Lines / ShowLine が
    //   バブル内容を差し替えるので閉じ処理は不要。
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

    // ───── S3-7 の下書き選択（3択。こはる面の Step_MidChoice を案C の温度3択へ組み直したもの）─────
    //   Pre 最終行「……つづけて、いいですか。」のバブルを保持したまま（＝BubblePaused 継続で弾・敵は
    //   停止のまま）ChoiceOverlay を重ねる。既定カーソルは末尾＝（送らない）で、沈黙20秒の自動決定も
    //   そこへ落ちる（台本どおり）。
    // 自動プレイ互換（--qa/--demo）: QaPilot/DemoPilot は BubblePaused 中 Z をパルスし続けるため、
    //   既定カーソルのまま1パルスで即決される＝ここで詰まらない（3択どれでも同じ step 17 へ収束する）。
    private ChoiceOverlay? _midChoice;
    private double _s37ChoiceT;                       // 提示からの経過＝迷い秒数（RecordChoice へ渡す）
    private (int who, string text, string face)[] _s37After = System.Array.Empty<(int, string, string)>();
    private void Step_MidChoice(double delta)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _s37ChoiceT = 0;
            _midChoice = ChoiceOverlay.Show(Hud, S37Choices, defaultSel: S37Choices.Length - 1);
        }
        _s37ChoiceT += delta;
        if (_midChoice == null || !_midChoice.Decided) return;
        ApplyS37Choice(_midChoice.Selected);
        _midChoice.QueueFree();
        _midChoice = null;
        Advance(); // → 17: 受け
    }

    private void ApplyS37Choice(int sel)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        // 【散】は表示候補（上2件）のうち選ばれなかったぶんだけを計上する（S1-4 と同じ流儀）。
        //   （送らない）自体は言葉ではないので送信語にも散る語にも数えない＝表示候補2件が丸ごと散る。
        bool sent = sel < S37Choices.Length - 1;
        var others = new System.Collections.Generic.List<string>();
        for (int i = 0; i < S37Choices.Length - 1; i++) if (i != sel) others.Add(S37Choices[i]);
        game?.RecordChoice("s3_7", sent ? S37Choices[sel] : "", others, (float)_s37ChoiceT);
        // （送らない）＝返事をせずに見送った ぶんだけ、ミナの光がわずかに濁る。
        if (!sent) game?.SetContamination((game.Contamination) + S37SkipContam);
        _s37After = S37Reply(sel);
    }

    // 受けを流し切ったら鈍色の膜を明けて戦闘へ戻す（→ case 12 の Step_BossWait）。
    //   Step_Lines は流し切ると Advance（_step++）してしまうので、抜けた瞬間を捕まえて
    //   ボス戦の step へ戻す（割り込みは一度きり＝_midStoryShown が再突入を止める）。
    private void Step_MidChoiceAfter(double delta)
    {
        if (!_stepStarted) SetQuietVeil(false);   // 会話の余韻を残してそっと戻す（1.4s）
        Step_Lines(delta, _s37After);
        if (_step > 17) { _step = 12; _stepStarted = true; }   // ボス戦は継続中＝入り直さない
    }

    // ───── S3-7「静けさの溜め」（こはる面から移植）─────
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
    private void Step_Clear(double delta)
    {
        if (!_clearBannerShown)
        {
            _clearBannerShown = true;
            // クリア確定＝この瞬間に経過秒を確定し、ベスト記録（自己ベスト更新ならバナーに NEW BEST!）。
            _clearTime = (float)_stageElapsed;
            var game = GetNodeOrNull<GameManager>("/root/Game");
            var rec = game?.RecordClearTime("rei", game.Difficulty, _clearTime) ?? (true, (float?)null);
            long score = game?.Score ?? 0;
            var recScore = game?.RecordScore("rei", game.Difficulty, score) ?? (true, (long?)null);
            Hud.ShowClearBanner("STAGE 3 CLEAR", _clearTime, rec.isBest, rec.prev, score, recScore.isBest, recScore.prev);
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
    // レイ面固有の“声”プール（投稿弾の源）。ハンドルは無し（""）＝この面のテーマ語だけを降らせる。
    //   仮台本 07 の S3-3 道中A／S3-5a 道中B の言葉弾。コメント欄と引用の嵐の語彙で統一する。
    private static readonly (string h, string w)[] PostWords =
        { ("", "初見です"), ("", "界隈"), ("", "ふーん"), ("", "低評価"), ("", "切り抜き") };

}
