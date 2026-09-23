using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

public partial class PhoneAppTransitionQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static object? Call(object obj, string method, params object[] args)
        => obj.GetType().GetMethod(method, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        GD.Print($"[PhoneAppQA] PASS {text}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var poseMethod = typeof(PhoneAppTransition).GetMethod("PhonePose", BindingFlags.Static | BindingFlags.NonPublic)!;
            var rectMethod = typeof(PhoneAppTransition).GetMethod("AppRect", BindingFlags.Static | BindingFlags.NonPublic)!;
            bool inside = true;
            for (float t = 0; t < PhoneAppTransition.TurnDuration; t += 0.01f)
            {
                var (angle, scale) = ((float, float))poseMethod.Invoke(null, new object[] { t })!;
                foreach (var corner in new[] { new Vector2(-240, -360), new(240, -360), new(240, 360), new(-240, 360) })
                {
                    Vector2 screen = new Vector2(640, 360) + corner.Rotated(angle) * scale;
                    inside &= screen.X >= -0.1f && screen.X <= 1280.1f && screen.Y >= -0.1f && screen.Y <= 720.1f;
                }
            }
            Check(inside, "turning phone stays inside the viewport throughout its motion");
            var finalPose = ((float Angle, float Scale))poseMethod.Invoke(null, new object[] { PhoneAppTransition.TurnDuration })!;
            Check(Mathf.IsEqualApprox(finalPose.Angle, Mathf.Pi / 2), "phone turns exactly ninety degrees");
            Check((Rect2)rectMethod.Invoke(null, new object[] { PhoneAppTransition.Duration })! == new Rect2(0, 0, 1280, 720),
                "expansion ends at full landscape size");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            Read<HashSet<string>>(game, "_cleared").Add(GameManager.FirstStageId);
            game.ShopTutorialSeen = true;
            game.TrainingSetImpression(10000);
            bool movie = Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--movie");
            Vector2I[] sizes = movie ? new[] { new Vector2I(1280, 720) }
                : new[] { new Vector2I(1280, 720), new(960, 540), new(1920, 1080), new(540, 960) };
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            foreach (Vector2I size in sizes)
            {
                DisplayServer.WindowSetSize(size);
                await Frames(4);
                foreach (var (index, type) in new[] { (1, typeof(Shop)), (4, typeof(Customize)), (2, typeof(Records)) })
                {
                    var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
                    GetTree().Root.AddChild(hub);
                    GetTree().CurrentScene = hub;
                    await Frames(movie ? 65 : 40);
                    long wallet = game.Impression;
                    Call(hub, "OpenHomeApp", index);
                    Call(hub, "OpenHomeApp", index);
                    Check(GetTree().CurrentScene == hub && GetNodeOrNull<PhoneAppTransition>("/root/PhoneAppTransition") != null,
                        $"{type.Name} begins with the portrait phone instead of an instant scene change");
                    SetKeys(true);
                    await Frames(20);
                    Check(GetTree().CurrentScene == hub, "rotation finishes before changing scenes");
                    await Shot($"{type.Name}_turn_{size.X}x{size.Y}");
                    await Frames(24);
                    var app = GetTree().CurrentScene as Node2D;
                    Check(app?.GetType() == type && app.Scale.X < 1 && app.Scale.Y < 1,
                        $"{type.Name} appears inside the expanding landscape phone");
                    await Shot($"{type.Name}_expand_{size.X}x{size.Y}");
                    await Frames(38);
                    Check(GetTree().CurrentScene == app && app!.Scale == Vector2.One && app.Position == Vector2.Zero
                        && GetNodeOrNull<PhoneAppTransition>("/root/PhoneAppTransition") == null,
                        $"{type.Name} transition cleans up and restores exact input coordinates");
                    Check(!GetNode<PauseMenu>("/root/PauseMenu").IsOpen && wallet == game.Impression,
                        "held confirm, back and pause do not purchase, leave or open an overlay");
                    SetKeys(false);
                    await Frames(movie ? 65 : 5);
                    await Shot($"{type.Name}_open_{size.X}x{size.Y}");
                    string selection = type == typeof(Customize) ? "_focus" : "_sel";
                    int before = Read<int>(app!, selection);
                    Input.ActionPress("ui_down");
                    await Frames(3);
                    Input.ActionRelease("ui_down");
                    await Frames(3);
                    Check(!Pad.UiBlocked(app!) && Read<int>(app!, selection) != before,
                        "destination navigation works after animation");
                    app!.QueueFree();
                    await Frames(5);
                }
            }
            Audio.Instance?.StopMusic(0);
            foreach (var node in GetNode<Audio>("/root/Audio").GetChildren())
                if (node is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[PhoneAppQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            SetKeys(false);
            GD.PushError($"[PhoneAppQA] FAIL {error}");
            GetTree().Quit(1);
        }
    }

    private static void SetKeys(bool pressed)
    {
        foreach (Key key in new[] { Key.Z, Key.X, Key.Escape })
            Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/phone_app/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
