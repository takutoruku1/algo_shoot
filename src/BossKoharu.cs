using Godot;

// BossKoharu : STAGE3「こはる（永遠に夕食を作り続ける台所）」のボス＝穢れの核「むだなわたし」。
// 兄の余命を知り、家事＝祈りが砕けた無力感。怒り（他責）の下にある悲しみへ光を届ける。
// 専用立ち絵は未用意のため、Enemy の _Draw プレースホルダ（人型）で本体を表示する。
// 禁止語「あなたのせいじゃない」は使わない。祈りが届いていたことを伝えて解く。
public partial class BossKoharu : Enemy
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
    private const int PatternCount = 4;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    private const string SCocky = "res://char/shonen_face.png";

    // HPがこの割合を割るたびに攻撃パターンを変える（独白は浄化のかけあいに集約）。
    private static readonly float[] PatternThresholds = { 0.78f, 0.50f, 0.26f };

    // 浄化のかけあい（who: 0=少年 / 1=ミナ / 2=こはる）。少年はミナの声で“中継”して届ける。
    // 設計書 v2 [P-03] のボス節を順序通りに（ミナの気遣い・少年の取り繕いも含む）。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "お兄ちゃんが、いなくなる。", ""),
        (2, "あたしが何をつくっても、お兄ちゃんは——", ""),
        (1, "……ご主人様?", ""),
        (0, "……なんでもない。続けるぞ。", SCocky),
        (5, "——怒りの下にある悲しみを、ちゃんと悲しんでいい。", ""),
        (2, "あたしのごはんは、お兄ちゃんを助けられない……! 意味なんて、ない……!", ""),
        (5, "お兄さんが今日まで生きてこられたのは……きみの食卓が、あったからだ。", ""),
        (5, "祈りは、届いてたよ。ちゃんと。", ""),
        (2, "……ちゃんと、食べてくれるかな。今日は。", ""),
    };

    protected override void OnEnemyReady()
    {
        _rng.Randomize();
        Points = 1800;
        BodyRadius = 9f;
        PanelCount = 5;          // 「むだだよ」等の言葉（黒い吹き出し）
        PanelInk = 2;
        OrbitRadius = 26f;
        SpinSpeed = 0.85f;
        PanelsFire = false;
        EnemyBulletSpeed = 80f;

        MaxHp = 44;
        PanelRespawnDelay = 1.4f;

        PreTexPath = "res://char/enemy_koharu_pre.png";   // 穢れ・病んだ核
        CryTexPath = "res://char/enemy_koharu_post.png";  // 浄化＝改心を見せながら会話（その場で静止）
        PostTexPath = "res://char/enemy_koharu_post.png";
        BodyDisplayH = 52f;
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    public override void _Ready()
    {
        base._Ready();
        GetHud()?.ShowBossBar("むだなわたし");
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

    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(1.0)) { _fireT = 0; Ring(pool, Dn(16), 70f); } break;
            case 1: if (_fireT >= Di(1.1)) { _fireT = 0; FanDown(pool); } break;
            case 2: if (_fireT >= Di(0.7)) { _fireT = 0; Aimed(pool); } break;
            default: if (_fireT >= Di(0.085)) { _fireT = 0; Spiral(pool); } break;
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

    private void FanDown(BulletPool pool)
    {
        int k = Dn(9);
        for (int i = 0; i < k; i++)
        {
            float t = (float)i / (k - 1) - 0.5f;
            float a = Mathf.Pi / 2f + t * Mathf.DegToRad(78f);
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * EnemyBulletSpeed, isEnemy: true, 3.4f, 1);
        }
    }

    private void Aimed(BulletPool pool)
    {
        Vector2 d = AimAtPlayer();
        float baseA = Mathf.Atan2(d.Y, d.X);
        for (int i = -1; i <= 1; i++)
        {
            float a = baseA + i * Mathf.DegToRad(13f);
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 96f, isEnemy: true, 3.4f, 1);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(12f);
        for (int s = 0; s < 2; s++)
        {
            float a = _ringOff + Mathf.Pi * s;
            pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 88f, isEnemy: true, 3.2f, 1);
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
            Hud.LineKind.Other => "res://char/koharu_face.png",
            _ => "res://char/mina_face.png", // ミナ・中継
        };
        hud.ShowDialog(kind, text, portrait, otherName: "こはる");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
