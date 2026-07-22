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

        // 雨の教室背景。道中＝横スクロールで前進感／ボス＝専用背景へ切替（StageBackground）。
        // 新アートが来たら Mid/Boss のパスを差すだけ。今は既存アートを道中背景に流用、
        // ボス専用背景は未用意のため当面 classroom.png を流用（突入で軽い動き＝差替え機構の動作確認）。
        var bg = new StageBackground
        {
            Name = "StageBackground",
            MidBgPath = "res://char/bg/akari/classroom.png",
            BossBgPath = "res://char/bg/akari/classroom.png",
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
        AddChild(new ScrollFx { Name = "ScrollFx", Kind = ScrollFx.StageKind.Akari }); // 近景パララックス：手前の雨筋で前進感（弾より奥 -60/-55）
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

        // R 長押し(0.7s)でリトライ（即発は誤爆しやすい週次PT指摘→長押し化。ゲームオーバー中は即発）。
        // パッドの Start はポーズメニューと衝突するため廃止＝メニュー内「さいしょからやりなおす」を使う。
        bool gameOver = (Player?.Lives ?? 1) <= 0;
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R), instant: gameOver))
        {
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
