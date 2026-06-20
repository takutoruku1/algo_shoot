using Godot;
using System;

// BossMover : ボスの徘徊（うろつき）移動を1か所に集約した再利用クラス。
//
// 旧実装は5ボスでほぼ完全に重複していた「広い箱からランダムにターゲットを選び、
// 一定速度・直線でそこへ飛ぶ」だった。立ち絵が手前を向いたまま画面を縦横無尽に
// 直線移動するのが無機質で違和感の原因。これを次の方針で“浮遊感”に作り替える：
//   ・ターゲット箱を画面上部のボスゾーンに狭める（全画面を突っ切らせない）。
//   ・イージング（指数補間の速度追従＝SmoothDamp相当）でターゲットへ加速→減速。到達で“間”。
//   ・ホバー（ゆっくりした縦サイン）を本体位置に薄く混ぜ、静止中も生きて見せる。
//   ・激しい揺れ・傾きは視覚（VisualOffset/Lean）側に寄せ、当たり判定の中心
//     （= 呼び出し側が GlobalPosition に入れる戻り値）はゆっくり素直に動かす。
//     → 弾避けの公平性を保つ（当たり判定がガクガクしない）。
//
// 当たり判定そのものは一切持たない。Step() が返すのは本体位置のみで、
// 視覚用の付加情報（VisualOffset / Lean / FacingLeft）は読み取りプロパティで渡す。
public sealed class BossMover
{
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // ── ターゲット箱（中心＋半幅/半高）。Configure で設定。画面は内部解像度384×216。
    private Vector2 _zoneCenter = new Vector2(200f, 70f);
    private float _zoneHalfW = 90f;
    private float _zoneHalfH = 28f;

    // ── 巡航・追従パラメータ。
    private float _cruiseSpeed = 40f;   // おおよその最高巡航速度(px/s)。旧 RoamSpeed を踏襲。
    private float _accelTime = 0.55f;   // 速度の追従時定数(s)。小さいほどキビキビ、大きいほどふわり。
    private float _arriveDist = 10f;    // この距離まで近づいたら到達扱い。
    private float _restMin = 0.25f;     // 到達後の小休止（下限/上限）。次ターゲットまでの“間”。
    private float _restMax = 0.7f;

    // ── ホバー（縦の微揺れ）。本体位置に薄く混ぜる＝静止中も漂って見える。
    private float _hoverAmp = 3.5f;     // 縦振幅(px)。当たり判定にも乗る“ゆっくり素直な”分だけ。
    private float _hoverFreq = 0.9f;    // 周波数(Hz相当, rad換算は内部で)。

    // ── 視覚専用（当たり判定に入れない）。
    private float _breatheAmp = 1.6f;   // 呼吸の上下微小オフセット(px)。
    private float _swayAmp = 1.4f;      // 横の揺らぎ(px)。
    private float _leanMax = 0.16f;     // 進行方向への最大傾き(rad)。

    // ── 内部状態。
    private Vector2 _target;
    private bool _hasTarget;
    private Vector2 _velocity;          // 平滑用の現在速度。
    private double _restTimer;          // 残り小休止。
    private double _hoverPhase;         // ホバー位相。
    private double _breathePhase;       // 呼吸位相（ホバーと別位相で動かす）。
    private float _lean;                // 現在の傾き（平滑して急変させない）。

    // ── 公開：視覚用の付加情報（呼び出し側が ApplyBossMotion に渡す）。
    public Vector2 VisualOffset { get; private set; }
    public float Lean => _lean;
    public bool FacingLeft { get; private set; } = true;

    public BossMover()
    {
        _rng.Randomize();
        _breathePhase = _rng.RandfRange(0f, Mathf.Tau);
        _hoverPhase = _rng.RandfRange(0f, Mathf.Tau);
    }

    // ボスごとの徘徊設定。zoneCenter/halfW/halfH でターゲット箱、cruiseSpeed で速さ。
    // 既定値で十分自然なので、ボス側は最低限 cruiseSpeed だけ渡せばよい。
    public void Configure(
        Vector2 zoneCenter, float zoneHalfW, float zoneHalfH, float cruiseSpeed,
        float accelTime = 0.55f, float hoverAmp = 3.5f, float hoverFreq = 0.9f,
        float leanMax = 0.16f)
    {
        _zoneCenter = zoneCenter;
        _zoneHalfW = zoneHalfW;
        _zoneHalfH = zoneHalfH;
        _cruiseSpeed = cruiseSpeed;
        _accelTime = Mathf.Max(0.05f, accelTime);
        _hoverAmp = hoverAmp;
        _hoverFreq = hoverFreq;
        _leanMax = leanMax;
    }

