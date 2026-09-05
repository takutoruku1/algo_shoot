using Godot;

// MinaRoot : FINAL「穢れたわたし」のルート（MinaBattle.tscn にアタッチ）。
// ミナの内側＝穢れに沈んだ暗い心象世界。自機（素の光）/Hud/StageMina を生成。
public partial class MinaRoot : Node2D
{
    public const int ScreenWidth = 384;
    public const int ScreenHeight = 216;

    public Player Player { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public StageMina Stage { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;

    private readonly RetryHold _retry = new();
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

        // 自機＝「素の光」（F1 の `> control: operator / upgrades: none`）。案C では少年は居ないので
        //   boy スキン（shonen_idle）は使わない＝既定のミナ自機のまま、強化なしで潜る。
        //   濁りは掛けない（穢れているのはここ＝彼女の内側であって、潜る光ではない）。
        Player = new Player { Name = "Player" };
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
            // 巡回の層(-95..-88)より奥へ沈める＝ミナ自身のグラデが常に最背面に敷かれ、
            // その上を三人の場所が巡り、最後に層が消えてここへ戻る（＝彼女に着地する）。
            BaseZ = -96,
        };
        AddChild(bg);
        _bg = bg;

        // 巡回用の層背景（案C の各面と同じ char/bg2 の層セットを敷く器）。開幕は層ゼロ＝
        // 上のグラデがそのまま見える。TickJourney が HP 段階で層セットを差し替える。
        var journey = new StageBackground
        {
            Name = "JourneyBackground",
            ForceLayers = true,
            StartInBoss = true,      // FINAL は常にボス中＝巡る先も各面の「ボス時の見え方」で敷く
        };
        AddChild(journey);
        _journeyBg = journey;
    }

    // ───── 追体験：歴代ボス背景の巡り（あかり→こはる→レイ→ミナ）─────
    // 順は面の順＝仮台本 08 F2 の背景巡回と、BREAK ごとの三人の返礼（BossMina.BreakThanks）に合わせる。
    // ミナのHPを削るほど、あなたが通ってきた道が背中側から手繰り寄せられる。BossMina 側の HP 閾値
    // （PatternThresholds 0.82/0.62/0.42/0.22）に相乗りせず、背景側だけで完結させる＝並行編集中の
    // BossMina.cs/StageMina.cs に触らずに済ませる（競合回避）。閾値は 0.80/0.58/0.36/0.16。
    //
    // 巡る先は案C の各面と同じ char/bg2 の層背景（旧 char/bg のピクセル調1枚絵は参照しない）。
    // 各面の「ボス時の見え方」に揃える＝あかり/こはるは Dim（光が消えて沈む）、レイだけ Brighten で
    // BossLayerDefs 相当（枠の全面版＋金の光）を敷く＝舞台が煌々と点く。FINAL は常にボス中なので
    // _dimK は 1 のまま＝敷いた層はそのまま各面のボス時の係数で沈む／点く。
    // 最後の 0.16 で層をすべて落とし、背後のミナ自身の背景（開幕と同じ暗いグラデ）だけが残る
    // ＝旅の終点＝彼女に着地する。
    private StageBackground _bg = null!;
    private StageBackground _journeyBg = null!;
    private int _journey;    // 0:ミナ(開幕) 1:あかり 2:こはる 3:レイ 4:ミナ(着地)

    // あかり（STAGE1 オフィス）: AkariRoot と同じ層セット。雨青の色掛けも合わせる。
    private static readonly Color AkariRainBlue = new Color(0.54f, 0.73f, 1.00f);
    private static readonly BgLayers.Layer[] AkariLayers =
    {
        new BgLayers.Layer("res://char/bg2/stage1/L1_far.png",           0.15f, -95, AkariRainBlue),
        new BgLayers.Layer("res://char/bg2/common/F2_rain_window.png",   0.15f, -94, AkariRainBlue),
        new BgLayers.Layer("res://char/bg2/stage1/L2_mid.png",           0.45f, -92, Colors.White),
        new BgLayers.Layer("res://char/bg2/stage1/L3_near_left.png",     1.00f, -91, Colors.White,
            offset: new Vector2(0f, 407f) * 0.3f),
        new BgLayers.Layer("res://char/bg2/stage1/L3_near_right.png",    1.00f, -91, Colors.White,
            offset: new Vector2(1057f, 566f) * 0.3f),
        new BgLayers.Layer("res://char/bg2/stage1/L4_light_monitor.png", 0f,    -88, Colors.White, additive: true),
        new BgLayers.Layer("res://char/bg2/stage1/L4_light_window.png",  0f,    -88, Colors.White, additive: true),
    };

