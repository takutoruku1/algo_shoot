using Godot;

// Shop : 強化ショップ（キャラクター別の強化リスト＋詳細パネル）。
//   操作（2026-09-27 に統一）：↑↓ で段を選び、←→ で詳細パネルの [強化する] [ためし撃ち] を切り替えて Z。Esc／X でもどる。
//   旧 T（トレーニングへ）の文字キーは廃止＝ためし撃ちは詳細パネルのボタンから行く。キーの案内は右下のヒント帯だけ。
//   マウスは従来どおり：段クリックで選択／同じ段の再クリックで購入、ボタンはクリックで即発火。
public partial class Shop : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    private const float FooterY = 664f;
    private const float ColX = 48f, ColW = 444f;
    private const float RowTop = 142f, RowH = 32f, RowBigH = 38f, RowGap = 7f;
    private const float DetailX = 562f, DetailW = 670f;
    private static readonly Color Surface = new("142c2b");
    private static readonly Color Raised = new("29413c");
    private static readonly Color Owned = new("bad6b6");
    private static readonly Color Ink = new("f8edd6");
    private static readonly Color Muted = new("b9c2ad");
    private static readonly Color Line = new("526650");
    private static readonly Color Deny = new("f1a798");
    private static readonly Color Brass = new("d4ad70"), Paper = new("f1e3c4"), Wood = new("503c2c");
    private Texture2D? _accountIcon;

    // 小話3（ショップの一言）：入店・購入時・退店でミナがぽつりと零す台詞。既存の Toast() で表示するだけ＝
    //   買い物のテンポを邪魔しない短時間表示（1.8秒）。docs/小話集_v1.md §3 の文面をそのまま採用。
    private static readonly string[] ShopEnterTalk =
    { "いらっしゃいませ。……冗談です、ご主人様しかいらっしゃいませんもの。", "さあ、わたくしを研いでくださいまし。", "お財布の中身、ちゃんと確認なさいました?", "本日の心の残高、しかとご報告いたします。", "急がなくて結構ですよ。ここは、時間が減りませんので。", "眺めているだけでも構いません。……買い物は、選んでいる時間がいちばん楽しいので。", "今日はどれにしましょう。わたくし、少し欲張りたくなります。", "ああ、これ。前もそこで迷っておられました。" };

    private static readonly string[] ShopBuyTalk =
    { "はい、たしかに。……染みますね、これは。", "また一つ、できることが増えました。……あとで、一緒に試しましょう。", "お買い上げ、ありがとうございます。領収書はご入用で?", "重くなった気がします。……気のせいですね。", "ご主人様、いい買い物です。わたくしが言うのですから間違いありません。", "これで、また一歩、遠くまで行けます。", "……一緒に強くなれるのは、うれしいですね。", "ありがとうございます。ちゃんと使いますので。" };

    private static readonly string[] ShopExitTalk =
    { "では、まいりましょう。……お忘れ物はありませんか。", "行ってまいります。……お留守番は、いたしませんので。", "支度は済みました。ご主人様の号令をどうぞ。", "戻ってきたら、また買い物に付き合ってくださいね。", "閉店です。……なんて、看板もないのですけれど。", "ご主人様、背筋。", "戻ってきたら、今日の使い心地をお話ししますね。" };

    private Texture2D? _playerShot;

    private int _sel;
    // 詳細パネルのボタンのフォーカス（0＝強化する／1＝ためし撃ち）。←→ で切り替え、Z で押す。
    private int _act;
    public int Action => _act;

    // 入力エッジ
    private bool _navHeld, _zHeld, _backHeld, _lrHeld, _jobHeld;
    private JobTuning[] _customers = System.Array.Empty<JobTuning>();
    private double _t, _toastT;
    private string _toast = "";
    private Color _toastCol = UiKit.Info;
    private bool _toastIsDialogue;
    private double _toastAge;    // 出てからの経過秒（ぴこん、と跳ねる出方の位相）
    private bool _autoplay;

    // 小話3・退店演出：ExitShop() は即遷移せず「一言トースト→短い遅延」を挟む。二重発火は _exitPending が防ぐ。
    private bool _exitPending;
    private double _exitDelayT;
    private string _pendingExitDest = "";

    private const int HsBack = -100;  // フッタ もどる
    private const int HsBuy = -101;   // 詳細パネルの「買う」ボタン
    private const int HsTrain = -102;
    private const int HsCustomer = -103;
    private static readonly Rect2 CustomerButton = new(373, 31, 177, 38);
    private Rect2 _backBtnRect, _buyBtnRect, _trainBtnRect;
    private bool _buyBtnActive;

    // 演出タイマー
    private double _buyFxT;       // 購入バースト
    private double _walletPopT;   // ウォレットpop
    private string _buyFxId = ""; // 購入したノード（段の充填グロー）

    // 段の数（＝カタログの長さ）。以降 for はすべてこれを回す。
    private static int Steps => GameManager.Upgrades.Length;
    private static GameManager.UpgradeDef Def(int i) => GameManager.Upgrades[i];

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        // ハブから買い物へ移っても拠点の主題を途切れさせない。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmShop);
        _customers = System.Array.FindAll(Jobs.All, j => _game.IsJobUnlocked(j.Id));
        LoadCustomerArt();
        _sel = System.Array.FindIndex(GameManager.Upgrades, def => !_game.Has(def.Id));
        if (_sel < 0) _sel = Steps - 1;

        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        // 移行があった直後だけ、何段引き継いだかを知らせる（黙って増えていると不信になる）。
        if (_game != null && _game.MigratedNodeCount >= 0)
            Toast($"前の強化を引き継ぎました（{_game.MigratedNodeCount}段）", UiKit.Info);
        else
            ShowShopTalk(CompanionDialogue.Menu.ShopEnter, ShopEnterTalk);
    }

    private bool CanSwitchCustomer => _customers.Length > 1 && !_game.JobForcedByCmdline && string.IsNullOrEmpty(_game.PendingResumeScene);

    private void LoadCustomerArt()
    {
        _playerShot = ResourceLoader.Load<Texture2D>(_game.JobDef.PlayerTexturePath);
        _accountIcon = ResourceLoader.Load<Texture2D>(CompanionDialogue.AccountPortrait(_game.SelectedJob));
    }

    private void NextCustomer()
    {
        if (!CanSwitchCustomer) return;
        int index = System.Array.FindIndex(_customers, j => j.Id == _game.SelectedJob);
        _game.SelectedJob = _customers[(index + 1) % _customers.Length].Id;
        _game.AutoSave();
        LoadCustomerArt();
        _buyFxT = 0;
        Audio.Instance?.PlayUiMove();
        ShowShopTalk(CompanionDialogue.Menu.ShopEnter, ShopEnterTalk);
    }

    private bool IsOwned(int i) => _game.Has(Def(i).Id);

    public override void _Process(double delta)
    {
        _t += delta;
        if (_toastT > 0) { _toastT -= delta; _toastAge += delta; }
        if (_buyFxT > 0) _buyFxT -= delta;
        if (_walletPopT > 0) _walletPopT -= delta;

        // 小話3・退店演出：ExitShop() が退店トーストを立てたら、実際のシーン遷移までここで待つ。
        //   _autoplay 分岐より前に置くことで、オートプレイでも遅延タイマーはちゃんと進む（進行不能にしない）。
        if (_exitPending)
        {
            _exitDelayT -= delta;
            QueueRedraw();
            if (_exitDelayT <= 0) GameManager.FadeToScene(this, _pendingExitDest);
            return;
        }
        if (_autoplay) { ExitShop(); return; }

        // ポーズメニュー（M で重なる）を閉じた Esc/M/Z の同じ押下がこのフレームに漏れて
        // 「もどる＝ショップごと閉じる」「購入」が誤発火しないよう、ゲート中は全キーを既押し扱いで食う。
        if (Pad.UiBlocked(this))
        {
            _navHeld = _zHeld = _backHeld = _lrHeld = _jobHeld = true;
            QueueRedraw();
            return;
        }

        // 右下のヒント帯の「メニュー」は左クリックでも開く（PauseMenu の右下チップは廃止＝帯に統合。Hub と同じ作法）。
        if (UiKit.HintItemClicked(UiKit.HintAnchor, HintItems(), "メニュー"))
        {
            GetNodeOrNull<PauseMenu>("/root/PauseMenu")?.Open();
            QueueRedraw();
            return;
        }

        bool changeCustomer = Input.IsKeyPressed(Key.Tab) || Pad.Pressed(JoyButton.Y);
        if (changeCustomer && !_jobHeld) NextCustomer();
        _jobHeld = changeCustomer;

        // ── マウス クリック（キーボード/パッドへ純粋に追加）──
        //   段のカードは「クリックで選ぶ」。既に選んでいる段をもう一度クリックしたら確定（＝買う）。
        //   ボタン類（買う／もどる／トレーニング）は1クリックで即発火。
        if (Pad.MouseClick())
        {
            int hit = UiKit.ClickedId(true);
            if (hit == HsBack) { Audio.Instance?.PlayUiCancel(); ExitShop(); }
            else if (hit == HsCustomer) NextCustomer();
            else if (hit == HsTrain) EnterTraining();
            else if (hit == HsBuy) OnConfirm();
            else if (hit >= 0 && hit < Steps)
            {
                if (hit == _sel) OnConfirm();
                else { _sel = hit; Audio.Instance?.PlayUiMove(); }
            }
        }

        // カーソル移動：↑↓ で商品を選ぶ。
        bool up = Input.IsActionPressed("ui_up");
        bool down = Input.IsActionPressed("ui_down");
        bool any = up || down;
        if (any && !_navHeld)
        {
            _sel = Mathf.Clamp(_sel + (up ? -1 : 1), 0, Steps - 1);
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = any;

        // ←→：詳細パネルのボタン [強化する] [ためし撃ち] を切り替える（2026-09-27。旧 T キーの置き換え）。
        bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
        bool lr = left || right;
        if (lr && !_lrHeld)
        {
            int nx = left ? 0 : 1;
            if (nx != _act) { _act = nx; Audio.Instance?.PlayUiMove(); }
        }
        _lrHeld = lr;

        // Z：フォーカス中のボタンを押す（強化する＝買う／ためし撃ち＝トレーニングへ）。
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2)
        {
            if (_act == 1) EnterTraining();
            else OnConfirm();
        }

        // X／Esc：もどる（Esc は 2026-09-26 に「一つ前の画面へ」として復帰。メニューを開くのは M）。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2) { Audio.Instance?.PlayUiCancel(); ExitShop(); }

        QueueRedraw();
    }

    private void ExitShop()
    {
        if (_exitPending) return; // 二重発火ガード（連打・オートプレイの毎フレーム呼び出し対策）
        var game = GetNodeOrNull<GameManager>("/root/Game");
        string dest = "res://Hub.tscn";
        if (game != null && !string.IsNullOrEmpty(game.PendingResumeScene))
        {
            dest = game.PendingResumeScene!;
            game.PendingResumeScene = null; // 消費
        }
        ShowShopTalk(CompanionDialogue.Menu.ShopExit, ShopExitTalk);
        _pendingExitDest = dest;
        _exitDelayT = _game.SelectedJob == Job.Tank ? 0.8 : 2.4;
        _exitPending = true;
    }

    // トレーニング（試し打ち場）へ。スキルを無料で付け外しして撃ち味を数値で比べる。
    //   本番状態（通貨/所持強化/ジョブ/フォロワー）は TrainingRoot が退避→復元＝ここでは何も汚さない。
    private void EnterTraining()
    {
        Audio.Instance?.PlayUiConfirm();
        GameManager.FadeToScene(this, "res://Training.tscn");
    }

    // Z（または「買う」ボタン／選択中の段の再クリック）で呼ばれる。買えない理由はトーストで返す。
    private void OnConfirm()
    {
        if (_game == null) return;
        var d = Def(_sel);
        if (IsOwned(_sel)) { Audio.Instance?.PlayUiDeny(); Toast("もう持っています", UiKit.Text4); return; }
        var prerequisite = _game.GetMissingUpgradePrerequisite(d.Id);
        if (prerequisite != null)
        {
            Audio.Instance?.PlayUiDeny();
            Toast($"先に「{prerequisite.Name}」を習得してください", Deny);
            return;
        }
        long cost = _game.GetUpgradeCost(d.Id);
        if (_game.Impression < cost)
        {
            Audio.Instance?.PlayUiDeny();
            Toast($"あと {cost - _game.Impression} たりません", Deny);
            return;
        }
        if (!_game.TryPurchase(d.Id)) { Audio.Instance?.PlayUiDeny(); return; }

        Audio.Instance?.PlayUiBuy();
        // 小話3：低頻度（約25%）で購入確認トーストの代わりにミナの一言。買い物のテンポを崩さない。
        if (GD.Randf() < 0.25f) ShowShopTalk(CompanionDialogue.Menu.ShopBuy, ShopBuyTalk);
        else Toast($"{d.Name}", UiKit.Info);
        _buyFxT = 0.7; _walletPopT = 0.5; _buyFxId = d.Id;
    }

    private void Toast(string msg, Color col)
    { _toast = msg; _toastCol = col; _toastT = 1.8; _toastIsDialogue = false; _toastAge = 0; }

    private void ShowShopTalk(CompanionDialogue.Menu scene, string[] minaLines)
    {
        var job = _game.SelectedJob;
        _toast = job == Job.Tank ? minaLines[GD.RandRange(0, minaLines.Length - 1)] : CompanionDialogue.MenuText(job, scene);
        _toastCol = CompanionDialogue.Accent(job);
        _toastT = job == Job.Tank ? 1.8 : 4.5;
        _toastIsDialogue = true;
        _toastAge = 0;
    }

    // ───────────────────────── 版面の計算 ─────────────────────────
    // i 段目の高さ（能力を覚える段だけ一回り大きい）。
    private static float RowHeightOf(int i) => GameManager.IsAbilityNode(Def(i).Id) ? RowBigH : RowH;
    // i 段目の上端 Y（0 段目から高さ＋隙間を積む）。段の高さが違うので前段までを実際に足す。
    private static float RowY(int i)
    {
        float y = RowTop;
        for (int k = 0; k < i; k++) y += RowHeightOf(k) + RowGap;
        return y;
    }
    private static Rect2 RowRect(int i) => new(ColX, RowY(i), ColW, RowHeightOf(i));

    // ───────────────────────── 描画 ─────────────────────────
    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        UiKit.BeginHotspots(Pad.MousePos());

        DrawBg();
        DrawHeader();
        DrawColumn();
        DrawDetail();
        DrawFooter();
        DrawToast();
        // 右下のヒント帯（退店の一言を待つあいだとオートプレイ中は出さない）。
        UiKit.HintBarPlate(this, UiKit.HintAnchor, HintItems(), "メニュー");

        UiKit.EndDesign(this);
    }

    // 右下のヒント帯の項目。キーボード：↑↓ えらぶ／←→ きりかえ／Z けってい／Esc もどる／M メニュー。
    //   パッド：十字 えらぶ／A けってい／B もどる／≡ メニュー。
    private (string token, string label)[] HintItems() =>
        _autoplay || _exitPending ? System.Array.Empty<(string, string)>()
            : UiKit.PhoneHints("↑↓", "けってい", "もどる", ("←→", "きりかえ"));

    // ヒント帯の外接矩形（QA 用。出していなければ大きさ 0）。
    public Rect2 HintBarRect => UiKit.HintBarBounds(UiKit.HintAnchor, HintItems());

    private void DrawBg()
    {
        UiKit.VGradient(this, new Rect2(0, 0, W, H), new[] { new Color("143a39"), new Color("0b2225") }, new[] { 0f, 1f });
        for (int x = 16; x < W; x += 48)
            DrawLine(new Vector2(x, 0), new Vector2(x, H), new Color(Brass, .045f), 1);
        DrawRect(new Rect2(0, 0, W, 8), Wood);
        DrawLine(new Vector2(0, 8), new Vector2(W, 8), Brass, 2);
        UiKit.Box(this, new Rect2(28, 98, 484, 548), Wood, 4, new Color("856343"), 2);
        UiKit.Box(this, new Rect2(38, 108, 464, 526), new Color("1a302a"), 2, new Color("9a7850"), 1);
        DrawRect(new Rect2(28, 635, 484, 12), new Color("77583b"));
        UiKit.Box(this, new Rect2(536, 99, 716, 453), Wood, 4, new Color("856343"), 2);
        UiKit.Box(this, new Rect2(546, 109, 696, 434), new Color("1a3734"), 2, new Color(Brass, .3f), 1);
        for (int x = 560; x < 1240; x += 24)
            for (int y = 120; y < 354; y += 24)
                DrawCircle(new Vector2(x, y), 1, new Color(Brass, .08f));
        DrawRect(new Rect2(542, 350, 704, 10), new Color("8d6843"));
        DrawLine(new Vector2(546, 350), new Vector2(1240, 350), Brass, 2);
        UiKit.Box(this, new Rect2(540, 562, 708, 76), new Color("122b28"), 3, new Color("766747"), 1);
        for (int x = 550; x <= 1238; x += 688)
            for (int y = 572; y <= 628; y += 56)
                DrawCircle(new Vector2(x, y), 2, Brass);
        DrawRect(new Rect2(0, 653, W, 67), new Color("2c3026"));
        DrawLine(new Vector2(0, 653), new Vector2(W, 653), new Color("9c7951"), 3);
    }

    private static string Money(long value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private void DrawHeader()
    {
        DrawLine(new Vector2(74, 8), new Vector2(74, 28), Brass, 2);
        UiKit.Box(this, new Rect2(48, 26, 48, 48), Paper, 4, Brass, 2);
        UiKit.Heart(this, new Vector2(72, 48), 11, new Color("8b4f49"));
        UiKit.Tracked(this, UiKit.Mono, new Vector2(112, 20), "REFRAIN / HEART WORKSHOP", 11, Brass, .8f);
        UiKit.Text(this, UiKit.KaiseiDecol, new Vector2(110, 39), "心の工房", 30, Ink);
        UiKit.Text(this, UiKit.Zen, new Vector2(258, 53), "強化ショップ", 14, Muted);
        if (CanSwitchCustomer)
        {
            bool hover = UiKit.Hotspot(CustomerButton, HsCustomer);
            UiKit.Box(this, CustomerButton, hover ? Raised : Surface, 3, Brass, 1);
            string key = Pad.UsingPad ? Pad.Face(JoyButton.Y) : "Tab";
            UiKit.Text(this, UiKit.ZenBold, CustomerButton.Position + new Vector2(0, 7),
                $"キャラ切替  {key}", 16, Ink, HorizontalAlignment.Center, CustomerButton.Size.X);
        }
        UiKit.FaceAvatar(this, new Vector2(600, 48), 20, _accountIcon,
            CompanionDialogue.Accent(_game.SelectedJob), false, 0, 1, _t);
        UiKit.Text(this, UiKit.Zen, new Vector2(634, 24), "強化するキャラ", 11, Muted);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(634, 44), _game.JobDef.CharacterName, 18, Ink);
        string money = Money(_game.Impression);
        int size = UiKit.TextW(UiKit.Mono, money, 28) > 280 ? 22 : 28;
        float moneyW = UiKit.TextW(UiKit.Mono, money, size);
        float walletX = Mathf.Min(788, 1212 - moneyW - 142);
        UiKit.Box(this, new Rect2(walletX, 21, 1232 - walletX, 54), new Color("203d35"), 3, Brass, 1);
        DrawLine(new Vector2(walletX + 7, 27), new Vector2(1225, 27), new Color(Brass, .3f), 1);
        UiKit.Text(this, UiKit.Zen, new Vector2(1212 - moneyW - 122, 39), "浄化した心", 14, Muted);
        UiKit.Heart(this, new Vector2(1212 - moneyW - 20, 48), 8, Brass);
        UiKit.Text(this, UiKit.Mono, new Vector2(1212 - moneyW, 30), money, size,
            _walletPopT > 0 ? Brass : Ink);
    }

    private void DrawColumn()
    {
        UiKit.Text(this, UiKit.ZenBold, new Vector2(ColX + 8, 112), "工房の品書き", 15, Ink);
        UiKit.Text(this, UiKit.Mono, new Vector2(ColX + ColW - 96, 112),
            $"{_game.OwnedUpgradeCount:00} / {Steps:00}", 15, Owned, HorizontalAlignment.Right, 96);

        for (int i = 0; i < Steps; i++)
        {
            var r = RowRect(i);
            bool owned = IsOwned(i), focus = i == _sel;
            bool hover = UiKit.Hotspot(r, i);
            DrawRow(i, r, owned, focus, hover);
        }
        float shelfY = RowRect(Steps - 1).End.Y + 24;
        if (shelfY + 88 < 634)
        {
            for (int jar = 0; jar < 3; jar++)
            {
                float x = 80 + jar * 78;
                UiKit.Box(this, new Rect2(x, shelfY + 12, 44, 47), new Color("2e5148"), 9, new Color(Owned, .45f), 1);
                DrawRect(new Rect2(x + 10, shelfY + 6, 24, 10), new Color("b18b5a"));
                DrawLine(new Vector2(x + 7, shelfY + 24), new Vector2(x + 7, shelfY + 43), new Color(1, 1, 1, .2f), 2);
                UiKit.Heart(this, new Vector2(x + 22, shelfY + 37), 7, new Color(Brass, .8f));
            }
            DrawRect(new Rect2(60, shelfY + 60, 430, 7), new Color("78573b"));
            DrawLine(new Vector2(60, shelfY + 60), new Vector2(490, shelfY + 60), Brass, 1);
            UiKit.Box(this, new Rect2(320, shelfY + 18, 158, 39), Paper, 2);
            UiKit.Text(this, UiKit.KleeOne, new Vector2(329, shelfY + 23), "お好きな品から。", 16, Wood);
        }
    }

    private void DrawRow(int i, Rect2 r, bool owned, bool focus, bool hover)
    {
        var d = Def(i);
        bool locked = !owned && _game.GetMissingUpgradePrerequisite(d.Id) != null;
        Color accent = locked ? Muted : UpgradeColor(d.Id);
        UiKit.Box(this, new Rect2(r.Position + new Vector2(0, 3), r.Size), new Color("101c19"), 2);
        UiKit.Box(this, r, focus || hover ? new Color("4a5140") : new Color("2d3c30"), 2,
            focus ? Brass : new Color("756346"), focus ? 2 : 1);
        DrawLine(r.Position + new Vector2(5, 3), new Vector2(r.End.X - 5, r.Position.Y + 3), new Color(Brass, .15f), 1);
        var node = new Vector2(r.Position.X + 17, r.GetCenter().Y);
        if (owned) DrawCheck(node, Owned, .65f);
        else UiKit.Text(this, UiKit.Mono, node - new Vector2(10, 8), $"{i + 1:00}", 11, Muted);
        UiKit.Box(this, new Rect2(r.Position.X + 38, r.GetCenter().Y - 13, 30, 26), new Color("1b2d25"), 2, new Color(Brass, .4f), 1);
        DrawUpgradeMark(d.Id, new Vector2(r.Position.X + 53, r.GetCenter().Y), accent, .75f);
        float textY = r.GetCenter().Y - UiKit.ZenBold.GetHeight(17) / 2;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X + 78, textY), d.Name, 17, locked ? Muted : Ink);
        var tag = new Rect2(r.End.X - 99, r.Position.Y + 4, 92, r.Size.Y - 8);
        UiKit.Box(this, tag, owned ? new Color("a9c0a0") : locked ? new Color("b5baa6") : Paper, 2, Brass, 1);
        DrawCircle(tag.Position + new Vector2(5, tag.Size.Y / 2), 1.5f, Wood);
        UiKit.Text(this, owned || locked ? UiKit.ZenBold : UiKit.Mono, new Vector2(r.End.X - 87, r.GetCenter().Y - 10),
            owned ? "購入済み" : locked ? "未解放" : Money(d.BaseCost), owned || locked ? 12 : 14,
            !owned && !locked && _game.Impression < d.BaseCost ? new Color("8e3f36") : Wood, HorizontalAlignment.Right, 73);
        if (_buyFxId == d.Id && _buyFxT > 0)
        {
            float k = (float)(_buyFxT / .7);
            DrawRect(new Rect2(r.Position.X + 39, r.End.Y - 2, (r.Size.X - 39) * (1 - k), 2), new Color(Brass, k));
        }
    }

    private void DrawDetail()
    {
        var d = Def(_sel);
        bool owned = IsOwned(_sel);
        var prerequisite = _game.GetMissingUpgradePrerequisite(d.Id);
        bool locked = !owned && prerequisite != null;
        Color accent = UpgradeColor(d.Id);
        UiKit.Text(this, UiKit.Mono, new Vector2(DetailX, 119), $"ORDER No. {_sel + 1:00} / 強化のご案内", 14, new Color("e8ce96"));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(DetailX, 154),
            d.Id == "n_slow" ? PlayerSpecial.NameFor(_game.SelectedJob) : d.Name, 32, Ink);
        UiKit.Text(this, UiKit.Zen, new Vector2(DetailX, 209), "強化評価", 13, Muted);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(DetailX + 66, 204),
            new string('★', d.Rating) + new string('☆', 5 - d.Rating), 19, Brass);
        UiKit.Text(this, UiKit.Mono, new Vector2(DetailX + 176, 208), $"{d.Rating} / 5", 14, Ink);
        if (GameManager.IsAbilityNode(d.Id))
            UiKit.Text(this, UiKit.Zen, new Vector2(DetailX + 240, 209),
                d.Id == "n_slow" ? $"必殺技 / {_game.JobDef.CharacterName}" : "ABILITY", 13, accent);
        UiKit.Multi(this, UiKit.Zen, new Vector2(DetailX, 241),
            d.Id == "n_slow" ? PlayerSpecial.DescriptionFor(_game.SelectedJob) : d.Desc, 18, Muted, 330, 2);

        UiKit.Box(this, new Rect2(DetailX + 8, 340, 60, 8), Brass, 2);
        DrawCircle(new Vector2(DetailX + 38, 322), 23, new Color("0c1b25"));
        DrawArc(new Vector2(DetailX + 38, 322), 23, 0, Mathf.Tau, 48, new Color(accent, .65f), 1.5f, true);
        DrawUpgradeMark(d.Id, new Vector2(DetailX + 38, 322), accent, 1.5f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(DetailX + 88, 317),
            owned ? "購入済み" : locked ? "未解放" : "未購入", 17, owned ? Owned : Muted);
        if (prerequisite != null)
            UiKit.Text(this, UiKit.Zen, new Vector2(DetailX + 168, 320),
                $"「{prerequisite.Name}」習得で{(owned ? "有効" : "解放")}", 14, Deny);
        DrawCharacter();

        var (now, after) = EffectPair(_sel);
        UiKit.Box(this, new Rect2(DetailX, 370, 302, 98), Paper, 3, Brass, 1);
        UiKit.Box(this, new Rect2(DetailX + 348, 370, 322, 98), new Color("304d3c"), 3, Brass, 1);
        float afterX = DetailX + 358;
        UiKit.Text(this, UiKit.Zen, new Vector2(DetailX + 14, 380), "強化前", 13, Wood);
        UiKit.Text(this, UiKit.Zen, new Vector2(afterX + 4, 380), "強化後", 13, Owned);
        UiKit.Multi(this, UiKit.ZenBold, new Vector2(DetailX + 14, 407), now, 18, Surface, 284, 2);
        UiKit.Multi(this, UiKit.ZenBold, new Vector2(afterX + 4, 407), after, 18, Owned, 284, 2);
        DrawArrow(new Vector2(DetailX + 322, 421), Muted, 1);

        long cost = _game.GetUpgradeCost(d.Id);
        bool enough = _game.Impression >= cost;
        _buyBtnActive = _game.CanPurchase(d.Id);
        // [強化する] と [ためし撃ち] を横に並べる（←→ で切り替えて Z。2026-09-27 に旧フッタの「ためし撃ち」をここへ移した）。
        const float TrainW = 176f, BtnGap = 12f;
        _buyBtnRect = new Rect2(DetailX, 486, DetailW - TrainW - BtnGap, 56);
        _trainBtnRect = new Rect2(DetailX + DetailW - TrainW, 486, TrainW, 56);
        float buyW = _buyBtnRect.Size.X;
        bool hover = _buyBtnActive && UiKit.Hotspot(_buyBtnRect, HsBuy);
        Color fill = _buyBtnActive ? (hover ? new Color("eed4a2") : Brass) : Raised;
        Color foreground = _buyBtnActive ? Surface : Muted;
        UiKit.Box(this, _buyBtnRect, Raised, 4, _buyBtnActive ? Brass : Line, 1);
        if (_buyBtnActive)
        {
            UiKit.Box(this, _buyBtnRect.Grow(-2), fill, 6);
            DrawLine(_buyBtnRect.Position + new Vector2(10, 3), new Vector2(_buyBtnRect.End.X - 10, _buyBtnRect.Position.Y + 3), new Color("fff0cb"), 2);
        }
        string label = owned ? "購入済み"
            : locked ? $"「{prerequisite!.Name}」の習得が必要"
            : !enough ? $"あと {Money(cost - _game.Impression)}"
            : "強化する";
        if (_buyBtnActive)
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(DetailX + 24, 500), label, 20, foreground);
            string price = Money(cost);
            float pw = UiKit.TextW(UiKit.Mono, price, 20);
            UiKit.Heart(this, new Vector2(DetailX + buyW - pw - 74, 514), 7, foreground);
            UiKit.Text(this, UiKit.Mono, new Vector2(DetailX + buyW - pw - 54, 500), price, 20, foreground);
            DrawArrow(new Vector2(DetailX + buyW - 26, 514), foreground, 0.8f);
        }
        else
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(DetailX + 20, 502), label, 17,
                !owned ? Deny : Muted, HorizontalAlignment.Center, buyW - 40);
        }

        // ためし撃ち（トレーニングへ）。枠だけの控えめなボタン＝主役は [強化する]。
        bool trainHover = UiKit.Hotspot(_trainBtnRect, HsTrain);
        bool trainOn = trainHover || (!Pad.UsingMouse && _act == 1);
        UiKit.Box(this, _trainBtnRect, trainOn ? Raised : Surface, 4, Brass, trainOn ? 2 : 1);
        DrawUpgradeMark("n_hitbox", new Vector2(_trainBtnRect.Position.X + 30, 514), trainOn ? Ink : Muted, 0.8f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(_trainBtnRect.Position.X + 52, 502), "ためし撃ち", 17, trainOn ? Ink : Muted);

        // キーボード／パッドのフォーカス枠（マウス操作中は出さない＝ホバーの明るさで足りる）。
        if (!Pad.UsingMouse)
            UiKit.Box(this, (_act == 0 ? _buyBtnRect : _trainBtnRect).Grow(4), Colors.Transparent, 10, Ink, 1.5f);

        if (_toastT <= 0)
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(DetailX, 574), $"{_game.JobDef.CharacterName}の購入記録", 13, Muted);
            UiKit.Text(this, UiKit.Mono, new Vector2(DetailX + DetailW - 110, 572),
                $"{_game.OwnedUpgradeCount:00} / {Steps:00}", 16, Owned, HorizontalAlignment.Right, 110);
            float segment = (DetailW - (Steps - 1) * 5) / Steps;
            for (int i = 0; i < Steps; i++)
                UiKit.Box(this, new Rect2(DetailX + (segment + 5) * i, 610, segment, 4),
                    IsOwned(i) ? Owned : Line, 2);
        }
    }

    // 素材ごとの透明余白に左右されないよう、不透明部分を基準に展示台へ揃える。
    private static readonly Rect2 CharArea = new(948, 110, 284, 244);
    private const float CharContentH = 188f;
    private static Vector2 CharFoot => new(CharArea.GetCenter().X, CharArea.End.Y - 22);

    private void DrawCharacter()
    {
        var area = CharArea;
        Color accent = CompanionDialogue.Accent(_game.SelectedJob);
        DrawLine(new Vector2(area.GetCenter().X, 110), new Vector2(area.GetCenter().X, 130), Brass, 2);
        DrawColoredPolygon(new[] { new Vector2(1060, 129), new Vector2(1120, 129),
            new Vector2(1190, 334), new Vector2(990, 334) }, new Color(Brass, .07f));
        DrawColoredPolygon(new[] { new Vector2(1076, 121), new Vector2(1104, 121),
            new Vector2(1118, 136), new Vector2(1062, 136) }, Brass);
        UiKit.RadialGlow(this, new Vector2(area.GetCenter().X, 240), 110, accent, .1f);
        var oval = new Vector2[48];
        for (int i = 0; i < oval.Length; i++)
            oval[i] = new Vector2(area.GetCenter().X, 333) + Vector2.FromAngle(i * Mathf.Tau / oval.Length) * new Vector2(109, 13);
        DrawColoredPolygon(oval, new Color("826044"));
        if (_playerShot == null) return;
        UiKit.DrawPortrait(this, _playerShot, CharFoot, CharContentH);
        if (_buyFxT > 0)
        {
            float k = 1 - (float)(_buyFxT / .7);
            float y = area.End.Y - area.Size.Y * k;
            DrawLine(new Vector2(area.Position.X + 28, y), new Vector2(area.End.X - 28, y), new Color(Brass, .65f * (1 - k)), 2, true);
        }
    }

    private static Color UpgradeColor(string id) => id switch
    {
        "n_life_1" or "n_life_2" or "n_hitbox" or "n_heart_magnet" => new Color("efa1b6"),
        "n_hit_invul" => new Color("a9cfe8"),
        "n_bomb_1" or "n_charge_shot" or "n_charge" => new Color("e8ce96"),
        _ => Owned,
    };

    private void DrawCheck(Vector2 c, Color color, float scale)
    {
        DrawPolyline(new[] { c + new Vector2(-6, 0) * scale, c + new Vector2(-1, 5) * scale,
            c + new Vector2(8, -5) * scale }, color, 2 * scale, true);
    }

    private void DrawArrow(Vector2 c, Color color, float scale)
    {
        DrawLine(c + new Vector2(-9, 0) * scale, c + new Vector2(9, 0) * scale, color, 1.8f, true);
        DrawPolyline(new[] { c + new Vector2(3, -6) * scale, c + new Vector2(9, 0) * scale,
            c + new Vector2(3, 6) * scale }, color, 1.8f, true);
    }

    private void DrawUpgradeMark(string id, Vector2 c, Color color, float scale)
    {
        Vector2 P(float x, float y) => c + new Vector2(x, y) * scale;
        void L(float x, float y, float xx, float yy) => DrawLine(P(x, y), P(xx, yy), color, 1.8f, true);
        if (id is "n_life_1" or "n_life_2") UiKit.Heart(this, c, 9 * scale, color);
        else if (id == "n_heart_magnet")
        {
            UiKit.Heart(this, c, 6 * scale, color);
            L(-13, -5, -9, 0); L(-9, 0, -13, 5);
            L(13, -5, 9, 0); L(9, 0, 13, 5);
        }
        else if (id == "n_hit_invul")
        {
            DrawPolyline(new[] { P(0, -12), P(10, -8), P(8, 5), P(0, 12),
                P(-8, 5), P(-10, -8), P(0, -12) }, color, 1.8f, true);
            L(0, -6, 0, 1); L(0, 1, 4, 3);
        }
        else if (id is "n_dodge" or "n_dodge_cd" or "n_move_15x")
        {
            L(-10, -7, -3, 0); L(-3, 0, -10, 7); L(1, -7, 8, 0); L(8, 0, 1, 7);
        }
        else if (id == "n_bomb_1")
        {
            DrawArc(c, 7 * scale, 0, Mathf.Tau, 24, color, 1.8f, true);
            L(5, -5, 10, -10); L(8, -13, 8, -10); L(11, -8, 14, -8);
        }
        else if (id == "n_slow")
        {
            DrawPolyline(new[] { P(0, -11), P(3, -3), P(11, 0), P(3, 3), P(0, 11),
                P(-3, 3), P(-11, 0), P(-3, -3), P(0, -11) }, color, 1.8f, true);
        }
        else if (id == "n_hitbox")
        {
            DrawArc(c, 9 * scale, 0, Mathf.Tau, 32, color, 1.8f, true);
            L(-13, 0, -6, 0); L(6, 0, 13, 0); L(0, -13, 0, -6); L(0, 6, 0, 13);
        }
        else
            DrawPolyline(new[] { P(2, -11), P(-7, 2), P(0, 2), P(-2, 11), P(8, -2), P(1, -2), P(2, -11) }, color, 1.8f, true);
    }

    private (string now, string after) EffectPair(int i)
    {
        var g = _game;
        string id = Def(i).Id;
        bool owned = IsOwned(i);
        switch (id)
        {
            case "n_life_1":
            case "n_life_2":
            {
                int before = g.StartLives - (owned ? 1 : 0);
                return ($"LIFE  {before}", $"LIFE  {before + 1}");
            }
            case "n_dodge":     return ("未習得", "回避を習得\n無敵時間 0.45秒");
            case "n_dodge_cd":
                return ($"再使用 {0.80f * g.JobDef.DodgeCdMul:0.00}秒\n距離 {64 * g.JobDef.DodgeDistMul:0.0}",
                    $"再使用 {0.65f * g.JobDef.DodgeCdMul:0.00}秒\n距離 {76 * g.JobDef.DodgeDistMul:0.0}");
            case "n_bomb_1":
            {
                int cur = g?.StartBombs ?? 4;
                return (owned ? $"ボム {cur - 1}" : $"ボム {cur}", $"ボム {(owned ? cur : cur + 1)}");
            }
            case "n_charge_shot": return ("未習得", $"{g.ChargeNeedSec:0.00}秒で充填\n{g.JobDef.ChargeDescription}");
            case "n_charge":    return ($"1段目 {g.ChargeNeedSec:0.00}秒",
                                        $"2段目 {g.ChargeNeedSec * ChargeTier.HoldMul:0.00}秒\n威力×{ChargeTier.PowerMul:0.0}・大きさ×{ChargeTier.RadiusMul:0.0}");
            case "n_hitbox":    return ("判定半径 2.0px", "判定半径 1.5px");
            case "n_heart_magnet": return ($"回収半径 {ScoreShards.MagnetRadius:0}px",
                $"回収半径 {ScoreShards.MagnetRadius * GameManager.HeartMagnetUpgradeMul:0}px");
            case "n_hit_invul": return ($"被弾後 {g.JobDef.HitInvulSec:0.0}秒",
                $"被弾後 {g.JobDef.HitInvulSec + GameManager.HitInvulUpgradeBonus:0.0}秒");
            case "n_move_15x":  return ($"移動速度 {75 * g.JobDef.MoveMul:0.0}", $"移動速度 {112.5f * g.JobDef.MoveMul:0.0}");
            case "n_slow":      return ("未習得", $"{PlayerSpecial.NameFor(g.SelectedJob)}\n再使用 {PlayerSpecial.Cooldown:0}秒");
            default:            return ("—", Def(i).Desc);
        }
    }

    private void DrawFooter()
    {
        _backBtnRect = new Rect2(48, FooterY, 156, 36);
        bool backHover = UiKit.Hotspot(_backBtnRect, HsBack);
        if (backHover) UiKit.Box(this, _backBtnRect, Raised, 6);
        DrawArrow(new Vector2(68, FooterY + 18), backHover ? Ink : Muted, -0.8f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(92, FooterY + 6),
            string.IsNullOrEmpty(_game.PendingResumeScene) ? "ホーム" : "もどる", 17, backHover ? Ink : Muted);
        // 右側はヒント帯（_Draw の末尾）。旧「ためし撃ち」ボタンは詳細パネルの [強化する] の隣へ移した。
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        if (_toastIsDialogue) { DrawDialogueBubble(); return; }
        float alpha = Mathf.Min(1, (float)_toastT / 0.25f);
        DrawLine(new Vector2(DetailX, 567), new Vector2(DetailX, 626), new Color(_toastCol, alpha), 2);
        UiKit.Multi(this, UiKit.ZenBold, new Vector2(DetailX + 16, 580), _toast, 17,
            new Color(Ink, alpha), DetailW - 32, 2);
    }

    private void DrawDialogueBubble()
    {
        float alpha = Mathf.Min(Mathf.Clamp((float)_toastAge / 0.15f, 0, 1),
            Mathf.Clamp((float)_toastT / 0.3f, 0, 1));
        DrawLine(new Vector2(DetailX, 564), new Vector2(DetailX, 638), new Color(_toastCol, alpha), 2);
        UiKit.Multi(this, UiKit.ZenBold, new Vector2(DetailX + 16, 565), _toast, 16,
            new Color(Ink, alpha), DetailW - 32, 3);
    }
}
