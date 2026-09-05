using Godot;

// StageImagery : 各ステージの「心象世界」を、汚染された SNS(X) のタイムラインが右→左へ流れる
//   ツイート風カードとしてエンジン描画で重ねる軽量レイヤー（設計書 4 / 6 の演出ト書きを SNS で翻訳）。
//   流れは ScrollFx / StageBackground と同じ左方向に統一＝左→右の前進感（縦の上昇感は出さない）。
//   Rei   : 順位晒しのタイムライン。「２位おめでとう（笑）」等の心ない投稿／「１位」は白飛びの固定ポスト。
//   Akari : 自責リプのスレッド。引用RTで「あたしのせいだ」が増幅、本人の「すき」「ごめん」が桃の差し色。雨は残す（左流れ・弱め）。
//   Koharu: 電気を消した部屋の静かな投稿。いいねは 0/1（誰も反応しない）。暗背景なのでα低め。
// 背景画像(ZIndex -90)の上、ゲーム要素(0..10)の下(ZIndex -50)に描く。浄化が進む(Warmth↑)と薄れて晴れる。
//
// 弾幕の視認性最優先：カードは「画面奥で流れ去る世間の声」で前に出さない。無彩色〜淡い同系色（弾の濃ピンク/
//   黒グリフと色相分離）、加算しない、α上限おおむね0.20（合算込み・こはるは暗背景で一段低い）、同時4枚・中央回避・
//   12px/s（ScrollFx近層より遥か遅い＝奥）。乱数は使わず Frac(Mathf.Sin(i*..)*..) の決定論パターン。
//   レーンは上下2段（画面の縦中央＝弾の主戦場を空ける）。横スクロールでも弾の視認性を確保する。
public partial class StageImagery : Node2D
{
    public enum StageKind { Rei, Akari, Koharu }
    public StageKind Kind = StageKind.Rei;

    private const float W = 384f, H = 216f;
    private FontFile _font = null!;
    private double _t;
    private double _flashT;   // Akari の記憶フラッシュ（>0 の間だけ描画）
    private float _bulletDamp = 1f; // 弾密度が高いほど背景を引く係数（致命情報を最前面の明るさに＝§3 視認性）
    private double _revT = -1;      // S3 画の反転：<0=未発火。TriggerReversal() から 0 で進行を始める
    private const float RevDur = 12f; // 反転の全尺（改心直後の会話〜帰還ビートの背景でゆっくり満ちる）

    public override void _Ready()
    {
        ZIndex = -50;
        ZAsRelative = false;
        AddToGroup("imagery");
        _font = UiKit.Zen; // 非ピクセル（滑らかゴシック）
    }

    // 雨の交差点のフラッシュを一瞬焚く（旧稿の伏線：あかりとの記憶）。
    //   専用SE（雨＋遠いクラクション＋言いかけて切れる一音）を白フラッシュと同フレームで鳴らす（音と画の同期）。
    //   案C（仮台本 06）の改心は回想ではなく「取り消されていない一通がひらく」なので、
    //   2026-09-06 に BossAkari 側の呼び出しを外した＝現在この経路の呼び手はいない。
    //   交差点の画（DrawAkari の分岐）ごと消すかどうかは、差し替えの背景演出を決めてから判断する。
    public void TriggerMemoryFlash()
    {
        _flashT = 2.4;
        Audio.Instance?.PlayMemoryFlash();
    }

    // S3 画の反転：改心成立（cry→post 遷移）の瞬間に各ボスの OnCryEnd から呼ばれる。
    //   汚染の象徴イメージ（白飛びの「１位」／自責スレッド／細る湯気）が、改心後の会話〜帰還ビートの
    //   背景で“ゆっくり”反転していく。ズームもフラッシュも鳴らさない＝プレイヤーが自分で気づく余白を残す。
    public void TriggerReversal() { if (_revT < 0) _revT = 0; }

    // 反転の局所進行：全尺 RevDur のうち t0..t1 秒の区間を 0..1 に正規化して smoothstep（ゆっくり入りゆっくり止まる）。
    private float RevPhase(float t0, float t1)
    {
        if (_revT < 0) return 0f;
        float u = Mathf.Clamp(((float)_revT - t0) / Mathf.Max(0.001f, t1 - t0), 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_flashT > 0) _flashT -= delta;
        if (_revT >= 0) _revT += delta; // S3 画の反転の進行（改心成立後）
        // 敵弾が増えるほど背景の声カードを一段引く（20発まで等倍→80発で0.45倍）。なめらかに追従。
        int bullets = GetTree().GetNodesInGroup("enemy_bullets").Count;
        float target = Mathf.Lerp(1f, 0.45f, Mathf.Clamp((bullets - 20) / 60f, 0f, 1f));
        _bulletDamp = Mathf.Lerp(_bulletDamp, target, 1f - Mathf.Exp(-6f * (float)delta));
        QueueRedraw();
    }

