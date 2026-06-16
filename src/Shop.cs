using Godot;

// Shop : ミナ強化ショップ。RefrainHTML/Refrain Screens.dc.html(Screen 3) を忠実移植（非ピクセル）。
//   左：強化リスト（▸/習得ピップ/コスト）＋右：詳細パネル（オーブ/レベル/射撃プレビュー/現在→次/コスト/購入）。
//   インプレ＝金／習得済み＝ミナ紫／選択＝シアン／準備中＝グレー。↑↓ えらぶ・Z 購入・X もどる。
public partial class Shop : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    private static readonly System.Collections.Generic.HashSet<string> Deferred = new()
    { "bomb_power", "option_sub", "contam_resist" };

    private static string Note(string id) => id switch
    {
        "shot_power" => "玉サイズ・輝度 UP", "fire_rate" => "連射数 UP", "move_speed" => "残像 強化",
        "hitbox" => "グレイズ域 拡大", "bomb_count" => "画面浄化", "max_life" => "ハート +1",
        "imp_mult" => "報酬 増加", "fol_gain" => "拡散 UP", "combo_hold" => "倍率 維持", _ => "実装予定",
    };

    private int _sel;
    private bool _navHeld, _zHeld, _backHeld;
    private double _t, _toastT;
    private string _toast = "";
    private Color _toastCol = UiKit.Info;
    private bool _autoplay;

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_toastT > 0) _toastT -= delta;
        if (_autoplay) { GetTree().ChangeSceneToFile("res://Hub.tscn"); return; }

        var defs = GameManager.Upgrades;
        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            if (up) _sel = (_sel - 1 + defs.Length) % defs.Length;
            if (down) _sel = (_sel + 1) % defs.Length;
        }
        _navHeld = up || down;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2) TryBuy(defs[_sel]);

        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2) GetTree().ChangeSceneToFile("res://Hub.tscn");

        QueueRedraw();
    }

    private void TryBuy(GameManager.UpgradeDef d)
    {
        if (Deferred.Contains(d.Id)) { Toast("準備中の強化です", UiKit.Text4); return; }
        int lv = _game?.GetUpgradeLevel(d.Id) ?? 0;
        if (lv >= d.MaxLevel) { Toast("すでに最大です", UiKit.Text4); return; }
        if (!(_game?.CanPurchase(d.Id) ?? false)) { Toast("インプレッションが足りません", new Color("ef9a9a")); return; }
        if (_game!.TryPurchase(d.Id)) Toast($"{d.Name} を強化！  Lv {_game.GetUpgradeLevel(d.Id)}", UiKit.Info);
    }

    private void Toast(string msg, Color col) { _toast = msg; _toastCol = col; _toastT = 1.8; }

    // 効果の文言（GameManager の効果式に準拠）
    private static string Effect(string id, int lv) => id switch
    {
        "shot_power" => $"威力 +{lv}", "fire_rate" => $"間隔 ×{Mathf.Max(0.4f, 1f - 0.08f * lv):0.00}",
        "move_speed" => $"速度 {100 + 12 * lv}%", "hitbox" => $"判定 ×{Mathf.Max(0.4f, 1f - 0.12f * lv):0.00}",
        "bomb_count" => $"所持 +{lv}", "bomb_power" => $"範囲 ×{1f + 0.25f * lv:0.00}", "max_life" => $"体力 {4 + lv}",
        "imp_mult" => $"×{1f + 0.12f * lv:0.00}", "fol_gain" => $"拡散 ×{1f + 0.15f * lv:0.00}",
        "combo_hold" => $"{2.0f + 0.4f * lv:0.0}s", "contam_resist" => $"上昇 ×{Mathf.Max(0f, 1f - 0.15f * lv):0.00}",
        "option_sub" => $"サブ {lv}", _ => $"Lv {lv}",
    };

    public override void _Draw()
    {
        UiKit.BeginDesign(this);

        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0d0b1c"), new Color("0a0916"), new Color("070611") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.78f, H * 0.30f), 460f, UiKit.Mina, 0.16f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        float padX = 40f, top = 32f;
        // ── ヘッダ ──
        Vector2 ac = new(padX + 21, top + 21);
        UiKit.RadialGlow(this, ac, 30f, UiKit.Mina, 0.5f);
        DrawCircle(ac, 21f, UiKit.Mina);
        DrawCircle(ac - new Vector2(4, 5), 7f, new Color(1, 1, 1, 0.9f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(padX + 54, top + 6), "ミナ強化", 26, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(padX + 192, top + 18), "強化は永続。汚染やリトライで失われません。", 13, UiKit.Text3);

        // 通貨ピル（右）
        long imp = _game?.Impression ?? 0;
        string impS = imp.ToString("N0");
        float pillW = 56f + UiKit.TextW(UiKit.Mono, impS, 20);
        float pillX = W - padX - pillW, pillY = top + 4f;
        UiKit.Box(this, new Rect2(pillX, pillY, pillW, 36f), new Color(232 / 255f, 196 / 255f, 90 / 255f, 0.1f), 18f, new Color(UiKit.Gold, 0.4f), 1f);
        DrawCircle(new Vector2(pillX + 18, pillY + 18), 7f, UiKit.Gold);
        UiKit.Text(this, UiKit.Zen, new Vector2(pillX + 30, pillY + 10), "インプレ", 12, new Color("f0d98a"));
        UiKit.Text(this, UiKit.Mono, new Vector2(pillX + pillW - 16 - UiKit.TextW(UiKit.Mono, impS, 20), pillY + 7), impS, 20, new Color("f0d98a"));

        DrawRect(new Rect2(padX, top + 50, W - padX * 2, 1f), new Color(1, 1, 1, 0.1f));

        // ── 本体：左リスト＋右詳細 ──
        float bodyTop = top + 68f, bodyBot = H - 60f, bodyH = bodyBot - bodyTop;
        float avail = W - padX * 2 - 22f;
        float listW = avail * 1.25f / 2.25f, detailW = avail - listW;
        float listX = padX, detailX = padX + listW + 22f;

        DrawList(listX, bodyTop, listW, bodyH);
        DrawDetail(detailX, bodyTop, detailW, bodyH);

        // ── フッタ ──
        float fy = H - 44f;
        float fx = padX;
        fx = Hint(fx, fy, "↑↓", "えらぶ", false);
        fx = Hint(fx, fy, "Z", "購入", true);
        Hint(fx, fy, "X", "もどる", false);

        DrawToast();
        UiKit.EndDesign(this);
    }

    private void DrawList(float x, float y, float w, float hAll)
    {
        var defs = GameManager.Upgrades;
        float rowH = Mathf.Min(42f, (hAll - 4f * (defs.Length - 1)) / defs.Length), gap = 4f;
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            float ry = y + i * (rowH + gap);
            bool sel = i == _sel;
            bool soon = Deferred.Contains(d.Id);
            int lv = _game?.GetUpgradeLevel(d.Id) ?? 0;

            if (sel) UiKit.Box(this, new Rect2(x, ry, w, rowH), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.6f), 11f, new Color(UiKit.Purify, 0.7f), 1.2f);

            float tx = x + 16f;
            if (sel) UiKit.Text(this, UiKit.Mono, new Vector2(tx, ry + rowH / 2f - 8), "▸", 13, UiKit.Purify);
            Color labCol = soon ? UiKit.Text3 : (sel ? UiKit.White : new Color("d8cfe6"));
            UiKit.Text(this, sel ? UiKit.ZenBold : UiKit.Zen, new Vector2(tx + 16, ry + rowH / 2f - 10), d.Name, 17, labCol);

            // 習得ピップ（中央寄り）
            float px = x + w * 0.46f, py = ry + rowH / 2f;
            for (int k = 0; k < d.MaxLevel; k++)
                DrawCircle(new Vector2(px + k * 14f, py), 4.5f, k < lv ? UiKit.Mina : new Color(1, 1, 1, 0.14f));

            // コスト（右）
            string cost; Color costCol;
            if (soon) { cost = "準備中"; costCol = UiKit.Text4; }
            else if (lv >= d.MaxLevel) { cost = "MAX"; costCol = UiKit.Mina; }
            else { cost = (_game?.GetUpgradeCost(d.Id) ?? 0).ToString("N0"); costCol = new Color("f0d98a"); }
            UiKit.Text(this, soon ? UiKit.Zen : UiKit.Mono, new Vector2(x + w - 16 - UiKit.TextW(soon ? UiKit.Zen : UiKit.Mono, cost, 14), ry + rowH / 2f - 9), cost, 14, costCol);
        }
    }

    private void DrawDetail(float x, float y, float w, float h)
    {
        var d = GameManager.Upgrades[_sel];
        bool soon = Deferred.Contains(d.Id);
        int lv = _game?.GetUpgradeLevel(d.Id) ?? 0;
        bool maxed = lv >= d.MaxLevel;

        UiKit.Box(this, new Rect2(x, y, w, h), new Color(20 / 255f, 16 / 255f, 30 / 255f, 0.55f), 16f, new Color(1, 1, 1, 0.1f), 1f);
        float ix = x + 22f, iw = w - 44f;

        // ヘッダ：オーブ＋名前＋説明
        Vector2 oc = new(ix + 28, y + 22 + 28);
        float pulse = 0.85f + 0.15f * Mathf.Sin((float)_t * Mathf.Pi * 2f / 2.8f);
        UiKit.RadialGlow(this, oc, 44f, UiKit.Purify, 0.5f * pulse);
        DrawCircle(oc, 28f, UiKit.Mina);
        DrawCircle(oc - new Vector2(5, 6), 9f, new Color(1, 1, 1, 0.9f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 70, y + 24), d.Name, 23, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(ix + 70, y + 54), soon ? "実装予定の強化です" : d.Desc, 13, UiKit.Info);

        DrawRect(new Rect2(ix, y + 90, iw, 1f), new Color(1, 1, 1, 0.08f));

        // レベル行
        float ly = y + 104f;
        UiKit.Text(this, UiKit.Mono, new Vector2(ix, ly + 2), "LEVEL", 12, UiKit.Text3);
        float lpx = ix + 60f;
        for (int k = 0; k < d.MaxLevel; k++)
        {
            var c = new Vector2(lpx + k * 20f, ly + 8);
            if (k < lv) DrawCircle(c, 6.5f, UiKit.Mina);
            else if (k == lv && !soon) { DrawArc(c, 6.5f, 0, Mathf.Tau, 20, new Color(UiKit.Info, 0.7f), 1.5f); }
            else DrawCircle(c, 6.5f, new Color(1, 1, 1, 0.12f));
        }
        string lvLabel = soon ? "—" : (maxed ? "MAX" : $"Lv.{lv} → Lv.{lv + 1}");
        UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 22 - UiKit.TextW(UiKit.Mono, lvLabel, 13), ly + 2), lvLabel, 13, UiKit.Text3);

        // 射撃プレビュー
        DrawPreview(ix, y + 130, iw, 150f, d, soon, lv);

        // 効果：現在 → 次
        float ey = y + 130 + 150 + 18;
        if (soon)
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ey), "実装予定", 13, UiKit.Text3);
        }
        else
        {
            float bx = ix;
            bx = EffBox(bx, ey, "現在", Effect(d.Id, lv), false);
            UiKit.Text(this, UiKit.Mono, new Vector2(bx + 4, ey + 4), "→", 18, UiKit.Purify);
            bx += 26;
            bx = EffBox(bx, ey, "次", maxed ? "MAX" : Effect(d.Id, lv + 1), true);
            UiKit.Text(this, UiKit.Zen, new Vector2(bx + 6, ey + 8), Note(d.Id), 12, UiKit.Text3);
        }

        // コスト＋購入ボタン（最下部）
        float by = y + h - 56f;
        UiKit.Text(this, UiKit.Zen, new Vector2(ix, by), "必要インプレ", 12, UiKit.Text3);
        string costS = soon ? "準備中" : (maxed ? "—" : (_game?.GetUpgradeCost(d.Id) ?? 0).ToString("N0"));
        DrawCircle(new Vector2(ix + 9, by + 28), 9f, UiKit.Gold);
        UiKit.Text(this, UiKit.Mono, new Vector2(ix + 24, by + 16), costS, 24, new Color("f0d98a"));

        DrawBuyButton(x + w - 22, by + 6, d, soon, maxed, lv);
    }

    private float EffBox(float x, float y, string label, string val, bool next)
    {
        float lw = UiKit.TextW(UiKit.Zen, label, 11), vw = UiKit.TextW(UiKit.Mono, val, 16);
        float bw = 24f + lw + 6 + vw;
        UiKit.Box(this, new Rect2(x, y, bw, 30f), next ? new Color(UiKit.Purify, 0.12f) : new Color(0, 0, 0, 0.24f), 9f,
            next ? new Color(UiKit.Info, 0.4f) : new Color(0, 0, 0, 0), next ? 1f : 0f);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 10, y + 8), label, 11, next ? UiKit.Info : UiKit.Text3);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 10 + lw + 6, y + 6), val, 16, next ? UiKit.PurifyHi : UiKit.Text2);
        return x + bw + 8;
    }

    private void DrawBuyButton(float right, float y, GameManager.UpgradeDef d, bool soon, bool maxed, int lv)
    {
        string label; Color bg, border, txt; bool hasZ = false;
        if (soon) { label = "準備中"; bg = new Color(1, 1, 1, 0.05f); border = new Color(1, 1, 1, 0.1f); txt = UiKit.Text4; }
        else if (maxed) { label = "MAX"; bg = new Color(UiKit.Mina, 0.15f); border = new Color(UiKit.Mina, 0.5f); txt = new Color("c9b6ef"); }
        else if (!(_game?.CanPurchase(d.Id) ?? false)) { label = "インプレ不足"; bg = new Color(232 / 255f, 90 / 255f, 90 / 255f, 0.1f); border = new Color(232 / 255f, 90 / 255f, 90 / 255f, 0.4f); txt = new Color("ef9a9a"); }
        else { label = "購入"; bg = UiKit.Purify; border = new Color(0, 0, 0, 0); txt = UiKit.White; hasZ = true; }

        float pad = 26f, zW = hasZ ? 30f : 0f;
        float bw = pad + zW + UiKit.TextW(UiKit.ZenBold, label, 16) + 20f;
        float bx = right - bw;
        UiKit.Box(this, new Rect2(bx, y, bw, 40f), bg, 20f, border, border.A > 0 ? 1f : 0f);
        float tx = bx + 18;
        if (hasZ)
        {
            UiKit.Box(this, new Rect2(tx, y + 9, 22f, 22f), new Color(1, 1, 1, 0.25f), 6f);
            UiKit.Text(this, UiKit.Mono, new Vector2(tx, y + 11), "Z", 12, UiKit.White, HorizontalAlignment.Center, 22);
            tx += 30;
        }
        UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, y + 11), label, 16, txt);
    }

    // 簡易射撃プレビュー（ミナ＋流れる光弾。素材アニメの“雰囲気”を再現）
    private void DrawPreview(float x, float y, float w, float h, GameManager.UpgradeDef d, bool soon, int lv)
    {
        UiKit.Box(this, new Rect2(x, y, w, h), new Color("090e1c"), 13f, new Color(1, 1, 1, 0.08f), 1f);
        UiKit.RadialGlow(this, new Vector2(x + w * 0.12f, y + h / 2f), w * 0.4f, UiKit.Mina, 0.16f);
        // スキャンライン
        for (float yy = y; yy < y + h; yy += 3f) DrawRect(new Rect2(x, yy, w, 1f), new Color(0, 0, 0, 0.18f));
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 13, y + 10), "射撃プレビュー", 10, UiKit.Text3);

        if (soon)
        {
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, y + h / 2f - 18), "準備中", 22, UiKit.Text4, HorizontalAlignment.Center, w);
            UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h / 2f + 10), "COMING SOON", 11, new Color("4f4a5e"), HorizontalAlignment.Center, w);
            return;
        }

        // 現在/次 Lv バッジ（右上）
        string nb = "現在 Lv." + lv, xb = "次 Lv." + (lv + 1);
        float xbw = UiKit.TextW(UiKit.Mono, xb, 10) + 14;
        UiKit.Box(this, new Rect2(x + w - 14 - xbw, y + 8, xbw, 18f), new Color(UiKit.Purify, 0.16f), 6f, new Color(UiKit.Info, 0.5f), 1f);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 14 - xbw, y + 11), xb, 10, UiKit.PurifyHi, HorizontalAlignment.Center, xbw);
        float nbw = UiKit.TextW(UiKit.Mono, nb, 10) + 14;
        UiKit.Box(this, new Rect2(x + w - 14 - xbw - 6 - nbw, y + 8, nbw, 18f), new Color(1, 1, 1, 0.06f), 6f, new Color(1, 1, 1, 0.12f), 1f);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 14 - xbw - 6 - nbw, y + 11), nb, 10, new Color("8a8398"), HorizontalAlignment.Center, nbw);

        // ミナ＋流れる光弾（2レーン: NOW 暗 / NEXT 明）
        float t = (float)_t;
        Vector2 mina = new(x + 36, y + h / 2f + Mathf.Sin(t * 2.6f) * 6f);
        UiKit.RadialGlow(this, mina, 26f, UiKit.Mina, 0.7f);
        DrawCircle(mina, 13f, UiKit.Mina);
        DrawCircle(mina - new Vector2(2, 3), 4.5f, new Color(1, 1, 1, 0.9f));
        DrawLane(x + 56, x + w - 10, y + h * 0.4f, 5f, new Color("9be0f5"), 1.7f, 4, t, "NOW");
        DrawLane(x + 56, x + w - 10, y + h * 0.72f, 8.5f, UiKit.PurifyHi, 1.45f, 4, t, "NEXT");
    }

    private void DrawLane(float x0, float x1, float y, float r, Color col, float dur, int count, float t, string label)
    {
        UiKit.Text(this, UiKit.Mono, new Vector2(x0 - 42, y - 6), label, 9, label == "NOW" ? new Color("8a8398") : UiKit.Info);
        for (int i = 0; i < count; i++)
        {
            float ph = (t / dur + (float)i / count) % 1f;
            float px = Mathf.Lerp(x0, x1, ph);
            float a = ph < 0.12f ? ph / 0.12f : (ph > 0.86f ? (1f - ph) / 0.14f : 1f);
            UiKit.RadialGlow(this, new Vector2(px, y), r * 2.2f, col, 0.5f * a);
            DrawCircle(new Vector2(px, y), r, new Color(col, a));
        }
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

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        float w = UiKit.TextW(UiKit.ZenBold, _toast, 16) + 48;
        float x = (W - w) / 2f;
        UiKit.Box(this, new Rect2(x, H - 120, w, 40f), new Color(0.06f, 0.05f, 0.10f, 0.96f), 12f, new Color(_toastCol, 0.7f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, H - 110), _toast, 16, _toastCol, HorizontalAlignment.Center, w);
    }
}
