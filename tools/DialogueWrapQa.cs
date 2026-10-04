using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public partial class DialogueWrapQa : Node2D
{
    private FontFile _font = null!;
    private int _checks;
    private double _elapsed;
    private bool _visual;
    private int _shot;
    private readonly string[] _samples =
    {
        "ああ、これ。前もそこで迷っておられました。",
        "ご主人様の趣味が出ますね、この枝の伸ばし方。",
        "いらっしゃいませ。……冗談です、ご主人様しかいらっしゃいませんもの。",
        "眺めているだけでも構いません。……買い物は、選んでいる時間がいちばん楽しいので。",
        "ご主人様。口の中に残る、という説を、今日どこかで読みました。……それでは、虫歯になってしまいます。",
        "光を届けてね。「待っています」……そう言った。",
        "ご主人様。——ここに、おります。……ずっと。",
        "The Godot Engine delivers light. Keep these words together.",
        "光🙂か\u3099届く。👩‍💻の言葉を、ここへ届けます。",
    };

    public override async void _Ready()
    {
        try
        {
            string fontPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                ProjectSettings.GlobalizePath("res://"), "assets/fonts/ZenKakuGothicNew-Bold.ttf"));
            _font = new FontFile { Data = Godot.FileAccess.GetFileAsBytes(fontPath) };
            foreach (string text in _samples)
                foreach (float width in new[] { 96f, 160f, 310f, 420f })
                    CheckWrap(text, width);
            var lines = UiKit.WrapLines(_font, _samples[0], 16, 310);
            Require(lines.SequenceEqual(new[] { "ああ、これ。", "前もそこで迷っておられました。" }), "Phrase boundary before a short ending");
            lines = UiKit.WrapLines(_font, "先に伝えます。\r\n\r\n短い一言。", 16, 310);
            Require(lines.SequenceEqual(new[] { "先に伝えます。", "", "短い一言。" }), "Explicit line and paragraph breaks");
            Require(UiKit.WrapLines(_font, "短いセリフです。", 16, 310).Count == 1, "Short dialogue stays on one line");
            Require(UiKit.WrapLines(_font, "", 16, 310).SequenceEqual(new[] { "" }), "Empty dialogue");
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--report=")) CheckCorpus(arg[9..]);
            CheckNaturalBreaks();
            GD.Print($"PASS: {_checks} wrapping assertions");
            if (OS.GetCmdlineUserArgs().Contains("--prologue"))
            {
                await CapturePrologue();
                GetTree().Quit();
                return;
            }
            _visual = OS.GetCmdlineUserArgs().Contains("--visual");
            if (!_visual) GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private void CheckWrap(string text, float width)
    {
        var lines = UiKit.WrapLines(_font, text, 16, width);
        Require(string.Concat(lines) == text, $"Text preserved: {text}");
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToHashSet();
        int consumed = 0;
        foreach (string line in lines)
        {
            Require(UiKit.TextW(_font, line, 16) <= width + 0.01f, $"Width {width}: {line}");
            consumed += line.Length;
            Require(boundaries.Contains(consumed), $"Grapheme boundary: {line}");
            if (consumed == text.Length) continue;
            char after = text[consumed], before = text[consumed - 1];
            Require("、。，．・：；！？ーっゃゅょぁぃぅぇぉッャュョァィゥェォ々）」』】!?.,:;)]}".IndexOf(after) < 0, $"Line head: {after}");
            Require("（「『【〔｛〈《‘“([{".IndexOf(before) < 0, $"Line tail: {before}");
            Require(!(before == after && after is '…' or '‥' or '—'), "Paired pause marks");
        }
        var pages = UiKit.Paginate(_font, text, 16, width, 2);
        Require(string.Concat(pages).Replace("\n", "") == text, "Page text preserved");
        foreach (string page in pages)
        {
            Require(page.Split('\n').Length <= 2, "Two-line page limit");
            Require(UiKit.WrapLines(_font, page, 16, width).SequenceEqual(page.Split('\n')), "Page layout remains stable");
        }
    }

    private void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private void CheckCorpus(string name)
    {
        string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "build/ui_precision/wrapping"));
        using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(folder + "/corpus.json"));
        string fontPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "assets/fonts/ZenKakuGothicNew-Regular.ttf"));
        using var font = new FontFile { Data = Godot.FileAccess.GetFileAsBytes(fontPath) };
        var report = new List<object>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            string text = item.GetProperty("text").GetString()!;
            foreach (var (profile, size, width) in new[] {
                ("prologue", DialogueBox.Body.Size, DialogueBox.WrapWidth(DialogueBox.FullScreen)),
                ("battle", DialogueBox.Body.Size, DialogueBox.WrapWidth(DialogueBox.Board)),
                ("memory", 24, 1056f), ("phone", DialogueBox.Body.Size, 432f) })
            {
                var lines = UiKit.WrapLines(font, text, size, width);
                Require(string.Concat(lines) == text.Replace("\r", "").Replace("\n", ""), $"Corpus text preserved: {text}");
                foreach (string line in lines)
                    Require(UiKit.TextW(font, line, size) <= width + .01f, $"Corpus width: {line}");
                var pages = UiKit.Paginate(font, text, size, width, 2);
                foreach (string page in pages)
                {
                    Require(page.Split('\n').Length <= 2, "Corpus page fits two lines");
                    Require(UiKit.WrapLines(font, page, size, width).SequenceEqual(page.Split('\n')), "Corpus page reflows consistently");
                }
                var dialoguePages = DialogueBox.Paginate(text, width);
                Require(string.Concat(dialoguePages).Replace("\n", "") == text.Replace("\r", "").Replace("\n", ""), "Dialogue formatting preserves the source");
                foreach (string page in dialoguePages)
                {
                    Require(page.Split('\n').Length <= 2, "Dialogue remains within two lines");
                    foreach (string line in page.Split('\n'))
                        Require(UiKit.TextW(DialogueBox.Body.Font, line, DialogueBox.Body.Size) <= width + .01f, "Dialogue fits the current box");
                }
                report.Add(new { file = item.GetProperty("file").GetString(), line = item.GetProperty("line").GetInt32(), profile, text, lines, pages, dialoguePages });
            }
        }
        System.IO.File.WriteAllText(folder + "/" + name + ".json", System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        GD.Print($"Corpus: {report.Count} layouts saved to {name}.json");
    }

    private void CheckNaturalBreaks()
    {
        const string boot = "起動記録に、operator と。起動時刻、10:08——集計に入れておきます。……あなたが、作った方ですね。";
        var lines = UiKit.WrapLines(UiKit.Zen, boot, 8, 328);
        GD.Print("BOOT " + string.Join(" / ", lines));
        Require(lines.Count == 2 && lines[1] == "……あなたが、作った方ですね。", "Boot dialogue keeps the final sentence together");
        lines = UiKit.WrapLines(DialogueBox.Body.Font, boot, DialogueBox.Body.Size, DialogueBox.WrapWidth(DialogueBox.FullScreen));
        Require(lines.Count == 2 && lines[1] == "……あなたが、作った方ですね。", "Boot dialogue keeps the final sentence together in the current box");
        lines = UiKit.WrapLines(_font, "記録を確認しました。あなたが、作った方ですね。", 16, 310);
        Require(lines.SequenceEqual(new[] { "記録を確認しました。", "あなたが、作った方ですね。" }), "Sentence end wins over a later comma");
        lines = UiKit.WrapLines(_font, "表示を確認。……あなたが、作った方ですね。", 16, 260);
        Require(!lines.Any(line => line.EndsWith("……")), "Opening pause stays with the next sentence");
        lines = UiKit.WrapLines(UiKit.Zen, "光が薄いのは、誰のせいでもありません。抱えたぶんの、重さです。……行けます。まだ。", 24, DialogueBox.WrapWidth(DialogueBox.Board));
        Require(lines.Count == 2 && lines[1] != "まだ。", "A short final phrase is not left alone");
        foreach (string token in new[] { "10:08", "@hoshiai_rei", "operator", "2,000" })
        {
            string text = "記録は" + token + "です。";
            lines = UiKit.WrapLines(_font, text, 16, UiKit.TextW(_font, token, 16) + 8);
            Require(lines.Any(line => line.Contains(token)), "Unbroken display token: " + token);
        }
        foreach (string text in new[] { boot, "……あなたが、作った方ですね。", "その投稿には、送れなかった言葉があります。", "光🙂が届く。👩‍💻の言葉を聞きます。" })
            foreach (float width in new[] { 96f, 160f, 310f, 420f }) CheckWrap(text, width);
    }

    private async System.Threading.Tasks.Task CapturePrologue()
    {
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Require(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "Isolated save data");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        var pro = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
        GetTree().Root.AddChild(pro);
        GetTree().CurrentScene = pro;
        void Set(string name, object value) => typeof(Prologue).GetField(name, fields)!.SetValue(pro, value);
        var talk = (System.Collections.IList)typeof(Prologue).GetMethod("P2Reply", fields)!.Invoke(pro, new object[] { "ひとりだった" })!;
        Set("_talk", talk);
        Set("_phase", 3);
        Set("_line", 3);
        Set("_p2ChoiceLine", -1);
        Set("_backdrop", 2);
        Set("_previousBackdrop", 2);
        Set("_backdropMix", 1f);
        GetNode<GameManager>("/root/Game").AutoAdvanceDialog = false;
        string folder = ProjectSettings.GlobalizePath("res://build/ui_precision/wrapping");
        for (int scene = 0; scene < 2; scene++)
        {
            if (scene == 1)
            {
                var entry = talk[3]!;
                entry.GetType().GetField("Text")!.SetValue(entry,
                    "起動記録に、operator と。起動時刻、10:08——集計に入れておきます。……あなたが、作った方ですね。");
                talk[3] = entry;
            }
            Set("_pagedLine", -1);
            typeof(Prologue).GetMethod("EnsurePages", fields)!.Invoke(pro, null);
            Set("_reveal", 10000d);
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540) })
            {
                DisplayServer.WindowSetSize(size);
                for (int frame = 0; frame < 12; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var shot = GetViewport().GetTexture().GetImage();
                Require(shot.GetSize() == size, $"Captured viewport {size}");
                shot.SavePng($"{folder}/prologue_{scene}_{size.X}.png");
            }
        }
        pro.QueueFree();
        GD.Print("PASS: actual prologue captures at 1280x720 and 960x540");
    }

    public override void _Process(double delta)
    {
        if (!_visual) return;
        _elapsed += delta;
        QueueRedraw();
        if (_elapsed < (_shot + 1) * 0.6) return;
        string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "build/ui_precision/wrapping"));
        DirAccess.MakeDirRecursiveAbsolute(output);
        GetViewport().GetTexture().GetImage().SavePng($"{output}/dialogue_{_shot}.png");
        if (++_shot == 3) GetTree().Quit();
    }

    public override void _Draw()
    {
        if (!_visual) return;
        DrawRect(new Rect2(0, 0, 1152, 720), new Color("0d0b1c"));
        for (int i = 0; i < 6; i++)
        {
            float x = 40 + (i % 3) * 370, y = 35 + (i / 3) * 235;
            UiKit.Box(this, new Rect2(x, y, 338, 202), new Color("181321"), 8, new Color("9690a5"), 1);
            var lines = UiKit.WrapLines(_font, _samples[i], 16, 310);
            UiKit.TypewriterLines(this, _font, lines, new Vector2(x + 14, y + 32), 310, 16,
                UiKit.Text2, int.MaxValue, extraLeading: 8);
        }
        var page = UiKit.Paginate(_font, _samples[0], 16, 310, 2)[0];
        UiKit.TypewriterLines(this, _font, new List<string>(page.Split('\n')), new Vector2(54, 560), 310,
            16, UiKit.White, (int)(_elapsed * 18), extraLeading: 8);
    }
}
