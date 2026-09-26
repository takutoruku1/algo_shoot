using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// DialogToolbarQa : 会話ボックス上辺のボタン列（AUTO / SKIP / LOG / MENU・src/DialogToolbar.cs）の回帰確認。
//   Akari シーンを立ち上げ、戦闘を止めたまま hud.HoldBubble=true で会話ボックスを出して、キー割り当てを検証する。
//     (e) ボックス非表示のとき A／S は何もしない
//     (a) A で GameManager.AutoAdvanceDialog が反転（user://settings.json の "auto" にも保存）
//     (b) S で SkipLatched：未読行では即 OFF／既読行では ON のまま FastForwarding が真／次の未読行・選択肢・
//         ボックスを閉じる、のそれぞれで自動 OFF
//     (c) L で Backlog.IsOpen ／ (d) M で PauseMenu.IsOpen、MENU クリック経路（PauseMenu.Open の Call）も開く
//     (g) ボタン列にマウスが乗っている間は左クリックが会話送り（Pad.AdvanceHeld）に数えられない
//     (h) Hud を通らない画面（Prologue／Epilogue／Hub の会話）にもボタン列が出て、枠の上辺右寄せに並び、
//         A で AUTO（全文表示後に自動で送る）、S で SKIP（未読行で即 OFF／既読行で早送り／選択肢・未読行・会話の終わりで OFF）
//   起動: --headless --path . res://tools/qa_dialog_toolbar.tscn
//         窓あり＋撮影: res://tools/qa_dialog_toolbar.tscn -- --dt-shot [--dt-out <dir>]
//           戦闘会話（AUTO 点灯）・ナレーション（SKIP 点灯）・カットシーン（StoryFilm 回想）・
//           プロローグ（AUTO 点灯）・ハブの返信会話（AUTO 点灯）の5枚を保存する。
//   APPDATA は build/qa_story/ 配下へ隔離して走らせる（settings.json／既読を本番に書かない）。
public partial class DialogToolbarQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private string _out = "";
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value) => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static bool Shown(object obj, string prop) => (bool)obj.GetType().GetProperty(prop, Private)!.GetValue(obj)!;
    private static Vector2 Anchor(Type t) =>
        t.GetField("ToolbarAnchor", PrivateStatic) is { } f ? (Vector2)f.GetValue(null)! : (Vector2)t.GetProperty("ToolbarAnchor", PrivateStatic)!.GetValue(null)!;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[Toolbar] PASS {message}");
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _ = Run();
    }

    private async Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        bool shot = Array.IndexOf(args, "--dt-shot") >= 0;
        _out = ProjectSettings.GlobalizePath("res://build/shots_toolbar");
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--dt-out" && i + 1 < args.Length) _out = args[i + 1];
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated user data");
            if (shot)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                DirAccess.MakeDirRecursiveAbsolute(_out);
            }
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.AutoAdvanceDialog = false;
            game.SelectedJob = Job.Tank;
            game.SelectedEntry = GameManager.StageEntry.Boss;

            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            await Frames(1);
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var hud = root.GetNode<Hud>("Hud");
            var world = root.GetNode<Node2D>("World");
            root.SetProcess(false);
            root.GetNode<Node>("StageAkari").SetProcess(false);
            world.GetNode<Player>("Player").SetPhysicsProcess(false);
            await Frames(5);
            hud.HideBubble();
            hud.HoldBubble = true;
            var backlog = GetNode<Backlog>("/root/Backlog");
            var pause = GetNode<PauseMenu>("/root/PauseMenu");
            var readField = typeof(Hud).GetField("_dlgReadBefore", Private)!;
            string stamp = Time.GetTicksMsec().ToString();

            // (e) ボックス非表示では何もしない
            await Seconds(0.5);
            Check(!hud.DialogToolbarVisible, "(e) no dialog box on screen");
            await Tap(Key.A);
            await Tap(Key.S);
            Check(!game.AutoAdvanceDialog, "(e) A does nothing while no box is shown");
            Check(!Hud.SkipLatched, "(e) S does nothing while no box is shown");

            // (a) AUTO
            hud.ShowDialog(Hud.LineKind.Mina, $"ツールバー検証の一行目です。{stamp}");
            await Seconds(0.45);
            Check(hud.DialogToolbarVisible, "(a) box is shown and the toolbar is live");
            await Tap(Key.A);
            Check(game.AutoAdvanceDialog, "(a) A turns AUTO on (GameManager.AutoAdvanceDialog)");
            Check(ReadSettingsAuto() == true, "(a) settings.json \"auto\" saved as true");
            await Tap(Key.A);
            Check(!game.AutoAdvanceDialog, "(a) A again turns AUTO off");
            Check(ReadSettingsAuto() == false, "(a) settings.json \"auto\" saved as false");

            // (b) SKIP
            await Tap(Key.S);
            Check(!Hud.SkipLatched, "(b) S on an unread line: latch drops immediately");
            readField.SetValue(hud, true);   // この行を「表示時点で既読だった」ことにする
            await Tap(Key.S);
            Check(Hud.SkipLatched, "(b) S on a read line: latch stays ON");
            Check(Hud.SkipHeld, "(b) SkipHeld is true while latched");
            Check(hud.FastForwarding, "(b) FastForwarding is true on the read line");
            await Frames(10);
            Check(Hud.SkipLatched, "(b) latch holds across frames on a read line");
            hud.ShowDialog(Hud.LineKind.Mina, $"ツールバー検証の未読の二行目です。{stamp}");
            await Frames(3);
            Check(!Hud.SkipLatched, "(b) next unread line turns the latch off");
            readField.SetValue(hud, true);
            await Tap(Key.S);
            Check(Hud.SkipLatched, "(b) latch re-armed");
            var choice = ChoiceOverlay.Show(root, new[] { "はい", "いいえ" }, 0, onBoard: true);
            await Frames(3);
            Check(!Hud.SkipLatched, "(b) a choice appearing turns the latch off");
            choice.QueueFree();
            await Frames(3);
            readField.SetValue(hud, true);
            await Tap(Key.S);
            Check(Hud.SkipLatched, "(b) latch re-armed again");
            hud.HideBubble();
            await Seconds(0.35);
            Check(!Hud.SkipLatched, "(b) closing the box turns the latch off");

            // (c) LOG ＝ L
            hud.ShowDialog(Hud.LineKind.Mina, $"ツールバー検証の三行目です。{stamp}");
            await Seconds(0.45);
            await Tap(Key.L);
            Check(backlog.IsOpen, "(c) L opens the backlog while the box is shown");
            await Tap(Key.X);
            await Seconds(0.2);
            Check(!backlog.IsOpen, "(c) backlog closed again");
            await Seconds(0.2);

            // (d) MENU ＝ M ／ クリック経路
            await Tap(Key.M);
            Check(pause.IsOpen, "(d) M opens the pause menu while the box is shown");
            await Tap(Key.M);
            await Seconds(0.2);
            Check(!pause.IsOpen, "(d) pause menu closed again");
            GetTree().Paused = false;
            await Seconds(0.2);
            DialogToolbar.OpenPauseFromToolbar(hud);
            await Frames(2);
            Check(pause.IsOpen, "(d) MENU click path (DialogToolbar.OpenPauseFromToolbar -> PauseMenu.Open) opens the pause menu");
            await Tap(Key.M);
            await Seconds(0.2);
            GetTree().Paused = false;
            Check(!pause.IsOpen, "(d) pause menu closed again after the click path");

            // (g) ボタン列の上ではクリックが送りに数えられない（Pad.CaptureMouse の門だけを見る）
            Pad.CaptureMouse();
            Check(Pad.MouseCaptured, "(g) mouse captured while hovering the toolbar");
            await Frames(3);
            Check(!Pad.MouseCaptured, "(g) capture lapses once the hover stops");
            var r0 = hud.ToolbarRect(0); var r3 = hud.ToolbarRect(3);
            Check(r0.End.X < r3.Position.X && Mathf.IsEqualApprox(r0.Position.Y, r3.Position.Y), "(g) AUTO..MENU laid out left to right on one row");
            Check(r3.End.X <= Hud.DlgBoxX + Hud.DlgBoxW && r3.Position.Y < 520f && r3.End.Y > 520f, "(g) row sits right-aligned and bites into the box's top edge");
            GD.Print($"[Toolbar] rects AUTO={r0} MENU={r3}");

            if (shot) await Shots(game, hud, world, readField, stamp);

            hud.HoldBubble = false;
            hud.HideBubble();
            await Frames(3);
            root.QueueFree();
            await Frames(5);

            // (h) Hud を通らない画面
            game.MsgCharsPerSec = 300;
            await PrologueChecks(game, shot);
            await EpilogueChecks(game);
            await HubChecks(game, shot, stamp);
            GD.Print("[Toolbar] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[Toolbar] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task Shots(GameManager game, Hud hud, Node2D world, FieldInfo readField, string stamp)
    {
        // 1) 戦闘会話（AUTO 点灯）
        game.AutoAdvanceDialog = true;
        hud.ShowDialog(Hud.LineKind.Mina, "……この投稿、下書きのほうが本音だね。消されたほうの言葉、ちゃんと読んだよ。");
        await Seconds(1.6);
        await Save("toolbar_battle");
        // 2) ナレーション（SKIP 点灯）
        game.AutoAdvanceDialog = false;
        hud.ShowMessage("タイムラインの底で、誰にも届かなかった下書きが、まだ光っている。");
        readField.SetValue(hud, true);
        await Seconds(0.45);
        await Tap(Key.S);
        await Seconds(0.6);
        await Save("toolbar_narration");
        hud.HideBubble();
        await Seconds(0.4);
        // 3) カットシーン（StoryFilm＝CinematicMode の会話）
        hud.HoldBubble = false;
        bool done = false;
        AkariStoryFilm.Play(hud, world, false, () => done = true);
        for (int i = 0; i < 600 && !hud.DialogToolbarVisible; i++) await Frames(1);
        await Seconds(2.0);
        await Save("toolbar_cinematic");
        GD.Print($"[Toolbar] shots saved to {_out} (film done={done})");
    }

    // (h-1) プロローグ：会話フェーズ（_phase 3）でボタン列が出る。A＝AUTO で送られる、S＝既読で早送り→選択肢で OFF。
    private async Task PrologueChecks(GameManager game, bool shot)
    {
        game.AutoAdvanceDialog = false;
        var pro = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
        GetTree().Root.AddChild(pro);
        GetTree().CurrentScene = pro;
        await Frames(5);
        for (int i = 0; i < 3 && Read<int>(pro, "_phase") < 3; i++) await Tap(Key.Z);
        await Until(() => Shown(pro, "TalkBoxShown"), 15.0);
        Check(Shown(pro, "TalkBoxShown"), "(h) prologue: talk box is shown (phase 3)");
        var anchor = Anchor(typeof(Prologue));
        var m0 = DialogToolbar.ButtonRect(anchor, DialogToolbar.Auto);
        var m3 = DialogToolbar.ButtonRect(anchor, DialogToolbar.Menu);
        GD.Print($"[Toolbar] prologue anchor={anchor} AUTO={m0} MENU={m3}");
        Check(Mathf.IsEqualApprox(anchor.X, 370f / UiKit.Scale) && Mathf.IsEqualApprox(anchor.Y, 158f / UiKit.Scale),
            "(h) prologue: anchor is the CutBox top-right (370,158) in design coords");
        Check(m3.End.X <= anchor.X && m3.Position.Y < anchor.Y && m3.End.Y > anchor.Y && m0.End.X < m3.Position.X,
            "(h) prologue: row is right-aligned on the box's top edge");
        await Seconds(0.4);

        // SKIP：未読行では即 OFF（台本の1行目＝起動ログ。その次が P2 の3択）
        Write(pro, "_lineWasRead", false);
        await Tap(Key.S);
        Check(!Hud.SkipLatched, "(h) prologue: S on an unread line drops the latch at once");
        // 既読行では ON のまま早送りし、選択肢（P2）が出たところで切れる
        foreach (var d in Read<System.Collections.IList>(pro, "_talk"))
            game.MarkLineRead((string)d!.GetType().GetField("Text")!.GetValue(d)!);
        Write(pro, "_lineWasRead", true);
        Write(pro, "_lineT", -30.0);   // 送りの最短間隔で止めておく＝まずラッチの状態だけを見る
        int line1 = Read<int>(pro, "_line");
        await Tap(Key.S);
        Check(Hud.SkipLatched, "(h) prologue: S on a read line keeps the latch ON");
        Write(pro, "_lineT", 1.0);     // 早送りを通す
        await Until(() => pro.GetNodeOrNull("ChoiceOverlay") != null, 10.0);
        Check(Read<int>(pro, "_line") > line1, $"(h) prologue: SKIP fast-forwards the read line ({line1} -> {Read<int>(pro, "_line")})");
        Check(pro.GetNodeOrNull("ChoiceOverlay") != null, "(h) prologue: fast-forward stops at the P2 choice");
        await Frames(3);
        Check(!Hud.SkipLatched, "(h) prologue: the choice turns the latch off");
        Check(!Shown(pro, "TalkBoxShown"), "(h) prologue: no toolbar while the choice is up");

        // 3択を決めて P2 の受け（ミナの数行）へ
        await Seconds(1.0);
        await Tap(Key.Z);
        await Until(() => Shown(pro, "TalkBoxShown"), 5.0);
        Check(Shown(pro, "TalkBoxShown"), "(h) prologue: talk box is back after the choice");
        await Seconds(0.4);

        // AUTO：全文表示後 1.0 秒で次の行へ
        int line0 = Read<int>(pro, "_line");
        await Tap(Key.A);
        Check(game.AutoAdvanceDialog, "(h) prologue: A turns AUTO on");
        await Until(() => Read<int>(pro, "_line") != line0, 5.0);
        Check(Read<int>(pro, "_line") > line0, $"(h) prologue: AUTO advances the line by itself ({line0} -> {Read<int>(pro, "_line")})");
        if (shot) { await Seconds(0.5); await Save("toolbar_prologue"); }
        await Until(() => Shown(pro, "TalkBoxShown"), 5.0);
        await Seconds(0.35);
        await Tap(Key.A);
        Check(!game.AutoAdvanceDialog, "(h) prologue: A again turns AUTO off");
        pro.QueueFree();
        await Frames(5);
    }

    // (h-2) エピローグ：見上げ（PhGaze）の語りでボタン列が出る。A＝AUTO、S＝既読で ON／未読で OFF。
    private async Task EpilogueChecks(GameManager game)
    {
        game.AutoAdvanceDialog = false;
        var ep = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
        GetTree().Root.AddChild(ep);
        GetTree().CurrentScene = ep;
        await Until(() => Shown(ep, "TalkBoxShown"), 5.0);
        Check(Shown(ep, "TalkBoxShown"), "(h) epilogue: talk box is shown (E5b gaze)");
        await Seconds(0.4);
        int line0 = Read<int>(ep, "_line");
        await Tap(Key.A);
        Check(game.AutoAdvanceDialog, "(h) epilogue: A turns AUTO on");
        await Until(() => Read<int>(ep, "_line") != line0, 5.0);
        Check(Read<int>(ep, "_line") > line0, $"(h) epilogue: AUTO advances the line by itself ({line0} -> {Read<int>(ep, "_line")})");
        await Tap(Key.A);
        Check(!game.AutoAdvanceDialog, "(h) epilogue: A again turns AUTO off");
        await Seconds(0.4);
        Write(ep, "_lineWasRead", false);
        await Tap(Key.S);
        Check(!Hud.SkipLatched, "(h) epilogue: S on an unread line drops the latch at once");
        Write(ep, "_lineWasRead", true);
        Write(ep, "_lineT", -30.0);   // 送りの最短間隔で止めておく＝ラッチの状態だけを見る
        await Tap(Key.S);
        Check(Hud.SkipLatched, "(h) epilogue: S on a read line keeps the latch ON");
        Write(ep, "_lineWasRead", false);
        await Frames(3);
        Check(!Hud.SkipLatched, "(h) epilogue: reaching an unread line turns the latch off");
        ep.QueueFree();
        await Frames(5);
    }

    // (h-3) ハブの返信会話：ボタン列が出る。A＝AUTO、S＝既読で ON→次の未読行で OFF→会話が閉じても OFF。
    private async Task HubChecks(GameManager game, bool shot, string stamp)
    {
        game.AutoAdvanceDialog = false;
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        await Frames(40);
        var modeType = typeof(Hub).GetNestedType("Mode", BindingFlags.NonPublic)!;
        var lines = new (string, string)[]
        {
            ("ミナ", $"ハブのツールバー検証、一行目です。{stamp}"),
            ("ミナ", $"ハブのツールバー検証、二行目です。{stamp}"),
            ("ミナ", $"ハブのツールバー検証、三行目です。{stamp}"),
            ("ミナ", $"ハブのツールバー検証、四行目です。{stamp}"),
        };
        typeof(Hub).GetMethod("StartDialogue", Private)!.Invoke(hub,
            new object?[] { lines, null, true, Enum.Parse(modeType, "Cards"), null });
        await Frames(3);
        Check(Shown(hub, "DialogShown"), "(h) hub: dialogue box is shown");
        var anchor = Anchor(typeof(Hub));
        var m3 = DialogToolbar.ButtonRect(anchor, DialogToolbar.Menu);
        GD.Print($"[Toolbar] hub anchor={anchor} AUTO={DialogToolbar.ButtonRect(anchor, DialogToolbar.Auto)} MENU={m3}");
        Check(anchor == new Vector2(880f, 428f) && m3.End.X <= 880f && m3.Position.Y < 428f && m3.End.Y > 428f,
            "(h) hub: row is right-aligned on the dialogue box's top edge (880,428)");
        await Seconds(0.4);
        int idx0 = Read<int>(hub, "_dlgIdx");
        await Tap(Key.A);
        Check(game.AutoAdvanceDialog, "(h) hub: A turns AUTO on");
        await Until(() => Read<int>(hub, "_dlgIdx") != idx0, 5.0);
        Check(Read<int>(hub, "_dlgIdx") > idx0, $"(h) hub: AUTO advances the line by itself ({idx0} -> {Read<int>(hub, "_dlgIdx")})");
        if (shot) { await Seconds(0.5); await Save("toolbar_hub"); }
        await Tap(Key.A);
        Check(!game.AutoAdvanceDialog, "(h) hub: A again turns AUTO off");
        await Seconds(0.2);
        Write(hub, "_dlgReadBefore", false);
        await Tap(Key.S);
        Check(!Hud.SkipLatched, "(h) hub: S on an unread line drops the latch at once");
        int idx1 = Read<int>(hub, "_dlgIdx");
        Write(hub, "_dlgReadBefore", true);
        Write(hub, "_dlgLineT", -30.0);   // 早送りの一歩（0.15s）を止めておく＝ラッチの状態だけを見る
        await Tap(Key.S);
        Check(Hud.SkipLatched, "(h) hub: S on a read line keeps the latch ON");
        Write(hub, "_dlgLineT", 1.0);    // 早送りを通す＝次の（未読の）行へ
        for (int i = 0; i < 60 && Read<int>(hub, "_dlgIdx") == idx1; i++) await Frames(1);
        await Frames(3);
        Check(Read<int>(hub, "_dlgIdx") > idx1, "(h) hub: SKIP fast-forwards the read line");
        Check(!Hud.SkipLatched, "(h) hub: the next unread line turns the latch off");
        // 会話を閉じてもラッチは残らない（場が消えて LatchGoneGrace 後に切れる）
        for (int i = 0; i < 60 && Shown(hub, "DialogShown"); i++) await Tap(Key.Z);
        Check(!Shown(hub, "DialogShown"), "(h) hub: dialogue closed");
        Hud.SkipLatched = true;   // 会話の外で立っていたら（場が消えている）
        await Seconds(0.35);
        Check(!Hud.SkipLatched, "(h) hub: closing the dialogue turns the latch off");
        hub.QueueFree();
        await Frames(5);
    }

    private async Task Save(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        string path = $"{_out}/{name}.png";
        image.SavePng(path);
        GD.Print($"[Toolbar] SHOT {path}");
    }

    private static bool? ReadSettingsAuto()
    {
        const string path = "user://settings.json";
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var json = new Json();
        if (f == null || json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary) return null;
        var d = json.Data.AsGodotDictionary();
        return d.ContainsKey("auto") ? d["auto"].AsBool() : null;
    }

    private async Task Tap(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        await Frames(3);
    }

    // 条件が立つまで待つ（実時間で上限。ヘッドレスはフレームが速く回るので、フレーム数では待たない）。
    private async Task Until(Func<bool> done, double seconds)
    {
        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (!done() && Time.GetTicksMsec() < end) await Frames(1);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
}
