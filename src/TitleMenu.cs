using Godot;

// TitleMenu : スタート画面。RefrainHTML/Refrain Title.dc.html を忠実移植（非ピクセル・滑らかUI）。
//   深い夜グラデ背景＋浮遊する言葉＋漂う弾＋光のオーブ＋グラデ大見出し＋シアン選択メニュー＋Xティッカー。
//   ↑↓ で選択・Z で決定。設計座標 1280×720 のまま UiKit.BeginDesign で描く。
public partial class TitleMenu : Node2D
{
    private GameManager _game = null!;

    private enum Item { NewGame, Continue, HowToPlay, Settings, Quit }
    private static readonly (Item item, string jp, string en)[] Items =
    {
        (Item.NewGame,   "はじめから", "NEW GAME"),
        (Item.Continue,  "つづきから", "CONTINUE"),
        (Item.HowToPlay, "あそびかた", "HOW TO PLAY"),
        (Item.Settings,  "設定",       "SETTINGS"),
        (Item.Quit,      "おわる",     "QUIT"),
    };

    // 浮遊する言葉（left%, top%, size, alpha, driftSpeed, phase）
    private static readonly (float x, float y, int size, float a, float spd, float ph)[] Words =
    {
        (0.12f, 0.24f, 20, 0.12f, 7f,   0f),
        (0.58f, 0.66f, 18, 0.10f, 8f,   0.6f),
        (0.80f, 0.54f, 17, 0.10f, 9f,   1.2f),
        (0.44f, 0.18f, 16, 0.09f, 7.5f, 0.9f),
    };
    private static readonly string[] WordText = { "あたしのせいだ", "きえたい", "とどかない", "ひとりになる" };

    private static readonly (string h, string t)[] Ticker =
    {
        ("@rei_0w0", "ごめん、もう無理かも。"),
        ("@kako__",  "どうせ、とどかない"),
        ("@nobody_7","もういない"),
    };

