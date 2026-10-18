using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossPacingQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value, Type? type = null)
        => (type ?? obj.GetType()).GetField(name, Private)!.SetValue(obj, value);
    private static void Call(object obj, string name, Type? type = null, params object[] args)
        => (type ?? obj.GetType()).GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[BossPacingQA] PASS {message}");
    }
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private static bool InFinale(Enemy boss) => boss switch
    {
        BossMina mina => mina.EncounterPhase == 4,
        BossAkari => Read<bool>(boss, "_lowHpRallied"),
        BossRei => Read<bool>(boss, "_rallied"),
        BossKoharu => Read<bool>(boss, "_finale"),
        _ => false,
    };

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.AutoAdvanceDialog = true;
            game.MsgCharsPerSec = 300;
            await Frames(2);
            var args = OS.GetCmdlineUserArgs();
            if (args.Contains("--pressure"))
            {
                await AuditPressure(game);
                GD.Print("[BossPacingQA] PRESSURE COMPLETE");
                GetTree().Quit();
                return;
            }
            var jobs = args.Contains("--all-jobs") ? Enum.GetValues<Job>() : new[] { Job.Tank };
            var stages = args.Contains("--cameo") ? new[] { "Akari", "Koharu", "Rei" }
                : args.Contains("--final-stages") ? new[] { "Rei", "MinaBattle" }
                : new[] { "Akari", "Koharu", "Rei", "MinaBattle" };
            if (args.Contains("--health")) await AuditHealth(game);
            else foreach (bool powered in new[] { false, true })
            {
                if (!powered && args.Contains("--powered-only")) continue;
                foreach (var job in jobs)
                    foreach (string id in stages)
                        await Encounter(game, id, powered, job);
            }
            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[BossPacingQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[BossPacingQA] FAIL {e}");
            GetTree().Quit(1);
        }
    }

    private async Task AuditHealth(GameManager game)
    {
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = difficulty;
            foreach (string id in new[] { "Akari", "Koharu", "Rei", "MinaBattle", "Hikage" })
            {
                var root = GD.Load<PackedScene>($"res://{(id == "Hikage" ? "Akari" : id)}.tscn").Instantiate<Node2D>();
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
                stage.SetProcess(false);
                root.GetNode<Player>("World/Player").SetPhysicsProcess(false);
                root.GetNode<Hud>("Hud").HideBubble();
                var world = root.GetNode<Node2D>("World");
                Enemy boss = id switch
                {
                    "Akari" => new BossAkari(), "Koharu" => new BossKoharu(), "Rei" => new BossRei(),
                    "MinaBattle" => new BossMina(), _ => new BossHikage(),
                };
                boss.ProcessMode = ProcessModeEnum.Disabled;
                world.AddChild(boss);
                int hp = (id switch
                {
                    "Akari" => new[] { 300, 375, 450, 525 },
                    "Koharu" or "Hikage" => new[] { 400, 500, 600, 700 },
                    "Rei" => new[] { 500, 625, 750, 875 },
                    _ => new[] { 600, 750, 900, 1050 },
                })[(int)difficulty];
                Check(Read<int>(boss, "_maxHp", typeof(Enemy)) == hp && Read<int>(boss, "_hp", typeof(Enemy)) == hp,
                    $"{difficulty}/{id}: initialized HP {hp}, without technique-repeat inflation");
                int barHp = new[] { 100, 125, 150, 175 }[(int)difficulty];
                Check(boss.BarHp == barHp && boss.CurrentBarIndex == boss.TotalBars - 1 && boss.CurrentBarFrac == 1,
                    $"{difficulty}/{id}: full HP bar uses {barHp} HP");
                Write(boss, "_hp", hp - barHp, typeof(Enemy));
                Check(boss.CurrentBarIndex == boss.TotalBars - 2 && boss.CurrentBarFrac == 1,
                    $"{difficulty}/{id}: draining one scaled bar advances exactly one bar");
                Write(boss, "_hp", 1, typeof(Enemy));
                Check(boss.CurrentBarIndex == 0 && Mathf.IsEqualApprox(boss.CurrentBarFrac, 1f / barHp),
                    $"{difficulty}/{id}: last HP is visible in the final bar");
                if (difficulty != GameManager.Diff.Lunatic && boss.GetNodeOrNull<BossPostSequence>("PostSequence") is { } sequence)
                {
                    typeof(BossPostSequence).GetProperty("Count")!.SetValue(sequence, 4);
                    Write(boss, "_hp", Mathf.RoundToInt(hp * 0.01f), typeof(Enemy));
                    Check(sequence.Pending, $"{difficulty}/{id}: final post is reachable at its rounded HP floor");
                }
                game.Difficulty = difficulty == GameManager.Diff.Easy ? GameManager.Diff.Lunatic : GameManager.Diff.Easy;
                Check(boss.BarHp == barHp && Read<int>(boss, "_maxHp", typeof(Enemy)) == hp,
                    $"{difficulty}/{id}: existing encounter keeps its initialized HP scale");
                game.Difficulty = difficulty;
                boss.QueueFree();
                await Frames(2);
                if (id is "Akari" or "Koharu" or "Rei")
                {
                    game.MarkIdleDialogSeen("once_midboss_shield");
                    Write(stage, "_cameoIntroDone", true);
                    Write(stage, "_stepStarted", false);
                    Call(stage, "Step_BossCameo", null, 0d);
                    var cameo = world.GetChildren().OfType<CameoBoss>().Single();
                    cameo.ProcessMode = ProcessModeEnum.Disabled;
                    int midHp = new[] { 200, 250, 300, 350 }[(int)difficulty];
                    Check(Read<int>(cameo, "_maxHp", typeof(Enemy)) == midHp && Read<int>(cameo, "_hp", typeof(Enemy)) == midHp,
                        $"{difficulty}/{id} midboss: stage-spawned HP {midHp}");
                }
                root.QueueFree();
                await Frames(4);
                Pool.DespawnAll();
                Hud.BubblePaused = false;
            }
        }
    }

    private async Task Encounter(GameManager game, string id, bool powered, Job job)
    {
        Check(Mathf.IsEqualApprox(Engine.TimeScale, 1), "previous encounter released its hitstop");
        var args = OS.GetCmdlineUserArgs();
        game.Difficulty = args.Contains("--easy") ? GameManager.Diff.Easy
            : args.Contains("--hard") ? GameManager.Diff.Hard
            : args.Contains("--lunatic") ? GameManager.Diff.Lunatic : GameManager.Diff.Normal;
        game.SelectedJob = job;
        bool charged = powered && !args.Contains("--rapid");
        game.TrainingSetAllUpgrades(powered);
        typeof(GameManager).GetProperty("Followers")!.SetValue(game, powered ? 2000 : 0);
        var root = GD.Load<PackedScene>($"res://{id}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
        stage.SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Inherit;
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        var player = world.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        if (args.Contains("--line-power"))
            for (int i = 0; i < Player.PowerLevelCap; i++) player.ApplyPowerup(PowerKind.Line);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        Enemy boss;
        if (args.Contains("--cameo"))
        {
            game.MarkIdleDialogSeen("once_midboss_shield");
            Write(stage, "_cameoIntroDone", true);
            Write(stage, "_stepStarted", false);
            Call(stage, "Step_BossCameo", null, 0d);
            boss = world.GetChildren().OfType<CameoBoss>().Single();
            id += "Cameo";
        }
        else
        {
            boss = id switch
            {
                "Akari" => new BossAkari(), "Koharu" => new BossKoharu(),
                "Rei" => new BossRei(), _ => new BossMina(),
            };
            boss.Position = new Vector2(Field.BossCenterX, Field.BossZoneCenterY);
            world.AddChild(boss);
        }
        Write(player, "_locked", true);
        Write(player, "_lockTarget", boss);
        if (args.Contains("--shields-only") || args.Contains("--cameo"))
        {
            await Shields(game, boss, player, hud, id, powered, charged);
            hud.HoldBubble = false;
            hud.HideBubble();
            while (Engine.TimeScale < 0.99) await Frames(1);
            root.QueueFree();
            await Frames(5);
            Pool.DespawnAll();
            Hud.BubblePaused = false;
            return;
        }
        double combat = 0, shield = 0, finale = 0, fire = 0;
        var phaseLengths = new List<string>();
        double phaseLength = 0;
        float phaseHp = boss.HpRatio;
        int breaks = 0, posts = 0;
        string previousPhase = "";
        for (int frame = 0; frame < 90000 && !boss.IsPurified; frame++)
        {
            if (frame % 1800 == 0)
                GD.Print($"[BossPacingQA] STATUS {id}/{game.Difficulty} frame={frame} hp={boss.HpRatio:F3} state={Read<object>(boss, "_phase", typeof(Enemy))} timeScale={Engine.TimeScale:F2} talking={Hud.BubblePaused}");
            // Cinematics are exercised by story QA; this benchmark measures only live combat.
            foreach (var group in new[] { "storyfilm", "mina_phase_scene", "boss_draft" })
            {
                if (GetTree().GetFirstNodeInGroup(group) is not Node scene) continue;
                Type type = scene is StoryFilm ? typeof(StoryFilm) : scene.GetType();
                Call(scene, "Restore", type);
                Read<Action>(scene, "_completed", type)();
                scene.QueueFree();
            }
            if (GetTree().GetFirstNodeInGroup("boss_post") is BossPost { IsBroken: true } post && !post.IsQueuedForDeletion())
            {
                float hp = boss.HpRatio;
                post._PhysicsProcess(BossPost.BreakDuration + 1);
                if (post.IsQueuedForDeletion()) posts++;
                if (post.IsQueuedForDeletion() && posts < 5 && boss is not BossMina)
                {
                    Check(boss.HpRatio == hp, "post effect adds no health");
                    if (posts == 4) Check(InFinale(boss), "final phase begins before the original draft");
                }
            }
            bool playing = !Hud.BubblePaused && !hud.CinematicMode && boss.Visible
                && !Read<bool>(boss, "_entering", typeof(Enemy));
            if (playing)
            {
                combat += 1d / 60;
                string phase = Read<object>(boss, "_phase", typeof(Enemy)).ToString()!;
                if (phase != previousPhase)
                {
                    if (previousPhase.Length > 0)
                        phaseLengths.Add($"{previousPhase}:{phaseLength:F1}s/{(phaseHp - boss.HpRatio) * 100:F1}%HP");
                    phaseLength = 0;
                    phaseHp = boss.HpRatio;
                }
                phaseLength += 1d / 60;
                if (phase == "Shielded") shield += 1d / 60;
                bool inFinale = InFinale(boss);
                if (phase == "Break" && previousPhase != phase)
                {
                    breaks++;
                }
                previousPhase = phase;
                if (inFinale) finale += 1d / 60;
                float distance = game.SelectedJob == Job.Melee ? 42 : game.SelectedJob == Job.Magic ? 140 : 90;
                player.GlobalPosition = new Vector2(Mathf.Max(Field.Left + 18, boss.GlobalPosition.X - distance),
                    Mathf.Clamp(boss.GlobalPosition.Y, Field.Top + 18, Field.Bottom - 18));
                fire -= 1d / 60;
                if (fire <= 0)
                {
                    if (charged)
                    {
                        Call(player, "FireCharge", null, ChargeTier.Second);
                        fire = game.ChargeTier2NeedSec;
                    }
                    else
                    {
                        Call(player, "Fire");
                        float mode = game.SelectedShotMode switch
                        {
                            GameManager.ShotMode.Homing => game.HomingRateMul,
                            GameManager.ShotMode.Spread => game.SpreadRateMul,
                            GameManager.ShotMode.Accel => 1f, _ => game.RapidRateMul,
                        };
                        fire = 0.13 * mode;
                    }
                }
            }
            await Frames(1);
        }
        GD.Print($"[BossPacingQA] RESULT {id} difficulty={game.Difficulty} job={game.SelectedJob} powered={powered} charged={charged} combat={combat:F1}s shield={shield:F1}s finale={finale:F1}s breaks={breaks} posts={posts} hp={boss.HpRatio:F3}");
        phaseLengths.Add($"{previousPhase}:{phaseLength:F1}s/{(phaseHp - boss.HpRatio) * 100:F1}%HP");
        GD.Print($"[BossPacingQA] CYCLES {id}/{game.SelectedJob} {string.Join(" | ", phaseLengths)}");
        Check(boss.IsPurified, $"{id}/{powered}: encounter completes");
        Check(posts == (GameManager.LunaticActive ? 0 : 5), $"{id}/{powered}: correct post count");
        Check(breaks >= 1, $"{id}/{powered}: body damage requires a shield break without forcing an extra final break");
        hud.HoldBubble = false;
        hud.HideBubble();
        while (Engine.TimeScale < 0.99) await Frames(1);
        root.QueueFree();
        await Frames(5);
        Pool.DespawnAll();
        Hud.BubblePaused = false;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shields(GameManager game, Enemy boss, Player player, Hud hud, string id, bool powered, bool charged)
    {
        int expectedInk = boss is CameoBoss ? 18 : id switch
        {
            "Akari" => 36, "Koharu" => 48, "Rei" => 54, "MinaBattle" => 66,
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        Check(Read<int>(boss, "PanelInk", typeof(Enemy)) == expectedInk
            && Read<List<Panel>>(boss, "_panels", typeof(Enemy)).All(panel => panel.Ink == expectedInk),
            $"{id}: shield panels start with the increased durability ({expectedInk})");
        var times = new List<double>();
        double elapsed = 0, fire = 0;
        string previous = "";
        for (int frame = 0; frame < 12000 && times.Count < 3; frame++)
        {
            if (!Read<bool>(boss, "_entering", typeof(Enemy)) && !Hud.BubblePaused && !hud.CinematicMode)
            {
                string phase = Read<object>(boss, "_phase", typeof(Enemy)).ToString()!;
                if (phase == "Break" && previous == "Shielded")
                {
                    times.Add(elapsed);
                    elapsed = 0;
                    Pool.DespawnAll();
                }
                previous = phase;
                if (phase == "Shielded")
                {
                    elapsed += 1d / 60;
                    float distance = game.SelectedJob == Job.Melee ? 42 : game.SelectedJob == Job.Magic ? 140 : 90;
                    player.GlobalPosition = new Vector2(Mathf.Max(Field.Left + 18, boss.GlobalPosition.X - distance),
                        Mathf.Clamp(boss.GlobalPosition.Y, Field.Top + 18, Field.Bottom - 18));
                    fire -= 1d / 60;
                    if (fire <= 0)
                    {
                        if (charged)
                        {
                            Call(player, "FireCharge", null, ChargeTier.Second);
                            fire = game.ChargeTier2NeedSec;
                        }
                        else
                        {
                            Call(player, "Fire");
                            float mode = game.SelectedShotMode switch
                            {
                                GameManager.ShotMode.Homing => game.HomingRateMul,
                                GameManager.ShotMode.Spread => game.SpreadRateMul,
                                GameManager.ShotMode.Accel => 1f, _ => game.RapidRateMul,
                            };
                            fire = .13 * mode;
                        }
                    }
                }
            }
            await Frames(1);
        }
        Check(times.Count == 3, $"{id}: three shield cycles complete");
        GD.Print($"[BossPacingQA] SHIELD {id} job={game.SelectedJob} powered={powered} charged={charged} lines={player.LinePower} seconds={string.Join(",", times.Select(t => t.ToString("F2")))} mean={times.Average():F2}");
    }
    private async Task AuditPressure(GameManager game)
    {
        var args = OS.GetCmdlineUserArgs();
        bool shots = args.Contains("--pressure-shots");
        bool stock = args.Contains("--stock");
        var difficulty = args.Contains("--hard") ? GameManager.Diff.Hard : args.Contains("--lunatic") ? GameManager.Diff.Lunatic
            : args.Contains("--easy") ? GameManager.Diff.Easy : GameManager.Diff.Normal;
        foreach (string id in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
            foreach (string mode in shots ? new[] { "evolved" } : new[] { "route", "boss", "evolved" })
            {
                if (mode == "route" && args.Contains("--boss-only")) continue;
                game.Difficulty = difficulty;
                game.SelectedJob = stock && id != "MinaBattle" ? Job.Tank : Job.Magic;
                game.TrainingSetAllUpgrades(!stock);
                typeof(GameManager).GetProperty("Followers")!.SetValue(game, stock ? 0 : 2000);
                var root = GD.Load<PackedScene>($"res://{id}.tscn").Instantiate<Node2D>();
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                root.SetProcess(false);
                var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
                stage.SetProcess(false);
                var world = root.GetNode<Node2D>("World");
                world.ProcessMode = ProcessModeEnum.Inherit;
                var hud = root.GetNode<Hud>("Hud");
                hud.HideBubble();
                var player = world.GetNode<Player>("Player");
                player.SetPhysicsProcess(false);
                Write(player, "_invincible", true);
                Write(player, "_invincibleTimer", 999f);
                game.SetStageTarget(99999);
                var rng = new RandomNumberGenerator { Seed = 934 };
                Enemy? boss = null;
                if (mode == "route")
                {
                    if (id == "MinaBattle") { Write(stage, "_wave", 2); Call(stage, "StartEchoWave"); }
                    else Call(stage, "StartMidwaveSpawner", null, 0, 0.7f);
                    var spawner = stage.GetChildren().OfType<Spawner>().Single();
                    Read<RandomNumberGenerator>(spawner, "_rng").Seed = 934;
                    spawner.SpawnLimit = 0;
                }
                else
                {
                    boss = id switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(),
                        "Rei" => new BossRei(), _ => new BossMina() };
                    world.AddChild(boss);
                    boss.Position = new Vector2(Field.BossCenterX, Field.CenterY);
                    Call(boss, "TickEntrance", typeof(Enemy), 0d);
                    Call(boss, "TickEntrance", typeof(Enemy), 100d);
                    if (mode == "evolved")
                    {
                        if (boss is BossMina)
                        {
                            Write(boss, "_pattern", BossMina.DragonPhase - 1);
                            game.Difficulty = GameManager.Diff.Lunatic;
                            Call(boss, "BeginPhaseTransition");
                            game.Difficulty = difficulty;
                            Call(boss, "CompletePhaseTransition");
                            for (int frame = 0; frame < 360 && boss.Transforming; frame++) await Frames(1);
                        }
                        else
                        {
                            Call(boss, "AdvanceForm2", typeof(Enemy));
                            for (int frame = 0; frame < 360 && boss.Transforming; frame++) await Frames(1);
                        }
                    }
                    boss.SetPanelsInvulnerable(true);
                }
                hud.HideBubble();
                await Frames(3);
                double fire = 0, sum = 0, empty = 0, longestEmpty = 0, currentEmpty = 0, aoe = 0;
                int samples = 0, peak = 0, contacts = 0;
                bool touching = false;
                double ambient = 0;
                int word = 0;
                var emitted = new HashSet<(ulong, ulong)>();
                var charged = new HashSet<(ulong, ulong)>();
                var areas = new HashSet<ulong>();
                var techniques = new HashSet<ulong>();
                for (int frame = 0; frame < (shots ? 1200 : 3600); frame++)
                {
                    double time = frame / 60d;
                    player.Position = new Vector2(Field.Left + 42, Field.CenterY + 48 * Mathf.Sin((float)time * 0.55f));
                    Enemy? target = boss ?? GetTree().GetNodesInGroup("enemies").OfType<Enemy>()
                        .Where(e => !e.IsPurified && Field.Rect.HasPoint(e.Position))
                        .MinBy(e => player.Position.DistanceSquaredTo(e.Position));
                    Write(player, "_locked", target != null);
                    Write(player, "_lockTarget", target!);
                    fire -= 1d / 60;
                    if (target != null && fire <= 0)
                    {
                        Call(player, "FireCharge", null, ChargeTier.Second);
                        fire = game.ChargeTier2NeedSec;
                    }
                    bool ambientBlocked = boss switch
                    {
                        BossAkari b => b.PostSequenceActive || b.EdgeAttackActive,
                        BossKoharu b => b.PostSequenceActive || b.EdgeAttackActive,
                        BossRei b => b.PostSequenceActive || b.AoeGateActive,
                        BossMina b => b.AoeGateActive,
                        _ => false,
                    };
                    if (boss != null && !ambientBlocked)
                    {
                        var theme = id switch { "Akari" => PostPool.Theme.Akari, "Koharu" => PostPool.Theme.Koharu,
                            "Rei" => PostPool.Theme.Rei, _ => PostPool.Theme.Final };
                        float fallSpeed = id switch { "Koharu" => 44f, "Rei" => 46f, "MinaBattle" => 56f, _ => 48f };
                        PostBullets.Tick(stage, rng, 1d / 60, ref ambient, ref word, boss, theme, fallSpeed,
                            new Color("789bcd"), murkAll: id is "Koharu" or "MinaBattle");
                    }
                    await Frames(1);
                    if (frame < 600 || Hud.BubblePaused) continue;
                    var bullets = GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>()
                        .Where(b => b.Active && !b.OverheadPending && b.CollisionLayer != 0 && Field.Rect.HasPoint(b.Position)).ToArray();
                    foreach (var b in bullets)
                    {
                        var key = (b.GetInstanceId(), b.ActivationId);
                        emitted.Add(key);
                        if (b.Accel) charged.Add(key);
                    }
                    if (shots && frame > 720 && bullets.Any(b => b.AccelCharging))
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        string folder = ProjectSettings.GlobalizePath("res://build/qa_story/combat_revision");
                        DirAccess.MakeDirRecursiveAbsolute(folder);
                        using var screenshot = GetViewport().GetTexture().GetImage();
                        screenshot.SavePng(folder + "/" + id + "_charged_live.png");
                        break;
                    }
                    var hazards = GetTree().GetNodesInGroup("aoe").OfType<Node>().ToArray();
                    foreach (var area in hazards) areas.Add(area.GetInstanceId());
                    foreach (var technique in GetTree().GetNodesInGroup("boss_animal_techniques")) techniques.Add(technique.GetInstanceId());
                    sum += bullets.Length;
                    bool nextTouch = bullets.Any(b => BossCharacterShotQa.HitsPlayer(b, player.Position));
                    if (nextTouch && !touching) contacts++;
                    touching = nextTouch;
                    peak = Math.Max(peak, bullets.Length);
                    if (hazards.Length > 0) aoe += 1d / 60;
                    if (bullets.Length == 0 && hazards.Length == 0)
                    { empty += 1d / 60; currentEmpty += 1d / 60; longestEmpty = Math.Max(longestEmpty, currentEmpty); }
                    else currentEmpty = 0;
                    samples++;
                }
                GD.Print($"[BossPacingQA] PRESSURE {id}/{mode}/{difficulty}/stock={stock} mean={sum / samples:F1} peak={peak} unique={emitted.Count} charged={charged.Count} aoe={areas.Count}/{aoe:F1}s techniques={techniques.Count} empty={empty:F1}s longest={longestEmpty:F1}s contacts={contacts} samples={samples}");
                if (mode != "route" && difficulty >= GameManager.Diff.Hard && !shots)
                    Check(samples >= 1200 && longestEmpty < 2.5 && emitted.Count >= 350 && contacts >= 8,
                        $"{id}/{mode}: the live boss keeps pressure and punishes simple one-axis movement");
                if (mode == "evolved" && boss is not BossMina && !shots)
                    Check(areas.Count > 0 && techniques.Count > 0, id + ": evolved techniques and area spells both appear in the real rotation");
                root.QueueFree();
                await Frames(5);
                Pool.DespawnAll();
                Hud.BubblePaused = false;
                Engine.TimeScale = 1;
            }
    }
}
