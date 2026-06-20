using Godot;

// MidEnemy : 道中ザコの汎用版。テクスチャパスと挙動パラメータを EnemySpec で受け取り、
// ステージごとに「心象世界」の姿・撃ち方を出し分ける（6個のサブクラスを量産しない）。
// 発射はパネルではなく本体が一括で行い、_spec.Pattern で種ごとの固有弾幕を撃ち分ける。
// 弾数=Dn(基準値)、間隔=Di(基準値) で必ず難易度スケールし、弾速は素の基準値（BulletSpeedMul が自動で乗る）。
// 進入中（_campX に着いて居座るまで）は撃たない＝画面外からの理不尽撃ちを防ぐ。
public partial class MidEnemy : Enemy
{
    private EnemySpec _spec;
    private double _swayT;
    private float _baseY;
    private bool _baseYSet;
    private float _campX;   // この位置まで来たら居座る（GlyphMote/PageShard と同じ作法）
    private float _vy;      // 居座り中の上下往復（うねらない種でもゆっくり動かす）
    private bool _camped;   // _campX に到達して居座り開始したか（これ以降のみ発射可）

    // 発射タイマー（居座り後に駆動）。バースト等のサブ状態もここで管理する。
    private double _fireT;
    private int _burstLeft;     // ロックオン連射の残り発数
    private double _burstT;     // バースト内の小間隔タイマー
    private Vector2 _burstDir;  // バースト方向（予告で固定した自機方向）
    private double _telegraphT;  // 予告中の残り秒（>0 で予告表示中＝まだ撃たない）

    // Spawner から AddChild 前に呼ぶ（OnEnemy Ready/_Ready より先に値を渡しておく）。
    public void Configure(in EnemySpec spec) => _spec = spec;

    protected override void OnEnemyReady()
    {
        Points = _spec.Points;
        BodyRadius = _spec.BodyRadius;
        PanelCount = 3;
        PanelInk = 2;
        OrbitRadius = 14f;
        SpinSpeed = _spec.SpinSpeed;
        PanelsFire = false; // 発射は本体へ移管。パネルは盾専念。
        PanelFireInterval = _spec.FireInterval;
        EnemyBulletSpeed = _spec.BulletSpeed;

        PreTexPath = _spec.PreTexPath;
        PostTexPath = _spec.PostTexPath;
        PanelTexPath = "res://char/panel_anti.png"; // 吹き出しは既存流用
        BodyDisplayH = 28f;

        // GlyphMote/PageShard と同じく、進入後は画面内に居座る（倒すまで去らない）。
        // これが無いと、道中ザコがパネルを剥がし切る前に左へ抜けてしまい「攻撃が通らない／無敵」に見える。
        _campX = GD.Randf() * 150f + 120f;                 // 120〜270 のどこかに陣取る
        _vy = (GD.Randf() < 0.5f ? -1f : 1f) * 14f;

        // 発射タイミングを種ごとにばらして同時斉射を避ける。
        _fireT = GD.Randf() * 0.8f;

        // 種ごとの弾形・色を1回だけ設定（FireBullet がこれを反映）。
        ApplySpellVisual();
    }

    // パターンごとの弾形・色を確定（spec 仕様の弾形・色）。
    private void ApplySpellVisual()
    {
        switch (_spec.Pattern)
        {
            case AttackPattern.ReiLockBurst:
                SetSpellVisual(BulletShape.Diamond, new Color(0.85f, 0.55f, 0.80f)); break;
            case AttackPattern.ReiPulseRing:
                SetSpellVisual(BulletShape.Ring, new Color(0.80f, 0.45f, 0.62f)); break;
            case AttackPattern.AkariScatter:
                SetSpellVisual(BulletShape.Orb, EnemyKegare); break; // 既定穢れ色
            case AttackPattern.AkariDrop:
                SetSpellVisual(BulletShape.Rice, new Color(0.72f, 0.62f, 0.85f)); break;
            case AttackPattern.KoharuSharp3:
                SetSpellVisual(BulletShape.Needle, new Color(0.95f, 0.50f, 0.70f)); break;
            case AttackPattern.KoharuSimmer:
                SetSpellVisual(BulletShape.Orb, new Color(0.88f, 0.55f, 0.45f)); break;
            case AttackPattern.DefaultAim:
                SetSpellVisual(BulletShape.Orb, EnemyKegare); break;
        }
    }
    // 既定の穢れ色（Bullet.EnemyMid #e072ac 相当）。Orb 種はこれで撒く。
    private static readonly Color EnemyKegare = new Color(0.882f, 0.447f, 0.675f);

