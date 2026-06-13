using Godot;
using System.Collections.Generic;

// DemoPilot : デモプレイ動画用の自動操縦オートロード (/root/DemoPilot)。
//
// 起動コマンドのユーザ引数（`-- --demo` のように `--` の後ろ）に "--demo" が
// 含まれているときだけ有効化し、合成入力を Input.ParseInputEvent で流して
// ゲームを“勝手にプレイ”させる。Player.cs 等はキーをポーリングしているだけなので、
// 本物のキー入力と同じ経路で合成イベントを注入すれば、ゲーム側を一切書き換えずに動く。
//
//   有効化:   res://...tscn -- --demo
//   尺の指定: -- --demo --seconds 80        （省略時 DefaultSeconds）
//   難易度:   -- --demo --normal / --hard   （既定は Easy ＝ 安全運転）
//
// 設計思想：「ノーダメージで、できるだけ速くクリアする」AI。被弾は絶対に避けつつ
// （死ぬと話が中断しリスタートしてしまう）、無駄な時間を一切作らないよう振る舞う：
//   ・回避は開ループのサイン波ではなく、敵弾を毎フレーム観測して速度ごと先読みする
//     閉ループ。16方向＋静止を弾道シミュレーションし、最も自機から弾が離れる動きを選ぶ。
//   ・脅威が無いときは攻撃定位置（自機は右へ撃つので左寄り＋ボスのYに合わせる）へ
//     張り付いて撃ち続け、最大DPSでボスのパネルを剥がす。ボスHPは難易度非依存で固定
//     なので（弾数だけが変わる）、Easy でもクリア所要は変わらず、むしろ弾が薄く回避に
//     使う時間が減る＝攻撃定位置に居られる時間が増える＝最速。だから既定は Easy。
//   ・会話送りは“読める速さ”ではなく最速（各行のゲート 0.25s ぎりぎり）でスキップする。
//     戦闘中は Z を押しっぱなしにして最大火力。会話中は弾も自機も止まる設計なので安全。
//   ・どう動いても被弾が避けられない瞬間だけ、最後の保険としてボム（画面弾消し＋無敵）。
//   ・指定秒で GetTree().Quit() し、--write-movie の録画を確定させる。
//
// ※「ストーリーをゆっくり見せたい」用途には不向き（会話を高速スキップする）。
//   その場合は StoryPeriod を大きく戻す。
public partial class DemoPilot : Node
{
    private const double DefaultSeconds = 80.0;

    // ---- プレイ領域・自機諸元（Player.cs と一致）----
    private const float MinX = 0f, MaxX = 384f, MinY = 0f, MaxY = 216f;
    private const float PlayerSpeed = 150f;   // Player.NormalSpeed
    private const float HitRadius = 2f;        // Player.HitRadius（極小の被弾点）

    // ---- 回避パラメータ ----
    private const int DirCount = 16;            // 評価する移動方向数（これに静止を加える）
    private const float Horizon = 0.5f;         // 先読み秒数
    private const int Steps = 8;                // 先読みの時間分割
    private const float NearRange = 150f;       // この距離内の脅威だけ評価（負荷削減）
    private const float GapCap = 24f;           // これ以上のクリアランスは同点扱い（無駄に逃げない）
    private const float SafeGap = 11f;          // 静止してもこれ以上空くなら「安全」＝攻撃定位置へ。
                                                // 小さいほど逃げ腰をやめて撃ち続ける＝速いが攻めすぎ注意。
    private const float EnemyRadius = 14f;      // 敵本体の安全側半径（BodyRadius は private）
    private const float HomeX = 104f;           // 攻撃定位置のX（自機は右へ撃つので左寄り。
                                                // ボスにやや近づけて弾の到達を早め、削り出しを速める）
    private const float PanicGap = 1.0f;        // 最善手でもこれ未満＝被弾不可避 → ボム
    private const double BombRearm = 2.0;       // ボム連発防止の再武装待ち
    private const double StoryPeriod = 0.30;    // 会話送りの周期。各行の最短ゲート 0.25s ぎりぎりまで
                                                // 詰めた最速スキップ（読ませる用途なら大きくする）。

    // ---- フラグ・時間 ----
    private bool _active;
    private GameManager.Diff _diff = GameManager.Diff.Easy; // 既定は安全運転
    private double _seconds = DefaultSeconds;
    private double _t;

    private GameManager _game = null!;

    // ---- 入力パルス状態 ----
    private bool _zDown;
    private double _zPhase;
    private bool _xDown;
    private double _bombArm;
    private int _prevDir;       // 慣性用（0=静止, 1..DirCount=方向インデックス+1）

