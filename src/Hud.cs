using Godot;

// Hud : CanvasLayer
// 画面下中央のチュートリアル指示、中央大きめの一時バナー、残機表示を Label で行う。
// 日本語フォント未設定でも読めるよう英語併記の簡潔文言を使う。
public partial class Hud : CanvasLayer
{
    private Label _messageLabel = null!;   // 画面下中央のチュートリアル指示
    private Label _bannerLabel = null!;    // 中央大きめの一時バナー
    private HeartsBar _hearts = null!;     // 残機表示（ドットハート）
    private Label _scoreLabel = null!;     // スコア（右上）
    private Label _comboLabel = null!;     // コンボ（右上・スコア下）
    private Label _bombLabel = null!;      // ボム数（ハート下）
    private ColorRect _flash = null!;      // ボム発動時の全画面フラッシュ

    private double _messageTimer;          // メッセージの自動消去残り時間
    private double _bannerTimer;           // バナーの自動消去残り時間
    private float _flashAlpha;             // フラッシュの現在アルファ

    private FontFile _font = null!;

    public override void _Ready()
    {
        AddToGroup("hud"); // Player のボム発動からフラッシュを呼べるように

        // ピクセルフォント（PixelMplus）。ドット感を保つため AA/サブピクセル/ヒンティングを無効化。
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");
        if (_font != null)
        {
            _font.Antialiasing = TextServer.FontAntialiasing.None;
            _font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
            _font.Hinting = TextServer.Hinting.None;
            _font.ForceAutohinter = false;
            _font.MultichannelSignedDistanceField = false;
        }

        // メッセージ: 画面下中央
        _messageLabel = new Label
        {
            Name = "MessageLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // 画面下部に横いっぱいの帯として配置（内部解像度 384x216 前提）
            Position = new Vector2(0, Main.ScreenHeight - 28),
            Size = new Vector2(Main.ScreenWidth, 20),
        };
        _messageLabel.AddThemeColorOverride("font_color", new Color(0.15f, 0.10f, 0.05f));
        _messageLabel.AddThemeColorOverride("font_outline_color", new Color(1f, 1f, 1f, 0.7f));
        _messageLabel.AddThemeConstantOverride("outline_size", 2);
        if (_font != null) _messageLabel.AddThemeFontOverride("font", _font);
        _messageLabel.AddThemeFontSizeOverride("font_size", 12);
        _messageLabel.Visible = false;
        AddChild(_messageLabel);

        // バナー: 中央大きめ
        _bannerLabel = new Label
        {
            Name = "BannerLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(0, Main.ScreenHeight / 2 - 16),
            Size = new Vector2(Main.ScreenWidth, 32),
        };
        _bannerLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.15f, 0.45f)); // 紫系
        _bannerLabel.AddThemeColorOverride("font_outline_color", new Color(1f, 1f, 1f, 0.9f));
        _bannerLabel.AddThemeConstantOverride("outline_size", 3);
        if (_font != null) _bannerLabel.AddThemeFontOverride("font", _font);
        _bannerLabel.AddThemeFontSizeOverride("font_size", 24); // native 12 の2倍でクリスプ
        _bannerLabel.Visible = false;
        AddChild(_bannerLabel);

        // 残機: 左上（ドットハート）
        _hearts = new HeartsBar { Name = "Hearts", Position = new Vector2(5, 5) };
        AddChild(_hearts);

        // ボム数: ハートの下
        _bombLabel = new Label
        {
            Name = "BombLabel",
            Position = new Vector2(5, 16),
            Size = new Vector2(80, 12),
        };
        StyleLabel(_bombLabel, 8, new Color(0.20f, 0.12f, 0.05f));
        AddChild(_bombLabel);

        // スコア: 右上（右寄せ）
        _scoreLabel = new Label
        {
            Name = "ScoreLabel",
            HorizontalAlignment = HorizontalAlignment.Right,
            Position = new Vector2(Main.ScreenWidth - 128, 4),
            Size = new Vector2(124, 12),
        };
        StyleLabel(_scoreLabel, 10, new Color(0.20f, 0.12f, 0.05f));
        AddChild(_scoreLabel);

        // コンボ: スコアの下（右寄せ・x2以上で表示）
        _comboLabel = new Label
        {
            Name = "ComboLabel",
            HorizontalAlignment = HorizontalAlignment.Right,
            Position = new Vector2(Main.ScreenWidth - 128, 16),
            Size = new Vector2(124, 12),
        };
        StyleLabel(_comboLabel, 8, new Color(0.45f, 0.20f, 0.55f)); // 紫
        _comboLabel.Visible = false;
        AddChild(_comboLabel);

        // ボム発動フラッシュ（全画面・最前面・初期は透明）
        _flash = new ColorRect
        {
            Name = "BombFlash",
            Color = new Color(1f, 1f, 1f, 0f),
            Position = Vector2.Zero,
            Size = new Vector2(Main.ScreenWidth, Main.ScreenHeight),
            ZIndex = 100,
        };
        _flash.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_flash);
    }

    // ラベルにピクセルフォント・サイズ・白アウトラインを適用するヘルパ。
    private void StyleLabel(Label l, int size, Color color)
    {
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(1f, 1f, 1f, 0.85f));
        l.AddThemeConstantOverride("outline_size", 2);
        if (_font != null) l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", size);
    }

    public override void _Process(double delta)
    {
        // スコア・コンボ・ボム表示の更新
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game != null)
        {
            _scoreLabel.Text = $"SCORE {game.Score:N0}";
            _bombLabel.Text = $"BOMB x{game.Bombs}";
            if (game.Combo >= 2)
            {
                _comboLabel.Text = $"やさしさが {game.Combo}人にひろがった！";
                _comboLabel.Visible = true;
            }
            else
            {
                _comboLabel.Visible = false;
            }
        }

        // ボムフラッシュの減衰
        if (_flashAlpha > 0f)
        {
            _flashAlpha = Mathf.Max(0f, _flashAlpha - (float)delta * 2.2f);
            _flash.Color = new Color(1f, 1f, 1f, _flashAlpha);
        }

        if (_messageTimer > 0)
        {
            _messageTimer -= delta;
            if (_messageTimer <= 0)
            {
                _messageLabel.Visible = false;
            }
        }

        if (_bannerTimer > 0)
        {
            _bannerTimer -= delta;
            if (_bannerTimer <= 0)
            {
                _bannerLabel.Visible = false;
            }
        }
    }

    // 画面下中央のチュートリアル指示（数秒で消す/上書き）。
    public void ShowMessage(string text)
    {
        _messageLabel.Text = text;
        _messageLabel.Visible = true;
        _messageTimer = 4.0; // 数秒で消える（後続 ShowMessage で上書き）
    }

    // 中央大きめの一時バナー（例: "STAGE CLEAR!"）。
    public void ShowBanner(string text)
    {
        _bannerLabel.Text = text;
        _bannerLabel.Visible = true;
        _bannerTimer = 5.0;
    }

    // 残機表示の更新（ドットハート）。
    public void SetLives(int n)
    {
        _hearts.SetCount(n);
    }

    // ボム発動時の全画面フラッシュ。
    public void Flash()
    {
        _flashAlpha = 0.55f;
        _flash.Color = new Color(1f, 1f, 1f, _flashAlpha);
    }
}
