using Godot;

// BossMina : FINAL「穢れたわたし」（案C・仮台本 08 F2/F3）。三人ぶんの穢れがミナの中で限界に達した姿。
// 自機は通信路を通る「あなたの光」。ミナ自身が抱えた穢れを撃ち祓う。
// HPを削り切る＝穢れを祓い、核が開く。短い邂逅（F3）のあと、Final（F4 の頂点）へ。
public partial class BossMina : Enemy
{
    public bool Finished { get; private set; }
    public bool MemoryPlayed => _memoryPlayed;
    public bool AoeGateActive => Transitioning || _memoryPending || PhasePending || (_caster != null && _caster.Active);
    public bool Transitioning { get; private set; }
    public int EncounterPhase => _pattern;
    private bool PhasePending => _pattern < PhaseThresholds.Length && HpRatio <= PhaseThresholds[_pattern];
    private bool _memoryPending, _memoryPlayed;

    private readonly BossMover _mover = new BossMover();
    private const float RoamSpeed = 38f;

    private double _fireT;
    private double _fireT2;   // フィナーレ用の第2タイマー（2スペル同時撃ち）
    private float _ringOff;
    private int _pattern;
    private const int PatternCount = 5;
    private Texture2D?[][] _spellArt = null!;
    private int _visualPattern, _artIndex;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    private MinaPhaseAttacks _caster = null!;

    // ── INI 外出しのバランス値（config/boss_stats.ini [mina]。読めなければ現行既定値）──
    private double _ringInterval = 0.95, _aimedInterval = 0.8, _flowerInterval = 1.0, _spiralInterval = 0.075;
    private int _ringCount = 16, _flowerPetals = 10, _aimedWing = 2; // wing=way数の片翼（5way→2）
    private float _ringSpeed = 72f, _aimedSpeed = 104f, _spiralSpeed = 92f;

