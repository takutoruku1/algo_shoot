using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

public partial class EpilogueQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";
    private readonly List<Image> _captures = new();
    private static T Read<T>(object obj, string field)
        => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value)
        => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[EpilogueQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/ending/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            GetNode<GameManager>("/root/Game").MsgCharsPerSec = 300;
            await Frames(1);

            for (int choice = 0; choice < 3; choice++)
            {
                var ep = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
                GetTree().Root.AddChild(ep);
                GetTree().CurrentScene = ep;
                await Frames(30);
                foreach (string field in new[] { "_skyNight", "_skyDawn", "_rest", "_goodbye" })
                    Check(Read<Texture2D>(ep, field).ResourcePath.StartsWith("res://char/bg2/ending/"), $"{field} loads ending art");

                if (choice == 0)
                {
                    await Frames(60);
                    var gaze = await Shot("gaze");
                    MatchArt(gaze, Read<Texture2D>(ep, "_rest"), true);
                    DisplayServer.WindowSetSize(new Vector2I(960, 540));
                    await Frames(15);
                    var small = await Shot("gaze_small");
                    Check(small.GetWidth() == 960 && small.GetHeight() == 540, "small viewport dimensions");
                    MatchArt(small, Read<Texture2D>(ep, "_rest"), true);
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                    await Frames(15);

                    await AdvanceUntil(() => Read<int>(ep, "_phase") == 1);
                    Check(Read<float>(ep, "_dawnK") < 0.15f, "walking starts at night");
                    await Shot("walk_night");
                    double walkTime = Read<double>(ep, "_walkT");
                    var before = await Shot("walk_motion_before");
                    await Frames(20);
                    var after = await Shot("walk_motion_after");
                    Check(Read<double>(ep, "_walkT") > walkTime, "walking animation advances");
                    int changed = 0;
                    for (int y = 360; y < 515; y += 3)
                        for (int x = 410; x < 880; x += 3)
                        {
                            Color a = before.GetPixel(x, y), b = after.GetPixel(x, y);
                            if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) > 0.2f) changed++;
                        }
                    Check(changed > 100, "walking sprites visibly animate");
                    await AdvanceUntil(() => Read<int>(ep, "_line") == Read<IList>(ep, "_walk").Count - 1);
                    await Frames(180);
                    Check(Read<float>(ep, "_dawnK") > 0.999f, "last walking line reaches dawn");
                    MatchArt(await Shot("walk_dawn"), Read<Texture2D>(ep, "_skyDawn"), false);
                    await VerifyWalking(ep);
                    await AdvanceUntil(() => Read<int>(ep, "_phase") == 2);
                    Write(ep, "_t", 11.0);
                    await Frames(2);
                    await Shot("credits");
                    await AdvanceUntil(() => Read<int>(ep, "_phase") == 3);
                }
                else
                {
                    Audio.Instance?.StopMusicOnce(0);
                    Write(ep, "_phase", 3);
                    Write(ep, "_dawnT", 1.0);
                    Write(ep, "_dawnK", 1f);
                }

                await AdvanceUntil(() => Read<ChoiceOverlay?>(ep, "_e6Choice") != null);
                await Frames(60);
                var overlay = Read<ChoiceOverlay>(ep, "_e6Choice");
                Check(Read<double>(ep, "_goodbyeT") == 0, "goodbye CG waits until after the choice");
                if (choice == 0) await Shot("choice");
                while (overlay.Selected != choice)
                {
                    Input.ParseInputEvent(new InputEventAction { Action = "ui_up", Pressed = true });
                    await Frames(2);
                    Input.ParseInputEvent(new InputEventAction { Action = "ui_up", Pressed = false });
                    await Frames(2);
                }
                await AdvanceUntil(() => Read<ChoiceOverlay?>(ep, "_e6Choice") == null);
                await AdvanceUntil(() => Read<int>(ep, "_line") == Read<IList>(ep, "_end").Count - 2);
                Check(Read<double>(ep, "_goodbyeT") < 1.2, $"choice {choice} starts a goodbye fade");
                await Frames(90);
                MatchArt(await Shot($"goodbye_{choice}"), Read<Texture2D>(ep, "_goodbye"), true);
                double goodbyeTime = Read<double>(ep, "_goodbyeT");
                await AdvanceUntil(() => Read<int>(ep, "_line") == Read<IList>(ep, "_end").Count - 1);
                Check(Read<double>(ep, "_goodbyeT") > goodbyeTime, "line changes do not restart the CG fade");
                if (choice == 0)
                {
                    await Frames(80);
                    await Shot("end");
                    DisplayServer.WindowSetSize(new Vector2I(960, 540));
                    await Frames(15);
                    await Shot("end_small");
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                }
                await AdvanceUntil(() => GetTree().CurrentScene.SceneFilePath == "res://TitleMenu.tscn");
                Check(!IsInstanceValid(ep), $"choice {choice} returns to title");
                GetTree().CurrentScene.QueueFree();
                await Frames(2);
            }
            foreach (var capture in _captures) capture.Dispose();
            _captures.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[EpilogueQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[EpilogueQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task VerifyWalking(Epilogue ep)
    {
        var walkers = Read<EpilogueWalker[]>(ep, "_walkers");
        var periods = new HashSet<float>();
        foreach (var walker in walkers)
        {
            periods.Add(walker.Motion.Cycle);
            float minBody = 0, maxBody = 0, maxLift = 0;
            var initial = walker.Pose(0, 0);
            float upperLength = initial.Hip.DistanceTo(initial.Knee);
            float lowerLength = initial.Knee.DistanceTo(initial.Ankle);
            bool grounded = true, stableBones = true;
            for (int frame = 0; frame < 360; frame++)
            {
                double time = frame / 60.0;
                float body = walker.BodyOffset(time);
                minBody = Mathf.Min(minBody, body);
                maxBody = Mathf.Max(maxBody, body);
                var back = walker.Pose(0, time);
                var front = walker.Pose(1, time);
                grounded &= Mathf.Max(back.SoleY, front.SoleY) >= 358.99f
                    && back.SoleY <= 359.01f && front.SoleY <= 359.01f;
                maxLift = Mathf.Max(maxLift, 359f - Mathf.Min(back.SoleY, front.SoleY));
                stableBones &= Mathf.Abs(back.Hip.DistanceTo(back.Knee) - upperLength) < 0.01f
                    && Mathf.Abs(back.Knee.DistanceTo(back.Ankle) - lowerLength) < 0.01f;
            }
            float bodyRange = (maxBody - minBody) * EpilogueWalker.Height / 360f;
            Check(bodyRange > 0.5f && bodyRange < 0.9f, $"{walker.Name} retains subtle upper-body sway ({bodyRange:F2} design pixels)");
            Check(Mathf.Abs(walker.Torso(walker.Motion.Cycle / 4f).Rotation) <= Mathf.DegToRad(0.5f), $"{walker.Name} torso never rocks excessively");
            Check(grounded && maxLift * EpilogueWalker.Height / 360f < 1.1f, $"{walker.Name} stays grounded without high knees");
            Check(stableBones, $"{walker.Name} leg lengths stay constant");
            Check(walker.Motion.Stride * 2f * EpilogueWalker.Height / 360f >= 5.5f, $"{walker.Name} takes full walking steps");
            double armPeak = (1f - walker.Motion.Phase) * walker.Motion.Cycle;
            Check(walker.ArmAngle(0, armPeak) > Mathf.DegToRad(4)
                && walker.ArmAngle(0, armPeak + walker.Motion.Cycle * 0.5) < Mathf.DegToRad(-4)
                && Mathf.Abs(walker.ArmAngle(0, armPeak) + walker.ArmAngle(1, armPeak)) < 0.001f,
                $"{walker.Name} swings both arms in opposition");
        }
        Check(periods.Count == 4, "all four characters have distinct walking tempos");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Frames(15);
        for (int frame = 0; frame < 120; frame++)
        {
            Write(ep, "_walkT", frame / 20.0);
            await Frames(2);
            using var capture = GetViewport().GetTexture().GetImage();
            Check(capture.SavePng($"{_out}/walk_cycle_{frame:D3}.png") == Error.Ok, $"walking frame {frame}");
        }
        await Shot("walk_small");
        var layer = new CanvasLayer { Layer = 100 };
        GetTree().Root.AddChild(layer);
        layer.AddChild(new ColorRect { Size = new Vector2(384, 216), Color = new Color("bcc9ce") });
        var closeup = new Node2D { Scale = new Vector2(2.6f, 2.6f), Position = new Vector2(-307.2f, -224) };
        closeup.Draw += () =>
        {
            for (int i = 0; i < walkers.Length; i++)
                walkers[i].Draw(closeup, 142f + 100f * i / 3f, Read<double>(ep, "_walkT"), 1);
        };
        layer.AddChild(closeup);
        for (int frame = 0; frame < 8; frame++)
        {
            Write(ep, "_walkT", frame / 4.0);
            closeup.QueueRedraw();
            await Frames(2);
            await Shot($"walk_detail_{frame}");
        }
        layer.QueueFree();
        await Frames(2);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(15);
    }

    private async Task AdvanceUntil(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = true });
            await Frames(16);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = false });
            await Frames(2);
        }
        Check(condition(), "dialogue advances to target");
    }

    private async Task<Image> Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        _captures.Add(image);
        Check(image.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
        return image;
    }

    private static void MatchArt(Image rendered, Texture2D texture, bool includeCharacters)
    {
        using var reference = texture.GetImage();
        int count = 0, matches = 0;
        foreach (float y in new[] { 0.02f, 0.2f, 0.4f, 0.6f })
            foreach (float x in new[] { 0.02f, 0.2f, 0.4f, 0.46f, 0.53f, 0.6f, 0.8f, 0.98f })
            {
                if (!includeCharacters && y > 0.45f && x > 0.35f && x < 0.65f) continue;
                Color a = rendered.GetPixel((int)(rendered.GetWidth() * x), (int)(rendered.GetHeight() * y));
                Color b = reference.GetPixel((int)(reference.GetWidth() * x), (int)(reference.GetHeight() * y));
                if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) < 0.16f) matches++;
                count++;
            }
        Check(matches >= count * 0.9f, $"{texture.ResourcePath} fills the viewport without duplicate characters ({matches}/{count})");
    }
}
