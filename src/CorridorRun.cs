using Godot;

// CorridorRun : あかり戦の中盤ギミック「雨の帰り道」＝横スクロールのイライラ棒。
//   全高216pxの壁カラム（厚12px・間隔24px）が右→左へ流れ、隙間（通路）が縦に蛇行する。
//   プレイヤーは12秒間、通路を縫って生き延びる（尺は全難易度固定＝差は幅/速度/蛇行で付ける）。
//   ・開始前に1.5sの非致死プレビュー（壁α0.35・判定なし）＝「これが来る」を先に見せる（§7 理不尽回避）。
//   ・本番の最初の3sに自機が出会う壁は直進・広幅（G×1.5）＝学習区間。蛇行は先に右から見えてくる。
//   ・蛇行の最大縦速度は難易度別でも必ず自機速度150px/sより低い＝「入力し続ければ必ず追える」公平ライン。
//   ・壁接触＝Player.TakeHit() 1回（Player側の被弾無敵1.2sが復帰猶予。--god 中はスキップ）。
//   ・ボム＝壁は消えないが 2.5s 間 通路幅×1.6（緊急脱出はできる・攻略のスキップはさせない）。
//   ・通路中央線上に「言葉の残滓」（弾なし・不可触の小ザコ）が3体流れてくる。撃破でボス総HPの4%直撃。
//   ・ボス（発生源）が浄化されたら通路は即時解散（残留攻撃を断つ＝AreaSpellCaster と同じ思想）。
//   ・会話中(Hud.BubblePaused)は完全フリーズ（AreaStrike と同作法）。
// 視覚言語は AreaStrike 踏襲（面塗り＋通路縁の危険ライン・あかり心象色 6c9cd8）。
// QA互換：AreaStrike と同じ "aoe" グループ＋ IsStriking/CoversPoint（IAoeHazard）を実装する。
//
// 使い方（BossAkari.StartCorridor）：
//   var c = new CorridorRun { Boss = this };
//   world.AddChild(c); c.GlobalPosition = Vector2.Zero;   // 画面座標基準で描く
public partial class CorridorRun : Node2D, IAoeHazard
{
    private const float W = 384f, H = 216f;
    private const float PlayerHit = 2.5f;       // 自機の被弾半径ぶんの寄せ（AreaStrike と同値）
    private const float WallThick = 12f;        // 壁カラムの厚み
    private const float WallStride = 24f;       // カラム間隔（中心から中心）
    private const double PreviewDur = 1.5;      // 非致死プレビュー
    private const double RunDur = 12.0;         // 本番（全難易度固定）
    private const double DissolveDur = 0.5;     // 終了後のフェードアウト
    private const double StraightDur = 3.0;     // 本番冒頭の直進・広幅（学習区間）
    private const double WidenBlend = 1.0;      // 広幅(G×1.5)→通常幅Gへ絞る移行秒
    private const double BombWideDur = 2.5;     // ボムで通路幅×1.6 する時間
    private const float BombWideMul = 1.6f;
    private const float SampleStep = 4f;        // 中心線サンプルの距離刻み(px)
    private static readonly Color Tint = new("6c9cd8"); // あかり心象色（AreaSpellCaster "akari" と同色）
    private static readonly Color Hot = new("a9dcff");

    // このギミックの発生源（BossAkari）。浄化されたら通路は即時解散する。
    public Enemy? Boss;

    // 進行状態（BossAkari / DemoPilot が読む）。
    public bool Finished { get; private set; }              // 12s完走（or 解散）した
    public bool RunActive => !Finished && _t >= PreviewDur; // 壁が致死の間
    public bool Steering => !Finished;                      // DemoPilot が中心線追従すべき間（プレビュー含む）
    public float ScrollSpeed => _scrollSpeed;

    // QaPilot観測（IAoeHazard）：走行中の壁は常時致死＝覆われての被弾は正規（suspicious にしない）。
    public bool IsStriking => RunActive;
    public bool CoversPoint(Vector2 p) => InsideWall(p);

    // 難易度パラメータ：通路幅G / スクロール速度 / 蛇行の最大縦速度(px/s)。
    private float _gap = 52f, _scrollSpeed = 70f, _maxVy = 80f;

    private readonly RandomNumberGenerator _rng = new();
    private double _t;              // 経過（プレビュー込み）。BubblePaused 中は進まない
    private float _scroll;          // 走行距離(px)＝壁の左流れ量
    private double _bombWideT;      // ボム拡張の残り
    private int _prevBombCount = -1; // Player.BombCount（累計発動回数）の増加でボム発動を検出
    private double _hitCd;          // TakeHit 呼び出しの間引き（本体の復帰猶予は Player 側の無敵1.2s）
    private bool _dissolving;
    private double _dissolveT;

