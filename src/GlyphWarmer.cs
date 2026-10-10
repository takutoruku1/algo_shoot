using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;

// 会話本文の字形を、文字がまだ画面に出ていない時間に先に作っておく（2026-10-09）。
//   本文フォント（DialogueBox.Body＝ZenBold・MSDF）の字形は、初めて使う瞬間に1字 約11〜15ms かけて生成される。
//   新しい行の頭でその行の初出の字がまとめて生成され、70〜150ms 固まっていた＝文字送りのカクつき
//  （DialogueFrameQa の計測）。一度作った字形はプロセス内でキャッシュされ、2回目は 0.03ms。
//   そこで「タイトルの裏でかな・英数・記号」「LoadingScreen の裏で行き先の台本」を先に作る。
//   ・メインスレッドで1フレーム BudgetMs まで。1字は分割できないので、毎フレーム最低1字は進める。
//     別スレッドから生成させると Godot が落ちる（2026-10-09 検証）ので使わない。
//   ・見た目は変えない（MSDF の設定・フォントはそのまま。生成の時期を前へずらすだけ）。
//   ・温め終わる前に本番の行が来ても、従来どおりその場で生成されるだけ（壊れない）。
public partial class GlyphWarmer : Node
{
    public const double BudgetMs = 6;
    public static bool Enabled = true;   // QA が「温めない」場合の数字を取るために切る

    // かな・英数・約物（どの場面の台本にも出る）。よく出る順＝約物→ひらがな→カタカナ→英数。
    private static readonly string BaseSet = BuildBaseSet();
    private static string BuildBaseSet()
    {
        var sb = new System.Text.StringBuilder("、。「」…！？ー―・『』（）：；～〜♪％＆＠＃＊＋－＝／｜〈〉《》【】〔〕“”‘’，．‥");
        for (char c = 'ぁ'; c <= 'ゖ'; c++) sb.Append(c);
        for (char c = 'ァ'; c <= 'ー'; c++) sb.Append(c);
        for (char c = '０'; c <= '９'; c++) sb.Append(c);
        for (char c = '!'; c <= '~'; c++) sb.Append(c);
        return sb.ToString();
    }

    private static GlyphWarmer? _instance;
    // 文字送りが進んだ最後のフレーム（DialogueBox.AdvanceReveal が付ける）。送り中は温めない：
    //   1字 約11ms の生成が送りのフレームに乗ると、それ自体が 25ms 超のカクつきになる（ハブで計測）。
    //   温めは、読んでいる間（ページを出し切った後の待ち）や演出・暗転の裏に回す。
    private static ulong _revealFrame = ulong.MaxValue;
    internal static void NoteReveal() => _revealFrame = Engine.GetProcessFrames();
    private static readonly Queue<char> Pending = new(), Urgent = new();
    private static readonly HashSet<char> Seen = new(), Done = new();
    private static readonly Dictionary<Type, List<string>> Scripts = new();

    // QA 用の数字：まだ待っている字数／作った字数／温めに使った時間の合計。
    public static int PendingCount => Pending.Count + Urgent.Count;
    public static int Warmed { get; private set; }
    public static double SpentMs { get; private set; }

    // 行き先のシーン → その場面で打ち出される台本を持つ型（静的な行配列・定数を全部拾う）。
    private static Type[] ScriptTypes(string scenePath) => System.IO.Path.GetFileNameWithoutExtension(scenePath) switch
    {
        "Prologue" => new[] { typeof(Prologue), typeof(OpeningFilm) },
        "Hub" => new[] { typeof(Hub), typeof(CompanionDialogue), typeof(CharacterStoryTalk) },
        "Akari" => new[] { typeof(StageAkari), typeof(BossAkari), typeof(StageTutorial), typeof(CompanionDialogue) },
        "Koharu" => new[] { typeof(StageKoharu), typeof(BossKoharu), typeof(StageTutorial), typeof(CompanionDialogue) },
        "Rei" => new[] { typeof(StageRei), typeof(BossRei), typeof(StageTutorial), typeof(CompanionDialogue) },
        "MinaBattle" => new[] { typeof(StageMina), typeof(BossMina), typeof(StageTutorial), typeof(CompanionDialogue) },
        "Stage0" => new[] { typeof(StageZero), typeof(StageTutorial), typeof(Stage0Root) },
        "Final" => new[] { typeof(Final) },
        "Epilogue" => new[] { typeof(Epilogue), typeof(EndingFilm) },
        _ => Array.Empty<Type>(),
    };

    // タイトルの裏など、行き先が決まる前に呼ぶ。
    public static void WarmBase(Node from)
    {
        Enqueue(BaseSet);
        Ensure(from);
    }

    // LoadingScreen が開いた瞬間に呼ぶ（読み込みと暗転の裏で、行き先の台本の字を作る）。
    public static void WarmScene(Node from, string scenePath)
    {
        Enqueue(BaseSet);
        foreach (var type in ScriptTypes(scenePath))
            foreach (string text in ScriptOf(type)) Enqueue(text);
        Ensure(from);
    }

