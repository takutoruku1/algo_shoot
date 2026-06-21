using Godot;

// PauseMenu : 全画面共通のポーズメニュー（オートロード /root/PauseMenu）。
//   Esc で開き、ツリーをポーズして「スロット1..3にセーブ / つづける / タイトルへ」を表示する。
//   セーブは手動・スロット制（自動セーブは廃止）＝ここでしか保存されない。
//   ゲームプレイ画面でだけ開く（タイトル/設定/カットシーンは除外＝Esc衝突を避ける）。
//   ゲームプレイ画面では右下に「Esc メニュー」ヒントを常時表示する。
//   --qa / --demo では無効（自動プレイのポーズ事故を防ぐ）。
public partial class PauseMenu : CanvasLayer
{
    private GameManager _game = null!;
    private PauseCanvas _canvas = null!;
    private bool _open;
    private int _sel;
    private bool _navHeld, _lrHeld, _zHeld, _escHeld;
    private double _savedToast;
    private int _savedSlot;
    private bool _autoplay;

    // ───────── 行モデル ─────────
    // 先頭に音量スライダー3行（←→で調整）、続いてアクション5行（Zで決定）。
    // 設定シーンへ遷移するとステージが消えるため、ポーズ中の音量はここでインライン調整する。
    public static readonly (string Key, string Label)[] VolRows =
    {
        ("master", "マスター音量"),
        ("bgm",    "BGM"),
        ("se",     "効果音 (SE)"),
    };
    // アクション：0..2 = スロット1..3 にセーブ / 3 = つづける / 4 = あそびかた / 5 = タイトルへ
    public static readonly string[] ItemsJp = { "スロット1にセーブ", "スロット2にセーブ", "スロット3にセーブ", "つづける", "あそびかた", "タイトルへ" };

    // 行構成：音量3行 → 操作表示1行（←→/Zで切替）→ アクション6行。
    private static int RowCount => VolRows.Length + 1 + ItemsJp.Length;
    private bool IsVolRow(int sel) => sel < VolRows.Length;
    private bool IsDisplayRow(int sel) => sel == VolRows.Length;       // 音量行のすぐ下
    private int ActionIndex(int sel) => sel - VolRows.Length - 1;      // 操作表示行の下＝アクション
    public static int DisplayRowGlobalIndex => VolRows.Length;         // 描画用：操作表示行のグローバル行番号

    // 操作表示モードの循環（Auto は含めず KB→PS→Xbox の3値を回す）。
    private static readonly Pad.DisplayMode[] DispCycle =
        { Pad.DisplayMode.Keyboard, Pad.DisplayMode.PadPlayStation, Pad.DisplayMode.PadXbox };

    public static string DisplayLabel(Pad.DisplayMode m) => m switch
    {
        Pad.DisplayMode.Keyboard       => "キーボード",
        Pad.DisplayMode.PadPlayStation => "PlayStation",
        Pad.DisplayMode.PadXbox        => "Xbox",
        _                              => "自動",
    };
    public string DisplayValue => DisplayLabel(Pad.Display);

    // ←→/Z で操作表示モードを循環し、即反映＋保存（Pad 側がファイルへマージ書き込み）。
    private void CycleDisplay(int dir)
    {
        int idx = System.Array.IndexOf(DispCycle, Pad.Display);
        idx = idx < 0 ? 0 : (idx + dir + DispCycle.Length) % DispCycle.Length; // Auto は KB から
        Pad.SetDisplayAndSave(DispCycle[idx]);
        Audio.Instance?.PlayUiMove();
    }

