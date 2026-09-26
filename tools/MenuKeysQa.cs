using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// MenuKeysQa : 2026-09-26 のキー変更（M＝メニュー／Esc＝一つ前へ／会話ログを全シーンで）の自動検証＋スクショ。
//   ・プロローグ冒頭で L を押すとログが開く（空でも開く）→ Esc で閉じる → 会話行が積まれる → 再び開ける
//   ・戦闘中：Esc／M でメニューが開き（Esc は 2026-09-27 から）、Esc で一段ずつ戻って閉じる／M はトグル
//   ・ハブ：SNS カードで Esc → ホームへ。ホームで Esc → 何もしない。会話行が積まれる
//   ・ショップ：Esc でハブへ
//   実行: Godot --path . res://tools/qa_menu_keys.tscn（ウィンドウ表示。APPDATA を build/qa_story/menu_appdata へ
//         向けて実セーブに触れない。--qa は付けない＝Backlog / PauseMenu が自動プレイ扱いで無効化されるため）
//   スクショ: build/shots_menu/*.png
public partial class MenuKeysQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private int _fails;
    private string _out = "";
    private void Check(bool ok, string message)
    {
        if (ok) GD.Print($"[MenuKeysQA] PASS {message}");
        else { _fails++; GD.PrintErr($"[MenuKeysQA] FAIL {message}"); }
    }
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value) => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static object HubMode(string name) => Enum.Parse(typeof(Hub).GetNestedType("Mode", BindingFlags.NonPublic)!, name);

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath("res://build/shots_menu");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.MsgCharsPerSec = 300;
            var pause = GetNode<PauseMenu>("/root/PauseMenu");
            var log = GetNode<Backlog>("/root/Backlog");
            await Frames(2);

            // ── 1. プロローグ ──
            var pro = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
            GetTree().Root.AddChild(pro);
            GetTree().CurrentScene = pro;
            await Frames(20);
            Check(Hud.Backlog.Count == 0, "prologue starts with an empty backlog");
            await Press(Key.L);
            Check(log.IsOpen && GetTree().Paused, "L opens the backlog at the very start of the prologue (empty log)");
            await Shot("prologue_log_empty");
            await Press(Key.Escape);
            await Frames(4);
            Check(!log.IsOpen && !GetTree().Paused, "Esc closes the backlog and the prologue resumes");
            for (int i = 0; i < 3 && Read<int>(pro, "_phase") < 3; i++) await Press(Key.Z);
            await WaitUntil(() => Read<int>(pro, "_phase") == 3, 600);
            await Frames(20);
            var talk = Read<System.Collections.IList>(pro, "_talk");
            string first = (string)talk[0]!.GetType().GetField("Text")!.GetValue(talk[0])!;
            Check(Hud.Backlog.Count > 0 && Hud.Backlog[0].Text == first, $"first prologue line is logged (\"{first}\")");
            await Press(Key.L);
            Check(log.IsOpen && GetTree().Paused, "L opens the backlog during the prologue talk and pauses it");
            await Shot("prologue_log");
            await Press(Key.Escape);
            await Frames(4);
            Check(!log.IsOpen && !GetTree().Paused, "Esc closes the backlog during the prologue");
            // 2026-09-27：カットシーンでもメニューが開く（M／Esc）。閉じれば会話はそのまま続く。
            await Press(Key.M);
            Check(pause.IsOpen && GetTree().Paused, "M opens the pause menu in the prologue (2026-09-27)");
            await Press(Key.M);
            Check(!pause.IsOpen && !GetTree().Paused, "M closes it again");
            await Press(Key.Escape);
            Check(pause.IsOpen && GetTree().Paused, "Esc opens the pause menu in the prologue talk");
            await Press(Key.Escape);
            Check(!pause.IsOpen && GetTree().CurrentScene == pro && Read<int>(pro, "_phase") == 3,
                "Esc closes it and the prologue talk continues");
            pro.QueueFree();
            await Frames(3);

            // ── 2. 戦闘中 ──
            var stage = GD.Load<PackedScene>("res://Akari.tscn").Instantiate();
            GetTree().Root.AddChild(stage);
            GetTree().CurrentScene = (Node)stage;
            await Frames(30);
            await Press(Key.Escape);
            Check(pause.IsOpen && GetTree().CurrentScene == stage, "Esc in battle opens the menu (2026-09-27)");
            await Press(Key.Escape);
            Check(!pause.IsOpen && !GetTree().Paused, "Esc closes it again");
            await Press(Key.M);
            Check(pause.IsOpen && GetTree().Paused, "M opens the pause menu in battle");
            await Shot("stage_menu_open");
            Write(pause, "_sel", pause.GearIndex);
            await Press(Key.Z);
            Check(pause.CurrentPage == PauseMenu.Page.Settings, "gear opens the settings page");
            await Press(Key.Escape);
            Check(pause.IsOpen && pause.CurrentPage == PauseMenu.Page.Top, "Esc steps back from settings to the top page");
            await Press(Key.Escape);
            Check(!pause.IsOpen && !GetTree().Paused, "Esc on the top page closes the menu and resumes");
            await Shot("stage_menu_closed");
            await Press(Key.M);
            await Press(Key.M);
            Check(!pause.IsOpen, "M toggles the menu open and closed");
            await Press(Key.L);
            Check(log.IsOpen, "L opens the backlog in battle");
            await Press(Key.Escape);
            await Frames(4);
            Check(!log.IsOpen && !pause.IsOpen, "Esc closes the backlog without opening the menu");
            stage.QueueFree();
            await Frames(3);

            // ── 3. ハブ ──
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(40);
            Write(hub, "_mode", HubMode("Cards"));
            await Frames(3);
            await Shot("hub_cards");
            await Press(Key.Escape);
            Check(Read<object>(hub, "_mode").Equals(HubMode("Home")), "Esc on the SNS cards returns to the home screen");
            await Shot("hub_home_after_esc");
            await Press(Key.Escape);
            Check(GetTree().CurrentScene == hub && Read<object>(hub, "_mode").Equals(HubMode("Home")), "Esc on the hub home does nothing");
            Check(!pause.IsOpen, "Esc on the hub does not open the menu");
            int before = Hud.Backlog.Count;
            Call(hub, "OpenHomeApp", 0);
            await WaitUntil(() => Read<object>(hub, "_mode").Equals(HubMode("Dialogue")), 300);
            if (Read<object>(hub, "_mode").Equals(HubMode("Dialogue")))
            {
                await Frames(5);
                Check(Hud.Backlog.Count > before, $"hub dialogue lines are logged ({before} -> {Hud.Backlog.Count})");
                var dlg = Read<(string sp, string tx)[]>(hub, "_dlg");
                Check(Hud.Backlog[^1].Text == dlg[0].tx && Hud.Backlog[^1].Speaker == dlg[0].sp,
                    $"logged hub line matches the shown line ({dlg[0].sp}: {dlg[0].tx})");
                await Press(Key.L);
                Check(log.IsOpen, "L opens the backlog during a hub dialogue");
                await Shot("hub_dialogue_log");
                await Press(Key.Escape);
                await Frames(4);
                Check(!log.IsOpen, "Esc closes the backlog on the hub");
            }
            else Check(false, "hub SNS intro dialogue did not start (cannot verify hub logging)");
            hub.QueueFree();
            await Frames(3);

            // ── 4. ショップ ──
            var shop = GD.Load<PackedScene>("res://Shop.tscn").Instantiate();
            GetTree().Root.AddChild(shop);
            GetTree().CurrentScene = (Node)shop;
            await Frames(30);
            await Shot("shop");
            await Press(Key.Escape);
            await WaitUntil(() => (GetTree().CurrentScene?.SceneFilePath ?? "").Contains("Hub"), 300);
            Check((GetTree().CurrentScene?.SceneFilePath ?? "").Contains("Hub"),
                $"Esc in the shop returns to the hub (scene={GetTree().CurrentScene?.SceneFilePath})");
            await Frames(10);
            await Shot("shop_esc_back_to_hub");
        }
        catch (Exception e)
        {
            _fails++;
            GD.PrintErr($"[MenuKeysQA] EXCEPTION {e}");
        }
        GD.Print(_fails == 0 ? "[MenuKeysQA] ALL PASS" : $"[MenuKeysQA] {_fails} FAILURE(S)");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    private async Task Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task WaitUntil(Func<bool> cond, int maxFrames)
    {
        for (int i = 0; i < maxFrames && !cond(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
