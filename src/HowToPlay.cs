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
//   ★2026-09-22：ユーザー要望「ボタン配置・強化アイテムの説明をメニューから確認できるように」。
//     ・導線はポーズメニューの「あそびかた」行（PauseMenu.Act.HowTo）。タイトルには置かない
//       （a210286 でタイトルの項目は意図して削られた＝TitleMenuQa「requested menu entries」）。
//     ・未取得の操作（回避／溜め打ち／集中モード）は**行ごと消さず薄く出す**。「配置を確認する」画面で
//       行が消えると、どのボタンが空いているのか・何が増えるのかが分からないため。右端に入手条件を添える。
//     ・末尾に「強化アイテム」ページを足した（ザコが落とす4種の欠片：見え方・効果・被弾で失う）。
public partial class HowToPlay : CanvasLayer
{
    private HowToCanvas _canvas = null!;
    private bool _open;
    private int _page;            // 0..PageCount-1（0..2 が操作タブ／3=画面の見かた／4=コア機能／5=強化アイテム）
    private bool _lrHeld, _backHeld;
    private bool _autoplay;

    // 閉じたときに呼ぶコールバック（任意）。ポーズから開いた場合などに使う。
    private System.Action? _onClose;

    // 操作の割り当て一覧タブ（先頭3ページ）。0=キーボード / 1=コントローラー / 2=マウス。
    public const int TabCount = 3;
    // 後続の読み物ページ：画面の見かた／コア機能／強化アイテム（2026-09-22 追加）。
    public const int PageHud = TabCount, PageCore = TabCount + 1, PageItems = TabCount + 2;
    public const int PageCount = TabCount + 3;

    // ── マウス用ジオメトリ（HowToCanvas.DrawScreen と同一式）──
    //   パネル：pad=64, x=pad, y=32, w=W-128, h=H-64。以降の3つはそこからの相対。
    //   ホットスポット id：0..TabCount-1＝デバイスタブ／IdDotBase+i＝ページドット／IdClose＝フッタ「とじる」。
    public const int IdDotBase = 10, IdClose = 90;

    // デバイスタブの矩形（DrawDeviceTabs と同一式。操作ページを開いている時だけ意味を持つ）。
    public static Rect2 TabRect(int i)
    {
        float pad = 64f, x = pad, y = 32f, w = UiKit.DesignW - pad * 2f;
        float ix = x + 32f, iy = y + 92f, iw = w - 64f;
        float tw = (iw - 16f) / TabCount, th = 34f;
        return new Rect2(ix + i * (tw + 8f), iy, tw, th);
    }

    // ページドット（右上）の当たり矩形。描画は半径5の円だが、点を突くのは酷なので 22×22 の枠で取る。
    public static Rect2 DotRect(int i)
    {
        float pad = 64f, x = pad, y = 32f, w = UiKit.DesignW - pad * 2f;
        var c = new Vector2(x + w - 32f - (PageCount - 1 - i) * 22f, y + 40f);
        return new Rect2(c.X - 11f, c.Y - 11f, 22f, 22f);
    }

