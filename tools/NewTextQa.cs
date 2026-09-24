using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// NewTextQa : 2026-09-25 追加ぶん（s2_1 の step 挿入・中ボス第一声の差分・【激情】初期値）の自動検証。
//   実行: Godot --headless --path . res://tools/qa_newtext.tscn -- --qa-newtext
//   ※セーブを書くので APPDATA を隔離した状態で走らせること。
public partial class NewTextQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private int _fail;

    private void Check(string label, bool ok, string detail = "")
    {
        if (!ok) _fail++;
        GD.Print($"[newtextqa] {(ok ? "OK  " : "NG  ")}{label}{(ok ? "" : $" — {detail}")}");
    }

    private static T Read<T>(object o, string f) => (T)o.GetType().GetField(f, Private)!.GetValue(o)!;
    private static void Call(object o, Type t, string m, params object[] a) => t.GetMethod(m, Private)!.Invoke(o, a);

    public override async void _Ready()
    {
        await Frames(2);
        var game = GetNode<GameManager>("/root/Game");
        game.AutoSaveEnabled = false;
        game.SelectedJob = Job.Tank;
        try
        {
            // ── 1) こはる面の step 番号（チェックポイント入口が正しい step に乗るか）──
            foreach (var (entry, wantStep, wantMethod) in new[]
            {
                (GameManager.StageEntry.Boss, 12, "Step_BossSpawn"),
                (GameManager.StageEntry.AfterMidBoss, 7, "Step_MidwaveB"),
                (GameManager.StageEntry.MidBoss, 6, "Step_BossCameo"),
            })
            {
                game.ResetPersistent();
                game.SelectedJob = Job.Tank;
                game.SelectedEntry = entry;
                var root = await Stage();
                // Step_BossSpawn は同フレームで Advance するので、Boss 入口だけは +1 を許す（12 → 13＝口上）。
                int step = Read<int>(root.Stage, "_step");
                Check($"入口 {entry} → step {wantStep}（{wantMethod}）", step == wantStep || step == wantStep + 1,
                    $"実際 step={step}");
                // 番号ではなく現物で確かめる＝その戦闘が実際に立っているか。
                if (entry == GameManager.StageEntry.Boss)
                    Check("入口 Boss で本ボスが立つ", root.World.GetNodeOrNull<BossKoharu>("BossKoharu") != null, "BossKoharu が無い");
                if (entry == GameManager.StageEntry.MidBoss)
                    Check("入口 MidBoss で中ボスが立つ", root.World.GetNodeOrNull<CameoBoss>("KoharuCameo") != null, "KoharuCameo が無い");
                await Drop(root);
            }

            // ── 2) step 3 が s2_1 を流す（選択 id が s2_1 になる）──
            {
                game.ResetPersistent();
                game.SelectedJob = Job.Tank;
                game.SelectedEntry = GameManager.StageEntry.Start;
                var root = await Stage();
                root.Stage.SetProcess(false);
                var st = root.Stage;
                typeof(StageKoharu).GetField("_step", Private)!.SetValue(st, 3);
                typeof(StageKoharu).GetField("_stepStarted", Private)!.SetValue(st, false);
                Call(st, typeof(StageKoharu), "Step_Schedule", 0d);
                Check("step 3 が s2_1 を開く", Read<string>(st, "_cId") == "s2_1", $"_cId='{Read<string>(st, "_cId")}'");
                Check("step 3 に留まる", Read<int>(st, "_step") == 3, $"step={Read<int>(st, "_step")}");
                await Drop(root);
            }

            // ── 3) 中ボスの第一声が s2_1 / s3_2 の選択で差し替わる ──
            foreach (var (choice, head) in new[]
            {
                ("", "あ、来た来た。……「あたし、なにしてんだろ」って顔、してた? ……してないよ。してないってば。"),
                ("一日、空けて", "あ、来た来た。……「一日、空けて」って、言ってたでしょ。……どこ空けんの? 空いてないってば、どこも。"),
                ("三本、飲んでから", "あ、来た来た。……「三本、飲んでから」? ……飲んだよ。……うそ。開けてない。……開けてないけど、買ったもん。"),
                ("全部に丸がついてる", "あ、来た来た。……「全部に丸がついてる」って。……でしょ? ぜんぶだよ。ぜんぶ、ちゃんと。……ちゃんとしてるでしょ?"),
                ("（送らない）", "あ、来た来た。……「あたし、なにしてんだろ」って顔、してた? ……してないよ。してないってば。"),
            })
            {
                game.ResetPersistent();
                game.SelectedJob = Job.Tank;
                game.SelectedEntry = GameManager.StageEntry.Start;
                if (choice != "") game.RecordChoice("s2_1", choice, System.Array.Empty<string>(), 1f);
                var root = await Stage();
                root.Stage.SetProcess(false);
                typeof(StageKoharu).GetField("_stepStarted", Private)!.SetValue(root.Stage, false);
                Call(root.Stage, typeof(StageKoharu), "Step_BossCameo", 0d);
                var cameo = root.World.GetNode<CameoBoss>("KoharuCameo");
                string got = cameo.Theme.IntroLines.Length > 0 ? cameo.Theme.IntroLines[0].text : "(空)";
                Check($"こはる中ボス第一声 s2_1='{(choice == "" ? "未選択" : choice)}'", got == head, $"実際='{got}'");
                // 一行オーバーレイ（Hud.ShowBossLine）に実際に載っているか＝配列だけでなく画面の文字を見る。
                await Frames(3);
                string shown = (string)typeof(Hud).GetField("_bossLine", Private)!.GetValue(root.Hud)!;
                Check($"　→ オーバーレイの文字も一致 s2_1='{(choice == "" ? "未選択" : choice)}'", shown == head, $"実際='{shown}'");
                await Drop(root);
            }

            foreach (var (choice, head) in new[]
            {
                ("", "……だれ? あなた。……わたしの配信、見に来た人?"),
                ("同接、9", "……だれ? あなた。……「同接、9」って、言ってたでしょ。……数えたの、あなたなの?"),
                ("ちゃんと見てる", "……だれ? あなた。……「ちゃんと見てる」って、さっき。……誰に言ったの、それ。わたしじゃ、ないわよね。"),
                ("見えてる", "……だれ? あなた。……「見えてる」って、言ってたわね。……見えてるなら、なんで、こっちは来ないのよ。"),
                ("（送らない）", "……だれ? あなた。……わたしの配信、見に来た人?"),
            })
            {
                game.ResetPersistent();
                game.SelectedJob = Job.Tank;
                game.SelectedEntry = GameManager.StageEntry.Start;
                if (choice != "") game.RecordChoice("s3_2", choice, System.Array.Empty<string>(), 1f);
                var root = await ReiStage();
                var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
                stage.SetProcess(false);
                stage.GetType().GetField("_stepStarted", Private)!.SetValue(stage, false);
                Call(stage, typeof(StageRei), "Step_BossCameo", 0d);
                var world = (Node2D)root.GetType().GetProperty("World")!.GetValue(root)!;
                var cameo = world.GetNode<CameoBoss>("ReiCameo");
                string got = cameo.Theme.IntroLines.Length > 0 ? cameo.Theme.IntroLines[0].text : "(空)";
                Check($"レイ中ボス第一声 s3_2='{(choice == "" ? "未選択" : choice)}'", got == head, $"実際='{got}'");
                await Frames(3);
                var hud = (Hud)root.GetType().GetProperty("Hud")!.GetValue(root)!;
                string shown = (string)typeof(Hud).GetField("_bossLine", Private)!.GetValue(hud)!;
                Check($"　→ オーバーレイの文字も一致 s3_2='{(choice == "" ? "未選択" : choice)}'", shown == head, $"実際='{shown}'");
                await Drop((Node)root);
            }

            // ── 4) 【激情】こはるの初期値レンジ ──
            {
                string[] s21 = { "一日、空けて", "三本、飲んでから", "全部に丸がついてる", "（送らない）", "" };
                string[] s22 = { "言わない", "電池、切れてる", "次は、点くほう", "（送らない）", "" };
                string[] s24 = { "返してない返信", "既読のまま、三日", "もう送れない相手", "（送らない）", "" };
                float lo = 999f, hi = -999f;
                foreach (var a in s21)
                    foreach (var b in s22)
                        foreach (var c in s24)
                        {
                            game.ResetPersistent();
                            if (a != "") game.RecordChoice("s2_1", a, System.Array.Empty<string>(), 1f);
                            if (b != "") game.RecordChoice("s2_2", b, System.Array.Empty<string>(), 1f);
                            if (c != "") game.RecordChoice("s2_4", c, System.Array.Empty<string>(), 1f);
                            float v = Fury.InitialFor(game, "koharu");
                            lo = Mathf.Min(lo, v);
                            hi = Mathf.Max(hi, v);
                        }
                GD.Print($"[newtextqa] こはる初期値レンジ = {lo:0.0} 〜 {hi:0.0}（InitialClamp={Fury.InitialClamp}）");
                // 稿の「-24」は s2_2（送らない）の -6 を数え落とした値。実測は -30（= -12 -6 -12）で、
                //   InitialClamp=30 にちょうど収まる（切られない）。
                Check("こはる初期値レンジ下端 -30", Mathf.IsEqualApprox(lo, -30f), $"実測 {lo}");
                Check("こはる初期値レンジ上端 +20", Mathf.IsEqualApprox(hi, 20f), $"実測 {hi}");
                Check("InitialClamp は 30 のまま", Mathf.IsEqualApprox(Fury.InitialClamp, 30f), $"実測 {Fury.InitialClamp}");
            }
        }
        catch (Exception e)
        {
            _fail++;
            GD.PrintErr($"[newtextqa] EXCEPTION {e}");
        }
        GD.Print(_fail == 0 ? "[newtextqa] ALL OK" : $"[newtextqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private async Task<KoharuRoot> Stage()
    {
        var root = GD.Load<PackedScene>("res://Koharu.tscn").Instantiate<KoharuRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        await Frames(4);
        return root;
    }

    private async Task<Node> ReiStage()
    {
        var root = GD.Load<PackedScene>("res://Rei.tscn").Instantiate();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        await Frames(4);
        return root;
    }

    private async Task Drop(Node root)
    {
        GetTree().Root.RemoveChild(root);
        root.QueueFree();
        await Frames(3);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
