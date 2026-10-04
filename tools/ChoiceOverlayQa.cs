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
            NarrativeReplies();
            await RoutePacing();
            await SkyRoutes();
            await RestRoutes();
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
        foreach (Type type in new[] { typeof(Prologue), typeof(StageAkari), typeof(StageKoharu), typeof(StageRei), typeof(Epilogue), typeof(GameManager), typeof(ChoiceEffects) })
            foreach (var field in type.GetFields(Static | BindingFlags.Public))
            {
                if (field.FieldType != typeof(string[]) || !field.Name.EndsWith("Choices")) continue;
                var labels = (string[])field.GetValue(null)!;
                if (type != typeof(GameManager)) Check(labels.Length == 2, $"{type.Name}/{field.Name}: exactly two story choices");
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
        choice._Process(25);
        Check(choice.Selected == 0 && !choice.Decided && !Read<bool>(choice, "_deciding"), "thinking for 25 seconds does not choose silence");
        Input.ActionPress("ui_down");
        choice._Process(0.2);
        Input.ActionRelease("ui_down");
        choice._Process(0.01);
        choice._Process(60);
        Check(choice.Selected == 1 && !choice.Decided && !Read<bool>(choice, "_deciding"), "highlighting silence for a minute does not confirm it");
        Input.ActionPress("ui_accept");
        choice._Process(0.01);
        Input.ActionRelease("ui_accept");
        choice._Process(0.6);
        Check(choice.Decided && choice.Selected == 1, "silence requires an explicit confirmation");
        choice.QueueFree();
        await Frames(2);

        choice = Open(new[] { "ここにいる" });
        choice.SetProcess(true);
        await Frames(3);
        GetTree().Paused = true;
        double time = Read<double>(choice, "_t");
        await Frames(12);
        Check(Read<double>(choice, "_t") == time, "pause freezes choice animation");
        GetTree().Paused = false;
        choice.QueueFree();
        await Frames(2);
    }

    private static (int who, string text, string face)[] Reply(Type type, string method, int selected)
        => ((int, string, string)[])type.GetMethod(method, Static)!.Invoke(null, new object[] { selected })!;

    private void NarrativeReplies()
    {
        var game = GetNode<GameManager>("/root/Game");
        game.SetContamination(0.4f);
        foreach (Type type in new[] { typeof(StageAkari), typeof(StageKoharu), typeof(StageRei) })
            foreach (var field in type.GetFields(Static))
            {
                if (field.FieldType != typeof(string[]) || !field.Name.EndsWith("Choices")) continue;
                var choices = (string[])field.GetValue(null)!;
                for (int sel = 0; sel < choices.Length; sel++)
                {
                    ChoiceEffects.Record(game, "qa_reply", choices, sel, 30);
                    Check(Mathf.IsEqualApprox(game.Contamination, 0.4f), $"{field.Name}/{sel}: no contamination penalty");
                    var reply = Reply(type, field.Name.Replace("Choices", "Reply"), sel);
                    Check(Array.Exists(reply, l => l.who == 1), $"{field.Name}/{sel}: Mina acknowledges the response");
                    Check(game.ChosenAt("qa_reply") == choices[sel] && game.LastSentWord == choices[sel],
                        $"{field.Name}/{sel}: either response is recorded as speech");
                    Check(Array.Exists(reply, l => l.who == 0 && l.text == choices[sel]),
                        $"{field.Name}/{sel}: player speech matches the selected text");
                }
            }
        for (int sel = 0; sel < 2; sel++)
        {
            var reply = Reply(typeof(StageRei), "S35cReply", sel);
            Check(!Array.Exists(reply, l => l.text.Contains("散りました") || l.text.Contains("気づいて、いただけ")),
                $"S35c/{sel}: no hidden right-answer reproach");
        }
        float? rei = null;
        foreach (string word in new[] { "誰も見てないなんて、思ってほしくない", "強がらなくていいよ。今のレイの話が聞きたい" })
        {
            game.RecordChoice("s3_5c", word, Array.Empty<string>(), 1);
            float value = Fury.InitialFor(game, "rei");
            Check(rei == null || Mathf.IsEqualApprox(rei.Value, value), "supportive Rei replies receive equal treatment");
            rei = value;
        }
        var koharuChoices = (string[])typeof(StageKoharu).GetField("S21Choices", Static)!.GetValue(null)!;
        foreach (string word in koharuChoices)
        {
            game.RecordChoice("s2_1", word, Array.Empty<string>(), 1);
            var lines = ((int, string, string)[])typeof(StageKoharu).GetMethod("CameoIntroFor", Static)!.Invoke(null, new object[] { game })!;
            Check(lines[0].Item2.Contains(word), $"Koharu remembers the new response: {word}");
        }
    }

    private async Task RoutePacing()
    {
        var game = GetNode<GameManager>("/root/Game");
        foreach (Type type in new[] { typeof(StageAkari), typeof(StageKoharu), typeof(StageRei) })
            Check(Array.FindAll(type.GetFields(Static), f => f.FieldType == typeof(string[]) && f.Name.EndsWith("Choices")).Length == 2,
                $"{type.Name}: exactly two route choices remain");
        foreach (var route in new[] { ("Akari", 6, "s1_2"), ("Koharu", 6, "s2_2"), ("Rei", 2, "s3_2") })
            foreach (Job job in new[] { Job.Tank, Job.Melee })
            {
                _root.QueueFree();
                await Frames(3);
                game.ResetPersistent();
                game.AutoSaveEnabled = false;
                game.SelectedJob = job;
                game.SelectedEntry = GameManager.StageEntry.Start;
                game.Difficulty = GameManager.Diff.Normal;
                _root = GD.Load<PackedScene>($"res://{route.Item1}.tscn").Instantiate<Node2D>();
                GetTree().Root.AddChild(_root);
                GetTree().CurrentScene = _root;
                var stage = _root.GetNode<Node>("Stage" + route.Item1);
                stage.SetProcess(false);
                _root.GetNode("World").ProcessMode = ProcessModeEnum.Disabled;
                var hud = _root.GetNode<Hud>("Hud");
                hud.HoldBubble = false;
                hud.HideBubble();
                Set(stage, "_step", route.Item2);
                Set(stage, "_stepStarted", route.Item1 == "Koharu");
                if (route.Item1 == "Koharu")
                {
                    Set(stage, "_cameoIntroDone", true);
                    // The preceding schedule used the same in-place dialogue cursor.
                    Set(stage, "_cStarted", true);
                    Set(stage, "_cLine", 99);
                }
                long score = game.Score;
                int ticks = 0;
                for (; ticks < 50 && Read<int>(stage, "_step") == route.Item2; ticks++)
                {
                    hud.RevealDialogNow();
                    Set(stage, "_zHeld", false);
                    Input.ActionPress("ui_accept");
                    stage._Process(1.5);
                    Input.ActionRelease("ui_accept");
                    Check(GetTree().GetFirstNodeInGroup("choice_overlay") == null
                        && hud.GetNodeOrNull<ChoiceOverlay>("ChoiceOverlay") == null,
                        $"{route.Item1}/{job}: ordinary dialogue never opens a choice");
                    await Frames(1);
                }
                Check(Read<int>(stage, "_step") == route.Item2 + 1 && !hud.HoldBubble && !Hud.BubblePaused,
                    $"{route.Item1}/{job}: dialogue proceeds to the next battle and releases pause");
                Check(!game.HasChoiceAt(route.Item3), $"{route.Item3}: no invented response is recorded");
                if (route.Item1 == "Koharu")
                {
                    Check(game.Score - score == (job == Job.Tank ? 500 : 0), "penlight reward is paid once, only on Mina's route");
                    Check(job != Job.Tank || ticks >= 4, "all three penlight lines play after resetting the old cursor");
                    stage._Process(0.1);
                    Check(game.Score - score == (job == Job.Tank ? 500 : 0), "next battle does not pay the reward again");
                }
            }
    }

    private async Task SkyRoutes()
    {
        var game = GetNode<GameManager>("/root/Game");
        game.SelectedJob = Job.Tank;
        game.SelectedEntry = GameManager.StageEntry.Start;
        GameManager.MinaNamed = true;
        foreach (var route in new[] { ("Akari", "StageAkari", 14), ("Koharu", "StageKoharu", 15), ("Rei", "StageRei", 13) })
            for (int sel = 0; sel < 2; sel++)
            {
                _root.QueueFree();
                await Frames(2);
                _root = GD.Load<PackedScene>($"res://{route.Item1}.tscn").Instantiate<Node2D>();
                GetTree().Root.AddChild(_root);
                GetTree().CurrentScene = _root;
                var stage = _root.GetNode<Node>(route.Item2);
                stage.SetProcess(false);
                _root.GetNode("World").ProcessMode = ProcessModeEnum.Disabled;
                var hud = _root.GetNode<Hud>("Hud");
                Set(stage, "_clearBannerShown", true);
                Set(stage, "_clearPhase", 2);
                Set(stage, "_step", route.Item3);
                Set(stage, "_stepStarted", false);
                string method = route.Item1 == "Akari" ? "ClearAfterFor" : "ClearFor";
                var lines = stage.GetType().GetMethod(method, Static)!.Invoke(null, new object[] { game })!;
                Set(stage, route.Item1 == "Akari" ? "_clearAfter" : "_clearLines", lines);
                var tick = stage.GetType().GetMethod("Step_Clear", Private)!;
                bool selected = false;
                for (int i = 0; i < 120 && Read<int>(stage, "_step") == route.Item3; i++)
                {
                    hud.RevealDialogNow();
                    Set(stage, "_lineHold", 2.0);
                    Set(stage, "_zEdge", true);
                    tick.Invoke(stage, new object[] { 1.5 });
                    if (!selected && hud.GetNodeOrNull<ChoiceOverlay>("ChoiceOverlay") is { } choice)
                    {
                        selected = true;
                        choice.SetProcess(false);
                        choice._Process(0.8);
                        choice._Process(25);
                        Check(!choice.Decided && Hud.BubblePaused, $"{route.Item1}: sky question waits for the player");
                        if (sel == 0)
                        {
                            foreach (Vector2I size in new[] { new Vector2I(1280, 720), new(540, 960) })
                            {
                                DisplayServer.WindowSetSize(size);
                                await Frames(3);
                                await Shot($"sky_{route.Item1}_{size.X}x{size.Y}");
                            }
                            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                        }
                        Click(choice, Row(choice, sel).GetCenter());
                        choice._Process(0.6);
                    }
                    await Frames(1);
                }
                string id = route.Item1 == "Akari" ? "s1_sky" : route.Item1 == "Koharu" ? "s2_sky" : "s3_sky";
                var skyChoices = (string[])stage.GetType().GetField("SkyChoices", Static)!.GetValue(null)!;
                Check(selected && game.HasChoiceAt(id) && game.ChosenAt(id) == skyChoices[sel],
                    $"{route.Item1}/{sel}: weather answer is recorded accurately");
                Check(Read<int>(stage, "_step") == route.Item3 + 1 && !hud.HoldBubble,
                    $"{route.Item1}/{sel}: clear dialogue finishes without a stuck choice");
            }
    }

    private async Task RestRoutes()
    {
        var stage = _root.GetNode<StageRei>("StageRei");
        var hud = _root.GetNode<Hud>("Hud");
        var game = GetNode<GameManager>("/root/Game");
        var world = _root.GetNode<Node2D>("World");
        var gameMode = game.ProcessMode;
        var begin = stage.GetType().GetMethod("BeginMemoryFollowUp", Private)!;
        for (int sel = 0; sel < 2; sel++)
        {
            Set(stage, "_midStoryShown", false);
            world.ProcessMode = ProcessModeEnum.Inherit;
            int resumed = 0;
            begin.Invoke(stage, new object[] { (Action)(() => resumed++) });
            Check(Read<int>(stage, "_step") == 15 && Hud.BubblePaused && hud.SuppressCallouts
                && world.ProcessMode == ProcessModeEnum.Disabled && game.ProcessMode == ProcessModeEnum.Disabled,
                $"S37/{sel}: film hand-off starts dialogue with no combat gap");
            Set(stage, "_step", 17);
            Set(stage, "_stepStarted", false);
            stage.GetType().GetMethod("SetQuietVeil", Private)!.Invoke(stage, new object[] { true });
            float contamination = game.Contamination;
            stage.GetType().GetMethod("ApplyS37Choice", Private)!.Invoke(stage, new object[] { sel });
            Check(Mathf.IsEqualApprox(game.Contamination, contamination), "S37 concern does not increase contamination");
            int resting = 0;
            for (int i = 0; i < 60 && Read<int>(stage, "_step") == 17; i++)
            {
                hud.RevealDialogNow();
                Set(stage, "_lineHold", 2.0);
                Set(stage, "_zEdge", true);
                double before = Read<double>(stage, "_s37RestRemaining");
                stage.GetType().GetMethod("Step_MidChoiceAfter", Private)!.Invoke(stage, new object[] { 0.1 });
                if (Read<double>(stage, "_s37RestRemaining") < before)
                {
                    resting++;
                    Check(Hud.BubblePaused && hud.SuppressCallouts, "combat stays paused throughout Mina's rest");
                }
                await Frames(1);
            }
            Check(Read<int>(stage, "_step") == 12 && !hud.SuppressCallouts && !hud.HoldBubble,
                $"S37/{sel}: returns to battle without leaking the pause");
            Check(resumed == 1 && world.ProcessMode == ProcessModeEnum.Inherit && game.ProcessMode == gameMode,
                $"S37/{sel}: restores processing and resumes battle exactly once");
            Check(sel == 0 ? resting == 0 : resting >= 19, $"S37/{sel}: respects the requested two-second rest");
            var quote = ((int, string, string))typeof(StageMina).GetMethod("S37Quote", Static)!.Invoke(null, new object[] { game })!;
            Check(!quote.Item2.Contains("すみません") && !quote.Item2.Contains("続行と"), "final recall does not blame concern or silence");
        }
        Set(stage, "_midStoryShown", false);
        world.ProcessMode = ProcessModeEnum.Pausable;
        begin.Invoke(stage, new object[] { (Action)(() => throw new Exception("aborted follow-up resumed combat")) });
        stage.QueueFree();
        await Frames(2);
        Check(world.ProcessMode == ProcessModeEnum.Pausable && game.ProcessMode == gameMode
            && !Hud.BubblePaused && !hud.SuppressCallouts, "aborted follow-up restores the original process modes and HUD");
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
        Check(GetNode<GameManager>("/root/Game").ChosenAt("p2") == "やっと会えた。ずっと話したかった", "prologue records the original selected story text");
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
        choice = ChoiceOverlay.Show(hud, (string[])typeof(StageAkari).GetField("S15Choices", Static)!.GetValue(null)!, 1, onBoard: true);
        choice.SetProcess(false);
        Set(choice, "_t", 2.0);
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Frames(4);
            await Shot($"stage_two_choices_{size.X}x{size.Y}");
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
        Check(Read<int>(_root, "_choiceLine") == -1 && Read<ChoiceOverlay?>(_root, "_choice") == null,
            "final refusal is accepted without forcing a second choice");
        Check(GetNode<GameManager>("/root/Game").HasChoiceAt("f4")
            && GetNode<GameManager>("/root/Game").ChosenAt("f4") == "", "final refusal is recorded without an invented message");
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
        Check(GetNode<GameManager>("/root/Game").ChosenAt("e6") == "ミナに会えてよかった。もう、ひとりじゃない", "ending records and resumes after the chosen response");
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
