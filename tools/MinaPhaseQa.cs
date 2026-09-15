using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class MinaPhaseQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object value, string name, Type? type = null)
        => (T)(type ?? value.GetType()).GetField(name, Private)!.GetValue(value)!;
    private static void Write(object value, string name, object data, Type? type = null)
        => (type ?? value.GetType()).GetField(name, Private)!.SetValue(value, data);
    private static void Call(object value, string name, params object[] args)
        => value.GetType().GetMethod(name, Private)!.Invoke(value, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[MinaPhaseQA] PASS {message}");
    }
    private static void CompleteOpener(MinaPhaseAttacks caster)
        => typeof(MinaPhaseAttacks).GetProperty("OpenerCompleted")!.SetValue(caster, true);
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.MsgCharsPerSec = 300;
            game.AutoAdvanceDialog = false;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            var root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<MinaRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            root.Stage.SetProcess(false);
            root.World.ProcessMode = ProcessModeEnum.Inherit;
            root.Hud.HideBubble();
            root.Player.SetPhysicsProcess(false);
            Write(root.Player, "_invincible", true);
            Write(root.Player, "_invincibleTimer", 999f);
            var boss = new BossMina();
            root.World.AddChild(boss);
            boss.GlobalPosition = new Vector2(Field.Right - 64, 75);
            root.GetNode<StageBackground>("StageBackground").EnterBoss();
            await Frames(150);
            boss.SetPhysicsProcess(false);
            var caster = Read<MinaPhaseAttacks>(boss, "_caster");
            caster.SetProcess(false);
            caster.CancelPendingAttacks();
            await Frames(3);
            if (!OS.GetCmdlineUserArgs().Contains("--sequence-only")) await CheckAttacks(game, root, boss, caster);
            await CheckPhases(game, root, boss, caster);
            root.QueueFree();
            await Frames(8);
            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[MinaPhaseQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[MinaPhaseQA] FAIL {e}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task CheckAttacks(GameManager game, MinaRoot root, BossMina boss, MinaPhaseAttacks caster)
    {
        var starts = new[] { new Vector2(Field.Left + 12, 20), new Vector2(Field.Right - 12, Field.Bottom - 20),
            new Vector2(Field.CenterX, Field.CenterY) };
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
            foreach (var job in Enum.GetValues<Job>())
            {
                game.Difficulty = diff;
                game.SelectedJob = job;
                foreach (var start in starts)
                    for (int phase = 0; phase < 5; phase++)
                    {
                        root.Player.GlobalPosition = start;
                        caster.BeginPhase(phase);
                        caster._Process(2d);
                        Check(caster.Active, $"{diff}/{job}/{phase}: signature announces before damage");
                        int waves = phase == 0 ? 1 : phase == 4 ? 7 : 3;
                        for (int wave = 0; wave < waves; wave++)
                        {
                            Vector2 locked = root.Player.GlobalPosition;
                            caster._Process(1d);
                            var zones = root.World.GetChildren().OfType<AreaStrike>().Where(z => !z.IsQueuedForDeletion()).ToArray();
                            foreach (var zone in zones) zone.SetProcess(false);
                            Check(zones.Length > 0 && zones.Any(z => z.CoversPoint(locked)) && zones.All(z => !z.IsStriking),
                                $"{phase}/{wave}: staying still is threatened only after a warning");
                            Vector2 safe = NearestSafe(locked, zones);
                            double warn = zones.Min(z => Read<double>(z, "_warn"));
                            Check(warn >= locked.DistanceTo(safe) / root.Player.SlowestMoveSpeed + 0.3,
                                $"{phase}/{wave}: escape is reachable even while locked on");
                            root.Hud.HoldBubble = true;
                            root.Hud.ShowDialog(Hud.LineKind.Mina, "Pause QA");
                            await Frames(1);
                            double timer = Read<double>(zones[0], "_t");
                            zones[0]._Process(5d);
                            Check(Read<double>(zones[0], "_t") == timer, "conversation suspends the warning clock");
                            root.Hud.HoldBubble = false;
                            root.Hud.HideBubble();
                            await Frames(1);
                            root.Player.GlobalPosition = safe;
                            foreach (var zone in zones) zone._Process(warn + 0.01);
                            Check(zones.All(z => !z.CoversPoint(safe)), "visible escape remains safe on impact");
                            foreach (var zone in zones) zone.QueueFree();
                            await Frames(2);
                        }
                        caster._Process(1d);
                        Check(!caster.Active && caster.OpenerCompleted, "all signature waves finish and release combat");
                    }
            }
        game.Difficulty = GameManager.Diff.Normal;
        game.SelectedJob = Job.Tank;
        caster.CancelPendingAttacks();
        await Frames(2);
        root.Player.GlobalPosition = new Vector2(Field.Left + 35, 160);
        Write(root.Player, "_invincible", false);
        Write(root.Player, "_invincibleTimer", 0f);
        int lives = root.Player.Lives;
        caster.BeginPhase(1);
        caster._Process(2d);
        caster._Process(1d);
        var danger = root.World.GetChildren().OfType<AreaStrike>().Single();
        danger.SetProcess(false);
        danger._Process(Read<double>(danger, "_warn") + 0.01);
        Check(root.Player.Lives == lives - 1, "stationary player actually takes AOE damage");
        Write(root.Player, "_invincible", true);
        Write(root.Player, "_invincibleTimer", 999f);
        caster.CancelPendingAttacks();
        await Frames(3);
        caster.BeginPhase(3);
        caster._Process(2d);
        typeof(Enemy).GetMethod("EnterBreak", Private)!.Invoke(boss, null);
        boss._PhysicsProcess(2d);
        await Frames(3);
        Check(Read<CollisionShape2D>(boss, "_bodyShape", typeof(Enemy)).Disabled,
            "opening the damage window cannot restore contact during an AOE");
        caster.CancelPendingAttacks();
        await Frames(3);
        Check(!Read<CollisionShape2D>(boss, "_bodyShape", typeof(Enemy)).Disabled,
            "ordinary combat restores body contact");
    }

    private static Vector2 NearestSafe(Vector2 start, AreaStrike[] zones)
    {
        Vector2 best = start;
        float distance = float.MaxValue;
        for (float y = 12; y <= Field.Bottom - 12; y += 2)
            for (float x = Field.Left + 12; x <= Field.Right - 12; x += 2)
            {
                var p = new Vector2(x, y);
                float d = start.DistanceSquaredTo(p);
                if (d >= distance || zones.Any(z => z.CoversPoint(p))) continue;
                best = p;
                distance = d;
            }
        Check(distance < float.MaxValue, "attack always has an in-field escape");
        return best;
    }

    private async Task CheckPhases(GameManager game, MinaRoot root, BossMina boss, MinaPhaseAttacks caster)
    {
        int maxHp = Read<int>(boss, "_maxHp", typeof(Enemy));
        Write(boss, "_pattern", 0);
        caster.BeginPhase(0);
        for (int phase = 1; phase <= 4; phase++)
        {
            CompleteOpener(caster);
            boss.DealDirectDamage(9999);
            Check(Mathf.IsEqualApprox(boss.HpRatio, BossMina.PhaseThresholds[phase - 1]),
                $"phase {phase}: burst damage stops at the next HP boundary (actual {boss.HpRatio}, paused {Hud.BubblePaused})");
            await Frames(2);
            var scene = GetTree().GetFirstNodeInGroup("mina_phase_scene") as MinaPhaseScene;
            Check(scene != null && boss.EncounterPhase == phase && boss.Transitioning, $"phase {phase}: transformation is not skipped");
            float hp = boss.HpRatio;
            var position = root.Player.GlobalPosition;
            double phaseTime = Read<double>(boss, "_phaseT", typeof(Enemy));
            int bombs = game.Bombs;
            boss.DealDirectDamage(9999);
            typeof(Enemy).GetMethod("BombStrike", Private)!.Invoke(boss, null);
            root.Player.SetPhysicsProcess(true);
            boss.SetPhysicsProcess(true);
            KeyEvent(Key.Right, true);
            KeyEvent(Key.X, true);
            await Frames(60);
            KeyEvent(Key.Right, false);
            KeyEvent(Key.X, false);
            root.Player.SetPhysicsProcess(false);
            boss.SetPhysicsProcess(false);
            Check(boss.HpRatio == hp && root.Player.GlobalPosition == position && game.Bombs == bombs
                && Read<double>(boss, "_phaseT", typeof(Enemy)) == phaseTime
                && root.World.ProcessMode == ProcessModeEnum.Disabled && game.ProcessMode == ProcessModeEnum.Disabled,
                "transformation freezes controls, HP and combat clocks");
            if (phase == 1)
            {
                var backlog = GetNode<Backlog>("/root/Backlog");
                backlog.Open();
                double time = Read<double>(scene!, "_time");
                await Frames(25);
                Check(Read<double>(scene!, "_time") == time, "backlog pauses the transformation");
                Call(backlog, "Close");
                await Frames(3);
            }
            if (phase == 4)
            {
                await AdvanceUntil(() => Read<int>(scene!, "_line") == 5);
                await Frames(45);
            }
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                await Shot($"phase_{phase}_scene_{size.X}x{size.Y}");
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            if (phase == 2)
            {
                game.AutoAdvanceDialog = true;
                await WaitUntil(() => !IsInstanceValid(scene), 1200);
                game.AutoAdvanceDialog = false;
            }
            else await AdvanceUntil(() => !IsInstanceValid(scene));
            Check(!boss.Transitioning && !root.Hud.CinematicMode && root.World.ProcessMode == ProcessModeEnum.Inherit,
                "transformation returns to playable combat");
            Call(root, "TickJourney");
            await Frames(90);
            var sprite = boss.GetNode<Sprite2D>("Body");
            Hud.BubblePaused = true;
            boss._PhysicsProcess(0.6d);
            Check(sprite.Texture.ResourcePath == BossMina.CostumePath(phase, "idle"), "new idle costume remains equipped");
            boss.ShowSignaturePose();
            Check(sprite.Texture.ResourcePath == BossMina.CostumePath(phase, "attack"), "new costume also has a casting pose");
            boss._PhysicsProcess(0.6d);
            Hud.BubblePaused = false;
            Check(sprite.Texture.ResourcePath == BossMina.CostumePath(phase, "idle"), "casting returns to the same costume");
            using (var pixels = sprite.Texture.GetImage())
                Check(pixels.DetectAlpha() != Image.AlphaMode.None, "costume retains actual transparent alpha");
            var layers = root.GetNode<BgLayers>("JourneyBackground/BgLayers").GetChildren().OfType<Sprite2D>().ToArray();
            Check(phase == 4 ? layers.Length == 0 : layers.Length == 1
                && layers[0].Texture.ResourcePath == BossMina.PhaseBackground(phase), "arena follows the active costume phase");
            await Shot($"phase_{phase}_battle");
            caster.BeginPhase(phase);
            caster._Process(2d);
            caster._Process(1d);
            foreach (var zone in root.World.GetChildren().OfType<AreaStrike>()) zone.SetProcess(false);
            await Shot($"phase_{phase}_signature");
            caster.CancelPendingAttacks();
            await Frames(3);
            if (phase == 2)
            {
                CompleteOpener(caster);
                boss.DealDirectDamage(9999);
                Check(Mathf.IsEqualApprox(boss.HpRatio, 0.5f), "memory has its own protected halfway boundary");
                await Frames(2);
                var memory = GetTree().GetFirstNodeInGroup("storyfilm");
                Check(memory is MinaStoryFilm, "memory follows Koharu's phase before Rei's reply");
                await AdvanceUntil(() => !IsInstanceValid(memory));
                Check(boss.MemoryPlayed, "mid-battle memory is recorded once");
            }
        }
        bool ended = false;
        MinaPhaseScene.Play(root.Hud, root.World, 3, () => ended = true);
        KeyEvent(Key.Ctrl, true);
        await WaitUntil(() => ended, 1200);
        KeyEvent(Key.Ctrl, false);
        await Frames(2);
        Check(!root.Hud.CinematicMode && !Hud.BubblePaused, "read-only fast-forward restores combat");
        MinaPhaseScene.Play(root.Hud, root.World, 4, () => throw new Exception("aborted callback fired"));
        await Frames(5);
        GetTree().GetFirstNodeInGroup("mina_phase_scene").QueueFree();
        await Frames(5);
        Check(!root.Hud.CinematicMode && !Hud.BubblePaused && root.World.ProcessMode == ProcessModeEnum.Inherit
            && game.ProcessMode != ProcessModeEnum.Disabled, "aborted transformation restores processing");
        caster.BeginPhase(4);
        boss.DealDirectDamage(9999);
        Check(!boss.IsPurified && boss.HpRatio > 0 && boss.HpRatio <= 2f / maxHp,
            "final costume survives until its opening signature finishes");
        CompleteOpener(caster);
        boss.DealDirectDamage(9999);
        await Frames(3);
        Check(boss.IsPurified && !caster.Active && root.World.GetChildren().OfType<AreaStrike>().Count() == 0,
            "defeat cancels every signature and enters the final conversation");
    }

    private static void KeyEvent(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    private async Task AdvanceUntil(Func<bool> done)
    {
        for (int i = 0; i < 140 && !done(); i++)
        {
            KeyEvent(Key.Z, true);
            await Frames(18);
            KeyEvent(Key.Z, false);
            await Frames(2);
        }
        Check(done(), "dialogue advances to its intended destination");
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task WaitUntil(Func<bool> done, int frames)
    {
        for (int i = 0; i < frames && !done(); i++) await Frames(1);
        Check(done(), "automatic dialogue reaches its destination");
    }
    private async Task Shot(string name)
    {
        string dir = ProjectSettings.GlobalizePath("res://build/qa_story/mina_phases/shots");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        await Frames(8);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{dir}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
