using Godot;

// DemoPilot : デモプレイ動画用の自動操縦オートロード (/root/DemoPilot)。
//
// 起動コマンドのユーザ引数（`-- --demo` のように `--` の後ろ）に "--demo" が
// 含まれているときだけ有効化し、合成入力を Input.ParseInputEvent で流して
// ゲームを“勝手にプレイ”させる。Player.cs 等はキーをポーリングしているだけなので、
// 本物のキー入力と同じ経路で合成イベントを注入すれば、ゲーム側を一切書き換えずに動く。
//
//   有効化:   res://...tscn -- --demo
//   尺の指定: -- --demo --seconds 80     （省略時 DefaultSeconds）
//
// ・Z をパルス（撃つ＋会話送り兼用）。会話中（Hud.BubblePaused）は周期を伸ばして
//   セリフが読める速さにする。
// ・方向キー(ui_*)をサインで揺らして自機をウィーブさせ、生きている画にする。
// ・たまにボム(X)を撃って画面を派手にする。
// ・指定秒で GetTree().Quit() し、--write-movie の録画を確定させる。
public partial class DemoPilot : Node
{
    private const double DefaultSeconds = 80.0;

    private bool _active;
    private double _seconds = DefaultSeconds;
    private double _t;

    // Z パルス状態
    private bool _zDown;
    private double _zPhase;

    // ボムパルス状態
    private double _bombPhase;
    private bool _xDown;

    public override void _Ready()
    {
        var user = OS.GetCmdlineUserArgs();
        for (int i = 0; i < user.Length; i++)
        {
            if (user[i] == "--demo") _active = true;
            else if (user[i] == "--seconds" && i + 1 < user.Length
                     && double.TryParse(user[i + 1], out var s)) _seconds = s;
        }

        if (!_active)
        {
            SetProcess(false);
            return;
        }
        GD.Print($"[DemoPilot] active. recording {_seconds:0}s of autoplay.");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t >= _seconds)
        {
            GD.Print("[DemoPilot] done. quitting to finalize movie.");
            GetTree().Quit();
            return;
        }

        DriveMovement();
        DriveShootAndAdvance(delta);
        DriveBomb(delta);
    }

    // 自機を左右に大きく、上下に小さく揺らして“避けている風”に動かす。
    // 画面外には Player 側でクランプされるので行き過ぎは気にしない。
    private void DriveMovement()
    {
        float vx = Mathf.Sin((float)_t * 1.3f) * 0.9f + Mathf.Sin((float)_t * 0.37f) * 0.4f;
        float vy = Mathf.Sin((float)_t * 0.8f + 1.1f) * 0.55f;
        SetAxis("ui_left", "ui_right", vx);
        SetAxis("ui_up", "ui_down", vy);
    }

    // 1軸ぶんの ui_neg / ui_pos を強さ付きで注入する（GetVector がこれを読む）。
    private static void SetAxis(string neg, string pos, float v)
    {
        v = Mathf.Clamp(v, -1f, 1f);
        Send(new InputEventAction { Action = pos, Pressed = v > 0.05f, Strength = Mathf.Max(0f, v) });
        Send(new InputEventAction { Action = neg, Pressed = v < -0.05f, Strength = Mathf.Max(0f, -v) });
    }

    // Z パルス。撃つのと会話送りを兼ねる。会話中はゆっくり（読める速さ）にする。
    private void DriveShootAndAdvance(double delta)
    {
        bool talking = Hud.BubblePaused;
        double period = talking ? 0.65 : 0.16; // 会話中は約1.5Hz、戦闘中は約6Hz
        _zPhase += delta;
        if (_zPhase >= period) _zPhase -= period;
        bool down = _zPhase < period * 0.45; // 周期の前半だけ押下
        if (down != _zDown)
        {
            _zDown = down;
            Send(new InputEventKey { Keycode = Key.Z, Pressed = down });
        }
    }

    // 約18秒ごとにボムを1発。会話中は撃たない。
    private void DriveBomb(double delta)
    {
        _bombPhase += delta;
        bool wantPress = !Hud.BubblePaused && _bombPhase >= 18.0;
        if (wantPress && !_xDown)
        {
            _xDown = true;
            Send(new InputEventKey { Keycode = Key.X, Pressed = true });
        }
        else if (_xDown && _bombPhase >= 18.0 + 0.12)
        {
            _xDown = false;
            _bombPhase = 0.0;
            Send(new InputEventKey { Keycode = Key.X, Pressed = false });
        }
    }

    private static void Send(InputEvent e) => Input.ParseInputEvent(e);
}