    // 次フレームの本体位置（= 当たり判定の中心 GlobalPosition に入れる値）を返す。
    // 中で：到達判定＋次ターゲット抽選 / 速度の指数追従（急停止・急発進しない）/
    //        ホバーを薄く混ぜる / 視覚用 VisualOffset・Lean・FacingLeft を更新。
    public Vector2 Step(Vector2 currentPos, double delta)
    {
        float dt = (float)delta;
        if (dt <= 0f) return currentPos;

        if (!_hasTarget) PickTarget(currentPos);

        // 小休止中はターゲットへ向かう推進を止め、速度を緩やかに減衰（ふっと止まる）。
        Vector2 desiredVel;
        if (_restTimer > 0.0)
        {
            _restTimer -= delta;
            desiredVel = Vector2.Zero;
        }
        else
        {
            Vector2 to = _target - currentPos;
            float dist = to.Length();
            if (dist < _arriveDist)
            {
                // 到達：少し“間”を置いてから次ターゲットへ。
                _restTimer = _rng.RandfRange(_restMin, _restMax);
                _hasTarget = false;
                desiredVel = Vector2.Zero;
            }
            else
            {
                // ターゲット近くで減速（残り距離でスケール）。遠ければ巡航速度。
                float speed = _cruiseSpeed * Mathf.Clamp(dist / 36f, 0.25f, 1f);
                desiredVel = (to / dist) * speed;
            }
        }

        // 速度の指数追従（SmoothDamp相当）：目標速度へ時定数 _accelTime で寄せる。
        float k = 1f - Mathf.Exp(-dt / _accelTime);
        _velocity = _velocity.Lerp(desiredVel, k);

        Vector2 nextPos = currentPos + _velocity * dt;

        // ホバー（ゆっくりした縦サイン）を本体位置へ薄く混ぜる＝静止中も漂う。
        // 振幅は小さく当たり判定が暴れない範囲。位相を進めて差分だけ反映する。
        double prevHover = Math.Sin(_hoverPhase) * _hoverAmp;
        _hoverPhase += _hoverFreq * Mathf.Tau * delta;
        double curHover = Math.Sin(_hoverPhase) * _hoverAmp;
        nextPos.Y += (float)(curHover - prevHover);

        // ── 視覚専用の付加情報を更新（当たり判定には入れない）。
        _breathePhase += 1.3 * delta;
        float breathe = (float)Math.Sin(_breathePhase) * _breatheAmp;          // 呼吸の上下
        float sway = (float)Math.Sin(_hoverPhase * 0.7) * _swayAmp;            // 横の揺らぎ
        VisualOffset = new Vector2(sway, -breathe);

        // 進行方向への傾き（横速度に比例）。平滑して急に傾かないようにする。
        float targetLean = Mathf.Clamp(_velocity.X / Mathf.Max(1f, _cruiseSpeed), -1f, 1f) * _leanMax;
        _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-dt / 0.25f));

        // 向き（速度のX符号）。素材は右向きなので進行方向左で FlipH=true。
        // 速度がほぼ0のときは前の向きを保つ（小刻みに反転しない）。
        if (Mathf.Abs(_velocity.X) > 4f) FacingLeft = _velocity.X < 0f;

        return nextPos;
    }

    private void PickTarget(Vector2 fromPos)
    {
        // ゾーン内のランダム点。直前位置から近すぎる点は避け、ある程度の移動を確保。
        Vector2 t = fromPos;
        for (int i = 0; i < 4; i++)
        {
            t = new Vector2(
                _zoneCenter.X + _rng.RandfRange(-_zoneHalfW, _zoneHalfW),
                _zoneCenter.Y + _rng.RandfRange(-_zoneHalfH, _zoneHalfH));
            if ((t - fromPos).Length() > 40f) break;
        }
        _target = t;
        _hasTarget = true;
    }
}
