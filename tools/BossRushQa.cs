using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossRushQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameManager _game = null!;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static void Property(object obj, string name, object value) => obj.GetType().GetProperty(name)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[BossRushQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            Read<System.Collections.Generic.HashSet<string>>(_game, "_readLines").Clear();
            _game.AutoSaveEnabled = false;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            GetTree().Paused = false;
            DisplayServer.WindowSetPosition(new Vector2I(-16000, -16000));
            await Frames(3);
            await CheckUnlock();
            await CheckMenu();
            if (!OS.GetCmdlineUserArgs().Contains("--retry-only"))
                foreach (var diff in Enum.GetValues<GameManager.Diff>()) await CheckRun(diff);
            await CheckRetry();
            Check(!GameManager.BossRushActive && !GameManager.SkipBossStory, "story mode restored after leaving the rush");
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Frames(10);
            GD.Print("[BossRushQA] ALL PASS (direct damage, attack prerequisites and invulnerability used; not a difficulty test)");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[BossRushQA] FAIL {e}");
            GetTree().Quit(1);
        }
    }

    private T Mount<T>(string scene) where T : Node
    {
        var root = GD.Load<PackedScene>(scene).Instantiate<T>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        return root;
    }

    private async Task Remove(Node node)
    {
        node.QueueFree();
        await Frames(5);
    }

    private async Task CheckUnlock()
    {
        Check(!_game.BossRushUnlocked, "new game is locked");
        foreach (var stage in GameManager.Stages) _game.CompleteStage(stage.Id);
        Check(!_game.BossRushUnlocked, "three story clears alone do not unlock the rush");
        _game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Easy, 100);
        Check(!_game.BossRushUnlocked, "Mina clear without credits remains locked");
        var hub = Mount<Hub>("res://Hub.tscn");
        Check(!(bool)Call(hub, "HomeAppUnlocked", 5)!, "home app uses the same unlock gate");
        Call(hub, "OpenHomeApp", 5);
        Check(!LoadingScreen.IsActive, "locked app cannot launch");
        await Remove(hub);
        var ending = Mount<Epilogue>("res://Epilogue.tscn");
        Write(ending, "_phase", 2);
        Write(ending, "_t", 10000d);
        ending._Process(1d / 60);
        Check(_game.EndingRollSeen && _game.BossRushUnlocked, "finishing actual credits unlocks the rush");
        await Remove(ending);
        Check(_game.SaveToSlot(3), "unlock can be saved");
        _game.ResetPersistent();
        Check(!_game.BossRushUnlocked && !_game.EndingRollSeen, "new game removes unlock");
        Check(_game.LoadFromSlot(3) && _game.BossRushUnlocked, "unlock survives save/load");
        _game.ResetPersistent();
        _game.RecordChoice("e6", "また話そう", Array.Empty<string>(), 0);
        Check(!_game.BossRushUnlocked, "ending choice without Mina clear is insufficient");
        _game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Normal, 200);
        Check(!_game.EndingRollSeen && _game.BossRushUnlocked, "existing post-credits choice unlocks old saves");
        Check(_game.SaveToSlot(3), "legacy evidence saved");
        _game.ResetPersistent();
        Check(_game.LoadFromSlot(3) && _game.BossRushUnlocked, "legacy evidence survives load");
        _game.ResetPersistent();
        _game.MarkLineRead(Epilogue.AfterCreditsLine);
        Check(!_game.BossRushUnlocked, "read history alone does not unlock a fresh save");
        _game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Normal, 200);
        Check(_game.BossRushUnlocked, "old completed ending is recognized from read history even without a later save");
        foreach (var stage in GameManager.Stages) _game.CompleteStage(stage.Id);
        _game.JustClearedStageId = null;
        _game.ShopTutorialSeen = true;
    }

    private async Task CheckMenu()
    {
        var hub = Mount<Hub>("res://Hub.tscn");
        var mode = typeof(Hub).GetField("_mode", Private)!;
        mode.SetValue(hub, Enum.Parse(mode.FieldType, "Home"));
        Check((bool)Call(hub, "HomeAppUnlocked", 5)!, "completed save exposes boss rush app");
        await Shot("home");
        Call(hub, "OpenHomeApp", 5);
        await Wait(() => GetTree().CurrentScene is DiffSelect && !LoadingScreen.IsActive, 600, "home opens difficulty selection");
        var select = (DiffSelect)GetTree().CurrentScene;
        Check(_game.PendingStageScene == BossRushRoot.ScenePath, "difficulty selection targets rush scene");
        await Shot("difficulty");
        Write(select, "_sel", (int)GameManager.Diff.Hard);
        Call(select, "Dive");
        await Wait(() => GetTree().CurrentScene is BossRushRoot && !LoadingScreen.IsActive, 600, "difficulty selection launches rush");
        var rush = (BossRushRoot)GetTree().CurrentScene;
        Check(_game.Difficulty == GameManager.Diff.Hard && rush.Round == 0, "chosen difficulty starts with Akari");
        await Remove(rush);
    }

    private async Task CheckRun(GameManager.Diff diff)
    {
        _game.Difficulty = diff;
        _game.SelectedJob = (Job)((int)diff % 4);
        _game.TrainingSetUpgrade("n_life_1", true);
        _game.TrainingSetUpgrade("n_bomb_1", true);
        var rush = Mount<BossRushRoot>(BossRushRoot.ScenePath);
        Check(rush.Player.Lives == _game.StartLives && _game.Bombs == _game.StartBombs,
            $"{diff}: character and shop upgrades applied");
        Check(_game.SelectedJob == (Job)((int)diff % 4), $"{diff}: selected character retained");
        typeof(Player).GetMethod("ExtendInvincible", Private)!.Invoke(rush.Player, new object[] { 10000f });
        Property(rush.Player, "Lives", 2);
        rush.Hud.SetLives(2);
        Property(_game, "Bombs", 1);
        _game.AddScoreShard(1234);
        var player = rush.Player;
        Type[] bosses = { typeof(BossAkari), typeof(BossKoharu), typeof(BossRei), typeof(BossMina) };
        for (int round = 0; round < 4; round++)
        {
            await Wait(() => rush.Boss != null, 180, $"{diff}: round {round + 1} starts");
            Enemy boss = rush.Boss!;
            Check(rush.Round == round && boss.GetType() == bosses[round], $"{diff}: boss order {BossRushRoot.BossNames[round]}");
            Check(rush.Player == player && player.Lives == 2 && _game.Bombs == 1 && _game.Score >= 1234,
                $"{diff}: life, bombs, score and player carry into round {round + 1}");
            Check(!rush.World.GetChildren().Any(n => n is Spawner or CameoBoss), $"{diff}: no road waves or midboss");
            Check(boss.GetNode<BossPostSequence>("PostSequence").Floor == 0, $"{diff}: no dialogue HP gate");
            if (boss is BossAkari) Write(boss, "_shieldBreaks", 3);
            if (diff == GameManager.Diff.Easy)
            {
                await Frames(150);
                await Shot($"round_{round + 1}");
            }
            bool evolved = false;
            for (int frame = 0; rush.Round == round && frame < 2400; frame++)
            {
                if (Read<string>(rush.Hud, "_dlgText").Length > 0 || Read<string>(rush.Hud, "_bossLine").Length > 0)
                    throw new Exception($"{diff}/{round}: unexpected dialogue: {Read<string>(rush.Hud, "_dlgText")} {Read<string>(rush.Hud, "_bossLine")}");
                evolved |= boss is BossMina ? Read<int>(boss, "_pattern") >= 4
                    : (bool)typeof(Enemy).GetField("_form2", Private)!.GetValue(boss)!;
                if (frame >= 90 && frame % 24 == 0 && !Hud.BubblePaused)
                {
                    if (boss is BossMina) Property(Read<MinaPhaseAttacks>(boss, "_caster"), "OpenerCompleted", true);
                    boss.DealDirectDamage(Math.Max(1, boss.BarHp * boss.TotalBars / 35));
                }
                await Frames(1);
            }
            Check(evolved, $"{diff}/{BossRushRoot.BossNames[round]}: evolved form reached without story (HP {boss.HpRatio:0.00})");
            Check(rush.Round == round + 1, $"{diff}: defeat advances exactly once");
            await Frames(3);
            Check(!rush.World.GetChildren().Any(n => n is Enemy or AreaStrike or BossPost), $"{diff}: defeated encounter cleaned up");
        }
        Check(rush.Completed && _game.GetBestTime(BossRushRoot.RecordId, diff).HasValue,
            $"{diff}: all four bosses complete and record a rush result");
        Check(_game.GetBestTime(GameManager.FinalStageId, GameManager.Diff.Easy) == null,
            $"{diff}: rush does not write story clear records");
        if (diff == GameManager.Diff.Easy) { await Frames(75); await Shot("clear"); }
        await Remove(rush);
    }

    private async Task CheckRetry()
    {
        _game.Difficulty = GameManager.Diff.Easy;
        var rush = Mount<BossRushRoot>(BossRushRoot.ScenePath);
        await Wait(() => rush.Boss != null, 180, "retry trial begins");
        Property(rush.Player, "Lives", 0);
        _game.AddScoreShard(8000);
        await Frames(60);
        Check((int)typeof(GameManager).GetField("_gameOverFirst", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)! == 1,
            "game over offers restart and exit, not a single-boss checkpoint");
        await Shot("game_over");
        Input.ActionPress("ui_accept");
        await Frames(2);
        Input.ActionRelease("ui_accept");
        await Wait(() => GetTree().CurrentScene is BossRushRoot next && next != rush && !LoadingScreen.IsActive,
            600, "game-over retry reloads the rush");
        rush = (BossRushRoot)GetTree().CurrentScene;
        Check(rush.Round == 0 && _game.Score == 0 && rush.Player.Lives == _game.StartLives, "retry resets the entire rush");
        var menu = GetNode<PauseMenu>("/root/PauseMenu");
        menu.Open();
        Write(menu, "_confirmYes", true);
        var act = typeof(PauseMenu).GetField("_confirmAct", Private)!;
        act.SetValue(menu, Enum.Parse(act.FieldType, "Restart"));
        Call(menu, "RunConfirm");
        await Wait(() => GetTree().CurrentScene is BossRushRoot next && next != rush && !LoadingScreen.IsActive,
            600, "pause restart reloads the rush");
        rush = (BossRushRoot)GetTree().CurrentScene;
        Check(rush.Round == 0, "pause restart begins with Akari");
        menu.Open();
        Write(menu, "_confirmYes", true);
        act.SetValue(menu, Enum.Parse(act.FieldType, "Leave"));
        Call(menu, "RunConfirm");
        await Wait(() => GetTree().CurrentScene is Hub && !LoadingScreen.IsActive, 600, "pause exit returns home");
        await Remove(GetTree().CurrentScene);
    }

    private async Task Wait(Func<bool> condition, int limit, string message)
    {
        for (int frame = 0; frame < limit && !condition(); frame++) await Frames(1);
        Check(condition(), message);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        string dir = ProjectSettings.GlobalizePath("res://build/boss_rush/shots");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{dir}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