    // HPがこの割合を割るたびに弾幕パターンを変える。
    public static readonly float[] PhaseThresholds = { 0.80f, 0.58f, 0.36f, 0.16f };
    private static readonly string[] Costumes = { "", "rain", "screen", "stream", "home" };
    public static string CostumePath(int phase, string pose) => phase == 0
        ? $"res://char/v3/boss_mina_body_{pose}.png"
        : $"res://char/v3/mina_phases/{Costumes[phase]}_{pose}.tres";
    public static string PhaseBackground(int phase) => $"res://char/bg2/boss/{phase switch
        { 1 => "akari", 2 => "koharu", 3 => "rei", _ => "mina" }}_v1.png";
    public static string PhaseName(int phase) => Spells[phase].name;
    public static Color PhaseTint(int phase) => Spells[phase].tint;
    public void ShowSignaturePose() => TriggerAttackPose();

    private static readonly (string name, BulletShape shape, Color tint)[] Spells =
    {
        ("穢れたわたし",         BulletShape.Diamond, new Color("b07cd0")),
        ("未送信の雨",           BulletShape.Star,    new Color("74b8e8")),
        ("消えない拍手",         BulletShape.Rice,    new Color("ee9bb7")),
        ("仮面の向こう",         BulletShape.Ring,    new Color("f0d98a")),
        ("わたしの声",           BulletShape.Orb,     new Color("85e8d0")),
    };
    private static BossMover.Attack StanceOf(int pattern) => (pattern % PatternCount) switch
    {
        1 => BossMover.Attack.Aimed,
        3 => BossMover.Attack.Wall,
        4 => BossMover.Attack.Spell,
        _ => BossMover.Attack.Ring,
    };

    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        _mover.SetNextAttack(StanceOf(_pattern));
        _artIndex = 0;
        SetMemoryVisual(_pattern);
        GetHud()?.SetBossBarTint(s.tint); // HPバーもスペル色へ（#26 フェーズ移行の可視化）
        GetHud()?.SetBossPhaseName($"ミナ / {s.name}");
    }

    private void SetMemoryVisual(int pattern)
    {
        _visualPattern = pattern;
        var spell = Spells[pattern];
        SetSpellVisual(spell.shape, spell.tint);
    }

    private void FireMemoryBullet(BulletPool pool, Vector2 pos, Vector2 velocity, float radius)
    {
        var bullet = FireBullet(pool, pos, velocity, radius);
        var art = _spellArt[_visualPattern];
        bullet.SetSprite(art[_artIndex++ % art.Length], 28f);
    }

    private static readonly (int who, string text, string face)[] Lines =
    {
        (1, "……今のは。業務報告では、ありません。", "res://char/mina_tears.png"),
        (2, "知ってる。あんたの声だった。", "res://char/v3/rei_face.png"),
        (1, "……助けてって。言っても、よかったのですね。", "res://char/mina_tears.png"),
        (2, "何回だって言いなさい。聞くから。", "res://char/v3/rei_face.png"),
        (1, "……では、もう一度。いっしょに、帰りたいです。", "res://char/mina_tears.png"),
    };

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [mina]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("mina", "points", 3000);
        BodyRadius = BossTuning.F("mina", "body_radius", 16f);
        BodyHalfH = BossTuning.F("mina", "body_half_h", 20f);   // 縦長カプセル（絵の形に沿わせる）
        PanelCount = BossTuning.I("mina", "panel_count", 6); // 渦巻く悲鳴の言葉（黒い吹き出し）
        PanelInk = BossTuning.I("mina", "panel_ink", 4); // 2→4（B-5: 終盤の強化に対しラスボスを最も厚く）
        OrbitRadius = BossTuning.F("mina", "orbit_radius", 32f);
        SpinSpeed = BossTuning.F("mina", "spin_speed", 1.0f);
        PanelsFire = false;
        EnemyBulletSpeed = BossTuning.F("mina", "bullet_speed", 86f);

        // HPバー本数は難易度別（ラスボス格は +2本：Easy4/Normal6/Hard7/Lunatic8。B-5）。INI hp_bars > 0 で固定上書き。
        int bars = BossTuning.I("mina", "hp_bars", 0);
        BarCount = bars > 0 ? bars : DiffBars(finalBoss: true);

        // 弾幕・ギミックの外出し値（INIに無ければフィールド初期値＝現行値のまま）。
        _ringInterval = BossTuning.F("mina", "ring_interval", 0.95f);
        _ringCount = BossTuning.I("mina", "ring_count", 16);
        _ringSpeed = BossTuning.F("mina", "ring_speed", 72f);
        _aimedInterval = BossTuning.F("mina", "aimed_interval", 0.8f);
        _aimedSpeed = BossTuning.F("mina", "aimed_speed", 104f);
        _aimedWing = Mathf.Max(0, BossTuning.I("mina", "aimed_ways", 5) / 2); // 奇数way→片翼数
        _flowerInterval = BossTuning.F("mina", "flower_interval", 1.0f);
        _flowerPetals = Mathf.Max(1, BossTuning.I("mina", "flower_petals", 10));
        _spiralInterval = BossTuning.F("mina", "spiral_interval", 0.075f);
        _spiralSpeed = BossTuning.F("mina", "spiral_speed", 92f);

        PreTexPath = "res://char/v3/boss_mina_body_idle.png";
        AttackTexPath = "res://char/v3/boss_mina_body_attack.png";
        // 改心の三段：穢れ(pre)→泣き(cry＝穢れ半剥がれ・決壊の涙)→清浄(post)。
        // cry は邂逅の会話尺いっぱい保持し、EndCryNow で post（本来の姿）へ着地（他ボスと同作法）。
        CryTexPath = "res://char/v3/boss_mina_body_cry.png";
        PostTexPath = "res://char/v3/boss_mina_body_post.png";
        BodyDisplayH = BossTuning.F("mina", "body_display_h", 56f);
        CryHoldDur = 9999.0;
    }

    public override void _Ready()
    {
        base._Ready();
        // ボス登場＝道中BGMからボスBGMへクロスフェード。ミナ戦本体は専用の実音源 BgmBossMina
        //   （Final/ヒカゲの汎用 BgmBoss は据え置き）。実音源は MusicTargetDb で粒を揃えて鳴る。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossMina);
        // 移動：スペルごとの立ち位置＋状態機械（待機→構え→攻撃→余韻）。数値は INI（[mina] の
        // cruise_speed / accel_time / stance_*）。ミナは「自機の動きを鏡のように追う」＝
        // stance_track_gain 1.0（自機と同じ x に寄る）。三ボスより速い。
        _mover.Configure("mina", new Vector2(Field.BossCenterX, Field.BossZoneCenterY), Field.BossZoneHalfW, Field.BossZoneHalfH);
        GetHud()?.ShowBossBar("穢れたわたし", BossHandles.MinaBattle);
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        _spellArt = new Texture2D?[][]
        {
            new[] { BulletArt.AkariEnvelope, BulletArt.Get("enemy_rei_anonymous") },
            new[] { BulletArt.AkariEnvelope, BulletArt.AkariDocs },
            new[] { BulletArt.KoharuAcrylic, BulletArt.KoharuPenlight },
            new[] { BulletArt.Get("enemy_rei_anonymous"), BulletArt.Get("enemy_rei_metrics") },
            new[] { GD.Load<Texture2D>("res://char/player/mina/mina_core_v1.png"), BulletArt.AkariEnvelope,
                BulletArt.KoharuPenlight, BulletArt.Get("enemy_rei_anonymous") },
        };
        ApplySpell();

        _caster = new MinaPhaseAttacks();
        _caster.Configure(this, GetParent());
        AddChild(_caster);
    }

    protected override void UpdateMovement(double delta)
    {
        if (_caster.Active || Transitioning || _memoryPending || PhasePending)
        {
            // EnterExposed can re-enable the body during a signature's safe-zone relay.
            if (_caster.Active) SetBodyContactEnabled(false);
            ApplyBossMotion(new Vector2(0, Mathf.Sin((float)Time.GetTicksMsec() * 0.003f) * 0.8f), 0, true);
            FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Mina, GlobalPosition, (float)delta, 48f);
            return;
        }
        // 自機の位置を渡す＝鏡写しの追従（track_gain 1.0／縦も gain_y 0.85 で高さを合わせる）と、反転の判定に使う。
        if (GetTree().GetFirstNodeInGroup("player") is Node2D pl) _mover.SetPlayerPos(pl.GlobalPosition);
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, _mover.FacingLeft);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Mina, GlobalPosition, (float)delta, 36f);
        FirePattern(delta);
    }

    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        // 全画面AOEの予告〜着弾中は通常弾を止める（避け先＝安置へ集中させる／弾の過密回避）。
        if (AoeGateActive) return;
        if (_pattern == 4) { FireFinale(pool, delta); return; }
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(_ringInterval)) { _fireT = 0; _mover.DeclareAttack(BossMover.Attack.Ring); Ring(pool, Dn(_ringCount), _ringSpeed); } break;
            case 1: if (_fireT >= Di(_aimedInterval)) { _fireT = 0; _mover.DeclareAttack(BossMover.Attack.Aimed); Aimed(pool); } break;
            case 2: if (_fireT >= Di(_flowerInterval)) { _fireT = 0; _mover.DeclareAttack(BossMover.Attack.Ring); Flower(pool, Dn(_flowerPetals)); } break;
            case 3: if (_fireT >= Di(_spiralInterval)) { _fireT = 0; _mover.DeclareAttack(BossMover.Attack.Wall); Spiral(pool); } break;
        }
    }

    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.95)) { _fireT = 0; SetMemoryVisual(4); Ring(pool, Dn(18), 70f); }
        if (_fireT2 >= Di(0.12)) { _fireT2 = 0; SetMemoryVisual(4); Spiral(pool); }
    }

    // 弾サイズ階層（#攻撃種ごとのサイズ差）：密集バラマキ(Ring)=小／連続糸(Spiral)=極小／
    //   自機狙いの精密弾(Aimed)=大／花弁(Flower)=遅い外周を大きく・速い内周を極小で「開花」を強調。
    //   当たり芯ドットは全形状共通描画＝大きくしても被弾点は埋もれない。
    private void Ring(BulletPool pool, int k, float spd)
    {
        _ringOff += Mathf.DegToRad(8f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / k;
            FireMemoryBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd, 3.0f);
        }
    }

    private void Aimed(BulletPool pool)
    {
        Vector2 d = AimAtPlayer();
        float baseA = Mathf.Atan2(d.Y, d.X);
        for (int i = -_aimedWing; i <= _aimedWing; i++)
        {
            float a = baseA + i * Mathf.DegToRad(11f);
            FireMemoryBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _aimedSpeed, 4.0f);
        }
    }

    private void Flower(BulletPool pool, int petals)
    {
        _ringOff += Mathf.DegToRad(360f / petals / 2f);
        for (int i = 0; i < petals; i++)
        {
            float a = _ringOff + Mathf.Tau * i / petals;
            FireMemoryBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 64f, 4.0f);
            FireMemoryBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 100f, 2.6f);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(15f);
        for (int s = 0; s < 3; s++)
        {
            float a = _ringOff + Mathf.Tau * s / 3f;
            FireMemoryBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _spiralSpeed, 2.6f);
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

    // 本体に弾が刺さった一拍を移動側へ渡す（小さくのけぞって戻る）。当たり判定の中心は
    // 最大4px・時定数0.12sでしか動かない＝弾避けの公平性は保つ（BossMover.OnHit のコメント参照）。
    protected override void OnBodyDamaged(Vector2 fromDir) => _mover.OnHit(fromDir);

    protected override int LimitBodyDamage(int damage)
    {
        if (Transitioning || Hud.BubblePaused || PhasePending || _memoryPending) return 0;
        float floor = _pattern < PhaseThresholds.Length ? PhaseThresholds[_pattern] : 0f;
        if (!_memoryPlayed && _pattern >= 2) floor = Mathf.Max(floor, 0.5f);
        // Each costume gets its opening attack before the next HP boundary can be crossed.
        if (_caster != null && !_caster.OpenerCompleted) floor += 1f / (TotalBars * BarHp);
        return DamageToHpFloor(damage, floor);
    }

    protected override void OnHpChanged()
    {
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        if (!_memoryPlayed && HpRatio <= 0.5f) _memoryPending = true;
    }

    private void BeginPhaseTransition()
    {
        Transitioning = true;
        _caster.CancelPendingAttacks();
        _pattern++;
        _fireT = _fireT2 = 0;
        ChangeBattleCostume(CostumePath(_pattern, "idle"), CostumePath(_pattern, "attack"));
        if (_pattern == 4) CryTexPath = CostumePath(4, "idle");
        ApplySpell();
        MinaPhaseScene.Play(GetHud()!, GetParent(), _pattern, CompletePhaseTransition);
    }

    private void CompletePhaseTransition()
    {
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        Transitioning = false;
        _caster.BeginPhase(_pattern);
        _zHeld = Pad.AdvanceHeld();
        GetHud()?.FlashBossBarBreak();
    }

    protected override void OnBreakCue()
    {
        var (name, line) = _pattern switch
        {
            1 => ("あかり", "届いてる。もう一歩、こっちへ！"),
            2 => ("こはる", "その声、消さないで。ちゃんと聞いてるよ！"),
            3 => ("レイ", "顔、上げて。ここからは一人でやらせない。"),
            4 => ("ミナ", "……聞こえています。帰り道を、開いてください！"),
            _ => ("ミナ", "……いけません。まだ、近づいては……。"),
        };
        GetHud()?.ShowBossLine(name, line, UiKit.Purify, 3.2);
    }

    private static readonly string[] RecloseLines =
    {
        "この重さは、わたくしが……。",
        "……返事を、待っていても、よいのですか。",
        "何もできなくても、ここに……？",
        "……消さずに。今度こそ、言葉に……。",
        "声が、邪魔を……でも。もう、聞こえています。",
    };

    protected override void OnRecloseLine()
        => ShowRecloseLine("ミナ", RecloseLines[_pattern]);

    protected override void GrantFollower() { }

    protected override void OnCryStart()
    {
        _caster.CancelPendingAttacks();
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        if (!_memoryPlayed)
        {
            _memoryPending = true;
            return;
        }
        // 会話を出せない状況（Hud が取れない／台詞が無い）なら会話に入らず即着地させる
        //   ＝送るものが無いのに EndCryNow を待ち続けて Finished が立たない詰まりを断つ。
        if (hud == null || Lines.Length == 0) { EndCryNow(); return; }
        hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd() => Finished = true;

    // 保険タイムアウトで cry が強制終了されたとき、会話ドライバも畳む（_seq が残ると台詞が出続ける）。
    protected override void AbortCrySequence() => _seq = false;

    public override void _Process(double delta)
    {
        if (Transitioning)
        {
            if (!GetHud()!.CinematicMode) CompletePhaseTransition();
            return;
        }
        if (!IsPurified && !_seq && !Hud.BubblePaused && !_caster.Active && PhasePending
            && (!_memoryPending || _pattern < 2))
        {
            BeginPhaseTransition();
            return;
        }
        if (_memoryPending && !_seq && !Hud.BubblePaused && !_caster.Active)
        {
            _memoryPending = false;
            _memoryPlayed = true;
            _caster.CancelPendingAttacks();
            MinaStoryFilm.Play(GetHud()!, GetParent(), aftermath: false, completed: () =>
            {
                _zHeld = Pad.AdvanceHeld();
                _fireT = _fireT2 = 0;
                if (IsPurified) OnCryStart();
                else
                {
                    Audio.Instance?.Music(Audio.Instance.BgmBossMina, 0.8f);
                    OnHpChanged();
                }
            });
            return;
        }
        // 改心の会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        _lineT += delta;

        if (_seq)
        {
            var dialogHud = GetHud()!;
            if (zEdge && _lineT >= 0.25 && !dialogHud.DialogRevealed)
            {
                dialogHud.RevealDialogNow();
                _lineT = 0;
                NotifyCryProgress();
            }
            else if (_lineT >= 0.25 && dialogHud.DialogRevealed
                     && (zEdge || dialogHud.FastForwarding || (dialogHud.AutoAdvance && _lineT >= 1.4)))
            {
                _lineT = 0; _line++;
                NotifyCryProgress(); // 送れている間は保険タイムアウトを起こさない
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
        // F3 に出るのは ミナ(1) と レイ(2)。who=2 の話者名は otherName で決まるので「レイ」を渡す。
        string portrait = string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face; // 行ごと差し替え可（他ステージと同方式）
        hud.ShowDialog(kind, text, portrait, otherName: "レイ");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