    private float Warmth => GetNodeOrNull<GameManager>("/root/Game")?.Warmth ?? 0f;

    public override void _Draw()
    {
        if (_font == null) return;
        float fade = (1f - Mathf.Clamp(Warmth, 0f, 1f)) * _bulletDamp; // 浄化で晴れる＋弾密度で引く
        // S3 画の反転：反転が始まったら「前」の汚染イメージは静かに引く（チェックポイント入場等で
        //   Warmth が満ちていない周回でも、反転後の象徴と旧イメージが二重写しにならないように）。
        fade *= 1f - RevPhase(0f, 4f);
        switch (Kind)
        {
            case StageKind.Rei: DrawRei(fade); break;
            case StageKind.Akari: DrawAkari(fade); break;
            case StageKind.Koharu: DrawKoharu(fade); break;
        }
        // 反転レイヤー（改心成立後のみ）。fade（汚染の濃さ）とは独立に、自前のαでゆっくり浮かぶ。
        if (_revT >= 0)
            switch (Kind)
            {
                case StageKind.Rei: DrawReiReversal(); break;
                case StageKind.Akari: DrawAkariReversal(); break;
                case StageKind.Koharu: DrawKoharuReversal(); break;
            }
    }

    // ───────── 共通：汚染SNSタイムラインの「X(旧Twitter)投稿」カード ─────────
    // 全ステージ共通の小カード。右→左へ 12px/s でループ。同時4枚・上下2段レーン（縦中央は弾の主戦場なので空ける）。
    // 本物の X 投稿の骨格に寄せる：①円アイコン＋表示名(太め)＋@ハンドル(灰)＋「· 2時間」相対時刻(中黒)＋時々青認証
    //   ②本文 ③アクション行4つ（返信/リポスト/いいね/閲覧数）を X の並び順で極小シルエット＋数字。
    private const float CardW = 156f, CardH = 52f;   // アクション行を入れる余地（奥行きは速度24px/sで担保）
    private const float ScrollSpeed = 12f;           // px/s（ScrollFx近層より遥か遅い＝奥／画面酔い対策で半減）
    private const int CardCount = 4;                 // 同時表示（控えめ）
    // 上下2段：縦中央の帯（弾の主戦場）を空ける。上段は画面上端寄り、下段は下端寄り（CardH=52）。
    private static readonly float[] LaneRows = { 8f, H - CardH - 8f }; // 上段 y=8 / 下段 y=156

    private static float Frac(float v) => v - Mathf.Floor(v);

    // 匿名アカウントの @ハンドルを決定論生成（@nanashi_3942 風）。
    private static readonly string[] Handles = { "nanashi", "mob", "no_name", "anon", "kuuki", "yajiruba", "tori398" };
    private string Handle(int i)
    {
        int s = (int)(Frac(Mathf.Sin(i * 45.3f) * 10247.7f) * Handles.Length);
        int num = 10 + (int)(Frac(Mathf.Sin(i * 91.7f) * 7351.3f) * 8900f);
        return $"@{Handles[s % Handles.Length]}_{num}";
    }

    // 表示名（@ハンドルとは別の、太め濃いめで出す日本語/英字の通り名）を決定論生成。
    private static readonly string[] Names = { "名無し", "通りすがり", "匿名", "ロム専", "外野", "観測者", "ふぉろわ" };
    private string DisplayName(int i)
    {
        int s = (int)(Frac(Mathf.Sin(i * 61.7f) * 8861.1f) * Names.Length);
        return Names[s % Names.Length];
    }

    // 相対時刻「· 2時間」等。決定論で 分/時間 を散らす（中黒「·」で区切る）。
    private string RelTime(int i)
    {
        float r = Frac(Mathf.Sin(i * 73.9f) * 4129.7f);
        if (r < 0.45f) return $"· {1 + (int)(r * 130f)}分";
        return $"· {1 + (int)((r - 0.45f) * 40f)}時間";
    }

    // 認証バッジを付けるか（時々だけ＝決定論）。
    private bool Verified(int i) => Frac(Mathf.Sin(i * 113.3f) * 2671.7f) > 0.72f;

