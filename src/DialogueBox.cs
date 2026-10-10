using Godot;
using System.Collections.Generic;

public static class DialogueBox
{
    public static readonly Rect2 FullScreen = new(72, 504, 1136, 196);
    public static Rect2 Board => new(Field.DLeft + 20, FullScreen.Position.Y, Field.DWidth - 40, FullScreen.Size.Y);
    public static readonly Color Surface = new("101820"), Border = new("53616d"), Ink = new("eef3f5");
    public static UiKit.TextStyle Body => new(UiKit.ZenBold, 24, 0, 1.2f);
    public const float Padding = 24, HeaderHeight = 54;
    private static Texture2D? _draft;
    private const double CharacterFade = 0.09;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CanvasItem, BodyLayout> Layouts = new();

    private readonly record struct Glyph(Rid Font, int Size, int Index, Vector2 Position, int End);

    private sealed class BodyLayout
    {
        public string Page = "";
        public readonly List<TextLine> Lines = new();
        public readonly List<Glyph> Glyphs = new();

        public void SetPage(string page)
        {
            if (Page == page) return;
            foreach (var line in Lines) line.Dispose();
            Lines.Clear();
            Glyphs.Clear();
            Page = page;
            var server = TextServerManager.GetPrimaryInterface();
            int offset = 0;
            float y = 0;
            foreach (string text in page.Split('\n'))
            {
                var line = new TextLine();
                line.AddString(text, Body.Font, Body.Size);
                Lines.Add(line);
                var utf16 = new List<int> { 0 };
                foreach (var rune in text.EnumerateRunes()) utf16.Add(utf16[^1] + rune.Utf16SequenceLength);
                float x = 0;
                // Shape the complete line once so kerning and combined characters never shift during reveal.
                foreach (var glyph in server.ShapedTextGetGlyphs(line.GetRid()))
                {
                    for (int repeat = 0; repeat < (int)glyph["repeat"]; repeat++)
                    {
                        Glyphs.Add(new((Rid)glyph["font_rid"], (int)glyph["font_size"], (int)glyph["index"],
                            new Vector2(x, y) + (Vector2)glyph["offset"],
                            offset + utf16[(int)glyph["end"]]));
                        x += (float)glyph["advance"];
                    }
                }
                offset += text.Length + 1;
                y += Body.LineHeight;
            }
        }
    }

    public const float DefaultCharsPerSec = 20f;
    public const double ReadPause = 1.0;
    public static float Speed(int setting) => setting switch { 0 => 14f, 2 => 32f, _ => DefaultCharsPerSec };

