using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class AkariHardRainQa : Node
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Dictionary<(Type, string), FieldInfo> Fields = new();
    private static FieldInfo FieldInfo(object target, string name)
    {
        var key = (target.GetType(), name);
        if (Fields.TryGetValue(key, out var field)) return field;
        for (Type? type = key.Item1; type != null; type = type.BaseType)
            if (type.GetField(name, Members) is { } found) return Fields[key] = found;
        throw new Exception($"Missing field {key.Item1.Name}.{name}");
    }
    private static T Read<T>(object target, string name) => (T)FieldInfo(target, name).GetValue(target)!;
    private static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name, Members)!.GetValue(target)!;
    private static void Write(object target, string name, object value) => FieldInfo(target, name).SetValue(target, value);
    private static object? Call(object target, string name, params object[] args)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetMethod(name, Members) is { } method) return method.Invoke(target, args);
        throw new Exception($"Missing method {target.GetType().Name}.{name}");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Particle
    {
        internal object Source = null!;
        internal int Wave, Row, Column, Burst, Side;
        internal Vector2 Origin;
        internal double Launch, Duration;
        internal bool Observed;
        internal Vector2 LastPosition;
        internal double LastAge;
        internal bool WasAlive;
    }

    private AkariRoot _root = null!;
    private BossAkari _boss = null!;
    private GameManager _game = null!;
    private BulletPool _pool = null!;
    private AutoplayInput _pilot = null!;
    private AkariBossTechnique? _attack;
    private readonly CircleShape2D _playerCore = new() { Radius = Player.HitRadius };
    private readonly List<object> _results = new();
    private bool _running, _moving, _targetSweep, _video, _shots;
    private int _attackContacts, _bodyContacts, _peak;
    private double _lastContactTime = -1;
    private string _output = "";

    public override async void _Ready()
    {
        int exitCode = 0;
        bool previousPause = Settings.PauseWhenUnfocused;
        try
        {
            Require(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "QA needs isolated saves under build/qa_story.");
            var args = OS.GetCmdlineUserArgs();
            _video = args.Contains("--video");
            _shots = args.Contains("--shots") && DisplayServer.GetName() != "headless";
            _output = ProjectSettings.GlobalizePath(args.FirstOrDefault(a => a.StartsWith("--output="))?[9..]
                ?? throw new ArgumentException("--output=<directory> is required."));
            System.IO.Directory.CreateDirectory(_output);
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = Enum.Parse<GameManager.Diff>(args.FirstOrDefault(a => a.StartsWith("--difficulty="))?[13..] ?? "Hard", true);
            if (!_game.JobForcedByCmdline) _game.SelectedJob = Job.Tank;
            _game.TrainingSetAllUpgrades(false);
            _game.MarkIdleDialogSeen("once_midboss_shield");
            _game.MarkIdleDialogSeen(StageTutorial.ChoiceSeenKey);
            _game.MarkIdleDialogSeen(StageTutorial.SkipKey);
            Engine.TimeScale = 1;
            Engine.PhysicsTicksPerSecond = 60;
            GD.Seed(2073);
            if (DisplayServer.GetName() != "headless")
            {
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                DisplayServer.WindowSetPosition(new Vector2I(-16000, -16000));
            }
            await Frames(2);
            _pool = GetNode<BulletPool>("/root/Pool");
            _root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
            GetTree().Root.AddChild(_root);
            GetTree().CurrentScene = _root;
            _root.SetProcess(false);
            _root.Stage.SetProcess(false);
            _root.Player.SetPhysicsProcess(false);
            _root.Hud.HoldBubble = false;
            _root.Hud.HideBubble();
            Hud.BubblePaused = false;
            _boss = new BossAkari { Position = new Vector2(Field.BossCenterX, Field.BossZoneCenterY) };
            _root.World.AddChild(_boss);
            _boss.SetProcess(false);
            _boss.SetPhysicsProcess(false);
            var posts = Read<BossPostSequence>(_boss, "_posts");
            posts.ProcessMode = ProcessModeEnum.Disabled;
            var caster = Read<AreaSpellCaster>(_boss, "_caster");
            caster.CancelPendingAttacks();
            caster.ProcessMode = ProcessModeEnum.Disabled;
            Call(_boss, "TickEntrance", 0d);
            Call(_boss, "TickEntrance", 2d);
            Call(_boss, "TickSwapAnim", 1d);
            _boss.SetPanelsInvulnerable(true);
            _game.EndFury();
            Write(_boss, "_memoryPlayed", true);
            Write(_root.Player, "_invincible", true);
            Write(_root.Player, "_invincibleTimer", 99999f);
            Write(_root.Player, "_fireCooldown", 99999f);
            for (int frame = 0; frame < 600 && _root.Player.ReturningToStart; frame++)
            {
                _root.Player._PhysicsProcess(1d / 60);
                await PhysicsFrames(1);
            }
            Require(!_root.Player.ReturningToStart, "Player return did not complete.");
            foreach (string timer in new[] { "_bannerTimer", "_cutinTimer", "_bossLineTimer" }) Write(_root.Hud, timer, 0d);
            _root.Hud.HideSpellCard();
            var background = _root.GetNode<StageBackground>("StageBackground");
            background.EnterBoss();
            background.GetNode<BgLayers>("BgLayers")._Process(2);
            posts.ResumeMusic();
            _pilot = new AutoplayInput(GetTree(), _game);
            ProcessPhysicsPriority = -100;
            GD.Print("[RAIN] CONDITIONS fps=60 time_scale=1 invincible=true bombs=false dodge=false; contact frames are counted, this is not a human difficulty or no-hit demonstration.");
            if (_video)
            {
                if (args.Contains("--evolved")) await Evolve();
                await RunRain("video", Field.PlayerStart, moving: true);
            }
            else
            {
                var onlyArgument = args.FirstOrDefault(a => a.StartsWith("--only="));
                var only = onlyArgument?[7..].Split(',').ToHashSet();
                if (only == null || only.Contains("other-difficulties")) await OtherDifficulties();
                var starts = new[] { ("center", new Vector2(Field.CenterX, Field.CenterY)),
                    ("left", new Vector2(Field.Left + 24, Field.CenterY)), ("right", new Vector2(Field.Right - 24, Field.CenterY)),
                    ("upper-left", new Vector2(Field.Left + 24, Field.Top + 24)), ("upper-right", new Vector2(Field.Right - 24, Field.Top + 24)),
                    ("lower-left", new Vector2(Field.Left + 24, Field.Bottom - 24)), ("lower-right", new Vector2(Field.Right - 24, Field.Bottom - 24)) };
                foreach (var (name, start) in starts)
                    if (only == null || only.Contains(name)) await RunRain(name, start, moving: false);
                if (only == null || only.Contains("moving")) await RunRain("moving", Field.PlayerStart, moving: true);
                if (only == null || only.Contains("cycle")) await InterruptAndSelection();
                if (only == null || only.Contains("evolved"))
                {
                    await Evolve();
                    await RunRain("evolved", Field.PlayerStart, moving: true);
                }
            }
            System.IO.File.WriteAllText(_output + "/results.json", JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[RAIN] COMPLETE cases={_results.Count}");
        }
        catch (Exception error)
        {
            exitCode = 1;
            GD.PushError($"[RAIN] FAIL {error}");
        }
        _running = false;
        _pilot?.Release();
        if (_attack is { Finished: false }) _attack.Cancel();
        _pool?.DespawnAll();
        typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, previousPause);
        foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>()) { audio.Stop(); audio.Stream = null; }
        if (IsInstanceValid(_root)) _root.QueueFree();
        _playerCore.Dispose();
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GetTree().Quit(exitCode);
    }

    private static Vector2 ExpectedPosition(Particle p, double age)
    {
        if (p.Side == 0) return p.Origin + new Vector2(0, (float)(76 * age));
        double rise = (p.Origin.X <= Field.CenterX ? p.Side > 0 : p.Side < 0) ? 144 : 68;
        return p.Origin + new Vector2((float)(p.Side * 50 * age), (float)(-rise * age + 24 * age * age));
    }
    private static Vector2 ExpectedVelocity(Particle p, double age)
    {
        if (p.Side == 0) return Vector2.Down * 76;
        double rise = (p.Origin.X <= Field.CenterX ? p.Side > 0 : p.Side < 0) ? 144 : 68;
        return new Vector2(p.Side * 50, (float)(-rise + 48 * age));
    }

    private async Task RunRain(string name, Vector2 start, bool moving, AkariBossTechnique? selected = null)
    {
        var difficulty = _game.Difficulty;
        int columns = difficulty switch { GameManager.Diff.Easy => 9, GameManager.Diff.Normal => 8, GameManager.Diff.Hard => 15, _ => 21 };
        int bursts = (difficulty switch { GameManager.Diff.Easy => 3, GameManager.Diff.Lunatic => 7, _ => 5 }) + (_boss.HasEvolved ? 2 : 0);
        double interval = difficulty switch { GameManager.Diff.Easy => .25, GameManager.Diff.Normal => .28, GameManager.Diff.Hard => .22, _ => .19 };
        double speed = difficulty == GameManager.Diff.Hard ? .85 : 1;
        double hold = difficulty == GameManager.Diff.Easy ? 2 : .6;
        double expectedAttackAt = .4 + 2 + hold;
        _pilot.Release();
        _pool.DespawnAll();
        _root.Player.GlobalPosition = start;
        _moving = moving;
        _targetSweep = name is "moving" or "cycle-retry";
        _running = true;
        _attackContacts = _bodyContacts = _peak = 0;
        _lastContactTime = -1;
        var attack = selected ?? (AkariBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Area, 0);
        _attack = attack;
        Require(attack.MechanicKey == "akari.rain-splash", "Selected attack is not Akari rain.");
        bool ended = false, completed = false;
        int totalShots = 0, remaining = -1;
        double endAt = 0;
        attack.Ended += a => { ended = true; completed = a.Completed; totalShots = a.ShotsCreated; remaining = a.NaturalEndRemainingShots; endAt = a.Elapsed; };
        var particles = new List<Particle>();
        var lockedTargets = new Dictionary<int, float>();
        var seenRows = new HashSet<(int, int)>();
        var forecastKinds = new HashSet<int>();
        var rowDurations = new double[6];
        var rowHoldFrames = new int[6];
        var rowLastForecast = new Dictionary<int, double>();
        var rowFirstLive = new Dictionary<int, double>();
        int forecastFrames = 0, overlapFrames = 0, holdFrames = 0, liveSamples = 0;
        double maxPositionError = 0, maxVelocityError = 0, firstLive = -1, lastForecast = -1, attackAt = double.PositiveInfinity;
        double maxEarlyDisappearance = 0;
        bool shotForecast = false, shotImpact = false, shotHold = false, shotOverlap = false, shotLive = false;
        long firstFrame = (long)Engine.GetProcessFrames();
        GD.Print($"[RAIN] START {name} evolved={_boss.HasEvolved} moving={moving}");
        while (!ended)
        {
            await Frames(1);
            if (ended) break;
            Require(attack.Elapsed < 120, "Rain did not end naturally within 120 seconds.");
            var sources = Read<IEnumerable>(attack, "_rainParticles").Cast<object>().ToArray();
            for (int i = particles.Count; i < sources.Length; i++)
            {
                var s = sources[i];
                particles.Add(new Particle { Source = s, Wave = Read<int>(s, "Wave"), Row = Read<int>(s, "Row"),
                    Column = Read<int>(s, "Column"), Burst = Read<int>(s, "Burst"), Side = Read<int>(s, "Side"),
                    Origin = Read<Vector2>(s, "Origin"), Launch = Read<double>(s, "Launch"), Duration = Read<double>(s, "Duration") });
                var p = particles[^1];
                int index = p.Wave * 3 + p.Row;
                rowDurations[index] = Math.Max(rowDurations[index], p.Launch + p.Duration - (p.Wave * 11.4 + p.Row * 3.6));
            }
            var reserved = Read<bool[]>(attack, "_easyRainReserved");
            var targets = Read<float[]>(attack, "_easyRainTargets");
            for (int row = 0; row < reserved.Length; row++)
            {
                if (!reserved[row]) continue;
                if (lockedTargets.TryGetValue(row, out float target)) Require(Math.Abs(target - targets[row]) < .001f, "Rain target moved after its forecast began.");
                else lockedTargets.Add(row, targets[row]);
            }
            double time = attack.Elapsed;
            attackAt = Property<double>(attack, "RainAttackAt");
            double WarningAge(Particle p) => time - .4 - (p.Wave * 11.4 + p.Row * 3.6) / speed;
            double PreviewAge(Particle p) => WarningAge(p) / 2 * rowDurations[p.Wave * 3 + p.Row]
                - (p.Launch - (p.Wave * 11.4 + p.Row * 3.6));
            var ghost = particles.Where(p => WarningAge(p) is >= 0 and < 2 && PreviewAge(p) >= 0 && PreviewAge(p) < p.Duration).ToArray();
            bool predicting = ghost.Length > 0;
            if (time < attackAt)
                Require(!attack.OwnedShots.Any() && !attack.IsStriking && !attack.CoversPoint(_root.Player.GlobalPosition), "Initial forecast acquired damage or a native projectile.");
            if (ghost.Length > 0)
            {
                forecastFrames++;
                lastForecast = time;
                foreach (var p in ghost)
                {
                    forecastKinds.Add(p.Side);
                    seenRows.Add((p.Wave, p.Row));
                    rowLastForecast[p.Wave * 3 + p.Row] = time;
                    Require(Read<object?>(p.Source, "Live") == null, "A row fired while its own preview was still moving.");
                }
                if (attack.OwnedShots.Any()) overlapFrames++;
                foreach (var p in ghost.Where((_, index) => index % 15 == 0))
                {
                    double actualAge = (double)Call(attack, "RainForecastAge", p.Source)!;
                    Require(Math.Abs(actualAge - PreviewAge(p)) < .00001, "Row preview timing differs from its two-second schedule.");
                    var actual = (Vector2)Call(attack, "RainPosition", p.Source, actualAge)!;
                    Require(actual.DistanceTo(ExpectedPosition(p, PreviewAge(p))) < .002f, "Forecast differs from independent ballistic trajectory.");
                }
            }
            for (int index = 0; index < 6; index++)
                if (time - .4 - (index / 3 * 11.4 + index % 3 * 3.6) / speed >= 2
                    && time - .4 - (index / 3 * 11.4 + index % 3 * 3.6) / speed < 2 + hold)
                { holdFrames++; rowHoldFrames[index]++; }
            if (time >= attackAt)
            {
                double modelTime = (time - attackAt) * speed;
                foreach (var p in particles)
                {
                    var letter = Read<object?>(p.Source, "Live");
                    if (letter == null) continue;
                    var shot = Read<Bullet>(letter, "Shot");
                    bool alive = shot.Active && shot.ActivationId == Read<ulong>(letter, "Activation");
                    if (alive)
                    {
                        p.Observed = true;
                        rowFirstLive.TryAdd(p.Wave * 3 + p.Row, time);
                        if (firstLive < 0)
                        {
                            firstLive = time;
                            GD.Print($"[RAIN] FOOTPRINT evolved={_boss.HasEvolved} radius={shot.Footprint!.Radius:F6}");
                        }
                        double age = modelTime - p.Launch;
                        Require(age >= -1e-6 && age < p.Duration + speed / 60 + 1e-4, "A live projectile is outside its forecast lifetime.");
                        var expected = ExpectedPosition(p, Math.Max(0, age));
                        var expectedVelocity = ExpectedVelocity(p, Math.Max(0, age)) * (float)speed;
                        maxPositionError = Math.Max(maxPositionError, shot.GlobalPosition.DistanceTo(expected));
                        maxVelocityError = Math.Max(maxVelocityError, shot.Velocity.DistanceTo(expectedVelocity));
                        Require(maxPositionError < .02 && maxVelocityError < .02, "Live rain or splash differs from the forecast trajectory / difficulty speed.");
                        Require(shot.BossPathDriven && !shot.LeadIn && shot.CollisionLayer != 0 && shot.CollisionMask != 0,
                            "Live rain lost its native collider or gained a deployment delay.");
                        Require(shot.Footprint is { Shapes.Length: > 0 } && shot.BossBarrageId == attack.GetInstanceId(), "Live rain lost its illustrated collider or owner.");
                        p.LastPosition = shot.GlobalPosition;
                        p.LastAge = age;
                        liveSamples++;
                    }
                    else if (p.WasAlive)
                    {
                        double remainingLife = p.Duration - p.LastAge;
                        maxEarlyDisappearance = Math.Max(maxEarlyDisappearance, remainingLife);
                        Require(remainingLife <= speed * 2 / 60 + .002, "Rain particle disappeared before its forecasted impact / exit.");
                    }
                    p.WasAlive = alive;
                }
            }
            if (_shots && !shotForecast && predicting && time > 1)
            { shotForecast = true; await Capture(name + "_forecast.png"); }
            if (_shots && !shotImpact && ghost.Any(p => p.Side == 0 && p.Duration - PreviewAge(p) is > 0 and < .4))
            { shotImpact = true; await Capture(name + "_impact_cue.png"); }
            if (_shots && !shotHold && time < attackAt && attackAt - time < .6)
            { shotHold = true; await Capture(name + "_launch_countdown.png"); }
            if (_shots && !shotOverlap && predicting && attack.OwnedShots.Any() && overlapFrames > 5)
            { shotOverlap = true; await Capture(name + "_forecast_overlap.png"); }
            if (_shots && !shotLive && firstLive >= 0 && time > firstLive + 7)
            { shotLive = true; await Capture(name + "_attack.png"); }
        }
        _running = false;
        _pilot.Release();
        _attack = null;
        int expectedCount = 2 * 3 * columns * bursts * 3;
        Require(completed && remaining == 0, "Rain ended with live projectiles or did not complete.");
        Require(totalShots == expectedCount && particles.Count == expectedCount && particles.All(p => p.Observed && Read<bool>(p.Source, "Fired")),
            $"Rain lost a volley: actual={totalShots}, records={particles.Count}, observed={particles.Count(p => p.Observed)}, expected={expectedCount}");
        Require(lockedTargets.Count == 6 && seenRows.Count == 6 && forecastKinds.SetEquals(new[] { -1, 0, 1 }) && overlapFrames > 0,
            "Forecast omitted a row, side, wave, or overlapping rain/splash.");
        Require(firstLive >= expectedAttackAt - .001 && firstLive <= expectedAttackAt + .04, $"First rain missed its countdown: {firstLive:F3}");
        Require(rowFirstLive.Count == 6 && rowHoldFrames.All(frames => frames >= hold * 60 - 2), "A row lost its preview hold.");
        for (int index = 0; index < 6; index++)
        {
            double expected = expectedAttackAt + (index / 3 * 11.4 + index % 3 * 3.6) / speed;
            Require(Math.Abs(rowFirstLive[index] - expected) < .04, "A row missed its forecasted launch.");
            Require(rowFirstLive[index] - rowLastForecast[index] >= hold - 1d / 60, "A row fired before its preview hold finished.");
        }
        double firstLaunch = particles.Where(p => p.Side == 0).Min(p => p.Launch);
        foreach (var p in particles)
        {
            double expectedLaunch = firstLaunch + p.Wave * 11.4 + p.Row * 3.6 + p.Burst * interval + (p.Side == 0 ? 0 : 3);
            Require(Math.Abs(p.Launch - expectedLaunch) < .00001, "Wave, row, burst, or splash cadence changed.");
            float first = Field.Left + 16 + ((p.Wave + p.Row) % 3 - 1) * 7;
            float pitch = (Field.Width - 32) / (columns - 1);
            float target = lockedTargets[p.Wave * 3 + p.Row];
            int nearest = Mathf.Clamp(Mathf.RoundToInt((target - first) / pitch), 0, columns - 1);
            float expectedX = p.Column == nearest ? target : first + p.Column * pitch;
            Require(Math.Abs(p.Origin.X - expectedX) < .001 && Math.Abs(p.Origin.Y - (p.Side == 0 ? -12 : Field.Bottom)) < .001,
                "Reserved rain column or splash impact differs from its original layout.");
        }
        if (!moving) Require(_attackContacts > 0, "A fresh stationary start escaped the complete attack.");
        if (_targetSweep) Require(lockedTargets.Values.Select(v => Math.Round(v)).Distinct().Count() >= 2, "Moving player did not exercise dynamic forecast target acquisition.");
        var result = new { name, difficulty = difficulty.ToString(), speed, hold, evolved = _boss.HasEvolved, passed = true, start = new { x = start.X, y = start.Y }, moving,
            invincible = true, bombs = false, dodge = false, normal_move_speed = _root.Player.MoveSpeed, forecast_input_sweep = _targetSweep,
            shots = totalShots, expected_shots = expectedCount, elapsed = endAt, first_frame = firstFrame,
            final_frame = Engine.GetProcessFrames(), attack_at = attackAt, first_live_at = firstLive, last_forecast_at = lastForecast,
            forecast_frames = forecastFrames, overlapping_forecast_frames = overlapFrames, hold_frames = holdFrames,
            row_first_live_at = rowFirstLive, row_hold_frames = rowHoldFrames,
            live_samples = liveSamples, maximum_position_error = maxPositionError, maximum_velocity_error = maxVelocityError,
            maximum_retirement_frame_gap = maxEarlyDisappearance, peak_shots = _peak,
            attack_contact_frames = _attackContacts, boss_body_contact_frames = _bodyContacts,
            fixed_targets = lockedTargets, natural_end_remaining_shots = remaining, human_comprehension_test = false };
        _results.Add(result);
        System.IO.File.WriteAllText(_output + "/results.json", JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"[RAIN] PASS {name} shots={totalShots} forecast_overlap_frames={overlapFrames} position_error={maxPositionError:F4} contacts={_attackContacts} body_contacts={_bodyContacts}");
        await Frames(3);
    }

    private async Task OtherDifficulties()
    {
        var previous = _game.Difficulty;
        foreach (var difficulty in new[] { GameManager.Diff.Easy, GameManager.Diff.Normal, GameManager.Diff.Lunatic })
        {
            _game.Difficulty = difficulty;
            await RunRain(difficulty.ToString(), Field.PlayerStart, moving: true);
        }
        _game.Difficulty = previous;
    }

    private async Task InterruptAndSelection()
    {
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        bool cycle = _boss.UsesTechniqueCycle;
        int Cursor() => cycle ? danmaku.CycleSlot : danmaku.EasySlot;
        string slotField = cycle ? "_cycleSlot" : "_easySlot";
        string completedField = cycle ? "_cycleCompleted" : "_easyCompleted";
        int segment = cycle ? BossAttackCycle.Segment(_boss) : _boss.HasEvolved ? 1 : 0;
        var slots = cycle ? BossAttackCycle.Slots(_boss, segment) : BossAttackCycle.EasySlots(_boss, segment);
        int rainSlot = Enumerable.Range(0, slots.Count).Single(i => slots[i].Kind == BossTechniqueKind.Area && slots[i].Variant == 0);
        Write(danmaku, cycle ? "_cycleSegment" : "_easySegment", segment);
        Write(danmaku, "_slotCount", slots.Count);
        Write(danmaku, slotField, rainSlot);
        Write(danmaku, "_timer", 0d);
        danmaku.Tick(1d / 60);
        Require(danmaku.ActiveTechnique is AkariBossTechnique { MechanicKey: "akari.rain-splash" },
            $"Ordinary cycle did not select rain: slot={Cursor()}/{rainSlot}, timer={Read<double>(danmaku, "_timer")}, "
            + $"returning={_boss.BossFrameReturning}, bubble={Hud.BubblePaused}, paused={GetTree().Paused}, "
            + $"visible={_boss.IsVisibleInTree()}, center={_boss.ShotCenter}, field={Field.Rect}, "
            + $"resolving={_boss.GaugeAttackResolving}, vulnerable={_boss.GaugeVulnerable}, reforming={_boss.GaugeReforming}, "
            + $"suppressed={Read<AreaSpellCaster>(_boss, "_caster").Suppressed}");
        var interrupted = danmaku.ActiveTechnique!;
        while (interrupted.Elapsed < 2) await Frames(1);
        interrupted.Cancel();
        Require(!interrupted.Completed && !interrupted.Visible && !interrupted.OwnedShots.Any() && Cursor() != rainSlot,
            "Forecast cancellation failed to advance or left a visible attack.");
        for (int frame = 0; frame < 120 && (danmaku.ActiveTechnique == null || danmaku.ActiveTechnique == interrupted); frame++)
        { danmaku.Tick(1d / 60); await Frames(1); }
        Require(danmaku.ActiveTechnique is AkariBossTechnique && danmaku.ActiveTechnique != interrupted && Cursor() != rainSlot,
            "Interrupted rain did not yield to a different attack.");
        danmaku.ActiveTechnique!.Cancel();
        _boss.TickBossFrame(2);
        danmaku.Tick(0);
        Write(danmaku, slotField, rainSlot);
        Write(danmaku, "_timer", 0d);
        danmaku.Tick(1d / 60);
        Require(danmaku.ActiveTechnique is AkariBossTechnique { MechanicKey: "akari.rain-splash" }, "Rain could not restart after interruption.");
        int completedBefore = Read<int>(danmaku, completedField);
        await RunRain("cycle-retry", Field.PlayerStart, moving: true, (AkariBossTechnique)danmaku.ActiveTechnique!);
        Require(Read<int>(danmaku, completedField) == completedBefore + 1 && Cursor() != rainSlot,
            "Completed rain was not recorded exactly once or selected an immediate repeat.");
        var expectedNext = slots[Cursor()];
        bool nextStarted = false;
        for (int frame = 0; frame < 600; frame++)
        {
            _boss.TickBossFrame(1d / 60);
            danmaku.Tick(1d / 60);
            if (danmaku.ActiveTechnique is { Finished: false } next)
            {
                Require(next.TechniqueKind == expectedNext.Kind && next.TechniqueVariant == expectedNext.Variant,
                    "The next ordinary attack does not match the cycle.");
                nextStarted = true;
                next.Cancel();
                break;
            }
            await Frames(1);
        }
        Require(nextStarted, "The next ordinary attack did not start after rain.");
        danmaku.Tick(0);
        var liveCancel = (AkariBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Area, 0);
        while (!liveCancel.OwnedShots.Any()) await Frames(1);
        var parent = Read<IEnumerable>(liveCancel, "_rainParticles").Cast<object>().First(p => Read<int>(p, "Side") == 0 && Read<object?>(p, "Live") != null);
        var children = Read<IEnumerable>(liveCancel, "_rainParticles").Cast<object>().Where(p => ReferenceEquals(Read<object?>(p, "Parent"), parent)).ToArray();
        Require(children.Length == 2, "A rain parent does not own exactly two splashes.");
        _pool.Despawn(Read<Bullet>(Read<object>(parent, "Live"), "Shot"));
        double suppressedAt = Property<double>(liveCancel, "RainAttackAt") + (Read<double>(parent, "Launch") + 3.2) / Property<double>(liveCancel, "RainAttackSpeed");
        while (liveCancel.Elapsed < suppressedAt) await Frames(1);
        Require(children.All(p => Read<object?>(p, "Live") == null), "A removed parent rain still emitted splashes.");
        liveCancel.Cancel();
        Require(!liveCancel.Completed && !liveCancel.Visible && !liveCancel.OwnedShots.Any(), "Live cancellation left projectiles or forecast art.");
        _results.Add(new { name = "cycle-and-interruption", passed = true, selected_slot = rainSlot, next_slot = Cursor(),
            forecast_cancel = true, live_cancel = true, cancellation_advances = true, retry_after_other_attack = true,
            removed_parent_suppresses_children = true });
        await Frames(3);
    }

    private async Task Evolve()
    {
        var previous = _game.Difficulty;
        _game.Difficulty = GameManager.Diff.Lunatic;
        Call(_boss, "AdvanceForm2");
        _game.Difficulty = previous;
        for (int frame = 0; frame < 600 && _boss.Transforming; frame++) await PhysicsFrames(1);
        Require(_boss.HasEvolved && !_boss.Transforming, "Akari transformation did not finish.");
        Call(_boss, "TickSwapAnim", 1d);
        _boss.SetPanelsInvulnerable(true);
        _game.EndFury();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_running || _attack is not { Finished: false } attack) return;
        if (_moving)
        {
            _pilot.Update(delta, shoot: false, bombs: false, dodge: false);
            if (_targetSweep && attack.Elapsed < Property<double>(attack, "RainAttackAt"))
            {
                float goalX = (int)(attack.Elapsed / 3) % 2 == 0 ? Field.Right - 32 : Field.Left + 32;
                Vector2 destination = new(goalX, Field.Bottom - 24);
                AutoplayInput.Move(((destination - _root.Player.GlobalPosition) / (float)delta).LimitLength(_root.Player.MoveSpeed) / _root.Player.MoveSpeed);
            }
            Input.FlushBufferedEvents();
            _root.Player._PhysicsProcess(delta);
            Call(_root.Player, "SetSpriteVisible", true);
        }
        if (Math.Abs(_lastContactTime - attack.Elapsed) < .000001) return;
        _lastContactTime = attack.Elapsed;
        var live = attack.OwnedShots.Where(b => !b.LeadIn && b.CollisionLayer != 0).ToArray();
        _peak = Math.Max(_peak, live.Length);
        var point = _root.Player.GlobalPosition;
        var core = new Transform2D(0, point);
        if (live.Any(b => point.DistanceTo(b.GlobalPosition) <= b.CollisionRadius + Player.HitRadius
            && b.Footprint!.Shapes.Any(s => s.Collide(b.GlobalTransform, _playerCore, core)))) _attackContacts++;
        bool body = false;
        if (_boss.BodyContactDamaging && _boss.CollisionLayer != 0)
            foreach (uint owner in _boss.GetShapeOwners())
            {
                if (_boss.IsShapeOwnerDisabled(owner)) continue;
                for (int i = 0; i < _boss.ShapeOwnerGetShapeCount(owner); i++)
                    if (_boss.ShapeOwnerGetShape(owner, i).Collide(_boss.GlobalTransform * _boss.ShapeOwnerGetTransform(owner), _playerCore, core)) body = true;
            }
        if (body) _bodyContacts++;
    }

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Require(image.SavePng(_output + "/" + name) == Error.Ok, "Screenshot could not be saved.");
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task PhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
