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

    // 直近に使った入力デバイス。HUDの操作ガイドを KB / パッドで出し分けるためだけに使う。
    // （パッド表記は Godot の JoyButton 配列＝Xbox 系の A/B/X/Y/LB に一致）。
    public static bool UsingPad { get; private set; }

    // ───────── ボタン表記スタイル（Xbox / PlayStation 切替）─────────
    // Godot の JoyButton は物理配置（Xbox 系）で固定。表記だけを設定で差し替える。
    // 既定は Xbox＝従来の見た目（リグレッション無し）。設定画面(操作カテゴリ)で切替し、
    // user://settings.json に保存・起動時に PadConfig.ApplySaved() で復元する。
    public enum ButtonStyle { Xbox, PlayStation }

    public static ButtonStyle Style { get; set; } = ButtonStyle.Xbox;

    // JoyButton → 現在スタイルでの表記文字列。HUD の操作子トークンが使う。
    // PlayStation は Unicode 記号（〇=U+3007 / ×=U+00D7 / □=U+25A1 / △=U+25B3）。
    public static string Face(JoyButton b)
    {
        bool ps = Style == ButtonStyle.PlayStation;
        return b switch
        {
            JoyButton.A             => ps ? "〇" : "A",   // 物理 A ＝ PS 〇
            JoyButton.B             => ps ? "×"  : "B",   // 物理 B ＝ PS ×
            JoyButton.X             => ps ? "□"  : "X",   // 物理 X ＝ PS □
            JoyButton.Y             => ps ? "△"  : "Y",   // 物理 Y ＝ PS △
            JoyButton.LeftShoulder  => ps ? "L1" : "LB",
            JoyButton.RightShoulder => ps ? "R1" : "RB",
            _ => b.ToString(),
        };
    }

    // 保存済みのボタン表記スタイルを Style へ復元（起動時に1回、Audio._Ready から呼ぶ）。
    // 永続キーは Settings.cs と一致させた "padstyle"（0=Xbox / 1=PlayStation）。未保存なら Xbox。
    public const string SettingKey = "padstyle";
    public static void ApplySaved()
    {
        const string path = "user://settings.json";
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary) return;
        var data = json.Data.AsGodotDictionary();
        if (data.ContainsKey(SettingKey))
            Style = data[SettingKey].AsInt32() == 1 ? ButtonStyle.PlayStation : ButtonStyle.Xbox;
    }

    private static readonly JoyButton[] HintButtons =
    {
        JoyButton.A, JoyButton.B, JoyButton.X, JoyButton.Y,
        JoyButton.LeftShoulder, JoyButton.RightShoulder,
        JoyButton.DpadUp, JoyButton.DpadDown, JoyButton.DpadLeft, JoyButton.DpadRight,
        JoyButton.Start, JoyButton.Back,
    };
    private static readonly Key[] HintKeys =
    {
        Key.W, Key.A, Key.S, Key.D, Key.Up, Key.Down, Key.Left, Key.Right,
        Key.Z, Key.X, Key.C, Key.V, Key.Shift, Key.Space,
    };

    // 毎フレーム呼ぶ：パッド操作があれば UsingPad=true、キー操作があれば false（無操作なら直前を維持）。
    public static void PollDevice()
    {
        foreach (var dev in Input.GetConnectedJoypads())
        {
            foreach (var b in HintButtons)
                if (Input.IsJoyButtonPressed(dev, b)) { UsingPad = true; return; }
            var ax = new Vector2(Input.GetJoyAxis(dev, JoyAxis.LeftX), Input.GetJoyAxis(dev, JoyAxis.LeftY));
            if (ax.Length() > 0.5f) { UsingPad = true; return; }
        }
        foreach (var k in HintKeys)
            if (Input.IsKeyPressed(k)) { UsingPad = false; return; }
    }
}
