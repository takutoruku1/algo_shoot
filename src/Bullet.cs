using Godot;

// 弾形。前6種は敵弾用（RefrainHTML/Refrain Danmaku v3 の弾形）。言葉弾は Bullet.Word で別扱い。
// Dart/Petal/Seeker は自機弾のモード別シルエット（連射＝ダート／拡散＝花弁／誘導＝シーカー）。
// 敵弾の描画 switch はこの3種を default（Orb 描画）へ落とすので、万一敵側へ渡っても安全（渡す箇所は無い）。
public enum BulletShape { Orb, Diamond, Star, Ring, Needle, Rice, Dart, Petal, Seeker }

// Bullet : Area2D。Pool により生成・使い回しされる弾。
// 当たり判定（被弾処理）は敵側/自機側で行うため、Bullet 自身は Area 重なり処理を持たない。
// _PhysicsProcess で等速直線移動し、画面外(余白16px)に出たら Pool.Despawn(this)。
public partial class Bullet : Area2D
{
    // 衝突レイヤー/マスク（ビット値, 仕様準拠）
    // PlayerBullet: layer=2, mask=4
    // EnemyBullet:  layer=8, mask=1
    private const uint LayerPlayerBullet = 2;
    private const uint MaskPlayerBullet = 4;
    private const uint LayerEnemyBullet = 8;
    private const uint MaskEnemyBullet = 1;

    // 画面サイズと画面外判定の余白
    private const float ScreenWidth = 384f;
    private const float ScreenHeight = 216f;
    private const float Margin = 16f;

    public Vector2 Velocity;
    public bool IsEnemy;
    public int Damage;
    public bool Active;
    public bool Grazed;  // グレイズ済みか（重複加点防止）
    // 残貫通数（自機の連射弾のみ・貫く光 shot_pierce）。>0 の弾はヒットで消えず、消費側が1減らして素通しする。
    public int Pierce;
    // 残跳弾数（自機の拡散弾のみ・連鎖の光 chain_light）。>0 の弾が消費された瞬間、最寄りの別の敵へ跳弾する。
    public int Chain;
    public string Word = "";  // 非空なら「言葉弾」＝文字そのものが弾（道中の敵。設計書 4）

    // 弾形とスペル色（敵弾のみ反映）。色未指定時は既定の穢れ色。
    public BulletShape Shape = BulletShape.Orb;
    public Color Tint;
    public bool TintSet;

    // ─── ボス別弾幕ギミック（#12 機構側）の弾フラグ ───
    // Erasable: 自機弾で消せる「祈り弾」（こはる FanDown）。消すと双方消滅＋やさしさ微加算。
    // SoftenOnGraze: グレイズすると一度だけ減速×GrazeSoftenMul＋淡色化する「キミ弾」（あかり）。被弾判定は不変。
    public bool Erasable;
    public bool SoftenOnGraze;
    public bool Softened;   // 減速・淡色化が適用済みか（1発につき1回だけ）
    // M2バランス：×0.75 は自機狙い弾をほぼ無力化していた（かすった時点で回避が確定する）ため ×0.85 に緩和。
    // “読める”手応えは残しつつ、グレイズ＝安全化ではなくす。
    public const float GrazeSoftenMul = 0.85f;

    // 加速球（自機ショットの“タメ→ロケット発進”モード）。発射直後は自機のすぐ近くでほぼ静止して「タメ」を作り、
    //   _accelDelay 秒経過した瞬間にロケットのように急加速して発進する（徐々にではなく瞬間的・方向は不変）。
    //   経過時間は _age で計る（会話停止中は進めない＝弾停止と整合）。付与は MakeAccel（Spawn 直後）。
    //   ★発進方向は _accelDir に保持する：タメ中の速度はほぼ 0 なので、Velocity から向きを復元すると
    //     正規化が破綻する（len≈0）。MakeAccel で確定した向きを別に持ち、発進時に _accelDir×_fastSpeed で撃つ。
    public bool Accel;
    private float _accelDelay;   // タメ時間＝加速までの遅延秒（既定0.8秒）
    private float _fastSpeed;    // 加速後（発進）の速さ（px/s）
    private Vector2 _accelDir;   // 発進方向（単位ベクトル・MakeAccel で確定）。タメ中の微速もこの向き。
    private bool _accelDone;     // 既に発進へ切り替えたか（毎フレーム上書きしない＝1回だけ切替）
    private float _age;          // このアクティブ化からの経過秒（加速判定用。会話停止中は進めない）

    // ホーミング（自機ショットの誘導モード・設計書 §3-2③）。進行方向側の穢れ標的へ最大旋回角つきで曲射。
    public bool Homing;
    // バックファイア（後方弾）フラグ：標的探索は基本 Velocity の向きで決めるため、これは
    // 「弾速がほぼ 0 で向きが読めないとき」のフォールバック既定（true=左を探す）としてのみ働く。
    public bool BackwardHoming;
    // 旋回角の上書き（0=既定 HomingTurnRate を使う）。誘導速射・後方追尾で個別に旋回を上げる。
    public int TurnRateOverride;
    private Node2D? _homeTarget;
    private const float HomingTurnRate = 150f; // deg/s（“曲がって当たる”手応え側へ。漂う弾は HomingLife で始末する）
    private float _retargetT;                  // 標的の再探索クールダウン（RetargetInterval ごとに乗り換え判定）
    private const float RetargetInterval = 0.25f; // 全探索は 0.25s に1回だけ＝毎フレーム探索より軽い
    private const float RetargetGain = 0.6f;      // 現標的より距離が 0.6 倍以下に近い敵がいたら乗り換える
    // 自機ホーミング弾の寿命（秒）。旋回強化で敵周囲に自機弾の雲ができ、敵弾（言葉弾）が読めなくなるのを防ぐ。
    // 2.5s＝画面幅1.3回分＝当てる弾は必ず決着する長さ。画面外 Despawn と同じ経路で消す。
    private const float HomingLife = 2.5f;

    // 言葉弾の文字フォント（全弾で共有。初回だけロード）。
    private static FontFile? _wordFont;
    private static FontFile? WordFont
    {
        get
        {
            _wordFont ??= UiKit.Zen; // 言葉弾も滑らかゴシック（非ピクセル）
            return _wordFont;
        }
    }

