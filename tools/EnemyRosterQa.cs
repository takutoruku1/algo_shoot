using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class EnemyRosterQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
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
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);

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

            foreach (var (theme, scene) in new[] { (StageTheme.Akari, "Akari"), (StageTheme.Koharu, "Koharu"), (StageTheme.Rei, "Rei") })
                await CheckStage(game, theme, scene);

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
        Call(stage, "StartMidwaveSpawner", 0.7f);
        var spawner = stage.GetNode<Spawner>("Spawner");
        spawner.SetProcess(false);
        Read<RandomNumberGenerator>(spawner, "_rng").Seed = 934;
        Check(spawner.Theme == theme, $"{scene} wires its actual spawner to the correct roster");
        var characters = EnemyTable.CharactersFor(theme);
        for (int i = 0; i < 3; i++)
        {
            spawner._Process(3);
            var enemy = world.GetChildren().OfType<MidEnemy>().Last();
            Check(Read<EnemySpec>(enemy, "_spec").PreTexPath == characters[i].PreTexPath,
                $"{theme} introduces character {i + 1} before random special enemies");
            var sprite = enemy.GetNode<Sprite2D>("Body");
            Check(sprite.FlipH == characters[i].FlipH && Mathf.IsEqualApprox(sprite.Scale.Y * sprite.Texture.GetHeight(), 30f),
                "new sprite faces left at a stable character size");
            Check(Read<float>(enemy, "BodyRadius", typeof(Enemy)) == 8f && Read<int>(enemy, "PanelCount", typeof(Enemy)) == 3,
                "contact radius and shield strength remain unchanged");
        }
        var firstWave = world.GetChildren().OfType<MidEnemy>().ToArray();
        await Frames(140);
        var before = firstWave.Select(e => e.GetNode<Sprite2D>("Body").Position).ToArray();
        await Frames(10);
        Check(firstWave.Where((e, i) => e.GetNode<Sprite2D>("Body").Position != before[i]).Any(), "characters gently move");
        Check(firstWave.All(e => Mathf.Abs(e.GetNode<Sprite2D>("Body").Rotation) <= 0.026f), "humanoids do not inherit prop spinning");
        Check(GetTree().GetNodesInGroup("enemy_bullets").Count > 0, "new characters use the existing attacks");
        await Shot($"{scene.ToLowerInvariant()}_new_enemies");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Frames(10);
        await Shot($"{scene.ToLowerInvariant()}_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));

        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
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
            Call(spawner, "SpawnOne");
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
        root.QueueFree();
        await Frames(5);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Hud.BubblePaused = false;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/enemy_roster/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
