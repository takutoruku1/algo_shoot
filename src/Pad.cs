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

    // ───────── UIゲート（オーバーレイの入力食い）─────────
    // ポーズメニュー等のオーバーレイは、開いている間 毎フレーム ConsumeUi(this) を呼ぶ。
    // 下の画面は UiBlocked(this) の間、キーを「既押し」扱い（held=true）にして入力を食う。
    // これで「オーバーレイを閉じた Esc/Z/Start の同じ押下」が下の画面の もどる/決定/リトライ として
    // 二重処理されない（例：ショップで Esc→Esc がメニューだけでなくショップごと閉じる不具合の修正）。
    private static object? _uiGateOwner;
    private static ulong _uiGateFrame;
    public static void ConsumeUi(object owner) { _uiGateOwner = owner; _uiGateFrame = Engine.GetProcessFrames(); }
    public static bool UiBlocked(object self) =>
        _uiGateOwner != null && !ReferenceEquals(_uiGateOwner, self)
        && Engine.GetProcessFrames() - _uiGateFrame <= 1;

    // 直近に使った入力デバイス。ボタンプロンプト表記を KB / パッドで動的に出し分ける根拠。
    // （パッド表記は Godot の JoyButton 配列＝Xbox 系の A/B/X/Y/LB に一致）。
    // パッドで開始→途中でキーボードへ持ち替え（逆も）ても、PollDevice が毎フレーム追跡して
    // 表記は「最後に触ったデバイス」へ自動で切り替わる（週次プレイテスト指摘の修正）。
    private static bool _autoUsingPad;

    // ───────── 操作表示モード（ボタン表記の初期値とパッド表記スタイル）─────────
    // タイトル「はじめから」で必ず1度選び、設定でも変更可。入力自体は KB/パッド常に有効。
    // KB/パッドどちらの表記を出すかは常に「直近に使ったデバイス」へ自動追従し（PollDevice）、
    // Display は「パッド使用時にどの表記(PS/Xbox)で見せるか」と起動直後の初期表示を決める。
    //   Keyboard       … 初期表示＝キーボード表記（Z / Shift / …）
    //   PadPlayStation … 初期表示＝パッド・PS 表記（〇 × □ △ / L1 R1）
    //   PadXbox        … 初期表示＝パッド・Xbox 表記（A B X Y / LB RB）
    //   Auto           … 未選択（旧セーブ互換）。パッド表記は旧 padstyle に従う
    public enum DisplayMode { Auto, Keyboard, PadPlayStation, PadXbox }

    public static DisplayMode Display { get; set; } = DisplayMode.Auto;

    // 表記がキーボードか＝直近に使ったデバイスがキーボードか。常に自動追従する
    // （固定はしない＝ゲームパッドで開始→途中でKBに持ち替えても表記が付いてくる）。
    // マウス使用時(_usingMouse)はマウス専用アイコンを作らない方針＝キーボード表記へフォールバックする。
    public static bool ShowKeyboard => _usingMouse || !_autoUsingPad;

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
            JoyButton.Start         => ps ? "OPTIONS" : "MENU", // PS=OPTIONS / Xbox=メニュー(≡)ボタン
            JoyButton.Back          => ps ? "SHARE" : "VIEW",   // PS=SHARE / Xbox=ビュー(左小)ボタン
            _ => b.ToString(),
        };
    }

    // ポーズ開閉の操作子表記：キーボード表示なら Esc、パッド表示なら Start(MENU/OPTIONS)。
    public static string PauseToken => ShowKeyboard ? "Esc" : Face(JoyButton.Start);
    // 決定の操作子表記：キーボードなら Z、パッドなら A(〇)。
    public static string ConfirmToken => ShowKeyboard ? "Z" : Face(JoyButton.A);

    // ───────── 共通の操作子表記（UI 横断の単一の真実）─────────
    // 各画面（Shop / Settings / Hud …）はボタン名を直書きせず、ここを通すことで Pad.Display(KB/PS/Xbox)
    // 切替に必ず追従する。物理マッピング（JoyButton）と KB キーの対応は Player.cs / *.cs の入力判定と一致させる。
    //   Cancel  … X(KB) / B(パッド＝×・もどる/キャンセル)
    //   Equip   … C(KB) / Y(パッド＝△・装備/サブ操作)
    //   Bomb    … X(KB) / X(パッド＝□)
    //   ModeSw  … V(KB) / B(パッド) … ショップの「過熱」プレビューや HUD のモード切替に流用
    public static string CancelToken => ShowKeyboard ? "X" : Face(JoyButton.B);
    public static string EquipToken  => ShowKeyboard ? "C" : Face(JoyButton.Y);
    public static string BombToken   => ShowKeyboard ? "X" : Face(JoyButton.X);
    public static string ModeToken   => ShowKeyboard ? "V" : Face(JoyButton.B);
    // Flip … F(KB) / RB(パッド＝R1)。射撃方向を右⇔左にトグルする向き反転ボタン。
    public static string FlipToken   => ShowKeyboard ? "F" : Face(JoyButton.RightShoulder);
    // 移動（方向）。キーボードは矢印、パッドは左スティック表記。
    public static string MoveToken   => ShowKeyboard ? "↑↓←→" : "L";

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
        // 表記の初期デバイスもここから種まき（以降は PollDevice が直近デバイスで上書きし続ける）。
        _autoUsingPad = m == DisplayMode.PadPlayStation || m == DisplayMode.PadXbox;
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
        // 新 inputdisplay（操作表示モード）。あれば初期表示の種に、無ければ Auto のまま。
        if (data.ContainsKey(DisplayKey))
        {
            Display = IntToDisplay(data[DisplayKey].AsInt32());
            // 起動直後の表記は選択済みモードに合わせる（初回入力からは直近デバイスに自動追従）。
            _autoUsingPad = Display == DisplayMode.PadPlayStation || Display == DisplayMode.PadXbox;
        }
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
        Key.Z, Key.X, Key.C, Key.V, Key.F, Key.Shift, Key.Ctrl, Key.Alt,
        Key.Space, Key.Enter, Key.Escape, Key.R, Key.Q, Key.T, Key.L, Key.Tab,
    };

    // 毎フレーム呼ぶ：パッド操作があれば _autoUsingPad=true、キー操作があれば false（無操作なら直前を維持）。
    // ゲーム中は Player が、メニュー/ハブ/カットシーン等は常駐の PauseMenu が呼ぶ＝全画面で表記が追従する。
    public static void PollDevice()
    {
        foreach (var dev in Input.GetConnectedJoypads())
        {
            foreach (var b in HintButtons)
                if (Input.IsJoyButtonPressed(dev, b)) { _autoUsingPad = true; _usingMouse = false; return; }
            var ax = new Vector2(Input.GetJoyAxis(dev, JoyAxis.LeftX), Input.GetJoyAxis(dev, JoyAxis.LeftY));
            if (ax.Length() > 0.5f) { _autoUsingPad = true; _usingMouse = false; return; }
        }
        foreach (var k in HintKeys)
            if (Input.IsKeyPressed(k)) { _autoUsingPad = false; _usingMouse = false; return; }
    }

    // ═════════════════════════════ マウス入力（キーボード/パッドへ純粋に追加）═════════════════════════════
    //   Pad は static ヘルパでノードではない＝_Input を持てないため、ホイールイベントの蓄積は常駐 PauseMenu
    //   （全画面で毎フレーム動く）が _UnhandledInput で拾って FeedWheel/FeedMouseButton へ流し込む方式にする。
    //   ボタンの押下/エッジは Input.IsMouseButtonPressed をこの PollMouse で前フレーム比較して立てる。
    //   座標系は UI 全体と同じ「設計座標(1280×720)」に統一する（実ビューポート384系 ÷ UiKit.Scale）。

    // 直近デバイスにマウスを第3の値として追加。KB/パッドの2値(_autoUsingPad)は壊さず、マウス時は
    // 別フラグで上書き表示を判断する。表記トークン(ConfirmToken 等)は KB/パッド2値前提なので、
    // マウス時は ShowKeyboard=true（キーボード表記）へフォールバックさせる（マウス専用アイコンは作らない）。
    private static bool _usingMouse;
    public static bool UsingMouse => _usingMouse;

    // マウス左/右ボタンの状態と前フレーム値（エッジ検出用）。PollMouse が毎フレーム更新する。
    private static bool _mL, _mLPrev, _mR, _mRPrev;
    // ホイールの当該フレーム蓄積量（上=+ / 下=-）。_UnhandledInput で貯め、フレーム末に消費してリセット。
    private static float _wheelAccum;
    private static float _wheelFrame;   // このフレームで確定したホイール量（PollMouse で accum→frame へ移す）
    // 最後に検知したマウス設計座標。移動しきい値でデバイス追従に使う。
    private static Vector2 _mousePos, _mousePosPrev;
    // 初回 PollMouse かどうか。既定の _mousePos は (0,0) なので、初回はカーソル実座標へ「跳ぶ」ぶんが
    //   そのまま移動量になり、プレイヤーがマウスに触れていなくても _usingMouse が true にラッチしてしまう
    //   （＝起動直後にカーソルが偶然乗っている項目へ選択が飛び、Hub ではロック中カードが選ばれて
    //     「Zが効かない」ように見えた）。初回は基準を埋めるだけにして移動判定を行わない。
    private static bool _mouseSeeded;
    private const float MouseMoveThresh = 3f; // 設計座標(1280系)で3px以上動いたら「マウスを使った」

    // 常駐 PauseMenu の _UnhandledInput から呼ぶ：ホイールを当該フレームぶん蓄積する
    //（1フレームに複数の WheelUp/Down が来ても取りこぼさない）。
    public static void FeedWheel(float delta) => _wheelAccum += delta;

    // 毎フレーム呼ぶ（PauseMenu / Player）：マウス座標・ボタンエッジを更新し、ホイール蓄積をフレーム値へ確定する。
    // viewport 経由でビューポート実座標(384系)を取り、UiKit.Scale で割って設計座標(1280系)へ変換する。
    public static void PollMouse(Viewport vp)
    {
        _mLPrev = _mL; _mRPrev = _mR;
        _mL = Input.IsMouseButtonPressed(MouseButton.Left);
        _mR = Input.IsMouseButtonPressed(MouseButton.Right);

        _mousePosPrev = _mousePos;
        if (vp != null) _mousePos = vp.GetMousePosition() / UiKit.Scale; // 384系 → 設計1280系

        // 初回は基準を埋めるだけ（前フレーム値を実座標に揃える）。ここで moved を出すと必ず誤検知になる。
        if (!_mouseSeeded) { _mouseSeeded = true; _mousePosPrev = _mousePos; }

        // ホイール蓄積を当該フレームの値へ確定してからクリア（読み手は WheelDelta で受け取る）。
        _wheelFrame = _wheelAccum; _wheelAccum = 0f;

        // ── デバイス追従：マウス移動(しきい値超)/クリック/ホイールがあれば直近デバイス=マウス ──
        bool moved = (_mousePos - _mousePosPrev).Length() > MouseMoveThresh;
        if (moved || _mL || _mR || _wheelFrame != 0f) _usingMouse = true;
        // KB/パッド操作は PollDevice 側で _usingMouse=false に落ちる（同フレームで PollDevice が後勝ち/先勝ちに
        // ならないよう、呼び順は「PollDevice → PollMouse」を推奨。マウス無操作なら _usingMouse は据え置き）。
    }

    // ── 読み取りヘルパ（各画面が使う公開API）──
    public static bool MouseDown() => _mL;                        // 左ボタン押下中
    public static bool MouseClick() => _mL && !_mLPrev;           // 左ボタン押下エッジ（このフレームで押された）
    public static bool MouseReleased() => !_mL && _mLPrev;        // 左ボタン離しエッジ
    public static bool MouseRightDown() => _mR;                   // 右ボタン押下中
    public static bool MouseRightClick() => _mR && !_mRPrev;      // 右ボタン押下エッジ
    public static float WheelDelta() => _wheelFrame;             // このフレームのホイール量（上=+ / 下=-、無=0）
    public static Vector2 MousePos() => _mousePos;                // 設計座標(1280×720)でのマウス位置

    // ═════════════════════════════ 会話送りの共通ヘルパ ═════════════════════════════
    //   従来 5ステージ＋Hud＋ShopTutorial＋3カットシーンで重複していた
    //     z = Key.Z (|| Key.Enter) || ui_accept || Pad.A
    //   の「送り」判定を一本化し、マウス左クリックを OR で追加する。各画面はこれを毎フレーム呼んで
    //   前フレーム比でエッジを取る（従来の _zEdge = z && !_zHeld と同じ使い方）。
    //   Enter を含めるかは画面差があったが、送りとしては Enter も許すのが自然なので全画面で含める。
    //   ※マウス左クリックはボタンUI(ホットスポット)を持つ画面では誤爆しうるが、会話専用画面(ステージ会話/
    //     カットシーン/Hud会話)では画面全体が「送り」なので問題ない。ホットスポットを持つ画面(Shop/メニュー)は
    //     この AdvanceHeld ではなく個別のクリック結線(フェーズ2/3)を使う。
    public static bool AdvanceHeld() =>
        Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter)
        || Input.IsActionPressed("ui_accept") || Pressed(JoyButton.A)
        || MouseDown();
}
