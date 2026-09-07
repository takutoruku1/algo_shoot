using Godot;
using System;

// BossMover : ボスの移動を1か所に集約した再利用クラス。
//
// ■ 旧実装（〜2026-09-06）の何が悪かったか
//   広いターゲット箱の中からランダムに点を選んで飛ぶだけ＝「ふよふよして反転を繰り返す」。
//   移動が攻撃と何も結びついていないので、ボスが何をしているのか読めない。左右の反転は
//   速度のX符号でその場で切り替わるだけなので、巡航のたびに向きがパタパタ入れ替わる。
//
// ■ 作り替えの方針（吉田: 予備動作→本動作→余韻／squash&stretch／被弾の一拍）
//   1) 状態機械で動く。Idle（その場で呼吸・位置は動かさない）→ Windup（構え＝攻撃の
//      0.3〜0.5秒前に攻撃の種類に応じて引く/浮く/沈む）→ Action（本動作＝前へ出る/踏ん張る/
//      横へ滑る）→ Recover（余韻＝0.3秒で元の高さへ）→ Idle。被弾は Hit（のけぞって戻る）。
//   2) ランダム巡航をやめ、スペルごとに「立ち位置（Stance）」を持つ。リング系は中央に据わり、
//      自機狙いは自機の x を追って横に滑り、壁系は端に寄る。位置の変更は攻撃の合間だけで、
//      「向かう(Travel)→着く→一拍(Settle)」の三段を踏む。
//   3) 反転を減らす。向きは自機が反対側へ FlipDx px 以上・FlipHold 秒続いたときだけ変え、
//      変えるときは FlipSquashDur 秒の縮み（squash）を挟む。
//   4) ボスごとの性格を Configure(name) の分岐と ini（cruise_speed / accel_time / stance_*）で出す。
//
// ■ 当たり判定との約束（吉田 P9）
//   当たり判定そのものは持たない。Step() が返すのは本体位置＝当たり判定の中心で、ここは
//   「ゆっくり素直に」しか動かさない（構え/本動作の変位も 1フレームで飛ばず時定数で寄せる）。
//   激しい伸縮・傾き・震えは視覚専用（VisualOffset / Lean / SquashScale）に寄せる。
public sealed class BossMover
{
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // ── 攻撃の種類。呼び出し側（各 Boss の FirePattern/ApplySpell）が「次に何をするか」を渡す。
    //    構えの向き（引く/浮く/沈む）と立ち位置（中央/自機追い/端）の両方をこれで決める。
    public enum Attack
    {
        Ring,    // 全方位リング・花型 ── 中央に据わって撒く。構えは「沈む」（力をためて開く）
        Aimed,   // 自機狙い・扇 ────── 自機の x を追って横に滑る。構えは「後ろへ引く」
        Wall,    // 壁・弾幕の帯・スパイラル ── 端に寄る。構えは「上へ浮く」
        Spell,   // 宣告付きの大技（AOE/イベント）── 中央高めで動かない。構えは「浮いて止まる」
    }

    // ── 状態。
    public enum St { Idle, Travel, Settle, Windup, Action, Recover, Hit }

    // ボスゾーンの基準（既定値。実際は各ボスの Configure が Field 基準で上書きする）。
    private Vector2 _zoneCenter = new Vector2(Field.BossCenterX, 70f);
    private float _zoneHalfW = Field.BossZoneHalfW;
    private float _zoneHalfH = 28f;

    // ── 巡航・追従パラメータ（ini: cruise_speed / accel_time）。
    private float _cruiseSpeed = 40f;
    private float _accelTime = 0.55f;
    private float _arriveDist = 6f;

