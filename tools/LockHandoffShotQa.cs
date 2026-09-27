using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// 検証ハーネス（QA・2026-09-27）：ロックの引き継ぎで自弾が消えない／対象が居なくても撃ち続ける。
//   作者報告「ロックオン状態で照射して、ターゲットを倒して切り替わった瞬間、球が消えちゃうんだけど、
//   また一から設置している」「タゲ居なくても攻撃してくれなきゃ道中厳しく感じる」。
//   4ジョブそれぞれで、
//     (a) ロックして撃ち込み → 対象を**本当に倒す**（Enemy.Purify＝Redeem を通す。QueueFree では再現しない）
//         → その瞬間に飛行中の自弾（あかりはタメ中の加速球も）が減っていない／ロックが残る敵へ移る
//     (b) 集中の光のスタックが引き継ぎで持ち越される（FocusFireMaxStack が 0＝機能停止中なら SKIP を出す）
//     (c) 敵ゼロ（ロックモードの待機）でも通常弾が出続ける
//     (d) 待機中に溜め打ちが撃てる
//   を見る。使い方： Godot --headless --path . res://tools/qa_lock_handoff_shot.tscn
public partial class LockHandoffShotQa : Node
{
    private const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Write(object o, string n, object v) => o.GetType().GetField(n, P)!.SetValue(o, v);
    private static T Read<T>(object o, string n) => (T)o.GetType().GetField(n, P)!.GetValue(o)!;

    private int _fail;

