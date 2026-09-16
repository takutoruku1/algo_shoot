using Godot;

// CharacterStory : 他ジョブ潜行（結び手＝ミナ以外のジョブで STAGE1〜3 に潜る）専用ストーリーの台詞テーブル集約。
//   2026-09-15 ユーザー承認仕様：他ジョブで潜ったランではミナは一切登場しない（who=1/3 の全行と
//   下書き選択・StoryFilm 回想・ミナ前提の改心シーンを抑止）。代わりに潜行キャラ本人の専用ストーリーを、
//   本編と同じビート枠（出撃／道中の節目3箇所／ボス前／改心相当／帰還）へ全面置換で流す。
//   ※FINAL は例外＝ジョブに関わらず常にミナ本編（StageMina 側で固定済み）。ハブ／ショップ／
//     トレーニングの掛け合い（CompanionDialogue.Menu*）も対象外＝現状維持。
//
// ── 本文の正典 ──
//   docs/20260915/キャラ別ストーリー_設計と本文_2026-09-15.md（scenario 執筆・第2部）。
//   ト書き（// ここでBGM停止 等）は RedemptionSilenceAt が実装を担う。
//
// ── ビート構造（共通契約）──
//   Sortie（出撃時）→ Mid1 → Mid2 → Mid3（道中の節目3箇所）→ PreBoss（ボス前）→ Return（帰還時）。
//   道中ビートは「キャラ×章」で引く＝どの面に潜っても、そのキャラの物語が章順に進む
//   （面の固有物は一切参照しない＝doc 第1部2節の設計）。
//   改心相当シーン（Redemption＝山場）だけは「キャラ×潜った面」の9通りで引く（相手ボスと噛み合わせるため）。
//
// ── 章管理 ──
//   キャラごとのダイブ回数（GameManager.CharacterDives・save_N.json "charDives" に永続）から
//   GameManager.CharacterChapter が章を決める：1〜3回目＝第1〜3章、4回目以降＝LoopChapter（ループ章＝毎回同じ軽量セット）。
//
// ── who の約束（Hud.LineKind と同値）──
//   6=潜行キャラ本人（話者名は素の名前。face 指定行は表情差分、空欄はジョブの立ち絵＝Hud.ShowDialog 参照）／
//   2=相手ボス／4=Ｘ投稿（face="" 固定）。1（ミナ）と 3（ナレ＝ミナの肉声）と 0/5 はこのテーブルでは使用禁止
//   （CompanionDialogueQa が全テーブルを機械検査する）。
public static class CharacterStory
{
    // ビート（本編ステージの会話ステップと 1:1 で対応する枠）。
    public enum Beat { Sortie, Mid1, Mid2, Mid3, PreBoss, Return }

    // 4回目以降のダイブが固定で引く「ループ章」の章番号。
    public const int LoopChapter = 4;

    // 他ジョブ潜行中か（＝ミナ本編の代わりに専用ストーリーを流すか）。3ステージと各ボスが読む唯一の判定。
    public static bool DiveActive(GameManager? game) => game != null && game.SelectedJob != Job.Tank;

    // 道中ビートの台詞（キャラ×章×ビート）。未執筆はプレースホルダ（現在は全アーム執筆済み＝到達しない）。
    public static (int who, string text, string face)[] Lines(Job job, int chapter, Beat beat)
    {
        var lines = Table(job, chapter, beat);
        bool placeholder = lines == null;
        lines ??= Placeholder(job, chapter, beat);
        GD.Print($"[CharStory] {Jobs.Get(job).CharacterId} ch{chapter} {beat} lines={lines.Length}{(placeholder ? " (placeholder)" : "")}");
        return lines;
    }

    // 改心相当シーン（キャラ×潜った面の9通り）。ボスの改心かけあい（ミナ前提）の代替。
    public static (int who, string text, string face)[] Redemption(Job job, string stageId)
    {
        var lines = RedemptionTable(job, stageId);
        bool placeholder = lines == null;
        lines ??= RedemptionPlaceholder(job, stageId);
        GD.Print($"[CharStory] {Jobs.Get(job).CharacterId} redemption@{stageId} lines={lines.Length}{(placeholder ? " (placeholder)" : "")}");
        return lines;
    }

    // 改心相当シーンの「ここでBGM停止」行（0始まり）。doc 第2部のト書きの位置＝この行の表示で
    //   BGM を落とし、以降の決定打を無音のまま置く（本編の BgmStopLine / SilenceAtLine と同じ流儀）。
    //   -1＝無し（未執筆アームの保険）。読み手は各 Boss*.ShowLine。
    public static int RedemptionSilenceAt(Job job, string stageId) => (job, stageId) switch
    {
        (Job.Melee, "akari") => 11,  // R1「——傘、さして帰ろ。二本あるんだし。」
        (Job.Melee, "koharu") => 9,  // R2「止まってごらん。……ほら。……世界、終わった?」
        (Job.Melee, "rei") => 11,    // R3「…………中の人なんて、いません。……って、言うことに、なってるの。」
        (Job.Heal, "akari") => 9,    // R4「……返事が、なくても……?」
        (Job.Heal, "koharu") => 8,   // R5「我に返ってごらん。……ここに、あたしがいるから。」
        (Job.Heal, "rei") => 9,      // R6「その七人の中に、あたしもいるよ。」
        (Job.Magic, "akari") => 9,   // R7「……三年……たった一行で……?」
        (Job.Magic, "koharu") => 10, // R8「……ねえ。わたしたちが「またね」って言うとき、……」
        (Job.Magic, "rei") => 6,     // R9「…………ごめん。」（謝罪を判決より先に置く）
        _ => -1,
    };

    // ── face 定数（doc 第1部8節。すべて実在ファイル）──
    private const string AFace = "res://char/v3/akari_face.png";        // あかり平常
    private const string ACry = "res://char/v3/akari_face_cry.png";     // あかり泣き＝あふれるわたしの崩れ
    private const string KFace = "res://char/v3/koharu_face.png";       // こはる平常＝明るい顔
    private const string KPale = "res://char/v3/koharu_face_pale.png";  // こはる蒼白＝絶望の担当（泣き顔素材は無い）
    private const string RFace = "res://char/v3/rei_face.png";          // レイ中の人・平常
    private const string RSmile = "res://char/v3/rei_face_smile.png";   // レイ中の人・配信用の笑顔
    private const string RCry = "res://char/v3/rei_face_cry.png";       // レイ中の人・泣き
    private const string RGawa = "res://char/v3/rei_gawa.png";          // ガワ。笑顔固定・泣き顔は存在しない

    // ═══════════════════════════════════════════════════════════════════
    // A. あかり（灯し手）——章ビート（面非依存）
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════ あかり 章1（初回ダイブ）＝土曜の相談を控えた週 ═══════════

    // 章1・出撃時
    private static readonly (int who, string text, string face)[] AkariCh1Intro =
    {
        (6, "……つながってる、これ? ……ま、いいや。つながってるつもりで話すから。", AFace),
        (6, "あたし、金曜まで総務。土曜は……予定がある人。ふふ、予定。久しぶりに言った。", AFace),   // 予定＝転職相談。中身は言わない
        (6, "行こっか。……あ、今の、取り消さない。行こう。", AFace),
    };

    // 章1・道中の節目1
    private static readonly (int who, string text, string face)[] AkariCh1Mid1 =
    {
        (6, "飛んでくるの、ぜんぶ言葉かあ。……送信ボタンって、こんなに押されてるんだ。", AFace),
        (6, "あたしのは、十二回押して、十二回、引っ込めたけど。……ここの誰かのこと、言えないね。", AFace),
        (6, "……よし。今日のノルマ。ひとつだけ、引っ込めない。", AFace),
    };

    // 章1・道中の節目2
    private static readonly (int who, string text, string face)[] AkariCh1Mid2 =
    {
        (6, "スマホのメモにね、下書きが一個だけ、残ってるの。消そうとして、消せなかったやつ。", AFace),   // 「……好きだよ。いまも。」の一行。中身は最後まで言わない
        (6, "……いまも消してない。お守りって呼んでる。効能は、不明。", AFace),
    };