    // 極小アイコン群（すべて点・線・三角の簡易シルエット。彩度は上げない＝text色の濃淡で描く）。
    // X のアクション行の並び：返信(吹き出し)・リポスト(二本矢印)・いいね(ハート)・閲覧数(棒グラフ)。
    private void IconReply(float x, float y, Color c)   // 吹き出し（角丸枠＋下のしっぽ）
    {
        DrawRect(new Rect2(x, y, 6f, 4f), new Color(c.R, c.G, c.B, c.A), false, 1f);
        DrawRect(new Rect2(x + 1f, y + 4f, 2f, 1f), c); // しっぽ
    }
    private void IconRepost(float x, float y, Color c)  // 二本矢印（リサイクル）＝上下の横線＋両端の縦
    {
        DrawLine(new Vector2(x, y + 1f), new Vector2(x + 6f, y + 1f), c, 1f);
        DrawLine(new Vector2(x, y + 4f), new Vector2(x + 6f, y + 4f), c, 1f);
        DrawRect(new Rect2(x, y + 1f, 1f, 3f), c);      // 左端の縦
        DrawRect(new Rect2(x + 5f, y + 1f, 1f, 3f), c); // 右端の縦
    }
    private void IconHeart(float x, float y, Color c)   // ハート＝上2点＋下三角の簡易シルエット
    {
        DrawRect(new Rect2(x, y, 2f, 2f), c);
        DrawRect(new Rect2(x + 3f, y, 2f, 2f), c);
        DrawRect(new Rect2(x + 1f, y + 2f, 3f, 1f), c);
        DrawRect(new Rect2(x + 2f, y + 3f, 1f, 1f), c);
    }
    private void IconViews(float x, float y, Color c)   // 閲覧数＝棒グラフ（高さの違う3本）
    {
        DrawRect(new Rect2(x, y + 3f, 1f, 2f), c);
        DrawRect(new Rect2(x + 2f, y + 1f, 1f, 4f), c);
        DrawRect(new Rect2(x + 4f, y, 1f, 5f), c);
    }

    // 1枚の X 投稿カードを描く。本文・名前・メタは i で決定論（周回中は固定＝チラつかない。切替は画面外）。
    //   panel : パネル基本色, text : 本文/名前色, accent : アイコン色
    //   replies/reposts/likes/views : アクション行の数字, liked : いいね済み（ハートを淡桃に）, quote : 引用リプ線（あかり）
    private void DrawCard(float x, float y, float pa, float fade, Color panel, Color text, Color accent,
                          int i, string body, int replies, int reposts, int likes, int views,
                          bool liked = false, bool quote = false)
    {
        var ci = GetCanvasItem();
        float a = pa * fade;
        // パネル（半透明・角丸）＋枠線（型を出すが主張させない）。角丸は投稿弾（Bullet.cs）や
        // ハブのカードと同じ UiKit.Box ＝「SNSの投稿カード」の語彙を画面全体で統一。α は従来のまま。
        UiKit.Box(this, new Rect2(x, y, CardW, CardH), new Color(panel.R, panel.G, panel.B, a), 5f,
            new Color(panel.R, panel.G, panel.B, a * 0.7f), 1f);

        // ① 円アイコン（左上・本物の X に寄せて真円に）＋薄い外周リング（アバターの縁取り）。
        float cx = x + 11f, cy = y + 11f;
        DrawCircle(new Vector2(cx, cy), 5f, new Color(accent.R, accent.G, accent.B, a * 1.4f));
        DrawArc(new Vector2(cx, cy), 5.5f, 0f, Mathf.Tau, 20, new Color(text.R, text.G, text.B, a * 0.9f), 1f);

        // 表示名（太め濃いめ＝1pxずらして二度描きで擬似ボールド・α高め）。
        var nameC = new Color(text.R, text.G, text.B, Mathf.Min(a * 2.6f, 0.6f));
        string name = DisplayName(i);
        var headY = new Vector2(x + 20f, y + 11f);
        _font.DrawString(ci, headY, name, HorizontalAlignment.Left, -1, 9, nameC);
        _font.DrawString(ci, headY + new Vector2(0.6f, 0f), name, HorizontalAlignment.Left, -1, 9, nameC);
        float nameW = _font.GetStringSize(name, HorizontalAlignment.Left, -1, 9).X;
        float hx = x + 20f + nameW + 2f;

        // 認証バッジ（時々）＝小さな青丸＋白チェック。彩度は控えめ・弾の色相と分離した薄青。
        if (Verified(i))
        {
            DrawCircle(new Vector2(hx + 2.5f, y + 8f), 2.5f, new Color(0.45f, 0.62f, 0.85f, a * 2.0f));
            DrawLine(new Vector2(hx + 1.3f, y + 8f), new Vector2(hx + 2.2f, y + 9f),
                new Color(0.95f, 0.97f, 1f, a * 2.2f), 1f);
            DrawLine(new Vector2(hx + 2.2f, y + 9f), new Vector2(hx + 3.6f, y + 6.8f),
                new Color(0.95f, 0.97f, 1f, a * 2.2f), 1f);
            hx += 6f;
        }

        // @ハンドル（灰色＝低α）＋「· 2時間」相対時刻（中黒区切り）。
        var grey = new Color(text.R, text.G, text.B, a * 1.25f);
        string handle = Handle(i);
        _font.DrawString(ci, new Vector2(hx, y + 11f), handle, HorizontalAlignment.Left, -1, 8, grey);
        float thx = hx + _font.GetStringSize(handle, HorizontalAlignment.Left, -1, 8).X + 2f;
        _font.DrawString(ci, new Vector2(thx, y + 11f), RelTime(i), HorizontalAlignment.Left, -1, 8, grey);

        // ② 本文（10px）。引用リプ（quote）なら左にスレッド線＋字下げ。
        float bx = x + 20f, by = y + 28f;
        if (quote)
        {
            DrawLine(new Vector2(x + 18f, y + 18f), new Vector2(x + 18f, y + 32f),
                new Color(text.R, text.G, text.B, a * 1.2f), 1f);
            bx = x + 23f;
        }
        _font.DrawString(ci, new Vector2(bx, by), body,
            HorizontalAlignment.Left, -1, 10, new Color(text.R, text.G, text.B, Mathf.Min(a * 2.4f, 0.55f)));

        // ③ アクション行（X の並び順で4つ＝返信・リポスト・いいね・閲覧数）。極小アイコン＋数字を横並び。
        float ay = y + CardH - 9f;       // アイコンの上端
        float ty = y + CardH - 3f;       // 数字のベースライン
        var meta = new Color(text.R, text.G, text.B, a * 1.15f);
        float ax = x + 20f;
        float step = (CardW - 26f) / 4f; // 4等分で横並び
        // 返信
        IconReply(ax, ay, meta);
        _font.DrawString(ci, new Vector2(ax + 8f, ty), replies.ToString(), HorizontalAlignment.Left, -1, 8, meta);
        // リポスト
        ax += step;
        IconRepost(ax, ay, meta);
        _font.DrawString(ci, new Vector2(ax + 8f, ty), reposts.ToString(), HorizontalAlignment.Left, -1, 8, meta);
        // いいね（押されていれば淡い桃＝Xのワンポイント。弾の濃ピンクと被らない範囲の低彩度桃）。
        ax += step;
        var heartC = liked ? new Color(0.78f, 0.55f, 0.62f, a * 1.7f) : meta;
        IconHeart(ax, ay, heartC);
        _font.DrawString(ci, new Vector2(ax + 8f, ty), likes.ToString(), HorizontalAlignment.Left, -1, 8,
            liked ? heartC : meta);
        // 閲覧数
        ax += step;
        IconViews(ax, ay, meta);
        _font.DrawString(ci, new Vector2(ax + 8f, ty), FmtCount(views), HorizontalAlignment.Left, -1, 8, meta);
    }

