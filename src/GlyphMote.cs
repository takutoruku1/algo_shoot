using Godot;

// GlyphMote : Enemy (E1 直進ザコ)
// Hp=2。サイズ約16px。一定速度で左へ(約-70px/s)。
// 約1.2s毎にプレイヤー方向へ敵弾1発(速度約120)。
// _Draw: 小さな白い豆型＋黒インクのにじみ＋オレンジの一つ目。
public partial class GlyphMote : Enemy
{
    private const float MoveSpeed = 64f;    // 左へ
    private const float FireInterval = 1.6f;
    private const float BulletSpeed = 95f;

    private double _fireTimer = 0.0;

    protected override void OnEnemyReady()
    {
        Hp = 2;
        Radius = 8f; // 約16px相当
        QueueRedraw();
    }

    protected override void UpdateMovement(double delta)
    {
        // 一定速度で左へ。
        GlobalPosition += new Vector2(-MoveSpeed * (float)delta, 0f);

        // 周期的にプレイヤー狙い弾を発射。
        _fireTimer += delta;
        if (_fireTimer >= FireInterval)
        {
            _fireTimer -= FireInterval;
            Fire();
        }
    }

    private void Fire()
    {
        // プレイヤー方向を狙う。
        Vector2 dir = new Vector2(-1f, 0f);
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Node2D player)
        {
            Vector2 toPlayer = player.GlobalPosition - GlobalPosition;
            if (toPlayer.LengthSquared() > 0.0001f)
            {
                dir = toPlayer.Normalized();
            }
        }

        GetPool().Spawn(GlobalPosition, dir * BulletSpeed, isEnemy: true, 3f, 1);
    }

    public override void _Draw()
    {
        // 基底の浄化演出を尊重。
        base._Draw();

        // 浄化演出中は本体を描かない（フラッシュ＋パーティクルのみ）。
        if (IsDying)
            return;

        // 小さな白い豆型（横長楕円を2円で近似）。
        Color body = new Color(0.96f, 0.96f, 0.92f);
        DrawCircle(new Vector2(-3f, 0f), 6f, body);
        DrawCircle(new Vector2(3f, 0f), 6f, body);
        DrawCircle(Vector2.Zero, 7f, body);

        // 黒インクのにじみ。
        Color ink = new Color(0.08f, 0.06f, 0.05f, 0.55f);
        DrawCircle(new Vector2(-5f, 3f), 2.2f, ink);
        DrawCircle(new Vector2(2f, -4f), 1.6f, ink);
        DrawCircle(new Vector2(5f, 4f), 1.4f, ink);

        // オレンジの一つ目（白目＋オレンジ瞳＋黒点）。
        Color sclera = new Color(1f, 0.97f, 0.9f);
        Color iris = new Color(0.95f, 0.55f, 0.15f);
        Color pupil = new Color(0.1f, 0.06f, 0.03f);
        Vector2 eye = new Vector2(-2f, -1f);
        DrawCircle(eye, 3.2f, sclera);
        DrawCircle(eye, 2.0f, iris);
        DrawCircle(eye + new Vector2(-0.6f, 0f), 0.9f, pupil);
    }
}
