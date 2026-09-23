using Godot;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

public partial class TitleMenuQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private string _out = "";
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static Rect2 Row(int i) => (Rect2)typeof(TitleMenu).GetMethod("MenuRowRect", Static)!.Invoke(null, new object[] { i })!;

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[TitleQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/title");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            await Frames(2);
            var game = GetNode<GameManager>("/root/Game");
            for (int i = 0; i <= GameManager.SlotCount; i++) Check(!game.SlotExists(i), "fresh test save directory");
            var title = await OpenTitle();
            Check(!Read<bool>(title, "_hasSave") && Read<int>(title, "_sel") == 0, "new player starts on new game");
            var illustration = title.GetNode<Sprite2D>("TitleIllustration");
            Check(illustration.Texture.GetWidth() >= 1280 && illustration.Texture.ResourcePath.EndsWith("title_mina_v2.png"), "new key visual is loaded at full resolution");
            Check(illustration.Material is ShaderMaterial && ((ShaderMaterial)illustration.Material).Shader.ResourcePath == "res://shaders/title_kv.gdshader",
                "key visual uses the localized wind shader");
            var titleFont = Read<FontFile>(title, "_titleFont");
            var menuFont = Read<FontFile>(title, "_menuFont");
            Check(UiKit.TextW(titleFont, "Refrain", 184) + 88 < 620, "wordmark stays clear of Mina");
            await Shot("title_entrance");
            await Frames(100);
            var items = (Array)typeof(TitleMenu).GetField("Items", Static)!.GetValue(null)!;
            var labels = new string[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                string text = (string)((ITuple)items.GetValue(i)!)[1]!;
                labels[i] = text;
                Check(UiKit.TextW(menuFont, text, 27) + 74 < Row(i).Size.X, $"menu label and selection arrow fit: {text}");
                Check(Row(i).End.Y <= 674 && (i == 0 || Row(i - 1).End.Y < Row(i).Position.Y), "rows do not overlap footer or each other");
            }
            Check(string.Join("|", labels) == (GameManager.TutorialEnabled
                ? "はじめから|つづきから|チュートリアル|設定|クレジット" : "はじめから|つづきから|設定|クレジット"), "title preserves all current menu entries");
            int settingsIndex = Array.IndexOf(labels, "設定");
            int creditsIndex = Array.IndexOf(labels, "クレジット");
            foreach (string field in new[] { "BootTalk", "IdleTalk" })
                foreach (string line in (string[])typeof(TitleMenu).GetField(field, Static)!.GetValue(null)!)
                    Check(UiKit.Paginate(menuFont, line, 19, 542, 2).Count == 1, "Mina remark fits two lines");

            foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(size);
                await Frames(10);
                Set(title, "_talk", "……お迷いですか。急がなくて結構ですよ。");
                Set(title, "_talkT", 6.0);
                title.QueueRedraw();
                await Shot($"title_{size.X}x{size.Y}");
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(10);
            await VerifyIllustrationMotion(title, illustration);
            await KeyPress(Key.Down);
            Check(Read<int>(title, "_sel") == 1, "keyboard changes menu selection");
            await KeyPress(Key.Z);
            Check(!Read<bool>(title, "_picking") && Read<double>(title, "_toastT") > 0, "continue without save shows feedback");
            await Shot("no_save");
            await KeyPress(Key.Up);
            await KeyPress(Key.Up);
            Check(Read<int>(title, "_sel") == items.Length - 1, "navigation wraps at the top");
            typeof(Pad).GetField("_usingMouse", Static)!.SetValue(null, true);
            typeof(Pad).GetField("_mousePos", Static)!.SetValue(null, Row(settingsIndex).GetCenter());
            title._Process(1.0 / 60);
            Check(Read<int>(title, "_sel") == settingsIndex, "pointer hover uses the visible menu rows");
            typeof(Pad).GetField("_usingMouse", Static)!.SetValue(null, false);
            Click(title, Row(items.Length).GetCenter());
            Check(!Read<bool>(title, "_dived"), "removed rows are not clickable");

            game.SaveToSlot(1);
            title = await OpenTitle();
            Check(Read<int>(title, "_sel") == 1 && Read<bool>(title, "_hasSave"), "returning player starts on continue");
            await KeyPress(Key.Z);
            Check(Read<bool>(title, "_picking") && Read<int>(title, "_pick") == 1, "continue selects the first occupied slot");
            await Shot("save_picker");
            await KeyPress(Key.Up);
            await KeyPress(Key.Z);
            Check(Read<bool>(title, "_picking") && !Read<bool>(title, "_dived"), "empty slot cannot load");
            var close = (Rect2)typeof(TitleMenu).GetMethod("SlotPickerCloseRect", Static)!.Invoke(null, new object[] { 4 })!;
            Click(title, close.GetCenter());
            Check(!Read<bool>(title, "_picking"), "pointer close dismisses save picker");
            await Frames(3);
            await KeyPress(Key.Z);
            await KeyPress(Key.X);
            Check(!Read<bool>(title, "_picking"), "keyboard cancel dismisses save picker");
            Click(title, Row(1).GetCenter());
            await Frames(3);
            var slot = (Rect2)typeof(TitleMenu).GetMethod("SlotPickerRowRect", Static)!.Invoke(null, new object[] { 1, 4 })!;
            Click(title, slot.GetCenter());
            await Frames(12);
            Check(GetTree().CurrentScene.SceneFilePath == "res://Hub.tscn", "pointer loads occupied slot into the hub");

            foreach (var (index, scene) in new[] { (settingsIndex, "res://Settings.tscn"), (creditsIndex, "res://Credits.tscn"), (0, "res://Prologue.tscn") })
            {
                title = await OpenTitle();
                Click(title, Row(index).GetCenter());
                await Frames(12);
                Check(GetTree().CurrentScene.SceneFilePath == scene, $"menu routes to {scene}");
                if (index != 0)
                {
                    await KeyPress(Key.Escape);
                    await Frames(12);
                    Check(GetTree().CurrentScene is TitleMenu && GetTree().CurrentScene.HasNode("TitleIllustration"), "back returns to the redesigned title");
                }
            }
            GetTree().CurrentScene.QueueFree();
            title = null!;
            illustration = null!;
            titleFont = menuFont = null!;
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[TitleQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[TitleQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task VerifyIllustrationMotion(TitleMenu title, Sprite2D illustration)
    {
        title.SetProcess(false);
        Set(title, "_t", 8.0);
        title._Process(0);
        var material = (ShaderMaterial)illustration.Material;
        material.SetShaderParameter("time_sec", 0f);
        using (var still = await Capture())
        {
            foreach (float time in new[] { 1.5f, 3.2f, 5.6f })
            {
                material.SetShaderParameter("time_sec", time);
                using var later = await Capture();
                Check(Difference(still, later, new Rect2I(755, 130, 170, 130)) < 0.0001f, "wind does not deform the face");
                Check(Difference(still, later, new Rect2I(680, 450, 100, 85)) < 0.0001f, "wind does not deform the extended hand");
                Check(Difference(still, later, new Rect2I(890, 350, 90, 60)) < 0.0001f, "wind does not deform the hand on her chest");
                Check(Difference(still, later, new Rect2I(430, 395, 140, 120)) < 0.0001f, "wind does not bend the buildings");
                Check(Difference(still, later, new Rect2I(1120, 330, 140, 150)) > 0.0005f, "upper hair strands move");
                Check(Difference(still, later, new Rect2I(1160, 570, 100, 80)) > 0.0005f, "trailing hair and ribbon move");
                Check(Difference(still, later, new Rect2I(840, 625, 100, 70)) > 0.0001f, "apron hem moves gently");
            }
        }
        using (var before = await Capture())
        {
            title._Process(2);
            using var after = await Capture();
            Check(Difference(before, after, new Rect2I(755, 130, 170, 130)) > 0.0005f, "camera movement is visible on the illustration");
            Check(illustration.Scale.X == illustration.Scale.Y, "camera preserves the illustration aspect ratio");
        }
        var row = Row(0);
        bool covered = true, framed = true;
        typeof(Pad).GetField("_usingMouse", Static)!.SetValue(null, true);
        foreach (Vector2 pointer in new[] { new Vector2(-1000, -1000), new Vector2(3000, 2000) })
        {
            typeof(Pad).GetField("_mousePos", Static)!.SetValue(null, pointer);
            for (int i = 0; i < 300; i++)
            {
                title._Process(0.1);
                Rect2 local = illustration.GetRect();
                var bounds = new Rect2(local.Position * illustration.Scale + illustration.Position, local.Size * illustration.Scale);
                covered &= bounds.Position.X < 0 && bounds.Position.Y < 0
                    && bounds.End.X > UiKit.DesignW * UiKit.Scale && bounds.End.Y > UiKit.DesignH * UiKit.Scale;
                framed &= bounds.Position.Y + bounds.Size.Y * 0.008f > 0;
            }
        }
        Check(covered, "overscan covers every edge through a full camera cycle and extreme pointer positions");
        Check(framed, "top anchoring keeps the headdress inside the frame");
        Check(Read<Vector2>(title, "_parallax").Length() <= new Vector2(3.5f, 2f).Length() + 0.001f, "pointer parallax is bounded");
        Check(Row(0) == row, "animated artwork never moves menu hitboxes");
        typeof(Pad).GetField("_usingMouse", Static)!.SetValue(null, false);
        title.SetProcess(true);
    }

    private async Task<TitleMenu> OpenTitle()
    {
        var current = GetTree().CurrentScene;
        if (current != this) current.QueueFree();
        await Frames(3);
        var title = GD.Load<PackedScene>("res://TitleMenu.tscn").Instantiate<TitleMenu>();
        GetTree().Root.AddChild(title);
        GetTree().CurrentScene = title;
        await Frames(20);
        return title;
    }

    private static void Click(TitleMenu title, Vector2 position)
    {
        // Godotの合成マウスイベントはOSポインターを動かさないため、Padの入力境界にクリックを渡す。
        typeof(Pad).GetField("_mousePos", Static)!.SetValue(null, position);
        typeof(Pad).GetField("_mL", Static)!.SetValue(null, true);
        typeof(Pad).GetField("_mLPrev", Static)!.SetValue(null, false);
        title._Process(1.0 / 60);
        typeof(Pad).GetField("_mL", Static)!.SetValue(null, false);
    }

    private async Task KeyPress(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        await Frames(3);
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
        using var image = await Capture();
        Check(image.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
        Color center = image.GetPixel(image.GetWidth() / 2, image.GetHeight() / 2);
        int varied = 0;
        for (int y = image.GetHeight() * 4 / 10; y < image.GetHeight() * 6 / 10; y += 5)
            for (int x = image.GetWidth() / 5; x < image.GetWidth() * 4 / 5; x += 7)
            {
                Color p = image.GetPixel(x, y);
                if (Mathf.Abs(p.R - center.R) + Mathf.Abs(p.G - center.G) + Mathf.Abs(p.B - center.B) > 0.1f) varied++;
            }
        Check(varied > 50, "canvas is nonblank");
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
}