    // 閲覧数は大きくなりがちなので 1.2万 / 980 のように省略表記（X感）。
    private static string FmtCount(int n)
        => n >= 10000 ? $"{n / 1000 / 10f:0.#}万" : n.ToString();

    // アクション行の4数字＋いいね済みをまとめて運ぶ（引用線は Akari 専用パスで個別指定）。
    private struct CardMeta { public int Replies, Reposts, Likes, Views; public bool Liked; }

    // 4枚のカードのループ x を等間隔で配り、右→左へ流す。各カードは上下2段に交互配置（縦中央を空ける）。
    // #11：巻き戻し（周回）ごとに本文/アカウントのインデックスを +CardCount ずらし、bodies 8件が
    //   「4枚ずつ・順番に」全部流れる。切替は必ず画面外（右外→左外の折り返し瞬間）＝画面内でチラつかない。
    private void DrawTimeline(float fade, Color panel, Color text, Color accent,
                              string[] bodies, System.Func<int, CardMeta> meta, float panelA)
    {
        float span = W + CardW;
        for (int i = 0; i < CardCount; i++)
        {
            // 等間隔に置いた初期 x から左へ流し、画面左外へ出たら右外へ巻き戻す（横ループ）。
            // x は W→-CardW（入退場とも完全に画面外）＝巻き戻しの瞬間が見えない。
            float raw = i * (span / CardCount) + (float)(_t * ScrollSpeed);
            float x = W - raw % span;
            float y = LaneRows[i % LaneRows.Length];
            int idx = (i + (int)(raw / span) * CardCount) % bodies.Length; // 周回ごとに +CardCount
            var m = meta(idx);
            DrawCard(x, y, panelA, fade, panel, text, accent, idx, bodies[idx],
                     m.Replies, m.Reposts, m.Likes, m.Views, m.Liked, quote: false);
        }
    }

