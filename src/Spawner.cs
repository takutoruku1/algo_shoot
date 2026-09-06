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
    private const int MaxAliveFallback = 8; // 同時出現の上限（GameManager が取れないときの既定＝Normal相当）

    // ── 第4種：回り込みザコ「引用リプ」（FlankAim）の調整値（C-1・左端張り付き対策）──
    // 右から出現→上下端を走行→自機の後方(x≈FlankCampX)に着座→右向き低速単発。
    // 進入経路が丸見え＋弾は低速なので理不尽ではない（走行中は撃たない＝MidEnemy の進入仕様）。
    private const float FlankRate = 0.15f;       // テーマ湧きのうちこの割合で出現
    private const float FlankRampGate = 0.5f;    // ランプ後半（進行度>=50%）のみ出現＝序盤は出さない
    private const float FlankCampX = 40f;        // 着座X＝自機後方（左端近傍）
    private const float FlankRunTopY = 16f;      // 上端走行レーンY
    private const float FlankRunBottomY = 200f;  // 下端走行レーンY
    private const float FlankCampTopY = 64f;     // 上から回った個体の着座Y
    private const float FlankCampBottomY = 152f; // 下から回った個体の着座Y

    // ── 盾もち種「バズ壁」（BuzzWall）の調整値（全テーマ・波B/C限定）──
    // 撃たない・遅い・硬い（パネル5枚×インク3）＝剥がし切るDPSチェックで優先順位判断を生む
    //（拡散/ホーミング/貫通の使い所）。序盤A波には出さない＝覚えることを増やしすぎない。
    private const float BuzzWallRate = 0.10f;         // テーマ湧きのうちこの割合で出現
    private const float BuzzWallMinIntensity = 0.3f;  // StartIntensity がこれ以上＝波B(0.35)/C(0.7)のみ
    // ── 祈り運び種（KoharuPrayerCarry・こはる面専用）の調整値 ──
    // 消せる祈り弾を3発ぶら下げて横断するボーナス種＝ボス戦「お残し禁止」の練習台。
    private const float PrayerCarrierRate = 0.10f;    // こはるテーマ湧きのうちこの割合で出現

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

        // 過密なら少し待つ（上限は難易度別＝Easy6 / Normal8 / Hard10 / Lunatic12）
        int maxAlive = game?.MaxAliveEnemies ?? MaxAliveFallback;
        if (GetTree().GetNodesInGroup("enemies").Count >= maxAlive) { _cd = 0.3; return; }

        SpawnOne();

        float ramp = Mathf.Clamp((float)_t / RampDur, 0f, 1f);
        // 難易度別の間隔倍率（Easy1.3 / Normal1.0 / Hard0.78 / Lunatic0.62）。基準の 2.0→0.8 と
        // 最大密度到達 28 秒は据え置きで、難しいほど同じランプを詰めて湧かす（2026-09-06）。
        float interval = Mathf.Lerp(IntervalStart, IntervalEnd, ramp) * (game?.SpawnIntervalMul ?? 1f);
        // 前のめり密度：自機が右へ寄る（攻める）ほど倍率が高い＝間隔を縮めて多く湧かす（除算）。
        // 同時上限は上の maxAlive で難易度別に頭打ち（圧殺防止）。GameManager.PlayerNormX は各 Root の TickProgress が毎フレーム更新。
        float spawnMul = game != null ? GameManager.SpawnRateMul(game.PlayerNormX) : 1f;
        _cd = interval * _rng.RandfRange(0.8f, 1.2f) / spawnMul;
    }

    private void SpawnOne()
    {
        float y = _rng.RandfRange(46f, 172f);
        bool drifter = _rng.Randf() < 0.25f; // 25%でうつむきさん(PageShard)、75%でアンチくん(GlyphMote)。どちらも弾は撃たず、パネルの盾で押し返す点は共通。

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
            var me = new MidEnemy();
            float ramp = Mathf.Clamp((float)_t / RampDur, 0f, 1f);
            // 第4種：回り込み「引用リプ」。ランプ後半のみ FlankRate で湧く（全テーマ共通・スキンは撃つ種を流用）。
            // 左端に張り付く自機の背後から“読める形”で圧をかける＝左端の安置化を構造的に崩す。
            if (ramp >= FlankRampGate && _rng.Randf() < FlankRate)
            {
                me.Configure(EnemyTable.Flanker(Theme));
                bool top = _rng.Randf() < 0.5f;
                float runY = top ? FlankRunTopY : FlankRunBottomY;
                pos = new Vector2(SpawnX, runY);
                // 経由点＝走行レーン終端（端を走り切る）→ 着座点＝自機後方。2区間の直進で経路が読める。
                me.SetFlankEntry(new Vector2(FlankCampX, runY),
                    new Vector2(FlankCampX, top ? FlankCampTopY : FlankCampBottomY));
            }
            // 盾もち「バズ壁」：波B/C（StartIntensity>=0.3）のみ。右から出て場の中ほどに陣取る壁。
            else if (StartIntensity >= BuzzWallMinIntensity && _rng.Randf() < BuzzWallRate)
            {
                me.Configure(EnemyTable.BuzzWall(Theme));
                pos = new Vector2(SpawnX, y);
                me.SetEntry(new Vector2(_rng.RandfRange(160f, 260f), y)); // 中列に居座って射線を塞ぐ
            }
            // 祈り運び：こはる面の道中のみ。ぶら下げた祈り弾ごと左へ横断（居座らない＝SetEntry不要）。
            else if (Theme == StageTheme.Koharu && _rng.Randf() < PrayerCarrierRate)
            {
                me.Configure(EnemyTable.PrayerCarrier());
                // ぶら下げ弾（最大+37px＋振れ）が画面下端216pxを割らないYで横断させる。
                pos = new Vector2(SpawnX, _rng.RandfRange(60f, 150f));
            }
            else
            {
                var (shooter, drift) = EnemyTable.For(Theme);
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
            }
            e = me;
        }
        World.AddChild(e);
        e.GlobalPosition = pos;
    }
}
