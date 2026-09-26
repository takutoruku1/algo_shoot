using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// 検証ハーネス（QA・2026-09-26）：
//   ① ロックオン**モード**の保持 — 作者指示「画面から敵が消えてもロックオン解除しないで」。
//      対象が画面外へ出た／倒された／敵が一体も居ない、のどれでもモード（Player.LockArmed）は落ちず、
//      次に現れた最寄りの敵へ自動で付く。落ちるのは解除入力だけ。
//   ② 会話停止中の自機の奥回し — 作者指摘「吹き出しの上に乗っていたら、動かせないのに読めなくなる」。
//      Hud.BubblePaused のあいだだけ Player.ZIndex が吹き出し（BubbleLayer -7）の下（-8）へ落ち、明けたら 10 に戻る。
//   使い方：
//     ヘッドレス : Godot --headless --path . res://tools/qa_lock_mode.tscn
//     スクショ付き（窓あり。Shot はヘッドレスで固まる）:
//                  Godot --path . res://tools/qa_lock_mode.tscn -- --lm-shot --lm-out <絶対パス>
public partial class LockModeQa : Node
{
    private const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Write(object o, string n, object v) => o.GetType().GetField(n, P)!.SetValue(o, v);

    private int _fail;
    private bool _shot;
    private string _out = "build/shots_lock";