    // ---- STAGE3 レイ：順位晒しのタイムライン（Cold=青系）----
    // #11 文面改稿（maeda）：観測者の声＝ミーム的な軽さ7:刺さる一言3。個人特定なし。
    // ※4枚ずつ・周回ごとに +4 して 8 件全部が順に流れる（DrawTimeline）。刺さる枠は前半 index 2／後半に2枚。
    private static readonly string[] ReiBodies =
        { "また２位ｗ", "点数バグってて草", "１位以外、覚えてないや", "実質優勝でよくない?",
          "２位おめでとう（笑）", "努力は裏切らない（笑）", "天才って、こわ", "がんばったね、で終わる話" };
    private void DrawRei(float fade)
    {
        // 「１位」は白飛びの固定ポスト（ピン留め）。スクロールしない最上部のカード。読めない眩しさ。
        var glow = new Color(1f, 1f, 1f, 0.16f * fade);
        DrawRect(new Rect2(W / 2f - 40f, 6f, 80f, 22f), glow);
        DrawRect(new Rect2(W / 2f - 40f, 6f, 80f, 22f), new Color(1f, 1f, 1f, 0.09f * fade), false, 1f);
        _font.DrawString(GetCanvasItem(), new Vector2(W / 2f - 16f, 22f), "１位",
            HorizontalAlignment.Left, -1, 12, new Color(1f, 1f, 1f, 0.42f * fade));

        // 順位晒しのタイムライン。冷たい青、メタは不自然に多い（晒しの拡散）。
        var panel = new Color(0.80f, 0.86f, 1f);
        var text = new Color(0.82f, 0.88f, 1f);
        var accent = new Color(0.86f, 0.90f, 1f);
        DrawTimeline(fade, panel, text, accent, ReiBodies, i => new CardMeta
        {
            // 順位晒し＝拡散して数字が不自然に多い。閲覧数は万単位（晒しの伸び）。
            Replies = 12 + (int)(Frac(Mathf.Sin(i * 11.3f) * 4127.1f) * 80f),
            Reposts = 20 + (int)(Frac(Mathf.Sin(i * 29.3f) * 3317.1f) * 90f),
            Likes = 60 + (int)(Frac(Mathf.Sin(i * 17.1f) * 5123.7f) * 180f),  // 60〜240
            Views = 8000 + (int)(Frac(Mathf.Sin(i * 37.7f) * 6619.3f) * 40000f),
            Liked = Frac(Mathf.Sin(i * 53.1f) * 2237.7f) > 0.5f,             // 半分はいいね済み（淡桃）
        }, panelA: 0.13f);
    }

    // ---- STAGE1 あかり：自責の雨のタイムライン（雨の湿度を残す）----
    // #11 文面改稿（maeda）：背景は“世界中の声”＝言えなかった側の観測者たち。ボス本人の声（すき/ごめん）は
    // 背景カードから外し、mutter（言いかけて弾ける吹き出し）に集約＝isVoice の桃差し色はあえて発火させない。
    // "> " 始まりは引用リプ線（既存ロジック・index 0 のみ）。4枚ずつ周回で 8 件全部が順に流れる。
    private static readonly string[] AkariBodies =
        { "> 雨、やばくない?", "片想いは無料です", "既読は、ついてる", "あの時ああ言えば",
          "言いかけて、やめた", "下書きが、増えてく", "傘、二本あるのに", "送信は、できなかった" };
    private void DrawAkari(float fade)
    {
        // 自責リプのタイムライン。寒色ニュートラルのパネルに、本人の声だけ桃の差し色。右→左・上下2段。
        // DrawTimeline と同じ周回ローテ（+CardCount）で 8 件全部を順に流す（切替は画面外）。
        var panel = new Color(0.80f, 0.85f, 1f);
        var accent = new Color(0.86f, 0.88f, 0.95f);
        float span = W + CardW;
        for (int i = 0; i < CardCount; i++)
        {
            float raw = i * (span / CardCount) + (float)(_t * ScrollSpeed);
            float x = W - raw % span;
            float y = LaneRows[i % LaneRows.Length];
            int idx = (i + (int)(raw / span) * CardCount) % AkariBodies.Length;
            string body = AkariBodies[idx];
            bool isVoice = body == "すき" || body.StartsWith("ごめん"); // 本人の声＝桃（#11 文面では意図的に不発）
            bool quote = body.StartsWith(">");
            var text = isVoice ? new Color(0.95f, 0.86f, 0.90f) : new Color(0.82f, 0.86f, 0.96f);
            // 自責リプ＝小さなスレッド。数字は控えめ。本人の声(isVoice)だけ淡桃のいいねが灯る。
            DrawCard(x, y, 0.13f, fade, panel, text, accent, idx, body,
                     replies: 1 + (int)(Frac(Mathf.Sin(idx * 31.7f) * 1913.1f) * 6f),  // 1〜7
                     reposts: (int)(Frac(Mathf.Sin(idx * 23.1f) * 1777.7f) * 4f),       // 0〜3
                     likes: 2 + (int)(Frac(Mathf.Sin(idx * 13.7f) * 2113.3f) * 9f),     // 2〜11
                     views: 80 + (int)(Frac(Mathf.Sin(idx * 41.3f) * 2551.9f) * 600f),
                     liked: isVoice,
                     quote: quote);
        }

        // 雨（細い横斜線）。画面（X）越しに降る雨の教室の湿度を残すが、進行＝横流れを優先＝左へ強く流す。
        // 縦の落下が主役にならないよう α は控えめ(0.07)、横へ寝た斜め(左:下 ≈ 4:1)。
        var rain = new Color(0.7f, 0.8f, 1f, 0.07f * fade);
        float rainSpan = W + 12f;
        for (int i = 0; i < 26; i++)
        {
            float rx = rainSpan - ((i * 53 + (float)(_t * 150.0)) % rainSpan); // 半減（画面酔い対策）
            float ry = (i * 71) % (int)H;
            DrawLine(new Vector2(rx, ry), new Vector2(rx - 9f, ry + 2.5f), rain, 1f);
        }

        // 言いかけて弾ける白い吹き出し（道中の演出）。文字が浮かびかけ、言い切る前に弾けて消える。
        string[] mutter = { "す——", "あの——", "ね、——", "ごめ——" };
        for (int i = 0; i < mutter.Length; i++)
        {
            const float life = 4.2f;
            float p = (float)(((_t + i * 1.05) % life) / life); // 0..1
            float bx = 46f + (i * 83) % (int)(W - 96f);
            float by = 122f - p * 44f;                          // ゆっくり上昇
            if (p < 0.72f)
            {
                float aa = Mathf.Min(p / 0.18f, 1f) * 0.55f * fade;
                var sz = _font.GetStringSize(mutter[i], HorizontalAlignment.Left, -1, 9);
                DrawRect(new Rect2(bx - 3f, by - sz.Y - 1f, sz.X + 6f, sz.Y + 4f), new Color(1f, 1f, 1f, aa));
                _font.DrawString(GetCanvasItem(), new Vector2(bx, by), mutter[i],
                    HorizontalAlignment.Left, -1, 9, new Color(0.2f, 0.2f, 0.26f, Mathf.Min(aa * 1.8f, 1f)));
            }
            else if (p < 0.80f)
            {
                // 弾ける瞬間：白い破片が散る
                float f = 1f - (p - 0.72f) / 0.08f;
                var frag = new Color(1f, 1f, 1f, 0.5f * f * fade);
                for (int k = 0; k < 6; k++)
                {
                    float ang = Mathf.Tau * k / 6f;
                    DrawLine(new Vector2(bx + 8f, by - 4f),
                        new Vector2(bx + 8f + Mathf.Cos(ang) * 6f, by - 4f + Mathf.Sin(ang) * 6f), frag, 1f);
                }
            }
        }

        // 記憶フラッシュ（雨の交差点。言いかけた唇。クラクション）。
        if (_flashT > 0)
        {
            float f = Mathf.Clamp((float)_flashT / 2.4f, 0f, 1f);
            float pulse = 0.35f + 0.25f * Mathf.Sin((float)_t * 18f);
            DrawRect(new Rect2(0, 0, W, H), new Color(1f, 1f, 1f, (0.30f + pulse * 0.2f) * f));
            // 交差点（十字の道）
            var road = new Color(0.6f, 0.62f, 0.7f, 0.5f * f);
            DrawRect(new Rect2(0, H / 2f - 16f, W, 32f), road);
            DrawRect(new Rect2(W / 2f - 16f, 0, 32f, H), road);
            _font.DrawString(GetCanvasItem(), new Vector2(W / 2f - 70f, H / 2f - 26f),
                "あのね、あたし——", HorizontalAlignment.Left, -1, 12, new Color(0.2f, 0.2f, 0.25f, 0.8f * f));
        }
    }

