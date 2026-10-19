using Godot;

// BossKoharu : STAGE2「こはる」のボス＝『我に返るわたし』（@koharu_light）。
// 消えた配信画面の前の部屋。止まったら我に返る人。明るさと蒼白を往復する。
// 浄化後は、ミナが消されたコメントを返し（S2-8 一段目）、来ていた回数を数えた証人として
// 自分の言葉で決定打を打つ（二段目「八十七回」「一秒も」）。推しの側の話も、親の話もしない。
// 台詞の正典: docs/20260928/wiki_仮台本_退避/07_粗い台本_案C_2_こはるとレイ.md（ユーザー承認済み・2026-09-05）の S2-7・S2-8。
public partial class BossKoharu : Enemy
{
    public override UnfolderKind UnfolderStyle => UnfolderKind.Koharu;
    public bool EdgeAttackActive => _caster != null && _caster.EdgeAttackActive;
    internal float BodyExtent => Mathf.Max(BodyRadius, BodyHalfH);
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();

    private bool _finale;
    private bool _memoryPending;
    private bool _memoryPlayed;
    private int _pattern;
    private int _beatsFired;
    private const int PatternCount = 4;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    // こはるの顔。学校の明るい顔＝koharu_face／蒼白＝koharu_face_pale／配信画面の光を浴びた＝koharu_face_lit。
    // 泣き顔差分は無く、蒼白が絶望を担う（仮台本 07 の但し書き）。
    private const string KFace = "res://char/v3/koharu_face.png";

    // 予測攻撃キャスター（通常テレグラフ。「お残し禁止」中は Suppressed で一時停止する）。
    private AreaSpellCaster _caster = null!;
    private BossPostSequence _posts = null!;
    public bool PostSequenceActive => _posts != null && _posts.Active;
    private float _memoryHp = 0.52f;
    private bool _midpointPassed;

    private bool _gotoFired;      // 発火ワンショット
    private int _gotoPhase;
    private float _gotoHp = 0.26f;              // INI: goto_hp（第4スペル切替26%と同じ被弾＝終盤入りの合図。進行中は切替宣言を保留する）

    // フィナーレ発動HPの上限（INI: finale_cap）。既定式 0.5/バー本数 が Easy(2本)だと25%＝第4スペル切替26%の直後で、
    // 第4スペル帯がほぼ無い（QA指摘）。旧0.18で第4帯（26→18%＝16HP）を作っていたが、その帯は読めない長さのまま
    // フィナーレが 36HP に痩せて（他3ボスの Easy は 50HP）宣言カード5秒の内に撃破される方が問題だった。
    // 0.26＝第4スペル切替と同じ被弾で発火させ、Easy のフィナーレを 52HP（他ボス相当）に戻す。Normal以上は式の方が小さく影響なし。
    // ※ 実装は Min(式, cap) なので cap は「下げる」方向にしか効かず、Easy は式 0.25 が採用される（実効 50HP・切替26%の 2HP 下）。
    private float _finaleCap = 0.26f;

    private static readonly float[] PatternThresholds = { 0.78f, 0.50f, 0.26f };

