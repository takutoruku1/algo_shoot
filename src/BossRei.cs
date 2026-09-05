using Godot;

// BossRei : STAGE1「レイ（順位掲示板の海）」のボス＝穢れの核「二番のわたし」。
// 順位に縛られた悔しさの弾幕。剥がしてHPを削り切る＝奥の“本当のレイ”の光に届く＝改心。
// 改心の会話：少年がミナに託し、ミナが届ける（少年は正体を隠す）。禁止語「あなたのせいじゃない」は使わない。
public partial class BossRei : Enemy
{
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();
    private const float RoamSpeed = 42f;

    private double _fireT;
    private double _fireT2;   // フィナーレ用の第2タイマー（2スペル同時撃ち）
    private bool _finale;     // HP2割以下＝2スペル同時展開
    private bool _accelerated; // HP2割以下で戦闘BGMを一度だけ加速させた（多重発火防止）
    private float _ringOff;
    private int _pattern;
    private int _beatsFired;
    private const int PatternCount = 4;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    // 予測攻撃キャスター（通常テレグラフ＋安置リレー「最終選考」の発火に使う）。
    private AreaSpellCaster _caster = null!;
    // 安置リレー/全画面AOEの宣告〜最終着弾中か。StageRei が投稿弾（言葉弾）の湧きを止めるゲートに参照する
    //（安置円の中に言葉弾が刺さって「安置なのに被弾」になる理不尽を断つ）。
    public bool AoeGateActive => _caster != null && _caster.AoeActive;
    // ── 安置リレー「最終選考」（HP26%ワンショット）──
    //   全画面AOEの安置を2〜4回連結し、緑リングを頼りに走り継がせる終盤の山場。
    //   完走判定＝開始/終了時の Player.Lives 比較（被弾してもリレー自体は止まらない）。
    //   無被弾完走の報酬＝レイの動的セリフ＋最終安置に回復ハート1個（AddLife。♥上限ならスコアで返す）。
    private bool _relayFired;      // 発火ワンショット
    private bool _relayWatching;   // リレー進行中（完走判定待ち）
    private int _relayStartLives;

    // HPがこの割合を割るたびに攻撃パターンを変える（独白は浄化のかけあいに集約）。
    private static readonly float[] PatternThresholds = { 0.78f, 0.50f, 0.26f };

    // ── 「また逃げる」ギミック（#12 機構側）：攻めない時間が続くと弾密度が漸増する圧 ──
    //   パネル/本体への与ダメがゼロのまま 6秒 で+1、以降 4秒 ごとに+1（上限+2）。リング系の弾数と
    //   自機狙いの扇の枚数に乗る。1ヒットごとに1段階即緩む＝「攻めれば密度が下がる」。
    //   タイマーは SHIELDED（殴れる時間）だけ進める＝BREAK/RECLOSE や会話中の理不尽な加圧を防ぐ。
    private double _noDmgT;
    private int _pressure;
    private int _pressureMax = 2;        // INI: pressure_max
    private double _pressureDelay = 6.0; // INI: pressure_delay（最初の加圧までの与ダメゼロ許容時間）
    private double _pressureStep = 4.0;  // INI: pressure_step（2段階目までの追加時間）
    private double _tauntCd;             // 挑発字幕の連発防止
    private int _tauntIdx;

    // ── INI 外出しのバランス値（config/boss_stats.ini [rei]。読めなければ現行既定値）──
    private double _ringInterval = 1.0, _ring2Interval = 1.1, _aimedInterval = 0.7, _spiralInterval = 0.085;
    private int _ringCount = 14, _ring2Count = 18;
    private float _ringSpeed = 70f, _ring2Speed = 76f, _aimedSpeed = 98f, _spiralSpeed = 90f;
    private float _relayHp = 0.26f;
    // ボス戦のレイが被っている配信のガワ（笑顔固定）。中の人（rei_face 系）とは目と輪郭だけが同じ別の姿で、
    // 姿が違うこと自体が仕込み＝道中の中ボスは中の人、ボスはガワ。
    private const string RGawa = "res://char/v3/rei_gawa_b.png";

    // 挑発（ボスの動的セリフ演出＝ShowBossLine。弾は止めない。中継 who=5 は使わない）。
    private static readonly string[] TauntLines =
    {
        "……また、逃げるの?",
        "戦ってよ。わたしを、ちゃんと見てよ。",
    };

