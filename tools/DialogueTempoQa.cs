using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class DialogueTempoQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print("[DialogueTempo] PASS " + message);
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.AutoAdvanceDialog = false;
            game.MsgCharsPerSec = DialogueBox.DefaultCharsPerSec;
            Hud.SkipLatched = false;
            float width = DialogueBox.WrapWidth(DialogueBox.FullScreen);
            const string text = "今夜は、こちらを。わたくしの目で見た空です。";
            var pages = DialogueBox.Paginate(text, width);
            Check(pages.SequenceEqual(new[] { "今夜は、こちらを。\nわたくしの目で見た空です。" }), "short clauses stay together and the sentence boundary makes a readable two-line page");
            Check(DialogueBox.Paginate("「聞こえる。」うん、\n届いてる。", width)
                .SequenceEqual(new[] { "「聞こえる。」うん、届いてる。" }), "closing quotes stay attached and short manual fragments join their phrase");
            Check(DialogueBox.Paginate("今、どうぞ。聞いています。", width)
                .SequenceEqual(new[] { "今、どうぞ。聞いています。" }), "a brief invitation is not chopped into two-character lines");
            Check(DialogueBox.Paginate("うん、\nそうだね。", width).SequenceEqual(new[] { "うん、そうだね。" }),
                "short source line breaks do not fragment a reply");
            foreach (string sample in new[] { "三秒。……今度は、取り消されませんでした。", "画面越しじゃ、ごはんは分けられないけど。おいしかった話なら、また持ってくる。" })
                Check(DialogueBox.Paginate(sample, 432f).SelectMany(p => p.Split('\n')).All(l => l.Length > 3),
                    "narrow hub dialogue also avoids tiny isolated lines");
            foreach (string token in new[] { "@hoshiai_rei", "10:08", "2,000", "Godot" })
                Check(DialogueBox.Paginate("記録を確認しました。こちらの" + token + "という表記を、そのまま残しておきます。", 432f)
                    .SelectMany(p => p.Split('\n')).Any(l => l.Contains(token)), "display tokens stay together: " + token);
            Check(DialogueBox.Speed(0) == 14f && DialogueBox.Speed(1) == 20f && DialogueBox.Speed(2) == 32f,
                "all three text-speed settings are slower");
            Check(DialogueBox.RevealDuration(text) > DialogueBox.RevealDuration(text, 28f), "default character reveal is slower than the previous standard");
            foreach (float wrap in new[] { width, DialogueBox.WrapWidth(DialogueBox.Board), 220f })
            {
                var result = DialogueBox.Paginate(text + text, wrap);
                Check(string.Concat(result).Replace("\n", "") == text + text, "pagination preserves every character");
                Check(result.All(p => p.Split('\n').Length <= 2 && p.Split('\n')
                    .All(l => UiKit.TextW(DialogueBox.Body.Font, l, DialogueBox.Body.Size) <= wrap + 0.1)), "pages fit two lines and the dialogue width");
            }
            double shown = DialogueBox.AdvanceReveal(text, 0, 0.4);
            Check(shown > 0 && shown < 9, "default dialogue is only partially revealed after 0.4 seconds");
            // ドラクエの文字送り：句読点や行頭で暗黙に溜めない。息継ぎは台本の Beat（DialoguePacing）で置く。
            foreach (float speed in new[] { DialogueBox.Speed(0), DialogueBox.Speed(1), DialogueBox.Speed(2) })
                Check(Math.Abs(DialogueBox.RevealDuration("あ、い。う！え？お……か", speed) - 12 / speed) < 0.0001
                    && Math.Abs(DialogueBox.RevealDuration("あいうえおかきくけこさし", speed) - 12 / speed) < 0.0001,
                    $"{speed} cps: punctuation and line starts take exactly one character time");
            CheckSteadyInk("あ、いう。えお！\nかき……くけ？こ", null, "unscripted punctuation");
            CheckSteadyInk(text, null, "a real two-sentence line");
            const string scripted = "潜れます。……潜れる、はずです。";
            var scriptedPacing = DialoguePacing.ForPages(scripted, new[] { scripted })[0];
            CheckSteadyInk(scripted, scriptedPacing, "an authored hesitation");
            double stepped = 0;
            for (int i = 0; i < 60; i++) stepped = DialogueBox.AdvanceReveal(text, stepped, 1.0 / 60);
            Check(Math.Abs(stepped - DialogueBox.AdvanceReveal(text, 0, 1)) < 0.0001, "reveal timing is independent of frame rate");
            CheckAuthoredTiming(width);
            await CheckSmoothDrawing();

            var hud = new Hud { HoldBubble = true };
            AddChild(hud);
            hud.SetProcess(false);
            string pagingText = text + text;
            var pagingPages = DialogueBox.Paginate(pagingText, width);
            Check(pagingPages.Count == 2, "long dialogue still has page breaks");
            hud.ShowDialog(Hud.LineKind.Mina, pagingText);
            for (int i = 0; i < 24; i++) hud._Process(1.0 / 60);
            Check(Read<float>(hud, "_dlgRevealed") < pagingPages[0].Length && !hud.DialogRevealed, "HUD uses gradual reveal");
            hud.RevealDialogNow();
            Check(Read<int>(hud, "_dlgPage") == 0 && Read<float>(hud, "_dlgRevealed") == pagingPages[0].Length, "first press reveals only the current page");
            hud._Process(0.01);
            hud.RevealDialogNow();
            Check(Read<int>(hud, "_dlgPage") == 1 && Read<float>(hud, "_dlgRevealed") == 0, "the next press immediately opens the next page without a forced reading pause");
            hud.RevealDialogNow();
            Check(hud.DialogRevealed, "the final page is immediately ready for a fresh manual press");
            hud.ShowDialog(Hud.LineKind.Mina, pagingText);
            game.AutoAdvanceDialog = true;
            for (int i = 0; i < 600 && !hud.AutoAdvanceReady; i++) hud._Process(1.0 / 60);
            Check(hud.AutoAdvanceReady && Read<int>(hud, "_dlgPage") == pagingPages.Count - 1, "AUTO reaches the final page and leaves reading time");
            hud.ShowDialog(Hud.LineKind.Mina, pagingText);
            hud.RevealDialogNow();
            hud._Process(0.5);
            Check(Read<int>(hud, "_dlgPage") == 0, "AUTO keeps a completed page visible before advancing");
            game.AutoAdvanceDialog = false;
            Hud.SkipLatched = true;
            hud._Process(1.0 / 60);
            hud._Process(1.0 / 60);
            Check(hud.DialogRevealed, "deliberate read skip still bypasses the pauses");
            Hud.SkipLatched = false;
            hud.ShowDialog(Hud.LineKind.Mina, "心配です。");
            hud._Process(0.3);
            Check((int)Read<float>(hud, "_dlgRevealed") == 0, "Mina takes a beat before the sincere reply");
            hud.RevealDialogNow();
            Check(hud.DialogRevealed, "manual reveal bypasses the authored afterglow without changing automatic pacing");
            hud.ShowDialog(Hud.LineKind.Other, "……好きだよ。いまも。");
            hud.RevealDialogNow();
            game.AutoAdvanceDialog = true;
            hud._Process(1.1);
            Check(!hud.AutoAdvanceReady, "AUTO leaves the longer confession afterglow intact");
            hud._Process(0.25);
            Check(hud.AutoAdvanceReady, "AUTO resumes after the confession afterglow");
            game.AutoAdvanceDialog = false;
            hud.ShowDialog(Hud.LineKind.Other, "……好きだよ。いまも。");
            Hud.SkipLatched = true;
            hud._Process(1.0 / 60);
            Check(hud.DialogRevealed, "read skip also bypasses authored holds");
            Hud.SkipLatched = false;
            hud.HideBubble();
            hud.QueueFree();
            await Frames(2);
            await CheckMidbossDialogue(game);

            var epilogue = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
            GetTree().Root.AddChild(epilogue);
            epilogue.SetProcess(false);
            Set(epilogue, "_zHeld", Pad.AdvanceHeld());
            for (int i = 0; i < 24; i++) epilogue._Process(1.0 / 60);
            Check(Read<double>(epilogue, "_reveal") < pages[0].Length, "epilogue uses the same gradual reveal");
            Check(Read<List<string>>(epilogue, "_pages").SequenceEqual(pages), "epilogue uses punctuation pagination");
            await Shot(epilogue, "epilogue_typing");
            for (int i = 0; i < 180; i++) epilogue._Process(1.0 / 60);
            await Shot(epilogue, "epilogue_page");
            Set(epilogue, "_phase", 1);

            foreach (Node2D film in new Node2D[] { new OpeningFilm(), new EndingFilm() })
            {
                epilogue.AddChild(film);
                film.SetProcess(false);
                var cuts = (double[])film.GetType().GetField("Cuts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                var captions = Read<List<List<string>>>(film, "_captionPages");
                var pacing = Read<List<DialoguePacing.Page[]>>(film, "_captionPacing");
                for (int i = 0; i < captions.Count; i++)
                {
                    Check(captions[i].All(p => p.Split('\n').Length <= 2), "film caption pages fit the dialogue box");
                    double lead = film is EndingFilm ? 0.25 : i switch { >= 1 and <= 3 => 0.2, 4 => 1, >= 5 and <= 8 => 0.48, 9 => 1.35, _ => 0 };
                    Check(DialogueBox.CaptionDuration(captions[i], pacing[i]) + lead <= cuts[i + 1] - cuts[i], "film leaves time for authored pauses and every page");
                }
                const int shot = 2;
                double start = cuts[shot] + (film is OpeningFilm ? 0.2 : 0.25);
                var current = DialogueBox.CaptionAt(captions[shot], 0.4);
                Check(current.Shown < current.Page.Length, "film text is not shown all at once");
                film.GetType().GetProperty("Elapsed")!.SetValue(film, start + 0.4);
                await Shot(film, film.GetType().Name + "_typing");
                film.GetType().GetProperty("Elapsed")!.SetValue(film, start + DialogueBox.RevealDuration(captions[shot][0]) + 0.5);
                await Shot(film, film.GetType().Name + "_page");
                film.QueueFree();
                await Frames(2);
            }
            epilogue.QueueFree();
            await Frames(2);
            await CheckFinalPacing(game);
            Audio.Instance?.StopMusic(0);
            foreach (var player in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>()) { player.Stop(); player.Stream = null; }
            await Frames(4);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[DialogueTempo] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task CheckSmoothDrawing()
    {
        if (DisplayServer.GetName() == "headless") return;
        await Frames(2);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        var canvas = new DialogueRevealQaCanvas();
        AddChild(canvas);
        string folder = ProjectSettings.GlobalizePath("res://build/qa_story/dialogue_tempo");
        DirAccess.MakeDirRecursiveAbsolute(folder);
        async Task<Image> Capture(double elapsed)
        {
            canvas.Shown = DialogueBox.AdvanceReveal(canvas.Page, 0, elapsed, canvas.Speed, canvas.Pacing);
            canvas.QueueRedraw();
            await Frames(3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            return GetViewport().GetTexture().GetImage();
        }
        static double Difference(Image a, Image b, Rect2I rect)
        {
            double total = 0;
            for (int y = rect.Position.Y; y < rect.End.Y; y++)
                for (int x = rect.Position.X; x < rect.End.X; x++)
                {
                    var first = a.GetPixel(x, y);
                    var second = b.GetPixel(x, y);
                    total += Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B);
                }
            return total;
        }
        foreach (int width in new[] { 960, 1280 })
        {
            DisplayServer.WindowSetSize(new Vector2I(width, width * 9 / 16));
            await Frames(4);
            float scale = width / UiKit.DesignW;
            var body = new Rect2I((int)(96 * scale), (int)(570 * scale), (int)(1088 * scale), (int)(88 * scale));
            var firstLetter = new Rect2I(body.Position, new Vector2I((int)(24 * scale), (int)(42 * scale)));
            canvas.Page = "あいうえお。\nAV office か\u3099 🙂";
            foreach (float speed in new[] { DialogueBox.Speed(0), DialogueBox.Speed(1), DialogueBox.Speed(2) })
            {
                canvas.Speed = speed;
                using var full = await Capture(100);
                canvas.Reference = true;
                using (var reference = await Capture(100))
                    Check(Difference(full, reference, body) < 1, $"{width}px: shaped glyphs match full text, including kerning and combined characters");
                canvas.Reference = false;
                double start = DialogueBox.RevealDuration("あ", speed) + 0.002;
                using var a = await Capture(start);
                using var b = await Capture(start + 1.0 / 60);
                using var c = await Capture(start + 2.0 / 60);
                if (Difference(a, b, body) <= 1 || Difference(b, c, body) <= 1)
                {
                    a.SavePng($"{folder}/smooth_failure_0.png");
                    b.SavePng($"{folder}/smooth_failure_1.png");
                    c.SavePng($"{folder}/smooth_failure_2.png");
                    GD.Print($"Reveal capture: {a.GetSize()}, window: {DisplayServer.WindowGetSize()}, deltas: {Difference(a, b, body)}, {Difference(b, c, body)}");
                }
                Check(Difference(a, b, body) > 1 && Difference(b, c, body) > 1,
                    $"{width}px, {speed} cps: successive frames change smoothly between whole characters");
                Check(Difference(a, full, firstLetter) < 1 && Difference(b, full, firstLetter) < 1,
                    "already revealed letters stay in exactly the same position");
                if (speed == DialogueBox.DefaultCharsPerSec)
                {
                    a.SavePng($"{folder}/smooth_{width}_0.png");
                    b.SavePng($"{folder}/smooth_{width}_1.png");
                    c.SavePng($"{folder}/smooth_{width}_2.png");
                    full.SavePng($"{folder}/smooth_{width}_full.png");
                }
            }
            canvas.Speed = DialogueBox.DefaultCharsPerSec;
            canvas.Page = "あ、いう";
            using var afterComma = await Capture(DialogueBox.RevealDuration("あ、") + 0.002);
            using var afterCommaNext = await Capture(DialogueBox.RevealDuration("あ、") + 0.002 + 1.0 / 60);
            Check(Difference(afterComma, afterCommaNext, body) > 1, "the letter after a comma keeps fading in on the next frame");
            canvas.Page = "心配です。";
            canvas.Pacing = DialoguePacing.ForPages(canvas.Page, new[] { canvas.Page })[0];
            using var empty = await Capture(0);
            using var lead = await Capture(0.3);
            Check(Difference(empty, lead, body) < 1, "authored lead-in remains empty until the reveal begins");
            canvas.Pacing = null;
        }
        canvas.QueueFree();
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(3);
    }

    // 60fps で1フレームずつ送り、画面のインク（DialogueBox.VisibleInk）が毎フレーム増えることを見る。
    //   止まってよいのは台本の Lead/Beat の位置だけ（その間は直前の言葉を残して待つ）。
    private static void CheckSteadyInk(string page, DialoguePacing.Page? pacing, string label)
    {
        foreach (float speed in new[] { DialogueBox.Speed(0), DialogueBox.Speed(1), DialogueBox.Speed(2) })
        {
            double shown = 0, ink = 0;
            int stalls = 0, authored = 0;
            string where = "";
            while (shown < page.Length)
            {
                double next = DialogueBox.AdvanceReveal(page, shown, 1.0 / 60, speed, pacing);
                double nextInk = DialogueBox.VisibleInk(page, next, speed, pacing);
                if (nextInk - ink < 1e-9 && next < page.Length)
                {
                    if ((pacing?.Before((int)shown) ?? 0) > 0 || (pacing?.Before((int)next) ?? 0) > 0) authored++;
                    else { stalls++; where = $"{(int)shown} after \"{(shown >= 1 ? page[(int)shown - 1] : '^')}\""; }
                }
                shown = next;
                ink = nextInk;
            }
            Check(stalls == 0, $"{speed} cps, {label}: text moves on every frame ({stalls} stalled frames{(stalls > 0 ? ", last at " + where : "")}; {authored} authored)");
            if (pacing != null && pacing.Beats.Count > 0)
                Check(authored > 0, $"{speed} cps, {label}: the authored beat still holds the screen");
        }
    }

    private static void CheckAuthoredTiming(float width)
    {
        foreach (var (source, cue) in DialoguePacing.Cues)
        {
            if (cue.Beats.Any(b => source.IndexOf(b.After, StringComparison.Ordinal) < 0
                || source.IndexOf(b.After, StringComparison.Ordinal) != source.LastIndexOf(b.After, StringComparison.Ordinal)))
                throw new Exception("Missing or ambiguous beat: " + source);
            double expected = cue.Lead + cue.Tail + cue.Beats.Sum(b => b.Seconds);
            foreach (float wrap in new[] { width, DialogueBox.WrapWidth(DialogueBox.Board), 180f })
            {
                var pages = DialogueBox.Paginate(source, wrap);
                var pacing = DialoguePacing.ForPages(source, pages);
                double actual = pacing.Sum(p => p.Lead + p.Tail + p.Beats.Values.Sum());
                if (Math.Abs(expected - actual) > 0.0001) throw new Exception("Lost or repeated beat: " + source);
            }
        }
        Check(true, $"all {DialoguePacing.Cues.Count} authored cues survive narrow, board and full-screen pagination exactly once");
        const string hesitation = "潜れます。……潜れる、はずです。";
        var inline = DialoguePacing.ForPages(hesitation, new[] { hesitation })[0];
        int boundary = inline.Beats.Keys.Single();
        double before = DialogueBox.RevealDuration(hesitation[..boundary], pacing: inline);
        Check((int)DialogueBox.AdvanceReveal(hesitation, 0, before + 0.4, pacing: inline) == boundary,
            "hesitation holds the last words on screen before continuing");
        Check((int)DialogueBox.AdvanceReveal(hesitation, 0, before + 0.95, pacing: inline) > boundary,
            "hesitation resumes without dropping characters");
        foreach (float speed in new[] { DialogueBox.Speed(0), DialogueBox.Speed(1), DialogueBox.Speed(2) })
            Check(Math.Abs(DialogueBox.RevealDuration(hesitation, speed, inline)
                - DialogueBox.RevealDuration(hesitation, speed) - 0.55) < 0.0001,
                "authored hesitation keeps its duration at each text speed");
        double stepped = 0;
        for (int i = 0; i < 120; i++) stepped = DialogueBox.AdvanceReveal(hesitation, stepped, 1.0 / 60, pacing: inline);
        Check(Math.Abs(stepped - DialogueBox.AdvanceReveal(hesitation, 0, 2, pacing: inline)) < 0.0001,
            "a long frame cannot jump over an authored beat");
        var split = new List<string> { "潜れます。", "……潜れる、はずです。" };
        var splitPacing = DialoguePacing.ForPages(hesitation, split);
        Check(splitPacing[0].Tail == 0.55 && splitPacing[1].Lead == 0,
            "a boundary beat holds the previous page without repeating on the next");
        const string closing = "……ええ。\nわたくしも、ここにいて、よいのですね。";
        var endPages = DialogueBox.Paginate(closing, width);
        var endPacing = DialoguePacing.ForPages(closing, endPages);
        Check(endPacing[0].Lead == 0.5 && endPacing[^1].Tail == 1.4,
            "film line breaks do not prevent the final reply from receiving its direction");
        var plain = DialoguePacing.ForPages("聞こえているよ。", new[] { "聞こえているよ。" })[0];
        Check(plain.Lead == 0 && plain.Tail == 0 && plain.Beats.Count == 0,
            "ordinary dialogue is not slowed indiscriminately");
    }

    private async Task CheckMidbossDialogue(GameManager game)
    {
        var lines = ((int who, string text, string face)[])typeof(StageAkari)
            .GetField("MidPre", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (bool skip in new[] { false, true })
        {
            game.SelectedJob = Job.Tank;
            game.SelectedEntry = GameManager.StageEntry.AfterMidBoss;
            game.Difficulty = GameManager.Diff.Normal;
            game.AutoAdvanceDialog = false;
            Hud.SkipLatched = false;
            Input.ActionRelease("ui_accept");
            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            var stage = root.GetNode<StageAkari>("StageAkari");
            var hud = root.GetNode<Hud>("Hud");
            stage.SetProcess(false);
            hud.SetProcess(false);
            root.GetNode<Node2D>("World").ProcessMode = ProcessModeEnum.Disabled;
            await Frames(3);
            stage._Process(0.01);
            if (skip)
            {
                Set(stage, "_introLine", lines.Length - 1);
                typeof(StageAkari).GetMethod("ShowLine", Private)!.Invoke(stage, new object[] { lines });
                hud._Process(0.4);
                hud.RevealDialogNow();
                Check(Read<string>(hud, "_dlgText") == "僕もだ。ミナ、君と一緒に、もう一度会いに行きたい。"
                    && Read<bool>(hud, "_dlgReadBefore"), "reported midboss line is recognised as read on replay");
                await Shot(root, "midboss_read_skip");
                const BindingFlags stat = BindingFlags.Static | BindingFlags.NonPublic;
                typeof(Pad).GetField("_mousePos", stat)!.SetValue(null, hud.ToolbarRect(DialogToolbar.Skip).GetCenter());
                typeof(Pad).GetField("_mL", stat)!.SetValue(null, true);
                typeof(Pad).GetField("_mLPrev", stat)!.SetValue(null, false);
                hud._Process(0.01);
                Check(Hud.SkipLatched && hud.FastForwarding, "clicking the reported scene's read-skip icon activates fast forward");
                stage._Process(0.2);
                typeof(Pad).GetField("_mL", stat)!.SetValue(null, false);
                stage._Process(0.01);
                hud._Process(0.01);
                Check(Read<int>(stage, "_choicePhase") == 1 && !Hud.SkipLatched,
                    "read skip advances the reported line and stops at the choice");
            }
            else
            {
                for (int press = 0; press < 32 && Read<int>(stage, "_choicePhase") == 0; press++)
                {
                    int line = Read<int>(stage, "_introLine"), page = Read<int>(hud, "_dlgPage");
                    var pages = Read<List<string>>(hud, "_dlgPages");
                    int pageCount = pages.Count, pageLength = pages[page].Length;
                    bool full = Read<float>(hud, "_dlgRevealed") >= pageLength;
                    Input.ActionPress("ui_accept");
                    hud._Process(0.01);
                    stage._Process(0.01);
                    if (!full)
                        Check(Read<int>(stage, "_introLine") == line && Read<int>(hud, "_dlgPage") == page
                            && Read<float>(hud, "_dlgRevealed") == pageLength,
                            "rapid press reveals the current page without also skipping it");
                    else if (page < pageCount - 1)
                        Check(Read<int>(stage, "_introLine") == line && Read<int>(hud, "_dlgPage") == page + 1,
                            "next rapid press immediately opens the next page");
                    else
                        Check(Read<int>(stage, "_introLine") == line + 1,
                            "next rapid press immediately advances the speaker with no debounce loss");
                    int after = Read<int>(stage, "_introLine");
                    stage._Process(0.01);
                    Check(Read<int>(stage, "_introLine") == after, "holding confirm does not advance a second line");
                    Input.ActionRelease("ui_accept");
                    hud._Process(0.01);
                    stage._Process(0.01);
                }
            }
            Check(Read<ChoiceOverlay>(stage, "_choice") is { Decided: false },
                "dialogue completion leaves the following choice undecided");
            Input.ActionRelease("ui_accept");
            root.QueueFree();
            await Frames(3);
        }
    }

    private async Task CheckFinalPacing(GameManager game)
    {
        var final = GD.Load<PackedScene>("res://Final.tscn").Instantiate<Final>();
        GetTree().Root.AddChild(final);
        final.SetProcess(false);
        Set(final, "_phase", 1);
        var talk = Read<System.Collections.IList>(final, "_talk");
        int index = Enumerable.Range(0, talk.Count).Single(i =>
            (string)talk[i]!.GetType().GetField("Text")!.GetValue(talk[i])! == "今、どうぞ。聞いています。");
        Set(final, "_line", index);
        final._Process(0.01);
        var pages = Read<List<string>>(final, "_pages");
        Check(pages.Count == 1, "Final's short invitation stays on a single page");
        Set(final, "_reveal", (double)pages[0].Length);
        Set(final, "_lineT", 1.0);
        void Press()
        {
            Set(final, "_zHeld", false);
            Input.ActionPress("ui_accept");
            final._Process(0.01);
            Input.ActionRelease("ui_accept");
        }
        game.AutoAdvanceDialog = true;
        final._Process(0.45);
        Check(Read<int>(final, "_line") == index, "AUTO respects the invitation beat");
        final._Process(0.55);
        Check(Read<int>(final, "_line") == index, "AUTO preserves the longer authored silence");
        game.AutoAdvanceDialog = false;
        Set(final, "_lineT", 0.01);
        Press();
        Check(Read<int>(final, "_line") == index + 1, "manual input immediately leaves the invitation even during its authored silence");
        final._Process(0.01);
        Check(Read<ChoiceOverlay>(final, "_choice") is { Decided: false }, "manual advance opens the choice without selecting an answer");
        final.QueueFree();
        await Frames(2);
    }

    private async Task Shot(Node2D scene, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        if (scene is OpeningFilm opening) opening._Process(0);
        scene.QueueRedraw();
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string folder = ProjectSettings.GlobalizePath("res://build/qa_story/dialogue_tempo");
        DirAccess.MakeDirRecursiveAbsolute(folder);
        using var shot = GetViewport().GetTexture().GetImage();
        shot.SavePng(folder + "/" + name + ".png");
    }
}

public partial class DialogueRevealQaCanvas : Node2D
{
    public string Page = "";
    public double Shown;
    public float Speed = DialogueBox.DefaultCharsPerSec;
    public DialoguePacing.Page? Pacing;
    public bool Reference;

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color("23323e"));
        var box = DialogueBox.FullScreen;
        DialogueBox.DrawFrame(this, box, "ミナ", UiKit.Mina);
        if (Reference)
            UiKit.TypewriterLines(this, DialogueBox.Body.Font, new List<string>(Page.Split('\n')),
                DialogueBox.TextPosition(box) + new Vector2(0, DialogueBox.Body.Font.GetAscent(DialogueBox.Body.Size)),
                DialogueBox.WrapWidth(box), DialogueBox.Body.Size, DialogueBox.Ink, int.MaxValue,
                extraLeading: DialogueBox.Body.ExtraLeading);
        else
            DialogueBox.DrawBody(this, box, Page, Shown, speed: Speed, pacing: Pacing);
        UiKit.EndDesign(this);
    }
}
