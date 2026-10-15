using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class SettingsQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, Private)!.GetValue(target)!;
    private static void Write(object target, string field, object value) => target.GetType().GetField(field, Private)!.SetValue(target, value);
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private)!.Invoke(target, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[SettingsQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_settings/"), "isolated save data");
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--prepare") >= 0)
            {
                SaveSettings("{\"mode\":0,\"auto\":true,\"autosave\":false,\"msg\":2,\"shake\":0,\"flash\":0,\"reduceflash\":true,\"pauseunfocused\":true}");
                GD.Print("[SettingsQA] prepared preferences for a fresh process");
                GetTree().Quit();
                return;
            }
            var game = GetNode<GameManager>("/root/Game");
            Check(game.AutoAdvanceDialog && !game.AutoSaveEnabled && game.MsgCharsPerSec == DialogueBox.Speed(2),
                "dialogue and autosave preferences restore before opening settings");
            Check(Settings.ShakeStrength == 0 && Settings.HitFlashStrength == 0 && Settings.ReduceFlashes,
                "effect preferences restore before opening settings");
            if (DisplayServer.GetName() != "headless")
                Check(DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed, "saved window mode restores on startup");
            game.AutoSave();
            Check(!game.SlotExists(0), "disabled autosave does not create a slot after restart");

            await Frames(2);
            var stage = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(stage);
            GetTree().CurrentScene = stage;
            stage.SetProcess(false);
            stage.GetNode<Node>("StageAkari").SetProcess(false);
            stage.GetNode<Node>("World").ProcessMode = ProcessModeEnum.Inherit;
            var hud = stage.GetNode<Hud>("Hud");
            hud.HideBubble();
            var camera = stage.GetNode<GameCamera>("GameCamera");
            camera.Shake(8, 1);
            camera._Process(0.02);
            Check(camera.Offset == Vector2.Zero, "zero shake suppresses actual camera movement");
            hud.HitFlash();
            Check(Read<float>(hud, "_flashAlpha") == 0 && Read<double>(hud, "_hurtEdge") > 0,
                "zero hit flash retains the damage edge cue");
            hud.Flash();
            Check(Read<float>(hud, "_flashAlpha") < 0.1f, "reduced white flash stays subdued");

            var menu = GetNode<PauseMenu>("/root/PauseMenu");
            menu._Notification((int)NotificationApplicationFocusOut);
            Check(menu.IsOpen && GetTree().Paused, "focus loss pauses combat");
            var player = stage.GetNode<Player>("World/Player");
            var start = player.Position;
            Input.ActionPress("ui_right");
            await Frames(8);
            Input.ActionRelease("ui_right");
            Check(player.Position == start, "player cannot move while focus pause is active");
            menu._Notification((int)NotificationApplicationFocusIn);
            Check(menu.IsOpen && GetTree().Paused, "focus return requires deliberate resume");
            Invoke(menu, "Close");
            SaveSettings("{\"pauseunfocused\":false}");
            Settings.ApplySaved(game);
            menu._Notification((int)NotificationApplicationFocusOut);
            Check(!menu.IsOpen && !GetTree().Paused, "focus pause can be disabled");
            Check(Settings.ShakeStrength == 1 && Settings.HitFlashStrength == 1 && !Settings.ReduceFlashes,
                "unspecified effects retain the existing default strength");
            game.AutoSaveEnabled = false;

            Check(game.SaveToSlot(1), "successful save reports success");
            string slot = ProjectSettings.GlobalizePath("user://save_1.json");
            using (var locked = new System.IO.FileStream(slot, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
            {
                Check(!game.SaveToSlot(1), "unwritable slot reports failure");
                menu.Open();
                Invoke(menu, "OpenAsk", PauseMenu.QuitKind.Exit, false);
                Invoke(menu, "RunAsk", PauseMenu.AskChoice.Save);
                menu.QuitOverride = () => { };
                Invoke(menu, "RunSlot", 1);
                Check(menu.IsOpen && menu.SlotOpen && !menu.QuitRequested && menu.SavedText.Contains("保存できません"),
                    "save failure keeps the exit flow open with an error");
                await Shot("save_failure");
            }
            Check(game.LoadFromSlot(1), "failed write preserves the existing locked save");
            Invoke(menu, "RunSlot", 1);
            Check(menu.QuitRequested, "retrying the save completes the pending exit");
            menu.QuitOverride = null;
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("user://save_2.json"), "[]");
            menu.Open();
            Invoke(menu, "OpenSlots", false);
            Invoke(menu, "RunSlot", 2);
            Check(menu.IsOpen && menu.SlotOpen && menu.SavedText.Contains("読み込めません"),
                "invalid load keeps the pause slot picker open with an error");
            Invoke(menu, "Close");
            GetNode<BulletPool>("/root/Pool").DespawnAll();
            stage.QueueFree();
            await Frames(3);

            var title = GD.Load<PackedScene>("res://TitleMenu.tscn").Instantiate<TitleMenu>();
            GetTree().Root.AddChild(title);
            GetTree().CurrentScene = title;
            title.SetProcess(false);
            Write(title, "_picking", true);
            Write(title, "_pick", 2);
            Write(title, "_t", 2d);
            Write(title, "_zHeld", false);
            Input.ActionPress("ui_accept");
            title._Process(0.02);
            Input.ActionRelease("ui_accept");
            Check(Read<bool>(title, "_picking") && Read<string>(title, "_toast").Contains("読み込めません"),
                "title refuses an invalid save instead of starting a new state");
            await Shot("load_failure");
            title.QueueFree();
            await Frames(3);

            SaveSettings("{\"msg\":99,\"shake\":-50,\"flash\":900,\"inputdisplay\":2}");
            var settings = GD.Load<PackedScene>("res://Settings.tscn").Instantiate<Settings>();
            GetTree().Root.AddChild(settings);
            GetTree().CurrentScene = settings;
            settings.SetProcess(false);
            Check(game.MsgCharsPerSec == DialogueBox.Speed(2) && Settings.ShakeStrength == 0 && Settings.HitFlashStrength == 1,
                "out-of-range saved settings clamp to supported values");
            Invoke(settings, "Save");
            Check(AudioConfig.GetInt("inputdisplay", -1) == 2, "controller selection preserves its saved device value");
            string[] categories = { "display", "audio", "gameplay", "controls", "accessibility" };
            for (int i = 0; i < categories.Length; i++)
            {
                Write(settings, "_cat", i);
                settings.QueueRedraw();
                await Shot($"settings_{categories[i]}");
            }
            settings.QueueFree();
            Audio.Instance?.StopMusic(0);
            await Frames(3);
            GD.Print("[SettingsQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[SettingsQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private static void SaveSettings(string json)
    {
        using var file = Godot.FileAccess.Open("user://settings.json", Godot.FileAccess.ModeFlags.Write);
        file.StoreString(json);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = "res://build/qa_settings/shots";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path));
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
