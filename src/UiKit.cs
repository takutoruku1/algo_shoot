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
    public static readonly Color Text2   = new("d5cfdf");
    public static readonly Color Text3   = new("b0a8bf");
    public static readonly Color Text4   = new("9690a5");
    public static readonly Color BgDeep  = new("070a16");
    public static readonly Color Ok      = new("2ec78c"); // リポスト緑/成功
    public static readonly Color Burn    = new("f2353d"); // 炎上赤

    // ── カットシーン（Prologue/Final/Epilogue）配色トークン ──
    //   3画面それぞれに同値の Cool/Warm/Ink/Code が独立コピーされていたのを一本化。
    //   既存トークンに寄せられるものは寄せる（bootログ緑＝Ok）。
    public static readonly Color CutInk    = new("eef0fa"); // 本文
    public static readonly Color CutInk2   = new("a8b0c8"); // 注記・ヒント
    public static readonly Color CutMina   = Info;          // ミナ（見出しシアン）
    public static readonly Color CutWarm   = new("ffd98c"); // 少年（暖色）
    public static readonly Color CutCode   = Ok;            // bootログ緑
    public static readonly Color CutNarr   = new("9ea3b8"); // 語り（話者名なし）の縁色

    // ── 文字サイズ階層（用途別の一貫サイズ）──
    //   画面ごとにハードコードされていた寸法を用途で束ねる。同じ役割のテキストは全画面で同値・同フォントにする。
    //   ※ここでの単位は「設計座標（1280×720）」。BeginDesign 下で UiKit.Text/Multi 等に渡す前提。
    //     内部解像度384系（Prologue/Final/Epilogue の生 DrawString）は別スケールなので、この定数はそのままは使わない
    //     （換算目安 ×0.3。カットシーンは座標系ごと揃っているので下の Cutscene* を使う）。
    public const int FontDisplay = 52; // ロゴ／リザルトバナー級（超特大・演出の要）
    public const int FontTitle   = 28; // 画面見出し（各画面のページタイトル・カットイン・大ダイアログ見出し）
    public const int FontHeading = 20; // サブ見出し・カード名・大きめ数値（SCORE 等）
    public const int FontSpeaker = 19; // 会話の話者名（旧18）
    public const int FontBody    = 17; // 本文・セリフ・説明文（画面をまたぐ標準本文・旧15）
    public const int FontLabel   = 14; // ボタン／行ラベル・カード説明・小見出し
    public const int FontSmall   = 13; // 注釈・キーヒント・メタ情報・英字サブラベル（旧11＝実効3.3pxで可読下限割れ）
    // FontTiny(9) は廃止（実効2.7px＝画面に出してはいけない大きさ）。参照は FontSmall(13) へ寄せた。

    // ── カットシーン（内部解像度384系・生 DrawString）用の文字サイズ ──
    //   Prologue/Final/Epilogue は BeginDesign を使わず 384×216 の生座標で描くため別スケール。
    //   従来はセリフ11/名前9/注記8/クライマックス16で3画面互いに揃っていたが、設計座標系の本文(FontBody=15→384換算約4.5)
    //   と比べ相対的に大きく（＝エンディングだけ文字が大きく見えた）。本文を本編セリフの見え方へ寄せて縮小する。
    //   3画面まとめて同値にするため定数化（座標系ごとの改修は避け、サイズだけ揃える）。
    public const int CutBody    = 8;  // カットシーンのセリフ／ナレ本文（旧11。設計座標の本文に見た目を寄せる。384系では8が可読下限）
    public const int CutSpeaker = 7;  // 話者名（旧9）
    public const int CutNote    = 6;  // 送りヒント・小注記（旧8）
    public const int CutClimax  = 11; // クライマックスの一行強調（END/stay./PW候補/頭文字 等・旧16。本文比を保ちつつ縮小）

    // ══════════════════════ TextStyle（役割 → フォント/サイズ/字間/行間）══════════════════════
    //   画面ごとに「サイズだけ」直書きしていたのをやめ、役割で引く。フォントの割り当ても型に含める
    //   ＝「英字ラベルなのに Mono、数値なのに Zen」のような取り違えが起こらない。
    //   Tracking : 1文字ごとに足す横アキ(px・設計座標)。短い英字ラベルは開けないと詰まって見える。
    //   Leading  : 行送りの倍率（1.0 = フォント既定の行高）。単行の役割では 1.0 のまま。
    //   使い方 :  UiKit.Draw(ci, UiKit.PanelLabel, pos, "LIFE", col);            // 字間つき1行
    public readonly record struct TextStyle(FontFile Font, int Size, float Tracking, float Leading)
    {
        // このスタイルの行送り(px)。MultiLeading の extraLeading へ渡す差分も添える。
        public float LineHeight => Font.GetHeight(Size) * Leading;
        public float ExtraLeading => Font.GetHeight(Size) * (Leading - 1f);
    }

    // 役割別スタイル（docs/20260906/HUD整理_案.md §9 の表）。
    public static TextStyle PanelValueLarge => new(Mono, 26, 0f, 1f);      // 数値（大）：SCORE / TIME の値
    public static TextStyle PanelValueMid   => new(Mono, 18, 0f, 1f);      // 数値（中）：コンボ / 残バー / 浄化 %
    public static TextStyle PanelLabel      => new(ZenBold, 15, 0f, 1f);   // セクション見出し：LIFE / BOMB / SCORE / TIME / 浄化
    public static TextStyle DialogBody      => new(Zen, FontBody, 0f, 1.55f);// 本文・セリフ・ナレ
    // 戦闘中（盤面下の会話バー・ナレのテロップ）の本文。弾を避けながら読む枠なので一回り大きい
    //   （2026-09-26 ユーザー「シューティング中の吹き出しの文字をもっと大きく」：17 → 22）。
    public const int FontBattle = 22;
    public static TextStyle BattleBody      => new(Zen, FontBattle, 0f, 1.5f);
    public static TextStyle DialogSpeaker   => new(ZenBold, FontSpeaker, 0f, 1f); // 話者名
    public static TextStyle SmallLabel      => new(ZenBold, FontSmall, 0f, 1f);   // 小ラベル：キーバッジ・炎上の内訳
    public static TextStyle SmallValue      => new(Mono, FontSmall, 0f, 1f);      // 小さい「値」：残バー数・リプ数（数値は Mono に残す）

    // ── 字間つき描画（Hud のローカル実装から公開ヘルパへ格上げ）──
    //   track を1文字ごとに足しながら1文字ずつ描く。track=0 なら普通の DrawString と同じなので素通しする。
    //   align は Left（topLeft 基準）/ Center（cx 基準）/ Right（右端基準）を width 無しで扱う。
    public static float TrackedW(Font f, string s, int size, float track)
    {
        if (s.Length == 0) return 0f;
        float w = 0f;
        for (int i = 0; i < s.Length; i++) w += TextW(f, s[i].ToString(), size) + track;
        return w - track; // 末尾の1字ぶんの字間は幅に含めない
    }

    public static float TrackedW(TextStyle st, string s) => TrackedW(st.Font, s, st.Size, st.Tracking);

    // 字間つき1行描画（上端基準・左寄せ）。返り値は描いた幅。
    public static float Tracked(CanvasItem ci, Font f, Vector2 topLeft, string s, int size, Color c, float track)
    {
        if (c.A <= 0.004f || s.Length == 0) return 0f;
        if (track == 0f) { Text(ci, f, topLeft, s, size, c); return TextW(f, s, size); }
        float asc = f.GetAscent(size);
        float x = topLeft.X;
        for (int i = 0; i < s.Length; i++)
        {
            string ch = s[i].ToString();
            ci.DrawString(f, new Vector2(x, topLeft.Y + asc), ch, HorizontalAlignment.Left, -1, size, c);
            x += TextW(f, ch, size) + track;
        }
        return x - topLeft.X - track;
    }

    // 役割スタイルで1行（左寄せ・上端基準）。
    public static float Draw(CanvasItem ci, TextStyle st, Vector2 topLeft, string s, Color c)
        => Tracked(ci, st.Font, topLeft, s, st.Size, c, st.Tracking);

    // 役割スタイルで1行（右寄せ・right が右端）。数値の桁が伸びても右端が動かない。
    public static float DrawRight(CanvasItem ci, TextStyle st, float right, float top, string s, Color c)
        => Tracked(ci, st.Font, new Vector2(right - TrackedW(st, s), top), s, st.Size, c, st.Tracking);

    // 役割スタイルでページ分割（MultiLeading と同じ折り返し結果になる）。
    public static System.Collections.Generic.List<string> Paginate(TextStyle st, string s, float width, int maxLines)
        => Paginate(st.Font, s, st.Size, width, maxLines);

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
        // Keep kinsoku when compacting phrase-based wrapping into a capped label.
        if (maxLines > 0 && lines.Count > maxLines)
        {
            var plain = WrapLines(f, s, size, width, preferPhrases: false);
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
    private static bool IsWordChar(char c) => c < 128 && (char.IsLetterOrDigit(c) || "'_@:/.,-".IndexOf(c) >= 0);

    private static bool CanBreakLine(string text, int at)
    {
        char before = text[at - 1], after = text[at];
        bool pause = after is '…' or '‥' or '—';
        if (pause && before == after) return false;
        bool pauseStart = pause && at + 1 < text.Length && text[at + 1] == after;
        if (before is '…' or '‥' or '—')
        {
            int head = at - 1;
            while (head > 0 && text[head - 1] == before) head--;
            if (head == 0 || IsSentenceEnd(text, head)) return false;
        }
        return (KinsokuNoHead.IndexOf(after) < 0 || pauseStart)
            && KinsokuNoTail.IndexOf(before) < 0;
    }

    private static bool IsSentenceEnd(string text, int at)
    {
        while (at > 0 && (char.IsWhiteSpace(text[at - 1]) || "）」』】〕｝〉》’”)]}".IndexOf(text[at - 1]) >= 0)) at--;
        return at > 0 && "。！？!?".IndexOf(text[at - 1]) >= 0
            || at > 1 && text[at - 1] == '.' && !char.IsDigit(text[at - 2]);
    }

    private static int BreakRank(string text, int at)
    {
        if (IsSentenceEnd(text, at)) return 0;
        if (at + 1 < text.Length && (text[at] is '…' or '‥' or '—') && text[at] == text[at + 1]) return 1;
        if ("、；;）」』】〕｝〉》’”)]}".IndexOf(text[at - 1]) >= 0) return 2;
        return char.IsWhiteSpace(text[at - 1]) ? 3 : 4;
    }

    private static bool[] WordBoundaries(string text)
    {
        // TextServer uses Unicode code-point offsets; C# slices use UTF-16 offsets.
        var offsets = new System.Collections.Generic.List<int> { 0 };
        int offset = 0;
        foreach (var rune in text.EnumerateRunes()) { offset += rune.Utf16SequenceLength; offsets.Add(offset); }
        var words = TextServerManager.GetPrimaryInterface().StringGetWordBreaks(text, "ja");
        var breaks = new bool[text.Length + 1];
        foreach (int at in words) breaks[offsets[at]] = true;
        for (int i = 0; i < words.Length; i += 2)
        {
            int start = offsets[words[i]], end = offsets[words[i + 1]];
            string word = text[start..end];
            if (word is "は" or "が" or "を" or "に" or "へ" or "と" or "で" or "も" or "の"
                or "ね" or "よ" or "か" or "から" or "まで" or "だけ" or "です" or "ます"
                or "た" or "て" or "だ" or "ない" or "たい" or "ません" or "でした" or "様" or "さん" or "ちゃん")
                breaks[start] = false;
            if (word is "お" or "ご") breaks[end] = false;
        }
        return breaks;
    }

    // width に収まるよう折り返した行リストを返す。明示改行 '\n' は尊重。
    //   エンディング等の独自レンダラも同じ折り返し結果（＝同じ禁則・同じ行数）を共有できるよう public。
    public static System.Collections.Generic.List<string> WrapLines(Font f, string s, int size, float width,
        bool kinsoku = true, bool preferPhrases = true)
    {
        var outLines = new System.Collections.Generic.List<string>();
        foreach (var para in s.Replace("\r\n", "\n").Split('\n'))
        {
            if (para.Length == 0) { outLines.Add(""); continue; }
            var stops = new System.Collections.Generic.List<int>(System.Globalization.StringInfo.ParseCombiningCharacters(para));
            stops.Add(para.Length);
            bool[]? wordBreaks = null;
            int first = 0;
            while (first < stops.Count - 1)
            {
                int start = stops[first];
                int fit = first + 1;
                while (fit + 1 < stops.Count && TextW(f, para.Substring(start, stops[fit + 1] - start), size) <= width)
                    fit++;
                int brk = fit;
                if (stops[brk] < para.Length && kinsoku)
                {
                    if (IsWordChar(para[stops[brk]]) && IsWordChar(para[stops[brk] - 1]))
                    {
                        int head = brk;
                        while (head > first && IsWordChar(para[stops[head] - 1])) head--;
                        if (head > first) brk = head;
                    }
                    while (brk > first + 1 && !CanBreakLine(para, stops[brk])) brk--;
                    if (preferPhrases)
                    {
                        int bestRank = 4;
                        int phraseBreak = brk;
                        for (int candidate = brk; candidate > first; candidate--)
                        {
                            int at = stops[candidate];
                            int rank = BreakRank(para, at);
                            if (rank >= bestRank || !CanBreakLine(para, at)) continue;
                            if (IsWordChar(para[at - 1]) && IsWordChar(para[at])) continue;
                            float used = TextW(f, para.Substring(start, at - start), size);
                            float tail = TextW(f, para.Substring(at), size);
                            bool lastLineFits = tail <= width;
                            if (used < width * 0.5f && !lastLineFits) continue;
                            if (tail < Mathf.Min(size * 6f, width * .4f)) continue;
                            phraseBreak = candidate;
                            bestRank = rank;
                        }
                        if (bestRank < 4) brk = phraseBreak;
                        else
                        {
                            wordBreaks ??= WordBoundaries(para);
                            float minimumTail = Mathf.Min(size * 6f, width * 0.4f);
                            for (int candidate = brk; candidate > first; candidate--)
                            {
                                int at = stops[candidate];
                                if (TextW(f, para.Substring(start, at - start), size) < width * .65f) break;
                                float tail = TextW(f, para.Substring(at), size);
                                if (!wordBreaks[at] || tail < minimumTail || !CanBreakLine(para, at)) continue;
                                if (IsWordChar(para[at - 1]) && IsWordChar(para[at])) continue;
                                brk = candidate;
                                break;
                            }
                        }
                    }
                }
                outLines.Add(para.Substring(start, stops[brk] - start));
                first = brk;
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
        HorizontalAlignment align = HorizontalAlignment.Left, bool shadow = false, float extraLeading = 0f)
    {
        float lineH = f.GetHeight(size) + extraLeading;
        float y = firstBaseline.Y;
        foreach (var ln in lines)
        {
            if (reveal <= 0) break;
            string part = reveal >= ln.Length ? ln : ln.Substring(0, reveal);
            float w = align == HorizontalAlignment.Left ? -1 : width;
            // ドロップシャドウ：背景直乗せ（箱なし）の画面で本文を背景から浮かせる。本体より先に描く。
            if (shadow)
                ci.DrawString(f, new Vector2(firstBaseline.X + 0.5f, y + 0.5f), part, align, w, size,
                    new Color(0f, 0f, 0f, 0.55f * c.A));
            ci.DrawString(f, new Vector2(firstBaseline.X, y), part, align, w, size, c);
            reveal -= ln.Length + 1;
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

    // ══════════════════════ 立ち絵（中身の高さでそろえ、足元を基準線に置く）══════════════════════
    //   2026-09-27 作者指摘「服のショップで、キャラクターのサイズが違うからサイズを統一して」
    //   「ショップの回避時の位置がおかしいんだな」への土台。
    //   自機の絵は2系統あり、既定衣装（char/player/<id>/<id>_*.png）は絵の外接で切り詰め済み、
    //   追加衣装（char/player/<id>/costume_v1/*.png）は 720 前後のキャンバスに大きな透明余白つきで入っている。
    //   テクスチャ全体を枠へ Mathf.Min でフィットさせると
    //     ・余白の量だけ見かけの大きさが変わる（衣装を替えるとキャラが縮む＝「サイズが違う」）
    //     ・ポーズごとにキャンバス寸法も中身の位置も違うので、回避アニメで倍率と中心が毎フレーム飛ぶ
    //   → 不透明部分の矩形（ContentRect）を測り、「中身の高さ」で倍率を決め、「中身の足元」を基準線に置く。
    //
    //   素材の縁にはごく薄い（α 1〜15/255）汚れが散っていて、α>0 で測る Image.GetUsedRect() だと余白を
    //   落としきれない（こはるの雨上がり・回避 spin_00 は α>0 なら高さ 720＝キャンバス全体、実際の絵は 672）。
    //   画面には出ない濃さなので、この値より濃い画素だけを中身として数える。
    public const float ContentAlphaCut = 0.05f;

    // 測った結果はリソースパスをキーに持ち続ける（GetImage() は数百万画素の走査＝毎フレーム呼んではいけない）。
    private static readonly System.Collections.Generic.Dictionary<string, Rect2I> _contentRects = new();
    // 実測した回数（QA 用。同じ絵を何度描いてもここは増えない＝キャッシュが効いている）。
    public static int ContentRectScans { get; private set; }

    // テクスチャの「中身」＝不透明部分の外接矩形（テクスチャ画素座標）。測れなければテクスチャ全体。
    public static Rect2I ContentRect(Texture2D tex)
    {
        string key = tex.ResourcePath.Length > 0 ? tex.ResourcePath : "#" + tex.GetInstanceId();
        if (_contentRects.TryGetValue(key, out var hit)) return hit;
        var rect = MeasureContent(tex);
        _contentRects[key] = rect;
        return rect;
    }

    private static Rect2I MeasureContent(Texture2D tex)
    {
        var full = new Rect2I(0, 0, tex.GetWidth(), tex.GetHeight());
        using var img = tex.GetImage();
        if (img == null) return full;
        ContentRectScans++;
        if (img.IsCompressed() && img.Decompress() != Error.Ok) return full;
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        byte[] data = img.GetData();
        if (data.Length < w * h * 4) return full;
        byte cut = (byte)Mathf.RoundToInt(ContentAlphaCut * 255f);
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4 + 3;   // その行の先頭画素のαのバイト位置
            for (int x = 0; x < w; x++)
            {
                if (data[row + x * 4] <= cut) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                y1 = y;
            }
        }
        return x1 < 0 ? full : new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    // 中身の高さを contentHeight にそろえる倍率。ポーズを替えても大きさを動かしたくないときは、
    //   その絵ではなく idle でこれを求めて DrawPortraitScaled へ渡す（回避の絵は姿勢で中身の高さが変わるため）。
    public static float PortraitScale(Texture2D tex, float contentHeight)
    {
        var c = ContentRect(tex);
        return c.Size.Y > 0 ? contentHeight / c.Size.Y : 1f;
    }

    // 中身の高さを contentHeight にそろえて描く。中身の水平中心が baselineCenter.X、
    //   中身の下端（足元）が baselineCenter.Y に来る。返り値は中身が画面上で占める矩形。
    public static Rect2 DrawPortrait(CanvasItem ci, Texture2D tex, Vector2 baselineCenter, float contentHeight, bool flip = false)
        => DrawPortraitScaled(ci, tex, baselineCenter, PortraitScale(tex, contentHeight), flip);

    // 描かずに「中身が画面上で占める矩形」だけを返す（枠からのはみ出しを見る QA・当たり確認用）。
    //   左右反転しても中身の中心が基準線に居続けるので、この矩形は flip に依らない。
    public static Rect2 PortraitRect(Texture2D tex, Vector2 baselineCenter, float scale)
    {
        var c = ContentRect(tex);
        return new Rect2(baselineCenter.X - c.Size.X * scale / 2f, baselineCenter.Y - c.Size.Y * scale,
            c.Size.X * scale, c.Size.Y * scale);
    }

    // 倍率を外から渡す版。返り値は PortraitRect と同じ「中身が画面上で占める矩形」。
    public static Rect2 DrawPortraitScaled(CanvasItem ci, Texture2D tex, Vector2 baselineCenter, float scale, bool flip = false)
    {
        var c = ContentRect(tex);
        var size = tex.GetSize() * scale;
        var pos = new Vector2(
            baselineCenter.X - (c.Position.X + c.Size.X / 2f) * scale,   // 中身の水平中心を基準線の X へ
            baselineCenter.Y - (c.Position.Y + c.Size.Y) * scale);       // 中身の下端（足元）を基準線の Y へ
        // 左右反転は「中身の中心」を軸に折り返す（テクスチャ枠の中心で折ると余白の差だけ中心がずれる）。
        //   ★DrawTextureRect は size.X が負でも position はそのまま＝「position から |size.X| ぶん右へ、
        //     UV だけ反転して」描く（RendererCanvasCull::canvas_item_add_texture_rect が size.X を正に直して
        //     FLIP_H を立てるだけ）。position を「右端」にずらす昔ながらの書き方だと絵が幅1枚ぶん右へ飛ぶ。
        var draw = flip
            ? new Rect2(2f * baselineCenter.X - pos.X - size.X, pos.Y, -size.X, size.Y)
            : new Rect2(pos, size);
        ci.DrawTextureRect(tex, draw, false);
        return PortraitRect(tex, baselineCenter, scale);
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

    // スコア表記（3桁区切り、例 12,345）。
    public static string FormatScore(long score) => score.ToString("N0");

    // ── キーキャップ（Z や ↑↓ の角丸チップ）。中央寄せのモノ文字 ──
    public static void Key(CanvasItem ci, Vector2 pos, string label, Color bg, Color border, Color textCol, float h = 24f, float minW = 24f)
    {
        float pad = 12f;
        float w = Mathf.Max(minW, TextW(Mono, label, 12) + pad);
        Box(ci, new Rect2(pos.X, pos.Y, w, h), bg, 6f, border, 1f);
        float asc = Mono.GetAscent(12), desc = Mono.GetDescent(12);
        ci.DrawString(Mono, new Vector2(pos.X, pos.Y + (h + asc - desc) / 2f), label, HorizontalAlignment.Center, w, 12, textCol);
    }

    // ══════════════════════ キーキャップ（押せる鍵の形）とヒント帯 ══════════════════════
    //   2026-09-27 作者指示「スマホの操作で J とか C とか X とか書いてあるけど操作性悪すぎでしょ」を受けて新設。
    //   項目ごとにキー文字を貼るのをやめ、操作の案内は画面下端の1行（HintBar）にまとめる。その1行の部品。
    //   ・キーボード表示：角丸のキー。上面＋下側 2px の側面（影）＋上端 1px のハイライト＝「押せる鍵」の形。
    //   ・パッド表示（Pad.UsingPad）：A／B／X／Y（〇／×／□／△）は丸ボタン、LB／RB／L1／R1／LT／RT／L3／R3／
    //     L／R（スティック）／View は横長のピル、≡（Menu）は丸に三本線、「十字」は十字キーの形。
    //   ・矢印クラスタ（↑↓←→／↑↓／←→）は1つの横長キャップにまとめ、矢印はベクタで描く
    //     （フォントの矢印は 12px だと線が細く小さく、キーの面の中で読みにくかった）。
    //   ・マウス（左クリック／右クリック／中クリック／ホイール）は文字ではなく小さなマウスの絵（該当ボタンを Info で塗る）。
    //   ★同日、全画面のキー表記（Hud のサイドパネル・あそびかた・操作カード・設定・トレーニング）をこれに揃えた。
    //     旧 Hud.KeyBadge／HowToPlay.KeyBadge は削除。旧 Key（上）はクレジットの案内だけが使う。
    //   pad 引数：null＝いまの表示（Pad.UsingPad）に従う。あそびかたのコントローラー表のように、表示と無関係に
    //     「その機種の絵」で描きたい所だけ true／false を渡す。
    private static readonly Color CapSide   = new(0.10f, 0.11f, 0.13f);   // 側面（下 2px の影）
    private static readonly Color CapTop    = new(0.18f, 0.19f, 0.22f);   // 上面（Hud.SideRaised 相当）
    private static readonly Color CapBorder = new(0.30f, 0.32f, 0.36f);   // 枠
    private static readonly Color CapInk    = new(0.94f, 0.95f, 0.95f);   // 文字（Hud.SideInk 相当）
    private const int CapFontSize = 12;
    private const float CapSideH = 2f;

    // パッドの丸ボタンとして描くトークンか。A／B／X／Y は文字だけではキーボードの同名キーと区別できないので、
    //   パッド表示のときだけ丸にする。PS の記号（〇×□△）は記号そのものがパッドの印なので常に丸。
    private static bool CapIsPadFace(string t, bool pad) =>
        t is "〇" or "○" or "×" or "□" or "△" || (pad && t is "A" or "B" or "X" or "Y");
    private static bool CapIsPill(string t, bool pad) =>
        pad && t is "LB" or "RB" or "L1" or "R1" or "LT" or "RT" or "L3" or "R3" or "L" or "R" or "View";
    private static bool CapIsMenu(string t) => t is "≡" or "Menu(≡)";
    private static bool CapIsDpad(string t) => t is "十字" or "十字キー";
    // マウスの絵で描くトークンか（Hud.TokLock＝左クリック／TokFocus＝ホイール、あそびかたのマウス表 等）。
    public static bool CapIsMouse(string t) => t is "左クリック" or "右クリック" or "中クリック" or "ホイール";
    // 矢印だけでできたトークン（↑↓←→ の並び）か。
    private static bool CapIsArrows(string t)
    {
        if (t.Length == 0) return false;
        foreach (char ch in t) if (ch is not ('↑' or '↓' or '←' or '→')) return false;
        return true;
    }
    private const float ArrowSlot = 9f, ArrowGap = 3f;
    // 高さ 22 を基準にした拡大率（文字・矢印・十字の太さを高さに比例させる）。
    private static float CapK(float h) => h / 22f;
    private static int CapSize(float h) => Mathf.Max(9, Mathf.RoundToInt(CapFontSize * CapK(h)));

    // キャップの文字に使うフォント：Mono で全部描ける（英数・矢印）なら Mono、かなを含めば ZenBold。
    private static FontFile CapFont(string t)
    {
        foreach (char ch in t) if (!Mono.HasChar(ch)) return ZenBold;
        return Mono;
    }

    // キャップの幅（KeyCap が描く幅と同一式）。
    public static float KeyCapW(string token, float h = 22f, bool? pad = null)
    {
        bool p = pad ?? Pad.UsingPad;
        float k = CapK(h);
        if (CapIsMouse(token)) return Mathf.Round(h * 0.8f);
        if (CapIsPadFace(token, p) || CapIsMenu(token)) return h;
        if (CapIsDpad(token)) return h + 4f;
        if (CapIsPill(token, p)) return Mathf.Max(h * 1.6f, TextW(Mono, token, CapSize(h) - 1) + 16f);
        if (CapIsArrows(token)) return Mathf.Max(h, (token.Length * ArrowSlot + (token.Length - 1) * ArrowGap) * k + 10f);
        return Mathf.Max(h, TextW(CapFont(token), token, CapSize(h)) + 10f);
    }

    // 22px の角丸キーキャップ。pos＝左上。上面ハイライト＋下側の影で「押せる鍵」の形。文字は中央。
    //   pressed=true で面が 1px 沈み（側面 1px）、上面がアクセント色（Info）へ寄る。描いた幅を返す。
    public static float KeyCap(CanvasItem ci, Vector2 pos, string token, float h = 22f, bool pressed = false, float alpha = 1f,
        bool? pad = null)
    {
        bool p = pad ?? Pad.UsingPad;
        float w = KeyCapW(token, h, p);
        float k = CapK(h);
        Color A(Color c, float kk = 1f) => new(c, c.A * kk * alpha);
        float sink = pressed ? 1f : 0f;
        Color top = pressed ? CapTop.Lerp(Info, 0.45f) : CapTop;
        int fs = CapSize(h);

        if (CapIsMouse(token))
        {
            DrawMouseCap(ci, pos, token, w, h, sink, top, alpha);
            return w;
        }

        if (CapIsPadFace(token, p) || CapIsMenu(token))
        {
            // 丸ボタン：下に 2px（押下時 1px）ずらした影の円 → 面の円 → 縁 → 上寄りの薄いハイライト弧。
            float r = h / 2f;
            var c = pos + new Vector2(r, r - CapSideH / 2f + sink);
            ci.DrawCircle(c + new Vector2(0, CapSideH - sink), r - 1f, A(CapSide));
            ci.DrawCircle(c, r - 1f, A(top));
            ci.DrawArc(c, r - 1f, 0f, Mathf.Tau, 32, A(CapBorder), 1f, true);
            ci.DrawArc(c, r - 2.5f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 12, A(new Color(1, 1, 1, 0.12f)), 1f, true);
            if (CapIsMenu(token))
            {
                for (int i = -1; i <= 1; i++)
                    ci.DrawLine(c + new Vector2(-r * 0.40f, i * r * 0.36f), c + new Vector2(r * 0.40f, i * r * 0.36f), A(CapInk), 1.2f, true);
                return w;
            }
            string glyph = token == "○" ? "〇" : token;
            FontFile gf = CapFont(glyph);
            float asc = gf.GetAscent(fs), desc = gf.GetDescent(fs);
            ci.DrawString(gf, new Vector2(pos.X, c.Y + (asc - desc) / 2f), glyph, HorizontalAlignment.Center, w, fs,
                A(PadFaceColor(glyph)));
            return w;
        }

        // 角丸キー（ピルは角を高さの半分まで丸める）。側面＝全高の箱、上面＝下 2px（押下時 1px）を残した箱。
        bool pill = CapIsPill(token, p);
        float rad = pill ? h / 2f : 5f;
        Box(ci, new Rect2(pos.X, pos.Y + sink, w, h - sink), A(CapSide), rad);
        var face = new Rect2(pos.X, pos.Y + sink, w, h - CapSideH);
        Box(ci, face, A(top), rad, A(CapBorder), 1f);
        ci.DrawRect(new Rect2(face.Position.X + rad * 0.6f, face.Position.Y + 1f, w - rad * 1.2f, 1f), A(new Color(1, 1, 1, 0.12f)));
        if (CapIsDpad(token))
        {
            // 十字キー：中心に太さ 4px の十字（縦棒＋横棒）。文字より形のほうが一目で分かる。
            var c = face.GetCenter();
            float arm = (face.Size.Y - 6f) / 2f, t = Mathf.Max(3f, 4f * k);
            ci.DrawRect(new Rect2(c.X - t / 2f, c.Y - arm, t, arm * 2f), A(CapInk, 0.9f));
            ci.DrawRect(new Rect2(c.X - arm, c.Y - t / 2f, arm * 2f, t), A(CapInk, 0.9f));
            return w;
        }
        if (CapIsArrows(token))
        {
            // 矢印を左から等間隔に。軸 1.6px ＋ 先端の三角（半幅 2.7px）。枠の中央に並びごと寄せる（寸法は高さに比例）。
            var mid = face.GetCenter();
            float slot = ArrowSlot * k, gap = ArrowGap * k;
            float span = token.Length * slot + (token.Length - 1) * gap;
            float ax = mid.X - span / 2f + slot / 2f;
            float half = 5f * k, head = 3.2f * k, hw = 2.7f * k;
            foreach (char ch in token)
            {
                var c = new Vector2(ax, mid.Y);
                Vector2 d = ch switch { '↑' => Vector2.Up, '↓' => Vector2.Down, '←' => Vector2.Left, _ => Vector2.Right };
                Vector2 n = new(-d.Y, d.X);
                Vector2 tip = c + d * half, baseC = tip - d * head;
                ci.DrawLine(c - d * half, baseC, A(CapInk), Mathf.Max(1.2f, 1.6f * k), true);
                ci.DrawColoredPolygon(new[] { tip, baseC + n * hw, baseC - n * hw }, A(CapInk));
                ax += slot + gap;
            }
            return w;
        }
        FontFile f = pill ? Mono : CapFont(token);
        int size = pill ? fs - 1 : fs;
        float a2 = f.GetAscent(size), d2 = f.GetDescent(size);
        ci.DrawString(f, new Vector2(face.Position.X, face.Position.Y + (face.Size.Y + a2 - d2) / 2f), token,
            HorizontalAlignment.Center, w, size, A(CapInk));
        return w;
    }

    // マウスの絵（高さ h・幅 KeyCapW）。楕円の胴＋左右ボタンの境の横線＋中央の縦線＋中央上の縦長ホイール。
    //   該当するボタン（左／右＝その半分の上側、中クリック／ホイール＝ホイール）を Info で塗る。
    //   キーキャップと同じく下 2px に影を落とし、pressed で 1px 沈む＝並べたときに段差が揃う。
    private static void DrawMouseCap(CanvasItem ci, Vector2 pos, string token, float w, float h, float sink, Color top, float alpha)
    {
        Color A(Color c) => new(c, c.A * alpha);
        var body = new Rect2(pos.X + 1f, pos.Y + sink, w - 2f, h - CapSideH);
        Vector2 c = body.GetCenter();
        float rx = body.Size.X / 2f, ry = body.Size.Y / 2f;
        const int Seg = 28;
        Vector2 P(Vector2 cc, float a) => cc + new Vector2(rx * Mathf.Cos(a), ry * Mathf.Sin(a));
        Vector2[] Ellipse(Vector2 cc)
        {
            var pts = new Vector2[Seg];
            for (int i = 0; i < Seg; i++) pts[i] = P(cc, Mathf.Tau * i / Seg);
            return pts;
        }
        ci.DrawColoredPolygon(Ellipse(c + new Vector2(0, CapSideH - sink)), A(CapSide));
        ci.DrawColoredPolygon(Ellipse(c), A(top));

        // 左右ボタンの境（中心より少し上）。s＝境の高さ（胴の半径に対する比。負＝上）。小さいとき（h≦24）は
        //   ボタンの面を広く取るため中心まで下げる＝塗った側が 20px でも読める。
        float s = h <= 24f ? 0f : -0.12f, splitY = c.Y + ry * s;
        float a0 = Mathf.Pi - Mathf.Asin(s);          // 左の弧の始点（境の左端）
        float a1 = Mathf.Tau + Mathf.Asin(s);         // 右の弧の終点（境の右端）
        Vector2[] Region(float from, float to, bool splitFirst)
        {
            var pts = new System.Collections.Generic.List<Vector2>();
            if (splitFirst) pts.Add(new Vector2(c.X, splitY));
            const int n = 10;
            for (int i = 0; i <= n; i++) pts.Add(P(c, Mathf.Lerp(from, to, i / (float)n)));
            if (!splitFirst) pts.Add(new Vector2(c.X, splitY));
            return pts.ToArray();
        }
        Color on = new(Info, 0.95f);
        if (token == "左クリック") ci.DrawColoredPolygon(Region(a0, Mathf.Pi * 1.5f, true), A(on));
        else if (token == "右クリック") ci.DrawColoredPolygon(Region(Mathf.Pi * 1.5f, a1, false), A(on));

        var outline = Ellipse(c);
        var closed = new Vector2[Seg + 1];
        System.Array.Copy(outline, closed, Seg);
        closed[Seg] = outline[0];
        ci.DrawPolyline(closed, A(CapBorder), 1f, true);
        float dx = rx * Mathf.Sqrt(1f - s * s);
        ci.DrawLine(new Vector2(c.X - dx, splitY), new Vector2(c.X + dx, splitY), A(CapBorder), 1f, true);
        ci.DrawLine(new Vector2(c.X, c.Y - ry), new Vector2(c.X, splitY), A(CapBorder), 1f, true);

        // ホイール（中央の小さな縦長）。中クリック／ホイールなら Info、ほかは縁寄りの色で形だけ。
        bool wheel = token is "中クリック" or "ホイール";
        float ww = Mathf.Max(2f, rx * 0.36f), wh = ry * 0.46f;
        var wr = new Rect2(c.X - ww / 2f, c.Y - ry * 0.78f, ww, wh);
        //   塗るときは縁を描かない（幅 3px ほどなので 1px の縁で塗りが消える）。
        if (wheel) Box(ci, wr.Grow(0.5f), A(on), ww / 2f);
        else Box(ci, wr, A(CapTop.Lerp(CapBorder, 0.6f)), ww / 2f, A(CapBorder), 1f);
    }

    // パッドの面ボタンの文字色（Xbox：A 緑・B 赤・X 青・Y 黄／PS：〇 赤・× 青・□ 桃・△ 緑）。
    private static Color PadFaceColor(string t) => t switch
    {
        "A" => new Color(0.42f, 0.80f, 0.36f),
        "B" => new Color(0.93f, 0.36f, 0.33f),
        "X" => new Color(0.36f, 0.60f, 0.98f),
        "Y" => new Color(0.97f, 0.80f, 0.28f),
        "〇" => new Color(0.93f, 0.36f, 0.33f),
        "×" => new Color(0.46f, 0.64f, 0.98f),
        "□" => new Color(0.93f, 0.52f, 0.78f),
        "△" => new Color(0.36f, 0.80f, 0.70f),
        _ => CapInk,
    };

    // ── キャップの並び（1つの操作に割り当たったキー群）──
    //   spec は表や案内に書いてきた文字列そのまま（例 "Z / Enter / Space"・"Shift 長押し"・"R / Shift+R"・
    //   "L スティック / 十字キー"・"左クリック 長押し"・"オート"）。これを読んで:
    //     ・" / " で区切った並記 → キャップを 4px 間隔で並べる（区切りの「/」は描かない）
    //     ・空白で区切った語のうち、キー名（英数記号・矢印・十字・≡・マウス・パッド記号）→ キャップ、
    //       それ以外の語（長押し／を離す／スティック／オート／— 等）→ 小さな文字で添える
    //     ・"Shift+R" のような同時押し → キャップ「+」キャップ
    //   ci=null なら描かずに幅だけ返す（KeyCapRowW）。ink は添え文字の色。
    private static bool CapIsKeyWord(string w)
    {
        if (w.Length == 0) return false;
        if (CapIsMouse(w) || CapIsDpad(w) || CapIsArrows(w) || CapIsMenu(w)) return true;
        if (w is "〇" or "○" or "×" or "□" or "△") return true;
        foreach (char ch in w) if (ch <= ' ' || ch > '~') return false;
        return true;
    }
    private const float CapRowGap = 4f;

    public static float KeyCapRowW(string spec, float h = 22f, bool? pad = null) => KeyCapRow(null, Vector2.Zero, spec, h, 1f, pad);

    public static float KeyCapRow(CanvasItem? ci, Vector2 pos, string spec, float h = 22f, float alpha = 1f, bool? pad = null,
        Color? ink = null, bool pressed = false)
    {
        int ts = Mathf.Max(10, Mathf.RoundToInt(12f * CapK(h)));
        float asc = ZenBold.GetAscent(ts), desc = ZenBold.GetDescent(ts);
        // 添え文字はキャップの上面（側面 2px を除いた高さ）の縦中央に揃える。
        float baseY = pos.Y + (h - CapSideH + asc - desc) / 2f;
        Color inkC = ink ?? Text2;
        Color col = new(inkC, inkC.A * alpha);
        float x = pos.X;
        float Word(string text)
        {
            if (ci != null) ci.DrawString(ZenBold, new Vector2(x, baseY), text, HorizontalAlignment.Left, -1, ts, col);
            return TextW(ZenBold, text, ts);
        }
        float Cap(string token) =>
            ci != null ? KeyCap(ci, new Vector2(x, pos.Y), token, h, pressed, alpha, pad) : KeyCapW(token, h, pad);
        bool firstGroup = true;
        foreach (string group in spec.Split(" / "))
        {
            if (!firstGroup) x += CapRowGap;
            firstGroup = false;
            bool firstWord = true;
            foreach (string word in group.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (!firstWord) x += CapRowGap;
                firstWord = false;
                if (word.Length > 1 && word.Contains('+') && CapIsKeyWord(word))
                {
                    var keys = word.Split('+');
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (i > 0) { x += 2f; x += Word("+"); x += 2f; }
                        x += Cap(keys[i]);
                    }
                }
                else if (CapIsKeyWord(word)) x += Cap(word);
                else x += Word(word);
            }
        }
        return x - pos.X;
    }

    // ヒント帯の寸法（HintBar と当たり判定で共有）。
    private const float HintCapH = 22f, HintItemGap = 18f, HintLabelGap = 6f;
    private const int HintLabelSize = 12;

    // ヒント帯の各項目（[cap] label）の矩形。rightBottom＝帯の右下。右寄せで並べる。
    //   クリックを受けたい画面（Hub の「メニュー」等）と QA がこの1本で位置を知る。
    public static Rect2[] HintBarRects(Vector2 rightBottom, params (string token, string label)[] items)
    {
        var rects = new Rect2[items.Length];
        float x = rightBottom.X, y = rightBottom.Y - HintCapH;
        for (int i = items.Length - 1; i >= 0; i--)
        {
            float w = KeyCapW(items[i].token, HintCapH) + HintLabelGap + TextW(ZenBold, items[i].label, HintLabelSize);
            x -= w;
            rects[i] = new Rect2(x, y, w, HintCapH);
            x -= HintItemGap;
        }
        return rects;
    }

    // 右下基準で [cap] label の並びを右寄せに描く。項目間 18px、cap と label の間 6px。
    public static void HintBar(CanvasItem ci, Vector2 rightBottom, params (string token, string label)[] items)
    {
        var rects = HintBarRects(rightBottom, items);
        float asc = ZenBold.GetAscent(HintLabelSize), desc = ZenBold.GetDescent(HintLabelSize);
        for (int i = 0; i < items.Length; i++)
        {
            var r = rects[i];
            float cw = KeyCap(ci, r.Position, items[i].token, HintCapH);
            // ラベルはキャップの上面（側面 2px を除いた高さ）の縦中央に揃える。
            float baseY = r.Position.Y + (HintCapH - CapSideH + asc - desc) / 2f;
            ci.DrawString(ZenBold, new Vector2(r.Position.X + cw + HintLabelGap, baseY), items[i].label,
                HorizontalAlignment.Left, -1, HintLabelSize, Text2);
        }
    }

    // ── スマホ系画面（ハブ・ショップ・記録・カスタマイズ・難易度選択）の右下ヒント帯（2026-09-27）──
    //   どの画面も同じ位置（右下 (1264, 708) 基準）・同じ下敷き・同じ作法で出す。PauseMenu の右下「M メニュー」
    //   チップはこの帯に統合して廃止した（帯の「メニュー」がクリックでも開く）。
    public static Vector2 HintAnchor => new(DesignW - 16f, DesignH - 12f);

    // 帯の外接矩形（項目が無ければ大きさ 0）。
    public static Rect2 HintBarBounds(Vector2 rightBottom, (string token, string label)[] items)
    {
        if (items.Length == 0) return new Rect2();
        var rects = HintBarRects(rightBottom, items);
        return rects[0].Merge(rects[^1]);
    }

    // label の項目の矩形（その項目が無ければ大きさ 0）。
    public static Rect2 HintItemRect(Vector2 rightBottom, (string token, string label)[] items, string label)
    {
        var rects = HintBarRects(rightBottom, items);
        for (int i = 0; i < items.Length; i++) if (items[i].label == label) return rects[i];
        return new Rect2();
    }

    // label の項目がこのフレームに左クリックされたか（クリックで押せる項目＝「メニュー」等の判定に使う）。
    public static bool HintItemClicked(Vector2 rightBottom, (string token, string label)[] items, string label)
    {
        var r = HintItemRect(rightBottom, items, label);
        return r.HasArea() && Pad.MouseClick() && r.HasPoint(Pad.MousePos());
    }

    // 帯を下敷きごと描く。clickable に挙げた項目にマウスが乗っているときだけ、その項目に明るい下敷きを敷く
    //   ＝押せることを見せる（旧チップの作法）。
    public static void HintBarPlate(CanvasItem ci, Vector2 rightBottom, (string token, string label)[] items, params string[] clickable)
    {
        if (items.Length == 0) return;
        Box(ci, HintBarBounds(rightBottom, items).Grow(6f), new Color(0.05f, 0.06f, 0.08f, 0.88f), 9f, new Color(1, 1, 1, 0.07f), 1f);
        if (Pad.UsingMouse)
            foreach (string label in clickable)
            {
                var r = HintItemRect(rightBottom, items, label);
                if (r.HasArea() && r.HasPoint(Pad.MousePos()))
                    Box(ci, r.Grow(4f), new Color(Purify, 0.14f), 8f, new Color(Info, 0.5f), 1f);
            }
        HintBar(ci, rightBottom, items);
    }

    // スマホ系画面の定番の並び。パッド＝十字／A／B／≡、キーボード＝矢印／Z／Esc／M。
    //   nav＝キーボードの「えらぶ」の矢印（"↑↓" 等。空なら「えらぶ」を出さない）、ok＝決定のラベル（空なら出さない）、
    //   back＝もどるのラベル（空なら出さない）。extra はキーボードだけに足す項目（「←→ きりかえ」等）で、えらぶの直後に入る
    //   （パッドは十字1つで上下左右を兼ねるので足さない）。
    public static (string token, string label)[] PhoneHints(string nav, string ok, string back,
        params (string token, string label)[] extra)
    {
        bool pad = Pad.UsingPad;
        var list = new System.Collections.Generic.List<(string, string)>();
        if (nav.Length > 0) list.Add((pad ? "十字" : nav, "えらぶ"));
        if (!pad) list.AddRange(extra);
        if (ok.Length > 0) list.Add((pad ? "A" : "Z", ok));
        if (back.Length > 0) list.Add((pad ? "B" : "Esc", back));
        list.Add((pad ? "≡" : "M", "メニュー"));
        return list.ToArray();
    }
}
