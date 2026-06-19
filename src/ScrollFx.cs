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
// 弾幕の視認性最優先：α上限0.18・ほぼ無彩色(白〜淡い同系色)・通常合成(加算しない)・線/粒1〜2px。
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

    // 二層を別ノードに分けて ZIndex を個別に持たせる（同一 _Draw だと ZIndex を分けられないため）。
    private Layer _far = null!;
    private Layer _near = null!;

    public override void _Ready()
    {
        ZIndex = -60;
        ZAsRelative = false;
        AddToGroup("scrollfx");
        _far = new Layer { Name = "Far", Owner2 = this, Near = false, ZIndex = -60, ZAsRelative = false };
        _near = new Layer { Name = "Near", Owner2 = this, Near = true, ZIndex = -55, ZAsRelative = false };
        AddChild(_far);
        AddChild(_near);
    }

    public override void _Process(double delta)
    {
        _t += delta;
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
                const int count = 24;
                float spd = 30f;
                for (int i = 0; i < count; i++)
                {
                    float seedx = Frac(Mathf.Sin(i * 12.9898f) * 43758.5453f);
                    float seedy = Frac(Mathf.Sin(i * 78.233f) * 12543.713f);
                    float x = seedx * W;
                    float span = H + 24f;
                    float y = (seedy * span + t * spd) % span - 12f;
                    float a = (0.06f + seedx * 0.04f) * fade; // 0.06〜0.10
                    var col = new Color(0.80f, 0.87f, 1f, a);
                    // 半分は点、半分は短い 1px 縦ストリーク（読めない粒感）。
                    if (i % 2 == 0)
                        DrawRect(new Rect2(x, y, 1f, 1f), col);
                    else
                        DrawLine(new Vector2(x, y), new Vector2(x, y + 3f), col, 1f);
                }
            }
            else
            {
                // 近層：下へ流れる横罫線（順位掲示板のスキャンライン）。70px/s（遠の約2.3倍）、
                // α0.12〜0.16、長さ8〜16px の横ストリークを段組みで。16 本。
                const int rows = 16;
                float spd = 70f;
                float gap = H / rows;
                float scroll = (t * spd) % gap;
                for (int r = -1; r < rows + 1; r++)
                {
                    float y = r * gap + scroll;
                    float seed = Frac(Mathf.Sin(r * 34.17f) * 9817.31f);
                    float x = seed * (W - 16f);
                    float len = 8f + seed * 8f;                  // 8〜16px
                    float a = (0.12f + seed * 0.04f) * fade;     // 0.12〜0.16
                    var col = new Color(0.78f, 0.85f, 1f, a);
                    DrawLine(new Vector2(x, y), new Vector2(x + len, y), col, 1f);
                    // たまに 2px の太め罫線（手前感）。
                    if (r % 4 == 0)
                    {
                        float x2 = Frac(seed + 0.37f) * (W - 24f);
                        DrawLine(new Vector2(x2, y + gap * 0.5f), new Vector2(x2 + 14f, y + gap * 0.5f),
                            new Color(0.80f, 0.86f, 1f, a * 0.9f), 2f);
                    }
                }
            }
        }

        // ---- STAGE2 あかり（後日）：手前を速く落ちる太い雨筋でパララックス ----
        private void DrawAkari(float fade, float t) { }

        // ---- STAGE3 こはる（後日）：湯気・埃のゆるい対流 ----
        private void DrawKoharu(float fade, float t) { }

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
