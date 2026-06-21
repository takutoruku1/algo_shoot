using Godot;

// DiffSelect : 難易度選択。RefrainHTML/Refrain Screens.dc.html(Screen 2) を忠実移植（非ピクセル）。
//   4ティア＋弾密度メーター。報酬倍率＝緑／選択＝シアン／ルナ解禁＝紫。↑↓ えらぶ・Z ダイブ・X もどる。
public partial class DiffSelect : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    private struct Tier
    {
        public string Name, Desc; public GameManager.Diff Diff; public int Density;
        public string Face;   // MINA 立ち絵（難易度に応じた表情）
        public string Quip;   // 立ち絵脇のミナの一言
    }
    private static readonly Tier[] Tiers =
    {
        new() { Name = "やさしい",     Desc = "弾は少なく、ゆっくり。物語を追いたい人へ。", Diff = GameManager.Diff.Easy,    Density = 2,
                Face = "res://char/mina_smile.png",   Quip = "ゆっくりで、いいんですよ。" },
        new() { Name = "ふつう",       Desc = "標準的な弾幕。",                             Diff = GameManager.Diff.Normal,  Density = 3,
                Face = "res://char/mina_face.png",    Quip = "では、いつも通りに。" },
        new() { Name = "むずかしい",   Desc = "弾が増え、密度が上がる。",                   Diff = GameManager.Diff.Hard,    Density = 4,
                Face = "res://char/mina_worried.png", Quip = "……無理は、しないでくださいね。" },
        new() { Name = "ルナティック", Desc = "極限の弾幕。最大強化前提の挑戦。",           Diff = GameManager.Diff.Lunatic, Density = 5,
                Face = "res://char/mina_tears.png",   Quip = "……覚悟は、できていますか。" },
    };

    private int _sel;
    private bool _navHeld, _zHeld, _backHeld, _hNavHeld;
    private double _t;
    private bool _autoplay;
    private string _stageTag = "STAGE 1", _diveName = "レイ";

    // ── チェックポイント入口（最初から / 中ボスから / ボスから）──
    //   中ボスを持つ3ステージでのみ表示。←→ で選ぶ。未解放はロック（中ボス=IsMidBossCleared / ボス=IsStageCleared で解放）。
    private string? _stageId;
    private bool _hasMidBoss;
    private int _entrySel; // 0=最初から / 1=中ボスから / 2=ボスから
    private static readonly string[] EntryNames = { "最初から", "中ボスから", "ボスから" };

    // ── MINA 立ち絵（表情クロスフェード）──
    //   選択が変わると _faceFrom→_faceTo を _xfade(0→1) で溶かす。瞬間差し替えにしない（吉田 §C）。
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _faceCache = new();
    private Texture2D? _faceFrom, _faceTo;
    private double _xfade = 1.0;      // 1=完了
    private const double XfadeDur = 0.22;
    private double _swapAt = -999;    // 切替時刻（アンティシペーション計測用）

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        foreach (var s in GameManager.Stages)
            if (s.Scene == _game?.PendingStageScene)
            {
                if (s.Title.Contains("—")) { var p = s.Title.Split('—'); _stageTag = p[0].Trim(); _diveName = p[^1].Trim(); }
                else _diveName = s.Title;
                break;
            }

        // 選択中ステージのIDと、入口選択を出す対象（中ボス持ち）かを判定。入口の既定は「最初から」。
        _stageId = GameManager.StageIdForScene(_game?.PendingStageScene ?? "");
        _hasMidBoss = _stageId != null && GameManager.StageHasMidBoss(_stageId);
        _entrySel = 0;

        _sel = (int)(_game?.Difficulty ?? GameManager.Diff.Normal);
        if (!Selectable(_sel)) _sel = (int)GameManager.Diff.Hard;

        // --diff=N : スクショ/検証用に初期選択を上書き（本番フローには無影響。Selectable無視で表情を撮れる）。
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--diff=") && int.TryParse(a.Substring(7), out int d) && d >= 0 && d < Tiers.Length)
                _sel = d;

        _faceTo = _faceFrom = LoadFace(Tiers[_sel].Face);
        _xfade = 1.0;
    }

    private Texture2D? LoadFace(string path)
    {
        if (_faceCache.TryGetValue(path, out var t)) return t;
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex != null) _faceCache[path] = tex;
        return tex;
    }

    // 選択が変わったら立ち絵をクロスフェード開始（旧→新）。
    private void StartFaceSwap(int newSel)
    {
        var next = LoadFace(Tiers[newSel].Face);
        if (next == _faceTo) return;
        _faceFrom = _faceTo;
        _faceTo = next;
        _xfade = 0.0;
        _swapAt = _t;
    }

    private bool Selectable(int i)
    {
        if (i < 0 || i >= Tiers.Length) return false;
        if (Tiers[i].Diff == GameManager.Diff.Lunatic) return _game?.IsLunaticUnlocked ?? false;
        return true;
    }

    // 入口の解放判定：最初から＝常時／中ボスから＝中ボス撃破済み／ボスから＝ステージクリア済み。
    private bool EntryUnlocked(int e)
    {
        if (_stageId == null) return e == 0;
        return e switch
        {
            1 => _game?.IsMidBossCleared(_stageId) ?? false,
            2 => _game?.IsStageCleared(_stageId) ?? false,
            _ => true,
        };
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_xfade < 1.0) _xfade = System.Math.Min(1.0, _xfade + delta / XfadeDur);
        if (_autoplay) { Dive(); QueueRedraw(); return; }

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            int n = Tiers.Length;
            if (up) _sel = (_sel - 1 + n) % n;
            if (down) _sel = (_sel + 1) % n;
            StartFaceSwap(_sel);
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;

        // ←→：チェックポイント入口の選択（中ボス持ちステージのみ）。未解放はスキップして次の解放済みへ。
        if (_hasMidBoss)
        {
            bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
            if ((left || right) && !_hNavHeld)
            {
                int dir = right ? 1 : -1;
                for (int k = 0; k < EntryNames.Length; k++)
                {
                    _entrySel = (_entrySel + dir + EntryNames.Length) % EntryNames.Length;
                    if (EntryUnlocked(_entrySel)) break;
                }
                Audio.Instance?.PlayUiMove();
            }
            _hNavHeld = left || right;
        }

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2 && Selectable(_sel)) { Audio.Instance?.PlayUiConfirm(); Dive(); }

        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2) { Audio.Instance?.PlayUiCancel(); GetTree().ChangeSceneToFile("res://Hub.tscn"); }

        QueueRedraw();
    }

    private void Dive()
    {
        if (_game != null && Selectable(_sel)) _game.Difficulty = Tiers[_sel].Diff;
        // 選んだ入口を GameManager へ（ラン単位・非セーブ）。未解放や非対象ステージは「最初から」へフォールバック。
        if (_game != null)
            _game.SelectedEntry = (_hasMidBoss && EntryUnlocked(_entrySel))
                ? (GameManager.StageEntry)_entrySel
                : GameManager.StageEntry.Start;
        string scene = _game?.PendingStageScene ?? "res://Rei.tscn";
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().ChangeSceneToFile(scene);
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);

        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0c142a"), new Color("0a1022"), new Color("070a16") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.5f, 0), 460f, new Color(120 / 255f, 150 / 255f, 210 / 255f), 0.14f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        float padX = 56f, top = 40f;
        // ── ヘッダ ──
        UiKit.Text(this, UiKit.Mono, new Vector2(padX, top + 8), _stageTag, 13, UiKit.Info);
        float tagW = UiKit.TextW(UiKit.Mono, _stageTag, 13);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(padX + tagW + 16, top), $"{_diveName} へダイブ", 28, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(padX, top + 4), "難易度で「弾の数」が変わります（浄化した心の報酬も変動）", 14, UiKit.Text3,
            HorizontalAlignment.Right, W - padX * 2);
        DrawRect(new Rect2(padX, top + 44, W - padX * 2, 1f), new Color(1, 1, 1, 0.1f));

        // ── 右カラム：MINA 立ち絵（選択難易度の表情）。先に描いて、ティア行は左へ寄せる ──
        float colW = 360f;                       // 右の立ち絵カラム幅
        float colX = W - padX - colW;            // 左端
        DrawMinaColumn(colX, top + 66f, colW, H - 56f - (top + 66f) - 18f);

        // ── ティア行（左カラムへ寄せる）──
        float rowTop = top + 66f, rowH = 84f, gap = 12f;
        float rowW = colX - padX - 28f;          // 立ち絵カラムとの間に余白
        for (int i = 0; i < Tiers.Length; i++)
            DrawTier(i, padX, rowTop + i * (rowH + gap), rowW, rowH);

        // ── 入口（チェックポイント）選択：ティアの下に横並び（中ボス持ちステージのみ）──
        if (_hasMidBoss)
        {
            float ey = rowTop + Tiers.Length * (rowH + gap) + 6f;
            DrawEntryRow(padX, ey, rowW, 58f);
        }

        // ── フッタ ──
        float fy = H - 56f;
        DrawRect(new Rect2(padX, fy - 14, W - padX * 2, 1f), new Color(1, 1, 1, 0.08f));
        float fx = padX;
        fx = Hint(fx, fy, "↑↓", "難易度", false);
        if (_hasMidBoss) fx = Hint(fx, fy, "←→", "入口", false);
        fx = Hint(fx, fy, "Z", "ダイブ", true);
        Hint(fx, fy, "X", "もどる", false);

        UiKit.EndDesign(this);
    }

    private void DrawTier(int i, float x, float y, float w, float h)
    {
        var tr = Tiers[i];
        bool sel = i == _sel;
        bool luna = tr.Diff == GameManager.Diff.Lunatic;
        bool locked = luna && !(_game?.IsLunaticUnlocked ?? false);

        if (locked)
            UiKit.Box(this, new Rect2(x, y, w, h), new Color(16 / 255f, 14 / 255f, 24 / 255f, 0.5f), 14f, new Color(1, 1, 1, 0.05f), 1f);
        else if (sel)
            UiKit.Box(this, new Rect2(x, y, w, h), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.6f), 14f, new Color(UiKit.Purify, 0.85f), 1.5f);
        else
            UiKit.Box(this, new Rect2(x, y, w, h), new Color(22 / 255f, 18 / 255f, 34 / 255f, 0.55f), 14f, new Color(1, 1, 1, 0.09f), 1f);

        float tx = x + 24f;
        if (locked)
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, y + 22), "★ " + tr.Name, 22, UiKit.Text4);
            UiKit.Text(this, UiKit.Zen, new Vector2(tx, y + 54), "解禁：フォロワー 300 または 威力 Lv4", 14, UiKit.Mina);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 100, y + h / 2f - 7), "LOCKED", 12, UiKit.Text4);
            return;
        }

        // 名前（選択時 ▸ カーソル）
        if (sel)
        {
            UiKit.Text(this, UiKit.Mono, new Vector2(tx, y + 24), "▸", 15, UiKit.Purify);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tx + 22, y + 20), tr.Name, 22, UiKit.White);
            UiKit.Text(this, UiKit.Zen, new Vector2(tx + 22, y + 54), tr.Desc, 14, new Color(166 / 255f, 196 / 255f, 212 / 255f));
        }
        else
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, y + 20), tr.Name, 22, UiKit.White);
            UiKit.Text(this, UiKit.Zen, new Vector2(tx, y + 54), tr.Desc, 14, UiKit.Text3);
        }

        // 右：弾密度メーター＋報酬倍率
        Color pipCol = luna ? UiKit.Kegare : (tr.Diff == GameManager.Diff.Hard ? new Color("e89460") : UiKit.Purify);
        float pipW = 11f, pipH = 8f, pipGap = 4f;
        float meterW = 5 * pipW + 4 * pipGap;
        float mx = x + w - 24f - meterW, my = y + 28f;
        for (int k = 0; k < 5; k++)
            UiKit.Box(this, new Rect2(mx + k * (pipW + pipGap), my, pipW, pipH), k < tr.Density ? pipCol : new Color(1, 1, 1, 0.12f), 2f);

        float mul = GameManager.DifficultyImpressionMulFor(tr.Diff);
        string reward = $"報酬 ×{mul:0.0}";
        UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 24f - UiKit.TextW(UiKit.Mono, reward, 13), y + 50), reward, 13, new Color("7ec880"));
    }

    // ── 入口（チェックポイント）選択：最初から / 中ボスから / ボスから を横並びで描く ──
    //   未解放はグレーで LOCK 表示。選択中はシアン枠＋▸。←→ で選ぶ。
    private void DrawEntryRow(float x, float y, float w, float h)
    {
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y - 16), "STAGE ENTRY ／ 入口", 11, UiKit.Text3);

        int n = EntryNames.Length;
        float gap = 12f;
        float cellW = (w - gap * (n - 1)) / n;
        for (int i = 0; i < n; i++)
        {
            float cx = x + i * (cellW + gap);
            bool unlocked = EntryUnlocked(i);
            bool sel = i == _entrySel;

            Color bg, border; float bw;
            if (!unlocked) { bg = new Color(16 / 255f, 14 / 255f, 24 / 255f, 0.5f); border = new Color(1, 1, 1, 0.05f); bw = 1f; }
            else if (sel) { bg = new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.6f); border = new Color(UiKit.Purify, 0.85f); bw = 1.5f; }
            else { bg = new Color(22 / 255f, 18 / 255f, 34 / 255f, 0.55f); border = new Color(1, 1, 1, 0.09f); bw = 1f; }
            UiKit.Box(this, new Rect2(cx, y, cellW, h), bg, 12f, border, bw);

            float tx = cx + 16f;
            if (sel && unlocked) { UiKit.Text(this, UiKit.Mono, new Vector2(tx, y + 14), "▸", 14, UiKit.Purify); tx += 18f; }
            Color nameCol = unlocked ? (sel ? UiKit.White : UiKit.Text2) : UiKit.Text4;
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, y + 12), EntryNames[i], 16, nameCol);

            // サブ説明 or ロック解放条件
            string sub = unlocked
                ? i switch { 1 => "中ボス戦から開始", 2 => "ボス戦から開始", _ => "道中の最初から" }
                : i switch { 1 => "解放：中ボスを倒す", _ => "解放：ステージクリア" };
            UiKit.Text(this, UiKit.Zen, new Vector2(cx + 16f, y + 34), sub, 12, unlocked ? UiKit.Text3 : UiKit.Mina);

            if (!unlocked)
                UiKit.Text(this, UiKit.Mono, new Vector2(cx + cellW - UiKit.TextW(UiKit.Mono, "LOCKED", 10) - 12, y + 12), "LOCKED", 10, UiKit.Text4);
        }
    }

    // ── 右カラム：MINA 立ち絵 ＋ 一言（選択難易度の表情をクロスフェードで反映）──
    private void DrawMinaColumn(float x, float y, float w, float h)
    {
        var tr = Tiers[_sel];
        bool luna = tr.Diff == GameManager.Diff.Lunatic;
        bool locked = luna && !(_game?.IsLunaticUnlocked ?? false);

        // カラム下敷き（左のカードと同じガラストーン）
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(18 / 255f, 16 / 255f, 30 / 255f, 0.34f), 16f, new Color(1, 1, 1, 0.06f), 1f);

        // アクセント色（カードのpip色に合わせる）：通常=Purify / Hard=橙 / Luna=穢れピンク
        Color accent = luna ? UiKit.Kegare : (tr.Diff == GameManager.Diff.Hard ? new Color("e89460") : UiKit.Purify);

        // ── 立ち絵の配置（縦長 474x720 を高さ基準でフィット、足元をカラム下端へ）──
        var tex = _faceTo ?? _faceFrom;
        float baseCx = x + w * 0.5f;
        float footY = y + h - 64f;               // 足元（一言ぶんの帯を下に残す）

        // 呼吸：周期3.4s。Hard/Luna は息を詰める＝振幅を落とす。ロック中は完全停止（緊張）。
        float breathAmp = locked ? 0f : (luna ? 1.0f : (tr.Diff == GameManager.Diff.Hard ? 1.4f : 2.2f));
        float scaleAmp  = locked ? 0f : (luna ? 0.004f : 0.007f);
        float tiltAmp   = (locked || luna) ? 0f : 0.5f; // ルナとロックは傾けない＝硬い
        float ph = (float)_t * Mathf.Tau / 3.4f;
        float breathY = Mathf.Sin(ph) * breathAmp;
        float breathS = 1f + Mathf.Sin(ph) * scaleAmp;
        float tiltDeg = Mathf.Sin((float)_t * 0.5f) * tiltAmp;

        // アンティシペーション→フォロースルー：切替直後 0.10s 軽く沈み(+6px,0.97倍)、その後 0.18s で 1.03→1.0 へ。
        float dt = (float)(_t - _swapAt);
        float swapY = 0f, swapS = 1f;
        if (dt < 0.10f) { float k = dt / 0.10f; swapY = Mathf.Lerp(0f, 6f, k); swapS = Mathf.Lerp(1f, 0.97f, k); }
        else if (dt < 0.40f) { float k = (dt - 0.10f) / 0.30f; float e = 1f - (1f - k) * (1f - k); swapY = Mathf.Lerp(6f, 0f, e); swapS = Mathf.Lerp(0.97f, 1f, e) + Mathf.Sin(k * Mathf.Pi) * 0.03f; }

        // 足元グロウ（ミナ色）＋ 選択アクセントのリム
        UiKit.RadialGlow(this, new Vector2(baseCx, footY + 6f), 150f, UiKit.Mina, 0.20f);
        UiKit.RadialGlow(this, new Vector2(baseCx, footY - 110f), 200f, accent, locked ? 0.05f : 0.10f);

        if (tex != null)
        {
            float maxH = h - 96f;
            float maxW = w - 28f;                 // カラム内に収める横幅上限
            float drawH = maxH;
            float drawW = drawH * tex.GetWidth() / tex.GetHeight();
            if (drawW > maxW) { drawW = maxW; drawH = drawW * tex.GetHeight() / tex.GetWidth(); }
            drawH *= breathS * swapS; drawW *= breathS * swapS;
            float cx = baseCx;
            float topY = footY - drawH + breathY + swapY;

            // 微傾き：ピボット（足元）回りに回転。設計スケールは維持したまま回す。
            float pivotY = footY;
            DrawSetTransform(new Vector2(cx, pivotY) * UiKit.Scale, Mathf.DegToRad(tiltDeg),
                new Vector2(UiKit.Scale, UiKit.Scale));
            var dstLocal = new Rect2(-drawW * 0.5f, topY - pivotY, drawW, drawH);

            // クロスフェード：旧表情をα落とししつつ新を上げる。ロック中は暗く沈める（穢れ＝届く前の硬さ）。
            Color tint = locked ? new Color(0.42f, 0.40f, 0.52f, 1f) : new Color(1, 1, 1, 1f);
            float xf = (float)_xfade;
            if (_faceFrom != null && _faceFrom != _faceTo && xf < 1f)
                DrawTextureRect(_faceFrom, dstLocal, false, tint with { A = 1f - xf });
            if (_faceTo != null)
                DrawTextureRect(_faceTo, dstLocal, false, tint with { A = (_faceFrom == _faceTo) ? 1f : xf });

            // 設計スケールへ戻す
            DrawSetTransform(Vector2.Zero, 0f, new Vector2(UiKit.Scale, UiKit.Scale));
        }

        // ── 一言（立ち絵の下／吹き出し風の帯）──
        string quip = locked ? "……まだ、その先は見せられません。" : tr.Quip;
        float qh = 44f, qy = y + h - qh - 12f;
        UiKit.Box(this, new Rect2(x + 16, qy, w - 32, qh), new Color(10 / 255f, 14 / 255f, 26 / 255f, 0.66f), 12f, new Color(accent, locked ? 0.18f : 0.32f), 1f);
        // 話者ドット＋名前
        DrawCircle(new Vector2(x + 16 + 18, qy + qh / 2f), 4f, UiKit.Mina);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 16 + 30, qy + 7), "ミナ", 12, UiKit.Mina);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 16 + 30, qy + 22), quip, 13, locked ? UiKit.Text3 : UiKit.Text2,
            HorizontalAlignment.Left, w - 32 - 30 - 14);
    }

    private float Hint(float x, float y, string key, string label, bool accent)
    {
        Color kbg = accent ? new Color(UiKit.Purify, 0.12f) : new Color(1, 1, 1, 0.07f);
        Color kbd = accent ? new Color(UiKit.Info, 0.5f) : new Color(1, 1, 1, 0.16f);
        UiKit.Key(this, new Vector2(x, y - 12), key, kbg, kbd, accent ? UiKit.PurifyHi : UiKit.Text2);
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + kw + 8, y - 8), label, 14, accent ? UiKit.Info : UiKit.Text3);
        return x + kw + 8 + UiKit.TextW(UiKit.Zen, label, 14) + 24f;
    }
}
