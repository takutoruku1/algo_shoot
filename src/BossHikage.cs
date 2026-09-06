using Godot;

// W0 専用・非正典。正典導線からは到達しない（2026-09-06 ユーザー決定: ヒカゲは使わない）。以後この系統への追加投資はしない。
// BossHikage : W0 climax の炎上中ボス「ヒカゲ」。
// ・HP制（パネルを剥がすたびHP減＋パネル補充）で約1分の持久戦。HPバーを表示。
// ・画面全体をうろつきながら攻撃し、進行方向を向く。
// ・HP0で【浄化→大泣き→最高の笑顔】の3段階で改心し、algo の特別フォロワー（ヒカゲ強化）になる。
public partial class BossHikage : Enemy
{
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();
    private const float RoamSpeed = 72f;

    // 幾何学弾幕（HPフェーズで変化）
    private double _fireT;
    private float _spiralAngle;
    private int _volley;
    private const float BulletR = 3.4f;

    // ── INI 外出しのバランス値（config/boss_stats.ini [hikage]。読めなければ現行既定値）──
    private double _p1Interval = 1.1, _p2Interval = 0.085, _p3Interval = 0.72;
    private int _p1Count = 18, _p3Count = 12;
    private float _p1Speed = 70f, _p2Speed = 90f;

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
        // 主要バランス値は INI（config/boss_stats.ini [hikage]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("hikage", "points", 1200);
        BodyRadius = BossTuning.F("hikage", "body_radius", 9f);
        PanelCount = BossTuning.I("hikage", "panel_count", 6); // 黒い炎のリング（盾＝剥がしてHPを削る。攻撃は本体の弾幕）
        PanelInk = BossTuning.I("hikage", "panel_ink", 2);
        OrbitRadius = BossTuning.F("hikage", "orbit_radius", 28f);
        SpinSpeed = BossTuning.F("hikage", "spin_speed", 1.2f);
        PanelsFire = false;      // 弾はパネルでなく本体の幾何学弾幕で撃つ
        EnemyBulletSpeed = BossTuning.F("hikage", "bullet_speed", 95f);

        // HPバー方式（言葉のシールド＋無防備窓サイクル）。本数は難易度別（通常ボス）。INI hp_bars > 0 で固定上書き。
        int bars = BossTuning.I("hikage", "hp_bars", 0);
        BarCount = bars > 0 ? bars : DiffBars(finalBoss: false);

        // 弾幕の外出し値（INIに無ければフィールド初期値＝現行値のまま）。
        _p1Interval = BossTuning.F("hikage", "phase1_interval", 1.1f);
        _p1Count = BossTuning.I("hikage", "phase1_count", 18);
        _p1Speed = BossTuning.F("hikage", "phase1_speed", 70f);
        _p2Interval = BossTuning.F("hikage", "phase2_interval", 0.085f);
        _p2Speed = BossTuning.F("hikage", "phase2_speed", 90f);
        _p3Interval = BossTuning.F("hikage", "phase3_interval", 0.72f);
        _p3Count = BossTuning.I("hikage", "phase3_count", 12);

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
        // ボス登場＝道中BGMからヒカゲ専用テーマ（The_Frozen_Threshold）へクロスフェード。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossHikage);
        // 徘徊：ヒカゲは動きが速い炎上ボス＝ゾーンをやや広め・縦も広めに（速度はINI: roam_speed）。
        _mover.Configure(new Vector2(192f, 74f), 110f, 34f, BossTuning.F("hikage", "roam_speed", RoamSpeed), accelTime: 0.4f);
        GetHud()?.ShowBossBar("ヒカゲ", "@hikage_");
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
    }

    // 画面上部を漂い、進行方向へ少し傾きながら向く（向き反映は BossMover 経由）。
    protected override void UpdateMovement(double delta)
    {
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, _mover.FacingLeft);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Hikage, GlobalPosition, (float)delta, 28f);
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
            if (_fireT >= Di(_p1Interval))
            {
                _fireT = 0;
                int k = Dn(_p1Count);
                float off = _volley * Mathf.DegToRad(9f);
                for (int i = 0; i < k; i++)
                {
                    float a = off + Mathf.Tau * i / k;
                    pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _p1Speed, isEnemy: true, BulletR, 1);
                }
                _volley++;
            }
        }
        else if (hp > 0.33f)
        {
            // フェーズ2：二重スパイラル（連続回転）。
            if (_fireT >= Di(_p2Interval))
            {
                _fireT = 0;
                _spiralAngle += Mathf.DegToRad(13f);
                for (int s = 0; s < 2; s++)
                {
                    float a = _spiralAngle + Mathf.Pi * s;
                    pool.Spawn(GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _p2Speed, isEnemy: true, BulletR, 1);
                }
            }
        }
        else
        {
            // フェーズ3：花型（複数リングを角度・速度オフセットで重ねる）。やや密。
            if (_fireT >= Di(_p3Interval))
            {
                _fireT = 0;
                int k = Dn(_p3Count);
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

    protected override void OnHpChanged() => GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);

    // BREAK 合図：ヒカゲ戦にミナは絡まないため、話者なしの合図にする（共通の「ミナが煽る」は使わない）。
    protected override void OnBreakCue()
    {
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?
            .ShowBossLine("", "いまだ──黒い炎を、撃ち抜け!", UiKit.Kegare, 0.45 + 4.0);
    }

    protected override void OnCryStart()
    {
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        // 改心の解決音（未完→完）。戦闘テーマから温かくクロスフェード＝「届いた」（他ボスと同じ作法）。
        Audio.Instance?.PlayRedeem(3);
        _seq = true;
        _line = 0;
        _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        _seq = false;
        Finished = true;
        // 解決音（一度きり・ループなし）が鳴り終わっているので、晴れた道中曲へ復帰（CameoBoss と同じ作法）。
        Audio.Instance?.ResumeStageMusic();
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
