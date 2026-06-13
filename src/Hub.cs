using Godot;

// Hub : タイムラインハブ（ステージ間の中枢）。STEP2 最小版。
//   - ヘッダにミナのアカウント情報（フォロワー / インプレ / 汚染）。
//   - 流れる投稿カード（NEW / CLEAR / LOCK）を ↑↓ で選び Z でダイブ。
//   - 3ステージ全クリアで FINAL カードが出現。
//   - autoplay(--demo/--qa)中は入力待ちで止まらないよう、自動で次へダイブする。
//   設計: docs/20260613/MINA_システム拡張設計書_v1.md ④-3 / ④-2
public partial class Hub : Node2D
{
    private FontFile _font = null!;
    private GameManager _game = null!;

    // 画面（pixel art 384x216）
    private const float W = 384f;
    private const float H = 216f;

    // 配色
    private static readonly Color BgCol = new(0.93f, 0.94f, 0.97f);
    private static readonly Color HeaderCol = new(0.88f, 0.90f, 0.96f);
    private static readonly Color CardCol = new(1f, 1f, 1f);
    private static readonly Color CardClearedCol = new(0.90f, 0.93f, 0.97f);
    private static readonly Color CardLockCol = new(0.82f, 0.82f, 0.86f);
    private static readonly Color SelCol = new(0.16f, 0.50f, 0.95f);   // X ブルー
    private static readonly Color TextDark = new(0.15f, 0.13f, 0.20f);
    private static readonly Color TextMuted = new(0.45f, 0.43f, 0.50f);
    private static readonly Color ContamCol = new(0.45f, 0.20f, 0.55f);

    // レイアウト
    private const float CardX = 10f;
    private const float CardW = 364f;
    private const float CardH = 40f;
    private const float CardGap = 6f;
    private const float FirstCardY = 30f;

    // エントリ（投稿カード）
    private struct Entry
    {
        public bool IsFinal;
        public string Id;     // stage id（final は空）
        public string Scene;
        public string Tag;
        public string Handle;
        public string Tweet;
        public bool Unlocked;
        public bool Cleared;
    }
    private Entry[] _entries = System.Array.Empty<Entry>();
    private Label[] _tagLabels = System.Array.Empty<Label>();
    private Label[] _handleLabels = System.Array.Empty<Label>();
    private Label[] _tweetLabels = System.Array.Empty<Label>();

    private Label _nameLabel = null!;
    private Label _statLabel = null!;
    private Label _contamLabel = null!;
    private Label _footerLabel = null!;
    private Label _toastLabel = null!;

    private int _sel;
    private bool _navHeld;
    private bool _zHeld;
    private bool _xHeld;
    private bool _dived;
    private double _t;

    private bool _autoplay;
    private const double AutoDiveDelay = 1.1; // autoplay時の自動ダイブまで

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");

        var user = OS.GetCmdlineUserArgs();
        foreach (var a in user)
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        BuildEntries();
        BuildLabels();

