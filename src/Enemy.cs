using Godot;

// Enemy 基底 : Area2D。グループ "enemies"。
// 自機弾(Bullet で IsEnemy==false)とのArea重なりを検出し、Hp -= bullet.Damage、
// その弾を Pool.Despawn。Hp<=0 で Die()。
// 画面左外(x<-16)に出たら QueueFree。
// 衝突レイヤー: Enemy layer=4, mask=2 (自機弾=2 を検出)。
public partial class Enemy : Area2D
{
    public int Hp;

    // 当たり半径（派生クラスで上書き可）。
    protected float Radius = 8f;

    private CollisionShape2D _shape = null!;

    // 浄化演出（Die後）の進行用フラグ。
    private bool _dying = false;

    // 派生クラスが「浄化演出中は本体を描かない」判定に使う。
    protected bool IsDying => _dying;
    private double _dieTimer = 0.0;
    private const double DieDuration = 0.25; // 演出時間
    private Vector2[] _moteOffsets = System.Array.Empty<Vector2>();

    public override void _Ready()
    {
        AddToGroup("enemies");

        // 衝突レイヤー設定: Enemy=4、自機弾=2 を検出。
        CollisionLayer = 4;
        CollisionMask = 2;

        Monitoring = true;
        Monitorable = true;

        // 当たり判定 CircleShape2D を子に追加。
        _shape = new CollisionShape2D
        {
            Shape = new CircleShape2D { Radius = Radius }
        };
        AddChild(_shape);

        // 自機弾被弾検出。
        AreaEntered += OnAreaEntered;

        OnEnemyReady();
    }

    // 派生クラスの初期化フック（_Ready の最後に呼ばれる）。
    protected virtual void OnEnemyReady()
    {
    }

    protected BulletPool GetPool()
    {
        return GetNode<BulletPool>("/root/Pool");
    }

    private void OnAreaEntered(Area2D area)
    {
        if (_dying)
        {
            return;
        }

        if (area is Bullet b && !b.IsEnemy && b.Active)
        {
            Hp -= b.Damage;
            GetPool().Despawn(b);

            if (Hp <= 0)
            {
                Die();
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_dying)
        {
            _dieTimer += delta;
            QueueRedraw();
            if (_dieTimer >= DieDuration)
            {
                QueueFree();
            }
            return;
        }

        UpdateMovement(delta);

        // 画面左外でQueueFree。
        if (GlobalPosition.X < -16f)
        {
            QueueFree();
        }
    }

    // 派生クラスが移動・攻撃ロジックを実装する。
    protected virtual void UpdateMovement(double delta)
    {
    }

    // 浄化演出（白→紫の小フラッシュ＋数個の得点パーティクル風の点を散らす簡易演出）の後 QueueFree。
    protected virtual void Die()
    {
        if (_dying)
        {
            return;
        }
        _dying = true;
        _dieTimer = 0.0;

        // 当たり判定を無効化（演出中は被弾しない）。
        Monitoring = false;
        Monitorable = false;
        // Disabled は CollisionShape2D のプロパティ。Area2D 自身ではなく shape に対して設定する。
        if (_shape != null)
            _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);

        // 散らす点の配置を生成。
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        int count = 6;
        _moteOffsets = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            float ang = rng.RandfRange(0f, Mathf.Tau);
            float dist = rng.RandfRange(6f, 18f);
            _moteOffsets[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_dying)
        {
            DrawPurifyEffect();
        }
    }

    private void DrawPurifyEffect()
    {
        float t = (float)(_dieTimer / DieDuration);
        t = Mathf.Clamp(t, 0f, 1f);

        // 中央フラッシュ: 白→紫へ。
        Color flash = new Color(1f, 1f, 1f).Lerp(new Color(0.66f, 0.45f, 0.9f), t);
        flash.A = 1f - t;
        float flashR = Mathf.Lerp(Radius, Radius * 2.2f, t);
        DrawCircle(Vector2.Zero, flashR, flash);

        // 散らばる得点パーティクル風の点。
        Color mote = new Color(0.85f, 0.7f, 1f, 1f - t);
        foreach (var off in _moteOffsets)
        {
            DrawCircle(off * t, Mathf.Lerp(2.5f, 0.5f, t), mote);
        }
    }
}
