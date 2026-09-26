using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

// LunaticQa : ルナティック「純粋STG化」（2026-09-26 作者指示「回想・エンディング・選択肢はカット、常に敵が出続け、
//   ボス戦は止まらない」）の自動検証。あかり／こはる／レイ／FINAL を Difficulty=Lunatic で自動進行し、
//     (a) 戦闘中に Hud.BubblePaused（弾と敵を止める会話）が一度も立たない・CinematicMode にも入らない
//     (b) StoryFilm／ChoiceOverlay（と MinaPhaseScene／BossDraftScene／CommentInput／BossPost）が一度も生成されない
//     (c) 各面がボス撃破まで詰まらず到達し、次のシーン（ハブ／FINAL はタイトル）へ遷移する
//   を見て `[LU] DONE ok` を出す。`--normal` を付けると同じ自動進行を Normal で回し、従来どおり回想・選択肢・停止会話が
//   **出る**こと（FINAL は Final.tscn へ行くこと）を確かめる＝ルナティックの切り分けが従来難易度へ漏れていない証拠。
//
//   自動操縦は QaPilot の --assist と同じ作法（合成入力＋自機周りの弾消し＋最寄りの敵へ弾）をこの中に持つ
//   ＝QaPilot（--qa）は起動しない。自機は QaPilot.GodActive を立てて死なせない（Player/AreaStrike/CorridorRun が参照）。
//   スクショは撮らない（Shot() はヘッドレスで固まる）。
//
//   起動:  APPDATA=<repo>/build/qa_story/lunatic_appdata  godot --headless --path <repo> res://tools/qa_lunatic.tscn
//          [-- --normal] [-- --stages akari,koharu,rei,final] [-- --limit 900] [-- --speed 2]
//   --stages … 走る面（既定は4面ぜんぶ・順番どおり）／--limit … 1面の上限（ゲーム内秒。超えたら詰まりとして FAIL）
//   --speed  … Engine.TimeScale（物理 tick を同じ倍率で増やし、1 tick の刻みは 1/60 のまま＝挙動は変えず壁時計だけ縮める）
public partial class LunaticQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[LU] PASS {message}");
    }

    // ---- 走行設定 ----
    private bool _normal;
    private double _speed = 1;
    private double _stageLimit = 900;
    private string[] _stages = { "Akari", "Koharu", "Rei", "MinaBattle" };

    // ---- 自動操縦（QaPilot.--assist と同じ数字）----
    private const float MinX = 0f, MaxX = 384f, MinY = 0f, MaxY = 216f;
    private const float GodClearRadius = 18f;   // 自機周囲のこの距離の敵弾を消す
    private const double AimInterval = 0.04;    // 最寄りの敵へ撃つ間隔
    private const double BombPeriod = 18.0;     // ボム（X）の周期
    private const double Heartbeat = 5.0;       // 進捗ログの間隔（ゲーム内秒）
    private GameManager _game = null!;
    private BulletPool _pool = null!;
    private bool _zDown, _xDown;
    private double _zPhase, _bombPhase, _aimT;
    // 自機の無敵（Player.TakeHit の先頭で弾く）。QaPilot.GodActive は敵本体・AOE・通路の接触しか塞がず、敵弾の接触
    //   （Player.OnAreaEntered）は GodClear の 18px 圏をすり抜けた弾で普通に被弾する＝FINAL の密度だと残機 3 が尽きて
    //   ゲームオーバーの選択（ChoiceOverlay）が立ち、走行が再読込で壊れた（2 回目の走行で実際に起きた）。
    //   他の QA（RouteBackgroundQa 等）と同じく _invincible を直に立て、タイマーが切れないよう毎物理フレーム貼り直す。
    private static readonly FieldInfo InvincibleField = typeof(Player).GetField("_invincible", Private)!;
    private static readonly FieldInfo InvincibleTimerField = typeof(Player).GetField("_invincibleTimer", Private)!;

    // ---- 観測（面ごとにリセット）----
    private Node? _root;
    private Hud? _hud;
    private Node? _stage;
    private double _t;                 // 面内の経過（ゲーム内秒）
    private double _hbT;
    private int _pausedFrames, _cinematicFrames;
    private string _pausedFirst = "";
    private bool _bossDefeated;
    private double _gapT, _maxGap;     // 「敵が一体も居ない」連続秒の最大（ボス撃破まで）＝道中の空白の実測（情報のみ）
    private readonly Dictionary<string, int> _spawned = new();   // 禁止ノードの生成回数（型名→回数）
    private static readonly string[] Forbidden =
        { "StoryFilm", "ChoiceOverlay", "MinaPhaseScene", "BossDraftScene", "CommentInput", "BossPost", "EndingFilm" };

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated user data");
            ParseArgs();
            _game = GetNode<GameManager>("/root/Game");
            _pool = GetNode<BulletPool>("/root/Pool");
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = _normal ? GameManager.Diff.Normal : GameManager.Diff.Lunatic;
            _game.MsgCharsPerSec = 300;   // Normal 走行の会話送りを速く（ルナティックでは会話そのものが無い）
            // 自機を死なせない（--qa の --god と同じ旗。setter は private なのでリフレクションで立てる）。
            typeof(QaPilot).GetProperty("GodActive")!.SetValue(null, true);
            if (_speed != 1) Engine.PhysicsTicksPerSecond = (int)Math.Round(60 * _speed);
            GetTree().NodeAdded += OnNodeAdded;
            GD.Print($"[LU] start diff={_game.Difficulty} stages={string.Join(",", _stages)} limit={_stageLimit:0}s speed={_speed:0.##}");
            foreach (string stage in _stages) await RunStage(stage);
            Audio.Instance?.StopMusic(0);
            await Frames(5);
            GD.Print($"[LU] DONE ok ({(_normal ? "Normal: story gimmicks present" : "Lunatic: pure STG")})");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[LU] FAIL {ex}");
            GD.Print($"[LU] FAIL {ex.Message}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private void ParseArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--normal": _normal = true; break;
                case "--speed": if (i + 1 < args.Length && double.TryParse(args[i + 1], out var s)) _speed = Math.Max(1, s); break;
                case "--limit": if (i + 1 < args.Length && double.TryParse(args[i + 1], out var l)) _stageLimit = l; break;
                case "--stages":
                    if (i + 1 < args.Length)
                    {
                        var list = new List<string>();
                        foreach (string id in args[i + 1].Split(','))
                            list.Add(id.Trim().ToLowerInvariant() switch
                            {
                                "akari" => "Akari", "koharu" => "Koharu", "rei" => "Rei",
                                "final" or "mina" or "minabattle" => "MinaBattle",
                                _ => throw new Exception($"unknown stage '{id}'"),
                            });
                        _stages = list.ToArray();
                    }
                    break;
            }
        }
    }

    // ───────── 1面ぶんの走行 ─────────
    private async Task RunStage(string name)
    {
        string stageNode = name == "MinaBattle" ? "StageMina" : $"Stage{name}";
        string expectedNext = name == "MinaBattle"
            ? (_normal ? "res://Final.tscn" : "res://TitleMenu.tscn")
            : "res://Hub.tscn";
        _game.SelectedEntry = GameManager.StageEntry.Start;
        var root = GD.Load<PackedScene>($"res://{name}.tscn").Instantiate<Node2D>();
        ResetObservation();
        _root = root;
        await Frames(1);
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        _hud = root.GetNode<Hud>("Hud");
        _stage = root.GetNode<Node>(stageNode);
        GD.Print($"[LU] ---- {name} ({_game.DiffName}) ----");

        // 遷移するまで回す。上限を超えたら詰まりとして落とす（どこで止まったかを添える）。
        while (IsInstanceValid(root) && GetTree().CurrentScene == root)
        {
            await Frames(1);
            if (_t > _stageLimit)
                throw new Exception($"{name}: no transition within {_stageLimit:0}s — {Describe()} (進行不能の疑い)");
        }
        await Frames(1);
        var next = GetTree().CurrentScene;
        string nextPath = next?.SceneFilePath ?? "(null)";
        _root = null; _hud = null; _stage = null;
        GD.Print($"[LU] {name}: transition -> {nextPath} at t={_t:0.0}s paused={_pausedFrames}f cinematic={_cinematicFrames}f "
               + $"spawned={{{SpawnedSummary()}}} boss={(_bossDefeated ? "defeated" : "NOT defeated")} maxEnemyGap={_maxGap:0.0}s");

        // ---- 判定 ----
        Check(nextPath == expectedNext, $"{name}: leaves the stage to {expectedNext} (got {nextPath})");
        Check(_bossDefeated, $"{name}: boss was defeated before the transition");
        if (!_normal)
        {
            Check(_pausedFrames == 0, $"{name}: Hud.BubblePaused never rose during combat" + (_pausedFrames > 0 ? $" (first at {_pausedFirst})" : ""));
            Check(_cinematicFrames == 0, $"{name}: Hud.CinematicMode never rose");
            foreach (string kind in Forbidden)
                Check(Count(kind) == 0, $"{name}: no {kind} was created ({Count(kind)})");
        }
        else
        {
            // 従来どおり：停止会話・回想（memory＋aftermath）・下書き選択・投稿の割り込みが出る。
            Check(_pausedFrames > 0, $"{name}: Normal still pauses combat for dialogue ({_pausedFrames} frames)");
            Check(Count("StoryFilm") >= 2, $"{name}: Normal still plays memory + aftermath films ({Count("StoryFilm")})");
            Check(Count("BossPost") >= 1, $"{name}: Normal still runs the boss post interlude ({Count("BossPost")})");
            if (name == "MinaBattle")
                Check(Count("MinaPhaseScene") == 4, $"{name}: Normal still plays the 4 phase scenes ({Count("MinaPhaseScene")})");
            else
                Check(Count("ChoiceOverlay") >= 1, $"{name}: Normal still shows draft choices ({Count("ChoiceOverlay")})");
        }

        // 次の面へ：遷移先（ハブ／タイトル／Final）は用が無いので畳む。
        await Frames(2);
        if (next != null && IsInstanceValid(next)) next.QueueFree();
        await Frames(3);
    }

    private void ResetObservation()
    {
        _t = 0; _hbT = 0;
        _pausedFrames = 0; _cinematicFrames = 0; _pausedFirst = "";
        _bossDefeated = false;
        _gapT = 0; _maxGap = 0;
        _spawned.Clear();
        _zPhase = 0; _bombPhase = 0; _aimT = 0;
    }

    private int Count(string kind) => _spawned.TryGetValue(kind, out int n) ? n : 0;
    private string SpawnedSummary()
    {
        var parts = new List<string>();
        foreach (var kv in _spawned) parts.Add($"{kv.Key}={kv.Value}");
        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }

    // 生成されたノードの型で物語ギミックを数える（型名の一致は継承も拾う＝AkariStoryFilm は StoryFilm）。
    private void OnNodeAdded(Node node)
    {
        if (_root == null) return;
        string? kind = node switch
        {
            StoryFilm => "StoryFilm",
            ChoiceOverlay => "ChoiceOverlay",
            MinaPhaseScene => "MinaPhaseScene",
            BossDraftScene => "BossDraftScene",
            CommentInput => "CommentInput",
            BossPost => "BossPost",
            EndingFilm => "EndingFilm",
            _ => null,
        };
        if (kind == null) return;
        _spawned[kind] = Count(kind) + 1;
        if (_spawned[kind] == 1) GD.Print($"[LU]   {kind} appeared at t={_t:0.0}s ({Describe()})");
    }

    private string Describe()
    {
        int step = _stage != null ? Read<int>(_stage, "_step") : -1;
        int enemies = GetTree().GetNodesInGroup("enemies").Count;
        int ebul = GetTree().GetNodesInGroup("enemy_bullets").Count;
        float boss = BossMinHp();
        return $"step={step} enemies={enemies} ebul={ebul} boss={(boss > 1.5f ? "-" : boss.ToString("0.00"))} "
             + $"bubble={Hud.BubblePaused} cinematic={_hud?.CinematicMode ?? false}";
    }

    // ───────── 毎フレーム：駆動＋観測 ─────────
    public override void _Process(double delta)
    {
        // Hitstop（GameCamera）は TimeScale を 0.06 に落として 1.0 へ戻すので、--speed は戻ったあとに貼り直す。
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
        AimAssist(delta, ppos);
    }

    private void Observe(double delta)
    {
        if (Hud.BubblePaused)
        {
            if (_pausedFrames == 0) _pausedFirst = $"t={_t:0.0}s {Describe()}";
            _pausedFrames++;
        }
        if (_hud != null && _hud.CinematicMode) _cinematicFrames++;
        // ボス撃破：各 Stage の _boss（Enemy 派生）が浄化されたら立てる（撃破後は enemies グループから抜けるので直接見る）。
        if (_stage != null && _stage.GetType().GetField("_boss", Private)?.GetValue(_stage) is Enemy boss
            && IsInstanceValid(boss) && boss.IsPurified) _bossDefeated = true;
        // 空白の実測：ボス撃破までの間、盤面に敵が一体も居ない連続秒（開幕の湧き待ち・FINAL のタイトルカードも含む）。
        if (!_bossDefeated)
        {
            if (GetTree().GetNodesInGroup("enemies").Count == 0) { _gapT += delta; if (_gapT > _maxGap) _maxGap = _gapT; }
            else _gapT = 0;
        }
        _hbT += delta;
        if (_hbT < Heartbeat) return;
        _hbT = 0;
        GD.Print($"[LU] t={_t:000.0} {Describe()} purified={_game.PurifiedCount}/{_game.StageTarget} paused={_pausedFrames}f spawned={{{SpawnedSummary()}}}");
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

    // 最寄りの敵（居なければ投稿の札＝Normal 走行で割る対象）へ自機弾を撃ち込む。
    private void AimAssist(double delta, Vector2 ppos)
    {
        _aimT += delta;
        if (_aimT < AimInterval) return;
        _aimT = 0;
        Node2D? target = null;
        float best = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && !e.IsPurified)
            {
                float d = ppos.DistanceTo(e.GlobalPosition);
                if (d < best) { best = d; target = e; }
            }
        if (target == null)
            foreach (Node n in GetTree().GetNodesInGroup("boss_post"))
                if (n is Node2D post) { target = post; break; }
        if (target == null) return;
        Vector2 dir = (target.GlobalPosition - ppos).Normalized();
        if (dir == Vector2.Zero) dir = Vector2.Right;
        _pool.Spawn(ppos + dir * 16f, dir * 460f, isEnemy: false, 3f, 1);
    }

    private float BossMinHp()
    {
        float min = 2f;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && e.HasHpBar && !e.IsPurified && e.HpRatio < min) min = e.HpRatio;
        return min;
    }

    private static void Send(InputEvent e) => Input.ParseInputEvent(e);

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
