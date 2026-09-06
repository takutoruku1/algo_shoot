using Godot;

// PageShard : 「うつむきさん」— 暴言は撒かない無口な人（最初の練習台）。
// 黒い吹き出しパネル3枚を持つが発射しない。剥がして浄化する基礎を学ぶ用。
public partial class PageShard : Enemy
{
    private const float DriftSpeed = 28f;
    private float _campX;
    private float _vy;

    protected override void OnEnemyReady()
    {
        Points = 80;
        BodyRadius = 4.2f;   // 当たり判定小さめ（一回り小さく）
        PanelCount = 3;
        PanelInk = 2;
        OrbitRadius = 11.5f;
        PanelDisplayScale = 0.82f; // 一回り小さく
        SpinSpeed = 1.0f;
        PanelsFire = false; // 無口（最初の練習台）

        // MVPでは アンチくん素材を流用（後で専用素材に差し替え可）
        PreTexPath = "res://char/enemy_anti_pre.png";
        PostTexPath = "res://char/enemy_anti_post.png";
        // 盾の絵は面ごとに Panel 側が解決する（Panel.ResolveTexPath）。ここでは指定しない。
        BodyDisplayH = 23f;             // 一回り小さく

        _campX = GD.Randf() * 150f + 130f;                 // 130〜280
        _vy = (GD.Randf() < 0.5f ? -1f : 1f) * 12f;
    }

    // 左へ進入 → _campX で居座る（上下にゆっくり往復）。倒すまで去らない。
    protected override void UpdateMovement(double delta)
    {
        float dt = (float)delta;
        if (GlobalPosition.X > _campX)
        {
            GlobalPosition += new Vector2(-DriftSpeed * dt, 0f);
            return;
        }
        float ny = GlobalPosition.Y + _vy * dt;
        if (ny < 28f || ny > 188f) { _vy = -_vy; ny = Mathf.Clamp(ny, 28f, 188f); }
        GlobalPosition = new Vector2(_campX, ny);
    }
}
