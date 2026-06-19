using Godot;

// MidEnemy : 道中ザコの汎用版。テクスチャパスと挙動パラメータを EnemySpec で受け取り、
// ステージごとに「心象世界」の姿・撃ち方を出し分ける（6個のサブクラスを量産しない）。
// 既存 GlyphMote/PageShard の幅（撃つ/撃たない・速い/ゆっくり漂う）を踏襲しつつ、
// 上下うねりの有無で2種に動きの差を持たせて単調にしない。
public partial class MidEnemy : Enemy
{
    private EnemySpec _spec;
    private double _swayT;
    private float _baseY;
    private bool _baseYSet;

    // Spawner から AddChild 前に呼ぶ（OnEnemy Ready/_Ready より先に値を渡しておく）。
    public void Configure(in EnemySpec spec) => _spec = spec;

    protected override void OnEnemyReady()
    {
        Points = _spec.Points;
        BodyRadius = _spec.BodyRadius;
        PanelCount = 3;
        PanelInk = 2;
        OrbitRadius = 14f;
        SpinSpeed = _spec.SpinSpeed;
        PanelsFire = _spec.Fires;
        PanelFireInterval = _spec.FireInterval;
        EnemyBulletSpeed = _spec.BulletSpeed;

        PreTexPath = _spec.PreTexPath;
        PostTexPath = _spec.PostTexPath;
        PanelTexPath = "res://char/panel_anti.png"; // 吹き出しは既存流用
        BodyDisplayH = 28f;
    }

    protected override void UpdateMovement(double delta)
    {
        // 基準Y（うねりの中心）は最初の移動フレームで確定（GlobalPosition は AddChild 後に設定されるため）。
        if (!_baseYSet) { _baseY = GlobalPosition.Y; _baseYSet = true; }

        GlobalPosition += new Vector2(-_spec.MoveSpeed * (float)delta, 0f);
        // SwayAmp>0 の種は上下にうねって動きに差を出す（撃たない種に揺れを持たせる等）。
        if (_spec.SwayAmp > 0f)
        {
            _swayT += delta;
            float y = _baseY + Mathf.Sin((float)_swayT * _spec.SwayFreq) * _spec.SwayAmp;
            GlobalPosition = new Vector2(GlobalPosition.X, y);
        }
    }
}
