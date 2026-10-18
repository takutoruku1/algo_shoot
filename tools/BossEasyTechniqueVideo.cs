using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private bool _easyMode, _easyStationary, _easyCycle;
    private BossCharacterTechnique? _easyAttack;
    private Node2D? _easyRoot;
    private readonly List<object> _easyCases = new();
    private readonly List<object> _easyCycles = new();
    private Vector2 _easyStart;
    private float _easyDisplacement;
    private static readonly CircleShape2D EasyPlayerCore = new() { Radius = Player.HitRadius };
    private static readonly CircleShape2D EasyRouteCore = new() { Radius = Player.HitRadius + 4.25f };
    private static readonly Vector2[] EasyStarts = {
        new(Field.CenterX, Field.CenterY + 48),
        new(Field.Left + 24, Field.CenterY), new(Field.Right - 24, Field.CenterY),
        new(Field.Left + 24, Field.Top + 24), new(Field.Right - 24, Field.Top + 24),
        new(Field.Left + 24, Field.Bottom - 24), new(Field.Right - 24, Field.Bottom - 24),
    };

    private static void EasyRequire(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private async Task RunEasy()
    {
        _easyMode = true;
        int exit = 0;
        bool oldPause = Settings.PauseWhenUnfocused;
        try
        {
            EasyRequire(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "An isolated QA save directory is required.");
            var args = OS.GetCmdlineUserArgs();
            _scene = args.First(a => a.StartsWith("--boss="))[7..];
            EasyRequire(new[] { "Koharu", "Rei", "MinaBattle" }.Contains(_scene), "Unknown EASY boss.");
            _output = ProjectSettings.GlobalizePath(args.First(a => a.StartsWith("--output="))[9..]);
            System.IO.Directory.CreateDirectory(_output);
            bool qa = args.Contains("--qa");
            bool probes = args.Contains("--stationary-probes");
            _easyCycle = args.Contains("--battle-cycle");
            EasyRequire(!args.Contains("--peel-qa") || _scene == "Rei", "Peeling QA requires Rei.");
            bool headless = DisplayServer.GetName() == "headless";
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = args.FirstOrDefault(arg => arg.StartsWith("--difficulty=")) is { } difficulty
                ? Enum.Parse<GameManager.Diff>(difficulty[13..]) : GameManager.Diff.Easy;
            _game.SelectedJob = _scene == "MinaBattle" ? Job.Magic : Job.Tank;
            _game.TrainingSetAllUpgrades(false);
            foreach (string tutorial in new[] { "once_midboss_shield", "once_tutorial_route", "once_tutorial_shield",
                "once_tutorial_lock", "once_tutorial_bomb", "once_skill_charge", "once_ankers_rei" })
                _game.MarkIdleDialogSeen(tutorial);
            Engine.TimeScale = 1;
            Engine.PhysicsTicksPerSecond = 60;
            GD.Seed(2081);
            Input.UseAccumulatedInput = false;
            if (!headless)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            await Frames(2);
            _pool = GetNode<BulletPool>("/root/Pool");
            _easyRoot = GD.Load<PackedScene>($"res://{_scene}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(_easyRoot);
            GetTree().CurrentScene = _easyRoot;
            _easyRoot.SetProcess(false);
            ((Node)_easyRoot.GetType().GetProperty("Stage")!.GetValue(_easyRoot)!).SetProcess(false);
            _easyRoot.GetNode<Node2D>("World").ProcessMode = ProcessModeEnum.Inherit;
            _player = _easyRoot.GetNode<Player>("World/Player");
            _player.SetPhysicsProcess(false);
            _hud = _easyRoot.GetNode<Hud>("Hud");
            _hud.HoldBubble = false;
            _hud.HideBubble();
            Hud.BubblePaused = false;
            _boss = _scene switch { "Koharu" => new BossKoharu(), "Rei" => new BossRei(), _ => new BossMina() };
            _boss.Position = new Vector2(Field.BossCenterX, Field.BossZoneCenterY);
            _easyRoot.GetNode<Node2D>("World").AddChild(_boss);
            _boss.SetProcess(false);
            _boss.SetPhysicsProcess(false);
            var posts = Read<BossPostSequence>(_boss, "_posts");
            posts.ProcessMode = ProcessModeEnum.Disabled;
            foreach (var child in _boss.GetChildren())
            {
                if (child is AreaSpellCaster caster) { caster.CancelPendingAttacks(); caster.SetProcess(false); }
                if (child is MinaPhaseAttacks minaCaster) { minaCaster.CancelPendingAttacks(); minaCaster.SetProcess(false); }
            }
            Call(_boss, "TickEntrance", 0d);
            Call(_boss, "TickEntrance", 2d);
            Call(_boss, "TickSwapAnim", 1d);
            _boss.SetPanelsInvulnerable(true);
            _game.EndFury();
            Write(_boss, "_memoryPlayed", true);
            Write(_player, "_invincible", true);
            Write(_player, "_invincibleTimer", 99999f);
            Write(_player, "_fireCooldown", 99999f);
            foreach (string timer in new[] { "_bannerTimer", "_cutinTimer", "_bossLineTimer" }) Write(_hud, timer, 0d);
            _hud.HideSpellCard();
            var bg = _easyRoot.GetNode<StageBackground>("StageBackground");
            bg.EnterBoss();
            bg.GetNode<BgLayers>("BgLayers")._Process(2);
            posts.ResumeMusic();
            _pilot = new AutoplayInput(GetTree(), _game);
            ProcessPhysicsPriority = -100;
            _player.GlobalPosition = EasyStarts[0];
            await EasyPhysicsFrames(60);
            GD.Print($"[EASY] CONDITIONS boss={_scene} difficulty={_game.Difficulty} fps=60 time_scale=1 invincible=true bombs=false dodge=false; reactive pilot or stationary fresh starts; not a human-comprehension or no-hit demonstration.");
            int phases = _boss is BossMina ? 5 : 2;
            var onlyArg = args.FirstOrDefault(a => a.StartsWith("--only="));
            HashSet<string>? only = onlyArg == null ? null : onlyArg[7..].Split(',').ToHashSet();
            for (int phase = 0; phase < phases; phase++)
            {
                await SetEasyPhase(phase);
                if (args.Contains("--peel-qa"))
                {
                    if (phase == 1) await RunEasyPeelQa();
                    continue;
                }
                if (args.Contains("--transition-qa"))
                {
                    await RunEasyTransitionQa(phase);
                    continue;
                }
                if (_easyCycle)
                {
                    await EasyBattleCycle(phase);
                    continue;
                }
                var slots = BossAttackCycle.EasySlots(_boss, phase).Distinct().ToList();
                if (phase == phases - 1 && _boss is not BossMina) slots.Add(new(BossTechniqueKind.Finale));
                foreach (var slot in slots)
                {
                    string id = $"{phase}:{slot.Kind}:{slot.Variant}";
                    if (only != null && !only.Contains(id)) continue;
                    int starts = probes ? EasyStarts.Length : qa ? 2 : 1;
                    for (int start = 0; start < starts; start++) await RunEasyClip(phase, slot, start, probes, qa);
                }
            }
            System.IO.File.WriteAllText(_output + "/chapters.json", JsonSerializer.Serialize(_easyCases,
                new JsonSerializerOptions { WriteIndented = true }));
            System.IO.File.WriteAllText(_output + "/cycles.json", JsonSerializer.Serialize(_easyCycles,
                new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[EASY] COMPLETE boss={_scene} clips={_easyCases.Count} cycle_phases={_easyCycles.Count}");
        }
        catch (Exception error)
        {
            exit = 1;
            GD.PushError($"[EASY] FAIL {error}");
        }
        _running = false;
        _pilot?.Release();
        _easyAttack?.Cancel();
        _pool?.DespawnAll();
        foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
        { audio.Stop(); audio.Stream = null; }
        if (IsInstanceValid(_easyRoot)) _easyRoot!.QueueFree();
        typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { oldPause });
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GetTree().Quit(exit);
    }

    private async Task SetEasyPhase(int phase)
    {
        _running = false;
        _pilot.Release();
        EasyRequire(_pool.GetChildren().OfType<Bullet>().All(b => !b.Active || !b.IsEnemy), "Previous technique left active bullets before phase transition.");
        if (_boss is BossMina mina)
        {
            Write(mina, "_pattern", phase);
            Read<MinaPhaseAttacks>(mina, "_caster").BeginPhase(phase);
            Call(mina, "ChangeBattleCostume", BossMina.BattleCostumePath(phase, "idle"), BossMina.BattleCostumePath(phase, "attack"), BossMina.BattleDownPath(phase));
            if (phase >= BossMina.DragonPhase) mina.GlobalPosition = mina.KeepDragonInField(mina.GlobalPosition);
        }
        else if (phase == 1 && !_boss.HasEvolved)
        {
            // Skip the form cutscene; restore the requested difficulty before creating any technique.
            var difficulty = _game.Difficulty;
            _game.Difficulty = GameManager.Diff.Lunatic;
            Call(_boss, "AdvanceForm2");
            _game.Difficulty = difficulty;
            for (int frame = 0; frame < 600 && _boss.Transforming; frame++) await EasyPhysicsFrames(1);
            EasyRequire(_boss.HasEvolved && !_boss.Transforming, "Transformation failed to finish.");
        }
        Call(_boss, _boss is BossRei ? "ApplyPhaseVisual" : "ApplySpell");
        _pilot.Release();
        for (int frame = 0; frame < 600 && _player.ReturningToStart; frame++)
        {
            _player._PhysicsProcess(1d / 60);
            await EasyPhysicsFrames(1);
        }
        EasyRequire(!_player.ReturningToStart, "Player return movement did not finish before the fresh probe.");
        _player.GlobalPosition = EasyStarts[0];
        _hud.HideSpellCard();
        _hud.HideBubble();
        Hud.BubblePaused = false;
        await EasyPhysicsFrames(5);
    }

    private async Task RunEasyClip(int phase, BossAttackCycle.Slot slot, int start, bool stationary, bool qa)
    {
        _running = false;
        _pilot.Release();
        _easyStationary = stationary;
        _easyStart = EasyStarts[start];
        _boss.GlobalPosition = new Vector2(Field.BossCenterX, Field.BossZoneCenterY);
        if (_boss is BossMina mina && mina.AttackPhase >= BossMina.DragonPhase)
            _boss.GlobalPosition = mina.KeepDragonInField(_boss.GlobalPosition);
        _player.GlobalPosition = _easyStart;
        _easyDisplacement = 0;
        _hud.HideSpellCard();
        await EasyPhysicsFrames(12);
        long firstFrame = (long)Engine.GetProcessFrames();
        _easyAttack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
        string name = _easyAttack.SpellName, key = _easyAttack.MechanicKey;
        var audit = new EasyAttackAudit(_easyAttack, _player, _boss, _easyStart);
        int shots = 0, hazardFrames = 0;
        bool completed = false, ended = false;
        _easyAttack.Ended += attack =>
        {
            shots = attack.ShotsCreated;
            hazardFrames = attack.HazardFrames;
            completed = attack.Completed;
            ended = true;
            audit.End(attack);
        };
        _running = true;
        GD.Print($"[EASY] START {_scene} phase={phase} {slot.Kind}/{slot.Variant} {key} start={start} stationary={stationary}");
        for (int frame = 0; frame < 180 * 60 && !ended; frame++)
        {
            await EasyPhysicsFrames(1);
            if (!ended) audit.Observe(_easyAttack, _player.GlobalPosition);
        }
        _running = false;
        _pilot.Release();
        EasyRequire(ended && completed && (shots > 0 || hazardFrames > 0),
            $"Technique did not complete naturally: {key} ended={ended} completed={completed} elapsed={_easyAttack.Elapsed} shots={shots} hazards={hazardFrames}");
        audit.Validate();
        EasyRequire(!stationary || _easyDisplacement < .01f, "Stationary probe moved.");
        long lastFrame = (long)Engine.GetProcessFrames();
        var result = new {
            character = _scene, difficulty = _game.Difficulty.ToString(), phase, kind = slot.Kind.ToString(), variant = slot.Variant,
            name, mechanic = key, start = firstFrame, end = lastFrame, fps = 60, completed, shots, hazardFrames,
            start_index = start, stationary, maximum_player_displacement = _easyDisplacement,
            capture = new { invincible = true, shooting = false, bombs = false, dodge = false,
                mouse_follow = false,
                visual_comprehension_test = false, human_difficulty_test = false, qa,
                player_job = _game.SelectedJob.ToString(), movement_speed = _player.MoveSpeed,
                movement = stationary ? "stationary fresh start" : EasyPilotPolicy() },
            audit = audit.Result(),
        };
        _easyCases.Add(result);
        System.IO.File.WriteAllText(_output + "/chapters.json", JsonSerializer.Serialize(_easyCases,
            new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"[EASY] END {key} shots={shots} hazards={hazardFrames} contacts={audit.Contacts} body_contacts={audit.BodyContacts}");
        _easyAttack = null;
        await EasyPhysicsFrames(48);
    }

    private async Task EasyBattleCycle(int phase)
    {
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        var slots = BossAttackCycle.EasySlots(_boss, phase);
        var started = new List<object>();
        var seen = new HashSet<BossCharacterTechnique>();
        int completed = 0;
        _easyStationary = true;
        _easyStart = EasyStarts[0];
        _easyDisplacement = 0;
        _running = true;
        for (int frame = 0; frame < 1200 * 60 && completed < slots.Count; frame++)
        {
            danmaku.Tick(1d / 60);
            if (danmaku.ActiveTechnique is { } attack && seen.Add(attack))
            {
                int cursor = danmaku.EasySlot;
                var expected = slots[cursor];
                EasyRequire(attack.TechniqueKind == expected.Kind && attack.TechniqueVariant == expected.Variant,
                    "Runtime EASY selection differed from canonical slot.");
                started.Add(new { slot = cursor, kind = attack.TechniqueKind.ToString(), variant = attack.TechniqueVariant, mechanic = attack.MechanicKey });
                attack.Ended += ended => { EasyRequire(ended.Completed, "Runtime slot was interrupted."); completed++; };
            }
            await EasyPhysicsFrames(1);
        }
        _running = false;
        EasyRequire(completed == slots.Count && danmaku.EasyRounds == 1 && danmaku.EasySlot == 0, "EASY round did not advance only after natural completion.");
        var first = slots[0];
        for (int frame = 0; frame < 120 && (danmaku.ActiveTechnique == null || danmaku.ActiveTechnique.Finished); frame++)
        { danmaku.Tick(1d / 60); await EasyPhysicsFrames(1); }
        var cancelled = danmaku.ActiveTechnique!;
        EasyRequire(cancelled != null && !cancelled.Finished, "Retry check did not start.");
        cancelled!.Cancel();
        EasyRequire(danmaku.EasySlot == 0 && danmaku.EasyRounds == 1, "Cancellation advanced the EASY cursor.");
        for (int frame = 0; frame < 120 && (danmaku.ActiveTechnique == null || danmaku.ActiveTechnique.Finished); frame++)
        { danmaku.Tick(1d / 60); await EasyPhysicsFrames(1); }
        var retry = danmaku.ActiveTechnique!;
        EasyRequire(retry != null && !retry.Finished && retry.TechniqueKind == first.Kind && retry.TechniqueVariant == first.Variant,
            "Cancelled EASY slot was not retried.");
        retry!.Cancel();
        await EasyPhysicsFrames(2);
        _easyCycles.Add(new { phase, passed = true, natural_completions = completed, rounds = danmaku.EasyRounds,
            cursor = danmaku.EasySlot, cancellation_preserves_cursor = true, retry_same_slot = true, started });
        if (_boss is BossMina phaseBoss)
            EasyRequire(Read<MinaPhaseAttacks>(phaseBoss, "_caster").OpenerCompleted, "Mina phase opener completion was not acknowledged.");
        if (phase == 1 && _boss is not BossMina)
        {
            var caster = Read<AreaSpellCaster>(_boss, "_caster");
            caster.RequestFinale();
            bool began = false;
            for (int frame = 0; frame < 180 * 60 && !caster.FinaleCompleted; frame++)
            {
                caster._Process(1d / 60);
                var finale = GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>()
                    .FirstOrDefault(a => !a.Finished && a.TechniqueKind == BossTechniqueKind.Finale);
                began |= finale != null;
                await EasyPhysicsFrames(1);
            }
            EasyRequire(began && caster.FinaleCompleted, "Native finale request failed to complete.");
        }
        GD.Print($"[EASY] CYCLE phase={phase} PASS slots={completed} cancelled_retry=true");
    }

    private void TickEasyPilot(double delta)
    {
        if (!_running) return;
        if (_easyStationary) _pilot.Release();
        else DriveEasyPilot(delta);
        Input.FlushBufferedEvents();
        Pad.ReleaseMouseFollow();
        _player._PhysicsProcess(delta);
        _easyDisplacement = Math.Max(_easyDisplacement, _player.GlobalPosition.DistanceTo(_easyStart));
        Call(_player, "SetSpriteVisible", true);
    }

    private async Task EasyPhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private sealed partial class EasyAttackAudit
    {
        private sealed class Cue
        {
            public double Start, Fire;
            public double? Full, Actual;
            public int Point, Growing, Complete;
            public bool Cancelled;
        }
        private readonly Dictionary<string, Cue> _cues = new();
        private readonly List<object> _emissions = new();
        private readonly Dictionary<string, (int Samples, float OriginError, float PathError, float WidthError)> _pathChecks = new();
        private readonly Enemy _boss;
        private readonly Player _player;
        private readonly Vector2[] _points;
        private readonly int[] _dangerFrames;
        private readonly List<(Shape2D Shape, Transform2D Transform, float Radius)> _bodyShapes = new();
        private readonly bool[] _regions = new bool[12];
        private bool[] _reachable;
        private bool[] _previousBlocked;
        private readonly int _columns;
        private int _frames, _samples, _dangerSamples, _safeMinimum = int.MaxValue, _noSafe, _created;
        private int _emptyStreak, _maximumInternalEmpty, _lastDangerFrame = -1, _remainingAtEnd, _liveHazardsAtEnd;
        private double _densitySum, _densityMaximum, _firstDanger = -1;
        private double _lastEmission = -1, _maximumEmissionGap, _routeElapsed;
        private bool _routeLost;
        private int? _expectedShots;
        private string? _failure;
        public int Contacts { get; private set; }
        public int BodyContacts { get; private set; }

        public EasyAttackAudit(BossCharacterTechnique attack, Player player, Enemy boss, Vector2 start)
        {
            _boss = boss;
            _player = player;
            var points = new List<Vector2>();
            for (float y = Field.Top + 6; y <= Field.Bottom - 6; y += 6)
                for (float x = Field.Left + 6; x <= Field.Right - 6; x += 6) points.Add(new(x, y));
            _points = points.ToArray();
            _columns = Enumerable.Range(0, _points.Length).Count(i => _points[i].Y == _points[0].Y);
            _dangerFrames = new int[_points.Length];
            _reachable = new bool[_points.Length];
            _previousBlocked = new bool[_points.Length];
            int nearest = Enumerable.Range(0, _points.Length).MinBy(i => _points[i].DistanceSquaredTo(start));
            _reachable[nearest] = true;
            Observe(attack, start);
        }

        private readonly record struct LiveShot(Vector2 Position, Transform2D Transform, float Radius, Shape2D[] Shapes);

        private static LiveShot SampleShot(Bullet b)
        {
            float radius = b.Footprint?.Radius ?? b.Radius;
            Transform2D transform = b.GlobalTransform;
            radius = radius * Math.Max(transform.X.Length(), transform.Y.Length()) + Player.HitRadius;
            return new(b.GlobalPosition, transform, radius, b.Footprint?.Shapes ?? Array.Empty<Shape2D>());
        }

        private static bool BulletHit(LiveShot b, Vector2 point, bool clearance = false)
        {
            float radius = b.Radius + (clearance ? 4.25f : 0);
            return b.Position.DistanceSquaredTo(point) <= radius * radius
                && b.Shapes.Any(shape => shape.Collide(b.Transform, clearance ? EasyRouteCore : EasyPlayerCore, new Transform2D(0, point)));
        }

        private bool BodyHit(Vector2 point, bool clearance = false) => _bodyShapes.Any(body =>
            point.DistanceTo(body.Transform.Origin) <= body.Radius + (clearance ? 4.25f : 0)
            && body.Shape.Collide(body.Transform, clearance ? EasyRouteCore : EasyPlayerCore, new Transform2D(0, point)));

        private void ObserveEditorialPaths(BossCharacterTechnique attack)
        {
            if (attack is not ReiBossTechnique) return;
            var paths = Read<System.Collections.IEnumerable>(attack, "_easyPaths").Cast<object>().ToArray();
            _expectedShots = paths.Length;
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (!Read<bool>(path, "Fired")) continue;
                string key = Read<string>(path, "Key");
                var shot = Read<Bullet?>(path, "Shot");
                if (shot is not { Active: true } || shot.ActivationId != Read<ulong>(path, "Activation")) continue;
                Vector2 from = Read<Vector2>(path, "From"), to = Read<Vector2>(path, "To");
                float speed = Read<float>(path, "Velocity"), halfWidth = Read<float>(path, "HalfWidth");
                var check = _pathChecks.GetValueOrDefault(key);
                Vector2 at = shot.GlobalPosition;
                float originError = check.Samples == 0 ? at.DistanceTo(from) : check.OriginError;
                float pathError = at.DistanceTo(Geometry2D.GetClosestPointToSegment(at, from, to));
                float width = (shot.Footprint?.Radius ?? shot.Radius)
                    * Math.Max(shot.GlobalTransform.X.Length(), shot.GlobalTransform.Y.Length());
                float widthError = Math.Abs(width - halfWidth);
                _pathChecks[key] = (check.Samples + 1, originError, Math.Max(check.PathError, pathError), Math.Max(check.WidthError, widthError));
                if (check.Samples == 0 && originError > speed * 2 / 60 + .1f)
                    _failure ??= "Actual spawn differs from its forecast origin: " + key;
                if (pathError > speed * 2 / 60 + .1f || widthError > .15f)
                    _failure ??= $"Actual projectile differs from forecast path or width: {key} path={pathError} width={widthError}";
                int parent = Read<int>(path, "Parent");
                if (parent >= 0 && from.DistanceTo(Read<Vector2>(paths[parent], "To")) > .001f)
                    _failure ??= "Child split differs from its parent's fixed destination: " + key;
                if (shot.BossBarrageId != attack.GetInstanceId() || shot.BossBarrageWave != i)
                    _failure ??= "Projectile lost its forecast identity: " + key;
            }
        }

        private void ObserveKoharuPaths(BossCharacterTechnique attack)
        {
            if (attack is not KoharuBossTechnique) return;
            var paths = Read<System.Collections.IEnumerable>(attack, "_easyPaths").Cast<object>().ToArray();
            _expectedShots = paths.Length;
            var positionMethod = attack.GetType().GetMethod("EasyPathPosition", Private | System.Reflection.BindingFlags.Static)!;
            foreach (var path in paths)
            {
                if (!Read<bool>(path, "Fired")) continue;
                var shot = Read<Bullet?>(path, "Shot");
                if (shot is not { Active: true } || shot.ActivationId != Read<ulong>(path, "Activation")) continue;
                string key = Read<string>(path, "Key");
                Vector2[] points = Read<Vector2[]>(path, "Points");
                double duration = Read<double>(path, "Duration"), fire = Read<double>(path, "FiresAt");
                Vector2 expected = (Vector2)positionMethod.Invoke(positionMethod.IsStatic ? null : attack,
                    new object[] { path, Math.Max(0, attack.Elapsed - fire) })!;
                float length = 0;
                for (int i = 1; i < points.Length; i++) length += points[i - 1].DistanceTo(points[i]);
                float tolerance = (float)(length / duration * 2 / 60) + .15f;
                var check = _pathChecks.GetValueOrDefault(key);
                Vector2 at = shot.GlobalPosition;
                float origin = check.Samples == 0 ? at.DistanceTo(points[0]) : check.OriginError;
                float pathError = at.DistanceTo(expected);
                float width = (shot.Footprint?.Radius ?? shot.Radius)
                    * Math.Max(shot.GlobalTransform.X.Length(), shot.GlobalTransform.Y.Length());
                float widthError = Math.Abs(width - Read<float>(path, "HalfWidth"));
                _pathChecks[key] = (check.Samples + 1, origin, Math.Max(check.PathError, pathError), Math.Max(check.WidthError, widthError));
                if (check.Samples == 0 && origin > tolerance || pathError > tolerance || widthError > .15f)
                    _failure ??= $"Koharu projectile differs from forecast: {key} origin={origin} path={pathError} width={widthError}";
                if (shot.BossBarrageId != attack.GetInstanceId() || shot.BossBarrageWave != Read<int>(path, "Wave"))
                    _failure ??= "Koharu projectile lost its forecast identity: " + key;
            }
        }

        public void Observe(BossCharacterTechnique attack, Vector2 player)
        {
            _frames++;
            ObserveEditorialPaths(attack);
            ObserveKoharuPaths(attack);
            ObserveKoharuSweeps(attack);
            ObserveMinaGeometry(attack);
            _bodyShapes.Clear();
            if (!_boss.IsPurified && _boss.CollisionLayer != 0 && _boss.Monitorable)
            foreach (uint owner in _boss.GetShapeOwners())
            {
                if (_boss.IsShapeOwnerDisabled(owner)) continue;
                Transform2D transform = _boss.GlobalTransform * _boss.ShapeOwnerGetTransform(owner);
                for (int i = 0; i < _boss.ShapeOwnerGetShapeCount(owner); i++)
                {
                    var shape = _boss.ShapeOwnerGetShape(owner, i);
                    float radius = shape switch { CircleShape2D c => c.Radius, CapsuleShape2D c => c.Height * .5f,
                        RectangleShape2D r => r.Size.Length() * .5f, _ => 100 };
                    radius = radius * Math.Max(transform.X.Length(), transform.Y.Length()) + Player.HitRadius;
                    _bodyShapes.Add((shape, transform, radius));
                }
            }
            foreach (var sample in attack.EasyCues)
            {
                if (!_cues.TryGetValue(sample.Key, out var cue))
                    _cues.Add(sample.Key, cue = new Cue { Start = sample.StartsAt, Fire = sample.FiresAt });
                cue.Cancelled = sample.Cancelled;
                if (Math.Abs(cue.Start - sample.StartsAt) > .00001 || Math.Abs(cue.Fire - sample.FiresAt) > .00001)
                    _failure ??= "Warning schedule changed after it was shown: " + sample.Key;
                if (sample.Progress <= 0) cue.Point++;
                else if (sample.Progress < 1) cue.Growing++;
                else { cue.Complete++; cue.Full ??= attack.Elapsed; }
                if (sample.Live && cue.Actual == null)
                {
                    cue.Actual = attack.Elapsed;
                    if (cue.Full == null || attack.Elapsed - cue.Full.Value + 1d / 60 + .00001 < 2)
                        _failure ??= $"Complete warning shorter than two seconds: {sample.Key} full={cue.Full} live={attack.Elapsed}";
                }
            }
            var live = attack.OwnedShots.Where(b => !b.LeadIn && b.CollisionLayer != 0).Select(SampleShot).ToArray();
            int newShots = attack.ShotsCreated - _created;
            if (newShots > 0)
            {
                _emissions.Add(new { at = attack.Elapsed, count = newShots, physics_sample = _frames });
                if (_lastEmission >= 0) _maximumEmissionGap = Math.Max(_maximumEmissionGap, attack.Elapsed - _lastEmission);
                _lastEmission = attack.Elapsed;
                _created = attack.ShotsCreated;
            }
            if (attack.CoversPoint(player) || live.Any(b => BulletHit(b, player))) Contacts++;
            if (BodyHit(player)) BodyContacts++;
            if (_frames % 6 != 0) return;
            _samples++;
            var blocked = new bool[_points.Length];
            int dangerous = 0, safe = 0;
            for (int i = 0; i < _points.Length; i++)
            {
                var point = _points[i];
                bool attackHit = attack.CoversPoint(point) || live.Any(b => BulletHit(b, point));
                bool bodyHit = BodyHit(point);
                blocked[i] = attack.DangerDistance(point, false) <= 4.25f || live.Any(b => BulletHit(b, point, true)) || BodyHit(point, true);
                if (!attackHit && !bodyHit) safe++;
                if (!attackHit) continue;
                dangerous++;
                _dangerFrames[i]++;
                int col = Math.Clamp((int)((point.X - Field.Left) / Field.Width * 4), 0, 3);
                int row = Math.Clamp((int)((point.Y - Field.Top) / Field.Height * 3), 0, 2);
                _regions[row * 4 + col] = true;
            }
            _safeMinimum = Math.Min(_safeMinimum, safe);
            if (safe == 0) _noSafe++;
            double density = dangerous / (double)_points.Length;
            _densitySum += density;
            _densityMaximum = Math.Max(_densityMaximum, density);
            if (dangerous > 0)
            {
                _dangerSamples++;
                if (_firstDanger < 0) _firstDanger = attack.Elapsed;
                if (_lastDangerFrame >= 0) _maximumInternalEmpty = Math.Max(_maximumInternalEmpty, _emptyStreak);
                _lastDangerFrame = _samples;
                _emptyStreak = 0;
            }
            else _emptyStreak++;
            _routeElapsed += .1;
            if (_routeElapsed * _player.SlowestMoveSpeed >= 6)
            {
                var next = new bool[_points.Length];
                for (int i = 0; i < _points.Length; i++)
                {
                    if (blocked[i]) continue;
                    if (_reachable[i]) { next[i] = true; continue; }
                    foreach (int previous in new[] { i - 1, i + 1, i - _columns, i + _columns })
                    {
                        if (previous < 0 || previous >= _points.Length || !_reachable[previous] || _previousBlocked[previous] || _previousBlocked[i]) continue;
                        if (_points[previous].DistanceSquaredTo(_points[i]) <= 36.001) { next[i] = true; break; }
                    }
                }
                _reachable = next;
                _routeElapsed -= 6 / _player.SlowestMoveSpeed;
                if (!next.Any(v => v)) _routeLost = true;
            }
            else
            {
                for (int i = 0; i < _reachable.Length; i++) _reachable[i] &= !blocked[i];
                if (!_reachable.Any(v => v)) _routeLost = true;
            }
            _previousBlocked = blocked;
        }

        public void End(BossCharacterTechnique attack)
        {
            EndKoharuSweeps(attack);
            EndMinaGeometry(attack);
            _remainingAtEnd = attack.NaturalEndRemainingShots;
            _liveHazardsAtEnd = attack.NaturalEndLiveHazards;
        }

        public void Validate()
        {
            EasyRequire(_failure == null, _failure ?? "");
            EasyRequire(_cues.Count > 0 && _cues.Values.All(c => c.Cancelled || c.Full != null && c.Actual != null), "Missing complete warning or actual attack event.");
            EasyRequire(_noSafe == 0, "Attack filled the entire reachable field.");
            EasyRequire(_expectedShots == null || _expectedShots == _created && _pathChecks.Count == _expectedShots,
                "Planned projectiles, physical spawns or all burst trajectories are missing.");
            EasyRequire(_remainingAtEnd == 0 && _liveHazardsAtEnd == 0, "Natural completion removed active bullets or hazards.");
        }

        public object Result() => new {
            frames = _frames, samples = _samples, spacing = 6, regions_reached = _regions.Count(v => v), regions = _regions,
            minimum_safe_points = _safeMinimum, samples_without_safe_region = _noSafe,
            mean_frame_density = _samples == 0 ? 0 : _densitySum / _samples, peak_frame_density = _densityMaximum,
            area_time_seconds = _densitySum * .1, danger_seconds = _dangerSamples * .1,
            first_danger_at = _firstDanger, maximum_internal_empty_seconds = _maximumInternalEmpty * .1,
            maximum_emission_gap_seconds = _maximumEmissionGap, emissions = _emissions,
            untouched_points = _dangerFrames.Count(c => c == 0), points = _points.Length,
            maximum_point_danger_seconds = _dangerFrames.Max() * .1,
            attack_contact_frames = Contacts, body_contact_frames = BodyContacts,
            active_owned_bullets_before_natural_cleanup = _remainingAtEnd,
            live_hazards_before_natural_cleanup = _liveHazardsAtEnd,
            expected_projectiles = _expectedShots, observed_created_projectiles = _created,
            mina_geometry = MinaGeometryResult(),
            koharu_sweeps = KoharuSweepResult(),
            projectile_geometry = _pathChecks.Select(pair => new { key = pair.Key, samples = pair.Value.Samples,
                first_observed_origin_error = pair.Value.OriginError, maximum_path_error = pair.Value.PathError,
                maximum_width_error = pair.Value.WidthError }),
            route = new { spacing = 6, sampled_seconds = .1, speed = _player.SlowestMoveSpeed, lost = _routeLost,
                input_dependent_counterfactual_verified = false, diagnostic_grid_only = true, clearance = 4.25,
                rejects_current_and_previous_blocked_cells = true, fractional_movement_budget_retained = true },
            cue_events = _cues.Select(pair => new { key = pair.Key, starts_at = pair.Value.Start, fires_at = pair.Value.Fire,
                full_at = pair.Value.Full, actual_at = pair.Value.Actual,
                cancelled_before_attack = pair.Value.Cancelled,
                complete_hold_seconds = pair.Value.Actual - pair.Value.Full,
                point_samples = pair.Value.Point, growing_samples = pair.Value.Growing, complete_samples = pair.Value.Complete }),
        };
    }
}
