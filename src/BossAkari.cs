using Godot;

// BossAkari : STAGE2「あかり（雨の教室）」のボス＝穢れの核「ゆるせないわたし」。
// 自責の言葉（黒い吹き出し＝パネル）を旋回させ、下向きの自責弾を撒く。
// パネルを剥がしてHPを削り切る＝奥の“本当のあかり”の光に届く＝浄化（改心）。
// 浄化後は改心の姿を見せながら、少年（正体を隠したまま）が普遍化した言葉を贈る。フォロワーにはしない。
public partial class BossAkari : Enemy
{
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();
    private const float RoamSpeed = 40f;

    private double _fireT;
    private double _fireT2;   // フィナーレ用の第2タイマー（2スペル同時撃ち）
    private bool _finale;     // HP2割以下＝2スペル同時展開
    private float _ringOff;
    private int _pattern;       // 現在の攻撃パターン（セリフを挟むたびに変化）
    private int _beatsFired;    // 流した独白の数
    private const int PatternCount = 4;

    // HPがこの割合を割るたびに攻撃パターンを変える（独白は浄化のかけあいに集約）。
    private static readonly float[] PatternThresholds = { 0.78f, 0.52f, 0.26f };

    // 予測攻撃キャスター（フィールド化：通路中は宣告ごと止めるため）。
    private AreaSpellCaster _caster = null!;
    // ── イライラ棒「雨の帰り道」（HP52%ワンショット）──
    //   宣告→ボスが画面右外(x≈434)へ退場・不可侵化・弾幕停止→1.5s非致死プレビュー→通路12s→
    //   出口到達でパネル全砕き(Purify)→BREAK窓＝完走のご褒美→ボス帰還。
    //   退場位置は自機弾の消滅境界(x>400)より外＝パネル軌道(26+3px)込みで物理的に届かない。
    private bool _corridorFired;   // 発火ワンショット
    private int _corridorPhase;    // 0=なし / 1=退場〜通路中 / 2=帰還中
    private CorridorRun? _corridor;
    private const float AwayX = 434f;   // 退場先X（弾消滅境界400 ＋ パネル軌道29 ＋ 余白）
    private const float DashSpeed = 320f; // 退場/帰還の移動速度（通路の尺を演出で食わない）

    // ── INI 外出しのバランス値（config/boss_stats.ini [akari]。読めなければ現行既定値）──
    private double _fanInterval = 1.0, _ringInterval = 1.2, _aimedInterval = 0.7, _spiralInterval = 0.085;
    private int _fanCount = 9, _ringCount = 16, _aimedWing = 1; // wing=way数の片翼（3way→1）
    private float _ringSpeed = 72f, _aimedSpeed = 96f, _spiralSpeed = 90f, _roamSpeed = RoamSpeed;
    private float _corridorHp = 0.52f;

