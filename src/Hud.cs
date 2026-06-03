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
    private ColorRect _kindBg = null!;     // やさしさゲージ 背景
    private ColorRect _kindFill = null!;   // やさしさゲージ 中身
    private Label _overloadLabel = null!;  // 「やさしさ全開！」
    private SpeechBubble _bubble = null!;  // 下部の吹き出し（ドット絵調）
    private TextureRect _portrait = null!; // algoの立ち絵（会話時）

    // 吹き出し表示中は敵を止める（他クラスから参照）
    public static bool BubblePaused = false;

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

        // algo 立ち絵（会話時に左下に表示）。元の高解像度イラストを使用。
        var portraitTex = ResourceLoader.Load<Texture2D>("res://char/algo_cutout.png")
                          ?? ResourceLoader.Load<Texture2D>("res://char/algo.png");
        _portrait = new TextureRect
        {
            Name = "Portrait",
            Texture = portraitTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Position = new Vector2(2, Main.ScreenHeight - 62),
            Size = new Vector2(54, 60),
            Visible = false,
        };
        _portrait.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_portrait);

        // 下部の吹き出し（ドット絵調・自前描画）
        _bubble = new SpeechBubble { Name = "Bubble", Visible = false };
        AddChild(_bubble);

        // メッセージ本文（吹き出しの上に乗せる）
        _messageLabel = new Label
        {
            Name = "MessageLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(0, Main.ScreenHeight - 28),
            Size = new Vector2(Main.ScreenWidth, 20),
            ZIndex = 1,
        };
        _messageLabel.AddThemeColorOverride("font_color", new Color(0.18f, 0.12f, 0.22f));
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

        // やさしさゲージ（ハート/ボムの下）
        _kindBg = new ColorRect { Name = "KindBg", Position = new Vector2(5, 28), Size = new Vector2(64, 4), Color = new Color(0.15f, 0.12f, 0.20f, 0.55f) };
        _kindBg.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_kindBg);
        _kindFill = new ColorRect { Name = "KindFill", Position = new Vector2(5, 28), Size = new Vector2(0, 4), Color = new Color("ffd98a") };
        _kindFill.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_kindFill);
        _overloadLabel = new Label { Name = "OverloadLabel", Position = new Vector2(72, 25), Size = new Vector2(140, 12) };
        StyleLabel(_overloadLabel, 8, new Color("ff7fb0"));
        _overloadLabel.Visible = false;
        AddChild(_overloadLabel);

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

            // やさしさゲージ
            float kw = 64f * Mathf.Clamp(game.Kindness, 0f, 1f);
            _kindFill.Size = new Vector2(kw, 4);
            _kindFill.Color = game.IsOverload ? new Color("ff7fb0") : new Color("ffd98a");
            _overloadLabel.Visible = game.IsOverload;
            if (game.IsOverload) _overloadLabel.Text = "やさしさ全開！";
            if (game.JustOverloaded) { ShowBanner("やさしさ全開！"); Flash(); }
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
                _bubble.Visible = false;
                _portrait.Visible = false;
            }
        }
        // 吹き出し表示中は敵を止める
        BubblePaused = _bubble.Visible;

        if (_bannerTimer > 0)
        {
            _bannerTimer -= delta;
            if (_bannerTimer <= 0)
            {
                _bannerLabel.Visible = false;
            }
        }
    }

    // テロップ（吹き出し）でメッセージ表示。立ち絵なし。
    public void ShowMessage(string text)
    {
        _portrait.Visible = false;
        _bubble.Position = new Vector2(18, Main.ScreenHeight - 32);
        _bubble.SetBox(new Vector2(348, 24), 170f);
        _messageLabel.Position = new Vector2(22, Main.ScreenHeight - 31);
        _messageLabel.Size = new Vector2(340, 22);
        _messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _messageLabel.Text = text;
        _bubble.Visible = true;
        _messageLabel.Visible = true;
        _messageTimer = 4.5;
    }

    // algo が話す会話（立ち絵＋吹き出し）。
    public void ShowDialog(string text)
    {
        _portrait.Visible = true;
        _bubble.Position = new Vector2(60, Main.ScreenHeight - 40);
        _bubble.SetBox(new Vector2(316, 32), 8f);
        _messageLabel.Position = new Vector2(66, Main.ScreenHeight - 39);
        _messageLabel.Size = new Vector2(304, 30);
        _messageLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _messageLabel.Text = text;
        _bubble.Visible = true;
        _messageLabel.Visible = true;
        _messageTimer = 6.0;
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
