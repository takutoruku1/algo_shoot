using Godot;
using System.Collections.Generic;

// QaPilot : 自動QAテスト用のオートロード (/root/QaPilot)。
//
// DemoPilot がデモ動画用に“見栄えよく”プレイするのに対し、こちらは
// 「バグ検出」に振り切った自動プレイヤー。ゲーム側のコードは一切いじらず、
// 合成入力（DemoPilot と同じ Input.ParseInputEvent 経路）でプレイしつつ、
// 毎フレーム シーンツリーを観測して異常をコンソールへ吐く。
//
//   有効化:        res://Prologue.tscn -- --qa
//   尺:           -- --qa --seconds 240
//   進行テスト:    -- --qa --assist          （= --god --aim、死なずに最後まで進む）
//   個別:          -- --qa --god / --aim
//   難易度固定:    -- --qa --easy / --normal / --hard / --lunatic（省略時はセーブ値のまま）
//   終了:          -- --qa --quit            （Epilogue 到達 or 尺で Quit）
//
// 検出して [QA-WARN] / [QA-ERROR] を出すもの:
//   ・suspicious-hit … 残機が減った瞬間、敵弾も敵も自機から遠い ＝ 変な所に当たり判定
//   ・player-oob     … 自機がプレイ領域外 or 座標が NaN
//   ・stuck          … シーンも浄化数もボスHPも会話も一定時間まったく動かない ＝ 進行不能の疑い
//   ・bullet-flood   … 敵弾が異常に増殖（リーク/パフォーマンス）
//   ・low-fps        … FPS が継続的に低い
// 1秒ごとに [QA] ハートビート（scene/lives/purified/boss/弾数/fps/pos）も出すので、
// ログを時系列で追える。最後に [QA-SUMMARY] で件数を出す。
public partial class QaPilot : Node
{
    // ---- 設定 ----
    private const double DefaultSeconds = 240.0;
    private const double HeartbeatInterval = 1.0;
    private const double StuckTimeout = 40.0;   // この秒数、何も進まなければ進行不能を疑う
    private const float SuspiciousGap = 4f;      // 表面どうしがこれ以上離れて被弾＝怪しい(px)
    private const float AssumedEnemyRadius = 14f; // 敵本体半径の安全側見積り（BodyRadius は private）
    private const float PlayerHitRadius = 2f;
    private const float GodClearRadius = 18f;     // god時、自機周囲のこの距離の敵弾を消す
    private const double AimInterval = 0.04;      // aim時のDPS弾の発射間隔
    private const int FloodThreshold = 1200;      // 敵弾がこれを超えたら増殖を疑う
    private const double LowFpsWindow = 3.0;      // この秒数連続で低FPSなら警告
    private const double LowFpsThreshold = 25.0;

    // プレイ領域（Player.cs と一致）
    private const float MinX = 0f, MaxX = 384f, MinY = 0f, MaxY = 216f;

    // ---- フラグ ----
    private bool _active;
    private bool _god;
    private bool _aim;
    private bool _quitOnEnd;
    private GameManager.Diff? _diff;   // 難易度固定（--easy/--normal/--hard/--lunatic。null=セーブ値のまま）
    private double _seconds = DefaultSeconds;

    // ---- 時間 ----
    private double _t;
    private double _hbT;
    private double _aimT;
    private double _lowFpsT;

    // ---- 入力パルス状態（DemoPilot 流）----
    private bool _zDown;
    private double _zPhase;
    private double _bombPhase;
    private bool _xDown;

    // ---- 観測状態 ----
    private string _scene = "";
    private double _sceneEnterT;
    private double _lastProgressT;
    private int _prevLives = int.MinValue;
    private int _prevPurified = -1;
    private float _prevBossMin = 2f;       // 全ボスの最小HpRatio（減れば進捗）
    private bool _prevBubble;
    private bool _stuckReported;

    // 被弾の瞬間に「直前フレーム」の最接近距離を使う（被弾で弾が即消えるレース回避）
    private float _prevBulletGap = 9999f;  // 表面ギャップ（負=重なり）
    private float _prevEnemyGap = 9999f;

    // ---- 集計 ----
    private readonly Dictionary<string, int> _counts = new();

    private GameManager _game = null!;
    private BulletPool _pool = null!;

