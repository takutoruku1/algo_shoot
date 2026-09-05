using Godot;
using System.Collections.Generic;

// PostPool : 投稿文集（X風）の“層付き”文言プール。
//
// 正典: wiki/08_仮台本/09_投稿文集_X風.md（ユーザー承認済み・2026-09-05）の「言葉弾の文言リスト」。
//   文面は 09 のまま一字も変えない。実在の投稿は転載していない（09 は創作のみ）。
//
// 層（Layer）の意味は 09 のとおり：
//   Layer1 日常     … 病んでいない。遊び手が「自分の TL だ」と感じるための地。
//   Layer2 病みサイン … 一見日常だが病みサインを“ひとつだけ”含む。遊び手が「見つける」対象（10 の案A）。
//   Layer3 本人     … その面のヒロイン本人の投稿。層1・層2 と同じ語彙で書かれ、細部だけが本人を示す。
//
// テーマ（Theme）は面＝ヒロインで分ける。GameManager.Stages の並び（あかり→こはる→レイ）に対応し、
// Final はミナの内側（FINAL の悲鳴＝日常の TL を持たない）。Common は面をまたいで降る汎用の語。
//
// 使い方：
//   PostPool.Draw(theme, layer, rng)       … 1 語引く（直近履歴を避ける）。
//   PostPool.Words(theme)                  … その面のティッカー用に層混合の並びを取る（Hud の帯）。
//   PostPool.ThemeForScene(scene)          … 現在のシーンパスから面のテーマを決める。
public static class PostPool
{
    public enum Theme { Common, Akari, Koharu, Rei, Final }
    public enum Layer { L1 = 1, L2 = 2, L3 = 3 }

    // ───────── 09「言葉弾の文言リスト」（8文字以内厳守）─────────
    // ハンドルは 09 の弾の表では省かれている（チップに乗るのは本文）。現行どおり "" で置く。

    // 共通（面をまたいで降る汎用の語）。
    private static readonly string[] CommonL1 =
        { "それな", "はいはい優勝", "#拡散希望", "バズる方法教えて", "誰か見てる?" };
    private static readonly string[] CommonL2 =
        { "どうせ届かない", "消すかも" };

    // あかりの面（既読・返事・承認・雨）。
    private static readonly string[] AkariL1 =
        { "既読スルー", "雨やばい", "片想い無料", "返事まだ?", "バズりたい", "誘われてない" };
    private static readonly string[] AkariL2 =
        { "下書き12件", "送れない", "置き傘二本", "なんでもない", "返事いらない", "4:03" };
    private static readonly string[] AkariL3 =
        { "読んだ?", "いいね1", "四回目" };

    // こはるの面（推し活・期待・他人の目・むだ）。
    private static readonly string[] KoharuL1 =
        { "期待", "打って消す", "明るいね", "進路は?", "塾代", "我に返るな" };
    private static readonly string[] KoharuL2 =
        { "今日も来ました", "3:10", "ちゃんと", "楽しそうだね", "見ちゃった", "見てる時間", "静か", "開けてない箱" };
    private static readonly string[] KoharuL3 =
        { "なにしてんだろ" };

    // レイの面（配信・数字・切り抜き・気づいて）。
    private static readonly string[] ReiL1 =
        { "初見です（毎回）", "ふーん", "切り抜き待ち", "ガワだけ豪華", "低評価1", "告知3回まで", "同接4", "界隈のノリ" };
    private static readonly string[] ReiL2 =
        { "同接3大丈夫", "2:47", "今日で万", "伏せて光った", "聞かれる側", "引用されたｗ" };
    private static readonly string[] ReiL3 =
        { "三秒で閉じた" };