    // 章1・道中の節目3
    private static readonly (int who, string text, string face)[] AkariCh1Mid3 =
    {
        (4, "「土曜日　15:00　のご予約を承っています。」", ""),   // リマインド通知
        (6, "はじめての人と、話す練習。……相手、プロだから大丈夫だよね。うん。", AFace),
        (6, "……変なの。飛んでくる言葉は、こんなによけられるのに。", AFace),
    };

    // 章1・ボス前
    private static readonly (int who, string text, string face)[] AkariCh1PreBoss =
    {
        (6, "……ん。いま、聞こえた。……ううん、ずっと聞こえてたんだ、これ。", AFace),   // 言いかけて、訂正して、言い切る（あかりの構文）
        (6, "だいじょうぶ。あたし、聞くのは得意。八年、聞く側だったから。", AFace),
        (6, "言うのは……まだ、練習中だけど。——行くよ。", AFace),
    };

    // 章1・帰還時
    private static readonly (int who, string text, string face)[] AkariCh1Return =
    {
        (6, "ただいま。……って、家じゃないか、ここ。", AFace),
        (4, "「傘、買った。八百円。……ビニールじゃないやつ。」", ""),   // 心象世界の八百円の傘（StageAkari）と無言で照応。説明しない
        (6, "土曜、行ってくる。……これも、取り消さない。予約番号、もう覚えちゃったし。", AFace),
    };

    // ═══════════ あかり 章2（2回目）＝相談後。口で言う練習 ═══════════

    // 章2・出撃時
    private static readonly (int who, string text, string face)[] AkariCh2Intro =
    {
        (6, "土曜ね、行ってきた。はじめての場所。……第一声、「あの」って言ったきり、七秒止まった。", AFace),
        (6, "でも、ぜんぶ言えた。言いたかったこと、最後まで。……プロってすごいね。待っててくれるの。", AFace),
        (6, "……さ。今日も行こ。", AFace),
    };

    // 章2・道中の節目1
    private static readonly (int who, string text, string face)[] AkariCh2Mid1 =
    {
        (6, "会社でね、ひとつ、口で言ってみたの。「それ、明日でもいいですか」って。", AFace),
        (6, "……言えた。世界、終わらなかった。……「いいよ」だって。拍子抜け。", AFace),
        (6, "八年分の下書き、なんだったんだろね。……ちがうか。あれ、ぜんぶ、練習だったんだ。", AFace),   // 数える自己ツッコミはこはる専有＝言い切りで放す
    };

    // 章2・道中の節目2
    private static readonly (int who, string text, string face)[] AkariCh2Mid2 =
    {
        (6, "そういえば。向かいの席に、人が来るんだって。中途の人。", AFace),
        (6, "……モニタ、また点くんだ。あの席の。", AFace),
        (6, "あたしの初日はね、向かいの人が先に喋ってくれたの。……変な挨拶だったな。うん、覚えてる。", AFace),   // AkariStoryFilm「今度、俺が間違えたら頼むから」。引用はしない
    };

    // 章2・道中の節目3
    private static readonly (int who, string text, string face)[] AkariCh2Mid3 =
    {
        (6, "「ようこそ」……は、変か。「はじめまして」……字面が固いな。", AFace),
        (6, "……ちがうちがう。チャットで打とうとしてる、あたし。口だよ、口。", AFace),
        (6, "口で言うのの、いいとこ、教えてあげる。——取り消せないの。", AFace),   // 章2のテーゼ
    };

    // 章2・ボス前
    private static readonly (int who, string text, string face)[] AkariCh2PreBoss =
    {
        (6, "……この先にいる人。あたしみたいに、練習中なのかな。それとも、練習の前かな。", AFace),
        (6, "どっちでも、いいや。……聞いてから、決める。", AFace),
        (6, "行くよ。今日は、声、出てるほうのあたしだから。", AFace),
    };

    // 章2・帰還時
    private static readonly (int who, string text, string face)[] AkariCh2Return =
    {
        (4, "「あしたの練習：おはようございます、を、目を見て。」", ""),
        (6, "……われながら、練習の粒が細かい。総務だからね。備品も一個ずつ数えるの。", AFace),
        (6, "じゃ、また。……あ。この「また」も、取り消さないでおく。", AFace),
    };

    // ═══════════ あかり 章3（3回目）＝話しかけた ═══════════

    // 章3・出撃時
    private static readonly (int who, string text, string face)[] AkariCh3Intro =
    {
        (6, "報告があります。……向かいの席の人に、話しかけました。自分から。", AFace),
        (6, "声がちっちゃすぎて、「え?」って聞き返されたけど。", AFace),
        (6, "……前のあたしなら、そこで「なんでもないです」だった。——もう一回、言った。届く声で。", AFace),
        (6, "以上、報告おわり。……ふふ。さ、行こ。", AFace),
    };

    // 章3・道中の節目1
    private static readonly (int who, string text, string face)[] AkariCh3Mid1 =
    {
        (6, "なんて話しかけたか、って? ……「お昼、どの辺で食べてます?」。", AFace),
        (6, "われながら、しょぼい。……でもね、続いたの。会話。三往復も。", AFace),
        (6, "三往復も。……うん。これは、数えて、いい数字。", AFace),   // 「あ、また数えてる」の自己ツッコミはこはる専有（P2）
    };

    // 章3・道中の節目2
    private static readonly (int who, string text, string face)[] AkariCh3Mid2 =
    {
        (6, "お守りの下書きね。……まだ、ある。消してない。", AFace),
        (6, "でも、最近、開いてない。……お守りって、そういうものよね。鞄の底で、いてくれれば。", AFace),
    };

    // 章3・道中の節目3
    private static readonly (int who, string text, string face)[] AkariCh3Mid3 =
    {
        (6, "きのう、雨だった。八百円の傘、初出動。", AFace),
        (6, "……雨の音、けっこう好きかも。傘があると、ぜんぜん違うんだよ。", AFace),
        (6, "帰りに、駅前で、あったかいの食べた。……冷める前に食べると、おいしいの。知ってた?", AFace),   // AkariStoryFilm Aftermath（駅前の店）の続き
    };

    // 章3・ボス前
    private static readonly (int who, string text, string face)[] AkariCh3PreBoss =
    {
        (6, "……あの声、ずっと聞いてた。言いかけて、止まるの。何回も。", AFace),   // 三体とも正典で言いさし（——）で止まる＝面非依存で嘘にならない
        (6, "止まるのはいいの。あたしも、七秒止まったし。", AFace),
        (6, "止まったまま終わるのだけ、もったいない。——行こ。", AFace),
    };

    // 章3・帰還時
    private static readonly (int who, string text, string face)[] AkariCh3Return =
    {
        (4, "「向かいの席、モニタ点いてる。……なんか、いいね。」", ""),
        (6, "明日は、あっちから話しかけてくれるかな。……どっちでもいいや。あたしから、いくし。", AFace),
        (6, "じゃあね。……この挨拶も、だいぶ板についてきたでしょ。", AFace),
    };

    // ═══════════ あかり ループ（4回目以降・軽量セット） ═══════════

    private static readonly (int who, string text, string face)[] AkariLoopIntro =
    {
        (6, "はい、あかりです。今日も定時で潜ってます。……この勤怠、どこにつければいいんだろ。", AFace),
        (6, "行こっか。いつもの。", AFace),
    };
    private static readonly (int who, string text, string face)[] AkariLoopMid1 =
    {
        (6, "総務の豆知識。備品はね、無くなる前に頼むの。……気持ちも、たぶん一緒。", AFace),
    };
    private static readonly (int who, string text, string face)[] AkariLoopMid2 =
    {
        (6, "今日の「引っ込めない」ノルマ、達成済み。朝の挨拶、目を見て言えたから。", AFace),
    };
    private static readonly (int who, string text, string face)[] AkariLoopMid3 =
    {
        (6, "傘の話、もうした? ……したか。じゃ、続きはまた雨の日に。", AFace),
    };
    private static readonly (int who, string text, string face)[] AkariLoopPreBoss =
    {
        (6, "いちばん深くまで行くよ。……聞くのは得意なの。ほんとだよ。", AFace),
    };
    private static readonly (int who, string text, string face)[] AkariLoopReturn =
    {
        (6, "おつかれさま。……うん、今日のぶんも、取り消さなかった。", AFace),
    };

