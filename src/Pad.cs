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
    // ※ Display!=Auto のときは「操作表示モード」が固定なので、この自動判定は表記に使わない。
    private static bool _autoUsingPad;

    // ───────── 操作表示モード（ヒント/ボタン表記をどれで固定して見せるか）─────────
    // タイトル「はじめから」で必ず1度選び、設定でも変更可。入力自体は KB/パッド常に有効で、
    // これは“見た目（表記）”だけを固定する。Auto のときだけ従来どおり直近デバイスで自動判定。
    //   Keyboard       … キーボード表記（Z / Shift / …）
    //   PadPlayStation … PS 表記（〇 × □ △ / L1 R1）
    //   PadXbox        … Xbox 表記（A B X Y / LB RB）
    //   Auto           … 直近に使ったデバイスで KB/パッドを出し分け（旧挙動・後方互換の既定）
    public enum DisplayMode { Auto, Keyboard, PadPlayStation, PadXbox }

    public static DisplayMode Display { get; set; } = DisplayMode.Auto;

    // 表記がキーボードか（Display で固定／Auto は直近デバイスから）。
    public static bool ShowKeyboard => Display switch
    {
        DisplayMode.Keyboard       => true,
        DisplayMode.PadPlayStation => false,
        DisplayMode.PadXbox        => false,
        _                          => !_autoUsingPad,
    };

    // 表記がパッドか（＝!ShowKeyboard）。HUD トークンが使う。
    public static bool UsingPad => !ShowKeyboard;

    // ───────── ボタン表記スタイル（Xbox / PlayStation 切替）─────────
    // Godot の JoyButton は物理配置（Xbox 系）で固定。表記だけを切り替える。
    // Display 導入に伴い、Style は Display から導出する（PadPlayStation⇔PlayStation / その他⇔Xbox）。
    // Auto のときは別途保持した _autoStyle（旧 padstyle 由来）に従う＝後方互換。
    public enum ButtonStyle { Xbox, PlayStation }

    private static ButtonStyle _autoStyle = ButtonStyle.Xbox; // Auto 時に使うパッド表記（旧 padstyle）

    public static ButtonStyle Style
    {
        get => Display switch
        {
            DisplayMode.PadPlayStation => ButtonStyle.PlayStation,
            DisplayMode.PadXbox        => ButtonStyle.Xbox,
            _                          => _autoStyle, // Keyboard でもパッド入力時の表記基準として使う
        };
        // 旧コード（Settings の padstyle 切替）との互換：Auto 用のスタイルを更新する。
        set => _autoStyle = value;
    }

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
            JoyButton.RightStick    => ps ? "R3" : "R3",
            _ => b.ToString(),
        };
    }

    // ───────── 永続化 ─────────
    // 旧キー padstyle(0=Xbox/1=PS) と新キー inputdisplay(0=KB/1=PS/2=Xbox) の両方を扱う。
    // 後方互換：inputdisplay が無ければ Display=Auto のまま、padstyle で _autoStyle だけ復元する。
    public const string SettingKey = "padstyle";        // 旧：Auto 用のパッド表記
    public const string DisplayKey = "inputdisplay";    // 新：操作表示モード(0=KB/1=PS/2=Xbox)

    // 操作表示モード ⇄ inputdisplay の整数の対応。
    public static int DisplayToInt(DisplayMode m) => m switch
    {
        DisplayMode.Keyboard       => 0,
        DisplayMode.PadPlayStation => 1,
        DisplayMode.PadXbox        => 2,
        _                          => -1, // Auto は保存しない（未選択）
    };
    public static DisplayMode IntToDisplay(int i) => i switch
    {
        0 => DisplayMode.Keyboard,
        1 => DisplayMode.PadPlayStation,
        2 => DisplayMode.PadXbox,
        _ => DisplayMode.Auto,
    };

    // 操作表示モードを設定し、user://settings.json の inputdisplay へ即保存（タイトルの3択から呼ぶ）。
    // 既存の他キーは保持してマージ書き込みする（Settings.Save と同じファイル・整合）。
    public static void SetDisplayAndSave(DisplayMode m)
    {
        Display = m;
        const string path = "user://settings.json";
        var data = new Godot.Collections.Dictionary();
        if (FileAccess.FileExists(path))
        {
            using var rf = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (rf != null)
            {
                var json = new Json();
                if (json.Parse(rf.GetAsText()) == Error.Ok && json.Data.VariantType == Variant.Type.Dictionary)
                    data = json.Data.AsGodotDictionary();
            }
        }
        int v = DisplayToInt(m);
        if (v >= 0) data[DisplayKey] = v; // Auto(-1) は保存しない
        using var wf = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        wf?.StoreString(Json.Stringify(data));
    }

    // 保存済みの設定を復元（起動時に1回、Audio._Ready から呼ぶ）。
    public static void ApplySaved()
    {
        const string path = "user://settings.json";
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary) return;
        var data = json.Data.AsGodotDictionary();
        // 旧 padstyle（Auto 用のパッド表記）。
        if (data.ContainsKey(SettingKey))
            _autoStyle = data[SettingKey].AsInt32() == 1 ? ButtonStyle.PlayStation : ButtonStyle.Xbox;
        // 新 inputdisplay（操作表示モード）。あれば固定表示、無ければ Auto のまま。
        if (data.ContainsKey(DisplayKey))
            Display = IntToDisplay(data[DisplayKey].AsInt32());
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
        Key.Z, Key.X, Key.C, Key.V, Key.Shift, Key.Ctrl,
    };

    // 毎フレーム呼ぶ：パッド操作があれば _autoUsingPad=true、キー操作があれば false（無操作なら直前を維持）。
    // Display!=Auto の固定表示中は表記に使わないが、Auto に戻したときのために更新は続ける。
    public static void PollDevice()
    {
        foreach (var dev in Input.GetConnectedJoypads())
        {
            foreach (var b in HintButtons)
                if (Input.IsJoyButtonPressed(dev, b)) { _autoUsingPad = true; return; }
            var ax = new Vector2(Input.GetJoyAxis(dev, JoyAxis.LeftX), Input.GetJoyAxis(dev, JoyAxis.LeftY));
            if (ax.Length() > 0.5f) { _autoUsingPad = true; return; }
        }
        foreach (var k in HintKeys)
            if (Input.IsKeyPressed(k)) { _autoUsingPad = false; return; }
    }
}
