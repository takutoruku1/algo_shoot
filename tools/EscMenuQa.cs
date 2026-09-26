using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// EscMenuQa : 2026-09-27 のユーザー指示5件のうち、ポーズメニューとハブまわりの自動検証。
//   (a) 戦闘中（Akari）：Esc で開く／Esc で閉じる
//   (b) Prologue / Epilogue / Final：Esc で開いて閉じても進行が続く（タイマー停止→再開・行送り・R 長押し・ログ）
//   (c) ハブのホーム：Esc では開かない（M では開く）。SNS の Esc は従来どおりホームへもどる
//   (d) 「タイトルへ」→ セーブしますか？ → セーブする → スロット → 書かれてタイトルへ
//   (e) 同ダイアログのキャンセル → メニューへ戻る／スロットのキャンセル → 問いへ戻る
//   (f) ウィンドウの×（NotificationWMCloseRequest）→ 問いが出る／キャンセルで元へ／セーブせずに終了で終了要求
//       （QuitOverride で差し替えて実際には終了させない）。タイトル画面では即終了、トレーニングは2択
//   (g) 右下ヒント「M メニュー」は戦闘画面では描かない（ShowHint==false）、ハブでは描く
//   (h) ハブの SNS でキーボードだけでアカウント切り替えに到達する（→ でフッタ／最後のカードで ↓ → Z）
//   実行: APPDATA=build/qa_story/escmenu_appdata で
//         Godot --headless --path . res://tools/qa_esc_menu.tscn
//         （--qa は付けない＝PauseMenu が自動プレイ扱いで無効になるため）
//         スクショ付き（窓あり）: Godot --path . res://tools/qa_esc_menu.tscn -- --esc-shot → build/shots_escmenu/
public partial class EscMenuQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private int _fails;
    private void Check(bool ok, string message)
    {
        if (ok) GD.Print($"[EscMenuQA] OK  {message}");
        else { _fails++; GD.PrintErr($"[EscMenuQA] NG  {message}"); }
    }
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value) => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static object HubMode(string name) => Enum.Parse(typeof(Hub).GetNestedType("Mode", BindingFlags.NonPublic)!, name);

    private PauseMenu _pause = null!;
    private GameManager _game = null!;
    // 窓ありで `-- --esc-shot` を付けたときだけ、問いのダイアログとフッタのカーソルを build/shots_escmenu/ に撮る
    //   （ヘッドレスでは FramePostDraw が来ないので撮らない）。
    private bool _shot;

    private async Task Shot(string name)
    {
        if (!_shot) return;
        string dir = ProjectSettings.GlobalizePath("res://build/shots_escmenu");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{dir}/{name}.png") == Error.Ok, $"screenshot {name}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), $"isolated save data ({OS.GetUserDataDir()})");
            _game = GetNode<GameManager>("/root/Game");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.MsgCharsPerSec = 300;
            _pause = GetNode<PauseMenu>("/root/PauseMenu");
            _pause.QuitOverride = () => { };
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--esc-shot") _shot = true;
            if (_shot)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            await Frames(2);

            await StageChecks();
            await CutsceneChecks();
            await HubChecks();
            await QuitChecks();
        }
        catch (Exception e)
        {
            _fails++;
            GD.PrintErr($"[EscMenuQA] EXCEPTION {e}");
        }
        GD.Print(_fails == 0 ? "[EscMenuQA] ALL PASS" : $"[EscMenuQA] {_fails} FAILURE(S)");
        _pause.QuitOverride = null;
        GetTree().Paused = false;
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    // ── (a)(g) 戦闘中 ──
    private async Task StageChecks()
    {
        var stage = await Swap("res://Akari.tscn", 30);
        Check(!_pause.ShowHint, "(g) battle: the M-menu hint is not drawn (ShowHint==false)");
        await Shot("battle_no_hint");
        await Press(Key.Escape);
        Check(_pause.IsOpen && GetTree().Paused, "(a) battle: Esc opens the menu and pauses");
        await Press(Key.Escape);
        Check(!_pause.IsOpen && !GetTree().Paused && GetTree().CurrentScene == stage, "(a) battle: Esc closes it and play resumes");
        // 会話中（BubblePaused）でも開く
        Hud.BubblePaused = true;
        await Press(Key.Escape);
        Check(_pause.IsOpen, "(a) battle: Esc opens the menu during a dialogue bubble too");
        await Press(Key.Escape);
        Hud.BubblePaused = false;
        Check(!_pause.IsOpen, "(a) and Esc closes it");
        Free(stage);
        await Frames(3);
    }

    // ── (b) カットシーン ──
    private async Task CutsceneChecks()
    {
        var log = GetNode<Backlog>("/root/Backlog");

        // Prologue
        var pro = await Swap("res://Prologue.tscn", 20);
        for (int i = 0; i < 3 && Read<int>(pro, "_phase") < 3; i++) await Press(Key.Z);
        await WaitUntil(() => Read<int>(pro, "_phase") == 3, 600);
        await Frames(30);
        Check(Read<int>(pro, "_phase") == 3, "(b) prologue reached the talk phase");
        int line0 = Read<int>(pro, "_line");
        await Press(Key.Escape);
        Check(_pause.IsOpen && GetTree().Paused, "(b) prologue: Esc opens the menu");
        string rows = string.Join("/", Array.ConvertAll(_pause.Rows, r => r.label));
        Check(rows == "リスタート/ログ/あそびかた/セーブ/ロード/タイトルへ", $"(b) prologue menu rows: {rows}");
        double t0 = Read<double>(pro, "_t");
        await Frames(30);
        Check(Read<double>(pro, "_t") == t0 && Read<int>(pro, "_line") == line0, "(b) prologue: timer and line are frozen while open");
        // 「閉じる」を Z で押す＝その Z が会話送りへ漏れないこと
        Write(_pause, "_sel", _pause.CloseIndex);
        await Press(Key.Z);
        Check(!_pause.IsOpen && !GetTree().Paused, "(b) prologue: the 閉じる button closes the menu");
        await Frames(5);
        Check(Read<int>(pro, "_line") == line0, $"(b) prologue: the Z that pressed 閉じる did not advance the talk (line {Read<int>(pro, "_line")})");
        Check(Read<double>(pro, "_t") > t0, "(b) prologue: the timer runs again after closing");
        await Press(Key.Escape);
        await Press(Key.Escape);
        Check(!_pause.IsOpen, "(b) prologue: Esc open → Esc close");
        for (int i = 0; i < 4 && Read<int>(pro, "_line") == line0; i++) await Press(Key.Z);
        Check(Read<int>(pro, "_line") > line0, $"(b) prologue: Z advances the talk after the menu ({line0} -> {Read<int>(pro, "_line")})");
        await Press(Key.L);
        Check(log.IsOpen, "(b) prologue: L opens the log after the menu");
        await Press(Key.Escape);
        await Frames(6);
        Check(!log.IsOpen && !_pause.IsOpen, "(b) prologue: Esc closes the log without opening the menu");
        // 長押し 0.45 秒（ヘッドレスはフレームが速いので、フレーム数ではなく「シーンが替わるまで」押し続ける）
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R, Pressed = true });
        await WaitUntil(() => GetTree().CurrentScene != pro, 3000);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R, Pressed = false });
        await Frames(5);
        Check(GetTree().CurrentScene != pro && (GetTree().CurrentScene?.SceneFilePath ?? "") == "res://Prologue.tscn",
            "(b) prologue: holding R still restarts the scene");
        Free(GetTree().CurrentScene!);
        await Frames(3);

        // Epilogue（E5b の語り）
        var epi = await Swap("res://Epilogue.tscn", 20);
        await Frames(20);
        int eLine0 = Read<int>(epi, "_line");
        await Press(Key.Escape);
        Check(_pause.IsOpen && GetTree().Paused, "(b) epilogue: Esc opens the menu");
        double et0 = Read<double>(epi, "_t");
        await Frames(30);
        Check(Read<double>(epi, "_t") == et0, "(b) epilogue: timer frozen while open");
        await Press(Key.Escape);
        Check(!_pause.IsOpen && !GetTree().Paused, "(b) epilogue: Esc closes the menu");
        await Frames(10);
        Check(Read<double>(epi, "_t") > et0, "(b) epilogue: timer runs again");
        for (int i = 0; i < 4 && Read<int>(epi, "_line") == eLine0; i++) await Press(Key.Z);
        Check(Read<int>(epi, "_line") > eLine0, $"(b) epilogue: Z advances the lines after the menu ({eLine0} -> {Read<int>(epi, "_line")})");
        Free(epi);
        await Frames(3);

        // Final
        var fin = await Swap("res://Final.tscn", 20);
        await Press(Key.Escape);
        Check(_pause.IsOpen && GetTree().Paused, "(b) final: Esc opens the menu");
        double ft0 = Read<double>(fin, "_t");
        await Frames(20);
        Check(Read<double>(fin, "_t") == ft0, "(b) final: timer frozen while open");
        await Press(Key.Escape);
        await Frames(5);
        Check(!_pause.IsOpen && Read<double>(fin, "_t") > ft0, "(b) final: closes and resumes");
        Free(fin);
        await Frames(3);

        // Credits は除外のまま
        var cred = await Swap("res://Credits.tscn", 10);
        await Press(Key.M);
        Check(!_pause.IsOpen, "(b) credits: the menu stays excluded");
        Free(cred);
        await Frames(3);
    }

    // ── (c)(g)(h) ハブ ──
    private async Task HubChecks()
    {
        var hub = await Swap("res://Hub.tscn", 40);
        Write(hub, "_mode", HubMode("Home"));
        await Frames(3);
        Check(_pause.ShowHint, "(g) hub: the M-menu hint is drawn");
        await Press(Key.Escape);
        Check(!_pause.IsOpen && Read<object>(hub, "_mode").Equals(HubMode("Home")), "(c) hub home: Esc does not open the menu");
        await Press(Key.M);
        Check(_pause.IsOpen, "(c) hub home: M opens the menu");
        await Press(Key.M);
        Check(!_pause.IsOpen, "(c) M closes it");
        await Frames(3);

        // SNS（カード）
        Write(hub, "_mode", HubMode("Cards"));
        Write(hub, "_footSel", -1);
        await Frames(3);
        await Press(Key.Escape);
        Check(!_pause.IsOpen && Read<object>(hub, "_mode").Equals(HubMode("Home")), "(c) hub SNS: Esc still goes back home");

        Write(hub, "_mode", HubMode("Cards"));
        Write(hub, "_footSel", -1);
        await Frames(3);
        await Action("ui_right");
        int fs = Read<int>(hub, "_footSel");
        Check(fs >= 0, $"(h) → moves the cursor down to the footer (footSel={fs})");
        await Shot("hub_footer_account");
        await Press(Key.Z);
        Check(Read<object>(hub, "_mode").Equals(HubMode("Job")), "(h) Z on the footer opens the account switcher");
        await Seconds(0.25);   // 切り替え画面は開いて 0.15 秒は閉じる入力を受けない（ProcessJob の _jobT）
        await Press(Key.X);
        Check(Read<object>(hub, "_mode").Equals(HubMode("Cards")), "(h) X returns to the SNS");
        await Action("ui_up");
        Check(Read<int>(hub, "_footSel") == -1, "(h) ↑ returns the cursor to the cards");

        var entries = Read<Array>(hub, "_entries");
        Write(hub, "_sel", entries.Length - 1);
        await Frames(2);
        await Action("ui_down");
        Check(Read<int>(hub, "_footSel") >= 0, "(h) ↓ on the last card also reaches the footer");
        await Action("ui_left");
        await Action("ui_right");
        Check(Read<int>(hub, "_footSel") == fs, "(h) ←→ cycles the footer items back to アカウント");
        // パッドの決定（ui_accept）でも同じ
        await Action("ui_accept");
        Check(Read<object>(hub, "_mode").Equals(HubMode("Job")), "(h) ui_accept (pad A) on the footer opens the account switcher");
        await Seconds(0.25);
        await Press(Key.Escape);
        Check(Read<object>(hub, "_mode").Equals(HubMode("Cards")) && !_pause.IsOpen, "(h) Esc closes the switcher (menu stays shut)");
        Write(hub, "_footSel", -1);
        await Press(Key.J);
        Check(Read<object>(hub, "_mode").Equals(HubMode("Job")), "(h) J still opens the switcher directly");
        await Seconds(0.25);
        await Press(Key.X);
        Free(hub);
        await Frames(3);
    }

    // ── (d)(e)(f) セーブ確認 ──
    private async Task QuitChecks()
    {
        // (e) タイトルへ → キャンセル
        var stage = await Swap("res://Akari.tscn", 30);
        await Press(Key.M);
        int titleRow = Array.FindIndex(_pause.Rows, r => r.act == PauseMenu.Act.Title);
        Write(_pause, "_sel", titleRow);
        await Press(Key.Z);
        Check(_pause.AskOpen && _pause.AskKind == PauseMenu.QuitKind.Title, "(d) タイトルへ opens the save question");
        Check(_pause.AskText == "タイトルへ戻る前に、セーブしますか？", $"(d) wording: {_pause.AskText}");
        string labels = string.Join("/", Array.ConvertAll(_pause.AskChoices, c => _pause.AskLabel(c)));
        Check(labels == "セーブする/セーブしない/キャンセル", $"(d) choices: {labels}");
        Check(_pause.AskSel == 0, "(d) default is セーブする");
        await Shot("ask_title");
        await Press(Key.Escape);
        Check(!_pause.AskOpen && _pause.IsOpen && GetTree().CurrentScene == stage, "(e) Esc cancels back to the menu");
        await Press(Key.Z);
        await Action("ui_right");
        await Action("ui_right");
        Check(_pause.AskSel == 2, "(e) → moves to キャンセル");
        await Press(Key.Z);
        Check(!_pause.AskOpen && _pause.IsOpen, "(e) Z on キャンセル returns to the menu");

        // (d) セーブする → スロット → キャンセルで問いへ戻る → スロット3へ保存 → タイトルへ
        await Press(Key.Z);
        await Press(Key.Z);
        Check(_pause.SlotOpen && _pause.SlotForSave && !_pause.AskOpen, "(d) セーブする opens the save slot picker");
        await Press(Key.X);
        Check(!_pause.SlotOpen && _pause.AskOpen, "(e) cancelling the slot picker returns to the save question");
        await Press(Key.Z);
        Check(!_pause.SlotFilled(3), "(d) slot 3 starts empty");
        Write(_pause, "_slotSel", 2);
        await Press(Key.Z);
        await Frames(10);
        Check(_pause.SlotFilled(3), "(d) the save was written to slot 3");
        Check((GetTree().CurrentScene?.SceneFilePath ?? "") == "res://TitleMenu.tscn",
            $"(d) then went to the title (scene={GetTree().CurrentScene?.SceneFilePath})");
        Check(!_pause.IsOpen && !GetTree().Paused, "(d) menu closed and tree unpaused");

        // (f) タイトル画面の×＝確認なしで終了
        Reset();
        _pause.Notification((int)Node.NotificationWMCloseRequest);
        await Frames(2);
        Check(_pause.QuitRequested && !_pause.IsOpen, "(f) title screen: × quits immediately without asking");
        Free(GetTree().CurrentScene!);
        await Frames(3);

        // (f) 戦闘中の×＝ポーズして問い → キャンセルで元へ
        stage = await Swap("res://Akari.tscn", 30);
        Reset();
        _pause.Notification((int)Node.NotificationWMCloseRequest);
        await Frames(3);
        Check(_pause.IsOpen && _pause.AskOpen && _pause.AskKind == PauseMenu.QuitKind.Exit && GetTree().Paused,
            "(f) battle: × pauses and shows the save question");
        labels = string.Join("/", Array.ConvertAll(_pause.AskChoices, c => _pause.AskLabel(c)));
        Check(labels == "セーブして終了/セーブせずに終了/キャンセル", $"(f) exit choices: {labels}");
        await Shot("ask_exit_battle");
        Check(!_pause.QuitRequested, "(f) nothing quit yet");
        await Press(Key.Escape);
        Check(!_pause.IsOpen && !GetTree().Paused, "(f) cancel closes everything and resumes the battle");
        // セーブせずに終了
        _pause.Notification((int)Node.NotificationWMCloseRequest);
        await Frames(3);
        await Action("ui_right");
        await Press(Key.Z);
        Check(_pause.QuitRequested, "(f) セーブせずに終了 requests the quit");
        // セーブして終了（スロット1）
        Reset();
        _pause.Notification((int)Node.NotificationWMCloseRequest);
        await Frames(3);
        await Press(Key.Z);
        Write(_pause, "_slotSel", 0);
        await Press(Key.Z);
        Check(_pause.SlotFilled(1) && _pause.QuitRequested, "(f) セーブして終了 writes slot 1 then quits");
        // メニューを開いている最中の×＝メニューの上に重ね、キャンセルでメニューへ戻る
        Reset();
        await Press(Key.M);
        _pause.Notification((int)Node.NotificationWMCloseRequest);
        await Frames(3);
        Check(_pause.IsOpen && _pause.AskOpen, "(f) × over an open menu stacks the question on top");
        await Press(Key.X);
        Check(_pause.IsOpen && !_pause.AskOpen, "(f) cancel returns to the still-open menu");
        await Press(Key.M);
        Free(stage);
        await Frames(3);

        // (f) ハブのホーム
        var hub = await Swap("res://Hub.tscn", 40);
        Write(hub, "_mode", HubMode("Home"));
        Reset();
        _pause.Notification((int)Node.NotificationWMCloseRequest);
        await Frames(3);
        Check(_pause.AskOpen && _pause.AskChoices.Length == 3, "(f) hub home: × shows the 3-way save question");
        await Press(Key.Escape);
        Check(!_pause.IsOpen, "(f) hub: cancel closes it");
        Free(hub);
        await Frames(3);

        // (f) トレーニング＝セーブできない＝2択
        var tr = await Swap("res://Training.tscn", 30);
        Reset();
        _pause.Notification((int)Node.NotificationWMCloseRequest);
        await Frames(3);
        labels = string.Join("/", Array.ConvertAll(_pause.AskChoices, c => _pause.AskLabel(c)));
        Check(_pause.AskOpen && labels == "セーブせずに終了/キャンセル", $"(f) training: 2 choices ({labels})");
        await Shot("ask_exit_training");
        await Press(Key.Z);
        Check(_pause.QuitRequested, "(f) training: セーブせずに終了 quits");
        Free(tr);
        await Frames(3);
    }

    private void Reset() => typeof(PauseMenu).GetProperty("QuitRequested")!.SetValue(_pause, false);

    private async Task<Node> Swap(string path, int settle)
    {
        var node = GD.Load<PackedScene>(path).Instantiate();
        GetTree().Root.AddChild(node);
        GetTree().CurrentScene = node;
        await Frames(settle);
        return node;
    }

    private static void Free(Node n) { if (IsInstanceValid(n)) n.QueueFree(); }

    private async Task Press(Key key) => await Hold(key, 3);

    private async Task Hold(Key key, int frames)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(frames);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task Action(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        await Frames(3);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Seconds(double s)
    {
        ulong end = Time.GetTicksMsec() + (ulong)(s * 1000);
        while (Time.GetTicksMsec() < end) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task WaitUntil(Func<bool> cond, int maxFrames)
    {
        for (int i = 0; i < maxFrames && !cond(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
