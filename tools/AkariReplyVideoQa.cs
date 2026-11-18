using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class AkariReplyVideoQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private bool _block;
    private const string Shots = "res://build/qa_story/akari_reply/shots";

    public override void _Process(double delta)
    {
        if (_block) Pad.ConsumeUi(this);
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.SelectedJob = Job.Tank;
            game.SelectedEntry = GameManager.StageEntry.Boss;
            game.MsgCharsPerSec = 300;
            game.AutoAdvanceDialog = false;
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Shots));
            if (DisplayServer.GetName() != "headless")
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
            }
            await Frames(3);
            if (OS.GetCmdlineUserArgs().Contains("--preview"))
            {
                Audio.Instance!.Music(Audio.Instance.StoryBgm("akari", true)!, 0);
                var preview = new AkariReplyVideo();
                AddChild(preview);
                await Wait(() => preview.Finished);
                await Frames(15);
                Audio.Instance.StopMusic(0);
                GD.Print("[AkariReplyQA] PREVIEW COMPLETE");
                GetTree().Quit();
                return;
            }
            var gameMode = game.ProcessMode;
            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var stage = root.GetNode<StageAkari>("StageAkari");
            var world = root.GetNode<Node2D>("World");
            var hud = root.GetNode<Hud>("Hud");
            world.GetNode<Player>("Player").SetPhysicsProcess(false);
            Write(stage, "_step", 14);
            Write(stage, "_stepStarted", false);
            Write(stage, "_startBannerShown", true);
            bool sawChoice = false;
            await Advance(() =>
            {
                sawChoice |= hud.GetChildren().OfType<ChoiceOverlay>().Any();
                return hud.HasNode("AkariStoryFilm");
            });
            Check(sawChoice, "return choice completes before aftermath");
            stage.SetProcess(false);
            var film = hud.GetNode<AkariStoryFilm>("AkariStoryFilm");
            await Advance(() => film.HasNode("ReplyVideo"));
            var video = film.GetNode<AkariReplyVideo>("ReplyVideo");
            var player = video.GetNode<VideoStreamPlayer>("Movie");
            int completed = 0;
            video.Completed += () => completed++;
            Check(hud.CinematicMode && world.ProcessMode == ProcessModeEnum.Disabled
                && game.ProcessMode == ProcessModeEnum.Disabled, "film keeps combat paused");
            Check(!FilmSkip.Seen(game, "akari_aftermath"), "unread ending is not prematurely marked seen");
            video.RequestSkip();
            await Wait(() => video.Elapsed > 1);
            Check(!video.Finished && completed == 0, "unread film cannot be skipped");
            Check(player.GetVideoTexture().GetSize() == new Vector2(1920, 1080), "native 1080p texture");
            _block = true;
            await Frames(3);
            double paused = video.Elapsed;
            await Frames(20);
            Check(Math.Abs(video.Elapsed - paused) < 0.05 && player.Paused, "overlay pauses playback clock");
            _block = false;
            await Wait(() => video.Elapsed >= AkariReplyVideo.SendAt + 0.25);
            Check(video.Sent && !video.Replied, "send precedes reply");
            await Capture("01_sent");
            await Wait(() => video.Elapsed >= AkariReplyVideo.ReplyAt + 0.25);
            Check(video.Replied, "reply notification fires");
            await Capture("02_reply");
            await Wait(() => video.Elapsed >= 4.1);
            await Capture("03_smile");
            await Wait(() => !IsInstanceValid(film));
            Check(completed == 1 && Read<int>(stage, "_step") == 15, "smile ends directly at return transition");
            Check(!hud.CinematicMode && world.ProcessMode == ProcessModeEnum.Inherit
                && game.ProcessMode == gameMode, "completion restores world and HUD");
            Check(Hud.Backlog.Count(l => l.Speaker == "送信したメッセージ" && l.Text == AkariReplyVideo.Message) == 1
                && Hud.Backlog.Count(l => l.Speaker == "西野" && l.Text == "新着メッセージ") == 1,
                "send and reply are logged once each");
            Check(FilmSkip.Seen(game, "akari_aftermath"), "completed ending is marked seen");
            var repeat = new AkariReplyVideo { Completed = () => completed++ };
            AddChild(repeat);
            await Frames(5);
            repeat.RequestSkip();
            await Wait(() => repeat.Finished);
            Check(completed == 2 && !repeat.Sent && !repeat.Replied, "read skip completes once without firing cues");
            repeat.QueueFree();
            stage.SetProcess(true);
            await Wait(() => GetTree().CurrentScene is Hub);
            Check(game.ProcessMode == gameMode, "normal Hub transition completes");
            GD.Print("[AkariReplyQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[AkariReplyQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value) => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[AkariReplyQA] PASS {message}");
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Wait(Func<bool> condition)
    {
        for (int i = 0; i < 2400 && !condition(); i++) await Frames(1);
        Check(condition(), "playback/transition reaches checkpoint");
    }
    private async Task Advance(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = true });
            await Frames(12);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = false });
            await Frames(3);
        }
        Check(condition(), "dialogue reaches checkpoint");
    }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        Check(frame.SavePng($"{Shots}/{name}.png") == Error.Ok, $"captured {name}");
    }
}
