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
        // 縦グラデの暗い背景を Sprite(GradientTexture2D) で敷く。
        var grad = new Gradient
        {
            Offsets = new[] { 0f, 0.55f, 1f },
            Colors = new[] { new Color("0c0818"), new Color("0a0612"), new Color("05030a") },
        };
        var tex = new GradientTexture2D
        {
            Gradient = grad, Width = 8, Height = 256,
            Fill = GradientTexture2D.FillEnum.Linear, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1),
        };
        var bg = new Sprite2D
        {
            Name = "Backdrop", Texture = tex, Centered = false,
            Scale = new Vector2(ScreenWidth / 8f, ScreenHeight / 256f),
            ZIndex = -100, ZAsRelative = false,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
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
    }
}
