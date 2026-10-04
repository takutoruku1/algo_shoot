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
            _game.Difficulty = GameManager.Diff.Normal;
            _game.MarkIdleDialogSeen("once_midboss_shield");
            await Frames(2);
            var densities = new List<int>();
            int previousHp = 0, previousInk = 0;
            foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
            {
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
                densities.Add(shots);
                Check(shots >= 60 && Hostile().All(b => b.CollisionMask == 1 && Read<bool>(b, "_bossProjectile")), scene + " sustained non-destructible player-style danmaku");
                Pool.DespawnAll();
                var sentinel = Pool.Spawn(new Vector2(350, 190), Vector2.Zero, true, 3);
                sentinel.SetPhysicsProcess(false);
                foreach (var panel in Read<List<Panel>>(boss, "_panels").ToArray()) panel.Shatter();
                Check(boss.GaugeVulnerable && sentinel.Active, scene + " shield break preserves fired bullets");
                int before = Hostile().Length;
                for (int i = 0; i < 120; i++) danmaku.Tick(1d / 60);
                Check(Hostile().Length > before, scene + " break has continuing reduced danmaku");
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
                    Call(caster, "SpawnTelegraphs");
                    var area = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>().First();
                    float radius = Read<float>(area, "_hw");
                    Check(area.CoversPoint(player.Position) && radius >= 34, scene + " broad AOE covers the player");
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
                root.QueueFree(); Pool.DespawnAll();
                foreach (string group in new[] { "aoe", "boss_edge_volleys", "boss_animal_techniques" })
                    foreach (var node in GetTree().GetNodesInGroup(group)) node.QueueFree();
                await Frames(5);
            }
            Check(densities.Zip(densities.Skip(1), (a, b) => b > a).All(increasing => increasing), "danmaku density increases Akari < Koharu < Rei < Mina");
            Check(UiKit.Zen.MultichannelSignedDistanceField && UiKit.Zen.MsdfSize >= 96, "dialogue uses smooth scalable Japanese fonts");
            var audio = GetNode<Audio>("/root/Audio");
            audio.StartPostMusic("mina", 0, 0);
            Check(Read<AudioEffectLowPassFilter>(audio, "_postFilter").CutoffHz >= 15000, "boss music opens with full rhythm and treble");
            GD.Print("[CombatQA] ALL PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("[CombatQA] FAIL " + error); GetTree().Quit(1); }
    }
    private static void CheckInField(Vector2 point)
    {
        if (!global::Field.Rect.HasPoint(point)) throw new Exception("boss route left the field: " + point);
    }
    private async Task CheckPlayerAndVolleys(Node world, Player player, Enemy boss)
    {
        Write(player, "_locked", true); Write(player, "_lockTarget", boss);
        float locked = player.MoveSpeed;
        Write(player, "_locked", false);
        Check(Mathf.IsEqualApprox(locked, player.MoveSpeed * .4f), "lock movement is half the previous speed");
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