    // ═══════════════════════════════════════════════════════════════════
    // B. こはる（祈り手）——章ビート（面非依存）
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════ こはる 章1（初回ダイブ）＝遭難仲間 ═══════════

    // 章1・出撃時
    private static readonly (int who, string text, string face)[] KoharuCh1Intro =
    {
        (6, "え、これ、あたしの声、飛んでってる感じ? ……テストテスト。こはるでーす。", KFace),
        (6, "……よし、入った。じゃ、行きます。予習はしてない。……予習しない自分に、まだドキドキするんだよね。", KFace),
        (6, "帰ったら数学の課題あるから、サクッとやろ。サクッと。", KFace),
    };

    // 章1・道中の節目1
    private static readonly (int who, string text, string face)[] KoharuCh1Mid1 =
    {
        (6, "隣の席の子とね、いま、同じとこで詰まってるの。数Ⅱ。", KFace),
        (6, "前は「分かんない」って言うの、負けだと思ってたけど。……二人で詰まると、遭難仲間って感じで、ちょっと楽しい。", KFace),
        (6, "……楽しいって言っちゃった。数Ⅱを。……先生に聞かせたい。", KFace),
    };

    // 章1・道中の節目2
    private static readonly (int who, string text, string face)[] KoharuCh1Mid2 =
    {
        (6, "夜はね、推しの配信、見たい日だけ見てる。……「だけ」って言えるまで、長かったんだから。", KFace),
        (6, "コメントも、たまに二行。……二行目、打つ前にちょっと深呼吸するけど。まだ。", KFace),
    };

    // 章1・道中の節目3
    private static readonly (int who, string text, string face)[] KoharuCh1Mid3 =
    {
        (6, "……あのさ。頼ってばっかで、あたし、返せてるのかなって、たまに思うの。", KFace),
        (6, "隣の子に三回聞いて、あたしが教えたの、まだ一回だし。……あ。また数えてる。", KFace),
        (6, "……やめやめ。友だちって、たぶん、割り勘じゃないし。", KFace),
    };

    // 章1・ボス前
    private static readonly (int who, string text, string face)[] KoharuCh1PreBoss =
    {
        (6, "……いるんでしょ、そこに。声、ずっとしてるもん。", KFace),
        (6, "だいじょうぶ。あたし、詰まってる人の隣に座るの、ちょっと得意になったから。", KFace),
        (6, "行くよ。ペンライトは……持ってきてないけど。気持ちだけ振っとく。", KFace),
    };

    // 章1・帰還時
    private static readonly (int who, string text, string face)[] KoharuCh1Return =
    {
        (6, "ふー。……帰ったら課題。やだー。……でも、やる。サクッと。", KFace),
        (4, "「今日の敵：数Ⅱ。二人がかりで、なんとか。」", ""),
        (6, "……じゃ、またね。あ、「またね」って、いい言葉だよね。なんか。", KFace),   // 本人は言葉の出どころを知らない。説明しない
    };

    // ═══════════ こはる 章2（2回目）＝母との会話の続き ═══════════

    // 章2・出撃時
    private static readonly (int who, string text, string face)[] KoharuCh2Intro =
    {
        (6, "きのうね、お母さんと、ちょっと話した。……模試の話じゃなくて。", KFace),
        (6, "途中で終わってた話の続き。一年ぶんくらい、ためてたやつ。", KFace),   // KoharuStoryFilm「昼休みの話は、途中で終わりました」の長距離回収
        (6, "……全部は話せてない。でも、お昼の話はした。ちゃんと最後まで。……さ、行こ!", KFace),
    };

    // 章2・道中の節目1
    private static readonly (int who, string text, string face)[] KoharuCh2Mid1 =
    {
        (6, "「学校、どう?」って聞かれて。「楽しいよ」って言って。……そこまでは、いつも通り。", KFace),
        (6, "そのあと、「でも、しんどい日もある」って言えたの。はじめて。", KFace),
        (6, "お母さん、ちょっと黙って。……「そっか」って。それだけ。……それが、よかった。", KFace),
    };

    // 章2・道中の節目2
    private static readonly (int who, string text, string face)[] KoharuCh2Mid2 =
    {
        (6, "塾はね、続けてる。……けど、「増やそうか」って言われたとき、「いまのままがいい」って言った。", KFace),
        (6, "心臓、ばくばくだったけど。……通った。希望が。", KFace),
        (6, "……「希望が通る」って、あれだね。ちょっと選挙みたいだね。", KFace),
    };

    // 章2・道中の節目3
    private static readonly (int who, string text, string face)[] KoharuCh2Mid3 =
    {
        (6, "そうだ。こないだ、推しの告知にね、「たぶん、地味だけど。わたしは、好きだから」って書いてあって。", KFace),   // ReiStoryFilm Aftermath の告知（原文一致）。片方向の照応
        (6, "いいなって思って、スクショした。……あたしも、好きなもの、好きって言お、って。", KFace),
        (6, "だから宣言します。あたし、抹茶オレは、ホット派です。……以上、宣言おわり。", KFace),
    };

    // 章2・ボス前
    private static readonly (int who, string text, string face)[] KoharuCh2PreBoss =
    {
        (6, "……すー、はー。……いまの、二行目を打つ前にやる、いつもの深呼吸。", KFace),   // 章1の癖（二行目の前の深呼吸）を所作として持ち込む
        (6, "あの声、息継ぎしてない感じだったから。……タイミングなら、あたし、ちょっと分かるんだよね。", KFace),
        (6, "行こ。……隣、座るだけでもいいし。", KFace),
    };

    // 章2・帰還時
    private static readonly (int who, string text, string face)[] KoharuCh2Return =
    {
        (4, "「宣言どおり、ホットの抹茶オレを飲みました。……冬の飲み物じゃん、とか言わない。」", ""),
        (6, "……あ、そうだ。お母さんとの話、続きの続きが、まだあるんだった。", KFace),
        (6, "帰ったら、もう一個、話してみる。……じゃ、またね!", KFace),
    };

    // ═══════════ こはる 章3（3回目）＝渡す側の一回目 ═══════════

    // 章3・出撃時
    private static readonly (int who, string text, string face)[] KoharuCh3Intro =
    {
        (6, "聞いて聞いて。きょう、隣の子に、教えたの。あたしが。数Ⅱ。", KFace),
        (6, "「先に式を書き直すと、分かりやすいよ」って。……言った瞬間、なんか、のど、あつくなった。", KFace),   // KoharuStoryFilm:14 中学時代の自分の台詞が二年ぶりに戻る
        (6, "教えるのなんて、中学ぶりだからかな。……ま、いっか。行こ!", KFace),
    };

    // 章3・道中の節目1
    private static readonly (int who, string text, string face)[] KoharuCh3Mid1 =
    {
        (6, "その子ね、「こはるに聞くと分かる」って言ってくれたの。", KFace),
        (6, "……前のあたしなら、これで一週間がんばれちゃってた。……いまは、半日ぶんにしとく。", KFace),
        (6, "残りは、ふつうに、うれしいだけ。……そのほうが長持ちするんだって。あたし調べ。", KFace),
    };

    // 章3・道中の節目2
    private static readonly (int who, string text, string face)[] KoharuCh3Mid2 =
    {
        (6, "コメントはね、いまは、ふつうに書けてる。今日あったこととか、一行。", KFace),
        (6, "読まれたかどうかは、見ない日もある。……送るとこまでが、あたしの番だから。", KFace),   // 章3のテーゼ
    };

    // 章3・道中の節目3
    private static readonly (int who, string text, string face)[] KoharuCh3Mid3 =
    {
        (6, "文化祭の実行委員……は、やりません。断りました。えらい。", KFace),
        (6, "そのかわり、看板の字だけ書く係。……字、ほめられたんだよね、こないだ。", KFace),
    };