    // レイ（STAGE3 配信）: ReiRoot の BossLayerDefs と同じ＝枠の全面版＋金の光。Brighten で敷く。
    private static readonly Color ReiDeepViolet = new Color(0.52f, 0.46f, 0.90f);
    private static readonly BgLayers.Layer[] ReiBossLayers =
    {
        new BgLayers.Layer("res://char/bg2/stage3/L1_far.png",          0.15f, -95, ReiDeepViolet),
        new BgLayers.Layer("res://char/bg2/stage3/L2_frame_full.png",   0.45f, -92, Colors.White),
        new BgLayers.Layer("res://char/bg2/stage3/L3_near_left.png",    1.00f, -91, Colors.White),
        new BgLayers.Layer("res://char/bg2/stage3/L3_near_right.png",   1.00f, -91, Colors.White),
        new BgLayers.Layer("res://char/bg2/stage3/L4_light_screen.png", 0f,    -88, Colors.White, additive: true),
        new BgLayers.Layer("res://char/bg2/stage3/L4_light_ring.png",   0f,    -88, Colors.White, additive: true),
        new BgLayers.Layer("res://char/bg2/stage3/L4_light_gold.png",   0f,    -88,
            new Color(1f, 1f, 1f, 0.50f), additive: true),
    };

    // 巡回表：HP 閾値 → その面の層セットとボス時の見え方。こはるは KoharuRoot の道中Aと同じ部屋の層
    // （ボス戦もこの場所のまま沈む面）を借りる＝定義を二重に持たない。
    private static readonly (float hp, BgLayers.BossBehavior onBoss, BgLayers.Layer[] defs)[] Journey =
    {
        (0.80f, BgLayers.BossBehavior.Dim,      AkariLayers),            // STAGE1 オフィス
        (0.58f, BgLayers.BossBehavior.Dim,      KoharuRoot.RoomLayers),  // STAGE2 部屋
        (0.36f, BgLayers.BossBehavior.Brighten, ReiBossLayers),          // STAGE3 配信＝煌々と点く
    };
    private const float JourneyHomeHp = 0.16f; // ここでミナ自身の背景へ着地

    private void TickJourney()
    {
        if (_journeyBg == null) return;
        // BossMina は StageMina が private に握るので、"enemies" group から拾う（StageMina に触らない＝競合回避）。
        BossMina? m = null;
        foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is BossMina bm) { m = bm; break; }
        if (m == null || !IsInstanceValid(m)) return;
        float hp = m.HpRatio;
        if (_journey < Journey.Length && hp <= Journey[_journey].hp)
        {
            var (_, onBoss, defs) = Journey[_journey];
            _journey++;
            _journeyBg.CrossfadeLayersToBoss(onBoss, defs, 2.6f);
        }
        else if (_journey == Journey.Length && hp <= JourneyHomeHp)
        {
            _journey++;
            // 着地はゆっくり（3.4s）＝旅の終わりに時間を掛ける。層が消え、ミナのグラデだけが残る。
            _journeyBg.FadeOutLayers(3.4f);
        }
    }

    public override void _Process(double delta)
    {
        TickJourney(); // 歴代ボス背景の追体験（ミナのHP段階でクロスフェード）。ポーズ中も止めない＝フェードが凍らない。
        // ポーズメニュー等を閉じた押下の漏れ（B=抜ける 等）がこのフレームに誤発火しないよう食う。
        if (Pad.UiBlocked(this)) { _exitHeld = true; return; }

        // R 長押し(0.45s)でリトライ（即発は誤爆しやすい週次PT指摘→長押し化。ゲームオーバー中は即発）。
        // パッドの Start はポーズメニューと衝突するため廃止＝メニュー内「さいしょからやりなおす」を使う。
        bool gameOver = (Player?.Lives ?? 1) <= 0;
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R), instant: gameOver))
        {
            // FINAL はチェックポイント対象外：StageMina._Ready() は SelectedEntry を一切参照せず、
            // 道中は無い（導入4行→BossMina出現→ボス戦のみ、SetStageTarget(1)）。ReloadCurrentScene() が
            // 既にボス直行と同義なので、R：ボスから／Shift+R：最初から の分岐は不要（意味が同じになる）。
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
        else { Hud?.ShowGameOverPrompt(""); _exitHeld = false; }
    }
}
