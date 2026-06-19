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
        QueueRedraw();
    }

    private float Warmth => GetNodeOrNull<GameManager>("/root/Game")?.Warmth ?? 0f;

    public override void _Draw()
    {
        if (_font == null) return;
        float fade = 1f - Mathf.Clamp(Warmth, 0f, 1f); // 浄化で晴れる
        switch (Kind)
        {
            case StageKind.Rei: DrawRei(fade); break;
            case StageKind.Akari: DrawAkari(fade); break;
            case StageKind.Koharu: DrawKoharu(fade); break;
        }
    }

    // ───────── 共通：汚染SNSタイムラインのツイート風カード ─────────
    // 全ステージ共通の小カード。上→下へ 24px/s でループ。同時4枚・横2レーン（中央は弾の主戦場なので空ける）。
    private const float CardW = 148f, CardH = 40f;
    private const float ScrollSpeed = 24f;     // px/s（ScrollFx近層96より遥か遅い＝奥）
    private const int CardCount = 4;            // 同時表示（控えめ）
    private static readonly float[] Lanes = { 14f, W - CardW - 14f }; // 左右の縁。中央を空ける

    private static float Frac(float v) => v - Mathf.Floor(v);

    // 匿名アカウント名を決定論生成（@nanashi_99 / @mob_2434 風）。
    private static readonly string[] Handles = { "nanashi", "mob", "no_name", "anon", "kuuki", "yajiruba", "_398" };
    private string Handle(int i)
    {
        int s = (int)(Frac(Mathf.Sin(i * 45.3f) * 10247.7f) * Handles.Length);
        int num = 10 + (int)(Frac(Mathf.Sin(i * 91.7f) * 7351.3f) * 8900f);
        return $"@{Handles[s % Handles.Length]}_{num}";
    }

    // 1枚のカードを描く。本文・@名・メタは i 固定（周回でチラつかない）。
    //   panel : パネル基本色（α込みの基準を fade で乗算）, text : 本文色, accent : アイコン色
    //   likes/rts : メタ数字（晒し＝多い／孤独＝0）, quote : リプライ引用線（あかり）
    private void DrawCard(float x, float y, float pa, float fade, Color panel, Color text, Color accent,
                          string handle, string body, int likes, int rts, bool quote = false)
    {
        var ci = GetCanvasItem();
        float a = pa * fade;
        // パネル（半透明）＋枠線（型を出すが主張させない）。ドット解像度なので素の矩形＋1px枠で「カード」感。
        DrawRect(new Rect2(x, y, CardW, CardH), new Color(panel.R, panel.G, panel.B, a));
        DrawRect(new Rect2(x, y, CardW, CardH), new Color(panel.R, panel.G, panel.B, a * 0.7f), false, 1f);

        // アイコン（左上の角丸シルエット＝8x8 の塗り＋四隅を1px欠いて丸める）。
        float ix = x + 6f, iy = y + 6f;
        var ic = new Color(accent.R, accent.G, accent.B, a * 1.4f);
        // 角丸風：8x8 の塗りから四隅の1pxを欠く（中央十字＋辺で丸みを出す）。
        DrawRect(new Rect2(ix + 1f, iy, 6f, 8f), ic);
        DrawRect(new Rect2(ix, iy + 1f, 8f, 6f), ic);

        // @ユーザー名（アイコン右、9px、やや低α）。
        _font.DrawString(ci, new Vector2(x + 18f, y + 13f), handle,
            HorizontalAlignment.Left, -1, 9, new Color(text.R, text.G, text.B, a * 1.5f));

        // 本文（10px）。引用リプ（quote）なら左にスレッド線＋字下げ。
        float bx = x + 18f, by = y + 26f;
        if (quote)
        {
            DrawLine(new Vector2(x + 16f, y + 18f), new Vector2(x + 16f, y + 34f),
                new Color(text.R, text.G, text.B, a * 1.2f), 1f);
            bx = x + 21f;
        }
        _font.DrawString(ci, new Vector2(bx, by), body,
            HorizontalAlignment.Left, -1, 10, new Color(text.R, text.G, text.B, Mathf.Min(a * 2.4f, 0.55f)));

        // 下部メタ：ハート(小三角)＋数字 / RT(点2つ)＋数字（9px・低α）。
        float my = y + CardH - 4f;
        var meta = new Color(text.R, text.G, text.B, a * 1.2f);
        // ハート＝小さな塗り三角の代用（2pxの点）
        DrawRect(new Rect2(x + 18f, my - 5f, 2f, 2f), meta);
        _font.DrawString(ci, new Vector2(x + 23f, my), likes.ToString(),
            HorizontalAlignment.Left, -1, 9, meta);
        // RT＝点2つ
        float rx = x + 50f;
        DrawRect(new Rect2(rx, my - 4f, 1f, 1f), meta);
        DrawRect(new Rect2(rx + 3f, my - 4f, 1f, 1f), meta);
        _font.DrawString(ci, new Vector2(rx + 6f, my), rts.ToString(),
            HorizontalAlignment.Left, -1, 9, meta);
    }

    // 4枚のカードのループ y を等間隔で配り、各ステージの描画を行う。
    private void DrawTimeline(float fade, Color panel, Color text, Color accent,
                              string[] bodies, System.Func<int, (int likes, int rts, bool quote)> meta, float panelA)
    {
        float span = H + CardH;
        for (int i = 0; i < CardCount; i++)
        {
            float y = (i * (span / CardCount) + (float)(_t * ScrollSpeed)) % span - CardH;
            float x = Lanes[i % Lanes.Length];
            var (likes, rts, quote) = meta(i);
            DrawCard(x, y, panelA, fade, panel, text, accent, Handle(i), bodies[i % bodies.Length], likes, rts, quote);
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
        DrawTimeline(fade, panel, text, accent, ReiBodies, i =>
        {
            int likes = 60 + (int)(Frac(Mathf.Sin(i * 17.1f) * 5123.7f) * 180f); // 60〜240（晒し）
            int rts = 20 + (int)(Frac(Mathf.Sin(i * 29.3f) * 3317.1f) * 90f);
            return (likes, rts, false);
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
            DrawCard(x, y, 0.13f, fade, panel, text, accent, Handle(i), body,
                     likes: 2 + (int)(Frac(Mathf.Sin(i * 13.7f) * 2113.3f) * 9f), // 2〜11（小さなリプ欄）
                     rts: (int)(Frac(Mathf.Sin(i * 23.1f) * 1777.7f) * 4f),       // 0〜3
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
        DrawTimeline(fade, panel, text, accent, KoharuBodies, i =>
        {
            int likes = (int)(Frac(Mathf.Sin(i * 19.3f) * 1303.1f) * 1.6f); // 0 か 1
            return (likes, 0, false);                                       // RT は常に 0
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
