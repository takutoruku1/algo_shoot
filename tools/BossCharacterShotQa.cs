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
    private static readonly CircleShape2D PlayerCircle = new() { Radius = Player.HitRadius };
    internal static bool HitsPlayer(Bullet bullet, Vector2 player) => bullet.Footprint!.Shapes.Any(shape =>
        shape.Collide(bullet.GlobalTransform, PlayerCircle, new Transform2D(0, player)));

    internal static float Clearance(Bullet a, Bullet b, Vector2? positionA = null, Vector2? positionB = null)
    {
        float distance = float.MaxValue;
        var at = new Transform2D(a.GlobalRotation, positionA ?? a.GlobalPosition);
        var bt = new Transform2D(b.GlobalRotation, positionB ?? b.GlobalPosition);
        var ap = a.Footprint!.Hull.Select(p => at * p).ToArray();
        var bp = b.Footprint!.Hull.Select(p => bt * p).ToArray();
        if (Geometry2D.IntersectPolygons(ap, bp).Count > 0) return 0;
        foreach (var (points, edges) in new[] { (ap, bp), (bp, ap) })
        foreach (var point in points)
        for (int i = 0; i < edges.Length; i++)
            distance = Mathf.Min(distance, point.DistanceTo(Geometry2D.GetClosestPointToSegment(point, edges[i], edges[(i + 1) % edges.Length])));
        return distance;
    }
    private static float BossSpeed(Job job, bool charged) => (job switch
    {
        Job.Melee => 0.6f,
        Job.Heal => charged ? 72f : 60f,
        Job.Magic => charged ? 72f : 62f,
        _ => charged ? 100f : 82f,
    }) * GameManager.Instance!.BulletSpeedMul;
    private static bool MatchesCharacter(Bullet bullet, Job character, bool charged)
    {
        var job = Jobs.Get(character);
        float speed = BossSpeed(character, charged);
        return bullet.Charged == charged && Mathf.IsEqualApprox(bullet.Radius,
                (charged ? BulletPool.BossChargeRadius : BulletPool.BossRadius) * (character == Job.Magic ? 1.5f : 1))
            && bullet.Accel == (job.Mode == GameManager.ShotMode.Accel)
            && bullet.Homing == (job.Mode == GameManager.ShotMode.Homing)
            && Mathf.IsEqualApprox(bullet.Velocity.Length(), speed)
            && ReferenceEquals(Read<BulletArt.PlayerVisual>(bullet, "_playerVisual"), BulletArt.PlayerShot(character));
    }

    private void CheckEnemyHoming(Player player, bool charged)
    {
        Vector2 original = player.GlobalPosition;
        var center = new Vector2(Field.CenterX, Field.CenterY);
        var shot = Pool.SpawnBossShot(center, Vector2.Right, Job.Heal, charged);
        shot.SetPhysicsProcess(false);
        float speed = shot.Velocity.Length(), totalTurn = 0;
        bool stopped = true;
        for (int frame = 0; frame < 90; frame++)
        {
            shot.GlobalPosition = center;
            player.GlobalPosition = center + shot.Velocity.Normalized().Rotated(0.7f) * 70;
            float before = shot.Velocity.Angle();
            shot._PhysicsProcess(1d / 60);
            float turn = Mathf.Abs(Mathf.AngleDifference(before, shot.Velocity.Angle()));
            totalTurn += turn;
            if (frame >= 37) stopped &= turn < 0.0001f;
        }
        Check(stopped, "enemy seeker stops steering after 0.6 seconds");
        Check(totalTurn > Mathf.DegToRad(30) && totalTurn <= Mathf.DegToRad(36.01f)
            && Mathf.IsEqualApprox(shot.Velocity.Length(), speed), "enemy seeker limits total bend without slowing down");
        Check(shot.Active, "slower enemy seeker can cross the field after steering ends");
        for (int frame = 0; frame < 400; frame++)
        {
            shot.GlobalPosition = center;
            shot._PhysicsProcess(1d / 60);
        }
        Check(!shot.Active, "enemy seeker has a bounded eight second lifetime");

        var reused = Pool.SpawnBossShot(center, Vector2.Right, Job.Heal, charged);
        reused.SetPhysicsProcess(false);
        player.GlobalPosition = center + new Vector2(70, 40);
        reused._PhysicsProcess(1d / 60);
        Check(ReferenceEquals(reused, shot) && reused.Velocity.Y > 0, "reused seeker resets its steering limit");
        Pool.Despawn(reused);

        var passed = Pool.SpawnBossShot(new Vector2(Field.Left + 25, Field.CenterY), Vector2.Right, Job.Heal, charged);
        passed.SetPhysicsProcess(false);
        player.GlobalPosition = passed.GlobalPosition + Vector2.Left * 10;
        passed._PhysicsProcess(1d / 60);
        player.GlobalPosition = passed.GlobalPosition + new Vector2(60, 40);
        passed._PhysicsProcess(0.1);
        Check(Mathf.Abs(passed.Velocity.Y) < 0.0001f, "enemy seeker never reacquires after passing the player");
        for (int frame = 0; frame < 480 && passed.Active; frame++) passed._PhysicsProcess(1d / 60);
        Check(!passed.Active && passed.GlobalPosition.X > Field.Right, "enemy seeker exits the field instead of orbiting");

        var playerShot = Pool.Spawn(center, Vector2.Right * speed, false, homing: true);
        playerShot.TurnRateOverride = charged ? 240 : Jobs.Get(Job.Heal).HomingTurnRate;
        playerShot.SetPhysicsProcess(false);
        var target = new Node2D();
        AddChild(target);
        typeof(Bullet).GetField("_homeTarget", Private)!.SetValue(playerShot, target);
        typeof(Bullet).GetField("_retargetT", Private)!.SetValue(playerShot, 10f);
        float playerTurn = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            playerShot.GlobalPosition = center;
            target.GlobalPosition = center + playerShot.Velocity.Normalized().Rotated(0.7f) * 70;
            float before = playerShot.Velocity.Angle();
            playerShot._PhysicsProcess(1d / 60);
            playerTurn += Mathf.Abs(Mathf.AngleDifference(before, playerShot.Velocity.Angle()));
        }
        Check(Mathf.IsEqualApprox(Mathf.RadToDeg(playerTurn), playerShot.TurnRateOverride), "player seeker retains full steering beyond 0.6 seconds");
        target.Free();
        Pool.DespawnAll();
        player.GlobalPosition = original;
    }

    private async Task CheckReiSplits(Player player)
    {
        var game = GameManager.Instance!;
        Vector2 original = player.GlobalPosition;
        player.GlobalPosition = new Vector2(Field.Left + 10, Field.Bottom - 10);
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        foreach (bool charged in new[] { false, true })
        foreach (bool aimed in new[] { false, true })
        {
            game.Difficulty = diff;
            Pool.DespawnAll();
            var seed = Pool.SpawnBossShot(new Vector2(320, 108), Vector2.Left, Job.Magic, charged);
            Vector2 launchTarget = new(Field.Left + 10, 108);
            if (aimed) seed.BossAimTarget = launchTarget;
            seed.SetPhysicsProcess(false);
            float speed = seed.Velocity.Length();
            float seedRadius = seed.Radius;
            seed._PhysicsProcess(41 / speed);
            Check(Shots(true).Length == 1 && seed.Active, $"Rei/{diff}/{charged}: travels before splitting");
            Hud.BubblePaused = true;
            seed._PhysicsProcess(1);
            Hud.BubblePaused = false;
            Check(Read<float>(seed, "_reiSplitTravel") < 42, "dialogue pauses split travel");
            bool capture = OS.GetCmdlineUserArgs().Contains("--shots") && diff == GameManager.Diff.Hard && !charged && !aimed;
            if (capture) await SplitShot("0_warning");
            seed._PhysicsProcess(1.1 / speed);
            var first = Shots(true);
            Check(!seed.Active && first.Length == 3 && first.All(b => Read<int>(b, "_reiSplitsRemaining") == 1
                && b.Radius < seedRadius && Mathf.IsEqualApprox(b.Velocity.Length(), speed * 0.9f)), "first split produces three smaller, slower branches");
            Check(first.All(b => b.CollisionLayer == 0 && b.LeadIn), "branch deployment is harmless until separated");
            foreach (var shot in first)
            { shot.SetPhysicsProcess(false); shot._PhysicsProcess(0.3); shot._PhysicsProcess(19 / shot.Velocity.Length()); }
            if (capture) await SplitShot("1_branches");
            float middleRadius = first[0].Radius;
            foreach (var shot in first) shot._PhysicsProcess(13.1 / shot.Velocity.Length());
            var second = Shots(true);
            foreach (var shot in second) { shot.SetPhysicsProcess(false); shot._PhysicsProcess(0.3); }
            Check(second.Length == 9 && second.All(b => Read<int>(b, "_reiSplitsRemaining") == 0
                && Mathf.IsEqualApprox(b.Velocity.Length(), speed * 0.81f)
                && b.Radius < middleRadius && b.Charged == charged && b.CollisionLayer == 8 && b.CollisionMask == 1
                && ReferenceEquals(Read<BulletArt.PlayerVisual>(b, "_playerVisual"), BulletArt.PlayerShot(Job.Magic))),
                "second split produces exactly nine smaller hostile fragments without multiplying difficulty twice");
            float clearance = second.SelectMany((a, i) => second.Skip(i + 1).Select(b => Clearance(a, b))).Min();
            Check(clearance >= BulletPool.BossLaneGap, $"Rei fragments leave {clearance:0.0}px lanes around a {Player.HitRadius * 2}px player core");
            if (aimed)
            {
                var center = second.Single(b => b.BossAimTarget.HasValue);
                Check(center.BossAimTarget == launchTarget
                    && center.Velocity.Normalized().Dot(center.GlobalPosition.DirectionTo(launchTarget)) > 0.99999f,
                    "aimed split keeps one route through the launch-time target without tracking the moved player");
            }
            foreach (var shot in second) { shot.SetPhysicsProcess(false); shot._PhysicsProcess(0.3); }
            if (capture) await SplitShot("2_fragments");
            foreach (var shot in second)
                for (int frame = 0; frame < 600 && shot.Active; frame++) shot._PhysicsProcess(1d / 60);
            Check(Shots(true).Length == 0, "final fragments never split again and leave the arena");
        }
        game.Difficulty = GameManager.Diff.Hard;
        foreach (int remaining in new[] { 2, 1 })
        {
            var close = Pool.SpawnBossShot(new Vector2(250, 108), Vector2.Left, Job.Magic, false);
            close.MakeReiSplit(remaining);
            close.SetPhysicsProcess(false);
            float distance = remaining == 2 ? 42 : 32;
            player.GlobalPosition = close.GlobalPosition + Vector2.Left * (distance + 15);
            close._PhysicsProcess((distance + 0.1) / close.Velocity.Length());
            Check(close.Active && Shots(true).Length == 1 && Read<int>(close, "_reiSplitsRemaining") == 0,
                "either split is canceled near the player");
            player.GlobalPosition = new Vector2(Field.Left, Field.Bottom);
            close._PhysicsProcess(0.3);
            Check(Shots(true).Length == 1, "canceled splits never resume after the player moves away");
            Pool.DespawnAll();
        }
        var outside = Pool.SpawnBossShot(new Vector2(Field.Right - 4, 108), Vector2.Right, Job.Magic, false);
        outside._PhysicsProcess(1);
        Check(!outside.Active && Shots(true).Length == 0, "offscreen seeds cannot create fragments");
        var canceled = Pool.SpawnBossShot(new Vector2(300, 108), Vector2.Left, Job.Magic, false);
        canceled.BossAimTarget = player.GlobalPosition;
        Pool.DespawnAll();
        canceled._PhysicsProcess(1);
        Check(Shots(true).Length == 0, "cleared seeds cannot split later");
        var reused = Pool.Spawn(new Vector2(300, 108), Vector2.Left * 60, false);
        Check(ReferenceEquals(reused, canceled) && Read<int>(reused, "_reiSplitsRemaining") == 0 && !reused.BossAimTarget.HasValue,
            "pool reuse does not transfer boss splits to player shots");
        Pool.DespawnAll();
        player.GlobalPosition = original;
        game.Difficulty = GameManager.Diff.Normal;
    }

    private async Task SplitShot(string label)
    {
        foreach (var bullet in Shots(true)) bullet.SetPhysicsProcess(false);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string dir = ProjectSettings.GlobalizePath("res://build/qa_story/rei_split");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{dir}/{label}.png") == Error.Ok, "split screenshot saved: " + label);
    }

    private void CheckReactionWindow(Player player)
    {
        var game = GameManager.Instance!;
        Vector2 original = player.GlobalPosition;
        game.SelectedJob = Job.Tank;
        Engine.TimeScale = 1;
        foreach (var diff in new[] { GameManager.Diff.Hard, GameManager.Diff.Lunatic })
        foreach (var job in new[] { Job.Melee, Job.Heal, Job.Magic, Job.Tank })
        foreach (bool charged in new[] { false, true })
        {
            game.Difficulty = diff;
            float contact = 0;
            foreach (bool dodge in new[] { false, true })
            {
                Pool.DespawnAll();
                player.GlobalPosition = new Vector2(200, 108);
                Pool.SpawnBossShot(new Vector2(300, 108), Vector2.Left, job, charged);
                bool hit = false;
                for (int frame = 0; frame < 240; frame++)
                {
                    if (dodge && frame >= 21)
                        player.GlobalPosition = new Vector2(200, Mathf.Max(30, player.GlobalPosition.Y - player.SlowestMoveSpeed / 60));
                    foreach (var (bullet, id) in Shots(true).Select(b => (b, b.ActivationId)).ToArray())
                        if (bullet.Active && bullet.ActivationId == id)
                        { bullet.SetPhysicsProcess(false); bullet._PhysicsProcess(1d / 60); }
                    hit = Shots(true).Any(b => b.CollisionLayer != 0 && HitsPlayer(b, player.GlobalPosition));
                    if (hit) { if (!dodge) contact = (frame + 1) / 60f; break; }
                }
                Check(dodge ? !hit : hit && contact >= 0.75f,
                    $"{job}/{diff}/{charged}: " + (dodge ? "locked Mina can sidestep after a 0.35s reaction delay" : $"100px contact takes {contact:0.00}s"));
            }
        }
        Pool.DespawnAll();
        player.GlobalPosition = original;
        game.Difficulty = GameManager.Diff.Normal;
    }

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
                    var body = (Bullet)fire.Invoke(boss, new object[] { Pool, boss.ShotCenter, Vector2.Left * 70, 4f, 1, true })!;
                    body.SetPhysicsProcess(false);
                    string label = job + (charged ? " charge" : " normal");
                    Check(body.Charged == charged && Mathf.IsEqualApprox(body.Radius,
                            (charged ? BulletPool.BossChargeRadius : BulletPool.BossRadius) * (job == Job.Magic ? 1.5f : 1)) && body.Homing == playerShot.Homing
                        && body.Accel == playerShot.Accel && body.TurnRateOverride == playerShot.TurnRateOverride,
                        label + " keeps character behavior with standardized boss hit radius");
                    float playerSpeed = job == Job.Melee ? charged ? 240 : 12 : charged ? Jobs.Get(job).ChargeSpeed : Jobs.Get(job).ShotSpeed;
                    Check(Mathf.IsEqualApprox(playerShot.Velocity.Length(), playerSpeed)
                        && playerShot.Radius == (charged ? Jobs.Get(job).ChargeRadius : Jobs.Get(job).ShotRadius)
                        && Read<int>(playerShot, "_reiSplitsRemaining") == 0, label + " player speed and splitting remain unchanged");
                    Check(Mathf.IsEqualApprox(body.Velocity.Length(), BossSpeed(job, charged)), label + " initial speed is tuned for dodging");
                    if (body.Accel)
                    {
                        Check(Mathf.IsEqualApprox(Read<float>(body, "_accelDelay"), 0.25f)
                            && Mathf.IsEqualApprox(body.AccelRampSeconds, charged ? 2.4f : 2.1f)
                            && Mathf.IsEqualApprox(Read<float>(body, "_fastSpeed"), (charged ? 100f : 88f) * game.BulletSpeedMul), label + " has readable windup and capped launch speed");
                        body._PhysicsProcess(Read<float>(body, "_accelDelay") - 0.01);
                        body._PhysicsProcess(0.011);
                        Check(!body.AccelCharging && body.Velocity.Length() < Read<float>(body, "_fastSpeed") * 0.2f,
                            label + " launch ramps up instead of teleporting to full speed");
                        float previousSpeed = body.Velocity.Length();
                        for (int sample = 0; sample < 4; sample++)
                        {
                            body._PhysicsProcess(body.AccelRampSeconds / 4);
                            float speed = body.Velocity.Length();
                            Check(speed > previousSpeed, label + " smoothly gains speed throughout the ramp");
                            if (sample == 0) Check(speed < Read<float>(body, "_fastSpeed") * 0.1f, label + " starts extremely slowly");
                            previousSpeed = speed;
                        }
                        Check(!body.AccelCharging && Mathf.IsEqualApprox(body.Velocity.Length(), Read<float>(body, "_fastSpeed")), label + " actually accelerates");
                    }
                    Check(ReferenceEquals(Read<BulletArt.PlayerVisual>(body, "_playerVisual"), Read<BulletArt.PlayerVisual>(playerShot, "_playerVisual"))
                        && Read<Texture2D?>(body, "_sprite") == null, label + " reuses exact player artwork");
                    Check(body.Material is ShaderMaterial && body.CollisionLayer == 8 && body.CollisionMask == 1,
                        label + " recolor stays hostile");
                    GD.Print($"[BossCharacterShotQA] {label}: silhouette parts={body.Footprint!.Shapes.Length}, bound={body.CollisionRadius:F2}");
                    body.GlobalPosition = new Vector2(250, 100);
                    if (body.Homing)
                    {
                        body.Velocity = body.GlobalPosition.DirectionTo(root.Player.GlobalPosition).Rotated(0.5f) * body.Velocity.Length();
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
                        float scale = image.GetWidth() / 384f;
                        Vector2 center = body.GetGlobalTransformWithCanvas().Origin * scale;
                        float core = 0;
                        for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                        {
                            var pixel = image.GetPixel(Mathf.RoundToInt(center.X) + x, Mathf.RoundToInt(center.Y) + y);
                            core = Mathf.Max(core, Mathf.Min(pixel.R, Mathf.Min(pixel.G, pixel.B)));
                        }
                        Check(core > 0.72f, label + $" artwork remains bright without a white damage dot ({core:F3})");
                        Check(body.Footprint is { Shapes.Length: > 0 } footprint && footprint.Shapes.All(s => s is ConvexPolygonShape2D),
                            label + " uses the illustrated silhouette instead of a circle");
                    }
                    Pool.DespawnAll();
                    var barrage = (Bullet)fire.Invoke(boss, new object[] { Pool, boss.ShotCenter, Vector2.Left * 70, 4f, 1, true })!;
                    barrage.SetPhysicsProcess(false);
                    Check(MatchesCharacter(barrage, job, charged), label + " body fire shares character shot performance");
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
                    Check(Shots(true).Length > 0 && Shots(true).All(b => !b.Charged && (job == Job.Melee
                        ? b.Accel && Read<BulletArt.PlayerVisual?>(b, "_playerVisual") != null
                        : Read<BulletArt.PlayerVisual?>(b, "_playerVisual") == null && Read<Texture2D?>(b, "_sprite") != null)),
                        label + " edge volley keeps its motif, with acceleration for Akari");
                    edge.Cancel();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Pool.DespawnAll();
                    var danmaku = new BossDanmaku(boss);
                    danmaku.Tick(1);
                    var remote = Read<BossCrossfire>(danmaku, "_crossfire");
                    danmaku.Tick(remote.WarningRemaining + 0.001);
                    Check(Shots(true).Length > 0 && Shots(true).All(b => MatchesCharacter(b, job, charged)),
                        label + $" sustained body fire uses same shot (count={Shots(true).Length}, visible={boss.IsVisibleInTree()}, transforming={boss.Transforming}, center={boss.ShotCenter})");
                    remote.Cancel();
                    Pool.DespawnAll();
                    string pattern = job switch { Job.Melee => "AimedSpread", Job.Heal => "FanDown", _ => "Aimed" };
                    boss.GetType().GetMethod(pattern, Private)!.Invoke(boss, new object[] { Pool });
                    Check(Shots(true).Length > 0 && Shots(true).All(b => MatchesCharacter(b, job, charged) && !b.SoftenOnGraze && !b.IsPatternBullet),
                        label + " boss pattern preserves character shot properties");
                    Pool.DespawnAll();
                    if (boss is BossKoharu)
                    {
                        CheckEnemyHoming(root.Player, charged);
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
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                }
                Pool.DespawnAll();
                boss.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await CheckReiSplits(root.Player);
            CheckReactionWindow(root.Player);
            foreach (var playerJob in new[] { Job.Tank, Job.Melee, Job.Heal, Job.Magic })
            foreach (var style in Enum.GetValues<BossShotStyle>())
            {
                var color = Bullet.BossColor(playerJob, style);
                Check(color.SrgbToLinear().Luminance > 0.26f, $"{playerJob}/{style}: enemy body color is bright enough on dark stage art");
                float hue = Mathf.Abs(Mathf.AngleDifference(color.H * Mathf.Tau, BulletArt.PlayerColor(playerJob).H * Mathf.Tau));
                Check(hue > Mathf.DegToRad(43), $"{playerJob}/{style}: enemy hue stays distinct from player hue");
                foreach (var other in Enum.GetValues<BossShotStyle>().Where(s => s != style))
                    Check(Mathf.Abs(Mathf.AngleDifference(color.H * Mathf.Tau, Bullet.BossColor(playerJob, other).H * Mathf.Tau)) > Mathf.DegToRad(40),
                        $"{playerJob}: {style} and {other} attacks have distinct hues");
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
