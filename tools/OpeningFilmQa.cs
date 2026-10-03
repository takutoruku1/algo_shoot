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

            // ── 字幕の文字送り音（2026-10-03）───────────────────────────────────────
            //   耳で判定できないことは見ない。見るのは「刻みが本編と同じ」「字幕が消える前に打ち切る」
            //   「記号ごとに語尾のピッチが動く」「話者ごとに抑揚の幅が違う」「ナレは無音」。
            const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic;
            var audio = GetNode<Audio>("/root/Audio");
            float cps = (float)typeof(OpeningFilm).GetField("TypeCps", Stat)!.GetRawConstantValue()!;
            int stride = (int)typeof(OpeningFilm).GetField("TypeStride", Stat)!.GetRawConstantValue()!;
            Check(cps == (float)typeof(Hud).GetField("CharsPerSec", Stat)!.GetRawConstantValue()!
                && stride == (int)typeof(Hud).GetField("TypeStride", Stat)!.GetRawConstantValue()!,
                "captions tick at the in-game typewriter's rate and stride");
            var dailyQuotes = (string[])typeof(OpeningFilm).GetField("DailyLines", Stat)!.GetValue(null)!;
            var cutinQuotes = (string[])typeof(OpeningFilm).GetField("CutinLines", Stat)!.GetValue(null)!;
            string heard = (string)typeof(OpeningFilm).GetField("HeardLine", Stat)!.GetRawConstantValue()!;
            string go = (string)typeof(OpeningFilm).GetField("GoLine", Stat)!.GetRawConstantValue()!;
            double Ticks(string text) => text.Length / cps;   // 1本を打ち切るのに要る秒数
            for (int i = 0; i < 3; i++)
                Check(0.2 + Ticks(dailyQuotes[i]) < 3.25, $"daily caption {i} finishes typing before it fades");
            Check(1 + Ticks(heard) + 0.1 + Ticks(go) < 4.2,
                "Mina's two lines type one after the other, not on top of each other");
            for (int i = 0; i < 4; i++)
                Check(0.48 + Ticks(cutinQuotes[i]) < cuts[6 + i] - cuts[5 + i] - 0.3,
                    $"cutin caption {i} finishes typing before the shot cuts");

            var prosody = typeof(Audio).GetMethod("Prosody", Private)!;
            var voiceOf = typeof(Audio).GetMethod("VoiceOf", Private)!;
            object Tone(Hud.LineKind kind) => voiceOf.Invoke(audio, new object[] { kind })!;
            float Say(Hud.LineKind kind, string line, int index)
            {
                object tone = Tone(kind);
                float sum = 0;
                for (int n = 0; n < 128; n++)   // ±0.18半音の乱数揺らぎは平均で消す
                    sum += ((ValueTuple<float, float>)prosody.Invoke(audio, new object[] { line, index, tone })!).Item1;
                return sum / 128f;
            }
            float Range(Hud.LineKind kind, string line)
            {
                float lo = 99f, hi = -99f;
                for (int i = 0; i < line.Length; i++)
                {
                    float v = Say(kind, line, i);
                    lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
                }
                return hi - lo;
            }
            const string ask = "ほんとうに、そうなの？";
            const string tell = "ほんとうに、そうなの。";
            const string trail = "ほんとうに、そうなの…";
            const string cry = "ほんとうに、そうなの！";
            int closing = ask.Length - 2;   // 終止記号の1つ前＝語尾の一打
            Check(Say(Hud.LineKind.Mina, ask, closing) > Say(Hud.LineKind.Mina, ask, 2) + 1.2f,
                "a question lifts its pitch toward the end");
            Check(Say(Hud.LineKind.Mina, tell, closing) < Say(Hud.LineKind.Mina, tell, 2) - 0.5f,
                "a full stop settles the pitch downward");
            Check(Say(Hud.LineKind.Mina, trail, closing) < Say(Hud.LineKind.Mina, tell, closing),
                "a trailing ellipsis sinks further than a full stop");
            Check(Say(Hud.LineKind.Mina, cry, closing) > Say(Hud.LineKind.Mina, tell, closing),
                "an exclamation stays raised instead of falling");
            Check(Range(Hud.LineKind.Mina, tell) > Range(Hud.LineKind.Boy, tell)
                && Range(Hud.LineKind.Boy, tell) > Range(Hud.LineKind.Other, tell)
                && Range(Hud.LineKind.Other, tell) > Range(Hud.LineKind.Post, tell),
                "Mina moves most and the stifled boss voice least — speakers differ in range");
            object narration = Tone(Hud.LineKind.Narration);
            Check(narration.GetType().GetProperty("Stream")!.GetValue(narration) == null,
                "narration keeps no type sound at all");
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
            foreach (string field in new[] { "DailyLines", "CutinLines" })
            {
                var quotes = (string[])typeof(OpeningFilm).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                foreach (string quote in quotes)
                {
                    var lines = UiKit.WrapLines(DialogueBox.Body.Font, quote, DialogueBox.Body.Size,
                        DialogueBox.WrapWidth(DialogueBox.FullScreen));
                    Check(lines.Count <= 2, $"{field}: dialogue fits the shared two-line panel");
                    foreach (string line in lines)
                    {
                        Check(UiKit.TextW(DialogueBox.Body.Font, line, DialogueBox.Body.Size) <= DialogueBox.WrapWidth(DialogueBox.FullScreen),
                            $"{field}: dialogue fits its caption area");
                        Check(Array.TrueForAll(line.ToCharArray(), c => DialogueBox.Body.Font.HasChar(c)),
                            $"{field}: all glyphs exist in the dialogue font");
                    }
                }
            }
            Check(UiKit.TextW(filmFont, cast[1].CharacterName, 80) < 280
                && UiKit.TextW(titleFont, "Refrain", 164) < 1000, "name and title fit the cinematic frame");
            var mina = Read<Sprite2D>(film, "_mina");
            using (var cutout = mina.Texture.GetImage())
            using (var blinkArt = Read<ShaderMaterial>(film, "_wind").GetShaderParameter("blink_texture").As<Texture2D>().GetImage())
            {
                Check(cutout.GetSize() == new Vector2I(1536, 1024) && blinkArt.GetSize() == cutout.GetSize(),
                    "Mina's new portrait and blink frame preserve the wind shader registration");
                Check(cutout.GetPixel(0, 0).A == 0 && cutout.GetPixel(1000, 300).A > 0.95f
                    && blinkArt.GetPixel(0, 0).A == 0 && blinkArt.GetPixel(1000, 300).A > 0.95f,
                    "Mina and her blink are real transparent character layers");
            }
            var recall = typeof(OpeningFilm).GetMethod("RecallIndex", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var (time, index) in new[] { (0.2f, 0), (0.6f, 1), (1.0f, 2) })
                Check((int)recall.Invoke(null, new object[] { time })! == index,
                    "closing recalls each person's separate everyday scene");
            var space = Read<OpeningBackdrop>(film, "_space");
            Check(Read<Texture2D>(space, "_depth").ResourcePath.EndsWith("timeline_depth_v2.png"),
                "Mina and the closing belong to the prologue's SNS space");
            foreach (string line in "まだ届いていない声が、\n待っている。".Split('\n'))
                Check(UiKit.TextW(filmFont, line, 36) <= 510, "invitation caption leaves Mina's face and hand clear");
            Seek(film, cuts[9] + 2.5);
            Check(mina.Visible && Read<ShaderMaterial>(film, "_wind").GetShaderParameter("opacity").AsDouble() > 0.99,
                "closing shows the detailed reaching portrait, not a four-fighter lineup");
            Seek(film, cuts[10] + 0.5);
            Check(!mina.Visible, "invitation leaves cleanly before the title");
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
                    (cuts[7] + 1.7, "rei_action"), (cuts[8] + 1.7, "mina_action"),
                    (cuts[9] + 0.2, "recall_akari"), (cuts[9] + 0.6, "recall_koharu"), (cuts[9] + 1.0, "recall_rei"),
                    (cuts[9] + 1.45, "invitation_enter"), (cuts[9] + 2.5, "invitation"), (cuts[10] + 1.7, "title") })
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
            await Shot("invitation_hold");
            Seek(film, cuts[9] + 1.65);
            using var invitationEnter = await Capture();
            Seek(film, cuts[9] + 2.5);
            using var invitationHold = await Capture();
            Check(Difference(invitationEnter, invitationHold, new Rect2I(620, 90, 560, 500)) > 0.01f,
                "invitation camera and portrait visibly settle");
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
            Check(Difference(moved, blink, new Rect2I(750, 360, 150, 250)) < 0.0001f,
                "blinking does not swap Mina's body or outfit");
            await Shot("mina_blink");
            await SkipButtonStates(film, cuts[8] + 1.9, cuts[6] + 1.7);
            film.QueueFree();
            await Frames(3);

            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = true });
            await Frames(2);
            int count = 0;
            film = new OpeningFilm { Completed = () => count++ };
            prologue.AddChild(film);
            await Seconds(1.4);
            Check(count == 0 && !Read<bool>(film, "_inputArmed"), "held dialogue advance cannot skip the opening");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = false });
            await Frames(3);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = true });
            await Seconds(0.2);
            Check(count == 0, "short press cannot accidentally skip");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = false });
            await Frames(3);
            Check(Read<double>(film, "_skipHold") == 0 && !Read<bool>(film, "_leaving"),
                "releasing a partial hold cancels the progress without skipping");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = true });
            await Seconds(1.3);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = false });
            Check(count == 1 && !IsInstanceValid(film), "held skip fades out and completes once");

            film = new OpeningFilm { Completed = () => count++ };
            prologue.AddChild(film);
            await Seconds(0.75);
            ClickSkip(film);
            Check(!Read<bool>(film, "_leaving"), "hidden skip button is not clickable");
            await Seconds(0.45);
            ClickSkip(film);
            await Seconds(0.6);
            Check(count == 2, "skip button handles a pointer click");

            film = new OpeningFilm { Completed = () => count++ };
            prologue.AddChild(film);
            ulong deadline = Time.GetTicksMsec() + (ulong)((OpeningFilm.Duration + 2) * 1000);
            while (Time.GetTicksMsec() < deadline && IsInstanceValid(film)) await Frames(1);
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
        var bounds = (Rect2)typeof(OpeningFilm).GetField("SkipRect", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        typeof(Pad).GetField("_mousePos", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, bounds.GetCenter());
        typeof(Pad).GetField("_mL", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        typeof(Pad).GetField("_mLPrev", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
        film._Process(1.0 / 60);
        typeof(Pad).GetField("_mL", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
    }

    private async Task SkipButtonStates(OpeningFilm film, double minaTime, double koharuTime)
    {
        const BindingFlags stat = BindingFlags.Static | BindingFlags.NonPublic;
        var bounds = (Rect2)typeof(OpeningFilm).GetField("SkipRect", stat)!.GetValue(null)!;
        Check(DialogueBox.FullScreen.Encloses(bounds) && bounds.End.Y < DialogueBox.TextPosition(DialogueBox.FullScreen).Y,
            "skip button stays inside the shared dialogue header and clear of captions");
        typeof(Pad).GetField("_mousePos", stat)!.SetValue(null, bounds.GetCenter());
        Seek(film, minaTime);
        film._Process(0.2);
        Check(Read<float>(film, "_skipHover") == 1, "pointer hover highlights the visible skip button");
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Frames(8);
            typeof(OpeningFilm).GetField("_skipHover", Private)!.SetValue(film, 0f);
            Seek(film, minaTime);
            await Shot($"skip_idle_{size.X}x{size.Y}");
            using var idle = await Capture();
            typeof(OpeningFilm).GetField("_skipHover", Private)!.SetValue(film, 1f);
            Seek(film, minaTime);
            await Shot($"skip_hover_{size.X}x{size.Y}");
            using var hover = await Capture();
            if (size.X == 1280)
                Check(Difference(idle, hover, (Rect2I)bounds) > 0.01f,
                    "hover is visibly distinct without moving or resizing the button");
            typeof(OpeningFilm).GetField("_skipHold", Private)!.SetValue(film, 0.325);
            Seek(film, minaTime);
            await Shot($"skip_hold_{size.X}x{size.Y}");
            using var held = await Capture();
            if (size.X == 1280)
                Check(Difference(hover, held, (Rect2I)bounds) > 0.02f,
                    "hold progress and amber artwork are visibly distinct");
            typeof(OpeningFilm).GetField("_skipHold", Private)!.SetValue(film, 0d);
            Seek(film, koharuTime);
            await Shot($"skip_right_caption_{size.X}x{size.Y}");
        }
        var icons = (CanvasTexture?[])typeof(DialogToolbar).GetField("Icons", stat)!.GetValue(null)!;
        var active = (CanvasTexture?[])typeof(DialogToolbar).GetField("ActiveIcons", stat)!.GetValue(null)!;
        Check(icons[DialogToolbar.Skip]!.DiffuseTexture.ResourcePath.EndsWith("dialog_skip_v1.png")
            && active[DialogToolbar.Skip]!.DiffuseTexture.ResourcePath.EndsWith("dialog_skip_on_v1.png"),
            "opening uses the existing illustrated skip artwork and its active variant");
        typeof(OpeningFilm).GetField("_skipHover", Private)!.SetValue(film, 0f);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(8);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Seconds(double seconds)
    {
        ulong deadline = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (Time.GetTicksMsec() < deadline) await Frames(1);
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
