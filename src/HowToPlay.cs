using Godot;

// HowToPlay : いつでも開ける「あそびかた／操作説明」オーバーレイ（オートロード /root/HowTo）。
//   タイトルの「あそびかた」とポーズメニューの「あそびかた」から Open() で呼ぶ。
//   ツリーがポーズ中(GetTree().Paused=true)でも描けるよう ProcessMode=Always・最前面 Layer で常駐し、
//   開いている間だけ入力を奪って描画する（閉じると元の画面/ポーズへそのまま戻る＝シーン遷移しない）。
//
//   ・ページ送り：←→ または LB/RB。X / B / Esc で閉じる。
//   ・マウス：デバイスタブ直接クリックで切替、ページドットで直接ジャンプ、フッタ「とじる」で閉じる
//     （Records/Credits と同じ BackHintRect 作法。右クリック・ホイールは従来どおり）。
//   ・キー表記は Pad.UsingPad / Pad.Face で KB / パッドを出し分ける（操作表示モードに自動追従）。
//     生文字でキーを書かず Tok* ヘルパに集約＝チュートリアル(StageRei)や HUD と同じ表記で自動一致する。
//
//   ★2026-09-13：「対応ボタンが分かる画面」の要望に対し、新規画面を起こさずここを拡張した。
//     オーバーレイの器・タイトル/ポーズ双方の導線・未取得行の非表示・UiKit 意匠が既にここに揃っており、
//     新画面を足すとそれらを丸ごと二重に持つことになるため。先頭の「操作」ページを
//     キーボード／コントローラー／マウスの**3タブ（＝割り当て一覧表）**に割り、←→ の同じ軸で送る。
//     あそびかた本体（画面の見かた／コア機能）は後続ページにそのまま残る＝読み物と一覧表が同居する。
public partial class HowToPlay : CanvasLayer
{
    private HowToCanvas _canvas = null!;
    private bool _open;
    private int _page;            // 0..PageCount-1（0..2 が操作タブ／3=画面の見かた／4=コア機能）
    private bool _lrHeld, _backHeld;
    private bool _autoplay;

    // 閉じたときに呼ぶコールバック（任意）。ポーズから開いた場合などに使う。
    private System.Action? _onClose;

    // 操作の割り当て一覧タブ（先頭3ページ）。0=キーボード / 1=コントローラー / 2=マウス。
    public const int TabCount = 3;
    public const int PageCount = TabCount + 2;

    // ── マウス用ジオメトリ（HowToCanvas.DrawScreen と同一式）──
    //   パネル：pad=64, x=pad, y=48, w=W-128, h=H-96。以降の3つはそこからの相対。
    //   ホットスポット id：0..TabCount-1＝デバイスタブ／IdDotBase+i＝ページドット／IdClose＝フッタ「とじる」。
    public const int IdDotBase = 10, IdClose = 90;

    // デバイスタブの矩形（DrawDeviceTabs と同一式。操作ページを開いている時だけ意味を持つ）。
    public static Rect2 TabRect(int i)
    {
        float pad = 64f, x = pad, y = 48f, w = UiKit.DesignW - pad * 2f;
        float ix = x + 32f, iy = y + 92f, iw = w - 64f;
        float tw = (iw - 16f) / TabCount, th = 34f;
        return new Rect2(ix + i * (tw + 8f), iy, tw, th);
    }

    // ページドット（右上）の当たり矩形。描画は半径5の円だが、点を突くのは酷なので 22×22 の枠で取る。
    public static Rect2 DotRect(int i)
    {
        float pad = 64f, x = pad, y = 48f, w = UiKit.DesignW - pad * 2f;
        var c = new Vector2(x + w - 32f - (PageCount - 1 - i) * 22f, y + 40f);
        return new Rect2(c.X - 11f, c.Y - 11f, 22f, 22f);
    }

