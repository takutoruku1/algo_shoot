using Godot;

// BossMina : FINAL「暴走したミナ」。三人ぶんの穢れがミナの中で限界に達し暴走した姿。
// 役割反転——いつも見ているだけだった少年が、初めて自分で“光”を握り、ミナの穢れを撃ち祓う。
// HPを削り切る＝穢れを祓い、奥の“本当のミナ”に光が届く。短い邂逅のあと、Final（対話で帰還）へ。
public partial class BossMina : Enemy
{
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();
    private const float RoamSpeed = 38f;

    private double _fireT;
    private double _fireT2;   // フィナーレ用の第2タイマー（2スペル同時撃ち）
    private bool _finale;     // HP2割以下＝2スペル同時展開
    private float _ringOff;
    private int _pattern;
    private int _beatsFired;
    private const int PatternCount = 5;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    // 全画面AOEキャスター（このボス専用）。HP閾値で安置型/全面型を1回ずつ撃たせ、予告中は通常弾を止める。
    private AreaSpellCaster _caster = null!;
    private bool _aoe62Done, _aoe42Done, _aoeFinaleDone; // 各閾値ワンショット

    // ── INI 外出しのバランス値（config/boss_stats.ini [mina]。読めなければ現行既定値）──
    private double _ringInterval = 0.95, _aimedInterval = 0.8, _flowerInterval = 1.0, _spiralInterval = 0.075;
    private int _ringCount = 16, _flowerPetals = 10, _aimedWing = 2; // wing=way数の片翼（5way→2）
    private float _ringSpeed = 72f, _aimedSpeed = 104f, _spiralSpeed = 92f;
    private float _aoeSingleHp = 0.62f, _aoeChainHp = 0.42f; // 全画面AOEの発動HP割合（単発/リレー2連）

    // HPがこの割合を割るたびに弾幕パターンを変える。
    private static readonly float[] PatternThresholds = { 0.82f, 0.62f, 0.42f, 0.22f };

