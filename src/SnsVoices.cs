// SnsVoices : ゲーム内 SNS(Y) に流れる「他人」のアカウント表（表示名・@ハンドル・アイコン）。
//
// ここが単一ソース。参照は2箇所：
//   ・src/Hub.cs      … タイムラインの埋め草カード（Kind.Filler）の名前／ハンドル／アイコン
//   ・src/StageImagery.cs … 道中の背景を流れる投稿カードの名前／ハンドル
// 表示名・@ハンドル・アイコンは「同じ添字」で引く＝同じ人には毎回同じ名前と同じ顔が付く。
//
// 名前の作り（2026-09-07 ユーザー指示。「低浮上」「匿名」のような単語1つの羅列をやめる）：
//   実際の X にありそうな作りに寄せる。名前＋職種／記号や絵文字／状態の括弧書き／ローマ字・数字混じり／
//   アンダースコア囲み、を混ぜる。対象読者は20代後半〜30代後半の社会人なので、子供っぽい名前に寄せない。
//   ジャンルは散らす：社会人・学生（社会人の視界に入る範囲）・推し活・創作・健康・生活・趣味。
//   実在の人物／アカウント／企業／作品の名前は使わない。攻撃的・差別的な語も使わない。
//
// 投稿と返信をまたいで同じ人を識別できるよう、各アカウントに専用のアイコンを割り当てる。
using System.Linq;

public static class SnsVoices
{
    public readonly struct Voice
    {
        public readonly string Name;    // 表示名（そのまま出す。絵文字・記号を含む）
        public readonly string Handle;  // @ の後ろの ASCII（呼び出し側が Handles.Mob で 1 文字化かし、末尾の数字を足す）
        public readonly int Icon;       // char/v3/icons/mob_{Icon:00}.png の番号（1..IconCount）
        public Voice(string name, string handle, int icon) { Name = name; Handle = handle; Icon = icon; }
    }

    // 20組。表示名と @ハンドルは必ず対（同じ人の名前とIDが噛み合って見える）。
    public static readonly Voice[] All =
    {
        // ── 社会人（本編の対象読者＝20代後半〜30代後半。ここが表の芯）──
        new("ゆき@webデザイナー",    "yuki_design",    3),  // 観葉植物
        new("k_tanaka",             "k_tanaka",       10), // カメラ
        new("なつめ｜3年目",         "natsume_3rd",    4),  // コーヒー
        new("あお。",               "ao_00",          5),  // 夕暮れの空
        new("ハラダ（通勤2時間）",   "harada_cmt",     11), // 自転車
        new("__nao__",              "nao_x2",         8),  // 幾何模様
        new("こまつ／総務",         "komatsu_soum",   9),  // 本
        new("のぞみ（産休あけ）",    "nozomi_re",      13),
        // ── 学生・若手 ──
        new("Ren",                  "ren_0921",       12), // 月
        new("みなみ＊提出前＊",      "minami_ddl",     14),
        // ── 推し活 ──
        new("しおり☆現場",         "shiori_live",    7),  // ケーキ
        new("もこ@両手にペンライト", "moco2000",       15),
        // ── 創作 ──
        new("三上（原稿逃亡中）",    "mikami_draft",   16),
        new("ao_ink",               "aoink",          17),
        // ── 健康・生活 ──
        new("ひかる（減量中）",      "hikaru_diet",    18),
        new("さとみ＊低浮上＊",      "satomi_low",     1),  // 猫
        new("ねむい；；",           "nemui_zzz",      19),
        new("みかん・冬眠中",        "mikan_711",      20),
        // ── 趣味 ──
        new("いぬの散歩係",         "inu_sanpo",      2),  // 犬
        new("しお／海まで2駅",      "shio_umi",       6),  // 海
    };

    public static int Count => All.Length;
    public static int IconCount => All.Length;

    // 添字（決定論の通し番号）→ アカウント。負数や範囲外も安全に丸める。
    public static Voice At(int i)
    {
        int n = All.Length;
        return All[((i % n) + n) % n];
    }

    // アイコンのリソースパス。番号は 1..IconCount。
    public static string IconPath(int icon) => $"res://char/v3/icons/mob_{icon:00}.png";

