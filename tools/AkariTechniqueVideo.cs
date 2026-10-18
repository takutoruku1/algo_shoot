using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class AkariTechniqueVideo : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string field, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object? value, Type? type = null)
        => (type ?? obj.GetType()).GetField(field, Private)!.SetValue(obj, value);
    private static void Call(object obj, string method, Type? type = null, params object[] args)
        => (type ?? obj.GetType()).GetMethod(method, Private)!.Invoke(obj, args);
    private static T Invoke<T>(object obj, string method, params object[] args)
        => (T)obj.GetType().GetMethod(method, Private)!.Invoke(obj, args)!;
    private static T Property<T>(object obj, string name)
        => (T)obj.GetType().GetProperty(name, Private)!.GetValue(obj)!;
    private static T Constant<T>(object obj, string name)
        => (T)obj.GetType().GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;

    private AkariRoot _root = null!;
    private BossAkari _boss = null!;
    private AutoplayInput _pilot = null!;
    private BulletPool _pool = null!;
    private bool _running, _shots, _qa, _shoot, _eightTechniques, _stationaryOnly, _stationaryProbe;
    private Vector2 _stationaryOrigin;
    private float _stationaryMaxDisplacement;
    private Vector2[] _draftGuidePoints = Array.Empty<Vector2>();
    private int _draftGuideIndex;
    private BossCharacterTechnique? _observedAttack;
    private TechniqueAudit? _audit;
    private WarningPathAudit? _warningPathAudit;
    private string? _warningPathFailure;
    private WarningStageAudit? _warningStageAudit;
    private string? _warningStageFailure;
    private float _paperCorridorX;
    private float _paperCorridorY;
    private int _paperPass;
    private int _paperGapChanges;
    private float _paperGapTravel;
    private int _wavePilotIndex = -1;
    private double _waveLiveObservedAt = -1, _waveGapObservedAt = -1, _waveReturnStartedAt = -1;
    private bool _waveBlockedCorridorObserved;
    private string? _waveGeometryFailure;
    private readonly float?[] _waveArtPreviousRadius = new float?[2];
    private readonly int[] _waveArtRadiusFrames = new int[2];
    private readonly float[] _waveArtMinimumRadiusDelta = new float[2];
    private readonly List<object> _waveReturns = new();
    private string _output = "";
    private readonly List<object> _chapters = new();

    public override async void _Ready()
    {
        int exitCode = 0;
        bool? pauseWhenUnfocused = null;
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"))
                throw new Exception("An isolated QA save directory is required.");
            var args = OS.GetCmdlineUserArgs();
            _qa = args.Contains("--qa");
            _shoot = args.Contains("--shoot");
            bool waveOnly = args.Contains("--wave-only");
            if (args.Contains("--ten-techniques")) throw new ArgumentException("Use --eight-techniques for the canonical EASY attacks.");
            _eightTechniques = args.Contains("--eight-techniques");
            _stationaryOnly = args.Contains("--stationary-probes");
            bool battleCycle = args.Contains("--battle-cycle");
            if (battleCycle && (!_qa || _eightTechniques || waveOnly || _stationaryOnly))
                throw new ArgumentException("--battle-cycle requires --qa and runs separately from technique capture modes.");
            if (waveOnly && _eightTechniques) throw new ArgumentException("Choose --wave-only or --eight-techniques.");
            if (_stationaryOnly && (!_eightTechniques || !_qa))
                throw new ArgumentException("--stationary-probes requires --eight-techniques --qa.");
            int[] selectedMechanics = { 0, 1, 2, 5, 6, 7, 11, 12 };
            var subsetArgs = args.Where(arg => arg == "--only-mechanics" || arg.StartsWith("--only-mechanics=")).ToArray();
            HashSet<int>? onlyMechanics = null;
            if (subsetArgs.Length > 0)
            {
                if (!_eightTechniques || subsetArgs.Length != 1 || !subsetArgs[0].StartsWith("--only-mechanics="))
                    throw new ArgumentException("--only-mechanics=<comma-separated IDs> requires --eight-techniques and may be specified once.");
                onlyMechanics = new HashSet<int>();
                foreach (string value in subsetArgs[0]["--only-mechanics=".Length..].Split(','))
                    if (!int.TryParse(value, out int mechanic) || !selectedMechanics.Contains(mechanic) || !onlyMechanics.Add(mechanic))
                        throw new ArgumentException($"Invalid selected mechanic ID: '{value}'. Choose distinct IDs from {string.Join(",", selectedMechanics)}.");
            }
            pauseWhenUnfocused = Settings.PauseWhenUnfocused;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!
                .Invoke(null, new object[] { false });
            string output = args.FirstOrDefault(arg => arg.StartsWith("--output="))?[9..]
                ?? throw new ArgumentException("--output=<directory> is required.");
            if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Output directory is empty.");
            _output = ProjectSettings.GlobalizePath(output);
            System.IO.Directory.CreateDirectory(_output);
            bool headless = DisplayServer.GetName() == "headless";
            _shots = args.Contains("--shots") && !headless;
            if (args.Contains("--shots") && headless) GD.Print("[TECHNIQUES] PNG capture skipped on the headless renderer.");

            GetNode<QaPilot>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = args.FirstOrDefault(arg => arg.StartsWith("--difficulty=")) is { } difficulty
                ? Enum.Parse<GameManager.Diff>(difficulty[13..]) : GameManager.Diff.Easy;
            game.SelectedJob = Job.Tank;
            game.TrainingSetAllUpgrades(false);
            game.MarkIdleDialogSeen("once_midboss_shield");
            Engine.TimeScale = 1;
            Engine.PhysicsTicksPerSecond = 60;
            GD.Seed(2073);
            if (!headless)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
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
            Call(_boss, "TickEntrance", typeof(Enemy), 0d);
            Call(_boss, "TickEntrance", typeof(Enemy), 2d);
            Call(_boss, "TickSwapAnim", typeof(Enemy), 1d);
            _boss.SetPanelsInvulnerable(true);
            game.EndFury();
            Write(_root.Player, "_invincible", true);
            Write(_root.Player, "_invincibleTimer", 9999f);
            Write(_root.Player, "_fireCooldown", _shoot ? 0f : 9999f);
            foreach (string timer in new[] { "_bannerTimer", "_cutinTimer", "_bossLineTimer" })
                Write(_root.Hud, timer, 0d);
            _root.Hud.HideSpellCard();
            var background = _root.GetNode<StageBackground>("StageBackground");
            background.EnterBoss();
            background.GetNode<BgLayers>("BgLayers")._Process(2);
            posts.ResumeMusic();
            _pilot = new AutoplayInput(GetTree(), game);
            ProcessPhysicsPriority = -100;

            string scope = battleCycle ? "actual EASY Akari twelve-slot round"
                : _eightTechniques ? "canonical eight techniques" : waveOnly ? "wave only" : "selected three techniques";
            if (_stationaryOnly) scope += " stationary fresh-start probes";
            if (onlyMechanics != null) scope += $" subset=[{string.Join(",", selectedMechanics.Where(onlyMechanics.Contains))}]";
            string movement = battleCycle ? "held player in controlled runtime selection"
                : _stationaryOnly ? "zero movement input from each fresh start"
                : "reactive avoidance with recorded-draft, paper-gap, and wave-opening input guidance";
            GD.Print($"[TECHNIQUES] CONDITIONS scope={scope}, difficulty={game.Difficulty}, time scale 1, {movement}, invincible, shooting={_shoot}; collision-response verification, not visual comprehension or human difficulty.");
            await PhysicsFrames(120);
            if (battleCycle) await BattleCycle();
            else if (_eightTechniques)
            {
                var selected = new[] { (BossTechniqueKind.Normal, 0), (BossTechniqueKind.Normal, 1),
                    (BossTechniqueKind.Normal, 2), (BossTechniqueKind.Edge, 0), (BossTechniqueKind.Area, 0),
                    (BossTechniqueKind.Area, 1),
                    (BossTechniqueKind.Crossfire, 2), (BossTechniqueKind.Crossfire, 3) };
                if (_stationaryOnly)
                {
                    var probes = new[] { ("center", new Vector2(Field.CenterX, Field.CenterY + 48)),
                        ("left", new Vector2(Field.Left + 24, Field.CenterY)),
                        ("right", new Vector2(Field.Right - 24, Field.CenterY)),
                        ("upper-left", new Vector2(Field.Left + 24, Field.Top + 24)),
                        ("upper-right", new Vector2(Field.Right - 24, Field.Top + 24)),
                        ("lower-left", new Vector2(Field.Left + 24, Field.Bottom - 24)),
                        ("lower-right", new Vector2(Field.Right - 24, Field.Bottom - 24)) };
                    for (int probe = 0; probe < probes.Length; probe++)
                        for (int i = 0; i < selected.Length; i++)
                            if (onlyMechanics == null || onlyMechanics.Contains(selectedMechanics[i]))
                                await Clip(probe * selected.Length + i + 1, selected[i].Item1, selected[i].Item2,
                                    probes[probe].Item2, probes[probe].Item1);
                }
                else
                {
                    for (int i = 0; i < selected.Length; i++)
                        if (onlyMechanics == null || onlyMechanics.Contains(selectedMechanics[i]))
                            await Clip(i + 1, selected[i].Item1, selected[i].Item2);
                    if (_qa)
                    {
                        var starts = new[] { new Vector2(Field.Left + 24, Field.Bottom - 24),
                            new Vector2(Field.Right - 24, Field.Top + 24) };
                        for (int i = 0; i < selected.Length; i++)
                            if (onlyMechanics == null || onlyMechanics.Contains(selectedMechanics[i]))
                                await Clip(i + 9, selected[i].Item1, selected[i].Item2, starts[i % starts.Length]);
                    }
                }
            }
            else
            {
                if (!waveOnly)
                {
                    await Clip(1, BossTechniqueKind.Edge, 0);
                    await Clip(2, BossTechniqueKind.Area, 0);
                }
                await Clip(3, BossTechniqueKind.Area, 1);
                if (_qa)
                {
                    if (!waveOnly)
                    {
                        var paperStarts = new[] { new Vector2(Field.Right - 10, Field.CenterY),
                            new Vector2(Field.CenterX, Field.Bottom - 10), new Vector2(Field.CenterX - 23, Field.CenterY) };
                        for (int i = 0; i < paperStarts.Length; i++) await Clip(i + 4, BossTechniqueKind.Edge, 0, paperStarts[i]);
                    }
                    var corners = new[] { new Vector2(Field.Left + 10, Field.Top + 10),
                        new Vector2(Field.Right - 10, Field.Top + 10), new Vector2(Field.Left + 10, Field.Bottom - 10),
                        new Vector2(Field.Right - 10, Field.Bottom - 10) };
                    for (int i = 0; i < corners.Length; i++) await Clip(i + 7, BossTechniqueKind.Area, 1, corners[i]);
                }
            }
            System.IO.File.WriteAllText(_output + "/chapters.json", System.Text.Json.JsonSerializer.Serialize(_chapters,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[TECHNIQUES] COMPLETE Akari/{game.Difficulty} clips={_chapters.Count} time_scale={Engine.TimeScale} qa={args.Contains("--qa")}");
        }
        catch (Exception error)
        {
            exitCode = 1;
            GD.PushError($"[TECHNIQUES] FAIL {error}");
        }

        if (pauseWhenUnfocused is { } previousPause)
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!
                .Invoke(null, new object[] { previousPause });
        _running = false;
        _pilot?.Release();
        _pool?.DespawnAll();
        foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
        { audio.Stop(); audio.Stream = null; }
        if (IsInstanceValid(_root)) _root.QueueFree();
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GetTree().Quit(exitCode);
    }

    private async Task BattleCycle()
    {
        _pilot.Release();
        _pool.DespawnAll();
        _root.Player.GlobalPosition = Field.PlayerStart;
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku", typeof(Enemy));
        if (!_boss.UsesEasyTechniqueSlots || _boss.UsesTechniqueCycle)
            throw new Exception("The EASY Akari runtime selection path is not enabled.");
        var starts = new List<object>();
        var completedKeys = new HashSet<string>();
        var seen = new HashSet<BossCharacterTechnique>();
        string? failure = null;
        int completedSlots = 0;
        ulong firstFrame = Engine.GetPhysicsFrames();
        while (danmaku.EasyAkariRounds == 0)
        {
            if (Engine.GetPhysicsFrames() - firstFrame > 1200 * 60)
                throw new Exception($"EASY Akari selection did not finish one round naturally; slot={danmaku.EasyAkariSlot}, completed={completedSlots}.");
            danmaku.Tick(1d / 60);
            if (danmaku.ActiveTechnique is { } attack && seen.Add(attack))
            {
                int slot = danmaku.EasyAkariSlot;
                var expected = BossAttackCycle.EasyAkariSlots[slot];
                if (attack.TechniqueKind != expected.Kind || attack.TechniqueVariant != expected.Variant)
                    throw new Exception($"EASY runtime selection differs from slot {slot}.");
                string key = attack.MechanicKey;
                starts.Add(new { slot, kind = attack.TechniqueKind.ToString(), variant = attack.TechniqueVariant, mechanic = key,
                    started_physics_frame = Engine.GetPhysicsFrames() });
                attack.Ended += ended =>
                {
                    if (!ended.Completed) failure = $"EASY runtime slot {slot} did not complete naturally: {key}.";
                    else { completedSlots++; completedKeys.Add(key); }
                };
                GD.Print($"[TECHNIQUES] BATTLE SLOT {slot} {key}");
            }
            await PhysicsFrames(1);
            if (failure != null) throw new Exception(failure);
        }
        string[] required = { "akari.returned-express", "akari.alternating-inboxes", "akari.bound-draft-retraction",
            "akari.filing-piles", "akari.rain-splash", "akari.spent-wavefront", "akari.carried-approval", "akari.extending-clock" };
        bool passed = completedSlots == BossAttackCycle.EasyAkariSlots.Length
            && required.All(completedKeys.Contains)
            && !completedKeys.Overlaps(new[] { "akari.unsent-history", "akari.wall-reply", "akari.dwell-timecard" });
        var result = new { passed, rounds = danmaku.EasyAkariRounds, next_slot = danmaku.EasyAkariSlot,
            naturally_completed_slots = completedSlots, required_canonical_keys = required,
            completed_keys = completedKeys.OrderBy(key => key).ToArray(), starts,
            scope = "Actual BossDanmaku.Tick selection and Completed-driven cursor transitions over a whole EASY Akari round, including retained Extra slots. The controlled boss is invulnerable and stationary; this does not establish that a damage-dealing human fight lasts long enough to see all slots, and does not change HP or lives." };
        System.IO.File.WriteAllText(_output + "/battle_cycle.json", System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new Exception("EASY Akari battle-cycle reachability audit failed.");
    }

    private async Task Clip(int number, BossTechniqueKind kind, int variant, Vector2? startPosition = null, string? stationaryProbe = null)
    {
        _pilot.Release();
        _pool.DespawnAll();
        Vector2 initialPosition = startPosition ?? Field.PlayerStart;
        _stationaryProbe = stationaryProbe != null;
        _stationaryOrigin = initialPosition;
        _stationaryMaxDisplacement = 0;
        _root.Player.GlobalPosition = initialPosition;
        Write(_root.Player, "_fireCooldown", _shoot ? 0f : 9999f);
        _boss.GlobalPosition = new Vector2(Field.BossCenterX, Field.BossZoneCenterY);
        _root.Hud.HideSpellCard();
        _running = true;
        long start = (long)Engine.GetProcessFrames();
        ulong firstPhysicsFrame = Engine.GetPhysicsFrames();
        var attack = BossCharacterTechnique.Begin(_boss, kind, variant);
        _draftGuideIndex = 0;
        float horizontal = initialPosition.X < Field.CenterX ? 1 : -1;
        float vertical = initialPosition.Y < Field.CenterY ? 1 : -1;
        _draftGuidePoints = new[] { initialPosition + new Vector2(horizontal * 32, 0),
            initialPosition + new Vector2(horizontal * 32, vertical * 34),
            initialPosition + new Vector2(horizontal * 64, vertical * 34) }
            .Select(point => point.Clamp(new Vector2(Field.Left + 14, Field.Top + 14),
                new Vector2(Field.Right - 14, Field.Bottom - 14))).ToArray();
        _wavePilotIndex = -1;
        _waveGeometryFailure = null;
        _warningPathFailure = null;
        _warningStageFailure = null;
        Array.Clear(_waveArtPreviousRadius);
        Array.Clear(_waveArtRadiusFrames);
        Array.Fill(_waveArtMinimumRadiusDelta, float.PositiveInfinity);
        _waveReturns.Clear();
        if (attack.MechanicKey == "akari.filing-piles")
        {
            _paperPass = Property<int>(attack, "EasyPilePass");
            _paperGapChanges = 0;
            _paperGapTravel = 0;
            _paperCorridorY = initialPosition.Y;
            _paperCorridorX = Enumerable.Range(0, Property<int>(attack, "EasyPileColumnCount") - 1)
                .Select(column => (Invoke<float>(attack, "EasyPileX", column) + Invoke<float>(attack, "EasyPileX", column + 1)) / 2)
                .OrderBy(x => Math.Round(Math.Abs(x - initialPosition.X), 2)).ThenBy(x => x).First();
        }
        using var audit = new TechniqueAudit(_root.Player.SlowestMoveSpeed, _qa, initialPosition, _stationaryProbe);
        _audit = audit;
        _observedAttack = attack;
        string title = attack.SpellName;
        string mechanic = attack.MechanicKey;
        var warningStageAudit = mechanic != "akari.rain-splash" && (_eightTechniques || mechanic == "akari.spent-wavefront")
            ? new WarningStageAudit(mechanic) : null;
        _warningStageAudit = warningStageAudit;
        var warningPathAudit = _eightTechniques ? new WarningPathAudit(mechanic, warningStageAudit) : null;
        _warningPathAudit = warningPathAudit;
        bool ended = false, completed = false, warningShot = false, attackShot = false;
        int? visibleDangerousShotsAtNaturalEnd = null;
        int warningGeometryFrames = 0;
        double firstWarning = -1, warningCaptureTime = -1, attackCaptureTime = -1;
        long end = 0;
        ulong endPhysicsFrame = 0;
        int shots = 0, hazardFrames = 0, peak = 0;
        double elapsed = 0, firstActive = -1;
        var checks = new HashSet<string>();
        (Bullet Shot, ulong Activation, Vector2 LastPosition, float Radius)? splashWatch = null;
        attack.Ended += finished =>
        {
            ended = true;
            completed = finished.Completed;
            end = (long)Engine.GetProcessFrames();
            endPhysicsFrame = Engine.GetPhysicsFrames();
            shots = finished.ShotsCreated;
            hazardFrames = finished.HazardFrames;
            elapsed = finished.Elapsed;
            if (_eightTechniques)
                visibleDangerousShotsAtNaturalEnd = Property<int?>(finished, "VisibleDangerousShotsAtNaturalEnd");
            if (warningPathAudit != null) _warningPathFailure ??= warningPathAudit.Observe(finished, captureForecasts: false);
            if (warningStageAudit != null) _warningStageFailure ??= warningStageAudit.Observe(finished);
            _observedAttack = null;
        };
        GD.Print($"[TECHNIQUES] START {number:00} frame={start} {kind}/{variant} {title}");

        while (!ended)
        {
            if (Engine.GetPhysicsFrames() - firstPhysicsFrame >= 120 * 60)
                throw new Exception($"Technique did not end naturally: {title}; elapsed={attack.Elapsed:F3}, tree_paused={GetTree().Paused}, bubble_paused={Hud.BubblePaused}, time_scale={Engine.TimeScale}, pause_when_unfocused={Settings.PauseWhenUnfocused}");
            await Frames(1);
            if (_waveGeometryFailure is { } geometryFailure) throw new Exception(geometryFailure);
            if (_warningPathFailure is { } pathFailure) throw new Exception(pathFailure);
            if (_warningStageFailure is { } stageFailure) throw new Exception(stageFailure);
            if (ended) break;
            var owned = attack.OwnedShots.ToArray();
            peak = Math.Max(peak, owned.Length);
            CheckBoundaries(attack, owned, checks, ref splashWatch);
            if (firstActive < 0 && (attack.IsStriking || owned.Any(b => !b.LeadIn && b.CollisionLayer != 0)))
                firstActive = attack.Elapsed;
            bool hasWarningGeometry = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(
                attack, "_hazards", typeof(BossCharacterTechnique)).Any(hazard => !hazard.Live);
            if (hasWarningGeometry)
            {
                warningGeometryFrames++;
                if (firstWarning < 0) firstWarning = attack.Elapsed;
            }
            bool warningComplete = mechanic switch
            {
                "akari.returned-express" => warningStageAudit?.Full("delivery:0") == true,
                "akari.alternating-inboxes" => warningStageAudit?.Full("inbox:0") == true,
                "akari.bound-draft-retraction" => warningStageAudit?.Full("unbinding:0:exit") == true,
                "akari.filing-piles" => warningStageAudit?.Full("pile:0:0") == true,
                "akari.rain-splash" => Invoke<float>(attack, "EasyRainDropWarningProgress", 0, 0) >= 1,
                "akari.spent-wavefront" => warningStageAudit?.Full("wave:0:expiry") == true,
                "akari.carried-approval" => warningStageAudit?.Full("envelope:body") == true,
                "akari.extending-clock" => warningStageAudit?.Full("clock") == true,
                _ => false
            };
            bool warningReady = attack.Elapsed >= .45 && (!_eightTechniques || hasWarningGeometry && warningComplete);
            if (_shots && !warningShot && warningReady)
            {
                warningShot = true;
                warningCaptureTime = attack.Elapsed;
                await Capture($"clip_{number:00}_warning.png");
            }
            bool live = attack.IsStriking || owned.Any(b => !b.LeadIn && b.CollisionLayer != 0);
            if (_shots && !attackShot && !ended && firstActive >= 0
                && attack.Elapsed >= firstActive + (_eightTechniques ? .08 : .35) && (!_eightTechniques || live))
            {
                attackShot = true;
                attackCaptureTime = attack.Elapsed;
                await Capture($"clip_{number:00}_attack.png");
            }
        }
        _audit = null;
        _warningPathAudit = null;
        _warningStageAudit = null;
        _pilot.Release();
        if (_waveGeometryFailure is { } finalGeometryFailure) throw new Exception(finalGeometryFailure);
        if (_warningPathFailure is { } finalPathFailure) throw new Exception(finalPathFailure);
        if (_warningStageFailure is { } finalStageFailure) throw new Exception(finalStageFailure);
        if (!completed || shots == 0 && hazardFrames == 0)
            throw new Exception($"Technique was interrupted or produced no attack: {title}");
        string[] requiredChecks = mechanic switch
        {
            "akari.filing-piles" => Enumerable.Range(0, 2).SelectMany(pass => new[] { "paper-warning-safe", "paper-falling-outline", "pile-center", "pile-outside", "pile-outline", "pile-expired" }
                .Select(check => $"{check}-pass{pass}")).Concat(new[] { "paper-left-edge-reached", "paper-right-edge-reached" }).ToArray(),
            "akari.rain-splash" => new[] { "rain-warning-safe", "rain-deployment-safe", "rain-shot-active", "rain-splash-expired", "illustration-collider" },
            "akari.spent-wavefront" => new[] { "wave-warning-safe", "wave-launch-center", "wave-center-released", "wave-before-opening-full", "wave-opening-target-cleared", "wave-open-sector-clear", "wave-retained-caps-active", "wave-edge-active", "wave-inner-safe", "wave-gap-cleared", "wave-expired" },
            _ => Array.Empty<string>()
        };
        var missingChecks = requiredChecks.Where(check => !checks.Contains(check)).ToArray();
        if (missingChecks.Length > 0) throw new Exception($"Missing boundary checks: {title}: {string.Join(", ", missingChecks)}");
        if (_shots)
        {
            if (!warningShot || !attackShot) throw new Exception($"Missing warning or attack screenshot: {title}");
            await Capture($"clip_{number:00}_end.png");
        }
        while (Engine.GetPhysicsFrames() - endPhysicsFrame < 72)
        {
            await Frames(1);
        }
        _running = false;
        _chapters.Add(new { title, kind = kind.ToString(), variant, mechanic, start, end, completed, shots,
            hazardFrames, elapsed, peak, check_count = checks.Count, difficulty = GameManager.Instance!.Difficulty.ToString(), fps = 60,
            capture = new { automatic_movement = !_stationaryProbe, invincible = true, shooting = _shoot, bombs = false, dodge = false,
                movement = _stationaryProbe ? "zero movement input from a fresh attack launch at the requested stationary position"
                    : mechanic == "akari.bound-draft-retraction" ? "normal-input movement through three reachable points during draft recording, then reactive avoidance"
                    : mechanic == "akari.filing-piles" ? "guided between each pass's nearest column gap using normal input"
                    : mechanic == "akari.spent-wavefront" ? "guided to the visible waiting marker; observe a live blocked inward corridor, then return 0.2 seconds after its collision geometry opens using normal input"
                    : "reactive bullet avoidance",
                guided_speed_limit = _root.Player.SlowestMoveSpeed,
                stationary_probe = stationaryProbe,
                stationary_maximum_displacement = _stationaryProbe ? _stationaryMaxDisplacement : (float?)null,
                draft_recording_waypoints = !_stationaryProbe && mechanic == "akari.bound-draft-retraction"
                    ? _draftGuidePoints.Select(point => new { x = point.X, y = point.Y }).ToArray() : null,
                draft_recording_waypoints_reached = _draftGuideIndex,
                warning_audit = new { observed_frames = warningGeometryFrames, first_warning_at = firstWarning,
                    first_active_at = firstActive, warning_precedes_first_attack = firstWarning >= 0 && firstActive > firstWarning,
                    warning_screenshot_at = warningCaptureTime >= 0 ? warningCaptureTime : (double?)null,
                    attack_screenshot_at = attackCaptureTime >= 0 ? attackCaptureTime : (double?)null,
                    screenshot_requires_target_warning_geometry = _eightTechniques,
                    screenshot_requires_live_attack = _eightTechniques },
                natural_end_audit = new { @checked = _eightTechniques,
                    visible_dangerous_owned_shots_before_finish = visibleDangerousShotsAtNaturalEnd,
                    passed = !_eightTechniques || visibleDangerousShotsAtNaturalEnd == 0,
                    basis = "Observed by the technique before normal Finish and its cancellation cleanup; active collision-enabled enemy shots intersecting the player-radius-padded playfield." },
                warning_path_audit = warningPathAudit?.Result(),
                warning_stage_audit = warningStageAudit?.Result(),
                paper_gap_changes = mechanic == "akari.filing-piles" ? _paperGapChanges : 0,
                paper_required_horizontal_travel = mechanic == "akari.filing-piles" ? _paperGapTravel : 0,
                wave_returns = _waveReturns.ToArray(),
                wave_radius_checks = Enumerable.Range(0, _waveArtRadiusFrames.Length)
                    .Where(wave => _waveArtRadiusFrames[wave] > 0)
                    .Select(wave => new { wave = wave + 1, frames = _waveArtRadiusFrames[wave],
                        minimum_radius_delta = _waveArtRadiusFrames[wave] > 1 ? _waveArtMinimumRadiusDelta[wave] : (float?)null }).ToArray(),
                visual_comprehension_test = false,
                initial_position = new { x = initialPosition.X, y = initialPosition.Y },
                survival_is_not_safety_evidence = true },
            arena_audit = audit.Result(elapsed, _qa, !_eightTechniques && !_stationaryProbe) });
        System.IO.File.WriteAllText(_output + "/chapters.json", System.Text.Json.JsonSerializer.Serialize(_chapters,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        audit.RequirePass(title, _qa, !_eightTechniques && !_stationaryProbe);
        warningPathAudit?.RequirePass(shots);
        warningStageAudit?.RequirePass();
        if (_eightTechniques && (warningGeometryFrames == 0 || firstWarning < 0 || firstActive <= firstWarning))
            throw new Exception($"A visible warning did not precede the first live attack: {title}");
        if (_eightTechniques && visibleDangerousShotsAtNaturalEnd != 0)
            throw new Exception($"Natural end did not verify an empty live-projectile field before Finish: {title}; observed={visibleDangerousShotsAtNaturalEnd?.ToString() ?? "not checked"}");
        if (_stationaryProbe && _stationaryMaxDisplacement > .01f)
            throw new Exception($"Stationary probe moved despite zero input: {title}; displacement={_stationaryMaxDisplacement:F3}");
        if (!_stationaryProbe && mechanic == "akari.filing-piles" && (_paperGapChanges != 1 || _paperGapTravel < 20))
            throw new Exception($"Paper pass did not require its gap change: changes={_paperGapChanges}, travel={_paperGapTravel:F2}");
        if (!_stationaryProbe && mechanic == "akari.spent-wavefront")
        {
            if (_waveReturns.Count != 2)
                throw new Exception($"Wave pilot did not react to two observed live openings: returns={_waveReturns.Count}");
            if (_waveArtRadiusFrames.Any(frames => frames < 2))
                throw new Exception($"Wave radius motion was not checked for both waves: frames={string.Join(",", _waveArtRadiusFrames)}");
        }
        GD.Print($"[TECHNIQUES] END {number:00} frame={end} completed={completed} shots={shots} hazard_frames={hazardFrames} checks={checks.Count} elapsed={elapsed:F3}");
    }

    private static void CheckBoundaries(BossCharacterTechnique attack, Bullet[] owned, HashSet<string> checks,
        ref (Bullet Shot, ulong Activation, Vector2 LastPosition, float Radius)? splashWatch)
    {
        void Check(string name, bool passed)
        {
            if (checks.Contains(name)) return;
            if (!passed) throw new Exception($"Boundary check failed: {attack.SpellName}/{name} at {attack.Elapsed:F3}s");
            checks.Add(name);
            GD.Print($"[TECHNIQUES] CHECK {attack.MechanicKey}/{name} at {attack.Elapsed:F3}s");
        }

        double warning = Read<double>(attack, "_warning");
        double age = attack.Elapsed - warning;
        var hazards = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(
            attack, "_hazards", typeof(BossCharacterTechnique));
        if (attack.MechanicKey == "akari.filing-piles")
        {
            int pass = Property<int>(attack, "EasyPilePass");
            string Key(string key) => $"{key}-pass{pass}";
            age = attack.Elapsed - Invoke<double>(attack, "EasyPileStart", 0);
            float x = Invoke<float>(attack, "EasyPileX", 0);
            if (attack.Elapsed >= .15 && age < 0)
                Check(Key("paper-warning-safe"), !attack.IsStriking && !attack.CoversPoint(new Vector2(x, Field.CenterY)));
            var sheets = Enumerable.Range(0, Property<int>(attack, "EasyPileColumnCount"))
                .Select(column => Invoke<Rect2>(attack, "EasyPileSheetBounds", column, 0).Intersection(Field.Rect)).ToArray();
            if (age > 0 && sheets.All(sheet => sheet.Position.Y > Field.Top + 4 && sheet.End.Y < Field.Bottom - 60
                && sheet.HasArea()))
            {
                Rect2 bounds = sheets[0];
                Check(Key("paper-falling-outline"), attack.CoversPoint(bounds.GetCenter())
                    && sheets.All(sheet => hazards.Any(h => h.Live && h.Shape == 2 && h.A == sheet.Position && h.B == sheet.End)));
                if (pass == 1)
                {
                    Rect2 left = Invoke<Rect2>(attack, "EasyPileSheetBounds", 0, 0).Intersection(Field.Rect);
                    Rect2 right = Invoke<Rect2>(attack, "EasyPileSheetBounds", Property<int>(attack, "EasyPileColumnCount") - 1, 0).Intersection(Field.Rect);
                    Check("paper-left-edge-reached", attack.CoversPoint(new Vector2(Field.Left + 2, left.GetCenter().Y)));
                    Check("paper-right-edge-reached", attack.CoversPoint(new Vector2(Field.Right - 2, right.GetCenter().Y)));
                }
            }
            if (Read<int[]>(attack, "_states")[0] == Constant<int>(attack, "EasyPileRows"))
            {
                Rect2 bounds = Invoke<Rect2>(attack, "EasyPileStackBounds", 0);
                Vector2 center = bounds.GetCenter();
                Check(Key("pile-center"), attack.CoversPoint(center));
                Check(Key("pile-outside"), !attack.CoversPoint(new Vector2(bounds.End.X + Player.HitRadius + 4, center.Y)));
                Check(Key("pile-outline"), hazards.Any(h => h.Live && h.Shape == 2 && h.A == bounds.Position && h.B == bounds.End));
            }
            if (age > 0 && Invoke<int>(attack, "EasyPileRemaining", 0) == 0)
                Check(Key("pile-expired"), !attack.CoversPoint(new Vector2(x, Field.Bottom - 4)));
        }
        else if (attack.MechanicKey == "akari.rain-splash")
        {
            if (attack.IsStriking) throw new Exception($"Rain warning became a contact hazard at {attack.Elapsed:F3}s");
            if (attack.Elapsed >= .15 && age < 0)
                Check("rain-warning-safe", !attack.CoversPoint(Invoke<Vector2>(attack, "EasyRainOrigin", 0, 0, 0)));
            foreach (var line in hazards.Where(h => !h.Live && h.Shape == 0))
                if (attack.CoversPoint((line.A + line.B) / 2))
                    throw new Exception($"Rain growing warning path became a contact hazard at {attack.Elapsed:F3}s");
            var deploying = owned.FirstOrDefault(b => b.LeadIn);
            if (deploying != null)
                Check("rain-deployment-safe", deploying.CollisionLayer == 0 && deploying.CollisionMask == 0);
            var active = owned.FirstOrDefault(b => !b.LeadIn);
            if (active != null)
                Check("rain-shot-active", active.CollisionLayer != 0 && active.CollisionMask != 0);
            var splash = owned.FirstOrDefault(b => !b.LeadIn && b.BossPattern == 2);
            if (splashWatch == null && splash != null)
                splashWatch = (splash, splash.ActivationId, splash.GlobalPosition,
                    splash.CollisionRadius * Math.Max(splash.GlobalTransform.X.Length(), splash.GlobalTransform.Y.Length()) + Player.HitRadius);
            if (splashWatch is { } watched)
            {
                if (watched.Shot.Active && watched.Shot.ActivationId == watched.Activation)
                    splashWatch = (watched.Shot, watched.Activation, watched.Shot.GlobalPosition, watched.Radius);
                else Check("rain-splash-expired", !Field.Rect.Grow(watched.Radius).HasPoint(watched.LastPosition));
            }
        }
        else if (attack.MechanicKey == "akari.spent-wavefront")
        {
            Vector2 origin = Read<Vector2>(attack, "_origin");
            var launches = Read<double[]>(attack, "_easyWaveLaunches");
            var warningStarts = Read<double[]>(attack, "_easyWaveWarningStarts");
            double opening = Constant<double>(attack, "EasyWaveOpeningAt");
            double duration = Property<double>(attack, "EasyWaveDuration");
            int spent = Constant<int>(attack, "EasyWaveSpentSegments");
            for (int wave = 0; wave < launches.Length; wave++)
            {
                if (launches[wave] < 0) continue;
                age = attack.Elapsed - launches[wave];
                void WaveCheck(string name, bool passed)
                {
                    if (!passed) throw new Exception($"Boundary check failed: {attack.SpellName}/{name}, wave={wave + 1} at {attack.Elapsed:F3}s");
                    Check(name, true);
                }
                if (attack.Elapsed >= warningStarts[wave] + .15 && age < 0)
                    WaveCheck("wave-warning-safe", !attack.CoversPoint(origin));
                if (age >= .05 && age < .25) WaveCheck("wave-launch-center", attack.CoversPoint(origin));
                if (age >= .35 && age < .65) WaveCheck("wave-center-released", !attack.CoversPoint(origin));
                float radius = Invoke<float>(attack, "EasyWaveRadius", age);
                Vector2 Point(int segment) => Invoke<Vector2>(attack, "EasyWavePoint", wave, segment, radius);
                if (age >= opening - .1 && age < opening)
                    WaveCheck("wave-before-opening-full", Enumerable.Range(1, spent - 1).All(segment => attack.CoversPoint(Point(segment))));
                if (age >= opening && age < opening + .1)
                {
                    WaveCheck("wave-opening-target-cleared", Enumerable.Range(1, spent - 1).All(segment => !attack.CoversPoint(Point(segment))));
                    Vector2 start = Point(spent), end = Point(Constant<int>(attack, "EasyWaveSegments"));
                    WaveCheck("wave-retained-caps-active", attack.CoversPoint(start) && attack.CoversPoint(end)
                        && hazards.Any(h => h.Live && h.Shape == 0 && h.A.IsEqualApprox(start))
                        && hazards.Any(h => h.Live && h.Shape == 0 && h.B.IsEqualApprox(end)));
                }
                if (age >= opening && age < opening + .1)
                {
                    var edge = hazards.First(h => h.Shape == 0 && h.Live);
                    WaveCheck("wave-edge-active", attack.CoversPoint(edge.A));
                    WaveCheck("wave-inner-safe", !attack.CoversPoint(origin.Lerp(edge.A, .3f)));
                }
                if (age >= opening + .1 && age < opening + .5)
                    WaveCheck("wave-gap-cleared", attack.IsStriking && !attack.CoversPoint(Point(spent / 2)));
                if (age >= duration + 1d / 60 && age < duration + .1)
                {
                    WaveCheck("wave-expired", !attack.IsStriking && !attack.CoversPoint(origin + Vector2.Right * 60));
                    if (wave == launches.Length - 1) Check("wave-open-sector-clear", true);
                }
            }
        }

        if (checks.Contains("illustration-collider") || attack.MechanicKey != "akari.rain-splash")
            return;
        var shot = owned.FirstOrDefault(b => !b.LeadIn && b.CollisionLayer != 0);
        if (shot == null) return;
        var footprint = shot.Footprint!;
        var edges = Read<Vector2[][]>(shot, "_illustrationEdges");
        var shapes = shot.GetShapeOwners().Select(owner => (uint)owner)
            .Where(owner => !shot.IsShapeOwnerDisabled(owner))
            .SelectMany(owner => Enumerable.Range(0, shot.ShapeOwnerGetShapeCount(owner)).Select(i => shot.ShapeOwnerGetShape(owner, i))).ToArray();
        Check("illustration-collider", edges != null && edges.Length > 0 && edges.Length == footprint.Contours.Length
            && edges.Select((edge, i) => edge.Length == footprint.Contours[i].Length + 1
                && edge[0] == edge[^1] && edge.Take(edge.Length - 1).SequenceEqual(footprint.Contours[i])).All(match => match)
            && shapes.Select(shape => shape.GetRid()).SequenceEqual(footprint.Shapes.Select(shape => shape.GetRid())));
    }

    private string? CheckWaveArtRadius(BossCharacterTechnique attack)
    {
        var launches = Read<double[]>(attack, "_easyWaveLaunches");
        double duration = Property<double>(attack, "EasyWaveDuration");
        for (int wave = 0; wave < launches.Length; wave++)
        {
            if (launches[wave] < 0 || attack.Elapsed - launches[wave] >= duration) continue;
            float radius = Invoke<float>(attack, "EasyWaveArtRadius", wave);
            if (_waveArtPreviousRadius[wave] is { } previous)
            {
                float delta = radius - previous;
                _waveArtMinimumRadiusDelta[wave] = Math.Min(_waveArtMinimumRadiusDelta[wave], delta);
                if (delta < -.0001f)
                    return $"Wave art radius moved inward: wave={wave + 1}, time={attack.Elapsed:F3}, previous={previous:F6}, radius={radius:F6}";
            }
            _waveArtPreviousRadius[wave] = radius;
            _waveArtRadiusFrames[wave]++;
        }
        return null;
    }

    private static string? CheckWaveOpenSector(BossCharacterTechnique attack)
    {
        if (!attack.IsStriking) return null;
        Vector2 origin = Read<Vector2>(attack, "_origin");
        var launches = Read<double[]>(attack, "_easyWaveLaunches");
        double duration = Property<double>(attack, "EasyWaveDuration");
        float halfAngle = Property<float>(attack, "EasyWaveExpiryHalfAngle");
        float inner = Property<float>(attack, "EasyWaveExpiryInnerRadius")
            * Mathf.Cos(halfAngle / Constant<int>(attack, "EasyWaveSpentSegments"));
        float outer = Property<float>(attack, "EasyWaveMaxRadius");
        var hazards = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(
            attack, "_hazards", typeof(BossCharacterTechnique));
        for (int wave = 0; wave < launches.Length; wave++)
        {
            if (launches[wave] < 0 || attack.Elapsed - launches[wave] >= duration) continue;
            Vector2 forward = Vector2.FromAngle(Invoke<float>(attack, "EasyWaveGapAngle", wave));
            Vector2 side = new(-forward.Y, forward.X);
            Vector2 leftNormal = forward * Mathf.Sin(halfAngle) + side * Mathf.Cos(halfAngle);
            Vector2 rightNormal = forward * Mathf.Sin(halfAngle) - side * Mathf.Cos(halfAngle);
            foreach (var hazard in hazards.Where(h => h.Live && h.Shape is 0 or 1))
            {
                float reach = hazard.Radius + Player.HitRadius;
                Vector2 a = hazard.A - origin, b = hazard.B - origin;
                if (Math.Max(a.Length(), b.Length()) + reach < inner
                    || Math.Max(a.Dot(leftNormal), b.Dot(leftNormal)) + reach < 0
                    || Math.Max(a.Dot(rightNormal), b.Dot(rightNormal)) + reach < 0) continue;
                var bounds = new Rect2(hazard.A, Vector2.Zero).Expand(hazard.B).Grow(reach).Intersection(Field.Rect);
                // Outside these padded hazard bounds CoversPoint is false; include inner-arc chords in the sector sample area.
                for (float y = Mathf.Ceil(bounds.Position.Y * 2) / 2; y <= bounds.End.Y; y += .5f)
                for (float x = Mathf.Ceil(bounds.Position.X * 2) / 2; x <= bounds.End.X; x += .5f)
                {
                    Vector2 point = new(x, y), offset = point - origin;
                    float distance = offset.Length();
                    if (distance < inner || distance > outer || offset.Dot(leftNormal) < 0 || offset.Dot(rightNormal) < 0) continue;
                    if (attack.CoversPoint(point))
                        return $"Wave hazard remains in the future/opened sector: wave={wave + 1}, time={attack.Elapsed:F3}, point={point}";
                }
            }
        }
        return null;
    }

    private sealed class WarningStageAudit
    {
        private readonly record struct Sample(string Key, double Start, double Fire, float Progress, bool RequireGrowing = true);
        private sealed class Stage
        {
            public double Start, Fire, InitialFire;
            public bool RequireGrowing;
            public readonly double?[] First = new double?[3];
            public readonly int[] Samples = new int[3];
            public int Last = -1, ActualObservations;
            public float LastProgress;
            public double? FirstActual;
        }
        private readonly string _mechanic;
        private readonly Dictionary<string, Stage> _stages = new();
        private double _lastTime = -1;
        private string? _failure;
        private const double FullHold = 2, FrameTolerance = 1d / 60;

        public WarningStageAudit(string mechanic) => _mechanic = mechanic;

        private IEnumerable<Sample> Events(BossCharacterTechnique attack)
        {
            double time = attack.Elapsed, warning = Read<double>(attack, "_warning");
            var result = new List<Sample>();
            void Add(string key, double start, double fire, float progress, bool requireGrowing = true)
            {
                if (time >= start && time < fire) result.Add(new(key, start, fire, progress, requireGrowing));
            }
            switch (_mechanic)
            {
                case "akari.returned-express":
                    for (int batch = 0; batch < 3; batch++)
                        Add($"delivery:{batch}", Invoke<double>(attack, "EasyDeliveryWarningStart", batch),
                            Invoke<double>(attack, "EasyDeliveryLaunch", batch),
                            Invoke<float>(attack, "EasyDeliveryWarningProgress", batch));
                    break;
                case "akari.alternating-inboxes":
                    for (int turn = 0; turn < 4; turn++)
                        Add($"inbox:{turn}", Invoke<double>(attack, "EasyInboxLaunch", turn) - Constant<double>(attack, "EasyInboxWarningLead"),
                            Invoke<double>(attack, "EasyInboxLaunch", turn),
                            Invoke<float>(attack, "EasyInboxWarningProgress", turn));
                    break;
                case "akari.bound-draft-retraction":
                    int round = Property<int>(attack, "EasyUnbindingRound");
                    if (round >= 0)
                    {
                        int first = round * Constant<int>(attack, "EasyUnbindingCopiesPerRound");
                        double start = Invoke<double>(attack, "EasyUnbindingWarningStart", round);
                        double fire = Invoke<double>(attack, "EasyUnbindingFire", first);
                        var progress = Invoke<(float Retrace, float Exit)>(attack, "EasyUnbindingWarningProgress", round);
                        var history = Read<List<Vector2>>(attack, "_history");
                        bool retraces = history.Zip(history.Skip(1), (a, b) => a.DistanceSquaredTo(b)).Any(length => length > .01f);
                        Add($"unbinding:{round}:retrace", start, fire, progress.Retrace, retraces);
                        Add($"unbinding:{round}:exit", start, fire, progress.Exit);
                    }
                    break;
                case "akari.filing-piles":
                    int pass = Property<int>(attack, "EasyPilePass");
                    for (int column = 0; column < Property<int>(attack, "EasyPileColumnCount"); column++)
                    {
                        double fire = Invoke<double>(attack, "EasyPileStart", column);
                        Add($"pile:{pass}:{column}", fire - warning, fire, Invoke<float>(attack, "EasyPileWarningProgress", column));
                    }
                    break;
                case "akari.spent-wavefront":
                    var starts = Read<double[]>(attack, "_easyWaveWarningStarts");
                    var launches = Read<double[]>(attack, "_easyWaveLaunches");
                    for (int wave = 0; wave < launches.Length; wave++)
                    {
                        if (launches[wave] < 0) continue;
                        var progress = Invoke<(float Ring, float Expiry)>(attack, "EasyWaveWarningProgress", wave);
                        Add($"wave:{wave}:ring", starts[wave], launches[wave], progress.Ring);
                        Add($"wave:{wave}:expiry", starts[wave], launches[wave], progress.Expiry);
                    }
                    break;
                case "akari.carried-approval":
                    Add("envelope:body", 0, warning, Invoke<float>(attack, "EasyEnvelopeTravelWarningProgress"));
                    var stamps = Read<System.Collections.IEnumerable>(attack, "_stamps").Cast<object>().ToArray();
                    for (int index = 0; index < stamps.Length; index++)
                    {
                        double fire = Invoke<double>(attack, "EasyApprovalStampFire", index), start = Invoke<double>(attack, "EasyApprovalWarningStart", index);
                        Add($"envelope:opening:{index}", start, Invoke<double>(attack, "EasyApprovalOpeningFire", index),
                            Invoke<float>(attack, "EasyEnvelopeOpeningWarningProgress", index));
                        var progress = Invoke<(float Frame, float Check)>(attack, "EasyApprovalStampWarningProgress", index);
                        Add($"stamp:{index}:frame", start, fire, progress.Frame);
                        Add($"stamp:{index}:check", start, fire, progress.Check);
                        Add($"stamp:{index}:disc", start, fire, Invoke<float>(attack, "EasyApprovalStampDiscWarningProgress", index));
                        var paths = Invoke<(Vector2 A, Vector2 B, float Radius)[]>(attack, "EasyApprovalStampPaths", index);
                        for (int path = 0; path < paths.Length; path++)
                            Add($"stamp:{index}:{path}", start, fire,
                                Invoke<float>(attack, "EasyApprovalStampPathWarningProgress", index, path));
                    }
                    break;
                case "akari.extending-clock":
                    Add("clock", 0, warning, Invoke<float>(attack, "EasyClockWarningProgress"));
                    break;
            }
            return result;
        }

        public bool Full(string key) => _stages.TryGetValue(key, out var stage) && stage.First[2] != null && stage.LastProgress >= 1;

        public string? Actual(string key, double time)
        {
            if (_failure != null) return _failure;
            if (!_stages.TryGetValue(key, out var stage) || stage.First[2] == null)
                return _failure = $"Actual attack has no fully observed staged warning: {_mechanic}/{key}, time={time:F3}";
            double fullAt = stage.First[2]!.Value;
            if (time + FrameTolerance + 1e-6 < stage.Fire || time - fullAt + FrameTolerance + 1e-6 < FullHold)
                return _failure = $"Actual attack preceded its complete warning hold: {_mechanic}/{key}, full={fullAt:F3}, actual={time:F3}, planned={stage.Fire:F3}";
            stage.FirstActual = stage.FirstActual == null ? time : Math.Min(stage.FirstActual.Value, time);
            stage.ActualObservations++;
            return null;
        }

        public string? Observe(BossCharacterTechnique attack)
        {
            if (_failure != null || attack.Elapsed == _lastTime) return _failure;
            _lastTime = attack.Elapsed;
            foreach (var sample in Events(attack))
            {
                if (!_stages.TryGetValue(sample.Key, out var stage))
                    _stages.Add(sample.Key, stage = new Stage { Start = sample.Start, Fire = sample.Fire, InitialFire = sample.Fire, RequireGrowing = sample.RequireGrowing });
                int current = sample.Progress <= 0 ? 0 : sample.Progress >= 1 ? 2 : 1;
                bool allowedDeadlineAdvance = _mechanic == "akari.carried-approval" && sample.Key.StartsWith("stamp:")
                    && sample.Fire <= stage.Fire && stage.InitialFire - sample.Fire <= .8 + .00001;
                if (sample.Progress < 0 || sample.Progress > 1 || sample.Progress + .00001f < stage.LastProgress
                    || current < stage.Last || stage.Last < 0 && current != 0
                    || sample.RequireGrowing && current > stage.Last + 1
                    || Math.Abs(stage.Fire - sample.Fire) > .00001 && !allowedDeadlineAdvance)
                    return _failure = $"Missing or reversed staged warning: {_mechanic}/{sample.Key}, previous={stage.Last}, current={current}, time={attack.Elapsed:F3}";
                stage.Fire = sample.Fire;
                stage.Last = current;
                stage.LastProgress = sample.Progress;
                stage.First[current] ??= attack.Elapsed;
                stage.Samples[current]++;
                if (stage.First[2] is { } full && stage.Fire - full + FrameTolerance + 1e-6 < FullHold)
                    return _failure = $"Planned attack does not retain two seconds of complete warning: {_mechanic}/{sample.Key}, full={full:F3}, planned={stage.Fire:F3}";
            }
            if (_mechanic == "akari.spent-wavefront")
            {
                var hazards = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(
                    attack, "_hazards", typeof(BossCharacterTechnique));
                var launches = Read<double[]>(attack, "_easyWaveLaunches");
                Vector2 origin = Read<Vector2>(attack, "_origin");
                for (int wave = 0; wave < launches.Length; wave++)
                    if (launches[wave] >= 0 && attack.Elapsed >= launches[wave] && attack.Elapsed < launches[wave] + .3
                        && hazards.Any(hazard => hazard.Live && hazard.Shape == 1 && hazard.A.DistanceTo(origin) < .01f))
                    {
                        string? failure = Actual($"wave:{wave}:ring", attack.Elapsed) ?? Actual($"wave:{wave}:expiry", attack.Elapsed);
                        if (failure != null) return failure;
                    }
            }
            return null;
        }

        private string? CompletionFailure()
        {
            if (_failure != null) return _failure;
            int expected = _mechanic switch
            {
                "akari.returned-express" => 3,
                "akari.alternating-inboxes" => 4,
                "akari.bound-draft-retraction" => 8,
                "akari.filing-piles" => 15,
                "akari.spent-wavefront" => 4,
                "akari.carried-approval" => 41,
                "akari.extending-clock" => 1,
                _ => 0,
            };
            if (expected == 0 || _stages.Count != expected)
                return $"Missing canonical warning events: {_mechanic}, expected={expected}, actual={_stages.Count}";
            foreach (var (key, stage) in _stages)
            {
                if (stage.Last != 2 || stage.First[0] == null || stage.First[2] == null
                    || stage.RequireGrowing && stage.First[1] == null || stage.FirstActual == null)
                    return $"Incomplete warning-to-actual sequence: {_mechanic}/{key}";
                if (stage.Fire - stage.First[2]!.Value + FrameTolerance + 1e-6 < FullHold
                    || stage.FirstActual.Value - stage.First[2]!.Value + FrameTolerance + 1e-6 < FullHold)
                    return $"Missing complete pre-attack warning hold: {_mechanic}/{key}";
            }
            string? Order(string first, string second)
            {
                if (!_stages.TryGetValue(first, out var before) || !_stages.TryGetValue(second, out var after)
                    || after.First[1]!.Value + FrameTolerance < before.First[2]!.Value)
                    return $"Compound warning routes overlapped out of order: {_mechanic}/{first}/{second}";
                return null;
            }
            if (_mechanic == "akari.bound-draft-retraction")
                for (int round = 0; round < 4; round++)
                    if (Order($"unbinding:{round}:retrace", $"unbinding:{round}:exit") is { } failure) return failure;
            if (_mechanic == "akari.spent-wavefront")
                for (int wave = 0; wave < 2; wave++)
                    if (Order($"wave:{wave}:ring", $"wave:{wave}:expiry") is { } failure) return failure;
            if (_mechanic == "akari.carried-approval")
                foreach (var key in _stages.Keys.Where(key => key.EndsWith(":frame")))
                    if (Order(key, key[..^"frame".Length] + "check") is { } failure) return failure;
            return null;
        }

        public object Result() => new
        {
            @checked = true, passed = CompletionFailure() == null, failure = CompletionFailure(),
            observed_events = _stages.Count, minimum_full_warning_seconds = FullHold, physics_frame_tolerance = FrameTolerance,
            minimum_observed_full_warning_seconds = _stages.Values.Where(stage => stage.FirstActual != null && stage.First[2] != null)
                .Select(stage => stage.FirstActual - stage.First[2]).DefaultIfEmpty(null).Min(),
            basis = "Actual warning progress is observed through point-only, growing, and full phases. Both planned firing and actual attacks must follow at least two seconds of completed warning, allowing one physics frame. Full paths are cached only after completion and matched to real projectiles or hazards. Approval deadlines may advance at most 0.8 seconds while retaining the full hold. A stationary recorded draft may have no retrace geometry; its complete outgoing route still requires all phases and the full hold. Rendered pixels are not inspected.",
            events = _stages.Select(item => new { key = item.Key, warning_start = item.Value.Start,
                planned_fire = item.Value.Fire, initially_planned_fire = item.Value.InitialFire,
                deadline_advance_seconds = item.Value.InitialFire - item.Value.Fire,
                point_only_at = item.Value.First[0], growing_at = item.Value.First[1],
                full_at = item.Value.First[2], first_actual_at = item.Value.FirstActual,
                full_warning_hold_seconds = item.Value.FirstActual - item.Value.First[2],
                visible_complete_warning_hold_seconds = item.Value.Fire - item.Value.First[2],
                growing_frame_required = item.Value.RequireGrowing, stage_sample_counts = item.Value.Samples,
                actual_observations = item.Value.ActualObservations }).ToArray()
        };

        public void RequirePass()
        {
            if (CompletionFailure() is { } failure) throw new Exception(failure);
        }
    }

    private sealed class WarningPathAudit
    {
        private sealed record Forecast(string Key, double LaunchAt, double ObservedAt, Vector2[] Points, float HalfWidth);
        private sealed class ShotTrace
        {
            public Forecast Forecast = null!;
            public double Born;
            public int Group, ActiveFrames, Probes;
            public Vector2 Origin;
            public Vector2? LastPosition, TurnPoint;
            public double? FirstLiveAt, FirstReflectionAt, FirstReleaseAt, TurnAt;
            public double? FirstTurnStateObservedAt, ReflectionStateObservedAt;
            public double? RetiredAt;
            public float? TurnPointError;
            public float Radius, MaximumError, MinimumWidthMargin = float.PositiveInfinity;
            public bool Retired, Reflected, Descending, ForegroundLayerObserved;
            public readonly Dictionary<int, (Vector2 Point, double At, float Error)> Turns = new();
        }
        private readonly record struct LetterSample(Bullet Shot, ulong Activation, Vector2 Origin,
            Vector2 Direction, double Born, int Group, int State, Vector2 TurnPoint, double Changed,
            Vector2 PreviousTurnPoint, double PreviousChanged);
        private readonly record struct StaticTarget(string Key, double Fire, double End, Vector2 A, Vector2 B, float Radius, int Shape);
        private sealed class RainStage
        {
            public readonly double?[] First = new double?[3];
            public readonly int[] Samples = new int[3];
            public int Last = -1;
            public double? FirstActual;
        }
        private sealed class RainParent
        {
            public int Wave, Row, Column, Burst;
            public Vector2 Origin, Impact;
            public double Born;
            public readonly Dictionary<int, (ulong Id, ulong Activation, double Born)> Children = new();
        }
        private readonly string _mechanic;
        private readonly WarningStageAudit? _stages;
        private readonly Dictionary<string, Forecast> _forecasts = new();
        private readonly Dictionary<(ulong Id, ulong Activation), ShotTrace> _traces = new();
        private readonly Dictionary<string, (Vector2 A, Vector2 B, float Radius, int Shape, double Time)> _staticWarnings = new();
        private readonly List<(Rect2 Bounds, double Time, string Key)> _boxWarnings = new();
        private readonly Dictionary<(int Wave, int Row), RainStage> _rainDropStages = new(), _rainImpactStages = new();
        private readonly Dictionary<(ulong Id, ulong Activation), RainParent> _rainParents = new();
        private readonly Dictionary<string, double> _rainFullForecastStarted = new();
        private int _expectedRainRows, _expectedRainParents;
        private float _maximumRainOriginError, _maximumRainImpactError;
        private const double RainFullHold = 2, RainFrameTolerance = 1d / 60;
        private Vector2[]? _clockOutline;
        private Vector2[]? _envelopePath;
        private float[] _envelopeCornerDistances = Array.Empty<float>();
        private double?[] _envelopeCornerReachedAt = Array.Empty<double?>();
        private int _envelopeRouteSamples, _envelopeNextCorner;
        private float _envelopeMaximumPathError;
        private double _clockWarningAt = -1, _lastTime = -1;
        private int _hazardSamples, _hazardProbes, _expectedShots, _requiredShots, _requiredBursts;
        private string? _failure;

        public WarningPathAudit(string mechanic, WarningStageAudit? stages) { _mechanic = mechanic; _stages = stages; }

        private bool Full(params string[] keys) => _stages == null || keys.All(_stages.Full);

        private string? ActualForecast(Forecast forecast, double time)
        {
            if (_stages == null) return null;
            string[] parts = forecast.Key.Split(':');
            return parts[0] switch
            {
                "delivery" => _stages.Actual($"delivery:{parts[1]}", time),
                "inbox" => _stages.Actual($"inbox:{parts[1]}", time),
                "unbinding" => _stages.Actual($"unbinding:{int.Parse(parts[1]) / 3}:retrace", time)
                    ?? _stages.Actual($"unbinding:{int.Parse(parts[1]) / 3}:exit", time),
                "envelope" => _stages.Actual($"envelope:opening:{parts[1]}", time),
                _ => null
            };
        }

        private static T LetterField<T>(object letter, string name)
            => (T)letter.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(letter)!;

        private static LetterSample[] Letters(BossCharacterTechnique attack)
            => Read<System.Collections.IEnumerable>(attack, "_letters").Cast<object>()
                .Select(letter => new LetterSample(LetterField<Bullet>(letter, "Shot"), LetterField<ulong>(letter, "Activation"),
                    LetterField<Vector2>(letter, "Origin"), LetterField<Vector2>(letter, "Direction"),
                    LetterField<double>(letter, "Born"), LetterField<int>(letter, "Group"), LetterField<int>(letter, "State"),
                    LetterField<Vector2>(letter, "TurnPoint"), LetterField<double>(letter, "Changed"),
                    LetterField<Vector2>(letter, "PreviousTurnPoint"), LetterField<double>(letter, "PreviousChanged"))).ToArray();

        private IEnumerable<StaticTarget> StaticTargets(BossCharacterTechnique attack)
        {
            if (_mechanic == "akari.carried-approval")
            {
                var stamps = Read<System.Collections.IEnumerable>(attack, "_stamps").Cast<object>().ToArray();
                for (int index = 0; index < stamps.Length; index++)
                {
                    double fire = Invoke<double>(attack, "EasyApprovalStampFire", index), end = Invoke<double>(attack, "EasyApprovalLeave", index);
                    var paths = Invoke<(Vector2 A, Vector2 B, float Radius)[]>(attack, "EasyApprovalStampPaths", index);
                    for (int path = 0; path < paths.Length; path++)
                        yield return new StaticTarget($"stamp:{index}:{path}", fire, end,
                            paths[path].A, paths[path].B, paths[path].Radius, 0);
                    Vector2 position = Invoke<Vector2>(attack, "EasyApprovalStampPosition", index);
                    yield return new StaticTarget($"stamp:{index}:disc", fire, end, position, position, 19, 1);
                }
            }
        }

        private void Remember(string key, double launch, double time, float halfWidth, params Vector2[] points)
        {
            if (time >= launch) return;
            _forecasts.TryAdd(key, new Forecast(key, launch, time, points, halfWidth));
        }

        private void CaptureForecasts(BossCharacterTechnique attack,
            List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)> hazards)
        {
            double time = attack.Elapsed, warning = Read<double>(attack, "_warning");
            void RememberVisible(string key, double launch, float width, params Vector2[] points)
            {
                for (int index = 1; index < points.Length; index++)
                {
                    Vector2 from = points[index - 1], to = points[index];
                    if (from.DistanceSquaredTo(to) < .0001f) continue;
                    if (!hazards.Any(hazard => !hazard.Live && hazard.Shape == 0 && Math.Abs(hazard.Radius - width) < .01f
                        && (hazard.A.DistanceTo(from) < .01f && hazard.B.DistanceTo(to) < .01f
                            || hazard.A.DistanceTo(to) < .01f && hazard.B.DistanceTo(from) < .01f)))
                    {
                        _failure ??= $"Complete projectile forecast has no matching displayed warning segment: {_mechanic}/{key}, from={from}, to={to}, time={time:F3}";
                        return;
                    }
                }
                Remember(key, launch, time, width, points);
            }
            switch (_mechanic)
            {
                case "akari.returned-express":
                    for (int batch = 0; batch < 3; batch++)
                    {
                        double launch = Invoke<double>(attack, "EasyDeliveryLaunch", batch);
                        if (time < Invoke<double>(attack, "EasyDeliveryWarningStart", batch) || time >= launch || !Full($"delivery:{batch}")) continue;
                        var columns = Invoke<IEnumerable<(Vector2 Start, Vector2 Exit)>>(attack, "EasyDeliveryWarningPaths", batch).ToArray();
                        var returned = Invoke<(Vector2 Start, Vector2 Turn, Vector2 Bounce, Vector2 Exit)[]>(attack, "EasyDeliveryReturnPaths", batch);
                        for (int column = 0; column < columns.Length; column++)
                        {
                            float width = Invoke<float>(attack, "EasyWarningHalfWidth", 1);
                            RememberVisible($"delivery:{batch}:ordinary:{column}", launch, width, columns[column].Start, columns[column].Exit);
                            var path = returned[column];
                            double delay = (path.Turn.Y - path.Start.Y) * (1.0 / 27 - 1.0 / 88);
                            RememberVisible($"delivery:{batch}:express:{column}", launch + delay, width, path.Start, path.Turn, path.Bounce, path.Exit);
                        }
                    }
                    break;
                case "akari.alternating-inboxes":
                    for (int turn = 0; turn < 4; turn++)
                    {
                        double launch = Invoke<double>(attack, "EasyInboxLaunch", turn);
                        if (time < launch - Constant<double>(attack, "EasyInboxWarningLead") || time >= launch || !Full($"inbox:{turn}")) continue;
                        var inbox = Invoke<IEnumerable<(Vector2 Start, Vector2 Exit)>>(attack, "EasyInboxWarningPaths", turn).ToArray();
                        for (int row = 0; row < inbox.Length; row++)
                            RememberVisible($"inbox:{turn}:{row}", launch, Invoke<float>(attack, "EasyWarningHalfWidth", 1), inbox[row].Start, inbox[row].Exit);
                    }
                    break;
                case "akari.bound-draft-retraction":
                    int round = Property<int>(attack, "EasyUnbindingRound");
                    if (round < 0 || !Full($"unbinding:{round}:retrace", $"unbinding:{round}:exit")) break;
                    int first = round * Constant<int>(attack, "EasyUnbindingCopiesPerRound");
                    if (time >= Invoke<double>(attack, "EasyUnbindingFire", first)) break;
                    for (int copy = first; copy < first + Constant<int>(attack, "EasyUnbindingCopiesPerRound"); copy++)
                        RememberVisible($"unbinding:{copy}", Invoke<double>(attack, "EasyUnbindingFire", copy),
                            Invoke<float>(attack, "EasyWarningHalfWidth", 2), Invoke<Vector2[]>(attack, "EasyUnbindingForecastPath", copy));
                    break;
                case "akari.rain-splash":
                    CaptureRainForecasts(attack);
                    break;
                case "akari.carried-approval":
                    int stampCount = Read<System.Collections.IEnumerable>(attack, "_stamps").Cast<object>().Count();
                    for (int opening = 0; opening < stampCount; opening++)
                    {
                        double launch = Invoke<double>(attack, "EasyApprovalOpeningFire", opening);
                        if (time < Invoke<double>(attack, "EasyApprovalWarningStart", opening) || time >= launch || !Full($"envelope:opening:{opening}")) continue;
                        var rows = Invoke<IEnumerable<(Vector2 Start, Vector2 Exit)>>(attack, "EasyEnvelopeOpeningPaths", opening).ToArray();
                        for (int row = 0; row < rows.Length; row++)
                            RememberVisible($"envelope:{opening}:{row}", launch, Invoke<float>(attack, "EasyWarningHalfWidth", 2), rows[row].Start, rows[row].Exit);
                    }
                    if (time < warning && Full("envelope:body"))
                    {
                        if (_envelopePath == null)
                        {
                            _envelopePath = Invoke<Vector2[]>(attack, "EasyEnvelopeTravelPath");
                            _envelopeCornerDistances = Enumerable.Repeat(float.PositiveInfinity, _envelopePath.Length - 1).ToArray();
                            _envelopeCornerReachedAt = new double?[_envelopeCornerDistances.Length];
                        }
                        foreach (var bounds in Invoke<IEnumerable<Rect2>>(attack, "EasyEnvelopeWarningBoxes", 1f))
                            if (hazards.Any(hazard => !hazard.Live && hazard.Shape == 2
                                && hazard.A.DistanceTo(bounds.Position) < .01f && hazard.B.DistanceTo(bounds.End) < .01f))
                                RememberBox(bounds, time, "envelope:body");
                    }
                    break;
                case "akari.extending-clock" when time < warning && Full("clock"):
                    _clockOutline ??= Invoke<Vector2[]>(attack, "EasyClockWarningOutline");
                    if (_clockWarningAt < 0) _clockWarningAt = time;
                    break;
            }
            if (_mechanic == "akari.carried-approval")
                foreach (var target in StaticTargets(attack).Where(target => time < target.Fire))
                {
                    if (!Full(target.Key)) continue;
                    var matching = hazards.Where(hazard => !hazard.Live && hazard.Shape == target.Shape && Math.Abs(hazard.Radius - target.Radius) < .01f
                        && (hazard.A.DistanceTo(target.A) < .01f && hazard.B.DistanceTo(target.B) < .01f
                            || target.Shape == 0 && hazard.A.DistanceTo(target.B) < .01f && hazard.B.DistanceTo(target.A) < .01f)).ToArray();
                    if (matching.Length > 0)
                    {
                        var hazard = matching[0];
                        _staticWarnings.TryAdd(target.Key, (target.A, target.B, hazard.Radius, hazard.Shape, time));
                    }
                }
            if (_mechanic == "akari.filing-piles")
            {
                int pass = Property<int>(attack, "EasyPilePass");
                float width = Constant<float>(attack, "EasyPileWidth");
                for (int column = 0; column < Property<int>(attack, "EasyPileColumnCount"); column++)
                {
                    string key = $"pile:{pass}:{column}";
                    if (!Full(key)) continue;
                    var bounds = new Rect2(Invoke<float>(attack, "EasyPileX", column) - width / 2,
                        Field.Top, width, Field.Height).Intersection(Field.Rect);
                    if (hazards.Any(hazard => !hazard.Live && hazard.Shape == 2
                        && hazard.A.DistanceTo(bounds.Position) < .01f && hazard.B.DistanceTo(bounds.End) < .01f))
                        RememberBox(bounds, time, key);
                }
            }
        }

        private void RememberBox(Rect2 bounds, double time, string key)
        {
            if (!_boxWarnings.Any(saved => saved.Bounds == bounds && saved.Key == key)) _boxWarnings.Add((bounds, time, key));
        }

        private void ObserveRainStage(Dictionary<(int Wave, int Row), RainStage> stages, int wave, int row,
            float progress, double time, string name)
        {
            if (!stages.TryGetValue((wave, row), out var stage))
                stages.Add((wave, row), stage = new RainStage());
            int current = progress <= 0 ? 0 : progress >= 1 ? 2 : 1;
            if (current < stage.Last || current > stage.Last + 1)
            {
                _failure ??= $"Missing or reversed rain warning stage: {name}, wave={wave}, row={row}, previous={stage.Last}, current={current}, time={time:F3}";
                return;
            }
            stage.Last = current;
            stage.First[current] ??= time;
            stage.Samples[current]++;
        }

        private void CaptureRainForecasts(BossCharacterTechnique attack)
        {
            double time = attack.Elapsed;
            int rows = Constant<int>(attack, "EasyRainRows"), columns = Constant<int>(attack, "EasyRainColumns");
            _expectedRainRows = Constant<int>(attack, "EasyRainWaves") * rows;
            _expectedRainParents = _expectedRainRows * columns * Property<int>(attack, "EasyRainBursts");
            float width = Invoke<float>(attack, "EasyWarningHalfWidth", 0);
            int next = Property<int>(attack, "EasyRainNextRow");
            if (next >= 0)
            {
                int wave = next / rows, row = next % rows;
                float progress = Invoke<float>(attack, "EasyRainDropWarningProgress", wave, row);
                ObserveRainStage(_rainDropStages, wave, row, progress, time, "drop");
                if (progress >= 1)
                    for (int column = 0; column < columns; column++)
                        Remember($"rain:{wave}:{row}:{column}:drop", Invoke<double>(attack, "EasyRainLaunch", wave, row),
                            time, width, Invoke<Vector2>(attack, "EasyRainOrigin", wave, row, column),
                            Invoke<Vector2>(attack, "EasyRainImpact", wave, row, column));
            }
            var warning = Invoke<(int Wave, int Row, double Remaining, float Progress, Vector2[] Points)?>(attack, "EasyRainImpactWarning");
            if (warning is not { } impactWarning) return;
            ObserveRainStage(_rainImpactStages, impactWarning.Wave, impactWarning.Row, impactWarning.Progress, time, "impact");
            double rowLaunch = Invoke<double>(attack, "EasyRainLaunch", impactWarning.Wave, impactWarning.Row);
            var candidates = Letters(attack).Where(letter => letter.Group == impactWarning.Wave && letter.State == 0
                && letter.Shot.Active && letter.Shot.ActivationId == letter.Activation && !letter.Shot.LeadIn
                && letter.Born >= rowLaunch - RainFrameTolerance - 1e-6
                && letter.Born <= rowLaunch + (Property<int>(attack, "EasyRainBursts") - 1)
                    * Property<double>(attack, "EasyRainBurstInterval") + RainFrameTolerance + 1e-6).ToArray();
            double firstBorn = candidates.Select(letter => letter.Born).DefaultIfEmpty(-1).Min();
            var falling = candidates.Where(letter => Math.Abs(letter.Born - firstBorn) < 1e-6).ToArray();
            if (impactWarning.Points.Length == 0 || impactWarning.Points.Length != falling.Length)
            {
                _failure ??= $"Impact warning includes a missing or already landed rain column: wave={impactWarning.Wave}, row={impactWarning.Row}, points={impactWarning.Points.Length}, falling={falling.Length}";
                return;
            }
            var seenColumns = new HashSet<int>();
            foreach (Vector2 point in impactWarning.Points)
            {
                int column = Enumerable.Range(0, columns).OrderBy(index => Math.Abs(
                    Invoke<Vector2>(attack, "EasyRainOrigin", impactWarning.Wave, impactWarning.Row, index).X - point.X)).First();
                Vector2 expected = Invoke<Vector2>(attack, "EasyRainImpact", impactWarning.Wave, impactWarning.Row, column);
                if (!seenColumns.Add(column) || point.DistanceTo(expected) > .01f
                    || !falling.Any(letter => Math.Abs(letter.Shot.GlobalPosition.X - point.X) <= .01f))
                {
                    _failure ??= $"Impact marker differs from its live falling column: wave={impactWarning.Wave}, row={impactWarning.Row}, column={column}, actual={point}, expected={expected}";
                    return;
                }
                if (impactWarning.Progress < 1) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    string key = $"rain:{impactWarning.Wave}:{impactWarning.Row}:{column}:{side}";
                    Remember(key, time + impactWarning.Remaining, time, width,
                        Invoke<Vector2[]>(attack, "EasyRainSplashWarningPath", point, side));
                    _rainFullForecastStarted.TryAdd(key, time);
                }
            }
        }

        private LetterSample? ParentRainDrop(LetterSample letter, LetterSample[] letters)
        {
            if (letter.Group != 10) return letter;
            return letters.Where(item => item.Group is 0 or 1 && item.State == 1 && item.Born < letter.Born
                    && Math.Abs(item.Origin.X - letter.Origin.X) < .01f
                    && _traces.TryGetValue((item.Shot.GetInstanceId(), item.Activation), out var trace)
                    && trace.RetiredAt is { } impactAt && Math.Abs(impactAt - letter.Born) <= RainFrameTolerance + 1e-6)
                .OrderByDescending(item => item.Born).Select(item => (LetterSample?)item).FirstOrDefault();
        }

        private static (int Wave, int Row, int Column, int Burst) RainSlot(BossCharacterTechnique attack, LetterSample parent)
        {
            int wave = parent.Group;
            int row = Enumerable.Range(0, Constant<int>(attack, "EasyRainRows"))
                .OrderBy(index => Math.Abs(Invoke<double>(attack, "EasyRainLaunch", wave, index) - parent.Born)).First();
            int column = Enumerable.Range(0, Constant<int>(attack, "EasyRainColumns"))
                .OrderBy(index => Math.Abs(Invoke<Vector2>(attack, "EasyRainOrigin", wave, row, index).X - parent.Origin.X)).First();
            int burst = Math.Clamp((int)Math.Round((parent.Born - Invoke<double>(attack, "EasyRainLaunch", wave, row))
                / Property<double>(attack, "EasyRainBurstInterval")), 0, Property<int>(attack, "EasyRainBursts") - 1);
            return (wave, row, column, burst);
        }

        private string? ObserveRainGeneration(BossCharacterTechnique attack, LetterSample letter, LetterSample[] letters)
        {
            var parent = ParentRainDrop(letter, letters);
            if (parent == null) return "A rain splash has no corresponding landed parent drop.";
            var slot = RainSlot(attack, parent.Value);
            var parentId = (parent.Value.Shot.GetInstanceId(), parent.Value.Activation);
            if (letter.Group != 10)
            {
                Vector2 origin = Invoke<Vector2>(attack, "EasyRainOrigin", slot.Wave, slot.Row, slot.Column);
                float error = letter.Origin.DistanceTo(origin);
                _maximumRainOriginError = Math.Max(_maximumRainOriginError, error);
                if (error > .01f)
                    return $"Rain spawn differs from its shared marker origin: wave={slot.Wave}, row={slot.Row}, column={slot.Column}, actual={letter.Origin}, expected={origin}";
                if (!_rainDropStages.TryGetValue((slot.Wave, slot.Row), out var stage) || stage.First[2] == null
                    || letter.Born - stage.First[2]!.Value + RainFrameTolerance + 1e-6 < RainFullHold)
                    return $"Rain drop launched without two seconds of complete warning: wave={slot.Wave}, row={slot.Row}";
                stage.FirstActual = stage.FirstActual == null ? letter.Born : Math.Min(stage.FirstActual.Value, letter.Born);
                _rainParents.Add(parentId, new RainParent { Wave = slot.Wave, Row = slot.Row, Column = slot.Column, Burst = slot.Burst,
                    Origin = letter.Origin, Impact = Invoke<Vector2>(attack, "EasyRainImpact", slot.Wave, slot.Row, slot.Column), Born = letter.Born });
                return null;
            }
            if (!_rainParents.TryGetValue(parentId, out var generation)) return "A rain splash parent was not observed before impact.";
            float impactError = letter.Origin.DistanceTo(generation.Impact);
            _maximumRainImpactError = Math.Max(_maximumRainImpactError, impactError);
            if (impactError > .01f)
                return $"Rain splash spawn differs from its parent's impact marker: actual={letter.Origin}, expected={generation.Impact}";
            int side = Math.Sign(letter.Direction.X);
            if (side is not (-1 or 1) || Math.Abs(letter.Direction.Y) > .01f || generation.Children.ContainsKey(side))
                return $"A parent rain drop did not emit a unique left/right splash: wave={slot.Wave}, row={slot.Row}, column={slot.Column}, side={side}";
            if (generation.Children.Values.Any(child => Math.Abs(child.Born - letter.Born) > 1e-6))
                return $"A parent rain drop emitted its two splashes at different times: wave={slot.Wave}, row={slot.Row}, column={slot.Column}";
            string key = $"rain:{slot.Wave}:{slot.Row}:{slot.Column}:{side}";
            if (!_rainFullForecastStarted.TryGetValue(key, out double fullAt)
                || letter.Born - fullAt + RainFrameTolerance + 1e-6 < RainFullHold)
                return $"Full splash warning was held for less than two seconds: {key}, full_at={fullAt:F3}, spawn_at={letter.Born:F3}";
            generation.Children.Add(side, (letter.Shot.GetInstanceId(), letter.Activation, letter.Born));
            if (!_rainImpactStages.TryGetValue((slot.Wave, slot.Row), out var impactStage))
                return "A rain splash appeared without staged impact warning observations.";
            impactStage.FirstActual = impactStage.FirstActual == null ? letter.Born : Math.Min(impactStage.FirstActual.Value, letter.Born);
            return null;
        }

        private Forecast? ClosestForecast(string prefix, LetterSample letter)
        {
            var candidates = _forecasts.Values.Where(forecast => forecast.Key.StartsWith(prefix)
                && forecast.ObservedAt < letter.Born && forecast.Points[0].DistanceTo(letter.Origin) < 1.2f);
            if (letter.Direction.LengthSquared() > .01f)
                candidates = candidates.Where(forecast => forecast.Points[0].DirectionTo(forecast.Points[1]).Dot(letter.Direction.Normalized()) > .999f);
            return candidates.OrderBy(forecast => forecast.Points[0].DistanceSquaredTo(letter.Origin))
                .ThenByDescending(forecast => forecast.Points[0].DirectionTo(forecast.Points[1]).Dot(letter.Direction.Normalized())).FirstOrDefault();
        }

        private Forecast? MatchForecast(BossCharacterTechnique attack, LetterSample letter, LetterSample[] letters)
        {
            switch (_mechanic)
            {
                case "akari.returned-express":
                    bool ordinary = letter.Group >= 100;
                    int slot = letter.Group % 100 % 9;
                    return _forecasts.GetValueOrDefault($"delivery:{slot / 3}:{(ordinary ? "ordinary" : "express")}:{slot % 3}");
                case "akari.alternating-inboxes":
                    return ClosestForecast($"inbox:{letter.Group}:", letter);
                case "akari.bound-draft-retraction":
                    return _forecasts.GetValueOrDefault($"unbinding:{Invoke<int>(attack, "EasyUnbindingCopyForGroup", letter.Group)}");
                case "akari.rain-splash":
                    var parent = ParentRainDrop(letter, letters);
                    if (parent == null) return null;
                    var rainSlot = RainSlot(attack, parent.Value);
                    string part = letter.Group == 10 ? Math.Sign(letter.Direction.X).ToString() : "drop";
                    return _forecasts.GetValueOrDefault($"rain:{rainSlot.Wave}:{rainSlot.Row}:{rainSlot.Column}:{part}");
                case "akari.carried-approval":
                    return ClosestForecast($"envelope:{letter.Group}:", letter);
                default: return null;
            }
        }

        private static float PathDistance(Vector2 point, Vector2[] points)
        {
            float distance = float.PositiveInfinity;
            for (int i = 1; i < points.Length; i++)
                distance = Math.Min(distance, point.DistanceTo(Geometry2D.GetClosestPointToSegment(point, points[i - 1], points[i])));
            return distance;
        }

        private static bool InsidePolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            // Half-open crossings avoid double-counting shared vertices on the densely sampled inner clock rim.
            for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
            {
                Vector2 a = polygon[previous], b = polygon[i];
                if ((a.Y > point.Y) == (b.Y > point.Y)) continue;
                double crossing = ((double)b.X - a.X) * ((double)point.Y - a.Y) / ((double)b.Y - a.Y) + a.X;
                if (point.X < crossing) inside = !inside;
            }
            return inside;
        }

        private string? SampleMovement(ShotTrace trace, Vector2 position)
        {
            Vector2 previous = trace.LastPosition ?? trace.Origin;
            int steps = Math.Max(1, Mathf.CeilToInt(previous.DistanceTo(position)));
            float tolerance = _mechanic == "akari.rain-splash" && trace.Group == 10 ? 1f : .6f;
            for (int sample = 0; sample <= steps; sample++)
            {
                Vector2 point = previous.Lerp(position, sample / (float)steps);
                if (!Field.Rect.HasPoint(point)) continue;
                float distance = PathDistance(point, trace.Forecast.Points);
                trace.MaximumError = Math.Max(trace.MaximumError, distance);
                trace.Probes++;
                if (distance > tolerance)
                    return $"Unwarned projectile trajectory: {_mechanic}/{trace.Forecast.Key}, point={point}, distance={distance:F4}, tolerance={tolerance}, born={trace.Born:F3}, warning={trace.Forecast.ObservedAt:F3}";
            }
            trace.LastPosition = position;
            return null;
        }

        private string? ObserveTurn(ShotTrace trace, int index, Vector2 point, double at, Vector2 expected, double time)
        {
            if (trace.Turns.TryGetValue(index, out var recorded))
                return recorded.Point.DistanceTo(point) > .01f || Math.Abs(recorded.At - at) > .00001
                    ? $"Recorded turning event changed after observation: {_mechanic}/{trace.Forecast.Key}, turn={index}" : null;
            float error = point.DistanceTo(expected);
            if (error > .01f || at < trace.Born || at > time + .00001
                || index > 0 && (!trace.Turns.TryGetValue(index - 1, out var previous) || previous.At > at))
                return $"Actual turning point differs from its prior forecast: {_mechanic}/{trace.Forecast.Key}, turn={index}, error={error:F4}, changed={at:F3}";
            trace.Turns.Add(index, (point, at, error));
            trace.TurnPoint = point;
            trace.TurnPointError = Math.Max(trace.TurnPointError ?? 0, error);
            trace.TurnAt = at;
            return null;
        }

        private string? ObserveShots(BossCharacterTechnique attack, LetterSample[] letters)
        {
            foreach (var letter in letters)
            {
                var identity = (letter.Shot.GetInstanceId(), letter.Activation);
                if (!_traces.TryGetValue(identity, out var trace))
                {
                    var forecast = MatchForecast(attack, letter, letters);
                    if (forecast == null || forecast.ObservedAt >= letter.Born)
                        return $"Missing pre-spawn full-path forecast: {_mechanic}, born={letter.Born:F3}, group={letter.Group}, origin={letter.Origin}";
                    if (forecast.LaunchAt - forecast.ObservedAt + RainFrameTolerance + 1e-6 < RainFullHold)
                        return $"Planned projectile launch does not retain two seconds of complete warning: {_mechanic}/{forecast.Key}, full={forecast.ObservedAt:F3}, launch={forecast.LaunchAt:F3}";
                    if (ActualForecast(forecast, letter.Born) is { } stageFailure) return stageFailure;
                    if (_mechanic == "akari.rain-splash")
                    {
                        string? generationFailure = ObserveRainGeneration(attack, letter, letters);
                        if (generationFailure != null) return generationFailure;
                    }
                    trace = new ShotTrace { Forecast = forecast, Born = letter.Born, Group = letter.Group, Origin = letter.Origin };
                    _traces.Add(identity, trace);
                }
                if (trace.Retired) continue;
                bool alive = letter.Shot.Active && letter.Shot.ActivationId == letter.Activation;
                if (!alive)
                {
                    if (trace.ActiveFrames == 0 || trace.LastPosition == null)
                        return $"Projectile never produced an observed live trajectory: {_mechanic}/{trace.Forecast.Key}";
                    if (letter.Shot.ActivationId == letter.Activation)
                    {
                        string? movementFailure = SampleMovement(trace, letter.Shot.GlobalPosition);
                        if (movementFailure != null) return movementFailure;
                    }
                    if (_mechanic == "akari.rain-splash" && trace.Group != 10)
                    {
                        if (trace.LastPosition.Value.DistanceTo(trace.Forecast.Points[^1]) > 2.5f)
                            return $"Rain drop disappeared before its forecast impact: {trace.Forecast.Key}, last={trace.LastPosition}";
                    }
                    else if (Field.Rect.Grow(trace.Radius).HasPoint(trace.LastPosition.Value))
                        return $"Projectile disappeared before leaving the field: {_mechanic}/{trace.Forecast.Key}, last={trace.LastPosition}";
                    trace.Retired = true;
                    trace.RetiredAt = attack.Elapsed;
                    continue;
                }
                if (letter.Shot.LeadIn || letter.Shot.CollisionLayer == 0) continue;
                trace.FirstLiveAt ??= attack.Elapsed;
                if (_mechanic == "akari.returned-express" && letter.Group < 100 && letter.State > 0)
                {
                    Vector2 firstPoint = letter.State == 1 ? letter.TurnPoint : letter.PreviousTurnPoint;
                    double firstAt = letter.State == 1 ? letter.Changed : letter.PreviousChanged;
                    if (ObserveTurn(trace, 0, firstPoint, firstAt, trace.Forecast.Points[1], attack.Elapsed) is { } firstFailure)
                        return firstFailure;
                    trace.FirstReleaseAt ??= firstAt;
                    int outgoing = letter.State == 1 ? 1 : 2;
                    Vector2 expectedVelocity = trace.Forecast.Points[outgoing].DirectionTo(trace.Forecast.Points[outgoing + 1]) * 78;
                    if (letter.Shot.Velocity.DistanceTo(expectedVelocity) > .01f)
                        return $"Returned express velocity differs from its forecast direction: {trace.Forecast.Key}, state={letter.State}, actual={letter.Shot.Velocity}, expected={expectedVelocity}";
                    if (letter.State == 1 && trace.FirstTurnStateObservedAt == null)
                    {
                        if (attack.Elapsed - letter.Changed > RainFrameTolerance + 1e-6)
                            return $"Returned express first turn was not observed at its event: {trace.Forecast.Key}";
                        trace.FirstTurnStateObservedAt = attack.Elapsed;
                    }
                    if (letter.State >= 2)
                    {
                        if (ObserveTurn(trace, 1, letter.TurnPoint, letter.Changed, trace.Forecast.Points[2], attack.Elapsed) is { } turnFailure)
                            return turnFailure;
                        trace.FirstReflectionAt ??= letter.Changed;
                        if (trace.ReflectionStateObservedAt == null)
                        {
                            if (attack.Elapsed - letter.Changed > RainFrameTolerance + 1e-6)
                                return $"Returned express wall reflection was not observed at its event: {trace.Forecast.Key}";
                            trace.ReflectionStateObservedAt = attack.Elapsed;
                        }
                        trace.Reflected = true;
                    }
                }
                else if (_mechanic == "akari.bound-draft-retraction" && letter.State > 0)
                {
                    if (ObserveTurn(trace, 0, letter.TurnPoint, letter.Changed, trace.Forecast.Points[^2], attack.Elapsed) is { } turnFailure)
                        return turnFailure;
                    trace.FirstReleaseAt ??= letter.Changed;
                }
                if (letter.Shot.ZAsRelative || letter.Shot.ZIndex <= attack.ZIndex)
                    return $"Projectile is not in front of its warning layer: {_mechanic}/{trace.Forecast.Key}, relative={letter.Shot.ZAsRelative}, projectile_z={letter.Shot.ZIndex}, warning_z={attack.ZIndex}";
                trace.ForegroundLayerObserved = true;
                trace.Radius = letter.Shot.CollisionRadius * Math.Max(letter.Shot.GlobalTransform.X.Length(), letter.Shot.GlobalTransform.Y.Length());
                float margin = trace.Forecast.HalfWidth - trace.Radius;
                trace.MinimumWidthMargin = Math.Min(trace.MinimumWidthMargin, margin);
                if (margin < -.01f)
                    return $"Forecast is narrower than its actual projectile footprint: {_mechanic}/{trace.Forecast.Key}, margin={margin:F4}";
                string? failure;
                if (_mechanic == "akari.bound-draft-retraction" && letter.State == 0)
                {
                    float distance = PathDistance(letter.Shot.GlobalPosition, trace.Forecast.Points);
                    trace.MaximumError = Math.Max(trace.MaximumError, distance);
                    trace.Probes++;
                    trace.LastPosition = letter.Shot.GlobalPosition;
                    int copy = Invoke<int>(attack, "EasyUnbindingCopyForGroup", letter.Group);
                    int burst = Invoke<int>(attack, "EasyUnbindingBurstForGroup", letter.Group);
                    double fire = Invoke<double>(attack, "EasyUnbindingBurstFire", copy, burst);
                    double lead = Constant<double>(attack, "EasyUnbindingLeadTime"), duration = Constant<double>(attack, "EasyUnbindingRetraceTime");
                    float progress = Mathf.Clamp((float)((attack.Elapsed - fire - lead) / duration), 0, 1);
                    Vector2 expected = Invoke<Vector2>(attack, "EasyUnbindingRetracePoint", copy, progress);
                    failure = distance > .6f || letter.Shot.GlobalPosition.DistanceTo(expected) > .01f
                        ? $"Retracing clip differs from its complete warned path or recorded timing: copy={letter.Group}, time={attack.Elapsed:F3}, error={distance:F4}" : null;
                }
                else failure = SampleMovement(trace, letter.Shot.GlobalPosition);
                if (failure != null) return failure;
                trace.ActiveFrames++;
                trace.Descending |= _mechanic == "akari.rain-splash" && letter.Group == 10 && letter.Shot.Velocity.Y > 0;
            }
            return null;
        }

        private string? ObserveHazards(BossCharacterTechnique attack,
            List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)> hazards)
        {
            if (_mechanic == "akari.carried-approval")
            {
                var targets = StaticTargets(attack).Where(target => attack.Elapsed >= target.Fire && attack.Elapsed < target.End).ToArray();
                var live = hazards.Where(hazard => hazard.Live && hazard.Shape != 2).ToArray();
                if (targets.Length != live.Length)
                    return $"Stationary hazard occurrence was omitted: {_mechanic}, expected={targets.Length}, actual={live.Length}";
                foreach (var target in targets)
                {
                    var matching = live.Where(hazard => hazard.Shape == target.Shape && Math.Abs(hazard.Radius - target.Radius) < .01f && hazard.A.DistanceTo(target.A) < .01f
                        && hazard.B.DistanceTo(target.B) < .01f).ToArray();
                    if (matching.Length == 0 || !_staticWarnings.TryGetValue(target.Key, out var saved)
                        || target.Fire - saved.Time + RainFrameTolerance + 1e-6 < RainFullHold
                        || saved.Shape != matching[0].Shape || saved.A.DistanceTo(matching[0].A) >= .01f
                        || saved.B.DistanceTo(matching[0].B) >= .01f || Math.Abs(saved.Radius - matching[0].Radius) >= .01f)
                        return $"Live stationary hazard differs from its occurrence's prior warning: {_mechanic}/{target.Key}";
                    if (_stages?.Actual(target.Key, attack.Elapsed) is { } stageFailure) return stageFailure;
                    string prefix = $"stamp:{target.Key.Split(':')[1]}:";
                    if (_stages?.Actual(prefix + "frame", attack.Elapsed) is { } frameFailure) return frameFailure;
                    if (_stages?.Actual(prefix + "check", attack.Elapsed) is { } checkFailure) return checkFailure;
                    _hazardSamples++;
                }
            }
            foreach (var hazard in hazards.Where(hazard => hazard.Live))
            {
                if (hazard.Shape == 2 && _mechanic is ("akari.filing-piles" or "akari.carried-approval"))
                {
                    Rect2 rawBounds = new(hazard.A, hazard.B - hazard.A);
                    Rect2 bounds = rawBounds.Intersection(Field.Rect);
                    string key = "envelope:body";
                    if (_mechanic == "akari.filing-piles")
                    {
                        int column = Enumerable.Range(0, Property<int>(attack, "EasyPileColumnCount"))
                            .OrderBy(index => Math.Abs(Invoke<float>(attack, "EasyPileX", index) - bounds.GetCenter().X)).First();
                        key = $"pile:{Property<int>(attack, "EasyPilePass")}:{column}";
                    }
                    else
                    {
                        if (_envelopePath == null) return "Missing pre-launch envelope travel path.";
                        Vector2 center = rawBounds.GetCenter();
                        float error = PathDistance(center, _envelopePath);
                        _envelopeMaximumPathError = Math.Max(_envelopeMaximumPathError, error);
                        if (error > .01f || rawBounds.Size.DistanceTo(new Vector2(36, 22)) > .01f)
                            return $"Envelope body differs from its pre-warned travel path or footprint: center={center}, error={error:F4}";
                        if (_envelopeNextCorner < _envelopeCornerDistances.Length)
                        {
                            int corner = _envelopeNextCorner;
                            float distance = center.DistanceTo(_envelopePath[corner]);
                            _envelopeCornerDistances[corner] = Math.Min(_envelopeCornerDistances[corner], distance);
                            if (distance <= 1.2f)
                            {
                                _envelopeCornerReachedAt[corner] = attack.Elapsed;
                                _envelopeNextCorner++;
                            }
                        }
                        _envelopeRouteSamples++;
                    }
                    if (!_boxWarnings.Any(saved => saved.Key == key && attack.Elapsed - saved.Time + RainFrameTolerance + 1e-6 >= RainFullHold
                        && saved.Bounds.Grow(.01f).Encloses(bounds)))
                        return $"Moving hazard left its pre-warned swept bounds: {_mechanic}, bounds={bounds}";
                    if (_stages?.Actual(key, attack.Elapsed) is { } stageFailure) return stageFailure;
                    _hazardSamples++;
                    _hazardProbes += 4;
                }
                else if (_mechanic == "akari.extending-clock")
                {
                    if (_clockOutline == null || Read<double>(attack, "_warning") - _clockWarningAt + RainFrameTolerance + 1e-6 < RainFullHold)
                        return "Missing pre-launch clock swept-area outline.";
                    if (_stages?.Actual("clock", attack.Elapsed) is { } stageFailure) return stageFailure;
                    int samples = Math.Max(1, Mathf.CeilToInt(hazard.A.DistanceTo(hazard.B)));
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        Vector2 point = hazard.A.Lerp(hazard.B, sample / (float)samples);
                        if (!Field.Rect.HasPoint(point)) continue;
                        _hazardProbes++;
                        if (!InsidePolygon(point, _clockOutline) && PathDistance(point, _clockOutline) > .6f)
                            return $"Clock hand left its pre-warned swept area: point={point}, time={attack.Elapsed:F3}";
                    }
                    _hazardSamples++;
                }
            }
            return null;
        }

        public string? Observe(BossCharacterTechnique attack, bool captureForecasts = true)
        {
            if (_failure != null || captureForecasts && attack.Elapsed == _lastTime) return _failure;
            _lastTime = attack.Elapsed;
            _expectedShots = attack.ShotsCreated;
            _requiredBursts = _mechanic switch
            {
                "akari.returned-express" => Property<int>(attack, "EasyDeliveryBursts"),
                "akari.alternating-inboxes" => Property<int>(attack, "EasyInboxBursts"),
                "akari.bound-draft-retraction" => Property<int>(attack, "EasyUnbindingBurstCount"),
                "akari.rain-splash" => Property<int>(attack, "EasyRainBursts"),
                "akari.carried-approval" => Property<int>(attack, "EasyApprovalBursts"),
                _ => 0,
            };
            _requiredShots = _mechanic switch
            {
                "akari.returned-express" => 18 * _requiredBursts,
                "akari.alternating-inboxes" => 28 * _requiredBursts,
                "akari.bound-draft-retraction" => Constant<int>(attack, "EasyUnbindingRounds")
                    * Constant<int>(attack, "EasyUnbindingCopiesPerRound") * _requiredBursts,
                "akari.rain-splash" => Constant<int>(attack, "EasyRainWaves") * Constant<int>(attack, "EasyRainRows")
                    * Constant<int>(attack, "EasyRainColumns") * 3 * _requiredBursts,
                "akari.carried-approval" => 20 * _requiredBursts,
                _ => 0,
            };
            var hazards = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(
                attack, "_hazards", typeof(BossCharacterTechnique));
            if (captureForecasts) CaptureForecasts(attack, hazards);
            if (_failure != null) return _failure;
            _failure = ObserveShots(attack, Letters(attack)) ?? ObserveHazards(attack, hazards);
            return _failure;
        }

        private string? RainCompletionFailure(int expectedShots)
        {
            if (_mechanic != "akari.rain-splash") return null;
            if (_expectedRainRows == 0 || _expectedRainParents == 0 || expectedShots != _expectedRainParents * 3)
                return $"Missing rain families: expected_rows={_expectedRainRows}, expected_parents={_expectedRainParents}, actual_shots={expectedShots}";
            foreach (var sequence in new[] { (Name: "drop", Stages: _rainDropStages), (Name: "impact", Stages: _rainImpactStages) })
            {
                if (sequence.Stages.Count != _expectedRainRows)
                    return $"Missing rain {sequence.Name} warning rows: expected={_expectedRainRows}, observed={sequence.Stages.Count}";
                foreach (var (slot, stage) in sequence.Stages)
                {
                    if (stage.Last != 2 || stage.First.Any(time => time == null) || stage.Samples.Any(samples => samples == 0)
                        || stage.FirstActual == null)
                        return $"Incomplete rain {sequence.Name} stage sequence: wave={slot.Wave}, row={slot.Row}";
                    if (stage.First[0]!.Value >= stage.First[1]!.Value || stage.First[1]!.Value >= stage.First[2]!.Value
                        || stage.First[2]!.Value >= stage.FirstActual.Value)
                        return $"Rain {sequence.Name} stages did not precede their attack in order: wave={slot.Wave}, row={slot.Row}";
                    if (stage.FirstActual.Value - stage.First[2]!.Value + RainFrameTolerance + 1e-6 < RainFullHold)
                        return $"Rain full {sequence.Name} warning was not held for two seconds before attack: wave={slot.Wave}, row={slot.Row}";
                }
            }
            if (_rainParents.Count != _expectedRainParents
                || _rainParents.Values.Select(parent => (parent.Wave, parent.Row, parent.Column, parent.Burst)).Distinct().Count() != _expectedRainParents)
                return $"Missing or repeated rain parent slots: expected={_expectedRainParents}, observed={_rainParents.Count}";
            foreach (var parent in _rainParents.Values)
            {
                if (parent.Children.Count != 2 || !parent.Children.TryGetValue(-1, out var left)
                    || !parent.Children.TryGetValue(1, out var right) || Math.Abs(left.Born - right.Born) > 1e-6)
                    return $"Missing simultaneous left/right rain splash pair: wave={parent.Wave}, row={parent.Row}, column={parent.Column}";
                foreach (var (side, child) in parent.Children)
                    if (!_rainFullForecastStarted.TryGetValue($"rain:{parent.Wave}:{parent.Row}:{parent.Column}:{side}", out double fullAt)
                        || child.Born - fullAt + RainFrameTolerance + 1e-6 < RainFullHold)
                        return $"Missing full splash warning hold: wave={parent.Wave}, row={parent.Row}, column={parent.Column}, side={side}";
            }
            if (_maximumRainOriginError > .01f || _maximumRainImpactError > .01f)
                return "Rain spawn or impact differs from its shared warning anchor.";
            return null;
        }

        private object? RainStageResult()
        {
            if (_mechanic != "akari.rain-splash") return null;
            string? completionFailure = RainCompletionFailure(_expectedShots);
            return new
            {
                @checked = true, passed = _failure == null && completionFailure == null,
                failure = _failure ?? completionFailure,
                expected_rows = _expectedRainRows, expected_parent_drops = _expectedRainParents,
                observed_drop_warning_rows = _rainDropStages.Count, observed_impact_warning_rows = _rainImpactStages.Count,
                observed_parent_drops = _rainParents.Count,
                simultaneous_left_right_pairs = _rainParents.Values.Count(parent => parent.Children.Count == 2
                    && parent.Children.ContainsKey(-1) && parent.Children.ContainsKey(1)
                    && Math.Abs(parent.Children[-1].Born - parent.Children[1].Born) <= 1e-6),
                maximum_spawn_marker_origin_error = _maximumRainOriginError,
                maximum_splash_impact_anchor_error = _maximumRainImpactError,
                anchor_coordinate_tolerance = .01,
                minimum_full_splash_warning_seconds = RainFullHold, physics_frame_tolerance = RainFrameTolerance,
                drop_rows = _rainDropStages.OrderBy(item => item.Key.Wave).ThenBy(item => item.Key.Row).Select(item => new
                {
                    wave = item.Key.Wave, row = item.Key.Row, startpoint_only_at = item.Value.First[0],
                    growing_drop_path_at = item.Value.First[1], complete_drop_path_at = item.Value.First[2],
                    first_drop_spawn_at = item.Value.FirstActual, stage_sample_counts = item.Value.Samples,
                    complete_warning_before_first_drop_seconds = item.Value.FirstActual - item.Value.First[2]
                }).ToArray(),
                impact_rows = _rainImpactStages.OrderBy(item => item.Key.Wave).ThenBy(item => item.Key.Row).Select(item => new
                {
                    wave = item.Key.Wave, row = item.Key.Row, impact_point_only_at = item.Value.First[0],
                    growing_splash_paths_at = item.Value.First[1], complete_splash_paths_at = item.Value.First[2],
                    first_splash_spawn_at = item.Value.FirstActual,
                    complete_warning_before_first_splash_seconds = item.Value.FirstActual - item.Value.First[2],
                    stage_sample_counts = item.Value.Samples
                }).ToArray(),
                parent_drops = _rainParents.Values.OrderBy(parent => parent.Wave).ThenBy(parent => parent.Row)
                    .ThenBy(parent => parent.Column).Select(parent =>
                    {
                        double? left = parent.Children.TryGetValue(-1, out var leftChild) ? leftChild.Born : null;
                        double? right = parent.Children.TryGetValue(1, out var rightChild) ? rightChild.Born : null;
                        string prefix = $"rain:{parent.Wave}:{parent.Row}:{parent.Column}:";
                        double? leftWarning = _rainFullForecastStarted.TryGetValue(prefix + "-1", out double leftAt) ? leftAt : null;
                        double? rightWarning = _rainFullForecastStarted.TryGetValue(prefix + "1", out double rightAt) ? rightAt : null;
                        return new
                        {
                            wave = parent.Wave, row = parent.Row, column = parent.Column, burst = parent.Burst,
                            drop_spawn_at = parent.Born, origin = new { x = parent.Origin.X, y = parent.Origin.Y },
                            impact = new { x = parent.Impact.X, y = parent.Impact.Y },
                            left_splash_spawn_at = left, right_splash_spawn_at = right,
                            left_complete_warning_at = leftWarning, right_complete_warning_at = rightWarning,
                            left_complete_warning_hold_seconds = left - leftWarning,
                            right_complete_warning_hold_seconds = right - rightWarning
                        };
                    }).ToArray(),
                basis = "Each row must expose point-only, growing, and complete warnings in order. The first complete paths are retained. Planned and actual drop/splash spawning must follow at least two seconds of complete warning, with one physics-frame tolerance. Actual drop origins match the render helper and every parent impact emits one simultaneous splash to each side at its shared marker. Rendered pixels are not inspected."
            };
        }

        public object Result() => new
        {
            @checked = true, passed = IsComplete(_expectedShots), expected_projectiles = _expectedShots,
            required_projectiles = _requiredShots, required_bursts_per_forecast_path = _requiredBursts,
            forecast_paths = _forecasts.Count, static_occurrence_forecasts = _staticWarnings.Count,
            moving_box_forecasts = _boxWarnings.Count, clock_sweep_forecast = _clockOutline != null,
            observed_projectiles = _traces.Count,
            fully_observed_retired_projectiles = _traces.Values.Count(trace => trace.Retired),
            projectile_swept_probe_points = _traces.Values.Sum(trace => trace.Probes),
            maximum_projectile_path_error = _traces.Values.Select(trace => trace.MaximumError).DefaultIfEmpty(0).Max(),
            hazard_samples = _hazardSamples, hazard_probe_points = _hazardProbes,
            foreground_layer_checked = _expectedShots > 0,
            rain_foreground_layer_checked = _mechanic == "akari.rain-splash",
            foreground_projectiles_observed = _traces.Values.Count(trace => trace.ForegroundLayerObserved),
            turning_point_geometry_checked = _mechanic is "akari.returned-express" or "akari.bound-draft-retraction",
            actual_turn_points_observed = _traces.Values.Sum(trace => trace.Turns.Count),
            maximum_turn_point_error = _traces.Values.Where(trace => trace.TurnPointError != null)
                .Select(trace => trace.TurnPointError!.Value).DefaultIfEmpty(0).Max(),
            turn_point_tolerance = .01,
            envelope_route_audit = _mechanic == "akari.carried-approval" ? new {
                passed = EnvelopeRouteComplete, sampled_live_frames = _envelopeRouteSamples,
                maximum_centerline_error = _envelopeMaximumPathError, centerline_tolerance = .01,
                required_start_and_turn_count = _envelopeCornerDistances.Length, observed_in_order = _envelopeNextCorner,
                start_and_turn_points = _envelopePath?.Take(_envelopePath.Length - 1).Select((point, index) => new {
                    x = point.X, y = point.Y, minimum_observed_distance = float.IsFinite(_envelopeCornerDistances[index])
                        ? (float?)_envelopeCornerDistances[index] : null, reached_at = _envelopeCornerReachedAt[index] }).ToArray(),
                turn_sample_tolerance = 1.2,
                basis = "Actual live body centers follow the full pre-launch polyline, and its start and each interior turn are reached in order within one 70 px/s physics step. A repeated return point is only counted after the preceding turn. Every visible body bound must also remain inside a completed pre-warning swept box." } : null,
            rain_staged_warning_audit = RainStageResult(),
            wave_geometry_checked_by_companion_boundary_and_open_sector_audits = _mechanic == "akari.spent-wavefront",
            minimum_complete_warning_seconds = RainFullHold,
            minimum_visible_complete_projectile_warning_seconds = _forecasts.Values
                .Select(forecast => (double?)(forecast.LaunchAt - forecast.ObservedAt)).DefaultIfEmpty(null).Min(),
            field_sample_spacing_maximum = 1, straight_path_tolerance = .6,
            reflection_discrete_step_tolerance = 1.2, ballistic_discrete_step_tolerance = 1,
            static_shape_bounds_tolerance = .01, failure = _failure,
            basis = "Production warning paths saved before spawn; actual projectile field positions swept at 60 Hz through reflection and ballistic descent; footprint half-widths and absolute projectile layers above their warning node checked. Actual release/reflection event positions match their prior forecast turn points. Rain paths saved only after their staged warning is complete, with spawn/impact anchors and paired splash timing checked. Static hazard bounds, envelope body centerline and turns, and moving-box/clock swept regions checked independently. This does not inspect rendered pixels.",
            projectiles = _traces.Values.Select(trace => new { forecast_key = trace.Forecast.Key,
                born = trace.Born, forecast_observed_at = trace.Forecast.ObservedAt, forecast_launch_at = trace.Forecast.LaunchAt,
                first_live_at = trace.FirstLiveAt, first_reflection_at = trace.FirstReflectionAt, first_release_at = trace.FirstReleaseAt,
                first_turn_state_observed_at = trace.FirstTurnStateObservedAt, reflection_state_observed_at = trace.ReflectionStateObservedAt,
                actual_turn_at = trace.TurnAt, actual_turn_point = trace.TurnPoint is { } turn ? new { x = turn.X, y = turn.Y } : null,
                turn_point_error = trace.TurnPointError,
                active_frames = trace.ActiveFrames, swept_probe_points = trace.Probes, retired = trace.Retired, retired_at = trace.RetiredAt,
                maximum_path_error = trace.MaximumError,
                minimum_forecast_footprint_margin = float.IsFinite(trace.MinimumWidthMargin) ? trace.MinimumWidthMargin : (float?)null,
                visible_complete_warning_hold_seconds = trace.Forecast.LaunchAt - trace.Forecast.ObservedAt,
                turn_points = trace.Turns.OrderBy(turn => turn.Key).Select(turn => new {
                    index = turn.Key, x = turn.Value.Point.X, y = turn.Value.Point.Y, at = turn.Value.At, error = turn.Value.Error }).ToArray(),
                reflection_observed = trace.Reflected, ballistic_descent_observed = trace.Descending,
                foreground_layer_observed = trace.ForegroundLayerObserved }).ToArray()
        };

        private bool EnvelopeRouteComplete => _envelopePath != null && _envelopeRouteSamples > 0
            && _envelopeNextCorner == _envelopeCornerDistances.Length
            && _envelopeCornerDistances.All(distance => distance <= 1.2f);

        private bool IsComplete(int expectedShots)
        {
            bool needsHazards = _mechanic is "akari.filing-piles" or "akari.carried-approval" or "akari.extending-clock";
            return !(_lastTime < 0 || _failure != null || _traces.Count != expectedShots || _traces.Values.Any(trace => !trace.Retired || trace.ActiveFrames == 0 || trace.Probes == 0)
                || expectedShots != _requiredShots
                || _requiredShots > 0 && (_forecasts.Count * _requiredBursts != _requiredShots
                    || _traces.Values.GroupBy(trace => trace.Forecast.Key).Any(group => group.Count() != _requiredBursts))
                || _mechanic is ("akari.returned-express" or "akari.bound-draft-retraction")
                    && _traces.Values.Select(trace => trace.Group).Distinct().Count() != _requiredShots
                || _traces.Values.Any(trace => !trace.ForegroundLayerObserved)
                || _forecasts.Values.Any(forecast => forecast.LaunchAt - forecast.ObservedAt + RainFrameTolerance + 1e-6 < RainFullHold)
                || _mechanic == "akari.bound-draft-retraction" && _traces.Values.Any(trace => trace.Turns.Count != 1)
                || _mechanic == "akari.returned-express" && _traces.Values.Any(trace => trace.Group < 100
                    && (trace.Turns.Count != 2 || !trace.Reflected || trace.FirstTurnStateObservedAt == null || trace.ReflectionStateObservedAt == null))
                || _mechanic == "akari.carried-approval" && (!EnvelopeRouteComplete || _staticWarnings.Count != 28)
                || needsHazards && _hazardSamples == 0
                || _mechanic == "akari.rain-splash" && (!_traces.Values.Any(trace => trace.Descending)
                    || RainCompletionFailure(expectedShots) != null));
        }

        public void RequirePass(int expectedShots)
        {
            if (!IsComplete(expectedShots))
                throw new Exception($"Warning path coverage audit failed or missed an attack: {_mechanic}, expected_shots={expectedShots}, observed={_traces.Count}, retired={_traces.Values.Count(trace => trace.Retired)}, turn_points={_traces.Values.Count(trace => trace.TurnPoint != null)}, envelope_route={EnvelopeRouteComplete}, hazard_samples={_hazardSamples}, failure={_failure ?? RainCompletionFailure(expectedShots) ?? "none"}");
        }
    }

    private sealed class TechniqueAudit : IDisposable
    {
        private const float Spacing = 6;
        private static readonly float Clearance = Spacing * Mathf.Sqrt(2) / 2;
        private readonly int _columns = (int)(Field.Width / Spacing) + 1;
        private readonly int _rows = (int)(Field.Height / Spacing) + 1;
        private readonly Vector2[] _points;
        private readonly bool[] _safe, _everDangerous, _frameDangerous, _everUnsafe;
        private readonly int[] _pointRegions, _pointDangerousFrames, _pointBodyFrames;
        private readonly List<int> _frameDangerousCounts = new();
        private readonly List<double> _sampleElapsedTimes = new();
        private readonly List<(double At, int Frame, int Count)> _shotCreationEvents = new();
        private readonly List<(int Start, int End)> _fieldDangerSpans = new();
        private int _lastShotsCreated;
        private readonly bool[] _regions = new bool[12], _frameRegions = new bool[12];
        private readonly int[] _regionPointCounts = new int[12], _regionDangerousPoints = new int[12],
            _regionDangerousFrames = new int[12], _edgeDangerousFrames = new int[4];
        private readonly bool[] _frameEdges = new bool[4];
        private readonly double?[] _regionFirstHit = new double?[12], _regionLastHit = new double?[12],
            _edgeFirstHit = new double?[4], _edgeLastHit = new double?[4];
        private readonly CircleShape2D _hitCircle = new() { Radius = Player.HitRadius };
        private readonly CircleShape2D _clearanceCircle = new() { Radius = Player.HitRadius + Clearance };
        private readonly float _speed;
        private readonly int _stepFrames;
        private readonly Route[] _routes;
        private int _samples, _minimumSafe = int.MaxValue, _unsafeFrames, _pilotContactFrames, _peakRegions, _intervalFrame;
        private int _liveFrames, _stationaryLiveFrames, _longestStationaryLiveFrames;
        private int _pilotAttackContactFrames, _pilotBodyContactFrames, _fieldDangerFrames;
        private long _dangerousPointFrames;
        private double _lastTime = -1, _peakTime;
        private Vector2? _lastPilot;
        private bool _wasLive;
        private readonly List<(double Start, double End)> _liveSpans = new();
        private double? _allRegionsTime;

        private sealed class Route
        {
            public Vector2 Start;
            public bool[] Reachable = null!;
            public readonly List<(int From, int To)> Edges = new();
            public double? FailedAt;
            public int Intervals;
        }

        public TechniqueAudit(float speed, bool paths, Vector2 initialPosition, bool actualStartOnly)
        {
            _speed = speed;
            _stepFrames = Mathf.CeilToInt(Spacing * Mathf.Sqrt(2) / speed * 60);
            _points = Enumerable.Range(0, _columns * _rows)
                .Select(i => new Vector2(Field.Left + i % _columns * Spacing, Field.Top + i / _columns * Spacing)).ToArray();
            _safe = new bool[_points.Length];
            _everDangerous = new bool[_points.Length];
            _frameDangerous = new bool[_points.Length];
            _everUnsafe = new bool[_points.Length];
            _pointDangerousFrames = new int[_points.Length];
            _pointBodyFrames = new int[_points.Length];
            _pointRegions = _points.Select(point =>
                Math.Min(2, (int)((point.Y - Field.Top) / Field.Height * 3)) * 4
                + Math.Min(3, (int)((point.X - Field.Left) / Field.Width * 4))).ToArray();
            foreach (int region in _pointRegions) _regionPointCounts[region]++;
            var starts = new[] { initialPosition,
                new Vector2(Field.Left + Field.Width * .25f, Field.Height * .25f),
                new Vector2(Field.Left + Field.Width * .75f, Field.Height * .25f),
                new Vector2(Field.Left + Field.Width * .25f, Field.Height * .75f),
                new Vector2(Field.Left + Field.Width * .75f, Field.Height * .75f) };
            _routes = paths ? starts.Take(actualStartOnly ? 1 : starts.Length).Select(start =>
            {
                int at = Nearest(start);
                var route = new Route { Start = start, Reachable = new bool[_points.Length] };
                route.Reachable[at] = true;
                return route;
            }).ToArray() : Array.Empty<Route>();
        }

        public void Observe(BossCharacterTechnique attack, Vector2 pilot)
        {
            if (attack.Elapsed == _lastTime) return;
            _lastTime = attack.Elapsed;
            _sampleElapsedTimes.Add(attack.Elapsed);
            int created = attack.ShotsCreated - _lastShotsCreated;
            if (created > 0) _shotCreationEvents.Add((attack.Elapsed, _samples, created));
            _lastShotsCreated = attack.ShotsCreated;
            Array.Fill(_safe, true);
            Array.Clear(_frameDangerous);
            Array.Clear(_frameRegions);
            Array.Clear(_frameEdges);
            var hazards = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(
                attack, "_hazards", typeof(BossCharacterTechnique));
            bool live = hazards.Any(h => h.Live) || attack.OwnedShots.Any(b => b.IsEnemy && !b.LeadIn && b.CollisionLayer != 0);
            if (live)
            {
                _liveFrames++;
                if (!_wasLive) _liveSpans.Add((attack.Elapsed, attack.Elapsed));
                _liveSpans[^1] = (_liveSpans[^1].Start, attack.Elapsed + 1d / 60);
            }
            _stationaryLiveFrames = live && _lastPilot is { } previous && previous.DistanceSquaredTo(pilot) < .05f * .05f
                ? _stationaryLiveFrames + 1 : 0;
            _longestStationaryLiveFrames = Math.Max(_longestStationaryLiveFrames, _stationaryLiveFrames);
            _lastPilot = pilot;
            _wasLive = live;
            foreach (var h in hazards.Where(h => h.Live))
            {
                float grow = h.Radius + Player.HitRadius + Clearance;
                var bounds = h.Shape == 3 ? Field.Rect : new Rect2(
                    new Vector2(Math.Min(h.A.X, h.B.X), Math.Min(h.A.Y, h.B.Y)),
                    new Vector2(Math.Abs(h.B.X - h.A.X), Math.Abs(h.B.Y - h.A.Y))).Grow(grow);
                Visit(bounds, i =>
                {
                    Vector2 point = _points[i];
                    bool Covers(float extra)
                    {
                        float radius = Player.HitRadius + extra;
                        if (h.Shape == 2) return new Rect2(h.A, h.B - h.A).Grow(radius).HasPoint(point);
                        if (h.Shape == 3) return point.DistanceTo(h.A) >= h.Radius - radius;
                        Vector2 ab = h.B - h.A;
                        float t = ab.LengthSquared() == 0 ? 0 : Mathf.Clamp((point - h.A).Dot(ab) / ab.LengthSquared(), 0, 1);
                        return point.DistanceSquaredTo(h.A + ab * t) <= (h.Radius + radius) * (h.Radius + radius);
                    }
                    if (Covers(Clearance)) _safe[i] = false;
                    if (Covers(0)) MarkRegion(i);
                });
            }

            bool pilotContact = attack.CoversPoint(pilot);
            foreach (var shot in attack.OwnedShots.Where(b => b.IsEnemy && !b.LeadIn && b.CollisionLayer != 0))
            {
                Vector2 position = shot.GlobalPosition;
                float scale = Math.Max(shot.GlobalTransform.X.Length(), shot.GlobalTransform.Y.Length());
                float outer = shot.CollisionRadius * scale + Player.HitRadius + Clearance;
                Transform2D transform = shot.GlobalTransform;
                var shapes = shot.Footprint!.Shapes;
                bool Hits(Vector2 point) => shapes.Any(shape => shape.Collide(transform, _hitCircle, new Transform2D(0, point)));
                Visit(new Rect2(position - Vector2.One * outer, Vector2.One * outer * 2), i =>
                {
                    if (_points[i].DistanceSquaredTo(position) > outer * outer) return;
                    _safe[i] = false;
                    if (Hits(_points[i])) MarkRegion(i);
                });
                pilotContact |= Hits(pilot);
            }
            if (pilotContact) _pilotAttackContactFrames++;
            bool pilotBodyContact = false;
            var frameBody = new bool[_points.Length];
            foreach (var enemy in attack.GetTree().GetNodesInGroup("enemies").OfType<Enemy>()
                .Where(enemy => !enemy.IsPurified && enemy.CollisionLayer != 0 && enemy.Monitorable))
            foreach (uint owner in enemy.GetShapeOwners())
            {
                if (enemy.IsShapeOwnerDisabled(owner)) continue;
                Transform2D transform = enemy.GlobalTransform * enemy.ShapeOwnerGetTransform(owner);
                for (int shapeIndex = 0; shapeIndex < enemy.ShapeOwnerGetShapeCount(owner); shapeIndex++)
                {
                    var shape = enemy.ShapeOwnerGetShape(owner, shapeIndex);
                    Vector2 half = shape switch
                    {
                        CircleShape2D circle => Vector2.One * circle.Radius,
                        CapsuleShape2D capsule => new Vector2(capsule.Radius, capsule.Height / 2),
                        _ => throw new Exception($"Unsupported enemy contact shape: {shape.GetClass()}")
                    };
                    var bounds = new Rect2(transform * -half, Vector2.Zero);
                    bounds = bounds.Expand(transform * half).Expand(transform * new Vector2(-half.X, half.Y))
                        .Expand(transform * new Vector2(half.X, -half.Y)).Grow(Player.HitRadius + Clearance);
                    Visit(bounds, i =>
                    {
                        if (shape.Collide(transform, _clearanceCircle, new Transform2D(0, _points[i]))) _safe[i] = false;
                        if (shape.Collide(transform, _hitCircle, new Transform2D(0, _points[i]))) frameBody[i] = true;
                    });
                    pilotBodyContact |= shape.Collide(transform, _hitCircle, new Transform2D(0, pilot));
                }
            }
            if (pilotBodyContact) _pilotBodyContactFrames++;
            if (pilotContact || pilotBodyContact) _pilotContactFrames++;
            int dangerousCount = 0;
            for (int i = 0; i < _points.Length; i++)
            {
                if (_frameDangerous[i]) { dangerousCount++; _pointDangerousFrames[i]++; }
                if (frameBody[i]) _pointBodyFrames[i]++;
                _everUnsafe[i] |= !_safe[i];
            }
            _frameDangerousCounts.Add(dangerousCount);
            _dangerousPointFrames += dangerousCount;
            if (dangerousCount > 0)
            {
                _fieldDangerFrames++;
                if (_fieldDangerSpans.Count == 0 || _fieldDangerSpans[^1].End != _samples)
                    _fieldDangerSpans.Add((_samples, _samples + 1));
                else _fieldDangerSpans[^1] = (_fieldDangerSpans[^1].Start, _samples + 1);
            }
            int safeCount = _safe.Count(safe => safe);
            _minimumSafe = Math.Min(_minimumSafe, safeCount);
            if (safeCount == 0) _unsafeFrames++;
            for (int region = 0; region < _frameRegions.Length; region++)
                if (_frameRegions[region])
                {
                    _regionDangerousFrames[region]++;
                    _regionFirstHit[region] ??= attack.Elapsed;
                    _regionLastHit[region] = attack.Elapsed;
                }
            for (int edge = 0; edge < _frameEdges.Length; edge++)
                if (_frameEdges[edge])
                {
                    _edgeDangerousFrames[edge]++;
                    _edgeFirstHit[edge] ??= attack.Elapsed;
                    _edgeLastHit[edge] = attack.Elapsed;
                }
            int regions = _frameRegions.Count(hit => hit);
            if (regions > _peakRegions) { _peakRegions = regions; _peakTime = attack.Elapsed; }
            if (_allRegionsTime == null && _regions.All(hit => hit)) _allRegionsTime = attack.Elapsed;
            AdvanceRoutes(attack.Elapsed);
            _samples++;
        }

        private void Visit(Rect2 bounds, Action<int> visit)
        {
            int x0 = Math.Max(0, Mathf.CeilToInt((bounds.Position.X - Field.Left) / Spacing));
            int x1 = Math.Min(_columns - 1, Mathf.FloorToInt((bounds.End.X - Field.Left) / Spacing));
            int y0 = Math.Max(0, Mathf.CeilToInt((bounds.Position.Y - Field.Top) / Spacing));
            int y1 = Math.Min(_rows - 1, Mathf.FloorToInt((bounds.End.Y - Field.Top) / Spacing));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) visit(y * _columns + x);
        }

        private void MarkRegion(int index)
        {
            _frameDangerous[index] = true;
            int region = _pointRegions[index];
            _regions[region] = _frameRegions[region] = true;
            if (!_everDangerous[index])
            {
                _everDangerous[index] = true;
                _regionDangerousPoints[region]++;
            }
            Vector2 point = _points[index];
            if (point.X <= Field.Left + Spacing) _frameEdges[0] = true;
            if (point.X >= Field.Right - Spacing) _frameEdges[1] = true;
            if (point.Y <= Field.Top + Spacing) _frameEdges[2] = true;
            if (point.Y >= Field.Bottom - Spacing) _frameEdges[3] = true;
        }

        private int Nearest(Vector2 point)
            => Mathf.Clamp(Mathf.RoundToInt((point.Y - Field.Top) / Spacing), 0, _rows - 1) * _columns
                + Mathf.Clamp(Mathf.RoundToInt((point.X - Field.Left) / Spacing), 0, _columns - 1);

        private void AdvanceRoutes(double time)
        {
            if (_routes.Length == 0) return;
            if (_samples == 0)
            {
                foreach (var route in _routes)
                    if (!_safe[Nearest(route.Start)]) route.FailedAt = time;
                return;
            }
            foreach (var route in _routes.Where(route => route.FailedAt == null))
            {
                if (_intervalFrame == 0)
                {
                    route.Edges.Clear();
                    for (int i = 0; i < route.Reachable.Length; i++)
                    {
                        if (!route.Reachable[i]) continue;
                        int x = i % _columns, y = i / _columns;
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (x + dx < 0 || x + dx >= _columns || y + dy < 0 || y + dy >= _rows) continue;
                            int to = (y + dy) * _columns + x + dx;
                            Vector2 from = route.Intervals == 0 ? route.Start : _points[i];
                            if (from.DistanceTo(_points[to]) <= _speed * _stepFrames / 60)
                                route.Edges.Add((i, to));
                        }
                    }
                }
                float fraction = (float)(_intervalFrame + 1) / _stepFrames;
                for (int i = route.Edges.Count - 1; i >= 0; i--)
                {
                    var edge = route.Edges[i];
                    // Padded grid samples cover the rounding error throughout each movement, including diagonals.
                    Vector2 from = route.Intervals == 0 ? route.Start : _points[edge.From];
                    if (_safe[Nearest(from.Lerp(_points[edge.To], fraction))]) continue;
                    route.Edges[i] = route.Edges[^1];
                    route.Edges.RemoveAt(route.Edges.Count - 1);
                }
                if (route.Edges.Count == 0) { route.FailedAt = time; continue; }
                if (_intervalFrame + 1 == _stepFrames)
                {
                    Array.Clear(route.Reachable);
                    foreach (var edge in route.Edges) route.Reachable[edge.To] = true;
                    route.Intervals++;
                }
            }
            _intervalFrame = (_intervalFrame + 1) % _stepFrames;
        }

        private object[] FixedWaitingAreas()
        {
            var seen = new bool[_points.Length];
            var areas = new List<(int Points, Rect2 Bounds, Vector2 Core, float Radius)>();
            for (int start = 0; start < _points.Length; start++)
            {
                if (seen[start] || _everUnsafe[start]) continue;
                var queue = new Queue<int>();
                var component = new List<int>();
                var bounds = new Rect2(_points[start], Vector2.Zero);
                seen[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int at = queue.Dequeue();
                    component.Add(at);
                    bounds = bounds.Expand(_points[at]);
                    int x = at % _columns, y = at / _columns;
                    foreach (var next in new[] { (X: x - 1, Y: y), (X: x + 1, Y: y), (X: x, Y: y - 1), (X: x, Y: y + 1) })
                    {
                        if (next.X < 0 || next.X >= _columns || next.Y < 0 || next.Y >= _rows) continue;
                        int index = next.Y * _columns + next.X;
                        if (seen[index] || _everUnsafe[index]) continue;
                        seen[index] = true;
                        queue.Enqueue(index);
                    }
                }
                float radius = 0;
                Vector2 core = _points[start];
                foreach (int at in component)
                {
                    Vector2 point = _points[at];
                    float clearance = Math.Min(Math.Min(point.X - Field.Left, Field.Right - point.X),
                        Math.Min(point.Y - Field.Top, Field.Bottom - point.Y));
                    for (int i = 0; i < _points.Length && clearance > radius; i++)
                        if (_everUnsafe[i]) clearance = Math.Min(clearance, Math.Max(0, point.DistanceTo(_points[i]) - Clearance));
                    if (clearance > radius) { radius = clearance; core = point; }
                }
                areas.Add((component.Count, bounds, core, radius));
            }
            return areas.OrderByDescending(area => area.Radius).ThenByDescending(area => area.Points)
                .Select(area => (object)new { sampled_points = area.Points,
                    sample_area_estimate = area.Points * Spacing * Spacing,
                    bounds = new { x = area.Bounds.Position.X, y = area.Bounds.Position.Y,
                        width = area.Bounds.Size.X, height = area.Bounds.Size.Y },
                    core = new { x = area.Core.X, y = area.Core.Y },
                    conservative_clear_radius = area.Radius }).ToArray();
        }

        private double DensityPercentile(double percentile)
        {
            int[] sorted = _frameDangerousCounts.OrderBy(count => count).ToArray();
            return sorted.Length == 0 ? 0 : sorted[(int)Math.Floor((sorted.Length - 1) * percentile)] / (double)_points.Length;
        }

        public object Result(double duration, bool requireActualStartRoute, bool requireZeroPilotContact) => new
        {
            pacing = new { duration, first_live_at = _liveSpans.Count == 0 ? (double?)null : _liveSpans[0].Start,
                trailing_no_live_seconds = _liveSpans.Count == 0 ? duration : Math.Max(0, duration - _liveSpans[^1].End),
                live_seconds = _liveFrames / 60d,
                longest_internal_no_live_seconds = _liveSpans.Skip(1)
                    .Select((span, index) => span.Start - _liveSpans[index].End).DefaultIfEmpty(0).Max(),
                longest_guided_pilot_stationary_seconds_while_live = _longestStationaryLiveFrames / 60d,
                live_intervals = _liveSpans.Select(span => new { start = span.Start, end = span.End }).ToArray(),
                basis = "Observed live hazard or enabled projectile at 60 Hz; stationary time describes this guided capture, not human difficulty." },
            cadence = new { observed_created_shots = _lastShotsCreated, creation_event_count = _shotCreationEvents.Count,
                creation_events = _shotCreationEvents.Select(item => new { attack_elapsed = item.At,
                    sampled_physics_frame = item.Frame, created_shots = item.Count }).ToArray(),
                maximum_internal_creation_gap_seconds = _shotCreationEvents.Skip(1)
                    .Select((item, index) => (item.Frame - _shotCreationEvents[index].Frame) / 60d).DefaultIfEmpty(0).Max(),
                mean_creation_event_gap_seconds = _shotCreationEvents.Count < 2 ? (double?)null
                    : (_shotCreationEvents[^1].Frame - _shotCreationEvents[0].Frame) / (60d * (_shotCreationEvents.Count - 1)),
                maximum_internal_empty_grid_seconds = _fieldDangerSpans.Skip(1)
                    .Select((span, index) => (span.Start - _fieldDangerSpans[index].End) / 60d).DefaultIfEmpty(0).Max(),
                initial_empty_grid_seconds = (_fieldDangerSpans.Count == 0 ? _samples : _fieldDangerSpans[0].Start) / 60d,
                trailing_empty_grid_seconds = (_fieldDangerSpans.Count == 0 ? _samples : _samples - _fieldDangerSpans[^1].End) / 60d,
                field_danger_spans = _fieldDangerSpans.Select(span => new { first_sample_frame = span.Start,
                    end_sample_frame_exclusive = span.End }).ToArray(),
                basis = "Observed ShotsCreated increments and actual in-field dangerous grid samples. Simultaneous shots form one creation event; deployment is not an active attack. Gaps use real 60 Hz sample frames, while attack_elapsed is stored separately because enemy-time effects may slow the technique. Initial required warnings are separate from internal firing gaps. No firing-rate threshold alone proves difficulty." },
            pressure = new { sampled_points = _points.Length, sampled_physics_frames = _samples, physics_fps = 60,
                attack_elapsed_by_sample_frame = _sampleElapsedTimes.ToArray(),
                actual_field_danger_frames = _fieldDangerFrames,
                actual_field_duty_fraction = _samples == 0 ? 0 : _fieldDangerFrames / (double)_samples,
                mean_frame_density = _samples == 0 ? 0 : _dangerousPointFrames / (double)(_points.Length * _samples),
                mean_active_frame_density = _fieldDangerFrames == 0 ? 0 : _dangerousPointFrames / (double)(_points.Length * _fieldDangerFrames),
                peak_frame_density = _frameDangerousCounts.DefaultIfEmpty(0).Max() / (double)_points.Length,
                median_frame_density = DensityPercentile(.5), percentile95_frame_density = DensityPercentile(.95),
                normalized_area_time_seconds = _dangerousPointFrames / (_points.Length * 60d),
                field_area_time_estimate = _dangerousPointFrames / (_points.Length * 60d) * Field.Width * Field.Height,
                dangerous_point_counts_by_frame = _frameDangerousCounts.ToArray(),
                attack_untouched_coordinates = Enumerable.Range(0, _points.Length).Where(i => !_everDangerous[i])
                    .Select(i => new { x = _points[i].X, y = _points[i].Y }).ToArray(),
                collision_untouched_coordinates = Enumerable.Range(0, _points.Length)
                    .Where(i => !_everDangerous[i] && _pointBodyFrames[i] == 0)
                    .Select(i => new { x = _points[i].X, y = _points[i].Y }).ToArray(),
                per_point_danger_frames = _pointDangerousFrames,
                per_point_body_frames = _pointBodyFrames,
                point_order = "row-major from Field.Left/Top at grid_spacing increments",
                fixed_waiting_areas = FixedWaitingAreas(),
                broad_waiting_area_diagnostic_radius = 12,
                basis = "60 Hz player-radius probes against actual live hazards and enabled enemy projectile collision shapes. Overlaps count once per point per frame. Enemy bodies are separate. Fixed waiting areas exclude every observed hazard, conservative projectile circle, and body clearance; four-connected grid components and their clear cores describe this one trace only. Input-dependent counterfactuals require separate fresh stationary launches. No numerical density threshold alone establishes human difficulty; between-frame events and sub-grid details are not measured." },
            coverage = new { columns = 4, rows = 3, regions_reached = _regions.Count(hit => hit),
                reached = _regions, all_regions_reached_at = _allRegionsTime,
                all_regions_required = true,
                peak_simultaneous_regions = _peakRegions, peak_elapsed = _peakTime,
                grid_spacing = Spacing, unique_dangerous_points = _regionDangerousPoints.Sum(),
                regions = Enumerable.Range(0, 12).Select(region => new { column = region % 4, row = region / 4,
                    sampled_points = _regionPointCounts[region], dangerous_points_ever = _regionDangerousPoints[region],
                    fraction_ever_dangerous = _regionDangerousPoints[region] / (double)_regionPointCounts[region],
                    dangerous_physics_frames = _regionDangerousFrames[region], dangerous_seconds = _regionDangerousFrames[region] / 60d,
                    first_hit_at = _regionFirstHit[region], last_hit_at = _regionLastHit[region] }).ToArray(),
                edges = new { band_width = Spacing, all_edges_required = false,
                    bands = new[] { "left", "right", "top", "bottom" }.Select((name, edge) => new { name,
                        reached = _edgeDangerousFrames[edge] > 0, dangerous_physics_frames = _edgeDangerousFrames[edge],
                        first_hit_at = _edgeFirstHit[edge], last_hit_at = _edgeLastHit[edge] }).ToArray() },
                basis = "Cumulative actual live hazards and active enemy projectile collision shapes, player-radius probes over one complete technique. Warning geometry, enemy body contact, and conservative safety clearance do not count toward coverage. Unique grid points, per-region dangerous frames, and four edge bands are diagnostics; all twelve regions are required independently of guided-pilot contacts." },
            safety = new { sampled_physics_frames = _samples, grid_spacing = Spacing, clearance = Clearance,
                minimum_safe_grid_points = _minimumSafe, frames_without_safe_region = _unsafeFrames,
                pilot_contact_frames_despite_invincibility = _pilotContactFrames,
                pilot_attack_contact_frames = _pilotAttackContactFrames,
                pilot_body_contact_frames = _pilotBodyContactFrames,
                zero_pilot_contact_required = requireZeroPilotContact,
                model = "Actual hazard geometry and enabled enemy body shapes; conservative bullet circumscribed circles; no bomb or dodge." },
            route = new { @checked = _routes.Length > 0, character = "Mina", speed = _speed, transition_frames = _stepFrames,
                directions = 8, permits_waiting = true, physics_fps = 60,
                actual_start_path_required = requireActualStartRoute,
                scope = "Conservative time-expanded paths through the recorded attack trace; not a human-difficulty proof.",
                starts = _routes.Select((route, index) => new { x = route.Start.X, y = route.Start.Y,
                    role = index == 0 ? requireActualStartRoute ? "actual start; required" : "actual start; diagnostic"
                        : "diagnostic position against the same recorded trace; not a separate attack launch",
                    path_exists = route.FailedAt == null, failed_at = route.FailedAt,
                    completed_intervals = route.Intervals, reachable_endpoints = route.Reachable.Count(value => value) }).ToArray() }
        };

        public void RequirePass(string title, bool requireActualStartRoute, bool requireZeroPilotContact)
        {
            if (_samples == 0 || _unsafeFrames > 0 || !_regions.All(hit => hit)
                || requireZeroPilotContact && _pilotContactFrames > 0
                || requireActualStartRoute && _routes.Length > 0 && _routes[0].FailedAt != null)
                throw new Exception($"Arena audit failed: {title}; regions={_regions.Count(hit => hit)}/12, unsafe_frames={_unsafeFrames}, pilot_contact_frames={_pilotContactFrames}, failed_routes={_routes.Count(route => route.FailedAt != null)}");
        }

        public void Dispose() { _hitCircle.Dispose(); _clearanceCircle.Dispose(); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_running) return;
        if (_observedAttack is { Finished: false } attack)
        {
            if (_warningStageAudit != null) _warningStageFailure ??= _warningStageAudit.Observe(attack);
            if (_warningPathAudit != null) _warningPathFailure ??= _warningPathAudit.Observe(attack);
            if (attack.MechanicKey == "akari.spent-wavefront")
            {
                _waveGeometryFailure ??= CheckWaveArtRadius(attack);
                _waveGeometryFailure ??= CheckWaveOpenSector(attack);
            }
            _audit!.Observe(attack, _root.Player.GlobalPosition);
            if (_stationaryProbe)
            {
                _pilot.Update(delta, shoot: _shoot, bombs: false, dodge: false);
                AutoplayInput.Move(Vector2.Zero);
                Input.FlushBufferedEvents();
            }
            else if (attack.MechanicKey == "akari.bound-draft-retraction"
                && attack.Elapsed < Property<double>(attack, "EasyUnbindingRecording"))
            {
                _pilot.Update(delta, shoot: _shoot, bombs: false, dodge: false);
                while (_draftGuideIndex < _draftGuidePoints.Length
                    && _root.Player.GlobalPosition.DistanceTo(_draftGuidePoints[_draftGuideIndex]) < .5f)
                    _draftGuideIndex++;
                GuidePlayer(_draftGuidePoints[Math.Min(_draftGuideIndex, _draftGuidePoints.Length - 1)], delta);
            }
            else if (attack.MechanicKey == "akari.filing-piles")
            {
                _pilot.Update(delta, shoot: _shoot, bombs: false, dodge: false);
                int pass = Property<int>(attack, "EasyPilePass");
                if (pass != _paperPass)
                {
                    _paperPass = pass;
                    float next = Enumerable.Range(0, Property<int>(attack, "EasyPileColumnCount") - 1)
                        .Select(column => (Invoke<float>(attack, "EasyPileX", column) + Invoke<float>(attack, "EasyPileX", column + 1)) / 2)
                        .OrderBy(x => Math.Round(Math.Abs(x - _root.Player.GlobalPosition.X), 2)).ThenBy(x => x).First();
                    _paperGapChanges++;
                    _paperGapTravel += Math.Abs(next - _paperCorridorX);
                    _paperCorridorX = next;
                }
                GuidePlayer(new Vector2(_paperCorridorX, _paperCorridorY), delta);
            }
            else if (attack.MechanicKey == "akari.spent-wavefront")
            {
                _pilot.Update(delta, shoot: _shoot, bombs: false, dodge: false);
                var launches = Read<double[]>(attack, "_easyWaveLaunches");
                int wave = launches[1] < 0 ? 0 : 1;
                Vector2 waiting = Read<Vector2[]>(attack, "_easyWaveWaitPoints")[wave];
                Vector2 origin = Read<Vector2>(attack, "_origin");
                Vector2 inward = origin + (waiting - origin).Normalized() * 12;
                if (wave != _wavePilotIndex)
                {
                    _wavePilotIndex = wave;
                    _waveLiveObservedAt = _waveGapObservedAt = _waveReturnStartedAt = -1;
                    _waveBlockedCorridorObserved = false;
                }
                var hazards = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(
                    attack, "_hazards", typeof(BossCharacterTechnique));
                bool liveRing = hazards.Any(h => h.Live && h.Shape == 0);
                if (liveRing && _waveLiveObservedAt < 0) _waveLiveObservedAt = attack.Elapsed;
                bool blocked = false;
                int samples = Mathf.CeilToInt(waiting.DistanceTo(inward));
                for (int sample = 0; sample <= samples && !blocked; sample++)
                    blocked = attack.CoversPoint(waiting.Lerp(inward, sample / (float)samples));
                _waveBlockedCorridorObserved |= liveRing && blocked;
                if (_waveReturnStartedAt < 0)
                {
                    if (!liveRing || blocked || !_waveBlockedCorridorObserved) _waveGapObservedAt = -1;
                    else
                    {
                        if (_waveGapObservedAt < 0) _waveGapObservedAt = attack.Elapsed;
                        if (attack.Elapsed - _waveGapObservedAt + 1e-6 >= .2)
                        {
                            _waveReturnStartedAt = attack.Elapsed;
                            _waveReturns.Add(new { wave = wave + 1, live_ring_observed_at = _waveLiveObservedAt,
                                blocked_corridor_observed = _waveBlockedCorridorObserved,
                                gap_observed_at = _waveGapObservedAt, return_started_at = _waveReturnStartedAt,
                                reaction_delay_seconds = _waveReturnStartedAt - _waveGapObservedAt,
                                live_ring_remains = liveRing, collision_probe_spacing = 1,
                                uses_opening_time_constant = false, visual_comprehension_test = false });
                            GD.Print($"[TECHNIQUES] WAVE REACTION wave={wave + 1} live={_waveLiveObservedAt:F3} observed_gap={_waveGapObservedAt:F3} return={_waveReturnStartedAt:F3}; live-collision response, not visual comprehension.");
                        }
                    }
                }
                Vector2 target = _waveReturnStartedAt < 0 ? waiting : inward;
                GuidePlayer(target, delta);
            }
            else _pilot.Update(delta, shoot: _shoot, bombs: false, dodge: false);
        }
        else AutoplayInput.Move(Vector2.Zero);
        _root.Player._PhysicsProcess(delta);
        if (_stationaryProbe)
            _stationaryMaxDisplacement = Math.Max(_stationaryMaxDisplacement, _root.Player.GlobalPosition.DistanceTo(_stationaryOrigin));
        Call(_root.Player, "SetSpriteVisible", null, true);
    }

    private void GuidePlayer(Vector2 target, double delta)
    {
        Vector2 movement = (target - _root.Player.GlobalPosition).LimitLength(_root.Player.SlowestMoveSpeed * (float)delta);
        AutoplayInput.Move(movement / (_root.Player.MoveSpeed * (float)delta));
        Input.FlushBufferedEvents();
    }

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_output + "/" + name);
        if (result != Error.Ok) throw new Exception($"Screenshot failed: {name}: {result}");
    }

    private async Task PhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
