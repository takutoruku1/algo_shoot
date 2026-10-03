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
            var jobs = args.Contains("--all-jobs") ? Enum.GetValues<Job>() : new[] { Job.Tank };
            foreach (bool powered in new[] { false, true })
            {
                if (!powered && args.Contains("--powered-only")) continue;
                foreach (var job in jobs)
                    foreach (string id in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
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
        ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Inherit;
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        var player = world.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        Enemy boss = id switch
        {
            "Akari" => new BossAkari(), "Koharu" => new BossKoharu(),
            "Rei" => new BossRei(), _ => new BossMina(),
        };
        boss.Position = new Vector2(Field.BossCenterX, Field.BossZoneCenterY);
        world.AddChild(boss);
        Write(player, "_locked", true);
        Write(player, "_lockTarget", boss);
        if (args.Contains("--shields-only"))
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
                    Check(!boss.GaugeVulnerable, "new post depth starts a new shield fight");
                    if (posts == 4) Check(Read<bool>(boss, "_finale"), "final attack begins before the original draft");
                }
            }
            bool playing = !Hud.BubblePaused && !hud.CinematicMode && boss.Visible
                && !Read<bool>(boss, "_entering", typeof(Enemy));
            if (playing)
            {
                combat += 1d / 60;
                string phase = Read<object>(boss, "_phase", typeof(Enemy)).ToString()!;
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
        GD.Print($"[BossPacingQA] SHIELD {id} job={game.SelectedJob} powered={powered} charged={charged} seconds={string.Join(",", times.Select(t => t.ToString("F2")))} mean={times.Average():F2}");
    }
}
