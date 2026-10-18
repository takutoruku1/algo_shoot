using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossPostsQa : Node
{
    protected virtual string[] Characters => new[] { "akari", "koharu", "rei", "mina" };
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(object obj, string name)
    {
        for (var t = obj.GetType(); t != null; t = t.BaseType)
            if (t.GetField(name, Private | BindingFlags.DeclaredOnly) is { } f) return f;
        throw new MissingFieldException(name);
    }
    private static void Write(object obj, string name, object value) => Field(obj, name).SetValue(obj, value);
    private static T Read<T>(object obj, string name) => (T)Field(obj, name).GetValue(obj)!;
    private static void Call(object obj, string name, params object[] args)
    {
        for (var t = obj.GetType(); t != null; t = t.BaseType)
            if (t.GetMethod(name, Private | BindingFlags.DeclaredOnly) is { } m) { m.Invoke(obj, args); return; }
        throw new MissingMethodException(name);
    }
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print("[BossPostsQA] PASS " + message);
    }
    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            await Frames(2);
            foreach (string id in Characters)
                foreach (var diff in Enum.GetValues<GameManager.Diff>())
                    foreach (var job in Enum.GetValues<Job>())
                    {
                        if (OS.GetCmdlineUserArgs().Contains("--smoke") && job != Job.Tank) continue;
                        await Encounter(game, id, diff, job);
                    }
            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            await Frames(8);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(2);
            GD.Print("[BossPostsQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError("[BossPostsQA] FAIL " + ex); GetTree().Quit(1); }
    }

    private async Task Encounter(GameManager game, string id, GameManager.Diff diff, Job job)
    {
        game.Difficulty = diff;
        game.SelectedJob = job;
        string scene = id switch { "akari" => "Akari", "koharu" => "Koharu", "rei" => "Rei", _ => "MinaBattle" };
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Inherit;
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        Write(hud, "_bannerTimer", 0d);
        root.GetNode<StageBackground>("StageBackground").EnterBoss();
        var player = world.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        player.Position = new Vector2(global::Field.Left + 32, 150);
        Enemy boss = id switch { "akari" => new BossAkari(), "koharu" => new BossKoharu(), "rei" => new BossRei(), _ => new BossMina() };
        boss.ProcessMode = ProcessModeEnum.Disabled;
        world.AddChild(boss);
        boss.Position = new Vector2(global::Field.Right - 64, 110);
        Call(boss, "TickEntrance", 0d);
        Call(boss, "TickEntrance", 100d);
        Call(boss, "TickSwapAnim", 1d);
        Write(boss, "_memoryPlayed", true);
        Write(boss, "_form2", true);
        if (boss is BossAkari) Write(boss, "_shieldBreaks", 3);
        if (boss is BossMina)
        {
            Write(boss, "_pattern", 4);
            typeof(MinaPhaseAttacks).GetProperty("OpenerCompleted")!.SetValue(Read<MinaPhaseAttacks>(boss, "_caster"), true);
        }
        var sequence = boss.GetNode<BossPostSequence>("PostSequence");
        string label = $"{id}/{diff}/{job}";
        await Frames(3);
        hud.HideBubble();
        Pool.DespawnAll();
        if (diff == GameManager.Diff.Lunatic)
        {
            Check(sequence.Floor == 0, label + " no cinematic HP floor");
            boss.DealDirectDamage(999999);
            sequence.UpdateDamage();
            Check(boss.IsPurified && GetTree().GetNodesInGroup("boss_post").Count == 0, label + " uninterrupted Lunatic defeat");
        }
        else
        {
            int hp = boss.TotalBars * boss.BarHp;
            boss.DealDirectDamage(Mathf.RoundToInt(hp * 0.1f));
            sequence._PhysicsProcess(0);
            var card = (BossPost)GetTree().GetFirstNodeInGroup("boss_post");
            Check(card != null && !card.IsBroken && Read<float>(card, "_damage") > 0, label + " body damage cracks the post");
            Check((object)card! is not CollisionObject2D && card!.GetChildren().All(n => n is not CollisionShape2D), label + " post cannot absorb shots or lock-on");
            card._PhysicsProcess(0.3);
            if (diff == GameManager.Diff.Hard && job == Job.Tank && OS.GetCmdlineUserArgs().Contains("--shots"))
                await Shot(id + "_cracks");
            float ratio = boss.HpRatio;
            var friendly = Pool.Spawn(new Vector2(150, 180), Vector2.Zero, false);
            var hostile = Pool.Spawn(new Vector2(370, 190), Vector2.Zero, true);
            boss.DealDirectDamage(Mathf.RoundToInt(hp * 0.15f));
            Check(boss.HpRatio < 0.8f && boss.HpRatio < ratio, label + " damage crosses the old 80% gate");
            sequence.UpdateDamage();
            Check(card.IsBroken && sequence.Count == 1 && !sequence.Active && boss.Visible, label + " shatter does not suspend or hide boss");
            Check(friendly.Active && hostile.Active, label + " shatter preserves both sides' projectiles");
            ratio = boss.HpRatio;
            boss.DealDirectDamage(1);
            Check(boss.HpRatio < ratio, label + " body still takes damage during shatter");
            sequence.UpdateDamage();
            Check(sequence.Count == 1, label + " one threshold cannot fracture twice");
            if (diff == GameManager.Diff.Hard && job == Job.Tank && OS.GetCmdlineUserArgs().Contains("--shots"))
            {
                card._PhysicsProcess(0.12);
                await Shot(id + "_impact");
            }
            Hud.BubblePaused = true;
            card._PhysicsProcess(3);
            Check(!card.IsQueuedForDeletion(), label + " dialogue freezes debris");
            Hud.BubblePaused = false;
            card._PhysicsProcess(BossPost.BreakDuration);
            await Frames(2);
            for (int index = 1; index < 5; index++)
            {
                Write(boss, "_memoryPending", false);
                boss.DealDirectDamage(hp);
                if (boss.Transforming)
                {
                    await Frames(2);
                    Read<BossTransformation>(boss, "_transformation")._Process(BossTransformation.Duration + .01);
                    await Frames(2);
                }
                sequence.UpdateDamage();
                card = (BossPost)GetTree().GetFirstNodeInGroup("boss_post");
                Check(card != null && card.IsBroken && card.Index == index && sequence.Count == index + 1,
                    label + $" automatic depth {index + 1} (hp={boss.HpRatio:F3}, count={sequence.Count}, pause={Hud.BubblePaused}, ui={Pad.UiBlocked(this)})");
                Check(!boss.IsPurified, label + " last draft is retained before redemption");
                card!._PhysicsProcess(BossPost.BreakDuration + .01);
                await Frames(2);
            }
            var draft = GetTree().GetFirstNodeInGroup("boss_draft") as BossDraftScene;
            Check(draft != null && sequence.Active && hud.CinematicMode, label + " only final draft pauses combat");
            Call(draft!, "Restore");
            Read<Action>(draft!, "_completed")();
            draft!.QueueFree();
            Check(sequence.Floor == 0 && boss.IsPurified, label + " final draft reaches redemption");
        }
        hud.HoldBubble = false;
        hud.HideBubble();
        Pool.DespawnAll();
        while (Engine.TimeScale < 0.99) await Frames(1);
        root.QueueFree();
        await Frames(5);
        Check(GetTree().GetNodesInGroup("boss_post").Count == 0 && GetTree().GetNodesInGroup("boss_realm").Count == 0, label + " retry leaves no effects behind");
    }

    private async Task Shot(string name)
    {
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute("res://build/qa_story/post_flow");
        Check(frame.SavePng($"res://build/qa_story/post_flow/{name}.png") == Error.Ok, name + " screenshot");
    }
}
