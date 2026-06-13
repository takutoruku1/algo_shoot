using Godot;

// Ui : ハブ/ショップ/難易度選択で共有する「ダークモードX風」UIの配色と描画ヘルパ。
//   低解像度(384x216)・ピクセルフォント前提。テキストは Label ではなく DrawString で描く
//   （配置をピクセル単位で完全制御するため）。角丸カードは StyleBoxFlat。
public static class Ui
{
    // ── 配色（X ダークモード基調）──
    public static readonly Color Bg = new(0.075f, 0.086f, 0.102f);     // ほぼ黒（青み）
    public static readonly Color HeaderBg = new(0.098f, 0.110f, 0.129f);
    public static readonly Color Divider = new(0.18f, 0.20f, 0.23f);
    public static readonly Color Card = new(0.118f, 0.133f, 0.157f);
    public static readonly Color CardSel = new(0.150f, 0.180f, 0.220f);
    public static readonly Color CardLocked = new(0.092f, 0.100f, 0.115f);
    public static readonly Color Border = new(0.20f, 0.23f, 0.27f);
    public static readonly Color Blue = new(0.114f, 0.608f, 0.941f);    // X ブルー #1d9bf0
    public static readonly Color TextMain = new(0.906f, 0.914f, 0.918f);
    public static readonly Color TextSub = new(0.62f, 0.65f, 0.69f);
    public static readonly Color TextMuted = new(0.42f, 0.46f, 0.50f);
    public static readonly Color Mina = new(0.83f, 0.46f, 0.72f);       // ミナ＝桃紫
    public static readonly Color Like = new(0.97f, 0.18f, 0.52f);       // いいね
    public static readonly Color Repost = new(0.0f, 0.73f, 0.49f);      // リポスト
    public static readonly Color Contam = new(0.55f, 0.33f, 0.78f);     // 汚染
    public static readonly Color Burn = new(0.96f, 0.20f, 0.24f);       // 炎上
    public static readonly Color Ok = new(0.18f, 0.78f, 0.55f);

    // アカウント別アバター色。
    public static Color AccountColor(string id) => id switch
    {
        "mina" => Mina,
        "rei" => new Color(0.90f, 0.52f, 0.38f),
        "akari" => new Color(0.40f, 0.62f, 0.88f),
        "koharu" => new Color(0.46f, 0.74f, 0.52f),
        _ => Contam,
    };

    // ── テキスト ──
    public static void Text(CanvasItem ci, Font f, Vector2 topLeft, string s, int size, Color c,
        HorizontalAlignment al = HorizontalAlignment.Left, float width = -1f)
    {
        float asc = f.GetAscent(size);
        ci.DrawString(f, new Vector2(topLeft.X, topLeft.Y + asc), s, al, width, size, c);
    }

    public static void MultiText(CanvasItem ci, Font f, Vector2 topLeft, string s, int size, Color c,
        float width, int maxLines)
    {
        float asc = f.GetAscent(size);
        ci.DrawMultilineString(f, new Vector2(topLeft.X, topLeft.Y + asc), s,
            HorizontalAlignment.Left, width, size, maxLines, c);
    }

    public static float TextW(Font f, string s, int size) => f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;

    // 1000以上を 1.2k / 3.4M に省略。
    public static string Abbrev(long n)
    {
        if (n >= 1_000_000) return (n / 1_000_000.0).ToString("0.0") + "M";
        if (n >= 1_000) return (n / 1_000.0).ToString("0.0") + "k";
        return n.ToString();
    }

