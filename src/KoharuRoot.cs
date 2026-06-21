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
    private bool _exitHeld;

    public override void _Ready()
    {
        var g = GetNodeOrNull<GameManager>("/root/Game");
        g?.ResetRun();
        g?.BeginStageRun("koharu");

        _tint = new CanvasModulate { Name = "Tint", Color = Cold };
        AddChild(_tint);

        // 台所背景。道中＝横スクロールで前進感／ボス＝専用背景へ切替（StageBackground）。
        // 新アートが来たら Mid/Boss のパスを差すだけ。今は既存アートを道中背景に流用、
        // ボス専用背景は未用意のため当面 kitchen.png を流用（突入で軽い動き＝差替え機構の動作確認）。
        var bg = new StageBackground
        {
            Name = "StageBackground",
            MidBgPath = "res://char/bg/koharu/kitchen.png",
            BossBgPath = "res://char/bg/koharu/kitchen.png",
            MidScrollSpeed = 18f, // 台所は凪いだ空気＝最も控えめな前進感
        };
        AddChild(bg);
        if (!bg.HasMid)
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
        AddChild(new MurkVignette { Name = "MurkVignette" }); // 高汚染で端から寄る濁りビネット（弾より奥・中央は抜け）

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

        // ゲームオーバー（残機0）中は「抜ける（ハブへ戻る）」を受付。お金は保存して持ち帰る。
        if ((Player?.Lives ?? 1) <= 0)
        {
            if (GameManager.HandleGameOverExit(this, Hud, ref _exitHeld)) return;
        }
        else { Hud?.ShowGameOverPrompt(""); _exitHeld = false; }

        var game = GetNodeOrNull<GameManager>("/root/Game");
        float target = game?.Warmth ?? 0f;
        _warmth = Mathf.MoveToward(_warmth, target, (float)delta * 0.4f);
        if (_tint != null) _tint.Color = Cold.Lerp(Warm, _warmth);

        // 汚染ゲージ：STAGE3は「縁の濁り(0.42)→深刻に翳る(0.72)」（設計書 4-b）。次のFINALで黒く溶ける(1.0)。
        // 開始値は据え置き、このステージで増える分だけ汚染耐性で緩む（#2-B）。
        const float baseFrom = 0.42f, baseTo = 0.72f;
        float gained = (baseTo - baseFrom) * (game?.ContaminationGainMul ?? 1f) * (game?.StageProgress ?? 0f);
        float corr = baseFrom + gained;
        game?.SetContamination(corr);
        Player?.SetCorruption(corr);
    }
}
