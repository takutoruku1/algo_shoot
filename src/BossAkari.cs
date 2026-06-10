using Godot;

// BossAkari : STAGE1「あかり（雨の教室）」のボス＝穢れの核「ゆるせないわたし」。
// 自責の言葉（黒い吹き出し＝パネル）を旋回させ、下向きの自責弾を撒く。
// パネルを剥がしてHPを削り切る＝奥の“本当のあかり”の光に届く＝浄化（改心）。
// 浄化後は改心の姿を見せながら、少年（正体を隠したまま）が普遍化した言葉を贈る。フォロワーにはしない。
public partial class BossAkari : Enemy
{
    public bool Finished { get; private set; }

    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private Vector2 _moveTarget;
    private bool _hasTarget;
    private const float RoamSpeed = 40f;

    private double _fireT;
    private float _ringOff;
    private int _pattern;       // 現在の攻撃パターン（セリフを挟むたびに変化）
    private int _beatsFired;    // 流した独白の数
    private const int PatternCount = 4;

    // 戦闘中に挟む“あかりの自責の独白”。HPがこの割合を下回るごとに1つ流し、攻撃パターンを変える。
    private static readonly (float hp, string line)[] Beats =
    {
        (0.78f, "……あたしが、呼び出したりしなければ。"),
        (0.52f, "あたしのせいだ。あたしが、車道に飛び出したから。"),
        (0.26f, "ごめんなさい……あなたを、好きに、なんて。"),
    };

    // 浄化時のかけあい（who: 0=少年テロップ / 1=ミナ立ち絵 / 2=あかり立ち絵）
    private bool _seq;
    private int _line;
    private double _lineT;
    private const double LineDur = 3.4;
    private static readonly (int who, string text)[] Lines =
    {
        (0, "きみが誰かを想って、その人に拒まれて、それでも喪ったとして——"),
        (0, "想ったことも、拒まれたことも、喪ったことも、きみのせいじゃない。"),
        (2, "……あったかい、声がした気がする。だれ、だったんだろう。"),
        (1, "さあ。……アホなご主人様の、お知り合いでは?"),
    };

    protected override void OnEnemyReady()
    {
        _rng.Randomize();
        Points = 1500;
        BodyRadius = 9f;
        PanelCount = 5;          // 自責の言葉（黒い吹き出し）
        PanelInk = 2;
        OrbitRadius = 26f;
        SpinSpeed = 0.9f;
        PanelsFire = false;      // 攻撃は本体の自責弾
        EnemyBulletSpeed = 80f;

        MaxHp = 40;
        PanelRespawnDelay = 1.4f;

        PreTexPath = "res://char/enemy_akari_pre.png";
        CryTexPath = "res://char/enemy_akari_post.png";  // 浄化＝改心を見せながら会話（その場で静止）
        PostTexPath = "res://char/enemy_akari_post.png";
        // パネルは専用素材なし → Panel のプレースホルダ（黒い「・・・」吹き出し）を使う
        BodyDisplayH = 52f;
        CryHoldDur = 14.5;       // 改心を見せて会話する尺
    }

    public override void _Ready()
    {
        base._Ready();
        GetHud()?.ShowBossBar("ゆるせないわたし");
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
        _moveTarget = new Vector2(_rng.RandfRange(70f, 320f), _rng.RandfRange(38f, 108f));
        _hasTarget = true;
    }

    // 攻撃パターン（セリフを挟むたびに _pattern が変わる）。
    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= 1.0) { _fireT = 0; FanDown(pool); } break;       // 下向きの雨の扇
            case 1: if (_fireT >= 1.2) { _fireT = 0; Ring(pool); } break;          // 回転する放射リング
            case 2: if (_fireT >= 0.7) { _fireT = 0; AimedSpread(pool); } break;   // 自機狙いの3way連射
            default: if (_fireT >= 0.085) { _fireT = 0; Spiral(pool); } break;     // 二重スパイラル
        }
    }

    private void FanDown(BulletPool pool)
    {
        const int k = 9;
        for (int i = 0; i < k; i++)
        {
            float t = (float)i / (k - 1) - 0.5f;
            float a = Mathf.Pi / 2f + t * Mathf.DegToRad(80f);
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * EnemyBulletSpeed, isEnemy: true, 3.4f, 1);
        }
    }

    private void Ring(BulletPool pool)
    {
        const int k = 16;
        _ringOff += Mathf.DegToRad(11f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / k;
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 72f, isEnemy: true, 3.4f, 1);
        }
    }

    private void AimedSpread(BulletPool pool)
    {
        float baseA = Mathf.Atan2(AimAtPlayer().Y, AimAtPlayer().X);
        for (int i = -1; i <= 1; i++)
        {
            float a = baseA + i * Mathf.DegToRad(14f);
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 96f, isEnemy: true, 3.4f, 1);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(13f);
        for (int s = 0; s < 2; s++)
        {
            float a = _ringOff + Mathf.Pi * s;
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 90f, isEnemy: true, 3.2f, 1);
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
        // HPが閾値を割るたびに“あかりの独白”を挟み、攻撃パターンを変える。
        if (_beatsFired < Beats.Length && HpRatio <= Beats[_beatsFired].hp)
        {
            var (_, line) = Beats[_beatsFired];
            GetHud()?.ShowDialog(line, "res://char/akari_face.png");
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
        }
    }

    protected override void GrantFollower() { } // 新canonにフォロワーは無い

    protected override void OnCryStart()
    {
        GetHud()?.HideBossBar();
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        _seq = false;
        Finished = true;
    }

    public override void _Process(double delta)
    {
        if (!_seq) return;
        _lineT += delta;
        if (_lineT >= LineDur)
        {
            _lineT = 0; _line++;
            if (_line >= Lines.Length) { _seq = false; return; }
            ShowLine();
        }
    }

    private void ShowLine()
    {
        var (who, text) = Lines[_line];
        var hud = GetHud();
        if (hud == null) return;
        if (who == 0) hud.ShowMessage(text);                                  // 少年（声＝テロップ）
        else if (who == 1) hud.ShowDialog(text, "res://char/mina_face.png");  // ミナ
        else hud.ShowDialog(text, "res://char/akari_face.png");              // あかり
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
