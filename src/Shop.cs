using Godot;

// Shop : 恒久強化ショップ（ダークモードX風）。ハブから X で開く。
//   - インプレッションで強化を購入。強化は永続（被弾・リトライ・汚染で溶けない・§0-3）。
//   - ↑↓ 選択、Z 購入、X/Esc でハブへ。効果未配線の項目は「準備中」。
//   設計: docs/20260613/MINA_システム拡張設計書_v1.md ④-4
public partial class Shop : Node2D
{
    private FontFile _font = null!;
    private GameManager _game = null!;
    private const float W = 384f, H = 216f;

    private static readonly System.Collections.Generic.HashSet<string> Deferred = new()
    { "bomb_power", "option_sub", "contam_resist" };

    private const float FirstRowY = 40f, RowH = 12.5f;

    private int _sel;
    private bool _navHeld, _zHeld, _backHeld;
    private double _t, _toastT;
    private string _toast = "";
    private Color _toastCol = Ui.Ok;
    private bool _autoplay;

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");
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
        if (Deferred.Contains(d.Id)) { Toast("準備中の強化です", Ui.TextMuted); return; }
        int lv = _game?.GetUpgradeLevel(d.Id) ?? 0;
        if (lv >= d.MaxLevel) { Toast("すでに最大です", Ui.TextMuted); return; }
        if (!(_game?.CanPurchase(d.Id) ?? false)) { Toast("インプレッションが足りません", Ui.Burn); return; }
        if (_game!.TryPurchase(d.Id))
            Toast($"{d.Name} を強化！  Lv {_game.GetUpgradeLevel(d.Id)}", Ui.Ok);
    }

    private void Toast(string msg, Color col) { _toast = msg; _toastCol = col; _toastT = 1.8; }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, W, H), Ui.Bg);

        // ヘッダ
        DrawRect(new Rect2(0, 0, W, 26), Ui.HeaderBg);
        Ui.Avatar(this, _font, new Vector2(14, 13), 8f, Ui.Mina, "ミ");
        Ui.Text(this, _font, new Vector2(27, 7), "ミナ強化", 11, Ui.TextMain);
        long imp = _game?.Impression ?? 0;
        string impS = Ui.Abbrev(imp);
        float impW = 12f + Ui.TextW(_font, impS, 10);
        DrawCircle(new Vector2(W - 8 - impW + 3, 12), 2.8f, new Color(0.98f, 0.78f, 0.30f));
        Ui.Text(this, _font, new Vector2(W - 8 - impW + 9, 6), impS, 10, Ui.TextMain);
        DrawRect(new Rect2(0, 25, W, 1), Ui.Divider);
        Ui.Text(this, _font, new Vector2(8, 28), "強化は永続。汚染やリトライで失われません。", 8, Ui.TextMuted);

        var defs = GameManager.Upgrades;
        for (int i = 0; i < defs.Length; i++)
            DrawRow(defs[i], i, FirstRowY + i * RowH);

        // フッタ（選択中の説明＋操作）
        var sd = defs[Mathf.Clamp(_sel, 0, defs.Length - 1)];
        Ui.Text(this, _font, new Vector2(8, 194), sd.Desc, 9, Ui.TextSub);
        Ui.Text(this, _font, new Vector2(8, 204), "↑↓ えらぶ   Z 購入   X もどる", 9, Ui.TextMuted);

        if (_toastT > 0)
        {
            float w = Ui.TextW(_font, _toast, 9) + 16;
            float x = (W - w) / 2;
            Ui.Box(this, new Rect2(x, 176, w, 14), new Color(0.10f, 0.12f, 0.15f, 0.96f), 7f, new Color(_toastCol, 0.8f), 1f);
            Ui.Text(this, _font, new Vector2(x + 8, 178), _toast, 9, _toastCol);
        }
    }

    private void DrawRow(GameManager.UpgradeDef d, int i, float ry)
    {
        bool sel = i == _sel;
        int lv = _game?.GetUpgradeLevel(d.Id) ?? 0;
        bool deferred = Deferred.Contains(d.Id);
        bool maxed = lv >= d.MaxLevel;
        bool affordable = !deferred && !maxed && (_game?.CanPurchase(d.Id) ?? false);

        if (sel)
        {
            Ui.Box(this, new Rect2(6, ry - 0.5f, W - 12, RowH), Ui.CardSel, 4f, new Color(Ui.Blue, 0.9f), 1f);
            DrawRect(new Rect2(6, ry + 1.5f, 2f, RowH - 4), Ui.Blue);
        }

        Color nameCol = deferred ? Ui.TextMuted : Ui.TextMain;
        if (!deferred && !maxed && !affordable) nameCol = Ui.TextSub;
        Ui.Text(this, _font, new Vector2(12, ry), d.Name, 9, nameCol);

        // レベルピップ（●現在 / ○残り）
        float px = 96;
        for (int k = 0; k < d.MaxLevel; k++)
        {
            var c = new Vector2(px + k * 5.5f, ry + 5.5f);
            if (k < lv) DrawCircle(c, 2f, deferred ? Ui.TextMuted : Ui.Mina);
            else { DrawCircle(c, 2f, Ui.Border); DrawCircle(c, 1.1f, Ui.Card); }
        }

        // 右：状態チップ
        string state; Color scol;
        if (deferred) { state = "準備中"; scol = Ui.TextMuted; }
        else if (maxed) { state = "MAX"; scol = Ui.Repost; }
        else { state = "Imp " + (_game?.GetUpgradeCost(d.Id) ?? 0); scol = affordable ? Ui.Blue : Ui.TextMuted; }
        float sw = Ui.TextW(_font, state, 9);
        Ui.Text(this, _font, new Vector2(W - 12 - sw, ry), state, 9, scol);
    }
}
