using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class StagePerformanceQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<object> _results = new();
    private string _output = "";
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print("[StagePerformance] PASS " + message);
    }
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object Summary(List<double> values)
    {
        values.Sort();
        return new { mean = values.Average(), p95 = values[(int)((values.Count - 1) * .95)], maximum = values[^1] };
    }
    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        int exit = 0;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            var args = OS.GetCmdlineUserArgs();
            _output = ProjectSettings.GlobalizePath(args.First(a => a.StartsWith("--output="))[9..]);
            System.IO.Directory.CreateDirectory(_output);
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Lunatic;
            game.SelectedJob = Job.Tank;
            await Frames(2);
            var source = new Node { Name = "PerformanceSource" };
            GetTree().Root.AddChild(source);
            GetTree().CurrentScene = source;
            string[] scenes = args.Contains("--all-stages") ? new[] { "Akari", "Koharu", "Rei", "MinaBattle" } : new[] { "Rei" };
            foreach (string scene in scenes) await LoadStage(scene);
            if (!args.Contains("--load-only")) await RunStorm();
            System.IO.File.WriteAllText(_output + "/results.json", JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { exit = 1; GD.PushError("[StagePerformance] FAIL " + error); }
        Audio.Instance?.StopMusic(0);
        foreach (var node in GetNode<Audio>("/root/Audio").GetChildren())
            if (node is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        await Frames(3);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(exit);
    }
    private async Task LoadStage(string scene)
    {
        var frames = new List<double>();
        var slow = new List<object>();
        var initialization = new List<object>();
        ulong start = Time.GetTicksUsec(), previous = start;
        ulong nodeAt = start;
        string nodeName = "before root";
        void NodeAdded(Node node)
        {
            ulong now = Time.GetTicksUsec();
            if (now - nodeAt > 30000) initialization.Add(new { after = nodeName, next = node.GetPath().ToString(), ms = (now - nodeAt) / 1000d });
            nodeName = node.GetPath();
            nodeAt = now;
        }
        GetTree().NodeAdded += NodeAdded;
        LoadingScreen.Open(this, $"res://{scene}.tscn");
        string phase = "Open";
        for (int frame = 0; frame < 3600 && LoadingScreen.IsActive; frame++)
        {
            await Frames(1);
            ulong now = Time.GetTicksUsec();
            double elapsed = (now - previous) / 1000d;
            frames.Add(elapsed);
            if (elapsed > 33.34) slow.Add(new { frame, phase, ms = elapsed });
            previous = now;
            var screen = GetNodeOrNull<LoadingScreen>("/root/LoadingScreen");
            phase = screen == null ? "Done" : typeof(LoadingScreen).GetField("_phase", Private)!.GetValue(screen)!.ToString()!;
        }
        Check(!LoadingScreen.IsActive && GetTree().CurrentScene.SceneFilePath == $"res://{scene}.tscn" && !GetTree().Paused,
            scene + " loading completes and unpauses");
        GetTree().NodeAdded -= NodeAdded;
        var result = new { scene, mode = "loading", seconds = (Time.GetTicksUsec() - start) / 1000000d, frame_ms = Summary(frames), slow, initialization };
        _results.Add(result);
        System.IO.File.WriteAllText(_output + "/loading_" + scene + ".json", JsonSerializer.Serialize(result));
        GD.Print("[StagePerformance] " + JsonSerializer.Serialize(result));
        GetTree().CurrentScene.SetProcess(false);
        if (GetTree().CurrentScene.GetType().GetProperty("Stage")!.GetValue(GetTree().CurrentScene) is Node stage) stage.SetProcess(false);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
    }
    private async Task RunStorm()
    {
        var root = (ReiRoot)GetTree().CurrentScene;
        root.Player.SetPhysicsProcess(false);
        root.Player.GlobalPosition = Field.PlayerStart;
        Write(root.Player, "_invincible", true);
        Write(root.Player, "_invincibleTimer", 99999f);
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        Hud.BubblePaused = false;
        Write(root.Stage, "_step", 18);
        Write(root.Stage, "_stepStarted", false);
        typeof(StageRei).GetMethod("Step_QuoteStorm", Private)!.Invoke(root.Stage, new object[] { 0d });
        var storm = root.World.GetNode<QuoteStorm>("QuoteStorm");
        var art = QuoteStorm.QuoteVisual;
        Check(ReferenceEquals(BulletArt.EnemyVisual(art.Texture, art.Accent), BulletArt.EnemyVisual(art.Texture, art.Accent)),
            "identical road projectiles share their cropped artwork and outline cache");
        var footprint = BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position, 11);
        var contours = BulletFootprint.TextureOutlines(art.Cropped);
        Vector2 pivot = (art.Pivot - art.Region.Position) / Mathf.Max(art.Cropped.GetWidth(), art.Cropped.GetHeight());
        for (int contour = 0; contour < contours.Length; contour++)
        for (int point = 0; point < contours[contour].Length; point++)
            Check(((contours[contour][point] - pivot) * 11).IsEqualApprox(footprint.Contours[contour][point]),
                "warning outline retains its original vertices");
        var frames = new List<double>();
        var draws = new List<double>();
        var slow = new List<object>();
        ulong previous = Time.GetTicksUsec();
        for (int frame = 0; frame < 4200 && !storm.Finished; frame++)
        {
            await Frames(1);
            ulong now = Time.GetTicksUsec();
            double elapsed = (now - previous) / 1000d;
            previous = now;
            frames.Add(elapsed);
            draws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            if (elapsed > 33.34) slow.Add(new { frame, ms = elapsed, quotes = storm.QuoteCount, draws = draws[^1],
                physics_ms = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000 });
            if (frame is 600 or 1500 or 2100 && DisplayServer.GetName() != "headless")
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng($"{_output}/storm_{frame}.png") == Error.Ok, "storm capture");
                previous = Time.GetTicksUsec();
            }
        }
        Check(storm.Finished && storm.QuoteCount == 17, "all 17 comments resolve without shooting");
        var result = new { scene = "Rei", mode = "quote-storm", difficulty = "Lunatic", invincible = true, shooting = false,
            frame_ms = Summary(frames), draw_calls = Summary(draws), slow };
        _results.Add(result);
        GD.Print("[StagePerformance] " + JsonSerializer.Serialize(result));
        storm.Dismiss();
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
