using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class CombatRevisionQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameManager _game = null!;
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private static FieldInfo Field(object value, string name)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Private | BindingFlags.DeclaredOnly) is { } field) return field;
        throw new MissingFieldException(name);
    }
    private static T Read<T>(object value, string name) => (T)Field(value, name).GetValue(value)!;
    private static void Write(object value, string name, object data) => Field(value, name).SetValue(value, data);
    private static object? Call(object value, string name, params object[] args)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetMethod(name, Private | BindingFlags.DeclaredOnly) is { } method) return method.Invoke(value, args);
        throw new MissingMethodException(name);
    }
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        GD.Print("[CombatQA] PASS " + label);
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private Bullet[] Hostile() => GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Where(b => b.Active).ToArray();
    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            _game = GetNode<GameManager>("/root/Game");
            _game.AutoSaveEnabled = false;
            _game.ResetPersistent();
            GetTree().AutoAcceptQuit = true;
            if (OS.GetCmdlineUserArgs().Contains("--live-clear"))
            {
                await Frames(2);
                BeginLiveClear();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--akari-acceleration"))
            {
                CheckAkariAcceleration();
                GD.Print("[CombatQA] ALL PASS");
                GetTree().Quit();
                return;
            }
            _game.Difficulty = OS.GetCmdlineUserArgs().Contains("--rhythm-video") ? GameManager.Diff.Lunatic
                : OS.GetCmdlineUserArgs().Contains("--crossfire-video") ? GameManager.Diff.Hard : GameManager.Diff.Normal;
            _game.MarkIdleDialogSeen("once_midboss_shield");
            await Frames(2);
            int previousHp = 0, previousInk = 0;
            string[] scenes = { "Akari", "Koharu", "Rei", "MinaBattle" };
            string? bossFilter = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--boss="));
            Check(bossFilter == null || scenes.Any(scene => bossFilter == "--boss=" + scene), "valid boss test selection");
            foreach (string scene in scenes)
            {
                if (bossFilter != null && bossFilter != "--boss=" + scene) continue;
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
                hud.HideBubble();
                root.GetNode<StageBackground>("StageBackground").EnterBoss();
                Enemy boss = scene switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(), "Rei" => new BossRei(), _ => new BossMina() };
                boss.ProcessMode = ProcessModeEnum.Disabled;
                world.AddChild(boss);
                boss.Position = new Vector2(global::Field.BossCenterX, 100);
                Call(boss, "TickEntrance", 0d);
                Call(boss, "TickEntrance", 100d);
                Call(boss, "TickSwapAnim", 1d);
                await Frames(3);
                hud.HideBubble();
                if (OS.GetCmdlineUserArgs().Contains("--extra-rotation"))
                {
                    await CheckExtraRotation(boss, player, hud, scene);
                    await CheckCasterTreeExit(root, boss, scene);
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--density-snapshot") || OS.GetCmdlineUserArgs().Contains("--density-extras"))
                {
                    if (!OS.GetCmdlineUserArgs().Contains("--density-extras")) await MeasureDensitySnapshot(boss, player, hud, scene);
                    await CheckSupplementalDensity(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--fair-crossfire"))
                {
                    CheckFairCrossfire(boss, player, scene);
                    await CheckFairCrossfireRoutes(boss, player, scene);
                    CheckCrossfireBreakRecovery(boss, player, hud, scene);
                    await CheckDodgeWave(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--crossfire-break"))
                {
                    CheckCrossfireBreakRecovery(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--debug-guide-qa"))
                {
                    await CheckDebugGuides(boss, player, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--difficulty-areas"))
                {
                    CheckAreaAnswers(boss, player, scene);
                    CheckAreaSequences(boss, player, scene);
                    CheckLearnedFullscreen(boss, player, scene);
                    await CheckEdgeLessons(boss, player, hud, scene);
                    await CheckSignatureLessons(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--barrage-handoffs"))
                {
                    await CheckMovingHandoffs(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--readable-normal") || OS.GetCmdlineUserArgs().Contains("--difficulty-lessons")
                    || OS.GetCmdlineUserArgs().Contains("--pressure") && OS.GetCmdlineUserArgs().Contains("--normal-routes"))
                {
                    await CheckReadableNormal(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--pressure"))
                {
                    if (OS.GetCmdlineUserArgs().Contains("--rehearsal")) CheckAnswerTiming(boss, player, scene);
                    if (OS.GetCmdlineUserArgs().Contains("--naive-routes")) await CheckNaiveRoutes(boss, player, hud, scene);
                    else await CheckPressure(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--answers"))
                {
                    CheckAnswerTiming(boss, player, scene);
                    CheckAreaAnswers(boss, player, scene);
                    await CheckConnectedAnswers(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--dodge-video"))
                {
                    await CaptureDodgeVideo(boss, player, hud, root, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--dodge-wave"))
                {
                    await CheckDodgeWave(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--dodge-handoff"))
                {
                    await CheckDodgeHandoff(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--challenge"))
                {
                    await CheckChallenge(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--telegraph"))
                {
                    await CheckTelegraphCommitment(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--rhythm-video"))
                {
                    await CaptureRhythmVideo(boss, player, hud, root, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--rhythm-shots"))
                {
                    await SaveRhythmFrames(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--rhythm"))
                {
                    await CheckAttackRhythm(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--crossfire-density"))
                {
                    MeasureCrossfire(boss, player, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--crossfire-video"))
                {
                    await CaptureCrossfire(boss, player, hud, root, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--crossfire"))
                {
                    await CheckCrossfire(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                if (OS.GetCmdlineUserArgs().Contains("--danmaku"))
                {
                    await CheckDanmakuDensity(boss, player, hud, scene);
                    root.QueueFree();
                    Pool.DespawnAll();
                    await Frames(5);
                    continue;
                }
                int hp = boss.TotalBars * Enemy.BarHp, ink = Read<int>(boss, "PanelInk");
                Check(hp > previousHp && ink > previousInk, scene + " health and shield strength rise with stage order");
                previousHp = hp; previousInk = ink;
                var mover = Read<BossMover>(boss, "_mover");
                var point = boss.Position;
                Vector2 min = point, max = point;
                for (int i = 0; i < 2400; i++)
                {
                    point = mover.Step(point, 1d / 60);
                    min = min.Min(point); max = max.Max(point);
                    CheckInField(point);
                }
                Check(max.X - min.X > 150 && max.Y - min.Y > 100, scene + " route covers both axes of the arena");
                Pool.DespawnAll();
                var danmaku = Read<BossDanmaku>(boss, "_danmaku");
                for (int i = 0; i < 600; i++) danmaku.Tick(1d / 60);
                int shots = Hostile().Length;
                Check(shots >= (boss is BossRei ? 15 : 45) && Hostile().All(b => (b.CollisionMask == 1 || b.LeadIn)
                    && ReferenceEquals(Read<BulletArt.PlayerVisual>(b, "_playerVisual"), BulletArt.PlayerShot(boss.BodyShotJob!.Value))),
                    scene + " sustained non-destructible character danmaku");
                Pool.DespawnAll();
                var sentinel = Pool.Spawn(new Vector2(350, 190), Vector2.Zero, true, 3);
                sentinel.SetPhysicsProcess(false);
                foreach (var panel in Read<List<Panel>>(boss, "_panels").ToArray()) panel.Shatter();
                Check(boss.GaugeVulnerable && sentinel.Active, scene + " shield break preserves fired bullets");
                int before = Hostile().Length;
                for (int i = 0; i < 120; i++) danmaku.Tick(1d / 60);
                Check(Hostile().Length == before, scene + " break preserves fired bullets without adding a new barrage");
                Call(boss, "EnterExposed");
                double phaseTime = Read<double>(boss, "_phaseT");
                Hud.BubblePaused = true;
                boss._PhysicsProcess(1);
                Check(Read<double>(boss, "_phaseT") == phaseTime, "dialogue cannot consume the vulnerable window");
                Hud.BubblePaused = false;
                Check(hud.Bubbles!.ShieldBreak.Active, scene + " break callout immediately active");
                Call(boss, "EnterReclose");
                hud.HideBubble();
                Call(boss, "EnterShielded");
                Pool.DespawnAll();
                if (scene != "MinaBattle")
                {
                    var caster = Read<AreaSpellCaster>(boss, "_caster");
                    Write(caster, "_pendShape", (AreaStrike.Shape?)AreaStrike.Shape.Circle);
                    Write(caster, "_castTarget", player.GlobalPosition);
                    Call(caster, "SpawnTelegraphs");
                    var area = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().First();
                    float radius = Read<float>(area, "_hw");
                    Check(area.CoversPoint(player.GlobalPosition) && radius >= 34, scene + " broad AOE covers the player");
                    Check(Read<double>(area, "_warn") >= (radius + 6) / player.SlowestMoveSpeed + 0.49,
                        scene + " AOE warning allows escape at the new lock speed");
                    foreach (var strike in GetTree().GetNodesInGroup("aoe")) strike.QueueFree();
                    Write(caster, "_castT", 2d);
                    Call(boss, "EnterBreak");
                    caster._Process(0.1);
                    Check(Read<double>(caster, "_castT") == 2d, scene + " shield break pauses rather than resets attack timers");
                    Call(boss, "EnterReclose");
                    hud.HideBubble();
                    Call(boss, "EnterShielded");
                }
                if (scene == "Akari") await CheckPlayerAndVolleys(world, player, boss);
                if (OS.GetCmdlineUserArgs().Contains("--shots"))
                {
                    Call(boss, "EnterReclose");
                    Call(boss, "EnterShielded");
                    hud.HideBubble();
                    hud.HideSpellCard();
                    Write(hud, "_bossLineTimer", 0d);
                    Engine.TimeScale = 1;
                    await Frames(240);
                    Pool.DespawnAll();
                    for (int frame = 0; frame < 180; frame++)
                    {
                        danmaku.Tick(1d / 60);
                        foreach (var shot in Hostile()) { shot.SetPhysicsProcess(false); shot._PhysicsProcess(1d / 60); }
                    }
                    hud.HideBubble();
                    await Frames(5);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    string folder = ProjectSettings.GlobalizePath("res://build/qa_story/combat_revision");
                    DirAccess.MakeDirRecursiveAbsolute(folder);
                    using var image = GetViewport().GetTexture().GetImage();
                    Check(image.SavePng(folder + "/" + scene + ".png") == Error.Ok, "battle screenshot saved");
                    hud.HoldBubble = true;
                    hud.ShowDialog(Hud.LineKind.Mina, "急がなくて大丈夫です。\nご主人様、一緒に進んでいきましょう。", "res://char/mina_face.png");
                    _game.MsgCharsPerSec = 300;
                    await Frames(240);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var dialogue = GetViewport().GetTexture().GetImage();
                    dialogue.SavePng(folder + "/" + scene + "_text.png");
                    hud.HoldBubble = false;
                    hud.HideBubble();
                }
                Pool.DespawnAll();
                if (boss is BossMina) Write(boss, "_pattern", BossMina.DragonPhase);
                else Write(boss, "_form2", true);
                Call(boss, "EnterShielded");
                hud.HideBubble();
                await Frames(2);
                var evolved = new BossDanmaku(boss);
                evolved.Tick(1);
                var evolvedOpening = Read<BossNormalBarrage>(evolved, "_normalBarrage");
                evolved.Tick(evolvedOpening.Warning + 0.001);
                var charged = Hostile();
                var character = Jobs.Get(boss.BodyShotJob!.Value);
                Check(charged.Length > 0 && charged.All(b => b.Charged && b.ChargeJob == character.Id
                    && b.Radius == BulletPool.BossChargeRadius * (character.Id == Job.Magic ? 1.5f : 1)
                    && b.Accel == (character.Mode == GameManager.ShotMode.Accel)
                    && b.Homing == (character.Mode == GameManager.ShotMode.Homing)), scene + " evolved barrage uses character charge properties");
                foreach (var bullet in charged) bullet.SetPhysicsProcess(false);
                if (OS.GetCmdlineUserArgs().Contains("--charge-shots"))
                {
                    hud.HideBubble();
                    hud.HideSpellCard();
                    await Frames(4);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    string folder = ProjectSettings.GlobalizePath("res://build/qa_story/combat_revision");
                    DirAccess.MakeDirRecursiveAbsolute(folder);
                    using var screenshot = GetViewport().GetTexture().GetImage();
                    screenshot.SavePng(folder + "/" + scene + "_charged.png");
                }
                Call(boss, "EnterBreak");
                Check(charged.All(b => b.Active), "existing waves survive shield break");
                Check(GetTree().GetNodesInGroup("corridor").Count == 0, "no removed corridor attack is spawned");
                if (boss is BossRei or BossMina)
                {
                    Call(boss, "EnterReclose");
                    var posts = Read<BossPostSequence>(boss, "_posts");
                    typeof(BossPostSequence).GetProperty("Count")!.SetValue(posts, 4);
                    Write(boss, "_hp", boss.TotalBars * Enemy.BarHp / 100);
                    Write(boss, "_memoryPlayed", true);
                    if (boss is BossRei) Write(boss, "_relayWatching", true);
                    else
                    {
                        if (Read<CharacterStoryTalk?>(boss, "_recloseTalk") is { Active: true } talk)
                            typeof(CharacterStoryTalk).GetMethod("Finish", Private)!.Invoke(talk, new object[] { hud });
                        Write(boss, "_pattern", 4);
                        var caster = Read<MinaPhaseAttacks>(boss, "_caster");
                        typeof(MinaPhaseAttacks).GetProperty("Active")!.SetValue(caster, true);
                    }
                    hud.HideBubble();
                    boss._Process(0.01);
                    var post = GetTree().GetFirstNodeInGroup("boss_post") as BossPost;
                    Check(post is { IsBroken: true } && !posts.Active,
                        scene + " final post shatters without suspending combat");
                    post!._PhysicsProcess(BossPost.BreakDuration + .01);
                    Check(posts.Active, scene + " pending story gate does not waste a break waiting for paused attacks");
                }
                root.QueueFree(); Pool.DespawnAll();
                foreach (string group in new[] { "aoe", "boss_edge_volleys", "boss_animal_techniques" })
                    foreach (var node in GetTree().GetNodesInGroup(group)) node.QueueFree();
                await Frames(5);
            }
            Check(UiKit.Zen.MultichannelSignedDistanceField && UiKit.Zen.MsdfSize >= 96, "dialogue uses smooth scalable Japanese fonts");
            var audio = GetNode<Audio>("/root/Audio");
            audio.StartPostMusic("mina", 0, 0);
            Check(Read<AudioEffectLowPassFilter>(audio, "_postFilter").CutoffHz >= 15000, "boss music opens with full rhythm and treble");
            audio.StopMusic(0);
            foreach (var stream in audio.GetChildren().OfType<AudioStreamPlayer>())
            { stream.Stop(); stream.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[CombatQA] ALL PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("[CombatQA] FAIL " + error); GetTree().Quit(1); }
    }
    private static void CheckInField(Vector2 point)
    {
        if (!global::Field.Rect.HasPoint(point)) throw new Exception("boss route left the field: " + point);
    }

    private async Task MeasureDensitySnapshot(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.SelectedJob = Job.Tank;
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        foreach (bool charged in new[] { false, true })
        {
            _game.Difficulty = difficulty;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            int patterns = difficulty == GameManager.Diff.Easy ? 3 : BossNormalBarrage.PatternCount(boss);
            for (int pattern = 0; pattern < patterns; pattern++)
            {
                Reset();
                var attack = BossNormalBarrage.Begin(boss, pattern);
                hud.HideBubble();
                double warning = attack.Warning, firstShot = Read<double>(attack, "_clock");
                float passage = (float)typeof(BossNormalBarrage).GetProperty("PassageRadius", Private)!.GetValue(attack)!;
                var planned = new List<int>();
                var emitted = new List<int>();
                var seen = new HashSet<(ulong, ulong)>();
                float maximumSpeed = 0;
                int frame = 0;
                for (; frame < 1800 && attack.Wave < 3 && !attack.Finished; frame++)
                {
                    int wave = attack.Wave, count = attack.Volley().Count(), before = Hostile().Length;
                    attack.Tick(1d / 60);
                    if (attack.Wave != wave)
                    {
                        planned.Add(count);
                        emitted.Add(Hostile().Length - before);
                    }
                    Observe(seen, ref maximumSpeed);
                    StepBullets();
                    Observe(seen, ref maximumSpeed);
                }
                Check(attack.Wave == 3 && planned.SequenceEqual(emitted), $"{scene}/{difficulty}/{charged}/{pattern}: three measured volleys match spawned shots");
                GD.Print($"DENSITY {scene}/{difficulty}/{charged}/normal-{pattern} planned={string.Join(',', planned)} emitted={string.Join(',', emitted)} spawned={seen.Count} elapsed={frame / 60f:F3} warning={warning:F3} firstShot={firstShot:F3} passage={passage:F3} pitch={Read<float>(attack, "_pitch"):F3} laneSpeed={Read<float>(attack, "_laneSpeed"):F3} maxSpeed={maximumSpeed:F3} {Stats()}");
                attack.Cancel();
                await Frames(1);
            }
            if (difficulty != GameManager.Diff.Lunatic) continue;
            for (int mode = 0; mode < 6; mode++)
            {
                Reset();
                var attack = BossCrossfire.Begin(boss, sweep: mode is 2 or 3, horizontal: mode != 3,
                    weave: mode == 1, pincer: mode >= 4, diagonal: mode == 5);
                hud.HideBubble();
                double warning = attack.WarningRemaining;
                var planned = new List<int>();
                var emitted = new List<int>();
                var seen = new HashSet<(ulong, ulong)>();
                float maximumSpeed = 0;
                int frame = 0;
                for (; frame < 1800 && attack.Wave < 3 && !attack.Finished; frame++)
                {
                    int wave = attack.Wave, count = Enumerable.Range(0, attack.Origins.Count).Sum(source => attack.Volley(source).Count());
                    int before = Hostile().Length;
                    attack.Tick(1d / 60);
                    if (attack.Wave != wave)
                    {
                        planned.Add(count);
                        emitted.Add(Hostile().Length - before);
                    }
                    Observe(seen, ref maximumSpeed);
                    StepBullets();
                    Observe(seen, ref maximumSpeed);
                }
                Check(attack.Wave == 3 && planned.SequenceEqual(emitted), $"{scene}/{difficulty}/{charged}/crossfire-{mode}: three measured volleys match spawned shots");
                GD.Print($"DENSITY {scene}/{difficulty}/{charged}/crossfire-{mode} planned={string.Join(',', planned)} emitted={string.Join(',', emitted)} spawned={seen.Count} elapsed={frame / 60f:F3} warning={warning:F3} pitch={Read<float>(attack, "_pitch"):F3} maxSpeed={maximumSpeed:F3} {Stats()}");
                attack.Cancel();
                await Frames(1);
            }
        }
        Pool.DespawnAll();

        void Reset()
        {
            Pool.DespawnAll();
            player.Position = global::Field.PlayerStart;
            boss.Position = new Vector2(global::Field.Right - 40, 100);
            hud.HideBubble();
        }
        void Observe(HashSet<(ulong, ulong)> seen, ref float maximumSpeed)
        {
            foreach (var shot in Hostile())
            {
                seen.Add((shot.GetInstanceId(), shot.ActivationId));
                maximumSpeed = Math.Max(maximumSpeed, shot.Velocity.Length());
            }
        }
        string Stats() => $"hp={boss.TotalBars * Enemy.BarHp} currentHp={Read<int>(boss, "_hp")} lives={_game.StartLives} currentLives={player.Lives} speedMul={_game.BulletSpeedMul:F3} laneGap={BulletPool.BossLaneGap:F3} warnings={GameManager.ShowAttackTelegraphs}";
    }

    private async Task CheckSupplementalDensity(Enemy boss, Player player, Hud hud, string scene)
    {
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        hud.HideBubble();
        int hp = Read<int>(boss, "_hp"), lives = player.Lives;
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            _game.Difficulty = difficulty;
            foreach (bool charged in new[] { false, true })
            {
                Pool.DespawnAll();
                if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
                else Write(boss, "_form2", charged);
                int baseline = difficulty switch
                {
                    GameManager.Diff.Lunatic => scene switch { "Akari" => 7, "Rei" => 5, _ => 6 },
                    GameManager.Diff.Hard => scene is "Akari" or "MinaBattle" ? 5 : 4,
                    _ => scene == "MinaBattle" ? 3 : 2,
                };
                Call(boss, "Spiral", Pool);
                var shots = Hostile();
                Check(shots.Length == (difficulty == GameManager.Diff.Lunatic ? Mathf.RoundToInt(baseline * 1.5f) : baseline),
                    $"{scene}/{difficulty}/{charged}: native spiral count preserves lower difficulties and increases LUNATIC");
                float speed = scene switch
                {
                    "Akari" => 0.6f, "Koharu" => charged ? 72 : 60,
                    "Rei" => charged ? 72 : 62, _ => charged ? 100 : 82,
                };
                Check(shots.All(shot => Mathf.Abs(shot.Velocity.Length() - speed * _game.BulletSpeedMul) < 0.001f),
                    $"{scene}/{difficulty}/{charged}: native spiral keeps projectile speed");
                GD.Print($"DENSITY {scene}/{difficulty}/{charged}/native-spiral baseline={baseline} emitted={shots.Length} speed={shots[0].Velocity.Length():F3}");
            }
            Pool.DespawnAll();
            double rain = 0;
            int tick = 0, emitted = 0;
            var rng = new RandomNumberGenerator { Seed = 481516 };
            bool speedsPreserved = true;
            for (int frame = 0; frame < 7200; frame++)
            {
                PostBullets.Tick(this, rng, 1d / 60, ref rain, ref tick, boss, PostPool.Theme.Common, fallSpeed: 46);
                var shots = Hostile();
                emitted += shots.Length;
                speedsPreserved &= shots.All(shot => Mathf.Abs(shot.Velocity.Length()
                    - 46 * _game.BulletSpeedMul * (shot.WordAching ? 0.8f : 1)) < 0.001f);
                if (shots.Length > 0) Pool.DespawnAll();
            }
            double oldFrequency = difficulty switch
            { GameManager.Diff.Easy => 0.55f, GameManager.Diff.Hard => 1.4f, GameManager.Diff.Lunatic => 1.9f, _ => 1 };
            double oldPeriod = 0.17 * _game.DanmakuIntervalMul / oldFrequency;
            int events = (int)Math.Floor(7200 / Math.Ceiling(oldPeriod * 60) / 7);
            int baselinePosts = events * (difficulty == GameManager.Diff.Lunatic ? 2 : 1);
            int expected = difficulty switch
            { GameManager.Diff.Easy => events, GameManager.Diff.Lunatic => events * 3, _ => events + events / 2 };
            Check(emitted == expected && speedsPreserved,
                $"{scene}/{difficulty}: posting clock and speed stay unchanged while NORMAL+ emits 1.5x shots");
            rain = 0; tick = 0;
            for (int frame = 0; frame < 3600; frame++)
                PostBullets.Tick(this, rng, 1d / 60, ref rain, ref tick, boss, PostPool.Theme.Common, fallSpeed: 46);
            int cap = difficulty switch
            { GameManager.Diff.Easy => 4, GameManager.Diff.Normal => 11, GameManager.Diff.Hard => 15, _ => 21 };
            Check(Hostile().Length == cap, $"{scene}/{difficulty}: posting cap is retained for EASY and accommodates the added NORMAL+ shots");
            GD.Print($"DENSITY {scene}/{difficulty}/posts baseline120s={baselinePosts} emitted120s={emitted} ratio={(double)emitted / baselinePosts:F3} cap={cap} period={oldPeriod:F6}");
            Pool.DespawnAll();
            foreach (var cast in GetTree().GetNodesInGroup("overhead_casts")) cast.QueueFree();
            await Frames(1);
        }
        Check(Read<int>(boss, "_hp") == hp && player.Lives == lives, scene + ": density checks leave HP and lives unchanged");
    }

    private async Task CheckExtraRotation(Enemy boss, Player player, Hud hud, string scene)
    {
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        int hp = Read<int>(boss, "_hp"), lives = player.Lives;
        var caster = boss.GetChildren().OfType<AreaSpellCaster>().SingleOrDefault();
        var minaCaster = boss.GetChildren().OfType<MinaPhaseAttacks>().SingleOrDefault();
        Read<Dictionary<string, int>>(_game, "_upgrades")["n_dodge"] = 1;
        BossCharacterTechnique[] Active() => GetTree().GetNodesInGroup("boss_character_techniques")
            .OfType<BossCharacterTechnique>().Where(a => !a.Finished).ToArray();
        void SetForm(int form)
        {
            if (boss is BossMina) Write(boss, "_pattern", form);
            else Write(boss, "_form2", form > 0);
        }
        void Clear()
        {
            caster?.CancelPendingAttacks();
            minaCaster?.CancelPendingAttacks();
            boss.CancelAnimalTechnique();
            foreach (var attack in Active()) attack.Cancel();
            Pool.DespawnAll();
        }
        void Step(BossDanmaku rotation)
        {
            rotation.Tick(1d / 60);
            if (caster != null)
            {
                Call(boss, "TickForm2Technique", 1d / 60, Read<BossMover>(boss, "_mover"), caster);
                caster._Process(1d / 60);
            }
            minaCaster?._Process(1d / 60);
            foreach (var attack in Active())
            {
                attack.SetProcess(false);
                attack.SetPhysicsProcess(false);
                attack.Tick(1d / 60);
            }
            StepBullets();
        }
        foreach (var difficulty in new[] { GameManager.Diff.Easy })
        foreach (int form in boss is BossMina ? new[] { 0, 1, 2, 3, 4 } : new[] { 0, 1 })
        {
            Clear();
            await Frames(1);
            _game.Difficulty = difficulty;
            SetForm(form);
            player.Position = global::Field.PlayerStart;
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            if (caster != null)
            {
                caster.Suppressed = false;
                Write(boss, "_animalTechniqueCooldown", 3d);
            }
            minaCaster?.BeginPhase(form);
            var rotation = new BossDanmaku(boss);
            Write(boss, "_danmaku", rotation);
            var seen = new HashSet<ulong>();
            var kinds = new HashSet<BossTechniqueKind>();
            var extras = new HashSet<int>();
            var order = new List<string>();
            int frame;
            bool Complete() => extras.Count == 2 && kinds.Contains(BossTechniqueKind.Edge)
                && kinds.Contains(BossTechniqueKind.Area)
                && kinds.Contains(difficulty == GameManager.Diff.Lunatic ? BossTechniqueKind.Dodge : BossTechniqueKind.Normal)
                && (form == 0 || boss is BossMina || kinds.Contains(BossTechniqueKind.Signature));
            for (frame = 0; frame < 36000 && !Complete(); frame++)
            {
                Step(rotation);
                var active = Active();
                if (active.Length > 1) throw new Exception($"{scene}/{difficulty}/{form}: overlapping techniques {string.Join(',', active.Select(a => a.MechanicKey))}");
                foreach (var attack in active)
                {
                    if (!seen.Add(attack.GetInstanceId())) continue;
                    kinds.Add(attack.TechniqueKind);
                    if (attack.TechniqueKind == BossTechniqueKind.Extra) extras.Add(attack.TechniqueVariant);
                    order.Add(attack.TechniqueKind + ":" + attack.TechniqueVariant);
                    if (Hostile().Any(b => b.BossBarrageId != attack.GetInstanceId()))
                        throw new Exception(scene + ": a new technique overlaps residual hostile shots");
                    double time = attack.Elapsed;
                    Hud.BubblePaused = true;
                    Step(rotation);
                    if (attack.Elapsed != time) throw new Exception(scene + ": dialogue advanced an attack");
                    Hud.BubblePaused = false;
                }
                if (frame % 300 == 299) await Frames(1);
            }
            Check(Complete(), $"{scene}/{difficulty}/{form}: unique main/extra/area/edge/signature/dodge rotation has no starvation ({frame / 60f:F1}s): {string.Join(',', order)}");
        }
        Clear();
        await Frames(1);
        SetForm(0);
        _game.Difficulty = GameManager.Diff.Easy;
        var interrupted = new BossDanmaku(boss);
        Write(boss, "_danmaku", interrupted);
        Write(interrupted, "_extraDue", true);
        var residual = Pool.Spawn(new Vector2(global::Field.Left + 20, 30), Vector2.Right * 60, true);
        interrupted.Tick(1);
        Check(interrupted.ActiveTechnique == null, scene + ": extra warning waits for residual shots");
        Pool.Despawn(residual);
        interrupted.Tick(.01);
        var windup = interrupted.ActiveTechnique!;
        Check(windup != null, scene + ": first extra starts after clearance");
        object shielded = Read<object>(boss, "_phase");
        Write(boss, "_phase", Enum.Parse(shielded.GetType(), "Break"));
        interrupted.Tick(.01);
        Check(windup!.Finished && !interrupted.CrossfireActive && !Hostile().Any(), scene + ": break clears owned attack and bullets");
        Write(boss, "_phase", shielded);
        interrupted.Tick(.71);
        Check(interrupted.ActiveTechnique is { Elapsed: 0 }, scene + ": return from break begins a full warning");
        var beforeChange = interrupted.ActiveTechnique!;
        SetForm(boss is BossMina ? BossMina.DragonPhase : 1);
        interrupted.Tick(.01);
        Check(beforeChange.Finished && Read<int>(interrupted, "_extraSlot") == 0, scene + ": form change cancels old attack and resets extras");
        interrupted.Tick(.71);
        Check(interrupted.ActiveTechnique is { TechniqueKind: BossTechniqueKind.Extra, TechniqueVariant: 0, Elapsed: 0 }, scene + ": new visible form starts its own first extra");
        Clear();
        await Frames(1);
        if (boss is BossKoharu or BossRei)
        {
            var rotation = new BossDanmaku(boss);
            Write(boss, "_danmaku", rotation);
            caster!.Suppressed = true;
            caster.RequestFinale();
            for (int frame = 0; frame < 3600 && !caster.FinaleCompleted; frame++)
            {
                Step(rotation);
                if (frame % 300 == 299) await Frames(1);
            }
            Check(caster.FinaleCompleted, scene + ": requested finale reaches completion while ordinary spells are suppressed");
            Write(boss, "_phase", Enum.Parse(shielded.GetType(), "Break"));
            caster._Process(.01);
            Check(caster.FinaleCompleted && !caster.AoeActive, scene + ": a break after completion does not restart or erase the finale");
            Write(boss, "_phase", shielded);
            caster.Suppressed = false;
        }
        Clear();
        Check(Read<int>(boss, "_hp") == hp && player.Lives == lives, scene + ": rotation keeps HP and lives unchanged");
        await CheckCentralRotation(boss, player, hud, scene);
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private async Task CheckCentralRotation(Enemy boss, Player player, Hud hud, string scene)
    {
        int savedHp = Read<int>(boss, "_hp"), lives = player.Lives;
        int maximumHp = Read<int>(boss, "_maxHp");
        object shielded = Enum.Parse(Read<object>(boss, "_phase").GetType(), "Shielded");
        var caster = boss.GetChildren().OfType<AreaSpellCaster>().SingleOrDefault();
        var minaCaster = boss.GetChildren().OfType<MinaPhaseAttacks>().SingleOrDefault();
        BossCharacterTechnique[] Active() => GetTree().GetNodesInGroup("boss_character_techniques")
            .OfType<BossCharacterTechnique>().Where(a => !a.Finished).ToArray();
        void Clear()
        {
            foreach (var attack in Active()) attack.Cancel();
            caster?.CancelPendingAttacks();
            if (caster != null)
            {
                caster.Suppressed = false;
                Write(caster, "_finalePending", false);
            }
            minaCaster?.CancelPendingAttacks();
            Pool.DespawnAll();
            hud.HideBubble();
            Hud.BubblePaused = false;
        }
        void Segment(int segment)
        {
            Write(boss, "_phase", shielded);
            Write(boss, "_shieldTechniqueCompleted", false);
            Write(boss, "_shieldTechnique", null!);
            if (boss is BossMina)
            {
                Write(boss, "_pattern", segment);
                minaCaster!.BeginPhase(segment);
            }
            else Write(boss, "_form2", segment > 0);
            Write(boss, "_hp", (int)(maximumHp * (segment == 0 ? .75 : segment == 1 ? .4 : .2)));
            player.Position = global::Field.PlayerStart;
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            if (boss is BossMina { IsDragonForm: true } mina) mina.GlobalPosition = mina.KeepDragonInField(mina.GlobalPosition);
        }
        void Step(BossDanmaku rotation)
        {
            rotation.Tick(1d / 60);
            foreach (var attack in Active())
            {
                attack.SetProcess(false);
                attack.SetPhysicsProcess(false);
                attack.Tick(1d / 60);
            }
            StepBullets();
        }
        int segments = boss is BossMina ? 5 : boss is BossAkari ? 2 : 3;
        foreach (var difficulty in new[] { GameManager.Diff.Normal, GameManager.Diff.Hard, GameManager.Diff.Lunatic })
        {
            _game.Difficulty = difficulty;
            var identities = new HashSet<string>();
            for (int segment = 0; segment < segments; segment++)
            {
                Clear();
                Segment(segment);
                await Frames(1);
                var slots = BossAttackCycle.Slots(boss, segment);
                bool evolved = boss is BossMina mina ? mina.IsDragonForm : boss.HasEvolved;
                foreach (var slot in slots)
                    Check(identities.Add($"{slot.Kind}/{slot.Variant}" + (slot.Kind == BossTechniqueKind.Extra
                        ? evolved ? "/after" : "/before" : "")),
                        $"{scene}/{difficulty}/{segment}: slot {slot.Kind}/{slot.Variant} occurs once in the encounter cycle");
                var rotation = new BossDanmaku(boss);
                Write(boss, "_danmaku", rotation);
                var order = new List<string>();
                var seen = new HashSet<ulong>();
                int completed = 0;
                for (int frame = 0; frame < 72000 && completed < slots.Count * 2; frame++)
                {
                    Step(rotation);
                    var active = Active();
                    CheckNoOverlap(active, scene);
                    foreach (var attack in active)
                    {
                        if (!seen.Add(attack.GetInstanceId())) continue;
                        var expected = slots[order.Count % slots.Count];
                        if (attack.TechniqueKind != expected.Kind || attack.TechniqueVariant != expected.Variant)
                            throw new Exception($"{scene}/{difficulty}/{segment}: expected {expected}, got {attack.MechanicKey}");
                        if (Hostile().Any(b => b.BossBarrageId != attack.GetInstanceId()))
                            throw new Exception(scene + ": a central technique overlaps previous residual shots");
                        order.Add($"{attack.TechniqueKind}:{attack.TechniqueVariant}");
                        attack.Ended += ended => { if (ended.Completed) completed++; };
                        double elapsed = attack.Elapsed;
                        int slot = rotation.CycleSlot;
                        Hud.BubblePaused = true;
                        Step(rotation);
                        if (attack.Elapsed != elapsed || rotation.CycleSlot != slot)
                            throw new Exception(scene + ": dialogue advanced the central attack");
                        Hud.BubblePaused = false;
                        GetTree().Paused = true;
                        Step(rotation);
                        GetTree().Paused = false;
                        if (attack.Elapsed != elapsed || rotation.CycleSlot != slot)
                            throw new Exception(scene + ": pause advanced the central attack");
                    }
                    if (frame % 600 == 599) await Frames(1);
                }
                Check(completed == slots.Count * 2 && rotation.CycleRounds == 2 && rotation.CycleSlot == 0,
                    $"{scene}/{difficulty}/{segment}: every central slot naturally completes two rounds: {string.Join(',', order)}");
                if (slots.Any(s => s.Kind == BossTechniqueKind.Finale))
                {
                    Check(caster!.FinaleCompleted, scene + ": later central slots preserve natural Finale completion");
                    Write(boss, "_phase", Enum.Parse(shielded.GetType(), "Break"));
                    caster._Process(.01);
                    Check(caster.FinaleCompleted, scene + ": BREAK preserves central Finale completion");
                    Write(boss, "_phase", shielded);
                }

                Clear();
                Segment(segment);
                rotation = new BossDanmaku(boss);
                Write(boss, "_danmaku", rotation);
                var residual = Pool.Spawn(new Vector2(global::Field.Left + 20, 30), Vector2.Right * 60, true);
                rotation.Tick(2);
                Check(rotation.ActiveTechnique == null, scene + ": central warning waits for residual projectiles");
                Pool.Despawn(residual);
                GetTree().Paused = true;
                double timer = Read<double>(rotation, "_timer");
                rotation.Tick(2);
                GetTree().Paused = false;
                Check(rotation.ActiveTechnique == null && Read<double>(rotation, "_timer") == timer,
                    scene + ": pause also freezes a central inter-attack delay");
                rotation.Tick(.01);
                var cancelled = rotation.ActiveTechnique!;
                Check(cancelled != null, scene + ": first central warning begins after clearance");
                cancelled!.Cancel();
                Check(rotation.CycleSlot == 0 && rotation.CycleRounds == 0,
                    scene + ": cancelling a warning does not consume its slot");
                rotation.Tick(.01);
                rotation.Tick(.51);
                var retry = rotation.ActiveTechnique!;
                Check(retry != null && retry != cancelled && retry.Elapsed == 0
                    && retry.TechniqueKind == slots[0].Kind && retry.TechniqueVariant == slots[0].Variant,
                    scene + ": cancellation retries the same central slot with its full warning");

                int next = (segment + 1) % segments;
                Segment(next);
                rotation.Tick(.01);
                Check(retry!.Finished && rotation.CycleSegment == next && rotation.CycleSlot == 0 && rotation.CycleRounds == 0,
                    scene + ": form or encounter phase change cancels the previous segment and resets its order");
                rotation.Tick(.71);
                var nextSlot = BossAttackCycle.Slots(boss, next)[0];
                Check(rotation.ActiveTechnique is { Elapsed: 0 } following && following.TechniqueKind == nextSlot.Kind
                    && following.TechniqueVariant == nextSlot.Variant,
                    scene + ": changed phase begins its own first slot");

                Clear();
                Segment(segment);
                rotation = new BossDanmaku(boss);
                Write(boss, "_danmaku", rotation);
                Call(boss, "ResolveShieldBreak");
                Check(boss.GaugeAttackResolving && !boss.GaugeVulnerable,
                    scene + ": shield broken before any technique enters resolving instead of BREAK");
                for (int frame = 0; frame < 7200 && !boss.GaugeBreaking; frame++)
                {
                    Call(boss, "TickBossPhase", 1d / 60);
                    if (!boss.GaugeBreaking) Step(rotation);
                    if (frame % 600 == 599) await Frames(1);
                }
                Check(boss.GaugeBreaking && rotation.CycleSlot == 1 % slots.Count,
                    scene + ": resolving starts and naturally completes a technique before entering BREAK");
            }
            Check(identities.Count == 19 && identities.Contains("Dodge/0")
                && Enumerable.Range(0, 5).All(i => identities.Contains($"Crossfire/{i}")),
                $"{scene}/{difficulty}: all 19 named techniques are present including every Crossfire and Dodge");
        }
        Clear();
        Write(boss, "_hp", savedHp);
        Write(boss, "_phase", shielded);
        Check(player.Lives == lives, scene + ": central lifecycle tests preserve player lives");
    }

    private static void CheckNoOverlap(BossCharacterTechnique[] attacks, string scene)
    {
        if (attacks.Length > 1)
            throw new Exception($"{scene}: central techniques overlap: {string.Join(',', attacks.Select(a => a.MechanicKey))}");
    }

    private async Task CheckCasterTreeExit(Node sceneRoot, Enemy boss, string scene)
    {
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        var caster = boss.GetChildren().First(node => node is AreaSpellCaster or MinaPhaseAttacks);
        if (boss.UsesTechniqueCycle)
        {
            var rotation = new BossDanmaku(boss);
            Write(boss, "_danmaku", rotation);
            rotation.Tick(.81);
        }
        else if (caster is AreaSpellCaster area)
        {
            area.Suppressed = false;
            Write(area, "_pending", true);
            Write(area, "_nextEdge", true);
            area._Process(0);
        }
        else
        {
            var mina = (MinaPhaseAttacks)caster;
            mina.BeginPhase(Read<int>(boss, "_pattern"));
            mina._Process(1.21);
        }
        var attack = Read<BossCharacterTechnique>(caster, "_attack");
        Check(IsInstanceValid(attack) && !attack.Finished && attack.IsInsideTree(),
            scene + ": scene-exit probe retains an active caster-owned technique");
        var owned = Pool.SpawnBossShot(boss.ShotCenter, Vector2.Down, boss.BodyShotJob!.Value, boss.HasEvolved);
        owned.BossBarrageId = attack.GetInstanceId();
        bool exited = false, settledAtExit = false;
        attack.TreeExited += () =>
        {
            exited = true;
            settledAtExit = attack.Finished && !attack.IsStriking
                && Read<System.Collections.ICollection>(attack, "_hazards").Count == 0
                && Read<System.Collections.ICollection>(attack, "_hints").Count == 0;
        };
        // The technique exits before its older caster sibling; do not pre-cancel or clear the pool.
        sceneRoot.QueueFree();
        await Frames(2);
        Check(exited && settledAtExit && !IsInstanceValid(attack) && !owned.Active,
            scene + ": scene exit settles the technique before caster cleanup and clears owned shots");
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private void StepBullets()
    {
        foreach (var (bullet, id) in Hostile().Select(b => (b, b.ActivationId)).ToArray())
            if (bullet.Active && bullet.ActivationId == id)
            { bullet.SetPhysicsProcess(false); bullet._PhysicsProcess(1d / 60); }
    }

    private void MeasureCrossfire(Enemy boss, Player player, string scene, bool verify = false)
    {
        foreach (bool charged in new[] { false, true })
        {
            _game.Difficulty = GameManager.Diff.Hard;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            player.GlobalPosition = global::Field.PlayerStart;
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            Pool.DespawnAll();
            var attack = BossCrossfire.Begin(boss);
            var seen = new HashSet<(ulong, ulong)>();
            var covered = new HashSet<int>();
            int peak = 0, nearPeak = 0, contactFrames = 0;
            for (int frame = 0; frame < 900; frame++)
            {
                attack.Tick(1d / 60);
                StepBullets();
                var shots = Hostile();
                foreach (var b in shots) seen.Add((b.GetInstanceId(), b.ActivationId));
                foreach (var b in shots.Where(b => b.CollisionLayer != 0 && global::Field.Rect.HasPoint(b.GlobalPosition)))
                {
                    int x = Math.Min(3, (int)((b.GlobalPosition.X - global::Field.Left) / global::Field.Width * 4));
                    int y = Math.Min(3, (int)((b.GlobalPosition.Y - global::Field.Top) / global::Field.Height * 4));
                    covered.Add(y * 4 + x);
                }
                peak = Math.Max(peak, shots.Length);
                nearPeak = Math.Max(nearPeak, shots.Count(b => b.GlobalPosition.DistanceTo(player.GlobalPosition) < 64));
                if (shots.Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.GlobalPosition))) contactFrames++;
            }
            GD.Print($"[CrossfireDensity] {scene} charged={charged} total={seen.Count} peak={peak} near={nearPeak} contact_frames={contactFrames} covered={covered.Count}/16");
            if (verify)
            {
                int minimum = boss switch { BossAkari => charged ? 36 : 32, BossKoharu => charged ? 60 : 55, BossRei => 48, _ => charged ? 44 : 55 };
                int nearby = boss switch { BossAkari => 8, BossKoharu => 24, BossRei => 24, _ => 18 };
                // Split fragments may leave between bursts; keep total fire and local pressure, not distant clutter.
                if (boss is BossRei) Check(seen.Count >= 200, $"{scene}/{charged}: split bursts retain their overall projectile budget");
                Check(peak >= minimum && nearPeak >= nearby && contactFrames >= 60,
                    $"{scene}/{charged}: HARD keeps a dense field and repeatedly threatens the player's position");
                Check(covered.Count >= 12, $"{scene}/{charged}: hostile projectiles travel through at least three quarters of the arena sectors");
            }
            attack.Cancel();
            Pool.DespawnAll();
        }
    }

    private void CheckAkariAcceleration()
    {
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        foreach (bool charged in new[] { false, true })
        {
            _game.Difficulty = difficulty;
            var bullet = Pool.SpawnBossShot(new Vector2(350, 100), Vector2.Left, Job.Melee, charged);
            bullet.SetPhysicsProcess(false);
            float maximum = (charged ? 100 : 88) * _game.BulletSpeedMul;
            float previous = bullet.Velocity.Length();
            Check(previous < 1 && bullet.AccelRampSeconds >= 2, "Akari starts nearly stationary and ramps for over two seconds");
            for (int frame = 0; frame < 180; frame++)
            {
                bullet._PhysicsProcess(1d / 60);
                float speed = bullet.Velocity.Length();
                if (speed + 0.001f < previous || speed > maximum + 0.001f || speed - previous > maximum * 0.017f)
                    throw new Exception($"{difficulty}/{charged}: acceleration discontinuity at frame {frame}");
                if (frame == 59) Check(speed < maximum * 0.15f, "Akari remains visibly slow after one second");
                if (frame == 119) Check(speed > maximum * 0.45f && speed < maximum * 0.8f, "Akari is still accelerating after two seconds");
                previous = speed;
            }
            Check(Mathf.IsEqualApprox(previous, maximum), $"{difficulty}/{charged}: original top speed is preserved");
            Pool.Despawn(bullet);
            var playerShot = Pool.Spawn(new Vector2(100, 100), Vector2.Right * 10, false);
            playerShot.MakeAccel(12, 640, 0.5f);
            playerShot._PhysicsProcess(0.499);
            playerShot._PhysicsProcess(0.011);
            Check(playerShot.AccelRampSeconds == 0 && Mathf.IsEqualApprox(playerShot.Velocity.Length(), 640),
                "pool reuse preserves the player's original instant acceleration");
            Pool.Despawn(playerShot);
        }
    }

    private async Task CheckDebugGuides(Enemy boss, Player player, string scene)
    {
        bool screenshots = OS.GetCmdlineUserArgs().Contains("--guide-shots");
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            _game.Difficulty = difficulty;
            player.Position = global::Field.PlayerStart;
            Check(!_game.DebugAttackGuides, scene + ": debug guides default to OFF");
            bool normalPolicy = GameManager.ShowAttackTelegraphs;
            var attack = BossNormalBarrage.Begin(boss, 0);
            var volley = attack.Volley().ToArray();
            var passage = attack.DodgeTarget;
            double warning = attack.Warning;
            Check(attack.GuidesVisible, difficulty + ": attack cues remain visible without debug input");

            using var press = new InputEventKey { Keycode = Key.G, Pressed = true };
            GetViewport().PushInput(press);
            Check(_game.DebugAttackGuides && attack.GuidesVisible, $"{scene}/{difficulty}: G enables guides");
            using var repeat = new InputEventKey { Keycode = Key.G, Pressed = true, Echo = true };
            GetViewport().PushInput(repeat);
            using var release = new InputEventKey { Keycode = Key.G, Pressed = false };
            GetViewport().PushInput(release);
            Check(_game.DebugAttackGuides, "key repeat and release do not toggle guides");
            Check(attack.Volley().SequenceEqual(volley) && attack.DodgeTarget == passage && attack.Warning == warning
                && attack.Wave == 0, "guides do not change the attack or route");
            Hud.BubblePaused = true;
            Check(!attack.GuidesVisible, "dialogue hides debug guides");
            GetViewport().PushInput(press);
            Check(_game.DebugAttackGuides, "dialogue input does not toggle debug guides");
            Hud.BubblePaused = false;

            if (screenshots && difficulty == GameManager.Diff.Hard)
            {
                attack.QueueRedraw();
                await SaveGuides("route");
            }
            attack.Cancel();
            var crossfire = BossCrossfire.Begin(boss, weave: true);
            crossfire.Tick(0.3);
            Check(crossfire.WarningVisible, $"{difficulty}: debug overrides crossfire warnings");
            if (screenshots && difficulty == GameManager.Diff.Lunatic) await SaveGuides("crossfire");
            GetViewport().PushInput(press);
            Check(!_game.DebugAttackGuides && GameManager.ShowAttackTelegraphs == normalPolicy
                && crossfire.WarningVisible == normalPolicy, $"{difficulty}: second G restores normal visibility");
            using var modified = new InputEventKey { Keycode = Key.G, Pressed = true, CtrlPressed = true };
            GetViewport().PushInput(modified);
            Check(!_game.DebugAttackGuides, "modified shortcuts do not toggle guides");
            using var locked = new InputEventKey { Keycode = Key.G, Pressed = true, ShiftPressed = true };
            GetViewport().PushInput(locked);
            Check(_game.DebugAttackGuides, "G works while holding the lock-on key");
            GetViewport().PushInput(press);
            var textField = new LineEdit();
            GetTree().Root.AddChild(textField);
            textField.GrabFocus();
            _game._UnhandledKeyInput(press);
            Check(!_game.DebugAttackGuides, "typing in a text field does not toggle guides");
            textField.ReleaseFocus();
            textField.QueueFree();
            GetTree().Paused = true;
            _game._UnhandledKeyInput(press);
            GetTree().Paused = false;
            Check(!_game.DebugAttackGuides, "pause menu does not toggle guides");
            crossfire.Cancel();
            await Frames(2);
        }

        async Task SaveGuides(string tag)
        {
            await Frames(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string folder = ProjectSettings.GlobalizePath("res://build/qa_story/debug_guides_art");
            DirAccess.MakeDirRecursiveAbsolute(folder);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng($"{folder}/{scene}_{_game.Difficulty}_{tag}.png") == Error.Ok, "debug guide screenshot saved");
        }
    }

    private async Task CheckReadableNormal(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.Difficulty = GameManager.Diff.Normal;
        _game.SelectedJob = Job.Tank;
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        var starts = new[] { new Vector2(global::Field.Left + 24, 36), global::Field.PlayerStart,
            new Vector2(global::Field.Left + 120, 180), new Vector2(global::Field.Right - 24, 36),
            new Vector2(global::Field.Right - 60, 108), new Vector2(global::Field.CenterX, 180) };
        bool probe = OS.GetCmdlineUserArgs().Contains("--gap-probe");
        bool artOnly = OS.GetCmdlineUserArgs().Contains("--lesson-art");
        if (probe) starts = new[] { new Vector2(global::Field.Left + 120, 180) };
        if (artOnly) starts = new[] { global::Field.PlayerStart };
        var patterns = (probe ? new[] { 1 } : Enumerable.Range(0, BossNormalBarrage.PatternCount(boss))).ToArray();
        string? patternFilter = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--lesson-pattern="));
        if (patternFilter != null)
        {
            Check(int.TryParse(patternFilter.Split('=')[1], out int pattern) && pattern >= 0 && pattern < BossNormalBarrage.PatternCount(boss), "valid lesson pattern selection");
            patterns = new[] { pattern };
        }
        string? startFilter = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--lesson-start="));
        if (startFilter != null)
        {
            Check(int.TryParse(startFilter.Split('=')[1], out int start) && start >= 0 && start < starts.Length, "valid lesson start selection");
            starts = new[] { starts[start] };
        }
        var lessons = new Dictionary<(bool, int, Vector2), Vector2>();
        var density = new Dictionary<(bool, int, Vector2), int>();
        var durations = new Dictionary<(bool, int, Vector2), int>();
        var cutbacks = new Dictionary<(bool, int, Vector2, int), Vector2>();
        foreach (var difficulty in new[] { GameManager.Diff.Easy, GameManager.Diff.Normal, GameManager.Diff.Hard })
        {
            _game.Difficulty = difficulty;
            foreach (bool charged in new[] { false, true })
            foreach (int pattern in patterns)
            foreach (Vector2 start in starts)
            foreach (int movement in artOnly ? new[] { 1 } : difficulty == GameManager.Diff.Easy ? new[] { 0, 1 } : new[] { 0, 1, 2, 3 })
            {
                bool move = movement != 0;
                Pool.DespawnAll();
                if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
                else Write(boss, "_form2", charged);
                boss.Position = new Vector2(start.X < global::Field.CenterX ? global::Field.Right - 40 : global::Field.Left + 40, 100);
                player.Position = start;
                var attack = BossNormalBarrage.Begin(boss, pattern);
                if (boss is BossAkari && difficulty != GameManager.Diff.Easy && movement == 1)
                {
                    float radius = (float)typeof(BossNormalBarrage).GetProperty("PassageRadius", Private)!.GetValue(attack)!;
                    Check(Mathf.IsEqualApprox(radius, (difficulty == GameManager.Diff.Normal ? 6 : 5) - (charged ? 1 : 0)),
                        "Akari keeps a usable escape width on both difficulties");
                    Vector2 forward = Read<Vector2>(attack, "_direction");
                    float distance = Read<float>(attack, "_akariDistance");
                    var angles = ((System.Collections.Generic.IEnumerable<float>)Call(attack, "AkariAngles")!).ToArray();
                    Check(angles.Length == (charged ? 3 : 2) && angles.All(angle =>
                    {
                        Vector2 origin = attack.Passage - forward.Rotated(angle) * distance;
                        return attack.Volley().Count(shot => pattern % 3 == 0
                            ? shot.Direction.IsEqualApprox(forward.Rotated(angle))
                            : Mathf.Abs((shot.Position - origin).Cross(shot.Direction)) < 0.01f) >= 3;
                    }), "Akari fires real projectile groups from every emitter");
                }
                Check(attack.GuidesVisible == GameManager.ShowAttackTelegraphs, $"{difficulty}: guide policy");
                if (difficulty != GameManager.Diff.Easy)
                    Check(Read<double>(attack, "_clock") >= (difficulty == GameManager.Diff.Normal ? 0.55 : 0.45),
                        $"{difficulty}: the initial shot lanes remain visible before deployment");
                Check(attack.Volley().All(shot => (BossNormalBarrage.WarningEnd(shot.Position, shot.Direction) - shot.Position)
                    .Normalized().Dot(shot.Direction) > 0.99999f), "warning lines match the actual shot direction");
                var lesson = (charged, pattern, start);
                if (difficulty == GameManager.Diff.Easy) lessons[lesson] = attack.Passage;
                Check(attack.Passage == lessons[lesson], "the EASY escape route transfers to NORMAL and HARD");
                if (difficulty != GameManager.Diff.Easy)
                    for (int beat = 1; beat < Read<List<Vector2>>(attack, "_passages").Count; beat++)
                    {
                        var key = (charged, pattern, start, beat);
                        if (difficulty == GameManager.Diff.Normal) cutbacks[key] = attack.GapForWave(beat);
                        else Check(attack.GapForWave(beat) == cutbacks[key], "every planned NORMAL cutback transfers to HARD");
                    }
                int count = attack.Volley().Count();
                if (difficulty == GameManager.Diff.Hard && density.TryGetValue(lesson, out int previous))
                    Check(count >= previous, "HARD does not reduce the NORMAL lane density");
                density[lesson] = count;
                Vector2 tolerance = RouteTolerance(boss);
                var goal = lessons[lesson] + tolerance;
                var pendingGoal = goal;
                int reactionAt = 24, observedBeat = 0;
                bool hit = false;
                var closedGaps = new HashSet<int>();
                int peak = 0, frame = 0;
                int firstVolley = -1, lastVolley = -1, emitted = 0;
                float chargingTravel = 0;
                float launchedSpeed = 0;
                bool chargeVisible = true, sawCharge = false;
                var splitBranches = new HashSet<(ulong, ulong)>();
                var splitFragments = new HashSet<(ulong, ulong)>();
                float reiSeedRadius = (charged ? BulletPool.BossChargeRadius : BulletPool.BossRadius) * 1.5f;
                for (; frame < 3600; frame++)
                {
                    if (attack.RouteBeat != observedBeat && difficulty != GameManager.Diff.Easy)
                    {
                        observedBeat = attack.RouteBeat;
                        var key = (charged, pattern, start, observedBeat);
                        Check(attack.DodgeTarget == cutbacks[key], "NORMAL cutback routes transfer to HARD");
                        if (movement == 1 || movement == 3 && observedBeat == 1)
                        {
                            pendingGoal = cutbacks[key] + tolerance;
                            reactionAt = frame + 24;
                        }
                    }
                    if (frame >= reactionAt) goal = pendingGoal;
                    if (move && frame >= 24) player.Position = player.Position.MoveToward(goal, player.SlowestMoveSpeed / 60);
                    if (movement == 1 && !global::Field.Rect.HasPoint(player.Position))
                        throw new Exception($"{scene}/{difficulty}/{charged}/{pattern}/{start}: gap route left the playable field");
                    attack.Tick(1d / 60);
                    if (attack.Wave != emitted)
                    {
                        emitted = attack.Wave;
                        if (firstVolley < 0) firstVolley = frame;
                        lastVolley = frame;
                    }
                    var shots = Hostile();
                    peak = Math.Max(peak, shots.Length);
                    if (boss is BossAkari)
                        foreach (var shot in shots.Where(b => b.AccelCharging && !b.LeadIn))
                        {
                            chargeVisible &= global::Field.Rect.HasPoint(shot.Position);
                            sawCharge = true;
                            chargingTravel = Math.Max(chargingTravel, shot.Velocity.Length());
                        }
                    foreach (var shot in shots.Where(b => b.Accel && !b.AccelCharging && !b.LeadIn))
                        launchedSpeed = Math.Max(launchedSpeed, shot.Velocity.Length());
                    StepBullets();
                    if (boss is BossRei && pattern == 1 && movement == 1)
                        foreach (var shot in Hostile().Where(b => b.BossBarrageId == attack.GetInstanceId()
                            && !b.LeadIn && b.CollisionLayer != 0 && b.Radius < reiSeedRadius * 0.9f))
                        {
                            var activation = (shot.GetInstanceId(), shot.ActivationId);
                            if (shot.Radius < reiSeedRadius * 0.5f) splitFragments.Add(activation);
                            else splitBranches.Add(activation);
                        }
                    if (movement == 1 && difficulty != GameManager.Diff.Easy)
                        foreach (var shot in Hostile().Where(b => b.CollisionLayer != 0 && b.BossBarrageWave > 0))
                            if (BossCharacterShotQa.HitsPlayer(shot, attack.GapForWave(shot.BossBarrageWave - 1) + tolerance))
                                closedGaps.Add(shot.BossBarrageWave);
                    if (movement == 1 && !hit)
                    {
                        foreach (var contact in Hostile().Where(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position)))
                            GD.Print($"GAP CONTACT frame={frame} route={attack.RouteBeat} wave={contact.BossBarrageWave} player={player.Position} bullet={contact.Position} velocity={contact.Velocity} target={goal}");
                        if (player.Position.DistanceTo(boss.ShotCenter) < 28)
                            GD.Print($"BODY CONTACT frame={frame} route={attack.RouteBeat} player={player.Position} boss={boss.ShotCenter}");
                    }
                    hit |= Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position))
                        || player.Position.DistanceTo(boss.ShotCenter) < 28;
                    if (OS.GetCmdlineUserArgs().Contains("--readable-shots") && movement == 1 && !charged && start == global::Field.PlayerStart)
                        await Frames(1);
                    if ((OS.GetCmdlineUserArgs().Contains("--readable-shots") || OS.GetCmdlineUserArgs().Contains("--lesson-shots"))
                        && movement == 1 && !charged && start == global::Field.PlayerStart
                        && frame is 45 or 135 or 240 or 480 or 720)
                    {
                        await Frames(2);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        string folder = ProjectSettings.GlobalizePath("res://build/qa_story/readable_normal_art");
                        DirAccess.MakeDirRecursiveAbsolute(folder);
                        using var screenshot = GetViewport().GetTexture().GetImage();
                        Check(screenshot.SavePng($"{folder}/{scene}_{difficulty}_{pattern}_{frame}.png") == Error.Ok, "readable pattern screenshot saved");
                    }
                    if (attack.Finished || movement != 1 && hit) break;
                }
                Check(frame < 3600 && (movement != 1 || peak >= 6), $"{scene}/{difficulty}/{charged}/{pattern}: sustained pattern completes within sixty seconds (elapsed={frame / 60f:F1}s, waves={attack.Wave}, beat={attack.RouteBeat})");
                Check(hit == (movement != 1), $"{scene}/{difficulty}/{charged}/{pattern}/{start}/{movement}: full route survives, parking or omitting a cutback is threatened (0.4s reaction, {tolerance.X}px tolerance)");
                if (movement == 1)
                {
                    if (boss is BossRei && pattern == 1)
                        Check(splitBranches.Count > 0 && splitFragments.Count > 0,
                            $"{scene}/{difficulty}/{charged}/{pattern}/{start}: the live fan produces both split generations (branches={splitBranches.Count}, fragments={splitFragments.Count})");
                    if (difficulty == GameManager.Diff.Easy)
                    {
                        durations[lesson] = lastVolley - firstVolley;
                        Check(attack.Wave == (charged ? 4 : 3)
                            && Mathf.IsEqualApprox(_game.BulletSpeedMul, 0.65f), "EASY preserves the previous NORMAL baseline");
                    }
                    else
                    {
                        Check(lastVolley - firstVolley > durations[lesson] * 3 && attack.RouteBeat >= 7 && attack.Wave >= 8,
                            "technical barrage sustains pressure through moving gaps");
                        Check(Enumerable.Range(0, attack.Wave).Select(i => new Vector2I((int)Mathf.Round(attack.GapForWave(i).X),
                            (int)Mathf.Round(attack.GapForWave(i).Y))).Distinct().Count() >= 4,
                            "a single attack visits multiple gaps instead of one fixed corridor");
                        Check(Enumerable.Range(1, attack.Wave - 1).All(closedGaps.Contains),
                            $"{scene}/{difficulty}/{charged}/{pattern}/{start}: EVERY wave closes the preceding gap (closed={string.Join(',', closedGaps.Order())})");
                        if (start == global::Field.PlayerStart)
                            GD.Print($"TECHNICAL {scene}/{difficulty}/{charged}/{pattern}: {frame / 60f:F1}s, {attack.Wave} volleys, {attack.RouteBeat} gap changes, mean interval={(lastVolley - firstVolley) / 60f / (attack.Wave - 1):F2}s, tolerance={tolerance}");
                    }
                }
                if (boss is BossAkari && movement == 1) Check(sawCharge && chargeVisible && chargingTravel < 1 && launchedSpeed > 40,
                    "Akari charges on screen below 1px/s then accelerates above 40px/s");
                attack.Cancel();
                await Frames(1);
                hud.HideBubble();
            }
            Pool.DespawnAll();
            player.Position = global::Field.PlayerStart;
            var rotation = new BossDanmaku(boss);
            rotation.Tick(0.71);
            Check(Read<BossNormalBarrage?>(rotation, "_normalBarrage") != null && rotation.Holding,
                scene + ": " + difficulty + " uses the shared sequence in actual combat");
            Call(rotation, "CancelCrossfire");
            await Frames(1);
        }
        if (boss is BossAkari akari)
        foreach (var difficulty in new[] { GameManager.Diff.Easy, GameManager.Diff.Normal })
        {
            _game.Difficulty = difficulty;
            Write(akari, "_form2", false);
            Write(akari, "_shieldBreaks", 0);
            Write(akari, "_hp", akari.TotalBars * Enemy.BarHp);
            Write(akari, "_beatsFired", 0);
            Write(akari, "_memoryPending", false);
            Write(akari, "_memoryPlayed", false);
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                Call(akari, "EnterBreak");
                Call(akari, "EnterExposed");
                hud.HideBubble();
                for (int hit = 0; hit < 30; hit++) Call(akari, "BombStrike");
                Check(Read<bool>(akari, "_memoryPending") == (cycle == 3), $"{difficulty} Akari transformation waits for shield break {cycle}/3");
                Call(akari, "EnterReclose");
                Call(akari, "EnterShielded");
                hud.HideBubble();
            }
            Write(akari, "_memoryPending", false);
            Write(akari, "_hp", akari.TotalBars * Enemy.BarHp);
            Write(akari, "_shieldBreaks", 1);
            Write(akari, "_beatsFired", 0);
            akari.DealDirectDamage(10000);
            Call(akari, "OnHpChanged");
            Check(akari.HpRatio >= 0.54f && !Read<bool>(akari, "_memoryPending"), "direct damage cannot bypass three breaks");
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private Vector2 RouteTolerance(Enemy boss) => Vector2.One * (boss is BossAkari && _game.Difficulty != GameManager.Diff.Easy
        ? _game.Difficulty == GameManager.Diff.Hard ? 2f : 2.5f : 3f);

    private void CheckMovingSequence(BossNormalBarrage attack, Player player, Enemy boss, Action<double> tick, string label)
    {
        var tolerance = RouteTolerance(boss);
        var goal = attack.Passage + tolerance;
        var pending = goal;
        int beat = 0, reaction = 24, frame = 0;
        var closed = new HashSet<int>();
        bool hit = false;
        for (; frame < 3600 && !attack.Finished; frame++)
        {
            if (attack.RouteBeat != beat)
            {
                beat = attack.RouteBeat;
                pending = attack.DodgeTarget + tolerance;
                reaction = frame + 24;
            }
            if (frame >= reaction) goal = pending;
            if (frame >= 24) player.Position = player.Position.MoveToward(goal, player.SlowestMoveSpeed / 60);
            tick(1d / 60);
            StepBullets();
            foreach (var bullet in Hostile().Where(b => b.CollisionLayer != 0))
            {
                if (BossCharacterShotQa.HitsPlayer(bullet, player.Position))
                {
                    if (!hit) GD.Print($"SPECIAL CONTACT {label} frame={frame} beat={beat} wave={bullet.BossBarrageWave} player={player.Position} bullet={bullet.Position} goal={goal}");
                    hit = true;
                }
                if (bullet.BossBarrageId == attack.GetInstanceId() && bullet.BossBarrageWave > 0
                    && BossCharacterShotQa.HitsPlayer(bullet, attack.GapForWave(bullet.BossBarrageWave - 1) + tolerance))
                    closed.Add(bullet.BossBarrageWave);
            }
            hit |= player.Position.DistanceTo(boss.ShotCenter) < 28;
        }
        GD.Print($"MOVING SEQUENCE {label}: {frame / 60f:F1}s, {attack.Wave} volleys, {attack.RouteBeat} gap changes");
        Check(frame < 3600 && attack.Wave >= 4 && !hit, label + ": continuous moving route survives");
        Check(Enumerable.Range(1, attack.Wave - 1).All(closed.Contains), label + ": every wave closes the old gap");
    }

    private async Task CheckSignatureLessons(Enemy boss, Player player, Hud hud, string scene)
    {
        if (boss is BossMina) return;
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        foreach (var difficulty in new[] { GameManager.Diff.Normal, GameManager.Diff.Hard })
        foreach (var start in new[] { global::Field.PlayerStart, new Vector2(90, 36), new Vector2(324, 180) })
        {
            _game.Difficulty = difficulty;
            Pool.DespawnAll();
            player.Position = start;
            boss.Position = new Vector2(start.X < global::Field.CenterX ? 340 : 110, 100);
            var caster = boss.GetChildren().OfType<AreaSpellCaster>().First();
            var attack = BossAnimalTechnique.Begin(boss, scene.ToLowerInvariant(), caster);
            attack.ProcessMode = ProcessModeEnum.Disabled;
            CheckMovingSequence(attack.Sequence!, player, boss, attack._PhysicsProcess, $"{scene}/{difficulty}/signature/{start}");
            attack.Cancel();
            await Frames(1);
            hud.HideBubble();
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private void CheckAreaSequences(Enemy boss, Player player, string scene)
    {
        if (boss is BossMina) return;
        _game.SelectedJob = Job.Tank;
        var caster = boss.GetChildren().OfType<AreaSpellCaster>().First();
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        foreach (var shape in new[] { AreaStrike.Shape.Circle, AreaStrike.Shape.Rect, AreaStrike.Shape.BeamH, AreaStrike.Shape.BeamV, AreaStrike.Shape.BeamSeg })
        {
            _game.Difficulty = difficulty;
            player.Position = global::Field.PlayerStart;
            boss.Position = new Vector2(340, 108);
            Call(caster, "Cast");
            Write(caster, "_pendShape", shape);
            int waves = difficulty == GameManager.Diff.Easy ? 1 : 4;
            for (int wave = 0; wave < waves; wave++)
            {
                caster._Process(0.71);
                var zones = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().Where(z => !z.IsQueuedForDeletion()).ToArray();
                Check(zones.Any(z => z.CoversPoint(player.Position)), $"{scene}/{difficulty}/{shape}/{wave}: reacquires the player");
                bool found = false;
                for (int radius = 4; radius <= 384 && !found; radius += 2)
                for (int direction = 0; direction < 32 && !found; direction++)
                {
                    var goal = player.Position + Vector2.FromAngle(direction * Mathf.Tau / 32) * radius;
                    if (!global::Field.Rect.Grow(-4).HasPoint(goal)) continue;
                    if (Geometry2D.GetClosestPointToSegment(boss.ShotCenter, player.Position, goal).DistanceTo(boss.ShotCenter) < 28) continue;
                    if (zones.Any(z => z.CoversPoint(player.Position.MoveToward(goal,
                        ((float)Read<double>(z, "_warn") - 0.4f) * player.SlowestMoveSpeed)))) continue;
                    player.Position = goal;
                    found = true;
                }
                Check(found, $"{scene}/{difficulty}/{shape}/{wave}: area sequence retains an exit after 0.4s reaction");
                foreach (var zone in zones) zone.Free();
            }
            caster._Process(0.06);
            Check(!Read<bool>(caster, "_pending"), $"area sequence ends after exactly {waves} waves");
            caster.CancelPendingAttacks();
        }
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            _game.Difficulty = difficulty;
            player.Position = global::Field.PlayerStart;
            var remaining = Pool.Spawn(new Vector2(global::Field.Left + 20, 24), Vector2.Right * 60, true);
            Call(caster, "Cast");
            var rotation = new BossDanmaku(boss);
            int frames = 0;
            double rainT = 0;
            int wordTick = 0;
            var rainRandom = new RandomNumberGenerator { Seed = 4401 };
            while (remaining.Active && frames++ < 1200)
            {
                caster._Process(1d / 60);
                rotation.Tick(1d / 60);
                PostBullets.Tick(this, rainRandom, 1d / 60, ref rainT, ref wordTick, source: boss);
                if (!caster.AttackPending || !rotation.Holding || GetTree().GetNodesInGroup("aoe").Any(n => !n.IsQueuedForDeletion()))
                    throw new Exception($"{scene}/{difficulty}: residual bullets must reserve, but not overlap, the next area");
                StepBullets();
            }
            Check(frames < 1200 && Hostile().Length == 0, $"{scene}/{difficulty}: reserved area lets residual shots leave without refilling them");
            GD.Print($"[AnswerQA] {scene}/{difficulty}: residual-shot handoff waited {frames / 60d:F2}s");
            caster._Process(0.71);
            var zones = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().ToArray();
            Check(zones.Length > 0, "area warning starts when its exit is clear");
            foreach (var zone in zones) zone.Free();
            caster.CancelPendingAttacks();
            CheckAreaBreakInterruptions(boss, caster, difficulty);
        }
        if (boss is BossKoharu)
        foreach (var difficulty in new[] { GameManager.Diff.Normal, GameManager.Diff.Hard })
        {
            _game.Difficulty = difficulty;
            player.Position = global::Field.PlayerStart;
            caster.Suppressed = true;
            Write(boss, "_gotoPhase", 1);
            Write(boss, "_gotoWave", 0);
            Write(boss, "_gotoT", 0d);
            for (int wave = 0; wave < 4; wave++)
            {
                Call(boss, "TickGoto", 0.25d);
                var zones = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().ToArray();
                Check(zones.Length == 2 && zones.All(z => z.CoversPoint(player.Position)), "Koharu cross reacquires on every wave");
                Vector2 goal = player.Position + (wave % 2 == 0 ? new Vector2(12, 12) : new Vector2(16, 0));
                Check(zones.All(z => !z.CoversPoint(player.Position.MoveToward(goal,
                    ((float)Read<double>(z, "_warn") - 0.4f) * player.SlowestMoveSpeed))), "four changing crosses remain escapable");
                player.Position = goal;
                foreach (var zone in zones) zone.Free();
            }
            Call(boss, "TickGoto", 0.25d);
            Check(Read<int>(boss, "_gotoPhase") == 0 && !caster.Suppressed, "cross sequence releases its attack gate after wave four");
        }
    }

    private void CheckAreaBreakInterruptions(Enemy boss, AreaSpellCaster caster, GameManager.Diff difficulty)
    {
        object shielded = Read<object>(boss, "_phase");
        foreach (string state in new[] { "Break", "Exposed", "Reclose" })
        {
            Call(caster, "Cast");
            caster._Process(1d);
            var zones = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().Where(z => !z.IsQueuedForDeletion()).ToArray();
            Check(zones.Length > 0, "break regression starts with live area warnings");
            Write(boss, "_phase", Enum.Parse(shielded.GetType(), state));
            foreach (var zone in zones) zone._Process(100d);
            caster._Process(1d);
            Check(zones.All(z => z.IsQueuedForDeletion() && !z.IsStriking) && !caster.AttackPending,
                $"{boss.GetType().Name}/{difficulty}/{state}: area warnings cancel without landing or restarting");
            foreach (var zone in zones) zone.Free();

            caster.RequestFinale();
            caster._Process(10d);
            Check(caster.AoeActive && Read<bool>(caster, "_aoePending"),
                $"{state}: a newly earned relay remains reserved without counting as completion");
            Write(boss, "_phase", shielded);
            caster._Process(1d);
            var interrupted = Read<AreaStrike>(caster, "_aoeStrike");
            int remaining = Read<int>(caster, "_chainRemain");
            Write(boss, "_phase", Enum.Parse(shielded.GetType(), state));
            interrupted._Process(100d);
            caster._Process(1d);
            Check(interrupted.IsQueuedForDeletion() && !interrupted.IsStriking && caster.AoeActive
                && Read<int>(caster, "_chainRemain") == remaining + 1,
                $"{state}: interrupted relay hop is cancelled and must be replayed before completion");
            interrupted.Free();
            Write(boss, "_phase", shielded);
            caster._Process(1d);
            var replayed = Read<AreaStrike>(caster, "_aoeStrike");
            Check(!replayed.IsStriking && Read<int>(caster, "_chainRemain") == remaining,
                "resumed relay reissues a full warning for the interrupted hop");
            caster.CancelPendingAttacks();
            replayed.Free();
        }
    }

    private async Task CheckMovingHandoffs(Enemy boss, Player player, Hud hud, string scene)
    {
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        foreach (var difficulty in new[] { GameManager.Diff.Normal, GameManager.Diff.Hard })
        foreach (bool charged in new[] { false, true })
        foreach (var start in new[] { global::Field.PlayerStart, new Vector2(90, 36), new Vector2(324, 180) })
        {
            _game.Difficulty = difficulty;
            Pool.DespawnAll();
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            boss.Position = new Vector2(start.X < global::Field.CenterX ? 340 : 110, 100);
            player.Position = start;
            var rotation = new BossDanmaku(boss);
            Vector2 goal = start, pending = start;
            ulong id = 0;
            int beat = -1, reaction = 0, frame = 0, previousEnd = -1, transfers = 0;
            int techniques = BossNormalBarrage.PatternCount(boss) + 1;
            for (; frame < 18000 && Read<int>(rotation, "_phrase") < techniques; frame++)
            {
                var attack = Read<BossNormalBarrage?>(rotation, "_normalBarrage");
                if (attack != null && (attack.GetInstanceId() != id || beat != attack.RouteBeat))
                {
                    if (attack.GetInstanceId() != id && previousEnd >= 0)
                    {
                        Check(frame - previousEnd <= 6, "next technique winds up within 0.1s");
                        transfers++;
                    }
                    id = attack.GetInstanceId();
                    beat = attack.RouteBeat;
                    pending = attack.DodgeTarget + RouteTolerance(boss);
                    reaction = frame + 24;
                }
                if (frame >= reaction) goal = pending;
                player.Position = player.Position.MoveToward(goal, player.SlowestMoveSpeed / 60);
                int phrase = Read<int>(rotation, "_phrase");
                rotation.Tick(1d / 60);
                if (phrase != Read<int>(rotation, "_phrase")) previousEnd = frame;
                StepBullets();
                var contact = Hostile().FirstOrDefault(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position));
                if (contact != null || player.Position.DistanceTo(boss.ShotCenter) < 28)
                    throw new Exception($"{scene}/{difficulty}/{charged}/{start}: handoff hit frame={frame} phrase={phrase} beat={beat} player={player.Position} goal={goal} bullet={contact?.Position} current={contact?.BossBarrageId == id}");
            }
            Check(frame < 18000 && transfers == techniques - 1, $"{scene}/{difficulty}/{charged}/{start}: {techniques} connected techniques remain escapable ({frame / 60f:F1}s total)");
            Call(rotation, "CancelCrossfire");
            await Frames(1);
            hud.HideBubble();
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private Vector2 ShapeAwareDodge(Player player, Enemy boss, Vector2 previous, Vector2? goal = null, float? speed = null, bool longLookahead = false, bool precise = false, bool dash = false, bool ignoreCrossfirePreview = false)
    {
        float[] times = _liveClear ? new[] { 0.02f, 0.05f, 0.10f, 0.16f, 0.24f, 0.34f, 0.46f, 0.62f, 0.82f, 1.1f, 1.4f }
            : dash ? new[] { 0.46f, 0.5f, 0.55f, 0.65f, 0.8f }
            : precise && boss is BossMina ? new[] { 0.06f, 0.12f, 0.18f, 0.24f, 0.3f, 0.4f, 0.5f, 0.7f, 0.9f, 1.1f }
            : precise ? new[] { 0.06f, 0.12f, 0.18f, 0.24f, 0.3f, 0.4f, 0.5f, 0.7f }
            : longLookahead ? new[] { 0.12f, 0.24f, 0.36f, 0.5f, 0.7f, 1.0f, 1.4f } : new[] { 0.12f, 0.24f, 0.36f, 0.5f, 0.7f };
        var threats = new List<(Vector2 Position, Transform2D Rotation, Vector2[] Hull, Rect2 Bounds, int Sample)>();
        float homingDuration = (float)typeof(Bullet).GetField("EnemyHomingDuration", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        float homingTurn = (float)typeof(Bullet).GetField("EnemyHomingTurnRate", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        foreach (var bullet in Hostile())
        {
            if (bullet.GlobalPosition.DistanceTo(player.GlobalPosition) > 160) continue;
            float lead = bullet.LeadIn ? Read<float>(bullet, "_leadDur") - Read<float>(bullet, "_leadElapsed") : 0;
            Vector2 origin = bullet.LeadIn ? Read<Vector2>(bullet, "_leadTo") : bullet.GlobalPosition;
            for (int sample = 0; sample < times.Length; sample++)
            {
                float time = times[sample];
                if (time < lead) continue;
                float duration = time - lead;
                Vector2 offset = bullet.Velocity * duration;
                float rotation = bullet.GlobalRotation;
                if (bullet.Homing && !Read<bool>(bullet, "_enemyHomingFinished"))
                {
                    float remaining = Math.Min(duration, Math.Max(0, homingDuration - Read<float>(bullet, "_age")));
                    Vector2 position = origin, velocity = bullet.Velocity;
                    float elapsed = 0;
                    while (elapsed < remaining)
                    {
                        float step = Math.Min(1f / 60, remaining - elapsed);
                        Vector2 target = player.Position + bullet.FormationHomingOffset
                            + previous * (speed ?? player.SlowestMoveSpeed) * (lead + elapsed);
                        if (velocity.Dot(target - position) <= 0) break;
                        float turn = Mathf.DegToRad(Math.Min(bullet.TurnRateOverride > 0 ? bullet.TurnRateOverride : homingTurn, homingTurn)) * step;
                        float angle = velocity.Angle() + Mathf.Clamp(Mathf.AngleDifference(velocity.Angle(), (target - position).Angle()), -turn, turn);
                        velocity = Vector2.FromAngle(angle) * velocity.Length();
                        position += velocity * step;
                        elapsed += step;
                    }
                    offset = position - origin + velocity * (duration - elapsed);
                    rotation = velocity.Angle();
                }
                if (bullet.Accel)
                {
                    float age = Read<float>(bullet, "_age"), delay = Read<float>(bullet, "_accelDelay");
                    float start = Read<float>(bullet, "_accelStartSpeed"), fast = Read<float>(bullet, "_fastSpeed");
                    float Travel(float at)
                    {
                        float seconds = bullet.AccelRampSeconds;
                        float ramp = Mathf.Clamp(at - delay, 0, seconds);
                        return start * at + (fast - start) * (ramp * ramp * ramp / (3 * seconds * seconds) + Math.Max(0, at - delay - seconds));
                    }
                    offset = Read<Vector2>(bullet, "_accelDir") * (Travel(age + duration) - Travel(age));
                }
                threats.Add((origin + offset, new Transform2D(-rotation, Vector2.Zero), bullet.Footprint!.Hull, bullet.Footprint.Bounds, sample));
            }
        }
        if (!ignoreCrossfirePreview && boss is BossMina && boss.GetChildren().OfType<BossCrossfire>().FirstOrDefault(a => a.WarningVisible) is { } preview)
        {
            var shape = BulletPool.BossHitShape(Job.Tank, boss.HasEvolved);
            float shotSpeed = (boss.HasEvolved ? 100f : 82f) * _game.BulletSpeedMul;
            for (int source = 0; source < preview.Origins.Count; source++)
            foreach (var shot in preview.Volley(source))
            {
                float launch = (float)preview.WarningRemaining + Mathf.Clamp(preview.Origins[source].DistanceTo(shot.Position) / Bullet.LeadSpeed, 0.12f, 0.32f);
                for (int sample = 0; sample < times.Length; sample++)
                    if (times[sample] >= launch) threats.Add((shot.Position + shot.Direction * shotSpeed * (times[sample] - launch),
                        new Transform2D(-shot.Direction.Angle(), Vector2.Zero), shape.Hull, shape.Bounds, sample));
            }
        }
        Vector2 best = Vector2.Zero;
        float bestScore = float.NegativeInfinity;
        Vector2 Clamp(Vector2 p) => new(Mathf.Clamp(p.X, global::Field.Left + 5, global::Field.Right - 5), Mathf.Clamp(p.Y, 5, global::Field.Bottom - 5));
        int directions = OS.GetCmdlineUserArgs().Contains("--rehearsal") ? 8 : precise ? 32 : 16;
        var positions = new Vector2[times.Length];
        var areas = _liveClear ? GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>()
            .Select(area => (Area: area, Remaining: (float)(Read<double>(area, "_warn") - Read<double>(area, "_t"))))
            .Where(area => area.Remaining <= 1.4f).ToArray() : Array.Empty<(AreaStrike Area, float Remaining)>();
        for (int candidate = -1; candidate < directions; candidate++)
        foreach (float stop in dash ? new[] { 0.8f } : new[] { 0.1f, 0.25f, 0.7f })
        {
            Vector2 direction = candidate < 0 ? Vector2.Zero : Vector2.FromAngle(Mathf.Tau * candidate / directions);
            Vector2 velocity = direction * (speed ?? player.SlowestMoveSpeed);
            Vector2 PositionAt(float time)
            {
                if (!dash) return Clamp(player.GlobalPosition + velocity * Math.Min(time, stop));
                float u = Mathf.Clamp((time - 0.05f) / 0.5f, 0, 1);
                return Clamp(player.GlobalPosition + direction * _game.DodgeDistance * (1 - (1 - u) * (1 - u))
                    + velocity * Math.Max(0, time - 0.55f));
            }
            float gap = 14;
            for (int sample = 0; sample < times.Length; sample++)
            {
                positions[sample] = PositionAt(times[sample]);
                gap = Math.Min(gap, positions[sample].DistanceTo(boss.ShotCenter) - 28);
            }
            // Circle bounds erase valid lanes between long, narrow projectiles.
            foreach (var threat in threats)
            {
                Vector2 p = threat.Rotation * (positions[threat.Sample] - threat.Position);
                if (!threat.Bounds.Grow(gap + Player.HitRadius).HasPoint(p)) continue;
                if (Geometry2D.IsPointInPolygon(p, threat.Hull)) { gap = -Player.HitRadius; break; }
                for (int i = 0; i < threat.Hull.Length; i++)
                    gap = Math.Min(gap, p.DistanceTo(Geometry2D.GetClosestPointToSegment(p, threat.Hull[i], threat.Hull[(i + 1) % threat.Hull.Length])) - Player.HitRadius);
            }
            foreach (var area in areas)
                if (area.Area.CoversPoint(PositionAt(Math.Max(0.02f, area.Remaining + 1f / 60)))) gap = -3;
            float score = Math.Min(gap, 14);
            // A nearby goal must never outrank a collision-free route.
            if (gap >= 2) score += 32 - Math.Min(24,
                Clamp(player.GlobalPosition + velocity * Math.Min(0.5f, stop)).DistanceTo(goal ?? global::Field.PlayerStart) * (goal.HasValue ? 0.08f : 0.025f));
            if (direction == previous) score += 0.08f;
            if (score > bestScore) { bestScore = score; best = direction; }
        }
        return best;
    }

    private async Task CheckAttackRhythm(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.SelectedJob = boss is BossMina ? Job.Magic : Job.Tank;
        GD.Print($"[RhythmQA] {scene}: unupgraded legal job movement={player.MoveSpeed:F1}px/s, focused={player.SlowestMoveSpeed:F1}px/s");
        var original = new Vector2(global::Field.BossCenterX, 100);
        _game.Difficulty = GameManager.Diff.Hard;
        foreach (bool horizontal in new[] { true, false })
        foreach (var corner in new[] { new Vector2(global::Field.Left + 4, 4), new Vector2(global::Field.Right - 4, 4),
            new Vector2(global::Field.Left + 4, global::Field.Bottom - 4), new Vector2(global::Field.Right - 4, global::Field.Bottom - 4),
            new Vector2(global::Field.Left, global::Field.Top), new Vector2(global::Field.Right, global::Field.Top),
            new Vector2(global::Field.Left, global::Field.Bottom), new Vector2(global::Field.Right, global::Field.Bottom) })
        {
            Pool.DespawnAll();
            player.Position = corner;
            var attack = BossCrossfire.Begin(boss, true, horizontal);
            attack.Tick(attack.WarningRemaining + 0.001);
            var first = Hostile();
            Check(first.Any(b => b.BossAimTarget == corner && b.Velocity.Normalized().Dot(Read<Vector2>(b, "_leadTo").DirectionTo(corner)) > 0.9999f),
                scene + ": edge positions remain aimed at by a warned band");
            float clearance = first.SelectMany((a, i) => first.Skip(i + 1).Select(b =>
                BossCharacterShotQa.Clearance(a, b, Read<Vector2>(a, "_leadTo"), Read<Vector2>(b, "_leadTo")))).Min();
            Check(clearance >= BulletPool.BossLaneGap && first.All(b => b.LeadIn && b.CollisionLayer == 0),
                scene + ": pressure-band launch spacing respects the player core");
            attack.Cancel();
        }
        var hardRhythm = new Dictionary<bool, (double Warning, double Interval, int Waves)>();
        foreach (bool charged in new[] { false, true })
        foreach (var difficulty in new[] { GameManager.Diff.Hard, GameManager.Diff.Lunatic })
        {
            _game.Difficulty = difficulty;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            boss.Position = original;
            player.Position = global::Field.PlayerStart;
            Pool.DespawnAll();
            var sequence = BossCrossfire.Begin(boss);
            bool axis = sequence.Horizontal;
            double warning = sequence.WarningRemaining;
            double interval = 0;
            for (int wave = 0; wave < sequence.Burst; wave++)
            {
                var aimedBefore = Hostile().Where(b => b.BossAimTarget.HasValue).ToHashSet();
                Vector2 aimedAt = sequence.Target;
                var aimedPlan = Enumerable.Range(0, sequence.Origins.Count).SelectMany(sequence.Volley).Where(s => s.Centered).ToArray();
                sequence.Tick(sequence.WarningRemaining + 0.001);
                if (wave == 0) interval = sequence.WarningRemaining;
                if (boss is BossMina)
                {
                    var fired = Hostile().Where(b => b.BossAimTarget.HasValue && !aimedBefore.Contains(b)).ToArray();
                    Check(fired.Length == aimedPlan.Length,
                        "Mina: curtain and crossing rows retain every committed aimed lane");
                    Check(aimedPlan.All(slot => fired.Any(b => Read<Vector2>(b, "_leadTo") == slot.Position
                        && b.Velocity.Normalized().Dot(slot.Direction) > 0.9999f)),
                        "Mina: each volley uses its previously warned aim");
                    if (wave < sequence.Burst - 1)
                        Check(sequence.Target == (difficulty == GameManager.Diff.Lunatic && wave == sequence.Burst - 2 ? player.Position : aimedAt),
                            "Mina: Hard holds the burst target while Lunatic re-aims its closing volley");
                    if (difficulty == GameManager.Diff.Lunatic && wave == sequence.Burst - 2)
                        Check(sequence.WarningRemaining >= 0.8, "Mina: the closing aim change has a visible reaction beat");
                    player.Position += new Vector2(0, 5);
                }
            }
            if (difficulty == GameManager.Diff.Hard) hardRhythm[charged] = (warning, interval, Read<int>(sequence, "_waves"));
            else Check(warning < hardRhythm[charged].Warning && interval < hardRhythm[charged].Interval
                && Read<int>(sequence, "_waves") > hardRhythm[charged].Waves,
                scene + ": Lunatic shortens the warning and cadence and adds a full attack group");
            Check(sequence.Horizontal != axis && sequence.WarningRemaining > interval,
                $"{scene}/{difficulty}/{charged}: a burst ends with a warned axis change and a reversal beat");
            int bullets = Hostile().Length;
            var target = sequence.Target;
            var bossBefore = boss.Position;
            Hud.BubblePaused = true;
            sequence.Tick(1);
            Check(sequence.Target == target && sequence.Wave == sequence.Burst && boss.Position == bossBefore,
                scene + ": dialogue freezes relocation as well as firing");
            Hud.BubblePaused = false;
            sequence.Tick(0.5);
            Check(Hostile().Length == bullets && boss.Position.DistanceTo(bossBefore) <= 12.01f,
                scene + ": relocation neither fires early nor teleports the boss");
            sequence.Cancel();

            foreach (bool horizontal in new[] { true, false })
            {
                Pool.DespawnAll();
                boss.Position = original;
                player.Position = global::Field.PlayerStart;
                var rotation = new BossDanmaku(boss);
                Write(rotation, "_phrase", horizontal ? 3 : 7);
                rotation.Tick(0.71);
                var scheduled = Read<BossCrossfire>(rotation, "_crossfire");
                Check(scheduled.Sweep && scheduled.Horizontal == horizontal,
                    scene + ": horizontal and vertical pressure bands are in the real rotation");
                scheduled.Cancel();
                Vector2 start = new(global::Field.Left + global::Field.Width * (charged ? 0.74f : 0.26f),
                    global::Field.Height * (charged ? 0.26f : 0.74f));
                bool staticHit = false, escaped = false;
                float crossed = 0;
                int peak = 0;
                foreach (int route in new[] { 0 }.Concat(Enumerable.Range(46, 32)).Concat(new[] { 9, 10, 11, 1, 2, 3, 4, 5, 6, 7, 8 })
                    .Concat(Enumerable.Range(14, 32)).Concat(new[] { 12, 13 }).Concat(Enumerable.Range(78, 32)))
                {
                    if (escaped) break;
                    bool dodging = route is 12 or 13 || route >= 78;
                    if (dodging && difficulty != GameManager.Diff.Lunatic) continue;
                    _game.TrainingSetUpgrade("n_dodge", dodging);
                    Write(player, "_dodgeCd", 0f);
                    Write(player, "_dodgeInv", 0f);
                    Call(player, "EndDodge");
                    bool readPattern = route > 0;
                    Write(player, "_locked", route < 4);
                    float moveSpeed = route >= 4 ? player.MoveSpeed : player.SlowestMoveSpeed;
                    Pool.DespawnAll();
                    boss.Position = original;
                    player.Position = start;
                    var trial = BossCrossfire.Begin(boss, true, horizontal);
                    var direction = Vector2.Zero;
                    Vector2 goal = start;
                    if (horizontal) goal.Y = global::Field.Height * (trial.Target.Y < global::Field.CenterY ? 0.82f : 0.18f);
                    else goal.X = global::Field.Left + global::Field.Width * (trial.Target.X < global::Field.CenterX ? 0.82f : 0.18f);
                    if (route == 2) { if (horizontal) goal.X = global::Field.CenterX; else goal.Y = global::Field.Height * 0.65f; }
                    float min = horizontal ? player.Position.Y : player.Position.X, max = min;
                    bool hit = false;
                    for (int frame = 0; frame < 2400; frame++)
                    {
                        if (readPattern && frame > (route is >= 9 and <= 11 ? 90 + (route - 9) * 30 : 21))
                        {
                            goal = player.Position;
                            if (trial.Horizontal) goal.Y = global::Field.Height * (trial.Target.Y < global::Field.CenterY ? 0.82f : 0.18f);
                            else goal.X = global::Field.Left + global::Field.Width * (trial.Target.X < global::Field.CenterX ? 0.82f : 0.18f);
                            if (route == 2) { if (trial.Horizontal) goal.X = global::Field.CenterX; else goal.Y = global::Field.Height * 0.65f; }
                            if (route is 6 or 7)
                            {
                                float side = route == 6 ? 0.35f : 0.65f;
                                if (trial.Horizontal) goal.X = global::Field.Left + global::Field.Width * side;
                                else goal.Y = global::Field.Height * side;
                            }
                            if (route is >= 14 and < 46)
                            {
                                int path = route - 14;
                                float angle = frame / 60f * (0.35f + 0.1f * (path / 8)) * (path % 2 == 0 ? 1 : -1) + path % 4 * Mathf.Pi / 2;
                                goal = new Vector2(global::Field.CenterX + Mathf.Cos(angle) * 112, global::Field.CenterY + Mathf.Sin(angle) * 80);
                            }
                            Vector2? routeGoal = route >= 46 ? BarrageRouteGoal(player, trial, (route - 46) % 32 + 1, frame) : goal;
                            if (route >= 46 ? frame % 3 == 0 : route is 3 or 4 || route >= 6 || frame % 3 == 1)
                                direction = ShapeAwareDodge(player, boss, direction, routeGoal, moveSpeed, longLookahead: route == 5, precise: route is 3 or 4 || route >= 6);
                            if (!dodging) player.Position = new Vector2(
                                Mathf.Clamp(player.Position.X + direction.X * moveSpeed / 60, global::Field.Left + 6, global::Field.Right - 6),
                                Mathf.Clamp(player.Position.Y + direction.Y * moveSpeed / 60, 6, global::Field.Bottom - 6));
                        }
                        if (dodging)
                        {
                            AutoplayInput.Move(direction);
                            Input.FlushBufferedEvents();
                            StepDodgePilot(player, route == 12 ? Vector2.Zero : direction, route >= 78 ? boss : null);
                        }
                        trial.Tick(1d / 60);
                        StepBullets();
                        peak = Math.Max(peak, Hostile().Length);
                        float at = horizontal ? player.Position.Y : player.Position.X;
                        min = Math.Min(min, at); max = Math.Max(max, at);
                        hit |= Read<float>(player, "_dodgeInv") <= 0 && (Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position))
                            || player.Position.DistanceTo(boss.ShotCenter) < 28);
                        if (hit && readPattern) GD.Print($"[RhythmQA] contact frame={frame} wave={trial.Wave} player={player.Position} boss={boss.ShotCenter} target={trial.Target}");
                        if (readPattern && hit || trial.Finished && Hostile().Length == 0) break;
                    }
                    if (readPattern)
                    {
                        crossed = max - min;
                        escaped = !hit && trial.Finished && Hostile().Length == 0
                            && crossed >= (horizontal ? global::Field.Height : global::Field.Width) * 0.45f;
                    }
                    else staticHit = hit;
                    GD.Print($"[RhythmQA] candidate={route} escaped={escaped}");
                    trial.Cancel();
                }
                GD.Print($"[RhythmQA] {scene}/{difficulty}/{charged} horizontal={horizontal} static_hit={staticHit} route={escaped} travel={crossed:F1} peak={peak}");
                _game.TrainingSetUpgrade("n_dodge", false);
                Check(staticHit, scene + ": camping is threatened by the pressure band");
                Check(escaped && crossed >= (horizontal ? global::Field.Height : global::Field.Width) * 0.45f,
                    scene + ": a warned opposite-side route survives without bombs (dodge allowed only on Lunatic)");
                Write(player, "_locked", false);
                await Frames(2);
                hud.HideBubble();
            }
        }
        Pool.DespawnAll();
        player.Position = global::Field.PlayerStart;
        boss.Position = original;
        var interrupted = new BossDanmaku(boss);
        Write(interrupted, "_phrase", 1);
        interrupted.Tick(0.71);
        interrupted.Tick(2);
        Call(boss, "EnterBreak");
        hud.HideBubble();
        interrupted.Tick(0.1);
        Check(!interrupted.CrossfireActive && Read<int>(interrupted, "_phrase") == 2,
            scene + ": breaking an attack advances the rotation instead of repeating it forever");
        interrupted.Tick(1);
        Check(Read<int>(interrupted, "_phrase") == 2, scene + ": the break advances the rotation only once");
        Pool.DespawnAll();
    }

    private async Task SaveRhythmFrames(Enemy boss, Player player, Hud hud, string scene)
    {
        bool lunatic = OS.GetCmdlineUserArgs().Contains("--lunatic");
        _game.Difficulty = lunatic ? GameManager.Diff.Lunatic : GameManager.Diff.Hard;
        if (boss is BossMina) _game.SelectedJob = Job.Magic;
        Engine.TimeScale = 1;
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        hud.HideSpellCard();
        Write(hud, "_bannerTimer", 0d);
        Write(hud, "_cutinTimer", 0d);
        Write(hud, "_bossLineTimer", 0d);
        string folder = ProjectSettings.GlobalizePath("res://build/qa_story/pressure_designs_2_059" + (lunatic ? "_lunatic" : ""));
        DirAccess.MakeDirRecursiveAbsolute(folder);
        for (int mode = 0; mode < 6; mode++)
        {
            Pool.DespawnAll();
            player.Position = global::Field.PlayerStart;
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            var attack = BossCrossfire.Begin(boss, mode is 1 or 2, mode == 1, weave: mode == 3, pincer: mode >= 4, diagonal: mode == 5);
            for (int frame = 0; frame < 360; frame++) { attack.Tick(1d / 60); StepBullets(); }
            await Frames(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screenshot = GetViewport().GetTexture().GetImage();
            Check(screenshot.SavePng($"{folder}/{scene}_{mode}.png") == Error.Ok, scene + ": rhythm screenshot saved");
            attack.Cancel();
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private static Vector2? BarrageRouteGoal(Player player, BossCrossfire attack, int path, int frame)
    {
        Vector2? goal = path <= 2 ? null : new Vector2(
            global::Field.Left + global::Field.Width * (path % 2 == 0 ? 0.7f : 0.3f),
            global::Field.Height * (path < 5 ? 0.3f : 0.7f));
        if (attack.Weave && path > 2) goal = attack.Horizontal
            ? new Vector2(player.Position.X, attack.WeavePassage) : new Vector2(attack.WeavePassage, player.Position.Y);
        if (path >= 8)
        {
            float rate = path < 12 ? 0.6f : path < 16 ? 0.9f : 0.35f + 0.1f * ((path - 16) / 4);
            float width = path < 16 ? 104 : path < 24 ? 80 : 112;
            float height = path < 16 ? 65 : path < 24 ? 50 : 80;
            float angle = frame / 60f * rate * (path % 2 == 0 ? 1 : -1) + (path % 4) * Mathf.Pi / 2;
            goal = new Vector2(global::Field.CenterX + Mathf.Cos(angle) * width, global::Field.CenterY + Mathf.Sin(angle) * height);
        }
        return goal;
    }

    private async Task CheckNaiveRoutes(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.SelectedJob = boss is BossMina ? Job.Magic : Job.Tank;
        _game.Difficulty = GameManager.Diff.Hard;
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        var corners = new[] { new Vector2(global::Field.Left + 28, 28), new Vector2(global::Field.Right - 28, 28),
            new Vector2(global::Field.Right - 28, global::Field.Bottom - 28), new Vector2(global::Field.Left + 28, global::Field.Bottom - 28) };
        foreach (bool charged in new[] { false, true })
        foreach (int mode in new[] { 0, 1, 2, 3, 4 })
        {
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            int failed = 0;
            for (int route = 0; route < 7; route++)
            {
                Pool.DespawnAll();
                boss.Position = new Vector2(global::Field.BossCenterX, 100);
                player.Position = global::Field.PlayerStart;
                var attack = BossCrossfire.Begin(boss, sweep: mode == 1, weave: mode == 2, pincer: mode is 0 or 3, diagonal: mode == 3);
                int waypoint = 0, contacts = 0, firstHit = -1;
                bool touching = false;
                for (int frame = 0; frame < 2400; frame++)
                {
                    Vector2 goal = player.Position;
                    float time = frame / 60f;
                    if (route == 1) goal = new Vector2(global::Field.PlayerStart.X, global::Field.CenterY + Mathf.Sin(time * 0.7f) * 80);
                    if (route == 2) goal = new Vector2(global::Field.CenterX + Mathf.Sin(time * 0.5f) * 120, global::Field.CenterY);
                    if (route is 3 or 4) goal = new Vector2(global::Field.CenterX + Mathf.Cos(time * 0.6f * (route == 3 ? 1 : -1) + Mathf.Pi) * 112,
                        global::Field.CenterY + Mathf.Sin(time * 0.6f * (route == 3 ? 1 : -1) + Mathf.Pi) * 80);
                    if (route == 5)
                    {
                        goal = corners[waypoint % corners.Length];
                        if (player.Position.DistanceTo(goal) < 2) waypoint++;
                    }
                    if (route == 6) goal = (boss.ShotCenter + Vector2.Left * 100).Clamp(corners[0], corners[2]);
                    player.Position = player.Position.MoveToward(goal, player.MoveSpeed / 60);
                    attack.Tick(1d / 60);
                    StepBullets();
                    bool hit = Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position))
                        || player.Position.DistanceTo(boss.ShotCenter) < 28;
                    if (hit && !touching) contacts++;
                    if (hit && firstHit < 0) firstHit = frame;
                    touching = hit;
                    if (attack.Finished && Hostile().Length == 0) break;
                }
                if (contacts > 0) failed++;
                Check(attack.Finished && Hostile().Length == 0, scene + ": naive-route trial drains all projectiles");
                GD.Print($"[NaiveQA] {scene}/{charged}/family={mode}/route={route}: contacts={contacts} first_hit={firstHit / 60d:F2}s");
                attack.Cancel();
            }
            Check(failed == 7, $"{scene}/{charged}/family={mode}: all seven unreactive movement routes are punished ({failed}/7)");
            for (int column = 0; column < 5; column++)
            for (int row = 0; row < 5; row++)
            {
                Pool.DespawnAll();
                boss.Position = new Vector2(global::Field.BossCenterX, 100);
                player.Position = new Vector2(global::Field.Left + global::Field.Width * column / 4,
                    global::Field.Top + global::Field.Height * row / 4);
                var attack = BossCrossfire.Begin(boss, sweep: mode == 1, weave: mode == 2, pincer: mode is 0 or 3, diagonal: mode == 3);
                bool hit = false;
                for (int frame = 0; frame < 1800 && !hit; frame++)
                {
                    attack.Tick(1d / 60);
                    StepBullets();
                    hit = Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position));
                    if (attack.Finished && Hostile().Length == 0) break;
                }
                Check(hit, $"{scene}/{charged}/family={mode}: projectiles reach the stationary core at grid ({column}, {row})");
                attack.Cancel();
            }
            await Frames(1);
            hud.HideBubble();
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private async Task CheckPressure(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.SelectedJob = boss is BossMina ? Job.Magic : Job.Tank;
        bool rehearsal = OS.GetCmdlineUserArgs().Contains("--rehearsal");
        bool movementUpgrade = OS.GetCmdlineUserArgs().Contains("--movement-upgrade");
        _game.TrainingSetUpgrade("n_move_15x", movementUpgrade);
        _game.TrainingSetUpgrade("n_dodge_cd", OS.GetCmdlineUserArgs().Contains("--dodge-upgrade"));
        bool locked = OS.GetCmdlineUserArgs().Contains("--locked-routes");
        Write(player, "_locked", locked);
        Write(player, "_lockArmed", locked);
        Write(player, "_lockByShift", false);
        if (locked) Write(player, "_lockTarget", boss);
        GD.Print($"[PressureQA] {scene}: job={_game.SelectedJob} movement_upgrade={movementUpgrade} locked={locked} move_speed={player.MoveSpeed:F1}px/s dodge_distance={_game.DodgeDistance} cooldown={_game.DodgeCooldown}");
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        var original = new Vector2(global::Field.BossCenterX, 100);
        foreach (bool charged in new[] { false, true })
        {
            if (OS.GetCmdlineUserArgs().Contains("--evolved-only") && !charged) continue;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            var densities = new List<int>();
            foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
            {
                if (difficulty == GameManager.Diff.Normal) continue;
                _game.Difficulty = difficulty;
                boss.Position = original;
                player.Position = global::Field.PlayerStart;
                Pool.DespawnAll();
                var rotation = new BossDanmaku(boss);
                rotation.Tick(0.71);
                var attack = Read<BossCrossfire>(rotation, "_crossfire");
                Check(attack.Pincer, scene + ": the opening uses remote emitters instead of an expanding safe ring");
                var edges = new HashSet<Vector2>();
                for (int wave = 0; wave < Math.Min(12, Read<int>(attack, "_waves")); wave++)
                {
                    var plan = Enumerable.Range(0, attack.Origins.Count).SelectMany(attack.Volley).ToArray();
                    foreach (var shot in plan) edges.Add(shot.Direction);
                    Pool.DespawnAll();
                    attack.Tick(attack.WarningRemaining + 0.001);
                    var bullets = Hostile();
                    Check(plan.Length == bullets.Length && plan.All(slot => bullets.Count(b => Read<Vector2>(b, "_leadTo") == slot.Position) == 1),
                        scene + ": every announced remote lane fires exactly once");
                    Check(bullets.Count(b => b.BossAimTarget.HasValue) == plan.Count(slot => slot.Centered),
                        scene + ": every emitter aims a lane through the captured player position");
                    float clearance = bullets.SelectMany((a, i) => bullets.Skip(i + 1).Select(b =>
                        BossCharacterShotQa.Clearance(a, b, Read<Vector2>(a, "_leadTo"), Read<Vector2>(b, "_leadTo")))).Min();
                    Check(clearance >= BulletPool.BossLaneGap, scene + ": remote lanes preserve core-width launch clearance");
                    player.Position += new Vector2(3, -2);
                }
                Check(edges.Count >= (difficulty == GameManager.Diff.Easy ? 3 : 4), scene + ": emitters rotate around the arena");
                attack.Cancel();
                Pool.DespawnAll();
                player.Position = global::Field.PlayerStart;
                boss.Position = original;
                rotation = new BossDanmaku(boss);
                var seen = new HashSet<(ulong, ulong)>();
                int peak = 0, lastShot = -1, longestGap = 0, arrivals = 0;
                bool wasThreatened = false;
                for (int frame = 0; frame < 1200; frame++)
                {
                    rotation.Tick(1d / 60);
                    bool fired = false;
                    foreach (var bullet in Hostile()) fired |= seen.Add((bullet.GetInstanceId(), bullet.ActivationId));
                    if (fired)
                    {
                        if (lastShot >= 0) longestGap = Math.Max(longestGap, frame - lastShot);
                        lastShot = frame;
                    }
                    StepBullets();
                    var shots = Hostile();
                    peak = Math.Max(peak, shots.Length);
                    bool threatened = shots.Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position));
                    if (threatened && !wasThreatened) arrivals++;
                    wasThreatened = threatened;
                }
                densities.Add(seen.Count);
                GD.Print($"[PressureQA] {scene}/{difficulty}/{charged}: shots/20s={seen.Count} peak={peak} max_launch_gap={longestGap / 60d:F2}s aimed_arrivals={arrivals}");
                if (difficulty >= GameManager.Diff.Hard)
                    Check(longestGap <= 90 && arrivals >= 4 && peak >= 70,
                        scene + ": hard pressure has no long launch gaps and repeatedly threatens camping");
                if (Read<BossCrossfire?>(rotation, "_crossfire") is { } remaining) remaining.Cancel();
                await Frames(1);
            }
            Check(densities.Zip(densities.Skip(1), (a, b) => b > a).All(increasing => increasing),
                scene + ": real rotation density increases across all four difficulties");
            foreach (int mode in new[] { 0, 1, 2, 3, 4 })
            {
                string? family = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--family="));
                if (family != null && family != "--family=" + mode) continue;
                bool lunatic = OS.GetCmdlineUserArgs().Contains("--lunatic-routes");
                _game.Difficulty = lunatic ? GameManager.Diff.Lunatic
                    : OS.GetCmdlineUserArgs().Contains("--easy-routes") ? GameManager.Diff.Easy
                    : OS.GetCmdlineUserArgs().Contains("--normal-routes") ? GameManager.Diff.Normal
                    : GameManager.Diff.Hard;
                _game.TrainingSetUpgrade("n_dodge", lunatic);
                bool escaped = false;
                for (int route = 0; route <= (lunatic ? 128 : rehearsal ? 32 : 15) && !escaped; route++)
                {
                    if (lunatic && route is > 0 and <= 32 && OS.GetCmdlineUserArgs().Contains("--dodge-routes")) continue;
                    int path = route > 0 ? (route - 1) % 32 + 1 : 0;
                    bool dodging = route > 32;
                    Write(player, "_dodgeCd", 0f);
                    Write(player, "_dodgeInv", 0f);
                    Call(player, "EndDodge");
                    Pool.DespawnAll();
                    boss.Position = original;
                    player.Position = global::Field.PlayerStart;
                    var attack = BossCrossfire.Begin(boss, sweep: mode == 1, weave: mode == 2, pincer: mode is 0 or 3, diagonal: mode == 3);
                    Vector2 direction = Vector2.Zero;
                    var inputs = new List<Vector2>();
                    bool hit = false;
                    int end = 0;
                    for (int frame = 0; frame < 1800; frame++)
                    {
                        end = frame;
                        if (route > 0 && frame >= 21)
                        {
                            Vector2? goal = BarrageRouteGoal(player, attack, path, frame);
                            if (frame % (rehearsal ? 6 : lunatic && boss is BossMina ? 1 : 3) == 0) direction = ShapeAwareDodge(player, boss, direction, goal,
                                player.MoveSpeed, longLookahead: path == 2, precise: path >= 3);
                            if (dodging)
                            {
                                AutoplayInput.Move(direction);
                                Input.FlushBufferedEvents();
                                StepDodgePilot(player, route is > 64 and <= 96 ? Vector2.Zero : direction, route > 96 ? boss : null);
                            }
                            else player.Position = (player.Position + direction * player.MoveSpeed / 60).Clamp(
                                    new Vector2(global::Field.Left + 6, 6), new Vector2(global::Field.Right - 6, global::Field.Bottom - 6));
                        }
                        inputs.Add(direction);
                        attack.Tick(1d / 60);
                        StepBullets();
                        hit |= Read<float>(player, "_dodgeInv") <= 0 && (Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position))
                            || player.Position.DistanceTo(boss.ShotCenter) < 28);
                        if (hit || attack.Finished && Hostile().Length == 0) break;
                    }
                    escaped = route > 0 && !hit && attack.Finished && Hostile().Length == 0;
                    if (route == 0) Check(hit, scene + ": standing still cannot survive any challenge attack family");
                    GD.Print($"[PressureQA] {scene}/{_game.Difficulty}/{charged}/mode={mode}: route={route} escaped={escaped} dodge={dodging} end={end / 60d:F2}s position={player.Position}");
                    attack.Cancel();
                    if (escaped && rehearsal && !dodging)
                    {
                        // Replay learned keyboard inputs, with no bullet prediction or collision avoidance.
                        for (int repeat = 0; repeat < 2; repeat++)
                        {
                            Pool.DespawnAll();
                            boss.Position = original;
                            player.Position = global::Field.PlayerStart;
                            var replay = BossCrossfire.Begin(boss, sweep: mode == 1, weave: mode == 2, pincer: mode is 0 or 3, diagonal: mode == 3);
                            foreach (var input in inputs)
                            {
                                player.Position = (player.Position + input * player.MoveSpeed / 60).Clamp(
                                    new Vector2(global::Field.Left + 6, 6), new Vector2(global::Field.Right - 6, global::Field.Bottom - 6));
                                replay.Tick(1d / 60);
                                StepBullets();
                                CheckReplayContact(boss, player, scene, mode);
                            }
                            Check(replay.Finished && Hostile().Length == 0, $"{scene}/{_game.Difficulty}/{charged}/{mode}: learned 8-way, 10Hz inputs replay {repeat + 1} without damage");
                            replay.Cancel();
                        }
                    }
                    await Frames(1);
                }
                Check(escaped, scene + ": a legal-speed route exists without bombs; dodge allowed only on Lunatic");
                _game.TrainingSetUpgrade("n_dodge", false);
                AutoplayInput.Move(Vector2.Zero);
                Input.FlushBufferedEvents();
            }
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
        await Frames(2);
    }

    private void CheckReplayContact(Enemy boss, Player player, string scene, int mode)
    {
        if (Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position))
            || player.Position.DistanceTo(boss.ShotCenter) < 28)
            throw new Exception($"{scene}/{mode}: learned replay collided at {player.Position}");
    }

    private void CheckAnswerTiming(Enemy boss, Player player, string scene)
    {
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            _game.Difficulty = difficulty;
            player.Position = global::Field.PlayerStart;
            var attack = BossCrossfire.Begin(boss, pincer: true);
            Vector2 captured = attack.Target;
            player.Position += new Vector2(20, 16);
            attack.Tick(attack.WarningRemaining + 0.001);
            Check(attack.Target == captured, scene + ": the second pincer beat does not re-aim into the first dodge");
            attack.Cancel();
            Pool.DespawnAll();
        }
        var caster = boss.GetChildren().OfType<AreaSpellCaster>().FirstOrDefault();
        if (caster != null)
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        foreach (Job job in Enum.GetValues<Job>())
        {
            _game.Difficulty = difficulty;
            _game.SelectedJob = job;
            foreach (var start in new[] { new Vector2(global::Field.Left + 4, 4), new Vector2(global::Field.Right - 4, global::Field.Bottom - 4) })
            {
                player.Position = start;
                Vector2 safe = new(global::Field.CenterX, global::Field.CenterY);
                Call(caster, "SpawnFullscreenStrike", safe, 17f, 1f);
                var strike = Read<AreaStrike>(caster, "_aoeStrike");
                double warning = Read<double>(strike, "_warn");
                Vector2 arrival = start.MoveToward(safe, (float)(warning - 0.35) * player.SlowestMoveSpeed);
                Check(arrival.DistanceTo(safe) <= 17 - Player.HitRadius - 3.9f,
                    $"{scene}/{difficulty}/{job}: fullscreen destination is reachable after reaction at locked speed");
                strike.Free();
            }
        }
        _game.SelectedJob = boss is BossMina ? Job.Magic : Job.Tank;
        player.Position = global::Field.PlayerStart;
    }

    private void CheckAreaAnswers(Enemy boss, Player player, string scene)
    {
        var caster = boss.GetChildren().OfType<AreaSpellCaster>().FirstOrDefault();
        if (caster == null) return;
        _game.SelectedJob = Job.Tank;
        Check(Mathf.IsEqualApprox(player.SlowestMoveSpeed, 52.8f), "area routes use the slowest unupgraded locked movement");
        Write(caster, "_owner", boss);
        for (int row = 0; row < 5; row++)
        for (int column = 0; column < 9; column++)
        {
            player.Position = new Vector2(Mathf.Lerp(global::Field.Left + 4, global::Field.Right - 4, column / 8f),
                Mathf.Lerp(4, global::Field.Bottom - 4, row / 4f));
            if (player.Position.DistanceTo(boss.ShotCenter) < 32) continue;
            Vector2 safe = (Vector2)Call(caster, "PickSafeNearPlayer", 44f)!;
            Check(safe.DistanceTo(player.Position) >= 70 && safe.DistanceTo(boss.ShotCenter) >= 60
                && Geometry2D.GetClosestPointToSegment(boss.ShotCenter, player.Position, safe).DistanceTo(boss.ShotCenter) >= 31.99f,
                scene + ": relay destination and approach avoid body contact without a stationary safe fallback");
        }
        var lessons = new Dictionary<(AreaStrike.Shape, int), Vector2>();
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        foreach (var shape in new[] { AreaStrike.Shape.Circle, AreaStrike.Shape.Rect, AreaStrike.Shape.BeamH, AreaStrike.Shape.BeamV, AreaStrike.Shape.BeamSeg })
        for (int seed = 0; seed < 12; seed++)
        {
            _game.Difficulty = difficulty;
            Read<RandomNumberGenerator>(caster, "_rng").Seed = (ulong)seed;
            player.Position = new[] { global::Field.PlayerStart,
                new Vector2(global::Field.Left + 4, 4), new Vector2(global::Field.Right - 4, 4),
                new Vector2(global::Field.Left + 4, global::Field.Bottom - 4), new Vector2(global::Field.Right - 4, global::Field.Bottom - 4) }[seed % 5];
            Write(caster, "_pendShape", shape);
            Write(caster, "_castTarget", player.Position);
            Call(caster, "SpawnTelegraphs");
            var strikes = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().Where(s => !s.IsQueuedForDeletion()).ToArray();
            if (strikes.Length == 0 || !new BossDanmaku(boss).Holding)
                throw new Exception(scene + ": AOE warnings must reserve their native-fire and body-movement window");
            bool escaped = false;
            for (int radius = 4; radius <= 384 && !escaped; radius += 2)
            for (int direction = 0; direction < 32 && !escaped; direction++)
            {
                Vector2 goal = player.Position + Vector2.FromAngle(direction * Mathf.Tau / 32) * radius;
                if (!global::Field.Rect.Grow(-3).HasPoint(goal)) continue;
                if (Geometry2D.GetClosestPointToSegment(boss.ShotCenter, player.Position, goal).DistanceTo(boss.ShotCenter) < 28) continue;
                escaped = strikes.All(s => !s.CoversPoint(player.Position.MoveToward(goal,
                    Mathf.Max(0, (float)Read<double>(s, "_warn") - 0.4f) * player.SlowestMoveSpeed)));
                if (escaped && difficulty == GameManager.Diff.Easy) lessons[(shape, seed)] = goal;
            }
            if (!escaped) throw new Exception($"{scene}/{difficulty}/{shape}/seed={seed}: no straight, reaction-delayed AOE exit at focused speed from {player.Position}");
            if (difficulty is GameManager.Diff.Normal or GameManager.Diff.Hard)
                Check(strikes.All(s => !s.CoversPoint(player.Position.MoveToward(lessons[(shape, seed)],
                    Mathf.Max(0, (float)Read<double>(s, "_warn") - 0.4f) * player.SlowestMoveSpeed))),
                    $"{scene}/{difficulty}/{shape}/{seed}: EASY area escape transfers with the visible warning");
            foreach (var strike in strikes) strike.Free();
            caster.CancelPendingAttacks();
        }
        GD.Print($"[AnswerQA] {scene}: 240 area placements have an in-field, body-clear exit after 0.4s reaction");
    }

    private void CheckLearnedFullscreen(Enemy boss, Player player, string scene)
    {
        _game.SelectedJob = Job.Tank;
        if (boss is BossMina mina)
        {
            CheckLearnedMinaAreas(mina, player);
            return;
        }
        var caster = boss.GetChildren().OfType<AreaSpellCaster>().First();
        var lessons = new Dictionary<(int, bool, int), Vector2>();
        for (int side = 0; side < 2; side++)
        foreach (bool chain in new[] { false, true })
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            _game.Difficulty = difficulty;
            boss.Position = new Vector2(side == 0 ? global::Field.Right - 44 : global::Field.Left + 44, 108);
            player.Position = new Vector2(side == 0 ? global::Field.Left + 24 : global::Field.Right - 24, 108);
            caster.RequestFinale();
            var remaining = Pool.Spawn(player.Position + Vector2.Up * 30, Vector2.Right * 60, true);
            Call(caster, "TickFullscreen", 10d);
            Check(caster.AoeActive && Read<bool>(caster, "_aoePending")
                && !GetTree().GetNodesInGroup("aoe").Any(n => !n.IsQueuedForDeletion()),
                $"{scene}/{difficulty}: fullscreen reserves its turn until residual bullets leave");
            Pool.Despawn(remaining);
            for (int hop = 0; hop < (difficulty == GameManager.Diff.Easy ? chain ? 3 : 1 : 4); hop++)
            {
                Call(caster, "TickFullscreen", 1d);
                var strike = Read<AreaStrike>(caster, "_aoeStrike");
                var safe = Read<Vector2>(strike, "_safeCenter");
                var key = (side, chain, hop);
                if (difficulty is GameManager.Diff.Easy or GameManager.Diff.Normal) lessons[key] = safe;
                if (difficulty != GameManager.Diff.Lunatic)
                    Check(safe == lessons[key], $"{scene}/{difficulty}: fixed fullscreen lesson {side}/{chain}/{hop}");
                Vector2 reached = player.Position.MoveToward(safe,
                    ((float)Read<double>(strike, "_warn") - 0.4f) * player.SlowestMoveSpeed);
                Check(!(bool)Call(strike, "Inside", reached)!
                    && Geometry2D.GetClosestPointToSegment(boss.ShotCenter, player.Position, reached).DistanceTo(boss.ShotCenter) >= 32,
                    "learned fullscreen path fits the windup without body contact");
                if (difficulty != GameManager.Diff.Easy)
                    Check(strike.CoversPoint(player.Position), "each fullscreen wave closes the previous safe position");
                player.Position = safe;
                strike.Free();
            }
            caster.CancelPendingAttacks();
            caster.RequestFinale();
            Call(caster, "TickFullscreen", 1d);
            var cancelled = Read<AreaStrike>(caster, "_aoeStrike");
            caster.CancelPendingAttacks();
            Check(cancelled.IsQueuedForDeletion() && !caster.AoeActive,
                $"{scene}/{difficulty}: interrupt removes the live fullscreen warning and its pending hops");
            cancelled.Free();
        }
    }

    private async Task CheckEdgeLessons(Enemy boss, Player player, Hud hud, string scene)
    {
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        string key = scene switch { "Akari" => "akari", "Koharu" => "koharu", "Rei" => "rei", _ => "mina" };
        foreach (var difficulty in new[] { GameManager.Diff.Easy, GameManager.Diff.Normal, GameManager.Diff.Hard })
        for (int sequence = 0; sequence < 4; sequence++)
        foreach (var start in new[] { global::Field.PlayerStart, new Vector2(global::Field.Left + 4, 4),
            new Vector2(global::Field.Right - 4, global::Field.Bottom - 4) })
        {
            _game.Difficulty = difficulty;
            player.Position = start;
            Pool.DespawnAll();
            boss.Position = new Vector2(start.X < global::Field.CenterX ? 340 : 110, 100);
            var attack = BossEdgeVolley.Begin(boss, key, sequence);
            attack.ProcessMode = ProcessModeEnum.Disabled;
            if (attack.Sequence is { } moving)
            {
                CheckMovingSequence(moving, player, boss, attack._PhysicsProcess, $"{scene}/{difficulty}/edge-{sequence}/{start}");
                attack.Cancel();
                await Frames(1);
                hud.HideBubble();
                continue;
            }
            bool vertical = attack.Side is BossEdgeVolley.Edge.Top or BossEdgeVolley.Edge.Bottom;
            float sidestep = Read<float>(attack, "_spacing") * 0.5f;
            Vector2 goal = start + (vertical ? Vector2.Right * (start.X < global::Field.CenterX ? sidestep : -sidestep)
                : Vector2.Down * (start.Y < global::Field.CenterY ? sidestep : -sidestep));
            bool hit = false;
            int frame = 0;
            for (; frame < 1800; frame++)
            {
                if (frame >= 24) player.Position = player.Position.MoveToward(goal, player.SlowestMoveSpeed / 60);
                attack._PhysicsProcess(1d / 60);
                StepBullets();
                hit |= Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position));
                if (attack.Finished && Hostile().Length == 0) break;
            }
            Check(Read<int>(attack, "_rows") == Read<int>(attack, "_totalRows"), "all edge volleys fire before the test completes");
            Check(frame < 1800 && !hit, $"{scene}/{difficulty}/{sequence}/{start}: learned edge sidestep survives");
            attack.Cancel();
            await Frames(1);
            hud.HideBubble();
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private void CheckLearnedMinaAreas(BossMina boss, Player player)
    {
        _game.SelectedJob = Job.Tank;
        Check(Mathf.IsEqualApprox(player.SlowestMoveSpeed, 52.8f), "Mina signatures use the slowest unupgraded locked movement");
        var caster = boss.GetChildren().OfType<MinaPhaseAttacks>().First();
        boss.Position = new Vector2(global::Field.Right - 44, 108);
        var lessons = new Dictionary<(Vector2, int, int), Vector2>();
        foreach (var start in new[] { global::Field.PlayerStart, new Vector2(global::Field.Left + 16, 20),
            new Vector2(global::Field.CenterX, 180) })
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        for (int phase = 0; phase < 5; phase++)
        {
            _game.Difficulty = difficulty;
            player.Position = start;
            caster.BeginPhase(phase);
            var remaining = Pool.Spawn(player.Position + Vector2.Up * 30, Vector2.Right * 60, true);
            caster._Process(2);
            Check(caster.Active && new BossDanmaku(boss).Holding
                && !GetTree().GetNodesInGroup("aoe").Any(n => !n.IsQueuedForDeletion()),
                $"Mina/{difficulty}/{phase}: residual bullets hold the signature before its first warning");
            Pool.Despawn(remaining);
            caster._Process(1);
            for (int wave = 0; wave < (int)Call(caster, "WaveCount", phase)!; wave++)
            {
                caster._Process(1);
                var zones = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().Where(z => !z.IsQueuedForDeletion()).ToArray();
                Check(zones.Length > 0, "Mina signature produces its area attack");
                var key = (start, phase, wave);
                bool replayNormal = difficulty == GameManager.Diff.Hard && phase != 4 && lessons.ContainsKey(key);
                if (!replayNormal)
                {
                    if (Read<AreaStrike.Shape>(zones[0], "_shape") == AreaStrike.Shape.Fullscreen)
                        lessons[key] = Read<Vector2>(zones[0], "_safeCenter");
                    else
                    {
                        bool found = false;
                        for (int radius = 6; radius <= 360 && !found; radius += 3)
                        for (int direction = 0; direction < 16 && !found; direction++)
                        {
                            Vector2 candidate = player.Position + Vector2.FromAngle(direction * Mathf.Tau / 16) * radius;
                            if (!global::Field.Rect.Grow(-12).HasPoint(candidate)) continue;
                            float tolerance = difficulty == GameManager.Diff.Easy ? 10 : 3;
                            if (new[] { Vector2.Zero, Vector2.Up * tolerance, Vector2.Down * tolerance, Vector2.Left * tolerance, Vector2.Right * tolerance }
                                .Any(offset => zones.Any(z => z.CoversPoint(candidate + offset)))) continue;
                            lessons[key] = candidate;
                            found = true;
                        }
                        Check(found, "Mina lesson has a destination with training margin");
                    }
                }
                if (difficulty != GameManager.Diff.Easy)
                    Check(zones.Any(z => z.CoversPoint(player.Position)), "every Mina wave forces another move");
                Check(zones.All(z => !z.CoversPoint(player.Position.MoveToward(lessons[key],
                    ((float)Read<double>(z, "_warn") - 0.4f) * player.SlowestMoveSpeed))),
                    $"Mina/{difficulty}/{phase}/{wave}: visible escape fits 0.4s reaction at unupgraded locked speed");
                player.Position = lessons[key];
                foreach (var zone in zones) zone.Free();
            }
            caster.CancelPendingAttacks();
        }
    }

    private async Task CheckConnectedAnswers(Enemy boss, Player player, Hud hud, string scene)
    {
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        _game.SelectedJob = boss is BossMina ? Job.Magic : Job.Tank;
        foreach (bool charged in new[] { false, true })
        {
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            _game.Difficulty = OS.GetCmdlineUserArgs().Contains("--normal-routes") ? GameManager.Diff.Normal : GameManager.Diff.Hard;
            var random = new RandomNumberGenerator { Seed = 71061 };
            var failedPrefixes = new HashSet<string>();
            List<Vector2>? learned = null;
            for (int attempt = 0; attempt < 1024 && learned == null; attempt++)
            {
                var policies = Enumerable.Range(0, 4).Select(_ => attempt < 32 ? attempt + 1 : random.RandiRange(1, 32)).ToArray();
                if (Enumerable.Range(1, 4).Any(n => failedPrefixes.Contains(string.Join(",", policies.Take(n))))) continue;
                boss.Position = new Vector2(global::Field.BossCenterX, 100);
                player.Position = global::Field.PlayerStart;
                Pool.DespawnAll();
                var rotation = new BossDanmaku(boss);
                Vector2 direction = Vector2.Zero;
                var inputs = new List<Vector2>();
                bool hit = false;
                int phrases = 0;
                for (int frame = 0; frame < 4200; frame++)
                {
                    var attack = Read<BossCrossfire?>(rotation, "_crossfire");
                    int path = policies[Math.Min(3, phrases)];
                    if (frame >= 21 && frame % 6 == 0)
                        direction = ShapeAwareDodge(player, boss, direction,
                            attack != null ? BarrageRouteGoal(player, attack, path, frame) : null, player.MoveSpeed,
                            longLookahead: path == 2, precise: path >= 3);
                    inputs.Add(direction);
                    player.Position = (player.Position + direction * player.MoveSpeed / 60).Clamp(
                        new Vector2(global::Field.Left + 6, 6), new Vector2(global::Field.Right - 6, global::Field.Bottom - 6));
                    if (phrases < 4) rotation.Tick(1d / 60);
                    StepBullets();
                    hit = Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position))
                        || player.Position.DistanceTo(boss.ShotCenter) < 28;
                    phrases = Read<int>(rotation, "_phrase");
                    if (hit || phrases >= 4 && Hostile().Length == 0) break;
                }
                bool escaped = !hit && phrases >= 4 && Hostile().Length == 0;
                if (Read<BossCrossfire?>(rotation, "_crossfire") is { } remaining) remaining.Cancel();
                GD.Print($"[AnswerQA] {scene}/{_game.Difficulty}/{charged}/connected policies={string.Join(",", policies)} phrases={phrases} escaped={escaped}");
                if (escaped) learned = inputs;
                else failedPrefixes.Add(string.Join(",", policies.Take(Math.Min(4, phrases + 1))));
                await Frames(1);
                hud.HideBubble();
            }
            Check(learned != null, scene + ": consecutive attack answers exist at base speed without bombs or dodge");
            Pool.DespawnAll();
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            player.Position = global::Field.PlayerStart;
            var replay = new BossDanmaku(boss);
            foreach (var input in learned!)
            {
                player.Position = (player.Position + input * player.MoveSpeed / 60).Clamp(
                    new Vector2(global::Field.Left + 6, 6), new Vector2(global::Field.Right - 6, global::Field.Bottom - 6));
                if (Read<int>(replay, "_phrase") < 4) replay.Tick(1d / 60);
                StepBullets();
                CheckReplayContact(boss, player, scene, -1);
            }
            Check(Read<int>(replay, "_phrase") >= 4, scene + ": four consecutive patterns replay with 8-way 10Hz inputs, without clearing residual bullets");
            if (Read<BossCrossfire?>(replay, "_crossfire") is { } last) last.Cancel();
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private async Task CaptureDodgeVideo(Enemy boss, Player player, Hud hud, Node root, string scene)
    {
        _game.Difficulty = GameManager.Diff.Lunatic;
        _game.SelectedJob = Job.Tank;
        _game.TrainingSetUpgrade("n_dodge", true);
        Engine.TimeScale = 1;
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        Pool.DespawnAll();
        player.Position = global::Field.PlayerStart;
        Write(player, "_invincible", false);
        Write(player, "_invincibleTimer", 0f);
        Write(player, "_dodgeCd", 0f);
        Write(player, "_fireCooldown", 999f);
        Write(hud, "_bannerTimer", 0d);
        Write(hud, "_cutinTimer", 0d);
        Write(hud, "_bossLineTimer", 0d);
        Write(boss, "_animalTechniqueCooldown", 999d);
        hud.HideSpellCard();
        root.GetNode<StageBackground>("StageBackground").GetNode<BgLayers>("BgLayers")._Process(2);
        Read<BossPostSequence>(boss, "_posts").ResumeMusic();
        AutoplayInput.Move(Vector2.Zero);
        Input.FlushBufferedEvents();
        var pattern = new BossDanmaku(boss);
        Write(pattern, "_phrase", 6);
        Write(boss, "_danmaku", pattern);
        pattern.Tick(0.71);
        var wall = Read<BossDodgeWave>(pattern, "_dodgeWave");
        int lives = player.Lives, dodges = player.DodgeCount, lastDodge = -1;
        ulong start = Engine.GetProcessFrames();
        GD.Print($"[DODGE-VIDEO] {scene} start_frame={start} difficulty={_game.Difficulty} lives={lives} god=false");
        for (int frame = 0; frame < 1800 && !wall.Finished; frame++)
        {
            if (wall.IsStriking && lastDodge != wall.Wave && wall.DistanceToFront(player.Position) <= 24)
            {
                Check(player.DodgeReady, "recorded dodge is off cooldown");
                Call(player, "TryDodge", -wall.Direction);
                lastDodge = wall.Wave;
                GD.Print($"[DODGE-VIDEO] dodge frame={Engine.GetProcessFrames()} wave={wall.Wave}");
            }
            player._PhysicsProcess(1d / 60);
            boss._PhysicsProcess(1d / 60);
            wall._Process(1d / 60);
            Check(player.Lives == lives, "recorded wall crossed without damage");
            await Frames(1);
        }
        Check(wall.Finished && player.DodgeCount - dodges == 3, "recorded all three real dodge crossings");
        await Frames(30);
        GD.Print($"[DODGE-VIDEO] {scene} end_frame={Engine.GetProcessFrames()} dodges={player.DodgeCount - dodges} lives={player.Lives}");
        Pool.ProcessMode = ProcessModeEnum.Inherit;
        _game.TrainingSetUpgrade("n_dodge", false);
    }

    private async Task CaptureRhythmVideo(Enemy boss, Player player, Hud hud, Node root, string scene)
    {
        _game.Difficulty = GameManager.Diff.Lunatic;
        Engine.TimeScale = 1;
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        Write(player, "_fireCooldown", 999f);
        Write(hud, "_bannerTimer", 0d);
        Write(hud, "_cutinTimer", 0d);
        Write(hud, "_bossLineTimer", 0d);
        Write(boss, "_animalTechniqueCooldown", 999d);
        hud.HideSpellCard();
        root.GetNode<StageBackground>("StageBackground").GetNode<BgLayers>("BgLayers")._Process(2);
        Read<BossPostSequence>(boss, "_posts").ResumeMusic();
        var pilot = new AutoplayInput(GetTree(), _game);
        for (int mode = 0; mode < 3; mode++)
        {
            Pool.DespawnAll();
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            player.Position = mode == 0 ? global::Field.PlayerStart
                : new Vector2(global::Field.Left + global::Field.Width * 0.26f, global::Field.Height * 0.74f);
            var pattern = new BossDanmaku(boss);
            Write(pattern, "_phrase", mode == 0 ? 1 : mode == 1 ? 3 : 7);
            Write(boss, "_danmaku", pattern);
            pattern.Tick(0.71);
            var attack = Read<BossCrossfire>(pattern, "_crossfire");
            var direction = Vector2.Zero;
            Vector2 goal = player.Position;
            int frames = mode == 0 ? 720 : 840;
            int contacts = 0, peak = 0;
            GD.Print($"[RHYTHM-VIDEO] {scene} mode={mode} start={Engine.GetProcessFrames() / 60d:F6} frames={frames} difficulty={_game.Difficulty}");
            for (int frame = 0; frame < frames; frame++)
            {
                float speed = mode == 0 && boss is BossMina ? player.MoveSpeed : player.SlowestMoveSpeed;
                if (frame > 21)
                {
                    if (mode == 0 && boss is BossKoharu)
                    {
                        Call(pilot, "DriveMovement", player, 1f / 60);
                        Input.FlushBufferedEvents();
                        direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
                    }
                    else if (frame % 3 == 1)
                    {
                        if (mode > 0 && !attack.Recovering)
                        {
                            goal = player.Position;
                            if (mode == 1) goal.Y = global::Field.Height * (attack.Target.Y < global::Field.CenterY ? 0.82f : 0.18f);
                            else goal.X = global::Field.Left + global::Field.Width * (attack.Target.X < global::Field.CenterX ? 0.82f : 0.18f);
                        }
                        direction = ShapeAwareDodge(player, boss, direction, mode > 0 ? goal : null, speed);
                    }
                }
                AutoplayInput.Move(direction * (speed / player.MoveSpeed));
                Input.FlushBufferedEvents();
                player._PhysicsProcess(1d / 60);
                Call(player, "SetSpriteVisible", true);
                boss._PhysicsProcess(1d / 60);
                StepBullets();
                var shots = Hostile();
                peak = Math.Max(peak, shots.Length);
                if (shots.Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position))) contacts++;
                await Frames(1);
            }
            GD.Print($"[RHYTHM-VIDEO] {scene} mode={mode} end={Engine.GetProcessFrames() / 60d:F6} peak={peak} contact_frames={contacts}");
            if (GodotObject.IsInstanceValid(attack)) attack.Cancel();
            pilot.Release();
            Pool.DespawnAll();
            await Frames(2);
        }
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private async Task CaptureCrossfire(Enemy boss, Player player, Hud hud, Node root, string scene)
    {
        _game.Difficulty = GameManager.Diff.Hard;
        Engine.TimeScale = 1;
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        Pool.DespawnAll();
        player.GlobalPosition = global::Field.PlayerStart;
        Write(player, "_fireCooldown", 999f);
        Write(hud, "_bannerTimer", 0d);
        Write(hud, "_cutinTimer", 0d);
        Write(hud, "_bossLineTimer", 0d);
        hud.HideSpellCard();
        root.GetNode<StageBackground>("StageBackground").GetNode<BgLayers>("BgLayers")._Process(2);
        var pattern = new BossDanmaku(boss);
        Write(pattern, "_phrase", 1);
        Write(boss, "_danmaku", pattern);
        Write(boss, "_animalTechniqueCooldown", 999d);
        var pilot = new AutoplayInput(GetTree(), _game);
        GD.Print($"[CROSSFIRE-VIDEO] {scene} start={Engine.GetProcessFrames() / 60d:F4}");
        for (int frame = 0; frame < 600; frame++)
        {
            pilot.Update(1d / 60, shoot: false, bombs: false, dodge: false);
            player._PhysicsProcess(1d / 60);
            Call(player, "SetSpriteVisible", true);
            boss._PhysicsProcess(1d / 60);
            StepBullets();
            await Frames(1);
        }
        GD.Print($"[CROSSFIRE-VIDEO] {scene} end={Engine.GetProcessFrames() / 60d:F4}");
        pilot.Release();
        Pool.DespawnAll();
    }

    private void CheckFairCrossfire(Enemy boss, Player player, string scene)
    {
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        _game.SelectedJob = Job.Tank;
        _game.TrainingSetUpgrade("n_dodge", false);
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        foreach (bool charged in new[] { false, true })
        foreach (int mode in Enumerable.Range(0, 6))
        foreach (var start in new[] { global::Field.PlayerStart,
            new Vector2(global::Field.Left + 6, 6), new Vector2(global::Field.Right - 6, global::Field.Bottom - 6) })
        {
            _game.Difficulty = difficulty;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            player.Position = start;
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            Pool.DespawnAll();
            var attack = BossCrossfire.Begin(boss, mode is 1 or 2, mode == 1,
                weave: mode == 5, pincer: mode is 3 or 4, diagonal: mode == 4);
            attack.Tick(0.201);
            string label = $"{scene}/{difficulty}/{charged}/{mode}/{start}";
            Check(attack.WarningVisible, label + ": the first committed volley has a visible cue");
            int tested = 0;
            for (int beat = 0; beat <= attack.Burst && attack.Wave < Read<int>(attack, "_waves"); beat++)
            {
                Vector2 target = attack.Target;
                var planned = Enumerable.Range(0, attack.Origins.Count).SelectMany(attack.Volley).ToArray();
                player.Position = (player.Position + (beat % 2 == 0 ? new Vector2(17, 13) : new Vector2(-17, -13)))
                    .Clamp(new Vector2(global::Field.Left + 4, 4), new Vector2(global::Field.Right - 4, global::Field.Bottom - 4));
                attack.Tick(Math.Max(0, attack.WarningRemaining - 0.001));
                Check(attack.Target == target && planned.SequenceEqual(Enumerable.Range(0, attack.Origins.Count).SelectMany(attack.Volley)),
                    label + ": moving during the warning does not move its firing lanes");
                Pool.DespawnAll();
                attack.Tick(0.002);
                var fired = Hostile();
                Check(fired.Length == planned.Length && planned.All(slot => fired.Any(b =>
                    Read<Vector2>(b, "_leadTo").IsEqualApprox(slot.Position) && b.Velocity.Normalized().Dot(slot.Direction) > 0.9999f)),
                    label + ": every visible firing lane produces its committed shot");
                tested++;
                if (attack.Wave >= Read<int>(attack, "_waves")) break;
                int pincerBeats = difficulty == GameManager.Diff.Lunatic && boss.BodyShotJob is Job.Heal or Job.Tank ? 3 : 2;
                bool changeSides = attack.Pincer ? attack.Wave % pincerBeats == 0
                    : difficulty != GameManager.Diff.Easy && attack.Wave % attack.Burst == 0;
                if (changeSides)
                {
                    double needed = attack.Sweep
                        ? (attack.Horizontal ? global::Field.Height : global::Field.Width) * 0.64 / player.SlowestMoveSpeed + 0.6
                        : Read<float>(attack, "_pitch") / player.SlowestMoveSpeed + 0.35;
                    Check(attack.WarningRemaining + 0.0001 >= needed && attack.WarningVisible,
                        label + ": a side change exposes the next lanes throughout the slow-movement allowance");
                }
                else if (attack.Pincer || difficulty != GameManager.Diff.Easy || attack.Sweep || attack.Weave)
                    Check(attack.Target == target, label + ": one burst keeps the same aim after a dodge");
                double remaining = attack.WarningRemaining;
                Hud.BubblePaused = true;
                attack.Tick(2);
                Check(attack.WarningRemaining == remaining && !attack.WarningVisible,
                    label + ": dialogue freezes both warning and firing");
                Hud.BubblePaused = false;
            }
            Check(tested >= 2, label + ": repeated volleys retain their pressure");
            attack.Cancel();
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
        player.Position = global::Field.PlayerStart;
        boss.Position = new Vector2(global::Field.BossCenterX, 100);
    }

    private async Task CheckFairCrossfireRoutes(Enemy boss, Player player, string scene)
    {
        _game.Difficulty = GameManager.Diff.Lunatic;
        _game.SelectedJob = Job.Tank;
        _game.TrainingSetUpgrade("n_dodge", false);
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        var forms = new[] { false, true };
        var phrases = new[] { 0, 1, 2, 3, 5, 7 };
        var starts = new[] { global::Field.PlayerStart, new Vector2(global::Field.Left + 6, 6) };
        string? formFilter = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--fair-form="));
        if (formFilter != null)
        {
            Check(formFilter is "--fair-form=normal" or "--fair-form=evolved", "valid fair crossfire form selection");
            forms = new[] { formFilter == "--fair-form=evolved" };
        }
        string? phraseFilter = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--fair-phrase="));
        if (phraseFilter != null)
        {
            Check(int.TryParse(phraseFilter.Split('=')[1], out int phrase) && phrases.Contains(phrase), "valid fair crossfire phrase selection");
            phrases = new[] { phrase };
        }
        string? startFilter = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--fair-start="));
        if (startFilter != null)
        {
            Check(int.TryParse(startFilter.Split('=')[1], out int start) && start >= 0 && start < starts.Length, "valid fair crossfire start selection");
            starts = new[] { starts[start] };
        }
        foreach (bool charged in forms)
        foreach (int phrase in phrases)
        foreach (var start in starts)
        {
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            bool escaped = false;
            foreach (int path in new[] { 3, 1, 2, 5, 6, 8, 12, 20 })
            {
                player.Position = start;
                boss.Position = new Vector2(global::Field.BossCenterX, 100);
                Pool.DespawnAll();
                var rotation = new BossDanmaku(boss);
                Write(boss, "_danmaku", rotation);
                Write(rotation, "_phrase", phrase);
                rotation.Tick(0.71);
                var attack = Read<BossCrossfire>(rotation, "_crossfire");
                Check(attack != null, $"{scene}/{phrase}: the tested attack comes from the live Lunatic rotation");
                Vector2 direction = Vector2.Zero;
                Vector2? goal = null;
                bool hit = false, heldTail = false;
                int peak = 0, end = 0;
                int observedGroup = 0, reactAt = 21;
                int groupSize = attack!.Pincer ? (boss.BodyShotJob is Job.Heal or Job.Tank ? 3 : 2) : attack.Burst;
                float speed = player.SlowestMoveSpeed;
                for (int frame = 0; frame < 5400; frame++)
                {
                    end = frame;
                    int group = attack.Wave / groupSize;
                    if (!attack.Finished && group != observedGroup)
                    {
                        observedGroup = group;
                        reactAt = frame + 21;
                    }
                    if (frame >= 21)
                    {
                        if (frame >= reactAt) goal = BarrageRouteGoal(player, attack!, path, frame);
                        if (frame % 3 == 0) direction = ShapeAwareDodge(player, boss, direction,
                            goal, speed, longLookahead: path == 2, precise: true, ignoreCrossfirePreview: frame < reactAt);
                        player.Position = (player.Position + direction * speed / 60).Clamp(
                            new Vector2(global::Field.Left + 5, 5), new Vector2(global::Field.Right - 5, global::Field.Bottom - 5));
                    }
                    rotation.Tick(1d / 60);
                    StepBullets();
                    var shots = Hostile();
                    peak = Math.Max(peak, shots.Length);
                    var contact = shots.FirstOrDefault(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position));
                    hit = contact != null || player.Position.DistanceTo(boss.ShotCenter) < 28;
                    if (hit) GD.Print($"[FairCrossfireContact] {scene}/{charged}/{phrase}/{path} frame={frame} wave={attack.Wave} player={player.Position} bullet={contact?.Position} velocity={contact?.Velocity} body={player.Position.DistanceTo(boss.ShotCenter):F1}");
                    if (attack!.Finished && shots.Length > 0)
                    {
                        bool waiting = rotation.Holding && rotation.CrossfireActive && Read<int>(rotation, "_phrase") == phrase + 1
                            && Read<BossCrossfire?>(rotation, "_crossfire") == null;
                        if (!waiting) throw new Exception(scene + ": residual shots overlap the next attack");
                        if (!heldTail) Check(waiting, scene + ": residual shots finish before the next attack begins");
                        heldTail = true;
                    }
                    if (hit || attack.Finished && shots.Length == 0) break;
                }
                escaped = !hit && attack!.Finished && Hostile().Length == 0;
                GD.Print($"[FairCrossfireQA] {scene}/{charged}/phrase={phrase}/start={start}/path={path} escaped={escaped} seconds={end / 60f:F2} peak={peak} tail={heldTail}");
                attack!.Cancel();
                Pool.DespawnAll();
                await Frames(1);
                if (escaped) break;
            }
            Check(escaped, $"{scene}/{charged}/phrase={phrase}/start={start}: a 0.35-second reaction to opening and side changes with unupgraded slow movement clears every shot without bombs or dodge");
        }
        Pool.ProcessMode = ProcessModeEnum.Inherit;
        player.Position = global::Field.PlayerStart;
        boss.Position = new Vector2(global::Field.BossCenterX, 100);
    }

    private void CheckCrossfireBreakRecovery(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.SelectedJob = Job.Tank;
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        foreach (bool charged in new[] { false, true })
        foreach (bool earlyClose in new[] { false, true })
        {
            _game.Difficulty = difficulty;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            Call(boss, "EnterShielded");
            player.Position = global::Field.PlayerStart;
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            Pool.DespawnAll();
            var rotation = new BossDanmaku(boss);
            Write(boss, "_danmaku", rotation);
            rotation.Tick(0.71);
            int Emitted() => Read<BossCrossfire?>(rotation, "_crossfire")?.Wave
                ?? Read<BossNormalBarrage?>(rotation, "_normalBarrage")?.Wave ?? 0;
            for (int frame = 0; frame < 900 && Emitted() < 2; frame++)
            {
                rotation.Tick(1d / 60);
                StepBullets();
            }
            Check(Hostile().Length > 0, scene + ": shield recovery begins with real emitted projectiles");
            int hp = Read<int>(boss, "_hp");
            Call(boss, "EnterBreak");
            hud.HideBubble();
            int elapsed = 0;
            for (; elapsed < 700 && Read<object>(boss, "_phase").ToString() != "Shielded"; elapsed++)
            {
                if (earlyClose && Read<object>(boss, "_phase").ToString() == "Exposed")
                    Write(boss, "_windowDamage", _game.ExposedDamageCap);
                Call(boss, "TickBossPhase", 1d / 60);
                hud.HideBubble();
                rotation.Tick(1d / 60);
                StepBullets();
            }
            Check(Read<object>(boss, "_phase").ToString() == "Shielded" && Read<int>(boss, "_hp") == hp,
                scene + ": the actual shield cycle completes without a test-only HP change");
            int remaining = Hostile().Length;
            GD.Print($"[CrossfireBreakQA] {scene}/{difficulty}/{charged}/early={earlyClose} recovery={elapsed / 60f:F2}s residual={remaining}");
            for (int frame = 0; frame < 900 && Hostile().Length > 0; frame++)
            {
                rotation.Tick(1d / 60);
                if (Read<BossCrossfire?>(rotation, "_crossfire") != null
                    || Read<BossNormalBarrage?>(rotation, "_normalBarrage") != null || !rotation.Holding)
                    throw new Exception(scene + ": shield recovery layers a new attack over the old projectiles");
                StepBullets();
            }
            Check(Hostile().Length == 0, scene + ": old projectiles eventually leave after shield recovery");
            rotation.Tick(0.8);
            var crossfire = Read<BossCrossfire?>(rotation, "_crossfire");
            var normal = Read<BossNormalBarrage?>(rotation, "_normalBarrage");
            Check(difficulty == GameManager.Diff.Lunatic ? crossfire is { Wave: 0, Finished: false } : normal is { Wave: 0, Finished: false },
                scene + ": the recovered shield restarts with a fresh warning after old projectiles leave");
            crossfire?.Cancel();
            normal?.Cancel();
        }
        Write(boss, "_windowDamage", 0);
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
    }

    private async Task CheckTelegraphCommitment(Enemy boss, Player player, Hud hud, string scene)
    {
        var caster = boss.GetChildren().OfType<AreaSpellCaster>().SingleOrDefault();
        var mina = boss.GetChildren().OfType<MinaPhaseAttacks>().SingleOrDefault();
        foreach (var difficulty in new[] { GameManager.Diff.Hard, GameManager.Diff.Lunatic })
        foreach (bool charged in new[] { false, true })
        foreach (int mode in new[] { 0, 1, 2, 3, 4, 5 })
        foreach (var corner in new[] { new Vector2(global::Field.Left + 4, 4), new Vector2(global::Field.Right - 4, 4),
            new Vector2(global::Field.Left + 4, global::Field.Bottom - 4), new Vector2(global::Field.Right - 4, global::Field.Bottom - 4),
            new Vector2(global::Field.Left, global::Field.Top), new Vector2(global::Field.Right, global::Field.Top),
            new Vector2(global::Field.Left, global::Field.Bottom), new Vector2(global::Field.Right, global::Field.Bottom) })
        {
            _game.Difficulty = difficulty;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            player.Position = corner;
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            Pool.DespawnAll();
            var attack = BossCrossfire.Begin(boss, mode is 1 or 2, mode == 1, weave: mode == 5, pincer: mode is 3 or 4, diagonal: mode == 4);
            attack.Tick(0.56);
            Check(attack.WarningVisible, scene + ": the opening volley has a visible warning");
            var planned = Enumerable.Range(0, attack.Origins.Count).SelectMany(attack.Volley).ToArray();
            player.Position = attack.Origins[0];
            Check(planned.SequenceEqual(Enumerable.Range(0, attack.Origins.Count).SelectMany(attack.Volley)),
                scene + ": approaching an emitter does not erase or retarget warned lanes");
            attack.Tick(attack.WarningRemaining + 0.001);
            var fired = Hostile();
            Check(fired.Length == planned.Length && planned.All(p => fired.Any(b =>
                Read<Vector2>(b, "_leadTo").IsEqualApprox(p.Position) && b.Velocity.Normalized().Dot(p.Direction) > 0.9999f)),
                $"{scene}/{difficulty}/{charged}/{mode}: every warned lane fires exactly once ({fired.Length})");
            player.Position = corner;
            var deployed = new HashSet<Bullet>();
            bool materializedOnPlayer = false;
            for (int frame = 0; frame < 21; frame++)
            {
                StepBullets();
                foreach (var shot in fired.Where(b => b.Active && !b.LeadIn && b.CollisionLayer != 0))
                    if (deployed.Add(shot)) materializedOnPlayer |= BossCharacterShotQa.HitsPlayer(shot, corner);
            }
            Check(deployed.Count == fired.Length,
                scene + ": edge deployment retains all shots, including beside the player");
            Check(!materializedOnPlayer,
                scene + ": edge deployment does not materialize on the player core");
            for (int wave = attack.Wave; wave < attack.Burst; wave++)
            {
                Pool.DespawnAll();
                var nextPlan = Enumerable.Range(0, attack.Origins.Count).SelectMany(attack.Volley).ToArray();
                attack.Tick(attack.WarningRemaining + 0.001);
                var nextShots = Hostile();
                Check(nextShots.Length == nextPlan.Length && nextPlan.All(slot => nextShots.Any(b =>
                    Read<Vector2>(b, "_leadTo").IsEqualApprox(slot.Position) && b.Velocity.Normalized().Dot(slot.Direction) > 0.9999f)),
                    scene + ": interleaved and crossing rows fire every committed lane");
            }
            Check(attack.WarningVisible,
                scene + ": relocation exposes the next lanes for the whole movement allowance");
            double before = attack.WarningRemaining;
            Hud.BubblePaused = true;
            attack.Tick(3);
            Check(attack.WarningRemaining == before && !attack.WarningVisible, scene + ": dialogue freezes the warning clock");
            Hud.BubblePaused = false;
            attack.Tick(Math.Max(0, attack.WarningRemaining - Read<double>(attack, "_warning")) + 0.001);
            Check(attack.WarningVisible, scene + ": firing arrows remain visible through the final warning interval");
            attack.Cancel();
            Check(!attack.WarningVisible, scene + ": canceled warnings disappear immediately");
        }
        Pool.DespawnAll();
        player.Position = global::Field.PlayerStart;
        boss.Position = new Vector2(global::Field.BossCenterX, 100);
        var rotation = new BossDanmaku(boss);
        Write(boss, "_danmaku", rotation);
        Write(rotation, "_phrase", 1);
        rotation.Tick(0.71);
        var committed = Read<BossCrossfire>(rotation, "_crossfire");
        double pending = 0;
        if (caster != null)
        {
            caster.RequestFinale();
            pending = Read<double>(caster, "_aoeFireT");
            caster._Process(3);
            Check(Read<double>(caster, "_aoeFireT") == pending, scene + ": queued special attacks wait for committed crossfire");
        }
        else
        {
            Write(mina!, "_cooldown", 0.01d);
            mina!._Process(3);
            Check(!mina.Active, scene + ": a ready phase attack waits for committed crossfire");
        }
        for (int frame = 0; frame < 2400 && rotation.CrossfireActive; frame++)
        {
            rotation.Tick(1d / 60);
            StepBullets();
        }
        Check(committed.Finished && committed.Wave == Read<int>(committed, "_waves"),
            scene + ": a queued special never cancels announced salvos");
        if (caster != null)
        {
            caster._Process(0.1);
            Check(Read<double>(caster, "_aoeFireT") < pending, scene + ": the queued special resumes after crossfire");
            caster.CancelPendingAttacks();
            Write(boss, "_animalTechniqueCooldown", 0d);
            Call(caster, "Cast");
            var mover = Read<BossMover>(boss, "_mover");
            Call(boss, "TickForm2Technique", 0.1d, mover, caster);
            Check(Read<bool>(caster, "_pending") && !boss.AnimalTechniqueActive,
                scene + ": an evolved technique cannot cancel an announced spell");
            caster._Process(1);
            Check(!Read<bool>(caster, "_pending") && GetTree().GetNodesInGroup("aoe").Count > 0,
                scene + ": the announced spell produces its telegraphs");
            Call(boss, "TickForm2Technique", 0.1d, mover, caster);
            Check(!boss.AnimalTechniqueActive, scene + ": evolved techniques wait for committed area strikes");
            foreach (var node in GetTree().GetNodesInGroup("aoe")) node.QueueFree();
            await Frames(2);
            Write(caster, "_castT", 7d);
            Call(boss, "TickForm2Technique", 0.1d, mover, caster);
            Check(boss.AnimalTechniqueActive, scene + ": the waiting evolved technique starts after the area strike");
            Check(Read<double>(caster, "_castT") == 7d, scene + ": evolved techniques preserve the next area spell's cooldown progress");
            boss.CancelAnimalTechnique();
            await Frames(2);
        }
        else
        {
            mina!._Process(0.1);
            Check(mina.Active, scene + ": the waiting phase attack starts after crossfire");
            mina.CancelPendingAttacks();
        }
        Pool.DespawnAll();
        var interrupted = BossCrossfire.Begin(boss);
        var edge = BossEdgeVolley.Begin(boss, scene == "MinaBattle" ? "mina" : scene.ToLowerInvariant(), 0);
        BossAnimalTechnique? animal = boss is BossMina ? null : BossAnimalTechnique.Begin(boss, scene.ToLowerInvariant(), caster!);
        edge.SetPhysicsProcess(false);
        animal?.SetPhysicsProcess(false);
        Call(boss, "EnterBreak");
        hud.HideBubble();
        interrupted.Tick(0.1);
        edge._PhysicsProcess(0.1);
        animal?._PhysicsProcess(0.1);
        Check(interrupted.Finished && !interrupted.WarningVisible && edge.Finished && (animal == null || animal.Finished),
            scene + ": shield breaks remove warnings instead of freezing them in place");
        await Frames(2);
        Check(Hostile().Length == 0, scene + ": interrupted warnings produce no late shots");
    }

    private async Task CheckCrossfire(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.SelectedJob = Job.Tank;
        GD.Print($"[CrossfireQA] {scene}: unupgraded slowest job movement={player.SlowestMoveSpeed:F1}px/s");
        MeasureCrossfire(boss, player, scene, verify: true);
        foreach (bool charged in new[] { false, true })
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            _game.Difficulty = diff;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            Pool.DespawnAll();
            player.GlobalPosition = global::Field.PlayerStart;
            boss.Position = new Vector2(global::Field.BossCenterX, 100);
            var pattern = new BossDanmaku(boss);
            Write(boss, "_danmaku", pattern);
            Write(pattern, "_phrase", 1);
            pattern.Tick(0.71);
            var attack = Read<BossCrossfire>(pattern, "_crossfire");
            Check(attack != null && attack.Origins.Count >= 2 && pattern.Holding, scene + ": crossfire is in the real attack rotation");
            float separation = attack!.Origins.SelectMany((a, i) => attack.Origins.Skip(i + 1).Select(b => a.DistanceTo(b))).Min();
            Check(separation >= 56 && attack.Origins.All(global::Field.Rect.HasPoint), scene + ": emitters are distinct and inside the arena");
            pattern.Tick(0.8);
            Check(Hostile().Length == 0, scene + ": at least 0.8s visible deployment before fire");
            var target = attack.Target;
            player.Position += new Vector2(0, 25);
            Hud.BubblePaused = true;
            pattern.Tick(5);
            Check(attack.Wave == 0 && Hostile().Length == 0, scene + ": dialogue freezes warning and salvos");
            Hud.BubblePaused = false;
            pattern.Tick(0.8);
            var first = Hostile();
            var centered = first.Where(b => b.BossAimTarget.HasValue).ToArray();
            Check(attack.Wave == 1 && centered.Length == (diff >= GameManager.Diff.Normal ? 1 : attack.Origins.Count)
                && first.Select(b => b.GlobalPosition).Distinct().Count() == attack.Origins.Count - (diff >= GameManager.Diff.Hard ? 1 : 0),
                $"{scene}/{diff}/{charged}: remote origins share the full formation");
            Check(centered.All(b => b.BossAimTarget == target
                && b.Velocity.Normalized().Dot((diff >= GameManager.Diff.Normal ? Read<Vector2>(b, "_leadTo") : b.GlobalPosition).DirectionTo(target)) > 0.9999f),
                scene + ": every source aims at its warned player position");
            if (diff >= GameManager.Diff.Normal)
            {
                float pitch = Read<float>(attack, "_pitch");
                var axes = first.Select(b => Read<Vector2>(b, "_leadTo")).Select(p => attack.Horizontal ? p.Y : p.X).ToArray();
                float low = attack.Horizontal ? global::Field.Top : global::Field.Left;
                float high = attack.Horizontal ? global::Field.Bottom : global::Field.Right;
                Check(axes.Min() - low <= pitch && high - axes.Max() <= pitch,
                    scene + ": curtain covers the entire axis without a large empty side");
            }
            Check(first.All(b => b.CollisionLayer == 0 && b.LeadIn && b.Charged == charged
                && b.Accel == (boss is BossAkari) && b.Homing == (boss is BossKoharu)),
                scene + ": deployment is harmless and character properties remain intact");
            float clearance = first.SelectMany((a, i) => first.Skip(i + 1).Select(b =>
                BossCharacterShotQa.Clearance(a, b, Read<Vector2>(a, "_leadTo"), Read<Vector2>(b, "_leadTo")))).Min();
            Check(clearance >= BulletPool.BossLaneGap, $"{scene}: launch lanes leave {clearance:0.0}px for the 4px core");
            foreach (var child in boss.GetChildren())
            {
                if (child is AreaSpellCaster caster)
                {
                    double before = Read<double>(caster, "_castT");
                    caster._Process(3);
                    Check(Read<double>(caster, "_castT") > before && !caster.EdgeAttackActive && !Read<bool>(caster, "_pending"),
                        scene + ": broad AOE prepares without interrupting crossfire");
                }
                if (child is MinaPhaseAttacks mina)
                {
                    double before = Read<double>(mina, "_cooldown");
                    mina._Process(3);
                    Check(Read<double>(mina, "_cooldown") < before && !mina.Active, scene + ": phase AOE prepares without interrupting crossfire");
                }
            }
            if (diff == GameManager.Diff.Hard && !charged && OS.GetCmdlineUserArgs().Contains("--shots"))
            {
                hud.HideSpellCard();
                Write(hud, "_bossLineTimer", 0d);
                for (int frame = 0; frame < 110; frame++) { pattern.Tick(1d / 60); StepBullets(); }
                await Frames(2);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string folder = ProjectSettings.GlobalizePath("res://build/qa_story/crossfire");
                DirAccess.MakeDirRecursiveAbsolute(folder);
                using var screenshot = GetViewport().GetTexture().GetImage();
                Check(screenshot.SavePng(folder + "/" + scene + ".png") == Error.Ok, scene + ": screenshot saved");
            }
            attack.Cancel();
            Pool.DespawnAll();
            if (diff is GameManager.Diff.Hard or GameManager.Diff.Lunatic)
            {
                bool escaped = false;
                foreach (float angle in new[] { -90f, 90f, -45f, 45f, -135f, 135f, 0f, 180f, 999f, 1000f, 1001f, 1002f, 1003f, 1004f, 1005f, 1006f, 1007f, 1008f, 1009f }
                    .Concat(Enumerable.Range(1011, 96).Select(i => (float)i)))
                {
                    bool dodgeRoute = angle is >= 1007f and <= 1009f or >= 1043f;
                    if (dodgeRoute && diff != GameManager.Diff.Lunatic) continue;
                    _game.TrainingSetUpgrade("n_dodge", dodgeRoute);
                    Write(player, "_dodgeCd", 0f);
                    Write(player, "_dodgeInv", 0f);
                    Call(player, "EndDodge");
                    Pool.DespawnAll();
                    player.GlobalPosition = global::Field.PlayerStart;
                    boss.Position = new Vector2(global::Field.BossCenterX, 100);
                    var trial = BossCrossfire.Begin(boss);
                    var direction = Vector2.FromAngle(Mathf.DegToRad(angle));
                    Vector2 goal = player.Position;
                    int group = -1;
                    var pilot = new AutoplayInput(GetTree(), _game);
                    Write(player, "_locked", angle < 1002f);
                    float moveSpeed = angle >= 1002f ? player.MoveSpeed : player.SlowestMoveSpeed;
                    bool hit = false;
                    for (int frame = 0; frame < 1500 && !hit; frame++)
                    {
                        if (frame > 21)
                        {
                            if (angle == 999f)
                            {
                                Call(pilot, "DriveMovement", player, 1f / 60);
                                Input.FlushBufferedEvents();
                                direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
                            }
                            if (angle == 1000f && frame % 3 == 1) direction = ShapeAwareDodge(player, boss, direction);
                            if (angle >= 1004f && angle <= 1007f && (angle == 1006f || frame % 3 == 1))
                                direction = ShapeAwareDodge(player, boss, direction, speed: moveSpeed, longLookahead: angle == 1005f, precise: angle == 1006f);
                            if (angle >= 1001f && angle <= 1003f || angle is 1008f or 1009f)
                            {
                                int phase = trial.Wave / trial.Burst;
                                if (boss is BossMina && diff == GameManager.Diff.Lunatic)
                                    phase = phase * 2 + (trial.Wave % trial.Burst == trial.Burst - 1 ? 1 : 0);
                                if (group != phase)
                                {
                                    group = phase;
                                    goal = player.Position;
                                    if (trial.Horizontal) goal.Y = global::Field.Height * (trial.Target.Y < global::Field.CenterY ? 0.82f : 0.18f);
                                    else goal.X = global::Field.Left + global::Field.Width * (trial.Target.X < global::Field.CenterX ? 0.82f : 0.18f);
                                }
                                if (frame % 3 == 1) direction = ShapeAwareDodge(player, boss, direction, goal, moveSpeed, angle == 1003f);
                            }
                            if (angle >= 1011 && frame % 3 == 0)
                            {
                                int path = ((int)angle - 1011) % 32 + 1;
                                direction = ShapeAwareDodge(player, boss, direction, BarrageRouteGoal(player, trial, path, frame),
                                    moveSpeed, longLookahead: path == 2, precise: path >= 3);
                            }
                            Vector2 next = player.GlobalPosition + direction * moveSpeed / 60;
                            float right = angle < 999 ? global::Field.CenterX - 32 : global::Field.Right - 8;
                            if (next.X < global::Field.Left + 8 || next.X > right) direction.X *= -1;
                            if (next.Y < 8 || next.Y > global::Field.Bottom - 8) direction.Y *= -1;
                            if (!dodgeRoute) player.GlobalPosition = (player.GlobalPosition + direction * moveSpeed / 60).Clamp(
                                new Vector2(global::Field.Left + 6, 6), new Vector2(global::Field.Right - 6, global::Field.Bottom - 6));
                        }
                        if (dodgeRoute)
                        {
                            AutoplayInput.Move(direction);
                            Input.FlushBufferedEvents();
                            StepDodgePilot(player, angle is 1009f or >= 1043f and < 1075f ? direction : Vector2.Zero);
                        }
                        trial.Tick(1d / 60);
                        StepBullets();
                        hit = Read<float>(player, "_dodgeInv") <= 0 && (Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.GlobalPosition))
                            || player.GlobalPosition.DistanceTo(boss.ShotCenter) < 28);
                        if (hit && angle >= 1000) GD.Print($"[CrossfireQA] contact frame={frame} wave={trial.Wave} target={trial.Target}");
                        if (trial.Finished && Hostile().Length == 0) break;
                    }
                    hit |= !trial.Finished || Hostile().Length > 0;
                    trial.Cancel();
                    pilot.Release();
                    Write(player, "_locked", false);
                    GD.Print($"[CrossfireQA] route {scene}/{diff}/{charged} angle={angle} survived={!hit} at={player.GlobalPosition}");
                    if (!hit) { escaped = true; break; }
                }
                _game.TrainingSetUpgrade("n_dodge", false);
                Check(escaped, $"{scene}/{diff}/{charged}: a legal-speed route escapes the full attack (dodge allowed only on Lunatic)");
            }
        }
        foreach (string group in new[] { "aoe", "boss_edge_volleys", "boss_animal_techniques" })
        {
            Pool.DespawnAll();
            player.GlobalPosition = global::Field.PlayerStart;
            var pattern = new BossDanmaku(boss);
            Write(pattern, "_phrase", 1);
            pattern.Tick(0.71);
            var hazard = new Node();
            boss.GetParent().AddChild(hazard);
            hazard.AddToGroup(group);
            pattern.Tick(2);
            Check(!pattern.CrossfireActive && Hostile().Length == 0, scene + ": pending crossfire cancels for " + group);
            hazard.Free();
        }
        Pool.DespawnAll();
        var interrupted = BossCrossfire.Begin(boss);
        Call(boss, "EnterBreak");
        hud.HideBubble();
        interrupted.Tick(2);
        Check(interrupted.Finished && Hostile().Length == 0, scene + ": shield break removes pending emitters without a late shot");
    }
    private void StepDodgePilot(Player player, Vector2 direction, Enemy? boss = null)
    {
        Vector2 movement = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down") * player.MoveSpeed;
        if (player.DodgeReady && Hostile().Any(b => !b.LeadIn &&
            new[] { 0f, 0.08f, 0.16f }.Any(time => BossCharacterShotQa.HitsPlayer(b, player.Position + (movement - b.Velocity) * time))))
            Call(player, "TryDodge", boss == null ? direction : ShapeAwareDodge(player, boss, direction,
                player.Position + direction * _game.DodgeDistance, player.MoveSpeed, precise: true, dash: true));
        player._PhysicsProcess(1d / 60);
        Pool.DespawnPlayerBullets();
    }

    private async Task CheckDodgeHandoff(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.Difficulty = GameManager.Diff.Lunatic;
        _game.SelectedJob = boss is BossMina ? Job.Magic : Job.Tank;
        _game.TrainingSetUpgrade("n_dodge", true);
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        foreach (bool charged in new[] { false, true })
        {
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            bool escaped = false;
            for (int path = 1; path <= 32 && !escaped; path++)
            {
                Pool.DespawnAll();
                boss.Position = new Vector2(global::Field.BossCenterX, 100);
                player.Position = global::Field.PlayerStart;
                Write(player, "_dodgeCd", 0f);
                Write(player, "_dodgeInv", 0f);
                Call(player, "EndDodge");
                var rotation = new BossDanmaku(boss);
                Write(rotation, "_phrase", 5);
                Vector2 direction = Vector2.Zero;
                bool hit = false, sawWall = false;
                int lastDodge = -1, end = 0, tail = 0;
                for (int frame = 0; frame < 3600; frame++)
                {
                    end = frame;
                    var attack = Read<BossCrossfire?>(rotation, "_crossfire");
                    var wall = Read<BossDodgeWave?>(rotation, "_dodgeWave");
                    Vector2? goal = attack is { Finished: false } ? BarrageRouteGoal(player, attack, path, frame) : null;
                    if (frame % 3 == 0) direction = ShapeAwareDodge(player, boss, direction, goal, player.MoveSpeed, precise: true);
                    if (wall is { Finished: false })
                    {
                        sawWall = true;
                        if (wall.IsStriking && lastDodge != wall.Wave && wall.DistanceToFront(player.Position) <= 24 && player.DodgeReady)
                        {
                            Call(player, "TryDodge", -wall.Direction);
                            lastDodge = wall.Wave;
                        }
                    }
                    AutoplayInput.Move(direction);
                    Input.FlushBufferedEvents();
                    if (wall == null) StepDodgePilot(player, direction);
                    else { player._PhysicsProcess(1d / 60); Pool.DespawnPlayerBullets(); }
                    rotation.Tick(1d / 60);
                    StepBullets();
                    if (sawWall) tail = Math.Max(tail, Hostile().Length);
                    bool vulnerable = Read<float>(player, "_dodgeInv") <= 0;
                    hit = vulnerable && (Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position))
                        || wall is { Finished: false } && wall.CoversPoint(player.Position)
                        || player.Position.DistanceTo(boss.ShotCenter) < 28);
                    if (hit || Read<int>(rotation, "_phrase") >= 7) break;
                }
                escaped = !hit && sawWall && Read<int>(rotation, "_phrase") >= 7;
                GD.Print($"[DodgeHandoffQA] {scene}/{charged}/path={path} escaped={escaped} end={end / 60d:F2}s residual_peak={tail}");
                if (Read<BossCrossfire?>(rotation, "_crossfire") is { } remaining) remaining.Cancel();
                if (Read<BossDodgeWave?>(rotation, "_dodgeWave") is { } remainingWall) remainingWall.Cancel();
                AutoplayInput.Move(Vector2.Zero);
                Input.FlushBufferedEvents();
                hud.HideBubble();
                await Frames(1);
            }
            Check(escaped, scene + ": the real rotation reaches and clears every dodge wall with preceding bullets still active");
        }
        Pool.DespawnAll();
        Pool.ProcessMode = ProcessModeEnum.Inherit;
        _game.TrainingSetUpgrade("n_dodge", false);
    }

    private async Task CheckDodgeWave(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.Difficulty = GameManager.Diff.Lunatic;
        _game.TrainingSetUpgrade("n_dodge", false);
        var unlearned = new BossDanmaku(boss);
        Write(unlearned, "_phrase", 6);
        unlearned.Tick(0.71);
        Check(!unlearned.DodgeWaveActive && unlearned.CrossfireActive, scene + ": an unlearned dodge uses a traversable barrage instead of an unavoidable wall");
        Read<BossCrossfire>(unlearned, "_crossfire").Cancel();
        _game.TrainingSetUpgrade("n_dodge", true);
        foreach (bool focused in new[] { false, true })
        foreach (var corner in new[] { new Vector2(global::Field.Left + 5, 5),
            new Vector2(global::Field.Right - 5, global::Field.Bottom - 5) })
        {
            Write(_game, "_focusModeT", focused ? 1000f : 0f);
            player.Position = corner;
            Call(player, "EndDodge");
            Write(player, "_dodgeCd", 0f);
            Call(player, "TryDodge", Vector2.Zero);
            Check(!player.DodgeReady, scene + ": the dodge wall can be announced immediately after the player uses dodge");
            var cooling = BossDodgeWave.Begin(boss);
            for (int frame = 0; frame < 1200 && !cooling.IsStriking; frame++)
            {
                player._PhysicsProcess(1d / 60);
                Pool.DespawnPlayerBullets();
                cooling.Tick(GameManager.EnemyDelta(1d / 60));
            }
            Check(cooling.IsStriking && player.DodgeReady,
                scene + ": the full cooldown expires before the earliest wall can reach an arena corner");
            cooling.Cancel();
        }
        Write(_game, "_focusModeT", 0f);
        foreach (var difficulty in new[] { GameManager.Diff.Hard, GameManager.Diff.Lunatic })
        {
            _game.Difficulty = difficulty;
            var rotation = new BossDanmaku(boss);
            Write(boss, "_danmaku", rotation);
            Write(rotation, "_phrase", 6);
            rotation.Tick(0.71);
            Check(rotation.DodgeWaveActive == (difficulty == GameManager.Diff.Lunatic), scene + ": dodge walls are exclusive to Lunatic");
            if (!rotation.DodgeWaveActive) { Read<BossNormalBarrage>(rotation, "_normalBarrage").Cancel(); continue; }
            foreach (var child in boss.GetChildren())
            {
                if (child is AreaSpellCaster caster)
                {
                    double before = Read<double>(caster, "_castT");
                    caster._Process(5);
                    Check(Read<double>(caster, "_castT") > before && !caster.EdgeAttackActive && !Read<bool>(caster, "_pending"),
                        scene + ": area spells prepare but never interrupt dodge walls");
                }
                if (child is MinaPhaseAttacks mina)
                {
                    double before = Read<double>(mina, "_cooldown");
                    mina._Process(5);
                    Check(Read<double>(mina, "_cooldown") < before && !mina.Active, scene + ": phase spells prepare but never interrupt dodge walls");
                }
            }
            Read<BossDodgeWave>(rotation, "_dodgeWave").Cancel();
        }
        foreach (bool charged in new[] { false, true })
        foreach (var job in Enum.GetValues<Job>())
        foreach (bool dash in new[] { false, true })
        foreach (bool focused in new[] { false, true })
        {
            if (focused && !dash) continue;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            _game.SelectedJob = job;
            Check(Mathf.Abs(Mathf.AngleDifference(BossDodgeWave.Ink.H * Mathf.Tau, BulletArt.PlayerColor(job).H * Mathf.Tau)) > Mathf.DegToRad(43),
                scene + ": dodge-wall color remains distinct from every player shot");
            Write(_game, "_focusModeT", focused ? 1000f : 0f);
            Pool.DespawnAll();
            player.Position = global::Field.PlayerStart;
            Write(player, "_dodgeCd", 0f);
            Write(player, "_dodgeInv", 0f);
            Write(player, "_invincible", false);
            Write(player, "_invincibleTimer", 0f);
            Call(player, "EndDodge");
            int lives = player.Lives, dodges = player.DodgeCount;
            var wave = BossDodgeWave.Begin(boss);
            Hud.BubblePaused = true;
            wave.Tick(2);
            Check(wave.WarningRemaining == 1.35 && !wave.IsStriking, scene + ": dialogue pauses the wall warning");
            Hud.BubblePaused = false;
            int lastDodge = -1;
            bool bodyHit = false;
            for (int frame = 0; frame < 6000 && !wave.Finished && player.Lives == lives; frame++)
            {
                if (wave.IsStriking && lastDodge != wave.Wave && wave.DistanceToFront(player.Position) <= (dash ? 24 : 8) * GameManager.EnemyTimeScale)
                {
                    Check(player.DodgeReady, $"{scene}/{job}: cooldown is ready for each wall pair");
                    Call(player, "TryDodge", dash ? -wave.Direction : Vector2.Zero);
                    lastDodge = wave.Wave;
                }
                player._PhysicsProcess(1d / 60);
                Pool.DespawnPlayerBullets();
                wave.Tick(GameManager.EnemyDelta(1d / 60));
                bodyHit |= Read<float>(player, "_dodgeInv") <= 0 && player.Position.DistanceTo(boss.ShotCenter) < 28;
            }
            Check(wave.Finished && player.Lives == lives && !bodyHit && player.DodgeCount - dodges == (charged ? 4 : 3),
                $"{scene}/{job}/{charged}/dash={dash}/focus={focused}: every double wall is crossed with real dodge physics and no damage");
            wave.Cancel();
        }
        _game.SelectedJob = Job.Tank;
        Write(_game, "_focusModeT", 0f);
        if (boss is BossMina) Write(boss, "_pattern", 0);
        else Write(boss, "_form2", false);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        Call(player, "EndDodge");
        Write(player, "_dodgeInv", 0f);
        player.Position = global::Field.PlayerStart;
        var stationary = BossDodgeWave.Begin(boss);
        bool hit = false;
        for (int frame = 0; frame < 400 && !hit; frame++)
        {
            stationary.Tick(1d / 60);
            hit |= stationary.CoversPoint(player.Position);
        }
        Check(hit, scene + ": not dodging is hit by the complete wall");
        stationary.Cancel();
        foreach (var corner in new[] { new Vector2(global::Field.Left, global::Field.Top), new Vector2(global::Field.Right, global::Field.Top),
            new Vector2(global::Field.Left, global::Field.Bottom), new Vector2(global::Field.Right, global::Field.Bottom) })
        {
            player.Position = corner;
            var edgeWall = BossDodgeWave.Begin(boss);
            bool edgeHit = false;
            for (int frame = 0; frame < 600 && !edgeHit; frame++)
            {
                edgeWall.Tick(1d / 60);
                edgeHit = edgeWall.CoversPoint(corner);
            }
            Check(edgeHit, scene + ": the full wall also hits the exact arena boundary at " + corner);
            edgeWall.Cancel();
        }
        player.Position = global::Field.PlayerStart;
        if (OS.GetCmdlineUserArgs().Contains("--shots") && DisplayServer.GetName() != "headless")
        {
            var preview = BossDodgeWave.Begin(boss);
            preview.Tick(1.35);
            preview.Tick(global::Field.Width * 0.45 / BossDodgeWave.Speed);
            hud.HideSpellCard();
            Write(hud, "_bannerTimer", 0d);
            await Frames(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string folder = ProjectSettings.GlobalizePath("res://build/qa_story/dodge_wave");
            DirAccess.MakeDirRecursiveAbsolute(folder);
            using (var screenshot = GetViewport().GetTexture().GetImage())
                Check(screenshot.SavePng($"{folder}/{scene}.png") == Error.Ok, scene + ": dodge-wall screenshot saved");
            preview.Cancel();
        }
        var interrupted = BossDodgeWave.Begin(boss);
        Call(boss, "EnterBreak");
        hud.HideBubble();
        interrupted.Tick(2);
        Check(interrupted.Finished && !interrupted.IsStriking, scene + ": a break cancels the wall before it can strike");
        _game.TrainingSetUpgrade("n_dodge", false);
        await Frames(2);
    }

    private async Task CheckChallenge(Enemy boss, Player player, Hud hud, string scene)
    {
        _game.SelectedJob = Job.Tank;
        var original = new Vector2(global::Field.BossCenterX, 100);
        foreach (bool charged in new[] { false, true })
        {
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            var counts = new List<int>();
            foreach (var difficulty in new[] { GameManager.Diff.Normal, GameManager.Diff.Hard, GameManager.Diff.Lunatic })
            {
                _game.Difficulty = difficulty;
                boss.Position = original;
                player.Position = global::Field.PlayerStart;
                Pool.DespawnAll();
                var attack = BossCrossfire.Begin(boss);
                var seen = new HashSet<(ulong, ulong)>();
                int peak = 0;
                for (int frame = 0; frame < 720; frame++)
                {
                    attack.Tick(1d / 60);
                    StepBullets();
                    var bullets = Hostile();
                    foreach (var bullet in bullets) seen.Add((bullet.GetInstanceId(), bullet.ActivationId));
                    peak = Math.Max(peak, bullets.Length);
                }
                counts.Add(seen.Count);
                GD.Print($"[ChallengeQA] {scene}/{difficulty}/{charged}: shots/12s={seen.Count} peak={peak}");
                attack.Cancel();
            }
            Check(counts[1] >= counts[0] * 1.5 && counts[2] > counts[1],
                scene + ": Hard adds at least 50% more sustained shots than the promoted Normal; Lunatic adds more");
            foreach (var difficulty in new[] { GameManager.Diff.Hard, GameManager.Diff.Lunatic })
            {
                _game.Difficulty = difficulty;
                boss.Position = original;
                player.Position = global::Field.PlayerStart;
                Pool.DespawnAll();
                var rotation = new BossDanmaku(boss);
                Write(rotation, "_phrase", 5);
                rotation.Tick(0.71);
                var weave = Read<BossCrossfire>(rotation, "_crossfire");
                Check(weave.Weave && !weave.Sweep, scene + ": the moving-gap wall is in the live rotation");
                var gaps = new HashSet<float>();
                for (int wave = 0; wave < weave.Burst; wave++)
                {
                    gaps.Add(weave.WeavePassage);
                    var plan = Enumerable.Range(0, weave.Origins.Count).SelectMany(weave.Volley).ToArray();
                    Pool.DespawnAll();
                    weave.Tick(weave.WarningRemaining + 0.001);
                    var bullets = Hostile();
                    Check(bullets.Length == plan.Length && plan.All(slot => bullets.Count(b => Read<Vector2>(b, "_leadTo") == slot.Position) == 1),
                        scene + ": every moving-wall warning fires exactly once");
                    float clearance = bullets.SelectMany((a, i) => bullets.Skip(i + 1).Select(b =>
                        BossCharacterShotQa.Clearance(a, b, Read<Vector2>(a, "_leadTo"), Read<Vector2>(b, "_leadTo")))).Min();
                    Check(clearance >= BulletPool.BossLaneGap, scene + ": moving walls preserve core-width clearance");
                }
                Check(gaps.Count >= 2, scene + ": the gap moves instead of leaving a stationary safe lane");
                weave.Cancel();
                bool escaped = false, staticHit = false;
                foreach (int route in new[] { 0, 1, 2, 3, 4, 5, 6 })
                {
                    if (escaped) break;
                    Pool.DespawnAll();
                    boss.Position = original;
                    player.Position = global::Field.PlayerStart;
                    var trial = BossCrossfire.Begin(boss, weave: true);
                    Vector2 direction = Vector2.Zero;
                    bool hit = false;
                    for (int frame = 0; frame < 2400; frame++)
                    {
                        if (route > 0 && frame >= (route >= 4 ? 90 + (route - 4) * 30 : 21))
                        {
                            Vector2 goal = player.Position;
                            if (trial.Horizontal) goal.Y = trial.WeavePassage;
                            else goal.X = trial.WeavePassage;
                            direction = ShapeAwareDodge(player, boss, direction, route == 3 ? null : goal,
                                player.MoveSpeed, longLookahead: route == 2, precise: true);
                            player.Position = (player.Position + direction * player.MoveSpeed / 60).Clamp(
                                new Vector2(global::Field.Left + 6, 6), new Vector2(global::Field.Right - 6, global::Field.Bottom - 6));
                        }
                        trial.Tick(1d / 60);
                        StepBullets();
                        hit |= Hostile().Any(b => b.CollisionLayer != 0 && BossCharacterShotQa.HitsPlayer(b, player.Position))
                            || player.Position.DistanceTo(boss.ShotCenter) < 28;
                        if (hit || trial.Finished && Hostile().Length == 0) break;
                    }
                    if (route == 0) staticHit = hit;
                    else escaped = !hit && trial.Finished && Hostile().Length == 0;
                    GD.Print($"[ChallengeQA] {scene}/{difficulty}/{charged}: weave route={route} survived={escaped}");
                    trial.Cancel();
                }
                Check(staticHit && escaped, scene + ": weaving threatens camping but has a legal-speed, no-bomb route");
            }
        }
        Pool.DespawnAll();
        _game.Difficulty = GameManager.Diff.Hard;
        if (boss is BossMina) Write(boss, "_pattern", 0);
        else Write(boss, "_form2", false);
        boss.Position = original;
        player.Position = global::Field.PlayerStart;
        var native = new BossDanmaku(boss);
        Write(boss, "_danmaku", native);
        Write(native, "_phrase", 2);
        native.Tick(0.71);
        Check(native.CrossfireActive && Read<BossCrossfire>(native, "_crossfire").Pincer,
            scene + ": the former passive native turn now uses aimed remote pressure");
        Hud.BubblePaused = true;
        native.Tick(20);
        Check(native.CrossfireActive && Read<BossCrossfire>(native, "_crossfire").Wave == 0,
            scene + ": dialogue freezes the remote attack turn");
        Hud.BubblePaused = false;
        for (int frame = 0; frame < 240; frame++)
        {
            native.Tick(1d / 60);
            Call(boss, "UpdateMovement", 1d / 60);
            StepBullets();
        }
        Check(boss.Position.DistanceTo(original) > 5 && Hostile().Length > 0,
            scene + ": the character turn moves and fires instead of becoming a rest window");
        for (int frame = 0; frame < 900 && Read<int>(native, "_phrase") < 3; frame++) { native.Tick(1d / 60); StepBullets(); }
        native.Tick(0.13);
        Check(native.CrossfireActive && Read<BossCrossfire>(native, "_crossfire").Sweep,
            scene + ": aimed character attacks hand off to an opposite-side pressure band");
        Read<BossCrossfire>(native, "_crossfire").Cancel();
        Pool.DespawnAll();
        await Frames(2);
    }

    private async Task CheckDanmakuDensity(Enemy boss, Player player, Hud hud, string scene)
    {
        var character = Jobs.Get(boss.BodyShotJob!.Value);
        player.Position = new Vector2(global::Field.Left + 25, global::Field.CenterY);
        foreach (bool charged in new[] { false, true })
        {
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            var counts = new List<int>();
            foreach (var diff in Enum.GetValues<GameManager.Diff>())
            {
                _game.Difficulty = diff;
                Pool.DespawnAll();
                var pattern = new BossDanmaku(boss);
                pattern.Tick(0.35);
                Check(Hostile().Length == 0, scene + ": opening has a preparation beat");
                pattern.Tick(0.36);
                Check(pattern.CrossfireActive && Hostile().Length == 0, scene + ": the opening deploys visible remote emitters before firing");
                var opening = Read<BossCrossfire>(pattern, "_crossfire");
                int sources = Enumerable.Range(0, opening.Origins.Count).Count(i => opening.Volley(i).Any());
                pattern.Tick(opening.WarningRemaining + 0.001);
                var first = Hostile();
                int directions = first.Select(b => Mathf.RoundToInt(b.Velocity.Angle() * 10000)).Distinct().Count();
                Check(first.Length > 0 && directions == sources,
                    $"{scene}/{diff}/charged={charged}: {first.Length} shots form intersecting parallel lanes");
                float speed = (character.Id switch
                {
                    Job.Melee => 2f,
                    Job.Heal => charged ? 72f : 60f,
                    Job.Magic => charged ? 72f : 62f,
                    _ => charged ? 100f : 82f,
                }) * _game.BulletSpeedMul;
                Check(first.All(b => b.Accel == (character.Mode == GameManager.ShotMode.Accel)
                    && b.Homing == (character.Mode == GameManager.ShotMode.Homing) && b.Charged == charged
                    && b.Radius == (charged ? BulletPool.BossChargeRadius : BulletPool.BossRadius)
                    && Mathf.IsEqualApprox(b.Velocity.Length(), speed)
                    && ReferenceEquals(Read<BulletArt.PlayerVisual>(b, "_playerVisual"), BulletArt.PlayerShot(boss.BodyShotJob!.Value))),
                    $"{scene}/{diff}/charged={charged}: barrage retains character shot performance and artwork");
                float clearance = first.SelectMany((a, i) => first.Skip(i + 1).Select(b =>
                    BossCharacterShotQa.Clearance(a, b, Read<Vector2>(a, "_leadTo"), Read<Vector2>(b, "_leadTo")))).Min();
                Check(clearance >= BulletPool.BossLaneGap && first.All(b => b.CollisionLayer == 0 && b.LeadIn),
                    $"{scene}/{diff}: deployment is harmless and lanes are {clearance:0.0}px, wider than the 4px player core");
                Vector2 aimedAt = player.GlobalPosition;
                var centers = first.Where(b => b.BossAimTarget.HasValue).ToArray();
                Check(centers.Length == sources && centers.All(b => b.BossAimTarget == aimedAt
                    && b.Velocity.Normalized().Dot(Read<Vector2>(b, "_leadTo").DirectionTo(aimedAt)) > 0.99999f),
                    $"{scene}/{diff}: every edge sends a lane through the player");
                player.Position += new Vector2(0, 35);
                pattern.Tick(opening.WarningRemaining + 0.001);
                if (character.Id == Job.Tank && diff == GameManager.Diff.Lunatic)
                {
                    while (opening.Wave < 6)
                    {
                        Check(opening.Target == aimedAt,
                            scene + ": Lunatic pincer keeps aligned lanes through its rotating emitter group");
                        pattern.Tick(opening.WarningRemaining + 0.001);
                    }
                }
                Check(opening.Target == player.GlobalPosition && centers.All(b => b.BossAimTarget == aimedAt),
                    scene + ": next target acquisition leaves committed shots unchanged");
                player.Position -= new Vector2(0, 35);
                for (int frame = 0; frame < 720; frame++)
                    foreach (var (bullet, id) in Hostile().Select(b => (b, b.ActivationId)).ToArray())
                        if (bullet.Active && bullet.ActivationId == id)
                        { bullet.SetPhysicsProcess(false); bullet._PhysicsProcess(1d / 60); }
                Check(Hostile().Length == 0, scene + ": character waves and all descendants leave the field without lingering");
                Pool.DespawnAll();
                opening.Cancel();
                pattern = new BossDanmaku(boss);
                var fired = new HashSet<(ulong, ulong)>();
                for (int frame = 0; frame < 1200; frame++)
                {
                    pattern.Tick(1d / 60);
                    foreach (var shot in Hostile()) fired.Add((shot.GetInstanceId(), shot.ActivationId));
                    StepBullets();
                }
                int count = Hostile().Length;
                counts.Add(fired.Count);
                GD.Print($"[CombatQA] DENSITY {scene}/{diff}/charged={charged}: {fired.Count} shots/20s");
                Check(Read<int>(pattern, "_phrase") > 0, scene + ": remote pressure advances into the next attack family");
                Call(boss, "EnterBreak");
                hud.HideBubble();
                for (int frame = 0; frame < 600; frame++) pattern.Tick(1d / 60);
                Check(Hostile().Length == count, $"{scene}/{diff}: shield break retains old bullets without spawning more");
                Call(boss, "EnterReclose");
                hud.HideBubble();
                int reformCount = Hostile().Length;
                pattern.Tick(1);
                Check(boss.GaugeReforming && Hostile().Length == reformCount, scene + ": shield reform does not launch a surprise wave");
                if (boss is BossMina && Read<CharacterStoryTalk?>(boss, "_recloseTalk") is { Active: true } talk)
                    Call(talk, "Finish", hud);
                Call(boss, "EnterShielded");
                hud.HideBubble();
                Pool.DespawnAll();
                pattern.Tick(0.35);
                Check(Hostile().Length == 0, scene + ": new shield gives a restart grace period");
                pattern.Tick(0.36);
                Check(Hostile().Length > 0 || pattern.CrossfireActive || pattern.DodgeWaveActive,
                    scene + ": barrage or character attacks resume after the grace period");
                if (Read<BossCrossfire?>(pattern, "_crossfire") is { } remaining) remaining.Cancel();
            }
            Check(counts.Zip(counts.Skip(1), (a, b) => b > a).All(increasing => increasing),
                $"{scene}/charged={charged}: density increases Easy < Normal < Hard < Lunatic");
        }
        if (boss is BossMina) Write(boss, "_pattern", 0);
        else Write(boss, "_form2", false);
        CheckStationaryTargets(boss, player, scene);
        Pool.DespawnAll();
        _game.Difficulty = GameManager.Diff.Hard;
        var formation = new BossDanmaku(boss);
        Write(boss, "_danmaku", formation);
        formation.Tick(0.71);
        int formationCount = Hostile().Length;
        var formationOrigin = boss.GlobalPosition;
        Call(boss, "UpdateMovement", 0.1d);
        Call(boss, "FirePattern", 3d);
        Check(formation.Holding && boss.GlobalPosition == formationOrigin && Hostile().Length == formationCount,
            scene + ": formation holds its origin without layering unrelated body attacks");
        player.GlobalPosition = boss.ShotCenter + Vector2.Left * 35;
        formation.Tick(0.01);
        Check(formation.CrossfireActive, scene + ": approaching the boss cannot cancel remote pressure");
        Read<BossCrossfire>(formation, "_crossfire").Cancel();
        player.Position = new Vector2(global::Field.Left + 25, global::Field.CenterY);
        Pool.DespawnAll();
        Hud.BubblePaused = true;
        boss._PhysicsProcess(1);
        Check(Hostile().Length == 0, scene + ": dialogue suspends the denser barrage");
        Hud.BubblePaused = false;
        foreach (string group in new[] { "aoe", "boss_edge_volleys", "boss_animal_techniques" })
        {
            var hazard = new Node();
            boss.GetParent().AddChild(hazard);
            hazard.AddToGroup(group);
            var pattern = new BossDanmaku(boss);
            pattern.Tick(5);
            Check(Hostile().Length == 0, scene + ": barrage yields to " + group);
            hazard.Free();
            pattern.Tick(0.35);
            Check(Hostile().Length == 0, scene + ": special attack exit has no catch-up burst");
            pattern.Tick(0.36);
            Check(pattern.CrossfireActive, scene + ": barrage resumes with a warning after " + group);
            Read<BossCrossfire>(pattern, "_crossfire").Cancel();
            Pool.DespawnAll();
        }
        player.GlobalPosition = boss.ShotCenter + Vector2.Left * 35;
        var closePattern = new BossDanmaku(boss);
        closePattern.Tick(2);
        Check(Hostile().Length == 0, scene + ": point-blank movement cannot spawn a ring on the player");
        player.Position = new Vector2(global::Field.Left + 25, global::Field.CenterY);
        closePattern.Tick(0.2);
        Check(Hostile().Length == 0, scene + ": leaving point-blank range has a reaction beat");
        Read<BossCrossfire>(closePattern, "_crossfire").Cancel();
        if (boss is BossAkari)
            foreach (var diff in Enum.GetValues<GameManager.Diff>())
            {
                _game.Difficulty = diff;
                Pool.DespawnAll();
                Call(boss, "AimedSpread", Pool);
                Check(Hostile().Length == (diff >= GameManager.Diff.Hard ? (_game.IsLunatic ? 15 : 13) : Mathf.Min(5, _game.ScaleBulletsOdd(3))),
                    $"Akari/{diff}: aimed fan scales within lane spacing limits");
                Pool.DespawnAll();
                Call(boss, "Spiral", Pool);
                Check(Hostile().Length == (diff >= GameManager.Diff.Hard ? (_game.IsLunatic ? 7 : 5) : Mathf.Max(2, _game.ScaleBullets(2))),
                    $"Akari/{diff}: spiral scales with difficulty");
            }
        foreach (var diff in new[] { GameManager.Diff.Hard, GameManager.Diff.Lunatic })
        foreach (bool charged in new[] { false, true })
        {
            _game.Difficulty = diff;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            string[] fans = boss switch
            {
                BossAkari => new[] { "FanDown", "AimedSpread" },
                BossKoharu => new[] { "FanDown", "Aimed" },
                _ => new[] { "Aimed" },
            };
            foreach (string fan in fans)
            {
                Pool.DespawnAll();
                Call(boss, fan, Pool);
                var shots = Hostile();
                Vector2 aim = boss.ShotCenter.DirectionTo(player.Position);
                var angles = shots.Select(b => aim.AngleTo(b.Velocity.Normalized())).ToArray();
                Check(angles.Max() - angles.Min() >= Mathf.DegToRad(150), $"{scene}/{diff}/{charged}/{fan}: fan spans the arena rather than a narrow target cone");
                float clearance = shots.SelectMany((a, i) => shots.Skip(i + 1).Select(b =>
                    BossCharacterShotQa.Clearance(a, b, boss.ShotCenter + a.Velocity.Normalized() * 64,
                        boss.ShotCenter + b.Velocity.Normalized() * 64))).Min();
                Check(clearance >= Player.HitRadius * 2 + 2, $"{scene}/{diff}/{charged}/{fan}: the expanded fan keeps a real dodge lane ({clearance:F1}px)");
            }
            Pool.DespawnAll();
            string key = scene switch { "Akari" => "akari", "Koharu" => "koharu", "Rei" => "rei", _ => "mina" };
            for (int side = 0; side < 4; side++)
            {
                var edge = BossEdgeVolley.Begin(boss, key, side);
                edge.SetPhysicsProcess(false);
                var gates = Read<Vector2[]>(edge, "_gates");
                bool vertical = edge.Side is BossEdgeVolley.Edge.Top or BossEdgeVolley.Edge.Bottom;
                var axes = gates.Select(p => vertical ? p.X : p.Y).OrderBy(v => v).ToArray();
                float low = vertical ? global::Field.Left : global::Field.Top;
                float high = vertical ? global::Field.Right : global::Field.Bottom;
                Check(axes.First() - low <= 22 && high - axes.Last() <= 22 && axes.Zip(axes.Skip(1)).All(p => p.Second - p.First >= 18),
                    $"{scene}/{diff}: edge volley covers both ends and retains its lanes");
                edge.Cancel();
            }
        }
        if (boss is BossMina) Write(boss, "_pattern", 0);
        else Write(boss, "_form2", false);
        Pool.DespawnAll();
        if (OS.GetCmdlineUserArgs().Contains("--shots"))
        {
            _game.Difficulty = GameManager.Diff.Hard;
            Pool.DespawnAll();
            hud.HideSpellCard();
            Write(hud, "_bossLineTimer", 0d);
            Engine.TimeScale = 1;
            await Frames(240);
            foreach (int phrase in new[] { 0, 1 })
            {
                Pool.DespawnAll();
                var pattern = new BossDanmaku(boss);
                Write(pattern, "_phrase", phrase);
                for (int frame = 0; frame < 146; frame++)
                {
                    pattern.Tick(1d / 60);
                    foreach (var (bullet, id) in Hostile().Select(b => (b, b.ActivationId)).ToArray())
                        if (bullet.Active && bullet.ActivationId == id)
                        { bullet.SetPhysicsProcess(false); bullet._PhysicsProcess(1d / 60); }
                }
                await Frames(3);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string folder = ProjectSettings.GlobalizePath("res://build/qa_story/danmaku_density");
                DirAccess.MakeDirRecursiveAbsolute(folder);
                using var image = GetViewport().GetTexture().GetImage();
                string suffix = phrase == 0 ? "_hard.png" : "_curtain_hard.png";
                Check(image.SavePng(folder + "/" + scene + suffix) == Error.Ok, "HARD barrage screenshot saved");
            }
        }
        _game.Difficulty = GameManager.Diff.Normal;
    }
    private void CheckStationaryTargets(Enemy boss, Player player, string scene)
    {
        Vector2 original = boss.GlobalPosition;
        var cases = new[]
        {
            (new Vector2(334, 108), new Vector2(86, 108)),
            (new Vector2(116, 108), new Vector2(364, 108)),
            (new Vector2(225, 150), new Vector2(225, 8)),
            (new Vector2(225, 60), new Vector2(225, 208)),
            (new Vector2(334, 100), new Vector2(global::Field.Left, global::Field.Top)),
            (new Vector2(116, 100), new Vector2(global::Field.Right, global::Field.Top)),
            (new Vector2(334, 100), new Vector2(global::Field.Left, global::Field.Bottom)),
            (new Vector2(116, 100), new Vector2(global::Field.Right, global::Field.Bottom)),
        };
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        foreach (bool charged in new[] { false, true })
        foreach (int phrase in diff >= GameManager.Diff.Hard ? new[] { 0, 1, 2, 3, 5, 7 } : new[] { 0, 1 })
        {
            _game.Difficulty = diff;
            if (boss is BossMina) Write(boss, "_pattern", charged ? BossMina.DragonPhase : 0);
            else Write(boss, "_form2", charged);
            foreach (var (origin, target) in cases)
            {
                Pool.DespawnAll();
                boss.GlobalPosition = origin;
                player.GlobalPosition = target;
                var pattern = new BossDanmaku(boss);
                Write(pattern, "_phrase", phrase);
                pattern.Tick(0.71);
                Check(Hostile().Length == 0 && pattern.CrossfireActive, scene + ": remote emitters warn before firing");
                var attack = Read<BossCrossfire>(pattern, "_crossfire");
                int sources = Enumerable.Range(0, attack.Origins.Count).Count(i => attack.Volley(i).Any());
                pattern.Tick(attack.WarningRemaining + 0.001);
                var shots = Hostile();
                Check(shots.Length > 0 && shots.All(b => b.BossStyle == BossShotStyle.Aimed),
                    $"{scene}/{diff}/{charged}/{phrase}: parallel formations keep a consistent visual identity");
                int directions = shots.Select(b => Mathf.RoundToInt(b.Velocity.Angle() * 1000)).Distinct().Count();
                Check(attack.Pincer ? directions == sources
                    : attack.Sweep ? directions == (boss is BossKoharu ? 1 : 2)
                    : diff >= GameManager.Diff.Normal ? directions == 1 : directions >= 2,
                    scene + ": remote pressure intersects while the curtain retains parallel lanes");
                float clearance = shots.SelectMany((a, i) => shots.Skip(i + 1).Select(b =>
                    BossCharacterShotQa.Clearance(a, b, Read<Vector2>(a, "_leadTo"), Read<Vector2>(b, "_leadTo")))).Min();
                Check(clearance >= BulletPool.BossLaneGap - 0.001f, scene + ": aimed formation preserves dodge lanes");
                bool hit = false;
                for (int frame = 0; frame < 600 && !hit; frame++)
                    foreach (var (bullet, id) in Hostile().Select(b => (b, b.ActivationId)).ToArray())
                    {
                        if (!bullet.Active || bullet.ActivationId != id) continue;
                        bullet.SetPhysicsProcess(false);
                        bullet._PhysicsProcess(1d / 60);
                        if (!bullet.Active || bullet.ActivationId != id || bullet.CollisionLayer == 0) continue;
                        hit |= BossCharacterShotQa.HitsPlayer(bullet, target);
                    }
                Check(hit, $"{scene}/{diff}/{charged}/{phrase}: staying at {target} is hit, including split descendants");
                if (Read<BossCrossfire?>(pattern, "_crossfire") is { } crossfire) crossfire.Cancel();
            }
        }
        Pool.DespawnAll();
        boss.GlobalPosition = original;
        player.Position = new Vector2(global::Field.Left + 25, global::Field.CenterY);
        if (boss is BossMina) Write(boss, "_pattern", 0);
        else Write(boss, "_form2", false);
    }
    private async Task CheckPlayerAndVolleys(Node world, Player player, Enemy boss)
    {
        Write(player, "_locked", true); Write(player, "_lockTarget", boss);
        float locked = player.MoveSpeed;
        Write(player, "_locked", false);
        Check(Mathf.IsEqualApprox(locked, player.MoveSpeed * .8f), "lock movement is 80 percent of normal speed");
        Pool.DespawnAll();
        var volley = BossEdgeVolley.Begin(boss, "akari", 0);
        volley.SetPhysicsProcess(false);
        volley._PhysicsProcess(1.6);
        var shots = Hostile().Select(b => (Bullet: b, Id: b.ActivationId)).ToArray();
        Check(shots.Length > 0, "edge attack emits a volley");
        volley.Cancel();
        await Frames(2);
        Check(shots.All(s => s.Bullet.Active && s.Bullet.ActivationId == s.Id), "stopping a volley retains emitted bullets");
        Pool.DespawnAll();
        var hostile = Pool.Spawn(new Vector2(280, 108), Vector2.Zero, true, 3);
        hostile.SetPhysicsProcess(false);
        var friendly = Pool.Spawn(hostile.Position, Vector2.Zero, false, 3);
        friendly.SetPhysicsProcess(false);
        await Frames(4);
        Check(hostile.Active && friendly.Active, "player bullets cannot destroy enemy bullets");
        Call(player, "TryBomb");
        Check(hostile.Active, "bomb protection does not erase enemy bullets");
        Pool.DespawnAll();
        var target = new Node2D { Position = new Vector2(300, 60) };
        world.AddChild(target);
        var accel = Pool.Spawn(new Vector2(130, 108), Vector2.Right * 640, false, 3.4f);
        accel.SetPhysicsProcess(false);
        accel.MakeAccel(12, 640, .8f);
        accel.SetLaunchTarget(target, null);
        for (int frame = 0; frame < 49; frame++)
        {
            target.Position = new Vector2(300, frame < 30 ? 60 + frame * 2 : 120);
            accel._PhysicsProcess(1d / 60);
        }
        Check(!accel.AccelCharging && accel.Velocity.Normalized().Dot(accel.Position.DirectionTo(target.Position)) > .999f,
            "Akari re-aims at the moving lock target when launching");
        Pool.Despawn(accel);
        var recycled = Pool.Spawn(Vector2.Zero, Vector2.Right * 640, false);
        Check(Read<Node2D?>(recycled, "_launchTarget") == null, "reused bullets do not retain a previous lock target");
        target.QueueFree(); Pool.DespawnAll();
    }
}
