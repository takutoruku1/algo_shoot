using Godot;

// DiffSelect : 難易度選択。RefrainHTML/Refrain Screens.dc.html(Screen 2) を忠実移植（非ピクセル）。
//   4ティア＋弾密度メーター。報酬倍率＝緑／選択＝シアン／ルナ解禁＝紫。↑↓ えらぶ・Z ダイブ・X もどる。
public partial class DiffSelect : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    private struct Tier { public string Name, Desc; public GameManager.Diff Diff; public int Density; }
    private static readonly Tier[] Tiers =
    {
        new() { Name = "やさしい",     Desc = "弾は少なく、ゆっくり。物語を追いたい人へ。", Diff = GameManager.Diff.Easy,    Density = 2 },
        new() { Name = "ふつう",       Desc = "標準的な弾幕。",                             Diff = GameManager.Diff.Normal,  Density = 3 },
        new() { Name = "むずかしい",   Desc = "弾が増え、密度が上がる。",                   Diff = GameManager.Diff.Hard,    Density = 4 },
        new() { Name = "ルナティック", Desc = "極限の弾幕。最大強化前提の挑戦。",           Diff = GameManager.Diff.Lunatic, Density = 5 },
    };

    private int _sel;
    private bool _navHeld, _zHeld, _backHeld;
    private double _t;
    private bool _autoplay;
    private string _stageTag = "STAGE 1", _diveName = "レイ";

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        foreach (var s in GameManager.Stages)
            if (s.Scene == _game?.PendingStageScene)
            {
                if (s.Title.Contains("—")) { var p = s.Title.Split('—'); _stageTag = p[0].Trim(); _diveName = p[^1].Trim(); }
                else _diveName = s.Title;
                break;
            }

        _sel = (int)(_game?.Difficulty ?? GameManager.Diff.Normal);
        if (!Selectable(_sel)) _sel = (int)GameManager.Diff.Hard;
    }

    private bool Selectable(int i)
    {
        if (i < 0 || i >= Tiers.Length) return false;
        if (Tiers[i].Diff == GameManager.Diff.Lunatic) return _game?.IsLunaticUnlocked ?? false;
        return true;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_autoplay) { Dive(); QueueRedraw(); return; }

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            int n = Tiers.Length;
            if (up) _sel = (_sel - 1 + n) % n;
            if (down) _sel = (_sel + 1) % n;
        }
        _navHeld = up || down;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2 && Selectable(_sel)) Dive();

        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2) GetTree().ChangeSceneToFile("res://Hub.tscn");

        QueueRedraw();
    }

    private void Dive()
    {
        if (_game != null && Selectable(_sel)) _game.Difficulty = Tiers[_sel].Diff;
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
        UiKit.Text(this, UiKit.Zen, new Vector2(padX, top + 4), "難易度で「弾の数」が変わります（インプレ報酬も変動）", 14, UiKit.Text3,
            HorizontalAlignment.Right, W - padX * 2);
        DrawRect(new Rect2(padX, top + 44, W - padX * 2, 1f), new Color(1, 1, 1, 0.1f));

        // ── ティア行 ──
        float rowTop = top + 66f, rowH = 96f, gap = 13f, rowW = W - padX * 2;
        for (int i = 0; i < Tiers.Length; i++)
            DrawTier(i, padX, rowTop + i * (rowH + gap), rowW, rowH);

        // ── フッタ ──
        float fy = H - 56f;
        DrawRect(new Rect2(padX, fy - 14, W - padX * 2, 1f), new Color(1, 1, 1, 0.08f));
        float fx = padX;
        fx = Hint(fx, fy, "↑↓", "えらぶ", false);
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
