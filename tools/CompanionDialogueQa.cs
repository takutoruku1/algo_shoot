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
            bool menusOnly = Array.IndexOf(OS.GetCmdlineUserArgs(), "--menus-only") >= 0;
            if (!menusOnly) CheckScripts();
            foreach (var job in Jobs.All)
            {
                game.SelectedJob = job.Id;
                if (!menusOnly) await CheckStages(job);
                if (job.Id == Job.Tank) continue;
                await CheckMenus(job);
            }
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
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
            // 他ジョブ潜行の専用ストーリー（CharacterStory・2026-09-15）：全章×全ビート＋改心相当9通りに
            // ミナの声（who=1/3）が一切無いこと、[仮] プレースホルダへ到達しないこと（＝全アーム執筆済み）、
            // 改心の BGM 停止行（ト書き）が本文の範囲内を指すこと、を機械検査する。
            if (job.Id != Job.Tank)
            {
                for (int ch = 1; ch <= CharacterStory.LoopChapter; ch++)
                    foreach (var beat in Enum.GetValues<CharacterStory.Beat>())
                    {
                        var story = CharacterStory.Lines(job.Id, ch, beat);
                        Check(story.Length > 0 && story.All(l => l.who != 1 && l.who != 3)
                            && story.All(l => !string.IsNullOrWhiteSpace(l.text)), $"{job.CharacterId} ch{ch}/{beat} has no Mina voice");
                        Check(story.All(l => !l.text.StartsWith("[仮]")), $"{job.CharacterId} ch{ch}/{beat} is authored (no placeholder)");
                        count += story.Length;
                    }
                foreach (string stage in new[] { "akari", "koharu", "rei" })
                {
                    var story = CharacterStory.Redemption(job.Id, stage);
                    Check(story.Length > 0 && story.All(l => l.who != 1 && l.who != 3), $"{job.CharacterId} redemption@{stage} has no Mina voice");
                    Check(story.All(l => !l.text.StartsWith("[仮]")), $"{job.CharacterId} redemption@{stage} is authored (no placeholder)");
                    int silence = CharacterStory.RedemptionSilenceAt(job.Id, stage);
                    Check(silence >= 0 && silence < story.Length, $"{job.CharacterId} redemption@{stage} BGM-stop line is in range");
                    count += story.Length;
                }
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
        GD.Print($"[CompanionQA] {count} story/menu lines, plus the tutorial scene");
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
            // フィールド→期待値の対応（2026-09-15 他ジョブ潜行リワーク）：
            //   結び手＝本編そのまま（beat=null）。他ジョブ＝CharacterStory のビートへ全面置換。
            //   beat=null のままの欄はボス本人の口上（who=2）＝どちらのモードでも本編を流す欄。
            var fields = job.Id == Job.Tank
                ? new (string field, string original, CharacterStory.Beat? beat)[]
                    { ("_playerIntro", "Intro", null), ("_playerMid", mid, null), ("_playerBoss", "BossIntro", null) }
                : stageId switch
                {
                    "akari" => new (string, string, CharacterStory.Beat?)[]
                        { ("_playerIntro", "Intro", CharacterStory.Beat.Sortie), ("_playerMid", mid, CharacterStory.Beat.PreBoss), ("_playerBoss", "BossIntro", null) },
                    "koharu" => new (string, string, CharacterStory.Beat?)[]
                        { ("_playerIntro", "Intro", CharacterStory.Beat.Sortie), ("_playerMid", mid, CharacterStory.Beat.Mid2), ("_playerBoss", "BossIntro", CharacterStory.Beat.PreBoss) },
                    _ => new (string, string, CharacterStory.Beat?)[]
                        { ("_playerIntro", "Intro", CharacterStory.Beat.Sortie), ("_playerMid", mid, CharacterStory.Beat.Mid2), ("_playerBoss", "BossIntro", null) },
                };
            foreach (var (field, original, beat) in fields)
            {
                var lines = Read<(int who, string text, string face)[]>(stage, field);
                var baseline = Data<(int who, string text, string face)[]>(stage.GetType(), original);
                if (beat == null)
                    Check(lines.SequenceEqual(baseline), $"{job.CharacterId} {stageId}/{original} keeps canon lines");
                else
                    Check(lines.SequenceEqual(CharacterStory.Lines(job.Id, 1, beat.Value)), $"{job.CharacterId} {stageId}/{beat} uses the character story");
                if (job.Id != Job.Tank)
                    Check(lines.All(l => l.who != 1 && l.who != 3), $"{job.CharacterId} {stageId}/{field} has no Mina voice");
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
                        // 話者名は素の名前（「（同行）」を付けない＝2026-09-15 仕様）。
                        Check(Read<string>(hud, "_dlgSpeaker") == job.CharacterName, "companion label is the bare character name");
                        // 立ち絵：face 指定行はその表情差分、空欄はジョブの立ち絵（spin）へ落ちる（scenario 実装メモ2項）。
                        string expected = string.IsNullOrEmpty(lines[i].face) ? CompanionDialogue.Portrait(job.Id) : lines[i].face;
                        Check(Read<Texture2D>(hud, "_dlgPortrait").ResourcePath == expected, "companion portrait follows the face token");
                        // 同一話者の表情差し替え行はクロスフェードが正しい挙動なので、重ね合わせ検査から除外する。
                        string? prevFace = i > 0 && lines[i - 1].who == 6
                            ? (string.IsNullOrEmpty(lines[i - 1].face) ? CompanionDialogue.Portrait(job.Id) : lines[i - 1].face) : null;
                        if (prevFace == null || prevFace == expected)
                            Check(Read<Texture2D?>(hud, "_dlgPortraitPrev") == null && Read<double>(hud, "_portraitFadeT") <= 0,
                                "speaker changes never superimpose two characters");
                        Check(Hud.Backlog[^1].Speaker == job.CharacterName, "backlog has the right speaker");
                        if (field == "_playerIntro" && stageId == job.CharacterId && i == lines.Length - 1)
                        {
                            await Shot($"{job.CharacterId}_own_stage");
                            DisplayServer.WindowSetSize(new Vector2I(960, 540));
                            await Frames(3);
                            await Shot($"{job.CharacterId}_own_stage_small");
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
            if (path == "Stage0.tscn")
                Check(lines.Count(l => l.who == 6) == (job.Id == Job.Tank ? 0 : 2), $"{job.CharacterId} {path} selects the correct introduction");
            else
                // FINAL はジョブに関わらず常にミナ本編（2026-09-15：CompanionDialogue.Final の置換を廃止）。
                Check(lines.All(l => l.who != 6) && lines.Any(l => l.text.Contains("あなたの光")),
                    $"{job.CharacterId} final keeps the Mina-canon introduction");
            await RemoveScene(root);
        }
    }

    private async Task CheckMenus(JobTuning job)
    {
        var game = GetNode<GameManager>("/root/Game");
        game.ResetPersistent();
        game.AutoSaveEnabled = false;
        game.SelectedJob = Job.Tank;
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        Read<HashSet<string>>(game, "_cleared").Add(job.UnlockStageId);
        await Frames(30);
        Check(Read<object>(hub, "_mode").ToString() == "Home", "first visit starts on the phone home");
        await Press(Key.Z);
        await Frames(45);
        Check(Read<object>(hub, "_mode").ToString() == "Cards", "rescued characters enter SNS through its home app");
        long followers = game.Followers, impression = game.Impression;
        await Press(Key.J);
        Check(Read<object>(hub, "_mode").ToString() == "Job", "keyboard opens character selection");
        var accounts = Read<JobTuning[]>(hub, "_jobChoices");
        Check(accounts.Length == 2 && accounts[0].Id == Job.Tank && accounts[1].Id == job.Id,
            "only Mina and the rescued character are listed");
        Check(accounts[Read<int>(hub, "_jobSel")].Id == Job.Tank && !Pad.UsingMouse,
            "keyboard shortcut keeps the selected character instead of following the mouse");
        int accountIndex = Array.FindIndex(accounts, entry => entry.Id == job.Id);
        for (int i = 0; i < accountIndex; i++) await Press(Key.Down);
        Check(accounts[Read<int>(hub, "_jobSel")].Id == job.Id, "keyboard selects a different account before switching");
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
        game.SelectedJob = Job.Tank;
        Call(hub, "OpenDetail");
        Write(hub, "_tierSel", (int)GameManager.Diff.Hard);
        int selectedStage = Read<int>(hub, "_sel");
        await Frames(12);
        await Press(Key.J);
        Check(Read<object>(hub, "_mode").ToString() == "Job", "detail shortcut opens character selection");
        for (int i = 0; i < accountIndex; i++) await Press(Key.Down);
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
        // ホーム初公開の演出（once_phone_home・1面初クリアの帰還時に一度だけ入るシネマ）を既読化する。
        //   未読のままだと帰還会話の戻り先が Home ではなく HomeReveal になり、下の Home 判定が偽陽性で落ちる。
        game.MarkIdleDialogSeen("once_phone_home");
        hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        Check(Read<(string, string)[]>(hub, "_dlg").TakeLast(3).SequenceEqual(CompanionDialogue.MenuLines(job.Id, CompanionDialogue.Menu.Return)),
            "stage-clear return appends the selected companion exchange");
        Check(game.JustClearedStageId == null, "stage-clear return is consumed once");
        long expectedImpression = (long)Mathf.Round(40 * game.TotalImpressionMul * game.ReplayMul * GameManager.MoneyGainMul);
        await ReadHubDialogue(hub, $"{job.CharacterId}_return");
        await Frames(145);
        Check(Read<object>(hub, "_mode").ToString() == "Home", "stage-clear conversation returns home");
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