    // ---- 毎フレーム再利用する脅威リスト（割り当てを抑える）----
    private readonly List<(Vector2 pos, Vector2 vel, float rad)> _threats = new();
    private float _aimY = 108f;  // 攻撃定位置のY（最寄りの敵に合わせる）

    public override void _Ready()
    {
        var user = OS.GetCmdlineUserArgs();
        for (int i = 0; i < user.Length; i++)
        {
            switch (user[i])
            {
                case "--demo": _active = true; break;
                case "--normal": _diff = GameManager.Diff.Normal; break;
                case "--hard": _diff = GameManager.Diff.Hard; break;
                case "--easy": _diff = GameManager.Diff.Easy; break;
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

        // Game は DemoPilot より前に登録済みのオートロード（project.godot 参照）。
        // 自機の残機・ボム数やスポーンの弾数/弾速はここで決めた難易度を読む。
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (_game != null) _game.Difficulty = _diff;

        GD.Print($"[DemoPilot] active. recording {_seconds:0}s of autoplay. difficulty={(_game?.DiffName ?? "?")}");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t >= _seconds)
        {
            GD.Print("[DemoPilot] done. quitting to finalize movie.");
            GetTree().Quit();
            return;
        }
        DriveShootAndAdvance(delta);
    }

    // 当たり判定・弾の移動は物理フレームで起きるので、回避もここで観測・操作する。
    // オートロードはシーンより先に処理されるため、ここで入力を仕込めば同フレームの
    // Player._PhysicsProcess が GetVector でそれを読む。
    public override void _PhysicsProcess(double delta)
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        bool talking = Hud.BubblePaused;

        float bestGap = 9999f;
        if (player == null || talking)
        {
            // 会話中は弾も自機も止まる＝動かす必要なし。軸を解放しておく。
            ReleaseAxes();
        }
        else
        {
            bestGap = DriveDodge(player.GlobalPosition);
        }

        DriveBomb(delta, bestGap, talking);
    }

    // =====================  回避ブレイン  =====================

    // 戻り値：どう動いても確保できる“最善のクリアランス”（ボム判定に使う）。
    private float DriveDodge(Vector2 ppos)
    {
        BuildThreats(ppos);

        // 静止し続けたときの最接近。十分空くなら危険でない → 攻撃定位置へ寄せる。
        float stayGap = Simulate(ppos, Vector2.Zero);
        if (stayGap >= SafeGap)
        {
            SeekHome(ppos);
            return 9999f;
        }

        // 危険：16方向＋静止を弾道シミュレーションし、最も弾が離れる動きを選ぶ。
        float bestScore = float.NegativeInfinity;
        float bestGap = stayGap;
        Vector2 bestDir = Vector2.Zero;
        int bestIdx = 0;

        // 候補0：静止（小さなボーナスでムダな揺れを抑える）
        {
            float score = Mathf.Min(stayGap, GapCap) + 0.6f + (_prevDir == 0 ? 1.0f : 0f);
            bestScore = score; bestDir = Vector2.Zero; bestIdx = 0;
        }

        for (int i = 0; i < DirCount; i++)
        {
            float a = Mathf.Tau * i / DirCount;
            Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
            float g = Simulate(ppos, dir);
            if (g > bestGap) bestGap = g;

            float score = Mathf.Min(g, GapCap);
            if (_prevDir == i + 1) score += 1.0f; // 慣性：同じ方向を続けると見栄えが安定
            // 同点帯では攻撃定位置に近い着地点を僅かに優遇（隅に追い込まれにくくする）
            Vector2 end = ppos + dir * PlayerSpeed * Horizon;
            float distHome = Mathf.Abs(end.X - HomeX) + Mathf.Abs(end.Y - _aimY);
            score -= 0.012f * distHome;

            if (score > bestScore) { bestScore = score; bestDir = dir; bestIdx = i + 1; }
        }

        _prevDir = bestIdx;
        ApplyMove(bestDir, 1f); // 回避は全速
        return bestGap;
    }

    // 攻撃定位置（HomeX, 最寄りの敵のY）へ向けて、距離に比例した強さでにじり寄る。
    // 近づくほど弱くするので行き過ぎ・小刻みな揺れが出ない＝意図ある定位置取りに見える。
    private void SeekHome(Vector2 ppos)
    {
        Vector2 home = new(HomeX, _aimY);
        Vector2 d = home - ppos;
        float dist = d.Length();
        if (dist < 1.5f) { ReleaseAxes(); _prevDir = 0; return; }
        ApplyMove(d / dist, Mathf.Clamp(dist / 24f, 0f, 1f));
        _prevDir = 0;
    }