    // ── 立ち位置（ini: stance_*）。ボスごとの性格はここと Configure(name) の分岐で出す。
    private float _stanceCenterX = Field.BossCenterX;  // リング系で据わる x
    private float _stanceY = 70f;         // 基準の高さ
    private float _stanceEdgeX = 62f;     // Wall で端へ寄るときの中心からの距離
    private float _stanceTrackW = 70f;    // Aimed で自機を追える横幅（中心±この値まで）
    private float _stanceTrackGain = 1f;  // 自機 x をどれだけ鏡写しに追うか（ミナ=1.0 / レイ=0.25）
    private float _stanceRiseY = -8f;     // Wall/Spell で浮く量（負＝上）

    // ── 構え・本動作・余韻の尺と量。
    private float _windupDur = 0.38f;     // 構えの尺(s)。攻撃の 0.3〜0.5 秒前に入る
    private float _actionDur = 0.22f;     // 本動作の尺(s)
    private float _recoverDur = 0.30f;    // 余韻の尺(s)＝元の高さへ戻る
    private float _windupBack = 7f;       // 構えで引く/浮く/沈む距離(px)
    private float _actionPush = 5f;       // 本動作で前へ出る距離(px)
    private float _settleDur = 0.35f;     // 立ち位置に着いてからの「一拍」(s)

    // ── ホバー（縦の微揺れ）。本体位置に薄く混ぜる＝静止中も漂って見える。
    //    あかりは「座ったまま滑る＝上下に揺れない」ので 0 にできる。
    private float _hoverAmp = 3.5f;
    private float _hoverFreq = 0.9f;

    // ── 視覚専用（当たり判定に入れない）。
    private float _breatheAmp = 1.6f;
    private float _swayAmp = 1.4f;
    private float _leanMax = 0.16f;

    // ── 反転抑制。自機が反対側へ FlipDx px 以上・FlipHold 秒続いたときだけ向きを変える。
    private const float FlipDx = 40f;
    private const float FlipHold = 0.6f;
    private const float FlipSquashDur = 0.15f;

    // ── 内部状態。
    private St _st = St.Idle;
    private double _stT;                  // 現在状態の経過(s)
    private Attack _next = Attack.Ring;   // 次（または今）の攻撃の種類
    private Vector2 _target;              // 立ち位置の目標（Travel の行き先）
    private Vector2 _velocity;
    private Vector2 _poseOffset;          // 構え/本動作/被弾の変位（本体位置に足す。時定数で寄せる）
    private Vector2 _poseWant;            // その目標
    private double _hoverPhase;
    private double _breathePhase;
    private float _lean;
    private double _flipWant;             // 反対側に居続けている時間(s)
    private bool _flipWantLeft;
    private double _flipSquashT;          // 反転 squash の残り(s)
    private float _playerX = Field.CenterX; // 直近の自機 x（呼び出し側が SetPlayerX で渡す）
    private bool _hasPlayerX;
    private int _flipCount;               // 反転回数（検証ログ用）

    // ── 公開：視覚用の付加情報（呼び出し側が ApplyBossMotion に渡す）。
    public Vector2 VisualOffset { get; private set; }
    public float Lean => _lean;
    public bool FacingLeft { get; private set; } = true;
    public St State => _st;
    public int FlipCount => _flipCount;

    // 反転 squash の見た目倍率（横に縮み縦に伸びる）。呼び出し側が使わなくても害はない。
    public Vector2 SquashScale
    {
        get
        {
            if (_flipSquashT <= 0.0) return Vector2.One;
            float k = (float)(_flipSquashT / FlipSquashDur);   // 1→0
            float s = Mathf.Sin(k * Mathf.Pi);                  // 0→1→0（山）
            return new Vector2(1f - 0.12f * s, 1f + 0.08f * s);
        }
    }

    public BossMover()
    {
        _rng.Randomize();
        _breathePhase = _rng.RandfRange(0f, Mathf.Tau);
        _hoverPhase = _rng.RandfRange(0f, Mathf.Tau);
    }

