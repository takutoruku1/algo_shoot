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

    public EnemySpec(string pre, string post, int points, float bodyRadius,
        float moveSpeed, float spinSpeed, bool fires, float fireInterval,
        float bulletSpeed, float swayAmp = 0f, float swayFreq = 0f)
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
        StageTheme.Rei => (
            new EnemySpec("res://char/enemy_rei_drone_pre.png", "res://char/enemy_rei_drone_post.png",
                100, 4f, moveSpeed: 56f, spinSpeed: 1.5f, fires: true, fireInterval: 1.8f, bulletSpeed: 95f),
            new EnemySpec("res://char/enemy_rei_eye_pre.png", "res://char/enemy_rei_eye_post.png",
                80, 5f, moveSpeed: 26f, spinSpeed: 0.9f, fires: false, fireInterval: 0f, bulletSpeed: 0f,
                swayAmp: 12f, swayFreq: 1.6f)),

        StageTheme.Akari => (
            new EnemySpec("res://char/enemy_akari_desk_pre.png", "res://char/enemy_akari_desk_post.png",
                100, 5f, moveSpeed: 46f, spinSpeed: 1.2f, fires: true, fireInterval: 2.0f, bulletSpeed: 90f),
            new EnemySpec("res://char/enemy_akari_note_pre.png", "res://char/enemy_akari_note_post.png",
                80, 4f, moveSpeed: 30f, spinSpeed: 1.1f, fires: false, fireInterval: 0f, bulletSpeed: 0f,
                swayAmp: 16f, swayFreq: 1.3f)),

        StageTheme.Koharu => (
            new EnemySpec("res://char/enemy_koharu_knife_pre.png", "res://char/enemy_koharu_knife_post.png",
                100, 4f, moveSpeed: 60f, spinSpeed: 1.6f, fires: true, fireInterval: 1.7f, bulletSpeed: 100f),
            new EnemySpec("res://char/enemy_koharu_pot_pre.png", "res://char/enemy_koharu_pot_post.png",
                80, 6f, moveSpeed: 24f, spinSpeed: 0.8f, fires: false, fireInterval: 0f, bulletSpeed: 0f,
                swayAmp: 10f, swayFreq: 1.1f)),

        // Default: 既存アンチくん/うつむきさん素材。Spawner が GlyphMote/PageShard を直接使う想定だが、
        // テーブル経由でも同じ姿が出るよう一応そろえておく。
        _ => (
            new EnemySpec("res://char/enemy_anti_pre.png", "res://char/enemy_anti_post.png",
                100, 4f, moveSpeed: 50f, spinSpeed: 1.4f, fires: true, fireInterval: 1.9f, bulletSpeed: 90f),
            new EnemySpec("res://char/enemy_anti_pre.png", "res://char/enemy_anti_post.png",
                80, 5f, moveSpeed: 28f, spinSpeed: 1.0f, fires: false, fireInterval: 0f, bulletSpeed: 0f)),
    };
}
