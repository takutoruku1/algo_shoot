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

        // 世界の色味（冷→暖トランジション用）。浄化が進むと暖かくなる。
        _canvasModulate = new CanvasModulate { Name = "WorldTint", Color = ColdTint };
        AddChild(_canvasModulate);

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

        // 演出レイヤー（パーティクル）と シェイク用カメラ
        World.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });

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

    private readonly RetryHold _retry = new();

    // 世界の色味: 冷たい荒れた世界 → 暖かい浄化された世界
    private CanvasModulate _canvasModulate = null!;
    private static readonly Color ColdTint = new Color(0.80f, 0.85f, 0.98f); // やや青暗い
    private static readonly Color WarmTint = new Color(1.06f, 1.0f, 0.94f);  // やや暖かく明るい
    private float _warmth = 0f;

    public override void _Process(double delta)
    {
        // ポーズメニュー等を閉じた押下の漏れがこのフレームに誤発火しないよう食う。
        if (Pad.UiBlocked(this)) return;

        // R 長押し(0.7s)で最初からリスタート（即発は誤爆しやすい週次PT指摘→長押し化。残機0中は即発）。
        bool gameOver = (Player?.Lives ?? 1) <= 0;
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R), instant: gameOver))
        {
            Restart();
            return;
        }
        Hud?.SetRetryHold(_retry.Progress);

        // 浄化が進むほど世界を暖色へ（なめらかに追従）
        float target = GetNodeOrNull<GameManager>("/root/Game")?.Warmth ?? 0f;
        _warmth = Mathf.MoveToward(_warmth, target, (float)delta * 0.5f);
        if (_canvasModulate != null)
            _canvasModulate.Color = ColdTint.Lerp(WarmTint, _warmth);
    }

    private void Restart()
    {
        // オートロードのプールに残る弾をクリアしてからシーン再読込。
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().ReloadCurrentScene();
    }
}
