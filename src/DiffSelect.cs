using Godot;

// DiffSelect : ダイブ直前の難易度選択（ダークモードX風）。ハブ→ここ→ステージ。
//   - やさしい/ふつう/むずかしい/ルナティックの4ティア。難度は「弾の数」で変わる。
//   - ルナティックはメタ強化で解禁（未解禁はロック）。
//   設計: docs/20260613/MINA_システム拡張設計書_v1.md ④-5 / ②-2
public partial class DiffSelect : Node2D
{
    private FontFile _font = null!;
    private GameManager _game = null!;
    private const float W = 384f, H = 216f;

    private const float RowX = 10f, RowW = 364f, FirstRowY = 44f, RowH = 36f, RowGap = 4f;

    private struct Tier { public string Name, Desc; public GameManager.Diff Diff; }
    private static readonly Tier[] Tiers =
    {
        new() { Name = "やさしい",     Desc = "弾は少なく、ゆっくり。物語を追いたい人へ。", Diff = GameManager.Diff.Easy },
        new() { Name = "ふつう",       Desc = "標準的な弾幕。",                             Diff = GameManager.Diff.Normal },
        new() { Name = "むずかしい",   Desc = "弾が増え、密度が上がる。",                   Diff = GameManager.Diff.Hard },
        new() { Name = "ルナティック", Desc = "極限の弾幕。最大強化前提の挑戦。",           Diff = GameManager.Diff.Lunatic },
    };

    private int _sel;
    private bool _navHeld, _zHeld, _backHeld;
    private double _t;
    private bool _autoplay;
    private string _title = "ダイブ";

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        foreach (var s in GameManager.Stages)
            if (s.Scene == _game?.PendingStageScene) { _title = $"{s.Title} へダイブ"; break; }

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
        DrawRect(new Rect2(0, 0, W, H), Ui.Bg);
        DrawRect(new Rect2(0, 0, W, 28), Ui.HeaderBg);
        Ui.Text(this, _font, new Vector2(10, 5), _title, 11, Ui.TextMain);
        Ui.Text(this, _font, new Vector2(10, 17), "難易度で「弾の数」が変わります（インプレ報酬も変動）。", 8, Ui.TextMuted);
        DrawRect(new Rect2(0, 27, W, 1), Ui.Divider);

        for (int i = 0; i < Tiers.Length; i++)
            DrawTier(i, FirstRowY + i * (RowH + RowGap));

        Ui.Text(this, _font, new Vector2(10, H - 14), "↑↓ えらぶ   Z ダイブ   X もどる", 9, Ui.TextMuted);
    }

    private void DrawTier(int i, float ry)
    {
        var tr = Tiers[i];
        bool sel = i == _sel;
        bool luna = tr.Diff == GameManager.Diff.Lunatic;
        bool locked = luna && !(_game?.IsLunaticUnlocked ?? false);
        Color acc = luna ? Ui.Contam : Ui.Blue;

        Color bg = locked ? Ui.CardLocked : (sel ? Ui.CardSel : Ui.Card);
        Color border = sel ? acc : Ui.Border;
        Ui.Box(this, new Rect2(RowX, ry, RowW, RowH), bg, 6f, border, sel ? 1.4f : 0.8f);
        if (sel) DrawRect(new Rect2(RowX, ry + 4, 2.5f, RowH - 8), acc);

        Color nameCol = locked ? Ui.TextMuted : (luna ? Ui.Contam : Ui.TextMain);
        Ui.Text(this, _font, new Vector2(RowX + 12, ry + 5), tr.Name, 11, nameCol);
        if (locked)
        {
            string lk = "🔒 ロック";
            // 鍵は環境フォント非依存にするためテキスト「LOCKED」で代替
            Ui.Text(this, _font, new Vector2(RowX + 12, ry + 19),
                $"解禁: フォロワー {GameManager.LunaticFollowerReq} または 威力Lv4", 8, Ui.Contam);
        }
        else
        {
            Ui.Text(this, _font, new Vector2(RowX + 12, ry + 19), tr.Desc, 8, Ui.TextSub);
            float mul = GameManager.DifficultyImpressionMulFor(tr.Diff);
            string reward = $"報酬 ×{mul:0.0}";
            float rw = Ui.TextW(_font, reward, 9);
            Ui.Text(this, _font, new Vector2(RowX + RowW - rw - 10, ry + 6), reward, 9, luna ? Ui.Contam : Ui.Repost);
        }
    }
}