    // 中心線（距離d[px]→通路中央Y）のサンプル列。_Ready で全長ぶん先に生成する（決定的な地形＝理不尽回避）。
    private float[] _center = System.Array.Empty<float>();
    private float _dStraight;       // ここまでのdは直進・広幅（自機x≈100が本番3s目に出会う位置まで）
    private float _blendDist;       // 広幅→通常幅の移行距離

    // 言葉の残滓：本番経過 4s/7s/10s の3体。通路中央線上を壁と一緒に流れてくる。
    private static readonly double[] RemnantTimes = { 4.0, 7.0, 10.0 };
    private int _remnantSpawned;

    public override void _Ready()
    {
        _rng.Randomize();
        AddToGroup("aoe");       // QaPilot が IAoeHazard として走査（AreaStrike と同じ観測経路）
        AddToGroup("corridor");  // StageAkari（投稿弾の停止）／DemoPilot（中心線追従）が探す
        ZIndex = 5; ZAsRelative = false; // 弾(0)より上・自機(10)より下（全画面AOEと同じ層）

        // 難易度：G / スクロール速度 / 蛇行最大縦速度。縦速度は自機150px/sを厳守で下回る。
        var diff = GetNodeOrNull<GameManager>("/root/Game")?.Difficulty ?? GameManager.Diff.Normal;
        (_gap, _scrollSpeed, _maxVy) = diff switch
        {
            GameManager.Diff.Easy => (64f, 60f, 55f),
            GameManager.Diff.Hard => (42f, 80f, 100f),
            GameManager.Diff.Lunatic => (34f, 90f, 115f),
            _ => (52f, 70f, 80f),
        };

        // 中心線ジェネレータ：傾きクランプ付き区分サイン。
        //   slope(d) = (maxVy/scroll) × sin(θ)。θ は波長ごとに進み、1周ごとに波長を引き直す＝区分サイン。
        //   これで「自機の観測する縦速度 = slope×scroll ≦ maxVy」が構造的に保証される（クランプ）。
        _dStraight = 100f + _scrollSpeed * (float)(PreviewDur + StraightDur);
        _blendDist = _scrollSpeed * (float)WidenBlend;
        float maxDist = W + WallStride + _scrollSpeed * (float)(PreviewDur + RunDur) + 64f;
        int n = Mathf.CeilToInt(maxDist / SampleStep) + 2;
        _center = new float[n];
        float maxSlope = _maxVy / _scrollSpeed;
        float yMargin = _gap * 0.8f + 10f; // 中央線が端に寄りすぎない（ボム拡幅1.6×の半幅＋余白）
        float y = H / 2f;
        double theta = 0;
        float waveLen = _rng.RandfRange(90f, 170f);
        for (int i = 0; i < n; i++)
        {
            _center[i] = y;
            float d = i * SampleStep;
            if (d < _dStraight) continue; // 学習区間＝直進
            y += maxSlope * Mathf.Sin((float)theta) * SampleStep;
            y = Mathf.Clamp(y, yMargin, H - yMargin);
            theta += Mathf.Tau / waveLen * SampleStep;
            if (theta >= Mathf.Tau) { theta -= Mathf.Tau; waveLen = _rng.RandfRange(90f, 170f); }
        }

        // 通路開始＝盤面を掃除（残弾に轢かれながらの進入は理不尽）。以降の投稿弾は StageAkari 側が止める。
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
    }

    // 距離d[px]の通路中央Y。DemoPilot/残滓は GuideYAt（画面x基準）で引く。
    private float CenterAtD(float d)
    {
        float fi = Mathf.Clamp(d / SampleStep, 0, _center.Length - 1.001f);
        int i = (int)fi;
        return Mathf.Lerp(_center[i], _center[i + 1], fi - i);
    }
    public float GuideYAt(float worldX) => CenterAtD(worldX + _scroll);

    // 距離dの通路幅（学習区間の広幅→通常幅の移行＋ボム拡張を織り込む）。
    private float GapAtD(float d)
    {
        float g = d < _dStraight ? _gap * 1.5f
                : d < _dStraight + _blendDist ? Mathf.Lerp(_gap * 1.5f, _gap, (d - _dStraight) / _blendDist)
                : _gap;
        if (_bombWideT > 0) g *= BombWideMul;
        return g;
    }

