using Godot;

// PauseMenu : 全画面共通のポーズメニュー（オートロード /root/PauseMenu）。
//   Esc で開き、ツリーをポーズして「スロット1..3にセーブ / つづける / さいしょからやりなおす / タイトルへ」等を表示する。
//   セーブは手動・スロット制（自動セーブは廃止）＝ここでしか保存されない。
//   ゲームプレイ画面でだけ開く（タイトル/設定/カットシーン/Hub/ショップ/難易度選択/記録は除外＝Esc衝突を避ける）。
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
    // 「ハブへもどる」の2段階確認。1回目のZ/クリックで true になり、同じ行をもう一度
    // 選ぶまで実行しない（誤爆防止）。選択行を離れる／メニューを閉じるとリセットする。
    private bool _hubConfirm;

    // ───────── 行モデル ─────────
    // 先頭に音量スライダー3行（←→で調整）、続いてアクション行（Zで決定）。
    // 設定シーンへ遷移するとステージが消えるため、ポーズ中の音量はここでインライン調整する。
    public static readonly (string Key, string Label)[] VolRows =
    {
        ("master", "マスター音量"),
        ("bgm",    "BGM"),
        ("se",     "効果音 (SE)"),
    };
    // アクション：0..2 = スロット1..3 にセーブ / 3 = つづける / 4 = さいしょからやりなおす /
    //             5 = 会話ログ / 6 = あそびかた / 7 = ハブへもどる / 8 = タイトルへ
    // 「さいしょからやりなおす」は R 即発リトライの置き換え先（誤爆防止）＝パッドの正式なリトライ導線。
    // 「ハブへもどる」も同じくステージ中のみ有効＝難易度を変えたいだけの離脱に、タイトル経由の
    //   数画面戻りを強いない導線（誤爆防止に2段階Z確認、Choose() 参照）。
    public static readonly string[] ItemsJp =
        { "スロット1にセーブ", "スロット2にセーブ", "スロット3にセーブ", "つづける", "さいしょからやりなおす", "会話ログ", "あそびかた", "ハブへもどる", "タイトルへ" };
    // 描画用：リトライ行のアクション index（ステージ外ではグレーアウトする）。
    public const int RetryAction = GameManager.SlotCount + 1;
    // 描画用：ハブへもどる行のアクション index（ステージ外ではグレーアウトする。2段階確認あり）。
    public const int HubReturnAction = GameManager.SlotCount + 4;

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

    // Hub/ショップ/難易度選択/記録＝各画面が独自の「戻る」導線を持つ非戦闘画面。
    // ここに Esc の戦闘用ポーズが割り込むと各画面固有の戻るより先に発火してしまうため除外する
    // （ShopTutorial は "Shop" の部分一致で自動的に含まれる）。RetryEnabled と揃えること。
    private static bool IsNonCombatMenuScreen(string path) =>
        path.Contains("Hub") || path.Contains("Shop") || path.Contains("DiffSelect") || path.Contains("Records");

    // ゲームプレイ画面でのみ開く/ヒントを出す。タイトル/設定/カットシーン/Hub系メニュー画面は除外。
    private bool CanOpenHere()
    {
        string path = GetTree().CurrentScene?.SceneFilePath ?? "";
        if (string.IsNullOrEmpty(path)) return false;
        return !(path.Contains("TitleMenu") || path.Contains("Settings") || path.Contains("Credits")
              || path.Contains("Prologue") || path.Contains("Final") || path.Contains("Epilogue")
              || path.Contains("Training") // トレーニングは試用のみ＝スロットセーブ導線を出さない（本番状態を汚さない）
              || IsNonCombatMenuScreen(path));
    }

    // マウスホイールは押下状態を持たない＝イベントでしか来ない。Pad は static ヘルパでノードではなく
    // _Input を持てないため、全画面で常駐するここ（PauseMenu）が拾って Pad の当該フレーム蓄積へ流し込む。
    // 蓄積は次フレームの Pad.PollMouse でフレーム値へ確定され、各画面が Pad.WheelDelta() で読む。
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)   Pad.FeedWheel(+1f);
            else if (mb.ButtonIndex == MouseButton.WheelDown) Pad.FeedWheel(-1f);
        }
    }

    public override void _Process(double delta)
    {
        // 直近デバイスの追跡（ボタン表記の動的 KB/パッド切替）。ゲーム中は Player も呼ぶが、
        // メニュー/ハブ/ショップ/カットシーン等の非戦闘画面は常駐のここが担う。
        Pad.PollDevice();
        // マウス座標・ボタンエッジの更新＋ホイール蓄積のフレーム確定（全画面で毎フレーム）。
        Pad.PollMouse(GetViewport());

        if (_autoplay) return;
        if (_savedToast > 0) _savedToast -= delta;
        // 操作説明・会話ログのオーバーレイが上に開いている間／閉じた直後フレーム(UiBlocked)は、
        // ポーズメニュー側の入力を止める（Esc/Z の二重処理でメニューまで連鎖して閉じるのを防ぐ）。
        // held は「既押し」扱いにして、同じ押下がエッジとして立たないよう食っておく。
        bool overlayOpen = GetNodeOrNull<HowToPlay>("/root/HowTo") is { IsOpen: true }
                        || GetNodeOrNull<Backlog>("/root/Backlog") is { IsOpen: true };
        if (overlayOpen || Pad.UiBlocked(this))
        {
            _escHeld = _zHeld = _navHeld = _lrHeld = true;
            _canvas.QueueRedraw();
            return;
        }
        // 開いている間は下の画面（ショップ/ハブ/ステージ等）への入力を食う
        // ＝「閉じる Esc/Start/Z」の同じ押下が、下の画面の もどる/決定 として二重処理されない。
        if (_open) Pad.ConsumeUi(this);

        // Esc（キーボード）／Start（パッド）どちらでも開閉できる。
        bool esc = Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.Start);
        bool escEdge = esc && !_escHeld; _escHeld = esc;

        if (!_open)
        {
            if (escEdge && CanOpenHere()) Open();
            _canvas.QueueRedraw(); // 常時ヒントの更新
            return;
        }

        // 「ハブへもどる」の2段階確認：選択行を離れたら（矢印/マウス移動/クリック）リセットする。
        int selBefore = _sel;

        // マウス：ポーズが開いている間だけホットスポットを登録する（＝下の画面はツリーポーズで停止中＝
        // 唯一の登録者。閉じている時は BeginHotspots を呼ばない＝下の画面のクリック判定に混線しない）。
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < RowCount; i++) UiKit.Hotspot(RowRect(i), i);
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= 0 && hov != _sel) { _sel = hov; Audio.Instance?.PlayUiMove(); }
        bool click = Pad.MouseClick();
        int clk = UiKit.ClickedId(click);

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

        // マウスクリック：クリック先の行種別で処理する（音量＝バー位置で値設定／操作表示＝循環／アクション＝決定）。
        if (clk >= 0)
        {
            _sel = clk;
            if (IsVolRow(clk))
            {
                // バーのクリック位置(X)で音量を直接設定（5刻みスナップ＝KB操作と粒度を揃える）。
                var (barX, barW) = VolBarMetrics(clk);
                float ratio = Mathf.Clamp((Pad.MousePos().X - barX) / barW, 0f, 1f);
                _vol[clk] = Mathf.RoundToInt(ratio * 20f) * 5f;
                AudioConfig.Set(VolRows[clk].Key, _vol[clk]);
                Audio.Instance?.PlayUiMove();
            }
            else if (IsDisplayRow(clk)) CycleDisplay(1);
            else
            {
                int act = ActionIndex(clk);
                bool wasArmed = _hubConfirm;
                if (Choose(act))
                {
                    // 「ハブへもどる」1段階目（確認状態へ入っただけ）は Shop.cs の振り直しフローに
                    // 揃え、確定音ではなく移動音を鳴らす＝2段階目（実行）とはっきり区別する。
                    if (act == HubReturnAction && !wasArmed) Audio.Instance?.PlayUiMove();
                    else Audio.Instance?.PlayUiConfirm();
                }
                else Audio.Instance?.PlayUiDeny();
            }
        }
        // 音量行で Z＝ミュート/復帰のトグル（0 ⇄ 既定相当）。アクション行は従来どおり決定。
        else if (zEdge && IsVolRow(_sel))
        {
            _vol[_sel] = _vol[_sel] > 0.5f ? 0f : 80f;
            AudioConfig.Set(VolRows[_sel].Key, _vol[_sel]);
            Audio.Instance?.PlayUiConfirm();
        }
        else if (zEdge && IsDisplayRow(_sel)) CycleDisplay(1); // Z でも前へ循環
        else if (zEdge)
        {
            // 選べない行（ステージ外の「さいしょからやりなおす」／「ハブへもどる」）は拒否音で応える。
            int act = ActionIndex(_sel);
            bool wasArmed = _hubConfirm;
            if (Choose(act))
            {
                if (act == HubReturnAction && !wasArmed) Audio.Instance?.PlayUiMove(); // 1段階目＝移動音
                else Audio.Instance?.PlayUiConfirm();
            }
            else Audio.Instance?.PlayUiDeny();
        }
        else if (escEdge) { Audio.Instance?.PlayUiCancel(); Close(); } // Esc でも閉じる（＝つづける）
        else if (Pad.MouseRightClick()) { Audio.Instance?.PlayUiCancel(); Close(); } // 右クリック＝閉じる（つづける）

        if (_sel != selBefore) _hubConfirm = false; // 別の行へ移ったら確認状態を解除（誤爆防止）

        _canvas.QueueRedraw();
    }

    // ── マウス用ジオメトリ（PauseCanvas.DrawPauseMenu と同一式。ホットスポット計算に共用）──
    //   ダイアログ box: w=460, h=728, x=(W-w)/2, y=(H-h)/2。
    private static (float x, float y, float w) BoxMetrics()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float w = 460, h = 728, x = (W - w) / 2f, y = (H - h) / 2f;
        return (x, y, w);
    }

    // グローバル行 index i の当たり矩形（音量3行 → 操作表示1行 → アクション8行）。
    public static Rect2 RowRect(int i)
    {
        var (x, y, w) = BoxMetrics();
        int nVol = VolRows.Length;
        float volTop = y + 80, volRowH = 38;
        if (i < nVol) return new Rect2(x + 22, volTop + i * volRowH, w - 44, 34);
        float dispLabelY = volTop + nVol * volRowH + 8f;
        float dispRowY = dispLabelY + 18f;
        if (i == nVol) return new Rect2(x + 22, dispRowY, w - 44, 34); // 操作表示行
        float divY = dispRowY + 34f + 8f;
        float top = divY + 16f, actRowH = 40f;
        int ai = i - nVol - 1;
        return new Rect2(x + 22, top + ai * actRowH, w - 44, 36);
    }

    // 音量行のバー（トラック）矩形の X 起点と幅（DrawPauseMenu と同一算出）。
    private static (float barX, float barW) VolBarMetrics(int volIdx)
    {
        var (x, y, w) = BoxMetrics();
        float barW = 132f, barX = x + w - barW - 64f;
        return (barX, barW);
    }

    private void Open()
    {
        Audio.Instance?.PlayUiCancel(); // ポーズ＝開く合図（柔らかい下降）
        _open = true; _sel = 0; _hubConfirm = false;
        _navHeld = false; _lrHeld = false; _zHeld = false;
        for (int i = 0; i < VolRows.Length; i++) _vol[i] = AudioConfig.Get(VolRows[i].Key); // 保存値を読む
        GetTree().Paused = true;
        Pad.ConsumeUi(this); // 開いたフレームから下の画面への入力を食う
        _canvas.QueueRedraw();
    }

    private void Close()
    {
        _open = false;
        _hubConfirm = false;
        GetTree().Paused = false;
        _canvas.QueueRedraw();
    }

    // 戻り値：実行できたか（false＝選べない行を押した→呼び元が拒否音を鳴らす）。
    private bool Choose(int action)
    {
        if (action < GameManager.SlotCount)
        {
            _game?.SaveToSlot(action + 1);       // スロットへ保存（上書き）
            _savedSlot = action + 1; _savedToast = 1.8;
        }
        else if (action == GameManager.SlotCount) Close();           // つづける
        else if (action == RetryAction)
        {
            // さいしょからやりなおす：ステージ中のみ。R 即発リトライの置き換え先（誤爆防止）で、
            // パッド（Start=このメニュー）からの正式なリトライ導線でもある。
            if (!RetryEnabled) return false;
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Close();
            GetTree().ReloadCurrentScene();
        }
        else if (action == GameManager.SlotCount + 2)
        {
            // 会話ログ：ポーズを保ったままバックログ・オーバーレイを重ねる（閉じたらポーズへ戻る）。
            GetNodeOrNull<Backlog>("/root/Backlog")?.Open();
        }
        else if (action == GameManager.SlotCount + 3)
        {
            // あそびかた：ポーズを保ったまま操作説明オーバーレイを重ねる（閉じたらポーズへ戻る）。
            GetNodeOrNull<HowToPlay>("/root/HowTo")?.Open();
        }
        else if (action == HubReturnAction)
        {
            // ハブへもどる：ステージ中のみ。誤爆防止に2段階Z確認（1回目で確認状態へ、2回目で実行）。
            // 実処理は HandleGameOverExit 後半（AutoSave→DespawnAll→Hub.tscn）と同型＝稼いだ心も保存される。
            if (!RetryEnabled) return false;
            if (!_hubConfirm) { _hubConfirm = true; return true; } // 1段階目：確認状態へ（まだ戻らない）
            _hubConfirm = false;
            _game?.AutoSave();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Close();
            GetTree().ChangeSceneToFile("res://Hub.tscn");
        }
        else { _game?.AutoSave(); Close(); GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); } // タイトルへ（離脱時オートセーブ）
        return true;
    }

    // 「さいしょからやりなおす」が意味を持つ画面か＝ステージ系のみ。
    // Hub/ショップ/難易度選択/記録ではシーン再読込に意味がないためグレーアウトする
    // （CanOpenHere() が既にこれらの画面を除外しているため実質ここには来ないが、
    //   将来 CanOpenHere() 側だけ緩んだ場合の保険として残す＝IsNonCombatMenuScreen で揃える）。
    public bool RetryEnabled
    {
        get
        {
            string path = GetTree().CurrentScene?.SceneFilePath ?? "";
            if (string.IsNullOrEmpty(path) || !CanOpenHere()) return false;
            return !IsNonCombatMenuScreen(path);
        }
    }

    // 上に重なるオーバーレイ（会話ログ/操作説明）が Esc で閉じた直後、その同じ押下を
    // こちらの開閉エッジとして拾わないための通知。押しっぱなし扱いにして1回ぶん吸収する
    //（オーバーレイ表示中は上の早期 return で _escHeld が更新されないため、放置すると
    //   閉じた次のフレームに Esc エッジが立ち、ポーズが勝手に開く/閉じる）。
    public void NoteOverlayClosed() => _escHeld = true;

    public bool IsOpen => _open;
    public int Sel => _sel;
    public bool HubConfirmPending => _hubConfirm;
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
        float w = 460, h = 728, x = (W - w) / 2f, y = (H - h) / 2f; // h はアクション9行（ハブへもどる追加）ぶん
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 18f, new Color(UiKit.Purify, 0.6f), 1.4f);
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + 28, y + 22), "MENU", UiKit.Info);
        DrawRect(new Rect2(x + 28, y + 48, w - 56, 1f), new Color(1, 1, 1, 0.1f));

        // ── 音量セクション（←→ で調整／Z でミュート切替）──
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 28, y + 62), "音量  VOLUME", UiKit.FontSmall, UiKit.Text4);
        float volTop = y + 80, rowH = 38;
        for (int i = 0; i < nVol; i++)
        {
            float ry = volTop + i * rowH;
            bool on = i == Menu.Sel;
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 22, ry, w - 44, 34), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 9f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 36, ry + 9), "▸", UiKit.FontBody, UiKit.Purify);
            }
            UiKit.Text(this, on ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(x + 58, ry + 7), PauseMenu.VolRows[i].Label, UiKit.FontBody,
                on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f));
            // バー（トラック＋塗り）＋数値
            float v = Menu.VolValue(i);
            float barW = 132f, barX = x + w - barW - 64f, barY = ry + 14f, barH = 6f;
            DrawRect(new Rect2(barX, barY, barW, barH), new Color(1, 1, 1, 0.12f));
            DrawRect(new Rect2(barX, barY, barW * v / 100f, barH), new Color(UiKit.Info, on ? 0.95f : 0.7f));
            UiKit.Text(this, UiKit.Mono, new Vector2(barX + barW + 8f, ry + 9), Mathf.RoundToInt(v).ToString(), UiKit.FontLabel,
                on ? UiKit.White : UiKit.Text3, HorizontalAlignment.Right, 40);
        }

        // ── 操作表示モード（←→/Z で キーボード / PlayStation / Xbox）──
        float dispLabelY = volTop + nVol * rowH + 8f;
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 28, dispLabelY), "操作表示  BUTTONS", UiKit.FontSmall, UiKit.Text4);
        float dispRowY = dispLabelY + 18f;
        {
            bool on = Menu.Sel == PauseMenu.DisplayRowGlobalIndex;
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 22, dispRowY, w - 44, 34), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 9f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 36, dispRowY + 9), "▸", UiKit.FontBody, UiKit.Purify);
            }
            UiKit.Text(this, on ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(x + 58, dispRowY + 7), "ボタン表記", UiKit.FontBody,
                on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + w - 64 - 200, dispRowY + 7), "◂ " + Menu.DisplayValue + " ▸", UiKit.FontLabel,
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
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 36, ry + 9), "▸", UiKit.FontBody, UiKit.Purify);
            }
            // 「さいしょからやりなおす」「ハブへもどる」はステージ外では選べない＝グレーアウト＋理由を右に出す。
            bool retryDim = i == PauseMenu.RetryAction && !Menu.RetryEnabled;
            bool hubDim = i == PauseMenu.HubReturnAction && !Menu.RetryEnabled;
            bool hubConfirming = i == PauseMenu.HubReturnAction && !hubDim && Menu.HubConfirmPending;
            string label = hubConfirming ? "ほんとうに もどる？" : PauseMenu.ItemsJp[i];
            Color labelCol = hubConfirming ? UiKit.Burn
                : (retryDim || hubDim) ? UiKit.Text4
                : (on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f));
            UiKit.Text(this, on ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(x + 58, ry + 7), label, UiKit.FontBody, labelCol);
            // セーブスロット行は状態（空き/保存済み）を右に出す
            if (i < GameManager.SlotCount)
            {
                bool filled = Menu.SlotFilled(i + 1);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 130, ry + 11), filled ? "保存済み" : "空き", UiKit.FontSmall,
                    filled ? UiKit.Info : UiKit.Text4, HorizontalAlignment.Right, 108);
            }
            else if (retryDim || hubDim)
            {
                UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 158, ry + 11), "ステージ中のみ", UiKit.FontSmall,
                    UiKit.Text4, HorizontalAlignment.Right, 136);
            }
            else if (hubConfirming)
            {
                UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 158, ry + 11), "もう一度で確定", UiKit.FontSmall,
                    UiKit.Burn, HorizontalAlignment.Right, 136);
            }
        }

        if (Menu.SavedText.Length > 0)
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + h - 54), Menu.SavedText, UiKit.FontLabel, UiKit.PurifyHi, HorizontalAlignment.Center, w);
        string footer = $"←→ 音量・表示    {Pad.ConfirmToken} 決定    {Pad.PauseToken} 閉じる";
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 30), footer, UiKit.FontSmall, UiKit.Text3, HorizontalAlignment.Center, w);
    }

    // 「Esc メニュー」ヒント（画面右下・ティッカーの上）。常時表示。
    private void DrawHint()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float y = H - 38f - 30f;
        const string label = "メニュー";
        string keyTok = Pad.PauseToken; // 表示モードに追従（Esc / MENU / OPTIONS）
        float keyW = Mathf.Max(28f, UiKit.TextW(UiKit.Mono, keyTok, 12) + 14f);
        float labelW = UiKit.TextW(UiKit.ZenBold, label, UiKit.FontLabel);
        float x = W - 24f - (keyW + 8f + labelW);
        UiKit.Key(this, new Vector2(x, y), keyTok, new Color(1, 1, 1, 0.06f), new Color(UiKit.Info, 0.4f), UiKit.Info);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + keyW + 8f, y + 4f), label, UiKit.FontLabel, UiKit.Text2);
    }
}
