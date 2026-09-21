using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class PowerPickupQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private GameManager _game = null!;
    private BulletPool _pool = null!;
    private string _out = "";
    private static T Read<T>(object o, string name) => (T)o.GetType().GetField(name, Private)!.GetValue(o)!;
    private static void Write(object o, string name, object value) => o.GetType().GetField(name, Private)!.SetValue(o, value);
    private static void Call(object o, string name) => o.GetType().GetMethod(name, Private)!.Invoke(o, null);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[PowerQA] PASS {message}");
    }
    private Bullet[] Shots() => _pool.GetChildren().OfType<Bullet>().Where(b => b.Active).ToArray();
    private ScoreShards ShardLayer => FxLayer.Instance.ScoreDrops;
    private IList Shards() => Read<IList>(ShardLayer, "_shards");
    private object[] Drops() => Shards().Cast<object>().Where(s => Read<PowerKind?>(s, "Power").HasValue).ToArray();
    private void Tick(int frames)
    {
        for (int i = 0; i < frames; i++) ShardLayer._PhysicsProcess(1.0 / 60);
    }
    private void EmitKill(Player player, Vector2 position) =>
        FxLayer.Instance.PurifyBurst(position, 80, FxLayer.PurifyTier.Zako, 4, power: player.CountPowerupKill());
    private static void Vulnerable(Player p)
    {
        Write(p, "_invincible", false);
        Write(p, "_dodgeInv", 0f);
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save directory");
            GD.Print($"[PowerQA] saves={OS.GetUserDataDir()}");
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/power_pickups");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            _game = GetNode<GameManager>("/root/Game");
            _pool = GetNode<BulletPool>("/root/Pool");
            _game.AutoSaveEnabled = false;
            _game.ResetPersistent();
            foreach (var stage in GameManager.Stages) Read<HashSet<string>>(_game, "_cleared").Add(stage.Id);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            CheckArtwork();
            foreach (var job in Jobs.All) await CheckPlayer(job);
            await CheckDrops();
            await CheckStageRoots();
            await Preview();
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[PowerQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[PowerQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private void CheckArtwork()
    {
        var hashes = new HashSet<string>();
        foreach (var kind in Enum.GetValues<PowerKind>())
        {
            var texture = PowerPickupArt.TextureFor(kind);
            Check(texture != null, $"{kind}: artwork imported");
            if (texture == null) continue;
            using var image = texture.GetImage();
            Check(image.GetWidth() == 128 && image.HasMipmaps(), $"{kind}: bounded, mipmapped texture");
            Check(image.GetPixel(0, 0).A == 0 && image.GetPixel(127, 127).A == 0
                && image.GetPixel(64, 64).A > 0.8f, $"{kind}: real alpha and opaque core");
            Check(hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.GetData()))),
                $"{kind}: distinct generated illustration");
            Check(ReferenceEquals(texture, PowerPickupArt.TextureFor(kind)), "texture cached");
        }
    }

    private async Task<AkariRoot> NewRoot(Job job = Job.Tank)
    {
        _game.SelectedJob = job;
        _game.SelectedEntry = GameManager.StageEntry.Start;
        _game.TrainingSetAllUpgrades(false);
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        root.Stage.SetProcess(false);
        root.World.ProcessMode = ProcessModeEnum.Inherit;
        root.Player.SetPhysicsProcess(false);
        root.Player.Position = new Vector2(170, 108);
        root.Hud.HideBubble();
        Write(root.Hud, "_bannerTimer", 0d);
        _game.SetStageTarget(999);
        _pool.DespawnAll();
        await Frames(3);
        return root;
    }

    private async Task Close(Node root)
    {
        root.QueueFree();
        _pool.DespawnAll();
        await Frames(4);
        Engine.TimeScale = 1;
    }

    private async Task CheckPlayer(JobTuning job)
    {
        var root = await NewRoot(job.Id);
        var p = root.Player;
        int startingLives = p.Lives;
        long money = _game.Impression;
        int hits = _game.RunHitCount;
        Check(Enum.GetValues<PowerKind>().All(k => p.PowerLevel(k) == 0), $"{job.CharacterId}: new run has no temporary buffs");
        for (int level = 0; level <= 2; level++)
        {
            if (level > 0)
            {
                Check(p.ApplyPowerup(PowerKind.Line), "line can increase");
                Check(p.ApplyPowerup(PowerKind.Speed), "speed can increase");
            }
            foreach (bool shop in new[] { false, true })
            {
                _game.TrainingSetUpgrade("n_lines", shop);
                _game.TrainingSetUpgrade("n_move_15x", shop);
                _pool.DespawnAll();
                Call(p, "Fire");
                int baseCount = job.Id == Job.Magic ? 5 : 2;
                int shopCount = shop ? (job.Id == Job.Magic ? 2 : 1) : 0;
                Check(Shots().Length == baseCount + shopCount + level,
                    $"{job.CharacterId}: level {level}, shop {shop}, exactly {baseCount + shopCount + level} shots");
                Check(Shots().All(b => !b.IsEnemy && ReferenceEquals(Read<BulletArt.PlayerVisual>(b, "_playerVisual"), BulletArt.PlayerShot(job.Id))),
                    "bonus lines retain the selected character artwork");
                if (job.Id == Job.Melee)
                {
                    for (int i = 0; i < 12; i++) Call(p, "Fire");
                    Check(Shots().Length <= 6 + level * 3, "acceleration charge cloud remains bounded");
                }
                p.Position = new Vector2(170, 90);
                Input.ActionPress("ui_down");
                p._PhysicsProcess(0.1);
                Input.ActionRelease("ui_down");
                float speed = 75f * job.MoveMul * (shop ? 1.5f : 1f) * (1f + level * 0.25f);
                Check(Mathf.IsEqualApprox(p.Position.Y - 90f, speed * 0.1f), "actual movement includes temporary and permanent speed");
                Check(Mathf.IsEqualApprox(p.SlowestMoveSpeed, speed * 0.8f), "AOE reachability uses the same boosted movement");
                _pool.DespawnAll();
            }
        }
        _game.TrainingSetAllUpgrades(false);
        foreach (var kind in new[] { PowerKind.Life, PowerKind.Shield })
            for (int level = 0; level < 2; level++) Check(p.ApplyPowerup(kind), $"{kind}: stack {level + 1}");
        foreach (var kind in Enum.GetValues<PowerKind>())
            Check(!p.ApplyPowerup(kind) && p.PowerLevel(kind) == 2, $"{kind}: third stack rejected");
        Check(p.Lives == startingLives + 2 && p.MaxLives == startingLives + 2 && !p.AddLife(), "two bonus lives, capped healing");
        Write(p, "_invincible", true);
        p.TakeHit();
        Check(p.ShieldPower == 2, "existing invulnerability does not consume shields");
        Write(p, "_invincible", false);
        Write(p, "_dodgeInv", 1f);
        p.TakeHit();
        Check(p.ShieldPower == 2, "dodging does not consume shields");
        for (int left = 1; left >= 0; left--)
        {
            Vulnerable(p);
            p.TakeHit();
            Check(p.ShieldPower == left && p.Lives == startingLives + 2 && _game.RunHitCount == hits,
                $"shield absorbs hit, {left} charge left, no life or hit-count loss");
            Check(p.LinePower == 2 && p.SpeedPower == 2 && p.LifePower == 2, "shield preserves other buffs");
            Check(Read<bool>(p, "_invincible") && Mathf.IsEqualApprox(Read<float>(p, "_invincibleTimer"), job.HitInvulSec)
                && Read<bool>(p, "_hitInvincible"), "shield grants normal hit invulnerability without graze farming");
            p.TakeHit();
            Check(p.ShieldPower == left && p.Lives == startingLives + 2, "one contact cannot consume two shields");
        }
        Vulnerable(p);
        p.TakeHit();
        Check(p.LinePower == 0 && p.SpeedPower == 0 && p.LifePower == 1 && p.Lives == startingLives + 1,
            "unprotected damage removes attack/speed and one bonus life");
        Check(p.MaxLives == startingLives + 1 && _game.RunHitCount == hits + 1, "life capacity and hit count stay consistent");
        Vulnerable(p);
        p.TakeHit();
        Check(p.LifePower == 0 && p.Lives == startingLives && p.MaxLives == startingLives, "second bonus life is consumed normally");
        Vulnerable(p);
        p.TakeHit();
        Check(p.AddLife(10) && p.Lives == startingLives && !p.AddLife(), "existing healing cannot regenerate temporary lives");
        Check(_game.Impression == money && _game.ExtraLines == 0 && Mathf.IsEqualApprox(_game.MoveSpeedMul, 1),
            "temporary buffs never modify permanent upgrades or currency");
        p.ApplyPowerup(PowerKind.Shield);
        Vulnerable(p);
        var hostile = _pool.Spawn(p.Position, Vector2.Zero, true, 3f);
        await Frames(5);
        Check(p.ShieldPower == 0 && p.Lives == startingLives && !hostile.Active, "real bullet contact consumes shield, not life, and despawns the bullet");
        await Close(root);
    }

    private object Spawn(PowerKind kind, Vector2 position)
    {
        ShardLayer.Add(new FxLayer.P
        {
            Type = FxLayer.T.HeartP, X = position.X, Y = position.Y, Vx = 65, Vy = -35,
            Size = 3, Ttl = 0.8f, Grav = 70, Drag = 0.7f, Col = FxLayer.Heart,
        }, 5, 4, power: kind);
        return Shards()[Shards().Count - 1]!;
    }

    private async Task CheckDrops()
    {
        var root = await NewRoot();
        var p = root.Player;
        ShardLayer.SetPhysicsProcess(false);
        var fx = FxLayer.Instance;
        Read<RandomNumberGenerator>(fx, "_rng").Seed = 713;
        fx.PurifyBurst(new Vector2(300, 100), 80, FxLayer.PurifyTier.Zako, 12);
        var original = Shards().Cast<object>().Select(s => Read<FxLayer.P>(s, "Particle")).ToArray();
        Shards().Clear();
        Read<RandomNumberGenerator>(fx, "_rng").Seed = 713;
        fx.PurifyBurst(new Vector2(300, 100), 80, FxLayer.PurifyTier.Zako, 12, power: PowerKind.Line);
        var powered = Shards().Cast<object>().Select(s => Read<FxLayer.P>(s, "Particle")).ToArray();
        Check(original.Length == powered.Length && original.Zip(powered, (a, b) =>
            a.Type == b.Type && a.Col == b.Col && a.Size == b.Size && a.Rot == b.Rot && a.Spin == b.Spin
            && a.Vx == b.Vx && a.Vy == b.Vy && a.Grav == b.Grav && a.Drag == b.Drag).All(same => same),
            "power overlay preserves original shard count, shapes, size, colors and scatter");
        Check(Drops().Length == 1 && ShardLayer.PowerCount == 1
            && Shards().Cast<object>().Sum(s => Read<int>(s, "Points")) == 8
            && Shards().Cast<object>().Sum(s => Read<int>(s, "Imp")) == 12,
            "exactly one original shard carries power without changing total rewards");
        Check(powered.All(particle => particle.Type is FxLayer.T.HeartP or FxLayer.T.Petal), "no extra pickup particles");
        Shards().Clear();
        for (int count = 1; count <= 4 * Player.KillsPerPowerDrop; count++)
        {
            var enemy = new PageShard { Position = new Vector2(310, 80) };
            root.World.AddChild(enemy);
            enemy.SetPhysicsProcess(false);
            foreach (var panel in enemy.GetChildren().OfType<Panel>().ToArray()) panel.Shatter();
            Check(enemy.IsPurified, "actual normal enemy defeated");
            Check(Drops().Length == count / Player.KillsPerPowerDrop, $"kill {count}: drop threshold respected");
            enemy.Purify();
            Check(Drops().Length == count / Player.KillsPerPowerDrop, "repeat purify cannot duplicate drops");
            enemy.QueueFree();
        }
        Check(Drops().Select(d => Read<PowerKind?>(d, "Power")!.Value).SequenceEqual(Enum.GetValues<PowerKind>()), "first four drops cover all four kinds");
        for (int i = 0; i < 12; i++) EmitKill(p, new Vector2(310, 80));
        Check(Drops().Length == 4, "four-item clutter cap");
        Shards().Clear();
        await Frames(3);
        Write(p, "_powerKills", 0);
        for (int i = 0; i < 6; i++)
        {
            var enemy = new PageShard { Position = new Vector2(310, 80) };
            root.World.AddChild(enemy);
            enemy.Purify();
            enemy.QueueFree();
        }
        Check(Read<int>(p, "_powerKills") == 3 && Drops().Length == 0, "bomb reward cap also caps drop progress");
        _game.TutorialNoConsume = true;
        for (int i = 0; i < 12; i++) p.CountPowerupKill();
        Check(Drops().Length == 0 && Read<int>(p, "_powerKills") == 3, "tutorial does not farm drops");
        _game.TutorialNoConsume = false;
        _game.TrainingMode = true;
        for (int i = 0; i < 12; i++) p.CountPowerupKill();
        Check(Drops().Length == 0 && Read<int>(p, "_powerKills") == 3, "training does not farm drops");
        _game.TrainingMode = false;
        int beforeBoss = Read<int>(p, "_powerKills");
        foreach (var tier in new[] { FxLayer.PurifyTier.MidBoss, FxLayer.PurifyTier.Boss })
        {
            var enemy = new TierEnemy { Tier = tier, Position = new Vector2(310, 80) };
            root.World.AddChild(enemy);
            foreach (var panel in enemy.GetChildren().OfType<Panel>().ToArray()) panel.Shatter();
            Check(enemy.IsPurified && Read<int>(p, "_powerKills") == beforeBoss, "boss-tier defeat does not drop a useless ending pickup");
            enemy.QueueFree();
        }

        Shards().Clear();
        Write(ShardLayer, "_rush", 0f);
        var drop = Spawn(PowerKind.Line, new Vector2(300, 100));
        Tick(30);
        float age = Read<float>(drop, "Age");
        Vector2 pos = Read<Vector2>(drop, "Position");
        root.Hud.HoldBubble = true;
        root.World.ProcessMode = ProcessModeEnum.Disabled;
        root.Hud.ShowMessage("QA");
        Check(!ShardLayer.Visible, "dialogue hides items synchronously even with World processing disabled");
        ShardLayer._PhysicsProcess(30);
        Check(!ShardLayer.Visible && Read<float>(drop, "Age") == age && Read<Vector2>(drop, "Position") == pos,
            "dialogue freezes shard and overlay together without spending lifetime");
        Check(p.LinePower == 0, "normal collection waits during dialogue");
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        Check(ShardLayer.Visible, "dialogue restores item visibility synchronously");
        root.World.ProcessMode = ProcessModeEnum.Inherit;
        ShardLayer._PhysicsProcess(0.01);
        Check(ShardLayer.Visible && Drops().Length == 1, "item resumes after dialogue");
        ShardLayer.SetPhysicsProcess(true);
        GetTree().Paused = true;
        age = Read<float>(drop, "Age");
        await Task.Delay(100);
        Check(Read<float>(drop, "Age") == age, "pause menu freezes drops");
        GetTree().Paused = false;
        ShardLayer.SetPhysicsProcess(false);
        p.Position = Read<Vector2>(drop, "Position") - new Vector2(40f, 0);
        _game.FlushShardImpression();
        long score = _game.Score, money = _game.Impression;
        Tick(45);
        _game.FlushShardImpression();
        Check(p.LinePower == 1 && Drops().Length == 0 && _game.Score == score + 5 && _game.Impression > money,
            "same shard magnet collects power, original points and currency together");
        money = _game.Impression;
        Tick(60);
        _game.FlushShardImpression();
        Check(p.LinePower == 1 && _game.Score == score + 5 && _game.Impression == money, "power and rewards apply exactly once");
        Spawn(PowerKind.Line, p.Position);
        ShardLayer._PhysicsProcess(0.4);
        Check(p.LinePower == 2 && Drops().Length == 0, "second real pickup upgrades to level two");
        Spawn(PowerKind.Line, p.Position);
        ShardLayer._PhysicsProcess(0.4);
        Check(p.LinePower == 2 && Drops().Length == 0 && _game.Score == score + 15,
            "already-maxed power cannot exceed level two or prevent ordinary shard rewards");
        Spawn(PowerKind.Speed, new Vector2(200, 185));
        p.Position = new Vector2(170, 40);
        Tick(330);
        Check(Drops().Length == 0 && p.SpeedPower == 0, "uncollected overlay expires with its shard");
        Spawn(PowerKind.Shield, new Vector2(310, 180));
        ShardLayer.BeginRush(2);
        root.Hud.HoldBubble = true;
        root.Hud.ShowMessage("QA");
        Check(ShardLayer.Visible, "boss collection rush remains visible during dialogue");
        Tick(90);
        Check(Drops().Length == 0 && p.ShieldPower == 1, "boss rush grants embedded power during dialogue instead of losing it");
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        Spawn(PowerKind.Shield, new Vector2(310, 180));
        typeof(Player).GetProperty(nameof(Player.Lives))!.SetValue(p, 0);
        ShardLayer._PhysicsProcess(0.1);
        Check(Drops().Length == 0 && p.ShieldPower == 1, "game over removes uncollected items without granting them");
        await Close(root);
        root = await NewRoot();
        Check(Drops().Length == 0 && Enum.GetValues<PowerKind>().All(k => root.Player.PowerLevel(k) == 0)
            && Read<int>(root.Player, "_powerKills") == 0, "retry/new scene resets buffs, drops and kill progress");
        foreach (var kind in Enum.GetValues<PowerKind>())
            for (int i = 0; i < 2; i++) root.Player.ApplyPowerup(kind);
        for (int i = 0; i < 4 * Player.KillsPerPowerDrop; i++) EmitKill(root.Player, new Vector2(310, 100));
        Check(Drops().Length == 0, "all maxed powers suppress unnecessary drops");
        Vulnerable(root.Player);
        root.Player.TakeHit();
        for (int i = 0; i < Player.KillsPerPowerDrop; i++) EmitKill(root.Player, new Vector2(900, -100));
        Tick(1);
        Check(Drops().Length == 1 && Read<PowerKind?>(Drops()[0], "Power") == PowerKind.Shield
            && Field.Rect.HasPoint(Read<Vector2>(Drops()[0], "Position")),
            "drops skip maxed types and stay inside the playfield");
        await Close(root);
        root = await NewRoot();
        ShardLayer.SetPhysicsProcess(false);
        for (int count = 1; count <= 40; count++)
        {
            EmitKill(root.Player, root.Player.Position);
            Tick(60);
        }
        Check(Enum.GetValues<PowerKind>().All(k => root.Player.PowerLevel(k) == 2),
            "all four powers can reach level two within the 45-enemy routes");
        await Close(root);
    }

    private async Task CheckStageRoots()
    {
        foreach (string scene in new[] { "Koharu.tscn", "Rei.tscn", "MinaBattle.tscn" })
        {
            _game.SelectedJob = Job.Melee;
            var root = GD.Load<PackedScene>($"res://{scene}").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            foreach (var stage in root.GetChildren().Where(n => n.Name.ToString().StartsWith("Stage"))) stage.SetProcess(false);
            var p = GetTree().GetFirstNodeInGroup("player") as Player;
            var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
            p!.SetPhysicsProcess(false);
            hud!.SetCinematicMode(false);
            hud.HideBubble();
            _game.SetStageTarget(999);
            ShardLayer.SetPhysicsProcess(false);
            for (int i = 0; i < Player.KillsPerPowerDrop; i++) EmitKill(p, p.Position);
            Check(Drops().Length == 1, $"{scene}: embedded power drops in its own shard layer");
            ShardLayer._PhysicsProcess(0.4);
            Check(p.LinePower == 1 && Drops().Length == 0, $"{scene}: its own player receives the embedded power");
            await Close(root);
        }
    }

    private async Task Preview()
    {
        foreach (var job in Jobs.All)
        {
            var root = await NewRoot(job.Id);
            foreach (var kind in Enum.GetValues<PowerKind>())
                for (int i = 0; i < 2; i++) root.Player.ApplyPowerup(kind);
            root.Player.Position = new Vector2(174, 119);
            Write(root.Player, "_invincible", false);
            root.Player.Modulate = Colors.White;
            root.Player.GetNode<Sprite2D>("Sprite").Visible = true;
            var fx = FxLayer.Instance!;
            fx.ScoreDrops.SetPhysicsProcess(false);
            Read<RandomNumberGenerator>(fx, "_rng").Seed = 713;
            for (int i = 0; i < 4; i++)
                fx.PurifyBurst(new Vector2(239 + i % 2 * 65, 94 + i / 2 * 54), 80,
                    FxLayer.PurifyTier.Zako, power: (PowerKind)i);
            for (int i = 0; i < 36; i++)
            {
                fx.ScoreDrops._PhysicsProcess(1.0 / 60);
                fx._Process(1.0 / 60);
            }
            for (int i = 0; i < 5; i++)
            {
                var b = _pool.Spawn(new Vector2(255 + i * 21, 52), Vector2.Zero, true, 3);
                b.SetSprite(BulletArt.AkariEnvelope);
                b.SetPhysicsProcess(false);
            }
            Call(root.Player, "Fire");
            foreach (var b in Shots().Where(b => !b.IsEnemy)) b.SetPhysicsProcess(false);
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                await Shot($"{job.CharacterId}_{size.X}x{size.Y}");
            }
            foreach (var b in Shots()) b.SetPhysicsProcess(true);
            await Close(root);
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        var sheet = new PickupSheet();
        AddChild(sheet);
        await Shot("item_sheet");
        sheet.QueueFree();
        await Frames(2);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
        var colors = new HashSet<uint>();
        for (int y = 0; y < image.GetHeight(); y += 8)
            for (int x = 0; x < image.GetWidth(); x += 8) colors.Add(image.GetPixel(x, y).ToRgba32());
        Check(colors.Count > 100, "rendered viewport is nonblank");
    }

    public partial class PickupSheet : Node2D
    {
        public override void _Draw()
        {
            UiKit.BeginDesign(this);
            DrawRect(new Rect2(0, 0, 1280, 720), new Color("232829"));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(64, 56), "ステージ強化アイテム", 32, Colors.White);
            string[] names = { "攻撃ライン", "移動速度", "追加ライフ", "シールド" };
            string[] levels = { "+1 / +2", "+25% / +50%", "+1 / +2", "1回 / 2回" };
            for (int i = 0; i < 4; i++)
            {
                float x = 72 + i * 300;
                PowerPickupArt.Draw(this, new Rect2(x + 12, 180, 216, 216), (PowerKind)i);
                UiKit.Text(this, UiKit.ZenBold, new Vector2(x, 435), names[i], 27, Colors.White, HorizontalAlignment.Center, 240);
                UiKit.Text(this, UiKit.Zen, new Vector2(x, 483), levels[i], 24, PowerPickupArt.ColorFor((PowerKind)i), HorizontalAlignment.Center, 240);
                PowerPickupArt.Draw(this, new Rect2(x + 94, 558, 52, 52), (PowerKind)i);
            }
            UiKit.EndDesign(this);
        }
    }

    public partial class TierEnemy : PageShard
    {
        public FxLayer.PurifyTier Tier { get; init; }
        protected override FxLayer.PurifyTier PurifyGrade => Tier;
    }
}
