using Godot;

// TitleMenu : スタート画面。RefrainHTML/Refrain Title.dc.html を忠実移植（非ピクセル・滑らかUI）。
//   深い夜グラデ背景＋浮遊する言葉＋漂う弾＋光のオーブ＋グラデ大見出し＋シアン選択メニュー＋Xティッカー。
//   ↑↓ で選択・Z で決定。設計座標 1280×720 のまま UiKit.BeginDesign で描く。
public partial class TitleMenu : Node2D
{
    private GameManager _game = null!;

    private enum Item { NewGame, Continue, HowToPlay, Tutorial, Settings, Quit }
    private static readonly (Item item, string jp, string en)[] Items =
    {
        (Item.NewGame,   "はじめから",       "NEW GAME"),
        (Item.Continue,  "つづきから",       "CONTINUE"),
        (Item.HowToPlay, "あそびかた",       "HOW TO PLAY"),
        (Item.Tutorial,  "チュートリアル",   "TUTORIAL"),
        (Item.Settings,  "設定",             "SETTINGS"),
        (Item.Quit,      "おわる",           "QUIT"),
    };

    private static readonly (string h, string t)[] Ticker =
    {
        ("@rei_0w0", "ごめん、もう無理かも。"),
        ("@kako__",  "どうせ、とどかない"),
        ("@nobody_7","もういない"),
    };

    private int _sel;
    private bool _navHeld, _zHeld, _backHeld, _hasSave, _picking;
    private int _pick; // つづきから：選択中スロット(0..2)

    // 「はじめから」後の操作表示モード3択（毎回必ず通す）。
    private bool _choosingDisplay;
    private int _dispSel; // 0=キーボード / 1=コントローラ(PS) / 2=コントローラ(Xbox)
    private static readonly (Pad.DisplayMode mode, string jp, string en)[] DisplayChoices =
    {
        (Pad.DisplayMode.Keyboard,       "キーボード",          "KEYBOARD"),
        (Pad.DisplayMode.PadPlayStation, "コントローラ（PS）",  "GAMEPAD / PS"),
        (Pad.DisplayMode.PadXbox,        "コントローラ（Xbox）","GAMEPAD / XBOX"),
    };
    private double _t, _toastT;
    private string _toast = "";
    private bool _autoplay, _dived;

    // タイトルのキービジュアル＝画面全体を覆うフル16:9の1枚絵（_Ready で一度だけロードしてキャッシュ）。
    private Texture2D? _kvTex;

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _kvTex = ResourceLoader.Exists("res://char/title_kv.png")
            ? ResourceLoader.Load<Texture2D>("res://char/title_kv.png") : null;
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
        // 操作説明オーバーレイが開いている間はタイトル側の入力を止める（Z/Xの二重処理を防ぐ）。
        if (GetNodeOrNull<HowToPlay>("/root/HowTo") is { IsOpen: true }) { QueueRedraw(); return; }

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld;

        // 「はじめから」後の操作表示モード3択中：←→/↑↓で選び Z=決定（＝ゲーム開始）/ X=やめる。
        if (_choosingDisplay)
        {
            int n = DisplayChoices.Length;
            bool nu = Input.IsActionPressed("ui_up")   || Input.IsActionPressed("ui_left");
            bool nd = Input.IsActionPressed("ui_down") || Input.IsActionPressed("ui_right");
            if ((nu || nd) && !_navHeld)
            {
                if (nu) _dispSel = (_dispSel + n - 1) % n;
                if (nd) _dispSel = (_dispSel + 1) % n;
                Audio.Instance?.PlayUiMove();
            }
            _navHeld = nu || nd;
            if (zEdge)
            {
                Audio.Instance?.PlayUiConfirm();
                Pad.SetDisplayAndSave(DisplayChoices[_dispSel].mode); // 反映＋永続化
                _game.ResetPersistent();                              // はじめから＝まっさらスタート
                Go("res://Prologue.tscn");
            }
            else if (backEdge) { Audio.Instance?.PlayUiCancel(); _choosingDisplay = false; }
            _zHeld = z; _backHeld = back;
            QueueRedraw();
            return;
        }

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
                // はじめから＝まず操作表示モードを必ず選ばせる（決定でリセット＋プロローグへ）。
                // 既存の選択があればそれを初期カーソルに（無ければキーボード）。
                _choosingDisplay = true;
                _dispSel = Pad.Display == Pad.DisplayMode.Auto ? 0 : Pad.DisplayToInt(Pad.Display);
                break;
            case Item.Continue:
                if (_hasSave) { _picking = true; _pick = FirstSlot(); }
                else Toast("セーブデータがありません");
                break;
            case Item.HowToPlay:
                // 「あそびかた」＝いつでも引ける静的な操作説明オーバーレイ（シーン遷移しない）。
                GetNodeOrNull<HowToPlay>("/root/HowTo")?.Open();
                break;
            case Item.Tutorial:
                // 「チュートリアル」＝独立ステージ0（完全チュートリアル）を再生（既読フラグは変えない）。
                // DiffSelect を通らないので難易度は Easy に固定（直前の選択を引き継がせない）。
                _game.Difficulty = GameManager.Diff.Easy;
                Go("res://Stage0.tscn");
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

        // ── 呼吸の位相（KV全体のごく僅かな呼吸的拡縮＆パララックス。やり過ぎない）──
        float breath = Mathf.Sin(t * Mathf.Pi * 2f / 6.0f);          // 周期6s（ゆったり）
        float kvScale = 1f + (0.5f + 0.5f * breath) * 0.018f;        // 1.000〜1.018 のごく僅かな脈
        float kvDx = breath * 4f;                                     // 水平のわずかな漂い(px)

