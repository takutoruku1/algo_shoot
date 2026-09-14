using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class CompanionDialogueQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static T Data<T>(Type type, string name) => (T)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        GD.Print($"[CompanionQA] PASS {text}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            CheckScripts();
            foreach (var job in Jobs.All)
            {
                game.SelectedJob = job.Id;
                await CheckStages(job);
                if (job.Id == Job.Tank) continue;
                await CheckMenus(job);
            }
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[CompanionQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[CompanionQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static void CheckScripts()
    {
        int count = 0;
        foreach (var job in Jobs.All)
        {
            foreach (string stage in new[] { "akari", "koharu", "rei" })
            foreach (var beat in Enum.GetValues<CompanionDialogue.Beat>())
            {
                var lines = CompanionDialogue.Stage(job.Id, stage, beat);
                if (job.Id == Job.Tank) { Check(lines.Length == 0, $"Mina {stage}/{beat} unchanged"); continue; }
                Check(lines.Length == 3 && lines.Any(l => l.who == 1) && lines.Any(l => l.who == 6), $"{job.CharacterId} {stage}/{beat} exchange");
                Check(lines.All(l => !string.IsNullOrWhiteSpace(l.text)), "no empty dialogue");
                count += lines.Length;
            }
            foreach (var scene in Enum.GetValues<CompanionDialogue.Menu>())
            {
                var lines = CompanionDialogue.MenuLines(job.Id, scene);
                if (job.Id == Job.Tank) { Check(lines.Length == 0, $"Mina {scene} unchanged"); continue; }
                Check(lines.Any(l => l.speaker == "ミナ") && lines.Any(l => l.speaker == job.CharacterName), $"{job.CharacterId} {scene} speakers");
                count += lines.Length;
                string text = CompanionDialogue.MenuText(job.Id, scene);
                if (scene is CompanionDialogue.Menu.TrainEnter or CompanionDialogue.Menu.TrainShoot or CompanionDialogue.Menu.TrainIdle)
                    Check(UiKit.WrapLines(UiKit.Zen, text, 13, 312).Count <= 3, $"{job.CharacterId} {scene} fits training bubble");
                if (scene is CompanionDialogue.Menu.ShopEnter or CompanionDialogue.Menu.ShopBuy or CompanionDialogue.Menu.ShopExit)
                    Check(UiKit.WrapLines(UiKit.ZenBold, text, 16, 548).Count <= 3, $"{job.CharacterId} {scene} fits below shop details");
            }
        }
        GD.Print($"[CompanionQA] {count} stage/menu lines, plus tutorial and final scenes");
    }

    private async Task CheckStages(JobTuning job)
    {
        foreach (var (path, stageId, mid) in new[] { ("Akari.tscn", "akari", "MidEnd"), ("Koharu.tscn", "koharu", "ClassTalk"), ("Rei.tscn", "rei", "BossTalk") })
        {
            var root = GD.Load<PackedScene>($"res://{path}").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var stage = root.GetChildren().OfType<Node>().Single(n => n.GetType().Name == "Stage" + char.ToUpper(stageId[0]) + stageId[1..]);
            stage.SetProcess(false);
            var hud = root.GetNode<Hud>("Hud");
            if (job.Id == Job.Tank && stageId == "akari")
            {
                hud.ShowDialog(Hud.LineKind.Mina, "first", "res://char/mina_face.png");
                hud.ShowDialog(Hud.LineKind.Mina, "second", "res://char/mina_worried.png");
                Check(Read<double>(hud, "_portraitFadeT") > 0 && Read<Texture2D?>(hud, "_dlgPortraitPrev") != null,
                    "same-speaker expression changes still crossfade");
                hud.HideBubble();
            }
            foreach (var (field, original) in new[] { ("_playerIntro", "Intro"), ("_playerMid", mid), ("_playerBoss", "BossIntro") })
            {
                var lines = Read<(int who, string text, string face)[]>(stage, field);
                var baseline = Data<(int who, string text, string face)[]>(stage.GetType(), original);
                Check(lines.Take(baseline.Length).SequenceEqual(baseline), $"{job.CharacterId} {stageId}/{original} keeps original lines");
                Check(lines.Length == baseline.Length + (job.Id == Job.Tank ? 0 : 3), "adds exactly one exchange");
                Write(stage, "_stepStarted", false);
                if (field == "_playerBoss") Call(stage, "Step_BossSpawn");
                Write(stage, "_zEdge", false);
                int step = Read<int>(stage, "_step");
                Call(stage, "Step_Lines", 0d, lines);
                for (int i = 0; i < lines.Length; i++)
                {
                    await Frames(2);
                    Check(Hud.BubblePaused, "dialogue pauses combat");
                    Check(Read<int>(stage, "_introLine") == i, "line order is stable");
                    for (int page = 0; !hud.DialogRevealed && page < 20; page++) hud.RevealDialogNow();
                    Check(hud.DialogRevealed, "dialogue pages can be revealed");
                    if (lines[i].who == 6)
                    {
                        Check(Read<string>(hud, "_dlgSpeaker") == $"{job.CharacterName}（同行）", "companion label differs from boss");
                        Check(Read<Texture2D>(hud, "_dlgPortrait").ResourcePath == CompanionDialogue.Portrait(job.Id), "companion uses its player portrait");
                        Check(Read<Texture2D?>(hud, "_dlgPortraitPrev") == null && Read<double>(hud, "_portraitFadeT") <= 0,
                            "speaker changes never superimpose two characters");
                        Check(Hud.Backlog[^1].Speaker == $"{job.CharacterName}（同行）", "backlog has the right speaker");
                        if (field == "_playerBoss" && stageId == job.CharacterId && i == lines.Length - 1)
                        {
                            await Shot($"{job.CharacterId}_own_boss");
                            DisplayServer.WindowSetSize(new Vector2I(960, 540));
                            await Frames(3);
                            await Shot($"{job.CharacterId}_own_boss_small");
                            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                        }
                    }
                    Write(stage, "_lineHold", 1d);
                    Write(stage, "_zEdge", true);
                    Call(stage, "Step_Lines", 0d, lines);
                }
                await Frames(2);
                Check(Read<int>(stage, "_step") == step + 1 && !Hud.BubblePaused, "combat resumes after the exchange");
            }
            await RemoveScene(root);
        }
        foreach (string path in new[] { "Stage0.tscn", "MinaBattle.tscn" })
        {
            var root = GD.Load<PackedScene>($"res://{path}").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var stage = root.GetChildren().OfType<Node>().Single(n => n is StageZero || n is StageMina);
            stage.SetProcess(false);
            var lines = Read<(int who, string text, string face)[]>(stage, path == "Stage0.tscn" ? "_playerIntro" : "_intro");
            Check(lines.Count(l => l.who == 6) == (job.Id == Job.Tank ? 0 : 2), $"{job.CharacterId} {path} selects the correct introduction");
            if (path == "MinaBattle.tscn" && job.Id != Job.Tank)
                Check(lines.Any(l => l.text.Contains($"同行する声：{job.CharacterName}"))
                    && !lines.Any(l => l.text.Contains("あなたの光") || l.text.Contains("小さな光が動く")), "final explains the playable companion instead of an orb");
            await RemoveScene(root);
        }
    }

    private async Task CheckMenus(JobTuning job)
    {
        var game = GetNode<GameManager>("/root/Game");
        game.ResetPersistent();
        game.AutoSaveEnabled = false;
        game.SelectedJob = job.Id;
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        Read<HashSet<string>>(game, "_cleared").Add(job.UnlockStageId);
        await Frames(30);
        long followers = game.Followers, impression = game.Impression;
        await Press(Key.J);
        Check(Read<object>(hub, "_mode").ToString() == "Job", "keyboard opens character selection");
        Check(Jobs.All[Read<int>(hub, "_jobSel")].Id == job.Id && !Pad.UsingMouse,
            "keyboard shortcut keeps the selected character instead of following the mouse");
        Write(hub, "_jobT", 1d);
        await Press(Key.Z);
        Check(Read<object>(hub, "_mode").ToString() == "Dialogue", "first character selection starts its conversation");
        Check(Read<double>(hub, "_toastT") <= 0, "selection toast does not cover the conversation");
        Check(Read<(string, string)[]>(hub, "_dlg").SequenceEqual(CompanionDialogue.MenuLines(job.Id, CompanionDialogue.Menu.Select)), "selection uses the selected character script");
        await ReadHubDialogue(hub, $"{job.CharacterId}_select");
        Check(Read<object>(hub, "_mode").ToString() == "Cards" && game.Followers == followers && game.Impression == impression,
            "selection returns to cards without posting or rewards");
        game.ResetIdleDialogSeen();
        Check(game.IsIdleDialogSeen($"once_companion_select_{job.CharacterId}"), "selection greeting survives small-talk pool resets");
        await Press(Key.J);
        Write(hub, "_jobT", 1d);
        await Press(Key.Z);
        Check(Read<object>(hub, "_mode").ToString() == "Cards", "repeat selection does not force another greeting");
        Read<HashSet<string>>(game, "_idleDialogSeen").Remove($"once_companion_select_{job.CharacterId}");
        Call(hub, "OpenDetail");
        Write(hub, "_tierSel", (int)GameManager.Diff.Hard);
        int selectedStage = Read<int>(hub, "_sel");
        await Frames(12);
        await Press(Key.J);
        Check(Read<object>(hub, "_mode").ToString() == "Job", "detail shortcut opens character selection");
        Write(hub, "_jobT", 1d);
        await Press(Key.Z);
        Check(Read<object>(hub, "_mode").ToString() == "Dialogue", "first selection from detail starts its conversation");
        await ReadHubDialogue(hub, $"{job.CharacterId}_select_detail");
        Check(Read<object>(hub, "_mode").ToString() == "Detail" && Read<int>(hub, "_sel") == selectedStage
            && Read<int>(hub, "_tierSel") == (int)GameManager.Diff.Hard && !Read<bool>(hub, "_dived"),
            "greeting returns to the same stage and difficulty without starting combat");
        await Press(Key.X);
        var face = ((Texture2D? face, Color col, float top))Call(hub, "SpeakerFace", job.CharacterName)!;
        Check(face.face?.ResourcePath == CompanionDialogue.Portrait(job.Id), "menu portrait matches playable character");
        Read<HashSet<string>>(game, "_cleared").Add("akari");
        Call(hub, "TryStartIdleSmallTalk");
        Check(Read<(string, string)[]>(hub, "_dlg").SequenceEqual(CompanionDialogue.MenuLines(job.Id, CompanionDialogue.Menu.Hub)), "hub prefers the companion's unread conversation");
        await ReadHubDialogue(hub, $"{job.CharacterId}_hub");
        Check(game.Followers == followers && game.Impression == impression, "private hub conversation grants no rewards");
        await RemoveScene(hub);

        game.JustClearedStageId = "akari";
        hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        Check(Read<(string, string)[]>(hub, "_dlg").TakeLast(3).SequenceEqual(CompanionDialogue.MenuLines(job.Id, CompanionDialogue.Menu.Return)),
            "stage-clear return appends the selected companion exchange");
        Check(game.JustClearedStageId == null, "stage-clear return is consumed once");
        long expectedImpression = (long)Mathf.Round(40 * game.TotalImpressionMul * game.ReplayMul * GameManager.MoneyGainMul);
        await ReadHubDialogue(hub, $"{job.CharacterId}_return");
        Check(game.Followers == followers + 8 && game.Impression == impression + expectedImpression,
            "return dialogue keeps the existing single post reward");
        await RemoveScene(hub);

        var shop = GD.Load<PackedScene>("res://Shop.tscn").Instantiate<Shop>();
        GetTree().Root.AddChild(shop);
        GetTree().CurrentScene = shop;
        Call(shop, "ShowShopTalk", CompanionDialogue.Menu.ShopEnter, new[] { "ミナ" });
        Check(Read<string>(shop, "_toast") == CompanionDialogue.MenuText(job.Id, CompanionDialogue.Menu.ShopEnter), "shop entrance uses a two-speaker exchange");
        Check(Read<Texture2D>(shop, "_playerShot").ResourcePath == job.PlayerTexturePath, "shop preview uses selected character");
        await Frames(8);
        await Shot($"{job.CharacterId}_shop");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Frames(3);
        await Shot($"{job.CharacterId}_shop_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        foreach (var scene in new[] { CompanionDialogue.Menu.ShopBuy, CompanionDialogue.Menu.ShopExit })
        {
            Call(shop, "ShowShopTalk", scene, new[] { "ミナ" });
            Check(Read<string>(shop, "_toast") == CompanionDialogue.MenuText(job.Id, scene), $"{scene} keeps both speakers");
        }
        await RemoveScene(shop);

        var training = new TrainingRoot();
        GetTree().Root.AddChild(training);
        GetTree().CurrentScene = training;
        Check(Read<string>(training, "_talk") == CompanionDialogue.MenuText(job.Id, CompanionDialogue.Menu.TrainEnter), "training entrance selects the character dialogue");
        await Frames(3);
        await Shot($"{job.CharacterId}_training");
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        await Frames(3);
        await Shot($"{job.CharacterId}_training_small");
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        foreach (var scene in new[] { CompanionDialogue.Menu.TrainShoot, CompanionDialogue.Menu.TrainIdle })
        {
            Call(training, "ShowTrainingTalk", scene, new[] { "ミナ" });
            Check(Read<string>(training, "_talk") == CompanionDialogue.MenuText(job.Id, scene), $"{scene} keeps both speakers");
        }
        await RemoveScene(training);
    }

    private async Task ReadHubDialogue(Hub hub, string shot)
    {
        bool captured = false;
        for (int tries = 0; Read<object>(hub, "_mode").ToString() == "Dialogue" && tries < 50; tries++)
        {
            var lines = Read<(string sp, string tx)[]>(hub, "_dlg");
            int index = Read<int>(hub, "_dlgIdx");
            if (!captured && lines[index].sp != "ミナ")
            {
                Write(hub, "_dlgReveal", 10000f);
                await Frames(3);
                await Shot(shot);
                captured = true;
            }
            await Press(Key.Z);
        }
        Check(Read<object>(hub, "_mode").ToString() != "Dialogue", "hub conversation can be completed with confirm");
    }

    private async Task Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        await Frames(12);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        await Frames(12);
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task RemoveScene(Node scene)
    {
        scene.QueueFree();
        await Frames(5);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Hud.BubblePaused = false;
    }
    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/companion/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
