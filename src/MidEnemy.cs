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
    private bool _camped;   // 居座り点に到達して居座り開始したか（これ以降のみ発射可）
    // 居座り目標点（Spawner が出現エッジに応じて指定。未指定なら右からの直進フォールバック）。
    private Vector2 _campTarget;
    private bool _entryConfigured;
    // Spawner から AddChild 前に呼ぶ：場内のどこへ進入して居座るか。
    public void SetEntry(Vector2 campTarget) { _campTarget = campTarget; _entryConfigured = true; }

    // 発射タイマー（居座り後に駆動）。バースト等のサブ状態もここで管理する。
    private double _fireT;
    private int _burstLeft;     // ロックオン連射の残り発数
    private double _burstT;     // バースト内の小間隔タイマー
    private Vector2 _burstDir;  // バースト方向（予告で固定した自機方向）
    private double _telegraphT;  // 予告中の残り秒（>0 で予告表示中＝まだ撃たない）

    // 進入が遅すぎると居座る前に浄化されて「撃たずに去る」ので、進入だけ最低速度を保証する
    // （居座り後の上下うねり＝種の性格は据え置き）。居座った瞬間に初弾を素早く撃つ＝設置→即攻撃。
    private const float ApproachFloor = 78f;     // 進入の最低速度(px/s)。遅い種でも約2.5sで居座る。
    private const double FirstShotDelay = 0.55;  // 居座り後この秒で初弾（撃つ前に倒され続けるのを防ぐ）。

    // パターン別の基準発射間隔（秒・難易度スケール前）。Di() を掛けて使う。初弾プライムと共用。
    private double BaseInterval() => _spec.Pattern switch
    {
        AttackPattern.ReiLockBurst => 2.2,
        AttackPattern.ReiPulseRing => 3.6,
        AttackPattern.AkariScatter => 2.0,
        AttackPattern.AkariDrop    => 3.2,
        AttackPattern.KoharuSharp3 => 1.6,
        AttackPattern.KoharuSimmer => 3.0,
        AttackPattern.DefaultAim   => 1.9,
        _ => 999.0,
    };

    // ─── “自走している生命感”モーション（吉田演出：1枚絵をtransformだけで生かす）───
    // _bodySprite(=子ノード"Body")の Position/Rotation/Scale だけを毎フレーム合成する。
    // 当たり判定（本体 Area2D／_bodyShape）と進行ロジック・SwayAmp は一切触らない（純・装飾）。
    // 種別＝_spec.Pattern からモチーフを判別し、特徴的な周期モーションを与える。
    private LivingMotion _motion;     // モチーフ別の動きの型（OnEnemyReady で確定）
    private float _motionPhase;       // 個体ごとの位相ずらし（群れの同期を防ぐ）
    private double _motionT;          // 生命感モーションの経過秒（_swayT とは独立に積む）
    private Sprite2D? _body;          // 子ノード"Body"のキャッシュ（毎フレーム探さない）
    private float _bodyBaseScale = 1f;// _bodySprite の素のスケール（Scale演出はこれに係数を掛ける）
    private float _gaze;              // 監視カメラの“ギョロッ”用：目標ヨー(rad)。たまに切り替える
    private double _gazeT;            // 次に視線を変えるまでの残り秒
    private float _spin;             // こはる包丁の“クルッ”用：余韻回転(rad)。撃つ瞬間に蹴って減衰
    private float _kick;             // 予告/発射の溜め：一瞬の縦スカッシュ量（0で平常、減衰）

    // 1枚絵の transform で“生きてる感”を出す周期モーションの型（種＝モチーフごと）。
    private enum LivingMotion { None, ReiDrone, ReiEye, AkariDesk, AkariNote, KoharuKnife, KoharuPot }

    // Spawner から AddChild 前に呼ぶ（OnEnemy Ready/_Ready より先に値を渡しておく）。
    public void Configure(in EnemySpec spec) => _spec = spec;

    protected override void OnEnemyReady()
    {
        Points = _spec.Points;
        BodyRadius = _spec.BodyRadius * 0.85f; // 一回り小さく
        PanelCount = 3;
        PanelInk = 2;
        OrbitRadius = 11.5f;            // 一回り小さく（周回をやや内側へ）
        PanelDisplayScale = 0.82f;      // パネル絵＆当たりを縮小
        SpinSpeed = _spec.SpinSpeed;
        PanelsFire = false; // 発射は本体へ移管。パネルは盾専念。
        PanelFireInterval = _spec.FireInterval;
        EnemyBulletSpeed = _spec.BulletSpeed;

        PreTexPath = _spec.PreTexPath;
        PostTexPath = _spec.PostTexPath;
        PanelTexPath = "res://char/panel_anti.png"; // 吹き出しは既存流用
        BodyDisplayH = 23f;             // 一回り小さく

        // GlyphMote/PageShard と同じく、進入後は画面内に居座る（倒すまで去らない）。
        // これが無いと、道中ザコがパネルを剥がし切る前に左へ抜けてしまい「攻撃が通らない／無敵」に見える。
        _campX = GD.Randf() * 150f + 120f;                 // 120〜270 のどこかに陣取る
        _vy = (GD.Randf() < 0.5f ? -1f : 1f) * 14f;

        // 発射タイミングを種ごとにばらして同時斉射を避ける。
        _fireT = GD.Randf() * 0.8f;

        // 種ごとの弾形・色を1回だけ設定（FireBullet がこれを反映）。
        ApplySpellVisual();

        // 生命感モーション：モチーフ＝_spec.Pattern から動きの型を決め、位相を個体ごとにずらす。
        _motion = MotionFor(_spec.Pattern);
        _motionPhase = GD.Randf() * Mathf.Tau;   // 0〜2π：群れが同期しないよう全位相を散らす
        _gazeT = GD.RandRange(1.2, 2.6);         // 監視カメラ：最初の視線変更までの間
    }

    // モチーフ別の動きの型を弾幕パターンから引く（種が動きでも見分けられるように1:1）。
    private static LivingMotion MotionFor(AttackPattern p) => p switch
    {
        AttackPattern.ReiLockBurst => LivingMotion.ReiDrone,   // 偵察ドローン
        AttackPattern.ReiPulseRing => LivingMotion.ReiEye,     // 監視カメラの目
        AttackPattern.AkariScatter => LivingMotion.AkariDesk,  // 机・椅子
        AttackPattern.AkariDrop    => LivingMotion.AkariNote,  // ノート・教科書
        AttackPattern.KoharuSharp3 => LivingMotion.KoharuKnife,// 包丁・まな板
        AttackPattern.KoharuSimmer => LivingMotion.KoharuPot,  // 鍋・お玉
        _ => LivingMotion.None,                                 // Default/アンチくん：素のまま
    };

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

        // 進入中も居座り中も“生きてる感”は出す（純・装飾＝当たり判定/進行は不変）。
        TickLivingMotion(delta);

        float dt = (float)delta;
        // 居座る目標点。未指定なら従来どおり「右→_campX へ左進」（=同Yへ水平移動）。
        Vector2 camp = _entryConfigured ? _campTarget : new Vector2(_campX, _baseY);

        // 進入：目標点へ直進。着くまでは撃たない（_camped=false のまま）＝画面外からの理不尽撃ちを防ぐ。
        if (!_camped)
        {
            Vector2 to = camp - GlobalPosition;
            if (to.Length() > 3f)
            {
                // 進入だけ最低速度を保証＝遅い種でも素早く居座って攻撃に移れる。
                float approach = Mathf.Max(_spec.MoveSpeed, ApproachFloor);
                GlobalPosition += to.Normalized() * approach * dt;
                return;
            }
            _camped = true;   // 居座り開始＝以降は発射ロジックが動く
            _baseY = camp.Y;  // 以降の上下往復の中心
            // 居座った瞬間に初弾を素早く（FirstShotDelay 秒後）。出現→即浄化でも一矢報いるように。
            _fireT = Mathf.Max(0.0, Di(BaseInterval()) - FirstShotDelay);
        }

        // 居座り：camp.X に留まり、上下にゆっくり往復（画面外へ出ない）。
        float ny = GlobalPosition.Y + _vy * dt;
        if (ny < 28f || ny > 188f) { _vy = -_vy; ny = Mathf.Clamp(ny, 28f, 188f); }
        // SwayAmp>0 の種は往復に小さなうねりを重ねて単調さを消す。
        if (_spec.SwayAmp > 0f)
        {
            _swayT += delta;
            ny += Mathf.Sin((float)_swayT * _spec.SwayFreq) * (_spec.SwayAmp * 0.4f);
            ny = Mathf.Clamp(ny, 28f, 188f);
        }
        GlobalPosition = new Vector2(camp.X, ny);

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
        if (_fireT < Di(BaseInterval())) return; // 基準間隔（初弾は居座り時にプライム済み）
        _fireT = 0;
        switch (_spec.Pattern)
        {
            case AttackPattern.ReiLockBurst:  BeginLockBurst();  break;
            case AttackPattern.ReiPulseRing:  FirePulseRing();   break;
            case AttackPattern.AkariScatter:  FireScatter();     break;
            case AttackPattern.AkariDrop:     FireDrop();        break;
            case AttackPattern.KoharuSharp3:  BeginSharp3();     break;
            case AttackPattern.KoharuSimmer:  FireSimmer();      break;
            case AttackPattern.DefaultAim:    FireDefaultAim();  break;
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

    // ── レイ shooter：ロックオンビーム ──
    // 自機方向へ予測線（予兆＝薄い危険色ライン・当たり判定なし）を BeamWarn 秒出してから、
    // 実体ビーム（着弾フレームのみ判定ON）を撃つ。予兆中の自機方向で固定＝避けられる必殺。
    // 予兆／実体／被弾は AreaStrike（ボスの範囲技と共用の予測攻撃基盤）に一任する。
    private const double BeamWarn = 0.5;       // 予兆時間の基準秒（Di で難易度伸縮：易しいほど猶予増）。
    private const float BeamLen = 240f;        // 画面を貫く長さ（はみ出しは画面外で見えないだけ）
    private const float BeamHalfThick = 5.5f;  // ビームの半太さ(px)。細長＝判定は線分への最短距離で取る。
    private void BeginLockBurst()
    {
        var dir = AimDir();
        double warn = Di(BeamWarn); // 予兆時間（難易度で伸縮＝易しいほど避ける猶予が増える）。
        var z = new AreaStrike();
        // 予測線/着弾色はボス・レイの予測攻撃（AreaSpellCaster "rei"）と同じ金/明金パレットに統一。
        // ＝道中ドローンの予兆もボス予兆と同系統に見え、危険色の意味（金＝レイの裁き）が一貫する。
        z.ConfigureBeam(dir, BeamLen, BeamHalfThick, warn,
            new Color("e8c45a"), new Color("ffe39a"));
        // World（親）へぶら下げて発射源の Transform に依存させない。ただし発生源（このドローン）を
        // owner に渡し、予兆中に倒されたら予測線ごとキャンセルさせる（倒せば攻撃も消える＝理不尽回避）。
        var host = GetParent() ?? this;
        host.AddChild(z);
        z.SetOwner(this);
        z.GlobalPosition = GlobalPosition; // 発射源＝この瞬間のドローン位置（以降は固定＝避けられる）。
        SquishBody(); // 撃つ前の溜め（縦スカッシュ）で“来る”を本体でも示す。
    }
    private void FireAfterTelegraph()
    {
        // ReiLockBurst のビームは AreaStrike が自前で予兆→着弾するため、ここでは何もしない。
        // KoharuSharp3 のみ MidEnemy 側の短予告を消費して本射する。
        if (_spec.Pattern == AttackPattern.KoharuSharp3)
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
        // 発射の解放に合わせ“クルッ”と一回転の余韻を蹴る（たまに見せる切れ味＝包丁の性格）。
        if (GD.Randf() < 0.5f) _spin = 1f;
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

    // 改心が確定したら生命感モーションは止め、立ち絵の傾き/潰しを素へ戻す
    //（暴れていた姿勢のまま笑顔になると不自然＝改心後は穏やかに着地）。
    // GrantFollower は浄化直後に必ず通る（cry を使わない道中ザコは PostTexPath 差替後すぐここへ）。
    protected override void GrantFollower()
    {
        _motion = LivingMotion.None;
        _spin = 0f; _kick = 0f;
        if (_body != null) _body.Rotation = 0f; // Scale/Position は SwapBody/TickSwapAnim が素へ戻す
        base.GrantFollower();
    }

    // こはる短予告の“溜め”：立ち絵を一瞬ギュッと縮める（当たり判定は不変）。
    // 生命感モーションと所有権を一本化するため、Tween でなく _kick に蹴り込み、
    // TickLivingMotion が縦スカッシュとして毎フレーム合成・減衰させる（予告終了で自然に戻る）。
    private void SquishBody() => _kick = Mathf.Max(_kick, 1f);

    // ─── 生命感モーション本体 ───
    // _bodySprite(子"Body")の Position/Rotation/Scale を毎フレーム“合成”する。
    // 進行（GlobalPosition）も SwayAmp も当たり判定も触らない＝1枚絵に乗っかる装飾レイヤ。
    // 各種パラメータ（px・rad・Hz）はモチーフで性格が動きでも見分けられるよう調律。位相は _motionPhase で個体ずらし。
    private void TickLivingMotion(double delta)
    {
        if (_motion == LivingMotion.None) return;
        if (_body == null)
        {
            _body = GetNodeOrNull<Sprite2D>("Body");
            if (_body == null) return;            // まだ立ち絵が無い（プレースホルダ種）
            _bodyBaseScale = _body.Scale.Y;       // SetupBodySprite が決めた素のスケールを基準に保持
        }

        _motionT += delta;
        float t = (float)_motionT + _motionPhase; // 個体位相込みの時刻（群れの非同期化）
        float dt = (float)delta;

        // 共通の溜め減衰（予告/発射キック）。0へ向けて速やかに抜ける＝予告の“戻し”の余韻。
        if (_kick > 0f) _kick = Mathf.Max(0f, _kick - dt * 3.2f);

        float ox = 0f, oy = 0f;   // 位置オフセット(px)
        float rot = 0f;           // 回転(rad)
        float sx = 1f, sy = 1f;   // スケール係数（基準スケールに掛ける）

        const float Tau = Mathf.Tau;
        switch (_motion)
        {
            // 偵察ドローン：ホバリング。速い小刻み上下ブレ＋進行(左)へわずか前傾＋ローター示唆の速い微小ロール。
            case LivingMotion.ReiDrone:
                oy = Mathf.Sin(t * 7.5f) * 1.4f;                 // 上下ブレ ±1.4px / ~1.2Hz
                rot = -0.05f + Mathf.Sin(t * 11.0f) * 0.035f;    // 前傾 -2.9°＋ローター微ロール ±2°/速い
                break;

            // 監視カメラの目：ふわふわ大きめ浮遊＋たまにギョロッと向きを変える（タメ→戻し）。
            case LivingMotion.ReiEye:
                oy = Mathf.Sin(t * 1.7f) * 2.6f;                 // ゆっくり大きめ上下 ±2.6px / ~0.27Hz
                _gazeT -= delta;
                if (_gazeT <= 0)
                {
                    _gaze = (GD.Randf() < 0.5f ? -1f : 1f) * (float)GD.RandRange(0.16, 0.26); // ±9〜15°
                    _gazeT = GD.RandRange(1.4, 3.0);             // 次のギョロッまでの間
                }
                rot = Mathf.Lerp(_body.Rotation, _gaze, dt * 6.5f); // タメつつ目標ヨーへ寄せ、また戻す
                break;

            // 机・椅子：脚で歩く。左右によじよじ傾く（ロール）＋一歩ごとの上下バウンド（接地リズム）。
            case LivingMotion.AkariDesk:
                rot = Mathf.Sin(t * 3.4f) * 0.10f;               // よじよじロール ±5.7° / ~0.54Hz
                oy = -Mathf.Abs(Mathf.Sin(t * 3.4f)) * 1.8f;     // 一歩ごと（倍周期）に持ち上がるバウンド
                ox = Mathf.Sin(t * 3.4f) * 0.6f;                 // 重心の左右ゆれを少し
                break;

            // ノート・教科書：羽ばたき。Scale.x を周期的に縮め広げ（ページ/羽の開閉）＋ふわっと上下。
            case LivingMotion.AkariNote:
                sx = 1f - 0.12f * (0.5f + 0.5f * Mathf.Sin(t * 6.2f)); // 横幅 1.0→0.88 開閉 / ~1Hz
                sy = 1f + 0.05f * (0.5f + 0.5f * Mathf.Sin(t * 6.2f)); // 閉じる時わずかに縦伸び（紙の張り）
                oy = Mathf.Sin(t * 2.0f) * 2.2f;                 // ふわっと上下 ±2.2px
                break;

            // 包丁・まな板：突進的な小刻み振動（じりじり）＋進行(左)へ鋭い前傾＋たまにクルッと一回転の余韻。
            case LivingMotion.KoharuKnife:
                ox = Mathf.Sin(t * 26.0f) * 0.7f;                // じりじり高速横振動 ±0.7px
                oy = Mathf.Sin(t * 23.0f) * 0.5f;
                rot = -0.12f;                                    // 鋭い前傾 -6.9°（攻め）
                if (_spin > 0f)                                   // 撃つ瞬間に蹴られた“クルッ”を減衰させて余韻に
                {
                    rot += _spin * Tau;                          // 余韻一回転（_spin:1→0）
                    _spin = Mathf.Max(0f, _spin - dt * 1.6f);
                }
                break;

            // 鍋・お玉：ぐつぐつ。上下に小バウンド＋ぷるぷる横揺れ（粘性のある重い揺れ＝低周波＋微振動）。
            case LivingMotion.KoharuPot:
                oy = -Mathf.Abs(Mathf.Sin(t * 2.6f)) * 1.6f      // ぐつっと持ち上がるバウンド
                     + Mathf.Sin(t * 14.0f) * 0.4f;              // 沸騰の微振動を重ねる
                ox = Mathf.Sin(t * 1.5f) * 0.9f;                 // 重い横揺れ（ゆっくり）
                sy = 1f + 0.04f * Mathf.Abs(Mathf.Sin(t * 2.6f));// バウンド頂点でわずかに縦伸び
                break;
        }

        // 予告/発射の溜め：縦に潰す（_kick:1→0）。攻撃の直前に“ためてる”を全種共通で足す。
        if (_kick > 0f)
        {
            sy *= 1f - 0.18f * _kick;
            sx *= 1f + 0.08f * _kick;
        }

        // 合成して反映（基準スケールへ係数を掛ける）。Rotation は FlipH と独立に効く。
        _body.Position = new Vector2(ox, oy);
        _body.Rotation = rot;
        _body.Scale = new Vector2(_bodyBaseScale * sx, _bodyBaseScale * sy);
    }
}
