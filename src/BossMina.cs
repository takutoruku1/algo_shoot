using Godot;

// BossMina : FINAL「暴走したミナ」。三人ぶんの穢れがミナの中で限界に達し暴走した姿。
// 役割反転——いつも見ているだけだった少年が、初めて自分で“光”を握り、ミナの穢れを撃ち祓う。
// HPを削り切る＝穢れを祓い、奥の“本当のミナ”に光が届く。短い邂逅のあと、Final（対話で帰還）へ。
public partial class BossMina : Enemy
{
    public bool Finished { get; private set; }

    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private Vector2 _moveTarget;
    private bool _hasTarget;
    private const float RoamSpeed = 38f;

    private double _fireT;
    private float _ringOff;
    private int _pattern;
    private int _beatsFired;
    private const int PatternCount = 5;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    // HPがこの割合を割るたびに弾幕パターンを変える。
    private static readonly float[] PatternThresholds = { 0.82f, 0.62f, 0.42f, 0.22f };

    // 邂逅のかけあい（who: 0=少年 / 1=ミナ）。本決着は Final（対話）に委ねるので、ここは短い一拍。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (0, "ミナ! ……ぼくだ。迎えに来た。", "res://char/shonen_gentle.png"),
        (1, "……ご主人、様? だめ……わたくしに、近づいては……穢れて、しまいます……", ""),
        (0, "うるさい。お前を置いて帰れるか。——今度は、ぼくが行く番だ。", "res://char/shonen_proud.png"),
    };

    protected override void OnEnemyReady()
    {
        _rng.Randomize();
        Points = 3000;
        BodyRadius = 10f;
        PanelCount = 6;          // 渦巻く悲鳴の言葉（黒い吹き出し）
        PanelInk = 2;
        OrbitRadius = 32f;
        SpinSpeed = 1.0f;
        PanelsFire = false;
        EnemyBulletSpeed = 86f;

        MaxHp = 56;              // ラスボス＝堅め
        PanelRespawnDelay = 1.2f;

        PreTexPath = "res://char/enemy_mina_pre.png";
        CryTexPath = "res://char/enemy_mina_post.png";
        PostTexPath = "res://char/enemy_mina_post.png";
        BodyDisplayH = 60f;
        CryHoldDur = 9999.0;
    }

    public override void _Ready()
    {
        base._Ready();
        GetHud()?.ShowBossBar("穢れたわたし");
        GetHud()?.UpdateBossBar(HpRatio);
    }

    protected override void UpdateMovement(double delta)
    {
        if (!_hasTarget) PickTarget();
        Vector2 to = _moveTarget - GlobalPosition;
        if (to.Length() < 8f) { PickTarget(); to = _moveTarget - GlobalPosition; }
        GlobalPosition += to.Normalized() * RoamSpeed * (float)delta;
        FirePattern(delta);
    }

    private void PickTarget()
    {
        _moveTarget = new Vector2(_rng.RandfRange(80f, 320f), _rng.RandfRange(36f, 104f));
        _hasTarget = true;
    }

    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(0.95)) { _fireT = 0; Ring(pool, Dn(16), 72f); } break;
            case 1: if (_fireT >= Di(0.8))  { _fireT = 0; Aimed(pool); } break;
            case 2: if (_fireT >= Di(1.0))  { _fireT = 0; Flower(pool, Dn(10)); } break;
            case 3: if (_fireT >= Di(0.075)){ _fireT = 0; Spiral(pool); } break;
            default: if (_fireT >= Di(1.1)) { _fireT = 0; Ring(pool, Dn(22), 66f); Ring(pool, Dn(22), 92f); } break;
        }
    }

    private void Ring(BulletPool pool, int k, float spd)
    {
        _ringOff += Mathf.DegToRad(8f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / k;
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd, isEnemy: true, 3.4f, 1);
        }
    }

    private void Aimed(BulletPool pool)
    {
        Vector2 d = AimAtPlayer();
        float baseA = Mathf.Atan2(d.Y, d.X);
        for (int i = -2; i <= 2; i++)
        {
            float a = baseA + i * Mathf.DegToRad(11f);
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 104f, isEnemy: true, 3.4f, 1);
        }
    }

    private void Flower(BulletPool pool, int petals)
    {
        _ringOff += Mathf.DegToRad(360f / petals / 2f);
        for (int i = 0; i < petals; i++)
        {
            float a = _ringOff + Mathf.Tau * i / petals;
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 64f, isEnemy: true, 3.6f, 1);
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 100f, isEnemy: true, 3.2f, 1);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(15f);
        for (int s = 0; s < 3; s++)
        {
            float a = _ringOff + Mathf.Tau * s / 3f;
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 92f, isEnemy: true, 3.2f, 1);
        }
    }

    private Vector2 AimAtPlayer()
    {
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Node2D pl)
        {
            var d = pl.GlobalPosition - GlobalPosition;
            if (d.LengthSquared() > 0.01f) return d.Normalized();
        }
        return new Vector2(-1, 0);
    }

    protected override void OnHpChanged()
    {
        GetHud()?.UpdateBossBar(HpRatio);
        if (_beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
        }
    }

    protected override void GrantFollower() { }

    protected override void OnCryStart()
    {
        GetHud()?.HideBossBar();
        var hud = GetHud();
        if (hud != null) hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd() => Finished = true;

    public override void _Process(double delta)
    {
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        _lineT += delta;

        if (_seq)
        {
            if (zEdge && _lineT >= 0.25)
            {
                _lineT = 0; _line++;
                if (_line >= Lines.Length)
                {
                    _seq = false;
                    var hud = GetHud();
                    if (hud != null) { hud.HoldBubble = false; hud.HideBubble(); }
                    EndCryNow();
                }
                else ShowLine();
            }
        }
    }

    private void ShowLine()
    {
        var (who, text, face) = Lines[_line];
        var hud = GetHud();
        if (hud == null) return;
        var kind = (Hud.LineKind)who;
        string portrait = kind == Hud.LineKind.Boy ? face : "res://char/mina_face.png";
        hud.ShowDialog(kind, text, portrait, otherName: "ミナ");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