    // FINAL（ミナの内側）。層1＝炎上のリプライ、層2＝悲鳴、層3＝三人の言葉とミナ語。
    //   ミナ語（わたくしの、せいです／ご主人様／……アホですね）は 09 のとおり X 化せず現行維持。
    private static readonly string[] FinalL1 =
        { "偽善乙", "通報した" };
    private static readonly string[] FinalL2 =
        { "むだだよ", "どうせ無理", "もういない", "私のせい", "ひとりになる", "なんで", "全部消す", "たすけて", "辞めたい", "いいね0", "大丈夫" };
    private static readonly string[] FinalL3 =
        { "気づいて", "すき すき", "わたくしの、せいです", "ご主人様", "……アホですね" };

    // ───────── 層の引き当て ─────────
    // 面ごとに層1・層2 が引ける。層3 を持たない組み合わせ（Common）は層1 へ落とす。
    private static string[] Pool(Theme t, Layer l) => (t, l) switch
    {
        (Theme.Akari,  Layer.L1) => AkariL1,
        (Theme.Akari,  Layer.L2) => AkariL2,
        (Theme.Akari,  Layer.L3) => AkariL3,
        (Theme.Koharu, Layer.L1) => KoharuL1,
        (Theme.Koharu, Layer.L2) => KoharuL2,
        (Theme.Koharu, Layer.L3) => KoharuL3,
        (Theme.Rei,    Layer.L1) => ReiL1,
        (Theme.Rei,    Layer.L2) => ReiL2,
        (Theme.Rei,    Layer.L3) => ReiL3,
        (Theme.Final,  Layer.L1) => FinalL1,
        (Theme.Final,  Layer.L2) => FinalL2,
        (Theme.Final,  Layer.L3) => FinalL3,
        (_,            Layer.L2) => CommonL2,
        _                        => CommonL1,
    };

    // 面別プール＋共通プール（各面の層1・層2 には共通の語も混ざる。FINAL は内側なので混ぜない）。
    private static string[] PoolWithCommon(Theme t, Layer l)
    {
        var own = Pool(t, l);
        if (t == Theme.Common || t == Theme.Final || l == Layer.L3) return own;
        var com = l == Layer.L2 ? CommonL2 : CommonL1;
        var all = new string[own.Length + com.Length];
        own.CopyTo(all, 0);
        com.CopyTo(all, own.Length);
        return all;
    }

    // 直近に引いた語（テーマ×層ごと）。同じ語が続けて降ると「見つける」の手がかりが濁るので避ける。
    //   保持数はプール最小長（FINAL 層1 の 2 語）を割らない範囲で 3。
    private const int RecentCap = 3;
    private static readonly Dictionary<(Theme, Layer), List<string>> _recent = new();

    // 1 語引く。直近 RecentCap 件と重複しない語を優先し、全部使い切っていれば履歴を捨てて引き直す。
    public static string Draw(Theme theme, Layer layer, RandomNumberGenerator rng)
    {
        var pool = PoolWithCommon(theme, layer);
        if (pool.Length == 0) return "";
        var key = (theme, layer);
        if (!_recent.TryGetValue(key, out var hist)) _recent[key] = hist = new List<string>();

        // プールが履歴に埋まっているなら履歴を空けてから引く（引けない状態を作らない）。
        if (hist.Count >= pool.Length) hist.Clear();
        string w;
        // 高々 8 回で「履歴に無い語」を探し、外れたらそのまま採る（試行で詰まらせない）。
        for (int i = 0; ; i++)
        {
            w = pool[rng.RandiRange(0, pool.Length - 1)];
            if (i >= 8 || !hist.Contains(w)) break;
        }
        hist.Add(w);
        while (hist.Count > RecentCap) hist.RemoveAt(0);
        return w;
    }

    // 履歴をリセットする（ステージ入場時。前の面の履歴で最初の数枚が偏らないように）。
    public static void ResetHistory() => _recent.Clear();

