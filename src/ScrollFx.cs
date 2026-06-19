using Godot;

// ScrollFx : 額装背景(ZIndex -90 静止)とゲーム要素(0..)の間に置く「近景パララックス層」。
//   自機は画面内をほぼ留まるので、背景を下へ流すことで「自機が前へ進んでいる」前進感を作る。
//   額装背景(board.png)はループさせない（心象世界の「囚われた一場面」）。流れるのはこの近景層だけ。
//
// 二段構成で速度差（パララックス）を作る：
//   遠層 ZIndex -60 : 遅い・薄い・細かい粒（読めない遠景）
//   近層 ZIndex -55 : 速い(遠の約2.3倍)・やや濃い・縦ストリーク（手前を過ぎ去る）
//   ※ 既存 StageImagery(ZIndex -50) より奥。弾(0..)には絶対かぶらない。
//
// 弾幕の視認性最優先：α上限0.21・ほぼ無彩色(白〜淡い同系色／弾の濃ピンク・黒グリフと色相を分離)・
//   通常合成(加算しない)・線/粒1〜2px。実機で弾より目立たないことを確認済み。
// Warmth(浄化進行)に連動：晴れるほど α を下げ速度を落とす（流れが凪ぐ）。StageImagery と同じ
//   fade = 1f - Warmth の語彙で統一。
//
// Kind 分岐で各ステージへ横展開：
//   Rei   : 順位掲示板の海。遠＝読めない順位の光点、近＝下へ流れる横罫線（スキャンライン）。Cold=青系。
//   Akari : 手前を速く落ちる太い雨筋（後日）。
//   Koharu: 湯気・埃のゆるい対流（後日）。
public partial class ScrollFx : Node2D
{
    public enum StageKind { Rei, Akari, Koharu }
    public StageKind Kind = StageKind.Rei;

    private const float W = 384f, H = 216f;
    private double _t;

    // ───── 主役：生成スクロール背景（縦長テクスチャを下へ高速ループ）─────
    // 額装背景(board.png, ZIndex -90 静止)の上、弾(0..)の奥(負ZIndex)に置く。
    // 2枚のスプライトを縦に並べ、毎フレーム下へ動かし、画面外へ出たら上へ巻き戻す＝シームレス無限スクロール。
    // テクスチャは暗い濃紺ベース＋シアンの光帯で、上下端を暗く均一に作ってあるので継ぎ目が見えない。
    // 速度は大きめ（明確に「流れている」と分かる量）。Warmth(浄化)で少し落ち着く。
    private Sprite2D[] _scrollTiles = System.Array.Empty<Sprite2D>();
    private float _scrollTexH;       // 1枚分の表示高(px)
    private float _scrollY;          // ループ用オフセット(0.._scrollTexH)
    private float _scrollBaseSpeed;  // px/s（ステージごと）

    // 二層を別ノードに分けて ZIndex を個別に持たせる（同一 _Draw だと ZIndex を分けられないため）。
    private Layer _far = null!;
    private Layer _near = null!;

    public override void _Ready()
    {
        ZIndex = -60;
        ZAsRelative = false;
        AddToGroup("scrollfx");

        SetupScrollTexture();

        _far = new Layer { Name = "Far", Owner2 = this, Near = false, ZIndex = -60, ZAsRelative = false };
        _near = new Layer { Name = "Near", Owner2 = this, Near = true, ZIndex = -55, ZAsRelative = false };
        AddChild(_far);
        AddChild(_near);
    }

    // ステージごとの生成スクロール背景テクスチャと基準速度。
    private (string path, float speed) ScrollDef => Kind switch
    {
        StageKind.Rei    => ("res://char/bg/rei/scroll.png",    120f), // 順位の層が下へ流れ落ちる（速め＝明確な前進感）
        StageKind.Akari  => ("res://char/bg/akari/scroll.png",  150f), // 窓の外を流れる雨の景色（最速）
        StageKind.Koharu => ("res://char/bg/koharu/scroll.png",  64f), // 湯気/光がゆっくり流れる（穏やかだが動く）
        _ => ("", 0f),
    };

