using Godot;

// PauseMenu : 全画面共通のポーズメニュー（オートロード /root/PauseMenu）。
//   Esc で開き、ツリーをポーズして**1枚のダイアログ**を出す。
//   2026-09-17 ユーザー指示で作り直した（旧版は Top/Stage/Config の三段ページ構成）。
//     トップ（ステージ中）: 離脱／リスタート／ログ／セーブ／ロード／タイトルへ
//       ・離脱／リスタートは破壊的なので確認ダイアログ（はい/いいえ）を挟む。
//       ・セーブ／ロードはスロット選択ダイアログを挟む（見た目・語彙はタイトルの「つづきから」に揃える）。
//     「つづける」行は廃止＝下部中央の「閉じる」ボタンへ。
//     「このステージ」の階層は廃止＝中身をトップへ展開した。
//     「ボタン表記」は廃止。
//     設定（音量・画面モード）は右上の歯車ボタンから開く別ページへ移した。
//   ★2026-09-22：「あそびかた」を戻した（ユーザー要望「ボタン配置・強化アイテムの説明をメニューから
//     確認できるように」）。2026-09-17 の作り直しで項目ごと落ちて HowToPlay へ入る導線がゼロになっていた。
//     ログと同じくポーズを保ったままオーバーレイを重ね、閉じればこのメニューへ戻る（シーン遷移なし）。
//     ステージ内・ステージ外のどちらでも出す＝ハブからでもボタン配置と強化アイテムを確認できる。
//   ステージ外（ハブ/ショップ/記録/難易度選択/トレーニング）では 離脱／リスタート／ログ が意味を持たない
//     ので出さない（あそびかた／セーブ／ロード／タイトルへ の4行＋閉じる＋歯車だけ）。RetryEnabled 参照。
//   セーブは手動・スロット制（自動セーブは別枠）＝ここでしか手動保存されない。
//   タイトル/設定/あそびかた/カットシーンは対象外（Esc が既に「閉じる/戻る」の画面＝そちらを優先）。
//   開ける画面では右下に「Esc メニュー」ヒントを常時表示する。
//   --qa / --demo では無効（自動プレイのポーズ事故を防ぐ）。
public partial class PauseMenu : CanvasLayer
{
    private GameManager _game = null!;
    private PauseCanvas _canvas = null!;
    private bool _open;
    private int _sel;
    private bool _navHeld, _lrHeld, _zHeld, _escHeld, _backHeld;
    private double _savedToast;
    private int _savedSlot;
    private bool _autoplay;
    // デバッグ限定：--pause-shot <top|confirm|save|load|settings> で、その状態を開いたところから始める
    //   （スクショ用。Hub の --hub-detail / --hub-job と同じ趣旨で、開くだけで何も確定しない）。
    private string? _shotState;

    // ───────── ページ ─────────
    //   Top      … 項目リスト＋閉じる＋歯車。
    //   Settings … 歯車の遷移先（音量3・画面モード）。X／右クリック／Esc でトップへ戻る。
    public enum Page { Top, Settings }
    private Page _page = Page.Top;
    public Page CurrentPage => _page;

    // ───────── トップの項目 ─────────
    //   Act は「何をする行か」。行の並びは列挙順そのままで、ステージ外では Leave/Restart/Log が落ちる。
    public enum Act { Leave, Restart, Log, HowTo, Save, Load, Title }
    private static readonly (Act act, string label)[] AllRows =
    {
        (Act.Leave,   "離脱"),
        (Act.Restart, "リスタート"),
        (Act.Log,     "ログ"),
        (Act.HowTo,   "あそびかた"),
        (Act.Save,    "セーブ"),
        (Act.Load,    "ロード"),
        (Act.Title,   "タイトルへ"),
    };
    // ステージ外で出す行（＝ラン中にしか意味が無い3つを外したもの）。
    private static readonly (Act act, string label)[] OutsideRows =
        System.Array.FindAll(AllRows, e => e.act is Act.HowTo or Act.Save or Act.Load or Act.Title);

    public (Act act, string label)[] Rows => RetryEnabled ? AllRows : OutsideRows;

    // 項目リストの下に続く「閉じる」「歯車」も、矢印キー/パッドで選べる仮想行として扱う
    //   ＝カーソルは 0..RowCount-1 が項目、RowCount が閉じる、RowCount+1 が歯車。
    //   ホットスポット id もこの番号をそのまま使う（マウスとカーソルの番号体系を一本化）。
    public int RowCount => Rows.Length;
    public int CloseIndex => RowCount;
    public int GearIndex => RowCount + 1;
    private int TopSelCount => RowCount + 2;

    // 設定ページの行。音量3行（←→で調整）＋画面モード1行（←→/Zで切替）。
    //   ★ここに出すのは「実際に効く項目」だけ。Settings.cs の「解像度」は表示だけで実反映のコードが
    //     無い（SType.Select＝Apply に case が無い）ため、ポーズの設定からは出さない。
    public static readonly (string Key, string Label)[] VolRows =
    {
        ("master", "マスター音量"),
        ("bgm",    "BGM"),
        ("se",     "効果音 (SE)"),
    };
    // 画面モード＝Settings.cs の "mode" セグメントと同じキー・同じ値（0=ウィンドウ / 1=フルスクリーン）。
    public static readonly string[] ScreenModes = { "ウィンドウ", "フルスクリーン" };
    private int _screenMode;
    public int ScreenMode => _screenMode;
    private int SettingsRowCount => VolRows.Length + 1;
    private bool IsVolRow(int sel) => _page == Page.Settings && sel < VolRows.Length;
    private bool IsScreenRow(int sel) => _page == Page.Settings && sel == VolRows.Length;
    public static int ScreenRowIndex => VolRows.Length;