    // 言葉弾にする（Spawn 後に呼ぶ）。再描画して文字を反映。
    // handle を渡すと「下に流れるコメント（ティッカー）」と同じ投稿者ハンドルを弾にも乗せ、
    // “下に流れていたコメントが弾として降ってくる”連動感を出す（Hud.TickerWords が共有ソース）。
    public void SetWord(string w, string handle = "")
    {
        Word = w;
        _wordHandle = handle;
        // 晒し投稿らしい“いいね数”を語ごとに固定で付与（X感／毎フレーム再計算しない）。
        int h = 0; foreach (char c in w) h = h * 31 + c;
        int likes = 800 + (Mathf.Abs(h) % 47000);
        _wordLikes = likes >= 10000 ? $"{likes / 10000f:0.0}万" : likes.ToString("#,0");
        QueueRedraw();
    }
    private string _wordLikes = "";
    private string _wordHandle = "";

    public float Radius { get; private set; } = 3f;

    // RefrainScripts_tama / Refrain HUD A.dc.html の弾デザイン。
    // ドット絵ではなく「白ハイライト→中間色→暗エッジのグラデ＋外周グロー」の滑らかな弾。
    // 敵弾: radial-gradient(circle at 35% 30%, #fff, #e072ac 60%, #7a2f5a) + glow rgba(224,114,172,.75)
    private static readonly Color EnemyMid  = new Color(0.882f, 0.447f, 0.675f); // #e072ac ボス穢れ
    // 自機弾: radial-gradient(circle at 40% 35%, #fff, #6cbcd8 65%) + glow rgba(108,188,216,.8)
    private static readonly Color PlayerMid  = new Color(0.424f, 0.737f, 0.847f); // #6cbcd8 浄化
    private static readonly Color PlayerEdge = new Color(0.247f, 0.490f, 0.604f); // 暗めの水色縁
    private static readonly Color PlayerGlow = new Color(0.424f, 0.737f, 0.847f); // rgba(108,188,216)
    // 加速球（自機弾の加速モード）：通常自機弾の水色と区別する琥珀色。
    private static readonly Color AccelMid  = new Color(0.96f, 0.78f, 0.36f); // 琥珀
    private static readonly Color AccelEdge = new Color(0.62f, 0.44f, 0.16f); // 暗めの琥珀縁
    private static readonly Color AccelGlow = new Color(1.0f, 0.82f, 0.40f);
    // ── 自機弾のモード別カラー（色＋形の二重符号化）──
    // 色相環で離れた4色：連射＝水色(≈197°・現行主力の顔)／拡散＝翠(≈150°)／誘導＝青藤(≈233°)／加速球＝琥珀(≈42°)。
    // いずれも冷色〜中性の「浄化の光」域で、敵弾の警告色（穢れ桃 #e072ac・深紅・橙）と混ざらない。
    // 拡散＝翠（エメラルド）：敵弾Tint（レイ銀/菫/金/ティール・あかり雨青/藍/白・こはる琥珀/深紅/橙・ミナ濁紫/濁桃/濁金・道中桃紫系）
    //   の全リストに緑は皆無＝全ステージで唯一色。花弁＝若葉の世界観にも合う。明度は水色PlayerMidと同格に揃える。
    private static readonly Color SpreadMid  = new Color(0.40f, 0.85f, 0.63f); // #66d9a1
    private static readonly Color SpreadGlow = new Color(0.42f, 0.88f, 0.66f);
    // 誘導＝青藤（ペリウィンクル）：レイの菫 #9a72d9(≈263°) より約30°青へ・あかりの藍 #4a6aa0(≈217°・暗鈍色) より
    //   明るく高彩度＝色相と明度の両方で敵Tintから離す。ミナの濁紫（低彩度）とも彩度差で分離。
    private static readonly Color HomingMid  = new Color(0.59f, 0.63f, 0.94f); // #96a0f0
    private static readonly Color HomingEdge = new Color(0.29f, 0.31f, 0.56f); // 暗めの青藤縁
    private static readonly Color HomingGlow = new Color(0.63f, 0.67f, 0.96f);
    // 誘導シーカーのフィン：HomingEdge→Mid の中間（頭のガラス玉より一段沈めて、頭の読みを邪魔しない）。
    private static readonly Color PlayerFin = new Color(0.44f, 0.47f, 0.75f);
    private static readonly Color KegareWord = new Color(0.96f, 0.56f, 0.78f);    // 言葉弾の文字（穢れ系）
    // 後方弾（FireBackfire）＝淡い金（≈45°）。敵弾の穢れ桃 #e072ac(≈337°) とも、他3モードの浄化色域とも
    //   離れた唯一の暖色＝「前方の連射/拡散/誘導とは別枠の弾」を色だけで即断できる。
    private static readonly Color BackMid  = new Color(0.98f, 0.86f, 0.55f);
    private static readonly Color BackGlow = new Color(1.0f, 0.90f, 0.62f);

