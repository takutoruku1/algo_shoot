using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// HubDetailQa : 投稿詳細（Hub の Mode.Detail）の改修（2026-09-17）の自動検証。
//   ・段の表示名が難易度名（EASY/NORMAL/HARD/LUNATIC）になっていること。
//   ・「潜る」ボタンが無いこと（段そのものが確定ボタン）。
//   ・**詳細を開いた直後のクリック1回でダイブすること**（旧実装は _detailT>0.15 の入力ゲートが
//     開幕0.15秒のクリックを捨てており、ホバーで段だけが動く＝「1回目は選択、2回目で入る」に見えた）。
//   実行: Godot --headless --path . res://tools/qa_hub_detail.tscn
public partial class HubDetailQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);

    private int _fails;
    private void Check(bool ok, string message)
    {
        if (ok) GD.Print($"[HubDetailQA] PASS {message}");
        else { _fails++; GD.PrintErr($"[HubDetailQA] FAIL {message}"); }
    }

    public override async void _Ready()
    {
        try
        {
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            GetNode<PauseMenu>("/root/PauseMenu").SetProcess(false); // 決定論的な入力テストにポーズを混ぜない
            await Frames(2);

            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(30);

            // 段の名前（Hub.Tiers は private static なのでリフレクションで読む）。
            var tiers = (Array)typeof(Hub).GetField("Tiers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            string names = "";
            foreach (var t in tiers)
                names += (names.Length > 0 ? "/" : "") + (string)t.GetType().GetField("Name")!.GetValue(t)!;
            Check(names == "EASY/NORMAL/HARD/LUNATIC", $"tier labels: {names}");
            Check(t0(tiers).GetType().GetField("Quip") == null, "the tier struct no longer carries ミナの一言 (Quip)");
            Check(typeof(Hub).GetMethod("DetailConfirmRect", Private) == null, "the 潜る button geometry is gone");
            Check(typeof(Hub).GetField("DetailConfirmId", BindingFlags.Static | BindingFlags.NonPublic) == null,
                "the 潜る button hotspot id is gone");

            // 「声のある投稿」を選んで詳細を開く（ProcessCards の Z 経路と同じ OpenDetail を呼ぶ）。
            Write(hub, "_mode", Enum.Parse(typeof(Hub).GetNestedType("Mode", Private)!, "Cards"));
            await Frames(2);
            int voice = FindVoiceCard(hub);
            Check(voice >= 0, $"found a divable post card (index {voice})");
            Write(hub, "_sel", voice);
            Call(hub, "OpenDetail");
            Check(Read<object>(hub, "_mode").ToString() == "Detail", "the card opens the post detail");

            // ★開いた直後の1クリックでダイブする（_detailT はまだ ~0）。
            double detailT = Read<double>(hub, "_detailT");
            Check(detailT < 0.15, $"the click lands inside the old input gate window (_detailT={detailT:0.000})");
            var tierRect = (Rect2)Call(hub, "TierHitRect", 1)!; // NORMAL
            ClickAt(hub, tierRect, "ProcessDetail", 1.0 / 60.0);
            Check(Read<bool>(hub, "_dived"), "one click on a tier dives immediately (no second click needed)");
            Check(game.Difficulty == GameManager.Diff.Normal, $"the clicked tier set the difficulty ({game.Difficulty})");
        }
        catch (Exception e)
        {
            _fails++;
            GD.PrintErr($"[HubDetailQA] EXCEPTION {e}");
        }
        GD.Print(_fails == 0 ? "[HubDetailQA] ALL PASS" : $"[HubDetailQA] {_fails} FAILURE(S)");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    private static object t0(Array tiers) => tiers.GetValue(0)!;

    // 「声のある投稿」かつ解放済みのカード＝詳細を開けるカードの index。
    private static int FindVoiceCard(Hub hub)
    {
        var entries = (Array)hub.GetType().GetField("_entries", Private)!.GetValue(hub)!;
        for (int i = 0; i < entries.Length; i++)
        {
            object e = entries.GetValue(i)!;
            var sort = e.GetType().GetField("Sort")!.GetValue(e)!;
            bool unlocked = (bool)e.GetType().GetField("Unlocked")!.GetValue(e)!;
            bool isFinal = (bool)e.GetType().GetField("IsFinal")!.GetValue(e)!;
            if (sort.ToString() == "Voice" && unlocked && !isFinal) return i;
        }
        return -1;
    }

    // デスクトップのカーソルを動かさず Pad の内部状態へ設計座標の押下を差し込み、ハンドラを直に呼ぶ
    //   （Pad.PollMouse が毎フレーム実マウスで上書きするので _Process 経由にはできない。HubJobQa と同じ手口）。
    private static void PadField(string name, object value)
        => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    private static void ClickAt(Hub hub, Rect2 rect, string handler, params object[] args)
    {
        Vector2 previous = Pad.MousePos();
        PadField("_mousePos", rect.GetCenter());
        PadField("_usingMouse", true);
        PadField("_mL", true);
        PadField("_mLPrev", false);
        Call(hub, handler, args);
        PadField("_mousePos", previous);
        PadField("_mL", false);
        PadField("_mLPrev", false);
        PadField("_usingMouse", false);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
