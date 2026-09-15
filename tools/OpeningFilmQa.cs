using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class OpeningFilmQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[OpeningQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            bool movieMode = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--movie");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(movieMode ? new Vector2I(960, 540) : new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/opening/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            await Frames(1);
            var prologue = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
            GetTree().Root.AddChild(prologue);
            prologue.SetProcess(false);
            GetTree().CurrentScene = prologue;
            if (movieMode)
            {
                int completed = 0;
                var movie = new OpeningFilm { Completed = () => completed++ };
                prologue.AddChild(movie);
                for (int i = 0; i < 2100 && completed == 0; i++) await Frames(1);
                Check(completed == 1, "movie plays to completion");
                await Cleanup();
                return;
            }

            var film = new OpeningFilm();
            prologue.AddChild(film);
            film.SetProcess(false);
            await Frames(3);
            var daily = Read<Texture2D[]>(film, "_daily");
            Check(daily.Length == 3 && Array.TrueForAll(daily, tex => tex.GetWidth() > 1000), "three dedicated full-resolution daily scenes");
            var portraits = Read<Texture2D[]>(film, "_cutins");
            Check(portraits.Length == 4 && portraits[2].ResourcePath.EndsWith("cutin_rei_gawa_a.png")
                && portraits[3].ResourcePath.EndsWith("op_mina_v1.png"), "four cutins keep Rei's avatar and Mina's normal appearance");
            var cast = Read<JobTuning[]>(film, "_cast");
            Check(cast[0].CharacterId == "akari" && cast[1].CharacterId == "koharu"
                && cast[2].CharacterId == "rei" && cast[3].CharacterId == "mina", "cutin names match the playable characters");
            foreach (var (field, width, font) in new[] { ("DailyLines", 1100f, 29), ("CutinLines", 490f, 27) })
            {
                var quotes = (string[])typeof(OpeningFilm).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                foreach (string quote in quotes)
                    foreach (string line in quote.Split('\n'))
                        Check(UiKit.TextW(UiKit.ZenBold, line, font) <= width, $"{field}: dialogue fits its caption area");
            }
            var mina = Read<Sprite2D>(film, "_mina");
            using (var cutout = mina.Texture.GetImage())
                Check(cutout.GetPixel(0, 0).A == 0 && cutout.GetPixel(1000, 300).A > 0.95f, "Mina is a real transparent character layer");
            for (int i = 0; i < 3; i++)
            {
                Vector2I size = i == 0 ? new(1280, 720) : i == 1 ? new(960, 540) : new(540, 960);
                DisplayServer.WindowSetSize(size);
                await Frames(10);
                foreach (var (time, name) in new (double, string)[] {
                    (1.1, "phone"), (5.2, "akari"), (8.7, "koharu"), (12.2, "rei"),
                    (16.55, "mina_wind"), (20.7, "akari_action"), (24.2, "koharu_action"),
                    (27.7, "rei_action"), (31.2, "mina_action"), (35.5, "together"), (38.7, "title") })
                {
                    Seek(film, time);
                    await Shot($"{name}_{size.X}x{size.Y}");
                }
            }

            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(10);
            Seek(film, 16.55);
            var wind = Read<ShaderMaterial>(film, "_wind");
            wind.SetShaderParameter("blink", 0);
            wind.SetShaderParameter("motion_time", 0);
            using var still = await Capture();
            wind.SetShaderParameter("motion_time", 1.3f);
            using var moved = await Capture();
            Check(Difference(still, moved, new Rect2I(220, 320, 170, 190)) > 0.001f, "loose hair visibly moves");
            Check(Difference(still, moved, new Rect2I(803, 233, 88, 65)) < 0.0001f, "wind does not distort the face");
            wind.SetShaderParameter("blink", 1);
            using var blink = await Capture();
            Check(Difference(moved, blink, new Rect2I(803, 233, 88, 65)) > 0.001f, "blink uses the closed-eyes drawing");
            film.QueueFree();
            await Frames(3);

            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = true });
            await Frames(2);
            int count = 0;
            film = new OpeningFilm { Completed = () => count++ };
            prologue.AddChild(film);
            await Frames(85);
            Check(count == 0 && !Read<bool>(film, "_inputArmed"), "held dialogue advance cannot skip the opening");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = false });
            await Frames(3);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = true });
            await Frames(20);
            Check(count == 0, "short press cannot accidentally skip");
            await Frames(65);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = false });
            Check(count == 1 && !IsInstanceValid(film), "held skip fades out and completes once");

            film = new OpeningFilm { Completed = () => count++ };
            prologue.AddChild(film);
            await Frames(48);
            ClickSkip(film);
            Check(!Read<bool>(film, "_leaving"), "hidden skip button is not clickable");
            await Frames(22);
            ClickSkip(film);
            await Frames(40);
            Check(count == 2, "skip button handles a pointer click");

            film = new OpeningFilm { Completed = () => count++ };
            prologue.AddChild(film);
            for (int i = 0; i < (OpeningFilm.Duration + 2) * 60 && IsInstanceValid(film); i++) await Frames(1);
            Check(count == 3, $"{OpeningFilm.Duration}-second playback completes automatically once");
            await Cleanup();
        }
        catch (Exception ex)
        {
            GD.PushError($"[OpeningQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static void Seek(OpeningFilm film, double time)
    {
        typeof(OpeningFilm).GetProperty(nameof(OpeningFilm.Elapsed))!.SetValue(film, time);
        typeof(OpeningFilm).GetMethod("UpdateMina", Private)!.Invoke(film, null);
        film.QueueRedraw();
        Read<Node2D>(film, "_overlay").QueueRedraw();
    }

    private static void ClickSkip(OpeningFilm film)
    {
        typeof(Pad).GetField("_mousePos", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, new Vector2(1180, 675));
        typeof(Pad).GetField("_mL", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        typeof(Pad).GetField("_mLPrev", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
        film._Process(1.0 / 60);
        typeof(Pad).GetField("_mL", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<Image> Capture()
    {
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return GetViewport().GetTexture().GetImage();
    }

    private async Task Shot(string name)
    {
        using var img = await Capture();
        Check(img.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
        int varied = 0;
        Color previous = img.GetPixel(img.GetWidth() / 2, img.GetHeight() / 2);
        for (int y = img.GetHeight() * 4 / 10; y < img.GetHeight() * 6 / 10; y += 5)
            for (int x = img.GetWidth() / 5; x < img.GetWidth() * 4 / 5; x += 7)
            {
                Color c = img.GetPixel(x, y);
                if (Mathf.Abs(c.R - previous.R) + Mathf.Abs(c.G - previous.G) + Mathf.Abs(c.B - previous.B) > 0.03f) varied++;
                previous = c;
            }
        Check(varied > 50, $"{name} canvas is nonblank");
    }

    private static float Difference(Image a, Image b, Rect2I rect)
    {
        float difference = 0;
        for (int y = rect.Position.Y; y < rect.End.Y; y += 2)
            for (int x = rect.Position.X; x < rect.End.X; x += 2)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                difference += Mathf.Abs(p.R - q.R) + Mathf.Abs(p.G - q.G) + Mathf.Abs(p.B - q.B);
            }
        return difference / (rect.Size.X * rect.Size.Y / 4f);
    }

    private async Task Cleanup()
    {
        GetTree().CurrentScene.QueueFree();
        Audio.Instance?.StopMusic(0);
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
            if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GD.Print("[OpeningQA] ALL PASS");
        GetTree().Quit();
    }
}