    // ── 徘徊ゾーンと速度だけを差し替える（退場・帰還など「ここへ行け」を直接指示する用途）。
    //    2026-09-07: 旧 Configure(Vector2,...) オーバーロードを置き換えた。旧版は hover_amp・
    //    lean_max・stance_* を既定引数で**上書き**していたため、あかりの「雨の帰り道」が
    //    退場→帰還で3回これを呼ぶたび（BossAkari.cs の TickCorridor / StartCorridor）
    //    ini で入れた性格（[akari] hover_amp=0 ほか）が消え、以降は汎用の hover 3.5 に戻っていた
    //    ＝ユーザー実機指摘「ふよふよする」の、あかり側の実際の原因（実測でも hover=3.5 が出た）。
    //    ここではゾーンと巡航速度だけを触り、性格（ini 由来の値）は保つ。
    public void MoveZoneTo(Vector2 zoneCenter, float zoneHalfW, float zoneHalfH, float cruiseSpeed)
    {
        _zoneCenter = zoneCenter;
        _zoneHalfW = zoneHalfW;
        _zoneHalfH = zoneHalfH;
        _cruiseSpeed = cruiseSpeed;

        // 立ち位置の基準もゾーンへ寄せる（退場のように中心が飛ぶ場合、即その先を向く）。
        // 端へ寄る量・自機を追える幅はゾーン幅に対する比で取り直す＝狭いゾーンで飛び出さない。
        _stanceCenterX = zoneCenter.X;
        _stanceY = zoneCenter.Y;
        _stanceEdgeX = Mathf.Min(_stanceEdgeX, zoneHalfW * 0.7f);
        _stanceTrackW = Mathf.Min(_stanceTrackW, zoneHalfW * 0.8f);

        RetargetStance();
    }