    // フッタの「とじる」矩形（Records.BackHintRect と同じ考え方＝キーキャップ＋ラベルの帯だけを取る）。
    //   フッタ行は中央寄せの1行なので、「とじる」の実描画位置を HowToCanvas.FootLayout の同じ式で再現して切り出す。
    public static Rect2 CloseHintRect()
    {
        var (_, closeX, top, closeW) = HowToCanvas.FootLayout();
        return new Rect2(closeX - 6f, top - 5f, closeW + 12f, HowToCanvas.FootCapH + 10f);
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
    private static string TokBomb => Pad.UsingPad ? Pad.Face(JoyButton.X) : Pad.UsingMouse ? "中クリック" : "X";
    // フッタの「とじる」表記は下の FootCloseToken（キーボードは Esc＝もどる、パッドは B）。
    //   メニューを開くキーの表記は Pad.PauseToken（M / Menu(≡)）＝2026-09-26 に Esc から分離した。

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
        float x = pad, y = 32f, w = W - pad * 2f, h = H - 64f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.05f, 0.05f, 0.10f, 0.98f), 18f, new Color(UiKit.Purify, 0.6f), 1.4f);

        // ── ヘッダ（タイトル＋ページインジケータ）──
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + 32, y + 22), "HOW TO PLAY", UiKit.Info);
        string[] titles = { "操作 — キーボード", "操作 — コントローラー", "操作 — マウス", "画面の見かた", "コア機能", "ドロップアイテム" };
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
        else if (Menu.Page == HowToPlay.PageHud) DrawPageHud(x + 32, bodyY, w - 64);
        else if (Menu.Page == HowToPlay.PageCore) DrawPageCore(x + 32, bodyY, w - 64);
        else DrawPageItems(x + 32, bodyY, w - 64);

        // ── フッタ（操作ヒント）──
        //   [←→] タブ・ページ　[Esc] とじる を中央寄せ1行で（2026-09-27 にキーを UiKit.KeyCap へ）。
        //   「とじる」だけはクリックできる＝ホバーで明るくして押せることを示す（左の「タブ・ページ」は
        //   純粋な操作説明なので触れない＝ショップの「箱がボタン／素の文字は説明」の流儀）。
        var (pageX, closeX, top, _) = FootLayout();
        bool closeHov = hov == HowToPlay.IdClose;
        if (closeHov)
            UiKit.Box(this, HowToPlay.CloseHintRect(), new Color(UiKit.Purify, 0.14f), 7f, new Color(UiKit.Info, 0.5f), 1f);
        float pw = UiKit.KeyCapRow(this, new Vector2(pageX, top), FootPageToken, FootCapH);
        FootLabel(pageX + pw + FootLabelGap, top, FootPageLabel, UiKit.Text3);
        float cw = UiKit.KeyCap(this, new Vector2(closeX, top), FootCloseToken, FootCapH);
        FootLabel(closeX + cw + FootLabelGap, top, "とじる", closeHov ? UiKit.PurifyHi : UiKit.Text3);
    }

    private void FootLabel(float x, float top, string label, Color col)
    {
        float asc = UiKit.Zen.GetAscent(UiKit.FontSmall), desc = UiKit.Zen.GetDescent(UiKit.FontSmall);
        DrawString(UiKit.Zen, new Vector2(x, top + (FootCapH - 2f + asc - desc) / 2f), label, HorizontalAlignment.Left, -1,
            UiKit.FontSmall, col);
    }

    // フッタ1行の寸法（描画と HowToPlay.CloseHintRect が同じ式を通る）。
    //   戻り値：ページ送りの左端・「とじる」のキャップ左端・キャップ上端・「とじる」項目（キャップ＋ラベル）の幅。
    public const float FootCapH = 22f;
    private const float FootLabelGap = 6f, FootItemGap = 28f;
    private const string FootPageLabel = "タブ・ページ";
    private static string FootPageToken => Pad.UsingPad ? Pad.Face(JoyButton.LeftShoulder) + " / " + Pad.Face(JoyButton.RightShoulder)
        : Pad.UsingMouse ? "ホイール" : "←→";
    // とじるキー：キーボードは Esc、パッドは B（HowToPlay._Process が読むのは X／Esc／B／右クリック。
    //   旧表記の Menu(≡) はこの画面では読まれていなかった）。
    public static string FootCloseToken => Pad.UsingPad ? Pad.Face(JoyButton.B) : Pad.UsingMouse ? "右クリック" : "Esc";
    public static (float pageX, float closeX, float top, float closeW) FootLayout()
    {
        float pad = 64f, x = pad, y = 32f, w = UiKit.DesignW - pad * 2f, h = UiKit.DesignH - 64f;
        float pageW = UiKit.KeyCapRowW(FootPageToken, FootCapH) + FootLabelGap + UiKit.TextW(UiKit.Zen, FootPageLabel, UiKit.FontSmall);
        float closeW = UiKit.KeyCapW(FootCloseToken, FootCapH) + FootLabelGap + UiKit.TextW(UiKit.Zen, "とじる", UiKit.FontSmall);
        float lineX = x + (w - (pageW + FootItemGap + closeW)) / 2f;   // 中央寄せ1行の左端
        float top = y + h - 36f;
        return (lineX, lineX + pageW + FootItemGap, top, closeW);
    }

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
    //   ★未取得の能力（回避／集中モード）も行は出す（2026-09-22。以前は行ごと消していた）。
    //     溜め打ちは 2026-09-25 から最初から使える＝常に通常の強調行で出る（入手条件は添えない）。
    //     薄く描いて右端に入手条件を添える＝「どのボタンが何になるか」を取る前から一覧できる。
    //     取ったあとは通常の強調行（★）になる。
    //   ★低速移動は 2026-09-13 に機能ごと廃止＝どのタブにも存在しない。
    private void DrawPageControls(float x, float y, float w, int tab)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        bool hasDodge = game?.HasDodge ?? false;
        float chargeSec = game?.ChargeNeedSec ?? 0.60f;
        bool hasFocus = game?.HasFocusMode ?? false;

        // (token, 名前, 説明, accent, 強調?, 未取得の入手条件)。locked が空でなければ薄く描く。
        //   デバイスごとに直に書く＝このタブが「その機種の一覧表」になる。
        var rows = new System.Collections.Generic.List<(string tok, string name, string desc, Color accent, bool hot, string locked)>();
        void Add(string tok, string name, string desc, Color accent, bool hot, string locked = "")
            => rows.Add((tok, name, desc, accent, hot, locked));

        string moveTok = tab switch { 1 => "L スティック / 十字キー", 2 => "カーソル", _ => "↑↓←→" };
        string moveDesc = tab == 2 ? "マウスカーソルの位置へ寄っていく" : "上下左右に動く";
        Add(moveTok, "移動", moveDesc, UiKit.Info, false);
        Add("オート", "撃つ", "通常の光は自動で出る。溜め中と回避中は止まる", UiKit.Purify, false);

        string lockTok = tab switch { 1 => Pad.Face(JoyButton.RightShoulder) + " / " + Pad.Face(JoyButton.RightStick), 2 => "左クリック 短押し", _ => "Shift 長押し" };
        string lockDesc = tab switch
        {
            1 => "RBで近い敵を狙う。R3で解除。\nロック中は移動が少し遅くなる",
            2 => "短く押すとロック、もう一度短く押すと解除。\nロック中は移動が少し遅くなる",
            _ => "押し続けると近い敵を狙い、離すと解除。\nロック中は移動が少し遅くなる",
        };
        Add(lockTok, "ロック・解除", lockDesc, UiKit.Purify, true);

        Add(tab switch { 1 => Pad.Face(JoyButton.RightShoulder), 2 => "ホイール", _ => "A / S" }, "狙う敵を替える",
            tab switch
            {
                1 => "押すたび次の敵へ。対象を倒すと近い敵へ自動で移る",
                2 => "ロック中、上で次・下で前の敵へ。\n対象を倒すと近い敵へ自動で移る",
                _ => "Shiftを押しながら、Aで前・Sで次の敵へ。\n対象を倒すと近い敵へ自動で移る",
            }, UiKit.Purify, false);

        // 溜め打ち（最初から使える。2026-09-25 のユーザー決定で習得ゲートを撤廃）。
        //   ショップの「溜め打ち 二段」（n_charge）を買うと、離さずさらに溜めて 2段目が撃てる
        //   ＝買う前は1段だけの説明、買った後は2段目の秒数と伸びぶんも添える（どちらも実値）。
        bool tier2 = game?.HasChargeTier2 ?? false;
        string chargeDesc = $"{chargeSec:0.0}秒ためて離すと、{game!.JobDef.ChargeDescription}。"
            + (tier2 ? $"\n{game.ChargeTier2NeedSec:0.0}秒までためると2段目（威力 ×{ChargeTier.PowerMul:0.0}）"
                     : "\nたまる前に離すと不発。完了は光と音で知らせる");
        // キーボードは Z 長押し（2026-09-26 作者決定。旧 C）。
        Add(tab switch { 1 => Pad.Face(JoyButton.Y) + " 長押し", 2 => "左クリック 長押し", _ => "Z 長押し" },
            "溜め打ち", chargeDesc, UiKit.Gold, true);

        Add(tab switch { 1 => Pad.Face(JoyButton.LeftStick), 2 => "右クリック", _ => "Space" }, "回避",
            (tab == 2 ? "カーソルの方向へ無敵ダッシュ。近ければその場回避"
                      : "移動方向へ無敵ダッシュ。方向入力なしならその場回避")
                + (hasDodge ? "" : "\nショップ「回避」で習得"),
            UiKit.Gold, hasDodge, hasDodge ? "" : "未習得");

        Add(tab switch { 1 => Pad.Face(JoyButton.X), 2 => "中クリック", _ => "X" },
            "ボム", "敵を攻撃し、短時間無敵になる。\n敵弾は消えない。残数を1つ使う", UiKit.Mina, false);

        // 集中モード（ショップの「集中モード」＝n_slow で覚える）。キーボードは C（2026-09-27。旧 V）。
        Add(tab switch { 1 => Pad.Face(JoyButton.LeftShoulder), 2 => "サイドボタン", _ => "C" },
            "集中モード", "敵の時間だけが遅くなる。1.5秒"
                + (tab == 2 ? "\n敵をロックしていない時はホイールでも発動" : "")
                + (!hasFocus && tab != 2 ? "\nショップ「集中モード」で習得" : ""), UiKit.Purify, hasFocus,
            hasFocus ? "" : tab == 2 ? "未習得 — ショップ" : "未習得");

        Add(tab switch { 1 => Pad.Face(JoyButton.A), 2 => "左クリック", _ => "Z / Enter / Space" },
            "会話を送る", "1回目で今のページを全文表示、2回目で続きを表示", UiKit.Info, false);
        Add(tab == 1 ? Pad.Face(JoyButton.RightShoulder) + " 長押し" : tab == 2 ? "左クリック" : "Ctrl 長押し",
            "既読スキップ", tab == 2 ? "会話ボックス上の「▶▶」を押して開始・停止"
                                    : "会話中、一度読んだ行だけ高速で送る", UiKit.Text2, false);
        // メニューは M / Esc（2026-09-27。Esc でも開く＝PauseMenu.EscOpensHere）。ただしスマホ系の画面（ハブ/ショップ等）の
        //   Esc は従来どおり「もどる」。メニューの中の Esc は一段もどる。
        Add(tab == 1 ? Pad.Face(JoyButton.Start) : "M / Esc",
            "メニュー", tab == 2 ? "戦闘中はM / Esc。会話中は「メニュー」をクリック"
                                 : tab == 1 ? "セーブ・音量・あそびかた" : "メニュー内の Esc は一段もどる（スマホの画面の Esc はもどる）", UiKit.Text2, false);
        Add(tab == 1 ? Pad.Face(JoyButton.Start) : "R 長押し",
            "やりなおす", tab == 1 ? "メニューの「リスタート」で最初からやりなおす"
                : $"{RetryHold.HoldTime:0.00}秒長押しで最初から。敗北時はRで再開\n（ボス到達後はボスから）、Shift+Rなら最初から", UiKit.Text2, false);

        float colW = (w - 24f) / 2f, rowH = 72f;
        int half = (rows.Count + 1) / 2;
        for (int i = 0; i < rows.Count; i++)
        {
            int col = i / half, idx = i % half;
            float rx = x + col * (colW + 24f);
            float ry = y + idx * rowH;
            DrawControlRow(rx, ry, colW, rows[i].tok, rows[i].name, rows[i].desc, rows[i].accent, rows[i].hot, tab == 1, rows[i].locked);
        }

        float ny = y + half * rowH + 6f;
        string choice = tab switch
        {
            1 => $"◇ 会話の選択肢：Lスティック / 十字キーで選ぶ、{Pad.Face(JoyButton.A)}で決定",
            2 => "◇ 会話の選択肢：選びたい項目を左クリックで決定",
            _ => "◇ 会話の選択肢：↑↓で選ぶ、Z / Enter / Spaceで決定",
        };
        UiKit.Text(this, UiKit.Zen, new Vector2(x, ny), choice, UiKit.FontLabel, UiKit.PurifyHi,
            HorizontalAlignment.Left, w);
        // 会話ボックス上辺のボタン列（AUTO / SKIP / LOG / MENU・src/DialogToolbar.cs）。割り当てはタブの機種で出す。
        string toolbar = tab switch
        {
            1 => $"◇ 会話ボックス上のボタン：{Pad.Face(JoyButton.Y)} 自動送り / {Pad.Face(JoyButton.RightShoulder)} 短押し 既読スキップ / "
                 + $"{Pad.Face(JoyButton.Back)} ログ / {Pad.Face(JoyButton.Start)} メニュー",
            2 => "◇ 会話ボックス上のボタン（▶ 自動送り / ▶▶ 既読スキップ / ログ / メニュー）：クリックで押せる",
            _ => "◇ 会話ボックス上のボタン：A 自動送り / S 既読スキップ / L ログ / M メニュー（クリックでも押せる）",
        };
        UiKit.Text(this, UiKit.Zen, new Vector2(x, ny + 22f), toolbar, UiKit.FontLabel, UiKit.PurifyHi,
            HorizontalAlignment.Left, w);
    }

    // locked（未取得の入手条件）が空でなければ薄く描く：キー・名前・説明のαを落とし、★の位置に条件を出す。
    //   pad＝コントローラー表（いまの表示が KB でも A／B／X／Y を丸ボタン、LB／RB／L3 をピルで描く）。
    private void DrawControlRow(float x, float y, float w, string tok, string name, string desc, Color accent, bool hot, bool pad,
        string locked = "")
    {
        float h = 70f;
        bool dim = locked.Length > 0;
        if (hot) UiKit.Box(this, new Rect2(x, y, w, h), new Color(accent, 0.10f), 10f, new Color(accent, 0.55f), 1.2f);
        // キーキャップの並び（2026-09-27 に旧 KeyBadge＝文字の枠から UiKit.KeyCapRow へ。並記は 4px 間隔でキャップを並べ、
        //   「長押し」「を離す」等の語は添え文字、マウスは絵）。高さ 22 を行の上寄せ（y+6）に置く＝名前の行と揃う。
        float badgeW = UiKit.KeyCapRow(this, new Vector2(x + 8, y + 6), tok, 22f, dim ? 0.45f : 1f, pad, new Color(accent, 1f));
        float tx = x + 8 + badgeW + 14f;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, y + 6), name, UiKit.FontBody,
                   dim ? UiKit.Text4 : hot ? new Color(accent, 1f) : UiKit.White);
        UiKit.Multi(this, UiKit.Zen, new Vector2(x + 8, y + 28), desc, UiKit.FontLabel,
                    dim ? UiKit.Text3 : UiKit.Text2, w - 16f, 2);
        if (dim)
        {
            float lw = UiKit.TextW(UiKit.Zen, locked, UiKit.FontSmall);
            UiKit.Text(this, UiKit.Zen, new Vector2(x + w - lw - 8, y + 8), locked, UiKit.FontSmall, UiKit.Text4);
        }
        else if (hot)
        {
            float sw = UiKit.TextW(UiKit.Mono, "★", UiKit.FontSmall);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - sw - 8, y + 6), "★", UiKit.FontSmall, accent);
        }
    }

    // ───────── ページ4：画面の見かた（凡例・各1行）─────────
    private void DrawPageHud(float x, float y, float w)
    {
        // (icon種別, 名前, 説明, 色)。icon: 0=ハート 1=色チップ円 2=ゲージ片
        var rows = new (int icon, string name, string desc, Color col)[]
        {
            (0, "LIFE",        "残りの体力。攻撃を受けると1つ減る",                    UiKit.Hp),
            (1, "BOMB",        "ボムの残り。" + TokBomb + " で攻撃しながら無敵になれる",        UiKit.Mina),
            (2, "浄化 ％",     "道中でどれだけ浄化が進んだかを表す",                   UiKit.Purify),
            (1, "コンボ",      "連続で浄化するとSCOREも、こぼれる心の量も増える。猶予内に次を倒せないと途切れる", UiKit.Mina),
            (1, "SCORE",       "遊びの得点。ハイスコアを狙える",                       UiKit.Gold),
            // ★2026-09-17 経済改修：通貨は撃破時ではなく「散った欠片を拾ったとき」に入る。
            //   拾う動作が報酬だと一目で分かる説明にする（実装と表記の一致＝§3 わかりやすさ）。
            // ショップはハブのスマホのアプリから開く（旧「ハブで X」の文字キーは廃止済み）。
            (0, "浄化した心",  "欠片を拾うと貯まる通貨。ハブのショップで仲間共通の強化に使える", UiKit.Hp),
            (1, "フォロワー",  "届けた証。増えるほど全弾ダメージが微増（上限+50%）とインプレに上乗せ", UiKit.Info),
            (1, "TIME",        "クリアタイム。記録に挑戦",                            UiKit.Text2),
            (1, "ロックオン",  "待機中は対象探し。追尾中は下の文字でシールド／本体を確認", UiKit.Purify),
            (2, "ボスの状態",  "「攻撃終了待ち」の間は避け続け、「本体に攻撃」で本体を狙う", UiKit.Gold),
        };
        float rowH = 40f;
        for (int i = 0; i < rows.Length; i++)
        {
            float ry = y + i * rowH;
            DrawIcon(new Vector2(x + 14, ry + 12), rows[i].icon, rows[i].col);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 40, ry + 4), rows[i].name, UiKit.FontBody, UiKit.White);
            UiKit.Text(this, UiKit.Zen, new Vector2(x + 240, ry + 4), rows[i].desc, UiKit.FontBody, UiKit.Text2);
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

    private void DrawPageItems(float x, float y, float w)
    {
        UiKit.Multi(this, UiKit.Zen, new Vector2(x, y),
            "アンチャーを浄化していると、ときどき、心の欠片に混じって「色の枠がついた欠片」がこぼれる。"
            + "拾い方はふつうの欠片と同じ（近づけば吸い寄せられる）。",
            UiKit.FontLabel, UiKit.Text2, w);

        var rows = new (PowerKind kind, string name, string desc)[]
        {
            (PowerKind.Line,   "光が強くなる",   "弾の威力が1段階で1.1倍、2段階で1.3倍になる"),
            (PowerKind.Speed,  "足が速くなる",   "移動が速くなる。よけやすく、拾いやすく"),
            (PowerKind.Life,   "LIFEを1回復",     "敵を30体倒すと出現。拾ったその場で回復する（最大LIFEまで）"),
        };
        float top = y + 58f, rowH = 78f;
        for (int i = 0; i < rows.Length; i++)
        {
            float ry = top + i * rowH;
            Color col = PowerPickupArt.ColorFor(rows[i].kind);
            UiKit.Box(this, new Rect2(x, ry, w, rowH - 12f), new Color(col, 0.06f), 12f, new Color(col, 0.35f), 1.1f);
            PowerPickupArt.Draw(this, new Rect2(x + 18f, ry + 13f, 40f, 40f), rows[i].kind);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 80f, ry + 10f), rows[i].name, UiKit.FontBody, col);
            UiKit.Text(this, UiKit.Zen, new Vector2(x + 80f, ry + 36f), rows[i].desc, UiKit.FontLabel, UiKit.Text2,
                       HorizontalAlignment.Left, w - 100f);
        }

        float ny = top + rows.Length * rowH + 2f;
        UiKit.Text(this, UiKit.Zen, new Vector2(x, ny),
            "◇ 光・移動の強化は重ねて拾うと段が上がる（上限あり）。いま持っているものは左のパネル、LIFE の上に出る",
            UiKit.FontLabel, UiKit.PurifyHi, HorizontalAlignment.Left, w);
        UiKit.Text(this, UiKit.Zen, new Vector2(x, ny + 24f),
            "※ 光・移動の強化は被弾すると失い、次のステージには持ち越せない。LIFE回復は拾った時に適用",
            UiKit.FontLabel, UiKit.Gold, HorizontalAlignment.Left, w);
    }

    // ───────── ページ5：コア機能（図解カード）─────────
    private void DrawPageCore(float x, float y, float w)
    {
        var cards = new (string title, string body, Color accent)[]
        {
            ("ボム",
             TokBomb + " で敵を攻撃し、短時間無敵に。敵弾は消えないので、安全な場所へ移動しよう。残数は限られる。",
             UiKit.Mina),
            ("仲間と強化",
             "撃ち方は仲間ごとに異なる。ハブのショップでは「浄化した心」を使い、回避・溜め打ち二段・弾数などを順番に強化できる。",
             UiKit.Gold),
            // 後方弾カード：2026-09-15 後方弾の廃止（Player.cs 側で発射停止）に合わせて削除。
            ("浄化と汚染",
             "アンチャーを浄化し、閉じ込められた心へ近づく。汚染による光や画面の濁りは、ミナが抱えていく負担を表す物語の演出。ショップでは減らせない。",
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