        // 既定カーソル：次の未クリア（無ければ FINAL / 末尾）。
        _sel = DefaultSelection();
    }

    private void BuildEntries()
    {
        var list = new System.Collections.Generic.List<Entry>();
        foreach (var s in GameManager.Stages)
        {
            bool cleared = _game?.IsStageCleared(s.Id) ?? false;
            bool unlocked = _game?.IsStageUnlocked(s.Id) ?? true;
            list.Add(new Entry
            {
                IsFinal = false,
                Id = s.Id,
                Scene = s.Scene,
                Tag = cleared ? "CLEAR" : (unlocked ? "NEW" : "LOCK"),
                Handle = s.Handle,
                Tweet = s.Tweet,
                Unlocked = unlocked,
                Cleared = cleared,
            });
        }
        if (_game?.AllStoryCleared ?? false)
        {
            list.Add(new Entry
            {
                IsFinal = true,
                Id = "",
                Scene = "res://Final.tscn",
                Tag = "FINAL",
                Handle = "@mina_ai_",
                Tweet = "——汚染が、限界へ。ミナ自身の内側へダイブする。",
                Unlocked = true,
                Cleared = false,
            });
        }
        _entries = list.ToArray();
    }

    private int DefaultSelection()
    {
        string? next = _game?.NextUnclearedStageId();
        if (next != null)
        {
            for (int i = 0; i < _entries.Length; i++)
                if (!_entries[i].IsFinal && _entries[i].Id == next) return i;
        }
        return _entries.Length - 1; // FINAL or 末尾
    }

    private void BuildLabels()
    {
        _nameLabel = MakeLabel(new Vector2(6, 3), new Vector2(220, 12), TextDark, 12);
        _nameLabel.Text = "ミナ  @mina_ai_";

        _statLabel = MakeLabel(new Vector2(150, 3), new Vector2(228, 12), TextDark, 12);
        _statLabel.HorizontalAlignment = HorizontalAlignment.Right;

        _contamLabel = MakeLabel(new Vector2(150, 13), new Vector2(228, 9), ContamCol, 12);
        _contamLabel.HorizontalAlignment = HorizontalAlignment.Right;

        _footerLabel = MakeLabel(new Vector2(6, H - 16), new Vector2(W - 12, 12), TextMuted, 12);
        _footerLabel.Text = "↑↓ えらぶ    Z ダイブ    X 強化";

        _toastLabel = MakeLabel(new Vector2(6, H - 30), new Vector2(W - 12, 12), new Color(0.10f, 0.45f, 0.20f), 12);
        _toastLabel.Visible = false;

        int n = _entries.Length;
        _tagLabels = new Label[n];
        _handleLabels = new Label[n];
        _tweetLabels = new Label[n];
        for (int i = 0; i < n; i++)
        {
            float cy = FirstCardY + i * (CardH + CardGap);
            _tagLabels[i] = MakeLabel(new Vector2(CardX + 6, cy + 4), new Vector2(70, 10), SelCol, 12);
            _handleLabels[i] = MakeLabel(new Vector2(CardX + 78, cy + 4), new Vector2(280, 10), TextMuted, 12);
            _tweetLabels[i] = MakeLabel(new Vector2(CardX + 6, cy + 18), new Vector2(CardW - 12, 20), TextDark, 12);
            _tweetLabels[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;

            var e = _entries[i];
            _tagLabels[i].Text = e.Tag;
            _tagLabels[i].AddThemeColorOverride("font_color", e.IsFinal ? ContamCol : (e.Unlocked ? SelCol : TextMuted));
            _handleLabels[i].Text = e.Handle;
            _tweetLabels[i].Text = e.Tweet;
            if (!e.Unlocked)
            {
                _tweetLabels[i].Text = "（まだダイブできません）";
                _tweetLabels[i].AddThemeColorOverride("font_color", TextMuted);
            }
        }
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

        // ヘッダ更新
        long imp = _game?.Impression ?? 0;
        int fol = _game?.Followers ?? 0;
        float contam = _game?.Contamination ?? 0f;
        _statLabel.Text = $"フォロワー {fol}    Imp {imp}";
        _contamLabel.Text = $"汚染 {Mathf.RoundToInt(contam * 100f)}%";

        // クリア帰還トースト（直近クリア報酬を数秒だけ表示）
        if (!_autoplay && (_game?.LastClearImpression ?? 0) > 0 && _t < 3.0)
        {
            _toastLabel.Visible = true;
            _toastLabel.Text = $"獲得！  Imp +{_game!.LastClearImpression}    フォロワー +{_game.LastClearFollowers}";
        }
        else _toastLabel.Visible = false;

        if (_dived) { QueueRedraw(); return; }

        // autoplay：入力待ちにならないよう自動ダイブ
        if (_autoplay)
        {
            if (_t >= AutoDiveDelay) DiveAuto();
            QueueRedraw();
            return;
        }

        // 入力
        if (Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start))
        {
            GetTree().ReloadCurrentScene();
            return;
        }

        bool up = Input.IsActionPressed("ui_up");
        bool down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld && _entries.Length > 0)
        {
            if (up) _sel = (_sel - 1 + _entries.Length) % _entries.Length;
            if (down) _sel = (_sel + 1) % _entries.Length;
        }
        _navHeld = up || down;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        if (zEdge && _t > 0.3 && _sel >= 0 && _sel < _entries.Length)
        {
            var e = _entries[_sel];
            if (e.Unlocked) Dive(e.Scene);
        }

        // X：強化ショップへ
        bool x = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.X);
        bool xEdge = x && !_xHeld;
        _xHeld = x;
        if (xEdge && _t > 0.3 && !_dived)
        {
            _dived = true;
            GetTree().ChangeSceneToFile("res://Shop.tscn");
        }

        QueueRedraw();
    }

    private void DiveAuto()
    {
        // 次の未クリア → そのステージ。全クリア → FINAL。
        string? next = _game?.NextUnclearedStageId();
        if (next != null)
        {
            foreach (var s in GameManager.Stages)
                if (s.Id == next) { Dive(s.Scene); return; }
        }
        Dive("res://Final.tscn");
    }

    private void Dive(string scene)
    {
        if (_dived) return;
        _dived = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().ChangeSceneToFile(scene);
    }

    public override void _Draw()
    {
        // 画面 / ヘッダ
        DrawRect(new Rect2(0, 0, W, H), BgCol);
        DrawRect(new Rect2(0, 0, W, 24), HeaderCol);
        DrawRect(new Rect2(0, 23, W, 1), new Color(0.7f, 0.72f, 0.80f));

        // 汚染バー（ヘッダ最下部の細線）
        float contam = _game?.Contamination ?? 0f;
        DrawRect(new Rect2(0, 24, W * Mathf.Clamp(contam, 0f, 1f), 2), ContamCol);

        // カード
        for (int i = 0; i < _entries.Length; i++)
        {
            var e = _entries[i];
            float cy = FirstCardY + i * (CardH + CardGap);
            var bg = e.Cleared ? CardClearedCol : (e.Unlocked ? CardCol : CardLockCol);
            DrawRect(new Rect2(CardX, cy, CardW, CardH), bg);
            // 選択枠
            if (i == _sel)
            {
                float t = 1.5f;
                var c = SelCol;
                DrawRect(new Rect2(CardX, cy, CardW, t), c);
                DrawRect(new Rect2(CardX, cy + CardH - t, CardW, t), c);
                DrawRect(new Rect2(CardX, cy, t, CardH), c);
                DrawRect(new Rect2(CardX + CardW - t, cy, t, CardH), c);
            }
            // クリア済の小マーク（左帯）
            if (e.Cleared)
                DrawRect(new Rect2(CardX, cy, 3, CardH), new Color(0.20f, 0.65f, 0.40f));
            else if (e.IsFinal)
                DrawRect(new Rect2(CardX, cy, 3, CardH), ContamCol);
        }
    }
}