    // ---- STAGE2 こはる：電気を消した部屋の静かな投稿（暗背景・α一段低め）----
    // いいねは 0 か 1（誰も反応しない）。叫ばない。4枚ずつ周回で 8 件全部が順に流れる。
    // 本文は仮台本 07（S2-1〜S2-5）の層1〜層3。明るい投稿から、だんだん本人の声が透ける並び。
    private static readonly string[] KoharuBodies =
        { "推しの配信、今日も来た", "塾の帰り、配信間に合った", "グッズの箱、3つ目", "親には言ってない",
          "今日も明るいねって言われた", "何の話してたか、覚えてない", "1:20 まだ振ってる", "…" };
    private void DrawKoharu(float fade)
    {
        // 孤独のタイムライン。暗背景なので一段低い α(0.07)。反応ゼロ＝いいね 0/1。
        var panel = new Color(0.85f, 0.84f, 0.82f);   // ニュートラル暖
        var text = new Color(0.86f, 0.85f, 0.83f);
        var accent = new Color(0.90f, 0.89f, 0.86f);
        DrawTimeline(fade, panel, text, accent, KoharuBodies, i => new CardMeta
        {
            // 孤独＝誰も反応しない。返信・リポスト・いいねは 0/1。閲覧数だけわずか（読まれてはいる）。
            Replies = 0,
            Reposts = 0,
            Likes = (int)(Frac(Mathf.Sin(i * 19.3f) * 1303.1f) * 1.6f),     // 0 か 1
            Views = 1 + (int)(Frac(Mathf.Sin(i * 27.7f) * 911.3f) * 8f),    // 1〜8（既読の冷たさ）
            Liked = false,                                                   // 誰もハートを押さない
        }, panelA: 0.07f);

        // 机の下に、箱が三つ（S2-1「開けられた跡は、ひとつだけ」）。
        float ty = 150f;
        var box = new Color(0.44f, 0.38f, 0.46f, 0.34f * fade);
        for (int i = 0; i < 3; i++)
            DrawRect(new Rect2(W / 2f - 40f + i * 20f, ty + 20f - i * 2f, 17f, 12f + i * 2f), box);
        // 一つだけ蓋が開いている（跡はひとつだけ＝説明しない）
        DrawLine(new Vector2(W / 2f - 40f, ty + 20f), new Vector2(W / 2f - 26f, ty + 15f),
            new Color(0.58f, 0.52f, 0.62f, 0.34f * fade), 1.2f);

        // 振られたままのペンライト：光は画面へ向かうが、届かない（S2-3。線は途中で切れる）
        float sway = Mathf.Sin((float)_t * 1.7f) * 6f;
        var stick = new Color(0.72f, 0.68f, 0.86f, 0.42f * fade);
        DrawLine(new Vector2(W / 2f + 44f, ty + 30f), new Vector2(W / 2f + 48f + sway, ty + 12f), stick, 1.6f);
        var beam = new Color(0.86f, 0.84f, 0.98f, 0.13f * fade * (0.6f + 0.4f * Mathf.Sin((float)_t * 2f)));
        DrawLine(new Vector2(W / 2f + 48f + sway, ty + 10f), new Vector2(W / 2f + 52f + sway * 1.4f, ty - 10f), beam, 1f);

        // 部屋の静けさの余韻：薄い文字が床に滲む（S2-3 の「むだだ」）
        _font.DrawString(GetCanvasItem(), new Vector2(W / 2f - 64f, 196f),
            "むだだ", HorizontalAlignment.Left, -1, 10, new Color(0.8f, 0.8f, 0.86f, 0.10f * fade));
    }