        // ── 全画面キービジュアル（フル16:9・最背面に画面全面へ）。アスペクト維持でカバー＝はみ出しはトリム ──
        if (_kvTex != null)
        {
            float tw = _kvTex.GetWidth(), th = _kvTex.GetHeight();
            // 「カバー」：画面を必ず覆うスケール。呼吸でさらに僅かに拡大。
            float cover = Mathf.Max(W / tw, H / th) * kvScale;
            float dw = tw * cover, dh = th * cover;
            float dx = (W - dw) / 2f + kvDx;                          // 中央寄せ＋呼吸の漂い
            float dy = (H - dh) / 2f;
            DrawTextureRect(_kvTex, new Rect2(dx, dy, dw, dh), false);
        }
        else
        {
            // フォールバック：KVが無い時だけ夜グラデで埋める（黒画面回避）。
            UiKit.VGradient(this, new Rect2(0, 0, W, H),
                new[] { new Color("0e1834"), new Color("0a1126"), new Color("070a16") },
                new[] { 0f, 0.55f, 1f });
        }

        // ── 可読性スクリム（KVの上・UIの下）──
        // 左を暗くする横グラデ（左=半透明ダーク→右=透明）。タイトル文字とメニューのコントラストを保証。
        HGradient(new Rect2(0, 0, W * 0.62f, H),
            new Color(6 / 255f, 9 / 255f, 20 / 255f, 0.74f), new Color(6 / 255f, 9 / 255f, 20 / 255f, 0f));
        // 下端の薄いスクリム（プロンプト・バージョン表記の足元を沈める）。
        UiKit.VGradient(this, new Rect2(0, H - 150f, W, 150f),
            new[] { new Color(6 / 255f, 9 / 255f, 20 / 255f, 0f), new Color(6 / 255f, 9 / 255f, 20 / 255f, 0.55f) },
            new[] { 0f, 1f });
        // 上端の薄いスクリム（ティッカー帯の可読性）。
        UiKit.VGradient(this, new Rect2(0, 0, W, 90f),
            new[] { new Color(6 / 255f, 9 / 255f, 20 / 255f, 0.45f), new Color(6 / 255f, 9 / 255f, 20 / 255f, 0f) },
            new[] { 0f, 1f });

        // ── 漂う光の弾（KVに溶け込む空気・脇役）──
        float kegPulse = 0.5f + 0.5f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.Pi * 2f / 2.4f));
        DrawDanmaku(new Vector2(W * 0.60f, H * 0.34f), 4f, new Color(150 / 255f, 200 / 255f, 1f, kegPulse));

        // ── スキャンライン（控えめ・画面の質感を統一）──
        for (float y = 0; y < H; y += 6f)
            DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.07f));

        DrawTitleBlock();
        DrawMenu();
        DrawPrompt();

        // ── バージョン（右下）──
        UiKit.Text(this, UiKit.Mono, new Vector2(W - 230, H - 48), "ver 0.3.0 — 体験版", 11, UiKit.Text4, HorizontalAlignment.Right, 204);
        UiKit.Text(this, UiKit.Mono, new Vector2(W - 230, H - 30), "© 2026 algo project", 11, UiKit.Text4, HorizontalAlignment.Right, 204);

        DrawTicker();
        DrawToast();
        if (_picking) DrawSlotPicker();
        if (_choosingDisplay) DrawDisplayPicker();
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

    // 「はじめから」後の操作表示モード3択ダイアログ（キーボード / コントローラPS / コントローラXbox）。
    // ここで選んだ表記でゲーム中のヒントを統一する（入力自体はどのデバイスも常に有効）。
    private void DrawDisplayPicker()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.6f)); // 暗幕
        int n = DisplayChoices.Length;
        float w = 600, rowH = 60, h = 132 + n * rowH, x = (W - w) / 2f, y = (H - h) / 2f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 16f, new Color(UiKit.Purify, 0.7f), 1.4f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + 24), "操作表示を選ぶ", 19, UiKit.White, HorizontalAlignment.Center, w);
        UiKit.Text(this, UiKit.Zen, new Vector2(x, y + 50), "ヒントの表記を統一します（入力はどれでも使えます）", 12,
            UiKit.Text3, HorizontalAlignment.Center, w);
        float top = y + 80;
        for (int i = 0; i < n; i++)
        {
            float ry = top + i * rowH;
            bool on = i == _dispSel;
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 28, ry, w - 56, 50), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 10f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 44, ry + 16), "▸", 16, UiKit.Purify);
            }
            Color nameCol = on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 70, ry + 13), DisplayChoices[i].jp, 20, nameCol);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 230, ry + 18), DisplayChoices[i].en, 12,
                on ? UiKit.Info : UiKit.Text4, HorizontalAlignment.Right, 200);
        }
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 30), "↑↓ / ←→ えらぶ    Z はじめる    X もどる", 11,
            UiKit.Text3, HorizontalAlignment.Center, w);
    }

    // 横リニアグラデ矩形（左→右に色を補間）。立ち絵の硬い矩形エッジを夜へ溶かすのに使う。
    private void HGradient(Rect2 r, Color left, Color right)
    {
        var g = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { left, right } };
        var tex = new GradientTexture2D
        {
            Gradient = g, Width = 256, Height = 8,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0, 0), FillTo = new Vector2(1, 0),
        };
        DrawTextureRect(tex, r, false);
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
