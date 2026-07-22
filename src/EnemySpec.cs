// EnemySpec / StageTheme : 道中ザコのステージ別スポーンテーブル。
// 「どの絵・どんな挙動のザコが湧くか」を表データで持ち、Spawner が StageTheme に応じて引く。
// MidEnemy 1クラス＋このテーブルで、ステージ×2種の出し分けをサブクラス量産なしに実現する。

// 各ステージの「心象世界」テーマ。Stage*.cs が Spawner.Theme に渡す。
public enum StageTheme
{
    Default, // 既存挙動（アンチくん/うつむきさん）。StageW0 等そのまま。
    Rei,     // ドローン系（偵察ドローン / 監視カメラ・目）
    Akari,   // 教室（机・椅子 / ノート・教科書）
    Koharu,  // 台所（包丁・まな板 / 鍋・お玉）
}

// 道中ザコの固有攻撃パターン。本体(MidEnemy)が _spec.Pattern で発射を分岐する。
// None=撃たない（drifter の無弾種・Default drifter）。弾数/間隔は Enemy.Dn/Di で難易度スケールする。
public enum AttackPattern
{
    None,            // 撃たない
    ReiLockBurst,    // レイ偵察ドローン：ロックオン連射（予告→自機方向3連バースト）
    ReiPulseRing,    // レイ監視カメラの目：視線パルス放射（全方位等間隔リング）
    AkariScatter,    // あかり机・椅子：ばらまき投擲（固定左方向扇）
    AkariDrop,       // あかりノート・教科書：落書きドロップ（真下へ低速）
    KoharuSharp3,    // こはる包丁・まな板：高速鋭3WAY（短予告→自機方向3本）
    KoharuSimmer,    // こはる鍋・お玉：とろ火ゆらぎ弾（自機狙い超低速単発）
    DefaultAim,      // アンチくん：自機狙い単発（現状踏襲）
    FlankAim,        // 回り込み「引用リプ」：右から出現→上下端を走行→自機後方(x≈40)に着座→右向き低速単発（全テーマ共通）
    BuzzWall,        // 盾もち「バズ壁」：撃たない・遅い・硬い（パネル5×インク3）＝DPSチェックの壁（全テーマ・波B/C限定）
    KoharuPrayerCarry, // 祈り運び：消せる祈り弾を3発ぶら下げて横断するボーナス種（こはる面専用・お残し禁止の練習台）
}

// 1種のザコの見た目（pre/post テクスチャ）と挙動パラメータ。
public readonly struct EnemySpec
{
    public readonly string PreTexPath;
    public readonly string PostTexPath;
    public readonly int Points;
    public readonly float BodyRadius;
    public readonly float MoveSpeed;
    public readonly float SpinSpeed;
    public readonly bool Fires;        // 暴言弾を撒くか（撃つ種/撃たない種）
    public readonly float FireInterval;
    public readonly float BulletSpeed;
    public readonly float SwayAmp;     // 上下うねりの振幅（0=直進）
    public readonly float SwayFreq;
    public readonly AttackPattern Pattern; // 固有攻撃パターン（既定 None＝撃たない、後方互換）

    public EnemySpec(string pre, string post, int points, float bodyRadius,
        float moveSpeed, float spinSpeed, bool fires, float fireInterval,
        float bulletSpeed, float swayAmp = 0f, float swayFreq = 0f,
        AttackPattern pattern = AttackPattern.None)
    {
        PreTexPath = pre;
        PostTexPath = post;
        Points = points;
        BodyRadius = bodyRadius;
        MoveSpeed = moveSpeed;
        SpinSpeed = spinSpeed;
        Fires = fires;
        FireInterval = fireInterval;
        BulletSpeed = bulletSpeed;
        SwayAmp = swayAmp;
        SwayFreq = swayFreq;
        Pattern = pattern;
    }
}