    // 章3・ボス前
    private static readonly (int who, string text, string face)[] KoharuCh3PreBoss =
    {
        (6, "……あのさ。たぶん、あたしが行っても、すぐには変わんないと思う。", KFace),
        (6, "でも、隣に誰か座るとさ、問題って、ちょっとだけ小さく見えるんだよ。……あたし調べ、二回目。", KFace),
        (6, "行こ。", KFace),
    };

    // 章3・帰還時
    private static readonly (int who, string text, string face)[] KoharuCh3Return =
    {
        (4, "「今日も来ました。あと一行は、今日は、ないしょ。」", ""),
        (6, "……ふふ。じゃ、帰って、看板の字、練習しよっと。", KFace),
        (6, "またね! ……うん、やっぱいい言葉だ、これ。", KFace),
    };

    // ═══════════ こはる ループ（4回目以降・軽量セット） ═══════════

    private static readonly (int who, string text, string face)[] KoharuLoopIntro =
    {
        (6, "こはるでーす。……この挨拶も、何回目だろ。数えない数えない。", KFace),
        (6, "行こっか。", KFace),
    };
    private static readonly (int who, string text, string face)[] KoharuLoopMid1 =
    {
        (6, "今日の抹茶オレ情報は、ありません。……平和です。", KFace),
    };
    private static readonly (int who, string text, string face)[] KoharuLoopMid2 =
    {
        (6, "数Ⅱは、たまにまだ遭難する。でも、遭難仲間がいるから、こわくない。", KFace),
    };
    private static readonly (int who, string text, string face)[] KoharuLoopMid3 =
    {
        (6, "ペンライトの電池、換えた。……使わない日も、光るようにしとくの。", KFace),
    };
    private static readonly (int who, string text, string face)[] KoharuLoopPreBoss =
    {
        (6, "隣、座りに行くよ。いちばん詰まってる人のとこ。", KFace),
    };
    private static readonly (int who, string text, string face)[] KoharuLoopReturn =
    {
        (6, "おつかれさま! 帰って、一行書いて、寝ます。", KFace),
    };

    // ═══════════════════════════════════════════════════════════════════
    // C. レイ（語り手）——章ビート（面非依存）
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════ レイ 章1（初回ダイブ）＝「よかった」が二個 ═══════════

    // 章1・出撃時
    private static readonly (int who, string text, string face)[] ReiCh1Intro =
    {
        (6, "はいはーい、星逢レイ、入りまーす。……って、テンション、誰に向けてんのよ。ここ、無人でしょ。", RSmile),
        (6, "……いるの? そっちに。……ふうん。じゃ、続けるわ。損した気分だけど。", RFace),
        (6, "今日の予定：飛んでくるのを、祓う。以上。……台本にすると一行なのよね、これ。", RFace),
    };

    // 章1・道中の節目1
    private static readonly (int who, string text, string face)[] ReiCh1Mid1 =
    {
        (6, "企画メモ、十四件あるのよ。やりたいのに出せてないのが、十三。", RFace),
        (6, "一件は、こないだやった。……地味って言われるかと思ったら、「よかった」が、二個来た。", RFace),
        (6, "二個。……笑いなさいよ。わたしはね、二個で、次のを出す気になったの。", RFace),
    };

    // 章1・道中の節目2
    private static readonly (int who, string text, string face)[] ReiCh1Mid2 =
    {
        (6, "昼はね、レジに立ってるの。……そっちの声とこっちの声、前は、別人だったんだけど。", RFace),
        (6, "最近、混ざってきた。「いらっしゃいませ」が、ちょっと、わたしの声になってきたのよ。", RFace),
        (6, "常連のおばあちゃんに「あんた、いい声ねえ」って言われたわ。……ふふん。でしょうね。", RSmile),
    };

    // 章1・道中の節目3
    private static readonly (int who, string text, string face)[] ReiCh1Mid3 =
    {
        (6, "家ではね、まだ、配信のこと、うまく話せない。「ふーん」で終わるの。", RFace),
        (6, "……でも、こないだ、夕飯のとき「今日は何の話したの」って聞かれた。初めて。", RFace),
        (6, "「本の話」って答えたら、「ふーん」だった。……いいのよ。質問がひとつ、増えたんだから。", RFace),
    };

    // 章1・ボス前
    private static readonly (int who, string text, string face)[] ReiCh1PreBoss =
    {
        (6, "……いるわね。分かるのよ、声で。", RFace),
        (6, "初対面の人と話すのは、得意なの。三年やってるんだから。……初対面のまま終わらせないほうも、練習中。", RFace),
        (6, "行くわよ。マイク……は、ないけど。声は、ある。", RFace),
    };

    // 章1・帰還時
    private static readonly (int who, string text, string face)[] ReiCh1Return =
    {
        (4, "「次の『好きな話』枠、決めた。十四分の二。……ゆっくりやるわ。」", ""),
        (6, "……さ、帰って告知つくらなきゃ。今回はね、消さないで出すの。一発で。", RFace),
        (6, "じゃ。……見ててくれた人も、ありがと。またね。", RSmile),   // 決まり文句。ここでは回線の向こうの「あなた」へ
    };

    // ═══════════ レイ 章2（2回目）＝数字→名前 ═══════════

    // 章2・出撃時
    private static readonly (int who, string text, string face)[] ReiCh2Intro =
    {
        (6, "ねえ、聞きなさいよ。常連さんに、名前で呼びかけたの。コメント読むとき。", RFace),
        (6, "そしたら「覚えててくれたんですか」って。……三ヶ月も毎回来てて、何言ってんのよって話でしょ。", RFace),
        (6, "……覚えてなかったのは、わたしのほうなのよね。数字だと思ってたから。……はい、この話おわり。行くわよ。", RFace),
    };

    // 章2・道中の節目1
    private static readonly (int who, string text, string face)[] ReiCh2Mid1 =
    {
        (6, "同接ってね、7とか8とか、そういう数なの。うち。", RFace),
        (6, "前は「たった」って思ってた。……いまは、名前が七個って思うことにしてる。", RFace),
        (6, "七個って多いわよ。あんた、友だちの名前、パッと七人言える?", RFace),
    };

    // 章2・道中の節目2
    private static readonly (int who, string text, string face)[] ReiCh2Mid2 =
    {
        (6, "この姿はね、衣装案から自分で描いたの。星の位置も、指定どおり。……かわいいでしょ。", RSmile),
        (6, "前はこの子に、ぜんぶ任せてた。笑うのも、強がるのも。", RFace),
        (6, "いまは、半分こ。笑うのはこの子、休むのはわたし。……配分は、応相談。", RFace),
    };

    // 章2・道中の節目3
    private static readonly (int who, string text, string face)[] ReiCh2Mid3 =
    {
        (6, "きのう、コメントで「初見です」って来て。全力で歓迎したら、「三回目です」って。", RSmile),
        (6, "……紛らわしいのよ! ……でも、名前、覚えたわ。もう間違えない。", RFace),
    };

    // 章2・ボス前
    private static readonly (int who, string text, string face)[] ReiCh2PreBoss =
    {
        (6, "……あの人、ずっと声、張ってるわね。", RFace),
        (6, "声を張るのって、体力いるのよ。知ってる? わたしは知ってる。", RFace),
        (6, "行くわよ。……今日は、地声で。", RFace),
    };

    // 章2・帰還時
    private static readonly (int who, string text, string face)[] ReiCh2Return =
    {
        (4, "「今日の配信、名前を三回、呼びました。……呼ばれるの、慣れてないのは、お互いさまね。」", ""),
        (6, "……ん。今日も、閉めの挨拶までやりきったわ。えらい。自分で言うの。", RSmile),
        (6, "じゃ、またね。……あんたの名前も、そのうち聞くから。覚悟なさい。", RSmile),
    };

    // ═══════════ レイ 章3（3回目）＝疲れた日を、そのまま出す ═══════════

    // 章3・出撃時
    private static readonly (int who, string text, string face)[] ReiCh3Intro =
    {
        (6, "……今日はね。正直、ちょっと疲れてるの。", RFace),
        (6, "……なんて。そんなわけ——", RFace),   // ReiStoryFilm:30-31 の言い直しを、途中で止める
        (6, "…………いえ。訂正しない。疲れてる。そのまま行く。ゆるくやるわよ。", RFace),
    };

