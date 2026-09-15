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
            Check(_characterPatterns.Count == 9 && _attackSignatures.Count == 9, "nine distinct projectile attacks");
            Check(EnemyTable.CharactersFor(StageTheme.Mina).Select(s => s.Pattern)
                .SequenceEqual(new[] { AttackPattern.DefaultAim, AttackPattern.None, AttackPattern.DefaultAim }),
                "Mina enemies keep their previous attacks");

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

    private Bullet[] EnemyBullets() => GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Where(b => b.Active).ToArray();

    private async Task CheckCameoAttacks(GameManager game, Node stage, Player player, Hud hud, string scene)
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        Write(stage, "_stepStarted", false);
        Call(stage, "Step_BossCameo", 0d);
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
            CheckVolley(firstArt, expectedFirst, firstSpeed, rain: scene != "Rei");
            pool.DespawnAll();
            Write(cameo, "_fireT", 0d);
            Write(cameo, "_fireT2", secondInterval * game.DanmakuIntervalMul);
            Call(cameo, "FirePattern", 0d);
            CheckVolley(secondArt, game.ScaleBullets(secondCount), secondSpeed, rain: false);
            pool.DespawnAll();
            Write(cameo, "_fireT", threshold);
            Write(cameo, "_fireT2", secondInterval * game.DanmakuIntervalMul);
            Call(cameo, "FirePattern", 0d);
            Check(EnemyBullets().Length == expectedFirst + game.ScaleBullets(secondCount),
                $"{scene}/{diff}: simultaneous cameo attacks keep their bullet counts");
            CheckVolley(firstArt, expectedFirst, firstSpeed, rain: scene != "Rei");
            CheckVolley(secondArt, game.ScaleBullets(secondCount), secondSpeed, rain: false);
        }
        game.Difficulty = GameManager.Diff.Normal;
        pool.DespawnAll();
        Write(cameo, "_fireT", 0d);
        Write(cameo, "_fireT2", 0d);
        cameo.SetPhysicsProcess(true);
        await Frames(130);
        Check(EnemyBullets().Any(b => Read<Texture2D>(b, "_sprite").ResourcePath.EndsWith($"/{firstArt}.png"))
            && EnemyBullets().Any(b => Read<Texture2D>(b, "_sprite").ResourcePath.EndsWith($"/{secondArt}.png")),
            $"{scene}: both illustrated attacks fire during real cameo combat");
        await Shot($"{scene.ToLowerInvariant()}_cameo_attacks");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Frames(3);
        await Shot($"{scene.ToLowerInvariant()}_cameo_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
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

        void CheckVolley(string art, int expected, float speed, bool rain)
        {
            var bullets = EnemyBullets().Where(b => Read<Texture2D?>(b, "_sprite")?.ResourcePath
                == $"res://char/v3/bullets/{art}.png").ToArray();
            Check(bullets.Length == expected && bullets.All(b => Mathf.IsEqualApprox(b.Radius, 3.2f)
                && b.Damage == 1 && !b.Homing && !b.Erasable && !b.Accel && b.Shape == cameo.Theme.SpellShape),
                $"{scene}/{game.Difficulty}: {art} retains count, collision and damage");
            Check(bullets.All(b => Math.Abs((rain ? b.Velocity.Y : b.Velocity.Length()) - speed * game.BulletSpeedMul) < 0.01f),
                $"{scene}/{game.Difficulty}: {art} retains speed scaling");
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
                    hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
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
                Call(enemy, "TickFire", 2.0d);
                for (int i = 0; i < 3 && Read<int>(enemy, "_salvoRemaining") > 0; i++)
                    Call(enemy, "TickFire", 2.0d);
                var bullets = EnemyBullets();
                int Expected(int n) => game.ScaleBullets(n);
                int count = spec.Pattern switch
                {
                    AttackPattern.AkariDeadline or AttackPattern.KoharuComparison => Expected(3),
                    AttackPattern.AkariUnsent or AttackPattern.ReiClipper => Expected(2) * 2,
                    AttackPattern.AkariVacant => Math.Max(3, Expected(5)) - 1,
                    AttackPattern.KoharuCheer => Expected(3) * 2,
                    AttackPattern.KoharuParcel => Expected(2) * 3,
                    AttackPattern.ReiAnonymous => Expected(1) * 3,
                    AttackPattern.ReiMetrics => Math.Max(2, Expected(4)),
                    _ => throw new Exception("Unexpected character attack"),
                };
                Check(bullets.Length == count && Read<int>(enemy, "_salvoRemaining") == 0,
                    $"{spec.Pattern}/{diff}: completes expected {count}-bullet pattern");
                Check(bullets.All(b => Read<Texture2D>(b, "_sprite").ResourcePath == texture.ResourcePath
                    && b.Radius >= 3.6f && b.Radius <= 4f && b.Damage == 1 && !b.Homing && !b.Erasable),
                    "every bullet uses its own illustration with consistent collision rules");
                Check(bullets.All(b => b.GlobalPosition.X > Field.Left && b.GlobalPosition.X < Field.Right
                    && b.GlobalPosition.Y > Field.Top && b.GlobalPosition.Y < Field.Bottom), "all projectiles originate inside the field");
                if (spec.Pattern == AttackPattern.ReiAnonymous)
                    Check(bullets.All(b => Math.Abs(b.Velocity.Normalized().AngleTo(lockedDirection)) < 0.09f),
                        "anonymous burst uses the telegraphed direction, not live tracking");
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
                    foreach (var bullet in bullets) bullet._PhysicsProcess(game.DanmakuIntervalMul * 0.6d);
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
                    hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
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