    // ── ボスごとの性格つき設定。ini（config/boss_stats.ini の各ボス節）から読む。
    //    section が ini のセクション名（"akari"/"koharu"/"rei"/"mina"/"hikage"/"cameo"）。
    //    ini にキーが無ければ、ここに書いた「性格の既定値」で動く（BossTuning がフォールバックする）。
    public void Configure(string section, Vector2 zoneCenter, float zoneHalfW, float zoneHalfH)
    {
        _zoneCenter = zoneCenter;
        _zoneHalfW = zoneHalfW;
        _zoneHalfH = zoneHalfH;

        // 性格の既定値（ini が無くてもこの通りに動く）。
        //   あかり : 座ったまま滑る。重い＝加速が遅く、上下に揺れない（hover 0）。
        //   こはる : 軽く小刻み。攻撃前に一瞬止まる（windup を長め・action を短く鋭く）。
        //   レイ   : 配信の枠から出ない。横移動をほぼ捨て、傾きだけで表情を作る。
        //   ミナ   : 自機を鏡のように追う（track_gain 1.0）。速い。
        float cruise, accel, hoverA, hoverF, leanM, edgeX, trackW, trackGain, riseY, windup, action;
        switch (section)
        {
            case "akari":
                cruise = 34f; accel = 0.85f; hoverA = 0f;   hoverF = 0.6f; leanM = 0.10f;
                edgeX = 58f;  trackW = 62f;  trackGain = 0.7f; riseY = -5f;
                windup = 0.44f; action = 0.26f; break;
            case "koharu":
                cruise = 46f; accel = 0.34f; hoverA = 0f  ; hoverF = 1.25f; leanM = 0.18f;
                edgeX = 62f;  trackW = 72f;  trackGain = 0.85f; riseY = -9f;
                windup = 0.40f; action = 0.16f; break;
            case "rei":
                cruise = 30f; accel = 0.6f;  hoverA = 0f  ; hoverF = 0.8f; leanM = 0.24f;
                edgeX = 20f;  trackW = 22f;  trackGain = 0.25f; riseY = -6f;
                windup = 0.36f; action = 0.20f; break;
            case "mina":
                cruise = 52f; accel = 0.40f; hoverA = 0f  ; hoverF = 1.0f; leanM = 0.17f;
                edgeX = 66f;  trackW = 84f;  trackGain = 1.0f; riseY = -8f;
                windup = 0.32f; action = 0.20f; break;

            // ── 道中の中ボス（CameoBoss）。戦いが短いので本戦より単純にする。
            //    共通の縛り: hover_amp = 0（常時の上下揺れ＝「ふよふよ」を全員やめる。待機は Idle の
            //    呼吸＝視覚専用の VisualOffset だけで見せ、本体位置は動かさない）。
            //    移動は攻撃の合間だけ＝撃つ弾の種類が変わったときにだけ立ち位置が変わる。
            //    性格は本人に沿って三者三様に分ける（本戦ボスと同じ方向で、振れ幅だけ小さく）。
            case "cameo_akari": // 重い。座ったまま滑る＝加速が遅く、寄る量も控えめ
                cruise = 34f; accel = 0.80f; hoverA = 0f; hoverF = 0.6f; leanM = 0.10f;
                edgeX = 46f;  trackW = 50f;  trackGain = 0.6f; riseY = -5f;
                windup = 0.42f; action = 0.26f; break;
            case "cameo_koharu": // 小刻み。軽く速く、構えて一瞬止まってから鋭く出る
                cruise = 48f; accel = 0.32f; hoverA = 0f; hoverF = 1.2f; leanM = 0.18f;
                edgeX = 52f;  trackW = 62f;  trackGain = 0.85f; riseY = -8f;
                windup = 0.38f; action = 0.16f; break;
            case "cameo_rei": // 枠から出ない。横移動は小さく、傾きで演じる（本戦のレイと同じ方向）
                cruise = 38f; accel = 0.55f; hoverA = 0f; hoverF = 0.8f; leanM = 0.24f;
                edgeX = 30f;  trackW = 34f;  trackGain = 0.45f; riseY = -6f;
                windup = 0.34f; action = 0.20f; break;

            default: // hikage ほか
                cruise = 42f; accel = 0.5f;  hoverA = 3.2f; hoverF = 0.95f; leanM = 0.16f;
                edgeX = 60f;  trackW = 70f;  trackGain = 0.7f; riseY = -7f;
                windup = 0.38f; action = 0.22f; break;
        }

        // ini 上書き（キーが無ければ上の性格既定値のまま）。
        _cruiseSpeed = BossTuning.F(section, "cruise_speed", BossTuning.F(section, "roam_speed", cruise));
        _accelTime = Mathf.Max(0.05f, BossTuning.F(section, "accel_time", accel));
        _hoverAmp = BossTuning.F(section, "hover_amp", hoverA);
        _hoverFreq = BossTuning.F(section, "hover_freq", hoverF);
        _leanMax = BossTuning.F(section, "lean_max", leanM);
        _stanceCenterX = BossTuning.F(section, "stance_center_x", zoneCenter.X);
        _stanceY = BossTuning.F(section, "stance_y", zoneCenter.Y);
        _stanceEdgeX = BossTuning.F(section, "stance_edge_x", edgeX);
        _stanceTrackW = BossTuning.F(section, "stance_track_w", trackW);
        _stanceTrackGain = BossTuning.F(section, "stance_track_gain", trackGain);
        _stanceRiseY = BossTuning.F(section, "stance_rise_y", riseY);
        _windupDur = BossTuning.F(section, "stance_windup", windup);
        _actionDur = BossTuning.F(section, "stance_action", action);
        _recoverDur = BossTuning.F(section, "stance_recover", 0.30f);
        _windupBack = BossTuning.F(section, "stance_back", 7f);
        _actionPush = BossTuning.F(section, "stance_push", 5f);
        _settleDur = BossTuning.F(section, "stance_settle", 0.35f);

        RetargetStance();
    }

    // 自機の x を毎フレーム渡す（Aimed の横滑りと、反転判定に使う）。
    public void SetPlayerX(float x) { _playerX = x; _hasPlayerX = true; }

