using Godot;

// UiKit : 非ピクセル（滑らか）UI 用の描画キット。
//   RefrainHTML（1280×720 設計）を Godot の Control/_Draw に忠実移植するための土台。
//   - 滑らかな TTF（Zen Kaku Gothic New 各ウェイト / JetBrains Mono）をAA付きで提供。
//   - 各UIシーンは _Draw 冒頭で BeginDesign() を呼び、以降 1280×720 の設計座標でそのまま描く
//     （内部解像度384×216へ自動スケール。canvas_items なのでウィンドウ実解像度でクッキリ描画される）。
//   - グラデ背景 / 放射グロウ / キーキャップ等のヘルパ。
public static class UiKit
{
    // 設計解像度（RefrainHTML の画面パネル）。内部解像度 384×216 への倍率 = 384/1280。
    public const float DesignW = 1280f, DesignH = 720f;
    public const float Scale = 384f / DesignW; // = 0.3

    // ── 役割色トークン（RefrainTheme / RefrainHTML 準拠）──
    public static readonly Color Hp      = new("e8769c");
    public static readonly Color Mina    = new("9a72d9");
    public static readonly Color Purify  = new("6cbcd8");
    public static readonly Color PurifyHi = new("d7f3ff");
    public static readonly Color Kegare  = new("e072ac");
    public static readonly Color Gold    = new("e8c45a");
    public static readonly Color Light   = new("ffd98a");
    public static readonly Color Info    = new("a6dcec"); // 見出しシアン
    public static readonly Color White   = new("ffffff");
    public static readonly Color Text2   = new("c8b8d8");
    public static readonly Color Text3   = new("8a7a9a");
    public static readonly Color Text4   = new("6b6478");
    public static readonly Color BgDeep  = new("070a16");
    public static readonly Color Ok      = new("2ec78c"); // リポスト緑/成功
    public static readonly Color Burn    = new("f2353d"); // 炎上赤

    // ── フォント（遅延ロード・AA付き）──
    private static FontFile? _zenR, _zenB, _zenBlack, _mono;
    private static FontFile Load(ref FontFile? slot, string path)
    {
        if (slot != null) return slot;
        slot = ResourceLoader.Load<FontFile>(path);
        if (slot != null)
        {
            slot.Antialiasing = TextServer.FontAntialiasing.Gray;
            slot.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
            slot.MultichannelSignedDistanceField = false;
        }
        return slot!;
    }
    public static FontFile Zen      => Load(ref _zenR, "res://assets/fonts/ZenKakuGothicNew-Regular.ttf");
    public static FontFile ZenBold  => Load(ref _zenB, "res://assets/fonts/ZenKakuGothicNew-Bold.ttf");
    public static FontFile ZenBlack => Load(ref _zenBlack, "res://assets/fonts/ZenKakuGothicNew-Black.ttf");
    public static FontFile Mono     => Load(ref _mono, "res://assets/fonts/JetBrainsMono.ttf");

    // ── 設計座標モードの開始/終了 ──
    // 以降の Draw 呼び出しを 1280×720 設計座標で行えるようスケール変換をかける。
    public static void BeginDesign(CanvasItem ci) => ci.DrawSetTransform(Vector2.Zero, 0f, new Vector2(Scale, Scale));
    public static void EndDesign(CanvasItem ci) => ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

    // ── テキスト（設計座標・上端基準）──
    public static void Text(CanvasItem ci, Font f, Vector2 topLeft, string s, int size, Color c,
        HorizontalAlignment al = HorizontalAlignment.Left, float width = -1f)
    {
        float asc = f.GetAscent(size);
        ci.DrawString(f, new Vector2(topLeft.X, topLeft.Y + asc), s, al, width, size, c);
    }

