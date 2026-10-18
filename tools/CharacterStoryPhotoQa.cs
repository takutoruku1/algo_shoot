using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// CharacterStoryPhotoQa : 写真アプリ（Hub の Mode.Photos）で、他ジョブ潜行の「もう一度／帰還」6枚が
//   **新しい解禁キーで開く**ことを確かめ、一覧の画を残す（2026-09-23）。
//
// ── なぜ要るか ────────────────────────────────────────────────────────
//   6枚の解禁キーはもともと `{id}_playable_ch{n}_{memory|aftermath}`＝CharacterStoryFilm の FilmId だった。
//   回想／アフターが一枚絵をやめて吹き出しだけになった（CharacterStory.Memory / Aftermath）ため、
//   あの FilmId はもう誰も立てない＝6枠が永久に開かない状態になっていた。解禁キーを
//   CharacterStory.SeenKey（キャラ単位・面は問わない）へ付け替えたので、その付け替えを機械検査する。
//
//   起動: tools/qa_charstory_photo.tscn（APPDATA は build/qa_story/<name>_appdata へ隔離すること）。
public partial class CharacterStoryPhotoQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static object? Call(object obj, string name, params object?[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        GD.Print($"[PhotoQA] PASS {text}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            await Frames(2);
            if (OS.GetCmdlineUserArgs().Contains("--progression"))
            {
                CheckClearAutosave(game);
                await CheckProgression(game);
                GD.Print("[PhotoQA] ALL PASS");
                GetTree().Quit();
                return;
            }

            // 写真エントリの表（PhotoEntries）を反射で引く。6枚のIDと、そこに載っているキーを見る。
            var entryType = typeof(Hub).GetNestedType("PhotoEntry", BindingFlags.NonPublic)!;
            var entries = (Array)typeof(Hub).GetField("PhotoEntries", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var idProp = entryType.GetProperty("Id")!;
            var keysProp = entryType.GetProperty("Keys")!;
            string[] playable =
            {
                "akari_playable_memory", "akari_playable_after",
                "koharu_playable_memory", "koharu_playable_after",
                "rei_playable_memory", "rei_playable_after",
            };
            foreach (object entry in entries)
            {
                string id = (string)idProp.GetValue(entry)!;
                if (!playable.Contains(id)) continue;
                var keys = (string[])keysProp.GetValue(entry)!;
                Check(keys.All(k => k.StartsWith("charstory_")),
                    $"{id} unlocks through the new character-story key ({string.Join(",", keys)})");
            }

            // どの面で見たかは問わない＝1回の潜行（memory/aftermath 各1本）で、そのキャラの2枚が開く。
            foreach (var job in new[] { Job.Melee, Job.Heal, Job.Magic })
            {
                FilmSkip.MarkSeen(game, CharacterStory.SeenKey(job, aftermath: false));
                FilmSkip.MarkSeen(game, CharacterStory.SeenKey(job, aftermath: true));
            }

            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            // 写真アプリを開いた状態で入る（--hub-photos と同じ入口。_Ready が見るので追加前に立てる）。
            hub.GetType().GetField("_openPhotos", Private)!.SetValue(hub, true);
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(40);

            int open = 0, total = entries.Length;
            foreach (object entry in entries)
            {
                bool acquired = (bool)Call(hub, "PhotoAcquired", entry)!;
                string id = (string)idProp.GetValue(entry)!;
                if (acquired) open++;
                if (playable.Contains(id)) Check(acquired, $"{id} is unlocked after one dive as that character");
            }
            // 他の枚（ミナ本編のフィルム等）は未見のまま＝6枚だけが開いていること。
            Check(open == 6, $"exactly the six playable photos are open ({open}/{total})");
            Check(Read<object>(hub, "_mode").ToString() == "Photos", "photo app is on screen");

            // 解禁した6枚（index 9〜14）までグリッドを送る＝「開いている枚」が画に入る。
            int first = Array.FindIndex(entries.Cast<object>().ToArray(), e => (string)idProp.GetValue(e)! == playable[0]);
            hub.GetType().GetField("_photoSel", Private)!.SetValue(hub, first + 3);
            await Frames(60);

            string outDir = ProjectSettings.GlobalizePath("res://build/qa_story/photos");
            DirAccess.MakeDirRecursiveAbsolute(outDir);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (var image = GetViewport().GetTexture().GetImage()) image.SavePng($"{outDir}/photos_playable.png");
            GD.Print($"[PhotoQA] shot {outDir}/photos_playable.png");

            GD.Print("[PhotoQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[PhotoQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static void CheckClearAutosave(GameManager game)
    {
        foreach (var job in Jobs.All)
        {
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Normal;
            if (job.UnlockStageId.Length > 0) game.CompleteStage(job.UnlockStageId);
            game.SelectedJob = job.Id;
            string stage = job.UnlockStageId == "akari" ? "koharu" : "akari";
            Check(game.SaveToSlot(1), job.CharacterName + ": save the pre-clear manual slot");
            game.AutoSaveEnabled = true;
            game.CompleteStage(stage);
            long impression = game.Impression;
            int followers = game.Followers;
            int chapter = game.CharacterChapter(job.Id);
            bool reiCleared = game.IsStageCleared("rei");
            Check(game.JustClearedStageId == stage, job.CharacterName + ": clear queues the return dialogue");
            game.ResetPersistent();
            Check(game.LoadFromSlot(0), job.CharacterName + ": load the immediate clear autosave before entering the hub");
            Check(game.IsStageCleared(stage) && game.IsJobUnlocked(job.Id) && game.IsJobUnlocked(Job.Melee),
                job.CharacterName + ": autosave preserves the cleared stage and unlocked characters");
            Check(game.SelectedJob == job.Id && game.CharacterChapter(job.Id) == chapter
                && game.ClearedWithCompanion(stage) == (job.Id != Job.Tank),
                job.CharacterName + ": autosave preserves the selected character, chapter and return route");
            Check(game.Impression == impression && game.Followers == followers,
                job.CharacterName + ": clear rewards are restored exactly once");
            Check(game.LoadFromSlot(1) && !game.IsStageCleared(stage),
                job.CharacterName + ": autosave does not overwrite the manual slot");
            game.AutoSaveEnabled = false;
            game.CompleteStage("rei");
            Check(game.LoadFromSlot(0) && game.IsStageCleared("rei") == reiCleared
                && game.Impression == impression && game.Followers == followers,
                job.CharacterName + ": disabling autosave leaves the previous clear snapshot intact");
        }
        game.ResetPersistent();
        game.JustClearedStageId = null;
        game.AutoSaveEnabled = false;
    }

    private async Task CheckProgression(GameManager game)
    {
        game.SelectedJob = Job.Tank;
        game.CompleteStage("akari");
        game.JustClearedStageId = null;
        game.SelectedJob = Job.Melee;
        game.SelectedEntry = GameManager.StageEntry.Start;
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        await Frames(2);
        Call(hub, "Dive", "res://Koharu.tscn");
        await QaSceneTransition.Wait(this);
        Check(GetTree().CurrentScene is KoharuRoot && game.CharacterChapter(Job.Melee) == 1,
            "entering the next story stage retains companion chapter one");

        await PauseAction(PauseMenu.Act.Leave);
        Check(GetTree().CurrentScene is Hub && game.CharacterChapter(Job.Melee) == 1
            && !game.ClearedWithCompanion("koharu"), "leaving an unfinished chapter does not advance or report a clear");
        Call(GetTree().CurrentScene, "Dive", "res://Koharu.tscn");
        await QaSceneTransition.Wait(this);
        Check(game.CharacterChapter(Job.Melee) == 1, "reentering after leaving resumes the same chapter");
        await PauseAction(PauseMenu.Act.Restart);
        Check(GetTree().CurrentScene is KoharuRoot && game.CharacterChapter(Job.Melee) == 1,
            "restarting also retains the unfinished chapter");

        Call(((KoharuRoot)GetTree().CurrentScene).Stage, "ReturnToHub");
        await QaSceneTransition.Wait(this);
        Check(game.CharacterChapter(Job.Melee) == 2 && game.ClearedWithCompanion("koharu"),
            "the real clear callback advances exactly one companion chapter");
        hub = (Hub)GetTree().CurrentScene;
        var dialogue = Read<(string speaker, string text)[]>(hub, "_dlg");
        Check(dialogue[0].speaker == "あかり" && dialogue.Any(l => l.text.Contains("帰ってきた仲間"))
            && !dialogue.Any(l => l.text.Contains("数えてまいりました") || l.text.Contains("必死で振った")),
            "companion returns before Mina receives the report without claiming to have been there");
        Check(dialogue.Any(l => l.text.Contains("聞いただけのAI")), "the backlash quotes the report Mina actually posted");
        game.SelectedJob = Job.Tank;
        var savedReturn = ((string, string)[])Call(hub, "ReturnDialog", "koharu")!;
        string post = savedReturn[0].Item2;
        Check(post.Contains("帰ってきた仲間"), "changing the selected character does not rewrite the recorded post");
        Check(game.SaveToSlot(2), "save companion progress and the return route");
        game.ResetPersistent();
        Check(game.CharacterChapter(Job.Melee) == 1 && !game.ClearedWithCompanion("koharu"), "new game clears story progress");
        Check(game.LoadFromSlot(2) && game.CharacterChapter(Job.Melee) == 2 && game.ClearedWithCompanion("koharu"),
            "save loading restores the completed chapter and matching post");
        game.SelectedJob = Job.Melee;
        game.CompleteStage(GameManager.FinalStageId);
        Check(game.CharacterChapter(Job.Melee) == 2, "FINAL does not advance the companion's separate story");
        game.Difficulty = GameManager.Diff.Lunatic;
        game.CompleteStage("akari");
        Check(game.CharacterChapter(Job.Melee) == 2, "Lunatic does not consume an unread companion chapter");
        game.Difficulty = GameManager.Diff.Normal;
        for (int i = 0; i < 5; i++) game.CompleteStage("akari");
        Check(game.CharacterChapter(Job.Melee) == CharacterStory.LoopChapter, "completed companion stories remain on the loop chapter");

        foreach (string id in new[] { "akari", "koharu", "rei" })
        {
            game.CompleteStage(id);
            var report = ((string, string)[])Call(hub, "ReturnDialog", id)!;
            Check(report[0].Item2.Contains("帰ってきた仲間"), id + " receives a companion report");
            if (id == "rei") Check(report.Any(l => l.Item2.Contains("声が"))
                && report.Any(l => l.Item2.Contains("返事して")), "companion Rei clear still leads to Mina's rescue");
            game.SelectedJob = Job.Tank;
            game.CompleteStage(id);
            var minaLines = ((string, string)[])Call(hub, "ReturnDialog", id)!;
            Check(!game.ClearedWithCompanion(id)
                && !minaLines.Any(l => l.Item2.Contains("帰ってきた仲間")), id + " keeps Mina's own return when she completes the stage");
            game.SelectedJob = Job.Melee;
        }
        game.JustClearedStageId = null;
        hub.QueueFree();
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Audio.Instance?.StopMusic(0);
        await Frames(4);
    }

    private async Task PauseAction(PauseMenu.Act act)
    {
        var menu = GetNode<PauseMenu>("/root/PauseMenu");
        menu.Open();
        Call(menu, "OpenConfirm", act, "QA");
        typeof(PauseMenu).GetField("_confirmYes", Private)!.SetValue(menu, true);
        Call(menu, "RunConfirm");
        await QaSceneTransition.Wait(this);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
