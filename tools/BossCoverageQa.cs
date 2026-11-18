using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossCoverageQa : Node
{
    private sealed class Attempt
    {
        internal BossCharacterTechnique Attack = null!;
        internal string Identity = "", Mechanic = "", Name = "";
        internal double Started;
        internal int Hp, Phase;
        internal bool Form, Active, Ended;
    }
    private sealed class Count
    {
        public int Started { get; set; }
        public int Activated { get; set; }
        public int Completed { get; set; }
        public int CompletedWithoutActivation { get; set; }
        public int Cancelled { get; set; }
    }

    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Dictionary<ulong, Attempt> _attempts = new();
    private readonly Dictionary<string, Count> _counts = new();
    private readonly HashSet<(ulong Id, ulong Activation)> _playerShots = new();
    private readonly Queue<(double Time, int Damage)> _damage = new();
    private List<string> _initialUpgrades = new();
    private GameManager _game = null!;
    private Player _player = null!;
    private Hud _hud = null!;
    private Node? _stage;
    private Enemy? _boss;
    private string _character = "", _profile = "", _fire = "", _output = "";
    private bool _observeRoster;
    private bool _cameo, _running, _done, _shift, _charge, _bomb, _defeated, _bombedShield, _wasTalking;
    private bool _postClearCompleted;
    private bool _mixedReleasePending;
    private int _mixedFirstReleases, _mixedSecondReleases, _mixedInterruptedReleases;
    private double _mixedRapidSeconds, _mixedChargeSeconds;
    private string _postClearReason = "";
    private double _time, _limit, _bossFoundAt, _lastStateAt, _lastStatus, _defeatedAt;
    private int _lastHp, _initialHp, _lastPhase, _frame, _breaks, _damageInWindow, _maxWindowDamage;
    private int _maximumExposedLedgerDamage;
    private int _initialLives, _initialLinePower, _initialSpeedPower, _lastLives, _lastLinePower, _lastSpeedPower;
    private Job _initialJob;
    private bool _lastForm;
    private string _lastState = "";
    private double _minimumShieldTime = double.PositiveInfinity, _maximumDps;

    private static string? Option(string name) => OS.GetCmdlineUserArgs()
        .FirstOrDefault(a => a.StartsWith(name + "="))?.Split('=', 2)[1];
    private static FieldInfo FieldOf(object value, string name)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Fields | BindingFlags.DeclaredOnly) is { } field) return field;
        throw new MissingFieldException(name);
    }
    private static T Read<T>(object value, string name) => (T)FieldOf(value, name).GetValue(value)!;
    private static void Write(object value, string name, object data) => FieldOf(value, name).SetValue(value, data);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Log(string kind, object data) => GD.Print("[BossCoverageQA] " + kind + " " + JsonSerializer.Serialize(data));

    public override async void _Ready()
    {
        try
        {
            Require(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data required");
            _character = Option("--character") ?? "Akari";
            _profile = Option("--profile") ?? "max";
            _output = Option("--output") ?? "";
            _cameo = OS.GetCmdlineUserArgs().Contains("--cameo");
            _observeRoster = OS.GetCmdlineUserArgs().Contains("--observe-roster");
            Require(!_observeRoster || _cameo, "Roster observation without shooting is for the single-form cameo encounter.");
            Require(new[] { "Akari", "Koharu", "Rei", "Mina" }.Contains(_character), "unknown character");
            Require(new[] { "max", "heal-bomb", "base" }.Contains(_profile), "unknown profile");
            Require(!_cameo || _character != "Mina", "Mina has no cameo encounter");
            Require(Enum.TryParse<GameManager.Diff>(Option("--difficulty") ?? "Normal", true, out var difficulty), "unknown difficulty");
            _fire = Option("--fire") ?? (_profile == "base" ? "charge" : "rapid");
            Require(_fire is "rapid" or "charge" or "mixed", "unknown firing policy");
            _limit = double.Parse(Option("--seconds") ?? "3600", System.Globalization.CultureInfo.InvariantCulture);
            Require(_limit > 0, "positive time limit required");
            GD.Seed(2070);
            Input.UseAccumulatedInput = false;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = difficulty;
            _game.SelectedJob = _profile == "max" ? Job.Melee : _profile == "heal-bomb" ? Job.Heal : Job.Tank;
            if (_profile != "base")
            {
                _game.TrainingSetAllUpgrades(true);
                _game.AddFollowers(2000);
            }
            _game.MarkIdleDialogSeen("once_midboss_shield");
            _game.SelectedEntry = _cameo ? GameManager.StageEntry.MidBoss : GameManager.StageEntry.Boss;
            ProcessMode = ProcessModeEnum.Always;
            ProcessPhysicsPriority = -1000;
            foreach (string identity in Expected()) _counts.Add(identity, new Count());
            GetTree().NodeAdded += OnNodeAdded;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var stage = GD.Load<PackedScene>($"res://{(_character == "Mina" ? "MinaBattle" : _character)}.tscn").Instantiate<Node2D>();
            _stage = stage;
            GetTree().Root.AddChild(stage);
            GetTree().CurrentScene = stage;
            _player = stage.GetNode<Player>("World/Player");
            _hud = stage.GetNode<Hud>("Hud");
            Write(_player, "_invincible", true);
            Write(_player, "_invincibleTimer", (float)_limit + 120);
            if (_profile != "base")
                for (int i = 0; i < Player.PowerLevelCap; i++)
                {
                    _player.ApplyPowerup(PowerKind.Line);
                    _player.ApplyPowerup(PowerKind.Speed);
                }
            _initialLives = _lastLives = _player.Lives;
            _initialLinePower = _lastLinePower = _player.LinePower;
            _initialSpeedPower = _lastSpeedPower = _player.SpeedPower;
            _initialJob = _game.SelectedJob;
            _initialUpgrades = _game.ColumnOwnedIds();
            _running = true;
            Log("START", new
            {
                character = _character, difficulty = difficulty.ToString(), profile = _profile, fire = _fire,
                cameo = _cameo, expected = _counts.Keys, timeLimit = _limit,
                job = _game.SelectedJob.ToString(), lives = _player.Lives, bombs = _game.Bombs,
                followers = _game.Followers, linePower = _player.LinePower,
                requestedJob = (_profile == "max" ? Job.Melee : _profile == "heal-bomb" ? Job.Heal : Job.Tank).ToString(),
                survivalAssist = "player invulnerability only; enemy projectiles and real collisions remain active",
            });
        }
        catch (Exception exception) { End(exception.ToString()); }
    }

    private IEnumerable<string> Expected()
    {
        if (_cameo)
        {
            return new[] { "Cameo/0", "Cameo/1" };
        }
        Enemy catalog = _character switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(),
            "Rei" => new BossRei(), _ => new BossMina() };
        int segments = _character == "Mina" ? 5 : _character == "Akari" ? 2 : 3;
        var identities = Enumerable.Range(0, segments).SelectMany(segment => BossAttackCycle.Slots(catalog, segment)
            .Select(slot => $"{slot.Kind}/{slot.Variant}" + (slot.Kind == BossTechniqueKind.Extra
                ? (segment >= (_character == "Mina" ? BossMina.DragonPhase : 1) ? "/after" : "/before") : "")))
            .Distinct().ToArray();
        catalog.Free();
        return identities;
    }

    private bool Matches(Enemy boss) => _cameo ? boss is CameoBoss : _character switch
    {
        "Akari" => boss is BossAkari, "Koharu" => boss is BossKoharu,
        "Rei" => boss is BossRei, _ => boss is BossMina,
    };
    private static bool Form(Enemy boss) => boss is BossMina mina ? mina.IsDragonForm : boss.HasEvolved;
    private static int Phase(Enemy boss) => boss is BossMina mina ? mina.EncounterPhase : Form(boss) ? 1 : 0;
    private static string State(Enemy boss) => Read<object>(boss, "_phase").ToString()!;

    private void OnNodeAdded(Node node)
    {
        if (_done || node is not BossCharacterTechnique attack) return;
        attack.Ready += () =>
        {
            try
            {
                if (!_done && attack.GetParent() is Enemy owner && Matches(owner)) TrackAttack(attack, owner);
            }
            catch (Exception exception) { End(exception.ToString()); }
        };
    }

    private Attempt TrackAttack(BossCharacterTechnique attack, Enemy owner)
    {
        if (_attempts.TryGetValue(attack.GetInstanceId(), out var found)) return found;
        bool form = Read<bool>(attack, "Evolved");
        string identity = $"{attack.TechniqueKind}/{(attack.TechniqueKind == BossTechniqueKind.Edge ? 0 : attack.TechniqueVariant)}"
            + (attack.TechniqueKind == BossTechniqueKind.Extra ? form ? "/after" : "/before" : "");
        Require(_counts.ContainsKey(identity), "unexpected technique in the live encounter: " + identity);
        var attempt = new Attempt
        {
            Attack = attack, Identity = identity, Mechanic = attack.MechanicKey, Name = attack.SpellName,
            Started = _time - attack.Elapsed, Hp = Read<int>(owner, "_hp"), Phase = Phase(owner), Form = form,
        };
        _attempts.Add(attack.GetInstanceId(), attempt);
        _counts[identity].Started++;
        attack.Ended += OnEnded;
        Log("BEGIN", new { time = attempt.Started, identity, mechanic = attempt.Mechanic, name = attempt.Name, hp = attempt.Hp, phase = attempt.Phase, form });
        if (attack.Finished) OnEnded(attack);
        return attempt;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_running || _done) return;
        try
        {
            if (_defeated && TryEndPostClear()) return;
            Require(IsInstanceValid(_player), "player was removed before the encounter verification completed");
            int previousLives = _lastLives, previousLinePower = _lastLinePower, previousSpeedPower = _lastSpeedPower;
            _lastLives = _player.Lives;
            _lastLinePower = _player.LinePower;
            _lastSpeedPower = _player.SpeedPower;
            Require(_game.RunHitCount == 0 && _lastLives >= previousLives && _lastLinePower >= previousLinePower && _lastSpeedPower >= previousSpeedPower,
                $"survival assist failed: hits={_game.RunHitCount}, lives {previousLives}->{_lastLives}, line {previousLinePower}->{_lastLinePower}, speed {previousSpeedPower}->{_lastSpeedPower}");
            if (_lastLives > previousLives || _lastLinePower > previousLinePower || _lastSpeedPower > previousSpeedPower)
                Log("PLAYER_GAIN", new { time = _time, previousLives, lives = _lastLives,
                    previousLinePower, linePower = _lastLinePower, previousSpeedPower, speedPower = _lastSpeedPower,
                    source = "game reward or collected pickup; QA grants no rewards after setup" });
            Require(_game.SelectedJob == _initialJob && _game.OwnedUpgradeCount == _initialUpgrades.Count
                && _initialUpgrades.All(_game.Has),
                "job or permanent upgrades changed during the encounter");
            Write(_player, "_invincible", true);
            Write(_player, "_invincibleTimer", _defeated ? 300f : (float)(_limit - _time + 120));
            if (_observeRoster && _counts.Values.Any(count => count.Completed == 0)) Write(_player, "_fireCooldown", 1f);
            _time += delta;
            _frame++;
            if (!_defeated && _time >= _limit) { End("time limit reached before a verified clear"); return; }
            if (_boss == null)
            {
                _boss = GetTree().GetNodesInGroup("enemies").OfType<Enemy>().FirstOrDefault(Matches);
                if (_boss != null)
                {
                    _bossFoundAt = _lastStateAt = _time;
                    _initialHp = _lastHp = Read<int>(_boss, "_hp");
                    if (!_cameo) Require(_game.StageProgress == .90f && !_game.StageCleared, "boss entry must show 90%");
                    _lastState = State(_boss);
                    _lastPhase = Phase(_boss);
                    _lastForm = Form(_boss);
                    Log("BOSS", new { time = _time, hp = _initialHp, maxHp = Read<int>(_boss, "_maxHp"), phase = _lastPhase, form = _lastForm });
                }
            }
            if (_boss != null && IsInstanceValid(_boss)) ObserveBoss();
            foreach (var lost in _attempts.Values.Where(a => !a.Ended && !IsInstanceValid(a.Attack)))
            {
                lost.Ended = true;
                _counts[lost.Identity].Cancelled++;
                Log("CANCEL", new { time = _time, identity = lost.Identity, reason = "removed without a completion event" });
            }
            foreach (var shot in GetTree().GetNodesInGroup("player_bullets").OfType<Bullet>().Where(b => b.Active))
                _playerShots.Add((shot.GetInstanceId(), shot.ActivationId));
            if (_defeated)
            {
                if (TryEndPostClear()) return;
                if (_time - _defeatedAt >= 180) { End("post-clear conversation did not complete within 180 seconds"); return; }
                DriveInput(delta);
                return;
            }
            DriveInput(delta);
            if (_time - _lastStatus >= 10)
            {
                _lastStatus = _time;
                Log("STATUS", new
                {
                    time = Math.Round(_time, 2), hp = _lastHp, phase = _lastPhase, form = _lastForm, state = _lastState,
                    completed = _counts.Values.Sum(c => c.Completed), started = _counts.Values.Sum(c => c.Started),
                    unseen = _counts.Count(p => p.Value.Completed == 0), shots = _playerShots.Count,
                    position = new { x = _player.Position.X, y = _player.Position.Y }, talking = Hud.BubblePaused,
                });
            }
        }
        catch (Exception exception) { End(exception.ToString()); }
    }

    private bool TryEndPostClear()
    {
        bool removed = !IsInstanceValid(_boss) || !_boss!.IsInsideTree();
        bool finished = !removed && (_boss switch
        {
            BossAkari akari => akari.Finished,
            BossKoharu koharu => koharu.Finished,
            BossRei rei => rei.Finished,
            BossMina mina => mina.Finished,
            CameoBoss cameo => cameo.Finished,
            _ => false,
        });
        if (!removed && !finished) return false;
        _postClearCompleted = true;
        _postClearReason = removed ? "defeated boss removed" : "boss Finished";
        Log("POST_CLEAR", new { time = _time, seconds = _time - _defeatedAt, reason = _postClearReason });
        End(null);
        return true;
    }

    private void ObserveBoss()
    {
        var boss = _boss!;
        foreach (var attack in boss.GetChildren().OfType<BossCharacterTechnique>())
            ObserveActivation(TrackAttack(attack, boss));
        int hp = Read<int>(boss, "_hp");
        if (!_cameo) Require(boss.IsPurified ? _game.StageProgress == 1 && _game.StageCleared
            : _game.StageProgress == .90f && !_game.StageCleared, "final 10% belongs to the defeated story boss");
        int exposedLedgerDamage = Read<int>(boss, "_windowDamage");
        _maximumExposedLedgerDamage = Math.Max(_maximumExposedLedgerDamage, exposedLedgerDamage);
        string state = State(boss);
        int phase = Phase(boss);
        bool formNow = Form(boss);
        if (hp < _lastHp)
        {
            int dealt = _lastHp - hp;
            _damageInWindow += dealt;
            if (!boss.IsPurified) _damage.Enqueue((_time, dealt));
        }
        while (_damage.Count > 0 && _damage.Peek().Time < _time - 1) _damage.Dequeue();
        _maximumDps = Math.Max(_maximumDps, _damage.Sum(sample => sample.Damage));
        if (state != _lastState)
        {
            if (state == "Shielded") _bombedShield = false;
            double elapsed = _time - _lastStateAt;
            if (state == "Break")
            {
                _breaks++;
            }
            if (_lastState == "Shielded" && (state is "Break" or "Resolving"))
                _minimumShieldTime = Math.Min(_minimumShieldTime, elapsed);
            if (_lastState == "Exposed")
            {
                _maxWindowDamage = Math.Max(_maxWindowDamage, _damageInWindow);
                _damageInWindow = 0;
            }
            Log("STATE", new { time = _time, from = _lastState, to = state, elapsed, hp, phase, form = formNow, bombs = _game.Bombs,
                unfinished = _attempts.Values.Where(a => !a.Ended).Select(a => a.Identity).ToArray() });
            _lastState = state;
            _lastStateAt = _time;
        }
        if (phase != _lastPhase || formNow != _lastForm)
        {
            Log("FORM", new { time = _time, fromPhase = _lastPhase, phase, form = formNow, hp,
                coverage = _counts.ToDictionary(pair => pair.Key, pair => pair.Value.Completed) });
            _lastPhase = phase;
            _lastForm = formNow;
        }
        _lastHp = hp;
        if (boss.IsPurified && !_defeated)
        {
            _defeated = true;
            _defeatedAt = _time;
            Log("DEFEATED", new { time = _time, battleSeconds = _time - _bossFoundAt, hp, phase, form = formNow, stageProgress = _game.StageProgress });
        }
    }

    private void ObserveActivation(Attempt attempt)
    {
        if (attempt.Active || !IsInstanceValid(attempt.Attack)) return;
        if (attempt.Attack.HazardFrames == 0 && !attempt.Attack.OwnedShots.Any(b => !b.LeadIn && b.CollisionLayer != 0)) return;
        attempt.Active = true;
        _counts[attempt.Identity].Activated++;
        Log("ACTIVE", new { time = _time, identity = attempt.Identity, elapsed = attempt.Attack.Elapsed,
            hp = _boss != null && IsInstanceValid(_boss) ? Read<int>(_boss, "_hp") : -1 });
    }

    private void OnEnded(BossCharacterTechnique attack)
    {
        if (!_attempts.TryGetValue(attack.GetInstanceId(), out var attempt) || attempt.Ended) return;
        ObserveActivation(attempt);
        attempt.Ended = true;
        var count = _counts[attempt.Identity];
        if (attack.Completed)
        {
            count.Completed++;
            if (!attempt.Active) count.CompletedWithoutActivation++;
        }
        else count.Cancelled++;
        Log(attack.Completed ? "COMPLETE" : "CANCEL", new
        {
            time = _time, identity = attempt.Identity, mechanic = attempt.Mechanic, elapsed = attack.Elapsed,
            activated = attempt.Active, shots = attack.ShotsCreated, hazardFrames = attack.HazardFrames,
            hp = _boss != null && IsInstanceValid(_boss) ? Read<int>(_boss, "_hp") : -1,
            phase = _boss != null && IsInstanceValid(_boss) ? Phase(_boss) : -1,
        });
    }

    private void DriveInput(double delta)
    {
        SetKey(Key.X, false, ref _bomb);
        bool talking = Hud.BubblePaused || _hud.CinematicMode;
        if (talking)
        {
            _wasTalking = true;
            AutoplayInput.Move(Vector2.Zero);
            SetKey(Key.Shift, false, ref _shift);
            SetKey(Key.Z, _frame % 8 < 4, ref _charge);
            Input.FlushBufferedEvents();
            return;
        }
        if (_wasTalking)
        {
            _wasTalking = false;
            Release();
            return;
        }
        Pad.ReleaseMouseFollow();
        SetKey(Key.Shift, true, ref _shift);
        if (_boss != null && IsInstanceValid(_boss) && !_boss.IsPurified && !_player.ReturningToStart)
        {
            Vector2 goal = (_boss.GlobalPosition + Vector2.Left * (_profile == "max" ? 42 : 78))
                .Clamp(new Vector2(global::Field.Left + 8, 8), new Vector2(global::Field.Right - 8, global::Field.Bottom - 8));
            AutoplayInput.Move((goal - _player.Position).LimitLength(_player.MoveSpeed * (float)delta) / (_player.MoveSpeed * (float)delta));
            if (_profile == "heal-bomb" && !_bombedShield && _boss.UnfoldersActive
                && _boss.GetChildren().OfType<Panel>().Any(p => p.CanLock) && _game.Bombs > 0)
            {
                SetKey(Key.X, true, ref _bomb);
                _bombedShield = true;
                Log("BOMB_INPUT", new { time = _time, hp = _lastHp, state = State(_boss), bombsBefore = _game.Bombs });
            }
        }
        else AutoplayInput.Move(Vector2.Zero);
        if (_fire == "mixed")
        {
            DriveMixedFire(delta);
            Input.FlushBufferedEvents();
            return;
        }
        bool ready = _game.HasChargeTier2 ? _player.ChargeFull2 : _player.ChargeFull;
        SetKey(Key.Z, _fire == "charge" && _player.LockedOn && !ready, ref _charge);
        Input.FlushBufferedEvents();
    }

    private void DriveMixedFire(double delta)
    {
        if (_mixedReleasePending)
        {
            SetKey(Key.Z, false, ref _charge);
            if (_player.ChargeRatio == 0 && !Read<bool>(_player, "_chargeHeld")) _mixedReleasePending = false;
            _mixedRapidSeconds += delta;
            return;
        }
        bool exposed = _boss != null && IsInstanceValid(_boss) && _boss.GaugeBodyExposed;
        if (!exposed)
        {
            if (_charge && _boss is { GaugeBreaking: true })
            { _mixedChargeSeconds += delta; return; }
            if (_charge) ReleaseMixedCharge(false);
            else SetKey(Key.Z, false, ref _charge);
            _mixedRapidSeconds += delta;
            return;
        }
        double remaining = _boss!.GaugeAttackResolving ? double.PositiveInfinity : _boss.GaugeWindowLeft * 8;
        float charged = _player.ChargeRatio * _game.ChargeNeedSec
            + _player.ChargeRatio2 * (_game.ChargeTier2NeedSec - _game.ChargeNeedSec);
        bool release = _player.ChargeFull2 || _player.ChargeFull
            && (!_game.HasChargeTier2 || remaining <= _game.ChargeTier2NeedSec - charged + .25);
        if (release) ReleaseMixedCharge(true);
        else if (_charge || _player.LockedOn && remaining >= _game.ChargeNeedSec + .25)
        { SetKey(Key.Z, true, ref _charge); _mixedChargeSeconds += delta; }
        else
        { SetKey(Key.Z, false, ref _charge); _mixedRapidSeconds += delta; }
    }

    private void ReleaseMixedCharge(bool exposed)
    {
        int stage = _player.ChargeStage;
        if (exposed && stage == ChargeTier.Second) _mixedSecondReleases++;
        else if (exposed && stage == ChargeTier.First) _mixedFirstReleases++;
        else if (stage > 0) _mixedInterruptedReleases++;
        Log("MIXED_RELEASE", new { time = _time, stage, bodyExposed = exposed,
            state = _boss != null && IsInstanceValid(_boss) ? State(_boss) : "none" });
        SetKey(Key.Z, false, ref _charge);
        _mixedReleasePending = true;
    }

    private static void SetKey(Key code, bool pressed, ref bool current)
    {
        if (current == pressed) return;
        current = pressed;
        Input.ParseInputEvent(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = pressed });
    }
    private void Release()
    {
        AutoplayInput.Move(Vector2.Zero);
        SetKey(Key.Shift, false, ref _shift);
        SetKey(Key.Z, false, ref _charge);
        SetKey(Key.X, false, ref _bomb);
        Input.FlushBufferedEvents();
    }
    private async void End(string? error)
    {
        if (_done) return;
        _done = true;
        Release();
        var unseen = _counts.Where(pair => pair.Value.Completed == 0).Select(pair => pair.Key).ToArray();
        if (_cameo && _character == "Akari" && _game?.Difficulty == GameManager.Diff.Normal && _defeated
            && (_initialHp != 300 || _counts.Values.Sum(count => count.Activated) < 3))
            error ??= "NORMAL midboss Akari must start with 300 HP and activate a third attack wave before defeat.";
        bool passed = error == null && _defeated && _postClearCompleted && _lastHp == 0 && _playerShots.Count > 0
            && (!_observeRoster || unseen.Length == 0);
        var result = new
        {
            passed, character = _character, difficulty = _game?.Difficulty.ToString(), profile = _profile, fire = _fire, cameo = _cameo, time = _time,
            error, defeated = _defeated, hp = _lastHp, initialHp = _initialHp,
            postClearCompleted = _postClearCompleted, postClearSeconds = _defeated ? (double?)(_time - _defeatedAt) : null,
            postClearReason = _postClearCompleted ? _postClearReason : error,
            shootingStartsAfterRoster = _observeRoster,
            mixedPolicy = _fire == "mixed" ? new {
                description = "rapid while shielded; charge tier two only after the visible body gauge opens, tier one near window end; normal input release edges; no baseline comparison",
                firstTierReleases = _mixedFirstReleases, secondTierReleases = _mixedSecondReleases,
                interruptedChargedReleases = _mixedInterruptedReleases,
                rapidInputSeconds = _mixedRapidSeconds, chargeInputSeconds = _mixedChargeSeconds,
            } : null,
            expected = _counts.Count, counts = _counts, unseen, playerProjectiles = _playerShots.Count, breaks = _breaks,
            completedWithoutActivation = _counts.Where(pair => pair.Value.CompletedWithoutActivation > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value.CompletedWithoutActivation),
            minimumShieldSeconds = double.IsPositiveInfinity(_minimumShieldTime) ? (double?)null : _minimumShieldTime,
            maximumOneSecondDamage = _maximumDps, maximumWindowDamage = Math.Max(_maxWindowDamage, _damageInWindow),
            maximumWindowDamageBasis = "HP delta, including final story damage",
            maximumExposedLedgerDamage = _maximumExposedLedgerDamage,
            initialLives = _initialLives, finalLives = _lastLives, initialLinePower = _initialLinePower, finalLinePower = _lastLinePower,
            initialSpeedPower = _initialSpeedPower, finalSpeedPower = _lastSpeedPower, initialJob = _initialJob.ToString(),
            upgradesVerifiedEachFrame = _initialUpgrades.Count,
            playerHits = _game?.RunHitCount,
        };
        Log(passed ? "PASS" : "FAIL", result);
        if (_output.Length > 0)
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(_output), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        GetTree().NodeAdded -= OnNodeAdded;
        GetTree().Paused = false;
        Hud.BubblePaused = false;
        foreach (var attempt in _attempts.Values)
            if (IsInstanceValid(attempt.Attack)) attempt.Attack.Ended -= OnEnded;
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        if (IsInstanceValid(_stage) && !_stage!.IsQueuedForDeletion()) _stage.QueueFree();
        if (GetTree().CurrentScene is { } current && current != this && current != _stage && !current.IsQueuedForDeletion()) current.QueueFree();
        for (int frame = 0; frame < 5; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Audio.Instance?.StopMusicOnce(0);
        foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
        { audio.Stop(); audio.Stream = null; }
        await Task.Delay(250);
        _attempts.Clear();
        _stage = null;
        _boss = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int frame = 0; frame < 5; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(passed ? 0 : 1);
    }
}
