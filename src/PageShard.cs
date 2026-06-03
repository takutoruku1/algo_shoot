using Godot;

// PageShard : 「うつむきさん」— 暴言は撒かない無口な人（最初の練習台）。
// 黒い吹き出しパネル3枚を持つが発射しない。剥がして浄化する基礎を学ぶ用。
public partial class PageShard : Enemy
{
    private const float DriftSpeed = 28f;

    protected override void OnEnemyReady()
    {
        Points = 80;
        BodyRadius = 5f;   // 当たり判定小さめ
        PanelCount = 3;
        PanelInk = 2;
        OrbitRadius = 14f;
        SpinSpeed = 1.0f;
        PanelsFire = false; // 無口（最初の練習台）

        // MVPでは アンチくん素材を流用（後で専用素材に差し替え可）
        PreTexPath = "res://char/enemy_anti_pre.png";
        PostTexPath = "res://char/enemy_anti_post.png";
        PanelTexPath = "res://char/panel_anti.png";
        BodyDisplayH = 28f;
    }

    protected override void UpdateMovement(double delta)
    {
        GlobalPosition += new Vector2(-DriftSpeed * (float)delta, 0f);
    }
}