    // 点pが壁の中か（当たり判定。CoversPoint と共用）。近傍3カラムだけ調べる。
    private bool InsideWall(Vector2 p)
    {
        int kc = Mathf.RoundToInt((p.X + _scroll) / WallStride);
        for (int k = kc - 1; k <= kc + 1; k++)
        {
            if (k < 0) continue;
            float colD = k * WallStride;
            float colX = colD - _scroll;
            if (Mathf.Abs(p.X - colX) > WallThick / 2f + PlayerHit) continue;
            float half = GapAtD(colD) / 2f;
            if (Mathf.Abs(p.Y - CenterAtD(colD)) >= half - PlayerHit) return true;
        }
        return false;
    }

    public override void _PhysicsProcess(double delta)
    {
        // 終了後：フェードアウトして退場（判定なし）。
        if (_dissolving)
        {
            _dissolveT += delta;
            QueueRedraw();
            if (_dissolveT >= DissolveDur) QueueFree();
            return;
        }

        if (Hud.BubblePaused) return; // 会話中は完全フリーズ（AreaStrike:110 と同作法）

        // 発生源（ボス）が浄化されたら通路は即時解散＝改心の会話に壁が残らない。
        if (Boss == null || !IsInstanceValid(Boss) || Boss.IsPurified) { Dissolve(); return; }

        _t += delta;
        _scroll += _scrollSpeed * (float)delta;
        if (_bombWideT > 0) _bombWideT -= delta;
        if (_hitCd > 0) _hitCd -= delta;

        var player = GetTree().GetFirstNodeInGroup("player") as Player;

        // ボム検出：BombCount（累計発動回数）の増加をポーリング → 2.5s 通路幅×1.6（壁は消さない）。
        if (player != null)
        {
            if (_prevBombCount >= 0 && player.BombCount > _prevBombCount) _bombWideT = BombWideDur;
            _prevBombCount = player.BombCount;
        }

        // 言葉の残滓：本番 4s/7s/10s に右端の中央線上へ1体ずつ（撃破報酬はボス総HPの4%直撃＝難易度非依存）。
        double runT = _t - PreviewDur;
        while (_remnantSpawned < RemnantTimes.Length && runT >= RemnantTimes[_remnantSpawned])
        {
            _remnantSpawned++;
            var r = new RemnantEnemy { Corridor = this, BossRef = Boss };
            GetParent().AddChild(r);
            float sx = W + 12f;
            r.GlobalPosition = new Vector2(sx, GuideYAt(sx));
        }

        // 壁接触＝被弾1回。QAの --god（assist走行）中はスキップ（AreaStrike.Strike:142-145 と同じ作法）。
        // 復帰猶予は Player 側の被弾無敵1.2s（TakeHit 自身が無敵中を弾く）。_hitCd は呼び出しの間引きだけ。
        if (RunActive && player != null && _hitCd <= 0 && InsideWall(player.GlobalPosition))
        {
            _hitCd = 0.2;
            if (!QaPilot.GodActive) player.TakeHit();
        }

        if (_t >= PreviewDur + RunDur) { Finished = true; _dissolving = true; } // 完走＝出口（報酬はボス側）
        QueueRedraw();
    }

    // 即時解散（ボス浄化時）。Finished も立てる＝ボス側の進行が固まらない。
    private void Dissolve()
    {
        Finished = true;
        _dissolving = true;
    }

    public override void _Draw()
    {
        // プレビュー＝薄く（判定なしの予告）。解散＝フェードアウト。
        float alpha = _t < PreviewDur ? 0.35f : 1f;
        if (_dissolving) alpha *= 1f - (float)(_dissolveT / DissolveDur);
        if (alpha <= 0f) return;

        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_t * 6f);
        var fill = new Color(Tint.R, Tint.G, Tint.B, 0.30f * alpha);
        var deep = new Color(Tint.R * 0.5f, Tint.G * 0.5f, Tint.B * 0.6f, 0.35f * alpha); // 壁の芯（雨脚の濃い帯）
        // ボム拡張中は縁を白熱色に＝「いま広い」を色でも伝える。
        var edgeBase = _bombWideT > 0 ? Hot : Tint;

        int kMin = Mathf.Max(0, Mathf.FloorToInt((_scroll - WallThick) / WallStride));
        int kMax = Mathf.FloorToInt((_scroll + W + WallThick) / WallStride);
        for (int k = kMin; k <= kMax; k++)
        {
            float colD = k * WallStride;
            float colX = colD - _scroll;
            if (colX < -WallThick || colX > W + WallThick) continue;
            float c = CenterAtD(colD);
            float half = GapAtD(colD) / 2f;
            float top = c - half, bot = c + half;
            float x0 = colX - WallThick / 2f;
            if (top > 0f)
            {
                DrawRect(new Rect2(x0, 0, WallThick, top), fill);
                DrawRect(new Rect2(x0 + 3f, 0, WallThick - 6f, top), deep);
            }
            if (bot < H)
            {
                DrawRect(new Rect2(x0, bot, WallThick, H - bot), fill);
                DrawRect(new Rect2(x0 + 3f, bot, WallThick - 6f, H - bot), deep);
            }
            // 通路縁の危険ライン（AreaStrike の予兆輪郭と同語彙）：カラムごとに位相をずらして明滅＝流れが読める。
            float ep = 0.45f + 0.45f * Mathf.Sin((float)_t * 7f + k * 0.9f);
            var edge = new Color(edgeBase.R, edgeBase.G, edgeBase.B, ep * alpha);
            DrawLine(new Vector2(x0, top), new Vector2(x0 + WallThick, top), edge, 1.5f);
            DrawLine(new Vector2(x0, bot), new Vector2(x0 + WallThick, bot), edge, 1.5f);
        }

