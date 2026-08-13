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

    // ── 文字サイズ階層（用途別の一貫サイズ）──
    //   画面ごとにハードコードされていた寸法を用途で束ねる。同じ役割のテキストは全画面で同値・同フォントにする。
    //   ※ここでの単位は「設計座標（1280×720）」。BeginDesign 下で UiKit.Text/Multi 等に渡す前提。
    //     内部解像度384系（Prologue/Final/Epilogue の生 DrawString）は別スケールなので、この定数はそのままは使わない
    //     （換算目安 ×0.3。カットシーンは座標系ごと揃っているので下の Cutscene* を使う）。
    public const int FontDisplay = 52; // ロゴ／リザルトバナー級（超特大・演出の要）
    public const int FontTitle   = 28; // 画面見出し（各画面のページタイトル・カットイン・大ダイアログ見出し）
    public const int FontHeading = 20; // サブ見出し・カード名・大きめ数値（SCORE 等）
    public const int FontSpeaker = 18; // 会話の話者名
    public const int FontBody    = 15; // 本文・セリフ・説明文（画面をまたぐ標準本文）
    public const int FontLabel   = 13; // ボタン／行ラベル・カード説明・小見出し
    public const int FontSmall   = 11; // 注釈・キーヒント・メタ情報・英字サブラベル
    public const int FontTiny    = 9;  // 極小補助（強調しない添字）

    // ── カットシーン（内部解像度384系・生 DrawString）用の文字サイズ ──
    //   Prologue/Final/Epilogue は BeginDesign を使わず 384×216 の生座標で描くため別スケール。
    //   従来はセリフ11/名前9/注記8/クライマックス16で3画面互いに揃っていたが、設計座標系の本文(FontBody=15→384換算約4.5)
    //   と比べ相対的に大きく（＝エンディングだけ文字が大きく見えた）。本文を本編セリフの見え方へ寄せて縮小する。
    //   3画面まとめて同値にするため定数化（座標系ごとの改修は避け、サイズだけ揃える）。
    public const int CutBody    = 8;  // カットシーンのセリフ／ナレ本文（旧11。設計座標の本文に見た目を寄せる。384系では8が可読下限）
    public const int CutSpeaker = 7;  // 話者名（旧9）
    public const int CutNote    = 6;  // 送りヒント・小注記（旧8）
    public const int CutClimax  = 11; // クライマックスの一行強調（END/stay./PW候補/頭文字 等・旧16。本文比を保ちつつ縮小）

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

    // ══════════════════════ クリック領域レジストリ（マウスのホバー/ヒット判定）══════════════════════
    //   各画面が「クリック可能な矩形」を描画時に登録し、マウス位置と突合してホバー中/クリックされた
    //   領域の id を返す小機構。座標系は全画面が BeginDesign で統一している設計座標(1280×720)なので、
    //   登録する Rect2 も Pad.MousePos() もそのまま設計座標で扱えばよい（換算不要）。
    //
    //   使い方（各画面の _Draw か _Process 内）:
    //     UiKit.BeginHotspots(Pad.MousePos());           // フレーム頭でクリア＋マウス位置を渡す
    //     UiKit.Hotspot(rectA, 0);                        // 描画しながら領域を登録（id は _sel 等に対応させる）
    //     UiKit.Hotspot(rectB, 1);
    //     int hov = UiKit.HoveredId();                    // ホバー中の id（無ければ -1）。ハイライト描画に使う
    //     int clicked = UiKit.ClickedId(Pad.MouseClick());// クリックされた領域の id（左クリックエッジ時のみ）
    //
    //   ・ヒット結果 → 各画面の _sel やアクションへの結線は各画面側（フェーズ2/3）が行う。ここは突合だけ。
    //   ・後勝ち：重なる領域は「後に登録した方」がホバー扱い（前面に描いた要素が拾われる想定）。
    //     Hotspot 呼び出し順を前面→背面の逆（＝背面から前面へ）にすれば直感どおりになる。
    private static Vector2 _hotMouse;
    private static bool _hotActive;
    private static int _hotHovered = -1;

    // フレーム頭で1回。マウス設計座標を渡してホバー判定をリセットする。
    public static void BeginHotspots(Vector2 mouseDesign)
    {
        _hotMouse = mouseDesign;
        _hotActive = true;
        _hotHovered = -1;
    }

    // クリック可能領域を登録。マウスが rect 内なら _hotHovered を id で上書き（後勝ち）。
    //   返り値：この領域がホバー中か（呼び出し側が即ハイライトしたいとき用）。
    public static bool Hotspot(Rect2 rect, int id)
    {
        if (!_hotActive) return false;
        bool inside = rect.HasPoint(_hotMouse);
        if (inside) _hotHovered = id; // 後勝ち
        return inside;
    }

    // 現在ホバー中の id（登録済み領域のうちマウスが乗っているもの）。無ければ -1。
    public static int HoveredId() => _hotHovered;

    // クリックされた領域の id を返す。clicked（＝Pad.MouseClick() 等の左クリックエッジ）が true の
    // フレームでのみ、ホバー中の id を返す。それ以外は -1。呼び出し側はこれを _sel 決定/確定に使う。
    public static int ClickedId(bool clicked) => clicked ? _hotHovered : -1;

    // ── テキスト（設計座標・上端基準）──
    public static void Text(CanvasItem ci, Font f, Vector2 topLeft, string s, int size, Color c,
        HorizontalAlignment al = HorizontalAlignment.Left, float width = -1f)
    {
        float asc = f.GetAscent(size);
        ci.DrawString(f, new Vector2(topLeft.X, topLeft.Y + asc), s, al, width, size, c);
    }

    // 複数行描画（禁則つき折り返し）。行間は font 既定＝MultiLeading の extraLeading=0 と同じ。
    public static void Multi(CanvasItem ci, Font f, Vector2 topLeft, string s, int size, Color c, float width, int maxLines = -1)
        => MultiLeading(ci, f, topLeft, s, size, c, width, 0f, maxLines);

    public static float TextW(Font f, string s, int size) => f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;

    // 行間（leading）を足して読みやすくした複数行描画。会話ボックス用。
    //   width で語境界折り返し。各行は font 高さ + extraLeading(px) 間隔で送る。
    //   maxLines>0 ならその行数で打ち切り（末尾「…」は付けない＝会話は1画面に収まる前提で行数を増やす）。
    //   返り値：描画に使った総高さ（box 高さの算出に使える）。
    public static float MultiLeading(CanvasItem ci, Font f, Vector2 topLeft, string s, int size, Color c,
        float width, float extraLeading, int maxLines = -1)
    {
        var lines = WrapLines(f, s, size, width);
        // 禁則の追い出しで行数が増え、上限(maxLines)からあふれるときだけ素の文字折りへ戻す
        //（上限打ち切りで末尾の文字が消えるより、多少不格好でも全文が読める方を優先）。
        if (maxLines > 0 && lines.Count > maxLines)
        {
            var plain = WrapLines(f, s, size, width, kinsoku: false);
            if (plain.Count <= maxLines) lines = plain;
        }
        float lineH = f.GetHeight(size) + extraLeading;
        float asc = f.GetAscent(size);
        int n = maxLines > 0 ? Mathf.Min(maxLines, lines.Count) : lines.Count;
        for (int i = 0; i < n; i++)
            ci.DrawString(f, new Vector2(topLeft.X, topLeft.Y + asc + i * lineH), lines[i],
                HorizontalAlignment.Left, -1, size, c);
        return n * lineH;
    }

    // ── 日本語折り返し（禁則処理つき）──
    //   行頭禁則：句読点・閉じ括弧・小書き仮名・長音などは行頭に置かない（手前の文字ごと次行へ追い出す）。
    //   行末禁則：開き括弧は行末に置かない（次行へ送る）。
    //   英単語：途中で折らない（単語頭まで戻す。行頭からの1語が幅を超えるときだけ文字折り）。
    private const string KinsokuNoHead =
        "、。，．・：；！？…‥ーっゃゅょぁぃぅぇぉゎッャュョァィゥェォヮ々ゝゞヽヾ）」』】〕｝〉》’”!?.,:;)]}";
    private const string KinsokuNoTail = "（「『【〔｛〈《‘“([{";
    private static bool IsWordChar(char c) => c < 128 && (char.IsLetterOrDigit(c) || c == '\'');

    // width に収まるよう折り返した行リストを返す。明示改行 '\n' は尊重。
    //   エンディング等の独自レンダラも同じ折り返し結果（＝同じ禁則・同じ行数）を共有できるよう public。
    public static System.Collections.Generic.List<string> WrapLines(Font f, string s, int size, float width,
        bool kinsoku = true)
    {
        var outLines = new System.Collections.Generic.List<string>();
        foreach (var para in s.Split('\n'))
        {
            if (para.Length == 0) { outLines.Add(""); continue; }
            int start = 0;
            while (start < para.Length)
            {
                // 幅に収まる最大文字数を貪欲に確定（最低1文字は必ず進める＝無限ループ防止）。
                int fit = 1;
                while (start + fit < para.Length && TextW(f, para.Substring(start, fit + 1), size) <= width)
                    fit++;
                int brk = start + fit;                       // para[brk] が次行の先頭
                if (brk < para.Length && kinsoku)
                {
                    // 英単語の途中では折らない：単語頭まで戻す。
                    if (IsWordChar(para[brk]) && IsWordChar(para[brk - 1]))
                    {
                        int head = brk;
                        while (head > start && IsWordChar(para[head - 1])) head--;
                        if (head > start) brk = head;
                    }
                    // 行頭禁則：句読点等が行頭に来るなら、手前の文字ごと次行へ追い出す。
                    while (brk > start + 1 && KinsokuNoHead.IndexOf(para[brk]) >= 0) brk--;
                    // 行末禁則：開き括弧で行が終わるなら、それも次行へ送る。
                    while (brk > start + 1 && KinsokuNoTail.IndexOf(para[brk - 1]) >= 0) brk--;
                }
                outLines.Add(para.Substring(start, brk - start));
                start = brk;
            }
        }
        return outLines;
    }

    // ── テキストボックスのページ分割（全ボックス共通の「2行固定＋ページ送り」用）──
    //   本文を WrapLines（禁則つき）で折り返し、maxLines 行ずつ束ねて1ページにする。返り値は各ページ本文
    //  （ページ内の行は '\n' 区切り＝そのまま WrapLines/TypewriterLines に渡せば同じ行構成で描ける）。
    //   セリフは削らない：長い行は複数ページに分かれ、送りで続きを読ませる。空文字は空1ページを返す。
    public static System.Collections.Generic.List<string> Paginate(Font f, string s, int size, float width, int maxLines)
    {
        var pages = new System.Collections.Generic.List<string>();
        var lines = WrapLines(f, s, size, width);
        if (lines.Count == 0) { pages.Add(s); return pages; }
        for (int i = 0; i < lines.Count; i += maxLines)
        {
            int n = System.Math.Min(maxLines, lines.Count - i);
            pages.Add(string.Join("\n", lines.GetRange(i, n)));
        }
        // 孤児行（オーファン）対策：最終ページが「ごく短い端数1行」だけになると、送った先に
        //   「も」のような数文字だけが1ページ丸ごと表示されて事故に見える（禁則で助詞が行頭送りされた場合など）。
        //   最終ページが1行かつ短いときは、直前ページの末尾行を1行分けてもらって2行にし、単独表示を避ける。
        //   （直前ページが1行しか無い場合は分けられないのでそのまま＝元の挙動を維持）
        const int OrphanMaxChars = 6;   // これ以下の長さの1行だけなら孤児とみなす
        if (pages.Count >= 2)
        {
            var last = pages[pages.Count - 1];
            if (last.IndexOf('\n') < 0 && last.Length <= OrphanMaxChars)
            {
                var prev = pages[pages.Count - 2].Split('\n');
                if (prev.Length >= 2)
                {
                    string moved = prev[prev.Length - 1];
                    pages[pages.Count - 2] = string.Join("\n", prev, 0, prev.Length - 1);
                    pages[pages.Count - 1] = moved + "\n" + last;
                }
            }
        }
        return pages;
    }

    // 折り返し済みの行リストを、タイプライターの表示済み文字数 reveal 分だけ描く（カットシーンの独自レンダラ用）。
    //   行構成は全文の WrapLines で確定済み＝表示途中で折り返し位置が動かない。座標はベースライン基準（DrawString と同じ）。
    //   align=Center はナレ用（全文を一括フェードインで出す前提。部分表示だと毎フレーム再センタリングされるため）。
    public static void TypewriterLines(CanvasItem ci, Font f, System.Collections.Generic.List<string> lines,
        Vector2 firstBaseline, float width, int size, Color c, int reveal,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        float lineH = f.GetHeight(size);
        float y = firstBaseline.Y;
        foreach (var ln in lines)
        {
            if (reveal <= 0) break;
            string part = reveal >= ln.Length ? ln : ln.Substring(0, reveal);
            ci.DrawString(f, new Vector2(firstBaseline.X, y), part, align,
                align == HorizontalAlignment.Left ? -1 : width, size, c);
            reveal -= ln.Length;
            y += lineH;
        }
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
        => ci.DrawTextureRect(GradTex(colors, offsets, vertical: true), r, false);

    // ── 横リニアグラデ矩形（左→右に色を補間）──
    public static void HGradient(CanvasItem ci, Rect2 r, Color left, Color right)
        => ci.DrawTextureRect(GradTex(new[] { left, right }, new[] { 0f, 1f }, vertical: false), r, false);

    // ── 放射グロウ（中心色→透明）。rect 全体に円形グラデを敷く ──
    //   テクスチャは「白→透明」の1枚を全呼び出しで使い回し、色は modulate で乗せる
    //   （白×modulate＝従来の色焼き込みと同値）。＝アルファが毎フレーム動く呼び出しでも
    //   テクスチャを新規生成しない（下の _gradCache コメント参照）。
    public static void RadialGlow(CanvasItem ci, Vector2 center, float radius, Color inner, float innerAlpha = -1f)
    {
        if (innerAlpha >= 0f) inner = new Color(inner.R, inner.G, inner.B, innerAlpha);
        _radialWhite ??= new GradientTexture2D
        {
            Gradient = new Gradient
            {
                Offsets = new[] { 0f, 1f },
                Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) },
            },
            Width = 128, Height = 128,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        };
        ci.DrawTextureRect(_radialWhite, new Rect2(center.X - radius, center.Y - radius, radius * 2, radius * 2), false, inner);
    }
    private static GradientTexture2D? _radialWhite;

    // ── グラデテクスチャの静的キャッシュ ──
    //   ★_Draw 内で GradientTexture2D を毎フレーム new してはいけない：
    //     .NET GC が管理ラッパーを回収するとファイナライザスレッドから RID が解放され、
    //     GradientTexture2D の遅延更新（内部の texture_replace / texture_set_path）と競合して
    //     レンダラが "Parameter \"tex\" is null" を実行時に吐く（タイトル起動で実際に発生）。
    //   → 同一パラメータのテクスチャは1枚だけ作って静的に持ち続ける（強参照＝GC に回収させない）。
    //     色/offsets は全呼び出し箇所でリテラル定数（毎フレーム可変の色は RadialGlow の modulate 側に逃がした）
    //     なので、キャッシュは高々十数枚で頭打ちになる。
    private static readonly System.Collections.Generic.Dictionary<string, GradientTexture2D> _gradCache = new();
    private static GradientTexture2D GradTex(Color[] colors, float[] offsets, bool vertical)
    {
        var kb = new System.Text.StringBuilder(vertical ? "v" : "h");
        for (int i = 0; i < colors.Length; i++) kb.Append('|').Append(colors[i].ToRgba32()).Append('@').Append(offsets[i]);
        string key = kb.ToString();
        if (_gradCache.TryGetValue(key, out var hit)) return hit;
        var tex = new GradientTexture2D
        {
            Gradient = new Gradient { Offsets = offsets, Colors = colors },
            Width = vertical ? 8 : 256, Height = vertical ? 256 : 8,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = Vector2.Zero, FillTo = vertical ? new Vector2(0, 1) : new Vector2(1, 0),
        };
        _gradCache[key] = tex;
        return tex;
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
