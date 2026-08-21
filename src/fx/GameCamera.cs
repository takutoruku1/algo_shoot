using Godot;

// GameCamera : 画面シェイクとヒットストップを提供する Camera2D。
// 固定画面(384x216)の中央に置き、Offset を揺らして全体をシェイクする。
public partial class GameCamera : Camera2D
{
    public static GameCamera Instance = null!;

    private float _shakeMag, _shakeT, _shakeDur = 1f;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    public override void _Ready()
    {
        Instance = this;
        _rng.Randomize();
        Position = new Vector2(192f, 108f); // 384x216 の中央＝現在の見え方を維持
        Zoom = Vector2.One;
        MakeCurrent();
    }

    public override void _Process(double delta)
    {
        if (_shakeT > 0f)
        {
            _shakeT -= (float)delta;
            float f = Mathf.Max(0f, _shakeT / _shakeDur);
            Offset = new Vector2(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)) * _shakeMag * f;
            // 揺れ切ったら強さを捨てる。残さないと以降の小さな Shake が
            // 直前の大きな mag を Max で引き継いだままになる。
            if (_shakeT <= 0f) _shakeMag = 0f;
        }
        else
        {
            Offset = Vector2.Zero;
        }
    }

    // 多重呼び出し時は強さ・締切ともに「大きい方／遅い方」へ合流する（Hitstop と同方針）。
    // 残り時間を上書きすると、長い揺れの最中に短い揺れが割り込んだとき強さだけ残って早く終わる。
    // ただし _shakeDur（減衰の分母）を更新してよいのは実際に締切が延長された場合だけ。
    // dur <= _shakeT の割り込み（例: 大きな被弾シェイクの減衰中に小さなダメージシェイクが来る）で
    // _shakeDur まで縮めると、次フレームの f=_shakeT/_shakeDur が 1.0 に跳ね上がり
    // 振幅が満額へ巻き戻ってしまう。延長が起きないときは _shakeDur を据え置き f の連続性を保つ。
    public void Shake(float mag, float dur)
    {
        _shakeMag = Mathf.Max(_shakeMag, mag);
        if (dur > _shakeT)
        {
            _shakeT = dur;
            _shakeDur = dur; // 減衰の分母は延長後の残り時間＝f は必ず 1 から落ちる
        }
    }

    private double _hitstopEndSec;
    private bool _hitstopRunning;

    // ヒットストップ：実時間タイマーで復帰（タイムスケールを一瞬下げる）。
    // 多重呼び出し時は Shake と同じ方針で、一番遅く終わる要求まで復帰を保留する。
    public async void Hitstop(double dur)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        _hitstopEndSec = Mathf.Max(_hitstopEndSec, now + dur);
        if (_hitstopRunning) return; // 既存の待機ループが延長された締切まで面倒を見る
        _hitstopRunning = true;

        Engine.TimeScale = 0.06;
        double remain;
        while ((remain = _hitstopEndSec - Time.GetTicksMsec() / 1000.0) > 0.0)
        {
            var t = GetTree().CreateTimer(remain, processAlways: true, processInPhysics: false, ignoreTimeScale: true);
            await ToSignal(t, SceneTreeTimer.SignalName.Timeout);
        }
        Engine.TimeScale = 1.0;
        _hitstopRunning = false;
    }
}