        // プレビュー中：中央に「来る」の合図（点滅する開始予告）。文字は使わず白フレーム収束（Fullscreen と同語彙）。
        if (_t < PreviewDur)
        {
            float cprog = (float)(_t / PreviewDur);
            float inset = Mathf.Lerp(0f, 10f, cprog);
            DrawRect(new Rect2(inset, inset, W - inset * 2f, H - inset * 2f),
                     new Color(1f, 1f, 1f, 0.35f * cprog * (0.5f + 0.5f * pulse)), false, 2f);
        }
    }
}

// RemnantEnemy : 通路に流れてくる「言葉の残滓」＝渡せなかった言葉の切れ端（小さな黒吹き出し）。
//   弾は撃たない・体も不可触（通路の正解位置＝中央線上に居るので、接触で痛いと理不尽）。
//   パネル1枚（インク1）を剥がすと浄化＝撃破。報酬はボス総HPの4%直撃（難易度非依存）。
//   ボムの一括浄化（Player.Bomb → Enemy.Purify）で3体まとめて取れるのは許容（設計済み）。
public partial class RemnantEnemy : Enemy
{
    public CorridorRun? Corridor;   // 中心線追従＆流速の参照（先に消えたら等速で流れ続ける）
    public Enemy? BossRef;          // 撃破報酬の宛先（BossAkari）

    protected override void OnEnemyReady()
    {
        Points = 200;        // 小物（浄化コンボ・カウントには通常どおり乗る）
        BodyRadius = 5f;
        PanelCount = 1;      // 一枚剥がせば浄化＝狙って一撃の的
        PanelInk = 1;
        OrbitRadius = 8f;
        SpinSpeed = 2.4f;
        PanelsFire = false;
        CollisionLayer = 0;  // 本体接触ダメージなし（"aoe" にも入れない＝壁とは別物）
    }

    // 通路と一緒に左へ流れ、常に中央線上（＝通路の安全地帯）に居る。撃たない限り素通りしていく。
    protected override void UpdateMovement(double delta)
    {
        bool live = Corridor != null && IsInstanceValid(Corridor);
        float x = GlobalPosition.X - (live ? Corridor!.ScrollSpeed : 60f) * (float)delta;
        GlobalPosition = new Vector2(x, live ? Corridor!.GuideYAt(x) : GlobalPosition.Y);
        QueueRedraw(); // 明滅リング用
    }

    // フォロワー化しない。撃破報酬＝ボス総HPの4%直撃。ボスは画面右外に退場中なので、
    // 「効いた」のフィードバックはこの残滓の位置に出す（HPバーの減りが本命の手応え）。
    protected override void GrantFollower()
    {
        if (BossRef == null || !IsInstanceValid(BossRef) || BossRef.IsPurified) return;
        BossRef.DealDirectDamage(Mathf.RoundToInt(Enemy.BarHp * BossRef.TotalBars * 0.04f));
        FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -10), "-4%", FxLayer.Gold, 12);
    }

    // 立ち絵なし＝専用プレースホルダ（基底の人型は使わない）。黒い吹き出し＋「…」＋報酬色のリング。
    public override void _Draw()
    {
        var ink = IsPurified ? new Color(0.55f, 0.72f, 0.92f, 0.8f) : new Color(0.12f, 0.10f, 0.16f, 0.92f);
        DrawCircle(Vector2.Zero, 5f, ink);
        var dot = IsPurified ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.75f, 0.80f, 0.95f, 0.9f);
        for (int i = -1; i <= 1; i++) DrawCircle(new Vector2(i * 2.2f, 0f), 0.7f, dot);
        if (!IsPurified)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() * 0.006f);
            DrawArc(Vector2.Zero, 7.5f, 0, Mathf.Tau, 24,
                    new Color(0.42f, 0.61f, 0.85f, 0.35f + 0.35f * pulse), 1.2f);
        }
    }
}
