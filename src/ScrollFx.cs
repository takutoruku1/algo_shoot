using Godot;

// ScrollFx : 額装背景(ZIndex -90 静止)とゲーム要素(0..)の間に置く「近景パララックス層」。
//   自機は画面内をほぼ留まるので、背景を左へ流すことで「自機が右へ前進している」前進感を作る。
//   （StageBackground と同じ「左へ流れる＝左→右に進む」方向に統一。縦の上昇感は出さない。）
//   額装背景(board.png)はループさせない（心象世界の「囚われた一場面」）。流れるのはこの近景層だけ。
//
// 二段構成で速度差（パララックス）を作る：
//   遠層 ZIndex -60 : 遅い・薄い・細かい粒（読めない遠景）
//   近層 ZIndex -55 : 速い(遠の約2.3倍)・やや濃い・横ストリーク（手前を過ぎ去る）
//   ※ 既存 StageImagery(ZIndex -50) より奥。弾(0..)には絶対かぶらない。
//
// 弾幕の視認性最優先：α上限0.21・ほぼ無彩色(白〜淡い同系色／弾の濃ピンク・黒グリフと色相を分離)・
//   通常合成(加算しない)・線/粒1〜2px。実機で弾より目立たないことを確認済み。
// Warmth(浄化進行)に連動：晴れるほど α を下げ速度を落とす（流れが凪ぐ）。StageImagery と同じ
//   fade = 1f - Warmth の語彙で統一。
//
// すべての流れは左方向（-X）に統一＝右への前進感。パララックス速度差は横方向で維持。
// Kind 分岐で各ステージへ横展開：
//   Rei   : 順位掲示板の海。遠＝読めない順位の光点が左へ、近＝左へ流れる横ストリーク（スキャンライン）。Cold=青系。
//   Akari : 手前を速く左へ流れる斜めの雨筋（縦落下より横流れを主役に）／奥のかすかな降りこめ。
//   Koharu: 左へたなびく湯気/冷気の横ストランド／宙を漂う埃。
public partial class ScrollFx : Node2D
{
    public enum StageKind { Rei, Akari, Koharu }
    public StageKind Kind = StageKind.Rei;

    private const float W = 384f, H = 216f;
    private double _t;

