using Godot;

// BossAkari : STAGE1「あかり（雨の降りやまない退勤後のフロア）」のボス＝「あふれるわたし」@akari_ame。
// 自責の言葉（黒い吹き出し＝パネル）を旋回させ、下向きの自責弾を撒く。
// パネルを剥がしてHPを削り切る＝奥の“本当のあかり”の光に届く＝浄化（改心）。
// 浄化後は改心の姿を見せながら、ミナ自身が決定打を届ける（案C。S1-10）。フォロワーにはしない。
public partial class BossAkari : Enemy
{
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();
    private const float RoamSpeed = 40f;

    private double _fireT;
    private double _fireT2;   // フィナーレ用の第2タイマー（2スペル同時撃ち）
    private bool _finale;     // HP2割以下＝2スペル同時展開
    private bool _memoryPending;
    private bool _memoryPlayed;
    private static readonly float[] PostThresholds = { 0.80f, 0.60f, 0.40f, 0.20f, 0.01f };
    private int _postsBroken;
    private bool _postPending, _revealingDraft;
    private BossPost? _post;
    private BossRealmFx _realm = null!;
    private Vector2 _postReturnPosition;
    private bool _postMonitoring;
    private double _postReturnGrace;
    public bool PostSequenceActive => _postPending || _post != null || _revealingDraft;
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
    private const float AwayX = Field.Right + 50f;   // 退場先X（弾消滅境界+16 ＋ パネル軌道29 ＋ 余白）
    private const float DashSpeed = 320f; // 退場/帰還の移動速度（通路の尺を演出で食わない）

    // ── INI 外出しのバランス値（config/boss_stats.ini [akari]。読めなければ現行既定値）──
    private double _fanInterval = 1.0, _ringInterval = 1.2, _aimedInterval = 0.7, _spiralInterval = 0.085;
    private int _fanCount = 9, _ringCount = 16, _aimedWing = 1; // wing=way数の片翼（3way→1）
    private float _ringSpeed = 72f, _aimedSpeed = 96f, _spiralSpeed = 90f, _roamSpeed = RoamSpeed;
    private float _corridorHp = 0.52f;

