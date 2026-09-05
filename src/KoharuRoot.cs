using Godot;

// KoharuRoot : STAGE2「こはる」のルート（Koharu.tscn にアタッチ）。
// こはるの心象世界（bg2 stage2 の四層）を敷き、Player(=ミナ)/Hud/StageKoharu を生成。浄化が進むと暖色へ。
// 場所は2つあり、道中A/Cは配信の部屋、道中Bで教室へ層セットごとクロスフェードで入れ替わる。
public partial class KoharuRoot : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageKoharu Stage { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;
    public StageBackground Bg { get; private set; } = null!;

    // 道中A＝部屋（配信の部屋）。L1 は菫寄りの藍で色掛け、L4 は配信画面の加算光。
    private static readonly Color RoomBlue = new Color(0.72f, 0.68f, 1.00f);
    public static readonly BgLayers.Layer[] RoomLayers =
    {
        new BgLayers.Layer("res://char/bg2/stage2/L1_far_room.png",        0.15f, -95, RoomBlue),
        new BgLayers.Layer("res://char/bg2/stage2/L2_mid_room.png",        0.45f, -92, Colors.White),
        new BgLayers.Layer("res://char/bg2/stage2/L3_near_room_left.png",  1.00f, -91, Colors.White,
            offset: new Vector2(0f, 509f) * 0.3f),
        new BgLayers.Layer("res://char/bg2/stage2/L3_near_room_right.png", 1.00f, -91, Colors.White,
            offset: new Vector2(1032f, 520f) * 0.3f),
        new BgLayers.Layer("res://char/bg2/stage2/L4_light_room_screen.png", 0f, -88, Colors.White, additive: true),
    };

    // 道中B＝教室（こはるが立てなかった場所）。L1 は青灰で色掛け、L4 は窓の加算光。
    private static readonly Color ClassBlue = new Color(0.80f, 0.86f, 1.00f);
    public static readonly BgLayers.Layer[] ClassLayers =
    {
        new BgLayers.Layer("res://char/bg2/stage2/L1_far_class.png",        0.15f, -95, ClassBlue),
        new BgLayers.Layer("res://char/bg2/stage2/L2_mid_class.png",        0.45f, -92, Colors.White),
        new BgLayers.Layer("res://char/bg2/stage2/L3_near_class_left.png",  1.00f, -91, Colors.White,
            offset: new Vector2(0f, 380f) * 0.3f),
        new BgLayers.Layer("res://char/bg2/stage2/L3_near_class_right.png", 1.00f, -91, Colors.White,
            offset: new Vector2(1051f, 285f) * 0.3f),
        new BgLayers.Layer("res://char/bg2/stage2/L4_light_class_window.png", 0f, -88, Colors.White, additive: true),
    };

    private CanvasModulate _tint = null!;
    private static readonly Color Cold = new Color(0.64f, 0.68f, 0.84f); // 電気の消えた部屋（背景が元々暗いので濃くしすぎない）
    private static readonly Color Warm = new Color(1.10f, 1.00f, 0.86f); // 灯りの戻った配信画面
    private float _warmth;
    private readonly RetryHold _retry = new();
    private bool _exitHeld;

