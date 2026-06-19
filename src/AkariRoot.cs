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
    private bool _rHeld;

    public override void _Ready()
    {
        var g = GetNodeOrNull<GameManager>("/root/Game");
        g?.ResetRun();
        g?.BeginStageRun("akari");

        _tint = new CanvasModulate { Name = "Tint", Color = Cold };
        AddChild(_tint);

        // 雨の教室背景（画面いっぱいにカバー表示）
        var tex = ResourceLoader.Load<Texture2D>("res://char/bg/akari/classroom.png");
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
        bool r = Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start);
        if (r && !_rHeld)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            GetTree().ReloadCurrentScene();
        }
        _rHeld = r;

        // 浄化が進むと部屋が晴れる（寒色→暖色）。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        float target = game?.Warmth ?? 0f;
        _warmth = Mathf.MoveToward(_warmth, target, (float)delta * 0.4f);
        if (_tint != null) _tint.Color = Cold.Lerp(Warm, _warmth);

        // 汚染ゲージ：STAGE2は「わずか(0.16)→縁の濁り(0.42)」（設計書 4-b）。
        float corr = Mathf.Lerp(0.16f, 0.42f, game?.StageProgress ?? 0f);
        game?.SetContamination(corr);
        Player?.SetCorruption(corr);
    }
}
