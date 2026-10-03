using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// DialogToolbarQa : 会話ボックス上辺のボタン列（AUTO / SKIP / LOG / MENU・src/DialogToolbar.cs）の回帰確認。
//   Akari シーンを立ち上げ、戦闘を止めたまま hud.HoldBubble=true で会話ボックスを出して、キー割り当てを検証する。
//     (e) ボックス非表示のとき A／S は何もしない
//     (a) A で GameManager.AutoAdvanceDialog が反転（user://settings.json の "auto" にも保存）
//     (b) S で SkipLatched：未読行では即 OFF／既読行では ON のまま FastForwarding が真／次の未読行（iii）・
//         選択肢（iv）で自動 OFF
//     (i) ラッチ ON のままボックスを閉じて 2 秒待っても ON（2026-09-27：画面が替わっても保つ）。
//         その間は盤面の右上に「▶▶」の印（v）＝SkipLatchMarkVisible と描いた矩形（Field.DRight-16, 14 が右上）
//     (ii) ラッチ ON のまま別シーン（Akari→Rei。Hud も作り直し）へ移っても ON で、既読の会話を出すと
//         入力なしで FastForwarding が真・印は消える
//     (c) L で Backlog.IsOpen ／ (d) M で PauseMenu.IsOpen、MENU クリック経路（PauseMenu.Open の Call）も開く
//     (g) ボタン列にマウスが乗っている間は左クリックが会話送り（Pad.AdvanceHeld）に数えられない
//     (vi) ボタン矩形：36×30・間隔 4・ボックス上辺に 3 食い込み・右端から 12 内側。
//     (h) Hud を通らない画面（Prologue／Epilogue／Hub の会話）にもボタン列が出て、枠の上辺右寄せに並び、
//         A で AUTO（全文表示後に自動で送る）、S で SKIP（未読行で即 OFF／既読行で早送り／選択肢・未読行で OFF。
//         ハブは会話を閉じても ON のまま右上に印）
//   起動: --headless --path . res://tools/qa_dialog_toolbar.tscn
//         窓あり＋撮影: res://tools/qa_dialog_toolbar.tscn -- --dt-shot [--dt-out <dir>]
//           戦闘会話（AUTO 点灯）・ボタン列の3倍切り抜き（toolbar_zoom／ホバーの吹き出し付き toolbar_zoom_hover）・
//           ナレーション（SKIP 点灯）・カットシーン（StoryFilm 回想）・ボックス無し＋ラッチ ON の右上の印（toolbar_latch_mark）・
//           プロローグ（AUTO 点灯）・ハブの返信会話（AUTO 点灯）を保存する。
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
            foreach (string icon in new[] { "auto", "skip", "log", "menu", "auto_on", "skip_on" })
            {
                var texture = GD.Load<Texture2D>($"res://char/ui/dialog_{icon}_v1.png");
                using var artwork = texture.GetImage();
                Check(texture.GetWidth() == 256 && artwork.HasMipmaps(), $"{icon}: compact import with smooth minification");
                Check(artwork.GetPixel(0, 0).A == 0 && artwork.GetUsedRect().HasArea(), $"{icon}: nonempty transparent artwork");
                if (icon == "menu") Check(artwork.GetPixel(128, 128).A < 0.05f, "gear center remains transparent");
            }
            if (shot)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                DirAccess.MakeDirRecursiveAbsolute(_out);
            }
            var game = GetNode<GameManager>("/root/Game");
            foreach (var box in new[] { DialogueBox.FullScreen, DialogueBox.Board,
                new Rect2(430, DialogueBox.FullScreen.Position.Y, 450, DialogueBox.FullScreen.Size.Y) })
            {
                float textBottom = DialogueBox.TextPosition(box).Y + DialogueBox.Body.Font.GetHeight(DialogueBox.Body.Size) * 2
                    + DialogueBox.Body.ExtraLeading;
                Check(textBottom < box.End.Y - 32, $"two full lines end at {textBottom:0.0}, above the continue button at {box.End.Y - 32}");
                var pages = UiKit.Paginate(DialogueBox.Body, "……ご主人様。誰にも届かなかった言葉が、まだここに残っています。ひとつずつ、一緒に読んでいきましょう。",
                    DialogueBox.WrapWidth(box), Hud.DlgMaxLines);
                foreach (string page in pages)
                    Check(page.Split('\n').Length <= 2 && UiKit.WrapLines(DialogueBox.Body.Font, page, DialogueBox.Body.Size,
                        DialogueBox.WrapWidth(box)).Count <= 2, "pagination matches the shared panel's rendered width");
            }
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

            // (i) ボックスを閉じてもラッチは残る ／ (v) その間は盤面の右上に印
            hud.HideBubble();
            await Seconds(2.0);
            Check(Hud.SkipLatched, "(i) latch stays ON 2s after the box closed");
            Check(hud.SkipLatchMarkVisible, "(v) latch mark is shown while no box is up");
            var markExpect = DialogToolbar.LatchMarkRect(new Vector2(Field.DRight - 16f, 14f));
            Check(hud.SkipLatchMarkDrawnRect == markExpect && markExpect.Size.Y == 18f,
                $"(v) latch mark drawn at the board's top-right {hud.SkipLatchMarkDrawnRect}");

            // (iii) 未読行に当たると切れる（ここでは (c) 用の三行目＝未読）
            hud.ShowDialog(Hud.LineKind.Mina, $"ツールバー検証の三行目です。{stamp}");
            await Frames(3);
            Check(!Hud.SkipLatched, "(iii) an unread line turns the latch off");
            Check(!hud.SkipLatchMarkVisible, "(v) no latch mark while the box is up / latch is off");

            // (c) LOG ＝ L
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
            Check(hud.DialogRect.Encloses(r0) && hud.DialogRect.Encloses(r3), "(g) all controls stay inside the dialogue panel");
            GD.Print($"[Toolbar] rects AUTO={r0} MENU={r3}");
            float ax = hud.DialogRect.End.X;
            Check(r3.End.Y < DialogueBox.TextPosition(hud.DialogRect).Y, "(vi) toolbar never overlaps the dialogue body");
            Check(r3.End.X == ax - DialogueBox.Padding, "(vi) controls share the dialogue's right padding");
            Check(hud.ToolbarRect(1).Position.X - hud.ToolbarRect(0).End.X == 4f, "(vi) 4px gap between buttons");
            Check(r3.End.X - r0.Position.X == 156f, "(vi) toolbar occupies only 156px before its thin rim");
            var boundsField = typeof(DialogToolbar).GetField("IconRegions", PrivateStatic)!;
            var regions = (Rect2[])boundsField.GetValue(null)!;
            for (int i = 0; i < regions.Length; i++)
            {
                Vector2 bounds = new(24, 20);
                Vector2 size = regions[i].Size * Mathf.Min(bounds.X / regions[i].Size.X, bounds.Y / regions[i].Size.Y);
                Check(size.Y >= 14 && size.Y <= 20 && size.X >= 15 && size.X <= 24,
                    $"(vi) illustration {i} stays small and legible at {size}");
            }
            var boardChoices = ChoiceOverlay.Show(root, ChoiceEffects.SkyChoices, 3, onBoard: true);
            await Frames(3);
            var rows = Read<Rect2[]>(boardChoices, "_rows");
            Check(rows[^1].End.Y + 8 <= r0.Position.Y - 4, "(vi) toolbar dock stays below all four choices");
            boardChoices.QueueFree();
            await Frames(3);

            if (shot) await Shots(game, hud, world, readField, stamp);

            hud.HoldBubble = false;
            hud.HideBubble();
            await Frames(3);

            // (ii) ラッチ ON のまま別シーン（Akari→Rei）へ。Hud は作り直しになるが static のラッチは残る
            Hud.SkipLatched = true;
            root.QueueFree();
            await Frames(5);
            Check(Hud.SkipLatched, "(ii) latch survives the old scene (and its Hud) leaving the tree");
            var rei = GD.Load<PackedScene>("res://Rei.tscn").Instantiate<Node2D>();
            await Frames(1);
            GetTree().Root.AddChild(rei);
            GetTree().CurrentScene = rei;
            var hud2 = rei.GetNode<Hud>("Hud");
            rei.SetProcess(false);
            rei.GetNode<Node>("StageRei").SetProcess(false);
            rei.GetNode<Node2D>("World").GetNode<Player>("Player").SetPhysicsProcess(false);
            hud2.HideBubble();   // 面の入りの行（未読なら正しくラッチを切る）を検証から外す
            await Frames(5);
            Check(hud2 != hud && Hud.SkipLatched, "(ii) latch is still ON in the next scene's new Hud");
            Check(hud2.SkipLatchMarkVisible, "(ii) new Hud shows the latch mark while no box is up");
            if (shot) { await Seconds(0.8); await Save("toolbar_latch_mark"); }
            string readLine = $"シーンを跨いだ既読の一行です。{stamp}";
            game.MarkLineRead(readLine);
            hud2.HoldBubble = true;
            hud2.ShowDialog(Hud.LineKind.Mina, readLine);
            await Frames(3);
            Check(Hud.SkipLatched && hud2.FastForwarding, "(ii) a read line in the new scene fast-forwards with no input");
            Check(!hud2.SkipLatchMarkVisible, "(ii) latch mark hides once the box is up");
            hud2.ShowDialog(Hud.LineKind.Mina, $"シーンを跨いだ未読の一行です。{stamp}");
            await Frames(3);
            Check(!Hud.SkipLatched, "(iii) the first unread line in the new scene turns the latch off");
            hud2.HoldBubble = false;
            hud2.HideBubble();
            await Frames(3);
            rei.QueueFree();
            await Frames(5);

            // (h) Hud を通らない画面
            game.MsgCharsPerSec = 300;
            await PrologueChecks(game, shot);
            await EpilogueChecks(game);
            await HubChecks(game, shot, stamp);
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Frames(10);
            GC.Collect();
            GC.WaitForPendingFinalizers();
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
        await SelectedStateShots(game, hud, readField);
        // 1) 戦闘会話（AUTO 点灯）
        game.AutoAdvanceDialog = true;
        hud.ShowDialog(Hud.LineKind.Mina, "……この投稿、下書きのほうが本音だね。消されたほうの言葉、ちゃんと読んだよ。");
        await Seconds(1.6);
        await Save("toolbar_battle");
        foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Frames(5);
            await Save($"toolbar_battle_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(5);
        // 1b) ボタン列の3倍切り抜き（AUTO・SKIP 点灯＝ON と OFF を1枚で見比べる）／マウスを乗せて吹き出し付き
        readField.SetValue(hud, true);
        await Tap(Key.S);
        await Seconds(0.4);
        await SaveZoom("toolbar_zoom", hud.ToolbarRect(DialogToolbar.Auto), hud.ToolbarRect(DialogToolbar.Menu));
        GetViewport().WarpMouse(hud.ToolbarRect(DialogToolbar.Log).GetCenter() * UiKit.Scale);
        await Seconds(0.3);
        GD.Print($"[Toolbar] hover after warp = {hud.DialogToolbarHover}");
        await SaveZoom("toolbar_zoom_hover", hud.ToolbarRect(DialogToolbar.Auto), hud.ToolbarRect(DialogToolbar.Menu));
        GetViewport().WarpMouse(new Vector2(200f, 100f));
        await Tap(Key.S);   // ラッチを戻す（残すと下のナレ・回想まで早送りされる）
        // 2) ナレーション（SKIP 点灯）
        game.AutoAdvanceDialog = false;
        hud.ShowMessage("タイムラインの底で、誰にも届かなかった下書きが、まだ光っている。");
        readField.SetValue(hud, true);
        await Seconds(0.45);
        await Tap(Key.S);
        await Seconds(0.6);
        await Save("toolbar_narration");
        await Tap(Key.S);   // ラッチを戻す（画面を跨いで残るので、回想フィルムを早送りさせない）
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

    private async Task SelectedStateShots(GameManager game, Hud hud, FieldInfo readField)
    {
        game.AutoAdvanceDialog = false;
        Hud.SkipLatched = false;
        hud.ShowDialog(Hud.LineKind.Mina, "……ご主人様。聞こえています。");
        readField.SetValue(hud, true);
        GetViewport().WarpMouse(new Vector2(200, 100));
        await Seconds(0.45);
        await SaveZoom("toolbar_off", hud.ToolbarRect(0), hud.ToolbarRect(3));
        using var off = GetViewport().GetTexture().GetImage();
        foreach (int button in new[] { DialogToolbar.Auto, DialogToolbar.Skip })
        {
            var rect = hud.ToolbarRect(button);
            await Click(hud, rect.GetCenter());
            Check(button == DialogToolbar.Auto ? game.AutoAdvanceDialog : Hud.SkipLatched,
                $"button {button}: compact hitbox toggles on with a mouse click");
            GetViewport().WarpMouse(new Vector2(200, 100));
            await Frames(3);
            await SaveZoom(button == DialogToolbar.Auto ? "toolbar_auto_on" : "toolbar_skip_on", hud.ToolbarRect(0), hud.ToolbarRect(3));
            using var on = GetViewport().GetTexture().GetImage();
            float scale = off.GetWidth() / UiKit.DesignW;
            Rect2I icon = new((int)((rect.Position.X + 6) * scale), (int)((rect.Position.Y + 4) * scale),
                (int)(24 * scale), (int)(20 * scale));
            int changed = 0;
            for (int y = icon.Position.Y; y < icon.End.Y; y++)
                for (int x = icon.Position.X; x < icon.End.X; x++)
                {
                    Color color = on.GetPixel(x, y), before = off.GetPixel(x, y);
                    bool selectedColor = button == DialogToolbar.Auto
                        ? color.G > color.R + 0.18f && color.B > color.R + 0.1f
                        : color.R > color.B + 0.25f && color.G > color.B + 0.12f;
                    if (selectedColor && Math.Abs(color.R - before.R) + Math.Abs(color.G - before.G)
                        + Math.Abs(color.B - before.B) > 0.3f) changed++;
                }
            Check(changed >= 18, $"button {button}: dedicated selected illustration visibly changes color ({changed} pixels)");
            await Click(hud, rect.GetCenter());
            Check(button == DialogToolbar.Auto ? !game.AutoAdvanceDialog : !Hud.SkipLatched,
                $"button {button}: second click restores the inactive state");
        }
        GetViewport().WarpMouse(new Vector2(200, 100));
    }

    private async Task Click(Hud hud, Vector2 designPosition)
    {
        const BindingFlags mouseFields = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(Pad).GetField("_mousePos", mouseFields)!.SetValue(null, designPosition);
        typeof(Pad).GetField("_mL", mouseFields)!.SetValue(null, true);
        typeof(Pad).GetField("_mLPrev", mouseFields)!.SetValue(null, false);
        typeof(Hud).GetMethod("TickDialogToolbar", Private)!.Invoke(hud, new object[] { 1.0 / 60 });
        Check(Pad.MouseCaptured && !Pad.AdvanceHeld(), "toolbar click does not advance the dialogue");
        typeof(Pad).GetField("_mL", mouseFields)!.SetValue(null, false);
        await Frames(3);
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
        Check(anchor == DialogueBox.Anchor(DialogueBox.FullScreen), "(h) prologue uses the shared cinematic panel");
        Check(DialogueBox.FullScreen.Encloses(m3) && DialogueBox.FullScreen.Encloses(m0),
            "(h) prologue controls stay inside the panel");
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
            new object?[] { lines, null, true, Enum.Parse(modeType, "Cards"), null, null });
        await Frames(3);
        Check(Shown(hub, "DialogShown"), "(h) hub: dialogue box is shown");
        var anchor = Anchor(typeof(Hub));
        var m3 = DialogToolbar.ButtonRect(anchor, DialogToolbar.Menu);
        GD.Print($"[Toolbar] hub anchor={anchor} AUTO={DialogToolbar.ButtonRect(anchor, DialogToolbar.Auto)} MENU={m3}");
        Check(anchor == new Vector2(880f, DialogueBox.FullScreen.Position.Y) && m3.End.X <= 880f
            && m3.Position.Y > anchor.Y && m3.End.Y < anchor.Y + DialogueBox.HeaderHeight,
            "(h) hub controls stay inside the same header layout");
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
        // 会話を閉じてもラッチは残り（画面を跨いで保つ）、会話の外では画面右上に印を出す
        for (int i = 0; i < 60 && Shown(hub, "DialogShown"); i++) await Tap(Key.Z);
        Check(!Shown(hub, "DialogShown"), "(h) hub: dialogue closed");
        Hud.SkipLatched = true;   // 会話の外で立っていたら
        await Seconds(0.5);
        var tb = Read<DialogToolbar>(hub, "_toolbar");
        Check(Hud.SkipLatched, "(h) hub: the latch stays ON outside the dialogue");
        Check(tb.LatchMarkVisible && tb.LatchMarkDrawnRect == DialogToolbar.LatchMarkRect(new Vector2(UiKit.DesignW - 16f, 14f)),
            $"(h) hub: latch mark drawn at the screen's top-right {tb.LatchMarkDrawnRect}");
        Hud.SkipLatched = false;
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

    // ボタン列（first..last の矩形）の周り（上は吹き出しの分まで）を切り抜いて3倍に拡大して保存する。
    //   設計座標→画像ピクセルは窓の実寸で換算（1280 幅なら等倍）。
    private async Task SaveZoom(string name, Rect2 first, Rect2 last)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        float k = image.GetWidth() / UiKit.DesignW;
        var design = new Rect2(first.Position.X - 12f, first.Position.Y - 34f, last.End.X - first.Position.X + 24f, first.Size.Y + 46f);
        var px = new Rect2I((int)(design.Position.X * k), (int)(design.Position.Y * k), (int)(design.Size.X * k), (int)(design.Size.Y * k));
        var crop = image.GetRegion(px);
        crop.Resize(crop.GetWidth() * 3, crop.GetHeight() * 3, Image.Interpolation.Nearest);
        string path = $"{_out}/{name}.png";
        crop.SavePng(path);
        GD.Print($"[Toolbar] SHOT {path} ({px} x3)");
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
