using Godot;

// Spawner : ステージ本編の連続スポナー。
// 右端から敵を一定間隔で湧かせ、経過時間とともに密度を上げる（押し引きの圧）。
// 浄化ゲージが目標到達(StageCleared)で自動停止。会話中(BubblePaused)は湧かせない。
// 過密防止に同時出現数の上限を設ける。
public partial class Spawner : Node
{
    public Node2D World = null!;
    // ステージの「心象世界」テーマ。
    // 既定は Default（既存アンチくん/うつむきさん）＝StageW0 等は従来どおり。
    public StageTheme Theme = StageTheme.Default;
    public bool Active { get; private set; }
    public int SpawnLimit;
    public int SpawnedCount { get; private set; }
    private int _characterIndex;

    // 道中の“波ごとの圧”を変える起点。0=ふつうに緩く立ち上がる、1=最初から最大密度。
    // 道中を三部構成にして「後半ほど詰めてくる」緩急を作るため、後続の波で上げて渡す（§3 緩急）。
    public float StartIntensity = 0f;
    // 浄化目標(StageCleared)に達しても止めない（ルナティック専用・2026-09-26）。レイ面の引用の嵐は「規定数で止める」
    //   ゲートを持たず嵐が終わるまで湧き続けるので、湧き間隔の詰まったルナティックでは嵐の途中で目標に達して
    //   自動停止し、以降の嵐〜道中C が敵ゼロの空白になる（QA 実測 17.6 秒）。既定 false＝従来難易度は従来どおり。
    public bool IgnoreStageCleared;

    private const float SpawnX = Field.Right + 14f;   // 盤面の右外
    private const float RampDur = 28f;    // この秒数で最大密度に（道中を“密度の変化”で見せる：60→28で立ち上がりを早く）
    private const float IntervalStart = 2.0f;
    private const float IntervalEnd = 0.8f;
    private const int MaxAliveFallback = 8; // 同時出現の上限（GameManager が取れないときの既定＝Normal相当）

    // ── 第4種：回り込みザコ「引用リプ」（FlankAim）の調整値（C-1）──
    // 右から出現→上下端を走行→盤面のやや左(x≈FlankCampX)に着座→右向き低速単発。
    // 進入経路が丸見え＋弾は低速なので理不尽ではない（走行中は撃たない＝MidEnemy の進入仕様）。
    private const float FlankRate = 0.15f;       // テーマ湧きのうちこの割合で出現
    private const float FlankRampGate = 0.5f;    // ランプ後半（進行度>=50%）のみ出現＝序盤は出さない
    // 着座X＝盤面の中央よりすこし左（Field.Left + 幅の40%）。2026-09-08 に Field.Left+16 から変更。
    //   Field.Left+16(=136) は自機の可動域の左端(Field.Left=120)から 16px しか離れておらず、
    //   自機の弾は右へしか飛ばないので **この敵より左へ回り込む余地がほぼ無かった**
    //   ＝ユーザー実機指摘「一番左側を上下に移動して攻撃してくる敵は仕様的に倒せない」。
    //   0.40 なら着座Xは 225.6 で、自機は左へ 105px ぶん回り込んで正面から撃ち返せる。
    //   上下の走行・着座Yと攻撃間隔はそのまま＝「背後から圧をかける」役割は変えていない。
    private const float FlankCampXK = 0.40f;
    private const float FlankCampX = Field.Left + Field.Width * FlankCampXK;
    private const float FlankRunTopY = 16f;      // 上端走行レーンY
    private const float FlankRunBottomY = 200f;  // 下端走行レーンY
    private const float FlankCampTopY = 64f;     // 上から回った個体の着座Y
    private const float FlankCampBottomY = 152f; // 下から回った個体の着座Y

    // ── 盾もち種「バズ壁」（BuzzWall）の調整値（全テーマ・波B/C限定）──
    // 撃たない・遅い・硬い（パネル5枚×インク3）＝剥がし切るDPSチェックで優先順位判断を生む
    //（拡散/ホーミング/貫通の使い所）。序盤A波には出さない＝覚えることを増やしすぎない。
    private const float BuzzWallRate = 0.10f;         // テーマ湧きのうちこの割合で出現
    private const float BuzzWallMinIntensity = 0.3f;  // StartIntensity がこれ以上＝波B(0.35)/C(0.7)のみ
    // ── 祈り運び種（KoharuPrayerCarry・こはる面専用）の調整値 ──
    // 消せる祈り弾を3発ぶら下げて横断するボーナス種＝ボス戦「お残し禁止」の練習台。
    private const float PrayerCarrierRate = 0.10f;    // こはるテーマ湧きのうちこの割合で出現