    // いま始まった会話の行を、ほかの待ちより先に作る（ハブの会話のように、場面の台本が大きくて
    //   読み込み中に作り切れないところ用）。最初のページは間に合わないことがあるが、2ページ目以降は
    //   読んでいる間に作り終わる。
    public static void WarmFirst(Node from, IEnumerable<string> texts)
    {
        foreach (string text in texts)
            foreach (char c in text)
                if (!char.IsWhiteSpace(c) && !char.IsSurrogate(c) && !Done.Contains(c))
                {
                    Seen.Add(c);
                    Urgent.Enqueue(c);
                }
        Ensure(from);
    }

    // c の字形がもう作ってあるか（QA が、重いフレームの原因が字形かどうかを見分けるのに使う）。
    internal static bool IsWarm(char c) => Done.Contains(c);

    internal static IReadOnlyList<string> ScriptOf(Type type)
    {
        if (Scripts.TryGetValue(type, out var found)) return found;
        var texts = new List<string>();
        void Collect(object? value, int depth)
        {
            if (value == null || depth > 6) return;
            switch (value)
            {
                case string s: texts.Add(s); break;
                case System.Runtime.CompilerServices.ITuple t: for (int i = 0; i < t.Length; i++) Collect(t[i], depth + 1); break;
                case System.Collections.IDictionary d:
                    foreach (System.Collections.DictionaryEntry e in d) { Collect(e.Key, depth + 1); Collect(e.Value, depth + 1); }
                    break;
                case System.Collections.IEnumerable e: foreach (var x in e) Collect(x, depth + 1); break;
            }
        }
        foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var t = field.FieldType;
            if (t != typeof(string) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) continue;
            try { Collect(field.GetValue(null), 0); } catch (Exception) { /* 初期化に失敗する静的フィールドは飛ばす（温めは補助） */ }
        }
        // メソッドの中に直接書かれた台詞（ReturnDialog の switch など）も拾う：IL の文字列リテラルを読む。
        Literals(type, texts);
        Scripts[type] = texts;
        return texts;
    }

    // 型（入れ子の型＝ラムダやイテレータの実体も含む）の全メソッドから ldstr の文字列を集める。
    private static void Literals(Type type, List<string> texts)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var methods = new List<MethodBase>(type.GetMethods(all));
        methods.AddRange(type.GetConstructors(all));
        if (type.TypeInitializer != null) methods.Add(type.TypeInitializer);
        foreach (var method in methods)
        {
            byte[]? il;
            try { il = method.GetMethodBody()?.GetILAsByteArray(); } catch (Exception) { continue; }
            if (il == null) continue;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                // ldstr = 0x72 ＋ 4バイトの文字列トークン（上位バイト 0x70）。オペランドの途中の 0x72 を拾っても、
                //   トークンとして解決できなければ捨てる。
                if (il[i] != 0x72 || il[i + 4] != 0x70) continue;
                try { texts.Add(method.Module.ResolveString(BitConverter.ToInt32(il, i + 1))); i += 4; }
                catch (Exception) { }
            }
        }
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) Literals(nested, texts);
    }

    private static void Enqueue(string text)
    {
        foreach (char c in text)
            if (!char.IsWhiteSpace(c) && !char.IsSurrogate(c) && Seen.Add(c)) Pending.Enqueue(c);
    }

    private static void Ensure(Node from)
    {
        if (IsInstanceValid(_instance) || !from.IsInsideTree()) return;
        _instance = new GlyphWarmer { Name = "GlyphWarmer", ProcessMode = ProcessModeEnum.Always };
        // _Ready 中（root が子を組み立て中）でも足せるよう遅延で。
        from.GetTree().Root.CallDeferred(Node.MethodName.AddChild, _instance);
    }

    public override void _Process(double delta)
    {
        if (!Enabled || PendingCount == 0) return;
        // 自分はシーンより後に足されるので、同じフレームの送りはもう済んでいる＝今フレームか前フレームに送りがあれば休む。
        ulong frame = Engine.GetProcessFrames();
        if (_revealFrame != ulong.MaxValue && frame - _revealFrame <= 1) return;
        var font = DialogueBox.Body.Font as FontFile;
        if (font == null) { Pending.Clear(); return; }
        var size = new Vector2I(DialogueBox.Body.Size, 0);
        ulong start = Time.GetTicksUsec();
        double spent = 0;
        while (PendingCount > 0 && spent < BudgetMs)
        {
            char c = Urgent.Count > 0 ? Urgent.Dequeue() : Pending.Dequeue();
            if (!Done.Add(c)) continue;
            // RenderRange は描画が使うのと同じキャッシュ（0番）へ作る。シェイプして作らせるより 3割ほど速い。
            if (font.HasChar(c)) { font.RenderRange(0, size, c, c); Warmed++; }
            spent = (Time.GetTicksUsec() - start) / 1000.0;
        }
        SpentMs += spent;
    }
}
