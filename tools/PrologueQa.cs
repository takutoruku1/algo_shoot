using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class PrologueQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";
    private static T Read<T>(object obj, string field)
        => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[PrologueQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/prologue/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            GetNode<GameManager>("/root/Game").MsgCharsPerSec = 300;
            await Frames(1);

            for (int route = 0; route < 3; route++)
            {
                GetNode<GameManager>("/root/Game").ResetPersistent();
                var pro = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
                GetTree().Root.AddChild(pro);
                GetTree().CurrentScene = pro;
                await Frames(60);
                var backgrounds = Read<Texture2D[]>(pro, "_backgrounds");
                Check(backgrounds.Length == 4, "four opening backgrounds loaded");
                foreach (var texture in backgrounds)
                    Check(texture.ResourcePath.StartsWith("res://char/bg2/prologue/") && texture.GetWidth() > 1000,
                        $"background resource {texture.ResourcePath}");

                if (route == 0)
                {
                    await Shot("boot", pro);
                    await WaitUntil(() => Read<int>(pro, "_phase") == 1);
                    await Frames(10);
                    await Shot("identity", pro);
                    DisplayServer.WindowSetSize(new Vector2I(960, 540));
                    await Frames(15);
                    await Shot("identity_small", pro);
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                    await Frames(15);
                    await WaitUntil(() => Read<int>(pro, "_phase") == 2);
                    await Frames(35);
                    Check(Read<float>(pro, "_backdropMix") is > 0.3f and < 0.8f, "awakening crossfades during ignition");
                    await Shot("awakening_fade", pro);
                    await WaitUntil(() => Read<int>(pro, "_phase") == 3);
                    await Frames(30);
                    await Shot("awakening", pro);
                }

                await AdvanceUntil(() => Read<ChoiceOverlay?>(pro, "_choice") != null);
                Check(!GameManager.MinaNamed, "Mina is unnamed at the first choice");
                await Frames(60);
                Check(Read<float>(pro, "_choiceShade") == 1f, "first choice dims the bright awakening background");
                if (route == 0) await Shot("first_choice");
                await Choose(pro, route);
                await AdvanceUntil(() => Read<int>(pro, "_line") >= 3);
                if (route == 0)
                {
                    await Frames(60);
                    await Shot("first_words", pro);
                    DisplayServer.WindowSetSize(new Vector2I(960, 540));
                    await Frames(15);
                    await Shot("first_words_small", pro);
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                    await Frames(15);
                }
                await AdvanceUntil(() => Read<ChoiceOverlay?>(pro, "_choice") != null);
                Check(Read<int>(pro, "_backdrop") == 1, "naming keeps the awakening background");
                await Choose(pro, route);
                await AdvanceUntil(() => GameManager.MinaNamed);
                Check(Read<int>(pro, "_backdrop") == 1, $"name route {route} does not switch to timeline early");
                await AdvanceUntil(() => Read<int>(pro, "_backdrop") == 2);
                Check(Read<float>(pro, "_backdropMix") < 1f, "timeline starts a crossfade");
                await Frames(80);
                Check(Read<PostToast?>(pro, "_toast") != null, "timeline post remains visible over the illustration");
                if (route == 0) await Shot("timeline", pro);
                await AdvanceUntil(() => Read<int>(pro, "_backdrop") == 3);
                Check(Read<float>(pro, "_backdropMix") < 1f, "erased draft starts its own crossfade");
                await Frames(90);
                if (route == 0) await Shot("unsent_typing", pro);
                await WaitUntil(() => Read<int>(pro, "_fxStep") >= 2);
                Check(Read<PostToast?>(pro, "_toast") != null, "draft typing and erasing continues");
                await AdvanceUntil(() => Read<ChoiceOverlay?>(pro, "_choice") != null);
                Check(Read<int>(pro, "_backdrop") == 3 && Read<float>(pro, "_backdropMix") == 1f,
                    "draft background persists through the final choice");
                if (route == 0)
                {
                    await Frames(60);
                    await Shot("last_choice");
                    DisplayServer.WindowSetSize(new Vector2I(960, 540));
                    await Frames(15);
                    await Shot("last_choice_small");
                }
                await Choose(pro, route % 2);
                await Frames(80);
                Check(Read<float>(pro, "_choiceShade") == 0f, "background brightness returns after choosing");
                if (route == 0) await Shot("unsent_reply_small", pro);
                Check(Read<float>(pro, "_backdropMix") == 1f, "dialogue and choices do not restart the fade");
                await AdvanceUntil(() => Read<PostToast?>(pro, "_toast") is PostToast toast
                    && Read<string>(toast, "_handle") == GameManager.Stages[0].Handle);
                var firstPost = Read<PostToast>(pro, "_toast");
                Check(Read<string>(firstPost, "_body") == GameManager.Stages[0].Tweet, "opening shows the first stage's actual SNS post");
                Check(Read<Texture2D>(firstPost, "_iconTex").ResourcePath.Contains("/player/akari/"), "first stage post uses Akari's portrait");
                if (route == 0) await Shot("first_stage_post");
                await AdvanceUntil(() => !IsInstanceValid(pro));
                Check(GetTree().CurrentScene.SceneFilePath == "res://Hub.tscn", $"route {route} reaches the hub");
                var hub = (Hub)GetTree().CurrentScene;
                Check(Read<object>(hub, "_mode").ToString() == "Cards", "opening continues into SNS instead of home");
                var entries = Read<System.Collections.IList>(hub, "_entries");
                var selected = entries[Read<int>(hub, "_sel")]!;
                Check((string)selected.GetType().GetField("Id")!.GetValue(selected)! == GameManager.FirstStageId,
                    "SNS keeps the first stage post selected");
                GetTree().CurrentScene.QueueFree();
                await Frames(2);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                await Frames(15);
            }
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
            await Task.Delay(250);
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[PrologueQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[PrologueQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 1800 && !condition(); i++) await Frames(1);
        Check(condition(), "automatic sequence advances");
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

    private async Task Choose(Prologue pro, int selected)
    {
        await Frames(2);
        var overlay = Read<ChoiceOverlay>(pro, "_choice");
        for (int i = 0; i < 3 && overlay.Selected != selected; i++)
        {
            Input.ParseInputEvent(new InputEventAction { Action = "ui_down", Pressed = true });
            await Frames(2);
            Input.ParseInputEvent(new InputEventAction { Action = "ui_down", Pressed = false });
            await Frames(2);
        }
        Check(overlay.Selected == selected, $"select option {selected}");
        await AdvanceUntil(() => Read<ChoiceOverlay?>(pro, "_choice") == null);
    }

    private async Task Shot(string name, Prologue? pro = null)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
        if (pro == null) return;
        float deviceProgress = (float)typeof(Prologue).GetMethod("DeviceProgress", Private)!.Invoke(pro, null)!;
        if (deviceProgress > 0f)
        {
            var device = (Rect2)typeof(Prologue).GetMethod("DeviceViewport", Private)!.Invoke(pro, null)!;
            Check(new Rect2(0, 0, 384, 216).Intersects(device) && device.Size.X < 384f, "opening pulls back to a visible device frame");
            Check(Mathf.Abs(device.Size.X / device.Size.Y - 140f / 192f) < 0.001f, "device keeps its portrait proportions while zooming");
            if (Read<int>(pro, "_phase") == 1) Check(new Rect2(0, 0, 384, 216).Encloses(device), "resting device fits the viewport");
            return;
        }
        var backgrounds = Read<Texture2D[]>(pro, "_backgrounds");
        int index = Read<int>(pro, "_backdrop"), previous = Read<int>(pro, "_previousBackdrop");
        using var reference = backgrounds[index].GetImage();
        using var old = backgrounds[previous].GetImage();
        float k = Read<float>(pro, "_backdropMix");
        k = k * k * (3f - 2f * k);
        int matches = 0, samples = 0;
        foreach (float y in new[] { 0.03f, 0.2f, 0.45f, 0.65f })
            foreach (float x in new[] { 0.015f, 0.985f })
            {
                Color a = image.GetPixel((int)(image.GetWidth() * x), (int)(image.GetHeight() * y));
                Color b = reference.GetPixel((int)(reference.GetWidth() * x), (int)(reference.GetHeight() * y));
                Color c = old.GetPixel((int)(old.GetWidth() * x), (int)(old.GetHeight() * y));
                b *= index == 0 ? 0.65f : 1f;
                c *= previous == 0 ? 0.65f : 1f;
                Color expected = c.Lerp(b, k);
                if (Mathf.Abs(a.R - expected.R) + Mathf.Abs(a.G - expected.G) + Mathf.Abs(a.B - expected.B) < 0.12f) matches++;
                samples++;
            }
        Check(matches >= samples - 1, $"{name} renders the expected full-bleed background ({matches}/{samples})");
    }
}