    // 表示用にキャッシュした音量（0..100）。Open 時に保存値から読む。
    private readonly float[] _vol = new float[VolRows.Length];

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // ポーズ中も動く
        Layer = 100;                          // 最前面
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
        _canvas = new PauseCanvas { Menu = this };
        AddChild(_canvas);
    }

    // ゲームプレイ画面でのみ開く/ヒントを出す。タイトル/設定/カットシーンは除外。
    private bool CanOpenHere()
    {
        string path = GetTree().CurrentScene?.SceneFilePath ?? "";
        if (string.IsNullOrEmpty(path)) return false;
        return !(path.Contains("TitleMenu") || path.Contains("Settings")
              || path.Contains("Prologue") || path.Contains("Final") || path.Contains("Epilogue"));
    }

    public override void _Process(double delta)
    {
        if (_autoplay) return;
        if (_savedToast > 0) _savedToast -= delta;
        // 操作説明オーバーレイが上に開いている間は、ポーズメニュー側の入力を止める（Esc/Z の二重処理を防ぐ）。
        if (GetNodeOrNull<HowToPlay>("/root/HowTo") is { IsOpen: true }) { _canvas.QueueRedraw(); return; }

        // Esc（キーボード）／Start（パッド）どちらでも開閉できる。
        bool esc = Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.Start);
        bool escEdge = esc && !_escHeld; _escHeld = esc;

        if (!_open)
        {
            if (escEdge && CanOpenHere()) Open();
            _canvas.QueueRedraw(); // 常時ヒントの更新
            return;
        }

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            if (up) _sel = (_sel + RowCount - 1) % RowCount;
            if (down) _sel = (_sel + 1) % RowCount;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;

        // ←→：音量行のときだけ ±5 調整＝即バス反映＋保存（SEは鳴らして耳で確認）。
        bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
        if ((left || right) && !_lrHeld)
        {
            if (IsVolRow(_sel))
            {
                _vol[_sel] = Mathf.Clamp(_vol[_sel] + (right ? 5f : -5f), 0f, 100f);
                AudioConfig.Set(VolRows[_sel].Key, _vol[_sel]);
                Audio.Instance?.PlayUiMove();
            }
            else if (IsDisplayRow(_sel)) CycleDisplay(right ? 1 : -1);
        }
        _lrHeld = left || right;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        // 音量行で Z＝ミュート/復帰のトグル（0 ⇄ 既定相当）。アクション行は従来どおり決定。
        if (zEdge && IsVolRow(_sel))
        {
            _vol[_sel] = _vol[_sel] > 0.5f ? 0f : 80f;
            AudioConfig.Set(VolRows[_sel].Key, _vol[_sel]);
            Audio.Instance?.PlayUiConfirm();
        }
        else if (zEdge && IsDisplayRow(_sel)) CycleDisplay(1); // Z でも前へ循環
        else if (zEdge) { Audio.Instance?.PlayUiConfirm(); Choose(ActionIndex(_sel)); }
        else if (escEdge) { Audio.Instance?.PlayUiCancel(); Close(); } // Esc でも閉じる（＝つづける）

        _canvas.QueueRedraw();
    }

    private void Open()
    {
        Audio.Instance?.PlayUiCancel(); // ポーズ＝開く合図（柔らかい下降）
        _open = true; _sel = 0;
        _navHeld = false; _lrHeld = false; _zHeld = false;
        for (int i = 0; i < VolRows.Length; i++) _vol[i] = AudioConfig.Get(VolRows[i].Key); // 保存値を読む
        GetTree().Paused = true;
        _canvas.QueueRedraw();
    }

    private void Close()
    {
        _open = false;
        GetTree().Paused = false;
        _canvas.QueueRedraw();
    }

    private void Choose(int action)
    {
        if (action < GameManager.SlotCount)
        {
            _game?.SaveToSlot(action + 1);       // スロットへ保存（上書き）
            _savedSlot = action + 1; _savedToast = 1.8;
        }
        else if (action == GameManager.SlotCount) Close();           // つづける
        else if (action == GameManager.SlotCount + 1)
        {
            // あそびかた：ポーズを保ったまま操作説明オーバーレイを重ねる（閉じたらポーズへ戻る）。
            GetNodeOrNull<HowToPlay>("/root/HowTo")?.Open();
        }
        else { _game?.AutoSave(); Close(); GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); } // タイトルへ（離脱時オートセーブ）
    }

    public bool IsOpen => _open;
    public int Sel => _sel;
    public bool ShowHint => !_open && !_autoplay && CanOpenHere();
    public bool SlotFilled(int slot) => _game?.SlotExists(slot) ?? false;
    public string SavedText => _savedToast > 0 ? $"スロット{_savedSlot}にセーブしました" : "";
    // 描画用：音量行の現在値（0..100）。
    public float VolValue(int i) => i >= 0 && i < _vol.Length ? _vol[i] : 0f;
}

// ポーズメニュー＆ヒントの描画（CanvasLayer の子。設計座標 1280x720）。
public partial class PauseCanvas : Node2D
{
    public PauseMenu Menu = null!;

    public override void _Ready() { ProcessMode = ProcessModeEnum.Always; }

    public override void _Draw()
    {
        if (Menu == null) return;
        if (Menu.IsOpen) { UiKit.BeginDesign(this); DrawPauseMenu(); UiKit.EndDesign(this); }
        else if (Menu.ShowHint) { UiKit.BeginDesign(this); DrawHint(); UiKit.EndDesign(this); }
    }

