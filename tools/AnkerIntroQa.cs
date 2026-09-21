using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// AnkerIntroQa : アンチャー紹介（StageTutorial の ③・2026-09-17）と強化アイテム説明（④・2026-09-22）の
//   発火とカード共存の自動検証。
//   各ステージを実際に立てて _playerIntro を読み、以下を判定する：
//     1) 結び手（ミナ潜行）の初回突入で、道中チュートリアル16行の「後ろ」に紹介が付いている
//     2) 同じセーブで2回目に入ると付かない（once が面ごとに効いている）
//     3) 操作カード（ControlCard）が従来どおり出る＝SyncCard が Route の各行を引き当てられる
//        （Route のさらに後ろへ Concat しても添字がズレない＝今回の落とし穴の回避確認）
//     4) 他ジョブ潜行中は出ない・once も消費しない
//     5) 強化アイテム説明（④）は あかり面だけ、紹介③の「直後」に付く。こはる／レイには混ざらない。
//        once（once_items_akari）・他ジョブ非表示・非消費・2回目非表示は③と同じ扱い。
//   実行: Godot --headless --path . res://tools/qa_anker_intro.tscn -- --qa
public partial class AnkerIntroQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // 本文先頭行（docs/20260917/アンチャー紹介_本文_2026-09-17.md）。実装と本文の一致確認も兼ねる。
    //   items: 紹介③の後ろに続く強化アイテム説明④の行数（あかり面のみ 4・他は 0）。
    private static readonly (string scene, string name, string key, int count, string head, int items)[] Cases =
    {
        ("res://Akari.tscn",  "あかり", StageTutorial.AnkerAkariSeenKey,  3,
            "この階のアンチャーは、間合いを詰めてまいります。落ちてくるもの、途中から速くなるもの。", 4),
        ("res://Koharu.tscn", "こはる", StageTutorial.AnkerKoharuSeenKey, 3,
            "ここのアンチャーは、一度に、たくさん寄越してきます。構えたと思った次の瞬間には、もう来ています。", 0),
        ("res://Rei.tscn",    "レイ",   StageTutorial.AnkerReiSeenKey,    4,
            "この枠のアンチャーは、正面から来ません。上から、まわりから、背中から。", 0),
    };

    // 強化アイテム説明④の先頭行（docs/20260922/強化アイテム説明_本文_2026-09-22.md）。
    private const string ItemsHead = "もうひとつ。浄化を重ねていると、ときどき、欠片に混じって——色の枠がついたものが、こぼれます。";
    // 紹介③（あかり）の末尾行＝④はこの「直後」に付いていなければならない。
    private const string AnkerAkariLast = "同じ場所に留まらないでください。それだけで、だいぶ違います。";

    private int _fail;

    public override async void _Ready()
    {
        await Frames(2);   // root が子のセットアップ中は AddChild できない＝1フレーム待ってから始める
        var game = GetNode<GameManager>("/root/Game");

        foreach (var (scene, node, key, count, head, items) in Cases)
        {
            // ── 1回目（結び手・未読）＝紹介が出る ──
            game.ResetPersistent();
            game.SelectedJob = Job.Tank;
            var first = await IntroOf(scene);
            int tail = count + items;   // 末尾のブロック＝紹介③（count行）＋アイテム説明④（items行）
            Check($"{node} 1回目 紹介{count}行", first.Length >= tail
                && first[^tail].text == head, $"末尾{tail}行の先頭='{Tail(first, tail)}'");
            Check($"{node} 1回目 once 消費", game.IsIdleDialogSeen(key), "未消費");

            // 紹介はチュートリアル16行の「後ろ」＝順序が 一般→個別 になっている
            Check($"{node} 順序(チュートリアル→紹介)",
                first.Length >= tail + 16 && first[^(tail + 1)].text.Contains("オペレータのお仕事"),
                $"紹介の直前='{(first.Length >= tail + 1 ? first[^(tail + 1)].text : "(なし)")}'");

            // ── 強化アイテム説明④：あかり面だけ、紹介③の直後に付く ──
            if (items > 0)
            {
                Check($"{node} 1回目 アイテム説明{items}行", first.Length >= items && first[^items].text == ItemsHead,
                    $"末尾{items}行の先頭='{Tail(first, items)}'");
                Check($"{node} 順序(紹介→アイテム説明)",
                    first.Length >= items + 1 && first[^(items + 1)].text == AnkerAkariLast,
                    $"アイテム説明の直前='{(first.Length >= items + 1 ? first[^(items + 1)].text : "(なし)")}'");
                Check($"{node} 1回目 items once 消費", game.IsIdleDialogSeen(StageTutorial.ItemsAkariSeenKey), "未消費");
            }
            else
            {
                Check($"{node} アイテム説明が混ざらない", first.All(l => l.text != ItemsHead), "あかり面専用の説明が出ている");
                Check($"{node} items once 未消費", !game.IsIdleDialogSeen(StageTutorial.ItemsAkariSeenKey), "消費されている");
            }

            // ── 操作カード：チュートリアル16行の各行で SyncCard が話題を引けるか ──
            await CardCheck(node, first, tail, items);

            // ── 2回目（同じセーブ）＝紹介もチュートリアルも出ない ──
            var second = await IntroOf(scene);
            Check($"{node} 2回目 出ない", second.Length > 0 && second[^1].text != first[^1].text,
                $"末尾='{second.LastOrDefault().text}'");
            if (items > 0)
                Check($"{node} 2回目 アイテム説明も出ない", second.All(l => l.text != ItemsHead), "2回目にも出ている");

            // ── 他ジョブ潜行＝出ない・once も消費しない ──
            game.ResetPersistent();
            game.SelectedJob = Job.Melee;   // 結び手以外＝キャラ別ストーリー潜行中
            var other = await IntroOf(scene);
            Check($"{node} 他ジョブで出ない", other.All(l => l.text != head), "紹介の先頭行が混ざっている");
            Check($"{node} 他ジョブで once 未消費", !game.IsIdleDialogSeen(key), "消費されている");
            if (items > 0)
            {
                Check($"{node} 他ジョブでアイテム説明が出ない", other.All(l => l.text != ItemsHead), "出ている");
                Check($"{node} 他ジョブで items once 未消費", !game.IsIdleDialogSeen(StageTutorial.ItemsAkariSeenKey), "消費されている");
            }
        }

        GD.Print(_fail == 0 ? "[ankerqa] ALL OK" : $"[ankerqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // 操作カードの同期確認。チュートリアル16行の位置で ShowLine を呼び、カードの Topic が期待どおり動くか見る。
    //   紹介③（＋あかりは説明④）を Route の後ろへ足したことで添字がズレていれば、カードが一切出ない＝ここで落ちる。
    //   tailCount: 末尾に続くブロックの総行数（③＋④）。items: そのうち④の行数。
    private async Task CardCheck(string node, (int who, string text, string face)[] lines, int tailCount, int items)
    {
        var stage = StageOf();
        var hud = (Hud)_live!.GetType().GetProperty("Hud")!.GetValue(_live)!;
        int tut = lines.Length - tailCount - 16;   // 道中チュートリアルの先頭（紹介・説明ぶんを差し引く）
        // 話題は RouteCues の並び：5行目=移動 / 6行目=撃つ / 9行目=ロックオン / 11行目=解除 / 12行目=ボム。
        // 14行目（心の欠片）と紹介・説明の各行は None＝畳む。ここで話題まで見ないと「カードは在るが中身がズレた」
        // 状態（＝Route の添字ズレ）を見逃す。
        var want = new List<(int line, ControlCard.Topic topic, string label)>
        {
            (tut + 4,  ControlCard.Topic.Move,      "移動"),
            (tut + 5,  ControlCard.Topic.Shot,      "撃つ"),
            (tut + 8,  ControlCard.Topic.Lock,      "ロックオン"),
            (tut + 10, ControlCard.Topic.LockClear, "解除"),
            (tut + 11, ControlCard.Topic.Bomb,      "ボム"),
            (tut + 13, ControlCard.Topic.None,      "心の欠片(畳む)"),
            (lines.Length - tailCount, ControlCard.Topic.None, "紹介1行目(畳む)"),
        };
        if (items > 0) want.Add((lines.Length - items, ControlCard.Topic.None, "アイテム説明1行目(畳む)"));
        foreach (var (line, topic, label) in want)
        {
            SetLine(stage, line);
            await Frames(4);
            var card = hud.GetNodeOrNull<ControlCard>("ControlCard");
            var got = card == null ? ControlCard.Topic.None
                : (ControlCard.Topic)typeof(ControlCard).GetField("_next", Private)!.GetValue(card)!;
            Check($"{node} カード話題 {label}", got == topic, $"期待={topic} 実際={got}");
        }
    }

    private static void SetLine(Node stage, int index)
    {
        var lines = stage.GetType().GetField("_playerIntro", Private)!.GetValue(stage)!;
        stage.GetType().GetField("_introLine", Private)!.SetValue(stage, index);
        stage.GetType().GetMethod("ShowLine", Private)!.Invoke(stage, new[] { lines });
    }

    // ステージを立てて _playerIntro（_Ready で確定済み）を読む。
    //   CurrentScene は自分自身（QA ノード）なので触らない＝差し替えると自分ごと消えて止まる。
    //   立てたステージは _live に握っておき、次を立てる前にここで解放する。
    private Node? _live;

    // ルート（AkariRoot/KoharuRoot/ReiRoot）は型が違うが、どれも Stage プロパティでステージ本体を公開している。
    private Node StageOf() => (Node)_live!.GetType().GetProperty("Stage")!.GetValue(_live)!;

    private async Task<(int who, string text, string face)[]> IntroOf(string scene)
    {
        if (_live != null && IsInstanceValid(_live))
        {
            GetTree().Root.RemoveChild(_live);
            _live.QueueFree();
            _live = null;
            await Frames(2);
        }
        _live = GD.Load<PackedScene>(scene).Instantiate();
        GetTree().Root.AddChild(_live);
        await Frames(30);
        var stage = StageOf();
        var raw = (System.ValueTuple<int, string, string>[])
            stage.GetType().GetField("_playerIntro", Private)!.GetValue(stage)!;
        return raw.Select(t => (t.Item1, t.Item2, t.Item3)).ToArray();
    }

    private static string Tail((int who, string text, string face)[] a, int n)
        => a.Length >= n ? a[^n].text : "(短すぎ)";

    private void Check(string label, bool ok, string detail)
    {
        if (!ok) _fail++;
        GD.Print($"[ankerqa] {(ok ? "OK  " : "NG  ")}{label}{(ok ? "" : $" — {detail}")}");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