    // 近傍の敵弾（速度つき）と敵本体を脅威として集める。攻撃定位置のYも更新。
    private void BuildThreats(Vector2 ppos)
    {
        _threats.Clear();
        float near2 = NearRange * NearRange;

        foreach (Node n in GetTree().GetNodesInGroup("enemy_bullets"))
            if (n is Bullet b && b.Active && ppos.DistanceSquaredTo(b.GlobalPosition) <= near2)
                _threats.Add((b.GlobalPosition, b.Velocity, b.Radius));

        float bestEnemyY = 108f, bestEnemyDist = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.IsPurified) continue;
            Vector2 ep = e.GlobalPosition;
            float d2 = ppos.DistanceSquaredTo(ep);
            if (d2 <= near2) _threats.Add((ep, Vector2.Zero, EnemyRadius));
            if (d2 < bestEnemyDist) { bestEnemyDist = d2; bestEnemyY = ep.Y; }
        }
        _aimY = Mathf.Clamp(bestEnemyY, MinY + 8f, MaxY - 8f);
    }

    // dir 方向へ PlayerSpeed で進んだとき、先読み区間中に脅威表面とどれだけ近づくか。
    // 返すのは区間中の最小クリアランス（負＝重なり）。
    private float Simulate(Vector2 ppos, Vector2 dir)
    {
        float minGap = 9999f;
        for (int s = 0; s <= Steps; s++)
        {
            float tt = Horizon * s / Steps;
            Vector2 pp = ppos + dir * PlayerSpeed * tt;
            pp.X = Mathf.Clamp(pp.X, MinX, MaxX);
            pp.Y = Mathf.Clamp(pp.Y, MinY, MaxY);
            foreach (var th in _threats)
            {
                Vector2 bp = th.pos + th.vel * tt;
                float g = pp.DistanceTo(bp) - (th.rad + HitRadius);
                if (g < minGap)
                {
                    minGap = g;
                    if (minGap < -4f) return minGap; // 深く重なる方向はこれ以上見ても無駄
                }
            }
        }
        return minGap;
    }

    // 1軸ぶんの ui_neg / ui_pos を強さ付きで注入する（Player の GetVector がこれを読む）。
    private void ApplyMove(Vector2 dir, float strength)
    {
        SetAxis("ui_left", "ui_right", dir.X * strength);
        SetAxis("ui_up", "ui_down", dir.Y * strength);
    }

    private void ReleaseAxes()
    {
        SetAxis("ui_left", "ui_right", 0f);
        SetAxis("ui_up", "ui_down", 0f);
    }

    private static void SetAxis(string neg, string pos, float v)
    {
        v = Mathf.Clamp(v, -1f, 1f);
        Send(new InputEventAction { Action = pos, Pressed = v > 0.05f, Strength = Mathf.Max(0f, v) });
        Send(new InputEventAction { Action = neg, Pressed = v < -0.05f, Strength = Mathf.Max(0f, -v) });
    }

    // =====================  撃つ／会話送り  =====================

    // 戦闘中は Z を押しっぱなしで最大火力。会話中は最速でパルスして1行ずつ飛ばす
    //（会話送りは Z の押下エッジ＝1回押すごとに1行。各行 0.25s のゲートがあるのでそれ以上は速くならない）。
    private void DriveShootAndAdvance(double delta)
    {
        if (!Hud.BubblePaused)
        {
            _zPhase = 0;
            SetZ(true);
            return;
        }
        _zPhase += delta;
        if (_zPhase >= StoryPeriod) _zPhase -= StoryPeriod;
        SetZ(_zPhase < StoryPeriod * 0.4); // 周期の前半だけ押下 → 1周期に1回のエッジ
    }

    private void SetZ(bool down)
    {
        if (down == _zDown) return;
        _zDown = down;
        Send(new InputEventKey { Keycode = Key.Z, Pressed = down });
    }

    // =====================  ボム（最後の保険）  =====================

    // どう動いても避けられない瞬間（最善のクリアランスが PanicGap 未満）だけ、
    // ボムが残っていれば1発撃って画面の弾を消し被弾を防ぐ。会話中は撃たない。
    private void DriveBomb(double delta, float bestGap, bool talking)
    {
        if (_bombArm > 0) _bombArm -= delta;

        bool panic = !talking && bestGap < PanicGap && (_game?.Bombs ?? 0) > 0 && _bombArm <= 0;
        if (panic && !_xDown)
        {
            _xDown = true;
            Send(new InputEventKey { Keycode = Key.X, Pressed = true });
        }
        else if (_xDown)
        {
            _xDown = false;
            _bombArm = BombRearm;
            Send(new InputEventKey { Keycode = Key.X, Pressed = false });
        }
    }

    private static void Send(InputEvent e) => Input.ParseInputEvent(e);
}
