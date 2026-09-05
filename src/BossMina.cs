using Godot;

// BossMina : FINAL「穢れたわたし」（案C・仮台本 08 F2/F3）。三人ぶんの穢れがミナの中で限界に達した姿。
// 自機は強化なしの「素の光」＝あなたが操作して、ミナ自身が抱えた穢れを撃ち祓う。
// BREAK ごとに、祓った三人（あかり→こはる→レイ）が浄化波の援護とともに返礼を投げる。
// HPを削り切る＝穢れを祓い、核が開く。短い邂逅（F3）のあと、Final（F4 の頂点）へ。
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

    // F3 邂逅（HP0。仮台本 wiki/08_仮台本/08。ユーザー承認済み・2026-09-05）。who: 1=ミナ / 2=レイ。
    //   核が開く一拍。本決着（F4 の頂点）は Final に委ねるので、ここは4行だけ。
    //   出自に触れる行は置かない（03 の「あなたが捨てた言葉で、できているのに」は不採用）。
    //   レイの1行は H1r 返信「あなたの言い方、誰かに似てる」の反転回収＝一面目のあかりの気づきを、
    //   三面あとにレイが言い切る。ガワではなく中の人の顔（v3 rei_face）で。
    //   ミナの受けは断定しない＝測れなかったことだけを言って白転へ渡す。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (1, "……こないで、ください。……ご主人、様……穢れて、しまいます……", "res://char/mina_worried.png"), // 動揺・拒絶＝worried（表情マトリクス指定行）
        (2, "ねえ、知ってた? あんたの言い方——この人に、そっくりよ。", "res://char/v3/rei_face.png"),
        (1, "…………。", "res://char/mina_worried.png"),
        (1, "……いまの、は。……観測、できません。", "res://char/mina_tears.png"), // 断定しない。測れなかったことだけ → 白転 → F4
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

    // 弾サイズ階層（#攻撃種ごとのサイズ差）：密集バラマキ(Ring)=小／連続糸(Spiral)=極小／
    //   自機狙いの精密弾(Aimed)=大／花弁(Flower)=遅い外周を大きく・速い内周を極小で「開花」を強調。
    //   当たり芯ドットは全形状共通描画＝大きくしても被弾点は埋もれない。
    private void Ring(BulletPool pool, int k, float spd)
    {
        _ringOff += Mathf.DegToRad(8f);
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
        for (int i = -_aimedWing; i <= _aimedWing; i++)
        {
            float a = baseA + i * Mathf.DegToRad(11f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _aimedSpeed, 4.0f);
        }
    }

    private void Flower(BulletPool pool, int petals)
    {
        _ringOff += Mathf.DegToRad(360f / petals / 2f);
        for (int i = 0; i < petals; i++)
        {
            float a = _ringOff + Mathf.Tau * i / petals;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 64f, 4.0f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 100f, 2.6f);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(15f);
        for (int s = 0; s < 3; s++)
        {
            float a = _ringOff + Mathf.Tau * s / 3f;
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

    // BREAK 合図（仮台本 wiki/08_仮台本/08 F2。ユーザー承認済み・2026-09-05）。
    //   ミナ本人が敵なので「ミナが煽る」共通実装は使わない。案C では少年が居ないので、
    //   祓った三人がミナへ返礼を投げる＝浄化波の援護になる。順は面の順（あかり→こはる→レイ）で、
    //   BREAK の回数ではなく **その時点の HP** で誰の番かを決める（BREAK はパネル全壊が条件で
    //   回数が可変なため。閾値は背景巡回 MinaRoot.Journey と同じ 0.80/0.58/0.36）。
    //   一人ぶん一度きり＝同じ人が二度返さない。四度目以降の BREAK は字幕を出さない（言い尽くした）。
    //   字幕は1行しか折り返さないので、返礼は2拍に割って順に出す（BREAK 窓 4.45s の内側）。
    private static readonly (float hp, string who, Color col, string a, string b)[] BreakThanks =
    {
        // あかり＝S1-11「あったかい声が、した。……知らない声なのに」の返礼。取り消さない側へ反転
        (0.80f, "あかり", UiKit.Purify,
            "あったかい声、って言ったの、あたし。",
            "——既読、つけに来た。あなたのぶん。……今度は、取り消さない。"),
        // こはる＝H2r「ありがと、知らない人。」の返礼。送れなかったコメントとペンライトを回収
        (0.58f, "こはる", UiKit.Purify,
            "ありがと、知らない人——って。……知らない人じゃ、なかったよ。",
            "送れなかったやつ、送ったもん。——ペンライト、振るね。"),
        // レイ＝H3r「は? 誰よあんた。」の返礼。決定打「見ていました」を見る側へ反転
        (0.36f, "レイ", UiKit.Purify,
            "誰よあんた、って言ったわね。——訂正する。",
            "……見てたの、あんたでしょ。今度は、わたしが見てる番なんだから。"),
    };
    private int _thanksIdx;                 // 次に返す人（0=あかり 1=こはる 2=レイ）。一人一度きり
    private double _thanksT;                // 1拍目からの経過（2拍目の差し替え待ち）
    private int _thanksPending = -1;        // 2拍目を出す相手（-1＝待ちなし）
    private const double ThanksBeat = 2.15; // 1拍目→2拍目の間（BREAK 窓 4.45s に2拍が収まる尺）

    protected override void OnBreakCue()
    {
        if (_thanksIdx >= BreakThanks.Length) return;   // 三人とも返し終えた＝以降は無言
        // HP が次の人の番に達していなければ、まだその人は出さない（背景巡回と歩を揃える）。
        if (HpRatio > BreakThanks[_thanksIdx].hp) return;
        // 高火力で閾値を飛び越えたときは、いま出ている背景の人まで送る＝背景と喋る人がずれない
        //   （飛ばされた人の返礼は聞けない。三人ぶんを必ず流したいわけではなく、画と声を合わせる方を採る）。
        while (_thanksIdx + 1 < BreakThanks.Length && HpRatio <= BreakThanks[_thanksIdx + 1].hp)
            _thanksIdx++;
        _thanksPending = _thanksIdx;
        _thanksT = 0;
        _thanksIdx++;
        var t = BreakThanks[_thanksPending];
        GetHud()?.ShowBossLine(t.who, t.a, t.col, ThanksBeat);
    }

    // 返礼の2拍目を、1拍目の尺が切れるところで差し替える（会話送りは無い＝戦闘は止めない）。
    private void TickThanks(double delta)
    {
        if (_thanksPending < 0) return;
        _thanksT += delta;
        if (_thanksT < ThanksBeat) return;
        var t = BreakThanks[_thanksPending];
        _thanksPending = -1;
        GetHud()?.ShowBossLine(t.who, t.b, t.col, ThanksBeat + 0.3);
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
        TickThanks(delta);   // BREAK 返礼の2拍目（戦闘は止めない字幕の差し替え）

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
        // F3 に出るのは ミナ(1) と レイ(2)。who=2 の話者名は otherName で決まるので「レイ」を渡す。
        string portrait = string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face; // 行ごと差し替え可（他ステージと同方式）
        hud.ShowDialog(kind, text, portrait, otherName: "レイ");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
