using Godot;

// RetryHold : R 長押しリトライの共通判定（各 *Root.cs / カットシーンが使う）。
//   即発の R リトライは誤爆しやすい（週次プレイテスト指摘）ため、HoldTime の長押しで発動に変更。
//   押している間は Progress(0..1) が上がり、HUD 側（Hud.DrawRetryHoldChip）が充填チップを描く
//   ＝「押した瞬間に何が起きるか」を見せつつ、離せばキャンセルできる。
//   ゲームオーバー中など「すぐやり直したい」局面は instant=true で従来どおり押した瞬間に発動する。
public class RetryHold
{
    public const float HoldTime = 0.45f; // 発動までの長押し秒数（誤爆と待たされ感のバランス）

    private double _t;      // 押しっぱなしの経過秒
    private bool _held;     // instant 用のエッジ検出

    // 押下中の充填率 0..1（0=非表示。HUD のチップ表示用）。
    public float Progress => Mathf.Clamp((float)(_t / HoldTime), 0f, 1f);

    // 毎フレーム呼ぶ。発動したフレームだけ true（呼び元がシーンを再読込する）。
    public bool Update(double delta, bool pressed, bool instant = false)
    {
        bool edge = pressed && !_held; _held = pressed;
        if (instant) { _t = 0; return edge; }
        if (!pressed) { _t = 0; return false; }
        _t += delta;
        return _t >= HoldTime;
    }
}
