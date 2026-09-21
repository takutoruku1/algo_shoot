using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossRetryQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Property(object obj, string name, object value) => obj.GetType().GetProperty(name)!.SetValue(obj, value);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[BossRetryQA] PASS {message}");
    }

    private GameManager _game = null!;

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            foreach (long score in new long[] { 0, 1, 2, 999, 10000, long.MaxValue })
            {
                Property(_game, "Score", score);
                _game.PrepareBossRetry();
                _game.ResetRun();
                Check(_game.Score == score / 2, $"score {score} retains half without overflow");
                _game.ResetRun();
                Check(_game.Score == 0, "retry score is consumed exactly once");
            }
            Property(_game, "Score", 1000L);
            _game.PrepareBossRetry();
            _game.ResetPersistent();
            _game.ResetRun();
            Check(_game.Score == 0, "new game discards a pending retry score");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle", "Stage0" })
            {
                _game.SelectedEntry = GameManager.StageEntry.Start;
                _game.SelectedJob = Job.Melee;
                _game.Difficulty = GameManager.Diff.Hard;
                var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                Freeze(root);
                Property(_game, "Score", 10000L);
                Property(_game, "Impression", 2345L);
                Property(_game, "Followers", 678);
                root = await RetryChoice(root, 0, mouse: false, capture: scene == "Akari");
                Check(_game.Score == 5000, $"{scene}: selected boss retry carries 5000 through scene reload");
                CheckEntry(root, scene, boss: true);
                root = await RetryChoice(root, 0, mouse: true);
                Check(_game.Score == 2500, $"{scene}: repeat mouse retry charges half once, not twice");

                Property(_game, "Score", 999L);
                root = await RetryKey(root, shift: false, alive: false);
                Check(_game.Score == 499, $"{scene}: R uses the same half-score cost");
                CheckEntry(root, scene, boss: true);
                Property(_game, "Score", 501L);
                root = await RetryKey(root, shift: true, alive: false);
                Check(_game.Score == 0, $"{scene}: Shift+R starts over with zero score");
                CheckEntry(root, scene, boss: false);
                Property(_game, "Score", 800L);
                root = await RetryChoice(root, 1, mouse: false);
                Check(_game.Score == 0, $"{scene}: start-over choice does not carry retry points");
                Property(_game, "Score", 600L);
                root = await RetryKey(root, shift: false, alive: true);
                Check(_game.Score == 0, $"{scene}: normal held-R restart still resets score");
                Property(_game, "Score", 0L);
                root = await RetryChoice(root, 0, mouse: false);
                Check(_game.Score == 0, $"{scene}: zero score does not prevent retry");
                Check(_game.Impression == 2345 && _game.Followers == 678, "retry does not spend permanent currencies");
                Check(_game.SelectedJob == Job.Melee && _game.Difficulty == GameManager.Diff.Hard, "character and difficulty survive retry");
                root.QueueFree();
                await Frames(3);
                GameManager.ClearGameOverChoice(null);
                Hud.BubblePaused = false;
            }
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[BossRetryQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[BossRetryQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private void Freeze(Node2D root)
    {
        root.SetProcess(false);
        ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
        root.GetNode<Node2D>("World").ProcessMode = ProcessModeEnum.Disabled;
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        hud.SetCinematicMode(false);
    }

    private async Task<Node2D> Reloaded(Node2D previous)
    {
        await Frames(4);
        var root = (Node2D)GetTree().CurrentScene;
        Check(root != previous, "scene was actually reloaded");
        Freeze(root);
        Check(root.GetNode<Player>("World/Player").Lives == _game.StartLives && _game.Bombs == _game.StartBombs,
            "lives and bombs are restored independently of score");
        Check(_game.Combo == 0 && _game.RunHitCount == 0, "other run state resets normally");
        return root;
    }

    private async Task<Node2D> RetryChoice(Node2D root, int selected, bool mouse, bool capture = false)
    {
        var hud = root.GetNode<Hud>("Hud");
        Property(root.GetNode<Player>("World/Player"), "Lives", 0);
        hud.SetLives(0);
        bool held = false;
        long score = _game.Score;
        GameManager.HandleGameOverExit(root, hud, ref held);
        var choice = (ChoiceOverlay)typeof(GameManager).GetField("_gameOverChoice", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        choice.SetProcess(false);
        choice._Process(2d);
        Check(Read<string[]>(choice, "_choices")[0].Contains("\u534a\u5206\u6d88\u8cbb"), "half-score cost is visible before confirmation");
        var rect = (Rect2)typeof(ChoiceOverlay).GetMethod("RowRect", Private)!.Invoke(choice, new object[] { selected })!;
        Check(rect.Position.X >= Field.DLeft && rect.End.X <= Field.DRight, "retry option fits inside the playfield");
        Check(_game.Score == score, "opening the choices does not charge score");
        if (capture)
        {
            root.GetNode<StageBackground>("StageBackground").EnterBoss();
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                await Frames(8);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng(ProjectSettings.GlobalizePath($"res://build/qa_story/boss_retry/menu_{size.X}x{size.Y}.png")) == Error.Ok, "retry cost screenshot");
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
        Property(choice, "Selected", selected);
        if (mouse)
        {
            Vector2 oldMouse = Pad.MousePos();
            PadField("_mousePos", rect.GetCenter());
            PadField("_usingMouse", true);
            PadField("_mL", true);
            PadField("_mLPrev", false);
            choice._Process(0.016d);
            PadField("_mousePos", oldMouse);
            PadField("_mL", false);
            PadField("_usingMouse", false);
        }
        else
        {
            Input.ActionPress("ui_accept");
            choice._Process(0.016d);
            Input.ActionRelease("ui_accept");
        }
        choice._Process(0.51d);
        Check(choice.Decided && choice.Selected == selected, "choice confirms through its input handler");
        Check(GameManager.HandleGameOverExit(root, hud, ref held), "confirmed choice initiates reload");
        return await Reloaded(root);
    }

    private async Task<Node2D> RetryKey(Node2D root, bool shift, bool alive)
    {
        Property(root.GetNode<Player>("World/Player"), "Lives", alive ? _game.StartLives : 0);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, Pressed = shift });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = true });
        await Frames(2);
        Check(Input.IsKeyPressed(Key.R) && !Pad.UiBlocked(root), "retry shortcut reaches the stage input handler");
        root._Process(alive ? RetryHold.HoldTime + 0.01d : 0.016d);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = false });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, Pressed = false });
        return await Reloaded(root);
    }

    private void CheckEntry(Node2D root, string scene, bool boss)
    {
        if (scene is "Akari" or "Koharu" or "Rei")
        {
            var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
            int step = Read<int>(stage, "_step");
            Check(boss ? step >= 10 : step == 1, $"{scene}: retry keeps the requested entry point");
        }
        Check(_game.SelectedEntry == GameManager.StageEntry.Start, "entry selection does not leak into another stage");
    }

    private static void PadField(string name, object value)
        => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