    // 章3・道中の節目1
    private static readonly (int who, string text, string face)[] ReiCh3Mid1 =
    {
        (6, "配信でもね、こないだ、言ったの。「今日は疲れてるから、ゆるい回」って。", RFace),
        (6, "……減らなかったわ、人。むしろ「そういう日も好き」って。", RFace),
        (6, "……何よ。先に言いなさいよね、それ。三年、聞きたかったんだから。", RFace),
    };

    // 章3・道中の節目2
    private static readonly (int who, string text, string face)[] ReiCh3Mid2 =
    {
        (6, "企画メモ、十四件だったのが、十六件になった。……減らないのよ。やると、増えるの。やりたいことって。", RFace),
        (6, "前は、たまっていくのが、こわかったんだけど。", RFace),
        (6, "いまは、冷蔵庫が埋まってる感じ。……当分、飢えないわ。", RSmile),
    };

    // 章3・道中の節目3
    private static readonly (int who, string text, string face)[] ReiCh3Mid3 =
    {
        (6, "あとね。……夕飯のとき、母が「その本、うちにまだある?」って。", RFace),
        (6, "配信、聞いてたのよ。台所で。……「ふーん」の人が。", RFace),   // 「台所の灯り」の好きな場面と無言で重なる。説明しない
        (6, "……本、渡したわ。感想は聞いてない。聞かないの。……返ってくるまでが、楽しみなんだから。", RFace),
    };

    // 章3・ボス前
    private static readonly (int who, string text, string face)[] ReiCh3PreBoss =
    {
        (6, "……聞こえる? あの声。無理してる音が、混ざってるの。", RFace),   // 面非依存（三体とも「無理してる声」は嘘にならない。「作った声」の診断はSTAGE3専用だったのでR9へ譲る）
        (6, "分かるのよ、わたし。プロだから。……無理も、本人の声のうちだってことも、ね。", RFace),
        (6, "行くわよ。挨拶は、素の声でしてくる。", RFace),
    };

    // 章3・帰還時
    private static readonly (int who, string text, string face)[] ReiCh3Return =
    {
        (4, "「今日は疲れてたので、ゆるい回でした。……また、ゆるくない日に。どっちも来てね。」", ""),
        (6, "……さて。帰って、湯船。今日は五分、長く入るの。決めたから。", RFace),
        (6, "またね。……ん? ええ、言ったわよ、今日も。ちゃんと素の声で。", RSmile),
    };

    // ═══════════ レイ ループ（4回目以降・軽量セット） ═══════════

    private static readonly (int who, string text, string face)[] ReiLoopIntro =
    {
        (6, "星逢レイ、いつもの、行くわよ。……「いつもの」がある生活って、悪くないわね。", RFace),
        (6, "ついてきなさい。", RSmile),
    };
    private static readonly (int who, string text, string face)[] ReiLoopMid1 =
    {
        (6, "今日の同接は……って、ここじゃ数えるものがないのよね。……いい傾向だわ。", RFace),
    };
    private static readonly (int who, string text, string face)[] ReiLoopMid2 =
    {
        (6, "レジのおばあちゃん、今日も来たわ。また「いい声ねえ」って。……ふふん。何回でもどうぞ。", RFace),   // ループ章は毎回同文＝固定の回数を持たせない
    };
    private static readonly (int who, string text, string face)[] ReiLoopMid3 =
    {
        (6, "冷蔵庫の企画メモ、また一件増えたの。……誰か止めなさいよ。", RSmile),
    };
    private static readonly (int who, string text, string face)[] ReiLoopPreBoss =
    {
        (6, "……さ、本番。地声で行くわ。", RFace),
    };
    private static readonly (int who, string text, string face)[] ReiLoopReturn =
    {
        (6, "おつかれさま。……またね。はい、今日も言った。", RSmile),
    };

    // ═══════════════════════════════════════════════════════════════════
    // D. 改心9本（キャラ×面。章に依存しない独立の場面）
    //   ボスの語彙は本編の実装と地続き（doc 第1部6節のアンカー行）。「消された一行を返す」は不使用。
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════ R1. あかり × STAGE1（あふれるわたし）＝自分の穢れと向き合う特別回 ═══════════
    // 過去の自分の声。倒さず、傘を持たせて連れて帰る。
    private static readonly (int who, string text, string face)[] Redeem_Akari_OnAkari =
    {
        (2, "ねえ、こっち見て。……見て、見て——", AFace),
        (6, "……見てるよ。ずっと。……ほかの誰でもなく、あたしが。", AFace),
        (2, "すきって言って。あたしも言うから。ずっと一緒。離さない。", AFace),
        (6, "……それ、言われた相手、困っちゃうんだよね。……知ってる。言いそうになった側だから。", AFace),
        (2, "だって、返事、来ないんだよ。既読はつくのに。読んだなら——返してよ。", AFace),
        (6, "うん。……来ないよ。", AFace),
        // 間。ごまかさない一言を先に置く
        (2, "……っ。なんで、そんなこと——", ACry),
        (6, "来ないまま、朝は来るの。それも、知ってる。", AFace),
        (6, "でね。……その朝に、あったかいもの食べたら、泣きそうになった。それも、あたし。", AFace),   // AkariStoryFilm Aftermath（駅前の店）
        (2, "……ずっと一緒にいてくれるなら、あたし、なんでも——", ACry),
        (6, "なんでも、しなくていい。……ひとつだけ、しよ。", AFace),
        // ここでBGM停止。決定打は無音のまま（RedemptionSilenceAt=11）
        (6, "——傘、さして帰ろ。二本あるんだし。", AFace),
        (2, "……かさ。……あたしの、どっちだっけ。", ACry),
        (6, "どっちでもいいよ。……ほんと、バカなんだから。——あたしも、だけど。", AFace),   // 口癖の再来。今回は自分に並んで言う
        (2, "……うん。……うん。", ACry),
    };

    // ═══════════ R2. あかり × STAGE2（我に返るわたし） ═══════════
    // 「みんな」の名前を問う。休む席を備品登録する。
    private static readonly (int who, string text, string face)[] Redeem_Akari_OnKoharu =
    {
        (2, "ちゃんとしなきゃ。ちゃんと、しなきゃ、だめだもん。", KFace),
        (6, "……宿題、多そうだね。手帳とか、書くタイプ?", AFace),   // 緩を一枚
        (2, "みんな見てる。……見てるもん。だから、止まれないの。", KFace),
        (6, "みんな、かあ。……ねえ。その「みんな」の名前、言える?", AFace),
        (2, "……え。", KPale),
        (6, "あたしはね、言えなかった。八年、「ちゃんとしなきゃ」って机に向かって——顔を上げたら、フロア、誰もいなかった。", AFace),
        (2, "……じゃあ、なんのために——", KPale),
        (6, "でしょ? ……その質問が出たら、もう半分、休めてるんだよ。", AFace),
        (2, "でも、止まったら……あたし、からっぽに——", KPale),
        // ここでBGM停止（RedemptionSilenceAt=9）
        (6, "止まってごらん。……ほら。……世界、終わった?", AFace),
        (2, "……終わって、ない。……しずか。", KPale),
        (6, "その静かなの、こわいやつじゃないよ。……休む席の音。ひとつ、ここに作っとこ。誰にも見せない用の。", AFace),
        (2, "……座っても、いいの? ……点数、つかない席に。", KPale),
        (6, "つかないよ。……あたし総務だから、備品登録しといたげる。「席・一名分・返却不要」。", AFace),
        (2, "……へんなの。……ふ、ふふ。", KFace),   // 蒼白→平常へ戻る
    };

