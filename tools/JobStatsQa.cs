using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

// JobStatsQa : アカウント切り替え画面の「性能表示」（2026-09-27 作者指示）を検める。
//   (a) 4キャラぶんの詳細に 撃ち方・火力・溜め打ち の文言が出る
//   (b) カーソルを動かすと詳細パネルがその人へ追従する
//   (c) パネルの文字が JobBox() の内側に収まる（UiKit.TextW で1行ごとに確かめる）
//   (d) 未解放キャラは一覧に出ない（この画面の既存作法＝押せないものを見せない）
//   窓ありで走らせると、各行を選んだ状態のスクショを build/shots_jobstats/ に残す。
public partial class JobStatsQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Shared = BindingFlags.Static | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static object? CallStatic(string name, params object[] args) => typeof(Hub).GetMethod(name, Shared)!.Invoke(null, args);
    private static string Mode(Hub hub) => Read<object>(hub, "_mode").ToString()!;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[JobStatsQA] PASS {message}");
    }

    // パネル内に実際に描かれる文字（詳細パネルの描画と同じ組み立て）。はみ出し検査で使う。
    private static List<(string text, Font font, int size)> PanelLines(JobTuning job)
    {
        var lines = new List<(string, Font, int)>
        {
            (Jobs.ModeName(job.Mode), UiKit.ZenBold, 15),
            (Jobs.ModeNote(job.Mode), UiKit.Zen, 13),
            (Jobs.PowerLabel(job), UiKit.ZenBold, 14),
            (Jobs.ChargeLabel(job), UiKit.Zen, 13),
        };
        foreach (var (label, value, _) in ((string, string, int)[])CallStatic("JobVitals", job)!)
        {
            lines.Add((label, UiKit.Zen, 11));
            lines.Add((value, UiKit.ZenBold, 15));
        }
        lines.Add(($"回避距離：{Jobs.DodgeDistNote(job)}", UiKit.Zen, 11));
        foreach (string s in (List<string>)CallStatic("JobPros", job)!) lines.Add(("・" + s, UiKit.Zen, 12));
        foreach (string s in (List<string>)CallStatic("JobCons", job)!) lines.Add(("・" + s, UiKit.Zen, 12));
        return lines;
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            foreach (var job in Jobs.All) game.MarkIdleDialogSeen($"once_companion_select_{job.CharacterId}");
            game.MarkIdleDialogSeen("once_phone_home");
            game.MarkIdleDialogSeen("once_sns_intro");
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            GetNode<PauseMenu>("/root/PauseMenu").SetProcess(false);

            // ── (a) 表示文言そのものの検査（画面を開く前に、素の JobTuning から組める形か）──
            foreach (var job in Jobs.All)
            {
                string mode = Jobs.ModeName(job.Mode), note = Jobs.ModeNote(job.Mode);
                string power = Jobs.PowerLabel(job), charge = Jobs.ChargeLabel(job);
                Check(mode.Length > 0 && note.Length > 0, $"{job.CharacterId} names its shot mode and what it is for");
                Check(power.Length > 0 && (job.PowerMul == 1f) == (power == "標準"),
                    $"{job.CharacterId} states its firepower, calling the baseline standard");
                Check(charge.Contains(job.ChargeDescription) && charge.Contains($"威力{job.ChargePower}"),
                    $"{job.CharacterId} states its charged shot with its actual power");
                Check(job.ChargeWays == 1 || charge.Contains($"×{job.ChargeWays}発"),
                    $"{job.CharacterId} states how many charged bullets it fires");
                var vitals = ((string, string, int)[])CallStatic("JobVitals", job)!;
                Check(vitals.Length == 4, $"{job.CharacterId} always lists life, movement and both dodge values");
                // 機動・耐久の4項目は「基準と同値でも出す」＝並べて序列が読めることが目的。
                Check(vitals[1].Item2 == System.FormattableString.Invariant($"×{job.MoveMul:0.00}")
                    && vitals[2].Item2 == System.FormattableString.Invariant($"×{job.DodgeDistMul:0.00}")
                    && vitals[3].Item2 == System.FormattableString.Invariant($"×{job.DodgeCdMul:0.00}"),
                    $"{job.CharacterId} prints its real movement and dodge multipliers");
                string row = (string)CallStatic("JobStats", job)!;
                Check(row.Contains(mode) && row.Contains(power), $"{job.CharacterId} summarises mode and firepower on its row");
            }
            // 4人が別の軸で強い（2026-09-27 の値振り）：機動の序列と耐久の序列が入れ替わっている。
            JobTuning Def(Job j) => Jobs.Get(j);
            Check(Def(Job.Melee).MoveMul > Def(Job.Heal).MoveMul
                && Def(Job.Heal).MoveMul > Def(Job.Magic).MoveMul
                && Def(Job.Magic).MoveMul > Def(Job.Tank).MoveMul,
                "movement ranks Akari over Koharu over Rei over Mina");
            Check(Def(Job.Tank).MaxLifeDelta > Def(Job.Magic).MaxLifeDelta
                && Def(Job.Magic).MaxLifeDelta > Def(Job.Heal).MaxLifeDelta
                && Def(Job.Heal).MaxLifeDelta > Def(Job.Melee).MaxLifeDelta,
                "hearts rank Mina over Rei over Koharu over Akari");
            // 回避距離は4人で「すこし極端に」開く（2026-09-27）。序列と、最長/最短が2.5倍以上離れること。
            Check(Def(Job.Melee).DodgeDistMul > Def(Job.Heal).DodgeDistMul
                && Def(Job.Heal).DodgeDistMul > Def(Job.Tank).DodgeDistMul
                && Def(Job.Tank).DodgeDistMul > Def(Job.Magic).DodgeDistMul,
                "dodge distance ranks Akari over Koharu over Mina over Rei");
            Check(Def(Job.Melee).DodgeDistMul / Def(Job.Magic).DodgeDistMul >= 2.5f,
                "the longest dodge reaches at least 2.5x the shortest");
            var notes = new HashSet<string>();
            foreach (var job in Jobs.All) notes.Add(Jobs.DodgeDistNote(job));
            Check(notes.Count == 4, "each account explains its dodge distance in its own words");

            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(30);

            // ── (d) 未解放キャラの表示：新規データはミナだけ＝伏せた行も出さない ──
            Call(hub, "OpenJob");
            await Frames(20);
            Check(Mode(hub) == "Job", "the accounts sheet opens");
            var single = Read<JobTuning[]>(hub, "_jobChoices");
            Check(single.Length == 1 && single[0].Id == Job.Tank, "new data lists only Mina instead of masked rows");
            foreach (var job in Jobs.All)
                Check(game.IsJobUnlocked(job.Id) == (job.UnlockStageId.Length == 0)
                    && (game.JobUnlockHint(job.Id) == null) == (job.UnlockStageId.Length == 0),
                    $"{job.CharacterId} keeps its unlock state and explains the stage that opens it");
            await CheckFits(hub, "mina only");
            await Shot("accounts_locked_mina_only");

            // 全員救った状態にして4人並べる（判定は _cleared だけを見る＝Hub と同じ経路で開き直す）。
            var cleared = Read<HashSet<string>>(game, "_cleared");
            foreach (var stage in GameManager.Stages) cleared.Add(stage.Id);
            Write(hub, "_mode", Enum.Parse(Read<object>(hub, "_mode").GetType(), "Cards"));
            Call(hub, "OpenJob");
            await Frames(20);
            var choices = Read<JobTuning[]>(hub, "_jobChoices");
            Check(choices.Length == 4, "every rescued account is listed");

            // ── (b)(c) カーソルを動かすと詳細が追従し、どの人でもパネル内に収まる ──
            for (int i = 0; i < choices.Length; i++)
            {
                Write(hub, "_jobSel", i);
                await Frames(6);
                Check(Read<int>(hub, "_jobSel") == i, $"cursor rests on {choices[i].CharacterId}");
                await CheckFits(hub, choices[i].CharacterId);
                await Shot($"accounts_{choices[i].CharacterId}");
            }
            // ↓ で順に降りて、パネルの中身が選択と一緒に変わることを確かめる。
            Write(hub, "_jobSel", 0);
            Write(hub, "_navHeld", false);
            await Frames(4);
            for (int i = 1; i < choices.Length; i++)
            {
                await KeyAction("ui_down");
                Check(Read<int>(hub, "_jobSel") == i, "↓ moves the cursor one account down");
                var shown = Read<JobTuning[]>(hub, "_jobChoices")[Read<int>(hub, "_jobSel")];
                Check(shown.CharacterId == choices[i].CharacterId
                    && Jobs.ModeName(shown.Mode) == Jobs.ModeName(choices[i].Mode),
                    $"the detail panel follows the cursor onto {choices[i].CharacterId}");
            }
            // 撃ち方が4人でばらけている＝パネルを見比べる意味がある。
            var modes = new HashSet<string>();
            foreach (var job in choices) modes.Add(Jobs.ModeName(job.Mode));
            Check(modes.Count == 4, "the four accounts show four different shot modes");
            await Finish();
        }
        catch (Exception ex)
        {
            GD.PushError($"[JobStatsQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    // パネルが JobBox() の内側にあり、描く文字が1行ずつ内幅を超えないこと。
    private async Task CheckFits(Hub hub, string who)
    {
        await Frames(2);
        var box = ((float x, float y, float w, float h))Call(hub, "JobBox")!;
        var sheet = new Rect2(box.x, box.y, box.w, box.h);
        var phone = new Rect2(400, 0, 480, 720);
        Check(phone.Encloses(sheet), $"the accounts sheet stays inside the phone column ({who})");
        var panel = (Rect2)Call(hub, "JobPanelRect")!;
        Check(sheet.Encloses(panel), $"the detail panel stays inside the sheet ({who})");
        var confirm = (Rect2)Call(hub, "JobConfirmRect")!;
        Check(panel.End.Y <= confirm.Position.Y, $"the detail panel never overlaps the confirm button ({who})");
        var rows = Read<JobTuning[]>(hub, "_jobChoices");
        for (int i = 0; i < rows.Length; i++)
        {
            var row = (Rect2)Call(hub, "JobHitRect", i)!;
            Check(sheet.Encloses(row) && !row.Intersects(panel), $"row {i} sits above the detail panel ({who})");
            string stats = (string)CallStatic("JobStats", rows[i])!;
            Check(UiKit.TextW(UiKit.Zen, stats, 12) <= row.Size.X - 110f, $"row {i} summary fits beside the name ({who})");
        }
        // パネルの本文（pad=14・強み/弱みの見出し幅36）に収まる幅で1行ずつ検める。
        float inner = panel.Size.X - 28f;
        foreach (var (text, font, size) in PanelLines(Read<JobTuning[]>(hub, "_jobChoices")[Read<int>(hub, "_jobSel")]))
            Check(UiKit.TextW(font, text, size) <= inner - 36f,
                $"\"{text}\" fits inside the detail panel ({who})");
    }

    private async Task Finish()
    {
        Audio.Instance?.StopMusic(0);
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
            if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        await Task.Delay(250);
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GD.Print("[JobStatsQA] ALL PASS");
        GetTree().Quit();
    }

    private async Task KeyAction(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Frames(2);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        await Frames(3);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;   // 撮影は窓ありのときだけ
        string path = ProjectSettings.GlobalizePath("res://build/shots_jobstats");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
