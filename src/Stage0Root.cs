using Godot;

// Stage0Root : ステージ0「完全チュートリアル」のルート（Stage0.tscn にアタッチ）。
// ReiRoot.cs を雛形に、穏やかな練習場トーンの背景と World/FxLayer/GameCamera/Player(=ミナ)/Hud を生成する。
// 物語演出（汚染ビネット・パララックス・掲示板の海など）は付けない＝迷いなく操作に集中できる無菌の練習場。
// 進行は StageZero が 9 ステップで駆動。練習モード（ゲージ/残機/ボムを消費しない）を _Ready で ON にする。
public partial class Stage0Root : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageZero Stage { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;

    private readonly RetryHold _retry = new();
    private bool _exitHeld;

    public override void _Ready()
    {
        var g = GetNodeOrNull<GameManager>("/root/Game");
        g?.ResetRun();
        g?.BeginStageRun("tutorial");
        // 練習モード ON：このシーンの間はボム・残機を消費しない（詰み防止）。Hub 遷移時に StageZero が OFF にする。
        if (g != null) g.TutorialNoConsume = true;

        // 穏やかな練習場トーン（暖色寄りの一様な明かり）。汚染演出なし。
        AddChild(new CanvasModulate { Name = "Tint", Color = new Color(1.0f, 0.98f, 0.94f) });

        // 背景：あかり面(char/bg2/stage1)の層を明るく敷く。小物（L3 の傘・看板）は置かない＝無菌の練習場。
        //   本編のあかり面が雨青 (0.54,0.73,1.00) で沈んでいるのに対し、練習場は「明るい方の同じ部屋」。
        //   Modulate は 1.0 が上限で明るくはできないので、(a) L1/L2 の色掛けを暖白 (1.00,0.95,0.85) に留めて
        //   ほぼ素通しにし、(b) 光の層 L4（加算）を強めに二重で足して持ち上げる、の2手で明るさを作る。
        //   ボス突入はこの面には無い（StageZero は EnterBoss を呼ばない）ので暗転の指定も要らない。
        var practiceWarm = new Color(1.00f, 0.95f, 0.85f);
        var bg = new StageBackground
        {
            Name = "StageBackground",
            LayerDefs = new[]
            {
                new BgLayers.Layer("res://char/bg2/stage1/L1_far.png",           0.15f, -95, practiceWarm),
                new BgLayers.Layer("res://char/bg2/stage1/L2_mid.png",           0.45f, -92, practiceWarm),
                new BgLayers.Layer("res://char/bg2/stage1/L4_light_window.png",  0f,    -88, Colors.White, additive: true),
                new BgLayers.Layer("res://char/bg2/stage1/L4_light_monitor.png", 0f,    -88, Colors.White, additive: true),
                // 窓の光をもう一枚だけ薄く重ねて全体を持ち上げる（加算の二度掛け＝Modulate の 1.0 上限の回避）。
                new BgLayers.Layer("res://char/bg2/stage1/L4_light_window.png",  0f,    -88,
                    new Color(1f, 1f, 1f, 0.55f), additive: true),
            },
        };
        AddChild(bg);
        if (!bg.HasMid)
        {
            var fill = new ColorRect { Name = "Fill", Color = new Color(0.10f, 0.12f, 0.18f), Size = new Vector2(ScreenWidth, ScreenHeight), ZIndex = -100 };
            fill.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(fill);
        }

        World = new Node2D { Name = "World" };
        AddChild(World);
        World.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });

        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(180, 140);
        g?.SetContamination(0f);
        Player.SetCorruption(0f);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.SetLives(Player.Lives);
        Hud.TutorialActive = true; // 常駐操作ガイドを引っ込め、個別指導の指示帯に一本化する

        Stage = new StageZero { Name = "StageZero", Player = Player, Hud = Hud, World = World };
        AddChild(Stage);
    }

    public override void _Process(double delta)
    {
        // ポーズメニュー等を閉じた押下の漏れ（B=抜ける 等）がこのフレームに誤発火しないよう食う。
        if (Pad.UiBlocked(this)) { _exitHeld = true; return; }

        // R 長押し(0.45s)＝最初から（練習をやり直す）。即発は誤爆しやすい週次PT指摘→長押し化
        //（ゲームオーバー中は即発）。Start はポーズメニューと衝突するため廃止＝メニュー内リトライを使う。
        bool gameOver = (Player?.Lives ?? 1) <= 0;
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R), instant: gameOver))
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            GetTree().ReloadCurrentScene();
            return;
        }
        Hud?.SetRetryHold(_retry.Progress);

        // 練習モードなので通常は残機0に到達しないが、安全網としてゲームオーバー抜けを受け付ける。
        if (gameOver)
        {
            if (GameManager.HandleGameOverExit(this, Hud, ref _exitHeld)) return;
        }
        else { Hud?.ShowGameOverPrompt(""); _exitHeld = false; }
    }

    public override void _ExitTree()
    {
        // どの経路でこのシーンを抜けても練習モードは確実に解除する（Hub/本編へ消費ガードを持ち越さない）。
        var g = GetNodeOrNull<GameManager>("/root/Game");
        if (g != null) g.TutorialNoConsume = false;
    }
}
