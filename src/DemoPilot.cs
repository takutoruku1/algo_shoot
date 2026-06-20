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

    // ---- 移動ブレインのパラメータ ----
    // 毎フレーム「16方向＋静止＋定位置へぴたり寄る」候補を弾道シミュレーションし、
    //   スコア = 安全度(min(クリアランス,GapCap)) ＋ ホーム接近ボーナス
    // が最大の動きを選ぶ。ただしホームボーナスは“安全な手”(クリアランス≥ComfortGap)にしか
    // 与えない。これで「安全は常に最優先（射線取りのために危険へ突っ込まない）／安全な範囲では
    // できるだけ射線(ボスのY)に張り付いて最大DPS」を両立する。
    private const int DirCount = 16;            // 評価する移動方向数（これに静止＋定位置寄せを加える）
    private const float Horizon = 0.5f;         // 先読み秒数
    private const int Steps = 8;                // 先読みの時間分割
    private const float NearRange = 150f;       // この距離内の脅威だけ評価（負荷削減）
    private const float GapCap = 24f;           // これ以上のクリアランスは同点扱い（無駄に逃げない）
    private const float ComfortGap = 8f;        // クリアランスがこれ未満の手にはホーム加点しない
                                                // ＝危険な手は純粋な安全度のみで選ぶ（被弾防止の要）。
    private const float HomeBonus = 6f;         // ホーム接近ボーナスの最大値（安全度レンジ24より十分小さく）
    private const float HomeFar = 180f;         // この距離で接近ボーナス0、近いほど満点に近づく
    private const float Hyst = 1.0f;            // 同じ選択を続ける慣性（ガタつき防止）
    private const float StayBonus = 0.5f;       // 静止のわずかな優遇（無駄な揺れを抑える）
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
    private double _wordCamT; // [一時/デバッグ] 言葉弾スクショ用タイマー

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

        // [一時/デバッグ] 言葉弾の当たり点確認用：ゆっくり降る言葉弾を定期的に出す。
        if (player != null && !talking)
        {
            _wordCamT += delta;
            if (_wordCamT >= 0.5)
            {
                int k = (int)(_t / 0.5);
                _wordCamT = 0;
                var pool = GetNodeOrNull<BulletPool>("/root/Pool");
                string[] ws = { "努力は天才に勝てない", "届かない", "どうせ二番", "私を見て" };
                pool?.Spawn(new Vector2(70f + (k * 53 % 240), -6f), new Vector2(0f, 28f), isEnemy: true, 3f, 1)?.SetWord(ws[k % ws.Length]);
            }
        }

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

    // =====================  移動ブレイン（回避＋射線張り付き）  =====================

    // 候補（速度ベクトル）を全部弾道シミュレーションして1手選ぶ統一スコアラ。
    // 戻り値：どう動いても確保できる“最善のクリアランス”（ボム判定に使う）。
    private float DriveDodge(Vector2 ppos)
    {
        BuildThreats(ppos);
        Vector2 home = new(HomeX, _aimY); // 攻撃定位置（左寄りＸ・ボスのＹ）

        float bestScore = float.NegativeInfinity;
        float bestAchievableGap = -9999f;
        Vector2 bestVel = Vector2.Zero;
        int bestIdx = 0;

        // 1つの候補速度を評価して暫定ベストを更新する。
        void Consider(Vector2 vel, int idx)
        {
            float g = Simulate(ppos, vel);
            if (g > bestAchievableGap) bestAchievableGap = g;

            float score = Mathf.Min(g, GapCap); // 安全度（クリアランス）が土台
            // ホーム接近ボーナスは“安全な手”だけに与える。危険な手(クリアランス<ComfortGap)は
            // 純粋に安全度だけで競わせる＝射線取りのために弾へ突っ込まない（被弾防止の要）。
            if (g >= ComfortGap)
            {
                Vector2 end = ppos + vel * Horizon;
                end.X = Mathf.Clamp(end.X, MinX, MaxX);
                end.Y = Mathf.Clamp(end.Y, MinY, MaxY);
                float homeReward = 1f - Mathf.Clamp(end.DistanceTo(home) / HomeFar, 0f, 1f);
                score += HomeBonus * homeReward;
            }
            if (idx == _prevDir) score += Hyst;
            if (idx == 0) score += StayBonus;

            if (score > bestScore) { bestScore = score; bestVel = vel; bestIdx = idx; }
        }

        Consider(Vector2.Zero, 0);                                  // 静止
        for (int i = 0; i < DirCount; i++)                          // 16方向・全速
        {
            float a = Mathf.Tau * i / DirCount;
            Consider(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * PlayerSpeed, i + 1);
        }
        // 攻撃定位置へ“ぴたり”寄る候補（近いほど低速＝行き過ぎない）。これで射線へ正確に乗る。
        Vector2 toHome = home - ppos;
        float dist = toHome.Length();
        if (dist > 0.5f)
            Consider(toHome / dist * Mathf.Min(PlayerSpeed, dist / (float)Horizon), DirCount + 1);

        _prevDir = bestIdx;
        ApplyMove(bestVel);
        return bestAchievableGap;
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

    // 速度 vel(px/s) で進んだとき、先読み区間中に脅威表面とどれだけ近づくか。
    // 返すのは区間中の最小クリアランス（負＝重なり）。
    private float Simulate(Vector2 ppos, Vector2 vel)
    {
        float minGap = 9999f;
        for (int s = 0; s <= Steps; s++)
        {
            float tt = Horizon * s / Steps;
            Vector2 pp = ppos + vel * tt;
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

    // 速度ベクトルを各軸の強さ(-1..1)に直して注入する（Player の GetVector がこれを読む）。
    private void ApplyMove(Vector2 vel)
    {
        SetAxis("ui_left", "ui_right", vel.X / PlayerSpeed);
        SetAxis("ui_up", "ui_down", vel.Y / PlayerSpeed);
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