    // ───────── 確認ダイアログ（はい/いいえ）─────────
    //   旧版の「同じ行を2回押す」方式（_hubConfirm）を廃止した新規の共通実装。
    //   ChoiceOverlay は沈黙で自動決定する（＝放っておくと勝手に選ばれる）仕様なので確認には使えない。
    //   キーボード（←→/↑↓ + Z）・パッド（十字/スティック + A）・マウス（ホバー＋クリック）で操作でき、
    //   既定は「いいえ」。X／パッドB／Esc／右クリックで閉じる＝キャンセル。
    private bool _confirmOpen;
    private string _confirmText = "";
    private Act _confirmAct;
    private bool _confirmYes;   // false = いいえ（既定）
    public bool ConfirmOpen => _confirmOpen;
    public string ConfirmText => _confirmText;
    public bool ConfirmYes => _confirmYes;

    private void OpenConfirm(Act act, string text)
    {
        Audio.Instance?.PlayUiMove();   // まだ何も起きていない＝移動音
        _confirmOpen = true; _confirmAct = act; _confirmText = text; _confirmYes = false;
    }
    private void CloseConfirm() { _confirmOpen = false; }

    // ───────── スロット選択ダイアログ（セーブ／ロード）─────────
    //   見た目と語彙はタイトルの「つづきから」（TitleMenu.DrawSlotPicker）に合わせる
    //   ＝行ラベル「スロット N」／右に「セーブあり」「空き」。
    //   セーブは空スロットも選べる（新規保存）。ロードは空スロットを選べない（拒否音）。
    //   オートセーブ枠(スロット0)は出さない：ユーザー指示が「3スロット」なので手動枠の1..3だけ扱う。
    private bool _slotOpen;
    private bool _slotForSave;  // true=セーブ / false=ロード
    private int _slotSel;       // 0..SlotCount-1 が スロット1..3、SlotCount が「閉じる」
    public bool SlotOpen => _slotOpen;
    public bool SlotForSave => _slotForSave;
    public int SlotSel => _slotSel;
    public int SlotCloseIndex => GameManager.SlotCount;

    private void OpenSlots(bool forSave)
    {
        Audio.Instance?.PlayUiMove();
        _slotOpen = true; _slotForSave = forSave;
        // カーソルの初期位置：セーブは先頭、ロードは最初の「セーブあり」に置く（空へ置いても押せない）。
        _slotSel = 0;
        if (!forSave)
            for (int i = 0; i < GameManager.SlotCount; i++)
                if (SlotFilled(i + 1)) { _slotSel = i; break; }
    }
    private void CloseSlots() { _slotOpen = false; }

