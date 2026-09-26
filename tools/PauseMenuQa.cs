using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// PauseMenuQa : 作り直したポーズメニュー（2026-09-17）の自動検証。
//   新設のトップ構成・確認ダイアログ（はい/いいえ）・スロット選択（セーブ/ロード）・歯車の設定ページを、
//   キーボード / パッド / マウスの3経路すべてで叩いて壊れていないことを確かめる。
//   2026-09-22：「あそびかた」行の復帰に追随（ステージ内7行／ステージ外4行）。あそびかた→閉じる→ポーズへ戻る
//   （Esc／X のどちらで閉じても同じ押下でポーズまで閉じない）をステージ内・ハブの両方で見る。
//   実行: Godot --headless --path . res://tools/qa_pause_menu.tscn -- --qa-pause
//   ※セーブを実際に書くので、user データは build/qa_pause/ へ隔離した状態で走らせること
//     （--userdata build/qa_pause 相当の起動をラッパ側で用意する）。
public partial class PauseMenuQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static object? Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);

    private int _fails;
    private void Check(bool ok, string message)
    {
        if (ok) GD.Print($"[PauseQA] PASS {message}");
        else { _fails++; GD.PrintErr($"[PauseQA] FAIL {message}"); }
    }

    private PauseMenu _pause = null!;

    public override async void _Ready()
    {
        try
        {
            _pause = GetNode<PauseMenu>("/root/PauseMenu");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            // このツールは手動セーブを実際に書く＝開発機の user:// のスロットを潰してしまう。
            //   走行前に 0..3 を .qabak へ退避し、最後（Finish）で必ず書き戻す。
            //   「空きスロットはロードできない」の検証に空きが要るので、退避後は消した状態から始める。
            StashSlots();
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            await Frames(2);

            // ステージ（Akari）を立ててポーズを開く＝トップに6行そろう構成で検証する。
            var stage = GD.Load<PackedScene>("res://Akari.tscn").Instantiate();
            GetTree().Root.AddChild(stage);
            GetTree().CurrentScene = (Node)stage;
            await Frames(30);

            Check(_pause.RetryEnabled, "stage scene enables the run-only rows");
            // 2026-09-26：開くキーは Esc → M。Esc は「一つ前へもどる」（トップでは閉じる）。M はトグル。
            // 2026-09-27：戦闘画面では Esc でも開く（スマホ系の画面だけは Esc＝もどる のまま）。
            await Press(Key.Escape);
            Check(_pause.IsOpen, "Esc opens the pause menu in a stage (2026-09-27)");
            await Press(Key.Escape);
            Check(!_pause.IsOpen, "Esc on the top page closes it again");
            await OpenPause();
            Check(_pause.IsOpen, "M opens the pause menu");
            await Press(Key.Escape);
            Check(!_pause.IsOpen && !GetTree().Paused, "Esc on the top page closes the menu (one step back)");
            await OpenPause();
            await Press(Key.M);
            Check(!_pause.IsOpen, "M toggles the open menu closed");
            await OpenPause();
            Check(_pause.RowCount == 7, $"in-stage top shows 7 rows (got {_pause.RowCount})");
            string labels = string.Join("/", System.Array.ConvertAll(_pause.Rows, r => r.label));
            Check(labels == "離脱/リスタート/ログ/あそびかた/セーブ/ロード/タイトルへ", $"row order/labels: {labels}");

            // ── あそびかた（2026-09-22 復帰）：ポーズを保ったままオーバーレイが重なり、Esc で閉じるとポーズへ戻る ──
            var how = GetNode<HowToPlay>("/root/HowTo");
            SetSel(3);
            await Press(Key.Z);
            Check(how.IsOpen, "あそびかた opens the HowTo overlay from the in-stage menu");
            Check(_pause.IsOpen && GetTree().Paused, "the pause menu stays open and the tree stays paused under HowTo");
            await Press(Key.Escape);
            Check(!how.IsOpen, "Esc closes HowTo");
            Check(_pause.IsOpen && GetTree().Paused, "closing HowTo with Esc returns to the still-open pause menu");
            await Frames(3);

            // ── 確認ダイアログ（キーボード）──
            // 「リスタート」をZで開き、既定が いいえ であること・キャンセルで閉じることを見る。
            SetSel(1);
            await Press(Key.Z);
            Check(_pause.ConfirmOpen, "Restart opens a confirmation dialog");
            Check(_pause.ConfirmText == "本当に最初からでよろしいですか？", $"restart wording: {_pause.ConfirmText}");
            Check(!_pause.ConfirmYes, "confirmation defaults to いいえ");
            await Action("ui_right");
            Check(_pause.ConfirmYes, "arrow keys move the confirmation cursor to はい");
            await Press(Key.X);
            Check(!_pause.ConfirmOpen && _pause.IsOpen, "X cancels the confirmation without leaving the menu");

            // 「離脱」も同様に確認を挟む（文言違い）。決定は "ui_accept" で叩く＝InputMap でパッドAが
            //   割り当たっている共有アクションなので、パッドの決定と同じ経路を通る
            //   （ヘッドレスには実機パッドが無く Input.GetConnectedJoypads が空＝Pad.Pressed は試せない）。
            SetSel(0);
            await Action("ui_accept");
            Check(_pause.ConfirmOpen, "ui_accept (pad A) opens the Leave confirmation");
            Check(_pause.ConfirmText == "本当に離脱しますか？", $"leave wording: {_pause.ConfirmText}");
            await Action("ui_up");
            Check(_pause.ConfirmYes, "up/down also moves the confirmation cursor");
            await Press(Key.Escape);
            Check(!_pause.ConfirmOpen && _pause.IsOpen, "Esc cancels the confirmation without closing the menu");

            // ── スロット選択：セーブ（マウスクリック）──
            Check(!_pause.SlotFilled(2), "slot 2 starts empty");
            ClickAt(PauseMenu.RowRect(4, 7), "ProcessTop");   // 「セーブ」行
            Check(_pause.SlotOpen && _pause.SlotForSave, "clicking セーブ opens the save slot picker");
            ClickAt(PauseMenu.SlotRowRect(1), "ProcessSlots"); // スロット2
            Check(!_pause.SlotOpen, "clicking a slot closes the picker");
            Check(_pause.SlotFilled(2), "saving to slot 2 writes the file");
            Check(_pause.SavedText.Contains("スロット2"), $"save toast: {_pause.SavedText}");

            // ── スロット選択：ロード（空スロットは選べない／埋まっていれば実際に復元する）──
            SetImpression(game, 12345);   // ロードで上書きされることの目印（setter は private）
            SetSel(5);                    // 「ロード」行
            await Press(Key.Z);
            Check(_pause.SlotOpen && !_pause.SlotForSave, "ロード opens the load slot picker");
            Check(_pause.SlotSel == 1, "load cursor starts on the first filled slot");
            SetSlotSel(0);                // スロット1＝空
            await Press(Key.Z);
            Check(_pause.SlotOpen, "an empty slot cannot be loaded (picker stays open)");
            SetSlotSel(1);                // スロット2＝保存済み
            await Press(Key.Z);
            await Frames(30);
            Check(game.Impression != 12345, $"loading slot 2 restored the saved state (Impression={game.Impression})");
            Check((GetTree().CurrentScene?.SceneFilePath ?? "").Contains("Hub"),
                $"loading moves to the hub like タイトル/つづきから (scene={GetTree().CurrentScene?.SceneFilePath})");

            // ── ステージ外（ハブ）ではラン専用の3行が消える ──
            await OpenPause();
            Check(!_pause.RetryEnabled, "hub is a non-combat screen");
            Check(_pause.RowCount == 4, $"outside a stage the top shows 4 rows (got {_pause.RowCount})");
            string outside = string.Join("/", System.Array.ConvertAll(_pause.Rows, r => r.label));
            Check(outside == "あそびかた/セーブ/ロード/タイトルへ", $"outside rows: {outside}");

            // ── ハブでも あそびかた が開き、X で閉じてもポーズへ戻る（同じ X の押下でポーズまで閉じない）──
            ClickAt(PauseMenu.RowRect(0, 4), "ProcessTop");   // 「あそびかた」行
            Check(how.IsOpen, "clicking あそびかた on the hub opens HowTo");
            Check(_pause.IsOpen && GetTree().Paused, "the hub pause menu stays open under HowTo");
            await Press(Key.X);
            Check(!how.IsOpen, "X closes HowTo");
            Check(_pause.IsOpen && GetTree().Paused, "closing HowTo with X returns to the still-open pause menu");
            await Frames(3);

            // ── 歯車 → 設定 → もどる（矢印キーだけで歯車まで到達できること）──
            SetSel(0);
            for (int i = 0; i < _pause.GearIndex; i++) await Action("ui_down");
            Check(_pause.Sel == _pause.GearIndex, $"arrow keys reach the gear (sel={_pause.Sel})");
            await Press(Key.Z);
            Check(_pause.CurrentPage == PauseMenu.Page.Settings, "the gear opens the settings page");

            float before = _pause.VolValue(0);
            SetSel(0);
            await Action("ui_left");
            Check(Mathf.Abs(_pause.VolValue(0) - (before - 5f)) < 0.01f,
                $"left arrow lowers the master volume ({before} -> {_pause.VolValue(0)})");
            Check(Mathf.Abs(AudioConfig.Get("master") - _pause.VolValue(0)) < 0.01f, "the volume change is persisted");

            SetSel(PauseMenu.ScreenRowIndex);
            int modeBefore = _pause.ScreenMode;
            await Press(Key.Z);
            Check(_pause.ScreenMode != modeBefore, "Z cycles the screen mode");
            // 実ウィンドウへの反映はヘッドレス（DisplayServer=dummy）では読み戻せないので、
            //   ここでは保存側だけを見る。実窓での効き目は実機起動で別途確認すること。
            if (DisplayServer.GetName() != "headless")
            {
                bool full = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen
                            or DisplayServer.WindowMode.ExclusiveFullscreen;
                Check(full == (_pause.ScreenMode == 1), "the screen mode is applied to the real window");
            }
            Check(AudioConfig.GetInt("mode", -1) == _pause.ScreenMode, "the screen mode is persisted to settings.json");
            await Press(Key.Z); // 元へ戻す

            await Press(Key.X);
            Check(_pause.CurrentPage == PauseMenu.Page.Top && _pause.IsOpen, "X returns from settings to the top page");
            Check(_pause.Sel == _pause.GearIndex, "the cursor returns to the gear it came from");

            // ── 閉じる（下部中央のボタン）──
            SetSel(_pause.CloseIndex);
            await Press(Key.Z);
            Check(!_pause.IsOpen, "the 閉じる button closes the menu");
            Check(!GetTree().Paused, "closing unpauses the tree");
        }
        catch (Exception e)
        {
            _fails++;
            GD.PrintErr($"[PauseQA] EXCEPTION {e}");
        }
        RestoreSlots();
        GD.Print(_fails == 0 ? "[PauseQA] ALL PASS" : $"[PauseQA] {_fails} FAILURE(S)");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    // ── 開発機のセーブスロットの退避 / 復元 ──
    private static string Slot(int i) => ProjectSettings.GlobalizePath($"user://save_{i}.json");
    private static string Bak(int i) => ProjectSettings.GlobalizePath($"user://save_{i}.json.qabak");

    private static void StashSlots()
    {
        for (int i = 0; i <= GameManager.SlotCount; i++)
        {
            if (FileAccess.FileExists(Bak(i))) DirAccess.RemoveAbsolute(Bak(i)); // 前回の中断ぶんは捨てる
            if (FileAccess.FileExists(Slot(i))) DirAccess.RenameAbsolute(Slot(i), Bak(i));
        }
    }

    private static void RestoreSlots()
    {
        for (int i = 0; i <= GameManager.SlotCount; i++)
        {
            if (FileAccess.FileExists(Slot(i))) DirAccess.RemoveAbsolute(Slot(i)); // 検証で書いたものを消す
            if (FileAccess.FileExists(Bak(i))) DirAccess.RenameAbsolute(Bak(i), Slot(i));
        }
    }

    // ── 内部状態の直接操作（カーソル位置だけを置く。決定は必ず入力経路で叩く）──
    private void SetSel(int v) => _pause.GetType().GetField("_sel", Private)!.SetValue(_pause, v);
    // Impression は private setter なので自動プロパティの backing field を直接書く。
    private static void SetImpression(GameManager g, long v)
        => typeof(GameManager).GetProperty("Impression")!.SetValue(g, v);
    private void SetSlotSel(int v) => _pause.GetType().GetField("_slotSel", Private)!.SetValue(_pause, v);

    private async Task OpenPause()
    {
        if (_pause.IsOpen) return;
        await Press(Key.M);
    }

    private async Task Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task Action(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        await Frames(3);
    }

    // マウス：デスクトップのカーソルを動かさずに Pad の内部状態へ設計座標の押下を差し込み、
    //   PauseMenu の該当ハンドラ（ProcessTop / ProcessSlots / ProcessConfirm / ProcessSettings）を直に呼ぶ。
    //   _Process 経由にできないのは Pad.PollMouse が毎フレーム実マウスで _mousePos/_mL を上書きするため
    //   （HubJobQa.Click も同じ理由でハンドラ直呼び）。
    private static void PadField(string name, object value)
        => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    private void ClickAt(Rect2 rect, string handler)
    {
        Vector2 previous = Pad.MousePos();
        PadField("_mousePos", rect.GetCenter());
        PadField("_usingMouse", true);
        PadField("_mL", true);
        PadField("_mLPrev", false);
        Call(_pause, handler, false, false);   // zEdge=false, cancel=false（押したのはマウスだけ）
        PadField("_mousePos", previous);
        PadField("_mL", false);
        PadField("_mLPrev", false);
        PadField("_usingMouse", false);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
