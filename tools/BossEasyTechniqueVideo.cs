using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private bool _easyMode, _easyStationary, _easyCycle, _bottomProbes, _survival, _pressureAudit;
    private bool _visualLoop;
    private int _reviewFailures;
    private BossCharacterTechnique? _easyAttack;
    private Node2D? _easyRoot;
    private readonly List<object> _easyCases = new();
    private readonly List<object> _easyCycles = new();
    private Vector2 _easyStart;
    private Vector2 _pressureMotion;
    private bool _perimeterProbe;
    private int _perimeterCorner;
    private double _pressureMoveAt, _pressureMoveUntil;
    private float _easyDisplacement;
    private double _settleAt = double.PositiveInfinity;
    private static readonly CircleShape2D EasyPlayerCore = new() { Radius = Player.HitRadius };
    private static readonly CircleShape2D EasyRouteCore = new() { Radius = Player.HitRadius + 4.25f };
    private static Vector2[] EasyStarts => new Vector2[] {
        new(Field.CenterX, Field.CenterY + 48),
        new(Field.Left + 24, Field.CenterY), new(Field.Right - 24, Field.CenterY),
        new(Field.Left + 24, Field.Top + 24), new(Field.Right - 24, Field.Top + 24),
        new(Field.Left + 24, Field.Bottom - 24), new(Field.Right - 24, Field.Bottom - 24),
        new(Field.CenterX, Field.CenterY),
        new(Field.Left + Player.HitRadius, Field.Top + Player.HitRadius), new(Field.Right - Player.HitRadius, Field.Top + Player.HitRadius),
        new(Field.Left + Player.HitRadius, Field.Bottom - Player.HitRadius), new(Field.Right - Player.HitRadius, Field.Bottom - Player.HitRadius),
        new(Field.CenterX, Field.Top + Player.HitRadius), new(Field.CenterX, Field.Bottom - Player.HitRadius),
        new(Field.Left + Player.HitRadius, Field.CenterY), new(Field.Right - Player.HitRadius, Field.CenterY),
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
            if (args.FirstOrDefault(a => a.StartsWith("--two-beat-steps=") || a.StartsWith("--move-steps=")) is { } steps)
            {
                var locale = System.Globalization.CultureInfo.InvariantCulture;
                _scriptedMovement = steps.StartsWith("--move-steps=");
                _twoBeatSteps = steps[(steps.IndexOf('=') + 1)..].Split(',').Select(step => {
                    var parts = step.Split(':');
                    if (parts.Length != 3) throw new ArgumentException("Expected frame:x:y for each movement.");
                    return (int.Parse(parts[0], locale) / 60d, new Vector2(float.Parse(parts[1], locale), float.Parse(parts[2], locale)));
                }).ToArray();
            }
            if (args.FirstOrDefault(arg => arg.StartsWith("--settle-at=")) is { } settle)
                _settleAt = double.Parse(settle[12..], System.Globalization.CultureInfo.InvariantCulture);
            _survival = args.Contains("--survival");
            _sourceCutMiss = args.Contains("--source-cut-miss");
            _bottomProbes = args.Contains("--bottom-probes");
            _pressureAudit = args.Contains("--pressure-audit");
            _perimeterProbe = args.Contains("--perimeter-probe");
            EasyRequire(!_perimeterProbe || _pressureAudit, "Perimeter comparator requires pressure audit.");
            if (_pressureAudit && args.FirstOrDefault(arg => arg.StartsWith("--probe-move=")) is { } motion)
            {
                _pressureMotion = motion[13..] switch { "left" => Vector2.Left, "right" => Vector2.Right,
                    "up" => Vector2.Up, "down" => Vector2.Down, _ => throw new ArgumentException("Unknown probe direction.") };
                _pressureMoveAt = double.Parse(args.First(arg => arg.StartsWith("--probe-move-at="))[16..], System.Globalization.CultureInfo.InvariantCulture);
                _pressureMoveUntil = double.Parse(args.First(arg => arg.StartsWith("--probe-move-until="))[19..], System.Globalization.CultureInfo.InvariantCulture);
            }
            _scene = args.First(a => a.StartsWith("--boss="))[7..];
            bool modelAudit = args.Contains("--model-audit");
            EasyRequire(new[] { "Koharu", "Rei", "MinaBattle" }.Contains(_scene) || (modelAudit || _bottomProbes || _survival || _pressureAudit || args.Contains("--body-attack-qa") || args.Contains("--battle-cycle") || args.Contains("--interrupt-qa") || args.Contains("--performance") || args.Contains("--collision-qa") || args.Contains("--frame-revision-qa") || args.Contains("--frame-shot-qa") || args.Contains("--boss-presence-qa") || args.Contains("--boss-portrait-qa")) && _scene == "Akari", "Unknown boss or unsupported audit mode.");
            _output = ProjectSettings.GlobalizePath(args.First(a => a.StartsWith("--output="))[9..]);
            System.IO.Directory.CreateDirectory(_output);
            bool qa = args.Contains("--qa") || args.Contains("--all-starts") || _bottomProbes;
            bool probes = args.Contains("--stationary-probes") || _bottomProbes || _pressureAudit;
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
            if (args.FirstOrDefault(arg => arg.StartsWith("--job=")) is { } job)
                _game.SelectedJob = Enum.Parse<Job>(job[6..]);
            _game.TrainingSetAllUpgrades(false);
            if (args.Contains("--include-rally")) _game.TrainingSetUpgrade("n_charge_shot", true);
            if (args.Contains("--small-hitbox")) _game.TrainingSetUpgrade("n_hitbox", true);
            foreach (string tutorial in new[] { "once_midboss_shield", "once_tutorial_route", "once_tutorial_shield",
                "once_tutorial_lock", "once_tutorial_bomb", "once_skill_charge", "once_ankers_rei" })
                _game.MarkIdleDialogSeen(tutorial);
            Engine.TimeScale = 1;
            Engine.PhysicsTicksPerSecond = 60;
            GD.Seed(args.FirstOrDefault(arg => arg.StartsWith("--seed=")) is { } seed ? uint.Parse(seed[7..]) : 2081u);
            Input.UseAccumulatedInput = false;
            if (!headless)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            await Frames(2);
            _pool = GetNode<BulletPool>("/root/Pool");
            bool solo = args.Contains("--solo");
            _game.PendingBossIndex = _scene switch { "Akari" => 0, "Koharu" => 1, "Rei" => 2, _ => 3 };
            _easyRoot = GD.Load<PackedScene>(solo ? BossRushRoot.SingleScenePath : $"res://{_scene}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(_easyRoot);
            GetTree().CurrentScene = _easyRoot;
            _easyRoot.SetProcess(false);
            if (solo) Call(_easyRoot, "SpawnBoss");
            else ((Node)_easyRoot.GetType().GetProperty("Stage")!.GetValue(_easyRoot)!).SetProcess(false);
            _easyRoot.GetNode<Node2D>("World").ProcessMode = ProcessModeEnum.Inherit;
            _player = _easyRoot.GetNode<Player>("World/Player");
            if (args.FirstOrDefault(arg => arg.StartsWith("--job=")) is { } requestedJob)
                EasyRequire(_game.SelectedJob == Enum.Parse<Job>(requestedJob[6..]), "The story scene changed the selected job; use --solo to verify another playable character.");
            _player.SetPhysicsProcess(false);
            _hud = _easyRoot.GetNode<Hud>("Hud");
            _hud.HoldBubble = false;
            _hud.HideBubble();
            Hud.BubblePaused = false;
            _boss = solo ? ((BossRushRoot)_easyRoot).Boss!
                : _scene switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(), "Rei" => new BossRei(), _ => new BossMina() };
            _boss.Position = new Vector2(Field.BossFrameCenterX, Field.BossZoneCenterY);
            if (!solo) _easyRoot.GetNode<Node2D>("World").AddChild(_boss);
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
            Write(_player, "_invincible", !_survival);
            Write(_player, "_invincibleTimer", _survival ? 0f : 99999f);
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
            GD.Print($"[EASY] CONDITIONS boss={_scene} difficulty={_game.Difficulty} fps=60 time_scale=1 invincible={!_survival} bombs=false dodge=false; automatic input, not a human-comprehension test.");
            bool nativeRoster = args.Contains("--native-roster");
            bool finalSegment = _easyCycle ? _boss.UsesTechniqueCycle : nativeRoster;
            int phases = _boss is BossMina ? 5 : finalSegment && _boss is BossRei or BossKoharu ? 3 : 2;
            if (args.Contains("--frame-cameo-qa"))
            {
                await RunFrameCameoQa();
                phases = 0;
            }
            var onlyArg = args.FirstOrDefault(a => a.StartsWith("--only="));
            HashSet<string>? only = onlyArg == null ? null : onlyArg[7..].Split(',').ToHashSet();
            var startsArg = args.FirstOrDefault(a => a.StartsWith("--starts="));
            HashSet<int>? selectedStarts = startsArg == null ? null : startsArg[9..].Split(',').Select(int.Parse).ToHashSet();
            Vector2[]? probeOrigins = null;
            if (args.FirstOrDefault(a => a.StartsWith("--probe-origins=")) is { } origins)
            {
                EasyRequire(_pressureAudit && !_survival && !_bottomProbes && !_perimeterProbe && !args.Contains("--edge-starts"),
                    "Custom probe origins require a pressure audit with a single starting-position mode.");
                probeOrigins = origins["--probe-origins=".Length..].Split(',').Select(point => {
                    var xy = point.Split(':');
                    if (xy.Length != 2) throw new ArgumentException("Expected a probe origin as x:y.");
                    return new Vector2(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
                }).ToArray();
            }
            var passedArg = args.FirstOrDefault(a => a.StartsWith("--skip-passed="));
            var passedCases = passedArg == null ? new HashSet<string>()
                : JsonSerializer.Deserialize<HashSet<string>>(System.IO.File.ReadAllText(passedArg[14..]))!;
            for (int phase = 0; phase < phases; phase++)
            {
                await SetEasyPhase(phase);
                if (args.Contains("--body-attack-qa"))
                {
                    await RunBodyAttackQa(phase);
                    continue;
                }
                if (args.Contains("--frame-shot-qa"))
                {
                    await RunFrameTargetQa(phase);
                    continue;
                }
                if (args.Contains("--frame-revision-qa"))
                {
                    await RunFrameRevisionQa(phase);
                    continue;
                }
                if (args.Contains("--boss-presence-qa") || args.Contains("--boss-portrait-qa"))
                {
                    await RunBossPresenceQa(phase);
                    continue;
                }
                if (args.Contains("--rei-color-qa"))
                {
                    EasyRequire(_scene == "Rei", "Color-call QA requires Rei.");
                    await RunReiColorCallQa(phase);
                    continue;
                }
                if (args.Contains("--collision-qa"))
                {
                    if (phase <= 1) await RunReiCollisionQa(phase, allCharacters: true);
                    continue;
                }
                if (args.Contains("--rei-selection-qa"))
                {
                    EasyRequire(_scene == "Rei", "Selection QA requires Rei.");
                    await RunReiSelectionQa(phase);
                    continue;
                }
                if (args.Contains("--rei-source-cut-qa"))
                {
                    EasyRequire(_scene == "Rei", "Source-cut QA requires Rei.");
                    await RunReiSourceCutQa(phase);
                    continue;
                }
                if (args.Contains("--rei-rally-presentation-qa"))
                {
                    EasyRequire(_scene == "Rei", "Rally presentation QA requires Rei.");
                    await RunReiRallyPresentationQa(phase);
                    continue;
                }
                if (args.Contains("--rei-rally-qa"))
                {
                    EasyRequire(_scene == "Rei", "Rally QA requires Rei.");
                    await RunReiRallyQa(phase);
                    continue;
                }
                if (args.Contains("--rei-collision-qa"))
                {
                    EasyRequire(_scene == "Rei", "Rei collision QA requires Rei.");
                    await RunReiCollisionQa(phase);
                    continue;
                }
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
                var slots = (nativeRoster ? BossAttackCycle.Slots(_boss, phase) : BossAttackCycle.EasySlots(_boss, phase)).Distinct().ToList();
                if (!nativeRoster && phase == phases - 1 && _boss is BossRei or BossKoharu) slots.Add(new(BossTechniqueKind.Finale));
                foreach (var slot in slots)
                {
                    string id = $"{phase}:{slot.Kind}:{slot.Variant}";
                    if (only != null && !only.Contains(id)) continue;
                    if (modelAudit)
                    {
                        await AuditAttackModels(phase, slot);
                        continue;
                    }
                    if (args.Contains("--interrupt-qa"))
                    {
                        await CheckTechniqueInterruption(phase, slot);
                        continue;
                    }
                    int starts = probeOrigins?.Length ?? (probes || args.Contains("--all-starts") ? EasyStarts.Length : qa ? 2 : 1);
                    if (args.Contains("--performance"))
                    {
                        if (_boss is BossRei && slot.Kind == BossTechniqueKind.Normal && slot.Variant is 3 or 4)
                        {
                            PrepareSourceCutPanels();
                            if (slot.Variant == 4) Write(_boss, "_hp", _boss.BarHp * _boss.TotalBars);
                        }
                        else _boss.SetPanelsInvulnerable(true);
                        await RunEasyPerformance(phase, slot);
                    }
                    else for (int start = 0; start < starts; start++)
                    {
                        if (args.FirstOrDefault(a => a.StartsWith("--start=")) is { } requestedStart && start != int.Parse(requestedStart[8..])) continue;
                        if (selectedStarts != null && !selectedStarts.Contains(start)) continue;
                        if (passedCases.Contains($"{phase}:{slot.Kind}:{slot.Variant}:{start}")) continue;
                        if (_boss is BossRei && slot.Kind == BossTechniqueKind.Normal && slot.Variant is 3 or 4)
                        {
                            PrepareSourceCutPanels();
                            if (slot.Variant == 4) Write(_boss, "_hp", _boss.BarHp * _boss.TotalBars);
                        }
                        else _boss.SetPanelsInvulnerable(true);
                        try
                        {
                            if (_survival) await RunSurvivalClip(phase, slot, start);
                            else await RunEasyClip(phase, slot, start, probes && !args.Contains("--cue-pilot"), qa, probeOrigins?[start]);
                        }
                        catch (Exception error) when (args.Contains("--review-all"))
                        {
                            _reviewFailures++;
                            _easyCases.Add(new { character = _scene, difficulty = _game.Difficulty.ToString(), phase,
                                kind = slot.Kind.ToString(), variant = slot.Variant, start_index = start,
                                name = _easyAttack?.SpellName, mechanic = _easyAttack?.MechanicKey,
                                counterplay = _easyAttack?.Counterplay, elapsed = _easyAttack?.Elapsed,
                                error = error.Message, completed = false });
                            GD.Print($"[REVIEW] FAIL {phase}:{slot.Kind}:{slot.Variant}:{start}: {error.Message}");
                            _running = false;
                            _pilot.Release();
                            _easyAttack?.Cancel();
                            _easyAttack = null;
                            _pool.DespawnAll();
                            await SettleEasyFrame();
                            System.IO.File.WriteAllText(_output + "/chapters.json", JsonSerializer.Serialize(_easyCases,
                                new JsonSerializerOptions { WriteIndented = true }));
                        }
                    }
                }
            }
            System.IO.File.WriteAllText(_output + "/chapters.json", JsonSerializer.Serialize(_easyCases,
                new JsonSerializerOptions { WriteIndented = true }));
            System.IO.File.WriteAllText(_output + "/cycles.json", JsonSerializer.Serialize(_easyCycles,
                new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[EASY] COMPLETE boss={_scene} clips={_easyCases.Count} cycle_phases={_easyCycles.Count}");
            EasyRequire(_reviewFailures == 0, $"Review recorded {_reviewFailures} failed cases; see chapters.json.");
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
        if (exit == 0 && (OS.GetCmdlineUserArgs().Contains("--boss-presence-qa")
            || OS.GetCmdlineUserArgs().Contains("--boss-portrait-qa")))
        {
            if (Field.BossArenaActive || Field.BossFrameActive || Field.Right != Field.WorldRight)
            {
                exit = 1;
                GD.PushError("[BOSS-PRESENCE] FAIL: Freeing the battle scene left a reserved boss frame.");
            }
            else GD.Print("[BOSS-PRESENCE] TEARDOWN boss_registry_empty=true right=384 frame_active=false");
        }
        await Task.Delay(250);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GetTree().Quit(exit);
    }

    private async Task SetEasyPhase(int phase)
    {
        _running = false;
        _pilot.Release();
        await SettleEasyFrame();
        EasyRequire(_pool.GetChildren().OfType<Bullet>().All(b => !b.Active || !b.IsEnemy), "Previous technique left active bullets before phase transition.");
        if (_boss is BossMina mina)
        {
            Write(mina, "_pattern", phase);
            Read<MinaPhaseAttacks>(mina, "_caster").BeginPhase(phase);
            Call(mina, "ChangeBattleCostume", BossMina.BattleCostumePath(phase, "idle"), BossMina.BattleCostumePath(phase, "attack"), BossMina.BattleDownPath(phase));
            mina.GlobalPosition = new Vector2(Field.BossFrameCenterX, Field.BossZoneCenterY);
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
            _boss.FinishFormReveal();
            EasyRequire(!Read<bool>(_boss, "_swapAnim"), "Transformation left the outgoing body visible.");
        }
        if ((_easyCycle || OS.GetCmdlineUserArgs().Contains("--native-roster")) && phase == 2 && _boss is BossRei or BossKoharu)
            Write(_boss, "_hp", (int)(_boss.BarHp * _boss.TotalBars * .2));
        if (_boss is not BossAkari) Call(_boss, _boss is BossRei ? "ApplyPhaseVisual" : "ApplySpell");
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

    private bool _performanceVisual;

    private async Task AuditAttackModels(int phase, BossAttackCycle.Slot slot)
    {
        await SettleEasyFrame();
        _player.GlobalPosition = EasyStarts[0];
        _hud.HideSpellCard();
        _hud.HideBubble();
        Hud.BubblePaused = false;
        var attack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
        attack.SetPhysicsProcess(false);
        var models = new HashSet<string>();
        var shapes = new HashSet<int>();
        int ticks = 0;
        const double dt = 1.0 / 30;
        bool body = false;
        bool native = attack is MinaArchiveTechnique or ReiBossTechnique { RallyTechnique: true };
        for (; ticks < 3600 && !attack.Finished; ticks++)
        {
            _boss.TickBossFrame(dt);
            if (attack.MechanicKey == "koharu.attention-charge")
                _player.GlobalPosition = _boss.TechniqueCenter.Clamp(Field.Rect.Position, Field.Rect.End);
            if (attack.MechanicKey == "mina.second-visit-weight")
                _player.GlobalPosition = Field.Rect.GetCenter() + new Vector2(48 * Mathf.Sin(ticks * (float)dt * 1.8f), 0);
            attack.Tick(dt);
            body |= _boss.BossInArena;
            foreach (var bullet in attack.OwnedShots.ToArray())
            {
                string model = bullet.ModelAnimationName ?? throw new Exception($"{attack.SpellName}: flat enemy shot");
                models.Add(model);
                bullet.StepEnemy(dt, 1);
            }
            foreach (var h in attack.PracticeHazards.Where(h => h.Live))
            {
                shapes.Add(h.Shape);
                if (native || attack.Finished) continue;
                var layer = attack.GetChildren().OfType<AttackLineLayer>().Single();
                bool line = h.Shape == 0 && h.A != h.B;
                EasyRequire((line ? layer.RenderedLines : layer.RenderedSurfaces) > 0, $"{attack.SpellName}: flat live area");
            }
            if (!attack.Finished && ticks % 60 == 59) await EasyPhysicsFrames(1);
        }
        EasyRequire(models.Count > 0 || shapes.Count > 0 || native || body,
            $"{attack.SpellName}: no rendered attack observed");
        _easyCases.Add(new { phase, kind = slot.Kind.ToString(), slot.Variant, attack.SpellName, models, shapes, native, body,
            attack.Completed, seconds = ticks * dt });
        GD.Print($"[ModelAudit] {phase}/{slot.Kind}/{slot.Variant} {attack.SpellName} models={models.Count} areas={shapes.Count}");
        attack.Cancel();
        _pool.DespawnAll();
        await EasyPhysicsFrames(2);
    }

    private async Task RunEasyPerformance(int phase, BossAttackCycle.Slot slot)
    {
        _running = false;
        _pilot.Release();
        await SettleEasyFrame();
        bool visualQa = OS.GetCmdlineUserArgs().Contains("--visual-qa") && DisplayServer.GetName() != "headless";
        _performanceVisual = visualQa;
        if (visualQa) _boss.SetPanelsInvulnerable(false);
        _visualLoop = OS.GetCmdlineUserArgs().Contains("--visual-loop");
        _easyStationary = !_visualLoop;
        _easyDisplacement = 0;
        _easyStart = EasyStarts[0];
        _player.GlobalPosition = _easyStart;
        _hud.HideSpellCard();
        await EasyPhysicsFrames(12);
        long firstFrame = (long)Engine.GetProcessFrames();
        _easyAttack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
        _running = true;
        for (int frame = 0; frame < 180 && string.IsNullOrEmpty(_easyAttack.SpellName); frame++)
            await EasyPhysicsFrames(1);
        EasyRequire(!string.IsNullOrEmpty(_easyAttack.SpellName), "Technique did not begin after arena entry.");
        string key = _easyAttack.MechanicKey;
        var attackPresentation = Read<AttackPresentation>(_easyAttack, "_presentation");
        int telegraphDraws = 0;
        Read<Node2D>(_easyAttack, "_timingCanvas").Draw += () => telegraphDraws++;
        var frames = new List<double>();
        var process = new List<double>();
        var physics = new List<double>();
        var draws = new List<double>();
        var slowFrames = new List<object>();
        int contacts = 0;
        int peakShots = 0, peakCollisionParts = 0, peakContours = 0;
        bool lineQa = OS.GetCmdlineUserArgs().Contains("--line-qa");
        var captured = new HashSet<string>();
        int captureFrames = 0;
        bool presentationFrames = visualQa && OS.GetCmdlineUserArgs().Contains("--presentation-frames");
        string frameDirectory = $"{_output}/{phase}_{slot.Kind}_{slot.Variant}_frames";
        if (presentationFrames) DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(frameDirectory));
        int presentationFrame = 0;
        double nextPresentationFrame = 0;
        var projectileVisuals = new Dictionary<string, string>();
        var modelVisuals = new HashSet<string>();
        double firstLive = -1;
        int lineFrames = 0;
        bool ended = false, completed = false;
        _easyAttack.Ended += attack => { completed = attack.Completed; ended = true; };
        _running = true;
        ulong previous = Godot.Time.GetTicksUsec();
        while (!ended && _easyAttack.Elapsed < 180)
        {
            await Frames(1);
            if (ended) break;
            bool capturedFrame = false;
            bool live = _easyAttack.PracticeHazards.Any(h => h.Live)
                || _easyAttack.OwnedShots.Any(shot => !shot.LeadIn && shot.CollisionLayer != 0);
            EasyRequire(_easyAttack.OwnedShots.All(shot => shot.ZIndex > attackPresentation.ZIndex),
                "Presentation must stay behind live projectiles");
            if (live && firstLive < 0) firstLive = _easyAttack.Elapsed;
            if (_easyAttack is AkariBossTechnique { MechanicKey: "akari.proximity-addressing" } addressing)
                EasyRequire(addressing.AddressLocks.Select(item => item.Envelope).Distinct().Count() == addressing.AddressLocks.Count,
                    "Each envelope must lock its recipient once.");
            if (visualQa)
            {
                string? stage = _visualLoop && live && !captured.Contains("impact") ? "impact"
                    : _easyAttack.Elapsed >= .8 && !live && !captured.Contains("warning") ? "warning"
                    : live && _easyAttack.Elapsed >= firstLive + .7 && !captured.Contains("live") ? "live"
                    : _easyAttack.Elapsed >= 5 && !captured.Contains("five") ? "five"
                    : _easyAttack.Elapsed >= 10 && !captured.Contains("ten") ? "ten" : null;
                if (stage == null && !live && !captured.Contains("formed_warning")
                    && _easyAttack.PracticeHazards.Any(h => !h.Live && h.Progress >= .8f))
                    stage = "formed_warning";
                if (stage == null && _easyAttack is ReiBossTechnique rei)
                {
                    if (!captured.Contains("locked_warning") && rei.SourceCutTechnique && rei.SourceCutRounds.Any(round =>
                        round.Locked && rei.Elapsed >= round.LocksAt + .75 && rei.Elapsed < round.FiresAt))
                        stage = "locked_warning";
                    else if (!captured.Contains("retaliation_warning") && rei.RallyTechnique
                        && rei.PracticeHazards.Any(h => !h.Live && h.Shape == 2 && h.Progress > .2f))
                        stage = "retaliation_warning";
                    else if (!captured.Contains("split_warning") && key == "rei-merge-then-branch"
                        && rei.Elapsed >= Read<double>(rei, "_warning") + 1.9 && !rei.Finished)
                        stage = "split_warning";
                }
                if (stage == null && _easyAttack is AkariBossTechnique { AddressDeadlineMissed: true }
                    && key == "akari.proximity-addressing")
                    stage = _easyAttack.Elapsed >= Read<double>(_easyAttack, "_warning") + 13.6
                        && !captured.Contains("deadline_fire") ? "deadline_fire"
                        : !captured.Contains("deadline_warning") ? "deadline_warning" : null;
                if (stage == null && _easyAttack is MinaBossTechnique memory && key == "mina.second-visit-weight" && _game.IsLunatic)
                {
                    if (!captured.Contains("echo_warning") && memory.VisibleVisitEchoes.Any(echo =>
                        _easyAttack.Elapsed >= echo.StartsAt + .3 && _easyAttack.Elapsed < echo.FiresAt))
                        stage = "echo_warning";
                    else if (!captured.Contains("echo_live") && memory.VisibleVisitEchoes.Any(echo =>
                        _easyAttack.Elapsed >= echo.FiresAt + .15 && _easyAttack.Elapsed < echo.EndsAt))
                        stage = "echo_live";
                }
                if (stage == null && key == "mina.wing-displacement")
                {
                    double age = _easyAttack.Elapsed - Read<double>(_easyAttack, "_warning");
                    stage = new[] { 3.7, 4.7, 4.9, 8.5, 9.5, 9.7 }
                        .Where(at => age >= at && !captured.Contains($"wind_{at:F1}"))
                        .Select(at => $"wind_{at:F1}").FirstOrDefault();
                }
                if (stage == null && key == "akari.solid-umbrellas" && _game.IsLunatic)
                {
                    double age = _easyAttack.Elapsed - Read<double>(_easyAttack, "_warning");
                    stage = new[] { 3.7, 5.7, 13.7, 15.7 }
                        .Where(at => age >= at && !captured.Contains($"umbrella_{at:F1}"))
                        .Select(at => $"umbrella_{at:F1}").FirstOrDefault();
                }
                if (stage == null && _game.IsLunatic && key is "akari.exchanged-priority" or "akari.paused-outbox")
                {
                    string field = key == "akari.exchanged-priority" ? "_priorityBatches" : "_outboxBatches";
                    int batchIndex = 0;
                    foreach (var batch in Read<System.Collections.IEnumerable>(_easyAttack, field))
                    {
                        double starts = (double)batch.GetType().GetProperty("StartsAt")!.GetValue(batch)!;
                        double fires = (double)batch.GetType().GetProperty("FiresAt")!.GetValue(batch)!;
                        var moments = key == "akari.exchanged-priority"
                            ? new[] { ("warning", starts + 1.1), ("travel", fires + .7), ("exchange", fires + 1.9) }
                            : new[] { ("warning", starts + 1.1), ("paused", fires + 2.1), ("restart", fires + 4.9) };
                        foreach (var (label, at) in moments)
                        {
                            string candidate = $"batch_{batchIndex}_{label}";
                            if (_easyAttack.Elapsed >= at && !captured.Contains(candidate)) { stage = candidate; break; }
                        }
                        if (stage != null) break;
                        batchIndex++;
                    }
                }
                if (stage == null && _easyAttack is KoharuBossTechnique && !captured.Contains("drop_warning")
                    && _easyAttack.EasyCues.Any(cue => cue.Key.StartsWith("support-drop:") && cue.Progress >= 1 && !cue.Live))
                    stage = "drop_warning";
                bool record = presentationFrames && _easyAttack.Elapsed >= nextPresentationFrame
                    && _easyAttack.Elapsed < 9 && (firstLive < 0 || _easyAttack.Elapsed < firstLive + 3);
                if (stage != null || record)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var shot = GetViewport().GetTexture().GetImage();
                    if (stage != null)
                    {
                        captured.Add(stage);
                        EasyRequire(shot.SavePng($"{_output}/{phase}_{slot.Kind}_{slot.Variant}_{stage}.png") == Error.Ok, "Save visual QA screenshot");
                    }
                    if (record)
                    {
                        EasyRequire(shot.SaveJpg($"{frameDirectory}/frame_{presentationFrame++:D4}.jpg", .94f) == Error.Ok,
                            "Save presentation frame");
                        nextPresentationFrame += .05;
                    }
                    capturedFrame = true;
                }
            }
            if (lineQa)
            {
                var expected = _easyAttack.PracticeHazards.Where(h => h.Live && h.Shape == 0 && h.A != h.B)
                    .Select(h => (h.A, h.B, h.Radius)).ToHashSet();
                var rendered = new HashSet<(Vector2, Vector2, float)>();
                foreach (var line in _easyAttack.GetChildren().OfType<AttackLineLayer>()
                    .SelectMany(layer => layer.GetChildren().OfType<Line2D>()).Where(line => line.Visible))
                {
                    var points = line.Points;
                    for (int i = 1; i < points.Length; i++) rendered.Add((points[i - 1], points[i], line.Width * .5f));
                }
                EasyRequire(expected.SetEquals(rendered), "Rendered line differs from live collision geometry: " + key);
                if (expected.Count > 0 && lineFrames++ == 0 && DisplayServer.GetName() != "headless")
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var shot = GetViewport().GetTexture().GetImage();
                    EasyRequire(shot.SavePng($"{_output}/line_{phase}_{slot.Kind}_{slot.Variant}.png") == Error.Ok, "Save live line screenshot");
                    capturedFrame = true;
                }
            }
            ulong now = Godot.Time.GetTicksUsec();
            if (capturedFrame)
            {
                captureFrames++;
                previous = now;
                continue;
            }
            frames.Add((now - previous) / 1000d);
            previous = now;
            process.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
            physics.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000);
            draws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            if (frames[^1] > 33.34)
                slowFrames.Add(new { at = _easyAttack.Elapsed, frame_ms = frames[^1], shots = _easyAttack.ShotsCreated,
                    draw_calls = draws[^1], physics_ms = physics[^1], gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2) });
            if (frames.Count % 30 == 0)
            {
                var shots = _easyAttack.OwnedShots.ToArray();
                foreach (var shot in shots)
                {
                    if (shot.ModelAnimationName is { } modelName) modelVisuals.Add(modelName);
                    if (shot.EnemyIllustration is not { } art) continue;
                    string asset = art.Texture is AtlasTexture atlas ? $"{atlas.Atlas.ResourcePath}:{atlas.Region}" : art.Texture.ResourcePath;
                    projectileVisuals[asset] = art.Accent.ToHtml();
                    EasyRequire(shot.Footprint is { Shapes.Length: > 0 } && float.IsFinite(shot.CollisionRadius),
                        "Projectile illustration has no valid collision outline: " + asset);
                }
                peakShots = Math.Max(peakShots, shots.Length);
                peakCollisionParts = Math.Max(peakCollisionParts, shots.Sum(shot => shot.Footprint?.Shapes.Length ?? 1));
                peakContours = Math.Max(peakContours, shots.Sum(shot => shot.Footprint?.Contours.Length ?? 0));
            }
            if (_easyAttack.CoversPoint(_player.GlobalPosition)) contacts++;
        }
        _running = false;
        _pilot.Release();
        EasyRequire(ended && completed, "Performance capture did not complete naturally: " + key);
        if (_visualLoop)
            EasyRequire(_easyDisplacement > 20 && _easyAttack.HazardFrames > 0, "Movement probe must activate hazards through normal input");
        if (lineQa) GD.Print($"[AttackLineQA] PASS live geometry and natural completion {key} line_frames={lineFrames}");
        if (await SettleEasyFrame(48, visualQa ? $"{_output}/{phase}_{slot.Kind}_{slot.Variant}" : null) && visualQa)
        {
            captured.Add("return");
            captured.Add("restored");
        }
        object Summary(List<double> values)
        {
            values.Sort();
            return new { mean = values.Average(), p95 = values[(int)((values.Count - 1) * .95)],
                p99 = values[(int)((values.Count - 1) * .99)], maximum = values[^1] };
        }
        var result = new { character = _scene, difficulty = _game.Difficulty.ToString(), phase, mechanic = key,
            kind = slot.Kind.ToString(), variant = slot.Variant, name = _easyAttack.SpellName,
            start = firstFrame / 60d, end = Engine.GetProcessFrames() / 60d, line_frames = lineFrames,
            visual_stages = captured.ToArray(),
            projectile_visuals = projectileVisuals,
            model_visuals = modelVisuals, counterplay = _easyAttack.Counterplay,
            warning_illustration = (typeof(BossCharacterTechnique).GetProperty("WarningVisual", Private)!.GetValue(_easyAttack)
                as BulletArt.PlayerVisual)?.Texture.ResourcePath,
            completed, shots = _easyAttack.ShotsCreated, frames = frames.Count, frame_ms = Summary(frames), process_ms = Summary(process),
            excluded_capture_frames = captureFrames,
            physics_ms = Summary(physics), draw_calls = Summary(draws), telegraph_redraws = telegraphDraws,
            frames_over_33ms = frames.Count(value => value > 33.34),
            peak_shots = peakShots, peak_collision_parts = peakCollisionParts, peak_contours = peakContours,
            invincible = true, stationary = _easyStationary, shooting = false, live_hazard_contact_frames = contacts,
            movement = _visualLoop ? "horizontal key input, reversing every 0.9 seconds" : "stationary",
            particle_launches = attackPresentation.Launches, particle_dissolves = attackPresentation.Dissolves,
            geometry_audit = false, human_difficulty_test = false };
        System.IO.File.WriteAllText($"{_output}/slow_{phase}_{slot.Kind}_{slot.Variant}.json", JsonSerializer.Serialize(slowFrames));
        _easyCases.Add(result);
        GD.Print("[PERFORMANCE] " + JsonSerializer.Serialize(result));
        _easyAttack = null;
        _performanceVisual = false;
        if (visualQa) _boss.SetPanelsInvulnerable(true);
    }

    private async Task RunEasyClip(int phase, BossAttackCycle.Slot slot, int start, bool stationary, bool qa, Vector2? probeOrigin = null)
    {
        _running = false;
        _pilot.Release();
        await SettleEasyFrame();
        _easyStationary = stationary;
        _easyStart = probeOrigin ?? (_bottomProbes ? new Vector2(Field.Left + Field.Width * start / (EasyStarts.Length - 1), Field.Bottom) : EasyStarts[start]);
        if (_perimeterProbe) { _easyStart = new Vector2(Field.Left + 12, Field.Top + 12); _perimeterCorner = 0; }
        if (OS.GetCmdlineUserArgs().Contains("--edge-starts"))
            _easyStart = start % 2 == 0 ? Field.Rect.Position : Field.Rect.End;
        _boss.GlobalPosition = new Vector2(Field.BossFrameCenterX, Field.BossZoneCenterY);
        _player.GlobalPosition = _easyStart;
        _easyDisplacement = 0;
        _hud.HideSpellCard();
        await EasyPhysicsFrames(12);
        long firstFrame = (long)Engine.GetProcessFrames();
        _easyAttack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
        _running = true;
        if (_boss.BossFrameEntering && stationary && !_bottomProbes)
        {
            _easyStart = probeOrigin ?? EasyStarts[start];
            _player.GlobalPosition = _easyStart;
        }
        for (int frame = 0; frame < 180 && _easyAttack.SpellName.Length == 0 && !_easyAttack.Finished; frame++)
            await EasyPhysicsFrames(1);
        EasyRequire(_easyAttack.SpellName.Length > 0, "Technique did not start after entering the arena.");
        string name = _easyAttack.SpellName, key = _easyAttack.MechanicKey;
        if (OS.GetCmdlineUserArgs().Contains("--hitbox-qa")) CheckPlayerHitBoundary(_easyAttack);
        var peeledOption = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--single-edition="));
        if (peeledOption != null) Read<bool[]>(_easyAttack, "_peeledEditions")[int.Parse(peeledOption[17..])] = true;
        var audit = new EasyAttackAudit(_easyAttack, _player, _boss, _easyStart);
        using var passage = HardPassageTrace.Open($"{_output}/opening_{phase}_{slot.Kind}_{slot.Variant}_{start}.jsonl");
        int contactsAtSettle = 0, bodyContactsAtSettle = 0;
        Vector2? settledPosition = null;
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
        var bodyPoses = new HashSet<string>();
        for (int frame = 0; frame < 180 * 60 && !ended; frame++)
        {
            await EasyPhysicsFrames(1);
            if (!ended)
            {
                EasyRequire(_hud.ActiveTechniqueNames == name, "Active technique name disappeared: " + name);
                audit.Observe(_easyAttack, _player.GlobalPosition);
                passage?.Frame(_easyAttack, _player.GlobalPosition);
                if (settledPosition == null && _easyAttack.Elapsed >= _settleAt)
                {
                    settledPosition = _player.GlobalPosition;
                    contactsAtSettle = audit.Contacts;
                    bodyContactsAtSettle = audit.BodyContacts;
                }
                bodyPoses.Add(Read<Sprite2D>(_boss, "_bodySprite").Texture.ResourcePath);
            }
        }
        _running = false;
        _pilot.Release();
        EasyRequire(ended && completed && (_pressureAudit || shots > 0 || hazardFrames > 0),
            $"Technique did not complete naturally: {key} ended={ended} completed={completed} elapsed={_easyAttack.Elapsed} shots={shots} hazards={hazardFrames}");
        audit.Validate();
        if (!_pressureAudit && _scene == "Rei" && phase == 0)
            foreach (string pose in _easyAttack is ReiBossTechnique { SourceCutTechnique: true }
                ? new[] { "cast_v3/frame_00", "cast_v3/frame_02", "cast_v3/frame_03", "cast_v3/frame_04", "cast_v3/frame_07" }
                : _easyAttack is ReiBossTechnique { RallyTechnique: true }
                ? new[] { "glide_v3/frame_00", "cast_v3/frame_03", "cast_v3/frame_04" }
                : new[] { "glide_v3/frame_00", "cast_v3/frame_02", "cast_v3/frame_04" })
                EasyRequire(bodyPoses.Contains($"res://char/v3/boss_motion/rei_{pose}.png"), "Rei pose was not shown: " + pose);
        EasyRequire(_pressureAudit || !stationary || _easyDisplacement < .01f, "Stationary probe moved.");
        await SettleEasyFrame(48);
        long lastFrame = (long)Engine.GetProcessFrames();
        var result = new {
            character = _scene, difficulty = _game.Difficulty.ToString(), phase, kind = slot.Kind.ToString(), variant = slot.Variant,
            name, mechanic = key, counterplay = _easyAttack.Counterplay,
            envelopes = _easyAttack is AkariBossTechnique { MechanicKey: "akari.proximity-addressing" } desk
                ? new { count = desk.AddressLocks.Count, approached = desk.ProximityAddresses, shots = desk.AddressShotsFired,
                    locks = desk.AddressLocks.Select(l => new { id = l.Envelope, x = l.Origin.X, y = l.Origin.Y,
                        targetX = l.Recipient.X, targetY = l.Recipient.Y, locked = l.LockedAt, fired = l.FiresAt, approached = l.Approached }) } : null,
            start = firstFrame, end = lastFrame, fps = 60, completed, shots, hazardFrames,
            start_index = start, stationary = stationary && !_perimeterProbe && _pressureMotion == Vector2.Zero && _easyDisplacement < .01f,
            no_input = stationary && !_perimeterProbe && _pressureMotion == Vector2.Zero, bottom_boundary_probe = _bottomProbes, maximum_player_displacement = _easyDisplacement,
            timed_movement = new { x = _pressureMotion.X, y = _pressureMotion.Y, starts_at = _pressureMoveAt, ends_at = _pressureMoveUntil },
            settled_probe = settledPosition is { } settled ? new { at = _settleAt, x = settled.X, y = settled.Y,
                attack_contact_frames = audit.Contacts - contactsAtSettle, body_contact_frames = audit.BodyContacts - bodyContactsAtSettle } : null,
            capture = new { invincible = true, shooting = !stationary && _easyAttack is ReiBossTechnique { RallyTechnique: true }, bombs = false, dodge = false,
                panels_invulnerable = _easyAttack is not ReiBossTechnique { SourceCutTechnique: true },
                boss_technique_practice = _boss.TechniquePractice,
                source_cut_miss = _sourceCutMiss,
                start_position = new { x = _easyStart.X, y = _easyStart.Y },
                mouse_follow = false,
                visual_comprehension_test = false, human_difficulty_test = false, qa,
                player_job = _game.SelectedJob.ToString(), movement_speed = _player.MoveSpeed,
                movement = _perimeterProbe ? "clockwise perimeter walking at slowest normal speed; only player position and arena bounds; no attack state or collision avoidance"
                    : _pressureMotion != Vector2.Zero ? "one timed directional input, then no input; no future-state access"
                    : stationary ? _easyDisplacement < .01f ? "stationary fresh start" : "no input; displaced by the mechanic" : EasyPilotPolicy() },
            audit = audit.Result(),
            panel_rewards = (_easyAttack as ReiBossTechnique)?.SourceCutRounds.Sum(round => round.RemovedPanels) ?? 0,
            rally = _easyAttack is ReiBossTechnique { RallyTechnique: true } rally ? new {
                failures = rally.RallyFailures, streak = rally.RallyStreak, wall_broken = rally.RallyWallBroken,
                damage_events = rally.RallyDamageEvents,
                balls = rally.RallyBalls.Select(ball => new { ball.Index, ball.IsDanger, ball.Returns,
                    ball.LeftBounces, ball.RightBounces, ball.TopBounces, ball.BottomBounces }).ToArray(),
                remaining_hp = Read<int>(_boss, "_hp"), movement_speed_limit = _player.SlowestMoveSpeed } : null,
            body_poses = bodyPoses,
        };
        _easyCases.Add(result);
        System.IO.File.WriteAllText(_output + "/chapters.json", JsonSerializer.Serialize(_easyCases,
            new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"[EASY] END {key} shots={shots} hazards={hazardFrames} contacts={audit.Contacts} body_contacts={audit.BodyContacts}");
        _easyAttack = null;
    }

    private void CheckPlayerHitBoundary(BossCharacterTechnique attack)
    {
        var marker = _player.GetNode<PlayerHitDot>("HitDot");
        var body = _player.GetNode<CollisionShape2D>("HitShape");
        float radius = ((CircleShape2D)body.Shape).Radius;
        EasyRequire(radius == marker.Radius && radius == _player.CurrentHitRadius
            && marker.GlobalPosition == body.GlobalPosition && body.GlobalPosition == _player.GlobalPosition,
            "Player marker and physics shape differ.");
        var shapes = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(attack, "_hazards");
        var saved = shapes.ToArray();
        Vector2 center = Field.Rect.GetCenter();
        try
        {
            foreach (int kind in new[] { 0, 1, 2, 3 })
            {
                Vector2 end = kind == 0 ? center + Vector2.Right * 12 : kind == 2 ? center + Vector2.One * 12 : center;
                shapes.Clear();
                shapes.Add((center, end, kind == 2 ? 0 : 9, true, 1, kind));
                Vector2 axis = kind == 2 ? -Vector2.One.Normalized() : Vector2.Up;
                float edge = kind switch { 2 => radius, 3 => 9 - radius, _ => 9 + radius };
                float outward = kind == 3 ? -1 : 1;
                EasyRequire(!attack.CoversPoint(center + axis * (edge + outward * .05f))
                    && attack.CoversPoint(center + axis * (edge - outward * .05f)), $"Marker contact differs at shape {kind}.");
            }
        }
        finally { shapes.Clear(); shapes.AddRange(saved); }
        GD.Print($"[HITBOX] player diameter {radius * 2}, marker/physics and four hazard boundaries passed.");
    }

    private async Task<bool> SettleEasyFrame(int settledFrames = 0, string? capturePrefix = null)
    {
        bool returning = _boss.BossFrameReturning;
        _pilot.Release();
        async Task Capture(string stage)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var shot = GetViewport().GetTexture().GetImage();
            EasyRequire(shot.SavePng($"{capturePrefix}_{stage}.png") == Error.Ok, "Save boss frame return screenshot");
        }
        for (int frame = 0; frame < 120 && _boss.BossFrameReturning; frame++)
        {
            _boss.TickBossFrame(1d / 60);
            Input.FlushBufferedEvents();
            Pad.ReleaseMouseFollow();
            _player._PhysicsProcess(1d / 60);
            await EasyPhysicsFrames(1);
            if (frame == 35 && capturePrefix != null) await Capture("return");
        }
        EasyRequire(!_boss.BossFrameReturning, "The frozen QA boss did not finish returning its frame.");
        await EasyPhysicsFrames(settledFrames);
        if (returning && capturePrefix != null) await Capture("restored");
        return returning;
    }

    private async Task<BossCharacterTechnique> RestartAfterDifferentTechnique(BossDanmaku danmaku, bool cycle, int targetSlot)
    {
        await SettleEasyFrame();
        for (int tick = 0; tick < 6 && danmaku.ActiveTechnique is not { Finished: false }; tick++)
        { _boss.TickBossFrame(1); danmaku.Tick(1); }
        var intervening = danmaku.ActiveTechnique;
        EasyRequire(intervening is { Finished: false } && (cycle ? danmaku.CycleSlot : danmaku.EasySlot) != targetSlot,
            "Cancellation did not start a different technique.");
        intervening!.Cancel();
        await SettleEasyFrame();
        danmaku.Tick(0);
        Write(danmaku, cycle ? "_cycleSlot" : "_easySlot", targetSlot);
        Write(danmaku, "_timer", 0d);
        danmaku.Tick(.01);
        var restarted = danmaku.ActiveTechnique;
        EasyRequire(restarted is { Finished: false }, "The selected completion fixture did not restart after another technique.");
        return restarted!;
    }

    private async Task CheckTechniqueInterruption(int phase, BossAttackCycle.Slot slot)
    {
        await SettleEasyFrame();
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        bool cycle = _boss.UsesTechniqueCycle;
        int Cursor() => cycle ? danmaku.CycleSlot : danmaku.EasySlot;
        int Rounds() => cycle ? danmaku.CycleRounds : danmaku.EasyRounds;
        danmaku.Tick(0);
        danmaku.ActiveTechnique?.Cancel();
        danmaku.Tick(0);
        var slots = cycle ? BossAttackCycle.Slots(_boss, BossAttackCycle.Segment(_boss)) : BossAttackCycle.EasySlots(_boss, phase);
        int cursor = Enumerable.Range(0, slots.Count).First(i => slots[i] == slot);
        Write(danmaku, cycle ? "_cycleSlot" : "_easySlot", cursor);
        _easyStationary = true;
        _running = true;
        BossCharacterTechnique? interrupted = null;
        for (int frame = 0; frame < 30 * 60; frame++)
        {
            danmaku.Tick(1d / 60);
            await EasyPhysicsFrames(1);
            if (danmaku.ActiveTechnique is { Finished: false } active && active.Elapsed >= 4.5
                && (active.IsStriking || active.OwnedShots.Any(b => !b.LeadIn)))
            { interrupted = active; break; }
        }
        EasyRequire(interrupted != null && interrupted.TechniqueKind == slot.Kind && interrupted.TechniqueVariant == slot.Variant,
            "The selected production technique did not reach its active phase.");
        int rounds = Rounds();
        double cancelledAt = interrupted!.Elapsed;
        interrupted.Cancel();
        EasyRequire(!interrupted.Completed && !interrupted.OwnedShots.Any() && !interrupted.IsStriking,
            "Cancellation left an active attack.");
        if (interrupted is KoharuBossTechnique pursuit)
        {
            EasyRequire(!pursuit.VisiblePursuitBarriers.Any(), "Cancellation left a visible pursuit barrier.");
            Vector2 crossing = new(Field.CenterX + 90, Field.CenterY);
            EasyRequire(pursuit.ResolvePlayerMotion(new Vector2(Field.CenterX - 90, Field.CenterY), crossing) == crossing,
                "Cancellation left blocked player movement.");
        }
        EasyRequire(Cursor() != cursor && Rounds() == rounds, "Cancellation must change selection without counting a completion.");
        BossCharacterTechnique? retry = null;
        for (int frame = 0; frame < 180; frame++)
        {
            danmaku.Tick(1d / 60);
            await EasyPhysicsFrames(1);
            if (danmaku.ActiveTechnique is { Finished: false } active && active != interrupted) { retry = active; break; }
        }
        EasyRequire(retry != null && new BossAttackCycle.Slot(retry.TechniqueKind, retry.TechniqueVariant) != slot && retry.Elapsed < .1,
            "The interrupted technique was repeated immediately.");
        bool naturalRetry = OS.GetCmdlineUserArgs().Contains("--body-attack-qa")
            || OS.GetCmdlineUserArgs().Contains("--natural-retry");
        if (naturalRetry)
        {
            retry = await RestartAfterDifferentTechnique(danmaku, cycle, cursor);
            bool completed = false;
            retry!.Ended += ended => {
                completed = ended.Completed && ended.NaturalEndLiveHazards == 0 && ended.NaturalEndRemainingShots == 0;
            };
            for (int frame = 0; frame < 120 * 60 && !completed; frame++)
            {
                danmaku.Tick(1d / 60);
                await EasyPhysicsFrames(1);
            }
            EasyRequire(completed && (Cursor() != cursor || Rounds() > rounds), "Natural production completion did not advance selection.");
        }
        else retry!.Cancel();
        _running = false;
        _easyCycles.Add(new { phase, difficulty = _game.Difficulty.ToString(), kind = slot.Kind.ToString(), variant = slot.Variant,
            cursor_fixture = true, production_selection = true, cancelled_at = cancelledAt, passed = true,
            no_remaining_attack = true, different_next_technique = true, natural_retry_after_another_technique = naturalRetry });
    }

    private async Task EasyBattleCycle(int phase)
    {
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        bool cycle = _boss.UsesTechniqueCycle;
        int Cursor() => cycle ? danmaku.CycleSlot : danmaku.EasySlot;
        int Rounds() => cycle ? danmaku.CycleRounds : danmaku.EasyRounds;
        var slots = cycle ? BossAttackCycle.Slots(_boss, BossAttackCycle.Segment(_boss)) : BossAttackCycle.EasySlots(_boss, phase);
        var started = new List<object>();
        var seen = new HashSet<BossCharacterTechnique>();
        var completedSlots = new HashSet<BossAttackCycle.Slot>();
        ulong? previousEndFrame = null;
        int returningFrames = 0;
        int completed = 0;
        _easyStationary = true;
        _easyStart = EasyStarts[0];
        _easyDisplacement = 0;
        _running = true;
        for (int frame = 0; frame < 1200 * 60 && completed < slots.Count; frame++)
        {
            if (previousEndFrame.HasValue && _boss.BossFrameReturning) returningFrames++;
            danmaku.Tick(1d / 60);
            if (danmaku.ActiveTechnique is { } attack && seen.Add(attack))
            {
                int cursor = Cursor();
                var expected = slots[cursor];
                EasyRequire(attack.TechniqueKind == expected.Kind && attack.TechniqueVariant == expected.Variant,
                    "Runtime selection differed from canonical slot.");
                ulong? idleFrames = previousEndFrame.HasValue ? Engine.GetPhysicsFrames() - previousEndFrame.Value : null;
                int returnFrames = returningFrames;
                previousEndFrame = null;
                returningFrames = 0;
                attack.Ended += ended =>
                {
                    EasyRequire(ended.Completed, "Runtime slot was interrupted.");
                    EasyRequire(completedSlots.Add(expected), "Runtime repeated a technique before the eligible roster completed.");
                    started.Add(new { slot = cursor, kind = ended.TechniqueKind.ToString(), variant = ended.TechniqueVariant,
                        mechanic = ended.MechanicKey, physics_frames_after_previous_end = idleFrames, boss_return_frames = returnFrames });
                    previousEndFrame = Engine.GetPhysicsFrames();
                    completed++;
                };
            }
            await EasyPhysicsFrames(1);
        }
        EasyRequire(completed == slots.Count && slots.All(completedSlots.Contains) && Rounds() == 1, "Round did not advance only after natural completion.");
        int retryCursor = Cursor();
        var first = slots[retryCursor];
        for (int frame = 0; frame < 120 && (danmaku.ActiveTechnique == null || danmaku.ActiveTechnique.Finished); frame++)
        { danmaku.Tick(1d / 60); await EasyPhysicsFrames(1); }
        var cancelled = danmaku.ActiveTechnique!;
        EasyRequire(cancelled != null && !cancelled.Finished, "Retry check did not start.");
        cancelled!.Cancel();
        EasyRequire(Cursor() != retryCursor && Rounds() == 1, "Cancellation must change the cursor without completing a round.");
        for (int frame = 0; frame < 120 && (danmaku.ActiveTechnique == null || danmaku.ActiveTechnique.Finished); frame++)
        { danmaku.Tick(1d / 60); await EasyPhysicsFrames(1); }
        var retry = danmaku.ActiveTechnique!;
        EasyRequire(retry != null && !retry.Finished && new BossAttackCycle.Slot(retry.TechniqueKind, retry.TechniqueVariant) != first,
            "Cancelled slot was repeated immediately.");
        retry!.Cancel();
        await EasyPhysicsFrames(2);
        _running = false;
        _easyCycles.Add(new { phase, difficulty = _game.Difficulty.ToString(), segment = BossAttackCycle.Segment(_boss), passed = true,
            natural_completions = completed, rounds = Rounds(), cursor = Cursor(), cancellation_advances_cursor = true, different_next_technique = true, started });
        if (_boss is BossMina phaseBoss)
            EasyRequire(Read<MinaPhaseAttacks>(phaseBoss, "_caster").OpenerCompleted, "Mina phase opener completion was not acknowledged.");
        if (phase == 1 && !cycle && _boss is BossKoharu or BossRei)
        {
            var caster = Read<AreaSpellCaster>(_boss, "_caster");
            caster.RequestFinale();
            bool began = false;
            if (_boss is BossKoharu) _running = true;
            for (int frame = 0; frame < 180 * 60 && !caster.FinaleCompleted; frame++)
            {
                caster._Process(1d / 60);
                var finale = GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>()
                    .FirstOrDefault(a => !a.Finished && a.TechniqueKind == BossTechniqueKind.Finale);
                began |= finale != null;
                await EasyPhysicsFrames(1);
            }
            _running = false;
            EasyRequire(began && caster.FinaleCompleted, "Native finale request failed to complete.");
            GD.Print($"[EASY] NATIVE_FINALE phase={phase} complete=true");
        }
        GD.Print($"[EASY] CYCLE phase={phase} PASS slots={completed} cancelled_advances=true");
    }

    private void TickEasyPilot(double delta)
    {
        if (!_running)
        {
            if (_rallyChargeDown)
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, Pressed = false });
                _rallyChargeDown = false;
            }
            Input.FlushBufferedEvents();
            return;
        }
        _boss.TickBossFrame(delta);
        if (_performanceVisual) Write(_player, "_fireCooldown", 99999f);
        if (Read<bool>(_boss, "_swapAnim"))
        {
            Call(_boss, "TickSwapAnim", delta);
            _boss.QueueRedraw();
        }
        Call(_boss, "TickAttackPose", delta);
        if ((_performanceVisual || _easyAttack is ReiBossTechnique { RallyTechnique: true })
            && _easyAttack is { Finished: false } && !Hud.BubblePaused)
            Call(_boss, "UpdateMovement", GameManager.EnemyDelta(delta));
        if (!Hud.BubblePaused && !_boss.GaugeVulnerable && !_boss.GaugeReforming)
            Call(_boss, "TickBodyMotion", GameManager.EnemyDelta(delta));
        if (_perimeterProbe && _easyAttack != null)
        {
            Vector2[] corners = { new(Field.Right - 12, Field.Top + 12), new(Field.Right - 12, Field.Bottom - 12),
                new(Field.Left + 12, Field.Bottom - 12), new(Field.Left + 12, Field.Top + 12) };
            Vector2 goal = corners[_perimeterCorner];
            if (_player.GlobalPosition.DistanceTo(goal) < 1) goal = corners[_perimeterCorner = (_perimeterCorner + 1) % 4];
            float step = _player.SlowestMoveSpeed * (float)delta;
            AutoplayInput.Move((goal - _player.GlobalPosition).LimitLength(step) / (_player.MoveSpeed * (float)delta));
        }
        else if (_pressureAudit && _pressureMotion != Vector2.Zero && _easyAttack != null
            && (_twoBeatSteps == null || _easyAttack.Elapsed >= _pressureMoveAt))
            AutoplayInput.Move(_easyAttack.Elapsed >= _pressureMoveAt && _easyAttack.Elapsed < _pressureMoveUntil ? _pressureMotion : Vector2.Zero);
        else if (_visualLoop && _easyAttack != null)
            AutoplayInput.Move((int)(_easyAttack.Elapsed / .9) % 2 == 0 ? Vector2.Left : Vector2.Right);
        else if (_easyStationary || _easyAttack != null && _easyAttack.Elapsed >= _settleAt) _pilot.Release();
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
        private long _liveShotSum;
        private int _peakLiveShots;
        private double _lastEmission = -1, _maximumEmissionGap, _routeElapsed;
        private bool _routeLost;
        private double? _routeLostAt;
        private readonly bool _bottomOnly = OS.GetCmdlineUserArgs().Contains("--bottom-probes");
        private readonly bool _preciseRoute = OS.GetCmdlineUserArgs().Contains("--precise-routes");
        private readonly bool _pressureOnly = OS.GetCmdlineUserArgs().Contains("--pressure-audit");
        private readonly bool _contactOnly = OS.GetCmdlineUserArgs().Contains("--contact-only");
        private readonly bool _wallRally;
        private float GridSpacing => _preciseRoute || _bottomOnly ? 2 : _pressureOnly ? 12 : 6;
        private int SampleFrames => _bottomOnly ? 1 : _preciseRoute ? 2 : 6;
        private double SampleSeconds => SampleFrames / 60d;
        private int? _expectedShots;
        private string? _failure;
        public int Contacts { get; private set; }
        public int BodyContacts { get; private set; }

        public EasyAttackAudit(BossCharacterTechnique attack, Player player, Enemy boss, Vector2 start)
        {
            _boss = boss;
            _player = player;
            _wallRally = attack is ReiBossTechnique { RallyTechnique: true };
            var points = new List<Vector2>();
            if (_bottomOnly)
                for (float x = Field.Left; x <= Field.Right; x += GridSpacing) points.Add(new(x, Field.Bottom));
            else
                for (float y = Field.Top + 6; y <= Field.Bottom - 6; y += GridSpacing)
                    for (float x = Field.Left + 6; x <= Field.Right - 6; x += GridSpacing) points.Add(new(x, y));
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
                var points = Read<Vector2[]>(path, "Points");
                if (check.Samples == 0 && key.StartsWith("rewrite:") && key.Contains(":revision:")
                    && (points.Length != 4 || (points[1] - points[0]).Dot(points[2] - points[1]) >= 0
                        || points[1].DistanceTo(points[2]) < 40))
                    _failure ??= "Rewritten quotation lost its forward/erase/release sequence: " + key;
                if (points.Length > 0)
                {
                    pathError = Enumerable.Range(1, points.Length - 1)
                        .Min(leg => at.DistanceTo(Geometry2D.GetClosestPointToSegment(at, points[leg - 1], points[leg])));
                    if (!Read<bool>(path, "Waiting"))
                    {
                        float remaining = (float)(attack.Elapsed - Read<double>(path, "FiresAt")) * speed;
                        for (int leg = 1; leg < points.Length; leg++)
                        {
                            float length = points[leg - 1].DistanceTo(points[leg]);
                            if (remaining > length && leg + 1 < points.Length) { remaining -= length; continue; }
                            Vector2 expected = points[leg - 1] + points[leg - 1].DirectionTo(points[leg]) * remaining;
                            pathError = Math.Max(pathError, at.DistanceTo(expected));
                            break;
                        }
                    }
                    if (check.Samples == 0 && Read<bool>(path, "RailReflection"))
                        for (int turn = 1; turn + 1 < points.Length; turn++)
                        {
                            Vector2 incoming = points[turn - 1].DirectionTo(points[turn]), outgoing = points[turn].DirectionTo(points[turn + 1]);
                            float railGap = Enumerable.Range(0, 4).Min(rail => Math.Abs(points[turn].X - (Field.Left + 6 + (Field.Width - 12) * rail / 3)));
                            if (Math.Abs(incoming.X + outgoing.X) > .001f || Math.Abs(incoming.Y - outgoing.Y) > .001f
                                || Math.Abs(railGap - 3 - halfWidth) > .01f || Math.Abs(points[turn].Y - from.Y) < 24)
                                _failure ??= "Reflection does not match a solid rail's inner surface: " + key;
                        }
                }
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
            if (attack.MechanicKey == "koharu.unbalanced-support")
            {
                _expectedShots = 2 * (2 + (int)GameManager.Instance!.Difficulty)
                    * (GameManager.Instance.Difficulty == GameManager.Diff.Hard && _boss is BossKoharu ? 2 : 1);
                foreach (var weight in Read<System.Collections.IEnumerable>(attack, "_supportLights").Cast<object>())
                {
                    double drop = Read<double>(weight, "DropAt");
                    if (drop < 0) continue;
                    var light = Read<object>(weight, "Light");
                    var shot = Read<Bullet>(light, "Bullet");
                    if (!shot.Active || shot.ActivationId != Read<ulong>(light, "Activation")) continue;
                    string key = $"support-drop:{Read<int>(light, "Index")}";
                    Vector2 origin = Read<Vector2>(weight, "DropOrigin");
                    Vector2 velocity = Read<Vector2>(weight, "DropVelocity");
                    float age = (float)Math.Max(0, attack.Elapsed - drop);
                    Vector2 expected = origin + velocity * age + Vector2.Down * (18 * age * age);
                    float pathError = shot.GlobalPosition.DistanceTo(expected);
                    float scale = Math.Max(shot.GlobalTransform.X.Length(), shot.GlobalTransform.Y.Length());
                    float radius = Read<float>(weight, "DropRadius");
                    float widthError = Math.Max(Math.Abs(shot.IllustrationRadius * scale - radius),
                        Math.Max(0, shot.CollisionRadius * scale - radius));
                    var check = _pathChecks.GetValueOrDefault(key);
                    float originError = check.Samples == 0 ? shot.GlobalPosition.DistanceTo(origin) : check.OriginError;
                    _pathChecks[key] = (check.Samples + 1, originError, Math.Max(check.PathError, pathError), Math.Max(check.WidthError, widthError));
                    if (originError > .2f || pathError > 2 || widthError > .15f)
                        _failure ??= $"Support drop differs from its fixed warning: {key} origin={originError} path={pathError} width={widthError}";
                    if (shot.EnemyIllustration?.Texture.ResourcePath != "res://assets/fx/koharu_prayer_tag.svg")
                        _failure ??= "Support drop retained the rolling badge illustration: " + key;
                }
                return;
            }
            if (Read<int>(attack, "_easyMode") == 0) return;
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
                float scale = Math.Max(shot.GlobalTransform.X.Length(), shot.GlobalTransform.Y.Length());
                float forecastRadius = Read<float>(path, "HalfWidth");
                // Forecasts enclose every frame of rotating projectile models.
                float widthError = Math.Max(Math.Abs(shot.IllustrationRadius * scale - forecastRadius),
                    Math.Max(0, shot.CollisionRadius * scale - forecastRadius));
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
            if (!_pressureOnly)
            {
                ObserveEditorialPaths(attack);
                ObserveKoharuPaths(attack);
                ObserveKoharuSweeps(attack);
                ObserveMinaGeometry(attack);
            }
            _bodyShapes.Clear();
            if (_boss.BodyContactDamaging && _boss.CollisionLayer != 0 && _boss.Monitorable)
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
            foreach (var sample in _pressureOnly ? Array.Empty<BossEasyCue>() : attack.EasyCues)
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
                    double requiredHold = GameManager.Instance!.Difficulty == GameManager.Diff.Easy ? 2 : .75;
                    if (cue.Full == null || attack.Elapsed - cue.Full.Value + 1d / 60 + .00001 < requiredHold)
                        _failure ??= $"Complete warning shorter than required hold ({requiredHold}): {sample.Key} full={cue.Full} live={attack.Elapsed}";
                }
            }
            var live = attack.OwnedShots.Where(b => !b.LeadIn && b.CollisionLayer != 0).Select(SampleShot).ToArray();
            _liveShotSum += live.Length;
            _peakLiveShots = Math.Max(_peakLiveShots, live.Length);
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
            if (_contactOnly) return;
            if (_frames % SampleFrames != 0) return;
            _samples++;
            var blocked = new bool[_points.Length];
            int dangerous = 0, safe = 0;
            bool striking = attack.IsStriking;
            const float bucketSize = 24;
            var buckets = new Dictionary<(int X, int Y), List<LiveShot>>();
            foreach (var shot in live)
            {
                float reach = shot.Radius + (_preciseRoute ? 0 : 4.25f);
                for (int y = Mathf.FloorToInt((shot.Position.Y - reach) / bucketSize); y <= Mathf.FloorToInt((shot.Position.Y + reach) / bucketSize); y++)
                for (int x = Mathf.FloorToInt((shot.Position.X - reach) / bucketSize); x <= Mathf.FloorToInt((shot.Position.X + reach) / bucketSize); x++)
                {
                    if (!buckets.TryGetValue((x, y), out var bucket)) buckets[(x, y)] = bucket = new();
                    bucket.Add(shot);
                }
            }
            for (int i = 0; i < _points.Length; i++)
            {
                var point = _points[i];
                buckets.TryGetValue((Mathf.FloorToInt(point.X / bucketSize), Mathf.FloorToInt(point.Y / bucketSize)), out var candidates);
                bool shotHit = candidates?.Any(b => BulletHit(b, point)) == true;
                bool shotBlocked = candidates?.Any(b => BulletHit(b, point, !_preciseRoute)) == true;
                if (_frames % 120 == 0 && i % 13 == 0)
                    EasyRequire(shotHit == live.Any(b => BulletHit(b, point))
                        && shotBlocked == live.Any(b => BulletHit(b, point, !_preciseRoute)), "Spatial audit omitted a projectile.");
                bool attackHit = striking && attack.CoversPoint(point) || shotHit;
                bool bodyHit = BodyHit(point);
                blocked[i] = striking && attack.DangerDistance(point, false) <= (_preciseRoute ? .25f : 4.25f)
                    || shotBlocked || BodyHit(point, !_preciseRoute);
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
            if (_bottomOnly || _pressureOnly) return;
            _routeElapsed += SampleSeconds;
            bool moved = false;
            while (_routeElapsed * _player.SlowestMoveSpeed >= GridSpacing)
            {
                moved = true;
                var next = new bool[_points.Length];
                for (int i = 0; i < _points.Length; i++)
                {
                    if (blocked[i]) continue;
                    if (_reachable[i]) { next[i] = true; continue; }
                    foreach (int previous in new[] { i - 1, i + 1, i - _columns, i + _columns })
                    {
                        if (previous < 0 || previous >= _points.Length || !_reachable[previous] || _previousBlocked[previous] || _previousBlocked[i]) continue;
                        if (_points[previous].DistanceSquaredTo(_points[i]) <= GridSpacing * GridSpacing + .001) { next[i] = true; break; }
                    }
                }
                _reachable = next;
                _routeElapsed -= GridSpacing / _player.SlowestMoveSpeed;
                if (!next.Any(v => v)) { _routeLost = true; _routeLostAt ??= attack.Elapsed; }
            }
            if (!moved)
            {
                for (int i = 0; i < _reachable.Length; i++) _reachable[i] &= !blocked[i];
                if (!_reachable.Any(v => v)) { _routeLost = true; _routeLostAt ??= attack.Elapsed; }
            }
            _previousBlocked = blocked;
        }

        public void End(BossCharacterTechnique attack)
        {
            if (!_pressureOnly)
            {
                EndKoharuSweeps(attack);
                EndMinaGeometry(attack);
            }
            _remainingAtEnd = attack.NaturalEndRemainingShots;
            _liveHazardsAtEnd = attack.NaturalEndLiveHazards;
        }

        public void Validate()
        {
            EasyRequire(_remainingAtEnd == 0 && _liveHazardsAtEnd == 0, "Natural completion removed active bullets or hazards.");
            if (_pressureOnly) return;
            EasyRequire(_failure == null, _failure ?? "");
            EasyRequire(_wallRally || _bottomOnly && _boss is BossAkari || _cues.Count > 0 && _cues.Values.All(c => c.Cancelled || c.Full != null && c.Actual != null), "Missing complete warning or actual attack event.");
            EasyRequire(_bottomOnly || _noSafe == 0, "Attack filled the entire reachable field.");
            EasyRequire(!_preciseRoute || !_routeLost, "Precise normal-speed escape route was lost.");
            EasyRequire(_expectedShots == null || _expectedShots == _created && _pathChecks.Count == _expectedShots,
                "Planned projectiles, physical spawns or all burst trajectories are missing.");
        }

        public object Result() => new {
            pressure_audit = _pressureOnly, contact_only = _contactOnly, cue_geometry_checked = !_pressureOnly, path_warning_required = !_wallRally,
            sample_domain = _contactOnly ? "actual player contacts only; density and reachable-grid metrics were not evaluated"
                : _bottomOnly ? "exact playable bottom boundary, not a whole-field escape audit" : "interior playfield",
            frames = _frames, samples = _samples, spacing = GridSpacing, regions_reached = _regions.Count(v => v), regions = _regions,
            minimum_safe_points = _safeMinimum, samples_without_safe_region = _noSafe,
            mean_frame_density = _samples == 0 ? 0 : _densitySum / _samples, peak_frame_density = _densityMaximum,
            mean_live_projectiles = _frames == 0 ? 0 : _liveShotSum / (double)_frames, peak_live_projectiles = _peakLiveShots,
            area_time_seconds = _densitySum * SampleSeconds, danger_seconds = _dangerSamples * SampleSeconds,
            first_danger_at = _firstDanger, maximum_internal_empty_seconds = _maximumInternalEmpty * SampleSeconds,
            maximum_emission_gap_seconds = _maximumEmissionGap, emissions = _emissions,
            untouched_points = _dangerFrames.Count(c => c == 0), points = _points.Length,
            boundary_samples = _bottomOnly ? _points.Select((point, index) => new { x = point.X, y = point.Y, danger_frames = _dangerFrames[index] }).ToArray() : null,
            maximum_point_danger_seconds = _dangerFrames.Max() * SampleSeconds,
            attack_contact_frames = Contacts, body_contact_frames = BodyContacts,
            active_owned_bullets_before_natural_cleanup = _remainingAtEnd,
            live_hazards_before_natural_cleanup = _liveHazardsAtEnd,
            expected_projectiles = _expectedShots, observed_created_projectiles = _created,
            mina_geometry = MinaGeometryResult(),
            koharu_sweeps = KoharuSweepResult(),
            projectile_geometry = _pathChecks.Select(pair => new { key = pair.Key, samples = pair.Value.Samples,
                first_observed_origin_error = pair.Value.OriginError, maximum_path_error = pair.Value.PathError,
                maximum_width_error = pair.Value.WidthError }),
            route = new { @checked = !_contactOnly && !_bottomOnly && !_pressureOnly, spacing = GridSpacing, sampled_seconds = SampleSeconds, speed = _player.SlowestMoveSpeed, lost = _routeLost, lost_at = _routeLostAt,
                input_dependent_counterfactual_verified = false, diagnostic_grid_only = true,
                hazard_margin_beyond_player_radius = _preciseRoute ? .25 : 4.25,
                projectile_and_body_probe_radius = _preciseRoute ? Player.HitRadius : Player.HitRadius + 4.25,
                rejects_current_and_previous_blocked_cells = true, fractional_movement_budget_retained = true },
            cue_events = _cues.Select(pair => new { key = pair.Key, starts_at = pair.Value.Start, fires_at = pair.Value.Fire,
                full_at = pair.Value.Full, actual_at = pair.Value.Actual,
                cancelled_before_attack = pair.Value.Cancelled,
                complete_hold_seconds = pair.Value.Actual - pair.Value.Full,
                point_samples = pair.Value.Point, growing_samples = pair.Value.Growing, complete_samples = pair.Value.Complete }),
        };
    }
}