    public override void _Ready()
    {
        var g = GetNodeOrNull<GameManager>("/root/Game");
        g?.ResetRun();
        g?.BeginStageRun("koharu");

        _tint = new CanvasModulate { Name = "Tint", Color = Cold };
        AddChild(_tint);

        // こはる面の四層背景（char/bg2/stage2）。場所が2つあり、道中Aは部屋、道中Bで教室へ層ごと入れ替わる
        // （StageKoharu が Step_MidwaveB の頭で Bg.CrossfadeLayersTo(ClassLayers) を呼ぶ）。
        // L1 は無彩色の素材なので Modulate で色掛けする（部屋＝菫寄りの藍／教室＝青灰）。
        // L3 の一枚物は素材座標(1280x720基準)の配置を 0.3 倍して画面座標に落とす。
        // L4（配信画面／窓の光）は加算・非スクロール。ボス突入(EnterBoss)で L4 が消えて L1〜L3 が沈む。
        var bg = new StageBackground
        {
            Name = "StageBackground",
            MidScrollSpeed = 18f, // 電気の消えた部屋は凪いだ空気＝最も控えめな前進感
            LayerDefs = RoomLayers,
        };
        AddChild(bg);
        Bg = bg;
        if (!bg.HasMid)
        {
            var fill = new ColorRect { Name = "Fill", Color = new Color(0.14f, 0.12f, 0.13f), Size = new Vector2(ScreenWidth, ScreenHeight), ZIndex = -100 };
            fill.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(fill);
        }

        World = new Node2D { Name = "World" };
        AddChild(World);
        World.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });
        // 近景パララックス：淀んだ空気の対流で凪いだ前進感（弾より奥 -60/-55）。
        // 生成スクロール背景(scroll.png, -70)は不透明の全画面板で bg2 の層(-95..-88)を隠すので敷かない。
        AddChild(new ScrollFx { Name = "ScrollFx", Kind = ScrollFx.StageKind.Koharu, SkipScrollTexture = true });
        AddChild(new StageImagery { Name = "Imagery", Kind = StageImagery.StageKind.Koharu }); // 空席に箸・冷める食卓
        AddChild(new WorldGrade { Name = "WorldGrade" }); // 進行度で「汚染→浄化」を4段階にくっきり切替（節目の色グレーディング）
        AddChild(new MurkVignette { Name = "MurkVignette" }); // 高汚染で端から寄る濁りビネット（弾より奥・中央は抜け）

        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(60, 108);
        // STAGE2：STAGE1を祓った分、ミナの光がわずかに濁り始める（伏線的に気づかない程度）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetContamination(0.18f);
        Player.SetCorruption(0.18f);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.SetLives(Player.Lives);

        Stage = new StageKoharu { Name = "StageKoharu", Player = Player, Hud = Hud, World = World };
        AddChild(Stage);
    }

    public override void _Process(double delta)
    {
        // ポーズメニュー等を閉じた押下の漏れ（B=抜ける 等）がこのフレームに誤発火しないよう食う。
        if (Pad.UiBlocked(this)) { _exitHeld = true; return; }

        // R 長押し(0.45s)でリトライ（即発は誤爆しやすい週次PT指摘→長押し化。ゲームオーバー中は即発）。
        // パッドの Start はポーズメニューと衝突するため廃止＝メニュー内「さいしょからやりなおす」を使う。
        bool gameOver = (Player?.Lives ?? 1) <= 0;
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R), instant: gameOver))
        {
            // ゲームオーバー中のみ Shift で分岐：R単体＝ボスから再開（StageKoharu._step=11 に乗る）／
            // Shift+R＝最初から（従来どおり）。Shift時は SelectedEntry に触らない
            // （--boss デバッグ起動中の DebugAlwaysBoss 持ち回りを壊さないため。通常プレイでは
            //  前回の _Ready() 時点で既に Start へ消費済みなので実質「最初から」になる）。
            if (gameOver && !Input.IsKeyPressed(Key.Shift))
            {
                var g = GetNodeOrNull<GameManager>("/root/Game");
                if (g != null) g.SelectedEntry = GameManager.StageEntry.Boss;
            }
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            GetTree().ReloadCurrentScene();
            return;
        }
        Hud?.SetRetryHold(_retry.Progress);

        // ゲームオーバー（残機0）中は「抜ける（ハブへ戻る）」を受付。お金は保存して持ち帰る。
        if (gameOver)
        {
            if (GameManager.HandleGameOverExit(this, Hud, ref _exitHeld)) return;
        }
        else { Hud?.ShowGameOverPrompt(""); _exitHeld = false; }

        var game = GetNodeOrNull<GameManager>("/root/Game");
        // 前のめり進行：自機の左右位置ぶんだけ時間アキュムレータを進める（撃破カウンタには不干渉）。
        if (Player != null) game?.TickProgress(Player.GlobalPosition.X, (float)delta);
        float target = game?.Warmth ?? 0f;
        _warmth = Mathf.MoveToward(_warmth, target, (float)delta * 0.4f);
        if (_tint != null) _tint.Color = Cold.Lerp(Warm, _warmth);

        // 汚染ゲージ：STAGE2は「わずか(0.18)→縁の濁り(0.45)」（設計書 4-b）。
        // 開始値は据え置き、このステージで増える分だけ汚染耐性で緩む（#2-B）。
        const float baseFrom = 0.18f, baseTo = 0.45f;
        float gained = (baseTo - baseFrom) * (game?.ContaminationGainMul ?? 1f) * (game?.StageProgress ?? 0f);
        float corr = baseFrom + gained;
        game?.SetContamination(corr);
        Player?.SetCorruption(corr);
    }
}
