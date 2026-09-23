using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class ChoiceOverlayQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private Node2D _root = null!;
    private string _out = "";
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static Rect2 Row(ChoiceOverlay choice, int i) => (Rect2)typeof(ChoiceOverlay).GetMethod("RowRect", Private)!.Invoke(choice, new object[] { i })!;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[ChoiceQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated user data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/choices/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            GetNode<GameManager>("/root/Game").AutoSaveEnabled = false;
            await Frames(2);
            _root = new Node2D();
            GetTree().Root.AddChild(_root);
            GetTree().CurrentScene = _root;
            await Frames(3);
            await Layouts();
            await Inputs();
            await Screens();
            _root.QueueFree();
            _root = null!;
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[ChoiceQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GetTree().Paused = false;
            GD.PushError($"[ChoiceQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private ChoiceOverlay Open(string[] labels, bool board = false, bool cinematic = false, int selected = 0)
    {
        var choice = ChoiceOverlay.Show(_root, labels, selected, board, cinematic);
        choice.SetProcess(false);
        return choice;
    }

    private void ValidateLayout(ChoiceOverlay choice, bool board, bool cinematic)
    {
        var labels = Read<string[]>(choice, "_disp");
        var font = Read<FontFile>(choice, "_font");
        int size = Read<int>(choice, "_fontSize");
        for (int i = 0; i < labels.Length; i++)
        {
            Rect2 row = Row(choice, i);
            Check(UiKit.TextW(font, labels[i], size) <= row.Size.X - 144 && font.GetHeight(size) <= row.Size.Y - 6,
                $"complete label fits at {size}px: {labels[i]}");
            Check(row.Position.X >= (board ? Field.DLeft : 0) && row.End.X <= UiKit.DesignW, "row stays inside its play area");
            Check(row.Position.Y >= (cinematic ? 500 : 130) && row.End.Y <= (cinematic ? 708 : 458), "row avoids portraits and dialogue");
            Check(i == 0 || Row(choice, i - 1).End.Y < row.Position.Y, "rows never overlap");
            Check(Row(choice, 0).Size == row.Size, "short and long choices have equal hitboxes");
        }
        Set(choice, "_t", 0.7);
        float lastAppear = (float)typeof(ChoiceOverlay).GetMethod("RowAppear", Private)!.Invoke(choice, new object[] { labels.Length - 1 })!;
        Check(lastAppear == 1, "all text is visible before the confirmation gate opens");
    }

    private async Task Layouts()
    {
        foreach (Type type in new[] { typeof(Prologue), typeof(StageAkari), typeof(StageKoharu), typeof(StageRei), typeof(Epilogue), typeof(GameManager) })
            foreach (var field in type.GetFields(Static))
            {
                if (field.FieldType != typeof(string[]) || !field.Name.EndsWith("Choices")) continue;
                var labels = (string[])field.GetValue(null)!;
                bool cinematic = type == typeof(Epilogue);
                bool board = type != typeof(Prologue) && !cinematic;
                var choice = Open(labels, board, cinematic);
                ValidateLayout(choice, board, cinematic);
                Check(labels[^1].StartsWith('（') == Read<string[]>(choice, "_disp")[^1].StartsWith('（'), "actions do not gain duplicate quotation marks");
                choice.QueueFree();
                await Frames(1);
            }
        for (int count = 1; count <= 4; count++)
            foreach (bool cinematic in new[] { false, true })
            {
                var labels = new string[count];
                Array.Fill(labels, "ここにいる");
                var choice = Open(labels, cinematic: cinematic);
                ValidateLayout(choice, false, cinematic);
                choice.QueueFree();
                await Frames(1);
            }
    }

    private async Task Inputs()
    {
        typeof(Pad).GetField("_usingMouse", Static)!.SetValue(null, false);
        var choice = Open(new[] { "ここにいる", "待っている", "（送らない）" });
        Input.ActionPress("ui_accept");
        choice._Process(0.8);
        Check(!Read<bool>(choice, "_deciding"), "held dialogue-advance input cannot accept a new choice");
        Input.ActionRelease("ui_accept");
        choice._Process(0.01);
        Input.ActionPress("ui_up");
        choice._Process(0.01);
        Check(choice.Selected == 2, "keyboard navigation wraps");
        Input.ActionRelease("ui_up");
        choice._Process(0.01);
        Input.ActionPress("ui_down");
        choice._Process(0.01);
        Check(choice.Selected == 0, "keyboard navigation returns to first choice");
        Input.ActionRelease("ui_down");
        choice._Process(0.01);
        Click(choice, new Vector2(12, 12));
        Check(!Read<bool>(choice, "_deciding"), "outside click cannot confirm");
        var row = Row(choice, 1);
        Click(choice, row.GetCenter());
        Check(choice.Selected == 1 && Read<bool>(choice, "_deciding"), "pointer chooses the visible row");
        choice._Process(0.2);
        Check(!choice.Decided, "decision waits for its visual transition");
        Input.ActionPress("ui_down");
        choice._Process(0.1);
        Input.ActionRelease("ui_down");
        Check(choice.Selected == 1 && Row(choice, 1) == row, "selection and hitboxes lock during confirmation");
        choice._Process(0.3);
        Check(choice.Decided, "decision completes after half a second");
        choice.QueueFree();
        await Frames(2);

        choice = Open(new[] { "ここにいる", "（送らない）" });
        Click(choice, Row(choice, 0).GetCenter());
        Check(!Read<bool>(choice, "_deciding"), "entrance gate also blocks early pointer clicks");
        typeof(Pad).GetField("_usingMouse", Static)!.SetValue(null, false);
        choice._Process(0.8);
        var devices = Input.GetConnectedJoypads();
        if (devices.Count > 0)
        {
            Input.ParseInputEvent(new InputEventJoypadButton { Device = devices[0], ButtonIndex = JoyButton.A, Pressed = true });
            Input.FlushBufferedEvents();
            choice._Process(0.01);
            Check(Read<bool>(choice, "_deciding"), "controller A confirms the selected response");
            Input.ParseInputEvent(new InputEventJoypadButton { Device = devices[0], ButtonIndex = JoyButton.A, Pressed = false });
            Input.FlushBufferedEvents();
        }
        else GD.Print("[ChoiceQA] SKIP controller input: no connected controller");
        choice.QueueFree();
        await Frames(2);

        choice = Open(new[] { "ここにいる", "（送らない）" });
        choice._Process(0.8);
        Set(choice, "_silenceT", 19.9);
        Input.ActionPress("ui_down");
        choice._Process(0.2);
        Input.ActionRelease("ui_down");
        Check(Read<double>(choice, "_silenceT") == 0 && !Read<bool>(choice, "_deciding"), "interaction resets the silence timer");
        choice._Process(0.01);
        Set(choice, "_silenceT", 19.95);
        choice._Process(0.1);
        Check(choice.Selected == 1 && Read<bool>(choice, "_quiet"), "silence still selects the final response quietly");
        choice.QueueFree();
        await Frames(2);

        choice = Open(new[] { "ここにいる" });
        choice.SetProcess(true);
        await Frames(3);
        GetTree().Paused = true;
        double time = Read<double>(choice, "_t");
        await Frames(12);
        Check(Read<double>(choice, "_t") == time, "pause freezes choice animation and timeout");
        GetTree().Paused = false;
        choice.QueueFree();
        await Frames(2);
    }

    private async Task Screens()
    {
        _root.QueueFree();
        await Frames(3);
        _root = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(_root);
        GetTree().CurrentScene = _root;
        _root.SetProcess(false);
        Set(_root, "_phase", 3);
        Set(_root, "_backdrop", 1);
        Set(_root, "_previousBackdrop", 1);
        Set(_root, "_backdropMix", 1f);
        Set(_root, "_choiceShade", 1f);
        Set(_root, "_line", Read<int>(_root, "_p2ChoiceLine"));
        ((Prologue)_root)._Process(0.01);
        var choice = Read<ChoiceOverlay>(_root, "_choice");
        choice.SetProcess(false);
        choice._Process(0.8);
        Click(choice, Row(choice, 0).GetCenter());
        choice._Process(0.6);
        ((Prologue)_root)._Process(0.01);
        Check(GetNode<GameManager>("/root/Game").ChosenAt("p2") == "おはよう", "prologue records the original selected story text");
        Set(_root, "_line", Read<int>(_root, "_p3ChoiceLine"));
        ((Prologue)_root)._Process(0.01);
        choice = Read<ChoiceOverlay>(_root, "_choice");
        choice.SetProcess(false);
        Set(choice, "_t", 2.0);
        _root.QueueRedraw();
        await Shot("prologue_long_choice");
        choice.QueueFree();
        _root.QueueFree();
        await Frames(3);

        GetNode<GameManager>("/root/Game").SelectedEntry = GameManager.StageEntry.Start;
        GameManager.MinaNamed = true;
        _root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(_root);
        GetTree().CurrentScene = _root;
        _root.GetNode("StageAkari").SetProcess(false);
        var hud = _root.GetNode<Hud>("Hud");
        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "……返事を、ひとつ。あなたなら、どんな言葉にしますか。", "res://char/mina_face.png");
        hud.RevealDialogNow();
        choice = ChoiceOverlay.Show(hud, (string[])typeof(StageAkari).GetField("S12Choices", Static)!.GetValue(null)!, 1, onBoard: true);
        choice.SetProcess(false);
        Set(choice, "_t", 2.0);
        Set(choice, "_hintA", 1f);
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Frames(4);
            await Shot($"stage_four_choices_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(3);
        Click(choice, Row(choice, 0).GetCenter());
        choice._Process(0.14);
        await Shot("confirmed_response");
        _root.QueueFree();
        await Frames(3);

        _root = GD.Load<PackedScene>("res://Final.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(_root);
        GetTree().CurrentScene = _root;
        _root.SetProcess(false);
        Set(_root, "_phase", 1);
        Set(_root, "_line", Read<int>(_root, "_choiceLine"));
        ((Final)_root)._Process(0.01);
        choice = Read<ChoiceOverlay>(_root, "_choice");
        choice.SetProcess(false);
        Set(choice, "_t", 2.0);
        _root.QueueRedraw();
        await Shot("final_two_choices");
        Click(choice, Row(choice, 0).GetCenter());
        choice._Process(0.6);
        ((Final)_root)._Process(0.01);
        Check(Read<bool>(_root, "_refused"), "final refusal keeps its original branch");
        Set(_root, "_line", Read<int>(_root, "_choiceLine"));
        ((Final)_root)._Process(0.01);
        choice = Read<ChoiceOverlay>(_root, "_choice");
        choice.SetProcess(false);
        choice._Process(0.8);
        Check(Read<string[]>(choice, "_choices").Length == 1, "final story can still offer one response");
        await Shot("final_single_choice");
        Click(choice, Row(choice, 0).GetCenter());
        choice._Process(0.6);
        ((Final)_root)._Process(0.01);
        Check(GetNode<GameManager>("/root/Game").HasChoiceAt("f4"), "single response completes the final branch");
        _root.QueueFree();
        await Frames(3);

        _root = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(_root);
        GetTree().CurrentScene = _root;
        _root.SetProcess(false);
        int phase = (int)typeof(Epilogue).GetField("PhEnd", Static)!.GetValue(null)!;
        Set(_root, "_phase", phase);
        Set(_root, "_line", Read<int>(_root, "_e6ChoiceLine"));
        typeof(Epilogue).GetMethod("ShowE6Choice", Private)!.Invoke(_root, null);
        choice = Read<ChoiceOverlay>(_root, "_e6Choice");
        choice.SetProcess(false);
        Set(choice, "_t", 2.0);
        _root.QueueRedraw();
        await Shot("ending_choices");
        Click(choice, Row(choice, 1).GetCenter());
        choice._Process(0.6);
        ((Epilogue)_root)._Process(0.01);
        Check(GetNode<GameManager>("/root/Game").ChosenAt("e6") == "ありがとう", "ending records and resumes after the chosen response");
    }

    private static void Click(ChoiceOverlay choice, Vector2 position)
    {
        typeof(Pad).GetField("_mousePos", Static)!.SetValue(null, position);
        typeof(Pad).GetField("_usingMouse", Static)!.SetValue(null, true);
        typeof(Pad).GetField("_mL", Static)!.SetValue(null, true);
        typeof(Pad).GetField("_mLPrev", Static)!.SetValue(null, false);
        choice._Process(1.0 / 60);
        typeof(Pad).GetField("_mL", Static)!.SetValue(null, false);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        Check(frame.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
