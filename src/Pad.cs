using Godot;

// Pad : 接続中のゲームパッドのボタン押下を、デバイス番号を気にせず判定する小ヘルパ。
// 移動と決定(ui_*/ui_accept)は既定のInputMapがジョイパッドを含むため、ここでは
// 追加で割り当てたいボタン（ボム/低速/スキル/リスタート/送り）の判定に使う。
public static class Pad
{
    public static bool Pressed(JoyButton b)
    {
        foreach (var dev in Input.GetConnectedJoypads())
            if (Input.IsJoyButtonPressed(dev, b))
                return true;
        return false;
    }
}
