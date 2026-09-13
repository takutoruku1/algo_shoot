using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public partial class WrapChecks : Node2D
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

    public override void _Ready()
    {
        try
        {
            string fontPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                ProjectSettings.GlobalizePath("res://"), "../../assets/fonts/ZenKakuGothicNew-Bold.ttf"));
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
            GD.Print($"PASS: {_checks} wrapping assertions");
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

    public override void _Process(double delta)
    {
        if (!_visual) return;
        _elapsed += delta;
        QueueRedraw();
        if (_elapsed < (_shot + 1) * 0.6) return;
        string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "../../build/ui_precision/wrapping"));
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