    private void DrawPauseMenu()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.62f)); // 暗幕

        int nVol = PauseMenu.VolRows.Length;
        float w = 460, h = 600, x = (W - w) / 2f, y = (H - h) / 2f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 18f, new Color(UiKit.Purify, 0.6f), 1.4f);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 28, y + 22), "MENU", 13, UiKit.Info);
        DrawRect(new Rect2(x + 28, y + 48, w - 56, 1f), new Color(1, 1, 1, 0.1f));

        // ── 音量セクション（←→ で調整／Z でミュート切替）──
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 28, y + 62), "音量  VOLUME", 10, UiKit.Text4);
        float volTop = y + 80, rowH = 38;
        for (int i = 0; i < nVol; i++)
        {
            float ry = volTop + i * rowH;
            bool on = i == Menu.Sel;
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 22, ry, w - 44, 34), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 9f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 36, ry + 9), "▸", 15, UiKit.Purify);
            }
            UiKit.Text(this, on ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(x + 58, ry + 7), PauseMenu.VolRows[i].Label, 15,
                on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f));
            // バー（トラック＋塗り）＋数値
            float v = Menu.VolValue(i);
            float barW = 132f, barX = x + w - barW - 64f, barY = ry + 14f, barH = 6f;
            DrawRect(new Rect2(barX, barY, barW, barH), new Color(1, 1, 1, 0.12f));
            DrawRect(new Rect2(barX, barY, barW * v / 100f, barH), new Color(UiKit.Info, on ? 0.95f : 0.7f));
            UiKit.Text(this, UiKit.Mono, new Vector2(barX + barW + 8f, ry + 9), Mathf.RoundToInt(v).ToString(), 12,
                on ? UiKit.White : UiKit.Text3, HorizontalAlignment.Right, 40);
        }

        // ── 操作表示モード（←→/Z で キーボード / PlayStation / Xbox）──
        float dispLabelY = volTop + nVol * rowH + 8f;
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 28, dispLabelY), "操作表示  BUTTONS", 10, UiKit.Text4);
        float dispRowY = dispLabelY + 18f;
        {
            bool on = Menu.Sel == PauseMenu.DisplayRowGlobalIndex;
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 22, dispRowY, w - 44, 34), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 9f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 36, dispRowY + 9), "▸", 15, UiKit.Purify);
            }
            UiKit.Text(this, on ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(x + 58, dispRowY + 7), "ボタン表記", 15,
                on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + w - 64 - 200, dispRowY + 7), "◂ " + Menu.DisplayValue + " ▸", 14,
                on ? UiKit.White : UiKit.Text3, HorizontalAlignment.Right, 200);
        }

        float divY = dispRowY + 34f + 8f;
        DrawRect(new Rect2(x + 28, divY, w - 56, 1f), new Color(1, 1, 1, 0.08f));

        // ── アクション（Z で決定）──
        float top = divY + 16f; rowH = 40f;
        for (int i = 0; i < PauseMenu.ItemsJp.Length; i++)
        {
            float ry = top + i * rowH;
            bool on = (nVol + 1 + i) == Menu.Sel; // +1 は操作表示行ぶん（これが無いと表示が1行上にずれ、表示行とセーブ1が同時選択に見える）
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 22, ry, w - 44, 36), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 10f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 36, ry + 9), "▸", 16, UiKit.Purify);
            }
            UiKit.Text(this, on ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(x + 58, ry + 7), PauseMenu.ItemsJp[i], 17,
                on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f));
            // セーブスロット行は状態（空き/保存済み）を右に出す
            if (i < GameManager.SlotCount)
            {
                bool filled = Menu.SlotFilled(i + 1);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 130, ry + 11), filled ? "保存済み" : "空き", 11,
                    filled ? UiKit.Info : UiKit.Text4, HorizontalAlignment.Right, 108);
            }
        }

        if (Menu.SavedText.Length > 0)
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + h - 54), Menu.SavedText, 14, UiKit.PurifyHi, HorizontalAlignment.Center, w);
        string footer = $"←→ 音量・表示    {Pad.ConfirmToken} 決定    {Pad.PauseToken} 閉じる";
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 30), footer, 11, UiKit.Text3, HorizontalAlignment.Center, w);
    }

    // 「Esc メニュー」ヒント（画面右下・ティッカーの上）。常時表示。
    private void DrawHint()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float y = H - 38f - 30f;
        const string label = "メニュー";
        string keyTok = Pad.PauseToken; // 表示モードに追従（Esc / MENU / OPTIONS）
        float keyW = Mathf.Max(28f, UiKit.TextW(UiKit.Mono, keyTok, 12) + 14f);
        float labelW = UiKit.TextW(UiKit.ZenBold, label, 13);
        float x = W - 24f - (keyW + 8f + labelW);
        UiKit.Key(this, new Vector2(x, y), keyTok, new Color(1, 1, 1, 0.06f), new Color(UiKit.Info, 0.4f), UiKit.Info);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + keyW + 8f, y + 4f), label, 13, UiKit.Text2);
    }
}
