using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class TutorialCardShot : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static T Tutorial<T>(string name) => (T)typeof(StageTutorial).GetField(name, Static)!.GetValue(null)!;
    private static void Device(int device)
    {
        typeof(Pad).GetField("_usingMouse", Static)!.SetValue(null, device == 2);
        typeof(Pad).GetField("_autoUsingPad", Static)!.SetValue(null, device == 1);
    }

    private string _outDir = "";
    private int _fails;
    private GameManager _game = null!;
    private Node? _scene;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("APPDATA must be isolated under build/qa_story before running this QA.");
            var args = OS.GetCmdlineUserArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--card-out" && i + 1 < args.Length) _outDir = args[++i];
            if (_outDir.Length == 0) throw new InvalidOperationException("--card-out <absolute directory> is required.");
            var mkdir = DirAccess.MakeDirRecursiveAbsolute(_outDir);
            if (mkdir != Error.Ok) throw new InvalidOperationException($"Cannot create screenshot directory: {mkdir}");
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.SelectedJob = Job.Tank;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            await Frames(2);
            await CheckCards();
            await CheckHowTo();
            await LeaveScene();
            await CheckFieldGuidance();
            await LeaveScene();
            await CheckPractice();
        }
        catch (Exception e)
        {
            _fails++;
            GD.PrintErr($"[TutorialQA] EXCEPTION {e}");
        }
        finally
        {
            foreach (var key in new[] { Godot.Key.W, Godot.Key.A, Godot.Key.S, Godot.Key.D, Godot.Key.X }) SetKey(key, false);
            Input.ActionRelease("ui_left");
            Input.ActionRelease("ui_right");
            Device(0);
            Engine.TimeScale = 1;
            GetTree().Paused = false;
            GD.Print(_fails == 0 ? "[TutorialQA] ALL PASS" : $"[TutorialQA] FAILURES {_fails}");
            GetTree().Quit(_fails == 0 ? 0 : 1);
        }
    }

    private async Task CheckCards()
    {
        _game.TrainingSetUpgrade("n_dodge", true);
        _game.TrainingSetUpgrade("n_charge", true);
        _scene = GD.Load<PackedScene>("res://Akari.tscn").Instantiate();
        GetTree().Root.AddChild(_scene);
        GetTree().CurrentScene = _scene;
        var stage = _scene.GetNode("StageAkari");
        stage.SetProcess(false);
        var hud = _scene.GetNode<Hud>("Hud");
        hud.HoldBubble = true;
        var lines = Read<(int who, string text, string face)[]>(stage, "_playerIntro");
        foreach (string block in new[] { "Route", "SkillDodge", "SkillCharge2", "Lock", "Bomb", "SkillCharge" })
        {
            var current = Tutorial<(int who, string text, string face)[]>(block);
            var cues = Tutorial<ControlCard.Topic[]>(block + "Cues");
            Check(current.Length == cues.Length, $"{block}: every line has a cue");
            ControlCard.Topic previous = ControlCard.Topic.None;
            for (int i = 0; i < current.Length; i++)
            {
                bool deferred = block is "Lock" or "Bomb" or "SkillCharge";
                var source = deferred ? current : lines;
                int index = Array.FindIndex(source, line => ReferenceEquals(line.text, current[i].text));
                Check(index >= 0, $"{block} line {i}: appears in its playback block");
                if (deferred) Check(!lines.Any(line => ReferenceEquals(line.text, current[i].text)), $"{block} line {i}: deferred until combat");
                if (index < 0) continue;
                Write(stage, "_introLine", index);
                Call(stage, "ShowLine", source);
                hud.RevealDialogNow();
                await Frames(30);
                var card = hud.GetNodeOrNull<ControlCard>("ControlCard");
                Check(card != null && Read<ControlCard.Topic>(card, "_topic") == cues[i], $"{block} line {i}: {cues[i]}");
                if (i == 0 || cues[i] != previous)
                    await Capture($"card_{block}_{i:00}_{cues[i]}");
                previous = cues[i];
            }
        }
        var route = Tutorial<(int who, string text, string face)[]>("Route");
        StageTutorial.SyncCard(hud, route, 0);
        await Frames(30);
        var active = hud.GetNode<ControlCard>("ControlCard");
        foreach (var topic in Enum.GetValues<ControlCard.Topic>().Where(t => t != ControlCard.Topic.None))
        {
            var spec = (System.Runtime.CompilerServices.ITuple)Call(active, "Spec", topic)!;
            foreach (var row in (Array)spec[3]!)
            {
                string note = (string)row!.GetType().GetProperty("note")!.GetValue(row)!;
                var wrapped = UiKit.WrapLines(UiKit.Zen, note, UiKit.FontLabel, (812f - 56f - 24f) / 3f - 20f);
                Check(wrapped.Count <= 2 && wrapped.Count * UiKit.Zen.GetHeight(UiKit.FontLabel) <= 46f,
                    $"{topic} note fits: {wrapped.Count} lines, {wrapped.Count * UiKit.Zen.GetHeight(UiKit.FontLabel):0.0}px");
            }
        }
        StageTutorial.SyncCard(hud, new[] { (0, "QA non-tutorial dialogue", "") }, 0);
        await Frames(30);
        Check(Read<ControlCard.Topic>(active, "_topic") == ControlCard.Topic.None, "ordinary dialogue dismisses the tutorial card");
        StageTutorial.SyncCard(hud, route, 0);
        await Frames(30);
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(30);
        Check(Read<ControlCard.Topic>(active, "_topic") == ControlCard.Topic.None, "closing dialogue dismisses the tutorial card");
        await Capture("card_dialogue_closed");
    }

    private async Task CheckHowTo()
    {
        float twoLineHeight = UiKit.Zen.GetHeight(UiKit.FontLabel) * 2f;
        GD.Print($"[TutorialQA] HowTo label metrics: font={UiKit.FontLabel}, line={UiKit.Zen.GetHeight(UiKit.FontLabel):0.0}px, two lines={twoLineHeight:0.0}px, row remainder=44px");
        Check(twoLineHeight <= 44f, "HowTo descriptions fit inside their control rows");
        var how = GetNode<HowToPlay>("/root/HowTo");
        Write(how, "_autoplay", false);
        _scene!.GetNode("World/Player").SetPhysicsProcess(false);
        GetTree().Paused = true;
        foreach (bool upgraded in new[] { false, true })
        {
            _game.TrainingSetUpgrade("n_dodge", upgraded);
            _game.TrainingSetUpgrade("n_charge", upgraded);
            _game.TrainingSetUpgrade("n_slow", upgraded);
            foreach (int device in new[] { 0, 1, 2 })
            {
                Device(device);
                how.Open();
                Check(Read<int>(how, "_page") == device, $"HowTo opens on device {device}");
                await Frames(4);
                await Capture($"howto_{(upgraded ? "upgraded" : "initial")}_{new[] { "keyboard", "pad", "mouse" }[device]}");
                Call(how, "Close");
            }
        }
        GetTree().Paused = false;
        _game.TrainingSetUpgrade("n_dodge", false);
        _game.TrainingSetUpgrade("n_charge", false);
        _game.TrainingSetUpgrade("n_slow", false);
        Device(0);
        await Frames(3);
    }

    private async Task CheckFieldGuidance()
    {
        foreach (Job job in new[] { Job.Tank, Job.Magic })
        {
            _game.ResetPersistent();
            _game.SelectedJob = job;
            _scene = GD.Load<PackedScene>("res://Akari.tscn").Instantiate();
            GetTree().Root.AddChild(_scene);
            GetTree().CurrentScene = _scene;
            var stage = _scene.GetNode<StageAkari>("StageAkari");
            stage.SetProcess(false);
            stage.Player.SetPhysicsProcess(false);
            stage.Hud.HoldBubble = false;
            stage.Hud.HideBubble();
            var intro = Read<(int who, string text, string face)[]>(stage, "_playerIntro");
            Check(job != Job.Tank || intro.Length == 9, "first sortie reaches combat after nine lines");
            Check(job == Job.Tank || intro.All(line => line.who != 1), "companion sortie does not use Mina's intro");
            Write(stage, "_step", 2);
            Write(stage, "_stepStarted", false);
            stage._Process(0);
            var spawner = Read<Spawner>(stage, "_spawner");
            spawner.SetProcess(false);
            Write(stage, "_stageElapsed", 5.0);
            await Frames(3);

            bool Tick() => (bool)Call(stage, "TickFieldTutorial")!;
            void AllowNext() => Write(stage, "_nextTutorialTime", 0.0);
            void Purified(int count) => typeof(GameManager).GetProperty(nameof(GameManager.PurifiedCount))!.SetValue(_game, count);
            GlyphMote Target(Vector2 position)
            {
                var enemy = new GlyphMote { Position = position };
                stage.World.AddChild(enemy);
                enemy.SetPhysicsProcess(false);
                return enemy;
            }
            void FinishGuide(string name)
            {
                Write(stage, "_zEdge", true);
                for (int i = 0; i < 100 && Read<(int, string, string)[]>(stage, "_fieldTutorial").Length > 0; i++)
                {
                    stage.Hud.RevealDialogNow();
                    Tick();
                }
                Write(stage, "_zEdge", false);
                Check(Read<(int, string, string)[]>(stage, "_fieldTutorial").Length == 0 && !Hud.BubblePaused,
                    $"{name}: dialogue ends and releases combat");
                Check(Read<int>(stage, "_step") == 2 && Read<int>(stage, "_waveBase") == 0
                    && ReferenceEquals(spawner, Read<Spawner>(stage, "_spawner")) && spawner.Active,
                    $"{name}: wave and spawner survive the guide");
            }

            Check(!Tick() && !_game.IsIdleDialogSeen(StageTutorial.ShieldSeenKey), "no shield explanation before an enemy appears");
            Target(new Vector2(285, 75));
            await Frames(3);
            if (job != Job.Tank)
            {
                stage.Player.ApplyPowerup(PowerKind.Line);
                Target(new Vector2(95, 145));
                Purified(8);
                Check(!Tick() && !Hud.BubblePaused && !_game.IsIdleDialogSeen(StageTutorial.ItemsAkariSeenKey)
                    && !_game.IsIdleDialogSeen(StageTutorial.LockSeenKey), "companion combat does not consume or display Mina's field guidance");
                await LeaveScene();
                continue;
            }
            Check(Tick() && Hud.BubblePaused && _game.IsIdleDialogSeen(StageTutorial.ShieldSeenKey), "visible panel enemy starts shield guidance");
            FinishGuide("shield");
            Check(!Tick(), "guides leave eight seconds of play between explanations");

            Target(new Vector2(95, 145));
            Purified(1);
            AllowNext();
            Check(Tick() && _game.IsIdleDialogSeen(StageTutorial.LockSeenKey), "a second or rear target starts lock guidance");
            FinishGuide("lock");
            Purified(3);
            AllowNext();
            Check(Tick() && _game.IsIdleDialogSeen(StageTutorial.SkillChargeSeenKey), "panel combat after practice starts charge guidance");
            FinishGuide("charge");
            Target(new Vector2(305, 155));
            Purified(6);
            AllowNext();
            Check(Tick() && _game.IsIdleDialogSeen(StageTutorial.BombSeenKey), "a group of enemies starts bomb guidance");
            FinishGuide("bomb");

            stage.Player.ApplyPowerup(PowerKind.Line);
            Purified(60);
            AllowNext();
            Check(!Tick() && !_game.IsIdleDialogSeen(StageTutorial.ItemsAkariSeenKey), "wave completion takes priority over new item guidance");
            Write(stage, "_step", 10);
            Purified(10);
            Check(!Tick() && !_game.IsIdleDialogSeen(StageTutorial.ItemsAkariSeenKey), "story scenes are not interrupted by item guidance");
            Write(stage, "_step", 2);
            Check(Tick() && _game.IsIdleDialogSeen(StageTutorial.ItemsAkariSeenKey), "collected power-up starts item guidance during combat");
            FinishGuide("item");
            AllowNext();
            Check(!Tick(), "completed field guidance does not repeat");
            await LeaveScene();
        }
        _game.ResetPersistent();
        _game.SelectedJob = Job.Tank;
    }

    private async Task CheckPractice()
    {
        var root = GD.Load<PackedScene>("res://Stage0.tscn").Instantiate<Stage0Root>();
        _scene = root;
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        root.Stage.SetProcess(false);
        root.Stage.SetPhysicsProcess(false);
        root.Player.SetPhysicsProcess(false);
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        await Frames(3);
        SetPhase(root.Stage, 2);
        root.Stage._Process(0);
        foreach (var key in new[] { Godot.Key.W, Godot.Key.A, Godot.Key.S, Godot.Key.D })
        {
            SetKey(key, true);
            await Frames(2);
            Check(Input.IsKeyPressed(key), $"WASD fixture: {key} press reaches Input");
            root.Player._PhysicsProcess(1.0 / 60);
            root.Stage._PhysicsProcess(1.1);
            root.Stage._Process(1.1);
            SetKey(key, false);
            await Frames(2);
        }
        Check(Read<double>(root.Stage, "_t1Up") == 0 && Read<double>(root.Stage, "_t1Down") == 0
            && Read<double>(root.Stage, "_t1Left") == 0 && Read<double>(root.Stage, "_t1Right") == 0,
            "WASD does not count as movement");
        Check(Read<int>(root.Stage, "_phase") == 2, "WASD does not complete movement practice");

        foreach (int fps in new[] { 30, 144 })
        {
            SetPhase(root.Stage, 2);
            root.Stage._Process(0);
            foreach (string action in new[] { "ui_right", "ui_left" })
            {
                root.Player.GlobalPosition = new Vector2(245, 108);
                Write(root.Stage, "_t1PreviousPosition", root.Player.GlobalPosition);
                Input.ActionPress(action);
                Check(Input.IsActionPressed(action), $"{fps} FPS fixture: {action} reaches Input");
                double renderTime = 0;
                for (int tick = 0; tick < 60; tick++)
                {
                    Device(0);
                    root.Player._PhysicsProcess(1.0 / 60);
                    root.Stage._PhysicsProcess(1.0 / 60);
                    renderTime += 1.0 / 60;
                    while (renderTime + 0.000001 >= 1.0 / fps)
                    {
                        root.Stage._Process(1.0 / fps);
                        renderTime -= 1.0 / fps;
                    }
                }
                Input.ActionRelease(action);
            }
            Check(Math.Abs(Read<double>(root.Stage, "_t1Left") - 1.0) < 0.000001
                && Math.Abs(Read<double>(root.Stage, "_t1Right") - 1.0) < 0.000001,
                $"{fps} render FPS: both directions count one second at 60 physics FPS");
        }

        SetPhase(root.Stage, 2);
        root.Stage._Process(0);
        Device(2);
        root.Player.GlobalPosition = new Vector2(180, 50);
        Write(root.Stage, "_t1PreviousPosition", root.Player.GlobalPosition);
        foreach (var point in Enumerable.Range(0, 3).SelectMany(_ => new[] { new Vector2(310, 50), new Vector2(310, 170), new Vector2(180, 170), new Vector2(180, 50) }))
        {
            // WarpMouse uses viewport coordinates; Player reads the same canvas-transformed cursor.
            GetViewport().WarpMouse(root.Player.GetCanvasTransform() * point);
            await Frames(2);
            for (int i = 0; i < 110 && Read<int>(root.Stage, "_phase") == 2; i++)
            {
                Device(2);
                root.Player._PhysicsProcess(1.0 / 60);
                root.Stage._PhysicsProcess(1.0 / 60);
                root.Stage._Process(1.0 / 60);
            }
        }
        Check(Read<int>(root.Stage, "_phase") == 3, "actual mouse-follow movement completes all four directions");
        foreach (int device in new[] { 0, 1, 2 })
        {
            Device(device);
            root.Hud.SetTutorialOp("move");
            root.Hud.SetTutorialHint("各方向へ一秒：↑済 ↓済 ←未 →済");
            await Frames(3);
            await Capture($"practice_move_{new[] { "keyboard", "pad", "mouse" }[device]}");
        }

        Device(0);
        SetPhase(root.Stage, 10);
        root.Stage._Process(0);
        var targets = GetTree().GetNodesInGroup("enemies").OfType<Enemy>().Where(e => !e.IsPurified).ToArray();
        Check(targets.Length == 3, "bomb practice spawns three targets");
        root.Player.GlobalPosition = new Vector2(180, 108);
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].GlobalPosition = new Vector2(250 + i * 35, 108);
            targets[i].SetPhysicsProcess(false);
        }
        int purifiedBefore = _game.PurifiedCount;
        root.Player.SetPhysicsProcess(true);
        for (int i = 0; i < 600 && _game.PurifiedCount - purifiedBefore < 3; i++)
        {
            Device(0);
            await Frames(1);
        }
        root.Player.SetPhysicsProcess(false);
        Check(_game.PurifiedCount - purifiedBefore == 3, "automatic shots purify three practice targets");
        root.Stage._Process(0.016);
        Check(Read<int>(root.Stage, "_phase") == 10, "automatic shots do not complete bomb practice");
        Check(_game.BombPurifiedCount == 0, "automatic shots are excluded from the bomb count");
        await Frames(2);
        BombDiagnostics(root, "before X");
        root.Player._PhysicsProcess(1.0 / 60);
        SetKey(Godot.Key.X, true);
        await Frames(2);
        Check(Input.IsKeyPressed(Godot.Key.X), "bomb fixture: X press reaches Input");
        root.Player._PhysicsProcess(1.0 / 60);
        BombDiagnostics(root, "X held");
        SetKey(Godot.Key.X, false);
        await Frames(2);
        root.Player._PhysicsProcess(1.0 / 60);
        Check(root.Player.BombCount > 0 && _game.BombPurifiedCount == 3, "one bomb purifies the three replacement targets");
        root.Stage._Process(0.016);
        Check(Read<int>(root.Stage, "_phase") == 11, "a successful bomb completes bomb practice");
        await Frames(12);
        Engine.TimeScale = 1;
        foreach (string operation in new[] { "shot", "dodge", "bomb" })
        {
            foreach (int device in new[] { 0, 1, 2 })
            {
                Device(device);
                root.Hud.SetTutorialOp(operation);
                root.Hud.SetTutorialHint(operation == "bomb" ? "一度のボムで、三体まとめて（0/3）"
                    : operation == "dodge" ? "回避を使う（0/3）" : "自動の光で、三体浄化する（0/3）");
                await Frames(3);
                await Capture($"practice_{operation}_{new[] { "keyboard", "pad", "mouse" }[device]}");
            }
        }
    }

    private static void SetPhase(StageZero stage, int phase)
    {
        Write(stage, "_phase", phase);
        Write(stage, "_phaseStarted", false);
        Write(stage, "_phaseTime", 0.0);
    }

    private void BombDiagnostics(Stage0Root root, string point)
    {
        var enemies = GetTree().GetNodesInGroup("enemies").OfType<Enemy>().ToArray();
        GD.Print($"[TutorialQA] {point}: bombs={root.Player.BombCount}, caught={_game.BombPurifiedCount}, live={enemies.Count(e => !e.IsPurified)}, transforming={enemies.Count(e => e.Transforming)}, bubble={Hud.BubblePaused}, ui={Pad.UiBlocked(root.Player)}, paused={GetTree().Paused}, scale={Engine.TimeScale}, X={Input.IsKeyPressed(Godot.Key.X)}, held={Read<bool>(root.Player, "_bombHeld")}, practice={_game.TutorialNoConsume}");
    }

    private static void SetKey(Godot.Key key, bool pressed) => Input.ParseInputEvent(
        new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private void Check(bool ok, string message)
    {
        if (ok) GD.Print($"[TutorialQA] PASS {message}");
        else { _fails++; GD.PrintErr($"[TutorialQA] FAIL {message}"); }
    }

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        string path = $"{_outDir.TrimEnd('/', '\\')}/{name}.png";
        var result = image.SavePng(path);
        Check(result == Error.Ok, $"screenshot {name}: {result}");
    }

    private async Task LeaveScene()
    {
        if (_scene == null) return;
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        _scene.GetParent().RemoveChild(_scene);
        _scene.QueueFree();
        _scene = null;
        GetTree().CurrentScene = this;
        Hud.BubblePaused = false;
        await Frames(3);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
