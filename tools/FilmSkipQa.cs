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
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_film_skip/"), "isolated save data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.MsgCharsPerSec = 300;
            game.AutoAdvanceDialog = false;
            game.SelectedEntry = GameManager.StageEntry.Boss;

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

        // ②最後まで通すと記録が立つ。新規セーブでは既読スキップ（Ctrl）が効かない
        //   （Hud.FastForwarding は「その行が表示時点で既読」を要求する）ので Z を叩いて送り切る。
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
