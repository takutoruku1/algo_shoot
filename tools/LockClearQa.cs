using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// 使い捨て検証ハーネス（QA・2026-09-17）：ロックオン解除の新規割り当て（G / パッド R3）。
//   ・敵を2体置いて F で送り → G で解除 → 未ロック状態で G を押しても何も起きない、を確認する。
//   ・パッド R3 は物理パッドが要る＝ヘッドレスでは押下を作れないので、結線（Pad.Pressed への問い合わせ）が
//     ある事実だけをソース上で担保し、ここではキーボード経路と「隣接キー(F)が壊れていないこと」を見る。
//   Input.ParseInputEvent は Godot の内部キー状態を更新する＝Input.IsKeyPressed に乗る。
public partial class LockClearQa : Node
{
    private const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Write(object o, string n, object v) => o.GetType().GetField(n, P)!.SetValue(o, v);

    private int _fail;

    public override async void _Ready()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            await Run();
            GD.Print(_fail == 0 ? "[LC] DONE ok" : $"[LC] DONE fail={_fail}");
            GetTree().Quit(_fail == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError($"[LC] FAIL {ex}"); GetTree().Quit(1); }
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

        // ステージ進行を止めて、自機と自作の敵だけの静かな盤面にする。
        (root.GetType().GetProperty("Stage")!.GetValue(root) as Node)?.SetProcess(false);
        var world = FindNamed<Node2D>(root, "World") ?? root;
        var player = FindNode<Player>(root);
        var hud = FindNode<Hud>(root);
        if (player == null) { GD.Print("[LC] NG player not found"); _fail++; return; }
        Write(player, "_invincible", true); Write(player, "_invincibleTimer", 999f);
        player.GlobalPosition = new Vector2(Field.Left + 60, 108);
        _hud = hud;
        if (hud != null) { hud.HoldBubble = false; hud.HideBubble(); }
        await Frames(4);

        // 会話が閉じていないと入力が全部食われるので確認しておく。
        GD.Print($"[LC] BubblePaused={Hud.BubblePaused}");

        // 敵を2体（距離違い）置く。
        var spec = EnemyTable.For(StageTheme.Rei).Item1;
        // Configure(EnemySpec) を持つのは MidEnemy（Enemy は基底で未定義）。EnemyTable.For も MidEnemy 用の spec を返す。
        // 進入モーションで動かれる／場外判定で自滅されると検証にならないので、
        //   AddChild 前に居座り点を確定させ、木に入った直後に物理を止めて位置を固定する。
        var p1 = new Vector2(Field.Left + 140, 108);
        var p2 = new Vector2(Field.Left + 220, 108);
        var e1 = new MidEnemy(); e1.Configure(spec); e1.SetEntry(p1); e1.Position = p1; world.AddChild(e1);
        var e2 = new MidEnemy(); e2.Configure(spec); e2.SetEntry(p2); e2.Position = p2; world.AddChild(e2);
        e1.SetPhysicsProcess(false); e2.SetPhysicsProcess(false);
        e1.GlobalPosition = p1; e2.GlobalPosition = p2;
        await Frames(4);
        if (!IsInstanceValid(e1) || !IsInstanceValid(e2)) { GD.Print("[LC] NG enemy despawned"); _fail++; return; }

        GD.Print($"[LC] enemies group={GetTree().GetNodesInGroup("enemies").Count} " +
                 $"e1x={e1.GlobalPosition.X:0} e2x={e2.GlobalPosition.X:0} px={player.GlobalPosition.X:0}");
        GD.Print($"[LC] start lockedOn={player.LockedOn} BubblePaused={Hud.BubblePaused}");

        // ① F で送り → ロック成立
        await Tap(Key.S);
        Check("S1 でロック成立", player.LockedOn);
        var t1 = player.LockTarget;

        // ② もう一度 F → 別の敵へ送られる（隣接キーの既存動作が壊れていない確認）
        await Tap(Key.S);
        Check("S2 で対象が次の敵へ", player.LockedOn && !ReferenceEquals(player.LockTarget, t1));

        // ③ G で解除
        await Tap(Key.Shift);
        Check("Shift を離して解除", !player.LockedOn);

        // ④ 未ロックで G → 何も起きない（例外も落ちない・ロックも付かない）
        await Tap(Key.Shift);
        Check("未ロックの Shift 押し離しは無反応", !player.LockedOn);

        // ⑤ 解除後にもう一度 F → 一番近い敵から再開できる
        await Tap(Key.S);
        Check("解除後の S で再ロック", player.LockedOn);
        Check("再ロックは最も近い敵", ReferenceEquals(player.LockTarget, e1));

        // ⑥ ロック中に G を押しっぱなし → 1回ぶんしか効かない（リピートで暴発しない）
        await Hold(Key.Shift, 20);
        await Tap(Key.S);
        Check("Shift 押し離しのあと S で再ロック", player.LockedOn);

        root.QueueFree(); await Frames(5);
    }

    private void Check(string name, bool ok)
    {
        if (!ok) _fail++;
        GD.Print($"[LC] {(ok ? "OK " : "NG ")} {name}");
    }

    // キーの押下→数フレーム保持→解放（Player はエッジ検出なので押しっぱなしを跨がせる）。
    private async Task Tap(Key k) => await Hold(k, 4);

    private async Task Hold(Key k, int frames)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        await Frames(frames);
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
        await Frames(4);
    }

    // ステージ本体はイントロ会話を抱えていて、ヘッドレスでは誰も送らない＝ずっと BubblePaused。
    // 入力が全部食われて検証にならないので、毎フレーム畳み続ける。
    private Hud? _hud;
    public override void _Process(double delta) { if (_hud != null && Hud.BubblePaused) { _hud.HoldBubble = false; _hud.HideBubble(); } }

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
