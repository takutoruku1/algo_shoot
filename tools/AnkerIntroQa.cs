using Godot;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// AnkerIntroQa : アンチャー紹介（StageTutorial の ③・2026-09-17）の発火とカード共存の自動検証。
//   各ステージを実際に立てて _playerIntro を読み、以下を判定する：
//     1) 結び手（ミナ潜行）の初回突入で、道中チュートリアル16行の「後ろ」に紹介が付いている
//     2) 同じセーブで2回目に入ると付かない（once が面ごとに効いている）
//     3) 操作カード（ControlCard）が従来どおり出る＝SyncCard が Route の各行を引き当てられる
//        （Route のさらに後ろへ Concat しても添字がズレない＝今回の落とし穴の回避確認）
//     4) 他ジョブ潜行中は出ない・once も消費しない
//   実行: Godot --headless --path . res://tools/qa_anker_intro.tscn -- --qa
public partial class AnkerIntroQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // 本文先頭行（docs/20260917/アンチャー紹介_本文_2026-09-17.md）。実装と本文の一致確認も兼ねる。
    private static readonly (string scene, string name, string key, int count, string head)[] Cases =
    {
        ("res://Akari.tscn",  "あかり", StageTutorial.AnkerAkariSeenKey,  3,
            "この階のアンチャーは、間合いを詰めてまいります。落ちてくるもの、途中から速くなるもの。"),
        ("res://Koharu.tscn", "こはる", StageTutorial.AnkerKoharuSeenKey, 3,
            "ここのアンチャーは、一度に、たくさん寄越してきます。構えたと思った次の瞬間には、もう来ています。"),
        ("res://Rei.tscn",    "レイ",   StageTutorial.AnkerReiSeenKey,    4,
            "この枠のアンチャーは、正面から来ません。上から、まわりから、背中から。"),
    };

    private int _fail;

    public override async void _Ready()
    {
        await Frames(2);   // root が子のセットアップ中は AddChild できない＝1フレーム待ってから始める
        var game = GetNode<GameManager>("/root/Game");

        foreach (var (scene, node, key, count, head) in Cases)
        {
            // ── 1回目（結び手・未読）＝紹介が出る ──
            game.ResetPersistent();
            game.SelectedJob = Job.Tank;
            var first = await IntroOf(scene);
            Check($"{node} 1回目 紹介{count}行", first.Length >= count
                && first[^count].text == head, $"末尾{count}行の先頭='{Tail(first, count)}'");
            Check($"{node} 1回目 once 消費", game.IsIdleDialogSeen(key), "未消費");

            // 紹介はチュートリアル16行の「後ろ」＝順序が 一般→個別 になっている
            Check($"{node} 順序(チュートリアル→紹介)",
                first.Length >= count + 16 && first[^(count + 1)].text.Contains("オペレータのお仕事"),
                $"紹介の直前='{(first.Length >= count + 1 ? first[^(count + 1)].text : "(なし)")}'");

            // ── 操作カード：チュートリアル16行の各行で SyncCard が話題を引けるか ──
            await CardCheck(node, first, count);

            // ── 2回目（同じセーブ）＝紹介もチュートリアルも出ない ──
            var second = await IntroOf(scene);
            Check($"{node} 2回目 出ない", second.Length > 0 && second[^1].text != first[^1].text,
                $"末尾='{second.LastOrDefault().text}'");

            // ── 他ジョブ潜行＝出ない・once も消費しない ──
            game.ResetPersistent();
            game.SelectedJob = Job.Melee;   // 結び手以外＝キャラ別ストーリー潜行中
            var other = await IntroOf(scene);
            Check($"{node} 他ジョブで出ない", other.Length < count || other[^count].text != head,
                $"末尾{count}行の先頭='{Tail(other, count)}'");
            Check($"{node} 他ジョブで once 未消費", !game.IsIdleDialogSeen(key), "消費されている");
        }

        GD.Print(_fail == 0 ? "[ankerqa] ALL OK" : $"[ankerqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // 操作カードの同期確認。チュートリアル16行の位置で ShowLine を呼び、カードの Topic が期待どおり動くか見る。
    //   紹介を Route の後ろへ足したことで添字がズレていれば、カードが一切出ない＝ここで落ちる。
    private async Task CardCheck(string node, (int who, string text, string face)[] lines, int ankerCount)
    {
        var stage = StageOf();
        var hud = (Hud)_live!.GetType().GetProperty("Hud")!.GetValue(_live)!;
        int tut = lines.Length - ankerCount - 16;   // 道中チュートリアルの先頭（紹介ぶんを差し引く）
        // 話題は RouteCues の並び：5行目=移動 / 6行目=撃つ / 9行目=ロックオン / 11行目=解除 / 12行目=ボム。
        // 14行目（心の欠片）と紹介の各行は None＝畳む。ここで話題まで見ないと「カードは在るが中身がズレた」
        // 状態（＝Route の添字ズレ）を見逃す。
        (int line, ControlCard.Topic want, string label)[] want =
        {
            (tut + 4,  ControlCard.Topic.Move,      "移動"),
            (tut + 5,  ControlCard.Topic.Shot,      "撃つ"),
            (tut + 8,  ControlCard.Topic.Lock,      "ロックオン"),
            (tut + 10, ControlCard.Topic.LockClear, "解除"),
            (tut + 11, ControlCard.Topic.Bomb,      "ボム"),
            (tut + 13, ControlCard.Topic.None,      "心の欠片(畳む)"),
            (lines.Length - ankerCount, ControlCard.Topic.None, "紹介1行目(畳む)"),
        };
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
