using Godot;

// BossRei : STAGE1「レイ（順位掲示板の海）」のボス＝穢れの核「二番のわたし」。
// 順位に縛られた悔しさの弾幕。剥がしてHPを削り切る＝奥の“本当のレイ”の光に届く＝改心。
// 改心の会話：少年がミナに託し、ミナが届ける（少年は正体を隠す）。禁止語「あなたのせいじゃない」は使わない。
public partial class BossRei : Enemy
{
    public bool Finished { get; private set; }

    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private Vector2 _moveTarget;
    private bool _hasTarget;
    private const float RoamSpeed = 42f;

    private double _fireT;
    private double _fireT2;   // フィナーレ用の第2タイマー（2スペル同時撃ち）
    private bool _finale;     // HP2割以下＝2スペル同時展開
    private float _ringOff;
    private int _pattern;
    private int _beatsFired;
    private const int PatternCount = 4;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    // HPがこの割合を割るたびに攻撃パターンを変える（独白は浄化のかけあいに集約）。
    private static readonly float[] PatternThresholds = { 0.78f, 0.50f, 0.26f };

    // スペルカード（RefrainHTML Danmaku v3 STAGE1 レイ＝順位掲示板・銀菫金ティール）。
    // index は _pattern と一致。切替時に弾形・色を変え、X風スペル宣言を出す。
    private static readonly (string name, BulletShape shape, Color tint)[] Spells =
    {
        ("どうせ二番",           BulletShape.Orb,     new Color("b9c2d0")), // 銀・全方位同心円
        ("努力は天才に勝てない", BulletShape.Diamond, new Color("9a72d9")), // 菫・回転スパイラル
        ("私を見て",             BulletShape.Star,    new Color("e8c45a")), // 金・星乱舞
        ("届かない",             BulletShape.Ring,    new Color("5fb8c0")), // ティール・中空リング
    };
    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        SetSpellVisual(s.shape, s.tint);
        GetHud()?.AnnounceSpell("レイ", "@rei_compete", s.name, s.tint);
    }

    // 改心のかけあい（who: 0=少年 / 1=ミナ / 2=レイ）。少年の言葉をミナの声で“中継”して届ける（伏線③の布石）。
    // 設計書 v2 [P-01b] のボス節を順序通りに：レイの悔しさ → 中継 → 好敵手だったと認める。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "次は、私が勝つから。……次は。次は……", ""),
        (2, "なのに、なんで——なんで、戦ってくれないのよ!", ""),
        (5, "——きみの努力を、ずっと見ていた者がいる。", ""),
        (5, "本気で戦う価値のある、唯一の好敵手だと、思っていた者がいる。", ""),
        (2, "ほんとに、私……ちゃんと、ライバルだった……?", ""),
    };

    protected override void OnEnemyReady()
    {
        _rng.Randomize();
        Points = 1500;
        BodyRadius = 9f;
        PanelCount = 5;          // 「二番」の言葉（黒い吹き出し）
        PanelInk = 2;
        OrbitRadius = 26f;
        SpinSpeed = 0.9f;
        PanelsFire = false;
        EnemyBulletSpeed = 82f;

        MaxHp = 38;
        PanelRespawnDelay = 1.4f;

        PreTexPath = "res://char/enemy_rei_pre.png";
        CryTexPath = "res://char/enemy_rei_post.png";
        PostTexPath = "res://char/enemy_rei_post.png";
        BodyDisplayH = 52f;
        CryHoldDur = 9999.0;
    }

    public override void _Ready()
    {
        base._Ready();
        GetHud()?.ShowBossBar("二番のわたし");
        GetHud()?.UpdateBossBar(HpRatio);
        ApplySpell();
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
        _moveTarget = new Vector2(_rng.RandfRange(70f, 320f), _rng.RandfRange(38f, 108f));
        _hasTarget = true;
    }

    // 攻撃パターン（セリフを挟むたびに変化）。
    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        if (_finale) { FireFinale(pool, delta); return; }
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(1.0)) { _fireT = 0; Ring(pool, Dn(14), 70f); } break;
            case 1: if (_fireT >= Di(1.1)) { _fireT = 0; Ring(pool, Dn(18), 76f); } break;
            case 2: if (_fireT >= Di(0.7)) { _fireT = 0; Aimed(pool); } break;
            default: if (_fireT >= Di(0.085)) { _fireT = 0; Spiral(pool); } break;
        }
    }

    // フィナーレ（HP2割以下）：「私を見て」(星金リング)＋「届かない」(ティールのリング螺旋)を同時展開。
    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.9)) { _fireT = 0; SetSpellVisual(Spells[2].shape, Spells[2].tint); Ring(pool, Dn(14), 72f); }
        if (_fireT2 >= Di(0.085)) { _fireT2 = 0; SetSpellVisual(Spells[3].shape, Spells[3].tint); Spiral(pool); }
    }

    private void Ring(BulletPool pool, int k, float spd)
    {
        _ringOff += Mathf.DegToRad(9f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / k;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd, 3.4f);
        }
    }

    private void Aimed(BulletPool pool)
    {
        Vector2 d = AimAtPlayer();
        float baseA = Mathf.Atan2(d.Y, d.X);
        for (int i = -1; i <= 1; i++)
        {
            float a = baseA + i * Mathf.DegToRad(13f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 98f, 3.4f);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(13f);
        for (int s = 0; s < 2; s++)
        {
            float a = _ringOff + Mathf.Pi * s;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 90f, 3.2f);
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
            ApplySpell();
        }
        if (!_finale && HpRatio <= 0.2f)
        {
            _finale = true;
            GetHud()?.AnnounceSpell("レイ", "@rei_compete", Spells[2].name + "＋" + Spells[3].name, Spells[2].tint);
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
            return;
        }
    }

    private void ShowLine()
    {
        var (who, text, face) = Lines[_line];
        var hud = GetHud();
        if (hud == null) return;
        var kind = (Hud.LineKind)who;
        string portrait = kind switch
        {
            Hud.LineKind.Boy => face,
            Hud.LineKind.Other => "res://char/rei_face.png",
            _ => "res://char/mina_face.png", // ミナ・中継
        };
        hud.ShowDialog(kind, text, portrait, otherName: "レイ");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
