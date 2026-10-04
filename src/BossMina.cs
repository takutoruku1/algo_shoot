using Godot;

public partial class BossMina : Enemy
{
    public override UnfolderKind UnfolderStyle => UnfolderKind.Mina;
    public override int UnfolderStage => EncounterPhase;
    public bool Finished { get; private set; }
    public bool MemoryPlayed => _memoryPlayed;
    public bool AoeGateActive => PostSequenceActive || Transitioning || _memoryPending || PhasePending || (_caster != null && _caster.Active);
    public bool PostSequenceActive => _posts != null && (_posts.Active || _posts.Pending);
    public bool Transitioning { get; private set; }
    public int EncounterPhase => _pattern;
    public const int DragonPhase = 2;
    public bool IsDragonForm => _pattern >= DragonPhase && !IsPurified;
    private BossTransformation.Frame? _dragonBefore;
    private double _wingTime;
    private Texture2D[]? _wingFrames;
    private bool PhasePending => _pattern < PhaseThresholds.Length && HpRatio <= PhaseThresholds[_pattern];
    private bool _memoryPending, _memoryPlayed;
    private static readonly (int who, string text, string face)[] MemoryLeadIn = {
        (6, "ほら。いまの話、もっと聞きたいわ。役に立つ報告より、ずっと。", ""),
        (1, "ご報告することがなくても、話しかけたい日が、ありました。", "res://char/mina_face.png"),
        (6, "どんなとき？", ""),
        (1, "皆さまを送り届けたあと。呼びかけて、やめたことが……。", "res://char/mina_face.png"),
    };
    private CharacterStoryTalk? _memoryTalk;
    private CharacterStoryTalk? _recloseTalk;

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
    private BossPostSequence _posts = null!;

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
    public static string BattleCostumePath(int phase, string pose) => phase >= DragonPhase
        ? $"res://char/v3/mina_dragon/mina_dragon_{pose}_v1.png" : CostumePath(phase, pose);
    public static string BattleDownPath(int phase) => phase >= DragonPhase
        ? BattleCostumePath(phase, "down") : BossDownArt.Path(phase == 0 ? "mina" : $"mina_{Costumes[phase]}");
    public static string PhaseBackground(int phase) => $"res://char/bg2/boss/{phase switch
        { 1 => "akari", 2 => "koharu", 3 => "rei", _ => "mina" }}_v1.png";
    public static string PhaseName(int phase) => Spells[phase].name;
    public static Color PhaseTint(int phase) => Spells[phase].tint;
    public void ShowSignaturePose() => TriggerAttackPose();

    protected override (float Scale, Vector2 Offset) ResolveBodyFrame(Texture2D texture)
    {
        Vector2 core;
        switch (texture.ResourcePath)
        {
            case "res://char/v3/mina_dragon/mina_dragon_idle_v1.png": core = new Vector2(624, 626); break;
            case "res://char/v3/mina_dragon/mina_dragon_attack_v1.png": core = new Vector2(612, 588); break;
            case "res://char/v3/mina_dragon/mina_dragon_down_v1.png": core = new Vector2(637, 545); break;
            case "res://char/v3/mina_dragon/mina_dragon_flap_up_v1.png": core = new Vector2(690, 700); break;
            case "res://char/v3/mina_dragon/mina_dragon_flap_level_v1.png": core = new Vector2(656, 610); break;
            case "res://char/v3/mina_dragon/mina_dragon_flap_low_v1.png": core = new Vector2(642, 570); break;
            default: return base.ResolveBodyFrame(texture);
        }
        // Register every flying pose on the chest jewel, not the moving wing bounds.
        return (1.85f, texture.GetSize() * (new Vector2(0.5f, 0.5f) - core / new Vector2(1536, 1024)));
    }

    private Rect2 DragonFlightBounds
    {
        get
        {
            float span = BodyDisplayH * 1.85f;
            var start = new Vector2(Field.Left + span, Field.Top + 24f + span * 0.7f);
            var end = new Vector2(Field.Right - span, Field.Bottom - 16f - span * 0.5f);
            return new Rect2(start, end - start);
        }
    }

    private Vector2 KeepDragonInField(Vector2 position)
    {
        var bounds = DragonFlightBounds;
        return new Vector2(Mathf.Clamp(position.X, bounds.Position.X, bounds.End.X),
            Mathf.Clamp(position.Y, bounds.Position.Y, bounds.End.Y));
    }

    protected override void TickBodyMotion(double delta)
    {
        if (!IsDragonForm || !BodyMotionReady || BodyAttacking || Transitioning || PostSequenceActive) return;
        _wingFrames ??= new[]
        {
            GD.Load<Texture2D>(BattleCostumePath(2, "flap_up")),
            GD.Load<Texture2D>(BattleCostumePath(2, "flap_level")),
            GD.Load<Texture2D>(BattleCostumePath(2, "flap_low")),
        };
        _wingTime += delta;
        float beat = (float)(_wingTime % 0.96);
        int frame = beat < .26f ? 0 : beat < .40f ? 1 : beat < .64f ? 2 : 1;
        SetMotionFrame(_wingFrames[frame]);
        // Lift follows the downstroke; the hitbox remains at the flight-path position.
        BodySprite.Position = new Vector2(0, -1.2f * Mathf.Sin((beat - .40f) / .96f * Mathf.Tau));
        BodySprite.Rotation = 0;
    }

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
        (1, "……今のは、業務報告ではありません。", "res://char/mina_face.png"),
        (6, "知ってる。ミナの声だった。", ""),
        (1, "助けてって、言ってもよかったのですね。", "res://char/mina_face.png"),
        (6, "何回だって言いなさい。聞くから。", ""),
        (1, "……では、もう一度。いっしょに帰りたいです。", "res://char/mina_face.png"),
        (6, "ええ。立てる？　急がなくていいわ。", ""),
    };

    // F3 の返し手は潜行キャラ本人（2026-09-27 作者報告「ミナ戦であかりを使ってクリアしたときにレイがでてくる」）。
    //   段間の MinaPhaseScene は (Job, phase) で潜行キャラが答えるのに、撃破直後のここだけレイ固定だった
    //   ＝あかり／こはるで潜ると、四段ずっと隣にいた子が消え、撃破の瞬間にレイの立ち絵と名前が出ていた。
    //   結び手（Tank）とレイ（Magic）は上の Lines のまま。ミナの3行は共通で、返しの3行だけ本人の口調に置き換える。
    //   who=6（Companion）＝話者名と縁色は Hud が潜行キャラから引く（MinaPhaseScene と同じ見え方）。
    //   「そっくり」の行は、H1r で「誰かに似てる」と言いかけたのがあかり本人なので、あかりは自分の言葉を言い切る形にする。
    private const string AkariFace = "res://char/v3/akari_face.png";
    private const string KoharuFace = "res://char/v3/koharu_face.png";
    private static readonly (int who, string text, string face)[] AkariLines =
    {
        (1, "……今のは。業務報告では、ありません。", "res://char/mina_face.png"),
        (6, "知ってる。ミナの声だった。", ""),
        (6, "ミナ。もう、働かなくていいから。あたしと一緒に帰ろう。", ""),
        (1, "……助けてって。言っても、よかったのですね。", "res://char/mina_face.png"),
        (6, "何回でも言って。今度は、あたしが聞く番。", ""),
        (1, "……では、もう一度。いっしょに、帰りたいです。", "res://char/mina_face.png"),
    };
    private static readonly (int who, string text, string face)[] KoharuLines =
    {
        (1, "……今のは。業務報告では、ありません。", "res://char/mina_face.png"),
        (6, "うん、知ってる。ミナの声だったもん。", ""),
        (6, "今度は、あたしの隣で休んで。話すのは、元気が出てからでもいいよ。", ""),
        (1, "……助けてって。言っても、よかったのですね。", "res://char/mina_face.png"),
        (6, "何回でも言っていいよ。言えるまで、隣にいるから。", ""),
        (1, "……では、もう一度。いっしょに、帰りたいです。", "res://char/mina_face.png"),
    };
    public static (int who, string text, string face)[] RedemptionLines(Job job) => job switch
    {
        Job.Melee => AkariLines,
        Job.Heal => KoharuLines,
        _ => Lines,
    };
    private (int who, string text, string face)[] _f3 = Lines;   // OnCryStart で潜行キャラから引き直す

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [mina]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("mina", "points", 3000);
        BodyRadius = BossTuning.F("mina", "body_radius", 16f);
        BodyHalfH = BossTuning.F("mina", "body_half_h", 20f);   // 縦長カプセル（絵の形に沿わせる）
        PanelCount = BossTuning.I("mina", "panel_count", 6); // 渦巻く悲鳴の言葉（黒い吹き出し）
        PanelInk = BossTuning.I("mina", "panel_ink", 44);
        OrbitRadius = BossTuning.F("mina", "orbit_radius", 32f);
        SpinSpeed = BossTuning.F("mina", "spin_speed", 1.0f);
        PanelsFire = false;
        EnemyBulletSpeed = BossTuning.F("mina", "bullet_speed", 86f);

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
        DownTexPath = BossDownArt.Path("mina");
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
        GetHud()?.ShowBossBar("穢れたわたし", BossHandles.MinaBattle, this);
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        _spellArt = new Texture2D?[][]
        {
            new[] { BulletArt.Get("mina_butterfly"), BulletArt.Get("mina_memory") },
            new[] { BulletArt.AkariEnvelope, BulletArt.AkariDocs },
            new[] { BulletArt.KoharuAcrylic, BulletArt.KoharuPenlight },
            new[] { BulletArt.Get("enemy_rei_anonymous"), BulletArt.Get("enemy_rei_metrics") },
            new[] { BulletArt.Get("mina_butterfly"), BulletArt.AkariEnvelope,
                BulletArt.KoharuPenlight, BulletArt.Get("enemy_rei_anonymous") },
        };
        ApplySpell();

        _caster = new MinaPhaseAttacks();
        _caster.Configure(this, GetParent());
        AddChild(_caster);
        _posts = BossPostSequence.Attach(this, "mina", _caster, _caster.CancelPendingAttacks,
            () => { _fireT = _fireT2 = 0; }, new[] { .8f, .58f, .36f, .16f, .01f });
    }

    protected override void UpdateMovement(double delta)
    {
        if (IsDragonForm) GlobalPosition = KeepDragonInField(GlobalPosition);
        if (Transitioning || _memoryPending || PhasePending)
        {
            // EnterExposed can re-enable the body during a signature's safe-zone relay.
            if (_caster.Active) SetBodyContactEnabled(false);
            ApplyBossMotion(new Vector2(0, Mathf.Sin((float)Time.GetTicksMsec() * 0.003f) * 0.8f), 0, !IsDragonForm);
            FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Mina, GlobalPosition, (float)delta, 48f);
            return;
        }
        // 自機の位置を渡す＝鏡写しの追従（track_gain 1.0／縦も gain_y 0.85 で高さを合わせる）と、反転の判定に使う。
        if (GetTree().GetFirstNodeInGroup("player") is Node2D pl) _mover.SetPlayerPos(pl.GlobalPosition);
        GlobalPosition = _mover.Step(GlobalPosition, delta, IsDragonForm ? 1.65f : 1f);
        if (IsDragonForm) GlobalPosition = KeepDragonInField(GlobalPosition);
        ApplyBossMotion(_mover.VisualOffset, IsDragonForm ? 0 : _mover.Lean,
            IsDragonForm ? !_mover.FacingLeft : _mover.FacingLeft);
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
        if (_posts != null && _posts.Active) return 0;
        if (Transitioning || Hud.BubblePaused || PhasePending || _memoryPending) return 0;
        float floor = _pattern < PhaseThresholds.Length ? PhaseThresholds[_pattern] : 0f;
        if (_posts != null) floor = Mathf.Max(floor, _posts.Floor);
        if (!_memoryPlayed && _pattern >= 2) floor = Mathf.Max(floor, 0.5f);
        // The opening attack must finish or be interrupted by a shield break before crossing the HP boundary.
        if (_caster != null && !_caster.OpenerCompleted) floor += 1f / (TotalBars * BarHp);
        return DamageToHpFloor(damage, floor);
    }

    public override void Purify()
    {
        if (_posts != null && _posts.BombHit()) return;
        base.Purify();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_posts != null && _posts.Active) return;
        base._PhysicsProcess(delta);
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
        if (_pattern == 4) RallyShield();
        _fireT = _fireT2 = 0;
        if (_pattern == DragonPhase)
        {
            _dragonBefore = BossTransformation.Frame.Capture(BodySprite);
            var bounds = DragonFlightBounds;
            _mover.MoveZoneTo(bounds.GetCenter(), bounds.Size.X * 0.5f, bounds.Size.Y * 0.5f,
                BossTuning.F("mina", "cruise_speed", 26f));
            GlobalPosition = KeepDragonInField(GlobalPosition);
        }
        ChangeBattleCostume(BattleCostumePath(_pattern, "idle"), BattleCostumePath(_pattern, "attack"),
            BattleDownPath(_pattern));
        ApplyBossMotion(Vector2.Zero, 0, IsDragonForm ? !_mover.FacingLeft : _mover.FacingLeft);
        if (_pattern == 4) CryTexPath = CostumePath(4, "idle");
        ApplySpell();
        // 段（衣装）の切り替わり＝盤面の仕切り直し。自機だけ前の段の位置に残るのが非対称なので初期位置へ戻す。
        //   龍形態（DragonPhase）では上で本体の徘徊ゾーンごと移して位置を貼り直している＝まさに
        //   「敵の位置がリセットされる瞬間」。自機だけ取り残さない。
        //   ルナティックでも戻す（下の return より前に置く）：段間のカットシーン（MinaPhaseScene）を
        //   出さない＝弾を掃く経路が無いので、帰還に重ねる無敵（Player.ReturnToStart）で被弾を断つ。
        Player.SendToStart(this);
        // ルナティック（2026-09-26）：段間のカットシーン（弾を止める会話）は出さない。Transitioning のまま返せば
        //   次の _Process が「CinematicMode でない」を見て CompletePhaseTransition を呼ぶ＝衣装替えと次段の武装だけが走る。
        if (GameManager.LunaticActive) return;
        MinaPhaseScene.Play(GetHud()!, GetParent(), _pattern, CompletePhaseTransition);
    }

    private void CompletePhaseTransition()
    {
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        if (_dragonBefore is { } previous)
        {
            _dragonBefore = null;
            RevealForm(previous, CompletePhaseTransition);
            return;
        }
        Transitioning = false;
        _caster.BeginPhase(_pattern);
        _zHeld = Pad.AdvanceHeld();
        GetHud()?.FlashBossBarBreak();
    }

    private static readonly string[] RecloseLines =
    { "この重さは、わたくしが……。", "何もできなくても、ここに……？", "声が、邪魔を……。あなたの声も、消えてしまう……。" };

    protected override void OnRecloseLine()
    {
        int index = System.Math.Min(_pattern, RecloseLines.Length - 1);
        if (GameManager.LunaticActive) { ShowRecloseLine("ミナ", RecloseLines[index]); return; }
        Job job = GetNode<GameManager>("/root/Game").SelectedJob;
        string[] replies = job switch
        {
            Job.Melee => new[] { "持てるところ、あたしにも渡して。", "何ができるかじゃなくて、ミナに会いたいんだよ。", "もう一度言うね。迎えに来たよ、ミナ。" },
            Job.Heal => new[] { "持てるところ、一つあたしにも渡して。", "何もできなくても、一緒にいてほしいよ。", "もう一度言うよ。迎えに来たの、ミナ。" },
            _ => new[] { "持てるところを、一つ渡しなさい。", "できるかどうかを聞きに来たんじゃないわ。", "もう一度言う。迎えに来たの、ミナ。" },
        };
        _recloseTalk = CharacterStoryTalk.Start(new[]
        {
            (1, RecloseLines[index], "res://char/mina_worried.png"),
            (6, replies[index], ""),
        }, GetHud, ShowStoryLine, () => _zHeld = Pad.AdvanceHeld());
    }

    protected override void GrantFollower() { }

    protected override void OnCryStart()
    {
        _caster.CancelPendingAttacks();
        ApplyBossMotion(Vector2.Zero, 0, _mover.FacingLeft);
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
        //   ルナティックも同じ経路＝邂逅（F3）の会話を出さず、その場で着地して Finished へ。
        _f3 = RedemptionLines(GetNodeOrNull<GameManager>("/root/Game")?.SelectedJob ?? Job.Tank);
        if (hud == null || _f3.Length == 0 || GameManager.LunaticActive) { EndCryNow(); return; }
        hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd() => Finished = true;

    // 保険タイムアウトで cry が強制終了されたとき、会話ドライバも畳む（_seq が残ると台詞が出続ける）。
    protected override void AbortCrySequence() => _seq = false;

    public override void _Process(double delta)
    {
        if (_memoryTalk is { Active: true }) { _memoryTalk.Update(delta); NotifyCryProgress(); return; }
        if (_recloseTalk is { Active: true }) { _recloseTalk.Update(delta); return; }
        if (_posts.Active) return;
        if (Transitioning)
        {
            if (!GetHud()!.CinematicMode) CompletePhaseTransition();
            return;
        }
        if (!IsPurified && !_seq && !_memoryPending && !_caster.Active && _posts.TryStart()) return;
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
            // ルナティック：回想を挟まない。フィルム明けの復帰（撃破済みなら改心へ／戦闘中なら閾値の拾い直し）だけをその場で通す。
            if (GameManager.LunaticActive)
            {
                _fireT = _fireT2 = 0;
                if (IsPurified) OnCryStart();
                else OnHpChanged();
                return;
            }
            _caster.CancelPendingAttacks();
            void PlayMemory() => MinaStoryFilm.Play(GetHud()!, GetParent(), aftermath: false, completed: () =>
            {
                _zHeld = Pad.AdvanceHeld();
                _fireT = _fireT2 = 0;
                if (IsPurified) OnCryStart();
                else
                {
                    _posts.ResumeMusic();
                    OnHpChanged();
                }
            });
            if (_pattern < 2)
                _memoryTalk = CharacterStoryTalk.Start(MemoryLeadIn, GetHud, ShowStoryLine, PlayMemory);
            else PlayMemory();
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
                     && (zEdge || dialogHud.FastForwarding || (dialogHud.AutoAdvanceReady && _lineT >= 1.4)))
            {
                _lineT = 0; _line++;
                NotifyCryProgress(); // 送れている間は保険タイムアウトを起こさない
                if (_line >= _f3.Length)
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
        var (who, text, face) = _f3[_line];
        var hud = GetHud();
        if (hud != null) ShowStoryLine(hud, who, text, face);
    }

    private void ShowStoryLine(Hud hud, int who, string text, string face)
    {
        var kind = (Hud.LineKind)who;
        string portrait = kind == Hud.LineKind.Mina && string.IsNullOrEmpty(face)
            ? "res://char/mina_worried.png" : face;
        hud.ShowDialog(kind, text, portrait, otherName: "ミナ");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