// テーマ → 2種(撃つ種A / 撃たない種B)のテーブル。
public static class EnemyTable
{
    // 各テーマの「撃つ種」（GlyphMote相当：速め・直進・弾を撒く）。
    // レイ＝偵察ドローン / あかり＝机・椅子 / こはる＝包丁・まな板。
    // 各テーマの「撃たない種」（PageShard相当：ゆっくり・上下にうねる・無口）。
    // レイ＝監視カメラ・目 / あかり＝ノート・教科書 / こはる＝鍋・お玉。
    public static (EnemySpec shooter, EnemySpec drifter) For(StageTheme theme) => theme switch
    {
        // 発射は本体(MidEnemy)が Pattern で行う。fires/fireInterval/bulletSpeed はパネル発射用の旧値で、
        // 本体発射では参照しない（Panel 側の自前発射は無効化済み）。各種の弾速・間隔・弾数は MidEnemy 内で定義。
        StageTheme.Rei => (
            new EnemySpec("res://char/enemy_rei_drone_pre.png", "res://char/enemy_rei_drone_post.png",
                100, 4f, moveSpeed: 56f, spinSpeed: 1.5f, fires: true, fireInterval: 1.8f, bulletSpeed: 95f,
                pattern: AttackPattern.ReiLockBurst),
            new EnemySpec("res://char/enemy_rei_eye_pre.png", "res://char/enemy_rei_eye_post.png",
                80, 5f, moveSpeed: 26f, spinSpeed: 0.9f, fires: false, fireInterval: 0f, bulletSpeed: 0f,
                swayAmp: 12f, swayFreq: 1.6f, pattern: AttackPattern.ReiPulseRing)),

        StageTheme.Akari => (
            new EnemySpec("res://char/enemy_akari_desk_pre.png", "res://char/enemy_akari_desk_post.png",
                100, 5f, moveSpeed: 46f, spinSpeed: 1.2f, fires: true, fireInterval: 2.0f, bulletSpeed: 90f,
                pattern: AttackPattern.AkariScatter),
            new EnemySpec("res://char/enemy_akari_note_pre.png", "res://char/enemy_akari_note_post.png",
                80, 4f, moveSpeed: 30f, spinSpeed: 1.1f, fires: false, fireInterval: 0f, bulletSpeed: 0f,
                swayAmp: 16f, swayFreq: 1.3f, pattern: AttackPattern.AkariDrop)),

        StageTheme.Koharu => (
            new EnemySpec("res://char/enemy_koharu_knife_pre.png", "res://char/enemy_koharu_knife_post.png",
                100, 4f, moveSpeed: 60f, spinSpeed: 1.6f, fires: true, fireInterval: 1.7f, bulletSpeed: 100f,
                pattern: AttackPattern.KoharuSharp3),
            new EnemySpec("res://char/enemy_koharu_pot_pre.png", "res://char/enemy_koharu_pot_post.png",
                80, 6f, moveSpeed: 24f, spinSpeed: 0.8f, fires: false, fireInterval: 0f, bulletSpeed: 0f,
                swayAmp: 10f, swayFreq: 1.1f, pattern: AttackPattern.KoharuSimmer)),

        // Default: 既存アンチくん/うつむきさん素材。Spawner が GlyphMote/PageShard を直接使う想定だが、
        // テーブル経由でも同じ姿が出るよう一応そろえておく。
        _ => (
            new EnemySpec("res://char/enemy_anti_pre.png", "res://char/enemy_anti_post.png",
                100, 4f, moveSpeed: 50f, spinSpeed: 1.4f, fires: true, fireInterval: 1.9f, bulletSpeed: 90f,
                pattern: AttackPattern.DefaultAim),
            new EnemySpec("res://char/enemy_anti_pre.png", "res://char/enemy_anti_post.png",
                80, 5f, moveSpeed: 28f, spinSpeed: 1.0f, fires: false, fireInterval: 0f, bulletSpeed: 0f)),
    };

    // 第4種：回り込み「引用リプ」（FlankAim）。スキンは各テーマの“撃つ種”を流用（新規アート不要）し、
    // 挙動だけ差し替える：右から出現→上下端を走行→自機後方(x≈40)に着座→右向き低速単発。
    // 走行が速めなのは回り込み経路を“見せて”からすぐ圧に移るためのテンポ確保（遅いと間延びする）。
    private const float FlankMoveSpeed = 110f; // 走行速度(px/s)。MidEnemy.ApproachFloor(78) より速い
    public static EnemySpec Flanker(StageTheme theme)
    {
        var (shooter, _) = For(theme);
        return new EnemySpec(shooter.PreTexPath, shooter.PostTexPath, shooter.Points, shooter.BodyRadius,
            moveSpeed: FlankMoveSpeed, spinSpeed: shooter.SpinSpeed, fires: true,
            fireInterval: shooter.FireInterval, bulletSpeed: shooter.BulletSpeed,
            pattern: AttackPattern.FlankAim);
    }

    // 盾もち種「バズ壁」（BuzzWall）。スキンは各テーマの“撃たない種”を流用（新規アート無し）し、
    // 撃たない・遅い・硬い（パネル数/インクは MidEnemy が Pattern で上書き）に差し替える。
    // 高ポイント＝剥がし切るDPSチェックへの対価（リスクとリターン：無視もできるが報酬は大きい）。
    private const float BuzzWallMoveSpeed = 18f; // のそのそ進む壁（設計目安値）
    public static EnemySpec BuzzWall(StageTheme theme)
    {
        var (_, drifter) = For(theme);
        return new EnemySpec(drifter.PreTexPath, drifter.PostTexPath, points: 150,
            bodyRadius: drifter.BodyRadius + 2f, moveSpeed: BuzzWallMoveSpeed, spinSpeed: drifter.SpinSpeed * 0.6f,
            fires: false, fireInterval: 0f, bulletSpeed: 0f,
            pattern: AttackPattern.BuzzWall);
    }

    // 祈り運び種（KoharuPrayerCarry・こはる面専用）。スキンはこはるの“撃たない種”（鍋・お玉＝運ぶ絵柄）を流用。
    // 消せる祈り弾3発をぶら下げて横断するボーナス種＝脅威ではないので低ポイント・すぐ剥がせる（MidEnemy 側で調整）。
    private const float PrayerCarrySpeed = 34f; // 横断速度(px/s)。約11秒で画面を渡り切る
    public static EnemySpec PrayerCarrier()
    {
        var (_, drifter) = For(StageTheme.Koharu);
        return new EnemySpec(drifter.PreTexPath, drifter.PostTexPath, points: 60,
            bodyRadius: drifter.BodyRadius, moveSpeed: PrayerCarrySpeed, spinSpeed: drifter.SpinSpeed,
            fires: false, fireInterval: 0f, bulletSpeed: 0f,
            pattern: AttackPattern.KoharuPrayerCarry);
    }
}
