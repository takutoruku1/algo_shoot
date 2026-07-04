using Godot;

// Shop : ミナ強化ショップ。UI/shuzinkou/Refrain Shot Upgrades.dc.html を移植したモードカード型レイアウト。
//   上：ヘッダ＋ウォレット ／ ショットモード切替ストリップ（過熱トグル）
//   中：3モードカード（連射／拡散／ホーミング）— 射撃プレビュー・スペック・レベルダイヤル・選択/購入
//   下：共通強化グリッド（攻撃／生存／応援の3カテゴリ・2列）＋フォーカス連動の詳細パネル
//       詳細パネル＝「いま買うと何がどう変わるか」：現在値→購入後値・コスト・購入後の残り・買えない理由を常時表示
//       （桜井流：買い物の意思決定に必要な情報を、押す前に全部見せる）。
//   演出：購入バースト・ウォレットpop・ダイヤル充填・モードスウィープ・過熱オーバーロード全画面フラッシュ。
//   操作：↑↓←→ えらぶ／Z 購入(解放/強化)／C 装備(モード選択)／V 過熱プレビュー／X もどる。
public partial class Shop : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    // フォーカス対象：0..2=モードカード（連射/拡散/ホーミング）、3..=共通強化グリッド（行優先・2列）。
    private const int ModeCount = 3;
    private int _sel;

    private static readonly GameManager.ShotMode[] Modes =
        { GameManager.ShotMode.Rapid, GameManager.ShotMode.Spread, GameManager.ShotMode.Homing };
    private static readonly string[] ModeUpId = { "", "shot_spread", "shot_homing" };
    private static readonly string[] ModeEn = { "RAPID", "SPREAD", "HOMING" };
    private static readonly string[] ModeDesc =
    {
        "右へ直線の高速ストリーム。正面の硬い敵に火力集中。",
        "右方向へ扇状に展開。雑魚処理・面制圧・道中向き。",
        "右側のマゼンタの穢れへ吸い寄せられて曲射。避けながら削る。",
    };

    // 共通強化の一覧（カテゴリ別・行優先2列で並ぶ）。cat: 0=攻撃 / 1=生存 / 2=応援。
    // GameManager.Upgrades に定義済みの全強化をここで売る（従来は3種のみ販売＝残りが死蔵だった）。
    private static readonly (string id, int cat)[] CoreItems =
    {
        ("shot_power", 0), ("fire_rate", 0),
        ("option_sub", 0), ("max_life", 1),
        ("bomb_count", 1), ("bomb_power", 1),
        ("move_speed", 1), ("hitbox", 1),
        ("contam_resist", 1), ("imp_mult", 2),
        ("fol_gain", 2), ("combo_hold", 2),
    };
    private static readonly string[] CatName = { "攻撃", "生存", "応援" };
    private static readonly Color[] CatCol = { new("9be0f5"), new("7ec880"), new("f0d98a") };
    private const int GridCols = 2;
    private static int GridRows => (CoreItems.Length + GridCols - 1) / GridCols;

    private static readonly Color Light = new("9be0f5");   // 光のハイライト
    private static readonly Color Magenta = new("cf90b5"); // ホーミングのアクセント
    private static readonly Color Orange = new("ff8a5a");  // 過熱
    private static readonly Color Deny = new("ef9a9a");    // 買えない理由（赤）

    // 射撃プレビューのミナ立ち絵（右へ撃つポーズ）。毎フレームLoadしないよう_Readyで一度だけキャッシュ。
    private Texture2D? _minaShot;

    // 入力エッジ
    private bool _navHeld, _zHeld, _equipHeld, _olHeld, _backHeld;
    private double _t, _toastT;
    private string _toast = "";
    private Color _toastCol = UiKit.Info;
    private bool _autoplay;

    // 演出タイマー
    private double _buyFxT;       // 購入バースト
    private double _walletPopT;   // ウォレットpop
    private int _buyFxItem = -1;  // 購入した item（ダイヤル充填）
    private Vector2 _buyFxAt;     // バースト発生源
    private double _sweepT;       // モードスウィープ
    private string _sweepName = "";
    private bool _overloadPreview;
    private double _olFlashT;     // 過熱発動フラッシュ

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        // 起動時、装備中モードにカーソルを合わせる。
        _sel = System.Array.IndexOf(Modes, _game?.SelectedShotMode ?? GameManager.ShotMode.Rapid);
        if (_sel < 0) _sel = 0;
        _minaShot = ResourceLoader.Load<Texture2D>("res://char/mina_shoot.png");
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_toastT > 0) _toastT -= delta;
        if (_buyFxT > 0) _buyFxT -= delta;
        if (_walletPopT > 0) _walletPopT -= delta;
        if (_sweepT > 0) _sweepT -= delta;
        if (_olFlashT > 0) _olFlashT -= delta;
        if (_autoplay) { ExitShop(); return; }

        // カーソル移動：十字で移動（カード列⇄グリッドは上下で行き来）。
        bool up = Input.IsActionPressed("ui_up");
        bool down = Input.IsActionPressed("ui_down");
        bool left = Input.IsActionPressed("ui_left");
        bool right = Input.IsActionPressed("ui_right");
        bool any = up || down || left || right;
        if (any && !_navHeld)
        {
            if (up) Nav(0, -1);
            else if (down) Nav(0, 1);
            else if (left) Nav(-1, 0);
            else Nav(1, 0);
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = any;

        // Z：購入（モードの解放/強化、または共通強化）。連射(購入対象なし)では装備に回す。
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2) OnConfirm();

        // C：装備（フォーカス中のモードを選択）。
        bool c = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        bool cEdge = c && !_equipHeld; _equipHeld = c;
        if (cEdge && _t > 0.2 && _sel < ModeCount) EquipMode(_sel);

        // V：過熱オーバーロードのプレビュー切替（演出確認用）。
        bool v = Input.IsKeyPressed(Key.V) || Pad.Pressed(JoyButton.X);
        bool vEdge = v && !_olHeld; _olHeld = v;
        if (vEdge && _t > 0.2)
        {
            _overloadPreview = !_overloadPreview;
            if (_overloadPreview) _olFlashT = 0.9;
        }

        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2) { Audio.Instance?.PlayUiCancel(); ExitShop(); }

        QueueRedraw();
    }

    // 十字ナビ：上段（モードカード3枚）と下段（強化グリッド2列）を上下で行き来する。
    //   カード上：←→でカード間、↓でグリッド先頭行、↑でグリッド最終行（循環）。
    //   グリッド上：←→で列トグル、↑↓で行移動。上下端を越えるとカードへ戻る。
    private void Nav(int dx, int dy)
    {
        int rows = GridRows;
        if (_sel < ModeCount)
        {
            if (dy > 0) { _sel = ModeCount + (_sel >= 2 ? 1 : 0); }                       // 下へ＝グリッド先頭行
            else if (dy < 0) { _sel = ModeCount + (rows - 1) * GridCols + (_sel >= 2 ? 1 : 0); } // 上へ＝グリッド最終行（循環）
            else _sel = (_sel + dx + ModeCount) % ModeCount;
        }
        else
        {
            int g = _sel - ModeCount, col = g % GridCols, row = g / GridCols;
            if (dx != 0) col = 1 - col;                                                    // 2列トグル
            if (dy != 0)
            {
                row += dy;
                if (row < 0 || row >= rows) { _sel = col == 0 ? 0 : 2; return; }           // 端を越える＝カードへ
            }
            int ng = Mathf.Min(row * GridCols + col, CoreItems.Length - 1);
            _sel = ModeCount + ng;
        }
    }

    // ショップ退出先：初回ショップ導線で復帰先(PendingResumeScene)が立っていれば、ハブでなくそのステージへ戻り
    // “中ボスの続き”から再開する（消費して以降は通常どおりハブへ）。それ以外は従来どおりハブ。
    private void ExitShop()
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        string dest = "res://Hub.tscn";
        if (game != null && !string.IsNullOrEmpty(game.PendingResumeScene))
        {
            dest = game.PendingResumeScene!;
            game.PendingResumeScene = null; // 消費
        }
        GetTree().ChangeSceneToFile(dest);
    }

    private void OnConfirm()
    {
        if (_sel < ModeCount)
        {
            string up = ModeUpId[_sel];
            if (string.IsNullOrEmpty(up)) { EquipMode(_sel); return; } // 連射＝買うものが無い→装備
            Buy(up, _sel, ModeCardCenter(_sel));
        }
        else
        {
            int g = _sel - ModeCount;
            Buy(CoreItems[g].id, _sel, GridCellRect(g).GetCenter());
        }
    }

    private void Buy(string id, int item, Vector2 at)
    {
        int lv = _game?.GetUpgradeLevel(id) ?? 0;
        var d = GameManager.GetUpgradeDef(id);
        if (d == null) return;
        if (lv >= d.MaxLevel) { Audio.Instance?.PlayUiDeny(); Toast("すでに最大です", UiKit.Text4); return; }
        if (!(_game?.CanPurchase(id) ?? false)) { Audio.Instance?.PlayUiDeny(); Toast("浄化した心が足りません", Deny); return; }
        if (_game!.TryPurchase(id))
        {
            Audio.Instance?.PlayUiBuy(); // 購入成功＝達成音
            string label = lv == 0 && (id == "shot_spread" || id == "shot_homing") ? "解放" : "強化";
            Toast($"{d.Name} を{label}！  Lv {_game.GetUpgradeLevel(id)}", UiKit.Info);
            _buyFxT = 0.7; _walletPopT = 0.5; _buyFxItem = item; _buyFxAt = at;
            // 拡散/ホーミングを解放したら自動で装備に切り替える。
            if (lv == 0 && _sel < ModeCount) EquipMode(_sel, silent: true);
        }
    }

    private void EquipMode(int idx, bool silent = false)
    {
        var m = Modes[idx];
        if (!(_game?.IsModeUnlocked(m) ?? false)) { if (!silent) { Audio.Instance?.PlayUiDeny(); Toast("まだ解放されていません", UiKit.Text4); } return; }
        if (_game!.SelectedShotMode == m && !silent) { return; }
        if (!silent) Audio.Instance?.PlayUiConfirm(); // 装備＝決定音
        _game.SelectedShotMode = m;
        _sweepName = _game.ShotModeName(m);
        _sweepT = 1.1;
    }

    private void Toast(string msg, Color col) { _toast = msg; _toastCol = col; _toastT = 1.8; }

    // ───────────────────────── レイアウト座標 ─────────────────────────
    private const float PadX = 40f;
    private const float CardsY = 150f, CardH = 260f, CardGap = 18f;
    private static float CardW => (W - PadX * 2 - CardGap * 2) / 3f;
    private static float CardX(int i) => PadX + i * (CardW + CardGap);
    private Vector2 ModeCardCenter(int i) => new(CardX(i) + CardW / 2f, CardsY + 50f);

    private const float SecY = CardsY + CardH + 16f;     // 共通強化グリッド／詳細パネル
    private const float CoreX = PadX, CoreW = 720f;
    private const float BalX = PadX + CoreW + 20f;       // 詳細パネル（旧バランス表の位置）
    private const float GridY0 = SecY + 30f, GridPitch = 32f, GridRowH = 28f, GridGapX = 10f;
    private static float GridColW => (CoreW - GridGapX) / GridCols;
    private static Rect2 GridCellRect(int g) =>
        new(CoreX + (g % GridCols) * (GridColW + GridGapX), GridY0 + (g / GridCols) * GridPitch, GridColW, GridRowH);

    // ───────────────────────── 描画 ─────────────────────────
    public override void _Draw()
    {
        UiKit.BeginDesign(this);

        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0d0b1c"), new Color("0a0916"), new Color("070611") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.12f, H * 0.42f), 460f, UiKit.Info, 0.10f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        DrawHeader();
        DrawModeStrip();
        for (int i = 0; i < ModeCount; i++) DrawModeCard(i);
        DrawCoreGrid();
        DrawDetailPanel();

        // フッタ操作ヒント（ボタン表記は Pad に集約＝KB/PS/Xbox 切替に追従）。
        float fy = H - 34f, fx = PadX;
        fx = Hint(fx, fy, Pad.MoveToken, "えらぶ", false);
        fx = Hint(fx, fy, Pad.ConfirmToken, "購入", true);
        fx = Hint(fx, fy, Pad.EquipToken, "装備", false);
        fx = Hint(fx, fy, Pad.ModeToken, "過熱", false);
        // 初回ショップ導線で復帰先がある間は、退出＝ステージの続きへ＝「つづける」表記にする。
        bool resuming = !string.IsNullOrEmpty(GetNodeOrNull<GameManager>("/root/Game")?.PendingResumeScene);
        Hint(fx, fy, Pad.CancelToken, resuming ? "つづける" : "もどる", false);

        DrawBuyFx();
        DrawModeSweep();
        DrawToast();
        DrawOverloadOverlay();
        UiKit.EndDesign(this);
    }

    private void DrawHeader()
    {
        UiKit.Text(this, UiKit.Mono, new Vector2(PadX, 22), "SHOT UPGRADE SYSTEM", 11, UiKit.Text3);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(PadX, 36), "弾・ショット強化システム", 28, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(PadX, 72), "連射は初期解放。拡散・ホーミングはショップで解放。共通強化は全モードに乗算。", 13, UiKit.Text2);

        // ウォレット（右）
        long imp = _game?.Impression ?? 0;
        string impS = imp.ToString("N0");
        float popA = _walletPopT > 0 ? (float)(_walletPopT / 0.5) : 0f;
        int impSize = 24 + Mathf.RoundToInt(3f * popA);
        float numW = UiKit.TextW(UiKit.Mono, impS, impSize);
        // ラベル実幅から pill 幅を算出＝「浄化した心」と数値が桁伸びでも衝突しないよう動的化。
        // 構成: [左余白16 + 円16 + 余白6] ラベル [ギャップ16] 数値 [右余白18]
        float lblW = UiKit.TextW(UiKit.Zen, "浄化した心", 12);
        float pillW = 16f + 16f + 6f + lblW + 16f + numW + 18f;
        float pillX = W - PadX - pillW, pillY = 30f;
        UiKit.Box(this, new Rect2(pillX, pillY, pillW, 44f), new Color(232 / 255f, 196 / 255f, 90 / 255f, 0.1f), 14f, new Color(UiKit.Gold, 0.4f), 1f);
        DrawCircle(new Vector2(pillX + 22, pillY + 22), 8f, UiKit.Gold);
        UiKit.Text(this, UiKit.Zen, new Vector2(pillX + 38, pillY + 14), "浄化した心", 12, new Color("f0d98a"));
        if (popA > 0) UiKit.RadialGlow(this, new Vector2(pillX + pillW - 24 - numW / 2f, pillY + 22), 50f, UiKit.Gold, 0.45f * popA);
        Color impCol = new Color("f0d98a").Lerp(UiKit.White, popA);
        UiKit.Text(this, UiKit.Mono, new Vector2(pillX + pillW - 18 - numW, pillY + 22 - impSize / 2f - popA * 1.5f), impS, impSize, impCol);
    }

    private void DrawModeStrip()
    {
        float x = PadX, y = 96f, w = W - PadX * 2, h = 42f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(15 / 255f, 11 / 255f, 26 / 255f, 0.7f), 13f, new Color(1, 1, 1, 0.1f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 16, y + h / 2f - 8), "ショットモード", 13, UiKit.Text2);

        float cx = x + 132f;
        for (int i = 0; i < ModeCount; i++)
        {
            var m = Modes[i];
            bool unlocked = _game?.IsModeUnlocked(m) ?? false;
            bool equipped = _game?.SelectedShotMode == m;
            string name = _game?.ShotModeName(m) ?? "";
            float chipW = 34f + UiKit.TextW(UiKit.ZenBold, name, 14);
            var r = new Rect2(cx, y + 7, chipW, h - 14);
            if (equipped) UiKit.Box(this, r, new Color(UiKit.Info, 0.22f), 999f, UiKit.Info, 1.2f);
            else UiKit.Box(this, r, new Color(1, 1, 1, 0.05f), 999f, new Color(1, 1, 1, unlocked ? 0.12f : 0.06f), 1f);
            DrawModeIcon(new Vector2(cx + 15, y + h / 2f), i, unlocked ? (equipped ? UiKit.PurifyHi : UiKit.Info) : UiKit.Text4);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(cx + 26, y + h / 2f - 8), name, 14, unlocked ? (equipped ? UiKit.White : UiKit.Text2) : UiKit.Text4);
            cx += chipW + 8f;
        }

        // 過熱トグル
        bool ol = _overloadPreview;
        float olW = 92f;
        var olr = new Rect2(cx + 6, y + 7, olW, h - 14);
        UiKit.Box(this, olr, ol ? new Color(Orange, 0.85f) : new Color(Orange, 0.10f), 999f, new Color(Orange, ol ? 0.9f : 0.4f), 1f);
        DrawCircle(new Vector2(cx + 20, y + h / 2f), 4.5f, ol ? UiKit.White : Orange);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(cx + 30, y + h / 2f - 8), "過熱 " + (ol ? "ON" : "OFF"), 12, ol ? UiKit.White : new Color("ff9a78"));

        // 右：過熱の循環/プレビュー操作子（Pad 経由＝表示モードに追従）。
        string olTok = Pad.ModeToken;
        UiKit.Key(this, new Vector2(x + w - 168, y + h / 2f - 13), olTok, new Color(1, 1, 1, 0.07f), new Color(1, 1, 1, 0.16f), UiKit.Text2);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + w - 140, y + h / 2f - 8), "過熱プレビュー", 12, UiKit.Text3);
    }

    private void DrawModeIcon(Vector2 c, int idx, Color col)
    {
        switch (idx)
        {
            case 0: // 連射＝三本の横線
                for (int k = -1; k <= 1; k++)
                    DrawLine(c + new Vector2(-6, k * 3.5f), c + new Vector2(6, k * 3.5f), col, 1.6f);
                break;
            case 1: // 拡散＝扇状の三本
                for (int k = -1; k <= 1; k++)
                {
                    float a = k * 0.5f;
                    DrawLine(c + new Vector2(-5, 0), c + new Vector2(-5 + Mathf.Cos(a) * 11, Mathf.Sin(a) * 11), col, 1.6f);
                }
                break;
            default: // ホーミング＝弧＋標的点
                DrawArc(c, 6f, Mathf.Pi * 0.2f, Mathf.Pi * 1.6f, 20, col, 1.6f, true);
                DrawCircle(c + new Vector2(5, -4), 2.2f, col);
                break;
        }
    }

    private void DrawModeCard(int i)
    {
        float x = CardX(i), y = CardsY, w = CardW, h = CardH;
        var m = Modes[i];
        string up = ModeUpId[i];
        bool locked = !(_game?.IsModeUnlocked(m) ?? false);
        bool equipped = _game?.SelectedShotMode == m;
        bool focus = _sel == i;
        int lv = string.IsNullOrEmpty(up) ? 0 : (_game?.GetUpgradeLevel(up) ?? 0);
        var def = string.IsNullOrEmpty(up) ? null : GameManager.GetUpgradeDef(up);
        int maxLv = def?.MaxLevel ?? 0;

        Color border = focus ? UiKit.Info : (equipped ? new Color(UiKit.Info, 0.6f) : new Color(1, 1, 1, 0.1f));
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(20 / 255f, 16 / 255f, 30 / 255f, 0.55f), 16f, border, focus ? 1.8f : 1.1f);
        if (focus) UiKit.Box(this, new Rect2(x - 2, y - 2, w + 4, h + 4), null, 18f, new Color(UiKit.Info, 0.5f), 1.5f);

        float ix = x + 14f, iw = w - 28f;

        // 射撃プレビュー（詳細パネル追加ぶんの圧縮＝100→72。挙動は同一）
        DrawModeField(ix, y + 14, iw, 72f, i, locked);

        // ヘッダ：アイコン＋名前＋EN＋装備中
        float hy = y + 96f;
        DrawModeIcon(new Vector2(ix + 9, hy + 9), i, UiKit.Info);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 24, hy), _game?.ShotModeName(m) ?? "", 19, UiKit.White);
        float nmW = UiKit.TextW(UiKit.ZenBlack, _game?.ShotModeName(m) ?? "", 19);
        UiKit.Text(this, UiKit.Mono, new Vector2(ix + 30 + nmW, hy + 5), ModeEn[i], 10, UiKit.Text3);
        if (equipped)
        {
            string b = "装備中";
            float bw = UiKit.TextW(UiKit.Mono, b, 10) + 18;
            UiKit.Box(this, new Rect2(x + w - 14 - bw, hy - 2, bw, 18f), new Color(UiKit.Info, 0.18f), 6f, new Color(UiKit.Info, 0.6f), 1f);
            DrawCircle(new Vector2(x + w - 14 - bw + 9, hy + 7), 3f, UiKit.Info);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 14 - bw + 16, hy + 2), b, 10, UiKit.PurifyHi);
        }

        // 説明（最大2行に制限し、下要素への食い込みを防ぐ）
        UiKit.Multi(this, UiKit.Zen, new Vector2(ix, hy + 24), ModeDesc[i], 12, UiKit.Text3, iw, 2);

        // スペックグリッド（3セル）
        float gy = hy + 60f, cellW = (iw - 16f) / 3f;
        (string, string, Color)[] stats = StatsFor(i, lv);
        for (int s = 0; s < 3; s++)
        {
            float cxp = ix + s * (cellW + 8f);
            UiKit.Box(this, new Rect2(cxp, gy, cellW, 40f), new Color(0, 0, 0, 0.24f), 9f);
            UiKit.Text(this, UiKit.Mono, new Vector2(cxp + 8, gy + 6), stats[s].Item1, 9, UiKit.Text3);
            UiKit.Text(this, UiKit.Mono, new Vector2(cxp + 8, gy + 18), stats[s].Item2, 14, stats[s].Item3);
        }

        // レベル行（ダイヤル＋現在→次の効果）or 連射＝初期解放。
        // スペックグリッド下端(gy+40)とボタン上端(ay)の中間に置き、両者と重ならないよう配置。
        float ay = y + h - 44f;
        float ly = (gy + 40f + ay) / 2f - 6f;  // グリッド下端とボタン上端の中央付近
        if (string.IsNullOrEmpty(up))
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ly), "初期解放・常時使用可", 12, new Color("7ec880"));
        }
        else
        {
            UiKit.Text(this, UiKit.Mono, new Vector2(ix, ly + 2), locked ? "解放Lv" : "Lv", 10, UiKit.Text3);
            DrawDial(new Vector2(ix + 50, ly + 6), 13f, lv, maxLv, UiKit.Info, i);
            // 効果ラベル：現在→次（買うと何が変わるか）を直接見せる。TextW で実幅を測り右寄せ。
            string eff = ModeEffect(i, lv) + (lv < maxLv ? " → " + ModeEffect(i, lv + 1) : "");
            UiKit.Text(this, UiKit.Zen, new Vector2(x + w - 14 - UiKit.TextW(UiKit.Zen, eff, 12), ly), eff, 12, new Color("a6dcec"));
        }
        DrawModeButtons(ix, ay, iw, i, locked, equipped, lv, maxLv, up);
    }

    // モード強化のレベル別効果（拡散=way数／ホーミング=追尾数）。GameManager のテーブルと一致させる。
    private static string ModeEffect(int i, int lv) => i switch
    {
        1 => lv == 0 ? "未解放" : new[] { 0, 5, 7, 9 }[Mathf.Clamp(lv, 0, 3)] + "way",
        2 => lv == 0 ? "未解放" : new[] { 0, 2, 2, 3 }[Mathf.Clamp(lv, 0, 3)] + "体追尾",
        _ => "",
    };

    private (string, string, Color)[] StatsFor(int i, int lv)
    {
        switch (i)
        {
            case 0:
                int lines = Mathf.Clamp(2 + (_game?.GetUpgradeLevel("shot_power") ?? 0) / 2, 2, 4);
                return new[] { ("弾速", "360", Light), ("段数", lines + "段", UiKit.White), ("威力", "×1.0", Light) };
            case 1:
                string n = lv == 0 ? "—" : _game!.SpreadWays + "way";
                return new[] { ("弾速", "320", Light), ("本数", n, UiKit.White), ("威力", "×0.8", new Color("a6dcec")) };
            default:
                string sh = lv == 0 ? "—" : _game!.HomingShots + "体";
                return new[] { ("弾速", "260", Light), ("追尾", sh, UiKit.White), ("間隔", "×1.15", Magenta) };
        }
    }

    private void DrawModeButtons(float x, float y, float w, int i, bool locked, bool equipped, int lv, int maxLv, string up)
    {
        bool hasBuy = !string.IsNullOrEmpty(up);
        float selW = w * 0.46f, gap = 8f;
        float buyX = x, buyW = w;
        if (true) // 装備ボタン（連射含め全モードに表示）
        {
            var r = new Rect2(x, y, selW, 36f);
            Color bg = equipped ? new Color(UiKit.Info, 0.18f) : new Color(1, 1, 1, 0.05f);
            Color bd = equipped ? UiKit.Info : new Color(1, 1, 1, 0.16f);
            UiKit.Box(this, r, bg, 10f, bd, 1f);
            string lab = locked ? "未解放" : (equipped ? "装備中" : Pad.EquipToken + " 装備");
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + 10), lab, 13,
                locked ? UiKit.Text4 : (equipped ? UiKit.PurifyHi : UiKit.Text2), HorizontalAlignment.Center, selW);
            buyX = x + selW + gap; buyW = w - selW - gap;
        }
        if (hasBuy)
        {
            bool maxed = lv >= maxLv;
            bool can = !maxed && (_game?.CanPurchase(up) ?? false);
            string label = locked ? Pad.ConfirmToken + " 解放" : (maxed ? "MAX" : Pad.ConfirmToken + " 強化");
            // コスト＝「浄化した心」。心アイコン(♥)＋数値で示す（◈から差し替え）。
            string cost = maxed ? "" : "♥" + (_game?.GetUpgradeCost(up) ?? 0).ToString("N0");
            Color bg = can ? UiKit.Info : new Color(1, 1, 1, 0.05f);
            Color tx = can ? UiKit.White : (maxed ? new Color("c9b6ef") : UiKit.Text4);
            UiKit.Box(this, new Rect2(buyX, y, buyW, 36f), bg, 10f, can ? new Color(0, 0, 0, 0) : new Color(1, 1, 1, 0.12f), can ? 0f : 1f);
            float tcx = buyX + 12;
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tcx, y + 10), label, 13, tx);
            if (cost.Length > 0)
                UiKit.Text(this, UiKit.Mono, new Vector2(buyX + buyW - 12 - UiKit.TextW(UiKit.Mono, cost, 13), y + 10), cost, 13, tx);
        }
        else
        {
            // 連射：購入枠なし → 装備ボタンを右まで広げる代わりに案内
            UiKit.Box(this, new Rect2(buyX, y, buyW, 36f), new Color(1, 1, 1, 0.04f), 10f);
            UiKit.Text(this, UiKit.Zen, new Vector2(buyX, y + 10), "強化不要", 12, UiKit.Text3, HorizontalAlignment.Center, buyW);
        }
    }

    // モード別の射撃プレビュー（ミナ＋流れる光弾。連射=直線/拡散=扇/ホーミング=曲射）。
    private void DrawModeField(float x, float y, float w, float h, int i, bool locked)
    {
        UiKit.Box(this, new Rect2(x, y, w, h), new Color("0a1020"), 12f, new Color(1, 1, 1, 0.08f), 1f);
        UiKit.RadialGlow(this, new Vector2(x + w * 0.08f, y + h / 2f), w * 0.4f, UiKit.Info, 0.14f);
        for (float yy = y; yy < y + h; yy += 3f) DrawRect(new Rect2(x, yy, w, 1f), new Color(0, 0, 0, 0.16f));

        float t = (float)_t;

        // 射撃リズム（モード別）に同期した微リコイル＋アンティシペーション。
        // 各発射サイクルで「タメ（前傾・わずか前進）→発射の反動（後方へキック）→余韻（戻し）」。
        float cycle = i == 0 ? 0.7f : i == 1 ? 1.0f : 1.3f;       // DrawLightBullet の位相と同周期
        float fp = (t / cycle) % 1f;                               // 0..1 発射位相
        float recoil;                                             // +で後方(左)へ引く
        if (fp < 0.12f) recoil = -Mathf.Lerp(0f, 2f, fp / 0.12f); // タメ：わずか前傾(前進)
        else if (fp < 0.30f) recoil = Mathf.Lerp(-2f, 5f, (fp - 0.12f) / 0.18f); // 発射：後方へキック
        else recoil = Mathf.Lerp(5f, 0f, (fp - 0.30f) / 0.70f);   // 余韻：ゆっくり戻す
        float breath = Mathf.Sin(t * 2.6f) * 4f;
        Vector2 mina = new(x + 28 - recoil, y + h / 2f + breath);

        if (!locked)
        {
            // 光弾の発射口は突き出した右手のあたり（mina中心より右寄り・腕の高さで少し上）。
            float x0 = mina.X + 30, x1 = x + w - 8;
            Vector2 muzzle = new(x0, mina.Y - 3f);
            switch (i)
            {
                case 0: // 連射：直線レーン
                    int lines = Mathf.Clamp(2 + (_game?.GetUpgradeLevel("shot_power") ?? 0) / 2, 2, 4);
                    float[] offs = lines <= 2 ? new[] { -5f, 5f } : lines == 3 ? new[] { -8f, 0f, 8f } : new[] { -10f, -4f, 4f, 10f };
                    foreach (float dy in offs)
                        for (int k = 0; k < 4; k++)
                        {
                            float ph = (t / 0.7f + k / 4f) % 1f;
                            DrawLightBullet(new Vector2(Mathf.Lerp(x0, x1, ph), mina.Y + dy), 4f, ph);
                        }
                    break;
                case 1: // 拡散：扇状
                    int n = Mathf.Max(5, _game?.SpreadWays ?? 5);
                    for (int b = 0; b < n; b++)
                    {
                        float tt = n == 1 ? 0f : (float)b / (n - 1) - 0.5f;
                        float ang = tt * Mathf.DegToRad(56f);
                        float ph = (t / 1.0f + b * 0.06f) % 1f;
                        Vector2 dir = new(Mathf.Cos(ang), Mathf.Sin(ang));
                        DrawLightBullet(muzzle + dir * (ph * (w * 0.72f)), 3.5f, ph);
                    }
                    break;
                default: // ホーミング：標的へ曲射
                    Vector2[] tg = { new(x + w - 26, y + 20), new(x + w - 20, y + h - 18) };
                    int shots = Mathf.Max(2, _game?.HomingShots ?? 2);
                    foreach (var tp in tg)
                    {
                        DrawCircle(tp, 7f, new Color(0.27f, 0.09f, 0.2f));
                        DrawArc(tp, 8f, 0, Mathf.Tau, 18, new Color(UiKit.Kegare, 0.6f), 1.2f, true);
                    }
                    for (int s = 0; s < shots; s++)
                    {
                        var tp = tg[s % tg.Length];
                        float ph = (t / 1.3f + s * 0.22f) % 1f;
                        float e = ph * ph * (3f - 2f * ph); // smoothstep
                        Vector2 mid = new(muzzle.X + (tp.X - muzzle.X) * 0.5f, muzzle.Y);
                        Vector2 p = QuadBezier(muzzle, mid, tp, e);
                        DrawLightBullet(p, 3.5f, ph);
                    }
                    break;
            }
        }

        // ミナ：右へ撃つ射撃ポーズの立ち絵（呼吸揺れ＋微リコイルは mina 座標に反映済み）。
        UiKit.RadialGlow(this, mina, 22f, UiKit.Mina, 0.6f);
        if (_minaShot != null)
        {
            float dh = Mathf.Min(h - 12f, 86f);
            float dw = dh * _minaShot.GetWidth() / _minaShot.GetHeight();
            // 体の中心を mina に合わせ、突き出した右手（テクスチャ右端寄り）が発射口側へ来るよう配置。
            var dst = new Rect2(mina.X - dw * 0.5f, mina.Y - dh * 0.5f, dw, dh);
            DrawTextureRect(_minaShot, dst, false);
        }
        else
        {
            // フォールバック（テクスチャ未ロード時）。
            DrawCircle(mina, 11f, UiKit.Mina);
            DrawCircle(mina - new Vector2(2, 3), 4f, new Color(1, 1, 1, 0.9f));
        }

        if (locked)
        {
            DrawRect(new Rect2(x, y, w, h), new Color(8 / 255f, 6 / 255f, 14 / 255f, 0.66f));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + h / 2f - 8), "ショップで解放", 13, UiKit.Text2, HorizontalAlignment.Center, w);
        }
    }

    private static Vector2 QuadBezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    private void DrawLightBullet(Vector2 p, float r, float ph)
    {
        float a = ph < 0.12f ? ph / 0.12f : (ph > 0.86f ? (1f - ph) / 0.14f : 1f);
        UiKit.RadialGlow(this, p, r * 2.4f, Light, 0.55f * a);
        DrawCircle(p, r, new Color(Light, a));
        DrawCircle(p - new Vector2(r * 0.3f, r * 0.3f), r * 0.4f, new Color(1, 1, 1, a));
    }

    // ───────────────────────── 共通強化グリッド ─────────────────────────
    // 12種を「攻撃／生存／応援」の色タグ付き2列で一覧。行は名前・Lvピップ・コストだけに絞り、
    // 「何がどう変わるか」は右の詳細パネルに寄せる（一覧=見つける／詳細=決める、の分業）。
    private void DrawCoreGrid()
    {
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(CoreX, SecY), "共通強化", 18, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(CoreX + 96, SecY + 6), "全モードに作用", 12, UiKit.Text3);
        // カテゴリ凡例（色タグの意味）
        float lx = CoreX + 220;
        for (int c = 0; c < CatName.Length; c++)
        {
            DrawRect(new Rect2(lx, SecY + 8, 10f, 10f), new Color(CatCol[c], 0.9f));
            UiKit.Text(this, UiKit.Zen, new Vector2(lx + 15, SecY + 5), CatName[c], 12, UiKit.Text3);
            lx += 15 + UiKit.TextW(UiKit.Zen, CatName[c], 12) + 18f;
        }

        long imp = _game?.Impression ?? 0;
        for (int g = 0; g < CoreItems.Length; g++)
        {
            var (id, cat) = CoreItems[g];
            var d = GameManager.GetUpgradeDef(id);
            if (d == null) continue;
            int lv = _game?.GetUpgradeLevel(id) ?? 0;
            bool maxed = lv >= d.MaxLevel;
            long cost = maxed ? -1 : (_game?.GetUpgradeCost(id) ?? 0);
            bool can = !maxed && cost >= 0 && imp >= cost;
            bool focus = _sel == ModeCount + g;
            var r = GridCellRect(g);

            UiKit.Box(this, r, new Color(22 / 255f, 18 / 255f, 34 / 255f, focus ? 0.75f : 0.5f), 9f,
                focus ? UiKit.Info : new Color(1, 1, 1, 0.07f), focus ? 1.8f : 1f);
            // カテゴリ色タグ（左端の縦バー）
            DrawRect(new Rect2(r.Position.X + 3, r.Position.Y + 5, 3f, r.Size.Y - 10), new Color(CatCol[cat], 0.9f));

            // 名前（買えるものは白＝“いま買える”が一覧で拾える。買えない/MAXは沈める）
            UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X + 14, r.Position.Y + 6), d.Name, 14,
                maxed ? UiKit.Text4 : (can ? UiKit.White : UiKit.Text3));

            // Lvピップ（MaxLevel 個の点、lv 分を充填）
            float px = r.Position.X + 150, py = r.Position.Y + r.Size.Y / 2f;
            for (int p = 0; p < d.MaxLevel; p++)
            {
                bool filled = p < lv;
                DrawCircle(new Vector2(px + p * 11f, py), 3.2f,
                    filled ? new Color(CatCol[cat], 0.95f) : new Color(1, 1, 1, 0.14f));
            }

            // 右端：コスト or MAX
            string tag = maxed ? "MAX" : "♥" + cost.ToString("N0");
            Color tagCol = maxed ? new Color("c9b6ef") : (can ? UiKit.Gold : UiKit.Text4);
            UiKit.Text(this, UiKit.Mono, new Vector2(r.Position.X + r.Size.X - 12 - UiKit.TextW(UiKit.Mono, tag, 13), r.Position.Y + 6), tag, 13, tagCol);

            // 購入直後の充填グロー
            if (_buyFxT > 0 && _buyFxItem == ModeCount + g)
            {
                float a = (float)(_buyFxT / 0.7);
                UiKit.Box(this, r, null, 9f, new Color(CatCol[cat], 0.8f * a), 2f);
            }
        }
    }

    // ───────────────────────── 詳細パネル（フォーカス連動） ─────────────────────────
    // 「いま何を選んでいて、買うと何がどう変わり、いくら残るか」を1箇所に集約（旧バランス早見表を置き換え）。
    private void DrawDetailPanel()
    {
        float x = BalX, y = SecY, w = W - PadX - BalX;
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, y), "つぎの一手", 18, UiKit.White);
        float by = y + 30f, bh = GridY0 + GridRows * GridPitch - 4f - by;
        UiKit.Box(this, new Rect2(x, by, w, bh), new Color(20 / 255f, 16 / 255f, 30 / 255f, 0.5f), 12f, new Color(UiKit.Info, 0.25f), 1f);

        // フォーカス対象の情報を集める（モードカード or 共通強化）。
        string name, desc, cur, next;
        int lv, maxLv, cat;
        long cost;
        if (_sel < ModeCount)
        {
            string up = ModeUpId[_sel];
            name = _game?.ShotModeName(Modes[_sel]) ?? "";
            desc = ModeDesc[_sel];
            cat = 0;
            if (string.IsNullOrEmpty(up)) { lv = 1; maxLv = 1; cost = -1; cur = "初期解放"; next = ""; }
            else
            {
                var d = GameManager.GetUpgradeDef(up)!;
                lv = _game?.GetUpgradeLevel(up) ?? 0; maxLv = d.MaxLevel;
                cost = lv >= maxLv ? -1 : (_game?.GetUpgradeCost(up) ?? 0);
                cur = ModeEffect(_sel, lv); next = lv >= maxLv ? "" : ModeEffect(_sel, lv + 1);
            }
        }
        else
        {
            var (id, c) = CoreItems[_sel - ModeCount];
            var d = GameManager.GetUpgradeDef(id)!;
            name = d.Name; desc = d.Desc; cat = c;
            lv = _game?.GetUpgradeLevel(id) ?? 0; maxLv = d.MaxLevel;
            cost = lv >= maxLv ? -1 : (_game?.GetUpgradeCost(id) ?? 0);
            cur = Eff(id, lv); next = lv >= maxLv ? "" : Eff(id, lv + 1);
        }

        float ix = x + 16f, iw = w - 32f;
        // 名前＋カテゴリタグ＋Lv
        DrawRect(new Rect2(ix, by + 16, 4f, 18f), new Color(CatCol[cat], 0.9f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 12, by + 12), name, 19, UiKit.White);
        string lvS = $"Lv {lv}/{maxLv}";
        UiKit.Text(this, UiKit.Mono, new Vector2(ix + iw - UiKit.TextW(UiKit.Mono, lvS, 12), by + 18), lvS, 12, UiKit.Text3);
        // 説明
        UiKit.Multi(this, UiKit.Zen, new Vector2(ix, by + 40), desc, 12, UiKit.Text2, iw, 2);

        // 現在 → 購入後（差分を大きく＝意思決定の中心）
        float ey = by + 78f;
        UiKit.Box(this, new Rect2(ix, ey, iw, 44f), new Color(0, 0, 0, 0.24f), 9f);
        if (next.Length > 0)
        {
            UiKit.Text(this, UiKit.Mono, new Vector2(ix + 12, ey + 6), "いま", 10, UiKit.Text3);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 12, ey + 18), cur, 15, UiKit.Text2);
            float aw = UiKit.TextW(UiKit.ZenBold, cur, 15);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 24 + aw, ey + 16), "▸", 17, new Color(CatCol[cat], 0.9f));
            UiKit.Text(this, UiKit.Mono, new Vector2(ix + 44 + aw, ey + 6), "買うと", 10, new Color(CatCol[cat], 0.8f));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 44 + aw, ey + 18), next, 15, UiKit.White);
        }
        else
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 12, ey + 14), cur + (maxLv > 1 ? "（最大強化済み）" : ""), 14, UiKit.Text2);
        }

        // コスト行：値段・購入後の残り・買えない理由（押す前に全部わかる）
        float cy = ey + 54f;
        long imp = _game?.Impression ?? 0;
        if (cost >= 0)
        {
            string costS = "♥" + cost.ToString("N0");
            UiKit.Text(this, UiKit.Mono, new Vector2(ix, cy), costS, 16, imp >= cost ? UiKit.Gold : Deny);
            float cw2 = UiKit.TextW(UiKit.Mono, costS, 16);
            if (imp >= cost)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), $"買うと のこり ♥{(imp - cost):N0}", 12, UiKit.Text3);
            else
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), $"あと ♥{(cost - imp):N0} たりない", 12, Deny);
        }
        else if (_sel < ModeCount && string.IsNullOrEmpty(ModeUpId[_sel]))
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, cy + 2), "買うものはありません（" + Pad.EquipToken + " で装備）", 12, UiKit.Text3);
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, cy + 2), "これ以上は強化できません", 12, UiKit.Text3);
        }
    }

    // 共通強化のレベル別効果表示。ゲーム側の実計算式（GameManager）と必ず一致させる。
    private static string Eff(string id, int lv) => id switch
    {
        "shot_power" => $"威力 +{lv}",
        "fire_rate" => $"発射間隔 ×{Mathf.Max(0.4f, 1f - 0.08f * lv):0.00}",
        "option_sub" => $"オプション {lv}基",
        "max_life" => $"ライフ上限 +{lv}",
        "bomb_count" => $"初期ボム +{lv}",
        "bomb_power" => $"ボム範囲 ×{1f + 0.25f * lv:0.00}",
        "move_speed" => $"移動速度 ×{1f + 0.12f * lv:0.00}",
        "hitbox" => $"被弾判定 ×{Mathf.Max(0.4f, 1f - 0.12f * lv):0.00}",
        "contam_resist" => $"汚染上昇 ×{Mathf.Max(0f, 1f - 0.15f * lv):0.00}",
        "imp_mult" => $"獲得心 ×{1f + 0.12f * lv:0.00}",
        "fol_gain" => $"拡散 ×{1f + 0.15f * lv:0.00}",
        "combo_hold" => $"コンボ猶予 {2.0 + 0.4 * lv:0.0}秒",
        _ => $"Lv{lv}",
    };

    // レベルダイヤル：充填リング（-90°起点）＋中央のレベル数値。購入直後は外周グロー。
    private void DrawDial(Vector2 c, float r, int lv, int max, Color col, int item)
    {
        DrawArc(c, r, 0, Mathf.Tau, 32, new Color(1, 1, 1, 0.12f), 3f, true);
        float frac = max > 0 ? (float)lv / max : 0f;
        if (frac > 0) DrawArc(c, r, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * frac, 32, col, 3f, true);
        if (_buyFxT > 0 && _buyFxItem == item)
        {
            float a = (float)(_buyFxT / 0.7);
            DrawArc(c, r + 3f, 0, Mathf.Tau, 32, new Color(col, 0.7f * a), 2f, true);
        }
        UiKit.Text(this, UiKit.Mono, new Vector2(c.X - r, c.Y - 8), lv.ToString(), 13, lv > 0 ? col : UiKit.Text3, HorizontalAlignment.Center, r * 2);
    }

    // 購入バースト：光のフラッシュ＋スパーク環。
    private void DrawBuyFx()
    {
        if (_buyFxT <= 0) return;
        float p = 1f - (float)(_buyFxT / 0.7);
        float a = 1f - p;
        UiKit.RadialGlow(this, _buyFxAt, 80f * (0.5f + p), Light, 0.5f * a);
        const int n = 10;
        for (int i = 0; i < n; i++)
        {
            float ang = Mathf.Tau * i / n;
            float rr = 14f + p * 84f;
            DrawCircle(_buyFxAt + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr, 2.8f * (1f - p), new Color(1, 1, 1, a));
        }
    }

    // モードスウィープ（装備切替時、中央に MODE ▸ 名称が横切る）。
    private void DrawModeSweep()
    {
        if (_sweepT <= 0) return;
        float k = 1f - (float)(_sweepT / 1.1);    // 0→1 進行
        float a = k < 0.18f ? k / 0.18f : (k > 0.82f ? (1f - k) / 0.18f : 1f);
        float slide = (k - 0.5f) * 220f;          // 横切り
        string t = "MODE ▸ " + _sweepName;
        float tw = UiKit.TextW(UiKit.ZenBlack, t, 32) + 90;
        float x = W / 2f - tw / 2f + slide, y = 330f;
        UiKit.Box(this, new Rect2(x, y, tw, 60f), new Color(0.06f, 0.11f, 0.16f, 0.92f * a), 16f, new Color(UiKit.Info, 0.6f * a), 1.4f);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, y + 14), t, 32, new Color(UiKit.PurifyHi, a), HorizontalAlignment.Center, tw);
    }

    // 過熱オーバーロード全画面オーバーレイ（プレビュー）。
    private void DrawOverloadOverlay()
    {
        if (!_overloadPreview) return;
        float pulse = 0.18f + 0.10f * Mathf.Sin((float)_t * 6f);
        // 左から差し込む橙のヴェール
        UiKit.RadialGlow(this, new Vector2(W * 0.12f, H * 0.5f), W * 0.6f, Orange, pulse);
        DrawRect(new Rect2(0, 0, W, H), new Color(0.0f, 0, 0, 0)); // no-op keep order
        // 縁の発光
        UiKit.Box(this, new Rect2(6, 6, W - 12, H - 12), null, 8f, new Color(Orange, 0.4f), 6f);
        // 発動フラッシュ
        if (_olFlashT > 0)
        {
            float a = (float)(_olFlashT / 0.9);
            UiKit.RadialGlow(this, new Vector2(W * 0.16f, H * 0.5f), W * 0.8f, new Color("ffd2b4"), 0.85f * a);
        }
        // バッジ
        float bw = 280f, bx = W / 2f - bw / 2f, by = 92f;
        UiKit.Box(this, new Rect2(bx, by, bw, 38f), new Color(40 / 255f, 12 / 255f, 8 / 255f, 0.86f), 999f, new Color(Orange, 0.8f), 1.2f);
        DrawCircle(new Vector2(bx + 24, by + 19), 5f, UiKit.White);
        UiKit.Text(this, UiKit.Mono, new Vector2(bx + 38, by + 12), "OVERLOAD", 15, new Color("ffd9c4"));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(bx + bw - 150, by + 12), "発射間隔 0.07s", 13, new Color("ff9a78"));
    }

    private float Hint(float x, float y, string key, string label, bool accent)
    {
        Color kbg = accent ? new Color(UiKit.Info, 0.12f) : new Color(1, 1, 1, 0.07f);
        Color kbd = accent ? new Color(UiKit.Info, 0.5f) : new Color(1, 1, 1, 0.16f);
        UiKit.Key(this, new Vector2(x, y - 12), key, kbg, kbd, accent ? UiKit.PurifyHi : UiKit.Text2);
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + kw + 8, y - 8), label, 14, accent ? UiKit.Info : UiKit.Text3);
        return x + kw + 8 + UiKit.TextW(UiKit.Zen, label, 14) + 22f;
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        float w = UiKit.TextW(UiKit.ZenBold, _toast, 16) + 48;
        float x = (W - w) / 2f;
        UiKit.Box(this, new Rect2(x, H - 96, w, 38f), new Color(0.06f, 0.05f, 0.10f, 0.96f), 12f, new Color(_toastCol, 0.7f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, H - 88), _toast, 16, _toastCol, HorizontalAlignment.Center, w);
    }
}
