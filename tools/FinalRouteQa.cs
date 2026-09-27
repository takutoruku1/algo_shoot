using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

// FinalRouteQa : FINAL（MinaBattle）の「道中の尺」を実測する QA（2026-09-27 作者指示「MINA の道中戦を長くして」）。
//   自動操縦は LunaticQa と同じ作法（合成入力＋自機周りの弾消し＋最寄りの敵へ弾／自機は無敵）をこの中に持つ
//   ＝QaPilot（--qa）は起動しない。スクショは撮らない（Shot() はヘッドレスで固まる）。
//
//   面ごとに測るもの:
//     (a) 道中の実秒   … Stage の _step が 2（道中の頭）に入ってから本ボス（_boss）が出るまで
//     (b) 空白の最長   … その間に「敵が一体も居ない」状態が続いた最長秒（波と波のつなぎ目の穴）
//     (c) 出現数/浄化数 … 道中に湧いた MidEnemy の総数と、ボス出現時点の PurifiedCount / StageTarget
//     (d) 停止会話     … Hud.BubblePaused が立ったフレーム数（ルナティックは道中も戦闘中も 0 でなければならない）
//     (e) 到達         … ボス撃破まで詰まらず進んで次のシーンへ遷移するか
//   さらに「スポナーが回っていた秒（SpawnerSec）」を別に積む。あかり面の道中は中ボス（カメオ）と
//   停止会話を含むので、ザコ戦だけを同じ土俵で比べるための数字がこれ。
//
//   ★測り方の偏り（読む人へ）: 自動操縦は毎秒 25 発を最寄りの敵へ吸わせるのでザコは湧いた直後にほぼ即死する。
//     したがってここで出る秒数は「湧き間隔だけで決まる下限値」＝人間のプレイでは必ずこれより長い。
//     あかり面も同じ偏りで測っているので、面どうしの比（FINAL / あかり）を読む用途では足りる。
//
//   起動:  APPDATA=<repo>/build/qa_story/finalroute_appdata
//          godot --headless --path <repo> res://tools/qa_final_route.tscn
//          [-- --runs final,final-lunatic,akari] [-- --measure] [-- --limit 900] [-- --speed 3]
//   --runs    … 走る面（既定 final,final-lunatic,akari）。akari は本ボス出現で打ち切る（比較用の道中だけ測る）
//   --measure … 判定せず数字だけ出す（変更前の実測に使う）
//   --limit   … 1面の上限（ゲーム内秒。超えたら詰まりとして FAIL）
//   --speed   … Engine.TimeScale（物理 tick も同じ倍率で増やす＝挙動は変えず壁時計だけ縮める）
public partial class FinalRouteQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? ReadObj(object obj, string field) => obj.GetType().GetField(field, Private)?.GetValue(obj);
    private static int ReadInt(object obj, string field) => ReadObj(obj, field) is int i ? i : -1;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[FR] PASS {message}");
    }

    // ---- 判定のしきい値 ----
    private const double GapLimit = 2.0;        // 道中の「敵ゼロ」の最長（作者指示：2 秒以内）
    // FINAL の道中の実測（2026-09-27・三波 66 体）: Normal 66 秒 / Lunatic 48 秒（ルナティックは湧き間隔が
    //   0.62 倍なので同じ体数でも短く出る＝難易度の密度差。長さを難易度で変えてはいない）。
    private const double FinalRouteMin = 38.0;  // これ未満＝波が飛んでいる（短すぎ）
    private const double FinalRouteMax = 110.0; // これ超＝間延び（人間では更に伸びる）
    // FINAL のザコ戦秒 ÷ あかり面のザコ戦秒。狙いは 0.6〜0.8（実測 0.74）。湧き間隔の乱数ぶれ（±0.8〜1.2）を見て幅を持たせる。
    private const double RatioMin = 0.55;
    private const double RatioMax = 0.95;

    // ---- 走行設定 ----
    private bool _measureOnly;
    // 自機弾の直接投入（AimAssist）を止めて、Z 連打で撃つ実弾だけで戦わせる（--realfire）。
    //   自機の発射間隔（Player.FireInterval=0.13s）と当てる下手さがそのまま出るので、
    //   「人間が遊んだときの道中の秒数」に近い値が測れる。ボス戦は倒しきれず詰まるので道中限定で使う。
    private bool _realFire;
    private double _speed = 1;
    private double _stageLimit = 900;
    private readonly List<(string stage, GameManager.Diff diff, bool stopAtBoss)> _runs = new();

    // ---- 自動操縦（LunaticQa と同じ数字）----
    private const float GodClearRadius = 18f;   // 自機周囲のこの距離の敵弾を消す
    private const double AimInterval = 0.04;    // 最寄りの敵へ撃つ間隔
    private const double BombPeriod = 18.0;     // ボム（X）の周期
    private const double Heartbeat = 10.0;      // 進捗ログの間隔（ゲーム内秒）
    private GameManager _game = null!;
    private BulletPool _pool = null!;
    private bool _zDown, _xDown;
    private double _zPhase, _bombPhase, _aimT;
    private static readonly FieldInfo InvincibleField = typeof(Player).GetField("_invincible", Private)!;
    private static readonly FieldInfo InvincibleTimerField = typeof(Player).GetField("_invincibleTimer", Private)!;

    // ---- 観測（面ごとにリセット）----
    private Node? _root;
    private Hud? _hud;
    private Node? _stage;
    private double _t, _hbT;
    private int _step = -1, _wave = -1;
    private bool _routeOpen, _routeDone;
    private double _routeSec, _routeFightSec, _spawnerSec, _gapT, _maxGap;
    private int _routeSpawned, _routePaused, _pausedFrames;
    private int _purifiedAtBoss, _targetAtBoss;
    private bool _bossDefeated;
    private readonly List<string> _timeline = new();
    private readonly HashSet<string> _routeSkins = new();   // 道中に出たザコの絵（残響以外が混ざっていないかを見る）
    private readonly Dictionary<string, double> _spawnerSecByStage = new();
    private readonly Dictionary<string, double> _routeSecByStage = new();

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated user data");
            ParseArgs();
            _game = GetNode<GameManager>("/root/Game");
            _pool = GetNode<BulletPool>("/root/Pool");
            typeof(QaPilot).GetProperty("GodActive")!.SetValue(null, true);
            if (_speed != 1) Engine.PhysicsTicksPerSecond = (int)Math.Round(60 * _speed);
            GetTree().NodeAdded += OnNodeAdded;
            GD.Print($"[FR] start runs={Runs()} limit={_stageLimit:0}s speed={_speed:0.##} measureOnly={_measureOnly} realFire={_realFire}");
            foreach (var run in _runs) await RunStage(run.stage, run.diff, run.stopAtBoss);
            Audio.Instance?.StopMusic(0);
            await Frames(5);
            Summary();
            GD.Print("[FR] DONE ok");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[FR] FAIL {ex}");
            GD.Print($"[FR] FAIL {ex.Message}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private string Runs()
    {
        var parts = new List<string>();
        foreach (var (stage, diff, stop) in _runs) parts.Add($"{stage}/{diff}{(stop ? "(route only)" : "")}");
        return string.Join(" ", parts);
    }

    private void ParseArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--measure": _measureOnly = true; break;
                case "--realfire": _realFire = true; break;
                case "--speed": if (i + 1 < args.Length && double.TryParse(args[i + 1], out var s)) _speed = Math.Max(1, s); break;
                case "--limit": if (i + 1 < args.Length && double.TryParse(args[i + 1], out var l)) _stageLimit = l; break;
                case "--runs":
                    if (i + 1 < args.Length)
                        foreach (string id in args[i + 1].Split(','))
                            _runs.Add(id.Trim().ToLowerInvariant() switch
                            {
                                "final" or "final-normal" => ("MinaBattle", GameManager.Diff.Normal, false),
                                "final-lunatic" => ("MinaBattle", GameManager.Diff.Lunatic, false),
                                "final-route" => ("MinaBattle", GameManager.Diff.Normal, true),
                                "final-lunatic-route" => ("MinaBattle", GameManager.Diff.Lunatic, true),
                                "akari" => ("Akari", GameManager.Diff.Normal, true),
                                "akari-lunatic" => ("Akari", GameManager.Diff.Lunatic, true),
                                "koharu" => ("Koharu", GameManager.Diff.Normal, true),
                                "rei" => ("Rei", GameManager.Diff.Normal, true),
                                _ => throw new Exception($"unknown run '{id}'"),
                            });
                    break;
            }
        }
        if (_runs.Count == 0)
        {
            _runs.Add(("MinaBattle", GameManager.Diff.Normal, false));
            _runs.Add(("MinaBattle", GameManager.Diff.Lunatic, false));
            _runs.Add(("Akari", GameManager.Diff.Normal, true));
        }
    }

    // ───────── 1面ぶんの走行 ─────────
    private async Task RunStage(string name, GameManager.Diff diff, bool stopAtBoss)
    {
        string stageNode = name == "MinaBattle" ? "StageMina" : $"Stage{name}";
        string label = $"{(name == "MinaBattle" ? "FINAL" : name)}/{(diff == GameManager.Diff.Lunatic ? "LUNATIC" : "NORMAL")}";
        _game.ResetPersistent();
        _game.AutoSaveEnabled = false;
        _game.Difficulty = diff;
        _game.MsgCharsPerSec = 300;   // 会話送りを速く（尺の比較を会話の長さで濁さない）
        _game.SelectedEntry = GameManager.StageEntry.Start;
        var root = GD.Load<PackedScene>($"res://{name}.tscn").Instantiate<Node2D>();
        ResetObservation();
        _root = root;
        await Frames(1);
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        _hud = root.GetNode<Hud>("Hud");
        _stage = root.GetNode<Node>(stageNode);
        GD.Print($"[FR] ---- {label} ----");

        while (IsInstanceValid(root) && GetTree().CurrentScene == root)
        {
            await Frames(1);
            if (stopAtBoss && _routeDone) break;
            if (_t > _stageLimit)
                throw new Exception($"{label}: no transition within {_stageLimit:0}s — {Describe()} (進行不能の疑い)");
        }
        await Frames(1);
        var next = GetTree().CurrentScene;
        string nextPath = stopAtBoss ? "(route only)" : next?.SceneFilePath ?? "(null)";
        bool reachedRoute = _routeSec > 0;
        _root = null; _hud = null; _stage = null;

        foreach (string line in _timeline) GD.Print($"[FR]   {line}");
        GD.Print($"[FR] {label}: route={_routeSec:0.0}s (fight={_routeFightSec:0.0}s spawner={_spawnerSec:0.0}s) "
               + $"maxEnemyGap={_maxGap:0.00}s spawned={_routeSpawned} purified={_purifiedAtBoss}/{_targetAtBoss} "
               + $"pausedInRoute={_routePaused}f pausedTotal={_pausedFrames}f "
               + $"boss={(_bossDefeated ? "defeated" : stopAtBoss ? "(skipped)" : "NOT defeated")} next={nextPath} total={_t:0.0}s");
        _spawnerSecByStage[label] = _spawnerSec;
        _routeSecByStage[label] = _routeSec;

        if (!_measureOnly)
        {
            Check(reachedRoute && _routeDone, $"{label}: the route runs and hands over to the boss");
            if (name == "MinaBattle")
            {
                Check(_routeSec >= FinalRouteMin && _routeSec <= FinalRouteMax,
                    $"{label}: route lasts {_routeSec:0.0}s (want {FinalRouteMin:0}..{FinalRouteMax:0}s)");
                Check(_maxGap <= GapLimit, $"{label}: never leaves the field empty for more than {GapLimit:0.0}s (max {_maxGap:0.00}s)");
                Check(_routeSpawned >= _purifiedAtBoss, $"{label}: every purified echo came from the route waves");
                // 道中に出た絵が残響3種だけであること（Spawner.CharactersOnly＝他テーマ流用のアンチくん／引用リプ／
                //   バズ壁を混ぜない）。FINAL に他の面の敵が紛れ込んでいたら落とす。
                var echoes = new HashSet<string>();
                foreach (var spec in EnemyTable.CharactersFor(StageTheme.Mina)) echoes.Add(spec.PreTexPath);
                Check(_routeSkins.Count == echoes.Count && _routeSkins.IsSubsetOf(echoes),
                    $"{label}: the route only spawns the three echoes ({string.Join(" ", _routeSkins)})");
            }
            if (diff == GameManager.Diff.Lunatic)
                Check(_pausedFrames == 0, $"{label}: Hud.BubblePaused never rises ({_pausedFrames} frames)");
            if (!stopAtBoss)
            {
                Check(_bossDefeated, $"{label}: boss was defeated before the transition");
                string expected = name == "MinaBattle"
                    ? (diff == GameManager.Diff.Lunatic ? "res://TitleMenu.tscn" : "res://Final.tscn")
                    : "res://Hub.tscn";
                Check(nextPath == expected, $"{label}: leaves the stage to {expected} (got {nextPath})");
            }
        }

        await Frames(2);
        if (next != null && IsInstanceValid(next) && next != this) next.QueueFree();
        if (IsInstanceValid(root)) root.QueueFree();
        await Frames(3);
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        Hud.BubblePaused = false;
    }

    // あかり面のザコ戦秒を基準に FINAL の比を出す（同じ自動操縦・同じ計り方なので面どうしは比べられる）。
    private void Summary()
    {
        GD.Print("[FR] ==== summary ====");
        foreach (var kv in _routeSecByStage)
            GD.Print($"[FR] {kv.Key}: route={kv.Value:0.0}s spawner={_spawnerSecByStage[kv.Key]:0.0}s");
        if (!_spawnerSecByStage.TryGetValue("Akari/NORMAL", out double akari) || akari <= 0) return;
        foreach (var kv in _spawnerSecByStage)
        {
            if (!kv.Key.StartsWith("FINAL")) continue;
            double ratio = kv.Value / akari;
            GD.Print($"[FR] {kv.Key} spawner seconds vs Akari: {kv.Value:0.0} / {akari:0.0} = {ratio:0.00}");
            if (_measureOnly || kv.Key != "FINAL/NORMAL") continue;
            Check(ratio >= RatioMin && ratio <= RatioMax,
                $"FINAL route is {ratio:0.00}x the Akari route waves (want {RatioMin:0.00}..{RatioMax:0.00})");
        }
    }

    private void ResetObservation()
    {
        _t = 0; _hbT = 0;
        _step = -1; _wave = -1;
        _routeOpen = false; _routeDone = false;
        _routeSec = 0; _routeFightSec = 0; _spawnerSec = 0; _gapT = 0; _maxGap = 0;
        _routeSpawned = 0; _routePaused = 0; _pausedFrames = 0;
        _purifiedAtBoss = 0; _targetAtBoss = 0;
        _bossDefeated = false;
        _timeline.Clear();
        _routeSkins.Clear();
        _zPhase = 0; _bombPhase = 0; _aimT = 0;
    }

    private void OnNodeAdded(Node node)
    {
        if (_root == null || !_routeOpen || _routeDone) return;
        if (node is MidEnemy) _routeSpawned++;
    }

    private string Describe()
    {
        int enemies = GetTree().GetNodesInGroup("enemies").Count;
        int ebul = GetTree().GetNodesInGroup("enemy_bullets").Count;
        return $"step={_step} wave={_wave} enemies={enemies} ebul={ebul} purified={_game.PurifiedCount}/{_game.StageTarget} "
             + $"bubble={Hud.BubblePaused}";
    }

    // ───────── 毎フレーム：駆動＋観測 ─────────
    public override void _Process(double delta)
    {
        if (_speed != 1 && Engine.TimeScale >= 0.99 && Math.Abs(Engine.TimeScale - _speed) > 0.001) Engine.TimeScale = _speed;
        if (_root == null || !IsInstanceValid(_root) || GetTree().CurrentScene != _root) return;
        _t += delta;
        DriveInput(delta);
        Observe(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_root == null || !IsInstanceValid(_root) || GetTree().CurrentScene != _root) return;
        if (GetTree().GetFirstNodeInGroup("player") is not Player player) return;
        InvincibleField.SetValue(player, true);
        InvincibleTimerField.SetValue(player, 9999f);
        Vector2 ppos = player.GlobalPosition;
        GodClear(ppos);
        if (!_realFire) AimAssist(delta, ppos);
    }

    private void Observe(double delta)
    {
        if (_stage == null || !IsInstanceValid(_stage)) return;
        if (Hud.BubblePaused) _pausedFrames++;

        // step / wave の遷移を時刻つきで残す（どこで何秒使ったかを読めるようにする）。
        int step = ReadInt(_stage, "_step");
        int wave = ReadInt(_stage, "_wave");
        if (step != _step) { _timeline.Add($"t={_t:000.0} step {_step} -> {step} ({Describe()})"); _step = step; }
        if (wave != _wave) { if (_wave >= 0) _timeline.Add($"t={_t:000.0} wave {_wave} -> {wave}"); _wave = wave; }

        // 道中の開始＝step 2 の頭（あかり面も FINAL も 2 が道中の最初）。終わり＝本ボスの出現。
        if (!_routeOpen && !_routeDone && step >= 2) { _routeOpen = true; _timeline.Add($"t={_t:000.0} route begins"); }
        bool bossUp = ReadObj(_stage, "_boss") is Enemy b && IsInstanceValid(b);
        if (_routeOpen && !_routeDone && bossUp)
        {
            _routeDone = true; _routeOpen = false;
            _purifiedAtBoss = _game.PurifiedCount; _targetAtBoss = _game.StageTarget;
            _timeline.Add($"t={_t:000.0} boss appears (route={_routeSec:0.0}s)");
        }
        if (_routeOpen)
        {
            _routeSec += delta;
            foreach (Node n in GetTree().GetNodesInGroup("enemies"))
                if (n is MidEnemy me && n.GetType().GetField("_spec", Private)?.GetValue(me) is EnemySpec spec)
                    _routeSkins.Add(spec.PreTexPath);
            if (Hud.BubblePaused) _routePaused++; else _routeFightSec += delta;
            if (GetTree().GetNodesInGroup("enemies").Count == 0) { _gapT += delta; if (_gapT > _maxGap) _maxGap = _gapT; }
            else _gapT = 0;
            foreach (Node child in _stage.GetChildren())
                if (child is Spawner { Active: true }) { _spawnerSec += delta; break; }
        }
        if (ReadObj(_stage, "_boss") is Enemy boss && IsInstanceValid(boss) && boss.IsPurified) _bossDefeated = true;

        _hbT += delta;
        if (_hbT < Heartbeat) return;
        _hbT = 0;
        GD.Print($"[FR] t={_t:000.0} {Describe()} route={_routeSec:0.0}s gap={_maxGap:0.00}s spawned={_routeSpawned}");
    }

    // 移動＝サイン波（会話中は軸を離す＝選択肢の既定カーソルを動かさない）／Z＝撃つ＋会話送り／X＝ボム。
    private void DriveInput(double delta)
    {
        bool talking = Hud.BubblePaused;
        if (talking)
        {
            SetAxis("ui_left", "ui_right", 0f);
            SetAxis("ui_up", "ui_down", 0f);
        }
        else if (_realFire && GetTree().GetFirstNodeInGroup("player") is Player me && NearestEnemy(me.GlobalPosition) is { } foe)
        {
            // 実弾モード：自機弾は右へしか飛ばないので、狙う敵の左・同じ高さに着けてから撃つ
            //   ＝「並んで撃つ」ぶんの手間が秒数に入る（人間の道中に近づける）。
            Vector2 want = new Vector2(foe.GlobalPosition.X - 70f, foe.GlobalPosition.Y);
            Vector2 d = want - me.GlobalPosition;
            SetAxis("ui_left", "ui_right", Mathf.Clamp(d.X / 24f, -1f, 1f));
            SetAxis("ui_up", "ui_down", Mathf.Clamp(d.Y / 16f, -1f, 1f));
        }
        else
        {
            float vx = Mathf.Sin((float)_t * 1.3f) * 0.9f + Mathf.Sin((float)_t * 0.37f) * 0.4f;
            float vy = Mathf.Sin((float)_t * 0.8f + 1.1f) * 0.55f;
            SetAxis("ui_left", "ui_right", vx);
            SetAxis("ui_up", "ui_down", vy);
        }
        double period = talking ? 0.5 : 0.16;
        _zPhase += delta;
        if (_zPhase >= period) _zPhase -= period;
        bool down = _zPhase < period * 0.45;
        if (down != _zDown)
        {
            _zDown = down;
            Send(new InputEventKey { Keycode = Key.Z, Pressed = down });
        }
        _bombPhase += delta;
        if (!talking && _bombPhase >= BombPeriod && !_xDown)
        {
            _xDown = true;
            Send(new InputEventKey { Keycode = Key.X, Pressed = true });
        }
        else if (_xDown && _bombPhase >= BombPeriod + 0.12)
        {
            _xDown = false;
            _bombPhase = 0;
            Send(new InputEventKey { Keycode = Key.X, Pressed = false });
        }
    }

    private static void SetAxis(string neg, string pos, float v)
    {
        v = Mathf.Clamp(v, -1f, 1f);
        Send(new InputEventAction { Action = pos, Pressed = v > 0.05f, Strength = Mathf.Max(0f, v) });
        Send(new InputEventAction { Action = neg, Pressed = v < -0.05f, Strength = Mathf.Max(0f, -v) });
    }

    private void GodClear(Vector2 ppos)
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemy_bullets"))
            if (n is Bullet b && b.Active && ppos.DistanceTo(b.GlobalPosition) <= GodClearRadius)
                _pool.Despawn(b);
    }

    private Enemy? NearestEnemy(Vector2 ppos)
    {
        Enemy? found = null;
        float best = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && !e.IsPurified && e.GlobalPosition.X <= Field.Right)
            {
                float d = ppos.DistanceTo(e.GlobalPosition);
                if (d < best) { best = d; found = e; }
            }
        return found;
    }

    private void AimAssist(double delta, Vector2 ppos)
    {
        _aimT += delta;
        if (_aimT < AimInterval) return;
        _aimT = 0;
        Node2D? target = NearestEnemy(ppos);
        if (target == null)
            foreach (Node n in GetTree().GetNodesInGroup("boss_post"))
                if (n is Node2D post) { target = post; break; }
        if (target == null) return;
        Vector2 dir = (target.GlobalPosition - ppos).Normalized();
        if (dir == Vector2.Zero) dir = Vector2.Right;
        _pool.Spawn(ppos + dir * 16f, dir * 460f, isEnemy: false, 3f, 1);
    }

    private static void Send(InputEvent e) => Input.ParseInputEvent(e);

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