    // 左へ進入 → _campX に着いたら居座る。倒すまで画面外へ出ない（攻撃を当てる時間を確保）。
    // SwayAmp>0 の種は居座り中の上下動に“うねり”を載せて種ごとの差を残す。
    protected override void UpdateMovement(double delta)
    {
        // 基準Y（うねりの中心）は最初の移動フレームで確定（GlobalPosition は AddChild 後に設定されるため）。
        if (!_baseYSet) { _baseY = GlobalPosition.Y; _baseYSet = true; }

        float dt = (float)delta;
        // 進入：_campX までは左へ進む。居座る前は撃たない（_camped=false のまま）。
        if (GlobalPosition.X > _campX)
        {
            GlobalPosition += new Vector2(-_spec.MoveSpeed * dt, 0f);
            return;
        }
        _camped = true; // 居座り開始＝以降は発射ロジックが動く

        // 居座り：_campX に留まり、上下にゆっくり往復（画面外へ出ない）。
        float ny = GlobalPosition.Y + _vy * dt;
        if (ny < 28f || ny > 188f) { _vy = -_vy; ny = Mathf.Clamp(ny, 28f, 188f); }
        // SwayAmp>0 の種は往復に小さなうねりを重ねて単調さを消す。
        if (_spec.SwayAmp > 0f)
        {
            _swayT += delta;
            ny += Mathf.Sin((float)_swayT * _spec.SwayFreq) * (_spec.SwayAmp * 0.4f);
            ny = Mathf.Clamp(ny, 28f, 188f);
        }
        GlobalPosition = new Vector2(_campX, ny);

        // 居座っている間だけ固有弾幕を駆動。会話中(BubblePaused)は Enemy._PhysicsProcess が
        // UpdateMovement を呼ばないため、ここに来る時点で攻撃してよい状態。
        TickFire(delta);
    }

    // ─── 固有弾幕の駆動 ───
    private void TickFire(double delta)
    {
        if (_spec.Pattern == AttackPattern.None) return;

        // 予告中：照準/合図を出して撃たずに待つ。
        if (_telegraphT > 0)
        {
            _telegraphT -= delta;
            if (_telegraphT <= 0) FireAfterTelegraph();
            return;
        }

        // ロックオン連射のバースト消化中（予告後の3連）。
        if (_burstLeft > 0)
        {
            _burstT -= delta;
            if (_burstT <= 0)
            {
                FireBurstShot();
                _burstLeft--;
                _burstT = 0.08; // バースト内 0.08s 間隔
            }
            return;
        }

        _fireT += delta;
        switch (_spec.Pattern)
        {
            case AttackPattern.ReiLockBurst:  if (_fireT >= Di(2.2)) { _fireT = 0; BeginLockBurst(); }   break;
            case AttackPattern.ReiPulseRing:  if (_fireT >= Di(3.6)) { _fireT = 0; FirePulseRing(); }    break;
            case AttackPattern.AkariScatter:  if (_fireT >= Di(2.0)) { _fireT = 0; FireScatter(); }      break;
            case AttackPattern.AkariDrop:     if (_fireT >= Di(3.2)) { _fireT = 0; FireDrop(); }         break;
            case AttackPattern.KoharuSharp3:  if (_fireT >= Di(1.6)) { _fireT = 0; BeginSharp3(); }      break;
            case AttackPattern.KoharuSimmer:  if (_fireT >= Di(3.0)) { _fireT = 0; FireSimmer(); }       break;
            case AttackPattern.DefaultAim:    if (_fireT >= Di(1.9)) { _fireT = 0; FireDefaultAim(); }   break;
        }
    }

    private BulletPool? Pool => GetNodeOrNull<BulletPool>("/root/Pool");