    public sealed record Reply(int Author, string Text);
    public readonly record struct Reach(int Likes, int Reposts, int Views);
    public sealed record Post(string Id, int Author, int Chapter, string PreviousId, string Text, Reach Reach, Reply[] Replies,
        string CharacterId = "");

    public static Reach VoiceReach(string id) => id switch
    {
        "akari" => new(1, 0, 37),
        "koharu" => new(4, 0, 62),
        "rei" => new(29, 3, 417),
        _ => throw new System.ArgumentOutOfRangeException(nameof(id)),
    };

    public static readonly Post AkariReturnPost = new("akari_warm_meal", -1, 1, "",
        "今日は、冷める前に食べた。", new(3, 0, 41), System.Array.Empty<Reply>(), "akari");
    public static readonly Post KoharuReturnPost = new("koharu_worked_notes", -1, 2, "",
        "ここ、やっと分かった。", new(2, 0, 29), System.Array.Empty<Reply>(), "koharu");
    public static readonly Post ReiReturnPost = new("rei_book_night", -1, 3, "",
        "本の好きなところを最後まで話せた。", new(12, 1, 186), System.Array.Empty<Reply>(), "rei");
    public static readonly Post MinaReturnPost = new("mina_shared_sky", -1, 4, "",
        "今夜はこの空を選びました。おやすみなさい。", new(6, 0, 78), System.Array.Empty<Reply>(), "mina");
    public static readonly Post[] ReturnPosts = { AkariReturnPost, KoharuReturnPost, ReiReturnPost, MinaReturnPost };

    public static string ReturnPostReadKey(Post post) => "once_sns_" + post.Id;
    public static string ReturnPostStageId(Post post) => post.CharacterId == "mina" ? GameManager.FinalStageId : post.CharacterId;

    public static string AuthorName(Post post) => post.CharacterId.Length > 0
        ? Jobs.All.Single(job => job.CharacterId == post.CharacterId).CharacterName : All[post.Author].Name;

    public static string AuthorHandle(Post post) => post.CharacterId == "mina" ? Handles.Mina
        : post.CharacterId.Length > 0 ? GameManager.Stages.Single(stage => stage.Id == post.CharacterId).Handle
        : AccountHandle(post.Author);