    public override async void _Ready()
    {
        // ※ 全体の --shot（ShotTool 自動ロード＝撮って終了する）と衝突しないよう、専用の旗にしてある。
        foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--lm-shot") _shot = true;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--lm-out") _out = args[i + 1];
        if (_shot) DirAccess.MakeDirRecursiveAbsolute(_out);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            await Run();
            GD.Print(_fail == 0 ? "[LM] DONE ok" : $"[LM] DONE fail={_fail}");
            GetTree().Quit(_fail == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError($"[LM] FAIL {ex}"); GetTree().Quit(1); }
    }

    private async Task Run()
    {
        var game = GetNode<GameManager>("/root/Game");
        game.ResetPersistent(); game.AutoSaveEnabled = false;

        var root = GD.Load<PackedScene>("res://Rei.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        await Frames(2);
        GetTree().CurrentScene = root;
        await Frames(8);

        (root.GetType().GetProperty("Stage")!.GetValue(root) as Node)?.SetProcess(false);
        var world = FindNamed<Node2D>(root, "World") ?? root;
        var player = FindNode<Player>(root);
        var hud = FindNode<Hud>(root);
        if (player == null || hud == null) { GD.Print("[LM] NG player/hud not found"); _fail++; return; }
        Write(player, "_invincible", true); Write(player, "_invincibleTimer", 999f);
        player.GlobalPosition = new Vector2(Field.Left + 60, 108);
        _hud = hud;
        hud.HoldBubble = false; hud.HideBubble();
        await Frames(4);
        GD.Print($"[LM] BubblePaused={Hud.BubblePaused} bubbles={(hud.Bubbles != null)}");

        // ── ① ロックオンモードの保持 ──
        var spec = EnemyTable.For(StageTheme.Rei).Item1;
        var e1 = Spawn(world, spec, new Vector2(Field.Left + 140, 108));
        var e2 = Spawn(world, spec, new Vector2(Field.Left + 220, 108));
        await Frames(4);
        if (!IsInstanceValid(e1) || !IsInstanceValid(e2)) { GD.Print("[LM] NG enemy despawned"); _fail++; return; }

        Check("最初は OFF（モードなし）", !player.LockArmed && !player.LockedOn);
        await Tap(Key.S);
        Check("S で追尾（掴む＋モード）", player.LockedOn && player.LockArmed);
        Check("追尾先は最寄り e1", ReferenceEquals(player.LockTarget, e1));
        await Shot("lock_tracking");

        // 対象も他の敵も画面外へ → 掴みは外れるがモードは残る（待機）
        e1.GlobalPosition = new Vector2(Field.Right + 400, 108);
        e2.GlobalPosition = new Vector2(Field.Right + 480, 108);
        await Frames(4);
        Check("全員が画面外：掴みは外れる", !player.LockedOn);
        Check("全員が画面外：モードは残る（待機）", player.LockArmed);
        await Shot("lock_waiting");

        // 新しい敵が現れる → 自動で付く
        var e3 = Spawn(world, spec, new Vector2(Field.Left + 180, 120));
        await Frames(4);
        Check("待機中に現れた敵へ自動で付く", player.LockedOn && ReferenceEquals(player.LockTarget, e3));

        // 倒される（QueueFree）→ 待機に戻る
        e3.QueueFree();
        await Frames(4);
        Check("対象が消えても待機を保つ", !player.LockedOn && player.LockArmed);

        // 待機中の解除入力（Shift 押し離し）→ モードごと OFF
        await Tap(Key.Shift);
        Check("待機中に Shift を離すとモードも切れる", !player.LockArmed && !player.LockedOn);
        await Shot("lock_off");

        // OFF のまま敵が現れても勝手には付かない
        var e4 = Spawn(world, spec, new Vector2(Field.Left + 200, 100));
        await Frames(4);
        Check("OFF なら現れた敵に勝手に付かない", !player.LockedOn && !player.LockArmed);

        // 敵の居ない盤面で狙う入力 → 待機に入る → 敵が現れたら付く
        e4.QueueFree();
        await Frames(3);
        await Tap(Key.S);
        Check("敵なしで S → 待機", player.LockArmed && !player.LockedOn);
        var e5 = Spawn(world, spec, new Vector2(Field.Left + 160, 116));
        await Frames(4);
        Check("待機 → 現れた敵へ付く", player.LockedOn && ReferenceEquals(player.LockTarget, e5));
        // 掴んだ状態で画面外へ出て戻ってくる → 戻れば再び付く
        e5.GlobalPosition = new Vector2(Field.Left - 200, 116);
        await Frames(3);
        Check("画面外へ出たら掴みだけ外れる", !player.LockedOn && player.LockArmed);
        e5.GlobalPosition = new Vector2(Field.Left + 160, 116);
        await Frames(3);
        Check("戻ってきたら再び付く", player.LockedOn && ReferenceEquals(player.LockTarget, e5));

        // 追尾中の S（候補1体）→ そのまま
        await Tap(Key.S);
        Check("候補1体で S → 掴み続ける", player.LockedOn && player.LockArmed);
        // 追尾中の Shift 押し離し → 解除
        await Tap(Key.Shift);
        Check("追尾中に Shift を離すと解除", !player.LockedOn && !player.LockArmed);

        // ── ② 会話停止中は自機を吹き出しの奥へ ──
        Check("平常時の Z は 10", player.ZIndex == 10);
        // 自機を会話ボックス（設計 y 520..690・盤面中央）の上に置く
        player.GlobalPosition = new Vector2(Field.CenterX, 600f * UiKit.Scale);
        _keepBubble = true;
        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Other, "……ここで止まる。読めるか、確かめて。", "", "レイ");
        await Frames(4);
        Check("会話で止まっている", Hud.BubblePaused);
        Check("止まっている間は吹き出しの奥（Z -8）", player.ZIndex == -8);
        await Shot("bubble_behind");
        _keepBubble = false;
        hud.HoldBubble = false; hud.HideBubble();
        await Frames(4);
        Check("会話が明けたら Z は 10 に戻る", !Hud.BubblePaused && player.ZIndex == 10);

        root.QueueFree(); await Frames(5);
    }

    private static MidEnemy Spawn(Node2D world, EnemySpec spec, Vector2 at)
    {
        // 進入モーションで動かれる／場外判定で自滅されると検証にならないので、
        //   AddChild 前に居座り点を確定させ、木に入った直後に物理を止めて位置を固定する。
        var e = new MidEnemy(); e.Configure(spec); e.SetEntry(at); e.Position = at; world.AddChild(e);
        e.SetPhysicsProcess(false); e.GlobalPosition = at;
        return e;
    }

    private void Check(string name, bool ok)
    {
        if (!ok) _fail++;
        GD.Print($"[LM] {(ok ? "OK " : "NG ")} {name}");
    }

    private async Task Tap(Key k) => await Hold(k, 4);

    private async Task Hold(Key k, int frames)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        await Frames(frames);
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
        await Frames(4);
    }

    private async Task Shot(string name)
    {
        if (!_shot) return;
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check($"screenshot {name}", image.SavePng($"{_out}/{name}.png") == Error.Ok);
    }

    // ステージ本体はイントロ会話を抱えていて、ヘッドレスでは誰も送らない＝ずっと BubblePaused。
    // 入力が全部食われて検証にならないので、②の区間以外は毎フレーム畳み続ける。
    private Hud? _hud;
    private bool _keepBubble;
    public override void _Process(double delta)
    {
        if (_keepBubble || _hud == null) return;
        if (Hud.BubblePaused) { _hud.HoldBubble = false; _hud.HideBubble(); }
    }

    private static T? FindNode<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren()) { var r = FindNode<T>(c); if (r != null) return r; }
        return null;
    }
    private static T? FindNamed<T>(Node n, string name) where T : Node
    {
        if (n is T t && n.Name == name) return t;
        foreach (var c in n.GetChildren()) { var r = FindNamed<T>(c, name); if (r != null) return r; }
        return null;
    }

    private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
}
