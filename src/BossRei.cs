using Godot;

// BossRei : STAGE3「星逢レイ」のボス＝本人が被っている配信のガワ。
// ガワは笑顔で固定＝被弾でも笑顔は崩さず、姿勢だけ落とす。怖さの源は笑顔のまま撃ってくること。
// 剥がしてHPを削り切る＝ガワが割れて、笑っていない中の人が出る＝改心。
// 台詞の正典: docs/20260928/wiki_仮台本_退避/07_粗い台本_案C_2_こはるとレイ.md（ユーザー承認済み・2026-09-05）の S3-6・S3-8。
public partial class BossRei : Enemy
{
    public override UnfolderKind UnfolderStyle => UnfolderKind.Rei;
    public bool Finished { get; private set; }
    public System.Action<System.Action>? MemoryFollowUp { get; set; }

    private readonly BossMover _mover = new BossMover();

    private bool _rallied;
    private bool _accelerated; // HP2割以下で戦闘BGMを一度だけ加速させた（多重発火防止）
    private bool _memoryPending;
    private bool _memoryPlayed;
    private int _pattern;
    private int _beatsFired;
    private const int PatternCount = 4;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    private AreaSpellCaster _caster = null!;
    private BossPostSequence _posts = null!;
    public bool PostSequenceActive => _posts != null && _posts.Active;
    public bool AoeGateActive => _caster != null && (_caster.AoeActive || _caster.EdgeAttackActive);
    private bool _relayFired;
    private bool _relayWatching;
    private int _relayStartLives;

    private static readonly float[] PatternThresholds = { 0.78f, 0.50f, 0.26f };

    private double _noDmgT;
    private int _tauntStage;
    private int _tauntLimit = 2;
    private double _tauntDelay = 6.0;
    private double _tauntStep = 4.0;
    private double _tauntCd;
    private int _tauntIdx;

    private float _relayHp = 0.26f;
    private const string RGawa = CompanionDialogue.ReiAvatarPortrait;

    // 挑発（ボスの動的セリフ演出＝ShowBossLine。弾は止めない。中継 who=5 は使わない）。
    private static readonly string[] TauntLines =
    { "初見さん、いらっしゃい!", "戦ってよ。わたしを、ちゃんと見てよ。", "……ミナ、手が止まってる。わたしの話、つまらなかった？" };

    private static readonly (BulletShape shape, Color tint, string art, float rotation)[] PhaseVisuals =
    {
        (BulletShape.Orb,     new Color("b9c2d0"), "rei_comment", 18f),
        (BulletShape.Diamond, new Color("9a72d9"), "rei_subscriber", -22f),
        (BulletShape.Star,    new Color("e8c45a"), "rei_microphone", 28f),
        (BulletShape.Ring,    new Color("5fb8c0"), "rei_film", -32f),
    };
    private static BossMover.Attack StanceOf(int pattern) => (pattern % PatternCount) switch
    {
        2 => BossMover.Attack.Aimed,
        3 => BossMover.Attack.Wall,
        _ => BossMover.Attack.Ring,
    };

