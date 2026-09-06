using Godot;

// BossKoharu : STAGE2「こはる」のボス＝『我に返るわたし』（@koharu_light）。
// 消えた配信画面の前の部屋。止まったら我に返る人。明るさと蒼白を往復する。
// 浄化後は、ミナが消されたコメントを返し（S2-8 一段目）、来ていた回数を数えた証人として
// 自分の言葉で決定打を打つ（二段目「八十七回」「一秒も」）。推しの側の話も、親の話もしない。
// 台詞の正典: wiki/08_仮台本/07_粗い台本_案C_2_こはるとレイ.md（ユーザー承認済み・2026-09-05）の S2-7・S2-8。
public partial class BossKoharu : Enemy
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
    private const int PatternCount = 4;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    // こはるの顔。学校の明るい顔＝koharu_face／蒼白＝koharu_face_pale／配信画面の光を浴びた＝koharu_face_lit。
    // 泣き顔差分は無く、蒼白が絶望を担う（仮台本 07 の但し書き）。
    private const string KFace = "res://char/v3/koharu_face.png";
    private const string KPale = "res://char/v3/koharu_face_pale.png";
    private const string MWorried = "res://char/mina_worried.png";

    // 予測攻撃キャスター（通常テレグラフ。「お残し禁止」中は Suppressed で一時停止する）。
    private AreaSpellCaster _caster = null!;

    // ── INI 外出しのバランス値（config/boss_stats.ini [koharu]。読めなければ現行既定値）──
    private double _ringInterval = 1.0, _fanInterval = 1.1, _aimedInterval = 0.7, _spiralInterval = 0.085;
    private int _ringCount = 16, _fanCount = 9, _aimedWing = 1; // wing=way数の片翼（3way→1）
    private float _ringSpeed = 70f, _aimedSpeed = 96f, _spiralSpeed = 88f;

    // ── 「お残し禁止」（HP52%ワンショットスペル）──
    //   通常弾とテレグラフを止めて宣告 → 画面右半分（ボス側）に“料理弾”3列×8＝24発を配膳
    //  （消せる祈り弾 MakeErasable・降下12px/s）。8秒以内に撃って消した分は祈り弾の既存経路で
    //   そのまま報われる（Bullet.OnAreaEntered → GameManager.AddPrayerCleared: +15点）。
    //   残した弾は時間切れで 0.06s 間隔で順に自機狙いニードル（130px/s）に変わって飛んでくる。
    //   完食（被弾なし・ボムなしで全24発を撃ち消す）＝こはるの動的セリフ＋回復ハート1個
    //  （報酬の流儀はレイの安置リレー完走報酬 BossRei.TickRelayWatch に合わせる。♥上限時はスコアで返す）。
    //   テーマ＝「アーカイブ、ぜんぶ見て」：前へ出て見るほど見残し（＝後の弾）が減って安全
    //   ＋ゲージも伸びる（リスクとリターン）。ボムで薙ぎ払うと弾は消えるが“食べて”いない＝完食報酬なし。
    private bool _mealFired;         // 発火ワンショット
    private int _mealPhase;          // 0=非活性 / 1=宣告→配膳待ち / 2=食事時間(8s) / 3=お残し→ニードル変換中
    private double _mealT;
    private int _mealStartLives, _mealStartBombs;  // 完食報酬の判定スナップショット（被弾なし・ボムなし）
    private readonly System.Collections.Generic.List<Bullet> _meal = new();     // 配膳した料理弾
    private readonly System.Collections.Generic.List<Bullet> _mealLeft = new(); // 時間切れ時のお残し（変換待ち行列）
    // 主要パラメータは INI（config/boss_stats.ini [koharu] meal_*）で上書き可。初期値＝現行値。
    private float _mealHp = 0.52f;         // 発動HP割合
    private int _mealRows = 3, _mealCols = 8; // 配膳の行×列（3×8=24発）
    private double _mealServeDelay = 0.9;  // 宣告→配膳の溜め
    private double _mealWindow = 8.0;      // 食事時間
    private double _mealConvStep = 0.06;   // お残し→ニードル変換の間隔（順に“飛んでくる”連鎖感）
    private float _mealFallSpeed = 12f;    // 料理弾の降下速度（ゆっくり＝狙って食べられる）
    private float _mealNeedleSpeed = 130f; // お残しニードルの速度
    private static readonly Color MealNeedleTint = new("d6443f"); // 深紅（「みんな見てる」の色）

    // ── 「五徳の十字火」（HP28%ワンショットスペル・B-4②）──
    //   宣告 → 自機の現在地に十字（BeamH＋BeamV・予兆1.2s×WarnMul）→ 0.8s後に同じ中心へ45°回転した
    //   第二十字（BeamSeg±45°・深紅のX）。第二十字を“同心”にするのは回避の読みやすさのため：
    //   第一十字の安全地帯（対角）へ逃げた自機を、同じ中心のX字が正確に追う＝「次は軸方向へ戻る」と
    //   いう読み筋が幾何で明示される。自機の最新位置に置き直す案は、回避移動中の頭上に湧いて
    //   残り猶予が読めない理不尽が出るため不採用。進行中は通常弾とテレグラフを止める（お残し禁止と同じ流儀）。
    private bool _gotoFired;      // 発火ワンショット
    private int _gotoPhase;       // 0=なし / 1=宣告→第一十字待ち / 2=第二十字待ち / 3=着弾待ち（ゲート解除待ち）
    private double _gotoT;
    private Vector2 _gotoCenter;  // 第一十字の中心（宣告時でなく“出現時”の自機位置＝予兆と実位置のズレを作らない）
    private float _gotoHp = 0.26f;              // INI: goto_hp（第4スペル切替26%と同じ被弾＝終盤入りの合図。進行中は切替宣言を保留する）
    private const double GotoFirstDelay = 0.7;  // 宣告→第一十字の溜め（通常テレグラフの _fireDelay と同格）
    private const double GotoSecondDelay = 0.8; // 第一十字→第二十字（45°回転）の遅延
    private const float GotoBeamHalf = 7f;      // 十字ビームの半太さ(px)（通常テレグラフのビーム帯 5-8px と同格）
    private const float GotoDiagLen = 460f;     // 斜め一閃の長さ＝対角441pxを覆う（包丁の軌跡と同じ）
    private static readonly Color GotoTint = new("e8945a");  // 琥珀（こはるAOEの危険色＝第一十字）
    private static readonly Color GotoHot = new("ffc06a");
    private static readonly Color GotoXTint = new("d6443f"); // 深紅（包丁の軌跡と同色＝斜め斬りの色＝第二十字）
    private static readonly Color GotoXHot = new("ff8a7a");

    // フィナーレ発動HPの上限（INI: finale_cap）。既定式 0.5/バー本数 が Easy(2本)だと25%＝第4スペル切替26%の直後で、
    // 第4スペル帯がほぼ無い（QA指摘）。旧0.18で第4帯（26→18%＝16HP）を作っていたが、その帯は読めない長さのまま
    // フィナーレが 36HP に痩せて（他3ボスの Easy は 50HP）宣言カード5秒の内に撃破される方が問題だった。
    // 0.26＝第4スペル切替と同じ被弾で発火させ、Easy のフィナーレを 52HP（他ボス相当）に戻す。Normal以上は式の方が小さく影響なし。
    // ※ 実装は Min(式, cap) なので cap は「下げる」方向にしか効かず、Easy は式 0.25 が採用される（実効 50HP・切替26%の 2HP 下）。
    private float _finaleCap = 0.26f;

    // HPがこの割合を割るたびに攻撃パターンを変える（独白は浄化のかけあいに集約）。
    private static readonly float[] PatternThresholds = { 0.78f, 0.50f, 0.26f };

    // スペルカード（STAGE2 こはる＝配信画面の光の琥珀と、我に返る一拍の深紅）。技名は仮台本 07 の S2-7。
    // 弾は「推し活グッズ」の絵で飛ぶ（art の名前＝char/v3/bullets/<name>.png）。推している間だけ
    // 忘れていられた物が、そのまま弾になって降ってくる。最後の「我に返る」だけは消灯したペンライト
    // ＝終わったあとの部屋を指す。弾形・色は絵の裏のグロー（と絵が無い時の保険）として残す
    // ＝当たり判定・弾数・弾速は不変。
    //   rot: 絵の回転速度(deg/s)。落ちてくるもの（ペンライト）ほど大きく回す。
    private static readonly (string name, BulletShape shape, Color tint, string art, float rot)[] Spells =
    {
        ("ちゃんとしなきゃ", BulletShape.Orb,     new Color("e8a24a"), "koharu_badge",    46f), // 缶バッジの輪
        ("みんな見てる",     BulletShape.Diamond, new Color("d6443f"), "koharu_acrylic",  30f), // アクスタ＝視線
        ("期待",             BulletShape.Needle,  new Color("e87a3c"), "koharu_ticket",   62f), // チケットの半券
        ("我に返る",         BulletShape.Rice,    new Color("ffa14a"), "koharu_penlight", 96f), // 消えたペンライト
    };
    // 攻撃パターン→立ち位置の対応（0 リング＝中央に据わる／1 祈り弾の扇＝端で帯を張る／
    // 2 自機狙い＝自機の x を追う／3 スパイラル＝端）。
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
        GetHud()?.AnnounceSpell("こはる", "@koharu_light", s.name, s.tint);
    }

    // S2-8 改心（仮台本 07。ユーザー承認済み・2026-09-05）。二段で抜く：
    //   (1) ミナが S2-4 で拾った「消されたコメント」を本人へ返す
    //   (2) 来ていた回数を数えた証人として、ミナ自身の言葉で決定打（「八十七回」「一秒も」）
    // 案C に少年は居ないので中継（who=5）も使わない＝決定打はミナが自分の声で言う。
    // 「来ていた回数を、数えました。」の行で BGM を落とし、決定打を無音のまま置く。
    // 泣き顔差分は無く、蒼白（KPale）が絶望を担う。推しの側の話も、親の話もしない。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "楽しいの。ほんとに。見てる間だけは、ぜんぶ、忘れてられるの。", KFace),
        (2, "でも、電気つけたら——机の上に、模試。机の下に、箱。……あたし、なにしてんだろ。", KPale),
        (2, "……忘れてた時間、ぜんぶ、むだで——", KPale),
        (1, "消されたコメントを、拾っておりました。「レイちゃんがいたから、今日も学校行けた」。", ""),   // (1) S2-4 の回収
        (2, "……っ。……それ、消したもん。……重いって、思われそうで、やだったから……", KPale),
        (1, "“むだだ”という声なら、ここへ来るまでに、ぜんぶ祓いました。", MWorried),
        (1, "来ていた回数を、数えました。八十七回。一度も、欠けていません。", MWorried),   // ここで BGM 停止
        (1, "——むだな時間は、一秒も、ありませんでしたよ。", MWorried),                     // (2) 決定打。無音のまま
        (2, "……ほんとに? ……明日も、見に行って、いいのかな。", KFace),
    };
    // 決定打の手前で音を落とす行（本文一致で拾う）。ここから BGM 無しで決定打を置く。
    private const string BgmStopLine = "来ていた回数を、数えました。八十七回。一度も、欠けていません。";

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [koharu]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("koharu", "points", 1800);
        BodyRadius = BossTuning.F("koharu", "body_radius", 9f);
        PanelCount = BossTuning.I("koharu", "panel_count", 5); // 「むだだ」等の言葉（黒い吹き出し）
        PanelInk = BossTuning.I("koharu", "panel_ink", 3); // 2→3（B-5: 中盤でシールド段が痩せない用）
        OrbitRadius = BossTuning.F("koharu", "orbit_radius", 26f);
        SpinSpeed = BossTuning.F("koharu", "spin_speed", 0.85f);
        PanelsFire = false;
        EnemyBulletSpeed = BossTuning.F("koharu", "bullet_speed", 80f);

        // HPバー本数は難易度別（通常ボス：Easy2/Normal4/Hard5/Lunatic6）。INI hp_bars > 0 で固定上書き。
        int bars = BossTuning.I("koharu", "hp_bars", 0);
        BarCount = bars > 0 ? bars : DiffBars(finalBoss: false);

        // 弾幕・ギミックの外出し値（INIに無ければフィールド初期値＝現行値のまま）。
        _ringInterval = BossTuning.F("koharu", "ring_interval", 1.0f);
        _ringCount = BossTuning.I("koharu", "ring_count", 16);
        _ringSpeed = BossTuning.F("koharu", "ring_speed", 70f);
        _fanInterval = BossTuning.F("koharu", "fan_interval", 1.1f);
        _fanCount = BossTuning.I("koharu", "fan_count", 9);
        _aimedInterval = BossTuning.F("koharu", "aimed_interval", 0.7f);
        _aimedSpeed = BossTuning.F("koharu", "aimed_speed", 96f);
        _aimedWing = Mathf.Max(0, BossTuning.I("koharu", "aimed_ways", 3) / 2); // 奇数way→片翼数
        _spiralInterval = BossTuning.F("koharu", "spiral_interval", 0.085f);
        _spiralSpeed = BossTuning.F("koharu", "spiral_speed", 88f);
        _mealHp = BossTuning.F("koharu", "meal_hp", 0.52f);
        _mealRows = Mathf.Max(1, BossTuning.I("koharu", "meal_rows", 3));
        _mealCols = Mathf.Max(1, BossTuning.I("koharu", "meal_cols", 8));
        _mealWindow = BossTuning.F("koharu", "meal_window", 8.0f);
        _mealServeDelay = BossTuning.F("koharu", "meal_serve_delay", 0.9f);
        _mealFallSpeed = BossTuning.F("koharu", "meal_fall_speed", 12f);
        _mealNeedleSpeed = BossTuning.F("koharu", "meal_needle_speed", 130f);
        _mealConvStep = Mathf.Max(0.01f, BossTuning.F("koharu", "meal_convert_step", 0.06f));
        _gotoHp = BossTuning.F("koharu", "goto_hp", 0.28f);
        _finaleCap = BossTuning.F("koharu", "finale_cap", 0.26f);

        // v3 の本体（エフェクト無し・720px）。視線の線・後光・ペンライトの光は BossParts が重ねる。
        PreTexPath = "res://char/v3/boss_koharu_body_idle.png";
        AttackTexPath = "res://char/v3/boss_koharu_body_attack.png"; // 撃つ一拍だけ差し替えて戻る
        // 改心の三段：穢れ(pre＝待機)→泣き(cry＝専用の泣き顔)→改心後(post)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post へ着地する。
        // 旧 *_body_hit.png は被弾リアクション用で笑顔のままだった＝撃破しても穢れのままに見えたので、
        // 描き下ろしの *_body_cry.png（720px・エフェクトなし）に差し替えた。倍率・アンカーは待機と同じ。
        CryTexPath = "res://char/v3/boss_koharu_body_cry.png";
        PostTexPath = "res://char/v3/enemy_koharu_post.png";
        // 表示高は ini（body_display_h）。v3 の本体はエフェクト込みで焼いていないぶん、旧52だと小さく見える。
        BodyDisplayH = BossTuning.F("koharu", "body_display_h", 72f);
        // 姿勢ごとの足元合わせ（BossParts.BodyOffsets の "koharu" 行）。こはるは 3 姿勢で足元の高さも
        // 違う（待機 y=629／攻撃 668／被弾 633）ので x だけでなく y も補正する。
        BodyOffsetName = "koharu";
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    public override void _Ready()
    {
        base._Ready();
        // ボス登場＝道中BGMからこはる固有テーマへクロスフェード（温かい旋律が冷えて減衰＝未完）。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossKoharu);
        // 移動：スペルごとの立ち位置＋状態機械（待機→構え→攻撃→余韻）。数値は INI（[koharu] の
        // cruise_speed / accel_time / stance_*）。こはるは「軽く小刻み・攻撃前に一瞬止まる」＝
        // accel_time が小さく（キビキビ）、構え（stance_windup）が長めで本動作が短く鋭い。
        _mover.Configure("koharu", new Vector2(200f, 70f), 90f, 28f);
        GetHud()?.ShowBossBar("我に返るわたし", "@koharu_light");
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        ApplySpell();

        _caster = new AreaSpellCaster();
        _caster.Configure("koharu", GetParent());
        AddChild(_caster);

        // 部品の演出層（char/v3/fx/koharu/*.png）を本体の子として1個ぶら下げる。当たり判定は持たない。
        // 引数は待機・攻撃の本体画像の幅（720px 基準）＝実測の基準点を中心基準へ読み替えるのに要る。
        AttachParts("koharu", idleTexW: 626f, attackTexW: 585f);
    }

    protected override void UpdateMovement(double delta)
    {
        // 自機の x を渡す＝自機狙いの横滑りと、反転の判定（40px 以上・0.6秒）に使う。
        if (GetTree().GetFirstNodeInGroup("player") is Node2D pl) _mover.SetPlayerX(pl.GlobalPosition.X);
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, _mover.FacingLeft);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Koharu, GlobalPosition, (float)delta, 32f);
        TickMeal(delta);
        TickGoto(delta);
        FirePattern(delta);
    }

    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        // 「お残し禁止」「五徳の十字火」進行中は通常弾を止める（食べる/避けるに集中させる。レイの安置リレーと同じ流儀）。
        if (_mealPhase != 0 || _gotoPhase != 0) return;
        if (_finale) { FireFinale(pool, delta); return; }
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(_ringInterval)) { _fireT = 0; _mover.OnAttack(BossMover.Attack.Ring); TriggerAttackPose(); Ring(pool, Dn(_ringCount), _ringSpeed); } break;
            case 1: if (_fireT >= Di(_fanInterval)) { _fireT = 0; _mover.OnAttack(BossMover.Attack.Wall); TriggerAttackPose(); FanDown(pool); } break;
            case 2: if (_fireT >= Di(_aimedInterval)) { _fireT = 0; _mover.OnAttack(BossMover.Attack.Aimed); TriggerAttackPose(); Aimed(pool); } break;
            default: if (_fireT >= Di(_spiralInterval)) { _fireT = 0; _mover.OnAttack(BossMover.Attack.Wall); TriggerAttackPose(); Spiral(pool); } break;
        }
    }

    // フィナーレ（HP2割以下）：「ちゃんとしなきゃ」(琥珀の円弾リング)＋「みんな見てる」(深紅の菱形雨)を同時展開。
    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.95)) { _fireT = 0; SetSpellVisual(Spells[0].shape, Spells[0].tint, BulletArt.Get(Spells[0].art), Spells[0].rot); Ring(pool, Dn(16), 70f); }
        if (_fireT2 >= Di(0.85)) { _fireT2 = 0; SetSpellVisual(Spells[1].shape, Spells[1].tint, BulletArt.Get(Spells[1].art), Spells[1].rot); FanDown(pool); }
    }

    // 弾サイズ階層（#攻撃種ごとのサイズ差）：密集バラマキ(Ring)=小／連続糸(Spiral)=極小／
    //   自機狙いの精密弾(Aimed)=大／受け止め・撃ち返しの対象弾(FanDown祈り弾／配膳／お残しニードル)=中。
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

    // 「祈り弾」ギミック（#12 機構側／#20）：下方向の扇＝画面から落ちてくる光は、自機弾で“受け止め”られる。
    // 消すと双方消滅＋加点（GameManager.AddPrayerCleared）。自機・フォロワーの弾列が受け皿になる。
    // FanDown はスペル「みんな見てる」(pattern1)とフィナーレでしか撃たない＝スペル限定が自然に成立。
    // サイズは「受け止める対象」であることが一目でわかる中サイズ（配膳の料理弾 ServeMeal と同格）。
    private void FanDown(BulletPool pool)
    {
        int k = Dn(_fanCount);
        for (int i = 0; i < k; i++)
        {
            float t = (float)i / (k - 1) - 0.5f;
            float a = Mathf.Pi / 2f + t * Mathf.DegToRad(78f);
            var b = FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * EnemyBulletSpeed, 3.6f);
            b.MakeErasable();
        }
    }

    // 「お残し禁止」の進行。UpdateMovement 経由＝会話中(BubblePaused)は弾もタイマーも一緒に止まる。
    private void TickMeal(double delta)
    {
        if (_mealPhase == 0) return;
        _mealT += delta;
        switch (_mealPhase)
        {
            case 1: // 宣告 → 配膳
                if (_mealT < _mealServeDelay) return;
                if (!ServeMeal()) { FinishMeal(fullEat: false); return; } // Pool不在（起こらない保険）＝中断
                _mealPhase = 2; _mealT = 0;
                return;
            case 2: // 食事時間：完食は即判定。時間切れでお残しを回収して変換へ。
                if (CountMealAlive() == 0) { FinishMeal(fullEat: true); return; }
                if (_mealT < _mealWindow) return;
                _mealLeft.Clear();
                foreach (var b in _meal)
                    if (IsInstanceValid(b) && b.Active && b.Erasable) _mealLeft.Add(b);
                _meal.Clear();
                _mealPhase = 3; _mealT = 0;
                GetHud()?.ShowBossLine("こはる", "……見なかったところ、あるでしょ。……ぜんぶ、見てほしいのに。", UiKit.Kegare, 2.0);
                return;
            default: // 3: お残し→自機狙いニードル（0.06s間隔で順に）。変換待ちの間も撃って食べれば減らせる。
                var pool = GetNodeOrNull<BulletPool>("/root/Pool");
                var pl = GetTree().GetFirstNodeInGroup("player") as Node2D;
                while (_mealT >= _mealConvStep && _mealLeft.Count > 0)
                {
                    _mealT -= _mealConvStep;
                    var b = _mealLeft[0];
                    _mealLeft.RemoveAt(0);
                    if (pool == null || !IsInstanceValid(b) || !b.Active || !b.Erasable) continue; // 変換待ち中に食べた/消えた分
                    Vector2 at = b.GlobalPosition;
                    pool.Despawn(b);
                    Vector2 d = pl != null ? pl.GlobalPosition - at : new Vector2(-1, 0);
                    d = d.LengthSquared() > 0.01f ? d.Normalized() : new Vector2(-1, 0);
                    // お残し→撃ち返しニードルは中サイズ（3.0→3.8）＝「食べ残すと反撃が来る」の脅威を弾の大きさでも語る。
                    // お残し＝見られなかったアーカイブが「視線」になって撃ち返してくる＝アクスタの絵。
                    // 速度・半径・弾数は不変（見た目だけ）。
                    var nb = pool.Spawn(at, d * _mealNeedleSpeed, true, 3.8f, 1, BulletShape.Needle, MealNeedleTint);
                    nb.SetSprite(BulletArt.KoharuAcrylic, 30f);
                }
                if (_mealLeft.Count == 0) FinishMeal(fullEat: false);
                return;
        }
    }

    // 配膳：画面右半分（ボス側＝前へ出るほど早く食べ進められる）に 3列×8＝24発の“料理弾”を並べる。
    // 祈り弾（MakeErasable）＝自機弾で消す→AddPrayerCleared の既存経路がそのまま「食べた」報酬になる。
    // Y=44/64/84 開始＋降下12px/s：8秒（＋難易度の弾速倍率）でも下端216pxに届かず画面外に落ちない
    // ＝「勝手に消えて完食扱い」の事故を構造で防ぐ。INIで行列数を増やしても、格子の間隔を画面内に
    // 収まるようクランプして同じ保証を維持する（右端356px・開始Y上限94px）。
    private bool ServeMeal()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return false;
        _meal.Clear();
        // 「全部見なきゃ」の配膳弾＝箱から溢れたグッズ（うちわ）。祈り弾として並ぶので、
        // 撃って消す＝「見た／片づけた」になる。琥珀の円弾の色はグローとして絵の裏に残す。
        SetSpellVisual(Spells[0].shape, Spells[0].tint, BulletArt.KoharuUchiwa, 34f);
        float colStep = _mealCols > 1 ? Mathf.Min(20f, (356f - 214f) / (_mealCols - 1)) : 0f;
        float rowStep = _mealRows > 1 ? Mathf.Min(20f, (94f - 44f) / (_mealRows - 1)) : 0f;
        for (int row = 0; row < _mealRows; row++)
            for (int col = 0; col < _mealCols; col++)
            {
                var pos = new Vector2(214f + col * colStep, 44f + row * rowStep);
                var b = FireBullet(pool, pos, new Vector2(0f, _mealFallSpeed), 3.6f);
                b.MakeErasable();
                _meal.Add(b);
            }
        return true;
    }

    // まだ画面に残っている配膳弾（＝見ていないアーカイブ）の数。プール再利用対策：参照が生きたまま別の弾に転用されても、
    // Activate が Erasable を必ずリセットするため「Active かつ Erasable」だけが本物の料理弾
    //（ギミック中は FanDown が撃たない＝他に Erasable を立てる者がいない）。
    private int CountMealAlive()
    {
        int n = 0;
        foreach (var b in _meal)
            if (IsInstanceValid(b) && b.Active && b.Erasable) n++;
        return n;
    }

    // ギミック終了。fullEat=画面が時間内に空になった（ボム薙ぎ払い等を含むため、報酬判定は中で絞る）。
    private void FinishMeal(bool fullEat)
    {
        _mealPhase = 0;
        _meal.Clear();
        _mealLeft.Clear();
        if (_caster != null && _gotoPhase == 0) _caster.Suppressed = false; // 十字火が進行中なら解除しない（保険）
        var cur = Spells[_pattern % Spells.Length];
        SetSpellVisual(cur.shape, cur.tint, BulletArt.Get(cur.art), cur.rot); // 弾形・色・絵を通常スペルへ戻す（宣告カードは再掲しない）
        // 食事の間に保留していたスペル切替（第3「期待」50%）／フィナーレの宣言を、ここで発火させる。
        // 以降は完食報酬の early return が並ぶので、報酬の有無に関わらず通るこの位置で呼ぶ。
        // ApplySpell が SetSpellVisual を上書きするため、通常スペルへ戻した後であることも必要。
        OnHpChanged();
        if (!fullEat) return;
        if (GetTree().GetFirstNodeInGroup("player") is not Player pl) return;
        // “食べた”証明＝被弾なし・ボムなし（ボムで消しても弾は消えるが「食べて」いない＝褒めない。
        // 撃ち消しぶんの AddPrayerCleared は入っているので無報酬にはならない）。
        bool noHit = _mealStartLives >= 0 && pl.Lives >= _mealStartLives;
        bool noBomb = _mealStartBombs >= 0 && (GetNodeOrNull<GameManager>("/root/Game")?.Bombs ?? _mealStartBombs) >= _mealStartBombs;
        if (!noHit || !noBomb) return;
        GetHud()?.ShowBossLine("こはる", "……ぜんぶ、見てくれた。……ありがと。ありがと、ね。", UiKit.Kegare, 2.4);
        Vector2 at = pl.GlobalPosition;
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

    // 「五徳の十字火」の進行。UpdateMovement 経由＝会話中(BubblePaused)は予兆(AreaStrike)側と一緒に凍る。
    private void TickGoto(double delta)
    {
        if (_gotoPhase == 0) return;
        _gotoT += delta;
        float wm = _caster?.WarnMul() ?? 1f;
        switch (_gotoPhase)
        {
            case 1: // 宣告 → 第一十字（“出現時”の自機の現在地に置く）
                if (_gotoT < GotoFirstDelay) return;
                _gotoCenter = (GetTree().GetFirstNodeInGroup("player") as Node2D)?.GlobalPosition
                              ?? new Vector2(120f, 130f); // 自機不在（起こらない保険）＝自機定位置側
                SpawnGotoAxisCross(1.2 * wm);
                _gotoPhase = 2; _gotoT = 0;
                return;
            case 2: // 0.8s後 → 第二十字（同心・45°回転＝深紅のX）
                if (_gotoT < GotoSecondDelay) return;
                SpawnGotoDiagCross(1.2 * wm);
                _gotoPhase = 3; _gotoT = 0;
                return;
            default: // 3: 第二十字の着弾（予兆＋フラッシュ0.2s）を待ってゲート解除
                if (_gotoT < 1.2 * wm + 0.3) return;
                _gotoPhase = 0;
                if (_caster != null && _mealPhase == 0) _caster.Suppressed = false;
                // 十字火の間に保留していたスペル切替／フィナーレの宣言を、X着弾の瞬間に発火させる
                // （次の被弾を待つと、シールドが戻っていた場合に数秒「第3スペルのまま」が続く）。
                OnHpChanged();
                return;
        }
    }

    // 第一十字：自機の行（BeamH・全幅）＋列（BeamV・全高）。画面 384×216 の中心軸に置く。
    private void SpawnGotoAxisCross(double warn)
    {
        var world = GetParent();
        AddCrossStrike(world, AreaStrike.Shape.BeamH, new Vector2(192f, _gotoCenter.Y), 192f, GotoBeamHalf, warn);
        AddCrossStrike(world, AreaStrike.Shape.BeamV, new Vector2(_gotoCenter.X, 108f), GotoBeamHalf, 108f, warn);
    }

    private void AddCrossStrike(Node world, AreaStrike.Shape shape, Vector2 c, float hw, float hh, double warn)
    {
        var z = new AreaStrike();
        z.Configure(shape, hw, hh, warn, GotoTint, GotoHot);
        z.SetOwner(this); // 着弾前に浄化されたら予兆ごと消える（残留着弾を断つ）
        world.AddChild(z);
        z.GlobalPosition = c;
    }

    // 第二十字：第一十字と同じ中心を通る±45°の一閃×2（BeamSeg。中心から両側へ伸ばす）。
    private void SpawnGotoDiagCross(double warn)
    {
        var world = GetParent();
        foreach (float deg in new[] { 45f, -45f })
        {
            float a = Mathf.DegToRad(deg);
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var z = new AreaStrike();
            z.ConfigureBeam(dir, GotoDiagLen, GotoBeamHalf - 1f, warn, GotoXTint, GotoXHot);
            z.SetOwner(this);
            world.AddChild(z);
            z.GlobalPosition = _gotoCenter - dir * (GotoDiagLen * 0.5f);
        }
    }

    private void Aimed(BulletPool pool)
    {
        Vector2 d = AimAtPlayer();
        float baseA = Mathf.Atan2(d.Y, d.X);
        for (int i = -_aimedWing; i <= _aimedWing; i++)
        {
            float a = baseA + i * Mathf.DegToRad(13f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _aimedSpeed, 4.0f);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(12f);
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
        // 「五徳の十字火」：HP26%（INI: goto_hp）を割った瞬間に一度だけ（第4スペル切替と同じ被弾＝終盤入りの合図）。
        // お残し禁止の進行中は持ち越し（TickMeal の終了が呼ぶ OnHpChanged で発火）＝ワンショットギミック同士を重ねない。
        // スペル切替より先に判定する＝同じ被弾では十字火の宣言が勝つ（切替の宣言は下の gotoHolds で保留）。
        if (!_gotoFired && _mealPhase == 0 && HpRatio <= _gotoHp)
        {
            _gotoFired = true;
            _gotoPhase = 1; _gotoT = 0;
            if (_caster != null) _caster.Suppressed = true; // 通常テレグラフも保留（十字に集中させる）
            GetHud()?.AnnounceSpell("こはる", "@koharu_light", "自分なにしてんだろ", GotoTint);
            GetHud()?.ShowBossLine("こはる", "……うごかないで。いま、鏡、見ちゃうから。", UiKit.Kegare, 2.0);
        }
        // 「お残し禁止」：HP52%（INI: meal_hp）を割った瞬間に一度だけ（パターン切替50%の直前＝中盤の山）。
        // 十字火と同じくスペル切替より先に判定する＝同じ被弾では食事の宣言が勝つ（切替の宣言は下の mealHolds で保留）。
        // 十字火(26%)は必ず食事(52%)より後なので、十字火側のような進行中ガードは要らない（＝ここで止めると発火しない）。
        if (!_mealFired && HpRatio <= _mealHp)
        {
            _mealFired = true;
            _mealPhase = 1; _mealT = 0;
            _mealStartLives = (GetTree().GetFirstNodeInGroup("player") as Player)?.Lives ?? -1;
            _mealStartBombs = GetNodeOrNull<GameManager>("/root/Game")?.Bombs ?? -1;
            if (_caster != null) _caster.Suppressed = true; // 通常テレグラフも保留（配膳の上に予兆を重ねない）
            GetHud()?.AnnounceSpell("こはる", "@koharu_light", "全部見なきゃ", Spells[0].tint);
            GetHud()?.ShowBossLine("こはる", "アーカイブ、ぜんぶ残ってるから。ぜんぶ、見て。ね?", UiKit.Kegare, 2.2);
        }
        // ワンショットギミック（食事・十字火）の進行中と、その発火待ちの間は、スペル切替とフィナーレの宣言を保留する。
        // 無防備窓（4秒・上限100HP）の中では発動HPと切替を何%離しても1〜2ヒットで跨ぐため、値では宣言カードを守れない
        //（旧 goto 0.28 は切替26%と 4HP、meal 0.52 も切替50%と 4HP（Easy）しか離れていない）。
        // 保留した分は TickMeal / TickGoto の終了で OnHpChanged() を呼んで即発火＝
        //「全部見なきゃ→期待」「十字火→我に返る」がそれぞれ一拍になる。
        bool gotoHolds = _gotoPhase != 0 || (!_gotoFired && _mealPhase == 0 && HpRatio <= _gotoHp);
        bool mealHolds = _mealPhase != 0 || (!_mealFired && HpRatio <= _mealHp);
        bool holds = gotoHolds || mealHolds;
        if (!holds && _beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
            ApplySpell();
        }
        // フィナーレ発火＝最後のバーの残り50%（finaleRatio = 0.5 / バー本数）。finale_cap（既定0.26）は Min なので
        // 下げる方向にしか効かず、Easy(2本)は式の 25% が採用される＝第4スペル切替(26%)の 2HP 下で発火し、
        // 上の ApplySpell の宣言をほぼ同時に上書きする（Easy は第4スペルを畳んでフィナーレへ直行＝レイ／あかりの Easy と同じ構造）。
        // ワンショットギミックの進行中は保留（holds）＝食事の終了／X着弾の後にフィナーレのカードが出る。
        if (!holds && !_finale && HpRatio <= Mathf.Min(0.5f / Mathf.Max(1, TotalBars), _finaleCap))
        {
            _finale = true;
            GetHud()?.SetBossBarTint(Spells[0].tint); // フィナーレ色（#26）
            GetHud()?.AnnounceSpell("こはる", "@koharu_light", Spells[0].name + "＋" + Spells[1].name, Spells[0].tint);
        }
    }

    // S2-7 の RECLOSE（仮台本 07）。宣言 → 「やめないで……止まったら」 → 取り繕い、の三段を順送り。
    //   「止まったら我に返る」の意味に変わっている（12 流用資産の型はそのまま、文言を組み直し）。
    private static readonly string[] RecloseLines =
    {
        "みんな見てる。……見てるもん。ちゃんとしなきゃ、だめだもん。",
        "やめないで……止まったら——",
        "……なんでもない。楽しいってば。",
    };
    private int _recloseIdx;
    protected override void OnRecloseLine()
    {
        ShowRecloseLine("こはる", RecloseLines[Mathf.Min(_recloseIdx, RecloseLines.Length - 1)]);
        _recloseIdx++;
    }

    protected override void GrantFollower() { }

    protected override void OnCryStart()
    {
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        // 改心が始まる確実な瞬間に「解決音（完）」へ移す＝冷えていた旋律に温かい残響が戻る。
        Audio.Instance?.PlayRedeem(2);
        // 会話を出せない状況（Hud が取れない／台詞が無い）なら会話に入らず即着地させる
        //   ＝送るものが無いのに EndCryNow を待ち続けて Finished が立たない詰まりを断つ。
        if (hud == null || Lines.Length == 0) { EndCryNow(); return; }
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
        // 旧稿の記憶フラッシュ（StageImagery.TriggerMemoryFlash）は呼ばない。案C の改心は回想ではなく
        // 「S2-4 で消えた一行を、本人の前で返す」なので、台所の回想の画は場面と食い違う。
        // 決定打の手前で音を落とす（台本の「ここでBGM停止。無音のまま」）。
        if (text == BgmStopLine) Audio.Instance?.StopMusic(1.2f);
        string portrait = kind switch
        {
            // こはるは通常 koharu_face。絶望行だけ face に蒼白(KPale)を指定して差し替える。
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? KFace : face,
            _ => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face,   // ミナも行ごと表情
        };
        hud.ShowDialog(kind, text, portrait, otherName: "こはる");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
