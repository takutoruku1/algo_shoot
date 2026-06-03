using Godot;

// Ripple : 人を浄化(改心)した瞬間に広がる「やさしさの波紋」。
// 同心円に広がり、触れた敵パネル(layer16)を剥がす＝連鎖浄化を起こす。
// 連鎖で次々と人が改心し、波紋がさらに広がっていく。
public partial class Ripple : Area2D
{
    public const float MaxRadius = 46f;
    private const float Duration = 0.35f;

    private double _t;
    private CircleShape2D _circle = null!;
    private CollisionShape2D _shape = null!;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 16; // パネル
        Monitoring = true;
        Monitorable = false;
        _circle = new CircleShape2D { Radius = 1f };
        _shape = new CollisionShape2D { Shape = _circle };
        AddChild(_shape);
        AreaEntered += OnAreaEntered;
        ZIndex = 5;
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area is Panel p)
            p.WeakenByRipple();
    }

    public override void _PhysicsProcess(double delta)
    {
        _t += delta;
        float prog = Mathf.Min(1f, (float)(_t / Duration));
        _circle.Radius = Mathf.Lerp(1f, MaxRadius, prog);
        QueueRedraw();
        if (_t >= Duration)
            QueueFree();
    }

    public override void _Draw()
    {
        float prog = Mathf.Min(1f, (float)(_t / Duration));
        float a = 0.55f * (1f - prog);
        float r = _circle.Radius;
        // 外側＝淡紫、内側＝水色〜白（やさしさの光の波）
        DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 48, new Color(0.79f, 0.72f, 0.94f, a * 0.85f), 2f);      // 淡紫
        DrawArc(Vector2.Zero, r * 0.9f, 0, Mathf.Tau, 48, new Color(0.92f, 0.98f, 1f, a), 1.5f);        // 水色〜白
    }
}