    // ═════════ S3 画の反転（改心成立＝cry→post 後、帰還ビートの背景でゆっくり進む） ═════════
    // 共通の作法：①同じ場所・同じ形で戻す（道中で見た象徴だと分かる）②白/寒色 → 暖色は
    //   クロスフェード（瞬間切替しない）③α上限は「前」と同水準（説明的に明るくしない・指ししない）。

    // ---- レイ：白飛びしていた「１位」のピン留めに、色が差す ----
    //   読めない眩しさ（＝1位の専制）が夜明けの暖色に置き換わり、その下に「２位」が同じ光で灯る。
    //   順位は変わらない。当たる光だけが等しくなる＝「2位にも価値」を言葉でなく色で言う。
    private void DrawReiReversal()
    {
        float aIn = RevPhase(0f, 4.5f);       // カードが戻ってくる
        float col = RevPhase(2f, 9f);         // 白 → 暖色（色が差す）
        float second = RevPhase(6.5f, 12f);   // 「２位」が同じ色で灯る
        if (aIn <= 0f) return;
        var ci = GetCanvasItem();

        // パネル：白飛びの glow と同じ矩形が、色温度だけ変えて同じ場所に戻る。
        var warm = new Color(1f, 0.78f, 0.45f); // 夜明けの琥珀（寒色の画面で洗われない程度に彩度を持つ）
        var pc = new Color(Mathf.Lerp(1f, warm.R, col), Mathf.Lerp(1f, warm.G, col), Mathf.Lerp(1f, warm.B, col));
        DrawRect(new Rect2(W / 2f - 40f, 6f, 80f, 22f), new Color(pc.R, pc.G, pc.B, 0.14f * aIn));
        DrawRect(new Rect2(W / 2f - 40f, 6f, 80f, 22f), new Color(pc.R, pc.G, pc.B, 0.09f * aIn), false, 1f);
        // 「１位」：白飛び(0.42)から“読める”暖色へ。眩しさが取れて、ただの一枚の投稿に戻る。
        var t1 = new Color(Mathf.Lerp(1f, 1f, col), Mathf.Lerp(1f, 0.83f, col), Mathf.Lerp(1f, 0.50f, col),
            Mathf.Lerp(0.40f, 0.62f, col) * aIn);
        _font.DrawString(ci, new Vector2(W / 2f - 16f, 22f), "１位", HorizontalAlignment.Left, -1, 12, t1);

        // 「２位」：ひとまわり小さく、同じ暖色・同じ光で下に灯る（大きさの序列は残す＝嘘をつかない）。
        if (second > 0f)
        {
            DrawRect(new Rect2(W / 2f - 28f, 32f, 56f, 16f), new Color(warm.R, warm.G, warm.B, 0.10f * second));
            DrawRect(new Rect2(W / 2f - 28f, 32f, 56f, 16f), new Color(warm.R, warm.G, warm.B, 0.07f * second), false, 1f);
            _font.DrawString(ci, new Vector2(W / 2f - 12f, 44f), "２位",
                HorizontalAlignment.Left, -1, 10, new Color(1f, 0.83f, 0.50f, 0.55f * second));
        }
    }