    // ── 次の攻撃の種類を宣言する。ApplySpell（スペル切替）から呼ぶ。
    //    立ち位置がこれで変わる＝「攻撃の合間に、次の攻撃のための位置へ移る」。
    public void SetNextAttack(Attack a)
    {
        if (_next == a) return;
        _next = a;
        // 移動中・待機中なら即座に新しい立ち位置へ向き直す。攻撃の一拍の最中は割り込まない
        //（構え〜余韻が終わってから Idle 経由で向かう＝動作が途中で崩れない）。
        if (_st == St.Idle || _st == St.Travel || _st == St.Settle) BeginTravel();
    }

    // ── 攻撃の一拍を始める。各 Boss の FirePattern が実際に撃つ「その手前」で呼ぶのが理想だが、
    //    撃つのと同時に呼んでも成立するよう Windup→Action は見た目の予備動作として走らせる
    //    （弾の発射タイミングそのものは呼び出し側が握ったまま＝弾幕の難易度を変えない）。
    public void OnAttack(Attack a)
    {
        _next = a;
        if (_st == St.Windup || _st == St.Action) return; // 連射中は一拍を伸ばさない（震えない）
        _st = St.Windup; _stT = 0.0;
    }

    // ── 撃つたびに呼ぶ「立ち位置の宣言＋一拍」（中ボスの CameoBoss.Declare と同じ役割）。
    //    SetNextAttack は種類が変わった時しか動かないため、1つのスペルで1種類しか撃たない本戦ボスでは
    //    スペルが変わる（＝HP 閾値の 3 回）まで立ち位置が固定されていた。ここは**種類が同じでも**
    //    持ち場（_perch）を次へ送って向かい直す＝撃つたびに立ち位置が変わる。
    //    弾の数・速度・間隔には一切触らない（撃つのは呼び出し側のまま）。
    //    攻撃の一拍（Windup/Action）の最中は割り込まない＝動作が途中で崩れない。
    public void DeclareAttack(Attack a)
    {
        bool changed = _next != a;
        _next = a;
        // 種類が変わったら即その立ち位置へ向き直す（従来の SetNextAttack と同じ）。
        // 種類が同じなら「次の持ち場へ送る」予約だけ立て、実際に向かうのは攻撃の一拍を終えた
        // Recover の後（RetargetIfDrifted）＝撃っている最中に本体が滑って弾幕の出所がぶれない。
        if (changed)
        {
            if (_st == St.Idle || _st == St.Travel || _st == St.Settle) BeginTravel();
        }
        else _perchPending = true;
        OnAttack(a);
    }
    private bool _perchPending;   // 次の一拍明けに持ち場を1つ送る

    // ── 被弾の一拍（小さくのけぞって戻る）。呼び出し側の被弾フックから。
    //    無防備窓では弾が 0.05 秒間隔で刺さり続ける（Enemy.BodyHitCd）ので、「Hit 中は弾く」だけだと
    //    のけぞりが延々と再点火して窓のあいだ立ち位置へ戻れなくなる。専用のクールダウンで間引き、
    //    のけぞりは 0.34 秒に1回までに絞る＝連射されても震え続けない。
    private double _hitCd;
    private const double HitCd = 0.34;
    public void OnHit(Vector2 fromDir)
    {
        if (_hitCd > 0.0) return;
        _hitCd = HitCd;
        _st = St.Hit; _stT = 0.0;
        Vector2 d = fromDir.LengthSquared() > 0.001f ? fromDir.Normalized() : Vector2.Up;
        _poseWant = d * 4f; // のけぞりは 4px まで（当たり判定が暴れない範囲）
    }