    private double _t;   // Begin からの経過（StartIntensity ぶん前倒しした実効時間）
    private double _cd;  // 次の出現までの残り
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    public override void _Ready() => _rng.Randomize();

    public void Begin()
    {
        Active = true;
        SpawnedCount = 0;
        _characterIndex = 0;
        // 後半の波は StartIntensity ぶんランプを前倒し＝最初からやや詰まった圧で始める。
        float si = Mathf.Clamp(StartIntensity, 0f, 1f);
        _t = si * RampDur;
        _cd = Mathf.Lerp(0.8f, 0.4f, si);
    }

    public void Stop() { Active = false; DropPendingLeft(); }

    public override void _Process(double delta)
    {
        if (Hud.BubblePaused) return; // 会話中は襲ってこない＝湧かせない（予告済みの左湧きもここで足踏みする）

        // 予告済みの左湧きは Active が落ちた後（SpawnLimit 到達＝波の打ち止め）でも実体化させる。
        // 予告を出したのに何も来ない、を作らない。Stop()/StageCleared の経路だけは DropPendingLeft で破棄する。
        TickPendingLeft(delta);
        if (!Active) return;

        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game != null && game.StageCleared && !IgnoreStageCleared) { Stop(); return; }

        _t += delta;
        _cd -= delta;
        if (_cd > 0) return;

        // 過密なら少し待つ（上限は難易度別＝Easy6 / Normal8 / Hard10 / Lunatic12）
        int maxAlive = game?.MaxAliveEnemies ?? MaxAliveFallback;
        if (GetTree().GetNodesInGroup("enemies").Count >= maxAlive) { _cd = 0.3; return; }

        SpawnOne(game);

