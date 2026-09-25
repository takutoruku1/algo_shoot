using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class AkariPostQa : Node
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
        GD.Print($"[AkariPostQA] PASS {message}");
    }
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
            if (OS.GetCmdlineUserArgs().Contains("--body-art")) await BodyArt(game);
            else if (OS.GetCmdlineUserArgs().Contains("--realm-movie")) await RealmMovie(game);
            else if (OS.GetCmdlineUserArgs().Contains("--realm-art")) await RealmArt(game);
            else
                foreach (var diff in Enum.GetValues<GameManager.Diff>())
                    foreach (var job in Enum.GetValues<Job>())
                        await Encounter(game, diff, job);
            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[AkariPostQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[AkariPostQA] FAIL {e}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task Encounter(GameManager game, GameManager.Diff diff, Job job)
    {
        game.Difficulty = diff;
        game.SelectedJob = job;
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        root.Stage.SetProcess(false);
        root.World.ProcessMode = ProcessModeEnum.Inherit;
        root.Hud.HideBubble();
        root.Hud.HoldBubble = false;
        root.Player.SetPhysicsProcess(false);
        root.Player.GlobalPosition = new Vector2(Field.Left + 35, 104);
        var boss = new BossAkari();
        root.World.AddChild(boss);
        boss.GlobalPosition = new Vector2(Field.Right - 64, 104);
        root.GetNode<StageBackground>("StageBackground").EnterBoss();
        await Frames(90);
        boss.SetPhysicsProcess(false);
        var caster = Read<AreaSpellCaster>(boss, "_caster");
        caster.SetProcess(false);
        caster.CancelPendingAttacks();
        bool story = diff == GameManager.Diff.Normal && job == Job.Tank;
        if (!story)
        {
            Write(boss, "_memoryPlayed", true);
            Write(boss, "_corridorFired", true);
        }
        float[] thresholds = { .8f, .6f, .4f, .2f, .01f };
        for (int i = 0; i < 5; i++)
        {
            boss.DealDirectDamage(99999);
            Check(Mathf.IsEqualApprox(boss.HpRatio, thresholds[i]) && !boss.IsPurified,
                $"{diff}/{job}/{i + 1}: burst damage stops at post boundary");
            await Frames(2);
            if (story && i == 2)
            {
                var film = GetTree().GetFirstNodeInGroup("storyfilm") as AkariStoryFilm;
                Check(film != null && GetTree().GetFirstNodeInGroup("boss_post") == null,
                    "memory runs before the pending third post");
                typeof(StoryFilm).GetMethod("Restore", Private)!.Invoke(film, null);
                Read<Action>(film!, "_completed", typeof(StoryFilm))();
                film!.QueueFree();
                await Frames(3);
                var corridor = GetTree().GetFirstNodeInGroup("corridor") as CorridorRun;
                Check(corridor != null && GetTree().GetFirstNodeInGroup("boss_post") == null,
                    "corridor runs before the pending third post");
                corridor!._PhysicsProcess(14.0);
                Call(boss, "TickCorridor");
                boss.GlobalPosition = new Vector2(Field.Right - 70, 104);
                Call(boss, "TickCorridor");
                corridor.QueueFree();
                await Frames(3);
            }
            var post = GetTree().GetFirstNodeInGroup("boss_post") as BossPost;
            Check(post != null && post.Index == i, "exactly one shootable post in order");
            Check(BossPost.Labels[i] == (i == 0 ? "公開したポスト" : i == 4 ? "最初の下書き" : $"{i}つ前の下書き"),
                "one published post rewinds through four draft revisions");
            post!.SetPhysicsProcess(false);
            float hp = boss.HpRatio;
            double phaseTime = Read<double>(boss, "_phaseT", typeof(Enemy));
            boss.DealDirectDamage(99999);
            boss._PhysicsProcess(10);
            Check(boss.HpRatio == hp && Read<double>(boss, "_phaseT", typeof(Enemy)) == phaseTime,
                "body damage and shield clock cannot bypass a post");
            boss.Purify();
            Check(Read<int>(post, "_ink") == 16 && boss.HpRatio == hp, "bomb damages the post, not the protected boss");
            root.Hud.HoldBubble = true;
            root.Hud.ShowDialog(Hud.LineKind.Mina, "Pause QA");
            await Frames(1);
            double clock = Read<double>(post, "_time");
            post._PhysicsProcess(6);
            boss.Purify();
            Check(Read<double>(post, "_time") == clock && Read<int>(post, "_ink") == 16,
                "conversation freezes the post and bomb damage");
            root.Hud.HoldBubble = false;
            root.Hud.HideBubble();
            await Frames(2);
            if (i == 0)
            {
                root.Player.SetPhysicsProcess(true);
                await Frames(3);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.S, Pressed = true });
                await Frames(3);
                Check(root.Player.LockTarget == boss, $"{job}: lock-on follows the post surface");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.S, Pressed = false });
                await Frames(120);
                root.Player.SetPhysicsProcess(false);
                Check(Read<int>(post, "_ink") < 16, $"{job}: actual player shots damage the post");
                Check(!Pool.GetChildren().OfType<Bullet>().Any(b => b.Active && b.IsEnemy),
                    "no boss barrage competes with post reading");
                Pool.DespawnAll();
            }
            post._PhysicsProcess(0.5);
            if (story)
            {
                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(540, 960) })
                {
                    DisplayServer.WindowSetSize(size);
                    await Shot($"post_{i + 1}_{size.X}x{size.Y}");
                }
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            for (int hit = 0; hit < 4; hit++)
            {
                Pool.Spawn(post.GlobalPosition + new Vector2(-70, 0), new Vector2(140, 0), false, 3, 99,
                    homing: job == Job.Heal);
                await Frames(5);
            }
            Check(Read<int>(post, "_ink") == 0 && !Read<bool>(post, "_broken"),
                "real bullet collisions crack the post without erasing it before reading");
            post._PhysicsProcess(post.MinimumReadTime);
            Check(Read<bool>(post, "_broken"), "shooting destroys the post after its reading hold");
            var realm = (BossRealmFx)GetTree().GetFirstNodeInGroup("boss_realm");
            Check(realm.Depth == i + 1 && realm.Revealing == (i == 4), "fracture depth grows; only the original draft opens reality");
            Check(Audio.Instance.AkariMusicDepth == i + 1, "music intensity follows the destroyed draft depth");
            if (story) await Shot($"post_{i + 1}_broken");
            if (i == 4) realm._Process(4.0);
            post._PhysicsProcess(post.BreakDuration + 0.1);
            await Frames(3);
            Check(Read<int>(boss, "_postsBroken") == i + 1, "one destruction advances exactly one gate");
            caster.SetProcess(false);
            caster.CancelPendingAttacks();
            if (i < 4)
            {
                Check(boss.Visible && !boss.PostSequenceActive, "normal battle resumes between posts");
                boss._PhysicsProcess(0.95);
            }
        }
        var draft = GetTree().GetFirstNodeInGroup("boss_draft") as BossDraftScene;
        Check(draft != null && root.World.ProcessMode == ProcessModeEnum.Disabled
            && game.ProcessMode == ProcessModeEnum.Disabled && !boss.IsPurified, "only the fifth post opens the unsent draft");
        Check(((BossRealmFx)GetTree().GetFirstNodeInGroup("boss_realm")).Revealed,
            "real realm persists underneath the final dialogue");
        Check(!Audio.Instance.AkariLayersPlaying, "battle stems stop before the aftermath theme");
        Check(!Pool.GetChildren().OfType<Bullet>().Any(b => b.Active), "draft has no remaining bullets");
        boss.DealDirectDamage(99999);
        boss.Purify();
        Check(!boss.IsPurified, "draft reveal cannot be skipped by damage or bombs");
        draft!.SetProcess(false);
        var backlog = GetNode<Backlog>("/root/Backlog");
        if (story)
        {
            draft.SetProcess(true);
            backlog.Open();
            double t = Read<double>(draft, "_time");
            await Frames(25);
            Check(Read<double>(draft, "_time") == t, "backlog freezes draft reveal");
            Call(backlog, "Close");
            await Frames(3);
            draft.SetProcess(false);
        }
        draft._Process(5);
        root.Hud.RevealDialogNow();
        if (story)
        {
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                await Shot($"draft_{size.X}x{size.Y}");
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
        for (int step = 0; step < 24 && !Read<bool>(draft, "_leaving"); step++)
        {
            root.Hud.RevealDialogNow();
            draft._Process(0.4);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = true });
            await Frames(2);
            draft._Process(0.4);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = false });
            await Frames(2);
            draft._Process(0.01);
        }
        Check(Read<bool>(draft, "_leaving"), "draft dialogue advances with the usual confirm input");
        draft._Process(0.7);
        await Frames(3);
        Check(boss.IsPurified && root.World.ProcessMode == ProcessModeEnum.Inherit
            && game.ProcessMode != ProcessModeEnum.Disabled && !root.Hud.CinematicMode,
            "draft ends in redemption and restores gameplay owners");
        root.QueueFree();
        await Frames(8);
        Check(!Hud.BubblePaused, "scene exit releases dialogue hold");
    }

    private async Task RealmMovie(GameManager game)
    {
        game.Difficulty = GameManager.Diff.Normal;
        game.SelectedJob = Job.Tank;
        game.AutoAdvanceDialog = true;
        game.MsgCharsPerSec = 22;
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.Stage.SetProcess(false);
        root.World.ProcessMode = ProcessModeEnum.Inherit;
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        Write(root.Hud, "_bannerTimer", 0d);
        root.Player.GlobalPosition = new Vector2(Field.Left + 33, 104);
        root.Player.SetPhysicsProcess(false);
        var boss = new BossAkari();
        root.World.AddChild(boss);
        boss.Position = new Vector2(Field.Right - 76, 104);
        Write(boss, "_memoryPlayed", true);
        Write(boss, "_corridorFired", true);
        root.GetNode<StageBackground>("StageBackground").EnterBoss();
        await Frames(100);
        boss.SetPhysicsProcess(false);
        var caster = Read<AreaSpellCaster>(boss, "_caster");
        caster.SetProcess(false);
        caster.CancelPendingAttacks();
        Pool.DespawnAll();
        await Frames(40);
        for (int i = 0; i < 5; i++)
        {
            boss.DealDirectDamage(99999);
            await Frames(3);
            var post = (BossPost)GetTree().GetFirstNodeInGroup("boss_post");
            Check(post.Index == i, $"movie revision {i + 1}");
            await Frames(i == 4 ? 100 : 68);
            root.Player.SetPhysicsProcess(true);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.S, Pressed = true });
            await Frames(2);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.S, Pressed = false });
            int frames = 0;
            while (IsInstanceValid(post) && !Read<bool>(post, "_broken") && frames++ < 600) await Frames(1);
            Check(frames < 600, "actual player shots destroy the revision");
            root.Player.SetPhysicsProcess(false);
            await Frames((int)(post.BreakDuration * 60) + 6);
            caster.SetProcess(false);
            caster.CancelPendingAttacks();
            Pool.DespawnAll();
            if (i < 4) await Frames(54);
        }
        var realm = (BossRealmFx)GetTree().GetFirstNodeInGroup("boss_realm");
        Check(realm.Revealed, "movie reaches the real realm");
        int tail = 0;
        while (GetTree().GetFirstNodeInGroup("boss_draft") != null && tail++ < 60 * 45) await Frames(1);
        Check(tail < 60 * 45, "movie completes the real-realm dialogue");
        await Frames(60);
        GD.Print("[AkariPostQA] REALM MOVIE COMPLETE");
        root.QueueFree();
        await Frames(4);
    }

    private async Task RealmArt(GameManager game)
    {
        var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.Stage.SetProcess(false);
        root.World.ProcessMode = ProcessModeEnum.Inherit;
        root.Hud.HoldBubble = false;
        root.Hud.HideBubble();
        root.Player.SetPhysicsProcess(false);
        Write(root.Hud, "_bannerTimer", 0d);
        var boss = new BossAkari();
        root.World.AddChild(boss);
        boss.SetPhysicsProcess(false);
        boss.SetProcess(false);
        boss.Hide();
        root.Hud.HideSpellCard();
        var caster = Read<AreaSpellCaster>(boss, "_caster");
        caster.SetProcess(false);
        caster.CancelPendingAttacks();
        root.GetNode<StageBackground>("StageBackground").EnterBoss();
        await Frames(90);
        Pool.DespawnAll();
        for (int index = 0; index < 5; index++)
        {
            int completed = 0;
            var post = new BossPost { Story = BossPostStory.Get("akari"), Boss = boss, Index = index,
                Position = new Vector2(Field.Right - 84, 104), Broken = boss.BreakPostRealm, Completed = () => completed++ };
            root.World.AddChild(post);
            post.SetPhysicsProcess(false);
            post._PhysicsProcess(0.5);
            await Shot($"illustrated_{index + 1}_intact", 4);
            var vertices = Read<Vector2[]>(post, "_vertices");
            var triangles = Read<int[]>(post, "_triangles");
            float area = 0;
            for (int i = 0; i < triangles.Length; i += 3)
                area += Mathf.Abs((vertices[triangles[i + 1]] - vertices[triangles[i]]).Cross(vertices[triangles[i + 2]] - vertices[triangles[i]])) / 2;
            Check(Mathf.Abs(area - 520 * 320) < 0.1f, "shards cover the entire original post, with no missing panel area");
            Check(Read<SubViewport>(post, "_plateView").Size == new Vector2I(1040, 640), "whole post snapshot retains high-resolution text and artwork");
            post.BombHit();
            post.BombHit();
            await Shot($"illustrated_{index + 1}_cracked", 4);
            post.BombHit();
            await Frames(3);
            post._PhysicsProcess(post.MinimumReadTime);
            post._PhysicsProcess(0.32);
            await Shot($"illustrated_{index + 1}_shards", 4);
            if (index == 4)
            {
                foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(540, 960) })
                {
                    DisplayServer.WindowSetSize(size);
                    await Shot($"illustrated_shards_{size.X}x{size.Y}", 4);
                }
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            post._PhysicsProcess(post.BreakDuration);
            Check(completed == 1, "whole-surface destruction still completes exactly once");
            await Frames(4);
        }
        await Frames(240);
        await Shot("illustrated_real_realm", 3);
        root.QueueFree();
        await Frames(4);
    }

    private static object? BaseCall(Enemy enemy, string name, params object[] args)
        => typeof(Enemy).GetMethod(name, Private)!.Invoke(enemy, args);

    private static Vector2 Foot(Sprite2D body, float x)
    {
        Vector2 p = new Vector2(x, 718) - body.Texture.GetSize() / 2f;
        if (body.FlipH) p.X = -p.X;
        return body.ToGlobal(p + body.Offset);
    }

    private async Task BodyArt(GameManager game)
    {
        foreach (string pose in new[] { "idle", "attack", "cry" })
        {
            using var image = GD.Load<Texture2D>($"res://char/v3/boss_akari_body_{pose}_v2.png").GetImage();
            Check(image.GetHeight() == 720 && image.GetPixel(0, 0).A == 0, $"{pose}: original height and real transparency");
            int w = image.GetWidth(), h = image.GetHeight();
            var solid = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) solid[y * w + x] = image.GetPixel(x, y).A > 0.5f;
            var queue = new System.Collections.Generic.Queue<int>();
            int largeParts = 0;
            for (int i = 0; i < solid.Length; i++)
            {
                if (!solid[i]) continue;
                solid[i] = false;
                queue.Enqueue(i);
                int size = 0;
                while (queue.Count > 0)
                {
                    int p = queue.Dequeue();
                    size++;
                    int x = p % w, y = p / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h || !solid[ny * w + nx]) continue;
                            solid[ny * w + nx] = false;
                            queue.Enqueue(ny * w + nx);
                        }
                }
                if (size >= 80) largeParts++;
            }
            Check(largeParts == 1, $"{pose}: one connected figure, no detached hand or foot component");
        }
        foreach (bool flip in new[] { true, false })
        {
            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            root.Stage.SetProcess(false);
            root.World.ProcessMode = ProcessModeEnum.Inherit;
            root.Player.SetPhysicsProcess(false);
            root.Hud.HideBubble();
            root.Hud.HideSpellCard();
            Write(root.Hud, "_bannerTimer", 0d);
            var boss = new BossAkari();
            Write(boss, "FaceLeft", flip, typeof(Enemy));
            root.World.AddChild(boss);
            boss.Position = new Vector2(285, 105);
            boss.SetPhysicsProcess(false);
            boss.SetProcess(false);
            BaseCall(boss, "TickEntrance", 0d);
            BaseCall(boss, "TickEntrance", 2d);
            Read<AreaSpellCaster>(boss, "_caster").SetProcess(false);
            root.GetNode<StageBackground>("StageBackground").EnterBoss();
            foreach (var panel in boss.GetChildren().OfType<Panel>()) panel.Hide();
            root.Hud.HideSpellCard();
            var parts = boss.GetNode<BossParts>("Parts");
            foreach (var part in Read<System.Collections.IEnumerable>(parts, "_parts"))
            {
                var texture = (Texture2D)part.GetType().GetField("Tex")!.GetValue(part)!;
                Check(!texture.ResourcePath.Contains("piece_"), "runtime parts contain no floating anatomy or hem");
            }
            var body = boss.GetNode<Sprite2D>("Body");
            Check(body.IsVisibleInTree(), "entrance completes before visual checks");
            Check(body.Texture.ResourcePath.EndsWith("idle_v2.png"), "battle starts with restored idle artwork");
            Vector2 anchor = Foot(body, 266);
            if (flip) await Shot("body_idle_v2");
            BaseCall(boss, "TriggerAttackPose");
            BaseCall(boss, "TickSwapAnim", 1d);
            Check(body.Texture.ResourcePath.EndsWith("attack_v2.png") && Foot(body, 302).DistanceTo(anchor) < 0.05f,
                $"attack switches artwork without foot drift (flip={flip})");
            if (flip) await Shot("body_attack_v2");
            BaseCall(boss, "TickAttackPose", 1d);
            BaseCall(boss, "TickSwapAnim", 1d);
            Check(Foot(body, 266).DistanceTo(anchor) < 0.05f, "return to first idle keeps foot anchored");
            BaseCall(boss, "AdvanceForm2");
            BaseCall(boss, "TickSwapAnim", 1d);
            Check(Foot(body, 205).DistanceTo(anchor) < 0.05f, "second form keeps foot anchored");
            BaseCall(boss, "TriggerAttackPose");
            BaseCall(boss, "TickSwapAnim", 1d);
            BaseCall(boss, "TickAttackPose", 1d);
            BaseCall(boss, "TickSwapAnim", 1d);
            Check(body.Texture.ResourcePath.EndsWith("idle2.png") && Foot(body, 205).DistanceTo(anchor) < 0.05f,
                "attacking in form two returns with the form-two offset");
            if (flip) await Shot("body_form2");
            BaseCall(boss, "SwapBody", "res://char/v3/boss_akari_body_cry_v2.png", 1f);
            Call(boss, "OnCryStart");
            BaseCall(boss, "TickSwapAnim", 1d);
            Check(Foot(body, 271.5f).DistanceTo(anchor) < 0.05f, "crying artwork keeps both feet and the same anchor");
            if (flip)
                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
                {
                    DisplayServer.WindowSetSize(size);
                    await Shot($"body_cry_{size.X}x{size.Y}");
                }
            Check(Read<float>(boss, "BodyRadius", typeof(Enemy)) == BossTuning.F("akari", "body_radius", 19)
                && Read<float>(boss, "BodyHalfH", typeof(Enemy)) == BossTuning.F("akari", "body_half_h", 23),
                "sprite repair leaves combat collision unchanged");
            if (!flip)
                foreach (Enemy other in new Enemy[] { new BossKoharu(), new BossRei() })
                {
                    root.Hud.HideBubble();
                    root.World.AddChild(other);
                    other.SetPhysicsProcess(false);
                    other.SetProcess(false);
                    BaseCall(other, "AdvanceForm2");
                    BaseCall(other, "TriggerAttackPose");
                    BaseCall(other, "TickAttackPose", 1d);
                    Check(Read<BossParts.Pose>(other, "_bodyPose", typeof(Enemy)) == BossParts.Pose.Form2
                        && other.GetNode<Sprite2D>("Body").Texture.ResourcePath == Read<string>(other, "Form2TexPath", typeof(Enemy)),
                        $"{other.GetType().Name}: second-form return keeps its own artwork and offset");
                }
            root.QueueFree();
            Pool.DespawnAll();
            await Frames(5);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Shot(string name, int settle = 65)
    {
        await Frames(settle);
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/akari_posts/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        if (name.StartsWith("body_"))
        {
            int orange = 0;
            for (int y = image.GetHeight() / 5; y < image.GetHeight() * 2 / 3; y += 2)
                for (int x = image.GetWidth() / 2; x < image.GetWidth() * 9 / 10; x += 2)
                {
                    Color c = image.GetPixel(x, y);
                    if (c.R > 0.25f && c.R > c.G * 1.2f && c.G > c.B * 1.2f) orange++;
                }
            Check(orange > 30, "Akari's rendered body is visible in the battle viewport");
        }
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
