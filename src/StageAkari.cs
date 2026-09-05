using Godot;
using System.Linq;

// StageAkari : STAGE1「あかり（雨の降りやまない退勤後のフロア）」進行。
//   1: 導入会話（ミナの独白。案C では少年は存在しない＝who=0 は「あなた」の送った下書きのみ）
//   2: あかりボス出現
//   3: ボス戦（自責の弾雨＋あかりの自責弾。浄化＝改心で会話完了まで）
//   4: クリア（灯がともる）
// ボス戦中は天井の自責の雨が降り続ける（会話中は止む）。
// 台詞の正典: wiki/08_仮台本/06_粗い台本_案C_1_冒頭とあかり.md（ユーザー承認済み・2026-09-05）の S1-1〜S1-11。
public partial class StageAkari : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
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

    // ミナの表情（案C では語り手はミナ一人＝行ごとに顔を差し替える）。
    private const string MFace = "res://char/mina_face.png";
    private const string MSmile = "res://char/mina_smile.png";
    private const string MWorried = "res://char/mina_worried.png";
    // あかりの顔。平常＝akari_face／画面の光を浴びた＝akari_face_lit／泣き＝akari_face_cry。
    private const string AFace = "res://char/v3/akari_face.png";
    private const string AFaceLit = "res://char/v3/akari_face_lit.png";

    // 道中ザコ戦（Spawner）。三部構成で「後半ほど圧が上がる」緩急を作る。あかりは型崩し（S2）で“カメオ先出し”：
    //   肩慣らし0（圧ゼロ）→ チラ見せ（6体目の浄化で割り込み）→ 小話 → 前半A（緩い導入）→ 考察 → 後半B（やや詰める）→ ミッドシナリオ（溜め）→ 終盤C（最大密度）→ 本ボス。
    // 体数より“密度と変化”で長さを作る（§3 緩急）：3波で圧と構成を変えて間延びさせない。
    private Spawner _spawner = null!;
    private int _waveBase;
    // M2バランス：道中ザコ総数を レイ面と同じ 60→45 に緩和（A>B<C のクレッシェンドは維持）。旧値: A21/B18/C21。
    // M3：Intro直後にいきなり中ボスの唐突さを解消するため、カメオ前に“肩慣らし”0波を挿入。総数45は維持（6+12+13+14）。
    private const int MidWave0 = 6;   // 肩慣らし（Intro直後・StartIntensity 0）。6体目の浄化でカメオが割り込む。
    private const int MidWaveA = 12;  // 導入（チラ見せ＝先出しの後）。緩く立ち上がる。旧15（-3）
    private const int MidWaveB = 13;  // 考察の後。やや詰めて始める。旧14（-1）
    private const int MidWaveC = 14;  // ミッドシナリオ後の終盤。最大密度＝ボス直前の山（合計45体）。旧16（-2）
    // ボスの“チラ見せ”（カメオ）＝本戦ボスと同じ土台の短いミニボス戦（CameoBoss＝Enemy 派生・シールド制）。
    // あかり＝怯え・自責で、攻撃も悲嘆寄り。撃破（HP/サイクル削り切り＝改心）まで Stage は進まない。保険退場は廃止。
    private CameoBoss _cameo = null!;

    // S1-1 フロア・導入（仮台本 06）。雨の、誰もいない退勤後のオフィス。
    //   一面目なので「飛んでくるのは言葉であって本人ではない／奥の本人へ届けに行く」という
    //   この世界の決まりの提示をここが担う（06 の但し書き）。
    // who: 0=あなた（送信された下書き） / 1=ミナ / 2=あかり / 3=システム表示 / 4=投稿。who=5（中継）は使わない。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (4, "「すき、すき、すき。……ひとつでいいから、本物になって。」", ""),   // A35。H0 と同文
        (1, "……いいねが、ひとつ。——この投稿の下から、まだ、聞こえます。", MFace),   // 中身は言わない
        (1, "着きました。……雨が、降りやみません。誰もいない、退勤後のフロア。机も、椅子も——天井へ、落ちていく。", MFace),
        (1, "雨の中を、通知の吹き出しが、いくつも漂っています。数字は、どれも「1」。", MFace),
        (1, "飛んでくるのは、この人を苦しめている“言葉”。本人では、ありません。——祓って、いちばん奥へ、届けます。", MFace),   // 世界の決まり
        (1, "では、ご主人様。——まいります。", MSmile),
    };

    // S1-9 ボス戦「あふれるわたし」の頭（仮台本 06）。ボスは出現済みだが会話中は止まる。
    //   宣言はカットイン『ねえ、こっち見て』と弾幕名の宣告（BossAkari.Spells / AnnounceSpell）に対応する。
    //   RECLOSE と最終形の宣言（「……返して。読んだなら、返してよ。」）は BossAkari 側に置いた。
    private static readonly (int who, string text, string face)[] BossIntro =
    {
        (2, "ねえ、こっち見て。", AFace),   // カットイン『ねえ、こっち見て』
        (2, "すきって言って。あたしも言う。……ずっと一緒。離さないから。", AFace),   // 『すきって言って』『ずっと一緒』『離さない』の宣言
    };

    // S1-2 小話 Mid（仮台本 06）。「返して」「すき」の声。ホワイトボードの字と置き傘を、ミナが自分で見つける。
    //   相方は「あなた」＝返事をしない相手なので、掛け合いではなく観測とひとり漫才で運ぶ。
    //   先頭2行は S1-5（中ボス）のミナ行。CameoBoss の一行オーバーレイは本人(who=2)しか流さないので、
    //   中ボスが消えた直後に開くこの step が受け皿になる（step 構成は変えない前提での置き場所）。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (1, "……スマホの光を、顔に浴びたまま。……画面のほうは、こちらから見えません。", MFace),   // S1-5。観測のみ
        (1, "……あの人、降りやまない雨の奥へ。逃げるみたいに、消えてしまいました。", MWorried),   // S1-5
        (1, "ここの声は……どれも、「返して」「すき」と、すがりついてきます。……返事の声だけが、ひとつも、混じっていません。", MFace),
        (1, "この“声”、ひとつ祓うたびに、少し肩が軽くなります。……比喩です。肩は、ありません。", MSmile),   // 一人漫才
        (1, "ホワイトボードに、字が。「あたしのせいだ」。……消しても消しても、浮いてくる、そういう字です。", MWorried),
        (1, "傘立てに、置き傘が二本。……色が、違います。それだけ、言っておきます。", MFace),   // 持ち主は言わない
        (1, "雨の音は、嫌いではありません。……うるさい、と思いながら、消していないので。", MSmile),
    };

    // S1-7 道中B／MidStory／道中C（仮台本 06）。向かいの席の暗いモニタと読めない付箋。「同じ部署」の声。
    //   返事の声だけが無い。「ぜんぶ浴びる」をミナ自身の方針として言う＝S1-10 の「証人」の仕込み。
    private static readonly (int who, string text, string face)[] BossTalk =
    {
        (1, "……向かいの席。モニタは、暗いまま。キーボードの上に、付箋が一枚。……字は、読めません。", MFace),   // S1-10 の一通に繋ぐ
        (4, "「向かいの席、空いたまま。……三日目。」", ""),   // A38。層2
        (1, "「気づいてほしい、でも気づかれたら困る」……そういう声が、「同じ部署」という言葉と、いっしょに流れていきます。", MFace),   // A28
        (1, "……返事の声だけが、ここまで来ても、ひとつも、ありません。", MWorried),
        (1, "取り消されたぶんの言葉を、ぜんぶ、浴びていきます。ひとつ残らず。——そう、決めました。", MFace),   // 改心の「証人」の仕込み
    };

    // S1-5 中ボス あかり（仮台本 06）。先出しの本人。退勤後のカーディガンに社員証、片手のスマホの光が顔に当たっている。
    //   CameoBoss は who=2（本人）の行だけを一行オーバーレイで流す（IntroLines＝第一声／TauntLines＝RECLOSE／
    //   DefeatLines＝捨て台詞）。ミナの2行はオーバーレイに乗らないので S1-4 の締めと Mid の頭に置いてある。
    //   「見て」は第一声の「見たよね」までにとどめ、以後は繰り返さない（12）。
    //   顔は「画面の光を浴びた」akari_face_lit（片手のスマホの光が顔に当たっている状態）。
    private static readonly (int who, string text, string face)[] CameoTalk1 =
    {
        (2, "あ、来た来た。ねえ、あなた、あたしの、読んだ? 返事、まだ? 見たよね? ね?", AFaceLit),   // 第一声
    };
    // RECLOSE（サイクルごとに順送り）。
    private static readonly (int who, string text, string face)[] CameoTalk3 =
    {
        (2, "ひとりにしないで。……ねえ、ひとりに、しないでってば。", AFaceLit),
        (2, "来ないで……っ。……ちがう、来て。……来ないで。", AFaceLit),
    };
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "ぜったい、また会いに来てよね。ぜったいだよ?", AFaceLit),   // 捨て台詞
    };

    // ───────── S1-4 束（ミッドシナリオ枠＝後半Bと終盤Cの境・ボス前の“溜め”）─────────
    // 仮台本 06 の S1-4。宙に浮いた机に、社内チャットの「メッセージの送信を取り消しました」だけが縦に積み上がった束。
    // 本文はひとつも残っていない。拾うかどうかを「あなた」に聞く＝この面唯一の下書き選択（ChoiceOverlay）。
    // 吹き出し会話（Step_Lines）で出す＝弾は止まる。
    private static readonly (int who, string text, string face)[] MidStory =
    {
        (4, "「送信取消。今日で十二回目。……全部、同じ人宛。」", ""),   // A42。層3
        (1, "ご主人様、これ。宙に浮いた机の上に、束が。……「メッセージの送信を取り消しました」。その一行だけが、縦に、積み上がっています。", MWorried),
        (1, "本文は、ひとつも残っていません。取り消しの行だけ。……数えました。十二。——いまの投稿と、同じ数です。", MWorried),   // 数えただけ
        (1, "ご主人様。——拾って、いいですか。", MFace),
    };

    // S1-4 の下書き選択。（送らない）は言葉ではないので【散】に数えない＝表示候補の2件だけが散る。
    private static readonly string[] S14Choices = { "ひろって", "そっとしといて", "（送らない）" };
    // 選択ごとの受け。どのみち「そっと拾う」＝最後の締め（S14Tail）へ合流する。
    //   先頭の who=0 は台本 06 の「送った下書きの復唱」行（Hud は LineKind.Boy を「あなた」名義・
    //   立ち絵なしの下書き印で描く）。（送らない）は言葉を送っていないので復唱を置かない。
    private static (int who, string text, string face)[] S14Reply(int sel) => sel switch
    {
        0 => new (int, string, string)[]
        {
            (0, "ひろって", ""),
            (1, "……はい。そっと、拾います。……渡すのは、わたくしではありませんので。", MFace),
        },
        1 => new (int, string, string)[]
        {
            (0, "そっとしといて", ""),
            (1, "……はい。そっと。——拾うのと、そっとしておくのは、両立します。", MFace),
        },
        // （送らない）／沈黙20秒。【濁】微増（仕様未決につき小さく）。
        _ => new (int, string, string)[]
        {
            (1, "……無言。——では、そっと。……二件、散りましたね。", MFace),
        },
    };
    // 選択の受けの後に必ず流す締め（中ボスが来る予感）。
    private static readonly (int who, string text, string face)[] S14Tail =
    {
        (1, "——来ます。雨の奥から、足音が。……スマホの光が、先に見えます。", MWorried),
    };

    // S1-8 小話 MidEnd（仮台本 06）。投稿の直後、通知の吹き出しが「1」のまま四つ同じ形で降ってくる。
    //   フロアが「すき」で埋まっていく。ボス戦直前の引き。
    private static readonly (int who, string text, string face)[] MidEnd =
    {
        (4, "「いいねが、ひとつ。……増えてないの、知ってるのに、今日だけで四回も、見にきちゃった。」", ""),   // A40
        (1, "……通知の吹き出しが、「1」のまま。同じ形で、四つ、降ってきました。", MWorried),   // 数えただけ。投稿の「四回」とは結び付けて言わない
        (1, "ホワイトボードも、モニタも、窓も……ぜんぶ「すき」で、埋まっていきます。取り消したぶんが、フロアじゅうに、あふれている。", MFace),
        (1, "この吹き出し、ぜんぶ「またね」と書いてあります。……祓います。ご主人様は、見なくていいです。", MFace),
        (1, "奥に、あの人が。……行きます。今度こそ、奥まで。", MFace),
    };

    // S1-11 クリア（仮台本 06）。あかりの投稿が変わる。空の問い（一度目）。
    //   空の問いは 1度目「いらない」→2度目 無言→3度目「もう聞きません」の階段の初段。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「ほんと、バカなんだから。……あたしも、だけど。」", ""),   // A44。投稿が変化
        (2, "……あったかい声が、した。……知らない声なのに。変なの。", AFace),
        (1, "……字が、変わっていく。——『ありがとう』。……♥も、ひとつ。", MFace),   // 読み上げるだけ。解釈しない
        (1, "ねえ、ご主人様。外の世界は、今日はどんな天気ですか。", MFace),
        (1, "……いえ。返事は、いりません。いつか、で結構ですので。", MSmile),   // 空の問い・一度目
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
        _lineHold += delta;
        // ステージ経過タイム：クリア確定まで積算しHUDへ反映。
        if (!_clearing) { _stageElapsed += delta; Hud.SetElapsed((float)_stageElapsed); }
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 1 START"); }
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
            case 6: Step_Lines(delta, BossTalk); break;   // S1-7 道中B／向かいの席／「ぜんぶ浴びる」
            case 7: Step_MidwaveB(delta); break;          // 道中ザコ戦B（やや詰める）
            case 8: Step_MidStory(delta); break;          // ★S1-4 束（下書き選択）＝ボス前の溜め
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
            PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, fallSpeed: 48f,
                accent: new Color(0.47f, 0.65f, 0.85f)); // あかり面テーマ＝雨の青（教室の雨弾幕と同系）
    }

    private void Advance()
    {
        _step++;
        _stepStarted = false;
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
        // 案C のこの面に出るのは あなた(0)／ミナ(1)／あかり(2)／投稿(4)（あなたと投稿は Hud 側で立ち絵を捨てる）。
        string portrait = kind switch
        {
            Hud.LineKind.Boy => "",                                            // 「あなた」に顔は無い
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? AFace : face,   // あかりは行ごと差し替え可
            _ => string.IsNullOrEmpty(face) ? MFace : face,                    // ミナも行ごと表情
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    // ---- S1-4 束（ミッドシナリオ枠）：問いかけまで流す → 下書き選択 → 受け＋締め ----
    // 台本 06 の S1-4。ミナの「拾って、いいですか」で ChoiceOverlay（3択・N択対応版）を重ね、
    // 決まったら受けの1行と共通の締め（足音）を続けて流してから終盤Cへ。
    // 会話バブルは提示中も保持（HoldBubble）＝BubblePaused が続いて弾・敵は止まったまま。
    // 自動プレイ（--qa/--demo）は BubblePaused 中 Z をパルスし続けるので既定カーソルのまま即決される＝詰まらない。
    private ChoiceOverlay? _s14Choice;
    private double _s14ChoiceT;                       // 提示からの経過＝迷い秒数（RecordChoice へ渡す）
    private (int who, string text, string face)[] _s14After = System.Array.Empty<(int, string, string)>();
    private int _s14Phase;                            // 0=問いかけまで / 1=選択提示中 / 2=受け＋締め
    private const float S14SkipContam = 0.02f;        // （送らない）で汚染を微増（仕様未決につき小さく）
    private void Step_MidStory(double delta)
    {
        switch (_s14Phase)
        {
            case 0:
                // 束の提示〜「拾って、いいですか」まで。Step_Lines は流し切ると Advance するので、
                // ここは自前で終端を見て次フェーズへ落とす（step は 8 のまま）。
                RunLinesInPlace(delta, MidStory, () => { _s14Phase = 1; _stepStarted = false; });
                break;
            case 1:
                if (!_stepStarted)
                {
                    _stepStarted = true;
                    _s14ChoiceT = 0;
                    // 既定カーソルは末尾＝（送らない）。ChoiceOverlay の沈黙20秒の自動決定もここへ落ちる（台本どおり）。
                    _s14Choice = ChoiceOverlay.Show(Hud, S14Choices, defaultSel: S14Choices.Length - 1);
                }
                _s14ChoiceT += delta;
                if (_s14Choice == null || !_s14Choice.Decided) return;
                ApplyS14Choice(_s14Choice.Selected);
                _s14Choice.QueueFree();
                _s14Choice = null;
                _s14Phase = 2;
                _stepStarted = false;
                break;
            default:
                RunLinesInPlace(delta, _s14After, Advance);
                break;
        }
    }

    private void ApplyS14Choice(int sel)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        // 【散】は表示候補（上2件）のうち選ばれなかったぶんだけを計上する。
        //   （送らない）自体は言葉ではないので送信語にも散る語にも数えない＝表示候補2件が丸ごと散る。
        bool sent = sel < S14Choices.Length - 1;
        var others = new System.Collections.Generic.List<string>();
        for (int i = 0; i < S14Choices.Length - 1; i++) if (i != sel) others.Add(S14Choices[i]);
        game?.RecordChoice("s1_4", sent ? S14Choices[sel] : "", others, (float)_s14ChoiceT);
        // （送らない）＝声を掛けずに見送った ぶんだけ、ミナの光がわずかに濁る。
        if (!sent) game?.SetContamination((game.Contamination) + S14SkipContam);
        _s14After = S14Reply(sel).Concat(S14Tail).ToArray();
    }

    // 会話配列を「この step に留まったまま」流すヘルパ（Step_Lines と同じ送り作法。終端で onEnd を呼ぶ）。
    private void RunLinesInPlace(double delta, (int who, string text, string face)[] lines, System.Action onEnd)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _introLine = 0;
            _lineHold = 0;
            if (lines.Length == 0) { Hud.HoldBubble = false; Hud.HideBubble(); onEnd(); return; }
            Hud.HoldBubble = true;
            ShowLine(lines);
        }
        if (_zEdge && _lineHold >= 0.15 && !Hud.DialogRevealed)
        {
            Hud.RevealDialogNow();
            _lineHold = 0;
        }
        else if (_lineHold >= 0.15 && Hud.DialogRevealed
                 && (_zEdge || Hud.FastForwarding || (Hud.AutoAdvance && _lineHold >= 1.4)))
        {
            _lineHold = 0;
            _introLine++;
            if (_introLine >= lines.Length)
            {
                // 選択の提示中はバブルを保持したまま（BubblePaused 継続＝弾・敵を止めておく）。
                if (_s14Phase != 0) { Hud.HoldBubble = false; Hud.HideBubble(); }
                onEnd();
                return;
            }
            ShowLine(lines);
        }
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
                    // v3 の中ボスは穢れ形態を持たない1枚絵なので Pre/Cry/Post に同じパスを入れる
                    // （50px 表示のちびなので、姿が変わらない損失はほぼ無い）。
                    PreTex = "res://char/v3/akari_mid.png",
                    CryTex = "res://char/v3/akari_mid.png",
                    PostTex = "res://char/v3/akari_mid.png",
                    Face = AFaceLit,   // S1-5：片手のスマホの光が顔に当たっている＝画面の光を浴びた顔
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
            // ここで即QueueFreeしない＝ガワが割れて出てきた中の人の改心退場アニメ
            // （Enemy._Process の _purified 分岐。PurifiedExitHoldOverride）を最後まで再生させ、自然にQueueFreeさせる。
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
            long score = game?.Score ?? 0;
            var recScore = game?.RecordScore("akari", game.Difficulty, score) ?? (true, (long?)null);
            Hud.ShowClearBanner("STAGE 1 CLEAR", _clearTime, rec.isBest, rec.prev, score, recScore.isBest, recScore.prev);
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll(); // クリア時に自弾・残弾を一掃(#17)
        }
        Step_Lines(delta, Clear);
    }

    // ---- 6: STAGE2（こはる）へ ----
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
