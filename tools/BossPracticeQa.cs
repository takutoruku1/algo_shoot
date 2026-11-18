using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossPracticeQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private BossPracticeRoot _practice = null!;
    private PauseMenu _pause = null!;
    private bool _fast;
    public override void _Process(double delta)
    {
        if (_fast && Engine.TimeScale >= 1) Engine.TimeScale = 4;
    }
    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Write(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[BossPracticeQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            DisplayServer.WindowSetPosition(new Vector2I(-16000, -16000));
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.TrainingSetImpression(3456);
            game.SelectedJob = Job.Melee;
            game.Difficulty = GameManager.Diff.Normal;
            game.SetContamination(.31f);
            Check(game.SaveToSlot(0), "initial save");
            var before = game.SnapshotMeta();
            _pause = GetNode<PauseMenu>("/root/PauseMenu");
            await Frames(3);

            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            Check((bool)Call(hub, "HomeAppUnlocked", 6)!, "practice icon unlocked before story clears");
            Check(((string[])typeof(Hub).GetField("HomeApps", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)[6] == "プラクティス",
                "phone entry uses the practice name");
            Write(hub, "_mode", Enum.Parse(typeof(Hub).GetNestedType("Mode", BindingFlags.NonPublic)!, "Home"));
            hub.SetProcess(false);
            await Shot("phone_icon");
            if (DisplayServer.GetName() == "headless") LoadingScreen.Open(hub, BossPracticeRoot.ScenePath);
            else Call(hub, "OpenHomeApp", 6);
            await Until(() => GetTree().CurrentScene is BossPracticeRoot && !LoadingScreen.IsActive, 900);
            _practice = (BossPracticeRoot)GetTree().CurrentScene;
            await Frames(6);
            Check(!game.AutoSaveEnabled && game.TutorialNoConsume && game.TrainingMode, "practice isolates saves and keeps unlimited bombs");
            Check(!game.SaveToSlot(1) && !game.SlotExists(1) && !_pause.SaveEnabled, "manual save also blocked");
            Check(_pause.Rows.All(row => row.act is not PauseMenu.Act.Save and not PauseMenu.Act.Load), "practice menu has no save/load routes");
            await Shot("selection");
            if (OS.GetCmdlineUserArgs().Contains("--ui-only"))
            {
                await CheckSelectionUsability();
                Check(game.Impression == before.Impression && game.SelectedJob == before.SelectedJob
                    && game.Difficulty == GameManager.Diff.Normal && Mathf.IsEqualApprox(game.Contamination, .31f), "UI round trip preserves player data");
                Check(game.AutoSaveEnabled && !game.TutorialNoConsume && !game.TrainingMode && !GameManager.BossPracticeActive
                    && !GetTree().Paused && Engine.TimeScale == 1, "UI exit restores mode and clock");
                GetTree().CurrentScene.QueueFree();
                foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
                { audio.Stop(); audio.Stream = null; }
                await Frames(5);
                GD.Print("[BossPracticeQA] UI ALL PASS (selection input, practice round trip, layout and data isolation)");
                GetTree().Quit();
                return;
            }
            await Press(Key.M);
            Check(_pause.IsOpen, "M opens selection menu");
            await Press(Key.Escape);
            await Frames(12);
            await Action("ui_right");
            Check(Read<int>(_practice, "_bossIndex") == 1, "keyboard changes boss");
            await Action("ui_down");
            await Action("ui_left");
            Check(game.Difficulty == GameManager.Diff.Easy, "keyboard changes difficulty");
            await Action("ui_down");
            await Action("ui_down");
            await Press(Key.Z);
            Check(_practice.Practicing, "keyboard starts the highlighted technique");
            await VerifyTools();
            _practice.ShowSelection();

            int entries = 0;
            for (int difficulty = 0; difficulty < 4; difficulty++)
            for (int boss = 0; boss < 4; boss++)
            for (int phase = 0; phase < (boss == 3 ? 5 : 2); phase++)
            {
                Configure(boss, phase, difficulty, 0);
                Check(game.Bombs == game.StartBombs && _practice.PracticePlayer.Lives == game.StartLives, "difficulty applies before resetting resources");
                var slots = Read<BossAttackCycle.Slot[]>(_practice, "_slots");
                var names = Read<(string Name, string Counterplay)[]>(_practice, "_descriptions");
                Check(slots.Length == names.Length && names.All(x => x.Name.Length > 0), $"catalog boss={boss} phase={phase} difficulty={difficulty}");
                var buttons = Enumerable.Range(0, slots.Length).Select(i => (Rect2)Call(_practice, "ButtonRect", i + 3)!).ToArray();
                Check(buttons.All(rect => rect.Position.X >= 404 && rect.End.X <= 1240 && rect.End.Y <= 496)
                    && !buttons.Where((rect, i) => buttons.Skip(i + 1).Any(other => rect.Intersects(other))).Any(),
                    "Every technique fits above its description without overlapping another button");
                foreach (var (slot, index) in slots.Select((slot, index) => (slot, index)))
                {
                    Call(_practice, "SelectTechnique", index);
                    var attack = await WaitForPreview();
                    Check(attack.TechniqueKind == slot.Kind && attack.TechniqueVariant == slot.Variant
                        && attack.IsEnhanced == (boss == 3 ? phase >= 2 : phase == 1), $"live identity {difficulty}/{boss}/{phase}/{names[index].Name}");
                    entries++;
                }
                _practice.PracticeBoss.Purify();
                _practice.PracticeBoss.DealDirectDamage(int.MaxValue);
                foreach (var panel in _practice.PracticeBoss.GetChildren().OfType<Panel>()) panel.Shatter();
                Check(!_practice.PracticeBoss.IsPurified && _practice.PracticeBoss.HpRatio == 1 && !_practice.PracticeBoss.GaugeVulnerable,
                    "boss cannot be defeated or change phase in practice");
                await Frames(4);
            }
            GD.Print($"[BossPracticeQA] catalog entries={entries}");

            foreach (var job in Jobs.All)
            {
                game.SelectedJob = job.Id;
                Configure((int)job.Id, 0, 1, 0);
                _practice.StartPractice();
                var player = _practice.PracticePlayer;
                int lives = player.Lives;
                int bombs = game.Bombs;
                Check(game.UseBomb() && game.Bombs == bombs, "practice bomb supply is unchanged");
                _practice.RemoveFromGroup("boss_practice");
                Write(player, "_invincible", false);
                player.TakeHit();
                Check(player.Lives == lives && game.RunHitCount == 0, "tutorial hit protection outside boss practice is unchanged");
                _practice.AddToGroup("boss_practice");
                Write(player, "_invincible", false);
                var bullet = GetNode<BulletPool>("/root/Pool").Spawn(player.GlobalPosition, Vector2.Zero, true);
                await Until(() => player.Lives < lives, 180);
                GetNode<BulletPool>("/root/Pool").Despawn(bullet);
                Check(player.Lives == lives - 1 && game.RunHitCount == 1
                    && Read<float>(player, "_hitReact") > 0, $"{job.CharacterId}: enemy bullet costs one life with feedback");
                player.TakeHit();
                Check(player.Lives == lives - 1, "hit invulnerability prevents repeated damage");
                Write(player, "_invincible", false);
                Write(player, "_dodgeInv", .45f);
                player.TakeHit();
                Check(player.Lives == lives - 1, "dodge invulnerability still prevents damage");
                Write(player, "_dodgeInv", 0f);
                while (player.Lives > 0)
                {
                    Write(player, "_invincible", false);
                    player.TakeHit();
                }
                await Until(() => _pause.IsOpen, 60);
                Check(GetTree().Paused && Read<bool>(player, "_gameOver")
                    && Read<Node2D>(_practice, "_battle").ProcessMode == ProcessModeEnum.Disabled,
                    "zero lives stops battle and opens the practice menu");
                Call(_pause, "Activate", 1);
                Write(_pause, "_confirmYes", true);
                Call(_pause, "RunConfirm");
                await Frames(12);
                Check(_practice.Practicing && !GetTree().Paused && !_pause.IsOpen
                    && _practice.PracticePlayer.Lives == game.StartLives && game.RunHitCount == 0
                    && !Read<bool>(_practice.PracticePlayer, "_gameOver"), "retry restores lives and active play after defeat");
                _practice.ShowSelection();
            }
            game.SelectedJob = Job.Melee;

            Engine.PhysicsTicksPerSecond = 240;
            Engine.MaxPhysicsStepsPerFrame = 16;
            Engine.TimeScale = 4;
            _fast = true;
            foreach (var (boss, phase) in OS.GetCmdlineUserArgs().Contains("--controls-only") ? Array.Empty<(int, int)>()
                : new[] { (0, 1), (1, 1), (2, 1), (3, 0), (3, 1), (3, 2), (3, 3), (3, 4) })
            {
                Configure(boss, phase, 1, 0);
                _practice.StartPractice();
                int lives = _practice.PracticePlayer.Lives;
                Write(_practice.PracticePlayer, "_invincible", false);
                _practice.PracticePlayer.TakeHit();
                Check(_practice.PracticePlayer.Lives == lives - 1 && Read<float>(_practice.PracticePlayer, "_hitReact") > 0, "hit feedback reduces lives");
                lives = _practice.PracticePlayer.Lives;
                await Frames(20);
                Write(_practice.PracticePlayer, "_invincible", true);
                Write(_practice.PracticePlayer, "_invincibleTimer", 99999f);
                Engine.TimeScale = 4;
                var selected = Read<BossAttackCycle.Slot[]>(_practice, "_slots")[0];
                int completed = 0;
                ulong seen = 0;
                await Until(() =>
                {
                    var active = GetTree().GetFirstNodeInGroup("boss_character_techniques") as BossCharacterTechnique;
                    if (active is { Finished: false } && active.GetInstanceId() != seen)
                    {
                        seen = active.GetInstanceId();
                        Check(active.TechniqueKind == selected.Kind && active.TechniqueVariant == selected.Variant, "only selected technique fires");
                        active.Ended += ended => { if (ended.Completed) completed++; };
                    }
                    return completed >= 2 && _practice.Repetitions >= 3;
                }, 12000);
                Check(_practice.PracticePlayer.Lives == lives, $"natural repeat boss={boss} phase={phase}; lives unchanged");
                await Shot($"battle_{boss}_{phase}");
            }
            _fast = false;
            Engine.TimeScale = 1;
            Engine.PhysicsTicksPerSecond = 60;
            Configure(2, 1, 1, 1);
            _practice.StartPractice();
            await Until(() => _practice.Repetitions > 0, 900);
            await Press(Key.Escape);
            Check(_pause.IsOpen && GetTree().Paused, "Escape pauses active technique");
            await Shot("battle_menu");
            var attackPaused = GetTree().GetFirstNodeInGroup("boss_character_techniques") as BossCharacterTechnique;
            double elapsed = attackPaused!.Elapsed;
            await Frames(25);
            Check(attackPaused.Elapsed == elapsed, "menu freezes attack time");
            Call(_pause, "Activate", 0);
            await Frames(12);
            Check(!_practice.Practicing && !_pause.IsOpen && !IsInstanceValid(attackPaused), "menu discards the old battle");
            await WaitForPreview();
            _practice.StartPractice();
            await Until(() => _practice.Repetitions > 0, 900);
            _pause.Open();
            Call(_pause, "Activate", 1);
            Write(_pause, "_confirmYes", true);
            Call(_pause, "RunConfirm");
            await Frames(12);
            Check(_practice.Practicing && !_pause.IsOpen, "menu restarts the same selected technique");
            game.TrainingSetImpression(999999);
            game.AddScoreShard(1);
            await Press(Key.F8);
            await Press(Key.F6);
            _pause.Open();
            Call(_pause, "Activate", 2);
            Write(_pause, "_confirmYes", true);
            Call(_pause, "RunConfirm");
            await Until(() => GetTree().CurrentScene is Hub && !LoadingScreen.IsActive, 900);
            Check(game.Impression == before.Impression && game.Followers == before.Followers && game.SelectedJob == before.SelectedJob
                && game.Difficulty == GameManager.Diff.Normal && Mathf.IsEqualApprox(game.Contamination, .31f), "exit restores currency, loadout, difficulty and contamination");
            Check(game.AutoSaveEnabled && !game.TutorialNoConsume && !game.TrainingMode && !GameManager.BossPracticeActive, "exit restores mode flags");
            Check(!GetTree().Paused && GetTree().GetNodesInGroup("boss_character_techniques").Count == 0
                && !GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Any(x => x.Active), "exit removes hazards and resumes phone");
            Check(Engine.TimeScale == 1 && BossPracticeRoot.Active == null, "exit restores preview clock");
            GetTree().CurrentScene.QueueFree();
            await Frames(3);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Frames(3);
            GD.Print("[BossPracticeQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[BossPracticeQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task CheckSelectionUsability()
    {
        Check(!_pause.EscOpensHere(), "selection uses Escape to return home");
        await Press(Key.M);
        Check(_pause.IsOpen, "M opens the menu");
        await Press(Key.Escape);
        await Frames(8);
        Check(!_pause.IsOpen && GetTree().CurrentScene == _practice, "closing the menu does not also leave practice");
        await Action("ui_right");
        Check(Read<int>(_practice, "_bossIndex") == 1, "keyboard switches the focused boss choice");
        await Action("ui_down");
        await Action("ui_left");
        Check(Read<int>(_practice, "_difficulty") == 0, "keyboard switches difficulty");
        await Press(Key.Z);
        Check(Read<int>(_practice, "_focus") == 2, "confirm advances to the next setting without changing it");

        for (int boss = 0; boss < 4; boss++)
        for (int phase = 0; phase < (boss == 3 ? 5 : 2); phase++)
        {
            Configure(boss, phase, 3, 0);
            var slots = Read<BossAttackCycle.Slot[]>(_practice, "_slots");
            var names = Read<(string Name, string Counterplay)[]>(_practice, "_descriptions");
            var rects = Enumerable.Range(0, slots.Length).Select(i => (Rect2)Call(_practice, "ButtonRect", i + 3)!).ToArray();
            Check(rects.All(rect => rect.Position.X >= 404 && rect.End.X <= 1240 && rect.End.Y <= 496)
                && !rects.Where((rect, i) => rects.Skip(i + 1).Any(other => rect.Intersects(other))).Any(), $"all techniques fit for boss {boss}, phase {phase}");
            foreach (var entry in names)
            {
                Check(UiKit.TextW(UiKit.ZenBold, entry.Name, 18) <= 516, $"selected title fits: {entry.Name}");
                Check(UiKit.WrapLines(UiKit.Zen, entry.Counterplay, 15, 516).Count <= 3, $"full counterplay fits: {entry.Name}");
            }
            var attack = await WaitForPreview();
            await Until(() => attack.Elapsed >= 2, 900);
            await Shot($"practice_{boss}_{phase}");
        }
        Configure(2, 1, 3, 0);
        await Frames(8);
        await Action("ui_right");
        Check(Read<int>(_practice, "_technique") == 1, "right moves to the adjacent technique");
        await Action("ui_down");
        Check(Read<int>(_practice, "_technique") == 3, "down keeps the same column");
        await Action("ui_left");
        await Action("ui_up");
        Check(Read<int>(_practice, "_technique") == 0, "left and up follow the visual grid");
        await Action("ui_left");
        Check(Read<int>(_practice, "_focus") == 2, "left edge returns to conditions");
        await Action("ui_down");
        Check(Read<int>(_practice, "_focus") == 3, "down returns to the selected technique");
        Input.ParseInputEvent(new InputEventAction { Action = "ui_down", Pressed = true });
        await Frames(110);
        Input.ParseInputEvent(new InputEventAction { Action = "ui_down", Pressed = false });
        await Frames(5);
        int count = Read<BossAttackCycle.Slot[]>(_practice, "_slots").Length;
        Check(Read<int>(_practice, "_focus") == count + 3 && Read<int>(_practice, "_technique") == count - 1,
            "holding down traverses the long list and stops at start");
        await Action("ui_up");
        await Action("ui_right");
        Check(Read<int>(_practice, "_focus") == count + 2, "ragged last row has no invisible right-hand selection");
        await Press(Key.Tab);
        Check(Read<int>(_practice, "_focus") == count + 3, "Tab jumps from the list to start");
        await Press(Key.Tab);
        Check(Read<int>(_practice, "_focus") == count + 4, "Tab reaches back without traversing every technique");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Shot("practice_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));

        ClickChoice(0, 3);
        ClickChoice(2, 4);
        Check(Read<int>(_practice, "_phase") == 4, "mouse selects Mina's fifth phase directly");
        ClickChoice(0, 0);
        Check(Read<int>(_practice, "_phase") == 0 && Read<int>(_practice, "_technique") == 0, "changing boss resets incompatible phase and technique");
        ClickChoice(1, 1);
        Check(Read<int>(_practice, "_difficulty") == 1, "mouse selects difficulty directly");
        ulong bossId = _practice.PracticeBoss.GetInstanceId();
        ClickChoice(1, 1);
        Check(_practice.PracticeBoss.GetInstanceId() == bossId, "clicking the current condition does not rebuild the battle");
        ClickAt(((Rect2)Call(_practice, "ButtonRect", 4)!).GetCenter());
        Check(!_practice.Practicing && Read<int>(_practice, "_technique") == 1, "a technique click selects it before starting");
        var previewBoss = _practice.PracticeBoss;
        var preview = await WaitForPreview();
        await Until(() => preview.ShotsCreated > 0 || preview.HazardFrames > 0, 900);
        var game = GetNode<GameManager>("/root/Game");
        Check(_practice.Repetitions == 0 && _practice.SimulationFrames == 0
            && game.RunHitCount == 0 && _practice.PracticePlayer.Lives == game.StartLives, "preview runs without spending lives or practice attempts");
        await Shot("practice_ready");
        var previousId = preview.GetInstanceId();
        string previewName = preview.SpellName;
        await Until(() => GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>()
            .Any(attack => !attack.Finished && attack.GetInstanceId() != previousId && attack.SpellName == previewName), 3600);
        ClickAt(((Rect2)Call(_practice, "ButtonRect", 5)!).GetCenter());
        await WaitForPreview();
        ClickAt(((Rect2)Call(_practice, "ButtonRect", 4)!).GetCenter());
        await WaitForPreview();
        ClickAt(((Rect2)Call(_practice, "ButtonRect", Read<BossAttackCycle.Slot[]>(_practice, "_slots").Length + 3)!).GetCenter());
        Check(_practice.Practicing && !GodotObject.IsInstanceValid(previewBoss) && _practice.Repetitions == 0
            && _practice.PracticeBoss.PracticeSlots![0] == Read<BossAttackCycle.Slot[]>(_practice, "_slots")[1]
            && !Read<ColorRect>(_practice, "_previewImage").Visible, "start launches the chosen technique from a fresh battle");
        await Frames(8);
        await Press(Key.F6);
        await Press(Key.F4);
        Check(!_practice.Practicing && !GetTree().Paused && Read<int>(_practice, "_focus") == 4,
            "F4 returns from paused practice to the same technique");
        await WaitForPreview();
        await Press(Key.Z);
        Check(_practice.Practicing, "confirm immediately restarts the retained selection");
        await Press(Key.Escape);
        Check(_pause.IsOpen && GetTree().Paused, "Escape still pauses an active practice battle");
        await Press(Key.Escape);
        await Frames(8);
        await Press(Key.F4);
        await Frames(8);
        await Press(Key.Escape);
        await Until(() => GetTree().CurrentScene is Hub && !LoadingScreen.IsActive, 900);
        Check(GetTree().CurrentScene is Hub, "Escape returns from selection to the phone");
    }

    private async Task<BossCharacterTechnique> WaitForPreview()
    {
        string name = Read<(string Name, string Counterplay)[]>(_practice, "_descriptions")[Read<int>(_practice, "_technique")].Name;
        BossCharacterTechnique? active = null;
        await Until(() => (active = GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>()
            .FirstOrDefault(attack => !attack.Finished && attack.SpellName == name && attack.Elapsed > .1)) != null, 900);
        Check(!_practice.Practicing && Read<ColorRect>(_practice, "_previewImage").IsVisibleInTree(), $"selected attack is previewed: {name}");
        return active!;
    }

    private void ClickChoice(int group, int option) => ClickAt(((Rect2)Call(_practice, "ChoiceRect", group, option)!).GetCenter());

    private void ClickAt(Vector2 at)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        foreach (var pair in new (string Field, object Value)[] { ("_mousePos", at), ("_usingMouse", true), ("_mL", true), ("_mLPrev", false) })
            typeof(Pad).GetField(pair.Field, flags)!.SetValue(null, pair.Value);
        _practice._Process(1d / 60);
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, false);
        _practice._Process(1d / 60);
    }

    private async Task VerifyTools()
    {
        await Until(() => _practice.Repetitions > 0, 900);
        await Press(Key.F6);
        Check(_practice.PreviewPaused && GetTree().Paused, "F6 pauses simulation");
        var attack = GetTree().GetFirstNodeInGroup("boss_character_techniques") as BossCharacterTechnique;
        double elapsed = attack!.Elapsed;
        Vector2 position = _practice.PracticePlayer.GlobalPosition;
        ulong frames = _practice.SimulationFrames;
        await Frames(12);
        Check(attack.Elapsed == elapsed && _practice.PracticePlayer.GlobalPosition == position, "pause freezes attack and player");
        await Press(Key.F7);
        Check(_practice.SimulationFrames == frames + 1 && GetTree().Paused, "F7 advances exactly one physics frame");
        Check(Math.Abs(attack.Elapsed - elapsed - 1.0 / Engine.PhysicsTicksPerSecond) < .0001, "single step advances technique by one fixed delta");
        await Press(Key.F9);
        Check(Read<bool>(_practice, "_showCollision"), "F9 enables collision outlines");
        await Shot("tools_paused");
        _pause.Open();
        Call(_pause, "Close");
        await Frames(8);
        Check(GetTree().Paused && _practice.SimulationFrames == frames + 1, "menu preserves preview pause");
        await Press(Key.F8);
        await Press(Key.F6);
        Check(Engine.TimeScale == .5 && !GetTree().Paused, "resume uses selected half speed");
        await Press(Key.F8);
        Check(Engine.TimeScale == .25, "quarter speed");
        elapsed = attack.Elapsed;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        double tickStart = attack.Elapsed;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Check(Math.Abs(attack.Elapsed - tickStart - .25 / Engine.PhysicsTicksPerSecond) < .0001, "slow playback scales simulation delta");
        await Press(Key.F8);
        _practice.ActivateTool(5);
        Check(_practice.AttackTempo == .75f && GameManager.EnemyTimeScale == .75f && _practice.Repetitions == 0, "attack tempo restarts with preview multiplier");
        _practice.ActivateTool(6);
        Check(_practice.RepeatDelay == 1, "repeat interval adjustment");
        for (int i = 0; i < 3; i++) { _practice.ActivateTool(5); _practice.ActivateTool(6); }
        Check(_practice.AttackTempo == 1 && _practice.RepeatDelay == .5, "preview adjustments return to defaults");
        await Press(Key.F6);
        await Press(Key.F5);
        Check(_practice.Practicing && !_practice.PreviewPaused && !GetTree().Paused && Engine.TimeScale == 1, "F5 restarts and resumes");
        await Press(Key.F9);
    }

    private void Configure(int boss, int phase, int difficulty, int technique)
    {
        Write(_practice, "_bossIndex", boss);
        Write(_practice, "_phase", phase);
        Write(_practice, "_difficulty", difficulty);
        Write(_practice, "_technique", technique);
        _practice.ShowSelection();
    }
    private async Task Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(4);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(4);
    }
    private async Task Action(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Frames(4);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        await Frames(4);
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Until(Func<bool> condition, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            if (condition()) return;
            await Frames(1);
        }
        throw new Exception("Timed out waiting for practice state");
    }
    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string? output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--output="));
        string directory = ProjectSettings.GlobalizePath(output == null ? "res://build/qa_story/boss_practice" : output[9..]);
        DirAccess.MakeDirRecursiveAbsolute(directory);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{directory}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
