using Godot;

// DiffSelect : ダイブ直前の難易度選択画面（ハブ→ここ→ステージ）。STEP4。
//   - やさしい / ふつう / むずかしい / ルナティック の4ティア。
//   - 難度は「弾の数」で変わる。高難度ほどインプレ報酬が増える。
//   - ルナティックはメタ強化で解禁（未解禁はロック表示・選択不可）。
//   設計: docs/20260613/MINA_システム拡張設計書_v1.md ④-5 / ②-2
public partial class DiffSelect : Node2D
{
    private FontFile _font = null!;
    private GameManager _game = null!;

    private const float W = 384f;
    private const float H = 216f;

    private static readonly Color BgCol = new(0.93f, 0.94f, 0.97f);
    private static readonly Color HeaderCol = new(0.88f, 0.90f, 0.96f);
    private static readonly Color SelCol = new(0.16f, 0.50f, 0.95f);
    private static readonly Color SelBg = new(0.80f, 0.88f, 1.0f);
    private static readonly Color TextDark = new(0.15f, 0.13f, 0.20f);
    private static readonly Color TextMuted = new(0.50f, 0.48f, 0.55f);
    private static readonly Color LunaCol = new(0.45f, 0.20f, 0.55f);

    private const float RowX = 12f;
    private const float RowW = 360f;
    private const float FirstRowY = 42f;
    private const float RowH = 34f;

    private struct Tier { public string Name; public string Desc; public GameManager.Diff Diff; }
    private static readonly Tier[] Tiers =
    {
        new() { Name = "やさしい",     Desc = "弾は少なく、ゆっくり。物語を追いたい人へ。", Diff = GameManager.Diff.Easy },
        new() { Name = "ふつう",       Desc = "標準的な弾幕。",                             Diff = GameManager.Diff.Normal },
        new() { Name = "むずかしい",   Desc = "弾が増え、密度が上がる。",                   Diff = GameManager.Diff.Hard },
        new() { Name = "ルナティック", Desc = "極限の弾幕。最大強化前提の挑戦。",           Diff = GameManager.Diff.Lunatic },
    };

    private Label _titleLabel = null!;
    private Label _noteLabel = null!;
    private Label _footerLabel = null!;
    private Label[] _nameLabels = null!;
    private Label[] _descLabels = null!;

    private int _sel;
    private bool _navHeld;
    private bool _zHeld;
    private bool _backHeld;
    private double _t;
    private bool _autoplay;

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");

        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        string title = "ダイブ";
        foreach (var s in GameManager.Stages)
            if (s.Scene == _game?.PendingStageScene) { title = $"{s.Title} へダイブ"; break; }

        _titleLabel = MakeLabel(new Vector2(12, 4), new Vector2(W - 24, 12), TextDark, 12);
        _titleLabel.Text = title;
        _noteLabel = MakeLabel(new Vector2(12, 16), new Vector2(W - 24, 10), TextMuted, 12);
        _noteLabel.Text = "難易度で「弾の数」が変わります（インプレ報酬も変動）。";

        _footerLabel = MakeLabel(new Vector2(12, H - 16), new Vector2(W - 24, 12), TextMuted, 12);
        _footerLabel.Text = "↑↓ えらぶ    Z ダイブ    X もどる";

        _nameLabels = new Label[Tiers.Length];
        _descLabels = new Label[Tiers.Length];
        for (int i = 0; i < Tiers.Length; i++)
        {
            float ry = FirstRowY + i * RowH;
            _nameLabels[i] = MakeLabel(new Vector2(RowX + 6, ry + 3), new Vector2(RowW - 12, 12), TextDark, 12);
            _descLabels[i] = MakeLabel(new Vector2(RowX + 6, ry + 17), new Vector2(RowW - 12, 12), TextMuted, 12);
        }

