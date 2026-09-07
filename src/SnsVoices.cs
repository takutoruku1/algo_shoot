// SnsVoices : ゲーム内 SNS(X) に流れる「他人」のアカウント表（表示名・@ハンドル・アイコン）。
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
// アイコン（char/v3/icons/mob_XX.png）は「人の顔ではない」SNS でよくある種類（猫・犬・観葉植物・
//   コーヒー・空・海・食べ物・幾何模様・本・カメラ・自転車・月）。画風は v3 のアニメ塗り2段・黒線なし。
//   12種を20人で分け合う＝同じアイコンの人が複数いるのも、実際の TL の見え方に合う。
public static class SnsVoices
{
    public readonly struct Voice
    {
        public readonly string Name;    // 表示名（そのまま出す。絵文字・記号を含む）
        public readonly string Handle;  // @ の後ろ（末尾の数字は呼び出し側が足す）
        public readonly int Icon;       // char/v3/icons/mob_{Icon:00}.png の番号（1..IconCount）
        public Voice(string name, string handle, int icon) { Name = name; Handle = handle; Icon = icon; }
    }

    public const int IconCount = 12;

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
        new("のぞみ（産休あけ）",    "nozomi_re",      4),
        // ── 学生・若手 ──
        new("Ren",                  "ren_0921",       12), // 月
        new("みなみ＊提出前＊",      "minami_ddl",     9),
        // ── 推し活 ──
        new("しおり☆現場",         "shiori_live",    7),  // ケーキ
        new("もこ@両手にペンライト", "moco2000",       12),
        // ── 創作 ──
        new("三上（原稿逃亡中）",    "mikami_draft",   9),
        new("ao_ink",               "aoink",          8),
        // ── 健康・生活 ──
        new("ひかる（減量中）",      "hikaru_diet",    11),
        new("さとみ＊低浮上＊",      "satomi_low",     1),  // 猫
        new("ねむい；；",           "nemui_zzz",      12),
        new("みかん・冬眠中",        "mikan_711",      7),
        // ── 趣味 ──
        new("いぬの散歩係",         "inu_sanpo",      2),  // 犬
        new("しお／海まで2駅",      "shio_umi",       6),  // 海
    };

    public static int Count => All.Length;

    // 添字（決定論の通し番号）→ アカウント。負数や範囲外も安全に丸める。
    public static Voice At(int i)
    {
        int n = All.Length;
        return All[((i % n) + n) % n];
    }

    // アイコンのリソースパス。番号は 1..IconCount。
    public static string IconPath(int icon) => $"res://char/v3/icons/mob_{icon:00}.png";
}
