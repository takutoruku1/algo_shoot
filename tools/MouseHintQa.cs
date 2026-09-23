using Godot;
using System.Reflection;
using System.Threading.Tasks;

// MouseHintQa : 2026-09-17 のマウス対応拡充（フッタ「もどる／とじる」・デバイスタブ・右下 Esc ヒント）の自動検証。
//   実マウスを動かさず Pad の内部状態へ設計座標の押下を差し込み、各画面の _Process を1フレーム回して
//   期待どおりに反応する／しないことを見る（PauseMenuQa.ClickAt と同じ流儀）。
//   実行: Godot --headless --path . res://tools/qa_mouse_hint.tscn -- --qa-mouse
public partial class MouseHintQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic;

    private string? _shotDir;   // --mouse-shot-out <dir> でスクショモード（検証の代わりにホバー絵を撮る）

    private int _fails;
    private void Check(bool ok, string message)
    {
        if (ok) GD.Print($"[MouseQA] PASS {message}");
        else { _fails++; GD.PrintErr($"[MouseQA] FAIL {message}"); }
    }

    private static void PadField(string name, object value)
        => typeof(Pad).GetField(name, Stat)!.SetValue(null, value);

    private static Rect2 StaticRect(System.Type t, string name, params object[] args)
        => (Rect2)t.GetMethod(name, Stat | BindingFlags.Public)!.Invoke(null, args)!;

    // 設計座標 p にカーソルを置いた状態を作る（click=true なら左ボタン押下エッジも立てる）。
    //   ※PauseMenu._Process が毎フレーム Pad.PollMouse で実マウスへ上書きするため、この状態を
    //     _Process 経由で読ませることはできない。仕込んだ直後に対象の _Process を直に呼ぶ
    //     （PauseMenuQa.ClickAt と同じ理由・同じ流儀）。
    private static void SetMouse(Vector2 p, bool click)
    {
        PadField("_mousePos", p);
        PadField("_usingMouse", true);
        PadField("_mL", click);
        PadField("_mLPrev", false);
    }
    private static void ClearMouse()
    {
        PadField("_mL", false); PadField("_mLPrev", false); PadField("_usingMouse", false);
    }

    // マウス状態を仕込んで、対象ノードの _Process を1回だけ直に回す。
    private static void Tick(Node target, Vector2 p, bool click)
    {
        SetMouse(p, click);
        target.GetType().GetMethod("_Process", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(target, new object[] { 0.016 });
        ClearMouse();
    }

    public override async void _Ready()
    {
        var user = OS.GetCmdlineUserArgs();
        for (int i = 0; i < user.Length; i++)
            if (user[i] == "--mouse-shot-out" && i + 1 < user.Length) _shotDir = user[i + 1];

        try
        {
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            await Frames(2);

            if (_shotDir != null) { await ShotRun(); return; }

            TestCursor();
            await TestHowTo();
            await TestBacklog();
            await TestPauseHint();
            await TestFooterBack();
            TestFooterGeometry();
        }
        catch (System.Exception e)
        {
            _fails++;
            GD.PrintErr($"[MouseQA] EXCEPTION {e}");
        }
        GD.Print(_fails == 0 ? "[MouseQA] ALL PASS" : $"[MouseQA] {_fails} FAILURE(S)");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    private void TestCursor()
    {
        string path = ProjectSettings.GetSetting("display/mouse_cursor/custom_image").AsString();
        Vector2 hotspot = ProjectSettings.GetSetting("display/mouse_cursor/custom_image_hotspot").AsVector2();
        Check(path == "res://char/ui/cursor_refrain_v1.png", "the illustrated cursor is configured for every scene");
        using var texture = GD.Load<Texture2D>(path);
        Check(texture != null, "the cursor texture imports successfully");
        if (texture == null) return;
        using var image = texture.GetImage();
        Check(image.GetWidth() == 34 && image.GetHeight() == 40, "cursor stays compact at native window resolution");
        Check(hotspot == Vector2.Zero && image.GetPixel(0, 0).A > 0.1f,
            "click hotspot matches the upper-left arrow tip");
        int clear = 0, bright = 0, dark = 0;
        for (int y = 0; y < image.GetHeight(); y++)
        for (int x = 0; x < image.GetWidth(); x++)
        {
            Color pixel = image.GetPixel(x, y);
            if (pixel.A == 0f) clear++;
            if (pixel.A > 0.8f && pixel.R > 0.7f && pixel.G > 0.7f && pixel.B > 0.7f) bright++;
            float onWhite = Mathf.Max(pixel.R, Mathf.Max(pixel.G, pixel.B)) * pixel.A + 1f - pixel.A;
            if (onWhite < 0.65f) dark++;
        }
        Check(clear > image.GetWidth() * image.GetHeight() / 3, "cursor background is transparent, not a painted square");
        Check(bright > 80 && dark > 40, $"crystal remains visible on dark backgrounds ({bright}px) and light backgrounds ({dark}px)");
        Check(Input.MouseMode == Input.MouseModeEnum.Visible, "native cursor remains visible without a software overlay");
    }

    // ── あそびかた：デバイスタブ／ページドット／「とじる」 ──
    private async Task TestHowTo()
    {
        var how = GetNode<HowToPlay>("/root/HowTo");
        how.GetType().GetField("_autoplay", Private)!.SetValue(how, false);

        how.Open();
        await Frames(2);
        Check(how.IsOpen, "HowTo opens");

        // タブ2（マウス）を直接クリック → ページが 2 になる。
        int before = how.Page;
        Tick(how, HowToPlay.TabRect(2).GetCenter(), true);
        Check(how.Page == 2, $"clicking the mouse tab selects page 2 (was {before}, now {how.Page})");

        // タブ0（キーボード）を直接クリック。
        Tick(how, HowToPlay.TabRect(0).GetCenter(), true);
        Check(how.Page == 0, $"clicking the keyboard tab selects page 0 (now {how.Page})");

        // ページドット（最終ページ＝コア機能）へ直接ジャンプ。
        Tick(how, HowToPlay.DotRect(HowToPlay.PageCount - 1).GetCenter(), true);
        Check(how.Page == HowToPlay.PageCount - 1, $"clicking the last page dot jumps there (now {how.Page})");

        // 空白（パネル中央付近）をクリックしても何も起きない＝誤爆しない。
        int held = how.Page;
        Tick(how, new Vector2(640, 400), true);
        Check(how.IsOpen && how.Page == held, "clicking empty panel space does nothing");

        // ホバーだけ（押していない）ではページが動かない＝誤爆しない。
        Tick(how, HowToPlay.TabRect(0).GetCenter(), false);
        Check(how.Page == held, "hovering a tab without clicking does not change the page");

        // フッタの「とじる」でオーバーレイが閉じる。
        Tick(how, HowToPlay.CloseHintRect().GetCenter(), true);
        Check(!how.IsOpen, "clicking the footer とじる closes HowTo");
        await Frames(2);
    }

    // ── 会話ログ：「とじる」 ──
    private async Task TestBacklog()
    {
        var log = GetNode<Backlog>("/root/Backlog");
        log.GetType().GetField("_autoplay", Private)!.SetValue(log, false);
        // 1行積んでおく（空でもフッタは出るが、実プレイに近い「本文あり」の状態で確かめる）。
        typeof(Hud).GetMethod("PushBacklog", Stat)!
            .Invoke(null, new object[] { "ミナ", "テスト行", UiKit.Mina, Hud.LineKind.Mina });

        log.Open();
        await Frames(2);
        Check(log.IsOpen, "Backlog opens");

        // パネル中央（ログ本文）をクリックしても閉じない。
        Tick(log, new Vector2(640, 360), true);
        Check(log.IsOpen, "clicking the log body does not close Backlog");

        // 「とじる」の上をホバーしただけでは閉じない。
        Tick(log, Backlog.CloseHintRect().GetCenter(), false);
        Check(log.IsOpen, "hovering とじる without clicking does not close Backlog");

        Tick(log, Backlog.CloseHintRect().GetCenter(), true);
        Check(!log.IsOpen, "clicking the footer とじる closes Backlog");
        await Frames(4);
    }

    // ── 右下「Esc／メニュー」ヒント：非戦闘画面では押せて、戦闘画面では押せない ──
    private async Task TestPauseHint()
    {
        var pause = GetNode<PauseMenu>("/root/PauseMenu");
        pause.GetType().GetField("_autoplay", Private)!.SetValue(pause, false);
        Vector2 hint = PauseMenu.HintRect().GetCenter();

        // ★PauseMenu._Process は冒頭で Pad.PollMouse を呼び、仕込んだ座標を実マウスで上書きする。
        //   そこで判定の実体（_Process が唯一通る HintClicked / HintHovered）を直に確かめる。
        bool Probe(Vector2 p, bool click, System.Func<bool> read)
        { SetMouse(p, click); bool v = read(); ClearMouse(); return v; }

        // 非戦闘画面（Hub）。
        await SwapScene("res://Hub.tscn");
        Check(pause.ShowHint, "Hub shows the Esc hint");
        Check(pause.HintClickable, "Hub makes the Esc hint clickable");
        Check(Probe(hint, false, () => pause.HintHovered), "hovering the hint on Hub lights it up");
        Check(Probe(hint, true, pause.HintClicked), "clicking the Esc hint on Hub opens the pause menu");
        // ヒントのすぐ外（左へ 40px）は反応しない＝矩形がキーキャップ＋ラベル帯に収まっている。
        Check(!Probe(hint - new Vector2(80, 0), true, pause.HintClicked), "clicking just outside the hint does nothing");
        Check(!Probe(hint, false, pause.HintClicked), "hovering the hint without clicking does not open the menu");

        // 戦闘画面（Akari）＝押せない・光らない。
        await SwapScene("res://Akari.tscn");
        Check(pause.ShowHint, "a stage still shows the Esc hint");
        Check(!pause.HintClickable, "a stage does NOT make the Esc hint clickable");
        Check(!Probe(hint, false, () => pause.HintHovered), "hovering the hint in battle does not light it up");
        Check(!Probe(hint, true, pause.HintClicked), "clicking the hint spot in battle does NOT open the pause menu");

        // 会話中（Hud.BubblePaused）は非戦闘画面でも押せない。
        await SwapScene("res://Hub.tscn");
        Hud.BubblePaused = true;
        Check(!pause.HintClickable, "a dialogue bubble disables the hint click");
        Check(!Probe(hint, true, pause.HintClicked), "clicking during dialogue does NOT open the pause menu");
        Hud.BubblePaused = false;
        await Frames(2);
    }

    // ── 設定／難易度選択：フッタの「もどる」クリック ──
    private async Task TestFooterBack()
    {
        // 設定：もどる＝保存してタイトルへ。_t>0.2 の門があるので 0.3 秒ぶん回してからクリックする。
        await SwapScene("res://Settings.tscn");
        var settings = GetTree().CurrentScene;
        settings.GetType().GetField("_autoplay", Private)!.SetValue(settings, false);
        settings.GetType().GetField("_t", Private)!.SetValue(settings, 1.0);
        var sBack = StaticRect(typeof(Settings), "BackHintRect");

        // 手前の操作説明（↑↓ 項目）の上をクリックしても何も起きない＝説明はボタンではない。
        Tick(settings, new Vector2(60f, UiKit.DesignH - 56f), true);
        Check(IsInTree(settings), "clicking the Settings ↑↓ hint does not leave the screen");
        Tick(settings, sBack.GetCenter(), false);
        Check(IsInTree(settings), "hovering the Settings もどる does not leave the screen");
        Tick(settings, sBack.GetCenter(), true);
        await Frames(6);
        Check(!IsInTree(settings), "clicking the Settings もどる leaves the screen");

        // 難易度選択：もどる＝ハブへ。潜る（ティア行）と取り違えていないことも見る。
        await SwapScene("res://DiffSelect.tscn");
        var diff = GetTree().CurrentScene;
        diff.GetType().GetField("_autoplay", Private)!.SetValue(diff, false);
        diff.GetType().GetField("_t", Private)!.SetValue(diff, 1.0);
        var dBack = StaticRect(typeof(DiffSelect), "BackHintRect");
        Tick(diff, new Vector2(60f, UiKit.DesignH - 56f), true);
        Check(IsInTree(diff), "clicking the DiffSelect ↑↓ hint does not leave the screen");
        Tick(diff, dBack.GetCenter(), true);
        await Frames(6);
        Check(!IsInTree(diff), "clicking the DiffSelect もどる leaves the screen");
        await Frames(2);
    }

    private static bool IsInTree(Node n) => GodotObject.IsInstanceValid(n) && n.IsInsideTree();

    // ── フッタ矩形のジオメトリ確認（画面内に収まり、他のクリック要素と重ならないこと）──
    private void TestFooterGeometry()
    {
        var screen = new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH);
        void Inside(Rect2 r, string who)
            => Check(screen.Encloses(r) && r.Size.X > 20f && r.Size.Y > 10f, $"{who} rect is on-screen and sized ({r})");

        Inside(HowToPlay.CloseHintRect(), "HowTo とじる");
        Inside(Backlog.CloseHintRect(), "Backlog とじる");
        Inside(PauseMenu.HintRect(), "PauseMenu hint");
        for (int i = 0; i < HowToPlay.TabCount; i++) Inside(HowToPlay.TabRect(i), $"HowTo tab {i}");
        for (int i = 0; i < HowToPlay.PageCount; i++) Inside(HowToPlay.DotRect(i), $"HowTo dot {i}");

        var settingsBack = StaticRect(typeof(Settings), "BackHintRect");
        var diffBack = StaticRect(typeof(DiffSelect), "BackHintRect");
        Inside(settingsBack, "Settings もどる");
        Inside(diffBack, "DiffSelect もどる");

        // 右下ヒントが、非戦闘画面のどのクリック要素とも重ならないこと（ショップの戻る／ハブのフッタ）。
        var hint = PauseMenu.HintRect();
        Check(!hint.Intersects(new Rect2(72f, 660f, 172f, 36f)), "hint does not overlap the Shop back button");
        Check(!hint.Intersects(new Rect2(400f, 653f, 480f, 60f)), "hint does not overlap the Hub footer row");
        Check(!hint.Intersects(new Rect2(40f, UiKit.DesignH - 52f, 168f, 34f)), "hint does not overlap the Training back button");
        Check(!hint.Intersects(diffBack), "hint does not overlap the DiffSelect もどる");
        Check(!hint.Intersects(settingsBack), "hint does not overlap the Settings もどる");
    }

    // ═══════ スクショモード：各ホバー状態を PNG に落とす ═══════
    //   PauseMenu._Process が毎フレーム Pad.PollMouse で実マウス座標へ戻してしまうので、
    //   撮影中だけ PauseMenu の _Process を止め、こちらが _mousePos を握る。
    private async Task ShotRun()
    {
        DirAccess.MakeDirRecursiveAbsolute(_shotDir!);
        var pause = GetNode<PauseMenu>("/root/PauseMenu");
        pause.GetType().GetField("_autoplay", Private)!.SetValue(pause, false);
        var pauseCanvas = (Node)pause.GetType().GetField("_canvas", Private)!.GetValue(pause)!;
        pause.SetProcess(false);   // 以降、_mousePos はこのツールが握る

        // ── あそびかた：タブ／ドット／とじる のホバー ──
        var how = GetNode<HowToPlay>("/root/HowTo");
        how.GetType().GetField("_autoplay", Private)!.SetValue(how, false);
        await SwapScene("res://Hub.tscn");
        how.Open();
        await Frames(4);
        // 選択中でないタブをホバー＝「押せる」強調が選択状態と区別できることを見る。
        int other = (how.Page + 1) % HowToPlay.TabCount;
        await ShotHover(how, HowToPlay.TabRect(other).GetCenter(), "howto_tab_hover");
        await ShotHover(how, HowToPlay.DotRect(3).GetCenter(), "howto_dot_hover");
        await ShotHover(how, HowToPlay.CloseHintRect().GetCenter(), "howto_close_hover");
        await ShotHover(how, new Vector2(20, 20), "howto_idle");
        how.GetType().GetMethod("Close", Private)!.Invoke(how, null);
        await Frames(4);

        // ── 会話ログ：とじる のホバー ──
        var log = GetNode<Backlog>("/root/Backlog");
        log.GetType().GetField("_autoplay", Private)!.SetValue(log, false);
        typeof(Hud).GetMethod("PushBacklog", Stat)!
            .Invoke(null, new object[] { "ミナ", "これはバックログの表示確認用の1行です。", UiKit.Mina, Hud.LineKind.Mina });
        log.Open();
        await Frames(4);
        await ShotHover(log, Backlog.CloseHintRect().GetCenter(), "backlog_close_hover");
        log.GetType().GetMethod("Close", Private)!.Invoke(log, null);
        await Frames(6);

        // ── 右下「Esc／メニュー」ヒント：Hub（押せる）とステージ（押せない）──
        await SwapScene("res://Hub.tscn");
        await ShotHover(pauseCanvas, PauseMenu.HintRect().GetCenter(), "pausehint_hub_hover");
        await ShotHover(pauseCanvas, new Vector2(20, 20), "pausehint_hub_idle");
        await SwapScene("res://Akari.tscn");
        await ShotHover(pauseCanvas, PauseMenu.HintRect().GetCenter(), "pausehint_stage_hover");

        // ── 設定／難易度選択：フッタ「もどる」のホバー ──
        await SwapScene("res://Settings.tscn");
        await ShotHover(GetTree().CurrentScene, StaticRect(typeof(Settings), "BackHintRect").GetCenter(), "settings_back_hover");
        GetNode<GameManager>("/root/Game").PendingStageScene = "res://Akari.tscn";
        await SwapScene("res://DiffSelect.tscn");
        await ShotHover(GetTree().CurrentScene, StaticRect(typeof(DiffSelect), "BackHintRect").GetCenter(), "diffselect_back_hover");

        GD.Print("[MouseQA] shots done.");
        GetTree().Quit();
    }

    // カーソルを p に置いた状態で target の _Process を数フレーム回し、描画が乗ってから撮る。
    private async Task ShotHover(Node target, Vector2 p, string name)
    {
        for (int i = 0; i < 4; i++)
        {
            SetMouse(p, false);
            target.GetType().GetMethod("_Process", BindingFlags.Instance | BindingFlags.Public)?
                .Invoke(target, new object[] { 0.016 });
            // 描画は CanvasItem 側（オーバーレイは _canvas フィールドの子 Node2D）。両方叩いておく。
            if (target is CanvasItem ci) ci.QueueRedraw();
            if (target.GetType().GetField("_canvas", Private)?.GetValue(target) is CanvasItem sub) sub.QueueRedraw();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.PushWarning($"[MouseQA] no image for {name}"); return; }
        string path = $"{_shotDir!.TrimEnd('/')}/mouse_{name}.png";
        GD.Print($"[MouseQA] saved {path} err={img.SavePng(path)}");
    }

    private async Task SwapScene(string path)
    {
        var old = GetTree().CurrentScene;
        if (old != null && old != this)
        {
            // QueueFree は遅延削除＝同フレームにまだ描かれる。撮影では前の画面が残って見えるので
            //   ツリーから外してから捨てる（RemoveChild は即時）。
            old.GetParent()?.RemoveChild(old);
            old.QueueFree();
            await Frames(2);
        }
        var node = GD.Load<PackedScene>(path).Instantiate();
        GetTree().Root.AddChild(node);
        GetTree().CurrentScene = node;
        await Frames(20);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
