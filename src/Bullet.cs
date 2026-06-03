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
    public bool Grazed;  // グレイズ済みか（重複加点防止）

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
        Grazed = false;

        GlobalPosition = pos;

        // グループ登録（ボムの一括消去・グレイズ判定用）
        AddToGroup(isEnemy ? "enemy_bullets" : "player_bullets");

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

        // グループから外す（プール返却時）
        RemoveFromGroup("enemy_bullets");
        RemoveFromGroup("player_bullets");

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
            // 明るいクリームのハロー → 黒インク本体 → 中心の熱。
            // 明色ハローで淡いピンク背景からもくっきり浮く（視認性最優先）。
            DrawCircle(Vector2.Zero, r + 2.2f, new Color(1f, 0.97f, 0.9f, 0.55f));    // 外側の柔らかな明ハロー
            DrawCircle(Vector2.Zero, r + 1.1f, new Color(1f, 0.95f, 0.88f, 0.95f));   // 明るいフチ
            DrawCircle(Vector2.Zero, r, new Color(0.06f, 0.04f, 0.09f));              // 黒インク本体
            DrawCircle(Vector2.Zero, r * 0.5f, new Color(1.0f, 0.32f, 0.46f));        // 中心の熱
        }
        else
        {
            // 光のインク＝白コア＋水色グロー（＋薄い暗縁で視認性確保）
            DrawCircle(Vector2.Zero, r * 1.05f, new Color(0.10f, 0.18f, 0.30f, 0.45f)); // 薄い暗縁
            DrawCircle(Vector2.Zero, r, new Color(0.50f, 0.86f, 1.0f, 0.95f));          // 水色グロー
            DrawCircle(Vector2.Zero, r * 0.5f, new Color(1.0f, 1.0f, 1.0f));            // 白コア
        }
    }
}
