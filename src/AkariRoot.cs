using Godot;

// AkariRoot : STAGE1「あかり」のルート（Akari.tscn にアタッチ）。
// 雨の教室の背景を敷き、Player(=ミナ)/Hud/StageAkari を生成。浄化が進むと部屋が暖色へ晴れる。
public partial class AkariRoot : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageAkari Stage { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;

    private CanvasModulate _tint = null!;
    private static readonly Color Cold = new Color(0.60f, 0.68f, 0.92f); // 雨の寒色
    private static readonly Color Warm = new Color(1.05f, 0.99f, 0.92f); // 晴れた暖色
    private float _warmth;
    private readonly RetryHold _retry = new();
    private bool _exitHeld;

    public override void _Ready()
    {
        var g = GetNodeOrNull<GameManager>("/root/Game");
        g?.ResetRun();
        g?.BeginStageRun("akari");

        _tint = new CanvasModulate { Name = "Tint", Color = Cold };
        AddChild(_tint);

        // 雨の街の四層背景（char/bg2/stage1）。奥→手前に L1 遠景 → F2 雨窓 → L2 中景 → L3 近景 → L4 光。
        // L1 と F2 は無彩色の素材なので Modulate で雨青に色掛けする（設計は (0.69,0.94,1.28) だが
        // Modulate は 1.0 を超えられないので同じ色相のまま (0.54,0.73,1.00) へ正規化）。
        // L3 の一枚物は素材座標(1280x720基準)の配置を 0.3 倍して画面座標に落とす。
        // L4（モニタと窓の光）は加算・非スクロール。ボス突入(EnterBoss)で L4 が消えて L1〜L3 が沈む。
        var rainBlue = new Color(0.54f, 0.73f, 1.00f);
        var bg = new StageBackground
        {
            Name = "StageBackground",
            LayerDefs = new[]
            {
                new BgLayers.Layer("res://char/bg2/stage1/L1_far.png",           0.15f, -95, rainBlue),
                new BgLayers.Layer("res://char/bg2/common/F2_rain_window.png",   0.15f, -94, rainBlue),
                new BgLayers.Layer("res://char/bg2/stage1/L2_mid.png",           0.45f, -92, Colors.White),
                new BgLayers.Layer("res://char/bg2/stage1/L3_near_left.png",     1.00f, -91, Colors.White,
                    offset: new Vector2(0f, 407f) * 0.3f),
                new BgLayers.Layer("res://char/bg2/stage1/L3_near_right.png",    1.00f, -91, Colors.White,
                    offset: new Vector2(1057f, 566f) * 0.3f),
                new BgLayers.Layer("res://char/bg2/stage1/L4_light_monitor.png", 0f,    -88, Colors.White, additive: true),
                new BgLayers.Layer("res://char/bg2/stage1/L4_light_window.png",  0f,    -88, Colors.White, additive: true),
            },
        };
        AddChild(bg);
        if (!bg.HasMid)
        {
            var fill = new ColorRect { Name = "Fill", Color = new Color(0.12f, 0.14f, 0.20f), Size = new Vector2(ScreenWidth, ScreenHeight), ZIndex = -100 };
            fill.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(fill);
        }

        World = new Node2D { Name = "World" };
        AddChild(World);
        World.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });
        // 近景パララックス：手前の雨筋で前進感（弾より奥 -60/-55）。
        // 生成スクロール背景(scroll.png, -70)は不透明の全画面板で bg2 の層(-95..-88)を隠すので敷かない。
        AddChild(new ScrollFx { Name = "ScrollFx", Kind = ScrollFx.StageKind.Akari, SkipScrollTexture = true });
        AddChild(new StageImagery { Name = "Imagery", Kind = StageImagery.StageKind.Akari }); // 黒板の自責・机が天井へ・記憶
        AddChild(new WorldGrade { Name = "WorldGrade" }); // 進行度で「汚染→浄化」を4段階にくっきり切替（節目の色グレーディング）
        AddChild(new MurkVignette { Name = "MurkVignette" }); // 高汚染で端から寄る濁りビネット（弾より奥・中央は抜け）

        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(60, 108);
        // STAGE2：STAGE1を祓った分、ミナの光がわずかに濁り始める（伏線的に気づかない程度）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetContamination(0.16f);
        Player.SetCorruption(0.16f);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.SetLives(Player.Lives);

        Stage = new StageAkari { Name = "StageAkari", Player = Player, Hud = Hud, World = World };
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
            // ゲームオーバー中のみ Shift で分岐：R単体＝ボスから再開（StageAkari._step=11 に乗る）／
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

        // 浄化が進むと部屋が晴れる（寒色→暖色）。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        // 前のめり進行：自機の左右位置ぶんだけ時間アキュムレータを進める（撃破カウンタには不干渉）。
        if (Player != null) game?.TickProgress(Player.GlobalPosition.X, (float)delta);
        float target = game?.Warmth ?? 0f;
        _warmth = Mathf.MoveToward(_warmth, target, (float)delta * 0.4f);
        if (_tint != null) _tint.Color = Cold.Lerp(Warm, _warmth);

        // 汚染ゲージ：STAGE2は「わずか(0.16)→縁の濁り(0.42)」（設計書 4-b）。
        // 開始値は据え置き、このステージで増える分だけ汚染耐性で緩む（#2-B）。
        const float baseFrom = 0.16f, baseTo = 0.42f;
        float gained = (baseTo - baseFrom) * (game?.ContaminationGainMul ?? 1f) * (game?.StageProgress ?? 0f);
        float corr = baseFrom + gained;
        game?.SetContamination(corr);
        Player?.SetCorruption(corr);
    }
}