    public override void _Ready()
    {
        var user = OS.GetCmdlineUserArgs();
        for (int i = 0; i < user.Length; i++)
        {
            switch (user[i])
            {
                case "--qa": _active = true; break;
                case "--god": _god = true; break;
                case "--aim": _aim = true; break;
                case "--assist": _god = true; _aim = true; break;
                case "--quit": _quitOnEnd = true; break;
                case "--easy": _diff = GameManager.Diff.Easy; break;
                case "--normal": _diff = GameManager.Diff.Normal; break;
                case "--hard": _diff = GameManager.Diff.Hard; break;
                case "--lunatic": _diff = GameManager.Diff.Lunatic; break;
                case "--seconds":
                    if (i + 1 < user.Length && double.TryParse(user[i + 1], out var s)) _seconds = s;
                    break;
            }
        }

        if (!_active)
        {
            SetProcess(false);
            SetPhysicsProcess(false);
            return;
        }

        _game = GetNodeOrNull<GameManager>("/root/Game");
        _pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (_diff.HasValue && _game != null) _game.Difficulty = _diff.Value;

        _lastProgressT = 0;
        GD.Print($"[QA] start. budget={_seconds:0}s god={_god} aim={_aim} diff={_diff?.ToString() ?? "(save)"}");
    }

    public override void _Process(double delta)
    {
        _t += delta;

        DriveMovement();
        DriveShootAndAdvance(delta);
        DriveBomb(delta);

        Heartbeat(delta);
        DetectStuck();
        DetectBulletFlood();
        DetectLowFps(delta);

        // Epilogue まで到達したら（全シーン走破＝正常完走）少し見せて終了。
        if (_quitOnEnd && _scene == "Epilogue.tscn" && _t - _sceneEnterT > 4.0)
        {
            GD.Print($"[QA] reached Epilogue (full run complete) at t={_t:0.0}s.");
            EndRun();
        }

        if (_t >= _seconds)
        {
            GD.Print($"[QA] budget reached at t={_t:0.0}s.");
            EndRun();
        }
    }