    // スペルカード（STAGE1 あかり＝雨のフロア・青と白の寒色）。技名は仮台本 06 の S1-9。
    // 弾は「仕事の書類」の絵で飛ぶ（art の名前＝char/v3/bullets/<name>.png）。総務三十歳の、
    // 最後の出社日の夜に三秒で取り消した一通——机の上のものが、そのまま人へ向かって飛んでくる。
    // 弾形・色は絵の裏のグロー（と、絵が無い時の保険）として残す＝当たり判定・弾数・弾速は不変。
    //   rot: 絵の回転速度(deg/s)。追う弾ほど遅く回して「じっと向いている」感を出す。
    private static readonly (string name, BulletShape shape, Color tint, string art, float rot)[] Spells =
    {
        ("ねえ、こっち見て", BulletShape.Needle,  new Color("6c9cd8"), "akari_sticky",   70f), // 赤い付箋＝こっち見て
        ("すきって言って",   BulletShape.Diamond, new Color("4a6aa0"), "akari_envelope", 38f), // 封筒＝未送信の一通
        ("ずっと一緒",       BulletShape.Orb,     new Color("a8c8e8"), "akari_clip",     52f), // クリップ＝鎖のように
        ("離さない",         BulletShape.Needle,  new Color("e8f0ff"), "akari_stamp",    26f), // 承認印＝離さない
    };
    // 攻撃パターン→立ち位置の対応。スペルが変わるたび BossMover に「次に何をするか」を伝え、
    // 攻撃の合間に「向かう→着く→一拍」でその立ち位置へ移る。
    //   0 雨の扇＝Wall（端に寄って帯を張る）／1 リング＝Ring（中央に据わる）／
    //   2 自機狙い＝Aimed（自機の x を追って横に滑る）／3 スパイラル＝Wall。
    private static BossMover.Attack StanceOf(int pattern) => (pattern % PatternCount) switch
    {
        1 => BossMover.Attack.Ring,
        2 => BossMover.Attack.Aimed,
        _ => BossMover.Attack.Wall,
    };

    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        _mover.SetNextAttack(StanceOf(_pattern));
        SetSpellVisual(s.shape, s.tint, BulletArt.Get(s.art), s.rot);
        GetHud()?.SetBossBarTint(s.tint); // HPバーもスペル色へ（#26 フェーズ移行の可視化）
        GetHud()?.AnnounceSpell("あかり", BossHandles.AkariSpell, s.name, s.tint);
    }

    // 浄化時のかけあい（who: 1=ミナ / 2=あかり）。Zで手動送り。
    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;
    // S1-10 改心（仮台本 06。ユーザー承認済み・2026-09-05）。二段で抜く：
    //   (1) ミナが束の底の一通（取り消されていない本物）を読み上げて返す
    //   (2) ミナ自身の言葉で決定打（証人型）「十二通、読みました。——汚れた“好き”は、ひとつも、ありませんでした。」
    // 案C に少年は居ないので中継（who=5）も使わない＝決定打はミナが自分の声で言う。
    // 「取り消された十二通は、ぜんぶ、わたくしに当たりました。」の行で BGM を落とし、決定打を無音のまま置く。
    // 相手の返事は代弁しない＝最後は「……ぁ……」の涙のまま抜く（cry 保持）。
    private const string ACry = "res://char/v3/akari_face_cry.png";
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "……返事がほしかった。おめでとう、より先に。", ACry),
        (2, "だから、何度も送って……怖くなって、取り消した。", ACry),
        (1, "……いま、見えた下書き。あの一通だけは、消さずに残していたのですね。", ""),
        (1, "——「おめでとう ほんとだよ 元気でね」。……宛名は、ありません。", ""),   // (1) 本物の一通。A43
        (2, "……ほんとは、ちゃんと笑って、見送りたかった。", ACry),
        (1, "取り消された十二通は、ぜんぶ、わたくしに当たりました。", ""),           // ここで BGM 停止
        (1, "十二通、読みました。——汚れた“好き”は、ひとつも、ありませんでした。", ""),   // (2) 決定打。無音のまま
        (2, "……ぁ……", ACry),                                                      // 言わせない（涙のまま抜く＝cry 保持）
    };
    // 決定打の手前で音を落とす行（本文一致で拾う）。ここから BGM 無しで決定打を置く。
    private const string BgmStopLine = "取り消された十二通は、ぜんぶ、わたくしに当たりました。";

    // 改心の台詞の実体。他ジョブ潜行中はミナ前提の Lines の代わりに改心相当シーン
    //   （CharacterStory.Redemption＝キャラ×面の9通り）を流す（2026-09-15。_Ready で差し替える）。
    //   _storySilenceAt＝改心相当シーンの「ここでBGM停止」行（ト書き。-1=無し）。
    private bool _charStory;
    private (int who, string text, string face)[] _lines = Lines;
    private int _storySilenceAt = -1;
    // 他ジョブ潜行の回想（CharacterStory.Memory）を会話枠だけで送るドライバ。null＝ミナ潜行（＝フィルム）。
    private CharacterStoryTalk? _memoryTalk;

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [akari]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("akari", "points", 1500);
        BodyRadius = BossTuning.F("akari", "body_radius", 19f);
        BodyHalfH = BossTuning.F("akari", "body_half_h", 23f);   // 縦長カプセル（絵の形に沿わせる）
        PanelCount = BossTuning.I("akari", "panel_count", 5); // 自責の言葉（黒い吹き出し）
        PanelInk = BossTuning.I("akari", "panel_ink", 3); // 2→3（B-5: 中盤でシールド段が痩せない用）
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

        PreTexPath = "res://char/v3/boss_akari_body_idle_v3.png";
        AttackTexPath = "res://char/v3/boss_akari_body_attack_v3.png";
        // 改心の三段：穢れ(pre＝待機)→泣き(cry＝専用の泣き顔)→改心後(post)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post へ着地する。
        // 旧 *_body_hit.png は被弾リアクション用で笑顔のままだった＝撃破しても穢れのままに見えたので、
        // 描き下ろしの *_body_cry.png（720px・エフェクトなし）に差し替えた。倍率・アンカーは待機と同じ。
        // 第二形態（2026-09-07）＝待つのをやめて顔を上げ、取り消した一通が溢れている姿。
        // 発動は下の OnHpChanged の閾値ブロック（PatternThresholds[1]=0.52）。攻撃・被弾の絵は流用する。
        Form2TexPath = "res://char/v3/boss_akari_body_form2_v3.png";
        CryTexPath = "res://char/v3/boss_akari_body_cry_v2.png";
        PostTexPath = "res://char/v3/enemy_akari_post.png";
        // パネルは専用素材なし → Panel のプレースホルダ（黒い「・・・」吹き出し）を使う
        // 表示高は ini（body_display_h）。v3 の本体はエフェクト込みで焼いていないぶん、旧52だと小さく見える。
        BodyDisplayH = BossTuning.F("akari", "body_display_h", 72f);
        BodyOffsetName = "akari";
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    protected override (float Scale, Vector2 Offset) GetBodyFrame(Texture2D texture)
    {
        // Match the person, not the trailing coat, and keep the head's horizontal axis steady.
        var axis = texture.ResourcePath.GetFile() switch
        {
            "boss_akari_body_idle_v3.png" => new Vector2(315, 603),
            "boss_akari_body_attack_v3.png" => new Vector2(340, 594),
            "boss_akari_body_form2_v3.png" => new Vector2(303, 572),
            _ => Vector2.Zero,
        };
        if (axis == Vector2.Zero) return base.GetBodyFrame(texture);
        float scale = 718f / axis.Y;
        return (scale, new Vector2(69.5f, 358f) / scale - (axis - texture.GetSize() / 2f));
    }

    public override void _Ready()
    {
        base._Ready();
        // ミナ以外の潜行は、改心のかけあい（ミナ前提）だけを操作キャラ版（CharacterStory.Redemption）へ差し替える。
        //   戦闘中の回想（memory）と撃破後のアフターは、操作キャラに依らず**この面のボス＝あかり**のフィルムで固定
        //   （2026-09-23 ユーザー報告「こはるの話であかりの回想／アフターが入っている」：以前は他ジョブ潜行で
        //   CharacterStoryFilm＝操作キャラ×章のフィルムを流していたため、あかりで潜ると面を問わずあかりの話になっていた）。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        _charStory = CharacterStory.DiveActive(game);
        if (_charStory)
        {
            _lines = CharacterStory.Redemption(game!.SelectedJob, "akari");
            _storySilenceAt = CharacterStory.RedemptionSilenceAt(game.SelectedJob, "akari");
        }
        // ルナティック（2026-09-26）：投稿の割り込み（ボスが隠れて札を撃たせる読みの間＋下書きの一枚絵）は出さない
        //   ＝ボス戦を止めない。全部「割った後」にしておくと _postPending が立たず LimitBodyDamage も素通し
        //   （BossPostSequence のしきい値空と同じ止め方）。回想と改心会話は _Process／OnCryStart 側で畳む。
        if (GameManager.LunaticActive) _postsBroken = PostThresholds.Length;
        // ボス登場＝道中BGMからあかり固有テーマへクロスフェード（フレーズが途中で切れる＝未完）。
        Audio.Instance?.StartAkariMusic(0);
        _realm = new BossRealmFx { Name = "BossRealmFx", Story = BossPostStory.Get("akari") };
        GetParent().GetParent().AddChild(_realm);
        // 移動：スペルごとの立ち位置＋状態機械（待機→構え→攻撃→余韻）。数値は INI（[akari] の
        // cruise_speed / accel_time / stance_*）。あかりは「座ったまま滑る」＝重く（accel_time 大）、
        // 上下に揺れない（hover_amp 0）。
        _mover.Configure("akari", new Vector2(Field.BossCenterX, Field.BossZoneCenterY), Field.BossZoneHalfW, Field.BossZoneHalfH);
        GetHud()?.ShowBossBar("あふれるわたし", BossHandles.AkariBar, this);
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        // 【激情】メーター開始。初期値は道中（p4/s1_4/s1_5/s1_2）の選択から決まる（FuryMeter.cs）。
        GetNodeOrNull<GameManager>("/root/Game")?.BeginFury("akari");
        ApplySpell();

        _caster = new AreaSpellCaster();
        _caster.Configure("akari", GetParent());
        AddChild(_caster);

        // 部品の演出層（char/v3/fx/akari/*.png）を本体の子として1個ぶら下げる。当たり判定は持たない。
        // GetBodyFrame normalizes the new outfit to the existing 720px effect reference frame.
        AttachParts("akari", idleTexW: 393f, attackTexW: 645f);
    }

    protected override void UpdateMovement(double delta)
    {
        // 自機の位置を渡す＝自機狙いの追従（x と y の両方に寄る）と、反転の判定（40px 以上・0.6秒）に使う。
        if (GetTree().GetFirstNodeInGroup("player") is Node2D pl) _mover.SetPlayerPos(pl.GlobalPosition);
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, _mover.FacingLeft, _mover.SquashScale);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Akari, GlobalPosition, (float)delta, 32f);
        if (_corridorPhase != 0) { TickCorridor(); return; } // 通路中は撃たない（避けに集中させる）
        FirePattern(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_post != null || _revealingDraft) return;
        base._PhysicsProcess(delta);
        if (_postReturnGrace > 0 && !IsPurified && !Hud.BubblePaused)
        {
            _postReturnGrace -= delta;
            SetBodyContactEnabled(_postReturnGrace <= 0);
        }
    }

    protected override int LimitBodyDamage(int damage)
    {
        if (_post != null || _revealingDraft) return 0;
        return _postsBroken < PostThresholds.Length
            ? DamageToHpFloor(damage, PostThresholds[_postsBroken]) : damage;
    }

    public override void Purify()
    {
        if (_post != null) { _post.BombHit(); return; }
        if (_postPending || _revealingDraft) return;
        base.Purify();
    }

    private void StartPost()
    {
        _postPending = false;
        _postReturnPosition = GlobalPosition;
        _postMonitoring = Monitoring;
        // The boss remains the lock-on/homing anchor while its post is the shootable surface.
        GlobalPosition = new Vector2(Field.Right - 84, 104);
        Visible = false;
        Monitoring = false;
        SetBodyContactEnabled(false);
        SetPanelsInvulnerable(true);
        _caster.CancelPendingAttacks();
        _caster.SetProcess(false);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        foreach (Node hazard in GetTree().GetNodesInGroup("aoe"))
            if (hazard is AreaStrike) hazard.QueueFree();
        GetHud()?.HideSpellCard();
        _post = new BossPost { Story = BossPostStory.Get("akari"), Boss = this, Index = _postsBroken,
            Position = GlobalPosition, Broken = BreakPostRealm, Completed = CompletePost };
        GetParent().AddChild(_post);
    }

    public void BreakPostRealm(int index, Vector2 position)
    {
        _realm.BreakPost(index, position);
        Audio.Instance?.StartAkariMusic(index + 1, 0.2f);
        if (index == 4) GetHud()?.HideBossBar();
    }

    private void CompletePost()
    {
        _post = null;
        _postsBroken++;
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        if (_postsBroken == PostThresholds.Length)
        {
            _revealingDraft = true;
            BossDraftScene.Play(GetHud()!, GetParent(), BossPostStory.Get("akari"), () =>
            {
                _revealingDraft = false;
                RestoreAfterPost();
                DealDirectDamage(BarHp * TotalBars);
            });
            return;
        }
        RestoreAfterPost();
        ApplySpell();
    }

    private void RestoreAfterPost()
    {
        GlobalPosition = _postReturnPosition;
        Visible = true;
        Monitoring = _postMonitoring;
        SetPanelsInvulnerable(false);
        _postReturnGrace = 0.9;
        _fireT = _fireT2 = 0;
        _caster.SetProcess(true);
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
                // 高速で戦線に戻る。ゾーンと速度だけを差し替える（MoveZoneTo）＝ini の性格は保つ。
                _mover.MoveZoneTo(new Vector2(Field.BossCenterX, Field.BossZoneCenterY), Field.BossZoneHalfW, Field.BossZoneHalfH, DashSpeed);
            }
        }
        else if (_corridorPhase == 2 && GlobalPosition.X <= Field.Right - 54f)
        {
            // 帰還完了：徘徊を通常速度へ戻し、宣告を再開。
            _corridorPhase = 0;
            // 性格つきの Configure で入り直す＝退場で狭めた stance_edge_x / stance_track_w も
            // ini の値に戻る（MoveZoneTo は縮める方向にしか触らないため、ここで復元が要る）。
            _mover.Configure("akari", new Vector2(Field.BossCenterX, Field.BossZoneCenterY), Field.BossZoneHalfW, Field.BossZoneHalfH);
            _caster.SetProcess(true);
            SetPanelsInvulnerable(false);
            SetBodyContactEnabled(true);   // 戦線に戻って通常速度＝接触判定も戻す
            // 出口報酬：パネル全砕き→BREAK窓誘発（SHIELDED中の Purify＝ボム時 Enemy.Purify と同じ経路）。
            if (!IsPurified) Purify();
        }
    }

    // HP52%ワンショット：宣告→退場→通路生成。以降の進行は TickCorridor。
    private void StartCorridor()
    {
        _corridorPhase = 1;
        GetHud()?.AnnounceSpell("あかり", BossHandles.AkariSpell, "雨の帰り道", Spells[0].tint);
        GetHud()?.ShowBossLine("あかり", "来ないで……っ", UiKit.Kegare, 2.0);
        _mover.MoveZoneTo(new Vector2(AwayX, Field.BossZoneCenterY), 4f, 6f, DashSpeed); // 画面右外へ退場（性格は保つ）
        SetPanelsInvulnerable(true);   // 退場中の剥がし事故＝BREAK空撃ちを防ぐ
        SetBodyContactEnabled(false);  // 退場/帰還は DashSpeed=320px/s で場を横切る＝通路中の自機を轢かない
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
            case 0: if (_fireT >= Di(_fanInterval)) { _fireT = 0; _mover.DeclareAttack(BossMover.Attack.Wall); TriggerAttackPose(); FanDown(pool); } break;       // 雨の扇＝自機方向へ帯を張る
            case 1: if (_fireT >= Di(_ringInterval)) { _fireT = 0; _mover.DeclareAttack(BossMover.Attack.Ring); TriggerAttackPose(); Ring(pool); } break;         // 回転する放射リング
            case 2: if (_fireT >= Di(_aimedInterval)) { _fireT = 0; _mover.DeclareAttack(BossMover.Attack.Aimed); TriggerAttackPose(); AimedSpread(pool); } break; // 自機狙いの3way連射
            default: if (_fireT >= Di(_spiralInterval)) { _fireT = 0; _mover.DeclareAttack(BossMover.Attack.Wall); TriggerAttackPose(); Spiral(pool); } break;    // 二重スパイラル
        }
    }

    // フィナーレ（HP2割以下）：「雨」(雨青の針)＋「机が落ちる」(藍の菱形螺旋)を同時展開。
    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        // 雨＝赤い付箋の扇／机が落ちる＝書類の束の螺旋。最後は机の上のものが全部こぼれてくる。
        if (_fireT >= Di(0.9)) { _fireT = 0; SetSpellVisual(Spells[0].shape, Spells[0].tint, BulletArt.Get(Spells[0].art), Spells[0].rot); FanDown(pool); }
        if (_fireT2 >= Di(0.085)) { _fireT2 = 0; SetSpellVisual(BulletShape.Diamond, Spells[1].tint, BulletArt.AkariDocs, 44f); Spiral(pool); }
    }

    // 弾サイズ階層（#攻撃種ごとのサイズ差）：密集バラマキ(FanDown/Ring)=小／連続糸(Spiral)=極小／
    //   自機狙いの精密弾(AimedSpread)=大。当たり芯ドットは全形状共通描画＝大きくしても被弾点は埋もれない。
    // 2026-09-17 ユーザー指示：真下（Pi/2）固定の扇をやめ、自機方向を中心に張る。
    // 左に張り付いているだけでは当たらない＝どこにいても避ける動きを要求する。扇の幅は据え置き。
    private void FanDown(BulletPool pool)
    {
        int k = Dn(_fanCount);
        var aim = AimAtPlayer();
        float baseA = Mathf.Atan2(aim.Y, aim.X);
        for (int i = 0; i < k; i++)
        {
            float t = k > 1 ? (float)i / (k - 1) - 0.5f : 0f;
            float a = baseA + t * Mathf.DegToRad(80f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * EnemyBulletSpeed, 3.0f);
        }
    }

    private void Ring(BulletPool pool)
    {
        int k = Dn(_ringCount);
        _ringOff += Mathf.DegToRad(11f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / k;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _ringSpeed, 3.0f);
        }
    }

    // グレイズで和らぐ弾（#12 機構側）：自機を追う2スペル（「ずっと一緒」「離さない」）の弾だけ、
    // グレイズ（かすり）すると減速×Bullet.GrazeSoftenMul＋淡色化する＝離さないと迫る距離が、触れると和らぐ。
    // 被弾判定は不変（安全化しすぎ防止）。フィナーレでは無効＝最後の圧は緩めない。
    private void AimedSpread(BulletPool pool)
    {
        float baseA = Mathf.Atan2(AimAtPlayer().Y, AimAtPlayer().X);
        for (int i = -_aimedWing; i <= _aimedWing; i++)
        {
            float a = baseA + i * Mathf.DegToRad(14f);
            var b = FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _aimedSpeed, 4.0f);
            b.SoftenOnGraze = true; // AimedSpread は「ずっと一緒」(pattern2)専用＝スペル限定が自然に成立
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(13f);
        for (int s = 0; s < 2; s++)
        {
            float a = _ringOff + Mathf.Pi * s;
            var b = FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _spiralSpeed, 2.6f);
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

    // 本体に弾が刺さった一拍を移動側へ渡す（小さくのけぞって戻る）。当たり判定の中心は
    // 最大4px・時定数0.12sでしか動かない＝弾避けの公平性は保つ（BossMover.OnHit のコメント参照）。
    protected override void OnBodyDamaged(Vector2 fromDir) => _mover.OnHit(fromDir);

    protected override void OnHpChanged()
    {
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        if (_postsBroken < PostThresholds.Length && HpRatio <= PostThresholds[_postsBroken] + 0.00001f)
            _postPending = true;
        if (_memoryPending) return;
        // HPが閾値を割るたびに攻撃パターンを変える。
        if (_beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
            ApplySpell();
            // 第二形態は既存の閾値の中盤（PatternThresholds[1]=0.52）に乗せる＝新しい閾値を足さない。
            // ここは既に ApplySpell の宣告が出る節目なので、形態変化も同じ一拍に重ねて読ませる。
            if (_beatsFired == 2 && !_memoryPlayed)
            {
                _memoryPending = true;
                return;
            }
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
            GetHud()?.AnnounceSpell("あかり", BossHandles.AkariSpell, Spells[0].name + "＋" + Spells[1].name, Spells[0].tint);
        }
    }

    // S1-9 の RECLOSE（仮台本 06）。虚勢→弱気→最終形の宣言（動詞「返して」）の三段。
    //   2026-09-26（docs/20260926/主人公の存在_診断と本文 §3.1(b)）：弱気と最終形の間に、画面の前で読んでいる
    //   もう一人（ミナの後ろ＝あなた）へ返事を求める行。最終形「返して」は最後のまま（Min(idx, len-1) で以後ずっと出る行）。
    private static readonly string[] RecloseLines =
    {
        "既読も三秒でつけるし。返事も、すぐ書くし。……だから、ね?",
        "バカ。……バカ、バカ。",
        "……読んでるの、あなただけじゃ、ないでしょ。……後ろの人。ねえ、そっちも、返して。",
        "……返して。読んだなら、返してよ。",
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
        SetBodyPose(BossParts.Pose.Cry);
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.EndFury();           // 【激情】メーターを止めて畳む
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        // 改心が始まる確実な瞬間に「解決音（完）」へ移す＝途切れていたフレーズが最後まで歌われる。
        Audio.Instance?.PlayRedeem(1);
        // 会話を出せない状況（Hud が取れない／台詞が無い）なら会話に入らず即着地させる
        //   ＝送るものが無いのに EndCryNow を待ち続けて Finished が立たない詰まりを断つ。
        //   ルナティックも同じ経路＝改心のかけあい（弾を止める会話）を出さず、その場で着地して Finished へ。
        if (hud == null || _lines.Length == 0 || GameManager.LunaticActive) { EndCryNow(); return; }
        hud.HoldBubble = true;
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

    // 保険タイムアウトで cry が強制終了されたとき、会話ドライバも畳む（_seq が残ると台詞が出続ける）。
    protected override void AbortCrySequence() => _seq = false;

    // 戦闘中の独白・浄化のかけあいを Z で手動送り。
    public override void _Process(double delta)
    {
        if (_post != null || _revealingDraft) return;
        // 他ジョブ潜行の回想（吹き出しのみ）は自前で送る。フィルムと違い World を止めないので、
        //   Hud.HoldBubble＝BubblePaused が弾と敵を止めている間にここで送り切る。
        if (_memoryTalk is { Active: true }) { _memoryTalk.Update(delta); return; }
        // Start outside collision dispatch so suspending the world cannot interrupt a hit callback.
        if (_memoryPending && !_seq && !IsPurified && !Hud.BubblePaused)
        {
            _memoryPending = false;
            _memoryPlayed = true;
            // ルナティック：回想を挟まない。フィルム明けの復帰（第二形態→宣告→保留していた閾値の拾い直し）だけをその場で通す
            //   ＝弾幕も曲も途切れない。
            if (GameManager.LunaticActive)
            {
                AdvanceForm2();
                ApplySpell();
                OnHpChanged();
                return;
            }
            _caster.CancelPendingAttacks();
            void ResumeBattle()
            {
                _zHeld = Pad.AdvanceHeld();
                _fireT = _fireT2 = 0;
                Audio.Instance?.StartAkariMusic(_postsBroken, 0.8f);
                AdvanceForm2();
                ApplySpell();
                OnHpChanged();
            }
            // ミナ潜行＝この面のボスのフィルム（操作キャラを問わない。StoryFilm.cs 冒頭の「選択の規則」参照）。
            // 他ジョブ潜行＝一枚絵を起こさず CharacterStory.Memory（潜行キャラ×この面のボスの9通り）を会話枠で
            //   （2026-09-23 ユーザー指示「吹き出しのやり取りだけにして」）。どちらも終わりは同じ ResumeBattle。
            if (_charStory)
            {
                var game = GetNodeOrNull<GameManager>("/root/Game");
                FilmSkip.MarkSeen(game, CharacterStory.SeenKey(game!.SelectedJob, aftermath: false));   // 写真アプリの解禁
                _memoryTalk = CharacterStoryTalk.Start(CharacterStory.Memory(game.SelectedJob, "akari"),
                    GetHud, ShowStoryLine, ResumeBattle);
            }
            else AkariStoryFilm.Play(GetHud()!, GetParent(), aftermath: false, completed: ResumeBattle);
            return;
        }
        if (_postPending && _corridorPhase == 0 && !_seq && !IsPurified && !Hud.BubblePaused)
        {
            StartPost();
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
                if (_line >= _lines.Length)
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
        var (who, text, face) = _lines[_line];
        var hud = GetHud();
        if (hud == null) return;
        var kind = (Hud.LineKind)who;
        // 決定打の手前で音を落とす（台本の「ここでBGM停止」）。以降は無音のまま決定打を置く。
        //   本編＝本文一致（BgmStopLine）／改心相当シーン＝ト書きの行番号（CharacterStory.RedemptionSilenceAt）。
        if (_charStory ? _line == _storySilenceAt : text == BgmStopLine) Audio.Instance?.StopMusic(1.2f);
        string portrait = kind switch
        {
            Hud.LineKind.Companion => face,                 // 潜行キャラ本人＝表情差分（空欄は Hud がジョブ立ち絵へ）
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? "res://char/v3/akari_face.png" : face, // あかりは行ごと差し替え可（こはる方式）
            _ => "res://char/mina_face.png",                // ミナ
        };
        hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    // 他ジョブ潜行の回想の1行（CharacterStoryTalk から呼ばれる）。立ち絵の引き当ては ShowLine と同じ規則。
    //   who は 6（潜行キャラ本人）／2（このボス）／4（Ｘ投稿。Hud 側が立ち絵を捨てる）だけ。
    private void ShowStoryLine(Hud hud, int who, string text, string face)
    {
        var kind = (Hud.LineKind)who;
        string portrait = kind switch
        {
            Hud.LineKind.Companion => face,
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? "res://char/v3/akari_face.png" : face,
            _ => face,
        };
        hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
