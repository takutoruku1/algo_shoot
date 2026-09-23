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
            bool introOnly = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--intro");
            bool movieMode = introOnly || Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--movie");
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
                for (int i = 0; i < (introOnly ? 14 : OpeningFilm.Duration + 2) * 60 && completed == 0; i++) await Frames(1);
                Check(introOnly ? movie.Elapsed >= 13.9 && completed == 0 : completed == 1,
                    introOnly ? "intro reaches the first character scene" : "movie plays to completion");
                await Cleanup();
                return;
            }

            var film = new OpeningFilm();
            prologue.AddChild(film);
            film.SetProcess(false);
            await Frames(3);
            var cuts = (double[])typeof(OpeningFilm).GetField("Cuts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var draftMethod = typeof(OpeningFilm).GetMethod("PhoneDraft", BindingFlags.Static | BindingFlags.NonPublic)!;
            (string Text, bool Caret) Draft(double time) => ((string, bool))draftMethod.Invoke(null, new object[] { time })!;
            foreach (var (time, text) in new (double, string)[]
            {
                // 消す手つきを見せるため 0.20→0.60 秒/字・迷いの静止 0.90→1.60 秒に伸ばした版の節目。
                (0, ""), (1.19, ""), (1.2, "た"), (1.75, "たす"), (2.3, "たすけ"),
                (3.89, "たすけ"), (3.9, "たす"), (4.5, "た"), (5.1, ""), (6.29, ""),
                (6.3, "た"), (6.95, "たす"), (7.75, "たすけ"), (8.5, "たすけて"), (10.49, "たすけて"),
            })
                Check(Draft(time).Text == text, $"draft at {time:0.00}s hesitates, types, erases, and retypes in order");
            Check(Draft(0.1).Caret && !Draft(0.7).Caret && Draft(5.2).Caret && !Draft(5.8).Caret,
                "caret blinks during both empty pauses and returns after editing");
            // 消すほうが打つより遅い＝「消す」がいちばん見せたい動作、という不変条件。
            Check(3.9 - 2.3 >= 1.5 && 4.5 - 3.9 > 1.75 - 1.2,
                "the hesitation holds and erasing is slower than typing");
            Check(cuts[1] - 8.5 >= 2 && cuts[^1] == OpeningFilm.Duration,
                "completed plea remains readable before the next scene");
            var cameraMethod = typeof(OpeningFilm).GetMethod("PhoneCamera", BindingFlags.Static | BindingFlags.NonPublic)!;
            (Vector2 Position, float Angle, float Scale) Camera(float time)
                => ((Vector2, float, float))cameraMethod.Invoke(null, new object[] { time })!;
            Check(Camera(1.2f).Scale > Camera(9.2f).Scale * 1.5f, "intro pulls back from a close-up to the whole phone");
            Check(Camera(8.5f) == Camera(10.49f), "camera settles for the completed plea");
            var phoneArt = Read<Texture2D>(film, "_phoneArt");
            var phoneRegion = Read<Rect2>(film, "_phoneArtRegion");
            Check(phoneArt.ResourcePath.EndsWith("op_phone_v1.png") && phoneRegion.Size.Y > 1400,
                "opening uses the detailed full-resolution phone artwork");
            Check(phoneRegion.Size.X / phoneRegion.Size.Y is > 0.42f and < 0.52f,
                "phone body keeps realistic tall proportions");
            using (var image = phoneArt.GetImage())
            {
                Check(image.GetPixel(0, 0).A == 0 && image.GetPixel(image.GetWidth() / 2, image.GetHeight() / 2).A > 0.98f,
                    "phone has transparent surroundings and an opaque display");
                foreach (var point in new[] { new Vector2(-122, -287), new(122, -287), new(122, 285), new(-122, 285) })
                {
                    Vector2 source = phoneRegion.GetCenter() + point * (phoneRegion.Size.Y / 610);
                    Check(image.GetPixel((int)source.X, (int)source.Y).A > 0.98f,
                        $"phone UI corner {point} stays on the physical display");
                }
            }
            var keyMethod = typeof(OpeningFilm).GetMethod("PhoneKeyAt", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var (time, row, column) in new[] { (1.2f, 1, 1), (1.75f, 0, 3), (2.3f, 0, 2),
                (3.9f, 0, 4), (4.5f, 0, 4), (5.1f, 0, 4), (6.3f, 1, 1), (8.5f, 1, 1) })
            {
                var key = ((int Row, int Column, float Press))keyMethod.Invoke(null, new object[] { time + 0.02f })!;
                Check(key.Row == row && key.Column == column && key.Press > 0.9f,
                    $"kana keyboard follows the correct typing or erase key at {time:0.00}s");
            }
            var idleKey = ((int, int, float))keyMethod.Invoke(null, new object[] { 3.2f })!;
            Check(idleKey.Item3 == 0, "no keyboard press during hesitation");
            Vector2 textPosition = (Vector2)typeof(OpeningFilm).GetField("PhoneTextPosition", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            int textSize = (int)typeof(OpeningFilm).GetField("PhoneTextSize", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
            for (float time = 1.2f; time < 10.5f; time += 0.1f)
            {
                var camera = Camera(time);
                float width = UiKit.TextW(UiKit.Zen, Draft(time).Text, textSize) + 4;
                foreach (var offset in new[] { Vector2.Zero, new Vector2(width, 0), new(width, 40), new(0, 40) })
                {
                    Vector2 local = textPosition + offset;
                    Vector2 screen = camera.Position + local.Rotated(camera.Angle) * camera.Scale;
                    if (!new Rect2(24, 38, 1232, 644).HasPoint(screen))
                        throw new Exception($"draft clipped at {time:0.0}s: {screen}");
                }
            }
            Check(true, "typed text and caret stay inside the cinematic frame throughout the camera move");
            foreach (var (index, duration) in new[] { (1, 3.5), (2, 3.5), (3, 3.5), (4, 5.0),
                (5, 3.1), (6, 4.0), (7, 3.3), (8, 3.6), (9, 4.0), (10, 4.0) })
                Check(Math.Abs(cuts[index + 1] - cuts[index] - duration) < 0.001,
                    $"shot {index} follows its authored duration");
            var daily = Read<Texture2D[]>(film, "_daily");
            Check(daily.Length == 3 && Array.TrueForAll(daily, tex => tex.GetWidth() > 1000), "three dedicated full-resolution daily scenes");
            var portraits = Read<Texture2D[]>(film, "_cutins");
            Check(portraits.Length == 4, "four dedicated opening cutins");
            var cast = Read<JobTuning[]>(film, "_cast");
            Check(cast[0].CharacterId == "akari" && cast[1].CharacterId == "koharu"
                && cast[2].CharacterId == "rei" && cast[3].CharacterId == "mina", "cutin names match the playable characters");
            var routes = Read<Texture2D[,]>(film, "_routes");
            var shots = Read<BulletArt.PlayerVisual[]>(film, "_shots");
            for (int i = 0; i < 4; i++)
            {
                Check(portraits[i].ResourcePath.EndsWith($"op_{cast[i].CharacterId}_cutin_v1.png")
                    && portraits[i].GetWidth() >= 1024 && portraits[i].GetHeight() >= 1536,
                    $"{cast[i].CharacterId} uses its new full-resolution portrait");
                using var portraitImage = portraits[i].GetImage();
                Check(portraitImage.GetPixel(0, 0).A == 0 && portraitImage.GetPixel(portraitImage.GetWidth() - 1, 0).A == 0,
                    $"{cast[i].CharacterId} portrait has transparent surroundings");
                for (int layer = 0; layer < 3; layer++)
                    Check(routes[i, layer].ResourcePath.Contains($"/route/{cast[i].CharacterId}_"),
                        $"{cast[i].CharacterId} action has route layer {layer}");
                Check(shots[i].Texture.ResourcePath.Contains($"/{cast[i].CharacterId}_shot_v1.png"),
                    $"{cast[i].CharacterId} uses its in-game projectile artwork");
            }
            var filmFont = Read<FontFile>(film, "_filmFont");
            var titleFont = Read<FontFile>(film, "_titleFont");
            Check(filmFont.ResourcePath.EndsWith("ShipporiMincho-SemiBold.ttf")
                && titleFont.ResourcePath.EndsWith("CormorantGaramond-Italic.ttf"), "cinematic typography is separate from game UI");
            foreach (var (field, width, font) in new[] { ("DailyLines", 1100f, 30), ("CutinLines", 368f, 30) })
            {
                var quotes = (string[])typeof(OpeningFilm).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                foreach (string quote in quotes)
                    foreach (string line in quote.Split('\n'))
                    {
                        Check(UiKit.TextW(filmFont, line, font) <= width, $"{field}: dialogue fits its caption area");
                        Check(Array.TrueForAll(line.ToCharArray(), c => filmFont.HasChar(c)), $"{field}: all glyphs exist in the cinematic font");
                    }
            }
            Check(UiKit.TextW(filmFont, cast[1].CharacterName, 80) < 280
                && UiKit.TextW(titleFont, "Refrain", 164) < 1000, "name and title fit the cinematic frame");
            var mina = Read<Sprite2D>(film, "_mina");
            using (var cutout = mina.Texture.GetImage())
                Check(cutout.GetPixel(0, 0).A == 0 && cutout.GetPixel(1000, 300).A > 0.95f, "Mina is a real transparent character layer");
            for (int i = 0; i < 3; i++)
            {
                Vector2I size = i == 0 ? new(1280, 720) : i == 1 ? new(960, 540) : new(540, 960);
                DisplayServer.WindowSetSize(size);
                await Frames(10);
                foreach (var (time, name) in new (double, string)[] {
                    (0.85, "phone_wake"), (1.2, "phone_wait"), (3.2, "phone_partial"), (4.05, "phone_erasing"),
                    (5.2, "phone_erased"), (6.8, "phone_retry"), (8.52, "phone_keypress"), (9.2, "phone_plea"),
                    (10.2, "phone_signal"),
                    (cuts[1] + 1.7, "akari"), (cuts[2] + 1.7, "koharu"), (cuts[3] + 1.7, "rei"),
                    (cuts[4] + 2.55, "mina_wind"), (cuts[5] + 1.7, "akari_action"), (cuts[6] + 1.7, "koharu_action"),
                    (cuts[7] + 1.7, "rei_action"), (cuts[8] + 1.7, "mina_action"), (cuts[9] + 2.5, "together"), (cuts[10] + 1.7, "title") })
                {
                    Seek(film, time);
                    await Shot($"{name}_{size.X}x{size.Y}");
                }
                for (int character = 0; character < 4; character++)
                {
                    Seek(film, cuts[5 + character] + 0.8);
                    await Shot($"{cast[character].CharacterId}_portrait_{size.X}x{size.Y}");
                }
            }

            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(10);
            var releases = (float[])typeof(OpeningFilm).GetField("ReleaseTimes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var impacts = (float[])typeof(OpeningFilm).GetField("ImpactTimes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Rect2I[] impactAreas = { new(820, 120, 390, 340), new(100, 80, 520, 470), new(870, 65, 350, 510), new(580, 180, 620, 285) };
            Rect2I[] portraitAreas = { new(550, 65, 630, 490), new(45, 60, 620, 490), new(300, 70, 720, 480), new(500, 60, 700, 490) };
            for (int i = 0; i < 4; i++)
            {
                Check(releases[i] < impacts[i] && impacts[i] + 0.5 < cuts[6 + i] - cuts[5 + i],
                    $"{cast[i].CharacterId} attack has time to land before the cut");
                foreach (var (offset, label) in new[] { (0.2, "closeup"), (0.8, "portrait"), (1.25, "wipe"),
                    ((double)releases[i] + 0.35, "release"), ((double)impacts[i] + 0.23, "break") })
                {
                    Seek(film, cuts[5 + i] + offset);
                    await Shot($"{cast[i].CharacterId}_{label}");
                }
                Seek(film, cuts[5 + i] + 0.2);
                using var portraitEntrance = await Capture();
                Seek(film, cuts[5 + i] + 0.8);
                using var portraitReveal = await Capture();
                Check(Difference(portraitEntrance, portraitReveal, portraitAreas[i]) > 0.025f,
                    $"{cast[i].CharacterId} closeup and reveal visibly animate");
                Seek(film, cuts[5 + i] + impacts[i] - 0.08);
                using var beforeImpact = await Capture();
                Seek(film, cuts[5 + i] + impacts[i] + 0.28);
                using var afterImpact = await Capture();
                Check(Difference(beforeImpact, afterImpact, impactAreas[i]) > 0.015f,
                    $"{cast[i].CharacterId} post shatters visibly");
            }
            Seek(film, cuts[9] + 3.4);
            await Shot("together_launch");
            Seek(film, cuts[4] + 4.65);
            await Shot("mina_dive");

            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(10);
            Seek(film, cuts[4] + 2.55);
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
        await Task.Delay(250);
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GD.Print("[OpeningQA] ALL PASS");
        GetTree().Quit();
    }
}