    // ═══════════ R3. あかり × STAGE3（星逢レイ＝ガワ） ═══════════
    // 会社用の顔の先輩として。ガワは割らない（声だけが素に戻る）。
    private static readonly (int who, string text, string face)[] Redeem_Akari_OnRei =
    {
        (2, "初見さん、いらっしゃい! コメント、ぜんぶ読むから!", RGawa),
        (6, "……わ、いい笑顔。……プロのやつだ。", AFace),
        (2, "でしょ? ずーっと、笑ってられるよ。疲れた顔は、映らないんだから。", RGawa),
        (6, "あたしもね、持ってるの。そういう顔。会社用の。八年もので。", AFace),
        (2, "……ふうん? じゃあ、わかるでしょ。外したら、終わりだって。", RGawa),
        (6, "うん、思ってた。……一回だけ、うっかり外れちゃってね。——終わらなかった。「え、どうしたの」って言われただけだった。", AFace),   // 一回の事故として語る＝章1と同一ランでも先走らない
        (2, "……。……見てて。ちゃんと、見ててよ。", RGawa),
        (6, "見てる。笑ってるほうも……その奥で、息継ぎしてるほうも。", AFace),
        (2, "……っ、なに、それ……やだ、笑えて——笑ってるってば。", RGawa),   // 笑顔は固定のまま、声だけ崩れる（正典の挙動）
        (6, "笑ったままでいいよ。外せなんて言わない。あたしも、会社用の、捨ててないし。……便利だもんね、あれ。", AFace),
        (6, "ただ、さ。——笑ってない時間の、ごはんと、お風呂と、寝るの。そっちも、ちゃんとやったげて。中の人に。", AFace),
        // ここでBGM停止（RedemptionSilenceAt=11）
        (2, "…………中の人なんて、いません。……って、言うことに、なってるの。", RGawa),
        (6, "そっか。……じゃ、いないことにしとく。——いない人のぶん、あったかいの、置いとくね。", AFace),
        (2, "……。……ばか、ね。……いない人は、泣けないのに。", RGawa),   // ガワに泣き顔はない。台詞がそれを引き受ける
        (6, "泣くのは、笑ってない時間にできるよ。……大丈夫。あたし、こないだやったばっかりだから。詳しいの。", AFace),
    };

    // ═══════════ R4. こはる × STAGE1（あふれるわたし） ═══════════
    // 感謝を言えないまま二年助けられていた側の証言。
    private static readonly (int who, string text, string face)[] Redeem_Koharu_OnAkari =
    {
        (2, "ねえ、読んだ? 返事、まだ? 見たよね? ね?", AFace),
        (6, "うわ、既読の圧……。……大人も、これ、なるんだ。", KFace),   // 緩を一枚
        (2, "だって、十二回も送ったのに——ちがう、送れなかったのに——", AFace),
        (6, "……知ってる、それ。打って、消すやつでしょ。あたしもやってた。五十回? もっとかも。数えてない。", KFace),
        (2, "……あなたも? ……返事、来た?", AFace),
        (6, "来てない。……あたしもね、ありがとうって、ちゃんと言えてなかったの。ずっと。", KFace),
        (2, "……どういうこと。", AFace),
        (6, "毎晩ね、あたしを助けてくれてた人がいるの。その人、あたしがコメント書かなくても、「見ててくれた人も、ありがとう」って言うの。", KFace),
        (6, "二年間、「今日も来ました」しか書けなかった。……助けられてたこと、伝えたかったのに。", KFace),
        // ここでBGM停止（RedemptionSilenceAt=9）
        (2, "……返事が、なくても……?", ACry),
        (6, "うん。……お礼を言えなかった日も、声が聞けて、うれしかったよ。あたしは。", KFace),
        (2, "……あたしの「すき」も……届いてるほうに、入ってるのかな……", ACry),
        (6, "……そこは、あたしには分かんない。でも、好きだったことまで、消さなくていいんじゃないかな。", KFace),
        (2, "……好きだったのは、ほんと。……返事がなくても、それは、ほんとだもん。", ACry),
        (6, "うん。……今日は画面、閉じよっか。その気持ちまで、消えるわけじゃないから。", KFace),
    };

    // ═══════════ R5. こはる × STAGE2（我に返るわたし）＝自分の穢れと向き合う特別回 ═══════════
    // 「鏡、見ちゃうから」に、鏡の側から答える。
    private static readonly (int who, string text, string face)[] Redeem_Koharu_OnKoharu =
    {
        (2, "……うごかないで。いま、鏡、見ちゃうから。", KPale),   // 正典の行（BossKoharu:545）から開く
        (6, "…………見て、いいよ。", KFace),
        (2, "……え。", KPale),
        (6, "あたしが、あんたの鏡だもん。……ほら。", KFace),
        (2, "……やだ。だって、画面消えたら……ペンライト持って、ひとりで——", KPale),   // 黒い画面の像（正典）
        (6, "うん。映ってた。真っ黒い画面に、ひとりで。……ひどい顔って、思ったよね。", KFace),
        (6, "……あれね、画面が黒かっただけ。電気つけたら、ふつうの顔だったよ。", KFace),
        (2, "……でも、止まったら、我に返る。我に返ったら、あたし、なんにも——", KPale),
        // ここでBGM停止（RedemptionSilenceAt=8）
        (6, "我に返ってごらん。……ここに、あたしがいるから。", KFace),
        (2, "…………。", KPale),
        (6, "……ね。なんにも、なくなかったでしょ。", KFace),
        (2, "……宿題と、模試と、箱が、ある……", KPale),   // 絶望の棚卸しが、そのまま緩になる
        (6, "あるねー。……いっこずつやろ。箱はまず、開けよ。中身、好きで買ったやつじゃん。", KFace),
        (2, "……うん。……ねえ。……楽しかったのは、ほんとだよ。", KFace),   // 蒼白→平常。正典「楽しいの。ほんとに」の癒えた形
        (6, "知ってる。——あたしだもん。", KFace),
    };

    // ═══════════ R6. こはる × STAGE3（星逢レイ＝ガワ） ═══════════
    // 七人の視聴者の一人が名乗り、送った続きの一行（KoharuStoryFilm Aftermath）が読まれていたと初めて知る。
    //   正典はミナに読まれたかを観測させないまま開けている（「向こう側ですので」）＝ここで閉じる。
    // ※レイは「こはる」個人を知らないまま（正典の片方向を保つ）。ガワが知っているのは「毎日来る一行」と「一度だけ来た続き」だけ。
    private static readonly (int who, string text, string face)[] Redeem_Koharu_OnRei =
    {
        (2, "初見さん、いらっしゃい! 今日も来てくれて、ありがとう!", RGawa),
        (6, "…………初見じゃ、ないよ。二年、来てる。", KFace),
        (2, "……二年? でも、あなたとこうして話すのは、初めてよね。", RGawa),
        (6, "「今日も来ました」って、いつも書いてる。……一回だけ、続きも書いた。", KFace),   // Aftermathで送った一行。中身はここでは言わない
        (6, "読まれたかは、知らないまま。……送るとこまでが、あたしの番、だから。", KFace),   // 章3のテーゼの再来
        (2, "…………。……あれ、あなたなの。", RGawa),
        (6, "……知ってたんだ。", KFace),
        (2, "知ってるわよ。……配信つけるとね、まず、あの一行、探すの。……来てるかなって。", RGawa),
        (2, "同接は、七人。その名前の向こうで、どんな顔して聞いてるか……考えたこと、なかった。", RGawa),
        // ここでBGM停止。決定打は無音のまま（RedemptionSilenceAt=9）
        (6, "その七人の中に、あたしもいるよ。……ずっと、ここで聞いてた。", KFace),
        (2, "…………。", RGawa),
        (6, "続きの一行ね、……声でも、言っとく。——あたし、あんたの「またね」で、学校行けてたの。", KFace),   // 打った言葉を、初めて声で
        (2, "……っ。……読んだわよ、あれ。何回も。……読み上げる勇気が、なかっただけ。", RGawa),   // 笑顔は固定のまま、声だけ崩れる。BossRei「ぜんぶ、読んだから」の地続き
        (6, "……なにそれ。……お互い、言わなさすぎでしょ。……ペンライト、振っていい? 配信中でしょ、ここ。", KFace),
        (2, "……うん。……いま、初めて、客席が見えた。", RGawa),
    };

