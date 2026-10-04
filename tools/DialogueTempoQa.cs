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
            Check(DialogueBox.RevealDuration("あ、いう") > DialogueBox.RevealDuration("あいうえ") + 0.1, "comma adds a breath");
            Check(DialogueBox.RevealDuration("……あ") > DialogueBox.RevealDuration("あいう") + 0.3, "ellipsis adds a hesitation once per run");
            double stepped = 0;
            for (int i = 0; i < 60; i++) stepped = DialogueBox.AdvanceReveal(text, stepped, 1.0 / 60);
            Check(Math.Abs(stepped - DialogueBox.AdvanceReveal(text, 0, 1)) < 0.0001, "reveal timing is independent of frame rate");
            CheckAuthoredTiming(width);

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
            for (int i = 0; i < 4; i++) { hud._Process(0.1); hud.RevealDialogNow(); }
            Check(Read<int>(hud, "_dlgPage") == 0, "rapid clicks keep the revealed page visible for at least half a second");
            hud._Process(0.11);
            hud.RevealDialogNow();
            Check(Read<int>(hud, "_dlgPage") == 1 && Read<float>(hud, "_dlgRevealed") == 0, "a fresh click after the pause starts the next page from zero");
            hud.RevealDialogNow();
            Check(!hud.DialogRevealed, "the final page also waits before allowing the next speaker");
            hud._Process(0.51);
            Check(hud.DialogRevealed, "manual dialogue can advance after the reading pause");
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
            Check(!hud.DialogRevealed, "a manual reveal keeps the authored afterglow");
            hud._Process(0.3);
            hud.RevealDialogNow();
            hud._Process(0.3);
            Check(hud.DialogRevealed, "extra presses during the afterglow do not restart its timer");
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
        Press();
        Check(Read<int>(final, "_line") == index, "manual Final advance respects the invitation beat");
        final._Process(0.45);
        Press();
        Check(Read<int>(final, "_line") == index, "rapid clicks do not bypass the longer authored silence");
        final._Process(0.55);
        Press();
        Check(Read<int>(final, "_line") == index, "Final waits before presenting the player's answer");
        await Shot(final, "final_invitation_pause");
        game.AutoAdvanceDialog = true;
        final._Process(0.25);
        final._Process(0.01);
        Check(Read<object?>(final, "_choice") != null, "AUTO reaches the answer choice after the invitation's silence");
        game.AutoAdvanceDialog = false;
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