    // 自機への向き（居なければ左向き）。
    private Vector2 AimDir()
    {
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Node2D pl)
        {
            var d = pl.GlobalPosition - GlobalPosition;
            if (d.LengthSquared() > 0.01f) return d.Normalized();
        }
        return new Vector2(-1, 0);
    }

    private static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = Mathf.DegToRad(deg), cs = Mathf.Cos(r), sn = Mathf.Sin(r);
        return new Vector2(v.X * cs - v.Y * sn, v.X * sn + v.Y * cs);
    }

    // ── レイ shooter：ロックオン連射 ──
    // 0.5s 照準予告（自機へ細線）→ その方向に 3連バースト(0.08s間隔, ±4°拡散) → セット間隔 Di(2.2) 休む。
    private void BeginLockBurst()
    {
        _burstDir = AimDir();
        _telegraphT = 0.5;
        FxLayer.Instance?.AimLine(GlobalPosition, _burstDir, 0.5f, new Color(0.85f, 0.55f, 0.80f));
    }
    private void FireAfterTelegraph()
    {
        // 予告終了。パターン別に本射へ。
        if (_spec.Pattern == AttackPattern.ReiLockBurst)
        {
            _burstLeft = Mathf.Max(1, Dn(3)); // 1セット=Dn(3)発、最低1発保証
            _burstT = 0;
        }
        else if (_spec.Pattern == AttackPattern.KoharuSharp3)
        {
            FireSharp3();
        }
    }
    private void FireBurstShot()
    {
        var pool = Pool; if (pool == null) return;
        float spread = (float)GD.RandRange(-4.0, 4.0); // ±4°拡散
        FireBullet(pool, GlobalPosition, Rotate(_burstDir, spread) * 110f, 3.0f, 1);
    }

    // ── レイ drifter：視線パルス放射 ── 全方位 Dn(6) 発の等間隔リング1回。予告なし。
    private void FirePulseRing()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(6));
        for (int i = 0; i < n; i++)
        {
            float deg = 360f * i / n;
            FireBullet(pool, GlobalPosition, Rotate(new Vector2(1, 0), deg) * 55f, 3.2f, 1);
        }
    }

    // ── あかり shooter：ばらまき投擲 ── 固定左(180°)±35°扇に Dn(5)way、各弾±5°ゆらぎ。予告なし。
    private void FireScatter()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(5));
        var baseDir = new Vector2(-1, 0); // 左180°中心
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0.5f : i / (float)(n - 1);
            float deg = Mathf.Lerp(-35f, 35f, t) + (float)GD.RandRange(-5.0, 5.0);
            FireBullet(pool, GlobalPosition, Rotate(baseDir, deg) * 80f, 3.2f, 1);
        }
    }

    // ── あかり drifter：落書きドロップ ── 真下(90°)±20°へ低速 Dn(3)way。うねり維持・予告なし。
    private void FireDrop()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(3));
        var down = new Vector2(0, 1); // 真下90°
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0.5f : i / (float)(n - 1);
            float deg = Mathf.Lerp(-20f, 20f, t);
            FireBullet(pool, GlobalPosition, Rotate(down, deg) * 50f, 3.0f, 1);
        }
    }

    // ── こはる shooter：高速鋭3WAY ── 0.4s 短予告(本体が一瞬縮む＋小白フラッシュ)→自機方向±12°の3本。
    private void BeginSharp3()
    {
        _telegraphT = 0.4;
        FxLayer.Instance?.AimFlash(GlobalPosition, new Color(0.95f, 0.50f, 0.70f));
        SquishBody(); // 本体が一瞬縮む（予告の溜め）
    }
    private void FireSharp3()
    {
        var pool = Pool; if (pool == null) return;
        var dir = AimDir();
        // 本数は固定3本（Dn(3) は最低3本の密度目安＝固定本数なのでスケールしない）。扇幅24°（±12°）。
        foreach (float deg in new[] { -12f, 0f, 12f })
            FireBullet(pool, GlobalPosition, Rotate(dir, deg) * 130f, 2.8f, 1);
    }

    // ── こはる drifter：とろ火ゆらぎ弾 ── 自機狙い超低速 Dn(1) 単発。うねり維持・予告なし。
    private void FireSimmer()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(1));
        var dir = AimDir();
        for (int i = 0; i < n; i++)
        {
            float jitter = n == 1 ? 0f : (float)GD.RandRange(-6.0, 6.0);
            FireBullet(pool, GlobalPosition, Rotate(dir, jitter) * 45f, 3.4f, 1);
        }
    }

    // ── Default(アンチくん)：自機狙い単発（現状踏襲を本体一括へ移しただけ）──
    private void FireDefaultAim()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(1));
        var dir = AimDir();
        for (int i = 0; i < n; i++)
            FireBullet(pool, GlobalPosition, dir * 90f, 3.0f, 1);
    }

    // こはる短予告の“溜め”：立ち絵を一瞬縮める（当たり判定は不変）。予告終了(0.4s)で元へ戻す。
    private void SquishBody()
    {
        var spr = GetNodeOrNull<Sprite2D>("Body");
        if (spr == null) return;
        var s = spr.Scale;
        spr.Scale = new Vector2(s.X * 0.82f, s.Y * 0.82f);
        var t = CreateTween();
        t.TweenProperty(spr, "scale", s, 0.18).SetDelay(0.4);
    }
}
