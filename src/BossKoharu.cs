using Godot;

// BossKoharu : STAGE3「こはる（永遠に夕食を作り続ける台所）」のボス＝穢れの核「むだなわたし」。
// もう帰らない兄を、夕食で呼び戻そうとする死の否認。家事＝祈りが砕けた無力感。
// 怒り（他責）の下にある悲しみへ光を届ける（正典 v3: 兄=少年は物語開始前に事故死。余命設定は非正典）。
// 禁止語「あなたのせいじゃない」は使わない。祈りが届いていたことを伝えて解く。
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

    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";
    // 絶望（兄が消える恐怖に呑まれる）行だけ、こはるの蒼白＝血色を失った立ち絵に差し替える。
    private const string KPale = "res://char/koharu_face_pale.png";

    // 予測攻撃キャスター（通常テレグラフ。「お残し禁止」中は Suppressed で一時停止する）。
    private AreaSpellCaster _caster = null!;

    // ── INI 外出しのバランス値（config/boss_stats.ini [koharu]。読めなければ現行既定値）──
    private double _ringInterval = 1.0, _fanInterval = 1.1, _aimedInterval = 0.7, _spiralInterval = 0.085;
    private int _ringCount = 16, _fanCount = 9, _aimedWing = 1; // wing=way数の片翼（3way→1）
    private float _ringSpeed = 70f, _aimedSpeed = 96f, _spiralSpeed = 88f;

    // ── 「お残し禁止」（HP52%ワンショットスペル）──
    //   通常弾とテレグラフを止めて宣告 → 画面右半分（ボス側）に“料理弾”3列×8＝24発を配膳
    //  （消せる祈り弾 MakeErasable・降下12px/s）。8秒以内に撃って消した分は祈り弾の既存経路で
    //   そのまま報われる（Bullet.OnAreaEntered → GameManager.AddPrayerCleared: +15点・やさしさ+0.02）。
    //   残した弾は時間切れで 0.06s 間隔で順に自機狙いニードル（130px/s）に変わって飛んでくる。
    //   完食（被弾なし・ボムなしで全24発を撃ち消す）＝こはるの動的セリフ＋回復ハート1個
    //  （報酬の流儀はレイの安置リレー完走報酬 BossRei.TickRelayWatch に合わせる。♥上限時はスコアで返す）。
    //   テーマ＝「ぜんぶ食べてね。のこしちゃだめ。」：前へ出て食べるほどお残し（＝後の弾）が減って安全
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
    private static readonly Color MealNeedleTint = new("d6443f"); // 深紅（「のこしちゃだめ」の色）

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
    private float _gotoHp = 0.28f;              // INI: goto_hp（第4スペル切替26%の直前＝終盤入りの合図）
    private const double GotoFirstDelay = 0.7;  // 宣告→第一十字の溜め（通常テレグラフの _fireDelay と同格）
    private const double GotoSecondDelay = 0.8; // 第一十字→第二十字（45°回転）の遅延
    private const float GotoBeamHalf = 7f;      // 十字ビームの半太さ(px)（通常テレグラフのビーム帯 5-8px と同格）
    private const float GotoDiagLen = 460f;     // 斜め一閃の長さ＝対角441pxを覆う（包丁の軌跡と同じ）
    private static readonly Color GotoTint = new("e8945a");  // 琥珀（こはるAOEの危険色＝第一十字）
    private static readonly Color GotoHot = new("ffc06a");
    private static readonly Color GotoXTint = new("d6443f"); // 深紅（包丁の軌跡と同色＝斜め斬りの色＝第二十字）
    private static readonly Color GotoXHot = new("ff8a7a");

    // フィナーレ発動HPの上限（INI: finale_cap）。既定式 0.5/バー本数 が Easy(2本)だと25%となり、
    // 第4スペル（26%〜）の発動域が実質1%しか無かった（QA指摘）。0.18で頭を抑えて Easy でも
    // 26%→18% の第4スペル帯を確保する（Normal以上は式の方が小さく影響なし）。
    private float _finaleCap = 0.18f;

    // HPがこの割合を割るたびに攻撃パターンを変える（独白は浄化のかけあいに集約）。
    private static readonly float[] PatternThresholds = { 0.78f, 0.50f, 0.26f };

    // スペルカード（RefrainHTML Danmaku v3 STAGE3 こはる＝台所・琥珀と深紅の暖色）。
    private static readonly (string name, BulletShape shape, Color tint)[] Spells =
    {
        ("ぜんぶ食べて",         BulletShape.Orb,     new Color("e8a24a")), // 琥珀・台所の灯
        ("のこしちゃだめ",       BulletShape.Diamond, new Color("d6443f")), // 深紅・高速ダイヤ雨
        ("じっとしてて",         BulletShape.Needle,  new Color("e87a3c")), // 橙・十字バースト（動くな）
        ("あたしがちゃんとするから", BulletShape.Rice, new Color("ffa14a")), // 燃え残り・扇の粒弾
    };
    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        SetSpellVisual(s.shape, s.tint);
        GetHud()?.SetBossBarTint(s.tint); // HPバーもスペル色へ（#26 フェーズ移行の可視化）
        GetHud()?.AnnounceSpell("こはる", "@koharu_kitchen", s.name, s.tint);
    }

    // 浄化のかけあい（who: 0=少年 / 1=ミナ / 2=こはる）。
    // 【届け方＝“少年の直接台詞（劇的アイロニー）”で抜く型】（優先度2）。レイ＝中継の記録／あかり＝名前一点、に対し、
    //   この面は決定打をミナ中継ではなく“少年が兄として妹に直接かける言葉”に置く。観客だけが意味を知り、ミナは半分気づく。
    //   中継(5)は最小限（一度だけ）に絞り、少年の生の声を前へ出す＝3戦で届け役・声の主を散らす（同型4拍の反復を断つ核）。
    // 設計書 v2 [P-03] のボス節（ミナの気遣い・少年の取り繕いも含む）。
    // 躁的暴走＝支配的な世話焼き。傷＝兄の死を認めない唯一の力＝完璧にすれば失わない、という呪術（死の否認）。
    private const string SAfraid = "res://char/shonen_afraid.png"; // 承第3段で立てた“消えかけ”を改心でも滲ませる
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "ごはん、できたよ! ぜんぶ食べてね。残したら……許さないんだから。", ""),
        (2, "あ、そこ汚れてる。掃除するから、じっとしてて。動かないで。ね?", ""),
        (2, "だいじょうぶ。あたしがちゃんとするから。お兄ちゃんは、なにもしなくていいの。", ""),
        (1, "……ご主人様? お顔の色が。", ""),
        (0, "……なんでもない。続けるぞ。", SAfraid),                             // 承第3段の弱りが改心にも尾を引く（cocky→afraid）
        (2, "ちゃんと作って、ちゃんと食べさせて、ちゃんと、ちゃんとしてれば——", ""),
        (2, "……ちゃんとしてれば、お兄ちゃん、いなくならないでしょ? ねえ、いなくならないよね……?", KPale), // 絶望＝蒼白
        (5, "——怒りの下の悲しみを、ちゃんと、悲しんでいい。", ""),                // 中継はここ一度だけ（最小限）
        (2, "あたしのごはんじゃ、お兄ちゃんは帰ってこない……! じゃあ、なんの、意味が……!", KPale),     // 絶望＝蒼白（否認が一瞬だけ割れる）
        // ↓ 決定打＝少年“本人”の直接の声（中継でなく who=0）。兄が妹に、を観客だけが知る＝劇的アイロニーで抜く。
        (0, "……お兄さんが最後の日まで、あったかいままでいられたのは。きみの食卓が、あったからだよ。", SGentle),
        (0, "祈りは、ちゃんと——届いてた。", SGentle),                            // 中継を挟まず少年の生声で言い切る（届け方の核）
        (2, "……ほんとに? ……ちゃんと、食べて、くれるかな。今日は。", ""),       // 日常語＝小さな願い
        (0, "……ああ。残さず、食べるよ。", SGentle),                              // 兄が、妹に（観客だけが意味を知る）
        (0, "明日のぶんは——……いや。たくさん、作ってやってくれ。きみは、えらいよ。", SAfraid), // “明日のぶんはいい”を呑み込む＝消えかけ（afraid）
        (1, "ご主人様。いまの……どなたに、おっしゃったんですか。", ""),         // ミナが半分気づく（Final/伏線へ・StageKoharu Clear が参照）
        // ② こはるの否認「ちゃんとすれば、いなくならない」と、ミナ自身の否認が同型。
        //    説明せず、ミナが半秒だけ自分の否認に触れる“間”の一行（Final受容への助走）。
        (1, "……ちゃんとしてれば、いなくならない。……ええ。わたくしも、そう思っていたいです。", "res://char/mina_worried.png"),
        (0, "…………帰ろう、ミナ。", SCocky),                                      // 再仮面
    };

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [koharu]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("koharu", "points", 1800);
        BodyRadius = BossTuning.F("koharu", "body_radius", 9f);
        PanelCount = BossTuning.I("koharu", "panel_count", 5); // 「むだだよ」等の言葉（黒い吹き出し）
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
        _finaleCap = BossTuning.F("koharu", "finale_cap", 0.18f);

        PreTexPath = "res://char/enemy_koharu_pre.png";   // 穢れ・病んだ核
        // 改心の三段：穢れ(pre)→泣き(cry＝黒い炎が熾火へ鎮まり大粒の涙)→笑顔(post)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post（笑顔）へ着地する。
        CryTexPath = "res://char/enemy_koharu_cry.png";
        PostTexPath = "res://char/enemy_koharu_post.png";
        BodyDisplayH = 52f;
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    public override void _Ready()
    {
        base._Ready();
        // ボス登場＝道中BGMからこはる固有テーマへクロスフェード（温かい旋律が冷えて減衰＝未完）。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossKoharu);
        // 徘徊：画面上部のボスゾーンに収め、イージング＋ホバーで漂わせる（速度はINI: roam_speed）。
        _mover.Configure(new Vector2(200f, 70f), 90f, 28f, BossTuning.F("koharu", "roam_speed", RoamSpeed));
        GetHud()?.ShowBossBar("とまれないわたし", "@koharu");
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        ApplySpell();

        _caster = new AreaSpellCaster();
        _caster.Configure("koharu", GetParent());
        AddChild(_caster);
    }

    protected override void UpdateMovement(double delta)
    {
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
            case 0: if (_fireT >= Di(_ringInterval)) { _fireT = 0; Ring(pool, Dn(_ringCount), _ringSpeed); } break;
            case 1: if (_fireT >= Di(_fanInterval)) { _fireT = 0; FanDown(pool); } break;
            case 2: if (_fireT >= Di(_aimedInterval)) { _fireT = 0; Aimed(pool); } break;
            default: if (_fireT >= Di(_spiralInterval)) { _fireT = 0; Spiral(pool); } break;
        }
    }

    // フィナーレ（HP2割以下）：「むだだよ」(琥珀の円弾リング)＋「怒り（他責）」(深紅の菱形雨)を同時展開。
    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.95)) { _fireT = 0; SetSpellVisual(Spells[0].shape, Spells[0].tint); Ring(pool, Dn(16), 70f); }
        if (_fireT2 >= Di(0.85)) { _fireT2 = 0; SetSpellVisual(Spells[1].shape, Spells[1].tint); FanDown(pool); }
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

    // 「祈り弾」ギミック（#12 機構側／#20）：下方向の扇＝食卓に落ちる祈りは、自機弾で“受け止め”られる。
    // 消すと双方消滅＋やさしさ微加算（GameManager.AddPrayerCleared）。自機・フォロワーの弾列が受け皿になる。
    // FanDown はスペル「のこしちゃだめ」(pattern1)とフィナーレでしか撃たない＝スペル限定が自然に成立。
    private void FanDown(BulletPool pool)
    {
        int k = Dn(_fanCount);
        for (int i = 0; i < k; i++)
        {
            float t = (float)i / (k - 1) - 0.5f;
            float a = Mathf.Pi / 2f + t * Mathf.DegToRad(78f);
            var b = FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * EnemyBulletSpeed, 3.4f);
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
                GetHud()?.ShowBossLine("こはる", "……のこしたね。のこしちゃだめって、いったのに。", UiKit.Kegare, 2.0);
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
                    pool.Spawn(at, d * _mealNeedleSpeed, true, 3.0f, 1, BulletShape.Needle, MealNeedleTint);
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
        SetSpellVisual(Spells[0].shape, Spells[0].tint); // 料理弾＝琥珀の円弾（「ぜんぶ食べて」の色）
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

    // まだ食卓に残っている料理弾の数。プール再利用対策：参照が生きたまま別の弾に転用されても、
    // Activate が Erasable を必ずリセットするため「Active かつ Erasable」だけが本物の料理弾
    //（ギミック中は FanDown が撃たない＝他に Erasable を立てる者がいない）。
    private int CountMealAlive()
    {
        int n = 0;
        foreach (var b in _meal)
            if (IsInstanceValid(b) && b.Active && b.Erasable) n++;
        return n;
    }

    // ギミック終了。fullEat=食卓が時間内に空になった（ボム薙ぎ払い等を含むため、報酬判定は中で絞る）。
    private void FinishMeal(bool fullEat)
    {
        _mealPhase = 0;
        _meal.Clear();
        _mealLeft.Clear();
        if (_caster != null && _gotoPhase == 0) _caster.Suppressed = false; // 十字火が進行中なら解除しない（保険）
        var cur = Spells[_pattern % Spells.Length];
        SetSpellVisual(cur.shape, cur.tint); // 弾形・色を通常スペルへ戻す（宣告カードは再掲しない）
        if (!fullEat) return;
        if (GetTree().GetFirstNodeInGroup("player") is not Player pl) return;
        // “食べた”証明＝被弾なし・ボムなし（ボムで消しても弾は消えるが「食べて」いない＝褒めない。
        // 撃ち消しぶんの AddPrayerCleared は入っているので無報酬にはならない）。
        bool noHit = _mealStartLives >= 0 && pl.Lives >= _mealStartLives;
        bool noBomb = _mealStartBombs >= 0 && (GetNodeOrNull<GameManager>("/root/Game")?.Bombs ?? _mealStartBombs) >= _mealStartBombs;
        if (!noHit || !noBomb) return;
        GetHud()?.ShowBossLine("こはる", "……ぜんぶ、食べてくれた。えらいね。……えらいね。", UiKit.Kegare, 2.4);
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
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _aimedSpeed, 3.4f);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(12f);
        for (int s = 0; s < 2; s++)
        {
            float a = _ringOff + Mathf.Pi * s;
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
        // 「お残し禁止」：HP52%（INI: meal_hp）を割った瞬間に一度だけ（パターン切替50%の直前＝中盤の山）。
        if (!_mealFired && HpRatio <= _mealHp)
        {
            _mealFired = true;
            _mealPhase = 1; _mealT = 0;
            _mealStartLives = (GetTree().GetFirstNodeInGroup("player") as Player)?.Lives ?? -1;
            _mealStartBombs = GetNodeOrNull<GameManager>("/root/Game")?.Bombs ?? -1;
            if (_caster != null) _caster.Suppressed = true; // 通常テレグラフも保留（配膳の上に予兆を重ねない）
            GetHud()?.AnnounceSpell("こはる", "@koharu_kitchen", "お残し禁止", Spells[0].tint);
            GetHud()?.ShowBossLine("こはる", "ごはんできたよ! ぜんぶ食べてね。のこしちゃ、だめ。", UiKit.Kegare, 2.2);
        }
        // 「五徳の十字火」：HP28%（INI: goto_hp）を割った瞬間に一度だけ（第4スペル切替26%の直前＝終盤入りの合図）。
        // お残し禁止の進行中は持ち越し（次の OnHpChanged で発火）＝ワンショットギミック同士を重ねない。
        if (!_gotoFired && _mealPhase == 0 && HpRatio <= _gotoHp)
        {
            _gotoFired = true;
            _gotoPhase = 1; _gotoT = 0;
            if (_caster != null) _caster.Suppressed = true; // 通常テレグラフも保留（十字に集中させる）
            GetHud()?.AnnounceSpell("こはる", "@koharu_kitchen", "五徳の十字火", GotoTint);
            GetHud()?.ShowBossLine("こはる", "うごかないでね。……火、つけるから。", UiKit.Kegare, 2.0);
        }
        // フィナーレ発火＝最後のバーの残り50%（finaleRatio = 0.5 / バー本数）。ただし finale_cap（既定0.18）で
        // 頭を抑える＝Easy(2本)の25%発火で第4スペル(26%〜)の発動域が1%しか無くなる問題の是正（QA指摘）。
        if (!_finale && HpRatio <= Mathf.Min(0.5f / Mathf.Max(1, TotalBars), _finaleCap))
        {
            _finale = true;
            GetHud()?.SetBossBarTint(Spells[0].tint); // フィナーレ色（#26）
            GetHud()?.AnnounceSpell("こはる", "@koharu_kitchen", Spells[0].name + "＋" + Spells[1].name, Spells[0].tint);
        }
    }

    // RECLOSE のキャラ別弱気セリフ（序盤=支配→終盤=絶望）。
    private static readonly string[] RecloseLines =
    {
        "まだだよ。ちゃんとしなきゃ、だめなの。",
        "じっとしてて。手を止めたら、終わっちゃう。",
        "やめないで……止まったら、お兄ちゃんが……",
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
        if (hud != null) hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        // S3 画の反転：改心成立（cry→post）で、空席の箸に湯気が戻り始める
        //（帰還の会話の背景でゆっくり回復。指ししない＝気づく余白）。
        (GetTree().GetFirstNodeInGroup("imagery") as StageImagery)?.TriggerReversal();
        Finished = true;
    }

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
            // こはるは通常 koharu_face。絶望行だけ face に蒼白(pale)を指定して差し替える。
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? "res://char/koharu_face.png" : face,
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, // ミナも行ごと表情（worried 等）
            _ => "res://char/mina_face.png", // 中継ほか
        };
        hud.ShowDialog(kind, text, portrait, otherName: "こはる");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
