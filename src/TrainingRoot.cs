using Godot;
using System.Collections.Generic;

// TrainingRoot : ショップの「トレーニング」から入る試し打ち場（Training.tscn にアタッチ）。
//   Stage0Root を雛形に、World/FxLayer/GameCamera/Player とダミー敵(TrainingDummy)を生成する無菌の実験場。
//
//   目的（ユーザー要望）：スキルツリーを好きに割り振り、ダミー敵に撃ち込んで「撃ち味がどのぐらい変わるか」を
//   与ダメ数字＋DPS（直近3秒平均）で比べる。割り振りは完全無料・試用のみ。
//
//   本番状態を壊さない核心（自分でレビュー済み）：
//     ・入場(_Ready)で SnapshotMeta＝通貨/フォロワー/装備モード/所持強化を退避。
//     ・入場で AutoSaveEnabled=false＝Hub帰還等の自動セーブでディスクへ漏れない。TutorialNoConsume=true＝死なない。
//     ・退場(ExitToShop / _ExitTree)で RestoreMeta＝退避を完全復元し、AutoSaveEnabled/TutorialNoConsume を戻す。
//     ・トレーニング中はセーブを一切呼ばない（PauseMenu もこのシーンでは開かない＝スロットセーブ導線を出さない）。
//
//   住み分け（試し打ちと割り振りを両立）：
//     ・自機＝矢印/WASD 移動・ショットは常時オート・X ボム・V モード切替（本編と同じ手触り）。
//     ・スキル割り振り＝右のスキルパネルをマウスで（行クリックで付け外し／ホイールでスクロール）。
//       キーボードは PageUp/PageDown でスクロール、[ / ] で全オフ/全解放（自機キーと衝突しない住み分け）。
//     ・もどる＝Esc または左下ボタンのクリック。
public partial class TrainingRoot : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;
    private const float W = UiKit.DesignW, H = UiKit.DesignH; // 設計座標(1280×720)

    private GameManager _game = null!;
    private Player _player = null!;
    private Node2D _world = null!;
    private TrainingDummy _dummy = null!;
    private Control _uiLayer = null!;

    // 退避したメタ状態と、復元済みフラグ（二重復元を避ける＝ExitToShop と _ExitTree の両方で呼んでも安全）。
    private GameManager.MetaSnapshot _snapshot = null!;
    private bool _restored;
    private bool _prevAutoSave;

    // ───── DPS 集計（直近 DpsWindow 秒の与ダメ合計 ÷ 窓）─────
    private const double DpsWindow = 3.0;
    private readonly Queue<(double t, int dmg)> _dmgLog = new();
    private double _elapsed;
    private long _totalDamage;   // このセッションの累計与ダメ（参考表示）。

    // ───── 小話4（トレーニング場の独り言）─────
    //   docs/小話集_v1.md §4。会話系が皆無だったトレーニング場に、Shop.cs の Toast() と同じ軽量トースト形式で
    //   器を新設。入場時・撃っている最中・放置時の3トリガーで出し分ける（who: 0=少年/1=ミナ、Hud.LineKind と同じ色分け）。
    private const double TalkShowSec = 2.6;  // Hub.Toast() と同じ表示秒数に合わせる。
    private const double IdleTalkSec = 7.0;  // これだけ無入力が続いたら「放置」と見なす。
    private string _talk = "";
    private int _talkWho;
    private double _talkT;
    private double _idleTimer;   // 無入力の継続秒数（移動/ショット/ボム/モード切替のいずれかで0にリセット）。
    private bool _idleFired;     // このアイドル継続中に一度でも独り言を出したか（連呼防止。入力が戻るまで再発火しない）。

    private static readonly (int who, string text)[] TrainEnter =
    {
        (1, "試し打ちですね。……ええ、思う存分どうぞ。ここでは誰も痛みません。"),
        (1, "的が一つきり。ずいぶん寂しい射撃場ですこと。"),
        (0, "遠慮するな。壊しても弁償はぼくの財布じゃない。"),
        (1, "整備の時間です。……こういう時間、嫌いではありません。"),
    };

    private static readonly (int who, string text)[] TrainShoot =
    {
        (1, "……いい音です。今のは、手応えがありました。"),
        (0, "おお、伸びたな。ぼくの設計が優秀だからだ。"),
        (1, "設計者が言うと、ただの自慢ですよ。"),
        (1, "数字が増えるのは、少し気持ちがいいですね。単純です、わたくし。"),
        (0, "撃ちすぎだ。的が気の毒になってきた。"),
        (1, "この的、文句ひとつ言いませんね。……えらいです。"),
        (1, "肩慣らしのつもりが、つい本気に。……いけませんね。"),
        (0, "そこ! もっと近づけ! ……いや近すぎる!"),
        (1, "ご主人様は黙っていてくださいまし。集中できません。"),
    };

    private static readonly (int who, string text)[] TrainIdle =
    {
        (1, "……ご主人様? 手が、止まっておりますよ。"),
        (1, "お休みですか。どうぞ、ゆっくり。逃げませんので、わたくしも、的も。"),
        (0, "…………zzz"),
        (1, "寝ましたね。……起きるまで、ここにおります。"),
        (1, "静かですね。……たまには、こういう日もいいものです。"),
        (1, "この時間、記録には残らないんですよ。もったいない話です。"),
        (1, "暇なので、的の数を数えていました。……一つでした。"),
    };

    // ───── スキルパネル（トグル開閉方式）─────
    //   普段は隠して画面全体を試し打ちに使い、Tab か右上のタブをクリックしたときだけスライド表示する。
    //   閉じているとき自機・ダミー・DPS計器が三者とも重ならず見える（ユーザー方針）。
    private bool _panelOpen;      // パネルが開いているか。
    private float _slide;         // スライド進行 0(閉)→1(開)。Lerp で滑らかに。
    private float _scroll;        // スクロール量（設計座標・px）。
    private Rect2 _tabRect, _backRect, _allOnRect, _allOffRect;
    // 描画時に確定する行の当たり矩形（設計座標）と対応 upgrade id。クリック突合に使う。
    private readonly List<(Rect2 rect, string id)> _rowHits = new();
    private bool _escHeld, _pgHeld, _bracketHeld, _tabHeld;

    // パネル位置（設計座標）。開いたときの定位置。閉じるときは右へスライドアウトする。
    private const float PanelW = 430f, PanelY = 60f, PanelH = 630f;
    private float PanelX => W - PanelW * _slide; // _slide=1 で W-PanelW（画面内）、0 で W（画面外・右）
    private float ListTop => PanelY + 100f;
    private float ListBot => PanelY + PanelH - 16f;
    private const float RowH = 30f;

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;

        // ── 本番状態の退避＆試用セットアップ（ここが「壊さない」の要）──
        if (_game != null)
        {
            _snapshot = _game.SnapshotMeta();
            _prevAutoSave = _game.AutoSaveEnabled;
            _game.AutoSaveEnabled = false;   // 自動セーブでディスクへ漏れない
            _game.TutorialNoConsume = true;  // 試し打ちで死なない（残機/ボム非消費）
            _game.ResetRun();                // Bombs 満タン等（Impression/_upgrades/Followers には触れない）
            _game.TrainingSetAllUpgrades(true);        // 既定は全解放＝撃ち味の全開から始めて外して比べる
            _game.TrainingSetImpression(999_999_999);  // 表示上は実質無限（購入では減らさない運用）
            _game.TrainingMode = true;                 // 加速球モードを解放＝V の切替ローテに入れる（トレーニング中のみ）
            _game.SelectedShotMode = GameManager.ShotMode.Rapid;
            _game.SetContamination(0f);
            if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        }

        // 背景（落ち着いた一様塗り）。
        AddChild(new CanvasModulate { Name = "Tint", Color = new Color(1.0f, 0.99f, 0.96f) });
        var fill = new ColorRect { Name = "Fill", Color = new Color(0.08f, 0.09f, 0.14f), Size = new Vector2(ScreenWidth, ScreenHeight), ZIndex = -100 };
        fill.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(fill);

        // World（弾/敵/自機の親）＋FxLayer（与ダメ数字/浄化演出）＋GameCamera。
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _world.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });

        _player = new Player { Name = "Player" };
        _world.AddChild(_player);
        _player.GlobalPosition = new Vector2(96, 120);
        _player.SetCorruption(0f);

        // ダミー敵（数値が見える実験台）。被弾ごとに DPS 計へ通知。
        //   x=215（設計 ~717px）＝スキルパネル（開くと設計 x850〜）と干渉しない中央〜やや右に配置。
        _dummy = new TrainingDummy { Name = "TrainingDummy", OnDamaged = OnDummyDamaged };
        _world.AddChild(_dummy);
        _dummy.GlobalPosition = new Vector2(215, 120);

        // スキルパネル＋DPS表示のオーバーレイ（設計座標で描く専用 Control）。
        _uiLayer = new Control { Name = "UiLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
        _uiLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _uiLayer.ZIndex = 50;
        _uiLayer.Draw += DrawUi;
        AddChild(_uiLayer);

        // 入場時の独り言（小話4）。他のトリガーより先に一度だけ。
        var enter = TrainEnter[GD.RandRange(0, TrainEnter.Length - 1)];
        ShowTalk(enter.who, enter.text);
    }

    private void ShowTalk(int who, string text)
    {
        _talkWho = who;
        _talk = text;
        _talkT = TalkShowSec;
    }

    private void OnDummyDamaged(int dmg)
    {
        _dmgLog.Enqueue((_elapsed, dmg));
        _totalDamage += dmg;

        // 撃っている最中の独り言（小話4）。頻発を避けるため低確率＋非表示中のみ抽選。
        if (_talkT <= 0 && GD.Randf() < 0.10f)
        {
            var line = TrainShoot[GD.RandRange(0, TrainShoot.Length - 1)];
            ShowTalk(line.who, line.text);
        }
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;

        // 直近 DpsWindow 秒より古い与ダメ記録を捨てる。
        while (_dmgLog.Count > 0 && _dmgLog.Peek().t < _elapsed - DpsWindow)
            _dmgLog.Dequeue();

        if (_talkT > 0) _talkT -= delta;

        // 放置検知（小話4）：移動/ショット/ボム/モード切替のいずれも無ければ無入力とみなす。
        //   入力が戻ったらタイマーをリセットし _idleFired も倒す＝次に無入力が続いたときだけ再発火（連呼防止）。
        bool moving = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down") != Vector2.Zero
            || Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.D);
        bool acting = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept")
            || Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.V);
        if (moving || acting)
        {
            _idleTimer = 0;
            _idleFired = false;
        }
        else
        {
            _idleTimer += delta;
            if (_idleTimer >= IdleTalkSec && !_idleFired)
            {
                _idleFired = true;
                var line = TrainIdle[GD.RandRange(0, TrainIdle.Length - 1)];
                ShowTalk(line.who, line.text);
            }
        }

        // パネルのスライド（トグル）。開＝1へ、閉＝0へ滑らかに寄せる。
        _slide = Mathf.Lerp(_slide, _panelOpen ? 1f : 0f, Mathf.Clamp(14f * (float)delta, 0f, 1f));

        _uiLayer?.QueueRedraw();

        // ポーズメニュー等を閉じた押下の漏れをこのフレームだけ食う。
        if (Pad.UiBlocked(this)) { _escHeld = _pgHeld = _bracketHeld = _tabHeld = true; return; }

        // Tab：スキルパネルの開閉トグル（自機の移動/ショット/ボムのキーとは衝突しない）。
        bool tab = Input.IsKeyPressed(Key.Tab);
        if (tab && !_tabHeld) { _panelOpen = !_panelOpen; Audio.Instance?.PlayUiMove(); }
        _tabHeld = tab;

        // ── スキルパネルのマウス操作（自機操作とは住み分け。タブ/もどるは常時クリック可）──
        HandlePanelMouse();

        // ホイールでスクロール（パネルが開いていて、その上にマウスがあるときだけ）。
        float wheel = Pad.WheelDelta();
        if (wheel != 0f && _panelOpen && Pad.MousePos().X >= PanelX)
            _scroll = Mathf.Clamp(_scroll - wheel * RowH * 1.5f, 0f, MaxScroll());

        // キーボード：PageUp/PageDown でスクロール（自機の矢印/Zと衝突しない）。
        bool pg = Input.IsKeyPressed(Key.Pageup) || Input.IsKeyPressed(Key.Pagedown);
        if (pg && !_pgHeld && _panelOpen)
        {
            float dir = Input.IsKeyPressed(Key.Pageup) ? -1f : 1f;
            _scroll = Mathf.Clamp(_scroll + dir * RowH * 5f, 0f, MaxScroll());
        }
        _pgHeld = pg;

        // [ = 全オフ / ] = 全解放（撃ち味の素↔全開を一発で比べる）。全解放/全オフは固定系も含むので自機再構築。
        bool br = Input.IsKeyPressed(Key.Bracketleft) || Input.IsKeyPressed(Key.Bracketright);
        if (br && !_bracketHeld)
        {
            _game?.TrainingSetAllUpgrades(Input.IsKeyPressed(Key.Bracketright));
            RebuildPlayer();
            Audio.Instance?.PlayUiConfirm();
        }
        _bracketHeld = br;

        // もどる＝Esc（このシーンでは PauseMenu は開かない＝Esc は退出専用）。
        bool esc = Input.IsKeyPressed(Key.Escape);
        if (esc && !_escHeld) ExitToShop();
        _escHeld = esc;
    }

    // スキルパネルのクリック：タブ＝開閉／もどる＝退出／行＝その強化を付け外し／全解放・全オフ。
    //   自機を作り直すボタンは廃止：固定系スキル（回避域/オプション/最大♥）をトグルした瞬間に
    //   自動で静かに自機再構築する（ユーザーがボタンを意識しなくていい＝#1 対応A）。
    private void HandlePanelMouse()
    {
        if (!Pad.MouseClick()) return;
        Vector2 m = Pad.MousePos();

        // タブ（常時表示・右上）はパネルの開閉。もどるボタンも常時クリック可。
        if (_tabRect.HasPoint(m)) { _panelOpen = !_panelOpen; Audio.Instance?.PlayUiMove(); return; }
        if (_backRect.HasPoint(m)) { ExitToShop(); return; }

        if (!_panelOpen) return; // 閉じているとき、以下のパネル内クリックは受けない。

        if (_allOnRect.HasPoint(m)) { _game?.TrainingSetAllUpgrades(true); RebuildPlayer(); Audio.Instance?.PlayUiConfirm(); return; }
        if (_allOffRect.HasPoint(m)) { _game?.TrainingSetAllUpgrades(false); RebuildPlayer(); Audio.Instance?.PlayUiConfirm(); return; }

        // 行トグル。
        foreach (var (rect, id) in _rowHits)
        {
            if (!rect.HasPoint(m)) continue;
            bool now = (_game?.GetUpgradeLevel(id) ?? 0) >= 1;
            _game?.TrainingSetUpgrade(id, !now);
            // 固定系（回避域/オプション/最大♥）は _Ready 確定＝トグルした瞬間に自動で自機再構築（射撃系は即反映）。
            if (NeedsRebuild(id)) RebuildPlayer();
            Audio.Instance?.PlayUiMove();
            return;
        }
    }

    // 当たり半径(hitbox)/オプション基数(option)/最大♥(max_life) は Player._Ready で確定＝付け外しの即反映に再構築が要る。
    private static bool NeedsRebuild(string id) =>
        id.StartsWith("hitbox") || id.StartsWith("option") || id.StartsWith("max_life");

    // 自機を作り直す（_Ready 固定値のスキルを即反映）。位置・モードは引き継ぐ。
    private void RebuildPlayer()
    {
        if (_player == null || !IsInstanceValid(_player)) return;
        Vector2 pos = _player.GlobalPosition;
        _player.QueueFree();
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnPlayerBullets();
        _player = new Player { Name = "Player" };
        _world.AddChild(_player);
        _player.GlobalPosition = pos;
        _player.SetCorruption(0f);
    }

    private float DummyDps()
    {
        int sum = 0;
        foreach (var (_, d) in _dmgLog) sum += d;
        // 立ち上がり（窓が満ちる前）は経過秒で割って過大表示を避ける。
        double win = Mathf.Min(_elapsed, DpsWindow);
        return win > 0.05 ? (float)(sum / win) : 0f;
    }

    // 復元＆退出。Shop へ戻る。RestoreMeta は _restored ガードで冪等。
    private void ExitToShop()
    {
        RestoreMeta();
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        Audio.Instance?.PlayUiCancel();
        GetTree().ChangeSceneToFile("res://Shop.tscn");
    }

    private void RestoreMeta()
    {
        if (_restored || _game == null) return;
        _restored = true;
        _game.RestoreMeta(_snapshot);        // 通貨/フォロワー/装備モード/所持強化を完全復元（装備モードも退避値へ戻る）
        _game.AutoSaveEnabled = _prevAutoSave; // 自動セーブを元へ
        _game.TutorialNoConsume = false;       // 練習モードを解除
        _game.TrainingMode = false;            // 加速球モードの解放を解除（通常プレイに持ち越さない）
    }

    public override void _ExitTree()
    {
        // どの経路で抜けても本番状態を確実に戻す（ExitToShop を通らない異常系の保険）。
        RestoreMeta();
    }

    // ───────────────────────── UI 描画（設計座標）─────────────────────────
    private float MaxScroll()
    {
        float content = TotalRows() * RowH;
        float view = ListBot - ListTop;
        return Mathf.Max(0f, content - view);
    }

    // 見出し行＋各強化行の総高さ算出用の行数（系統見出し5行＋強化61行 ≒ 66）。
    private int TotalRows()
    {
        int n = 0;
        Stream prev = (Stream)(-1);
        foreach (var d in GameManager.Upgrades)
        {
            Stream s = StreamOf(d.Id);
            if (s != prev) { n++; prev = s; } // 見出し行
            n++;
        }
        return n;
    }

    private void DrawUi()
    {
        UiKit.BeginDesign(_uiLayer);
        _rowHits.Clear();
        Vector2 mouse = Pad.MousePos();

        DrawHeaderAndDps(mouse);
        DrawTalk();
        DrawSkillPanel(mouse);
        DrawTabAndBack(mouse); // タブ/もどる はパネルの上（前面）に常時描く

        UiKit.EndDesign(_uiLayer);
    }

    private void DrawHeaderAndDps(Vector2 mouse)
    {
        // ヘッダ（左上）。
        UiKit.Text(_uiLayer, UiKit.Mono, new Vector2(40, 22), "TRAINING — SHOOTING RANGE", 11, UiKit.Info);
        UiKit.Text(_uiLayer, UiKit.ZenBlack, new Vector2(40, 36), "トレーニング（試し打ち）", 26, UiKit.White);
        UiKit.Text(_uiLayer, UiKit.Zen, new Vector2(40, 72), "スキルは無料で付け外し・試用のみ。もどると本番の状態はそのまま（保存されません）。", 12, UiKit.Text2);

        // 操作ヒント（左下）。
        UiKit.Text(_uiLayer, UiKit.Zen, new Vector2(40, H - 96),
            "移動: 矢印/WASD　ショット: オート　ボム: X　モード切替: V", 12, UiKit.Text3);
        UiKit.Text(_uiLayer, UiKit.Zen, new Vector2(40, H - 76),
            "スキル割り振り: Tab で開閉（開いたら行をクリックで付け外し・ホイールでスクロール）　全解放/全オフ: ] / [", 12, UiKit.Text3);

        // ── DPS/与ダメ計（画面上部中央。自機・ダミー（画面中ほど）と重ならない帯に置く＝三者とも見える）──
        var box = new Rect2(430, 88, 344, 122);
        UiKit.Box(_uiLayer, box, new Color(0.06f, 0.07f, 0.12f, 0.88f), 12f, new Color(UiKit.Info, 0.45f), 1f);
        UiKit.Text(_uiLayer, UiKit.Mono, new Vector2(box.Position.X + 18, box.Position.Y + 12), "DPS (直近3秒平均)", 11, UiKit.Text3);
        float dps = DummyDps();
        UiKit.Text(_uiLayer, UiKit.Mono, new Vector2(box.Position.X + 18, box.Position.Y + 26), dps.ToString("N0"), 40, UiKit.PurifyHi);
        UiKit.Text(_uiLayer, UiKit.Zen, new Vector2(box.Position.X + 18, box.Position.Y + 82),
            $"累計与ダメ: {_totalDamage:N0}", 13, UiKit.Text2);
        string modeName = _game?.ShotModeName(_game.SelectedShotMode) ?? "連射";
        UiKit.Text(_uiLayer, UiKit.Zen, new Vector2(box.Position.X + 18, box.Position.Y + 100),
            $"装備モード: {modeName}（V で切替）", 13, UiKit.Text2);
    }

    // 独り言トースト（小話4）。DPS計器（x430-774,y88-210）の真下・スキルパネル（開時 x850〜）より
    //   十分左の空き帯に置く＝三者とも重ならない。Shop.cs の Toast() と同じ箱＋名前ラベルの軽量トースト。
    private void DrawTalk()
    {
        if (_talkT <= 0 || string.IsNullOrEmpty(_talk)) return;
        var box = new Rect2(430, 224, 344, 96);
        bool mina = _talkWho != 0;
        Color accent = mina ? UiKit.Mina : UiKit.Info; // Hud.LineKind と同じ色分け（少年=Info／ミナ=Mina）。
        UiKit.Box(_uiLayer, box, new Color(0.06f, 0.07f, 0.12f, 0.88f), 12f, new Color(accent, 0.5f), 1f);
        UiKit.Text(_uiLayer, UiKit.ZenBold, new Vector2(box.Position.X + 16, box.Position.Y + 10), mina ? "ミナ" : "少年", 12, accent);
        UiKit.Multi(_uiLayer, UiKit.Zen, new Vector2(box.Position.X + 16, box.Position.Y + 32), _talk, 13, UiKit.Text2, box.Size.X - 32, 3);
    }

    // 常時表示：右上のスキルタブ（開閉トグル）と左下のもどるボタン。パネルの開閉に関係なく押せる。
    private void DrawTabAndBack(Vector2 mouse)
    {
        // スキルタブ（右上・パネル左肩に付く“つまみ”）。開くと ▶、閉じると ◀ でスライド方向を示す。
        // 閉状態（PanelX=W）だとタブ右端がちょうど画面右端に張り付き枠線/ラベルが見切れるため、
        // 右端に6pxの内側マージンを持たせてクランプ（開状態はPanelXが十分内側なのでこのクランプは効かない）。
        float tabX = Mathf.Min(PanelX - 96f, W - 96f - 6f);
        _tabRect = new Rect2(tabX, PanelY, 96, 40);
        bool th = _tabRect.HasPoint(mouse);
        UiKit.Box(_uiLayer, _tabRect, new Color(UiKit.Info, th ? 0.28f : 0.14f), 8f, new Color(UiKit.Info, 0.7f), 1.4f);
        string tabLabel = _panelOpen ? "スキル ▶" : "◀ スキル";
        UiKit.Text(_uiLayer, UiKit.ZenBold, new Vector2(tabX + 12, PanelY + 11), tabLabel, 13, UiKit.White);
        UiKit.Text(_uiLayer, UiKit.Mono, new Vector2(tabX + 12, PanelY + 27), "Tab", 8, new Color(UiKit.Info, 0.7f));

        // もどるボタン（左下）。
        _backRect = new Rect2(40, H - 52, 168, 34);
        bool bh = _backRect.HasPoint(mouse);
        UiKit.Box(_uiLayer, _backRect, new Color(UiKit.Info, bh ? 0.22f : 0.10f), 8f, new Color(UiKit.Info, 0.6f), 1.2f);
        UiKit.Text(_uiLayer, UiKit.ZenBold, new Vector2(_backRect.Position.X + 16, _backRect.Position.Y + 8), "◀ ショップへもどる (Esc)", 12, UiKit.White);
    }

    private void DrawSkillPanel(Vector2 mouse)
    {
        // 閉じ切っている（画面外）ときは中身を描かない（負荷とクリック誤爆を避ける）。
        if (_slide <= 0.001f) return;

        // パネル枠。
        UiKit.Box(_uiLayer, new Rect2(PanelX, PanelY, PanelW, PanelH), new Color(0.06f, 0.06f, 0.11f, 0.96f), 14f, new Color(1, 1, 1, 0.14f), 1f);
        UiKit.Text(_uiLayer, UiKit.ZenBold, new Vector2(PanelX + 20, PanelY + 16), "スキル割り振り（無料・試用）", 16, UiKit.White);
        int owned = 0, total = 0;
        foreach (var d in GameManager.Upgrades) { total++; if ((_game?.GetUpgradeLevel(d.Id) ?? 0) >= 1) owned++; }
        UiKit.Text(_uiLayer, UiKit.Mono, new Vector2(PanelX + 20, PanelY + 40), $"ON {owned} / {total}", 12, UiKit.Info);
        UiKit.Text(_uiLayer, UiKit.Zen, new Vector2(PanelX + 108, PanelY + 41),
            "行クリックで付け外し（当たり判定/ライフ系は自動反映）", 10, UiKit.Text3);

        // 全解放/全オフボタン（自機再構築ボタンは廃止＝固定系トグルは自動反映）。
        _allOnRect = new Rect2(PanelX + 20, PanelY + 62, 190, 28);
        _allOffRect = new Rect2(PanelX + 218, PanelY + 62, 190, 28);
        DrawMiniBtn(_allOnRect, "全解放 ]", mouse, UiKit.Ok);
        DrawMiniBtn(_allOffRect, "全オフ [", mouse, UiKit.Text3);

        // リスト。系統見出し＋強化行を _scroll ぶんずらして描く。行が表示窓 [ListTop,ListBot] に
        //   完全に収まるものだけ描く＝はみ出しゼロ（別クリップノードを持たずに済ませる）。
        float y = ListTop - _scroll;
        Stream prev = (Stream)(-1);
        foreach (var d in GameManager.Upgrades)
        {
            Stream s = StreamOf(d.Id);
            if (s != prev)
            {
                prev = s;
                if (y >= ListTop && y + RowH <= ListBot)
                {
                    UiKit.Text(_uiLayer, UiKit.ZenBold, new Vector2(PanelX + 20, y + 8),
                        StreamName[(int)s], 13, StreamCol[(int)s]);
                    _uiLayer.DrawLine(new Vector2(PanelX + 90, y + 15), new Vector2(PanelX + PanelW - 20, y + 15),
                        new Color(StreamCol[(int)s], 0.3f), 1f);
                }
                y += RowH;
            }

            if (y >= ListTop && y + RowH <= ListBot)
            {
                DrawSkillRow(d, y, mouse);
                _rowHits.Add((new Rect2(PanelX + 12, y, PanelW - 24, RowH - 2f), d.Id)); // 窓内のみクリック有効
            }
            y += RowH;
        }

        // スクロールバー（右端）。
        float max = MaxScroll();
        if (max > 1f)
        {
            float trackH = ListBot - ListTop;
            float thumbH = trackH * Mathf.Clamp(trackH / (trackH + max), 0.12f, 1f);
            float t = _scroll / max;
            float thumbY = ListTop + t * (trackH - thumbH);
            UiKit.Box(_uiLayer, new Rect2(PanelX + PanelW - 8, ListTop, 3, trackH), new Color(1, 1, 1, 0.08f), 2f);
            UiKit.Box(_uiLayer, new Rect2(PanelX + PanelW - 9, thumbY, 5, thumbH), new Color(UiKit.Info, 0.6f), 3f);
        }
    }

    private void DrawSkillRow(GameManager.UpgradeDef d, float y, Vector2 mouse)
    {
        bool on = (_game?.GetUpgradeLevel(d.Id) ?? 0) >= 1;
        var rect = new Rect2(PanelX + 12, y, PanelW - 24, RowH - 2f);
        bool hov = rect.HasPoint(mouse);
        Color sc = StreamCol[(int)StreamOf(d.Id)];
        // 背景（ON＝系統色を薄く／OFF＝ほぼ透明）。ホバーで一段明るく。
        Color bg = on ? new Color(sc, hov ? 0.30f : 0.20f) : new Color(1, 1, 1, hov ? 0.08f : 0.03f);
        UiKit.Box(_uiLayer, rect, bg, 6f, on ? new Color(sc, 0.7f) : new Color(1, 1, 1, 0.10f), 1f);
        // チェックマーク（ON/OFF）。
        var cbox = new Rect2(rect.Position.X + 8, rect.Position.Y + 6, 14, 14);
        UiKit.Box(_uiLayer, cbox, on ? new Color(sc, 0.9f) : new Color(0, 0, 0, 0.3f), 3f, new Color(1, 1, 1, 0.3f), 1f);
        if (on) UiKit.Text(_uiLayer, UiKit.ZenBold, new Vector2(cbox.Position.X + 2, cbox.Position.Y - 1), "✓", 12, UiKit.White);
        // 名前＋効果。
        UiKit.Text(_uiLayer, UiKit.ZenBold, new Vector2(rect.Position.X + 30, y + 3), d.Name, 12, on ? UiKit.White : UiKit.Text2);
        UiKit.Text(_uiLayer, UiKit.Zen, new Vector2(rect.Position.X + 30, y + 16), d.Desc, 9, UiKit.Text3);
    }

    private void DrawMiniBtn(Rect2 r, string label, Vector2 mouse, Color accent)
    {
        bool hov = r.HasPoint(mouse);
        UiKit.Box(_uiLayer, r, new Color(accent, hov ? 0.28f : 0.12f), 6f, new Color(accent, 0.6f), 1f);
        float tw = UiKit.TextW(UiKit.ZenBold, label, 12);
        UiKit.Text(_uiLayer, UiKit.ZenBold, new Vector2(r.Position.X + (r.Size.X - tw) / 2f, r.Position.Y + 6), label, 12, UiKit.White);
    }

    // ───── 系統分類（Shop の StreamOf を最小移植。見出し色分けに使う）─────
    private enum Stream { Rapid = 0, Spread = 1, Homing = 2, Backfire = 3, Survive = 4, Accel = 5 }
    private static readonly Color[] StreamCol =
    {
        new("7ad7f0"), new("f2b866"), new("86dca0"), new("c39cf0"), new("f0a0a8"), new("f0925c"),
    };
    private static readonly string[] StreamName = { "連射", "拡散", "ホーミング", "後方の光", "生存・経済", "加速球" };
    private static Stream StreamOf(string id) =>
        id.StartsWith("spread") || id.StartsWith("fol_gain") || id.StartsWith("combo_hold")
            || id.StartsWith("option") || id.StartsWith("chain") ? Stream.Spread
        : id.StartsWith("homing") || id.StartsWith("counter") || id.StartsWith("veil") ? Stream.Homing
        : id.StartsWith("bf_") ? Stream.Backfire
        : id.StartsWith("accel") ? Stream.Accel
        : id.StartsWith("move_speed") || id.StartsWith("contam") || id.StartsWith("hitbox")
            || id.StartsWith("imp_mult") || id.StartsWith("max_life") || id.StartsWith("bomb") ? Stream.Survive
        : Stream.Rapid;
}