    public static List<string> Paginate(string text, float width)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r", "").Split("\n\n"))
        {
            if (lines.Count > 0) lines.Add("");
            lines.AddRange(WrapDialogue(paragraph, width));
        }
        return UiKit.Paginate(Body, string.Join("\n", lines), width, Hud.DlgMaxLines);
    }

    private static List<string> WrapDialogue(string text, float width)
    {
        var explicitBreaks = new HashSet<int>();
        int length = 0;
        foreach (char c in text)
            if (c == '\n') explicitBreaks.Add(length); else length++;
        text = text.Replace("\n", "");
        var lines = new List<string>();
        if (text.Length == 0) return new() { "" };
        float maxWidth = Mathf.Min(width, Body.Size * 28);
        float minWidth = Mathf.Min(Body.Size * 8, maxWidth * 0.4f);
        var words = UiKit.WordBoundaries(text);
        var stops = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        float Measure(string s) => UiKit.TextW(Body.Font, s, Body.Size);
        int start = 0;
        while (start < text.Length)
        {
            string rest = text[start..];
            float remainingWidth = Measure(rest);
            float target = Mathf.Min(Body.Size * 22, remainingWidth / 2);
            int best = -1;
            float bestScore = float.MaxValue;
            foreach (int at in stops)
            {
                if (at <= start) continue;
                float headWidth = Measure(text[start..at]);
                if (headWidth > maxWidth) break;
                bool authored = explicitBreaks.Contains(at) && at - start >= 6;
                if ((!authored && headWidth < minWidth) || Measure(text[at..]) < minWidth || !UiKit.CanBreakLine(text, at)) continue;
                if (UiKit.IsWordChar(text[at - 1]) && UiKit.IsWordChar(text[at])) continue;
                int rank = UiKit.IsSentenceEnd(text, at) || authored ? 0
                    : text[at - 1] is '、' or '，' or ',' or '；' or ';' ? 1
                    : words[at] ? 2 : 3;
                if (rank == 3 || (rank > 0 && remainingWidth <= Mathf.Min(maxWidth, Body.Size * 22))) continue;
                float score = Mathf.Abs(headWidth - target) / Body.Size + rank * 6;
                if (score >= bestScore) continue;
                best = at; bestScore = score;
            }
            if (best < 0)
            {
                var wrapped = UiKit.WrapLines(Body.Font, rest, Body.Size, maxWidth);
                best = start + wrapped[0].Length;
            }
            lines.Add(text[start..best]);
            start = best;
        }
        return lines;
    }

    // 小数部は「次の1文字までの経過」。1文字の時間は CharacterTime ＋ 台本の間（DialoguePacing）。
    public static double AdvanceReveal(string text, double shown, double delta, float speed = DefaultCharsPerSec,
        DialoguePacing.Page? pacing = null)
    {
        while (shown < text.Length && delta > 0)
        {
            int index = (int)shown;
            double step = CharacterTime(text, index, speed) + (pacing?.Before(index) ?? 0);
            double remaining = (1 - (shown - index)) * step;
            if (delta < remaining)
            {
                GlyphWarmer.NoteReveal();   // まだ送りの途中＝字形の先作りを休ませる（GlyphWarmer）
                return shown + delta / step;
            }
            delta -= remaining;
            shown = index + 1;
        }
        return shown;
    }

    // 1文字の所要時間。ドラクエの文字送りのように、どの文字も同じ速さで刻む（2026-10-09）。
    //   以前は行頭 0.12s／「、」0.12s／「。！？」0.24s／「…」0.36s の溜めを暗黙に全行へ入れていたが、
    //   20文字/秒ではそのひとつひとつが「進む→止まる→進む」に見え、文字送り全体がカクついていた。
    //   息継ぎが要る所は台本側で DialoguePacing の Beat／Lead／Tail として置く（作者が選んだ所だけ止まる）。
    //   改行は画面に何も出さないので時間を取らない（取ると行替えの瞬間だけ字が止まって見える）。
    private static double CharacterTime(string text, int index, float speed)
        => text[index] == '\n' ? 0 : 1.0 / speed;

    // ── 文字送りの送り音（ドラクエの「ピッ」・2026-10-09）──
    //   shown の整数部が進んだフレームで1回、いま出た文字の位置で PlayType（話者の音色）を鳴らす。
    //   1フレームで何文字進んでも鳴らすのは1回だけ＝処理落ちで数文字まとめて出ても連打しない。
    //   早送り（全文即表示）は AdvanceReveal を通らないので、呼び手が AdvanceReveal の直後にだけ呼べば鳴らない。
    //   改行・空白が出ただけのフレームは鳴らさない。cursor は呼び手ごとに1つ持ち、ページが替われば自動で頭に戻る。
    public struct TypeCursor { internal string? Page; internal int Shown; }
    public static int TypeSounds { get; private set; }   // 鳴らした回数（QA が配線を数える）

    public static void TypeSound(Hud.LineKind kind, string page, double shown, ref TypeCursor cursor)
    {
        int now = System.Math.Min((int)shown, page.Length);
        if (!ReferenceEquals(cursor.Page, page) || now < cursor.Shown) cursor = new TypeCursor { Page = page };
        if (now <= cursor.Shown) return;
        cursor.Shown = now;
        if (char.IsWhiteSpace(page[now - 1])) return;
        TypeSounds++;
        Audio.Instance?.PlayType(kind, page, now - 1);
    }

    public static double RevealDuration(string text, float speed = DefaultCharsPerSec, DialoguePacing.Page? pacing = null)
    {
        double duration = 0;
        for (int i = 0; i < text.Length; i++) duration += CharacterTime(text, i, speed) + (pacing?.Before(i) ?? 0);
        return duration;
    }

    public static double CaptionDuration(List<string> pages, DialoguePacing.Page[]? pacing = null)
    {
        double duration = 0;
        for (int i = 0; i < pages.Count; i++)
            duration += RevealDuration(pages[i], pacing: pacing?[i]) + (pacing?[i].AutoWait ?? ReadPause);
        return duration;
    }

    public static (string Page, double Shown, DialoguePacing.Page? Pacing) CaptionAt(List<string> pages, double elapsed, DialoguePacing.Page[]? pacing = null)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            double duration = RevealDuration(pages[i], pacing: pacing?[i]) + (pacing?[i].AutoWait ?? ReadPause);
            if (elapsed < duration || i == pages.Count - 1)
                return (pages[i], AdvanceReveal(pages[i], 0, System.Math.Max(0, elapsed), pacing: pacing?[i]), pacing?[i]);
            elapsed -= duration;
        }
        return ("", 0, null);
    }

    public static Vector2 Anchor(Rect2 box) => new(box.End.X, box.Position.Y);
    public static float WrapWidth(Rect2 box) => box.Size.X - Padding * 2;
    public static Vector2 TextPosition(Rect2 box) => box.Position + new Vector2(Padding, 66);

    public static void DrawFrame(CanvasItem ci, Rect2 box, string speaker, Color accent,
        Texture2D? portrait = null, bool draft = false, float alpha = 1, float faceTop = 0)
    {
        UiKit.Box(ci, new Rect2(box.Position + new Vector2(0, 4), box.Size), new Color("05090d", 0.24f * alpha), 8);
        UiKit.Box(ci, box, new Color(Surface, 0.96f * alpha), 8, new Color(Border, 0.7f * alpha), 1);
        ci.DrawLine(box.Position + new Vector2(Padding, HeaderHeight),
            new Vector2(box.End.X - Padding, box.Position.Y + HeaderHeight), new Color(Border, 0.4f * alpha), 1, true);
        ci.DrawLine(box.Position + new Vector2(Padding, 0), box.Position + new Vector2(Padding + 40, 0),
            new Color(accent, 0.9f * alpha), 2, true);
        var center = box.Position + new Vector2(40, 27);
        if (portrait != null)
            UiKit.FaceAvatar(ci, center, 17, portrait, accent, false, faceTop, alpha);
        else if (draft)
        {
            _draft ??= GD.Load<Texture2D>("res://char/ui/dialogue_you_v1.png");
            var size = _draft.GetSize() * (32f / Mathf.Max(_draft.GetWidth(), _draft.GetHeight()));
            ci.DrawTextureRect(_draft, new Rect2(center - size / 2, size), false, new Color(Colors.White, alpha));
        }
        else
            ci.DrawCircle(center, 3, new Color(accent, alpha));
        UiKit.Text(ci, UiKit.ZenBold, box.Position + new Vector2(70, 14), speaker, 18, new Color(accent, alpha));
    }

    // ends[i] = i 文字目までを出し終える時刻。shown（小数）をページ先頭からの経過秒に戻して返す。
    private static double RevealClock(string page, double shown, float speed, DialoguePacing.Page? pacing, System.Span<double> ends)
    {
        ends[0] = 0;
        for (int i = 0; i < page.Length; i++)
            ends[i + 1] = ends[i] + CharacterTime(page, i, speed) + (pacing?.Before(i) ?? 0);
        int complete = System.Math.Min((int)shown, page.Length);
        double elapsed = ends[complete];
        if (complete < page.Length) elapsed += (shown - complete) * (ends[complete + 1] - ends[complete]);
        return elapsed;
    }

    // 1文字のフェード（0..1）。その文字を出し終える時刻 end の CharacterFade 秒前から立ち上がる。
    private static float Fade(double elapsed, double end)
    {
        float fade = Mathf.Clamp((float)((elapsed - end + CharacterFade) / CharacterFade), 0, 1);
        return fade * fade * (3 - 2 * fade);
    }

    // 画面に出ているインクの総量（各文字のフェードの和・空白と改行は数えない）。QA が「このフレームで
    //   見た目が動いたか」を測るための物差し。描画と同じ時計とフェードを使う。
    internal static double VisibleInk(string page, double shown, float speed = DefaultCharsPerSec, DialoguePacing.Page? pacing = null)
    {
        if (shown <= 0 || page.Length == 0) return 0;
        System.Span<double> ends = stackalloc double[page.Length + 1];
        double elapsed = RevealClock(page, shown, speed, pacing, ends);
        double ink = 0;
        for (int i = 0; i < page.Length; i++)
            if (!char.IsWhiteSpace(page[i])) ink += Fade(elapsed, ends[i + 1]);
        return ink;
    }

    public static void DrawBody(CanvasItem ci, Rect2 box, string page, double shown, float alpha = 1, Color? ink = null,
        float speed = DefaultCharsPerSec, DialoguePacing.Page? pacing = null)
    {
        if (shown <= 0 || alpha <= 0 || page.Length == 0) return;
        var layout = Layouts.GetOrCreateValue(ci);
        layout.SetPage(page);
        System.Span<double> ends = stackalloc double[page.Length + 1];
        double elapsed = RevealClock(page, shown, speed, pacing, ends);
        var origin = TextPosition(box) + new Vector2(0, Body.Font.GetAscent(Body.Size));
        var server = TextServerManager.GetPrimaryInterface();
        foreach (var glyph in layout.Glyphs)
        {
            float fade = Fade(elapsed, ends[glyph.End]);
            if (fade > 0)
                server.FontDrawGlyph(glyph.Font, ci.GetCanvasItem(), glyph.Size, origin + glyph.Position,
                    glyph.Index, new Color(ink ?? Ink, alpha * fade));
        }
    }

    public static void DrawContinue(CanvasItem ci, Rect2 box, bool more)
    {
        var button = new Rect2(box.End.X - Padding - 100, box.End.Y - 32, 100, 24);
        bool hover = button.HasPoint(Pad.MousePos());
        UiKit.Box(ci, button, new Color(Ink, hover ? 0.12f : 0.04f), 4,
            new Color(Border, hover ? 0.9f : 0.55f), 1);
        UiKit.Text(ci, UiKit.ZenBold, button.Position + new Vector2(0, 2), more ? "つづき  ›" : "次へ  ›",
            13, new Color(Ink, hover ? 1 : 0.85f), HorizontalAlignment.Center, button.Size.X);
    }
}
