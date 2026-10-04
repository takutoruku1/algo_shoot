using Godot;
using System;
using System.Collections.Generic;
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
            if (OS.GetCmdlineUserArgs().Contains("--overhead"))
                await CheckOverhead(game);
            else
            {
                foreach (string name in new[] { "rei_comment", "rei_subscriber", "rei_microphone", "rei_film",
                    "mina_eraser", "mina_memory", "mina_unanswered", "mina_butterfly", "koharu_star_pin", "akari_sticky" })
                {
                    var art = BulletArt.Get(name)!;
                    using var image = art.GetImage();
                    Check(art is AtlasTexture && image.DetectAlpha() != Image.AlphaMode.None
                        && image.GetUsedRect().Size == image.GetSize(), $"{name}: tightly framed transparent illustration");
                    Check(ReferenceEquals(art, BulletArt.Get(name)), $"{name}: cached texture");
                    if (name == "akari_sticky")
                    {
                        int opaque = 0, total = 0;
                        for (int y = 0; y < image.GetHeight(); y += 3)
                            for (int x = 0; x < image.GetWidth(); x += 3)
                            {
                                if (image.GetPixel(x, y).A > 0.5f) opaque++;
                                total++;
                            }
                        Check((float)opaque / total is > 0.25f and < 0.72f,
                            $"Akari paper has a compact torn silhouette, not a solid square (coverage={(float)opaque / total:0.000}, size={image.GetSize()}, region={((AtlasTexture)art).Region})");
                    }
                    await CheckPixels(name, art);
                }
                foreach (var (theme, scene) in new[] { (StageTheme.Akari, "Akari"), (StageTheme.Koharu, "Koharu"),
                    (StageTheme.Rei, "Rei"), (StageTheme.Mina, "MinaBattle") })
                    await CheckStage(game, theme, scene);
                CheckReuse();
            }
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

    private async Task CheckOverhead(GameManager game)
    {
        ProcessMode = ProcessModeEnum.Always;
        string output = ProjectSettings.GlobalizePath("res://build/qa_story/overhead");
        DirAccess.MakeDirRecursiveAbsolute(output);
        var spawnPost = typeof(PostBullets).GetMethod("SpawnOne", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (scene, cameo) in new[] { ("Akari", false), ("Koharu", false), ("Rei", false),
            ("MinaBattle", false), ("Akari", true), ("Koharu", true) })
        {
            string label = scene + (cameo ? "_midboss" : "_boss");
            game.SelectedEntry = GameManager.StageEntry.Start;
            game.Difficulty = GameManager.Diff.Normal;
            var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.ProcessMode = ProcessModeEnum.Disabled;
            var world = root.GetNode<Node2D>("World");
            world.ProcessMode = ProcessModeEnum.Inherit;
            var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
            var player = world.GetNode<Player>("Player");
            player.Position = new Vector2(Field.Left + 45, 145);
            var hud = root.GetNode<Hud>("Hud");
            hud.HoldBubble = false;
            hud.HideBubble();
            hud.SetCinematicMode(false);
            Enemy source;
            if (cameo)
            {
                Write(stage, "_stepStarted", false);
                Write(stage, "_lunatic", true);
                Call(stage, "Step_BossCameo", 0d);
                Write(stage, "_lunatic", false);
                source = Read<CameoBoss>(stage, "_cameo");
            }
            else
            {
                source = scene switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(),
                    "Rei" => new BossRei(), _ => new BossMina() };
                world.AddChild(source);
                root.GetNode<StageBackground>("StageBackground").EnterBoss();
            }
            root.GetNode<StageBackground>("StageBackground").GetNode<BgLayers>("BgLayers")._Process(2);
            source.Position = new Vector2(Field.Right - 65, 105);
            var entrance = typeof(Enemy).GetMethod("TickEntrance", Private)!;
            entrance.Invoke(source, new object[] { 0d });
            entrance.Invoke(source, new object[] { 2d });
            hud.HideSpellCard();
            Write(hud, "_bannerTimer", 0d);
            Write(hud, "_cutinTimer", 0d);
            Write(hud, "_bossLineTimer", 0d);
            Hud.BubblePaused = false;
            hud._Process(0);
            Pool.DespawnAll();
            var theme = scene switch { "Akari" => PostPool.Theme.Akari, "Koharu" => PostPool.Theme.Koharu,
                "Rei" => PostPool.Theme.Rei, _ => PostPool.Theme.Final };
            var accent = scene switch { "Akari" => new Color("78a6d9"), "Koharu" => new Color("d99970"),
                "Rei" => new Color("9eb3eb"), _ => new Color("b38cd6") };
            using var rng = new RandomNumberGenerator { Seed = 953 };
            OverheadCast[] Casts() => world.GetChildren().OfType<OverheadCast>().Where(c => !c.IsQueuedForDeletion()).ToArray();
            void Spawn()
            {
                if (cameo)
                {
                    var art = scene == "Akari" ? BulletArt.AkariDocs : BulletArt.KoharuPenlight;
                    typeof(Enemy).GetMethod("SetSpellVisual", Private)!.Invoke(source,
                        new object?[] { BulletShape.Needle, accent, art, 24f });
                    Call(source, "RainDown", Pool, game.ScaleBullets(scene == "Akari" ? 7 : 8), 72f);
                }
                else spawnPost.Invoke(null, new object?[] { Pool, rng, theme, 46f, accent, false, true, source });
                foreach (var bullet in Bullets()) bullet.SetPhysicsProcess(false);
            }
            async Task Clear()
            {
                Pool.DespawnAll();
                foreach (var cast in Casts()) cast.QueueFree();
                await Frames(2);
            }
            async Task<Image> Capture(string name)
            {
                foreach (var cast in Casts()) cast.QueueRedraw();
                await Frames(3);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var pixels = GetViewport().GetTexture().GetImage();
                Check(pixels.SavePng($"{output}/{label}_{name}.png") == Error.Ok, $"{label}: screenshot {name}");
                return pixels;
            }
            foreach (var diff in Enum.GetValues<GameManager.Diff>())
            {
                await Clear();
                game.Difficulty = diff;
                Spawn();
                var bullets = Bullets();
                var cast = Casts().Single();
                var positions = bullets.Select(b => b.Position).ToArray();
                var velocities = bullets.Select(b => b.Velocity).ToArray();
                Check(bullets.Length == (cameo ? game.ScaleBullets(scene == "Akari" ? 7 : 8) : 1),
                    $"{label}/{diff}: original shot count");
                Check(bullets.All(b => b.OverheadPending && !b.Visible && b.CollisionLayer == 0 && b.CollisionMask == 0),
                    $"{label}/{diff}: windup is invisible and harmless, not a new moving hitbox");
                int lives = player.Lives, grazes = game.GrazeCount;
                Call(player, "OnAreaEntered", bullets[0]);
                Call(player, "OnGrazeAreaEntered", bullets[0]);
                Check(player.Lives == lives && game.GrazeCount == grazes, $"{label}/{diff}: stale overlaps cannot hit or reward during windup");
                foreach (var bullet in bullets) bullet._PhysicsProcess(0.3);
                cast._PhysicsProcess(0.28);
                Check(bullets.Select(b => b.Position).SequenceEqual(positions) && bullets.All(b => b.OverheadPending),
                    $"{label}/{diff}: charges before any bullet drops");
                Hud.BubblePaused = true;
                cast._PhysicsProcess(3);
                Check(Mathf.IsEqualApprox(Read<float>(cast, "_time"), 0.28f), $"{label}/{diff}: dialogue freezes cast");
                Hud.BubblePaused = false;
                cast.ProcessMode = ProcessModeEnum.Pausable;
                GetTree().Paused = true;
                await Frames(3);
                Check(Mathf.IsEqualApprox(Read<float>(cast, "_time"), 0.28f), $"{label}/{diff}: pause freezes cast");
                cast.ProcessMode = ProcessModeEnum.Inherit;
                GetTree().Paused = false;
                Write(game, "_focusModeT", 2f);
                cast._PhysicsProcess(0.1);
                Check(Mathf.IsEqualApprox(Read<float>(cast, "_time"), 0.28f + 0.1f * GameManager.FocusModeScale),
                    $"{label}/{diff}: cast follows bullet focus time");
                Write(game, "_focusModeT", 0f);
                cast._PhysicsProcess(0.4);
                Check(bullets.All(b => !b.OverheadPending && b.Visible && b.CollisionLayer == 8
                    && b.CollisionMask == 1u),
                    $"{label}/{diff}: exact collision masks return on release");
                Check(bullets.Select(b => b.Position).SequenceEqual(positions) && bullets.Select(b => b.Velocity).SequenceEqual(velocities)
                    && bullets.All(b => b.Radius == (cameo ? 3.2f : 3f)), $"{label}/{diff}: fall origin, velocity and radius preserved");
                if (!cameo)
                {
                    await Clear();
                    double timer = 0;
                    for (int i = 0; i < 30; i++)
                    {
                        int tick = 6;
                        PostBullets.Tick(stage, rng, 1, ref timer, ref tick, source, theme, 46, accent);
                    }
                    int cap = diff switch { GameManager.Diff.Easy => 4, GameManager.Diff.Hard => 10,
                        GameManager.Diff.Lunatic => 14, _ => 7 };
                    Check(Bullets().Length == cap && Bullets().Count(b => b.WordAching) <= 1,
                        $"{label}/{diff}: reserved posts obey density and aching-post caps");
                }
            }
            await Clear();
            game.Difficulty = GameManager.Diff.Normal;
            Spawn();
            var canceled = Casts().Single();
            Pool.DespawnAll();
            var reused = Pool.Spawn(new Vector2(180, 110), new Vector2(60, 0), false);
            canceled._PhysicsProcess(1);
            Check(reused.Active && reused.Visible && !reused.OverheadPending && reused.CollisionLayer == 2
                && reused.Position == new Vector2(180, 110), $"{label}: clearing and pool reuse cannot resurrect or hide a shot");
            await Clear();
            Spawn();
            var defeated = Casts().Single();
            typeof(Enemy).GetField("_purified", Private)!.SetValue(source, true);
            defeated._PhysicsProcess(1);
            Check(Bullets().Length == 0, $"{label}: defeat cancels pending fire");
            typeof(Enemy).GetField("_purified", Private)!.SetValue(source, false);
            await Clear();
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Spawn();
                if (Bullets().Any(b => b.Position.X > Field.Left + 28 && b.Position.X < Field.Right - 28)) break;
                await Clear();
            }
            var visual = Casts().Single();
            visual._PhysicsProcess(0.16);
            using (await Capture("send")) { }
            visual._PhysicsProcess(0.30);
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                using (await Capture($"gate_{size.X}x{size.Y}")) { }
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            using var on = await Capture("charge");
            visual.Hide();
            using var off = await Capture("baseline");
            int topPixels = 0, pathPixels = 0;
            for (int y = 0; y < 350; y++)
                for (int x = 400; x < 1280; x++)
                {
                    var a = on.GetPixel(x, y); var b = off.GetPixel(x, y);
                    float d = Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B);
                    if (d > 0.05f) { if (y < 100) topPixels++; else pathPixels++; }
                }
            Check(topPixels > 150 && pathPixels > 30, $"{label}: visible overhead source and connecting trail ({topPixels}/{pathPixels})");
            visual.Show();
            visual._PhysicsProcess(0.15);
            foreach (var bullet in Bullets()) bullet._PhysicsProcess(0.24);
            using (await Capture("release")) { }
            visual._PhysicsProcess(1);
            await Frames(2);
            Check(!IsInstanceValid(visual), $"{label}: launch afterglow cleans itself up");
            await Clear();
            Spawn();
            source.QueueFree();
            foreach (var cast in Casts()) cast._PhysicsProcess(1);
            Check(Bullets().Length == 0, $"{label}: source removal cancels pending shots");
            root.QueueFree();
            await Frames(4);
            Check(GetTree().GetNodesInGroup("overhead_casts").Count == 0, $"{label}: no emitter leaks between stages");
            Hud.BubblePaused = false;
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
        // 弾の「出どころ」はテーマに依らない共通仕様なので、盤面が一式そろう最初の面で全パターンを一度だけ見る。
        if (theme == StageTheme.Akari) await CheckShotOrigins(world);

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
                if (theme == StageTheme.Akari && phase == 0)
                {
                    var shots = Bullets();
                    float speed = (float)typeof(Enemy).GetField("EnemyBulletSpeed", Private)!.GetValue(boss)!;
                    // 扇は自機狙い＝本数は必ず奇数（ScaleBulletsOdd）。中心の1本が自機の正面を通る。
                    Check(shots.Length == game.ScaleBulletsOdd(Read<int>(boss, "_fanCount"))
                        && shots.All(b => b.Radius == 3f && Mathf.IsEqualApprox(b.Velocity.Length(), speed * game.BulletSpeedMul)
                            && ReferenceEquals(Read<Texture2D>(b, "_sprite"), BulletArt.AkariSticky)),
                        $"Akari/{diff}: folded paper fan preserves count, speed and hit radius");
                }
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
            if (theme == StageTheme.Akari)
                Check(Bullets().Any(b => ReferenceEquals(Read<Texture2D>(b, "_sprite"), BulletArt.AkariSticky))
                    && Bullets().Any(b => ReferenceEquals(Read<Texture2D>(b, "_sprite"), BulletArt.AkariDocs)),
                    $"Akari/{diff}: finale uses folded paper without replacing the document spiral");
        }
        Pool.DespawnAll();
        game.Difficulty = GameManager.Diff.Normal;
        if (theme != StageTheme.Mina)
        {
            var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
            Write(stage, "_stepStarted", false);
            // 中ボスの登場カットシーン（CameoIntroScene）は会話送りを待つ＝ここでは通り抜けられない。
            // ルナティック経路はカットシーンを流さずその場で中ボスを出すので、この一歩だけ借りて弾幕に入る
            //（弾の絵・当たり・弾速の検査が目的で、登場演出は qa_cameo_intro の担当）。
            Write(stage, "_lunatic", true);
            Call(stage, "Step_BossCameo", 0d);
            Write(stage, "_lunatic", false);
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
        spawn.Invoke(null, new object?[] { Pool, rng, postTheme, 46f, null, false, true, boss });
        var post = Bullets().Single();
        var overhead = GetTree().GetNodesInGroup("overhead_casts").OfType<OverheadCast>()
            .Single(c => post.IsHeldBy(c));
        overhead._PhysicsProcess(OverheadCast.Windup);
        var core = Read<BulletWordCore>(post, "_wordCore");
        Check(ReferenceEquals(core.Art, BulletArt.PostCore(postTheme)) && core.CoreR == post.Radius
            && core.Visible, $"{theme}/post: themed core preserves hit radius");
        Pool.DespawnAll();
        boss.QueueFree();
        await Frames(2);
        if (theme == StageTheme.Rei)
        {
            var storm = new QuoteStorm();
            world.AddChild(storm);
            storm.SetProcess(false);
            Call(storm, "TickStorm", 1d);
            var quote = Bullets().Single();
            Check(ReferenceEquals(Read<BulletWordCore>(quote, "_wordCore").Art, BulletArt.Get("rei_film"))
                && quote.IsPatternBullet && quote.Radius == 3f, "quote storm keeps its non-destructible film core");
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

    // ─── 弾の「出どころ」（2026-09-28 作者報告への検査）───
    //   報告：「敵のたまが、アンチャーから少し離れた位置から予兆なくわいて攻撃している」。
    //   直した形：敵弾は必ず敵の中心（Enemy.ShotCenter）に湧き、そこから本来の発射点まで自分で
    //   飛んでいってから（Bullet.MakeLeadIn）従来どおりのパターンの速度・軌道へ移る。
    //   ここでは AttackPattern を1つ残らず列挙し、種ごとに
    //     ① 生成直後の弾は敵の中心から OriginTolerance(4px) 以内＝体から離れた空中に湧かない
    //     ② 導入区間は瞬間移動でもフェードでもなく毎フレーム発射点へ近づく／その間も当たり判定が生きている
    //     ③ 導入を終えた弾は本来の発射点・本来の速度にちょうど到達している＝弾幕の幾何（避け方）は不変
    //   を見る。取りこぼすと意味がないので、列挙に漏れが無いことも検査する。
    private const float OriginTolerance = 4f;

    // ロスターに載っている全 EnemySpec（全テーマの撃つ種/撃たない種・人型12種・引用リプ・バズ壁・祈り運び）。
    private static IEnumerable<EnemySpec> AllSpecs()
    {
        foreach (var theme in Enum.GetValues<StageTheme>())
        {
            var (shooter, drifter) = EnemyTable.For(theme);
            yield return shooter;
            yield return drifter;
            foreach (var character in EnemyTable.CharactersFor(theme)) yield return character;
            yield return EnemyTable.Flanker(theme);
            yield return EnemyTable.BuzzWall(theme);
        }
        yield return EnemyTable.PrayerCarrier();
    }

    private async Task CheckShotOrigins(Node2D world)
    {
        var specs = AllSpecs().ToArray();
        var patterns = Enum.GetValues<AttackPattern>();
        Check(patterns.All(p => specs.Any(s => s.Pattern == p)),
            $"all {patterns.Length} attack patterns are covered by the roster");
        foreach (var pattern in patterns)
            await CheckPatternOrigin(world, specs.First(s => s.Pattern == pattern));
    }

    private async Task CheckPatternOrigin(Node2D world, EnemySpec spec)
    {
        var game = GetNode<GameManager>("/root/Game");
        string label = $"origin/{spec.Pattern}";
        var enemy = new MidEnemy();
        enemy.Configure(spec);
        world.AddChild(enemy);
        enemy.GlobalPosition = new Vector2(Field.Right - 90f, 118f);
        enemy.SetPhysicsProcess(false);
        Pool.DespawnAll();
        try
        {
            // 撃たない種（盾専念のバズ壁／パターン無し）：黙っていることそのものが仕様。
            if (spec.Pattern is AttackPattern.None or AttackPattern.BuzzWall)
            {
                Call(enemy, "TickFire", 900d);
                Check(Bullets().Length == 0, $"{label}: stays silent by design");
                return;
            }
            // 祈り運び：撃つのではなく本体にぶら下げて運ぶ荷物＝鎖の位置に生まれるのが仕様。
            //   毎フレーム TickPrayerCarry が位置を握るので、中心からの導入区間は付けない（例外）。
            if (spec.Pattern == AttackPattern.KoharuPrayerCarry)
            {
                Call(enemy, "SpawnCarriedPrayers");
                var carried = Bullets();
                Check(carried.Length == 3 && carried.All(b => b.IsPatternBullet && !b.LeadIn
                        && b.GlobalPosition.Y > enemy.ShotCenter.Y
                        && b.GlobalPosition.Y - enemy.ShotCenter.Y <= 40f),
                    $"{label}: carried prayers hang on the chain below the body (cargo, not a shot)");
                return;
            }
            if (spec.Pattern >= AttackPattern.AkariDeadline)
            {
                // 記憶の残響だけは「自分が通った道」が発射点＝足跡を仕込んでから撃たせる
                //（居座りを止めている QA では足跡が溜まらず、いちばん遠い発射点の検査にならない）。
                if (spec.Pattern == AttackPattern.MinaMemory) SeedTrail(enemy);
                Call(enemy, "BeginCharacterAttack");
                for (int salvo = 0; Read<int>(enemy, "_salvoRemaining") > 0 && salvo < 8; salvo++)
                {
                    Pool.DespawnAll();
                    Call(enemy, "FireCharacterSalvo");
                    await CheckLeadIn(enemy, $"{label}/salvo{salvo}");
                    CheckGeometry(enemy, game, spec.Pattern, $"{label}/salvo{salvo}");
                }
                return;
            }
            // 予告なしで直接撃つ種（道具6種・アンチくん・引用リプ）。どれも発射点＝本体の現在地。
            Write(enemy, "_burstDir", new Vector2(-1f, 0f)); // ロックオン連射の弾速が 0 にならないように
            Call(enemy, spec.Pattern switch
            {
                AttackPattern.ReiLockBurst => "FireBurstShot", AttackPattern.ReiPulseRing => "FirePulseRing",
                AttackPattern.AkariScatter => "FireScatter", AttackPattern.AkariDrop => "FireDrop",
                AttackPattern.KoharuSharp3 => "FireSharp3", AttackPattern.KoharuSimmer => "FireSimmer",
                AttackPattern.FlankAim => "FireFlank", AttackPattern.DefaultAim => "FireDefaultAim",
                _ => throw new Exception($"{label}: no firing entry point"),
            });
            await CheckLeadIn(enemy, label);
        }
        finally
        {
            enemy.QueueFree();
            Pool.DespawnAll();
            await Frames(2);
        }
    }

    // 「記憶の残響」の足跡を仕込む（QA では本体を動かさないので自然には溜まらない）。
    // 本体から 20〜50px 離れた点＝導入区間がいちばん長く働くケースを作る。
    private static void SeedTrail(MidEnemy enemy)
    {
        var trail = Read<Vector2[]>(enemy, "_trail");
        for (int i = 0; i < trail.Length; i++)
            trail[i] = enemy.GlobalPosition + new Vector2(-6f - i * 3f, 34f - i * 6f);
        Write(enemy, "_trailCount", trail.Length);
        Write(enemy, "_trailHead", 0);
    }

    // 1回ぶんの斉射について「中心に湧いた → 飛んで発射点へ着いた → 速度は撃った時のまま」を見る。
    private async Task CheckLeadIn(MidEnemy enemy, string label)
    {
        var bullets = Bullets();
        Check(bullets.Length > 0, $"{label}: the attack actually fires");
        Vector2 centre = enemy.ShotCenter;
        float worst = bullets.Max(b => b.GlobalPosition.DistanceTo(centre));
        Check(worst <= OriginTolerance,
            $"{label}: every shot is born on the enemy centre (worst {worst:0.00}px <= {OriginTolerance}px)");
        // 本来の発射点と、着く前の速度を控える（導入は Velocity を触らない＝幾何が変わらないの検算）。
        var leading = bullets.Select(b => b.LeadIn).ToArray();
        var targets = bullets.Select(b => Read<Vector2>(b, "_leadTo")).ToArray();
        var speeds = bullets.Select(b => b.Velocity).ToArray();
        // 以降はこちらで1フレームずつ送る（エンジン側に動かされない）。Activate の遅延セットだけ1フレーム流す。
        foreach (var b in bullets) b.SetPhysicsProcess(false);
        await Frames(1);
        for (int i = 0; i < bullets.Length; i++)
        {
            var b = bullets[i];
            if (!leading[i]) continue;
            // 導入中の弾も当たる（無敵の弾を作らない）。
            Check(b.Active && b.Visible && b.Monitorable && b.Monitoring
                && b.GetChildren().OfType<CollisionShape2D>().All(c => !c.Disabled),
                $"{label}: the shot can still be hit and can still hit while it leaves the body");
            // 瞬間移動でもフェードでもなく「飛んでいく」＝毎フレーム必ず発射点へ近づき、ずっと見えている。
            float travel = b.GlobalPosition.DistanceTo(targets[i]);
            float prev = travel;
            int steps = 0;
            bool flies = true;
            while (b.LeadIn && steps++ < 64)
            {
                b._PhysicsProcess(1.0 / 60.0);
                float now = b.GlobalPosition.DistanceTo(targets[i]);
                flies &= now < prev && b.Visible && b.Active;
                prev = now;
            }
            Check(flies && steps > 1 && steps <= Mathf.CeilToInt(Bullet.LeadMaxDur * 60f) + 1,
                $"{label}: flies {travel:0}px out of the body over {steps} frames"
                + $" (visible the whole way, never longer than {Bullet.LeadMaxDur:0.00}s)");
            Check(b.GlobalPosition.DistanceTo(targets[i]) < 0.01f && b.Velocity.IsEqualApprox(speeds[i]),
                $"{label}: lands on the pattern's own firing point at the pattern's own speed");
        }
    }

    // 弾幕の幾何（＝避け方）が変わっていないこと。設計値そのものを書いて、着いた先の並びと照合する。
    private void CheckGeometry(MidEnemy enemy, GameManager game, AttackPattern pattern, string label)
    {
        foreach (var b in Bullets()) while (b.LeadIn) b._PhysicsProcess(1.0 / 60.0);
        var bullets = Bullets();
        var at = Read<Vector2>(enemy, "_characterOrigin");
        var xs = bullets.Select(b => b.GlobalPosition.X).ToArray();
        var ys = bullets.Select(b => b.GlobalPosition.Y).OrderBy(y => y).ToArray();
        switch (pattern)
        {
            // 切り抜きの人：鋏の2枚を狙いの直交方向へ開き、それぞれが予告時の自機を向いて閉じる
            //   ＝交点が自機に重なる（2026-10-03。旧仕様の「本体の上下24px から world 左へ∓24°」は
            //   自機の位置と無関係で、棒立ちの自機の上下を素通りしていた）。
            case AttackPattern.ReiClipper:
            {
                var target = Read<Vector2>(enemy, "_burstTarget");
                var aim = Read<Vector2>(enemy, "_burstDir");
                var perp = new Vector2(-aim.Y, aim.X);
                float jaw = Mathf.Min(0.4452f * Mathf.Max(32f, (target - at).Length()), 72f);
                Vector2 Blade(float side) => new(
                    Mathf.Clamp(at.X + perp.X * jaw * side, Field.Left + 8f, Field.Right - 8f),
                    Mathf.Clamp(at.Y + perp.Y * jaw * side, Field.Top + 8f, Field.Bottom - 8f));
                var blades = bullets.Select(b => b.GlobalPosition).Distinct().OrderBy(p => p.Y).ToArray();
                var want = new[] { Blade(-1f), Blade(1f) }.OrderBy(p => p.Y).ToArray();
                Check(blades.Length == 2 && blades.Zip(want, (a, b) => a.DistanceTo(b) < 0.01f).All(ok => ok),
                    $"{label}: opens a {jaw * 2f:0}px pincer across the aim");
                Check(bullets.All(b => Mathf.Abs(b.Velocity.Normalized()
                            .AngleTo(target - b.GlobalPosition)) <= Mathf.DegToRad(4.01f))
                    && blades.All(p => bullets.Any(b => b.GlobalPosition.IsEqualApprox(p)
                            && Mathf.Abs(b.Velocity.Normalized().AngleTo(target - p)) < 0.001f)),
                    $"{label}: both blades close on the telegraphed player spot");
                break;
            }
            // 空席の人：縦一列の壁。格子は「自機の居る行」を含み、空席（隙間）はそこには置かない
            //   ＝棒立ちは必ず壁に当たり、避けは「見えている空席へ移る」になる（2026-10-03）。
            case AttackPattern.AkariVacant:
            {
                int slots = Math.Clamp(game.ScaleBullets(5), 3, Mathf.FloorToInt((Field.Height - 24f) / 16f) + 1);
                float spacing = Math.Max(16f, 96f / (slots - 1));
                float mine = Mathf.Clamp(Read<Vector2>(enemy, "_burstTarget").Y, Field.Top, Field.Bottom);
                var gaps = ys.Zip(ys.Skip(1), (a, b) => b - a).ToArray();
                Check(bullets.Length == slots - 1 && xs.All(x => Mathf.Abs(x - at.X) < 0.01f)
                    && ys.All(y => Mathf.Abs((y - mine) - Mathf.Round((y - mine) / spacing) * spacing) < 0.01f)
                    && ys.Any(y => Mathf.Abs(y - mine) < 0.01f)
                    && gaps.Count(g => g > spacing + 0.01f) <= 1
                    && gaps.Max() <= spacing * 2f + 0.01f,
                    $"{label}: seats {slots - 1} of {slots} rows on the player's own row ({spacing:0}px apart), "
                    + "leaving the one empty seat somewhere else");
                break;
            }
            // 数字の人：本体の 24px 上、横 80px に広げた列から降る。車線の束は「自機を通る1本」を含む
            //   ＝敵の真上に固定していた旧仕様の「列から外れて立てば永久に届かない」を潰す（2026-10-03）。
            case AttackPattern.ReiMetrics:
            {
                int count = Math.Max(2, game.ScaleBullets(4));
                float row = Mathf.Max(Field.Top + 16f, at.Y - 24f);
                float step = 80f / (count - 1);
                var lanes = xs.OrderBy(x => x).ToArray();
                Check(bullets.Length == count && ys.All(y => Mathf.Abs(y - row) < 0.01f)
                    && lanes.Zip(lanes.Skip(1), (a, b) => b - a).All(d => Mathf.Abs(d - step) < 0.01f),
                    $"{label}: keeps the {count} ranking lanes {step:0.0}px apart, 24px above the body");
                var spot = Read<Vector2>(enemy, "_burstTarget");
                if (spot.Y > row)
                    Check(bullets.Any(b => Mathf.Abs((spot - b.GlobalPosition).Cross(b.Velocity.Normalized())) < 0.02f),
                        $"{label}: one lane falls straight through the telegraphed player spot");
                break;
            }
            // 荷物の人：縦 22px 間隔で積んだ置き弾。
            case AttackPattern.KoharuParcel:
            {
                int parcels = Math.Max(1, game.ScaleBullets(2));
                Check(bullets.Length == parcels && xs.All(x => Mathf.Abs(x - at.X) < 0.01f)
                    && (parcels == 1 || Mathf.Abs(ys.Last() - ys.First() - 22f * (parcels - 1)) < 0.01f),
                    $"{label}: keeps the 22px parcel stack");
                break;
            }
            // 記憶の残響：自機ではなく「自分が通った道」へ置く＝仕込んだ足跡の上に必ず落ちる。
            case AttackPattern.MinaMemory:
            {
                var trail = Read<Vector2[]>(enemy, "_trail");
                Check(bullets.All(b => trail.Any(p => p.DistanceTo(b.GlobalPosition) < 0.01f)
                        && b.GlobalPosition.DistanceTo(enemy.ShotCenter) > Bullet.LeadMinDist),
                    $"{label}: still lands on a place the enemy actually walked, away from the body");
                break;
            }
            // 残り（扇・単発）は本体の発射点から出るだけ＝CheckLeadIn の到達検査で十分。
            default:
                Check(bullets.All(b => b.GlobalPosition.DistanceTo(at) < 0.01f),
                    $"{label}: fires from the telegraphed point on the body");
                break;
        }
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
        // -- --dump-masks : 縁の見た目を目で確かめる用。96px の描画結果と、絵ごとに生成した線／光のマスクを PNG に落とす。
        if (OS.GetCmdlineUserArgs().Contains("--dump-masks"))
        {
            string dump = ProjectSettings.GlobalizePath("res://build/qa_story/enemy_projectiles/masks");
            DirAccess.MakeDirRecursiveAbsolute(dump);
            image.SavePng($"{dump}/{name}_render.png");
            var sil = Bullet.SilhouetteOf(art);
            sil.Rim.GetImage().SavePng($"{dump}/{name}_rim.png");
            sil.Glow.GetImage().SavePng($"{dump}/{name}_glow.png");
        }
        Check(image.GetPixel(48, 48).A > 0.8f, $"{name}: illustrated hit center is visible");

        // 縁の規則（2026-09-26 蛍光縁）：絵の輪郭から外へ「明るいネオン線（不透明）→ 同じ色相の半透明の光 → 無」
        // の順に並ぶこと。線も光も絵の形に沿う（丸いグローではない）＝絵から pad+2px 離れた点は完全に透明。
        // 距離は「絵の α≥0.5 画素までの最短距離（world px）」を絵の画像から直接測り、右と下の 2 方向で検査する
        // （縦長・横長の絵では、丸い光なら短辺側の far 点を覆ってしまう）。
        const float r = 8f;
        float rimW = Bullet.RimFrac * r * 2f * Bullet.SpriteFit, pad = Bullet.GlowFrac * r * 2f * Bullet.SpriteFit;
        using var src = art.GetImage();
        float k = Mathf.Min(1f, 96f / Mathf.Max(src.GetWidth(), src.GetHeight()));
        if (k < 1f) src.Resize(Mathf.RoundToInt(src.GetWidth() * k), Mathf.RoundToInt(src.GetHeight() * k), Image.Interpolation.Bilinear);
        float scale = r * 2f * Bullet.SpriteFit / Mathf.Max(src.GetWidth(), src.GetHeight());
        var solid = new List<Vector2>();
        for (int y = 0; y < src.GetHeight(); y++)
            for (int x = 0; x < src.GetWidth(); x++)
                if (src.GetPixel(x, y).A >= 0.5f)
                    solid.Add(new Vector2(48f + (x + 0.5f - src.GetWidth() / 2f) * scale, 48f + (y + 0.5f - src.GetHeight() / 2f) * scale));
        float Dist(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var s in solid) best = Mathf.Min(best, p.DistanceSquaredTo(s));
            return Mathf.Sqrt(best);
        }
        // 中心から dir へ 0.25px 刻みに進み、この方向で最後に絵へ触れた点より外で、絵からの距離が minDist 以上に
        // なった最初の点を含む画素を返す（絵の中心が透明な絵＝二つの泡などでは、中心の白い芯を拾わないように）。
        Color Sample(Vector2 dir, float minDist)
        {
            var center = new Vector2(48f, 48f);
            float lastInside = 0f;
            for (float t = 0f; t < 40f; t += 0.25f)
                if (Dist(center + dir * t) < 0.5f) lastInside = t;
            for (float t = lastInside; ; t += 0.25f)
            {
                var q = center + dir * t;
                if (Dist(q) >= minDist) return image.GetPixel(Mathf.FloorToInt(q.X), Mathf.FloorToInt(q.Y));
            }
        }
        foreach (var (dir, label) in new[] { (Vector2.Right, "right"), (Vector2.Down, "down") })
        {
            Color line = Sample(dir, 1.0f);          // 線の帯（絵の縁から 1〜2px。線幅 2.2px）：不透明で明るく、色がある
            Color glow = Sample(dir, rimW + 0.9f);   // 線のすぐ外（光の帯の内側）：半透明で線と同じ色相
            Color far = Sample(dir, pad + 2f);       // 光の帯の外：完全に透明（丸いグローならここが覆われる）
            Check(line.A > 0.6f && line.Luminance > 0.3f && line.S > 0.25f,
                $"{name}: bright neon line just outside the illustration ({label}: {line})");
            Check(glow.A > 0.06f && glow.A < 0.85f && glow.S > 0.5f && Mathf.Abs(Mathf.Wrap(glow.H - line.H, -0.5f, 0.5f)) < 0.08f,
                $"{name}: translucent glow of the same hue beyond the line ({label}: {glow})");
            Check(far.A == 0f, $"{name}: light follows the outline and ends within {pad + 2f:0.0}px — no round glow ({label})");
        }
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
