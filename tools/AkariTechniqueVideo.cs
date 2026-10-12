using Godot;
using System;
using System.Collections.Generic;
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
    private AkariRoot _root = null!;
    private BossAkari _boss = null!;
    private AreaSpellCaster _caster = null!;
    private AutoplayInput _pilot = null!;
    private BulletPool _pool = null!;
    private bool _running, _allowBody, _allowBarrage, _allowTechnique;
    private string _output = "";
    private readonly List<object> _chapters = new();
    private int _clipIndex;

    public override async void _Ready()
    {
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"))
                throw new Exception("An isolated QA save directory is required.");
            _output = ProjectSettings.GlobalizePath("res://build/akari_techniques_2_052");
            DirAccess.MakeDirRecursiveAbsolute(_output);
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Hard;
            game.SelectedJob = Job.Tank;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            _pool = GetNode<BulletPool>("/root/Pool");
            _root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
            GetTree().Root.AddChild(_root);
            GetTree().CurrentScene = _root;
            _root.SetProcess(false);
            _root.Stage.SetProcess(false);
            _root.World.ProcessMode = ProcessModeEnum.Disabled;
            _root.Hud.HoldBubble = false;
            _root.Hud.HideBubble();
            Write(_root.Hud, "_bannerTimer", 0d);
            _root.GetNode<StageBackground>("StageBackground").EnterBoss();
            _root.GetNode<StageBackground>("StageBackground").GetNode<BgLayers>("BgLayers")._Process(2);
            _boss = new BossAkari { Position = new Vector2(Field.BossCenterX, Field.BossZoneCenterY) };
            _root.World.AddChild(_boss);
            _boss.SetProcess(false);
            Read<BossPostSequence>(_boss, "_posts").ProcessMode = ProcessModeEnum.Disabled;
            Call(_boss, "TickEntrance", typeof(Enemy), 0d);
            Call(_boss, "TickEntrance", typeof(Enemy), 2d);
            _caster = Read<AreaSpellCaster>(_boss, "_caster");
            Write(_caster, "_interval", 999d);
            Write(_root.Player, "_invincible", true);
            Write(_root.Player, "_invincibleTimer", 999f);
            Write(_root.Player, "_fireCooldown", 999f);
            Write(_root.Hud, "_cutinTimer", 0d);
            Write(_root.Hud, "_bossLineTimer", 0d);
            _pilot = new AutoplayInput(GetTree(), game);
            ProcessPhysicsPriority = -100;
            await Frames(3);
            _root.World.ProcessMode = ProcessModeEnum.Inherit;
            _running = true;

            await Clip("ねえ、こっち見て / 狙い扇・加速弾", 12, () => Pattern(0));
            await Clip("すきって言って / 放射リング", 12, () => Pattern(1));
            await Clip("多重リング → 自機狙いの帯状弾幕", 22, () =>
            {
                _allowBarrage = true;
                Write(_boss, "_danmaku", new BossDanmaku(_boss), typeof(Enemy));
                _root.Hud.AnnounceSpell("あかり", BossHandles.AkariSpell, "ねえ、こっち見て", LifeEmblem.GemColor("akari"));
            });
            foreach (var spell in new[] { ("豪雨予報", AreaStrike.Shape.BeamV), ("沈黙の波紋", AreaStrike.Shape.Circle) })
                await Clip(spell.Item1, 9, () =>
                {
                    Write(_caster, "_spells", new (string, AreaStrike.Shape?)[] { (spell.Item1, spell.Item2) });
                    Call(_caster, "Cast");
                });
            foreach (int direction in new[] { 0, 1 })
                await Clip(direction == 0 ? "降り積もる未送信 / 上から" : "降り積もる未送信 / 下から", 8, () =>
                {
                    var edge = BossEdgeVolley.Begin(_boss, "akari", direction);
                    Write(_caster, "_edgeVolley", edge);
                    _root.Hud.AnnounceSpell("あかり", BossHandles.AkariSpell, edge.SpellName, new Color("a6d8ed"));
                });
            await Clip("第二形態 / ずっと一緒", 16, () =>
            {
                SetHp(0.5f);
                Call(_boss, "AdvanceForm2", typeof(Enemy));
                Pattern(2);
                Audio.Instance?.StartAkariMusic(3, 0.5f);
            });
            await Clip("離さない / 連続放射", 12, () => { SetHp(0.25f); Pattern(3); });
            await Clip("狐火・三尾の便り", 12, () =>
            {
                _allowTechnique = true;
                Write(_boss, "_animalTechniqueCooldown", 0d, typeof(Enemy));
            });
            await Clip("終盤 / 狙い扇＋連続放射", 14, () =>
            {
                SetHp(0.18f);
                _allowBody = true;
                Write(_boss, "_finale", true);
                _root.Hud.AnnounceSpell("あかり", BossHandles.AkariSpell, "ねえ、こっち見て＋すきって言って", new Color("6c9cd8"));
                Audio.Instance?.StartAkariMusic(4, 0.5f);
            });
            _running = false;
            _pilot.Release();
            System.IO.File.WriteAllText(_output + "/chapters.json", System.Text.Json.JsonSerializer.Serialize(_chapters));
            GD.Print("[TECHNIQUES] ALL 11 CLIPS COMPLETE: HARD, 60 FPS, time scale 1");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"[TECHNIQUES] FAIL {error}");
            GetTree().Quit(1);
        }
    }

    private async Task Clip(string title, int seconds, Action configure)
    {
        _allowBody = _allowBarrage = _allowTechnique = false;
        _caster.CancelPendingAttacks();
        _pool.DespawnAll();
        foreach (var node in GetTree().GetNodesInGroup("aoe")) node.QueueFree();
        _root.Player.GlobalPosition = Field.PlayerStart;
        _boss.GlobalPosition = new Vector2(Field.BossCenterX, Field.BossZoneCenterY);
        Write(_boss, "_fireT", 0d);
        Write(_boss, "_fireT2", 0d);
        Write(_boss, "_danmaku", new BossDanmaku(_boss), typeof(Enemy));
        _root.Hud.HideSpellCard();
        double start = Engine.GetProcessFrames() / 60d;
        GD.Print($"[TECHNIQUES] START {_clipIndex + 1:00} t={start:F3} {title}");
        configure();
        for (int frame = 0; frame < seconds * 60; frame++)
        {
            await Frames(1);
            if (frame == 3 * 60)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng($"{_output}/clip_{_clipIndex + 1:00}.png");
            }
        }
        _chapters.Add(new { title, start, end = Engine.GetProcessFrames() / 60d });
        _clipIndex++;
    }

    private void Pattern(int pattern)
    {
        _allowBody = true;
        Write(_boss, "_pattern", pattern);
        Call(_boss, "ApplySpell");
    }

    private void SetHp(float ratio)
    {
        Write(_boss, "_hp", Mathf.RoundToInt(_boss.TotalBars * Enemy.BarHp * ratio), typeof(Enemy));
        _root.Hud.UpdateBossBar(_boss.CurrentBarIndex, _boss.TotalBars, _boss.CurrentBarFrac);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_running) return;
        // Showcase scheduling selects existing attacks without changing their bullets or timing.
        if (!_allowBarrage) Write(Read<BossDanmaku>(_boss, "_danmaku", typeof(Enemy)), "_timer", 999d);
        if (!_allowBody) { Write(_boss, "_fireT", -999d); Write(_boss, "_fireT2", -999d); }
        if (!_allowTechnique) Write(_boss, "_animalTechniqueCooldown", 999d, typeof(Enemy));
        _pilot.Update(delta, shoot: false, bombs: false, dodge: false);
    }

    public override void _Process(double delta)
    {
        if (_running) Call(_root.Player, "SetSpriteVisible", null, true);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