    public override async void _Ready()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            foreach (var job in new[] { Job.Tank, Job.Melee, Job.Heal, Job.Magic })
                await RunJob(job);
            GD.Print(_fail == 0 ? "[LH] DONE ok" : $"[LH] DONE fail={_fail}");
            GetTree().Quit(_fail == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError($"[LH] FAIL {ex}"); GetTree().Quit(1); }
    }

    private async Task RunJob(Job job)
    {
        var game = GetNode<GameManager>("/root/Game");
        game.ResetPersistent(); game.AutoSaveEnabled = false;
        game.SelectedJob = job;
        string tag = $"{Jobs.Get(job).CharacterName}({game.SelectedShotMode})";

        var root = GD.Load<PackedScene>("res://Rei.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        await Frames(2);
        GetTree().CurrentScene = root;
        await Frames(8);

        (root.GetType().GetProperty("Stage")!.GetValue(root) as Node)?.SetProcess(false);
        var world = FindNamed<Node2D>(root, "World") ?? root;
        var player = FindNode<Player>(root);
        _hud = FindNode<Hud>(root);
        if (player == null || _hud == null) { Check($"{tag} player/hud", false); return; }
        Write(player, "_invincible", true); Write(player, "_invincibleTimer", 999f);
        player.GlobalPosition = new Vector2(Field.Left + 40, 108);
        _hud.HoldBubble = false; _hud.HideBubble();
        await Frames(4);
        var pool = GetNode<BulletPool>("/root/Pool");
        pool.DespawnPlayerBullets();

        // ── (a) ロックして撃ち込み → 対象を倒す ──
        var spec = EnemyTable.For(StageTheme.Rei).Item1;
        var e1 = Spawn(world, spec, new Vector2(Field.Left + 200, 100));
        var e2 = Spawn(world, spec, new Vector2(Field.Left + 260, 130));
        await Frames(4);
        await Tap(Key.S);
        Check($"{tag} S で最寄り e1 をロック", player.LockedOn && ReferenceEquals(player.LockTarget, e1));
        // 撃ち込み。直前に盤面の自弾を空にしてから 0.5 秒＝加速球はタメ 0.8 秒の途中＝全部まだ自機の前に「設置」されている。
        pool.DespawnPlayerBullets();
        await Frames(30);
        Check($"{tag} (a) 前提：撃ち込み中に e1 はまだ倒れていない", !e1.IsPurified);
        var (fly0, charging0) = CountShots();
        // 倒す＝Redeem を通す。直後（物理フレームを挟まない）の数を比べる＝撃破そのものが消したかだけを見る。
        e1.Purify();
        var (fly1, charging1) = CountShots();
        GD.Print($"[LH] {tag} 撃破の直前 自弾={fly0}（タメ中={charging0}） → 直後 自弾={fly1}（タメ中={charging1}）");
        Check($"{tag} (a) 撃ち込み中に自弾が飛んでいる", fly0 > 0);
        Check($"{tag} (a) 撃破の瞬間に自弾が消えない", fly1 >= fly0);
        if (job == Job.Melee) Check($"{tag} (a) タメ中の加速球が消えない", charging0 > 0 && charging1 >= charging0);
        await Frames(4);
        Check($"{tag} (a) ロックは残った e2 へ引き継ぐ", player.LockedOn && ReferenceEquals(player.LockTarget, e2));

        // ── (b) 集中の光：引き継ぎでスタックを持ち越す ──
        if ((game.FocusFireMaxStack) <= 0)
            GD.Print($"[LH] SKIP {tag} (b) FocusFireMaxStack=0（集中の光は機能停止中＝NotifyShotHit が早期 return）");
        else
        {
            Write(player, "_focusTarget", e2); Write(player, "_focusHits", 16);
            e2.Purify();
            var e3 = Spawn(world, spec, new Vector2(Field.Left + 220, 108));
            player.NotifyShotHit(e3);
            Check($"{tag} (b) 倒れた対象から次へ移ってもスタックを持ち越す", Read<int>(player, "_focusHits") == 17);
            e3.Purify();
        }

        // ── (c) 敵ゼロ（待機）でも撃ち続ける ──
        if (IsInstanceValid(e2) && !e2.IsPurified) e2.Purify();
        await Frames(4);
        Check($"{tag} (c) 敵ゼロでもモードは残る（待機）", player.LockArmed && !player.LockedOn);
        pool.DespawnPlayerBullets();
        int spawned = 0;
        var seen = new System.Collections.Generic.HashSet<ulong>();
        for (int i = 0; i < 60; i++)
        {
            await Frames(1);
            foreach (Node n in GetTree().GetNodesInGroup("player_bullets"))
                if (n is Bullet b && b.Active && seen.Add(b.GetInstanceId())) spawned++;
        }
        GD.Print($"[LH] {tag} 待機中 1 秒の自弾の出現数={spawned}");
        Check($"{tag} (c) 待機中も通常弾が出る", spawned > 0);

        // ── (d) 待機中に溜め打ち ──
        KeyEvent(Key.Z, true);
        await Frames(50);   // ChargeNeed 0.60 秒 + 余裕
        bool full = player.ChargeFull;
        KeyEvent(Key.Z, false);
        await Frames(3);
        int charged = 0;
        foreach (Node n in GetTree().GetNodesInGroup("player_bullets"))
            if (n is Bullet b && b.Active && b.Charged) charged++;
        Check($"{tag} (d) 待機中に溜めが満ちる", full);
        Check($"{tag} (d) 待機中に溜め打ちが出る（溜め弾={charged}）", charged > 0);

        await Tap(Key.Shift);
        root.QueueFree(); await Frames(5);
        pool.DespawnAll();
    }

    private (int Fly, int Charging) CountShots()
    {
        int fly = 0, charging = 0;
        foreach (Node n in GetTree().GetNodesInGroup("player_bullets"))
            if (n is Bullet b && b.Active && !b.Charged) { fly++; if (b.AccelCharging) charging++; }
        return (fly, charging);
    }

    private static MidEnemy Spawn(Node2D world, EnemySpec spec, Vector2 at)
    {
        var e = new MidEnemy(); e.Configure(spec); e.SetEntry(at); e.Position = at; world.AddChild(e);
        e.SetPhysicsProcess(false); e.GlobalPosition = at;
        return e;
    }

    private void Check(string name, bool ok)
    {
        if (!ok) _fail++;
        GD.Print($"[LH] {(ok ? "OK " : "NG ")} {name}");
    }

    private static void KeyEvent(Key k, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = pressed });

    private async Task Tap(Key k)
    {
        KeyEvent(k, true);
        await Frames(4);
        KeyEvent(k, false);
        await Frames(4);
    }

    // ステージ本体のイントロ会話はヘッドレスでは誰も送らない＝毎フレーム畳む（LockModeQa と同じ）。
    private Hud? _hud;
    public override void _Process(double delta)
    {
        if (_hud == null || !IsInstanceValid(_hud)) return;
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
