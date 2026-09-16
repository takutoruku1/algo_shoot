using Godot;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class ScoreShardQa : Node
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Fields)!.GetValue(obj)!;
    private static IList Shards(ScoreShards drops) => Read<IList>(drops, "_shards");
    private static int Value(ScoreShards drops) => Shards(drops).Cast<object>().Sum(s => Read<int>(s, "Points"));
    private static FxLayer.P[] Particles(FxLayer fx) => Read<IList>(fx, "_p").Cast<FxLayer.P>().ToArray();
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[ScoreShardQA] PASS {message}");
    }
    private static void Tick(ScoreShards drops, int frames)
    {
        for (int i = 0; i < frames; i++) drops._PhysicsProcess(1.0 / 60);
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--movie"))
            {
                await Movie(game);
                GetTree().Quit();
                return;
            }
            foreach (var job in Jobs.All)
            {
                game.SelectedJob = job.Id;
                var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                root.Stage.SetProcess(false);
                root.World.ProcessMode = ProcessModeEnum.Inherit;
                root.Player.SetPhysicsProcess(false);
                root.Player.GlobalPosition = new Vector2(Field.Left + 28, 150);
                root.Hud.HoldBubble = false;
                root.Hud.HideBubble();
                game.SetStageTarget(999);
                var fx = root.World.GetNode<FxLayer>("FxLayer");
                var drops = fx.ScoreDrops;
                drops.SetPhysicsProcess(false);
                await Frames(10);

                Read<RandomNumberGenerator>(fx, "_rng").Seed = 713;
                fx.PurifyBurst(new Vector2(302, 100));
                var original = Particles(fx).Where(p => p.Type is FxLayer.T.Petal or FxLayer.T.HeartP).ToArray();
                Check(Shards(drops).Count == 0, "non-defeat effects stay decorative and cannot award points");
                Read<IList>(fx, "_p").Clear();
                Read<RandomNumberGenerator>(fx, "_rng").Seed = 713;
                fx.PurifyBurst(new Vector2(302, 100), 80);
                var retained = Shards(drops).Cast<object>().Select(s => Read<FxLayer.P>(s, "Particle")).ToArray();
                Check(original.Length == retained.Length && original.Zip(retained, (a, b) =>
                    a.Type == b.Type && a.Col == b.Col && a.Size == b.Size && a.Rot == b.Rot && a.Spin == b.Spin
                    && a.Vx == b.Vx && a.Vy == b.Vy && a.Grav == b.Grav && a.Drag == b.Drag).All(same => same),
                    "pickups retain the original diamonds and hearts, palette, size, rotation and scatter velocity");
                Check(!Particles(fx).Any(p => p.Type is FxLayer.T.Petal or FxLayer.T.HeartP),
                    "defeat particles are transferred, never duplicated by another pickup burst");
                Check(retained.Any(p => p.Type == FxLayer.T.Petal) && retained.Any(p => p.Type == FxLayer.T.HeartP),
                    "both original particle shapes remain collectible");
                Shards(drops).Clear();
                Read<IList>(fx, "_p").Clear();

                var enemy = new PageShard { Position = new Vector2(302, 100) };
                root.World.AddChild(enemy);
                enemy.SetPhysicsProcess(false);
                var panels = enemy.GetChildren().OfType<Panel>().ToArray();
                long defeatScore = game.Score + 80 + panels.Length * 5;
                foreach (var panel in panels) panel.Shatter();
                int dropCount = Shards(drops).Count;
                Check(enemy.IsPurified && dropCount >= 10 && dropCount <= 16,
                    $"{job.CharacterId}: actual enemy defeat keeps its original particle count");
                Check(game.Score == defeatScore && game.Combo == 1 && game.PurifiedCount == 1,
                    "original defeat score, combo and stage progress are unchanged");
                int bonus = Value(drops);
                Check(bonus == 8, "shards carry a small separate pickup bonus");
                enemy.Purify();
                Check(Shards(drops).Count == dropCount && game.Score == defeatScore, "repeat defeat cannot duplicate rewards");
                Tick(drops, 30);
                Check(Shards(drops).Count == dropCount && game.Score == defeatScore
                    && Shards(drops).Cast<object>().All(s => !Read<bool>(s, "Attracted")), "distant shards scatter without collecting");
                var positions = Shards(drops).Cast<object>().Select(s => Read<Vector2>(s, "Position")).ToArray();
                Check(positions.Distinct().Count() == dropCount && positions.Max(p => p.DistanceTo(enemy.Position)) > 12,
                    "visible outward burst has distinct trajectories");
                await Shot(job.CharacterId + "_scatter");

                root.Hud.HoldBubble = true;
                root.Hud.ShowMessage("QA");
                float age = Read<float>(Shards(drops)[0]!, "Age");
                Tick(drops, 360);
                Check(!drops.Visible && Shards(drops).Count == dropCount && Read<float>(Shards(drops)[0]!, "Age") == age,
                    "dialogue hides and freezes shards without spending their lifetime");
                root.Hud.HoldBubble = false;
                root.Hud.HideBubble();
                drops.SetPhysicsProcess(true);
                GetTree().Paused = true;
                await Frames(20);
                Check(Read<float>(Shards(drops)[0]!, "Age") == age, "pause menu freezes pickups");
                GetTree().Paused = false;
                drops.SetPhysicsProcess(false);

                long money = game.Impression;
                root.Player.GlobalPosition = new Vector2(302, 100);
                Tick(drops, 2);
                Check(Shards(drops).Cast<object>().All(s => Read<bool>(s, "Attracted")), "entering the radius starts attraction");
                root.Player.GlobalPosition = new Vector2(205, 160);
                await Shot(job.CharacterId + "_attract");
                Tick(drops, 150);
                Check(Shards(drops).Count == 0 && game.Score == defeatScore + bonus,
                    "attracted shards follow the moving player and award their points exactly once");
                Check(game.Combo == 1 && game.PurifiedCount == 1 && game.Impression == money,
                    "collecting cannot farm combo, stage progress or shop currency");
                Check(!Particles(fx).Any(p => p.Type == FxLayer.T.Dmg && p.Text.StartsWith("+")),
                    "pickup rewards update the HUD without floating plus-point labels");
                Tick(drops, 90);
                Check(game.Score == defeatScore + bonus, "collected shards never score twice");
                await Shot(job.CharacterId + "_collected");

                root.Player.GlobalPosition = new Vector2(Field.Left + 10, 190);
                fx.PurifyBurst(new Vector2(340, 65), 100);
                Tick(drops, 330);
                Check(Shards(drops).Count == 0 && game.Score == defeatScore + bonus, "uncollected shards expire without awarding points");
                fx.PurifyBurst(new Vector2(Field.Right + 30, -20), 100);
                Tick(drops, 1);
                Check(Shards(drops).Cast<object>().All(s => Field.Rect.HasPoint(Read<Vector2>(s, "Position"))),
                    "edge drops stay inside the playable field");
                Shards(drops).Clear();

                game.ResetRun();
                game.SetStageTarget(999);
                for (int i = 0; i < 4; i++)
                {
                    int previousCount = Shards(drops).Count;
                    var bombEnemy = new PageShard { Position = new Vector2(320, 80) };
                    root.World.AddChild(bombEnemy);
                    bombEnemy.SetPhysicsProcess(false);
                    bombEnemy.Purify();
                    if (i == 3) Check(Shards(drops).Count == previousCount, "bomb-capped effects remain non-collectible");
                }
                Check(Value(drops) == 24 && game.PurifiedCount == 4 && game.Combo == 3,
                    "bomb reward cap also caps shard drops without blocking stage progress");
                Shards(drops).Clear();

                fx.PurifyBurst(new Vector2(350, 70), 1500, true);
                Check(Value(drops) == 150, "boss defeat preserves its larger pickup reward without adding particles");
                int clearBonus = Value(drops);
                long beforeClear = game.Score;
                game.SetStageTarget(game.PurifiedCount);
                root.Hud.HoldBubble = true;
                root.Hud.ShowMessage("QA");
                Tick(drops, 150);
                Check(Shards(drops).Count == 0 && game.Score == beforeClear + clearBonus,
                    "stage clear sweeps up all remaining shards even during the defeat dialogue");
                root.Hud.HoldBubble = false;
                root.Hud.HideBubble();
                game.SetStageTarget(999);

                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540) })
                {
                    DisplayServer.WindowSetSize(size);
                    root.Player.GlobalPosition = new Vector2(148, 150);
                    fx.PurifyBurst(new Vector2(280, 100), 100);
                    int runtimeCount = Shards(drops).Count;
                    long beforePickup = game.Score;
                    int runtimeBonus = Value(drops);
                    drops.SetPhysicsProcess(true);
                    await PhysicsFrames(40);
                    Check(Shards(drops).Count == runtimeCount && game.Score == beforePickup,
                        $"{job.CharacterId} at {size}: actual physics keeps distant drops available");
                    await Shot($"{job.CharacterId}_{size.X}_runtime_scatter");
                    root.Player.GlobalPosition = new Vector2(280, 100);
                    await PhysicsFrames(2);
                    root.Player.GlobalPosition = new Vector2(208, 150);
                    await PhysicsFrames(12);
                    await Shot($"{job.CharacterId}_{size.X}_runtime_attract");
                    await PhysicsFrames(90);
                    Check(Shards(drops).Count == 0 && game.Score == beforePickup + runtimeBonus,
                        $"{job.CharacterId} at {size}: actual physics collects every shard once");
                    drops.SetPhysicsProcess(false);
                }
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));

                for (int i = 0; i < 60; i++) fx.PurifyBurst(new Vector2(310, 100), 100);
                Check(Shards(drops).Count == 256, "large chains have a bounded particle count");
                typeof(Player).GetProperty("Lives")!.SetValue(root.Player, 0);
                long beforeDeath = game.Score;
                Tick(drops, 60);
                Check(Shards(drops).Count == 0 && game.Score == beforeDeath, "game over cannot collect lingering bonuses");
                fx.PurifyBurst(new Vector2(300, 100), 100);
                root.QueueFree();
                await Task.Delay(150);
                await Frames(10);
                Check(!IsInstanceValid(drops), "scene changes discard the previous stage's shards");
            }
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[ScoreShardQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[ScoreShardQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Movie(GameManager game)
    {
        game.SelectedJob = Job.Magic;
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.Stage.SetProcess(false);
        root.World.ProcessMode = ProcessModeEnum.Inherit;
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        game.SetStageTarget(999);
        root.Player.GlobalPosition = new Vector2(156, 125);
        typeof(Player).GetField("_fireCooldown", Fields)!.SetValue(root.Player, 999f);
        var fx = root.World.GetNode<FxLayer>("FxLayer");
        Read<RandomNumberGenerator>(fx, "_rng").Seed = 713;
        Audio.Instance?.StopMusic(0);
        await Frames(45);

        for (int wave = 0; wave < 2; wave++)
        {
            var enemies = new PageShard[3];
            for (int i = 0; i < enemies.Length; i++)
            {
                var enemy = new PageShard { Position = new Vector2(278 + i * 12, 80 + i * 36) };
                root.World.AddChild(enemy);
                enemy.SetPhysicsProcess(false);
                enemies[i] = enemy;
            }
            await Frames(45);
            foreach (var enemy in enemies)
            {
                foreach (var panel in enemy.GetChildren().OfType<Panel>().ToArray()) panel.Shatter();
                enemy.SetPhysicsProcess(true);
                await Frames(16);
            }
            await Frames(40);
            long beforePickup = game.Score;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, Pressed = true });
            await Frames(100);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, Pressed = false });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = true });
            await Frames(35);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = false });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.S, Pressed = true });
            await Frames(70);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.S, Pressed = false });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = true });
            await Frames(35);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = false });
            await Frames(25);
            Check(game.Score > beforePickup, "movie demonstrates proximity pickups");
            if (wave == 0)
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.A, Pressed = true });
                await Frames(100);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.A, Pressed = false });
            }
        }
        await Frames(30);
        GD.Print("[ScoreShardQA] MOVIE COMPLETE");
    }

    private async Task PhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string dir = ProjectSettings.GlobalizePath("res://build/qa_story/score_shards");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"{dir}/{name}.png");
    }
}
