using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossIntroQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object o, string name) => (T)o.GetType().GetField(name, Private)!.GetValue(o)!;
    private static void Write(object o, string name, object value) => o.GetType().GetField(name, Private)!.SetValue(o, value);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[BossIntroQA] PASS {message}");
    }
    private GameManager _game = null!;
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private static string StageName(string id) => id switch { "akari" => "Akari", "koharu" => "Koharu", "rei" => "Rei", _ => "Mina" };
    private static Node Stage(Node root, string id) => root.GetNode("Stage" + StageName(id));

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.AutoAdvanceDialog = false;
            _game.MsgCharsPerSec = 1;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            foreach (string id in new[] { "akari", "koharu", "rei", "mina" })
            {
                foreach (Job job in Enum.GetValues<Job>()) await Encounter(id, job);
                await RouteEntry(id);
                await Lunatic(id);
            }
            foreach (int chapter in new[] { 2, 3, 4 })
            foreach (Job job in new[] { Job.Melee, Job.Heal, Job.Magic })
            {
                var opening = CharacterStory.Lines(job, chapter, CharacterStory.Beat.PreBoss);
                var lines = ((int who, string text, string face)[])typeof(CameoIntroScene)
                    .GetMethod("BossDialogue", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { job, "koharu", opening })!;
                Check(lines.Take(opening.Length).SequenceEqual(opening), $"{job}/chapter {chapter}: personal story retained");
                Check(lines.All(l => UiKit.WrapLines(UiKit.Zen, l.text, 24, 1056).Count <= 2), "chapter dialogue fits the subtitle area");
            }
            await ReplaySkip();
            await EarlyExit();
            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Frames(10);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[BossIntroQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[BossIntroQA] FAIL {e}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private Node2D Load(string id, Job job, GameManager.StageEntry entry = GameManager.StageEntry.Boss,
        GameManager.Diff difficulty = GameManager.Diff.Normal)
    {
        _game.SelectedJob = job;
        _game.SelectedEntry = entry;
        _game.Difficulty = difficulty;
        string scene = id == "mina" ? "MinaBattle" : StageName(id);
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        return root;
    }

    private async Task Encounter(string id, Job job)
    {
        if (job == Job.Tank) Read<HashSet<string>>(_game, "_idleDialogSeen").Remove(StageTutorial.BossSeenKey);
        Input.ActionPress("ui_accept");
        var root = Load(id, job);
        var stage = Stage(root, id);
        var hud = root.GetNode<Hud>("Hud");
        var world = root.GetNode<Node2D>("World");
        await Frames(3);
        var intro = GetTree().GetFirstNodeInGroup("boss_intro") as CameoIntroScene;
        Check(intro != null && _game.BossReached, $"{id}/{job}: boss entry opens exchange and preserves retry checkpoint");
        intro!.SetProcess(false);
        stage.SetProcess(false);
        Check(GetTree().GetFirstNodeInGroup("cameo_intro") == null, "main boss is distinct from the midboss introduction");
        Check(hud.CinematicMode && Hud.BubblePaused && world.ProcessMode == ProcessModeEnum.Disabled
            && _game.ProcessMode == ProcessModeEnum.Disabled, "battle and clocks are paused");
        Check(!Pool.GetChildren().OfType<Bullet>().Any(b => b.Active), "bullets cleared before conversation");
        var boss = Read<Enemy>(stage, "_boss");
        Vector2 bossPosition = boss.Position;
        double elapsed = Read<double>(stage, "_stageElapsed");
        stage._Process(10);
        Check(elapsed == Read<double>(stage, "_stageElapsed"), "stage clock does not advance during exchange");
        double spellTimer = Read<double>(hud, "_spellTimer");
        if (id != "mina") Check(spellTimer > 0, "opening spell declaration survives cinematic setup");
        var lines = Read<(int who, string text, string face)[]>(intro, "_lines");
        var opening = Read<(int who, string text, string face)[]>(intro, "_opening");
        Check(lines.Take(opening.Length).Select(l => l.text).SequenceEqual(opening.Select(l => l.text)), "original opening dialogue retained in order");
        int actor = job != Job.Tank ? 6 : id == "mina" ? 0 : 1;
        Check(lines[^3].who == actor && lines[^2].who == 2 && lines[^1].who == actor, "player reply, boss reaction and player resolve alternate");
        Check(lines.Where(l => l.who != 2).All(l => l.who == actor), "only the selected player answers the boss");
        Check(lines.All(l => l.face == "" || ResourceLoader.Exists(l.face)), "every portrait exists");
        Check(lines.All(l => UiKit.WrapLines(UiKit.Zen, l.text, 24, 1056).Count <= 2), "all dialogue fits within two subtitle lines");
        Check(Read<Texture2D>(intro, "_background").ResourcePath == BossPostStory.Get(id).FakeBackground, "main-boss location background is used");
        if (id == "rei") Check(lines.Where(l => l.who == 2).All(l => l.face == CompanionDialogue.ReiAvatarPortrait), "Rei remains her avatar until the reveal");
        if (id == "mina" && job == Job.Tank)
            Check(Read<Texture2D?>(intro, "_playerPortrait") == null && lines.Where(l => l.who == 0).All(l => l.face == ""), "operator has no Mina portrait or duplicate body");
        intro._Process(0.6);
        intro._Process(0.7);
        Check(!Read<bool>(intro, "_postClosing"), "held confirm cannot dismiss the opening post");
        Input.ActionRelease("ui_accept");
        intro._Process(0.1);
        var post = Read<PostToast>(intro, "_post");
        Check(UiKit.WrapLines(UiKit.Zen, Read<string>(post, "_body"), UiKit.FontBody, 518).Count <= 2, "post is not truncated");
        if (job == Job.Tank) await Shot($"{id}_post");
        if (id == "akari" && job == Job.Tank)
        {
            double time = Read<double>(intro, "_time");
            var backlog = GetNode<Backlog>("/root/Backlog");
            intro.SetProcess(true);
            backlog.Open();
            await Frames(5);
            Check(Read<double>(intro, "_time") == time, "backlog pauses the conversation");
            typeof(Backlog).GetMethod("Close", Private)!.Invoke(backlog, null);
            intro.SetProcess(false);
        }
        var shown = new HashSet<int>();
        for (int i = 0; i < 400 && IsInstanceValid(intro); i++)
        {
            intro._Process(0.1);
            await Frames(2);
            if (!IsInstanceValid(intro)) break;
            int line = Read<int>(intro, "_line");
            if (!Read<bool>(intro, "_talking") || line >= lines.Length || Read<double>(intro, "_lineTime") < 0.4 || !shown.Add(line)) continue;
            Check(hud.DialogRevealed, "dialogue reveals immediately at the slowest text speed");
            Check(Read<double>(hud, "_spellTimer") == spellTimer && boss.Position == bossPosition, "spell timer and boss stay frozen through dialogue");
            if (line >= lines.Length - 3 && line <= lines.Length - 2 && (job == Job.Tank || Jobs.Get(job).CharacterId == id))
                await Shot($"{id}_{job}_{(lines[line].who == 2 ? "boss" : "reply")}");
            if (line == lines.Length - 3 && id == "rei" && job == Job.Tank)
            {
                foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(540, 960) })
                {
                    DisplayServer.WindowSetSize(size);
                    await Shot($"rei_{size.X}x{size.Y}");
                }
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
        }
        Check(shown.Count == lines.Length && !IsInstanceValid(intro), "every line plays once and auto completion works with auto mode off");
        Check(!hud.CinematicMode && world.ProcessMode == ProcessModeEnum.Inherit && _game.ProcessMode != ProcessModeEnum.Disabled, "processing modes restored");
        Check(FilmSkip.Seen(_game, $"boss_intro_{id}_{Jobs.Get(job).CharacterId}"), "replay state is scoped to boss and player");
        if (id != "mina" && job == Job.Tank)
        {
            var tutorial = Read<(int who, string text, string face)[]>(stage, "_playerBoss");
            Check(tutorial.Length == 5 && Hud.BubblePaused && hud.HoldBubble, "first-time tutorial follows the film without a combat frame between");
            Check(Read<string>(hud, "_dlgText") == tutorial[0].text, "tutorial starts at its first line");
        }
        else Check(!Hud.BubblePaused, "battle resumes without a repeated opening or unexpected tutorial");
        if (id != "mina") Check(Read<double>(hud, "_spellTimer") > 0, "spell declaration remains available at battle handoff");
        stage._Process(0.01);
        Check(GetTree().GetFirstNodeInGroup("boss_intro") == null && Read<Enemy>(stage, "_boss") == boss, "no duplicate intro or boss on the next tick");
        await Clean(root);
    }

    private async Task RouteEntry(string id)
    {
        var root = Load(id, Job.Melee, GameManager.StageEntry.Start);
        var stage = Stage(root, id);
        stage.SetProcess(false);
        var hud = root.GetNode<Hud>("Hud");
        hud.HideBubble(); hud.HoldBubble = false;
        root.GetNode<Node2D>("World").ProcessMode = ProcessModeEnum.Inherit;
        Write(stage, "_step", id == "akari" ? 10 : id == "koharu" ? 11 : id == "rei" ? 9 : 2);
        stage.GetType().GetMethod("Advance", Private)!.Invoke(stage, null);
        stage.GetType().GetMethod("Step_BossSpawn", Private)!.Invoke(stage, null);
        Check(GetTree().GetFirstNodeInGroup("boss_intro") is CameoIntroScene, $"{id}: normal route uses the same encounter");
        await Clean(root);
    }

    private async Task Lunatic(string id)
    {
        Read<HashSet<string>>(_game, "_idleDialogSeen").Remove(StageTutorial.BossSeenKey);
        var root = Load(id, Job.Tank, difficulty: GameManager.Diff.Lunatic);
        await Frames(5);
        Check(GetTree().GetFirstNodeInGroup("boss_intro") == null && !Hud.BubblePaused && _game.BossReached,
            $"{id}: Lunatic immediately enters combat");
        Check(!_game.IsIdleDialogSeen(StageTutorial.BossSeenKey), "Lunatic does not consume the tutorial");
        await Clean(root);
    }

    private async Task ReplaySkip()
    {
        _game.MarkIdleDialogSeen(StageTutorial.BossSeenKey);
        var root = Load("akari", Job.Tank);
        await Frames(3);
        var intro = (CameoIntroScene)GetTree().GetFirstNodeInGroup("boss_intro");
        intro.SetProcess(false);
        Stage(root, "akari").SetProcess(false);
        Check(Read<FilmSkip>(intro, "_skip").Available, "read boss exchange is skippable on replay");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = true });
        Input.FlushBufferedEvents();
        intro._Process(0.5); intro._Process(0.5);
        await Frames(3);
        Check(IsInstanceValid(intro) && root.GetNode<Node2D>("World").ProcessMode == ProcessModeEnum.Disabled, "held skip cannot fire a bomb in battle");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = false });
        Input.FlushBufferedEvents();
        intro._Process(0.1);
        await Frames(3);
        Check(!IsInstanceValid(intro) && !Hud.BubblePaused, "releasing skip hands control back to combat");
        await Clean(root);
    }

    private async Task EarlyExit()
    {
        var root = Load("mina", Job.Tank);
        await Frames(3);
        Stage(root, "mina").SetProcess(false);
        GetTree().GetFirstNodeInGroup("boss_intro").QueueFree();
        await Frames(3);
        Check(root.GetNode<Node2D>("World").ProcessMode == ProcessModeEnum.Inherit && !Hud.BubblePaused
            && _game.ProcessMode != ProcessModeEnum.Disabled, "interruption restores all pause owners");
        await Clean(root);
    }

    private async Task Clean(Node root)
    {
        Input.ActionRelease("ui_accept");
        Pool.DespawnAll();
        root.QueueFree();
        await Frames(25);
        Check(!Hud.BubblePaused && _game.ProcessMode != ProcessModeEnum.Disabled, "scene exit leaves no pause owner");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/boss_intro/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await Frames(5);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
