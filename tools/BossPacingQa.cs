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

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
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
            foreach (bool powered in new[] { false, true })
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

    private async Task Encounter(GameManager game, string id, bool powered, Job job)
    {
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
        int breaks = 0, posts = 0, finalBreaks = 0;
        string previousPhase = "";
        for (int frame = 0; frame < 90000 && !boss.IsPurified; frame++)
        {
            // Cinematics are exercised by story QA; this benchmark measures only live combat.
            foreach (var group in new[] { "storyfilm", "mina_phase_scene", "boss_draft" })
            {
                if (GetTree().GetFirstNodeInGroup(group) is not Node scene) continue;
                Type type = scene is StoryFilm ? typeof(StoryFilm) : scene.GetType();
                Call(scene, "Restore", type);
                Read<Action>(scene, "_completed", type)();
                scene.QueueFree();
            }
            if (GetTree().GetFirstNodeInGroup("boss_post") is BossPost post && !post.IsQueuedForDeletion())
            {
                float hp = boss.HpRatio;
                Call(post, "Damage", null, 24);
                post._PhysicsProcess(post.MinimumReadTime + post.BreakDuration + 1);
                post._PhysicsProcess(post.BreakDuration + 1);
                posts++;
                if (posts < 5 && boss is not BossMina)
                {
                    Check(boss.HpRatio == hp, "rally adds no health");
                    if (posts % 2 == 0) Check(!boss.GaugeVulnerable, "two post depths share one shield break");
                    if (posts == 4) Check(Read<bool>(boss, "_finale"), "final attack begins before the original draft");
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
                bool inFinale = boss is BossMina mina ? mina.EncounterPhase == 4 : Read<bool>(boss, "_finale");
                if (phase == "Break" && previousPhase != phase)
                {
                    breaks++;
                    if (inFinale) finalBreaks++;
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
                        fire = 0.13 * game.FireIntervalMul * mode;
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
        Check(finalBreaks >= 1, $"{id}/{powered}: final attack includes a shield break and counterattack");
        hud.HoldBubble = false;
        hud.HideBubble();
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
                            fire = .13 * game.FireIntervalMul * mode;
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
        bool shots = OS.GetCmdlineUserArgs().Contains("--pressure-shots");
        foreach (string id in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
            foreach (string mode in shots ? new[] { "evolved" } : new[] { "route", "boss", "evolved" })
            {
                game.Difficulty = GameManager.Diff.Normal;
                game.SelectedJob = Job.Magic;
                game.TrainingSetAllUpgrades(true);
                typeof(GameManager).GetProperty("Followers")!.SetValue(game, 2000);
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
                    else Call(stage, "StartMidwaveSpawner", null, 0.7f);
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
                            Write(boss, "_pattern", 1);
                            game.Difficulty = GameManager.Diff.Lunatic;
                            Call(boss, "BeginPhaseTransition");
                            game.Difficulty = GameManager.Diff.Normal;
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
                int samples = 0, peak = 0;
                double ambient = 0;
                int word = 0;
                var emitted = new HashSet<(ulong, ulong)>();
                var charged = new HashSet<(ulong, ulong)>();
                var areas = new HashSet<ulong>();
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
                    sum += bullets.Length;
                    peak = Math.Max(peak, bullets.Length);
                    if (hazards.Length > 0) aoe += 1d / 60;
                    if (bullets.Length == 0 && hazards.Length == 0)
                    { empty += 1d / 60; currentEmpty += 1d / 60; longestEmpty = Math.Max(longestEmpty, currentEmpty); }
                    else currentEmpty = 0;
                    samples++;
                }
                GD.Print($"[BossPacingQA] PRESSURE {id}/{mode} mean={sum / samples:F1} peak={peak} unique={emitted.Count} charged={charged.Count} aoe={areas.Count}/{aoe:F1}s empty={empty:F1}s longest={longestEmpty:F1}s samples={samples}");
                root.QueueFree();
                await Frames(5);
                Pool.DespawnAll();
                Hud.BubblePaused = false;
                Engine.TimeScale = 1;
            }
    }
}
