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
    private static void Write(object obj, string field, object value)
        => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
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

            if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--backdrop"))
            {
                await CheckBackdrop();
                await Finish();
                return;
            }

            if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--erase"))
            {
                var erase = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
                GetTree().Root.AddChild(erase);
                GetTree().CurrentScene = erase;
                var talk = Read<System.Collections.IList>(erase, "_talk");
                var intro = (System.Collections.IList)typeof(Prologue).GetMethod("P4Intro", Private)!.Invoke(erase, null)!;
                talk.Clear();
                int line = 0;
                foreach (var entry in intro)
                {
                    if ((string)entry.GetType().GetField("Text")!.GetValue(entry)! == "fx:erase") line = talk.Count;
                    talk.Add(entry);
                }
                Write(erase, "_line", line);
                Write(erase, "_phase", 3);
                Write(erase, "_p2ChoiceLine", -1);
                Write(erase, "_timelineLine", 0);
                Write(erase, "_unsentLine", line);
                Write(erase, "_backdrop", 3);
                Write(erase, "_previousBackdrop", 3);
                GameManager.MinaNamed = true;
                await CheckErasePacing(erase, true);
                await Frames(90);
                erase.QueueFree();
                await Finish();
                return;
            }

            CheckMinaArtwork();
            for (int route = 0; route < 2; route++)
            {
                GetNode<GameManager>("/root/Game").ResetPersistent();
                var pro = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
                GetTree().Root.AddChild(pro);
                GetTree().CurrentScene = pro;
                await Frames(60);
                Check(pro.TextureFilter == CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                    "high-resolution conversation art uses the shooting scene's downsampling filter");
                var artwork = Read<OpeningBackdrop>(pro, "_backdropArt");
                var layers = Read<Texture2D[]>(artwork, "_cards");
                Check(layers.Length == 3, "inactive, timeline and unsent card layers loaded");
                foreach (var texture in layers)
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
                await AdvanceUntil(() => CurrentDialogue(pro).Text.Contains("「ご主人様」"));
                Check(CurrentDialogue(pro).Face == "res://char/v3/mina_conversation_v1.png",
                    $"route {route} addresses the player with the shooting conversation portrait");
                if (route == 0)
                {
                    await Frames(60);
                    await Shot("mina_master", pro);
                    DisplayServer.WindowSetSize(new Vector2I(960, 540));
                    await Frames(15);
                    await Shot("mina_master_small", pro);
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
                await AdvanceUntil(() => CurrentDialogue(pro).Face == "res://char/v3/mina_conversation_worried_v1.png");
                Check(CurrentDialogue(pro).Text.Contains("投稿しようとして、送れなかった言葉が聞こえます。"),
                    $"route {route} switches to the matching concerned expression for the unheard voice");
                if (route == 0)
                {
                    await Frames(60);
                    await Shot("mina_worried", pro);
                    DisplayServer.WindowSetSize(new Vector2I(960, 540));
                    await Frames(15);
                    await Shot("mina_worried_small", pro);
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                    await Frames(15);
                }
                await AdvanceUntil(() => CurrentDialogue(pro).Text == "……何か、迷っていたようです。");
                Check(Read<int>(pro, "_backdrop") == 2
                    && CurrentDialogue(pro).Face == "res://char/v3/mina_conversation_worried_v1.png",
                    $"route {route} explains the hesitation before revealing the erased draft");
                await AdvanceUntil(() => Read<int>(pro, "_backdrop") == 3);
                Check(Read<float>(pro, "_backdropMix") < 1f, "erased draft starts its own crossfade");
                await CheckErasePacing(pro, route == 0);
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
                Check(Read<Texture2D>(firstPost, "_iconTex").ResourcePath == CompanionDialogue.AccountIcon(GameManager.FirstStageId),
                    "first stage post uses the current SNS account portrait");
                if (route == 0) await Shot("first_stage_post");
                await AdvanceUntil(() => Read<int>(pro, "_phase") == 6);
                Check(GameManager.MinaNamed && pro.GetNodeOrNull<OpeningFilm>("OpeningFilm") != null,
                    $"route {route} starts the animated opening after the first stage post");
                await AdvanceUntil(() => !IsInstanceValid(pro));
                Check(GetTree().CurrentScene.SceneFilePath == "res://Hub.tscn", $"route {route} reaches the hub");
                var hub = (Hub)GetTree().CurrentScene;
                Check(Read<object>(hub, "_mode").ToString() == "Home", "opening continues into the phone home before SNS");
                var entries = Read<System.Collections.IList>(hub, "_entries");
                var selected = entries[Read<int>(hub, "_sel")]!;
                Check((string)selected.GetType().GetField("Id")!.GetValue(selected)! == GameManager.FirstStageId,
                    "SNS keeps the first stage post selected");
                GetTree().CurrentScene.QueueFree();
                await Frames(2);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                await Frames(15);
            }
            await Finish();
        }
        catch (Exception ex)
        {
            GD.PushError($"[PrologueQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static (string Text, string Face) CurrentDialogue(Prologue pro)
    {
        var entry = Read<System.Collections.IList>(pro, "_talk")[Read<int>(pro, "_line")]!;
        return ((string)entry.GetType().GetField("Text")!.GetValue(entry)!,
            (string)entry.GetType().GetField("Face")!.GetValue(entry)!);
    }

    private static void CheckMinaArtwork()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        string neutralPath = (string)typeof(Prologue).GetField("FMina", flags)!.GetRawConstantValue()!;
        string worriedPath = (string)typeof(Prologue).GetField("FMinaWorried", flags)!.GetRawConstantValue()!;
        Check(neutralPath == "res://char/v3/mina_conversation_v1.png", "prologue reuses the shooting conversation portrait");
        Check(worriedPath == "res://char/v3/mina_conversation_worried_v1.png", "prologue uses the matching worried variant");
        using var neutral = GD.Load<Texture2D>(neutralPath).GetImage();
        using var worried = GD.Load<Texture2D>(worriedPath).GetImage();
        Check(neutral.GetSize() == new Vector2I(1024, 1536) && worried.GetSize() == neutral.GetSize(),
            "Mina's expression variants share the same canvas and framing");
        Check(neutral.HasMipmaps() && worried.HasMipmaps(), "both expressions retain smooth reduced-size rendering");
        int overlap = 0, union = 0;
        for (int y = 0; y < neutral.GetHeight(); y += 8)
            for (int x = 0; x < neutral.GetWidth(); x += 8)
            {
                bool a = neutral.GetPixel(x, y).A > 0.5f, b = worried.GetPixel(x, y).A > 0.5f;
                if (a || b) union++;
                if (a && b) overlap++;
            }
        Check((float)overlap / union > 0.98f, "expression switches keep Mina's silhouette and proportions aligned");
        Check(neutral.GetPixel(0, 0).A == 0 && worried.GetPixel(0, 0).A == 0
            && neutral.GetPixel(500, 600).A > 0.95f && worried.GetPixel(500, 600).A > 0.95f,
            "both dialogue portraits have transparent surroundings and an opaque face");
    }

    private async Task CheckErasePacing(Prologue pro, bool screenshots)
    {
        await WaitUntil(() => Read<PostToast?>(pro, "_toast") != null);
        int line = Read<int>(pro, "_line");
        var beats = ((string Text, double Hold, bool Send)[])typeof(Prologue)
            .GetField("EraseBeats", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var doneAt = new double[beats.Length];
        var leftAt = new double[beats.Length];
        Array.Fill(doneAt, -1);
        Array.Fill(leftAt, -1);
        double start = Read<double>(pro, "_t");
        int lastStep = -1, cries = 0, erased = 0;
        bool sent = false;
        Check(Read<int>(pro, "_fxStep") == 0 && Read<string>(Read<PostToast>(pro, "_toast"), "_body") == "",
            "draft opens empty before any typing");
        for (int frame = 0; frame < 1800 && Read<int>(pro, "_line") == line; frame++)
        {
            int step = Read<int>(pro, "_fxStep");
            double now = Read<double>(pro, "_t");
            if (step != lastStep)
            {
                if (lastStep >= 0 && lastStep < beats.Length) leftAt[lastStep] = now;
                lastStep = step;
            }
            var toast = Read<PostToast>(pro, "_toast");
            if (step < beats.Length && Read<int>(pro, "_fxBegun") == step && toast.Done && doneAt[step] < 0)
            {
                doneAt[step] = now;
                string body = Read<string>(toast, "_body");
                Check(body == beats[step].Text && Read<bool>(toast, "_sending") == beats[step].Send,
                    $"draft beat {step} reaches its intended text and send state");
                if (body == "たすけて") cries++;
                if (body == "" && step > 0) erased++;
                if (screenshots)
                {
                    if (step == 0) { await Frames(24); await Shot("unsent_wait", pro); }
                    else if (body == "たすけて") await Shot($"unsent_cry_{cries}", pro);
                    else if (body == "") await Shot($"unsent_erased_{erased}", pro);
                    else if (beats[step].Send) await Shot("unsent_sent", pro);
                }
            }
            if (!sent && Read<double>(toast, "_sendGlow") > 0)
            {
                sent = true;
                Check(step == beats.Length - 1 && Read<string>(toast, "_body") == "元気です。",
                    "only the final reassuring post is sent");
            }
            await Frames(1);
        }
        Check(Read<int>(pro, "_line") == line + 1 && Read<PostToast?>(pro, "_toast") == null,
            "draft sequence cleans up and returns to Mina's dialogue once");
        Check(cries == 3 && erased == 3 && sent, "three unsent pleas still lead to the original reassuring post");
        for (int i = 0; i < beats.Length; i++)
            Check(doneAt[i] >= 0 && leftAt[i] - doneAt[i] >= (i == 0 ? 1.3 : beats[i].Hold - 0.04),
                $"beat {i} pauses after editing completes ({leftAt[i] - doneAt[i]:0.00}s)");
        double elapsed = Read<double>(pro, "_t") - start;
        Check(elapsed is > 17 and < 22, $"draft hesitation remains paced at {elapsed:0.00}s");
    }

    private async Task Finish()
    {
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
        CheckEdges(image, name);
    }

    private static void CheckEdges(Image image, string name)
    {
        foreach (float y in new[] { 0.03f, 0.2f, 0.45f, 0.65f })
            foreach (float x in new[] { 0.001f, 0.999f })
            {
                Color pixel = image.GetPixel((int)(image.GetWidth() * x), (int)(image.GetHeight() * y));
                if (pixel.R + pixel.G + pixel.B < 0.025f)
                    throw new Exception($"{name}: uncovered edge at {x}, {y}");
            }
        Check(true, $"{name}: illustrated background covers the moving edges");
    }

    private async Task CheckBackdrop()
    {
        var preview = new OpeningBackdropPreview();
        AddChild(preview);
        foreach (var texture in Read<Texture2D[]>(preview.Artwork, "_cards"))
        {
            using var source = texture.GetImage();
            Check(source.DetectAlpha() != Image.AlphaMode.None, "card artwork has real transparency");
            Check(source.GetPixel(source.GetWidth() / 2, source.GetHeight() / 2).A == 0,
                "card layers leave the dialogue character area transparent");
        }
        for (int phase = 0; phase < 4; phase++)
        {
            preview.Phase = phase;
            preview.Time = 0;
            preview.QueueRedraw();
            await Shot($"layered_{phase}_start");
            using var first = GetViewport().GetTexture().GetImage();
            CheckEdges(first, $"layered {phase} start");
            preview.Time = 3.2f;
            preview.QueueRedraw();
            await Shot($"layered_{phase}_moving");
            using var second = GetViewport().GetTexture().GetImage();
            CheckEdges(second, $"layered {phase} moving");
            int changed = 0;
            for (int y = first.GetHeight() / 5; y < first.GetHeight() * 3 / 4; y += 4)
                for (int x = first.GetWidth() / 10; x < first.GetWidth() / 3; x += 4)
                {
                    Color a = first.GetPixel(x, y), b = second.GetPixel(x, y);
                    if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) > 0.06f) changed++;
                }
            Check(changed > 50, $"phase {phase} produces visible parallax ({changed} changed samples)");
        }
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        preview.Phase = 2;
        await Frames(3);
        string frames = $"{_out}/motion";
        DirAccess.MakeDirRecursiveAbsolute(frames);
        for (int i = 0; i < 120; i++)
        {
            preview.Time = i / 15f;
            preview.QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng($"{frames}/frame_{i:000}.png") != Error.Ok)
                throw new Exception($"Could not save motion frame {i}");
        }
        Check(true, "120 motion-preview frames captured at 960x540");
        preview.QueueFree();
    }
}

public partial class OpeningBackdropPreview : Node2D
{
    public OpeningBackdrop Artwork { get; } = new();
    public int Phase;
    public float Time;
    public override void _Draw() => Artwork.Draw(this, new Rect2(0, 0, 384, 216), Phase, Phase, 1f, Time);
}