    // ───── 主役：生成スクロール背景（テクスチャを左へ高速ループ）─────
    // 額装背景(board.png, ZIndex -90 静止)の上、弾(0..)の奥(負ZIndex)に置く。
    // 2枚のスプライトを横に並べ、毎フレーム左へ動かし、画面外へ出たら右へ巻き戻す＝シームレス無限スクロール。
    // テクスチャは暗い濃紺ベース＋シアンの光帯で、左右端を暗く均一に作ってあるので継ぎ目が見えない
    // （※継ぎ目が出るアートは後日 tileable に差し替え。方向を左流れに揃えるのが今回の主目的）。
    // 速度は大きめ（明確に「流れている」と分かる量）。Warmth(浄化)で少し落ち着く。
    private Sprite2D[] _scrollTiles = System.Array.Empty<Sprite2D>();
    private float _scrollTexW;       // 1枚分の表示幅(px)
    private float _scrollX;          // ループ用オフセット(0.._scrollTexW)
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
        StageKind.Rei    => ("res://char/bg/rei/scroll.png",     60f), // 順位の層が左へ流れる（前進感／画面酔い対策で半減）
        StageKind.Akari  => ("res://char/bg/akari/scroll.png",   75f), // 窓の外を流れる雨の景色（最速／半減）
        StageKind.Koharu => ("res://char/bg/koharu/scroll.png",  32f), // 湯気/光がゆっくり左へ流れる（穏やかだが動く／半減）
        _ => ("", 0f),
    };

    private void SetupScrollTexture()
    {
        var (path, speed) = ScrollDef;
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path)) return;
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null) return;

        _scrollBaseSpeed = speed;
        // 画面高(216)に合わせて等倍スケール（StageBackground と同じ＝高さフィット）。幅はアスペクト維持。
        float scale = H / tex.GetHeight();
        _scrollTexW = tex.GetWidth() * scale;

        // 画面幅(384)を覆うのに必要なタイル枚数＋1（ループの巻き戻し用に最低2枚）。
        int count = Mathf.Max(2, Mathf.CeilToInt(W / _scrollTexW) + 1);
        var tiles = new Sprite2D[count];
        for (int i = 0; i < count; i++)
        {
            var spr = new Sprite2D
            {
                Name = $"Scroll{i}",
                Texture = tex,
                Centered = false,
                Scale = new Vector2(scale, scale),
                Position = new Vector2(i * _scrollTexW, 0f),
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

        // ── 主役のテクスチャを左へスクロール（左→右の前進感。StageBackground と同じ向き）──
        if (_scrollTiles.Length > 0)
        {
            float warm = Mathf.Clamp(Warmth, 0f, 1f);
            // 浄化が進むほど少し落ち着くが、止めない（最低でも55%は流れ続ける＝常に動いて見える）。
            // 位置係数 scrollMul を乗算で共存：右にいるほど手前の景色も速く流れて一体感（StageBackground と同じ 0.65..1.45）。
            float scrollMul = 0.65f + 0.80f * BgScroll.PlayerNx(this);
            float speed = _scrollBaseSpeed * (1f - 0.45f * warm) * scrollMul;
            _scrollX += speed * (float)delta;
            float off = _scrollX % _scrollTexW; // 0.._scrollTexW の連続オフセット
            for (int i = 0; i < _scrollTiles.Length; i++)
            {
                var p = _scrollTiles[i].Position;
                p.X = i * _scrollTexW - off;
                if (p.X <= -_scrollTexW) p.X += _scrollTiles.Length * _scrollTexW; // 左へ出た列を右端へ
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

        // ---- STAGE1 レイ：左へ通り過ぎる順位の光点（遠）／左へ流れる横ストリーク罫線（近）。Cold=青系。----
        private void DrawRei(float fade, float t)
        {
            var ci = GetCanvasItem();
            if (!Near)
            {
                // 遠層：読めない順位の光点が左へ流れる。左方向 21px/s、α0.10〜0.15、1px の点／短い横ストリーク。
                // 画面に 36 個。決定論的に散らす（疑似乱数を i から作る）。y は固定、x だけ左へ送る。
                // 弾(濃いピンク/黒グリフ＝高コントラスト)とは色相が違い埋もれない。
                const int count = 36;
                float spd = 21f;                  // 半減（画面酔い対策）
                float span = W + 24f;
                for (int i = 0; i < count; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 12.9898f) * 43758.5453f);
                    float seedy = Frac(Mathf.Sin(i * 78.233f) * 12543.713f);
                    float x = (seedx * span - t * spd) % span;
                    if (x < 0) x += span;
                    x -= 12f;
                    float y = seedy * H;
                    float a = (0.10f + seedx * 0.05f) * fade; // 0.10〜0.15
                    var col = new Color(0.80f, 0.87f, 1f, a);
                    // 半分は点、半分は短い 1px 横ストリーク（流れの手掛かり）。
                    if (i % 2 == 0)
                        DrawRect(new Rect2(x, y, 1f, 1f), col);
                    else
                        DrawLine(new Vector2(x, y), new Vector2(x + 5f, y), col, 1f);
                }
            }
            else
            {
                // 近層：左へ流れる横ストリーク罫線（順位掲示板のスキャンライン）。48px/s（遠の約2.3倍）、
                // α0.16〜0.21、長さ10〜22px の横ストリークを段組みで。20 本。
                // 罫線は段(行)に固定して並べ、x を左へ送って「過ぎ去る」流れを作る。
                const int rows = 20;
                float spd = 48f;                  // 半減（遠の約2.3倍比は維持）
                float gap = H / rows;
                float span = W + 24f;
                for (int r = 0; r < rows; r++)
                {
                    float y = r * gap + gap * 0.5f;
                    float seed = Frac(Mathf.Sin(r * 34.17f) * 9817.31f);
                    float x = (seed * span - t * spd) % span;
                    if (x < 0) x += span;
                    x -= 12f;
                    float len = 10f + seed * 12f;                 // 10〜22px（流れの手掛かりを長く）
                    float a = (0.16f + seed * 0.05f) * fade;      // 0.16〜0.21
                    var col = new Color(0.78f, 0.85f, 1f, a);
                    DrawLine(new Vector2(x, y), new Vector2(x + len, y), col, 1f);
                    // たまに 2px の太め罫線（手前感）。2本目を逆位相で左へ。
                    if (r % 4 == 0)
                    {
                        float x2 = (Frac(seed + 0.37f) * span - t * spd) % span;
                        if (x2 < 0) x2 += span;
                        x2 -= 12f;
                        DrawLine(new Vector2(x2, y + gap * 0.5f), new Vector2(x2 + 16f, y + gap * 0.5f),
                            new Color(0.80f, 0.86f, 1f, a * 0.9f), 2f);
                    }
                }
            }
        }

        // ---- STAGE2 あかり：左へ強く流れる斜めの雨筋（近）／奥のかすかな降りこめ・埃（遠）。----
        // classroom.png は寒色・中明度で、既存 StageImagery 側に左へ流れる雨(ScrollFx と同方向)がある。
        // 進行＝横流れを優先：雨は「左へ強く流れる斜め筋」にし、縦移動が主役にならないようにする。
        // 速度差は横方向で維持（手前(近層)＝速い・太い／奥(遠層)＝遅い・薄い・点）。
        // 色は教室の寒色〜ニュートラル(0.78〜0.82, 0.84〜0.88, 1.0)。彩度は上げない。中明度背景なのでレイに近いαで読める。
        private void DrawAkari(float fade, float t)
        {
            if (!Near)
            {
                // 遠層：奥のかすかな降りこめ／漂う埃が左へ流れる。左方向 25px/s、α0.05〜0.09、1px の点。28個。
                // 既存雨(StageImagery)より遥かに薄い＝「ずっと奥で流れている」遠景。決定論的に散らす。
                const int count = 28;
                float spd = 25f;                  // 半減（画面酔い対策）
                float span = W + 16f;
                for (int i = 0; i < count; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 12.9898f) * 43758.5453f);
                    float seedy = Frac(Mathf.Sin(i * 78.233f) * 12543.713f);
                    float x = (seedx * span - t * spd) % span;
                    if (x < 0) x += span;
                    x -= 8f;
                    float y = seedy * H;
                    float a = (0.05f + seedx * 0.04f) * fade;       // 0.05〜0.09
                    var col = new Color(0.80f, 0.86f, 1f, a);
                    // 大半は漂う埃(点)、たまに左へ流れる 1px の短い降りこめ（横長＋わずかに落下）。
                    if (i % 3 == 0)
                        DrawLine(new Vector2(x, y), new Vector2(x - 4f, y + 1f), col, 1f);
                    else
                        DrawRect(new Rect2(x, y, 1f, 1f), col);
                }
            }
            else
            {
                // 近層：手前を速く左へ流れる斜めの雨筋。120px/s（遠の約5倍＝手前感）、
                // α0.11〜0.16、長さ12〜22px・2px幅・横へ寝た斜め(横:縦 ≈ 4:1)。16本。
                // 横移動が主役＝前進感。縦の落下成分は弱く（+3px だけ）して上昇/落下を感じさせない。
                const int streaks = 16;
                float spd = 120f;                 // 半減（遠の約5倍比は維持）
                float span = W + 28f;
                for (int i = 0; i < streaks; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 31.7f) * 8123.17f);
                    float seedy = Frac(Mathf.Sin(i * 53.91f) * 4517.93f);
                    float x = (seedx * span - t * spd) % span;
                    if (x < 0) x += span;
                    x -= 14f;
                    float y = seedy * H;
                    float len = 12f + seedx * 10f;                   // 12〜22px（横長）
                    float a = (0.11f + seedy * 0.05f) * fade;        // 0.11〜0.16
                    var col = new Color(0.80f, 0.85f, 1f, a);
                    // 横へ寝た斜め筋：左へ -len、わずかに下へ +len*0.25（落下より横流れが主役）。
                    DrawLine(new Vector2(x, y), new Vector2(x - len, y + len * 0.25f), col, 2f);
                }
            }
        }

        // ---- STAGE3 こはる：左へたなびく湯気/冷気の横ストランド（近）／宙を漂う小さな埃（遠）。----
        // kitchen.png は深い紺・暗め(低明度)。3ステージ中いちばん遅め＝「凪いだ空気だけが動く」。
        // 暖気は本来立ちのぼるが、進行＝横流れを優先：左へたなびく横基調にして上昇感を出さない。
        // 暗い背景なので α はレイ/あかりより一段下げる（主張しすぎ防止）。暖色寄りだが彩度は上げない(0.90台のニュートラル暖)。
        private void DrawKoharu(float fade, float t)
        {
            if (!Near)
            {
                // 遠層：宙を漂う小さな埃が左へゆっくり流れる。左方向 7px/s（最も遅い＝漂うだけ）、
                // 縦方向に sin で微かに揺れる。α0.04〜0.07、1px、22個。
                const int count = 22;
                float spd = 7f;                   // 半減（最も遅い＝漂うだけ）
                float span = W + 12f;
                for (int i = 0; i < count; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 12.9898f) * 43758.5453f);
                    float seedy = Frac(Mathf.Sin(i * 78.233f) * 12543.713f);
                    float baseX = (seedx * span - t * spd) % span;  // ごくゆっくり左へ漂う
                    if (baseX < 0) baseX += span;
                    float x = baseX - 6f;
                    float y = seedy * H + Mathf.Sin(t * 0.5f + i * 1.7f) * 4f; // 縦に微かにゆらぐ
                    float a = (0.04f + seedx * 0.03f) * fade;       // 0.04〜0.07（暗背景＝一段低い）
                    var col = new Color(0.92f, 0.90f, 0.85f, a);    // ニュートラル暖
                    DrawRect(new Rect2(x, y, 1f, 1f), col);
                }
            }
            else
            {
                // 近層：左へたなびいて流れる湯気/冷気のストランド。左方向 17px/s（3ステージ最遅）、縦に sin で大きくうねる。
                // α0.07〜0.12、1〜2px の横ストランド10本。暖色寄りだが彩度は上げない。弾より遥かに遅い＝凪。
                const int strands = 10;
                float spd = 17f;                  // 半減（3ステージ最遅）
                float span = W + 28f;
                for (int i = 0; i < strands; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 27.3f) * 6571.13f);
                    float seedy = Frac(Mathf.Sin(i * 61.7f) * 3391.71f);
                    float baseY = seedy * H;
                    float phase = (seedx * span + t * spd) % span;
                    float x = W + 8f - phase;                       // 右から左へたなびく
                    // 左へ流れるほど縦へうねる（対流）。左に行くほど振幅大＝消えゆくたなびき。
                    float drift = Mathf.Clamp((W - x) / W, 0f, 1f);
                    float y = baseY + Mathf.Sin(t * 0.7f + i * 2.1f + drift * 3f) * (3f + drift * 7f);
                    float seg = 6f + seedx * 6f;                    // 6〜12px の短い横ストランド
                    float a = (0.07f + seedy * 0.05f) * fade * (0.4f + 0.6f * drift); // 左で濃く＝たなびき
                    var col = new Color(0.93f, 0.91f, 0.87f, a);
                    float w = i % 3 == 0 ? 2f : 1f;                 // たまに太め（手前の対流）
                    DrawLine(new Vector2(x, y), new Vector2(x - seg, y + Mathf.Sin(t + i) * 1.5f), col, w);
                }
            }
        }

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
