using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class RecordsStyleQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameManager _game = null!;
    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Write(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        GD.Print($"[RecordsQA] PASS {text}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.ShopTutorialSeen = true;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(3);
            if (OS.GetCmdlineUserArgs().Contains("--before"))
            {
                SeedRecords();
                var before = await Open();
                await Shot("before");
                before.QueueFree();
            }
            else await CheckRecords();
            Audio.Instance?.StopMusic(0);
            foreach (var player in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { player.Stop(); player.Stream = null; }
            await Frames(6);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(4);
            GD.Print("[RecordsQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[RecordsQA] FAIL {e}");
            GetTree().Quit(1);
        }
    }

    private void SeedRecords()
    {
        for (int i = 0; i < 4; i++)
        {
            string id = new[] { "akari", "koharu", "rei", "final" }[i];
            if (i < 3) Read<HashSet<string>>(_game, "_cleared").Add(id);
            _game.RecordClearTime(id, GameManager.Diff.Easy, 142.36f + i * 37);
            _game.RecordClearTime(id, GameManager.Diff.Normal, 189.84f + i * 41);
            _game.RecordClearTime(id, GameManager.Diff.Hard, 207.52f + i * 45);
            _game.RecordScore(id, GameManager.Diff.Easy, 123480 + i * 32450);
            _game.RecordScore(id, GameManager.Diff.Normal, 284650 + i * 39350);
            _game.RecordScore(id, GameManager.Diff.Hard, 241800 + i * 34380);
        }
    }

    private async Task<Records> Open()
    {
        var records = GD.Load<PackedScene>("res://Records.tscn").Instantiate<Records>();
        GetTree().Root.AddChild(records);
        GetTree().CurrentScene = records;
        await Frames(20);
        Write(records, "_t", 1.0);
        return records;
    }

    private async Task CheckRecords()
    {
        var records = await Open();
        for (int i = 0; i < 4; i++)
        {
            Check((string)Call(records, "StageName", i)! == "???", "uncleared stage name stays hidden");
            Check(Read<Texture2D?[]>(records, "_memories")[i] == null && Read<Texture2D?[]>(records, "_avatars")[i] == null,
                "uncleared art is not loaded or exposed");
        }
        await Shot("empty");
        records.QueueFree();
        await Frames(3);

        Read<HashSet<string>>(_game, "_cleared").Add("akari");
        _game.RecordClearTime("akari", GameManager.Diff.Normal, 184.23f);
        records = await Open();
        Check((string)Call(records, "StageName", 0)! == "あかり", "cleared stage name is revealed");
        Check(Read<Texture2D?[]>(records, "_memories")[0] != null, "cleared stage uses its real-realm artwork");
        Check(Call(records, "BestScore", "akari") == null, "legacy time-only records keep the score empty");
        await Shot("first_clear");
        Call(records, "SelectStage", 1);
        await Frames(16);
        await Shot("locked_stage");
        records.QueueFree();
        await Frames(3);

        Read<HashSet<string>>(_game, "_cleared").UnionWith(new[] { "koharu", "rei" });
        records = await Open();
        Check((string)Call(records, "StageName", 3)! == "ミナ" && Read<Texture2D?[]>(records, "_memories")[3] == null,
            "final name can open without revealing its uncleared real realm");
        records.QueueFree();
        await Frames(3);

        _game.ClearTimes.Clear();
        SeedRecords();
        var times = _game.ClearTimes.ToArray();
        var scores = _game.BestScores.ToArray();
        var job = _game.SelectedJob;
        var difficulty = _game.Difficulty;
        records = await Open();
        foreach (int i in Enumerable.Range(0, 4))
        {
            Call(records, "SelectStage", i);
            await Frames(16);
            Check(Read<Texture2D?[]>(records, "_memories")[i] != null, "completed stage displays real-realm artwork");
            var best = ((GameManager.Diff diff, long score))Call(records, "BestScore", new[] { "akari", "koharu", "rei", "final" }[i])!;
            Check(best.diff == GameManager.Diff.Normal, "highest score is independent of fastest difficulty");
            await Shot(new[] { "akari", "koharu", "rei", "mina" }[i]);
        }
        Check((int)Call(records, "ClearCount")! == 4, "final clear contributes to completion");
        Call(records, "SelectStage", 0);
        foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(1920, 1080), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Frames(8);
            await Shot($"size_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(5);
        Check(times.SequenceEqual(_game.ClearTimes) && scores.SequenceEqual(_game.BestScores)
            && _game.SelectedJob == job && _game.Difficulty == difficulty, "browsing never changes saved records or gameplay selection");

        Input.ActionPress("ui_right");
        records._Process(0.016);
        records._Process(0.016);
        Check(Read<int>(records, "_sel") == 1, "held navigation changes one stage only");
        Input.ActionRelease("ui_right");
        records._Process(0.016);
        Call(records, "SelectStage", 0);
        Input.ActionPress("ui_left");
        records._Process(0.016);
        Input.ActionRelease("ui_left");
        records._Process(0.016);
        Check(Read<int>(records, "_sel") == 3, "navigation wraps between first and final stages");
        Pad.ConsumeUi(this);
        Input.ActionPress("ui_right");
        records._Process(0.016);
        Check(Read<int>(records, "_sel") == 3, "pause overlay blocks stage changes");
        Input.ActionRelease("ui_right");
        await Frames(4);
        Click(records, new Vector2(160, 134));
        Check(Read<int>(records, "_sel") == 0, "click selects a stage on the first press");
        SetPad("_mousePos", new Vector2(1110, 170));
        records._Process(0.016);
        Check(Read<int>(records, "_sel") == 0, "hover alone does not change the selected stage");

        _game.RecordScore("akari", GameManager.Diff.Lunatic, long.MaxValue);
        _game.RecordClearTime("akari", GameManager.Diff.Lunatic, 599999.99f);
        await Frames(4);
        Check((int)Call(records, "ValueSize", long.MaxValue.ToString("N0"), 216f, 22)! >= 13,
            "maximum score fits without clipping or unreadably small text");
        await Shot("large_values");
        Click(records, new Vector2(144, 682));
        await Frames(8);
        Check(GetTree().CurrentScene is Hub, $"home button returns to the smartphone home (scene={GetTree().CurrentScene?.SceneFilePath})");
        GetTree().CurrentScene.QueueFree();
        await Frames(4);
        records = await Open();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.T, Pressed = true });
        Input.FlushBufferedEvents();
        records._Process(0.016);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.T, Pressed = false });
        Input.FlushBufferedEvents();
        await Frames(6);
        Check(GetTree().CurrentScene is Hub, "existing T shortcut still returns home");
        GetTree().CurrentScene.QueueFree();
        await Frames(4);
    }

    private static void SetPad(string name, object value) => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    private static void Click(Records records, Vector2 at)
    {
        SetPad("_mousePos", at);
        SetPad("_mL", true);
        SetPad("_mLPrev", false);
        records._Process(0.016);
        SetPad("_mL", false);
        SetPad("_mLPrev", false);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string dir = ProjectSettings.GlobalizePath("res://build/qa_story/records_style");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{dir}/{name}.png") == Error.Ok, $"screenshot {name}");
        int varied = 0;
        Color first = image.GetPixel(image.GetWidth() / 4, image.GetHeight() / 2);
        for (int y = image.GetHeight() * 2 / 5; y < image.GetHeight() * 3 / 5; y += 4)
        for (int x = image.GetWidth() / 4; x < image.GetWidth() * 3 / 4; x += 6)
        {
            Color pixel = image.GetPixel(x, y);
            if (Mathf.Abs(pixel.R - first.R) + Mathf.Abs(pixel.G - first.G) + Mathf.Abs(pixel.B - first.B) > 0.05f) varied++;
        }
        Check(varied > 10, "rendered record view is nonblank");
    }
}
