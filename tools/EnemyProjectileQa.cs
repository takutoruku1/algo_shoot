using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class EnemyProjectileQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private static T Read<T>(object o, string field) => (T)o.GetType().GetField(field, Private)!.GetValue(o)!;
    private static void Write(object o, string field, object value) => o.GetType().GetField(field, Private)!.SetValue(o, value);
    private static object? Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Private | BindingFlags.Public)!.Invoke(o, args);
    private Bullet[] Bullets() => Pool.GetChildren().OfType<Bullet>().Where(b => b.Active && b.IsEnemy).ToArray();
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[EnemyProjectileQA] PASS {message}");
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
            foreach (string name in new[] { "rei_comment", "rei_subscriber", "rei_microphone", "rei_film",
                "mina_eraser", "mina_memory", "mina_unanswered", "mina_butterfly", "koharu_star_pin" })
            {
                var art = BulletArt.Get(name)!;
                using var image = art.GetImage();
                Check(art is AtlasTexture && image.DetectAlpha() != Image.AlphaMode.None
                    && image.GetUsedRect().Size == image.GetSize(), $"{name}: tightly framed transparent illustration");
                Check(ReferenceEquals(art, BulletArt.Get(name)), $"{name}: cached texture");
                await CheckPixels(name, art);
            }
            foreach (var (theme, scene) in new[] { (StageTheme.Akari, "Akari"), (StageTheme.Koharu, "Koharu"),
                (StageTheme.Rei, "Rei"), (StageTheme.Mina, "MinaBattle") })
                await CheckStage(game, theme, scene);
            CheckReuse();
            Audio.Instance?.StopMusic(0);
            foreach (var node in GetNode<Audio>("/root/Audio").GetChildren())
                if (node is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[EnemyProjectileQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[EnemyProjectileQA] FAIL {ex}");
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
        ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Inherit;
        var player = world.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        player.GlobalPosition = new Vector2(Field.Left + 30f, 166f);
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        Hud.BubblePaused = false;
        await Frames(3);
        var (shooter, drifter) = EnemyTable.For(theme);
        var specs = EnemyTable.CharactersFor(theme).Concat(new[] { shooter, drifter, EnemyTable.Flanker(theme) });
        if (theme == StageTheme.Koharu) specs = specs.Append(EnemyTable.PrayerCarrier());
        foreach (var spec in specs) await CheckEnemy(world, spec);

        Enemy boss = theme switch
        {
            StageTheme.Akari => new BossAkari(), StageTheme.Koharu => new BossKoharu(),
            StageTheme.Rei => new BossRei(), _ => new BossMina(),
        };
        world.AddChild(boss);
        boss.GlobalPosition = new Vector2(Field.Right - 65f, 95f);
        var caster = Read<Node>(boss, "_caster");
        caster.SetProcess(false);
        Call(caster, "CancelPendingAttacks");
        root.GetNode<StageBackground>("StageBackground").EnterBoss();
        await Frames(160);
        boss.SetPhysicsProcess(false);
        boss.GlobalPosition = new Vector2(Field.Right - 65f, 95f);
        Hud.BubblePaused = false;
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            for (int phase = 0; phase < (theme == StageTheme.Mina ? 5 : 4); phase++)
            {
                Pool.DespawnAll();
                Write(boss, "_pattern", phase);
                Call(boss, "ApplySpell");
                Call(boss, "FirePattern", 3d);
                CheckIllustrated($"{theme}/{diff}/phase{phase}");
                if (theme == StageTheme.Rei)
                    Check(Bullets().All(b => ReferenceEquals(Read<Texture2D>(b, "_sprite"),
                        BulletArt.Get(new[] { "rei_comment", "rei_subscriber", "rei_microphone", "rei_film" }[phase]))),
                        $"Rei phase {phase} uses its own motif");
                if (diff == GameManager.Diff.Normal && phase == 0)
                {
                    foreach (var b in Bullets()) { b.GlobalPosition += b.Velocity * 0.7f; b.SetPhysicsProcess(false); }
                    await Shot($"{theme}_1280");
                    DisplayServer.WindowSetSize(new Vector2I(960, 540));
                    await Frames(5);
                    await Shot($"{theme}_960");
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                    await Frames(5);
                }
            }
            Pool.DespawnAll();
            Call(boss, "FireFinale", Pool, 3d);
            CheckIllustrated($"{theme}/{diff}/finale");
        }
        Pool.DespawnAll();
        boss.QueueFree();
        await Frames(2);
        game.Difficulty = GameManager.Diff.Normal;
        if (theme != StageTheme.Mina)
        {
            var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
            Write(stage, "_stepStarted", false);
            Call(stage, "Step_BossCameo", 0d);
            var cameo = Read<CameoBoss>(stage, "_cameo");
            cameo.SetPhysicsProcess(false);
            Hud.BubblePaused = false;
            Call(cameo, "FirePattern", 3d);
            CheckIllustrated($"{theme}/cameo");
            cameo.QueueFree();
            Pool.DespawnAll();
        }
        var postTheme = theme switch
        {
            StageTheme.Akari => PostPool.Theme.Akari, StageTheme.Koharu => PostPool.Theme.Koharu,
            StageTheme.Rei => PostPool.Theme.Rei, _ => PostPool.Theme.Final,
        };
        var spawn = typeof(PostBullets).GetMethod("SpawnOne", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var rng = new RandomNumberGenerator { Seed = 953 };
        spawn.Invoke(null, new object?[] { Pool, rng, postTheme, 46f, null, false, true });
        var post = Bullets().Single();
        var core = Read<BulletWordCore>(post, "_wordCore");
        Check(ReferenceEquals(core.Art, BulletArt.PostCore(postTheme)) && core.CoreR == post.Radius
            && core.Visible, $"{theme}/post: themed core preserves hit radius");
        Pool.DespawnAll();
        if (theme == StageTheme.Rei)
        {
            var storm = new QuoteStorm();
            world.AddChild(storm);
            storm.SetProcess(false);
            Call(storm, "TickStorm", 1d);
            var quote = Bullets().Single();
            Check(ReferenceEquals(Read<BulletWordCore>(quote, "_wordCore").Art, BulletArt.Get("rei_film"))
                && quote.Erasable && quote.Radius == 3f, "quote storm keeps its erasable film core");
            Pool.DespawnAll();
            storm.QueueFree();
            var tutorial = new StageZero { Player = player, Hud = hud, World = world };
            world.AddChild(tutorial);
            tutorial.SetProcess(false);
            Call(tutorial, "SpawnSlowBullets");
            CheckIllustrated("tutorial dodge practice");
            Check(Bullets().Length == 4 && Bullets().All(b => b.Radius == 3f), "tutorial retains four small slow shots");
            Pool.DespawnAll();
            tutorial.QueueFree();
        }
        if (theme == StageTheme.Mina)
        {
            var legacy = new BossHikage();
            world.AddChild(legacy);
            legacy.SetPhysicsProcess(false);
            Call(legacy, "FirePatterns", 3d);
            CheckIllustrated("legacy boss");
            legacy.QueueFree();
            Pool.DespawnAll();
        }
        root.QueueFree();
        await Frames(5);
        Hud.BubblePaused = false;
    }

    private async Task CheckEnemy(Node2D world, EnemySpec spec)
    {
        var enemy = new MidEnemy();
        enemy.Configure(spec);
        world.AddChild(enemy);
        enemy.GlobalPosition = new Vector2(Field.Right - 60f, 100f);
        enemy.SetPhysicsProcess(false);
        Pool.DespawnAll();
        if (spec.Pattern >= AttackPattern.AkariDeadline)
        {
            Call(enemy, "BeginCharacterAttack");
            Call(enemy, "FireCharacterSalvo");
        }
        else
        {
            string? method = spec.Pattern switch
            {
                AttackPattern.ReiLockBurst => "FireBurstShot", AttackPattern.ReiPulseRing => "FirePulseRing",
                AttackPattern.AkariScatter => "FireScatter", AttackPattern.AkariDrop => "FireDrop",
                AttackPattern.KoharuSharp3 => "FireSharp3", AttackPattern.KoharuSimmer => "FireSimmer",
                AttackPattern.FlankAim => "FireFlank", AttackPattern.KoharuPrayerCarry => "SpawnCarriedPrayers",
                AttackPattern.DefaultAim => "FireDefaultAim", _ => null,
            };
            if (method != null) Call(enemy, method);
        }
        if (spec.Pattern != AttackPattern.None)
        {
            CheckIllustrated($"enemy/{spec.Pattern}");
            var art = (Texture2D)typeof(Enemy).GetField("CurSprite", Private)!.GetValue(enemy)!;
            Check(Bullets().All(b => ReferenceEquals(Read<Texture2D>(b, "_sprite"), art)),
                $"{spec.Pattern}: spawned shots use character art");
        }
        enemy.QueueFree();
        Pool.DespawnAll();
        await Frames(2);
    }

    private void CheckIllustrated(string label)
    {
        var bullets = Bullets();
        Check(bullets.Length > 0 && bullets.All(b => Read<Texture2D?>(b, "_sprite") != null),
            $"{label}: all {bullets.Length} projectiles have illustrations");
        Check(bullets.All(b => b.Radius > 0 && b.Damage == 1 &&
            b.GetChildren().OfType<CollisionShape2D>().Any(c => c.Shape is CircleShape2D shape && shape.Radius == b.Radius)),
            $"{label}: hit shapes and damage remain intact");
    }

    private void CheckReuse()
    {
        var b = Pool.Spawn(new Vector2(180, 110), Vector2.Left * 75, true, 3.4f, 2);
        var velocity = b.Velocity;
        b.SetSprite(BulletArt.Get("mina_eraser"), 12);
        Check(b.Radius == 3.4f && b.Damage == 2 && b.Velocity == velocity,
            "SetSprite changes neither radius, damage nor velocity");
        b.SetWord("QA", coreArt: BulletArt.Get("rei_film"));
        var core = Read<BulletWordCore>(b, "_wordCore");
        Pool.Despawn(b);
        var reused = Pool.Spawn(Vector2.One * 100, Vector2.Right * 100, false, 2.6f, 1);
        Check(ReferenceEquals(reused, b) && Read<Texture2D?>(reused, "_sprite") == null
            && !core.Visible && reused.Word.Length == 0 && !reused.IsEnemy, "pool reuse clears enemy and post visuals");
        Pool.DespawnAll();
        Hud.BubblePaused = true;
        var paused = Pool.Spawn(Vector2.One * 100, Vector2.Left * 80, true);
        paused.SetSprite(BulletArt.Get("rei_comment"));
        Check(!paused.Active && !paused.Visible && Bullets().Length == 0, "illustrated shots remain hidden during dialogue");
        Hud.BubblePaused = false;
    }

    private async Task CheckPixels(string name, Texture2D art)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(96, 96), TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        var bullet = new Bullet();
        viewport.AddChild(bullet);
        bullet.Activate(new Vector2(48, 48), Vector2.Zero, true, 8f, 1);
        bullet.SetSprite(art, 0f);
        bullet.Rotation = 0;
        bullet.SetPhysicsProcess(false);
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = viewport.GetTexture().GetImage();
        Check(image.GetPixel(48, 48).A > 0.8f, $"{name}: illustrated hit center is visible");
        Check(image.GetPixel(62, 48).A == 0f && image.GetPixel(48, 62).A == 0f,
            $"{name}: no circular glow beyond the illustration");
        bullet.Deactivate();
        viewport.QueueFree();
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        string output = ProjectSettings.GlobalizePath("res://build/qa_story/enemy_projectiles");
        DirAccess.MakeDirRecursiveAbsolute(output);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.GetWidth() >= 960 && image.GetPixel(image.GetWidth() / 2, image.GetHeight() / 2).A > 0.9f,
            $"{name}: rendered viewport");
        Check(image.SavePng($"{output}/{name}.png") == Error.Ok, $"{name}: screenshot");
    }
}