    // ---- あかり：自責の言葉が「ありがとう」へ溶けて変わる ----
    //   スレッドの「あたしのせいだ」が残響として薄く戻り、重なったまま桃色の「ありがとう」と
    //   クロスフェード。最後に淡桃のいいねが“1”つ灯る＝はじめて誰かが応えた。
    private void DrawAkariReversal()
    {
        float echo = RevPhase(0f, 3.5f);    // 自責の残響が薄く戻る
        float cross = RevPhase(3f, 9f);     // 自責 → ありがとう（重ねたまま入れ替わる）
        float heart = RevPhase(9f, 12f);    // 淡桃のいいねが 1 つ灯る
        if (echo <= 0f) return;
        var ci = GetCanvasItem();

        const float y = 42f;
        string oldS = "あたしのせいだ", newS = "ありがとう";
        float oldW = _font.GetStringSize(oldS, HorizontalAlignment.Left, -1, 11).X;
        float newW = _font.GetStringSize(newS, HorizontalAlignment.Left, -1, 11).X;

        // 旧：寒色の自責（引用スレッドの線ごと戻り、クロスフェードで消えていく）。
        float oldA = 0.34f * echo * (1f - cross);
        if (oldA > 0.003f)
        {
            float ox = W / 2f - oldW / 2f;
            DrawLine(new Vector2(ox - 5f, y - 10f), new Vector2(ox - 5f, y + 2f),
                new Color(0.82f, 0.86f, 0.96f, oldA * 0.8f), 1f);
            _font.DrawString(ci, new Vector2(ox, y), oldS, HorizontalAlignment.Left, -1, 11,
                new Color(0.82f, 0.86f, 0.96f, oldA));
        }

        // 新：本人の声の桃色（DrawAkari の isVoice と同系だが、雨の寒色に洗われない程度に一段深く）。
        float newA = 0.58f * cross;
        if (newA > 0.003f)
        {
            float nx = W / 2f - newW / 2f;
            _font.DrawString(ci, new Vector2(nx, y), newS, HorizontalAlignment.Left, -1, 11,
                new Color(0.97f, 0.72f, 0.80f, newA));
            // いいね“1”：孤独に0だった数字ではなく、たった一つの応答が灯る。
            if (heart > 0f)
            {
                var hc = new Color(0.78f, 0.55f, 0.62f, 0.55f * heart);
                IconHeart(nx + newW + 6f, y - 8f, hc);
                _font.DrawString(ci, new Vector2(nx + newW + 14f, y - 1f), "1",
                    HorizontalAlignment.Left, -1, 8, hc);
            }
        }
    }

    // ---- こはる：消えていた配信画面が灯り、ペンライトの光が画面に届く（S2-9）----
    //   道中で見た小道具（机の下の箱／振られたままのペンライト）を、同じ場所・同じ形のまま戻す。
    //   変わるのは光だけ：消えていた画面に色が差し、途中で切れていた光の線が画面まで伸び切る。
    //   説明的に明るくしない（α上限は「前」と同水準）。指ししない＝気づく余白。
    private void DrawKoharuReversal()
    {
        float back = RevPhase(0f, 4f);    // 箱とペンライトが戻る
        float lit = RevPhase(2.5f, 11f);  // 画面が灯り、光が届き切るまで
        if (back <= 0f) return;

        float ty = 150f;
        // 机の下の箱（前と同じ形。色温度だけほんの少し暖かく）
        var box = new Color(0.48f, 0.42f, 0.48f, 0.34f * back);
        for (int i = 0; i < 3; i++)
            DrawRect(new Rect2(W / 2f - 40f + i * 20f, ty + 20f - i * 2f, 17f, 12f + i * 2f), box);

        // 灯った配信画面（道中は消えていた矩形。lit に応じて色が差す）
        var scr = new Rect2(W / 2f + 42f, ty - 44f, 44f, 28f);
        DrawRect(scr, new Color(0.86f, 0.84f, 0.98f, 0.05f + 0.13f * lit * back));

        // ペンライト：前と同じ揺れ。光の線だけが、途中で切れずに画面まで伸び切る。
        float sway = Mathf.Sin((float)_t * 1.7f) * 6f;
        DrawLine(new Vector2(W / 2f + 44f, ty + 30f), new Vector2(W / 2f + 48f + sway, ty + 12f),
            new Color(0.78f, 0.74f, 0.92f, 0.42f * back), 1.6f);
        var beam = new Color(0.90f, 0.88f, 1.00f, (0.13f + 0.10f * lit) * back * (0.6f + 0.4f * Mathf.Sin((float)_t * 2f)));
        // 届く先＝画面の下端。lit=0 では前と同じ長さ（20px）、lit=1 で画面まで届き切る。
        var from = new Vector2(W / 2f + 48f + sway, ty + 10f);
        var to = from.Lerp(new Vector2(scr.Position.X + scr.Size.X / 2f, scr.End.Y), 0.28f + 0.72f * lit);
        DrawLine(from, to, beam, 1f);
    }
}