    // 当たり判定・進行は物理フレームで観測（衝突はここで起きる）。
    public override void _PhysicsProcess(double delta)
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player == null) return;

        Vector2 ppos = player.GlobalPosition;

        // --- 自機の座標健全性 ---
        if (!IsFinite(ppos))
            Flag("player-oob", $"player pos not finite ({ppos})");
        else if (ppos.X < MinX - 1f || ppos.X > MaxX + 1f || ppos.Y < MinY - 1f || ppos.Y > MaxY + 1f)
            Flag("player-oob", $"player out of play area pos={Fmt(ppos)}");

        // --- 最接近の敵弾／敵（表面ギャップ）を毎フレーム計算 ---
        float bulletGap = 9999f;
        foreach (Node n in GetTree().GetNodesInGroup("enemy_bullets"))
        {
            if (n is Bullet b && b.Active)
            {
                float g = ppos.DistanceTo(b.GlobalPosition) - (PlayerHitRadius + b.Radius);
                if (g < bulletGap) bulletGap = g;
            }
        }
        float enemyGap = 9999f;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is Enemy e && !e.IsPurified)
            {
                float g = ppos.DistanceTo(e.GlobalPosition) - (PlayerHitRadius + AssumedEnemyRadius);
                if (g < enemyGap) enemyGap = g;
            }
        }

        // --- 被弾検出（残機が減った瞬間）---
        int lives = player.Lives;
        if (_prevLives != int.MinValue && lives < _prevLives)
        {
            // 直前フレームのギャップで判定（被弾フレームでは弾がもう消えていることがある）
            if (_prevBulletGap > SuspiciousGap && _prevEnemyGap > SuspiciousGap)
                Flag("suspicious-hit",
                    $"lost a life but nearest enemy_bullet gap={_prevBulletGap:0.0}px, enemy gap={_prevEnemyGap:0.0}px (>{SuspiciousGap}px) at {Fmt(ppos)} scene={_scene} t={_t:0.0}");
            else
                GD.Print($"[QA] hit (ok) bulletGap={_prevBulletGap:0.0} enemyGap={_prevEnemyGap:0.0} lives={lives} t={_t:0.0}");
        }
        _prevLives = lives;
        _prevBulletGap = bulletGap;
        _prevEnemyGap = enemyGap;

        if (_god) GodClear(ppos);
        if (_aim) AimAssist(delta, ppos);
    }

    // =====================  自動操作  =====================

    private void DriveMovement()
    {
        float vx = Mathf.Sin((float)_t * 1.3f) * 0.9f + Mathf.Sin((float)_t * 0.37f) * 0.4f;
        float vy = Mathf.Sin((float)_t * 0.8f + 1.1f) * 0.55f;
        SetAxis("ui_left", "ui_right", vx);
        SetAxis("ui_up", "ui_down", vy);
    }

    private static void SetAxis(string neg, string pos, float v)
    {
        v = Mathf.Clamp(v, -1f, 1f);
        Send(new InputEventAction { Action = pos, Pressed = v > 0.05f, Strength = Mathf.Max(0f, v) });
        Send(new InputEventAction { Action = neg, Pressed = v < -0.05f, Strength = Mathf.Max(0f, -v) });
    }

    // Z パルス：撃つ＋会話送り。会話中は読める速さに落とす。
    private void DriveShootAndAdvance(double delta)
    {
        bool talking = Hud.BubblePaused;
        double period = talking ? 0.5 : 0.16;
        _zPhase += delta;
        if (_zPhase >= period) _zPhase -= period;
        bool down = _zPhase < period * 0.45;
        if (down != _zDown)
        {
            _zDown = down;
            Send(new InputEventKey { Keycode = Key.Z, Pressed = down });
        }
    }

    private void DriveBomb(double delta)
    {
        _bombPhase += delta;
        bool wantPress = !Hud.BubblePaused && _bombPhase >= 18.0;
        if (wantPress && !_xDown)
        {
            _xDown = true;
            Send(new InputEventKey { Keycode = Key.X, Pressed = true });
        }
        else if (_xDown && _bombPhase >= 18.0 + 0.12)
        {
            _xDown = false;
            _bombPhase = 0.0;
            Send(new InputEventKey { Keycode = Key.X, Pressed = false });
        }
    }

    // god：自機周囲の敵弾を消して死なせない（進行テスト用。当たり判定テストでは使わない）。
    private void GodClear(Vector2 ppos)
    {
        if (_pool == null) return;
        foreach (Node n in GetTree().GetNodesInGroup("enemy_bullets"))
            if (n is Bullet b && b.Active && ppos.DistanceTo(b.GlobalPosition) <= GodClearRadius)
                _pool.Despawn(b);
    }

    // aim：最寄りの敵へ自機弾を撃ち込み、ボスを確実に削る（進行テスト用）。
    private void AimAssist(double delta, Vector2 ppos)
    {
        if (_pool == null) return;
        _aimT += delta;
        if (_aimT < AimInterval) return;
        _aimT = 0;

        Node2D? target = null;
        float best = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is Enemy e && !e.IsPurified)
            {
                float d = ppos.DistanceTo(e.GlobalPosition);
                if (d < best) { best = d; target = e; }
            }
        }
        if (target == null) return;
        Vector2 dir = (target.GlobalPosition - ppos).Normalized();
        if (dir == Vector2.Zero) dir = Vector2.Right;
        _pool.Spawn(ppos + dir * 16f, dir * 460f, isEnemy: false, 3f, 1);
    }

    // =====================  異常検出  =====================

    private void Heartbeat(double delta)
    {
        // シーン遷移ログ
        string scene = CurrentSceneName();
        if (scene != _scene)
        {
            GD.Print($"[QA] scene -> {scene} (t={_t:0.0})");
            _scene = scene;
            _sceneEnterT = _t;
            _lastProgressT = _t;     // 遷移は進捗
            _stuckReported = false;
        }

        _hbT += delta;
        if (_hbT < HeartbeatInterval) return;
        _hbT = 0;

        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        int lives = player?.Lives ?? -1;
        Vector2 pos = player?.GlobalPosition ?? Vector2.Zero;
        int purified = _game?.PurifiedCount ?? -1;
        int target = _game?.StageTarget ?? -1;
        float prog = _game?.StageProgress ?? 0f;
        int bombs = _game?.Bombs ?? -1;
        float bossMin = BossMinHp();
        int enemies = GetTree().GetNodesInGroup("enemies").Count;
        int ebul = GetTree().GetNodesInGroup("enemy_bullets").Count;
        int pbul = GetTree().GetNodesInGroup("player_bullets").Count;
        double fps = Engine.GetFramesPerSecond();

        GD.Print($"[QA] t={_t:000.0} scene={_scene} lives={lives} bombs={bombs} " +
                 $"purified={purified}/{target} prog={prog:0.00} boss={(bossMin > 1.5f ? "-" : bossMin.ToString("0.00"))} " +
                 $"enemies={enemies} ebul={ebul} pbul={pbul} fps={fps:0} pos={Fmt(pos)}");
    }

    private void DetectStuck()
    {
        // 進捗シグナル：浄化数増 / ボスHP減 / 会話の開閉トグル（シーン変化は Heartbeat 側で更新）
        int purified = _game?.PurifiedCount ?? 0;
        float bossMin = BossMinHp();
        bool bubble = Hud.BubblePaused;

        bool progressed = false;
        if (purified != _prevPurified) { _prevPurified = purified; progressed = true; }
        if (bossMin < _prevBossMin - 0.001f) { progressed = true; }
        if (bossMin < _prevBossMin) _prevBossMin = bossMin;
        if (bossMin > 1.5f) _prevBossMin = 2f; // ボス不在ならリセット（次戦に備える）
        if (bubble != _prevBubble) { _prevBubble = bubble; progressed = true; }

        if (progressed) { _lastProgressT = _t; _stuckReported = false; }

        if (!_stuckReported && _t - _lastProgressT > StuckTimeout)
        {
            _stuckReported = true;
            Flag("stuck",
                $"no progress for {_t - _lastProgressT:0}s in scene={_scene} " +
                $"(purified={purified} bossMin={(bossMin > 1.5f ? "-" : bossMin.ToString("0.00"))} bubble={bubble}). 進行不能の疑い t={_t:0.0}");
        }
    }

    private void DetectBulletFlood()
    {
        int ebul = GetTree().GetNodesInGroup("enemy_bullets").Count;
        if (ebul > FloodThreshold)
            Flag("bullet-flood", $"enemy_bullets={ebul} (>{FloodThreshold}) scene={_scene} t={_t:0.0}");
    }

    private void DetectLowFps(double delta)
    {
        double fps = Engine.GetFramesPerSecond();
        if (fps > 0 && fps < LowFpsThreshold) _lowFpsT += delta;
        else _lowFpsT = 0;
        if (_lowFpsT >= LowFpsWindow)
        {
            _lowFpsT = 0;
            Flag("low-fps", $"fps={fps:0} sustained {LowFpsWindow:0}s scene={_scene} t={_t:0.0}");
        }
    }

    // =====================  ヘルパ  =====================

    private float BossMinHp()
    {
        float min = 2f;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && e.HasHpBar && !e.IsPurified)
                if (e.HpRatio < min) min = e.HpRatio;
        return min;
    }

    private string CurrentSceneName()
    {
        var cs = GetTree().CurrentScene;
        if (cs == null) return "?";
        string path = cs.SceneFilePath;
        if (string.IsNullOrEmpty(path)) return cs.Name;
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    private void EndRun()
    {
        PrintSummary();
        GetTree().Quit();
    }

    private void PrintSummary()
    {
        if (_counts.Count == 0)
        {
            GD.Print("[QA-SUMMARY] no anomalies flagged. clean run.");
            return;
        }
        var parts = new List<string>();
        foreach (var kv in _counts) parts.Add($"{kv.Key}={kv.Value}");
        GD.Print("[QA-SUMMARY] " + string.Join(" ", parts));
    }

    // 異常を記録＆出力。oob は致命なので ERROR、それ以外は WARN。
    private void Flag(string kind, string msg)
    {
        _counts.TryGetValue(kind, out int c);
        _counts[kind] = c + 1;
        string level = kind == "player-oob" ? "QA-ERROR" : "QA-WARN";
        GD.Print($"[{level}] {kind}: {msg}");
    }

    private static bool IsFinite(Vector2 v) =>
        !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsInfinity(v.X) || float.IsInfinity(v.Y));

    private static string Fmt(Vector2 v) => $"({v.X:0},{v.Y:0})";

    private static void Send(InputEvent e) => Input.ParseInputEvent(e);
}