internal sealed class HardPassageTrace : System.IDisposable
{
    private readonly System.IO.StreamWriter _writer;
    private readonly Dictionary<BulletFootprint, int> _footprints = new();
    private HardPassageTrace(string file) => _writer = new System.IO.StreamWriter(file);
    internal static HardPassageTrace? Open(string file) => OS.GetCmdlineUserArgs().Contains("--trace-passages")
        ? new HardPassageTrace(file) : null;

    private int Shape(Bullet bullet)
    {
        if (bullet.Footprint is not { } shape) return -1;
        if (_footprints.TryGetValue(shape, out int id)) return id;
        _footprints.Add(shape, id = _footprints.Count);
        var polygons = shape.Shapes.OfType<ConvexPolygonShape2D>()
            .Select(poly => poly.Points.Select(p => new[] { p.X, p.Y }));
        _writer.WriteLine(JsonSerializer.Serialize(new { id, polygons, radius = shape.Radius }));
        return id;
    }

    internal void Frame(BossCharacterTechnique attack, Vector2 player)
    {
        var shots = attack.OwnedShots.Where(b => !b.LeadIn && b.CollisionLayer != 0).Select(b => {
            var t = b.GlobalTransform;
            return new[] { Shape(b), t.Origin.X, t.Origin.Y, t.X.X, t.X.Y, t.Y.X, t.Y.Y, b.CollisionRadius,
                b.Velocity.X, b.Velocity.Y };
        }).ToArray();
        var hazards = attack.PracticeHazards.Where(h => h.Live)
            .Select(h => new[] { h.Shape, h.A.X, h.A.Y, h.B.X, h.B.Y, h.Radius }).ToArray();
        var barriers = attack is KoharuBossTechnique { MechanicKey: "koharu.pursuit-train" } chase
            ? chase.VisiblePursuitBarriers.Where(b => b.Solid).Select(b => new[] {
                b.Bounds.Position.X, b.Bounds.Position.Y, b.Bounds.End.X, b.Bounds.End.Y }).ToArray()
            : Array.Empty<float[]>();
        if (attack is KoharuBossTechnique { MechanicKey: "koharu.pursuit-train" } pursuit)
        {
            _writer.WriteLine(JsonSerializer.Serialize(new { t = attack.Elapsed, x = player.X, y = player.Y,
                right = Field.Right, shots, hazards, barriers, envelope = Array.Empty<float[]>(), original = hazards.Length,
                renderFrame = Engine.GetProcessFrames(), pursuitActive = pursuit.PursuitHunting,
                headX = pursuit.BossCenter.X, headY = pursuit.BossCenter.Y }));
            return;
        }
        _writer.WriteLine(JsonSerializer.Serialize(new { t = attack.Elapsed, x = player.X, y = player.Y,
            right = Field.Right, shots, hazards, barriers, envelope = Array.Empty<float[]>(), original = hazards.Length }));
    }
    public void Dispose() => _writer.Dispose();
}
