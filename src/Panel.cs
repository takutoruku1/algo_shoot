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
        Monitoring = false;
        Monitorable = false;
        if (_shape != null) _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        GetNodeOrNull<GameManager>("/root/Game")?.AddBulletCleared(); // 剥がし小加点
        _owner?.OnPanelStripped(this);
        QueueFree();
    }

    public override void _Draw()
    {
        if (_dead || _hasTex) return; // スプライトがあれば図形は描かない
        float r = 3.2f + Ink * 0.8f; // インクが多いほど大きい
        // 視認性のための高彩度フチ（黒弾が背景に埋もれない）
        DrawCircle(Vector2.Zero, r + 1f, new Color(1f, 0.35f, 0.55f, 0.9f));
        // 黒インク本体
        DrawCircle(Vector2.Zero, r, new Color(0.10f, 0.08f, 0.13f));
        // 「・・・」（暴言の抽象表現）
        DrawCircle(new Vector2(-1.4f, 0), 0.5f, new Color(0.85f, 0.85f, 0.9f));
        DrawCircle(new Vector2(0f, 0), 0.5f, new Color(0.85f, 0.85f, 0.9f));
        DrawCircle(new Vector2(1.4f, 0), 0.5f, new Color(0.85f, 0.85f, 0.9f));
    }
}