    // ───────── 層の混合比（09「層の比率案」の言葉弾の行）─────────
    // あかり・レイ = 5:4:1／こはる = 3:6:1／FINAL = 3:5:2。0〜9 の目盛で層を決める。
    public static Layer RollLayer(Theme theme, RandomNumberGenerator rng)
    {
        int r = rng.RandiRange(0, 9);
        return theme switch
        {
            Theme.Koharu => r < 3 ? Layer.L1 : r < 9 ? Layer.L2 : Layer.L3,
            Theme.Final  => r < 3 ? Layer.L1 : r < 8 ? Layer.L2 : Layer.L3,
            Theme.Common => r < 7 ? Layer.L1 : Layer.L2,
            _            => r < 5 ? Layer.L1 : r < 9 ? Layer.L2 : Layer.L3,   // あかり・レイ
        };
    }

    // ───────── ティッカー（HUD 下の帯「降ってくる言葉」）─────────
    // 帯は毎フレーム全語を走査して幅を測るので、面ごとに一度だけ組んだ固定の並びを配る
    // （毎フレーム抽選しない＝帯が踊らない）。並びは決定論（面ごとに同じ帯が流れる）。
    private static readonly Dictionary<Theme, (string h, string w)[]> _ticker = new();

    public static (string h, string w)[] Words(Theme theme)
    {
        if (_ticker.TryGetValue(theme, out var cached)) return cached;
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(theme.ToString().GetHashCode() & 0x7fffffff);   // 面ごとに固定の並び
        var list = new List<string>();
        // 帯は 8 セル（現行 TickerWords と同じ密度）。層の比率は上の RollLayer に従う。
        var seen = new HashSet<string>();
        for (int i = 0; i < 8; i++)
        {
            string w = "";
            for (int t = 0; t < 12 && (w.Length == 0 || seen.Contains(w)); t++)
                w = Draw(theme, RollLayer(theme, rng), rng);
            if (w.Length == 0) continue;
            seen.Add(w);
            list.Add(w);
        }
        ResetHistory();   // 帯の抽選で本編の履歴を汚さない
        var arr = new (string h, string w)[list.Count];
        for (int i = 0; i < list.Count; i++) arr[i] = ("", list[i]);
        _ticker[theme] = arr;
        return arr;
    }

    // ───────── 面の決定 ─────────
    // GameManager.Stages の現在面（＝いま走っているシーン）でテーマを決める。
    // Stages に無いシーン（FINAL＝Mina.tscn、タイトル等）は Final / Common へ落とす。
    public static Theme ThemeForScene(string scene) => GameManager.StageIdForScene(scene) switch
    {
        "akari"  => Theme.Akari,
        "koharu" => Theme.Koharu,
        "rei"    => Theme.Rei,
        _        => scene.Contains("Mina") ? Theme.Final : Theme.Common,
    };

    // 実行中のシーンからテーマを取る（Hud / PostBullets の既定値）。
    // CurrentScene は起動直後の 1 フレームだけ null になり得るので、その場合は自ノードの
    // 所属シーン（オーナー側の SceneFilePath）を遡って拾う。
    public static Theme CurrentTheme(Node node)
    {
        var scene = node.GetTree()?.CurrentScene?.SceneFilePath;
        if (string.IsNullOrEmpty(scene))
            for (Node? n = node; n != null && string.IsNullOrEmpty(scene); n = n.GetParent())
                scene = n.SceneFilePath;
        return ThemeForScene(scene ?? "");
    }

    // ───────── 層2 が濁色チップになる語かどうか（PostBullets.MurkWords の置換）─────────
    // 09 の「層2＝濁色チップ」に読み替え、遊び手が色で「見つける」手がかりにする。
    private static HashSet<string>? _murk;
    public static bool IsMurk(string w)
    {
        if (_murk == null)
        {
            _murk = new HashSet<string>();
            foreach (var t in new[] { Theme.Common, Theme.Akari, Theme.Koharu, Theme.Rei, Theme.Final })
                foreach (var s in Pool(t, Layer.L2)) _murk.Add(s);
        }
        return _murk.Contains(w);
    }
}
