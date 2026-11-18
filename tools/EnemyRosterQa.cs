using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class EnemyRosterQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly HashSet<AttackPattern> _characterPatterns = new();
    private readonly HashSet<string> _attackSignatures = new();
    private static T Read<T>(object obj, string name, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value)
        => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static void Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[EnemyRosterQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            game.Difficulty = GameManager.Diff.Normal;
            if (OS.GetCmdlineUserArgs().Contains("--cutout-preview"))
            {
                if (OS.GetCmdlineUserArgs().Contains("--source-match")) await CheckOriginalComposites();
                await CutoutPreview();
                await StopVisualAudio();
                GD.Print("[EnemyRosterQA] CUTOUT PREVIEW COMPLETE");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--animation-only"))
            {
                await CheckBodyAnimations();
                if (DisplayServer.GetName() != "headless") await CutoutPreview();
                await StopVisualAudio();
                GD.Print("[EnemyRosterQA] NATIVE ANIMATION ALL PASS");
                GetTree().Quit();
                return;
            }
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--tutorial-visuals") >= 0)
            {
                await CheckTutorialVisuals(game);
                await StopVisualAudio();
                GD.Print("[EnemyRosterQA] TUTORIAL VISUALS ALL PASS");
                GetTree().Quit();
                return;
            }
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--quote-visuals") >= 0)
            {
                await CheckQuoteVisuals(game);
                await StopVisualAudio();
                GD.Print("[EnemyRosterQA] QUOTE VISUALS ALL PASS");
                GetTree().Quit();
                return;
            }
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--projectile-visuals") >= 0)
            {
                await CheckProjectileVisuals(game);
                await StopVisualAudio();
                GD.Print("[EnemyRosterQA] PROJECTILE VISUALS ALL PASS");
                GetTree().Quit();
                return;
            }
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);

            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--front-wall") >= 0)
            {
                await CheckFrontWall(game);
                GD.Print("[EnemyRosterQA] FRONT WALL ALL PASS");
                GetTree().Quit();
                return;
            }

            var paths = new HashSet<string>();
            foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Mina })
            {
                var roster = EnemyTable.CharactersFor(theme);
                Check(roster.Count == 3, $"{theme} has three new characters");
                foreach (var spec in roster)
                {
                    using var image = GD.Load<Texture2D>(spec.PreTexPath).GetImage();
                    Check(paths.Add(spec.PreTexPath) && image.GetHeight() == 720
                        && image.DetectAlpha() != Image.AlphaMode.None, $"unique transparent texture {spec.PreTexPath}");
                    Check(spec.Humanoid && spec.PostTexPath == spec.PreTexPath,
                        "humanoid remains the same character during purification");
                }
            }
            Check(paths.Count == 12 && EnemyTable.CharactersFor(StageTheme.Default).Count == 0,
                "twelve assets registered without changing the tutorial roster");

            await CheckMotionVariants();
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--motion-only") >= 0)
            {
                await CheckApproachAlwaysCamps();
                await MotionShots(game);
                GD.Print("[EnemyRosterQA] MOTION ALL PASS");
                GetTree().Quit();
                return;
            }

            foreach (var (theme, scene) in new[] { (StageTheme.Akari, "Akari"), (StageTheme.Koharu, "Koharu"), (StageTheme.Rei, "Rei") })
                await CheckStage(game, theme, scene);
            Check(_characterPatterns.Count == 9 && _attackSignatures.Count == 9, "nine distinct projectile attacks");
            // FINAL の3種は 2026-09-17 に固有パターンを新設した。それ以前は eraser/unanswered が
            // DefaultAim・memory が None ＝「絵以外の差分ゼロ」で、3体とも同じ動きの敵だった。
            Check(EnemyTable.CharactersFor(StageTheme.Mina).Select(s => s.Pattern)
                .SequenceEqual(new[] { AttackPattern.MinaEraser, AttackPattern.MinaMemory, AttackPattern.MinaUnanswered }),
                "Mina echoes each own a distinct attack");
            CheckShardWeights();
            await CheckApproachAlwaysCamps();

            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
            await Task.Delay(250);
            await Frames(5);
            GD.Print("[EnemyRosterQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[EnemyRosterQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task CheckProjectileVisuals(GameManager game)
    {
        await Frames(2);
        string output = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs()
            .First(a => a.StartsWith("--output="))[9..]);
        System.IO.Directory.CreateDirectory(output);
        typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
        GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
        GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
        var pause = GetNode<PauseMenu>("/root/PauseMenu");
        Write(pause, "_autoplay", true);
        bool visual = DisplayServer.GetName() != "headless";
        bool comparePickups = OS.GetCmdlineUserArgs().Contains("--pickup-contrast");
        if (visual) DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        var pool = GetNode<BulletPool>("/root/Pool");
        var results = new List<object>();
        string? onlyPattern = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--pattern="))?[10..];
        string? difficultyArg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--difficulty="))?[13..];
        GameManager.Diff? onlyDifficulty = difficultyArg == null ? null : Enum.Parse<GameManager.Diff>(difficultyArg, true);
        bool cueMovie = OS.GetCmdlineUserArgs().Contains("--cue-movie");
        bool movingSource = OS.GetCmdlineUserArgs().Contains("--moving-source");
        if (cueMovie) Check(visual, "continuous cue capture requires a rendered viewport");
        int frameCount = cueMovie ? 360 : 180;
        foreach (var (theme, scene) in new[] { (StageTheme.Akari, "Akari"), (StageTheme.Koharu, "Koharu"),
            (StageTheme.Rei, "Rei"), (StageTheme.Mina, "MinaBattle") })
        {
            game.SelectedEntry = GameManager.StageEntry.Start;
            var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
            var world = root.GetNode<Node2D>("World");
            world.ProcessMode = ProcessModeEnum.Inherit;
            var player = world.GetNode<Player>("Player");
            player.SetPhysicsProcess(false);
            player.GlobalPosition = new Vector2(Field.Left + 35, Field.CenterY);
            Write(player, "_invincible", true);
            Write(player, "_invincibleTimer", 99999f);
            var hud = root.GetNode<Hud>("Hud");
            hud.HoldBubble = false;
            hud.HideBubble();
            Hud.BubblePaused = false;
            game.SetStageTarget(10000);
            foreach (var enemy in world.GetChildren().OfType<Enemy>()) enemy.QueueFree();
            await Frames(3);
            if (visual)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                await Frames(2);
            }
            var specs = EnemyTable.CharactersFor(theme).ToList();
            if (theme != StageTheme.Mina)
            {
                var (shooter, drifter) = EnemyTable.For(theme);
                specs.AddRange(new[] { shooter, drifter, EnemyTable.Flanker(theme) });
                if (theme == StageTheme.Koharu) specs.Add(EnemyTable.PrayerCarrier());
            }
            foreach (var spec in specs)
            foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
            {
                if (onlyPattern != null && spec.Pattern.ToString() != onlyPattern) continue;
                if (comparePickups && difficulty != GameManager.Diff.Normal) continue;
                if (onlyDifficulty != null && difficulty != onlyDifficulty) continue;
                game.Difficulty = difficulty;
                pool.DespawnAll();
                var enemy = new MidEnemy();
                enemy.Configure(spec);
                world.AddChild(enemy);
                enemy.GlobalPosition = new Vector2(Field.Right - 70, Field.CenterY);
                enemy.SetPhysicsProcess(false);
                enemy.SetPanelsInvulnerable(true);
                foreach (var follower in player.GetChildren().OfType<Follower>()) follower.ProcessMode = ProcessModeEnum.Disabled;
                var art = enemy.RoadVisual!;
                Check(art != null, $"{theme}/{spec.Pattern}: projectile-specific warning illustration");
                art!.Accent.ToHsv(out float hue, out _, out _);
                Check(hue < 0.7f || hue >= 0.98f, $"{theme}/{spec.Pattern}: attack accent avoids pink and lavender pickups");
                string name = $"{theme}_{spec.Pattern}_{difficulty}";
                string movieDirectory = $"{output}/{name}";
                if (cueMovie) System.IO.Directory.CreateDirectory(movieDirectory);
                int capturedFrames = 0;
                var seen = new HashSet<(ulong Instance, ulong Activation)>();
                var strikes = new HashSet<ulong>();
                Write(enemy, "_fireT", 100d);
                if (spec.Pattern == AttackPattern.KoharuPrayerCarry) Call(enemy, "SpawnCarriedPrayers");
                for (int frame = 0; frame < frameCount; frame++)
                {
                    if (movingSource) enemy.GlobalPosition = new Vector2(Field.Right - 70 + Mathf.Sin(frame / 60f) * 12,
                        Field.CenterY + Mathf.Sin(frame / 75f) * 38);
                    if (pause.IsOpen) Call(pause, "Close");
                    if (comparePickups && frame == 105)
                        world.GetNode<FxLayer>("FxLayer").PurifyBurst(new Vector2(Field.CenterX, Field.CenterY + 20), 100);
                    if (Hud.BubblePaused) hud.HideBubble();
                    Call(enemy, "TickFire", 1d / 60);
                    Read<Node2D>(enemy, "_roadCueCanvas").QueueRedraw();
                    await Frames(1);
                    foreach (var shot in EnemyBullets())
                    {
                        seen.Add((shot.GetInstanceId(), shot.ActivationId));
                        CheckOutline(shot);
                    }
                    bool captureStrike = false;
                    foreach (var strike in world.GetChildren().OfType<AreaStrike>().Where(a => a.IsStriking))
                    {
                        if (!movingSource && !strike.CoversPoint(player.GlobalPosition))
                            throw new Exception("Road beam does not reach its announced target.");
                        captureStrike |= strikes.Add(strike.GetInstanceId());
                    }
                    bool captureSnapshot = frame == 3 || frame == 40 || frame == 140;
                    if (visual && (cueMovie || captureStrike || captureSnapshot))
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using var image = GetViewport().GetTexture().GetImage();
                        if (captureStrike)
                            Check(image.SavePng($"{output}/{name}_strike.png") == Error.Ok, name + " beam strike");
                        if (captureSnapshot)
                            Check(image.SavePng($"{output}/{name}_{(frame == 3 ? "warning" : frame == 40 ? "forecast" : "live")}.png") == Error.Ok, name);
                        if (cueMovie)
                        {
                            string path = $"{movieDirectory}/frame_{frame:0000}.png";
                            if (image.SavePng(path) != Error.Ok) throw new Exception("Could not save cue frame: " + path);
                            capturedFrames++;
                        }
                    }
                }
                Check(seen.Count > 0 || (spec.Pattern == AttackPattern.ReiLockBurst && strikes.Count > 0),
                    $"{name} produces its attack; remaining={Read<int>(enemy, "_salvoRemaining")} warning={Read<double>(enemy, "_telegraphT")} fire={Read<double>(enemy, "_fireT")} paused={Hud.BubblePaused} purified={enemy.IsPurified} position={enemy.GlobalPosition} player={player.GlobalPosition}");
                results.Add(new { theme = theme.ToString(), pattern = spec.Pattern.ToString(), difficulty = difficulty.ToString(),
                    illustration = art!.Texture.ResourcePath, ink = art.Accent.ToHtml(), shots = seen.Count, beam_strikes = strikes.Count,
                    captured_frames = capturedFrames, simulation_fps = 60, simulation_seconds = frameCount / 60d,
                    invincible = true, stationary = !movingSource, dialogue_suppressed = true, human_difficulty_test = false });
                if (cueMovie) Check(capturedFrames == frameCount, name + " continuous cue frames");
                if (pause.IsOpen) Call(pause, "Close");
                enemy.SetPanelsInvulnerable(false);
                enemy.Purify();
                enemy._PhysicsProcess(1d / 60);
                await Frames(2);
                Check(!EnemyBullets().Any(b => b.RoadPending), name + " cancels pending warnings on purification");
                enemy.QueueFree();
                foreach (var hazard in world.GetChildren().OfType<AreaStrike>()) hazard.QueueFree();
                await Frames(2);
            }
            root.QueueFree();
            pool.DespawnAll();
            await Frames(3);
        }
        Check(results.Count > 0, "requested projectile pattern and difficulty were exercised");
        System.IO.File.WriteAllText($"{output}/coverage.json", System.Text.Json.JsonSerializer.Serialize(results));
        static void CheckOutline(Bullet shot)
        {
            if (shot.Footprint is not { Shapes.Length: > 0 } || !float.IsFinite(shot.CollisionRadius))
                throw new Exception("Road projectile lost its illustrated collision outline.");
            if (shot.Material is not CanvasItemMaterial { LightMode: CanvasItemMaterial.LightModeEnum.Unshaded })
                throw new Exception("Road projectile material overrides its type-specific colors.");
            if (shot.RoadPending && shot.ZIndex >= 0)
                throw new Exception("Road warning is not behind active projectiles.");
        }
    }

    private async Task StopVisualAudio()
    {
        Audio.Instance?.StopMusic(0);
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
            if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        await Task.Delay(250);
        await Frames(5);
    }

    private async Task CheckTutorialVisuals(GameManager game)
    {
        await Frames(2);
        string output = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().First(a => a.StartsWith("--output="))[9..]);
        System.IO.Directory.CreateDirectory(output);
        typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
        GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
        GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
        Write(GetNode<PauseMenu>("/root/PauseMenu"), "_autoplay", true);
        var pool = GetNode<BulletPool>("/root/Pool");
        var results = new List<object>();
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = difficulty;
            var root = GD.Load<PackedScene>("res://Stage0.tscn").Instantiate<Stage0Root>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            root.Stage.SetProcess(false);
            root.World.ProcessMode = ProcessModeEnum.Inherit;
            root.Player.SetPhysicsProcess(false);
            root.Player.GlobalPosition = new Vector2(Field.Left + 30, Field.CenterY);
            root.Hud.HoldBubble = false;
            root.Hud.HideBubble();
            Hud.BubblePaused = false;
            await Frames(3);
            foreach (var follower in root.Player.GetChildren().OfType<Follower>()) follower.ProcessMode = ProcessModeEnum.Disabled;
            Call(root.Stage, "SpawnSlowBullets");
            for (int i = 0; i < 2; i++)
            {
                await Frames(i == 0 ? 1 : 40);
                var bullets = EnemyBullets();
                Check(bullets.Length == 4 && bullets.All(b => b.RoadPending == (i == 0)
                    && (b.CollisionLayer == 0) == (i == 0) && b.Footprint is { Shapes.Length: > 0 }),
                    $"Tutorial/{difficulty}: {(i == 0 ? "hollow harmless warning" : "four live illustrated shots")}");
                if (DisplayServer.GetName() != "headless")
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var pixels = GetViewport().GetTexture().GetImage();
                    Check(pixels.SavePng($"{output}/{difficulty}_{i}.png") == Error.Ok, "tutorial screenshot");
                }
            }
            results.Add(new { difficulty = difficulty.ToString(), completed = true, shots = 4, tutorial_no_consume = true });
            pool.DespawnAll();
            root.QueueFree();
            await Frames(3);
        }
        System.IO.File.WriteAllText($"{output}/coverage.json", System.Text.Json.JsonSerializer.Serialize(results));
    }

    private async Task CheckQuoteVisuals(GameManager game)
    {
        await Frames(2);
        var args = OS.GetCmdlineUserArgs();
        string output = ProjectSettings.GlobalizePath(args.First(a => a.StartsWith("--output="))[9..]);
        System.IO.Directory.CreateDirectory(output);
        typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
        GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
        GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
        Write(GetNode<PauseMenu>("/root/PauseMenu"), "_autoplay", true);
        bool visual = DisplayServer.GetName() != "headless";
        var pool = GetNode<BulletPool>("/root/Pool");
        var results = new List<object>();
        var selected = args.FirstOrDefault(a => a.StartsWith("--difficulty="));
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            if (selected != null && difficulty.ToString() != selected[13..]) continue;
            game.Difficulty = difficulty;
            game.SelectedEntry = GameManager.StageEntry.Start;
            var root = GD.Load<PackedScene>("res://Rei.tscn").Instantiate<ReiRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            root.Stage.SetProcess(false);
            var world = root.World;
            world.ProcessMode = ProcessModeEnum.Inherit;
            var player = root.Player;
            player.SetPhysicsProcess(false);
            player.GlobalPosition = new Vector2(Field.Left + 35, Field.CenterY);
            Write(player, "_invincible", true);
            Write(player, "_invincibleTimer", 99999f);
            root.Hud.HoldBubble = false;
            root.Hud.HideBubble();
            Hud.BubblePaused = false;
            foreach (var enemy in world.GetChildren().OfType<Enemy>()) enemy.QueueFree();
            await Frames(3);
            foreach (var follower in player.GetChildren().OfType<Follower>()) follower.ProcessMode = ProcessModeEnum.Disabled;
            if (visual) DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var storm = new QuoteStorm();
            world.AddChild(storm);
            Read<RandomNumberGenerator>(storm, "_rng").Seed = 953;
            int flying = 0, harmless = 0;
            double elapsed = 0;
            int capture = 0;
            double[] times = { 1.05, 1.8, 9, 18 };
            while (!storm.Finished && elapsed < 100)
            {
                var pause = GetNode<PauseMenu>("/root/PauseMenu");
                if (pause.IsOpen) Call(pause, "Close");
                if (Hud.BubblePaused) root.Hud.HideBubble();
                await Frames(1);
                elapsed += GetProcessDeltaTime();
                foreach (var bullet in EnemyBullets().Where(b => b.Word.Length > 0))
                {
                    var core = Read<BulletWordCore>(bullet, "_wordCore");
                    if (core.Art != QuoteStorm.QuoteVisual.Cropped || core.Material is not CanvasItemMaterial)
                        throw new Exception("Quote projectile lost its dedicated illustration or original palette.");
                    if (bullet.CollisionLayer != 0)
                    {
                        flying++;
                        if (!core.Visible || bullet.Footprint is not { Shapes.Length: > 0 })
                            throw new Exception("Flying quote has no visible collision core.");
                    }
                    else
                    {
                        harmless++;
                        if (core.Visible) throw new Exception("Harmless pinned quote still shows a damaging core.");
                    }
                }
                if (visual && capture < times.Length && elapsed >= times[capture])
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage();
                    Check(image.SavePng($"{output}/{difficulty}_{capture++}.png") == Error.Ok, "quote screenshot");
                }
            }
            Check(storm.Finished && storm.QuoteCount == 17 && flying > 0 && harmless > 0 && EnemyBullets().Length == 0,
                $"QuoteStorm/{difficulty}: finished={storm.Finished} quotes={storm.QuoteCount} flying={flying} harmless={harmless} remaining={EnemyBullets().Length}; all 17 quotes resolve without shooting");
            results.Add(new { difficulty = difficulty.ToString(), completed = storm.Finished, quotes = storm.QuoteCount,
                illustration = QuoteStorm.QuoteVisual.Texture.ResourcePath, elapsed, flying_samples = flying,
                harmless_samples = harmless, invincible = true, stationary = true, human_difficulty_test = false });
            root.QueueFree();
            pool.DespawnAll();
            await Frames(3);
        }
        System.IO.File.WriteAllText($"{output}/coverage.json", System.Text.Json.JsonSerializer.Serialize(results));
    }

    private async Task CheckFrontWall(GameManager game)
    {
        Hud.BubblePaused = false;
        var pool = GetNode<BulletPool>("/root/Pool");
        var world = new Node2D();
        GetTree().Root.AddChild(world);
        var player = new Node2D();
        world.AddChild(player);
        player.AddToGroup("player");
        var points = new[]
        {
            new Vector2(Field.Left + 12f, Field.Top + 32f),
            new Vector2(Field.Left + Field.Width / 2f, Field.Top + Field.Height / 2f),
            new Vector2(Field.Right - 12f, Field.Bottom - 32f),
        };
        foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu })
        {
            var roster = EnemyTable.CharactersFor(theme);
            int index = roster.ToList().FindIndex(s => s.Pattern is AttackPattern.AkariVacant or AttackPattern.KoharuParcel);
            var spec = roster[index];
            foreach (var diff in Enum.GetValues<GameManager.Diff>())
            {
                game.Difficulty = diff;
                foreach (bool pair in new[] { false, true })
                {
                    int spawned = 0;
                    foreach (int point in Enumerable.Range(0, points.Length))
                    foreach (int seed in new[] { 17, 491, 2026 })
                    {
                        player.GlobalPosition = points[point];
                        var spawner = new Spawner { World = world, Theme = theme, CharactersOnly = true };
                        world.AddChild(spawner);
                        spawner.SetProcess(false);
                        Read<RandomNumberGenerator>(spawner, "_rng").Seed = (ulong)seed;
                        Write(spawner, "_characterIndex", roster.Count + index);
                        if (pair) Call(spawner, "SpawnPair");
                        else Call(spawner, "SpawnOne", game);
                        var pending = Read<List<(Enemy enemy, Vector2 pos, EnemySpawnWarning warning, double t, double lead)>>(spawner, "_pending");
                        var reserved = pending.ToArray();
                        string tag = $"{spec.Pattern}/{diff}/{(pair ? "pair" : "single")}/{point}/{seed}";
                        if (reserved.Length != (pair ? 2 : 1)) throw new Exception($"{tag}: wrong pending count");
                        foreach (var entry in reserved)
                        {
                            entry.enemy.ProcessMode = ProcessModeEnum.Disabled;
                            var enemy = (MidEnemy)entry.enemy;
                            var camp = Read<Vector2>(enemy, "_campTarget");
                            if (Read<EnemySpec>(enemy, "_spec").Pattern != spec.Pattern
                                || entry.pos.X != Field.Right + 14f || entry.pos.X <= player.GlobalPosition.X
                                || camp.X < Field.Right - 64f || camp.X > Field.Right - 40f
                                || entry.warning.GlobalPosition.X != Field.Right - 8f
                                || Math.Abs(Mathf.AngleDifference(entry.warning.Rotation, Mathf.Pi)) > 0.001f
                                || Math.Abs(entry.lead - 0.75) > 0.001)
                                throw new Exception($"{tag}: front spawn/camp/warning mismatch");
                        }
                        Call(spawner, "TickPending", 0.35d);
                        if (reserved.Any(e => e.enemy.IsInsideTree())) throw new Exception($"{tag}: spawned before warning finished");
                        player.GlobalPosition = points[(point + 1) % points.Length];
                        Call(spawner, "TickPending", 0.41d);
                        if (pending.Count != 0 || spawner.SpawnedCount != reserved.Length
                            || reserved.Any(e => !e.enemy.IsInsideTree() || e.enemy.GlobalPosition != e.pos))
                            throw new Exception($"{tag}: changing player position moved or dropped the reserved spawn");
                        spawned += reserved.Length;
                        if (!pair && point == 0 && seed == 17)
                            CheckFrontWallMovement(world, spec, reserved[0].pos, Read<Vector2>(reserved[0].enemy, "_campTarget"), diff);
                        foreach (var entry in reserved) entry.enemy.QueueFree();
                        spawner.Stop();
                        spawner.QueueFree();
                        await Frames(1);
                    }
                    Check(true, $"{spec.Pattern}/{diff}/{(pair ? "pair" : "single")}: {spawned} right entries retain right warnings and camps across moving-player reservations");
                }
                foreach (float row in new[] { Field.Top + 32f, Field.Top + Field.Height / 2f, Field.Bottom - 32f })
                    CheckFrontWallFire(world, player, pool, game, spec, row);
                await Frames(1);
            }
        }
        pool.DespawnAll();
        world.QueueFree();
        await Frames(2);
    }

    private static void CheckFrontWallMovement(Node2D world, EnemySpec spec, Vector2 start, Vector2 camp, GameManager.Diff diff)
    {
        foreach (int fps in new[] { 30, 120 })
        {
            GD.Seed(241);
            var enemy = new MidEnemy { Position = start, ProcessMode = ProcessModeEnum.Disabled };
            enemy.Configure(spec);
            enemy.SetEntry(camp);
            world.AddChild(enemy);
            Write(enemy, "_approachFired", true);
            Write(enemy, "_fireT", -1000d);
            var move = (Action<double>)typeof(MidEnemy).GetMethod("UpdateMovement", Private)!
                .CreateDelegate(typeof(Action<double>), enemy);
            float maxSpeed = 0;
            for (int frame = 0; frame < fps * 18; frame++)
            {
                Vector2 before = enemy.GlobalPosition;
                move(1d / fps);
                maxSpeed = Mathf.Max(maxSpeed, before.DistanceTo(enemy.GlobalPosition) * fps);
                if (frame >= fps * 12 && (!Read<bool>(enemy, "_camped")
                    || Math.Abs(enemy.GlobalPosition.X - camp.X) > 9.1f
                    || enemy.GlobalPosition.Y < Field.Top + 28f || enemy.GlobalPosition.Y > Field.Bottom - 28f))
                    throw new Exception($"{spec.Pattern}/{diff}/{fps}Hz: failed to settle within the front camp");
            }
            Check(maxSpeed <= 58.1f, $"{spec.Pattern}/{diff}/{fps}Hz: front camp reached without teleport or speed increase ({maxSpeed:0.000}px/s)");
            enemy.QueueFree();
        }
    }

    private void CheckFrontWallFire(Node2D world, Node2D player, BulletPool pool, GameManager game, EnemySpec spec, float row)
    {
        pool.DespawnAll();
        var enemy = new MidEnemy
        {
            Position = new Vector2(Field.Right - 52f, Field.Top + Field.Height / 2f),
            ProcessMode = ProcessModeEnum.Disabled,
        };
        enemy.Configure(spec);
        world.AddChild(enemy);
        Write(enemy, "_camped", true);
        Write(enemy, "_fireT", 1000d);
        string tag = $"{spec.Pattern}/{game.Difficulty}/y={row:0}";
        foreach (float x in new[] { Field.Right - 4f, enemy.GlobalPosition.X, enemy.GlobalPosition.X - 51.99f })
        {
            player.GlobalPosition = new Vector2(x, row);
            Call(enemy, "TickFire", 1d);
            if (enemy.SalvoActive || EnemyBullets().Length != 0)
                throw new Exception($"{tag}: new wall attack started behind or less than 52px ahead of player");
        }
        player.GlobalPosition = new Vector2(enemy.GlobalPosition.X - 52f, row);
        Call(enemy, "TickFire", 1d / 60d);
        int expectedSalvos = spec.Pattern == AttackPattern.KoharuParcel ? game.Difficulty >= GameManager.Diff.Hard ? 4 : 3
            : game.Difficulty >= GameManager.Diff.Hard ? 4 : game.Difficulty == GameManager.Diff.Normal ? 3 : 2;
        if (Read<double>(enemy, "_telegraphT") <= 0 || Read<int>(enemy, "_salvoRemaining") != expectedSalvos
            || EnemyBullets().Length != 0)
            throw new Exception($"{tag}: front-facing 52px boundary did not start the complete warned sequence");
        Vector2 target = Read<Vector2>(enemy, "_burstTarget");
        player.GlobalPosition = new Vector2(Field.Right - 4f, Field.Bottom - row);
        enemy.GlobalPosition += new Vector2(-4f, 8f);
        var formations = new List<Vector2[]>();
        for (int salvo = 0; salvo < expectedSalvos; salvo++)
        {
            Vector2 origin = enemy.ShotCenter;
            var before = EnemyBullets().ToHashSet();
            Call(enemy, "TickFire", 2d);
            var added = EnemyBullets().Where(b => !before.Contains(b)).ToArray();
            if (added.Length < 2 || Read<Vector2>(enemy, "_characterOrigin") != origin
                || Read<Vector2>(enemy, "_burstTarget") != target)
                throw new Exception($"{tag}: salvo missing or warned geometry retargeted after player crossed the enemy");
            foreach (var bullet in added)
            {
                bullet.SetPhysicsProcess(false);
                if (!bullet.RoadPending || bullet.CollisionLayer != 0 || !bullet.GlobalPosition.IsEqualApprox(origin))
                    throw new Exception($"{tag}: warning is not attached to the body");
                bullet._PhysicsProcess(0.45d);
                bullet._PhysicsProcess(0);
                for (int frame = 0; frame < 120 && bullet.LeadIn; frame++) bullet._PhysicsProcess(1d / 60d);
                float speed = (spec.Pattern == AttackPattern.AkariVacant ? 52f : 38f) * game.BulletSpeedMul;
                if (!bullet.Active || bullet.RoadPending || bullet.CollisionLayer != 8
                    || Math.Abs(bullet.Velocity.X + speed) > 0.01f || Math.Abs(bullet.Velocity.Y) > 0.01f
                    || Math.Abs(bullet.GlobalPosition.X - origin.X) > 0.01f
                    || bullet.GlobalPosition.Y <= Field.Top || bullet.GlobalPosition.Y >= Field.Bottom)
                    throw new Exception($"{tag}: wall direction, original speed, armed state, or fixed column changed");
            }
            formations.Add(added.Select(b => b.GlobalPosition).OrderBy(p => p.Y).ToArray());
            if (salvo > 0 && !formations[0].SequenceEqual(formations[salvo]))
                throw new Exception($"{tag}: later salvo changed formation after the player moved behind");
        }
        if (enemy.SalvoActive || Read<int>(enemy, "_salvoIndex") != expectedSalvos
            || EnemyBullets().Length != expectedSalvos * formations[0].Length)
            throw new Exception($"{tag}: sequence did not finish all salvos");
        Write(enemy, "_fireT", 1000d);
        Call(enemy, "TickFire", 1d);
        if (enemy.SalvoActive) throw new Exception($"{tag}: next wall attack began with the player behind");
        player.GlobalPosition = new Vector2(Field.Left + 12f, row);
        Call(enemy, "TickFire", 1d / 60d);
        Check(enemy.SalvoActive, $"{tag}: front gate, locked leftward {expectedSalvos} x {formations[0].Length} shots, back-side wait, and return-to-front restart");
        pool.DespawnAll();
        enemy.QueueFree();
    }

    private async Task CheckBodyAnimations()
    {
        await Frames(2);
        var world = new Node2D();
        GetTree().Root.AddChild(world);
        var specs = CutoutSpecs();
        var pool = GetNode<BulletPool>("/root/Pool");
        static bool SamePose(Transform2D a, Transform2D b) => a.Origin.DistanceTo(b.Origin) < .002f
            && a.X.DistanceTo(b.X) < .0001f && a.Y.DistanceTo(b.Y) < .0001f;
        static Transform2D LocalPose(Node2D node, Node2D ancestor)
        {
            Transform2D pose = node.Transform;
            for (var parent = node.GetParent(); parent != ancestor; parent = parent.GetParent())
                pose = ((Node2D)parent).Transform * pose;
            return pose;
        }
        foreach (var spec in specs)
        {
            var finalPoses = new List<float>();
            foreach (int fps in new[] { 30, 120 })
            {
                GD.Seed(241);
                var enemy = new MidEnemy { Position = new Vector2(260, 100) };
                enemy.Configure(spec);
                world.AddChild(enemy);
                enemy.SetPhysicsProcess(false);
                Call(enemy, "TickLivingMotion", 0d);
                var body = enemy.GetNode<Sprite2D>("Body");
                var rig = body.GetNode<EnemyCutout>("Cutout");
                var idle = rig.GetNode<AnimationPlayer>("IdleAnimation");
                var action = rig.GetNode<AnimationPlayer>("ActionAnimation");
                var joint = rig.GetNode<Node2D>(spec.Humanoid ? "Torso/Action/Gesture" : "Torso/Action/Part0");
                var sprites = rig.FindChildren("*", "Sprite2D", true, false).OfType<Sprite2D>().ToArray();
                Polygon2D[] layers = Array.Empty<Polygon2D>();
                var sampleVertices = new List<(Vector2 Point, float Weight)>();
                var opaqueSamples = new List<(Vector2 Point, float Weight)>();
                Vector2[] rigidPoints = Array.Empty<Vector2>();
                Skeleton2D? skeleton = null;
                Bone2D? poseBone = null, motionBone = null;
                Transform2D poseRest = Transform2D.Identity, motionRest = Transform2D.Identity;
                bool supportsStill = true, gripUnchanged = true, sourceUnchanged = true;
                float visibleIdleMotion = 0, gameScale = 0;
                if (spec.Humanoid)
                {
                    layers = new[] { rig.GetNode<Polygon2D>("BackLayer"), rig.GetNode<Polygon2D>("GestureLayer"),
                        rig.GetNode<Polygon2D>("FrontLayer") };
                    skeleton = rig.GetNode<Skeleton2D>("Torso/Action");
                    poseBone = skeleton.GetNode<Bone2D>("Pose");
                    motionBone = skeleton.GetNode<Bone2D>("Gesture/Action");
                    poseRest = poseBone.GetSkeletonRest();
                    motionRest = motionBone.GetSkeletonRest();
                    Check(joint is Bone2D && SamePose(poseRest, Transform2D.Identity)
                        && SamePose(motionBone.Rest, Transform2D.Identity)
                        && SamePose(LocalPose(skeleton, rig), Transform2D.Identity),
                        $"{spec.Pattern}: native skeleton binds the source pose without an extra offset");
                    Vector2 sourceSize = layers[0].Texture.GetSize(), halfSize = sourceSize / 2;
                    gameScale = 30f / sourceSize.Y * Math.Abs(rig.Scale.Y);
                    using var sourceImage = layers[0].Texture.GetImage();
                    var weightsByUv = new Dictionary<Vector2, float>();
                    var triangles = new HashSet<string>();
                    var edges = new Dictionary<(Vector2 A, Vector2 B), int>();
                    double area = 0;
                    bool sourceMapping = true, skinBinding = true, stationaryLayers = true, meshTopology = true, blendedJoint = false;
                    foreach (var layer in layers)
                    {
                        Vector2[] vertices = layer.Polygon, uv = layer.UV;
                        sourceMapping &= layer.Texture.ResourcePath == spec.PreTexPath
                            && layer.Material == null && !layer.Antialiased && layer.Color == Colors.White
                            && SamePose(LocalPose(layer, rig), Transform2D.Identity)
                            && layer.Offset == Vector2.Zero && layer.TextureOffset == Vector2.Zero
                            && layer.TextureScale == Vector2.One && Mathf.IsZeroApprox(layer.TextureRotation)
                            && vertices.Length == uv.Length;
                        Check(layer.GetBoneCount() == 2 && layer.GetNode<Skeleton2D>(layer.Skeleton) == skeleton
                            && layer.GetBonePath(0).ToString() == "Pose" && layer.GetBonePath(1).ToString() == "Gesture/Action",
                            $"{spec.Pattern}/{layer.Name}: mesh uses the fixed and coordinated native bones");
                        float[] fixedWeights = layer.GetBoneWeights(0), movingWeights = layer.GetBoneWeights(1);
                        Check(fixedWeights.Length == vertices.Length && movingWeights.Length == vertices.Length,
                            $"{spec.Pattern}/{layer.Name}: every source vertex has complete skin weights");
                        for (int v = 0; v < vertices.Length; v++)
                        {
                            sourceMapping &= vertices[v].DistanceTo(uv[v] - halfSize) < .0001f
                                && uv[v].X >= 0 && uv[v].Y >= 0 && uv[v].X <= sourceSize.X && uv[v].Y <= sourceSize.Y;
                            skinBinding &= movingWeights[v] >= 0 && movingWeights[v] <= 1
                                && Math.Abs(fixedWeights[v] + movingWeights[v] - 1) < .0001f;
                            if (layer != layers[1]) stationaryLayers &= movingWeights[v] == 0;
                            if (weightsByUv.TryGetValue(uv[v], out float weight))
                                skinBinding &= Math.Abs(weight - movingWeights[v]) < .0001f;
                            else weightsByUv.Add(uv[v], movingWeights[v]);
                        }
                        foreach (Variant polygon in layer.Polygons)
                        {
                            int[] triangle = polygon.AsInt32Array();
                            if (triangle.Length != 3 || triangle.Any(v => v < 0 || v >= vertices.Length))
                                throw new Exception($"{spec.Pattern}/{layer.Name}: invalid source triangle indices");
                            blendedJoint |= Math.Abs(movingWeights[triangle[0]] - movingWeights[triangle[1]]) > .0001f
                                || Math.Abs(movingWeights[triangle[1]] - movingWeights[triangle[2]]) > .0001f;
                            Vector2 a = uv[triangle[0]], b = uv[triangle[1]], c = uv[triangle[2]];
                            Vector2 center = (a + b + c) / 3;
                            if (sourceImage.GetPixel((int)center.X, (int)center.Y).A >= .9f)
                                opaqueSamples.Add((center - halfSize,
                                    (movingWeights[triangle[0]] + movingWeights[triangle[1]] + movingWeights[triangle[2]]) / 3));
                            double triangleArea = Math.Abs((b - a).Cross(c - a)) * .5;
                            area += triangleArea;
                            meshTopology &= triangleArea > 0 && triangles.Add(string.Join("|", new[] { a, b, c }
                                .OrderBy(p => p.X).ThenBy(p => p.Y).Select(p => $"{p.X:R},{p.Y:R}")));
                            foreach (var edge in new[] { (a, b), (b, c), (c, a) })
                            {
                                var ordered = edge.Item1.X < edge.Item2.X || edge.Item1.X == edge.Item2.X && edge.Item1.Y <= edge.Item2.Y
                                    ? edge : (edge.Item2, edge.Item1);
                                edges[ordered] = edges.GetValueOrDefault(ordered) + 1;
                            }
                        }
                    }
                    foreach (var edge in edges)
                    {
                        bool border = edge.Key.A.X == edge.Key.B.X && (edge.Key.A.X == 0 || edge.Key.A.X == sourceSize.X)
                            || edge.Key.A.Y == edge.Key.B.Y && (edge.Key.A.Y == 0 || edge.Key.A.Y == sourceSize.Y);
                        meshTopology &= edge.Value == (border ? 1 : 2);
                    }
                    Check(sourceMapping, $"{spec.Pattern}: original texture, source vertices, and UVs remain registered");
                    Check(meshTopology && Math.Abs(area - sourceSize.X * sourceSize.Y) < .01,
                        $"{spec.Pattern}: layers partition the complete original rectangle once without missing or duplicated triangles");
                    Check(skinBinding && stationaryLayers && layers[0].ZIndex < layers[1].ZIndex && layers[1].ZIndex < layers[2].ZIndex,
                        $"{spec.Pattern}: background and foreground stay fixed around the connected gesture layer");
                    sampleVertices = weightsByUv.Select(v => (v.Key - halfSize, v.Value)).ToList();
                    foreach (var vertex in weightsByUv)
                        if (vertex.Key.X < sourceSize.X && vertex.Key.Y < sourceSize.Y
                            && sourceImage.GetPixel((int)vertex.Key.X, (int)vertex.Key.Y).A >= .9f)
                            opaqueSamples.Add((vertex.Key - halfSize, vertex.Value));
                    rigidPoints = sampleVertices.Where(v => v.Weight == 1).Select(v => v.Point).ToArray();
                    Check(rigidPoints.Length >= 3 && blendedJoint,
                        $"{spec.Pattern}: the moving art has a rigid core and a blended joint attachment");
                }
                else Check(sprites.Length >= 4 && sprites.All(s => s.Texture is AtlasTexture),
                    $"{spec.Pattern}/{fps}: generated parts render as separate native sprites");
                void SampleHumanPose(bool sampleIdle = false)
                {
                    if (!spec.Humanoid) return;
                    Transform2D fixedSkin = LocalPose(poseBone!, skeleton!) * poseRest.AffineInverse();
                    Transform2D movingSkin = LocalPose(motionBone!, skeleton!) * motionRest.AffineInverse();
                    supportsStill &= SamePose(fixedSkin, Transform2D.Identity)
                        && SamePose(poseBone!.GetSkeletonRest(), poseRest) && SamePose(motionBone!.GetSkeletonRest(), motionRest);
                    foreach (var vertex in sampleVertices)
                    {
                        Vector2 moved = (fixedSkin * vertex.Point) * (1 - vertex.Weight) + (movingSkin * vertex.Point) * vertex.Weight;
                        if (vertex.Weight == 0) supportsStill &= moved.DistanceTo(vertex.Point) < .001f;
                    }
                    if (sampleIdle)
                        foreach (var vertex in opaqueSamples)
                        {
                            Vector2 moved = (fixedSkin * vertex.Point) * (1 - vertex.Weight) + (movingSkin * vertex.Point) * vertex.Weight;
                            visibleIdleMotion = Mathf.Max(visibleIdleMotion, moved.DistanceTo(vertex.Point) * gameScale);
                        }
                    Vector2 first = movingSkin * rigidPoints[0];
                    foreach (Vector2 point in rigidPoints)
                        gripUnchanged &= Math.Abs(first.DistanceTo(movingSkin * point) - rigidPoints[0].DistanceTo(point)) < .001f;
                    sourceUnchanged &= layers.All(layer => layer.Visible && layer.Texture.ResourcePath == spec.PreTexPath
                        && SamePose(LocalPose(layer, rig), Transform2D.Identity));
                }
                Check(idle.IsPlaying() && (spec.Humanoid || idle.GetAnimation("idle").GetTrackCount() >= 4),
                    $"{spec.Pattern}: native idle timeline plays");
                float angleStart = joint.Rotation, angleRange = 0, maxIdleAngle = 0;
                int restFrames = 0, holdFrames = 0, longestHold = 0;
                int idleFrames = spec.Humanoid ? (int)Math.Round(idle.GetAnimation("idle").Length * fps * 2) : fps * 8;
                Transform2D bodyStart = body.Transform;
                for (int frame = 0; frame < idleFrames; frame++)
                {
                    Call(enemy, "TickLivingMotion", 1d / fps);
                    angleRange = Mathf.Max(angleRange, Math.Abs(joint.Rotation - angleStart));
                    maxIdleAngle = Mathf.Max(maxIdleAngle, Math.Abs(joint.Rotation));
                    if (Math.Abs(joint.Rotation) < .0001f
                        && (!spec.Humanoid || joint.Position.DistanceTo(((Bone2D)joint).Rest.Origin) < .001f))
                    { restFrames++; holdFrames++; }
                    else holdFrames = 0;
                    longestHold = Math.Max(longestHold, holdFrames);
                    SampleHumanPose(true);
                }
                if (spec.Humanoid)
                {
                    Check(angleRange > Mathf.DegToRad(.05f) && maxIdleAngle <= Mathf.DegToRad(12.01f)
                        && body.Transform == bodyStart,
                        $"{spec.Pattern}: the native bone gesture stays bounded and leaves the body root fixed");
                    Check(visibleIdleMotion >= .6f,
                        $"{spec.Pattern}: opaque source art visibly moves at game size ({visibleIdleMotion:0.000} logical pixels at 30px height)");
                    Check(restFrames >= idleFrames * .15 && restFrames <= idleFrames * .35
                        && longestHold <= fps * .65 + 1 && idle.GetAnimation("idle").Length >= 1.6
                        && idle.GetAnimation("idle").Length <= 2.6,
                        $"{spec.Pattern}: short neutral rests retain regular visible motion ({100f * restFrames / idleFrames:0}% rest, {longestHold / (float)fps:0.00}s longest)");
                    Check(supportsStill && sourceUnchanged,
                        $"{spec.Pattern}: the original stationary art and layer placement retain their pose during idle");
                    Check(gripUnchanged, $"{spec.Pattern}: rigid source art keeps its internal geometry throughout the gesture");
                }
                else Check(angleRange > .02f && body.Transform == bodyStart,
                    $"{spec.Pattern}: prop parts move while the body root stays still (joint range {angleRange:0.000})");
                finalPoses.Add(joint.Rotation);
                double pausedTime = idle.CurrentAnimationPosition;
                await Frames(2);
                Check(Math.Abs(idle.CurrentAnimationPosition - pausedTime) < .0001,
                    $"{spec.Pattern}: animation respects suspended enemy processing");
                body.FlipH = !body.FlipH;
                Call(enemy, "TickLivingMotion", 0d);
                Check(Math.Sign(rig.Scale.X) == (body.FlipH ? -1 : 1),
                    $"{spec.Pattern}: every part mirrors with facing");
                body.FlipH = !body.FlipH;
                if (spec.Humanoid) idle.Seek(idle.GetAnimation("idle").Length * .51, true);
                Transform2D beforeAnticipate = spec.Humanoid ? LocalPose(motionBone!, skeleton!) : Transform2D.Identity;
                Call(enemy, "SquishBody");
                if (spec.Humanoid)
                {
                    Transform2D afterAnticipate = LocalPose(motionBone!, skeleton!);
                    Check(beforeAnticipate.Origin.DistanceTo(afterAnticipate.Origin) <= .001f
                        && beforeAnticipate.X.DistanceTo(afterAnticipate.X) <= .001f
                        && beforeAnticipate.Y.DistanceTo(afterAnticipate.Y) <= .001f,
                        $"{spec.Pattern}: anticipation captures the current idle pose without a jump");
                }
                float maxActionAngle = 0;
                bool idleSuspended = true;
                void SampleActionPose()
                {
                    SampleHumanPose();
                    if (!spec.Humanoid || !action.IsPlaying()) return;
                    idleSuspended &= !idle.IsPlaying() && SamePose(joint.Transform, ((Bone2D)joint).Rest);
                    maxActionAngle = Mathf.Max(maxActionAngle,
                        Math.Abs(joint.Rotation + joint.GetNode<Node2D>("Action").Rotation));
                }
                SampleActionPose();
                for (int frame = 0; frame < fps / 5; frame++)
                {
                    Call(enemy, "TickLivingMotion", 1d / fps);
                    SampleActionPose();
                }
                Check(action.CurrentAnimation == "anticipate", $"{spec.Pattern}: joint anticipation plays");
                Transform2D beforeRecoil = spec.Humanoid ? LocalPose(motionBone!, skeleton!) : Transform2D.Identity;
                Call(enemy, "FireBullet", pool, enemy.GlobalPosition, Vector2.Left * 60, 3.4f, 1, true);
                if (spec.Humanoid)
                {
                    Transform2D afterRecoil = LocalPose(motionBone!, skeleton!);
                    Check(beforeRecoil.Origin.DistanceTo(afterRecoil.Origin) <= .001f
                        && beforeRecoil.X.DistanceTo(afterRecoil.X) <= .001f
                        && beforeRecoil.Y.DistanceTo(afterRecoil.Y) <= .001f,
                        $"{spec.Pattern}: a shot captures interrupted anticipation without a jump");
                }
                Check(action.CurrentAnimation == "recoil", $"{spec.Pattern}: actual shot triggers joint recoil");
                for (int frame = 0; frame < fps; frame++)
                {
                    Call(enemy, "TickLivingMotion", 1d / fps);
                    SampleActionPose();
                }
                Check(!action.IsPlaying(), $"{spec.Pattern}: recoil finishes");
                if (spec.Humanoid)
                {
                    Check(idleSuspended && maxActionAngle > Mathf.DegToRad(.05f) && maxActionAngle <= Mathf.DegToRad(20.01f),
                        $"{spec.Pattern}: attack gestures stay bounded without stacking idle and action rotations");
                    Check(idle.IsPlaying() && idle.CurrentAnimationPosition > 0
                        && SamePose(joint.GetNode<Node2D>("Action").Transform, Transform2D.Identity),
                        $"{spec.Pattern}: shooting returns to neutral idle without a residual attack pose");
                    Check(supportsStill && sourceUnchanged && gripUnchanged,
                        $"{spec.Pattern}: attack preserves the fixed pose and connected grip");
                }
                Check(enemy.Position == new Vector2(260, 100) && Read<float>(enemy, "BodyRadius", typeof(Enemy)) == 8f,
                    $"{spec.Pattern}: movement and collision stay unchanged");
                enemy.Purify();
                Check(enemy.FadingBody != null && rig.GetParent() == enemy.FadingBody && !idle.IsPlaying(),
                    $"{spec.Pattern}: articulated pose transfers into purification fade");
                Check(body.Texture.ResourcePath == spec.PostTexPath, $"{spec.Pattern}: purification restores the correct art");
                typeof(Enemy).GetMethod("TickSwapAnim", Private)!.Invoke(enemy, new object[] { .06d });
                rig._Process(0);
                Check(rig.Modulate.A > 0 && rig.Modulate.A < 1
                    && Mathf.IsEqualApprox(rig.Modulate.A, enemy.FadingBody!.SelfModulate.A),
                    $"{spec.Pattern}: purification alpha propagates to every part");
                enemy.QueueFree();
                await Frames(2);
                foreach (var child in world.GetChildren()) child.QueueFree();
                pool.DespawnAll();
                await Frames(2);
            }
            Check(Math.Abs(finalPoses[0] - finalPoses[1]) < .001f,
                $"{spec.Pattern}: 30 and 120 Hz joint poses match");
        }
        world.QueueFree();
        await Frames(2);
    }

    private static List<EnemySpec> CutoutSpecs()
    {
        var specs = new List<EnemySpec>();
        foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Mina })
            specs.AddRange(EnemyTable.CharactersFor(theme));
        foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei })
        {
            var (shooter, drifter) = EnemyTable.For(theme);
            specs.Add(shooter);
            specs.Add(drifter);
        }
        return specs;
    }

    private async Task CheckOriginalComposites()
    {
        await Frames(2);
        var results = new List<object>();
        string output = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--motion-compare")
            ? "res://build/qa_story/enemy_readable_motion_20261029" : "res://build/qa_story/enemy_original_layers_20261029");
        System.IO.Directory.CreateDirectory(output);
        foreach (var spec in CutoutSpecs().Where(s => s.Humanoid))
        {
            var texture = GD.Load<Texture2D>(spec.PreTexPath);
            var size = new Vector2I(texture.GetWidth(), texture.GetHeight());
            var views = new SubViewport[2];
            for (int side = 0; side < 2; side++)
            {
                var view = new SubViewport
                {
                    Size = size, TransparentBg = true, World2D = new World2D(),
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                };
                AddChild(view);
                views[side] = view;
                var body = new Sprite2D { Texture = texture, Position = (Vector2)size / 2,
                    TextureFilter = CanvasItem.TextureFilterEnum.Linear };
                view.AddChild(body);
                if (side == 1)
                {
                    string id = System.IO.Path.GetFileNameWithoutExtension(spec.PreTexPath)[6..^4];
                    var rig = GD.Load<PackedScene>($"res://char/v3/enemies/cutout/{id}.tscn").Instantiate<EnemyCutout>();
                    body.AddChild(rig);
                    rig.Bind(body, 0);
                }
            }
            await Frames(3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var source = views[0].GetTexture().GetImage();
            using var assembled = views[1].GetTexture().GetImage();
            byte[] a = source.GetData(), b = assembled.GetData();
            int changed = 0, maxDifference = 0, beyondRounding = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int difference = Math.Abs(a[i] - b[i]);
                if (difference > 0) changed++;
                if (difference > 1) beyondRounding++;
                maxDifference = Math.Max(maxDifference, difference);
            }
            string name = System.IO.Path.GetFileNameWithoutExtension(spec.PreTexPath);
            results.Add(new { name, width = size.X, height = size.Y, changed_channels = changed,
                max_channel_delta = maxDifference, channels_beyond_one = beyondRounding });
            System.IO.File.WriteAllText($"{output}/source_match.json",
                System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Check(beyondRounding == 0, $"{name}: original pixels match at native resolution; max channel delta {maxDifference}/255");
            foreach (var view in views) view.QueueFree();
            await Frames(2);
        }
    }

    private async Task CutoutPreview()
    {
        bool compare = OS.GetCmdlineUserArgs().Contains("--compare-original");
        bool motionCompare = OS.GetCmdlineUserArgs().Contains("--motion-compare");
        await Frames(2);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
        Write(GetNode<PauseMenu>("/root/PauseMenu"), "_autoplay", true);
        var preview = new Node2D();
        GetTree().Root.AddChild(preview);
        var bg = new ColorRect { Color = new Color("202738"), Size = new Vector2(384, 216), ZIndex = -20 };
        preview.AddChild(bg);
        var title = new Label { Text = motionCompare ? "動きの比較 / 上：拡大　下：ゲーム内サイズ（高さ30px）"
            : compare ? "元絵との比較 / 同じ倍率・反転なし" : "必要な部位だけ動かす / 姿勢と持ち物のつながりを固定", Position = new Vector2(8, 3) };
        title.AddThemeFontOverride("font", UiKit.ZenBold);
        title.AddThemeFontSizeOverride("font_size", motionCompare ? 6 : 7);
        preview.AddChild(title);
        var specs = CutoutSpecs();
        string[] names = { "締切", "送信取消", "空席", "比較", "声援", "荷物", "匿名", "切り抜き", "数字", "消しゴム", "記憶", "未応答",
            "机と椅子", "送信取消の束", "ペンライト", "グッズの箱", "視聴者アイコン", "吹き出し" };
        if (motionCompare)
        {
            await CutoutMotionComparison(preview, specs, names);
            preview.QueueFree();
            await Frames(2);
            return;
        }
        int pages = compare ? 3 : OS.GetCmdlineUserArgs().Contains("--humans-only") ? 2 : 3;
        int perPage = compare ? 4 : 6;
        for (int page = 0; page < pages; page++)
        {
            var group = new Node2D();
            preview.AddChild(group);
            var enemies = new List<MidEnemy>();
            for (int i = 0; i < perPage; i++)
            {
                int index = page * perPage + i;
                var enemy = new MidEnemy { Position = compare
                    ? new Vector2(140 + i % 2 * 192, 72 + i / 2 * 100)
                    : new Vector2(65 + i % 3 * 126, 60 + i / 3 * 99) };
                enemy.Configure(specs[index]);
                group.AddChild(enemy);
                enemy.SetPhysicsProcess(false);
                foreach (var panel in enemy.GetChildren().OfType<Panel>()) panel.Hide();
                Call(enemy, "TickLivingMotion", 0d);
                var body = enemy.GetNode<Sprite2D>("Body");
                body.Scale = Vector2.One * ((compare ? 72f : 76f) / body.Texture.GetHeight());
                if (compare)
                {
                    body.FlipH = false;
                    Call(enemy, "TickLivingMotion", 0d);
                    body.GetNode<EnemyCutout>("Cutout").GetNode<AnimationPlayer>("IdleAnimation").Seek(0, true);
                    var original = new Sprite2D
                    {
                        Texture = GD.Load<Texture2D>(specs[index].PreTexPath),
                        Position = new Vector2(50 + i % 2 * 192, 72 + i / 2 * 100),
                        Scale = body.Scale, FlipH = body.FlipH,
                        TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                    };
                    group.AddChild(original);
                    for (int side = 0; side < 2; side++)
                    {
                        var caption = new Label { Text = side == 0 ? "元絵" : "現在", Position = new Vector2(16 + i % 2 * 192 + side * 90, 25 + i / 2 * 100),
                            Size = new Vector2(68, 8), HorizontalAlignment = HorizontalAlignment.Center };
                        caption.AddThemeFontOverride("font", UiKit.Zen);
                        caption.AddThemeFontSizeOverride("font_size", 5);
                        group.AddChild(caption);
                    }
                }
                enemies.Add(enemy);
                var label = new Label { Text = names[index], Position = compare
                    ? new Vector2(8 + i % 2 * 192, 15 + i / 2 * 100)
                    : new Vector2(16 + i % 3 * 126, 99 + i / 3 * 99), Size = new Vector2(compare ? 176 : 100, 9),
                    HorizontalAlignment = HorizontalAlignment.Center };
                label.AddThemeFontOverride("font", UiKit.Zen);
                label.AddThemeFontSizeOverride("font_size", 7);
                group.AddChild(label);
            }
            for (int frame = 0; frame < 240; frame++)
            {
                if (frame == 90) foreach (var e in enemies) Call(e, "PlayBodyAction", "anticipate", .7f);
                if (frame == 132) foreach (var e in enemies) Call(e, "PlayBodyAction", "recoil", 0f);
                foreach (var e in enemies) Call(e, "TickLivingMotion", 1d / 60);
                await Frames(1);
                if (frame == 30 || frame == 110 || frame == 140) await Shot($"{(compare ? "original_compare" : "cutout")}_{page}_{frame}");
            }
            group.QueueFree();
            await Frames(2);
        }
        preview.QueueFree();
        await Frames(2);
    }

    private async Task CutoutMotionComparison(Node2D preview, List<EnemySpec> specs, string[] names)
    {
        for (int page = 0; page < 3; page++)
        {
            var group = new Node2D();
            preview.AddChild(group);
            var rigs = new List<EnemyCutout>();
            for (int i = 0; i < 4; i++)
            {
                int index = page * 4 + i, column = i % 2, row = i / 2;
                var spec = specs[index];
                string id = System.IO.Path.GetFileNameWithoutExtension(spec.PreTexPath)[6..^4];
                var label = new Label { Text = names[index], Position = new Vector2(8 + column * 192, 12 + row * 102),
                    Size = new Vector2(176, 8), HorizontalAlignment = HorizontalAlignment.Center };
                label.AddThemeFontOverride("font", UiKit.Zen);
                label.AddThemeFontSizeOverride("font_size", 6);
                group.AddChild(label);
                for (int side = 0; side < 2; side++)
                {
                    float x = 50 + column * 192 + side * 90;
                    var caption = new Label { Text = side == 0 ? "変更前" : "変更後",
                        Position = new Vector2(x - 34, 21 + row * 102), Size = new Vector2(68, 6),
                        HorizontalAlignment = HorizontalAlignment.Center };
                    caption.AddThemeFontOverride("font", UiKit.Zen);
                    caption.AddThemeFontSizeOverride("font_size", 5);
                    group.AddChild(caption);
                    string scene = side == 0
                        ? $"res://build/qa_story/enemy_readable_motion_20261029/before/{id}.tscn"
                        : $"res://char/v3/enemies/cutout/{id}.tscn";
                    for (int size = 0; size < 2; size++)
                    {
                        var texture = GD.Load<Texture2D>(spec.PreTexPath);
                        var body = new Sprite2D { Texture = texture, FlipH = false,
                            Position = new Vector2(x, (size == 0 ? 51 : 94) + row * 102),
                            Scale = Vector2.One * ((size == 0 ? 48f : 30f) / texture.GetHeight()),
                            TextureFilter = CanvasItem.TextureFilterEnum.Linear };
                        group.AddChild(body);
                        var rig = GD.Load<PackedScene>(scene).Instantiate<EnemyCutout>();
                        body.AddChild(rig);
                        rig.Bind(body, 0);
                        rigs.Add(rig);
                    }
                }
            }
            for (int frame = 0; frame < 480; frame++)
            {
                if (frame == 240)
                {
                    foreach (var rig in rigs) rig.PlayAction("anticipate", .7f);
                    GD.Print($"[EnemyRosterQA] MOTION COMPARE page {page + 1}, frame {frame}: anticipate");
                }
                if (frame == 282)
                {
                    foreach (var rig in rigs) rig.PlayAction("recoil");
                    GD.Print($"[EnemyRosterQA] MOTION COMPARE page {page + 1}, frame {frame}: recoil");
                }
                foreach (var rig in rigs) rig.Advance(1d / 60);
                await Frames(1);
                if (frame == 30 || frame == 75 || frame == 110 || frame == 270 || frame == 290)
                    await Shot($"motion_compare_{page}_{frame}");
            }
            group.QueueFree();
            await Frames(2);
        }
    }

    private async Task CheckMotionVariants()
    {
        var world = new Node2D();
        GetTree().Root.AddChild(world);
        var variants = Enum.GetValues(typeof(MidEnemy).GetNestedType("MoveVariant", BindingFlags.NonPublic)!);
        Check(variants.Length == 8, "eight entry and hover variants are available");
        var specs = new List<EnemySpec>();
        foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Mina })
        {
            specs.AddRange(EnemyTable.CharactersFor(theme));
            if (theme == StageTheme.Mina) continue;
            var (shooter, drifter) = EnemyTable.For(theme);
            specs.Add(shooter);
            specs.Add(drifter);
        }
        foreach (var spec in specs)
        {
            foreach (object variant in variants)
            {
                var endPositions = new List<Vector2>();
                foreach (int fps in new[] { 30, 120 })
                {
                    GD.Seed(241);
                    var enemy = new MidEnemy { Position = new Vector2(Field.Right + 14, 70) };
                    enemy.Configure(spec);
                    enemy.SetSilentEntry(new Vector2(260, 110));
                    world.AddChild(enemy);
                    enemy.SetPhysicsProcess(false);
                    Write(enemy, "_moveVariant", variant);
                    Write(enemy, "_approachFired", true);
                    Write(enemy, "_fireT", -1000d);
                            var lateral = (Func<float, float>)typeof(MidEnemy).GetMethod("LateralOffset", Private)!
                        .CreateDelegate(typeof(Func<float, float>), enemy);
                    Check(Math.Abs(lateral(0)) < 0.001f && Math.Abs(lateral(1)) < 0.001f,
                        $"{spec.Pattern}/{variant}: entry curves meet both endpoints without a lateral jump");
                    var move = (Action<double>)typeof(MidEnemy).GetMethod("UpdateMovement", Private)!
                        .CreateDelegate(typeof(Action<double>), enemy);
                    var body = enemy.GetNode<Sprite2D>("Body");
                    float maxSpeed = 0;
                    for (int frame = 0; frame < fps * 18; frame++)
                    {
                        Vector2 before = enemy.Position;
                        Vector2 pose = body.Position;
                        float angle = body.Rotation;
                        move(1d / fps);
                        maxSpeed = Mathf.Max(maxSpeed, before.DistanceTo(enemy.Position) * fps);
                        if (spec.Humanoid && frame > fps)
                        {
                            if (pose.DistanceTo(body.Position) * fps > 40f || Math.Abs(Mathf.AngleDifference(angle, body.Rotation)) * fps > 1.8f)
                                throw new Exception($"{spec.Pattern}/{variant}/{fps}: discontinuous body pose");
                            if (Math.Abs(body.Rotation) > 0.151f || body.Scale.X <= 0 || body.Scale.Y <= 0)
                                throw new Exception($"{spec.Pattern}/{variant}/{fps}: invalid body transform");
                        }
                        if (frame == fps * 12 && !Read<bool>(enemy, "_camped"))
                            throw new Exception($"{spec.Pattern}/{variant}/{fps}: entry did not finish");
                        if (frame > fps * 12 && (Math.Abs(enemy.Position.X - 260) > 9.1f
                            || enemy.Position.Y < 28 || enemy.Position.Y > 188))
                            throw new Exception($"{spec.Pattern}/{variant}/{fps}: hover left its safe area");
                    }
                    Check(maxSpeed <= 58.1f, $"{spec.Pattern}/{variant}/{fps}: smooth motion stays below player speed ({maxSpeed:0.0})");
                    Check(Read<float>(enemy, "BodyRadius", typeof(Enemy)) == 8f, "body collision radius stays unchanged");
                    endPositions.Add(enemy.Position);
                    enemy.QueueFree();
                    await Frames(1);
                }
                Check(endPositions[0].DistanceTo(endPositions[1]) < 3f,
                    $"{spec.Pattern}/{variant}: 30 and 120 Hz paths stay consistent");
            }
        }
        world.QueueFree();
        await Frames(2);
    }

    private async Task MotionShots(GameManager game)
    {
        if (DisplayServer.GetName() == "headless") return;
        typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
        Write(GetNode<PauseMenu>("/root/PauseMenu"), "_autoplay", true);
        game.SelectedEntry = GameManager.StageEntry.Start;
        Call(GetNode<PauseMenu>("/root/PauseMenu"), "Close");
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.GetNode("StageAkari").SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Disabled;
        root.GetNode<Hud>("Hud").HideBubble();
        var enemies = new List<MidEnemy>();
        foreach (var theme in new[] { StageTheme.Akari, StageTheme.Koharu, StageTheme.Rei, StageTheme.Mina })
            foreach (var spec in EnemyTable.CharactersFor(theme))
            {
                int i = enemies.Count;
                var enemy = new MidEnemy { Position = new Vector2(182 + (i % 4) * 54, 40 + (i / 4) * 62) };
                enemy.Configure(spec);
                world.AddChild(enemy);
                enemies.Add(enemy);
            }
        bool nativePreview = OS.GetCmdlineUserArgs().Contains("--animation-only");
        foreach (int frame in Enumerable.Range(0, nativePreview ? 480 : 60))
        {
            if (nativePreview && (frame == 120 || frame == 300))
                foreach (var enemy in enemies) Call(enemy, "PlayBodyAction", "anticipate", .7f);
            if (nativePreview && (frame == 162 || frame == 342))
                foreach (var enemy in enemies) Call(enemy, "PlayBodyAction", "recoil", 0f);
            foreach (var enemy in enemies) Call(enemy, "TickLivingMotion", 1d / 60);
            if (nativePreview) await Frames(1);
            if (frame != 0 && frame != 20 && frame != 59 && (!nativePreview || (frame != 140 && frame != 170 && frame != 420))) continue;
            await Frames(2);
            await Shot($"motion_{frame:00}");
        }
        Check(enemies.All(e => e.GetNode<Sprite2D>("Body").Texture != null), "all twelve animated character assets render");
        root.QueueFree();
        await Frames(3);
    }

    private async Task CheckStage(GameManager game, StageTheme theme, string scene)
    {
        game.SelectedEntry = GameManager.StageEntry.Start;
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
        var world = root.GetNode<Node2D>("World");
        var player = world.GetNode<Player>("Player");
        var hud = root.GetNode<Hud>("Hud");
        stage.SetProcess(false);
        world.ProcessMode = ProcessModeEnum.Inherit;
        player.SetPhysicsProcess(false);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        player.GlobalPosition = new Vector2(Field.Left + 22, 184);
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(3);
        Call(stage, "StartMidwaveSpawner", 0, 0.7f);
        var spawner = stage.GetNode<Spawner>("Spawner");
        spawner.SetProcess(false);
        Read<RandomNumberGenerator>(spawner, "_rng").Seed = 934;
        Check(spawner.Theme == theme, $"{scene} wires its actual spawner to the correct roster");
        var characters = EnemyTable.CharactersFor(theme);
        for (int i = 0; i < 3; i++)
        {
            spawner._Process(3);
            Call(spawner, "TickPending", 1d);
            var enemy = world.GetChildren().OfType<MidEnemy>().Last();
            Check(Read<EnemySpec>(enemy, "_spec").PreTexPath == characters[i].PreTexPath,
                $"{theme} introduces character {i + 1} before random special enemies");
            var sprite = enemy.GetNode<Sprite2D>("Body");
            Check(sprite.FlipH == characters[i].FlipH && Mathf.IsEqualApprox(sprite.Scale.Y * sprite.Texture.GetHeight(), 30f),
                "new sprite faces left at a stable character size");
            Check(Read<float>(enemy, "BodyRadius", typeof(Enemy)) == 8f
                && enemy.GetChildren().OfType<Panel>().Count() == 3
                && enemy.GetChildren().OfType<Panel>().All(p => p.Ink > 4),
                "road enemies retain three unfolders with independent HP gauges");
        }
        var firstWave = world.GetChildren().OfType<MidEnemy>().ToArray();
        await Frames(140);
        var before = firstWave.Select(e => e.GetNode<Sprite2D>("Body").Position).ToArray();
        await Frames(10);
        Check(firstWave.Where((e, i) => e.GetNode<Sprite2D>("Body").Position != before[i]).Any(), "characters gently move");
        // 人型12種は 2026-09-17 に「全員 Humanoid 一種（±0.025rad）」から種ごとの専用モーションへ分けた。
        // 上限を 0.026→0.16rad(≒9.2°) へ広げる。狙いは変わらない＝道具種の SpinSpeed による
        // “ぐるぐる回る”を人型が受け継がないこと（AutoBank=false のまま基底の回転は握らせない）。
        // 9.2° は「首を振る／応援で体を振る」の範囲で、人が物のように回っては見えない線。
        Check(firstWave.All(e => Mathf.Abs(e.GetNode<Sprite2D>("Body").Rotation) <= 0.16f), "humanoids do not inherit prop spinning");
        // 予備動作を種ごとに 0.34〜0.78s へ分けた（2026-09-17 差別化）ので初弾の時刻が種で変わる。
        // あかり面は最長の溜め（Deadline 0.78 / Vacant 0.74）を持ち、NORMAL の Di(x1.35) に
        // 発射ゲート(0.7s)と FirstShotDelay(0.25s) が乗ると初弾が約 2.1s ＝ 固定150フレーム時点
        // （実測 約1.88s）に間に合わない。フレーム数ではなく「弾が出るまで待つ」判定に変え、
        // 溜めの長さにも実行環境のフレーム間隔にも依存しないようにする。
        Check(await WaitFrames(() => GetTree().GetNodesInGroup("enemy_bullets").Count > 0, 300),
            "new characters use the existing attacks");
        await Shot($"{scene.ToLowerInvariant()}_new_enemies");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Frames(10);
        await Shot($"{scene.ToLowerInvariant()}_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));

        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/v3/mina_conversation_v1.png");
        await Frames(3);
        var positions = firstWave.Select(e => e.GlobalPosition).ToArray();
        int spawned = spawner.SpawnedCount;
        spawner._Process(20);
        await Frames(20);
        Check(spawner.SpawnedCount == spawned && firstWave.Where((e, i) => e.GlobalPosition != positions[i]).Count() == 0,
            "dialogue freezes both spawning and movement");
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(3);

        foreach (var enemy in firstWave)
        {
            string path = Read<EnemySpec>(enemy, "_spec").PreTexPath;
            int purified = game.PurifiedCount;
            enemy.Purify();
            enemy.Purify();
            Check(enemy.IsPurified && game.PurifiedCount == purified + 1
                && enemy.GetNode<Sprite2D>("Body").Texture.ResourcePath == path,
                "purification awards once and preserves the character illustration");
        }
        await Frames(140);
        Check(firstWave.All(e => !IsInstanceValid(e)), "purified characters leave the field");

        var skins = new HashSet<string>();
        var patterns = new HashSet<AttackPattern>();
        for (int i = 0; i < 100; i++)
        {
            Call(spawner, "SpawnOne", game);   // Spawner.SpawnOne は GameManager? を取る（出現方向の拡張で引数が増えた）
            Call(spawner, "TickPending", 1d);
            var enemy = world.GetChildren().OfType<MidEnemy>().Last();
            var spec = Read<EnemySpec>(enemy, "_spec");
            skins.Add(spec.PreTexPath);
            patterns.Add(spec.Pattern);
            enemy.QueueFree();
            await Frames(1);
        }
        var (shooter, drifter) = EnemyTable.For(theme);
        Check(skins.Count == 5 && skins.Contains(shooter.PreTexPath) && skins.Contains(drifter.PreTexPath),
            "new and existing enemies continue appearing together");
        Check(patterns.Contains(AttackPattern.FlankAim) && patterns.Contains(AttackPattern.BuzzWall),
            "flanking and shield enemies remain available");
        if (theme == StageTheme.Koharu) Check(patterns.Contains(AttackPattern.KoharuPrayerCarry), "prayer carrier remains available");
        for (int i = 0; i < 20; i++) spawner._Process(3);
        Check(GetTree().GetNodesInGroup("enemies").Count == game.MaxAliveEnemies, "existing concurrent enemy limit is respected");
        spawner.Stop();
        spawned = spawner.SpawnedCount;
        spawner._Process(20);
        Check(spawner.SpawnedCount == spawned, "stopped waves cannot spawn");
        foreach (var node in world.GetChildren())
            if (node is Enemy or Ripple) node.QueueFree();
        await Frames(3);
        await CheckCharacterAttacks(game, world, player, hud, characters);
        await CheckCameoAttacks(game, stage, player, hud, scene);
        root.QueueFree();
        await Frames(5);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Hud.BubblePaused = false;
    }

    // ─── 進入が必ず終わる（＝着座に到達する）ことの実測（2026-09-17 移動パターン差別化）───
    //   移動を種ごとに変えた結果いちばん怖いのは「着座しない個体」＝倒すまで居座る前提が崩れ、
    //   MaxAliveEnemies の枠を食い潰して以降のウェーブが湧かない＝進行不能になること。
    //   全種 × 全出現エッジ（右/上/下/左）× 全難易度 を実際に物理で回し、
    //   規定時間内に _camped が立つことを実測する。設計上の最悪値は
    //   「盤面対角 460px ÷ 実効前進 46px/s ≒ 10秒」なので、余裕をみて 12 秒を上限にする。
    private async Task CheckApproachAlwaysCamps()
    {
        const double LimitSec = 12.0;
        var world = new Node2D { Name = "ApproachWorld" };
        GetTree().Root.AddChild(world);
        var game = GetNode<GameManager>("/root/Game");

        // 全種を集める（基底6種＋人型12種＋引用リプ＋バズ壁＋祈り運び）。
        var specs = new List<(string label, EnemySpec spec)>();
        foreach (var theme in new[] { StageTheme.Rei, StageTheme.Akari, StageTheme.Koharu, StageTheme.Mina })
        {
            var (shooter, drifter) = EnemyTable.For(theme);
            if (theme != StageTheme.Mina)
            {
                specs.Add(($"{theme}.shooter", shooter));
                specs.Add(($"{theme}.drifter", drifter));
                specs.Add(($"{theme}.flank", EnemyTable.Flanker(theme)));
                specs.Add(($"{theme}.wall", EnemyTable.BuzzWall(theme)));
            }
            foreach (var c in EnemyTable.CharactersFor(theme))
                specs.Add(($"{theme}.{c.Pattern}", c));
        }

        // 出現点と着座点の組。Spawner が実際に使う4エッジを模す（右/上/下/左）。
        // 左だけは SetSilentEntry（着座まで撃たない）で、着座 x は 184〜224 の保証帯を使う。
        var entries = new (string edge, Vector2 pos, Vector2 camp, bool silent)[]
        {
            ("right",  new Vector2(Field.Right + 14f, 46f),  new Vector2(268f, 150f), false),
            ("top",    new Vector2(300f, Field.Top - 18f),   new Vector2(176f, 172f), false),
            ("bottom", new Vector2(320f, Field.Bottom + 18f), new Vector2(200f, 40f),  false),
            ("left",   new Vector2(Field.Left - 18f, 120f),  new Vector2(184f, 60f),  true),
        };

        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            foreach (var (label, spec) in specs)
            {
                // 祈り運びは「居座らず横断する」設計＝着座しないのが正。左端へ抜けて消えることだけ確かめる。
                if (spec.Pattern == AttackPattern.KoharuPrayerCarry) continue;
                foreach (var (edge, pos, camp, silent) in entries)
                {
                    var e = new MidEnemy();
                    e.Configure(spec);
                    if (silent) e.SetSilentEntry(camp); else e.SetEntry(camp);
                    world.AddChild(e);
                    e.GlobalPosition = pos;
                    double t = 0;
                    while (!Read<bool>(e, "_camped") && t < LimitSec)
                    {
                        e._PhysicsProcess(1.0 / 60.0);
                        t += 1.0 / 60.0;
                    }
                    Check(Read<bool>(e, "_camped"),
                        $"{label}/{edge}/{diff}: approach reaches its camp point in {t:0.00}s (< {LimitSec}s)");
                    // 着座点から離れていないこと（進入の横オフセットが収束せずズレたまま止まっていない）。
                    //   X は camp.X ± CampDriftMax(9px) の帯に収まる＝左湧きの着座保証 x=184..224 が保たれる。
                    //   Y は種ごとの縦の形（鋸波±22 / 段送り±24 など）を許す帯で見る。
                    Check(Mathf.Abs(e.GlobalPosition.X - camp.X) <= 10f,
                        $"{label}/{edge}/{diff}: settles on the camp column (x={e.GlobalPosition.X:0.0} vs {camp.X:0.0})");
                    Check(Mathf.Abs(e.GlobalPosition.Y - camp.Y) <= 30f,
                        $"{label}/{edge}/{diff}: settles near the camp row (y={e.GlobalPosition.Y:0.0} vs {camp.Y:0.0})");
                    // 左湧きは着座まで一度も撃たない（SetSilentEntry の保証を移動変更で壊していない）。
                    if (silent) Check(GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().All(b => !b.Active),
                        $"{label}/{edge}/{diff}: left-edge spawns stay silent until camped");
                    e.QueueFree();
                    // 予兆（AreaStrike）は発生源に紐づくが、発生源を消すのは次フレーム＝ここで一緒に掃除する
                    //（4難易度×全種×4エッジ ぶん溜め込まない）。
                    foreach (var n in world.GetChildren()) if (n is AreaStrike) n.QueueFree();
                    GetNode<BulletPool>("/root/Pool").DespawnAll();
                    await Frames(1);
                }
            }
        }

        // 着座後は盤面内に留まり続ける（横揺れ・縦の形が画面外へ持ち出さない）ことを長回しで確認。
        foreach (var (label, spec) in specs)
        {
            if (spec.Pattern == AttackPattern.KoharuPrayerCarry) continue;
            var e = new MidEnemy();
            e.Configure(spec);
            e.SetEntry(new Vector2(200f, 108f));
            world.AddChild(e);
            e.GlobalPosition = new Vector2(Field.Right + 14f, 108f);
            for (int i = 0; i < 60 * 25; i++)   // 25 秒ぶん回す
            {
                e._PhysicsProcess(1.0 / 60.0);
                if (!Read<bool>(e, "_camped")) continue;   // 進入中は見ない（出現点 Field.Right+14 は盤面外＝初回フレームで必ず範囲外になる）。このループの主題は「着座後」に留まり続けるか。
                var q = e.GlobalPosition;
                if (q.X < Field.Left + 8f || q.X > Field.Right - 8f || q.Y < 20f || q.Y > 196f)
                    throw new Exception($"{label}: camped enemy left the safe area at {q}");
            }
            e.QueueFree();
            foreach (var n in world.GetChildren()) if (n is AreaStrike) n.QueueFree();
            GetNode<BulletPool>("/root/Pool").DespawnAll();
            await Frames(1);
        }
        game.Difficulty = GameManager.Diff.Normal;
        world.QueueFree();
        await Frames(2);
        Check(true, "every enemy type camps in bounded time and stays inside the field");
    }

    // 種ごとの欠片配分（EnemySpec.ShardWeight・2026-09-17）の検証。
    //   ①「強い敵・倒しにくい敵ほど多い」の序列が崩れていないこと（リスクとリターンの比例）。
    //   ②粒数を変えても経済（ショップ通貨＝インプレ）が一切変わらないこと。
    //     PurifyBurst は impBase を n 粒へ総和保存で割る（impBase/n ＋ 余りを先頭から1ずつ）ので、
    //     n をどう動かしても拾い切ったときの合計は impBase のまま——これを実数で確かめる。
    private void CheckShardWeights()
    {
        float Weight(StageTheme theme, int i) => EnemyTable.CharactersFor(theme)[i].ShardWeight;

        // ① 序列：手間2.5倍のバズ壁が最大、手間1/3のボーナス種（祈り運び）が最小。
        float wall = EnemyTable.BuzzWall(StageTheme.Akari).ShardWeight;
        float carry = EnemyTable.PrayerCarrier().ShardWeight;
        Check(wall == 2.5f && carry == 0.45f && wall > EnemyTable.Flanker(StageTheme.Rei).ShardWeight,
            "shield wall drops the most shards and the bonus carrier the fewest");
        // 撃つ種 > 撃たない種（同じ6ヒットなら、残すと痛い方が多く落とす）。
        foreach (var theme in new[] { StageTheme.Rei, StageTheme.Akari, StageTheme.Koharu })
        {
            var (shooter, drifter) = EnemyTable.For(theme);
            Check(shooter.ShardWeight > drifter.ShardWeight, $"{theme} shooter outdrops its drifter");
        }
        // 人型は「撃つ頻度が高い／避け場を奪う」ほど多い。各面の最寡黙な種が最小になっていること。
        Check(Weight(StageTheme.Koharu, 2) < Weight(StageTheme.Koharu, 0)
            && Weight(StageTheme.Koharu, 2) < Weight(StageTheme.Koharu, 1)
            && Weight(StageTheme.Rei, 2) < Weight(StageTheme.Rei, 0)
            && Weight(StageTheme.Akari, 1) < Weight(StageTheme.Akari, 0),
            "quiet characters drop fewer shards than the pressuring ones");
        // 全種が妥当な範囲（0 や負で消えない・中ボス級を超えない）。
        var all = new[] { StageTheme.Rei, StageTheme.Akari, StageTheme.Koharu, StageTheme.Mina }
            .SelectMany(t => EnemyTable.CharactersFor(t).Select(s => s.ShardWeight))
            .Concat(new[] { wall, carry }).ToArray();
        Check(all.All(w => w >= 0.4f && w <= 2.5f), "every shard weight stays inside a sane range");

        // ② 経済不変：同じ impBase を、粒数だけ変えて撒いたときの入金額が一致するか。
        //    FxLayer 実物を通すのではなく、PurifyBurst と同じ総和保存の分配式で検算する
        //    （FxLayer は他ワーカーが編集中のため、ここでは式の不変性だけを見る）。
        int Distributed(int impBase, int n)
        {
            int sum = 0;
            for (int i = 0; i < n; i++) sum += impBase / n + (i < impBase % n ? 1 : 0);
            return sum;
        }
        foreach (int impBase in new[] { 1, 2, 7, 13, 40, 99 })
        {
            int baseline = Distributed(impBase, 13);   // 従来の Zako 粒数（10〜16）の中央
            foreach (float w in all)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(13 * w), 1, 64);
                Check(Distributed(impBase, n) == impBase && baseline == impBase,
                    $"shard weight {w} keeps the {impBase} impression payout intact");
            }
        }
    }

    private Bullet[] EnemyBullets() => GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Where(b => b.Active).ToArray();

    // 敵弾は「敵の中心に湧いて、本来の発射点まで飛んでから」パターンへ移る（Bullet.MakeLeadIn・2026-09-28）。
    // 弾幕の幾何（配置・弾速）を見る検査はその導入区間を消化してから行う＝以降の判定は従来と同じ座標で回る。
    // 導入を終えたフレームは速度を適用せず発射点で止まるので、ここを抜けた時点の位置＝本来の発射点ちょうど。
    private static Bullet[] SettleLeadIn(Bullet[] bullets)
    {
        foreach (var b in bullets)
        {
            Check(b.RoadPending && b.CollisionLayer == 0, "road bullet warns on the body before launch");
            b._PhysicsProcess(0.45);
            b._PhysicsProcess(0);
            for (int i = 0; i < 64 && b.LeadIn; i++) b._PhysicsProcess(1.0 / 60.0);
            Check(!b.RoadPending && b.CollisionLayer == 8, "road bullet arms after its warning");
        }
        return bullets;
    }

    private async Task CheckCameoAttacks(GameManager game, Node stage, Player player, Hud hud, string scene)
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        game.MarkIdleDialogSeen("once_midboss_shield");
        Write(stage, "_stepStarted", false);
        // 中ボスの登場カットシーン（CameoIntroScene）は会話送りを待つ＝ここでは通り抜けられない。
        // ルナティック経路はカットシーンを流さずその場で中ボスを出すので、この一歩だけ借りて弾幕に入る
        //（ここで見るのは弾の絵・弾数・弾速で、登場演出は qa_cameo_intro の担当）。
        Write(stage, "_lunatic", true);
        Call(stage, "Step_BossCameo", 0d);
        Write(stage, "_lunatic", false);
        var cameo = Read<CameoBoss>(stage, "_cameo");
        await Frames(60);
        cameo.SetPhysicsProcess(false);
        player.GlobalPosition = new Vector2(Field.Left + 22f, 184f);
        var (firstArt, secondArt, firstInterval, secondInterval, firstCount, secondCount, firstSpeed, secondSpeed) = scene switch
        {
            "Akari" => ("akari_docs", "akari_envelope", 1.0d, 1.4d, 7, 8, 72f, 44f),
            "Koharu" => ("koharu_penlight", "koharu_ticket", 1.1d, 1.5d, 8, 5, 76f, 56f),
            "Rei" => ("enemy_rei_anonymous", "enemy_rei_metrics", 0.95d, 1.2d, 3, 12, 96f, 68f),
            _ => throw new Exception("Unexpected cameo stage"),
        };
        foreach (string art in new[] { firstArt, secondArt })
        {
            using var image = BulletArt.Get(art)!.GetImage();
            Check(image.DetectAlpha() != Image.AlphaMode.None && image.GetPixel(0, 0).A < 0.05f,
                $"{scene} cameo loads transparent projectile {art}");
        }
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            pool.DespawnAll();
            Write(cameo, "_fireT", 0d);
            Write(cameo, "_fireT2", 0d);
            double threshold = firstInterval * game.DanmakuIntervalMul;
            Call(cameo, "FirePattern", threshold - 0.00001d);
            Check(EnemyBullets().Length == 0, $"{scene}/{diff}: cameo preserves its firing interval");
            Call(cameo, "FirePattern", 0.00002d);
            int expectedFirst = scene == "Rei" ? firstCount : game.ScaleBullets(firstCount);
            // こはる中ボスの2つ目は自機狙いの扇（FanDown）＝本数は必ず奇数（2026-10-03）。
            // あかり/レイの2つ目はリング・雨なので従来どおり ScaleBullets。
            int expectedSecond = scene == "Koharu" ? game.ScaleBulletsOdd(secondCount) : game.ScaleBullets(secondCount);
            CheckVolley(firstArt, expectedFirst, firstSpeed, rain: scene != "Rei");
            pool.DespawnAll();
            Write(cameo, "_fireT", 0d);
            Write(cameo, "_fireT2", secondInterval * game.DanmakuIntervalMul);
            Call(cameo, "FirePattern", 0d);
            CheckVolley(secondArt, expectedSecond, secondSpeed, rain: false);
            pool.DespawnAll();
            Write(cameo, "_fireT", threshold);
            Write(cameo, "_fireT2", secondInterval * game.DanmakuIntervalMul);
            Call(cameo, "FirePattern", 0d);
            Check(EnemyBullets().Length == expectedFirst + expectedSecond,
                $"{scene}/{diff}: simultaneous cameo attacks keep their bullet counts");
            CheckVolley(firstArt, expectedFirst, firstSpeed, rain: scene != "Rei");
            CheckVolley(secondArt, expectedSecond, secondSpeed, rain: false);
        }
        game.Difficulty = GameManager.Diff.Normal;
        pool.DespawnAll();
        Write(cameo, "_fireT", 0d);
        Write(cameo, "_fireT2", 0d);
        cameo.SetPhysicsProcess(true);
        // 2つ目の攻撃の間隔は Di 込みで最大 1.4x1.35=1.89s（あかり）。固定130フレーム（約1.6s）では
        // 遅い方が間に合わずに落ちるので、両方の弾が出そろうまで待つ判定にする。
        bool bothFired = await WaitFrames(() => Volley(firstArt).Length > 0 && Volley(secondArt).Length > 0, 600);
        Check(bothFired, $"{scene}: both illustrated attacks fire during real cameo combat");
        await Shot($"{scene.ToLowerInvariant()}_cameo_attacks");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Frames(3);
        await Shot($"{scene.ToLowerInvariant()}_cameo_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/v3/mina_conversation_v1.png");
        await Frames(3);
        var active = EnemyBullets();
        var positions = active.Select(b => b.GlobalPosition).ToArray();
        double fireTime = Read<double>(cameo, "_fireT");
        await Frames(20);
        Check(fireTime == Read<double>(cameo, "_fireT")
            && active.Select((b, i) => b.GlobalPosition == positions[i]).All(x => x),
            $"{scene}: dialogue freezes cameo attacks and illustrated bullets");
        cameo.QueueFree();
        hud.HoldBubble = false;
        hud.HideBubble();
        pool.DespawnAll();
        await Frames(3);

        Bullet[] Volley(string art)
        {
            bool rain = art == firstArt && scene != "Rei";
            return EnemyBullets().Where(b => rain ? b.OverheadPending
                : !b.OverheadPending && b.BossStyle == (art == secondArt && scene != "Koharu" ? BossShotStyle.Ring : BossShotStyle.Aimed)).ToArray();
        }

        void CheckVolley(string art, int expected, float speed, bool rain)
        {
            var bullets = Volley(art);
            var job = cameo.BodyShotJob!.Value;
            float radius = rain ? 3.2f : BulletPool.BossRadius * (job == Job.Magic ? 1.5f : 1);
            bool accel = job == Job.Melee;
            bool homing = !rain && job == Job.Heal;
            float initialSpeed = accel ? 0.6f : rain ? speed : job == Job.Magic ? 62f : 60f;
            Check(bullets.Length == expected && bullets.All(b => Mathf.IsEqualApprox(b.Radius, radius)
                && b.Damage == 1 && b.Homing == homing && !b.IsPatternBullet && b.Accel == accel),
                $"{scene}/{game.Difficulty}: {art} retains count, collision and damage");
            Check(bullets.All(b => Math.Abs(b.Velocity.Length() - initialSpeed * game.BulletSpeedMul) < 0.01f),
                $"{scene}/{game.Difficulty}: {art} retains speed scaling");
            if (accel) Check(bullets.All(b => b.AccelCharging && Read<float>(b, "_accelDelay") >= 1.05f),
                "both Akari cameo attacks visibly charge before accelerating");
        }
    }

    private async Task CheckCharacterAttacks(GameManager game, Node2D world, Player player, Hud hud,
        IReadOnlyList<EnemySpec> characters)
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        game.SetStageTarget(10000);
        foreach (var spec in characters)
        {
            Check(_characterPatterns.Add(spec.Pattern) && spec.Fires, "character has a unique enabled attack");
            foreach (var diff in Enum.GetValues<GameManager.Diff>())
            {
                game.Difficulty = diff;
                pool.DespawnAll();
                player.GlobalPosition = new Vector2(Field.Left + 22f, 108f);
                var enemy = new MidEnemy();
                enemy.Configure(spec);
                world.AddChild(enemy);
                enemy.GlobalPosition = new Vector2(Field.Right - 80f, 108f);
                enemy.SetPhysicsProcess(false);
                var texture = Read<Texture2D>(enemy, "CurSprite", typeof(Enemy));
                using (var pixels = texture.GetImage())
                    Check(pixels.GetHeight() == 128 && pixels.GetPixel(0, 0).A < 0.05f
                        && pixels.DetectAlpha() != Image.AlphaMode.None, $"{spec.Pattern}/{diff}: transparent generated bullet loaded");
                string asset = texture.ResourcePath.GetFile().GetBaseName();
                Call(enemy, "BeginCharacterAttack");
                Call(enemy, "TickFire", 0.1d);
                Check(EnemyBullets().Length == 0, "warning never damages before firing");
                if (diff == GameManager.Diff.Normal)
                {
                    hud.HoldBubble = true;
                    hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/v3/mina_conversation_v1.png");
                    enemy.SetPhysicsProcess(true);
                    await Frames(3);
                    double warning = Read<double>(enemy, "_telegraphT");
                    await Frames(25);
                    Check(warning == Read<double>(enemy, "_telegraphT") && EnemyBullets().Length == 0,
                        "dialogue freezes pending character attacks");
                    enemy.SetPhysicsProcess(false);
                    hud.HoldBubble = false;
                    hud.HideBubble();
                    await Frames(3);
                }
                Vector2 lockedDirection = Read<Vector2>(enemy, "_burstDir");
                player.GlobalPosition = new Vector2(Field.Left + 22f, 184f);
                int salvos = Read<int>(enemy, "_salvoRemaining");
                Check(salvos >= (diff >= GameManager.Diff.Normal ? 3 : 2), "attack repeats across multiple salvos");
                int perSalvo = 0;
                for (int i = 0; i < salvos; i++)
                {
                    int before = EnemyBullets().Length;
                    Call(enemy, "TickFire", 2.0d);
                    int added = EnemyBullets().Length - before;
                    Check(added >= (diff >= GameManager.Diff.Normal ? 3 : 1), "each salvo contains the required bullet density");
                    if (i == 0) perSalvo = added;
                    Check(added == perSalvo, "consecutive salvos retain their complete formation");
                }
                var bullets = SettleLeadIn(EnemyBullets());
                int count = perSalvo * salvos;
                Check(bullets.Length == count && Read<int>(enemy, "_salvoRemaining") == 0,
                    $"{spec.Pattern}/{diff}: completes expected {count}-bullet pattern");
                Check(bullets.All(b => Read<Texture2D>(b, "_sprite").ResourcePath == texture.ResourcePath
                    && b.Radius >= 3.6f && b.Radius <= 4f && b.Damage == 1 && !b.Homing && !b.IsPatternBullet),
                    "every bullet uses its own illustration with consistent collision rules");
                Check(bullets.All(b => b.GlobalPosition.X > Field.Left && b.GlobalPosition.X < Field.Right
                    && b.GlobalPosition.Y > Field.Top && b.GlobalPosition.Y < Field.Bottom), "all projectiles originate inside the field");
                if (spec.Pattern == AttackPattern.ReiAnonymous)
                    Check(bullets.Any(b => Math.Abs(b.Velocity.Normalized().AngleTo(lockedDirection)) < 0.001f),
                        "anonymous burst retains the warned center while adding symmetric lanes");
                if (spec.Pattern == AttackPattern.ReiMetrics)
                    Check(bullets.All(b => b.Rotation == 0f), "ranking arrows always point down");
                if (spec.Pattern == AttackPattern.AkariVacant)
                {
                    var rows = bullets.Select(b => b.GlobalPosition.Y).OrderBy(y => y).ToArray();
                    Check(rows.Zip(rows.Skip(1), (a, b) => b - a).Max() >= 31f,
                        "ID badge wall leaves a visible escape gap");
                }
                if (diff == GameManager.Diff.Normal)
                {
                    string signature = string.Join(";", bullets.Select(b => $"{b.GlobalPosition.X:0},{b.GlobalPosition.Y:0}:{b.Velocity.X:0},{b.Velocity.Y:0}"));
                    Check(_attackSignatures.Add(signature), "projectile pattern differs from other characters");
                }
                if (spec.Pattern == AttackPattern.AkariDeadline)
                {
                    Check(bullets.All(b => b.AccelCharging && Math.Abs(b.Velocity.Length() - 10f * game.BulletSpeedMul) < 0.01f),
                        "clock projectiles begin with a readable charge");
                    foreach (var bullet in bullets) bullet._PhysicsProcess(game.DanmakuIntervalMul * 0.55d + 0.24d);
                    Check(bullets.All(b => !b.AccelCharging && Math.Abs(b.Velocity.Length() - 116f * game.BulletSpeedMul) < 0.01f),
                        "clock acceleration preserves difficulty scaling");
                }
                else
                {
                    float speed = spec.Pattern switch
                    {
                        AttackPattern.AkariUnsent => 64f, AttackPattern.AkariVacant => 52f,
                        AttackPattern.KoharuComparison => 84f, AttackPattern.KoharuCheer => 72f,
                        AttackPattern.KoharuParcel => 38f, AttackPattern.ReiAnonymous => 100f,
                        AttackPattern.ReiClipper => 85f, AttackPattern.ReiMetrics => 55f,
                        _ => 0f,
                    };
                    Check(bullets.All(b => Math.Abs(b.Velocity.Length() - speed * game.BulletSpeedMul) < 0.01f),
                        "projectile speed respects difficulty");
                }
                if (diff == GameManager.Diff.Normal)
                {
                    pool.DespawnAll();
                    Call(enemy, "BeginCharacterAttack");
                    int frames = 0;
                    while (Read<double>(enemy, "_telegraphT") > 0 || Read<int>(enemy, "_salvoRemaining") > 0)
                    {
                        Call(enemy, "TickFire", 1.0d / 60.0d);
                        await Frames(1);
                        if (++frames >= 300) throw new Exception("Timed character attack did not complete");
                    }
                    await Frames(24);
                    await Shot($"attack_{asset}");
                    if (spec.Pattern == AttackPattern.ReiMetrics)
                        Check(EnemyBullets().All(b => b.Rotation == 0f), "ranking arrows retain their orientation in flight");
                    hud.HoldBubble = true;
                    hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/v3/mina_conversation_v1.png");
                    await Frames(3);
                    var active = EnemyBullets();
                    var positions = active.Select(b => b.GlobalPosition).ToArray();
                    await Frames(20);
                    Check(active.Select((b, i) => b.GlobalPosition == positions[i]).All(x => x), "dialogue freezes generated projectiles");
                    hud.HoldBubble = false;
                    hud.HideBubble();
                    await Frames(3);
                }
                pool.DespawnAll();
                Call(enemy, "BeginCharacterAttack");
                if (spec.Pattern is AttackPattern.AkariUnsent or AttackPattern.KoharuCheer or AttackPattern.KoharuParcel or AttackPattern.ReiAnonymous)
                {
                    Call(enemy, "TickFire", 2.0d);
                    pool.DespawnAll();
                }
                enemy.Purify();
                enemy.SetPhysicsProcess(true);
                await Frames(100);
                Check(EnemyBullets().Length == 0, "purification cancels warnings and remaining salvos");
                if (IsInstanceValid(enemy)) enemy.QueueFree();
                await Frames(2);
                var reused = pool.Spawn(new Vector2(Field.Right - 40f, 108f), Vector2.Left * 30f, true);
                Check(Read<Texture2D?>(reused, "_sprite") == null && !reused.Accel, "pool reuse clears sprite and acceleration state");
                pool.DespawnAll();
            }
        }
        game.Difficulty = GameManager.Diff.Normal;
    }

    // 条件が成立するまで最大 limit フレーム待つ（成立したら true）。
    private async Task<bool> WaitFrames(Func<bool> cond, int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            if (cond()) return true;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return cond();
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--motion-compare")
            ? "res://build/qa_story/enemy_readable_motion_20261029/shots" : "res://build/qa_story/enemy_roster/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
