using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class AttackToolsQa : Node
{
    private BossPracticeRoot _practice = null!;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        GD.Print("[AttackToolsQA] PASS " + message);
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            DisplayServer.WindowSetPosition(new Vector2I(-16000, -16000));
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.Difficulty = GameManager.Diff.Easy;
            await Frames(2);
            _practice = GD.Load<PackedScene>(BossPracticeRoot.ScenePath).Instantiate<BossPracticeRoot>();
            GetTree().Root.AddChild(_practice);
            GetTree().CurrentScene = _practice;
            _practice.StartPractice();
            for (int frame = 0; frame < 300 && _practice.Repetitions == 0; frame++) await Frames(1);
            await Press(Key.F6);
            var attack = GetTree().GetFirstNodeInGroup("boss_character_techniques") as BossCharacterTechnique;
            Check(attack != null && _practice.PreviewPaused, "live practice paused");
            double battleTime = attack!.Elapsed;
            await Press(Key.F10);
            var editor = _practice.CurveEditor ?? throw new Exception("F10 did not open the editor");
            Check(GetTree().Paused, "F10 opens curve editor and freezes battle");
            Vector2 oldPoint = editor.Curve.GetPointPosition(1), moved = oldPoint + new Vector2(15, -20);
            await Drag(oldPoint, moved);
            Check(editor.Curve.GetPointPosition(1).DistanceTo(moved) < .01f, "mouse drag moves anchor");
            Vector2 oldHandle = editor.Curve.GetPointOut(1), handle = moved + oldHandle;
            await Drag(handle, handle + new Vector2(-6, 12));
            Check(editor.Curve.GetPointOut(1).DistanceTo(oldHandle + new Vector2(-6, 12)) < .01f, "mouse drag changes tangent");
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
                Position = new Vector2(160, 90), DoubleClick = true }, true);
            await Frames(2);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(160, 90) }, true);
            await Frames(2);
            Check(editor.Curve.PointCount == 4, "double click inserts a point");
            await Press(Key.Z, true);
            Check(editor.Curve.PointCount == 3, "Ctrl+Z restores previous curve");
            await Press(Key.S, true);
            Check(FileAccess.FileExists(editor.DraftPath), "Ctrl+S saves Curve2D asset");
            await Drag(moved, moved + new Vector2(12, 12));
            await Press(Key.O, true);
            Check(editor.Curve.GetPointPosition(1).DistanceTo(moved) < .01f, "Ctrl+O reloads saved geometry");
            Check(attack.Elapsed == battleTime, "editing does not advance the real attack");
            await Shot("curve_edit");
            editor.AdvancePreview(2.7);
            await Shot("curve_fire");
            var line = editor.GetChildren().OfType<AttackLineLayer>().Single().GetChildren().OfType<Line2D>().Single();
            Check(line.Visible && line.Points.SequenceEqual(editor.Curve.TessellateEvenLength(6, 2)), "preview uses the edited Curve2D geometry");
            double time = editor.Elapsed;
            await Frames(8);
            Check(editor.Elapsed == time, "paused preview stays fixed");
            await Press(Key.F7);
            Check(Math.Abs(editor.Elapsed - time - 1d / 60) < .0001, "single step advances one preview frame");
            var presentation = editor.GetChildren().OfType<AttackPresentation>().Single();
            Check(presentation.Launches == 1 && presentation.GetChildren().OfType<GpuParticles2D>().Single().Amount == 48,
                "preview launch uses bounded GPU particles");
            var animation = presentation.GetChildren().OfType<AnimationPlayer>().Single();
            Check(animation.HasAnimation("charge") && animation.HasAnimation("fire")
                && animation.CallbackModeProcess == AnimationMixer.AnimationCallbackModeProcess.Manual,
                "charge and fire animations use the combat clock");
            await Press(Key.F10);
            Check(_practice.CurveEditor == null && _practice.PreviewPaused && GetTree().Paused, "closing editor restores prior pause state");
            Check(attack.Elapsed == battleTime, "closing editor preserves attack position");
            var retired = new AttackPresentation { Character = Job.Melee };
            _practice.PracticePlayer.GetParent().AddChild(retired);
            retired.Dissolve(new Vector2(Field.CenterX, 120));
            retired.Finish(true);
            await Frames(10);
            Check(IsInstanceValid(retired), "finished attack particles freeze with practice");
            for (int i = 0; i < 20; i++) await Press(Key.F7);
            Check(!IsInstanceValid(retired), "single stepping retires finished attack particles");
            await Press(Key.F10);
            await Press(Key.O, true);
            Check(_practice.CurveEditor!.Curve.GetPointPosition(1).DistanceTo(moved) < .01f, "saved curve survives closing the editor");
            _practice.ShowSelection();
            await Frames(3);
            Check(_practice.CurveEditor == null && !GetTree().Paused, "selection cleans editor and resumes menus");
            VerifyShortWakes();
            VerifyReleaseTiming();
            await VerifyCombatCurves();
            _practice.QueueFree();
            await Frames(3);
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            GD.Print("[AttackToolsQA] ALL PASS");
            await Frames(3);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(3);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("[AttackToolsQA] " + error);
            GetTree().Quit(1);
        }
    }

    private void VerifyShortWakes()
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        var presentation = new AttackPresentation { Character = Job.Melee, PreviewSpeed = 0 };
        AddChild(presentation);
        var shots = Enumerable.Range(0, 80).Select(_ => {
            var shot = pool.SpawnBossShot(new Vector2(Field.CenterX, Field.CenterY), Vector2.Right, Job.Melee, false);
            shot.SetPhysicsProcess(false);
            return shot;
        }).ToArray();
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var wakes = (System.Collections.IDictionary)typeof(AttackPresentation).GetField("_wakes", fields)!.GetValue(presentation)!;
        for (int frame = 0; frame < 10; frame++)
        {
            foreach (var shot in shots) shot.Position += new Vector2(.6f, .2f);
            presentation.Trace(shots, 1d / 60);
        }
        Check(wakes.Count == 64, "short wakes stay within the rendering budget");
        object wake = wakes[shots[0]]!;
        var points = (Vector2[])wake.GetType().GetField("Points", fields)!.GetValue(wake)!;
        var frozen = points.ToArray();
        shots[0].Position += Vector2.Right;
        presentation.Trace(shots, 0);
        Check(points.SequenceEqual(frozen), "paused wakes retain their exact geometry");
        shots[0].Position += Vector2.Right * 40;
        presentation.Trace(shots, 1d / 60);
        Check((int)wake.GetType().GetField("Count", fields)!.GetValue(wake)! == 1,
            "relocation does not draw a false dangerous connecting line");
        foreach (var shot in shots) pool.Despawn(shot);
        presentation.Trace(Array.Empty<Bullet>(), 1d / 60);
        Check(wakes.Count == 0, "pooled projectiles leave no stale wakes");
        presentation.Finish(false);
        Check(presentation.IsQueuedForDeletion(), "cancel removes the wake canvas");
    }

    private void VerifyReleaseTiming()
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (Job character in Enum.GetValues<Job>())
        {
            var presentation = new AttackPresentation { Character = character, PreviewSpeed = 0 };
            AddChild(presentation);
            var origin = new Vector2(Field.CenterX, Field.CenterY);
            var shot = pool.SpawnBossShot(origin, Vector2.Down * 60, character, false);
            shot.SetPhysicsProcess(false);
            shot.DeployBossFormation(origin + Vector2.Down * 10, warnedLaunch: true);
            presentation.Trace(new[] { shot }, 1d / 60);
            Check(presentation.Launches == 0, $"{character}: reservation has no release flash");
            shot.CancelLeadIn();
            presentation.Trace(new[] { shot }, 1d / 60);
            Check(presentation.Launches == 1, $"{character}: live release flashes once");
            presentation.Advance(.1, origin);
            presentation.Trace(new[] { shot }, 1d / 60);
            Check(presentation.Launches == 1, $"{character}: a moving projectile does not repeat its release");
            var bursts = (System.Collections.ICollection)typeof(AttackPresentation).GetField("_bursts", fields)!.GetValue(presentation)!;
            presentation.Advance(0, origin);
            Check(bursts.Count == 1, $"{character}: pause preserves the release frame");
            presentation.Advance(.2, origin);
            Check(bursts.Count == 0, $"{character}: release expires on the combat clock");
            for (int i = 0; i < 20; i++) presentation.Launch(origin, Vector2.Down);
            Check(presentation.Launches == 2 && bursts.Count == 1, $"{character}: simultaneous salvos have bounded flashes");
            presentation.Reset();
            Check(bursts.Count == 0 && !presentation.GetNode<Node2D>("Charge").Visible,
                $"{character}: retry clears charge and release");
            pool.Despawn(shot);
            presentation.Finish(false);
        }
    }

    private async Task VerifyCombatCurves()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var cases = new[] {
            (Boss: 0, Phase: 0, Kind: BossTechniqueKind.Crossfire, Variant: 2),
            (Boss: 1, Phase: 0, Kind: BossTechniqueKind.Normal, Variant: 0),
            (Boss: 2, Phase: 0, Kind: BossTechniqueKind.Normal, Variant: 1),
            (Boss: 3, Phase: 4, Kind: BossTechniqueKind.Area, Variant: 4),
        };
        foreach (var test in cases)
        {
            foreach (var entry in new[] { ("_bossIndex", test.Boss), ("_phase", test.Phase), ("_technique", 0) })
                typeof(BossPracticeRoot).GetField(entry.Item1, fields)!.SetValue(_practice, entry.Item2);
            _practice.ShowSelection();
            var slots = (BossAttackCycle.Slot[])typeof(BossPracticeRoot).GetField("_slots", fields)!.GetValue(_practice)!;
            int selected = Array.FindIndex(slots, slot => slot.Kind == test.Kind && slot.Variant == test.Variant);
            Check(selected >= 0, $"boss {test.Boss} exposes its curve technique in the real roster");
            typeof(BossPracticeRoot).GetField("_technique", fields)!.SetValue(_practice, selected);
            _practice.StartPractice();
            BossCharacterTechnique? attack = null;
            for (int frame = 0; frame < 600; frame++)
            {
                await Frames(1);
                attack = GetTree().GetFirstNodeInGroup("boss_character_techniques") as BossCharacterTechnique;
                if (attack is { Elapsed: >= 1 }) break;
            }
            Check(attack is { Elapsed: >= 1 } && attack.TechniqueKind == test.Kind && attack.TechniqueVariant == test.Variant,
                $"boss {test.Boss} starts its curve technique through normal selection");
            object Read(string name) => attack!.GetType().GetField(name, fields)!.GetValue(attack)!;
            bool curved = test.Boss switch {
                0 => ((AttackCurveRoute[])Read("_carrierRoutes")).Take(4).All(route =>
                    route.Length > route.Points[0].DistanceTo(route.Points[^1]) + .5f),
                1 => ((Curve2D)Read("_twoBeatCurve")).PointCount == 5,
                2 => ((System.Collections.IEnumerable)Read("_easyPaths")).Cast<object>().Any(path =>
                    ((Vector2[])path.GetType().GetField("Points", fields | BindingFlags.Public)!.GetValue(path)!).Length > 2),
                _ => ((System.Collections.IEnumerable)Read("_easyMemoryCues")).Cast<object>().Any(cue =>
                    cue.GetType().GetField("Route", fields | BindingFlags.Public)!.GetValue(cue) is AttackCurveRoute route && route.Points.Length > 2),
            };
            Check(curved, $"boss {test.Boss} uses authored Curve2D geometry");
            _practice.ShowSelection();
            await Frames(3);
            Check(GetTree().GetNodesInGroup("boss_character_techniques").Count == 0,
                $"boss {test.Boss} cancels curve hazards when leaving practice");
        }
    }

    private async Task Press(Key key, bool control = false)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, CtrlPressed = control });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task Drag(Vector2 from, Vector2 to)
    {
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = from, Pressed = true }, true);
        await Frames(2);
        GetViewport().PushInput(new InputEventMouseMotion { Position = to, Relative = to - from, ButtonMask = MouseButtonMask.Left }, true);
        await Frames(2);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = to, Pressed = false }, true);
        await Frames(2);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string output = ProjectSettings.GlobalizePath("res://build/qa_story/attack_tools");
        DirAccess.MakeDirRecursiveAbsolute(output);
        using var image = GetViewport().GetTexture().GetImage();
        if (name == "curve_fire")
        {
            Vector2 probe = (_practice.CurveEditor!.Curve.GetPointPosition(0) + Vector2.Up * 20) * image.GetWidth() / 384f;
            Check(image.GetPixel((int)probe.X, (int)probe.Y).Luminance < .18f, "particle sprites stay near their launch origin");
        }
        Check(image.SavePng($"{output}/{name}.png") == Error.Ok, "saved " + name);
    }
}
