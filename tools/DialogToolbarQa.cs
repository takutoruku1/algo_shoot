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
//   起動: --headless --path . res://tools/qa_dialog_toolbar.tscn
//         窓あり＋撮影: res://tools/qa_dialog_toolbar.tscn -- --dt-shot [--dt-out <dir>]
//           戦闘会話（AUTO 点灯）・ナレーション（SKIP 点灯）・カットシーン（StoryFilm 回想）の3枚を保存する。
//   APPDATA は build/qa_story/ 配下へ隔離して走らせる（settings.json／既読を本番に書かない）。
public partial class DialogToolbarQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";

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
            typeof(Hud).GetMethod("OpenPauseFromToolbar", Private)!.Invoke(hud, null);
            await Frames(2);
            Check(pause.IsOpen, "(d) MENU click path (PauseMenu.Open via Call) opens the pause menu");
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

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
}
