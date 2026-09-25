using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// NewTextQa : 2026-09-25 追加ぶん（s2_1 の step 挿入・中ボス第一声の差分・【激情】初期値）の自動検証。
//   2026-09-26 に「主人公の存在」17 行（docs/20260926/主人公の存在_診断と本文）の静的検査（5）を追加。
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

            // ── 5) 主人公の存在（2026-09-26 docs/20260926/主人公の存在_診断と本文 §3-4）──
            //   足した 17 行のうち静的に検められるぶん（クリア後の並び・迷い秒ゲートの純関数・RECLOSE／挑発／F3／P2 受け）。
            //   実際に画面へ流れるところは tools/qa_hero_lines.tscn（HeroLinesQa）が撮る。
            {
                const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
                static (int who, string text, string face)[] Lines(Type t, string m, GameManager? g)
                    => ((int who, string text, string face)[])t.GetMethod(m, Static)!.Invoke(null, new object?[] { g })!;
                static string[] Strs(Type t, string f) => (string[])t.GetField(f, Static)!.GetValue(null)!;
                static string Join((int who, string text, string face)[] a) => string.Join(" / ", a.Select(l => $"{l.who}:{l.text}"));

                // 迷い秒ゲート（ChoiceEffects.Hesitated）：未通過→false／p2 以下→false／p2 より長い→true／game 無し→false。
                game.ResetPersistent();
                game.RecordChoice("p2", "おはよう", System.Array.Empty<string>(), 4f);
                Check("Hesitated: 未通過の id は false", !ChoiceEffects.Hesitated(game, "s1_4"));
                game.RecordChoice("s1_4", "十二件、ぜんぶ", System.Array.Empty<string>(), 4f);
                Check("Hesitated: p2 と同秒は false", !ChoiceEffects.Hesitated(game, "s1_4"));
                game.RecordChoice("s1_4", "十二件、ぜんぶ", System.Array.Empty<string>(), 4.5f);
                Check("Hesitated: p2 より長ければ true", ChoiceEffects.Hesitated(game, "s1_4"));
                Check("Hesitated: game 無しは false", !ChoiceEffects.Hesitated(null, "s1_4"));

                // クリア後の並び：錨の行の直後に、ゲート有り／無しで期待どおりの行が続く（★以外は必ず出る）。
                foreach (var (type, id, anchor, star, withGate, noGate) in new (Type, string, string, string, (int, string)[], (int, string)[])[]
                {
                    (typeof(StageAkari), "s1_4", "……あったかい声が、した。……知らない声なのに。変なの。",
                        "……ねえ。あのとき、すぐ決めなかったでしょ。……うん。それで、いい。",
                        new[] { (2, "……ねえ。あのとき、すぐ決めなかったでしょ。……うん。それで、いい。"), (2, "……知らない声のくせに。……言い方だけ、どこかで、聞いたことある。"), (1, "……言い方は、わたくしのです。……たぶん。"), (1, "……♥が、ひとつ。") },
                        new[] { (2, "……知らない声のくせに。……言い方だけ、どこかで、聞いたことある。"), (1, "……言い方は、わたくしのです。……たぶん。"), (1, "……♥が、ひとつ。") }),
                    (typeof(StageKoharu), "s2_4", "入力欄に、一行、増えました。……読み上げは、しません。もう、送られたものですので。",
                        "……迷ってくれたよね。あたし、それ、見てたよ。",
                        new[] { (2, "……迷ってくれたよね。あたし、それ、見てたよ。"), (2, "……あの手。……ペンライト、ちゃんと振ってた。……点いてなくても。"), (1, "……ご主人様。外の世界は、今日はどんな天気ですか。") },
                        new[] { (2, "……あの手。……ペンライト、ちゃんと振ってた。……点いてなくても。"), (1, "……ご主人様。外の世界は、今日はどんな天気ですか。") }),
                    (typeof(StageRei), "s3_5c", "右上の数字が、「4」に。……ひとつ、増えました。",
                        "……即答されてたら、たぶん、信じてなかった。",
                        new[] { (2, "……四。……あんたは、数に入らないんでしょう。……なら、増えたの、誰。"), (1, "……集計は、向こう側の、ものですので。"), (2, "……即答されてたら、たぶん、信じてなかった。"), (1, "コメント欄の、あの一行。……まだ、同じ場所にあります。") },
                        new[] { (2, "……四。……あんたは、数に入らないんでしょう。……なら、増えたの、誰。"), (1, "……集計は、向こう側の、ものですので。"), (1, "コメント欄の、あの一行。……まだ、同じ場所にあります。") }),
                })
                {
                    foreach (bool gate in new[] { true, false })
                    {
                        game.ResetPersistent();
                        game.RecordChoice("p2", "おはよう", System.Array.Empty<string>(), 4f);
                        game.RecordChoice(id, "x", System.Array.Empty<string>(), gate ? 9f : 1f);
                        var lines = Lines(type, "ClearFor", game);
                        int at = System.Array.FindIndex(lines, l => l.text == anchor);
                        var want = gate ? withGate : noGate;
                        var got = at >= 0 ? lines.Skip(at + 1).Take(want.Length).Select(l => (l.who, l.text)).ToArray() : System.Array.Empty<(int, string)>();
                        Check($"{type.Name} ClearFor(迷い{(gate ? "有" : "無")}) 錨の直後の並び", got.SequenceEqual(want), Join(lines));
                        Check($"{type.Name} ClearFor(迷い{(gate ? "有" : "無")}) ★の有無", lines.Any(l => l.text == star) == gate, Join(lines));
                    }
                    // ボスから入場（選択未通過）でも★は出ない。
                    game.ResetPersistent();
                    Check($"{type.Name} ClearFor(未通過) ★なし", !Lines(type, "ClearFor", game).Any(l => l.text == star));
                }
                // あかり：フィルム前／後の割り目は「……♥が、ひとつ。」の手前（旧 Take(2) のベタ書きを廃止）。
                foreach (bool gate in new[] { true, false })
                {
                    game.ResetPersistent();
                    game.RecordChoice("p2", "おはよう", System.Array.Empty<string>(), 4f);
                    game.RecordChoice("s1_4", "x", System.Array.Empty<string>(), gate ? 9f : 1f);
                    var all = Lines(typeof(StageAkari), "ClearFor", game);
                    var before = Lines(typeof(StageAkari), "ClearBeforeFor", game);
                    var after = Lines(typeof(StageAkari), "ClearAfterFor", game);
                    Check($"あかり 割り目(迷い{(gate ? "有" : "無")}) 前半の末尾＝ミナ「たぶん」", before.Length == (gate ? 5 : 4) && before[^1].text == "……言い方は、わたくしのです。……たぶん。", Join(before));
                    Check($"あかり 割り目(迷い{(gate ? "有" : "無")}) 後半の先頭＝「♥が、ひとつ」", after.Length > 0 && after[0].text == "……♥が、ひとつ。" && before.Concat(after).SequenceEqual(all), Join(after));
                }

                // RECLOSE（index 2 に挿入・最終形は最後のまま）／レイの挑発 3 本目／F3 レイの 1 行。
                var ra = Strs(typeof(BossAkari), "RecloseLines");
                Check("あかり RECLOSE[2]＝後ろの人／[3]＝返して", ra.Length == 4 && ra[2] == "……読んでるの、あなただけじゃ、ないでしょ。……後ろの人。ねえ、そっちも、返して。" && ra[3] == "……返して。読んだなら、返してよ。", string.Join(" / ", ra));
                var rk = Strs(typeof(BossKoharu), "RecloseLines");
                Check("こはる RECLOSE[2]＝そっちの手／[3]＝楽しいってば", rk.Length == 4 && rk[2] == "……ねえ。いま動いてるの、そっちの手じゃ、ないでしょ。……あたし、手は、見るもん。" && rk[3] == "……なんでもない。楽しいってば。", string.Join(" / ", rk));
                var rr = Strs(typeof(BossRei), "RecloseLines");
                Check("レイ RECLOSE[2]＝あんたの後ろ／[3]＝見てて", rr.Length == 4 && rr[2] == "……あんたの後ろ。初見さん、いるでしょう? ……いらっしゃい。" && rr[3] == "見てて。……ちゃんと、見ててよ。", string.Join(" / ", rr));
                var tr = Strs(typeof(BossRei), "TauntLines");
                Check("レイ 挑発[2]＝手、止まってる", tr.Length == 3 && tr[2] == "……手、止まってる。……見てるだけの人、ひとり、増えたのね。", string.Join(" / ", tr));
                var f3 = ((int who, string text, string face)[])typeof(BossMina).GetField("Lines", Static)!.GetValue(null)!;
                int f3At = System.Array.FindIndex(f3, l => l.text == "知ってる。あんたの声だった。");
                Check("F3 「あんたの声だった」の直後＝レイ「そっくりよ」", f3.Length == 6 && f3At >= 0 && f3[f3At + 1] == (2, "……あんたの言い方。……この人に、そっくりよ。", "res://char/v3/rei_face.png"), Join(f3));

                // P2 の受け：「敬っている〜」の直後に「いまの言い回し」（ミナ）。Prologue はシーンを起こさず、
                //   未初期化オブジェクトで P2Reply（Godot 側に触らない純関数）だけを呼ぶ。終了処理（finalizer）は抑止しておく。
                var pro = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Prologue));
                GC.SuppressFinalize(pro);
                var reply = (System.Collections.IList)typeof(Prologue).GetMethod("P2Reply", Private)!.Invoke(pro, new object[] { "おはよう" })!;
                var texts = new List<(int who, string text)>();
                foreach (var d in reply) texts.Add(((int)d!.GetType().GetField("Who")!.GetValue(d)!, (string)d.GetType().GetField("Text")!.GetValue(d)!));
                int p2At = texts.FindIndex(t => t.text == "……敬っている、とは言っていませんが。");
                Check("P2 受け 「敬っている〜」の直後＝「いまの言い回し」", p2At >= 0 && p2At + 1 < texts.Count && texts[p2At + 1] == (1, "……いまの言い回し。……どこで覚えたのか、記録に、ありません。"), string.Join(" / ", texts.Select(t => t.text)));
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
