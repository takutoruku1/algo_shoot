using Godot;

// PauseMenu : 全画面共通のポーズメニュー（オートロード /root/PauseMenu）。
//   Esc で開き、ツリーをポーズして**二段構成**のメニューを出す（2026-09-07。旧版は13行を一度に並べていた）。
//     一段目: つづける ／ このステージ ▸ ／ 設定とデータ ▸
//     二段目: このステージ＝さいしょからやりなおす・会話ログ・ハブへもどる
//             設定とデータ＝音量3・ボタン表記・あそびかた・スロット1..3にセーブ・タイトルへ
//   二段目からは X／右クリック／Esc で一段目へ戻り、Esc は一段目でだけ閉じる。
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
    private bool _navHeld, _lrHeld, _zHeld, _escHeld, _backHeld;
    private int _pageFrom;   // 二段目へ入るとき一段目のどの行から来たか（戻り先）
    private double _savedToast;
    private int _savedSlot;
    private bool _autoplay;
    // 「ハブへもどる」の2段階確認。1回目のZ/クリックで true になり、同じ行をもう一度
    // 選ぶまで実行しない（誤爆防止）。選択行を離れる／メニューを閉じるとリセットする。
    private bool _hubConfirm;

    // ───────── 行モデル（2026-09-07: 二段構成へ）─────────
    // ユーザー実機指摘「メニューの情報量が多いから、三つぐらいから選択したら次のメニューが開ける
    // ような感じにして」。旧版は音量3行＋操作表示1行＋アクション9行＝13行が一度に並んでいた。
    //
    // 一段目は3つだけ:
    //   つづける         … 即閉じる（いちばん多い用事なので、二段目を挟まず1操作で終わる）
    //   このステージ     … いま遊んでいる面に対する操作（やりなおす／会話ログ／ハブへもどる）
    //   設定とデータ     … ラン外の話（音量・ボタン表記・あそびかた・セーブ・タイトルへ）
    // 分類の根拠: 「今すぐ戻る」「この面をどうするか」「ゲーム全体の設定と出入り」の3つが、
    //   実際の項目を並べたときに自然に割れる境目だった（セーブはランを跨ぐ恒久データなので設定側）。
    //
    // 二段目からは X／右クリック／Esc で一段目へ戻る。Esc は一段目でだけメニューを閉じる。
    public enum Page { Top, Stage, Config }
    private Page _page = Page.Top;

    // 一段目の3行。Act は「即実行するか、どの二段目を開くか」。
    public static readonly string[] TopRows = { "つづける", "このステージ", "設定とデータ" };

    // 設定とデータ（二段目）の先頭に置く音量スライダー3行（←→で調整）。
    // 設定シーンへ遷移するとステージが消えるため、ポーズ中の音量はここでインライン調整する。
    public static readonly (string Key, string Label)[] VolRows =
    {
        ("master", "マスター音量"),
        ("bgm",    "BGM"),
        ("se",     "効果音 (SE)"),
    };

    // このステージ（二段目）のアクション。
    //   「さいしょからやりなおす」は R 即発リトライの置き換え先（誤爆防止）＝パッドの正式なリトライ導線。
    //   「ハブへもどる」も同じくステージ中のみ有効＝難易度を変えたいだけの離脱に、タイトル経由の
    //   数画面戻りを強いない導線（誤爆防止に2段階Z確認、Choose() 参照）。
    public static readonly string[] StageRows = { "さいしょからやりなおす", "会話ログ", "ハブへもどる" };
    public const int StageRetry = 0, StageBacklog = 1, StageHub = 2;

    // 設定とデータ（二段目）の、音量3行＋操作表示1行より下のアクション。
    public static readonly string[] ConfigRows =
        { "あそびかた", "スロット1にセーブ", "スロット2にセーブ", "スロット3にセーブ", "タイトルへ" };
    public const int CfgHowTo = 0, CfgSlot1 = 1, CfgTitle = 4;

    // ページごとの行数。設定は 音量3 + 操作表示1 + ConfigRows。
    private int RowCount => _page switch
    {
        Page.Top => TopRows.Length,
        Page.Stage => StageRows.Length,
        _ => VolRows.Length + 1 + ConfigRows.Length,
    };
    // 設定ページの中だけ、先頭が音量／その次が操作表示／以降がアクション。
    private bool IsVolRow(int sel) => _page == Page.Config && sel < VolRows.Length;
    private bool IsDisplayRow(int sel) => _page == Page.Config && sel == VolRows.Length;
    private int ConfigActionIndex(int sel) => sel - VolRows.Length - 1;
    public static int DisplayRowGlobalIndex => VolRows.Length;         // 描画用：操作表示行のグローバル行番号
    public Page CurrentPage => _page;

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
        for (int i = 0; i < RowCount; i++) UiKit.Hotspot(RowRect(_page, i), i);
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

        // もどる（二段目→一段目）。X／パッドB＝他画面の「もどる」と同じ割り当て。
        bool back = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;

        // マウスクリック：クリック先の行種別で処理する（音量＝バー位置で値設定／操作表示＝循環／アクション＝決定）。
        if (clk >= 0)
        {
            _sel = clk;
            if (IsVolRow(clk))
            {
                // バーのクリック位置(X)で音量を直接設定（5刻みスナップ＝KB操作と粒度を揃える）。
                var (barX, barW) = VolBarMetrics();
                float ratio = Mathf.Clamp((Pad.MousePos().X - barX) / barW, 0f, 1f);
                _vol[clk] = Mathf.RoundToInt(ratio * 20f) * 5f;
                AudioConfig.Set(VolRows[clk].Key, _vol[clk]);
                Audio.Instance?.PlayUiMove();
            }
            else if (IsDisplayRow(clk)) CycleDisplay(1);
            else Activate(clk);
        }
        // 音量行で Z＝ミュート/復帰のトグル（0 ⇄ 既定相当）。アクション行は従来どおり決定。
        else if (zEdge && IsVolRow(_sel))
        {
            _vol[_sel] = _vol[_sel] > 0.5f ? 0f : 80f;
            AudioConfig.Set(VolRows[_sel].Key, _vol[_sel]);
            Audio.Instance?.PlayUiConfirm();
        }
        else if (zEdge && IsDisplayRow(_sel)) CycleDisplay(1); // Z でも前へ循環
        else if (zEdge) Activate(_sel);
        // 戻る／閉じる：二段目では一段目へ戻り、一段目でだけ閉じる（＝つづける）。
        //   X も受ける＝他の画面（Hub/Shop/DiffSelect）の「もどる」と同じ指の動きにする。
        else if (escEdge || backEdge || Pad.MouseRightClick())
        {
            Audio.Instance?.PlayUiCancel();
            if (_page == Page.Top) Close();
            else { _page = Page.Top; _sel = _pageFrom; _hubConfirm = false; }
        }

        if (_sel != selBefore) _hubConfirm = false; // 別の行へ移ったら確認状態を解除（誤爆防止）

        _canvas.QueueRedraw();
    }

    // ── マウス用ジオメトリ（PauseCanvas.DrawPauseMenu と同一式。ホットスポット計算に共用）──
    //   ダイアログ box: w=460、高さはページごと（一段目は3行しかないので低く、設定は全部入るぶん高い）。
    //   画面中央に置く（x/y は幅高から算出）。
    public static (float x, float y, float w, float h) BoxMetrics(Page p)
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float w = 460;
        float h = p switch
        {
            Page.Top => 268,     // 見出し＋3行＋フッタ
            Page.Stage => 268,   // 同上（3行）
            _ => 560,            // 設定＝音量3 + 操作表示1 + アクション5
        };
        float x = (W - w) / 2f, y = (H - h) / 2f;
        return (x, y, w, h);
    }

    // ページ内の行 index i の当たり矩形。
    //   一段目／このステージ: 見出しの下からアクション行が並ぶだけ。
    //   設定とデータ: 音量3行 → 操作表示1行 → アクション行、の従来の積み方。
    public static Rect2 RowRect(Page p, int i)
    {
        var (x, y, w, _) = BoxMetrics(p);
        if (p != Page.Config)
        {
            float top0 = y + 78f, rowH0 = 44f;
            return new Rect2(x + 22, top0 + i * rowH0, w - 44, 36);
        }
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

    // 音量行のバー（トラック）矩形の X 起点と幅（DrawPauseMenu と同一算出）。設定ページ専用。
    private static (float barX, float barW) VolBarMetrics()
    {
        var (x, _, w, _) = BoxMetrics(Page.Config);
        float barW = 132f, barX = x + w - barW - 64f;
        return (barX, barW);
    }

    private void Open()
    {
        Audio.Instance?.PlayUiCancel(); // ポーズ＝開く合図（柔らかい下降）
        _open = true; _sel = 0; _hubConfirm = false;
        _page = Page.Top;   // 開くたび一段目から（前回どこを見ていたかは引きずらない）
        _navHeld = false; _lrHeld = false; _zHeld = false; _backHeld = true;   // back は開幕の押下を食う
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

    // 行を決定する。一段目は「即実行 or 二段目を開く」、二段目は各アクション。
    // 音が3種類あるのは意味が違うから: 移動音＝まだ何も起きていない（ページ遷移／確認の1段階目）、
    // 確定音＝実行した、拒否音＝この画面では選べない行。
    private void Activate(int sel)
    {
        _sel = sel;
        if (_page == Page.Top)
        {
            switch (sel)
            {
                case 0: Audio.Instance?.PlayUiConfirm(); Close(); return;          // つづける
                case 1: OpenPage(Page.Stage); return;                               // このステージ
                default: OpenPage(Page.Config); return;                             // 設定とデータ
            }
        }

        if (_page == Page.Stage)
        {
            switch (sel)
            {
                case StageRetry:
                    // さいしょからやりなおす：ステージ中のみ。R 即発リトライの置き換え先（誤爆防止）で、
                    // パッド（Start=このメニュー）からの正式なリトライ導線でもある。
                    if (!RetryEnabled) { Audio.Instance?.PlayUiDeny(); return; }
                    Audio.Instance?.PlayUiConfirm();
                    GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                    Close();
                    GetTree().ReloadCurrentScene();
                    return;
                case StageBacklog:
                    // 会話ログ：ポーズを保ったままバックログ・オーバーレイを重ねる（閉じたらポーズへ戻る）。
                    Audio.Instance?.PlayUiConfirm();
                    GetNodeOrNull<Backlog>("/root/Backlog")?.Open();
                    return;
                default:
                    // ハブへもどる：ステージ中のみ。誤爆防止に2段階Z確認（1回目で確認状態へ、2回目で実行）。
                    // 実処理は GameManager の抜け処理と同型（AutoSave→DespawnAll→Hub.tscn）＝稼いだ心も保存される。
                    if (!RetryEnabled) { Audio.Instance?.PlayUiDeny(); return; }
                    if (!_hubConfirm) { _hubConfirm = true; Audio.Instance?.PlayUiMove(); return; }
                    _hubConfirm = false;
                    Audio.Instance?.PlayUiConfirm();
                    _game?.AutoSave();
                    GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                    Close();
                    GetTree().ChangeSceneToFile("res://Hub.tscn");
                    return;
            }
        }

        // 設定とデータ（音量行・操作表示行は呼び出し側で処理済み＝ここはその下のアクションのみ）。
        int act = ConfigActionIndex(sel);
        if (act == CfgHowTo)
        {
            // あそびかた：ポーズを保ったまま操作説明オーバーレイを重ねる（閉じたらポーズへ戻る）。
            Audio.Instance?.PlayUiConfirm();
            GetNodeOrNull<HowToPlay>("/root/HowTo")?.Open();
        }
        else if (act >= CfgSlot1 && act < CfgSlot1 + GameManager.SlotCount)
        {
            int slot = act - CfgSlot1 + 1;
            Audio.Instance?.PlayUiConfirm();
            _game?.SaveToSlot(slot);             // スロットへ保存（上書き）
            _savedSlot = slot; _savedToast = 1.8;
        }
        else
        {
            Audio.Instance?.PlayUiConfirm();
            _game?.AutoSave(); Close(); GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); // タイトルへ（離脱時オートセーブ）
        }
    }

    // 二段目を開く。戻り先（一段目のどの行から来たか）を覚えてカーソルを先頭へ。
    private void OpenPage(Page p)
    {
        Audio.Instance?.PlayUiMove();   // まだ何も起きていない＝移動音（確定音は実行のときだけ）
        _pageFrom = _sel;
        _page = p;
        _sel = 0;
        _hubConfirm = false;
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

    // 選択行のハイライト（枠＋▸）。3ページで同じ見え方にするためここに集約する。
    private void DrawRowCursor(Rect2 r)
    {
        UiKit.Box(this, r, new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 10f, new Color(UiKit.Purify, 0.45f), 1f);
        UiKit.Text(this, UiKit.Mono, new Vector2(r.Position.X + 14, r.Position.Y + 9), "▸", UiKit.FontBody, UiKit.Purify);
    }

    // 行のラベル（選択中は白＋太字）。
    private void DrawRowLabel(Rect2 r, string label, bool on, Color? col = null)
        => UiKit.Text(this, on ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(r.Position.X + 36, r.Position.Y + 7),
            label, UiKit.FontBody, col ?? (on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f)));

    private void DrawPauseMenu()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.62f)); // 暗幕

        var page = Menu.CurrentPage;
        var (x, y, w, h) = PauseMenu.BoxMetrics(page);
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 18f, new Color(UiKit.Purify, 0.6f), 1.4f);

        // 見出し。二段目は「MENU ▸ このステージ」のように親を残す＝いま二段目に居ると分かる。
        string head = page switch
        {
            PauseMenu.Page.Stage => "MENU ▸ このステージ",
            PauseMenu.Page.Config => "MENU ▸ 設定とデータ",
            _ => "MENU",
        };
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + 28, y + 22), head, UiKit.Info);
        DrawRect(new Rect2(x + 28, y + 48, w - 56, 1f), new Color(1, 1, 1, 0.1f));

        if (page == PauseMenu.Page.Top) DrawTopPage(x, w);
        else if (page == PauseMenu.Page.Stage) DrawStagePage(x, w);
        else DrawConfigPage(x, y, w);

        if (Menu.SavedText.Length > 0)
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + h - 54), Menu.SavedText, UiKit.FontLabel, UiKit.PurifyHi, HorizontalAlignment.Center, w);
        // フッタ：一段目は「閉じる」、二段目は「もどる」＝いまその操作が何をするかを言う。
        string footer = page == PauseMenu.Page.Top
            ? $"{Pad.ConfirmToken} 決定    {Pad.PauseToken} 閉じる"
            : $"{Pad.ConfirmToken} 決定    X もどる";
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 30), footer, UiKit.FontSmall, UiKit.Text3, HorizontalAlignment.Center, w);
    }

    // 一段目：3つの区分だけ。二段目を持つ行には「▸」を右端に添えて「まだ先がある」を示す。
    private void DrawTopPage(float x, float w)
    {
        for (int i = 0; i < PauseMenu.TopRows.Length; i++)
        {
            var r = PauseMenu.RowRect(PauseMenu.Page.Top, i);
            bool on = i == Menu.Sel;
            if (on) DrawRowCursor(r);
            DrawRowLabel(r, PauseMenu.TopRows[i], on);
            if (i > 0)   // 「つづける」以外は二段目へ入る
                UiKit.Text(this, UiKit.Mono, new Vector2(r.Position.X + r.Size.X - 30, r.Position.Y + 9), "▸",
                    UiKit.FontBody, on ? UiKit.Purify : UiKit.Text4);
        }
    }

    // 二段目「このステージ」：やりなおす／会話ログ／ハブへもどる。
    private void DrawStagePage(float x, float w)
    {
        for (int i = 0; i < PauseMenu.StageRows.Length; i++)
        {
            var r = PauseMenu.RowRect(PauseMenu.Page.Stage, i);
            bool on = i == Menu.Sel;
            if (on) DrawRowCursor(r);
            // 「さいしょからやりなおす」「ハブへもどる」はステージ外では選べない＝グレーアウト＋理由を右に出す。
            bool dim = (i == PauseMenu.StageRetry || i == PauseMenu.StageHub) && !Menu.RetryEnabled;
            bool confirming = i == PauseMenu.StageHub && !dim && Menu.HubConfirmPending;
            string label = confirming ? "ほんとうに もどる？" : PauseMenu.StageRows[i];
            Color col = confirming ? UiKit.Burn
                : dim ? UiKit.Text4
                : (on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f));
            DrawRowLabel(r, label, on, col);
            if (dim)
                UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 158, r.Position.Y + 11), "ステージ中のみ", UiKit.FontSmall,
                    UiKit.Text4, HorizontalAlignment.Right, 136);
            else if (confirming)
                UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 158, r.Position.Y + 11), "もう一度で確定", UiKit.FontSmall,
                    UiKit.Burn, HorizontalAlignment.Right, 136);
        }
    }

    // 二段目「設定とデータ」：音量3行（←→）→ ボタン表記（←→/Z）→ あそびかた／セーブ3／タイトルへ。
    private void DrawConfigPage(float x, float y, float w)
    {
        int nVol = PauseMenu.VolRows.Length;

        // ── 音量セクション（←→ で調整／Z でミュート切替）──
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 28, y + 62), "音量  VOLUME", UiKit.FontSmall, UiKit.Text4);
        for (int i = 0; i < nVol; i++)
        {
            var r = PauseMenu.RowRect(PauseMenu.Page.Config, i);
            bool on = i == Menu.Sel;
            if (on) DrawRowCursor(r);
            DrawRowLabel(r, PauseMenu.VolRows[i].Label, on);
            // バー（トラック＋塗り）＋数値
            float v = Menu.VolValue(i);
            float barW = 132f, barX = x + w - barW - 64f, barY = r.Position.Y + 14f, barH = 6f;
            DrawRect(new Rect2(barX, barY, barW, barH), new Color(1, 1, 1, 0.12f));
            DrawRect(new Rect2(barX, barY, barW * v / 100f, barH), new Color(UiKit.Info, on ? 0.95f : 0.7f));
            UiKit.Text(this, UiKit.Mono, new Vector2(barX + barW + 8f, r.Position.Y + 9), Mathf.RoundToInt(v).ToString(), UiKit.FontLabel,
                on ? UiKit.White : UiKit.Text3, HorizontalAlignment.Right, 40);
        }

        // ── 操作表示モード（←→/Z で キーボード / PlayStation / Xbox）──
        var dr = PauseMenu.RowRect(PauseMenu.Page.Config, nVol);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 28, dr.Position.Y - 18f), "操作表示  BUTTONS", UiKit.FontSmall, UiKit.Text4);
        {
            bool on = Menu.Sel == PauseMenu.DisplayRowGlobalIndex;
            if (on) DrawRowCursor(dr);
            DrawRowLabel(dr, "ボタン表記", on);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + w - 64 - 200, dr.Position.Y + 7), "◂ " + Menu.DisplayValue + " ▸", UiKit.FontLabel,
                on ? UiKit.White : UiKit.Text3, HorizontalAlignment.Right, 200);
        }

        // ── アクション（Z で決定）──
        for (int i = 0; i < PauseMenu.ConfigRows.Length; i++)
        {
            var r = PauseMenu.RowRect(PauseMenu.Page.Config, nVol + 1 + i);
            bool on = (nVol + 1 + i) == Menu.Sel;
            if (on) DrawRowCursor(r);
            DrawRowLabel(r, PauseMenu.ConfigRows[i], on);
            // セーブスロット行は状態（空き/保存済み）を右に出す
            if (i >= PauseMenu.CfgSlot1 && i < PauseMenu.CfgSlot1 + GameManager.SlotCount)
            {
                bool filled = Menu.SlotFilled(i - PauseMenu.CfgSlot1 + 1);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 130, r.Position.Y + 11), filled ? "保存済み" : "空き", UiKit.FontSmall,
                    filled ? UiKit.Info : UiKit.Text4, HorizontalAlignment.Right, 108);
            }
        }
    }

    // 「Esc メニュー」ヒント（画面右下・ティッカーの上）。常時表示。
    //   2026-09-07: プレイ中の常駐操作ガイド（Hud.DrawControls）を撤去した際、これ1つだけを残した。
    //   Esc（メニュー）の存在を知らせる唯一の手がかりなので消さない。ただし弾の視認を妨げないよう
    //   薄く小さく（キー枠の縁とラベルのαを落とし、ラベルは FontSmall へ）。
    private const float HintAlpha = 0.5f;
    private void DrawHint()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float y = H - 38f - 30f;
        const string label = "メニュー";
        string keyTok = Pad.PauseToken; // 表示モードに追従（Esc / MENU / OPTIONS）
        float keyW = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, keyTok, 11) + 12f);
        float labelW = UiKit.TextW(UiKit.ZenBold, label, UiKit.FontSmall);
        float x = W - 24f - (keyW + 7f + labelW);
        UiKit.Key(this, new Vector2(x, y), keyTok, new Color(1, 1, 1, 0.04f),
            new Color(UiKit.Info, 0.22f), new Color(UiKit.Info, HintAlpha));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + keyW + 7f, y + 5f), label, UiKit.FontSmall,
            new Color(UiKit.Text2, HintAlpha));
    }
}
