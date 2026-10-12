using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class RoadBalanceQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(object obj, string name)
    {
        for (var t = obj.GetType(); t != null; t = t.BaseType)
            if (t.GetField(name, Private | BindingFlags.DeclaredOnly) is { } f) return f;
        throw new MissingFieldException(name);
    }
    private static T Read<T>(object obj, string name) => (T)Field(obj, name).GetValue(obj)!;
    private static void Write(object obj, string name, object value) => Field(obj, name).SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args)
    {
        for (var t = obj.GetType(); t != null; t = t.BaseType)
            if (t.GetMethod(name, Private | BindingFlags.DeclaredOnly) is { } m) return m.Invoke(obj, args);
        throw new MissingMethodException(name);
    }
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        GD.Print("[RoadQA] PASS " + text);
    }
    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private Bullet[] Bullets() => GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Where(b => b.Active).ToArray();
    private bool Shots => OS.GetCmdlineUserArgs().Contains("--shots");
    private bool Combat => OS.GetCmdlineUserArgs().Contains("--combat");
    private bool Spawns => OS.GetCmdlineUserArgs().Contains("--spawns");
    private bool Health => OS.GetCmdlineUserArgs().Contains("--health");
    private bool Stationary => OS.GetCmdlineUserArgs().Contains("--stationary");

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            game.SelectedJob = Job.Tank;
            await Frames(2);
            foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Mina })
                foreach (var diff in Enum.GetValues<GameManager.Diff>())
                {
                    if (OS.GetCmdlineUserArgs().Contains("--hud") && theme != StageTheme.Akari) continue;
                    if ((Spawns || (Shots && !OS.GetCmdlineUserArgs().Contains("--normal-only"))
                        || OS.GetCmdlineUserArgs().Contains("--hard-only")) && diff != GameManager.Diff.Hard) continue;
                    if (OS.GetCmdlineUserArgs().Contains("--normal-only") && diff != GameManager.Diff.Normal) continue;
                    await Encounter(game, theme, diff);
                }
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[RoadQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError("[RoadQA] FAIL " + e); GetTree().Quit(1); }
    }

    private async Task Encounter(GameManager game, StageTheme theme, GameManager.Diff diff)
    {
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
        GD.Seed(7123);
        game.Difficulty = diff;
        string scene = theme == StageTheme.Mina ? "MinaBattle" : theme.ToString();
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Inherit;
        var player = world.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        player.Position = new Vector2(global::Field.Left + 32, 110);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(3);
        if (OS.GetCmdlineUserArgs().Contains("--hud"))
        {
            Check(UiKit.TextW(UiKit.Mono, game.DiffName, 14) < 104, game.DiffName + " fits before the activity status");
            hud.HoldBubble = true;
            hud.ShowDialog(Hud.LineKind.Mina, "難易度表示の確認", "res://char/mina_face.png");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540) })
            {
                DisplayServer.WindowSetSize(size);
                await Frames(4);
                await Shot($"hud_{game.DiffName}_{size.X}");
            }
            hud.HoldBubble = false;
            hud.HideBubble();
            root.QueueFree();
            await Frames(5);
            return;
        }
        if (Health)
        {
            await HealthChecks(world, player, theme, diff);
            root.QueueFree();
            await Frames(5);
            return;
        }
        if (Spawns)
        {
            await SpawnChecks(game, world, hud, theme);
            root.QueueFree();
            await Frames(5);
            return;
        }
        if (Combat)
        {
            await CombatSample(game, world, player, theme, diff);
            root.QueueFree();
            await Frames(5);
            return;
        }
        foreach (var spec in EnemyTable.CharactersFor(theme))
        {
            var sample = new MidEnemy { ProcessMode = ProcessModeEnum.Disabled };
            sample.Configure(spec);
            world.AddChild(sample);
            foreach (float distance in new[] { 52f, 100f, 180f })
                foreach (float span in new[] { 0f, 8f, 28f, 46f, 70f, 76f })
                {
                    sample.Position = player.Position + Vector2.Right * distance;
                    float pitch = (float)Call(sample, "LanePitch", 3.8f)!;
                    int n = (int)Call(sample, "FanCount", sample.Position, 5, span, 3.8f, true)!;
                    if (n % 2 != 1) throw new Exception("fan lost its aimed center");
                    if (n > 1)
                    {
                        float chord = 2 * distance * Mathf.Sin(Mathf.DegToRad(span / (n - 1)) / 2);
                        if (chord < pitch - 0.001f) throw new Exception("fan gap narrower than silhouette + player clearance");
                    }
                    float widened = (float)Call(sample, "FanSpan", sample.Position, 3, span, 3.8f, true)!;
                    int lanes = (int)Call(sample, "FanCount", sample.Position, 3, widened, 3.8f, true)!;
                    if (diff >= GameManager.Diff.Normal && distance >= 100 && lanes < 3)
                        throw new Exception("aimed fan collapsed into a single bullet");
                    if (lanes > 1 && 2 * distance * Mathf.Sin(Mathf.DegToRad(widened / (lanes - 1)) / 2) < pitch - 0.001f)
                        throw new Exception("widened fan lost its dodge gap");
                }
            sample.QueueFree();
        }
        await Frames(2);
        Check(true, $"{theme}/{diff}: fan silhouettes leave at least {BulletPool.BossLaneGap}px at the aimed distance");
        int highestRoad = 0;
        double earlyAverage = 0;
        foreach (float intensity in new[] { 0f, 0.7f })
        {
            GD.Seed(7123);
            var spawner = new Spawner { World = world, Theme = theme, StartIntensity = intensity,
                IgnoreStageCleared = true, CharactersOnly = true };
            world.AddChild(spawner);
            Read<RandomNumberGenerator>(spawner, "_rng").Seed = 1741;
            spawner.Begin();
            float opening = spawner.Intensity;
            int peak = 0, bodies = 0, activeSalvos = 0;
            double sum = 0;
            int frames = 1800;
            for (int i = 0; i < frames; i++)
            {
                await Frames(1);
                var enemies = GetTree().GetNodesInGroup("enemies").OfType<MidEnemy>().ToArray();
                bodies = Math.Max(bodies, enemies.Length);
                activeSalvos = Math.Max(activeSalvos, enemies.Count(e => e.SalvoActive));
                int count = Bullets().Length;
                peak = Math.Max(peak, count);
                if (i >= 600) sum += count;
                if (Shots && intensity > 0 && i == 1100) await Shot(theme + "_road");
            }
            double average = sum / (frames - 600);
            highestRoad = Math.Max(highestRoad, peak);
            Check(bodies == 5 && bodies <= game.MaxAliveEnemies, $"{theme}/{diff}/{intensity}: unshot enemy cap {bodies}");
            Check(activeSalvos <= (diff >= GameManager.Diff.Hard ? 2 : 1), "concurrent volleys stay within the difficulty limit");
            Check(spawner.Intensity > opening && spawner.Intensity <= intensity + 0.16f, "pressure ramps gradually within the wave");
            GD.Print($"[RoadQA] SAMPLE {theme}/{diff}/{intensity}: peak={peak} average={average:F2} bodies={bodies}");
            if (intensity == 0) earlyAverage = average;
            else Check(average >= earlyAverage * 0.9, $"{theme}/{diff}: later wave is not substantially weaker");
            spawner.Stop();
            foreach (Node e in GetTree().GetNodesInGroup("enemies")) e.QueueFree();
            spawner.QueueFree();
            foreach (Node e in GetTree().GetNodesInGroup("aoe")) e.QueueFree();
            Pool.DespawnAll();
            await Frames(5);
        }
        Enemy boss = theme switch { StageTheme.Akari => new BossAkari(), StageTheme.Koharu => new BossKoharu(), StageTheme.Rei => new BossRei(), _ => new BossMina() };
        boss.ProcessMode = ProcessModeEnum.Disabled;
        world.AddChild(boss);
        boss.Position = new Vector2(global::Field.Right - 66, 100);
        Call(boss, "TickEntrance", 0d);
        Call(boss, "TickEntrance", 100d);
        Call(boss, "TickSwapAnim", 1d);
        var barrage = Read<BossDanmaku>(boss, "_danmaku");
        int bossPeak = 0;
        for (int i = 0; i < 1200; i++)
        {
            barrage.Tick(1d / 60);
            await Frames(1);
            bossPeak = Math.Max(bossPeak, Bullets().Length);
        }
        GD.Print($"[RoadQA] COMPARE {theme}/{diff}: road={highestRoad} boss={bossPeak}");
        Check(highestRoad < bossPeak, "unshot road stays below boss-only barrage peak");
        Pool.DespawnAll();
        root.QueueFree();
        await Frames(5);
    }

    private async Task CombatSample(GameManager game, Node2D world, Player player, StageTheme theme, GameManager.Diff diff)
    {
        player.SetPhysicsProcess(true);
        var spawner = new Spawner { World = world, Theme = theme, IgnoreStageCleared = true,
            CharactersOnly = theme == StageTheme.Mina };
        world.AddChild(spawner);
        Read<RandomNumberGenerator>(spawner, "_rng").Seed = 1741;
        spawner.Begin();
        var input = new AutoplayInput(GetTree(), game);
        double elapsed = 0, sampled = 0, sum = 0, empty = 0;
        double nextHit = 0;
        int contacts = 0;
        int peak = 0, bodies = 0, playerBullets = 0;
        try
        {
            while (elapsed < 40)
            {
                input.Update(GetProcessDeltaTime(), bombs: false, dodge: false);
                if (Stationary) { AutoplayInput.Move(Vector2.Zero); Input.FlushBufferedEvents(); }
                await Frames(1);
                // Hitstop uses real time, so a fixed frame count can represent only seconds of actual combat in headless runs.
                double dt = GetProcessDeltaTime();
                elapsed += dt;
                int count = Bullets().Length;
                if (elapsed >= nextHit && Bullets().Any(b => b.CollisionLayer != 0
                    && BossCharacterShotQa.HitsPlayer(b, player.GlobalPosition)))
                { contacts++; nextHit = elapsed + game.JobDef.HitInvulSec; }
                if (Shots && elapsed >= 15 && elapsed - dt < 15) await Shot(theme + "_combat");
                peak = Math.Max(peak, count);
                bodies = Math.Max(bodies, GetTree().GetNodesInGroup("enemies").Count);
                playerBullets = Math.Max(playerBullets, GetTree().GetNodesInGroup("player_bullets").Count);
                if (elapsed < 10) continue;
                sampled += dt;
                sum += count * dt;
                if (count == 0) empty += dt;
            }
        }
        finally { input.Release(); }
        Check(bodies <= game.MaxAliveEnemies, $"{theme}/{diff}: shooting enemy cap {bodies}");
        Check(playerBullets > 0, "player fire is active during the sample");
        GD.Print($"[RoadQA] COMBAT {theme}/{diff}: peak={peak} average={sum / sampled:F2} empty={100.0 * empty / sampled:F1}% contacts={contacts} seconds={elapsed:F1}");
        if (Stationary) Check(contacts >= 3, "standing still while firing repeatedly gets hit");
        if (diff == GameManager.Diff.Normal)
            Check(sum / sampled >= 18 && empty < sampled * 0.15, $"{theme}: NORMAL sustains multiple bullet rows during shooting");
        if (diff == GameManager.Diff.Hard)
            Check(sum / sampled >= 25 && empty < sampled * 0.10, $"{theme}: HARD maintains dense attacks while the player shoots");
        spawner.Stop();
        player.SetPhysicsProcess(false);
        Pool.DespawnAll();
        while (Engine.TimeScale < 0.99) await Frames(1);
    }

    private async Task HealthChecks(Node2D world, Player player, StageTheme theme, GameManager.Diff diff)
    {
        foreach (var spec in EnemyTable.CharactersFor(theme))
        {
            Pool.DespawnAll();
            var enemy = new MidEnemy { Position = new Vector2(310, 90), ProcessMode = ProcessModeEnum.Disabled };
            enemy.Configure(spec);
            world.AddChild(enemy);
            var panels = enemy.GetChildren().OfType<Panel>().ToArray();
            Check(panels.Length == 3 && panels.All(p => p.Ink > 4 && p.CanLock)
                && !enemy.HasHpBar && enemy.CollisionMask == 0,
                $"{spec.Pattern}/{diff}: three restored unfolders with individual HP and no body gauge");
            var panel = panels[0];
            int hp = Read<int>(panel, "_maxInk");
            var before = panel.Position;
            panel._PhysicsProcess(0.25);
            Check(panel.Position != before && enemy.SelectUnfolder(player.Position, panel) == panel,
                "unfolders retain orbit motion and lock-on targeting");
            for (int hit = 0; hit < 4; hit++)
            {
                var normal = Pool.Spawn(player.Position, Vector2.Right, false, 3, 1);
                Call(panel, "OnAreaEntered", normal);
                Check(!normal.Active, "unfolder intercepts the player shot");
            }
            Check(panel.Ink == hp - 4 && panel.CanLock && !enemy.IsPurified,
                "four ordinary hits no longer destroy an unfolder");
            int chargeDamage = Math.Min(12, hp - 5);
            var bullet = Pool.Spawn(player.Position, Vector2.Right, false, 3, chargeDamage);
            bullet.MakeCharged(Job.Tank);
            bullet.Pierce = 2;
            Call(panel, "OnAreaEntered", bullet);
            Call(panel, "OnAreaEntered", bullet);
            Check(panel.Ink == hp - 4 - chargeDamage && bullet.Pierce == 1 && panels[1].Ink == hp,
                "charge damage counts once per unfolder and leaves other gauges unchanged");
            panels[1].WeakenByRipple();
            Check(panels[1].Ink == hp - 6 && panels[1].CanLock && !enemy.IsPurified,
                "ripple chips the touched unfolder instead of deleting it");
            var finisher = Pool.Spawn(player.Position, Vector2.Right, false, 3, panel.Ink);
            Call(panel, "OnAreaEntered", finisher);
            Check(!panel.CanLock && !enemy.IsPurified && panels.Skip(1).All(p => p.CanLock),
                "only the depleted unfolder breaks; the others remain");
            Pool.DespawnAll();
            Call(enemy, "BeginCharacterAttack");
            int salvos = Read<int>(enemy, "_salvoRemaining");
            Check(salvos >= 2, "every character attack repeats at least twice");
            Call(enemy, "FireCharacterSalvo");
            int count = Bullets().Length;
            if (diff >= GameManager.Diff.Normal) Check(count >= 3, $"{spec.Pattern}: {count} bullets in one salvo");
            Check(Bullets().All(b => b.RoadPending && b.CollisionLayer == 0), "road bullets are harmless during deployment and warning");
            var shot = Bullets()[0];
            shot.SetPhysicsProcess(false);
            shot.CancelLeadIn();
            player.Position = shot.Position;
            shot._PhysicsProcess(0.2);
            Check(shot.CollisionLayer == 0 && shot.Position == player.Position, "spawn-overlap stays harmless during the warning");
            shot._PhysicsProcess(0.3);
            shot._PhysicsProcess(1d / 60);
            Check(shot.CollisionLayer == 0, "warning expiry cannot damage an overlapping player");
            player.Position = new Vector2(100, 190);
            shot._PhysicsProcess(1d / 60);
            Check(!shot.RoadPending && shot.CollisionLayer == 8, "bullet arms only after clearing the player");
            Pool.DespawnAll();
            Call(enemy, "FireCharacterSalvo");
            var pending = Bullets().ToArray();
            enemy.Purify();
            foreach (var b in pending) { b.SetPhysicsProcess(false); b.CancelLeadIn(); b._PhysicsProcess(1); }
            Check(enemy.IsPurified && panels.All(p => !p.CanLock) && pending.All(b => !b.Active),
                "bomb breaks remaining unfolders and cancels unlaunched shots");
            enemy.QueueFree();
            await Frames(3);
            player.Position = new Vector2(100, 110);
        }
    }

    private async Task SpawnChecks(GameManager game, Node2D world, Hud hud, StageTheme theme)
    {
        var spawner = new Spawner { World = world, Theme = theme, IgnoreStageCleared = true,
            CharactersOnly = true, StartIntensity = 0.7f };
        world.AddChild(spawner);
        spawner.SetProcess(false);
        EnemySpawnWarning[] Warnings() => world.GetChildren().OfType<EnemySpawnWarning>()
            .Where(n => !n.IsQueuedForDeletion()).ToArray();
        int Pending() => ((System.Collections.ICollection)Field(spawner, "_pending").GetValue(spawner)!).Count;
        var edgeType = typeof(Spawner).GetNestedType("Edge", BindingFlags.NonPublic)!;
        var entries = new[] { ("Right", new Vector2(global::Field.Right + 14, 86)),
            ("Top", new Vector2(250, global::Field.Top - 12)),
            ("Bottom", new Vector2(294, global::Field.Bottom + 12)),
            ("Left", new Vector2(global::Field.Left - 16, 136)) };
        foreach (var (edge, pos) in entries)
        {
            var enemy = new MidEnemy { RoadDirector = spawner, ProcessMode = ProcessModeEnum.Disabled };
            enemy.Configure(EnemyTable.CharactersFor(theme)[0]);
            enemy.SetEntry(new Vector2(280, 108));
            Call(spawner, "QueueSpawn", enemy, pos, Enum.Parse(edgeType, edge));
            var warning = Warnings().Last();
            Check(warning.Position.X >= global::Field.Left + 8 && warning.Position.X <= global::Field.Right - 8
                && warning.Position.Y >= 8 && warning.Position.Y <= global::Field.Bottom - 8, edge + " warning is inside the visible field");
            Check(edge switch
            {
                "Right" => warning.Position.X == global::Field.Right - 8 && warning.Position.Y == pos.Y,
                "Left" => warning.Position.X == global::Field.Left + 8 && warning.Position.Y == pos.Y,
                "Top" => warning.Position.Y == global::Field.Top + 8 && warning.Position.X == pos.X,
                _ => warning.Position.Y == global::Field.Bottom - 8 && warning.Position.X == pos.X,
            }, edge + " warning lines up with its actual entry boundary");
            Vector2 inward = Vector2.Right.Rotated(warning.Rotation);
            Check(inward.Dot(new Vector2(252, 108) - warning.Position) > 0, edge + " chevrons face into the field");
        }
        spawner._Process(0.4);
        Check(spawner.SpawnedCount == 0 && Warnings().Length == 4, "every edge warns before the enemy appears");
        float progress = Read<float>(Warnings()[0], "_progress");
        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
        await Frames(3);
        spawner._Process(2);
        Check(spawner.SpawnedCount == 0 && Read<float>(Warnings()[0], "_progress") == progress,
            "dialogue freezes both warning and spawn clocks");
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(3);
        if (Shots) await Shot(theme + "_entry_warnings");
        spawner._Process(0.51);
        Check(spawner.SpawnedCount == 4 && Warnings().Length == 0, "warnings end on the exact spawn frame");
        var actual = world.GetChildren().OfType<MidEnemy>().ToArray();
        Check(entries.All(e => actual.Any(n => n.Position == e.Item2)), "all four enemies appear at their announced entrances");
        foreach (var enemy in actual) enemy.QueueFree();
        await Frames(2);

        spawner.SpawnLimit = 5;
        spawner.Begin();
        bool pairSeen = false;
        for (int i = 0; i < 18; i++)
        {
            int previous = spawner.SpawnedCount;
            spawner._Process(0.5);
            var enemies = world.GetChildren().OfType<MidEnemy>().ToArray();
            foreach (var enemy in enemies) enemy.SetProcess(false);
            if (spawner.SpawnedCount - previous == 2)
            {
                var pair = enemies.TakeLast(2).ToArray();
                Check(Mathf.IsEqualApprox(pair[0].Position.X, pair[1].Position.X) && Mathf.Abs(pair[0].Position.Y - pair[1].Position.Y) >= 71.99f,
                    "paired shooters enter together with separated lanes");
                if (!pairSeen)
                    Check(EnemyTable.CharactersFor(theme).All(s => enemies.Any(e => Read<EnemySpec>(e, "_spec").Pattern == s.Pattern)),
                        "the first three enemies still introduce the complete character roster");
                else Check(Read<EnemySpec>(pair[0], "_spec").Pattern == Read<EnemySpec>(pair[1], "_spec").Pattern,
                    "subsequent paired shooters share one coherent attack motif");
                pairSeen = true;
            }
            Check(enemies.Length + Pending() <= game.MaxAliveEnemies, "live enemies plus reservations respect the cap");
            await Frames(1);
        }
        Check(pairSeen && spawner.SpawnedCount == 5 && Pending() == 0 && !spawner.Active,
            theme + ": pairs fulfill an odd spawn limit without overflow or canceled final warnings");
        foreach (Node enemy in GetTree().GetNodesInGroup("enemies")) enemy.QueueFree();
        await Frames(2);
        spawner.Begin();
        spawner._Process(0.7);
        Check(Pending() > 0, "cancel test has a live warning");
        spawner.Stop();
        spawner._Process(5);
        Check(Pending() == 0 && Warnings().Length == 0 && spawner.SpawnedCount == 0, "stopping cancels both warning and reserved enemy");

        foreach (float intensity in new[] { 0f, 0.7f })
        {
            int previousPairs = 0;
            foreach (var diff in Enum.GetValues<GameManager.Diff>())
            {
                game.Difficulty = diff;
                spawner.StartIntensity = intensity;
                Write(spawner, "_t", 0d);
                int pairs = 0;
                for (int i = 0; i < 12; i++)
                {
                    Write(spawner, "_formationIndex", i);
                    if ((bool)Call(spawner, "WantsPair", game)!) pairs++;
                }
                Check(pairs >= previousPairs && (diff != GameManager.Diff.Easy || pairs == 0),
                    $"{diff}/{intensity}: {pairs}/12 formations are paired, increasing with difficulty");
                previousPairs = pairs;
            }
        }
        game.Difficulty = GameManager.Diff.Hard;
        spawner.QueueFree();
        await Frames(2);
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute("res://build/qa_story/road_balance_2_064");
        Check(frame.SavePng($"res://build/qa_story/road_balance_2_064/{name}.png") == Error.Ok, name + " screenshot");
    }
}
