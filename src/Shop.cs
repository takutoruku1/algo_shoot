using Godot;

public partial class Shop : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    private const float FooterY = 664f;
    private const float ColX = 48f, ColW = 444f;
    private const float RowTop = 134f, RowH = 30f, RowBigH = 38f, RowGap = 4f;
    private const float DetailX = 562f, DetailW = 670f;
    private static readonly Color Surface = new("141719");
    private static readonly Color Raised = new("202628");
    private static readonly Color Owned = new("98dec6");
    private static readonly Color Ink = new("edf3ef");
    private static readonly Color Muted = new("a1afaa");
    private static readonly Color Line = new("303a38");
    private static readonly Color Deny = new("f1a798");
    private Texture2D? _accountIcon;

    // 小話3（ショップの一言）：入店・購入時・退店でミナがぽつりと零す台詞。既存の Toast() で表示するだけ＝
    //   買い物のテンポを邪魔しない短時間表示（1.8秒）。docs/小話集_v1.md §3 の文面をそのまま採用。
    private static readonly string[] ShopEnterTalk =
    {
        "いらっしゃいませ。……冗談です、ご主人様しかいらっしゃいませんもの。",
        "さあ、わたくしを研いでくださいまし。",
        "お財布の中身、ちゃんと確認なさいました?",
        "本日の心の残高、しかとご報告いたします。",
        "急がなくて結構ですよ。ここは、時間が減りませんので。",
        "眺めているだけでも構いません。……買い物は、選んでいる時間がいちばん楽しいので。",
        "道は一本きりです。迷いようがありませんでしょう?",
        "ああ、これ。前もそこで迷っておられました。",
    };

    private static readonly string[] ShopBuyTalk =
    {
        "はい、たしかに。……染みますね、これは。",
        "またひとつ、ご主人様の色になりました。",
        "お買い上げ、ありがとうございます。領収書はご入用で?",
        "重くなった気がします。……気のせいですね。",
        "ご主人様、いい買い物です。わたくしが言うのですから間違いありません。",
        "これで、また一歩、遠くまで行けます。",
        // ユーザー承認済み: docs/20260914/ストーリー添削_2026-09-14.md 【3】（感情アークの厳格運用）
        //   ショップはあかり面クリア直後から開く＝まだ「笑う」を獲得していない区間を含むので、
        //   「ふふ」は置けない。観測の言い回しへ置換（内容と軽さは変えない）。
        "……育てられるのは、悪くありませんね。",
        "ありがとうございます。ちゃんと使いますので。",
    };

    private static readonly string[] ShopExitTalk =
    {
        "では、まいりましょう。……お忘れ物はありませんか。",
        "行ってまいります。……お留守番は、いたしませんので。",
        "支度は済みました。ご主人様の号令をどうぞ。",
        "戻ってきたら、また買い物に付き合ってくださいね。",
        "閉店です。……なんて、看板もないのですけれど。",
        "ご主人様、背筋。",
        "次に来るときは、もう少し稼いでおきます。",
    };

    private Texture2D? _playerShot;

    // フォーカス＝列のインデックス（0..13）。この画面には列以外の選択対象が無い＝番号体系はこれだけ。
    private int _sel;

    // 入力エッジ
    private bool _navHeld, _zHeld, _backHeld, _trainHeld;
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

    // マウス：段のカード（id=0..13）とフッタのボタン（負の予約帯）をホットスポットで拾う。
    private const int HsBack = -100;  // フッタ もどる
    private const int HsBuy = -101;   // 詳細パネルの「買う」ボタン
    private const int HsTrain = -102;
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
        // ショップ専用曲「シンプルスタイル」（2026-09-14〜。従来は BgmMenu の使い回し）。
        //   ハブより硬く電子的な音色で「移動した感」を耳に出す。周回で何十回も入るので平坦な曲を選んである。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmShop);
        _playerShot = ResourceLoader.Load<Texture2D>(_game.JobDef.PlayerTexturePath);
        _accountIcon = ResourceLoader.Load<Texture2D>(CompanionDialogue.AccountPortrait(_game.SelectedJob));

        // 起動時のカーソルは「次に買える段」に置く＝開いた瞬間に手が届くところを指している。
        //   全部買い切っていれば最終段（読み返す用）。
        _sel = NextIndex();
        if (_sel < 0) _sel = Steps - 1;

        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        // 移行があった直後だけ、何段引き継いだかを知らせる（黙って増えていると不信になる）。
        if (_game != null && _game.MigratedNodeCount >= 0)
            Toast($"前の強化を引き継ぎました（{_game.MigratedNodeCount}段）", UiKit.Info);
        else
            ShowShopTalk(CompanionDialogue.Menu.ShopEnter, ShopEnterTalk);
    }

    // 次に買える段のインデックス（全部所持なら -1）。
    private int NextIndex()
    {
        for (int i = 0; i < Steps; i++)
            if ((_game?.GetUpgradeLevel(Def(i).Id) ?? 0) < 1) return i;
        return -1;
    }
    private bool IsOwned(int i) => (_game?.GetUpgradeLevel(Def(i).Id) ?? 0) >= 1;

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
            if (_exitDelayT <= 0) GetTree().ChangeSceneToFile(_pendingExitDest);
            return;
        }
        if (_autoplay) { ExitShop(); return; }

        // ポーズメニュー（M で重なる）を閉じた Esc/M/Z の同じ押下がこのフレームに漏れて
        // 「もどる＝ショップごと閉じる」「購入」が誤発火しないよう、ゲート中は全キーを既押し扱いで食う。
        if (Pad.UiBlocked(this))
        {
            _navHeld = _zHeld = _backHeld = _trainHeld = true;
            QueueRedraw();
            return;
        }

        // ── マウス クリック（キーボード/パッドへ純粋に追加）──
        //   段のカードは「クリックで選ぶ」。既に選んでいる段をもう一度クリックしたら確定（＝買う）。
        //   ボタン類（買う／もどる／トレーニング）は1クリックで即発火。
        if (Pad.MouseClick())
        {
            int hit = UiKit.ClickedId(true);
            if (hit == HsBack) { Audio.Instance?.PlayUiCancel(); ExitShop(); }
            else if (hit == HsTrain) EnterTraining();
            else if (hit == HsBuy) OnConfirm();
            else if (hit >= 0 && hit < Steps)
            {
                if (hit == _sel) OnConfirm();
                else { _sel = hit; Audio.Instance?.PlayUiMove(); }
            }
        }

        // カーソル移動：一本道なので上下だけ（左右も同じ意味に割り当てる＝どのキーでも動く）。
        bool up = Input.IsActionPressed("ui_up") || Input.IsActionPressed("ui_left");
        bool down = Input.IsActionPressed("ui_down") || Input.IsActionPressed("ui_right");
        bool any = up || down;
        if (any && !_navHeld)
        {
            _sel = Mathf.Clamp(_sel + (up ? -1 : 1), 0, Steps - 1);
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = any;

        // Z：買う。
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2) OnConfirm();

        // X／Esc：もどる（Esc は 2026-09-26 に「一つ前の画面へ」として復帰。メニューを開くのは M）。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2) { Audio.Instance?.PlayUiCancel(); ExitShop(); }

        // T：トレーニング（試し打ち場）へ。
        bool train = Input.IsKeyPressed(Key.T);
        bool trainEdge = train && !_trainHeld; _trainHeld = train;
        if (trainEdge && _t > 0.2) EnterTraining();

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
        GetTree().ChangeSceneToFile("res://Training.tscn");
    }

    // Z（または「買う」ボタン／選択中の段の再クリック）で呼ばれる。買えない理由はトーストで返す。
    private void OnConfirm()
    {
        if (_game == null) return;
        var d = Def(_sel);
        if (IsOwned(_sel)) { Audio.Instance?.PlayUiDeny(); Toast("もう持っています", UiKit.Text4); return; }
        // 一本道：直前の段を持っていないと買えない。何が足りないかを名前で返す（番号や記号で言わない）。
        if (!_game.IsParentMet(d.Id))
        {
            var prev = GameManager.GetUpgradeDef(d.ParentId);
            Audio.Instance?.PlayUiDeny();
            Toast($"さきに「{prev?.Name ?? "ひとつ上"}」から", Deny);
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

        // 買ったら、そのまま次の段へカーソルを送る＝一本道の「つぎ」が指先に来る。
        int nx = NextIndex();
        if (nx >= 0) _sel = nx;
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

        UiKit.EndDesign(this);
    }

    private void DrawBg()
    {
        DrawRect(new Rect2(0, 0, W, H), Surface);
        DrawRect(new Rect2(0, 0, W, 88), new Color("191e20"));
        DrawRect(new Rect2(0, 88, W, 1), Line);
        DrawLine(new Vector2(526, 110), new Vector2(526, 638), Line, 1);
        DrawRect(new Rect2(0, 650, W, 70), new Color("191e20"));
        DrawRect(new Rect2(0, 650, W, 1), Line);
    }

    private static string Money(long value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private void DrawHeader()
    {
        UiKit.Box(this, new Rect2(48, 22, 44, 44), Owned, 8);
        DrawUpgradeMark("n_power_2x", new Vector2(70, 44), Surface, 1.3f);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(108, 25), "強化ショップ", 28, Ink);

        UiKit.FaceAvatar(this, new Vector2(600, 44), 20, _accountIcon,
            CompanionDialogue.Accent(_game.SelectedJob), false, 0, 1, _t);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(634, 31), _game.JobDef.CharacterName, 19, Ink);
        string money = Money(_game.Impression);
        int size = UiKit.TextW(UiKit.Mono, money, 28) > 280 ? 22 : 28;
        float moneyW = UiKit.TextW(UiKit.Mono, money, size);
        UiKit.Text(this, UiKit.Zen, new Vector2(1232 - moneyW - 122, 36), "浄化した心", 14, Muted);
        UiKit.Heart(this, new Vector2(1232 - moneyW - 20, 45), 8, Owned);
        UiKit.Text(this, UiKit.Mono, new Vector2(1232 - moneyW, 27), money, size,
            _walletPopT > 0 ? Owned : Ink);
    }

    private void DrawColumn()
    {
        int next = NextIndex();
        UiKit.Text(this, UiKit.ZenBold, new Vector2(ColX, 103), "強化リスト", 15, Muted);
        UiKit.Text(this, UiKit.Mono, new Vector2(ColX + ColW - 96, 103),
            $"{(next < 0 ? Steps : next):00} / {Steps:00}", 15, Owned, HorizontalAlignment.Right, 96);

        for (int i = 0; i < Steps; i++)
        {
            var r = RowRect(i);
            bool owned = IsOwned(i), focus = i == _sel;
            bool hover = UiKit.Hotspot(r, i);
            if (i < Steps - 1)
                DrawLine(new Vector2(ColX + 17, r.GetCenter().Y),
                    new Vector2(ColX + 17, RowRect(i + 1).GetCenter().Y),
                    owned ? new Color(Owned, 0.7f) : Line, 2);
            DrawRow(i, r, owned, i == next, focus, hover);
        }
    }

    private void DrawRow(int i, Rect2 r, bool owned, bool next, bool focus, bool hover)
    {
        var d = Def(i);
        Color accent = UpgradeColor(d.Id);
        if (focus)
        {
            UiKit.Box(this, r, Raised, 6, new Color(Owned, 0.75f), 1);
            DrawRect(new Rect2(r.Position.X, r.Position.Y + 6, 3, r.Size.Y - 12), Owned);
        }
        else if (hover) UiKit.Box(this, r, new Color("1c2224"), 6);
        else DrawLine(new Vector2(r.Position.X + 40, r.End.Y), r.End, new Color(Line, 0.5f), 1);

        var node = new Vector2(r.Position.X + 17, r.GetCenter().Y);
        DrawCircle(node, 10, Surface);
        if (owned)
        {
            DrawCircle(node, 6, new Color(Owned, 0.13f));
            DrawCheck(node, Owned, 0.65f);
        }
        else
        {
            DrawArc(node, next ? 6 : 4, 0, Mathf.Tau, 24, next ? Owned : Muted, next ? 2 : 1, true);
            if (next) DrawCircle(node, 2, Owned);
        }

        DrawUpgradeMark(d.Id, new Vector2(r.Position.X + 53, r.GetCenter().Y),
            owned || focus || next ? accent : Muted, 0.75f);
        float textY = r.GetCenter().Y - UiKit.ZenBold.GetHeight(17) / 2;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X + 78, textY),
            d.Name, 17, owned || focus || next ? Ink : Muted);
        if (owned)
            UiKit.Text(this, UiKit.Zen, new Vector2(r.End.X - 86, r.GetCenter().Y - 10),
                "購入済み", 13, Owned, HorizontalAlignment.Right, 72);
        else
            UiKit.Text(this, UiKit.Mono, new Vector2(r.End.X - 92, r.GetCenter().Y - 10),
                Money(d.BaseCost), 14, next && _game.Impression < d.BaseCost ? Deny : next ? Ink : Muted,
                HorizontalAlignment.Right, 78);

        if (_buyFxId == d.Id && _buyFxT > 0)
        {
            float k = (float)(_buyFxT / 0.7);
            DrawRect(new Rect2(r.Position.X + 39, r.End.Y - 2, (r.Size.X - 39) * (1 - k), 2),
                new Color(Owned, k));
        }
    }

    private void DrawDetail()
    {
        var d = Def(_sel);
        bool owned = IsOwned(_sel), unlocked = _game.IsParentMet(d.Id);
        Color accent = UpgradeColor(d.Id);
        UiKit.Text(this, UiKit.Mono, new Vector2(DetailX, 112), $"UPGRADE / {_sel + 1:00}", 14, Muted);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(DetailX, 154), d.Name, 34, Ink);
        if (GameManager.IsAbilityNode(d.Id))
            UiKit.Text(this, UiKit.Mono, new Vector2(DetailX, 209), "ABILITY", 13, accent);
        UiKit.Multi(this, UiKit.Zen, new Vector2(DetailX, 241), d.Desc, 18, Muted, 330, 2);

        DrawUpgradeMark(d.Id, new Vector2(DetailX + 22, 319), accent, 1.4f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(DetailX + 55, 306),
            owned ? "購入済み" : unlocked ? "次の強化" : "未解放", 17, owned || unlocked ? Owned : Muted);
        DrawCharacter();

        var (now, after) = EffectPair(_sel);
        DrawLine(new Vector2(DetailX, 364), new Vector2(DetailX + DetailW, 364), Line, 1);
        float afterX = DetailX + 358;
        UiKit.Text(this, UiKit.Zen, new Vector2(DetailX, 380), "強化前", 13, Muted);
        UiKit.Text(this, UiKit.Zen, new Vector2(afterX, 380), "強化後", 13, Owned);
        UiKit.Multi(this, UiKit.ZenBold, new Vector2(DetailX, 407), now, 18, Ink, 284, 2);
        UiKit.Multi(this, UiKit.ZenBold, new Vector2(afterX, 407), after, 18, Owned, 284, 2);
        DrawArrow(new Vector2(DetailX + 322, 421), Muted, 1);

        long cost = _game.GetUpgradeCost(d.Id);
        bool enough = _game.Impression >= cost;
        _buyBtnActive = !owned && unlocked && enough;
        _buyBtnRect = new Rect2(DetailX, 486, DetailW, 56);
        bool hover = _buyBtnActive && UiKit.Hotspot(_buyBtnRect, HsBuy);
        Color fill = _buyBtnActive ? (hover ? new Color("c2f3e1") : Owned) : Raised;
        Color foreground = _buyBtnActive ? Surface : Muted;
        UiKit.Box(this, _buyBtnRect, fill, 8, _buyBtnActive ? null : Line, 1);
        string label = owned ? "購入済み"
            : !unlocked ? $"「{GameManager.GetUpgradeDef(d.ParentId)!.Name}」の購入で解放"
            : !enough ? $"あと {Money(cost - _game.Impression)}"
            : "強化する";
        if (_buyBtnActive)
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(DetailX + 24, 500), label, 20, foreground);
            string price = Money(cost);
            float pw = UiKit.TextW(UiKit.Mono, price, 20);
            UiKit.Heart(this, new Vector2(DetailX + DetailW - pw - 74, 514), 7, foreground);
            UiKit.Text(this, UiKit.Mono, new Vector2(DetailX + DetailW - pw - 54, 500), price, 20, foreground);
            DrawArrow(new Vector2(DetailX + DetailW - 26, 514), foreground, 0.8f);
        }
        else
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(DetailX + 20, 502), label, 17,
                unlocked && !owned ? Deny : Muted, HorizontalAlignment.Center, DetailW - 40);
        }

        int count = 0;
        for (int i = 0; i < Steps; i++) if (IsOwned(i)) count++;
        if (_toastT <= 0)
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(DetailX, 574), "強化進行", 13, Muted);
            UiKit.Text(this, UiKit.Mono, new Vector2(DetailX + DetailW - 110, 572),
                $"{count:00} / {Steps:00}", 16, Owned, HorizontalAlignment.Right, 110);
            float segment = (DetailW - (Steps - 1) * 5) / Steps;
            for (int i = 0; i < Steps; i++)
                UiKit.Box(this, new Rect2(DetailX + (segment + 5) * i, 610, segment, 4),
                    IsOwned(i) ? Owned : Line, 2);
        }
    }

    private void DrawCharacter()
    {
        var area = new Rect2(948, 110, 284, 244);
        Color accent = CompanionDialogue.Accent(_game.SelectedJob);
        DrawLine(new Vector2(964, 134), new Vector2(980, 134), new Color(accent, 0.65f), 1);
        DrawLine(new Vector2(964, 134), new Vector2(964, 150), new Color(accent, 0.65f), 1);
        DrawLine(new Vector2(1216, 330), new Vector2(1200, 330), new Color(accent, 0.65f), 1);
        DrawLine(new Vector2(1216, 330), new Vector2(1216, 314), new Color(accent, 0.65f), 1);
        if (_playerShot == null) return;
        Vector2 size = _playerShot.GetSize();
        float scale = Mathf.Min(228 / size.X, 244 / size.Y);
        size *= scale;
        var rect = new Rect2(area.GetCenter() - size / 2, size);
        DrawTextureRect(_playerShot, rect, false);
        if (_buyFxT > 0)
        {
            float k = 1 - (float)(_buyFxT / 0.7);
            float y = area.End.Y - area.Size.Y * k;
            DrawLine(new Vector2(area.Position.X + 28, y), new Vector2(area.End.X - 28, y),
                new Color(Owned, 0.65f * (1 - k)), 2, true);
        }
    }

    private static Color UpgradeColor(string id) => id switch
    {
        "n_life_1" or "n_life_2" or "n_hitbox" => new Color("efa1b6"),
        "n_bomb_1" or "n_power_2x" or "n_charge" or "n_lines" or "n_rate_2x" or "n_pierce" => new Color("e8ce96"),
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
        else if (id is "n_dodge" or "n_dodge_cd" or "n_move_15x")
        {
            L(-10, -7, -3, 0); L(-3, 0, -10, 7); L(1, -7, 8, 0); L(8, 0, 1, 7);
        }
        else if (id == "n_bomb_1")
        {
            DrawArc(c, 7 * scale, 0, Mathf.Tau, 24, color, 1.8f, true);
            L(5, -5, 10, -10); L(8, -13, 8, -10); L(11, -8, 14, -8);
        }
        else if (id is "n_hitbox" or "n_slow")
        {
            DrawArc(c, 9 * scale, 0, Mathf.Tau, 32, color, 1.8f, true);
            if (id == "n_slow") { L(0, -5, 0, 0); L(0, 0, 4, 2); }
            else { L(-13, 0, -6, 0); L(6, 0, 13, 0); L(0, -13, 0, -6); L(0, 6, 0, 13); }
        }
        else if (id is "n_lines" or "n_rate_2x")
        {
            L(-8, -8, 8, -8); L(-8, 0, 8, 0); L(-8, 8, 8, 8);
        }
        else if (id == "n_option")
        {
            DrawArc(c, 5 * scale, 0, Mathf.Tau, 20, color, 1.8f, true);
            DrawCircle(P(-11, 0), 2 * scale, color); DrawCircle(P(11, 0), 2 * scale, color);
        }
        else if (id == "n_pierce") { DrawArrow(c, color, scale); L(3, -10, 3, 10); }
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
                int before = g.StartLives - g.MaxLifeBonus + (id == "n_life_2" ? 1 : 0);
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
            case "n_power_2x":  return ("弾の火力 ×1", "弾の火力 ×2");
            // 効果の文面に「→」を混ぜない（表示側が値と値を矢印でつなぐので、二重の矢印になって読めなくなる）。
            // 溜め打ちは最初から使える（2026-09-25）＝この段が売るのは「もう一段」。
            //   左右とも「何秒ためて、どれだけの一発が出るか」を並べる（秒と倍率は ChargeTier が正典）。
            case "n_charge":    return ($"チャージ {g.ChargeNeedSec:0.00}秒 まで",
                                        $"チャージ {g.ChargeNeedSec * ChargeTier.HoldMul:0.00}秒 まで\n二段目は 威力 ×{ChargeTier.PowerMul:0.0} / 弾 ×{ChargeTier.RadiusMul:0.0}");
            case "n_hitbox":    return ("当たり判定 2.0px", "当たり判定 1.0px");
            case "n_lines":
            {
                return g.SelectedJob == Job.Magic ? ("同時発射 5発", "同時発射 7発") : ("同時発射 2発", "同時発射 3発");
            }
            case "n_move_15x":  return ($"移動速度 {75 * g.JobDef.MoveMul:0.0}", $"移動速度 {112.5f * g.JobDef.MoveMul:0.0}");
            case "n_slow":      return ("未習得", "敵の時間 ×0.35 / 1.5秒\n再使用 20秒");
            case "n_rate_2x":   return ("発射間隔 ×1.0", "発射間隔 ×0.5");
            case "n_pierce":    return ("弾は1体で消える", "どの撃ち方でも 敵1体を貫く");
            case "n_option":    return ("オプション なし", "オプション 1基\n威力 ×0.5 で同時射撃");
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

        _trainBtnRect = new Rect2(980, FooterY, 168, 36);
        bool trainHover = UiKit.Hotspot(_trainBtnRect, HsTrain);
        if (trainHover) UiKit.Box(this, _trainBtnRect, Raised, 6);
        DrawUpgradeMark("n_hitbox", new Vector2(1002, FooterY + 18), trainHover ? Ink : Muted, 0.8f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(1026, FooterY + 6), "ためし撃ち", 17, trainHover ? Ink : Muted);
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