    // フッタの「とじる」矩形（Records.BackHintRect と同じ考え方＝キー表記＋ラベルの帯だけを取る）。
    //   フッタ行は中央寄せの1本の文字列なので、「とじる」の実描画位置を同じ式で再現して切り出す。
    public static Rect2 CloseHintRect()
    {
        float pad = 64f, x = pad, y = 48f, w = UiKit.DesignW - pad * 2f, h = UiKit.DesignH - 96f;
        string page = (Pad.UsingPad ? Pad.Face(JoyButton.LeftShoulder) + " / " + Pad.Face(JoyButton.RightShoulder) : "←→")
                      + HowToCanvas.FootGap;
        string close = HowToCanvas.FootCloseToken + " とじる";
        float pageW = UiKit.TextW(UiKit.Mono, page, UiKit.FontSmall);
        float closeW = UiKit.TextW(UiKit.Mono, close, UiKit.FontSmall);
        float lineX = x + (w - (pageW + closeW)) / 2f;   // 中央寄せ1行の左端
        float ty = y + h - 32f;
        return new Rect2(lineX + pageW - 6f, ty - 6f, closeW + 12f, UiKit.Mono.GetHeight(UiKit.FontSmall) + 12f);
    }

    // スクリーンショット用（--shot --howto N）：起動直後にこのページを開いたまま固定する。
    //   Shot オートロードはメニューを操作できないので、撮りたいページを引数で名指しできるようにする。
    //   --shot 無しでは一切効かない＝通常プレイ・配布ビルドには影響しない。
    private int _shotPage = -1;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // ポーズ中も動く
        Layer = 110;                          // ポーズメニュー(100)よりさらに前面
        var args = OS.GetCmdlineUserArgs();
        bool shot = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--demo" || args[i] == "--qa") _autoplay = true;
            else if (args[i] == "--shot") shot = true;
            else if (args[i] == "--howto" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) _shotPage = p;
        }
        if (!shot) _shotPage = -1;
        _canvas = new HowToCanvas { Menu = this };
        AddChild(_canvas);
        if (_shotPage >= 0)
        {
            _autoplay = false;   // 撮影のために開く（--qa と併用されても開けるように）
            _open = true;
            _page = Mathf.Clamp(_shotPage, 0, PageCount - 1);
        }
    }

    // 操作説明を開く。onClose は閉じたときに1度だけ呼ばれる（ポーズ復帰などに使う）。
    public void Open(System.Action? onClose = null)
    {
        if (_autoplay) { onClose?.Invoke(); return; }
        // 開いた時点で「いま握っているデバイス」のタブから見せる（探させない）。
        _open = true;
        _page = Pad.UsingMouse ? 2 : Pad.UsingPad ? 1 : 0;
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
        // 撮影固定中はページを動かさず閉じもしない（Shot が撮り終えて Quit するまで留める）。
        if (_shotPage >= 0) { Pad.ConsumeUi(this); _canvas.QueueRedraw(); return; }

        // 開いている間＝下の画面（タイトル/ポーズ/ステージ）への入力を食う
        //（閉じた Esc/X の同じ押下が下で二重処理されないための門・Pad.UiBlocked）。
        Pad.ConsumeUi(this);

        // マウス：タブ／ページドット／「とじる」を登録する。開いている間はツリーがポーズ済み or
        //   下の画面の入力を食っている＝このフレームの唯一の登録者（PauseMenu は overlayOpen で早期 return）。
        UiKit.BeginHotspots(Pad.MousePos());
        UiKit.Hotspot(CloseHintRect(), IdClose);
        for (int i = 0; i < PageCount; i++) UiKit.Hotspot(DotRect(i), IdDotBase + i);
        if (_page < TabCount) for (int i = 0; i < TabCount; i++) UiKit.Hotspot(TabRect(i), i);
        int clk = UiKit.ClickedId(Pad.MouseClick());
        if (clk == IdClose) { Close(); return; }
        if (clk >= IdDotBase) { SetPage(clk - IdDotBase); }
        else if (clk >= 0) SetPage(clk);

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

    // ページ移動（クリック経由）。同じページを押しても音は鳴らさない＝連打で耳が痛くならない。
    private void SetPage(int p)
    {
        p = Mathf.Clamp(p, 0, PageCount - 1);
        if (p == _page) return;
        _page = p;
        Audio.Instance?.PlayUiMove();
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
    // ※フッタなど「いま握っているデバイス向けの案内」で使う。割り当て一覧表（3タブ）は
    //   デバイス固定で書くので、下の ControlRows(tab) が直に文字列を持つ。
    private static string TokBomb  => Pad.UsingPad ? Pad.Face(JoyButton.X)            : "X";
    // メニュー（Esc / MENU / OPTIONS）の表記はフッタの「とじる」と共有＝下の FootCloseToken を使う。

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
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + 32, y + 22), "HOW TO PLAY", UiKit.Info);
        string[] titles = { "操作 — キーボード", "操作 — コントローラー", "操作 — マウス", "画面の見かた", "コア機能" };
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 32, y + 38), "あそびかた — " + titles[Menu.Page], UiKit.FontTitle, UiKit.White);
        // ページドット（右上）。クリックで直接ジャンプできるので、ホバー中は一回り大きく光らせる。
        int hov = UiKit.HoveredId();
        for (int i = 0; i < HowToPlay.PageCount; i++)
        {
            var dc = new Vector2(x + w - 32 - (HowToPlay.PageCount - 1 - i) * 22f, y + 40f);
            bool dh = hov == HowToPlay.IdDotBase + i;
            DrawCircle(dc, dh ? 7f : 5f, i == Menu.Page ? UiKit.PurifyHi : new Color(1, 1, 1, dh ? 0.55f : 0.2f));
        }
        DrawRect(new Rect2(x + 32, y + 74, w - 64, 1f), new Color(1, 1, 1, 0.1f));

        float bodyY = y + 92;
        if (Menu.Page < HowToPlay.TabCount)
        {
            // 操作ページ：3つのデバイスタブを見出しとして描いてから、選択中のタブの一覧表を出す。
            DrawDeviceTabs(x + 32, bodyY, w - 64, Menu.Page);
            DrawPageControls(x + 32, bodyY + 44f, w - 64, Menu.Page);
        }
        else if (Menu.Page == HowToPlay.TabCount) DrawPageHud(x + 32, bodyY, w - 64);
        else DrawPageCore(x + 32, bodyY, w - 64);

        // ── フッタ（操作ヒント）──
        //   「とじる」だけはクリックできる＝ホバーで明るくして押せることを示す（左の「タブ・ページ」は
        //   純粋な操作説明なので触れない＝ショップの「箱がボタン／素の文字は説明」の流儀）。
        string pageTok = (Pad.UsingPad ? Pad.Face(JoyButton.LeftShoulder) + " / " + Pad.Face(JoyButton.RightShoulder) : "←→")
                         + FootGap;
        string closeTok = FootCloseToken + " とじる";
        float pageW = UiKit.TextW(UiKit.Mono, pageTok, UiKit.FontSmall);
        float closeW = UiKit.TextW(UiKit.Mono, closeTok, UiKit.FontSmall);
        float lineX = x + (w - (pageW + closeW)) / 2f;
        float fy = y + h - 32;
        bool closeHov = hov == HowToPlay.IdClose;
        if (closeHov)
            UiKit.Box(this, HowToPlay.CloseHintRect(), new Color(UiKit.Purify, 0.14f), 7f, new Color(UiKit.Info, 0.5f), 1f);
        UiKit.Text(this, UiKit.Mono, new Vector2(lineX, fy), pageTok, UiKit.FontSmall, UiKit.Text3);
        UiKit.Text(this, UiKit.Mono, new Vector2(lineX + pageW, fy), closeTok, UiKit.FontSmall,
            closeHov ? UiKit.PurifyHi : UiKit.Text3);
    }

    // フッタ1行を「ページ送りの説明」と「とじる（クリック可）」に割るための共有トークン。
    //   HowToPlay.CloseHintRect が同じ式で矩形を再現するので、ここを変えたら向こうも自動で追従する。
    public const string FootGap = " タブ・ページ    ";
    public static string FootCloseToken => Pad.UsingPad ? Pad.Face(JoyButton.Start) : "Esc";

    // ───────── デバイスタブの見出し（操作ページの上端）─────────
    //   3つ並べ、選択中だけ塗りとアクセント色を強める。切替そのものは ←→ / LB・RB のページ送り
    //   （HowToPlay._Process）と同じ軸に載せてある＝操作が増えない。
    private static readonly string[] TabNames = { "キーボード", "コントローラー", "マウス" };

    private void DrawDeviceTabs(float x, float y, float w, int sel)
    {
        float tw = (w - 16f) / HowToPlay.TabCount, th = 34f;
        int hov = UiKit.HoveredId();
        for (int i = 0; i < HowToPlay.TabCount; i++)
        {
            float tx = x + i * (tw + 8f);
            bool on = i == sel;
            // タブは直接クリックできる（HowToPlay._Process）。未選択でもホバー中は枠と塗りを一段上げて
            //   「押せる」ことと「いま触れている」ことを両方示す。
            bool hv = !on && hov == i;
            Color accent = on ? UiKit.PurifyHi : hv ? UiKit.Info : UiKit.Text3;
            UiKit.Box(this, new Rect2(tx, y, tw, th),
                      new Color(accent, on ? 0.16f : hv ? 0.12f : 0.05f), 9f, new Color(accent, on ? 0.9f : hv ? 0.7f : 0.35f), 1.2f);
            UiKit.Text(this, on ? UiKit.ZenBold : UiKit.Zen, new Vector2(tx, y + 8),
                       TabNames[i], UiKit.FontLabel, on ? UiKit.White : hv ? UiKit.PurifyHi : UiKit.Text3,
                       HorizontalAlignment.Center, tw);
        }
    }

    // ───────── ページ1〜3：操作の割り当て一覧（デバイス別・2列）─────────
    //   tab: 0=キーボード / 1=コントローラー(Xbox 表記) / 2=マウス。
    //   ★未取得の能力（回避／溜め打ち／集中モード）は行ごと出さない（既存の作法）。
    //   ★低速移動は 2026-09-13 に機能ごと廃止＝どのタブにも存在しない。
    private void DrawPageControls(float x, float y, float w, int tab)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        bool hasDodge = game?.HasDodge ?? false;
        bool hasCharge = game?.HasChargeShot ?? false;
        bool hasFocus = game?.HasFocusMode ?? false;

        // (token, 名前, 説明, accent, 強調?)。デバイスごとに直に書く＝このタブが「その機種の一覧表」になる。
        var rows = new System.Collections.Generic.List<(string tok, string name, string desc, Color accent, bool hot)>();

        string moveTok = tab switch { 1 => "L スティック / 十字キー", 2 => "カーソル", _ => "矢印 / WASD" };
        string moveDesc = tab == 2 ? "マウスカーソルの位置へ寄っていく" : "上下左右に動く";
        rows.Add((moveTok, "移動", moveDesc, UiKit.Info, false));
        rows.Add(("オート", "撃つ", "自動で撃ちます。光を放って心を浄化する", UiKit.Purify, false));

        // ロックオン送り。マウスだけ短押し／長押しの分岐があるので説明を変える。
        string lockTok = tab switch { 1 => Pad.Face(JoyButton.RightShoulder), 2 => "左クリック", _ => "F" };
        string lockDesc = tab == 2
            ? "短く押すたび近い敵から順に狙う。移動は少し遅くなる"
            : "押すたび近い敵から順に狙う。移動は少し遅くなる";
        rows.Add((lockTok, "ロックオン送り", lockDesc, UiKit.Purify, true));

        // ロックオン解除。2026-09-17 にキーボード(G)／パッド(R3)へも割り当てた（従来はマウス右クリックのみ）。
        // マウスの右クリックは回避と兼用なので、下の回避行でまとめて出す＝ここはKB/パッドの2タブだけ。
        if (tab != 2)
            rows.Add((tab == 1 ? Pad.Face(JoyButton.RightStick) : "G", "ロックオン解除",
                      "狙っている敵から照準を外す。放っておいても自然に外れる", UiKit.Purify, false));

        if (hasCharge)
        {
            string chTok = tab switch { 1 => Pad.Face(JoyButton.Y) + " 長押し", 2 => "左クリック 長押し", _ => "C 長押し" };
            rows.Add((chTok, "溜め打ち", "0.6秒ためて離す。威力12倍、盾や敵を貫く", UiKit.Gold, true));
        }

        // 回避とロック解除。マウスは同じ右クリックが両方を兼ねる。
        if (tab == 2)
            rows.Add(("右クリック", hasDodge ? "回避＋ロック解除" : "ロック解除",
                      hasDodge ? "一瞬無敵で弾をすり抜けつつ、ロックも外れる" : "狙っている敵から照準を外す",
                      hasDodge ? UiKit.Gold : UiKit.Text2, hasDodge));
        else if (hasDodge)
            rows.Add((tab == 1 ? "L3" : "Alt", "回避ダッシュ", "一瞬無敵で弾をすり抜ける。攻めの切り札", UiKit.Gold, true));

        rows.Add((tab switch { 1 => Pad.Face(JoyButton.X), 2 => "中クリック", _ => "X" },
                  "ボム", "画面の弾を消し短時間無敵。残数ぶん", UiKit.Mina, false));

        if (hasFocus)
            rows.Add((tab switch { 1 => Pad.Face(JoyButton.LeftShoulder), 2 => "ホイール / サイドボタン", _ => "V" },
                      "集中モード", "敵の時間だけが遅くなる。1.5秒", UiKit.Purify, true));

        rows.Add((tab switch { 1 => Pad.Face(JoyButton.A), 2 => "左クリック", _ => "Z / Enter / Space" },
                  "会話を送る", "1回目で全文表示、2回目で次の行へ", UiKit.Info, false));
        rows.Add((tab == 1 ? Pad.Face(JoyButton.RightShoulder) + " 長押し" : tab == 2 ? "—" : "Ctrl 長押し",
                  "既読スキップ", tab == 2 ? "マウスには割り当てなし（Ctrl / " + Pad.Face(JoyButton.RightShoulder) + "）"
                                          : "一度読んだ行だけ高速で送る", UiKit.Text2, false));
        rows.Add((tab == 1 ? Pad.Face(JoyButton.Start) : tab == 2 ? "—" : "Esc",
                  "メニュー", tab == 2 ? "マウスには割り当てなし（Esc / " + Pad.Face(JoyButton.Start) + "）"
                                       : "セーブ・音量・つづける", UiKit.Text2, false));
        rows.Add((tab == 1 ? "—" : "R / Shift+R",
                  "やりなおす", tab == 1 ? "キーボードのみ（R＝続きから / Shift+R＝最初から）"
                                          : "R＝続きから、Shift+R＝最初から", UiKit.Text2, false));

        float colW = (w - 24f) / 2f, rowH = 60f;
        int half = (rows.Count + 1) / 2;
        for (int i = 0; i < rows.Count; i++)
        {
            int col = i / half, idx = i % half;
            float rx = x + col * (colW + 24f);
            float ry = y + idx * rowH;
            DrawControlRow(rx, ry, colW, rows[i].tok, rows[i].name, rows[i].desc, rows[i].accent, rows[i].hot);
        }

        // 念押し：光はオート発射＝撃つボタンが無いことを明示する。
        float ny = y + half * rowH + 6f;
        UiKit.Text(this, UiKit.Zen, new Vector2(x, ny),
            "※ 光は自動で出ます。撃つボタンはありません", UiKit.FontLabel, UiKit.Gold);
        // 会話中の2択（ChoiceOverlay）は全デバイス共通の操作なので1行案内。
        UiKit.Text(this, UiKit.Zen, new Vector2(x, ny + 22f),
            "◇ 会話中の2択：↑↓ / マウスで選ぶ、" + Pad.ConfirmToken + " で決定", UiKit.FontLabel, UiKit.PurifyHi,
            HorizontalAlignment.Left, w);
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
            (2, "浄化 ％",     "ステージの進み具合。100%でボスへ",                     UiKit.Purify),
            (1, "コンボ",      "連続で浄化するとSCOREも、こぼれる心の量も増える。猶予内に次を倒せないと途切れる", UiKit.Mina),
            (1, "SCORE",       "遊びの得点。ハイスコアを狙える",                       UiKit.Gold),
            // ★2026-09-17 経済改修：通貨は撃破時ではなく「散った欠片を拾ったとき」に入る。
            //   拾う動作が報酬だと一目で分かる説明にする（実装と表記の一致＝§3 わかりやすさ）。
            (0, "浄化した心",  "通貨。浄化でこぼれた欠片を拾うと貯まる。ショップ（ハブで " + TokBomb + "）でミナを強化できる", UiKit.Hp),
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
            ("ボム",
             "ピンチの保険。" + TokBomb + " で画面の弾を消し無敵に。残数は限られる。",
             UiKit.Mina),
            ("弾強化",
             "ハブで " + TokBomb + " →ショップ。「浄化した心」で 連射 / 拡散 / ホーミング / 加速球（タメて撃つロケット弾） を解放・強化。",
             UiKit.Gold),
            // 後方弾カード：2026-09-15 後方弾の廃止（Player.cs 側で発射停止）に合わせて削除。
            ("浄化と汚染",
             "敵を浄化＝救うこと。汚染は物語が進むほど自然に上がる演出で、画面の濁りとして表れる。澄んだ心I/IIで上昇をゆるやかにできる。",
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
