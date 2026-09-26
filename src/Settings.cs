using Godot;
using System.Collections.Generic;

// Settings : 設定画面。RefrainHTML/Refrain Settings.dc.html を忠実移植（非ピクセル・滑らかUI）。
//   左カテゴリ（6）→右コントロール。←→ で値変更・↑↓ 項目・Q/E カテゴリ・X/Esc でタイトルへ。
//   値は user://settings.json に永続。スライダー/トグル/セグメントは変更のたびApply()経由でエンジンへ即時反映
//   （音量バス各種・会話速度・オート送り・自動セーブ・操作表示モード等）。画面モードのみ_initializingガードで
//   初回表示時（_ReadyのApplyAll）は反映をスキップし、ユーザーが明示的に操作したときだけ実ウィンドウへ反映する。
public partial class Settings : Node2D
{
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    private enum SType { Slider, Toggle, Segment, Select, KeyBind }

    private sealed class Def
    {
        public string Key = "", Label = "", Sub = "";
        public SType Type;
        public string[] Options = System.Array.Empty<string>();
        public string[] Keys = System.Array.Empty<string>();
        public float F; public bool B; public int I; public string S = "";
    }
    private sealed class Cat { public string Key = "", Name = "", Sub = ""; public List<Def> Items = new(); }

    private readonly List<Cat> _cats = new();
    private int _cat, _row;
    private bool _navHeld, _lrHeld, _catHeld, _zHeld, _backHeld;
    private double _t;
    private bool _autoplay;
    // true の間は Apply() 内の "mode" が DisplayServer へ反映しない（_Ready の初回 ApplyAll 用）。
    // ユーザーが実際に操作するまで、開いただけではウィンドウを変えないため。
    private bool _initializing = true;

