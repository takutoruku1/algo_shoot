using Godot;

// Panel : 悪魔化した人(Enemy)に貼りついた「黒い吹き出し（暴言）」パネル。
// 本体の周囲を旋回し、①暴言弾の発生源 ②algoの弾を遮る盾 を兼ねる。
// algoの光弾が当たるとインクが減り、0で砕けて剥がれる（＝浄化が一歩進む）。
// 衝突: layer=16(パネル), mask=2(自機弾)。自機本体(mask)はパネルを含めないので接触では痛くない。
public partial class Panel : Area2D
{
    public int Ink = 2;
    // 一時不可侵（Enemy.SetPanelsInvulnerable 経由）。演出でボスが画面外へ退場している間、
    // 流れ弾・ボム・波紋でパネルが剥がれて BREAK が空撃ちされるのを防ぐ（あかり戦「雨の帰り道」）。
    public bool Invulnerable;

    private Enemy _owner = null!;
    private float _baseAngle, _orbitRadius, _spinSpeed, _spin, _bulletSpeed, _fireInterval;
    private bool _fires;
    private double _fireTimer;
    private bool _dead;
    private CollisionShape2D _shape = null!;
    private string _texPath = "";
    private Sprite2D _sprite = null!;
    private bool _hasTex;
    private float _displayScale = 1f; // ザコ縮小用。絵＆当たりに掛ける（ボスは1）
    private const float DisplayH = 14f;

    public void Setup(Enemy owner, float baseAngle, float orbitRadius, float spinSpeed,
                      bool fires, float fireInterval, int ink, float bulletSpeed, string texPath = "", float displayScale = 1f)
    {
        _displayScale = displayScale;
        _owner = owner;
        _baseAngle = baseAngle;
        _orbitRadius = orbitRadius;
        _spinSpeed = spinSpeed;
        // 発射は本体(Enemy/MidEnemy)へ一括移管したため、パネル自前の発射は常に無効。
        // パネルは「盾＝弾を遮る／剥がして浄化」の役割のみに専念する（引数 fires は後方互換のため残置）。
        _fires = false;
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
        _shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = 3f * _displayScale } };
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
                float s = DisplayH * _displayScale / t.GetHeight();
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
        if (Hud.BubblePaused) return; // 吹き出し表示中は旋回を止める
        UpdateOrbit(delta);
        // 発射は本体側へ移管済み（_fires は常に false）。ここでは旋回＝盾の挙動のみ。
    }

    private void OnAreaEntered(Area2D area)
    {
        if (_dead || Invulnerable) return; // 不可侵中は弾も受けない（消しもしない＝素通し）
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
        if (_dead || Invulnerable) return; // 不可侵中はボム/波紋の一括砕きも効かない（退場中のBREAK空撃ち防止）
        _dead = true;
        // 砕けは被弾シグナル(OnAreaEntered)中に走るため、衝突無効化は遅延設定する。
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        if (_shape != null) _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        GetNodeOrNull<GameManager>("/root/Game")?.AddBulletCleared(); // 剥がし小加点
        FxLayer.Instance?.Shatter(GlobalPosition); // 砕け＋やさしさの粒
        Audio.Instance?.PlayStrip(); // ④軽い剥離「コツッ」（浄化成立より一段軽い）
        _owner?.OnPanelStripped(this);
        QueueFree();
    }

    // 穢れの吹き出し（設計色 #e072ac）。ドット絵ではなく、マゼンタのグロー＋明リング＋
    // 暗マルーン核（radial-gradient rgba(70,24,52)→#180812 相当）＋滑らかな「・・・」。
    private static readonly Color KegareRim = new Color(0.882f, 0.447f, 0.675f);   // #e072ac
    private static readonly Color MaroonMid = new Color(0.274f, 0.094f, 0.204f);   // rgba(70,24,52)
    private static readonly Color MaroonCore = new Color(0.094f, 0.031f, 0.071f);  // #180812
    private static readonly Color BubbleDot = new Color(0.92f, 0.86f, 0.92f);

    public override void _Draw()
    {
        if (_dead || _hasTex) return; // スプライトがあれば図形は描かない
        float r = 3.2f + Ink * 0.8f;  // インクが多いほど大きい

        // 外周グロー（box-shadow 相当）。
        for (int i = 3; i >= 1; i--)
        {
            float t = i / 3f;
            DrawCircle(Vector2.Zero, r * (1f + 0.9f * t),
                new Color(KegareRim.R, KegareRim.G, KegareRim.B, 0.12f * (1f - t) + 0.05f), true, -1f, true);
        }
        DrawCircle(Vector2.Zero, r + 1.2f, new Color(KegareRim, 0.9f), true, -1f, true); // 明マゼンタリング
        DrawCircle(Vector2.Zero, r, MaroonMid, true, -1f, true);                         // マルーン
        DrawCircle(Vector2.Zero, r * 0.66f, MaroonCore, true, -1f, true);                // 暗芯

        // 「・・・」（滑らかな白ドット）。
        DrawCircle(new Vector2(-2f, 0f), 0.7f, BubbleDot, true, -1f, true);
        DrawCircle(new Vector2(0f, 0f), 0.7f, BubbleDot, true, -1f, true);
        DrawCircle(new Vector2(2f, 0f), 0.7f, BubbleDot, true, -1f, true);
    }
}