    // スペルカード（RefrainHTML Danmaku v3 FINAL ミナ＝全ステージの弾形・色を濁らせて融合）。
    private static readonly (string name, BulletShape shape, Color tint)[] Spells =
    {
        ("レイの渦＋あかりの雨", BulletShape.Diamond, new Color("b07cd0")), // 濁紫
        ("こはるの怒り＋レイの星", BulletShape.Star,  new Color("e0648c")), // 濁桃
        ("あかりの落下＋こはるの扇", BulletShape.Rice, new Color("9a8cd0")), // 濁藍
        ("心象の核",             BulletShape.Ring,    new Color("f0d98a")), // 濁金
        ("世界中の悲鳴",         BulletShape.Orb,     new Color("e0729c")), // 濁桃・全部同時
    };
    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        SetSpellVisual(s.shape, s.tint);
        GetHud()?.SetBossBarTint(s.tint); // HPバーもスペル色へ（#26 フェーズ移行の可視化）
        GetHud()?.AnnounceSpell("ミナ", "@mina_ai_", s.name, s.tint);
    }

    // 邂逅のかけあい（who: 0=少年 / 1=ミナ）。本決着は Final（対話）に委ねるので、ここは短い一拍。
    // 正典 v3（S2 点検済み）：この声は archive replay。ミナの叫びに“正確には応えない”＝自分の告白を
    // 自分のペースで続ける（3〜4行目）のが replay の既定応答感。末尾の同語反復（StageMina Intro と同じ
    // 「今度は、ぼくが行く番だ」）は録音の反復＝兆候として意図的に残す。明言はしない（種明かしは Epilogue）。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (0, "ミナ! ……ぼくだ。迎えに来た。", "res://char/shonen_gentle.png"),
        (1, "……ご主人、様? だめ……来ないで……わたくしに、近づいては……穢れて、しまいます……", "res://char/mina_worried.png"), // 動揺・拒絶＝worried（表情マトリクス指定行）
        (0, "ぼくはずっと、自分が行くのが怖かった。お前に光を握らせて、後ろにいた。", "res://char/shonen_afraid.png"), // 臆病の告白＝afraid（優しさ顔での代用をやめる。表情と物語の一致）。ミナの「来ないで」には答えない＝自分の告白を続ける（既定応答感）
        (0, "……でも、お前を一人にする方が、もっと怖い。——今度は、ぼくが行く番だ。", "res://char/shonen_proud.png"), // StageMina Intro:39 と同語＝録音の反復（兆候）
    };

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [mina]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("mina", "points", 3000);
        BodyRadius = BossTuning.F("mina", "body_radius", 10f);
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
        _aoeSingleHp = BossTuning.F("mina", "aoe_single_hp", 0.62f);
        _aoeChainHp = BossTuning.F("mina", "aoe_chain_hp", 0.42f);

        PreTexPath = "res://char/enemy_mina_pre.png";
        // 改心の三段：穢れ(pre)→泣き(cry＝穢れ半剥がれ・決壊の涙)→清浄(post)。
        // cry は邂逅の会話尺いっぱい保持し、EndCryNow で post（本来の姿）へ着地（他ボスと同作法）。
        CryTexPath = "res://char/enemy_mina_cry.png";
        PostTexPath = "res://char/enemy_mina_post.png";
        BodyDisplayH = 60f;
        CryHoldDur = 9999.0;
    }

    public override void _Ready()
    {
        base._Ready();
        // ボス登場＝道中BGMからボスBGMへクロスフェード。ミナ戦本体は専用の実音源 BgmBossMina
        //   （Final/ヒカゲの汎用 BgmBoss は据え置き）。実音源は MusicTargetDb で粒を揃えて鳴る。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossMina);
        // 徘徊：画面上部のボスゾーンに収め、イージング＋ホバーで漂わせる（速度はINI: roam_speed）。
        _mover.Configure(new Vector2(200f, 68f), 90f, 28f, BossTuning.F("mina", "roam_speed", RoamSpeed));
        GetHud()?.ShowBossBar("穢れたわたし", "@mina_ai_");
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        ApplySpell();

        _caster = new AreaSpellCaster();
        _caster.Configure("mina", GetParent());
        AddChild(_caster);
    }

    protected override void UpdateMovement(double delta)
    {
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        // 全画面AOE予告中は詠唱モーション：小刻みに身震いさせ（visualOffset を揺らす）、オーラを強める。
        bool casting = _caster != null && _caster.AoeActive;
        Vector2 vis = _mover.VisualOffset;
        if (casting)
        {
            float q = Mathf.Sin((float)Time.GetTicksMsec() * 0.03f) * 1.6f;
            vis += new Vector2(q, -Mathf.Abs(q) * 0.5f);
        }
        ApplyBossMotion(vis, _mover.Lean, _mover.FacingLeft);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Mina, GlobalPosition, (float)delta, casting ? 64f : 36f);
        FirePattern(delta);
    }

    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        // 全画面AOEの予告〜着弾中は通常弾を止める（避け先＝安置へ集中させる／弾の過密回避）。
        if (_caster != null && _caster.AoeActive) return;
        if (_finale) { FireFinale(pool, delta); return; }
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(_ringInterval)) { _fireT = 0; Ring(pool, Dn(_ringCount), _ringSpeed); } break;
            case 1: if (_fireT >= Di(_aimedInterval)) { _fireT = 0; Aimed(pool); } break;
            case 2: if (_fireT >= Di(_flowerInterval)) { _fireT = 0; Flower(pool, Dn(_flowerPetals)); } break;
            case 3: if (_fireT >= Di(_spiralInterval)) { _fireT = 0; Spiral(pool); } break;
            default: if (_fireT >= Di(1.1)) { _fireT = 0; Ring(pool, Dn(22), 66f); Ring(pool, Dn(22), 92f); } break;
        }
    }

    // フィナーレ（HP2割以下）：「心象の核」(濁金リング)＋「世界中の悲鳴」(濁桃の星螺旋)を同時展開＝全部同時。
    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.95)) { _fireT = 0; SetSpellVisual(Spells[4].shape, Spells[4].tint); Ring(pool, Dn(18), 70f); }
        if (_fireT2 >= Di(0.08)) { _fireT2 = 0; SetSpellVisual(Spells[1].shape, Spells[1].tint); Spiral(pool); }
    }

    private void Ring(BulletPool pool, int k, float spd)
    {
        _ringOff += Mathf.DegToRad(8f);
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
        for (int i = -_aimedWing; i <= _aimedWing; i++)
        {
            float a = baseA + i * Mathf.DegToRad(11f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _aimedSpeed, 3.4f);
        }
    }

    private void Flower(BulletPool pool, int petals)
    {
        _ringOff += Mathf.DegToRad(360f / petals / 2f);
        for (int i = 0; i < petals; i++)
        {
            float a = _ringOff + Mathf.Tau * i / petals;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 64f, 3.6f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 100f, 3.2f);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(15f);
        for (int s = 0; s < 3; s++)
        {
            float a = _ringOff + Mathf.Tau * s / 3f;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _spiralSpeed, 3.2f);
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
        // 全画面AOE（ラスボス専用）：HP 0.62（INI: aoe_single_hp）で安置型の単発（学習）→
        // 0.42（INI: aoe_chain_hp）で安置リレー2連（強化）。フィナーレ突入時に狭安置の「絶域」を1回（計3回）。
        // リレーの距離帯 140-190px は到達限界（(1.6s×WarnMul−0.3s)×150px/s＋r30。Normal 225px）内。
        if (!_aoe62Done && HpRatio <= _aoeSingleHp) { _aoe62Done = true; _caster?.CastFullscreen(wide: true); }
        if (!_aoe42Done && HpRatio <= _aoeChainHp) { _aoe42Done = true; _caster?.CastFullscreenChain(2, 140f, 190f); }

        // フィナーレ発火＝最後のバーの残り50%（finaleRatio = 0.5 / バー本数）。
        if (!_finale && HpRatio <= 0.5f / Mathf.Max(1, TotalBars))
        {
            _finale = true;
            GetHud()?.SetBossBarTint(Spells[4].tint); // フィナーレ色（#26）
            GetHud()?.AnnounceSpell("ミナ", "@mina_ai_", Spells[3].name + "＋" + Spells[4].name, Spells[4].tint);
            // フィナーレの「絶域」：安置は狭い（Easy30/Normal24/Hard20/Luna17px）が必ず在り、
            // そのぶん予兆を 1.45 倍に伸ばす（Normal 2.32s）＝狭い的でも走って入れる。
            // 旧実装は安置なし＝ボム以外に回避手段が無く、ボム0個なら確定被弾だった。
            if (!_aoeFinaleDone) { _aoeFinaleDone = true; _caster?.CastFullscreen(wide: false); }
        }
    }

    // BREAK 合図：ミナ本人が敵なので「ミナが煽る」共通実装は使わない。
    // ナレ全廃方針＝話者なしのシステム声は廃し、役割反転で前に出た少年（＝MINA再生）の号令にする。
    protected override void OnBreakCue()
    {
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?
            .ShowBossLine("少年", "ミナ! いまだ、撃ち抜け!", UiKit.Info, 0.45 + 4.0);
    }

    // RECLOSE のキャラ別弱気セリフ（高貴さの仮面の下で剥がれを拒む）。
    private static readonly string[] RecloseLines =
    {
        "いけません……これ以上、近づいては……",
        "わたくしに、触れないでくださいまし……穢れて、しまう……",
        "おやめください……あなたまで、汚したくない……",
    };
    private int _recloseIdx;
    protected override void OnRecloseLine()
    {
        ShowRecloseLine("ミナ", RecloseLines[Mathf.Min(_recloseIdx, RecloseLines.Length - 1)]);
        _recloseIdx++;
    }

    protected override void GrantFollower() { }

    protected override void OnCryStart()
    {
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
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
        }
    }

    private void ShowLine()
    {
        var (who, text, face) = Lines[_line];
        var hud = GetHud();
        if (hud == null) return;
        var kind = (Hud.LineKind)who;
        string portrait = kind == Hud.LineKind.Boy ? face
            : string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face; // ミナも行ごと差し替え可（他ステージと同方式）
        hud.ShowDialog(kind, text, portrait, otherName: "ミナ");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
