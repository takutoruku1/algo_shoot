using Godot;

// StageImagery : 各ステージの「心象世界」をエンジン描画で重ねる軽量レイヤー（設計書 4 / 6 の演出ト書き）。
//   Rei   : 終わらないコンテスト会場。「２位」が無限に並ぶ順位掲示板／「１位」は白飛びで読めない。
//   Akari : 雨の教室。黒板に「あたしのせいだ」が無限、隙間に「すき」「ごめん」。机が天井へ落ちていく。記憶フラッシュ。
//   Koharu: 永遠に夕食を作り続ける台所。空席に箸だけ。料理は冷めていく（湯気が細る）。
// 背景画像(ZIndex -90)の上、ゲーム要素(0..10)の下(ZIndex -50)に描く。浄化が進む(Warmth↑)と薄れて晴れる。
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

    // ---- STAGE1 レイ：順位掲示板の海 ----
    private void DrawRei(float fade)
    {
        const float cell = 30f;
        float scroll = (float)(_t * 10.0) % cell; // ゆっくり上昇
        var dim = new Color(0.78f, 0.86f, 1f, 0.10f * fade);
        for (int row = -1; row * cell < H + cell; row++)
        {
            float y = row * cell - scroll + 8f;
            for (int col = 0; col * cell < W; col++)
            {
                float x = col * cell + 6f;
                _font.DrawString(GetCanvasItem(), new Vector2(x, y), "２位",
                    HorizontalAlignment.Left, -1, 12, dim);
            }
        }
        // 「１位」は白飛び（読めない）。中央上に眩しい矩形＋微かな文字。
        var glow = new Color(1f, 1f, 1f, 0.20f * fade);
        DrawRect(new Rect2(W / 2f - 34f, 20f, 68f, 22f), glow);
        DrawRect(new Rect2(W / 2f - 34f, 20f, 68f, 22f), new Color(1f, 1f, 1f, 0.10f * fade), false, 1f);
        _font.DrawString(GetCanvasItem(), new Vector2(W / 2f - 16f, 38f), "１位",
            HorizontalAlignment.Left, -1, 13, new Color(1f, 1f, 1f, 0.5f * fade));
    }

    // ---- STAGE2 あかり：雨の教室／黒板の自責／机が天井へ ----
    private void DrawAkari(float fade)
    {
        // 黒板（上部）。「あたしのせいだ」を無限に、隙間に「すき」「ごめん」。
        DrawRect(new Rect2(20f, 12f, W - 40f, 64f), new Color(0.08f, 0.14f, 0.10f, 0.45f * fade));
        var chalk = new Color(0.85f, 0.95f, 0.88f, 0.16f * fade);
        var chalk2 = new Color(0.95f, 0.85f, 0.9f, 0.18f * fade);
        for (int r = 0; r < 4; r++)
        {
            float y = 26f + r * 15f;
            for (int c = 0; c < 4; c++)
            {
                float x = 28f + c * 86f;
                _font.DrawString(GetCanvasItem(), new Vector2(x, y), "あたしのせいだ",
                    HorizontalAlignment.Left, -1, 9, chalk);
            }
            if (r % 2 == 0)
                _font.DrawString(GetCanvasItem(), new Vector2(300f, y), "すき", HorizontalAlignment.Left, -1, 9, chalk2);
            else
                _font.DrawString(GetCanvasItem(), new Vector2(300f, y), "ごめん", HorizontalAlignment.Left, -1, 9, chalk2);
        }

        // 机が天井へ落ちていく（上向きに浮上してループ）。
        var deskCol = new Color(0.5f, 0.42f, 0.32f, 0.22f * fade);
        for (int i = 0; i < 6; i++)
        {
            float phase = (float)((_t * 14.0 + i * 33.0) % 150.0);
            float y = H - phase;                       // 下から上へ
            float x = 40f + (i * 61) % (int)(W - 80f);
            DrawRect(new Rect2(x, y, 22f, 6f), deskCol);          // 天板
            DrawRect(new Rect2(x + 2f, y + 6f, 3f, 7f), deskCol); // 脚
            DrawRect(new Rect2(x + 17f, y + 6f, 3f, 7f), deskCol);
        }

        // 雨（細い斜線）。
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

    // ---- STAGE3 こはる：台所／空席に箸／冷めていく料理 ----
    private void DrawKoharu(float fade)
    {
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