    private void ApplyPhaseVisual()
    {
        var s = PhaseVisuals[_pattern % PhaseVisuals.Length];
        SetSpellVisual(s.shape, s.tint, BulletArt.Get(s.art), s.rotation);
        _mover.SetNextAttack(StanceOf(_pattern));
        GetHud()?.SetBossBarTint(s.tint); // HPバーもスペル色へ（#26 フェーズ移行の可視化）
    }
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "初見さん、大歓迎。コメント、全部読むから。……ぜんぶ、読んだから。", "res://char/v3/rei_gawa_face_v1.png"),
        (2, "登録者、二千。去年も、二千。……減ってない。減ってないでしょう?", "res://char/v3/rei_gawa_face_v1.png"),
        (2, "だから……だからなんで、だれも、中にいるわたしに、気づいてくれないのよ!", "res://char/v3/rei_gawa_face_v1.png"),
        (1, "見ていました。", "res://char/mina_face.png"),
        (2, "……は? ……なに、それ。……見てたって、なにを——", "res://char/v3/rei_gawa_face_v1.png"),
        (1, "だれも見ていない場所に、あなたが書いて、消した一行を。", "res://char/mina_face.png"),
        (1, "「わたしに、気づいてよ。わたしを、見てよ」。", "res://char/mina_face.png"),
        (2, "…………。", "res://char/v3/rei_face_cry.png"),
    };
    // 決定打の手前で BGM を落とす行（0始まり。Lines の「見ていました。」）＝ここから無音のまま置く。
    private const int SilenceAtLine = 3;
    // ガワが割れる行（0始まり）。この行の表示で cry テクスチャ（＝中の人）へ差し替える。
    private const int BreakAtLine = 5;

    // 改心の台詞の実体。他ジョブ潜行中はミナ前提の Lines の代わりに改心相当シーン
    //   （CharacterStory.Redemption＝キャラ×面の9通り）を流す（2026-09-15。_Ready で差し替える）。
    //   行番号トリガのうち BGM 停止はト書き（_storySilenceAt）へ差し替え、**ガワ割れは発火させない**：
    //   R3/R6/R9 の正典（doc 第1部6節）は「ガワは割れず、声だけが素に戻る／再契約する」＝
    //   割れる（ShellPeel）はミナ本編の専有。改心後（post）もガワのまま退場する（_Ready で差し替え）。
    private bool _charStory;
    private (int who, string text, string face)[] _lines = Lines;
    private int _storySilenceAt = -1;
    // 他ジョブ潜行の回想（CharacterStory.Memory）を会話枠だけで送るドライバ。null＝ミナ潜行（＝フィルム）。
    private static readonly (int who, string text, string face)[] MemoryLeadIn = {
        (1, "その「いらっしゃい」は、最初から今の言い方だったのですか。", "res://char/mina_face.png"),
        (2, "……もっと、下手だったわよ。誰もいない画面に、何しゃべればいいか分からなくて。", "res://char/v3/rei_gawa_face_v1.png"),
        (1, "それでも、始めたかったのですね。", "res://char/mina_face.png"),
        (2, "帰ってきた人に、おかえりって言える場所が欲しかったの。わたしも、誰かに言ってほしかったから。", "res://char/v3/rei_gawa_face_v1.png"),
        (1, "最初に、言葉を返してくれた人は？", "res://char/mina_face.png"),
        (2, "一行だけ。「今日、誰とも話してなかった」って。……まだ、覚えてる。", "res://char/v3/rei_gawa_face_v1.png"),
        (1, "聞かせてください。その一行を読んだ、レイさんのお話を。", "res://char/mina_face.png"),
    };

    private CharacterStoryTalk? _memoryTalk;

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [rei]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("rei", "points", 1500);
        BodyRadius = BossTuning.F("rei", "body_radius", 19f);
        BodyHalfH = BossTuning.F("rei", "body_half_h", 23f);   // 縦長カプセル（絵の形に沿わせる）
        PanelCount = BossTuning.I("rei", "panel_count", 5); // 「二番」の言葉（黒い吹き出し）
        PanelInk = BossTuning.I("rei", "panel_ink", 54);
        OrbitRadius = BossTuning.F("rei", "orbit_radius", 26f);
        SpinSpeed = BossTuning.F("rei", "spin_speed", 0.9f);
        EnemyBulletSpeed = BossTuning.F("rei", "bullet_speed", 82f);

        // INI hp_bars > 0 で全難易度を固定本数に上書きできる。
        int bars = BossTuning.I("rei", "hp_bars", 0);
        BarCount = bars > 0 ? bars : DiffBars(finalBoss: false) + 1;

        _tauntLimit = BossTuning.I("rei", "pressure_max", 2);
        _tauntDelay = BossTuning.F("rei", "pressure_delay", 6.0f);
        _tauntStep = BossTuning.F("rei", "pressure_step", 4.0f);
        _relayHp = BossTuning.F("rei", "relay_hp", 0.26f);

        // v3 の本体＝ガワ（エフェクト無し・720px）。飾り枠・吹き出し・光の帯は BossParts が重ねる。
        PreTexPath = "res://char/v3/boss_rei_body_idle.png";
        ConfigureReiAvatarMotion();
        ReiBossTechnique.PrepareAssets();
        DownTexPath = BossDownArt.Path("rei");
        AttackTexPath = "res://char/v3/boss_rei_body_attack.png"; // 撃つ一拍だけ差し替えて戻る
        RecoveryTexPath = "res://char/v3/boss_motion/rei_recover_v1.png";
        // 改心の三段：ガワ(pre＝待機)→中の人が泣いている(cry)→中の人(post)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post へ着地する。
        // 旧 *_body_hit.png は被弾リアクション用でガワのまま笑っていた＝ガワが割れた感じが出なかったので、
        // 描き下ろしの *_body_cry.png（720px・中の人・エフェクトなし）に差し替えた。
        Form2TexPath = BossAnimalArt.Path("rei", "idle");
        Form2AttackTexPath = BossAnimalArt.Path("rei", "attack");
        Form2DownTexPath = BossAnimalArt.Path("rei", "down");
        CryTexPath = "res://char/v3/boss_rei_body_cry.png";
        PostTexPath = "res://char/v3/boss_rei_post.png";
        // レイだけ cry/post が「ガワの中の人」＝ガワと同じ表示高で出すと同一人物の等身が破綻する。
        // ガワ（待機）の 75% で出す。素材はどれも足元まで詰めてあるので、縮めて浮くぶんは
        // Enemy.ApplyBodyOffset が押し下げて、ガワと同じ床に両足で立たせる。
        CryBodyScale = 0.75f;
        PostBodyScale = 0.75f;
        // 表示高は ini（body_display_h）。v3 の本体はエフェクト込みで焼いていないぶん、旧52だと小さく見える。
        BodyDisplayH = BossTuning.F("rei", "body_display_h", 72f);
        // 姿勢ごとの足元合わせ（BossParts.BodyOffsets の "rei" 行）。
        BodyOffsetName = "rei";
        CryHoldDur = 9999.0;     // 自動終了させない＝cry を会話尺いっぱい保持（EndCryNow で post へ）
        // S3-8：ガワは改心の会話の途中（決定打の行）で割れる。撃破の瞬間に cry 絵へ差し替えてしまうと
        // 「笑顔のまま声だけが崩れる」三行が中の人の顔で流れてしまうので、差し替えを遅延させて
        // ShowLine が BreakAtLine で BreakCryBodyNow() を呼ぶ。
        DeferCryBodySwap = true;
        // その差し替えを「クロスフェード」ではなく「ガワが左右にほどけて、奥にいた人が見える」で見せる
        //（docs/20260906/astra_試行_ガワ割れ.md 案1・ユーザー決定 2026-09-06）。
        // 中の人は決定打の時点で既にガワの背後に立っている＝変身ではなく、隠れていた人が現れる。
        PeelCryBodySwap = true;
        // ガワの下から現れた姿を見せる余韻は、他の敵より長く保つ。
        PurifiedExitHoldOverride = 3.6;
    }

    public override void _Ready()
    {
        base._Ready();
        var game = GetNodeOrNull<GameManager>("/root/Game");
        _charStory = CharacterStory.DiveActive(game);
        if (_charStory)
        {
            _lines = CharacterStory.Redemption(game!.SelectedJob, "rei");
            _storySilenceAt = CharacterStory.RedemptionSilenceAt(game.SelectedJob, "rei");
            // ガワは割らない（R3/R6/R9 の契約）：BreakCryBodyNow を呼ばないので DeferCryBodySwap の
            //   ままガワで喋り切り、FinishCry の post 着地もガワの姿へ差し替える（中の人を出さない）。
            //   等身補正（0.75）は中の人用なのでガワには掛けない。
            PostTexPath = PreTexPath;
            PostBodyScale = 1f;
        }
        // ボス登場＝道中BGMからレイ固有テーマへクロスフェード（モチーフが主音直前で半音落ちる＝未完）。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossRei);
        // 移動：状態機械＋立ち位置つき（あかり・こはる・ミナと同じ作法）。レイの性格＝配信の枠から
        // 出ない＝横移動をほぼ捨て（stance_edge_x/track_w が小さい）、傾き（lean_max）で表情を作る。
        // 数値は config/boss_stats.ini の [rei] 節（cruise_speed / stance_* 一式）。
        _mover.Configure("rei", new Vector2(Field.BossCenterX, Field.BossZoneCenterY), Field.BossZoneHalfW, Field.BossZoneHalfH);
        GetHud()?.ShowBossBar("星逢レイ", BossHandles.ReiMain, this);
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        // 【激情】メーター開始。初期値は道中（s3_5c）の選択から決まる（FuryMeter.cs）。
        GetNodeOrNull<GameManager>("/root/Game")?.BeginFury("rei");
        ApplyPhaseVisual();

        _caster = new AreaSpellCaster();
        _caster.Configure("rei", GetParent());
        AddChild(_caster);
        _posts = BossPostSequence.Attach(this, "rei", _caster.CancelPendingAttacks, OnHpChanged);

        // 部品の演出層（char/v3/fx/rei/*.png）を本体の子として1個ぶら下げる。当たり判定は持たない。
        // 引数は待機・攻撃の本体画像の幅（720px 基準）＝実測の基準点を中心基準へ読み替えるのに要る。
        AttachParts("rei", idleTexW: 533f, attackTexW: 556f);
    }

    protected override void UpdateMovement(double delta)
    {
        // 自機の位置は毎フレーム渡す（自機狙いの追従＝x と y の両方、向きの反転判定に要る）。
        if (GetTree().GetFirstNodeInGroup("player") is Node2D pl) _mover.SetPlayerPos(pl.GlobalPosition);
        if (!BarrageHolding) GlobalPosition = _mover.Step(GlobalPosition, delta, IsForm2 ? 1.65f : 1f);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, IsForm2 ? !_mover.FacingLeft : _mover.FacingLeft, _mover.SquashScale);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Rei, GlobalPosition, (float)delta, 32f);
        TickRelayWatch();
        TickTaunt(delta);
        if (_caster.AoeActive || _caster.EdgeAttackActive) _mover.SetNextAttack(BossMover.Attack.Spell);
        else TickForm2Technique(delta, _mover, _caster);
    }

    // 中断は完走に含めず、開始時の残機を保った場合だけ既存の回復報酬を渡す。
    private void TickRelayWatch()
    {
        if (!_relayWatching || _caster == null || _caster.AoeActive) return;
        _relayWatching = false;
        if (GetTree().GetFirstNodeInGroup("player") is not Player pl) return;
        if (_relayStartLives < 0 || pl.Lives < _relayStartLives || !_caster.FinaleCompleted) return;
        GetHud()?.ShowBossLine("レイ", "……初見さん、じゃなかったのね。", UiKit.Kegare, 2.4); // 無被弾報酬の一言（仮台本 07 の S3-6。「合格。なんて」は捨てた）
        Vector2 at = _caster.LastChainSafe;
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

    private void TickTaunt(double delta)
    {
        if (_tauntCd > 0) _tauntCd -= delta;
        if (_caster != null && (_caster.AoeActive || _caster.EdgeAttackActive)) return; // 回避に集中する時間は逃げ腰を咎めない
        if (!IsShieldPhase) return; // 殴れない時間（合図/窓/セリフ）は与ダメゼロを咎めない
        _noDmgT += delta;
        int want = _noDmgT < _tauntDelay ? 0
                 : Mathf.Min(_tauntLimit, 1 + (int)((_noDmgT - _tauntDelay) / _tauntStep));
        if (want > _tauntStage)
        {
            _tauntStage = want;
            if (_tauntCd <= 0)
            {
                _tauntCd = 8.0;
                GetHud()?.ShowBossLine("レイ", TauntLines[Mathf.Min(_tauntIdx, TauntLines.Length - 1)], UiKit.Kegare, 1.6);
                _tauntIdx++;
            }
        }
    }

    public override void OnPlayerDealtDamage()
    {
        _noDmgT = 0;
        _tauntStage = 0;
    }

    // 本体に弾が刺さった一拍を移動側へ渡す（小さくのけぞって戻る）。当たり判定の中心は
    // 最大4px・時定数0.12sでしか動かない＝弾避けの公平性は保つ（BossMover.OnHit のコメント参照）。
    protected override void OnBodyDamaged(Vector2 fromDir) => _mover.OnHit(fromDir);

    protected override void OnHpChanged()
    {
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        if (_memoryPending) return;
        if (!_memoryPlayed && HpRatio <= PatternThresholds[1])
        {
            _memoryPending = true;
            return;
        }
        while (_beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
            ApplyPhaseVisual();
            // 第二形態は既存の閾値の中盤（PatternThresholds[1]=0.50）に乗せる＝新しい閾値を足さない。
            // ガワにひびが入る一拍。割るのは改心の決定打（ShellPeelFx）なのでここでは割らない。
            if (_beatsFired == 2) AdvanceForm2();
        }
        // 適応演出：総HPの2割を割った瞬間に、実音源の戦闘BGMを一度だけ加速させる（緊迫の高揚）。
        //   PitchScale を 0.6秒かけて約1.15へ滑らかに上げる＝ピッチも少し上がる（合意済み）。
        //   フェードで別曲に切り替わると Audio.Music() 側で 1.0 に戻る＝ボス戦を抜けたら通常速度。
        if (!_accelerated && HpRatio <= 0.2f)
        {
            _accelerated = true;
            Audio.Instance?.SetMusicSpeed(1.15f);
        }
        if (!_relayFired && HpRatio <= _relayHp)
        {
            _relayFired = true;
            _caster?.RequestFinale();
            _relayWatching = true;
            _relayStartLives = (GetTree().GetFirstNodeInGroup("player") as Player)?.Lives ?? -1;
        }
        if (!_rallied && HpRatio <= 0.20f && (GameManager.SkipBossStory || _posts.Count >= 4))
        {
            _rallied = true;
            RallyShield();
            GetHud()?.SetBossBarTint(PhaseVisuals[2].tint); // フィナーレ色（#26）
        }
    }

    protected override int LimitBodyDamage(int damage)
        => _posts == null ? damage : _posts.Active ? 0 : DamageToHpFloor(damage, _posts.Floor);

    public override void Purify()
    {
        if (_posts != null && _posts.Active) return;
        base.Purify();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_posts != null && _posts.Active) return;
        base._PhysicsProcess(delta);
    }

    // RECLOSE のキャラ別弱気セリフ（序盤=虚勢→終盤=弱気。サイクルごとに index を進め、超えたら最後を使い回す）。
    // S3-6 の RECLOSE（仮台本 07）。笑顔のまま。ガワは崩れず、言葉だけが逃げ腰になっていく。
    private static readonly string[] RecloseLines =
    { "……逃げるの? また。——いいわ。切り抜かれないんだから、わたし。", "初見さん、まだ、いてくれるでしょう?", "……ミナ。初見さんって呼ばないほうが、ここにいてくれる？", "見てて。……ちゃんと、見ててよ。" };
    private int _recloseIdx;
    protected override void OnRecloseLine()
    {
        ShowRecloseLine("レイ", RecloseLines[Mathf.Min(_recloseIdx, RecloseLines.Length - 1)]);
        _recloseIdx++;
    }

    protected override void OnCryStart()
    {
        if (IsForm2 && DeferCryBodySwap)
            SetMotionFrame(ResourceLoader.Load<Texture2D>("res://char/v3/boss_rei_body_idle2.png"));
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.EndFury();           // 【激情】メーターを止めて畳む
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        // 改心が始まる確実な瞬間に「解決音（完）」へ移す＝半音で落ちていたモチーフが主音に届く。
        Audio.Instance?.PlayRedeem(0);
        // 会話を出せない状況（Hud が取れない／台詞が無い）なら会話に入らず即着地させる
        //   ＝送るものが無いのに EndCryNow を待ち続けて Finished が立たない詰まりを断つ。
        //   ルナティックも同じ経路＝改心のかけあい（弾を止める会話）を出さず、その場で着地して Finished へ
        //   （ガワ割れ ShellPeel は決定打の行で起こす演出なので、会話ごと出さない＝post の中の人へ直接着地）。
        if (hud == null || _lines.Length == 0 || GameManager.SkipBossStory) { EndCryNow(); return; }
        hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        // S3 画の反転：改心成立（cry→post）で、白飛びしていた「登録者 2,000」に色が差し始める
        //（帰還の会話の背景でゆっくり進む。ズーム/フラッシュで指ししない＝気づく余白）。
        (GetTree().GetFirstNodeInGroup("imagery") as StageImagery)?.TriggerReversal();
        Finished = true;
    }

    // 保険タイムアウトで cry が強制終了されたとき、会話ドライバも畳む（_seq が残ると台詞が出続ける）。
    protected override void AbortCrySequence() => _seq = false;

    public override void _Process(double delta)
    {
        if (_posts.Active) return;
        // 他ジョブ潜行の回想（吹き出しのみ）は自前で送る。フィルムと違い World を止めないので、
        //   Hud.HoldBubble＝BubblePaused が弾と敵を止めている間にここで送り切る。
        if (_memoryTalk is { Active: true }) { _memoryTalk.Update(delta); return; }
        if (_memoryPending && !_seq && !IsPurified && !Hud.BubblePaused)
        {
            _memoryPending = false;
            _memoryPlayed = true;
            // ルナティック：回想を挟まない。フィルム明けの復帰（第二形態・保留していた閾値の拾い直し）だけをその場で通す
            //   ＝弾幕も曲も途切れない（S3-7 の割り込みは StageRei 側がルナティックで抑止）。
            if (GameManager.SkipBossStory) { OnHpChanged(); return; }
            _caster.CancelPendingAttacks();
            void ResumeBattle()
            {
                _zHeld = Pad.AdvanceHeld();
                _posts.ResumeMusic();
                OnHpChanged();
            }
            // ミナ潜行＝この面のボスのフィルム（操作キャラを問わない。StoryFilm.cs 冒頭の「選択の規則」参照）。
            // 他ジョブ潜行＝一枚絵を起こさず CharacterStory.Memory（潜行キャラ×この面のボスの9通り）を会話枠で
            //   （2026-09-23 ユーザー指示「吹き出しのやり取りだけにして」）。どちらも終わりは同じ ResumeBattle。
            if (_charStory)
            {
                var game = GetNodeOrNull<GameManager>("/root/Game");
                FilmSkip.MarkSeen(game, CharacterStory.SeenKey(game!.SelectedJob, aftermath: false));   // 写真アプリの解禁
                _memoryTalk = CharacterStoryTalk.Start(CharacterStory.Memory(game.SelectedJob, "rei"),
                    GetHud, ShowStoryLine, ResumeBattle);
            }
            else _memoryTalk = CharacterStoryTalk.Start(MemoryLeadIn, GetHud, ShowStoryLine,
                () => ReiStoryFilm.Play(GetHud()!, GetParent(), aftermath: false, completed: () =>
                {
                    if (MemoryFollowUp != null) MemoryFollowUp(ResumeBattle);
                    else ResumeBattle();
                }));
            return;
        }
        if (!_seq && !_memoryPending) _posts.UpdateDamage();
        // 改心の会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld && !Pad.UiBlocked(this);
        _zHeld = z;
        _lineT += delta;

        if (_seq)
        {
            // ガワ割れが走っている間（2.2秒）は送らせない。ここを送れてしまうと、二枚が離れていく途中で
            // 会話が畳まれて post 差し替えが走り、「割れる」より先に「別の絵になった」が来てしまう。
            // QA の自動送りも同じ経路（Pad.AdvanceHeld）を通るので、同じだけ待つ。
            var dialogHud = GetHud()!;
            if (zEdge && !ShellPeelBusy && !dialogHud.DialogRevealed)
            {
                dialogHud.RevealDialogNow();
                _lineT = 0;
                NotifyCryProgress();
            }
            else if (!ShellPeelBusy && dialogHud.DialogRevealed
                     && (zEdge || (dialogHud.FastForwarding && _lineT >= 0.25) || (dialogHud.AutoAdvanceReady && _lineT >= 1.4)))
            {
                _lineT = 0; _line++;
                NotifyCryProgress(); // 送れている間は保険タイムアウトを起こさない
                if (_line >= _lines.Length)
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
        var (who, text, face) = _lines[_line];
        var hud = GetHud();
        if (hud == null) return;
        var kind = (Hud.LineKind)who;
        // 決定打の手前で音を落とす（台本の「ここでBGM停止。無音のまま」。あかり・こはる面と同型）。
        //   本編＝SilenceAtLine／改心相当シーン＝ト書きの行番号（CharacterStory.RedemptionSilenceAt）。
        if (_charStory ? _line == _storySilenceAt : _line == SilenceAtLine) Audio.Instance?.StopMusic(1.2f);
        // 決定打でガワが割れる＝ここで初めて本体を cry（中の人）へ差し替える。それまではガワのまま。
        //   改心相当シーン（他ジョブ）では**割らない**＝R3/R6/R9 の正典「ガワは割れず、声だけが素に戻る」。
        if (!_charStory && _line == BreakAtLine) BreakCryBodyNow();
        string portrait = kind switch
        {
            Hud.LineKind.Companion => face,   // 潜行キャラ本人＝表情差分（空欄は Hud がジョブ立ち絵へ）
            // 戦闘中のレイはガワ（笑顔固定・泣き顔なし）。割れる行だけ face 指定で中の人の泣き顔へ。
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? RGawa : face,
            _ => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, // ミナも行ごと表情
        };
        hud.ShowDialog(kind, text, portrait, otherName: "レイ");
    }

    // 他ジョブ潜行の回想の1行（CharacterStoryTalk から呼ばれる）。立ち絵の引き当ては ShowLine と同じ規則（既定はガワ）。
    //   who は 6（潜行キャラ本人）／2（このボス）／4（Ｙ投稿。Hud 側が立ち絵を捨てる）だけ。ガワは割らない。
    private void ShowStoryLine(Hud hud, int who, string text, string face)
    {
        var kind = (Hud.LineKind)who;
        string portrait = kind switch
        {
            Hud.LineKind.Companion => face,
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? RGawa : face,
            _ => face,
        };
        hud.ShowDialog(kind, text, portrait, otherName: "レイ");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
