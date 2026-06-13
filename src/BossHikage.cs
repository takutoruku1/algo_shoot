using Godot;

// BossHikage : W0 climax の炎上中ボス「ヒカゲ」。
// ・HP制（パネルを剥がすたびHP減＋パネル補充）で約1分の持久戦。HPバーを表示。
// ・画面全体をうろつきながら攻撃し、進行方向を向く。
// ・HP0で【浄化→大泣き→最高の笑顔】の3段階で改心し、algo の特別フォロワー（ヒカゲ強化）になる。
public partial class BossHikage : Enemy
{
    public bool Finished { get; private set; }

    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private Vector2 _moveTarget;
    private bool _hasTarget;
    private const float RoamSpeed = 72f;

    // 幾何学弾幕（HPフェーズで変化）
    private double _fireT;
    private float _spiralAngle;
    private int _volley;
    private const float BulletR = 3.4f;

    // 大泣き中のかけあい（話者: false=ヒカゲ / true=algo）
    private bool _seq;
    private int _line;
    private double _lineT;
    private const double LineDur = 2.6;
    private static readonly (string text, bool algo)[] Lines =
    {
        ("…うち、ずっと わらうの、へただった。", false),
        ("そのままで いいよ。いっしょに わらお？", true),
        ("…うん。ありがとう…ともだちに なってくれて。", false),
    };

    protected override void OnEnemyReady()
    {
        _rng.Randomize();

        Points = 1200;
        BodyRadius = 9f;
        PanelCount = 6;          // 黒い炎のリング（盾＝剥がしてHPを削る。攻撃は本体の弾幕）
        PanelInk = 2;
        OrbitRadius = 28f;
        SpinSpeed = 1.2f;
        PanelsFire = false;      // 弾はパネルでなく本体の幾何学弾幕で撃つ
        EnemyBulletSpeed = 95f;

        // HP制：剥がした枚数ぶんHPを削る。約1分戦えるよう多め（要調整）。
        MaxHp = 42;
        PanelRespawnDelay = 1.25f;

        PreTexPath = "res://char/enemy_hikage_pre.png";
        CryTexPath = "res://char/enemy_hikage_cry.png";
        PostTexPath = "res://char/enemy_hikage_post.png";
        PanelTexPath = "res://char/panel_hikage.png";
        BodyDisplayH = 46f;      // algo(36px)に近づけて小型化（でかすぎ対策）
        CryHoldDur = 8.0;        // 大泣き→笑顔までの尺（かけあいに合わせる）
    }

    public override void _Ready()
    {
        base._Ready();
        GetHud()?.ShowBossBar("ヒカゲ");
        GetHud()?.UpdateBossBar(HpRatio);
    }

    // 画面全体をうろつき、進行方向を向く。
    protected override void UpdateMovement(double delta)
    {
        if (!_hasTarget) PickTarget();
        Vector2 to = _moveTarget - GlobalPosition;
        if (to.Length() < 8f) { PickTarget(); to = _moveTarget - GlobalPosition; }
        Vector2 step = to.Normalized() * RoamSpeed * (float)delta;
        GlobalPosition += step;
        if (Mathf.Abs(step.X) > 0.01f) SetSpriteFlip(step.X < 0f); // 進行方向へ向く

        FirePatterns(delta);
    }

    // HPに応じた幾何学弾幕。フェーズ1=放射リング / 2=二重スパイラル / 3=花型。
    private void FirePatterns(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _fireT += delta;
        float hp = HpRatio;

        if (hp > 0.66f)
        {
            // フェーズ1：放射リング（毎回少しずつ回転）。ゆっくりで避けやすい。
            if (_fireT >= Di(1.1))
            {
                _fireT = 0;
                int k = Dn(18);
                float off = _volley * Mathf.DegToRad(9f);
                for (int i = 0; i < k; i++)
                {
                    float a = off + Mathf.Tau * i / k;
                    pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 70f, isEnemy: true, BulletR, 1);
                }
                _volley++;
            }
        }
        else if (hp > 0.33f)
        {
            // フェーズ2：二重スパイラル（連続回転）。
            if (_fireT >= Di(0.085))
            {
                _fireT = 0;
                _spiralAngle += Mathf.DegToRad(13f);
                for (int s = 0; s < 2; s++)
                {
                    float a = _spiralAngle + Mathf.Pi * s;
                    pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 90f, isEnemy: true, BulletR, 1);
                }
            }
        }
        else
        {
            // フェーズ3：花型（複数リングを角度・速度オフセットで重ねる）。やや密。
            if (_fireT >= Di(0.72))
            {
                _fireT = 0;
                int k = Dn(12);
                const int rings = 3;
                for (int r = 0; r < rings; r++)
                {
                    float off = _volley * Mathf.DegToRad(7f) + r * Mathf.DegToRad(12f);
                    float spd = 68f + r * 16f;
                    for (int i = 0; i < k; i++)
                    {
                        float a = off + Mathf.Tau * i / k;
                        pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd, isEnemy: true, BulletR, 1);
                    }
                }
                _volley++;
            }
        }
    }

    private void PickTarget()
    {
        _moveTarget = new Vector2(_rng.RandfRange(52f, 332f), _rng.RandfRange(36f, 182f));
        _hasTarget = true;
    }

    protected override void OnHpChanged() => GetHud()?.UpdateBossBar(HpRatio);

    protected override void OnCryStart()
    {
        GetHud()?.HideBossBar();
        _seq = true;
        _line = 0;
        _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        _seq = false;
        Finished = true;
        GetHud()?.ShowDialog("…うちも、ちゃんと わらえた。", "res://char/hikage_face_happy.png");
    }

    // ボスはヒカゲ強化フォロワーを付与（満員なら1体を強化）。
    protected override void GrantFollower()
    {
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Player pl)
            pl.AddHikageFollower(GlobalPosition);
    }

    // 大泣き中のセリフ送り。
    public override void _Process(double delta)
    {
        if (!_seq) return;
        _lineT += delta;
        if (_lineT >= LineDur)
        {
            _lineT = 0;
            _line++;
            if (_line >= Lines.Length) { _seq = false; return; }
            ShowLine();
        }
    }

    private void ShowLine()
    {
        var (text, algo) = Lines[_line];
        var hud = GetHud();
        if (hud == null) return;
        if (algo) hud.ShowDialog(text); // algo の立ち絵
        else hud.ShowDialog(text, "res://char/hikage_face_cry.png"); // ヒカゲの大泣き立ち絵
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
