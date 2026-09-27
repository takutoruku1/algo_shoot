using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

// KeyCapSweepQa : スマホ系の全画面を「矢印で選ぶ・Z で決める・Esc／X でもどる」に揃え、キーの表記を UiKit.KeyCap
//   （押せる鍵の形・パッドの丸ボタン・マウスの絵）へ統一した変更（2026-09-27）の検証。
//   (a) ショップ：T を押してもトレーニングへ行かない。→ で [ためし撃ち] にフォーカス → Z で行ける
//   (b) 記録：T ではもどらない。Esc／X でもどる
//   (c) カスタマイズ：カテゴリ／キャラ／ポーズ／アイテム／購入確認まで、フォーカス（矢印）＋Z だけで届く。Esc でもどる
//   (d) Hub（ホーム／写真）・ショップ・記録・カスタマイズ・難易度選択で PauseMenu.ShowHint==false、
//       右下のヒント帯（UiKit.HintBarRects）が画面内の右下に収まり、末尾が「メニュー」
//   (e) Hud.KeyBadge の呼び出し 0 件は grep で確認（このツールでは見ない）
//   窓ありで `-- --kc-shot` を付けると build/shots_keycap/ に各画面（KB／パッド）、あそびかたの3タブ、
//   サイドパネルの拡大（LOCK-ON の Shift が沈んだ状態と通常・マウス時の絵）、トレーニングの左下ヒントを保存する。
//   実行: Godot --headless --path . res://tools/qa_keycap_sweep.tscn（APPDATA は build/qa_story/ 配下）
public partial class KeyCapSweepQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object? value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[KeyCapQA] PASS {message}");
    }

    private bool _shots;
    private string _out = "";
    private GameManager _game = null!;
    private PauseMenu _pause = null!;

    public override async void _Ready()
    {
        try
        {
            _shots = Array.IndexOf(OS.GetCmdlineUserArgs(), "--kc-shot") >= 0;
            _out = ProjectSettings.GlobalizePath("res://build/shots_keycap");
            if (_shots) DirAccess.MakeDirRecursiveAbsolute(_out);
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _game = GetNode<GameManager>("/root/Game");
            _pause = GetNode<PauseMenu>("/root/PauseMenu");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            foreach (var job in Jobs.All) _game.MarkIdleDialogSeen($"once_companion_select_{job.CharacterId}");
            _game.MarkIdleDialogSeen("once_sns_intro");
            _game.MarkIdleDialogSeen("once_phone_home");
            _game.MarkIdleDialogSeen("once_account_intro");
            _game.ShopTutorialSeen = true;
            foreach (var s in GameManager.Stages) Read<HashSet<string>>(_game, "_cleared").Add(s.Id);
            _game.TrainingSetImpression(20000);
            _game.PendingStageScene = "res://Akari.tscn";
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            SetDevice(pad: false);

            await CheckShop();
            await CheckRecords();
            await CheckCustomize();
            await CheckDiffSelect();
            await CheckHub();
            if (_shots) await ShotRun();

            Audio.Instance?.StopMusic(0);
            await Frames(4);
            GD.Print("[KeyCapQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[KeyCapQA] FAIL {ex.Message}");
            GD.PrintErr(ex.StackTrace ?? "");
            GetTree().Quit(1);
        }
    }

    // ───────── (a) ショップ ─────────
    private async Task CheckShop()
    {
        var shop = await Open<Shop>("res://Shop.tscn");
        Write(shop, "_t", 3.0);
        await CheckHintBar(shop, "Shop", shop.HintBarRect, Items(shop));
        var items = Items(shop);
        Check(Array.Exists(items, i => i.token == "←→" && i.label == "きりかえ"), "(d) Shop: keyboard hint has [←→] きりかえ");
        await Key(shop, Godot.Key.T);
        Check(GetTree().CurrentScene == shop, "(a) Shop: T does not open training any more");
        await Action(shop, "ui_right");
        Check(shop.Action == 1, "(a) Shop: → focuses [ためし撃ち]");
        await Action(shop, "ui_accept");
        await Frames(6);
        Check(GetTree().CurrentScene is TrainingRoot, "(a) Shop: focus + Z opens training");
        await Leave();
    }

    // ───────── (b) 記録 ─────────
    private async Task CheckRecords()
    {
        var rec = await Open<Records>("res://Records.tscn");
        Write(rec, "_t", 1.0);
        await CheckHintBar(rec, "Records", rec.HintBarRect, Items(rec));
        await Key(rec, Godot.Key.T);
        Check(GetTree().CurrentScene == rec && !Read<bool>(rec, "_leaving"), "(b) Records: T does not go back");
        await Key(rec, Godot.Key.Escape);
        await Frames(4);
        Check(GetTree().CurrentScene is Hub, "(b) Records: Esc goes back to the phone home");
        await Leave();
        rec = await Open<Records>("res://Records.tscn");
        Write(rec, "_t", 1.0);
        await Key(rec, Godot.Key.X);
        await Frames(4);
        Check(GetTree().CurrentScene is Hub, "(b) Records: X goes back too");
        await Leave();
    }

    // ───────── (c) カスタマイズ ─────────
    private async Task CheckCustomize()
    {
        var menu = await Open<Customize>("res://Customize.tscn");
        Write(menu, "_time", 2.0);
        await CheckHintBar(menu, "Customize", menu.HintBarRect, Items(menu));
        Check(Read<JobTuning[]>(menu, "_characters").Length == 4, "(c) setup: four accounts in the wardrobe");
        Check(Read<int>(menu, "_focus") == 3, "(c) Customize starts on the item row");
        for (int i = 0; i < 3; i++) await Action(menu, "ui_up");
        Check(Read<int>(menu, "_focus") == 0, "(c) ↑ reaches the category row");
        await Action(menu, "ui_accept");
        Check(Read<int>(menu, "_tab") == 1, "(c) Z on the category row switches to costumes");
        await Action(menu, "ui_down");
        int ch = Read<int>(menu, "_character");
        await Action(menu, "ui_accept");
        Check(Read<int>(menu, "_focus") == 1 && Read<int>(menu, "_character") == (ch + 1) % 4, "(c) Z on the character row picks the next account");
        await Action(menu, "ui_down");
        int pose = Read<int>(menu, "_pose");
        await Action(menu, "ui_accept");
        Check(Read<int>(menu, "_pose") == (pose + 1) % 3, "(c) Z on the pose row changes the preview pose");
        await Action(menu, "ui_down");
        Check(Read<int>(menu, "_focus") == 3, "(c) ↓ reaches the item row");
        var itemsBefore = Read<CosmeticItem[]>(menu, "_items");
        int sel = Read<int>(menu, "_selected");
        await Action(menu, "ui_right");
        int target = Read<int>(menu, "_selected");
        Check(target == (sel + 1) % itemsBefore.Length, "(c) → picks the next item");
        var item = itemsBefore[target];
        Check(!_game.OwnsCosmetic(item.Id), "(c) setup: the picked costume is not owned yet");
        await Action(menu, "ui_down");
        Check(Read<int>(menu, "_focus") == 4, "(c) ↓ reaches the buy / equip button");
        await Action(menu, "ui_accept");
        Check(Read<string?>(menu, "_pendingPurchase") == item.Id, "(c) Z on the button opens the purchase confirmation");
        var confirmItems = Items(menu);
        Check(Array.Exists(confirmItems, i => i.label == "キャンセル"), "(d) Customize: confirmation hint reads キャンセル");
        await Action(menu, "ui_right");
        Check(Read<bool>(menu, "_confirmYes"), "(c) → moves to 購入して装備");
        long before = _game.Impression;
        await Action(menu, "ui_accept");
        Check(_game.OwnsCosmetic(item.Id) && _game.Impression == before - item.Price && Read<string?>(menu, "_pendingPurchase") == null,
            "(c) Z confirms: bought and equipped with keys only");
        await Key(menu, Godot.Key.Escape);
        await Frames(4);
        Check(GetTree().CurrentScene is Hub, "(c) Esc goes back to the phone home");
        await Leave();
    }

    // ───────── 難易度選択 ─────────
    private async Task CheckDiffSelect()
    {
        var diff = await Open<Node2D>("res://DiffSelect.tscn");
        Write(diff, "_autoplay", false);
        Write(diff, "_t", 1.0);
        var items = StaticItems(typeof(DiffSelect));
        await CheckHintBar(diff, "DiffSelect", UiKit.HintBarBounds(UiKit.HintAnchor, items), items);
        await Leave();
    }

    // ───────── Hub（ホーム・写真アプリ）─────────
    private async Task CheckHub()
    {
        var hub = await Open<Hub>("res://Hub.tscn");
        await Frames(30);
        SetMode(hub, "Home");
        await CheckHintBar(hub, "Hub home", hub.HintBarRect, HubItems(hub));
        SetMode(hub, "Photos");
        var items = HubItems(hub);
        await CheckHintBar(hub, "Hub photos", hub.HintBarRect, items);
        Check(Array.Exists(items, i => i.label == "背景にする") && Array.Exists(items, i => i.label == "もどる"),
            "(d) Hub photos: the hint bar carries 背景にする / もどる (the in-phone key line was removed)");
        await Leave();
    }

    // (d) 共通：チップは出ない・帯は画面内の右下・末尾はメニュー。パッド表記では 十字／A／B／≡。
    private async Task CheckHintBar(Node screen, string where, Rect2 r, (string token, string label)[] items)
    {
        await Frames(2);
        var view = new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH);
        Check(!_pause.ShowHint, $"(d) {where}: PauseMenu.ShowHint == false");
        var rects = UiKit.HintBarRects(UiKit.HintAnchor, items);
        Check(items.Length > 0 && rects.Length == items.Length && Array.TrueForAll(rects, x => view.Encloses(x)),
            $"(d) {where}: every hint item rect is on screen");
        Check(r.HasArea() && view.Encloses(r) && r.End.X > UiKit.DesignW - 24f && r.End.Y > UiKit.DesignH - 24f,
            $"(d) {where}: hint bar sits at the bottom-right ({r})");
        Check(items[^1].label == "メニュー" && items[^1].token == "M", $"(d) {where}: last item is [M] メニュー");
        SetDevice(pad: true);
        var padItems = ItemsOf(screen, where);
        Check(padItems[^1].token == "≡" && Array.TrueForAll(padItems, i => i.token is "十字" or "A" or "B" or "≡"),
            $"(d) {where}: pad hint uses 十字 / A / B / ≡");
        SetDevice(pad: false);
    }

    private (string token, string label)[] ItemsOf(Node screen, string where) =>
        screen is Hub hub ? HubItems(hub) : screen is Shop or Records or Customize ? Items(screen) : StaticItems(typeof(DiffSelect));

    private static (string token, string label)[] Items(object screen) =>
        ((string token, string label)[])screen.GetType().GetMethod("HintItems", Private)!.Invoke(screen, null)!;
    private static (string token, string label)[] StaticItems(Type t) =>
        ((string token, string label)[])t.GetMethod("HintItems", Stat)!.Invoke(null, null)!;
    private static (string token, string label)[] HubItems(Hub hub) =>
        ((string token, string label)[])typeof(Hub).GetMethod("HintItems", Private)!.Invoke(hub, null)!;

    private static void SetMode(Hub hub, string mode)
    {
        var cur = Read<object>(hub, "_mode");
        Write(hub, "_mode", Enum.Parse(cur.GetType(), mode));
    }

    // ───────── スクショ（窓ありのみ）─────────
    private async Task ShotRun()
    {
        foreach (bool pad in new[] { false, true })
        {
            string suffix = pad ? "_pad" : "_kb";
            SetDevice(pad);
            var shop = await Open<Shop>("res://Shop.tscn");
            Write(shop, "_toastT", 0.0);
            await Shot("shop" + suffix);
            await ShotZoom("shop_hint" + suffix, shop.HintBarRect);
            Write(shop, "_act", 1);
            await Shot("shop_training_focus" + suffix);
            await Leave();

            var rec = await Open<Records>("res://Records.tscn");
            await Shot("records" + suffix);
            await ShotZoom("records_hint" + suffix, rec.HintBarRect);
            await Leave();

            var menu = await Open<Customize>("res://Customize.tscn");
            await Shot("customize" + suffix);
            await ShotZoom("customize_hint" + suffix, menu.HintBarRect);
            await Leave();

            var diff = await Open<Node2D>("res://DiffSelect.tscn");
            Write(diff, "_autoplay", false);
            await Shot("diffselect" + suffix);
            await ShotZoom("diffselect_hint" + suffix, UiKit.HintBarBounds(UiKit.HintAnchor, StaticItems(typeof(DiffSelect))));
            await Leave();

            var hub = await Open<Hub>("res://Hub.tscn");
            await Frames(30);
            SetMode(hub, "Photos");
            await Shot("hub_photos" + suffix);
            await ShotZoom("hub_photos_hint" + suffix, hub.HintBarRect);
            await Leave();
        }
        SetDevice(pad: false);

        // キャップ見本（全種類を高さ 22 と 44 で。マウスの絵・押下状態の確認用）。
        await Leave();
        var gallery = new CapGallery();
        GetTree().Root.AddChild(gallery);
        GetTree().CurrentScene = gallery;
        await Frames(4);
        await Shot("caps_gallery");
        await ShotZoom("caps_gallery_zoom", new Rect2(20, 20, 520, 150), 4);
        await Leave();

        // あそびかた：キーボード／コントローラー／マウスの3タブ（表示は KB のまま＝パッドタブも丸ボタンで描けること）。
        var hubForHow = await Open<Hub>("res://Hub.tscn");
        var how = GetNode<HowToPlay>("/root/HowTo");
        Write(how, "_autoplay", false);
        how.Open();
        foreach (int tab in new[] { 0, 1, 2 })
        {
            Write(how, "_page", tab);
            await Frames(3);
            await Shot($"howto_tab{tab}_" + new[] { "keyboard", "pad", "mouse" }[tab]);
        }
        Write(how, "_page", HowToPlay.PageHud);
        await Frames(3);
        await ShotZoom("howto_footer", HowToPlay.CloseHintRect().Grow(260f));
        how.GetType().GetMethod("Close", Private)!.Invoke(how, null);
        await Frames(3);
        _ = hubForHow;
        await Leave();

        // サイドパネル：LOCK-ON の Shift キャップ（通常／沈んだ状態）とマウス時の絵、BOMB の X、集中の C。
        var stage = await Open<Node>("res://Akari.tscn");
        await Frames(40);
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        Check(player != null, "shot setup: a player exists in the stage");
        var panel = new Rect2(0, 170, 380, 540);
        Write(player!, "_lockArmed", false);
        await Frames(3);
        await ShotZoom("sidepanel_kb", panel, 2);
        Write(player!, "_lockArmed", true);
        Write(player!, "_lockByShift", false);
        await Frames(3);
        await ShotZoom("sidepanel_kb_lock_pressed", panel, 2);
        SetDevice(pad: false, mouse: true);
        await Frames(3);
        await ShotZoom("sidepanel_mouse", panel, 2);
        SetDevice(pad: true);
        await Frames(3);
        await ShotZoom("sidepanel_pad", panel, 2);
        SetDevice(pad: false);
        Write(player!, "_lockArmed", false);
        _ = stage;
        await Leave();

        // トレーニング：左下の操作ヒント（KB とパッド）。
        foreach (bool pad in new[] { false, true })
        {
            SetDevice(pad);
            await Open<Node>("res://Training.tscn");
            await Frames(20);
            await Shot("training" + (pad ? "_pad" : "_kb"));
            await ShotZoom("training_hint" + (pad ? "_pad" : "_kb"), new Rect2(30, UiKit.DesignH - 108, 840, 60), 2);
            await Leave();
        }
        SetDevice(pad: false);
    }

    // キャップの見本帳（撮影専用）。上段＝キーボードの絵、中段＝パッドの絵、下段＝マウスの絵と並び（KeyCapRow）。
    private partial class CapGallery : Node2D
    {
        public override void _Draw()
        {
            UiKit.BeginDesign(this);
            DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color(0.05f, 0.06f, 0.08f));
            string[] kb = { "Z", "Esc", "Shift", "Space", "↑↓←→", "↑↓", "←→", "M" };
            string[] pad = { "A", "B", "X", "Y", "LB", "RB", "L3", "L", "十字", "≡" };
            string[] mouse = { "左クリック", "右クリック", "中クリック", "ホイール" };
            float y = 30f;
            foreach (float h in new[] { 22f, 44f })
            {
                foreach (bool pressed in new[] { false, true })
                {
                    float x = 30f;
                    foreach (var t in kb) x += UiKit.KeyCap(this, new Vector2(x, y), t, h, pressed, 1f, false) + 10f;
                    foreach (var t in pad) x += UiKit.KeyCap(this, new Vector2(x, y), t, h, pressed, 1f, true) + 10f;
                    y += h + 14f;
                    x = 30f;
                    foreach (var t in mouse) x += UiKit.KeyCap(this, new Vector2(x, y), t, h, pressed, 1f, false) + 16f;
                    y += h + 22f;
                }
            }
            float rx = 30f;
            foreach (var spec in new[] { "Z / Enter / Space", "Shift 長押し", "R / Shift+R", "L スティック / 十字キー", "左クリック 長押し", "オート" })
                rx += UiKit.KeyCapRow(this, new Vector2(rx, y), spec, 24f, 1f, spec.StartsWith("L ")) + 28f;
            UiKit.EndDesign(this);
        }
    }

    // ───────── 共通の道具 ─────────
    private async Task<T> Open<T>(string path) where T : Node
    {
        await Leave();
        var node = GD.Load<PackedScene>(path).Instantiate<T>();
        GetTree().Root.AddChild(node);
        GetTree().CurrentScene = node;
        await Frames(8);
        return node;
    }

    private async Task Leave()
    {
        var cur = GetTree().CurrentScene;
        if (cur != null && cur != this && GodotObject.IsInstanceValid(cur))
        {
            cur.GetParent()?.RemoveChild(cur);
            cur.QueueFree();
        }
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        await Frames(3);
    }

    private static void SetDevice(bool pad, bool mouse = false)
    {
        typeof(Pad).GetField("_autoUsingPad", Stat)!.SetValue(null, pad);
        typeof(Pad).GetField("_usingMouse", Stat)!.SetValue(null, mouse);
    }

    // アクション（ui_*）を押して離す。対象の _Process を直に回す（CustomizeQa.Nav と同じ流儀）。
    private async Task Action(Node target, string action)
    {
        Input.ActionPress(action);
        Proc(target);
        Input.ActionRelease(action);
        Proc(target);
        await Frames(1);
    }

    // 物理キーを押して離す（Input.IsKeyPressed で読む画面向け）。
    private async Task Key(Node target, Godot.Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.FlushBufferedEvents();
        Proc(target);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        Input.FlushBufferedEvents();
        if (GodotObject.IsInstanceValid(target) && target.IsInsideTree()) Proc(target);
        await Frames(3);
    }

    private static void Proc(Node target) =>
        target.GetType().GetMethod("_Process", BindingFlags.Instance | BindingFlags.Public)!.Invoke(target, new object[] { 1.0 / 60 });

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        string path = $"{_out}/{name}.png";
        Check(image.SavePng(path) == Error.Ok, $"screenshot {path}");
    }

    // 設計座標の矩形のまわりを切り抜いて拡大して保存する（キャップの読みやすさの確認用）。
    private async Task ShotZoom(string name, Rect2 design, int scale = 3)
    {
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        float k = image.GetWidth() / UiKit.DesignW;
        var grown = design.Grow(10f).Intersection(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH));
        var px = new Rect2I((int)(grown.Position.X * k), (int)(grown.Position.Y * k), (int)(grown.Size.X * k), (int)(grown.Size.Y * k));
        var crop = image.GetRegion(px);
        crop.Resize(crop.GetWidth() * scale, crop.GetHeight() * scale, Image.Interpolation.Nearest);
        string path = $"{_out}/{name}.png";
        Check(crop.SavePng(path) == Error.Ok, $"screenshot {path}");
    }
}
