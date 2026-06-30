using Godot;
using System.Collections.Generic;

// Settings : 設定画面。RefrainHTML/Refrain Settings.dc.html を忠実移植（非ピクセル・滑らかUI）。
//   左カテゴリ（6）→右コントロール。←→ で値変更・↑↓ 項目・Q/E カテゴリ・X/Esc でタイトルへ。
//   値は user://settings.json に永続。マスター音量と画面モードのみエンジンへ即時反映。
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

    private const string SettingsPath = "user://settings.json";

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        BuildDefaults();
        LoadSettings();
        ApplyAll();
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
        // 操作表示モード：ヒント/ボタン表記を固定（0=キーボード / 1=PlayStation / 2=Xbox。inputdisplay と一致）。
        ctrl.Items.Add(Seg("inputdisplay", "操作表示", new[] { "キーボード", "PlayStation", "Xbox" }, 0, "ヒントの表記を固定"));
        ctrl.Items.Add(Keys("move", "移動", new[] { "↑", "↓", "←", "→" }));
        ctrl.Items.Add(Keys("shot", "ショット", new[] { "Z" }));
        ctrl.Items.Add(Keys("bomb", "ボム", new[] { "X" }));
        ctrl.Items.Add(Keys("slow", "低速移動", new[] { "Shift" }, "判定を見ながら避ける"));
        ctrl.Items.Add(Keys("dodge", "回避", new[] { "Alt" }, "一瞬無敵で弾を抜ける"));
        ctrl.Items.Add(Keys("pause", "ポーズ", new[] { "Esc" }));

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

    public override void _Process(double delta)
    {
        _t += delta;
        if (_autoplay) { GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); return; }

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

        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2) { Audio.Instance?.PlayUiCancel(); Save(); GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); }

        QueueRedraw();
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
                DisplayServer.WindowSetMode(d.I == 1 ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
                break;
            // 操作表示モード（0=キーボード / 1=PlayStation / 2=Xbox）。HUD の操作ガイドへ即反映。
            // 旧 padstyle(0=Xbox/1=PS) は Pad 側で後方互換に読むだけ（このセグメントが上書きする）。
            case "inputdisplay":
                Pad.Display = Pad.IntToDisplay(d.I); // 0/1/2 → Keyboard/PS/Xbox
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
        // 操作表示モードが未保存（旧データ）なら、起動時に復元済みの Pad.Display を反映して
        // セグメントの初期値が実態とズレないようにする（Auto のときは Xbox 表示に寄せる）。
        if (!data.ContainsKey("inputdisplay"))
            foreach (var c in _cats) foreach (var d in c.Items)
                if (d.Key == "inputdisplay")
                    d.I = Pad.Display == Pad.DisplayMode.Auto ? 2 : Pad.DisplayToInt(Pad.Display);
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
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(padX + 54, hy + 6), "設定", 25, UiKit.White);
        UiKit.Text(this, UiKit.Mono, new Vector2(padX + 110, hy + 20), "SETTINGS", 11, UiKit.Text4);
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
        FootHint(fx, fy, Pad.CancelToken, "もどる");

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
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 30, ry + 10), _cats[i].Name, 16, on ? UiKit.White : UiKit.Text2);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + 30, ry + 32), _cats[i].Sub.ToUpper(), 9, on ? UiKit.Info : UiKit.Text4);
        }
    }

    private void DrawPanel(float x, float y, float w)
    {
        var cat = _cats[_cat];
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, y - 4), cat.Name, 20, UiKit.White);
        float titleW = UiKit.TextW(UiKit.ZenBlack, cat.Name, 20);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + titleW + 12, y + 6), cat.Sub.ToUpper(), 10, UiKit.Text4);

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
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 18, y + 9), d.Label, 16, UiKit.White);
            UiKit.Text(this, UiKit.Zen, new Vector2(x + 18, y + 31), d.Sub, 12, UiKit.Text3);
        }
        else
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 18, y + (h - 16) / 2f - 2), d.Label, 16, UiKit.White);
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
        UiKit.Text(this, UiKit.Mono, new Vector2(vx, cy - 9), Mathf.RoundToInt(d.F).ToString(), 15, UiKit.PurifyHi, HorizontalAlignment.Right, valW);
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
        for (int i = 0; i < d.Options.Length; i++) { ws[i] = UiKit.TextW(UiKit.ZenBold, d.Options[i], 14) + optPadX * 2f; total += ws[i] + (i > 0 ? 4f : 0f); }
        float gx = right - total, gy = cy - h / 2f - padIn;
        UiKit.Box(this, new Rect2(gx, gy, total, h + padIn * 2f), new Color(0, 0, 0, 0.28f), 10f);
        float ox = gx + padIn;
        for (int i = 0; i < d.Options.Length; i++)
        {
            bool on = i == d.I;
            if (on) UiKit.Box(this, new Rect2(ox, gy + padIn, ws[i], h), UiKit.Purify, 7f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ox, cy - 8), d.Options[i], 14, on ? UiKit.White : new Color(138 / 255f, 131 / 255f, 152 / 255f), HorizontalAlignment.Center, ws[i]);
            ox += ws[i] + 4f;
        }
    }

    private void DrawSelect(Def d, float right, float cy)
    {
        float tw = UiKit.TextW(UiKit.Mono, d.S, 14);
        float boxW = tw + 44f, h = 34f;
        float bx = right - boxW;
        UiKit.Box(this, new Rect2(bx, cy - h / 2f, boxW, h), new Color(0, 0, 0, 0.28f), 9f, new Color(1, 1, 1, 0.1f), 1f);
        UiKit.Text(this, UiKit.Mono, new Vector2(bx + 16, cy - 9), d.S, 14, UiKit.PurifyHi);
        UiKit.Text(this, UiKit.Zen, new Vector2(bx + boxW - 18, cy - 9), "▾", 12, new Color(138 / 255f, 131 / 255f, 152 / 255f));
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
            float kw = Mathf.Max(32f, UiKit.TextW(UiKit.Mono, toks[i], 14) + 18f);
            x -= kw;
            UiKit.Box(this, new Rect2(x, cy - h / 2f, kw, h), new Color(1, 1, 1, 0.07f), 7f, new Color(1, 1, 1, 0.18f), 1f);
            UiKit.Text(this, UiKit.Mono, new Vector2(x, cy - 9), toks[i], 14, new Color("e8e2f0"), HorizontalAlignment.Center, kw);
            x -= gap;
        }
    }

    // 操作キーバインド行の表記を、操作表示モード(Pad.Display)に従って解決する。
    // KB 表示時は従来どおりキーボードキー（移動=矢印、ショット=Z…）。
    // パッド表示時は Pad.Face で物理 JoyButton を Xbox/PS 表記に出し分ける（物理マッピングは不変・表記のみ）。
    private static string[] KeyTokens(string rowKey)
    {
        if (Pad.ShowKeyboard)
            return rowKey switch
            {
                "move"  => new[] { "↑", "↓", "←", "→" },
                "shot"  => new[] { "Z" },
                "bomb"  => new[] { "X" },
                "slow"  => new[] { "Shift" },
                "dodge" => new[] { "Alt" },
                "pause" => new[] { "Esc" },
                _       => System.Array.Empty<string>(),
            };
        // パッド表記（Pad.Style に従い Xbox/PS）。Player.cs の入力判定と一致させる。
        return rowKey switch
        {
            "move"  => new[] { "L" },                                    // 左スティック
            "shot"  => new[] { Pad.Face(JoyButton.A) },
            "bomb"  => new[] { Pad.Face(JoyButton.X) },
            "slow"  => new[] { Pad.Face(JoyButton.LeftShoulder), Pad.Face(JoyButton.RightShoulder) },
            "dodge" => new[] { Pad.Face(JoyButton.LeftStick) },
            "pause" => new[] { Pad.Face(JoyButton.Start) },
            _       => System.Array.Empty<string>(),
        };
    }

    private float FootHint(float x, float y, string key, string label)
    {
        UiKit.Key(this, new Vector2(x, y - 12), key, new Color(1, 1, 1, 0.07f), new Color(1, 1, 1, 0.16f), UiKit.Text2);
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + kw + 8, y - 8), label, 13, UiKit.Text3);
        return x + kw + 8 + UiKit.TextW(UiKit.Zen, label, 13) + 24f;
    }
}
