using Godot;

// KoharuRoot : STAGE3「こはる」のルート（Koharu.tscn にアタッチ）。
// 台所の心象世界を敷き、Player(=ミナ)/Hud/StageKoharu を生成。浄化が進むと暖色へ。
// 専用背景は未用意のため、暖色の暗いフィルで台所の薄暗さを表現する。
public partial class KoharuRoot : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageKoharu Stage { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;

    private CanvasModulate _tint = null!;
    private static readonly Color Cold = new Color(0.64f, 0.68f, 0.84f); // 冷めた台所（背景が元々暗いので濃くしすぎない）
    private static readonly Color Warm = new Color(1.10f, 1.00f, 0.86f); // 灯のともった食卓
    private float _warmth;
    private bool _rHeld;

    public override void _Ready()
    {
        var g = GetNodeOrNull<GameManager>("/root/Game");
        g?.ResetRun();
        g?.BeginStageRun("koharu");

        _tint = new CanvasModulate { Name = "Tint", Color = Cold };
        AddChild(_tint);

        var tex = ResourceLoader.Load<Texture2D>("res://char/bg/koharu/kitchen.png");
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
            var fill = new ColorRect { Name = "Fill", Color = new Color(0.14f, 0.12f, 0.13f), Size = new Vector2(ScreenWidth, ScreenHeight), ZIndex = -100 };
            fill.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(fill);
        }

        World = new Node2D { Name = "World" };
        AddChild(World);
        World.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });
        AddChild(new ScrollFx { Name = "ScrollFx", Kind = ScrollFx.StageKind.Koharu }); // 近景パララックス：湯気/冷気の対流で凪いだ前進感（弾より奥 -60/-55）
        AddChild(new StageImagery { Name = "Imagery", Kind = StageImagery.StageKind.Koharu }); // 空席に箸・冷める食卓

        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(60, 108);
        // STAGE3：縁の濁りがはっきり広がる段階。漫才の裏で深刻化していく。
        GetNodeOrNull<GameManager>("/root/Game")?.SetContamination(0.42f);
        Player.SetCorruption(0.42f);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.SetLives(Player.Lives);

        Stage = new StageKoharu { Name = "StageKoharu", Player = Player, Hud = Hud, World = World };
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

        var game = GetNodeOrNull<GameManager>("/root/Game");
        float target = game?.Warmth ?? 0f;
        _warmth = Mathf.MoveToward(_warmth, target, (float)delta * 0.4f);
        if (_tint != null) _tint.Color = Cold.Lerp(Warm, _warmth);

        // 汚染ゲージ：STAGE3は「縁の濁り(0.42)→深刻に翳る(0.72)」（設計書 4-b）。次のFINALで黒く溶ける(1.0)。
        float corr = Mathf.Lerp(0.42f, 0.72f, game?.StageProgress ?? 0f);
        game?.SetContamination(corr);
        Player?.SetCorruption(corr);
    }
}
