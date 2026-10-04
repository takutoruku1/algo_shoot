using Godot;
using System.Linq;

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
    // ユーザー承認済み: docs/20260914/ストーリー添削_2026-09-14.md 【3】
    //   感情アークの厳格運用：ミナは「困る（あかり後）→笑う（こはる後）→泣く（レイ後）」の順に獲得する。
    //   あかり面は**まだ何も獲得していない**区間なので、笑顔の立ち絵（mina_smile.png）は一枚も出さない。
    //   軽口の行そのものは残す（緩急に要る）が、顔は平常のまま＝「面白いことを真顔で言う」機械に見せる。
    //   呼び出し側を全部書き換えると差分が広がるので、定数の**指し先**だけを平常顔へ倒している。
    private const string MFace = "res://char/mina_face.png";
    private const string MSmile = MFace;   // ※あかり面では笑わない（笑う＝こはる面クリア後に獲得）
    private const string MWorried = "res://char/mina_worried.png";
    // あかりの顔。平常＝akari_face／画面の光を浴びた＝akari_face_lit／泣き＝akari_face_cry。
    private const string AFace = "res://char/v3/akari_face.png";
    private const string AFaceLit = "res://char/v3/akari_face_lit.png";

    private Spawner _spawner = null!;
    private int _waveBase;
    private const int MidWave0 = 60;
    private const int MidWaveA = 24;
    private const int MidWaveB = 26;
    private const int MidWaveC = 28;
    // ボスの“チラ見せ”（カメオ）＝本戦ボスと同じ土台の短いミニボス戦（CameoBoss＝Enemy 派生・シールド制）。
    // あかり＝怯え・自責で、攻撃も悲嘆寄り。撃破（HP/サイクル削り切り＝改心）まで Stage は進まない。保険退場は廃止。
    private CameoBoss _cameo = null!;
    private bool _cameoIntroStarted, _cameoIntroDone;

    // S1-1 フロア・導入（仮台本 06）。雨の、誰もいない退勤後のオフィス。
    //   一面目なので「飛んでくるのは言葉であって本人ではない／奥の本人へ届けに行く」という
    //   この世界の決まりの提示をここが担う（06 の但し書き）。
    // who: 0=あなた（送信された下書き） / 1=ミナ / 2=あかり / 3=システム表示 / 4=投稿。who=5（中継）は使わない。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (4, "すき、すき、すき。……ひとつでいいから、本物になって。", ""),
        (0, "到着した。回線はつながってる。ミナ、聞こえる？", ""),
        (1, "はい。雨が、下から上へ……。机も椅子も、天井へ落ちていきます。", "res://char/mina_face.png"),
        (0, "ここはあかりさんの心が映る場所だ。会社そのものじゃない。見えている床より、僕のつける道のしるしを追って。", ""),
        (1, "承知しました。……初出勤にしては、通勤路が過酷ですね。", "res://char/mina_face.png"),
        (0, "悪いね。退勤の道まで、ちゃんと案内する。", ""),
        (1, "言いましたね。記録しましたよ。", "res://char/mina_face.png"),
        (0, "前方、アンチャー。飛んでくる言葉はよけて。進み方は、一つずつ伝える。", ""),
        (1, "はい。あの方のところへ、行きましょう。", "res://char/mina_face.png"),
    };

    // S1-9 ボス戦「あふれるわたし」の頭（仮台本 06）。ボスは出現済みだが会話中は止まる。
    //   宣言はカットイン『ねえ、こっち見て』と弾幕名の宣告（BossAkari.Spells / AnnounceSpell）に対応する。
    //   RECLOSE と最終形の宣言（「……返して。読んだなら、返してよ。」）は BossAkari 側に置いた。
    private static readonly (int who, string text, string face)[] BossIntro =
    {
        (2, "ねえ、こっち見て。", "res://char/v3/akari_face.png"),
        (2, "すきって言って。あたしも言う。……ずっと一緒。離さないから。", "res://char/v3/akari_face.png"),
        (1, "わたくしはミナです。あなたが待っている方ではありません。でも、ここでお話を聞かせてほしいのです。", "res://char/mina_face.png"),
        (2, "代わりじゃ、だめなの。……誰でもいいわけじゃ、ないの。", "res://char/v3/akari_face.png"),
    };

    // S1-2 小話 Mid（仮台本 06）。「返して」「すき」の声。ホワイトボードの字と置き傘を、ミナが自分で見つける。
    //   相方は「あなた」＝返事をしない相手なので、掛け合いではなく観測とひとり漫才で運ぶ。
    //   先頭2行は S1-5（中ボス）のミナ行。CameoBoss の一行オーバーレイは本人(who=2)しか流さないので、
    //   中ボスが消えた直後に開くこの step が受け皿になる（step 構成は変えない前提での置き場所）。
    //   17（道中の選択肢 案C）: この先頭2行＋「問いだけが残っています」の2行までを MidPre（きっかけ）に切り、
    //   下書き選択（s1_5）を挟んでから、受け＋この配列（S1-2 の小話）へ戻る。
    private static readonly (int who, string text, string face)[] MidPre =
    {
        (1, "雨の奥へ、行ってしまいました。……追っても、よろしいですか。", "res://char/mina_face.png"),
        (0, "道はつながってる。ただ、僕らを待っていた人と取り違えている。誰かの代わりに、約束はできない。", ""),
        (1, "分かっています。それでも、あのまま一人にはしたくありません。", "res://char/mina_face.png"),
        (0, "僕もだ。ミナ、君と一緒に、もう一度会いに行きたい。", ""),
    };
    private static readonly (int who, string text, string face)[] Mid =
    {
        (1, "ここの声は……どれも、「返して」「すき」と、すがりついてきます。……返事の声だけが、ひとつも、混じっていません。", "res://char/mina_face.png"),
        (0, "アンチャーがほどけて、道が広がってる。ミナ、身体は重くない？", ""),
        (1, "少し肩が軽くなった気がします。……今のところは。", "res://char/mina_face.png"),
        (1, "ホワイトボードに、字が。「あたしのせいだ」。……消しても消しても、浮いてくる、そういう字です。", "res://char/mina_face.png"),
        (0, "傘が二本あるね。片方には、まだ値札がついてる。", ""),
        (1, "……ちなみに。片方の柄に、値札が付いたままです。八百円。", "res://char/mina_face.png"),
        (0, "そこまで読んだの？　僕は色しか見てなかった。", ""),
        (1, "八百円でも、置いていくには惜しい傘です。……誰も取りに来ないのでしょうか。", "res://char/mina_face.png"),
        (1, "雨の音は、嫌いではありません。……うるさい、と思いながら、消していないので。", "res://char/mina_face.png"),
    };

    // S1-7 道中B／MidStory／道中C（仮台本 06）。向かいの席の暗いモニタと読めない付箋。「同じ部署」の声。
    //   返事の声だけが無い。「ぜんぶ浴びる」をミナ自身の方針として言う＝S1-10 の「証人」の仕込み。
    private static readonly (int who, string text, string face)[] BossTalk =
    {
        (1, "……向かいの席。モニタは、暗いまま。キーボードの上に、付箋が一枚。……字は、読めません。", "res://char/mina_face.png"),
        (4, "「向かいの席、空いたまま。……三日目。」", ""),
        (1, "「気づいてほしい、でも気づかれたら困る」……そういう声が、「同じ部署」という言葉と、いっしょに流れていきます。", "res://char/mina_face.png"),
        (1, "……返事の声だけが、ここまで来ても、ひとつも、ありません。", "res://char/mina_face.png"),
        (0, "消された言葉が残ってる。全部を一度に抱えなくていい。聞こえた順に、僕にも教えて。", ""),
        (1, "……はい。自分だけで持たなくてよいのですね。では、まずこの付箋から。", "res://char/mina_face.png"),
    };

    // S1-5 中ボス あかり（仮台本 06）。先出しの本人。退勤後のカーディガンに社員証、片手のスマホの光が顔に当たっている。
    //   CameoBoss は who=2（本人）の行だけを一行オーバーレイで流す（IntroLines＝第一声／TauntLines＝RECLOSE／
    //   DefeatLines＝捨て台詞）。ミナの2行はオーバーレイに乗らないので S1-4 の締めと Mid の頭に置いてある。
    //   「見て」は第一声の「見たよね」までにとどめ、以後は繰り返さない（12）。
    //   顔は「画面の光を浴びた」akari_face_lit（片手のスマホの光が顔に当たっている状態）。
    private static readonly (int who, string text, string face)[] CameoTalk1 =
    {
        (2, "あ、来た！　読んだよね？　返事、まだ？", "res://char/v3/akari_face.png"),
        (1, "読んだのは、わたくしです。どなたからのお返事を待っているのですか？", "res://char/mina_face.png"),
        (2, "……あなたじゃ、ない。あの人に、返してほしいの。", "res://char/v3/akari_face.png"),
    };
    // RECLOSE（サイクルごとに順送り）。
    private static readonly (int who, string text, string face)[] CameoTalk3 =
    {
        (2, "ひとりにしないで。……ねえ、ひとりに、しないでってば。", "res://char/v3/akari_face.png"),
        (2, "来ないで……っ。……ちがう、来て。……来ないで。", "res://char/v3/akari_face.png"),
    };
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "ぜったい、また会いに来てよね。ぜったいだよ?", "res://char/v3/akari_face.png"),
        (1, "また会いに来ます。今度は、お名前を聞かせてください。", "res://char/mina_face.png"),
    };

    // ───────── S1-4 束（ミッドシナリオ枠＝後半Bと終盤Cの境・ボス前の“溜め”）─────────
    // 仮台本 06 の S1-4。宙に浮いた机に、社内チャットの「メッセージの送信を取り消しました」だけが縦に積み上がった束。
    // 本文はひとつも残っていない。拾うかどうかを「あなた」に聞く＝この面唯一の下書き選択（ChoiceOverlay）。
    // 吹き出し会話（Step_Lines）で出す＝弾は止まる。
    private static readonly (int who, string text, string face)[] MidStory =
    {
        (4, "「送信取消。今日で十二回目。……全部、同じ人宛。」", ""),
        (1, "ご主人様、机の上に束が。「メッセージの送信を取り消しました」。こればかりです。", "res://char/mina_face.png"),
        (0, "十二通。さっきの投稿と同じだ。本文が見えないぶん、何を書こうとしたのか気になる。", ""),
        (1, "わたくしもです。……一通、開いてみても？", "res://char/mina_face.png"),
    };

    private static readonly string[] S14Choices = { "あの人の言葉、消えたままにしたくない", "一つずつ聞こう。ミナも無理しないで" };
    private static (int who, string text, string face)[] S14Reply(int sel) => sel switch
    {
        0 => new (int, string, string)[] {
        (0, "あの人の言葉、消えたままにしたくない", ""),
        (1, "……はい。消す前には、伝えたかった言葉があったはずです。一つずつ、聞いていきましょう。", "res://char/mina_face.png"),
        (1, "十二件ぶん。……ご主人様も、一緒に聞いていてください。", "res://char/mina_face.png"),
    },
        _ => new (int, string, string)[] {
        (0, "一つずつ聞こう。ミナも無理しないで", ""),
        (1, "……わたくしのことまで。ありがとうございます。では、いちばん上の一通から。", "res://char/mina_face.png"),
        (1, "残りは、今は開かずにおきます。……急がなくてよいと、言っていただけたので。", "res://char/mina_face.png"),
    },
    };
    // 選択の受けの後に必ず流す締め（中ボスが来る予感）。
    private static readonly (int who, string text, string face)[] S14Tail =
    {
        (0, "前方に反応。雨の奥から、あかりさんが来る。ミナ、いったん足を止めて。", ""),
        (1, "見えました。……今度は、こちらを見てくださるでしょうか。", "res://char/mina_face.png"),
    };

    // ───────── 道中の下書き選択（正典: wiki/08_仮台本/17_道中の選択肢_案C.md・承認 2026-09-06）─────────
    //   s1_5 … 中ボスの捨て台詞の直後（Mid の頭＝step 4）。効果＝ハブ返信（ミナ→@akari）に一語混ざる。

    private static readonly string[] S15Choices = { "ミナ、もう一度会いに行こう。放っておけない。", "うまく言えないけど、ひとりにしたくない" };
    private static (int who, string text, string face)[] S15Reply(int sel) => sel switch
    {
        0 => new (int, string, string)[] {
        (0, "ミナ、もう一度会いに行こう。放っておけない。", ""),
        (1, "はい。今度は、わたくしたちの名前も伝えましょう。", "res://char/mina_face.png"),
    },
        _ => new (int, string, string)[] {
        (0, "うまく言えないけど、ひとりにしたくない", ""),
        (1, "……いまの言葉で、伝わりました。うまく言えなくても、そばへ行くことはできます。わたくしも、ご一緒します。", "res://char/mina_face.png"),
    },
    };
    private static readonly (int who, string text, string face)[] S15Tail =
    {
        (0, "返事はまだない。雨の奥へ続く道が見えた。ミナ、右の通路へ。", ""),
        (1, "はい。……次は、置き去りの問いだけにしません。", "res://char/mina_face.png"),
    };

    // S1-8 小話 MidEnd（仮台本 06）。投稿の直後、通知の吹き出しが「1」のまま四つ同じ形で降ってくる。
    //   フロアが「すき」で埋まっていく。ボス戦直前の引き。
    private static readonly (int who, string text, string face)[] MidEnd =
    {
        (4, "同期が、新しい職場へ。\nあたしも、負けずに頑張らなきゃ。\nおめでとう！", ""),
        (1, "……公開された本文の下に、何度も書き直した跡が、重なっています。", "res://char/mina_face.png"),
        (4, "「いいねが、ひとつ。……増えてないの、知ってるのに、今日だけで四回も、見にきちゃった。」", ""),
        (0, "同じ通知を、何度も確かめた跡だ。増えていないと知ってても、開いてしまうんだね。", ""),
        (1, "ホワイトボードも、モニタも、窓も……ぜんぶ「すき」で、埋まっていきます。取り消したぶんが、フロアじゅうに、あふれている。", "res://char/mina_face.png"),
        (1, "吹き出しに、全部「またね」と。……ご主人様、これを祓うのは、少し嫌です。", "res://char/mina_face.png"),
        (0, "消すのは言葉じゃない。言葉を閉じ込めているアンチャーのほうだ。狙う場所を示すよ。", ""),
        (1, "奥に、あの人が。……行きます。今度こそ、奥まで。", "res://char/mina_face.png"),
    };

    // S1-11 クリア（仮台本 06）。あかりの投稿が変わる。空の問い（一度目）。
    //   2026-09-26（docs/20260926/主人公の存在_診断と本文 §3.1(c)）：あかりが「知らない声」の言い回しに聞き覚えを言い、
    //   ミナが「たぶん」と受ける＝ミナの声の出所（あなたの未送信414件）に最初のひびが入る対句。説明はしない。
    //   ★の行は迷い秒ゲート（s1_4 で p2 より長く迷ったときだけ＝ChoiceEffects.Hesitated）。実行時に ClearFor が残す／外す。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (2, "……あったかい声がした。ミナの声。知らない人なのに、変なの。", "res://char/v3/akari_face.png"),
        (1, "知らない人同士から、始めてもよろしいでしょう。わたくしも、今日初めてあなたに会いました。", "res://char/mina_face.png"),
        (2, "ふふ。そうだね。……あの人の返事は、代わりにくれなくていいから。", "res://char/v3/akari_face.png"),
        (1, "はい。あかりさんの言葉を、聞いていました。", "res://char/mina_face.png"),
        (2, "ありがと。これは、消さない。", "res://char/v3/akari_face.png"),
        (1, "もしよければ、今度は帰ってからのお話も。相談に乗ってくれた方を、ご紹介したいのです。", "res://char/mina_face.png"),
        (2, "ミナにも、そんな人がいるんだ。……うん、話してみたい。", "res://char/v3/akari_face.png"),
        (0, "あかりさん。初めまして。ミナと一緒に、あなたの話を聞いていた。", ""),
        (2, "あ……今、初めて聞こえた。あなたが、ミナと？", "res://char/v3/akari_face.png"),
        (0, "うん。話してくれてありがとう。寂しかった気持ちまで、消さなくてよかった。", ""),
        (2, "……ミナに似たこと言うんだね。そっか。二人で来てくれたんだ。", "res://char/v3/akari_face.png"),
        (0, "帰還の道を開くよ。ミナ、お疲れさま。", ""),
        (1, "……ご主人様。外の世界は、今日はどんな天気ですか。", "res://char/mina_face.png"),
        (0, "ずっと画面を見てたから、分からないや。", ""),
        (1, "道案内の方にも、見落としがあるのですね。……あとで、教えてください。", "res://char/mina_face.png"),
    };

    // 迷い秒ゲートの行（★）と、フィルム前／後の割り目。
    //   割り目は行数（旧 Take(2)）ではなく本文「……♥が、ひとつ。」で引く＝ゲートで前半の行数が揺れても崩れない。

    private const string ClearFilmSplit = "帰還の道を開くよ。ミナ、お疲れさま。";
    private static readonly string[] SkyChoices = { "あとで空を見よう。ミナと一緒に。", "今は、ミナが無事でほっとしてる。" };

    private static (int who, string text, string face)[] SkyReply(int sel) => sel switch
    {
        0 => new (int, string, string)[] {
        (0, "あとで空を見よう。ミナと一緒に。", ""),
        (1, "一緒に。はい。ご主人様が見上げた空も、聞かせてください。", "res://char/mina_face.png"),
        (0, "約束する。今度は、窓のところまで行ってみるよ。", ""),
    },
        _ => new (int, string, string)[] {
        (0, "今は、ミナが無事でほっとしてる。", ""),
        (1, "……わたくしを、心配してくださっていたのですね。ただいま、ご主人様。", "res://char/mina_face.png"),
        (0, "おかえり。初めてのダイブ、一緒に帰れてよかった。", ""),
    },
    };

    private static (int who, string text, string face)[] ClearFor(GameManager? game) => Clear;
    private static (int who, string text, string face)[] ClearBeforeFor(GameManager? game)
        => ClearFor(game).TakeWhile(l => l.text != ClearFilmSplit).ToArray();
    private static (int who, string text, string face)[] ClearAfterFor(GameManager? game)
        => ClearFor(game).SkipWhile(l => l.text != ClearFilmSplit).ToArray();
    // クリア確定時（Step_Clear のバナー表示）に台帳を読んで組む。既定はゲート無しの並び。
    private (int who, string text, string face)[] _clearBefore = ClearBeforeFor(null);
    private (int who, string text, string face)[] _clearAfter = ClearAfterFor(null);

    private (int who, string text, string face)[] _playerIntro = null!;
    private (int who, string text, string face)[] _playerMid = null!;
    private (int who, string text, string face)[] _playerBoss = null!;

    // ── 他ジョブ潜行（2026-09-15）──
    //   結び手以外で潜ったとき true。ミナの行（who=1/3）・下書き選択を抑止し、
    //   ビート枠（出撃／道中3節目／ボス前／帰還）を CharacterStory のテーブルへ全面置換する。
    //   回想（memory）と撃破後のアフターのフィルムは操作キャラに依らず**この面のボス＝あかり**のもの
    //   （2026-09-23 ユーザー報告。以前は操作キャラ×章の CharacterStoryFilm を流していた）。
    //   改心相当シーン（山場）は BossAkari 側が CharacterStory.Redemption で差し替える。
    private bool _charStory;
    private (int who, string text, string face)[] _storyMid1 = System.Array.Empty<(int, string, string)>();
    private (int who, string text, string face)[] _storyMid2 = System.Array.Empty<(int, string, string)>();
    private (int who, string text, string face)[] _storyMid3 = System.Array.Empty<(int, string, string)>();
    private (int who, string text, string face)[] _storyReturn = System.Array.Empty<(int, string, string)>();
    // 他ジョブ潜行の撃破後アフター（CharacterStory.Aftermath＝潜行キャラ×この面のボスの9通り）。
    //   フィルムの代わりに会話で流し、そのあと _storyReturn（帰還ビート）へ続ける。
    private (int who, string text, string face)[] _storyAftermath = System.Array.Empty<(int, string, string)>();

    private bool _lunatic;
    // 会話・選択の step 一覧（1 イントロ／4 s1_5／6 道中会話／8 S1-4 束／10 MidEnd／12 ボス口上）。ルナティックはここを飛ばす。
    private static bool IsTalkStep(int step) => step is 1 or 4 or 6 or 8 or 10 or 12;
    // ルナティックのクリア：アフターの代わりに、リザルトのバナーを読む間だけ置いてから帰る。
    private const double LunaticClearHold = 3.0;
    private double _lunaticClearT;

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中（肩慣らし0＋A+B+C 三波）＋ボスで浄化カプセルが満ちる（部屋が晴れる）。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        var job = game?.SelectedJob ?? Job.Tank;
        _lunatic = game?.IsLunatic == true;
        // 会話の実体：結び手＝ミナ本編（従来）／他ジョブ＝キャラ専用ストーリー（章は GameManager が管理）。
        //   ※旧 CompanionDialogue.Add（本編＋同行3行）はステージ内では廃止＝全面置換に一本化（2026-09-15）。
        _charStory = CharacterStory.DiveActive(game);
        if (_charStory)
        {
            int ch = game!.CharacterChapter(job);
            _playerIntro = CharacterStory.Lines(job, ch, CharacterStory.Beat.Sortie);
            _storyMid1 = CharacterStory.Lines(job, ch, CharacterStory.Beat.Mid1);
            _storyMid2 = CharacterStory.Lines(job, ch, CharacterStory.Beat.Mid2);
            _storyMid3 = CharacterStory.Lines(job, ch, CharacterStory.Beat.Mid3);
            _playerMid = CharacterStory.Lines(job, ch, CharacterStory.Beat.PreBoss);
            _storyReturn = CharacterStory.Lines(job, ch, CharacterStory.Beat.Return);
            _storyAftermath = CharacterStory.Aftermath(job, "akari");
        }
        else
        {
            _playerIntro = Intro;
            _playerMid = MidEnd;
        }
        _playerBoss = BossIntro;   // ボス本人の口上（who=2）はどちらのモードでも流す
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
        _zHeld = Pad.AdvanceHeld();
        // ルナティックはイントロを流さない＝ここで once を消費させず、_Process の先頭で step 1 を飛ばす。
        if (_lunatic) return;
        // 初見チュートリアル（2026-09-16）：セーブで最初の道中入りに一度だけ、イントロ末尾＝道中開始の
        //   直前にミナの説明を流す。_step==1 確定後に繋ぐ＝チェックポイント入口（中ボス/ボスから）では
        //   イントロごと飛ぶので消費しない。結び手のみ・once はセーブ単位（StageTutorial が一括で判定）。
        if (_step == 1) _playerIntro = _playerIntro.Concat(StageTutorial.TakeRoute(game)).ToArray();
        // 習得スキル説明（2026-09-22）：回避／溜め打ち（どちらもショップ）を覚えたあと最初に入った面で
        //   一度だけ、チュートリアルの直後＝アンチャー紹介の前に使い方を流す（操作の説明が先）。
        //   未習得は出さず once も消費しない。結び手潜行のみ（StageTutorial.TakeSkillIntros）。
        if (_step == 1) _playerIntro = _playerIntro.Concat(StageTutorial.TakeSkillIntros(game)).ToArray();
        // アンチャー紹介（2026-09-17）：チュートリアルの後ろ＝道中開始の直前に、この面のアンチャーの
        //   性格だけを流す（一般→個別）。once は面ごと（once_ankers_akari）＝ステージ初回のみ。
        if (_step == 1) _playerIntro = _playerIntro.Concat(StageTutorial.TakeAnkerAkari(game)).ToArray();
        // 強化アイテム説明（2026-09-22）：アンチャー紹介の直後＝道中開始の直前に、ザコが落とす強化欠片の
        //   拾い方・効果・表示位置・被弾で失うことを流す。最初の面（あかり）だけ。once はセーブ単位
        //   （once_items_akari）。結び手潜行のときだけ・他ジョブでは非表示かつ非消費（StageTutorial.Take）。
        if (_step == 1) _playerIntro = _playerIntro.Concat(StageTutorial.TakeItemIntroAkari(game)).ToArray();
        if (_step == 1) Step_Lines(0, _playerIntro);
    }

    private bool _startBannerShown;

    public override void _Process(double delta)
    {
        if (Hud.CinematicMode) { _zHeld = Pad.AdvanceHeld(); return; }
        _lineHold += delta;
        if (!_clearing && !Hud.BubblePaused) { _stageElapsed += delta; Hud.SetElapsed((float)_stageElapsed); }
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowStageStart(1, "あかり", new Color("efbc87")); }
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        _zEdge = z && !_zHeld;
        _zHeld = z;
        // ルナティック：会話・選択の step は踏まずに次の戦闘 step へ（同じフレームで次の波が立つ＝空白を作らない）。
        while (_lunatic && IsTalkStep(_step)) Advance();
        switch (_step)
        {
            case 1: Step_Lines(delta, _playerIntro); break;
            case 2: Step_MidWave0(delta); break;
            case 3: Step_BossCameo(delta); break;         // ボスのチラ見せ（先出し＝あかりから割り込んで来る）
            // ★S1-5 の下書き選択（17）＝中ボスの受け2行＋問い → 選択 → 受け＋締め → S1-2 の小話
            //   他ジョブ潜行中は下書き選択ごと抑止（ミナ前提）＝専用ストーリーの道中ビートに置換。
            case 4: if (_charStory) Step_Lines(delta, _storyMid1); else Step_Choice(delta, "s1_5", MidPre, S15Choices, S15Reply, S15Tail, Mid); break;
            case 5: Step_MidwaveA(delta); break;          // 道中ザコ戦A（導入）
            case 6: Step_Lines(delta, _charStory ? _storyMid2 : BossTalk); break;
            case 7: Step_MidwaveB(delta); break;          // 道中ザコ戦B（やや詰める）
            case 8: if (_charStory) Step_Lines(delta, _storyMid3); else Step_MidStory(delta); break;   // ★S1-4 束（下書き選択）＝ボス前の溜め
            case 9: Step_MidwaveC(delta); break;          // 道中ザコ戦C（終盤＝最大密度の山）
            case 10: Step_Lines(delta, _playerMid); break;
            case 11: Step_BossSpawn(); break;
            case 12: Step_Lines(delta, _playerBoss); break;
            case 13: Step_BossWait(delta); break;
            case 14: Step_Clear(delta); break;
            case 15: Step_Transition(); break;
        }
        // ボス戦中の ambient は、全ボス共通の投稿弾（Y投稿モチーフ＝ティッカー連動の言葉弾）に統一。
        // 旧「ただの自責の雨（落下弾）」は止め、Rei と同じく投稿弾のみ降らせる（難易度で数がスケール）。
        // あかり面は PostPool のあかりのテーマ（09 の A01〜A40 由来の 8 文字弾）を引く。
        // 下を流れるコメント（ティッカー）も同じプールを見る＝そのまま降る一体感は保つ。
        // ボス本体(BossAkari)のスペル/予測線/パネル弾はそのまま。
        // イライラ棒「雨の帰り道」（CorridorRun 展開中）は降らせない＝通路避けに弾を重ねる理不尽を断つ。
        if (_bossActive && _boss?.PostSequenceActive != true && _boss?.EdgeAttackActive != true
            && GetTree().GetFirstNodeInGroup("corridor") == null)
            PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, source: _boss!, theme: PostPool.Theme.Akari, fallSpeed: 48f,
                accent: new Color(0.47f, 0.65f, 0.85f)); // あかり面テーマ＝雨の青（教室の雨弾幕と同系）
    }

    // 背景はここでは触らない：会話・選択肢は直前の戦闘背景（道中パノラマ／中ボスの部屋／ボスの部屋）の上で進む。
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
                 && (_zEdge || Hud.FastForwarding || (Hud.AutoAdvanceReady && _lineHold >= 1.4)))  // FastForwarding=既読スキップ（Ctrl/RB長押し・既読行のみ・#22）
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
        // 他ジョブ潜行では潜行キャラ本人(6)が加わる＝face をそのまま渡す（空欄は Hud がジョブ立ち絵へ落とす）。
        string portrait = kind switch
        {
            Hud.LineKind.Boy => "",                                            // 「あなた」に顔は無い
            Hud.LineKind.Companion => face,                                    // 表情差分は face 指定、空欄＝ジョブ立ち絵
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? AFace : face,   // あかりは行ごと差し替え可
            _ => string.IsNullOrEmpty(face) ? MFace : face,                    // ミナも行ごと表情
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "あかり");
        // 初見チュートリアル（2026-09-17）：操作の話をしている行では盤面中央に操作カードを出す。
        //   道中チュートリアル本文の行でなければ内部で畳む＝通常の会話には一切干渉しない。
        StageTutorial.SyncCard(Hud, lines, _introLine);
    }

    private ChoiceOverlay? _s14Choice;
    private double _s14ChoiceT;                       // 提示からの経過＝迷い秒数（RecordChoice へ渡す）
    private (int who, string text, string face)[] _s14After = System.Array.Empty<(int, string, string)>();
    private int _s14Phase;                            // 0=問いかけまで / 1=選択提示中 / 2=受け＋締め
    private void Step_MidStory(double delta)
    {
        switch (_s14Phase)
        {
            case 0:
                // 束の提示〜「拾って、いいですか」まで。Step_Lines は流し切ると Advance するので、
                // ここは自前で終端を見て次フェーズへ落とす（step は 8 のまま）。
                _holdForChoice = true;
                RunLinesInPlace(delta, MidStory, () => { _s14Phase = 1; _stepStarted = false; });
                break;
            case 1:
                if (!_stepStarted)
                {
                    _stepStarted = true;
                    _s14ChoiceT = 0;
                    _s14Choice = ChoiceOverlay.Show(Hud, S14Choices, defaultSel: S14Choices.Length - 1, onBoard: true);
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
                _holdForChoice = false;
                RunLinesInPlace(delta, _s14After, Advance);
                break;
        }
    }

    private void ApplyS14Choice(int sel)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        ChoiceEffects.Record(game, "s1_4", S14Choices, sel, (float)_s14ChoiceT);
        _s14After = S14Reply(sel).Concat(S14Tail).ToArray();
    }

    // ---- 道中の下書き選択（17）の汎用三フェーズ：きっかけ → 選択 → 受け＋締め（＋残りの会話）----
    // 型は S1-4（Step_MidStory）と同じで、id・候補・受け・締めを引数で受けるようにしただけ。
    //   提示中もバブルは保持（HoldBubble）＝BubblePaused が続いて弾・敵は止まったまま。
    //   自動プレイ（--qa/--demo）は BubblePaused 中 Z をパルスし続けるので既定カーソル
    //   のまま即決される＝詰まらない。tail の後ろに rest を繋げば、選択のあとに元の会話の続きを流せる。
    private ChoiceOverlay? _choice;
    private double _choiceT;                          // 提示からの経過＝迷い秒数（RecordChoice へ渡す）
    private (int who, string text, string face)[] _choiceAfter = System.Array.Empty<(int, string, string)>();
    private int _choicePhase;                         // 0=きっかけ / 1=選択提示中 / 2=受け＋締め
    private void Step_Choice(double delta, string id,
        (int who, string text, string face)[] cue, string[] choices,
        System.Func<int, (int who, string text, string face)[]> reply,
        (int who, string text, string face)[] tail,
        (int who, string text, string face)[]? rest = null)
    {
        switch (_choicePhase)
        {
            case 0:
                // Step_Lines は流し切ると Advance するので、ここは自前で終端を見て次フェーズへ落とす。
                _holdForChoice = true;
                RunLinesInPlace(delta, cue, () => { _choicePhase = 1; _stepStarted = false; });
                break;
            case 1:
                if (!_stepStarted)
                {
                    _stepStarted = true;
                    _choiceT = 0;
                    _choice = ChoiceOverlay.Show(Hud, choices, defaultSel: choices.Length - 1, onBoard: true);
                }
                _choiceT += delta;
                if (_choice == null || !_choice.Decided) return;
                int sel = _choice.Selected;
                var game = GetNodeOrNull<GameManager>("/root/Game");
                ChoiceEffects.Record(game, id, choices, sel, (float)_choiceT);
                _choiceAfter = reply(sel).Concat(tail).Concat(rest ?? System.Array.Empty<(int, string, string)>()).ToArray();
                _choice.QueueFree();
                _choice = null;
                _choicePhase = 2;
                _stepStarted = false;
                break;
            default:
                _holdForChoice = false;
                RunLinesInPlace(delta, _choiceAfter, () => { _choicePhase = 0; Advance(); });
                break;
        }
    }

    // 流し切ってもバブルを閉じない（＝直後に選択が重なる）か。RunLinesInPlace の終端処理だけが見る。
    private bool _holdForChoice;

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
                 && (_zEdge || Hud.FastForwarding || (Hud.AutoAdvanceReady && _lineHold >= 1.4)))
        {
            _lineHold = 0;
            _introLine++;
            if (_introLine >= lines.Length)
            {
                // この後すぐ選択が重なる場面（きっかけの流し切り）ではバブルを保持したまま渡す
                //   ＝BubblePaused が途切れず、弾・敵は止まったまま ChoiceOverlay が乗る。
                if (!_holdForChoice) { Hud.HoldBubble = false; Hud.HideBubble(); }
                onEnd();
                return;
            }
            ShowLine(lines);
        }
    }

    private void Step_MidWave0(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            StartMidwaveSpawner();
        }
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
        (GetTree().GetFirstNodeInGroup("stagebg") as StageBackground)?.BeginRoute();
        // 道中の段の頭（0/A/B/C の各波）＝直前の波で盤面の弾とザコを掃き切ってから湧き直す仕切り直し。
        //   敵がゼロから湧き直すのに自機だけ前の段の位置に残るのが非対称なので、自機も初期位置へ戻す。
        //   ・ルナティックは除外：会話の区切りを全部飛ばして戦闘を途切れさせない難易度なので、ここで
        //     段ごとに入力を預かると「途切れない」設計を壊す（中ボス・ボス・形態変化は向こうでも戻す）。
        //   ・ステージ開始直後の波0 では自機がもう初期位置に居る＝ReturnToStart 側が距離を見て何もしない。
        if (!_lunatic) Player?.ReturnToStart();
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
        if (!_lunatic && !_cameoIntroDone)
        {
            if (!_cameoIntroStarted)
            {
                _cameoIntroStarted = true;
                CameoIntroScene.Play(Hud, World, "akari", CameoTalk1, () => _cameoIntroDone = true);
            }
            return;
        }
        if (!_stepStarted)
        {
            _stepStarted = true;
            (GetTree().GetFirstNodeInGroup("stagebg") as StageBackground)?.BeginMidboss();
            _cameo = new CameoBoss
            {
                Name = "AkariCameo",
                Theme = new CameoTheme
                {
                    DisplayName = "あかり", Handle = BossHandles.AkariBar,
                    PreTex = "res://char/v3/akari_mid_v2.png",
                    CryTex = "res://char/v3/akari_mid.png",
                    PostTex = "res://char/v3/akari_mid.png",
                    Face = AFaceLit,   // S1-5：片手のスマホの光が顔に当たっている＝画面の光を浴びた顔
                    SpellTint = new Color("6c9cd8"), SpellShape = BulletShape.Needle,
                    Fire = CameoFireTheme.AkariGrief,
                    Aura = FxLayer.BossAura.Akari,
                    Bgm = Audio.Instance?.BgmBossAkari,
                    IntroLines = _lunatic ? CameoTalk1 : System.Array.Empty<(int, string, string)>(),
                    TauntLines = CameoTalk3, DefeatLines = CameoPost,
                },
            };
            World.AddChild(_cameo);
            _cameo.GlobalPosition = new Vector2(SpawnX, 70f);
            // 中ボス戦の開始＝中ボスが湧き位置(SpawnX,70)に立つ仕切り直し。自機も初期位置へ戻す
            //   （ルナティックでも戻す＝新しい敵が定位置に現れる瞬間はどの難易度でも仕切り直し）。
            //   実際に滑り出すのは中ボス登場のカットシーン／会話が明けたフレーム（Player.ReturnToStart 参照）。
            Player?.ReturnToStart();
        }

        // 撃破→捨て台詞を流し切ったら次フェーズへ（型崩し後：道中突入の小話 Mid へ）。
        if (!IsInstanceValid(_cameo) || _cameo.Finished)
        {
            Hud.HideBossBar();                                   // バー出っ放しにしない（後で本ボスが再表示）
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            if (IsInstanceValid(_cameo)) _cameo.QueueFree();
            // 中ボス撃破フック：撃破記録（「中ボスから」入口の解放）。ショップ説明は最初の面のボス撃破後へ移した（2026-09-07）。
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
            // 今ランでボス戦に到達した印（ゲームオーバーの「ボスから」はこれが立っているときだけ出る。中ボスでは立てない）。
            GetNodeOrNull<GameManager>("/root/Game")?.NotifyBossReached();
            var opening = _playerBoss;
            if (!_lunatic) _playerBoss = StageTutorial.TakeBoss(GetNodeOrNull<GameManager>("/root/Game"));
            if (!_lunatic) CameoIntroScene.PlayBoss(Hud, World, "akari", opening, () => {
                _zHeld = Pad.AdvanceHeld(); _zEdge = false;
                Step_Lines(0, _playerBoss);
            }, SpawnBoss);
            else SpawnBoss();
        }

        void SpawnBoss()
        {
            _boss = new BossAkari { Name = "BossAkari" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            // ボス戦の開始＝ボスが湧き位置に立つ仕切り直し。自機も初期位置へ戻す（ルナティックでも戻す）。
            //   ここはボス登場のカットシーン（CameoIntroScene.PlayBoss の arriving）の中なので、実際に
            //   滑るのは口上の会話が明けて操作が戻ったフレーム（Player.ReturnToStart 参照）。
            Player?.ReturnToStart();
            _bossActive = true;
            (GetTree().GetFirstNodeInGroup("stagebg") as StageBackground)?.EnterBoss();
            Advance();
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
    private int _clearPhase;
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
            _clearBefore = ClearBeforeFor(game);   // 迷い秒ゲート（s1_4）をここで確定
            _clearAfter = ClearAfterFor(game);
        }
        if (_lunatic)
        {
            _lunaticClearT += delta;
            if (_lunaticClearT >= LunaticClearHold) Advance();
            return;
        }
        // 撃破後のアフター：
        //   ミナ本編＝ClearBefore → あかりのフィルム → ClearAfter。
        //   他ジョブ潜行＝一枚絵を起こさず CharacterStory.Aftermath（潜行キャラ×この面のボスの9通り）を
        //     会話で流してから、既存の帰還ビート（_storyReturn）へ（2026-09-23 ユーザー指示「吹き出しのやり取りだけに」）。
        if (_clearPhase == 0)
        {
            if (_charStory) RunLinesInPlace(delta, _storyAftermath, MarkAftermathSeenThenReturn);
            else RunLinesInPlace(delta, _clearBefore, StartAftermathFilm);
        }
        else if (_clearPhase == 2)
        {
            if (_charStory) Step_Lines(delta, _storyReturn);
            else Step_Choice(delta, "s1_sky", _clearAfter, SkyChoices,
                SkyReply, System.Array.Empty<(int, string, string)>());
        }
    }

    // 他ジョブ潜行のアフターを流し切ったところ。写真アプリの「帰還」枚を解禁して、帰還ビートへ。
    private void MarkAftermathSeenThenReturn()
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game != null) FilmSkip.MarkSeen(game, CharacterStory.SeenKey(game.SelectedJob, aftermath: true));
        _clearPhase = 2;
        _stepStarted = false;
    }

    private void StartAftermathFilm()
    {
        _clearPhase = 1;
        AkariStoryFilm.Play(Hud, World, aftermath: true, completed: () =>
        {
            _clearPhase = 2;
            _stepStarted = false;
            _zHeld = Pad.AdvanceHeld();
            _zEdge = false;
        });
    }

    // ---- 6: STAGE2（こはる）へ ----
    private bool _clearing;
    private void Step_Transition()
    {
        if (_clearing) return;
        _clearing = true;
        // 撃破後のアフターは Step_Clear で流し終えている（この面のボスのフィルム）。ここは帰るだけ。
        ReturnToHub();
    }

    private void ReturnToHub()
    {
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        // 2026-09-22: ここで強化ショップの説明パートへ離脱するのをやめ、かならずハブへ帰すようにした。
        //   強化が解禁されるのはこの瞬間だが、説明とショップを自動で開くと「ホームのアイコンを押して入る」
        //   操作をプレイヤーが一度も経験しない。説明はハブ側が帰還会話と解禁演出のあとに一度だけ挟む。
        GetNodeOrNull<GameManager>("/root/Game")?.CompleteStage("akari");
        // 暗転してからハブへ（ボス背景のフラッシュ止め・2026-09-22。StageRei と同じ理由）。
        GameManager.FadeToScene(this, "res://Hub.tscn");
    }

    // 投稿弾（ティッカー連動の言葉弾）の周期/tick 用アキュムレータ。
    // 湧き処理は全ボス共通ヘルパ PostBullets.Tick に集約（難易度で数がスケール）。
    private int _wordTick;
}