    // スペルカード（RefrainHTML Danmaku v3 STAGE2 あかり＝雨の教室・青と白の寒色）。
    private static readonly (string name, BulletShape shape, Color tint)[] Spells =
    {
        ("ねえ、こっち見て", BulletShape.Needle,  new Color("6c9cd8")), // 雨青・降雨の針
        ("すきって言って",   BulletShape.Diamond, new Color("4a6aa0")), // 藍・大きく遅い菱形
        ("ずっと一緒",       BulletShape.Orb,     new Color("a8c8e8")), // 淡青・狙い撃ち（包囲）
        ("離さない",         BulletShape.Needle,  new Color("e8f0ff")), // 白・追尾で逃がさない
    };
    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        SetSpellVisual(s.shape, s.tint);
        GetHud()?.SetBossBarTint(s.tint); // HPバーもスペル色へ（#26 フェーズ移行の可視化）
        GetHud()?.AnnounceSpell("あかり", "@akari_ame", s.name, s.tint);
    }

    // 浄化時のかけあい（who: 0=少年 / 1=ミナ / 2=あかり / 3=地・記憶＝ミナ語り＋記憶フラッシュ）。Zで手動送り。
    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;
    // 浄化のかけあい（設計書 v2 [P-02b] のボス節を順序通りに）。少年は自分の声では言えず、ミナが“中継”して届ける。
    // ※「あなたのせいじゃない」は最も効かない言葉＝禁止。庇った側の意志を立てる言葉で解く。
    private const string SGentle = "res://char/shonen_gentle.png";
    // 躁的暴走＝愛の洪水（love-bombing）。傷＝本命に言えなかった「好き」が宛先を失い溢れる。
    // 決定打＝本作の主モチーフ「名前を呼ぶ」（lines§2）。本人が乞うた一語で抜く。
    // 伏線③「ぼくの声じゃだめ」は維持＝名前はミナの声で届ける（少年は正体を隠す代償）。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "ねえ、こっち見て。ねえってば。あたしのこと、見て。", ""),
        (2, "好き? 好きって言って。あたしも好き。だいすき。ずっと一緒にいようね。離さないから。", ""),
        (2, "……ねえ。どうして、名前で、呼んでくれないの。", ""),
        (2, "いつから、あたしは……“キミ”に、なったの……?", ""),
        (1, "“キミ”……? ご主人様、これは——", ""),
        (0, "————行こう。奥だ。", SGentle),
        (2, "来ないで……っ。あたしの“好き”は、迷惑なだけ。だから、世界中に、ばら撒くしか——", ""),
        (1, "……見えました。雨の交差点。言いかけた唇。「あのね、あたし——」。そして、クラクション。", ""), // MemFlashLine：記憶フラッシュを焚く
        (0, "ミナ。……一度だけ。ぼくの代わりに、あの子の名前を、呼んでやってくれ。", SGentle),
        (1, "ご主人様の、お声では。いけないんですか。", ""),
        (0, "————ぼくの声じゃ、だめなんだ。気づかれて、しまうから。", SGentle),
        (5, "——あかり。", ""),                                                   // 決定打＝名前（一行手前で無音→ここで主題）
        (2, "……いま、名前。あたしの、名前……。あったかい……なんで、こんなに……", ""),
        (5, "きみの“好き”は、迷惑なんかじゃない。——届けたかった一人には、とっくに、届いてるよ。", ""),
        (2, "……ぁ……", ""),                                                     // 言わせない
    };

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [akari]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("akari", "points", 1500);
        BodyRadius = BossTuning.F("akari", "body_radius", 9f);
        PanelCount = BossTuning.I("akari", "panel_count", 5); // 自責の言葉（黒い吹き出し）
        PanelInk = BossTuning.I("akari", "panel_ink", 2);
        OrbitRadius = BossTuning.F("akari", "orbit_radius", 26f);
        SpinSpeed = BossTuning.F("akari", "spin_speed", 0.9f);
        PanelsFire = false;      // 攻撃は本体の自責弾
        EnemyBulletSpeed = BossTuning.F("akari", "bullet_speed", 80f);

        // HPバー本数は難易度別（通常ボス：Easy2/Normal4/Hard5/Lunatic6）。INI hp_bars > 0 で固定上書き。
        int bars = BossTuning.I("akari", "hp_bars", 0);
        BarCount = bars > 0 ? bars : DiffBars(finalBoss: false);

        // 弾幕・ギミックの外出し値（INIに無ければフィールド初期値＝現行値のまま）。
        _fanInterval = BossTuning.F("akari", "fan_interval", 1.0f);
        _fanCount = BossTuning.I("akari", "fan_count", 9);
        _ringInterval = BossTuning.F("akari", "ring_interval", 1.2f);
        _ringCount = BossTuning.I("akari", "ring_count", 16);
        _ringSpeed = BossTuning.F("akari", "ring_speed", 72f);
        _aimedInterval = BossTuning.F("akari", "aimed_interval", 0.7f);
        _aimedSpeed = BossTuning.F("akari", "aimed_speed", 96f);
        _aimedWing = Mathf.Max(0, BossTuning.I("akari", "aimed_ways", 3) / 2); // 奇数way→片翼数
        _spiralInterval = BossTuning.F("akari", "spiral_interval", 0.085f);
        _spiralSpeed = BossTuning.F("akari", "spiral_speed", 90f);
        _roamSpeed = BossTuning.F("akari", "roam_speed", RoamSpeed);
        _corridorHp = BossTuning.F("akari", "corridor_hp", 0.52f);

        PreTexPath = "res://char/enemy_akari_pre.png";
        // 改心の三段：穢れ(pre)→泣き(cry＝触手がほどけ涙があふれる中間)→笑顔(post)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post（笑顔）へ着地する。
        CryTexPath = "res://char/enemy_akari_cry.png";
        PostTexPath = "res://char/enemy_akari_post.png";
        // パネルは専用素材なし → Panel のプレースホルダ（黒い「・・・」吹き出し）を使う
        BodyDisplayH = 52f;
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    public override void _Ready()
    {
        base._Ready();
        // ボス登場＝道中BGMからあかり固有テーマへクロスフェード（フレーズが途中で切れる＝未完）。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossAkari);
        // 徘徊：画面上部のボスゾーンに収め、イージング＋ホバーで漂わせる（速度はINI: roam_speed）。
        _mover.Configure(new Vector2(200f, 70f), 90f, 28f, _roamSpeed);
        GetHud()?.ShowBossBar("あふれるわたし", "@akari.");
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        ApplySpell();

        _caster = new AreaSpellCaster();
        _caster.Configure("akari", GetParent());
        AddChild(_caster);
    }

    protected override void UpdateMovement(double delta)
    {
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, _mover.FacingLeft);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Akari, GlobalPosition, (float)delta, 32f);
        if (_corridorPhase != 0) { TickCorridor(); return; } // 通路中は撃たない（避けに集中させる）
        FirePattern(delta);
    }

    // 「雨の帰り道」の進行。UpdateMovement 経由＝会話中は通路(CorridorRun)側と一緒に凍る。
    private void TickCorridor()
    {
        if (_corridorPhase == 1)
        {
            // 通路の完走（or ボス浄化で解散）を待って帰還へ。
            if (_corridor == null || !IsInstanceValid(_corridor) || _corridor.Finished)
            {
                _corridorPhase = 2;
                _mover.Configure(new Vector2(200f, 70f), 90f, 28f, DashSpeed); // 高速で戦線に戻る
            }
        }
        else if (_corridorPhase == 2 && GlobalPosition.X <= 330f)
        {
            // 帰還完了：徘徊を通常速度へ戻し、宣告を再開。
            _corridorPhase = 0;
            _mover.Configure(new Vector2(200f, 70f), 90f, 28f, _roamSpeed);
            _caster.SetProcess(true);
            SetPanelsInvulnerable(false);
            // 出口報酬：パネル全砕き→BREAK窓誘発（SHIELDED中の Purify＝ボム時 Enemy.Purify と同じ経路）。
            if (!IsPurified) Purify();
        }
    }

    // HP52%ワンショット：宣告→退場→通路生成。以降の進行は TickCorridor。
    private void StartCorridor()
    {
        _corridorPhase = 1;
        GetHud()?.AnnounceSpell("あかり", "@akari_ame", "雨の帰り道", Spells[0].tint);
        GetHud()?.ShowBossLine("あかり", "来ないで……っ", UiKit.Kegare, 2.0);
        _mover.Configure(new Vector2(AwayX, 70f), 4f, 6f, DashSpeed); // 画面右外へ退場
        SetPanelsInvulnerable(true);   // 退場中の剥がし事故＝BREAK空撃ちを防ぐ
        _caster.SetProcess(false);     // 通常テレグラフの宣告も止める（通路に集中させる）
        _corridor = new CorridorRun { Boss = this };
        GetParent().AddChild(_corridor);
        _corridor.GlobalPosition = Vector2.Zero; // 画面座標基準で描く（Fullscreen AOE と同作法）
    }

    // 攻撃パターン（セリフを挟むたびに _pattern が変わる）。
    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        if (_finale) { FireFinale(pool, delta); return; }
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(_fanInterval)) { _fireT = 0; FanDown(pool); } break;       // 下向きの雨の扇
            case 1: if (_fireT >= Di(_ringInterval)) { _fireT = 0; Ring(pool); } break;         // 回転する放射リング
            case 2: if (_fireT >= Di(_aimedInterval)) { _fireT = 0; AimedSpread(pool); } break; // 自機狙いの3way連射
            default: if (_fireT >= Di(_spiralInterval)) { _fireT = 0; Spiral(pool); } break;    // 二重スパイラル
        }
    }

    // フィナーレ（HP2割以下）：「雨」(雨青の針)＋「机が落ちる」(藍の菱形螺旋)を同時展開。
    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.9)) { _fireT = 0; SetSpellVisual(Spells[0].shape, Spells[0].tint); FanDown(pool); }
        if (_fireT2 >= Di(0.085)) { _fireT2 = 0; SetSpellVisual(Spells[1].shape, Spells[1].tint); Spiral(pool); }
    }

    private void FanDown(BulletPool pool)
    {
        int k = Dn(_fanCount);
        for (int i = 0; i < k; i++)
        {
            float t = (float)i / (k - 1) - 0.5f;
            float a = Mathf.Pi / 2f + t * Mathf.DegToRad(80f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * EnemyBulletSpeed, 3.4f);
        }
    }

    private void Ring(BulletPool pool)
    {
        int k = Dn(_ringCount);
        _ringOff += Mathf.DegToRad(11f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / k;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _ringSpeed, 3.4f);
        }
    }

    // 「キミ弾」ギミック（#12 機構側）：自機を追う2スペル（「ずっと一緒」「離さない」）の弾だけ、
    // グレイズ（かすり）すると減速×Bullet.GrazeSoftenMul＋淡色化する＝名前を知らない“キミ”呼びの距離が、触れると和らぐ。
    // 被弾判定は不変（安全化しすぎ防止）。フィナーレでは無効＝最後の圧は緩めない。
    private void AimedSpread(BulletPool pool)
    {
        float baseA = Mathf.Atan2(AimAtPlayer().Y, AimAtPlayer().X);
        for (int i = -_aimedWing; i <= _aimedWing; i++)
        {
            float a = baseA + i * Mathf.DegToRad(14f);
            var b = FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _aimedSpeed, 3.4f);
            b.SoftenOnGraze = true; // AimedSpread は「ずっと一緒」(pattern2)専用＝スペル限定が自然に成立
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(13f);
        for (int s = 0; s < 2; s++)
        {
            float a = _ringOff + Mathf.Pi * s;
            var b = FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _spiralSpeed, 3.2f);
            if (_pattern == 3 && !_finale) b.SoftenOnGraze = true; // 「離さない」中のみ（フィナーレ流用時は付けない）
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
        // HPが閾値を割るたびに攻撃パターンを変える。
        if (_beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
            ApplySpell();
        }
        // イライラ棒「雨の帰り道」：HP52%（INI: corridor_hp）を割った瞬間に一度だけ（パターン第2切替と同じ節目＝中盤の山）。
        // 上の ApplySpell と同フレームで重なり得るが、宣告は後勝ち＝「雨の帰り道」が表示される。
        if (!_corridorFired && HpRatio <= _corridorHp)
        {
            _corridorFired = true;
            StartCorridor();
        }
        // フィナーレ発火＝最後のバーの残り50%（finaleRatio = 0.5 / バー本数）。
        if (!_finale && HpRatio <= 0.5f / Mathf.Max(1, TotalBars))
        {
            _finale = true;
            GetHud()?.SetBossBarTint(Spells[0].tint); // フィナーレ色（#26）
            GetHud()?.AnnounceSpell("あかり", "@akari_ame", Spells[0].name + "＋" + Spells[1].name, Spells[0].tint);
        }
    }

    // RECLOSE のキャラ別弱気セリフ（序盤=虚勢→終盤=弱気）。
    private static readonly string[] RecloseLines =
    {
        "やだ、まだ見て。離さないってば。",
        "お願い、嫌わないで……ちゃんと、いい子にするから……",
        "ひとりにしないで……お願い、まだ……",
    };
    private int _recloseIdx;
    protected override void OnRecloseLine()
    {
        ShowRecloseLine("あかり", RecloseLines[Mathf.Min(_recloseIdx, RecloseLines.Length - 1)]);
        _recloseIdx++;
    }

    protected override void GrantFollower() { } // 新canonにフォロワーは無い

    protected override void OnCryStart()
    {
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        // 改心が始まる確実な瞬間に「解決音（完）」へ移す＝途切れていたフレーズが最後まで歌われる。
        Audio.Instance?.PlayRedeem(1);
        if (hud != null) hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        // S3 画の反転：改心成立（cry→post）で、自責の言葉が「ありがとう」へ溶け始める
        //（帰還の会話の背景でゆっくりクロスフェード。指ししない＝気づく余白）。
        (GetTree().GetFirstNodeInGroup("imagery") as StageImagery)?.TriggerReversal();
        Finished = true;
    }

    // 戦闘中の独白・浄化のかけあいを Z で手動送り。
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
                    EndCryNow(); // 改心の笑顔へ着地し退場
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
        // 記憶フラッシュ：ナレ枠は廃したので、回想の核となる行（雨の交差点）の内容でトリガする。
        // 語り手はミナ(who=1)に統一しても、この一行で記憶フラッシュ演出は焚かれる。
        if (text.Contains("雨の交差点"))
            (GetTree().GetFirstNodeInGroup("imagery") as StageImagery)?.TriggerMemoryFlash();
        string portrait = kind switch
        {
            Hud.LineKind.Boy => face,                       // 少年（行ごとの表情）
            Hud.LineKind.Other => "res://char/akari_face.png", // あかり
            _ => "res://char/mina_face.png",                // ミナ・中継
        };
        hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