    public static void Multi(CanvasItem ci, Font f, Vector2 topLeft, string s, int size, Color c, float width, int maxLines = -1)
    {
        float asc = f.GetAscent(size);
        ci.DrawMultilineString(f, new Vector2(topLeft.X, topLeft.Y + asc), s, HorizontalAlignment.Left, width, size, maxLines, c,
            TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.GraphemeBound);
    }

    public static float TextW(Font f, string s, int size) => f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;

    // ── 角丸ボックス（border/塗り）──
    public static void Box(CanvasItem ci, Rect2 r, Color? bg, float radius, Color? border = null, float borderW = 0f)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg ?? new Color(0, 0, 0, 0),
            CornerRadiusTopLeft = (int)radius, CornerRadiusTopRight = (int)radius,
            CornerRadiusBottomLeft = (int)radius, CornerRadiusBottomRight = (int)radius,
            AntiAliasing = true,
        };
        if (border is Color bc && borderW > 0f) { sb.BorderColor = bc; sb.SetBorderWidthAll((int)Mathf.Max(1, borderW)); }
        ci.DrawStyleBox(sb, r);
    }

    // ── 縦リニアグラデ矩形（上→下に色を補間）──
    public static void VGradient(CanvasItem ci, Rect2 r, Color[] colors, float[] offsets)
    {
        var g = new Gradient { Offsets = offsets, Colors = colors };
        var tex = new GradientTexture2D
        {
            Gradient = g, Width = 8, Height = 256,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1),
        };
        ci.DrawTextureRect(tex, r, false);
    }

    // ── 放射グロウ（中心色→透明）。rect 全体に円形グラデを敷く ──
    public static void RadialGlow(CanvasItem ci, Vector2 center, float radius, Color inner, float innerAlpha = -1f)
    {
        if (innerAlpha >= 0f) inner = new Color(inner.R, inner.G, inner.B, innerAlpha);
        var g = new Gradient
        {
            Offsets = new[] { 0f, 1f },
            Colors = new[] { inner, new Color(inner.R, inner.G, inner.B, 0f) },
        };
        var tex = new GradientTexture2D
        {
            Gradient = g, Width = 128, Height = 128,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        };
        ci.DrawTextureRect(tex, new Rect2(center.X - radius, center.Y - radius, radius * 2, radius * 2), false);
    }

    // ── アバター（丸＋頭文字）──
    public static void Avatar(CanvasItem ci, Vector2 center, float r, Color col, string initial)
    {
        ci.DrawCircle(center, r, col);
        int size = Mathf.Max(10, (int)(r * 1.1f));
        float asc = ZenBold.GetAscent(size), desc = ZenBold.GetDescent(size);
        float w = TextW(ZenBold, initial, size);
        ci.DrawString(ZenBold, new Vector2(center.X - w / 2f, center.Y + (asc - desc) / 2f), initial,
            HorizontalAlignment.Left, -1, size, new Color(0.06f, 0.05f, 0.10f, 0.92f));
    }

    // 1000以上を 1.2k / 3.4M に省略。
    public static string Abbrev(long n)
    {
        if (n >= 1_000_000) return (n / 1_000_000.0).ToString("0.0") + "M";
        if (n >= 1_000) return (n / 1_000.0).ToString("0.0") + "k";
        return n.ToString();
    }

    // ── キーキャップ（Z や ↑↓ の角丸チップ）。中央寄せのモノ文字 ──
    public static void Key(CanvasItem ci, Vector2 pos, string label, Color bg, Color border, Color textCol, float h = 24f, float minW = 24f)
    {
        float pad = 12f;
        float w = Mathf.Max(minW, TextW(Mono, label, 12) + pad);
        Box(ci, new Rect2(pos.X, pos.Y, w, h), bg, 6f, border, 1f);
        float asc = Mono.GetAscent(12), desc = Mono.GetDescent(12);
        ci.DrawString(Mono, new Vector2(pos.X, pos.Y + (h + asc - desc) / 2f), label, HorizontalAlignment.Center, w, 12, textCol);
    }
}
