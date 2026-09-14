using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

public partial class HubJobQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static string Mode(Hub hub) => Read<object>(hub, "_mode").ToString()!;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[HubJobQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(30);
            Check(Mode(hub) == "Cards", "new game starts on stage selection without a forced job prompt");
            await Shot("cards");
            Click(hub, new Rect2(540, 30, 184, 42), "ProcessCards");
            Check(Mode(hub) == "Job", "header button opens all four job types");
            foreach (var job in Jobs.All)
            {
                var face = Read<Dictionary<string, Texture2D>>(hub, "_playerFaces")[job.CharacterId];
                Check(face.ResourcePath == $"res://char/player/{job.CharacterId}/{job.CharacterId}_spin_v2_00.png",
                    $"{job.CharacterId} player portrait is available before stage clears");
                float labelWidth = UiKit.TextW(UiKit.Zen, "捨てる", UiKit.FontSmall) + 10f;
                float bodyWidth = 1040f - 88f - 18f - labelWidth;
                Check(UiKit.TextW(UiKit.Zen, job.Strength, UiKit.FontLabel) <= bodyWidth
                    && UiKit.TextW(UiKit.Zen, job.Weakness, UiKit.FontLabel) <= bodyWidth,
                    $"{job.CharacterId} descriptions fit beside the portrait");
            }
            await Frames(20);
            await Shot("types");
            Click(hub, (Rect2)Call(hub, "JobHitRect", 1)!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Cards" && game.SelectedJob == Job.Melee, "header selection applies the job and returns to cards");
            await Frames(10);

            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail", "stage selection opens its difficulty screen");
            await KeyAction("ui_down");
            int stage = Read<int>(hub, "_sel"), tier = Read<int>(hub, "_tierSel");
            Check(tier == (int)GameManager.Diff.Hard, "difficulty can be chosen before changing type");
            Check(!((Rect2)Call(hub, "DetailJobRect", true)!).Intersects((Rect2)Call(hub, "TierHitRect", 3)!),
                "type button does not overlap difficulty rows");
            await Frames(240);
            await Shot("detail");
            for (int i = 0; i < Jobs.All.Length; i++)
            {
                Click(hub, (Rect2)Call(hub, "DetailJobRect", true)!, "ProcessDetail", 0.01);
                Check(Mode(hub) == "Job", "detail button opens job selection");
                await Frames(20);
                Click(hub, (Rect2)Call(hub, "JobHitRect", i)!, "ProcessJob", 0.01);
                Check(Mode(hub) == "Detail" && game.SelectedJob == Jobs.All[i].Id
                    && game.SelectedShotMode == Jobs.All[i].Mode, $"{Jobs.All[i].Id} applies without starting combat");
                Check(Read<int>(hub, "_sel") == stage && Read<int>(hub, "_tierSel") == tier
                    && !Read<bool>(hub, "_dived"), "stage and difficulty remain selected");
                await Frames(20);
            }
            await Keypress(Key.J);
            Check(Mode(hub) == "Job", "J also opens types from the detail screen");
            await KeyAction("ui_down");
            await Keypress(Key.X);
            Check(Mode(hub) == "Detail" && game.SelectedJob == Job.Magic, "keyboard cancel keeps the selected job");
            await Keypress(Key.J);
            Click(hub, (Rect2)Call(hub, "JobCloseRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail", "close button returns to the originating detail screen");
            await Frames(10);
            await Keypress(Key.J);
            game.JobForcedByCmdline = true;
            await KeyAction("ui_down");
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail" && game.SelectedJob == Job.Magic, "debug job lock preserves the detail screen and job");
            game.JobForcedByCmdline = false;
            await Frames(240);

            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(20);
            await Shot("detail_small");
            Click(hub, (Rect2)Call(hub, "DetailJobRect", true)!, "ProcessDetail", 0.01);
            await Frames(20);
            await Shot("types_small");
            await Keypress(Key.X);
            await Keypress(Key.X);
            Check(Mode(hub) == "Cards", "detail still closes to stage selection");
            await Shot("cards_small");
            await Keypress(Key.J);
            await Keypress(Key.X);
            Check(Mode(hub) == "Cards", "existing card-screen shortcut still returns to cards");

            var cleared = Read<HashSet<string>>(game, "_cleared");
            foreach (var item in GameManager.Stages) cleared.Add(item.Id);
            Call(hub, "BuildEntries");
            var entries = Read<IList>(hub, "_entries");
            int final = -1;
            for (int i = 0; i < entries.Count; i++)
                if ((bool)entries[i]!.GetType().GetField("IsFinal")!.GetValue(entries[i])!) final = i;
            Check(final >= 0, "final stage is available for layout verification");
            Write(hub, "_sel", final);
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail", "final stage detail opens normally");
            await Shot("final_detail_small");
            Click(hub, (Rect2)Call(hub, "DetailJobRect", false)!, "ProcessDetail", 0.01);
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "JobCloseRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail" && Read<int>(hub, "_sel") == final, "final stage job button returns to its shorter detail layout");
            hub.QueueFree();
            await Frames(5);
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer player) player.Stop();
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[HubJobQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[HubJobQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static void Click(Hub hub, Rect2 rect, string handler, params object[] args)
    {
        // Inject design-space mouse state without moving the desktop cursor.
        void PadField(string name, object value) => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
        Vector2 previousPosition = Pad.MousePos();
        PadField("_mousePos", rect.GetCenter());
        PadField("_usingMouse", true);
        PadField("_mL", true);
        PadField("_mLPrev", false);
        Call(hub, handler, args);
        PadField("_mousePos", previousPosition);
        PadField("_mL", false);
        PadField("_mLPrev", false);
        PadField("_usingMouse", false);
    }

    private async Task Keypress(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        await Frames(20);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task KeyAction(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Frames(2);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        await Frames(3);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/hub_job/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
