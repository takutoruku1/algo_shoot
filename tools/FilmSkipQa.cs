using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// FilmSkipQa : 「一度見たムービーは2回目以降スキップできる」（2026-09-22 ユーザー要望）の回帰テスト。
//
// 見るもの:
//   ①初回は **スキップが開かない**（X を長押ししても畳まれない・ヒントも出ない）
//   ②初回を最後まで通すと **once_film_<id> が立つ**
//   ③2回目は X 長押し 0.45 秒で畳まれ、**_completed が呼ばれる**（進行が飛ばない）
//   ④長押しの途中で離すと **畳まれない**（誤爆防止のキャンセル）
//   ⑤記録が **セーブをまたいで残る**（idleDialogSeen に載る／小話プールの戻しでも消えない）
//   ⑥FINAL のフェーズ間（MinaPhaseScene）も同じ作法で働く
//
// 起動: res://tools/qa_film_skip.tscn -- --quit
public partial class FilmSkipQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string field, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value, Type? type = null)
        => (type ?? obj.GetType()).GetField(field, Private)!.SetValue(obj, value);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[FilmSkipQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.MsgCharsPerSec = 300;
            game.AutoAdvanceDialog = false;
            game.SelectedJob = Job.Tank;
            game.SelectedEntry = GameManager.StageEntry.Boss;
            CheckMouseInput(game);
            await Frames(3);

            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            await Frames(1);
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var hud = root.GetNode<Hud>("Hud");
            var world = root.GetNode<Node2D>("World");
            // ステージ進行とルート（リトライ長押し等）を止める。ルートを生かしたままだと
            //   Pad.ConsumeUi を毎フレーム取られてフィルムの _Process が UiBlocked で素通りする。
            root.GetNode<Node>("StageAkari").SetProcess(false);
            root.SetProcess(false);
            world.ProcessMode = ProcessModeEnum.Inherit;
            await Frames(10);

            await CheckStoryFilm(game, hud, world);
            await CheckPersistence(game);
            await CheckMinaPhase(game, hud, world);
            await CheckMouseScenes(game, hud, world);
            await CheckPolledMouseScene(game, hud, world);

            root.QueueFree();
            await Frames(8);
            GetNode<BulletPool>("/root/Pool").DespawnAll();
            Audio.Instance?.StopMusic(0);
            await Frames(5);
            GD.Print("[FilmSkipQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[FilmSkipQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private static void Mouse(Vector2 position, bool down)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(Pad).GetField("_mousePos", flags)!.SetValue(null, position);
        typeof(Pad).GetField("_mLPrev", flags)!.SetValue(null, Pad.MouseDown());
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, down);
    }

    private static void CheckMouseInput(GameManager game)
    {
        Vector2 inside = FilmSkip.ButtonRect.GetCenter(), outside = new(200, 300);
        FilmSkip.MarkSeen(game, "mouse_qa");
        var skip = new FilmSkip();
        Mouse(inside, true);
        skip.Begin(game, "mouse_qa");
        Check(!skip.Update(1), "a held mouse button cannot skip a newly opened scene");
        Mouse(inside, false); skip.Update(0.01);
        Mouse(inside, true);
        Check(!skip.Update(0.2) && skip.Progress > 0 && !Pad.AdvanceHeld(),
            "holding the button fills progress without advancing dialogue");
        Mouse(inside, false);
        Check(!skip.Update(0.01) && skip.Progress == 0, "releasing early cancels mouse skip");
        Mouse(outside, true); skip.Update(0.1);
        Mouse(inside, true);
        Check(!skip.Update(1) && skip.Progress == 0, "dragging a dialogue press onto skip cannot activate it");
        Mouse(inside, false); skip.Update(0.01);
        Mouse(inside, true); skip.Update(0.2);
        Mouse(outside, true);
        Check(!skip.Update(1) && skip.Progress == 0 && !Pad.AdvanceHeld(),
            "dragging out cancels skip without leaking the held click into dialogue");
        Mouse(inside, true);
        Check(!skip.Update(1), "returning while held requires a fresh press");
        Mouse(inside, false); skip.Update(0.01);
        Mouse(inside, true);
        Check(skip.Update(0.5), "a fresh mouse hold completes skip");
        Mouse(inside, false); skip.Update(0.01);
        skip.Begin(game, "unseen_mouse_qa");
        Mouse(inside, true);
        Check(!skip.Update(1), "mouse input respects the film's availability");
        Mouse(outside, false);
    }

    private async Task CheckMouseScenes(GameManager game, Hud hud, Node2D world)
    {
        int completed = 0;
        AkariStoryFilm.Play(hud, world, false, () => completed++);
        var story = (StoryFilm)GetTree().GetFirstNodeInGroup("storyfilm");
        await CheckMouseScene(story, typeof(StoryFilm), hud, world, () => completed);

        completed = 0;
        MinaPhaseScene.Play(hud, world, 1, () => completed++);
        var phase = (MinaPhaseScene)GetTree().GetFirstNodeInGroup("mina_phase_scene");
        await CheckMouseScene(phase, typeof(MinaPhaseScene), hud, world, () => completed);

        completed = 0;
        int arrived = 0;
        const string line = "マウスでスキップを確認します。";
        game.MarkLineRead(line);
        FilmSkip.MarkSeen(game, "boss_intro_akari_mina");
        CameoIntroScene.PlayBoss(hud, world, "akari", new[] { (1, line, "") }, () => completed++, () => arrived++);
        var boss = (CameoIntroScene)GetTree().GetFirstNodeInGroup("boss_intro");
        await CheckMouseScene(boss, typeof(CameoIntroScene), hud, world, () => completed);
        Check(arrived == 1, "mouse skip still prepares the boss exactly once");
    }

    private async Task CheckMouseScene(Node2D scene, Type type, Hud hud, Node2D world, Func<int> completed)
    {
        scene.SetProcess(false);
        await Frames(3);
        var skip = Read<FilmSkip>(scene, "_skip", type);
        Check(skip.Available, $"{type.Name}: mouse skip is available");
        Vector2 center = FilmSkip.ButtonRect.GetCenter();
        Mouse(center, false); scene._Process(0.01);
        Mouse(center, true); scene._Process(0.1);
        Check(!Read<bool>(scene, "_held", type) && skip.Progress > 0,
            $"{type.Name}: the skip press is captured before dialogue reads it");
        scene._Process(0.4);
        Check(Read<bool>(scene, "_leaving", type), $"{type.Name}: mouse hold starts the normal exit");
        scene._Process(1);
        Check(completed() == 0 && world.ProcessMode == ProcessModeEnum.Disabled,
            $"{type.Name}: combat stays paused until the mouse is released");
        Mouse(center, false); scene._Process(0.01);
        await Frames(3);
        Check(completed() == 1 && !IsInstanceValid(scene) && !hud.CinematicMode
            && world.ProcessMode == ProcessModeEnum.Inherit,
            $"{type.Name}: releasing the mouse completes once and restores combat");
    }

    private async Task CheckPolledMouseScene(GameManager game, Hud hud, Node2D world)
    {
        int completed = 0;
        const string line = "わたくしはミナです。あなたが待っている方ではありません。\nでも、ここでお話を聞かせてほしいのです。";
        game.MarkLineRead(line);
        FilmSkip.MarkSeen(game, "boss_intro_akari_mina");
        CameoIntroScene.PlayBoss(hud, world, "akari", new[] { (1, line, "") }, () => completed++, () => { });
        var scene = (CameoIntroScene)GetTree().GetFirstNodeInGroup("boss_intro");
        await WaitUntil(() => Read<bool>(scene, "_talking"), 900);
        var skip = Read<FilmSkip>(scene, "_skip");
        Vector2 center = FilmSkip.ButtonRect.GetCenter();
        SendMouse(center, false);
        await Frames(3);
        Check(Pad.MousePos().DistanceTo(center) < 1, $"polled pointer matches the displayed skip button ({Pad.MousePos()})");
        SendMouse(center, true);
        await Frames(8);
        Check(skip.Progress > 0 && !Read<bool>(scene, "_held"), "mouse input events fill the skip bar without advancing dialogue");
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng("user://mouse_skip_progress.png") == Error.Ok, "save rendered mouse skip progress");
        }
        await WaitUntil(() => Read<bool>(scene, "_leaving"), 120);
        Check(completed == 0 && world.ProcessMode == ProcessModeEnum.Disabled, "held mouse keeps the battle paused after skip");
        SendMouse(center, false);
        await WaitUntil(() => completed == 1, 120);
        Check(!IsInstanceValid(scene) && world.ProcessMode == ProcessModeEnum.Inherit, "polled mouse skip restores battle after release");
    }

    private void SendMouse(Vector2 designPosition, bool down)
    {
        if (DisplayServer.GetName() != "headless") GetViewport().WarpMouse(designPosition * UiKit.Scale);
        Vector2 position = GetViewport().GetFinalTransform() * (designPosition * UiKit.Scale);
        Input.ParseInputEvent(new InputEventMouseButton {
            ButtonIndex = MouseButton.Left, Pressed = down, Position = position, GlobalPosition = position,
        });
        Input.FlushBufferedEvents();
    }

    // ①〜④を回想（あかりの memory）で確認する。
    private async Task CheckStoryFilm(GameManager game, Hud hud, Node2D world)
    {
        string key = FilmSkip.SeenKey("akari_memory");
        Check(!game.IsIdleDialogSeen(key), "a fresh save has not seen the flashback");

        // ①未読はスキップが開かない。X を長押ししても畳まれず、ヒントも出ない。
        bool ended = false;
        AkariStoryFilm.Play(hud, world, aftermath: false, completed: () => ended = true);
        await Frames(5);
        var film = GetTree().GetFirstNodeInGroup("storyfilm") as StoryFilm;
        Check(film != null, "flashback starts");
        var skip = Read<FilmSkip>(film!, "_skip", typeof(StoryFilm));
        Check(!skip.Available, "the first viewing does not offer skipping");
        KeyEvent(Key.X, true);
        await Frames(90);   // 0.45 秒（RetryHold.HoldTime）を大きく超える
        KeyEvent(Key.X, false);
        Check(IsInstanceValid(film) && !ended && !Read<bool>(film!, "_leaving", typeof(StoryFilm)),
              "holding skip during the first viewing does nothing");

        // ②最後まで通すと記録が立つ。「初見を最後まで読んだ」を再現するため Z を叩いて送り切る
        await AdvanceUntil(() => ended, film!);
        await Frames(5);
        Check(ended && game.IsIdleDialogSeen(key), "finishing the flashback records it as seen");

        // ④2回目：長押しの途中で離すと畳まれない。
        ended = false;
        AkariStoryFilm.Play(hud, world, aftermath: false, completed: () => ended = true);
        await Frames(5);
        film = GetTree().GetFirstNodeInGroup("storyfilm") as StoryFilm;
        skip = Read<FilmSkip>(film!, "_skip", typeof(StoryFilm));
        Check(skip.Available, "the second viewing offers skipping");
        KeyEvent(Key.X, true);
        await Frames(10);   // 0.45 秒に満たない
        KeyEvent(Key.X, false);
        await Frames(10);
        Check(IsInstanceValid(film) && !ended && !Read<bool>(film!, "_leaving", typeof(StoryFilm)),
              "releasing the hold early cancels the skip");

        // ③2回目：長押しを満たすと畳まれ、_completed が呼ばれる（呼び元へ必ず戻る）。
        KeyEvent(Key.X, true);
        await WaitUntil(() => ended, 300);
        KeyEvent(Key.X, false);
        Check(ended, "holding skip on a seen flashback completes it");
        Check(!hud.CinematicMode && world.ProcessMode == ProcessModeEnum.Inherit,
              "skipping restores the world and HUD exactly like reading it through");
        Check(GetTree().GetNodesInGroup("storyfilm").Count == 0, "the skipped film is freed");
    }

    // ⑤記録の永続。セーブ→読み直しで残ること、小話プールの戻しで消えないこと。
    private async Task CheckPersistence(GameManager game)
    {
        string key = FilmSkip.SeenKey("akari_memory");
        game.ResetIdleDialogSeen();
        Check(game.IsIdleDialogSeen(key), "small-talk pool resets keep the film record (once_ prefix)");
        game.SaveToSlot(0);
        game.ResetIdleDialogSeen();
        Write(game, "_idleDialogSeen", new System.Collections.Generic.HashSet<string>());
        Check(!game.IsIdleDialogSeen(key), "the in-memory record is cleared for the round trip");
        Check(game.LoadFromSlot(0) && game.IsIdleDialogSeen(key), "the film record survives saving and loading");
        await Frames(1);
    }

    // ⑥FINAL のフェーズ間カットシーンも同じ作法で働く。
    private async Task CheckMinaPhase(GameManager game, Hud hud, Node2D world)
    {
        string key = FilmSkip.SeenKey($"mina_phase_1_{game.JobDef.CharacterId}");
        Check(!game.IsIdleDialogSeen(key), "the phase cutscene starts unseen");
        bool ended = false;
        MinaPhaseScene.Play(hud, world, 1, () => ended = true);
        await Frames(5);
        var scene = GetTree().GetFirstNodeInGroup("mina_phase_scene") as MinaPhaseScene;
        Check(scene != null && !Read<FilmSkip>(scene!, "_skip").Available,
              "the first phase cutscene does not offer skipping");
        await AdvanceUntil(() => ended);
        await Frames(5);
        Check(ended && game.IsIdleDialogSeen(key), "finishing the phase cutscene records it as seen");
        Check(!game.IsIdleDialogSeen(FilmSkip.SeenKey($"mina_phase_2_{game.JobDef.CharacterId}")),
              "each phase is recorded separately");

        ended = false;
        MinaPhaseScene.Play(hud, world, 1, () => ended = true);
        await Frames(5);
        scene = GetTree().GetFirstNodeInGroup("mina_phase_scene") as MinaPhaseScene;
        Check(Read<FilmSkip>(scene!, "_skip").Available, "the second phase cutscene offers skipping");
        KeyEvent(Key.X, true);
        await WaitUntil(() => ended, 300);
        KeyEvent(Key.X, false);
        Check(ended, "holding skip on a seen phase cutscene completes it");
        Check(!hud.CinematicMode && world.ProcessMode == ProcessModeEnum.Inherit,
              "skipping the phase cutscene restores the battle state");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // Z のパルスで会話を送り切る（StoryFilmQa.AdvanceUntil と同じ作法）。
    private async Task AdvanceUntil(Func<bool> condition, Node? probe = null)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            KeyEvent(Key.Z, true);
            await Frames(16);
            KeyEvent(Key.Z, false);
            await Frames(2);
        }
        if (!condition())
        {
            string where = probe != null && IsInstanceValid(probe)
                ? $" (line={Read<int>(probe, "_line", typeof(StoryFilm))}"
                  + $" started={Read<bool>(probe, "_started", typeof(StoryFilm))}"
                  + $" fadeT={Read<double>(probe, "_fadeT", typeof(StoryFilm)):0.00})"
                : " (film already freed)";
            throw new Exception("Timed out advancing the film" + where);
        }
    }

    private async Task WaitUntil(Func<bool> condition, int limit)
    {
        for (int i = 0; i < limit && !condition(); i++) await Frames(1);
        if (!condition()) throw new Exception("Timed out waiting for the film");
    }

    private static void KeyEvent(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });
}
