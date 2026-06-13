using Godot;

// Shop : 恒久強化ショップ（タイムラインハブから開く）。STEP3。
//   - インプレッションを消費して強化を購入。強化は永続（被弾・リトライ・汚染で溶けない・§0-3）。
//   - ↑↓ で選択、Z で購入、X/Esc でハブへ戻る。
//   - 効果が未配線の項目（ボム威力=既に全画面 / 拡散サブ / 汚染耐性）は「準備中」表示。
//   設計: docs/20260613/MINA_システム拡張設計書_v1.md ④-4
public partial class Shop : Node2D
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
    private static readonly Color OkCol = new(0.10f, 0.45f, 0.20f);
    private static readonly Color NgCol = new(0.65f, 0.20f, 0.25f);

    private const float RowX = 8f;
    private const float RowW = 368f;
    private const float FirstRowY = 30f;
    private const float RowH = 13.5f;

    // 効果が未配線の項目（購入不可・準備中表示）。
    private static readonly System.Collections.Generic.HashSet<string> Deferred = new()
    {
        "bomb_power",   // ボムは既に全画面消去のため範囲強化が無意味
        "option_sub",   // 追従オプションの恒久付与は未配線
        "contam_resist" // 汚染は各ステージで絶対値設定のため未配線
    };

    private Label _titleLabel = null!;
    private Label _impLabel = null!;
    private Label _noteLabel = null!;
    private Label _footerLabel = null!;
    private Label _toastLabel = null!;
    private Label[] _rows = System.Array.Empty<Label>();

    private int _sel;
    private bool _navHeld;
    private bool _zHeld;
    private bool _backHeld;
    private double _t;
    private double _toastT;
    private string _toast = "";
    private Color _toastCol = OkCol;
    private bool _autoplay;

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");

        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        _titleLabel = MakeLabel(new Vector2(8, 3), new Vector2(220, 12), TextDark, 12);
        _titleLabel.Text = "ミナ強化";
        _impLabel = MakeLabel(new Vector2(160, 3), new Vector2(216, 12), TextDark, 12);
        _impLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _noteLabel = MakeLabel(new Vector2(8, 15), new Vector2(W - 16, 10), TextMuted, 12);
        _noteLabel.Text = "強化は永続。汚染やリトライで失われません。";

        _footerLabel = MakeLabel(new Vector2(8, H - 26), new Vector2(W - 16, 22), TextMuted, 12);
        _footerLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _toastLabel = MakeLabel(new Vector2(8, H - 40), new Vector2(W - 16, 12), OkCol, 12);
        _toastLabel.Visible = false;

        var defs = GameManager.Upgrades;
        _rows = new Label[defs.Length];
        for (int i = 0; i < defs.Length; i++)
            _rows[i] = MakeLabel(new Vector2(RowX + 4, FirstRowY + i * RowH + 1), new Vector2(RowW - 8, 12), TextDark, 12);
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
        _impLabel.Text = $"Imp 所持: {_game?.Impression ?? 0}";

        var defs = GameManager.Upgrades;
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            int lv = _game?.GetUpgradeLevel(d.Id) ?? 0;
            string state;
            if (Deferred.Contains(d.Id)) state = "準備中";
            else if (lv >= d.MaxLevel) state = "MAX";
            else state = $"Imp {_game?.GetUpgradeCost(d.Id) ?? 0}";
            _rows[i].Text = $"{d.Name}   Lv {lv}/{d.MaxLevel}   {state}";

            Color c = TextDark;
            if (Deferred.Contains(d.Id)) c = TextMuted;
            else if (lv >= d.MaxLevel) c = OkCol;
            else if (!(_game?.CanPurchase(d.Id) ?? false)) c = TextMuted; // 買えない（資金不足）
            _rows[i].AddThemeColorOverride("font_color", c);
        }

        // 選択項目の説明
        var sd = defs[Mathf.Clamp(_sel, 0, defs.Length - 1)];
        _footerLabel.Text = $"{sd.Desc}\nZ 購入    X もどる";

        if (_toastT > 0)
        {
            _toastT -= delta;
            _toastLabel.Visible = true;
            _toastLabel.Text = _toast;
            _toastLabel.AddThemeColorOverride("font_color", _toastCol);
        }
        else _toastLabel.Visible = false;

        // autoplay では即ハブへ戻る（パイロットがショップに留まらないよう防御）
        if (_autoplay) { Back(); QueueRedraw(); return; }

        // ナビ
        bool up = Input.IsActionPressed("ui_up");
        bool down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld && defs.Length > 0)
        {
            if (up) _sel = (_sel - 1 + defs.Length) % defs.Length;
            if (down) _sel = (_sel + 1) % defs.Length;
        }
        _navHeld = up || down;

        // 購入
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        if (zEdge && _t > 0.2) TryBuy(sd);

        // 戻る
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld;
        _backHeld = back;
        if (backEdge && _t > 0.2) Back();

        QueueRedraw();
    }

    private void TryBuy(GameManager.UpgradeDef d)
    {
        if (Deferred.Contains(d.Id)) { Toast("準備中の強化です", NgCol); return; }
        int lv = _game?.GetUpgradeLevel(d.Id) ?? 0;
        if (lv >= d.MaxLevel) { Toast("すでに最大です", TextMuted); return; }
        if (!(_game?.CanPurchase(d.Id) ?? false)) { Toast("インプレッションが足りません", NgCol); return; }
        if (_game!.TryPurchase(d.Id))
            Toast($"{d.Name} を強化しました！  Lv {_game.GetUpgradeLevel(d.Id)}", OkCol);
    }

    private void Toast(string msg, Color col)
    {
        _toast = msg; _toastCol = col; _toastT = 1.8;
    }

    private void Back()
    {
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, W, H), BgCol);
        DrawRect(new Rect2(0, 0, W, 26), HeaderCol);
        DrawRect(new Rect2(0, 25, W, 1), new Color(0.7f, 0.72f, 0.80f));

        var defs = GameManager.Upgrades;
        for (int i = 0; i < defs.Length; i++)
        {
            float ry = FirstRowY + i * RowH;
            if (i == _sel)
            {
                DrawRect(new Rect2(RowX, ry, RowW, RowH), SelBg);
                DrawRect(new Rect2(RowX, ry, 2f, RowH), SelCol);
            }
        }
    }
}
