using Godot;

// HowToPlay : いつでも開ける「あそびかた／操作説明」オーバーレイ（オートロード /root/HowTo）。
//   タイトルの「あそびかた」とポーズメニューの「あそびかた」から Open() で呼ぶ。
//   ツリーがポーズ中(GetTree().Paused=true)でも描けるよう ProcessMode=Always・最前面 Layer で常駐し、
//   開いている間だけ入力を奪って描画する（閉じると元の画面/ポーズへそのまま戻る＝シーン遷移しない）。
//
//   ・3ページのページ送り：←→ または LB/RB でページ切替。X / B / Esc で閉じる。
//   ・キー表記は Pad.UsingPad / Pad.Face で KB / パッドを出し分ける（操作表示モードに自動追従）。
//     生文字でキーを書かず Tok* ヘルパに集約＝チュートリアル(StageRei)や HUD と同じ表記で自動一致する。
public partial class HowToPlay : CanvasLayer
{
    private HowToCanvas _canvas = null!;
    private bool _open;
    private int _page;            // 0..2
    private bool _lrHeld, _backHeld;
    private bool _autoplay;

    // 閉じたときに呼ぶコールバック（任意）。ポーズから開いた場合などに使う。
    private System.Action? _onClose;

    public const int PageCount = 3;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // ポーズ中も動く
        Layer = 110;                          // ポーズメニュー(100)よりさらに前面
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
        _canvas = new HowToCanvas { Menu = this };
        AddChild(_canvas);
    }

    // 操作説明を開く。onClose は閉じたときに1度だけ呼ばれる（ポーズ復帰などに使う）。
    public void Open(System.Action? onClose = null)
    {
        if (_autoplay) { onClose?.Invoke(); return; }
        _open = true; _page = 0;
        _lrHeld = false; _backHeld = false;
        _onClose = onClose;
        Audio.Instance?.PlayUiConfirm();
        _canvas.QueueRedraw();
    }

    private void Close()
    {
        _open = false;
        Audio.Instance?.PlayUiCancel();
        // Esc で閉じたとき、同じ押下を PauseMenu が開閉エッジとして拾わないよう通知（Backlog と同作法）。
        GetNodeOrNull<PauseMenu>("/root/PauseMenu")?.NoteOverlayClosed();
        _canvas.QueueRedraw();
        var cb = _onClose; _onClose = null;
        cb?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!_open) return;

        // 開いている間＝下の画面（タイトル/ポーズ/ステージ）への入力を食う
        //（閉じた Esc/X の同じ押下が下で二重処理されないための門・Pad.UiBlocked）。
        Pad.ConsumeUi(this);

        // ←→ / LB・RB でページ送り。
        bool left  = Input.IsActionPressed("ui_left")  || Pad.Pressed(JoyButton.LeftShoulder);
        bool right = Input.IsActionPressed("ui_right") || Pad.Pressed(JoyButton.RightShoulder);
        if ((left || right) && !_lrHeld)
        {
            if (left)  _page = (_page + PageCount - 1) % PageCount;
            if (right) _page = (_page + 1) % PageCount;
            Audio.Instance?.PlayUiMove();
        }
        _lrHeld = left || right;

        // マウスホイールでもページ送り（他画面と同じ WheelDelta 規約：上(+)＝前ページ／下(−)＝次ページ）。
        float wheel = Pad.WheelDelta();
        if (wheel > 0f) { _page = (_page + PageCount - 1) % PageCount; Audio.Instance?.PlayUiMove(); }
        else if (wheel < 0f) { _page = (_page + 1) % PageCount; Audio.Instance?.PlayUiMove(); }

        // X / B / Esc / 右クリックで閉じる。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B)
                    || Pad.MouseRightClick();
        if (back && !_backHeld) Close();
        _backHeld = back;

        _canvas.QueueRedraw();
    }

    public bool IsOpen => _open;
    public int Page => _page;
}

// 操作説明オーバーレイの描画（CanvasLayer の子。設計座標 1280×720）。
public partial class HowToCanvas : Node2D
{
    public HowToPlay Menu = null!;

    public override void _Ready() { ProcessMode = ProcessModeEnum.Always; }

