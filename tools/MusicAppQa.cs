using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class MusicAppQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Output = "res://build/qa_story/music_mvs_20261118/shots";
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print("[MusicAppQA] PASS " + message);
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            bool headless = DisplayServer.GetName() == "headless";
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            foreach (string seen in new[] { "once_sns_intro", "once_phone_home", "once_account_intro" }) game.MarkIdleDialogSeen(seen);
            foreach (var job in Jobs.All) game.MarkIdleDialogSeen($"once_companion_select_{job.CharacterId}");
            game.ShopTutorialSeen = true;
            await Frames(3);
            if (!headless)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
                GetTree().Root.AddChild(hub);
                GetTree().CurrentScene = hub;
                await Seconds(1.3);
                Pad.ReleaseMouseFollow();
                typeof(Hub).GetField("_homeSel", Private)!.SetValue(hub, 0);
                await Keypress(Key.Left);
                await Keypress(Key.Left);
                Check((int)typeof(Hub).GetField("_homeSel", Private)!.GetValue(hub)! == 8, "music shortcut reachable on a new save");
                await Capture("01_phone");
                await Keypress(Key.Z);
                await Until(() => GetTree().CurrentScene is MusicApp && !LoadingScreen.IsActive, 15);
            }
            else
            {
                var direct = GD.Load<PackedScene>(MusicApp.ScenePath).Instantiate<MusicApp>();
                GetTree().Root.AddChild(direct);
                GetTree().CurrentScene = direct;
            }
            var app = (MusicApp)GetTree().CurrentScene;
            await Frames(8);
            var tracks = app.Tracks;
            Check(tracks.Length == 22 && tracks.Count(t => t.IsVocal) == 5
                && tracks.Count(t => t.Category == "stage") == 5, "22 Suno songs: 5 vocals and 17 instrumentals, including 5 stage themes");
            Check(tracks.Where(t => t.IsVocal).Select(t => t.Video).Distinct().Count() == 5,
                "five vocal songs each have their own lyric MV");
            Check(tracks.Where(t => !t.IsVocal).GroupBy(t => t.Character)
                .All(group => group.Select(t => t.Video).Distinct().Count() == 1),
                "instrumental arrangements retain the shared character PVs");
            Check(tracks.Select(t => t.Id).Distinct().Count() == tracks.Length, "no duplicate tracks");
            Check(!Audio.Instance.HasMusic, "game BGM stops for the music app");
            var player = app.GetNode<AudioStreamPlayer>("FullTrack");
            var video = app.GetNode<VideoStreamPlayer>("CharacterPV");
            for (int i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                Check(GD.Load<Texture2D>(track.Cover) != null && GD.Load<VideoStream>(track.Video) != null, $"art and video resolve: {track.Title}");
                if (track.IsVocal)
                {
                    var lines = track.Lyrics.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith('[')).ToArray();
                    Check(lines.SequenceEqual(track.Cues.Select(c => c.Text)), $"original lyrics preserved: {track.Title}");
                    Check(track.Cues.All(c => c.Start >= 0 && c.End > c.Start && c.End <= track.Duration)
                        && track.Cues.Zip(track.Cues.Skip(1)).All(p => p.First.End <= p.Second.Start), $"ordered, bounded lyric cues: {track.Title}");
                }
                else Check(track.Cues.Length == 0 && track.Lyrics.Length == 0, $"instrumental has no vocal subtitles: {track.Title}");
                app.PlayTrack(i);
                await Frames(4);
                Check(video.IsPlaying() && video.Volume == 0 && video.GetStreamLength() > 30,
                    $"video plays silently with the original song: {track.Title}");
                Check(video.Loop == !track.IsVocal, $"full vocal MVs do not loop independently: {track.Title}");
                if (track.IsVocal)
                    Check(Math.Abs(video.GetStreamLength() - track.Duration) < .15,
                        $"lyric MV spans the complete song: {track.Title}");
                Check(player.Stream is AudioStreamMP3 { Loop: false } && player.PitchScale == 1
                    && Math.Abs(player.Stream.GetLength() - track.Duration) < .15, $"full original playback: {track.Title}");
                app.Seek(track.Duration - .18);
                await Until(() => app.IsEnded, 3);
                Check(app.PlaybackTime == track.Duration && !player.Playing, $"natural end without gameplay loop: {track.Title}");
            }
            app.PlayTrack(1);
            await Seconds(.25);
            if (!headless)
            {
                byte[] before = VideoFrame(video);
                await Seconds(.3);
                Check(!before.SequenceEqual(VideoFrame(video)), "decoded video frames visibly advance");
            }
            await Keypress(Key.Space);
            Check(app.IsPaused && player.StreamPaused && video.Paused, "Space pauses audio and video once");
            double paused = app.PlaybackTime;
            double pausedVideo = video.StreamPosition;
            byte[] pausedFrame = headless ? Array.Empty<byte>() : VideoFrame(video);
            await Seconds(.25);
            Check(Math.Abs(app.PlaybackTime - paused) < .05 && Math.Abs(video.StreamPosition - pausedVideo) < .05,
                "pause freezes audio and video clocks");
            if (!headless) Check(pausedFrame.SequenceEqual(VideoFrame(video)), "pause freezes the decoded picture");
            await Keypress(Key.Space);
            await Seconds(.2);
            Check(!app.IsPaused && app.PlaybackTime > paused, "Space resumes playback");
            app.Seek(48);
            await Until(() => Math.Abs(app.PlaybackTime - 48) < .5, 2);
            await Keypress(Key.Right);
            Check(Math.Abs(app.PlaybackTime - 58) < 1, $"ten-second seek updates audio and lyrics ({app.PlaybackTime:0.000}s)");
            Check(Math.Abs(video.StreamPosition - app.PlaybackTime % video.GetStreamLength()) < .2,
                "ten-second seek keeps the actual video in sync");
            Click(app, new Vector2(803, 616));
            Check(Math.Abs(app.PlaybackTime - tracks[1].Duration / 2) < 1, "mouse seeks through the full track");
            await Frames(4);
            Check(Math.Abs(video.StreamPosition - app.PlaybackTime % video.GetStreamLength()) < .2,
                "mouse seek keeps the actual video in sync");
            await Keypress(Key.R);
            app.Seek(tracks[1].Duration - .18);
            await Seconds(.55);
            Check(!app.IsEnded && app.CurrentIndex == 1 && app.PlaybackTime < 2, "repeat restarts only the current full song");
            await Keypress(Key.R);
            await Keypress(Key.Tab);
            await Keypress(Key.Pagedown);
            Check(app.CurrentIndex == 2, "next song follows the vocal filter");
            foreach (int index in Enumerable.Range(0, 5))
            {
                app.PlayTrack(index);
                app.Seek(tracks[index].Cues[16].Start + .6);
                await Frames(5);
                await Capture($"pv_{tracks[index].Character}");
            }
            await Keypress(Key.F);
            Check(app.IsFullscreen, "fullscreen toggle");
            Click(app, new Vector2(549, 155));
            Check(app.IsPaused && video.Paused, "fullscreen top controls pause playback");
            Click(app, new Vector2(549, 155));
            Check(!app.IsPaused, "fullscreen top controls resume playback");
            Click(app, new Vector2(640, 112));
            Check(Math.Abs(app.PlaybackTime - tracks[app.CurrentIndex].Duration / 2) < 1,
                "fullscreen seek stays above the embedded lyric area");
            await Frames(5);
            await Capture("03_fullscreen");
            await Keypress(Key.X);
            Check(!app.IsFullscreen && GetTree().CurrentScene == app, "Back first exits fullscreen");
            await Keypress(Key.Tab);
            await Keypress(Key.Z);
            Check(app.CurrentIndex == 5 && !tracks[app.CurrentIndex].IsVocal && video.IsPlaying(),
                "BGM filter reuses the character PV with its own full-length song");
            await Capture("04_bgm");
            if (!headless)
            {
                await Keypress(Key.X);
                await Until(() => GetTree().CurrentScene is Hub && !LoadingScreen.IsActive, 15);
                Check(!IsInstanceValid(app) && !IsInstanceValid(player) && !IsInstanceValid(video) && Audio.Instance.HasMusic,
                    "returning home releases both players and restores menu music");
                GetTree().CurrentScene.QueueFree();
            }
            else app.QueueFree();
            GetTree().CurrentScene = this;
            Audio.Instance.StopMusic(0);
            await Frames(4);
            GD.Print("[MusicAppQA] COMPLETE");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("[MusicAppQA] FAIL " + error);
            GetTree().Quit(1);
        }
    }

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Output));
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath($"{Output}/{name}.png")) == Error.Ok, "capture " + name);
    }
    private static byte[] VideoFrame(VideoStreamPlayer video)
    {
        using var image = video.GetVideoTexture().GetImage();
        return image.GetData();
    }
    private async Task Keypress(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }
    private static void Click(MusicApp app, Vector2 position)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(Pad).GetField("_mousePos", flags)!.SetValue(null, position);
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, true);
        typeof(Pad).GetField("_mLPrev", flags)!.SetValue(null, false);
        app._Process(1.0 / 60);
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, false);
        typeof(Pad).GetField("_mLPrev", flags)!.SetValue(null, false);
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Seconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private async Task Until(Func<bool> predicate, double timeout)
    {
        ulong deadline = Time.GetTicksMsec() + (ulong)(timeout * 1000);
        while (!predicate() && Time.GetTicksMsec() < deadline) await Frames(1);
        Check(predicate(), "condition reached before timeout");
    }
}
