using Godot;

// MinaRoot : FINAL「穢れたわたし」のルート（MinaBattle.tscn にアタッチ）。
// ミナの内側＝穢れに沈んだ暗い心象世界。自機/Hud/StageMina を生成。
public partial class MinaRoot : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageMina Stage { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;

    public static readonly BgLayers.Layer[] RouteLayers =
    {
        new BgLayers.Layer("res://char/bg2/route/mina_far.png", 0.22f, -98, Colors.White, loop: true, fitToField: true),
        new BgLayers.Layer("res://char/bg2/route/mina_mid.png", 0.62f, -97, Colors.White, loop: true, fitToField: true),
        new BgLayers.Layer("res://char/bg2/route/mina_near.png", 1.20f, -96, Colors.White, loop: true, fitToField: true),
    };

    private readonly RetryHold _retry = new();
    private bool _exitHeld;

    public override void _Ready()
    {
        var g = GetNodeOrNull<GameManager>("/root/Game");
        g?.ResetRun();
        g?.SetContamination(1f); // ミナの内側は穢れが頂点

        DrawBackdrop();

        World = new Node2D { Name = "World" };
        AddChild(World);
        World.AddChild(new FxLayer { Name = "FxLayer" });
        AddChild(new GameCamera { Name = "GameCamera" });
        AddChild(new MurkVignette { Name = "MurkVignette" }); // FINAL=汚染頂点：端から寄る濁りビネット（弾より奥・中央は抜け）
        AddChild(new FuryDial { Name = "FuryDial" }); // 【激情】＝ボスの下書き（入力欄）＋画面の色連動（盤面の奥 -44/-46・弾より遥かに奥）

        Player = new Player { Name = "Player" };
        World.AddChild(Player);
        Player.GlobalPosition = new Vector2(Field.Left + 60f, 108f); // 盤面の左端から60px（サイドパネル裏に湧かない）
        Player.SetCorruption(0f);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.SetLives(Player.Lives);

        Stage = new StageMina { Name = "StageMina", Player = Player, Hud = Hud, World = World };
        AddChild(Stage);
    }

    private void DrawBackdrop()
    {
        var bg = new StageBackground
        {
            Name = "StageBackground",
            LayerDefs = RouteLayers,
            BossLayerDefs = new[]
            {
                new BgLayers.Layer("res://char/bg2/boss/mina_v1.png", 0.15f, -96,
                    new Color(0.85f, 0.85f, 0.90f), fitToField: true),
            },
            LayerBossBehavior = BgLayers.BossBehavior.Illustrated,
            BaseZ = -96,
        };
        AddChild(bg);

        // 三人の記憶をミナの心象画より前に重ね、最後に消して彼女自身の場所へ戻す。
        var journey = new StageBackground
        {
            Name = "JourneyBackground",
            ForceLayers = true,
            LayerBossBehavior = BgLayers.BossBehavior.Illustrated,
            StartInBoss = true,      // FINAL は常にボス中＝巡る先も各面の「ボス時の見え方」で敷く
        };
        AddChild(journey);
        _journeyBg = journey;
    }

    private StageBackground _journeyBg = null!;
    private int _journey;    // 0:ミナ(開幕) 1:あかり 2:こはる 3:レイ 4:ミナ(着地)

    private static readonly BgLayers.Layer[][] Journey =
    {
        AkariRoot.BossLayers, KoharuRoot.BossLayers, ReiRoot.BossLayers,
    };

    private void TickJourney()
    {
        if (_journeyBg == null) return;
        // BossMina は StageMina が private に握るので、"enemies" group から拾う（StageMina に触らない＝競合回避）。
        BossMina? m = null;
        foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is BossMina bm) { m = bm; break; }
        if (m == null || !IsInstanceValid(m)) return;
        int phase = m.EncounterPhase;
        if (phase == _journey) return;
        _journey = phase;
        if (phase <= Journey.Length)
        {
            _journeyBg.CrossfadeLayersToBoss(BgLayers.BossBehavior.Illustrated, Journey[phase - 1], 1.2f);
        }
        else
        {
            _journeyBg.FadeOutLayers(1.2f);
        }
    }

    public override void _Process(double delta)
    {
        if (Hud.CinematicMode) { _exitHeld = true; return; }
        TickJourney(); // 歴代ボス背景の追体験（ミナのHP段階でクロスフェード）。ポーズ中も止めない＝フェードが凍らない。
        // ポーズメニュー等を閉じた押下の漏れ（B=抜ける 等）がこのフレームに誤発火しないよう食う。
        if (Pad.UiBlocked(this)) { _exitHeld = true; return; }

        // R 長押し(0.45s)でリトライ（即発は誤爆しやすい週次PT指摘→長押し化。ゲームオーバー中は即発）。
        // パッドの Start はポーズメニューと衝突するため廃止＝メニュー内「さいしょからやりなおす」を使う。
        bool gameOver = (Player?.Lives ?? 1) <= 0;
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R), instant: gameOver))
        {
            if (gameOver && !Input.IsKeyPressed(Key.Shift))
                GetNode<GameManager>("/root/Game").PrepareBossRetry(bossCheckpoint: false);
            // FINAL はチェックポイント対象外なので、残響戦を含めて最初から再開する。
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            GetTree().ReloadCurrentScene();
            return;
        }
        Hud?.SetRetryHold(_retry.Progress);

        // ゲームオーバー（残機0）中は「抜ける（ハブへ戻る）」を受付。お金は保存して持ち帰る。
        if (gameOver)
        {
            if (GameManager.HandleGameOverExit(this, Hud, ref _exitHeld)) return;
        }
        else { GameManager.ClearGameOverChoice(Hud); _exitHeld = false; }
    }
}