    // 次フレームの本体位置（= 当たり判定の中心 GlobalPosition に入れる値）を返す。
    public Vector2 Step(Vector2 currentPos, double delta)
    {
        float dt = (float)delta;
        if (dt <= 0f) return currentPos;

        _stT += delta;
        if (_hitCd > 0.0) _hitCd -= delta;
        TickState(currentPos);

        // ── 立ち位置へ向かう推進。Travel 以外はその場（Idle/Settle/構え〜余韻は位置を保つ）。
        //    Aimed の横滑りだけは例外で、待機中も自機の x をゆっくり追う（＝狙っている感）。
        Vector2 desiredVel = Vector2.Zero;
        if (_st == St.Travel)
        {
            Vector2 to = _target - currentPos;
            float dist = to.Length();
            if (dist > _arriveDist)
            {
                float speed = _cruiseSpeed * Mathf.Clamp(dist / 36f, 0.3f, 1f);
                desiredVel = (to / dist) * speed;
            }
        }
        else if (_next == Attack.Aimed && _st != St.Hit)
        {
            // 自機狙いの間は「自機の x を追って横に滑る」。上下は動かさない。
            float wantX = TrackedX();
            float dx = wantX - currentPos.X;
            if (Mathf.Abs(dx) > _arriveDist)
                desiredVel = new Vector2(Mathf.Sign(dx) * _cruiseSpeed * Mathf.Clamp(Mathf.Abs(dx) / 40f, 0.25f, 1f), 0f);
        }

        // 速度の指数追従（SmoothDamp相当）。accel_time が大きいほど重い＝あかり。
        float k = 1f - Mathf.Exp(-dt / _accelTime);
        _velocity = _velocity.Lerp(desiredVel, k);

        Vector2 nextPos = currentPos + _velocity * dt;

        // ── 構え/本動作/被弾の変位。1フレームで飛ばさず時定数 0.12s で寄せる＝判定が跳ねない。
        Vector2 prevPose = _poseOffset;
        _poseOffset = _poseOffset.Lerp(_poseWant, 1f - Mathf.Exp(-dt / 0.12f));
        nextPos += _poseOffset - prevPose;

        // ── ホバー（ゆっくりした縦サイン）。差分だけ本体位置へ薄く混ぜる。
        double prevHover = Math.Sin(_hoverPhase) * _hoverAmp;
        _hoverPhase += _hoverFreq * Mathf.Tau * delta;
        double curHover = Math.Sin(_hoverPhase) * _hoverAmp;
        nextPos.Y += (float)(curHover - prevHover);

        // ── 視覚専用の付加情報。
        //    Idle は「その場で小さく呼吸」＝ここの breathe だけが動く（位置は動かさない）。
        _breathePhase += (_st == St.Idle ? 1.1 : 1.6) * delta;
        float breathe = (float)Math.Sin(_breathePhase) * _breatheAmp;
        float sway = (float)Math.Sin(_hoverPhase * 0.7) * _swayAmp;
        VisualOffset = new Vector2(sway, -breathe);

        // 傾き：横速度に比例＋構えの一拍で少し余分に傾ける（レイは lean_max が大きい＝傾きで演じる）。
        float targetLean = Mathf.Clamp(_velocity.X / Mathf.Max(1f, _cruiseSpeed), -1f, 1f) * _leanMax;
        if (_st == St.Windup) targetLean *= 1.4f;
        _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-dt / 0.25f));

        TickFacing(currentPos, delta);

        return nextPos;
    }

    // ── 状態機械の進行。位置そのものは Step 側が動かし、ここは状態遷移と _poseWant を決める。
    private void TickState(Vector2 currentPos)
    {
        switch (_st)
        {
            case St.Idle:
                _poseWant = Vector2.Zero;
                break;

            case St.Travel:
                _poseWant = Vector2.Zero;
                // 着いたら一拍（Settle）。「向かう→着く→一拍」の三段目。
                if ((_target - currentPos).Length() <= _arriveDist) { _st = St.Settle; _stT = 0.0; }
                break;

            case St.Settle:
                _poseWant = Vector2.Zero;
                if (_stT >= _settleDur) { _st = St.Idle; _stT = 0.0; }
                break;

            case St.Windup:
                // 構え＝攻撃の種類に応じた予備動作。
                //   Aimed : 後ろへ引く（自機と反対＝上へ引く）／ Ring : 沈む（力をためて開く）
                //   Wall/Spell : 上へ浮く
                _poseWant = _next switch
                {
                    Attack.Aimed => new Vector2(0f, -_windupBack),
                    Attack.Ring => new Vector2(0f, _windupBack * 0.7f),
                    _ => new Vector2(0f, -_windupBack * 1.1f),
                };
                if (_stT >= _windupDur) { _st = St.Action; _stT = 0.0; }
                break;

            case St.Action:
                // 本動作＝解放。Aimed は前（下）へ出る／Ring はその場で踏ん張る（変位を戻すだけ）／
                // Wall は横へ滑る（今向いている側へ）。
                _poseWant = _next switch
                {
                    Attack.Aimed => new Vector2(0f, _actionPush),
                    Attack.Ring => Vector2.Zero,
                    Attack.Wall => new Vector2(FacingLeft ? -_actionPush : _actionPush, 0f),
                    _ => Vector2.Zero,
                };
                if (_stT >= _actionDur) { _st = St.Recover; _stT = 0.0; }
                break;

            case St.Recover:
                // 余韻＝元の高さへ戻る。戻り切ったら立ち位置を取り直して、必要なら向かい直す。
                //   Wall は「自機と反対の端」なので自機が回り込めば行き先が変わるし、被弾ののけぞりや
                //   Aimed の横滑りで立ち位置から流されていることもある。ここで確かめないと、
                //   同じ種類の攻撃が続く間ずっと同じ場所に留まる（＝旧実装の「その場から動かない」）。
                _poseWant = Vector2.Zero;
                if (_stT >= _recoverDur) { RetargetIfDrifted(currentPos); }
                break;

            case St.Hit:
                // のけぞり→戻る。半分過ぎたら戻し始める。
                if (_stT >= 0.09) _poseWant = Vector2.Zero;
                if (_stT >= 0.26) { RetargetIfDrifted(currentPos); }
                break;
        }
    }

    // 次の攻撃の立ち位置へ向かい始める。
    private void BeginTravel()
    {
        RetargetStance();
        _st = St.Travel; _stT = 0.0;
    }

    // 一拍が終わった直後の後始末。立ち位置を取り直し、そこから離れていれば向かい直す（Travel）。
    // 離れていなければ素直に待機（Idle）＝その場で呼吸する。
    // DeclareAttack が「同じ種類の連投」で予約した持ち場送り（_perchPending）はここで消化する
    // ＝撃ち終えてから次の持ち場へ歩き出す（撃っている最中に本体が滑らない）。
    private void RetargetIfDrifted(Vector2 currentPos)
    {
        if (_perchPending) { _perchPending = false; _perchStep++; }
        RetargetStance();
        _stT = 0.0;
        _st = (_target - currentPos).Length() > _arriveDist ? St.Travel : St.Idle;
    }

    // ── スペルごとの立ち位置。
    //    Ring  : 中央に据わる（撒くので真ん中が一番読みやすい）
    //    Aimed : 自機の x を追う（Step 側で常時追うので、目標は現在の追従先）
    //    Wall  : 端に寄る（帯を張るので端から）。寄る側は自機と反対＝画面が広く使える方
    //    Spell : 中央の高め（宣告を見せる。動かない）
    //
    //  Ring/Spell は「中央に据わる」ので、同じスペルが続くあいだ目標が1点に固定される。本戦ボスは
    //  1つのスペルにつき1種類の弾しか撃たない（中ボスのように2種を交互に撃たない）ため、それだと
    //  スペルが変わるまで立ち位置が動かない＝ユーザー実機指摘「本戦のボスが動いていない」。
    //  そこで据わる系には「据わり位置そのものを左右に振る」オフセット（_perch）を持たせ、
    //  撃つたび（DeclareAttack）に次の持ち場へ送る。振り幅は徘徊ゾーンの内側に収める。
    private void RetargetStance()
    {
        float x = _next switch
        {
            Attack.Ring => _stanceCenterX + PerchOffset(),
            Attack.Aimed => TrackedX(),
            Attack.Wall => _stanceCenterX + (PlayerOnLeft() ? _stanceEdgeX : -_stanceEdgeX),
            _ => _stanceCenterX + PerchOffset() * 0.5f,   // Spell は宣告を見せる＝振り幅を半分に
        };
        float y = _next switch
        {
            Attack.Ring => _stanceY,
            Attack.Aimed => _stanceY,
            _ => _stanceY + _stanceRiseY,
        };
        _target = new Vector2(
            Mathf.Clamp(x, _zoneCenter.X - _zoneHalfW, _zoneCenter.X + _zoneHalfW),
            Mathf.Clamp(y, _zoneCenter.Y - _zoneHalfH, _zoneCenter.Y + _zoneHalfH));
    }

    // 据わる系（Ring/Spell）の持ち場を左右に振るオフセット。-1/0/+1/0 … と巡回する3点で、
    // 振り幅は「端へ寄る量（stance_edge_x）の 0.7 倍」かつ徘徊ゾーンの内側。ランダムにしないのは
    // 旧実装の「ランダム巡航＝何をしているか読めない」に戻さないため＝順番に持ち場を移る。
    private int _perchStep;
    private float PerchOffset()
    {
        // 振り幅は徘徊ゾーンの 6 割。端へ寄る量（stance_edge_x）を基準にすると、レイのように
        // 「枠から出ない＝edge_x が小さい」性格では振り幅が Wall の可動域に埋もれ、持ち場を
        // 移っても見た目の可動域が変わらなかった（実測: range 74→72 で横ばい）。ゾーン基準なら
        // 性格（edge_x / track_w）は Wall・Aimed 側に残したまま、据わる系だけ広く巡れる。
        float amp = _zoneHalfW * 0.6f;
        return (_perchStep % 4) switch { 0 => -amp, 2 => amp, _ => 0f };
    }

    // 自機を追うときの目標 x。track_gain で「どれだけ鏡写しに追うか」を決める
    //（ミナ=1.0 ＝ 同じ x に寄る／レイ=0.25 ＝ 枠の中で気配だけ動く）。
    private float TrackedX()
    {
        if (!_hasPlayerX) return _stanceCenterX;
        float want = _stanceCenterX + (_playerX - _stanceCenterX) * _stanceTrackGain;
        return Mathf.Clamp(want, _stanceCenterX - _stanceTrackW, _stanceCenterX + _stanceTrackW);
    }

    private bool PlayerOnLeft() => _hasPlayerX && _playerX < _stanceCenterX;

    // ── 向き。自機が反対側へ FlipDx px 以上・FlipHold 秒続いたときだけ変える。
    //    変える瞬間に FlipSquashDur 秒の縮み（SquashScale）を立てる。
    private void TickFacing(Vector2 currentPos, double delta)
    {
        if (_flipSquashT > 0.0) _flipSquashT -= delta;

        if (!_hasPlayerX) return;
        float dx = _playerX - currentPos.X;
        if (Mathf.Abs(dx) < FlipDx) { _flipWant = 0.0; return; }

        bool wantLeft = dx < 0f;           // 自機が左に居る＝左を向く（素材は右向き＝FlipH=true）
        if (wantLeft == FacingLeft) { _flipWant = 0.0; return; }

        if (wantLeft != _flipWantLeft) { _flipWantLeft = wantLeft; _flipWant = 0.0; }
        _flipWant += delta;
        if (_flipWant < FlipHold) return;

        FacingLeft = wantLeft;
        _flipWant = 0.0;
        _flipSquashT = FlipSquashDur;
        _flipCount++;
    }
}
