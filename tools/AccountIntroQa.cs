using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// AccountIntroQa : アカウント追加の説明（2026-09-23・docs/20260923/アカウント追加説明_本文_2026-09-23.md A案）と
//   サイドパネル立ち絵の大きさ統一の自動検証＋スクショ。
//     1) あかり初回の帰還会話（H1）に説明3行が「被弾は{n}回でした」の直後・「……お疲れさまでした。」の前へ差し込まれる
//     2) 読み切る（EndDialogue）と once_account_intro が立ち、AccountNudgePending（フッタ誘導の予約）が立つ
//     3) 同じセーブで2回目（再クリア）には差し込まれない／こはる後にも出ない
//     4) SNS（Cards）のフッタ「アカウント」が脈動し、OpenJob（押す）で誘導が消える
//     5) 左パネルの立ち絵：4人の顔の高さ（目線→顎）が画面上で同じ大きさになっている（SidePortraitFace の比で確認）
//     6) スクショ：左パネル4人／右パネル final／説明中の会話／フッタ誘導／写真アプリを最大スクロール
//   実行: APPDATA=<隔離> Godot --path . res://tools/qa_account_intro.tscn   （--qa は付けない＝実プレイと同じ非オートで見る）
public partial class AccountIntroQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object? value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static object? Prop(object obj, string name) => obj.GetType().GetProperty(name, Private)!.GetValue(obj);
    private static string Mode(Hub hub) => Read<object>(hub, "_mode").ToString()!;
    private static void SetMode(Hub hub, string mode)
    {
        var cur = Read<object>(hub, "_mode");
        Write(hub, "_mode", Enum.Parse(cur.GetType(), mode));
    }

    private const string IntroHead = "ご報告。アカウントが、ひとつ、増えています。……名義は、わたくしではありません。あの方です。";
    private const string IntroLast = "光の形も、そこで語られる話も、あの方のものになります。……戻すのも、同じ場所からです。";
    private const string SeenKey = "once_account_intro";

    private int _fail;
    private Hub? _hub;

    public override async void _Ready()
    {
        await Frames(2);
        Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data", OS.GetUserDataDir());
        var game = GetNode<GameManager>("/root/Game");
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(1);
        GetNode<PauseMenu>("/root/PauseMenu").SetProcess(false);

        // ── A: 左パネル立ち絵の大きさ（4人）・右パネル final ──
        game.ResetPersistent();
        var hub = await Spawn();
        hub.SetProcess(false);
        Write(hub, "_t", 3d);
        float minaFace = 0;
        foreach (var job in Jobs.All)
        {
            game.SelectedJob = job.Id;
            hub.QueueRedraw();
            await Frames(3);
            await Shot($"side_{job.CharacterId}");
            // 画面上の顔の高さ＝face × (780/1536) × (ミナの face / その face) ＝ ミナと同じ値になるはず
            var (_, face) = ((Vector2, float))typeof(Hub).GetMethod("SidePortraitFace", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { job.CharacterId })!;
            var (_, mina) = ((Vector2, float))typeof(Hub).GetMethod("SidePortraitFace", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { "mina" })!;
            float onScreen = face * (780f / 1536f) * (mina / face);
            if (minaFace == 0) minaFace = onScreen;
            Check(Mathf.Abs(onScreen - minaFace) < 0.01f, $"{job.CharacterId} face height on screen == mina ({onScreen:0.0}px)");
        }
        game.SelectedJob = Job.Tank;
        Write(hub, "_sideStoryId", "final");
        hub.QueueRedraw();
        await Frames(3);
        await Shot("story_final");
        Write(hub, "_sideStoryId", "akari");
        hub.QueueRedraw();
        await Frames(3);
        await Shot("story_akari");

        // ── C: 写真アプリを最大スクロール（指揮官の DrawPhotos 修正の目視用） ──
        SetMode(hub, "Photos");
        Write(hub, "_photoT", 1d);
        Write(hub, "_homeSel", 3);
        float maxScroll = (float)Call(hub, "PhotoMaxScroll")!;
        Write(hub, "_photoScroll", maxScroll);
        Write(hub, "_photoScrollTarget", maxScroll);
        hub.QueueRedraw();
        await Frames(3);
        await Shot("photos_scrolled_max");
        GD.Print($"[accountqa] photos max scroll = {maxScroll}");
        Write(hub, "_photoScroll", 0f);
        Write(hub, "_photoScrollTarget", 0f);
        hub.QueueRedraw();
        await Frames(3);
        await Shot("photos_scrolled_0");
        await Despawn();

        // ── B-1: あかり初回の帰還＝説明3行が差し込まれる ──
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
        game.CompleteStage("akari");
        hub = await Spawn();
        Check(Mode(hub) == "Dialogue", "return dialogue starts after clearing akari", Mode(hub));
        var dlg = Read<(string sp, string tx)[]>(hub, "_dlg");
        int head = Array.FindIndex(dlg, l => l.tx == IntroHead);
        Check(head >= 0, "account intro is in the return dialogue");
        if (head >= 0)
        {
            Check(dlg.Length > head + 2 && dlg[head + 2].tx == IntroLast, "account intro keeps its 3 lines in order");
            Check(head > 0 && dlg[head - 1].tx.Contains("被弾は"), "account intro follows the hit-count line", head > 0 ? dlg[head - 1].tx : "(none)");
            Check(dlg.Length > head + 3 && dlg[head + 3].tx.StartsWith("……お疲れさまでした"), "account intro precedes 'お疲れさまでした'",
                dlg.Length > head + 3 ? dlg[head + 3].tx : "(none)");
            Check(dlg[^1].tx == "次の声も、もう、聞こえています。", "the closing line stays last", dlg[^1].tx);
            Check(dlg.Skip(head).Take(3).All(l => l.sp == "ミナ"), "account intro speaker is ミナ");
        }
        Check((string?)Read<object?>(hub, "_dlgSeenKey") == SeenKey, "dialogue carries the once key");
        Check(!game.IsIdleDialogSeen(SeenKey), "once key is not consumed before reading");
        Check(!game.AccountNudgePending, "nudge is not reserved before reading");
        // 説明の1行目を表示した状態を撮る
        Write(hub, "_dlgIdx", head);
        Write(hub, "_dlgReveal", 999f);
        Write(hub, "_dlgPagedIdx", -1);
        hub.QueueRedraw();
        await Frames(4);
        await Shot("dialogue_account_intro");
        // 読み切る＝EndDialogue（HomeReveal → ShopTutorial への遷移が走る前にハブを畳む）
        hub.SetProcess(false);
        Call(hub, "EndDialogue");
        Check(game.IsIdleDialogSeen(SeenKey), "once key is consumed after reading");
        Check(game.AccountNudgePending, "footer nudge is reserved after reading");
        await Despawn();

        // ── B-2: 2回目（再クリア）＝差し込まれない。予約した誘導は残る ──
        game.CompleteStage("akari");
        hub = await Spawn();
        dlg = Read<(string sp, string tx)[]>(hub, "_dlg");
        Check(Mode(hub) == "Dialogue" && dlg.All(l => l.tx != IntroHead), "second return does not repeat the account intro");
        Check(Read<object?>(hub, "_dlgSeenKey") == null, "second return carries no once key");
        Check(game.AccountNudgePending, "nudge reservation survives the next hub entry");
        hub.SetProcess(false);
        Call(hub, "EndDialogue");
        Check(game.AccountNudgePending, "a dialogue without the intro does not clear the nudge");
        // SNS（Cards）でフッタ「アカウント」が脈動する
        SetMode(hub, "Cards");
        Write(hub, "_t", 2.4d);
        Write(hub, "_toastT", 0d);
        hub.QueueRedraw();
        await Frames(3);
        Check((bool)Prop(hub, "AccountNudge")!, "footer nudge is visible on the timeline");
        await Shot("footer_account_nudge");
        // 押す（OpenJob）＝誘導が消える
        Call(hub, "OpenJob");
        Check(Mode(hub) == "Job", "OpenJob opens the account switcher", Mode(hub));
        Check(!game.AccountNudgePending && !(bool)Prop(hub, "AccountNudge")!, "pressing アカウント clears the nudge");
        await Despawn();

        // ── B-3: こはる後には出ない ──
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
        game.CompleteStage("akari");
        game.MarkIdleDialogSeen(SeenKey);
        game.CompleteStage("koharu");
        hub = await Spawn();
        dlg = Read<(string sp, string tx)[]>(hub, "_dlg");
        Check(dlg.All(l => l.tx != IntroHead), "koharu's return has no account intro");
        Check(!game.AccountNudgePending, "koharu's return reserves no nudge");
        hub.SetProcess(false);
        await Despawn();

        GD.Print(_fail == 0 ? "[accountqa] ALL OK" : $"[accountqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private async Task<Hub> Spawn()
    {
        _hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(_hub);
        await Frames(30);
        return _hub;
    }

    private async Task Despawn()
    {
        if (_hub == null) return;
        GetTree().Root.RemoveChild(_hub);
        _hub.QueueFree();
        _hub = null;
        await Frames(2);
    }

    private void Check(bool ok, string label, string detail = "")
    {
        if (!ok) _fail++;
        GD.Print($"[accountqa] {(ok ? "OK  " : "NG  ")}{label}{(ok || detail.Length == 0 ? "" : $" — {detail}")}");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/account_intro/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
