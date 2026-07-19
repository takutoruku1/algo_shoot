using Godot;

// Shop : ミナ強化ショップ。スキルツリー型レイアウト（モードカード型から改装）。
//   上：ヘッダ＋ウォレット ／ ショットモード切替ストリップ（R0・フォーカス可能な装備チップ＋過熱トグル）
//   中：スキルツリー＝3幹（連射／拡散／ホーミング）×3段。段1-2 は前提なし（配置＝関連表示のみ）、
//       段3 はカプストーン（奥義）で各1条件の前提ロック。前提未達セルには理由を常時表示。
//       前提未達でも所持済み Lv は没収しない（次の Lv 購入時のみ判定＝グランドファーザー規則）。
//   下：共通強化帯（前提なし・全モードに乗算）＝生存・経済系 7 ノードの 2 行。
//   右：詳細パネル「つぎの一手」＝射撃プレビュー（旧モードカード3枚ぶんを1面に集約）＋
//       現在値→購入後値・コスト・購入後の残り・買えない理由/前提（押す前に全部見せる＝桜井流）。
//   演出：購入バースト・ウォレットpop・モードスウィープ・過熱フラッシュ・フロンティア金パルス
//        （おすすめ∩いま買える）・カプストーン解放パルス。
//   操作：↑↓←→ えらぶ／Z 購入(解放/強化)／C 装備(その幹のモード)／V 過熱プレビュー／X もどる。
public partial class Shop : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

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

    // ───── ツリー構成（行×列 → 強化ID）─────
    // R1-R3＝幹（列0=連射/1=拡散/2=ホーミング）。縦に 段1→段2→段3(奥義)。
    // R4-R5＝共通帯（前提なし・全モード）。
    private static readonly string[][] TreeIds =
    {
        new[] { "fire_rate",   "shot_spread", "shot_homing" },   // R1 段1
        new[] { "shot_power",  "fol_gain",    "hitbox" },        // R2 段2
        new[] { "shot_pierce", "option_sub",  "counter_light" }, // R3 段3（カプストーン）
    };
    private static readonly string[] Row4Ids = { "max_life", "bomb_count", "bomb_power", "move_speed" };
    private static readonly string[] Row5Ids = { "contam_resist", "imp_mult", "combo_hold" };
    private static readonly string[] CapstoneIds = { "shot_pierce", "option_sub", "counter_light" };

    // おすすめ（迷ったらこれ）：進行（ステージクリア）に連動して“次の一手”を段階的に指す道しるべ。
    //   初期＝体感しやすい基礎3種 → STAGE1後＝新モード解放＋ボム → STAGE2後＝生存の質 → STAGE3後＝経済。
    //   表示は「フロンティア強調」＝おすすめ ∩ いま買えるノードの枠を金でパルス。
    private string[] RecommendedNow() =>
        _game == null ? new[] { "shot_power", "fire_rate", "max_life" }
        : _game.IsStageCleared("koharu") ? new[] { "imp_mult", "fol_gain" }
        : _game.IsStageCleared("akari") ? new[] { "hitbox", "bomb_power" }
        : _game.IsStageCleared("rei") ? new[] { "shot_spread", "shot_homing", "bomb_count" }
        : new[] { "shot_power", "fire_rate", "max_life" };
    private string[] _recommended = System.Array.Empty<string>(); // _Draw 冒頭で毎フレーム更新（描画中は不変）

    // カテゴリ色（詳細パネル・共通帯の左タグ）。0=攻撃 / 1=生存 / 2=応援。
    private static readonly Color[] CatCol = { new("9be0f5"), new("7ec880"), new("f0d98a") };
    private static int CatFor(string id) => id switch
    {
        "max_life" or "bomb_count" or "bomb_power" or "move_speed" or "hitbox" or "contam_resist" => 1,
        "imp_mult" or "fol_gain" or "combo_hold" => 2,
        _ => 0,
    };

    private static readonly Color Light = new("9be0f5");   // 光のハイライト
    private static readonly Color Orange = new("ff8a5a");  // 過熱
    private static readonly Color Deny = new("ef9a9a");    // 買えない理由（赤）

    // 射撃プレビューのミナ立ち絵（右へ撃つポーズ）。毎フレームLoadしないよう_Readyで一度だけキャッシュ。
    private Texture2D? _minaShot;

    // フォーカス（仮想6行グリッド）：R0=モードストリップ3 / R1-R3=幹3列 / R4=共通4列 / R5=共通3列。
    // ←→は行内wrap、↑↓は列記憶（_colMem）つき行移動・上下端で循環。
    private int _row, _col, _colMem;
    private static int ColsOf(int row) => row == 4 ? 4 : 3;

    // 入力エッジ
    private bool _navHeld, _zHeld, _equipHeld, _olHeld, _backHeld;
    private double _t, _toastT;
    private string _toast = "";
    private Color _toastCol = UiKit.Info;
    private bool _autoplay;

    // 演出タイマー
    private double _buyFxT;       // 購入バースト
    private double _walletPopT;   // ウォレットpop
    private string _buyFxId = ""; // 購入したノード（セルの充填グロー）
    private Vector2 _buyFxAt;     // バースト発生源
    private double _sweepT;       // モードスウィープ
    private string _sweepName = "";
    private bool _overloadPreview;
    private double _olFlashT;     // 過熱発動フラッシュ

    // カプストーン解放パルス（前提が成立した瞬間に一度だけ「解放!」）。
    private readonly System.Collections.Generic.HashSet<string> _capSeen = new();
    private string _capPulseId = "";
    private double _capPulseT;

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        // 起動時、装備中モードのチップ（R0）にカーソルを合わせる。
        _row = 0;
        _col = System.Array.IndexOf(Modes, _game?.SelectedShotMode ?? GameManager.ShotMode.Rapid);
        if (_col < 0) _col = 0;
        _colMem = _col;
        _minaShot = ResourceLoader.Load<Texture2D>("res://char/mina_shoot.png");
        // 既に前提成立済みのカプストーンは「解放!」パルスの対象外（入店時点の状態を既知とする）。
        foreach (var id in CapstoneIds)
            if (_game?.IsPrereqMet(id) ?? false) _capSeen.Add(id);
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
        if (_capPulseT > 0) _capPulseT -= delta;
        if (_autoplay) { ExitShop(); return; }

        // カプストーンの前提がこの画面内で成立した瞬間（例：光の出力 Lv2 を購入）に一度だけ解放パルス。
        foreach (var id in CapstoneIds)
            if ((_game?.IsPrereqMet(id) ?? false) && _capSeen.Add(id))
            { _capPulseId = id; _capPulseT = 1.4; }

        // ポーズメニュー（Esc で重なる）を閉じた Esc/Z の同じ押下がこのフレームに漏れて
        // 「もどる＝ショップごと閉じる」「購入」が誤発火しないよう、ゲート中は全キーを既押し扱いで食う。
        if (Pad.UiBlocked(this))
        {
            _navHeld = _zHeld = _equipHeld = _olHeld = _backHeld = true;
            QueueRedraw();
            return;
        }

        // カーソル移動：十字で移動（6行グリッド）。
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

        // Z：購入（ノードの解放/強化）。R0（装備チップ）では装備に回す。
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2) OnConfirm();

        // C：装備（フォーカス列の幹のモードを選択）。共通帯では対象がないので案内トースト。
        bool c = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        bool cEdge = c && !_equipHeld; _equipHeld = c;
        if (cEdge && _t > 0.2)
        {
            if (_row <= 3) EquipMode(_col);
            else { Audio.Instance?.PlayUiDeny(); Toast("共通強化は全モードで有効です（装備は幹の列で）", UiKit.Text4); }
        }

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

    // 十字ナビ：←→は行内wrap、↑↓は列記憶つきで行移動（上下端は循環）。
    // 列数が違う行へ移るときは記憶列（_colMem）を行の列数にクランプ＝「同じ縦筋」の感覚を保つ。
    private void Nav(int dx, int dy)
    {
        if (dx != 0)
        {
            int n = ColsOf(_row);
            _col = (_col + dx + n) % n;
            _colMem = _col;
        }
        if (dy != 0)
        {
            _row = (_row + dy + 6) % 6;
            _col = Mathf.Min(_colMem, ColsOf(_row) - 1);
        }
    }

    // フォーカス中のノードID（R0 は装備チップ＝shot_spread/shot_homing の定義を流用。連射は ""）。
    private string FocusId() => _row switch
    {
        0 => ModeUpId[_col],
        >= 1 and <= 3 => TreeIds[_row - 1][_col],
        4 => Row4Ids[_col],
        _ => Row5Ids[_col],
    };

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
        if (_row == 0) { EquipMode(_col); return; } // R0 チップ＝装備（解放はツリーの段1で）
        Buy(FocusId(), CellRect(_row, _col).GetCenter());
    }

    private void Buy(string id, Vector2 at)
    {
        var d = GameManager.GetUpgradeDef(id);
        if (d == null || _game == null) return;
        int lv = _game.GetUpgradeLevel(id);
        if (lv >= d.MaxLevel) { Audio.Instance?.PlayUiDeny(); Toast("すでに最大です", UiKit.Text4); return; }
        // 前提未達（カプストーンのみ）：理由を明示して拒否。所持済み Lv には触れない（グランドファーザー規則）。
        if (!_game.IsPrereqMet(id))
        {
            string pn = GameManager.GetUpgradeDef(d.PrereqId)?.Name ?? d.PrereqId;
            Audio.Instance?.PlayUiDeny(); Toast($"前提: {pn} Lv{d.PrereqLv} が必要です", Deny); return;
        }
        if (!_game.CanPurchase(id)) { Audio.Instance?.PlayUiDeny(); Toast("浄化した心が足りません", Deny); return; }
        if (_game.TryPurchase(id))
        {
            Audio.Instance?.PlayUiBuy(); // 購入成功＝達成音
            string label = lv == 0 && (id == "shot_spread" || id == "shot_homing") ? "解放" : "強化";
            Toast($"{d.Name} を{label}！  Lv {_game.GetUpgradeLevel(id)}", UiKit.Info);
            _buyFxT = 0.7; _walletPopT = 0.5; _buyFxId = id; _buyFxAt = at;
            // 拡散/ホーミングを解放したら自動で装備に切り替える（従来挙動を踏襲）。
            if (lv == 0 && id == "shot_spread") EquipMode(1, silent: true);
            if (lv == 0 && id == "shot_homing") EquipMode(2, silent: true);
        }
    }

    private void EquipMode(int idx, bool silent = false)
    {
        var m = Modes[idx];
        if (!(_game?.IsModeUnlocked(m) ?? false)) { if (!silent) { Audio.Instance?.PlayUiDeny(); Toast("まだ解放されていません（幹の段1で解放）", UiKit.Text4); } return; }
        if (_game!.SelectedShotMode == m && !silent) { return; }
        if (!silent) Audio.Instance?.PlayUiConfirm(); // 装備＝決定音
        _game.SelectedShotMode = m;
        _sweepName = _game.ShotModeName(m);
        _sweepT = 1.1;
    }

    private void Toast(string msg, Color col) { _toast = msg; _toastCol = col; _toastT = 1.8; }

    // ───────────────────────── レイアウト座標 ─────────────────────────
    private const float PadX = 40f;
    private const float StripY = 96f, StripH = 42f;                 // R0 モードストリップ
    private const float TrunkY = 150f, TrunkHeadH = 48f;            // 幹ヘッダ y150-198
    private const float TrunkW = 278f, TrunkGap = 18f;              // 幹列 x=40/336/632
    private static float TrunkX(int c) => PadX + c * (TrunkW + TrunkGap);
    private static readonly float[] TierY = { 206f, 290f, 374f };   // R1-R3（段1/段2/段3）
    private const float TierH = 56f;
    private const float CommonLabelY = 442f;                        // 共通帯の見出し
    private const float CommonY0 = 474f, CommonY1 = 512f, CommonH = 34f; // R4 / R5
    private const float CommonW = 204f, CommonPitch = 218f;         // x=40/258/476/694
    private const float DetailX = 930f, DetailW = 310f;             // 詳細パネル x930-1240 / y150-556

    // 行×列 → セル矩形（R0 はストリップのチップ矩形を使う）。
    private static Rect2 CellRect(int row, int col) => row switch
    {
        >= 1 and <= 3 => new Rect2(TrunkX(col), TierY[row - 1], TrunkW, TierH),
        4 => new Rect2(PadX + col * CommonPitch, CommonY0, CommonW, CommonH),
        _ => new Rect2(PadX + col * CommonPitch, CommonY1, CommonW, CommonH),
    };

    // R0 装備チップの矩形（幅は名前の実幅から。ストリップ描画とフォーカス枠が共有する）。
    private float ChipW(int i) => 34f + UiKit.TextW(UiKit.ZenBold, _game?.ShotModeName(Modes[i]) ?? "", 14);
    private Rect2 ChipRect(int i)
    {
        float cx = PadX + 132f;
        for (int k = 0; k < i; k++) cx += ChipW(k) + 8f;
        return new Rect2(cx, StripY + 7f, ChipW(i), StripH - 14f);
    }

    // ───────────────────────── 描画 ─────────────────────────
    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        _recommended = RecommendedNow(); // 進行連動おすすめ（フロンティア金パルスの対象）

        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0d0b1c"), new Color("0a0916"), new Color("070611") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.12f, H * 0.42f), 460f, UiKit.Info, 0.10f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        DrawHeader();
        DrawModeStrip();
        DrawTree();
        DrawCommonBand();
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
        UiKit.Text(this, UiKit.Zen, new Vector2(PadX, 72), "幹を伸ばして型を強く。段3の奥義は前提つき。共通強化は全モードに乗算。", 13, UiKit.Text2);

        // 長期目標（LUNATIC解放）＝「何のために稼ぐか」の遠い灯り。条件は GameManager.IsLunaticUnlocked
        //（フォロワー200 or 光の出力Lv4＝ツリー側にも王冠マークで重ねる）。解放済みなら出さない。
        if (_game != null && !_game.IsLunaticUnlocked)
        {
            string goal = $"LUNATIC解放まで: フォロワー {_game.Followers}/{GameManager.LunaticFollowerReq} ／ 光の出力 Lv{_game.GetUpgradeLevel("shot_power")}/4";
            UiKit.Text(this, UiKit.Zen, new Vector2(W - PadX - UiKit.TextW(UiKit.Zen, goal, 11), 80), goal, 11, new Color("c9b6ef"));
        }

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

    // R0：装備チップ（フォーカス可能）＋過熱トグル。装備は Z/C の1押し。
    private void DrawModeStrip()
    {
        float x = PadX, y = StripY, w = W - PadX * 2, h = StripH;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(15 / 255f, 11 / 255f, 26 / 255f, 0.7f), 13f, new Color(1, 1, 1, 0.1f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 16, y + h / 2f - 8), "ショットモード", 13, UiKit.Text2);

        for (int i = 0; i < 3; i++)
        {
            var m = Modes[i];
            bool unlocked = _game?.IsModeUnlocked(m) ?? false;
            bool equipped = _game?.SelectedShotMode == m;
            bool focus = _row == 0 && _col == i;
            string name = _game?.ShotModeName(m) ?? "";
            var r = ChipRect(i);
            if (equipped) UiKit.Box(this, r, new Color(UiKit.Info, 0.22f), 999f, UiKit.Info, 1.2f);
            else UiKit.Box(this, r, new Color(1, 1, 1, 0.05f), 999f, new Color(1, 1, 1, unlocked ? 0.12f : 0.06f), 1f);
            if (focus) UiKit.Box(this, r.Grow(3f), null, 999f, new Color(UiKit.Info, 0.85f), 1.6f);
            DrawModeIcon(new Vector2(r.Position.X + 15, y + h / 2f), i, unlocked ? (equipped ? UiKit.PurifyHi : UiKit.Info) : UiKit.Text4);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X + 26, y + h / 2f - 8), name, 14, unlocked ? (equipped ? UiKit.White : UiKit.Text2) : UiKit.Text4);
        }

        // 過熱トグル
        float cx = ChipRect(2).End.X + 8f;
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

    // ───────────────────────── スキルツリー（幹ヘッダ＋段1-3＋エッジ） ─────────────────────────
    private void DrawTree()
    {
        for (int c = 0; c < 3; c++)
        {
            DrawTrunkHeader(c);

            float cx = TrunkX(c) + TrunkW / 2f;
            // エッジ：段1→段2 は点線（関連表示のみ・ロックなし）。
            DrawDottedV(cx, TierY[0] + TierH, TierY[1], new Color(1, 1, 1, 0.22f));
            // エッジ：段2→段3 は実線＋錠（前提未達）。前提成立で金の実線に変わる。
            string capId = TreeIds[2][c];
            bool capOpen = _game?.IsPrereqMet(capId) ?? false;
            Color edgeCol = capOpen ? new Color(UiKit.Gold, 0.6f) : new Color(1, 1, 1, 0.28f);
            DrawLine(new Vector2(cx, TierY[1] + TierH), new Vector2(cx, TierY[2]), edgeCol, 2f);
            if (!capOpen) DrawLockIcon(new Vector2(cx, (TierY[1] + TierH + TierY[2]) / 2f), 7f, new Color(1, 1, 1, 0.6f));

            for (int r = 0; r < 3; r++)
                DrawTreeCell(TreeIds[r][c], CellRect(r + 1, c), _row == r + 1 && _col == c);
        }
    }

    private void DrawTrunkHeader(int c)
    {
        float x = TrunkX(c), y = TrunkY;
        var m = Modes[c];
        bool unlocked = _game?.IsModeUnlocked(m) ?? (c == 0);
        bool equipped = _game?.SelectedShotMode == m;
        UiKit.Box(this, new Rect2(x, y, TrunkW, TrunkHeadH), new Color(20 / 255f, 16 / 255f, 30 / 255f, 0.6f), 12f,
            equipped ? new Color(UiKit.Info, 0.7f) : new Color(1, 1, 1, 0.10f), equipped ? 1.6f : 1f);
        DrawModeIcon(new Vector2(x + 22, y + TrunkHeadH / 2f), c, unlocked ? UiKit.Info : UiKit.Text4);
        string name = _game?.ShotModeName(m) ?? "";
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 38, y + 10), name, 19, unlocked ? UiKit.White : UiKit.Text3);
        float nmW = UiKit.TextW(UiKit.ZenBlack, name, 19);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 44 + nmW, y + 17), ModeEn[c], 10, UiKit.Text3);

        if (equipped)
        {
            string b = "装備中";
            float bw = UiKit.TextW(UiKit.Mono, b, 10) + 18;
            UiKit.Box(this, new Rect2(x + TrunkW - 14 - bw, y + 15, bw, 18f), new Color(UiKit.Info, 0.18f), 6f, new Color(UiKit.Info, 0.6f), 1f);
            DrawCircle(new Vector2(x + TrunkW - 14 - bw + 9, y + 24), 3f, UiKit.Info);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + TrunkW - 14 - bw + 16, y + 19), b, 10, UiKit.PurifyHi);
        }
        else
        {
            string tail = unlocked ? Pad.EquipToken + " 装備" : "未解放";
            UiKit.Text(this, UiKit.Zen, new Vector2(x + TrunkW - 14 - UiKit.TextW(UiKit.Zen, tail, 11), y + 17), tail, 11, UiKit.Text4);
        }
    }

    // 幹ノードセル（278×56）：1行目=名前＋Lvピップ、2行目=コスト or 前提。前提未達は理由を常時表示。
    private void DrawTreeCell(string id, Rect2 r, bool focus)
    {
        var d = GameManager.GetUpgradeDef(id);
        if (d == null) return;
        int lv = _game?.GetUpgradeLevel(id) ?? 0;
        bool maxed = lv >= d.MaxLevel;
        long cost = maxed ? -1 : (_game?.GetUpgradeCost(id) ?? 0);
        bool prereqOk = _game?.IsPrereqMet(id) ?? true;
        long imp = _game?.Impression ?? 0;
        bool can = !maxed && prereqOk && cost >= 0 && imp >= cost;

        UiKit.Box(this, r, new Color(22 / 255f, 18 / 255f, 34 / 255f, focus ? 0.8f : 0.55f), 10f,
            focus ? UiKit.Info : new Color(1, 1, 1, 0.08f), focus ? 1.8f : 1f);

        float x = r.Position.X, y = r.Position.Y;

        // 名前（買えるものは白＝“いま買える”が一覧で拾える。買えない/MAXは沈める）
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 14, y + 7), d.Name, 15,
            maxed ? UiKit.Text4 : (can ? UiKit.White : UiKit.Text3));
        float nw = UiKit.TextW(UiKit.ZenBold, d.Name, 15);

        // 王冠（shot_power：Lv4＝LUNATIC解放条件のひとつ）。
        if (id == "shot_power")
            DrawCrown(new Vector2(x + 14 + nw + 16, y + 17), 7f, lv >= 4 ? UiKit.Gold : new Color(UiKit.Gold, 0.5f));

        // 「全モード」チップ（fol_gain/hitbox＝幹に置くが効果は全モード共通）。
        if (id is "fol_gain" or "hitbox")
        {
            const string am = "全モード";
            float aw = UiKit.TextW(UiKit.Zen, am, 10) + 12f;
            var ar = new Rect2(x + 14 + nw + 10, y + 8f, aw, 16f);
            UiKit.Box(this, ar, new Color(1, 1, 1, 0.06f), 999f, new Color(1, 1, 1, 0.18f), 1f);
            UiKit.Text(this, UiKit.Zen, new Vector2(ar.Position.X, y + 9f), am, 10, UiKit.Text3, HorizontalAlignment.Center, aw);
        }

        // Lvピップ（右上：MaxLevel 個、lv ぶん充填）
        float px = r.End.X - 14 - d.MaxLevel * 11f, py = y + 16f;
        for (int p = 0; p < d.MaxLevel; p++)
            DrawCircle(new Vector2(px + p * 11f + 4f, py), 3.4f,
                p < lv ? new Color(UiKit.Info, 0.95f) : new Color(1, 1, 1, 0.14f));

        // 2行目：前提未達なら理由を常時表示、それ以外はコスト or MAX。
        float ly = y + 32f;
        if (!prereqOk)
        {
            string pn = GameManager.GetUpgradeDef(d.PrereqId)?.Name ?? d.PrereqId;
            DrawLockIcon(new Vector2(x + 20, ly + 9f), 6f, new Color(Deny, 0.9f));
            UiKit.Text(this, UiKit.Zen, new Vector2(x + 32, ly), $"前提: {pn} Lv{d.PrereqLv}（いま Lv{_game?.GetUpgradeLevel(d.PrereqId) ?? 0}）", 11, Deny);
        }
        else if (maxed)
        {
            UiKit.Text(this, UiKit.Mono, new Vector2(x + 14, ly), "MAX", 12, new Color("c9b6ef"));
        }
        else
        {
            string costS = "♥" + cost.ToString("N0");
            UiKit.Text(this, UiKit.Mono, new Vector2(x + 14, ly), costS, 13, can ? UiKit.Gold : UiKit.Text4);
            if (lv > 0)
                UiKit.Text(this, UiKit.Zen, new Vector2(x + 22 + UiKit.TextW(UiKit.Mono, costS, 13), ly + 1), $"→ Lv{lv + 1}", 10, UiKit.Text4);
        }

        DrawCellFx(id, r, lv, can);
    }

    // フロンティア金パルス（おすすめ∩いま買える）／購入直後グロー／カプストーン解放パルス。
    private void DrawCellFx(string id, Rect2 r, int lv, bool can)
    {
        if (lv == 0 && can && System.Array.IndexOf(_recommended, id) >= 0)
        {
            float pulse = 0.35f + 0.35f * Mathf.Sin((float)_t * 4f);
            UiKit.Box(this, r, null, 10f, new Color(UiKit.Gold, pulse), 1.6f);
            const string rec = "おすすめ";
            float rw = UiKit.TextW(UiKit.Zen, rec, 10) + 12f;
            var rr = new Rect2(r.End.X - rw - 8f, r.Position.Y - 8f, rw, 16f);
            UiKit.Box(this, rr, new Color(UiKit.Gold, 0.2f), 999f, new Color(UiKit.Gold, 0.7f), 1f);
            UiKit.Text(this, UiKit.Zen, new Vector2(rr.Position.X, rr.Position.Y + 1f), rec, 10, new Color("f0d98a"), HorizontalAlignment.Center, rw);
        }
        if (_buyFxT > 0 && _buyFxId == id)
        {
            float a = (float)(_buyFxT / 0.7);
            UiKit.Box(this, r, null, 10f, new Color(UiKit.Info, 0.8f * a), 2f);
        }
        if (_capPulseT > 0 && _capPulseId == id)
        {
            float k = 1f - (float)(_capPulseT / 1.4);
            float a = 1f - k;
            UiKit.Box(this, r.Grow(2f + 8f * k), null, 12f, new Color(UiKit.Gold, 0.8f * a), 2f);
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(r.Position.X, r.Position.Y - 26f), "解放!", 16, new Color(UiKit.Gold, a), HorizontalAlignment.Center, r.Size.X);
        }
    }

    // 縦の点線（段1→段2 の関連エッジ）。
    private void DrawDottedV(float x, float y0, float y1, Color col)
    {
        for (float yy = y0 + 2f; yy < y1 - 2f; yy += 8f)
            DrawLine(new Vector2(x, yy), new Vector2(x, Mathf.Min(yy + 4f, y1 - 2f)), col, 1.6f);
    }

    // 小さな錠前（段3 の前提ロック合図）。
    private void DrawLockIcon(Vector2 c, float s, Color col)
    {
        DrawArc(new Vector2(c.X, c.Y - s * 0.25f), s * 0.45f, Mathf.Pi, Mathf.Tau, 10, col, 1.4f, true);
        UiKit.Box(this, new Rect2(c.X - s * 0.6f, c.Y - s * 0.2f, s * 1.2f, s * 0.95f), col, 2f);
    }

    // 小さな王冠（shot_power＝Lv4 で LUNATIC 解放条件、の印）。凹多角形は使わず矩形＋三角3枚で描く。
    private void DrawCrown(Vector2 c, float s, Color col)
    {
        DrawRect(new Rect2(c.X - s, c.Y, s * 2f, s * 0.55f), col);
        DrawColoredPolygon(new[] { new Vector2(c.X - s, c.Y), new Vector2(c.X - s, c.Y - s * 0.8f), new Vector2(c.X - s * 0.34f, c.Y) }, col);
        DrawColoredPolygon(new[] { new Vector2(c.X - s * 0.33f, c.Y), new Vector2(c.X, c.Y - s * 0.95f), new Vector2(c.X + s * 0.33f, c.Y) }, col);
        DrawColoredPolygon(new[] { new Vector2(c.X + s * 0.34f, c.Y), new Vector2(c.X + s, c.Y - s * 0.8f), new Vector2(c.X + s, c.Y) }, col);
    }

    // ───────────────────────── 共通強化帯（前提なし・全モード） ─────────────────────────
    private void DrawCommonBand()
    {
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(PadX, CommonLabelY), "共通強化", 16, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(PadX + 88, CommonLabelY + 4), "全モードに乗算・前提なし", 12, UiKit.Text3);

        for (int i = 0; i < Row4Ids.Length; i++)
            DrawCommonCell(Row4Ids[i], CellRect(4, i), _row == 4 && _col == i);
        for (int i = 0; i < Row5Ids.Length; i++)
            DrawCommonCell(Row5Ids[i], CellRect(5, i), _row == 5 && _col == i);
    }

    // 共通ノードセル（204×34・1行）：名前＋Lvピップ＋コスト。
    private void DrawCommonCell(string id, Rect2 r, bool focus)
    {
        var d = GameManager.GetUpgradeDef(id);
        if (d == null) return;
        int lv = _game?.GetUpgradeLevel(id) ?? 0;
        bool maxed = lv >= d.MaxLevel;
        long cost = maxed ? -1 : (_game?.GetUpgradeCost(id) ?? 0);
        long imp = _game?.Impression ?? 0;
        bool can = !maxed && cost >= 0 && imp >= cost;
        int cat = CatFor(id);

        UiKit.Box(this, r, new Color(22 / 255f, 18 / 255f, 34 / 255f, focus ? 0.8f : 0.5f), 9f,
            focus ? UiKit.Info : new Color(1, 1, 1, 0.07f), focus ? 1.8f : 1f);
        DrawRect(new Rect2(r.Position.X + 3, r.Position.Y + 5, 3f, r.Size.Y - 10), new Color(CatCol[cat], 0.9f));

        UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X + 12, r.Position.Y + 8), d.Name, 13,
            maxed ? UiKit.Text4 : (can ? UiKit.White : UiKit.Text3));

        // Lvピップ
        float px = r.Position.X + 118, py = r.Position.Y + r.Size.Y / 2f;
        for (int p = 0; p < d.MaxLevel; p++)
            DrawCircle(new Vector2(px + p * 10f, py), 3f,
                p < lv ? new Color(CatCol[cat], 0.95f) : new Color(1, 1, 1, 0.14f));

        // 右端：コスト or MAX
        string tag = maxed ? "MAX" : "♥" + cost.ToString("N0");
        Color tagCol = maxed ? new Color("c9b6ef") : (can ? UiKit.Gold : UiKit.Text4);
        UiKit.Text(this, UiKit.Mono, new Vector2(r.End.X - 10 - UiKit.TextW(UiKit.Mono, tag, 11), r.Position.Y + 9), tag, 11, tagCol);

        DrawCellFx(id, r, lv, can);
    }

    // ───────────────────────── 詳細パネル「つぎの一手」（フォーカス連動） ─────────────────────────
    // 射撃プレビュー（旧モードカード3枚を1面に集約）＋「いま何を選んでいて、買うと何がどう変わり、
    // いくら残るか／なぜ買えないか」を1箇所に集約。前提・LUNATIC条件もここで種明かしする。
    private void DrawDetailPanel()
    {
        float x = DetailX, w = DetailW;
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, TrunkY), "つぎの一手", 18, UiKit.White);
        float by = TrunkY + 30f, bh = CommonY1 + CommonH + 10f - by; // ツリー〜共通帯の下端に揃える
        UiKit.Box(this, new Rect2(x, by, w, bh), new Color(20 / 255f, 16 / 255f, 30 / 255f, 0.5f), 12f, new Color(UiKit.Info, 0.25f), 1f);

        // 射撃プレビュー（フォーカス列の幹。共通帯では装備中モード）。
        int pv = _row <= 3 ? _col : System.Array.IndexOf(Modes, _game?.SelectedShotMode ?? GameManager.ShotMode.Rapid);
        if (pv < 0) pv = 0;
        bool pvLocked = !(_game?.IsModeUnlocked(Modes[pv]) ?? (pv == 0));
        DrawModeField(x + 10, by + 10, w - 20, 90, pv, pvLocked);
        string pvLabel = (_row <= 3 ? "" : "装備中: ") + (_game?.ShotModeName(Modes[pv]) ?? "");
        // ラベルは右上（左はミナ立ち絵が立つので隠れる）。
        UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 16 - UiKit.TextW(UiKit.Mono, pvLabel, 10), by + 14), pvLabel, 10, new Color(1, 1, 1, 0.55f));

        // フォーカス対象の情報を集める（R0 モードチップ or 強化ノード）。
        string id = FocusId();
        float ix = x + 16f, iw = w - 32f, iy = by + 112f;
        if (_row == 0 && string.IsNullOrEmpty(id))
        {
            // 連射チップ：買うものが無い＝装備の案内のみ。
            DrawDetailTitle(ix, iw, iy, "連射", 0, 1, 1);
            UiKit.Multi(this, UiKit.Zen, new Vector2(ix, iy + 28), ModeDesc[0], 12, UiKit.Text2, iw, 2);
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, iy + 74), "初期解放・常時使用可（" + Pad.EquipToken + " で装備）", 12, new Color("7ec880"));
            return;
        }

        var d = GameManager.GetUpgradeDef(id)!;
        int lv = _game?.GetUpgradeLevel(id) ?? 0;
        bool maxed = lv >= d.MaxLevel;
        long cost = maxed ? -1 : (_game?.GetUpgradeCost(id) ?? 0);
        bool prereqOk = _game?.IsPrereqMet(id) ?? true;
        long imp = _game?.Impression ?? 0;

        DrawDetailTitle(ix, iw, iy, d.Name, CatFor(id), lv, d.MaxLevel);
        UiKit.Multi(this, UiKit.Zen, new Vector2(ix, iy + 26), d.Desc, 12, UiKit.Text2, iw, 2);

        // 現在 → 購入後（差分＝意思決定の中心）。長い効果文が入るので上下2段で見せる。
        float ey = iy + 66f;
        UiKit.Box(this, new Rect2(ix, ey, iw, 56f), new Color(0, 0, 0, 0.24f), 9f);
        string cur = Eff(id, lv);
        UiKit.Text(this, UiKit.Mono, new Vector2(ix + 12, ey + 8), "いま", 10, UiKit.Text3);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 58, ey + 6), cur, 13, UiKit.Text2);
        if (!maxed)
        {
            UiKit.Text(this, UiKit.Mono, new Vector2(ix + 12, ey + 32), "買うと", 10, new Color(CatCol[CatFor(id)], 0.8f));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 58, ey + 30), Eff(id, lv + 1), 13, UiKit.White);
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 12, ey + 32), "最大強化済み", 12, new Color("c9b6ef"));
        }

        // コスト行：値段・購入後の残り・買えない理由（押す前に全部わかる）。
        float cy = ey + 68f;
        if (cost >= 0)
        {
            string costS = "♥" + cost.ToString("N0");
            bool afford = imp >= cost;
            UiKit.Text(this, UiKit.Mono, new Vector2(ix, cy), costS, 16, afford && prereqOk ? UiKit.Gold : Deny);
            float cw2 = UiKit.TextW(UiKit.Mono, costS, 16);
            if (!prereqOk)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), "前提が必要です（下記）", 12, Deny);
            else if (afford)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), $"買うと のこり ♥{(imp - cost):N0}", 12, UiKit.Text3);
            else
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), $"あと ♥{(cost - imp):N0} たりない", 12, Deny);
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, cy + 2), "これ以上は強化できません", 12, UiKit.Text3);
        }

        // 前提行（カプストーン）／LUNATIC 条件（shot_power）。
        float ny = cy + 26f;
        if (!string.IsNullOrEmpty(d.PrereqId))
        {
            string pn = GameManager.GetUpgradeDef(d.PrereqId)?.Name ?? d.PrereqId;
            int plv = _game?.GetUpgradeLevel(d.PrereqId) ?? 0;
            string pre = $"前提: {pn} Lv{d.PrereqLv}（いま Lv{plv}）";
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), pre, 12, prereqOk ? new Color("7ec880") : Deny);
            // グランドファーザー規則の注記：前提未達でも所持済み Lv は有効のまま。
            if (!prereqOk && lv > 0)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny + 20), $"所持済みの Lv{lv} は有効のままです", 11, UiKit.Text3);
        }
        else if (id == "shot_power")
        {
            DrawCrown(new Vector2(ix + 7, ny + 10), 6f, UiKit.Gold);
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 18, ny), "Lv4 で LUNATIC 解放条件のひとつを満たします", 11, new Color("c9b6ef"));
        }
        else if (_row == 0)
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), "解放・強化は下のツリー（段1）で", 11, UiKit.Text3);
        }
    }

    // 詳細パネルの見出し行（カテゴリ色タグ＋名前＋Lv）。
    private void DrawDetailTitle(float ix, float iw, float iy, string name, int cat, int lv, int maxLv)
    {
        DrawRect(new Rect2(ix, iy + 4, 4f, 18f), new Color(CatCol[cat], 0.9f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 12, iy), name, 19, UiKit.White);
        string lvS = $"Lv {lv}/{maxLv}";
        UiKit.Text(this, UiKit.Mono, new Vector2(ix + iw - UiKit.TextW(UiKit.Mono, lvS, 12), iy + 6), lvS, 12, UiKit.Text3);
    }

    // 強化のレベル別効果表示。ゲーム側の実計算式（GameManager/Player/Enemy）と必ず一致させる。
    // 澄んだ心は現在の汚染度での実効値を正直に出す（無汚染では Lv3 が伸びないことも見えるように）。
    private string Eff(string id, int lv) => id switch
    {
        "shot_power" => $"威力 +{lv}",
        "fire_rate" => $"発射間隔 ×{Mathf.Max(0.4f, 1f - 0.08f * lv):0.00}",
        "shot_spread" => lv == 0 ? "未解放" : $"{new[] { 0, 5, 7, 9 }[Mathf.Clamp(lv, 0, 3)]}way",
        "shot_homing" => lv == 0 ? "未解放" : $"{new[] { 0, 2, 2, 3 }[Mathf.Clamp(lv, 0, 3)]}体追尾",
        "shot_pierce" => lv == 0 ? "貫通なし" : $"連射弾が敵 {lv} 体を貫通",
        "counter_light" => lv == 0 ? "変換なし" : lv == 1 ? "2発に1発を光弾化（上限6/回避）" : "全弾を光弾化（上限12/回避）",
        "option_sub" => lv == 0 ? "オプションなし" : $"追従オプション {lv} 基（威力×0.5）",
        "max_life" => $"ライフ上限 +{lv}",
        "bomb_count" => $"初期ボム +{lv}",
        "bomb_power" => $"ボム直撃 {Mathf.RoundToInt(Enemy.BombStrikeBase * (1f + 0.25f * lv))}ダメージ",
        "move_speed" => $"移動×{1f + 0.12f * lv:0.00}・回避CD{0.8f - 0.1f * lv:0.0}s・{64 + 4 * lv}px",
        "hitbox" => $"被弾判定 ×{Mathf.Max(0.4f, 1f - 0.12f * lv):0.00}",
        "contam_resist" => $"汚染上昇 ×{Mathf.Max(0f, 1f - 0.15f * lv):0.00}・心の効率 ×{_game?.KindnessGainMulAt(lv) ?? 1f:0.00}",
        "imp_mult" => $"獲得心 ×{1f + 0.12f * lv:0.00}",
        "fol_gain" => $"口コミ ×{1f + 0.15f * lv:0.00}", // “拡散 ×N”はショットモード「拡散」と紛らわしいため別名（実体＝フォロワー獲得倍率）
        "combo_hold" => $"コンボ猶予 {2.0 + 0.4 * lv:0.0}秒",
        _ => $"Lv{lv}",
    };

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
                        float ang = tt * Mathf.DegToRad(70f); // 実弾道（Player.FireSpread ±35°＝全幅70°）と一致させる
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
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + h / 2f - 8), "ツリーで解放", 13, UiKit.Text2, HorizontalAlignment.Center, w);
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
