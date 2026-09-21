using Godot;

// Shop : ミナ強化ショップ。★2026-09-13 に「分岐なし・一本道13段」へ作り直した（2026-09-22 に回避を段に加えて14段）。
//
//   それまでは 70 ノードの二分木（排他フォーク・振り直し・おすすめ誘導・装備チップ・縦横スクロール）だった。
//   選択肢が多すぎて「次に何を買えばいいか」が決められず、木を見るために画面を動かす必要があり、
//   おすすめの金パルスが「結局これを買えばいい」と答えを出してしまっていた＝選ばせているようで選ばせていない。
//
//   作り直しの方針はひとつだけ：**買える段は常にひとつ**。
//     ・縦1列14段。上から順にしか買えない（順序条件は GameManager.IsParentMet＝直前の段の所持）。
//     ・1画面に収める。スクロールなし・カメラなし・ミニマップなし。
//     ・ノード名＝効果そのもの（「ハート +1」「火力 2倍」）。説明の地の文を読ませない。
//     ・買った段は点灯して縦線で連結、次の1段だけ光って呼吸、先の段は暗いが名前と価格は見せる
//       ＝「いまどこまで来たか」と「この先どこまで行けるか」が、動かさずに一目で分かる。
//     ・能力を覚える段（#2 回避／#7 溜め打ち／#11 集中モード）だけ一回り大きい＝数値ではなく手が増える段の格。
//     ・詳細は「買う前の値 → 買った後の値」の矢印1行のみ（長い段だけ矢印を行頭に落として2行）。
//   操作：↑↓ えらぶ ／ Z 買う ／ X もどる ／ T トレーニング（マウスはクリックで選択＋確定）。
//
//   撤去したもの（復活させないこと）：振り直し／おすすめ誘導と金パルス／排他と封印／装備チップと C 装備操作／
//   系統バナー／カプストーン解放パルス／スクロールバーとカメラ。
public partial class Shop : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    // ───── 版面（すべて設計座標 1280×720・1画面固定）─────
    //   左に列（段のカード）、右に詳細。上にヘッダ（財布）、下にフッタ（操作）。
    private const float HeaderH = 92f;         // ヘッダの高さ
    private const float FooterY = 660f;        // フッタの基準 Y
    private const float ColX = 72f;            // 列の左端
    private const float ColW = 470f;           // カードの幅
    // 14段ぶんの高さ（通常31×11＋大42×3＋隙間5×13）= 532 に収める。RowTop=104 で下端は 636＝
    //   フッタの区切り線（FooterY-14=646）より上＝最終段が footer に食い込まない。版面は1画面固定なので、
    //   段を増やすならここの3値のどれかを必ず詰めること（スクロールは付けない）。
    //   ★2026-09-22：回避（#2・大）が加わって13→14段になったので 通常34→31・大44→42・隙間6→5 に詰めた
    //     （13段時の下端 638 とほぼ同じ位置に収まる。本文17px／見出し20px は行内に余白が残る）。
    private const float RowTop = 104f;         // 1段目の上端
    private const float RowH = 31f;            // 通常の段の高さ
    private const float RowBigH = 42f;         // 能力を覚える段（#2 #7 #11）の高さ＝一回り大きい
    private const float RowGap = 5f;           // 段と段の隙間（ここに縦の連結線が通る）
    private const float DetailX = 600f;        // 詳細パネルの左端
    private const float DetailW = 608f;        // 同・幅

    // ───── 色（UiKit の語彙だけで足りる。この画面だけの新色は作らない）─────
    private static readonly Color Owned = UiKit.Purify;   // 買った段＝浄化の水色（点灯）
    private static readonly Color NextUp = UiKit.Gold;    // 次に買える1段＝金（呼吸する）
    private static readonly Color Far = UiKit.Text4;      // まだ先の段＝沈んだ灰
    private static readonly Color Deny = new("ef9a9a");   // 買えない理由（赤）

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

    // フォーカス＝列のインデックス（0..12）。この画面には列以外の選択対象が無い＝番号体系はこれだけ。
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

    // マウス：段のカード（id=0..12）とフッタのボタン（負の予約帯）をホットスポットで拾う。
    private const int HsBack = -100;  // フッタ もどる
    private const int HsBuy = -101;   // 詳細パネルの「買う」ボタン
    private const int HsTrain = -102; // ヘッダ トレーニング
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
        _playerShot = ResourceLoader.Load<Texture2D>(_game.SelectedJob == Job.Tank
            ? "res://char/mina_shoot.png" : _game.JobDef.PlayerTexturePath);

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

        // ポーズメニュー（Esc で重なる）を閉じた Esc/Z の同じ押下がこのフレームに漏れて
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

        // X：もどる（Esc は 2026-09-14 に外した＝どの画面でもポーズメニューを開く役へ一本化）。
        bool back = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.B);
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
        DrawRect(new Rect2(0, 0, W, H), UiKit.BgDeep);
        // 列の後ろにだけ、うっすら縦の帯を敷く＝「一本の道」であることを地の色でも言う。
        //   上下の余白は 14px。フッタの区切り線（FooterY-14）を越えないよう、下端は最終段+14 までに留める。
        float top = RowTop - 14f;
        float bot = RowY(Steps - 1) + RowHeightOf(Steps - 1) + 14f;
        UiKit.Box(this, new Rect2(ColX - 22f, top, ColW + 44f, bot - top), new Color(1f, 1f, 1f, 0.022f), 18f);
    }

    private void DrawHeader()
    {
        // 見出しはひとつだけ。以前は小ラベル "SHOP" ＋ 大見出し「つよくなる」の二段だったが、
        //   同じことを二度言っているだけなので "SHOP" を FontTitle へ昇格して1行に畳んだ。
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(ColX, 36f), "SHOP", UiKit.FontTitle, UiKit.White);

        // 財布（購入の瞬間だけ跳ねる）。
        float pop = 1f + 0.10f * (float)Mathf.Max(0, _walletPopT) / 0.5f;
        string money = $"{_game?.Impression ?? 0}";
        float mw = UiKit.TextW(UiKit.Mono, money, UiKit.FontTitle) * pop;
        UiKit.Text(this, UiKit.Zen, new Vector2(W - 72f - mw - 96f, 52f), "浄化した心", UiKit.FontLabel, UiKit.Text3);
        UiKit.Text(this, UiKit.Mono, new Vector2(W - 72f - mw, 42f), money,
                   Mathf.RoundToInt(UiKit.FontTitle * pop), _walletPopT > 0 ? UiKit.Gold : UiKit.White);

        // トレーニング（試し打ち場）への入口。列の外＝買い物の流れを邪魔しない位置に小さく置く。
        _trainBtnRect = new Rect2(DetailX, 30f, 168f, 30f);
        bool hovT = UiKit.Hotspot(_trainBtnRect, HsTrain);
        UiKit.Box(this, _trainBtnRect, new Color(1f, 1f, 1f, hovT ? 0.10f : 0.05f), 9f, new Color(UiKit.Info, hovT ? 0.7f : 0.3f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(_trainBtnRect.Position.X, 37f),
                   "T  ためし撃ち", UiKit.FontLabel, hovT ? UiKit.White : UiKit.Text3,
                   HorizontalAlignment.Center, _trainBtnRect.Size.X);

        DrawRect(new Rect2(ColX, HeaderH - 10f, W - ColX * 2f, 1f), new Color(1f, 1f, 1f, 0.10f));
    }

    // ───── 列（本体）─────
    //   買った段：水色で点灯し、次の段へ太い縦線で連結する（道が伸びていく）。
    //   次の1段：金で呼吸する（今ここ）。
    //   先の段：沈んだ灰。それでも**名前と価格は出す**＝この先どこまで行けるかを隠さない。
    private void DrawColumn()
    {
        int next = NextIndex();
        for (int i = 0; i < Steps; i++)
        {
            var r = RowRect(i);
            bool owned = IsOwned(i);
            bool isNext = i == next;
            bool focus = i == _sel;
            bool big = GameManager.IsAbilityNode(Def(i).Id);

            // ── 段と段をつなぐ縦線（この段と次の段のあいだ）。買った先までが明るい＝進んだ距離が線で見える ──
            if (i < Steps - 1)
            {
                float lx = ColX + 26f;
                float y0 = r.End.Y, y1 = RowY(i + 1);
                // 買った段から出る線は水色、これから先は細く沈める。
                bool lit = owned;
                DrawLine(new Vector2(lx, y0), new Vector2(lx, y1),
                         lit ? new Color(Owned, 0.85f) : new Color(1f, 1f, 1f, 0.10f), lit ? 2.4f : 1.2f);
            }

            UiKit.Hotspot(r, i);
            DrawRow(i, r, owned, isNext, focus, big);
        }
    }

    private void DrawRow(int i, Rect2 r, bool owned, bool isNext, bool focus, bool big)
    {
        var d = Def(i);
        // 呼吸（次の1段だけ）。0..1 でゆっくり往復。
        float breath = isNext ? 0.5f + 0.5f * Mathf.Sin((float)_t * 2.6f) : 0f;
        Color accent = owned ? Owned : isNext ? NextUp : Far;

        // 地と縁。フォーカス中は縁を強く、次の1段は呼吸で明滅させる。
        float bAlpha = owned ? 0.55f : isNext ? 0.45f + 0.35f * breath : 0.16f;
        Color bg = owned ? new Color(0.07f, 0.12f, 0.15f, 0.70f)
                 : isNext ? new Color(0.14f, 0.12f, 0.06f, 0.62f)
                          : new Color(0.07f, 0.06f, 0.11f, 0.45f);
        if (focus) bg = bg.Lerp(new Color(0.18f, 0.20f, 0.28f, 0.85f), 0.45f);
        UiKit.Box(this, r, bg, big ? 13f : 10f, new Color(accent, focus ? 0.95f : bAlpha), focus ? 2.0f : (big ? 1.4f : 1f));

        // 購入直後のグロー（買った段が一瞬ふくらむ）。
        if (_buyFxId == d.Id && _buyFxT > 0)
        {
            float k = (float)(_buyFxT / 0.7);
            UiKit.Box(this, r.Grow(4f * k), null, big ? 16f : 13f, new Color(Owned, 0.8f * k), 2.4f * k);
        }

        // ── 左端のしるし：買った＝塗りつぶした丸／次＝呼吸する輪／先＝小さな点 ──
        var dot = new Vector2(r.Position.X + 26f, r.Position.Y + r.Size.Y / 2f);
        if (owned)
        {
            DrawCircle(dot, big ? 8f : 6.5f, new Color(Owned, 0.30f));
            DrawCircle(dot, big ? 5f : 4f, Owned);
        }
        else if (isNext)
        {
            DrawArc(dot, (big ? 8f : 6.5f) + 1.5f * breath, 0f, Mathf.Tau, 22, new Color(NextUp, 0.55f + 0.45f * breath), 1.8f);
            DrawCircle(dot, 2.4f, new Color(NextUp, 0.75f + 0.25f * breath));
        }
        else
        {
            DrawCircle(dot, 2.4f, new Color(1f, 1f, 1f, 0.22f));
        }

        // ── 名前（＝効果そのもの）。能力を覚える段はひと回り大きい字で「格」を付ける ──
        int nameSize = big ? UiKit.FontHeading : UiKit.FontBody;
        Color nameCol = owned ? UiKit.White : isNext ? UiKit.White : UiKit.Text4;
        float ty = r.Position.Y + (r.Size.Y - nameSize) / 2f - 2f;
        UiKit.Text(this, big ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(r.Position.X + 46f, ty),
                   d.Name, nameSize, nameCol);

        // ── 右端：買った段は「済」、それ以外は価格。足りないときだけ赤く沈める ──
        if (owned)
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(r.End.X - 74f, ty + 2f), "済", UiKit.FontLabel, new Color(Owned, 0.9f),
                       HorizontalAlignment.Right, 58f);
        }
        else
        {
            long cost = d.BaseCost;
            bool afford = (_game?.Impression ?? 0) >= cost;
            Color cc = isNext ? (afford ? UiKit.Gold : Deny) : new Color(UiKit.Text4, afford ? 0.85f : 0.55f);
            UiKit.Text(this, UiKit.Mono, new Vector2(r.End.X - 132f, ty + 2f), $"{cost}", UiKit.FontLabel, cc,
                       HorizontalAlignment.Right, 116f);
        }
    }

    // ───── 詳細：「いま → 買うと」の2行だけ ─────
    private void DrawDetail()
    {
        var d = Def(_sel);
        bool owned = IsOwned(_sel);
        bool big = GameManager.IsAbilityNode(d.Id);

        float x = DetailX, y = RowTop, w = DetailW;

        float fy = y, fh = 96f;
        UiKit.Box(this, new Rect2(x, fy, w, fh), new Color(0.05f, 0.06f, 0.12f, 0.6f), 12f, new Color(UiKit.Mina, 0.25f), 1f);
        if (_playerShot != null)
        {
            float ih = 78f, iw = ih * _playerShot.GetWidth() / Mathf.Max(1, _playerShot.GetHeight());
            DrawTextureRect(_playerShot, new Rect2(x + 18f, fy + (fh - ih) / 2f, iw, ih), false);
        }
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 128f, fy + fh / 2f - 24f),
                   _game!.JobDef.CharacterName,
                   UiKit.FontLabel, UiKit.Text3);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 128f, fy + fh / 2f - 4f),
                   $"{_sel + 1} / {Steps} 段目", UiKit.FontHeading, UiKit.White);

        // 見出し（段の名前）。
        float hy = fy + fh + 26f;
        if (big)
            UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x, hy - 16f), "NEW ABILITY", UiKit.Gold);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, hy), d.Name, UiKit.FontTitle,
                   owned ? Owned : UiKit.White);

        // ── 現在値 → 購入後 ──（この画面で読ませる地の文はここだけ）
        //   旧版は「いま」「買うと」の2ラベルで対比していたが、所持済みの段では「いま」が
        //   “買う前の値”を指す（＝画面の「いま」と意味が反転する）ので読み違えが起きていた。
        //   ラベルはひとつだけ置き、値そのものは矢印でつなぐ＝どちらからどちらへ動くかを記号で言う。
        var (now, after) = EffectPair(_sel);
        float ey = hy + 52f;
        UiKit.Box(this, new Rect2(x, ey, w, 84f), new Color(1f, 1f, 1f, 0.035f), 12f);
        Color afterCol = owned ? new Color(Owned, 0.95f) : UiKit.Gold;
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + 20f, ey + 13f),
                   owned ? "買って、こうなった" : "買うとどうなる", UiKit.Text4);

        // 1行に畳めるなら「現在値 → 購入後」。長い段（集中モード等）だけ矢印を行頭に落として2行にする。
        const float arrowGap = 10f;
        float bodyX = x + 20f, bodyW = w - 40f;
        string arrow = "→";
        float nowW = UiKit.TextW(UiKit.ZenBold, now, UiKit.FontBody);
        float arrowW = UiKit.TextW(UiKit.ZenBold, arrow, UiKit.FontBody);
        float afterW = UiKit.TextW(UiKit.ZenBold, after, UiKit.FontBody);
        if (nowW + afterW + arrowW + arrowGap * 2f <= bodyW)
        {
            float ty2 = ey + 45f;
            UiKit.Text(this, UiKit.ZenBold, new Vector2(bodyX, ty2), now, UiKit.FontBody, UiKit.Text4);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(bodyX + nowW + arrowGap, ty2), arrow, UiKit.FontBody, UiKit.Text3);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(bodyX + nowW + arrowGap + arrowW + arrowGap, ty2),
                       after, UiKit.FontBody, afterCol);
        }
        else
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(bodyX, ey + 32f), now, UiKit.FontBody, UiKit.Text4);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(bodyX, ey + 56f), arrow, UiKit.FontBody, UiKit.Text3);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(bodyX + arrowW + arrowGap, ey + 56f),
                       after, UiKit.FontBody, afterCol, HorizontalAlignment.Left, bodyW - arrowW - arrowGap);
        }

        // ── 買うボタン（状態で文言と色が変わる。押せないときは理由をそのまま書く）──
        float by = ey + 110f;
        string label; bool enabled = false;
        if (owned) { label = "もう持っています"; }
        else if (!(_game?.IsParentMet(d.Id) ?? false))
        {
            var prev = GameManager.GetUpgradeDef(d.ParentId);
            label = $"さきに「{prev?.Name ?? "ひとつ上"}」から";
        }
        else if ((_game?.Impression ?? 0) < d.BaseCost)
            label = $"あと {d.BaseCost - (_game?.Impression ?? 0)} たりません";
        else { label = $"{Pad.ConfirmToken}  買う（{d.BaseCost}）"; enabled = true; }

        _buyBtnRect = new Rect2(x, by, 300f, 44f);
        _buyBtnActive = enabled;
        bool hov = enabled && UiKit.Hotspot(_buyBtnRect, HsBuy);
        Color bc = enabled ? UiKit.Gold : UiKit.Text4;
        UiKit.Box(this, _buyBtnRect, new Color(bc, enabled ? (hov ? 0.28f : 0.16f) : 0.05f), 12f, new Color(bc, enabled ? 0.9f : 0.3f), enabled ? 1.6f : 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(_buyBtnRect.Position.X, by + 13f), label, UiKit.FontLabel,
                   enabled ? UiKit.White : UiKit.Text4, HorizontalAlignment.Center, _buyBtnRect.Size.X);

        // 進み具合（何段まで来たか）。列を数えなくても一目で分かる小さな帯。
        int ownedCount = 0;
        for (int i = 0; i < Steps; i++) if (IsOwned(i)) ownedCount++;
        float py = by + 66f;
        UiKit.Text(this, UiKit.Zen, new Vector2(x, py), $"{ownedCount} / {Steps} 段", UiKit.FontLabel, UiKit.Text3);
        float pw = w, ph = 4f;
        UiKit.Box(this, new Rect2(x, py + 24f, pw, ph), new Color(1f, 1f, 1f, 0.08f), 2f);
        if (ownedCount > 0)
            UiKit.Box(this, new Rect2(x, py + 24f, pw * ownedCount / Steps, ph), Owned, 2f);
    }

    // 矢印の左右（買う前の値 → 買った後の値）を作る。所持済みの段では左＝過去の値・右＝いまの値になる
    //   （表示側のラベルが「買って、こうなった」に変わるので、左右の意味は所持前後で一貫する）。
    //   ここは各段の効果を人の言葉で1行にするだけ＝数式は GameManager のアクセサが正典。
    private (string now, string after) EffectPair(int i)
    {
        var g = _game;
        string id = Def(i).Id;
        bool owned = IsOwned(i);
        // 「いま」は“この段を持っていない状態の値”を指す。所持済みなら1段ぶん戻した値を見せる。
        switch (id)
        {
            case "n_life_1":
            case "n_life_2":
            {
                int cur = g?.StartLives ?? 4;
                return (owned ? $"はーと {cur - 1}" : $"はーと {cur}", $"はーと {(owned ? cur : cur + 1)}");
            }
            case "n_dodge":     return ("回避は使えない", "Alt / 右クリック / L3 で 0.45秒 無敵になって弾を抜ける");
            case "n_dodge_cd":
                return owned ? ("戻り 0.80秒・距離 64", "戻り 0.65秒・距離 76")
                             : ("戻り 0.80秒・距離 64", "戻り 0.65秒・距離 76");
            case "n_bomb_1":
            {
                int cur = g?.StartBombs ?? 4;
                return (owned ? $"ボム {cur - 1}" : $"ボム {cur}", $"ボム {(owned ? cur : cur + 1)}");
            }
            case "n_power_2x":  return ("弾の火力 ×1", "弾の火力 ×2");
            // 効果の文面に「→」を混ぜない（表示側が値と値を矢印でつなぐので、二重の矢印になって読めなくなる）。
            case "n_charge":    return ("溜め打ちは使えない", "0.6秒ためて放つ 威力×12 の貫通弾");
            case "n_hitbox":    return ("当たり判定 2.0px", "当たり判定 1.0px");
            case "n_lines":
            {
                string now = "連射2線・拡散5way・追尾2発";
                string aft = "連射3線・拡散7way・追尾3発";
                return (now, aft);
            }
            case "n_move_15x":  return ("移動 75", "移動 112");
            case "n_slow":      return ("集中モードは使えない", "V / ホイール / LB で敵の時間だけ ×0.35 を1.5秒（CD20秒）");
            case "n_rate_2x":   return ("発射間隔 ×1.0", "発射間隔 ×0.5");
            case "n_pierce":    return ("弾は1体で消える", "どの撃ち方でも 敵1体を貫く");
            case "n_option":    return ("オプション なし", "オプション 1基（威力×0.5で同時射撃）");
            default:            return ("—", Def(i).Desc);
        }
    }

    private void DrawFooter()
    {
        DrawRect(new Rect2(ColX, FooterY - 14f, W - ColX * 2f, 1f), new Color(1f, 1f, 1f, 0.10f));

        _backBtnRect = new Rect2(ColX, FooterY, 172f, 36f);
        bool hov = UiKit.Hotspot(_backBtnRect, HsBack);
        UiKit.Box(this, _backBtnRect, new Color(1f, 1f, 1f, hov ? 0.10f : 0.04f), 10f, new Color(UiKit.Text3, hov ? 0.8f : 0.3f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(_backBtnRect.Position.X, FooterY + 9f),
                   $"{Pad.CancelToken}  {(string.IsNullOrEmpty(_game.PendingResumeScene) ? "ホーム" : "もどる")}", UiKit.FontLabel, hov ? UiKit.White : UiKit.Text3,
                   HorizontalAlignment.Center, _backBtnRect.Size.X);

        UiKit.Text(this, UiKit.Zen, new Vector2(ColX + 200f, FooterY + 10f),
                   // 「上から順に買えます」は削らないと説明過多：点灯・縦線・呼吸の演出と、
                   //   買えないときの理由（DrawDetail の買うボタン文言）で既に言えている。
                   $"{Pad.MoveToken} えらぶ　　{Pad.ConfirmToken} 買う",
                   UiKit.FontLabel, UiKit.Text4);
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        if (_toastIsDialogue) { DrawDialogueBubble(); return; }
        float w = UiKit.TextW(UiKit.ZenBold, _toast, 16) + 48;
        float x = (W - w) / 2f;
        UiKit.Box(this, new Rect2(x, H - 96, w, 38f), new Color(0.06f, 0.05f, 0.10f, 0.96f), 12f, new Color(_toastCol, 0.7f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, H - 88), _toast, 16, _toastCol, HorizontalAlignment.Center, w);
    }

    private void DrawDialogueBubble()
    {
        const float bw = DetailW - 24f;
        const float padX = 18f, padY = 14f;
        var lines = UiKit.WrapLines(UiKit.ZenBold, _toast, 16, bw - padX * 2f);
        float lh = UiKit.ZenBold.GetHeight(16);
        float bh = padY * 2f + lh * lines.Count;
        float bx = DetailX + 12f;
        float by = 516f;

        float k = Mathf.Clamp((float)(_toastAge / 0.18), 0f, 1f);
        float e = 1f - Mathf.Pow(1f - k, 3f);
        float scale = 0.9f + 0.14f * e - 0.04f * Mathf.Pow(e, 6f);
        float a = Mathf.Min(k, Mathf.Clamp((float)_toastT / 0.3f, 0f, 1f));
        float dy = 10f * (1f - e);
        if (a <= 0.004f) return;

        float dw = bw * scale, dh = bh * scale;
        float cx = bx + bw * 0.5f;
        float x = cx - dw * 0.5f, y = by + dy;

        Color edge = new(_toastCol, 0.6f * a);
        Color face = new Color(0.05f, 0.04f, 0.09f).Lerp(new Color(0.09f, 0.07f, 0.15f), 0.5f) with { A = a };
        UiKit.Box(this, new Rect2(x, y, dw, dh), face, 16f, edge, 1.4f);

        for (int i = 0; i < lines.Count; i++)
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + padX * scale, y + padY * scale + lh * i * scale),
                lines[i], 16, new Color(_toastCol, a), HorizontalAlignment.Left, dw - padX * 2f * scale);
    }
}
