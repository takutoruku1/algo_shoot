using Godot;

// Bullet : Area2D。Pool により生成・使い回しされる弾。
// 当たり判定（被弾処理）は敵側/自機側で行うため、Bullet 自身は Area 重なり処理を持たない。
// _PhysicsProcess で等速直線移動し、画面外(余白16px)に出たら Pool.Despawn(this)。
public partial class Bullet : Area2D
{
    // 衝突レイヤー/マスク（ビット値, 仕様準拠）
    // PlayerBullet: layer=2, mask=4
    // EnemyBullet:  layer=8, mask=1
    private const uint LayerPlayerBullet = 2;
    private const uint MaskPlayerBullet = 4;
    private const uint LayerEnemyBullet = 8;
    private const uint MaskEnemyBullet = 1;

    // 画面サイズと画面外判定の余白
    private const float ScreenWidth = 384f;
    private const float ScreenHeight = 216f;
    private const float Margin = 16f;

    public Vector2 Velocity;
    public bool IsEnemy;
    public int Damage;
    public bool Active;

    public float Radius { get; private set; } = 3f;

    private CollisionShape2D _shape = null!;
    private CircleShape2D _circle = null!;

    public override void _Ready()
    {
        // 子に CollisionShape2D(CircleShape2D) を追加
        _circle = new CircleShape2D { Radius = Radius };
        _shape = new CollisionShape2D { Shape = _circle };
        AddChild(_shape);

        // 初期状態は非アクティブ
        Deactivate();
    }

    // layer/mask/見た目/位置を設定し、可視化・monitoring 有効化。
    public void Activate(Vector2 pos, Vector2 vel, bool isEnemy, float radius, int damage)
    {
        Velocity = vel;
        IsEnemy = isEnemy;
        Damage = damage;
        Radius = radius;
        Active = true;

        GlobalPosition = pos;

        // 当たり半径を反映
        if (_circle != null)
            _circle.Radius = radius;

        // レイヤー/マスク設定（isEnemy に応じて）
        if (isEnemy)
        {
            CollisionLayer = LayerEnemyBullet; // 8
            CollisionMask = MaskEnemyBullet;   // 1
        }
        else
        {
            CollisionLayer = LayerPlayerBullet; // 2
            CollisionMask = MaskPlayerBullet;   // 4
        }

        // 可視化・検出有効化
        Visible = true;
        Monitoring = true;
        Monitorable = true;
        SetPhysicsProcess(true);

        if (_shape != null)
            _shape.Disabled = false;

        QueueRedraw();
    }

    // 非表示・monitoring 無効化・プールへ戻る準備。
    public void Deactivate()
    {
        Active = false;
        Velocity = Vector2.Zero;

        Visible = false;
        Monitoring = false;
        Monitorable = false;
        SetPhysicsProcess(false);

        if (_shape != null)
            _shape.Disabled = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active)
            return;

        GlobalPosition += Velocity * (float)delta;

        // 画面外(余白16px)に出たら Despawn
        var p = GlobalPosition;
        if (p.X < -Margin || p.X > ScreenWidth + Margin ||
            p.Y < -Margin || p.Y > ScreenHeight + Margin)
        {
            var pool = GetNodeOrNull<BulletPool>("/root/Pool");
            if (pool != null)
                pool.Despawn(this);
            else
                Deactivate();
        }
    }

    public override void _Draw()
    {
        if (!Active)
            return;

        float r = Radius;

        if (IsEnemy)
        {
            // 敵弾: 温色（赤〜オレンジ; 明コア + 濃リング）
            // 濃い赤リング
            DrawCircle(Vector2.Zero, r, new Color(0.85f, 0.15f, 0.10f));
            // オレンジ中間
            DrawCircle(Vector2.Zero, r * 0.7f, new Color(1.0f, 0.55f, 0.15f));
            // 明コア
            DrawCircle(Vector2.Zero, r * 0.4f, new Color(1.0f, 0.92f, 0.70f));
        }
        else
        {
            // 自機弾: 白〜水色（明コア + 水色）
            // 水色の外側
            DrawCircle(Vector2.Zero, r, new Color(0.45f, 0.85f, 1.0f, 0.85f));
            // 明コア（白）
            DrawCircle(Vector2.Zero, r * 0.55f, new Color(1.0f, 1.0f, 1.0f));
        }
    }
}
