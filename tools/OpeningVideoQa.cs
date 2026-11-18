using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class OpeningVideoQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[OpeningVideoQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            bool playback = Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--playback");
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/opening_video_v08/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            if (DisplayServer.GetName() != "headless")
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
            }
            await Frames(3);

            var prologue = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
            GetTree().Root.AddChild(prologue);
            GetTree().CurrentScene = prologue;
            typeof(Prologue).GetMethod("StartGame", Private)!.Invoke(prologue, null);
            var film = prologue.GetNode<OpeningVideo>("OpeningVideo");
            var player = film.GetNode<VideoStreamPlayer>("Movie");
            int completed = 0;
            film.Completed += () => completed++;
            await Seconds(1.5);
            Check(player.Stream.ResourcePath == OpeningVideo.VideoPath && player.IsPlaying(), "prologue plays the opening video");
            Check(player.GetVideoTexture().GetSize() == new Vector2(1920, 1080), "native 1080p video texture");
            Check(player.Bus == "Music" && !player.Loop && !Audio.Instance!.HasMusic,
                "embedded soundtrack uses Music volume without overlapping BGM");
            Check(film.Elapsed > 1, "playback clock advances");
            if (DisplayServer.GetName() != "headless")
            {
                int musicBus = AudioServer.GetBusIndex("Music");
                float peak = -100;
                for (int i = 0; i < 30; i++)
                {
                    peak = Math.Max(peak, AudioServer.GetBusPeakVolumeLeftDb(musicBus, 0));
                    await Frames(1);
                }
                Check(peak > -60, $"movie soundtrack produces audio on Music bus ({peak:F1} dB)");
            }

            if (playback)
            {
                ulong playbackStarted = Time.GetTicksMsec();
                double firstPosition = film.Elapsed;
                foreach (double time in new[] { 3.2, 11, 16, 21, 31, 36, 38.5, 41, 43.5, 46, 48.3, 50.5, 55.2, 57, 68 })
                {
                    ulong deadline = Time.GetTicksMsec() + (ulong)((time - film.Elapsed + 10) * 1000);
                    while (IsInstanceValid(film) && film.Elapsed < time && Time.GetTicksMsec() < deadline)
                        await Frames(1);
                    Check(IsInstanceValid(film) && film.Elapsed >= time, $"playback reaches {time:F1}s (position={film.Elapsed:F2}s, paused={GetTree().Paused}, playerPaused={player.Paused}, fps={Engine.GetFramesPerSecond()})");
                    GD.Print($"[OpeningVideoQA] playback video={film.Elapsed - firstPosition:F2}s wall={(Time.GetTicksMsec() - playbackStarted) / 1000.0:F2}s fps={Engine.GetFramesPerSecond()}");
                    await Capture($"{time:00.0}");
                }
            }
            else film.RequestSkip();

            await WaitUntil(() => GetTree().CurrentScene is Hub, playback ? 10 : 4);
            Check(completed == 1 && !IsInstanceValid(film), "completion reaches Hub exactly once");
            Check(FilmSkip.Seen(GetNode<GameManager>("/root/Game"), "opening"), "opening completion is saved");
            GetTree().CurrentScene.QueueFree();
            GetTree().CurrentScene = this;
            await Frames(3);
            Audio.Instance?.StopMusic(0);

            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = true });
            film = new OpeningVideo { Completed = () => completed++ };
            AddChild(film);
            await Seconds(1.5);
            Check(completed == 1 && !Read<bool>(film, "_inputArmed"), "held dialogue key does not skip");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = false });
            await Frames(3);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = true });
            await Seconds(0.2);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = false });
            await Frames(3);
            Check(completed == 1 && Read<double>(film, "_skipHold") == 0, "short press cancels skip progress");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = true });
            await Seconds(1.3);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = false });
            Check(completed == 2 && !IsInstanceValid(film), "held skip fades and completes once");

            film = new OpeningVideo { Completed = () => completed++ };
            AddChild(film);
            await Seconds(0.75);
            ClickSkip(film);
            Check(!Read<bool>(film, "_leaving"), "hidden skip button does not respond");
            await Seconds(0.5);
            ClickSkip(film);
            await Seconds(0.6);
            Check(completed == 3 && !IsInstanceValid(film), "visible skip button completes once");
            GD.Print("[OpeningVideoQA] COMPLETE");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[OpeningVideoQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static T Read<T>(object owner, string field) =>
        (T)owner.GetType().GetField(field, Private)!.GetValue(owner)!;

    private static void ClickSkip(OpeningVideo film)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(Pad).GetField("_mousePos", flags)!.SetValue(null, DialogToolbar.FilmSkipRect.GetCenter());
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, true);
        typeof(Pad).GetField("_mLPrev", flags)!.SetValue(null, false);
        film._Process(1.0 / 60);
    }

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{_out}/{name}.png") == Error.Ok, $"captured {name}s");
    }

    private async Task WaitUntil(Func<bool> condition, double seconds)
    {
        ulong deadline = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (!condition() && Time.GetTicksMsec() < deadline) await Frames(1);
        Check(condition(), "transition finishes before deadline");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Seconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