        // 既定選択：現在の難易度（ロック中ルナならむずかしいへ）。
        _sel = (int)(_game?.Difficulty ?? GameManager.Diff.Normal);
        if (!Selectable(_sel)) _sel = (int)GameManager.Diff.Hard;
    }

    private bool Selectable(int i)
    {
        if (i < 0 || i >= Tiers.Length) return false;
        if (Tiers[i].Diff == GameManager.Diff.Lunatic) return _game?.IsLunaticUnlocked ?? false;
        return true;
    }

    private Label MakeLabel(Vector2 pos, Vector2 size, Color color, int fontSize)
    {
        var l = new Label { Position = pos, Size = size };
        l.AddThemeColorOverride("font_color", color);
        if (_font != null) l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", fontSize);
        AddChild(l);
        return l;
    }

    public override void _Process(double delta)
    {
        _t += delta;

        for (int i = 0; i < Tiers.Length; i++)
        {
            var tr = Tiers[i];
            bool luna = tr.Diff == GameManager.Diff.Lunatic;
            bool locked = luna && !(_game?.IsLunaticUnlocked ?? false);
            float mul = GameManager.DifficultyImpressionMulFor(tr.Diff);
            _nameLabels[i].Text = locked ? $"{tr.Name}   🔒" : tr.Name;
            _nameLabels[i].AddThemeColorOverride("font_color", luna ? LunaCol : TextDark);
            _descLabels[i].Text = locked
                ? $"解禁: フォロワー {GameManager.LunaticFollowerReq} or 威力Lv4"
                : $"{tr.Desc}   報酬 ×{mul:0.0}";
            _descLabels[i].AddThemeColorOverride("font_color", locked ? LunaCol : TextMuted);
        }

        if (_autoplay) { Dive(); QueueRedraw(); return; }

        bool up = Input.IsActionPressed("ui_up");
        bool down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            int n = Tiers.Length;
            if (up) _sel = (_sel - 1 + n) % n;
            if (down) _sel = (_sel + 1) % n;
        }
        _navHeld = up || down;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        if (zEdge && _t > 0.2)
        {
            if (Selectable(_sel)) Dive();
        }

        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld;
        _backHeld = back;
        if (backEdge && _t > 0.2) GetTree().ChangeSceneToFile("res://Hub.tscn");

        QueueRedraw();
    }

    private void Dive()
    {
        if (_game != null && Selectable(_sel))
            _game.Difficulty = Tiers[_sel].Diff;
        string scene = _game?.PendingStageScene ?? "res://Rei.tscn";
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().ChangeSceneToFile(scene);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, W, H), BgCol);
        DrawRect(new Rect2(0, 0, W, 28), HeaderCol);
        DrawRect(new Rect2(0, 27, W, 1), new Color(0.7f, 0.72f, 0.80f));

        for (int i = 0; i < Tiers.Length; i++)
        {
            float ry = FirstRowY + i * RowH;
            bool luna = Tiers[i].Diff == GameManager.Diff.Lunatic;
            bool locked = luna && !(_game?.IsLunaticUnlocked ?? false);
            // 行の下地
            DrawRect(new Rect2(RowX, ry, RowW, RowH - 4), locked ? new Color(0.86f, 0.84f, 0.88f) : new Color(1f, 1f, 1f));
            if (i == _sel)
            {
                DrawRect(new Rect2(RowX, ry, RowW, RowH - 4), SelBg);
                var c = luna ? LunaCol : SelCol;
                float t = 1.5f;
                DrawRect(new Rect2(RowX, ry, RowW, t), c);
                DrawRect(new Rect2(RowX, ry + RowH - 4 - t, RowW, t), c);
                DrawRect(new Rect2(RowX, ry, t, RowH - 4), c);
                DrawRect(new Rect2(RowX + RowW - t, ry, t, RowH - 4), c);
            }
            if (luna)
                DrawRect(new Rect2(RowX, ry, 3, RowH - 4), LunaCol);
        }
    }
}