    private const string SettingsPath = "user://settings.json";

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        BuildDefaults();
        LoadSettings();
        SyncModeFromWindow();
        ApplyAll();
        _initializing = false;
    }

    // 「画面モード」セグメントを実際のウィンドウ状態へ同期する（設定ファイルの保存値より実状態を優先）。
    // これにより、開いた瞬間の UI 表示が実状態と食い違わない。
    private void SyncModeFromWindow()
    {
        var mode = DisplayServer.WindowGetMode();
        bool fullscreen = mode == DisplayServer.WindowMode.Fullscreen || mode == DisplayServer.WindowMode.ExclusiveFullscreen;
        foreach (var c in _cats) foreach (var d in c.Items)
            if (d.Key == "mode") d.I = fullscreen ? 1 : 0;
    }

    private void BuildDefaults()
    {
        Cat C(string key, string name, string sub) { var c = new Cat { Key = key, Name = name, Sub = sub }; _cats.Add(c); return c; }
        Def Slider(string k, string l, float v, string sub = "") => new() { Key = k, Label = l, Type = SType.Slider, F = v, Sub = sub };
        Def Toggle(string k, string l, bool v, string sub = "") => new() { Key = k, Label = l, Type = SType.Toggle, B = v, Sub = sub };
        Def Seg(string k, string l, string[] o, int v, string sub = "") => new() { Key = k, Label = l, Type = SType.Segment, Options = o, I = v, Sub = sub };
        Def Sel(string k, string l, string v) => new() { Key = k, Label = l, Type = SType.Select, S = v };
        Def Keys(string k, string l, string[] keys, string sub = "") => new() { Key = k, Label = l, Type = SType.KeyBind, Keys = keys, Sub = sub };

        var disp = C("display", "表示", "Display");
        disp.Items.Add(Seg("mode", "画面モード", new[] { "ウィンドウ", "フルスクリーン" }, 1));
        disp.Items.Add(Sel("res", "解像度", "1920 × 1080"));
        disp.Items.Add(Seg("fps", "リフレッシュレート", new[] { "60", "120", "144" }, 0));
        disp.Items.Add(Slider("scan", "スキャンライン", 40, "レトロな走査線の濃さ"));
        disp.Items.Add(Slider("bloom", "ブルーム", 65, "光のにじみ"));
        disp.Items.Add(Toggle("pixel", "ピクセルパーフェクト", true, "内部384×216を整数倍で表示"));

        var audio = C("audio", "サウンド", "Audio");
        audio.Items.Add(Slider("master", "マスター音量", 80));
        audio.Items.Add(Slider("bgm", "BGM", 70));
        audio.Items.Add(Slider("se", "効果音 (SE)", 85));
        audio.Items.Add(Slider("voice", "ボイス", 90));
        audio.Items.Add(Slider("amb", "環境音", 55, "心象世界のノイズ"));

        var gp = C("game", "ゲームプレイ", "Gameplay");
        gp.Items.Add(Toggle("bright", "弾を明るく表示", true, "視認性を上げる"));
        gp.Items.Add(Toggle("hitbox", "当たり判定を常時表示", false));
        gp.Items.Add(Slider("shake", "画面振動", 30));
        gp.Items.Add(Slider("flash", "被弾フラッシュ", 60, "赤い明滅の強さ"));
        gp.Items.Add(Seg("msg", "メッセージ速度", new[] { "遅", "中", "速" }, 1));
        gp.Items.Add(Toggle("auto", "オート会話送り", false));
        gp.Items.Add(Toggle("autosave", "オートセーブ", true, "クリア・帰還時に自動保存"));

        var ctrl = C("controls", "操作", "Controls");
        // 操作表示モード：起動直後（まだ何も触っていない状態）にどちらの表記でヒントを出すかの初期値。
        // 触った瞬間から直近デバイスへ自動追従する。パッド表記は Xbox 基準に一本化（2026-09-13）＝
        // PlayStation の選択肢は廃止し、旧セーブの値(1)は「コントローラー」へ合流する。
        ctrl.Items.Add(Seg("inputdisplay", "操作表示", new[] { "キーボード", "コントローラー" }, 0, "起動直後の表記（以降は自動切替）"));
        ctrl.Items.Add(Keys("move", "移動", new[] { "↑", "↓", "←", "→" }));
        // ★2026-09-26 作者決定のキーボード割り当て：Z＝溜め打ち（旧 C）／Shift 長押し＝ロックオン・離すと解除
        //   （旧 F 送り／G 解除。F は「次の敵へ送り」として残る）／Ctrl＝回避（旧 Alt）／M＝メニュー（旧 Esc）。
        //   パッド・マウスは変更なし。行数はパネルの高さ（8行）に収めるため、溜め打ちはショットの行に同居させる。
        //   ★2026-09-27：メニューは Esc でも開く（PauseMenu.EscOpensHere）。スマホ系の画面の Esc は従来どおり「もどる」、
        //     メニューの中の Esc は一段もどる。
        ctrl.Items.Add(Keys("shot", "ショット / 溜め打ち", new[] { "オート", "Z 長押し" }, "光は自動。Z を長押しして離すと溜め打ち"));
        ctrl.Items.Add(Keys("lock", "ロックオン", new[] { "Shift" }, "押しているあいだ狙う。離すと外れる。F で次の敵へ"));
        ctrl.Items.Add(Keys("bomb", "ボム", new[] { "X" }));
        ctrl.Items.Add(Keys("dodge", "回避", new[] { "Ctrl" }, "一瞬無敵で弾を抜ける"));
        ctrl.Items.Add(Keys("focus", "集中モード", new[] { "V" }, "ホイール／サイドボタン／LB でも"));
        ctrl.Items.Add(Keys("pause", "メニュー", new[] { "M", "Esc" }, "メニュー内の Esc は一段もどる（スマホの画面の Esc はもどる）"));

        var a11y = C("a11y", "アクセシビリティ", "Accessibility");
        a11y.Items.Add(Toggle("reduceflash", "明滅を抑える", true, "フラッシュ表現を軽減"));
        a11y.Items.Add(Seg("cvd", "色覚サポート", new[] { "OFF", "1型", "2型", "3型" }, 0));
        a11y.Items.Add(Seg("textsize", "文字サイズ", new[] { "小", "中", "大" }, 1));
        a11y.Items.Add(Toggle("softhit", "被弾演出を控えめに", false));
        a11y.Items.Add(Toggle("contrast", "高コントラストUI", false));

        var x = C("x", "Y連携", "Y Integration");
        x.Items.Add(Toggle("share", "浄化の結果をYに投稿", true));
        x.Items.Add(Toggle("showid", "スコアカードに@IDを表示", true));
        x.Items.Add(Toggle("realposts", "「降ってくる言葉」を実投稿から生成", false, "体験版では固定フレーズ"));
        x.Items.Add(Slider("notif", "リプ通知演出", 50));
        x.Items.Add(Toggle("filter", "センシティブ表現フィルタ", true));
    }

    private List<Def> Cur => _cats[_cat].Items;

    // ── マウス用のジオメトリ定数（_Draw のレイアウトと同一。ホットスポット計算に共用）──
    //   ヘッダ hy=36 → bodyTop=hy+70=106。ナビ navX=40, navW=236, rowH=54, gap=5。
    //   パネル panX=40+236+26=302, panW=W-302-40。カード top=bodyTop+34=140, cardH=54, gap=9。
    private const float NavX = 40f, NavW = 236f, NavRowH = 54f, NavGap = 5f, NavTop = 106f;
    private const float PanX = 302f, CardTop = 140f, CardH = 54f, CardGap = 9f;
    private static float PanW => W - PanX - 40f;
    private static Rect2 NavRowRect(int i) => new Rect2(NavX, NavTop + i * (NavRowH + NavGap), NavW, NavRowH);
    private static Rect2 CardRect(int i) => new Rect2(PanX, CardTop + i * (CardH + CardGap), PanW, CardH);
    // ホットスポット id 空間（単一の HoveredId/ClickedId を種別で解釈するため範囲で分ける）。
    private const int IdNavBase = 1000;   // ナビ・カテゴリ
    private const int IdSegBase = 3000;   // セグメントの各オプション（IdSegBase + row*100 + opt）
    // フッタの「もどる」。カード id（0..Cur.Count）より上・ナビ/セグメントの帯より下に置く
    //   ＝下のホバー追従（hov<Cur.Count／hov>=IdSegBase の分岐）のどちらにも誤って吸われない。
    private const int IdBack    = 500;

    // フッタ「もどる」のクリック矩形。_Draw のフッタは FootHint を左から並べていくので、
    //   手前3つ（↑↓項目／←→調整／カテゴリ）の送り幅を同じ式で足して4つ目の左端を求める。
    //   ※手前3つは「操作説明」なのでクリック対象にしない（ショップの「箱で囲われた方がボタン」の流儀）。
    private static Rect2 BackHintRect()
    {
        float padX = 40f, fy = H - 56f;
        string catTok = Pad.ShowKeyboard ? "Q E" : $"{Pad.Face(JoyButton.LeftShoulder)} {Pad.Face(JoyButton.RightShoulder)}";
        float x = padX;
        x = FootHintAdvance(x, "↑↓", "項目");
        x = FootHintAdvance(x, "←→", "調整");
        x = FootHintAdvance(x, catTok, "カテゴリ");
        string key = Pad.CancelToken;
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        float lw = UiKit.TextW(UiKit.Zen, "もどる", UiKit.FontLabel);
        return new Rect2(x, fy - 16f, kw + 8f + lw + 8f, 32f);
    }

    // FootHint が返す「次の x」だけを、描画せずに求める（FootHint 本体と同一式）。
    private static float FootHintAdvance(float x, string key, string label)
    {
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        return x + kw + 8 + UiKit.TextW(UiKit.Zen, label, UiKit.FontLabel) + 24f;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_autoplay) { GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); return; }

        // マウス：フレーム頭でホットスポットをクリア。設定はポーズ対象外（CanOpenHere=false）＝唯一の登録者。
        UiKit.BeginHotspots(Pad.MousePos());
        bool click = Pad.MouseClick();

        bool cprev = Input.IsKeyPressed(Key.Q) || Pad.Pressed(JoyButton.LeftShoulder);
        bool cnext = Input.IsKeyPressed(Key.E) || Pad.Pressed(JoyButton.RightShoulder);
        if ((cprev || cnext) && !_catHeld)
        {
            if (cprev) _cat = (_cat - 1 + _cats.Count) % _cats.Count;
            if (cnext) _cat = (_cat + 1) % _cats.Count;
            _row = 0;
            Audio.Instance?.PlayUiMove();
        }
        _catHeld = cprev || cnext;

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            if (up) _row = (_row - 1 + Cur.Count) % Cur.Count;
            if (down) _row = (_row + 1) % Cur.Count;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;

        bool l = Input.IsActionPressed("ui_left"), r = Input.IsActionPressed("ui_right");
        if ((l || r) && !_lrHeld) { Adjust(l ? -1 : 1); Audio.Instance?.PlayUiMove(); } // 値変更も手応え（SE音量を耳で確認）
        _lrHeld = l || r;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2) { Adjust(1, viaZ: true); Audio.Instance?.PlayUiConfirm(); }

        // ── マウス：カテゴリ／カード／セグメント各オプション／フッタ「もどる」の登録と処理 ──
        bool clickBack = MouseSettings(click);

        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B)
                    || Pad.MouseRightClick(); // 右クリック＝もどる
        bool backEdge = back && !_backHeld; _backHeld = back;
        if ((backEdge || clickBack) && _t > 0.2) { Audio.Instance?.PlayUiCancel(); Save(); GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); }

        QueueRedraw();
    }

    // マウス操作：左ナビでカテゴリ選択／右カードで項目選択＋値変更。
    //   ・ナビ行クリック → カテゴリ切替（_row リセット）。
    //   ・カード：未選択ならまず選択。選択済みカードの操作領域クリックで値を変える
    //     （スライダー＝トラック上のクリック位置で値を直接設定／トグル＝反転／セグメント＝各オプション直接選択）。
    //   返り値：フッタの「もどる」が押されたか（シーン遷移は呼び出し側＝_Process の back 判定に合流させる）。
    private bool MouseSettings(bool click)
    {
        Vector2 m = Pad.MousePos();
        // フッタの「もどる」（他とは重ならない）。
        UiKit.Hotspot(BackHintRect(), IdBack);
        // ナビ（カテゴリ）: id = IdNavBase + i。
        for (int i = 0; i < _cats.Count; i++) UiKit.Hotspot(NavRowRect(i), IdNavBase + i);
        // カード（項目）: id = i。セグメントは各オプション矩形も別 id で登録（後勝ち＝オプションが優先）。
        for (int i = 0; i < Cur.Count; i++)
        {
            UiKit.Hotspot(CardRect(i), i);
            var d = Cur[i];
            if (d.Type == SType.Segment)
                for (int o = 0; o < d.Options.Length; o++)
                    UiKit.Hotspot(SegmentOptRect(d, i, o), IdSegBase + i * 100 + o);
        }

        // ホバー追従：マウス使用中はカーソル（カテゴリ/行）をホバー先へ寄せる（KBカーソルと同じ強調）。
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= 0)
        {
            if (hov >= IdNavBase && hov < IdNavBase + _cats.Count)
            {
                int ci = hov - IdNavBase;
                if (ci != _cat) { /* カテゴリはクリックで確定＝ホバーだけでは切替えない（誤爆防止） */ }
            }
            else if (hov >= 0 && hov < Cur.Count && hov != _row) { _row = hov; Audio.Instance?.PlayUiMove(); }
            else if (hov >= IdSegBase)
            {
                int ri = (hov - IdSegBase) / 100;
                if (ri != _row) { _row = ri; Audio.Instance?.PlayUiMove(); }
            }
        }

        if (!click) return false;
        int clk = UiKit.HoveredId(); // クリックした瞬間のホバー先
        if (clk < 0) return false;

        // フッタの「もどる」をクリック（遷移は呼び出し側へ返す）。
        if (clk == IdBack) return true;
        // カテゴリをクリック
        if (clk >= IdNavBase && clk < IdNavBase + _cats.Count)
        {
            int ci = clk - IdNavBase;
            if (ci != _cat) { _cat = ci; _row = 0; Audio.Instance?.PlayUiMove(); }
            return false;
        }
        // セグメントのオプションを直接クリック
        if (clk >= IdSegBase)
        {
            int ri = (clk - IdSegBase) / 100, oi = (clk - IdSegBase) % 100;
            _row = ri;
            var d = Cur[ri];
            if (d.Type == SType.Segment && oi >= 0 && oi < d.Options.Length && oi != d.I)
            { d.I = oi; Apply(d); Save(); Audio.Instance?.PlayUiConfirm(); }
            return false;
        }
        // カード本体クリック：未選択なら選択、選択済みなら操作領域のクリック位置で値変更。
        if (clk >= 0 && clk < Cur.Count)
        {
            if (clk != _row) { _row = clk; Audio.Instance?.PlayUiMove(); return false; }
            AdjustByClick(Cur[clk], m);
        }
        return false;
    }

    // 選択済みカードの操作領域を、クリック位置で操作する（スライダー＝比率設定／トグル＝反転）。
    private void AdjustByClick(Def d, Vector2 m)
    {
        switch (d.Type)
        {
            case SType.Slider:
            {
                // トラック矩形（DrawSlider と同一寸法）にクリックした X 比率で値を設定。
                var (tx, trackW) = SliderTrack(_row);
                float ratio = Mathf.Clamp((m.X - tx) / trackW, 0f, 1f);
                d.F = Mathf.RoundToInt(ratio * 20f) * 5f; // 5刻みにスナップ（KB操作と粒度を揃える）
                Apply(d); Save(); Audio.Instance?.PlayUiMove();
                break;
            }
            case SType.Toggle:
                d.B = !d.B; Apply(d); Save(); Audio.Instance?.PlayUiConfirm();
                break;
            default: break; // Select/KeyBind は値変更なし（選択のみ）
        }
    }

    // スライダーのトラック左端 x とトラック幅（DrawSlider と同一算出）。
    private static (float tx, float trackW) SliderTrack(int row)
    {
        var r = CardRect(row);
        float right = r.Position.X + r.Size.X - 18f;
        float valW = 34f, gap = 14f, trackW = 238f;
        float vx = right - valW;
        float tx = vx - gap - trackW;
        return (tx, trackW);
    }

    // セグメントの各オプション矩形（DrawSegment と同一算出）。row/opt で位置を再現する。
    private static Rect2 SegmentOptRect(Def d, int row, int opt)
    {
        var r = CardRect(row);
        float right = r.Position.X + r.Size.X - 18f, cy = r.Position.Y + r.Size.Y / 2f;
        float padIn = 4f, optPadX = 15f, h = 30f;
        int n = d.Options.Length;
        float[] ws = new float[n];
        float total = padIn * 2f;
        for (int i = 0; i < n; i++) { ws[i] = UiKit.TextW(UiKit.ZenBold, d.Options[i], UiKit.FontLabel) + optPadX * 2f; total += ws[i] + (i > 0 ? 4f : 0f); }
        float gx = right - total, gy = cy - h / 2f - padIn;
        float ox = gx + padIn;
        for (int i = 0; i < opt; i++) ox += ws[i] + 4f;
        return new Rect2(ox, gy + padIn, ws[opt], h);
    }

    private void Adjust(int dir, bool viaZ = false)
    {
        if (_row < 0 || _row >= Cur.Count) return;
        var d = Cur[_row];
        switch (d.Type)
        {
            case SType.Slider: if (viaZ) return; d.F = Mathf.Clamp(d.F + dir * 5f, 0f, 100f); break;
            case SType.Toggle: d.B = viaZ ? !d.B : dir > 0; break;
            case SType.Segment: if (d.Options.Length > 0) d.I = (d.I + dir + d.Options.Length) % d.Options.Length; break;
            default: return;
        }
        Apply(d); Save();
    }

    private void ApplyAll() { foreach (var c in _cats) foreach (var d in c.Items) Apply(d); }

    // 0..100 のスライダー値を該当バスの音量に反映（実体は AudioConfig＝ポーズ画面と同一カーブ）。
    private static void SetBusDb(string bus, float f) => AudioConfig.SetBus(bus, f);

    private void Apply(Def d)
    {
        switch (d.Key)
        {
            // 音量（バスは default_bus_layout.tres で定義）。
            // Alert（被弾等の最優先音）は Master のみに従属させ、ここでは下げない。
            case "master": SetBusDb("Master", d.F); break;
            case "bgm":    SetBusDb("Music",  d.F); break;
            case "se":     SetBusDb("SE",     d.F); break;
            case "voice":  SetBusDb("Voice",  d.F); break;
            case "amb":    SetBusDb("Amb",    d.F); break;
            // 会話：メッセージ速度（遅/中/速）とオート送り。GameManager へ反映。
            case "msg":
            {
                var gm = GetNodeOrNull<GameManager>("/root/Game");
                if (gm != null) gm.MsgCharsPerSec = d.I == 0 ? 28f : d.I == 2 ? 80f : 48f;
                break;
            }
            case "auto":
            {
                var gm = GetNodeOrNull<GameManager>("/root/Game");
                if (gm != null) gm.AutoAdvanceDialog = d.B;
                break;
            }
            case "autosave":
            {
                var gm = GetNodeOrNull<GameManager>("/root/Game");
                if (gm != null) gm.AutoSaveEnabled = d.B;
                break;
            }
            case "mode":
                // 初回表示（_Ready の ApplyAll）ではスキップ。ユーザーが明示的に値を変えたときだけ実ウィンドウへ反映する。
                if (!_initializing)
                    DisplayServer.WindowSetMode(d.I == 1 ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
                break;
            // 操作表示モード。セグメントは 0=キーボード / 1=コントローラー の2値になった（Xbox 一本化）。
            // 保存キー inputdisplay は 0=KB / 2=Xbox を書く（旧値 1=PS も読み込み側で Xbox 扱いに合流）。
            case "inputdisplay":
                Pad.Display = d.I <= 0 ? Pad.DisplayMode.Keyboard : Pad.DisplayMode.PadXbox;
                break;
        }
    }

    private void Save()
    {
        var data = new Godot.Collections.Dictionary();
        foreach (var c in _cats) foreach (var d in c.Items)
        {
            if (d.Type == SType.Slider) data[d.Key] = d.F;
            else if (d.Type == SType.Toggle) data[d.Key] = d.B;
            else if (d.Type == SType.Segment) data[d.Key] = d.I;
        }
        using var f = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
        f?.StoreString(Json.Stringify(data));
    }

    private void LoadSettings()
    {
        if (!FileAccess.FileExists(SettingsPath)) return;
        using var f = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary) return;
        var data = json.Data.AsGodotDictionary();
        foreach (var c in _cats) foreach (var d in c.Items)
        {
            if (!data.ContainsKey(d.Key)) continue;
            if (d.Type == SType.Slider) d.F = (float)data[d.Key].AsDouble();
            else if (d.Type == SType.Toggle) d.B = data[d.Key].AsBool();
            else if (d.Type == SType.Segment) d.I = data[d.Key].AsInt32();
        }
        // 操作表示モードのセグメントは 0=キーボード / 1=コントローラー の2値。
        //   ・未保存（旧データ）なら起動時に復元済みの Pad.Display から初期値を決める。
        //   ・保存済みでも旧3値（1=PS / 2=Xbox）が入っていることがあるので 0/1 へ丸める＝
        //     選択肢の範囲外を指したまま描かない（Xbox 一本化でどちらも「コントローラー」に合流）。
        foreach (var c in _cats) foreach (var d in c.Items)
            if (d.Key == "inputdisplay")
                d.I = data.ContainsKey("inputdisplay")
                    ? (d.I <= 0 ? 0 : 1)
                    : (Pad.Display == Pad.DisplayMode.Keyboard ? 0 : 1);
    }

    // ───────── 描画（設計座標 1280×720）─────────
    public override void _Draw()
    {
        UiKit.BeginDesign(this);

        // 背景：放射紫グロウ＋縦グラデ＋スキャンライン＋ビネット
        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0d0b1c"), new Color("0a0916"), new Color("070611") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.80f, 0), 460f, UiKit.Mina, 0.16f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.06f));

        float padX = 40f;
        // ── ヘッダ ──
        float hy = 36f;
        UiKit.Box(this, new Rect2(padX, hy, 40, 40), new Color(UiKit.Purify, 0.12f), 11f, new Color(UiKit.Info, 0.35f), 1f);
        DrawArc(new Vector2(padX + 20, hy + 20), 9f, Mathf.Pi * 0.2f, Mathf.Pi * 1.5f, 24, UiKit.Info, 3f);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(padX + 54, hy + 6), "設定", UiKit.FontTitle, UiKit.White);
        float headingRight = padX + 54 + UiKit.TextW(UiKit.ZenBlack, "設定", UiKit.FontTitle);
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(headingRight + 12, hy + 20), "SETTINGS", UiKit.Text3);
        DrawRect(new Rect2(padX, hy + 50, W - padX * 2, 1f), new Color(1, 1, 1, 0.1f));

        // ── 本体：左ナビ＋右パネル ──
        float bodyTop = hy + 70f;
        float navW = 236f, navX = padX;
        DrawNav(navX, bodyTop, navW);

        float panX = navX + navW + 26f, panW = W - panX - padX;
        DrawPanel(panX, bodyTop, panW);

        // ── フッタ ──
        float fy = H - 56f;
        DrawRect(new Rect2(padX, fy - 14, W - padX * 2, 1f), new Color(1, 1, 1, 0.08f));
        float fx = padX;
        // ボタン表記は Pad 経由＝操作表示モードに追従。Q/E はパッドでは LB/RB(L1/R1)。
        string catTok = Pad.ShowKeyboard ? "Q E" : $"{Pad.Face(JoyButton.LeftShoulder)} {Pad.Face(JoyButton.RightShoulder)}";
        fx = FootHint(fx, fy, "↑↓", "項目");
        fx = FootHint(fx, fy, "←→", "調整");
        fx = FootHint(fx, fy, catTok, "カテゴリ");
        // 「もどる」だけクリックできる＝ホバー中は下敷きを敷いて明るくする
        //   （手前3つは操作説明なので素のまま。ショップの「箱がボタン／素の文字が説明」の流儀）。
        bool backHov = UiKit.HoveredId() == IdBack;
        if (backHov) UiKit.Box(this, BackHintRect(), new Color(UiKit.Purify, 0.14f), 8f, new Color(UiKit.Info, 0.5f), 1f);
        FootHint(fx, fy, Pad.CancelToken, "もどる", backHov);

        UiKit.EndDesign(this);
    }

    private void DrawNav(float x, float y, float w)
    {
        float rowH = 54f, gap = 5f;
        for (int i = 0; i < _cats.Count; i++)
        {
            float ry = y + i * (rowH + gap);
            bool on = i == _cat;
            if (on) UiKit.Box(this, new Rect2(x, ry, w, rowH), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.6f), 11f, new Color(UiKit.Info, 0.4f), 1f);
            // 左バー
            DrawRect(new Rect2(x + 15, ry + 15, 4f, 24f), on ? UiKit.Purify : new Color(0, 0, 0, 0));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 30, ry + 10), _cats[i].Name, UiKit.FontBody, on ? UiKit.White : UiKit.Text2);
            UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + 30, ry + 32), _cats[i].Sub.ToUpper(), on ? UiKit.Info : UiKit.Text4);
        }
    }

    private void DrawPanel(float x, float y, float w)
    {
        var cat = _cats[_cat];
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, y - 4), cat.Name, UiKit.FontHeading, UiKit.White);
        float titleW = UiKit.TextW(UiKit.ZenBlack, cat.Name, UiKit.FontHeading);
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + titleW + 12, y + 6), cat.Sub.ToUpper(), UiKit.Text3);

        float top = y + 34f, cardH = 54f, gap = 9f;
        for (int i = 0; i < Cur.Count; i++)
            DrawCard(Cur[i], x, top + i * (cardH + gap), w, cardH, i == _row);
    }

    private void DrawCard(Def d, float x, float y, float w, float h, bool sel)
    {
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(22 / 255f, 18 / 255f, 34 / 255f, 0.5f), 12f,
            sel ? new Color(UiKit.Info, 0.55f) : new Color(1, 1, 1, 0.07f), sel ? 1.5f : 1f);

        // 左：ラベル＋サブ
        if (d.Sub.Length > 0)
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 18, y + 9), d.Label, UiKit.FontBody, UiKit.White);
            UiKit.Text(this, UiKit.Zen, new Vector2(x + 18, y + 31), d.Sub, UiKit.FontLabel, UiKit.Text3);
        }
        else
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 18, y + (h - 16) / 2f - 2), d.Label, UiKit.FontBody, UiKit.White);
        }

        float right = x + w - 18f, cy = y + h / 2f;
        switch (d.Type)
        {
            case SType.Slider: DrawSlider(d, right, cy); break;
            case SType.Toggle: DrawToggle(d, right, cy); break;
            case SType.Segment: DrawSegment(d, right, cy); break;
            case SType.Select: DrawSelect(d, right, cy); break;
            case SType.KeyBind: DrawKeys(d, right, cy); break;
        }
    }

    private void DrawSlider(Def d, float right, float cy)
    {
        float valW = 34f, gap = 14f, trackW = 238f;
        float vx = right - valW;
        UiKit.Text(this, UiKit.Mono, new Vector2(vx, cy - 9), Mathf.RoundToInt(d.F).ToString(), UiKit.FontBody, UiKit.PurifyHi, HorizontalAlignment.Right, valW);
        float tx = vx - gap - trackW, ty = cy - 4f;
        UiKit.Box(this, new Rect2(tx, ty, trackW, 8f), new Color(1, 1, 1, 0.1f), 4f);
        float fillW = trackW * (d.F / 100f);
        UiKit.Box(this, new Rect2(tx, ty, fillW, 8f), UiKit.Purify, 4f);
        DrawCircle(new Vector2(tx + fillW, cy), 8f, UiKit.White);
    }

    private void DrawToggle(Def d, float right, float cy)
    {
        float pw = 48f, ph = 27f;
        float px = right - pw, py = cy - ph / 2f;
        UiKit.Box(this, new Rect2(px, py, pw, ph), d.B ? UiKit.Purify : new Color(1, 1, 1, 0.14f), ph / 2f);
        float kx = d.B ? px + pw - 24 : px + 3;
        DrawCircle(new Vector2(kx + 10.5f, cy), 10.5f, UiKit.White);
    }

    private void DrawSegment(Def d, float right, float cy)
    {
        // 右端から左へ各オプションを配置
        float padIn = 4f, optPadX = 15f, h = 30f;
        float[] ws = new float[d.Options.Length];
        float total = padIn * 2f;
        for (int i = 0; i < d.Options.Length; i++) { ws[i] = UiKit.TextW(UiKit.ZenBold, d.Options[i], UiKit.FontLabel) + optPadX * 2f; total += ws[i] + (i > 0 ? 4f : 0f); }
        float gx = right - total, gy = cy - h / 2f - padIn;
        UiKit.Box(this, new Rect2(gx, gy, total, h + padIn * 2f), new Color(0, 0, 0, 0.28f), 10f);
        float ox = gx + padIn;
        for (int i = 0; i < d.Options.Length; i++)
        {
            bool on = i == d.I;
            if (on) UiKit.Box(this, new Rect2(ox, gy + padIn, ws[i], h), UiKit.Purify, 7f);
            float textTop = cy - UiKit.ZenBold.GetHeight(UiKit.FontLabel) / 2f;
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ox, textTop), d.Options[i], UiKit.FontLabel, on ? UiKit.BgDeep : UiKit.Text2, HorizontalAlignment.Center, ws[i]);
            ox += ws[i] + 4f;
        }
    }

    private void DrawSelect(Def d, float right, float cy)
    {
        float tw = UiKit.TextW(UiKit.Mono, d.S, UiKit.FontLabel);
        float boxW = tw + 44f, h = 34f;
        float bx = right - boxW;
        UiKit.Box(this, new Rect2(bx, cy - h / 2f, boxW, h), new Color(0, 0, 0, 0.28f), 9f, new Color(1, 1, 1, 0.1f), 1f);
        UiKit.Text(this, UiKit.Mono, new Vector2(bx + 16, cy - 9), d.S, UiKit.FontLabel, UiKit.PurifyHi);
        UiKit.Text(this, UiKit.Zen, new Vector2(bx + boxW - 18, cy - 9), "▾", UiKit.FontSmall, new Color(138 / 255f, 131 / 255f, 152 / 255f));
    }

    private void DrawKeys(Def d, float right, float cy)
    {
        float h = 32f, gap = 6f;
        // 表記は Pad に集約：操作表示モード(KB/PS/Xbox)に応じて各操作のキー/ボタン表記を出し分ける。
        // d.Keys（BuildDefaults の静的キーボード表記）は KB 表示時のフォールバックにのみ使う。
        string[] toks = KeyTokens(d.Key);
        // 右端から逆順に配置
        float x = right;
        for (int i = toks.Length - 1; i >= 0; i--)
        {
            float kw = Mathf.Max(32f, UiKit.TextW(UiKit.Mono, toks[i], UiKit.FontLabel) + 18f);
            x -= kw;
            UiKit.Box(this, new Rect2(x, cy - h / 2f, kw, h), new Color(1, 1, 1, 0.07f), 7f, new Color(1, 1, 1, 0.18f), 1f);
            UiKit.Text(this, UiKit.Mono, new Vector2(x, cy - 9), toks[i], UiKit.FontLabel, new Color("e8e2f0"), HorizontalAlignment.Center, kw);
            x -= gap;
        }
    }

    // 操作キーバインド行の表記を、直近デバイス（Pad.ShowKeyboard）に従って解決する。
    // KB 表示時は従来どおりキーボードキー（移動=矢印、ショット=オート…）。
    // パッド表示時は Pad.Face で物理 JoyButton を Xbox 表記へ（物理マッピングは不変・表記のみ）。
    //   ※"slow"（低速移動）は 2026-09-13 に機能ごと廃止＝行が存在しない。
    private static string[] KeyTokens(string rowKey)
    {
        if (Pad.ShowKeyboard)
            return rowKey switch
            {
                "move"  => new[] { "↑", "↓", "←", "→" },
                "shot"  => new[] { "オート", "Z 長押し" },   // 射撃ボタンは廃止（常時オート発射）。Z 長押し＝溜め打ち
                "lock"  => new[] { "Shift" },                // 押しているあいだロック。F で次の敵へ
                "bomb"  => new[] { "X" },
                "dodge" => new[] { "Ctrl" },
                "focus" => new[] { "V" },
                "pause" => new[] { "M", "Esc" },
                _       => System.Array.Empty<string>(),
            };
        // パッド表記（Xbox 基準）。Player.cs の入力判定と一致させる。
        return rowKey switch
        {
            "move"  => new[] { "L" },                                    // 左スティック
            "shot"  => new[] { "オート", Pad.Face(JoyButton.Y) + " 長押し" }, // 射撃ボタンは廃止（常時オート発射）。Y 長押し＝溜め打ち
            "lock"  => new[] { Pad.Face(JoyButton.RightShoulder) },
            "bomb"  => new[] { Pad.Face(JoyButton.X) },
            "dodge" => new[] { Pad.Face(JoyButton.LeftStick) },
            "focus" => new[] { Pad.Face(JoyButton.LeftShoulder) },
            "pause" => new[] { Pad.Face(JoyButton.Start) },
            _       => System.Array.Empty<string>(),
        };
    }

    // hot=true はマウスホバー中（クリックできるヒントだけが取りうる状態）＝縁と文字を一段明るく。
    private float FootHint(float x, float y, string key, string label, bool hot = false)
    {
        UiKit.Key(this, new Vector2(x, y - 12), key,
            hot ? new Color(UiKit.Purify, 0.16f) : new Color(1, 1, 1, 0.07f),
            hot ? new Color(UiKit.Info, 0.6f) : new Color(1, 1, 1, 0.16f),
            hot ? UiKit.PurifyHi : UiKit.Text2);
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + kw + 8, y - 8), label, UiKit.FontLabel, hot ? UiKit.Info : UiKit.Text3);
        return x + kw + 8 + UiKit.TextW(UiKit.Zen, label, UiKit.FontLabel) + 24f;
    }
}
