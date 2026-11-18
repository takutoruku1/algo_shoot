using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class SaveSlotQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Invoke(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Private)!.Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print("[SaveSlotQA] PASS " + message);
    }
    private static string Saved(int slot) => System.IO.File.ReadAllText(ProjectSettings.GlobalizePath($"user://save_{slot}.json"));
    private string _output = "res://build/qa_story/autosave_perf/slots";

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        bool uiOnly = Array.IndexOf(OS.GetCmdlineUserArgs(), "--ui-only") >= 0;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--output=", StringComparison.Ordinal)) _output = arg[9..];
        int exit = 0;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save directory");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            await Frames(2);
            Invoke(GetNode<PauseMenu>("/root/PauseMenu"), "Close");
            var game = GetNode<GameManager>("/root/Game");
            var clock = game.GetNode("PlayClock");
            clock.SetProcess(false);
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            clock._Process(3661);
            Check(Math.Abs(game.PlayTimeSeconds - 3661) < .01, "new game counts elapsed play time");
            game.ProcessMode = ProcessModeEnum.Disabled;
            Check(clock.CanProcess(), "story freeze does not stop play time");
            game.ProcessMode = ProcessModeEnum.Inherit;
            GetTree().Paused = true;
            Check(!clock.CanProcess(), "pause excludes play time");
            GetTree().Paused = false;
            Engine.TimeScale = .5;
            clock._Process(5);
            Engine.TimeScale = 1;
            Check(Math.Abs(game.PlayTimeSeconds - 3671) < .01, "slow motion counts real seconds");
            game.Difficulty = GameManager.Diff.Hard;
            game.CompleteStage("akari");
            game.SelectedJob = Job.Melee;
            Check(!game.SlotExists(0), "disabled autosave does not write milestones");
            Check(game.SaveToSlot(1), "manual save writes metadata");
            var summary = game.ReadSlotInfo(1);
            Check(summary.Exists && summary.Readable && summary.SavedAt.Contains('/') && summary.PlayTime.Contains("1時間01分")
                && summary.Progress.Contains("1/3") && summary.Details.Contains("HARD") && summary.Details.Contains("あかり"), "slot describes its own date, time, character, difficulty and progress");
            game.ResetPersistent();
            Check(game.PlayTimeSeconds == 0, "new game resets time");
            Check(game.LoadFromSlot(1) && Math.Abs(game.PlayTimeSeconds - 3671) < .01 && game.Difficulty == GameManager.Diff.Hard,
                "loading restores play time and difficulty");
            clock._Process(60);
            Check(game.SaveToSlot(2), "save after loading retains prior play time");
            Check(game.ReadSlotInfo(1).PlayTime != game.ReadSlotInfo(2).PlayTime, "slot metadata is independent of current state");
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("user://save_3.json"), "{\"impression\":1234,\"followers\":12}");
            Check(game.ReadSlotInfo(3).PlayTime.Contains("未記録") && game.LoadFromSlot(3) && game.PlayTimeSeconds == 0,
                "legacy save remains loadable without inventing historical play time");
            clock._Process(120);
            Check(game.SaveToSlot(3) && game.ReadSlotInfo(3).PlayTime.Contains("記録開始後"), "legacy time is labelled as measured since update");
            game.AutoSaveEnabled = true;
            game.MarkMidBossCleared("akari");
            Check(game.SlotExists(0) && Saved(0).Contains("akari"), "midboss unlock saves immediately");
            game.GainImpression(100000);
            string upgrade = GameManager.Upgrades[0].Id;
            Check(game.TryPurchase(upgrade) && Saved(0).Contains(upgrade), "purchase saves immediately");
            string auto = Saved(0), manual = Saved(1);
            game.TrainingMode = true;
            game.AutoSave();
            Check(!game.SaveToSlot(1) && Saved(0) == auto && Saved(1) == manual, "training cannot leak into auto or manual saves");
            game.TrainingMode = false;
            var pause = GetNode<PauseMenu>("/root/PauseMenu");
            pause.Open();
            pause.QuitOverride = () => { };
            Invoke(pause, "OpenAsk", PauseMenu.QuitKind.Exit, false);
            game.AddFollowers(10);
            Invoke(pause, "RunAsk", PauseMenu.AskChoice.NoSave);
            Check(pause.QuitRequested && Saved(0) == auto && Saved(1) == manual, "exit without saving leaves all existing slots unchanged");
            pause.QuitOverride = null;
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("user://save_2.json"), "[]");
            Check(game.ReadSlotInfo(2) is { Exists: true, Readable: false }, "invalid save is identified without loading it");
            var title = GD.Load<PackedScene>("res://TitleMenu.tscn").Instantiate<TitleMenu>();
            GetTree().Root.AddChild(title);
            GetTree().CurrentScene = title;
            title.SetProcess(false);
            double beforeTitle = game.PlayTimeSeconds;
            clock._Process(120);
            Check(game.PlayTimeSeconds == beforeTitle, "title screen is excluded from play time");
            Invoke(title, "Confirm");
            await Shot("title_slots");
            pause.Open();
            Invoke(pause, "OpenSlots", true);
            Check(pause.SlotInfo(1).SavedAt == summary.SavedAt, "pause and title read the same slot data");
            await Shot("pause_slots");
            Invoke(pause, "Close");
            if (uiOnly) await CaptureSlotViews(game, title, pause);
            title.QueueFree();
            await Frames(3);
            if (!uiOnly)
            {
                await VerifyLunaticUnlock(game);
                await VerifyMinaBossModeDoesNotUnlock(game);
                await VerifyFinalClear(game);
            }
            GD.Print("[SaveSlotQA] ALL PASS");
        }
        catch (Exception error) { exit = 1; GD.PushError("[SaveSlotQA] FAIL " + error); }
        GetTree().Paused = false;
        Engine.TimeScale = 1;
        Audio.Instance?.StopMusic(0);
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
            if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
        await Frames(3);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(3);
        GetTree().Quit(exit);
    }

    private async Task CaptureSlotViews(GameManager game, TitleMenu title, PauseMenu pause)
    {
        string regular = Saved(1);
        using var json = new Json();
        Check(json.Parse(regular) == Error.Ok, "UI fixture starts from a valid saved slot");
        var full = json.Data.AsGodotDictionary();
        var cleared = new Godot.Collections.Array();
        foreach (var stage in GameManager.Stages) cleared.Add(stage.Id);
        full["cleared"] = cleared;
        full["clearTimes"].AsGodotDictionary()[$"{GameManager.FinalStageId}_{GameManager.Diff.Lunatic}"] = 42;
        full["playTimeSeconds"] = 9999 * 3600 + 59 * 60;
        full["playTimePartial"] = true;
        full["difficulty"] = (int)GameManager.Diff.Lunatic;
        full["followers"] = int.MaxValue;
        full["impression"] = 999999999999999L;
        string expanded = Json.Stringify(full);
        System.IO.File.Delete(ProjectSettings.GlobalizePath("user://save_3.json"));
        Check(game.ReadSlotInfo(0) is { Exists: true, Readable: true }
            && game.ReadSlotInfo(1) is { Exists: true, Readable: true }
            && game.ReadSlotInfo(2) is { Exists: true, Readable: false }
            && !game.ReadSlotInfo(3).Exists, "UI fixture contains auto, readable, unreadable and empty slots");
        title.SetProcess(false);
        pause.SetProcess(false);
        typeof(TitleMenu).GetField("_t", Private)!.SetValue(title, 8.0);
        typeof(TitleMenu).GetField("_talkT", Private)!.SetValue(title, 0.0);
        if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540) })
        {
            if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetSize(size);
            await Frames(8);
            Invoke(title, "RefreshSlotInfos");
            foreach (var (slot, label) in new[] { (0, "auto"), (1, "saved"), (2, "unreadable"), (3, "empty") })
                await TitleSlotShot(title, slot, $"title_{label}_{size.X}x{size.Y}");
            foreach (bool save in new[] { true, false })
            {
                foreach (var (slot, label) in new[] { (0, "saved"), (1, "unreadable"), (2, "empty") })
                    await PauseSlotShot(title, pause, save, slot, $"{(save ? "save" : "load")}_{label}_{size.X}x{size.Y}");
                Invoke(pause, "Close");
            }
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("user://save_1.json"), expanded);
            var info = game.ReadSlotInfo(1);
            Check(info.Progress.Contains("FINAL クリア") && info.PlayTime.Contains("9999時間59分（記録開始後）")
                && info.Details.Contains(int.MaxValue.ToString("N0")), "expanded UI fixture preserves complete save metadata");
            Invoke(title, "RefreshSlotInfos");
            await TitleSlotShot(title, 1, $"title_full_clear_long_details_{size.X}x{size.Y}");
            foreach (bool save in new[] { true, false })
            {
                await PauseSlotShot(title, pause, save, 0, $"{(save ? "save" : "load")}_full_clear_long_details_{size.X}x{size.Y}");
                Invoke(pause, "Close");
            }
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("user://save_1.json"), regular);
        }
        pause.SetProcess(true);
    }

    private async Task TitleSlotShot(TitleMenu title, int slot, string name)
    {
        typeof(TitleMenu).GetField("_picking", Private)!.SetValue(title, true);
        typeof(TitleMenu).GetField("_pick", Private)!.SetValue(title, slot);
        title.QueueRedraw();
        await Shot(name);
    }

    private async Task PauseSlotShot(TitleMenu title, PauseMenu pause, bool save, int slot, string name)
    {
        typeof(TitleMenu).GetField("_picking", Private)!.SetValue(title, false);
        title.QueueRedraw();
        if (!pause.IsOpen) pause.Open();
        Invoke(pause, "OpenSlots", save);
        typeof(PauseMenu).GetField("_slotSel", Private)!.SetValue(pause, slot);
        ((CanvasItem)typeof(PauseMenu).GetField("_canvas", Private)!.GetValue(pause)!).QueueRedraw();
        await Shot(name);
    }

    private async Task VerifyLunaticUnlock(GameManager game)
    {
        game.ResetPersistent();
        game.AutoSaveEnabled = false;
        game.AddFollowers(10000);
        Check(!game.IsLunaticUnlocked && !game.LunaticUnlockPending, "followers alone cannot unlock LUNATIC");
        foreach (string stage in new[] { "akari", "koharu", "rei", "boss_solo_akari", "boss_solo_koharu", "boss_solo_rei" })
            game.RecordClearTime(stage, GameManager.Diff.Hard, 10);
        Check(!game.IsLunaticUnlocked, "other boss clears cannot unlock LUNATIC");
        foreach (string record in new[] { "boss_solo_mina", BossRushRoot.RecordId })
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.RecordClearTime(record, difficulty, 42);
            Check(!game.IsLunaticUnlocked && !game.LunaticUnlockPending,
                $"{record}/{difficulty}: MINA clear outside story cannot unlock LUNATIC");
            game.SaveToSlot(0);
            game.ResetPersistent();
            Check(game.LoadFromSlot(0) && !game.IsLunaticUnlocked && !game.LunaticUnlockPending,
                "loading boss challenge clear records cannot unlock LUNATIC");
        }
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.RecordClearTime(GameManager.FinalStageId, difficulty, 42);
            Check(game.Followers == 0 && game.IsLunaticUnlocked && game.LunaticUnlockPending,
                $"story/{difficulty}: MINA clear unlocks with zero followers");
            game.SaveToSlot(0);
            game.ResetPersistent();
            Check(game.LoadFromSlot(0) && game.IsLunaticUnlocked && game.LunaticUnlockPending,
                "existing clear records unlock on load without a new save field");
        }
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        hub.SetProcess(false);
        hub._Process(0);
        Check(hub.HasNode("LunaticUnlock"), "loaded MINA clear presents unlock on entering home");
        await FinishUnlock(hub);
        game.ResetPersistent();
        Check(game.LoadFromSlot(0) && game.IsLunaticUnlocked && !game.LunaticUnlockPending,
            "unlock presentation completion persists even with optional autosave off");
        game.ResetIdleDialogSeen();
        Check(!game.LunaticUnlockPending && !LunaticUnlock.PlayIfPending(hub), "revisiting home and resetting small talk cannot repeat the presentation");
        hub.QueueFree();
        await Frames(3);
        game.ResetPersistent();
        Check(!game.IsLunaticUnlocked && !game.LunaticUnlockPending, "new game resets the unlock");
    }

    private async Task VerifyMinaBossModeDoesNotUnlock(GameManager game)
    {
        foreach (bool single in new[] { true, false })
        {
            game.ResetPersistent();
            game.AutoSaveEnabled = true;
            game.Difficulty = GameManager.Diff.Normal;
            game.PendingBossIndex = 3;
            game.SaveToSlot(1);
            string manual = Saved(1);
            var root = GD.Load<PackedScene>(single ? BossRushRoot.SingleScenePath : BossRushRoot.ScenePath).Instantiate<BossRushRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            if (!single) typeof(BossRushRoot).GetProperty(nameof(BossRushRoot.Round))!.SetValue(root, 3);
            Invoke(root, "SpawnBoss");
            root.World.ProcessMode = ProcessModeEnum.Disabled;
            var boss = (BossMina)root.Boss!;
            typeof(Enemy).GetMethod("Redeem", Private)!.Invoke(boss, null);
            Invoke(boss, "OnCryEnd");
            root.Hud.HideBubble();
            Hud.BubblePaused = false;
            root._Process(.1);
            string record = single ? "boss_solo_mina" : BossRushRoot.RecordId;
            Check(root.Completed && !game.IsLunaticUnlocked && !game.LunaticUnlockPending
                && Saved(0).Contains(record) && Saved(1) == manual,
                $"{record}: production MINA defeat saves the result without unlocking LUNATIC");
            root._Process(5.1);
            Check(!root.HasNode("LunaticUnlock"), $"{record}: clear result does not show an unlock presentation");
            await QaSceneTransition.Wait(this);
            Check(single ? GetTree().CurrentScene is BossSelectRoot : GetTree().CurrentScene is Hub,
                $"{record}: clear result returns to the expected screen");
            game.ResetPersistent();
            Check(game.LoadFromSlot(0) && !game.IsLunaticUnlocked && !game.LunaticUnlockPending && !game.IsFinalCleared,
                $"{record}: saved boss challenge result cannot unlock LUNATIC");
            GetTree().CurrentScene.QueueFree();
            await Frames(3);
        }
    }

    private async Task FinishUnlock(Node owner)
    {
        var reveal = owner.GetNode<LunaticUnlock>("LunaticUnlock");
        reveal.SetProcess(false);
        reveal._Process(.2);
        await Shot("lunatic_unlock_start");
        reveal._Process(1.6);
        await Shot("lunatic_unlocked");
        Check(GameManager.Instance!.LunaticUnlockPending, "presentation is not marked seen before it finishes");
        reveal._Process(3);
        await Frames(3);
        Check(!owner.HasNode("LunaticUnlock") && !GameManager.Instance.LunaticUnlockPending,
            "presentation finishes and releases the screen");
    }

    private async Task VerifyFinalClear(GameManager game)
    {
        foreach (var difficulty in Enum.GetValues<GameManager.Diff>())
        {
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            foreach (var stageDef in GameManager.Stages) game.CompleteStage(stageDef.Id);
            game.JustClearedStageId = null;
            game.ShopTutorialSeen = true;
            game.Difficulty = difficulty;
            game.SelectedEntry = GameManager.StageEntry.Start;
            Check(game.SaveToSlot(1), $"{difficulty}: pre-clear manual save");
            string manual = Saved(1);
            game.AutoSaveEnabled = difficulty is GameManager.Diff.Easy or GameManager.Diff.Hard;
            var root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<MinaRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.ProcessMode = ProcessModeEnum.Disabled;
            var stage = root.Stage;
            var boss = new BossMina();
            root.World.AddChild(boss);
            typeof(StageMina).GetField("_boss", Private)!.SetValue(stage, boss);
            typeof(StageMina).GetField("_step", Private)!.SetValue(stage, 4);
            typeof(StageMina).GetField("_stageElapsed", Private)!.SetValue(stage, 123.25);
            typeof(Enemy).GetMethod("Redeem", Private)!.Invoke(boss, null);
            long score = game.Score;
            Check(boss.IsPurified && score > 0, $"{difficulty}: actual defeat awards score");
            root.Hud.SetCinematicMode(true);
            stage._Process(.5);
            string firstSave = Saved(0);
            stage._Process(30);
            Check(Saved(0) == firstSave && game.GetBestTime(GameManager.FinalStageId, difficulty) == 123.25f,
                $"{difficulty}: clear saves before cinematics and excludes dialogue time without repeated writes");
            game.ResetPersistent();
            Check(game.LoadFromSlot(0) && game.IsFinalCleared && game.IsLunaticUnlocked && game.AllStoryCleared
                && game.GetBestTime(GameManager.FinalStageId, difficulty) == 123.25f
                && game.GetBestScore(GameManager.FinalStageId, difficulty) == score
                && game.ReadSlotInfo(0).Progress.Contains("FINAL クリア"),
                $"{difficulty}: defeat, difficulty, time and score survive reloading (autosave={game.AutoSaveEnabled})");
            root.Hud.SetCinematicMode(false);
            game.AddFollowers(7);
            int followers = game.Followers;
            if (difficulty == GameManager.Diff.Lunatic)
            {
                Invoke(stage, "Step_LunaticFinish", 0.0);
                Invoke(stage, "Step_LunaticFinish", 0.0);
                await FinishUnlock(root);
                Invoke(stage, "Step_LunaticFinish", 3.1);
                await QaSceneTransition.Wait(this);
                Check(GetTree().CurrentScene is Hub && game.Contamination == 0, "LUNATIC result returns to a clean home");
                await Shot("final_lunatic_home");
            }
            else
            {
                Invoke(stage, "Step_Transition");
                await FinishUnlock(root);
                Invoke(stage, "Step_Transition");
                var film = root.Hud.GetNode<MinaStoryFilm>("MinaStoryFilm");
                Check(game.IsFinalCleared && root.Hud.CinematicMode, $"{difficulty}: aftermath starts with the clear already saved");
                film.QueueFree();
                await Frames(2);
                Invoke(stage, "Step_Transition");
                await QaSceneTransition.Wait(this);
                Check(GetTree().CurrentScene is Final, $"{difficulty}: final conversation is preserved");
                GetTree().CurrentScene.QueueFree();
                await Frames(3);
                var ep = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
                GetTree().Root.AddChild(ep);
                GetTree().CurrentScene = ep;
                ep.SetProcess(false);
                game.MarkEndingRollSeen();
                Invoke(ep, "ApplyE6Choice", (int)difficulty % 2);
                string reply = game.LastSentWord;
                typeof(Epilogue).GetField("_phase", Private)!.SetValue(ep, 3);
                var end = (System.Collections.IList)typeof(Epilogue).GetField("_end", Private)!.GetValue(ep)!;
                typeof(Epilogue).GetField("_line", Private)!.SetValue(ep, end.Count - 1);
                ep._Process(0);
                var pages = (System.Collections.IList)typeof(Epilogue).GetField("_pages", Private)!.GetValue(ep)!;
                typeof(Epilogue).GetField("_page", Private)!.SetValue(ep, pages.Count - 1);
                typeof(Epilogue).GetField("_reveal", Private)!.SetValue(ep, 10000.0);
                typeof(Epilogue).GetField("_zHeld", Private)!.SetValue(ep, false);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = true });
                await Frames(2);
                ep._Process(.1);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = false });
                Check(LoadingScreen.IsActive, $"{difficulty}: final dialogue input starts home transition");
                await QaSceneTransition.Wait(this);
                Check(GetTree().CurrentScene is Hub && game.Contamination == 0, $"{difficulty}: ending returns to a clean home");
                await Shot($"final_{difficulty.ToString().ToLowerInvariant()}_home");
                game.ResetPersistent();
                Check(game.LoadFromSlot(0) && game.EndingRollSeen && game.ChosenAt("e6") == reply,
                    $"{difficulty}: final choice and credits persist after restart");
            }
            game.ResetPersistent();
            Check(game.LoadFromSlot(0) && game.IsFinalCleared && game.Followers == followers && Saved(1) == manual,
                $"{difficulty}: home save includes late rewards and leaves the manual slot untouched");
            var hub = (Hub)GetTree().CurrentScene;
            var entries = (Array)typeof(Hub).GetField("_entries", Private)!.GetValue(hub)!;
            int finalIndex = 0;
            while (!(bool)entries.GetValue(finalIndex)!.GetType().GetField("IsFinal")!.GetValue(entries.GetValue(finalIndex))!) finalIndex++;
            object finalEntry = entries.GetValue(finalIndex)!;
            Check((bool)finalEntry.GetType().GetField("Cleared")!.GetValue(finalEntry)!, $"{difficulty}: final card displays cleared status");
            typeof(Hub).GetField("_sel", Private)!.SetValue(hub, finalIndex);
            Check(!(bool)typeof(Hub).GetMethod("CanReplySel", Private)!.Invoke(hub, null)!, "final card does not expose ordinary-stage replies");
            Invoke(hub, "OpenDetail");
            await Shot($"final_{difficulty.ToString().ToLowerInvariant()}_cleared");
            GetTree().CurrentScene.QueueFree();
            await Frames(3);
        }
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string folder = ProjectSettings.GlobalizePath(_output);
        DirAccess.MakeDirRecursiveAbsolute(folder);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{folder}/{name}.png") == Error.Ok, "screenshot " + name);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