    // ═══════════ R7. レイ × STAGE1（あふれるわたし） ═══════════
    // 知らない人の一行で三年生きた側の証言。宛先は変えていい。
    private static readonly (int who, string text, string face)[] Redeem_Rei_OnAkari =
    {
        (2, "ねえ、こっち見て。すきって言って。あたしも言うから。", AFace),
        (6, "見てるわよ。ちゃんと。……「すき」は言わない。安売りしない主義なの、わたし。", RFace),   // 緩を一枚
        (2, "……なんで。言うだけなら、タダじゃない。", AFace),
        (6, "タダじゃないから、取り消したんでしょ。……そこら中の封筒、数えたわよ、さっき。十二。", RFace),   // 集計（ミナの専有）ではなく、舞台に見えている封筒（弾幕＝akari_envelope）を数える
        (2, "……っ。……だって、送ったら、重いって思われて、既読のまま——", ACry),
        (6, "……ねえ。ひとつ、わたしの話をするわ。三年前、知らない人がね、わたしに一行くれたの。", RFace),
        (6, "「今日、誰とも話してなかった。声聞けてよかった」。……それだけ。返事する暇もなく、その人、もういなかった。", RFace),   // ReiStoryFilm:19 の一行
        (2, "……返事、できなかったの? あなたも。", AFace),
        (6, "できてない。いまも。……でもね、わたし、その一行で、三年やってるの。", RFace),
        // ここでBGM停止（RedemptionSilenceAt=9）
        (2, "……三年……たった一行で……?", ACry),
        (6, "そうよ。言葉ってね、届いたあと、送った人の知らないところで、勝手に働くの。——取り消したぶんだって、書いた人の中には残るわ。……あんた、まだ全文、言えるでしょ。", RFace),
        (2, "……言える。……一通目から、ぜんぶ……", ACry),
        (6, "でしょうね。……なら、次の一通は、外に出しなさい。宛先は、変えてもいいから。", RFace),
        (2, "……うん。……ねえ。……あなたの声、聞けて、よかった。", ACry),   // 三年前の一行と同じ形が、いま自分に返る
        (6, "…………。……っ、はい、今日はここまで! ……またね。", RSmile),   // 泣きそうなのを配信の締めでごまかす。言わせない
    };

    // ═══════════ R8. レイ × STAGE2（我に返るわたし） ═══════════
    // 推し本人が視聴者の部屋に立つ。「またね」の作者による定義。
    // ※レイは部屋の主が誰かを知らないまま（正典の片方向を保つ）。
    private static readonly (int who, string text, string face)[] Redeem_Rei_OnKoharu =
    {
        (2, "……見てるもん。ちゃんと見てる。アーカイブも、ぜんぶ、ちゃんと——", KPale),
        (6, "……この部屋。……そのペンライト。……うちは、グッズなんて出してないのに。", RFace),   // 同接7の現実と整合。公式グッズのない推しのために買った市販のペンライト
        (2, "……え。……え、うそ、その髪飾り——なんで、本物——", KPale),
        (2, "やだ、見ないで! 部屋、散らかってるし、あたし、いま、ひどい顔——", KPale),   // 推しに病みを見られる、最悪の形
        (6, "お邪魔してるのは、こっちよ。……で? 「ぜんぶ見なきゃ」って、聞こえたけど。", RFace),
        (2, "……だって。ちゃんと見て、ちゃんと応援しないと……ファンでいる資格、ないもん。", KPale),
        (6, "……資格。……へえ。うちの配信、いつから資格制になったの。わたし、運営なんだけど。", RFace),   // 勝ち気の緩
        (2, "でも……休んだら、数字、減るでしょ。……悲しいでしょ。", KPale),
        (6, "……悲しいわよ。数字はね。……でもそれは、わたしの宿題。あんたの宿題は、別にあるでしょ。机の上に。", RFace),
        (2, "……画面消えたら、あたし、なんにも、なくなっちゃうのに……", KPale),
        // ここでBGM停止（RedemptionSilenceAt=10）
        (6, "……ねえ。わたしたちが「またね」って言うとき、何を祈ってるか、教えてあげる。", RFace),
        (6, "ごはん食べて、寝て、学校行って——それで、また来たい日が来たら、来て。……それだけなの。「また」に、日付はないのよ。", RFace),
        (2, "……見ない日も……ファン、でいい……?", KPale),
        (6, "当然でしょ。ペンライトは、消えてるときも、ペンライトなんだから。", RFace),
        (2, "……っ、ふ……。……推しに、部屋、見られた……最悪で、最高……。", KFace),   // 蒼白→平常。涙は言わせない
    };

    // ═══════════ R9. レイ × STAGE3（星逢レイ＝ガワ）＝自分の穢れと向き合う特別回 ═══════════
    // 作った人と作られた子。ガワは割らない。泣く係と笑う係の再契約。
    private static readonly (int who, string text, string face)[] Redeem_Rei_OnRei =
    {
        (2, "はじめまして! 星逢レイです。今日も来てくれて、ありがとう!", RGawa),
        (6, "……「はじめまして」は、ないでしょ。……その挨拶、考えたの、わたしよ。", RFace),
        (2, "……ああ。……作った人だ。", RGawa),
        (2, "ねえ、見てた? わたし、ちゃんと笑えてた? 切り抜かれてない? 減ってない?", RGawa),   // 作られた子が、作った人に採点を求める
        (6, "……ずっと見てたわよ。鏡より長く。", RFace),
        (2, "じゃあ、点数つけて。今日の笑顔、何点? 明日も、これでいい? ねえ——", RGawa),
        // ここでBGM停止。謝罪を判決より先に置く（RedemptionSilenceAt=6）
        (6, "…………ごめん。", RFace),
        (2, "……え?", RGawa),
        (6, "笑うのも、強がるのも、営業も、ぜんぶあんたに投げて——泣く係だけ、誰にも振ってなかった。", RFace),
        (2, "……泣く係……。わたし、泣けないよ。この顔、そういうふうに、描かれてないもん。", RGawa),   // ガワに泣き顔はない（正典）を、本人が言う
        (6, "知ってる。わたしが発注したんだから。……だから、決めた。——泣くのは、わたしがやる。あんたは、笑ってて。それでやっと、二人で一人前でしょ。", RFace),
        (2, "……いいの? ……作りものが、隣で。", RGawa),
        (6, "作りものなもんですか。……衣装案、何枚描いたと思ってんのよ。あんたは、わたしの、一番いい仕事。", RFace),
        (6, "……この子で、いっぱい話そう。——ね、相棒。", RCry),   // 正典の日常語の再来（ReiStoryFilm:28）。泣き顔で「笑ってて」を引き受ける
        (2, "……うん。……任せなさい!", RGawa),
    };