    // ───── ポリゴン弾のGC対策：頂点バッファを static 使い回し（毎フレーム new を廃止）─────
    // 弾は飛行中に回転しない＝頂点角度は定数。単位方向テンプレを一度だけ計算し、
    // 描画時は半径 r を掛けて共有バッファへ書き込むだけ（new 割当ゼロ／再テッセレーション無し）。
    private static readonly Vector2[] _starTemplate = BuildStarTemplate(); // 10頂点の単位方向×半径比
    private static readonly Vector2[] _starBuf = new Vector2[10];          // DrawStar 用の共有出力
    private static readonly Vector2[] _diaBuf = new Vector2[4];            // DrawDiamond 本体（±s）
    private static readonly Vector2[] _diaCoreBuf = new Vector2[4];        // DrawDiamond 芯の光（×0.5）
    // 自機弾のモード別シルエットも同じ作法（static 使い回し・per-frame の new 割当ゼロ）。
    // DrawColoredPolygon は呼び出し時に頂点列をコピーするので、同フレーム内で書き換えて使い回して安全。
    private static readonly Vector2[] _dartBuf = new Vector2[4];      // 連射ダート本体（進行方向へ尖る凧形）
    private static readonly Vector2[] _dartCoreBuf = new Vector2[4];  // 連射ダート芯の光（先端寄り）
    private static readonly Vector2[] _petalBuf = new Vector2[4];     // 拡散花弁本体（短い凧形）
    private static readonly Vector2[] _petalCoreBuf = new Vector2[4]; // 拡散花弁芯の光
    private static readonly Vector2[] _finBuf = new Vector2[3];       // 誘導シーカーの後退フィン（上下で書き換えて2回描く）
    private static readonly Vector2[] _backBuf = new Vector2[4];      // 後方弾の菱形本体（進行方向へ尖る）
    private static readonly Vector2[] _backCoreBuf = new Vector2[4];  // 後方弾の芯の光
    private static Vector2[] BuildStarTemplate()
    {
        var t = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float ang = Mathf.DegToRad(-90f + i * 36f);
            float ratio = (i % 2 == 0) ? 1.15f : 0.48f;
            t[i] = new Vector2(Mathf.Cos(ang) * ratio, Mathf.Sin(ang) * ratio);
        }
        return t;
    }

    private CollisionShape2D _shape = null!;
    private CircleShape2D _circle = null!;

    public override void _Ready()
    {
        // 子に CollisionShape2D(CircleShape2D) を追加
        _circle = new CircleShape2D { Radius = Radius };
        _shape = new CollisionShape2D { Shape = _circle };
        AddChild(_shape);

        // 「祈り弾」（Erasable）のみ自機弾との重なりを自前処理する（MakeErasable が mask を開く）。
        AreaEntered += OnAreaEntered;

        // 初期状態は非アクティブ
        Deactivate();
    }

    // layer/mask/見た目/位置を設定し、可視化・monitoring 有効化。
    public void Activate(Vector2 pos, Vector2 vel, bool isEnemy, float radius, int damage,
        BulletShape shape = BulletShape.Orb, Color? tint = null, bool homing = false, bool backwardHoming = false)
    {
        Velocity = vel;
        IsEnemy = isEnemy;
        Damage = damage;
        Radius = radius;
        Active = true;
        Grazed = false;
        Pierce = 0; // 貫通数も再利用時に持ち越さない（付与は FireRapid 側）
        Chain = 0;  // 跳弾数も同様（付与は FireSpread 側）
        Word = "";  // 再利用時に前の言葉を持ち越さない
        Erasable = false;       // ギミックフラグも再利用時に持ち越さない
        SoftenOnGraze = false;
        Softened = false;
        Shape = shape;
        TintSet = tint.HasValue;
        if (tint.HasValue) Tint = tint.Value;
        Homing = homing;
        BackwardHoming = backwardHoming; // 再利用時に持ち越さない（既定 false）
        TurnRateOverride = 0;            // 旋回上書きも再利用時にリセット（付与は Spawn 後に設定）
        _homeTarget = null;
        _retargetT = 0f;                 // 再探索タイマーも持ち越さない（次フレームで即1回探索）
        // 加速球フラグ群も再利用時に必ずリセット（プール再利用で持ち越すと別の弾が誤加速する）。
        Accel = false; _accelDone = false; _accelDelay = 0f; _fastSpeed = 0f; _accelDir = Vector2.Zero; _age = 0f;

        // ノード回転のリセット（最重要：プール再利用で回転を持ち越すと別形状の弾が傾いて描かれる事故になる）。
        // Seeker（誘導の自機弾）だけがノード回転で向きを表現する：描画コマンドはこの Activate 直後の1回だけ
        // 記録し、以後の旋回追従は _PhysicsProcess の Rotation 代入（変換行列更新のみ＝再描画ゼロ）で行う。
        // 他の全弾形は常に 0（Dart/Petal は直進なので描画時の DrawSetTransform 1回で足りる）。
        Rotation = shape == BulletShape.Seeker && vel.LengthSquared() > 0.01f ? vel.Angle() : 0f;

        GlobalPosition = pos;

        // グループ登録（ボムの一括消去・グレイズ判定用）
        AddToGroup(isEnemy ? "enemy_bullets" : "player_bullets");

        // 当たり半径を反映
        if (_circle != null)
            _circle.Radius = radius;

        // レイヤー/マスク設定（isEnemy に応じて）
        if (isEnemy)
        {
            CollisionLayer = LayerEnemyBullet; // 8
            CollisionMask = MaskEnemyBullet;   // 1
        }
        else
        {
            CollisionLayer = LayerPlayerBullet; // 2
            CollisionMask = MaskPlayerBullet;   // 4
        }

        // 可視化・検出有効化。衝突状態の変更はシグナル中でも安全なよう遅延設定する
        // （Deactivate と順序を揃え、再利用時の取り違えを防ぐ）。
        Visible = true;
        SetPhysicsProcess(true);
        SetDeferred(Area2D.PropertyName.Monitoring, true);
        SetDeferred(Area2D.PropertyName.Monitorable, true);
        if (_shape != null)
            _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, false);

        QueueRedraw();
    }

    // 非表示・monitoring 無効化・プールへ戻る準備。
    public void Deactivate()
    {
        Active = false;
        Velocity = Vector2.Zero;

        // グループから外す（プール返却時）
        RemoveFromGroup("enemy_bullets");
        RemoveFromGroup("player_bullets");

        Visible = false;
        SetPhysicsProcess(false);
        // 衝突の無効化は遅延で行う。被弾シグナル（OnAreaEntered → Despawn）の最中に
        // monitoring / shape.disabled を直接書き換えると Godot にブロックされ、
        // 「見えないのに当たり判定だけ残る弾」が発生していた。Active=false は即時なので、
        // 消費側（Player/Panel）が Active を見ている限り遅延中の1フレームも安全。
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        if (_shape != null)
            _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
    }

    // 「祈り弾」にする（Spawn 後に呼ぶ。こはる FanDown）。自機弾(layer=2)を拾う mask を開き、
    // 消せる合図の淡い暖色ハロを再描画で反映する。Activate が mask/フラグを毎回リセットするので持ち越さない。
    public void MakeErasable()
    {
        Erasable = true;
        CollisionMask |= LayerPlayerBullet;
        QueueRedraw();
    }

    // 加速球にする（Spawn 直後に呼ぶ）。発射直後は自機のすぐ近くで chargeSpeed のほぼ静止でタメ、
    //   delaySec 秒後に fastSpeed へロケット発進する。発進方向は Spawn 時の vel の向きで確定して保持する
    //   （chargeSpeed を極小にしても向きが失われない＝タメ中の Velocity から向きを復元しない）。
    public void MakeAccel(float chargeSpeed, float fastSpeed, float delaySec)
    {
        Accel = true;
        _accelDone = false;
        _accelDelay = delaySec;
        _fastSpeed = fastSpeed;
        float len = Velocity.Length();
        _accelDir = len > 0.01f ? Velocity / len : Vector2.Right; // 発進方向を確定（vel が空なら右へ）
        Velocity = _accelDir * chargeSpeed;                        // タメ中はこの向きへごく僅かに進む（ほぼ静止）
        QueueRedraw();
    }

    // 祈り弾×自機弾の重なり：双方消して「受け止めた」の手応え＋やさしさ微加算。
    // 自機弾も消費する＝雨を受け止めるぶん本体への火力が落ちる（受け皿のコスト＝リスクとリターン）。
    private void OnAreaEntered(Area2D area)
    {
        if (!Active || !IsEnemy || !Erasable) return;
        if (area is Bullet pb && !pb.IsEnemy && pb.Active)
        {
            var pool = GetNodeOrNull<BulletPool>("/root/Pool");
            pool?.Despawn(pb);
            FxLayer.Instance?.BulletToPetal(GlobalPosition); // 弾が花びらへ＝“祈りを受け止めた”
            Audio.Instance?.PlayStrip();                     // 軽い「コツッ」（剥離と同域＝浄化より一段軽い）
            GetNodeOrNull<GameManager>("/root/Game")?.AddPrayerCleared();
            if (pool != null) pool.Despawn(this); else Deactivate();
        }
    }

    // 連鎖の光（chain_light）：この拡散弾が敵/パネルに消費された瞬間、最寄りの「別の敵」へ跳弾する。
    // 消費側（Enemy 本体ヒット／Panel インク削り）が Despawn の直前に呼ぶ。威力×0.4（下限1）で残数を引き継ぐ。
    //
    // バランス査定メモ（新奥義バランス査定）：
    //   Panel.OnAreaEntered はインク−1を Damage 参照なしで行う（Panel.cs:103）ため、この×0.4は
    //   パネル持ちの雑魚/バズ壁が主対象のときは実質無意味（Mathf.Max(1,…)の下限で常に1相当のインク欠損を
    //   起こす）。実際に効いているのは「射程無制限で必ず1体拾える追加ヒット」のほうで、拡散(5〜9way)×
    //   跳弾2（chain_2）を重ねると密集waveでの掃討速度が跳ね上がりすぎる。0.4自体は（ボス本体の
    //   露出窓ヒットでは効いており、そこは１〜4クランプ済みで無害）据え置きつつ、跳弾の探索を
    //   ChainRange 以内の「近い敵」に限定＝密集狙いのリスクリターン（寄せて撃つほど得）に寄せる。
    private const float ChainRange = 170f; // 跳弾の最大到達距離(px)。画面幅384の約44%＝近くの群れだけ拾う
    public void TryChain(Node2D? exclude)
    {
        if (Chain <= 0 || IsEnemy || !Active) return;
        Node2D? best = null;
        float bestD = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is Enemy e && !e.IsPurified && !ReferenceEquals(e, exclude))
            {
                float d2 = e.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                if (d2 < bestD) { bestD = d2; best = e; }
            }
        }
        if (best == null || bestD > ChainRange * ChainRange) return; // 跳ね先がいない／遠すぎるなら何も起きない
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        Vector2 dir = (best.GlobalPosition - GlobalPosition).Normalized();
        var nb = pool.Spawn(GlobalPosition, dir * 320f, isEnemy: false, 2.6f,
            Mathf.Max(1, Mathf.RoundToInt(Damage * 0.4f)), Shape); // 弾形を引き継ぐ＝跳弾しても花弁のまま（弾の素性が読める）
        nb.Chain = Chain - 1; // Lv2 は2回まで連鎖（威力は跳ねるたび×0.4）
    }

    // 「キミ弾」のグレイズ軟化（あかり）：一度だけ減速×GrazeSoftenMul＋淡色化。
    // 当たり判定（半径・被弾処理）は一切変えない＝“安全になる”のではなく“読める”ようになる。
    public void ApplyGrazeSoften()
    {
        if (!SoftenOnGraze || Softened) return;
        Softened = true;
        Velocity *= GrazeSoftenMul;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active)
            return;

        // 会話中（吹き出し表示中）は飛んでいる弾も止める＝攻撃を停止
        if (Hud.BubblePaused)
            return;

        // 経過時間を進める（会話停止中は上で return 済み＝弾停止と整合）。
        _age += (float)delta;

        // 加速球：_accelDelay 秒（タメ）経過した瞬間に、確定済みの発進方向へロケット発進（1回だけ・瞬間切替）。
        //   タメ中の Velocity はほぼ 0 なので向き復元は使わず、MakeAccel で保持した _accelDir を使う（len≈0破綻回避）。
        if (Accel && !_accelDone)
        {
            if (_age >= _accelDelay)
            {
                _accelDone = true;
                Velocity = _accelDir * _fastSpeed;
            }
            QueueRedraw(); // タメ中は充填リングを毎フレーム脈動（発進の瞬間は尾へ切替）
        }

        // ホーミング：右側の最寄りの穢れ標的へ向きを補間（速度の大きさは一定）。
        if (Homing && !IsEnemy)
        {
            SteerToTarget((float)delta);
            // シーカー形の旋回追従は「ノード回転」で行う。毎フレーム QueueRedraw で描き直す方式は
            // 80発前後の滞留で CanvasItem 描画コマンドの再記録が積み重なり FPS が 85→9 まで崩落した（QA実測）。
            // Rotation 代入は RenderingServer の変換行列更新のみ＝描画コマンドは Activate 時の1回のまま。
            if (Shape == BulletShape.Seeker && Velocity.LengthSquared() > 0.01f)
                Rotation = Velocity.Angle();
        }

        GlobalPosition += Velocity * (float)delta;

        // 自機のホーミング弾は 2.5 秒で寿命切れ（画面内を漂う“自機弾の雲”を作らない）。
        if (Homing && !IsEnemy && _age >= HomingLife)
        {
            var hp = GetNodeOrNull<BulletPool>("/root/Pool");
            if (hp != null) hp.Despawn(this); else Deactivate();
            return;
        }

        // 画面外(余白16px)に出たら Despawn
        var p = GlobalPosition;
        if (p.X < -Margin || p.X > ScreenWidth + Margin ||
            p.Y < -Margin || p.Y > ScreenHeight + Margin)
        {
            var pool = GetNodeOrNull<BulletPool>("/root/Pool");
            if (pool != null)
                pool.Despawn(this);
            else
                Deactivate();
        }
    }

    // 標的をロックしつつ、0.25 秒ごとに再探索して「明らかに近い別の敵」へ乗り換える。
    // 旧実装は消滅/浄化時しか探し直さず、遠ざかる敵をロックしたまま無駄弾になっていた。
    // 全探索は RetargetInterval に1回だけ＝毎フレーム探索ではないので負荷は旧実装以下。
    private void SteerToTarget(float delta)
    {
        var tgt = _homeTarget;
        bool lost = tgt == null || !IsInstanceValid(tgt) || (tgt is Enemy en && en.IsPurified);
        _retargetT -= delta;
        if (lost)
        {
            tgt = _homeTarget = AcquireTarget();
            _retargetT = RetargetInterval;
        }
        else if (_retargetT <= 0f)
        {
            _retargetT = RetargetInterval;
            var cand = AcquireTarget();
            // 現標的より十分（距離比 0.6 以下＝二乗で 0.36 以下）近ければ乗り換える。
            if (cand != null && !ReferenceEquals(cand, tgt) && tgt != null &&
                cand.GlobalPosition.DistanceSquaredTo(GlobalPosition)
                    <= tgt.GlobalPosition.DistanceSquaredTo(GlobalPosition) * (RetargetGain * RetargetGain))
                tgt = _homeTarget = cand;
        }
        if (tgt == null) return; // 標的が無ければ直進

        float spd = Velocity.Length();
        if (spd < 0.01f) return;
        float cur = Velocity.Angle();
        float want = (tgt.GlobalPosition - GlobalPosition).Angle();
        float turn = TurnRateOverride > 0 ? TurnRateOverride : HomingTurnRate; // 誘導速射・後方追尾で旋回を上げる
        float maxStep = Mathf.DegToRad(turn) * delta;
        float na = cur + Mathf.Clamp(Mathf.AngleDifference(cur, want), -maxStep, maxStep);
        Velocity = new Vector2(Mathf.Cos(na), Mathf.Sin(na)) * spd;
    }

    // 未浄化の敵本体から最寄りを選ぶ。探索範囲は「弾自身の進行方向の側」＝X 座標をハードコードで
    // 右/左に決め打ちしない（自機の向き反転で前方が左になっても、後方弾が右を向いても正しく働く）。
    // 進行方向の符号 sx（+1=右へ飛んでいる / -1=左へ飛んでいる）で前方判定を組み立てる。
    // Velocity がほぼ 0（加速球のタメ中など）の場合だけ、フラグから従来の既定（前方=右/後方=左）へ落とす。
    private Node2D? AcquireTarget()
    {
        float vx = Velocity.X;
        float sx = Mathf.Abs(vx) > 0.01f ? Mathf.Sign(vx) : (BackwardHoming ? -1f : 1f);
        Node2D? best = null;
        float bestD = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is Enemy e && !e.IsPurified)
            {
                // 進行方向の側にいる敵だけを狙う（4px の緩衝は自機に重なった敵を取りこぼさないため）。
                bool inRange = (e.GlobalPosition.X - GlobalPosition.X) * sx > -4f;
                if (!inRange) continue;
                float d = e.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                if (d < bestD) { bestD = d; best = e; }
            }
        }
        return best;
    }

    public override void _Draw()
    {
        if (!Active)
            return;

        float r = Radius;

        // 言葉弾：X の“晒し投稿”そのものが弾。ステージ選択/ボスカード等と同じ X カード語彙
        // （アバター丸＋認証バッジ［色丸＋白✓］＋@ハンドル＋本文＋いいね）に寄せる。
        // ただし弾は極小・大量に出る＝可読性最優先（吉田 §1/§10）。読みの優先度は
        //   ①本文（刺さる言葉）＞②“ポストだ”の記号（アバター＋✓）＞③メタ（@ハンドル/いいね）。
        var wf = WordFont;
        if (!string.IsNullOrEmpty(Word) && wf != null)
        {
            const int fs = 9, hs = 6, ms = 6; // 本文/ハンドル/いいねの文字サイズ
            var sz = wf.GetStringSize(Word, HorizontalAlignment.Left, -1, fs);
            const float av = 5.5f, pad = 3f, gap = 3.5f, metaH = 6.5f;
            bool hasHandle = !string.IsNullOrEmpty(_wordHandle);
            float handleH = hasHandle ? 6.5f : 0f;
            float bodyW = sz.X;
            // ハンドル行ぶんも幅に効かせる（本文より短ければ本文幅で決まる）。
            if (hasHandle) bodyW = Mathf.Max(bodyW, wf.GetStringSize(_wordHandle, HorizontalAlignment.Left, -1, hs).X);
            float cw = pad + av * 2f + gap + bodyW + pad;
            float bodyBlock = handleH + Mathf.Max(av * 2f - handleH, sz.Y);
            float ch = pad + Mathf.Max(av * 2f, bodyBlock) + metaH + pad;
            float x0 = -cw / 2f, y0 = -ch / 2f;

            // 投稿カード（X ダーク・角丸ガラス）＝ハブ/ステージ選択のカードと同じ UiKit.Box で統一。
            // StyleBox はアンチエイリアスが効くので小サイズでも角丸が潰れない。
            UiKit.Box(this, new Rect2(x0, y0, cw, ch), new Color(0.05f, 0.06f, 0.10f, 0.9f), 5f,
                new Color(KegareWord.R, KegareWord.G, KegareWord.B, 0.34f), 1f);

            // アバター（穢れ）＋認証バッジ（右下：小さな桃丸＋白✓）＝ボス/スペルカードと同じ語彙。
            Vector2 ac = new Vector2(x0 + pad + av, y0 + pad + av);
            DrawCircle(ac, av, new Color(0.35f, 0.13f, 0.27f));
            Vector2 bc = ac + new Vector2(av * 0.7f, av * 0.7f);
            DrawCircle(bc, av * 0.5f, KegareWord);
            DrawWordCheck(bc, av * 0.34f, new Color(1f, 1f, 1f, 0.95f)); // 認証✓（白・2線分）

            // 右カラム：①@ハンドル（小・淡）→②本文（晒し言葉・主役）
            float bx = x0 + pad + av * 2f + gap;
            float ty = y0 + pad;
            if (hasHandle)
            {
                DrawString(wf, new Vector2(bx, ty + hs - 1f), _wordHandle, HorizontalAlignment.Left, -1, hs,
                    new Color(0.74f, 0.60f, 0.72f, 0.85f));
                ty += handleH;
            }
            DrawString(wf, new Vector2(bx, ty + sz.Y - 2f), Word, HorizontalAlignment.Left, -1, fs, KegareWord);

            // いいね（小さなハート＋数）＝拡散の重み
            float ly = y0 + ch - 2.2f;
            DrawWordHeart(new Vector2(bx + 2f, ly - 2.4f), 2.2f, new Color(KegareWord.R, KegareWord.G, KegareWord.B, 0.9f));
            DrawString(wf, new Vector2(bx + 8f, ly), _wordLikes, HorizontalAlignment.Left, -1, ms,
                new Color(0.78f, 0.62f, 0.74f, 0.9f));

            // 致命点（カード中心＝当たり判定）。自機の被弾点と同じ記号語彙＝赤コア＋白フチ。
            // 「文字は脅し、刺さるのはこの点」を一目で読ませる（§3 視認性／§7 理不尽回避）。
            DrawCircle(Vector2.Zero, Radius + 1f, new Color(1f, 1f, 1f, 0.95f));
            DrawCircle(Vector2.Zero, Radius, new Color(1f, 0.2f, 0.3f, 1f));
            return;
        }

        if (!IsEnemy)
        {
            // 加速球：通常自機弾（水色）と区別できる琥珀色のガラス弾。
            //   タメ中（発進前）は脈動する充填リングで「いまタメている」を、発進後は進行方向へ尾を引く
            //   ストリークで「ロケット発進した」を視覚化（余力の見た目・当たり判定は不変）。
            if (Accel)
            {
                if (!_accelDone)
                {
                    // タメ中：発進が近いほど速く脈動する収縮リング（チャージ感）。
                    float prog = _accelDelay > 0.01f ? Mathf.Clamp(_age / _accelDelay, 0f, 1f) : 1f; // 0→1
                    float pulse = 0.5f + 0.5f * Mathf.Sin((float)_age * (10f + 18f * prog));
                    float ring = r * (2.4f - 1.2f * prog) + r * 0.4f * pulse; // 発進が近いほど締まる
                    DrawArc(Vector2.Zero, ring, 0, Mathf.Tau, 24,
                        new Color(AccelGlow.R, AccelGlow.G, AccelGlow.B, 0.35f + 0.45f * prog), 1.4f, true);
                }
                else if (Velocity.LengthSquared() > 0.01f)
                {
                    // 発進後：進行方向と逆へ細い光の尾（速さの表現）。
                    Vector2 back = -Velocity.Normalized();
                    DrawLine(Vector2.Zero, back * (r * 3.2f), new Color(AccelGlow.R, AccelGlow.G, AccelGlow.B, 0.55f), r * 0.8f, true);
                }
                DrawGlassBullet(r, AccelMid, AccelEdge, AccelGlow);
                return;
            }
            // モード別シルエット×カラー（色＋形の二重符号化＝撃った瞬間に「今どのモードか」が完全に読める）。
            //   連射＝水色ダート／拡散＝翠の花弁／誘導＝青藤シーカー／加速球＝琥珀（上の Accel 分岐）／後方弾＝淡い金の菱形。
            //   1モードにつき edge/mid/白の3色に絞り、明度を水色と同格に揃える＝敵弾（警告色）より控えめを保つ。
            switch (Shape)
            {
                case BulletShape.Dart:    DrawPlayerDart(r);    return;
                case BulletShape.Petal:   DrawPlayerPetal(r);   return;
                case BulletShape.Seeker:  DrawPlayerSeeker(r);  return;
                case BulletShape.Diamond: DrawPlayerDiamond(r); return; // 後方弾（FireBackfire）＝淡い金
            }
            // 形未指定の自機弾（オプション/フォロワー/後方弾/救済弾など）は従来のガラス円弾（浄化の水色）。
            DrawGlassBullet(r, PlayerMid, PlayerEdge, PlayerGlow);
            return;
        }

        // 敵弾：スペルの色（未指定なら既定の穢れ色）と弾形で描く。
        Color c = TintSet ? Tint : EnemyMid;
        // グレイズ軟化済み（キミ弾）：白へ寄せた淡色＝「和らいだ」を色で読ませる（判定は不変）。
        if (Softened) c = c.Lerp(new Color(1f, 1f, 1f), 0.5f);
        switch (Shape)
        {
            case BulletShape.Diamond: DrawDiamond(r, c); break;
            case BulletShape.Star:    DrawStar(r, c);    break;
            case BulletShape.Ring:    DrawRing(r, c);    break;
            case BulletShape.Needle:  DrawNeedle(r, c);  break;
            case BulletShape.Rice:    DrawRice(r, c);    break;
            default:                  DrawOrb(r, c);     break; // 円弾＝白リング＋暗芯（芯色のみ可変）
        }

        // 当たり芯（#16 見える化）：弾中心の高輝度ドット＝「刺さるのはこの点」。
        // 言葉弾の赤コアと同じ発想を通常弾へ。弾形の色を隠さないよう小さく・白のみ（派手にしない）。
        DrawCircle(Vector2.Zero, Mathf.Min(1.5f, r * 0.42f), new Color(1f, 1f, 1f, 0.9f), true, -1f, true);

        // 祈り弾（消せる弾）の合図：淡い暖白のハロリング＝「自機弾で受け止められる」を一目で。
        if (Erasable)
            DrawArc(Vector2.Zero, r + 2.4f, 0, Mathf.Tau, 24, new Color(1f, 0.95f, 0.8f, 0.55f), 1.1f, true);
    }

    // 認証バッジの白✓。極小サイズではフォント✓が潰れるので2線分のチェック記号で描く。
    private void DrawWordCheck(Vector2 c, float r, Color col)
    {
        Vector2 a = c + new Vector2(-r, 0.05f * r);
        Vector2 b = c + new Vector2(-0.25f * r, 0.75f * r);
        Vector2 d = c + new Vector2(r, -0.7f * r);
        DrawLine(a, b, col, 0.7f, true);
        DrawLine(b, d, col, 0.7f, true);
    }

    // 言葉弾カードの小さなハート（いいね）。2円＋三角で簡易描画。
    private void DrawWordHeart(Vector2 c, float r, Color col)
    {
        DrawCircle(c + new Vector2(-r * 0.45f, -r * 0.2f), r * 0.6f, col);
        DrawCircle(c + new Vector2(r * 0.45f, -r * 0.2f), r * 0.6f, col);
        DrawColoredPolygon(new[]
        {
            c + new Vector2(-r * 0.9f, 0f), c + new Vector2(r * 0.9f, 0f), c + new Vector2(0f, r),
        }, col);
    }

    // 外周グロー（box-shadow 相当）：薄い同心円を外→内に重ねてぼかしを近似。
    private void DrawGlow(float baseR, Color glow, float reach = 1.3f)
    {
        const int gSteps = 5;
        for (int i = gSteps; i >= 1; i--)
        {
            float t = i / (float)gSteps;
            float gr = baseR * (1f + reach * t);
            float a = 0.16f * (1f - t) + 0.04f;
            DrawCircle(Vector2.Zero, gr, new Color(glow.R, glow.G, glow.B, a), true, -1f, true);
        }
    }

    // HTML(Refrain HUD A) のガラス円弾：外周グロー＋白ハイライト→中間→暗エッジのグラデ。
    private void DrawGlassBullet(float r, Color mid, Color edge, Color glow)
    {
        DrawGlow(r, glow);
        DrawCircle(Vector2.Zero, r, edge, true, -1f, true);
        DrawCircle(Vector2.Zero, r * 0.82f, edge.Lerp(mid, 0.6f), true, -1f, true);
        DrawCircle(Vector2.Zero, r * 0.60f, mid, true, -1f, true);
        var hl = new Vector2(-0.28f * r, -0.36f * r);
        DrawCircle(hl, r * 0.34f, new Color(1f, 1f, 1f, 0.95f), true, -1f, true);
    }

    // ───── 自機弾のモード別シルエット（吉田 §1：形で読む・1モード3色＝edge/mid/白に絞る）─────
    // 自機弾は「敵弾より控えめ」が掟（弾幕で読む主役は敵弾）：グローは敵弾 DrawGlow(5段) より薄い2段だけ。
    private void DrawPlayerGlow(float baseR, Color glow)
    {
        DrawCircle(Vector2.Zero, baseR * 1.9f, new Color(glow.R, glow.G, glow.B, 0.09f), true, -1f, true);
        DrawCircle(Vector2.Zero, baseR * 1.35f, new Color(glow.R, glow.G, glow.B, 0.16f), true, -1f, true);
    }

    // 連射＝光のダート（水色＝現行主力の顔）：進行方向へ長く尖る凧形。「まっすぐ速い主力弾」を形そのもので語る。
    // 芯の白い光を先端寄りに通し、速度の向きがシルエットだけで読めるようにする（花弁との違いは縦横比）。
    private void DrawPlayerDart(float r)
    {
        float ang = Velocity.LengthSquared() > 0.01f ? Velocity.Angle() : 0f;
        DrawSetTransform(Vector2.Zero, ang, Vector2.One);
        DrawPlayerGlow(r * 0.85f, PlayerGlow);
        _dartBuf[0] = new Vector2(2.4f * r, 0f);          // 前へ長く尖る＝速さ
        _dartBuf[1] = new Vector2(-0.3f * r, -0.62f * r);
        _dartBuf[2] = new Vector2(-1.4f * r, 0f);         // 後端は短い矢羽根
        _dartBuf[3] = new Vector2(-0.3f * r, 0.62f * r);
        DrawColoredPolygon(_dartBuf, PlayerMid);
        for (int i = 0; i < 4; i++) _dartCoreBuf[i] = _dartBuf[i] * 0.5f + new Vector2(0.45f * r, 0f); // 光は先端へ寄せる
        DrawColoredPolygon(_dartCoreBuf, new Color(1f, 1f, 1f, 0.92f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // 拡散＝花弁（翠＝敵弾に無い唯一色）：短く幅広の凧形。1枚は小さく、扇に開いた瞬間に翠の花になる。
    // 数が出るモードなのでグローは1段だけ＝面で光り過ぎて敵弾を埋もれさせない。跳弾（TryChain）も同色で一貫。
    private void DrawPlayerPetal(float r)
    {
        float ang = Velocity.LengthSquared() > 0.01f ? Velocity.Angle() : 0f;
        DrawSetTransform(Vector2.Zero, ang, Vector2.One);
        DrawCircle(Vector2.Zero, r * 1.5f, new Color(SpreadGlow.R, SpreadGlow.G, SpreadGlow.B, 0.13f), true, -1f, true);
        _petalBuf[0] = new Vector2(1.5f * r, 0f);
        _petalBuf[1] = new Vector2(-0.1f * r, -0.85f * r);
        _petalBuf[2] = new Vector2(-0.95f * r, 0f);
        _petalBuf[3] = new Vector2(-0.1f * r, 0.85f * r);
        DrawColoredPolygon(_petalBuf, SpreadMid);
        for (int i = 0; i < 4; i++) _petalCoreBuf[i] = _petalBuf[i] * 0.5f + new Vector2(0.18f * r, 0f);
        DrawColoredPolygon(_petalCoreBuf, new Color(1f, 1f, 1f, 0.85f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // 誘導＝彗星シーカー（青藤＝菫より青く・藍より明るく）：ガラス玉の頭＋後退フィン＋短い尾。曲がる軌跡が尾で映える。
    // 描画はローカル +X 向きでこの1回だけ記録し、旋回への追従はノード回転（Activate の初期角＋
    // _PhysicsProcess の Rotation 代入）で表現する＝毎フレームの再描画ゼロ（大量滞留時の FPS 崩落対策）。
    // 頭のハイライトは回転に連れて回る（左上固定の光源統一より性能を優先。小径なので読みは崩れない）。
    private void DrawPlayerSeeker(float r)
    {
        // 短い尾（加速球ストリークの前例。より短く淡く＝敵弾より控えめ）。
        DrawLine(new Vector2(-0.8f * r, 0f), new Vector2(-3.0f * r, 0f),
            new Color(HomingGlow.R, HomingGlow.G, HomingGlow.B, 0.30f), r * 0.45f, true);
        // 後退フィン×2（上下）。頭より一段沈んだ色＝シルエットは立つが頭の読みを邪魔しない。
        _finBuf[0] = new Vector2(0.1f * r, -0.5f * r);
        _finBuf[1] = new Vector2(-1.6f * r, -1.25f * r);
        _finBuf[2] = new Vector2(-1.1f * r, -0.2f * r);
        DrawColoredPolygon(_finBuf, PlayerFin);
        _finBuf[0] = new Vector2(0.1f * r, 0.5f * r);
        _finBuf[1] = new Vector2(-1.6f * r, 1.25f * r);
        _finBuf[2] = new Vector2(-1.1f * r, 0.2f * r);
        DrawColoredPolygon(_finBuf, PlayerFin);
        // 頭（小さなガラス玉）＋グロー。
        DrawPlayerGlow(r * 0.85f, HomingGlow);
        DrawCircle(Vector2.Zero, r, HomingEdge, true, -1f, true);
        DrawCircle(Vector2.Zero, r * 0.72f, HomingMid, true, -1f, true);
        DrawCircle(new Vector2(-0.26f * r, -0.32f * r), r * 0.3f, new Color(1f, 1f, 1f, 0.95f), true, -1f, true);
    }

    // 後方弾（FireBackfire）＝淡い金の菱形：進行方向へ尖らせた菱形で「前方3モードとは別枠」を形でも語る。
    // ダート/シーカーと同じ作法（DrawSetTransform で進行方向へ回転・控えめな2段グロー・白ハイライト）。
    // 敵弾の菱形（DrawDiamond）は無回転の45度菱形だが、こちらは進行方向に長い菱形にして向きが読めるようにする。
    private void DrawPlayerDiamond(float r)
    {
        float ang = Velocity.LengthSquared() > 0.01f ? Velocity.Angle() : 0f;
        DrawSetTransform(Vector2.Zero, ang, Vector2.One);
        DrawPlayerGlow(r * 0.85f, BackGlow);
        float s = r * 1.15f;
        _backBuf[0] = new Vector2(1.5f * s, 0f);   // 前へ尖る
        _backBuf[1] = new Vector2(0f, -0.85f * s);
        _backBuf[2] = new Vector2(-1.1f * s, 0f);  // 後端も短く尖る＝敵弾の菱形との違いを保つ
        _backBuf[3] = new Vector2(0f, 0.85f * s);
        DrawColoredPolygon(_backBuf, BackMid);
        for (int i = 0; i < 4; i++) _backCoreBuf[i] = _backBuf[i] * 0.5f;
        DrawColoredPolygon(_backCoreBuf, new Color(1f, 1f, 1f, 0.88f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // 円弾：作品準拠の「白リング＋暗芯」。芯色のみスペル色で可変（Danmaku v3 shapeInner orb）。
    private void DrawOrb(float r, Color core)
    {
        DrawGlow(r, core);
        DrawCircle(Vector2.Zero, r, new Color(1f, 1f, 1f, 0.95f), true, -1f, true); // 白リング
        DrawCircle(Vector2.Zero, r * 0.70f, new Color(0.086f, 0.039f, 0.071f), true, -1f, true); // 暗芯リング
        DrawCircle(Vector2.Zero, r * 0.40f, core, true, -1f, true); // 芯色
    }

    // 菱形：45度回転の四角＋グロー（shapeInner diamond）。頂点は static バッファへ書き込み（new 割当なし）。
    private void DrawDiamond(float r, Color c)
    {
        DrawGlow(r, c, 1.1f);
        float s = r * 1.15f;
        _diaBuf[0] = new Vector2(0, -s); _diaBuf[1] = new Vector2(s, 0);
        _diaBuf[2] = new Vector2(0, s);  _diaBuf[3] = new Vector2(-s, 0);
        DrawColoredPolygon(_diaBuf, c);
        for (int i = 0; i < 4; i++) _diaCoreBuf[i] = _diaBuf[i] * 0.5f;
        DrawColoredPolygon(_diaCoreBuf, new Color(1f, 1f, 1f, 0.85f)); // 芯の光
    }

    // 星：5芒星（shapeInner star の clip-path 相当）。単位方向テンプレ×r を static バッファへ（trig も new も無し）。
    private void DrawStar(float r, Color c)
    {
        DrawGlow(r, c, 1.1f);
        for (int i = 0; i < 10; i++) _starBuf[i] = _starTemplate[i] * r;
        DrawColoredPolygon(_starBuf, c);
    }

    // リング：中空の輪（shapeInner ring）。
    private void DrawRing(float r, Color c)
    {
        DrawGlow(r, c, 1.0f);
        DrawArc(Vector2.Zero, r * 0.9f, 0, Mathf.Tau, 28, c, Mathf.Max(1.4f, r * 0.42f), true);
        DrawArc(Vector2.Zero, r * 0.9f, 0, Mathf.Tau, 28, new Color(c.R, c.G, c.B, 0.5f), 0.9f, true);
    }

    // 針：進行方向へ伸びる細い弾（shapeInner needle）。
    private void DrawNeedle(float r, Color c)
    {
        float ang = Velocity.LengthSquared() > 0.01f ? Velocity.Angle() : Mathf.Pi / 2f;
        DrawSetTransform(Vector2.Zero, ang, Vector2.One);
        float len = r * 2.8f, w = r * 0.78f;
        DrawGlow(r * 0.8f, c, 0.8f);
        DrawRect(new Rect2(-len * 0.5f, -w * 0.5f, len, w), c);
        DrawCircle(new Vector2(-len * 0.5f, 0), w * 0.5f, c, true, -1f, true); // 後端の丸
        DrawCircle(new Vector2(len * 0.5f, 0), w * 0.5f, c, true, -1f, true);  // 先端の丸
        DrawRect(new Rect2(-len * 0.5f, -w * 0.18f, len, w * 0.36f), new Color(1f, 1f, 1f, 0.55f)); // 中央のハイライト
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // 粒弾：進行方向へ細長い楕円（shapeInner rice）。
    private void DrawRice(float r, Color c)
    {
        float ang = Velocity.LengthSquared() > 0.01f ? Velocity.Angle() : Mathf.Pi / 2f;
        DrawGlow(r * 0.8f, c, 0.8f);
        DrawSetTransform(Vector2.Zero, ang, new Vector2(1.15f, 0.5f));
        DrawCircle(Vector2.Zero, r, c, true, -1f, true);
        DrawCircle(new Vector2(-r * 0.25f, -r * 0.25f), r * 0.4f, new Color(1f, 1f, 1f, 0.7f), true, -1f, true);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
