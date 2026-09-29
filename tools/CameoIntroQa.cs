using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class CameoIntroQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object o, string name) => (T)o.GetType().GetField(name, Private)!.GetValue(o)!;
    private static void Write(object o, string name, object value) => o.GetType().GetField(name, Private)!.SetValue(o, value);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[CameoIntroQA] PASS {message}");
    }
    private GameManager _game = null!;
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.AutoAdvanceDialog = false;
            _game.MsgCharsPerSec = 300;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            foreach (string id in new[] { "akari", "koharu", "rei" })
            {
                foreach (Job job in Enum.GetValues<Job>()) await Encounter(id, job);
                await Bypass(id, GameManager.StageEntry.MidBoss, GameManager.Diff.Lunatic);
                await Bypass(id, GameManager.StageEntry.AfterMidBoss, GameManager.Diff.Normal);
                await Bypass(id, GameManager.StageEntry.Boss, GameManager.Diff.Normal);
                await RouteEntry(id);
            }
            foreach (string chosen in new[] { "少し休んでほしい", "好きな時間は残して", "本人はどうしたい" })
                await ChoiceReaction("koharu", "s2_1", chosen);
            foreach (string chosen in new[] { "同接、9", "ちゃんと見てる", "見えてる" })
                await RemovedChoiceReaction(chosen);
            await EarlyExit();
            await ReplaySkip();
            await ManualAdvance();
            await RealtimeCadence();
            await ShieldReplayAndAbort();
            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Frames(10);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[CameoIntroQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[CameoIntroQA] FAIL {e}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private Node2D Load(string id, Job job, GameManager.StageEntry entry, GameManager.Diff difficulty = GameManager.Diff.Normal)
    {
        _game.SelectedJob = job;
        _game.SelectedEntry = entry;
        _game.Difficulty = difficulty;
        _game.AutoAdvanceDialog = false;
        string name = id switch { "akari" => "Akari", "koharu" => "Koharu", _ => "Rei" };
        var root = GD.Load<PackedScene>($"res://{name}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        return root;
    }

    private static Node Stage(Node root, string id) => root.GetNode("Stage" + (id switch { "akari" => "Akari", "koharu" => "Koharu", _ => "Rei" }));

    private async Task Encounter(string id, Job job)
    {
        Read<System.Collections.Generic.HashSet<string>>(_game, "_idleDialogSeen").Remove("once_midboss_shield");
        Input.ActionPress("ui_accept");
        var root = Load(id, job, GameManager.StageEntry.MidBoss);
        var stage = Stage(root, id);
        var hud = root.GetNode<Hud>("Hud");
        var world = root.GetNode<Node2D>("World");
        Pool.Spawn(new Vector2(Field.CenterX, 90), Vector2.Zero, true, 3, 1);
        await Frames(3);
        var intro = GetTree().GetFirstNodeInGroup("cameo_intro") as CameoIntroScene;
        Check(intro != null && GetTree().GetNodesInGroup("enemies").Count == 0, $"{id}/{job}: post scene precedes midboss spawn");
        intro!.SetProcess(false);
        stage.SetProcess(false);
        Check(hud.CinematicMode && Hud.BubblePaused && world.ProcessMode == ProcessModeEnum.Disabled
            && _game.ProcessMode == ProcessModeEnum.Disabled, "dialogue owns the pause for world and game clocks");
        Check(!Pool.GetChildren().OfType<Bullet>().Any(b => b.Active), "no bullets during post or dialogue");
        double elapsed = Read<double>(stage, "_stageElapsed");
        stage._Process(10);
        Check(elapsed == Read<double>(stage, "_stageElapsed"), "stage timer remains frozen");
        intro._Process(0.6);
        intro._Process(0.7);
        Check(!Read<bool>(intro, "_postClosing"), "held advance from previous battle cannot skip the post");
        Input.ActionRelease("ui_accept");
        intro._Process(0.1);
        var post = Read<PostToast>(intro, "_post");
        await Frames(24);
        Check(Read<Texture2D>(post, "_iconTex").ResourcePath == CompanionDialogue.AccountIcon(id), "post uses dedicated SNS icon");
        Check(UiKit.WrapLines(UiKit.Zen, Read<string>(post, "_body"), UiKit.FontBody, 518).Count <= 2,
            "full post fits without truncation");
        var lines = Read<(int who, string text, string face)[]>(intro, "_lines");
        Check(lines.Length == 3 && lines[0].who == 2 && lines[2].who == 2 && lines[1].who != 2,
            "exactly three barks: enemy, player, enemy");
        Check(lines.All(line => line.text.Length <= 30 && UiKit.WrapLines(UiKit.Zen, line.text, 24, 928).Count == 1),
            "each bark fits a single short line");
        Check(lines.Where(line => line.who != 2).All(line => line.who == (job == Job.Tank ? 1 : 6)),
            "only selected character responds; Mina does not leak into other routes");
        Check(lines.All(line => ResourceLoader.Exists(line.face)), "all dialogue portraits exist");
        if (job == Job.Tank)
            Check(lines[1].face == "res://char/v3/mina_conversation_v1.png",
                "Mina uses the conversation portrait matched to the other characters' drawing style");
        if (id == "akari")
            Check(lines.Where(line => line.who == 2).All(line => line.face == "res://char/v3/akari_mid_conversation_v1.png"),
                "Akari uses her natural-expression midboss costume portrait, never the wide-eyed lit face");
        if (id == "koharu")
            Check(lines.Where(line => line.who == 2).All(line => line.face == "res://char/v3/koharu_mid_conversation_v1.png"),
                "Koharu uses her midboss costume throughout the intro");
        Check(Read<Texture2D>(intro, "_playerPortrait").ResourcePath
            == (job == Job.Magic ? "res://char/v3/rei_gawa.png" : lines[1].face),
            "selected character uses conversation art, not a gameplay sprite");
        Check(Read<Texture2D>(intro, "_enemyPortrait").ResourcePath == lines[0].face,
            "enemy uses the dedicated story portrait, not a battle sprite");
        if (Jobs.Get(job).CharacterId == id)
            Check(Read<Texture2D>(intro, "_enemyPortrait").ResourcePath != Read<Texture2D>(intro, "_playerPortrait").ResourcePath,
                "same-character encounters use visually distinct player and enemy costumes");
        Check(intro.TextureFilter == CanvasItem.TextureFilterEnum.LinearWithMipmaps, "high-resolution portraits use smooth downscaling");
        Check(!Read<bool>(hud, "_cinematicBubble"), "dialogue uses story-film subtitles without duplicate face bubbles");
        if (job == Job.Tank) await Shot($"{id}_post");
        if (id == "akari" && job == Job.Tank)
        {
            var backlog = GetNode<Backlog>("/root/Backlog");
            double time = Read<double>(intro, "_time");
            intro.SetProcess(true);
            backlog.Open();
            await Frames(5);
            Check(Read<double>(intro, "_time") == time, "backlog freezes the introduction");
            typeof(Backlog).GetMethod("Close", Private)!.Invoke(backlog, null);
            intro.SetProcess(false);
        }
        var shown = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < 110 && IsInstanceValid(intro); i++)
        {
            intro._Process(0.1);
            await Frames(3);
            if (!IsInstanceValid(intro)) break;
            int line = Read<int>(intro, "_line");
            if (Read<bool>(intro, "_talking") && line < 3 && Read<double>(intro, "_lineTime") >= 0.4 && shown.Add(line))
            {
                Check(hud.DialogRevealed, "short barks reveal immediately, independent of text speed");
                if (lines[line].who == 2)
                    Check(Read<Texture2D>(intro, "_enemyPortrait").ResourcePath == lines[line].face,
                        "enemy portrait follows the speaking expression");
                if (line <= 1 && (job == Job.Tank || Jobs.Get(job).CharacterId == id))
                    await Shot($"{id}_talk_{job}_{line}");
                if (line == 1 && id == "rei" && job == Job.Tank)
                {
                    foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(540, 960) })
                    {
                        DisplayServer.WindowSetSize(size);
                        await Shot($"rei_talk_{size.X}x{size.Y}");
                    }
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                }
            }
        }
        Check(shown.Count == 3, "all three utterances appear once before combat");
        Check(!IsInstanceValid(intro) && !hud.CinematicMode && !Hud.BubblePaused,
            "short intro finishes automatically even when global auto advance is off");
        Check(world.ProcessMode == ProcessModeEnum.Inherit && _game.ProcessMode != ProcessModeEnum.Disabled, "previous processing modes restored");
        Check(FilmSkip.Seen(_game, $"cameo_intro_{id}_{Jobs.Get(job).CharacterId}"), "seen state is scoped to encounter and player");
        stage._Process(0.1);
        stage._Process(0.1);
        await Frames(2);
        var bosses = GetTree().GetNodesInGroup("enemies").OfType<CameoBoss>().ToArray();
        Check(bosses.Length == 1 && bosses[0].Theme.IntroLines.Length == 0, "exactly one boss spawns without repeating the opening line");
        Check(GetTree().GetFirstNodeInGroup("cameo_intro") == null, "intro does not restart when battle ticks");
        await ShieldLesson(bosses[0], hud, id, job);
        await Clean(root);
    }

    private async Task ShieldLesson(CameoBoss boss, Hud hud, string id, Job job)
    {
        Check(!Hud.BubblePaused, "shield explanation waits for the visible battle entrance");
        await Frames(65);
        boss.SetProcess(false);
        var talk = Read<CharacterStoryTalk>(boss, "_shieldTalk");
        var panels = boss.GetChildren().OfType<Panel>().ToArray();
        Check(talk.Active && panels.Length == 3 && Hud.BubblePaused && !hud.CinematicMode,
            $"{id}/{job}: explanation accompanies the actual shield and its three panels");
        Check(hud.DialogToolbarVisible && hud.HoldBubble, "shield lesson uses the shared dialogue toolbar");
        Vector2 position = boss.GlobalPosition;
        foreach (var panel in panels) panel.WeakenByRipple();
        await Frames(8);
        Check(panels.All(p => IsInstanceValid(p) && p.Invulnerable) && boss.GlobalPosition == position,
            "movement and panel destruction remain blocked during the explanation");
        Check(!Pool.GetChildren().OfType<Bullet>().Any(b => b.Active), "battle cannot fire over the shield explanation");
        boss._Process(5);
        Check(Read<int>(talk, "_line") == 0 && !_game.IsIdleDialogSeen("once_midboss_shield"),
            "first-time explanation waits for input and is not marked complete prematurely");
        var backlog = GetNode<Backlog>("/root/Backlog");
        backlog.Open();
        boss._Process(5);
        Check(Read<int>(talk, "_line") == 0, "backlog blocks shield dialogue progression");
        typeof(Backlog).GetMethod("Close", Private)!.Invoke(backlog, null);
        var lines = Read<(int who, string text, string face)[]>(talk, "_lines");
        Check(lines.Length == 3 && lines[0].text.Contains("本体") && lines[1].text.Contains("砕")
            && lines[1].text.Contains("シールド") && lines[2].text.Contains("張り直"),
            "lesson explains protection, panel destruction and the temporary damage window in order");
        _game.AutoAdvanceDialog = job is Job.Tank or Job.Heal;
        Hud.SkipLatched = job == Job.Magic;
        for (int i = 0; i < lines.Length; i++)
        {
            Check(Read<string>(hud, "_dlgText") == lines[i].text
                && Read<Hud.LineKind>(hud, "_dlgKind") == (job == Job.Tank ? Hud.LineKind.Mina : Hud.LineKind.Companion),
                "selected character delivers each shield instruction");
            Check(Read<System.Collections.Generic.List<string>>(hud, "_dlgPages").Count == 1,
                "instruction fits one page so AUTO and SKIP never stall");
            hud.RevealDialogNow();
            if (job == Job.Tank && i == 0) await Shot($"{id}_shield_intro");
            if (job == Job.Melee) Input.ActionPress("ui_accept");
            boss._Process(1.5);
            Input.ActionRelease("ui_accept");
            boss._Process(0.01);
        }
        Hud.SkipLatched = false;
        _game.AutoAdvanceDialog = false;
        Check(!talk.Active && !Hud.BubblePaused && !hud.HoldBubble && !hud.BattleMemoryTempo
            && panels.All(p => !p.Invulnerable) && _game.IsIdleDialogSeen("once_midboss_shield"),
            "manual, AUTO or SKIP completion restores combat and records the one-time lesson");
        foreach (var panel in panels) panel.Shatter();
        await Frames(18);
        var cue = hud.Bubbles!.ShieldBreak;
        Check(cue.Active && Read<string>(cue, "_line").Contains("シールド")
            && Read<string>(cue, "_line").Contains("本体")
            && Read<string>(cue, "_speaker") == Jobs.Get(job).CharacterName,
            "actual shield break names the shield and tells the player to target the body");
        if (job == Job.Tank)
        {
            await Shot($"{id}_shield_break");
            for (int i = 0; i < 480 && !boss.GetChildren().OfType<Panel>().Any(); i++) await Frames(1);
            Check(boss.GetChildren().OfType<Panel>().Count() == 3 && !Hud.BubblePaused && !talk.Active
                && Read<string>(hud, "_bossLine").Contains("シールドが戻"),
                "regeneration announces the restored shield without replaying the paused lesson");
        }
    }

    private async Task ShieldReplayAndAbort()
    {
        Read<System.Collections.Generic.HashSet<string>>(_game, "_idleDialogSeen").Remove("once_midboss_shield");
        foreach (bool replay in new[] { false, true })
        {
            if (replay) _game.MarkIdleDialogSeen("once_midboss_shield");
            var root = Load("akari", Job.Tank, GameManager.StageEntry.MidBoss);
            var stage = Stage(root, "akari");
            Write(stage, "_cameoIntroDone", true);
            await Frames(65);
            stage.SetProcess(false);
            var boss = GetTree().GetNodesInGroup("enemies").OfType<CameoBoss>().Single();
            var hud = root.GetNode<Hud>("Hud");
            if (replay)
                Check(!Hud.BubblePaused && Read<CharacterStoryTalk?>(boss, "_shieldTalk") == null
                    && Read<string>(hud, "_bossLine").Contains("シールド"),
                    "later encounters use a short shield cue without pausing combat");
            else
            {
                Check(Hud.BubblePaused, "unread lesson starts again after an interrupted encounter");
                boss.QueueFree();
                await Frames(3);
                Check(!Hud.BubblePaused && !hud.HoldBubble && !hud.BattleMemoryTempo
                    && !_game.IsIdleDialogSeen("once_midboss_shield"),
                    "interrupted lesson releases its pause without marking the tutorial complete");
            }
            await Clean(root);
        }
    }

    private async Task Bypass(string id, GameManager.StageEntry entry, GameManager.Diff difficulty)
    {
        if (difficulty == GameManager.Diff.Lunatic)
            Read<System.Collections.Generic.HashSet<string>>(_game, "_idleDialogSeen").Remove("once_midboss_shield");
        var root = Load(id, Job.Tank, entry, difficulty);
        await Frames(difficulty == GameManager.Diff.Lunatic ? 65 : 5);
        Check(GetTree().GetFirstNodeInGroup("cameo_intro") == null, $"{id}/{entry}/{difficulty}: unrelated entry skips new intro");
        if (difficulty == GameManager.Diff.Lunatic)
            Check(!Hud.BubblePaused && !_game.IsIdleDialogSeen("once_midboss_shield")
                && GetTree().GetNodesInGroup("enemies").OfType<CameoBoss>().Count() == 1,
                "Lunatic enters battle immediately without story pause");
        await Clean(root);
    }

    private async Task ChoiceReaction(string id, string key, string chosen)
    {
        _game.RecordChoice(key, chosen, Array.Empty<string>(), 1);
        var root = Load(id, Job.Tank, GameManager.StageEntry.MidBoss);
        await Frames(3);
        var intro = (CameoIntroScene)GetTree().GetFirstNodeInGroup("cameo_intro");
        var lines = Read<(int who, string text, string face)[]>(intro, "_lines");
        Check(lines[0].text.Contains(chosen), "earlier choice still changes boss's opening line");
        Check(!Read<FilmSkip>(intro, "_skip").Available, "unread choice reaction cannot be skipped as an old film");
        await Clean(root);
    }

    private async Task RemovedChoiceReaction(string chosen)
    {
        _game.RecordChoice("s3_2", chosen, Array.Empty<string>(), 1);
        var root = Load("rei", Job.Tank, GameManager.StageEntry.MidBoss);
        await Frames(3);
        var intro = (CameoIntroScene)GetTree().GetFirstNodeInGroup("cameo_intro");
        var lines = Read<(int who, string text, string face)[]>(intro, "_lines");
        Check(lines[0].text == "……だれ？　配信なら、終わったけど。", "Rei no longer quotes a removed choice from old saves");
        await Clean(root);
    }

    private async Task EarlyExit()
    {
        var root = Load("akari", Job.Tank, GameManager.StageEntry.MidBoss);
        await Frames(3);
        var intro = (CameoIntroScene)GetTree().GetFirstNodeInGroup("cameo_intro");
        Check(Read<FilmSkip>(intro, "_skip").Available, "read encounter is skippable on replay");
        Stage(root, "akari").SetProcess(false);
        intro.QueueFree();
        await Frames(3);
        Check(root.GetNode<Node2D>("World").ProcessMode == ProcessModeEnum.Inherit && !Hud.BubblePaused
            && _game.ProcessMode != ProcessModeEnum.Disabled, "interrupted scene releases world, HUD and global clock");
        await Clean(root);
    }

    private async Task RouteEntry(string id)
    {
        var root = Load(id, Job.Melee, GameManager.StageEntry.Start);
        var stage = Stage(root, id);
        var hud = root.GetNode<Hud>("Hud");
        hud.HideBubble();
        hud.HoldBubble = false;
        Write(stage, "_step", id == "akari" ? 2 : id == "koharu" ? 5 : 4);
        stage.SetProcess(false);
        stage.GetType().GetMethod("Advance", Private)!.Invoke(stage, null);
        stage.SetProcess(true);
        await Frames(3);
        Check(GetTree().GetFirstNodeInGroup("cameo_intro") is CameoIntroScene,
            $"{id}: normal route transition also opens the introduction");
        await Clean(root);
    }

    private async Task ReplaySkip()
    {
        var root = Load("akari", Job.Tank, GameManager.StageEntry.MidBoss);
        await Frames(3);
        var intro = (CameoIntroScene)GetTree().GetFirstNodeInGroup("cameo_intro");
        intro.SetProcess(false);
        Stage(root, "akari").SetProcess(false);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = true });
        Input.FlushBufferedEvents();
        intro._Process(0.5);
        intro._Process(0.5);
        await Frames(3);
        Check(IsInstanceValid(intro) && root.GetNode<Node2D>("World").ProcessMode == ProcessModeEnum.Disabled,
            "held skip cannot leak a bomb into the resumed battle");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, Pressed = false });
        Input.FlushBufferedEvents();
        intro._Process(0.1);
        await Frames(3);
        Check(!IsInstanceValid(intro) && !Hud.BubblePaused, "releasing skip completes the handoff");
        await Clean(root);
    }

    private async Task ManualAdvance()
    {
        var root = Load("akari", Job.Tank, GameManager.StageEntry.MidBoss);
        await Frames(3);
        var intro = (CameoIntroScene)GetTree().GetFirstNodeInGroup("cameo_intro");
        intro.SetProcess(false);
        Stage(root, "akari").SetProcess(false);
        intro._Process(0.5);
        intro._Process(0.7);
        Input.ActionPress("ui_accept");
        intro._Process(0.1);
        Check(Read<bool>(intro, "_postClosing"), "fresh advance can dismiss the post early");
        Input.ActionRelease("ui_accept");
        await Frames(20);
        intro._Process(0.1);
        Check(Read<bool>(intro, "_talking"), "post dismiss hands off to the two-character scene");
        for (int line = 0; line < 3; line++)
        {
            intro._Process(0.5);
            Check(Read<int>(intro, "_line") == line, "manual input advances one utterance at a time");
            Input.ActionPress("ui_accept");
            intro._Process(0.1);
            intro._Process(0.1);
            Check(Read<int>(intro, "_line") == line + 1, "held confirm does not double-advance");
            Input.ActionRelease("ui_accept");
            intro._Process(0.05);
        }
        intro._Process(0.5);
        await Frames(3);
        Check(!IsInstanceValid(intro) && !Hud.BubblePaused, "manual advance releases the battle normally");
        await Clean(root);
    }

    private async Task RealtimeCadence()
    {
        _game.MsgCharsPerSec = 1;
        var root = Load("akari", Job.Tank, GameManager.StageEntry.MidBoss);
        await Frames(3);
        var intro = (CameoIntroScene)GetTree().GetFirstNodeInGroup("cameo_intro");
        Stage(root, "akari").SetProcess(false);
        int frames = 0;
        while (IsInstanceValid(intro) && frames < 660) { await Frames(1); frames++; }
        Check(!IsInstanceValid(intro) && frames > 360 && frames < 600,
            $"real-time intro lasts {frames / 60f:F2}s with auto advance off and slowest text speed");
        _game.MsgCharsPerSec = 300;
        await Clean(root);
    }

    private async Task Clean(Node root)
    {
        _game.AutoAdvanceDialog = false;
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
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/cameo_intro/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await Frames(5);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
