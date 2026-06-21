using Godot;

// MinaRoot : FINAL「暴走したミナ」のルート（MinaBattle.tscn にアタッチ）。
// ミナの内側＝穢れに沈んだ暗い心象世界。少年(=自機・役割反転)/Hud/StageMina を生成。
public partial class MinaRoot : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageMina Stage { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;

    private bool _rHeld;
    private bool _exitHeld;

    public override void _Ready()
    {
        var g = GetNodeOrNull<GameManager>("/root/Game");
        g?.ResetRun();
        g?.SetContamination(1f); // ミナの内側は穢れが頂点

        // 暗い心象背景（縦グラデ：黒〜深い紫）。
        DrawBackdrop();

        World = new Node2D { Name = "World" };
        AddChild(World);
        World.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });
        AddChild(new MurkVignette { Name = "MurkVignette" }); // FINAL=汚染頂点：端から寄る濁りビネット（弾より奥・中央は抜け）

        // 自機＝少年（役割反転）。少年は穢れていないので浄化度0。
        Player = new Player { Name = "Player", Skin = "boy" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(60, 108);
        Player.SetCorruption(0f);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.SetLives(Player.Lives);

        Stage = new StageMina { Name = "StageMina", Player = Player, Hud = Hud, World = World };
        AddChild(Stage);
    }

    private void DrawBackdrop()
    {
        // FINAL は道中の無いボス専用ステージ。暗いグラデを生成テクスチャとして StageBackground に渡し、
        // 最初からボス背景モードで軽く動かす（ゆっくり横ドリフト＋呼吸＋光の明滅）。
        // グラデは横方向に均一なので横ドリフトでも継ぎ目が出ない。
        var grad = new Gradient
        {
            Offsets = new[] { 0f, 0.55f, 1f },
            Colors = new[] { new Color("0c0818"), new Color("0a0612"), new Color("05030a") },
        };
        var tex = new GradientTexture2D
        {
            // 横ドリフト用に横幅を持たせる（8→32px）。縦グラデは FillFrom/To で維持。
            Gradient = grad, Width = 32, Height = 256,
            Fill = GradientTexture2D.FillEnum.Linear, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1),
        };
        var bg = new StageBackground
        {
            Name = "StageBackground",
            BossBgTexture = tex,
            StartInBoss = true,
            BossDriftSpeed = 5f,    // 暗い心象世界＝最もゆっくり
            BossBreathAmp = 0.010f,
            BossPulseAmp = 0.05f,
        };
        AddChild(bg);
    }

    public override void _Process(double delta)
    {
        bool r = Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start);
        if (r && !_rHeld)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            GetTree().ReloadCurrentScene();
        }
        _rHeld = r;

        // ゲームオーバー（残機0）中は「抜ける（ハブへ戻る）」を受付。お金は保存して持ち帰る。
        if ((Player?.Lives ?? 1) <= 0)
        {
            if (GameManager.HandleGameOverExit(this, Hud, ref _exitHeld)) return;
        }
        else { Hud?.ShowGameOverPrompt(""); _exitHeld = false; }
    }
}
