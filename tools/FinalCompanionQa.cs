using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// FinalCompanionQa : FINAL を三人（あかり／こはる／レイ）で潜ったとき、段間〜撃破後〜エピローグ冒頭に
//   「潜っていない子」の名前・立ち絵が一対一の場面へ紛れ込まないかを見る（2026-09-27 作者報告
//   「ミナ戦であかりを使ってクリアしたときにレイがでてくる」＝F3 の邂逅が潜行キャラに依らずレイ固定だった）。
//   ヘッドレス可（撮影しない）。Normal・3面クリア済み（直前にクリアしたのはレイ面）の状態から、
//   MinaPhaseScene の四段を実物で再生 → 撃破（Redeem）→ 回想 → F3 → アフター → Final（F4）→ Epilogue の E5b 末尾まで送る。
//   会話ログ（Hud.Backlog）に積まれた行を場面ごとに控え、Hud 経由の行は表示中の立ち絵パスも拾う。
//   群像の場面（アフターの「三人の声」と CG に合わせた帰り道／エピローグ E5b の四人）は設計どおり三人が出るので、
//   一覧に出すだけで判定には入れない（ensemble）。それ以外は ミナ／あなた／投稿／ナレ／潜行キャラ本人だけ。
//   使い方: res://tools/qa_final_companion.tscn [-- --jobs=melee,heal,magic]
public partial class FinalCompanionQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string field, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value, Type? type = null)
        => (type ?? obj.GetType()).GetField(field, Private)!.SetValue(obj, value);
    private static void Call(object obj, string method, Type? type = null)
        => (type ?? obj.GetType()).GetMethod(method, Private)!.Invoke(obj, null);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[FinalCompQA] PASS {message}");
    }

    private readonly record struct Seen(string Ctx, Hud.LineKind Kind, string Speaker, string Text, string Portrait);
    private static readonly string[] Heroines = { "akari", "koharu", "rei" };
    private readonly List<Seen> _seen = new();
    private int _logIndex;
    private string _ctx = "stage";

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var jobs = new List<Job> { Job.Melee, Job.Heal, Job.Magic };
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--jobs="))
                jobs = a["--jobs=".Length..].Split(',').Select(s => Jobs.Parse(s.Trim()) ?? Job.Tank).ToList();
        if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"))
        {
            GD.PushError("[FinalCompQA] FAIL save data is not isolated (set APPDATA under build/qa_story/)");
            GetTree().Quit(1);
            return;
        }
        int failures = 0;
        foreach (var job in jobs)
        {
            try { await RunJob(job); }
            catch (Exception ex)
            {
                failures++;
                GD.PushError($"[FinalCompQA] FAIL job={job}: {ex.Message}");
            }
            GetTree().Paused = false;
            var cur = GetTree().CurrentScene;
            if (cur != null && cur != this) cur.QueueFree();
            await Frames(10);
        }
        GD.Print(failures == 0 ? "[FinalCompQA] ALL PASS" : $"[FinalCompQA] {failures} JOB(S) FAILED");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private async Task RunJob(Job job)
    {
        Engine.MaxFps = 60;
        Engine.TimeScale = 4;
        var def = Jobs.Get(job);
        GD.Print($"[FinalCompQA] ===== job={def.CharacterName}({job}) =====");
        var game = GetNode<GameManager>("/root/Game");
        game.Difficulty = GameManager.Diff.Normal;
        foreach (var s in GameManager.Stages) game.CompleteStage(s.Id);   // 3面クリア済み（最後＝レイ面が直前のクリア）
        game.SelectedJob = job;
        game.MsgCharsPerSec = 300;
        game.AutoAdvanceDialog = false;
        _seen.Clear();
        Hud.ClearBacklog();
        _logIndex = 0;
        _ctx = "stage";

        var root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<MinaRoot>();
        await Frames(1);
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        var hud = root.Hud; var world = root.World; var player = root.Player; var stage = root.Stage;
        await Frames(10);
        Check(player.CharacterId == def.CharacterId, $"{def.CharacterName}: FINAL uses the dive character");
        await AdvanceUntil(() => Read<int>(stage, "_step") == 2);
        player.SetPhysicsProcess(false);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        await WaitUntil(() => Read<Spawner?>(stage, "_echoSpawner") is { } wave && wave.SpawnedCount >= 3, 800);
        foreach (var node in world.GetChildren()) if (node is MidEnemy echo) echo.Purify();
        // 道中（step 2）は 2026-09-27 に残響の三波へ伸びた。この QA が見たいのは段間と撃破後の会話なので、
        //   浄化数を直に積んで波のゲートだけ通す（目標 StageTarget は跨がない＝StageCleared は立てない）。
        var purifiedProp = typeof(GameManager).GetProperty("PurifiedCount")!;
        for (int i = 0; i < 900 && Read<int>(stage, "_step") == 2; i++)
        {
            // 最後の波は予定数が湧き切るまでボスへ渡らない。この走行は自機を止めていて敵が死なないので、
            //   同時上限で湧きが頭打ちになる＝予定数にも届かない。湧きの予定数も出た数まで詰めて通す。
            if (Read<Spawner?>(stage, "_echoSpawner") is { } wave && IsInstanceValid(wave))
                wave.SpawnLimit = Math.Max(1, wave.SpawnedCount);
            purifiedProp.SetValue(game, Math.Min(game.PurifiedCount + 1, game.StageTarget - 1));
            await Frames(1);
        }
        // 道中（step 2）→ ボス出現（step 3＝Step_BossSpawn）はチェックポイント入口「ボスから」の追加で
        //   別 step に割れた。step が 3 になったフレームではまだミナが建っていないので、ノードを待つ。
        await WaitUntil(() => world.GetNodeOrNull<BossMina>("BossMina") != null, 200);
        var boss = world.GetNode<BossMina>("BossMina");
        await AdvanceUntil(() => GetTree().GetFirstNodeInGroup("boss_intro") == null
            && Read<int>(stage, "_step") == 4);
        var caster = Read<MinaPhaseAttacks>(boss, "_caster");
        caster.SetProcess(false);
        caster.CancelPendingAttacks();

        // 段間シーン（四段）を実物で再生して、各段の返し手を拾う。
        for (int phase = 1; phase <= 4; phase++)
        {
            bool done = false;
            _ctx = $"phase{phase}";
            MinaPhaseScene.Play(hud, world, phase, () => done = true);
            await AdvanceUntil(() => done);
            await Frames(3);
        }
        _ctx = "stage";

        // 撃破 → 回想（初回）→ F3 → アフター → Final。
        Write(boss, "_hp", 0, typeof(Enemy));
        Call(boss, "Redeem", typeof(Enemy));
        await AdvanceUntil(() => GetTree().CurrentScene?.SceneFilePath == "res://Final.tscn");
        Check(game.SelectedJob == job, $"{def.CharacterName}: dive character survives into Final");
        await AdvanceUntil(() => GetTree().CurrentScene?.SceneFilePath == "res://Epilogue.tscn");
        var epilogue = GetTree().CurrentScene;
        await AdvanceUntil(() => Read<int>(epilogue, "_phase") != 0);   // E5b（見上げる）を送り切るまで
        Check(game.SelectedJob == job, $"{def.CharacterName}: dive character survives into Epilogue");

        // ── 集計 ──
        foreach (var s in _seen)
            GD.Print($"[FinalCompQA] {def.CharacterId} [{s.Ctx}] kind={s.Kind} who={(s.Speaker.Length == 0 ? "-" : s.Speaker)}"
                     + $" face={(s.Portrait.Length == 0 ? "-" : s.Portrait)} | {s.Text.Replace("\n", " / ")}");
        var foreignIds = Heroines.Where(h => h != def.CharacterId).ToArray();
        var foreignNames = new HashSet<string>(Jobs.All.Where(j => foreignIds.Contains(j.CharacterId)).Select(j => j.CharacterName));
        static bool Ensemble(Seen s) => s.Ctx is "film_aftermath" or "epilogue";
        bool Foreign(Seen s) => foreignNames.Contains(s.Speaker) || foreignIds.Any(id => s.Portrait.Contains($"/{id}_"));
        var leaks = _seen.Where(s => !Ensemble(s) && Foreign(s)).ToList();
        foreach (var s in leaks)
            GD.Print($"[FinalCompQA] LEAK {def.CharacterId} [{s.Ctx}] who={s.Speaker} face={s.Portrait} | {s.Text}");
        foreach (var g in _seen.Where(s => Ensemble(s) && Foreign(s)).GroupBy(s => s.Ctx + ":" + s.Speaker))
            GD.Print($"[FinalCompQA] ensemble {def.CharacterId} {g.Key} x{g.Count()}");
        for (int phase = 1; phase <= 4; phase++)
        {
            string ctx = $"phase{phase}";
            Check(_seen.Any(s => s.Ctx == ctx && s.Speaker == def.CharacterName),
                $"{def.CharacterName}: phase {phase} is answered by {def.CharacterName}");
        }
        Check(_seen.Any(s => s.Ctx == "f3" && s.Speaker == def.CharacterName),
            $"{def.CharacterName}: F3 (after the defeat) is answered by {def.CharacterName}");
        Check(_seen.Any(s => s.Ctx == "final") && _seen.Any(s => s.Ctx == "epilogue"),
            $"{def.CharacterName}: reached Final and Epilogue");
        Check(leaks.Count == 0, $"{def.CharacterName}: no other heroine outside the ensemble scenes ({leaks.Count} leak(s))");
    }

    // 1フレームごとに、会話ログへ増えた行を場面ラベル付きで控える（Hud 経由なら表示中の立ち絵も）。
    private void Capture()
    {
        string ctx = CurrentCtx();
        var log = Hud.Backlog;
        if (log.Count < _logIndex) _logIndex = 0;
        var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
        for (; _logIndex < log.Count; _logIndex++)
        {
            var l = log[_logIndex];
            string portrait = "";
            if (hud != null && ctx is not ("final" or "epilogue"))
                portrait = Read<Texture2D?>(hud, "_dlgPortrait")?.ResourcePath ?? "";
            _seen.Add(new Seen(ctx, l.Kind, l.Speaker, l.Text, portrait));
        }
        if (log.Count > 150) { Hud.ClearBacklog(); _logIndex = 0; }
    }

    private string CurrentCtx()
    {
        string scene = GetTree().CurrentScene?.SceneFilePath ?? "";
        if (scene == "res://Final.tscn") return "final";
        if (scene == "res://Epilogue.tscn") return "epilogue";
        if (_ctx.StartsWith("phase")) return _ctx;
        if (GetTree().GetFirstNodeInGroup("storyfilm") is MinaStoryFilm film)
            return Read<bool>(film, "_aftermath", typeof(StoryFilm)) ? "film_aftermath" : "film_memory";
        if (GetTree().CurrentScene is MinaRoot root && IsInstanceValid(root.World)
            && root.World.GetNodeOrNull<BossMina>("BossMina") is { IsPurified: true })
            return "f3";
        return "stage";
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Capture();
        }
    }

    private async Task AdvanceUntil(Func<bool> condition)
    {
        for (int i = 0; i < 800 && !condition(); i++)
        {
            KeyEvent(Key.Z, true);
            await Frames(8);
            KeyEvent(Key.Z, false);
            await Frames(2);
        }
        if (!condition()) throw new Exception($"Timed out advancing dialogue (ctx={CurrentCtx()})");
    }

    private async Task WaitUntil(Func<bool> condition, int frames)
    {
        for (int i = 0; i < frames && !condition(); i++) await Frames(1);
        if (!condition()) throw new Exception("Timed out waiting");
    }

    private static void KeyEvent(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });
}