    private void SetupScrollTexture()
    {
        var (path, speed) = ScrollDef;
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path)) return;
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null) return;

        _scrollBaseSpeed = speed;
        // 画面幅(384)に合わせて等倍スケール。高さはアスペクト維持。
        float scale = W / tex.GetWidth();
        _scrollTexH = tex.GetHeight() * scale;

        // 画面高(216)を覆うのに必要なタイル枚数＋1（ループの巻き戻し用に最低2枚）。
        int count = Mathf.Max(2, Mathf.CeilToInt(H / _scrollTexH) + 1);
        var tiles = new Sprite2D[count];
        for (int i = 0; i < count; i++)
        {
            var spr = new Sprite2D
            {
                Name = $"Scroll{i}",
                Texture = tex,
                Centered = false,
                Scale = new Vector2(scale, scale),
                Position = new Vector2(0f, i * _scrollTexH),
                ZIndex = -70,        // board.png(-90)の上・StageImagery(-50)/弾(0..)の奥
                ZAsRelative = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest, // ドット感を保つ
            };
            AddChild(spr);
            tiles[i] = spr;
        }
        _scrollTiles = tiles;
    }

    public override void _Process(double delta)
    {
        _t += delta;

        // ── 主役のテクスチャを下へスクロール ──
        if (_scrollTiles.Length > 0)
        {
            float warm = Mathf.Clamp(Warmth, 0f, 1f);
            // 浄化が進むほど少し落ち着くが、止めない（最低でも55%は流れ続ける＝常に動いて見える）。
            float speed = _scrollBaseSpeed * (1f - 0.45f * warm);
            _scrollY += speed * (float)delta;
            float span = _scrollTiles.Length * _scrollTexH;
            float baseOff = _scrollY % _scrollTexH; // 0.._scrollTexH の連続オフセット
            for (int i = 0; i < _scrollTiles.Length; i++)
            {
                var p = _scrollTiles[i].Position;
                p.Y = i * _scrollTexH + baseOff;
                if (p.Y >= H) p.Y -= span; // 画面下へ出た列を最上段へ巻き戻す
                _scrollTiles[i].Position = p;
            }
        }

        _far.QueueRedraw();
        _near.QueueRedraw();
    }

    private float Warmth => GetNodeOrNull<GameManager>("/root/Game")?.Warmth ?? 0f;

    // 子レイヤー：遠／近のどちらかを描く。fade と t は親から読む。
    public partial class Layer : Node2D
    {
        public ScrollFx Owner2 = null!;
        public bool Near;

        public override void _Draw()
        {
            float fade = 1f - Mathf.Clamp(Owner2.Warmth, 0f, 1f); // 浄化で凪ぐ
            float t = (float)Owner2._t;
            switch (Owner2.Kind)
            {
                case StageKind.Rei: DrawRei(fade, t); break;
                case StageKind.Akari: DrawAkari(fade, t); break;
                case StageKind.Koharu: DrawKoharu(fade, t); break;
            }
        }

        // ---- STAGE1 レイ：通り過ぎる順位の光点（遠）／下へ流れる横罫線（近）。Cold=青系。----
        private void DrawRei(float fade, float t)
        {
            var ci = GetCanvasItem();
            if (!Near)
            {
                // 遠層：読めない順位の光点。下方向 26〜34px/s、α0.06〜0.10、1px の点／短い縦ストリーク。
                // 画面に 24 個。決定論的に散らす（疑似乱数を i から作る）。
                // 実機確認：board.png(-90)が明るく細密なため α0.06〜0.10では流れが全く読めず静止に見えた。
                // 弾(濃いピンク/黒グリフ＝高コントラスト)とは色相が違い埋もれないので、粒数・α・速度を一段上げる。
                const int count = 36;
                float spd = 42f;
                for (int i = 0; i < count; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 12.9898f) * 43758.5453f);
                    float seedy = Frac(Mathf.Sin(i * 78.233f) * 12543.713f);
                    float x = seedx * W;
                    float span = H + 24f;
                    float y = (seedy * span + t * spd) % span - 12f;
                    float a = (0.10f + seedx * 0.05f) * fade; // 0.10〜0.15
                    var col = new Color(0.80f, 0.87f, 1f, a);
                    // 半分は点、半分は短い 1px 縦ストリーク（読めない粒感）。流れを見せるため少し長く。
                    if (i % 2 == 0)
                        DrawRect(new Rect2(x, y, 1f, 1f), col);
                    else
                        DrawLine(new Vector2(x, y), new Vector2(x, y + 5f), col, 1f);
                }
            }
            else
            {
                // 近層：下へ流れる横罫線（順位掲示板のスキャンライン）。70px/s（遠の約2.3倍）、
                // α0.12〜0.16、長さ8〜16px の横ストリークを段組みで。16 本。
                // 実機確認で凪ぎすぎ＝静止見え。近層は速さ・α・本数を上げ「過ぎ去る」流れを明確化。
                // 横罫線は弾と直交方向（横）なので縦に流れても弾の追従を妨げない。
                const int rows = 20;
                float spd = 96f;
                float gap = H / rows;
                float scroll = (t * spd) % gap;
                for (int r = -1; r < rows + 1; r++)
                {
                    float y = r * gap + scroll;
                    float seed = Frac(Mathf.Sin(r * 34.17f) * 9817.31f);
                    float x = seed * (W - 16f);
                    float len = 10f + seed * 12f;                 // 10〜22px（流れの手掛かりを長く）
                    float a = (0.16f + seed * 0.05f) * fade;      // 0.16〜0.21
                    var col = new Color(0.78f, 0.85f, 1f, a);
                    DrawLine(new Vector2(x, y), new Vector2(x + len, y), col, 1f);
                    // たまに 2px の太め罫線（手前感）。
                    if (r % 4 == 0)
                    {
                        float x2 = Frac(seed + 0.37f) * (W - 24f);
                        DrawLine(new Vector2(x2, y + gap * 0.5f), new Vector2(x2 + 16f, y + gap * 0.5f),
                            new Color(0.80f, 0.86f, 1f, a * 0.9f), 2f);
                    }
                }
            }
        }

        // ---- STAGE2 あかり：手前を速く落ちる太い雨筋（近）／奥のかすかな降りこめ・埃（遠）。----
        // classroom.png は寒色・中明度で、既存 StageImagery 側に均一な雨(220/320px/s, α0.10, ZIndex -50)がある。
        // その雨より「手前(近層)＝速く・太く(2px)・少し斜め」「奥(遠層)＝遅く・薄く・点」で速度差＝奥行きを作る。
        // 色は教室の寒色〜ニュートラル(0.78〜0.82, 0.84〜0.88, 1.0)。彩度は上げない。中明度背景なのでレイに近いαで読める。
        private void DrawAkari(float fade, float t)
        {
            if (!Near)
            {
                // 遠層：奥のかすかな降りこめ／漂う埃。下方向 30px/s、α0.05〜0.09、1px の点。28個。
                // 既存雨(StageImagery)より遥かに遅く・薄い＝「ずっと奥で降っている」遠景。決定論的に散らす。
                const int count = 28;
                float spd = 30f;
                for (int i = 0; i < count; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 12.9898f) * 43758.5453f);
                    float seedy = Frac(Mathf.Sin(i * 78.233f) * 12543.713f);
                    float x = seedx * W;
                    float span = H + 16f;
                    float y = (seedy * span + t * spd) % span - 8f;
                    float a = (0.05f + seedx * 0.04f) * fade;       // 0.05〜0.09
                    var col = new Color(0.80f, 0.86f, 1f, a);
                    // 大半は漂う埃(点)、たまに 1px の短い降りこめ。
                    if (i % 3 == 0)
                        DrawLine(new Vector2(x, y), new Vector2(x - 1f, y + 4f), col, 1f);
                    else
                        DrawRect(new Rect2(x, y, 1f, 1f), col);
                }
            }
            else
            {
                // 近層：手前を速く落ちる太い雨筋。210px/s（遠の7倍／既存雨に近いが太く長い＝手前感）、
                // α0.11〜0.16、長さ10〜18px・2px幅・やや斜め(風)。16本。縦流れだが弾(濃ピンク/黒)とは色相が違い埋もれない。
                const int streaks = 16;
                float spd = 210f;
                float span = H + 24f;
                for (int i = 0; i < streaks; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 31.7f) * 8123.17f);
                    float seedy = Frac(Mathf.Sin(i * 53.91f) * 4517.93f);
                    float x = seedx * W;
                    float y = (seedy * span + t * spd) % span - 12f;
                    float len = 10f + seedx * 8f;                    // 10〜18px
                    float a = (0.11f + seedy * 0.05f) * fade;        // 0.11〜0.16
                    var col = new Color(0.80f, 0.85f, 1f, a);
                    // 斜め(風で右へ -2.5px)・2px幅の雨筋。
                    DrawLine(new Vector2(x, y), new Vector2(x - 2.5f, y + len), col, 2f);
                }
            }
        }

        // ---- STAGE3 こはる：立ちのぼって流れる湯気/冷気の対流（近）／宙を漂う小さな埃（遠）。----
        // kitchen.png は深い紺・暗め(低明度)。3ステージ中いちばん遅め＝「凪いだ空気だけが動く」。
        // 暗い背景なので α はレイ/あかりより一段下げる（主張しすぎ防止）。暖色寄りだが彩度は上げない(0.90台のニュートラル暖)。
        private void DrawKoharu(float fade, float t)
        {
            if (!Near)
            {
                // 遠層：宙を漂う小さな埃。ほぼ無移動の凪、横方向に sin で微かに揺れる。α0.04〜0.07、1px、22個。
                const int count = 22;
                float spd = 8f;                                     // 最も遅い（漂うだけ）
                for (int i = 0; i < count; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 12.9898f) * 43758.5453f);
                    float seedy = Frac(Mathf.Sin(i * 78.233f) * 12543.713f);
                    float span = H + 12f;
                    float baseY = (seedy * span - t * spd) % span;  // ごくゆっくり上へ漂う
                    if (baseY < 0) baseY += span;
                    float y = baseY - 6f;
                    float x = seedx * W + Mathf.Sin(t * 0.5f + i * 1.7f) * 4f; // 横にゆらぐ
                    float a = (0.04f + seedx * 0.03f) * fade;       // 0.04〜0.07（暗背景＝一段低い）
                    var col = new Color(0.92f, 0.90f, 0.85f, a);    // ニュートラル暖
                    DrawRect(new Rect2(x, y, 1f, 1f), col);
                }
            }
            else
            {
                // 近層：立ちのぼって流れる湯気/冷気の対流。上方向 22px/s（3ステージ最遅）、横に sin で大きくうねる。
                // α0.07〜0.12、1〜2px の縦ストランド10本。暖色寄りだが彩度は上げない。弾より遥かに遅い＝凪。
                const int strands = 10;
                float spd = 22f;
                float span = H + 28f;
                for (int i = 0; i < strands; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 27.3f) * 6571.13f);
                    float seedy = Frac(Mathf.Sin(i * 61.7f) * 3391.71f);
                    float baseX = seedx * W;
                    float phase = (seedy * span + t * spd) % span;
                    float y = H + 8f - phase;                       // 下から上へ立ちのぼる
                    // 上るほど横へうねって流れる（対流）。高い位置ほど振幅大。
                    float rise = Mathf.Clamp((H - y) / H, 0f, 1f);
                    float x = baseX + Mathf.Sin(t * 0.7f + i * 2.1f + rise * 3f) * (3f + rise * 7f);
                    float seg = 6f + seedx * 6f;                    // 6〜12px の短いストランド
                    float a = (0.07f + seedy * 0.05f) * fade * (0.4f + 0.6f * rise); // 上で濃く＝立ちのぼり
                    var col = new Color(0.93f, 0.91f, 0.87f, a);
                    float w = i % 3 == 0 ? 2f : 1f;                 // たまに太め（手前の対流）
                    DrawLine(new Vector2(x, y), new Vector2(x + Mathf.Sin(t + i) * 1.5f, y - seg), col, w);
                }
            }
        }

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