    private int _sel;
    private bool _navHeld, _zHeld, _backHeld, _hasSave, _picking;
    private int _pick; // つづきから：選択中スロット(0..2)
    private double _t, _toastT;
    private string _toast = "";
    private bool _autoplay, _dived;

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        _hasSave = _game.SlotExists(0) || _game.SlotExists(1) || _game.SlotExists(2) || _game.SlotExists(3);
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
        _sel = _hasSave ? 1 : 0;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_toastT > 0) _toastT -= delta;
        if (_dived) { QueueRedraw(); return; }
        if (_autoplay) { if (_t > 0.3) Go("res://Hub.tscn"); QueueRedraw(); return; }

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld;

        // 「つづきから」スロット選択中：↑↓で選び Z=ロード / X=やめる（0=オートセーブ）
        if (_picking)
        {
            int n = GameManager.SlotCount + 1; // 0=オート + 1..3=手動
            bool pu = Input.IsActionPressed("ui_up"), pd = Input.IsActionPressed("ui_down");
            if ((pu || pd) && !_navHeld)
            {
                if (pu) _pick = (_pick + n - 1) % n;
                if (pd) _pick = (_pick + 1) % n;
                Audio.Instance?.PlayUiMove();
            }
            _navHeld = pu || pd;
            if (zEdge && _game.SlotExists(_pick)) { Audio.Instance?.PlayUiConfirm(); _game.LoadFromSlot(_pick); Go("res://Hub.tscn"); }
            else if (backEdge) { Audio.Instance?.PlayUiCancel(); _picking = false; }
            _zHeld = z; _backHeld = back;
            QueueRedraw();
            return;
        }

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            if (up) _sel = (_sel - 1 + Items.Length) % Items.Length;
            if (down) _sel = (_sel + 1) % Items.Length;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;

        if (zEdge && _t > 0.2) { Audio.Instance?.PlayUiConfirm(); Confirm(); }
        _zHeld = z; _backHeld = back;

        QueueRedraw();
    }

    private void Confirm()
    {
        switch (Items[_sel].item)
        {
            case Item.NewGame:
                // はじめから＝まっさらスタート（スロットは消さない。進行は後でメニューから保存）。
                _game.ResetPersistent();
                Go("res://Prologue.tscn");
                break;
            case Item.Continue:
                if (_hasSave) { _picking = true; _pick = FirstSlot(); }
                else Toast("セーブデータがありません");
                break;
            case Item.HowToPlay:
                // 「あそびかた」＝STAGE1にチュートリアルを重ねて強制再生（既読フラグは変えない）。
                _game.ForceTutorialReplay = true;
                Go("res://Rei.tscn");
                break;
            case Item.Settings: Go("res://Settings.tscn"); break;
            case Item.Quit:     GetTree().Quit(); break;
        }
    }

    private void Toast(string msg) { _toast = msg; _toastT = 2.0; }
    private void Go(string scene) { if (_dived) return; _dived = true; GetTree().ChangeSceneToFile(scene); }
    private int FirstSlot()
    {
        for (int i = 0; i <= GameManager.SlotCount; i++) // 0(オート)..3
            if (_game.SlotExists(i)) return i;
        return 0;
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float t = (float)_t;

        // ── 背景（縦グラデ ＋ 放射グロウ）──
        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0e1834"), new Color("0a1126"), new Color("070a16") },
            new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.76f, H * 0.30f), 460f, new Color(120 / 255f, 150 / 255f, 210 / 255f), 0.22f);
        UiKit.RadialGlow(this, new Vector2(W * 0.18f, H * 0.92f), 360f, new Color(154 / 255f, 114 / 255f, 217 / 255f), 0.18f);

        // ── 浮遊する言葉 ──
        for (int i = 0; i < Words.Length; i++)
        {
            var w = Words[i];
            float dy = Mathf.Sin(t / w.spd * Mathf.Pi * 2f + w.ph * 6f) * 8f;
            UiKit.Text(this, UiKit.Zen, new Vector2(w.x * W, w.y * H + dy), WordText[i], w.size,
                new Color(200 / 255f, 184 / 255f, 216 / 255f, w.a));
        }

        // ── 漂う弾（グロウ付き）──
        DrawDanmaku(new Vector2(W * 0.66f, H * 0.40f), 6f, UiKit.Kegare);
        DrawDanmaku(new Vector2(W * 0.88f, H * 0.64f), 5f, UiKit.Purify);

        // ── 本人（光）オーブ：脈動 ──
        Vector2 oc = new(884 + 115, 216 + 115);
        float pulse = 0.92f + 0.08f * Mathf.Sin(t * Mathf.Pi * 2f / 4f);
        UiKit.RadialGlow(this, oc, 150f, UiKit.Light, 0.5f * pulse);
        UiKit.RadialGlow(this, oc, 100f, new Color(1f, 0.94f, 0.77f), 0.85f * pulse);
        DrawCircle(oc, 42f, new Color(1f, 1f, 1f, 0.95f * pulse));

        // ── ミナ オーブ ──
        Vector2 mc = new(866 + 15, 417 + 15);
        float mFloat = Mathf.Sin(t * Mathf.Pi * 2f / 5f) * 6f;
        mc.Y += mFloat;
        UiKit.RadialGlow(this, mc, 36f, new Color(200 / 255f, 180 / 255f, 1f), 0.5f);
        DrawCircle(mc, 15f, UiKit.Mina);
        DrawCircle(mc - new Vector2(3, 4), 5f, new Color(1, 1, 1, 0.9f));

        // ── スキャンライン（控えめ）──
        for (float y = 0; y < H; y += 6f)
            DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.08f));

        DrawTitleBlock();
        DrawMenu();
        DrawPrompt();

        // ── バージョン（右下）──
        UiKit.Text(this, UiKit.Mono, new Vector2(W - 230, H - 48), "ver 0.3.0 — 体験版", 11, UiKit.Text4, HorizontalAlignment.Right, 204);
        UiKit.Text(this, UiKit.Mono, new Vector2(W - 230, H - 30), "© 2026 algo project", 11, UiKit.Text4, HorizontalAlignment.Right, 204);

        DrawTicker();
        DrawToast();
        if (_picking) DrawSlotPicker();
        UiKit.EndDesign(this);
    }

    // 「つづきから」スロット選択ダイアログ（オート＋3スロット・空きはグレー）。
    private void DrawSlotPicker()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.6f)); // 暗幕
        int n = GameManager.SlotCount + 1;
        float w = 560, rowH = 56, h = 100 + n * rowH, x = (W - w) / 2f, y = (H - h) / 2f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 16f, new Color(UiKit.Purify, 0.7f), 1.4f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + 26), "つづきから — スロットを選ぶ", 18, UiKit.White, HorizontalAlignment.Center, w);
        float top = y + 64;
        for (int i = 0; i < n; i++)
        {
            float ry = top + i * rowH;
            bool on = i == _pick;
            bool exists = _game.SlotExists(i);
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 28, ry, w - 56, 46), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 10f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 44, ry + 14), "▸", 16, UiKit.Purify);
            }
            Color nameCol = exists ? (on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f)) : UiKit.Text4;
            string name = i == 0 ? "オートセーブ" : $"スロット {i}";
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 70, ry + 12), name, 19, nameCol);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 220, ry + 15), exists ? "セーブあり" : "—— 空き ——", 12,
                exists ? UiKit.Info : UiKit.Text4, HorizontalAlignment.Right, 192);
        }
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 28), "Z 決定    X 戻る", 11, UiKit.Text3, HorizontalAlignment.Center, w);
    }

    private void DrawDanmaku(Vector2 c, float r, Color col)
    {
        UiKit.RadialGlow(this, c, r * 2.4f, col, 0.6f);
        DrawCircle(c, r, col);
        DrawCircle(c - new Vector2(r * 0.3f, r * 0.35f), r * 0.4f, new Color(1, 1, 1, 0.9f));
    }

    private void DrawTitleBlock()
    {
        float x = 88f;
        UiKit.Text(this, UiKit.Mono, new Vector2(x, 92), "A L G O :", 15, UiKit.Info);
        // 大見出し（白→シアン→紫のグラデを2行の色分けで近似）＋落ち影
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 2, 122), "Refrain", 62, new Color(0.08f, 0.06f, 0.16f, 0.6f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, 120), "Refrain", 62, UiKit.PurifyHi);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 2, 190), "of Light", 62, new Color(0.08f, 0.06f, 0.16f, 0.6f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, 188), "of Light", 62, new Color(155 / 255f, 183 / 255f, 232 / 255f));
        // 区切り＋サブ
        DrawRect(new Rect2(x, 270, 34f, 2f), UiKit.Purify);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 46, 262), "心象シューティング", 16, UiKit.Text2);
        UiKit.Text(this, UiKit.Zen, new Vector2(x, 286), "— その痛みに、光を届けに。", 14, UiKit.Text3);
    }

    private void DrawMenu()
    {
        float x = 88f, top = 330f, rowH = 44f, gap = 4f, w = 360f;
        for (int i = 0; i < Items.Length; i++)
        {
            float ry = top + i * (rowH + gap);
            bool on = i == _sel;
            bool disabled = Items[i].item == Item.Continue && !_hasSave;

            if (on)
            {
                UiKit.Box(this, new Rect2(x, ry, w, rowH), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 12f,
                    new Color(UiKit.Purify, 0.45f), 1f);
            }
            // ▸ カーソル
            if (on) UiKit.Text(this, UiKit.Mono, new Vector2(x + 18, ry + 11), "▸", 18, UiKit.Purify);
            // 名前
            var nameFont = on ? UiKit.ZenBlack : UiKit.ZenBold;
            Color nameCol = disabled ? UiKit.Text4 : (on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f));
            UiKit.Text(this, nameFont, new Vector2(x + 42, ry + 7), Items[i].jp, 23, nameCol);
            // EN ラベル（右）
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 130, ry + 14), Items[i].en, 11,
                on ? UiKit.Info : UiKit.Text4, HorizontalAlignment.Right, 120);
        }
    }

    private void DrawPrompt()
    {
        float x = 88f, y = 656f;
        float blink = 0.55f + 0.45f * Mathf.Sin((float)_t * Mathf.Pi * 2f / 1.6f);
        UiKit.Key(this, new Vector2(x, y), "Z", new Color(UiKit.Purify, 0.14f * blink + 0.06f), new Color(UiKit.Info, 0.5f), UiKit.PurifyHi);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 36, y + 4), "けってい", 13, UiKit.Info);
        UiKit.Key(this, new Vector2(x + 130, y), "↑↓", new Color(1, 1, 1, 0.06f), new Color(1, 1, 1, 0.16f), UiKit.Text2);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 178, y + 4), "えらぶ", 13, UiKit.Text3);
    }

    private void DrawTicker()
    {
        float W = UiKit.DesignW, barH = 34f;
        UiKit.VGradient(this, new Rect2(0, 0, W, barH),
            new[] { new Color(10 / 255f, 8 / 255f, 16 / 255f, 0.55f), new Color(10 / 255f, 8 / 255f, 16 / 255f, 0f) },
            new[] { 0f, 1f });
        // DIVING ラベル
        DrawCircle(new Vector2(20, barH / 2f), 5f, UiKit.Purify);
        UiKit.Text(this, UiKit.Mono, new Vector2(34, barH / 2f - 6), "DIVING", 10, UiKit.Text3);

        // スクロールするツイート
        float startX = 120f, gap = 60f;
        // 1ブロックの幅を概算して周期スクロール
        float block = 0f;
        foreach (var (h, tx) in Ticker) block += UiKit.TextW(UiKit.Mono, h, 12) + 6 + UiKit.TextW(UiKit.Zen, tx, 12) + gap;
        float scroll = ((float)_t * 60f) % block;
        float cx = startX - scroll;
        for (int rep = 0; rep < 3; rep++)
        {
            foreach (var (h, txt) in Ticker)
            {
                if (cx > 80 && cx < W)
                {
                    UiKit.Text(this, UiKit.Mono, new Vector2(cx, barH / 2f - 6), h, 12, UiKit.Text4);
                    float hw = UiKit.TextW(UiKit.Mono, h, 12) + 6;
                    UiKit.Text(this, UiKit.Zen, new Vector2(cx + hw, barH / 2f - 7), txt, 12, new Color(UiKit.Text2, 0.5f));
                }
                cx += UiKit.TextW(UiKit.Mono, h, 12) + 6 + UiKit.TextW(UiKit.Zen, txt, 12) + gap;
            }
        }
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        float W = UiKit.DesignW;
        float w = UiKit.TextW(UiKit.ZenBold, _toast, 16) + 48;
        float x = (W - w) / 2f;
        UiKit.Box(this, new Rect2(x, 600, w, 44), new Color(0.06f, 0.05f, 0.10f, 0.96f), 12f, new Color(UiKit.Purify, 0.7f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, 612), _toast, 16, UiKit.Text2, HorizontalAlignment.Center, w);
    }
}
