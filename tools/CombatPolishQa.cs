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
            if (OS.GetCmdlineUserArgs().Contains("--midboss-roster"))
            {
                await Frames(2);
                foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
                    foreach (string scene in new[] { "Akari", "Koharu", "Rei" }) await CheckMidboss(game, scene, difficulty);
                foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
                { audio.Stop(); audio.Stream = null; }
                await Task.Delay(250);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Frames(5);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--player-protection"))
            {
                await Frames(2);
                await CheckPlayerProtection(game);
                foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
                { audio.Stop(); audio.Stream = null; }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Frames(5);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--bomb-shields"))
            {
                await Frames(2);
                await CheckBombShields(game);
                foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
                { audio.Stop(); audio.Stream = null; }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Frames(5);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--gaze-steps"))
            {
                await Frames(2);
                await CheckGazeSteps(game);
                foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
                { audio.Stop(); audio.Stream = null; }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Frames(5);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--stationary-contacts") || OS.GetCmdlineUserArgs().Contains("--counterplay-probes"))
            {
                await Frames(2);
                await CheckStationaryContacts(game);
                foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
                { audio.Stop(); audio.Stream = null; }
                await Task.Delay(250);
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
            await Frames(2);
            foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
                await CheckRoad(game, scene);
            if (OS.GetCmdlineUserArgs().Contains("--stage-progress"))
            {
                GetTree().Quit();
                return;
            }
            foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
                foreach (string scene in new[] { "Akari", "Koharu", "Rei" })
                    await CheckMidboss(game, scene, difficulty);
            GD.Print("[PolishQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("[PolishQA] FAIL " + error); GetTree().Quit(1); }
    }
    private async Task CheckPlayerProtection(GameManager game)
    {
        bool shots = OS.GetCmdlineUserArgs().Contains("--protection-shots");
        string output = "res://build/qa_story/player_protection_20261020";
        if (shots)
        {
            DirAccess.MakeDirRecursiveAbsolute(output);
            GetWindow().Mode = Window.ModeEnum.Windowed;
            GetWindow().Size = new Vector2I(1280, 720);
        }
        foreach (var job in Jobs.All)
        {
            game.ResetPersistent();
            game.Difficulty = GameManager.Diff.Normal;
            game.SelectedJob = job.Id;
            game.ResetRun();
            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.ProcessMode = ProcessModeEnum.Disabled;
            var player = root.GetNode<Player>("World/Player");
            var hud = root.GetNode<Hud>("Hud");
            var sprite = player.GetNode<Sprite2D>("Sprite");
            hud.HideBubble();
            Hud.BubblePaused = false;
            Pad.ReleaseMouseFollow();
            player.GlobalPosition = new Vector2(global::Field.CenterX, 126f);

            void Tick(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    player._PhysicsProcess(1d / 60d);
                    hud._Process(1d / 60d);
                    FxLayer.Instance?._Process(1d / 60d);
                }
            }
            async Task Capture(string name)
            {
                if (!shots || job.Id != Job.Tank) return;
                player.GetNode<PlayerHitDot>("HitDot").QueueRedraw();
                await Frames(3);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng($"{output}/{name}.png") == Error.Ok, "captured " + name);
            }

            Tick(95);
            int lives = player.Lives, bombs = game.Bombs;
            await Capture("01_normal");
            Call(player, "TryBomb");
            Check(player.Lives == lives && game.Bombs == bombs - 1, $"{job.Id}: bomb consumes only a bomb");
            Check(Read<double>(hud, "_lifeLossNotice") == 0 && Read<double>(hud, "_hurtEdge") == 0,
                $"{job.Id}: bomb has no damage notification");
            Check(sprite.Visible && Read<float>(GameCamera.Instance, "_shakeT") == 0,
                $"{job.Id}: bomb keeps the body visible without damage shake");
            Tick(8);
            await Capture("02_bomb");
            if (shots && job.Id == Job.Tank)
            {
                Vector2 center = player.GlobalPosition;
                foreach (float x in new[] { global::Field.Left + 12f, global::Field.Right - 12f })
                {
                    player.GlobalPosition = new Vector2(x, global::Field.Bottom - 12f);
                    await Capture($"02_bomb_edge_{x:0}");
                }
                player.GlobalPosition = center;
            }
            bool protectedContact = true;
            for (int frame = 0; frame < 52; frame++)
            {
                player.TakeHit();
                protectedContact &= player.Lives == lives && sprite.Visible;
                Tick(1);
            }
            Check(protectedContact, $"{job.Id}: body stays visible and contacts cause no damage during bomb protection");
            Check(player.InvincibleRemaining > 0 && player.InvincibleRemaining < .3f, $"{job.Id}: protection countdown");
            await Capture("03_bomb_ending");
            Tick(15);
            Check(player.InvincibleRemaining == 0, $"{job.Id}: bomb protection expires");
            await Capture("04_bomb_expired");
            player.TakeHit();
            Check(player.Lives == lives - 1 && Read<int>(hud, "_lifeLostAmount") == 1
                && Read<double>(hud, "_lifeLossNotice") > 0, $"{job.Id}: real damage removes and reports one life");
            Check(Mathf.IsEqualApprox(player.InvincibleRemaining, job.HitInvulSec), $"{job.Id}: hit protection duration unchanged");
            Tick(5);
            await Capture("05_hit");
            player.TakeHit();
            Check(player.Lives == lives - 1, $"{job.Id}: no second hit during recovery");
            Call(player, "TryBomb");
            Check(sprite.Visible && player.Lives == lives - 1 && Read<double>(hud, "_lifeLossNotice") > 0,
                $"{job.Id}: bomb after damage preserves the actual life-loss notice");
            await Capture("06_hit_then_bomb");
            Tick(80);
            Check(player.InvincibleRemaining == 0 && sprite.Visible, $"{job.Id}: normal visibility restored");
            int casts = player.BombCount;
            Call(player, "TryBomb");
            Check(player.BombCount == casts && player.InvincibleRemaining == 0, $"{job.Id}: empty bomb grants no protection");
            await ToSignal(GetTree().CreateTimer(.12, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
            root.QueueFree();
            await Frames(3);
            GetNode<BulletPool>("/root/Pool").DespawnAll();
        }
        GD.Print("[PolishQA] PLAYER PROTECTION ALL PASS");
    }

    private async Task CheckBombShields(GameManager game)
    {
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle", "Cameo" })
        {
            game.Difficulty = difficulty;
            var (root, stage, world, hud) = Open(game, scene == "Cameo" ? "Rei" : scene);
            Enemy boss;
            if (scene == "Cameo")
            {
                Write(stage, "_lunatic", true);
                Write(stage, "_stepStarted", false);
                Call(stage, "Step_BossCameo", 0d);
                boss = Read<CameoBoss>(stage, "_cameo");
            }
            else
            {
                boss = scene switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(),
                    "Rei" => new BossRei(), _ => new BossMina() };
                world.AddChild(boss);
            }
            Call(boss, "TickEntrance", 0d);
            Call(boss, "TickEntrance", 2d);
            Call(boss, "TickSwapAnim", 1d);
            boss.SetPanelsInvulnerable(false);
            var panels = Read<System.Collections.Generic.List<Panel>>(boss, "_panels").ToArray();
            var ink = panels.Select(p => p.Ink).ToArray();
            Check(panels.Length > 0 && ink.All(value => value > 1), $"{scene}/{difficulty}: intact shields");
            boss.Purify();
            Check(panels.Select((p, i) => p.Ink > 0 && p.Ink < ink[i] && !p.IsQueuedForDeletion()).All(value => value),
                $"{scene}/{difficulty}: one bomb damages without clearing intact shields");
            boss.SetPanelsInvulnerable(true);
            var after = panels.Select(p => p.Ink).ToArray();
            boss.Purify();
            Check(panels.Select((p, i) => p.Ink == after[i]).All(value => value), $"{scene}/{difficulty}: invulnerable shields preserved");
            boss.SetPanelsInvulnerable(false);
            foreach (var panel in panels) panel.Ink = 1;
            boss.Purify();
            Check(panels.All(p => p.IsQueuedForDeletion()), $"{scene}/{difficulty}: weakened shields still break");
            root.QueueFree();
            await Frames(3);
        }
        GD.Print("[PolishQA] BOMB SHIELDS ALL PASS");
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
        string method = mina ? "Step_Route" : "Step_MidwaveC";
        object[] args = mina ? Array.Empty<object>() : new object[] { 0d };
        int step = scene switch { "Akari" => 9, "Koharu" => 10, "Rei" => 8, _ => 2 };
        Write(stage, "_step", step);
        Write(stage, "_stepStarted", false);
        Call(stage, method, args);
        if (mina) Write(stage, "_wave", 2);
        var spawner = Read<Spawner>(stage, mina ? "_echoSpawner" : "_spawner");
        spawner.Stop();
        var enemy = new MidEnemy { Position = new Vector2(global::Field.CenterX, 70) };
        enemy.Configure(EnemyTable.PrayerCarrier());
        world.AddChild(enemy);
        while (!game.SpawnTargetReached) game.AddPurify(0);
        Check(!game.StageCleared && Mathf.RoundToInt(game.StageProgress * 100) == 89, scene + ": last enemy still on field, road capped at 89%");
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
        Check(!game.StageCleared && game.StageProgress == .90f, scene + ": final wave resolved, boss approach at 90%");
        pool.DespawnAll();
        root.QueueFree();
        await Frames(3);
    }
    private async Task CheckGazeSteps(GameManager game)
    {
        string output = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--output="))[9..];
        System.IO.Directory.CreateDirectory(output);
        var results = new System.Collections.Generic.List<object>();
        using var core = new CircleShape2D { Radius = Player.HitRadius };
        foreach (var difficulty in new[] { GameManager.Diff.Hard, GameManager.Diff.Lunatic })
        {
            game.Difficulty = difficulty;
            var (root, stage, world, hud) = Open(game, "Koharu");
            var player = root.GetNode<Player>("World/Player");
            var boss = new BossKoharu();
            world.AddChild(boss);
            Call(boss, "TickEntrance", 0d);
            Call(boss, "TickEntrance", 2d);
            Call(boss, "TickSwapAnim", 1d);
            boss.SetPanelsInvulnerable(true);
            game.EndFury();
            hud.HideBubble();
            Hud.BubblePaused = false;
            Pad.ReleaseMouseFollow();
            var starts = new[] { new Vector2(global::Field.CenterX, 166),
                global::Field.Rect.Position + Vector2.One * 4, global::Field.Rect.End - Vector2.One * 4,
                new Vector2(global::Field.Left + 4, global::Field.Bottom - 4),
                new Vector2(global::Field.Right - 4, global::Field.Top + 4),
                new Vector2(global::Field.Left + 4, global::Field.CenterY), new Vector2(global::Field.Right - 4, global::Field.CenterY) };
            foreach (int phase in new[] { 0, 1 })
            foreach (int seed in new[] { 2081, 5167 })
            foreach (string policy in new[] { "follow_eyes", "hold_entry", "hold_seam", "enter_early", "skip_step" })
            for (int start = 0; start < starts.Length; start++)
            {
                GD.Seed((ulong)seed);
                Write(boss, "_form2", phase != 0);
                boss.GlobalPosition = new Vector2(global::Field.BossCenterX, global::Field.BossZoneCenterY);
                player.GlobalPosition = starts[start];
                Write(player, "_invincibleTimer", 99999f);
                var attack = (KoharuBossTechnique)BossCharacterTechnique.Begin(boss, BossTechniqueKind.Area, 1);
                Vector2 target = attack.GazeEntry;
                int contacts = 0, bodyContacts = 0, transitions = 0;
                double last = -1;
                float maximumMove = 0;
                int columns = difficulty == GameManager.Diff.Hard ? 8 : 6;
                int[] staticHits = new int[difficulty == GameManager.Diff.Hard ? 48 : 24];
                for (int frame = 0; frame < 7200 && !attack.Finished; frame++)
                {
                    if (attack.VisibleGazeStep is { } cue)
                    {
                        var from = attack.GazeCell(cue.From);
                        var to = attack.GazeCell(cue.To);
                        if (cue.StartsAt != last)
                        {
                            Check(Math.Abs(cue.From % columns - cue.To % columns) + Math.Abs(cue.From / columns - cue.To / columns) == 1,
                                "Gaze transition stays adjacent");
                            transitions++;
                            last = cue.StartsAt;
                        }
                        if (policy == "follow_eyes" && attack.Elapsed >= cue.OpensAt + .2) target = to.GetCenter();
                        if (policy == "hold_seam" && transitions == 1 && attack.Elapsed >= cue.OpensAt + .2)
                            target = from.GetCenter().Lerp(to.GetCenter(), .5f);
                        if (policy == "enter_early") target = to.GetCenter();
                        if (policy == "skip_step" && attack.Elapsed >= cue.OpensAt + .2)
                            target = (to.GetCenter() * 2 - from.GetCenter()).Clamp(global::Field.Rect.Position + Vector2.One * 4,
                                global::Field.Rect.End - Vector2.One * 4);
                    }
                    Vector2 before = player.GlobalPosition;
                    Vector2 move = (target - before).LimitLength(player.SlowestMoveSpeed / 60);
                    AutoplayInput.Move(move / (player.MoveSpeed / 60));
                    Input.FlushBufferedEvents();
                    player._PhysicsProcess(1d / 60);
                    maximumMove = Math.Max(maximumMove, before.DistanceTo(player.GlobalPosition));
                    attack.Tick(1d / 60);
                    if (attack.CoversPoint(player.GlobalPosition)) contacts++;
                    if (boss.GetChildren().OfType<CollisionShape2D>().Any(shape => !shape.Disabled
                        && shape.Shape.Collide(shape.GlobalTransform, core, new Transform2D(0, player.GlobalPosition)))) bodyContacts++;
                    for (int i = 0; i < staticHits.Length; i++) if (attack.CoversPoint(attack.GazeCell(i).GetCenter())) staticHits[i]++;
                    if (frame % 600 == 599) await Frames(1);
                }
                AutoplayInput.Move(Vector2.Zero);
                Input.FlushBufferedEvents();
                bool intended = policy == "follow_eyes";
                Check(attack.Completed && transitions == (6 + (int)difficulty + phase * 2) * (difficulty == GameManager.Diff.Hard ? 2 : 1), "Every gaze handoff completes");
                Check(intended ? contacts == 0 && bodyContacts == 0 : contacts > 0,
                    $"{difficulty}/{phase}/{seed}/{policy}/{start}: contacts={contacts}, body={bodyContacts}");
                Check(staticHits.All(hits => hits > 0), "No permanently safe gaze panel");
                Check(maximumMove <= player.SlowestMoveSpeed / 60 + .02f,
                    $"Gaze movement uses normal speed: maximum={maximumMove}, allowed={player.SlowestMoveSpeed / 60}");
                results.Add(new { difficulty = difficulty.ToString(), phase, seed, policy, start, contacts, bodyContacts,
                    transitions, maximumMove, invincible = true, input = "ordinary movement from visible eye states, 0.2 second reaction delay; no future route access" });
                await Frames(1);
            }
            root.QueueFree();
            await Frames(3);
        }
        System.IO.File.WriteAllText(output + "/gaze_steps.json", System.Text.Json.JsonSerializer.Serialize(results,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task CheckStationaryContacts(GameManager game)
    {
        bool cameo = OS.GetCmdlineUserArgs().Contains("--cameo");
        bool moving = OS.GetCmdlineUserArgs().Contains("--counterplay-probes");
        string[] policies = moving ? new[] { "perimeter", "retreat_top", "retreat_bottom" } : new[] { "stationary" };
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
        if (OS.GetCmdlineUserArgs().Contains("--dense-edge-probes"))
            starts = Enumerable.Range(0, 81).SelectMany(i => new[] {
                new Vector2(global::Field.Left + global::Field.Width * i / 80, global::Field.Top),
                new Vector2(global::Field.Left + global::Field.Width * i / 80, global::Field.Bottom),
                new Vector2(global::Field.Left, global::Field.Top + global::Field.Height * i / 80),
                new Vector2(global::Field.Right, global::Field.Top + global::Field.Height * i / 80),
            }).Distinct().Prepend(starts[0]).ToArray();
        if (moving) starts = new[] { starts[0], starts[1], starts[2] };
        var results = new System.Collections.Generic.List<object>();
        string output = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--output="))[9..];
        System.IO.Directory.CreateDirectory(output);
        string? selected = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--boss="))?[7..];
        string? selectedDifficulty = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--difficulty="))?[13..];
        string? selectedSlot = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--only="))?[7..];
        foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" }.Where(s => (!cameo || s != "MinaBattle") && (selected == null || s == selected)))
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>().Where(d => selectedDifficulty == null || d.ToString() == selectedDifficulty))
        {
            game.Difficulty = difficulty;
            var (root, stage, world, hud) = Open(game, scene);
            var player = root.GetNode<Player>("World/Player");
            Write(player, "_invincibleTimer", 99999f);
            Enemy boss = cameo ? SpawnMidboss(stage) : scene switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(), "Rei" => new BossRei(), _ => new BossMina() };
            if (!cameo) world.AddChild(boss);
            Call(boss, "TickEntrance", 0d);
            Call(boss, "TickEntrance", 2d);
            Call(boss, "TickSwapAnim", 1d);
            boss.SetPanelsInvulnerable(true);
            game.EndFury();
            hud.HideBubble();
            Hud.BubblePaused = false;
            if (cameo)
            {
                starts = new[] { global::Field.CenterX, global::Field.Left + 4, global::Field.Right - 4 }
                    .SelectMany(x => new[] { global::Field.CenterY, global::Field.Top + 4, global::Field.Bottom - 4 }
                        .Select(y => new Vector2(x, y))).ToArray();
                for (int frame = 0; frame < 180 && player.ReturningToStart; frame++) player._PhysicsProcess(1d / 60);
            }
            int phases = cameo ? 1 : boss is BossMina ? 5 : 2;
            for (int phase = 0; phase < phases; phase++)
            {
                if (boss is BossMina) Write(boss, "_pattern", phase);
                else Write(boss, "_form2", phase != 0);
                var slots = (cameo ? BossAttackCycle.Slots(boss, phase) : BossAttackCycle.EasySlots(boss, phase)).Distinct().ToList();
                if (phase == 1 && boss is BossKoharu or BossRei) slots.Add(new(BossTechniqueKind.Finale));
                foreach (var slot in slots.Where(slot => selectedSlot == null || selectedSlot.Split(',').Contains($"{phase}:{slot.Kind}:{slot.Variant}")))
                foreach (string policy in policies)
                for (int start = 0; start < starts.Length; start++)
                {
                    player.GlobalPosition = starts[start];
                    Write(player, "_fireCooldown", 99999f);
                    boss.GlobalPosition = new Vector2(global::Field.BossCenterX, global::Field.BossZoneCenterY);
                    if (boss is BossMina mina && mina.IsDragonForm) boss.GlobalPosition = mina.KeepDragonInField(boss.GlobalPosition);
                    var attack = BossCharacterTechnique.Begin(boss, slot.Kind, slot.Variant);
                    string name = attack.SpellName, mechanic = attack.MechanicKey;
                    bool passiveWind = mechanic == "mina.wing-displacement" && (int)difficulty >= 2;
                    int contacts = 0, bodyContacts = 0, frames = 0;
                    int corner = start % 4;
                    Vector2[] perimeter = { global::Field.Rect.Position + Vector2.One * 4,
                        new(global::Field.Right - 4, global::Field.Top + 4), global::Field.Rect.End - Vector2.One * 4,
                        new(global::Field.Left + 4, global::Field.Bottom - 4) };
                    var bodyShapes = boss.GetChildren().OfType<CollisionShape2D>().ToArray();
                    for (; frames < 10800 && !attack.Finished; frames++)
                    {
                        if (moving)
                        {
                            if (player.GlobalPosition.DistanceTo(perimeter[corner]) < 2) corner = (corner + 1) % 4;
                            Vector2 target = policy == "perimeter" ? perimeter[corner]
                                : new Vector2(starts[start].X, policy == "retreat_top" ? global::Field.Top + 4 : global::Field.Bottom - 4);
                            Vector2 move = (target - player.GlobalPosition).LimitLength(player.SlowestMoveSpeed / 60);
                            AutoplayInput.Move(move / (player.MoveSpeed / 60));
                            Input.FlushBufferedEvents();
                            Pad.ReleaseMouseFollow();
                            player._PhysicsProcess(1d / 60);
                        }
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
                    if (moving) { AutoplayInput.Move(Vector2.Zero); Input.FlushBufferedEvents(); }
                    if (!attack.Finished || !attack.Completed || !moving && !passiveWind && player.GlobalPosition.DistanceTo(starts[start]) > .01f)
                        throw new Exception($"Stationary probe failed: {scene}/{difficulty}/{phase}/{name}/{start}");
                    results.Add(new { scene, difficulty = difficulty.ToString(), phase, name, mechanic,
                        kind = slot.Kind.ToString(), variant = slot.Variant, start, x = starts[start].X, y = starts[start].Y, frames,
                        attack_contact_frames = contacts, body_contact_frames = bodyContacts,
                        rally_boss_damage_events = (attack as ReiBossTechnique)?.RallyDamageEvents ?? 0,
                        rally_failures = (attack as ReiBossTechnique)?.RallyFailures ?? 0,
                        rally_miss_retaliations = (attack as ReiBossTechnique)?.RallyMissRetaliations ?? 0,
                        policy = !moving && passiveWind ? "no_input_wind" : policy, stationary = !moving && !passiveWind, invincible = true,
                        automatic_fire = false,
                        scope = "Fresh launch and actual collision shapes; ordinary movement input for blind policies, no shooting, bombs or dodge. Intended counterplay and human readability are evaluated separately." });
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
    private static CameoBoss SpawnMidboss(Node stage)
    {
        Write(stage, "_lunatic", true);
        Write(stage, "_stepStarted", false);
        Call(stage, "Step_BossCameo", 0d);
        return Read<CameoBoss>(stage, "_cameo");
    }
    private async Task CheckMidboss(GameManager game, string scene, GameManager.Diff difficulty)
    {
        game.Difficulty = difficulty;
        var (root, stage, world, hud) = Open(game, scene);
        var boss = SpawnMidboss(stage);
        Call(boss, "TickEntrance", 0d);
        Call(boss, "TickEntrance", 2d);
        Call(boss, "TickSwapAnim", 1d);
        boss.Position = new Vector2(global::Field.BossCenterX, 100);
        hud.HideBubble();
        Hud.BubblePaused = false;
        Check(boss.UsesTechniqueCycle, $"{scene}/{difficulty}: midboss uses current technique cycle");
        var rotation = Read<BossDanmaku>(boss, "_danmaku");
        var slots = BossAttackCycle.Slots(boss, 0);
        Check(slots.Count == 2 && slots.SequenceEqual(new[] { new BossAttackCycle.Slot(BossTechniqueKind.Cameo, 0),
            new BossAttackCycle.Slot(BossTechniqueKind.Cameo, 1) }), $"{scene}/{difficulty}: both exclusive midboss slots");
        var seen = new System.Collections.Generic.HashSet<ulong>();
        var completedSlots = new System.Collections.Generic.HashSet<BossAttackCycle.Slot>();
        int finished = 0;
        for (int frame = 0; frame < 36000 && finished < slots.Count; frame++)
        {
            rotation.Tick(1d / 60);
            if (rotation.ActiveTechnique is { Finished: false } attack)
            {
                if (seen.Add(attack.GetInstanceId()))
                {
                    var expected = slots[rotation.CycleSlot];
                    Check(attack.TechniqueKind == expected.Kind && attack.TechniqueVariant == expected.Variant
                        && attack.TechniqueKind == BossTechniqueKind.Cameo, $"{scene}/{difficulty}: selected {attack.SpellName}");
                    if (!OS.GetCmdlineUserArgs().Contains("--midboss-roster"))
                        Check(!Read<bool>(hud, "_spellCompact"), "Technique announcement uses the original post card");
                    attack.Ended += ended => {
                        Check(ended.Completed && completedSlots.Add(expected), "Midboss completes each selected technique once");
                        Check(ended.NaturalEndRemainingShots == 0 && ended.NaturalEndLiveHazards == 0, "Midboss drains all attacks before completion");
                        finished++;
                    };
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
