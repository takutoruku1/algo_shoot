using Godot;

// Hud : CanvasLayer
// 画面下中央のチュートリアル指示、中央大きめの一時バナー、残機表示を Label で行う。
// 日本語フォント未設定でも読めるよう英語併記の簡潔文言を使う。
public partial class Hud : CanvasLayer
{
    private Label _messageLabel = null!;   // 画面下中央のチュートリアル指示
    private Label _speakerLabel = null!;   // 吹き出し上の話者名（種類で色分け）
    private Label _bannerLabel = null!;    // 中央大きめの一時バナー
    private HeartsBar _hearts = null!;     // 残機表示（ドットハート）
    private Label _scoreLabel = null!;     // スコア（右上）
    private Label _comboLabel = null!;     // コンボ（右上・スコア下）
    private Label _burnLabel = null!;      // 炎上中の弱体表示
    private ColorRect[] _bombPips = System.Array.Empty<ColorRect>(); // ボム数（紫の四角ピップ）
    private ColorRect _flash = null!;      // ボム発動時の全画面フラッシュ
    private ColorRect _kindBg = null!;     // やさしさゲージ 背景
    private ColorRect _kindFill = null!;   // やさしさゲージ 中身
    private Label _overloadLabel = null!;  // 「やさしさ全開！」
    private Label _skillLabel = null!;     // ヒカゲ専用スキルの表示（C）
    private ColorRect _purifyBg = null!;   // 浄化ゲージ 背景（上部中央・ステージ目標）
    private ColorRect _purifyFill = null!; // 浄化ゲージ 中身
    private Label _purifyLabel = null!;    // 「浄化 XX%」
    private const float PurifyGaugeX = 100f;
    private const float PurifyGaugeW = 150f;
    private ColorRect _bossBg = null!;      // ボスHPバー 背景
    private ColorRect _bossFill = null!;    // ボスHPバー 中身
    private Label _bossLabel = null!;       // ボス名
    private const float BossBarX = 92f;
    private const float BossBarY = 32f;
    private const float BossBarW = 200f;
    private SpeechBubble _bubble = null!;  // 下部の吹き出し（ドット絵調）
    private TextureRect _portrait = null!; // algoの立ち絵（会話時）

    // 吹き出し表示中は敵を止める（他クラスから参照）
    public static bool BubblePaused = false;
    // true の間は吹き出しを自動で消さない（手動送り用）。送り側が HideBubble で閉じる。
    public bool HoldBubble = false;

    private double _messageTimer;          // メッセージの自動消去残り時間
    private double _bannerTimer;           // バナーの自動消去残り時間
    private float _flashAlpha;             // フラッシュの現在アルファ
    private Color _flashRgb = new Color(1f, 1f, 1f); // フラッシュ色（白=ボム/赤=被弾）

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

