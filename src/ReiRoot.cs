using Godot;

// ReiRoot : STAGE1「レイ」のルート（Rei.tscn にアタッチ）。
// 順位掲示板の海の背景を敷き、Player(=ミナ)/Hud/StageRei を生成。改心が進むと少し晴れる。
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
    private bool _rHeld;

    public override void _Ready()
    {
        GetNodeOrNull<GameManager>("/root/Game")?.ResetRun();

        _tint = new CanvasModulate { Name = "Tint", Color = Cold };
        AddChild(_tint);

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
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            };
            AddChild(bg);
        }
        else
        {
            var fill = new ColorRect { Name = "Fill", Color = new Color(0.08f, 0.10f, 0.18f), Size = new Vector2(ScreenWidth, ScreenHeight), ZIndex = -100 };
            fill.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(fill);
        }

        World = new Node2D { Name = "World" };
        AddChild(World);
        World.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });

        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(60, 108);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.SetLives(Player.Lives);

        Stage = new StageRei { Name = "StageRei", Player = Player, Hud = Hud, World = World };
        AddChild(Stage);
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

        float target = GetNodeOrNull<GameManager>("/root/Game")?.Warmth ?? 0f;
        _warmth = Mathf.MoveToward(_warmth, target, (float)delta * 0.4f);
        if (_tint != null) _tint.Color = Cold.Lerp(Warm, _warmth);
    }
}
