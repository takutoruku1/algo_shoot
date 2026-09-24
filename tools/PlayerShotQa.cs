using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class PlayerShotQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameManager _game = null!;
    private BulletPool _pool = null!;
    private string _out = "";
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static void Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args.Length == 0 ? null : args);
    private Bullet[] Active() => _pool.GetChildren().OfType<Bullet>().Where(b => b.Active).ToArray();
    private static (int ways, int damage, float radius, int pierce, float speed, float spread) ChargeStats(Job job) => job switch
    {
        Job.Tank => (1, 12, 9f, 5, 820f, 0f),
        Job.Melee => (1, 18, 12f, 1, 240f, 0f),
        Job.Heal => (3, 4, 6f, 1, 300f, 32f),
        Job.Magic => (5, 3, 6f, 1, 540f, 64f),
        _ => throw new ArgumentOutOfRangeException(nameof(job)),
    };

    private Bullet[] CheckChargeVolley(Player player)
    {
        var job = _game.SelectedJob;
        var expected = ChargeStats(job);
        var shots = Active().Where(b => b.Charged).OrderBy(b => player.ShotDir.AngleTo(b.Velocity)).ToArray();
        Check(shots.Length == expected.ways, $"{job}: charged volley has {expected.ways} projectiles");
        for (int i = 0; i < shots.Length; i++)
        {
            var b = shots[i];
            Check(b.Damage == expected.damage && b.Radius == expected.radius && b.Pierce == expected.pierce && b.ChargeJob == job,
                $"{job}: charge damage, radius, penetration and identity match the character");
            Check(Mathf.IsEqualApprox(b.Velocity.Length(), expected.speed)
                && b.Homing == (job == Job.Heal) && b.Accel == (job == Job.Melee)
                && b.TurnRateOverride == (job == Job.Heal ? 240 : 0), $"{job}: charge has its own speed and movement");
            float angle = shots.Length == 1 ? 0 : (float)i / (shots.Length - 1) - 0.5f;
            Check(Mathf.Abs(player.ShotDir.AngleTo(b.Velocity) - Mathf.DegToRad(angle * expected.spread)) < 0.001f,
                $"{job}: charge fan is symmetric about the aim direction");
            Check(ReferenceEquals(Read<BulletArt.PlayerVisual>(b, "_playerVisual"), BulletArt.PlayerShot(job)),
                $"{job}: charge retains its character artwork");
        }
        return shots;
    }
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[ShotQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/player_shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            _game = GetNode<GameManager>("/root/Game");
            _pool = GetNode<BulletPool>("/root/Pool");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            foreach (var stage in GameManager.Stages) Read<HashSet<string>>(_game, "_cleared").Add(stage.Id);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            CheckArtwork();
            if (OS.GetCmdlineUserArgs().Contains("--charge-demo"))
                foreach (var job in Jobs.All) await DemoCharge(job);
            else if (OS.GetCmdlineUserArgs().Contains("--charge-tier-shot"))
                foreach (var job in Jobs.All) await ShotChargeTiers(job);
            else if (OS.GetCmdlineUserArgs().Contains("--charge-only"))
                foreach (var job in Jobs.All) await CheckCharge(job);
            else
            {
                foreach (var job in Jobs.All) await CheckCharacter(job);
                await CheckStageEntries();
                await ShowComparison();
            }
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[ShotQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[ShotQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private void CheckArtwork()
    {
        var hashes = new HashSet<string>();
        foreach (var job in Jobs.All)
        {
            var art = BulletArt.PlayerShot(job.Id);
            using var image = art.Texture.GetImage();
            Check(image.DetectAlpha() != Image.AlphaMode.None && image.GetPixel(0, 0).A == 0
                && image.GetPixel(image.GetWidth() - 1, image.GetHeight() - 1).A == 0, $"{job.CharacterId}: true transparent sprite");
            Check(image.GetWidth() <= 256 && image.HasMipmaps(), $"{job.CharacterId}: bounded texture with mipmaps");
            Check(image.GetPixel((int)art.Pivot.X, (int)art.Pivot.Y).A > 0.9f, $"{job.CharacterId}: opaque core sits on the hit center");
            Check(hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.GetData()))), $"{job.CharacterId}: distinct artwork");
            Check(ReferenceEquals(art, BulletArt.PlayerShot(job.Id)), $"{job.CharacterId}: artwork is cached");
        }
    }

    private async Task DemoCharge(JobTuning job)
    {
        _game.SelectedJob = job.Id;
        _game.TrainingSetUpgrade("n_charge", true);
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.Stage.SetProcess(false);
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        Write(root.Hud, "_bannerTimer", 0d);
        Write(root.Player, "_invincible", false);
        Write(root.Player, "_fireCooldown", 100f);
        // Keep keyboard control selected while recording, regardless of the desktop mouse position.
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = true });
        Input.FlushBufferedEvents();
        var target = new TrainingDummy { Position = new Vector2(300, 108) };
        root.World.AddChild(target);
        await Frames(24);
        for (int take = 0; take < 2; take++)
        {
            int hp = target.Hp;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = true });
            await Frames(54);
            Check(root.Player.ChargeFull, "demo reaches full charge using live input");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = false });
            await Frames(10);
            await Shot($"{job.CharacterId}_charge_impact_{take}");
            await Frames(44);
            var expected = ChargeStats(job.Id);
            int hits = job.Id == Job.Heal ? expected.ways : 1;
            Check(target.Hp == hp - expected.damage * hits,
                $"live physics applies charged damage exactly once (before={hp}, after={target.Hp}, player={root.Player.Position}, target={target.Position})");
        }
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = false });
        root.QueueFree();
        await Frames(5);
        _pool.DespawnAll();
    }

    // Shoot the two-tier charge for review: the meter at tier 1 and at tier 2, and the projectile of each.
    //   Run with a window (screenshots hang headless):
    //     Godot --path . res://tools/qa_player_shots.tscn -- --charge-tier-shot
    private async Task ShotChargeTiers(JobTuning job)
    {
        _game.SelectedJob = job.Id;
        _game.TrainingSetUpgrade("n_charge", true);
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.Stage.SetProcess(false);
        var player = root.Player;
        player.SetPhysicsProcess(false);
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        Write(root.Hud, "_bannerTimer", 0d);
        Write(player, "_invincible", false);
        _pool.DespawnAll();
        await Frames(2);

        // Hold to tier 1, shoot the meter, then keep holding to tier 2 and shoot it again.
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = true });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.61);
        for (int i = 0; i < 8; i++) { player._PhysicsProcess(0.016); await Frames(1); }
        Check(player.ChargeStage == ChargeTier.First, $"{job.CharacterId}: meter shot is tier 1");
        await Shot($"{job.CharacterId}_tier1_meter");
        player._PhysicsProcess(0.60);
        for (int i = 0; i < 8; i++) { player._PhysicsProcess(0.016); await Frames(1); }
        Check(player.ChargeStage == ChargeTier.Second, $"{job.CharacterId}: meter shot is tier 2");
        await Shot($"{job.CharacterId}_tier2_meter");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = false });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.01);
        _pool.DespawnAll();

        // The projectiles themselves, side by side: fire one of each and let it travel a little.
        foreach (int stage in new[] { ChargeTier.First, ChargeTier.Second })
        {
            _pool.DespawnAll();
            Call(player, "FireCharge", stage);
            foreach (var b in Active()) b.SetPhysicsProcess(false);
            foreach (var b in Active().Where(b => b.Charged)) b._PhysicsProcess(0.06);
            await Frames(2);
            await Shot($"{job.CharacterId}_tier{stage}_projectile");
        }
        _pool.DespawnAll();
        root.QueueFree();
        await Frames(5);
    }

    private async Task CheckCharge(JobTuning job)
    {
        var expected = ChargeStats(job.Id);
        _game.SelectedJob = job.Id;
        _game.TrainingSetUpgrade("n_charge", true);
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.Stage.SetProcess(false);
        var player = root.Player;
        player.SetPhysicsProcess(false);
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        Write(root.Hud, "_bannerTimer", 0d);
        Write(player, "_invincible", false);
        _pool.DespawnAll();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = true });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.3);
        Check(player.ChargeRatio > 0.4f && !player.ChargeFull,
            $"{job.CharacterId}: half charge is not ready (ratio={player.ChargeRatio}, need={_game.ChargeNeedSec}, paused={Hud.BubblePaused}, key={Input.IsKeyPressed(Key.C)})");
        Check(Active().Length == 0, "normal fire stops on the first charging frame");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = false });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.01);
        Check(Active().All(b => !b.Charged), "early release does not fire");
        Check(Active().Any(), "early release resumes normal fire");
        _pool.DespawnAll();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = true });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.61);
        Check(player.ChargeFull && Active().Length == 0, "normal fire stays stopped until the full charge is released");
        player._PhysicsProcess(0.1);
        Check(Active().Length == 0, "holding a full charge does not resume normal fire");
        Check(FxLayer.Instance.GetChildren().OfType<ChargeShotFx>().Count(fx => fx.Kind == ChargeShotFx.Beat.Ready) == 1,
            "ready burst happens once per hold");
        _pool.DespawnAll();
        await Shot($"{job.CharacterId}_charge_ready");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = false });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.01);
        var charges = CheckChargeVolley(player);
        var charge = charges[charges.Length / 2];
        Check(player.ChargeRatio == 0, "release resets the meter");
        Check(Active().Any(b => !b.Charged), "full release resumes normal fire");
        foreach (var b in Active()) b.SetPhysicsProcess(false);
        foreach (var b in charges) b._PhysicsProcess(0.1);
        await Shot($"{job.CharacterId}_charge_release");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Shot($"{job.CharacterId}_charge_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        _pool.DespawnAll();

        CheckChargeInputs(player);
        CheckChargeMovement(player, root.World);

        var enemy = new Enemy();
        Write(enemy, "BarCount", 4);
        Write(enemy, "PanelCount", 0);
        root.World.AddChild(enemy);
        enemy.Position = player.Position + new Vector2(85, 0);
        enemy.SetPhysicsProcess(false);
        enemy.SetProcess(false);
        Call(enemy, "EnterExposed");
        var bodyHit = typeof(Enemy).GetMethod("OnBodyHitByPlayerBullet", Private)!;
        Bullet Fire()
        {
            Call(player, "FireCharge", ChargeTier.First);
            var shots = Active().Where(b => b.Charged).OrderBy(b => player.ShotDir.AngleTo(b.Velocity)).ToArray();
            var shot = shots[shots.Length / 2];
            foreach (var other in shots) if (other != shot) _pool.Despawn(other);
            shot.SetPhysicsProcess(false);
            return shot;
        }
        charge = Fire();
        Write(enemy, "_bodyHitCd", 0.04d);
        int hp = Read<int>(enemy, "_hp");
        bodyHit.Invoke(enemy, new object[] { charge });
        Check(Read<int>(enemy, "_hp") == hp - expected.damage, "character charge bypasses ordinary hit cooldown and damage cap");
        bodyHit.Invoke(enemy, new object[] { charge });
        Check(Read<int>(enemy, "_hp") == hp - expected.damage && charge.Pierce == expected.pierce - 1, "moving body cannot be hit twice by the same charge");
        _pool.DespawnAll();
        enemy.Position = player.Position + new Vector2(30, 0);
        Call(enemy, "EnterExposed");
        charge = Fire();
        hp = Read<int>(enemy, "_hp");
        bodyHit.Invoke(enemy, new object[] { charge });
        int nearDamage = hp - Read<int>(enemy, "_hp");
        Check(nearDamage >= expected.damage && nearDamage <= 32, "close-range bonus never lowers charged damage");
        _pool.DespawnAll();
        enemy.Position = player.Position + new Vector2(85, 0);
        Call(enemy, "EnterExposed");
        Write(enemy, "_windowDamage", _game.ExposedDamageCap - 3);
        charge = Fire();
        hp = Read<int>(enemy, "_hp");
        bodyHit.Invoke(enemy, new object[] { charge });
        Check(Read<int>(enemy, "_hp") == hp - 3, "charge respects the remaining boss window budget");
        _pool.DespawnAll();
        Call(enemy, "EnterExposed");
        _game.TrainingSetUpgrade("n_power_2x", true);
        charge = Fire();
        Check(charge.Damage == expected.damage * 2, "charge inherits purchased power upgrades");
        _game.TrainingSetUpgrade("n_power_2x", false);
        _pool.DespawnAll();
        charge = Fire();
        charge.Damage = 120;
        hp = Read<int>(enemy, "_hp");
        bodyHit.Invoke(enemy, new object[] { charge });
        Check(Read<int>(enemy, "_hp") == hp - 32, "upgraded charge is bounded at 32 boss damage");
        _pool.DespawnAll();
        Call(enemy, "EnterExposed");
        var normal = _pool.Spawn(player.Position, Vector2.Right * 360, false, 3, 12);
        hp = Read<int>(enemy, "_hp");
        bodyHit.Invoke(enemy, new object[] { normal });
        Check(Read<int>(enemy, "_hp") == hp - 8, "ordinary projectiles retain their existing cap");
        _pool.DespawnAll();

        Call(enemy, "EnterExposed");
        Call(player, "FireCharge", ChargeTier.First);
        hp = Read<int>(enemy, "_hp");
        foreach (var b in Active().Where(b => b.Charged).ToArray()) bodyHit.Invoke(enemy, new object[] { b });
        Check(Read<int>(enemy, "_hp") == hp - expected.damage * expected.ways,
            "every projectile in a charged volley can damage the same exposed boss once");
        _pool.DespawnAll();
        Call(enemy, "EnterExposed");
        Write(enemy, "_windowDamage", _game.ExposedDamageCap - 5);
        Call(player, "FireCharge", ChargeTier.First);
        hp = Read<int>(enemy, "_hp");
        foreach (var b in Active().Where(b => b.Charged).ToArray()) bodyHit.Invoke(enemy, new object[] { b });
        Check(Read<int>(enemy, "_hp") == hp - 5, "multi-projectile charge respects the shared boss window cap");
        _pool.DespawnAll();

        var panel = new Panel();
        panel.Setup(enemy, 0, 18, 0, false, 0, 20);
        enemy.AddChild(panel);
        panel.SetPhysicsProcess(false);
        charge = Fire();
        var panelHit = typeof(Panel).GetMethod("OnAreaEntered", Private)!;
        panelHit.Invoke(panel, new object[] { charge });
        panelHit.Invoke(panel, new object[] { charge });
        int ink = 1 + (expected.damage - 1) / 2;
        Check(panel.Ink == 20 - ink && charge.Active && charge.Pierce == expected.pierce - 1,
            "charge strips character-specific ink and hits each shield only once");
        for (int i = 0; i < expected.pierce; i++)
        {
            var next = new Panel();
            next.Setup(enemy, 0, 18, 0, false, 0, 20);
            enemy.AddChild(next);
            next.SetPhysicsProcess(false);
            panelHit.Invoke(next, new object[] { charge });
        }
        Check(!charge.Active, "the character penetration budget consumes the charge");
        var recycled = _pool.Spawn(player.Position, Vector2.Left * 40, true);
        Check(ReferenceEquals(recycled, charge) && !recycled.Charged && recycled.Pierce == 0
            && !recycled.Homing && !recycled.Accel && recycled.TurnRateOverride == 0,
            "pool reuse clears charged state for enemies");
        _pool.DespawnAll();
        charge = Fire();
        _pool.DespawnPlayerBullets(preserveCharged: true);
        Check(charge.Active, "defeating a small enemy preserves the piercing charge");
        _pool.DespawnAll();
        Check(!charge.Active, "boss and scene cleanup still remove charges");
        enemy.QueueFree();
        await Frames(5);

        Write(player, "_facing", -1);
        Call(player, "FireCharge", ChargeTier.First);
        CheckChargeVolley(player);
        _pool.DespawnAll();
        charge = Fire();
        Check(charge.Velocity.X < 0 && Mathf.Abs(charge.Rotation) > 2, "charge follows a left-facing shot direction");
        _pool.DespawnAll();
        Write(player, "_facing", 1);
        Write(player, "_chargeHeld", true);
        Write(player, "_chargeT", 0.6f);
        Write(player, "_dodgeTimer", 0.2f);
        player._PhysicsProcess(0.01);
        Check(player.ChargeRatio == 0 && Active().All(b => !b.Charged), "dodge cancels charge without firing");
        Write(player, "_dodgeTimer", 0f);
        Write(player, "_dodgeInv", 0f);
        Write(player, "_chargeT", 0.6f);
        Write(player, "_invincible", false);
        player.TakeHit();
        Check(player.ChargeRatio == 0, "damage cancels charging");
        _pool.DespawnAll();
        Write(player, "_chargeHeld", true);
        Write(player, "_chargeT", 0.6f);
        root.Hud.ShowDialog(Hud.LineKind.Mina, "……少し、お話ししましょう。", "res://char/mina_face.png");
        player._PhysicsProcess(0.01);
        Call(player, "FireCharge", ChargeTier.First);
        await Frames(4);
        Check(player.ChargeRatio == 0 && Active().Length == 0 && !FxLayer.Instance.GetChildren().OfType<ChargeShotFx>().Any(),
            "dialogue cancels charging, shots and charge effects");
        await Shot($"{job.CharacterId}_charge_dialogue");
        await Task.Delay(100);
        await Frames(4);
        root.QueueFree();
        await Frames(5);
        _pool.DespawnAll();
    }

    private void CheckChargeMovement(Player player, Node2D world)
    {
        // Movement is a tier-1 concern: the tier only scales power/radius, never speed or steering.
        Call(player, "FireCharge", ChargeTier.First);
        var shots = CheckChargeVolley(player);
        foreach (var b in shots) b.SetPhysicsProcess(false);
        var charge = shots[shots.Length / 2];
        charge.Position = new Vector2(120, 108);
        if (_game.SelectedJob == Job.Melee)
        {
            charge._PhysicsProcess(0.05);
            Check(charge.AccelCharging && Mathf.IsEqualApprox(charge.Velocity.Length(), 240),
                "akari: released flame moves immediately before its acceleration kick");
            charge._PhysicsProcess(0.08);
            Check(!charge.AccelCharging && Mathf.IsEqualApprox(charge.Velocity.Length(), 960),
                "akari: released flame accelerates after 0.12 seconds");
        }
        else if (_game.SelectedJob == Job.Heal)
        {
            Enemy Target(Vector2 position)
            {
                var e = new Enemy();
                Write(e, "PanelCount", 0);
                Write(e, "BarCount", 4);
                world.AddChild(e);
                e.Position = position;
                e.SetPhysicsProcess(false);
                e.SetProcess(false);
                return e;
            }
            var first = Target(new Vector2(210, 150));
            var second = Target(new Vector2(290, 80));
            charge._PhysicsProcess(0.05);
            Check(ReferenceEquals(Read<Node2D>(charge, "_homeTarget"), first) && charge.Velocity.Y > 0
                && Mathf.IsEqualApprox(charge.Velocity.Length(), 300), "koharu: charge steers toward an off-axis enemy at constant speed");
            charge.RegisterChargeHit(first);
            charge._PhysicsProcess(0.01);
            Check(ReferenceEquals(Read<Node2D>(charge, "_homeTarget"), second),
                "koharu: a pierced target is replaced by the next unhit enemy");
            charge.RegisterChargeHit(second);
            var direction = charge.Velocity;
            charge._PhysicsProcess(0.01);
            Check(Read<Node2D?>(charge, "_homeTarget") == null && charge.Velocity.IsEqualApprox(direction),
                "koharu: no unhit target means straight flight instead of orbiting a previous hit");
            charge.Position = new Vector2(200, 100);
            Write(charge, "_age", 2.49f);
            charge._PhysicsProcess(0.02);
            Check(!charge.Active, "koharu: seeking charge expires after 2.5 seconds");
            world.RemoveChild(first);
            world.RemoveChild(second);
            first.QueueFree();
            second.QueueFree();
        }
        else
        {
            var direction = charge.Velocity;
            var start = charge.Position;
            charge._PhysicsProcess(0.05);
            Check(charge.Velocity.IsEqualApprox(direction) && charge.Position.IsEqualApprox(start + direction * 0.05f),
                "mina and rei: released charge keeps its straight trajectory");
        }
        _pool.DespawnAll();
    }

    private void CheckChargeInputs(Player player)
    {
        void Press(string input, bool down)
        {
            InputEvent evt = input switch
            {
                "keyboard" => new InputEventKey { Keycode = Key.C, Pressed = down },
                _ => new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down },
            };
            Input.ParseInputEvent(evt);
            Input.FlushBufferedEvents();
            Pad.PollMouse(GetViewport());
        }
        try
        {
            foreach (string input in new[] { "keyboard", "mouse" })
            {
                // Charging works from the very first stage (2026-09-25); n_charge only unlocks the second tier.
                // Without it the meter needs 0.60s and never goes past tier 1, however long the button is held.
                _game.TrainingSetUpgrade("n_charge", false);
                Write(player, "_fireCooldown", 0f);
                Press(input, true);
                player._PhysicsProcess(0.45);
                Check(player.ChargeRatio > 0 && !player.ChargeFull,
                    $"{input}: charging is available before buying the second tier (ratio={player.ChargeRatio})");
                player._PhysicsProcess(0.20);
                Check(player.ChargeFull, $"{input}: the first tier fills at 0.60s");
                player._PhysicsProcess(1.0);
                Check(!player.ChargeFull2 && player.ChargeRatio2 == 0 && player.ChargeStage == ChargeTier.First,
                    $"{input}: holding longer stays on tier 1 without the upgrade (stage={player.ChargeStage})");
                Press(input, false);
                player._PhysicsProcess(0.01);
                Check(Active().Where(b => b.Charged).All(b => b.ChargeStage == ChargeTier.First),
                    $"{input}: the unupgraded release fires a tier-1 projectile");
                _pool.DespawnAll();
                _game.TrainingSetUpgrade("n_charge", true);

                // With the upgrade the meter keeps going: tier 2 arrives at twice the hold (0.60 -> 1.20s)
                // and the projectile carries the tier, its power multiplier and its wider radius.
                Write(player, "_fireCooldown", 0f);
                Press(input, true);
                player._PhysicsProcess(0.65);
                Check(player.ChargeFull && !player.ChargeFull2 && player.ChargeStage == ChargeTier.First,
                    $"{input}: tier 1 is reached first and tier 2 is still filling (ratio2={player.ChargeRatio2})");
                player._PhysicsProcess(0.60);
                Check(player.ChargeFull2 && player.ChargeStage == ChargeTier.Second,
                    $"{input}: tier 2 fills at {_game.ChargeTier2NeedSec:0.00}s (ratio2={player.ChargeRatio2})");
                Press(input, false);
                player._PhysicsProcess(0.01);
                {
                    var tier2 = Active().Where(b => b.Charged).ToArray();
                    var stats = ChargeStats(_game.SelectedJob);
                    Check(tier2.Length == stats.ways && tier2.All(b => b.ChargeStage == ChargeTier.Second),
                        $"{input}: the tier-2 release fires a full volley tagged as tier 2");
                    Check(tier2.All(b => b.Damage == Mathf.RoundToInt(stats.damage * ChargeTier.PowerMul)
                                      && Mathf.IsEqualApprox(b.Radius, stats.radius * ChargeTier.RadiusMul)),
                        $"{input}: tier 2 scales damage and radius (dmg={tier2[0].Damage}, r={tier2[0].Radius})");
                    Check(Mathf.IsEqualApprox(tier2[0].FuryMul, ChargeTier.FuryMul),
                        $"{input}: tier 2 carries the fury multiplier for the next stage (x{tier2[0].FuryMul})");
                }
                _pool.DespawnAll();

                Write(player, "_fireCooldown", 0f);
                player._PhysicsProcess(0.01);
                var existing = Active();
                int shots = Read<int>(player, "_shotParity");
                Press(input, true);
                if (input == "mouse")
                {
                    Write(player, "_fireCooldown", 0f);
                    player._PhysicsProcess(0.1);
                    Check(player.ChargeRatio == 0 && Read<int>(player, "_shotParity") > shots,
                        "short-click lock-on window keeps normal fire");
                    existing = Active();
                    shots = Read<int>(player, "_shotParity");
                }
                Write(player, "_fireCooldown", 0f);
                player._PhysicsProcess(0.3);
                Check(player.ChargeRatio > 0 && Read<int>(player, "_shotParity") == shots,
                    $"{input}: charging suppresses the next normal volley");
                Check(existing.All(b => b.Active) && Active().Length == existing.Length,
                    $"{input}: existing projectiles are preserved without new shots");
                var position = existing[0].Position;
                existing[0]._PhysicsProcess(0.05);
                Check(existing[0].Position != position, $"{input}: an already fired projectile keeps moving");
                player._PhysicsProcess(0.8);
                Check(player.ChargeFull && Read<int>(player, "_shotParity") == shots,
                    $"{input}: full charge stays held without normal fire");
                _pool.DespawnAll();
                Press(input, false);
                player._PhysicsProcess(0.01);
                Check(Active().Count(b => b.Charged) == ChargeStats(_game.SelectedJob).ways && Active().Any(b => !b.Charged) && player.ChargeRatio == 0,
                    $"{input}: release fires one character volley and resumes normal fire");
                _pool.DespawnAll();
            }
        }
        finally
        {
            foreach (string input in new[] { "keyboard", "mouse" }) Press(input, false);
            _game.TrainingSetUpgrade("n_charge", true);
        }
    }

    private async Task CheckCharacter(JobTuning job)
    {
        _game.SelectedJob = job.Id;
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        Check(Hud.BubblePaused && Active().Length == 0 && Read<float>(root.Player, "_recoil") == 0,
            $"{job.CharacterId}: introduction pauses before the first physics frame");
        await Frames(20);
        Check(Active().Length == 0 && Read<float>(root.Player, "_recoil") == 0, $"{job.CharacterId}: no opening autofire or frozen bullets");
        await Shot($"{job.CharacterId}_intro");
        root.Stage.SetProcess(false);
        root.Player.SetPhysicsProcess(false);
        root.Hud.HideBubble();
        Write(root.Hud, "_bannerTimer", 0d);
        Check(!Hud.BubblePaused, "dialogue ends synchronously");
        _pool.DespawnAll();
        Call(root.Player, "Fire");
        var shots = Active();
        int expectedCount = job.Id == Job.Magic ? 5 : 2;
        float speed = job.Id switch { Job.Tank => 360, Job.Melee => 12, Job.Heal => 200, _ => 320 };
        float radius = job.Id == Job.Melee ? 3.4f : 3;
        int damage = job.Id == Job.Melee ? 4 : 1;
        Check(shots.Length == expectedCount, $"{job.CharacterId}: original shot count retained");
        foreach (var shot in shots)
        {
            Check(!shot.IsEnemy && ReferenceEquals(Read<BulletArt.PlayerVisual>(shot, "_playerVisual"), BulletArt.PlayerShot(job.Id)), "shot retains its firing character");
            Check(Mathf.IsEqualApprox(shot.Velocity.Length(), speed) && Mathf.IsEqualApprox(shot.Radius, radius)
                && shot.Damage == damage && shot.Pierce == 0 && shot.Chain == 0, "speed, hitbox, damage and upgrades unchanged");
            Check(shot.Homing == (job.Id == Job.Heal) && shot.Accel == (job.Id == Job.Melee), "original movement flags retained");
            if (shot.Accel)
            {
                shot._PhysicsProcess(0.49);
                Check(shot.AccelCharging, "flame waits for the original charge delay");
                shot._PhysicsProcess(0.02);
                Check(!shot.AccelCharging && Mathf.IsEqualApprox(shot.Velocity.Length(), 760), "flame launches at the original speed");
            }
        }
        _pool.DespawnAll();
        Call(root.Player, "FireCharge");
        CheckChargeVolley(root.Player);
        _pool.DespawnAll();
        Write(root.Player, "_facing", -1);
        var particles = Read<List<FxLayer.P>>(FxLayer.Instance, "_p");
        particles.Clear();
        Call(root.Player, "Fire");
        Check(Active().All(b => b.Velocity.X < 0 && Mathf.Abs(b.Rotation) > 2), "left-facing art follows the shot direction");
        if (job.Id != Job.Heal)
            Check(particles.Where(p => p.Type == FxLayer.T.Spark).All(p => p.Vx < 0 && p.Col == BulletArt.PlayerColor(job.Id)),
                "muzzle sparks match direction and character color");
        _pool.DespawnAll();
        Write(root.Player, "_facing", 1);
        Write(root.Player, "_invincible", false);
        root.Player.GetNode<Sprite2D>("Sprite").Visible = true;
        root.Player.Modulate = Colors.White;
        root.Player.SetPhysicsProcess(true);
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Frames(job.Id == Job.Melee ? 58 : 18);
            Check(Active().Length > 0, "autofire resumes after dialogue");
            await Shot($"{job.CharacterId}_shoot_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(8);
        root.Player.SetPhysicsProcess(false);
        var enemy = _pool.Spawn(new Vector2(285, 90), Vector2.Left * 40, true, 5);
        enemy.SetWord("test", aching: true);
        root.Hud.ShowDialog(Hud.LineKind.Mina, "……少し、ここでお話ししましょう。", "res://char/mina_worried.png");
        root.Hud.RevealDialogNow();
        Check(Hud.BubblePaused && Active().Length == 0 && !enemy.IsVisibleInTree(), "dialogue immediately clears both sides, including word bullets");
        foreach (bool hostile in new[] { false, true })
        {
            var late = _pool.Spawn(new Vector2(280, 100), Vector2.Left * 30, hostile, 4);
            late.SetSprite(BulletArt.AkariEnvelope);
            Check(!late.Active && !late.IsVisibleInTree(), "late spawns during dialogue are not shown or collidable");
        }
        await Shot($"{job.CharacterId}_mid_dialogue");
        root.Hud.HideBubble();
        Check(!Hud.BubblePaused && Active().Length == 0, "ending dialogue never restores old bullets");
        var fresh = _pool.Spawn(new Vector2(260, 110), Vector2.Right * 360, false);
        Check(fresh.Active && fresh.Visible && fresh.CollisionLayer == 2, "pool resumes normal friendly bullets");
        _pool.Despawn(fresh);
        var recycled = _pool.Spawn(new Vector2(260, 110), Vector2.Left * 40, true);
        Check(ReferenceEquals(fresh, recycled) && Read<BulletArt.PlayerVisual?>(recycled, "_playerVisual") == null
            && !recycled.Accel && !recycled.Homing && recycled.Word.Length == 0 && recycled.Rotation == 0,
            "enemy reuse clears player art and motion state");
        Check(recycled.TextureFilter == CanvasItem.TextureFilterEnum.ParentNode, "enemy retains its original texture filtering");
        root.Hud.SetCinematicMode(true);
        Check(Hud.BubblePaused && Active().Length == 0, "flashback clears projectiles immediately");
        root.Hud.HideBubble();
        Check(Hud.BubblePaused, "cinematic pause survives hidden dialogue");
        root.Hud.SetCinematicMode(false);
        Check(!Hud.BubblePaused, "cinematic exit restores combat");
        root.QueueFree();
        await Frames(5);
        Check(!Hud.BubblePaused, "leaving the scene clears the global dialogue pause");
        _pool.DespawnAll();
    }

    private async Task CheckStageEntries()
    {
        _game.SelectedJob = Job.Tank;
        foreach (string scene in new[] { "Stage0.tscn", "Koharu.tscn", "Rei.tscn", "MinaBattle.tscn" })
        {
            _game.SelectedJob = scene == "Stage0.tscn" ? Job.Melee : Job.Tank;
            var root = GD.Load<PackedScene>($"res://{scene}").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            if (scene == "MinaBattle.tscn")
            {
                bool firedDuringBanner = false;
                for (int i = 0; i < 600 && !Hud.BubblePaused; i++)
                {
                    firedDuringBanner |= Active().Length > 0;
                    await Frames(1);
                }
                Check(!firedDuringBanner, "final title card cannot fire before its dialogue");
            }
            else Check(Hud.BubblePaused, $"{scene}: intro begins before physics");
            await Frames(20);
            Check(Hud.BubblePaused && Active().Length == 0, $"{scene}: opening dialogue has no visible shots");
            await Shot($"entry_{scene.Replace(".tscn", "")}");
            root.QueueFree();
            await Frames(5);
            Check(!Hud.BubblePaused, "scene exit resets pause");
            _pool.DespawnAll();
            _game.TutorialNoConsume = false;
        }
        _game.SelectedEntry = GameManager.StageEntry.Boss;
        var checkpoint = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(checkpoint);
        GetTree().CurrentScene = checkpoint;
        Check(Read<int>(checkpoint.Stage, "_step") == 11 && !Hud.BubblePaused, "boss checkpoint still skips the stage introduction");
        checkpoint.QueueFree();
        await Frames(5);
        _pool.DespawnAll();
    }

    private async Task ShowComparison()
    {
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        var sheet = new ShotSheet();
        AddChild(sheet);
        await Frames(8);
        await Shot("comparison");
        sheet.QueueFree();
        await Frames(3);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
        Color p = image.GetPixel(image.GetWidth() / 2, image.GetHeight() / 2);
        int varied = 0;
        for (int y = image.GetHeight() * 4 / 10; y < image.GetHeight() * 6 / 10; y += 4)
            for (int x = image.GetWidth() / 2; x < image.GetWidth() * 9 / 10; x += 5)
            {
                Color q = image.GetPixel(x, y);
                if (Mathf.Abs(p.R - q.R) + Mathf.Abs(p.G - q.G) + Mathf.Abs(p.B - q.B) > 0.04) varied++;
            }
        Check(varied > 25, "rendered canvas is nonblank");
    }

    public partial class ShotSheet : Node2D
    {
        public override void _Draw()
        {
            UiKit.BeginDesign(this);
            DrawRect(new Rect2(0, 0, 1280, 720), new Color("162329"));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(56, 26), "Refrain", 30, Colors.White);
            for (int i = 0; i < Jobs.All.Length; i++)
            {
                var job = Jobs.All[i];
                var art = BulletArt.PlayerShot(job.Id);
                float y = 107 + i * 150;
                DrawLine(new Vector2(56, y + 123), new Vector2(1224, y + 123), new Color("35484e"), 1);
                var portrait = GD.Load<Texture2D>(job.PlayerTexturePath);
                var size = portrait.GetSize() * (120f / portrait.GetHeight());
                DrawTextureRect(portrait, new Rect2(new Vector2(82, y) - new Vector2(size.X / 2, 0), size), false);
                UiKit.Text(this, UiKit.ZenBold, new Vector2(146, y + 34), job.CharacterName, 27, art.Accent);
                // 2026-09-17：ジョブ名（結び手…）の表記は全廃。副題は撃ち方の語に差し替える。
                UiKit.Text(this, UiKit.Zen, new Vector2(148, y + 74), ModeLabel(job.Mode), 17, Colors.White);
                Vector2 enlarged = art.Region.Size * Mathf.Min(260 / art.Region.Size.X, 110 / art.Region.Size.Y);
                DrawTextureRectRegion(art.Texture, new Rect2(new Vector2(510, y + 57) - enlarged / 2, enlarged), art.Region);
                float radius = job.Id == Job.Melee ? 3.4f : 3;
                Vector2 actual = art.Region.Size * (radius * 3.8f / UiKit.Scale / art.Region.Size.X);
                for (int j = 0; j < 6; j++)
                    DrawTextureRectRegion(art.Texture, new Rect2(new Vector2(760 + j * 75, y + 57) - actual / 2, actual), art.Region);
            }
            UiKit.EndDesign(this);
        }

        // 撃ち方の表記（GameManager.ShotModeName と同じ語）。QA走行では /root/Game に頼らない。
        private static string ModeLabel(GameManager.ShotMode m) => m switch
        {
            GameManager.ShotMode.Spread => "拡散",
            GameManager.ShotMode.Homing => "ホーミング",
            GameManager.ShotMode.Accel => "加速球",
            _ => "連射",
        };
    }
}
