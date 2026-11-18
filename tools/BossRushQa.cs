using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossRushQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type[] BossTypes = { typeof(BossAkari), typeof(BossKoharu), typeof(BossRei), typeof(BossMina) };
    private static readonly string[] SoloRecords = { "boss_solo_akari", "boss_solo_koharu", "boss_solo_rei", "boss_solo_mina" };
    private GameManager _game = null!;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static void Property(object obj, string name, object value) => obj.GetType().GetProperty(name)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[BossRushQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            Read<System.Collections.Generic.HashSet<string>>(_game, "_readLines").Clear();
            _game.AutoSaveEnabled = false;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            GetTree().Paused = false;
            bool reiDamage = OS.GetCmdlineUserArgs().Contains("--rei-damage-only");
            bool health = OS.GetCmdlineUserArgs().Contains("--health-only");
            bool finaleRotation = OS.GetCmdlineUserArgs().Contains("--finale-rotation-only");
            if (health || reiDamage || finaleRotation || OS.GetCmdlineUserArgs().Contains("--rotation-only"))
            {
                await Frames(3);
                if (health) await CheckHealthScaling();
                else if (reiDamage) await CheckReiDamage();
                else
                {
                    GD.Seed(20261106);
                    CheckRotationHistory();
                    if (!finaleRotation)
                    {
                        await CheckRotations();
                        await CheckCameoRotations();
                    }
                    await CheckFinaleInterruptions();
                }
                Audio.Instance?.StopMusic(0);
                foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
                { audio.Stop(); audio.Stream = null; }
                await Frames(5);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Frames(5);
                GD.Print(health ? "[BossRushQA] HEALTH ALL PASS (initialization and gauge checks; not a clear-time test)" : reiDamage
                    ? "[BossRushQA] REI DAMAGE ALL PASS (forced exposure and projectile hit callbacks; not a difficulty test)"
                    : "[BossRushQA] ROTATION ALL PASS (synthetic completion and phase changes; not a difficulty test)");
                GetTree().Quit();
                return;
            }
            if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetPosition(new Vector2I(-16000, -16000));
            await Frames(3);
            await CheckAlwaysAvailable();
            bool uiOnly = OS.GetCmdlineUserArgs().Contains("--ui-only");
            if (uiOnly) await CheckMenuStyles();
            else if (!OS.GetCmdlineUserArgs().Contains("--retry-only"))
            {
                foreach (var diff in Enum.GetValues<GameManager.Diff>()) await CheckRun(diff);
                for (int boss = 0; boss < BossTypes.Length; boss++) await CheckSingleRun(boss);
                await CheckRecordsPersist();
            }
            if (!uiOnly)
            {
                await CheckRetry(single: false);
                await CheckRetry(single: true);
            }
            Check(!GameManager.BossRushActive && !GameManager.SkipBossStory, "story mode restored after leaving both boss modes");
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Frames(10);
            GD.Print(uiOnly ? "[BossRushQA] MENU UI ALL PASS (layout, selection and scene transitions)"
                : "[BossRushQA] ALL PASS (direct damage, attack prerequisites and invulnerability used; not a difficulty test)");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[BossRushQA] FAIL {e}");
            GetTree().Quit(1);
        }
    }

    private T Mount<T>(string scene) where T : Node
    {
        var root = GD.Load<PackedScene>(scene).Instantiate<T>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        return root;
    }

    private async Task CheckHealthScaling()
    {
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
            foreach (bool solo in new[] { false, true })
                for (int index = 0; index < BossTypes.Length; index++)
                {
                    _game.Difficulty = diff;
                    _game.PendingBossIndex = index;
                    var root = Mount<BossRushRoot>(solo ? BossRushRoot.SingleScenePath : BossRushRoot.ScenePath);
                    root.SetProcess(false);
                    if (!solo) Property(root, nameof(BossRushRoot.Round), index);
                    Call(root, "SpawnBoss");
                    root.World.ProcessMode = ProcessModeEnum.Disabled;
                    var boss = root.Boss!;
                    int barHp = solo && diff == GameManager.Diff.Hard
                        ? new[] { 800, 900, 748, 650 }[index] : new[] { 100, 125, 150, 175 }[(int)diff];
                    int bars = new[] { 3, 4, 5, 6 }[index];
                    int maxHp = (int)typeof(Enemy).GetField("_maxHp", Private)!.GetValue(boss)!;
                    Check(boss.BarHp == barHp && boss.TotalBars == bars && maxHp == barHp * bars,
                        $"{diff}/{(solo ? "solo" : "rush")}/{BossTypes[index].Name}: HP={maxHp}, bars={bars}, per_bar={barHp}");
                    typeof(Enemy).GetField("_hp", Private)!.SetValue(boss, maxHp - barHp);
                    Check(boss.CurrentBarIndex == bars - 2 && boss.CurrentBarFrac == 1,
                        "one depleted bar advances the gauge without changing its scale");
                    typeof(Enemy).GetField("_hp", Private)!.SetValue(boss, 1);
                    Check(boss.CurrentBarIndex == 0 && Mathf.IsEqualApprox(boss.CurrentBarFrac, 1f / barHp),
                        "the final HP remains represented in the gauge");
                    await Remove(root);
                }
    }

    private void CheckRotationHistory()
    {
        var history = new BossAttackCycle.History();
        var slots = new[] { new BossAttackCycle.Slot(BossTechniqueKind.Extra, 1), new BossAttackCycle.Slot(BossTechniqueKind.Normal) };
        history.Start(slots[0]);
        Check(history.Next(slots, 0, false) == 1, "an interrupted attack is excluded before any completion");
        history.Complete(slots[0], false);
        Check(history.Next(slots, 0, true) == 1, "a form change cannot repeat the last attack slot");
        history.Start(slots[1]);
        Check(history.Next(slots, 1, true) == 0, "an interrupted attack remains eligible after another attack");
    }

    private async Task CheckRotations()
    {
        GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
        GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
            for (int index = 0; index < BossTypes.Length; index++)
                foreach (bool partial in new[] { false, true })
                foreach (bool story in index == 3 ? new[] { false, true } : new[] { false })
                {
                    _game.Difficulty = diff;
                    _game.PendingBossIndex = index;
                    var root = Mount<BossRushRoot>(BossRushRoot.SingleScenePath);
                    root.SetProcess(false);
                    Call(root, "SpawnBoss");
                    if (story) root.RemoveFromGroup("boss_rush");
                    root.World.ProcessMode = ProcessModeEnum.Disabled;
                    var boss = root.Boss!;
                    boss.PrepareTechniquePractice(0);
                    root.Hud.HideBubble();
                    Hud.BubblePaused = false;
                    var danmaku = (BossDanmaku)typeof(Enemy).GetField("_danmaku", Private)!.GetValue(boss)!;
                    var used = new HashSet<(BossAttackCycle.Slot Slot, bool Enhanced)>();
                    (BossAttackCycle.Slot Slot, bool Enhanced)? last = null;
                    int count = 0, phases = boss is BossMina ? 5 : diff == GameManager.Diff.Easy || boss is BossAkari ? 2 : 3;
                    for (int phase = 0; phase < phases; phase++)
                    {
                        if (boss is BossMina mina)
                        {
                            Write(mina, "_pattern", phase);
                            Read<MinaPhaseAttacks>(mina, "_caster").BeginPhase(phase);
                        }
                        else typeof(Enemy).GetField("_form2", Private)!.SetValue(boss, phase > 0);
                        int maxHp = (int)typeof(Enemy).GetField("_maxHp", Private)!.GetValue(boss)!;
                        typeof(Enemy).GetField("_hp", Private)!.SetValue(boss, phase == 2 ? maxHp / 5 : maxHp);
                        boss.GlobalPosition = new Vector2(Field.BossCenterX, Field.BossZoneCenterY);
                        danmaku.Tick(0);
                        int segment = BossAttackCycle.Segment(boss);
                        var slots = boss.UsesTechniqueCycle ? BossAttackCycle.Slots(boss, segment) : BossAttackCycle.EasySlots(boss, phase);
                        if (boss is BossMina && !story)
                            Check(slots.Count == new[] { 8, 9, 8, 8, 6 }[phase] && slots.Distinct().Count() == slots.Count
                                && Enumerable.Range(0, 5).All(v => slots.Contains(new BossAttackCycle.Slot(BossTechniqueKind.Area, v))),
                                $"{diff}/Mina/{phase}: five phase attacks and current unique techniques available in solo");
                        var phaseSeen = new HashSet<BossAttackCycle.Slot>();
                        int iterations = partial && phase < phases - 1 ? 1 : slots.Count * 2;
                        for (int n = 0; n < iterations; n++)
                        {
                            BossCharacterTechnique StartNext()
                            {
                                for (int tick = 0; tick < 6 && danmaku.ActiveTechnique is not { Finished: false }; tick++)
                                {
                                    boss.TickBossFrame(1);
                                    danmaku.Tick(1);
                                }
                                var active = danmaku.ActiveTechnique;
                                if (active is not { Finished: false }) throw new Exception($"{diff}/{index}/{phase}: selection stalled");
                                return active;
                            }
                            var attack = StartNext();
                            var slot = new BossAttackCycle.Slot(attack.TechniqueKind, attack.TechniqueVariant);
                            var key = (slot, slot.Kind == BossTechniqueKind.Extra && attack.IsEnhanced);
                            Check(slot != last?.Slot, $"{diff}/{index}/{phase}: no consecutive attack {slot}");
                            bool opener = boss is BossMina phaseBoss && !Read<MinaPhaseAttacks>(phaseBoss, "_caster").OpenerCompleted;
                            Check(opener || !used.Contains(key)
                                || slots.Where(s => s != last?.Slot).All(s => used.Contains((s, s.Kind == BossTechniqueKind.Extra && attack.IsEnhanced))),
                                $"{diff}/{index}/{phase}: unseen eligible attacks precede repeats");
                            if (n == 0)
                            {
                                attack.Cancel();
                                attack = StartNext();
                                var following = new BossAttackCycle.Slot(attack.TechniqueKind, attack.TechniqueVariant);
                                Check(slot != following, $"{diff}/{index}/{phase}: interruption selects a different technique");
                                slot = following;
                                key = (slot, slot.Kind == BossTechniqueKind.Extra && attack.IsEnhanced);
                            }
                            typeof(BossCharacterTechnique).GetMethod("Finish", Private)!.Invoke(attack, null);
                            Check(attack.Completed, $"{diff}/{index}/{phase}: completion acknowledged");
                            used.Add(key);
                            phaseSeen.Add(slot);
                            last = key;
                            count++;
                            await Frames(1);
                        }
                        if (iterations >= slots.Count)
                            Check(slots.All(phaseSeen.Contains), $"{diff}/{index}/{phase}: every eligible technique selected");
                        if (partial && phase < phases - 1)
                        {
                            for (int tick = 0; tick < 6 && danmaku.ActiveTechnique is not { Finished: false }; tick++)
                            { boss.TickBossFrame(1); danmaku.Tick(1); }
                            var interrupted = danmaku.ActiveTechnique!;
                            Check(interrupted is { Finished: false }, $"{diff}/{index}/{phase}: active attack before phase change");
                            last = (new(interrupted.TechniqueKind, interrupted.TechniqueVariant),
                                interrupted.TechniqueKind == BossTechniqueKind.Extra && interrupted.IsEnhanced);
                        }
                    }
                    if (!story) CheckEncounter(root, index, "rotation");
                    GD.Print($"[BossRushQA] ROTATION difficulty={diff} boss={index} partial={partial} story={story} completed={count}");
                    await Remove(root);
                }
    }

    private async Task CheckCameoRotations()
    {
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        foreach (var theme in Enum.GetValues<CameoFireTheme>())
        {
            _game.Difficulty = diff;
            var root = Mount<BossRushRoot>(BossRushRoot.SingleScenePath);
            root.SetProcess(false);
            root.World.ProcessMode = ProcessModeEnum.Disabled;
            string id = theme == CameoFireTheme.AkariGrief ? "akari" : theme == CameoFireTheme.KoharuFalling ? "koharu" : "rei";
            var boss = new CameoBoss { Theme = new CameoTheme {
                DisplayName = id, Handle = id, Fire = theme,
                PreTex = $"res://char/v3/{id}_mid{(id == "akari" ? "_v2" : "")}.png", CryTex = $"res://char/v3/{id}_mid.png",
                PostTex = $"res://char/v3/{id}_mid.png", SpellTint = Colors.White,
                IntroLines = Array.Empty<(int, string, string)>(), TauntLines = Array.Empty<(int, string, string)>(),
                DefeatLines = Array.Empty<(int, string, string)>() } };
            root.World.AddChild(boss);
            boss.PrepareTechniquePractice(0);
            root.Hud.HideBubble();
            Hud.BubblePaused = false;
            var danmaku = (BossDanmaku)typeof(Enemy).GetField("_danmaku", Private)!.GetValue(boss)!;
            BossAttackCycle.Slot? last = null;
            for (int n = 0; n < 12; n++)
            {
                for (int tick = 0; tick < 6 && danmaku.ActiveTechnique is not { Finished: false }; tick++)
                { boss.TickBossFrame(1); danmaku.Tick(1); }
                var attack = danmaku.ActiveTechnique;
                Check(attack is { Finished: false }, $"{diff}/{theme}: middle boss selection continues");
                var slot = new BossAttackCycle.Slot(attack!.TechniqueKind, attack.TechniqueVariant);
                Check(slot != last, $"{diff}/{theme}: middle boss alternates after completion and cancellation");
                last = slot;
                if (n % 3 == 0) typeof(BossCharacterTechnique).GetMethod("Finish", Private)!.Invoke(attack, null);
                else attack.Cancel();
                await Frames(1);
            }
            await Remove(root);
        }
    }

    private async Task CheckFinaleInterruptions()
    {
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        foreach (int index in new[] { 1, 2 })
        {
            _game.Difficulty = diff;
            _game.PendingBossIndex = index;
            var root = Mount<BossRushRoot>(BossRushRoot.SingleScenePath);
            root.SetProcess(false);
            Call(root, "SpawnBoss");
            root.World.ProcessMode = ProcessModeEnum.Disabled;
            var boss = root.Boss!;
            boss.PrepareTechniquePractice(0);
            typeof(Enemy).GetField("_form2", Private)!.SetValue(boss, true);
            typeof(Enemy).GetField("_hp", Private)!.SetValue(boss, boss.BarHp * boss.TotalBars / 5);
            root.Hud.HideBubble();
            Hud.BubblePaused = false;
            var caster = boss.GetChildren().OfType<AreaSpellCaster>().Single();
            var danmaku = (BossDanmaku)typeof(Enemy).GetField("_danmaku", Private)!.GetValue(boss)!;
            caster.Suppressed = index == 1;
            caster.RequestFinale();
            BossCharacterTechnique Next()
            {
                for (int tick = 0; tick < 10; tick++)
                {
                    boss.TickBossFrame(1);
                    danmaku.Tick(1);
                    caster._Process(1);
                    var attack = boss.GetChildren().OfType<BossCharacterTechnique>().FirstOrDefault(a => !a.Finished);
                    if (attack != null) return attack;
                }
                throw new Exception($"{diff}/{index}: pending finale stalled selection");
            }
            var finale = Next();
            Check(finale.TechniqueKind == BossTechniqueKind.Finale, $"{diff}/{index}: requested finale starts");
            for (int retry = 0; retry < 3; retry++)
            {
                finale.Cancel();
                var following = Next();
                Check(following.TechniqueKind != BossTechniqueKind.Finale && !caster.FinaleCompleted,
                    $"{diff}/{index}: cancelled finale allows a different attack while completion stays false");
                typeof(BossCharacterTechnique).GetMethod("Finish", Private)!.Invoke(following, null);
                finale = Next();
                Check(finale.TechniqueKind == BossTechniqueKind.Finale, $"{diff}/{index}: pending finale resumes after another attack");
                await Frames(1);
            }
            typeof(BossCharacterTechnique).GetMethod("Finish", Private)!.Invoke(finale, null);
            caster.Suppressed = false;
            Check(caster.FinaleCompleted && Next().TechniqueKind != BossTechniqueKind.Finale,
                $"{diff}/{index}: completed finale advances to a different attack");
            await Remove(root);
        }
    }

    private async Task CheckReiDamage()
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        var hpField = typeof(Enemy).GetField("_hp", Private)!;
        var expose = typeof(Enemy).GetMethod("EnterExposed", Private)!;
        var hit = typeof(Enemy).GetMethod("OnBodyHitByPlayerBullet", Private)!;
        foreach (var job in new[] { Job.Melee, Job.Tank, Job.Heal, Job.Magic })
        foreach (string attack in new[] { "rapid", "charge", "bomb" })
        {
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = GameManager.Diff.Lunatic;
            _game.SelectedJob = job;
            _game.PendingBossIndex = 2;
            var root = Mount<BossRushRoot>(BossRushRoot.SingleScenePath);
            root.SetProcess(false);
            Call(root, "SpawnBoss");
            root.World.ProcessMode = ProcessModeEnum.Disabled;
            var boss = (BossRei)root.Boss!;
            boss.PrepareTechniquePractice(0);
            root.Hud.HideBubble();
            Hud.BubblePaused = false;
            root.Player.GlobalPosition = boss.GlobalPosition + new Vector2(-42, 0);
            var segments = new HashSet<int>();
            int hits = 0;
            while (!boss.IsPurified && hits < boss.BarHp * boss.TotalBars)
            {
                segments.Add(BossAttackCycle.Segment(boss));
                expose.Invoke(boss, null);
                int before = (int)hpField.GetValue(boss)!;
                if (attack == "bomb") boss.Purify();
                else
                {
                    if (attack == "charge") Call(root.Player, "FireCharge", ChargeTier.Second);
                    else Call(root.Player, "Fire");
                    var shots = pool.GetChildren().OfType<Bullet>().Where(b => b.Active && !b.IsEnemy).ToArray();
                    if (shots.Length == 0) throw new Exception($"{job}/{attack}: no player projectile emitted");
                    foreach (var shot in shots) hit.Invoke(boss, new object[] { shot });
                }
                pool.DespawnAll();
                boss._Process(0);
                int after = (int)hpField.GetValue(boss)!;
                if (after >= before)
                    throw new Exception($"Rei/{job}/{attack}: damage stalled at HP {after}/{boss.BarHp * boss.TotalBars}, evolved={boss.HasEvolved}, segment={BossAttackCycle.Segment(boss)}");
                hits++;
                if (hits % 32 == 0) await Frames(1);
            }
            Check(boss.IsPurified && boss.Finished && segments.SetEquals(new[] { 0, 1, 2 }),
                $"Rei/Lunatic/{job}/{attack}: projectile/bomb damage crosses both HP gates and defeats the boss ({hits} volleys)");
            root._Process(.1);
            Check(root.Completed && _game.GetBestTime("boss_solo_rei", GameManager.Diff.Lunatic).HasValue,
                $"Rei/{job}/{attack}: solo clear completes and records the result");
            await Remove(root);
        }
    }

    private async Task CheckMenuStyles()
    {
        _game.SelectedJob = Job.Melee;
        _game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Normal, 180);
        foreach (string scene in new[] { BossRushRoot.ScenePath, BossRushRoot.SingleScenePath })
        {
            _game.PendingStageScene = scene;
            _game.PendingBossIndex = 2;
            _game.Difficulty = GameManager.Diff.Lunatic;
            var difficulty = Mount<DiffSelect>("res://DiffSelect.tscn");
            Check((bool)Call(difficulty, "Selectable", 3)!, "story clear opens LUNATIC in the redesigned menu");
            await Shot(scene == BossRushRoot.ScenePath ? "rush_lunatic" : "solo_lunatic");
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Shot(scene == BossRushRoot.ScenePath ? "rush_small" : "solo_small");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Remove(difficulty);
        }
        var select = Mount<BossSelectRoot>(BossSelectRoot.ScenePath);
        for (int i = 0; i < 4; i++)
        {
            Write(select, "_selected", i);
            await Shot($"boss_select_{i}");
        }
        await Remove(select);
        var practice = Mount<BossPracticeRoot>(BossPracticeRoot.ScenePath);
        for (int boss = 0; boss < 4; boss++)
        {
            for (int phase = 0; phase < (boss == 3 ? 5 : 2); phase++)
            {
                Write(practice, "_bossIndex", boss);
                Write(practice, "_phase", phase);
                Write(practice, "_difficulty", 3);
                practice.ShowSelection();
                await Frames(4);
                var slots = Read<BossAttackCycle.Slot[]>(practice, "_slots");
                for (int i = 0; i < slots.Length; i++)
                {
                    var rect = (Rect2)Call(practice, "ButtonRect", i + 3)!;
                    Check(rect.End.X <= 1240 && rect.End.Y <= 496,
                        $"boss {boss} phase {phase}: technique {i + 1}/{slots.Length} remains inside the console panel ({rect})");
                }
                await Shot($"practice_{boss}_{phase}");
            }
        }
        await Remove(practice);
        _game.Difficulty = GameManager.Diff.Normal;
    }

    private async Task Remove(Node node)
    {
        node.QueueFree();
        await Frames(5);
    }

    private async Task CheckAlwaysAvailable()
    {
        CheckNoStoryProgress("fresh game");
        await CheckMenu("fresh");
        Check(_game.SaveToSlot(3), "fresh state can be saved in the isolated directory");
        _game.ResetPersistent();
        Check(_game.LoadFromSlot(3), "fresh state loads without clear or ending evidence");
        CheckNoStoryProgress("fresh save loaded");
        await CheckMenu("loaded");
    }

    private Hub OpenHome()
    {
        var hub = Mount<Hub>("res://Hub.tscn");
        var mode = typeof(Hub).GetField("_mode", Private)!;
        mode.SetValue(hub, Enum.Parse(mode.FieldType, "Home"));
        Check((bool)Call(hub, "HomeAppUnlocked", 5)!, "boss rush app is always available");
        Check((bool)Call(hub, "HomeAppUnlocked", 7)!, "single boss app is always available");
        return hub;
    }

    private async Task CheckMenu(string state)
    {
        var hub = OpenHome();
        await Shot($"{state}_home");
        Call(hub, "OpenHomeApp", 5);
        await Wait(() => GetTree().CurrentScene is DiffSelect && !LoadingScreen.IsActive, 600, "home opens difficulty selection");
        var select = (DiffSelect)GetTree().CurrentScene;
        Check(_game.PendingStageScene == BossRushRoot.ScenePath, "difficulty selection targets rush scene");
        await Shot($"{state}_rush_difficulty");
        Write(select, "_sel", (int)GameManager.Diff.Hard);
        ClickBossStart(select);
        await Wait(() => GetTree().CurrentScene is BossRushRoot && !LoadingScreen.IsActive, 600, "difficulty selection launches rush");
        var rush = (BossRushRoot)GetTree().CurrentScene;
        Check(_game.Difficulty == GameManager.Diff.Hard && rush.Round == 0 && !rush.SingleBoss && rush.BossCount == 4,
            $"{state}: chosen difficulty starts the four-boss rush");
        await Wait(() => rush.Boss != null, 180, $"{state}: fresh-state rush reaches the first boss");
        CheckEncounter(rush, 0, state);
        await Remove(rush);
        var solo = await OpenSingle(3, $"{state}_solo");
        await Wait(() => solo.Boss != null, 180, $"{state}: Mina can be entered before any story clear");
        CheckEncounter(solo, 3, state);
        await Remove(solo);
        CheckNoStoryProgress(state);
    }

    private async Task<BossRushRoot> OpenSingle(int bossIndex, string label)
    {
        var hub = OpenHome();
        Call(hub, "OpenHomeApp", 7);
        await Wait(() => GetTree().CurrentScene is BossSelectRoot && !LoadingScreen.IsActive, 600,
            $"{label}: home opens boss selection");
        var bosses = (BossSelectRoot)GetTree().CurrentScene;
        if (bossIndex == 3) await Shot($"{label}_selection");
        Call(bosses, "ChooseBoss", bossIndex);
        await Wait(() => GetTree().CurrentScene is DiffSelect && !LoadingScreen.IsActive, 600,
            $"{label}: chosen boss opens difficulty selection");
        Check(_game.PendingBossIndex == bossIndex && _game.PendingStageScene == BossRushRoot.SingleScenePath,
            $"{label}: selected boss and solo scene reach difficulty selection");
        var select = (DiffSelect)GetTree().CurrentScene;
        Write(select, "_sel", (int)GameManager.Diff.Hard);
        if (bossIndex == 3) await Shot($"{label}_difficulty");
        ClickBossStart(select);
        await Wait(() => GetTree().CurrentScene is BossRushRoot && !LoadingScreen.IsActive, 600,
            $"{label}: difficulty selection launches the single boss");
        var solo = (BossRushRoot)GetTree().CurrentScene;
        Check(solo.SingleBoss && solo.BossCount == 1 && solo.BossIndex == bossIndex && solo.Round == 0
            && _game.Difficulty == GameManager.Diff.Hard && solo.RunRecordId == SoloRecords[bossIndex],
            $"{label}: one selected boss with an independent record");
        return solo;
    }

    private static void ClickBossStart(DiffSelect select)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var rect = (Rect2)typeof(DiffSelect).GetProperty("BossStartRect", flags)!.GetValue(null)!;
        Write(select, "_t", 1d);
        foreach (var pair in new (string Field, object Value)[] { ("_mousePos", rect.GetCenter()), ("_usingMouse", true), ("_mL", true), ("_mLPrev", false) })
            typeof(Pad).GetField(pair.Field, flags)!.SetValue(null, pair.Value);
        select._Process(1d / 60);
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, false);
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private void CheckEncounter(BossRushRoot root, int bossIndex, string label)
    {
        Check(root.Boss?.GetType() == BossTypes[bossIndex], $"{label}: selected main boss {BossRushRoot.BossNames[bossIndex]}");
        Check(!Descendants(root).Any(n => n is Spawner or CameoBoss or StageAkari or StageKoharu or StageRei or StageMina),
            $"{label}: no road controller, road waves or midboss");
        Check(root.IsInGroup("boss_rush") && GameManager.BossRushActive && GameManager.SkipBossStory,
            $"{label}: boss-only story suppression is active");
        Check(root.Boss!.GetNode<BossPostSequence>("PostSequence").Floor == 0, $"{label}: no dialogue HP gate");
    }

    private void CheckNoStoryProgress(string label)
    {
        Check(GameManager.Stages.All(stage => !_game.IsStageCleared(stage.Id)) && !_game.IsFinalCleared && !_game.EndingRollSeen,
            $"{label}: story clears and credits remain untouched");
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
            Check(GameManager.Stages.All(stage => _game.GetBestTime(stage.Id, diff) == null
                && _game.GetBestScore(stage.Id, diff) == null)
                && _game.GetBestTime(GameManager.FinalStageId, diff) == null
                && _game.GetBestScore(GameManager.FinalStageId, diff) == null,
                $"{label}/{diff}: no story clear records");
    }

    private async Task CheckRun(GameManager.Diff diff)
    {
        _game.Difficulty = diff;
        _game.SelectedJob = (Job)((int)diff % 4);
        _game.TrainingSetUpgrade("n_life_1", true);
        _game.TrainingSetUpgrade("n_bomb_1", true);
        var rush = Mount<BossRushRoot>(BossRushRoot.ScenePath);
        Check(rush.Player.Lives == _game.StartLives && _game.Bombs == _game.StartBombs,
            $"{diff}: character and shop upgrades applied");
        Check(_game.SelectedJob == (Job)((int)diff % 4), $"{diff}: selected character retained");
        typeof(Player).GetMethod("ExtendInvincible", Private)!.Invoke(rush.Player, new object[] { 10000f });
        Property(rush.Player, "Lives", 2);
        rush.Hud.SetLives(2);
        Property(_game, "Bombs", 1);
        _game.AddScoreShard(1234);
        var player = rush.Player;
        for (int round = 0; round < 4; round++)
        {
            await Wait(() => rush.Boss != null, 180, $"{diff}: round {round + 1} starts");
            Check(rush.Round == round && rush.BossIndex == round, $"{diff}: boss order {BossRushRoot.BossNames[round]}");
            Check(rush.Player == player && player.Lives == 2 && _game.Bombs == 1 && _game.Score >= 1234,
                $"{diff}: life, bombs, score and player carry into round {round + 1}");
            await DefeatEncounter(rush, round, $"rush_{diff}_{round + 1}", diff == GameManager.Diff.Easy);
        }
        Check(rush.Completed && _game.GetBestTime(BossRushRoot.RecordId, diff).HasValue
            && _game.GetBestScore(BossRushRoot.RecordId, diff).HasValue,
            $"{diff}: all four bosses complete and record a rush result");
        Check(SoloRecords.All(id => _game.GetBestTime(id, diff) == null && _game.GetBestScore(id, diff) == null),
            $"{diff}: rush does not write single-boss records");
        CheckNoStoryProgress($"rush {diff}");
        if (diff == GameManager.Diff.Easy)
        {
            await Frames(75);
            await Shot("rush_clear");
            await Wait(() => GetTree().CurrentScene is Hub && !LoadingScreen.IsActive, 600, "rush clear returns home");
            await Remove(GetTree().CurrentScene);
        }
        else await Remove(rush);
    }

    private async Task DefeatEncounter(BossRushRoot root, int bossIndex, string label, bool screenshot)
    {
        CheckEncounter(root, bossIndex, label);
        Enemy boss = root.Boss!;
        int round = root.Round;
        if (boss is BossAkari) Write(boss, "_shieldBreaks", 3);
        if (screenshot)
        {
            await Frames(150);
            await Shot(label);
        }
        var forms = new HashSet<int>();
        for (int frame = 0; root.Round == round && frame < 2400; frame++)
        {
            string dialogue = Read<string>(root.Hud, "_dlgText");
            string bossLine = Read<string>(root.Hud, "_bossLine");
            if (dialogue.Length > 0 || bossLine.Length > 0)
                throw new Exception($"{label}: unexpected dialogue: {dialogue} {bossLine}");
            forms.Add(boss is BossMina ? Read<int>(boss, "_pattern")
                : (bool)typeof(Enemy).GetField("_form2", Private)!.GetValue(boss)! ? 1 : 0);
            if (frame >= 90 && frame % 24 == 0 && !Hud.BubblePaused)
            {
                if (boss is BossMina) Property(Read<MinaPhaseAttacks>(boss, "_caster"), "OpenerCompleted", true);
                boss.DealDirectDamage(Math.Max(1, boss.BarHp * boss.TotalBars / 35));
            }
            await Frames(1);
        }
        Check(forms.SetEquals(boss is BossMina ? new[] { 0, 1, 2, 3, 4 } : new[] { 0, 1 }),
            $"{label}: every form reached without story dialogue (forms {string.Join(",", forms)})");
        Check(root.Round == round + 1, $"{label}: defeat advances exactly once");
        await Frames(3);
        Check(root.Boss == null && !Descendants(root.World).Any(n => n is Enemy or AreaStrike or BossPost),
            $"{label}: defeated encounter cleaned up");
    }

    private async Task CheckSingleRun(int bossIndex)
    {
        var previousTimes = _game.ClearTimes.ToDictionary(p => p.Key, p => p.Value);
        var previousScores = _game.BestScores.ToDictionary(p => p.Key, p => p.Value);
        _game.SelectedJob = (Job)bossIndex;
        var solo = await OpenSingle(bossIndex, $"solo_{bossIndex + 1}");
        Check(_game.SelectedJob == (Job)bossIndex && solo.Player.Lives == _game.StartLives && _game.Bombs == _game.StartBombs,
            $"solo {bossIndex}: character and upgrades retained");
        typeof(Player).GetMethod("ExtendInvincible", Private)!.Invoke(solo.Player, new object[] { 10000f });
        await Wait(() => solo.Boss != null, 180, $"solo {bossIndex}: selected encounter starts");
        await DefeatEncounter(solo, bossIndex, $"solo_{bossIndex + 1}_battle", screenshot: true);
        Check(solo.Completed && solo.Round == 1 && solo.BossCount == 1,
            $"solo {bossIndex}: one defeat completes the entire run");
        Check(_game.GetBestTime(SoloRecords[bossIndex], GameManager.Diff.Hard).HasValue
            && _game.GetBestScore(SoloRecords[bossIndex], GameManager.Diff.Hard).HasValue,
            $"solo {bossIndex}: time and score recorded under the selected boss");
        Check(_game.ClearTimes.Count == previousTimes.Count + 1 && _game.BestScores.Count == previousScores.Count + 1
            && previousTimes.All(p => _game.ClearTimes.TryGetValue(p.Key, out var t) && t == p.Value)
            && previousScores.All(p => _game.BestScores.TryGetValue(p.Key, out var s) && s == p.Value),
            $"solo {bossIndex}: other bosses and rush records unchanged");
        await Frames(75);
        await Shot($"solo_{bossIndex + 1}_clear");
        await Wait(() => GetTree().CurrentScene is BossSelectRoot && !LoadingScreen.IsActive, 600,
            $"solo {bossIndex}: clear returns to selection without another boss");
        var select = (BossSelectRoot)GetTree().CurrentScene;
        Check(Read<int>(select, "_selected") == bossIndex && !GameManager.BossRushActive && !GameManager.SkipBossStory,
            $"solo {bossIndex}: selected boss retained and story suppression released");
        Call(select, "Back");
        await Wait(() => GetTree().CurrentScene is Hub && !LoadingScreen.IsActive, 600,
            $"solo {bossIndex}: selection can return home");
        await Remove(GetTree().CurrentScene);
        CheckNoStoryProgress($"solo {bossIndex}");
    }

    private async Task CheckRecordsPersist()
    {
        var previousTimes = _game.ClearTimes.ToDictionary(p => p.Key, p => p.Value);
        var previousScores = _game.BestScores.ToDictionary(p => p.Key, p => p.Value);
        Check(_game.SaveToSlot(3), "boss-only records saved in isolated slot");
        _game.ResetPersistent();
        Check(_game.LoadFromSlot(3), "boss-only records load");
        Check(_game.ClearTimes.Count == previousTimes.Count && _game.BestScores.Count == previousScores.Count
            && previousTimes.All(p => _game.ClearTimes.TryGetValue(p.Key, out var t) && t == p.Value)
            && previousScores.All(p => _game.BestScores.TryGetValue(p.Key, out var s) && s == p.Value),
            "rush and four single-boss results persist independently");
        CheckNoStoryProgress("boss-only records loaded");
        await Frames(1);
    }

    private async Task CheckRetry(bool single)
    {
        _game.Difficulty = GameManager.Diff.Easy;
        _game.PendingBossIndex = 2;
        string label = single ? "solo" : "rush";
        string scene = single ? BossRushRoot.SingleScenePath : BossRushRoot.ScenePath;
        var rush = Mount<BossRushRoot>(scene);
        await Wait(() => rush.Boss != null, 180, "retry trial begins");
        Property(rush.Player, "Lives", 0);
        _game.AddScoreShard(8000);
        await Frames(60);
        Check((int)typeof(GameManager).GetField("_gameOverFirst", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)! == 1,
            "game over offers restart and exit, not a single-boss checkpoint");
        await Shot($"{label}_game_over");
        Input.ActionPress("ui_accept");
        await Frames(2);
        Input.ActionRelease("ui_accept");
        await Wait(() => GetTree().CurrentScene is BossRushRoot next && next != rush && !LoadingScreen.IsActive,
            600, $"{label}: game-over retry reloads the same mode");
        rush = (BossRushRoot)GetTree().CurrentScene;
        Check(rush.Round == 0 && _game.Score == 0 && rush.Player.Lives == _game.StartLives
            && rush.SingleBoss == single && rush.BossIndex == (single ? 2 : 0) && rush.SceneFilePath == scene,
            $"{label}: retry resets the run and retains its selected boss");
        var menu = GetNode<PauseMenu>("/root/PauseMenu");
        menu.Open();
        Write(menu, "_confirmYes", true);
        var act = typeof(PauseMenu).GetField("_confirmAct", Private)!;
        act.SetValue(menu, Enum.Parse(act.FieldType, "Restart"));
        Call(menu, "RunConfirm");
        await Wait(() => GetTree().CurrentScene is BossRushRoot next && next != rush && !LoadingScreen.IsActive,
            600, $"{label}: pause restart reloads the same mode");
        rush = (BossRushRoot)GetTree().CurrentScene;
        Check(rush.Round == 0 && rush.SingleBoss == single && rush.BossIndex == (single ? 2 : 0),
            $"{label}: pause restart retains its starting boss");
        if (single)
        {
            Property(rush.Player, "Lives", 0);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = true });
            try { await Frames(2); }
            finally { Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = false }); }
            await Wait(() => GetTree().CurrentScene is BossRushRoot next && next != rush && !LoadingScreen.IsActive,
                600, "solo: R retry reloads single-boss mode");
            rush = (BossRushRoot)GetTree().CurrentScene;
            Check(rush.SingleBoss && rush.BossIndex == 2 && rush.Round == 0 && rush.SceneFilePath == scene,
                "solo: R retry cannot switch to the four-boss rush");
        }
        menu.Open();
        Write(menu, "_confirmYes", true);
        act.SetValue(menu, Enum.Parse(act.FieldType, "Leave"));
        Call(menu, "RunConfirm");
        await Wait(() => GetTree().CurrentScene is Hub && !LoadingScreen.IsActive, 600, $"{label}: pause exit returns home");
        await Remove(GetTree().CurrentScene);
    }

    private async Task Wait(Func<bool> condition, int limit, string message)
    {
        ulong loadingDeadline = Time.GetTicksMsec() + 30000;
        for (int frame = 0; !condition() && (frame < limit || LoadingScreen.IsActive && Time.GetTicksMsec() < loadingDeadline); frame++)
            await Frames(1);
        if (!condition())
        {
            var scene = GetTree().CurrentScene;
            GD.Print($"[BossRushQA] timeout scene={scene?.SceneFilePath} paused={GetTree().Paused} bubble={Hud.BubblePaused} loading={LoadingScreen.IsActive} scale={Engine.TimeScale}");
            if (scene is BossRushRoot root)
                GD.Print($"[BossRushQA] timeout round={root.Round} interval={Read<double>(root, "_interval")} leaving={Read<bool>(root, "_leaving")} blocked={Pad.UiBlocked(root)} lives={root.Player.Lives}");
        }
        Check(condition(), message);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        string? output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--output="));
        string dir = ProjectSettings.GlobalizePath(output == null ? "res://build/qa_story/release_2.086/modes/shots" : output[9..]);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{dir}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