    // ───────────────────────────────────────────────────────────
    // 道中ビートの引き当て（キャラ×章×ビート）。章は 1〜3＋LoopChapter(4)。
    // ───────────────────────────────────────────────────────────
    private static (int who, string text, string face)[]? Table(Job job, int chapter, Beat beat) => (job, chapter, beat) switch
    {
        // ── あかり（灯し手）──
        (Job.Melee, 1, Beat.Sortie) => AkariCh1Intro,
        (Job.Melee, 1, Beat.Mid1) => AkariCh1Mid1,
        (Job.Melee, 1, Beat.Mid2) => AkariCh1Mid2,
        (Job.Melee, 1, Beat.Mid3) => AkariCh1Mid3,
        (Job.Melee, 1, Beat.PreBoss) => AkariCh1PreBoss,
        (Job.Melee, 1, Beat.Return) => AkariCh1Return,
        (Job.Melee, 2, Beat.Sortie) => AkariCh2Intro,
        (Job.Melee, 2, Beat.Mid1) => AkariCh2Mid1,
        (Job.Melee, 2, Beat.Mid2) => AkariCh2Mid2,
        (Job.Melee, 2, Beat.Mid3) => AkariCh2Mid3,
        (Job.Melee, 2, Beat.PreBoss) => AkariCh2PreBoss,
        (Job.Melee, 2, Beat.Return) => AkariCh2Return,
        (Job.Melee, 3, Beat.Sortie) => AkariCh3Intro,
        (Job.Melee, 3, Beat.Mid1) => AkariCh3Mid1,
        (Job.Melee, 3, Beat.Mid2) => AkariCh3Mid2,
        (Job.Melee, 3, Beat.Mid3) => AkariCh3Mid3,
        (Job.Melee, 3, Beat.PreBoss) => AkariCh3PreBoss,
        (Job.Melee, 3, Beat.Return) => AkariCh3Return,
        (Job.Melee, LoopChapter, Beat.Sortie) => AkariLoopIntro,
        (Job.Melee, LoopChapter, Beat.Mid1) => AkariLoopMid1,
        (Job.Melee, LoopChapter, Beat.Mid2) => AkariLoopMid2,
        (Job.Melee, LoopChapter, Beat.Mid3) => AkariLoopMid3,
        (Job.Melee, LoopChapter, Beat.PreBoss) => AkariLoopPreBoss,
        (Job.Melee, LoopChapter, Beat.Return) => AkariLoopReturn,
        // ── こはる（祈り手）──
        (Job.Heal, 1, Beat.Sortie) => KoharuCh1Intro,
        (Job.Heal, 1, Beat.Mid1) => KoharuCh1Mid1,
        (Job.Heal, 1, Beat.Mid2) => KoharuCh1Mid2,
        (Job.Heal, 1, Beat.Mid3) => KoharuCh1Mid3,
        (Job.Heal, 1, Beat.PreBoss) => KoharuCh1PreBoss,
        (Job.Heal, 1, Beat.Return) => KoharuCh1Return,
        (Job.Heal, 2, Beat.Sortie) => KoharuCh2Intro,
        (Job.Heal, 2, Beat.Mid1) => KoharuCh2Mid1,
        (Job.Heal, 2, Beat.Mid2) => KoharuCh2Mid2,
        (Job.Heal, 2, Beat.Mid3) => KoharuCh2Mid3,
        (Job.Heal, 2, Beat.PreBoss) => KoharuCh2PreBoss,
        (Job.Heal, 2, Beat.Return) => KoharuCh2Return,
        (Job.Heal, 3, Beat.Sortie) => KoharuCh3Intro,
        (Job.Heal, 3, Beat.Mid1) => KoharuCh3Mid1,
        (Job.Heal, 3, Beat.Mid2) => KoharuCh3Mid2,
        (Job.Heal, 3, Beat.Mid3) => KoharuCh3Mid3,
        (Job.Heal, 3, Beat.PreBoss) => KoharuCh3PreBoss,
        (Job.Heal, 3, Beat.Return) => KoharuCh3Return,
        (Job.Heal, LoopChapter, Beat.Sortie) => KoharuLoopIntro,
        (Job.Heal, LoopChapter, Beat.Mid1) => KoharuLoopMid1,
        (Job.Heal, LoopChapter, Beat.Mid2) => KoharuLoopMid2,
        (Job.Heal, LoopChapter, Beat.Mid3) => KoharuLoopMid3,
        (Job.Heal, LoopChapter, Beat.PreBoss) => KoharuLoopPreBoss,
        (Job.Heal, LoopChapter, Beat.Return) => KoharuLoopReturn,
        // ── レイ（語り手）──
        (Job.Magic, 1, Beat.Sortie) => ReiCh1Intro,
        (Job.Magic, 1, Beat.Mid1) => ReiCh1Mid1,
        (Job.Magic, 1, Beat.Mid2) => ReiCh1Mid2,
        (Job.Magic, 1, Beat.Mid3) => ReiCh1Mid3,
        (Job.Magic, 1, Beat.PreBoss) => ReiCh1PreBoss,
        (Job.Magic, 1, Beat.Return) => ReiCh1Return,
        (Job.Magic, 2, Beat.Sortie) => ReiCh2Intro,
        (Job.Magic, 2, Beat.Mid1) => ReiCh2Mid1,
        (Job.Magic, 2, Beat.Mid2) => ReiCh2Mid2,
        (Job.Magic, 2, Beat.Mid3) => ReiCh2Mid3,
        (Job.Magic, 2, Beat.PreBoss) => ReiCh2PreBoss,
        (Job.Magic, 2, Beat.Return) => ReiCh2Return,
        (Job.Magic, 3, Beat.Sortie) => ReiCh3Intro,
        (Job.Magic, 3, Beat.Mid1) => ReiCh3Mid1,
        (Job.Magic, 3, Beat.Mid2) => ReiCh3Mid2,
        (Job.Magic, 3, Beat.Mid3) => ReiCh3Mid3,
        (Job.Magic, 3, Beat.PreBoss) => ReiCh3PreBoss,
        (Job.Magic, 3, Beat.Return) => ReiCh3Return,
        (Job.Magic, LoopChapter, Beat.Sortie) => ReiLoopIntro,
        (Job.Magic, LoopChapter, Beat.Mid1) => ReiLoopMid1,
        (Job.Magic, LoopChapter, Beat.Mid2) => ReiLoopMid2,
        (Job.Magic, LoopChapter, Beat.Mid3) => ReiLoopMid3,
        (Job.Magic, LoopChapter, Beat.PreBoss) => ReiLoopPreBoss,
        (Job.Magic, LoopChapter, Beat.Return) => ReiLoopReturn,
        _ => null,   // 想定外（結び手・範囲外の章）＝プレースホルダの保険へ
    };

    // ───────────────────────────────────────────────────────────
    // 改心相当シーンの引き当て（キャラ×潜った面の9通り）。
    // ───────────────────────────────────────────────────────────
    private static (int who, string text, string face)[]? RedemptionTable(Job job, string stageId) => (job, stageId) switch
    {
        (Job.Melee, "akari") => Redeem_Akari_OnAkari,
        (Job.Melee, "koharu") => Redeem_Akari_OnKoharu,
        (Job.Melee, "rei") => Redeem_Akari_OnRei,
        (Job.Heal, "akari") => Redeem_Koharu_OnAkari,
        (Job.Heal, "koharu") => Redeem_Koharu_OnKoharu,
        (Job.Heal, "rei") => Redeem_Koharu_OnRei,
        (Job.Magic, "akari") => Redeem_Rei_OnAkari,
        (Job.Magic, "koharu") => Redeem_Rei_OnKoharu,
        (Job.Magic, "rei") => Redeem_Rei_OnRei,
        _ => null,   // 想定外＝プレースホルダの保険へ
    };

    // ── プレースホルダ（[仮] 接頭辞つき）。全アーム執筆済みの現在は保険＝実プレイ経路では到達しない
    //    （CompanionDialogueQa が全章×全ビート＋9通りの [仮] 不在を機械検査する）。──
    private static (int who, string text, string face) C(string text, string face = "") => (6, text, face);   // 潜行キャラ本人
    private static (int who, string text, string face) B(string text, string face = "") => (2, text, face);   // 相手ボス

    private static (int who, string text, string face)[] Placeholder(Job job, int chapter, Beat beat)
    {
        string name = Jobs.Get(job).CharacterName;
        string ch = chapter >= LoopChapter ? "ループ章" : $"第{chapter}章";
        return new[]
        {
            C($"[仮] {name}・{ch}・{BeatName(beat)}。……ここに専用ストーリーの本文が入る。"),
            C("[仮] （scenario 執筆分と差し替え。CharacterStory.Table にアームを足す）"),
        };
    }

    private static (int who, string text, string face)[] RedemptionPlaceholder(Job job, string stageId)
    {
        string name = Jobs.Get(job).CharacterName;
        return new[]
        {
            B($"[仮] {BossName(stageId)}・改心相当シーン。……相手の声が、ここに入る。"),
            C($"[仮] {name}が、自分の言葉で締める。（CharacterStory.RedemptionTable にアームを足す）"),
        };
    }

    private static string BeatName(Beat b) => b switch
    {
        Beat.Sortie => "出撃",
        Beat.Mid1 => "道中1",
        Beat.Mid2 => "道中2",
        Beat.Mid3 => "道中3",
        Beat.PreBoss => "ボス前",
        _ => "帰還",
    };

    private static string BossName(string stageId) => stageId switch
    {
        "akari" => "あかり",
        "koharu" => "こはる",
        _ => "レイ",
    };
}
