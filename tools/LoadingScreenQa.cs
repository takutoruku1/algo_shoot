using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

public partial class LoadingScreenQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private LoadingScreen? Screen => GetNodeOrNull<LoadingScreen>("/root/LoadingScreen");
    private bool _movie;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[LoadingQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _movie = Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--movie");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.ShopTutorialSeen = true;
            Read<HashSet<string>>(game, "_cleared").Add(GameManager.FirstStageId);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(3);
            var title = GD.Load<PackedScene>("res://TitleMenu.tscn").Instantiate();
            GetTree().Root.AddChild(title);
            GetTree().CurrentScene = title;
            await Frames(_movie ? 90 : 30);

            string[] scenes = _movie ? new[] { "Settings", "Hub", "Akari", "Hub", "Koharu" }
                : new[] { "Settings", "Credits", "TitleMenu", "Prologue", "Hub", "ShopTutorial", "Shop",
                    "Training", "Customize", "Records", "Stage0", "Akari", "Koharu", "Rei", "MinaBattle", "Final", "Epilogue", "Hub" };
            foreach (string scene in scenes)
            {
                if (scene == "Koharu") game.SelectedJob = Job.Melee;
                if (scene == "Rei") game.SelectedJob = Job.Heal;
                if (scene == "MinaBattle") game.SelectedJob = Job.Magic;
                await Navigate(scene);
            }
            if (!_movie)
            {
                foreach (Vector2I size in new[] { new Vector2I(960, 540), new(1920, 1080), new(540, 960) })
                {
                    DisplayServer.WindowSetSize(size);
                    await Frames(3);
                    await Navigate("Settings", $"{size.X}x{size.Y}");
                }
                ulong before = GetTree().CurrentScene.GetInstanceId();
                Engine.TimeScale = 0.06;
                await Navigate("Settings", "slow_time");
                Engine.TimeScale = 1;
                Check(GetTree().CurrentScene.GetInstanceId() != before, "retry replaces the scene instance");
                game.ProcessMode = ProcessModeEnum.Disabled;
                GetTree().Paused = true;
                await Navigate("Hub", "paused");
                game.ProcessMode = ProcessModeEnum.Inherit;
                Check(!GetTree().Paused, "transition works from a paused tree and disabled game manager");
                await FailureRecovery();
            }
            Audio.Instance?.StopMusic(0);
            await Frames(5);
            GD.Print("[LoadingQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            Keys(false);
            Engine.TimeScale = 1;
            GetTree().Paused = false;
            GD.PushError($"[LoadingQA] FAIL {error}");
            GetTree().Quit(1);
        }
    }

    private async Task Navigate(string name, string suffix = "default")
    {
        var source = GetTree().CurrentScene;
        var probe = new TickProbe();
        source.AddChild(probe);
        GameManager.FadeToScene(this, $"res://{name}.tscn");
        var screen = Screen;
        GameManager.FadeToScene(this, "res://Main.tscn");
        Check(Screen == screen && Read<string>(screen!, "_destination") == $"res://{name}.tscn", "duplicate request is ignored");
        Keys(true);
        for (int frame = 0; frame < 1800 && Read<float>(screen!, "_alpha") < 1; frame++) await Frames(1);
        Check(probe.Ticks == 0 && GetTree().Paused, $"{name}: source does not advance behind loading");
        Check(Read<float>(screen!, "_alpha") == 1, "loading screen is fully opaque before scene replacement");
        await Shot($"{name}_{suffix}");
        bool dive = name is "Stage0" or "Training" or "Main" or "Akari" or "Koharu" or "Rei" or "MinaBattle";
        Check(Read<Job>(screen!, "_job") == GameManager.Instance!.SelectedJob, "loading character follows the selected character");
        if (name == "Settings" && suffix == "default") await CheckCompanionMotion();
        bool sawDive = false;
        bool sawReveal = false;
        for (int frame = 0; frame < 1800 && LoadingScreen.IsActive; frame++)
        {
            string phase = Read<object>(screen!, "_phase").ToString()!;
            if (phase == "Diving" && !sawDive)
            {
                sawDive = true;
                Check(Read<float>(screen!, "_loaded") == 1 && GetTree().Paused, "dive starts only when resources are ready and gameplay is paused");
                await Frames(21);
                Check(Read<double>(screen!, "_diveTime") > 0.2 && GetTree().CurrentScene == source, "character dives before scene replacement");
                await Shot($"{name}_{suffix}_dive");
                await Frames(33);
                await Shot($"{name}_{suffix}_rush");
            }
            if (phase == "Reveal")
            {
                sawReveal = true;
                Check(GetTree().Paused, "destination remains paused during reveal");
                break;
            }
            await Frames(1);
        }
        Check(sawReveal, $"{name}: scene becomes ready before reveal");
        Check(sawDive == dive, $"{name}: dive animation is exclusive to stage destinations");
        await Idle();
        await Frames(20);
        Check(GetTree().CurrentScene.SceneFilePath == $"res://{name}.tscn" && !LoadingScreen.IsActive && !GetTree().Paused,
            $"{name}: navigation completes without a leaked pause or loading layer");
        Check(!GetNode<PauseMenu>("/root/PauseMenu").IsOpen, "held menu and back keys do not leak into destination");
        Keys(false);
        await Frames(_movie ? 80 : 5);
    }

    private async Task FailureRecovery()
    {
        GameManager.FadeToScene(this, "res://missing_scene_for_loading_qa.tscn");
        for (int frame = 0; frame < 180 && Read<object>(Screen!, "_phase").ToString() != "Failed"; frame++) await Frames(1);
        Check(Read<object>(Screen!, "_phase").ToString() == "Failed", "missing resource displays a recoverable error");
        await Shot("failure");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = false });
        await Idle();
        Check(GetTree().CurrentScene is TitleMenu && !GetTree().Paused, "error action returns to the title");
    }

    private async Task CheckCompanionMotion()
    {
        if (DisplayServer.GetName() == "headless") return;
        using var before = GetViewport().GetTexture().GetImage();
        await Frames(12);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var after = GetViewport().GetTexture().GetImage();
        float difference = 0;
        int samples = 0;
        for (int y = before.GetHeight() * 32 / 100; y < before.GetHeight() * 67 / 100; y += 4)
            for (int x = before.GetWidth() * 42 / 100; x < before.GetWidth() * 58 / 100; x += 4)
            {
                Color a = before.GetPixel(x, y), b = after.GetPixel(x, y);
                difference += Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B);
                samples++;
            }
        Check(difference / samples > 0.004f, "loading companion visibly drifts while the scene is paused");
        await Shot("companion_motion");
    }

    private async Task Idle()
    {
        for (int frame = 0; frame < 1800 && LoadingScreen.IsActive; frame++) await Frames(1);
        Check(!LoadingScreen.IsActive, "loading completes within timeout");
    }

    private static void Keys(bool pressed)
    {
        foreach (Key key in new[] { Key.Z, Key.X, Key.M, Key.Escape })
            Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/loading/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
        Color edge = image.GetPixel(0, 0);
        Check(edge.V < 0.3f, "dark loading background covers the viewport edge");
        int bright = 0, samples = 0;
        for (int y = 0; y < image.GetHeight(); y += 8)
            for (int x = 0; x < image.GetWidth(); x += 8)
            {
                if (image.GetPixel(x, y).V > 0.7f) bright++;
                samples++;
            }
        Check((float)bright / samples < 0.08f, "loading and dive frames keep a dark overall tone");
    }

    private partial class TickProbe : Node
    {
        public int Ticks;
        public override void _Process(double delta) => Ticks++;
        public override void _PhysicsProcess(double delta) => Ticks++;
    }
}
