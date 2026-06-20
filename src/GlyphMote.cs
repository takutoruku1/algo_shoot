using Godot;

// GlyphMote : 「アンチくん」— SNSで悪魔化した直進ザコの人。
// 黒い吹き出しパネル3枚を旋回させ、暴言弾を撒きながら左へ進む。
// 全パネルを剥がすと改心して笑顔の味方になる（Enemy基底）。
public partial class GlyphMote : Enemy
{
    private const float MoveSpeed = 50f;
    private float _campX;   // この位置まで来たら居座る（倒さない限り画面外に出ない）
    private float _vy;      // 居座り中の上下往復

    protected override void OnEnemyReady()
    {
        Points = 100;
        BodyRadius = 4f;   // 当たり判定小さめ
        PanelCount = 3;
        PanelInk = 2;
        OrbitRadius = 14f;
        SpinSpeed = 1.4f;
        PanelsFire = true;
        PanelFireInterval = 1.9f;
        EnemyBulletSpeed = 90f;

        // 生成済みドット絵素材
        PreTexPath = "res://char/enemy_anti_pre.png";
        PostTexPath = "res://char/enemy_anti_post.png";
        PanelTexPath = "res://char/panel_anti.png";
        BodyDisplayH = 28f;
        OrbitRadius = 14f;

        _campX = GD.Randf() * 150f + 120f;                 // 120〜270 のどこかに陣取る
        _vy = (GD.Randf() < 0.5f ? -1f : 1f) * 16f;
    }

    // 左へ進入 → _campX に着いたら居座る（上下にゆっくり往復・画面外へ出ない）。倒すまで去らない。
    protected override void UpdateMovement(double delta)
    {
        float dt = (float)delta;
        if (GlobalPosition.X > _campX)
        {
            GlobalPosition += new Vector2(-MoveSpeed * dt, 0f);
            return;
        }
        float ny = GlobalPosition.Y + _vy * dt;
        if (ny < 28f || ny > 188f) { _vy = -_vy; ny = Mathf.Clamp(ny, 28f, 188f); }
        GlobalPosition = new Vector2(_campX, ny);
    }
}