    public static readonly Post[] Posts =
    {
        new("old_blanket", 15, 0, "", "来た！　猫が自分から膝に来た！　いつもの毛布はいま洗濯機です。", new(26, 3, 284),
            new Reply[] {
                new(17, "かわいい……この寝顔、起こせないね。"),
                new(15, "いま洗濯機が鳴った。干しに行きたい。立ちたくない。") }),
        new("last_train", 4, 0, "", "終電の一本前で帰ってきた。会社に傘を忘れたから、駅の屋根の下で雨宿り中。", new(3, 0, 46),
            new Reply[] {
                new(1, "ホームまでびしょびしょ。私ならもう一本買っちゃうなあ。"),
                new(4, "そうやって買った傘が、もう玄関に四本あるんですよ。"),
                new(1, "四本……。すみません、私でも五本目は買えない。") }),
        new("draft_reply", 2, 0, "", "『おつかれさま』のあとが長くなって、結局それだけ送った。机の上だけ片づいた。", new(4, 0, 57),
            new Reply[] {
                new(6, "机きれい！　うちもこうしたい。片づけのコツあります？"),
                new(2, "カメラをちょっと右に向ける。左は見せられません"),
                new(6, "うちも右だけならいけます。") }),
        new("read_only", 8, 0, "", "配信の最後の『またねー！』に、声出して『またねー！』って返しちゃった。コメント欄には一文字も書いてないのに。", new(11, 1, 138),
            new Reply[] {
                new(11, "えっ、私なんて毎回手も振ってるよ！"),
                new(8, "もう配信終わってるのに、今ちょっと手を振ってみた。"),
                new(11, "じゃ、こっちからも。Renさんまたねー！") }),
        new("archive_night", 11, 1, "", "配信、休憩入った！　再開までに一問解ける人、よーいどん！", new(8, 0, 92),
            new Reply[] {
                new(9, "そっち数学か！　まって私の教科書どこ"),
                new(11, "言い出した私も今ペン探してる"),
                new(9, "一問できた！"),
                new(11, "早っ！　私まだ名前しか書いてない！") }),
        new("unused_lines", 12, 1, "", "ボツ置き場から二行、本文に返り咲きました。おかえり。", new(7, 1, 73),
            new Reply[] {
                new(9, "そこ、何て名前で保存してます？　うちの『使うかも』はもう三つあります"),
                new(12, "『墓場』です。"),
                new(13, "うちのラフ置き場は『仮』。三年目の仮。") }),
        new("muted_notifications", 7, 1, "", "やった！！　餃子が十二個、くっついたままひっくり返せた！　フライパンに一個も残ってない。", new(9, 1, 109),
            new Reply[] {
                new(18, "真ん中の一個ください。"),
                new(7, "真ん中は私のです。端っこのカリカリなら一個どうぞ。") }),
        new("rain_window", 1, 1, "", "帰りの電車から撮った。今日はなんとなく窓の外を見ていたくて。動画の音は消してあるよ。", new(5, 0, 67),
            new Reply[] {
                new(4, "雨の日の車窓、好きです。これ、もう一周見ていいやつだ。"),
                new(1, "どうぞ。撮った私は見入ってひと駅乗り過ごしました。"),
                new(4, "おっと。私もいま電車なので、二周目は降りてからにします。") }),
        new("stream_room", 13, 2, "", "配信おつかれさまでした。……待って、マイクしまう今になって、いちばん話したかったこと思い出した。", new(6, 0, 82),
            new Reply[] {
                new(12, "え、ここで聞いてもいい？　何の話？"),
                new(13, "描きかけの絵のこと！　言葉で説明するより載せたほうが早いかも。"),
                new(12, "見たい。載せたら教えて！") }),
        new("penlight_drawer", 10, 2, "", "家でもう一回アンコールしたい人ー！　半券は箱へ。ライトはまだしまいません。", new(14, 2, 159),
            new Reply[] {
                new(11, "はーい！　私も紫にした！"),
                new(10, "おそろい！　サビからいくよー！"),
                new(11, "もう一本出す！　次の返信は曲のあと！") }),
        new("waiting_reply", 0, 2, "", "友だちと『せーの』で好きなパンを送ったら、二人とも揚げパン！　そこまでかぶる？", new(3, 0, 39),
            new Reply[] {
                new(5, "写真のはきなこっぽい。友だちもきなこ派？"),
                new(0, "聞いてきた。私はきなこ、向こうは砂糖。惜しい！") }),
        new("quiet_account", 16, 2, "", "夜中、いいねの欄に久しぶりのアイコンがいた。こっちからもひとつ。", new(6, 0, 74),
            new Reply[] {
                new(3, "あ、元気だったんだなってなるやつ。"),
                new(16, "うん。ちょっといい夜。") }),
        new("unfinished_color", 13, 3, "stream_room", "配信で言いそびれた絵、これ！　この青が気に入ったので今日は消しゴムの出番なし。", new(32, 4, 357),
            new Reply[] {
                new(12, "待ってた、この絵！　肩の青、最初から決めてたの？"),
                new(13, "ううん、試し塗り。いまレイヤー名を『仮』から『青』に変えたとこ。") }),
        new("two_cups", 6, 3, "", "久しぶりに会った。話すこと考えて来たのに、最初の十分はカップが熱い話をしてた。", new(4, 0, 51),
            new Reply[] {
                new(2, "どっちも熱そう。十分もその話してたんですか？"),
                new(6, "『もう持てる？』『まだ』『そっちは？』『無理』") }),
        new("late_reply", 2, 3, "draft_reply", "この前『おつかれさま』だけ送った人と電話した。最初に口から出たのは『ごはん食べた？』だった。", new(3, 0, 43),
            new Reply[] {
                new(6, "あの机の写真の日だ。長電話になりました？"),
                new(2, "七分二十秒。切ったあと、通話時間を二度見しました。") }),
        new("own_pace", 11, 3, "", "昨日は配信を途中で閉じて寝た。めちゃくちゃよく寝た！　で、噂の場面は何分何秒？　時間だけください！", new(10, 1, 123),
            new Reply[] {
                new(10, "1:12:08"),
                new(11, "見た。あの間ずるい。もう三回巻き戻した") }),
    };

    public static string AccountHandle(int author) => Handles.Mob(All[author].Handle);
}
