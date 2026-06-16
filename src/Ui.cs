using Godot;

// Ui : ハブ/ショップ/難易度選択で共有する「ダークモードX風」UIの配色と描画ヘルパ。
//   低解像度(384x216)・ピクセルフォント前提。テキストは Label ではなく DrawString で描く
//   （配置をピクセル単位で完全制御するため）。角丸カードは StyleBoxFlat。
public static class Ui
{
    // ── 配色（Refrain of Light デザイントークン: スキャフォールド RefrainTheme 準拠）──
    //   深い夜紫のサーフェスに、役割色（浄化シアン／ミナ紫／穢れマゼンタ／HP桃／SCORE金）を載せる。
    //   面: BgDeep #070a16 / Panel #14101e。テキストは紫味の階調。
    public static readonly Color Bg = new("070a16");         // BgDeep 深い夜（最背面）
    public static readonly Color HeaderBg = new("0d0b1a");   // ヘッダ面（Panel より僅かに明）
    public static readonly Color Divider = new("241d36");    // 紫味の区切り線
    public static readonly Color Card = new("171225");       // カード面（Panel 近傍）
    public static readonly Color CardSel = new("241a38");    // 選択カード面
    public static readonly Color CardLocked = new("0f0b18"); // ロック面（沈める）
    public static readonly Color Border = new("2c2442");     // 枠線
    public static readonly Color Blue = new("6cbcd8");       // ＝Purify 浄化シアン（選択ハイライト/アクセント）
    public static readonly Color TextMain = new("eef1f6");   // Text
    public static readonly Color TextSub = new("c8b8d8");    // Text2（紫味）
    public static readonly Color TextMuted = new("8a7a9a");  // Text3
    public static readonly Color Mina = new("9a72d9");       // ミナ／BOMB＝紫
    public static readonly Color Like = new("e8769c");       // いいね＝HP桃
    public static readonly Color Repost = new("00ba7c");     // リポスト＝緑
    public static readonly Color Contam = new("e072ac");     // 汚染／穢れ＝マゼンタ（Kegare）
    public static readonly Color Burn = new("f2353d");       // 炎上＝赤
    public static readonly Color Ok = new("2ec78c");

    // ── 追加の役割色トークン（HUD 等で直接使う）──
    public static readonly Color Hp = new("e8769c");         // 体力＝桃
    public static readonly Color Bomb = new("9a72d9");       // ボム＝紫（＝Mina）
    public static readonly Color Purify = new("6cbcd8");     // 浄化／光＝シアン
    public static readonly Color PurifyHi = new("d7f3ff");   // 浄化100%の冴え
    public static readonly Color Kegare = new("e072ac");     // ボス穢れ＝マゼンタ
    public static readonly Color Score = new("e8c45a");      // SCORE／インプレ＝金
    public static readonly Color Light = new("ffd98a");      // 本人（光）＝淡い金
    public static readonly Color OutlineDark = new(0.027f, 0.039f, 0.086f, 0.78f); // HUD文字の暗縁（明背景でも読める）

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