    // 表示用にキャッシュした音量（0..100）。Open 時に保存値から読む。
    private readonly float[] _vol = new float[VolRows.Length];

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // ポーズ中も動く
        Layer = 100;                          // 最前面
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--demo" || args[i] == "--qa") _autoplay = true;
            else if (args[i] == "--pause-shot" && i + 1 < args.Length) _shotState = args[i + 1];
        }
        _canvas = new PauseCanvas { Menu = this };
        AddChild(_canvas);
    }

    // デバッグ限定：--pause-shot の状態へ飛ぶ（最初の _Process で1回だけ）。撮影用なので何も実行しない。
    private void ApplyShotState()
    {
        string s = _shotState!;
        _shotState = null;
        if (!CanOpenHere()) return;
        Open();
        // ツリーポーズは掛けたままにしない：Shot オートロードは Inherit で止まってしまい撮影できない。
        GetTree().Paused = false;
        switch (s)
        {
            case "confirm":  _sel = 0; OpenConfirm(Act.Leave, "本当に離脱しますか？"); break;
            case "save":     OpenSlots(forSave: true); break;
            case "load":     OpenSlots(forSave: false); break;
            case "settings": _page = Page.Settings; _sel = 0; break;
            // あそびかた：ポーズの上にオーバーレイが重なった状態（--howto N と併用すればページも指せる）。
            case "howto":    GetNodeOrNull<HowToPlay>("/root/HowTo")?.Open(); break;
        }
    }

    // Hub/ショップ/難易度選択/記録/トレーニング＝ランの外側にある非戦闘画面。
    // メニューは開くが 離脱／リスタート／ログ は意味を持たない。
    // （ShopTutorial は "Shop" の部分一致で自動的に含まれる）。RetryEnabled と揃えること。
    private static bool IsNonCombatMenuScreen(string path) =>
        path.Contains("Hub") || path.Contains("Shop") || path.Contains("DiffSelect")
        || path.Contains("Records") || path.Contains("Training") || path.Contains("Customize");

    // Esc でメニューを開ける画面か。除外するのは「Esc が既に閉じる/戻るを意味する画面」だけ:
    //   TitleMenu … ここがルート（戻り先が無い＝メニューの「タイトルへ」も無意味）
    //   Settings  … Esc＝保存してタイトルへ戻る。音量も画面モードもこの画面自体が持つ＝重ねる意味が無い
    //   カットシーン(Prologue/Final/Epilogue/Credits) … Start/R 長押しのやりなおし導線が既にあり、
    //     BGM とフェーズタイマーが進行中。ツリーポーズを挟むと演出の整合を取り直す必要があるので触らない。
    // 上に重なるオーバーレイ（あそびかた/会話ログ）は _Process 側の overlayOpen で別途止めている。
    private bool CanOpenHere()
    {
        string path = GetTree().CurrentScene?.SceneFilePath ?? "";
        if (string.IsNullOrEmpty(path)) return false;
        return !(path.Contains("TitleMenu") || path.Contains("Settings") || path.Contains("Credits")
              || path.Contains("Prologue") || path.Contains("Final") || path.Contains("Epilogue"));
    }

    // スロットセーブを出す画面か。トレーニングだけ false＝試用で付け外しした強化がディスクへ漏れない
    // （TrainingRoot は AutoSaveEnabled=false で自動セーブを止めているが、SaveToSlot はそれを迂回する）。
    public bool SaveEnabled => !(GetTree().CurrentScene?.SceneFilePath ?? "").Contains("Training");

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
        // 直近デバイスの追跡（ボタンプロンプト表記の動的 KB/パッド切替）。ゲーム中は Player も呼ぶが、
        // メニュー/ハブ/ショップ/カットシーン等の非戦闘画面は常駐のここが担う。
        Pad.PollDevice();
        // マウス座標・ボタンエッジの更新＋ホイール蓄積のフレーム確定（全画面で毎フレーム）。
        Pad.PollMouse(GetViewport());
        // ポーズ中・非戦闘画面のホイールは、弾幕パートの集中モード（Pad.ConsumeWheelTurn）へ持ち越さない。
        //   ポーズ中は Player._PhysicsProcess が止まるのでラッチを誰も消費できず、再開した瞬間に
        //   メニューで回したぶんが集中モードとして暴発する。ここで毎フレーム捨てておく
        //   （戦闘中は Player が先に消費するので、この呼び出しは何も奪わない）。
        if (GetTree().Paused) Pad.ConsumeWheelTurn();

        if (_shotState != null) { ApplyShotState(); _canvas.QueueRedraw(); return; }
        if (_autoplay) return;
        if (_savedToast > 0) _savedToast -= delta;
        // 会話ログのオーバーレイが上に開いている間／閉じた直後フレーム(UiBlocked)は、
        // ポーズメニュー側の入力を止める（Esc/Z の二重処理でメニューまで連鎖して閉じるのを防ぐ）。
        // held は「既押し」扱いにして、同じ押下がエッジとして立たないよう食っておく。
        bool overlayOpen = GetNodeOrNull<HowToPlay>("/root/HowTo") is { IsOpen: true }
                        || GetNodeOrNull<Backlog>("/root/Backlog") is { IsOpen: true };
        if (overlayOpen || Pad.UiBlocked(this))
        {
            // _backHeld も含める（2026-09-22）：あそびかた／ログを X（パッド B）で閉じた同じ押下が、
            //   次のフレームで cancel エッジとして立ち、ポーズメニューまで閉じてしまっていた。
            _escHeld = _zHeld = _navHeld = _lrHeld = _backHeld = true;
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
            // 右下ヒントの左クリック。非戦闘画面かつ会話中でないときだけ受ける（HintClickable のコメント参照）。
            if ((escEdge && CanOpenHere()) || HintClicked()) Open();
            _canvas.QueueRedraw(); // 常時ヒントの更新
            return;
        }

        // 共通の入力エッジ（どのページ／どのダイアログでも同じ割り当てで読む）。
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        bool back = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        bool cancel = backEdge || escEdge || Pad.MouseRightClick();

        // 上に重ねたダイアログが優先（確認 → スロット選択 → ページ本体）。
        if (_confirmOpen) { ProcessConfirm(zEdge, cancel); _canvas.QueueRedraw(); return; }
        if (_slotOpen) { ProcessSlots(zEdge, cancel); _canvas.QueueRedraw(); return; }
        if (_page == Page.Settings) ProcessSettings(zEdge, cancel);
        else ProcessTop(zEdge, cancel);

        _canvas.QueueRedraw();
    }

    // ───────── トップ ─────────
    private void ProcessTop(bool zEdge, bool cancel)
    {
        // マウス：項目・閉じる・歯車をまとめて登録（id＝カーソル番号）。
        //   ポーズが開いている間だけホットスポットを登録する（＝下の画面はツリーポーズで停止中＝
        //   唯一の登録者。閉じている時は BeginHotspots を呼ばない＝下の画面のクリック判定に混線しない）。
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < RowCount; i++) UiKit.Hotspot(RowRect(i, RowCount), i);
        UiKit.Hotspot(CloseRect(RowCount), CloseIndex);
        UiKit.Hotspot(GearRect(RowCount), GearIndex);
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= 0 && hov != _sel) { _sel = hov; Audio.Instance?.PlayUiMove(); }
        int clk = UiKit.ClickedId(Pad.MouseClick());

        // ↑↓：項目 → 閉じる → 歯車 の順に巡る（歯車も矢印キー/パッドで選べる）。
        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            int n = TopSelCount;
            _sel = (_sel + (up ? n - 1 : 1)) % n;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;
        // ←→：閉じる ⇄ 歯車（下段の2ボタンは横並びなので、横キーでも行き来できると素直）。
        bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
        if ((left || right) && !_lrHeld && _sel >= CloseIndex)
        {
            _sel = _sel == CloseIndex ? GearIndex : CloseIndex;
            Audio.Instance?.PlayUiMove();
        }
        _lrHeld = left || right;

        if (clk >= 0) { _sel = clk; Activate(clk); return; }
        if (zEdge) { Activate(_sel); return; }
        if (cancel) { Audio.Instance?.PlayUiCancel(); Close(); }
    }

    // 行（または閉じる／歯車）を決定する。破壊的なものは確認ダイアログを、
    // セーブ/ロードはスロット選択ダイアログを挟む。
    private void Activate(int sel)
    {
        _sel = sel;
        if (sel == CloseIndex) { Audio.Instance?.PlayUiConfirm(); Close(); return; }
        if (sel == GearIndex) { Audio.Instance?.PlayUiMove(); _page = Page.Settings; _sel = 0; return; }
        if (sel < 0 || sel >= RowCount) return;

        switch (Rows[sel].act)
        {
            case Act.Leave:
                // 離脱＝このステージを抜けてハブへ。稼いだぶんを捨てるので確認を挟む。
                OpenConfirm(Act.Leave, "本当に離脱しますか？");
                return;
            case Act.Restart:
                // リスタート＝ステージの最初から。進捗が消えるので確認を挟む。
                OpenConfirm(Act.Restart, "本当に最初からでよろしいですか？");
                return;
            case Act.Log:
                // ログ：ポーズを保ったままバックログ・オーバーレイを重ねる（閉じたらポーズへ戻る）。
                Audio.Instance?.PlayUiConfirm();
                GetNodeOrNull<Backlog>("/root/Backlog")?.Open();
                return;
            case Act.HowTo:
                // あそびかた：ログと同じ作法でオーバーレイ（HowToPlay・Layer 110）を重ねる。ツリーポーズは
                //   掛けたまま＝HowToPlay は ProcessMode=Always で動き、開いている間はこちらの _Process が
                //   overlayOpen で早期 return して入力を譲る。閉じると NoteOverlayClosed 経由でここへ戻る。
                //   決定音は HowToPlay.Open が鳴らす（ここで重ねて鳴らさない）。
                GetNodeOrNull<HowToPlay>("/root/HowTo")?.Open();
                return;
            case Act.Save:
                // トレーニング中は試用の強化がディスクへ漏れるので保存させない（グレーアウト行）。
                if (!SaveEnabled) { Audio.Instance?.PlayUiDeny(); return; }
                OpenSlots(forSave: true);
                return;
            case Act.Load:
                OpenSlots(forSave: false);
                return;
            default:
                Audio.Instance?.PlayUiConfirm();
                _game?.AutoSave(); Close(); GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); // タイトルへ（離脱時オートセーブ）
                return;
        }
    }

    // ───────── 確認ダイアログ ─────────
    private void ProcessConfirm(bool zEdge, bool cancel)
    {
        UiKit.BeginHotspots(Pad.MousePos());
        UiKit.Hotspot(ConfirmBtnRect(false), 0); // いいえ
        UiKit.Hotspot(ConfirmBtnRect(true), 1);  // はい
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= 0 && (hov == 1) != _confirmYes) { _confirmYes = hov == 1; Audio.Instance?.PlayUiMove(); }
        int clk = UiKit.ClickedId(Pad.MouseClick());

        // 左右（横並びの2ボタン）でも上下でも行き来できる＝どのキーでも迷わない。
        bool nav = Input.IsActionPressed("ui_left") || Input.IsActionPressed("ui_right")
                || Input.IsActionPressed("ui_up") || Input.IsActionPressed("ui_down");
        if (nav && !_navHeld) { _confirmYes = !_confirmYes; Audio.Instance?.PlayUiMove(); }
        _navHeld = nav;
        _lrHeld = nav;

        if (clk >= 0) { _confirmYes = clk == 1; RunConfirm(); return; }
        if (zEdge) { RunConfirm(); return; }
        if (cancel) { Audio.Instance?.PlayUiCancel(); CloseConfirm(); }
    }

    // 確認の決定。いいえ＝閉じるだけ。はい＝そのアクションを実行する。
    private void RunConfirm()
    {
        if (!_confirmYes) { Audio.Instance?.PlayUiCancel(); CloseConfirm(); return; }
        var act = _confirmAct;
        CloseConfirm();
        Audio.Instance?.PlayUiConfirm();
        if (act == Act.Restart)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Close();
            GetTree().ReloadCurrentScene();
        }
        else // Act.Leave ＝ハブへもどる。GameManager の抜け処理と同型（AutoSave→DespawnAll→Hub.tscn）。
        {
            _game?.AutoSave();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Close();
            GetTree().ChangeSceneToFile("res://Hub.tscn");
        }
    }

    // ───────── スロット選択ダイアログ ─────────
    private void ProcessSlots(bool zEdge, bool cancel)
    {
        int n = GameManager.SlotCount;
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < n; i++) UiKit.Hotspot(SlotRowRect(i), i);
        UiKit.Hotspot(SlotCloseRect(), SlotCloseIndex);
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= 0 && hov < n && hov != _slotSel) { _slotSel = hov; Audio.Instance?.PlayUiMove(); }
        int clk = UiKit.ClickedId(Pad.MouseClick());

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            _slotSel = (_slotSel + (up ? n : 1)) % (n + 1); // n（閉じる）も巡回に含める
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;

        if (clk == SlotCloseIndex) { Audio.Instance?.PlayUiCancel(); CloseSlots(); return; }
        if (clk >= 0) _slotSel = clk;
        if (zEdge || clk >= 0)
        {
            if (_slotSel == SlotCloseIndex) { Audio.Instance?.PlayUiCancel(); CloseSlots(); return; }
            RunSlot(_slotSel + 1); // 表示のスロット1..3 ＝ GameManager のスロット番号と同じ
            return;
        }
        if (cancel) { Audio.Instance?.PlayUiCancel(); CloseSlots(); }
    }

    private void RunSlot(int slot)
    {
        if (_slotForSave)
        {
            Audio.Instance?.PlayUiConfirm();
            _game?.SaveToSlot(slot);             // スロットへ保存（上書き）
            _savedSlot = slot; _savedToast = 1.8;
            CloseSlots();
            return;
        }
        // ロード：空スロットは選べない（タイトルの「つづきから」と同じ拒否音）。
        if (!SlotFilled(slot)) { Audio.Instance?.PlayUiDeny(); return; }
        Audio.Instance?.PlayUiConfirm();
        CloseSlots();
        // ロード後の遷移はタイトルの「つづきから」と同じ作法＝LoadFromSlot してハブへ入る
        //   （どのステージの途中から開いても、読み込んだセーブの続きはハブから始まる）。
        if (_game?.LoadFromSlot(slot) == true)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Close();
            GetTree().ChangeSceneToFile("res://Hub.tscn");
        }
    }

    // ───────── 設定ページ（歯車の遷移先）─────────
    private void ProcessSettings(bool zEdge, bool cancel)
    {
        int n = SettingsRowCount;
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < n; i++) UiKit.Hotspot(SettingsRowRect(i), i);
        UiKit.Hotspot(SettingsBackRect(), n);
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= 0 && hov != _sel) { _sel = hov; Audio.Instance?.PlayUiMove(); }
        int clk = UiKit.ClickedId(Pad.MouseClick());

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            _sel = (_sel + (up ? n : 1)) % (n + 1); // n＝「もどる」ボタン
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;

        // ←→：音量は ±5／画面モードは循環。どちらも即反映＋保存。
        bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
        if ((left || right) && !_lrHeld)
        {
            if (IsVolRow(_sel)) SetVol(_sel, _vol[_sel] + (right ? 5f : -5f));
            else if (IsScreenRow(_sel)) CycleScreenMode(right ? 1 : -1);
        }
        _lrHeld = left || right;

        if (clk == n) { Audio.Instance?.PlayUiCancel(); BackToTop(); return; }
        if (clk >= 0)
        {
            _sel = clk;
            if (IsVolRow(clk))
            {
                // バーのクリック位置(X)で音量を直接設定（5刻みスナップ＝KB操作と粒度を揃える）。
                var (barX, barW) = VolBarMetrics();
                SetVol(clk, Mathf.RoundToInt(Mathf.Clamp((Pad.MousePos().X - barX) / barW, 0f, 1f) * 20f) * 5f);
            }
            else if (IsScreenRow(clk)) CycleScreenMode(1);
            return;
        }
        if (zEdge)
        {
            if (_sel == n) { Audio.Instance?.PlayUiCancel(); BackToTop(); return; }
            // 音量行で Z＝ミュート/復帰のトグル（0 ⇄ 既定相当）。
            if (IsVolRow(_sel)) { SetVol(_sel, _vol[_sel] > 0.5f ? 0f : 80f); Audio.Instance?.PlayUiConfirm(); }
            else if (IsScreenRow(_sel)) CycleScreenMode(1);
            return;
        }
        if (cancel) { Audio.Instance?.PlayUiCancel(); BackToTop(); }
    }

    // 設定 → トップ。カーソルは歯車（そこから来た場所）へ戻す。
    private void BackToTop() { _page = Page.Top; _sel = GearIndex; }

    private void SetVol(int i, float v)
    {
        _vol[i] = Mathf.Clamp(v, 0f, 100f);
        AudioConfig.Set(VolRows[i].Key, _vol[i]);
        Audio.Instance?.PlayUiMove();   // SE は鳴らして耳で確認する
    }

    // 画面モードを循環＝DisplayServer へ即反映し、settings.json の "mode" へも書く
    // （設定画面 Settings.cs が同じキーを読む＝どちらから変えても一つの真実になる）。
    private void CycleScreenMode(int dir)
    {
        _screenMode = (_screenMode + dir + ScreenModes.Length) % ScreenModes.Length;
        DisplayServer.WindowSetMode(_screenMode == 1
            ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        AudioConfig.SetInt("mode", _screenMode);
        Audio.Instance?.PlayUiMove();
    }

    // ── マウス用ジオメトリ（PauseCanvas の描画と同一式。ホットスポット計算に共用）──
    //   ダイアログ box: w=460。高さは「見出し＋行＋下段ボタン」。画面中央に置く。
    public const float BoxW = 460f;
    private const float HeadH = 78f;    // 箱の上端から1行目までの余白（見出し＋区切り線）
    private const float RowH = 44f;     // 項目1行の送り
    private const float FootH = 76f;    // 最終行の下から箱の下端まで（閉じるボタン帯）

    public static (float x, float y, float w, float h) TopBox(int rows)
    {
        float h = HeadH + rows * RowH + FootH;
        return ((UiKit.DesignW - BoxW) / 2f, (UiKit.DesignH - h) / 2f, BoxW, h);
    }

    // 設定ページの箱（音量3＋画面モード1＝4行。下段は「もどる」ボタン）。
    public static (float x, float y, float w, float h) SettingsBox()
    {
        float h = HeadH + (VolRows.Length + 1) * 46f + FootH;
        return ((UiKit.DesignW - BoxW) / 2f, (UiKit.DesignH - h) / 2f, BoxW, h);
    }

    public static Rect2 RowRect(int i, int rows)
    {
        var (x, y, w, _) = TopBox(rows);
        return new Rect2(x + 22, y + HeadH + i * RowH, w - 44, 36);
    }

    // 下部中央の「閉じる」ボタン（旧「つづける」行の役）。
    public static Rect2 CloseRect(int rows)
    {
        var (x, y, w, h) = TopBox(rows);
        const float bw = 150f;
        return new Rect2(x + (w - bw) / 2f, y + h - 56f, bw, 38f);
    }

    // 右上の歯車ボタン（設定ページへ）。見出しと同じ帯の右端に置く。
    public static Rect2 GearRect(int rows)
    {
        var (x, y, w, _) = TopBox(rows);
        return new Rect2(x + w - 52f, y + 12f, 38f, 38f);
    }

    public static Rect2 SettingsRowRect(int i)
    {
        var (x, y, w, _) = SettingsBox();
        return new Rect2(x + 22, y + HeadH + i * 46f, w - 44, 38);
    }

    public static Rect2 SettingsBackRect()
    {
        var (x, y, w, h) = SettingsBox();
        const float bw = 150f;
        return new Rect2(x + (w - bw) / 2f, y + h - 56f, bw, 38f);
    }

    // 音量行のバー（トラック）矩形の X 起点と幅（描画と同一算出）。設定ページ専用。
    private static (float barX, float barW) VolBarMetrics()
    {
        var (x, _, w, _) = SettingsBox();
        float barW = 132f, barX = x + w - barW - 64f;
        return (barX, barW);
    }

    // ── 確認ダイアログのジオメトリ（中央・横並び2ボタン）──
    public const float ConfirmW = 420f, ConfirmH = 170f;
    public static (float x, float y, float w, float h) ConfirmBox()
        => ((UiKit.DesignW - ConfirmW) / 2f, (UiKit.DesignH - ConfirmH) / 2f, ConfirmW, ConfirmH);

    public static Rect2 ConfirmBtnRect(bool yes)
    {
        var (x, y, w, h) = ConfirmBox();
        const float bw = 150f, gap = 20f;
        float left = x + (w - bw * 2f - gap) / 2f;
        return new Rect2(yes ? left + bw + gap : left, y + h - 62f, bw, 42f);
    }

    // ── スロット選択のジオメトリ（タイトルの「つづきから」と同じ寸法体系）──
    public const float SlotW = 460f;
    public static float SlotBoxH => 82f + GameManager.SlotCount * 56f;
    public static (float x, float y, float w, float h) SlotBox()
        => ((UiKit.DesignW - SlotW) / 2f, (UiKit.DesignH - SlotBoxH) / 2f, SlotW, SlotBoxH);

    public static Rect2 SlotRowRect(int i)
    {
        var (x, y, w, _) = SlotBox();
        return new Rect2(x + 22, y + 64f + i * 56f, w - 44, 46);
    }

    public static Rect2 SlotCloseRect()
    {
        var (x, y, w, _) = SlotBox();
        return new Rect2(x + w - 56f, y + 12f, 40f, 40f);
    }

    private void Open()
    {
        Audio.Instance?.PlayUiCancel(); // ポーズ＝開く合図（柔らかい下降）
        _open = true; _sel = 0;
        _page = Page.Top;   // 開くたびトップから（前回どこを見ていたかは引きずらない）
        _confirmOpen = false; _slotOpen = false;
        _navHeld = false; _lrHeld = false; _zHeld = false; _backHeld = true;   // back は開幕の押下を食う
        for (int i = 0; i < VolRows.Length; i++) _vol[i] = AudioConfig.Get(VolRows[i].Key); // 保存値を読む
        // 画面モードは保存値ではなく実ウィンドウ状態から読む（Settings.SyncModeFromWindow と同じ理由＝
        // 表示と実状態を食い違わせない）。
        var wm = DisplayServer.WindowGetMode();
        _screenMode = wm is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen ? 1 : 0;
        GetTree().Paused = true;
        Pad.ConsumeUi(this); // 開いたフレームから下の画面への入力を食う
        _canvas.QueueRedraw();
    }

    private void Close()
    {
        _open = false;
        _confirmOpen = false; _slotOpen = false;
        GetTree().Paused = false;
        _canvas.QueueRedraw();
    }

    // 「リスタート」「離脱」「ログ」が意味を持つ画面か＝ステージ系のみ。
    // Hub/ショップ/難易度選択/記録ではシーン再読込にも離脱にも意味がないため、行ごと出さない
    // （CanOpenHere() が既にこれらの画面を除外しているわけではない＝ステージ外でもメニューは開く）。
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
    public bool ShowHint => !_open && !_autoplay && CanOpenHere();

    // ═══════ 右下「Esc／メニュー」ヒントのクリック対応（2026-09-17）═══════
    //   ★戦闘画面では対応しない。理由は3つ、どれも実装上の事実:
    //     1) 戦闘中の自機はマウスカーソルへ追従する（Player.cs:597）。弾を避けて画面右下へ寄った瞬間に
    //        ポーズが開く＝避けている最中に一番やってはいけない誤爆になる。
    //     2) 戦闘中の左クリックはロックオン送り（HowToPlay のマウスタブ参照）。右下だけ意味が変わる
    //        ボタンを置くと、狙いを送ったつもりでメニューが開く。
    //     3) ホットスポットは UiKit のグローバル単一レジストリ。戦闘中に毎フレーム BeginHotspots を
    //        呼ぶと、同フレームで登録している ChoiceOverlay（ゲームオーバー3択）と互いの登録を潰し合う。
    //   → 非戦闘画面（Hub/ショップ/難易度選択/記録/トレーニング）でだけ押せるようにする。この5画面は
    //     どれも自前で BeginHotspots を呼ぶ「唯一の登録者」なので、ここは共有レジストリに載せず
    //     Rect2.HasPoint 直書きで判定する（OpeningFilm / TrainingRoot と同じ既存の流儀）。
    //     クリックの二重処理は Open() の Pad.ConsumeUi → 各画面の Pad.UiBlocked 早期 return が防ぐ。
    //   会話中（Hud.BubblePaused）は左クリックが会話送り（Pad.AdvanceHeld）なので無効にする。
    public bool HintClickable
    {
        get
        {
            if (!ShowHint) return false;
            // 上にオーバーレイ（あそびかた/会話ログ）が乗っている間は押せない＝暗幕の下で光らせない
            //（_Process 側は overlayOpen で早期 return するのでクリックは元から通らないが、
            //  描画だけは続くので HintHovered をここで止める）。
            if (GetNodeOrNull<HowToPlay>("/root/HowTo") is { IsOpen: true }
                || GetNodeOrNull<Backlog>("/root/Backlog") is { IsOpen: true }) return false;
            string path = GetTree().CurrentScene?.SceneFilePath ?? "";
            return IsNonCombatMenuScreen(path) && !Hud.BubblePaused;
        }
    }

    // ヒントの当たり矩形（PauseCanvas.DrawHint と同一式＝キーキャップ＋ラベル帯だけ。周囲へは広げない）。
    public static Rect2 HintRect()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float y = H - 38f - 30f;
        string keyTok = Pad.PauseToken;
        float keyW = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, keyTok, 11) + 12f);
        float labelW = UiKit.TextW(UiKit.ZenBold, "メニュー", UiKit.FontSmall);
        float x = W - 24f - (keyW + 7f + labelW);
        return new Rect2(x - 4f, y - 3f, keyW + 7f + labelW + 8f, 30f);
    }

    // ヒントにマウスが乗っているか（描画のハイライト用）。押せない画面では常に false＝光らせない。
    public bool HintHovered => HintClickable && HintRect().HasPoint(Pad.MousePos());

    // このフレームにヒントが左クリックされたか。_Process と QA が同じ1本の判定を通る。
    public bool HintClicked() => HintHovered && Pad.MouseClick();
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

    private static readonly Color RowIdle = new(185 / 255f, 174 / 255f, 203 / 255f);

    public override void _Draw()
    {
        if (Menu == null) return;
        if (Menu.IsOpen) { UiKit.BeginDesign(this); DrawPauseMenu(); UiKit.EndDesign(this); }
        else if (Menu.ShowHint) { UiKit.BeginDesign(this); DrawHint(); UiKit.EndDesign(this); }
    }

    // 選択行のハイライト（枠＋▸）。全ページで同じ見え方にするためここに集約する。
    private void DrawRowCursor(Rect2 r)
    {
        UiKit.Box(this, r, new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 10f, new Color(UiKit.Purify, 0.45f), 1f);
        UiKit.Text(this, UiKit.Mono, new Vector2(r.Position.X + 14, r.Position.Y + 9), "▸", UiKit.FontBody, UiKit.Purify);
    }

    // 行のラベル（選択中は白＋太字）。
    private void DrawRowLabel(Rect2 r, string label, bool on, Color? col = null)
        => UiKit.Text(this, on ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(r.Position.X + 36, r.Position.Y + 7),
            label, UiKit.FontBody, col ?? (on ? UiKit.White : RowIdle));

    // 下段の枠つきボタン（閉じる／もどる／はい／いいえ）。選択中は塗りと縁を強める。
    private void DrawButton(Rect2 r, string label, bool on, Color accent)
    {
        UiKit.Box(this, r, new Color(accent, on ? 0.24f : 0.10f), 9f, new Color(accent, on ? 0.85f : 0.4f), on ? 1.4f : 1f);
        UiKit.Text(this, on ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(r.Position.X, r.Position.Y + (r.Size.Y - 18f) / 2f),
            label, UiKit.FontBody, on ? UiKit.White : UiKit.Text2, HorizontalAlignment.Center, r.Size.X);
    }

    // 歯車アイコン（既存ヘルパが無いので手描き）。外周の8歯＋内円のリング。
    //   歯は「内半径→外半径」の短い線分を 8 本、45度おきに引く＝ポリゴンを組まずに歯車に見える。
    private void DrawGear(Rect2 r, bool on)
    {
        Color col = on ? UiKit.White : UiKit.Text3;
        if (on) UiKit.Box(this, r, new Color(UiKit.Purify, 0.18f), 9f, new Color(UiKit.Purify, 0.7f), 1f);
        Vector2 c = r.GetCenter();
        float rIn = 6.0f, rMid = 8.0f, rOut = 11.0f;
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.Tau;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(c + d * rMid, c + d * rOut, col, 2.4f, true);
        }
        DrawArc(c, rMid, 0f, Mathf.Tau, 28, col, 2.0f, true);
        DrawArc(c, rIn * 0.5f, 0f, Mathf.Tau, 20, col, 1.6f, true);
    }

    private void DrawPauseMenu()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.62f)); // 暗幕

        if (Menu.CurrentPage == PauseMenu.Page.Settings) DrawSettingsPage();
        else DrawTopPage();

        // 上に重なるダイアログ（確認 → スロット）。暗幕を1枚追加して手前に描く。
        if (Menu.ConfirmOpen) DrawConfirm();
        else if (Menu.SlotOpen) DrawSlotPicker();
    }

    // 箱の見出し帯（タイトル文字＋区切り線）。全ページ共通。
    private void DrawHead(float x, float y, float w, string head)
    {
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + 28, y + 22), head, UiKit.Info);
        DrawRect(new Rect2(x + 28, y + 48, w - 56, 1f), new Color(1, 1, 1, 0.1f));
    }

    // トップ：項目リスト ＋ 下部中央の「閉じる」 ＋ 右上の歯車。
    private void DrawTopPage()
    {
        var rows = Menu.Rows;
        var (x, y, w, h) = PauseMenu.TopBox(rows.Length);
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 18f, new Color(UiKit.Purify, 0.6f), 1.4f);
        DrawHead(x, y, w, "MENU");
        DrawGear(PauseMenu.GearRect(rows.Length), Menu.Sel == Menu.GearIndex);

        for (int i = 0; i < rows.Length; i++)
        {
            var r = PauseMenu.RowRect(i, rows.Length);
            bool on = i == Menu.Sel;
            if (on) DrawRowCursor(r);
            // トレーニング中はセーブだけ選べない＝グレーアウト＋理由を右に添える（旧版と同じ作法）。
            bool dim = rows[i].act == PauseMenu.Act.Save && !Menu.SaveEnabled;
            DrawRowLabel(r, rows[i].label, on, dim ? UiKit.Text4 : null);
            if (dim)
                UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 180, r.Position.Y + 11), "トレーニング中は不可", UiKit.FontSmall,
                    UiKit.Text4, HorizontalAlignment.Right, 158);
        }

        if (Menu.SavedText.Length > 0)
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + h - 80), Menu.SavedText, UiKit.FontLabel, UiKit.PurifyHi, HorizontalAlignment.Center, w);
        DrawButton(PauseMenu.CloseRect(rows.Length), "閉じる", Menu.Sel == Menu.CloseIndex, UiKit.Purify);
    }

    // 設定：音量3行（←→）＋画面モード1行（←→/Z）＋「もどる」。
    //   解像度は Settings.cs 側でも表示だけ（実反映のコードが無い）なのでここには出さない。
    private void DrawSettingsPage()
    {
        var (x, y, w, h) = PauseMenu.SettingsBox();
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 18f, new Color(UiKit.Purify, 0.6f), 1.4f);
        DrawHead(x, y, w, "MENU ▸ 設定");

        int nVol = PauseMenu.VolRows.Length;
        for (int i = 0; i < nVol; i++)
        {
            var r = PauseMenu.SettingsRowRect(i);
            bool on = i == Menu.Sel;
            if (on) DrawRowCursor(r);
            DrawRowLabel(r, PauseMenu.VolRows[i].Label, on);
            // バー（トラック＋塗り）＋数値
            float v = Menu.VolValue(i);
            float barW = 132f, barX = x + w - barW - 64f, barY = r.Position.Y + 16f, barH = 6f;
            DrawRect(new Rect2(barX, barY, barW, barH), new Color(1, 1, 1, 0.12f));
            DrawRect(new Rect2(barX, barY, barW * v / 100f, barH), new Color(UiKit.Info, on ? 0.95f : 0.7f));
            UiKit.Text(this, UiKit.Mono, new Vector2(barX + barW + 8f, r.Position.Y + 10), Mathf.RoundToInt(v).ToString(), UiKit.FontLabel,
                on ? UiKit.White : UiKit.Text3, HorizontalAlignment.Right, 40);
        }

        var sr = PauseMenu.SettingsRowRect(PauseMenu.ScreenRowIndex);
        bool son = Menu.Sel == PauseMenu.ScreenRowIndex;
        if (son) DrawRowCursor(sr);
        DrawRowLabel(sr, "画面サイズ", son);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + w - 64 - 200, sr.Position.Y + 8), "◂ " + PauseMenu.ScreenModes[Menu.ScreenMode] + " ▸",
            UiKit.FontLabel, son ? UiKit.White : UiKit.Text3, HorizontalAlignment.Right, 200);

        DrawButton(PauseMenu.SettingsBackRect(), "もどる", Menu.Sel == PauseMenu.ScreenRowIndex + 1, UiKit.Purify);
    }

    // 確認ダイアログ（はい/いいえ）。既定は「いいえ」＝誤爆しても何も起きない。
    private void DrawConfirm()
    {
        DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color(0, 0, 0, 0.45f));
        var (x, y, w, h) = PauseMenu.ConfirmBox();
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.07f, 0.055f, 0.115f, 0.99f), 16f, new Color(UiKit.Burn, 0.55f), 1.4f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + 40f), Menu.ConfirmText, UiKit.FontBody, UiKit.White,
            HorizontalAlignment.Center, w);
        DrawButton(PauseMenu.ConfirmBtnRect(false), "いいえ", !Menu.ConfirmYes, UiKit.Purify);
        DrawButton(PauseMenu.ConfirmBtnRect(true), "はい", Menu.ConfirmYes, UiKit.Burn);
    }

    // スロット選択（セーブ／ロード）。見た目と語彙はタイトルの「つづきから」に合わせる。
    private void DrawSlotPicker()
    {
        DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color(0, 0, 0, 0.45f));
        var (x, y, w, h) = PauseMenu.SlotBox();
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.99f), 16f, new Color(UiKit.Purify, 0.6f), 1.4f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 28, y + 20), Menu.SlotForSave ? "セーブ" : "ロード", UiKit.FontHeading, UiKit.White);

        // 右上の×（閉じる）。カーソルでも選べる（SlotCloseIndex）。
        Rect2 close = PauseMenu.SlotCloseRect();
        bool closeOn = Menu.SlotSel == Menu.SlotCloseIndex;
        Color closeCol = closeOn ? UiKit.White : UiKit.Text3;
        if (closeOn) UiKit.Box(this, close, new Color(UiKit.Purify, 0.18f), 9f, new Color(UiKit.Purify, 0.7f), 1f);
        Vector2 c = close.GetCenter();
        DrawLine(c + new Vector2(-6, -6), c + new Vector2(6, 6), closeCol, 2, true);
        DrawLine(c + new Vector2(6, -6), c + new Vector2(-6, 6), closeCol, 2, true);

        for (int i = 0; i < GameManager.SlotCount; i++)
        {
            Rect2 r = PauseMenu.SlotRowRect(i);
            bool on = i == Menu.SlotSel;
            bool exists = Menu.SlotFilled(i + 1);
            // ロード時の空スロットは選べない＝タイトル同様「空き」を薄く出して押しても拒否音だけ。
            bool dim = !Menu.SlotForSave && !exists;
            if (on) DrawRowCursor(r);
            UiKit.Text(this, on && !dim ? UiKit.ZenBlack : UiKit.ZenBold, new Vector2(r.Position.X + 36, r.Position.Y + 12),
                $"スロット {i + 1}", UiKit.FontSpeaker, dim ? UiKit.Text4 : on ? UiKit.White : RowIdle);
            UiKit.Text(this, UiKit.Zen, new Vector2(r.Position.X + r.Size.X - 140, r.Position.Y + 15),
                exists ? "セーブあり" : "空き", UiKit.FontBody, exists ? UiKit.Info : UiKit.Text4,
                HorizontalAlignment.Right, 132);
        }
    }

    // 「Esc メニュー」ヒント（画面右下・ティッカーの上）。常時表示。
    //   2026-09-07: プレイ中の常駐操作ガイド（Hud.DrawControls）を撤去した際、これ1つだけを残した。
    //   Esc（メニュー）の存在を知らせる唯一の手がかりなので消さない。ただし弾の視認を妨げないよう
    //   薄く小さく（キー枠の縁とラベルのαを落とし、ラベルは FontSmall へ）。
    //   ★2026-09-17：非戦闘画面（Hub/ショップ/難易度選択/記録/トレーニング）ではここを左クリックでも
    //     開けるようにした（PauseMenu.HintClickable）。押せる画面でホバーしたときだけ下敷きを敷いて
    //     明るくし、「押せる」ことと「いま触れている」ことを見せる。戦闘中は押せないので光りもしない
    //     ＝見た目が変わらない＝弾の視認を邪魔しない。
    private const float HintAlpha = 0.85f;
    private void DrawHint()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float y = H - 38f - 30f;
        const string label = "メニュー";
        string keyTok = Pad.PauseToken; // 表示モードに追従（Esc / MENU / OPTIONS）
        float keyW = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, keyTok, 11) + 12f);
        float labelW = UiKit.TextW(UiKit.ZenBold, label, UiKit.FontSmall);
        float x = W - 24f - (keyW + 7f + labelW);
        bool hov = Menu.HintHovered;
        if (hov) UiKit.Box(this, PauseMenu.HintRect(), new Color(UiKit.Purify, 0.14f), 8f, new Color(UiKit.Info, 0.5f), 1f);
        UiKit.Key(this, new Vector2(x, y), keyTok, new Color(1, 1, 1, hov ? 0.10f : 0.04f),
            new Color(UiKit.Info, hov ? 0.6f : 0.22f), new Color(hov ? UiKit.PurifyHi : UiKit.Info, hov ? 1f : HintAlpha));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + keyW + 7f, y + 5f), label, UiKit.FontSmall,
            new Color(hov ? UiKit.White : UiKit.Text2, hov ? 1f : HintAlpha));
    }
}
