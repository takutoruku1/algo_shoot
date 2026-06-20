using Godot;

// StageImagery : 各ステージの「心象世界」を、汚染された SNS(X) のタイムラインが上→下へ流れ落ちる
//   ツイート風カードとしてエンジン描画で重ねる軽量レイヤー（設計書 4 / 6 の演出ト書きを SNS で翻訳）。
//   Rei   : 順位晒しのタイムライン。「２位おめでとう（笑）」等の心ない投稿／「１位」は白飛びの固定ポスト。
//   Akari : 自責リプのスレッド。引用RTで「あたしのせいだ」が増幅、本人の「すき」「ごめん」が桃の差し色。雨は残す。
//   Koharu: 孤独の静かな投稿。「だれも、こない」。いいねは 0（誰も反応しない）。台所の余韻は残す。暗背景なのでα低め。
// 背景画像(ZIndex -90)の上、ゲーム要素(0..10)の下(ZIndex -50)に描く。浄化が進む(Warmth↑)と薄れて晴れる。
//
// 弾幕の視認性最優先：カードは「画面奥で流れ落ちる世間の声」で前に出さない。無彩色〜淡い同系色（弾の濃ピンク/
//   黒グリフと色相分離）、加算しない、α上限おおむね0.20（合算込み・こはるは暗背景で一段低い）、同時4枚・中央回避・
//   24px/s（ScrollFx近層96より遥か遅い＝奥）。乱数は使わず Frac(Mathf.Sin(i*..)*..) の決定論パターン。
public partial class StageImagery : Node2D
{
    public enum StageKind { Rei, Akari, Koharu }
    public StageKind Kind = StageKind.Rei;

    private const float W = 384f, H = 216f;
    private FontFile _font = null!;
    private double _t;
    private double _flashT;   // Akari の記憶フラッシュ（>0 の間だけ描画）
    private float _bulletDamp = 1f; // 弾密度が高いほど背景を引く係数（致命情報を最前面の明るさに＝§3 視認性）

    public override void _Ready()
    {
        ZIndex = -50;
        ZAsRelative = false;
        AddToGroup("imagery");
        _font = UiKit.Zen; // 非ピクセル（滑らかゴシック）
    }

    // BossAkari の「地・記憶」行で呼ばれ、雨の交差点のフラッシュを一瞬焚く（伏線：あかりとの記憶）。
    public void TriggerMemoryFlash() => _flashT = 2.4;

    public override void _Process(double delta)
    {
        _t += delta;
        if (_flashT > 0) _flashT -= delta;
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
        switch (Kind)
        {
            case StageKind.Rei: DrawRei(fade); break;
            case StageKind.Akari: DrawAkari(fade); break;
            case StageKind.Koharu: DrawKoharu(fade); break;
        }
    }

    // ───────── 共通：汚染SNSタイムラインの「X(旧Twitter)投稿」カード ─────────
    // 全ステージ共通の小カード。上→下へ 24px/s でループ。同時4枚・横2レーン（中央は弾の主戦場なので空ける）。
    // 本物の X 投稿の骨格に寄せる：①円アイコン＋表示名(太め)＋@ハンドル(灰)＋「· 2時間」相対時刻(中黒)＋時々青認証
    //   ②本文 ③アクション行4つ（返信/リポスト/いいね/閲覧数）を X の並び順で極小シルエット＋数字。
    private const float CardW = 156f, CardH = 52f;   // アクション行を入れる余地（奥行きは速度24px/sで担保）
    private const float ScrollSpeed = 24f;           // px/s（ScrollFx近層96より遥か遅い＝奥）
    private const int CardCount = 4;                 // 同時表示（控えめ）
    private static readonly float[] Lanes = { 14f, W - CardW - 14f }; // 左右の縁。中央を空ける

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

