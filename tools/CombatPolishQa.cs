using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class CombatPolishQa : Node
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static FieldInfo Field(object value, string name)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Flags | BindingFlags.DeclaredOnly) is { } field) return field;
        throw new MissingFieldException(name);
    }
    private static T Read<T>(object value, string name) => (T)Field(value, name).GetValue(value)!;
    private static void Write(object value, string name, object? data) => Field(value, name).SetValue(value, data);
    private static void Call(object value, string name, params object[] args)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetMethod(name, Flags | BindingFlags.DeclaredOnly) is { } method)
            { method.Invoke(value, args); return; }
        throw new MissingMethodException(name);
    }
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        GD.Print("[PolishQA] PASS " + label);
    }
    private async Task Frames(int count)
    {
        for (int frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            if (OS.GetCmdlineUserArgs().Contains("--warning-edges"))
            {
                await Frames(5);
                CheckWarningEdges();
                await Frames(5);
                GetTree().Quit();
                return;
            }
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            if (OS.GetCmdlineUserArgs().Contains("--stationary-contacts"))
            {
                await Frames(2);
                await CheckStationaryContacts(game);
                foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
                { audio.Stop(); audio.Stream = null; }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Frames(5);
                GetTree().Quit();
                return;
            }
            if (DisplayServer.GetName() != "headless")
            {
                GetWindow().Mode = Window.ModeEnum.Windowed;
                foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(1280, 720), new Vector2I(1920, 1080) })
                {
                    GetWindow().Size = size;
                    await Frames(4);
                    Check(game.CursorPixelDimensions.Y == size.Y / 36, $"cursor follows resize {size}: {game.CursorPixelDimensions}");
                }
                GetWindow().Size = new Vector2I(1280, 720);
                game.TrainingSetImpression(1000);
                foreach (string cursor in new[] { "cursor_ember", "cursor_star" })
                {
                    game.TryPurchaseCosmetic(cursor);
                    Check(game.EquipCosmetic(cursor) && game.CursorPixelDimensions == new Vector2I(17, 20), "scaled cursor equips: " + cursor);
                }
                game.EquipCosmetic(Cosmetics.DefaultCursor);
            }
            foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
                await CheckRoad(game, scene);
            foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
                foreach (string scene in new[] { "Akari", "Koharu", "Rei" })
                    await CheckMidboss(game, scene, difficulty);
            GD.Print("[PolishQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("[PolishQA] FAIL " + error); GetTree().Quit(1); }
    }
    private static void CheckWarningEdges()
    {
        var random = new Random(2818);
        int probes = 0;
        for (int sample = 0; sample < 400; sample++)
        {
            Vector2 from = new((float)random.NextDouble() * 390 + 30, (float)random.NextDouble() * 290 - 40);
            Vector2 to = sample % 4 == 0 ? from : new((float)random.NextDouble() * 390 + 30, (float)random.NextDouble() * 290 - 40);
            float radius = sample % 7 == 0 ? 0 : 1 + (float)random.NextDouble() * 24;
            for (int edge = 0; edge < 4; edge++)
            {
                var span = BossCharacterTechnique.WarningEdgeSpan(from, to, radius, edge);
                for (int step = 0; step <= 636; step++)
                {
                    float t = step / 636f;
                    Vector2 point = edge switch {
                        0 => new(global::Field.Left, t * global::Field.Height),
                        1 => new(global::Field.Right, t * global::Field.Height),
                        2 => new(global::Field.Left + t * global::Field.Width, global::Field.Top),
                        _ => new(global::Field.Left + t * global::Field.Width, global::Field.Bottom),
                    };
                    float distance = point.DistanceTo(Geometry2D.GetClosestPointToSegment(point, from, to));
                    if (Math.Abs(distance - radius) < .001f) continue;
                    bool marked = span is { } range && point.DistanceTo(Geometry2D.GetClosestPointToSegment(point, range.A, range.B)) < .0001f;
                    if (marked != (distance < radius)) throw new Exception($"Warning boundary mismatch sample={sample}, edge={edge}, point={point}");
                    probes++;
                }
            }
        }
        Check(probes > 1000000, $"warning edge intersections agree with independent collision-distance probes ({probes})");
    }

    private (Node2D Root, Node Stage, Node2D World, Hud Hud) Open(GameManager game, string scene)
    {
        game.ResetPersistent();
        game.MarkIdleDialogSeen("once_midboss_shield");
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.ProcessMode = ProcessModeEnum.Disabled;
        var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        Hud.BubblePaused = false;
        Write(root.GetNode<Player>("World/Player"), "_invincible", true);
        return (root, stage, root.GetNode<Node2D>("World"), hud);
    }
    private async Task CheckRoad(GameManager game, string scene)
    {
        game.Difficulty = GameManager.Diff.Normal;
        var (root, stage, world, hud) = Open(game, scene);
        bool mina = scene == "MinaBattle";
        string method = mina ? "Step_Route" : "Step_MidwaveA";
        object[] args = mina ? Array.Empty<object>() : new object[] { 0d };
        int step = scene == "Akari" ? 5 : scene == "Rei" ? 3 : 2;
        Write(stage, "_step", step);
        Write(stage, "_stepStarted", false);
        Call(stage, method, args);
        if (mina) Write(stage, "_wave", 2);
        var spawner = Read<Spawner>(stage, mina ? "_echoSpawner" : "_spawner");
        spawner.Stop();
        var enemy = new MidEnemy { Position = new Vector2(global::Field.CenterX, 70) };
        enemy.Configure(EnemyTable.PrayerCarrier());
        world.AddChild(enemy);
        while (!game.StageCleared) game.AddPurify(0);
        Call(stage, method, args);
        Check(Read<int>(stage, "_step") == step && !enemy.IsQueuedForDeletion(), scene + ": purification threshold never deletes surviving enemies");
        var pool = GetNode<BulletPool>("/root/Pool");
        var shot = pool.Spawn(new Vector2(global::Field.CenterX, 150), Vector2.Right * 100, true);
        shot.SetPhysicsProcess(false);
        enemy.Purify();
        await Frames(2);
        Call(stage, method, args);
        Check(Read<int>(stage, "_step") == step && shot.Active, scene + ": enemy bullet prevents dialogue and is not cleared");
        var zone = new AreaStrike();
        zone.Configure(AreaStrike.Shape.Circle, 12, 12, 1, Colors.Red, Colors.White);
        world.AddChild(zone);
        shot._PhysicsProcess(10);
        Check(!shot.Active, scene + ": bullet exits naturally");
        Call(stage, method, args);
        Check(Read<int>(stage, "_step") == step, scene + ": pending area attack prevents dialogue");
        for (int frame = 0; frame < 240 && !zone.IsQueuedForDeletion(); frame++) zone._Process(1d / 60);
        await Frames(2);
        Call(stage, method, args);
        Check(Read<int>(stage, "_step") == step + 1, scene + ": next scenario starts only after all enemies and attacks drain");
        pool.DespawnAll();
        root.QueueFree();
        await Frames(3);
    }
    private async Task CheckStationaryContacts(GameManager game)
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        pool.ProcessMode = ProcessModeEnum.Disabled;
        using var core = new CircleShape2D { Radius = Player.HitRadius };
        var starts = new[] { new Vector2(global::Field.CenterX, global::Field.CenterY + 48),
            new Vector2(global::Field.Left + 24, global::Field.CenterY), new Vector2(global::Field.Right - 24, global::Field.CenterY),
            new Vector2(global::Field.Left + 24, global::Field.Top + 24), new Vector2(global::Field.Right - 24, global::Field.Top + 24),
            new Vector2(global::Field.Left + 24, global::Field.Bottom - 24), new Vector2(global::Field.Right - 24, global::Field.Bottom - 24) };
        if (OS.GetCmdlineUserArgs().Contains("--edge-probes"))
            starts = Enumerable.Range(0, 5).SelectMany(i => new[] {
                new Vector2(global::Field.Left + global::Field.Width * i / 4, global::Field.Top),
                new Vector2(global::Field.Left + global::Field.Width * i / 4, global::Field.Bottom),
                new Vector2(global::Field.Left, global::Field.Top + global::Field.Height * i / 4),
                new Vector2(global::Field.Right, global::Field.Top + global::Field.Height * i / 4),
            }).Distinct().Prepend(starts[0]).ToArray();
        var results = new System.Collections.Generic.List<object>();
        string output = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--output="))[9..];
        string? selected = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--boss="))?[7..];
        string? selectedDifficulty = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--difficulty="))?[13..];
        string? selectedSlot = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--only="))?[7..];
        foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" }.Where(s => selected == null || s == selected))
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>().Where(d => selectedDifficulty == null || d.ToString() == selectedDifficulty))
        {
            game.Difficulty = difficulty;
            var (root, stage, world, hud) = Open(game, scene);
            var player = root.GetNode<Player>("World/Player");
            Write(player, "_invincibleTimer", 99999f);
            Enemy boss = scene switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(), "Rei" => new BossRei(), _ => new BossMina() };
            world.AddChild(boss);
            Call(boss, "TickEntrance", 0d);
            Call(boss, "TickEntrance", 2d);
            Call(boss, "TickSwapAnim", 1d);
            boss.SetPanelsInvulnerable(true);
            game.EndFury();
            hud.HideBubble();
            Hud.BubblePaused = false;
            int phases = boss is BossMina ? 5 : 2;
            for (int phase = 0; phase < phases; phase++)
            {
                if (boss is BossMina) Write(boss, "_pattern", phase);
                else Write(boss, "_form2", phase != 0);
                var slots = BossAttackCycle.EasySlots(boss, phase).Distinct().ToList();
                if (phase == 1 && boss is BossKoharu or BossRei) slots.Add(new(BossTechniqueKind.Finale));
                foreach (var slot in slots.Where(slot => selectedSlot == null || selectedSlot == $"{phase}:{slot.Kind}:{slot.Variant}"))
                for (int start = 0; start < starts.Length; start++)
                {
                    player.GlobalPosition = starts[start];
                    boss.GlobalPosition = new Vector2(global::Field.BossCenterX, global::Field.BossZoneCenterY);
                    if (boss is BossMina mina && mina.IsDragonForm) boss.GlobalPosition = mina.KeepDragonInField(boss.GlobalPosition);
                    var attack = BossCharacterTechnique.Begin(boss, slot.Kind, slot.Variant);
                    string name = attack.SpellName, mechanic = attack.MechanicKey;
                    int contacts = 0, bodyContacts = 0, frames = 0;
                    var bodyShapes = boss.GetChildren().OfType<CollisionShape2D>().ToArray();
                    for (; frames < 10800 && !attack.Finished; frames++)
                    {
                        attack.Tick(1d / 60);
                        bool hit = !attack.Finished && attack.CoversPoint(player.GlobalPosition);
                        foreach (var bullet in GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Where(b => b.Active))
                        {
                            bullet._PhysicsProcess(1d / 60);
                            if (!bullet.Active || bullet.LeadIn || bullet.CollisionLayer == 0 || bullet.Footprint == null) continue;
                            float radius = bullet.Footprint.Radius * Math.Max(bullet.GlobalTransform.X.Length(), bullet.GlobalTransform.Y.Length()) + Player.HitRadius;
                            if (bullet.GlobalPosition.DistanceSquaredTo(player.GlobalPosition) <= radius * radius
                                && bullet.Footprint.Shapes.Any(shape => shape.Collide(bullet.GlobalTransform, core, new Transform2D(0, player.GlobalPosition)))) hit = true;
                        }
                        if (hit) contacts++;
                        if (bodyShapes.Any(shape => !shape.Disabled && shape.Shape.Collide(shape.GlobalTransform, core, new Transform2D(0, player.GlobalPosition)))) bodyContacts++;
                        if (frames % 600 == 599) await Frames(1);
                    }
                    if (!attack.Finished || !attack.Completed || player.GlobalPosition.DistanceTo(starts[start]) > .01f)
                        throw new Exception($"Stationary probe failed: {scene}/{difficulty}/{phase}/{name}/{start}");
                    results.Add(new { scene, difficulty = difficulty.ToString(), phase, name, mechanic,
                        kind = slot.Kind.ToString(), variant = slot.Variant, start, x = starts[start].X, y = starts[start].Y, frames,
                        attack_contact_frames = contacts, body_contact_frames = bodyContacts, stationary = true, invincible = true,
                        scope = "Fresh stationary launch and actual collision shapes; no movement, shooting, bombs or dodge. Human readability and reachable escape paths are evaluated separately." });
                    pool.DespawnAll();
                    await Frames(1);
                    if (start == starts.Length - 1)
                    {
                        System.IO.File.WriteAllText(output + "/stationary.json", System.Text.Json.JsonSerializer.Serialize(results,
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        GD.Print($"[PolishQA] PROBED {scene}/{difficulty}/{phase}/{slot.Kind}/{slot.Variant} cumulative={results.Count}");
                    }
                }
            }
            System.IO.File.WriteAllText(output + "/stationary.json", System.Text.Json.JsonSerializer.Serialize(results,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[PolishQA] STATIONARY {scene}/{difficulty} completed; cumulative={results.Count}");
            root.QueueFree();
            await Frames(3);
        }
    }
    private async Task CheckMidboss(GameManager game, string scene, GameManager.Diff difficulty)
    {
        game.Difficulty = difficulty;
        var (root, stage, world, hud) = Open(game, scene);
        Write(stage, "_lunatic", true);
        Write(stage, "_stepStarted", false);
        Call(stage, "Step_BossCameo", 0d);
        var boss = Read<CameoBoss>(stage, "_cameo");
        Call(boss, "TickEntrance", 0d);
        Call(boss, "TickEntrance", 2d);
        Call(boss, "TickSwapAnim", 1d);
        boss.Position = new Vector2(global::Field.BossCenterX, 100);
        hud.HideBubble();
        Hud.BubblePaused = false;
        Check(boss.UsesTechniqueCycle, $"{scene}/{difficulty}: midboss uses current technique cycle");
        var rotation = Read<BossDanmaku>(boss, "_danmaku");
        var slots = BossAttackCycle.Slots(boss, 0);
        var seen = new System.Collections.Generic.HashSet<ulong>();
        int finished = 0;
        for (int frame = 0; frame < 36000 && finished < slots.Count; frame++)
        {
            rotation.Tick(1d / 60);
            if (rotation.ActiveTechnique is { Finished: false } attack)
            {
                if (seen.Add(attack.GetInstanceId()))
                {
                    var expected = slots[seen.Count - 1];
                    Check(attack.TechniqueKind == expected.Kind && attack.TechniqueVariant == expected.Variant
                        && attack.TechniqueKind != BossTechniqueKind.Cameo, $"{scene}/{difficulty}: selected {attack.SpellName}");
                    Check(!Read<bool>(hud, "_spellCompact"), "Technique announcement uses the original post card");
                    attack.Ended += ended => { if (ended.Completed) finished++; };
                }
                attack.Tick(1d / 60);
                hud._Process(1d / 60);
                if (!attack.Finished)
                    if (hud.ActiveTechniqueNames != attack.SpellName || Read<double>(hud, "_spellTimer") <= 0)
                        throw new Exception("Technique label expired during " + attack.SpellName);
            }
            foreach (var bullet in GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Where(b => b.Active))
            { bullet.SetPhysicsProcess(false); bullet._PhysicsProcess(1d / 60); }
            if (frame % 600 == 599) await Frames(1);
        }
        Check(finished == slots.Count && rotation.CycleRounds == 1 && hud.ActiveTechniqueNames == "",
            $"{scene}/{difficulty}: every new midboss technique completes; names persist and clear at the end");
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        root.QueueFree();
        await Frames(3);
    }
}
