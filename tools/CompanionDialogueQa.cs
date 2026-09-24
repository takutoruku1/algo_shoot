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
            if (OS.GetCmdlineUserArgs().Contains("--portraits-only")) await CheckPortraits(game);
            else if (OS.GetCmdlineUserArgs().Contains("--timers-only")) await CheckStageTimers(game);
            else
            {
                bool menusOnly = Array.IndexOf(OS.GetCmdlineUserArgs(), "--menus-only") >= 0;
                if (!menusOnly) CheckScripts();
                foreach (var job in Jobs.All)
                {
                    game.SelectedJob = job.Id;
                    if (!menusOnly) await CheckStages(job);
                    if (job.Id == Job.Tank) continue;
                    await CheckMenus(job);
                }
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
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task CheckPortraits(GameManager game)
    {
        var portraits = new HashSet<string>(Jobs.All.Select(job => CompanionDialogue.Portrait(job.Id)));
        void Collect((int who, string text, string face)[] lines)
        {
            foreach (var line in lines)
                if (!string.IsNullOrEmpty(line.face)) portraits.Add(line.face);
        }
        foreach (var type in new[] { typeof(StageZero), typeof(StageAkari), typeof(StageKoharu), typeof(StageRei),
            typeof(StageMina), typeof(BossAkari), typeof(BossKoharu), typeof(BossRei), typeof(BossMina) })
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                if (field.GetValue(null) is (int, string, string)[] lines) Collect(lines);
        foreach (var job in Jobs.All.Where(job => job.Id != Job.Tank))
        {
            for (int chapter = 1; chapter <= CharacterStory.LoopChapter; chapter++)
                foreach (var beat in Enum.GetValues<CharacterStory.Beat>())
                    Collect(CharacterStory.Lines(job.Id, chapter, beat));
            foreach (string stage in new[] { "akari", "koharu", "rei" })
            {
                Collect(CharacterStory.Redemption(job.Id, stage));
                Collect(CharacterStory.Memory(job.Id, stage));
                Collect(CharacterStory.Aftermath(job.Id, stage));
            }
        }
        foreach (string path in portraits)
        {
            Check(!path.Contains("/player/") && !path.Contains("body") && !path.Contains("cutout")
                && !path.EndsWith("rei_gawa.png") && !path.EndsWith("rei_gawa_b.png"), $"dedicated dialogue portrait: {path}");
            Check(GD.Load<Texture2D>(path) != null, $"portrait imports: {path}");
        }
        using (var image = GD.Load<Texture2D>(CompanionDialogue.ReiAvatarPortrait).GetImage())
        {
            Check(image.GetPixel(0, 0).A == 0 && image.GetPixel(image.GetWidth() - 1, 0).A == 0,
                "Rei avatar has a transparent background");
            Check(image.GetPixel(image.GetWidth() / 2, image.GetHeight() / 3).A > 0.95f,
                "Rei avatar face remains opaque");
        }
        foreach (var job in Jobs.All)
        {
            game.SelectedJob = job.Id;
            var root = GD.Load<PackedScene>("res://Rei.tscn").Instantiate<ReiRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.Stage.SetProcess(false);
            var hud = root.Hud;
            hud.HoldBubble = true;
            Write(root.Stage, "_stepStarted", false);
            Call(root.Stage, "Step_BossSpawn");
            Write(root.Stage, "_introLine", 0);
            Call(root.Stage, "ShowLine", (object)Data<(int, string, string)[]>(typeof(StageRei), "BossIntro"));
            Check(Read<Texture2D>(hud, "_dlgPortrait").ResourcePath == CompanionDialogue.ReiAvatarPortrait,
                $"{job.Id}: boss introduction uses the avatar portrait");
            var boss = root.World.GetChildren().OfType<BossRei>().Single();
            await Frames(90);
            hud.RevealDialogNow();
            await Shot($"rei_boss_portrait_{job.Id}");
            if (job.Id == Job.Tank)
            {
                DisplayServer.WindowSetSize(new Vector2I(960, 540));
                await Frames(4);
                await Shot("rei_boss_portrait_small");
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            var lines = Read<(int who, string text, string face)[]>(boss, "_lines");
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].who != 2 && lines[i].who != 6) continue;
                Write(boss, "_line", i);
                Call(boss, "ShowLine");
                string expected = !string.IsNullOrEmpty(lines[i].face) ? lines[i].face
                    : lines[i].who == 2 ? CompanionDialogue.ReiAvatarPortrait : CompanionDialogue.Portrait(job.Id);
                Check(Read<Texture2D>(hud, "_dlgPortrait").ResourcePath == expected,
                    $"{job.Id}: redemption line {i} preserves avatar/person expressions");
            }
            hud.ShowDialog(Hud.LineKind.Companion, "Portrait check");
            Check(Read<Texture2D>(hud, "_dlgPortrait").ResourcePath == CompanionDialogue.Portrait(job.Id),
                $"{job.Id}: unspecified companion portrait is a bust");
            Check(Read<Dictionary<Job, Texture2D>>(hud, "_accountFaces")[job.Id].ResourcePath == CompanionDialogue.AccountPortrait(job.Id),
                $"{job.Id}: account icon remains unchanged");
            await RemoveScene(root);
        }
        foreach (var job in Jobs.All)
        {
            game.SelectedJob = job.Id;
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            hub.SetProcess(false);
            var face = ((Texture2D? face, Color col, float top))Call(hub, "SpeakerFace", job.CharacterName)!;
            Check(face.face?.ResourcePath == CompanionDialogue.Portrait(job.Id), $"{job.Id}: menu uses a dialogue portrait");
            Check(Read<Dictionary<string, Texture2D>>(hub, "_playerFaces")[job.CharacterId].ResourcePath == CompanionDialogue.AccountPortrait(job.Id),
                $"{job.Id}: menu account icon remains unchanged");
            if (job.Id != Job.Tank)
            {
                var lines = CompanionDialogue.MenuLines(job.Id, CompanionDialogue.Menu.Select);
                Call(hub, "StartDialogue", lines, null, true, Read<object>(hub, "_mode"), null);
                Write(hub, "_dlgIdx", Array.FindIndex(lines, line => line.speaker == job.CharacterName));
                Call(hub, "DlgEnsurePages");
                Write(hub, "_dlgReveal", 10000f);
                hub.QueueRedraw();
                await Frames(3);
                await Shot($"{job.CharacterId}_menu_portrait");
            }
            await RemoveScene(hub);
        }
    }

    private async Task CheckStageTimers(GameManager game)
    {
        foreach (var job in Jobs.All)
        foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
        {
            game.SelectedJob = job.Id;
            var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
            stage.SetProcess(false);
            var hud = root.GetNode<Hud>("Hud");
            var world = root.GetNode<Node2D>("World");
            Write(stage, "_startBannerShown", true);
            Write(stage, "_stepStarted", false);
            var intro = Read<(int, string, string)[]>(stage, scene == "MinaBattle" ? "_intro" : "_playerIntro");
            Call(stage, "Step_Lines", 0d, intro);
            world.ProcessMode = ProcessModeEnum.Disabled;
            double elapsed = Read<double>(stage, "_stageElapsed");
            stage._Process(30d);
            Check(Hud.BubblePaused && Read<double>(stage, "_stageElapsed") == elapsed
                && Read<float>(hud, "_elapsed") == (float)elapsed, $"{scene}/{job.Id}: reading time is excluded from HUD and clear time");
            Check(Read<double>(stage, "_lineHold") >= 30d, "dialogue input timing still advances");
            for (int page = 0; !hud.DialogRevealed && page < 20; page++) hud.RevealDialogNow();
            Check(hud.DialogRevealed, "dialogue can finish revealing while the clock is stopped");
            stage.SetProcess(true);
            await Press(Key.Z);
            stage.SetProcess(false);
            Check(Read<int>(stage, "_introLine") == 1 && Read<double>(stage, "_stageElapsed") == elapsed,
                "confirm advances the conversation without advancing the clock");
            hud.HoldBubble = false;
            hud.HideBubble();
            Write(stage, "_step", 0);
            stage._Process(0.75d);
            elapsed += 0.75d;
            Check(Read<double>(stage, "_stageElapsed") == elapsed && Read<float>(hud, "_elapsed") == (float)elapsed,
                "combat resumes the same clock without adding the reading time");

            hud.HoldBubble = true;
            hud.ShowDialog(Hud.LineKind.Other, "Waiting for a reply", otherName: "Timer QA");
            stage._Process(60d);
            Check(Read<double>(stage, "_stageElapsed") == elapsed, "held dialogue and choice waiting do not count");
            hud.HoldBubble = false;
            hud.HideBubble();
            hud.ShowMessage("Timed conversation");
            stage._Process(3d);
            Check(Read<double>(stage, "_stageElapsed") == elapsed, "auto-closing messages also stop the clock");
            hud._Process(5d);
            stage._Process(0.25d);
            elapsed += 0.25d;
            Check(!Hud.BubblePaused && Read<double>(stage, "_stageElapsed") == elapsed, "auto-close resumes time immediately");

            hud.SetCinematicMode(true);
            stage._Process(20d);
            Check(Read<double>(stage, "_stageElapsed") == elapsed, "flashback time remains excluded");
            hud.SetCinematicMode(false);
            hud.ShowBossLine("Rei", "Combat continues", Colors.Gold, 2d);
            stage._Process(0.5d);
            elapsed += 0.5d;
            Check(!Hud.BubblePaused && Read<double>(stage, "_stageElapsed") == elapsed, "nonblocking battle callouts still count as play time");

            GetTree().Paused = true;
            stage.SetProcess(true);
            await Frames(10);
            Check(Read<double>(stage, "_stageElapsed") == elapsed, "pause-menu time remains excluded");
            GetTree().Paused = false;
            await Frames(5);
            stage.SetProcess(false);
            Check(Read<double>(stage, "_stageElapsed") > elapsed, "engine processing resumes the clock after pause");
            elapsed = Read<double>(stage, "_stageElapsed");
            Write(stage, "_clearing", true);
            stage._Process(10d);
            Check(Read<double>(stage, "_stageElapsed") == elapsed && Read<float>(hud, "_elapsed") == (float)elapsed,
                "finished runs retain their fixed clear time");
            await RemoveScene(root);
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
                    // 回想（memory）とアフター（aftermath）の9通り。2026-09-23 に一枚絵（StoryFilm）をやめて
                    //   吹き出しだけになった枚。who は 6（潜行キャラ本人）／2（相手ボス）／4（Ｘ投稿）のみで、
                    //   ミナの声（who=1/3）も「あなた」（0）も中継（5）も一行も無いことを、改心と同形で検める。
                    foreach (var (kind, scene) in new[] { ("memory", CharacterStory.Memory(job.Id, stage)),
                                                          ("aftermath", CharacterStory.Aftermath(job.Id, stage)) })
                    {
                        Check(scene.Length > 0 && scene.All(l => l.who is 6 or 2 or 4)
                            && scene.All(l => !string.IsNullOrWhiteSpace(l.text)),
                            $"{job.CharacterId} {kind}@{stage} speaks only as 6/2/4");
                        Check(scene.All(l => !l.text.StartsWith("[仮]")), $"{job.CharacterId} {kind}@{stage} is authored (no placeholder)");
                        // Ｘ投稿（who=4）は立ち絵を持たない（face=""）。それ以外は全行 face 指定あり。
                        Check(scene.All(l => l.who == 4 ? l.face.Length == 0 : l.face.Length > 0),
                            $"{job.CharacterId} {kind}@{stage} portraits follow the who rule");
                        count += scene.Length;
                    }
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

    // 初見チュートリアル（src/StageTutorial.cs・2026-09-16〜22）の本文を private static から引く。
    //   各ステージの _Ready はイントロ会話（_playerIntro）の末尾へこれらを Concat する＝
    //   「本編イントロそのまま」ではなくなるので、期待値も同じ規則で組み立てる。
    private static (int who, string text, string face)[] Tutorial(string name)
        => Data<(int who, string text, string face)[]>(typeof(StageTutorial), name);

    // その面の _Ready が _playerIntro の末尾へ足すはずのチュートリアル本文を、
    //   シーン生成**前**の既読キー集合（once）から組み立てて返す。
    //   並びは StageAkari/StageKoharu/StageRei._Ready と同じ：道中 → 習得スキル（回避→溜め打ち）
    //   → アンチャー紹介 → 強化アイテム（あかりのみ）。他ジョブ潜行中は一切出ない（StageTutorial.Take）。
    private static (int who, string text, string face)[] ExpectedTutorial(
        GameManager game, JobTuning job, string stageId, HashSet<string> seenBefore)
    {
        var expected = new List<(int who, string text, string face)>();
        if (job.Id != Job.Tank) return expected.ToArray();   // 潜行キャラのストーリー中は非表示・非消費
        void Take(string key, string block) { if (!seenBefore.Contains(key)) expected.AddRange(Tutorial(block)); }
        Take(StageTutorial.RouteSeenKey, "Route");
        if (game.HasDodge) Take(StageTutorial.SkillDodgeSeenKey, "SkillDodge");
        if (game.HasChargeShot) Take(StageTutorial.SkillChargeSeenKey, "SkillCharge");
        switch (stageId)
        {
            case "akari":
                Take(StageTutorial.AnkerAkariSeenKey, "AnkerAkari");
                Take(StageTutorial.ItemsAkariSeenKey, "ItemsAkari");
                break;
            case "koharu": Take(StageTutorial.AnkerKoharuSeenKey, "AnkerKoharu"); break;
            default: Take(StageTutorial.AnkerReiSeenKey, "AnkerRei"); break;
        }
        return expected.ToArray();
    }

    private async Task CheckStages(JobTuning job)
    {
        var game = GetNode<GameManager>("/root/Game");
        foreach (var (path, stageId, mid) in new[] { ("Akari.tscn", "akari", "MidEnd"), ("Koharu.tscn", "koharu", "ClassTalk"), ("Rei.tscn", "rei", "BossTalk") })
        {
            // Stage._Ready がチュートリアルの once を消費する前の既読集合を控える（期待値の組み立てに使う）。
            var seenBefore = new HashSet<string>(Read<HashSet<string>>(game, "_idleDialogSeen"));
            var expectedTutorial = ExpectedTutorial(game, job, stageId, seenBefore);
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
                // 初見チュートリアル（2026-09-16〜22）は _playerIntro の**末尾へ**繋がれる。
                //   本編イントロが前半にそのまま残っていること＋続く分が期待どおりのチュートリアル本文
                //   であることの両方を検める（ゆるく「前方一致だけ」にすると混入を見逃す）。
                var canon = beat == null && field == "_playerIntro"
                    ? baseline.Concat(expectedTutorial).ToArray() : baseline;
                if (beat == null)
                    Check(lines.SequenceEqual(canon), $"{job.CharacterId} {stageId}/{original} keeps canon lines");
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
        // 強化ショップの初回説明（ShopTutorial・2026-09-22）を既読にしておく。ResetPersistent が
        //   ShopTutorialSeen を折るので、下で "akari" を _cleared に入れた瞬間 ShopUnlocked が立ち、
        //   ホームの最初の Process が Hub.TryOpenShopTutorial() で ShopTutorial.tscn へ飛ばしてしまう
        //   （ハブごと解放されるので Z を押しても SNS へ入れない）。ここで見たいのは SNS の導線なので、
        //   once_phone_home と同じ流儀でこの一度きりの説明だけ消費済みにする。
        game.ShopTutorialSeen = true;
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        Read<HashSet<string>>(game, "_cleared").Add(job.UnlockStageId);
        await Frames(30);
        Check(Read<object>(hub, "_mode").ToString() == "Home", "first visit starts on the phone home");
        await Press(Key.Z);
        // SNS はアプリ起動アニメ（Mode.SnsOpening・Hub.SnsOpenDuration=0.65s）を挟んでから Cards へ移る。
        //   固定フレーム待ちだと尺を延ばされた途端に落ちるので、抜けるまで待ってから見る。
        for (int i = 0; i < 180 && Read<object>(hub, "_mode").ToString() == "SnsOpening"; i++) await Frames(1);
        await Frames(5);
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