    // ── 操作子トークン（KB / パッド出し分け。Hud と同じ規則）──
    // Hud.Tok* は private なのでここで同等に持つ（割り当ては Player.cs と一致）。
    private static string TokMove  => Pad.UsingPad ? "L スティック"                   : "矢印 / WASD";
    private static string TokShot  => "オート";                                                       // ショットはボタン不要（常時自動発射）
    private static string TokFocus => Pad.UsingPad ? Pad.Face(JoyButton.LeftShoulder)  : "Shift";       // RB は向き反転へ移した
    private static string TokFlip  => Pad.UsingPad ? Pad.Face(JoyButton.RightShoulder) : "F / 左クリック"; // 向き反転：Player.cs RightShoulder / 左クリック
    private static string TokDodge => Pad.UsingPad ? "L3"                             : "Alt";        // 回避ダッシュ：Player.cs LeftStick
    private static string TokBomb  => Pad.UsingPad ? Pad.Face(JoyButton.X)            : "X";
    private static string TokMode  => Pad.UsingPad ? Pad.Face(JoyButton.B)            : "V";          // ショット切替：Player.cs JoyButton.B
    private static string TokKind  => Pad.UsingPad ? Pad.Face(JoyButton.RightStick)   : "Ctrl";       // やさしさ全開：Player.cs RightStick
    private static string TokMenu  => Pad.UsingPad ? Pad.Face(JoyButton.Start)        : "Esc"; // PS=OPTIONS / Xbox=MENU

    public override void _Draw()
    {
        if (Menu == null || !Menu.IsOpen) return;
        UiKit.BeginDesign(this);
        DrawScreen();
        UiKit.EndDesign(this);
    }

    private void DrawScreen()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        // 暗幕（下に何があっても読めるよう濃いめ）。
        DrawRect(new Rect2(0, 0, W, H), new Color(4 / 255f, 6 / 255f, 14 / 255f, 0.9f));

