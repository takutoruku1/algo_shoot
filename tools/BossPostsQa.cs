using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossPostsQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object o, string name, Type? type = null)
        => (T)(type ?? o.GetType()).GetField(name, Private)!.GetValue(o)!;
    private static void Write(object o, string name, object value, Type? type = null)
        => (type ?? o.GetType()).GetField(name, Private)!.SetValue(o, value);
    private static void Call(object o, string name, params object[] args)
        => o.GetType().GetMethod(name, Private | BindingFlags.Public)!.Invoke(o, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[BossPostsQA] PASS {message}");
    }
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.AutoAdvanceDialog = false;
            game.MsgCharsPerSec = 300;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            foreach (string id in new[] { "koharu", "rei", "mina" })
            {
                if (OS.GetCmdlineUserArgs().Contains("--mina-only") && id != "mina") continue;
                foreach (var diff in Enum.GetValues<GameManager.Diff>())
                    foreach (var job in Enum.GetValues<Job>())
                        await Encounter(game, id, diff, job);
            }
            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var player in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { player.Stop(); player.Stream = null; }
            await Task.Delay(250);
            await Frames(8);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[BossPostsQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[BossPostsQA] FAIL {e}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task Encounter(GameManager game, string id, GameManager.Diff diff, Job job)
    {
        game.Difficulty = diff;
        game.SelectedJob = job;
        string scenePath = id switch { "koharu" => "Koharu", "rei" => "Rei", _ => "MinaBattle" };
        var root = GD.Load<PackedScene>($"res://{scenePath}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        var stage = root.GetNode(id == "mina" ? "StageMina" : id == "rei" ? "StageRei" : "StageKoharu");
        stage.SetProcess(false);
        Write(stage, "_step", -1);
        Write(stage, "_startBannerShown", true);
        if (id == "mina") Write(stage, "_titleThump", true);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Inherit;
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        var player = world.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        player.GlobalPosition = new Vector2(Field.Left + 32, 104);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        Enemy boss = id switch { "koharu" => new BossKoharu(), "rei" => new BossRei(), _ => new BossMina() };
        world.AddChild(boss);
        Write(stage, "_boss", boss);
        Write(stage, "_bossActive", true);
        boss.GlobalPosition = new Vector2(Field.Right - 64, 104);
        root.GetNode<StageBackground>("StageBackground").EnterBoss();
        await Frames(diff == GameManager.Diff.Normal ? 150 : 4);
        boss.SetPhysicsProcess(false);
        boss._PhysicsProcess(2);
        var caster = Read<Node>(boss, "_caster");
        caster.SetProcess(false);
        Call(caster, "CancelPendingAttacks");
        var sequence = boss.GetNode<BossPostSequence>("PostSequence");
        var realm = root.GetNode<BossRealmFx>("BossRealmFx");
        bool pictures = diff == GameManager.Diff.Normal && job == Job.Tank;
        int memories = 0, phases = 0;
        for (int index = 0; index < 5; index++)
        {
            if (caster is MinaPhaseAttacks mina)
                typeof(MinaPhaseAttacks).GetProperty("OpenerCompleted")!.SetValue(mina, true);
            boss.DealDirectDamage(99999);
            await Frames(2);
            for (int attempt = 0; attempt < 30 && GetTree().GetFirstNodeInGroup("boss_post") == null; attempt++)
            {
                if (GetTree().GetFirstNodeInGroup("storyfilm") is StoryFilm film)
                {
                    Check(!sequence.Active, "memory precedes post; no overlapping cutscenes");
                    typeof(StoryFilm).GetMethod("Restore", Private)!.Invoke(film, null);
                    Read<Action>(film, "_completed", typeof(StoryFilm))();
                    film.QueueFree();
                    memories++;
                }
                if (boss is BossKoharu && Read<int>(boss, "_gotoPhase") > 0)
                    for (int tick = 0; tick < 3; tick++) Call(boss, "TickGoto", 5d);
                if (boss is BossRei && Read<bool>(boss, "_relayWatching"))
                {
                    Check(!sequence.Active, "safe-zone relay completes before post");
                    Call(caster, "CancelPendingAttacks");
                    Call(boss, "TickRelayWatch");
                }
                await Frames(3);
                if (!sequence.Pending && !sequence.Active && !hud.CinematicMode) boss.DealDirectDamage(99999);
            }
            var post = GetTree().GetFirstNodeInGroup("boss_post") as BossPost;
            Check(post != null && post.Index == index && post.Story.Id == id,
                $"{id}/{diff}/{job}/{index}: exactly one correctly ordered character post");
            Check(sequence.Pending && !boss.IsPurified && !boss.Visible, "burst stops at post boundary");
            post!.SetPhysicsProcess(false);
            stage._Process(5);
            Check(Read<double>(stage, "_rainT") == 0 && !Pool.GetChildren().OfType<Bullet>().Any(b => b.Active && b.IsEnemy),
                "stage-level ambient attacks also pause for the post");
            var text = UiKit.WrapLines(UiKit.Zen, post.Story.Posts[index], index == 4 ? 23 : 25, 466);
            Check(text.Count <= 4, "post body fits illustrated card");
            float hp = boss.HpRatio;
            double clock = Read<double>(boss, "_phaseT", typeof(Enemy));
            boss.DealDirectDamage(99999);
            boss._PhysicsProcess(10);
            Check(boss.HpRatio == hp && Read<double>(boss, "_phaseT", typeof(Enemy)) == clock, "body and shield clock locked");
            boss.Purify();
            Check(Read<int>(post, "_ink") == 16, "bomb hits post only");
            hud.HoldBubble = true;
            hud.ShowDialog(Hud.LineKind.Mina, "Pause check");
            await Frames(2);
            double postTime = Read<double>(post, "_time");
            post._PhysicsProcess(10);
            boss.Purify();
            Check(Read<double>(post, "_time") == postTime && Read<int>(post, "_ink") == 16, "dialogue pauses reading and damage");
            hud.HoldBubble = false;
            hud.HideBubble();
            await Frames(2);
            post._PhysicsProcess(.5);
            if (pictures) await Shot($"{id}_post_{index}");
            if (diff == GameManager.Diff.Normal && index == 0)
            {
                player.SetPhysicsProcess(true);
                await Frames(3);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, Pressed = true });
                await Frames(4);
                Check(player.LockTarget == boss, $"{job}: lock follows post");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, Pressed = false });
                for (int frame = 0; frame < 1200 && Read<int>(post, "_ink") == 16; frame++) await Frames(1);
                player.SetPhysicsProcess(false);
                Check(Read<int>(post, "_ink") < 16, $"{job}: actual shooting hits post");
            }
            if (index == 1)
            {
                var charged = Pool.Spawn(post.GlobalPosition, Vector2.Zero, false, 3, 99);
                charged.MakeCharged(job);
                charged.Pierce = 2;
                Call(post, "Hit", charged);
                Call(post, "Hit", charged);
                Check(Read<int>(post, "_ink") == 4, "charged shot cracks deeply once without repeat overlap damage");
                Pool.Despawn(charged);
            }
            for (int hit = 0; hit < 4; hit++) boss.Purify();
            Check(Read<int>(post, "_ink") == 0 && !Read<bool>(post, "_broken"), "reading hold protects text before shatter");
            Check(!Pool.GetChildren().OfType<Bullet>().Any(b => b.Active && b.IsEnemy), "no enemy fire while reading post");
            post._PhysicsProcess(post.MinimumReadTime);
            Check(realm.Depth == index + 1 && Audio.Instance!.PostMusicDepth == index + 1, "fracture and music grow together");
            post._PhysicsProcess(.5);
            if (pictures) await Shot($"{id}_shatter_{index}");
            if (index == 4) realm._Process(4);
            post._PhysicsProcess(post.BreakDuration);
            await Frames(3);
            Check(sequence.Count == index + 1, "one destruction consumes one gate");
            if (id == "mina" && index < 4)
            {
                var phase = GetTree().GetFirstNodeInGroup("mina_phase_scene") as MinaPhaseScene;
                Check(phase != null && ((BossMina)boss).EncounterPhase == index + 1, "post leads into corresponding costume conversation");
                Call(phase!, "Restore");
                Read<Action>(phase!, "_completed")();
                phase!.QueueFree();
                phases++;
                Check(!((MinaPhaseAttacks)caster).OpenerCompleted, "new costume keeps mandatory opening signature");
                Call(root, "TickJourney");
            }
            if (index < 4)
            {
                Check(boss.Visible && !sequence.Active, "battle resumes between posts");
                caster.SetProcess(false);
                Call(caster, "CancelPendingAttacks");
                await Frames(3);
            }
        }
        Check(memories == 1 && (id != "mina" || phases == 4), "memory and every transformation preserved exactly once");
        var draft = GetTree().GetFirstNodeInGroup("boss_draft") as BossDraftScene;
        Check(draft != null && realm.Revealed && world.ProcessMode == ProcessModeEnum.Disabled
            && game.ProcessMode == ProcessModeEnum.Disabled, "original draft reveals reality and holds battle");
        Check(!Audio.Instance!.PostMusicActive, "battle mix stops for true feelings");
        boss.DealDirectDamage(99999);
        boss.Purify();
        Check(!boss.IsPurified, "final dialogue cannot be bypassed by bombs or damage");
        draft!.SetProcess(false);
        draft._Process(5);
        hud.RevealDialogNow();
        if (pictures)
        {
            await Shot($"{id}_real_realm");
            DisplayServer.WindowSetSize(new Vector2I(540, 960));
            await Shot($"{id}_real_realm_mobile");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
        game.AutoAdvanceDialog = true;
        for (int line = 0; line < 4; line++)
        {
            hud.RevealDialogNow();
            draft._Process(2);
        }
        draft._Process(1);
        await Frames(3);
        game.AutoAdvanceDialog = false;
        Check(boss.IsPurified && !sequence.Active && !hud.CinematicMode, "redemption follows original draft exactly once");
        Check(world.ProcessMode == ProcessModeEnum.Inherit && game.ProcessMode != ProcessModeEnum.Disabled, "world and timer restore");
        if (root is KoharuRoot or ReiRoot) root._Process(.1);
        Check(root.GetNodeOrNull<CanvasModulate>("Tint") is not { } tint || tint.Color == Colors.White, "reality remains free of cold tint");
        var layers = root.GetNode<BgLayers>("StageBackground/BgLayers").GetChildren().OfType<Sprite2D>();
        Check(layers.Any(layer => layer.Texture.ResourcePath == BossPostStory.Get(id).RealBackground), "character's real background persists");
        Pool.DespawnAll();
        hud.HoldBubble = false;
        hud.HideBubble();
        root.QueueFree();
        await Frames(8);
        Check(GetTree().GetFirstNodeInGroup("boss_post") == null && GetTree().GetFirstNodeInGroup("boss_realm") == null,
            "leaving encounter removes post and realm state");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/boss_posts/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await Frames(5);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var pixels = GetViewport().GetTexture().GetImage();
        Check(pixels.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
