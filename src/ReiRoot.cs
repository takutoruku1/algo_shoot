using Godot;

// ReiRoot : STAGE3「レイ」のルート（Rei.tscn にアタッチ）。
// レイの心象世界（bg2 の描き込み背景）を敷き、Player(=ミナ)/Hud/StageRei を生成。改心が進むと少し晴れる。
// ボス突入で道中の街から星飾りの専用舞台へ切り替わる。
public partial class ReiRoot : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageRei Stage { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;

    public static readonly BgLayers.Layer[] RouteLayers =
    {
        new BgLayers.Layer("res://char/bg2/route/rei_far.png", 0.22f, -95, Colors.White, loop: true, fitToField: true),
        new BgLayers.Layer("res://char/bg2/route/rei_mid.png", 0.62f, -92, Colors.White, loop: true, fitToField: true),
        new BgLayers.Layer("res://char/bg2/route/rei_near.png", 1.20f, -91, Colors.White, loop: true, fitToField: true),
    };

    public static readonly BgLayers.Layer[] MidbossLayers =
    {
        new BgLayers.Layer("res://char/bg2/midboss/rei_v1.png", 0.15f, -95, Colors.White, fitToField: true),
    };

    public static readonly BgLayers.Layer[] BossLayers =
    {
        new BgLayers.Layer("res://char/bg2/boss/rei_v1.png", 0.15f, -95, Colors.White, fitToField: true),
    };

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

        // 描き込み背景（char/bg2/route・midboss・boss）。開幕は配信の街の道中パノラマ（RouteLayers）を敷き、
        // 中ボスで専用の部屋、ボス突入で星飾りの専用舞台へ層ごと切り替える。会話・選択肢の間も直前の背景を
        // そのまま保つので、会話用の層セット（LayerDefs）は渡さない。
        var bg = new StageBackground
        {
            Name = "StageBackground",
            RouteLayerDefs = RouteLayers,
            MidbossLayerDefs = MidbossLayers,
            LayerBossBehavior = BgLayers.BossBehavior.Illustrated,
            BossLayerDefs = BossLayers,
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
        AddChild(new StageImagery { Name = "Imagery", Kind = StageImagery.StageKind.Rei }); // 配信のコメント欄・白飛びの「登録者 2,000」
        AddChild(new WorldGrade { Name = "WorldGrade" }); // 進行度で「汚染→浄化」を4段階にくっきり切替（節目の色グレーディング）
        AddChild(new MurkVignette { Name = "MurkVignette" }); // 高汚染で端から寄る濁りビネット（弾より奥・中央は抜け）
        AddChild(new FuryDial { Name = "FuryDial" }); // 【激情】＝ボスの下書き（入力欄）＋画面の色連動（盤面の奥 -44/-46・弾より遥かに奥）

        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(Field.Left + 60f, 108f); // 盤面の左端から60px（サイドパネル裏に湧かない）
        // STAGE3：縁の濁りがはっきり広がる段階。漫才の裏で深刻化していく。
        GetNodeOrNull<GameManager>("/root/Game")?.SetContamination(0.45f);
        Player.SetCorruption(0.45f);

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
            // ゲームオーバー中のみ Shift で分岐：R単体＝ボスから再開（StageRei._step=10 に乗る。
            //   今ランでボス未到達なら PrepareBossRetry が予約しない＝最初から）／
            // Shift+R＝最初から（従来どおり）。Shift時は SelectedEntry に触らない
            // （--boss デバッグ起動中の DebugAlwaysBoss 持ち回りを壊さないため。通常プレイでは
            //  前回の _Ready() 時点で既に Start へ消費済みなので実質「最初から」になる）。
            if (gameOver && !Input.IsKeyPressed(Key.Shift))
            {
                var g = GetNodeOrNull<GameManager>("/root/Game");
                g?.PrepareBossRetry();
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
        else { GameManager.ClearGameOverChoice(Hud); _exitHeld = false; }

        if (Hud?.CinematicMode == true) return;

        var game = GetNodeOrNull<GameManager>("/root/Game");
        // 前のめり進行：自機の左右位置ぶんだけ時間アキュムレータを進める（撃破カウンタには不干渉）。
        if (Player != null) game?.TickProgress(Player.GlobalPosition.X, (float)delta);
        bool realRealm = GetNodeOrNull<BossRealmFx>("BossRealmFx") is { Revealed: true };
        float target = realRealm ? 1f : game?.Warmth ?? 0f;
        _warmth = Mathf.MoveToward(_warmth, target, (float)delta * 0.4f);
        if (_tint != null) _tint.Color = realRealm ? Colors.White : Cold.Lerp(Warm, _warmth);

        // 汚染ゲージ：STAGE3は「縁の濁り(0.45)→深刻に翳る(0.80)」（設計書 4-b）。次のFINALで黒く溶ける(1.0)。
        // 開始値は据え置き、このステージで増える分だけ汚染耐性で緩む（#2-B）。
        const float baseFrom = 0.45f, baseTo = 0.80f;
        float gained = (baseTo - baseFrom) * (game?.ContaminationGainMul ?? 1f) * (game?.StageProgress ?? 0f);
        float corr = baseFrom + gained;
        game?.SetContamination(corr);
        Player?.SetCorruption(corr);
    }
}