    // スペルカード（STAGE2 こはる＝配信画面の光の琥珀と、我に返る一拍の深紅）。技名は仮台本 07 の S2-7。
    // 弾は「推し活グッズ」の絵で飛ぶ（art の名前＝char/v3/bullets/<name>.png）。推している間だけ
    // 忘れていられた物が、そのまま弾になって降ってくる。最後の「我に返る」だけは消灯したペンライト
    // ＝終わったあとの部屋を指す。弾形・色は絵の裏のグロー（と絵が無い時の保険）として残す
    // ＝当たり判定・弾数・弾速は不変。
    //   rot: 絵の回転速度(deg/s)。落ちてくるもの（ペンライト）ほど大きく回す。
    private static readonly (string name, BulletShape shape, Color tint, string art, float rot)[] Spells =
    {
        ("ちゃんとしなきゃ", BulletShape.Orb,     new Color("e8a24a"), "koharu_star_pin", 46f),
        ("みんな見てる",     BulletShape.Diamond, new Color("d6443f"), "koharu_acrylic",  30f), // アクスタ＝視線
        ("期待",             BulletShape.Needle,  new Color("e87a3c"), "koharu_ticket",   62f), // チケットの半券
        ("我に返る",         BulletShape.Rice,    new Color("ffa14a"), "koharu_penlight", 96f), // 消えたペンライト
    };
    private static BossMover.Attack StanceOf(int pattern) => (pattern % PatternCount) switch
    {
        0 => BossMover.Attack.Ring,
        2 => BossMover.Attack.Aimed,
        _ => BossMover.Attack.Wall,
    };

    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        _mover.SetNextAttack(StanceOf(_pattern));
        SetSpellVisual(s.shape, s.tint, BulletArt.Get(s.art), s.rot);
        GetHud()?.SetBossBarTint(s.tint); // HPバーもスペル色へ（#26 フェーズ移行の可視化）
        GetHud()?.AnnounceSpell("こはる", BossHandles.KoharuMain, s.name, s.tint);
    }

    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "楽しいの。ほんとに。見てる間だけは、ぜんぶ、忘れてられるの。", "res://char/v3/koharu_face_pale.png"),
        (2, "でも、電気つけたら——机の上に、模試。机の下に、箱。……あたし、なにしてんだろ。", "res://char/v3/koharu_face_pale.png"),
        (2, "……忘れてた時間、ぜんぶ、むだで——", "res://char/v3/koharu_face_pale.png"),
        (1, "消されたコメントを、拾っておりました。「レイちゃんがいたから、今日も学校行けた」。", "res://char/mina_face.png"),
        (2, "……っ。……それ、消したもん。……重いって、思われそうで、やだったから……", "res://char/v3/koharu_face_pale.png"),
        (1, "“むだだ”という声なら、ここへ来るまでに、ぜんぶ祓いました。", "res://char/mina_face.png"),
        (1, "来ていた回数を、数えました。八十七回。", "res://char/mina_face.png"),
        (1, "——むだな時間は、一秒も、ありませんでしたよ。", "res://char/mina_face.png"),
        (2, "……ほんとに？", "res://char/v3/koharu_face_pale.png"),
        (1, "明日も来る約束は、なくてよいと思います。今日は、眠りたいとおっしゃっていましたから。", "res://char/mina_face.png"),
        (2, "……また、見に行って、いいのかな。", "res://char/v3/koharu_face_pale.png"),
        (1, "もちろん。見たい日に、見たいぶんだけ。……ペンライトの振り方も、まだ教わりたいです。", "res://char/mina_face.png"),
    };
    // 決定打の手前で音を落とす行（本文一致で拾う）。ここから BGM 無しで決定打を置く。
    private const string BgmStopLine = "来ていた回数を、数えました。八十七回。";

    // 改心の台詞の実体。他ジョブ潜行中はミナ前提の Lines の代わりに改心相当シーン
    //   （CharacterStory.Redemption＝キャラ×面の9通り）を流す（2026-09-15。_Ready で差し替える）。
    //   _storySilenceAt＝改心相当シーンの「ここでBGM停止」行（ト書き。-1=無し）。
    private bool _charStory;
    private (int who, string text, string face)[] _lines = Lines;
    private int _storySilenceAt = -1;
    // 他ジョブ潜行の回想（CharacterStory.Memory）を会話枠だけで送るドライバ。null＝ミナ潜行（＝フィルム）。
    private static readonly (int who, string text, string face)[] MemoryLeadIn = {
        (1, "こはるさん。初めて配信を見た日も、このペンライトを？", "res://char/mina_face.png"),
        (2, "ううん。最初は、何も持ってなかった。コメントも、書けなくて。", "res://char/v3/koharu_face.png"),
        (1, "それでも、また見たくなったのですね。", "res://char/mina_face.png"),
        (2, "……見てるだけのあたしにも、ありがとうって言ってくれたの。", "res://char/v3/koharu_face.png"),
        (1, "その言葉が、うれしかった。", "res://char/mina_face.png"),
        (2, "うん。模試が返ってきた夜で、眠れなかったから。あたし、誰とも話したくなくて……。", "res://char/v3/koharu_face.png"),
        (1, "はい。……その夜のお話、もう少し聞かせてください。", "res://char/mina_face.png"),
    };

    private CharacterStoryTalk? _memoryTalk;

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [koharu]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("koharu", "points", 1800);
        BodyRadius = BossTuning.F("koharu", "body_radius", 19f);
        BodyHalfH = BossTuning.F("koharu", "body_half_h", 23f);   // 縦長カプセル（絵の形に沿わせる）
        PanelCount = BossTuning.I("koharu", "panel_count", 5); // 「むだだ」等の言葉（黒い吹き出し）
        PanelInk = BossTuning.I("koharu", "panel_ink", 48);
        OrbitRadius = BossTuning.F("koharu", "orbit_radius", 26f);
        SpinSpeed = BossTuning.F("koharu", "spin_speed", 0.85f);
        EnemyBulletSpeed = BossTuning.F("koharu", "bullet_speed", 80f);

        int bars = BossTuning.I("koharu", "hp_bars", 0);
        BarCount = bars > 0 ? bars : DiffBars(finalBoss: false);

        // 弾幕・ギミックの外出し値（INIに無ければフィールド初期値＝現行値のまま）。
        _memoryHp = BossTuning.F("koharu", "meal_hp", 0.52f);
        _gotoHp = BossTuning.F("koharu", "goto_hp", 0.28f);
        _finaleCap = BossTuning.F("koharu", "finale_cap", 0.26f);

        // v3 の本体（エフェクト無し・720px）。視線の線・後光・ペンライトの光は BossParts が重ねる。
        PreTexPath = "res://char/v3/boss_koharu_body_idle.png";
        DownTexPath = BossDownArt.Path("koharu");
        AttackTexPath = "res://char/v3/boss_koharu_body_attack.png"; // 撃つ一拍だけ差し替えて戻る
        RecoveryTexPath = "res://char/v3/boss_motion/koharu_recover_v1.png";
        // 改心の三段：穢れ(pre＝待機)→泣き(cry＝専用の泣き顔)→改心後(post)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post へ着地する。
        // 旧 *_body_hit.png は被弾リアクション用で笑顔のままだった＝撃破しても穢れのままに見えたので、
        // 描き下ろしの *_body_cry.png（720px・エフェクトなし）に差し替えた。倍率・アンカーは待機と同じ。
        Form2TexPath = BossAnimalArt.Path("koharu", "idle");
        Form2AttackTexPath = BossAnimalArt.Path("koharu", "attack");
        Form2DownTexPath = BossAnimalArt.Path("koharu", "down");
        CryTexPath = "res://char/v3/boss_koharu_body_cry.png";
        PostTexPath = "res://char/v3/enemy_koharu_post.png";
        // 表示高は ini（body_display_h）。v3 の本体はエフェクト込みで焼いていないぶん、旧52だと小さく見える。
        BodyDisplayH = BossTuning.F("koharu", "body_display_h", 72f);
        SquashScale = 1f;
        PopLiftPx = 0f;
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    protected override (float Scale, Vector2 Offset) GetBodyFrame(Texture2D texture)
    {
        // Normalize the person, not the staff or coat, and keep the idle foot anchor.
        var (headY, foot) = texture.ResourcePath.GetFile() switch
        {
            "boss_koharu_body_attack.png" => (75f, new Vector2(263f, 718f)),
            "boss_koharu_body_idle2.png" => (0f, new Vector2(235f, 718f)),
            "boss_koharu_body_cry.png" => (0f, new Vector2(220f, 718f)),
            "enemy_koharu_post.png" => (0f, new Vector2(93f, 359f)),
            _ => (0f, new Vector2(219f, 718f)),
        };
        float height = texture.GetHeight();
        float scale = (629f - 72f) / (foot.Y - headY) * height / 720f;
        Vector2 anchor = new Vector2(114f, 269f) * height / (720f * scale);
        return (scale, anchor - (foot - texture.GetSize() / 2f));
    }

    public override void _Ready()
    {
        base._Ready();
        var game = GetNodeOrNull<GameManager>("/root/Game");
        _charStory = CharacterStory.DiveActive(game);
        if (_charStory)
        {
            _lines = CharacterStory.Redemption(game!.SelectedJob, "koharu");
            _storySilenceAt = CharacterStory.RedemptionSilenceAt(game.SelectedJob, "koharu");
        }
        // ボス登場＝道中BGMからこはる固有テーマへクロスフェード（温かい旋律が冷えて減衰＝未完）。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossKoharu);
        // 移動：スペルごとの立ち位置＋状態機械（待機→構え→攻撃→余韻）。数値は INI（[koharu] の
        // cruise_speed / accel_time / stance_*）。こはるは「軽く小刻み・攻撃前に一瞬止まる」＝
        // accel_time が小さく（キビキビ）、構え（stance_windup）が長めで本動作が短く鋭い。
        _mover.Configure("koharu", new Vector2(Field.BossCenterX, Field.BossZoneCenterY), Field.BossZoneHalfW, Field.BossZoneHalfH);
        GetHud()?.ShowBossBar("我に返るわたし", BossHandles.KoharuMain, this);
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        // 【激情】メーター開始。初期値は予定表（s2_1）の選択から決まる（FuryMeter.cs）。
        GetNodeOrNull<GameManager>("/root/Game")?.BeginFury("koharu");
        ApplySpell();

        _caster = new AreaSpellCaster();
        _caster.Configure("koharu", GetParent());
        AddChild(_caster);
        _posts = BossPostSequence.Attach(this, "koharu", _caster.CancelPendingAttacks, OnHpChanged);

        // 部品の演出層（char/v3/fx/koharu/*.png）を本体の子として1個ぶら下げる。当たり判定は持たない。
        // 引数は待機・攻撃の本体画像の幅（720px 基準）＝実測の基準点を中心基準へ読み替えるのに要る。
        AttachParts("koharu", idleTexW: 626f, attackTexW: 585f);
    }

    protected override void UpdateMovement(double delta)
    {
        // 自機の位置を渡す＝自機狙いの追従（x と y の両方に寄る）と、反転の判定（40px 以上・0.6秒）に使う。
        if (GetTree().GetFirstNodeInGroup("player") is Node2D pl) _mover.SetPlayerPos(pl.GlobalPosition);
        if (!BarrageHolding) GlobalPosition = _mover.Step(GlobalPosition, delta, IsForm2 ? 1.65f : 1f);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, IsForm2 ? !_mover.FacingLeft : _mover.FacingLeft);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Koharu, GlobalPosition, (float)delta, 32f);
        TickGoto();
        if (_gotoPhase == 0 && !_caster.EdgeAttackActive)
            TickForm2Technique(delta, _mover, _caster);
    }

    private void TickGoto()
    {
        if (_gotoPhase == 0 || _caster.AoeActive) return;
        _gotoPhase = 0;
        _caster.Suppressed = false;
        OnHpChanged();
    }

    // 本体に弾が刺さった一拍を移動側へ渡す（小さくのけぞって戻る）。当たり判定の中心は
    // 最大4px・時定数0.12sでしか動かない＝弾避けの公平性は保つ（BossMover.OnHit のコメント参照）。
    protected override void OnBodyDamaged(Vector2 fromDir) => _mover.OnHit(fromDir);

    protected override void OnHpChanged()
    {
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        if (_memoryPending) return;
        if (!_memoryPlayed && HpRatio <= _memoryHp && _gotoPhase == 0)
        {
            _memoryPending = true;
            return;
        }
        // 「五徳の十字火」：HP26%（INI: goto_hp）を割った瞬間に一度だけ（第4スペル切替と同じ被弾＝終盤入りの合図）。
        // スペル切替より先に判定する＝同じ被弾では十字火の宣言が勝つ（切替の宣言は下の holds で保留）。
        if (HpRatio <= _memoryHp) _midpointPassed = true;
        if (!_gotoFired && _midpointPassed && HpRatio <= _gotoHp)
        {
            _gotoFired = true;
            _gotoPhase = 1;
            if (_caster != null) _caster.Suppressed = true; // 通常テレグラフも保留（十字に集中させる）
            _caster!.RequestFinale();
            GetHud()?.ShowBossLine("こはる", "……うごかないで。いま、鏡、見ちゃうから。", UiKit.Kegare, 2.0);
        }
        // 一撃で複数の閾値を跨いでも、終盤の技が終わるまで形態変化を保留する。
        bool holds = _gotoPhase != 0 || (!_gotoFired && HpRatio <= _gotoHp);
        if (!holds && _beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
            ApplySpell();
            // 第二形態は既存の閾値の中盤（PatternThresholds[1]=0.50）に乗せる＝新しい閾値を足さない。
            if (_beatsFired == 2) AdvanceForm2();
        }
        if (!holds && !_finale && HpRatio <= Mathf.Min(0.20f, _finaleCap)
            && (GameManager.SkipBossStory || _posts.Count >= 4))
        {
            _finale = true;
            RallyShield();
            GetHud()?.SetBossBarTint(Spells[0].tint); // フィナーレ色（#26）
            GetHud()?.AnnounceSpell("こはる", BossHandles.KoharuMain, Spells[0].name + "＋" + Spells[1].name, Spells[0].tint);
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

    // S2-7 の RECLOSE（仮台本 07）。宣言 → 「やめないで……止まったら」 → 取り繕い、の三段を順送り。
    //   「止まったら我に返る」の意味に変わっている（12 流用資産の型はそのまま、文言を組み直し）。
    //   2026-09-26（docs/20260926/主人公の存在_診断と本文 §3.2(a)）：「みんな見てる」と言い張る子が本当に見ているのは手だけ。
    //   自機の足取りはご主人様のもの（正典）＝ミナの後ろの手を見つける行。取り繕い「楽しいってば」は最後のまま。
    private static readonly string[] RecloseLines =
    { "みんな見てる。……見てるもん。ちゃんとしなきゃ、だめだもん。", "やめないで……止まったら——", "……ねえ、ミナ。ペンライト、止まってるよ。楽しく、なくなっちゃった？", "……なんでもない。楽しいってば。" };
    private int _recloseIdx;
    protected override void OnRecloseLine()
    {
        ShowRecloseLine("こはる", RecloseLines[Mathf.Min(_recloseIdx, RecloseLines.Length - 1)]);
        _recloseIdx++;
    }

    protected override void OnCryStart()
    {
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.EndFury();           // 【激情】メーターを止めて畳む
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        // 改心が始まる確実な瞬間に「解決音（完）」へ移す＝冷えていた旋律に温かい残響が戻る。
        Audio.Instance?.PlayRedeem(2);
        // 会話を出せない状況（Hud が取れない／台詞が無い）なら会話に入らず即着地させる
        //   ＝送るものが無いのに EndCryNow を待ち続けて Finished が立たない詰まりを断つ。
        //   ルナティックも同じ経路＝改心のかけあい（弾を止める会話）を出さず、その場で着地して Finished へ。
        if (hud == null || _lines.Length == 0 || GameManager.SkipBossStory) { EndCryNow(); return; }
        hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        // 画の反転（S2-9）：改心成立（cry→post）で、消えていた配信画面が灯り、途中で切れていた
        //   ペンライトの光が画面まで届き切る（帰還の会話の背景でゆっくり。指ししない＝気づく余白）。
        //   旧演出（空席の箸に湯気が戻る）は StageImagery.DrawKoharuReversal ごと差し替え済み。
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
            // ルナティック：回想を挟まない。フィルム明けの復帰（保留していた閾値の拾い直し）だけをその場で通す
            //   ＝弾幕も曲も途切れない。
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
                _memoryTalk = CharacterStoryTalk.Start(CharacterStory.Memory(game.SelectedJob, "koharu"),
                    GetHud, ShowStoryLine, ResumeBattle);
            }
            else _memoryTalk = CharacterStoryTalk.Start(MemoryLeadIn, GetHud, ShowStoryLine,
                () => KoharuStoryFilm.Play(GetHud()!, GetParent(), aftermath: false, completed: ResumeBattle));
            return;
        }
        if (!_seq && !_memoryPending && _gotoPhase == 0) _posts.UpdateDamage();
        // 改心の会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld && !Pad.UiBlocked(this);
        _zHeld = z;
        _lineT += delta;

        if (_seq)
        {
            var dialogHud = GetHud()!;
            if (zEdge && !dialogHud.DialogRevealed)
            {
                dialogHud.RevealDialogNow();
                _lineT = 0;
                NotifyCryProgress();
            }
            else if (dialogHud.DialogRevealed
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
        // 旧稿の記憶フラッシュ（StageImagery 側・撤去済み）は呼ばない。案C の改心は回想ではなく
        // 「S2-4 で消えた一行を、本人の前で返す」なので、台所の回想の画は場面と食い違う。
        // 決定打の手前で音を落とす（台本の「ここでBGM停止。無音のまま」）。
        //   本編＝本文一致（BgmStopLine）／改心相当シーン＝ト書きの行番号（CharacterStory.RedemptionSilenceAt）。
        if (_charStory ? _line == _storySilenceAt : text == BgmStopLine) Audio.Instance?.StopMusic(1.2f);
        string portrait = kind switch
        {
            Hud.LineKind.Companion => face,   // 潜行キャラ本人＝表情差分（空欄は Hud がジョブ立ち絵へ）
            // こはるは通常 koharu_face。絶望行だけ face に蒼白(KPale)を指定して差し替える。
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? KFace : face,
            _ => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face,   // ミナも行ごと表情
        };
        hud.ShowDialog(kind, text, portrait, otherName: "こはる");
    }

    // 他ジョブ潜行の回想の1行（CharacterStoryTalk から呼ばれる）。立ち絵の引き当ては ShowLine と同じ規則。
    //   who は 6（潜行キャラ本人）／2（このボス）／4（Ｙ投稿。Hud 側が立ち絵を捨てる）だけ。
    private void ShowStoryLine(Hud hud, int who, string text, string face)
    {
        var kind = (Hud.LineKind)who;
        string portrait = kind switch
        {
            Hud.LineKind.Companion => face,
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? KFace : face,
            _ => face,
        };
        hud.ShowDialog(kind, text, portrait, otherName: "こはる");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
