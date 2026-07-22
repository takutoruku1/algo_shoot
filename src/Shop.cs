using Godot;
using System.Collections.Generic;

// Shop : ミナ強化ショップ。二分木ディシジョンツリー型レイアウト（3幹並列スキルツリーから改装）。
//   上：ヘッダ＋ウォレット ／ ショットモード切替ストリップ（R0・装備チップ＋過熱トグル）
//   中：単一ルート「ミナの核」から 1→2→4→7→6 と二個ずつ広がる二分木。
//       攻めの系譜（連射速度→…）と支えの系譜（身のこなし→…）。親Lv≥1 でノードに入れる（続きLvは親不要）。
//       枝の末端は排他フォーク4対（⊗）＝どちらか一方だけ。片側を選ぶと相方は封印（振り直しで解除可）。
//       通常の二叉＝白灰の独立エッジ2本／排他フォーク＝金のY字束ね＋⊗メダル＋共通ブラケットで二重符号化。
//   右：詳細パネル「つぎの一手」＝射撃プレビュー・いま→買うと・コスト・前提/親/封印・振り直し差引き。
//   振り直し：封印側ノードで Z →「選び直す？」2段確認。返金100%−手数料20%（10単位切上げ・最低100）。
//   操作：↑↓←→ えらぶ（方向最近傍・エッジ接続先優先）／Z 購入/選び直し／C 装備／V 過熱／X もどる。
public partial class Shop : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    private static readonly GameManager.ShotMode[] Modes =
        { GameManager.ShotMode.Rapid, GameManager.ShotMode.Spread, GameManager.ShotMode.Homing };
    private static readonly string[] ModeEn = { "RAPID", "SPREAD", "HOMING" };
    private static readonly string[] ModeDesc =
    {
        "右へ直線の高速ストリーム。正面の硬い敵に火力集中。",
        "右方向へ扇状に展開。雑魚処理・面制圧・道中向き。",
        "右側のマゼンタの穢れへ吸い寄せられて曲射。避けながら削る。",
    };

    // ───── 二分木ノード配置（設計座標・セル左上）。W146×H44、深さ列 x: d1=168/d2=344/d3=520/d4=696 ─────
    private static readonly (string id, float x, float y)[] NodePos =
    {
        ("fire_rate",     168, 238), // 攻めの系譜（d1）
        ("shot_power",    344, 172),
        ("shot_pierce",   520, 146), // ⊗集中の光
        ("focus_fire",    520, 198), // ⊗貫く光
        ("shot_spread",   344, 305),
        ("fol_gain",      520, 268),
        ("option_sub",    696, 242), // ⊗連鎖の光
        ("chain_light",   696, 294), // ⊗拡散サブ
        ("combo_hold",    520, 342),
        ("move_speed",    168, 495), // 支えの系譜（d1）
        ("shot_homing",   344, 440),
        ("hitbox",        520, 440),
        ("counter_light", 696, 414), // ⊗祈りの帳
        ("veil_light",    696, 466), // ⊗返し光
        ("contam_resist", 344, 550),
        ("imp_mult",      520, 518),
        ("max_life",      520, 582),
        ("bomb_count",    696, 556), // ⊗ボム威力
        ("bomb_power",    696, 608), // ⊗ボム所持
    };
    private const float NodeW = 146f, NodeH = 44f;
    private static readonly Vector2 RootC = new(72f, 389f); // ルート「ミナの核」＝円形メダリオン
    private const float RootR = 34f;
    // 解放パルスの対象（前提つき奥義＝排他フォークの両翼すべて）。
    private static readonly string[] CapstoneIds =
        { "shot_pierce", "focus_fire", "option_sub", "chain_light", "counter_light", "veil_light" };

    // おすすめ（迷ったらこれ）：進行連動の道しるべ。表示はフロンティア強調＝おすすめ∩いま買えるを金パルス。
    // 親未接続で買えないおすすめは、親チェーンを遡って最初に買える祖先を代わりに光らせる（RebuildFrontier）。
    private string[] RecommendedNow() =>
        _game == null ? new[] { "shot_power", "fire_rate", "max_life" }
        : _game.IsStageCleared("koharu") ? new[] { "imp_mult", "fol_gain" }
        : _game.IsStageCleared("akari") ? new[] { "hitbox", "bomb_power" }
        : _game.IsStageCleared("rei") ? new[] { "shot_spread", "shot_homing", "bomb_count" }
        : new[] { "shot_power", "fire_rate", "max_life" };
    private string[] _recommended = System.Array.Empty<string>();
    private readonly HashSet<string> _frontier = new(); // _Draw 冒頭で毎フレーム更新

    // カテゴリ色（詳細パネルの色タグ）。0=攻撃 / 1=生存 / 2=応援。
    private static readonly Color[] CatCol = { new("9be0f5"), new("7ec880"), new("f0d98a") };
    private static int CatFor(string id) => id switch
    {
        "max_life" or "bomb_count" or "bomb_power" or "move_speed" or "hitbox" or "contam_resist" or "veil_light" => 1,
        "imp_mult" or "fol_gain" or "combo_hold" => 2,
        _ => 0,
    };

    // 系統アイデンティティ色：所持済みエッジ／育てた道の発光をこの色で塗る＝
    // 「自分が伸ばしてきた道」が系統ごとに違う光で読める（既存の CatCol と同一系統・色弱でも枝形状と二重符号化）。
    //   攻め＝連射/威力系はシアン、拡散/応援系はゴールド寄り、支え＝生存系はグリーン。
    private static readonly Color[] StreamCol = { new("9be0f5"), new("7ec880"), new("f0d98a") };
    private static Color StreamColor(string id) => StreamCol[CatFor(id)];

    private static readonly Color Light = new("9be0f5");   // 光のハイライト
    private static readonly Color Orange = new("ff8a5a");  // 過熱
    private static readonly Color Deny = new("ef9a9a");    // 買えない理由（赤）
    private static readonly Color EdgeDim = new(1, 1, 1, 0.22f);       // 通常エッジ（白灰）
    private static readonly Color ForkGold = new(0.91f, 0.77f, 0.35f); // 排他フォーク（金）

    // 射撃プレビューのミナ立ち絵（右へ撃つポーズ）。毎フレームLoadしないよう_Readyで一度だけキャッシュ。
    private Texture2D? _minaShot;

    // フォーカス：0..2=R0チップ / 3=root（ミナの核） / 4..=NodePos 順のノード。
    private int _sel;

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
    private readonly HashSet<string> _capSeen = new();
    private string _capPulseId = "";
    private double _capPulseT;

    // 振り直し（排他フォーク単点）の2段確認。封印側ノードで Z → 確認表示 → Z 確定 / X・カーソル移動で取消。
    private bool _respecArmed;
    private string _respecId = ""; // 封印側（＝選び直したい側）のノードID

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        // 起動時、装備中モードのチップ（R0）にカーソルを合わせる。
        _sel = System.Array.IndexOf(Modes, _game?.SelectedShotMode ?? GameManager.ShotMode.Rapid);
        if (_sel < 0) _sel = 0;
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

        // カーソル移動：十字で方向最近傍へ（エッジ接続先を第一候補）。移動で振り直し確認は取り消す。
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

        // Z：購入（解放/強化）。R0チップ＝装備、root＝説明、封印ノード＝選び直しフロー。
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2) OnConfirm();

        // C：装備。チップ＝そのモード、ノード＝その枝のモード（全モード系は案内トースト）。
        bool c = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        bool cEdge = c && !_equipHeld; _equipHeld = c;
        if (cEdge && _t > 0.2) OnEquipKey();

        // V：過熱オーバーロードのプレビュー切替（演出確認用）。
        bool v = Input.IsKeyPressed(Key.V) || Pad.Pressed(JoyButton.X);
        bool vEdge = v && !_olHeld; _olHeld = v;
        if (vEdge && _t > 0.2)
        {
            _overloadPreview = !_overloadPreview;
            if (_overloadPreview) _olFlashT = 0.9;
        }

        // X：振り直し確認中はその取消、通常はショップ退出。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2)
        {
            if (_respecArmed) { CancelRespec(); Audio.Instance?.PlayUiCancel(); }
            else { Audio.Instance?.PlayUiCancel(); ExitShop(); }
        }

        QueueRedraw();
    }

    // ───────────────────────── フォーカスとナビ ─────────────────────────
    private static Rect2 NodeRect(int i) => new(NodePos[i].x, NodePos[i].y, NodeW, NodeH);
    private static int SelOf(string id)
    {
        for (int i = 0; i < NodePos.Length; i++)
            if (NodePos[i].id == id) return i + 4;
        return -1;
    }
    private string? FocusNodeId => _sel >= 4 ? NodePos[_sel - 4].id : null;

    private Vector2 FocusCenter(int sel) => sel switch
    {
        <= 2 => ChipRect(sel).GetCenter(),
        3 => RootC,
        _ => NodeRect(sel - 4).GetCenter(),
    };

    // グラフ隣接（エッジ接続先）＝ナビの第一候補。チップは隣チップ、root は系譜の入り口2つ。
    private List<int> LinkedSels(int sel)
    {
        var l = new List<int>();
        if (sel <= 2)
        {
            if (sel > 0) l.Add(sel - 1);
            if (sel < 2) l.Add(sel + 1);
        }
        else if (sel == 3)
        {
            l.Add(SelOf("fire_rate"));
            l.Add(SelOf("move_speed"));
        }
        else
        {
            string id = NodePos[sel - 4].id;
            var d = GameManager.GetUpgradeDef(id);
            if (d != null)
            {
                l.Add(string.IsNullOrEmpty(d.ParentId) ? 3 : SelOf(d.ParentId));
                if (!string.IsNullOrEmpty(d.ExclusiveWith)) l.Add(SelOf(d.ExclusiveWith));
            }
            foreach (var nd in GameManager.Upgrades)
                if (nd.ParentId == id) l.Add(SelOf(nd.Id));
        }
        l.RemoveAll(v => v < 0);
        return l;
    }

    // 方向最近傍探索：押した方向の半平面から「前進距離＋直交ずれ×1.8」が最小の対象へ。
    // エッジ接続先（親/子/相方/チップ隣）はスコア×0.45 で優先＝木に沿って歩ける。
    private void Nav(int dx, int dy)
    {
        Vector2 from = FocusCenter(_sel);
        var linked = LinkedSels(_sel);
        int best = -1;
        float bestScore = float.MaxValue;
        int count = 4 + NodePos.Length;
        for (int s = 0; s < count; s++)
        {
            if (s == _sel) continue;
            Vector2 d = FocusCenter(s) - from;
            float forward = d.X * dx + d.Y * dy;
            if (forward < 4f) continue;
            float perp = Mathf.Abs(d.X * dy) + Mathf.Abs(d.Y * dx);
            float score = forward + perp * 1.8f;
            if (linked.Contains(s)) score *= 0.45f;
            if (score < bestScore) { bestScore = score; best = s; }
        }
        if (best >= 0) { _sel = best; CancelRespec(); }
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
        if (_sel <= 2) { EquipMode(_sel); return; } // R0 チップ＝装備
        if (_sel == 3) { Toast("ここからすべてが伸びます。線でつながった隣から買えますわ", UiKit.Info); return; }
        string id = NodePos[_sel - 4].id;
        // 封印中は購入の代わりに「選び直す？」フロー（2段確認）。
        if (_game?.IsSealed(id) ?? false) { RespecStep(id); return; }
        Buy(id, NodeRect(_sel - 4).GetCenter());
    }

    // 振り直しの2段確認。1回目のZ＝確認表示（詳細パネルに差引きプレビュー）、2回目のZ＝確定。
    private void RespecStep(string id)
    {
        var d = GameManager.GetUpgradeDef(id);
        if (d == null || _game == null || string.IsNullOrEmpty(d.ExclusiveWith)) return;
        if (!_respecArmed || _respecId != id)
        {
            _respecArmed = true;
            _respecId = id;
            Audio.Instance?.PlayUiMove();
            return;
        }
        // 確定：対の両ノードを Lv0 に戻し、返金−手数料をウォレットへ（RunImpression には足さない）。
        long refund = _game.RespecRefund(id, d.ExclusiveWith);
        long fee = _game.RespecFee(id, d.ExclusiveWith);
        if (_game.TryRespec(id, d.ExclusiveWith))
        {
            Audio.Instance?.PlayUiBuy();
            Toast($"選び直しました　＋♥{refund - fee:N0}（返金 ♥{refund:N0} − 手数料 ♥{fee:N0}）", UiKit.Info);
            _walletPopT = 0.5;
        }
        CancelRespec();
    }

    private void CancelRespec() { _respecArmed = false; _respecId = ""; }

    private void Buy(string id, Vector2 at)
    {
        var d = GameManager.GetUpgradeDef(id);
        if (d == null || _game == null) return;
        int lv = _game.GetUpgradeLevel(id);
        if (lv >= d.MaxLevel) { Audio.Instance?.PlayUiDeny(); Toast("すでに最大です", UiKit.Text4); return; }
        // 親未接続（ノードに入るときだけ）：どこから伸ばすかを明示して拒否。
        if (!_game.IsParentMet(id))
        {
            string pn = GameManager.GetUpgradeDef(d.ParentId)?.Name ?? "ミナの核";
            Audio.Instance?.PlayUiDeny(); Toast($"まず {pn} を Lv1 に（線でつながった親から）", Deny); return;
        }
        // 前提未達（奥義のみ）：理由を明示して拒否。所持済み Lv には触れない（グランドファーザー規則）。
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

    private void OnEquipKey()
    {
        if (_sel <= 2) { EquipMode(_sel); return; }
        // ノード上の C＝その枝のモードを装備。枝がモード固有でない（全モード系）なら案内。
        int m = PreviewModeFor(FocusNodeId);
        if (m < 0) { Audio.Instance?.PlayUiDeny(); Toast("この枝は全モードに効きます（装備は上のチップで）", UiKit.Text4); return; }
        EquipMode(m);
    }

    private void EquipMode(int idx, bool silent = false)
    {
        var m = Modes[idx];
        if (!(_game?.IsModeUnlocked(m) ?? false)) { if (!silent) { Audio.Instance?.PlayUiDeny(); Toast("まだ解放されていません（枝の入り口で解放）", UiKit.Text4); } return; }
        if (_game!.SelectedShotMode == m && !silent) { return; }
        if (!silent) Audio.Instance?.PlayUiConfirm(); // 装備＝決定音
        _game.SelectedShotMode = m;
        _sweepName = _game.ShotModeName(m);
        _sweepT = 1.1;
    }

    private void Toast(string msg, Color col) { _toast = msg; _toastCol = col; _toastT = 1.8; }

    // ノード → その枝のショットモード（詳細プレビューと C 装備用。-1＝モード固有でない＝装備中を映す）。
    private static int PreviewModeFor(string? id) => id switch
    {
        "fire_rate" or "shot_power" or "shot_pierce" or "focus_fire" => 0,
        "shot_spread" or "fol_gain" or "option_sub" or "chain_light" or "combo_hold" => 1,
        "shot_homing" or "hitbox" or "counter_light" or "veil_light" => 2,
        _ => -1,
    };

    // ───────────────────────── レイアウト座標 ─────────────────────────
    private const float PadX = 40f;
    private const float StripY = 96f, StripH = 42f;     // R0 モードストリップ
    private const float DetailX = 870f, DetailW = 370f; // 詳細パネル x870-1240
    private const float TreeTop = 146f, TreeBot = 652f; // ツリー領域 y146-652

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
        _recommended = RecommendedNow();
        RebuildFrontier(); // フロンティア強調（祖先フォールバック込み）を毎フレーム確定

        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0d0b1c"), new Color("0a0916"), new Color("070611") }, new[] { 0f, 0.55f, 1f });
        // 大きな放射光をゆっくり呼吸させる＝静止画に見えない下地の脈動（地味さ対策の核）。
        float bgBreath = 0.10f + 0.03f * Mathf.Sin((float)_t * 0.6f);
        UiKit.RadialGlow(this, new Vector2(W * 0.12f, H * 0.42f), 460f, UiKit.Info, bgBreath);
        DrawBgMotes();
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        DrawHeader();
        DrawModeStrip();
        DrawTree();
        DrawDetailPanel();

        // フッタ操作ヒント（ボタン表記は Pad に集約＝KB/PS/Xbox 切替に追従）。
        float fy = H - 34f, fx = PadX;
        fx = Hint(fx, fy, Pad.MoveToken, "えらぶ", false);
        fx = Hint(fx, fy, Pad.ConfirmToken, "購入/選び直し", true);
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
        UiKit.Text(this, UiKit.Zen, new Vector2(PadX, 72), "ミナの核から枝を伸ばす。⊗の枝はどちらか一方——心を払えば選び直せる。", 13, UiKit.Text2);

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
            bool focus = _sel == i;
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

    // ───────────────────────── 二分木ツリー描画 ─────────────────────────
    private void DrawTree()
    {
        // 1) エッジ（通常＝白灰の独立エッジ／排他＝金のY字束ね＋⊗メダル）。フォーク対は片側だけが描く（重複回避）。
        var forkDrawn = new HashSet<string>();
        foreach (var nd in GameManager.Upgrades)
        {
            int childSel = SelOf(nd.Id);
            if (childSel < 0) continue;
            Vector2 to = LeftCenter(NodeRect(childSel - 4));
            Vector2 from = string.IsNullOrEmpty(nd.ParentId)
                ? RootC + new Vector2(RootR, 0)
                : RightCenter(NodeRect(SelOf(nd.ParentId) - 4));

            if (!string.IsNullOrEmpty(nd.ExclusiveWith))
            {
                // 排他フォーク：対でまとめて1回だけ描く（Y字＋メダル＋ブラケット）。
                string key = string.CompareOrdinal(nd.Id, nd.ExclusiveWith) < 0 ? nd.Id : nd.ExclusiveWith;
                if (!forkDrawn.Add(key)) continue;
                DrawForkEdges(nd.Id, nd.ExclusiveWith, from);
            }
            else
            {
                // 通常エッジ：所持済みの枝は系統色で灯し、光が流れて「育てた道」が生きて見える。
                bool lit = (_game?.GetUpgradeLevel(nd.Id) ?? 0) >= 1;
                if (lit) DrawLitEdge(from, to, StreamColor(nd.Id), nd.Id);
                else DrawElbow(from, to, EdgeDim, 1.6f);
            }
        }

        // 2) root メダリオン「ミナの核」
        DrawRootMedallion();

        // 3) ノードセル
        for (int i = 0; i < NodePos.Length; i++)
            DrawNodeCell(NodePos[i].id, NodeRect(i), _sel == i + 4);
    }

    private static Vector2 LeftCenter(Rect2 r) => new(r.Position.X, r.Position.Y + r.Size.Y / 2f);
    private static Vector2 RightCenter(Rect2 r) => new(r.End.X, r.Position.Y + r.Size.Y / 2f);

    // L字エッジ（水平→垂直→水平）。二分木の枝分かれを配線図の語彙で読ませる。
    private void DrawElbow(Vector2 from, Vector2 to, Color col, float width)
    {
        float midX = (from.X + to.X) / 2f;
        DrawLine(from, new Vector2(midX, from.Y), col, width);
        DrawLine(new Vector2(midX, from.Y), new Vector2(midX, to.Y), col, width);
        DrawLine(new Vector2(midX, to.Y), to, col, width);
    }

    // 所持済みエッジ：系統色でL字を描き、その上を光の粒が流れる（根元→ノードへ「育てた道」が生きる）。
    //   ・下地に太く薄い同色グロウを重ねてコントラストを底上げ（地味さの主因＝発光不足への対処）。
    //   ・光の粒は id をシード化した位相で流し、全エッジが同時明滅しない（画面が呼吸して見える）。
    //   ・購入直後（_buyFxId==id）はその区間だけ強く伝播点灯＝解放が「線を伝って」広がる手触り。
    private void DrawLitEdge(Vector2 from, Vector2 to, Color col, string id)
    {
        float midX = (from.X + to.X) / 2f;
        Vector2 a = from, b = new(midX, from.Y), c = new(midX, to.Y), d = to;
        bool justBought = _buyFxT > 0 && _buyFxId == id;
        float boost = justBought ? (float)(_buyFxT / 0.7) : 0f;   // 1→0
        // 下地グロウ（太い半透明）＋本線。
        DrawLine(a, b, new Color(col, 0.10f + 0.30f * boost), 5f);
        DrawLine(b, c, new Color(col, 0.10f + 0.30f * boost), 5f);
        DrawLine(c, d, new Color(col, 0.10f + 0.30f * boost), 5f);
        float baseA = 0.5f + 0.4f * boost;
        DrawLine(a, b, new Color(col, baseA), 1.8f);
        DrawLine(b, c, new Color(col, baseA), 1.8f);
        DrawLine(c, d, new Color(col, baseA), 1.8f);
        // 流れる光の粒（区間長で正規化した位相を1つ流す）。位相は id ハッシュで散らす。
        float lenAB = Mathf.Abs(b.X - a.X), lenBC = Mathf.Abs(c.Y - b.Y), lenCD = Mathf.Abs(d.X - c.X);
        float total = lenAB + lenBC + lenCD;
        if (total < 1f) return;
        float seed = (Mathf.Abs(id.GetHashCode()) % 1000) / 1000f;
        float ph = Mathf.PosMod((float)_t * 0.35f + seed, 1f) * total;   // 進んだ距離
        Vector2 p = ph < lenAB ? a.Lerp(b, ph / Mathf.Max(1f, lenAB))
            : ph < lenAB + lenBC ? b.Lerp(c, (ph - lenAB) / Mathf.Max(1f, lenBC))
            : c.Lerp(d, (ph - lenAB - lenBC) / Mathf.Max(1f, lenCD));
        float dotA = 0.7f + 0.3f * boost;
        UiKit.RadialGlow(this, p, 9f, col, 0.45f * dotA);
        DrawCircle(p, 2.2f, new Color(1, 1, 1, dotA));
    }

    // 排他フォーク：親からの1本をY字の根元で束ね、⊗メダル＋対セルの共通ブラケット＋「どちらか一方」。
    // エッジ色（金）と束ね形状の二重符号化＝色弱でも判別できる。
    private void DrawForkEdges(string idA, string idB, Vector2 from)
    {
        int sa = SelOf(idA), sb = SelOf(idB);
        var ra = NodeRect(sa - 4);
        var rb = NodeRect(sb - 4);
        Vector2 ta = LeftCenter(ra), tb = LeftCenter(rb);
        float forkX = Mathf.Min(ta.X, tb.X) - 26f;
        Vector2 fork = new(forkX, (ta.Y + tb.Y) / 2f);

        bool chosen = (_game?.GetUpgradeLevel(idA) ?? 0) >= 1 || (_game?.GetUpgradeLevel(idB) ?? 0) >= 1;
        Color gold = new(ForkGold, chosen ? 0.85f : 0.55f);

        // 親→根元は1本に束ねる（通常の独立エッジとの見分けの本体）。
        DrawLine(from, new Vector2(forkX, from.Y), gold, 2f);
        DrawLine(new Vector2(forkX, from.Y), fork, gold, 2f);
        // 根元→両翼
        DrawLine(fork, ta, gold, 2f);
        DrawLine(fork, tb, gold, 2f);

        // ⊗メダル（金の円＋クロス）
        DrawCircle(fork, 9f, new Color(0.12f, 0.10f, 0.05f, 0.95f));
        DrawArc(fork, 9f, 0, Mathf.Tau, 24, gold, 1.6f, true);
        DrawLine(fork + new Vector2(-4, -4), fork + new Vector2(4, 4), gold, 1.6f);
        DrawLine(fork + new Vector2(-4, 4), fork + new Vector2(4, -4), gold, 1.6f);

        // 対の2セルを囲う薄金ブラケット＋ラベル「どちらか一方」。
        float top = Mathf.Min(ra.Position.Y, rb.Position.Y) - 5f;
        float bot = Mathf.Max(ra.End.Y, rb.End.Y) + 5f;
        var br = new Rect2(ra.Position.X - 5f, top, NodeW + 10f, bot - top);
        UiKit.Box(this, br, null, 12f, new Color(ForkGold, 0.35f), 1f);
        UiKit.Text(this, UiKit.Zen, new Vector2(br.Position.X + 8f, top - 13f), "どちらか一方", 9, new Color(ForkGold, 0.8f));
    }

    // ルート「ミナの核」：無償付与・購入不可。ツリーの説明アンカー（フォーカス可能）。
    private void DrawRootMedallion()
    {
        bool focus = _sel == 3;
        UiKit.RadialGlow(this, RootC, RootR * 2.2f, UiKit.Mina, focus ? 0.30f : 0.18f);
        DrawCircle(RootC, RootR, new Color(0.10f, 0.08f, 0.16f, 0.95f));
        DrawArc(RootC, RootR, 0, Mathf.Tau, 40, focus ? UiKit.Info : new Color(UiKit.Mina, 0.8f), focus ? 2.2f : 1.5f, true);
        DrawArc(RootC, RootR - 4f, 0, Mathf.Tau, 40, new Color(UiKit.Mina, 0.35f), 1f, true);
        // 核＝ハート（ミナの心）。
        UiKit.Heart(this, RootC + new Vector2(0, -6f), 10f, new Color(UiKit.Mina, 0.95f));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(RootC.X - RootR, RootC.Y + 8f), "ミナの核", 11, UiKit.White, HorizontalAlignment.Center, RootR * 2f);
    }

    // ノードセル（146×44）：1行目=名前＋Lvピップ、2行目=コスト or 前提。封印は暗転＋斜線＋「封印」タグ。
    private void DrawNodeCell(string id, Rect2 r, bool focus)
    {
        var d = GameManager.GetUpgradeDef(id);
        if (d == null) return;
        int lv = _game?.GetUpgradeLevel(id) ?? 0;
        bool maxed = lv >= d.MaxLevel;
        long cost = maxed ? -1 : (_game?.GetUpgradeCost(id) ?? 0);
        bool sealed_ = _game?.IsSealed(id) ?? false;
        bool parentOk = _game?.IsParentMet(id) ?? true;
        bool prereqOk = _game?.IsPrereqMet(id) ?? true;
        long imp = _game?.Impression ?? 0;
        bool can = !maxed && !sealed_ && parentOk && prereqOk && cost >= 0 && imp >= cost;

        // 封印・親未接続は背景から沈めて「まだ触れない」を先に読ませる。
        float bgA = sealed_ ? 0.35f : (!parentOk && lv == 0) ? 0.38f : focus ? 0.8f : 0.55f;
        UiKit.Box(this, r, new Color(22 / 255f, 18 / 255f, 34 / 255f, bgA), 8f,
            focus ? UiKit.Info : new Color(1, 1, 1, 0.08f), focus ? 1.8f : 1f);

        float x = r.Position.X, y = r.Position.Y;

        // 名前（買えるものは白＝“いま買える”が一覧で拾える。買えない/MAX/封印は沈める）
        Color nameCol = sealed_ ? UiKit.Text4 : maxed ? UiKit.Text4 : can ? UiKit.White : (!parentOk && lv == 0) ? UiKit.Text4 : UiKit.Text3;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 12, y + 5), d.Name, 12, nameCol);
        float nw = UiKit.TextW(UiKit.ZenBold, d.Name, 12);

        // 王冠（shot_power：Lv4＝LUNATIC解放条件のひとつ）。
        if (id == "shot_power")
            DrawCrown(new Vector2(x + 12 + nw + 12, y + 13f), 6f, lv >= 4 ? UiKit.Gold : new Color(UiKit.Gold, 0.5f));

        // Lvピップ（右上：MaxLevel 個、lv ぶん充填）
        float px = r.End.X - 10 - d.MaxLevel * 9f, py = y + 12f;
        for (int p = 0; p < d.MaxLevel; p++)
            DrawCircle(new Vector2(px + p * 9f + 3f, py), 2.6f,
                p < lv ? new Color(UiKit.Info, 0.95f) : new Color(1, 1, 1, 0.14f));

        // 2行目：封印はタグで示しコストは出さない。前提未達は理由を常時表示。
        float ly = y + 24f;
        if (sealed_)
        {
            // 斜線＋封印タグ（暗転はセル背景のアルファで済ませ、名前は読めるまま残す）。
            for (float sx = x + 8f; sx < r.End.X - 4f; sx += 22f)
                DrawLine(new Vector2(sx, r.End.Y - 4f), new Vector2(sx + 12f, y + 4f), new Color(ForkGold, 0.22f), 1f);
            const string tag = "封印";
            float tw = UiKit.TextW(UiKit.ZenBold, tag, 10) + 14f;
            var tr = new Rect2(r.End.X - tw - 8f, ly + 2f, tw, 16f);
            UiKit.Box(this, tr, new Color(0.12f, 0.10f, 0.05f, 0.9f), 4f, new Color(ForkGold, 0.6f), 1f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tr.Position.X, ly + 3f), tag, 10, new Color(ForkGold, 0.9f), HorizontalAlignment.Center, tw);
            UiKit.Text(this, UiKit.Zen, new Vector2(x + 12, ly + 3f), Pad.ConfirmToken + " 選び直し", 9, UiKit.Text4);
        }
        else if (!prereqOk)
        {
            string pn = GameManager.GetUpgradeDef(d.PrereqId)?.Name ?? d.PrereqId;
            DrawLockIcon(new Vector2(x + 17, ly + 8f), 5f, new Color(Deny, 0.9f));
            UiKit.Text(this, UiKit.Zen, new Vector2(x + 27, ly + 2f), $"前提: {pn} Lv{d.PrereqLv}", 9, Deny);
        }
        else if (maxed)
        {
            UiKit.Text(this, UiKit.Mono, new Vector2(x + 12, ly + 1f), "MAX", 11, new Color("c9b6ef"));
        }
        else
        {
            string costS = "♥" + cost.ToString("N0");
            UiKit.Text(this, UiKit.Mono, new Vector2(x + 12, ly + 1f), costS, 11, can ? UiKit.Gold : UiKit.Text4);
            if (lv > 0)
                UiKit.Text(this, UiKit.Zen, new Vector2(x + 18 + UiKit.TextW(UiKit.Mono, costS, 11), ly + 3f), $"→Lv{lv + 1}", 9, UiKit.Text4);
        }

        DrawCellFx(id, r, can);
    }

    // フロンティア金パルス／購入可能ノードの呼吸／購入直後グロー／カプストーン解放パルス。
    private void DrawCellFx(string id, Rect2 r, bool can)
    {
        // 購入可能ノードの呼吸：フロンティア（金＝おすすめ）でない「いま買える」全ノードを
        // 系統色でそっと明滅させ、「触れるところ」が一覧で息づく（金の道しるべと衝突しないよう控えめに）。
        if (can && !_frontier.Contains(id))
        {
            float breath = 0.18f + 0.16f * Mathf.Sin((float)_t * 2.6f + r.Position.X * 0.03f);
            UiKit.Box(this, r, null, 8f, new Color(StreamColor(id), breath), 1.4f);
        }
        if (_frontier.Contains(id))
        {
            float pulse = 0.35f + 0.35f * Mathf.Sin((float)_t * 4f);
            UiKit.Box(this, r, null, 8f, new Color(UiKit.Gold, pulse), 1.6f);
            const string rec = "おすすめ";
            float rw = UiKit.TextW(UiKit.Zen, rec, 9) + 10f;
            var rr = new Rect2(r.End.X - rw - 6f, r.Position.Y - 7f, rw, 14f);
            UiKit.Box(this, rr, new Color(UiKit.Gold, 0.2f), 999f, new Color(UiKit.Gold, 0.7f), 1f);
            UiKit.Text(this, UiKit.Zen, new Vector2(rr.Position.X, rr.Position.Y), rec, 9, new Color("f0d98a"), HorizontalAlignment.Center, rw);
        }
        if (_buyFxT > 0 && _buyFxId == id)
        {
            float a = (float)(_buyFxT / 0.7);
            UiKit.Box(this, r, null, 8f, new Color(UiKit.Info, 0.8f * a), 2f);
        }
        if (_capPulseT > 0 && _capPulseId == id)
        {
            float k = 1f - (float)(_capPulseT / 1.4);
            float a = 1f - k;
            UiKit.Box(this, r.Grow(2f + 8f * k), null, 10f, new Color(UiKit.Gold, 0.8f * a), 2f);
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(r.Position.X, r.Position.Y - 22f), "解放!", 14, new Color(UiKit.Gold, a), HorizontalAlignment.Center, r.Size.X);
        }
    }

    // フロンティア（金パルス対象）を確定：おすすめが未購入かつ買えるならそれを、
    // 親未接続で買えないなら親チェーンを遡って最初に買える祖先を代わりに光らせる。
    private void RebuildFrontier()
    {
        _frontier.Clear();
        if (_game == null) return;
        foreach (var start in _recommended)
        {
            string cur = start;
            for (int guard = 0; guard < 8 && !string.IsNullOrEmpty(cur); guard++)
            {
                var d = GameManager.GetUpgradeDef(cur);
                if (d == null) break;
                if (_game.GetUpgradeLevel(cur) > 0) break;           // 既に着手済み＝道しるべ不要
                if (_game.CanPurchase(cur)) { _frontier.Add(cur); break; }
                if (!_game.IsParentMet(cur)) { cur = d.ParentId; continue; } // 親未接続→祖先へ
                break;                                               // 資金/前提/封印が理由なら光らせない
            }
        }
    }

    // 小さな錠前（前提ロックの合図）。
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

    // ───────────────────────── 詳細パネル「つぎの一手」（フォーカス連動） ─────────────────────────
    // 射撃プレビュー＋「いま何を選んでいて、買うと何がどう変わり、いくら残るか／なぜ買えないか」を集約。
    // 封印ノードでは振り直しの差引きを常時表示し、Z の2段確認もこのパネルで完結する。
    private void DrawDetailPanel()
    {
        float x = DetailX, w = DetailW;
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, TreeTop), "つぎの一手", 18, UiKit.White);
        float by = TreeTop + 30f, bh = TreeBot - by;
        UiKit.Box(this, new Rect2(x, by, w, bh), new Color(20 / 255f, 16 / 255f, 30 / 255f, 0.5f), 12f, new Color(UiKit.Info, 0.25f), 1f);

        // 射撃プレビュー（フォーカスノードの枝のモード。全モード系・root は装備中を映す）。
        int pv = _sel <= 2 ? _sel : PreviewModeFor(FocusNodeId);
        if (pv < 0) pv = System.Array.IndexOf(Modes, _game?.SelectedShotMode ?? GameManager.ShotMode.Rapid);
        if (pv < 0) pv = 0;
        bool pvLocked = !(_game?.IsModeUnlocked(Modes[pv]) ?? (pv == 0));
        DrawModeField(x + 10, by + 10, w - 20, 90, pv, pvLocked);
        string pvLabel = _game?.ShotModeName(Modes[pv]) ?? "";
        // ラベルは右上（左はミナ立ち絵が立つので隠れる）。
        UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 16 - UiKit.TextW(UiKit.Mono, pvLabel, 10), by + 14), pvLabel, 10, new Color(1, 1, 1, 0.55f));

        float ix = x + 16f, iw = w - 32f, iy = by + 112f;

        // root（ミナの核）：説明アンカー。
        if (_sel == 3)
        {
            DrawRect(new Rect2(ix, iy + 4, 4f, 18f), new Color(UiKit.Mina, 0.9f));
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 12, iy), "ミナの核", 19, UiKit.White);
            UiKit.Multi(this, UiKit.Zen, new Vector2(ix, iy + 28),
                "ここからすべてが伸びる。線でつながった隣のノードから買える。上＝攻めの系譜、下＝支えの系譜。", 12, UiKit.Text2, iw, 3);
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, iy + 84), "⊗ の枝は「どちらか一方」。心を払えば選び直せる。", 11, new Color(ForkGold, 0.9f));
            return;
        }

        // R0 チップ：モードの案内。
        if (_sel <= 2)
        {
            bool unlocked = _game?.IsModeUnlocked(Modes[_sel]) ?? (_sel == 0);
            DrawRect(new Rect2(ix, iy + 4, 4f, 18f), new Color(CatCol[0], 0.9f));
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 12, iy), _game?.ShotModeName(Modes[_sel]) ?? "", 19, UiKit.White);
            UiKit.Multi(this, UiKit.Zen, new Vector2(ix, iy + 28), ModeDesc[_sel], 12, UiKit.Text2, iw, 2);
            if (_sel == 0)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix, iy + 74), "初期解放・常時使用可（" + Pad.EquipToken + " で装備）", 12, new Color("7ec880"));
            else if (unlocked)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix, iy + 74), Pad.EquipToken + " で装備", 12, new Color("7ec880"));
            else
                UiKit.Text(this, UiKit.Zen, new Vector2(ix, iy + 74),
                    _sel == 1 ? "解放: 連射速度 → 拡散展開（枝の入り口）" : "解放: 身のこなし → 誘導の祈り（枝の入り口）", 12, UiKit.Text3);
            return;
        }

        // ノード詳細
        string id = FocusNodeId!;
        var d = GameManager.GetUpgradeDef(id)!;
        int lv = _game?.GetUpgradeLevel(id) ?? 0;
        bool maxed = lv >= d.MaxLevel;
        long cost = maxed ? -1 : (_game?.GetUpgradeCost(id) ?? 0);
        bool sealed_ = _game?.IsSealed(id) ?? false;
        bool parentOk = _game?.IsParentMet(id) ?? true;
        bool prereqOk = _game?.IsPrereqMet(id) ?? true;
        long imp = _game?.Impression ?? 0;

        DrawRect(new Rect2(ix, iy + 4, 4f, 18f), new Color(CatCol[CatFor(id)], 0.9f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 12, iy), d.Name, 19, UiKit.White);
        string lvS = $"Lv {lv}/{d.MaxLevel}";
        UiKit.Text(this, UiKit.Mono, new Vector2(ix + iw - UiKit.TextW(UiKit.Mono, lvS, 12), iy + 6), lvS, 12, UiKit.Text3);
        UiKit.Multi(this, UiKit.Zen, new Vector2(ix, iy + 26), d.Desc, 12, UiKit.Text2, iw, 2);

        // 現在 → 購入後（差分＝意思決定の中心）。長い効果文が入るので上下2段。
        float ey = iy + 66f;
        UiKit.Box(this, new Rect2(ix, ey, iw, 56f), new Color(0, 0, 0, 0.24f), 9f);
        UiKit.Text(this, UiKit.Mono, new Vector2(ix + 12, ey + 8), "いま", 10, UiKit.Text3);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 58, ey + 6), Eff(id, lv), 13, UiKit.Text2);
        if (!maxed)
        {
            UiKit.Text(this, UiKit.Mono, new Vector2(ix + 12, ey + 32), "買うと", 10, new Color(CatCol[CatFor(id)], 0.8f));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 58, ey + 30), Eff(id, lv + 1), 13, UiKit.White);
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 12, ey + 32), "最大強化済み", 12, new Color("c9b6ef"));
        }

        // 封印中：振り直しの差引きプレビュー（常時）＋2段確認。
        if (sealed_)
        {
            DrawRespecPanel(ix, iw, ey + 68f, id, d);
            return;
        }

        // コスト行：値段・購入後の残り・買えない理由（押す前に全部わかる）。
        float cy = ey + 68f;
        if (cost >= 0)
        {
            string costS = "♥" + cost.ToString("N0");
            bool afford = imp >= cost;
            bool blocked = !parentOk || !prereqOk;
            UiKit.Text(this, UiKit.Mono, new Vector2(ix, cy), costS, 16, afford && !blocked ? UiKit.Gold : Deny);
            float cw2 = UiKit.TextW(UiKit.Mono, costS, 16);
            if (blocked)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), "条件が必要です（下記）", 12, Deny);
            else if (afford)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), $"買うと のこり ♥{(imp - cost):N0}", 12, UiKit.Text3);
            else
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), $"あと ♥{(cost - imp):N0} たりない", 12, Deny);
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, cy + 2), "これ以上は強化できません", 12, UiKit.Text3);
        }

        // 条件行：親（未接続時）→前提（奥義）→排他の注意→LUNATIC 条件（shot_power）。
        float ny = cy + 26f;
        if (!parentOk)
        {
            string pn = GameManager.GetUpgradeDef(d.ParentId)?.Name ?? "ミナの核";
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), $"親: {pn} を先に Lv1 へ（線でつながった隣）", 12, Deny);
            ny += 20f;
        }
        if (!string.IsNullOrEmpty(d.PrereqId))
        {
            string pn = GameManager.GetUpgradeDef(d.PrereqId)?.Name ?? d.PrereqId;
            int plv = _game?.GetUpgradeLevel(d.PrereqId) ?? 0;
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), $"前提: {pn} Lv{d.PrereqLv}（いま Lv{plv}）", 12, prereqOk ? new Color("7ec880") : Deny);
            ny += 20f;
            if (!prereqOk && lv > 0)
            {
                UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), $"所持済みの Lv{lv} は有効のままです", 11, UiKit.Text3);
                ny += 20f;
            }
        }
        if (!string.IsNullOrEmpty(d.ExclusiveWith))
        {
            string en = GameManager.GetUpgradeDef(d.ExclusiveWith)?.Name ?? d.ExclusiveWith;
            int elv = _game?.GetUpgradeLevel(d.ExclusiveWith) ?? 0;
            // 両側所持（旧セーブの共存特例）はその旨を明示。未選択なら「⊗＝選ぶと相方は封印」を予告。
            string exText = elv >= 1 && lv >= 1 ? $"⊗ {en} と両立中（引き継ぎ特例）"
                          : lv >= 1 ? $"⊗ 相方 {en} は封印中（そちらで {Pad.ConfirmToken}＝選び直し）"
                          : $"⊗ どちらか一方: 買うと {en} は封印されます";
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), exText, 11, new Color(ForkGold, 0.9f));
            ny += 20f;
        }
        if (id == "shot_power")
        {
            DrawCrown(new Vector2(ix + 7, ny + 10), 6f, UiKit.Gold);
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 18, ny), "Lv4 で LUNATIC 解放条件のひとつを満たします", 11, new Color("c9b6ef"));
        }
    }

    // 封印ノードの振り直しパネル：差引きの内訳（返金−手数料）を常時見せ、Z の2段確認をここで完結する。
    private void DrawRespecPanel(float ix, float iw, float ry, string id, GameManager.UpgradeDef d)
    {
        string partner = d.ExclusiveWith;
        var pd = GameManager.GetUpgradeDef(partner);
        int plv = _game?.GetUpgradeLevel(partner) ?? 0;
        long refund = _game?.RespecRefund(id, partner) ?? 0;
        long fee = _game?.RespecFee(id, partner) ?? 0;

        UiKit.Text(this, UiKit.Zen, new Vector2(ix, ry), $"封印中: ⊗ {pd?.Name ?? partner} を選んだ枝", 12, new Color(ForkGold, 0.9f));

        bool armed = _respecArmed && _respecId == id;
        var box = new Rect2(ix, ry + 22f, iw, armed ? 112f : 70f);
        UiKit.Box(this, box, new Color(0.12f, 0.10f, 0.05f, 0.5f), 9f, new Color(ForkGold, armed ? 0.9f : 0.45f), armed ? 1.6f : 1f);
        float yy = ry + 30f;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 12, yy), $"手放す: {pd?.Name} Lv{plv} → 選べる: {d.Name}", 12, UiKit.Text2);
        yy += 22f;
        UiKit.Text(this, UiKit.Zen, new Vector2(ix + 12, yy), $"返金 ♥{refund:N0} − 手数料 ♥{fee:N0} = ♥{refund - fee:N0}", 12, UiKit.Gold);
        yy += 24f;
        if (armed)
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 12, yy), "本当に選び直しますか？", 13, UiKit.White);
            yy += 22f;
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 12, yy), Pad.ConfirmToken + " 確定　／　" + Pad.CancelToken + " 取消", 12, new Color(ForkGold, 0.9f));
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 12, yy), Pad.ConfirmToken + " で選び直し（2段確認）", 11, UiKit.Text3);
        }
    }

    // 強化のレベル別効果表示。ゲーム側の実計算式（GameManager/Player/Enemy/Bullet）と必ず一致させる。
    // 澄んだ心は現在の汚染度での実効値を正直に出す（無汚染では Lv3 が伸びないことも見えるように）。
    private string Eff(string id, int lv) => id switch
    {
        "shot_power" => $"威力 +{lv}",
        "fire_rate" => $"発射間隔 ×{Mathf.Max(0.4f, 1f - 0.08f * lv):0.00}",
        "shot_spread" => lv == 0 ? "未解放" : $"{new[] { 0, 5, 7, 9 }[Mathf.Clamp(lv, 0, 3)]}way",
        "shot_homing" => lv == 0 ? "未解放" : $"{new[] { 0, 2, 2, 3 }[Mathf.Clamp(lv, 0, 3)]}体追尾",
        "shot_pierce" => lv == 0 ? "貫通なし" : $"連射弾が敵 {lv} 体を貫通",
        "focus_fire" => lv == 0 ? "集中なし" : $"同じ敵に当て続けて威力 最大+{lv}",
        "counter_light" => lv == 0 ? "変換なし" : lv == 1 ? "2発に1発を光弾化（上限6/回避）" : "全弾を光弾化（上限12/回避）",
        "veil_light" => lv == 0 ? "光輪なし" : $"回避後の光輪 r{(lv == 1 ? 20 : 28)}px・{(lv == 1 ? 0.5f : 0.7f):0.0}s",
        "option_sub" => lv == 0 ? "オプションなし" : $"追従オプション {lv} 基（威力×0.5）",
        "chain_light" => lv == 0 ? "跳弾なし" : $"拡散弾が {lv} 回跳弾（威力×0.4）",
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

    // 背景の浮遊粒：ツリー領域をゆっくり昇る淡い光の粒。座標は index からの決定的擬似乱数
    //   （new/割り当てなし・時間で位相を進めるだけ）。動きの無さ＝地味さへの最小コストの回答。
    private void DrawBgMotes()
    {
        const int n = 22;
        float top = TreeTop, bot = TreeBot, span = bot - top;
        for (int i = 0; i < n; i++)
        {
            float sx = (i * 131 % 97) / 97f;                 // 0..1 横位置の散らし
            float speed = 0.06f + (i % 5) * 0.015f;          // 粒ごとに違う速度
            float phase = Mathf.PosMod((float)_t * speed + i * 0.137f, 1f);
            float x = 60f + sx * (DetailX - 120f);
            float y = bot - phase * span;                    // 下→上へ
            float tw = 0.5f + 0.5f * Mathf.Sin((float)_t * 1.3f + i);   // またたき
            float fade = Mathf.Sin(phase * Mathf.Pi);        // 端で消える
            DrawCircle(new Vector2(x, y), 1.3f, new Color(UiKit.PurifyHi, 0.16f * tw * fade));
        }
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
