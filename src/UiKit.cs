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

    // 行間（leading）を足して読みやすくした複数行描画。会話ボックス用。
    //   width で語境界折り返し。各行は font 高さ + extraLeading(px) 間隔で送る。
    //   maxLines>0 ならその行数で打ち切り（末尾「…」は付けない＝会話は1画面に収まる前提で行数を増やす）。
    //   返り値：描画に使った総高さ（box 高さの算出に使える）。
    public static float MultiLeading(CanvasItem ci, Font f, Vector2 topLeft, string s, int size, Color c,
        float width, float extraLeading, int maxLines = -1)
    {
        var lines = WrapLines(f, s, size, width);
        float lineH = f.GetHeight(size) + extraLeading;
        float asc = f.GetAscent(size);
        int n = maxLines > 0 ? Mathf.Min(maxLines, lines.Count) : lines.Count;
        for (int i = 0; i < n; i++)
            ci.DrawString(f, new Vector2(topLeft.X, topLeft.Y + asc + i * lineH), lines[i],
                HorizontalAlignment.Left, -1, size, c);
        return n * lineH;
    }

    // 語境界（日本語は文字境界）で width に収まるよう折り返す。明示改行 '\n' は尊重。
    private static System.Collections.Generic.List<string> WrapLines(Font f, string s, int size, float width)
    {
        var outLines = new System.Collections.Generic.List<string>();
        foreach (var para in s.Split('\n'))
        {
            if (para.Length == 0) { outLines.Add(""); continue; }
            var cur = new System.Text.StringBuilder();
            foreach (var ch in para)
            {
                string trial = cur.ToString() + ch;
                if (TextW(f, trial, size) > width && cur.Length > 0)
                {
                    outLines.Add(cur.ToString());
                    cur.Clear();
                }
                cur.Append(ch);
            }
            if (cur.Length > 0) outLines.Add(cur.ToString());
        }
        return outLines;
    }

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

    // ── 顔アバター（円形クリップした立ち絵の頭部 ＋ アカウント色リング）──
    //   face!=null : 下敷き色円(r+2) → 32角形の円ファン+UV（縦長立ち絵の頭部だけを正方窓で抜き、円に内接させる）→ 選択時グロウ。
    //                window: x=0, y=topCrop, w=1.0, h=tw/th（縦長画像の上部・幅いっぱいを正方サンプリング＝顔が円に収まる）。
    //   face==null : ロック表示（暗円＋グレーリング＋"?"）にフォールバック。
    public static void FaceAvatar(CanvasItem ci, Vector2 center, float r, Texture2D? face, Color ringCol,
        bool selected, float topCrop = 0.06f, float alpha = 1f, double t = 0)
    {
        const int seg = 32;
        if (face == null)
        {
            // ロック：暗円 → グレーリング → "?"
            ci.DrawCircle(center, r, new Color(0.10f, 0.09f, 0.14f, 0.9f * alpha));
            DrawRing(ci, center, r, seg, new Color(0.45f, 0.42f, 0.52f, 0.7f * alpha), 2f);
            int qs = Mathf.Max(12, (int)(r * 1.2f));
            float qasc = ZenBold.GetAscent(qs), qdesc = ZenBold.GetDescent(qs);
            float qw = TextW(ZenBold, "?", qs);
            ci.DrawString(ZenBold, new Vector2(center.X - qw / 2f, center.Y + (qasc - qdesc) / 2f), "?",
                HorizontalAlignment.Left, -1, qs, new Color(0.62f, 0.58f, 0.70f, alpha));
            return;
        }

        // 選択時の背面グロウ（外側に淡くにじむ・呼吸）
        if (selected)
        {
            float pulse = 0.18f + 0.10f * Mathf.Sin((float)t * 2.4f);
            RadialGlow(ci, center, r * 1.9f, ringCol, pulse * alpha);
        }

        // 下敷き色円（隙間からの背景抜けを隠す）
        ci.DrawCircle(center, r + 2f, new Color(ringCol.R * 0.5f, ringCol.G * 0.5f, ringCol.B * 0.5f, 0.9f * alpha));

        // 円ファン（中心＋外周 seg+1 点）＋ UV（縦長立ち絵の頭部正方窓）
        int tw = face.GetWidth(), th = face.GetHeight();
        float winH = Mathf.Min(1f, (float)tw / Mathf.Max(1, th)); // 正方サンプル窓の高さ（UV）
        float winY = topCrop;
        var pts = new Vector2[seg + 2];
        var uvs = new Vector2[seg + 2];
        pts[0] = center;
        uvs[0] = new Vector2(0.5f, winY + winH * 0.5f);
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.Tau - Mathf.Pi * 0.5f; // 上から時計回り
            float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
            pts[i + 1] = new Vector2(center.X + cx * r, center.Y + cy * r);
            // 円内 [-1,1] → UV窓へ。x:0→1, y:winY→winY+winH。
            uvs[i + 1] = new Vector2(0.5f + cx * 0.5f, winY + winH * (0.5f + cy * 0.5f));
        }
        var modulate = new Color(1, 1, 1, alpha);
        var cols = new Color[pts.Length];
        for (int i = 0; i < cols.Length; i++) cols[i] = modulate;
        ci.DrawPolygon(pts, cols, uvs, face);

        // アカウント色リング
        DrawRing(ci, center, r, seg, ringCol with { A = (selected ? 1f : 0.85f) * alpha }, selected ? 2.4f : 1.8f);
    }

    private static void DrawRing(CanvasItem ci, Vector2 center, float r, int seg, Color col, float width)
    {
        var ring = new Vector2[seg + 1];
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.Tau;
            ring[i] = new Vector2(center.X + Mathf.Cos(a) * r, center.Y + Mathf.Sin(a) * r);
        }
        ci.DrawPolyline(ring, col, width, true);
    }

    // ── 認証バッジ（X風の塗り円＋白チェック）。center は円中心、r は円半径。──
    public static void VerifiedBadge(CanvasItem ci, Vector2 center, float r, Color col, float alpha = 1f)
    {
        ci.DrawCircle(center, r, col with { A = col.A * alpha });
        // 白チェック（√型の2線）
        float s = r * 0.62f;
        var p0 = new Vector2(center.X - s * 0.72f, center.Y + s * 0.06f);
        var p1 = new Vector2(center.X - s * 0.16f, center.Y + s * 0.56f);
        var p2 = new Vector2(center.X + s * 0.78f, center.Y - s * 0.52f);
        var w = new Color(1, 1, 1, alpha);
        ci.DrawLine(p0, p1, w, Mathf.Max(1.2f, r * 0.28f), true);
        ci.DrawLine(p1, p2, w, Mathf.Max(1.2f, r * 0.28f), true);
    }

    // ── ハート（HP）──
    public static void Heart(CanvasItem ci, Vector2 c, float r, Color col)
    {
        ci.DrawCircle(new Vector2(c.X - r * 0.42f, c.Y - r * 0.28f), r * 0.54f, col);
        ci.DrawCircle(new Vector2(c.X + r * 0.42f, c.Y - r * 0.28f), r * 0.54f, col);
        ci.DrawColoredPolygon(new[]
        {
            new Vector2(c.X - r * 0.9f, c.Y + r * 0.04f),
            new Vector2(c.X + r * 0.9f, c.Y + r * 0.04f),
            new Vector2(c.X, c.Y + r),
        }, col);
    }

    // 1000以上を 1.2k / 3.4M に省略。
    public static string Abbrev(long n)
    {
        if (n >= 1_000_000) return (n / 1_000_000.0).ToString("0.0") + "M";
        if (n >= 1_000) return (n / 1_000.0).ToString("0.0") + "k";
        return n.ToString();
    }

    // クリアタイム表記 m:ss.cc（分:秒.センチ秒、例 1:23.45）。負値は 0 扱い。
    public static string FormatTime(float sec)
    {
        if (sec < 0f) sec = 0f;
        int totalCenti = Mathf.RoundToInt(sec * 100f);
        int minutes = totalCenti / 6000;
        int seconds = (totalCenti / 100) % 60;
        int centi = totalCenti % 100;
        return $"{minutes}:{seconds:00}.{centi:00}";
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
