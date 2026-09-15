using Godot;
using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;

public partial class EpilogueQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";
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
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/ending_v2/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            GetNode<GameManager>("/root/Game").MsgCharsPerSec = 300;
            await Frames(2);
            await VerifyFinal();
            for (int choice = 0; choice < 3; choice++) await VerifyEpilogue(choice);
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
            await Frames(5);
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

    private async Task VerifyFinal()
    {
        var game = GetNode<GameManager>("/root/Game");
        for (int route = 0; route < 3; route++)
        {
            game.FirstScattered = "おかえり";
            game.LastSentWord = "まだ送っていない";
            var final = GD.Load<PackedScene>("res://Final.tscn").Instantiate<Final>();
            GetTree().Root.AddChild(final);
            GetTree().CurrentScene = final;
            await Frames(12);
            if (route == 0)
            {
                using var intro = await Shot("final_wait");
                MatchArt(intro, Read<Texture2D>(final, "_waiting"), FinalRect(final));
            }
            await AdvanceUntil(() => Read<ChoiceOverlay?>(final, "_choice") != null);
            await Frames(60);
            var overlay = Read<ChoiceOverlay>(final, "_choice");
            if (route == 0) { using var capture = await Shot("final_choice"); }
            if (route == 1)
            {
                await Select(overlay, 0);
                await AdvanceUntil(() => Read<ChoiceOverlay?>(final, "_choice") == null);
                Check(game.LastSentWord == "まだ送っていない", "F4 refusal does not send a word");
                await AdvanceUntil(() => Read<ChoiceOverlay?>(final, "_choice") != null);
                Check(Read<bool>(final, "_refused"), "F4 refusal offers the first word again");
            }
            if (route == 2)
            {
                await Frames(1250);
                Check(Read<ChoiceOverlay?>(final, "_choice") == null, "F4 silence sends after twenty seconds");
            }
            else await AdvanceUntil(() => Read<ChoiceOverlay?>(final, "_choice") == null);
            Check(game.LastSentWord == "おかえり", $"F4 route {route} sends the original word");
            Check(!Read<bool>(final, "_cueResolveDone"), "Mina does not smile before receiving the word");
            await AdvanceUntil(() => Read<bool>(final, "_cueResolveDone"));
            await Frames(155);
            Check(Read<double>(final, "_resolveT") >= 2.4, "received CG finishes its dissolve");
            if (route == 0)
            {
                using var resolved = await Shot("final_received");
                MatchArt(resolved, Read<Texture2D>(final, "_received"), FinalRect(final));
            }
            await AdvanceUntil(() => Read<int>(final, "_phase") == 2);
            Write(final, "_t", 2.6);
            await Frames(2);
            if (route == 0)
            {
                using var returning = await Shot("final_rooftop");
                MatchArt(returning, Read<Texture2D>(final, "_rooftop"), new Rect2(0, 0, 1280, 720));
            }
            await AdvanceUntil(() => GetTree().CurrentScene.SceneFilePath == "res://Epilogue.tscn");
            Check(!IsInstanceValid(final), "Final reaches the illustrated epilogue");
            GetTree().CurrentScene.QueueFree();
            await Frames(3);
        }
    }

    private static Rect2 FinalRect(Final final)
    {
        float zoom = 1 + 0.045f * (1 - Mathf.Exp(-(float)Read<double>(final, "_cameraT") / 18));
        Vector2 size = new Vector2(1280, 720) * zoom;
        return new Rect2((new Vector2(1280, 720) - size) / 2, size);
    }

    private async Task VerifyEpilogue(int choice)
    {
        var game = GetNode<GameManager>("/root/Game");
        game.LastSentWord = "おかえり";
        var ep = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
        GetTree().Root.AddChild(ep);
        GetTree().CurrentScene = ep;
        await Frames(30);
        foreach (string field in new[] { "_skyDawn", "_rest", "_goodbye", "_together" })
            Check(Read<Texture2D>(ep, field).ResourcePath.StartsWith("res://char/bg2/ending/"), $"{field} loads ending art");

        if (choice == 0)
        {
            using var gaze = await Shot("gaze");
            await AdvanceUntil(() => Read<int>(ep, "_phase") == 1);
            await VerifyFilm(Read<EndingFilm>(ep, "_film"));
            Check(Read<int>(ep, "_phase") == 2 && Read<EndingFilm?>(ep, "_film") == null, "film completes once and enters credits");
        }
        else
        {
            KeyDown(Key.Z, true);
            typeof(Epilogue).GetMethod("StartFilm", Private)!.Invoke(ep, null);
            var film = Read<EndingFilm>(ep, "_film");
            await Frames(90);
            Check(!film.Finished && Read<int>(ep, "_phase") == 1, "held dialogue input cannot skip the film on entry");
            KeyDown(Key.Z, false);
            await Frames(5);
            if (choice == 1) KeyDown(Key.Z, true);
            else ClickSkip(film);
            await Frames(120);
            Check(Read<int>(ep, "_phase") == 2, "film skip enters credits without also skipping credits");
            KeyDown(Key.Z, false);
            await Frames(3);
        }
        Write(ep, "_t", 11.0);
        await Frames(2);
        if (choice == 0) { using var credits = await Shot("credits"); }
        await AdvanceUntil(() => Read<int>(ep, "_phase") == 3);
        await AdvanceUntil(() => Read<ChoiceOverlay?>(ep, "_e6Choice") != null);
        await Frames(60);
        var overlay = Read<ChoiceOverlay>(ep, "_e6Choice");
        float previousBottom = 500;
        for (int i = 0; i < 3; i++)
        {
            var row = (Rect2)typeof(ChoiceOverlay).GetMethod("RowRect", Private)!.Invoke(overlay, new object[] { i })!;
            Check(row.Position.Y >= previousBottom && row.End.Y <= 720, "ending choices stay below faces with separate hit targets");
            previousBottom = row.End.Y;
        }
        Check(Read<double>(ep, "_goodbyeT") == 0, "last close-up waits until after the choice");
        if (choice == 0) { using var capture = await Shot("choice"); }
        await Select(overlay, choice);
        await AdvanceUntil(() => Read<ChoiceOverlay?>(ep, "_e6Choice") == null);
        string expected = choice == 0 ? "また来る" : choice == 1 ? "ありがとう" : "おかえり";
        Check(game.HasChoiceAt("e6") && game.LastSentWord == expected, $"E6 choice {choice} records the correct last word");
        await AdvanceUntil(() => Read<int>(ep, "_line") == Read<IList>(ep, "_end").Count - 2);
        Check(Read<double>(ep, "_goodbyeT") < 1.2, "last response starts a continuous close-up dissolve");
        await Frames(90);
        using (var goodbye = await Shot($"goodbye_{choice}"))
            MatchArt(goodbye, Read<Texture2D>(ep, "_goodbye"), new Rect2(0, 0, 1280, 720));
        double goodbyeTime = Read<double>(ep, "_goodbyeT");
        await AdvanceUntil(() => Read<int>(ep, "_line") == Read<IList>(ep, "_end").Count - 1);
        Check(Read<double>(ep, "_goodbyeT") > goodbyeTime, "line changes do not restart the close-up");
        if (choice == 0)
        {
            await Frames(80);
            using var end = await Shot("end");
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(15);
            using var small = await Shot("end_small");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
        await AdvanceUntil(() => GetTree().CurrentScene.SceneFilePath == "res://TitleMenu.tscn");
        Check(!IsInstanceValid(ep), $"E6 choice {choice} returns to title");
        GetTree().CurrentScene.QueueFree();
        await Frames(3);
    }

    private async Task VerifyFilm(EndingFilm film)
    {
        Check(film.GetChildCount() == 0, "film has no walking sprites or articulated body parts");
        film.SetProcess(false);
        var art = Read<Texture2D[]>(film, "_art");
        var cuts = (double[])typeof(EndingFilm).GetField("Cuts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var lines = (string[])typeof(EndingFilm).GetField("Lines", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        for (int shot = 0; shot < art.Length; shot++)
        {
            foreach (string line in lines[shot].Split('\n'))
                Check(UiKit.Zen.GetStringSize(line, fontSize: 30).X <= 1120, $"film caption {shot} fits");
            double time = (cuts[shot] + cuts[shot + 1]) / 2;
            typeof(EndingFilm).GetProperty(nameof(EndingFilm.Elapsed))!.SetValue(film, time);
            film.QueueRedraw();
            var rect = (Rect2)typeof(EndingFilm).GetMethod("ArtRect", Private)!.Invoke(film, new object[] { shot, 0.5f })!;
            Check(rect.Position.X <= 0 && rect.Position.Y <= 0 && rect.End.X >= 1280 && rect.End.Y >= 720, $"film cut {shot} covers the frame");
            foreach (int width in new[] { 1280, 960 })
            {
                DisplayServer.WindowSetSize(new Vector2I(width, width * 9 / 16));
                await Frames(5);
                using var image = await Shot($"film_{shot:D2}_{width}");
                MatchArt(image, art[shot], rect);
            }
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        typeof(EndingFilm).GetProperty(nameof(EndingFilm.Elapsed))!.SetValue(film, 5.2);
        film.QueueRedraw();
        await Frames(3);
        using var before = await Shot("camera_before");
        film.SetProcess(true);
        await Frames(90);
        using var after = await Shot("camera_after");
        int changed = 0;
        for (int y = 100; y < 510; y += 5)
            for (int x = 60; x < 1220; x += 5)
                if (Difference(before.GetPixel(x, y), after.GetPixel(x, y)) > 0.15f) changed++;
        Check(changed > 100, "illustrated camera movement is visible");
        typeof(EndingFilm).GetProperty(nameof(EndingFilm.Elapsed))!.SetValue(film, 49.0);
        await Frames(260);
        Check(!IsInstanceValid(film), "film completes automatically");
    }

    private static void ClickSkip(EndingFilm film)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(Pad).GetField("_mousePos", flags)!.SetValue(null, new Vector2(1180, 686));
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, true);
        typeof(Pad).GetField("_mLPrev", flags)!.SetValue(null, false);
        film._Process(1.0 / 60);
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, false);
    }

    private async Task Select(ChoiceOverlay overlay, int selected)
    {
        for (int i = 0; i < 4 && overlay.Selected != selected; i++)
        {
            Input.ParseInputEvent(new InputEventAction { Action = "ui_up", Pressed = true });
            await Frames(2);
            Input.ParseInputEvent(new InputEventAction { Action = "ui_up", Pressed = false });
            await Frames(2);
        }
        Check(overlay.Selected == selected, "choice selection responds");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void KeyDown(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });

    private async Task AdvanceUntil(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            KeyDown(Key.Z, true);
            await Frames(16);
            KeyDown(Key.Z, false);
            await Frames(2);
        }
        Check(condition(), "dialogue advances to target");
    }

    private async Task<Image> Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
        return image;
    }

    private static float Difference(Color a, Color b)
        => Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B);

    private static void MatchArt(Image rendered, Texture2D texture, Rect2 rect)
    {
        using var reference = texture.GetImage();
        int count = 0, matches = 0;
        foreach (float y in new[] { 0.1f, 0.2f, 0.4f, 0.6f })
            foreach (float x in new[] { 0.06f, 0.2f, 0.4f, 0.46f, 0.53f, 0.6f, 0.8f, 0.94f })
            {
                int px = (int)(rendered.GetWidth() * x), py = (int)(rendered.GetHeight() * y);
                Vector2 position = new((px + 0.5f) * 1280 / rendered.GetWidth(), (py + 0.5f) * 720 / rendered.GetHeight());
                Vector2 uv = (position - rect.Position) / rect.Size;
                Color actual = rendered.GetPixel(px, py);
                Color expected = SampleLinear(reference, uv);
                if (Difference(actual, expected) < 0.18f) matches++;
                count++;
            }
        Check(matches >= count * 0.9f, $"art fills the viewport with no character deformation ({matches}/{count})");
    }

    private static Color SampleLinear(Image image, Vector2 uv)
    {
        Vector2 p = uv * image.GetSize() - Vector2.One * 0.5f;
        int x = Mathf.FloorToInt(p.X), y = Mathf.FloorToInt(p.Y);
        Color Pixel(int dx, int dy) => image.GetPixel(Mathf.Clamp(x + dx, 0, image.GetWidth() - 1),
            Mathf.Clamp(y + dy, 0, image.GetHeight() - 1));
        return Pixel(0, 0).Lerp(Pixel(1, 0), p.X - x).Lerp(Pixel(0, 1).Lerp(Pixel(1, 1), p.X - x), p.Y - y);
    }
}
