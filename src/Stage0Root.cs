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

        // 背景：あかり面の道中パノラマ（char/bg2/route/akari_*）をそのまま静的な層セットとして敷く。
        //   練習場はこの後に入る STAGE1 と同じ雨の街＝チュートリアル→本編で場所の連続感を保つ。
        //   旧四層（あかり面の手描き層）を明るく敷いていた作りは、四層の廃止（2026-09-22）に伴いこちらへ差し替えた。
        //   ボス突入はこの面には無い（StageZero は EnterBoss を呼ばない）ので暗転の指定も要らない。
        var bg = new StageBackground
        {
            Name = "StageBackground",
            LayerDefs = AkariRoot.RouteLayers,
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
        Player.GlobalPosition = new Vector2(Field.Left + 60f, 140f); // 練習場の定位置（盤面左寄り）
        g?.SetContamination(0f);
        Player.SetCorruption(0f);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        AddChild(new BubbleLayer { Name = "BubbleLayer", Hud = Hud }); // 会話の吹き出し（会話バー・一行字幕・宣告カード）＝弾・自機より奥（世界側 -7）に描く層。状態は Hud が持つ
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
            if (gameOver && !Input.IsKeyPressed(Key.Shift))
                GetNode<GameManager>("/root/Game").PrepareBossRetry(bossCheckpoint: false);
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
        else { GameManager.ClearGameOverChoice(Hud); _exitHeld = false; }
    }

    public override void _ExitTree()
    {
        // どの経路でこのシーンを抜けても練習モードは確実に解除する（Hub/本編へ消費ガードを持ち越さない）。
        var g = GetNodeOrNull<GameManager>("/root/Game");
        if (g != null) g.TutorialNoConsume = false;
    }
}
