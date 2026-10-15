using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossExtraTechniqueQa : Node
{
    private sealed record Spec(string Character, BossTechniqueKind Kind, int Variant = 0, bool Evolved = false)
    {
        public string Identity => $"{Character}/{Kind}/{Variant}/{Evolved}";
    }
    private sealed class Features
    {
        public int Frames, Peak, Contacts, BodyContacts, StationaryFrames, AccelerationChanges, TurnChanges, NewShots;
        public int LiveGeometryFrames, WarningGeometryFrames, PresentationFrames, DodgePassFrames, PlayerShots;
        public float MaximumSpeed, MaximumTurn, MinimumClearance = float.MaxValue, DistanceMoved, GeometryTravel;
        public int FirstContact = -1;
        public Vector2 FirstContactPosition;
        public ulong Trace = 1469598103934665603UL;
        public readonly HashSet<(ulong, ulong)> Seen = new();
        public readonly Dictionary<(ulong, ulong), Vector2> Velocities = new();
        public readonly Dictionary<(ulong, ulong), Vector2> Positions = new();
        public readonly List<(Vector2 A, Vector2 B, int Shape)> PreviousGeometry = new();
    }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const double Step = 1d / 60;
    private readonly CircleShape2D _core = new() { Radius = Player.HitRadius };
    private readonly Dictionary<string, string> _mechanics = new();
    private readonly Dictionary<string, string> _identities = new();
    private readonly HashSet<string> _names = new();
    private readonly Dictionary<string, (int Trials, int Survivals)> _routeOutcomes = new();
    private GameManager _game = null!;
    private int _cases, _safeProbes, _contacts, _screenshots;
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private static string[] Args => OS.GetCmdlineUserArgs();
    private static string? Option(string name) => Args.FirstOrDefault(arg => arg.StartsWith(name + "="))?.Split('=', 2)[1];
    private static FieldInfo FieldOf(object value, string name)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Private | BindingFlags.Public | BindingFlags.DeclaredOnly) is { } field) return field;
        throw new MissingFieldException(name);
    }
    private static T Read<T>(object value, string name) => (T)FieldOf(value, name).GetValue(value)!;
    private static void Write(object value, string name, object data) => FieldOf(value, name).SetValue(value, data);
    private static object? Call(object value, string name, params object[] args)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetMethod(name, Private | BindingFlags.DeclaredOnly) is { } method) return method.Invoke(value, args);
        throw new MissingMethodException(name);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Pass(string message) => GD.Print("[CharacterTechniqueQA] PASS " + message);
    private Bullet[] Shots(ulong id) => GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>()
        .Where(b => b.Active && b.BossBarrageId == id).ToArray();
    private static List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)> Hazards(BossCharacterTechnique attack)
        => Read<List<(Vector2, Vector2, float, bool, float, int)>>(attack, "_hazards");
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static IEnumerable<Spec> Specifications()
    {
        foreach (string character in new[] { "Akari", "Koharu", "Rei", "Mina" })
        {
            for (int i = 0; i < (character == "Akari" ? 5 : 3); i++) yield return new(character, BossTechniqueKind.Normal, i);
            yield return new(character, BossTechniqueKind.Edge);
            for (int i = 0; i < (character == "Akari" ? 2 : character == "Mina" ? 5 : 3); i++)
                yield return new(character, BossTechniqueKind.Area, i, character == "Mina" && i >= BossMina.DragonPhase);
            if (character != "Mina") yield return new(character, BossTechniqueKind.Signature, 0, true);
            if (character is "Koharu" or "Rei") yield return new(character, BossTechniqueKind.Finale, 0, true);
            for (int i = 0; i < 5; i++) yield return new(character, BossTechniqueKind.Crossfire, i, true);
            yield return new(character, BossTechniqueKind.Dodge, 0, true);
            foreach (bool form in new[] { false, true })
                for (int i = 0; i < 2; i++) yield return new(character, BossTechniqueKind.Extra, i, form);
            if (character != "Mina")
                for (int i = 0; i < 2; i++) yield return new(character, BossTechniqueKind.Cameo, i);
        }
    }

    public override async void _Ready()
    {
        try
        {
            Require(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            Require(GameManager.ShowAttackTelegraphs, "attack warnings remain enabled");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.SelectedJob = Job.Tank;
            _game.MarkIdleDialogSeen("once_midboss_shield");
            Pool.ProcessMode = ProcessModeEnum.Disabled;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            string? selected = Option("--character") ?? Option("--boss");
            if (selected == "MinaBattle") selected = "Mina";
            Require(selected == null || new[] { "Akari", "Koharu", "Rei", "Mina" }.Contains(selected), "valid character selection");
            var specs = Specifications().ToArray();
            Require(specs.Length == 82, "82 independently dispatched named techniques");
            foreach (string character in specs.Select(s => s.Character).Distinct().Where(c => selected == null || c == selected))
                await CheckCharacter(character, specs.Where(s => s.Character == character).ToArray());
            if (selected == null && Option("--kind") == null && Option("--variant") == null)
                Require(_mechanics.Count == 82, "all 82 mechanism identities tested");
            foreach (var outcome in _routeOutcomes.Where(pair => pair.Value.Survivals == 0))
                GD.Print($"[CharacterTechniqueQA] UNRESOLVED_ROUTE {outcome.Key}: 0/{outcome.Value.Trials} tested probes survived");
            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Pass($"ALL LIFECYCLE PASS: trials={_cases}, mechanisms={_mechanics.Count}, displayed_names={_names.Count}, contact_free_probes={_safeProbes}, contact_frames={_contacts}, screenshots={_screenshots}. Contact-free probes are observations, not proof of universal fairness.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError("[CharacterTechniqueQA] FAIL " + exception);
            Hud.BubblePaused = false;
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task CheckCharacter(string character, Spec[] specifications)
    {
        _game.Difficulty = GameManager.Diff.Normal;
        string scene = character == "Mina" ? "MinaBattle" : character;
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Inherit;
        var player = world.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        root.GetNode<StageBackground>("StageBackground").EnterBoss();
        Enemy boss = character switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(), "Rei" => new BossRei(), _ => new BossMina() };
        boss.ProcessMode = ProcessModeEnum.Disabled;
        world.AddChild(boss);
        boss.Position = new Vector2(global::Field.BossCenterX, 70);
        Call(boss, "TickEntrance", 0d);
        Call(boss, "TickEntrance", 100d);
        boss.FinishFormReveal();
        await Frames(3);
        hud.HideBubble();
        Write(hud, "_cutinDoneThisBoss", true);
        Write(hud, "_cutinTimer", 0d);
        CheckHeldShots(boss);
        int hp = Read<int>(boss, "_hp"), maxHp = Read<int>(boss, "_maxHp"), lives = player.Lives;
        string idle = Read<string>(boss, "PreTexPath"), attackArt = Read<string>(boss, "AttackTexPath"), down = Read<string>(boss, "DownTexPath");
        var difficulties = Enum.GetValues<GameManager.Diff>();
        if (Option("--difficulty") is { } difficultyText)
        {
            Require(Enum.TryParse<GameManager.Diff>(difficultyText, true, out var selected), "valid difficulty selection");
            difficulties = new[] { selected };
        }
        else if (Args.Contains("--visual")) difficulties = new[] { GameManager.Diff.Normal };
        var starts = new[] { global::Field.PlayerStart, new Vector2(global::Field.Right - 18, global::Field.Bottom - 18) };
        if (Args.Contains("--no-dodge") || Args.Contains("--grid-starts"))
            starts = new[] { global::Field.Left + 8, global::Field.CenterX, global::Field.Right - 8 }
                .SelectMany(x => new[] { 8f, global::Field.CenterY, global::Field.Bottom - 8 }.Select(y => new Vector2(x, y))).ToArray();
        var indices = Enumerable.Range(0, starts.Length).ToArray();
        if (Option("--start") is { } start)
        {
            Require(int.TryParse(start, out int selected) && selected >= 0 && selected < starts.Length, "valid start selection");
            indices = new[] { selected };
        }
        foreach (var spec in specifications)
        {
            if (Option("--kind") is { } kind && !spec.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase)) continue;
            if (Option("--variant") is { } variant && spec.Variant.ToString() != variant) continue;
            SetForm(boss, spec, idle, attackArt, down);
            foreach (var difficulty in difficulties)
            foreach (int index in indices)
            {
                _game.Difficulty = difficulty;
                if (Args.Contains("--both-policies"))
                {
                    await CheckTrial(spec, boss, player, hud, starts[index], index, "stationary");
                    await CheckTrial(spec, boss, player, hud, starts[index], index, "reactive");
                }
                else await CheckTrial(spec, boss, player, hud, starts[index], index);
                Require(Read<int>(boss, "_hp") == hp && Read<int>(boss, "_maxHp") == maxHp && player.Lives == lives,
                    spec.Identity + ": techniques preserve HP and lives independently of contact sampling");
            }
        }
        Pass(character + ": all requested techniques completed; HP/lives unchanged");
        root.QueueFree();
        Pool.DespawnAll();
        Hud.BubblePaused = false;
        await Frames(5);
    }

    private static void SetForm(Enemy boss, Spec spec, string idle, string attack, string down)
    {
        if (boss is BossMina)
        {
            int phase = spec.Kind == BossTechniqueKind.Area ? spec.Variant : spec.Evolved ? BossMina.DragonPhase : 0;
            Write(boss, "_pattern", phase);
            Call(boss, "ChangeBattleCostume", BossMina.BattleCostumePath(phase, "idle"), BossMina.BattleCostumePath(phase, "attack"), BossMina.BattleDownPath(phase));
        }
        else
        {
            Write(boss, "_form2", spec.Evolved);
            Call(boss, "ChangeBattleCostume", spec.Evolved ? Read<string>(boss, "Form2TexPath") : idle,
                spec.Evolved ? Read<string>(boss, "Form2AttackTexPath") : attack, spec.Evolved ? Read<string>(boss, "Form2DownTexPath") : down);
            Call(boss, "SetBodyPose", spec.Evolved ? BossParts.Pose.Form2 : BossParts.Pose.Idle);
            if (Read<BossParts?>(boss, "_parts") is { } parts) parts.Visible = !spec.Evolved;
        }
        boss.FinishFormReveal();
    }

    private BossCharacterTechnique Begin(Enemy boss, Spec spec)
    {
        var attack = BossCharacterTechnique.Begin(boss, spec.Kind, spec.Variant);
        attack.SetPhysicsProcess(false);
        attack.SetProcess(false);
        return attack;
    }
    private void CheckHeldShots(Enemy boss)
    {
        var shot = Pool.SpawnBossShot(boss.ShotCenter, Vector2.Down, boss.BodyShotJob!.Value, false);
        shot.Accel = shot.Homing = false;
        shot.Velocity = Vector2.Zero;
        shot.DeployBossFormation(boss.ShotCenter + Vector2.Down * 40, warnedLaunch: true);
        shot.HoldBossShot();
        Require(shot.LeadIn, "held-shot test starts before deployment finishes");
        for (int frame = 0; frame < 30; frame++)
        {
            shot._PhysicsProcess(Step);
            Require(shot.Active && shot.CollisionLayer == 0 && shot.CollisionMask == 0,
                "held shot remains harmless before and after deployment finishes");
        }
        Require(!shot.LeadIn, "held-shot test crosses deployment completion");
        shot.ReleaseBossShot();
        Require(shot.CollisionLayer == 8 && shot.CollisionMask == 1, "released shot regains its enemy hit shape");
        shot.HoldBossShot();
        ulong activation = shot.ActivationId;
        Pool.Despawn(shot);
        var reused = Pool.SpawnBossShot(boss.ShotCenter, Vector2.Down, boss.BodyShotJob.Value, false);
        Require(ReferenceEquals(shot, reused) && reused.ActivationId != activation,
            "held-shot test actually reuses the pooled instance");
        reused.Accel = reused.Homing = false;
        reused.Velocity = Vector2.Zero;
        reused.DeployBossFormation(boss.ShotCenter + Vector2.Down * 40, warnedLaunch: true);
        for (int frame = 0; frame < 30; frame++) reused._PhysicsProcess(Step);
        Require(!reused.LeadIn && reused.CollisionLayer == 8 && reused.CollisionMask == 1,
            "pooled reactivation clears held status through deployment");
        Pool.Despawn(reused);
        Pass(boss.GetType().Name + ": held-shot deployment, release, and pool reset");
    }

    private static Vector2 Toward(Player player, Vector2 goal) => player.Position.DistanceTo(goal) > player.SlowestMoveSpeed * Step
        ? player.Position.DirectionTo(goal) : Vector2.Zero;

    private async Task CheckTrial(Spec spec, Enemy boss, Player player, Hud hud, Vector2 start, int startIndex, string? explicitPolicy = null)
    {
        Pool.DespawnAll();
        player.Position = start;
        Write(player, "_dodgeCd", 0f);
        Write(player, "_dodgeTimer", 0f);
        Write(player, "_dodgeInv", 0f);
        Write(player, "_fireCooldown", 999f);
        bool noDodge = Args.Contains("--no-dodge");
        _game.TrainingSetUpgrade("n_dodge", spec.Kind == BossTechniqueKind.Dodge && !noDodge);
        boss.Position = new Vector2(start.X < global::Field.CenterX ? global::Field.Right - 44 : global::Field.CenterX, 70);
        if (boss is BossMina { IsDragonForm: true } mina) boss.Position = mina.KeepDragonInField(boss.Position);
        if (Args.Contains("--grid-starts") || noDodge && spec.Kind == BossTechniqueKind.Dodge)
        {
            var validStart = WalkingProbeStart(start, boss);
            if (validStart != start)
                GD.Print($"[CharacterTechniqueQA] START_ADJUST {spec.Identity}/{_game.Difficulty}/start{startIndex}: {start} -> {validStart}, boss={boss.Position}");
            player.Position = start = validStart;
        }
        Hud.BubblePaused = false;
        var attack = Begin(boss, spec);
        ulong id = attack.GetInstanceId();
        string label = $"{spec.Identity}/{_game.Difficulty}/start{startIndex}";
        Require(!string.IsNullOrWhiteSpace(attack.SpellName) && !string.IsNullOrWhiteSpace(attack.MechanicKey) && !string.IsNullOrWhiteSpace(attack.Counterplay), label + ": meaningful identity and counterplay");
        if (_mechanics.TryGetValue(attack.MechanicKey, out string? owner)) Require(owner == spec.Identity, label + ": mechanism key reused by another attack");
        else _mechanics[attack.MechanicKey] = spec.Identity;
        if (_identities.TryGetValue(spec.Identity, out string? key)) Require(key == attack.MechanicKey, label + ": stable mechanism across difficulty/position");
        else _identities[spec.Identity] = attack.MechanicKey;
        _names.Add(attack.SpellName);
        Require(Read<string>(hud, "_spellName") == attack.SpellName && Read<double>(hud, "_spellTimer") > 0, label + ": real HUD announcement");
        CheckPause(attack, label);
        var features = new Features();
        Vector2 direction = Vector2.Zero;
        bool flightPause = false, warningSaved = false, activeSaved = false, developmentSaved = false;
        bool cutinChecked = false, independentCitationBranch = false;
        var body = Read<CollisionShape2D>(boss, "_bodyShape");
        string policy = explicitPolicy ?? Option("--policy") ?? (startIndex == 0 ? "stationary" : "reactive");
        Require(policy is "stationary" or "reactive", "valid policy selection");
        int dodgesBefore = player.DodgeCount;
        int routeCorner = 0;
        bool clockAtRoot = false, clockRoutePlanned = false, clockOutside = false;
        Vector2 clockExit = start;
        bool pageRoutePlanned = false, pageCornerReached = false;
        Vector2 pageCorner = start, pageGoal = start;
        Vector2 finaleAxis = Vector2.Zero, finaleGoal = start, attentionGoal = start;
        int finaleCueFrame = 0, attentionCueFrame = -1;
        bool finalePlanNeeded = false, attentionPlanNeeded = true;
        var returnCenters = new Queue<Vector2>();
        int quotationIndex = -1, quotationCueFrame = -1, repostCueFrame = -1;
        bool quotationPlanNeeded = true, repostPlanNeeded = true;
        Vector2 quotationGoal = start;
        Rect2 lockedQuotation = default;
        bool observedRecoil = attack.MechanicKey == "mina.elastic-disconnection";
        int recoilCueFrame = -1;
        bool recoilPlanNeeded = true;
        Vector2 recoilGoal = start;
        bool airlockClosedSeen = false;
        int airlockOpenFrame = -1;
        Vector2 walkGoal = noDodge && spec.Kind == BossTechniqueKind.Dodge && !observedRecoil
            ? DodgeWalkGoal(player, boss, attack) : start;
        for (int frame = 0; frame < 3600 && !attack.Finished; frame++)
        {
            var before = Shots(id);
            Vector2 previous = player.Position;
            if (attack.MechanicKey == "mina.return-distance")
            {
                returnCenters.Enqueue(boss.Position);
                if (returnCenters.Count > 25) returnCenters.Dequeue();
            }
            if (attack.MechanicKey == "koharu.cross-reorientation")
            {
                var lines = Hazards(attack);
                if (lines.Count > 0)
                {
                    Vector2 axis = lines[0].A.DirectionTo(lines[0].B);
                    if (!axis.IsEqualApprox(finaleAxis))
                    {
                        finaleAxis = axis;
                        finaleCueFrame = frame;
                        finalePlanNeeded = true;
                    }
                }
            }
            if (attack.MechanicKey == "koharu.attention-charge" && attentionCueFrame < 0
                && Hazards(attack).Any(h => h.Shape == 1 && h.A.DistanceTo(boss.Position) > 8))
                attentionCueFrame = frame;
            if (observedRecoil && recoilCueFrame < 0 && Read<bool>(attack, "_connectionsLocked"))
            {
                Require(Hazards(attack).Any(h => h.Shape == 0 && !h.Live), label + ": connection lock visibly announces recoil paths");
                recoilCueFrame = frame;
            }
            if (attack.MechanicKey == "mina.protective-airlock")
            {
                Vector2 exitDoor = Read<Vector2>(attack, "_airlockOrigin") + Read<Vector2>(attack, "_airlockAxis") * 60;
                bool closed = Hazards(attack).Any(h => h.Shape == 0 && h.Live
                    && Geometry2D.GetClosestPointToSegment(exitDoor, h.A, h.B).DistanceTo(exitDoor) < .1f);
                if (closed) airlockClosedSeen = true;
                else if (airlockClosedSeen && airlockOpenFrame < 0) airlockOpenFrame = frame;
            }
            if (attack.MechanicKey == "rei-reader-selects-quotation")
            {
                int selected = Read<int>(attack, "_selectionIndex");
                if (selected != quotationIndex)
                {
                    quotationIndex = selected;
                    quotationCueFrame = -1;
                    quotationPlanNeeded = true;
                    quotationGoal = player.Position;
                }
                if (Read<bool>(attack, "_selectionLocked"))
                {
                    if (quotationCueFrame < 0)
                    {
                        quotationCueFrame = frame;
                        lockedQuotation = Read<Rect2>(attack, "_selectionRect");
                    }
                    Require(lockedQuotation == Read<Rect2>(attack, "_selectionRect"), label + ": confirmed quotation stays fixed while the player leaves");
                }
                if (repostCueFrame < 0 && Read<double>(attack, "_selectionRepostAt") >= 0) repostCueFrame = frame;
            }
            if (policy == "reactive" && frame >= 24)
            {
                if (frame % 6 == 0) direction = ChooseMovement(player, boss, attack, before, features, direction);
                if (attack.MechanicKey == "koharu.motion-observation" && attack.Elapsed < 5.5) direction = Vector2.Up;
                if (attack.MechanicKey == "koharu.attention-charge")
                {
                    if (attentionCueFrame < 0 || frame < attentionCueFrame + 24)
                        direction = Toward(player, boss.Position + Vector2.Down * 64);
                    else
                    {
                        if (attentionPlanNeeded)
                        {
                            attentionGoal = TelegraphWalkGoal(player, boss, attack);
                            attentionPlanNeeded = false;
                        }
                        direction = Toward(player, attentionGoal);
                    }
                }
                if (attack.MechanicKey == "koharu.screen-aperture")
                    direction = Toward(player, startIndex == 0 ? new Vector2(198, 8) : new Vector2(374, 86));
                if (attack.MechanicKey == "koharu.cross-reorientation")
                {
                    if (finalePlanNeeded && frame >= finaleCueFrame + 24)
                    {
                        finaleGoal = TelegraphWalkGoal(player, boss, attack);
                        finalePlanNeeded = false;
                    }
                    direction = Toward(player, finaleGoal);
                }
                if (attack.MechanicKey == "koharu.deer-leap" && attack.Elapsed < 3.8)
                    direction = Toward(player, start + (startIndex == 0 ? new Vector2(0, 72) : new Vector2(-72, 0)));
                if (attack.MechanicKey == "koharu.braided-lights")
                    direction = Toward(player, new Vector2(startIndex == 0 ? 84 : 376, start.Y));
                if (attack.MechanicKey == "koharu.unbalanced-support")
                    direction = Toward(player, new Vector2(start.X, 8));
                if (attack.MechanicKey == "koharu.overlapping-gazes" && attack.Elapsed < 2)
                    direction = Toward(player, start + new Vector2(startIndex == 0 ? 72 : -72, 0));
                if (attack.MechanicKey == "akari.delivery-overtake")
                    direction = Toward(player, start + new Vector2(startIndex == 0 ? 90 : -100, 0));
                if (attack.MechanicKey == "akari.extending-clock")
                {
                    Vector2 center = Read<Vector2>(attack, "_origin");
                    Vector2 radial = center.DirectionTo(start);
                    Vector2 approach = center + radial * 6 + new Vector2(-radial.Y, radial.X) * 5;
                    if (!clockRoutePlanned)
                    {
                        clockOutside = BodyBlocks(boss, start, approach);
                        if (clockOutside)
                        {
                            var exits = new[] { new Vector2(global::Field.Left + 8, start.Y), new Vector2(global::Field.Right - 8, start.Y) }
                                .Where(exit => !BodyBlocks(boss, start, exit)).OrderBy(exit => start.DistanceSquaredTo(exit)).ToArray();
                            Require(exits.Length > 0, label + ": clock outer route avoids the boss body");
                            clockExit = exits[0];
                        }
                        clockRoutePlanned = true;
                    }
                    if (clockOutside) direction = Toward(player, clockExit);
                    else
                    {
                        if (player.Position.DistanceTo(approach) <= player.SlowestMoveSpeed * Step) clockAtRoot = true;
                        direction = Toward(player, clockAtRoot ? center : approach);
                    }
                }
                if (attack.MechanicKey == "akari.braided-delivery")
                {
                    float x = startIndex == 0 ? 366 : 84;
                    direction = Toward(player, new Vector2(x, Math.Abs(player.Position.X - x) < 2 ? 140 : 198));
                }
                if (attack.MechanicKey == "akari.alternating-inboxes")
                {
                    if (_game.Difficulty == GameManager.Diff.Hard)
                    {
                        var corners = new[] { new Vector2(366, 132), new Vector2(84, 132), new Vector2(84, 198), new Vector2(366, 198) };
                        Vector2 goal = corners[routeCorner % corners.Length];
                        if (player.Position.DistanceTo(goal) <= player.SlowestMoveSpeed * Step * 1.1) routeCorner++;
                        direction = Toward(player, corners[routeCorner % corners.Length]);
                    }
                    else
                    {
                        bool lunatic = _game.Difficulty == GameManager.Diff.Lunatic;
                        float angle = (float)(attack.Elapsed - .4) * (lunatic ? .49f : .43f) + (startIndex == 0 ? Mathf.Pi : 0);
                        direction = Toward(player, new Vector2(global::Field.CenterX + Mathf.Cos(angle) * 132,
                            (lunatic ? 144 : 162) + Mathf.Sin(angle) * (lunatic ? 44 : 40)));
                    }
                }
                if (attack.MechanicKey == "rei-continuous-sentence")
                {
                    double warning = Read<double>(attack, "_warning");
                    double duration = _game.Difficulty == GameManager.Diff.Lunatic ? 6.8 : _game.Difficulty == GameManager.Diff.Hard ? 7.5 : 8.8;
                    float progress = Mathf.Clamp((float)((attack.Elapsed - warning) / duration), 0, 1);
                    float from = start.X < global::Field.CenterX ? global::Field.Left + 30 : global::Field.Right - 30;
                    float to = start.X < global::Field.CenterX ? global::Field.Right - 30 : global::Field.Left + 30;
                    Vector2 goal = attack.Elapsed < warning ? new Vector2(start.X, 108)
                        : new Vector2(Mathf.Lerp(from, to, progress), 108 + Mathf.Sin(progress * Mathf.Tau) * 24);
                    direction = Toward(player, goal);
                }
                if (attack.MechanicKey == "rei-reader-selects-quotation")
                {
                    if (repostCueFrame >= 0)
                    {
                        if (repostPlanNeeded && frame >= repostCueFrame + 24)
                        {
                            quotationGoal = TelegraphWalkGoal(player, boss, attack, verticalOnly: true);
                            repostPlanNeeded = false;
                        }
                    }
                    else if (quotationCueFrame >= 0 && frame >= quotationCueFrame + 24 && quotationPlanNeeded)
                    {
                        quotationGoal = TelegraphWalkGoal(player, boss, attack);
                        quotationPlanNeeded = false;
                    }
                    direction = Toward(player, quotationGoal);
                }
                if (attack.MechanicKey == "rei-negated-quotation")
                {
                    var regions = Hazards(attack);
                    if (regions.Count == 4) direction = Toward(player, new Vector2(225, 130));
                    else if (regions.Count == 1)
                        direction = Toward(player, new Vector2(player.Position.Y < 183 ? player.Position.X : 225, 184));
                }
                if (attack.MechanicKey == "rei-branch-front-tip")
                {
                    Vector2 root = Read<Vector2>(attack, "_frontRoot");
                    Vector2 branches = Read<Vector2>(attack, "_frontUp");
                    direction = Toward(player, new Vector2(start.X, Mathf.Clamp(root.Y - branches.Y * 24, 8, global::Field.Bottom - 8)));
                }
                if (attack.MechanicKey == "rei-optional-quote-peeling")
                {
                    if (!pageRoutePlanned)
                    {
                        var pages = Read<List<(Rect2 Rect, int Hits)>>(attack, "_pages");
                        float top = pages.Min(page => page.Rect.Position.Y) - 7;
                        float bottom = pages.Max(page => page.Rect.End.Y) + 7;
                        float exit = top < 8 ? bottom : bottom > global::Field.Bottom - 8 ? top
                            : Math.Abs(start.Y - top) <= Math.Abs(start.Y - bottom) ? top : bottom;
                        pageGoal = new Vector2(start.X, exit);
                        if (BodyBlocks(boss, start, pageGoal))
                        {
                            float radius = body.Shape is CapsuleShape2D capsule ? capsule.Radius : ((CircleShape2D)body.Shape).Radius;
                            float reach = radius + Player.HitRadius + 6;
                            var sides = new[] { body.GlobalPosition.X - reach, body.GlobalPosition.X + reach }
                                .Where(x => x >= global::Field.Left + 8 && x <= global::Field.Right - 8
                                    && !BodyBlocks(boss, start, new Vector2(x, start.Y))
                                    && !BodyBlocks(boss, new Vector2(x, start.Y), new Vector2(x, exit)))
                                .OrderBy(x => Math.Abs(x - start.X)).ToArray();
                            Require(sides.Length > 0, label + ": page exit has a walkable path around the boss body");
                            pageCorner = new Vector2(sides[0], start.Y);
                            pageGoal.X = sides[0];
                        }
                        pageRoutePlanned = true;
                    }
                    if (player.Position.DistanceTo(pageCorner) <= player.SlowestMoveSpeed * Step) pageCornerReached = true;
                    direction = Toward(player, pageCornerReached ? pageGoal : pageCorner);
                }
                if (attack.MechanicKey is "rei-edit-three-destinations" or "rei-opaque-quotation-layers")
                    direction = Toward(player, new Vector2(start.X, startIndex == 0 ? 8 : 208));
                if (attack.MechanicKey == "rei-directed-reading-gate")
                {
                    double crossing = (global::Field.Width + 36) / (38 + (int)_game.Difficulty * 3.0);
                    bool reverse = attack.Elapsed >= Read<double>(attack, "_warning") + crossing;
                    direction = Toward(player, new Vector2(reverse ? global::Field.Left + 8 : global::Field.Right - 8,
                        startIndex == 0 ? 160 : start.Y));
                }
                if (attack.MechanicKey == "rei-citation-dependency-erasure")
                {
                    double[] erased = Read<double[]>(attack, "_citationDeletes");
                    Vector2 goal;
                    if (startIndex == 0)
                        goal = attack.Elapsed >= erased[3] ? new Vector2(global::Field.Left + 53, 108) : new Vector2(start.X, 72);
                    else if (player.Position.Y > 145 && routeCorner == 0)
                        goal = new Vector2(start.X, 144);
                    else
                    {
                        routeCorner = 1;
                        goal = new Vector2(global::Field.CenterX,
                            Math.Abs(player.Position.X - global::Field.CenterX) < 1 && attack.Elapsed >= erased[7] ? 180 : 144);
                    }
                    direction = Toward(player, goal);
                }
                if (attack.MechanicKey == "mina.second-visit-weight" && attack.Elapsed < 7)
                    direction = player.Position.DirectionTo(start + Vector2.Left * (Mathf.Sin((float)attack.Elapsed * 1.7f) * 35 + 40));
                if (attack.MechanicKey == "mina.protective-airlock")
                {
                    float distance = airlockOpenFrame >= 0 && frame >= airlockOpenFrame + 24 ? 84 : 42;
                    direction = Toward(player, Read<Vector2>(attack, "_airlockOrigin") + Read<Vector2>(attack, "_airlockAxis") * distance);
                }
                if (attack.MechanicKey == "mina.decompressing-pages" && startIndex == 1)
                    direction = Toward(player, new Vector2(global::Field.Left + 8, 198));
                if (attack.MechanicKey == "mina.return-distance")
                {
                    Vector2 observed = attack.Elapsed < Read<double>(attack, "_warning") ? boss.Position : returnCenters.Peek();
                    Vector2 radial = observed.DirectionTo(player.Position);
                    Vector2 goal = observed + radial * 65;
                    direction = Toward(player, goal);
                }
                if (attack.MechanicKey == "akari.coincident-membranes")
                {
                    var boundaries = Hazards(attack);
                    if (boundaries.Count == 2)
                    {
                        float middle = (boundaries[0].A.X + boundaries[1].A.X) * .5f;
                        direction = Math.Abs(player.Position.X - middle) > 1 ? Vector2.Right * Math.Sign(middle - player.Position.X) : Vector2.Zero;
                    }
                }
                if (observedRecoil)
                {
                    if (recoilCueFrame >= 0 && frame >= recoilCueFrame + 24 && recoilPlanNeeded)
                    {
                        recoilGoal = TelegraphWalkGoal(player, boss, attack, verticalOnly: true);
                        recoilPlanNeeded = false;
                    }
                    direction = Toward(player, recoilGoal);
                }
                if (noDodge && spec.Kind == BossTechniqueKind.Dodge && !observedRecoil) direction = Toward(player, walkGoal);
                if (spec.Kind == BossTechniqueKind.Dodge && !noDodge)
                {
                    if (player.DodgeReady && attack.DangerDistance(player.Position, false) < 2)
                        Call(player, "TryDodge", direction);
                    AutoplayInput.Move(direction * player.SlowestMoveSpeed / player.MoveSpeed);
                    Input.FlushBufferedEvents();
                    player._PhysicsProcess(Step);
                }
                else
                    player.Position = (player.Position + direction * player.SlowestMoveSpeed * (float)Step)
                        .Clamp(new Vector2(global::Field.Left + 8, 8), new Vector2(global::Field.Right - 8, global::Field.Bottom - 8));
                if (attack.MechanicKey == "mina.reply-driven-stars" && frame % 24 == 0)
                {
                    Call(player, "Fire");
                    features.PlayerShots++;
                }
            }
            foreach (var friendly in GetTree().GetNodesInGroup("player_bullets").OfType<Bullet>().Where(b => b.Active))
            {
                friendly.CollisionLayer = friendly.CollisionMask = 0;
                friendly._PhysicsProcess(Step);
            }
            features.DistanceMoved += previous.DistanceTo(player.Position);
            if (!cutinChecked) Write(hud, "_cutinTimer", 2d);
            double spellTime = Read<double>(hud, "_spellTimer");
            attack.Tick(Step);
            if (attack.MechanicKey == "rei-citation-dependency-erasure")
            {
                bool[] roots = Read<bool[]>(attack, "_deletedRoots");
                double[] erased = Read<double[]>(attack, "_citationDeletes");
                if (roots[1] && !roots[0] && !roots[2])
                {
                    Require(new[] { 0, 1, 4, 5, 6, 8 }.All(i => double.IsPositiveInfinity(erased[i])),
                        label + ": deleting one citation root preserves unrelated branches");
                    if (new[] { 2, 3, 7 }.All(i => attack.Elapsed >= erased[i]))
                    {
                        Require(Hazards(attack).Count(h => h.Shape == 2 && h.Live) == 6,
                            label + ": only the deleted root's child and grandchildren disappear");
                        independentCitationBranch = true;
                    }
                }
            }
            if (!cutinChecked && !attack.Finished && (attack.IsStriking || Shots(id).Any(b => !b.LeadIn && b.CollisionLayer != 0)))
            {
                Require(Read<double>(hud, "_cutinTimer") == 0 && Read<double>(hud, "_spellTimer") == spellTime,
                    label + ": live attack ends the portrait cutin while preserving its spell announcement");
                cutinChecked = true;
            }
            foreach (var bullet in Shots(id))
            {
                ulong activation = bullet.ActivationId;
                bullet._PhysicsProcess(Step);
                if (bullet.Active && bullet.ActivationId == activation) ObserveBullet(features, bullet);
            }
            var shots = Shots(id);
            features.Frames++;
            features.Peak = Math.Max(features.Peak, shots.Length);
            if (Hazards(attack).Any(h => h.Live)) features.LiveGeometryFrames++;
            if (Hazards(attack).Any(h => !h.Live)) features.WarningGeometryFrames++;
            if (Hazards(attack).Count > 0 || shots.Length > 0 || ((System.Collections.ICollection)FieldOf(attack, "_hints").GetValue(attack)!).Count > 0)
                features.PresentationFrames++;
            var geometry = Hazards(attack);
            if (features.PreviousGeometry.Count == geometry.Count)
                for (int i = 0; i < geometry.Count; i++)
                    if (features.PreviousGeometry[i].Shape == geometry[i].Shape)
                        features.GeometryTravel += features.PreviousGeometry[i].A.DistanceTo(geometry[i].A)
                            + features.PreviousGeometry[i].B.DistanceTo(geometry[i].B);
            features.PreviousGeometry.Clear();
            features.PreviousGeometry.AddRange(geometry.Select(h => (h.A, h.B, h.Shape)));
            bool contact = attack.CoversPoint(player.Position) || shots.Any(b => Hits(b, player.Position));
            bool bodyContact = !body.Disabled && body.Shape.Collide(body.GlobalTransform, _core, new Transform2D(0, player.Position));
            bool dodgeProtected = Read<float>(player, "_dodgeInv") > 0;
            if ((contact || bodyContact) && dodgeProtected) features.DodgePassFrames++;
            if (contact && !dodgeProtected) features.Contacts++;
            if (bodyContact && !dodgeProtected) features.BodyContacts++;
            if ((contact || bodyContact) && !dodgeProtected && features.FirstContact < 0)
            {
                features.FirstContact = frame;
                features.FirstContactPosition = player.Position;
                if (Args.Contains("--visual") && Args.Contains("--capture-contact") && !attack.Finished)
                    await Capture(spec, hud, attack, "first-contact");
            }
            float clearance = Math.Min(attack.DangerDistance(player.Position, false), shots.Where(b => !b.LeadIn && b.CollisionLayer != 0)
                .Select(b => b.GlobalPosition.DistanceTo(player.Position) - b.CollisionRadius - Player.HitRadius).DefaultIfEmpty(float.MaxValue).Min());
            features.MinimumClearance = Math.Min(features.MinimumClearance, clearance);
            unchecked
            {
                features.Trace ^= (ulong)(shots.Length + Hazards(attack).Count * 251);
                features.Trace *= 1099511628211;
                if (frame % 12 == 0)
                {
                    foreach (var bullet in shots.Take(32))
                    { features.Trace ^= (ulong)(Math.Max(0, (int)bullet.Position.X) * 257 + Math.Max(0, (int)bullet.Position.Y)); features.Trace *= 1099511628211; }
                    foreach (var hazard in geometry)
                    {
                        features.Trace ^= (ulong)(Math.Max(0, (int)hazard.A.X) * 257 + Math.Max(0, (int)hazard.A.Y)
                            + Math.Max(0, (int)hazard.B.X) * 65537 + Math.Max(0, (int)(hazard.Radius * 10)));
                        features.Trace *= 1099511628211;
                    }
                }
            }
            Require(global::Field.Rect.HasPoint(player.Position), label + ": policy stays on the field");
            if (!flightPause && !attack.Finished && (shots.Any(b => !b.LeadIn) || attack.IsStriking))
            { CheckPause(attack, label); flightPause = true; }
            if (Args.Contains("--visual") && (startIndex == 0 || Option("--start") == startIndex.ToString()) && !attack.Finished)
            {
                if (!warningSaved && frame >= 12) { await Capture(spec, hud, attack, "warning"); warningSaved = true; }
                if (!activeSaved && (shots.Any(b => !b.LeadIn && b.CollisionLayer != 0) || attack.IsStriking))
                { await Capture(spec, hud, attack, "active"); activeSaved = true; }
                double developmentAt = attack.MechanicKey switch
                {
                    "koharu.cross-reorientation" => 2.1,
                    "koharu.deer-leap" => 2.9,
                    _ => 5.2,
                };
                if (!developmentSaved && attack.Elapsed >= developmentAt)
                { await Capture(spec, hud, attack, "development"); developmentSaved = true; }
            }
        }
        Require(attack.Finished && attack.Completed && Shots(id).Length == 0, label + ": finite natural completion clears owned projectiles");
        Require(features.PresentationFrames > 0, label + ": attack provides visible presentation even when the player's correct action suppresses firing");
        if (attack.MechanicKey == "rei-citation-dependency-erasure")
        {
            double[] erased = Read<double[]>(attack, "_citationDeletes");
            Require(independentCitationBranch && erased.All(double.IsFinite), label + ": all citation branches resolve independently");
            foreach (var (parent, child) in new[] { (3, 2), (3, 7), (4, 0), (4, 8), (5, 1), (5, 6) })
                Require(Math.Abs(erased[child] - erased[parent] - .3) <= Step * 2,
                    label + ": citation deletion follows its parent after the visible delay");
        }
        Write(hud, "_cutinTimer", 0d);
        AutoplayInput.Move(Vector2.Zero);
        Input.FlushBufferedEvents();
        CheckCancel(boss, player, spec, start, label);
        if (startIndex == 0) CheckInterruption(boss, player, spec, start, label);
        if (startIndex == 0 && attack.MechanicKey == "rei-optional-quote-peeling")
            CheckQuotePeeling(boss, player, spec, label);
        if (startIndex == 0 && policy == "stationary" && attack.MechanicKey == "rei-directed-reading-gate")
            CheckReadingGate(boss, player, spec, label);
        if (startIndex == 0 && policy == "stationary" && attack.MechanicKey == "mina.snapshot-exchange")
            CheckMemoryExchange(boss, player, spec, label);
        if (startIndex == 0 && policy == "stationary" && attack.MechanicKey == "rei-opaque-quotation-layers")
            CheckQuotationLayers(boss, player, spec, label);
        if (startIndex == 1 && policy == "reactive" && attack.MechanicKey == "mina.protective-airlock")
            CheckFieldBoundary(boss, player, spec, label);
        bool survived = features.Contacts == 0 && features.BodyContacts == 0;
        if (noDodge && spec.Kind == BossTechniqueKind.Dodge)
        {
            Require(player.DodgeCount == dodgesBefore, label + ": walking probe never invokes dodge");
            if (policy == "reactive") Require(survived, label + ": walking route avoids attack and boss contact");
        }
        if (survived) _safeProbes++;
        _contacts += features.Contacts;
        string route = attack.MechanicKey + "/" + _game.Difficulty;
        var outcome = _routeOutcomes.GetValueOrDefault(route);
        _routeOutcomes[route] = (outcome.Trials + 1, outcome.Survivals + (survived ? 1 : 0));
        GD.Print("[CharacterTechniqueQA] RESULT " + JsonSerializer.Serialize(new
        {
            label, name = attack.SpellName, mechanic = attack.MechanicKey, policy, noDodge, completed = true, survived, cutinChecked,
            startX = start.X, startY = start.Y,
            seconds = Math.Round(features.Frames * Step, 3), seeds = attack.ShotsCreated, observed = features.NewShots,
            peak = features.Peak, geometry = features.LiveGeometryFrames, contacts = features.Contacts, bodyContacts = features.BodyContacts,
            geometryTravel = Math.Round(features.GeometryTravel, 2),
            dodges = player.DodgeCount - dodgesBefore, dodgeCrossingFrames = features.DodgePassFrames, playerShots = features.PlayerShots,
            firstContact = features.FirstContact, contactX = MathF.Round(features.FirstContactPosition.X, 2), contactY = MathF.Round(features.FirstContactPosition.Y, 2),
            maxSpeed = Math.Round(features.MaximumSpeed, 2), turns = features.TurnChanges, accelerations = features.AccelerationChanges,
            stopped = features.StationaryFrames, moved = Math.Round(features.DistanceMoved, 2),
            minimumClearance = features.MinimumClearance == float.MaxValue ? (float?)null : MathF.Round(features.MinimumClearance, 2), trace = features.Trace.ToString("x16")
        }));
        _cases++;
        await Frames(1);
    }

    private void CheckFieldBoundary(Enemy boss, Player player, Spec spec, string label)
    {
        Vector2 saved = player.Position;
        player.Position = new Vector2(global::Field.Right - 18, global::Field.Bottom - 18);
        var attack = Begin(boss, spec);
        for (int frame = 0; frame < 180 && !Hazards(attack).Any(h => h.Shape == 2 && h.Live); frame++)
            attack.Tick(Step);
        var regions = Hazards(attack).Where(h => h.Shape == 2 && h.Live
            && h.B.X == global::Field.Right && h.B.Y == global::Field.Bottom).ToArray();
        Require(regions.Length > 0, label + ": actual airlock pressure reaches the right and bottom field boundaries");
        var region = regions[0];
        Vector2 right = new(global::Field.Right, (region.A.Y + region.B.Y) * .5f);
        Vector2 bottom = new((region.A.X + region.B.X) * .5f, global::Field.Bottom);
        foreach (Vector2 point in new[] { right, bottom })
        {
            Require(attack.DangerDistance(point, false) < 0, label + ": boundary sample lies inside the actual live region");
            Require(attack.CoversPoint(point), label + ": field boundary does not disable a live geometry hit");
        }
        Require(!attack.CoversPoint(right + Vector2.Right * .01f) && !attack.CoversPoint(bottom + Vector2.Down * .01f),
            label + ": geometry excludes points outside the field");
        Vector2 safe = Read<Vector2>(attack, "_airlockOrigin") + Read<Vector2>(attack, "_airlockAxis") * 84;
        Require(attack.DangerDistance(safe, false) > 3 && !attack.CoversPoint(safe),
            label + ": an ordinary safe point remains harmless");
        attack.Cancel();
        player.Position = saved;
        Pass(label + ": inclusive field boundary geometry, exterior exclusion, and safe interior");
    }

    private void CheckReadingGate(Enemy boss, Player player, Spec spec, string label)
    {
        player.Position = new Vector2(global::Field.Left + 8, 160);
        var attack = Begin(boss, spec);
        double warning = Read<double>(attack, "_warning");
        while (attack.Elapsed < warning + 1.2) attack.Tick(Step);
        player.Position = new Vector2(Read<float>(attack, "_previousGate") - 1, 160);
        attack.Tick(Step);
        bool crossed = false;
        for (int frame = 0; frame < 60 && !crossed; frame++)
        {
            player.Position += Vector2.Right * player.SlowestMoveSpeed * (float)Step;
            attack.Tick(Step);
            Require(!attack.CoversPoint(player.Position), label + ": crossing in reading order is harmless");
            crossed = player.Position.X - Read<float>(attack, "_previousGate") > 2;
        }
        Require(crossed, label + ": minimum movement speed can overtake the reading boundary");
        bool rejected = false;
        for (int frame = 0; frame < 30 && !rejected; frame++)
        {
            player.Position += Vector2.Left * player.SlowestMoveSpeed * (float)Step;
            attack.Tick(Step);
            rejected = attack.CoversPoint(player.Position);
        }
        Require(rejected, label + ": crossing against reading order produces an actual hit region");
        attack.Cancel();
        Pass(label + ": directional reading gate distinguishes real forward and reverse crossings");
    }

    private void CheckQuotationLayers(Enemy boss, Player player, Spec spec, string label)
    {
        player.Position = new Vector2(global::Field.Left + 60, 8);
        var attack = Begin(boss, spec);
        double warning = Read<double>(attack, "_warning");
        double duration = 8.8 - (int)_game.Difficulty * .4;
        while (attack.Elapsed < warning + duration * .4) attack.Tick(Step);
        Rect2[] sheets = Read<Rect2[]>(attack, "_quotationSheets");
        float backTextY = sheets[0].GetCenter().Y + 17;
        Require(attack.CoversPoint(new Vector2(sheets[0].GetCenter().X - 26, backTextY))
            && !attack.CoversPoint(new Vector2((sheets[0].GetCenter().X + sheets[1].GetCenter().X) * .5f, backTextY))
            && attack.CoversPoint(sheets[1].GetCenter() + Vector2.Up * 17),
            label + ": an overlapping paper clips only the covered portion of the other paper's text");
        double middle = warning + duration * .5;
        while (attack.Elapsed < middle) attack.Tick(Step);
        Rect2 front = Read<Rect2[]>(attack, "_quotationSheets")[1];
        var coveredLine = new Vector2(front.GetCenter().X, 108);
        Require(front.HasPoint(coveredLine) && !attack.CoversPoint(coveredLine),
            label + ": opaque foreground paper removes the covered background collision");
        Require(attack.CoversPoint(new Vector2(global::Field.Left + 22, 108)),
            label + ": uncovered background text remains dangerous");
        Require(new[] { -17, 17 }.All(offset => attack.CoversPoint(front.GetCenter() + Vector2.Down * offset)),
            label + ": the foreground paper's own text retains its collision");
        attack.Cancel();
        Pass(label + ": opaque quotation layers clip only the text behind them");
    }

    private void CheckMemoryExchange(Enemy boss, Player player, Spec spec, string label)
    {
        player.Position = new Vector2(global::Field.CenterX, 198);
        var attack = Begin(boss, spec);
        ulong id = attack.GetInstanceId();
        for (int frame = 0; frame < 600 && !Read<bool>(attack, "_exchangePending"); frame++)
        {
            attack.Tick(Step);
            if (!Read<bool>(attack, "_exchangePending"))
                foreach (var shot in Shots(id)) shot._PhysicsProcess(Step);
        }
        Require(Read<bool>(attack, "_exchangePending"), label + ": moving memory reaches a snapshot exchange");
        var memories = ((System.Collections.IEnumerable)FieldOf(attack, "_memories").GetValue(attack)!).Cast<object>()
            .Select(memory => (Memory: memory, Bullet: Read<Bullet>(memory, "Bullet"), Origin: Read<Vector2>(memory, "Origin"),
                Direction: Read<Vector2>(memory, "Direction"), Index: Read<int>(memory, "Index"))).ToArray();
        int count = 3 + (int)_game.Difficulty;
        Require(memories.Length == count * 2 && memories.All(m => m.Bullet.GlobalPosition.DistanceTo(m.Origin) < .001f),
            label + ": snapshot captures the actual positions in both moving regions");
        bool movedAfterSnapshot = false;
        foreach (var shot in Shots(id)) shot._PhysicsProcess(Step);
        for (int frame = 0; frame < 300 && Read<bool>(attack, "_exchangePending"); frame++)
        {
            attack.Tick(Step);
            bool pending = Read<bool>(attack, "_exchangePending");
            foreach (var memory in memories)
            {
                Require(Read<Vector2>(memory.Memory, "Origin") == memory.Origin && Read<Vector2>(memory.Memory, "Direction") == memory.Direction,
                    label + ": warned destination and direction stay fixed after the snapshot");
                Require(memory.Bullet.Active && !memory.Bullet.LeadIn && memory.Bullet.CollisionLayer == 8 && memory.Bullet.CollisionMask == 1,
                    label + ": both moving memory groups retain their real projectile collision");
                if (pending) movedAfterSnapshot |= memory.Bullet.GlobalPosition.DistanceTo(memory.Origin) > 2;
                else
                {
                    int side = memory.Index / count;
                    Vector2 destination = memory.Origin + Vector2.Right * (side == 0 ? 174 : -174);
                    Require(memory.Bullet.GlobalPosition.DistanceTo(destination) < .001f && memory.Bullet.Velocity.DistanceTo(memory.Direction) < .001f,
                        label + ": exchange commits the saved position and velocity in the opposite region");
                    Require(Read<int>(memory.Memory, "Index") == (1 - side) * count + memory.Index % count,
                        label + ": exchanged memory belongs to its destination region");
                }
            }
            if (pending) foreach (var shot in Shots(id)) shot._PhysicsProcess(Step);
        }
        Require(!Read<bool>(attack, "_exchangePending") && Read<int>(attack, "_exchanges") == 1 && movedAfterSnapshot,
            label + ": moving groups exchange a frozen snapshot rather than following its warning");
        attack.Cancel();
        Require(Shots(id).Length == 0, label + ": snapshot test clears its projectiles");
        Pass(label + ": live moving memory preserves and exchanges its warned snapshot");
    }

    private void ObserveBullet(Features features, Bullet bullet)
    {
        var key = (bullet.GetInstanceId(), bullet.ActivationId);
        if (features.Seen.Add(key)) features.NewShots++;
        if (bullet.LeadIn || bullet.CollisionLayer == 0) return;
        Vector2 velocity = features.Positions.TryGetValue(key, out Vector2 lastPosition)
            ? (bullet.GlobalPosition - lastPosition) / (float)Step : bullet.Velocity;
        features.Positions[key] = bullet.GlobalPosition;
        float speed = velocity.Length();
        features.MaximumSpeed = Math.Max(features.MaximumSpeed, speed);
        if (speed < .01f) features.StationaryFrames++;
        if (features.Velocities.TryGetValue(key, out Vector2 previous))
        {
            if (Math.Abs(previous.Length() - speed) > .1f) features.AccelerationChanges++;
            if (speed > .1f && previous.Length() > .1f)
            {
                float turn = Mathf.Abs(Mathf.AngleDifference(previous.Angle(), velocity.Angle()));
                if (turn > .005f) features.TurnChanges++;
                features.MaximumTurn = Math.Max(features.MaximumTurn, turn);
            }
        }
        features.Velocities[key] = velocity;
    }
    private void CheckQuotePeeling(Enemy boss, Player player, Spec spec, string label)
    {
        Vector2 savedPosition = player.Position;
        int facing = Read<int>(player, "_facing");
        foreach (bool charged in new[] { false, true })
        {
            player.Position = new Vector2(global::Field.Left + 60, 54);
            Write(player, "_facing", 1);
            var attack = Begin(boss, spec);
            bool roundShapeSeen = false;
            var pages = Read<List<(Rect2 Rect, int Hits)>>(attack, "_pages");
            int frame = 0;
            for (; frame < 180 && pages[0].Hits < 3; frame++)
            {
                if (frame % 24 == 0)
                {
                    if (charged) Call(player, "FireCharge", ChargeTier.First);
                    else Call(player, "Fire");
                }
                foreach (var friendly in GetTree().GetNodesInGroup("player_bullets").OfType<Bullet>().Where(b => b.Active))
                {
                    roundShapeSeen |= friendly.Footprint == null;
                    friendly.CollisionLayer = friendly.CollisionMask = 0;
                    friendly._PhysicsProcess(Step);
                }
                attack.Tick(Step);
            }
            Require(roundShapeSeen, label + ": actual player fire exercises the circular footprint path");
            Require(pages[0].Hits >= 3 && attack.Elapsed < (double)Call(attack, "get_PageSweepEnd")!,
                label + ": " + (charged ? "charged" : "ordinary") + " player shots peel a page before its natural fade");
            Pass(label + ": " + (charged ? "charged" : "ordinary") + $" player shots peel in {frame * Step:0.00}s");
            attack.Cancel();
            Pool.DespawnPlayerBullets();
        }
        player.Position = savedPosition;
        Write(player, "_facing", facing);
    }
    private bool Hits(Bullet bullet, Vector2 point)
    {
        if (!bullet.Active || bullet.LeadIn || bullet.CollisionLayer == 0) return false;
        float radius = bullet.CollisionRadius + Player.HitRadius;
        if (bullet.GlobalPosition.DistanceSquaredTo(point) > radius * radius) return false;
        return bullet.Footprint!.Shapes.Any(shape => shape.Collide(bullet.GlobalTransform, _core, new Transform2D(0, point)));
    }
    private bool BodyBlocks(Enemy boss, Vector2 from, Vector2 to)
    {
        var body = Read<CollisionShape2D>(boss, "_bodyShape");
        if (body.Disabled) return false;
        using var margin = new CircleShape2D { Radius = Player.HitRadius + 3 };
        int segments = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to)));
        for (int i = 0; i <= segments; i++)
            if (body.Shape.Collide(body.GlobalTransform, margin, new Transform2D(0, from.Lerp(to, i / (float)segments)))) return true;
        return false;
    }

    private Vector2 WalkingProbeStart(Vector2 requested, Enemy boss)
    {
        var body = Read<CollisionShape2D>(boss, "_bodyShape");
        if (body.Disabled || !body.Shape.Collide(body.GlobalTransform, _core, new Transform2D(0, requested))) return requested;
        using var margin = new CircleShape2D { Radius = Player.HitRadius + 3 };
        Vector2 nearest = requested;
        float best = float.MaxValue;
        for (float x = global::Field.Left + 8; x <= global::Field.Right - 8; x++)
        for (float y = 8; y <= global::Field.Bottom - 8; y++)
        {
            Vector2 candidate = new(x, y);
            float distance = requested.DistanceSquaredTo(candidate);
            if (distance >= best || body.Shape.Collide(body.GlobalTransform, margin, new Transform2D(0, candidate))) continue;
            nearest = candidate;
            best = distance;
        }
        Require(best < float.MaxValue, "a walking probe starts outside the boss body");
        return nearest;
    }

    private Vector2 DodgeWalkGoal(Player player, Enemy boss, BossCharacterTechnique attack)
    {
        int tier = (int)_game.Difficulty;
        Vector2 start = player.Position;
        var body = Read<CollisionShape2D>(boss, "_bodyShape");
        Require(body.Disabled || !body.Shape.Collide(body.GlobalTransform, _core, new Transform2D(0, start)),
            attack.SpellName + ": walking probe does not begin in contact with the boss");
        float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
            => Geometry2D.GetClosestPointToSegment(point, a, b).DistanceTo(point);
        bool Safe(Vector2 point)
        {
            if (attack is AkariBossTechnique)
            {
                float center = Mathf.Clamp(start.X, global::Field.Left + 70, global::Field.Right - 70);
                float reach = Math.Max(58, (30 + tier * 4) * 4.3f - 58);
                return Math.Abs(point.X - center) > reach + 2.5f + tier * .35f + Player.HitRadius + 3;
            }
            if (attack is KoharuBossTechnique)
            {
                Vector2 center = start.Clamp(new Vector2(global::Field.Left + 60, 60), new Vector2(global::Field.Right - 60, global::Field.Bottom - 60));
                return point.DistanceTo(center) > 90 + 48 * .32f + 3 + tier * .5f + Player.HitRadius + 3;
            }
            if (attack is ReiBossTechnique)
            {
                Vector2 center = start.Clamp(new Vector2(global::Field.Left + 65, 65), new Vector2(global::Field.Right - 65, global::Field.Bottom - 65));
                float sign = start.X < global::Field.CenterX ? -1 : 1;
                float x = center.X + sign * 15;
                float half = (1 + tier) * 1.7f + 2.5f + Player.HitRadius + 3;
                if (SegmentDistance(point, new Vector2(x, center.Y - 56), new Vector2(x, center.Y + 56)) <= half) return false;
                for (int edge = -1; edge <= 1; edge += 2)
                for (int step = 0; step <= 11; step++)
                {
                    float open = step * 3.4f;
                    if (SegmentDistance(point, new Vector2(x, center.Y + edge * (20 + open)),
                        new Vector2(x - sign * 54, center.Y + edge * (56 + open))) <= 4 + Player.HitRadius + 5) return false;
                }
                return true;
            }
            throw new InvalidOperationException("This dodge technique requires a telegraph-driven walking probe.");
        }
        Vector2 goal = start;
        float best = float.MaxValue;
        for (float x = global::Field.Left + 8; x <= global::Field.Right - 8; x += 2)
        for (float y = 8; y <= global::Field.Bottom - 8; y += 2)
        {
            Vector2 candidate = new(x, y);
            float distance = start.DistanceTo(candidate);
            if (distance >= best || !Safe(candidate)) continue;
            bool blocked = false;
            int segments = Math.Max(1, (int)Math.Ceiling(distance / 3));
            if (!body.Disabled)
                for (int i = 0; i <= segments; i++)
                    if (body.Shape.Collide(body.GlobalTransform, _core, new Transform2D(0, start.Lerp(candidate, i / (float)segments))))
                    { blocked = true; break; }
            if (blocked) continue;
            goal = candidate;
            best = distance;
        }
        Require(best < float.MaxValue, attack.SpellName + ": a walkable destination exists inside the field");
        return goal;
    }

    private Vector2 TelegraphWalkGoal(Player player, Enemy boss, BossCharacterTechnique attack, bool verticalOnly = false)
    {
        var body = Read<CollisionShape2D>(boss, "_bodyShape");
        Vector2 start = player.Position, goal = start;
        float best = float.MaxValue;
        float firstX = verticalOnly ? start.X : global::Field.Left + 8;
        float lastX = verticalOnly ? start.X : global::Field.Right - 8;
        for (float x = firstX; x <= lastX; x += 2)
        for (float y = 8; y <= global::Field.Bottom - 8; y += 2)
        {
            Vector2 candidate = new(x, y);
            float distance = start.DistanceTo(candidate);
            if (distance >= best || attack.DangerDistance(candidate, true) < 4) continue;
            bool blocked = false;
            int segments = Math.Max(1, (int)Math.Ceiling(distance / 2));
            if (!body.Disabled)
                for (int i = 0; i <= segments; i++)
                    if (body.Shape.Collide(body.GlobalTransform, _core, new Transform2D(0, start.Lerp(candidate, i / (float)segments))))
                    { blocked = true; break; }
            if (blocked) continue;
            goal = candidate;
            best = distance;
        }
        Require(best < float.MaxValue, attack.SpellName + ": the visible telegraph has a walkable destination outside the boss body");
        return goal;
    }

    private Vector2 ChooseMovement(Player player, Enemy boss, BossCharacterTechnique attack, Bullet[] shots, Features features, Vector2 previous)
    {
        Vector2 best = Vector2.Zero;
        float bestScore = float.NegativeInfinity;
        for (int candidate = -1; candidate < 16; candidate++)
        {
            Vector2 direction = candidate < 0 ? Vector2.Zero : Vector2.FromAngle(candidate * Mathf.Tau / 16);
            float score = direction == Vector2.Zero ? 1.1f : previous.Dot(direction) * .3f;
            foreach (float horizon in new[] { .1f, .3f, .6f })
            {
                Vector2 position = player.Position + direction * player.SlowestMoveSpeed * horizon;
                if (!global::Field.Rect.Grow(-8).HasPoint(position)) { score -= 500; continue; }
                float clearance = attack.DangerDistance(position, false);
                foreach (var bullet in shots)
                {
                    if (bullet.LeadIn || bullet.CollisionLayer == 0) continue;
                    Vector2 velocity = features.Velocities.GetValueOrDefault((bullet.GetInstanceId(), bullet.ActivationId), bullet.Velocity);
                    Vector2 predicted = bullet.GlobalPosition + velocity * horizon;
                    clearance = Math.Min(clearance, predicted.DistanceTo(position) - bullet.CollisionRadius - Player.HitRadius - 2);
                }
                var body = Read<CollisionShape2D>(boss, "_bodyShape");
                if (!body.Disabled && body.Shape.Collide(body.GlobalTransform, _core, new Transform2D(0, position))) clearance = -20;
                score += Math.Min(20, clearance) * (clearance < 0 ? 20 : 1);
                float warning = attack.DangerDistance(position, true);
                if (warning < 6) score -= (6 - warning) * .35f;
            }
            if (score > bestScore) { bestScore = score; best = direction; }
        }
        return best;
    }

    private void CheckPause(BossCharacterTechnique attack, string label)
    {
        var bullets = Shots(attack.GetInstanceId()).Select(b => (Bullet: b, Id: b.ActivationId, Position: b.Position, Velocity: b.Velocity)).ToArray();
        double elapsed = attack.Elapsed;
        int seeds = attack.ShotsCreated, geometry = attack.HazardFrames;
        Hud.BubblePaused = true;
        attack.Tick(1);
        foreach (var item in bullets) item.Bullet._PhysicsProcess(1);
        Hud.BubblePaused = false;
        Require(attack.Elapsed == elapsed && attack.ShotsCreated == seeds && attack.HazardFrames == geometry, label + ": dialogue pauses attack state");
        Require(bullets.All(item => item.Bullet.Active && item.Bullet.ActivationId == item.Id && item.Bullet.Position == item.Position && item.Bullet.Velocity == item.Velocity), label + ": dialogue pauses live projectiles");
        GetTree().Paused = true;
        attack.Tick(1);
        GetTree().Paused = false;
        Require(attack.Elapsed == elapsed, label + ": menu pause freezes attack state");
    }
    private void CheckCancel(Enemy boss, Player player, Spec spec, Vector2 start, string label)
    {
        foreach (bool active in new[] { false, true })
        {
            player.Position = start;
            var attack = Begin(boss, spec);
            ulong id = attack.GetInstanceId();
            if (active)
            {
                for (int frame = 0; frame < 300 && !attack.Finished && attack.ShotsCreated == 0 && attack.HazardFrames == 0; frame++)
                {
                    attack.Tick(Step);
                    foreach (var bullet in Shots(id)) bullet._PhysicsProcess(Step);
                }
                if (!attack.Finished)
                    for (int frame = 0; frame < 30 && !attack.Finished; frame++)
                    {
                        attack.Tick(Step);
                        foreach (var bullet in Shots(id)) bullet._PhysicsProcess(Step);
                    }
            }
            var foreign = Pool.SpawnBossShot(boss.ShotCenter, Vector2.Down, boss.BodyShotJob!.Value, false);
            foreign.BossBarrageId = ulong.MaxValue;
            attack.Cancel();
            int seeds = attack.ShotsCreated;
            attack.Tick(10);
            Require(attack.Finished && !attack.IsStriking && Shots(id).Length == 0 && attack.ShotsCreated == seeds, label + ": cancellation clears owned bullets/hazards and suppresses future work");
            Require(foreign.Active, label + ": cancellation preserves unrelated projectiles");
            Pool.Despawn(foreign);
        }
    }
    private void CheckInterruption(Enemy boss, Player player, Spec spec, Vector2 start, string label)
    {
        foreach (string reason in new[] { "Break", "Reclose", "Form", "Hidden" })
        {
            player.Position = start;
            var attack = Begin(boss, spec);
            for (int frame = 0; frame < 120 && !attack.Finished && attack.ShotsCreated == 0 && attack.HazardFrames == 0; frame++)
            {
                attack.Tick(Step);
                foreach (var bullet in Shots(attack.GetInstanceId())) bullet._PhysicsProcess(Step);
            }
            Require(!attack.Finished, label + ": interruption reaches a live attack");
            var phaseField = FieldOf(boss, "_phase");
            object phase = phaseField.GetValue(boss)!;
            int minaPhase = boss is BossMina ? Read<int>(boss, "_pattern") : 0;
            bool form = Read<bool>(boss, "_form2");
            if (reason is "Break" or "Reclose") phaseField.SetValue(boss, Enum.Parse(phaseField.FieldType, reason));
            if (reason == "Form")
            {
                if (boss is BossMina) Write(boss, "_pattern", spec.Evolved ? 0 : BossMina.DragonPhase);
                else Write(boss, "_form2", !form);
            }
            if (reason == "Hidden") boss.Hide();
            attack.Tick(Step);
            Require(attack.Finished && !attack.IsStriking && Shots(attack.GetInstanceId()).Length == 0, label + ": " + reason + " interruption clears the technique");
            phaseField.SetValue(boss, phase);
            if (boss is BossMina) Write(boss, "_pattern", minaPhase);
            Write(boss, "_form2", form);
            boss.Show();
        }
    }
    private async Task Capture(Spec spec, Hud hud, BossCharacterTechnique attack, string moment)
    {
        Write(hud, "_spellTimer", moment == "warning" ? 2d : 0d);
        attack.QueueRedraw();
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string folder = ProjectSettings.GlobalizePath(Option("--output") ?? "res://build/qa_story/character_techniques_20261015/art");
        DirAccess.MakeDirRecursiveAbsolute(folder);
        using var image = GetViewport().GetTexture().GetImage();
        string filename = $"{folder}/{spec.Character}_{spec.Kind}_{spec.Variant}_{(spec.Evolved ? "after" : "before")}_{_game.Difficulty}_{moment}.png";
        Require(image.SavePng(filename) == Error.Ok, "screenshot saved: " + filename);
        _screenshots++;
    }
}
