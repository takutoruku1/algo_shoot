using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

// FinalJobLockQa : FINAL 初挑戦のミナ封じ（2026-09-17 ユーザー指示「ラスボスに初めて入るときは
//   ミナは使えなくしてほしい」）の自動検証。
//     (a) FINAL 未クリアのとき、FINAL のカードを開いた状態ではアカウント一覧にミナが出ない
//     (b) 結び手のまま FINAL のカードで Z ／ 自動ダイブしても、ミナのまま潜らない
//     (c) FINAL をクリアした記録（ClearTimes の "final_*"）があれば、ミナでも入れる
//     (d) 三人の面（STAGE1〜3）のアカウント一覧は従来どおり全員出る
//     (e) --job= 固定中（JobForcedByCmdline）は QA のため素通し
//   実行: Godot --headless --path . res://tools/qa_final_job_lock.tscn
public partial class FinalJobLockQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static string Mode(Hub hub) => Read<object>(hub, "_mode").ToString()!;

    private int _fails;
    private void Check(bool ok, string message)
    {
        if (ok) GD.Print($"[FinalJobLockQA] PASS {message}");
        else { _fails++; GD.PrintErr($"[FinalJobLockQA] FAIL {message}"); }
    }

    public override async void _Ready()
    {
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"))
                throw new Exception("Use isolated QA save data.");
            var game = GetNode<GameManager>("/root/Game");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.ShopTutorialSeen = true;
            foreach (var job in Jobs.All) game.MarkIdleDialogSeen($"once_companion_select_{job.CharacterId}");
            game.MarkIdleDialogSeen("once_phone_home");
            game.MarkIdleDialogSeen("once_sns_intro");
            GetNode<PauseMenu>("/root/PauseMenu").SetProcess(false);
            var cleared = Read<HashSet<string>>(game, "_cleared");
            foreach (var stage in GameManager.Stages) cleared.Add(stage.Id);
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);

            Check(game.AllStoryCleared && !game.IsFinalCleared, "three stages cleared but FINAL is still untouched");
            Check(game.IsMinaLockedForFinal, "Mina is locked for the first FINAL attempt");
            await CheckHomeTab(game);

            var hub = await OpenHub(game);
            int final = FindFinal(hub);
            Check(final >= 0, $"the FINAL card is on the timeline (index {final})");
            CheckFinalAccount(hub, final, false);
            Write(hub, "_sel", final);
            Call(hub, "UpdateFeedScrollTarget");
            await Frames(20);
            await Shot("final_masked_timeline");

            // (a) FINAL のカードを開いた状態のアカウント一覧にミナが出ない。
            game.SelectedJob = Job.Tank;
            await ConfirmZ(hub);
            Check(Mode(hub) == "Detail", "the FINAL card opens its post detail");
            await Frames(20);
            await Shot("final_detail_locked");     // ★スクショ：ミナのままで開いた FINAL（拒否の理由が載る）
            ClickAt(hub, FinalButtonRect(hub), "ProcessDetail", 0.01);
            Check(Mode(hub) == "Job", "mouse confirmation opens the rescue account switch");
            if (Mode(hub) != "Job") throw new Exception("FINAL has no working mouse entry button.");
            var choices = Read<JobTuning[]>(hub, "_jobChoices");
            Check(Array.TrueForAll(choices, j => j.Id != Job.Tank), "the FINAL account list drops Mina");
            Check(choices.Length == Jobs.All.Length - 1, $"the other three remain selectable ({choices.Length})");
            await Frames(20);
            await Shot("final_accounts_without_mina");   // ★スクショ：ミナが選べないアカウント切り替え
            await Keypress(hub, Key.X);
            Check(Mode(hub) == "Detail", "cancelling returns to the FINAL detail");

            // (b) 結び手のままの Z はダイブせず、アカウント切り替えへ送られる。
            game.SelectedJob = Job.Tank;
            await ConfirmZ(hub);
            Check(!Read<bool>(hub, "_dived"), "confirming as Mina does not dive into FINAL");
            Check(Mode(hub) == "Job", "the refusal opens the account switch instead of dead-ending");
            Check(Read<string>(hub, "_toast") == GameManager.MinaFinalLockHint,
                $"the hint is shown: {Read<string>(hub, "_toast")}");

            // (b2) 自動ダイブ（--demo/--qa の DiveAuto）も結び手のまま潜らない。Dive は実際にシーンを
            //   切り替えるので、この確認だけ最後に回す（以降の検証はハブが生きている前提のため）。
            hub.QueueFree();
            await Frames(5);

            foreach (Job job in new[] { Job.Melee, Job.Heal, Job.Magic })
                await CheckMouseEntry(game, job);

            // (c) 他ジョブなら通る＆(d) 三人の面は従来どおり。
            game.SelectedJob = Job.Melee;
            hub = await OpenHub(game);
            final = FindFinal(hub);
            Write(hub, "_sel", final);
            Call(hub, "UpdateFeedScrollTarget");
            await Settle(hub);
            await ConfirmZ(hub);
            Check(Mode(hub) == "Detail", $"akari opens the FINAL detail (mode={Mode(hub)})");
            Call(hub, "OpenJob");
            Check(Read<JobTuning[]>(hub, "_jobChoices").Length == Jobs.All.Length - 1, "akari also sees the three-account list");
            await Keypress(hub, Key.X);
            Write(hub, "_mode", Enum.Parse(typeof(Hub).GetNestedType("Mode", Private)!, "Detail"));
            await ConfirmZ(hub);
            Check(Read<bool>(hub, "_dived"), "akari dives into FINAL");
            Check(game.PendingStageScene == "res://MinaBattle.tscn", $"the FINAL scene is queued ({game.PendingStageScene})");
            await WaitForFinal();
            Check(GetTree().CurrentScene is MinaRoot, $"the FINAL scene actually loads ({GetTree().CurrentScene?.GetType().Name})");
            GetTree().CurrentScene?.QueueFree();
            await Frames(8);

            hub = await OpenHub(game);
            int voice = FindVoice(hub);
            Check(voice >= 0, "a STAGE card is available");
            Write(hub, "_sel", voice);
            Call(hub, "UpdateFeedScrollTarget");
            await Frames(10);
            Call(hub, "OpenJob");
            Check(Read<JobTuning[]>(hub, "_jobChoices").Length == Jobs.All.Length,
                "the three story stages still offer every account including Mina");
            hub.QueueFree();
            await Frames(5);

            // (c2) FINAL のクリア記録が入れば周回＝ミナでも入れる。記録は既存の ClearTimes だけを使う。
            game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Normal, 150f);
            Check(game.IsFinalCleared && !game.IsMinaLockedForFinal, "a FINAL clear time reopens Mina");
            game.SaveToSlot(0);
            game.ClearTimes.Clear();
            Check(!game.IsFinalCleared, "clearing the in-memory record locks Mina again");
            Check(game.LoadFromSlot(0) && game.IsFinalCleared, "the FINAL clear record survives save and load");
            Check(game.FinalBossRevealed, "existing FINAL clear records reveal the boss without an encounter flag");
            game.MarkEndingRollSeen();
            await CheckHomeTab(game);
            await CheckMouseEntry(game, Job.Tank);
            game.SelectedJob = Job.Tank;
            hub = await OpenHub(game);
            final = FindFinal(hub);
            CheckFinalAccount(hub, final, true);
            Write(hub, "_sel", final);
            Call(hub, "UpdateFeedScrollTarget");
            await Settle(hub);
            await ConfirmZ(hub);
            Check(Mode(hub) == "Detail", "the replay FINAL card opens its detail");
            Call(hub, "OpenJob");
            Check(Read<JobTuning[]>(hub, "_jobChoices").Length == Jobs.All.Length, "the replay account list includes Mina again");
            await Keypress(hub, Key.X);
            Write(hub, "_mode", Enum.Parse(typeof(Hub).GetNestedType("Mode", Private)!, "Detail"));
            await ConfirmZ(hub);
            Check(Read<bool>(hub, "_dived") && game.SelectedJob == Job.Tank, "Mina dives into FINAL on a replay");
            await WaitForFinal();
            GetTree().CurrentScene?.QueueFree();
            await Frames(8);

            // (e) --job= 固定は素通し（QA の逃し口）。
            game.ClearTimes.Clear();
            Check(game.IsMinaLockedForFinal, "clearing the record locks Mina again");
            game.JobForcedByCmdline = true;
            Check(!game.IsMinaLockedForFinal, "--job= bypasses the FINAL lock for QA runs");
            game.JobForcedByCmdline = false;

            // (b2) 詳細を通らない自動ダイブ（--demo/--qa の DiveAuto）でも、結び手のままでは潜らない。
            //   Dive はシーンを切り替えるので、他の検証が終わってから最後に叩く。
            game.SelectedJob = Job.Tank;
            hub = await OpenHub(game);
            Call(hub, "DiveAuto");
            Check(game.SelectedJob != Job.Tank, $"the automatic dive swaps off Mina ({game.JobDef.CharacterName})");
            await WaitForFinal();
            Check(GetTree().CurrentScene is MinaRoot, "the automatic dive still reaches FINAL");
        }
        catch (Exception e)
        {
            _fails++;
            GD.PrintErr($"[FinalJobLockQA] EXCEPTION {e}");
        }
        GetTree().CurrentScene?.QueueFree();
        await Frames(8);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Audio.Instance?.StopMusic(0);
        foreach (var node in GetNode<Audio>("/root/Audio").GetChildren())
            if (node is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        await Task.Delay(250);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GD.Print(_fails == 0 ? "[FinalJobLockQA] ALL PASS" : $"[FinalJobLockQA] {_fails} FAILURE(S)");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    private async Task CheckMouseEntry(GameManager game, Job job)
    {
        game.SelectedJob = game.IsFinalCleared ? job : Job.Tank;
        game.Difficulty = GameManager.Diff.Hard;
        var hub = await OpenHub(game);
        Write(hub, "_sel", FindFinal(hub));
        Call(hub, "UpdateFeedScrollTarget");
        await Frames(40);
        ClickAt(hub, (Rect2)Call(hub, "CardHitRect", Read<int>(hub, "_sel"))!, "ProcessCards");
        Check(Mode(hub) == "Detail", "mouse opens the FINAL post from the timeline");
        if (game.SelectedJob != job)
        {
            ClickAt(hub, FinalButtonRect(hub), "ProcessDetail", 0.01);
            Check(Mode(hub) == "Job", "the final button works immediately after opening the post");
            await Frames(20);
            var choices = Read<JobTuning[]>(hub, "_jobChoices");
            int selected = Array.FindIndex(choices, choice => choice.Id == job);
            ClickAt(hub, (Rect2)Call(hub, "JobHitRect", selected)!, "ProcessJob", 0.01);
            Check(game.SelectedJob == job && Mode(hub) == "Detail", $"{job}: mouse account selection returns to the FINAL post");
        }
        Check(!(bool)typeof(Hub).GetProperty("NeedsFinalAccount", Private)!.GetValue(hub)!,
            $"{job}: the entry is no longer labeled blocked");
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Frames(20);
            var (x, y, w, h) = ((float, float, float, float))Call(hub, "DetailBox", true)!;
            Rect2 button = (Rect2)Call(hub, "TierHitRect", (int)GameManager.Diff.Hard)!;
            Check(new Rect2(x, y, w, h).Encloses(button)
                && !button.Intersects((Rect2)Call(hub, "DetailJobRect", true)!),
                "FINAL difficulty button fits without overlapping the account control");
            if (job == Job.Melee) await Shot($"final_ready_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        game.SelectedEntry = GameManager.StageEntry.Boss;
        ClickAt(hub, (Rect2)Call(hub, "TierHitRect", (int)GameManager.Diff.Hard)!, "ProcessDetail", 0.01);
        Check(Read<bool>(hub, "_dived") && game.PendingStageScene == "res://MinaBattle.tscn",
            $"{job}: one mouse click starts FINAL");
        Check(game.Difficulty == GameManager.Diff.Hard && game.SelectedEntry == GameManager.StageEntry.Start,
            "FINAL preserves difficulty and starts from the beginning");
        await WaitForFinal();
        Check(GetTree().CurrentScene is MinaRoot && game.SelectedJob == job, $"{job}: Mina stage actually loads with the selected character");
        GetTree().CurrentScene?.QueueFree();
        await Frames(8);
    }

    private static Rect2 FinalButtonRect(Hub hub)
    {
        var (x, y, w, h) = ((float, float, float, float))Call(hub, "DetailBox", false)!;
        return new Rect2(x + 24, y + h - 64, w - 48, 48);
    }

    private static void ClickAt(Hub hub, Rect2 rect, string handler, params object[] args)
    {
        void Set(string name, object value)
            => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
        Vector2 previous = Pad.MousePos();
        Set("_mousePos", rect.GetCenter());
        Set("_usingMouse", true);
        Set("_mL", true);
        Set("_mLPrev", false);
        Call(hub, handler, args);
        Set("_mousePos", previous);
        Set("_mL", false);
        Set("_mLPrev", false);
        Set("_usingMouse", false);
    }

    private async Task CheckHomeTab(GameManager game)
    {
        game.SelectedJob = Job.Magic;
        var hub = await OpenHub(game, home: true);
        Check(Mode(hub) == "Home", "start at the phone home without opening SNS");
        Write(hub, "_sel", FindFinal(hub));
        await Settle(hub);
        await Keypress(hub, Key.Tab);
        Check(Mode(hub) == "Job", "Tab opens the home character list");
        int mina = Array.FindIndex(Read<JobTuning[]>(hub, "_jobChoices"), j => j.Id == Job.Tank);
        Check(mina >= 0, "home offers Mina even with FINAL selected behind the phone");
        if (mina >= 0)
        {
            await Shot(game.IsFinalCleared ? "home_tab_after_ending" : "home_tab_before_final");
            for (int i = 0; i < Jobs.All.Length && Read<int>(hub, "_jobSel") != mina; i++) await Keypress(hub, Key.Down);
            await ConfirmZ(hub);
            Check(game.SelectedJob == Job.Tank && Mode(hub) == "Home", "confirm Mina directly from home");
            if (game.IsFinalCleared) await Shot("home_mina_selected");
        }
        hub.QueueFree();
        await Frames(5);
    }

    private async Task<Hub> OpenHub(GameManager game, bool home = false)
    {
        game.PendingStageScene = "";
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        await Frames(20);
        // ハブは電話のホーム画面から始まるので、検証対象のタイムラインへ直接置く（会話・小話は挟まない）。
        Write(hub, "_idleTalkPending", false);
        if (!home) Write(hub, "_mode", Enum.Parse(typeof(Hub).GetNestedType("Mode", Private)!, "Cards"));
        await Frames(5);
        return hub;
    }

    private void CheckFinalAccount(Hub hub, int index, bool cleared)
    {
        var entry = Read<Array>(hub, "_entries").GetValue(index)!;
        string name = (string)entry.GetType().GetField("Name")!.GetValue(entry)!;
        string handle = (string)entry.GetType().GetField("Handle")!.GetValue(entry)!;
        var icon = Call(hub, "FaceFor", GameManager.FinalStageId);
        var mina = Read<Dictionary<string, Texture2D>>(hub, "_playerFaces")["mina"];
        Check(name == (cleared ? "ミナ" : FinalAccount.Name) && handle == (cleared ? Handles.Mina : FinalAccount.Handle)
            && icon != null && ReferenceEquals(icon, mina) == cleared, "FINAL post uses the right name, handle and icon for this save");
        Call(hub, "UpdateVoiceMedallions", 0.016);
        var medals = Read<Array>(Read<VoiceMedallions>(hub, "_voiceMedallions"), "_medals");
        for (int i = 0; i < medals.Length; i++)
        {
            object medal = medals.GetValue(i)!;
            bool saved = i < GameManager.Stages.Length || cleared;
            var number = (Label3D)medal.GetType().GetField("Number")!.GetValue(medal)!;
            Check(number.Text == (i + 1).ToString() && number.Visible == !saved
                && (medal.GetType().GetField("Face")!.GetValue(medal) != null) == saved,
                $"stage {i + 1} shows the numbered or portrait side according to clearance");
        }
    }

    private static int FindFinal(Hub hub)
    {
        var entries = (IList)hub.GetType().GetField("_entries", Private)!.GetValue(hub)!;
        for (int i = 0; i < entries.Count; i++)
            if ((bool)entries[i]!.GetType().GetField("IsFinal")!.GetValue(entries[i])!) return i;
        return -1;
    }

    private static int FindVoice(Hub hub)
    {
        var entries = (IList)hub.GetType().GetField("_entries", Private)!.GetValue(hub)!;
        for (int i = 0; i < entries.Count; i++)
        {
            object e = entries[i]!;
            if (e.GetType().GetField("Sort")!.GetValue(e)!.ToString() != "Voice") continue;
            if (!(bool)e.GetType().GetField("Unlocked")!.GetValue(e)!) continue;
            if ((bool)e.GetType().GetField("IsFinal")!.GetValue(e)!) continue;
            return i;
        }
        return -1;
    }

    private async Task Keypress(Hub hub, Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(20);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }

    // Z（決定）を押下エッジとして1回だけ流す。実キー入力は次のフレームまで Input へ反映されないので、
    //   押しっぱなしのまま数フレーム回して Hub 側に自然にエッジを拾わせ、最後に離す（_zHeld を前もって
    //   落としておくのは、前の画面で押した状態が残って初回のエッジが消えるのを防ぐため）。
    // ヘッドレスはフレームが極端に速く、Hub の入力ゲート（_t > 0.3 秒）へフレーム数では届かない。
    //   検証したいのはゲートそのものではないので、実時間で越えさせてから押す。
    private async Task Settle(Hub hub)
    {
        for (int i = 0; i < 120 && Read<double>(hub, "_t") <= 0.35; i++) await Frames(10);
        if (Read<double>(hub, "_t") <= 0.35) throw new Exception("Hub did not become interactive.");
        await Frames(3);
    }

    private async Task WaitForFinal()
    {
        for (int i = 0; i < 1800; i++)
        {
            if (GetTree().CurrentScene is MinaRoot && !LoadingScreen.IsActive) return;
            await Frames(1);
        }
        throw new Exception("FINAL did not finish loading.");
    }

    private async Task ConfirmZ(Hub hub)
    {
        Write(hub, "_zHeld", false);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, Pressed = true });
        await Frames(6);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, Pressed = false });
        await Frames(6);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // ヘッドレス（--headless）では FramePostDraw が来ないので撮らない＝ロジック検証はヘッドレス、
    //   スクショはウィンドウ実行で同じシーンを回す（他のQAツールと同じ手口を、待ちで固まらない形にした）。
    private static bool Headless => DisplayServer.GetName() == "headless";

    private async Task Shot(string name)
    {
        if (Headless) return;
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/final_job_lock");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
