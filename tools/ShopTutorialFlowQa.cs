using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

// ShopTutorialFlowQa : 強化ショップの説明パート（初回のボス撃破後・一度きり）の導線検査。
//   2026-10-03 の修正（専用シーン ShopTutorial.tscn を廃止し、説明をハブの会話として同じ画面で流す）が
//   狙いどおりかを機械的に見る。ユーザー報告は「最初の強化ショップの入り方がおかしい＝何も表示されない
//   強化ショップでミナと会話してから、ホームの強化アイコンを押す流れになっている」。
//     (1) 初回のあかりクリアでハブへ帰ると、帰還会話 → ホーム解禁演出 → そのまま説明9行が同じ画面で流れる
//     (2) 説明の前後を通して画面遷移が一度も起きない（LoadingScreen／PhoneAppTransition が立たない・
//         CurrentScene が変わらない・ハブが _dived を立てない）
//     (3) 本文は ShopTutorialLines のまま・立ち絵も行ごと（8行目だけ mina_worried）・9行とも会話ログに残る
//     (4) 読み切るとホームに戻り、強化アイコンが誘導表示（_shopNudge）＋カーソルがアイコンに乗る。
//         最終行を送った Z がそのまま「ひらく」に漏れない
//     (5) ShopTutorialSeen が save_0.json に保存される
//     (6) 二周目（同じセーブで再クリア）には流れない
//     (7) 解禁演出を経ない入場（ホームから）でも一度だけ流れる＝TryOpenShopTutorial の二重の入口が生きている
//     (8) そのあと強化アイコンを押すと、そこで初めてショップへの遷移が立つ
//   実行（セーブは隔離。本物の %APPDATA%\Godot\app_userdata\Refrain には触らない）:
//     APPDATA=D:/dev/algo_shoot/build/qa_story/shop_tutorial_appdata \
//     Godot_v4.6.3-stable_mono_win64 --path . res://tools/qa_shop_tutorial.tscn
//   ★--headless では動かさない。止まりはしないが (1)(2) の2件が必ず NG になる＝偽陽性。
//     解禁演出（Hub._homeSnapshot）がビューポートを1枚取る作りで、描画の無いヘッドレスでは演出が成立せず、
//     「帰還会話 → 解禁演出 → 説明」の受け渡しを踏まないまま Home に着いてしまうため。窓ありで動かすこと。
//     （スクショ自体は撮らない。--st-shot を付けたときだけ2枚保存する＝これも窓あり限定。）
public partial class ShopTutorialFlowQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object? value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static string Mode(Hub hub) => Read<object>(hub, "_mode").ToString()!;
    private static string Field(Hub hub, string name) => Read<object>(hub, name).ToString()!;
    private static (string sp, string tx, string face)[] Lines =>
        ((string sp, string tx, string face)[])typeof(Hub)
            .GetField("ShopTutorialLines", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    // 会話ログの種別（Hub.DialogLogKind）。話者文字列が従来の who と同じ種別に落ちることを見るために使う。
    private static Hud.LineKind LogKind(string sp) => (Hud.LineKind)typeof(Hub)
        .GetMethod("DialogLogKind", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { sp })!;

    private int _fail;
    private Hub? _hub;
    private string? _violation;
    private bool _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;   // 遷移が立った場合でもツリーポーズに巻き込まれず検査を続ける
        // 窓ありで `-- --st-shot` を付けると build/qa_story/shop_tutorial/shots/ に見え方を2枚保存する
        //   （説明がホームの上に出ている図と、読み切ったあとの誘導）。ヘッダレスでは付けない（FramePostDraw を待つ）。
        _shots = Array.IndexOf(OS.GetCmdlineUserArgs(), "--st-shot") >= 0;
        await Frames(2);
        Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data", OS.GetUserDataDir());
        var game = GetNode<GameManager>("/root/Game");
        GetNode<PauseMenu>("/root/PauseMenu").SetProcess(false);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(1);

        // ───────── (1)〜(5) 初回のあかりクリア → ハブ帰還 ─────────
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
        game.Difficulty = GameManager.Diff.Normal;
        game.CompleteStage(GameManager.FirstStageId);
        Hud.ClearBacklog();
        var hub = await Spawn();
        Check(Mode(hub) == "Dialogue", "the first clear opens the return dialogue", Mode(hub));
        Check(!game.ShopTutorialSeen, "the shop tutorial is still unseen on arrival");

        // 帰還会話（＋アカウント追加の説明）を Z で読み切る → ホーム解禁演出へ
        for (int i = 0; i < 400 && Mode(hub) == "Dialogue"; i++) { await Keypress(Key.Z); Guard(hub, "return dialogue"); }
        Check(Mode(hub) == "HomeReveal", "the return dialogue hands over to the home reveal", Mode(hub));

        // 解禁演出（2.3秒）のあと、同じ画面のまま説明が始まる
        for (int i = 0; i < 900 && Mode(hub) != "Dialogue"; i++) { await Frames(1); Guard(hub, "home reveal"); }
        Check(Mode(hub) == "Dialogue" && Read<bool>(hub, "_shopTutorialDlg"),
            "(1) the shop tutorial starts in place, right after the reveal", $"mode={Mode(hub)}");
        CheckNoTransition("(2) up to the start of the tutorial");

        // (3) 本文と立ち絵：ShopTutorialLines のまま（話者はハブの「ミナ」＝会話ログの種別も Mina）
        var dlg = Read<(string sp, string tx)[]>(hub, "_dlg");
        var faces = Read<string[]>(hub, "_dlgFaces");
        var data = Lines;
        Check(dlg.Length == data.Length && faces.Length == data.Length,
            $"(3) the tutorial runs all {data.Length} lines", $"dlg={dlg.Length} faces={faces.Length}");
        bool same = dlg.Length == data.Length && faces.Length == data.Length;
        for (int i = 0; same && i < data.Length; i++)
            same = dlg[i].tx == data[i].tx && dlg[i].sp == data[i].sp && faces[i] == data[i].face;
        Check(same, "(3) every line keeps its text, speaker and portrait path");
        Check(Array.TrueForAll(dlg, l => LogKind(l.sp) == Hud.LineKind.Mina),
            "(3) every line logs as ミナ (Hud.LineKind.Mina)");
        var worried = (Texture2D?)Call(hub, "LineFace", 7);
        Check(worried != null && worried.ResourcePath == "res://char/mina_worried.png",
            "(3) line 8 shows the worried portrait", worried?.ResourcePath ?? "(null)");
        var plain = (Texture2D?)Call(hub, "LineFace", 0);
        Check(plain != null && plain.ResourcePath == "res://char/mina_face.png",
            "(3) the other lines show the plain portrait", plain?.ResourcePath ?? "(null)");
        Check(Field(hub, "_dlgReturnMode") == "Home", "the tutorial returns to the phone home", Field(hub, "_dlgReturnMode"));
        await Shot("tutorial_in_place");   // ホーム（光った強化アイコン）の上に説明が出ている図

        // 説明を読み切る（遷移が立たないことを1フレームずつ見張る）
        for (int i = 0; i < 400 && Mode(hub) == "Dialogue"; i++) { await Keypress(Key.Z); Guard(hub, "shop tutorial"); }
        CheckNoTransition("(2) through the whole tutorial");
        Check(Mode(hub) == "Home", "(4) reading it through lands on the phone home", Mode(hub));
        Check(Read<bool>(hub, "_shopNudge"), "(4) the upgrade icon is nudging");
        Check(Read<int>(hub, "_homeSel") == 1, "(4) the cursor sits on the upgrade icon", Read<int>(hub, "_homeSel").ToString());
        Check(!Read<bool>(hub, "_shopTutorialDlg"), "(4) the tutorial flag is consumed");
        Check(!Read<bool>(hub, "_dived") && !LoadingScreen.IsActive,
            "(4) the Z that finished the last line does not open the shop by itself");
        await Shot("home_shop_nudge");   // 読み切ったあと＝強化アイコンが脈動しているホーム
        // 会話ログ（L／Tab で開く Backlog）に9行とも残る
        int logged = 0;
        foreach (var line in data)
            foreach (var entry in Hud.Backlog)
                if (entry.Text == line.tx) { logged++; break; }
        Check(logged == data.Length, $"(3) all {data.Length} lines reach the dialogue log", $"logged={logged}");

        // (5) 一度きりの印が保存されている
        Check(game.ShopTutorialSeen, "(5) ShopTutorialSeen is set in memory");
        Check(SavedShopTutorialSeen(), "(5) ShopTutorialSeen is written to save_0.json");
        await Despawn();

        // ───────── (6) 二周目（同じセーブで再クリア）には流れない ─────────
        Hud.ClearBacklog();
        game.CompleteStage(GameManager.FirstStageId);
        hub = await Spawn();
        for (int i = 0; i < 400 && Mode(hub) != "Home"; i++) { await Keypress(Key.Z); Guard(hub, "second visit"); }
        Check(Mode(hub) == "Home", "the second visit also ends up on the phone home", Mode(hub));
        Check(!Read<bool>(hub, "_shopTutorialDlg") && !Read<bool>(hub, "_shopNudge"),
            "(6) the second visit neither replays the tutorial nor nudges");
        int replayed = 0;
        foreach (var entry in Hud.Backlog)
            foreach (var line in data)
                if (entry.Text == line.tx) { replayed++; break; }
        Check(replayed == 0, "(6) none of the tutorial lines show up again", $"replayed={replayed}");
        CheckNoTransition("(2) on the second visit");
        await Despawn();

        // ───────── (7) 解禁演出を経ない入場でも一度だけ流れる（取りこぼし防止の入口）─────────
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
        Read<HashSet<string>>(game, "_cleared").Add(GameManager.FirstStageId);
        Hud.ClearBacklog();
        hub = await Spawn();
        Write(hub, "_idleTalkPending", false);   // 再訪小話は乱数なので止める（見たいのは説明の導線だけ）
        for (int i = 0; i < 120 && Mode(hub) != "Dialogue"; i++) { await Frames(1); Guard(hub, "home entry"); }
        Check(Mode(hub) == "Dialogue" && Read<bool>(hub, "_shopTutorialDlg"),
            "(7) entering the home without the reveal still runs the tutorial once", Mode(hub));
        for (int i = 0; i < 400 && Mode(hub) == "Dialogue"; i++) { await Keypress(Key.Z); Guard(hub, "home entry tutorial"); }
        Check(Mode(hub) == "Home" && Read<bool>(hub, "_shopNudge"), "(7) it ends on the nudging home as well");
        CheckNoTransition("(2) on the home entry path");

        // ───────── (8) そこで初めて強化アイコンを押す → ショップへ ─────────
        await Keypress(Key.Z);
        Check(Read<bool>(hub, "_dived"), "(8) pressing the upgrade icon leaves the hub");
        Check(!Read<bool>(hub, "_shopNudge"), "(8) pressing it clears the nudge");
        for (int i = 0; i < 120 && !LoadingScreen.IsActive; i++) await Frames(1);
        Check(LoadingScreen.IsActive, "(8) the shop transition starts only now");
        var screen = GetTree().Root.GetNodeOrNull<LoadingScreen>("LoadingScreen");
        Check(screen != null && Read<string>(screen, "_destination") == "res://Shop.tscn",
            "(8) the destination is the real shop", screen == null ? "(no loading screen)" : Read<string>(screen, "_destination"));

        GD.Print(_fail == 0 ? "[shoptutqa] ALL OK" : $"[shoptutqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private bool SavedShopTutorialSeen()
    {
        using var f = FileAccess.Open("user://save_0.json", FileAccess.ModeFlags.Read);
        if (f == null) return false;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return false;
        var dict = parsed.AsGodotDictionary();
        return dict.ContainsKey("shopTutorialSeen") && dict["shopTutorialSeen"].AsBool();
    }

    private async Task<Hub> Spawn()
    {
        _violation = null;
        _hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(_hub);
        GetTree().CurrentScene = _hub;
        await Frames(10);
        await Seconds(0.5);   // ハブの入力ゲート（_t > 0.3）を実時間で越える
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

    // 「この区間で画面が変わっていないか」を1フレームずつ見張る（最初の違反だけ覚える）。
    private void Guard(Hub hub, string where)
    {
        if (_violation != null) return;
        if (LoadingScreen.IsActive) _violation = $"{where}: LoadingScreen opened";
        else if (GetTree().Root.GetNodeOrNull("PhoneAppTransition") != null) _violation = $"{where}: PhoneAppTransition opened";
        else if (GetTree().CurrentScene != hub) _violation = $"{where}: CurrentScene became {GetTree().CurrentScene?.Name}";
        else if (Read<bool>(hub, "_dived")) _violation = $"{where}: the hub marked itself as leaving (_dived)";
    }

    private void CheckNoTransition(string label)
    {
        Check(_violation == null, $"no screen transition {label}", _violation ?? "");
        _violation = null;
    }

    private async Task Shot(string name)
    {
        if (!_shots) return;
        await Seconds(1.2);   // タイプライターが1行ぶん出るまで待つ（送りは押さないので行は進まない）
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/shop_tutorial/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }

    private void Check(bool ok, string label, string detail = "")
    {
        if (!ok) _fail++;
        GD.Print($"[shoptutqa] {(ok ? "OK  " : "NG  ")}{label}{(ok || detail.Length == 0 ? "" : $" — {detail}")}");
    }

    private async Task Keypress(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(4);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