    // 1枚の X 投稿カードを描く。本文・名前・メタは i 固定（周回でチラつかない）。
    //   panel : パネル基本色, text : 本文/名前色, accent : アイコン色
    //   replies/reposts/likes/views : アクション行の数字, liked : いいね済み（ハートを淡桃に）, quote : 引用リプ線（あかり）
    private void DrawCard(float x, float y, float pa, float fade, Color panel, Color text, Color accent,
                          int i, string body, int replies, int reposts, int likes, int views,
                          bool liked = false, bool quote = false)
    {
        var ci = GetCanvasItem();
        float a = pa * fade;
        // パネル（半透明）＋枠線（型を出すが主張させない）。
        DrawRect(new Rect2(x, y, CardW, CardH), new Color(panel.R, panel.G, panel.B, a));
        DrawRect(new Rect2(x, y, CardW, CardH), new Color(panel.R, panel.G, panel.B, a * 0.7f), false, 1f);

        // ① 円アイコン（左上・本物の X に寄せて真円に）。
        float cx = x + 11f, cy = y + 11f;
        DrawCircle(new Vector2(cx, cy), 5f, new Color(accent.R, accent.G, accent.B, a * 1.4f));

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

    // 4枚のカードのループ y を等間隔で配り、各ステージの描画を行う。
    private void DrawTimeline(float fade, Color panel, Color text, Color accent,
                              string[] bodies, System.Func<int, CardMeta> meta, float panelA)
    {
        float span = H + CardH;
        for (int i = 0; i < CardCount; i++)
        {
            float y = (i * (span / CardCount) + (float)(_t * ScrollSpeed)) % span - CardH;
            float x = Lanes[i % Lanes.Length];
            var m = meta(i);
            DrawCard(x, y, panelA, fade, panel, text, accent, i, bodies[i % bodies.Length],
                     m.Replies, m.Reposts, m.Likes, m.Views, m.Liked, quote: false);
        }
    }

    // ---- STAGE1 レイ：順位晒しのタイムライン（Cold=青系）----
    private static readonly string[] ReiBodies =
        { "２位おめでとう（笑）", "所詮この程度", "期待して損した", "また２位ｗ", "がんばっただけ", "知ってた" };
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

    // ---- STAGE2 あかり：自責リプのスレッド（雨の湿度を残す）----
    // 本文は引用RT/リプ構造。「すき」「ごめん」は本人の声＝桃の差し色（DrawCardAkariBody で別色）。
    private static readonly string[] AkariBodies =
        { "> あたしのせいだ", "ごめん", "すき", "ぜんぶ、あたしの", "ごめんね", "> あたしのせいだ" };
    private void DrawAkari(float fade)
    {
        // 自責リプのタイムライン。寒色ニュートラルのパネルに、本人の声だけ桃の差し色。
        var panel = new Color(0.80f, 0.85f, 1f);
        var accent = new Color(0.86f, 0.88f, 0.95f);
        float span = H + CardH;
        for (int i = 0; i < CardCount; i++)
        {
            float y = (i * (span / CardCount) + (float)(_t * ScrollSpeed)) % span - CardH;
            float x = Lanes[i % Lanes.Length];
            string body = AkariBodies[i % AkariBodies.Length];
            bool isVoice = body == "すき" || body.StartsWith("ごめん"); // 本人の声＝桃
            bool quote = body.StartsWith(">");
            var text = isVoice ? new Color(0.95f, 0.86f, 0.90f) : new Color(0.82f, 0.86f, 0.96f);
            // 自責リプ＝小さなスレッド。数字は控えめ。本人の声(isVoice)だけ淡桃のいいねが灯る。
            DrawCard(x, y, 0.13f, fade, panel, text, accent, i, body,
                     replies: 1 + (int)(Frac(Mathf.Sin(i * 31.7f) * 1913.1f) * 6f),  // 1〜7
                     reposts: (int)(Frac(Mathf.Sin(i * 23.1f) * 1777.7f) * 4f),       // 0〜3
                     likes: 2 + (int)(Frac(Mathf.Sin(i * 13.7f) * 2113.3f) * 9f),     // 2〜11
                     views: 80 + (int)(Frac(Mathf.Sin(i * 41.3f) * 2551.9f) * 600f),
                     liked: isVoice,
                     quote: quote);
        }

        // 雨（細い斜線）。画面（X）越しに降る雨の教室の湿度を残す。
        var rain = new Color(0.7f, 0.8f, 1f, 0.10f * fade);
        for (int i = 0; i < 26; i++)
        {
            float rx = (i * 53 + (float)(_t * 220.0)) % W;
            float ry = (i * 71 + (float)(_t * 320.0)) % H;
            DrawLine(new Vector2(rx, ry), new Vector2(rx - 2f, ry + 8f), rain, 1f);
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

    // ---- STAGE3 こはる：孤独の静かな投稿（暗背景・α一段低め）＋台所の余韻 ----
    // いいねは 0 か 1（誰も反応しない孤独）。叫ばない。台所の食卓・空席・箸・湯気は残す。
    private static readonly string[] KoharuBodies =
        { "だれも、こない", "つくりすぎた", "きょうも、ひとり", "いただきます", "おかえり、って", "…" };
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

        // 食卓（テーブル天板）
        float ty = 150f;
        DrawRect(new Rect2(60f, ty, W - 120f, 10f), new Color(0.42f, 0.30f, 0.22f, 0.40f * fade));
        DrawRect(new Rect2(70f, ty + 10f, 8f, 36f), new Color(0.38f, 0.27f, 0.20f, 0.36f * fade));   // 脚
        DrawRect(new Rect2(W - 78f, ty + 10f, 8f, 36f), new Color(0.38f, 0.27f, 0.20f, 0.36f * fade));

        // 空席（椅子の背だけ）＝誰も座っていない
        var chair = new Color(0.4f, 0.34f, 0.28f, 0.30f * fade);
        DrawRect(new Rect2(W / 2f - 8f, ty + 14f, 16f, 4f), chair);
        DrawRect(new Rect2(W / 2f - 8f, ty + 18f, 3f, 24f), chair);
        DrawRect(new Rect2(W / 2f + 5f, ty + 18f, 3f, 24f), chair);

        // 箸だけが置かれている（空席の手前）
        var hashi = new Color(0.85f, 0.78f, 0.6f, 0.6f * fade);
        DrawLine(new Vector2(W / 2f - 18f, ty - 2f), new Vector2(W / 2f - 2f, ty - 5f), hashi, 1.4f);
        DrawLine(new Vector2(W / 2f - 18f, ty + 1f), new Vector2(W / 2f - 2f, ty - 2f), hashi, 1.4f);

        // 茶碗（湯気が細っていく＝料理が冷める）
        var bowl = new Color(0.7f, 0.72f, 0.78f, 0.45f * fade);
        DrawRect(new Rect2(W / 2f - 40f, ty - 6f, 14f, 6f), bowl);
        var steam = new Color(0.9f, 0.92f, 0.95f, 0.18f * fade * (0.5f + 0.5f * Mathf.Sin((float)_t * 2f)));
        for (int i = 0; i < 3; i++)
        {
            float sx = W / 2f - 35f + Mathf.Sin((float)_t * 3f + i) * 2f;
            DrawLine(new Vector2(sx, ty - 8f), new Vector2(sx, ty - 16f - i * 2f), steam, 1f);
        }

        // 「誰のためでもないごはん」の余韻：薄い文字が床に滲む
        _font.DrawString(GetCanvasItem(), new Vector2(W / 2f - 64f, 196f),
            "だれも、こない", HorizontalAlignment.Left, -1, 10, new Color(0.8f, 0.8f, 0.86f, 0.10f * fade));
    }
}
