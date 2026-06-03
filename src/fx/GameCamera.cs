using Godot;

// GameCamera : 画面シェイクとヒットストップを提供する Camera2D。
// 固定画面(384x216)の中央に置き、Offset を揺らして全体をシェイクする。
public partial class GameCamera : Camera2D
{
    public static GameCamera Instance;

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
        }
        else
        {
            Offset = Vector2.Zero;
        }
    }

    public void Shake(float mag, float dur)
    {
        _shakeMag = Mathf.Max(_shakeMag, mag);
        _shakeT = dur;
        _shakeDur = dur;
    }

    // ヒットストップ：実時間タイマーで復帰（タイムスケールを一瞬下げる）。
    public async void Hitstop(double dur)
    {
        Engine.TimeScale = 0.06;
        var t = GetTree().CreateTimer(dur, processAlways: true, processInPhysics: false, ignoreTimeScale: true);
        await ToSignal(t, SceneTreeTimer.SignalName.Timeout);
        Engine.TimeScale = 1.0;
    }
}
