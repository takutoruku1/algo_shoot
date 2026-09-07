using Godot;

// DiffSelect : 潜り方（難易度）の選択。
//   2026-09-06（2-b）以降、ハブの投稿詳細（Hub.cs の Mode.Detail）が通常の潜り方の選択を吸収し、
//   さらに 2026-09-07 に入口（最初から/中ボスから/ボスから）を問う画面を廃止したため、
//   **通常プレイでこの画面へ来る導線は無い**（ハブから直接ステージへ潜る）。
//   単体シーンとしては生きているので、デバッグ起動（DiffSelect.tscn を直接実行）と
//   スクショ検証（--diff=N）でこれまでどおり使える。消さずに残すのはそのため。
//   潜り方の段の名前・一言はハブ側（Hub.Tiers）と同じ語彙に揃える（数値・実装は不変）。
//   4ティア＋弾密度メーター。選択＝シアン／底まで解禁＝紫。↑↓ 潜り方・Z 潜る・X もどる。
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
    // Density（弾密度メーター、5マス満点）は実際のリスクである GameManager.BulletCountMul
    // （GameManager.cs:63 — Easy0.38/Normal0.7/Hard1.1/Lunatic1.9、Lunaticが最大値）に比例させて算出。
    // 5 * mul/1.9 を四捨五入：Easy1/Normal2/Hard3/Lunatic5。旧実装は毎段+1の線形(2/3/4/5)で、
    // 実値では最大の跳ね幅であるHard→Lunatic(+0.8, 全区間最大)が他の段(+0.32/+0.4)と同じ+1マスにしか
    // 見えず、Lunaticへの賭け金を過小に見せていた。以後 BulletCountMul を変えたらここも合わせて見直すこと。
    private static readonly Tier[] Tiers =
    {
        // 名前はハブの投稿詳細と同じ 浅く／いつも通り／深く／底まで（3-1）。説明はミナの観測の言い方に寄せる。
        new() { Name = "浅く",       Desc = "光は六つ。弾は少なく、ゆっくり。",   Diff = GameManager.Diff.Easy,    Density = 1,
                Face = "res://char/mina_smile.png",   Quip = "ゆっくりで、いいんですよ。" },
        new() { Name = "いつも通り", Desc = "光は四つ。",                         Diff = GameManager.Diff.Normal,  Density = 2,
                Face = "res://char/mina_face.png",    Quip = "では、いつも通りに。" },
        new() { Name = "深く",       Desc = "光は三つ。弾が増え、密度が上がる。", Diff = GameManager.Diff.Hard,    Density = 3,
                Face = "res://char/mina_worried.png", Quip = "……無理は、しないでくださいね。" },
        new() { Name = "底まで",     Desc = "光は二つ。最大強化前提の深さ。",     Diff = GameManager.Diff.Lunatic, Density = 5,
                Face = "res://char/mina_tears.png",   Quip = "……覚悟は、できていますか。" },
    };

    private int _sel;
    private bool _navHeld, _zHeld, _backHeld;
    private double _t;
    private bool _autoplay;
    private string _stageTag = "STAGE 1", _diveName = "あかり";

    // ── チェックポイント入口について（2026-09-07 に画面を廃止）──
    //   以前は難易度を確定したあと「どこから始めますか?（最初から／中ボスから／ボスから）」の
    //   モーダルを開いていたが、ユーザー実機指摘「どこからやるを非表示にして」「基本的に最初から
    //   始める仕様で OK」により**通常プレイの導線を廃止**した。潜り方を選んだらそのままダイブする。
    //   開始位置の仕組み自体（GameManager.StageEntry / 各 Stage の _step 飛ばし）は残っており、
    //   起動フラグ `--boss`（GameManager.cs:1216）とゲームオーバーの R リトライ（*Root.cs）が
    //   従来どおり SelectedEntry を直接立てて使う＝デバッグとリトライは壊れない。

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

        // FINAL は GameManager.Stages に持たない（AllStoryCleared/NextUnclearedStageId が本編3ステージを
        // 数えるため）。ここだけ明示に見出しを与える。未登録シーンでも既定の "STAGE 1 — あかり" を出さない。
        if (_game?.PendingStageScene == "res://MinaBattle.tscn") { _stageTag = "FINAL"; _diveName = "ミナ"; }
        else
            foreach (var s in GameManager.Stages)
                if (s.Scene == _game?.PendingStageScene)
                {
                    if (s.Title.Contains("—")) { var p = s.Title.Split('—'); _stageTag = p[0].Trim(); _diveName = p[^1].Trim(); }
                    else _diveName = s.Title;
                    break;
                }

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

    // ── マウス用ジオメトリ（_Draw と同一式）──
    //   ティア行：padX=56, rowTop=top+66=106, rowH=84, gap=12, rowW=colX-padX-28（colX=W-56-360）。
    private static Rect2 TierRect(int i)
    {
        float padX = 56f, top = 40f, colW = 360f;
        float colX = W - padX - colW;
        float rowTop = top + 66f, rowH = 84f, gap = 12f;
        float rowW = colX - padX - 28f;
        return new Rect2(padX, rowTop + i * (rowH + gap), rowW, rowH);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_xfade < 1.0) _xfade = System.Math.Min(1.0, _xfade + delta / XfadeDur);
        if (_autoplay) { Dive(); QueueRedraw(); return; }

        // ポーズメニューを閉じた Esc/Z の同じ押下が漏れて「もどる/決定」が誤発火しないよう食う（Pad.UiBlocked）。
        if (Pad.UiBlocked(this))
        {
            _navHeld = _zHeld = _backHeld = true;
            QueueRedraw();
            return;
        }

        // マウス：フレーム頭でホットスポットをクリア（DiffSelect はポーズ対象外＝唯一の登録者）。
        UiKit.BeginHotspots(Pad.MousePos());
        bool click = Pad.MouseClick();

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

        // マウス：ティア行にホバー＝カーソル移動（選択可の行のみ・表情もクロスフェード）、クリック＝決定。
        for (int i = 0; i < Tiers.Length; i++) UiKit.Hotspot(TierRect(i), i);
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= 0 && hov != _sel && Selectable(hov))
        { _sel = hov; StartFaceSwap(_sel); Audio.Instance?.PlayUiMove(); }
        int clk = UiKit.ClickedId(click);

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        bool confirm = zEdge && _t > 0.2 && Selectable(_sel);
        if (clk >= 0 && _t > 0.2)
        {
            if (Selectable(clk)) { _sel = clk; confirm = true; }
            else Audio.Instance?.PlayUiDeny(); // ロック中ティアをクリック
        }
        if (confirm)
        {
            Audio.Instance?.PlayUiConfirm();
            if (_game != null) _game.Difficulty = Tiers[_sel].Diff; // 難易度はここで確定
            // 潜り方を選んだらそのままダイブ（入口の問いは 2026-09-07 に廃止＝常に「最初から」）。
            Dive();
        }

        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B)
                    || Pad.MouseRightClick(); // 右クリック＝もどる
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2) { Audio.Instance?.PlayUiCancel(); GetTree().ChangeSceneToFile("res://Hub.tscn"); }

        QueueRedraw();
    }

    private void Dive()
    {
        if (_game != null && Selectable(_sel)) _game.Difficulty = Tiers[_sel].Diff;
        // 入口は常に「最初から」（2026-09-07 に入口ダイアログを廃止＝通常プレイで中ボス/ボスから
        // 始める導線は無い）。`--boss` は GameManager が起動時に SelectedEntry を立て、Stage 側は
        // DebugAlwaysBoss を見て毎ランそれを復元するので、ここで潰しても壊れない。
        if (_game != null && !_game.DebugAlwaysBoss) _game.SelectedEntry = GameManager.StageEntry.Start;
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
        UiKit.Text(this, UiKit.Mono, new Vector2(padX, top + 8), _stageTag, UiKit.FontLabel, UiKit.Info);
        float tagW = UiKit.TextW(UiKit.Mono, _stageTag, UiKit.FontLabel);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(padX + tagW + 16, top), $"{_diveName} へ潜る", UiKit.FontTitle, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(padX, top + 4), "深く潜るほど、弾は増え、光は減ります", UiKit.FontBody, UiKit.Text3,
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

        // ── フッタ ──
        float fy = H - 56f;
        DrawRect(new Rect2(padX, fy - 14, W - padX * 2, 1f), new Color(1, 1, 1, 0.08f));
        float fx = padX;
        fx = Hint(fx, fy, "↑↓", "潜り方", false);
        fx = Hint(fx, fy, "Z", "潜る", true);
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
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, y + 22), tr.Name, UiKit.FontHeading, UiKit.Text4);
            // 解禁条件は GameManager の定数から引く（旧実装は 300 とハードコードされており、実際の解禁値 200 と
            //   食い違っていた＝プレイヤーが「まだ100足りない」と誤解する。ショップ側 Shop.cs:1167 は元から定数参照）。
            UiKit.Text(this, UiKit.Zen, new Vector2(tx, y + 54), $"解禁：フォロワー {GameManager.LunaticFollowerReq} または 威力 Lv4", UiKit.FontBody, UiKit.Mina);
            // ロックの段はピル無し（ハブのカードと同じ作法。「LOCKED」の英語ステータスは画面から消す）。
            return;
        }

        // 名前（選択時 ▸ カーソル）
        if (sel)
        {
            UiKit.Text(this, UiKit.Mono, new Vector2(tx, y + 24), "▸", UiKit.FontBody, UiKit.Purify);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tx + 22, y + 20), tr.Name, UiKit.FontHeading, UiKit.White);
            UiKit.Text(this, UiKit.Zen, new Vector2(tx + 22, y + 54), tr.Desc, UiKit.FontBody, new Color(166 / 255f, 196 / 255f, 212 / 255f));
        }
        else
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, y + 20), tr.Name, UiKit.FontHeading, UiKit.White);
            UiKit.Text(this, UiKit.Zen, new Vector2(tx, y + 54), tr.Desc, UiKit.FontBody, UiKit.Text3);
        }

        // 右：弾密度メーター＋報酬倍率
        Color pipCol = luna ? UiKit.Kegare : (tr.Diff == GameManager.Diff.Hard ? new Color("e89460") : UiKit.Purify);
        float pipW = 11f, pipH = 8f, pipGap = 4f;
        float meterW = 5 * pipW + 4 * pipGap;
        float mx = x + w - 24f - meterW, my = y + 28f;
        for (int k = 0; k < 5; k++)
            UiKit.Box(this, new Rect2(mx + k * (pipW + pipGap), my, pipW, pipH), k < tr.Density ? pipCol : new Color(1, 1, 1, 0.12f), 2f);

        // 報酬倍率：「報酬」の文字は消し、♥アイコン＋倍率だけを置く（3-1。ハブの投稿詳細と同じ見せ方）。
        float mul = GameManager.DifficultyImpressionMulFor(tr.Diff);
        string mulS = $"×{mul:0.0}";
        float mulX = x + w - 24f - UiKit.TextW(UiKit.Mono, mulS, UiKit.FontLabel);
        UiKit.Text(this, UiKit.Mono, new Vector2(mulX, y + 50), mulS, UiKit.FontLabel, UiKit.Hp);
        UiKit.Heart(this, new Vector2(mulX - 12f, y + 57f), 6f, UiKit.Hp);

        // 「賭け金」＝残機・ボム初期数（GameManager.BaseLivesFor/BaseBombsFor）。密度メーターだけでは
        // 見えない残機3倍差（Easy6→Lunatic2）を選ぶ前に提示する。恒久強化ボーナスは含めない素の値。
        string stake = $"♥{GameManager.BaseLivesFor(tr.Diff)}  ボム{GameManager.BaseBombsFor(tr.Diff)}";
        UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 24f - UiKit.TextW(UiKit.Mono, stake, UiKit.FontSmall), y + 66), stake, UiKit.FontSmall, UiKit.Text3);
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
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 16 + 30, qy + 7), "ミナ", UiKit.FontLabel, UiKit.Mina);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 16 + 30, qy + 22), quip, UiKit.FontLabel, locked ? UiKit.Text3 : UiKit.Text2,
            HorizontalAlignment.Left, w - 32 - 30 - 14);
    }

    private float Hint(float x, float y, string key, string label, bool accent)
    {
        Color kbg = accent ? new Color(UiKit.Purify, 0.12f) : new Color(1, 1, 1, 0.07f);
        Color kbd = accent ? new Color(UiKit.Info, 0.5f) : new Color(1, 1, 1, 0.16f);
        UiKit.Key(this, new Vector2(x, y - 12), key, kbg, kbd, accent ? UiKit.PurifyHi : UiKit.Text2);
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + kw + 8, y - 8), label, UiKit.FontLabel, accent ? UiKit.Info : UiKit.Text3);
        return x + kw + 8 + UiKit.TextW(UiKit.Zen, label, UiKit.FontLabel) + 24f;
    }
}