    // スペルカード（RefrainHTML Danmaku v3 STAGE1 レイ＝順位掲示板・銀菫金ティール）。
    // index は _pattern と一致。切替時に弾形・色を変え、X風スペル宣言を出す。
    private static readonly (string name, BulletShape shape, Color tint)[] Spells =
    {
        ("Q.E.D.",       BulletShape.Orb,     new Color("b9c2d0")), // 銀・全方位同心円（証明終わり）
        ("凡人の定理",   BulletShape.Diamond, new Color("9a72d9")), // 菫・回転スパイラル
        ("敵などいない", BulletShape.Star,    new Color("e8c45a")), // 金・星乱舞
        ("孤高の王座",   BulletShape.Ring,    new Color("5fb8c0")), // ティール・中空リング（裏に孤独）
    };
    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        SetSpellVisual(s.shape, s.tint);
        GetHud()?.SetBossBarTint(s.tint); // HPバーもスペル色へ（#26 フェーズ移行の可視化）
        GetHud()?.AnnounceSpell("レイ", "@rei_compete", s.name, s.tint);
    }

    // 改心のかけあい（who: 0=少年 / 1=ミナ / 2=レイ）。少年の言葉をミナの声で“中継”して届ける（伏線③の布石）。
    // 【届け方＝基準型：ミナ中継の“具体の記録”】（優先度2）。3戦で決定打の届け方を変える。この面が原型：
    //   少年が割れる→中継で二人だけの記録（地区予選の秒数）を明かす→レイが崩れる→未来を託して余白で抜く。
    //   あかり＝“名前を一点で呼ぶ”／こはる＝“少年の直接台詞（劇的アイロニー）”で抜く、と型を散らす（この面は温存）。
    // 躁的暴走＝全能感の誇示。傷＝唯一の好敵手が手を抜いた“頂点の孤独”。
    // 決定打は説明でなく“具体の記録”で示す（行動で証明）。最後は言わせず余白で抜く。
    private const string SGentle = "res://char/shonen_gentle.png";
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "完璧。わたしの式に、誤りはひとつもない。", ""),
        (2, "世界中の誰も、わたしには追いつけない。——当然でしょう?", ""),
        (2, "二位? 笑わせないで。わたし以外が、ぜんぶ二位なの。", ""),
        (2, "だから……だからなんで、あなたは、本気で来てくれないのよ!", ""),
        (0, "……ぼくのせいだ。ぼくが、本気で挑むのを、やめたから。", SGentle),     // 少年が割れる一拍
        (5, "——三年前の、地区予選。最終問題。きみは、二十二分で解いた。", ""),
        (5, "ぼくは、二十三分かかった。ぼくが本気で負けた相手は、後にも先にも、きみひとりだ。", ""),
        (2, "う、嘘……。だってあなた、誰……? なんで、それを……", "res://char/v3/rei_face_cry.png"), // 気丈さの決壊＝cry（こらえ顔での代用をやめる）
        (5, "強くなってくれ。ぼくが、もう一度挑みたくなるくらい。——挑めなく、なっても。", ""),
        (2, "…………。", "res://char/v3/rei_face_cry.png"),                             // 言わせない・余白（涙は流れたまま＝cry 保持）
    };

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [rei]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("rei", "points", 1500);
        BodyRadius = BossTuning.F("rei", "body_radius", 9f);
        PanelCount = BossTuning.I("rei", "panel_count", 5); // 「二番」の言葉（黒い吹き出し）
        PanelInk = BossTuning.I("rei", "panel_ink", 2);
        OrbitRadius = BossTuning.F("rei", "orbit_radius", 26f);
        SpinSpeed = BossTuning.F("rei", "spin_speed", 0.9f);
        PanelsFire = false;
        EnemyBulletSpeed = BossTuning.F("rei", "bullet_speed", 82f);

        // HPバー本数は難易度別（通常ボス：Easy2/Normal4/Hard5/Lunatic6）。総HP=BarHp×本数。
        // INI hp_bars > 0 で全難易度を固定本数に上書きできる。
        int bars = BossTuning.I("rei", "hp_bars", 0);
        BarCount = bars > 0 ? bars : DiffBars(finalBoss: false);

        // 弾幕・ギミックの外出し値（INIに無ければフィールド初期値＝現行値のまま）。
        _ringInterval = BossTuning.F("rei", "ring_interval", 1.0f);
        _ringCount = BossTuning.I("rei", "ring_count", 14);
        _ringSpeed = BossTuning.F("rei", "ring_speed", 70f);
        _ring2Interval = BossTuning.F("rei", "ring2_interval", 1.1f);
        _ring2Count = BossTuning.I("rei", "ring2_count", 18);
        _ring2Speed = BossTuning.F("rei", "ring2_speed", 76f);
        _aimedInterval = BossTuning.F("rei", "aimed_interval", 0.7f);
        _aimedSpeed = BossTuning.F("rei", "aimed_speed", 98f);
        _spiralInterval = BossTuning.F("rei", "spiral_interval", 0.085f);
        _spiralSpeed = BossTuning.F("rei", "spiral_speed", 90f);
        _pressureMax = BossTuning.I("rei", "pressure_max", 2);
        _pressureDelay = BossTuning.F("rei", "pressure_delay", 6.0f);
        _pressureStep = BossTuning.F("rei", "pressure_step", 4.0f);
        _relayHp = BossTuning.F("rei", "relay_hp", 0.26f);

        // v3 の本体＝ガワ（エフェクト無し・720px）。飾り枠・吹き出し・光の帯は BossParts が重ねる。
        PreTexPath = "res://char/v3/boss_rei_body_idle.png";
        AttackTexPath = "res://char/v3/boss_rei_body_attack.png"; // 撃つ一拍だけ差し替えて戻る
        // 改心の三段：ガワ(pre＝待機)→崩れ(cry＝被弾の姿勢)→中の人(post・360px のちび)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post へ着地する。
        CryTexPath = "res://char/v3/boss_rei_body_hit.png";
        PostTexPath = "res://char/v3/boss_rei_post.png";
        // 表示高は ini（body_display_h）。v3 の本体はエフェクト込みで焼いていないぶん、旧52だと小さく見える。
        BodyDisplayH = BossTuning.F("rei", "body_display_h", 72f);
        CryHoldDur = 9999.0;     // 自動終了させない＝cry を会話尺いっぱい保持（EndCryNow で post へ）
    }

    public override void _Ready()
    {
        base._Ready();
        // ボス登場＝道中BGMからレイ固有テーマへクロスフェード（モチーフが主音直前で半音落ちる＝未完）。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossRei);
        // 徘徊：画面上部のボスゾーンに収め、イージング＋ホバーで漂わせる（速度はINI: roam_speed）。
        _mover.Configure(new Vector2(200f, 70f), 90f, 28f, BossTuning.F("rei", "roam_speed", RoamSpeed));
        GetHud()?.ShowBossBar("孤高のわたし", "@rei_____");
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        ApplySpell();

        // 予測攻撃（テレグラフ）キャスター：技名宣告→予測線/予測エリア。数は難易度でスケール。
        _caster = new AreaSpellCaster();
        _caster.Configure("rei", GetParent());
        AddChild(_caster);
    }

    protected override void UpdateMovement(double delta)
    {
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, _mover.FacingLeft);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Rei, GlobalPosition, (float)delta, 32f);
        TickRelayWatch();
        TickPressure(delta);
        FirePattern(delta);
    }

    // 安置リレーの完走判定：AoeActive が落ちた瞬間（最終ホップの着弾終了）に残機を開始時と比較する。
    // 無被弾完走→レイが初めて認める一言＋最終安置に回復ハート1個（既存 Player.AddLife 経由。
    // 専用ドロップ機構は無いので即時付与＋その場にフィードバック表示。♥上限時はスコアボーナスで返す）。
    private void TickRelayWatch()
    {
        if (!_relayWatching || _caster == null || _caster.AoeActive) return;
        _relayWatching = false;
        if (GetTree().GetFirstNodeInGroup("player") is not Player pl) return;
        if (_relayStartLives < 0 || pl.Lives < _relayStartLives) return; // 被弾あり＝報酬なし（リレー自体は完走済み）
        GetHud()?.ShowBossLine("レイ", "……っ、合格。……なんて、言ってあげないんだから。", UiKit.Kegare, 2.4); // 本音（合格）が先に漏れ、負け惜しみで引っ込める＝改心の先取りはしない（scenario推敲済み）
        Vector2 at = _caster.LastChainSafe; // 最終安置＝報酬の出現位置
        if (pl.AddLife(1))
        {
            FxLayer.Instance?.DamageNumber(at, "♥+1", FxLayer.Heart, 13);
        }
        else
        {
            // ♥が上限で受け取れない時はスコアで返す（+1000。AddBulletCleared=+5 の純スコア加算を束ねる）。
            var game = GetNodeOrNull<GameManager>("/root/Game");
            if (game != null) for (int i = 0; i < 200; i++) game.AddBulletCleared();
            FxLayer.Instance?.DamageNumber(at, "+1000", FxLayer.Gold, 13);
        }
    }

    // 「また逃げる」圧の進行。UpdateMovement 経由＝会話中(BubblePaused)・登場演出中は自然に止まる。
    private void TickPressure(double delta)
    {
        if (_tauntCd > 0) _tauntCd -= delta;
        if (_caster != null && _caster.AoeActive) return; // 安置リレー中＝走るのが正解の時間。逃げ腰を咎めない
        if (!IsShieldPhase) return; // 殴れない時間（合図/窓/セリフ）は与ダメゼロを咎めない
        _noDmgT += delta;
        int want = _noDmgT < _pressureDelay ? 0
                 : Mathf.Min(_pressureMax, 1 + (int)((_noDmgT - _pressureDelay) / _pressureStep));
        if (want > _pressure)
        {
            _pressure = want;
            if (_tauntCd <= 0)
            {
                _tauntCd = 8.0;
                GetHud()?.ShowBossLine("レイ", TauntLines[Mathf.Min(_tauntIdx, TauntLines.Length - 1)], UiKit.Kegare, 1.6);
                _tauntIdx++;
            }
        }
    }

    // 有効打（パネルのインク削り／無防備窓の本体ヒット）のたびに圧が1段階すぐ緩む。
    public override void OnPlayerDealtDamage()
    {
        _noDmgT = 0;
        if (_pressure > 0) _pressure--;
    }

    // 攻撃パターン（セリフを挟むたびに変化）。
    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        // 安置リレー（最終選考）の宣告〜最終着弾中は通常弾を止める（避け先＝安置へ集中させる）。
        if (_caster != null && _caster.AoeActive) return;
        if (_finale) { FireFinale(pool, delta); return; }
        _fireT += delta;
        // 「また逃げる」圧：リング系は弾数+_pressure、自機狙いは扇の枚数が増える（Aimed 内）。
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(_ringInterval)) { _fireT = 0; TriggerAttackPose(); Ring(pool, Dn(_ringCount) + _pressure, _ringSpeed); } break;
            case 1: if (_fireT >= Di(_ring2Interval)) { _fireT = 0; TriggerAttackPose(); Ring(pool, Dn(_ring2Count) + _pressure, _ring2Speed); } break;
            case 2: if (_fireT >= Di(_aimedInterval)) { _fireT = 0; TriggerAttackPose(); Aimed(pool); } break;
            default: if (_fireT >= Di(_spiralInterval)) { _fireT = 0; TriggerAttackPose(); Spiral(pool); } break;
        }
    }

    // フィナーレ（HP2割以下）：「私を見て」(星金リング)＋「届かない」(ティールのリング螺旋)を同時展開。
    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.9)) { _fireT = 0; SetSpellVisual(Spells[2].shape, Spells[2].tint); Ring(pool, Dn(14) + _pressure, 72f); }
        if (_fireT2 >= Di(0.085)) { _fireT2 = 0; SetSpellVisual(Spells[3].shape, Spells[3].tint); Spiral(pool); }
    }

    // 弾サイズ階層（#攻撃種ごとのサイズ差）：密集バラマキ(Ring/扇)=小(隙間を編む読み)／
    //   連続糸(Spiral)=極小／自機狙いの精密弾(Aimed)=大（数が少ないぶん一発の重さで語る）。
    //   当たり芯ドットは Bullet._Draw が全形状共通で描くので、大きくしても被弾点は埋もれない。
    private void Ring(BulletPool pool, int k, float spd)
    {
        _ringOff += Mathf.DegToRad(9f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / k;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd, 3.0f);
        }
    }

    private void Aimed(BulletPool pool)
    {
        Vector2 d = AimAtPlayer();
        float baseA = Mathf.Atan2(d.Y, d.X);
        int wing = 1 + _pressure; // 「また逃げる」圧：3way→5way→7way（角度は不変＝扇が広がる）
        for (int i = -wing; i <= wing; i++)
        {
            float a = baseA + i * Mathf.DegToRad(13f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _aimedSpeed, 4.0f);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(13f);
        for (int s = 0; s < 2; s++)
        {
            float a = _ringOff + Mathf.Pi * s;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _spiralSpeed, 2.6f);
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
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        if (_beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
            ApplySpell();
        }
        // 適応演出：総HPの2割を割った瞬間に、実音源の戦闘BGMを一度だけ加速させる（緊迫の高揚）。
        //   PitchScale を 0.6秒かけて約1.15へ滑らかに上げる＝ピッチも少し上がる（合意済み）。
        //   フェードで別曲に切り替わると Audio.Music() 側で 1.0 に戻る＝ボス戦を抜けたら通常速度。
        if (!_accelerated && HpRatio <= 0.2f)
        {
            _accelerated = true;
            Audio.Instance?.SetMusicSpeed(1.15f);
        }
        // 安置リレー「最終選考」：HP26%（INI: relay_hp）を割った瞬間に一度だけ（パターン最終切替と同じ節目＝終盤の山）。
        // ホップ数と距離帯は難易度別。距離上限は到達限界（(予兆1.6s×WarnMul−反応0.3s)×150px/s＋安置r30）内：
        //   Easy 297px / Normal 225px / Hard 189px / Lunatic 158px ≧ 各帯の上限。
        if (!_relayFired && HpRatio <= _relayHp)
        {
            _relayFired = true;
            var diff = GetNodeOrNull<GameManager>("/root/Game")?.Difficulty ?? GameManager.Diff.Normal;
            var (hops, hopMin, hopMax) = diff switch
            {
                GameManager.Diff.Easy => (2, 140f, 190f),
                GameManager.Diff.Hard => (3, 130f, 165f),
                GameManager.Diff.Lunatic => (4, 110f, 145f),
                _ => (3, 140f, 190f),
            };
            _caster?.CastFullscreenChain(hops, hopMin, hopMax);
            _relayWatching = true;
            _relayStartLives = (GetTree().GetFirstNodeInGroup("player") as Player)?.Lives ?? -1;
        }
        // フィナーレ発火＝最後のバーの残り50%（finaleRatio = 0.5 / バー本数）。
        if (!_finale && HpRatio <= 0.5f / Mathf.Max(1, TotalBars))
        {
            _finale = true;
            GetHud()?.SetBossBarTint(Spells[2].tint); // フィナーレ色（#26）
            GetHud()?.AnnounceSpell("レイ", "@rei_compete", Spells[2].name + "＋" + Spells[3].name, Spells[2].tint);
        }
    }

    // RECLOSE のキャラ別弱気セリフ（序盤=虚勢→終盤=弱気。サイクルごとに index を進め、超えたら最後を使い回す）。
    private static readonly string[] RecloseLines =
    {
        "近づかないで。わたしの式は、まだ完璧よ。",
        "……二位のくせに。わたしに触れる気?",
        "や……やめて。崩さないで、わたしを……",
    };
    private int _recloseIdx;
    protected override void OnRecloseLine()
    {
        ShowRecloseLine("レイ", RecloseLines[Mathf.Min(_recloseIdx, RecloseLines.Length - 1)]);
        _recloseIdx++;
    }

    protected override void GrantFollower() { }

    protected override void OnCryStart()
    {
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        // 改心が始まる確実な瞬間に「解決音（完）」へ移す＝半音で落ちていたモチーフが主音に届く。
        Audio.Instance?.PlayRedeem(0);
        // 会話を出せない状況（Hud が取れない／台詞が無い）なら会話に入らず即着地させる
        //   ＝送るものが無いのに EndCryNow を待ち続けて Finished が立たない詰まりを断つ。
        if (hud == null || Lines.Length == 0) { EndCryNow(); return; }
        hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        // S3 画の反転：改心成立（cry→post）で、白飛びしていた「１位」に色が差し始める
        //（帰還の会話の背景でゆっくり進む。ズーム/フラッシュで指ししない＝気づく余白）。
        (GetTree().GetFirstNodeInGroup("imagery") as StageImagery)?.TriggerReversal();
        Finished = true;
    }

    // 保険タイムアウトで cry が強制終了されたとき、会話ドライバも畳む（_seq が残ると台詞が出続ける）。
    protected override void AbortCrySequence() => _seq = false;

    public override void _Process(double delta)
    {
        // 改心の会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        _lineT += delta;

        if (_seq)
        {
            if (zEdge && _lineT >= 0.25)
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
            // 戦闘中のレイはガワ（笑顔固定・泣き顔なし）。改心の決壊行だけ face 指定で中の人の泣き顔へ割れる。
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? RGawa : face, // レイも行ごと差し替え可（こはる方式）
            _ => "res://char/mina_face.png", // ミナ・中継
        };
        hud.ShowDialog(kind, text, portrait, otherName: "レイ");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