    // ── 角丸ボックス（カード/ピル）──
    public static void Box(CanvasItem ci, Rect2 r, Color bg, float radius, Color? border = null, float borderW = 0f)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            CornerRadiusTopLeft = (int)radius,
            CornerRadiusTopRight = (int)radius,
            CornerRadiusBottomLeft = (int)radius,
            CornerRadiusBottomRight = (int)radius,
            AntiAliasing = true,
        };
        if (border is Color bc && borderW > 0f)
        {
            sb.BorderColor = bc;
            sb.SetBorderWidthAll((int)borderW);
        }
        ci.DrawStyleBox(sb, r);
    }

    // アバター（丸＋頭文字）。
    public static void Avatar(CanvasItem ci, Font f, Vector2 center, float r, Color col, string initial)
    {
        ci.DrawCircle(center, r, col);
        int size = Mathf.Max(8, (int)(r * 1.35f));
        float asc = f.GetAscent(size), desc = f.GetDescent(size);
        float w = TextW(f, initial, size);
        ci.DrawString(f, new Vector2(center.X - w / 2f, center.Y + (asc - desc) / 2f - 0.5f),
            initial, HorizontalAlignment.Left, -1, size, new Color(0.07f, 0.08f, 0.10f, 0.92f));
    }

    // ── エンゲージメントの小アイコン（約5px）──
    public static void IconReply(CanvasItem ci, Vector2 c, Color col)
    {
        // 吹き出し（角丸枠＋小さな尻尾）
        Box(ci, new Rect2(c.X - 2.5f, c.Y - 2.0f, 5f, 3.6f), new Color(0, 0, 0, 0), 1.2f, col, 0.8f);
        ci.DrawColoredPolygon(new[] { new Vector2(c.X - 1.4f, c.Y + 1.6f), new Vector2(c.X - 0.2f, c.Y + 1.6f), new Vector2(c.X - 1.4f, c.Y + 3.0f) }, col);
    }

    public static void IconRepost(CanvasItem ci, Vector2 c, Color col)
    {
        // 上下2本の矢印（リポスト）
        ci.DrawLine(new Vector2(c.X - 2.2f, c.Y - 1.4f), new Vector2(c.X + 1.6f, c.Y - 1.4f), col, 0.9f);
        ci.DrawColoredPolygon(new[] { new Vector2(c.X + 1.4f, c.Y - 2.4f), new Vector2(c.X + 2.6f, c.Y - 1.4f), new Vector2(c.X + 1.4f, c.Y - 0.4f) }, col);
        ci.DrawLine(new Vector2(c.X + 2.2f, c.Y + 1.4f), new Vector2(c.X - 1.6f, c.Y + 1.4f), col, 0.9f);
        ci.DrawColoredPolygon(new[] { new Vector2(c.X - 1.4f, c.Y + 0.4f), new Vector2(c.X - 2.6f, c.Y + 1.4f), new Vector2(c.X - 1.4f, c.Y + 2.4f) }, col);
    }

    public static void IconLike(CanvasItem ci, Vector2 c, Color col)
    {
        // ハート（左右の丸＋下の三角）
        ci.DrawCircle(new Vector2(c.X - 1.2f, c.Y - 0.8f), 1.5f, col);
        ci.DrawCircle(new Vector2(c.X + 1.2f, c.Y - 0.8f), 1.5f, col);
        ci.DrawColoredPolygon(new[] { new Vector2(c.X - 2.5f, c.Y - 0.2f), new Vector2(c.X + 2.5f, c.Y - 0.2f), new Vector2(c.X, c.Y + 2.6f) }, col);
    }

    // 小さなアイコン＋数値（エンゲージメント1項目）。次のXを返す。
    public static float Engagement(CanvasItem ci, Font f, float x, float y, int kind, long count, Color col)
    {
        var c = new Vector2(x + 2.5f, y + 3f);
        switch (kind)
        {
            case 0: IconReply(ci, c, col); break;
            case 1: IconRepost(ci, c, col); break;
            default: IconLike(ci, c, col); break;
        }
        Text(ci, f, new Vector2(x + 7f, y - 1f), Abbrev(count), 8, col);
        return x + 7f + TextW(f, Abbrev(count), 8) + 9f;
    }
}
