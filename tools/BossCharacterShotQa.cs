using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossCharacterShotQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private static T Read<T>(object value, string name) => (T)value.GetType().GetField(name, Private)!.GetValue(value)!;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print("[BossCharacterShotQA] PASS " + message);
    }
    private Bullet[] Shots(bool enemy) => Pool.GetChildren().OfType<Bullet>().Where(b => b.Active && b.IsEnemy == enemy).ToArray();

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Normal;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            root.Stage.SetProcess(false);
            root.World.ProcessMode = ProcessModeEnum.Disabled;
            root.Hud.HoldBubble = false;
            root.Hud.HideBubble();
            Hud.BubblePaused = false;
            root.Player.GlobalPosition = new Vector2(Field.Left + 50, 150);
            var fire = typeof(Enemy).GetMethod("FireBullet", Private)!;
            foreach (var (boss, job) in new (Enemy, Job)[]
            {
                (new BossAkari(), Job.Melee), (new BossKoharu(), Job.Heal),
                (new BossRei(), Job.Magic), (new BossMina(), Job.Tank),
            })
            {
                game.SelectedJob = job;
                root.World.AddChild(boss);
                boss.Position = new Vector2(Field.BossCenterX, 80);
                typeof(Enemy).GetMethod("TickEntrance", Private)!.Invoke(boss, new object[] { 0d });
                typeof(Enemy).GetMethod("TickEntrance", Private)!.Invoke(boss, new object[] { 100d });
                typeof(Enemy).GetMethod("TickSwapAnim", Private)!.Invoke(boss, new object[] { 1d });
                root.Hud.HideBubble();
                Check(boss.BodyShotJob == job, job + " body selects its character");
                foreach (bool charged in new[] { false, true })
                {
                    Pool.DespawnAll();
                    if (boss is BossMina)
                        typeof(BossMina).GetField("_pattern", Private)!.SetValue(boss, charged ? 1 : 0);
                    else
                        typeof(Enemy).GetField("_form2", Private)!.SetValue(boss, charged);
                    string method = job switch { Job.Melee => "FireAccel", Job.Heal => "FireHoming", Job.Magic => "FireSpread", _ => "FireRapid" };
                    if (charged) typeof(Player).GetMethod("FireCharge", Private)!.Invoke(root.Player, new object[] { ChargeTier.First });
                    else typeof(Player).GetMethod(method, Private)!.Invoke(root.Player, new object[] { new Vector2(190, 90), 1 });
                    var playerShot = Shots(false).First();
                    foreach (var shot in Shots(false)) shot.SetPhysicsProcess(false);
                    var body = (Bullet)fire.Invoke(boss, new object[] { Pool, boss.ShotCenter, Vector2.Left * 70, 2.6f, 1, true })!;
                    body.SetPhysicsProcess(false);
                    string label = job + (charged ? " charge" : " normal");
                    Check(body.Charged == charged && body.Radius == playerShot.Radius && body.Homing == playerShot.Homing
                        && body.Accel == playerShot.Accel && body.TurnRateOverride == playerShot.TurnRateOverride,
                        label + " radius, acceleration and homing match actual player fire");
                    Check(Mathf.IsEqualApprox(body.Velocity.Length(), playerShot.Velocity.Length()), label + " initial speed matches");
                    if (body.Accel)
                    {
                        Check(Read<float>(body, "_accelDelay") == Read<float>(playerShot, "_accelDelay")
                            && Read<float>(body, "_fastSpeed") == Read<float>(playerShot, "_fastSpeed"), label + " launch delay and speed match");
                        body._PhysicsProcess(Read<float>(body, "_accelDelay") - 0.01);
                        body._PhysicsProcess(0.011);
                        Check(!body.AccelCharging && Mathf.IsEqualApprox(body.Velocity.Length(), Read<float>(playerShot, "_fastSpeed")), label + " actually accelerates");
                    }
                    Check(ReferenceEquals(Read<BulletArt.PlayerVisual>(body, "_playerVisual"), Read<BulletArt.PlayerVisual>(playerShot, "_playerVisual"))
                        && Read<Texture2D?>(body, "_sprite") == null, label + " reuses exact player artwork");
                    Check(body.Material is ShaderMaterial && body.CollisionLayer == 8 && body.CollisionMask == 1,
                        label + " recolor stays hostile");
                    body.GlobalPosition = new Vector2(250, 100);
                    if (body.Homing)
                    {
                        body.Velocity = Vector2.Up * body.Velocity.Length();
                        float before = Mathf.Abs(body.Velocity.AngleTo(root.Player.GlobalPosition - body.GlobalPosition));
                        body._PhysicsProcess(0.05);
                        Check(Read<Node2D>(body, "_homeTarget") == root.Player
                            && Mathf.Abs(body.Velocity.AngleTo(root.Player.GlobalPosition - body.GlobalPosition)) < before,
                            label + " turns toward player, never toward boss");
                    }
                    if (OS.GetCmdlineUserArgs().Contains("--shots"))
                    {
                        foreach (var extra in Shots(false).Skip(1)) Pool.Despawn(extra);
                        playerShot.GlobalPosition = new Vector2(175, 95);
                        body.GlobalPosition = new Vector2(225, 95);
                        playerShot.Rotation = body.Rotation = 0;
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        string dir = ProjectSettings.GlobalizePath("res://build/qa_story/boss_character_shots");
                        DirAccess.MakeDirRecursiveAbsolute(dir);
                        using var image = GetViewport().GetTexture().GetImage();
                        image.SavePng($"{dir}/{job}_{(charged ? "charge" : "normal")}.png");
                    }
                    Pool.DespawnAll();
                    var offBody = (Bullet)fire.Invoke(boss, new object[] { Pool, new Vector2(200, 10), Vector2.Down * 70, 3.6f, 1, false })!;
                    offBody.SetPhysicsProcess(false);
                    Check(Read<BulletArt.PlayerVisual?>(offBody, "_playerVisual") == null && !offBody.Charged
                        && !offBody.Homing && !offBody.Accel && offBody.Radius == 3.6f
                        && Mathf.IsEqualApprox(offBody.Velocity.Length(), 70 * game.BulletSpeedMul), label + " falling shot retains original physics");
                    Pool.DespawnAll();
                    var edge = BossEdgeVolley.Begin(boss, Jobs.Get(job).CharacterId, 0);
                    edge.SetPhysicsProcess(false);
                    edge._PhysicsProcess(Read<double>(edge, "_warning") + 0.01);
                    Check(Shots(true).Length > 0 && Shots(true).All(b => !b.Charged && Read<BulletArt.PlayerVisual?>(b, "_playerVisual") == null
                        && Read<Texture2D?>(b, "_sprite") != null), label + " edge volley keeps original illustration");
                    edge.Cancel();
                    Pool.DespawnAll();
                    var danmaku = new BossDanmaku(boss);
                    danmaku.Tick(1);
                    Check(Shots(true).Length > 0 && Shots(true).All(b => b.Charged == charged
                        && ReferenceEquals(Read<BulletArt.PlayerVisual>(b, "_playerVisual"), BulletArt.PlayerShot(job))),
                        label + $" sustained body fire uses same shot (count={Shots(true).Length}, visible={boss.IsVisibleInTree()}, transforming={boss.Transforming}, center={boss.ShotCenter})");
                    Pool.DespawnAll();
                    string pattern = job switch { Job.Melee => "AimedSpread", Job.Heal => "FanDown", _ => "Aimed" };
                    boss.GetType().GetMethod(pattern, Private)!.Invoke(boss, new object[] { Pool });
                    Check(Shots(true).Length > 0 && Shots(true).All(b => b.Charged == charged && !b.SoftenOnGraze && !b.IsPatternBullet
                        && ReferenceEquals(Read<BulletArt.PlayerVisual>(b, "_playerVisual"), BulletArt.PlayerShot(job))),
                        label + " boss pattern preserves character shot properties");
                    Pool.DespawnAll();
                    if (boss is BossKoharu)
                    {
                        typeof(BossKoharu).GetMethod("ServeMeal", Private)!.Invoke(boss, null);
                        Check(Shots(true).Length > 0 && Shots(true).All(b => b.IsPatternBullet && !b.Homing && !b.Charged
                            && ReferenceEquals(Read<Texture2D>(b, "_sprite"), BulletArt.KoharuUchiwa)),
                            label + " placed meal keeps its original art and behavior");
                        Pool.DespawnAll();
                    }
                    if (charged && boss is not BossMina)
                    {
                        var technique = BossAnimalTechnique.Begin(boss, Jobs.Get(job).CharacterId, Read<AreaSpellCaster>(boss, "_caster"));
                        technique.SetPhysicsProcess(false);
                        technique._PhysicsProcess(Read<double>(technique, "_warning") + 0.01);
                        Check(Shots(true).Length > 0 && Shots(true).All(b => b.Charged && b.ChargeJob == job
                            && ReferenceEquals(Read<BulletArt.PlayerVisual>(b, "_playerVisual"), BulletArt.PlayerShot(job))),
                            label + " transformed technique uses existing charge art");
                        technique.Cancel();
                    }
                }
                Pool.DespawnAll();
                boss.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            foreach (var (theme, job) in new[] { (CameoFireTheme.AkariGrief, Job.Melee), (CameoFireTheme.KoharuFalling, Job.Heal),
                (CameoFireTheme.ReiAggressive, Job.Magic) })
            {
                var cameo = new CameoBoss { Theme = new CameoTheme { Fire = theme } };
                Check(cameo.BodyShotJob == job, theme + " cameo selects its character");
                cameo.Free();
            }
            GD.Print("[BossCharacterShotQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError("[BossCharacterShotQA] FAIL " + ex);
            GetTree().Quit(1);
        }
    }
}
