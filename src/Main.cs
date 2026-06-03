using Godot;

// Main : ルート / Main.tscn にアタッチ。
// 固定画面(384x216)。背景を敷き、Player/Hud/StageW0 を生成して参照を渡す。
// Pool は Autoload なので生成不要。カメラ不要。
public partial class Main : Node2D
{
    // 内部解像度
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageW0 Stage { get; private set; } = null!;

    // 敵などをぶら下げるワールドノード（StageW0 がここに add する想定）
    public Node2D World { get; private set; } = null!;

    public override void _Ready()
    {
        // ゲーム状態（スコア/コンボ/ボム）をリセット
        GetNodeOrNull<GameManager>("/root/Game")?.ResetRun();

        // パララックス空背景（char/bg/w0/bg_w0_sky.png があれば流れる）
        var background = new Background { Name = "Background" };
        AddChild(background); // _Ready で HasSky が確定する

        // 空画像が無い時だけ、淡い緑の下地をフォールバックとして敷く
        if (!background.HasSky)
        {
            var bg = new ColorRect
            {
                Name = "BaseFill",
                Color = new Color(0.78f, 0.90f, 0.74f), // 淡い緑
                Position = Vector2.Zero,
                Size = new Vector2(ScreenWidth, ScreenHeight),
                ZIndex = -100
            };
            bg.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(bg);
        }

        // 敵/弾以外のゲーム内オブジェクトを束ねるワールドノード
        World = new Node2D { Name = "World" };
        AddChild(World);

        // Player を (60,108) に生成
        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(60, 108);

        // Hud を生成（CanvasLayer）
        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.SetLives(Player.Lives);

        // StageW0 を生成して参照を渡す
        Stage = new StageW0 { Name = "StageW0" };
        Stage.Player = Player;
        Stage.Hud = Hud;
        Stage.World = World;
        AddChild(Stage);
    }
}
