using Godot;

// Spawner : ステージ本編の連続スポナー。
// 右端から敵を一定間隔で湧かせ、経過時間とともに密度を上げる（押し引きの圧）。
// 浄化ゲージが目標到達(StageCleared)で自動停止。会話中(BubblePaused)は湧かせない。
// 過密防止に同時出現数の上限を設ける。
public partial class Spawner : Node
{
    public Node2D World = null!;
    // ステージの「心象世界」テーマ。湧くザコ2種の絵・挙動をここで切り替える。
    // 既定は Default（既存アンチくん/うつむきさん）＝StageW0 等は従来どおり。
    public StageTheme Theme = StageTheme.Default;
    public bool Active { get; private set; }

    private const float SpawnX = 398f;   // 画面右外
    private const float RampDur = 60f;    // この秒数で最大密度に
    private const float IntervalStart = 2.0f;
    private const float IntervalEnd = 0.9f;
    private const int MaxAlive = 9;       // 同時出現の上限

    private double _t;   // Begin からの経過
    private double _cd;  // 次の出現までの残り
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    public override void _Ready() => _rng.Randomize();

    public void Begin()
    {
        Active = true;
        _t = 0;
        _cd = 0.8;
    }

    public void Stop() => Active = false;

    public override void _Process(double delta)
    {
        if (!Active) return;
        if (Hud.BubblePaused) return; // 会話中は襲ってこない＝湧かせない

        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game != null && game.StageCleared) { Active = false; return; }

        _t += delta;
        _cd -= delta;
        if (_cd > 0) return;

        // 過密なら少し待つ
        if (GetTree().GetNodesInGroup("enemies").Count >= MaxAlive) { _cd = 0.3; return; }

        SpawnOne();

        float ramp = Mathf.Clamp((float)_t / RampDur, 0f, 1f);
        float interval = Mathf.Lerp(IntervalStart, IntervalEnd, ramp);
        _cd = interval * _rng.RandfRange(0.8f, 1.2f);
    }

    private void SpawnOne()
    {
        float y = _rng.RandfRange(46f, 172f);
        bool drifter = _rng.Randf() < 0.25f; // 25%で撃たない種、75%で撃つ種

        Enemy e;
        if (Theme == StageTheme.Default)
        {
            // 既存挙動はそのまま（チュートリアル等の見た目・挙動を一切変えない）。
            e = drifter ? new PageShard() : new GlyphMote();
        }
        else
        {
            var (shooter, drift) = EnemyTable.For(Theme);
            var me = new MidEnemy();
            me.Configure(drifter ? drift : shooter);
            e = me;
        }
        World.AddChild(e);
        e.GlobalPosition = new Vector2(SpawnX, y);
    }
}
