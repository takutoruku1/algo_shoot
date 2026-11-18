using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class PlayerSpecialQa : Node
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameManager _game = null!;
    private BulletPool _pool = null!;
    private Node2D? _scene;
    private Node2D _world = null!;
    private Player _player = null!;
    private Hud _hud = null!;
    private string _output = "";
    private bool _showcase;
    private bool _battleCapture;
    private int _checks;
    private readonly List<string> _notes = new();

    private static FieldInfo FieldOf(object value, string name)
    {
        for (Type? type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Hidden) is { } field) return field;
        throw new MissingFieldException(value.GetType().Name, name);
    }
    private static void Set(object value, string name, object replacement) => FieldOf(value, name).SetValue(value, replacement);
    private static T Read<T>(object value, string name) => (T)FieldOf(value, name).GetValue(value)!;
    private static void Phase(Enemy enemy, string phase)
    {
        var field = FieldOf(enemy, "_phase");
        field.SetValue(enemy, Enum.Parse(field.FieldType, phase));
    }
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
        GD.Print($"[SpecialQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            _game = GetNode<GameManager>("/root/Game");
            _pool = GetNode<BulletPool>("/root/Pool");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = GameManager.Diff.Normal;
            foreach (var stage in GameManager.Stages) Read<HashSet<string>>(_game, "_cleared").Add(stage.Id);
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            var args = OS.GetCmdlineUserArgs();
            bool tutorials = args.Contains("--tutorial-only");
            _showcase = args.Contains("--showcase");
            _battleCapture = args.Any(a => a.StartsWith("--battle="));
            string? output = args.FirstOrDefault(a => a.StartsWith("--output="));
            _output = ProjectSettings.GlobalizePath(output == null ? "res://build/qa_story/player_special_20261031" : output[9..]);
            DirAccess.MakeDirRecursiveAbsolute(_output + "/shots");
            if (DisplayServer.GetName() != "headless")
            {
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                DisplayServer.WindowSetPosition(new Vector2I(-16000, -16000));
            }
            await Frames(2);
            if (_battleCapture) await CaptureBattles();
            else if (tutorials) await CheckTutorials();
            else if (_showcase) await Showcase();
            else
            {
                await CheckCutins();
                await CheckLifecycle();
                await CheckInputs();
                await CheckMina();
                await CheckAkari();
                await CheckKoharu();
                await CheckRei();
                await CheckDamageRules();
            }
            await DisposeScene();
            using var report = FileAccess.Open(_output + (_showcase || _battleCapture ? "/showcase.json" : "/checks.json"), FileAccess.ModeFlags.Write);
            report.StoreString(System.Text.Json.JsonSerializer.Serialize(new
            {
                checks = _checks,
                mode = _battleCapture ? "live_battle_capture" : tutorials ? "tutorial_flow" : _showcase ? "scripted_visual_fixture" : "headless_mechanics",
                automatic_input = true,
                showcase_invincible = false,
                player_damage_collision_disabled = _showcase,
                enemy_ai_disabled = !tutorials && !_battleCapture,
                ordinary_shots_disabled = !tutorials && !_battleCapture,
                human_difficulty_evaluated = false,
                notes = _notes,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Audio.Instance?.StopMusic(0);
            foreach (var player in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            {
                player.Stop();
                player.Stream = null;
            }
            await Frames(4);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(4);
            GD.Print($"[SpecialQA] ALL PASS {_checks}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GetTree().Paused = false;
            ReleaseInputs();
            GD.PushError($"[SpecialQA] FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task TutorialScene(string path, Job job, bool learned = true,
        GameManager.StageEntry entry = GameManager.StageEntry.Start, GameManager.Diff difficulty = GameManager.Diff.Normal)
    {
        await DisposeScene();
        _game.SelectedJob = job;
        _game.TrainingMode = false;
        _game.TrainingSetUpgrade("n_slow", learned);
        _game.SelectedEntry = entry;
        _game.Difficulty = difficulty;
        _game.AutoAdvanceDialog = false;
        _scene = GD.Load<PackedScene>(path).Instantiate<Node2D>();
        GetTree().Root.AddChild(_scene);
        GetTree().CurrentScene = _scene;
        _world = _scene.GetNode<Node2D>("World");
        _player = _world.GetNode<Player>("Player");
        _hud = _scene.GetNode<Hud>("Hud");
        await Frames(65);
        await TutorialFrames(3);
    }

    private Node TutorialStage => (Node)_scene!.GetType().GetProperty("Stage")!.GetValue(_scene)!;

    private async Task TutorialKey(Key key)
    {
        KeyEvent(key, true);
        await TutorialFrames(3);
        KeyEvent(key, false);
        await TutorialFrames(3);
    }

    private async Task TutorialFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task ChooseTutorial(bool yes)
    {
        if (!yes) await TutorialKey(Key.Down);
        await TutorialKey(Key.Z);
        for (int i = 0; i < 180 && _hud.GetNodeOrNull<ChoiceOverlay>("ChoiceOverlay") != null; i++)
            await TutorialFrames(1);
    }

    private async Task CheckTutorials()
    {
        _game.MarkIdleDialogSeen(StageTutorial.ChoiceSeenKey);
        _game.MarkIdleDialogSeen(StageTutorial.SkipKey);
        await TutorialScene("res://Akari.tscn", Job.Tank, learned: false);
        Check(_scene!.GetNodeOrNull<PlayerSpecialTutorial>("PlayerSpecialTutorial") == null
            && TutorialStage.IsInsideTree(), "unlearned character enters without a special prompt");
        Check(!_game.IsIdleDialogSeen(PlayerSpecialTutorial.SeenKey(Job.Tank)), "unlearned entry preserves the future prompt");

        foreach (var (job, path) in new[]
        {
            (Job.Tank, "res://Akari.tscn"), (Job.Melee, "res://Koharu.tscn"),
            (Job.Heal, "res://Rei.tscn"), (Job.Magic, "res://MinaBattle.tscn"),
        })
        {
            await TutorialScene(path, job);
            var tutorial = _scene!.GetNode<PlayerSpecialTutorial>("PlayerSpecialTutorial");
            Check(!TutorialStage.IsInsideTree() && Hud.BubblePaused && !_player.TrySpecial(),
                $"{job}: prompt pauses combat before the stage introduction");
            Check(Read<string>(_hud, "_dlgText").Contains(PlayerSpecial.NameFor(job)), $"{job}: prompt names the entering character's skill");
            var choice = _hud.GetNode<ChoiceOverlay>("ChoiceOverlay");
            Check(Read<string[]>(choice, "_choices").SequenceEqual(new[] { "はい", "いいえ" }), $"{job}: yes/no choices");
            Vector2 before = _player.Position;
            await TutorialKey(Key.Right);
            Check(_player.Position.IsEqualApprox(before), $"{job}: movement stays paused during the prompt");
            await Shot($"tutorial_{job}_prompt");
            await ChooseTutorial(true);
            Check(_hud.GetNodeOrNull<ControlCard>("ControlCard") != null
                && Read<string>(_hud, "_dlgText") == PlayerSpecialTutorial.LinesFor(job)[0], $"{job}: yes opens the matching explanation and controls");
            Check(!TutorialStage.IsInsideTree() && !_game.IsIdleDialogSeen(PlayerSpecialTutorial.SeenKey(job)),
                $"{job}: reading is not completed early");
            _hud.RevealDialogNow();
            await Frames(20);
            await Shot($"tutorial_{job}_explanation");
            GetTree().Paused = true;
            await Frames(4);
            Check(Read<int>(tutorial, "_line") == 0, $"{job}: pause does not advance the explanation");
            GetTree().Paused = false;
            for (int i = 0; i < 60 && IsInstanceValid(tutorial); i++) await TutorialKey(Key.Z);
            Check(!IsInstanceValid(tutorial) && TutorialStage.IsInsideTree(), $"{job}: reading completes and starts the stage");
            Check(_game.IsIdleDialogSeen(PlayerSpecialTutorial.SeenKey(job)), $"{job}: completion is tracked separately");
            Check(PlayerSpecialTutorial.LinesFor(job).All(text => Hud.Backlog.Any(entry => entry.Text == text)),
                $"{job}: every explanation line is available in the backlog");
        }
        await DisposeScene();
        Check(_game.SaveToSlot(0), "tutorial completion saves successfully");
        _game.ResetPersistent();
        Check(_game.LoadFromSlot(0), "tutorial completion reloads successfully");
        Check(Jobs.All.All(job => _game.IsIdleDialogSeen(PlayerSpecialTutorial.SeenKey(job.Id))), "all four completion keys survive a save reload");
        await TutorialScene("res://Akari.tscn", Job.Tank);
        Check(_scene!.GetNodeOrNull<PlayerSpecialTutorial>("PlayerSpecialTutorial") == null && TutorialStage.IsInsideTree(),
            "completed explanation is not offered again");

        await DisposeScene();
        Read<HashSet<string>>(_game, "_idleDialogSeen").Remove(PlayerSpecialTutorial.SeenKey(Job.Melee));
        await TutorialScene("res://Rei.tscn", Job.Melee, entry: GameManager.StageEntry.Boss, difficulty: GameManager.Diff.Lunatic);
        Check(!TutorialStage.IsInsideTree(), "boss checkpoint and Lunatic still offer the acquired skill");
        await ChooseTutorial(false);
        Check(_scene!.GetNodeOrNull<PlayerSpecialTutorial>("PlayerSpecialTutorial") == null && TutorialStage.IsInsideTree()
            && _hud.GetNodeOrNull<ControlCard>("ControlCard") == null, "no starts the checkpoint without an explanation");
        Check(_game.IsIdleDialogSeen(PlayerSpecialTutorial.SeenKey(Job.Melee)), "declining consumes only that character's prompt");

        await DisposeScene();
        Read<HashSet<string>>(_game, "_idleDialogSeen").Remove(PlayerSpecialTutorial.SeenKey(Job.Heal));
        await TutorialScene(BossRushRoot.ScenePath, Job.Heal);
        Check(_scene!.GetNodeOrNull<PlayerSpecialTutorial>("PlayerSpecialTutorial") != null
            && ((BossRushRoot)_scene).Boss == null, "boss rush also waits for the tutorial choice");
        await ChooseTutorial(false);
        Check(_game.IsIdleDialogSeen(PlayerSpecialTutorial.SeenKey(Job.Heal)), "boss rush can continue after declining");

        await DisposeScene();
        Read<HashSet<string>>(_game, "_idleDialogSeen").Remove(PlayerSpecialTutorial.SeenKey(Job.Magic));
        await TutorialScene("res://Koharu.tscn", Job.Magic);
        var pendingStage = TutorialStage;
        await DisposeScene();
        Check(!IsInstanceValid(pendingStage) && !_game.IsIdleDialogSeen(PlayerSpecialTutorial.SeenKey(Job.Magic)),
            "leaving before choosing releases the pending stage and keeps the prompt available");
    }

    private async Task Scene(Job job)
    {
        await DisposeScene();
        _game.SelectedJob = job;
        _game.MarkIdleDialogSeen(PlayerSpecialTutorial.SeenKey(job));
        _game.MarkIdleDialogSeen(StageTutorial.ChoiceSeenKey);
        _game.MarkIdleDialogSeen(StageTutorial.SkipKey);
        _game.TrainingMode = false;
        _game.SelectedEntry = GameManager.StageEntry.Start;
        string path = job switch { Job.Heal => "Koharu", Job.Magic => "Rei", _ => "Akari" };
        _scene = GD.Load<PackedScene>($"res://{path}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(_scene);
        GetTree().CurrentScene = _scene;
        _scene.SetProcess(false);
        ((Node)_scene.GetType().GetProperty("Stage")!.GetValue(_scene)!).SetProcess(false);
        _world = _scene.GetNode<Node2D>("World");
        _world.ProcessMode = ProcessModeEnum.Inherit;
        _player = _world.GetNode<Player>("Player");
        _hud = _scene.GetNode<Hud>("Hud");
        _hud.HoldBubble = false;
        _hud.HideBubble();
        _hud.SetCinematicMode(false);
        Hud.BubblePaused = false;
        _game.TrainingSetUpgrade("n_slow", true);
        _player.GlobalPosition = new Vector2(126, 118);
        Set(_player, "_fireCooldown", 10000f);
        Set(_player, "_invincible", false);
        Set(_player, "_invincibleTimer", 0f);
        Pad.ReleaseMouseFollow();
        await Frames(4);
        Check(_player.CharacterId == Jobs.Get(job).CharacterId && _game.SpecialReady,
            $"{job}: correct player and inherited n_slow unlock");
    }

    private async Task DisposeScene()
    {
        ReleaseInputs();
        GetTree().Paused = false;
        if (_game != null) _game.CancelSpecial();
        if (_pool != null) _pool.DespawnAll();
        if (IsInstanceValid(_scene))
        {
            GetTree().CurrentScene = this;
            _scene!.QueueFree();
            _scene = null;
            await Frames(3);
        }
        Hud.BubblePaused = false;
    }

    private Enemy Target(Vector2 position, bool shielded = false)
    {
        var enemy = new Enemy { Position = position };
        Set(enemy, "BarCount", 12);
        Set(enemy, "PanelCount", 0);
        _world.AddChild(enemy);
        enemy.SetPhysicsProcess(false);
        enemy.SetProcess(false);
        Phase(enemy, shielded ? "Shielded" : "Exposed");
        Set(enemy, "_entering", false);
        enemy.SetBodyContactEnabled(false);
        return enemy;
    }

    private Panel Shield(Enemy owner, Vector2 position, int ink = 20)
    {
        var panel = new Panel();
        panel.Setup(owner, 0, 0, 0, ink, UnfolderMotion.TexturePath(UnfolderKind.Rei));
        owner.AddChild(panel);
        Read<List<Panel>>(owner, "_panels").Add(panel);
        panel.SetPhysicsProcess(false);
        panel.GlobalPosition = position;
        return panel;
    }

    private Bullet EnemyBullet(Vector2 position, Vector2 velocity)
        => _pool.Spawn(position, velocity, true, 2, 1, BulletShape.Orb, new Color("f56a9c"));

    private async Task<PlayerSpecial> Start(Key? direction = null)
    {
        if (direction is { } key) KeyEvent(key, true);
        KeyEvent(Key.C, true);
        await Frames(2);
        KeyEvent(Key.C, false);
        if (direction is { } released) KeyEvent(released, false);
        Check(_game.SpecialActive && _game.CurrentSpecial is { }, $"{_game.SelectedJob}: C activates the special");
        var special = _game.CurrentSpecial!;
        Check(special.Character == _game.SelectedJob && special.Owner == _player, "special keeps its character and owner");
        Check(Mathf.IsEqualApprox(GameManager.EnemyTimeScale, 1f), "special does not slow enemies");
        if (!_showcase) await FinishCutin(special);
        return special;
    }

    private async Task FinishCutin(PlayerSpecial special)
    {
        for (int frame = 0; frame < 80 && special.CutinActive; frame++) await Frames(1);
        Check(!special.CutinActive && !GetTree().Paused, "cut-in releases the combat pause naturally");
    }

    private async Task CheckCutins()
    {
        var menu = GetNode<PauseMenu>("/root/PauseMenu");
        foreach (var job in Jobs.All)
        {
            await Scene(job.Id);
            Target(new Vector2(235, 118));
            var bullet = EnemyBullet(new Vector2(145, 118), new Vector2(-90, 0));
            Check(_player.TrySpecial(), $"{job.Id}: cut-in starts through the normal ability input");
            var special = _game.CurrentSpecial!;
            Vector2 bulletAt = bullet.Position, playerAt = _player.Position;
            int lives = _player.Lives;
            await Frames(12);
            Check(special.CutinActive && GetTree().Paused && special.Elapsed == 0 && special.AttackStages == 0,
                $"{job.Id}: the full cut-in precedes the attack");
            Check(bullet.Position == bulletAt && _player.Position == playerAt && _player.Lives == lives,
                $"{job.Id}: enemies and player remain frozen during the portrait");
            menu.Open();
            float before = special.CutinElapsed;
            await Frames(12);
            Check(Mathf.IsEqualApprox(before, special.CutinElapsed), $"{job.Id}: menu pauses the cut-in");
            typeof(PauseMenu).GetMethod("Close", Hidden)!.Invoke(menu, null);
            Check(GetTree().Paused && special.CutinActive, $"{job.Id}: closing the menu preserves the cut-in pause");
            await FinishCutin(special);
            await Frames(3);
            Check(special.Elapsed > 0 && bullet.Position != bulletAt, $"{job.Id}: combat resumes after the cut-in");
        }
        await Scene(Job.Tank);
        Check(_player.TrySpecial(), "cancellation fixture starts a cut-in");
        menu.Open();
        _game.CancelSpecial();
        Check(GetTree().Paused && menu.IsOpen, "cancelling a cut-in preserves the user's pause menu");
        typeof(PauseMenu).GetMethod("Close", Hidden)!.Invoke(menu, null);
        Check(!GetTree().Paused, "closing the menu after cancellation restores play");
        await Scene(Job.Tank);
        Check(_player.TrySpecial(), "scene-exit fixture starts a cut-in");
        _scene!.QueueFree();
        await Frames(3);
        Check(!GetTree().Paused && !_game.SpecialActive, "leaving during a cut-in releases its pause and overlay");
        _scene = null;
    }

    private async Task CheckLifecycle()
    {
        var names = Jobs.All.Select(j => PlayerSpecial.NameFor(j.Id)).ToArray();
        Check(names.Distinct().Count() == 4 && names.All(s => s.Length > 0), "four distinct special names");
        foreach (var job in Jobs.All)
        {
            await Scene(job.Id);
            _game.TrainingSetUpgrade("n_slow", false);
            Check(!_game.HasSpecial && !_player.TrySpecial() && !_game.SpecialActive, $"{job.Id}: locked skill cannot activate");
            _game.TrainingSetUpgrade("n_slow", true);
            _hud.SetCinematicMode(true, dialogueBubble: true);
            await Frames(2);
            Check(!_player.TrySpecial(), $"{job.Id}: dialogue prevents activation");
            _hud.SetCinematicMode(false);
            await Frames(2);
            if (job.Id == Job.Magic) Target(new Vector2(235, 118));
            var special = await Start();
            Check(!_player.TrySpecial(), $"{job.Id}: repeated activation is rejected");
            float elapsed = special.Elapsed;
            _hud.SetCinematicMode(true, dialogueBubble: true);
            await Frames(15);
            Check(Mathf.Abs(special.Elapsed - elapsed) < .001f, $"{job.Id}: dialogue freezes the active special");
            _hud.SetCinematicMode(false);
            await Frames(3);
            Check(special.Elapsed > elapsed, $"{job.Id}: special resumes after dialogue");
            elapsed = special.Elapsed;
            GetTree().Paused = true;
            await Frames(10);
            Check(Mathf.Abs(special.Elapsed - elapsed) < .001f && !_player.TrySpecial(), $"{job.Id}: tree pause freezes and blocks activation");
            GetTree().Paused = false;
            await Frames(3);
            Set(_player, "_gameOver", true);
            await Frames(3);
            Check(!_game.SpecialActive && !_player.TrySpecial(), $"{job.Id}: game over cancels the skill and prevents restart");
            Set(_player, "_gameOver", false);
        }
        await Scene(Job.Tank);
        KeyEvent(Key.C, true);
        await Frames(2);
        Check(_game.SpecialActive, "held C initially starts once");
        await FinishCutin(_game.CurrentSpecial!);
        await Frames(Mathf.CeilToInt(PlayerSpecial.DurationFor(Job.Tank) * 60) + 3);
        Check(!_game.SpecialActive && !_game.SpecialReady && _game.SpecialCooldownRatio > .9f, "cooldown begins after the effect ends");
        float cooldown = _game.SpecialCooldownRatio;
        _hud.SetCinematicMode(true, dialogueBubble: true);
        await Frames(30);
        Check(Mathf.Abs(_game.SpecialCooldownRatio - cooldown) < .001f, "dialogue freezes cooldown");
        _hud.SetCinematicMode(false);
        await Frames(Mathf.CeilToInt(PlayerSpecial.Cooldown * Engine.PhysicsTicksPerSecond) + 50);
        Check(_game.SpecialReady && !_game.SpecialActive, "holding C does not auto-repeat after cooldown");
        KeyEvent(Key.C, false);
        await Frames(2);
        await Start();
        await DisposeScene();
        Check(!_game.SpecialActive, "leaving the scene cancels the effect");
    }

    private async Task CheckInputs()
    {
        foreach (var button in new[] { MouseButton.WheelUp, MouseButton.WheelDown, MouseButton.Xbutton1, MouseButton.Xbutton2 })
        {
            await Scene(Job.Tank);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true,
                Position = new Vector2(126, 118), GlobalPosition = new Vector2(126, 118) });
            Input.FlushBufferedEvents();
            await Frames(3);
            Check(_game.SpecialActive, $"{button}: actual mouse input activates special");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false });
            Input.FlushBufferedEvents();
        }
        var pads = Input.GetConnectedJoypads();
        if (pads.Count > 0)
        {
            await Scene(Job.Tank);
            Input.ParseInputEvent(new InputEventJoypadButton { Device = pads[0], ButtonIndex = JoyButton.LeftShoulder, Pressed = true });
            await Frames(3);
            Check(_game.SpecialActive, "LB input activates special");
            Input.ParseInputEvent(new InputEventJoypadButton { Device = pads[0], ButtonIndex = JoyButton.LeftShoulder, Pressed = false });
        }
        else
        {
            _notes.Add("LB runtime input is unverified because no connected joypad is available; synthetic button events do not create a connected device.");
            GD.Print("[SpecialQA] UNVERIFIED LB: no connected joypad");
        }
    }

    private async Task CheckMina()
    {
        await Scene(Job.Tank);
        var target = Target(new Vector2(265, 118));
        var outside = Target(new Vector2(265, 185));
        var rear = EnemyBullet(new Vector2(92, 118), Vector2.Zero);
        var special = await Start();
        EnemyBullet(new Vector2(171, 118), Vector2.Left * 60);
        EnemyBullet(new Vector2(180, 125), Vector2.Left * 60);
        int lives = _player.Lives;
        await Frames(90);
        Check(special.AbsorbedCount >= 2 && rear.Active, "Mina absorbs front bullets and leaves rear bullets alone");
        Check(_player.Lives == lives, "Mina shield stops incoming physical bullets before the player is hit");
        await Frames(75);
        Check(target.HpRatio < 1 && Mathf.IsEqualApprox(outside.HpRatio, 1), "Mina beam hits its line and leaves off-line enemies untouched");
        Check(special.HitCount == 1 && special.AttackStages == 3, "Mina sends three salvos through the same target");
        float hp = target.HpRatio;
        int absorbedDamage = Read<int>(target, "_maxHp") - Read<int>(target, "_hp");
        await Frames(30);
        Check(Mathf.IsEqualApprox(target.HpRatio, hp) && !_game.SpecialActive, "Mina beam does not repeat damage during its fade");
        await Scene(Job.Tank);
        target = Target(new Vector2(265, 118));
        await Start();
        await Frames(170);
        int emptyDamage = Read<int>(target, "_maxHp") - Read<int>(target, "_hp");
        Check(emptyDamage >= 200 && absorbedDamage > emptyDamage, "Mina deals at least 200 body damage and gains more from absorbed bullets");

        await Scene(Job.Tank);
        await Start();
        var fast = EnemyBullet(_player.Position + new Vector2(70, 0), Vector2.Zero);
        await Frames(2);
        lives = _player.Lives;
        fast.Velocity = Vector2.Left * 4200;
        await Frames(3);
        Check(!fast.Active && _player.Lives == lives,
            "Mina catches a physical bullet crossing the entire shield-to-player gap in one 60 Hz tick");
    }

    private async Task CheckAkari()
    {
        await Scene(Job.Melee);
        var along = Target(new Vector2(185, 118));
        var outside = Target(new Vector2(185, 183));
        var pathBullet = EnemyBullet(new Vector2(183, 118), Vector2.Zero);
        var offBullet = EnemyBullet(new Vector2(183, 173), Vector2.Zero);
        Vector2 origin = _player.GlobalPosition;
        var special = await Start(Key.Right);
        await Frames(10);
        int lives = _player.Lives;
        _player.TakeHit();
        Check(special.ProtectsPlayer && _player.Lives == lives, "Akari is protected during the dash");
        await Frames(30);
        Check(_player.GlobalPosition.X > origin.X + 90 && Mathf.Abs(_player.GlobalPosition.Y - origin.Y) < 1,
            "Akari moves along the selected direction");
        Check(along.HpRatio < 1 && Mathf.IsEqualApprox(outside.HpRatio, 1), "Akari slash damages only the swept band");
        Check(!pathBullet.Active && offBullet.Active, "Akari clears only bullets in the swept band");
        float afterDash = along.HpRatio;
        var crossTarget = Target(_player.GlobalPosition + new Vector2(24, 19));
        var crossBullet = EnemyBullet(crossTarget.GlobalPosition, Vector2.Zero);
        await Frames(20);
        Check(crossTarget.HpRatio < 1 && !crossBullet.Active && special.AttackStages == 3,
            "Akari follows her dash with two damaging crossing cuts that clear their own band");
        Check(along.HpRatio == afterDash, "Akari finishing cuts do not damage targets behind their actual reach");
        var crescentTarget = Target(_player.GlobalPosition + new Vector2(52, 0));
        var beyondCrescent = Target(_player.GlobalPosition + new Vector2(90, 0));
        var crescentBullet = EnemyBullet(crescentTarget.Position, Vector2.Zero);
        var armored = Target(_player.GlobalPosition + new Vector2(72, -42), true);
        var crescentShield = Shield(armored, crescentTarget.Position + Vector2.Down * 10, 54);
        await Frames(15);
        Check(Read<int>(crescentTarget, "_maxHp") - Read<int>(crescentTarget, "_hp") == 150
            && beyondCrescent.HpRatio == 1 && !crescentBullet.Active && special.AttackStages == 4,
            "Akari finishes with a 150-damage crescent at its actual visible arc, leaving beyond-arc enemies intact");
        Check(!IsInstanceValid(crescentShield) || crescentShield.Ink == 0, "Akari's final crescent breaks a full 54-ink shield");
        float crescentHp = crescentTarget.HpRatio;
        await Frames(30);
        Check(crescentTarget.HpRatio == crescentHp, "Akari's lingering crescent never repeats its damage");
        Check(!_game.SpecialActive, "Akari ends after the dash and recovery");
        await Scene(Job.Melee);
        _player.GlobalPosition = new Vector2(Field.Right - 10, 118);
        await Start(Key.Right);
        await Frames(45);
        Check(_player.GlobalPosition.X <= Field.Right, "Akari dash respects the playfield edge");

        await Scene(Job.Melee);
        _player.GlobalPosition = new Vector2(Field.Right - 18, Field.Bottom - 55);
        KeyEvent(Key.Down, true);
        var cornerDash = await Start(Key.Right);
        KeyEvent(Key.Down, false);
        await Frames(15);
        Check(cornerDash.Direction.X > .5f && cornerDash.Direction.Y > .5f,
            "Akari corner fixture uses diagonal movement");
        if (DisplayServer.GetName() != "headless")
        {
            var models = Read<System.Collections.IDictionary>(cornerDash, "_models");
            var wake = Read<Node3D>(models["AkariWake"]!, "Root");
            Vector3 end = wake.Transform * Vector3.Right;
            Check(new Vector2(end.X, -end.Y).DistanceTo(_player.GlobalPosition) < .1f,
                "Akari 3D wake ends at the actual clipped dash position");
            await Shot("akari_diagonal_edge");
        }

        await Scene(Job.Melee);
        Vector2 cursor = _player.Position + new Vector2(24, 0);
        GetViewport().WarpMouse(cursor);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = cursor, GlobalPosition = cursor, Relative = new Vector2(24, 0) });
        Input.FlushBufferedEvents();
        await Frames(3);
        origin = _player.Position;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1, Pressed = true,
            Position = cursor, GlobalPosition = cursor });
        Input.FlushBufferedEvents();
        await Frames(3);
        Check(_game.SpecialActive, "Akari side button starts a mouse-directed dash");
        Vector2 direction = _game.CurrentSpecial!.Direction;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1, Pressed = false,
            Position = cursor, GlobalPosition = cursor });
        Input.FlushBufferedEvents();
        await FinishCutin(_game.CurrentSpecial!);
        await Frames(110);
        Check((_player.Position - origin).Dot(direction) > 90 && !Pad.UsingMouse,
            "Akari stays at the landing point instead of returning to an unmoved cursor");
    }

    private async Task CheckKoharu()
    {
        foreach (var mode in new[] { "inside", "outside", "full" })
        {
            await Scene(Job.Heal);
            if (mode != "full") typeof(Player).GetProperty(nameof(Player.Lives))!.GetSetMethod(true)!.Invoke(_player, new object[] { _player.MaxLives - 2 });
            _hud.SetLives(_player.Lives);
            int lives = _player.Lives;
            var special = await Start();
            Vector2 origin = special.Origin;
            var incoming = EnemyBullet(origin + new Vector2(65, 0), Vector2.Left * 55);
            var outside = EnemyBullet(origin + new Vector2(0, 70), Vector2.Zero);
            var bloomTarget = Target(origin + new Vector2(85, 0));
            var farTarget = Target(origin + new Vector2(245, 0));
            var finaleTarget = Target(origin + new Vector2(185, 0));
            await Frames(55);
            Check(!incoming.Active && outside.Active, $"Koharu {mode}: ring clears incoming bullets only inside its boundary");
            Check(special.Origin.IsEqualApprox(origin), "Koharu ring remains at its placement point");
            if (mode == "outside")
            {
                KeyEvent(Key.Right, true);
                await Frames(65);
                KeyEvent(Key.Right, false);
                Check(_player.GlobalPosition.DistanceTo(origin) > PlayerSpecial.KoharuRadius + 10, "Koharu can leave her ring using normal movement");
            }
            await Frames(mode == "outside" ? 205 : 270);
            int expected = mode == "inside" ? _player.MaxLives : lives;
            Check(_player.Lives == expected && !_game.SpecialActive, $"Koharu {mode}: completion fully restores life inside the ring and respects its boundary and cap");
            Check(bloomTarget.HpRatio < 1 && farTarget.HpRatio == 1 && !outside.Active && special.AttackStages == 4,
                $"Koharu {mode}: four expanding waves reach nearby enemies and bullets within their visible radius");
            Check(Read<int>(finaleTarget, "_maxHp") - Read<int>(finaleTarget, "_hp") == 100,
                $"Koharu {mode}: the final blossom reaches 185 units away for one 100-damage hit");
        }
        await Scene(Job.Heal);
        await Start();
        var fast = EnemyBullet(_player.Position + new Vector2(80, 0), Vector2.Zero);
        await Frames(2);
        int previousLives = _player.Lives;
        fast.Velocity = Vector2.Left * 4800;
        await Frames(3);
        Check(!fast.Active && _player.Lives == previousLives,
            "Koharu catches a physical bullet crossing from outside the ring to the player within one 60 Hz tick");
    }

    private async Task CheckRei()
    {
        await Scene(Job.Magic);
        var one = Target(new Vector2(171, 92));
        var two = Target(new Vector2(235, 110));
        var three = Target(new Vector2(291, 151));
        var disconnected = Target(new Vector2(78, 203));
        var special = await Start();
        await Frames(10);
        Check(one.HpRatio < 1 && Mathf.IsEqualApprox(three.HpRatio, 1), "Rei chain travels in order instead of hitting the whole screen at once");
        await Frames(35);
        Check(new[] { one, two, three }.All(e => e.HpRatio < 1) && Mathf.IsEqualApprox(disconnected.HpRatio, 1),
            "Rei reaches connected nearby targets and leaves a disconnected target untouched");
        Check(new[] { one, two, three }.Select(e => Read<int>(e, "_hp")).Distinct().Count() == 1,
            "Rei hits each connected enemy once without revisiting");
        await Frames(75);
        Check(!_game.SpecialActive, "Rei chain finishes when no connected unhit target remains");

        await Scene(Job.Magic);
        var owner = Target(new Vector2(275, 118), true);
        var shields = new[] { Shield(owner, new Vector2(175, 85), 80), Shield(owner, new Vector2(230, 100), 80), Shield(owner, new Vector2(280, 155), 80) };
        Set(_player, "_lockArmed", true);
        Set(_player, "_locked", true);
        Set(_player, "_lockTarget", owner);
        await Start();
        await Frames(50);
        Check(shields.All(p => p.Ink == 26) && Mathf.IsEqualApprox(owner.HpRatio, 1),
            "Rei chains across unfolders while respecting the guarded boss body");

        await Scene(Job.Magic);
        one = Target(new Vector2(171, 118));
        two = Target(new Vector2(240, 118));
        three = Target(new Vector2(306, 118));
        await Start();
        await Frames(11);
        two.Position = new Vector2(381, 205);
        await Frames(80);
        Check(one.HpRatio < 1 && Mathf.IsEqualApprox(two.HpRatio, 1) && Mathf.IsEqualApprox(three.HpRatio, 1),
            "Rei breaks the chain when the next target moves beyond its link range");

        await Scene(Job.Magic);
        owner = Target(new Vector2(300, 118), true);
        var firstShield = Shield(owner, new Vector2(171, 118), 80);
        var doomedShield = Shield(owner, new Vector2(240, 118), 1);
        var finalShield = Shield(owner, new Vector2(306, 118), 80);
        await Start();
        await Frames(11);
        var ordinaryShot = _pool.Spawn(new Vector2(232, 118), Vector2.Right * 360, false, 3, 1);
        await Frames(4);
        Check(!ordinaryShot.Active && (!IsInstanceValid(doomedShield) || doomedShield.Ink <= 0),
            "ordinary player shot destroys the next shield while Rei's light is in flight");
        await Frames(55);
        Check(firstShield.Ink == 26 && finalShield.Ink == 26,
            "Rei continues through a removed target and resolves one constellation follow-up at each remaining shield");

        await Scene(Job.Magic);
        Target(new Vector2(185, 118));
        var inThread = EnemyBullet(new Vector2(157, 118), Vector2.Zero);
        var away = EnemyBullet(new Vector2(157, 175), Vector2.Zero);
        special = await Start();
        await Frames(14);
        Check(inThread.Active && away.Active, "Rei's traveling chain does not erase unrelated bullets");
        await Frames(20);
        Check(!inThread.Active && away.Active && special.AttackStages == 2,
            "Rei's constellation release clears the actual thread once and preserves off-thread bullets");

        await Scene(Job.Magic);
        var chain = new List<Enemy>();
        for (int i = 0; i < 12; i++) chain.Add(Target(new Vector2(170 + i % 4 * 44, 55 + i / 4 * 50)));
        special = await Start();
        await Frames(180);
        Check(special.AttackStages == 13 && chain.All(e => Read<int>(e, "_maxHp") - Read<int>(e, "_hp") == 150),
            "Rei connects twelve distinct targets and releases one 110-damage constellation after each 40-damage chain hit");
    }

    private async Task CheckDamageRules()
    {
        await Scene(Job.Tank);
        var enemy = Target(new Vector2(260, 118), true);
        var shield = Shield(enemy, new Vector2(225, 118));
        Check(!enemy.TakeSpecialHit(30, _player.Position) && Mathf.IsEqualApprox(enemy.HpRatio, 1), "shielded body rejects special damage");
        shield.Invulnerable = true;
        Check(!shield.TakeSpecialHit(6) && shield.Ink == 20, "invulnerable unfolder rejects special damage");
        shield.Invulnerable = false;
        Check(shield.TakeSpecialHit(6) && shield.Ink == 14, "active unfolder receives special damage");
        enemy.RallyGuarded = true;
        Check(!shield.TakeSpecialHit(6) && shield.Ink == 14, "rally guard protects the unfolder");
        enemy.RallyGuarded = false;
        Phase(enemy, "Exposed");
        Check(enemy.TakeSpecialHit(30, _player.Position) && enemy.HpRatio < 1, "exposed body accepts special damage");
        enemy.TechniquePractice = true;
        float hp = enemy.HpRatio;
        Check(!enemy.TakeSpecialHit(30, _player.Position) && Mathf.IsEqualApprox(enemy.HpRatio, hp), "practice targets retain their no-damage rule");
    }

    private async Task Showcase()
    {
        var chapters = new List<object>();
        foreach (var job in Jobs.All)
        {
            if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--job=")) is { } selected
                && selected[6..] != job.Id.ToString()) continue;
            await Scene(job.Id);
            _player.CollisionLayer = 0;
            _player.CollisionMask = 0;
            _player.Monitoring = false;
            _player.Monitorable = false;
            AddCaption(job.Id);
            _player.Position = job.Id == Job.Heal ? new Vector2(182, 118) : new Vector2(128, 123);
            if (job.Id == Job.Heal)
                typeof(Player).GetProperty(nameof(Player.Lives))!.GetSetMethod(true)!.Invoke(_player, new object[] { _player.MaxLives - 2 });
            _hud.SetLives(_player.Lives);
            var theme = job.Id switch { Job.Melee => StageTheme.Akari, Job.Heal => StageTheme.Koharu, Job.Magic => StageTheme.Rei, _ => StageTheme.Mina };
            Vector2[] positions = job.Id switch
            {
                Job.Magic => new[] { new Vector2(187, 78), new Vector2(246, 90), new Vector2(292, 140), new Vector2(237, 172) },
                Job.Heal => new[] { new Vector2(252, 80), new Vector2(272, 155), new Vector2(114, 165) },
                Job.Melee => new[] { new Vector2(185, 123), new Vector2(250, 105), new Vector2(257, 143), new Vector2(316, 123) },
                _ => new[] { new Vector2(237, 123), new Vector2(302, 120) },
            };
            foreach (var position in positions)
            {
                var enemy = new MidEnemy { Position = position };
                enemy.Configure(EnemyTable.For(theme).Item1);
                enemy.SetEntry(position);
                _world.AddChild(enemy);
                enemy.SetPhysicsProcess(false);
                enemy.SetBodyContactEnabled(false);
                foreach (var panel in enemy.GetChildren().OfType<Panel>()) panel.Ink = 54;
            }
            await Frames(40);
            long startFrame = (long)Engine.GetProcessFrames();
            var special = await Start(job.Id == Job.Melee ? Key.Right : null);
            for (int frame = 0; frame < 380; frame++)
            {
                int activeFrame = frame - Mathf.CeilToInt(PlayerSpecial.CutinDuration * 60);
                if (job.Id == Job.Tank && activeFrame >= 0 && activeFrame < 88 && activeFrame % 5 == 0)
                    EnemyBullet(new Vector2(201, 103 + (frame / 5 % 5) * 9), new Vector2(-100, 0));
                if (job.Id == Job.Heal && activeFrame >= 0 && activeFrame < 170 && activeFrame % 18 == 0)
                {
                    float angle = frame * .18f;
                    Vector2 from = special.Origin + Vector2.FromAngle(angle) * 77;
                    EnemyBullet(from, (special.Origin - from).Normalized() * 64);
                }
                if (job.Id == Job.Melee && frame == 0)
                    foreach (var point in new[] { new Vector2(180, 123), new Vector2(202, 123), new Vector2(217, 126) }) EnemyBullet(point, Vector2.Zero);
                if (frame is 5 or 15 or 32 or 47 or 60 or 85 or 104 or 124 or 134 or 164 or 184 or 207 or 260 or 299 or 320 or 359)
                    await Shot($"{Jobs.Get(job.Id).CharacterId}_{frame:000}");
                await Frames(1);
            }
            Check(!_game.SpecialActive, $"{job.Id}: showcase completes naturally");
            chapters.Add(new { character = job.Id.ToString(), start = startFrame, end = (long)Engine.GetProcessFrames(),
                attack_stages = special.AttackStages, hit_targets = special.HitCount, absorbed = special.AbsorbedCount });
        }
        System.IO.File.WriteAllText(_output + "/chapters.json", System.Text.Json.JsonSerializer.Serialize(chapters));
    }

    private void AddCaption(Job job)
    {
        var layer = new CanvasLayer { Layer = 30 };
        _scene!.AddChild(layer);
        void LabelAt(string text, Vector2 at, int size, Color color)
        {
            var label = new Label { Text = text, Position = at, MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontOverride("font", UiKit.Zen);
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeColorOverride("font_color", color);
            layer.AddChild(label);
        }
        LabelAt("自動入力・被弾判定なし / 敵AI・通常射撃は停止", new Vector2(Field.Left + 8, 204), 7, Colors.White);
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(_output + "/shots/" + name + ".png") == Error.Ok, $"saved {name}");
    }
    private static void KeyEvent(Key key, bool pressed)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
        Input.FlushBufferedEvents();
    }
    private static void ReleaseInputs()
    {
        foreach (var key in new[] { Key.C, Key.Z, Key.Up, Key.Down, Key.Left, Key.Right }) KeyEvent(key, false);
        foreach (var button in new[] { MouseButton.Xbutton1, MouseButton.Xbutton2 })
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false });
        Input.FlushBufferedEvents();
        Pad.ConsumeWheelTurn();
        Pad.ReleaseMouseFollow();
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
