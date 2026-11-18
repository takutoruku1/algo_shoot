using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private int _overflowReplyLimit;
    private (double At, Vector2 Goal)[]? _twoBeatSteps;
    private bool _scriptedMovement;
    private async Task RunSurvivalClip(int phase, BossAttackCycle.Slot slot, int start)
    {
        _running = false;
        _pilot.Release();
        await SettleEasyFrame();
        _easyStationary = false;
        _easyStart = EasyStarts[start];
        _bodyPilotGoal = _easyStart;
        _bodyPilotCue = -1;
        _bodyOrbitPlanned = false;
        _bandReadAt = -1;
        _bandGoal = _easyStart.X;
        _bandForecast = Array.Empty<Rect2>();
        _bandVolleys = 0;
        _bandGeometryMatches = true;
        _shortestBandHold = double.PositiveInfinity;
        _bodyOrbitDirection = 1;
        _boss.GlobalPosition = new Vector2(Field.BossFrameCenterX, Field.BossZoneCenterY);
        _player.GlobalPosition = _easyStart;
        var routeArg = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--two-beat-route="));
        if (routeArg != null)
        {
            EasyRequire(_boss is BossKoharu && phase == 0 && start == 0 && slot.Kind == BossTechniqueKind.Normal && slot.Variant == 0
                && _game.Difficulty == GameManager.Diff.Normal, "Two-beat recording requires Koharu's first NORMAL technique.");
            (double At, Vector2 Goal)[] approach = [
                (1.8, new(139, 168)), (3.4, new(123, 160)), (4.8, new(133, 160)),
                (5.9, new(134, 185)), (7.65, new(102, 165)), (8.8, new(96, 135)),
                (10.2, new(129, 124)), (11.3, new(102.6f, 124)) ];
            (double At, Vector2 Goal)[] crossing = [.. approach, (11.7, new(139.9f, 86.7f)),
                (12.5, new(139.9f, 99.9f)), (12.7, new(137.2f, 104.6f)) ];
            _twoBeatSteps = routeArg["--two-beat-route=".Length..] switch
            {
                "inside" => [.. crossing, (12.7833, new(140, 82)), (14.8, new(78, 127)),
                    (16.7, new(78, 202)), (18.5, new(107, 165)), (20, new(201, 197))],
                "outside" => [(1.55, new(85, 174)), (3.95, new(83, 196)), (5.65, new(87, 127)),
                    (7.5, new(151, 202)), (9.35, new(88, 143)), (11.3, new(78, 161)),
                    (13.2, new(78, 93)), (15.4, new(168, 159)), (17.5, new(228, 127)), (20.3, new(161, 68))],
                "peel" => [.. crossing,
                    (12.7833, new(140.6f, 91.8f)), (12.9833, new(232.1f, 39)), (14.5833, new(300, 70)),
                    (16.5833, new(295, 15)), (18.5833, new(190, 37)), (20.2, new(172, 16))],
                "follow" => [.. crossing,
                    (12.7833, new(143.8f, 93.2f)), (12.9833, new(140.4f, 80.4f)), (13.1833, new(127.6f, 77)),
                    (13.3833, new(118.3f, 86.3f)), (13.5833, new(124.9f, 97.8f)), (13.7833, new(137.6f, 101.2f)),
                    (13.9833, new(147, 110.5f)), (14.1833, new(160.2f, 110.5f)), (14.3833, new(166.8f, 121.9f)),
                    (14.5833, new(218, 154)), (16.5833, new(141, 196)), (18.5833, new(198, 156)),
                    (22, new(112, 115))],
                "edge-exit" => [.. approach, (11.517, new(Field.Left, 124)), (19.55, new(94, 150)), (20, new(156, 200))],
                _ => throw new ArgumentException("Unknown two-beat route; use inside, outside, peel, follow or edge-exit.")
            };
        }
        if (_twoBeatSteps == null && !OS.GetCmdlineUserArgs().Contains("--showcase-fire")) _player.AddLife(10);
        Write(_player, "_invincible", false);
        Write(_player, "_invincibleTimer", 0f);
        Write(_player, "_hitInvincible", false);
        Write(_player, "_dodgeInv", 0f);
        EasyRequire(!_game.TutorialNoConsume && !QaPilot.GodActive, "Survival audit must use real damage.");
        _hud.HideSpellCard();
        await EasyPhysicsFrames(12);
        int lives = _player.Lives;
        int initialGrazes = _game.GrazeCount;
        long firstFrame = (long)Engine.GetProcessFrames();
        _easyAttack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
        _easyStationary = true;
        _running = true;
        for (int frame = 0; frame < 180 && _easyAttack.SpellName.Length == 0 && !_easyAttack.Finished; frame++)
            await EasyPhysicsFrames(1);
        EasyRequire(_easyAttack.SpellName.Length > 0, "Survival technique did not start after arena entry.");
        _easyStationary = false;
        string name = _easyAttack.SpellName, key = _easyAttack.MechanicKey;
        bool replyTest = key == "mina.reply-driven-stars";
        bool overflowTest = replyTest && OS.GetCmdlineUserArgs().Contains("--reply-overflow");
        _overflowReplyLimit = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--reply-limit=")) is { } limit
            ? Math.Clamp(int.Parse(limit["--reply-limit=".Length..]), 0, 2) : 0;
        bool peelAttack = key == "rei-edited-page-peeling" && (int)_game.Difficulty >= 2;
        bool peelFire = peelAttack && !OS.GetCmdlineUserArgs().Contains("--peel-no-fire");
        bool peelPlaythrough = peelAttack && OS.GetCmdlineUserArgs().Contains("--peel-playthrough");
        bool showcaseFire = OS.GetCmdlineUserArgs().Contains("--showcase-fire");
        Write(_player, "_fireCooldown", peelFire || showcaseFire || replyTest && (!overflowTest || _overflowReplyLimit > 0) ? 0f : 99999f);
        if (showcaseFire) _boss.SetPanelsInvulnerable(false);
        int initialBossHp = Read<int>(_boss, "_hp");
        int initialShieldInk = Read<List<Panel>>(_boss, "_panels").Where(IsInstanceValid).Sum(panel => Math.Max(0, panel.Ink));
        var playerShots = new HashSet<(ulong, ulong)>();
        var checkedDraftRounds = new HashSet<int>();
        var acknowledgedRounds = new HashSet<int>();
        var unpairedShots = new HashSet<(ulong, ulong)>();
        int readingPasses = 0;
        var wingSwitches = new Dictionary<double, (double ShownAt, int WeakSide)>();
        var verifiedWingSwitches = new HashSet<double>();
        bool ended = false, completed = false;
        int shots = 0;
        double elapsed = 0;
        _easyAttack.Ended += attack => { ended = true; completed = attack.Completed; shots = attack.ShotsCreated; elapsed = attack.Elapsed; };
        var trace = new List<object>();
        var mergingShots = new HashSet<(ulong, ulong)>();
        var mergingRounds = new Dictionary<double,int>();
        _running = true;
        GD.Print($"[SURVIVAL] START {_scene} {phase}:{slot.Kind}:{slot.Variant} start={start} {name}");
        int attackContacts = 0, bodyContacts = 0;
        using var passage = HardPassageTrace.Open($"{_output}/opening_{phase}_{slot.Kind}_{slot.Variant}_{start}.jsonl");
        using var clearanceCore = new CircleShape2D { Radius = Player.HitRadius * 1.5f };
        int clearanceContacts = 0;
        int overflowFrames = 0;
        int cropGateChecks = 0;
        Vector2? burdenPivot = null;
        float burdenLength = 0, burdenMinX = float.MaxValue, burdenMaxX = float.MinValue, lowestGap = float.MaxValue;
        var burdenWarnings = new Dictionary<(float Left, float Right), double>();
        var burdenLandings = new HashSet<(float Left, float Right)>();
        bool cropPassed = false;
        int cropRetakeFrames = 0;
        bool retakeTest = OS.GetCmdlineUserArgs().Contains("--crop-retake");
        int barrierChecks = 0, throughBarrierFrames = 0;
        float pursuitDirectionError = 0;
        Vector2? previousPursuitHead = null;
        float maximumStep = 0;
        Vector2 previous = _player.GlobalPosition;
        for (int frame = 0; frame < 180 * 60 && !ended; frame++)
        {
            await EasyPhysicsFrames(1);
            Vector2 at = _player.GlobalPosition;
            foreach (var shot in GetTree().GetNodesInGroup("player_bullets").OfType<Bullet>().Where(shot => shot.Active))
                playerShots.Add((shot.GetInstanceId(), shot.ActivationId));
            maximumStep = Math.Max(maximumStep, at.DistanceTo(previous));
            previous = at;
            bool body = EasyBodyAt(at);
            bool hazard = !ended && _easyAttack.CoversPoint(at);
            if (!ended) passage?.Frame(_easyAttack, at);
            if (!ended && _game.Difficulty == GameManager.Diff.Hard && key == "rei-crop-three-edges")
            {
                var crop = (ReiBossTechnique)_easyAttack;
                cropPassed |= Read<bool>(crop, "_cropPassed");
                if (crop.Elapsed < Read<double>(crop, "_cropFilmEnd"))
                {
                    Vector2 entry = crop.VisibleCropEntry;
                    Vector2 toward = entry.DirectionTo(crop.VisibleCropExit);
                    Vector2 end = (entry + toward * Field.WorldRight).Clamp(Field.Rect.Position, Field.Rect.End);
                    int steps = Mathf.CeilToInt(entry.DistanceTo(end));
                    for (int i = 0; i <= steps; i++)
                    {
                        float room = crop.DangerDistance(entry.Lerp(end, i / (float)steps), false) + _player.CurrentHitRadius;
                        EasyRequire(room >= 5 - .01f, "An attack entered the advertised crop exit or its continuation.");
                    }
                }
                if (retakeTest && crop.Elapsed >= Read<double>(crop, "_cropFilmEnd") + 1.6)
                {
                    bool vertical = Read<int>(crop, "_cropExit") is 0 or 2;
                    var walls = crop.PracticeHazards.Where(h => h.Shape == 2 && h.Live).ToArray();
                    float gap = walls.Length != 2 ? 0 : vertical ? walls[1].A.X - walls[0].B.X : walls[1].A.Y - walls[0].B.Y;
                    EasyRequire(Math.Abs(gap - 10) < .01f, "The retake shutter did not leave ten pixels.");
                    cropRetakeFrames++;
                }
                if (crop.Elapsed >= Read<double>(crop, "_cropRimAt") && crop.Elapsed < Read<double>(crop, "_warning"))
                {
                    Vector2 mid = (crop.VisibleCropEntry + crop.VisibleCropExit) / 2;
                    Vector2 along = crop.VisibleCropEntry.DirectionTo(crop.VisibleCropExit).Orthogonal();
                    EasyRequire(Math.Abs(crop.DangerDistance(mid, false) + _player.CurrentHitRadius - 5) < .02f,
                        "Crop gate contours are not ten pixels apart.");
                    EasyRequire(!crop.CoversPoint(mid) && crop.CoversPoint(mid + along * 4) && crop.CoversPoint(mid - along * 4),
                        "The center and edge controls did not straddle the crop gate collision.");
                    cropGateChecks++;
                }
            }
            if (!ended && _game.Difficulty == GameManager.Diff.Hard && key == "mina.suspended-burden")
            {
                foreach (var h in _easyAttack.PracticeHazards)
                {
                    float bottom = Math.Max(h.A.Y, h.B.Y) + h.Radius;
                    if (h.Shape == 0)
                    {
                        if (!burdenPivot.HasValue) { burdenPivot = h.A; burdenLength = h.A.DistanceTo(h.B); }
                        EasyRequire(h.A.DistanceTo(burdenPivot.Value) < .01f && Math.Abs(h.A.DistanceTo(h.B) - burdenLength) < .01f
                            && h.Radius == 3, "The pendulum rod changed its pivot, length, or thickness.");
                    }
                    if (h.Shape == 1)
                    {
                        EasyRequire(h.Radius == 22 && Field.Bottom - bottom >= 9.99f, "The pendulum bob crossed the floor clearance.");
                        lowestGap = Math.Min(lowestGap, Field.Bottom - bottom);
                        burdenMinX = Math.Min(burdenMinX, h.A.X);
                        burdenMaxX = Math.Max(burdenMaxX, h.A.X);
                    }
                    if (h.Shape != 2) continue;
                    var lane = (h.A.X, h.B.X);
                    if (!h.Live) burdenWarnings.TryAdd(lane, _easyAttack.Elapsed);
                    else
                    {
                        const float height = 12;
                        bool clipped = h.A.Y < .01f || h.B.Y > Field.Bottom - .01f;
                        EasyRequire(burdenWarnings.TryGetValue(lane, out double cueAt) && _easyAttack.Elapsed - cueAt >= 1.55
                            && (clipped ? h.B.Y - h.A.Y <= height + .02f : Math.Abs(h.B.Y - h.A.Y - height) < .02f),
                            "A falling bundle differed from its forecast, clipping or release height.");
                        if (h.A.Y <= Field.Bottom && h.B.Y >= Field.Bottom) burdenLandings.Add(lane);
                    }
                }
            }
            if (overflowTest && !ended && ((MinaBossTechnique)_easyAttack).ReplyFailed)
            {
                var burst = _easyAttack.PracticeHazards.FirstOrDefault(h => h.Shape == 1 && h.Live);
                if (burst.Radius > 0)
                {
                    EasyRequire(burst.Radius == ((MinaBossTechnique)_easyAttack).UnansweredRadius, "The unanswered star changed size after warning.");
                    EasyRequire(_easyAttack.DangerDistance(at, false) > _player.CurrentHitRadius * .5f,
                        "The clearance probe touched the overflow.");
                    overflowFrames++;
                }
            }
            if (!ended && _easyAttack is MinaBossTechnique wing && key == "mina.wing-displacement")
            {
                if (wing.VisibleWingSwitch is { } next && !wingSwitches.ContainsKey(next.At))
                    wingSwitches.Add(next.At, (wing.Elapsed, next.WeakSide));
                foreach (var change in wingSwitches.Where(change => wing.Elapsed >= change.Key))
                {
                    if (!verifiedWingSwitches.Add(change.Key)) continue;
                    double lead = _game.Difficulty switch { GameManager.Diff.Easy => 2.4, GameManager.Diff.Normal => 1.8, GameManager.Diff.Hard => 1.2, _ => 3.2 };
                    EasyRequire(Math.Abs(change.Key - change.Value.ShownAt - lead) <= 1d / 60 + .001,
                        "Wing force changed before the announced preparation time.");
                    int side = _player.GlobalPosition.X < Field.CenterX ? -1 : 1;
                    EasyRequire(Math.Abs(wing.VisibleWingWind.X) == (side == change.Value.WeakSide ? 12 : 38),
                        "Wing switch cue disagreed with the actual force.");
                }
            }
            if (!ended && _easyAttack is KoharuBossTechnique reading && key == "koharu.reading-head" && (int)_game.Difficulty >= 2)
            {
                foreach (var line in reading.ReadingLines.Skip(readingPasses))
                {
                    EasyRequire(line.FiresAt - line.LockedAt >= 1.35 - .00001, "Reading line did not keep its completed warning.");
                    EasyRequire(Math.Abs(line.From.Y - at.Y) <= Field.Height / 12 + _player.MoveSpeed / 60 + 1,
                        "New reading line did not capture the visible current row.");
                }
                readingPasses = reading.ReadingLines.Count;
            }
            if (!ended && _easyAttack is KoharuBossTechnique unpaired && key == "koharu.unpaired-light")
            {
                foreach (var shot in unpaired.OwnedShots.Where(shot => !shot.LeadIn && shot.CollisionLayer != 0))
                {
                    EasyRequire(!shot.Homing, "A fixed-route charm homed after its forecast.");
                    var path = unpaired.UnpairedVolleys.Where(v => v.FiresAt <= unpaired.Elapsed)
                        .SelectMany(v => unpaired.UnpairedRays(v).Select(ray => (Volley: v, Ray: ray)))
                        .Where(p => p.Ray.Direction.Dot(shot.Velocity.Normalized()) > .999f)
                        .MinBy(p => Math.Abs(p.Ray.Direction.Cross(shot.GlobalPosition - p.Ray.From)));
                    EasyRequire(Math.Abs(path.Ray.Direction.Cross(shot.GlobalPosition - path.Ray.From)) < 1,
                        "A charm left its announced straight route.");
                    EasyRequire(path.Volley.FiresAt - path.Volley.StartsAt >= (_game.Difficulty == GameManager.Diff.Easy ? 2 : 1.1),
                        "Charm warning was shorter than the completed-cue hold.");
                    unpairedShots.Add((shot.GetInstanceId(), shot.ActivationId));
                }
            }
            if (!ended && key == "mina.acknowledged-rows")
            {
                var drafts = Read<IEnumerable>(_easyAttack, "_easyMemoryCues").Cast<object>()
                    .Where(c => Read<int>(c, "Mode") == 1).GroupBy(c => Read<int>(c, "Round"));
                foreach (var round in drafts)
                {
                    if (checkedDraftRounds.Add(round.Key))
                    {
                        var safe = round.Single(c => Read<double?>(c, "CancelledAt") == Read<double>(c, "StartsAt"));
                        EasyRequire(Read<bool>(safe, "CardAccessVerified") && Read<double>(safe, "CardTravelSeconds") <= 2,
                            "The initially safe row has no verified normal-speed route.");
                    }
                    if (round.Any(c => Read<double?>(c, "CancelledAt") > Read<double>(c, "StartsAt"))) acknowledgedRounds.Add(round.Key);
                }
            }
            if (!ended && key == "rei-merge-then-branch")
            {
                double fire = Read<double>(_easyAttack, "_amplificationStart") + Read<double>(_easyAttack, "_warning");
                if (_easyAttack.Elapsed >= fire && _easyAttack.Elapsed < fire + 1.8)
                {
                    EasyRequire(_game.Difficulty != GameManager.Diff.Easy || Read<double>(_easyAttack, "_warning") - .6 >= 2,
                        "Easy amplification launched before two seconds of complete route warning.");
                    var incoming = _easyAttack.OwnedShots.Where(shot => shot.BossPathDriven).ToArray();
                    float opening = _game.Difficulty switch { GameManager.Diff.Easy => 14, GameManager.Diff.Normal => 12,
                        GameManager.Diff.Hard => 10, _ => 8 };
                    var merge = Read<Vector2[]>(_easyAttack, "_points")[0];
                    float upper = Mathf.Clamp(merge.Y - 38, Field.Top - 12, Field.Bottom + 12);
                    float lower = Mathf.Clamp(merge.Y + 38, Field.Top - 12, Field.Bottom + 12);
                    int expected = 1 + Mathf.CeilToInt(new Vector2(Field.Width + 24, lower - upper).Length() / (opening + 4));
                    var ordered = incoming.Select(b => b.GlobalPosition).OrderBy(p => p.X).ToArray();
                    for (int i = 1; i < ordered.Length; i++)
                        EasyRequire(ordered[i].DistanceTo(ordered[i - 1]) <= opening + 4.02f, "Incoming copy pitch exceeded the difficulty target.");
                    mergingRounds[fire] = expected;
                    EasyRequire(incoming.Length == expected && incoming.All(shot => !shot.LeadIn && shot.CollisionLayer != 0),
                        "Incoming manuscripts did not carry their announced collision.");
                    float expectedRadius = BulletPool.BossHitShape(Job.Magic, _boss.HasEvolved).Radius;
                    EasyRequire(incoming.All(shot => Math.Abs(shot.IllustrationRadius - expectedRadius) < .15f
                        && shot.CollisionRadius <= expectedRadius + .15f),
                        "Merging manuscript width differed from its full forecast.");
                    foreach (var shot in incoming) mergingShots.Add((shot.GetInstanceId(), shot.ActivationId));
                }
            }
            if (!ended && _easyAttack is KoharuBossTechnique pursuit && key == "koharu.pursuit-train" && (int)_game.Difficulty >= 2)
            {
                var barriers = pursuit.VisiblePursuitBarriers.ToArray();
                foreach (var barrier in barriers.Where(b => b.Solid))
                    EasyRequire(!barrier.Bounds.Grow(Player.HitRadius - .01f).HasPoint(at), "Player entered a solid pursuit barrier.");
                if (barrierChecks == 0 && barriers.All(b => b.Solid))
                {
                    foreach (var barrier in barriers)
                    {
                        Rect2 bounds = barrier.Bounds.Grow(Player.HitRadius);
                        Vector2 center = bounds.GetCenter();
                        foreach (var direction in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
                        {
                            Vector2 from = center + direction * (bounds.Size * .5f + Vector2.One * 8);
                            Vector2 requested = center - direction * (bounds.Size * .5f + Vector2.One * 8);
                            Vector2 stopped = pursuit.ResolvePlayerMotion(from, requested);
                            EasyRequire(stopped.DistanceTo(requested) > 8 && !bounds.Grow(-.01f).HasPoint(stopped), "Pursuit barrier did not block a crossing movement.");
                            barrierChecks++;
                        }
                        Vector2 corner = bounds.Position - Vector2.One * 2;
                        EasyRequire(!bounds.Grow(-.01f).HasPoint(pursuit.ResolvePlayerMotion(corner, corner + Vector2.One * 12)), "Diagonal movement tunneled into a barrier.");
                        barrierChecks++;
                    }
                }
                if (pursuit.OwnedShots.Any())
                {
                    Vector2 head = Read<Vector2>(pursuit, "_point");
                    if (pursuit.PursuitSteeredThisFrame && previousPursuitHead is { } oldHead && head.DistanceTo(oldHead) > .001f)
                    {
                        Vector2 target = Read<Vector2>(pursuit, "_lastPlayer");
                        float error = Math.Abs(oldHead.DirectionTo(head).Cross(oldHead.DirectionTo(target)));
                        pursuitDirectionError = Math.Max(pursuitDirectionError, error);
                        EasyRequire(error < .001f, "Pursuit head deviated from the direct route to the player.");
                    }
                    previousPursuitHead = head;
                    if (barriers.Any(b => b.Solid && b.Bounds.HasPoint(head))) throughBarrierFrames++;
                }
            }
            bool bullet = !ended && _easyAttack.OwnedShots.Any(b => !b.LeadIn && b.CollisionLayer != 0
                && b.GlobalPosition.DistanceTo(at) <= b.CollisionRadius * Math.Max(b.GlobalTransform.X.Length(), b.GlobalTransform.Y.Length()) + Player.HitRadius
                && b.Footprint != null && b.Footprint.Shapes.Any(shape => shape.Collide(b.GlobalTransform, EasyPlayerCore, new Transform2D(0, at))));
            if (!ended && (_easyAttack.DangerDistance(at, false) + _player.CurrentHitRadius <= clearanceCore.Radius
                || _easyAttack.OwnedShots.Any(b => !b.LeadIn && b.CollisionLayer != 0
                    && b.GlobalPosition.DistanceTo(at) <= b.CollisionRadius * Math.Max(b.GlobalTransform.X.Length(), b.GlobalTransform.Y.Length()) + clearanceCore.Radius
                    && b.Footprint != null && b.Footprint.Shapes.Any(shape => shape.Collide(b.GlobalTransform, clearanceCore, new Transform2D(0, at))))))
                clearanceContacts++;
            if (body) bodyContacts++;
            if (hazard || bullet) attackContacts++;
            if (frame % 6 == 0 || body || hazard || bullet || _player.Lives != lives)
                trace.Add(new { t = _easyAttack.Elapsed, x = at.X, y = at.Y,
                    boss_x = _boss.GlobalPosition.X, boss_y = _boss.GlobalPosition.Y, lives = _player.Lives, body, hazard, bullet,
                    grazes = _game.GrazeCount - initialGrazes,
                    warning_count = _easyAttack.EasyCues.Count(), completed_warning_count = _easyAttack.EasyCues.Count(c => c.Progress >= 1),
                    rally = _easyAttack is ReiBossTechnique { RallyTechnique: true } observed ? observed.RallyBalls.Select(ball => new {
                        ball.Index, stage = ball.Stage.ToString(), ball.Returns, ball.Attempts, ball.IsDanger,
                        ball.Revealed, ball.ReturnEligible, ball.LeftBounces, ball.RightBounces, ball.TopBounces, ball.BottomBounces,
                        x = ball.Position.X, y = ball.Position.Y, vx = ball.Velocity.X, vy = ball.Velocity.Y }).ToArray() : null });
            if (!peelPlaythrough && (_player.Lives != lives || body || hazard || bullet)) break;
            EasyRequire((!Read<bool>(_player, "_invincible") || peelPlaythrough && _player.Lives < lives)
                && Read<float>(_player, "_dodgeInv") <= 0, "Unexpected invincibility during survival audit.");
        }
        _running = false;
        _pilot.Release();
        var rally = _easyAttack as ReiBossTechnique;
        bool rallyAttack = rally?.RallyTechnique == true;
        var replies = _easyAttack as MinaBossTechnique;
        bool replyAttack = replies?.MechanicKey == "mina.reply-driven-stars";
        var addresses = _easyAttack as AkariBossTechnique;
        bool addressAttack = addresses?.MechanicKey == "akari.proximity-addressing";
        bool passed = ended && completed && _player.Lives == lives && attackContacts == 0 && bodyContacts == 0
            && (key != "rei-crop-three-edges" || _game.Difficulty != GameManager.Diff.Hard
                || cropGateChecks > 0 && (retakeTest ? !cropPassed && cropRetakeFrames > 0 : cropPassed))
            && (!rallyAttack || (OS.GetCmdlineUserArgs().Contains("--rally-no-return")
                ? rally!.RallyFailures == ReiBossTechnique.RallyFailureLimit && rally.RallyMissRetaliations == 1 && !rally.RallyWallBroken
                : rally!.RallyFailures == 0 && rally.RallyWallBroken && rally.RallyStreak == 5 && rally.RallyDamageEvents == 1))
            && (!peelAttack || rally!.PeeledEditions.All(peeled => peeled) && !rally.PeelReviewFailed)
            && (!addressAttack || addresses!.AddressLocks.Count == 4 + (int)_game.Difficulty
                && addresses.AddressShotsFired >= 2 * addresses.AddressLocks.Count)
            && (!replyAttack || (overflowTest ? replies!.ReplyFailed && replies.StarReplies.Count == _overflowReplyLimit && overflowFrames > 0
                : replies!.StarReplies.Count == 3 && replies.StarReplies.All(reply => reply.Fired) && !replies.ReplyFailed));
        if (completed && UsesAkariBandPilot)
        {
            EasyRequire(_bandVolleys == 2, "A forecast or its live attack was missing.");
            EasyRequire(_shortestBandHold >= 2 - 1d / 60, "Attack began before the complete bands were held for two seconds.");
            EasyRequire(_bandGeometryMatches, "Live attack outside its displayed band or paper width mismatch.");
        }
        if (ended && key == "koharu.reading-head" && (int)_game.Difficulty >= 2)
            EasyRequire(readingPasses == ((int)_game.Difficulty + 2) * (_game.Difficulty == GameManager.Diff.Hard && _boss is BossKoharu ? 2 : 1), "Reading head omitted a locked pass.");
        if (ended && key == "koharu.unpaired-light" && _easyAttack is KoharuBossTechnique charms)
            EasyRequire(unpairedShots.Count == charms.UnpairedVolleys.Sum(v => charms.UnpairedRays(v).Length) * charms.UnpairedShots,
                "A charm volley omitted announced shots.");
        if (ended && key == "mina.acknowledged-rows" && OS.GetCmdlineUserArgs().Contains("--draft-reply"))
            EasyRequire(checkedDraftRounds.SetEquals(acknowledgedRounds), "A completed draft round had no player reply.");
        if (ended && key == "mina.wing-displacement")
            EasyRequire(verifiedWingSwitches.Count == 2, "Wing attack omitted a displayed force change.");
        if (ended && _game.Difficulty == GameManager.Diff.Hard && key == "mina.suspended-burden")
        {
            EasyRequire(lowestGap < 10.01f && burdenMaxX - burdenMinX >= Field.Width * .75f, "The pendulum did not sweep across the playfield.");
            EasyRequire(burdenWarnings.Count >= 5 && burdenLandings.SetEquals(burdenWarnings.Keys), "A forecast bundle never reached the floor.");
        }
        bool expectedFailure = (key == "mina.suspended-burden" && OS.GetCmdlineUserArgs().Contains("--burden-stay"))
            || OS.GetCmdlineUserArgs().Contains("--crop-offset") || OS.GetCmdlineUserArgs().Contains("--crop-wrong-edge") || OS.GetCmdlineUserArgs().Contains("--peel-no-fire")
            || OS.GetCmdlineUserArgs().Contains("--address-skip") || OS.GetCmdlineUserArgs().Contains("--draft-skip");
        bool peelChoiceVerified = peelPlaythrough && ended && completed && rally!.PeeledEditions.All(peeled => peeled == peelFire)
            && rally.PeelReviewFailed != peelFire;
        if (peelPlaythrough ? !peelChoiceVerified : !passed && !expectedFailure) _reviewFailures++;
        if (ended && key == "rei-merge-then-branch")
            EasyRequire(mergingRounds.Count == (_game.Difficulty >= GameManager.Diff.Hard ? (int)_game.Difficulty + 1 : 1)
                && mergingShots.Count == mergingRounds.Values.Sum(),
                "A complete amplification round omitted its collidable incoming manuscripts.");
        var failurePosition = _player.GlobalPosition;
        float? forecastGap = !passed && !ended && OS.GetCmdlineUserArgs().Contains("--forecast-pilot")
            ? SurvivalForecast(_easyAttack, 1)[0].Gap(failurePosition) : null;
        var nearShots = !passed && !ended ? _easyAttack.OwnedShots
            .Where(b => !b.LeadIn && b.CollisionLayer != 0 && b.GlobalPosition.DistanceTo(failurePosition) < 30)
            .Select(b => new { x = b.GlobalPosition.X, y = b.GlobalPosition.Y, radius = b.CollisionRadius,
                scale_x = b.Scale.X, scale_y = b.Scale.Y, wave = b.BossBarrageWave }).ToArray() : null;
        var collision = !passed ? Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(_easyAttack, "_hazards")
            .Where(h => h.Live).Select(h => new { ax = h.A.X, ay = h.A.Y, bx = h.B.X, by = h.B.Y, radius = h.Radius, shape = h.Shape }).ToArray() : null;
        if (!ended) { elapsed = _easyAttack.Elapsed; shots = _easyAttack.ShotsCreated; _easyAttack.Cancel(); }
        var result = new { character = _scene, difficulty = _game.Difficulty.ToString(), phase,
            start = firstFrame, end = (long)Engine.GetProcessFrames(), fps = 60,
            kind = slot.Kind.ToString(), variant = slot.Variant, name, mechanic = key, start_index = start,
            expected_failure = expectedFailure,
            start_position = new { x = _easyStart.X, y = _easyStart.Y }, passed, completed, elapsed, shots,
            life_loss = lives - _player.Lives, attack_contacts = attackContacts, body_contacts = bodyContacts,
            grazes = _game.GrazeCount - initialGrazes,
            six_pixel_contact_frames = clearanceContacts,
            verified_band_volleys = _bandVolleys, shortest_completed_band_hold = double.IsFinite(_shortestBandHold) ? (double?)_shortestBandHold : null,
            collidable_merging_manuscripts = mergingShots.Count,
            verified_reading_passes = readingPasses, verified_straight_charms = unpairedShots.Count,
            draft_rounds = checkedDraftRounds.Count, player_acknowledged_rounds = acknowledgedRounds.Count,
            player_shots = playerShots.Count, boss_hp_damage = initialBossHp - Read<int>(_boss, "_hp"),
            shield_ink_removed = initialShieldInk - Read<List<Panel>>(_boss, "_panels").Where(IsInstanceValid).Sum(panel => Math.Max(0, panel.Ink)),
            verified_wing_switches = verifiedWingSwitches.Count,
            pursuit_barrier_checks = barrierChecks, pursuit_through_barrier_frames = throughBarrierFrames,
            pursuit_direction_error = pursuitDirectionError,
            invincible = false, shooting = rallyAttack && !OS.GetCmdlineUserArgs().Contains("--rally-no-return")
                || replyAttack || peelFire || showcaseFire, bombs = false, dodge = false, player_job = _game.SelectedJob.ToString(),
            peeled_editions = peelAttack ? rally!.PeeledEditions.ToArray() : null,
            peel_shooting_outcome_verified = peelPlaythrough ? peelChoiceVerified : (bool?)null,
            peel_review_failed = peelAttack && rally!.PeelReviewFailed,
            proximity_addresses = addressAttack ? addresses!.ProximityAddresses : (int?)null,
            address_deadline_missed = addressAttack && addresses!.AddressDeadlineMissed,
            address_shots = addressAttack ? addresses!.AddressShotsFired : (int?)null,
            reply_requests = replyAttack ? replies!.StarReplies.Count : 0,
            reply_responses = replyAttack ? replies!.StarReplies.Count(reply => reply.Fired) : 0,
            reply_failure = replyAttack && replies!.ReplyFailed,
            reply_overflow_frames = overflowFrames,
            rally_failures = rallyAttack ? rally!.RallyFailures : (int?)null,
            rally_returns = rallyAttack ? rally!.RallyBalls.Select(ball => ball.Returns).ToArray() : null,
            rally_damage_events = rallyAttack ? rally!.RallyDamageEvents : 0,
            rally_wall_broken = rallyAttack && rally!.RallyWallBroken,
            rally_streak = rallyAttack ? rally!.RallyStreak : 0,
            panels_invulnerable = !showcaseFire && _easyAttack is not ReiBossTechnique { SourceCutTechnique: true },
            boss_technique_practice = _boss.TechniquePractice,
            source_cut_miss = _sourceCutMiss,
            crop_gate_checks = cropGateChecks, crop_exit_crossed = cropPassed, crop_retake_frames = cropRetakeFrames,
            panel_rewards = (_easyAttack as ReiBossTechnique)?.SourceCutRounds.Sum(round => round.RemovedPanels) ?? 0,
            movement_speed = _player.MoveSpeed, maximum_step = maximumStep, physics_hz = 60,
            human_comprehension_test = false, collision_hazards = collision,
            failure_position = passed ? null : new { x = failurePosition.X, y = failurePosition.Y },
            forecast_gap = forecastGap, nearby_shots = nearShots,
            policy = _twoBeatSteps != null ? "rehearsed normal movement toward timed waypoints; no hazard lookahead, shooting, dodge or invincibility"
                : rallyAttack ? "normal movement and charged-shot input from visible ball positions, velocities and four-edge reflection; no invincibility, dodge, teleport or future spawn data"
                : _easyAttack?.MechanicKey == "akari.returned-express" && OS.GetCmdlineUserArgs().Contains("--forecast-pilot")
                    ? "normal input from the moving lead cue with 0.2s observation delay; prediction stops at the already shown cue position, repeats reuse the same path; actual damage enabled"
                : OS.GetCmdlineUserArgs().Contains("--forecast-pilot") ? "normal input planning from completed visible forecasts after 0.2s, plus character-specific cue guidance; actual damage enabled" : EasyPilotPolicy() };
        _easyCases.Add(result);
        System.IO.File.WriteAllText($"{_output}/trace_{phase}_{slot.Kind}_{slot.Variant}_{start}.json", JsonSerializer.Serialize(trace));
        System.IO.File.WriteAllText(_output + "/chapters.json", JsonSerializer.Serialize(_easyCases, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("[SURVIVAL] " + JsonSerializer.Serialize(result));
        _easyAttack = null;
        Write(_player, "_fireCooldown", 99999f);
        _pool.DespawnPlayerBullets();
        await EasyPhysicsFrames(48);
    }

    private string EasyPilotPolicy() => UsesAkariBandPilot
        ? "normal horizontal input at the slowest walking speed, after viewing the completed attack bands for 0.25s; no future attack state"
        : OS.GetCmdlineUserArgs().Contains("--cue-pilot") && _easyAttack?.MechanicKey == "akari.filing-piles"
        ? "ordinary movement at the slowest walking speed; wait for the currently drawn sheets in the preceding column to pass before crossing; no future attack state; not a human comprehension test"
        : OS.GetCmdlineUserArgs().Contains("--cue-pilot")
        ? "ordinary movement using displayed hands, warning curves, light cones, locked eye discs, growing branches and compression nodes; pursuit routes around currently visible barriers at the slowest normal speed; no future attack state; not a human comprehension test"
        : OS.GetCmdlineUserArgs().Contains("--crop-wrong-edge")
        ? "negative comparator: normal movement through a numbered edge other than the last closing edge, then waiting outside"
        : _easyAttack switch {
        AkariBossTechnique { MechanicKey: "akari.filing-piles" } => "normal directional input at the slowest walking speed, planned from announced falling columns and collapse timing; geometric reachability test, not a human comprehension test",
        AkariBossTechnique { MechanicKey: "akari.proximity-addressing" } => "normal movement into visible address rings followed by avoidance of the locked letter trajectories; --address-skip omits deliberate approach",
        ReiBossTechnique { MechanicKey: "rei-edited-page-peeling" } => "ordinary movement aligning rightward automatic shots with visible unpeeled page editions, then avoiding their edits; --peel-no-fire omits shooting",
        AkariBossTechnique { MechanicKey: "akari.solid-umbrellas" } => "normal movement following the currently visible umbrella handle; no future trajectory access",
        AkariBossTechnique { MechanicKey: "akari.paused-outbox" } => _game.IsLunatic
            ? "normal movement planned from displayed bundle origins, pause markers and restart order after the completed cue plus 0.2s; no unshown batches; not a human comprehension test"
            : "normal movement through the two displayed pause markers after the document bundles stop",
        AkariBossTechnique { MechanicKey: "akari.exchanged-priority" } => _game.IsLunatic
            ? "normal movement planned from currently displayed crossing mail lanes and speed exchanges after the completed cue plus 0.2s; no unshown batches; not a human comprehension test"
            : "normal movement from the first unoccupied quarter to the cleared side after the accelerated vertical bundle passes",
        MinaBossTechnique { MechanicKey: "mina.second-visit-weight" } => "normal movement into unstamped cells, using the visible memory marks and current hazards with body-safe routing",
        MinaBossTechnique { MechanicKey: "mina.boundary-wrap" } => "normal movement projected from currently visible memory bands and the opposite-edge wrapping rule; geometric pilot, not a human comprehension test",
        MinaBossTechnique { MechanicKey: "mina.sliding-cache" } => "normal movement around the visibly compressed sliding memory into its vacated cell, using only the current transfer cue after 0.2s",
        MinaBossTechnique { MechanicKey: "mina.delayed-spring" } => "normal movement from the approaching visible compression wave into the relaxed half of the screen",
        MinaBossTechnique { MechanicKey: "mina.suspended-burden" } => "normal movement below the visibly lifted pendulum weight, switching sides before it returns and leaving currently announced falling lanes",
        MinaBossTechnique { MechanicKey: "mina.snapshot-exchange" } => "normal movement planned from visible bands, their directions and the announced snapshot exchange; geometric pilot, not a comprehension test",
        MinaBossTechnique { MechanicKey: "mina.wing-displacement" } => "ordinary directional input countersteering the visible wing current and moving outside the currently announced sweep; displacement includes the actual wind force",
        MinaBossTechnique { MechanicKey: "mina.reply-driven-stars" } => "normal shot inputs while the reply indicator is open, with normal movement outside the locked return-star paths",
        KoharuBossTechnique { SequentialGaze: true } => "normal movement between adjacent panels using only the currently visible eye states, with 0.2 second reaction delay",
        KoharuBossTechnique { MechanicKey: "koharu.reading-head" } => "normal movement perpendicular to the visible reading-head warning or live head, with body-safe routing",
        KoharuBossTechnique { MechanicKey: "koharu.attention-charge" } => "normal movement following the visible moving afterimage after the eye locks",
        KoharuBossTechnique { MechanicKey: "koharu.inward-coil" } => "normal movement aligned with the visible coil gap, exiting after the outer light clears and avoiding the announced central burst",
        KoharuBossTechnique { MechanicKey: "koharu.clap-annihilation" } => "normal movement from the incoming row into a cleared row after the opposing lights annihilate; current forecasts and body-safe routing",
        KoharuBossTechnique { MechanicKey: "koharu.unbalanced-support" } => "normal movement following the visible moving fulcrum to keep the beam balanced, with vertical clearance from the rolling lights",
        KoharuBossTechnique { MechanicKey: "koharu.hinged-antlers" } => "normal movement around the visible antler tips toward the opening, with body-safe routing",
        KoharuBossTechnique { MechanicKey: "koharu.parabolic-exchange" } => "normal movement outside the displayed exchange curves, starting 1.4s after the cue appears",
        ReiBossTechnique { SourceCutTechnique: true } => "normal input toward a computed point on the source-to-boss line while tracking, then forecast avoidance after lock",
        ReiBossTechnique { MechanicKey: "rei-crop-three-edges" } => "normal movement through the edge bearing the last closing number, first approaching its inner side; body-safe routing",
        ReiBossTechnique { MechanicKey: "rei-opaque-quotation-layers" } => "normal movement within the current foreground sheets while avoiding visible ink and the boss body; no future paper positions",
        ReiBossTechnique { MechanicKey: "rei-insert-displaces-suffix" } => "normal movement to the prefix side of the visible insertion marker, routing around the boss body",
        ReiBossTechnique { MechanicKey: "rei-seam-cross-before" } => "normal movement through the announced seam before it closes and cuts away the starting side",
        KoharuBossTechnique => "normal input using completed curved-path forecasts and live hazards",
        MinaBossTechnique { MechanicKey: "mina.acknowledged-rows" } => OS.GetCmdlineUserArgs().Contains("--draft-reply")
            ? "normal input toward an offered envelope to add a safe row, then staying in a marked row"
            : "normal input into the nearest visibly checked row, routing around the boss after a reading delay",
        MinaBossTechnique { TechniqueVariant: 2 } => "normal input through announced airlock doors using body-safe lanes inside the actual protected room",
        MinaBossTechnique { TechniqueVariant: 3 } => "normal input to a nearby unrecorded cell after the next generation forecast completes, waiting for the previous live cells to clear",
        ReiBossTechnique { TechniqueKind: BossTechniqueKind.Finale } => "normal input guidance through the announced sentence margin",
        ReiBossTechnique { TechniqueKind: BossTechniqueKind.Area, TechniqueVariant: 3 } => "vertical movement into the clear strip between the visible stamp paths after a 0.25s reaction",
        _ => "reactive bullet and live-hazard avoidance",
    };

    private bool EasyBodyAt(Vector2 point)
    {
        if (!_boss.BodyContactDamaging || _boss.CollisionLayer == 0 || !_boss.Monitorable) return false;
        foreach (uint owner in _boss.GetShapeOwners())
        {
            if (_boss.IsShapeOwnerDisabled(owner)) continue;
            var transform = _boss.GlobalTransform * _boss.ShapeOwnerGetTransform(owner);
            for (int i = 0; i < _boss.ShapeOwnerGetShapeCount(owner); i++)
                if (_boss.ShapeOwnerGetShape(owner, i).Collide(transform, EasyPlayerCore, new Transform2D(0, point))) return true;
        }
        return false;
    }

    private bool EasyBodyAlong(Vector2 a, Vector2 b)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b)));
        for (int i = 0; i <= steps; i++) if (EasyBodyAt(a.Lerp(b, i / (float)steps))) return true;
        return false;
    }

    private Vector2[]? EasyBodyRoute(Vector2 target, Rect2 allowed)
    {
        Vector2 position = _player.GlobalPosition;
        var inside = allowed.Intersection(Field.Rect).Grow(-Player.HitRadius - .75f);
        if ((OS.GetCmdlineUserArgs().Contains("--cue-pilot") || _easyAttack?.MechanicKey is "mina.acknowledged-rows" or "rei-crop-three-edges") && !inside.HasPoint(position)
            && Field.Rect.Grow(.01f).HasPoint(position))
        {
            Vector2 entry = position.Clamp(inside.Position + Vector2.One * .01f, inside.End - Vector2.One * .01f);
            if (!EasyBodyAlong(position, entry)) return new[] { position, entry };
        }
        if (!inside.HasPoint(position)) return null;
        target = target.Clamp(inside.Position + Vector2.One * .01f, inside.End - Vector2.One * .01f);
        if (!_boss.BodyContactDamaging) return new[] { position, target };
        var candidates = new List<Vector2[]> {
            new[] { position, target },
            new[] { position, new Vector2(position.X, target.Y), target },
            new[] { position, new Vector2(target.X, position.Y), target },
        };
        Rect2 body = default;
        bool hasBody = false;
        foreach (uint owner in _boss.GetShapeOwners())
        {
            if (_boss.IsShapeOwnerDisabled(owner)) continue;
            var transform = _boss.GlobalTransform * _boss.ShapeOwnerGetTransform(owner);
            for (int shape = 0; shape < _boss.ShapeOwnerGetShapeCount(owner); shape++)
            {
                var bounds = transform * _boss.ShapeOwnerGetShape(owner, shape).GetRect();
                body = hasBody ? body.Merge(bounds) : bounds;
                hasBody = true;
            }
        }
        if (hasBody)
        {
            body = body.Grow(Player.HitRadius + 1);
            foreach (float x in new[] { body.Position.X, body.End.X })
                candidates.Add(new[] { position, new Vector2(x, position.Y), new Vector2(x, target.Y), target });
            foreach (float y in new[] { body.Position.Y, body.End.Y })
                candidates.Add(new[] { position, new Vector2(position.X, y), new Vector2(target.X, y), target });
        }
        Vector2[]? chosen = null;
        float best = float.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            float length = 0;
            for (int segment = 1; segment < candidate.Length; segment++) length += candidate[segment - 1].DistanceTo(candidate[segment]);
            if (length >= best || candidate.Any(point => !inside.HasPoint(point))) continue;
            if (Enumerable.Range(1, candidate.Length - 1).Any(index => EasyBodyAlong(candidate[index - 1], candidate[index]))) continue;
            best = length;
            chosen = candidate;
        }
        return chosen;
    }

    private void EasyFollowRoute(Vector2[]? route, double delta)
    {
        Vector2 position = _player.GlobalPosition;
        Vector2 target = route?.Skip(1).FirstOrDefault(point => point.DistanceTo(position) > .05f, position) ?? position;
        Vector2 move = (target - position).LimitLength(_player.SlowestMoveSpeed * (float)delta);
        _pilot.Release();
        AutoplayInput.Move(move / (_player.MoveSpeed * (float)delta));
    }

    private void EasyGuideTo(Vector2 target, double delta)
        => EasyFollowRoute(EasyBodyRoute(target, Field.Rect), delta);

    private void EasyGuideInside(Rect2 destination, Rect2 allowed, double delta)
    {
        var inside = destination.Intersection(allowed).Grow(-Player.HitRadius - .75f);
        Vector2 position = _player.GlobalPosition;
        if (inside.HasPoint(position) && !EasyBodyAt(position)) { EasyFollowRoute(null, delta); return; }
        var targets = new List<Vector2> { position.Clamp(inside.Position + Vector2.One * .01f, inside.End - Vector2.One * .01f), inside.GetCenter() };
        foreach (float y in new[] { inside.Position.Y + .01f, inside.GetCenter().Y, inside.End.Y - .01f })
            foreach (float x in new[] { inside.Position.X + .01f, inside.GetCenter().X, inside.End.X - .01f }) targets.Add(new(x, y));
        foreach (var target in targets.OrderBy(point => point.DistanceSquaredTo(position)))
        {
            var route = EasyBodyRoute(target, allowed);
            if (route == null) continue;
            EasyFollowRoute(route, delta);
            return;
        }
        EasyFollowRoute(null, delta);
    }

    private ulong _easyGenerationAttack;
    private int _easyGenerationRound = -1;
    private Vector2? _easyGenerationTarget;

    private void EasyGuideGeneration(BossCharacterTechnique attack, double delta)
    {
        var cues = Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>()
            .Where(c => Read<int>(c, "Mode") == 3 && Read<double>(c, "StartsAt") <= attack.Elapsed).ToArray();
        if (cues.Length == 0) { EasyFollowRoute(null, delta); return; }
        int round = cues.Max(c => Read<int>(c, "Round"));
        if (_easyGenerationAttack != attack.GetInstanceId() || _easyGenerationRound != round)
        {
            _easyGenerationAttack = attack.GetInstanceId();
            _easyGenerationRound = round;
            _easyGenerationTarget = null;
        }
        var latest = cues.Where(c => Read<int>(c, "Round") == round).ToArray();
        if (_survival && attack.Elapsed < latest.Min(c => Read<double>(c, "StartsAt")) + 1.4)
        { EasyFollowRoute(null, delta); return; }
        var keys = latest.Select(c => Read<string>(c, "Key")).ToHashSet();
        if (attack.EasyCues.Where(c => keys.Contains(c.Key)).Any(c => c.Progress < 1))
        { EasyFollowRoute(null, delta); return; }
        var future = latest.SelectMany(c => Read<Rect2[]>(c, "Bounds"))
            .Select(bounds => bounds.Grow(Player.HitRadius + .75f)).ToArray();
        bool Safe(Vector2 point) => !future.Any(bounds => bounds.HasPoint(point));
        bool Clear(Vector2[] route)
        {
            for (int segment = 1; segment < route.Length; segment++)
            {
                Vector2 from = route[segment - 1], to = route[segment];
                int steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to)));
                for (int sample = 0; sample <= steps; sample++)
                    if (attack.DangerDistance(from.Lerp(to, sample / (float)steps), false) < .75f) return false;
            }
            return true;
        }
        double fire = latest.Min(c => Read<double>(c, "FiresAt"));
        Vector2 position = _player.GlobalPosition;
        var targets = _easyGenerationTarget is Vector2 held ? new[] { held }
            : Enumerable.Range(0, 35).Select(index => new Vector2(
                Field.Left + (index % 7 + .5f) * Field.Width / 7,
                Field.Top + (index / 7 + .5f) * Field.Height / 5))
                .OrderBy(point => point.DistanceSquaredTo(position)).ToArray();
        foreach (var target in targets)
        {
            if (!Safe(target)) continue;
            if (position.DistanceTo(target) <= .1f) { EasyFollowRoute(null, delta); return; }
            var route = EasyBodyRoute(target, Field.Rect);
            if (route == null || !Clear(route)) continue;
            float distance = 0;
            for (int segment = 1; segment < route.Length; segment++) distance += route[segment - 1].DistanceTo(route[segment]);
            if (attack.Elapsed + distance / _player.SlowestMoveSpeed + 2 * delta >= fire) continue;
            _easyGenerationTarget = target;
            EasyFollowRoute(route, delta);
            return;
        }
        EasyFollowRoute(null, delta);
    }

    private bool TryEasyGuidance(double delta)
    {
        var attack = _easyAttack;
        if (attack is ReiBossTechnique { MechanicKey: "rei-finite-cut-endpoint" } cutting)
        {
            var cuts = cutting.VisibleSweepingCuts.ToArray();
            if (cuts.Length == 0) { EasyFollowRoute(null, delta); return true; }
            var position = _player.GlobalPosition;
            bool Clears(Vector2 point, (Vector2 From, Vector2 Axis, float Reach) edge)
            {
                float current = (position - edge.From).Dot(edge.Axis), goal = (point - edge.From).Dot(edge.Axis);
                float reach = edge.Reach + _player.CurrentHitRadius + 3;
                return Math.Abs(goal) >= reach && (Math.Abs(current) < reach || current * goal > 0);
            }
            var targets = Enumerable.Range(0, Mathf.CeilToInt(Field.Width / 6)).SelectMany(x =>
                Enumerable.Range(0, Mathf.CeilToInt(Field.Height / 6)).Select(y => new Vector2(Field.Left + 3 + x * 6, Field.Top + 3 + y * 6)))
                .Where(point => Field.Rect.Grow(-3).HasPoint(point) && Clears(point, cuts[0]))
                .OrderByDescending(point => cuts.All(edge => Clears(point, edge)))
                .ThenBy(point => point.DistanceSquaredTo(position));
            foreach (var target in targets)
            {
                var route = EasyBodyRoute(target, Field.Rect);
                if (route == null) continue;
                EasyFollowRoute(route, delta);
                return true;
            }
            EasyFollowRoute(null, delta);
            return true;
        }
        if (attack is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Area, TechniqueVariant: 3 } colorCall)
        {
            if (colorCall.ColorCallCue is { } cue && attack.Elapsed >= cue.ReadyAt + .25)
            {
                var at = _player.GlobalPosition;
                float y = Mathf.Clamp(at.Y, cue.Gap.Position.Y + Player.HitRadius + 2, cue.Gap.End.Y - Player.HitRadius - 2);
                if (cue.Index > 0 && attack.Elapsed < cue.FiresAt && Math.Abs(y - at.Y) > .2f)
                {
                    float gutter = colorCall.NearestStampGutter(at.X);
                    EasyFollowRoute(new[] { at, new Vector2(gutter, at.Y), new Vector2(gutter, y) }, delta);
                }
                else EasyFollowRoute(new[] { at, new Vector2(at.X, y) }, delta);
            }
            else EasyFollowRoute(null, delta);
            return true;
        }
        if (attack is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Normal, TechniqueVariant: 0 } && attack.Elapsed < 2)
        {
            float x = _easyStart.X < Field.CenterX ? 1 : -1;
            float y = _easyStart.Y < Field.CenterY ? 1 : -1;
            Vector2 target = _easyStart + new Vector2(x * 90, y * (attack.Elapsed < 1 ? 25 : 100));
            EasyGuideTo(target.Clamp(Field.Rect.Position + Vector2.One * 10, Field.Rect.End - Vector2.One * 10), delta);
            return true;
        }
        if (attack is MinaBossTechnique && attack.TechniqueVariant == 3)
        {
            EasyGuideGeneration(attack, delta);
            return true;
        }
        if (attack is MinaBossTechnique && attack.TechniqueVariant == 2)
        {
            if (_survival && attack.Elapsed < 1.4) { EasyFollowRoute(null, delta); return true; }
            var room = Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>().FirstOrDefault(c => Read<int>(c, "Mode") == 2);
            if (room == null) return false;
            var method = attack.GetType().GetMethod("EasyAirlockDoors", BindingFlags.Static | BindingFlags.NonPublic)!;
            var doors = ((Rect2 Entry, Rect2 Exit, double OpensAt, double ClosesAt)[])method.Invoke(null, new[] { room })!;
            var current = doors.FirstOrDefault(d => attack.Elapsed < d.ClosesAt + .4);
            var protectedMethod = attack.GetType().GetMethod("EasyRoomProtectedBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
            var allowed = (Rect2)protectedMethod.Invoke(null, new object[] { room, attack.Elapsed - Read<double>(room, "FiresAt") })!;
            var destination = current.Entry.Size == Vector2.Zero ? doors[^1].Exit
                : attack.Elapsed < current.OpensAt ? current.Entry : current.Exit;
            if (_game.Difficulty == GameManager.Diff.Hard)
                EasyFollowRoute(EasyBodyRoute(destination.GetCenter(), allowed), delta);
            else EasyGuideInside(destination, allowed, delta);
            return true;
        }
        if (attack is MinaBossTechnique { MechanicKey: "mina.acknowledged-rows" })
        {
            if (OS.GetCmdlineUserArgs().Contains("--draft-skip"))
            { EasyFollowRoute(null, delta); return true; }
            var cues = Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>()
                .Where(c => Read<int>(c, "Mode") == 1 && attack.Elapsed >= Read<double>(c, "StartsAt")
                    && attack.Elapsed < Read<double>(c, "EndsAt")).ToArray();
            if (cues.Length == 0) return false;
            if (attack.Elapsed < Read<double>(cues[0], "StartsAt") + .25)
            { EasyFollowRoute(null, delta); return true; }
            var safe = cues.Where(c => Read<bool>(c, "Cancelled"))
                .OrderBy(c => Math.Abs(Read<Rect2[]>(c, "Bounds")[0].GetCenter().Y - _player.GlobalPosition.Y)).First();
            if (OS.GetCmdlineUserArgs().Contains("--draft-reply") && cues.Count(c => Read<bool>(c, "Cancelled")) == 1
                && attack.Elapsed < Read<double>(safe, "FiresAt"))
            {
                var offer = cues.Where(c => !Read<bool>(c, "Cancelled") && Read<bool>(c, "AcceptsReply"))
                    .Select(c => EasyBodyRoute(Read<Vector2>(c, "Card"), Field.Rect)).Where(route => route != null)
                    .MinBy(route => Enumerable.Range(1, route!.Length - 1).Sum(i => route[i - 1].DistanceTo(route[i])));
                if (offer != null) { EasyFollowRoute(offer, delta); return true; }
            }
            var row = Read<Rect2[]>(safe, "Bounds")[0];
            if (_game.Difficulty == GameManager.Diff.Hard)
            {
                row.Position = new Vector2(row.Position.X, row.GetCenter().Y - 5);
                row.Size = new Vector2(row.Size.X, 10);
            }
            EasyGuideInside(row, Field.Rect, delta);
            return true;
        }
        if (attack is ReiBossTechnique && attack.TechniqueKind == BossTechniqueKind.Finale)
        {
            var method = attack.GetType().GetMethod("EasySentenceGeometry", Private)!;
            var g = ((float Head, float Center, Rect2 Upper, Rect2 Lower, Rect2 Tail))method.Invoke(attack,
                new object[] { attack.Elapsed + .25 })!;
            Vector2 target = new(g.Head, g.Center);
            foreach (float offset in new[] { 0f, 24, -24, 28, -28 })
            {
                var candidate = new Vector2(g.Head, g.Center + offset);
                if (EasyBodyAt(candidate)) continue;
                target = candidate;
                break;
            }
            EasyGuideTo(target, delta);
            return true;
        }
        return false;
    }

    private ulong _gazePilotAttack;
    private Vector2 _gazePilotTarget;
    private ulong _visitPilotAttack;
    private Vector2? _visitPilotTarget;
    private ulong _cropPilotAttack;
    private bool _cropPilotLeaving;
    private ulong _prayerPilotAttack;
    private Vector2? _prayerPilotGoal;
    private ulong _burdenPilotAttack;
    private float _burdenPilotTargetX;

    private void DriveVisitMarks(MinaBossTechnique memory, double delta)
    {
        if (_visitPilotAttack != memory.GetInstanceId())
        {
            _visitPilotAttack = memory.GetInstanceId();
            _visitPilotTarget = null;
        }
        var marks = memory.VisibleVisitMarks.ToArray();
        if (marks.All(mark => mark.Visits == 0)) { EasyFollowRoute(null, delta); return; }
        if (_visitPilotTarget is { } held && _player.GlobalPosition.DistanceTo(held) < 2) _visitPilotTarget = null;
        if (_game.IsLunatic)
        {
            var echoes = memory.VisibleVisitEchoes.Where(echo => memory.Elapsed >= echo.StartsAt + .2).ToArray();
            foreach (var mark in marks.Where(mark => mark.Visits < 2)
                .Where(mark => mark.Visits == 0 || _visitPilotTarget == mark.Bounds.GetCenter()
                    || !mark.Bounds.HasPoint(_player.GlobalPosition))
                .Where(mark => !echoes.Any(echo => echo.Bounds.Grow(Player.HitRadius + 1).HasPoint(mark.Bounds.GetCenter())))
                .OrderBy(mark => _visitPilotTarget == mark.Bounds.GetCenter() ? 0 : 1)
                .ThenBy(mark => mark.Visits)
                .ThenBy(mark => mark.Bounds.GetCenter().DistanceSquaredTo(_player.GlobalPosition)))
            {
                var route = EasyBodyRoute(mark.Bounds.GetCenter(), Field.Rect);
                if (route == null) continue;
                float distance = 0;
                bool clear = true;
                for (int segment = 1; segment < route.Length && clear; segment++)
                {
                    float length = route[segment - 1].DistanceTo(route[segment]);
                    for (int sample = 0; sample <= 32; sample++)
                    {
                        float fraction = sample / 32f;
                        Vector2 point = route[segment - 1].Lerp(route[segment], fraction);
                        double at = memory.Elapsed + (distance + length * fraction) / _player.SlowestMoveSpeed;
                        if (marks.Any(old => old.Visits >= 2 && old.Bounds.Grow(Player.HitRadius + .5f).HasPoint(point)
                            && (!old.Bounds.Grow(Player.HitRadius + .5f).HasPoint(_player.GlobalPosition)
                                || memory.PracticeHazards.Any(hazard => hazard.Live && hazard.Shape == 2
                                    && hazard.A == old.Bounds.Position && hazard.B == old.Bounds.End)))
                            || echoes.Any(echo => at >= echo.FiresAt - .1 && at < echo.EndsAt + .1
                                && echo.Bounds.Grow(Player.HitRadius + .5f).HasPoint(point)))
                        { clear = false; break; }
                    }
                    distance += length;
                }
                if (!clear) continue;
                _visitPilotTarget = mark.Bounds.GetCenter();
                EasyFollowRoute(route, delta);
                return;
            }
            _visitPilotTarget = null;
            EasyFollowRoute(null, delta);
            return;
        }
        if (_visitPilotTarget == null)
        {
            foreach (var mark in marks.Where(mark => mark.Visits == 0)
                .OrderBy(mark => mark.Bounds.GetCenter().DistanceSquaredTo(_player.GlobalPosition)))
            {
                var route = EasyBodyRoute(mark.Bounds.GetCenter(), Field.Rect);
                if (route == null) continue;
                bool clear = Enumerable.Range(1, route.Length - 1).All(segment =>
                    Enumerable.Range(0, 33).All(sample => memory.DangerDistance(route[segment - 1].Lerp(route[segment], sample / 32f), true) > .5f));
                if (!clear) continue;
                _visitPilotTarget = mark.Bounds.GetCenter();
                break;
            }
        }
        if (_visitPilotTarget is { } target) EasyGuideTo(target, delta);
        else EasyFollowRoute(null, delta);
    }

    private void DriveGazeSteps(KoharuBossTechnique gaze, double delta)
    {
        if (_gazePilotAttack != gaze.GetInstanceId())
        {
            _gazePilotAttack = gaze.GetInstanceId();
            _gazePilotTarget = gaze.GazeEntry;
        }
        if (gaze.VisibleGazeStep is { } cue && gaze.Elapsed >= cue.OpensAt + .2)
            _gazePilotTarget = gaze.GazeCell(cue.To).GetCenter();
        EasyGuideTo(_gazePilotTarget, delta);
    }

    private void DriveReadingHead(BossCharacterTechnique attack, double delta)
    {
        Vector2 position = _player.GlobalPosition;
        var danger = attack.PracticeHazards.Where(h => h.Shape == 0
            && Math.Abs(h.A.Y - position.Y) <= h.Radius + Player.HitRadius + 4).ToArray();
        if (danger.Length == 0) { EasyFollowRoute(null, delta); return; }
        var targets = danger.SelectMany(h => new[] { h.A.Y - h.Radius - Player.HitRadius - 6,
            h.A.Y + h.Radius + Player.HitRadius + 6 }).Where(y => y > Field.Top + 3 && y < Field.Bottom - 3)
            .Select(y => new Vector2(position.X, y)).OrderBy(point => point.DistanceSquaredTo(position));
        foreach (var target in targets)
        {
            var route = EasyBodyRoute(target, Field.Rect);
            if (route == null) continue;
            EasyFollowRoute(route, delta);
            return;
        }
        EasyFollowRoute(null, delta);
    }

    private ulong _sevenPilotId;
    private int _sevenClockGap;
    private int _sevenLoopDirection;
    private Vector2 _sevenPilotGoal;
    private readonly Dictionary<(Vector2 A, Vector2 B), double> _sevenSeenCurves = new();
    private Vector2 PursuitRouteStep(KoharuBossTechnique attack, Vector2 target, Vector2? from = null)
    {
        Vector2 position = from ?? _player.GlobalPosition;
        var barriers = attack.VisiblePursuitBarriers.Select(b => b.Bounds.Grow(Player.HitRadius + 5)).ToArray();
        var points = new List<Vector2> { position, target };
        foreach (var rect in barriers)
            points.AddRange(new[] { rect.Position - Vector2.One, new Vector2(rect.End.X + 1, rect.Position.Y - 1),
                rect.End + Vector2.One, new Vector2(rect.Position.X - 1, rect.End.Y + 1) });
        bool Clear(Vector2 a, Vector2 b)
        {
            foreach (var rect in barriers)
            {
                if (rect.HasPoint(position)) continue;
                float entry = 0, exit = 1;
                for (int axis = 0; axis < 2; axis++)
                {
                    float direction = b[axis] - a[axis];
                    if (Math.Abs(direction) < .0001f)
                    {
                        if (a[axis] < rect.Position[axis] || a[axis] > rect.End[axis]) { exit = -1; break; }
                        continue;
                    }
                    float near = (rect.Position[axis] - a[axis]) / direction, far = (rect.End[axis] - a[axis]) / direction;
                    entry = Math.Max(entry, Math.Min(near, far));
                    exit = Math.Min(exit, Math.Max(near, far));
                }
                if (entry <= exit) return false;
            }
            return true;
        }
        var distance = Enumerable.Repeat(float.PositiveInfinity, points.Count).ToArray();
        var previous = Enumerable.Repeat(-1, points.Count).ToArray();
        var visited = new bool[points.Count];
        distance[0] = 0;
        for (int step = 0; step < points.Count; step++)
        {
            int at = Enumerable.Range(0, points.Count).Where(i => !visited[i]).MinBy(i => distance[i]);
            if (float.IsPositiveInfinity(distance[at]) || at == 1) break;
            visited[at] = true;
            for (int next = 1; next < points.Count; next++)
            {
                float cost = distance[at] + points[at].DistanceTo(points[next]);
                if (visited[next] || cost >= distance[next] || !Clear(points[at], points[next])) continue;
                distance[next] = cost;
                previous[next] = at;
            }
        }
        if (previous[1] < 0) return position;
        int first = 1;
        while (previous[first] > 0) first = previous[first];
        return points[first];
    }
    private bool DriveSevenTechniquePilot(double delta)
    {
        if (_easyAttack is not { } attack || (int)_game.Difficulty < 2
            && attack.MechanicKey is not ("koharu.pursuit-train" or "mina.connected-homeward-relay")) return false;
        Vector2 position = _player.GlobalPosition, center = new(Field.CenterX, Field.CenterY);
        if (_sevenPilotId != attack.GetInstanceId())
        {
            _sevenPilotId = attack.GetInstanceId();
            _sevenClockGap = -1;
            _sevenPilotGoal = position;
            _sevenLoopDirection = 0;
            _sevenSeenCurves.Clear();
        }
        switch (attack.MechanicKey)
        {
            case "akari.extending-clock":
                return false;
            case "koharu.pursuit-train":
                FollowKoharuTail((KoharuBossTechnique)attack, delta);
                return true;
            case "koharu.parabolic-exchange":
            {
                var warnings = attack.PracticeHazards.Where(h => !h.Live && h.Shape == 0).ToArray();
                foreach (var h in warnings) _sevenSeenCurves[(h.A, h.B)] = attack.Elapsed;
                foreach (var curve in _sevenSeenCurves.Where(pair => attack.Elapsed - pair.Value > 5.1).Select(pair => pair.Key).ToArray())
                    _sevenSeenCurves.Remove(curve);
                if (warnings.Length > 0 && _sevenSeenCurves.Keys.Any(h => Geometry2D.GetClosestPointToSegment(_sevenPilotGoal, h.A, h.B).DistanceTo(_sevenPilotGoal) < 15))
                {
                    var candidates = Enumerable.Range(0, 31).SelectMany(x => Enumerable.Range(0, 26)
                        .Select(y => new Vector2(Field.Left + 6 + x * 8, Field.Top + 6 + y * 8))).Where(Field.Rect.Grow(-6).HasPoint)
                        .Where(p => _sevenSeenCurves.Keys.All(h => Geometry2D.GetClosestPointToSegment(p, h.A, h.B).DistanceTo(p) > 17))
                        .OrderBy(p => p.DistanceSquaredTo(position));
                    foreach (Vector2 target in candidates)
                        if (EasyBodyRoute(target, Field.Rect) != null) { _sevenPilotGoal = target; break; }
                }
                EasyGuideTo(_sevenPilotGoal, delta);
                return true;
            }
            case "rei-branch-front-tip":
            {
                var root = attack.PracticeHazards.FirstOrDefault(h => h.Shape == 0 && Math.Abs(h.A.Y - h.B.Y) < .01f);
                if (root.A == root.B) { _pilot.Release(); return true; }
                float away = root.A.Y < Field.CenterY ? -1 : 1;
                float y = root.A.Y + away * 28;
                if (y < Field.Top + 6 || y > Field.Bottom - 6)
                    y = root.A.Y - away * (Math.Min(104 + (int)_game.Difficulty * 8,
                        away < 0 ? Field.Bottom - 8 - root.A.Y : root.A.Y - 8) + 18);
                EasyGuideTo(new Vector2(position.X, y), delta);
                return true;
            }
            case "mina.connected-homeward-relay":
                return DriveSurvivalForecast(delta);
            case "mina.delayed-spring":
            {
                FollowMinaSpring(attack, delta);
                return true;
            }
            default: return false;
        }
    }

    private void FollowKoharuTail(KoharuBossTechnique chase, double delta)
    {
        bool inside = OS.GetCmdlineUserArgs().Contains("--tail-inward");
        Vector2 center = new(Field.CenterX, Field.CenterY);
        Vector2 radii = new(Field.Width * (inside ? .31f : .45f), Field.Height * (inside ? .29f : .44f));
        Vector2 position = _player.GlobalPosition;
        Vector2 head = _boss.GlobalPosition;
        Vector2 polar = (position - center) / radii;
        if (polar.LengthSquared() < .001f) polar = Vector2.Down;
        if (_sevenLoopDirection == 0)
        {
            Vector2 tangent = Vector2.FromAngle(polar.Angle() + Mathf.Pi / 2);
            _sevenLoopDirection = tangent.Dot(head.DirectionTo(position)) < 0 ? -1 : 1;
        }
        float nextAngle = polar.Angle() + _sevenLoopDirection * (inside ? .80f : .50f);
        Vector2 next = PursuitRouteStep(chase, center + Vector2.FromAngle(nextAngle) * radii);
        Vector2 desired = position.DirectionTo(next);
        var lights = chase.OwnedShots.Where(b => !b.LeadIn && b.CollisionLayer != 0
            && b.GlobalPosition.DistanceSquaredTo(position) < 130 * 130).ToArray();
        float bestScore = float.NegativeInfinity;
        Vector2 best = Vector2.Zero;
        foreach (Vector2 direction in Enumerable.Range(0, 24).Select(i => Vector2.FromAngle(i * Mathf.Tau / 24)).Prepend(desired))
        {
            Vector2 ahead = position + direction * 16;
            if (!Field.Rect.Grow(-4).HasPoint(ahead) || chase.ResolvePlayerMotion(position, ahead).DistanceSquaredTo(ahead) > .1f) continue;
            float space = ahead.DistanceTo(head) - chase.PursuitBodyRadius;
            foreach (var light in lights)
            {
                Vector2 nearest = Geometry2D.GetClosestPointToSegment(light.GlobalPosition, position + direction * 2, ahead);
                space = Math.Min(space, nearest.DistanceTo(light.GlobalPosition) - light.CollisionRadius);
            }
            float score = direction.Dot(desired) * 16 + Math.Min(22, space) * 1.5f;
            if (score <= bestScore) continue;
            best = direction;
            bestScore = score;
        }
        _pilot.Release();
        AutoplayInput.Move(best);
    }

    private ulong _filingPilotAttack;
    private int _filingPilotStep;

    internal static Vector2 StepAwayFromUnansweredStar(MinaBossTechnique attack, Vector2 position, bool preferLeft)
    {
        var marks = attack.PracticeHazards.Where(h => h.Shape == 1 && h.Radius == attack.UnansweredRadius).ToArray();
        if (marks.All(h => position.DistanceTo(h.A) >= h.Radius + 10)) return Vector2.Zero;
        float angle = preferLeft ? Mathf.Pi : 0;
        var destinations = Enumerable.Range(0, 12)
            .Select(i => position + Vector2.FromAngle(angle + i * Mathf.Tau / 12) * (attack.UnansweredRadius + 14))
            .Where(goal => Field.Rect.Grow(-5).HasPoint(goal)).ToArray();
        if (destinations.Length == 0) return Vector2.Zero;
        foreach (Vector2 goal in destinations)
            if (marks.All(h => goal.DistanceTo(h.A) >= h.Radius + 9)) return position.DirectionTo(goal);
        return position.DirectionTo(destinations.MaxBy(goal => marks.Min(h => goal.DistanceTo(h.A))));
    }

    private void FollowMinaSpring(BossCharacterTechnique attack, double delta)
    {
        if (attack is not MinaBossTechnique spring) return;
        Vector2 at = _player.GlobalPosition;
        foreach (var wave in spring.VisibleSpringWaves.Where(wave => wave.Live))
        {
            if (wave.Front < at.X - 65 || wave.Front > at.X + 24) continue;
            bool top = wave.Top < Field.CenterY;
            EasyGuideTo(new(at.X, Field.CenterY + (top ? 22 : -22)), delta);
            return;
        }
    }

    private bool UsesAkariBandPilot => _survival && OS.GetCmdlineUserArgs().Contains("--cue-pilot")
        && (int)_game.Difficulty <= 1 && _easyAttack?.MechanicKey == "akari.filing-piles";
    private double _bandReadAt;
    private float _bandGoal;
    private Rect2[] _bandForecast = Array.Empty<Rect2>();
    private int _bandVolleys;
    private bool _bandGeometryMatches;
    private double _shortestBandHold;

    private void DriveAkariBandPilot(double delta)
    {
        var attack = _easyAttack!;
        if (attack.PracticeHazards.Any(h => h.Live))
        {
            if (_bandReadAt >= 0)
            {
                _bandVolleys++;
                _shortestBandHold = Math.Min(_shortestBandHold, attack.Elapsed - _bandReadAt);
            }
            _bandReadAt = -1;
            foreach (var h in attack.PracticeHazards.Where(h => h.Live))
            {
                float left = Math.Max(Field.Left, h.A.X - h.Radius), right = Math.Min(Field.Right, h.B.X + h.Radius);
                if (left > right) continue;
                _bandGeometryMatches &= _bandForecast.Any(b => left >= b.Position.X - .01f && right <= b.End.X + .01f
                    && (h.Shape != 2 || Math.Abs(left - b.Position.X) < .01f && Math.Abs(right - b.End.X) < .01f));
            }
        }
        else
        {
            var bands = attack.PracticeHazards.Where(h => !h.Live && h.Shape == 2).OrderBy(h => h.A.X).ToArray();
            if (_bandReadAt < 0 && bands.Length > 0 && bands.All(h => h.A.Y == Field.Top && h.B.Y == Field.Bottom))
            {
                _bandReadAt = attack.Elapsed;
                _bandForecast = bands.Select(h => new Rect2(h.A, h.B - h.A)).ToArray();
            }
            if (_bandReadAt >= 0 && attack.Elapsed >= _bandReadAt + .25)
            {
                float x = _player.GlobalPosition.X;
                var gaps = _bandForecast.Select(b => b.Position.X).Append(Field.Right)
                    .Zip(_bandForecast.Select(b => b.End.X).Prepend(Field.Left), (right, left) => (left, right));
                const float margin = Player.HitRadius + 2;
                var targets = gaps.Where(gap => gap.right - gap.left >= 2 * margin)
                    .Select(gap => gap.right - gap.left < 24 ? (gap.left + gap.right) / 2
                        : Mathf.Clamp(x, gap.left + margin, gap.right - margin));
                _bandGoal = targets.OrderBy(goal => Math.Abs(goal - x)).DefaultIfEmpty(x).First();
            }
        }
        EasyGuideTo(new Vector2(_bandGoal, _player.GlobalPosition.Y), delta);
    }

    private void DriveFilingPiles(AkariBossTechnique piles, double delta)
    {
        if (_filingPilotAttack != piles.GetInstanceId())
        { _filingPilotAttack = piles.GetInstanceId(); _filingPilotStep = 0; }
        float y = 18;
        float precedingColumn = _filingPilotStep == 0 ? Field.CenterX - Field.Width / 7 : Field.Left + Field.Width / 7;
        var sheets = piles.PracticeHazards.Where(h => h.Live && h.Shape == 2
            && Math.Abs((h.A.X + h.B.X) * .5f - precedingColumn) < .1f).ToArray();
        if (_filingPilotStep < 2 && sheets.Length > 0 && sheets.All(h => h.A.Y > y + Player.HitRadius + 6))
            _filingPilotStep++;
        Vector2 target = new(_filingPilotStep == 1 ? Field.Left + 10 : Field.CenterX, y);
        EasyGuideTo(target, delta);
    }

    private (ulong Id, int Round)? _frontPilot, _echoPilot;
    private Vector2 _frontGoal, _echoGoal;
    private void DriveEasyPilot(double delta)
    {
        if (_twoBeatSteps != null && _easyAttack != null && (_scriptedMovement || _easyAttack.MechanicKey == "koharu.braided-rest"))
        {
            var step = _twoBeatSteps.LastOrDefault(point => point.At <= _easyAttack.Elapsed, (At: 0d, Goal: _easyStart));
            var offset = step.Goal - _player.GlobalPosition;
            AutoplayInput.Move((offset / (_player.MoveSpeed * (float)delta)).LimitLength());
            return;
        }
        if (UsesAkariBandPilot) { DriveAkariBandPilot(delta); return; }
        if (_easyAttack is ReiBossTechnique { MechanicKey: "rei-merge-then-branch" } echo)
        {
            int round = Read<int>(echo, "_amplificationRound");
            if (_echoPilot != (echo.GetInstanceId(), round))
            {
                if (echo.Elapsed < Read<double>(echo, "_amplificationStart") + .65) { EasyFollowRoute(null, delta); return; }
                Vector2 origin = Read<Vector2[]>(echo, "_points")[0];
                var direction = echo.GetType().GetMethod("AmplificationDirection", Private)!;
                int count = (int)echo.GetType().GetProperty("AmplificationRays", Private)!.GetValue(echo)!;
                float radius = BulletPool.BossHitShape(Job.Magic, echo.IsEnhanced).Radius;
                float opening = _game.Difficulty switch { GameManager.Diff.Easy => 14, GameManager.Diff.Normal => 12,
                    GameManager.Diff.Hard => 10, _ => 8 };
                float range = (radius + opening / 2) / Mathf.Sin(Mathf.Pi / count) + 8;
                var candidates = Enumerable.Range(0, count).Select(i => origin + ((Vector2)direction.Invoke(echo, new object[] { i })!)
                    .Rotated(Mathf.Pi / count) * range).Where(Field.Rect.Grow(-4).HasPoint)
                    .Where(p => echo.DangerDistance(p, true) > 1 && EasyBodyRoute(p, Field.Rect) != null)
                    .OrderByDescending(p => echo.DangerDistance(p, true)).ToArray();
                if (candidates.Length == 0) { EasyFollowRoute(null, delta); return; }
                _echoPilot = (echo.GetInstanceId(), round);
                _echoGoal = candidates[0];
            }
            EasyGuideTo(_echoGoal, delta);
            return;
        }
        if (_easyAttack is ReiBossTechnique { MechanicKey: "rei-branch-front-tip" } front && _game.Difficulty != GameManager.Diff.Lunatic)
        {
            int round = Read<int>(front, "_frontRound");
            if (_frontPilot == (front.GetInstanceId(), round)) { EasyGuideTo(_frontGoal, delta); return; }
            var stems = front.PracticeHazards.Where(h => h.Shape == 0 && Math.Abs(h.A.X - h.B.X) < .01f
                && Math.Abs(h.A.Y - h.B.Y) > 12).GroupBy(h => Math.Sign(h.B.Y - h.A.Y));
            var openings = stems.SelectMany(stem => {
                var rails = stem.OrderBy(h => h.A.X).ToArray();
                return rails.Zip(rails.Skip(1), (a,b) => new Vector2((a.A.X + b.A.X) / 2, a.A.Y + stem.Key * 20));
            }).Where(p => Field.Rect.Grow(-3).HasPoint(p) && front.DangerDistance(p, true) >= .8f)
                .OrderBy(p => p.DistanceSquaredTo(_player.GlobalPosition));
            foreach (var opening in openings)
            {
                var route = EasyBodyRoute(opening, Field.Rect);
                if (route != null)
                {
                    _frontPilot = (front.GetInstanceId(), round);
                    _frontGoal = opening;
                    EasyFollowRoute(route, delta);
                    return;
                }
            }
            EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.unfolding-burden" } paper)
        {
            var sheets = Read<IEnumerable>(paper, "_easyMemoryCues").Cast<object>()
                .GroupBy(cue => Read<int>(cue, "Round")).OrderByDescending(g => g.Key).FirstOrDefault();
            if (sheets != null && paper.Elapsed > Read<double>(sheets.First(), "StartsAt") + 1.4)
            {
                var bands = sheets.Select(cue => Read<Rect2[]>(cue, "Bounds")[0]).Distinct().OrderBy(b => b.Position.Y).ToArray();
                var crease = bands.Zip(bands.Skip(1), (above, below) => (Above: above.End.Y, Below: below.Position.Y))
                    .Single(pair => pair.Below - pair.Above > 6);
                EasyGuideTo(new Vector2(_player.GlobalPosition.X, (crease.Above + crease.Below) / 2), delta);
            }
            else EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is MinaArchiveTechnique archive && _game.Difficulty == GameManager.Diff.Hard)
        {
            float y = _player.GlobalPosition.Y;
            var floors = archive.PracticeHazards.Where(h => h.Shape == 2 && h.B.Y >= y - 3 && h.A.Y <= y + 9).ToArray();
            float left = floors.Where(h => h.A.X <= Field.Left + .01f).Select(h => h.B.X).DefaultIfEmpty(Field.Left).Max();
            float right = floors.Where(h => h.B.X >= Field.Right - .01f).Select(h => h.A.X).DefaultIfEmpty(Field.Right).Min();
            EasyGuideTo(new Vector2((left + right) / 2, y), delta);
            return;
        }
        if (_survival && (_easyAttack?.MechanicKey is "akari.body-delivery-return" or "rei.body-spiral-close" or "mina.return-distance"
            || _easyAttack?.MechanicKey == "koharu.pursuit-train" && !OS.GetCmdlineUserArgs().Contains("--cue-pilot")))
        {
            DriveBodyAttackPilot(delta);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--cue-pilot") && _easyAttack is AkariBossTechnique { MechanicKey: "akari.filing-piles" } piles
            && (int)_game.Difficulty >= 2)
        {
            DriveFilingPiles(piles, delta);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--cue-pilot") && DriveSevenTechniquePilot(delta)) return;
        if (_easyAttack is ReiBossTechnique { MechanicKey: "rei-edited-page-peeling" } book)
        { FollowPrintedPage(book, delta); return; }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.return-distance" } returning)
        {
            if (returning.Elapsed < .3) { EasyFollowRoute(null, delta); return; }
            Vector2 center = _boss.GlobalPosition;
            Vector2 radial = center.DirectionTo(_player.GlobalPosition);
            EasyGuideTo(center + radial * ((returning.ReturnInner + returning.ReturnOuter) / 2), delta);
            return;
        }
        if (_easyAttack is ReiBossTechnique { MechanicKey: "rei-directed-reading-gate" } gate)
        {
            if (gate.Elapsed < .3) { EasyFollowRoute(null, delta); return; }
            float y = _player.GlobalPosition.Y;
            if (_game.Difficulty >= GameManager.Diff.Hard)
            {
                float pitch = _game.Difficulty == GameManager.Diff.Hard ? Field.Height / Mathf.Round(Field.Height / 16) : 24;
                y = Field.Top + Mathf.Clamp(Mathf.Round((y - Field.Top) / pitch), 1, Field.Height / pitch - 1) * pitch;
            }
            EasyGuideTo(new Vector2(Read<int>(gate, "_gateLeg") == 0 ? Field.Right - 6 : Field.Left + 6, y), delta);
            return;
        }
        if (_easyAttack is KoharuBossTechnique { MechanicKey: "koharu.screen-aperture" } screen && (int)_game.Difficulty >= 2)
        {
            if (_game.Difficulty == GameManager.Diff.Hard && _boss is BossKoharu) { DriveSurvivalForecast(delta); return; }
            if (screen.Elapsed < .3) { EasyFollowRoute(null, delta); return; }
            Vector2 origin = Read<Vector2>(screen, "_origin"), facing = Vector2.FromAngle(Read<float>(screen, "_angle"));
            float radius = Read<float>(screen, "_radius");
            var targets = new List<Vector2>();
            for (float y = Field.Top + 8; y <= Field.Bottom - 8; y += 8)
            for (float x = Field.Left + 8; x <= Field.Right - 8; x += 8)
            {
                Vector2 target = new(x, y), offset = target - origin;
                if (offset.Length() < radius - 8 || Math.Abs(facing.Cross(offset)) > Math.Max(0, facing.Dot(offset)) * Mathf.Tan(.52f) + 10)
                    targets.Add(target);
            }
            foreach (var target in targets.OrderBy(point => point.DistanceSquaredTo(_player.GlobalPosition)))
            {
                var route = EasyBodyRoute(target, Field.Rect);
                if (route == null) continue;
                EasyFollowRoute(route, delta);
                return;
            }
            EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.reply-driven-stars" } replies)
        {
            if (OS.GetCmdlineUserArgs().Contains("--reply-overflow"))
            {
                if (replies.StarReplies.Count < _overflowReplyLimit)
                {
                    _pilot.Update(delta, shoot: replies.ReplyReady, bombs: false, dodge: false);
                    DriveSurvivalForecast(delta);
                    return;
                }
                Write(_player, "_fireCooldown", 99999f);
                if (replies.PracticeHazards.Any(h => h.Shape == 1 && h.Radius == replies.UnansweredRadius))
                    AutoplayInput.Move(StepAwayFromUnansweredStar(replies, _player.GlobalPosition, true));
                else if (replies.StarReplies.Count > 0) DriveSurvivalForecast(delta);
                else EasyFollowRoute(null, delta);
                return;
            }
            if (replies.StarReplies.Count == 0)
            {
                Vector2 p = _player.GlobalPosition;
                Vector2 sendFrom = new(Mathf.Clamp(p.X, Field.CenterX - 44, Field.CenterX + 44),
                    Mathf.Clamp(p.Y, Field.CenterY - 28, Field.CenterY + 44));
                if (p.DistanceTo(sendFrom) > 1)
                {
                    EasyGuideTo(sendFrom, delta);
                    return;
                }
            }
            _pilot.Update(delta, shoot: replies.ReplyReady, bombs: false, dodge: false);
            DriveSurvivalForecast(delta);
            return;
        }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.snapshot-exchange" } snapshot && (int)_game.Difficulty >= 2)
        {
            if (_game.Difficulty != GameManager.Diff.Hard || snapshot.SnapshotDraining) { DriveSurvivalForecast(delta); return; }
            float x = Field.CenterX + (_easyStart.X < Field.CenterX ? -20 : 20);
            int side = x < Field.CenterX ? 0 : 1;
            var bands = snapshot.VisibleSnapshotBands.Where(band => band.Side == side).OrderBy(band => band.Y).ToArray();
            float y = bands.Zip(bands.Skip(1), (a, b) => (a.Y + b.Y) / 2)
                .MinBy(seam => Math.Abs(seam - _player.GlobalPosition.Y));
            EasyGuideTo(new Vector2(x, y), delta);
            return;
        }
        if (_easyAttack is KoharuBossTechnique { MechanicKey: "koharu.screen-bloom" } && (int)_game.Difficulty >= 2)
        { DriveSurvivalForecast(delta); return; }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.wing-displacement" } wings)
        {
            Vector2 positionNow = _player.GlobalPosition;
            float wingX = Field.CenterX;
            float wingY = Field.Bottom - 20;
            if (_game.Difficulty == GameManager.Diff.Hard)
            {
                float pitch = 16 * new Vector2(16, 38).Length() / 38;
                wingX = Field.Left + Mathf.Round((wingX - Field.Left) / pitch) * pitch;
                wingY = 5 * pitch * 38 / 16 - 8;
            }
            if (_game.IsLunatic && wings.VisibleWingSweep is { } sweep)
                wingX += sweep.Bounds.GetCenter().X < Field.CenterX ? 26 : -26;
            var route = EasyBodyRoute(new Vector2(wingX, wingY), Field.Rect);
            Vector2 goal = route?.Skip(1).FirstOrDefault(p => p.DistanceTo(positionNow) > .05f, positionNow) ?? positionNow;
            Vector2 desired = (goal - positionNow).LimitLength(_player.SlowestMoveSpeed * (float)delta) / (float)delta;
            _pilot.Release();
            AutoplayInput.Move((desired - wings.VisibleWingWind).LimitLength(_player.SlowestMoveSpeed) / _player.MoveSpeed);
            return;
        }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.sliding-cache" } cache && (int)_game.Difficulty >= 2)
        {
            var cue = cache.VisibleCacheSlide;
            Vector2 target = cue.Age < .2 ? MinaBossTechnique.CacheCell(cue.Slide.From).GetCenter() : cue.Age < 1.3 ? cue.Slide.SideA
                : cue.Age < 3.1 ? cue.Slide.SideB : MinaBossTechnique.CacheCell(cue.Slide.To).GetCenter();
            EasyGuideTo(target, delta);
            return;
        }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.delayed-spring" } spring)
        {
            FollowMinaSpring(spring, delta);
            return;
        }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.suspended-burden" } burden)
        {
            if (_burdenPilotAttack != burden.GetInstanceId())
            {
                _burdenPilotAttack = burden.GetInstanceId();
                _burdenPilotTargetX = Field.CenterX;
            }
            if (!OS.GetCmdlineUserArgs().Contains("--burden-stay"))
            {
                var columns = burden.PracticeHazards.Where(h => h.Shape == 2)
                    .Select(h => (Left: h.A.X, Right: h.B.X)).Distinct().OrderBy(b => b.Left).ToArray();
                foreach (var (a, b) in columns.Zip(columns.Skip(1)))
                    if (b.Left - a.Right is >= 6 and < 15.2f)
                        _burdenPilotTargetX = (a.Right + b.Left) / 2;
            }
            EasyGuideTo(new Vector2(_burdenPilotTargetX, Field.Bottom - 3.5f), delta);
            return;
        }
        if (_easyAttack is ReiBossTechnique { MechanicKey: "rei-insert-displaces-suffix" } insertion && (int)_game.Difficulty >= 2)
        {
            var cue = insertion.VisibleInsertion;
            if (cue.Direction == 0) { EasyFollowRoute(null, delta); return; }
            float x = cue.X - cue.Direction * 24;
            foreach (float y in new[] { _player.GlobalPosition.Y, Field.Top + 20, Field.Bottom - 20 })
            {
                var route = EasyBodyRoute(new Vector2(x, y), Field.Rect);
                if (route == null) continue;
                EasyFollowRoute(route, delta);
                return;
            }
            EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is ReiBossTechnique { MechanicKey: "rei-seam-cross-before" } splice)
        {
            Vector2 center = splice.VisibleSpliceCenter;
            EasyGuideTo(center + splice.VisibleSpliceExit * 30, delta);
            return;
        }
        if (_easyAttack is ReiBossTechnique { MechanicKey: "rei-opaque-quotation-layers" } quotations && (int)_game.Difficulty >= 2)
        {
            if (_game.Difficulty == GameManager.Diff.Hard)
            {
                var tracks = quotations.VisibleQuotationSheets.Select((rect, index) =>
                    new Vector2(rect.GetCenter().X, rect.GetCenter().Y + 14 * Mathf.Sin((float)Math.Max(0,
                        quotations.Elapsed - Read<double>(quotations, "_warning")) * 1.1f + index * Mathf.Pi)))
                    .OrderBy(p => p.DistanceSquaredTo(_player.GlobalPosition));
                foreach (var target in tracks)
                {
                    var route = EasyBodyRoute(target, Field.Rect);
                    if (route != null) { EasyFollowRoute(route, delta); return; }
                }
            }
            var targets = quotations.VisibleQuotationSheets.SelectMany(rect => Enumerable.Range(0, 13)
                .SelectMany(x => Enumerable.Range(0, 9).Select(y => rect.Position + new Vector2(5 + x * (rect.Size.X - 10) / 12, 5 + y * (rect.Size.Y - 10) / 8))))
                .Where(p => quotations.DangerDistance(p, true) >= 7).OrderBy(p => p.DistanceSquaredTo(_player.GlobalPosition));
            foreach (var target in targets)
            {
                var route = EasyBodyRoute(target, Field.Rect);
                if (route == null) continue;
                EasyFollowRoute(route, delta);
                return;
            }
            EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is KoharuBossTechnique { MechanicKey: "koharu.hinged-antlers" } antlers && (int)_game.Difficulty >= 2)
        {
            if (antlers.Elapsed < .7) { EasyFollowRoute(null, delta); return; }
            var frame = antlers.VisibleAntlers;
            foreach (var target in new[] { 70, 90, 114 }.SelectMany(distance => new[] { 0, -38, 38, -65, 65 }
                .Select(side => frame.Center + frame.Opening * distance + frame.Opening.Orthogonal() * side))
                .Where(Field.Rect.Grow(-3).HasPoint).OrderBy(p => p.DistanceSquaredTo(_player.GlobalPosition)))
            {
                var route = EasyBodyRoute(target, Field.Rect);
                if (route == null) continue;
                EasyFollowRoute(route, delta);
                return;
            }
            EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is KoharuBossTechnique { MechanicKey: "koharu.parabolic-exchange" } prayers && (int)_game.Difficulty >= 2)
        {
            if (_prayerPilotAttack != prayers.GetInstanceId()) { _prayerPilotAttack = prayers.GetInstanceId(); _prayerPilotGoal = null; }
            if (prayers.Elapsed < 1.4) { EasyFollowRoute(null, delta); return; }
            if (_prayerPilotGoal == null)
            {
                var targets = Enumerable.Range(0, 40).SelectMany(x => Enumerable.Range(0, 27)
                    .Select(y => new Vector2(Field.Left + 4 + x * 8, Field.Top + 4 + y * 8)))
                    .Where(p => prayers.DangerDistance(p, true) > 16).OrderBy(p => p.DistanceSquaredTo(_player.GlobalPosition));
                foreach (var target in targets)
                    if (EasyBodyRoute(target, Field.Rect) != null) { _prayerPilotGoal = target; break; }
            }
            if (_prayerPilotGoal is { } goal) EasyGuideTo(goal, delta);
            else EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is KoharuBossTechnique { MechanicKey: "koharu.unbalanced-support" } support)
        {
            var rails = support.PracticeHazards.Where(h => h.Shape == 0).OrderByDescending(h => h.Radius).Take(3).ToArray();
            if (rails.Length < 3) { EasyFollowRoute(null, delta); return; }
            var shelf = rails.OrderBy(h => h.A.Y + h.B.Y).First();
            var middle = rails.MinBy(h => h.Radius);
            Vector2 pivot = support.VisibleSupportCenter, back = (shelf.A + shelf.B) / 2;
            float width = pivot.DistanceTo(back) - shelf.Radius - middle.Radius;
            EasyGuideTo(pivot + pivot.DirectionTo(back) * (middle.Radius + width / 2), delta);
            return;
        }
        if (_easyAttack is KoharuBossTechnique { MechanicKey: "koharu.clap-annihilation" } clap && (int)_game.Difficulty >= 2)
        {
            if (_game.IsLunatic || _boss is BossKoharu) { DriveSurvivalForecast(delta); return; }
            var boxes = clap.PracticeHazards.Where(h => h.Shape == 2).ToArray();
            var rows = Enumerable.Range(0, 3).Where(row => !boxes.Any(h => Math.Abs(h.A.Y - Field.Top - row * Field.Height / 3) < 1))
                .Select(row => Field.Top + (row + .5f) * Field.Height / 3).OrderBy(y => Math.Abs(y - _player.GlobalPosition.Y));
            foreach (float y in rows)
            {
                var route = EasyBodyRoute(new Vector2(_player.GlobalPosition.X < Field.CenterX ? Field.Left + 24 : Field.Right - 24, y), Field.Rect);
                if (route == null) continue;
                Vector2 next = route.Skip(1).FirstOrDefault(p => p.DistanceTo(_player.GlobalPosition) > .05f, _player.GlobalPosition);
                bool blocked = false;
                for (int step = 1; step <= 12; step++)
                {
                    float time = step * .1f;
                    Vector2 projected = _player.GlobalPosition.MoveToward(next, _player.SlowestMoveSpeed * time);
                    foreach (var box in boxes.Where(h => h.Live))
                    {
                        bool left = (box.A.X + box.B.X) * .5f < clap.VisibleClapTargetX;
                        float speed = left ? (clap.VisibleClapTargetX - Field.Left) / 3.6f : (clap.VisibleClapTargetX - Field.Right) / 3.6f;
                        Vector2 move = Vector2.Right * speed * time;
                        if (new SurvivalShape(box.A + move, box.B + move, 0, true).Gap(projected) < 3) blocked = true;
                    }
                }
                if (blocked) { EasyFollowRoute(null, delta); return; }
                EasyFollowRoute(route, delta);
                return;
            }
            EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is ReiBossTechnique { MechanicKey: "rei-crop-three-edges" } crop)
        {
            if (_cropPilotAttack != crop.GetInstanceId()) { _cropPilotAttack = crop.GetInstanceId(); _cropPilotLeaving = false; }
            if (crop.Elapsed < .6) { EasyFollowRoute(null, delta); return; }
            Vector2 entry = crop.VisibleCropEntry, exit = crop.VisibleCropExit;
            if (OS.GetCmdlineUserArgs().Contains("--crop-retake"))
            {
                var center = Read<float?>(crop, "_retakeCenter");
                Vector2 goal = entry;
                if (center.HasValue)
                {
                    if (Read<int>(crop, "_cropExit") is 0 or 2) goal.X = center.Value;
                    else goal.Y = center.Value;
                }
                EasyGuideTo(goal, delta);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--crop-offset"))
            {
                Vector2 shift = entry.DirectionTo(exit).Orthogonal() * 4;
                entry += shift;
                exit += shift;
            }
            if (OS.GetCmdlineUserArgs().Contains("--crop-wrong-edge"))
            {
                var frame = Read<Rect2>(crop, "_cropFrame");
                Vector2[] normals = { Vector2.Up, Vector2.Right, Vector2.Down, Vector2.Left };
                foreach (int edge in Enumerable.Range(0, 4).Where(edge => edge != Read<int>(crop, "_cropExit")))
                {
                    Vector2 normal = normals[edge];
                    Vector2 border = frame.GetCenter() + normal * frame.Size * .5f;
                    if (!Field.Rect.Grow(-3).HasPoint(border + normal * 16) || EasyBodyRoute(border - normal * 16, Field.Rect) == null) continue;
                    entry = border - normal * 16; exit = border + normal * 16;
                    break;
                }
            }
            if (_player.GlobalPosition.DistanceTo(entry) < 1) _cropPilotLeaving = true;
            EasyGuideTo(_cropPilotLeaving ? exit : entry, delta);
            return;
        }
        if (_easyAttack is KoharuBossTechnique { MechanicKey: "koharu.inward-coil" } coil)
        {
            var cue = coil.VisibleCoil;
            var outer = coil.PracticeHazards.FirstOrDefault(h => h.Shape == 3);
            var inner = coil.PracticeHazards.FirstOrDefault(h => h.Shape == 1);
            float radius = cue.OutsideOpen ? inner.Radius + 18 : (inner.Radius + outer.Radius) / 2;
            EasyGuideTo(cue.Center + cue.GapDirection * radius, delta);
            return;
        }
        if (_easyAttack is KoharuBossTechnique { MechanicKey: "koharu.attention-charge" } attention)
        {
            var shadow = attention.PracticeHazards.FirstOrDefault(h => h.Shape == 3);
            if (shadow.Radius > 0) EasyGuideTo(shadow.A, delta);
            else EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.boundary-wrap" } wrap)
        {
            var tiles = wrap.PracticeHazards.Where(h => h.Shape == 2).ToArray();
            if (tiles.Length >= 2)
            {
                var openings = Enumerable.Range(0, tiles.Length / 2)
                    .Select(i => (tiles[2 * i].B.Y + tiles[2 * i + 1].A.Y) * .5f)
                    .OrderBy(center => Math.Abs(center - _player.GlobalPosition.Y)).ToArray();
                var nearbyOpenings = openings.Where(center => Math.Abs(center - openings[0]) <= 4.1f).ToArray();
                float openingY = (nearbyOpenings.Min() + nearbyOpenings.Max()) / 2;
                EasyFollowRoute(new[] { _player.GlobalPosition, new Vector2(_player.GlobalPosition.X, openingY) }, delta);
            }
            else EasyFollowRoute(null, delta);
            return;
        }
        if (_easyAttack is MinaBossTechnique { MechanicKey: "mina.second-visit-weight" } memory && (int)_game.Difficulty >= 2)
        { DriveVisitMarks(memory, delta); return; }
        if (_easyAttack is AkariBossTechnique { MechanicKey: "akari.proximity-addressing" } desk
            && !OS.GetCmdlineUserArgs().Contains("--address-skip"))
        {
            var unopened = desk.UnaddressedLetters.ToArray();
            if (unopened.Length > 0)
            {
                bool clockwise = !OS.GetCmdlineUserArgs().Contains("--address-reverse");
                var origin = unopened.OrderBy(point => point.DistanceSquaredTo(_player.GlobalPosition))
                    .ThenBy(point => clockwise ? point.Y : -point.Y).First();
                Vector2 bearing = (origin - Field.Rect.GetCenter()).Normalized();
                Vector2 approach = origin + bearing.Orthogonal() * (clockwise ? 38 : -38);
                EasyGuideTo(approach, delta);
                return;
            }
        }
        if (_easyAttack is AkariBossTechnique { MechanicKey: "akari.solid-umbrellas" } umbrella && (int)_game.Difficulty >= 2)
        {
            EasyGuideTo(umbrella.VisibleUmbrellaShelters.OrderBy(point => point.DistanceSquaredTo(_player.GlobalPosition)).First(), delta);
            return;
        }
        if (_easyAttack is KoharuBossTechnique { SequentialGaze: true } gaze) { DriveGazeSteps(gaze, delta); return; }
        if (_easyAttack?.MechanicKey == "koharu.reading-head") { DriveReadingHead(_easyAttack, delta); return; }
        if (_easyAttack is ReiBossTechnique { RallyTechnique: true } rally) { DriveRallyPilot(rally, delta); return; }
        if (_easyAttack is ReiBossTechnique { SourceCutTechnique: true } sourceCut)
        {
            FollowSourceCutPrint(sourceCut, delta);
            return;
        }
        if (_survival && OS.GetCmdlineUserArgs().Contains("--forecast-pilot")
            && (DriveSurvivalGuidance(delta) || DriveSurvivalForecast(delta))) return;
        if (TryEasyGuidance(delta)) return;
        _pilot.Update(delta, shoot: false, bombs: false, dodge: false);
        var attack = _easyAttack;
        bool editorialCue = attack?.MechanicKey is "rei-finite-cut-endpoint" or "rei-merge-then-branch"
            or "rei-trail-quotation" or "rei-branch-front-tip"
            or "koharu.screen-aperture" or "koharu.unpaired-light";
        editorialCue |= attack?.MechanicKey is "akari.braided-delivery" or "akari.proximity-addressing";
        if (attack == null || !attack.IsStriking && attack is not KoharuBossTechnique && !editorialCue) return;
        Vector2 position = _player.GlobalPosition;
        var bullets = attack.OwnedShots.Where(b => (attack is not KoharuBossTechnique || attack.MechanicKey == "koharu.unpaired-light")
                && !b.LeadIn && b.CollisionLayer != 0)
            .Select(b => (At: b.GlobalPosition, b.Velocity, Radius: b.CollisionRadius
                * Math.Max(b.GlobalTransform.X.Length(), b.GlobalTransform.Y.Length()))).ToArray();
        var curves = attack is KoharuBossTechnique
            ? Read<IEnumerable>(attack, "_easyPaths").Cast<object>()
                .Where(path => Read<Vector2[]>(path, "Points").Length > 1 && attack.Elapsed >= Read<double>(path, "StartsAt") + .9)
                .Select(path => (Points: Read<Vector2[]>(path, "Points"), Fire: Read<double>(path, "FiresAt"),
                    Duration: Read<double>(path, "Duration"), Radius: Read<float>(path, "HalfWidth"))).ToArray()
            : Array.Empty<(Vector2[] Points, double Fire, double Duration, float Radius)>();
        var preferred = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        float best = float.NegativeInfinity;
        Vector2 chosen = Vector2.Zero;
        const float clearanceReward = 24;
        for (int direction = -2; direction < 16; direction++)
        {
            Vector2 move = direction == -2 ? preferred : direction == -1 ? Vector2.Zero : Vector2.FromAngle(direction * Mathf.Tau / 16);
            float minimum = float.MaxValue, total = 0;
            for (int step = 1; step <= 8; step++)
            {
                float time = step * .075f;
                Vector2 at = (position + move * _player.MoveSpeed * time).Clamp(
                    Field.Rect.Position + Vector2.One * 6, Field.Rect.End - Vector2.One * 6);
                float gap = attack.DangerDistance(at, editorialCue && attack.Elapsed >= .35);
                if (attack is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Normal, TechniqueVariant: 0 }
                    && Read<bool>(attack, "_easySelectionBuilt"))
                {
                    var allowed = Read<Rect2>(attack, "_selectionRect").Grow(-Player.HitRadius - 1);
                    gap = Math.Min(gap, Math.Min(Math.Min(at.X - allowed.Position.X, allowed.End.X - at.X),
                        Math.Min(at.Y - allowed.Position.Y, allowed.End.Y - at.Y)));
                }
                foreach (var bullet in bullets) gap = Math.Min(gap, at.DistanceTo(bullet.At + bullet.Velocity * time) - bullet.Radius - Player.HitRadius);
                foreach (var curve in curves)
                {
                    double age = attack.Elapsed + time - curve.Fire;
                    if (age < 0 || age >= curve.Duration) continue;
                    float sample = (float)(age / curve.Duration) * (curve.Points.Length - 1);
                    int index = Math.Min((int)sample, curve.Points.Length - 2);
                    Vector2 future = curve.Points[index].Lerp(curve.Points[index + 1], sample - index);
                    gap = Math.Min(gap, at.DistanceTo(future) - curve.Radius - Player.HitRadius);
                }
                if (EasyBodyAt(at)) gap = Math.Min(gap, -20);
                minimum = Math.Min(minimum, gap);
                total += Mathf.Clamp(gap, -30, clearanceReward);
            }
            float score = Mathf.Clamp(minimum, -30, clearanceReward) + total * .12f - move.DistanceTo(preferred) * .1f;
            if (score > best) { best = score; chosen = move; }
        }
        AutoplayInput.Move(chosen);
    }

    private bool _sourceCutMiss;

    private bool _rallyChargeDown;
    private (ulong Attack, int Ball, int Attempt) _rallyReleased;
    private static Vector2 RallyPilotPosition(ReiBossTechnique.RallyBall ball, float seconds)
    {
        float Reflect(float value, float low, float high)
        {
            float span = high - low;
            float phase = Mathf.PosMod(value - low, span * 2);
            return low + (phase <= span ? phase : span * 2 - phase);
        }
        Vector2 at = ball.Position + ball.Velocity * seconds;
        return new Vector2(Reflect(at.X, Field.Left + ReiBossTechnique.RallyRadius, Field.Right - ReiBossTechnique.RallyRadius),
            Reflect(at.Y, Field.Top + ReiBossTechnique.RallyRadius, Field.Bottom - ReiBossTechnique.RallyRadius));
    }

    private void DriveRallyPilot(ReiBossTechnique attack, double delta)
    {
        bool letPass = OS.GetCmdlineUserArgs().Contains("--rally-no-return");
        if (letPass)
        {
            var bands = attack.PracticeHazards.Where(h => h.Shape == 2).OrderBy(h => h.A.Y).ToArray();
            if (bands.Length == 2)
            {
                Vector2 goal = new(_player.GlobalPosition.X, (bands[0].B.Y + bands[1].A.Y) * .5f);
                EasyFollowRoute(new[] { _player.GlobalPosition, goal }, delta);
                return;
            }
        }
        var next = letPass ? null : attack.RallyBalls.Where(ball => !ball.IsDanger && ball.Revealed
            && ball.Stage == ReiBossTechnique.RallyStage.Incoming && ball.Velocity.X < 0).MinBy(ball => ball.Position.X);
        Rect2 band = attack.RallyPlayerBounds;
        Vector2 position = _player.GlobalPosition;
        Vector2 target = new(Math.Min(Field.Left + 25, band.GetCenter().X), position.Y);
        float gateX = attack.RallyReturnWindowBounds.GetCenter().X;
        if (next != null)
        {
            float arrival = Math.Max(0, (next.Position.X - gateX) / -next.Velocity.X);
            target.Y = RallyPilotPosition(next, arrival).Y;
        }
        var hazards = attack.RallyBalls.Where(ball => ball.Stage == ReiBossTechnique.RallyStage.Incoming && ball != next).ToArray();
        Vector2 desired = (target - position).LimitLength(_player.MoveSpeed * (float)delta) / (_player.MoveSpeed * (float)delta);
        Vector2 chosen = desired;
        float best = float.NegativeInfinity;
        for (int direction = -2; direction < 16; direction++)
        {
            Vector2 move = direction == -2 ? desired : direction == -1 ? Vector2.Zero : Vector2.FromAngle(direction * Mathf.Tau / 16);
            float minimum = 50;
            Vector2 future = position;
            for (int step = 1; step <= 8; step++)
            {
                float seconds = step * .1f;
                future = (direction == -2 ? position.MoveToward(target, _player.SlowestMoveSpeed * seconds)
                    : position + move * _player.SlowestMoveSpeed * seconds).Clamp(band.Position, band.End);
                if (attack.RallyReceiver.HasPoint(position))
                    minimum = Math.Min(minimum, Math.Min(future.X - attack.RallyReceiver.Position.X,
                        attack.RallyReceiver.End.X - future.X) - Player.HitRadius);
                foreach (var ball in hazards)
                    minimum = Math.Min(minimum, future.DistanceTo(RallyPilotPosition(ball, seconds)) - ReiBossTechnique.RallyRadius - Player.HitRadius);
            }
            float score = Mathf.Clamp(minimum, -30, 8) * 12 - future.DistanceTo(target) - move.DistanceTo(desired) * .2f;
            if (score > best) { best = score; chosen = move; }
        }
        AutoplayInput.Move(chosen);
        bool hold = !letPass && attack.RallyBalls.Any(ball => ball.Stage is ReiBossTechnique.RallyStage.Feint or ReiBossTechnique.RallyStage.Incoming or ReiBossTechnique.RallyStage.Returning);
        if (next != null)
        {
            var key = (attack.GetInstanceId(), next.Index, next.Attempts);
            if (_rallyReleased == key) hold = false;
            else if (_player.ChargeFull && next.ReturnEligible && next.Position.X <= gateX + 2
                && Math.Abs(next.Position.Y - position.Y) <= 12)
            {
                hold = false;
                _rallyReleased = key;
            }
        }
        if (hold != _rallyChargeDown || hold != Input.IsKeyPressed(Key.Z))
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, Pressed = hold });
            _rallyChargeDown = hold;
        }
    }

    private void FollowPrintedPage(ReiBossTechnique book, double delta)
    {
        Vector2 at = _player.GlobalPosition;
        if (book.VisibleReviewCrease is { } crease)
        {
            EasyFollowRoute(new[] { at, new Vector2(crease, at.Y) }, delta);
            return;
        }
        float step = _player.SlowestMoveSpeed * (float)delta;
        float rise = Mathf.Clamp(book.VisiblePageBinding - at.Y, -step, step);
        float reach = Mathf.Sqrt(Math.Max(0, step * step - rise * rise));
        float station = Field.Left + 24;
        float dx = Mathf.Clamp(station - at.X, -reach, reach), best = float.NegativeInfinity;
        var nearby = book.OwnedShots.Where(shot => !shot.LeadIn && shot.CollisionLayer != 0
            && at.DistanceSquaredTo(shot.GlobalPosition) < 88 * 88).ToArray();
        foreach (float move in new[] { dx, -reach, reach, 0 })
        {
            float space = 24;
            for (int sample = 1; sample <= 8; sample++)
            {
                float t = sample * .07f;
                Vector2 predicted = new(Mathf.Clamp(at.X + move * t / (float)delta, Field.Left + 5, Field.Right - 5),
                    book.VisiblePageBinding);
                foreach (var shot in nearby)
                    space = Math.Min(space,predicted.DistanceTo(shot.GlobalPosition + shot.Velocity * t) - shot.CollisionRadius);
            }
            float score = -Math.Abs(station - at.X - move * 20) * .1f - Math.Max(0, 7 - space) * 5;
            if (score > best) { best = score; dx = move; }
        }
        EasyFollowRoute(new[] { at, at + new Vector2(dx, rise) }, delta);
    }

    private Vector2 _sourcePilotGoal;
    private ReiBossTechnique? _sourcePilotAttack;
    private double _sourcePilotAt;
    private void FollowSourceCutPrint(ReiBossTechnique attack, double delta)
    {
        Vector2 at = _player.GlobalPosition, mark = attack.SourceCutAnchor;
        if (_sourcePilotAttack == attack && attack.Elapsed < _sourcePilotAt + .066)
        {
            EasyFollowRoute(new[] { at, _sourcePilotGoal }, delta);
            return;
        }
        _sourcePilotAttack = attack;
        _sourcePilotAt = attack.Elapsed;
        var tracking = attack.SourceCutRounds.LastOrDefault(round => !round.Locked);
        if (_sourceCutMiss && tracking != null)
        {
            _sourcePilotGoal = SourceCutGuidanceTargets(attack).FirstOrDefault(at);
            EasyFollowRoute(new[] { at, _sourcePilotGoal }, delta);
            return;
        }
        var sheets = attack.SourceCutRounds.Where(cut => cut.Locked && cut.EndsAt > attack.Elapsed)
            .Select(cut => (Cut: cut, Normal: cut.From.DirectionTo(cut.To).Orthogonal())).ToArray();
        var fragments = attack.OwnedShots.Where(s => at.DistanceSquaredTo(s.GlobalPosition) < 100 * 100).ToArray();
        float speed = _player.SlowestMoveSpeed;
        float Clearance(Vector2 p, double time)
        {
            float clear = 14;
            foreach (var (cut, normal) in sheets)
            {
                if (time < cut.FiresAt - .075) continue;
                float projection = (p - cut.From).Dot(normal);
                if (time < cut.EndsAt && (time < cut.FiresAt || (time - cut.FiresAt) % attack.SourceCutSpacing < attack.SourceCutLive + .09))
                    clear = Math.Min(clear, Math.Abs(projection) - attack.SourceCutWidth - _player.CurrentHitRadius);
            }
            if (time < attack.Elapsed + .7)
                foreach (var shot in fragments)
                    clear = Math.Min(clear, p.DistanceTo(shot.GlobalPosition + shot.Velocity * (float)(time - attack.Elapsed))
                        - shot.CollisionRadius - _player.CurrentHitRadius);
            return clear;
        }
        float best = float.NegativeInfinity;
        _sourcePilotGoal = at;
        void Consider(Vector2 goal)
        {
            if (!Field.Rect.Grow(-4).HasPoint(goal)) return;
            float distance = at.DistanceTo(goal);
            float cost = distance * .08f + Math.Abs(mark.DistanceTo(goal) - 28) * .08f;
            if (tracking != null)
            {
                float offset = Math.Abs((mark - tracking.From).Cross(tracking.From.DirectionTo(goal)));
                float limit = attack.SourceCutWidth + attack.SourceCutTargetRadius;
                cost += Math.Max(0, offset - limit + 2) * 4;
            }
            float route = 14;
            int steps = Math.Max(1, Mathf.CeilToInt(distance / 4));
            for (int step = 1; step <= steps; step++)
            {
                float f = step / (float)steps;
                route = Math.Min(route, Clearance(at.Lerp(goal, f), attack.Elapsed + distance * f / speed));
                if (route < -1) break;
            }
            double arrive = attack.Elapsed + distance / speed;
            float reserve = 14;
            foreach (var (cut, _) in sheets)
                if (cut.EndsAt > arrive && cut.FiresAt < arrive + (tracking == null ? 3.1 : .8))
                    reserve = Math.Min(reserve, Clearance(goal, Math.Max(arrive, cut.FiresAt + .025)));
            reserve = Math.Min(reserve, Clearance(goal, arrive + .22));
            float score = -cost - Math.Max(0, .38f - route) * 300 - Math.Max(0, .65f - reserve) * 36;
            if (score > best) { best = score; _sourcePilotGoal = goal; }
        }
        Consider(at);
        if (tracking != null)
            foreach (int d in new[] { -76, -46, -23, 0, 23, 46, 76 })
                Consider(mark + tracking.From.DirectionTo(mark) * d);
        foreach (float radius in new[] { 2f, 5, 9, 14, 20, 30, 44, 62, 88, 128, 176 })
            for (int direction = 0; direction < 32; direction++)
                Consider(at + Vector2.FromAngle(direction * Mathf.Tau / 32) * radius);
        EasyFollowRoute(new[] { at, _sourcePilotGoal }, delta);
    }

    private Vector2[] SourceCutGuidanceTargets(ReiBossTechnique attack, Vector2? visibleSource = null)
    {
        var round = attack.SourceCutRounds.FirstOrDefault(round => !round.Locked
            && attack.Elapsed >= round.StartsAt && attack.Elapsed < round.LocksAt);
        if (round == null && visibleSource == null) return Array.Empty<Vector2>();
        Vector2 source = visibleSource ?? round!.From;
        if (!_sourceCutMiss)
        {
            if (_game.Difficulty < GameManager.Diff.Hard) return new[] { round!.Target };
            Vector2 axis = source.DirectionTo(attack.SourceCutAnchor);
            var targets = new List<Vector2>();
            foreach (float sign in new[] { 1f, -1f })
                for (int distance = 72; distance >= 30; distance -= 6)
                {
                    Vector2 point = attack.SourceCutAnchor + axis * distance * sign;
                    if (!Field.Rect.Grow(-12).HasPoint(point) || EasyBodyAt(point)) continue;
                    targets.Add(point);
                    break;
                }
            return targets.ToArray();
        }
        float clearance = attack.SourceCutTargetRadius + attack.SourceCutWidth + 14;
        Vector2 position = _player.GlobalPosition;
        var misses = new List<Vector2>();
        bool Misses(Vector2 point) => Field.Rect.Grow(-6).HasPoint(point)
            && Geometry2D.GetClosestPointToSegment(attack.SourceCutAnchor, source,
                source + source.DirectionTo(point) * Field.Width * 3).DistanceTo(attack.SourceCutAnchor) >= clearance;
        if (Misses(position)) misses.Add(position);
        for (int direction = 0; direction < 16; direction++)
            for (int distance = 12; distance <= 240; distance += 12)
            {
                Vector2 point = position + Vector2.FromAngle(direction * Mathf.Tau / 16) * distance;
                if (!Misses(point)) continue;
                misses.Add(point);
                break;
            }
        EasyRequire(misses.Count > 0, "Source-cut negative control has no visibly misaligned destination.");
        return misses.ToArray();
    }
}
