using Godot;

// GlyphMote : 「アンチくん」— SNSで悪魔化した直進ザコの人。
// 黒い吹き出しパネル3枚を旋回させ、暴言弾を撒きながら左へ進む。
// 全パネルを剥がすと改心して笑顔の味方になる（Enemy基底）。
public partial class GlyphMote : Enemy
{
    private const float MoveSpeed = 50f;

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
    }

    protected override void UpdateMovement(double delta)
    {
        GlobalPosition += new Vector2(-MoveSpeed * (float)delta, 0f);
    }
}
