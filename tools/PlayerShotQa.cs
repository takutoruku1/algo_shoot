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
    private static void Call(object obj, string name) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, null);
    private Bullet[] Active() => _pool.GetChildren().OfType<Bullet>().Where(b => b.Active).ToArray();
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
            Check(target.Hp == hp - 12,
                $"live physics applies charged damage exactly once (before={hp}, after={target.Hp}, player={root.Player.Position}, target={target.Position})");
        }
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = false });
        root.QueueFree();
        await Frames(5);
        _pool.DespawnAll();
    }

    private async Task CheckCharge(JobTuning job)
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
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = true });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.3);
        Check(player.ChargeRatio > 0.4f && !player.ChargeFull,
            $"{job.CharacterId}: half charge is not ready (ratio={player.ChargeRatio}, unlocked={_game.HasChargeShot}, paused={Hud.BubblePaused}, key={Input.IsKeyPressed(Key.C)})");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = false });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.01);
        Check(Active().All(b => !b.Charged), "early release does not fire");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = true });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.61);
        Check(player.ChargeFull && Active().Any(b => !b.Charged), "normal fire continues while charging");
        player._PhysicsProcess(0.1);
        Check(FxLayer.Instance.GetChildren().OfType<ChargeShotFx>().Count(fx => fx.Kind == ChargeShotFx.Beat.Ready) == 1,
            "ready burst happens once per hold");
        _pool.DespawnAll();
        await Shot($"{job.CharacterId}_charge_ready");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, Pressed = false });
        Input.FlushBufferedEvents();
        player._PhysicsProcess(0.01);
        var charge = Active().Single(b => b.Charged);
        Check(charge.Damage == 12 && charge.Radius == 10 && charge.Pierce == 3 && charge.ChargeJob == job.Id,
            "release creates a 12-power character charge with three penetrations");
        Check(player.ChargeRatio == 0 && !charge.Accel && !charge.Homing, "release resets the meter and fires immediately");
        charge.SetPhysicsProcess(false);
        charge._PhysicsProcess(0.1);
        await Shot($"{job.CharacterId}_charge_release");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Shot($"{job.CharacterId}_charge_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        _pool.DespawnAll();

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
            Call(player, "FireCharge");
            var shot = Active().Last(b => b.Charged);
            shot.SetPhysicsProcess(false);
            return shot;
        }
        charge = Fire();
        Write(enemy, "_bodyHitCd", 0.04d);
        int hp = Read<int>(enemy, "_hp");
        bodyHit.Invoke(enemy, new object[] { charge });
        Check(Read<int>(enemy, "_hp") == hp - 12, "charge bypasses ordinary hit cooldown and eight-damage cap");
        bodyHit.Invoke(enemy, new object[] { charge });
        Check(Read<int>(enemy, "_hp") == hp - 12 && charge.Pierce == 2, "moving body cannot be hit twice by the same charge");
        _pool.DespawnAll();
        enemy.Position = player.Position + new Vector2(30, 0);
        Call(enemy, "EnterExposed");
        charge = Fire();
        hp = Read<int>(enemy, "_hp");
        bodyHit.Invoke(enemy, new object[] { charge });
        int nearDamage = hp - Read<int>(enemy, "_hp");
        Check(nearDamage >= 12 && nearDamage <= 32, "close-range bonus never lowers charged damage");
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
        Check(charge.Damage == 24, "charge inherits purchased power upgrades");
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

        var panel = new Panel();
        panel.Setup(enemy, 0, 18, 0, false, 0, 20);
        enemy.AddChild(panel);
        panel.SetPhysicsProcess(false);
        charge = Fire();
        var panelHit = typeof(Panel).GetMethod("OnAreaEntered", Private)!;
        panelHit.Invoke(panel, new object[] { charge });
        panelHit.Invoke(panel, new object[] { charge });
        Check(panel.Ink == 14 && charge.Active && charge.Pierce == 2, "charge strips six ink and hits each shield only once");
        for (int i = 0; i < 3; i++)
        {
            var next = new Panel();
            next.Setup(enemy, 0, 18, 0, false, 0, 20);
            enemy.AddChild(next);
            next.SetPhysicsProcess(false);
            panelHit.Invoke(next, new object[] { charge });
        }
        Check(!charge.Active, "fourth shield contact consumes the charge");
        var recycled = _pool.Spawn(player.Position, Vector2.Left * 40, true);
        Check(ReferenceEquals(recycled, charge) && !recycled.Charged && recycled.Pierce == 0,
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
        Call(player, "FireCharge");
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
        var charged = Active().Single();
        Check(charged.Charged && charged.Pierce == 3 && charged.Radius == 10 && charged.Damage == 12 && Mathf.IsEqualApprox(charged.Velocity.Length(), 760)
            && ReferenceEquals(Read<BulletArt.PlayerVisual>(charged, "_playerVisual"), BulletArt.PlayerShot(job.Id)),
            $"{job.CharacterId}: charge shot uses its own artwork with enhanced power and piercing");
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
