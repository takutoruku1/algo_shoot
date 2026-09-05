using Godot;

// PostBullets : 全ボス戦“共通”の「投稿弾」ambient（X投稿モチーフ＝ティッカー連動の言葉弾）。
//
// 由来：もとは StageRei だけで「ボス戦中に下を流れるコメント（Hud.TickerWords）が弾になって降ってくる」
// 演出を出していた（StageRei.Rain の wordsOnly 分岐）。これを 4 ステージ（Rei/Akari/Koharu/Mina）で
// 共通に出すため、重複していた“言葉弾を1発（or 数発）湧かせる”ロジックをここへ切り出した。
// 各ステージの Rain() がボス戦中だけ Tick() を呼ぶ＝固有のスペル/予測線/パネル弾・道中 Spawner には一切触れない。
//
// 投稿の“声”の源：PostPool（wiki/08_仮台本/09_投稿文集_X風.md の層付きプール。ユーザー承認済み・2026-09-05）。
//   面のテーマ（あかり／こはる／レイ／FINAL）を GameManager.Stages の現在面から引き、層1（日常）／
//   層2（病みサイン）／層3（本人）を 09 の比率で混ぜる。旧・共通 Hud.TickerWords 8 語の
//   ハードコードと各面の PostWords は廃止した（HUD 下のティッカーも同じプールを見る）。
//   描画は Bullet.SetWord(word, handle) の X風カード（Bullet.cs:236〜）を流用。
//
// 難易度スケール（やること2）：難しいほど“投稿数（量）”が増える。
//   ・湧き間隔：DanmakuIntervalMul（Easy ほど長い）に加え、難易度別の頻度係数 FreqMul を掛ける。
//   ・同時数  ：1 tick あたりの投稿弾本数を難易度で増やす（Lunatic は最大同時2発＝面が埋まり過ぎない範囲）。
//   X風カードは大きめ＝大量描画は重いので、同時数・頻度ともに控えめの上限に収める（描画負荷への配慮）。
public static class PostBullets
{
    // ───────── 難易度テーブル（基準＝Normal）─────────
    // 投稿弾“1発を湧かせるか判定する”基本周期。これに DanmakuIntervalMul と FreqMul を掛けて実効周期にする。
    private const double BaseInterval = 0.17;   // 旧 StageRei.Rain の 0.17（×_wordTick%7）と同オーダー。
    private const int    BaseEvery    = 7;      // 何 tick に1回 投稿弾を出すか（=平均周期 BaseInterval*BaseEvery）。

    // FreqMul：湧き“頻度”の難易度倍率（大きいほど数が増える）。Easy=少なめ〜Lunatic=多め。
    // BulletCountMul（Easy0.38〜Lunatic1.9）の progression に揃え、投稿弾“だけ”では過密にならない控えめな幅に。
    private static float FreqMul(GameManager.Diff d) => d switch
    {
        GameManager.Diff.Easy    => 0.55f,  // Easy : まばら（読みやすい・圧少なめ）
        GameManager.Diff.Hard    => 1.4f,
        GameManager.Diff.Lunatic => 1.9f,   // Lunatic : 多め（同時数上限 MaxOnScreen で画面は埋まり過ぎない）
        _                        => 1.0f,   // Normal : 基準（旧 StageRei.Rain と同オーダー）
    };

    // Salvo：1 tick で同時に出す投稿弾の本数（同時に降る“晒し”の枚数）。X風カードは大きいので上限2。
    private static int Salvo(GameManager.Diff d) => d switch
    {
        GameManager.Diff.Lunatic => 2,
        _                        => 1,
    };

    // 画面上に同時に存在できる投稿弾の上限（難易度別）。X風カードは大きく毎フレーム全弾描画＝重いので、
    // どれだけ難しくても“面が投稿弾で埋まらない／描画が破綻しない”ようハードキャップする（描画負荷への配慮）。
    private static int MaxOnScreen(GameManager.Diff d) => d switch
    {
        GameManager.Diff.Easy    => 4,
        GameManager.Diff.Hard    => 10,
        GameManager.Diff.Lunatic => 14,
        _                        => 7,      // Normal
    };