        // 話者名（吹き出しの左上に乗せる。種類ごとに色分けして「誰の何か」を明示）
        _speakerLabel = new Label
        {
            Name = "SpeakerLabel",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            ZIndex = 1,
            Visible = false,
        };
        _speakerLabel.AddThemeColorOverride("font_outline_color", new Color(1f, 1f, 1f, 0.9f));
        _speakerLabel.AddThemeConstantOverride("outline_size", 3);
        if (_font != null) _speakerLabel.AddThemeFontOverride("font", _font);
        _speakerLabel.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_speakerLabel);

        // バナー: 中央大きめ
        _bannerLabel = new Label
        {
            Name = "BannerLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(0, Main.ScreenHeight / 2 - 16),
            Size = new Vector2(Main.ScreenWidth, 32),
        };
        _bannerLabel.AddThemeColorOverride("font_color", Ui.Light); // 光（淡い金）
        _bannerLabel.AddThemeColorOverride("font_outline_color", Ui.OutlineDark);
        _bannerLabel.AddThemeConstantOverride("outline_size", 3);
        if (_font != null) _bannerLabel.AddThemeFontOverride("font", _font);
        _bannerLabel.AddThemeFontSizeOverride("font_size", 24); // native 12 の2倍でクリスプ
        _bannerLabel.Visible = false;
        AddChild(_bannerLabel);

        // 残機: 左上（ドットハート）
        _hearts = new HeartsBar { Name = "Hearts", Position = new Vector2(5, 5) };
        AddChild(_hearts);

        // ボム数: ハートの下（紫の四角ピップ。残機ハートと並ぶピクセル調）
        const int maxPips = 8;
        _bombPips = new ColorRect[maxPips];
        for (int i = 0; i < maxPips; i++)
        {
            var pip = new ColorRect
            {
                Name = $"BombPip{i}",
                Position = new Vector2(5 + i * 6, 18),
                Size = new Vector2(4, 4),
                Color = Ui.Bomb,
                Visible = false,
            };
            pip.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(pip);
            _bombPips[i] = pip;
        }

        // やさしさゲージ（ハート/ボムの下）
        _kindBg = new ColorRect { Name = "KindBg", Position = new Vector2(5, 28), Size = new Vector2(64, 4), Color = new Color(0.15f, 0.12f, 0.20f, 0.55f) };
        _kindBg.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_kindBg);
        _kindFill = new ColorRect { Name = "KindFill", Position = new Vector2(5, 28), Size = new Vector2(0, 4), Color = Ui.Light };
        _kindFill.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_kindFill);
        _overloadLabel = new Label { Name = "OverloadLabel", Position = new Vector2(72, 25), Size = new Vector2(140, 12) };
        StyleLabel(_overloadLabel, 8, Ui.Hp);
        _overloadLabel.Visible = false;
        AddChild(_overloadLabel);

        // ヒカゲ専用スキル表示（やさしさゲージの下）。ヒカゲ加入時のみ表示。
        _skillLabel = new Label { Name = "SkillLabel", Position = new Vector2(5, 34), Size = new Vector2(160, 12) };
        StyleLabel(_skillLabel, 8, Ui.Hp);
        _skillLabel.Visible = false;
        AddChild(_skillLabel);

        // 浄化ゲージ（上部中央・ステージの目標）。空が晴れていく度合いを表す。
        _purifyBg = new ColorRect { Name = "PurifyBg", Position = new Vector2(PurifyGaugeX, 6), Size = new Vector2(PurifyGaugeW, 6), Color = new Color(0.12f, 0.10f, 0.18f, 0.6f) };
        _purifyBg.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_purifyBg);
        _purifyFill = new ColorRect { Name = "PurifyFill", Position = new Vector2(PurifyGaugeX, 6), Size = new Vector2(0, 6), Color = Ui.Purify };
        _purifyFill.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_purifyFill);
        _purifyLabel = new Label { Name = "PurifyLabel", Position = new Vector2(PurifyGaugeX, 13), Size = new Vector2(PurifyGaugeW, 10), HorizontalAlignment = HorizontalAlignment.Center };
        StyleLabel(_purifyLabel, 8, Ui.Purify);
        AddChild(_purifyLabel);

        // ボスHPバー（中ボス戦のみ表示）
        _bossBg = new ColorRect { Name = "BossBg", Position = new Vector2(BossBarX, BossBarY), Size = new Vector2(BossBarW, 7), Color = new Color(0.14f, 0.06f, 0.12f, 0.7f), Visible = false };
        _bossBg.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_bossBg);
        _bossFill = new ColorRect { Name = "BossFill", Position = new Vector2(BossBarX, BossBarY), Size = new Vector2(BossBarW, 7), Color = Ui.Kegare, Visible = false };
        _bossFill.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_bossFill);
        _bossLabel = new Label { Name = "BossLabel", Position = new Vector2(BossBarX, BossBarY - 11), Size = new Vector2(BossBarW, 10) };
        StyleLabel(_bossLabel, 8, Ui.Kegare);
        _bossLabel.Visible = false;
        AddChild(_bossLabel);

        // スコア: 右上（右寄せ）
        _scoreLabel = new Label
        {
            Name = "ScoreLabel",
            HorizontalAlignment = HorizontalAlignment.Right,
            Position = new Vector2(Main.ScreenWidth - 128, 4),
            Size = new Vector2(124, 12),
        };
        StyleLabel(_scoreLabel, 10, Ui.Score);
        AddChild(_scoreLabel);

        // コンボ: スコアの下（右寄せ・x2以上で表示）
        _comboLabel = new Label
        {
            Name = "ComboLabel",
            HorizontalAlignment = HorizontalAlignment.Right,
            Position = new Vector2(Main.ScreenWidth - 128, 16),
            Size = new Vector2(124, 12),
        };
        StyleLabel(_comboLabel, 8, Ui.Mina); // 紫
        _comboLabel.Visible = false;
        AddChild(_comboLabel);

        // 炎上中インジケータ（中央上・赤）
        _burnLabel = new Label
        {
            Name = "BurnLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(Main.ScreenWidth / 2 - 80, 26),
            Size = new Vector2(160, 12),
        };
        StyleLabel(_burnLabel, 9, Ui.Burn); // 赤
        _burnLabel.Visible = false;
        AddChild(_burnLabel);

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

    // ラベルにピクセルフォント・サイズ・暗アウトラインを適用するヘルパ。
    // Refrain パレットは明るい役割色（金/シアン/桃）が多いので、縁取りは夜色にして
    // 明るいステージ背景でも沈まず読めるようにする。
    private void StyleLabel(Label l, int size, Color color)
    {
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", Ui.OutlineDark);
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
            for (int i = 0; i < _bombPips.Length; i++)
                _bombPips[i].Visible = i < game.Bombs;
            _burnLabel.Visible = game.BurningThisRun;
            if (game.BurningThisRun) _burnLabel.Text = "炎上中  発射↓ 移動↓ Imp↓";
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
            _kindFill.Color = game.IsOverload ? Ui.Hp : Ui.Light;
            _overloadLabel.Visible = game.IsOverload;
            if (game.IsOverload) _overloadLabel.Text = "やさしさ全開！";
            if (game.JustOverloaded) { ShowBanner("やさしさ全開！"); Flash(); }

            // 浄化ゲージ（目標達成度）。色は冷たい青→暖色へ。
            _purifyFill.Size = new Vector2(PurifyGaugeW * game.StageProgress, 6);
            _purifyFill.Color = Ui.Purify.Lerp(Ui.PurifyHi, game.StageProgress);
            _purifyLabel.Text = $"浄化 {Mathf.RoundToInt(game.StageProgress * 100f)}%";
        }

        // ボムフラッシュの減衰
        if (_flashAlpha > 0f)
        {
            _flashAlpha = Mathf.Max(0f, _flashAlpha - (float)delta * 2.2f);
            _flash.Color = new Color(_flashRgb.R, _flashRgb.G, _flashRgb.B, _flashAlpha);
        }

        if (_messageTimer > 0)
        {
            if (!HoldBubble) _messageTimer -= delta; // 手動送り中は減らさない＝消えない
            if (_messageTimer <= 0)
            {
                _messageLabel.Visible = false;
                _speakerLabel.Visible = false;
                _bubble.Visible = false;
                _portrait.Visible = false;
            }
        }
        // 吹き出し表示中は敵を止める。会話が始まった瞬間（false→true）に、
        // 画面上に飛んでいる敵弾をすべて消す＝しゃべり始めたら攻撃をリセットする。
        bool nowPaused = _bubble.Visible;
        if (nowPaused && !BubblePaused)
            ClearEnemyBullets();
        BubblePaused = nowPaused;

        if (_bannerTimer > 0)
        {
            _bannerTimer -= delta;
            if (_bannerTimer <= 0)
            {
                _bannerLabel.Visible = false;
            }
        }
    }

    // 会話開始時に画面上の敵弾を一掃する（攻撃のリセット）。
    private void ClearEnemyBullets()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        foreach (Node n in GetTree().GetNodesInGroup("enemy_bullets"))
            if (n is Bullet b && b.Active)
                pool.Despawn(b);
    }

    // テロップ（吹き出し）でメッセージ表示。立ち絵なし。
    public void ShowMessage(string text)
    {
        _portrait.Visible = false;
        LayoutBubble(text, dialog: false);
        _messageTimer = 4.5;
    }

    // テキストボックスに出す「行の種類」。誰のセリフ／ナレ／投稿／中継かを区別する。
    public enum LineKind
    {
        Boy = 0,        // 少年のセリフ
        Mina = 1,       // ミナのセリフ
        Other = 2,      // 相手（ボス）のセリフ
        Narration = 3,  // 地の文・記憶（ミナの語り）＝話者名なし・中央寄せ
        Post = 4,       // X投稿（UI本文）＝「Ｘ 投稿」枠
        Relay = 5,      // 中継：少年の言葉をミナの声で届ける
    }

    // 話者名の色（Refrain: 少年＝浄化シアン／ミナ＝紫／相手＝穢れマゼンタ／投稿＝ミュート）。
    private static readonly Color SpeakerBoyCol = Ui.Purify;
    private static readonly Color SpeakerMinaCol = Ui.Mina;
    private static readonly Color SpeakerOtherCol = Ui.Kegare;
    private static readonly Color SpeakerPostCol = Ui.TextMuted;

    // algo が話す会話（立ち絵＋吹き出し）。旧チュートリアル用。話者名は出さない。
    public void ShowDialog(string text) => ShowDialog(text, "res://char/algo_cutout.png");

    // 話者の立ち絵を指定して会話表示（旧API：話者名ラベルなし）。
    // 立ち絵が無い／読み込めない場合は立ち絵を隠す（前の話者の絵が残らないように）。
    public void ShowDialog(string text, string portraitResPath)
    {
        SetPortrait(portraitResPath);
        LayoutBubble(text, dialog: true);
        _messageTimer = 6.0;
    }

    // 種類つき会話表示。話者名ラベルと描き分け（地の文＝中央ナレ／投稿＝Ｘ枠／中継＝少年(ミナの声)）。
    // portrait は呼び出し側が指定（少年は行ごとの表情、相手はそのボスの立ち絵）。
    // otherName は LineKind.Other のときの話者名（例「レイ」）。
    public void ShowDialog(LineKind kind, string text, string portrait = "", string otherName = "")
    {
        string speaker;
        Color color;
        bool dialog = true;
        string portraitToUse = portrait;
        switch (kind)
        {
            case LineKind.Boy:   speaker = "少年"; color = SpeakerBoyCol; break;
            case LineKind.Mina:  speaker = "ミナ"; color = SpeakerMinaCol; break;
            case LineKind.Other: speaker = otherName; color = SpeakerOtherCol; break;
            case LineKind.Relay: speaker = "少年（ミナの声）"; color = SpeakerBoyCol; break;
            case LineKind.Post:  speaker = "Ｘ 投稿"; color = SpeakerPostCol; portraitToUse = ""; break;
            default: // Narration：話者名なし・立ち絵なし・テロップ調（中央寄せ）で「語り」と分かる
                speaker = ""; color = default; portraitToUse = ""; dialog = false; break;
        }
        SetPortrait(portraitToUse);
        LayoutBubble(text, dialog: dialog, speaker: speaker, speakerColor: color);
        _messageTimer = 6.0;
    }

    private void SetPortrait(string portraitResPath)
    {
        Texture2D? tex = string.IsNullOrEmpty(portraitResPath)
            ? null : ResourceLoader.Load<Texture2D>(portraitResPath);
        if (tex != null) { _portrait.Texture = tex; _portrait.Visible = true; }
        else _portrait.Visible = false; // 地の文／立ち絵未用意の話者は立ち絵なし
    }

    // 吹き出しとテキストを配置。日本語は空白が無いので任意位置で折り返し、
    // テキスト量に応じて吹き出しの高さを自動調整して下端に揃える（はみ出し防止）。
    private void LayoutBubble(string text, bool dialog, string speaker = "", Color? speakerColor = null)
    {
        const float padX = 6f, padY = 5f, margin = 4f;
        float bubbleX = dialog ? 60f : 18f;
        float bubbleW = dialog ? 316f : 348f;
        float innerW = bubbleW - padX * 2f;
        var halign = dialog ? HorizontalAlignment.Left : HorizontalAlignment.Center;

        _messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; // 単語境界優先で自然に折り返す

        // 折り返し後の高さを実測（WordSmart に合わせ WordBound+GraphemeBound で計測）
        float textH = 14f;
        if (_font != null)
        {
            Vector2 sz = _font.GetMultilineStringSize(
                text, halign, innerW, 12, -1,
                TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.GraphemeBound);
            textH = Mathf.Max(14f, Mathf.Ceil(sz.Y) + 2f);
        }
        float bubbleH = textH + padY * 2f;
        float bubbleY = Main.ScreenHeight - bubbleH - margin;

        _bubble.Position = new Vector2(bubbleX, bubbleY);
        _bubble.SetBox(new Vector2(bubbleW, bubbleH), dialog ? 8f : bubbleW / 2f - 4f);

        _messageLabel.Position = new Vector2(bubbleX + padX, bubbleY + padY);
        _messageLabel.Size = new Vector2(innerW, textH);
        _messageLabel.HorizontalAlignment = halign;
        _messageLabel.VerticalAlignment = VerticalAlignment.Top;
        _messageLabel.Text = text;

        _bubble.Visible = true;
        _messageLabel.Visible = true;

        // 話者名ラベルを吹き出しの左上に乗せる（空＝地の文等は非表示）
        if (!string.IsNullOrEmpty(speaker))
        {
            _speakerLabel.Text = speaker;
            _speakerLabel.AddThemeColorOverride("font_color", speakerColor ?? new Color(0.3f, 0.3f, 0.3f));
            _speakerLabel.Position = new Vector2(bubbleX + 4f, bubbleY - 13f);
            _speakerLabel.Size = new Vector2(bubbleW - 8f, 12f);
            _speakerLabel.Visible = true;
        }
        else _speakerLabel.Visible = false;
    }

    // 吹き出しを即座に閉じる（手動送りの終了時に呼ぶ）。
    public void HideBubble()
    {
        _messageTimer = 0;
        _messageLabel.Visible = false;
        _speakerLabel.Visible = false;
        _bubble.Visible = false;
        _portrait.Visible = false;
    }

    // 中央大きめの一時バナー（例: "STAGE CLEAR!"）。
    public void ShowBanner(string text)
    {
        _bannerLabel.Text = text;
        _bannerLabel.Visible = true;
        _bannerTimer = 5.0;
    }

    // ヒカゲ専用スキルの表示（加入時のみ。準備OKでピンク、充填中はグレー）。
    public void SetHikageSkill(bool has, bool ready)
    {
        _skillLabel.Visible = has;
        if (!has) return;
        _skillLabel.Text = ready ? "C: ヒカゲの大波 OK!" : "C: ヒカゲの大波 充填中…";
        _skillLabel.AddThemeColorOverride("font_color", ready ? Ui.Hp : Ui.TextMuted);
    }

    // ボスHPバー表示／更新／非表示。
    public void ShowBossBar(string bossName)
    {
        _bossLabel.Text = bossName;
        _bossBg.Visible = true;
        _bossFill.Visible = true;
        _bossLabel.Visible = true;
    }

    public void UpdateBossBar(float frac)
    {
        _bossFill.Size = new Vector2(BossBarW * Mathf.Clamp(frac, 0f, 1f), 7);
    }

    public void HideBossBar()
    {
        _bossBg.Visible = false;
        _bossFill.Visible = false;
        _bossLabel.Visible = false;
    }

    // 残機表示の更新（ドットハート）。
    public void SetLives(int n)
    {
        _hearts.SetCount(n);
    }

    // ボム発動時の全画面フラッシュ（白）。
    public void Flash()
    {
        _flashRgb = new Color(1f, 1f, 1f);
        _flashAlpha = 0.55f;
        _flash.Color = new Color(_flashRgb.R, _flashRgb.G, _flashRgb.B, _flashAlpha);
    }

    // 被弾時の全画面フラッシュ（赤）。何に当たったか分かるよう強めに。
    public void HitFlash()
    {
        _flashRgb = new Color(1f, 0.2f, 0.28f);
        _flashAlpha = 0.7f;
        _flash.Color = new Color(_flashRgb.R, _flashRgb.G, _flashRgb.B, _flashAlpha);
    }
}
