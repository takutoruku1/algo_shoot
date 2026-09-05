using Godot;

// ReiRoot : STAGE1「レイ」のルート（Rei.tscn にアタッチ）。
// レイの心象世界（bg2 stage3 の四層）を敷き、Player(=ミナ)/Hud/StageRei を生成。改心が進むと少し晴れる。
// ボス突入は他の2面と逆に光が増える（枠が全面化して金の光が点く）＝舞台に上がる面。
public partial class ReiRoot : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageRei Stage { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;

    private CanvasModulate _tint = null!;
    private static readonly Color Cold = new Color(0.58f, 0.66f, 0.95f); // サイバー寒色
    private static readonly Color Warm = new Color(1.02f, 1.0f, 0.96f);
    private float _warmth;
    private readonly RetryHold _retry = new();
    private bool _exitHeld;

    public override void _Ready()
    {
        var g = GetNodeOrNull<GameManager>("/root/Game");
        g?.ResetRun();
        g?.BeginStageRun("rei");

        _tint = new CanvasModulate { Name = "Tint", Color = Cold };
        AddChild(_tint);

        // レイ面の四層背景（char/bg2/stage3）。奥→手前に L1 遠景 → L2 中景（飾り枠と配信卓）→ L3 近景 → L4 光。
        // L1 は無彩色の素材なので Modulate で菫寄りの深い藍に色掛けする。
        // L3 の一枚物は素材の中で既に置き場所が決まった 1280x720 なので配置は (0,0) のまま（0.3 倍で画面に収まる）。
        // ボス突入は他の2面と逆に「光を増やす」：L2_mid → L2_frame_full（枠の全面版）へ層セットを
        // クロスフェードし、L4_light_gold を加算で足す＝舞台が煌々と点く。L1〜L3 の沈みも 0.7 倍に留める
        // （BgLayers.BossBehavior.Brighten）。
        var deepViolet = new Color(0.52f, 0.46f, 0.90f);
        var bg = new StageBackground
        {
            Name = "StageBackground",
            LayerDefs = new[]
            {
                new BgLayers.Layer("res://char/bg2/stage3/L1_far.png",          0.15f, -95, deepViolet),
                new BgLayers.Layer("res://char/bg2/stage3/L2_mid.png",          0.45f, -92, Colors.White),
                new BgLayers.Layer("res://char/bg2/stage3/L3_near_left.png",    1.00f, -91, Colors.White),
                new BgLayers.Layer("res://char/bg2/stage3/L3_near_right.png",   1.00f, -91, Colors.White),
                new BgLayers.Layer("res://char/bg2/stage3/L4_light_screen.png", 0f,    -88, Colors.White, additive: true),
                new BgLayers.Layer("res://char/bg2/stage3/L4_light_ring.png",   0f,    -88, Colors.White, additive: true),
            },
            LayerBossBehavior = BgLayers.BossBehavior.Brighten,
            // ボスの層セット：枠が全面へ広がり、金の光（α0.35 の加算）が足される。近景と遠景は据え置き。
            BossLayerDefs = new[]
            {
                new BgLayers.Layer("res://char/bg2/stage3/L1_far.png",          0.15f, -95, deepViolet),
                new BgLayers.Layer("res://char/bg2/stage3/L2_frame_full.png",   0.45f, -92, Colors.White),
                new BgLayers.Layer("res://char/bg2/stage3/L3_near_left.png",    1.00f, -91, Colors.White),
                new BgLayers.Layer("res://char/bg2/stage3/L3_near_right.png",   1.00f, -91, Colors.White),
                new BgLayers.Layer("res://char/bg2/stage3/L4_light_screen.png", 0f,    -88, Colors.White, additive: true),
                new BgLayers.Layer("res://char/bg2/stage3/L4_light_ring.png",   0f,    -88, Colors.White, additive: true),
                new BgLayers.Layer("res://char/bg2/stage3/L4_light_gold.png",   0f,    -88,
                    new Color(1f, 1f, 1f, 0.35f), additive: true),
            },
        };
        AddChild(bg);
        if (!bg.HasMid)
        {
            var fill = new ColorRect { Name = "Fill", Color = new Color(0.08f, 0.10f, 0.18f), Size = new Vector2(ScreenWidth, ScreenHeight), ZIndex = -100 };
            fill.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(fill);
        }

        World = new Node2D { Name = "World" };
        AddChild(World);
        World.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });
        // 近景パララックス：前進感（弾より奥 -60/-55）。
        // 生成スクロール背景(scroll.png, -70)は不透明の全画面板で bg2 の層(-95..-88)を隠すので敷かない。
        AddChild(new ScrollFx { Name = "ScrollFx", Kind = ScrollFx.StageKind.Rei, SkipScrollTexture = true });
        AddChild(new StageImagery { Name = "Imagery", Kind = StageImagery.StageKind.Rei }); // 順位掲示板の海
        AddChild(new WorldGrade { Name = "WorldGrade" }); // 進行度で「汚染→浄化」を4段階にくっきり切替（節目の色グレーディング）
        AddChild(new MurkVignette { Name = "MurkVignette" }); // 高汚染で端から寄る濁りビネット（弾より奥・中央は抜け）

        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(60, 108);
        // STAGE1：ミナの光はまだ澄んでいる（汚染なし）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetContamination(0f);
        Player.SetCorruption(0f);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.SetLives(Player.Lives);

        Stage = new StageRei { Name = "StageRei", Player = Player, Hud = Hud, World = World };
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
            // ゲームオーバー中のみ Shift で分岐：R単体＝ボスから再開（StageRei._step=10 に乗る）／
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

        // 汚染ゲージ：祓うほど濁る。STAGE1は「澄み(0)→わずか(0.16)」（設計書 4-b）。
        // 開始値は据え置き、このステージで増える分だけ汚染耐性で緩む（#2-B）。
        const float baseFrom = 0f, baseTo = 0.16f;
        float gained = (baseTo - baseFrom) * (game?.ContaminationGainMul ?? 1f) * (game?.StageProgress ?? 0f);
        float corr = baseFrom + gained;
        game?.SetContamination(corr);
        Player?.SetCorruption(corr);
    }
}
