using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// ゲームオーバーの「ボスから・スコア半分消費」まわりの QA（tools/qa_boss_retry.tscn）。
//   ・スコア半分の持ち越しが再読込を跨いで一度だけ効くこと（選択肢／R 単体／Shift+R／通常 R 長押し）。
//   ・2026-09-23：「ボスから」は**今ランでボス戦に到達したとき**だけ出ること（GameManager.BossReached）。
//       未到達＝2択（最初から／抜ける）・R 単体も最初から（スコア 0）。到達＝従来の3択。
//       到達の印は各 Stage の Step_BossSpawn が立てる＝入口 Boss で建てて回すと立ち、入口 MidBoss（中ボス cameo）では立たない。
//   Freeze で Stage の step は回らないので、主ループでは NotifyBossReached を QA が直接立てる。
public partial class BossRetryQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Property(object obj, string name, object value) => obj.GetType().GetProperty(name)!.SetValue(obj, value);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[BossRetryQA] PASS {message}");
    }

    private GameManager _game = null!;

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            foreach (long score in new long[] { 0, 1, 2, 999, 10000, long.MaxValue })
            {
                Property(_game, "Score", score);
                Check(!_game.BossReached && !_game.PrepareBossRetry() && _game.Score == score,
                    $"score {score}: boss retry is refused before the boss is reached");
                _game.NotifyBossReached();
                Check(_game.PrepareBossRetry(), "boss retry is accepted once the boss was reached this run");
                _game.ResetRun();
                Check(_game.Score == score / 2, $"score {score} retains half without overflow");
                Check(!_game.BossReached, "boss-reached flag is run-scoped and clears with the next run");
                _game.ResetRun();
                Check(_game.Score == 0, "retry score is consumed exactly once");
            }
            Property(_game, "Score", 1000L);
            _game.NotifyBossReached();
            _game.PrepareBossRetry();
            _game.ResetPersistent();
            _game.ResetRun();
            Check(_game.Score == 0, "new game discards a pending retry score");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle", "Stage0" })
            {
                _game.SelectedEntry = GameManager.StageEntry.Start;
                _game.SelectedJob = Job.Melee;
                _game.Difficulty = GameManager.Diff.Hard;
                var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                Freeze(root);
                Property(_game, "Score", 10000L);
                Property(_game, "Impression", 2345L);
                Property(_game, "Followers", 678);

                // 道中（ボス未到達）で倒れた：2択＝「ボスから」が無く、先頭の「最初から」はスコアを持ち越さない。
                Check(!_game.BossReached, $"{scene}: a fresh run has not reached the boss");
                root = await RetryChoice(root, 0, mouse: false, capture: scene == "Akari" ? "menu_unreached" : null);
                Check(_game.Score == 0, $"{scene}: before the boss the first choice is start-over and carries nothing");
                CheckEntry(root, scene, boss: false);
                Property(_game, "Score", 999L);
                root = await RetryKey(root, shift: false, alive: false);
                Check(_game.Score == 0, $"{scene}: R before the boss restarts from the top with zero score");
                CheckEntry(root, scene, boss: false);

                // ここからはボス到達済み（Freeze で Stage の step は回らないので印は QA が立てる）。
                Property(_game, "Score", 10000L);
                _game.NotifyBossReached();
                root = await RetryChoice(root, 0, mouse: false, capture: scene == "Akari" ? "menu" : null);
                Check(_game.Score == 5000, $"{scene}: selected boss retry carries 5000 through scene reload");
                CheckEntry(root, scene, boss: true);
                _game.NotifyBossReached();
                root = await RetryChoice(root, 0, mouse: true);
                Check(_game.Score == 2500, $"{scene}: repeat mouse retry charges half once, not twice");

                Property(_game, "Score", 999L);
                _game.NotifyBossReached();
                root = await RetryKey(root, shift: false, alive: false);
                Check(_game.Score == 499, $"{scene}: R uses the same half-score cost");
                CheckEntry(root, scene, boss: true);
                Property(_game, "Score", 501L);
                _game.NotifyBossReached();
                root = await RetryKey(root, shift: true, alive: false);
                Check(_game.Score == 0, $"{scene}: Shift+R starts over with zero score");
                CheckEntry(root, scene, boss: false);
                Property(_game, "Score", 800L);
                _game.NotifyBossReached();
                root = await RetryChoice(root, 1, mouse: false);
                Check(_game.Score == 0, $"{scene}: start-over choice does not carry retry points");
                Property(_game, "Score", 600L);
                _game.NotifyBossReached();
                root = await RetryKey(root, shift: false, alive: true);
                Check(_game.Score == 0, $"{scene}: normal held-R restart still resets score");
                Property(_game, "Score", 0L);
                _game.NotifyBossReached();
                root = await RetryChoice(root, 0, mouse: false);
                Check(_game.Score == 0, $"{scene}: zero score does not prevent retry");
                Check(_game.Impression == 2345 && _game.Followers == 678, "retry does not spend permanent currencies");
                Check(_game.SelectedJob == Job.Melee && _game.Difficulty == GameManager.Diff.Hard, "character and difficulty survive retry");
                root.QueueFree();
                await Frames(3);
                GameManager.ClearGameOverChoice(null);
                Hud.BubblePaused = false;
            }

            // 到達の印が**実経路**で立つこと：入口 Boss で建てて数フレーム回す＝Step_BossSpawn が NotifyBossReached を呼ぶ。
            //   入口 MidBoss（中ボス cameo）では立たない＝中ボスに着いただけでは「ボスから」は出ない（2択のまま）。
            foreach (var (entry, reached) in new[] { (GameManager.StageEntry.MidBoss, false), (GameManager.StageEntry.Boss, true) })
            {
                _game.SelectedEntry = entry;
                var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                await Frames(3);
                Freeze(root);
                Check(_game.BossReached == reached, $"Akari entry {entry}: BossReached={reached} through the real stage step");
                Property(_game, "Score", 10000L);
                root = await RetryChoice(root, 0, mouse: false, capture: reached ? null : "menu_midboss");
                Check(_game.Score == (reached ? 5000 : 0), $"Akari entry {entry}: first choice {(reached ? "retries the boss for half" : "starts over for free")}");
                CheckEntry(root, "Akari", boss: reached);
                root.QueueFree();
                await Frames(3);
                GameManager.ClearGameOverChoice(null);
                Hud.BubblePaused = false;
            }

            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[BossRetryQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[BossRetryQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private void Freeze(Node2D root)
    {
        root.SetProcess(false);
        ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
        root.GetNode<Node2D>("World").ProcessMode = ProcessModeEnum.Disabled;
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        hud.SetCinematicMode(false);
    }

    private async Task<Node2D> Reloaded(Node2D previous)
    {
        await Frames(4);
        var root = (Node2D)GetTree().CurrentScene;
        Check(root != previous, "scene was actually reloaded");
        Freeze(root);
        Check(root.GetNode<Player>("World/Player").Lives == _game.StartLives && _game.Bombs == _game.StartBombs,
            "lives and bombs are restored independently of score");
        Check(_game.Combo == 0 && _game.RunHitCount == 0, "other run state resets normally");
        return root;
    }

    // selected は**表示された**選択肢の添字（到達済み: 0=ボスから/1=最初から/2=抜ける、未到達: 0=最初から/1=抜ける）。
    // capture: 非 null なら盤面のスクリーンショットを build/qa_story/boss_retry/<capture>_<W>x<H>.png に保存。
    private async Task<Node2D> RetryChoice(Node2D root, int selected, bool mouse, string? capture = null)
    {
        var hud = root.GetNode<Hud>("Hud");
        Property(root.GetNode<Player>("World/Player"), "Lives", 0);
        hud.SetLives(0);
        bool held = false;
        long score = _game.Score;
        bool reached = _game.BossReached;
        GameManager.HandleGameOverExit(root, hud, ref held);
        var choice = (ChoiceOverlay)typeof(GameManager).GetField("_gameOverChoice", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        choice.SetProcess(false);
        choice._Process(2d);
        string[] choices = Read<string[]>(choice, "_choices");
        Check(choices.Length == (reached ? 3 : 2), reached ? "three choices once the boss was reached" : "two choices before the boss: no boss retry offered");
        Check(choices[0].Contains("半分消費") == reached,
            reached ? "half-score cost is visible before confirmation" : "the boss-retry line is absent before the boss");
        Check(choices[^1].Contains("抜ける"), "the last (silent-default) choice stays 'leave the stage'");
        string prompt = Read<string>(hud, "_gameOverPrompt");
        Check(reached ? (!Pad.ShowKeyboard || prompt.Contains("ボスから")) : !prompt.Contains("ボスから"),
            "the keyboard hint advertises R=boss retry only when it is offered");
        var rect = (Rect2)typeof(ChoiceOverlay).GetMethod("RowRect", Private)!.Invoke(choice, new object[] { selected })!;
        Check(rect.Position.X >= Field.DLeft && rect.End.X <= Field.DRight, "retry option fits inside the playfield");
        Check(_game.Score == score, "opening the choices does not charge score");
        if (capture != null)
        {
            if (reached) root.GetNode<StageBackground>("StageBackground").EnterBoss();
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                await Frames(8);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng(ProjectSettings.GlobalizePath($"res://build/qa_story/boss_retry/{capture}_{size.X}x{size.Y}.png")) == Error.Ok, $"retry screenshot {capture}");
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
        Property(choice, "Selected", selected);
        if (mouse)
        {
            Vector2 oldMouse = Pad.MousePos();
            PadField("_mousePos", rect.GetCenter());
            PadField("_usingMouse", true);
            PadField("_mL", true);
            PadField("_mLPrev", false);
            choice._Process(0.016d);
            PadField("_mousePos", oldMouse);
            PadField("_mL", false);
            PadField("_usingMouse", false);
        }
        else
        {
            Input.ActionPress("ui_accept");
            choice._Process(0.016d);
            Input.ActionRelease("ui_accept");
        }
        choice._Process(0.51d);
        Check(choice.Decided && choice.Selected == selected, "choice confirms through its input handler");
        Check(GameManager.HandleGameOverExit(root, hud, ref held), "confirmed choice initiates reload");
        return await Reloaded(root);
    }

    private async Task<Node2D> RetryKey(Node2D root, bool shift, bool alive)
    {
        Property(root.GetNode<Player>("World/Player"), "Lives", alive ? _game.StartLives : 0);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, Pressed = shift });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = true });
        await Frames(2);
        Check(Input.IsKeyPressed(Key.R) && !Pad.UiBlocked(root), "retry shortcut reaches the stage input handler");
        root._Process(alive ? RetryHold.HoldTime + 0.01d : 0.016d);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = false });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, Pressed = false });
        return await Reloaded(root);
    }

    private void CheckEntry(Node2D root, string scene, bool boss)
    {
        bool checkpointStage = scene is "Akari" or "Koharu" or "Rei";
        if (checkpointStage)
        {
            var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
            int step = Read<int>(stage, "_step");
            Check(boss ? step >= 10 : step == 1, $"{scene}: retry keeps the requested entry point");
        }
        // 再読込は ResetRun で印を下ろす。ボス入口（3ステージ）なら Reloaded が待った数フレームの間に
        //   Step_BossSpawn が実経路で立て直す＝ボス戦で倒れ続けても「ボスから」が消えない。
        //   最初から／FINAL・練習（bossCheckpoint:false＝入口 Start）では下りたまま。
        Check(_game.BossReached == (boss && checkpointStage), $"{scene}: boss-reached flag after reload follows the entry point");
        Check(_game.SelectedEntry == GameManager.StageEntry.Start, "entry selection does not leak into another stage");
    }

    private static void PadField(string name, object value)
        => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
