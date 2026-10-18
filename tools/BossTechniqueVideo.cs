using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossTechniqueVideo : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static FieldInfo FieldOf(object obj, string name)
    {
        for (var type = obj.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Private | BindingFlags.DeclaredOnly) is { } field) return field;
        throw new MissingFieldException(name);
    }
    private static T Read<T>(object obj, string name) => (T)FieldOf(obj, name).GetValue(obj)!;
    private static void Write(object obj, string name, object? value) => FieldOf(obj, name).SetValue(obj, value);
    private static void Call(object obj, string name, params object[] args)
    {
        for (var type = obj.GetType(); type != null; type = type.BaseType)
            if (type.GetMethod(name, Private | BindingFlags.DeclaredOnly) is { } method)
            { method.Invoke(obj, args); return; }
        throw new MissingMethodException(name);
    }
    private GameManager _game = null!;
    private Enemy _boss = null!;
    private Player _player = null!;
    private Hud _hud = null!;
    private AutoplayInput _pilot = null!;
    private BulletPool _pool = null!;
    private bool _running;
    private string _output = "", _scene = "";
    private readonly List<object> _chapters = new();
    private readonly HashSet<string> _attacks = new();

    public override async void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--easy") || OS.GetCmdlineUserArgs().Any(arg => arg.StartsWith("--difficulty="))) { await RunEasy(); return; }
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"))
                throw new Exception("An isolated QA save directory is required.");
            var args = OS.GetCmdlineUserArgs();
            _scene = args.First(a => a.StartsWith("--boss="))[7..];
            if (!new[] { "Akari", "Koharu", "Rei", "MinaBattle" }.Contains(_scene))
                throw new Exception("Unknown boss.");
            _output = ProjectSettings.GlobalizePath(args.First(a => a.StartsWith("--output="))[9..]);
            DirAccess.MakeDirRecursiveAbsolute(_output);
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = args.Contains("--hard") ? GameManager.Diff.Hard : GameManager.Diff.Normal;
            _game.SelectedJob = _scene == "MinaBattle" ? Job.Magic : Job.Tank;
            _game.TrainingSetAllUpgrades(false);
            _game.MarkIdleDialogSeen("once_midboss_shield");
            Engine.TimeScale = 1;
            GD.Seed(2059);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            _pool = GetNode<BulletPool>("/root/Pool");
            var root = GD.Load<PackedScene>($"res://{_scene}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
            var world = root.GetNode<Node2D>("World");
            world.ProcessMode = ProcessModeEnum.Inherit;
            _player = world.GetNode<Player>("Player");
            _player.SetPhysicsProcess(false);
            _hud = root.GetNode<Hud>("Hud");
            _hud.HoldBubble = false;
            _hud.HideBubble();
            _boss = _scene switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(),
                "Rei" => new BossRei(), _ => new BossMina() };
            _boss.Position = new Vector2(Field.BossCenterX, Field.BossZoneCenterY);
            world.AddChild(_boss);
            _boss.SetProcess(false);
            _boss.SetPhysicsProcess(false);
            Read<BossPostSequence>(_boss, "_posts").ProcessMode = ProcessModeEnum.Disabled;
            Call(_boss, "TickEntrance", 0d);
            Call(_boss, "TickEntrance", 2d);
            Call(_boss, "TickSwapAnim", 1d);
            _boss.SetPanelsInvulnerable(true);
            _game.EndFury();
            Write(_boss, "_memoryPlayed", true);
            Write(_player, "_invincible", true);
            Write(_player, "_invincibleTimer", 9999f);
            Write(_player, "_fireCooldown", 9999f);
            foreach (string timer in new[] { "_bannerTimer", "_cutinTimer", "_bossLineTimer" }) Write(_hud, timer, 0d);
            _hud.HideSpellCard();
            var bg = root.GetNode<StageBackground>("StageBackground");
            bg.EnterBoss();
            bg.GetNode<BgLayers>("BgLayers")._Process(2);
            Read<BossPostSequence>(_boss, "_posts").ResumeMusic();
            _pilot = new AutoplayInput(GetTree(), _game);
            await Frames(3);
            _running = true;
            bool smoke = args.Contains("--smoke");
            for (int phase = 0; phase < 5; phase++)
            {
                if (phase == 2 || _boss is BossMina && phase > 0)
                {
                    await FinishAttack();
                    _running = false;
                    _pilot.Release();
                    _pool.DespawnAll();
                    if (_boss is BossMina)
                    {
                        // Skip story interludes only; restore difficulty before any combat frame.
                        var difficulty = _game.Difficulty;
                        _game.Difficulty = GameManager.Diff.Lunatic;
                        Call(_boss, "BeginPhaseTransition");
                        _game.Difficulty = difficulty;
                        Call(_boss, "CompletePhaseTransition");
                    }
                    else Call(_boss, "AdvanceForm2");
                    for (int i = 0; i < 360 && _boss.Transforming; i++) await Frames(1);
                    if (_boss.Transforming) throw new Exception("Transformation did not finish.");
                    _running = true;
                }
                Write(_boss, "_pattern", Math.Min(phase, _boss is BossMina ? 4 : 3));
                if (_boss is not BossMina) Write(_boss, "_beatsFired", Math.Min(phase, 3));
                if (phase == 4 && _boss is not BossMina) Write(_boss, "_finale", true);
                Call(_boss, "ApplySpell");
                int hp = Mathf.RoundToInt(_boss.TotalBars * _boss.BarHp * new[] { 1f, .76f, .54f, .32f, .14f }[phase]);
                Write(_boss, "_hp", hp);
                _hud.UpdateBossBar(_boss.CurrentBarIndex, _boss.TotalBars, _boss.CurrentBarFrac);
                string title = _boss is BossMina ? BossMina.PhaseName(phase)
                    : phase == 4 ? "終盤・複合攻撃" : (phase < 2 ? "通常形態" : "進化後") + $" / 第{phase + 1}スペル";
                long start = (long)Engine.GetProcessFrames();
                int peak = 0;
                GD.Print($"[TECHNIQUES] START {_scene}/{_game.Difficulty} phase={phase} frame={start} {title}");
                for (int frame = 0; frame < (smoke ? 120 : 1920); frame++)
                {
                    await Frames(1);
                    peak = Math.Max(peak, GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Count(b => b.Active));
                    TrackAttack();
                }
                long end = (long)Engine.GetProcessFrames();
                _chapters.Add(new { title, phase, start, end, peak, difficulty = _game.Difficulty.ToString() });
                GD.Print($"[TECHNIQUES] END phase={phase} frame={end} peak={peak}");
            }
            _running = false;
            _pilot.Release();
            System.IO.File.WriteAllText(_output + "/chapters.json", System.Text.Json.JsonSerializer.Serialize(_chapters));
            GD.Print($"[TECHNIQUES] COMPLETE {_scene}/{_game.Difficulty} time_scale={Engine.TimeScale} attacks={string.Join(",", _attacks)}");
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            root.QueueFree();
            _pool.DespawnAll();
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"[TECHNIQUES] FAIL {error}");
            GetTree().Quit(1);
        }
    }

    private void TrackAttack()
    {
        var pattern = Read<BossDanmaku>(_boss, "_danmaku");
        var cross = Read<BossCrossfire?>(pattern, "_crossfire");
        string attack = cross is { Finished: false } ? cross.Diagonal ? "diagonal" : cross.Pincer ? "pincer"
            : cross.Weave ? "weave" : cross.Sweep ? cross.Horizontal ? "horizontal" : "vertical" : "curtain"
            : _boss.AnimalTechniqueActive ? "evolved-signature"
            : GetTree().GetNodesInGroup("boss_edge_volleys").Count > 0 ? "edge-volley"
            : GetTree().GetNodesInGroup("aoe").Count > 0 ? "area" : "native";
        if (_attacks.Add(attack)) GD.Print($"[TECHNIQUES] ATTACK {attack} frame={Engine.GetProcessFrames()}");
    }

    private async Task FinishAttack()
    {
        for (int frame = 0; frame < 3600; frame++)
        {
            bool pending = _boss.CrossfireActive || _boss.AnimalTechniqueActive
                || GetTree().GetNodesInGroup("aoe").Count > 0 || GetTree().GetNodesInGroup("boss_edge_volleys").Count > 0
                || (_boss is BossMina ? Read<MinaPhaseAttacks>(_boss, "_caster").Active
                    : Read<AreaSpellCaster>(_boss, "_caster").AttackPending);
            if (!pending) return;
            await Frames(1);
            TrackAttack();
        }
        throw new Exception("No attack boundary within 60 seconds.");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_easyMode) { TickEasyPilot(delta); return; }
        if (!_running) return;
        _pilot.Update(delta, shoot: false, bombs: false, dodge: false);
        Input.FlushBufferedEvents();
        _player._PhysicsProcess(delta);
        Call(_player, "SetSpriteVisible", true);
        _boss._PhysicsProcess(delta);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