        float pad = 64f;
        float x = pad, y = 48f, w = W - pad * 2f, h = H - 96f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.05f, 0.05f, 0.10f, 0.98f), 18f, new Color(UiKit.Purify, 0.6f), 1.4f);

        // ── ヘッダ（タイトル＋ページインジケータ）──
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 32, y + 22), "HOW TO PLAY", UiKit.FontSmall, UiKit.Info);
        string[] titles = { "操作", "画面の見かた", "コア機能" };
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 32, y + 38), "あそびかた — " + titles[Menu.Page], UiKit.FontTitle, UiKit.White);
        // ページドット（右上）
        for (int i = 0; i < HowToPlay.PageCount; i++)
        {
            var dc = new Vector2(x + w - 32 - (HowToPlay.PageCount - 1 - i) * 22f, y + 40f);
            DrawCircle(dc, 5f, i == Menu.Page ? UiKit.PurifyHi : new Color(1, 1, 1, 0.2f));
        }
        DrawRect(new Rect2(x + 32, y + 74, w - 64, 1f), new Color(1, 1, 1, 0.1f));

        float bodyY = y + 92;
        switch (Menu.Page)
        {
            case 0: DrawPageControls(x + 32, bodyY, w - 64); break;
            case 1: DrawPageHud(x + 32, bodyY, w - 64); break;
            case 2: DrawPageCore(x + 32, bodyY, w - 64); break;
        }

        // ── フッタ（操作ヒント）──
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 32),
            (Pad.UsingPad ? "LB / RB" : "←→") + " ページ    " + TokMenu + " とじる", UiKit.FontSmall,
            UiKit.Text3, HorizontalAlignment.Center, w);
    }

    // ───────── ページ1：操作（2列）─────────
    private void DrawPageControls(float x, float y, float w)
    {
        // (token, 名前, 説明, accent, 強調?)
        var rows = new (string tok, string name, string desc, Color accent, bool hot)[]
        {
            (TokMove,  "移動",        "上下左右に動く",                              UiKit.Info,   false),
            (TokShot,  "撃つ",        "自動で撃ちます。光を放って心を浄化する",          UiKit.Purify, false),
            (TokFocus, "低速移動",    "ゆっくり精密に動く。当たり判定が見やすい",        UiKit.Info,   false),
            (TokDodge, "回避ダッシュ","一瞬無敵で弾をすり抜ける。攻めの切り札",          UiKit.Gold,   true),
            (TokFlip,  "向き反転",    "押すたび撃つ方向が 右⇔左 に切り替わる",           UiKit.Gold,   true),
            (TokBomb,  "ボム",        "画面の弾を消し短時間無敵。残数ぶん",             UiKit.Mina,   false),
            (TokMode,  "ショット切替","連射↔拡散↔ホーミング↔加速球（解放後）",           UiKit.Gold,   true),
            (TokKind,  "やさしさ全開","満タンで発動、5秒ひかりが溢れる",               UiKit.PurifyHi,false),
            (TokMenu,  "メニュー",    "セーブ・音量・つづける",                       UiKit.Text2,  false),
        };

        float colW = (w - 24f) / 2f, rowH = 60f;
        int half = (rows.Length + 1) / 2;
        for (int i = 0; i < rows.Length; i++)
        {
            int col = i / half, idx = i % half;
            float rx = x + col * (colW + 24f);
            float ry = y + idx * rowH;
            DrawControlRow(rx, ry, colW, rows[i].tok, rows[i].name, rows[i].desc, rows[i].accent, rows[i].hot);
        }

        // 念押し：やさしさ全開は Space ではなく TokKind（KB のとき Ctrl）。
        // あわせて弾幕パートのマウス操作も1行で（マウスは専用トークンを持たないので地の文で示す）。
        if (!Pad.UsingPad)
        {
            float ny = y + half * rowH + 6f;
            UiKit.Text(this, UiKit.Zen, new Vector2(x, ny),
                "※ 光は自動で出ます。撃つボタンはありません（やさしさ全開だけ Ctrl）", UiKit.FontLabel, UiKit.Gold);
            UiKit.Text(this, UiKit.Zen, new Vector2(x, ny + 22f),
                "◆ マウスでも遊べます — カーソルへ移動／左クリック 向き反転／右クリック 回避／中クリック ボム／ホイール ショット切替（低速は Shift）",
                UiKit.FontLabel, UiKit.Info, HorizontalAlignment.Left, w);
        }
        // 会話中の2択（ChoiceOverlay）は KB/パッド共通の操作なので出し分けなしで1行案内。
        {
            float ny2 = y + half * rowH + (Pad.UsingPad ? 6f : 50f);
            UiKit.Text(this, UiKit.Zen, new Vector2(x, ny2),
                "◇ 会話中の2択：↑↓ / マウスで選ぶ、" + Pad.ConfirmToken + " で決定", UiKit.FontLabel, UiKit.PurifyHi,
                HorizontalAlignment.Left, w);
        }
    }

    private void DrawControlRow(float x, float y, float w, string tok, string name, string desc, Color accent, bool hot)
    {
        float h = 52f;
        if (hot) UiKit.Box(this, new Rect2(x, y, w, h), new Color(accent, 0.10f), 10f, new Color(accent, 0.55f), 1.2f);
        // キーバッジ（可変幅）
        float badgeW = KeyBadge(new Vector2(x + 8, y + 6), tok, accent);
        float tx = x + 8 + badgeW + 14f;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, y + 6), name, UiKit.FontBody, hot ? new Color(accent, 1f) : UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(tx, y + 28), desc, UiKit.FontLabel, UiKit.Text2, HorizontalAlignment.Left, w - (tx - x) - 8);
        if (hot)
        {
            float sw = UiKit.TextW(UiKit.Mono, "★", UiKit.FontSmall);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - sw - 8, y + 6), "★", UiKit.FontSmall, accent);
        }
    }

    // 可変幅キーバッジ（やや大きめ／HowTo 用。高さ22）。Hud.KeyBadge と同じ意匠。
    private float KeyBadge(Vector2 p, string token, Color accent)
    {
        float w = UiKit.TextW(UiKit.Mono, token, UiKit.FontLabel) + 16, h = 28;
        UiKit.Box(this, new Rect2(p.X, p.Y, w, h), new Color(0.10f, 0.09f, 0.16f, 0.95f), 6f, new Color(accent, 0.8f), 1.2f);
        UiKit.Text(this, UiKit.Mono, new Vector2(p.X, p.Y + 6), token, UiKit.FontLabel, new Color(accent, 1f), HorizontalAlignment.Center, w);
        return w;
    }

    // ───────── ページ2：画面の見かた（凡例・各1行）─────────
    private void DrawPageHud(float x, float y, float w)
    {
        // (icon種別, 名前, 説明, 色)。icon: 0=ハート 1=色チップ円 2=ゲージ片
        var rows = new (int icon, string name, string desc, Color col)[]
        {
            (0, "LIFE",        "残りの体力。弾に当たると1つ減る",                      UiKit.Hp),
            (1, "BOMB",        "ボムの残り。" + TokBomb + " で画面の弾を消せる",        UiKit.Mina),
            (2, "やさしさ",    "かすり・浄化で貯まる。満タンで " + TokKind + " → 5秒だけ弾を祓いやすく", UiKit.Gold),
            (2, "浄化 ％",     "ステージの進み具合。100%でボスへ",                     UiKit.Purify),
            (1, "救った人 ◯/3","物語の到達度。3人すべて救うのが目標",                  UiKit.PurifyHi),
            (2, "汚染",        "物語が進むほど濁る。高いとやさしさが貯まりにくくなる",     UiKit.Kegare),
            (1, "コンボ",      "連続で浄化するとSCOREと浄化した心が倍増。猶予内に次を倒せないと途切れる", UiKit.Mina),
            (1, "SCORE",       "遊びの得点。ハイスコアを狙える",                       UiKit.Gold),
            (0, "浄化した心",  "通貨。ショップ（ハブで " + TokBomb + "）でミナを強化できる", UiKit.Hp),
            (1, "フォロワー",  "届けた証。増えるほど全弾ダメージが微増（上限+50%）とインプレに上乗せ", UiKit.Info),
            (1, "TIME",        "クリアタイム。記録に挑戦",                            UiKit.Text2),
        };
        float rowH = 40f;
        for (int i = 0; i < rows.Length; i++)
        {
            float ry = y + i * rowH;
            DrawIcon(new Vector2(x + 14, ry + 12), rows[i].icon, rows[i].col);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 40, ry + 4), rows[i].name, UiKit.FontBody, UiKit.White);
            UiKit.Text(this, UiKit.Zen, new Vector2(x + 240, ry + 5), rows[i].desc, UiKit.FontLabel, UiKit.Text2);
        }
    }

    private void DrawIcon(Vector2 c, int kind, Color col)
    {
        switch (kind)
        {
            case 0: UiKit.Heart(this, c, 9f, col); break;
            case 1: DrawCircle(c, 7f, col); break;
            default: // ゲージ片
                UiKit.Box(this, new Rect2(c.X - 9, c.Y - 4, 18, 8), new Color(col, 0.85f), 4f);
                break;
        }
    }

    // ───────── ページ3：コア機能（図解カード）─────────
    private void DrawPageCore(float x, float y, float w)
    {
        var cards = new (string title, string body, Color accent)[]
        {
            ("やさしさ全開",
             "かすって・浄化して貯める→満タンで " + TokKind + "。攻めるほど早く貯まり、5秒だけ連射最速＆近くの弾が花びらに。守りには使えない＝攻めの見返り。",
             UiKit.PurifyHi),
            ("ボム",
             "ピンチの保険。" + TokBomb + " で画面の弾を消し無敵に。残数は限られる。",
             UiKit.Mina),
            ("弾強化",
             "ハブで " + TokBomb + " →ショップ。「浄化した心」で 連射 / 拡散 / ホーミング / 加速球（タメて撃つロケット弾） を解放・強化。",
             UiKit.Gold),
            ("後方弾",
             "後方(-X)に敵がいると自動で弱いホーミング弾を撃つ、無操作の保険。ショップの 後方威力/後方速射/後方追尾 で強化できる。",
             UiKit.Info),
            ("浄化と汚染",
             "敵を浄化＝救うこと。汚染は物語が進むほど自然に上がる演出で、高いとやさしさが鈍る。澄んだ心I/IIで上昇をゆるやかにできる。",
             UiKit.Kegare),
        };
        float cardH = 96f, gap = 14f;
        for (int i = 0; i < cards.Length; i++)
        {
            float ry = y + i * (cardH + gap);
            UiKit.Box(this, new Rect2(x, ry, w, cardH), new Color(cards[i].accent, 0.08f), 12f, new Color(cards[i].accent, 0.5f), 1.2f);
            // 左の色帯
            UiKit.Box(this, new Rect2(x, ry, 5f, cardH), cards[i].accent, 3f);
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 22, ry + 14), "[" + cards[i].title + "]", UiKit.FontSpeaker, new Color(cards[i].accent, 1f));
            UiKit.Multi(this, UiKit.Zen, new Vector2(x + 22, ry + 44), cards[i].body, UiKit.FontLabel, UiKit.Text2, w - 44);
        }
    }
}
