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

    // 道中の“波ごとの圧”を変える起点。0=ふつうに緩く立ち上がる、1=最初から最大密度。
    // 道中を三部構成にして「後半ほど詰めてくる」緩急を作るため、後続の波で上げて渡す（§3 緩急）。
    public float StartIntensity = 0f;

    private const float SpawnX = 398f;   // 画面右外
    private const float RampDur = 28f;    // この秒数で最大密度に（道中を“密度の変化”で見せる：60→28で立ち上がりを早く）
    private const float IntervalStart = 2.0f;
    private const float IntervalEnd = 0.8f;
    private const int MaxAlive = 10;      // 同時出現の上限

    private double _t;   // Begin からの経過（StartIntensity ぶん前倒しした実効時間）
    private double _cd;  // 次の出現までの残り
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    public override void _Ready() => _rng.Randomize();

    public void Begin()
    {
        Active = true;
        // 後半の波は StartIntensity ぶんランプを前倒し＝最初からやや詰まった圧で始める。
        float si = Mathf.Clamp(StartIntensity, 0f, 1f);
        _t = si * RampDur;
        _cd = Mathf.Lerp(0.8f, 0.4f, si);
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
        Vector2 pos;
        if (Theme == StageTheme.Default)
        {
            // 既存挙動はそのまま（チュートリアル等の見た目・挙動を一切変えない）＝右から左進。
            e = drifter ? new PageShard() : new GlyphMote();
            pos = new Vector2(SpawnX, y);
        }
        else
        {
            var (shooter, drift) = EnemyTable.For(Theme);
            var me = new MidEnemy();
            me.Configure(drifter ? drift : shooter);
            // 出現エッジを散らす：右60% / 右上20% / 右下20%。各エッジから場内の居座り点へ進入する。
            int edge = _rng.Randf() < 0.6f ? 0 : (_rng.Randf() < 0.5f ? 1 : 2);
            Vector2 camp;
            switch (edge)
            {
                case 1: // 画面上部・右上から下りてくる
                    pos = new Vector2(_rng.RandfRange(230f, 360f), -12f);
                    camp = new Vector2(_rng.RandfRange(180f, 300f), _rng.RandfRange(55f, 110f));
                    break;
                case 2: // 画面下部・右下から上ってくる
                    pos = new Vector2(_rng.RandfRange(324f, 374f), 228f);
                    camp = new Vector2(_rng.RandfRange(120f, 240f), _rng.RandfRange(110f, 165f));
                    break;
                default: // 右から（従来）
                    pos = new Vector2(SpawnX, y);
                    camp = new Vector2(_rng.RandfRange(150f, 280f), y);
                    break;
            }
            me.SetEntry(camp);
            e = me;
        }
        World.AddChild(e);
        e.GlobalPosition = pos;
    }
}