    // 各難易度の“投稿量の目安”（Normalを1.0とした相対量＝FreqMul×Salvo）：
    //   Easy 0.55 / Normal 1.0 / Hard 1.4 / Lunatic 3.8（1.9×2発）。
    //   DanmakuIntervalMul（Easy ほど周期が長い）も併さるので体感差はさらに開く。
    //   実効の同時数は MaxOnScreen（Easy4/Normal7/Hard10/Lunatic14）でも頭打ちする。

    // ボス戦中に毎フレーム呼ぶ。会話中（Hud.BubblePaused）は内部で止む。
    //   node     : 呼び元ステージ（GetNodeOrNull 用）。
    //   rng      : 呼び元の RNG（位置/言葉の抽選を呼び元のシードに乗せる）。
    //   delta    : フレーム時間。
    //   rainT    : 呼び元の周期アキュムレータ（ref）。
    //   wordTick : 呼び元の tick カウンタ（ref）。
    //   theme    : 引く面のテーマ（null なら実行中のシーンから決める＝GameManager.Stages の現在面）。
    //   fallSpeed: 落下速度（面ごとの“雨脚”に合わせる）。
    //   accent   : 投稿チップの縁光・文字色に馴染ませるステージテーマ色（null＝既定の穢れ桃）。
    //   murkAll  : プール全語を穢れ系＝濁色チップにする（こはる/ミナの悲鳴プール用）。
    //              false のときも層2（病みサイン）の語は語単位で濁す（PostPool.IsMurk）。
    public static void Tick(Node node, RandomNumberGenerator rng, double delta,
        ref double rainT, ref int wordTick,
        PostPool.Theme? theme = null, float fallSpeed = 46f,
        Color? accent = null, bool murkAll = false)
    {
        if (Hud.BubblePaused) return; // 会話中（バブル）は投稿弾を止める。

        var pool = node.GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        var game = node.GetNodeOrNull<GameManager>("/root/Game");
        var diff = game?.Difficulty ?? GameManager.Diff.Normal;

        // 実効周期＝基準 × DanmakuIntervalMul（Easy ほど長い）÷ FreqMul（難しいほど短い＝数が増える）。
        float intervalMul = game?.DanmakuIntervalMul ?? 1f;
        double period = BaseInterval * intervalMul / Mathf.Max(0.01f, FreqMul(diff));

        rainT += delta;
        if (rainT < period) return;
        rainT = 0;

        if ((++wordTick % BaseEvery) != 0) return; // BaseEvery 回に1回だけ投稿弾を出す（落下弾の代わり＝投稿弾のみ）。

        // 同時数ハードキャップ：すでに上限ぶん投稿弾が出ていればこの tick は見送る（過密／描画負荷を防ぐ）。
        int live = CountPostBullets(pool);
        int room = MaxOnScreen(diff) - live;
        if (room <= 0) return;

        var th = theme ?? PostPool.CurrentTheme(node);
        int salvo = Mathf.Min(Salvo(diff), room);
        for (int i = 0; i < salvo; i++)
            SpawnOne(pool, rng, th, fallSpeed, accent, murkAll);
    }

    // 画面上で生きている投稿弾（Word 付きの敵弾）の数。MaxOnScreen キャップ判定用。
    private static int CountPostBullets(BulletPool pool)
    {
        int n = 0;
        foreach (Node c in pool.GetChildren())
            if (c is Bullet b && b.Active && b.IsEnemy && !string.IsNullOrEmpty(b.Word)) n++;
        return n;
    }

    // 投稿弾を1発スポーン（SNSコメントチップ弾。中心の小ドットが当たり判定＝Bullet 既存仕様のまま）。
    // 語は PostPool から「面のテーマ×層」で引く（層の比率は 09 の言葉弾の行＝RollLayer）。
    private static void SpawnOne(BulletPool pool, RandomNumberGenerator rng,
        PostPool.Theme theme, float fallSpeed, Color? accent, bool murkAll)
    {
        var layer = PostPool.RollLayer(theme, rng);
        string w = PostPool.Draw(theme, layer, rng);
        if (string.IsNullOrEmpty(w)) return;
        // 落下X範囲は左右の縁まで（旧70〜314）。中央帯だけ降ると画面端（特に左端）が安置化するため崩す。
        var b = pool.Spawn(new Vector2(rng.RandfRange(24f, 360f), -8f), new Vector2(0f, fallSpeed), isEnemy: true, 3f, 1);
        b?.SetWord(w, "", accent, murkAll || PostPool.IsMurk(w));
    }
}
