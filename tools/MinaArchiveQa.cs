using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class MinaArchiveQa : Node
{
    private const double Step = 1d / 60;
    private Player _player = null!;
    private BossMina _boss = null!;
    private GameManager _game = null!;
    private Node2D _root = null!;
    private string _output = "";
    private readonly List<object> _reports = new();

    public override async void _Ready()
    {
        int exit = 0;
        try
        {
            Require(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated QA saves");
            var destination = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--output="));
            _output = ProjectSettings.GlobalizePath(destination == null ? "res://build/qa_story/mina_archive" : destination[9..]);
            System.IO.Directory.CreateDirectory(_output);
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = GameManager.Diff.Easy;
            _game.SelectedJob = Job.Magic;
            _game.TrainingSetAllUpgrades(false);
            foreach (string tutorial in new[] { "once_midboss_shield", "once_tutorial_route", "once_tutorial_shield",
                "once_tutorial_lock", "once_tutorial_bomb", "once_skill_charge", "once_ankers_rei" })
                _game.MarkIdleDialogSeen(tutorial);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            Input.UseAccumulatedInput = false;
            await Frames(2);
            _root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(_root);
            GetTree().CurrentScene = _root;
            _root.SetProcess(false);
            ((Node)_root.GetType().GetProperty("Stage")!.GetValue(_root)!).SetProcess(false);
            var world = _root.GetNode<Node2D>("World");
            world.ProcessMode = ProcessModeEnum.Inherit;
            _player = world.GetNode<Player>("Player");
            _player.SetPhysicsProcess(false);
            typeof(QaPilot).GetProperty(nameof(QaPilot.GodActive))!.SetValue(null, true);
            Write(_player, "_invincible", false);
            Write(_player, "_invincibleTimer", 0f);
            Write(_player, "_fireCooldown", 99999f);
            _boss = new BossMina { TechniquePractice = true, Position = new Vector2(350, 108) };
            world.AddChild(_boss);
            _boss.SetProcess(false);
            _boss.SetPhysicsProcess(false);
            Read<BossPostSequence>(_boss, "_posts").ProcessMode = ProcessModeEnum.Disabled;
            _boss.GetChildren().OfType<MinaPhaseAttacks>().Single().SetProcess(false);
            _boss.PrepareTechniquePractice(3);
            _boss.SetPanelsInvulnerable(true);
            var hud = _root.GetNode<Hud>("Hud");
            hud.HoldBubble = false;
            hud.HideBubble();
            Hud.BubblePaused = false;
            _game.EndFury();
            _root.GetNode<StageBackground>("StageBackground").EnterBoss();
            _root.GetNode<BgLayers>("StageBackground/BgLayers")._Process(2);
            _boss.TickBossFrame(2);
            await Frames(3);
            bool render = DisplayServer.GetName() != "headless";
            if (render)
            {
                var layer = new CanvasLayer { Layer = 110 };
                AddChild(layer);
                var label = new Label { Text = "検証映像\n自動操作・無敵", Position = new Vector2(5, 154) };
                label.AddThemeFontOverride("font", UiKit.ZenBold);
                label.AddThemeFontSizeOverride("font_size", 5);
                label.AddThemeColorOverride("font_color", new Color("a9c4cf"));
                layer.AddChild(label);
                await RunCase(0, new Vector2(Field.Left + 12, 110), true);
                await CheckVisualCleanup();
            }
            else
            {
                for (int difficulty = 0; difficulty < 4; difficulty++)
                {
                    foreach (float x in new[] { Field.Left + 3, Field.CenterX, Field.Right - 3 })
                    foreach (float y in new[] { 3f, 108f, 213f })
                        await RunCase(difficulty, new Vector2(x, y), false);
                    await CheckCycle(difficulty);
                }
            }
            System.IO.File.WriteAllText(_output + (render ? "/render.json" : "/qa.json"),
                JsonSerializer.Serialize(_reports, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[MINA_ARCHIVE] PASS cases={_reports.Count} invulnerable=true contacts_measured=true");
        }
        catch (Exception error)
        {
            GD.PushError($"[MINA_ARCHIVE] FAIL {error}");
            exit = 1;
        }
        Input.ActionRelease("ui_left");
        Input.ActionRelease("ui_right");
        if (IsInstanceValid(_root)) _root.QueueFree();
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
        { audio.Stop(); audio.Stream = null; }
        await Frames(5);
        GetTree().Quit(exit);
    }

    private MinaArchiveTechnique Begin(Vector2 start)
    {
        _player.GlobalPosition = start;
        _boss.TickBossFrame(2);
        var attack = (MinaArchiveTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Extra, 2);
        attack.SetPhysicsProcess(false);
        Require(attack.SpellName == "未送信の摩天楼", "practice identity");
        return attack;
    }

    private void Pilot(MinaArchiveTechnique attack)
    {
        // Only current, visible blueprint/solid rectangles are read; no future floor table is used.
        var shown = attack.PracticeHazards;
        float y = _player.GlobalPosition.Y;
        float left = Field.Left + 3, right = Field.Right - 3;
        foreach (var h in shown)
        {
            if (h.B.Y < y - 3 || h.A.Y > y + 13) continue;
            if (Math.Abs(h.A.X - Field.Left) < .01f) left = Math.Max(left, h.B.X + 3);
            if (Math.Abs(h.B.X - Field.Right) < .01f) right = Math.Min(right, h.A.X - 3);
        }
        if (y > Field.Bottom - 15)
            foreach (var cue in attack.EntranceForecasts.Where(cue => cue.EntersAt - attack.Elapsed < .07))
            {
                left = Math.Max(left, cue.Left.End.X + 3);
                right = Math.Min(right, cue.Right.Position.X - 3);
            }
        Require(right > left, $"visible passage stays connected at {attack.Elapsed:F3}s, y={y:F2}, interval={left:F2}..{right:F2}");
        float target = (left + right) * .5f;
        float dx = target - _player.GlobalPosition.X;
        Input.ActionRelease("ui_left");
        Input.ActionRelease("ui_right");
        if (Math.Abs(dx) > .65f) Input.ActionPress(dx < 0 ? "ui_left" : "ui_right");
        _player._PhysicsProcess(Step);
    }

    private async Task RunCase(int difficulty, Vector2 start, bool render)
    {
        _game.Difficulty = (GameManager.Diff)difficulty;
        var attack = Begin(start);
        Require(attack.EasyCues.All(cue => cue.FiresAt - .4 >= 2), "complete blueprint hold >= 2s");
        int contacts = 0, frame = 0, geometry = 0, densityPeak = 0;
        float moved = 0;
        var probes = (from x in new[] { Field.Left + 3, Field.CenterX, Field.Right - 3 }
            from y in new[] { 3f, 108f, 213f } select new Vector2(x, y)).ToArray();
        var struck = new bool[probes.Length];
        var floors = new HashSet<int>();
        var entranceLeads = new Dictionary<int, double>();
        int projections = 0;
        var captureTimes = new Queue<double>(new[] { .8, 4.6, 6.4, 10.0, 14.0, 20.0 });
        bool video = render && OS.GetCmdlineUserArgs().Contains("--video");
        string frames = _output + "/frames";
        if (video) System.IO.Directory.CreateDirectory(frames);
        while (!attack.Finished && frame < 2400)
        {
            Vector2 before = _player.GlobalPosition;
            Pilot(attack);
            moved += before.DistanceTo(_player.GlobalPosition);
            attack.Tick(Step);
            if (!attack.Finished)
            {
                if (attack.CoversPoint(_player.GlobalPosition)) contacts++;
                for (int i = 0; i < probes.Length; i++) struck[i] |= attack.CoversPoint(probes[i]);
                foreach (var cue in attack.EasyCues.Where(cue => cue.Live))
                    floors.Add(int.Parse(cue.Key.Split('-').Last()));
                foreach (var cue in attack.EntranceForecasts)
                    entranceLeads.TryAdd(cue.Floor, cue.EntersAt - attack.Elapsed);
                foreach (var h in attack.PracticeHazards)
                {
                    Require(h.Shape == 2 && h.A.X >= Field.Left && h.B.X <= Field.Right,
                        "collision/blueprint rectangles stay inside the arena");
                    geometry++;
                }
                if (frame % 30 == 0)
                {
                    int density = 0;
                    for (float x = Field.Left + 4; x < Field.Right; x += 8)
                    for (float y = 4; y < Field.Bottom; y += 8)
                        if (attack.CoversPoint(new Vector2(x, y))) density++;
                    densityPeak = Math.Max(densityPeak, density);
                }
            }
            if (render)
            {
                if (frame % 30 == 0) projections += CheckProjection(attack);
                await Frames(1);
                if (captureTimes.TryPeek(out double at) && attack.Elapsed >= at)
                {
                    captureTimes.Dequeue();
                    await Shot($"{_output}/stage_{at:00.0}.png");
                }
                if (video && frame % 2 == 0) await Shot($"{frames}/{frame / 2:00000}.jpg");
            }
            else if (frame % 300 == 0) await Frames(1);
            frame++;
        }
        Require(attack.Finished && attack.Completed, "natural completion");
        Require(attack.NaturalEndRemainingShots == 0 && attack.NaturalEndLiveHazards == 0, "clean natural end");
        Require(floors.Count == MinaArchiveTechnique.FloorCount, "every floor locks");
        var incoming = Enumerable.Range(0, MinaArchiveTechnique.FloorCount)
            .Where(floor => attack.FloorBounds(floor, false, 0).Position.Y >= Field.Bottom);
        Require(incoming.ToHashSet().SetEquals(entranceLeads.Keys) && entranceLeads.Values.All(lead => lead >= 2.58),
            "every floor entering from below has a complete >=2 second forecast");
        Require(struck.All(value => value), "no fixed corner/edge/centre camping point");
        Require(contacts == 0, $"normal input route contacts={contacts} start={start} difficulty={difficulty}");
        _reports.Add(new { difficulty, start = new { x = start.X, y = start.Y }, contacts, moved,
            elapsed = attack.Elapsed, floors = floors.Count, geometry, densityPeak, projections,
            minimumEntranceLead = entranceLeads.Values.Min(),
            staticProbesHit = struck.Count(value => value), pilot = "current visible rectangles, horizontal keyboard input",
            invulnerable = true, render });
        GD.Print($"[MINA_ARCHIVE] case tier={difficulty} start={start} contacts={contacts} floors={floors.Count} distance={moved:0.0}");
        await Frames(2);
        Require(!IsInstanceValid(attack), "technique and 3D children freed");
    }

    private int CheckProjection(MinaArchiveTechnique attack)
    {
        if (attack.Finished) return 0;
        var visuals = Read<MinaArchiveVisuals>(attack, "_visuals");
        var blocks = Read<List<(int Floor, bool Right, Node3D Root, MeshInstance3D Mesh)>>(visuals, "_blocks");
        int checkedBounds = 0;
        foreach (var block in blocks.Where(block => block.Root.Visible && attack.Elapsed >= attack.LockAt(block.Floor)))
        {
            var box = block.Mesh.GetAabb();
            var corners = Enumerable.Range(0, 8).Select(index => block.Mesh.GlobalTransform * box.GetEndpoint(index))
                .Select(point => new Vector2(point.X, -point.Y)).ToArray();
            var expected = attack.FloorBounds(block.Floor, block.Right, attack.Lift);
            var low = new Vector2(corners.Min(p => p.X), corners.Min(p => p.Y));
            var high = new Vector2(corners.Max(p => p.X), corners.Max(p => p.Y));
            Require(low.DistanceTo(expected.Position) < .01f && high.DistanceTo(expected.End) < .01f,
                $"projected 3D volume matches collision bounds: {low}..{high} versus {expected}");
            checkedBounds++;
        }
        return checkedBounds;
    }

    private async Task CheckVisualCleanup()
    {
        foreach (double at in new[] { .2, 4.5, 10.0 })
        {
            var attack = Begin(new Vector2(Field.CenterX, 108));
            while (attack.Elapsed < at) attack.Tick(Step);
            var visuals = Read<MinaArchiveVisuals>(attack, "_visuals");
            var view = Read<SubViewport>(visuals, "_view");
            attack.Cancel();
            Require(!attack.Completed && !attack.IsStriking && attack.PracticeHazards.Count == 0
                && view.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled, "cancel clears hazards and stops the 3D renderer");
            await Frames(2);
            Require(!IsInstanceValid(visuals) && !IsInstanceValid(view), "cancel frees the 3D world");
            _reports.Add(new { cancelledAt = at, cleared = true });
        }
    }

    private async Task CheckCycle(int difficulty)
    {
        _game.Difficulty = (GameManager.Diff)difficulty;
        _boss.TechniquePractice = false;
        var target = new BossAttackCycle.Slot(BossTechniqueKind.Extra, 2);
        var slots = BossAttackCycle.Slots(_boss, 3);
        Require(slots.Count(slot => slot == target) == 1, "one normal battle slot in phase 3");
        var caster = _boss.GetChildren().OfType<MinaPhaseAttacks>().Single();
        typeof(MinaPhaseAttacks).GetProperty(nameof(MinaPhaseAttacks.OpenerCompleted))!.SetValue(caster, true);
        var cycle = new BossDanmaku(_boss);
        var history = Read<BossAttackCycle.History>(cycle, "_history");
        foreach (var slot in slots.Where(slot => slot != target))
        {
            history.Start(slot);
            history.Complete(slot, _boss.IsDragonForm);
        }
        void TickCycle()
        {
            _boss.TickBossFrame(Step);
            cycle.Tick(Step);
        }
        for (int frame = 0; frame < 180 && cycle.ActiveTechnique == null; frame++) TickCycle();
        Require(cycle.ActiveTechnique is MinaArchiveTechnique, "normal scheduler selects new attack after prior completed slots");
        int Slot() => _boss.UsesEasyTechniqueSlots ? cycle.EasySlot : cycle.CycleSlot;
        int selected = Slot();
        cycle.ActiveTechnique!.Cancel();
        await Frames(2);
        for (int frame = 0; frame < 180 && cycle.ActiveTechnique is not { Finished: false }; frame++) TickCycle();
        Require(cycle.ActiveTechnique is { Finished: false } and not MinaArchiveTechnique && Slot() != selected,
            "the immediately preceding attack is excluded after cancellation");
        var interlude = cycle.ActiveTechnique!;
        interlude.SetPhysicsProcess(false);
        for (int frame = 0; frame < 10800 && !interlude.Finished; frame++) interlude.Tick(Step);
        Require(interlude.Completed, "the intervening attack finishes before a retry");
        await Frames(2);
        for (int frame = 0; frame < 180 && cycle.ActiveTechnique is not { Finished: false }; frame++) TickCycle();
        Require(cycle.ActiveTechnique is MinaArchiveTechnique && Slot() == selected, "the interrupted floor sequence is retried after a different attack");
        var retry = cycle.ActiveTechnique!;
        retry.SetPhysicsProcess(false);
        for (int frame = 0; frame < 2400 && !retry.Finished; frame++) retry.Tick(Step);
        Require(retry.Completed && Slot() != selected, $"natural completion advances scheduler: complete={retry.Completed} slot={Slot()} previous={selected}");
        _reports.Add(new { difficulty, scheduler = "normal battle with preceding slots seeded completed", retry = true, advances = true });
        _boss.TechniquePractice = true;
        await Frames(2);
    }

    private async Task Shot(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Require((path.EndsWith(".jpg") ? image.SaveJpg(path, .9f) : image.SavePng(path)) == Error.Ok, "capture saved");
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    private static T Read<T>(object target, string field) => (T)FindField(target, field).GetValue(target)!;
    private static void Write(object target, string field, object value) => FindField(target, field).SetValue(target, value);
    private static FieldInfo FindField(object target, string field)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is { } found) return found;
        throw new MissingFieldException(field);
    }
}