        float ramp = Mathf.Clamp((float)_t / RampDur, 0f, 1f);
        // 難易度別の間隔倍率（Easy1.3 / Normal1.0 / Hard0.78 / Lunatic0.62）。基準の 2.0→0.8 と
        // 最大密度到達 28 秒は据え置きで、難しいほど同じランプを詰めて湧かす（2026-09-06）。
        float interval = Mathf.Lerp(IntervalStart, IntervalEnd, ramp) * (game?.SpawnIntervalMul ?? 1f);
        // 前のめり密度：自機が右へ寄る（攻める）ほど倍率が高い＝間隔を縮めて多く湧かす（除算）。
        // 同時上限は上の maxAlive で難易度別に頭打ち（圧殺防止）。GameManager.PlayerNormX は各 Root の TickProgress が毎フレーム更新。
        float spawnMul = game != null ? GameManager.SpawnRateMul(game.PlayerNormX) : 1f;
        _cd = interval * _rng.RandfRange(0.8f, 1.2f) / spawnMul;
    }

    private void SpawnOne(GameManager? game)
    {
        float y = _rng.RandfRange(46f, 172f);
        bool drifter = _rng.Randf() < 0.25f; // 25%でうつむきさん(PageShard)、75%でアンチくん(GlyphMote)。どちらも弾は撃たず、パネルの盾で押し返す点は共通。

        Enemy e;
        Vector2 pos;
        if (Theme == StageTheme.Default)
        {
            // 既存挙動はそのまま（チュートリアル等の見た目・挙動を一切変えない）＝右から左進。
            e = drifter ? new PageShard() : new GlyphMote();
            pos = new Vector2(SpawnX, y);
        }
        else
        {
            var me = new MidEnemy();
            var characters = EnemyTable.CharactersFor(Theme);
            bool introducing = _characterIndex < characters.Count;
            float ramp = Mathf.Clamp((float)_t / RampDur, 0f, 1f);
            // 第4種：回り込み「引用リプ」。ランプ後半のみ FlankRate で湧く（全テーマ共通・スキンは撃つ種を流用）。
            // 盤面のやや左に陣取って“読める形”で圧をかける＝左端の安置化を構造的に崩す。
            // 自機は着座Xより左へ回り込めるので、撃ち返して倒せる（2026-09-08 の FlankCampXK 変更）。
            if (!introducing && ramp >= FlankRampGate && _rng.Randf() < FlankRate)
            {
                me.Configure(EnemyTable.Flanker(Theme));
                bool top = _rng.Randf() < 0.5f;
                float runY = top ? FlankRunTopY : FlankRunBottomY;
                pos = new Vector2(SpawnX, runY);
                // 経由点＝走行レーン終端（端を走り切る）→ 着座点＝盤面のやや左。2区間の直進で経路が読める。
                me.SetFlankEntry(new Vector2(FlankCampX, runY),
                    new Vector2(FlankCampX, top ? FlankCampTopY : FlankCampBottomY));
            }
            // 盾もち「バズ壁」：波B/C（StartIntensity>=0.3）のみ。右から出て場の中ほどに陣取る壁。
            else if (!introducing && StartIntensity >= BuzzWallMinIntensity && _rng.Randf() < BuzzWallRate)
            {
                me.Configure(EnemyTable.BuzzWall(Theme));
                pos = new Vector2(SpawnX, y);
                me.SetEntry(new Vector2(_rng.RandfRange(160f, 260f), y)); // 中列に居座って射線を塞ぐ
            }
            // 祈り運び：こはる面の道中のみ。ぶら下げた祈り弾ごと左へ横断（居座らない＝SetEntry不要）。
            else if (!introducing && Theme == StageTheme.Koharu && _rng.Randf() < PrayerCarrierRate)
            {
                me.Configure(EnemyTable.PrayerCarrier());
                // ぶら下げ弾（最大+37px＋振れ）が画面下端216pxを割らないYで横断させる。
                pos = new Vector2(SpawnX, _rng.RandfRange(60f, 150f));
            }
            else
            {
                var (shooter, drift) = EnemyTable.For(Theme);
                bool character = introducing || (characters.Count > 0 && _rng.Randf() < 0.6f);
                me.Configure(character ? characters[_characterIndex++ % characters.Count] : drifter ? drift : shooter);
                // 出現エッジを散らす（難易度別。PickEdge / 各 Spawn*Edge を参照）。
                Vector2 camp;
                var edge = PickEdge(game);
                switch (edge)
                {
                    case Edge.Top:    (pos, camp) = SpawnTopEdge(WideVertical(game));    break;
                    case Edge.Bottom: (pos, camp) = SpawnBottomEdge(WideVertical(game)); break;
                    case Edge.Left:   (pos, camp) = SpawnLeftEdge();   break;
                    default:          (pos, camp) = SpawnRightEdge(y); break;
                }
                if (edge == Edge.Left)
                {
                    // 左＝自機の背後。ここだけは「予告→間→出現」の順にする（§4 予備動作／§7 理不尽の排除）。
                    // 出現点でリングが弾けてから LeftWarnLead 秒おいて実体が現れる＝背後を取られる前に気づける。
                    // 実体化は TickPendingLeft が行う（この個体はまだ AddChild しない）。
                    me.SetSilentEntry(camp);
                    QueueLeftSpawn(me, pos);
                    // 予約した時点で「1体湧かせた」と数える＝出現間隔・SpawnLimit の勘定は他エッジと同じ。
                    SpawnedCount++;
                    if (SpawnLimit > 0 && SpawnedCount >= SpawnLimit) Active = false;
                    return;
                }
                me.SetEntry(camp);
            }
            e = me;
        }
        World.AddChild(e);
        e.GlobalPosition = pos;
        SpawnedCount++;
        if (SpawnLimit > 0 && SpawnedCount >= SpawnLimit) Active = false;
    }

    // ─── 出現エッジ（2026-09-17：難易度で「どの方角から来るか」を変える）───
    //   旧仕様は全難易度で 右60% / 右上20% / 右下20% の3種。ただし右上・右下も x は Field.Right 寄りに
    //   限定されていたので、体感はどれも「右から来る」＝方角の圧に差が無かった。
    //   ここを難易度の第4の軸にする（既存3軸＝弾の数 BulletCountMul / 弾速 BulletSpeedMul / 湧き間隔 SpawnIntervalMul は
    //   どれも「量」の調整で、"どこを見ていればいいか" は変えなかった）。方角が増えると視野を広く持つ必要が生まれ、
    //   同じ弾量でも読みの負荷が上がる＝「量」でなく「読み」で難しくする（§5 避けられる難しさ）。
    private enum Edge { Right, Top, Bottom, Left }

    // 難易度別の出現エッジ配分。
    //   Easy    : 右60 / 上20 / 下20（＝**分布・座標レンジは従来と同一**。入口の体験は変えない。
    //             ※乱数の消費回数だけは違う（旧実装は配分決定に Randf() を最大2回、新 PickEdge は1回）。
    //               同一シードでもストリームがずれるので、リプレイ的な再現一致は保証しない）
    //   Normal  : 右65 / 上17.5 / 下17.5（ユーザー指示「後ろを除く3方向」。ただし上下は“本当に上端・下端”から
    //             入るよう SpawnTopEdge/SpawnBottomEdge を全幅化した。右が主体のまま上下が混ざる程度に留める＝
    //             既存プレイヤーの標準体験を壊さない。上下計35%は旧40%より僅かに低く、増えたのは「進入位置の幅」だけ）
    //   Hard    : 左7  → 右60.5 / 上16.3 / 下16.3（残り93%を 0.65:0.175:0.175 で配る）
    //   Lunatic : 左12 → 右57.2 / 上15.4 / 下15.4（左を厚くして最終ティアの賭け金差を作る）
    //   ★左（＝自機の背後）は比率を1割前後に抑える。左は自機の弾が届かない方角なので、
    //     多いほど「撃てないまま溜まる」渋滞になる。あくまで「たまに背後を取られる」緊張の香り付け。
    //   ★左エッジは引用リプ（FlankAim・着座x=225.6）と役割が被りうるが、左エッジの着座xは
    //     LeftCampMinX(=184)以上かつ FlankCampX より手前寄りに散らすので、左側が二重に埋まる密度にはならない。
    //     さらに FlankRate=0.15 は「テーマ湧き全体」から先に間引かれるので、左エッジ枠はその残りから取る＝累積しない。
    private Edge PickEdge(GameManager? game)
    {
        float leftRate = game?.Difficulty switch
        {
            GameManager.Diff.Hard => 0.07f,
            GameManager.Diff.Lunatic => 0.12f,
            _ => 0f,                      // Easy / Normal は左から出さない
        };
        // 残り（左以外）を 右 : 上 : 下 に配る比率。
        //   Easy    : 0.60（＝従来と同じ 60/20/20。上下の進入xも旧仕様のまま＝完全な現状維持）
        //   Normal〜: 0.65（上下は計35%。旧40%より“回数”は僅かに減らす一方、進入xを全幅へ広げて
        //             “どこから降ってくるか分からない”ぶんの圧が増える。回数×幅で体感が跳ね上がらないよう
        //             回数側を一段引いた＝標準体験の据わりを守るための引き算（§6・§15）。
        float rightShare = (game?.Difficulty ?? GameManager.Diff.Normal) == GameManager.Diff.Easy ? 0.60f : 0.65f;
        float r = _rng.Randf();
        if (r < leftRate) return Edge.Left;
        float rest = 1f - leftRate;
        float t = (r - leftRate) / Mathf.Max(0.0001f, rest);
        if (t < rightShare) return Edge.Right;
        // 上下は等分（残りの半々）。
        return t < rightShare + (1f - rightShare) * 0.5f ? Edge.Top : Edge.Bottom;
    }

    // 右から（従来どおり）。同Yへ水平に入って中列に着座。
    private (Vector2 pos, Vector2 camp) SpawnRightEdge(float y) =>
        (new Vector2(SpawnX, y),
         new Vector2(_rng.RandfRange(Field.Left + 60f, Field.Left + 180f), y));

    // 上下エッジの進入位置を「盤面の全幅」に広げるか。
    //   Easy だけ false＝旧仕様（x は Field.Right 寄りに限定＝実質“右上/右下から”）のまま。
    //   入口の難易度はユーザー指示で現状維持（体験を一切変えない）。
    private static bool WideVertical(GameManager? game) => (game?.Difficulty ?? GameManager.Diff.Normal) != GameManager.Diff.Easy;

    // 上端から下りてくる。★2026-09-17：wide=true では x の限定(230〜360)を盤面のほぼ全幅へ広げた＝
    //   「右上から」ではなく「真上から」も降ってくる。ただし自機が張り付きがちな最左(Field.Left)より
    //   少し右から入れて、頭上に湧いて避ける間もなく接触する事故を避ける。
    private const float VertSpawnMinX = Field.Left + 28f;   // = 148
    private const float VertSpawnMaxX = Field.Right - 16f;  // = 368
    private (Vector2 pos, Vector2 camp) SpawnTopEdge(bool wide)
    {
        if (!wide) // 旧仕様（Easy）: 右上から下りて、着座も従来レンジ
            return (new Vector2(_rng.RandfRange(Field.Right - 154f, Field.Right - 24f), Field.Top - 12f),
                    new Vector2(_rng.RandfRange(Field.Left + 100f, Field.Left + 200f), _rng.RandfRange(55f, 110f)));
        float sx = _rng.RandfRange(VertSpawnMinX, VertSpawnMaxX);
        // 着座は上半分。降りてきた x の近くに着座させる（真横に大きく流れない＝進入線が読める）。
        float cx = Mathf.Clamp(sx + _rng.RandfRange(-36f, 16f), Field.Left + 60f, Field.Right - 40f);
        return (new Vector2(sx, Field.Top - 12f),
                new Vector2(cx, _rng.RandfRange(48f, 104f)));
    }

    // 下端から上ってくる（上と対称）。着座は下半分。
    private (Vector2 pos, Vector2 camp) SpawnBottomEdge(bool wide)
    {
        if (!wide) // 旧仕様（Easy）: 右下から上ってくる
            return (new Vector2(_rng.RandfRange(Field.Right - 60f, Field.Right - 10f), Field.Bottom + 12f),
                    new Vector2(_rng.RandfRange(Field.Left + 60f, Field.Left + 160f), _rng.RandfRange(110f, 165f)));
        float sx = _rng.RandfRange(VertSpawnMinX, VertSpawnMaxX);
        float cx = Mathf.Clamp(sx + _rng.RandfRange(-36f, 16f), Field.Left + 60f, Field.Right - 40f);
        return (new Vector2(sx, Field.Bottom + 12f),
                new Vector2(cx, _rng.RandfRange(112f, 168f)));
    }

    // ─── 左＝自機の背後から（Hard 以上のみ）───
    //   理不尽回避の3点セット：
    //   (a) 出現Xを LeftSpawnX(=104) にする。Enemy の左外カリング(Field.Left-24=96)より右なので
    //       湧いた瞬間に消えず、かつ MidEnemy.OnScreen (X>Field.Left=120) には入らない＝
    //       進入の出だしは発射ゲートの外。
    //       ★x=104 は HUD サイドパネル（Hud.DrawSidePanel：設計 0..373 ＝内部 0..111.9 が不透明、
    //         影帯を入れて 114.3 まで）の裏side なので、**敵も予告もここでは一切見えない**。
    //         だから予告は出現点ではなく「敵が最初に見える列」に出す（LeftWarnX を参照）。
    //   (b) SetSilentEntry で「着座するまで一切撃たない」（MidEnemy._silentEntry）。
    //       X>120 を跨いでも撃たない＝背後から歩きながら撃たれる二重苦を構造的に潰す。
    //   (c) 着座Xを LeftCampMinX(=184) 以上＝Field.Left から 64px 以上右に置く。自機は Field.Left まで
    //       下がれるので、必ず「この敵より左へ回り込んで正面から撃ち返す」余地が残る
    //       （2026-09-08 の FlankCampXK 是正と同じ判断基準。あのとき問題だったのは着座x=136＝16px しか
    //         余地が無かったこと。64px あれば回り込める）。
    //   (d) 出現Yは自機が居がちな帯を避けず**盤面の縦ほぼ全域**に散らすが、湧いた位置では撃たないので
    //       密着スポーンが即死にならない（接触ダメージだけは残る＝「背後に気配を感じたら離れる」を促す）。
    private const float LeftSpawnX = Field.Left - 16f;    // = 104（カリング 96 の右／画面内判定 120 の左）
    private const float LeftCampMinX = Field.Left + 64f;  // = 184
    private const float LeftCampMaxX = Field.Left + 104f; // = 224（引用リプの着座 225.6 の手前に収める）
    private (Vector2 pos, Vector2 camp) SpawnLeftEdge()
    {
        float sy = _rng.RandfRange(40f, 176f);
        return (new Vector2(LeftSpawnX, sy),
                new Vector2(_rng.RandfRange(LeftCampMinX, LeftCampMaxX),
                            Mathf.Clamp(sy + _rng.RandfRange(-24f, 24f), 40f, 176f)));
    }

    // ─── 左湧きの「予告→間→出現」───
    //   ★2026-09-17 修正（QA 指摘）：予告を出現点(x=104)に出していたが、そこは HUD サイドパネルの
    //     裏側で、リング(R1=12)の右端 x=116 のうち見えるのは影帯の外 114.3..116 の 1.7px だけだった
    //     ＝事実上「予告が無い」状態。Hud は CanvasLayer なので ZIndex では前に出せない。
    //
    //   採った方針：**予告は出現点ではなく「敵が最初に見える列」に出す**。
    //     プレイヤーが読むべき情報は敵の正確な出現Xではなく「どの高さから入ってくるか」なので、
    //     盤面の左端（敵が必ず横切る境界）にYを示すマーカーを置けば役目は足りる。
    //     LeftSpawnX / カリング境界 / OnScreen ゲートの関係（QA が実測合格させた幾何）は一切動かさない。
    private const float LeftWarnX = Field.Left + 6f;  // = 126。パネル影(114.3)より右＝確実に見える列
    //   リードは 0.4s：AimFlash の Ttl(0.4s) と一致させる＝「リングが消える瞬間に敵が現れる」で
    //   因果が途切れない（旧 0.55s はリング消滅後 0.15s の“無”を挟んでいた）。
    //   実効の猶予はこれより長い：敵は x=104 から進入するので、盤面左端(120)へ届くまで更に時間が要る。
    //   進入速度は MidEnemy の種別倍率（ApproachSpeedMin/Max）＋上限クランプ（SpeedCeil=58px/s）で
    //   実効 28.5〜58px/s（2026-09-22 の「敵は自機より速くしない」リワーク後）＝可変。
    //   旧値（55.8〜166.5px/s）より**必ず遅い**ので、予告点灯から敵が盤面左端へ届くまでの時間は
    //   旧実測 0.533〜0.565s より長くなるだけ＝そのあいだに自機（NormalSpeed=75px/s）が動ける距離は
    //   旧 32.7〜38.1px を必ず上回る。接触半径は自機2＋敵8＝10px なので、真横に逃げれば確実に抜けられる
    //   （猶予は一方向にしか増えないので、この予告リードは据え置きで安全側）。
    private const double LeftWarnLead = 0.4;
    private readonly System.Collections.Generic.List<(Enemy enemy, Vector2 pos, double t)> _pendingLeft = new();

    private void QueueLeftSpawn(Enemy e, Vector2 pos)
    {
        _pendingLeft.Add((e, pos, LeftWarnLead));
        // 予告そのもの。既存の予告表現（AimFlash＝白フラッシュ＋広がるリング）を流用する＝
        // 新しい色語彙を作らず「これから何か来る」の意味が既に学習済みの記号で伝わる（§4／§13 一貫性）。
        // 位置は出現点の Y を保ったまま X だけ盤面内へ寄せる＝「この高さの左端から来る」を示す。
        FxLayer.Instance?.AimFlash(new Vector2(LeftWarnX, pos.Y), LeftWarnColor);
    }

    // 予告リングの色。ザコの既定穢れ色（Bullet.EnemyMid 系）と同系＝「敵が来る」の意味で一貫。
    private static readonly Color LeftWarnColor = new Color(0.882f, 0.447f, 0.675f);

    private void TickPendingLeft(double delta)
    {
        for (int i = _pendingLeft.Count - 1; i >= 0; i--)
        {
            var (enemy, pos, t) = _pendingLeft[i];
            t -= delta;
            if (t > 0) { _pendingLeft[i] = (enemy, pos, t); continue; }
            _pendingLeft.RemoveAt(i);
            if (!IsInstanceValid(enemy)) continue;
            // ステージ遷移で World が消えている可能性がある（予約は最大 LeftWarnLead 秒だけ未来に生きる）。
            if (!IsInstanceValid(World)) { enemy.QueueFree(); continue; }
            World.AddChild(enemy);
            enemy.GlobalPosition = pos;
        }
    }

    // ステージ側が Stop した時点で未実体化の予約が残っていると、会話や次の波に無言で1体だけ
    // 湧いてしまう。予約は破棄する（＝予告だけ出て何も来ないが、これは無害な側）。
    public override void _ExitTree() => DropPendingLeft();

    private void DropPendingLeft()
    {
        foreach (var (enemy, _, _) in _pendingLeft)
            if (IsInstanceValid(enemy)) enemy.QueueFree();
        _pendingLeft.Clear();
    }
}
