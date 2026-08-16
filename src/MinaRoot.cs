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
        _bg = bg;
        _minaTex = tex;
    }

    // ───── 追体験：歴代ボス背景の巡り（レイ→あかり→こはる→ミナ）─────
    // ミナのHPを削るほど、少年が通ってきた道が背中側から手繰り寄せられる。BossMina 側の HP 閾値
    // （PatternThresholds 0.82/0.62/0.42/0.22）に相乗りせず、背景側だけで完結させる＝並行編集中の
    // BossMina.cs/StageMina.cs に触らずに済ませる（競合回避）。閾値は 0.80/0.58/0.36/0.16。
    //
    // 視認性：歴代背景は道中用で明るいので、Dim で暗め・低コントラストに沈めて敷く（α=1、RGB を
    // 0.30 前後に落とす）。深い紫寄りの敷き色で、ミナの心象世界の色から浮かせない。
    // 最後の 0.16 でミナ自身の背景（開幕と同じ暗いグラデ）へ戻る＝旅が終点＝彼女に着地する。
    private StageBackground _bg = null!;
    private Texture2D _minaTex = null!;
    private int _journey;    // 0:ミナ(開幕) 1:レイ 2:あかり 3:こはる 4:ミナ(着地)
    private static readonly (float hp, string path, Color dim)[] Journey =
    {
        (0.80f, "res://char/bg/rei/boss.png",         new Color(0.30f, 0.33f, 0.44f, 1f)), // 盤面＝青灰。冷たく
        (0.58f, "res://char/bg/akari/classroom.png",  new Color(0.36f, 0.30f, 0.34f, 1f)), // 教室＝くすんだ桃
        (0.36f, "res://char/bg/koharu/kitchen.png",   new Color(0.34f, 0.31f, 0.26f, 1f)), // 台所＝沈んだ琥珀
    };
    private const float JourneyHomeHp = 0.16f; // ここでミナ自身の背景へ着地

    private void TickJourney()
    {
        if (_bg == null) return;
        // BossMina は StageMina が private に握るので、"enemies" group から拾う（StageMina に触らない＝競合回避）。
        BossMina? m = null;
        foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is BossMina bm) { m = bm; break; }
        if (m == null || !IsInstanceValid(m)) return;
        float hp = m.HpRatio;
        if (_journey < Journey.Length && hp <= Journey[_journey].hp)
        {
            var (_, path, dim) = Journey[_journey];
            _journey++;
            _bg.CrossfadeBossTo(path, dim, 2.6f);
        }
        else if (_journey == Journey.Length && hp <= JourneyHomeHp)
        {
            _journey++;
            // 着地はゆっくり（3.4s）＝旅の終わりに時間を掛ける。
            _bg.CrossfadeBossTo(_minaTex, Colors.White, 3.4f);
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
