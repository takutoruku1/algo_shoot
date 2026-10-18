using Godot;

public partial class TrainingDummy : Enemy
{
    private const float Radius = 12f;
    public System.Action<int, bool>? OnDamaged;
    private double _hitCooldown;
    private double _flashT;
    private double _bobT;
    private Sprite2D _sprite = null!;

    protected override void OnEnemyReady()
    {
        // Keep boss phases inactive while sharing the live body damage rules.
        BarCount = 0;
        PanelCount = 0;
        PanelsFire = false;
        BodyRadius = Radius;
        AutoBank = false;
    }

    public override void _Ready()
    {
        base._Ready();
        Monitoring = true;
        SetCollisionMaskValue(2, true);
        AreaEntered += OnHitByPlayerBullet;
        var texture = GD.Load<Texture2D>("res://char/enemy_anti_pre.png");
        _sprite = new Sprite2D
        {
            Name = "DummyBody", Texture = texture, FlipH = true,
            TextureFilter = TextureFilterEnum.Linear,
            Scale = Vector2.One * (40f / texture.GetHeight()), ZIndex = -1,
        };
        AddChild(_sprite);
        ZIndex = 5;
    }

    public override float HpRatio => 1f;

    public void ResetMeasurement()
    {
        _hitCooldown = 0;
        _flashT = 0;
        QueueRedraw();
    }

    protected override void UpdateMovement(double delta)
    {
        _bobT += delta;
        _sprite.Position = new Vector2(0, Mathf.Sin((float)_bobT * 2.4f) * 2.2f);
    }

    public override void _PhysicsProcess(double delta)
    {
        _hitCooldown = Mathf.Max(0, _hitCooldown - GameManager.EnemyDelta(delta));
        _flashT = Mathf.Max(0, _flashT - delta);
        base._PhysicsProcess(delta);
        QueueRedraw();
    }

    private void OnHitByPlayerBullet(Area2D area)
    {
        if (area is not Bullet b || b.IsEnemy || !b.Active || !b.RegisterChargeHit(this)) return;
        b.TryChain(this);
        if (b.Pierce > 0) b.Pierce--;
        else GetNode<BulletPool>("/root/Pool").Despawn(b);
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        player?.NotifyShotHit(this);
        if (_hitCooldown > 0 && !b.Charged) return;
        int damage = BodyShotDamage(b, player, out bool critical);
        _hitCooldown = BodyHitCd;
        b.ChargeImpact(b.GlobalPosition);
        ApplyDamage(damage, critical ? FxLayer.Gold : FxLayer.Sig2);
    }

    public override void Purify()
    {
        int damage = Mathf.RoundToInt(BombStrikeBase * GameManager.Instance!.BombPowerMul);
        ApplyDamage(damage, FxLayer.Gold, bomb: true);
    }

    private void ApplyDamage(int damage, Color color, bool bomb = false)
    {
        OnDamaged?.Invoke(damage, bomb);
        FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -16), damage.ToString(), color, bomb ? 15 : 9);
        _flashT = 0.16;
        Audio.Instance?.PlayBossHit(Mathf.Clamp(damage, 1, 4));
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 40, new Color(UiKit.Info, 0.65f), 0.6f, true);
        if (_flashT <= 0) return;
        float fade = (float)(_flashT / 0.16);
        DrawArc(Vector2.Zero, Radius + 8f * (1f - fade), 0, Mathf.Tau, 40,
            new Color(1, 1, 1, fade * 0.8f), 1f, true);
    }
}
