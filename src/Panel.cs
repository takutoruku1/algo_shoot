using Godot;

// Panel : 悪魔化した人(Enemy)に貼りついた「黒い吹き出し（暴言）」パネル。
// 本体の周囲を旋回し、①暴言弾の発生源 ②algoの弾を遮る盾 を兼ねる。
// algoの光弾が当たるとインクが減り、0で砕けて剥がれる（＝浄化が一歩進む）。
// 衝突: layer=16(パネル), mask=2(自機弾)。自機本体(mask)はパネルを含めないので接触では痛くない。
public partial class Panel : Area2D
{
    public int Ink = 2;

    private Enemy _owner = null!;
    private float _baseAngle, _orbitRadius, _spinSpeed, _spin, _bulletSpeed, _fireInterval;
    private bool _fires;
    private double _fireTimer;
    private bool _dead;
    private CollisionShape2D _shape = null!;
    private string _texPath = "";
    private Sprite2D _sprite = null!;
    private bool _hasTex;
    private const float DisplayH = 14f;

    public void Setup(Enemy owner, float baseAngle, float orbitRadius, float spinSpeed,
                      bool fires, float fireInterval, int ink, float bulletSpeed, string texPath = "")
    {
        _owner = owner;
        _baseAngle = baseAngle;
        _orbitRadius = orbitRadius;
        _spinSpeed = spinSpeed;
        _fires = fires;
        _fireInterval = fireInterval;
        Ink = ink;
        _bulletSpeed = bulletSpeed;
        _texPath = texPath;
    }

    public override void _Ready()
    {
        CollisionLayer = 16; // パネル
        CollisionMask = 2;   // 自機弾
        Monitoring = true;
        Monitorable = true;
        _shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = 3f } };
        AddChild(_shape);
        AreaEntered += OnAreaEntered;

        // 吹き出しスプライト（あれば）。無ければ _Draw のプレースホルダ。
        if (!string.IsNullOrEmpty(_texPath))
        {
            var t = ResourceLoader.Load<Texture2D>(_texPath);
            if (t != null)
            {
                _hasTex = true;
                _sprite = new Sprite2D
                {
                    Texture = t,
                    Centered = true,
                    TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                };
                float s = DisplayH / t.GetHeight();
                _sprite.Scale = new Vector2(s, s);
                AddChild(_sprite);
            }
        }

        _fireTimer = GD.Randf() * _fireInterval; // 発射タイミングをばらす
        UpdateOrbit(0);
    }

    private void UpdateOrbit(double delta)
    {
        _spin += _spinSpeed * (float)delta;
        float a = _baseAngle + _spin;
        Position = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _orbitRadius;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_dead) return;
        if (Hud.BubblePaused) return; // 吹き出し表示中は攻撃・旋回を止める
        UpdateOrbit(delta);

        if (_fires)
        {
            _fireTimer += delta;
            if (_fireTimer >= _fireInterval)
            {
                _fireTimer = 0;
                Fire();
            }
        }
    }

    private void Fire()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        Vector2 dir = new Vector2(-1, 0);
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Node2D pl)
        {
            var d = pl.GlobalPosition - GlobalPosition;
            if (d.LengthSquared() > 0.01f) dir = d.Normalized();
        }
        pool.Spawn(GlobalPosition, dir * _bulletSpeed, isEnemy: true, 3f, 1);
    }

    private void OnAreaEntered(Area2D area)
    {
        if (_dead) return;
        if (area is Bullet b && !b.IsEnemy && b.Active)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.Despawn(b);
            Ink--;
            if (Ink <= 0) Shatter();
            else QueueRedraw();
        }
    }

    // やさしさの波紋で剥がす（MVPでは即砕く）。
    public void WeakenByRipple()
    {
        if (_dead) return;
        Shatter();
    }

    public void Shatter()
    {
        if (_dead) return;
        _dead = true;
        // 砕けは被弾シグナル(OnAreaEntered)中に走るため、衝突無効化は遅延設定する。
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        if (_shape != null) _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        GetNodeOrNull<GameManager>("/root/Game")?.AddBulletCleared(); // 剥がし小加点
        FxLayer.Instance?.Shatter(GlobalPosition); // 砕け＋やさしさの粒
        _owner?.OnPanelStripped(this);
        QueueFree();
    }

    public override void _Draw()
    {
        if (_dead || _hasTex) return; // スプライトがあれば図形は描かない
        float r = 3.2f + Ink * 0.8f; // インクが多いほど大きい
        // ドットの黒い吹き出し（滑らかな円でなくピクセルで）
        PixelDisc(r + 1f, new Color(1f, 0.35f, 0.55f, 0.9f)); // ホット縁
        PixelDisc(r, new Color(0.10f, 0.08f, 0.13f));         // 黒インク本体
        // 「・・・」を1pxドットで
        var dot = new Color(0.85f, 0.85f, 0.9f);
        DrawRect(new Rect2(-2, 0, 1, 1), dot);
        DrawRect(new Rect2(0, 0, 1, 1), dot);
        DrawRect(new Rect2(2, 0, 1, 1), dot);
    }

    // 1x1の正方ドットで塗る“ピクセルの円”。
    private void PixelDisc(float r, Color col)
    {
        int ri = Mathf.CeilToInt(r);
        float r2 = r * r;
        for (int y = -ri; y < ri; y++)
            for (int x = -ri; x < ri; x++)
            {
                float cx = x + 0.5f, cy = y + 0.5f;
                if (cx * cx + cy * cy <= r2)
                    DrawRect(new Rect2(x, y, 1, 1), col);
            }
    }
}
