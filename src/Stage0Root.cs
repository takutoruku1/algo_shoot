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

    private bool _rHeld;
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

        // 背景：board.png があれば流用、無ければ落ち着いた一様塗り。こだわらない。
        var tex = ResourceLoader.Load<Texture2D>("res://char/bg/rei/board.png");
        if (tex != null)
        {
            float scale = Mathf.Max((float)ScreenWidth / tex.GetWidth(), (float)ScreenHeight / tex.GetHeight());
            float w = tex.GetWidth() * scale, h = tex.GetHeight() * scale;
            var bg = new Sprite2D
            {
                Name = "BG",
                Texture = tex,
                Centered = false,
                Scale = new Vector2(scale, scale),
                Position = new Vector2((ScreenWidth - w) / 2f, (ScreenHeight - h) / 2f),
                ZIndex = -90,
                ZAsRelative = false,
                Modulate = new Color(1f, 1f, 1f, 0.55f), // 練習場なので背景は控えめに沈める
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            };
            AddChild(bg);
        }
        else
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
        // R＝最初から（練習をやり直す）。
        bool r = Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start);
        if (r && !_rHeld)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            GetTree().ReloadCurrentScene();
        }
        _rHeld = r;

        // 練習モードなので通常は残機0に到達しないが、安全網としてゲームオーバー抜けを受け付ける。
        if ((Player?.Lives ?? 1) <= 0)
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
