// EnemySpec / StageTheme : 道中ザコのステージ別スポーンテーブル。
// 「どの絵・どんな挙動のザコが湧くか」を表データで持ち、Spawner が StageTheme に応じて引く。

// 各ステージの「心象世界」テーマ。Stage*.cs が Spawner.Theme に渡す。
public enum StageTheme
{
    Default, // 既存挙動（アンチくん/うつむきさん）。StageW0 等そのまま。
    Rei,     // 配信枠（視聴者アイコン / 空の吹き出し）
    Akari,   // 退勤後のフロア（向かいの席の机 / 送信取消の束）
    Koharu,  // 推し活の部屋（ペンライト / グッズの箱）
    Mina,
}

// 道中ザコの固有攻撃パターン。本体(MidEnemy)が _spec.Pattern で発射を分岐する。
// None=撃たない（drifter の無弾種・Default drifter）。弾数/間隔は Enemy.Dn/Di で難易度スケールする。
public enum AttackPattern
{
    None,            // 撃たない
    ReiLockBurst,    // レイ視聴者アイコン：ロックオン連射（予告→自機方向3連バースト）
    ReiPulseRing,    // レイ空の吹き出し：視線パルス放射（全方位等間隔リング）
    AkariScatter,    // あかり向かいの席の机：ばらまき投擲（固定左方向扇）
    AkariDrop,       // あかり送信取消の束：落書きドロップ（真下へ低速）
    KoharuSharp3,    // こはるペンライト：高速鋭3WAY（短予告→自機方向3本）
    KoharuSimmer,    // こはるグッズの箱：とろ火ゆらぎ弾（自機狙い超低速単発）
    DefaultAim,      // アンチくん：自機狙い単発（現状踏襲）
    FlankAim,        // 回り込み「引用リプ」：右から出現→上下端を走行→自機後方(x≈40)に着座→右向き低速単発（全テーマ共通）
    BuzzWall,        // 盾もち「バズ壁」：撃たない・遅い・硬い（パネル5×インク3）＝DPSチェックの壁（全テーマ・波B/C限定）
    KoharuPrayerCarry, // 祈り運び：消せる祈り弾を3発ぶら下げて横断するボーナス種（こはる面専用・お残し禁止の練習台）
    AkariDeadline,
    AkariUnsent,
    AkariVacant,
    KoharuComparison,
    KoharuCheer,
    KoharuParcel,
    ReiAnonymous,
    ReiClipper,
    ReiMetrics,
    // FINAL（ミナの内側）の残響3種。以前は eraser/unanswered が DefaultAim、memory が None で
    // 「絵以外の差分ゼロ」だった（2026-09-17 差別化）。3種とも固有の弾の動きを持つ：
    MinaEraser,      // 消しゴムの残響：書いた線を消す＝自機狙い1本の「消し線」を、間を置いて2度なぞる（低速→加速）
    MinaMemory,      // 記憶の残響：撃たないのをやめ、過去に自分が居た場所へ弾を落とす（遅延置き弾・自機は狙わない）
    MinaUnanswered,  // 未応答の残響：既読がつかない＝左へ投げた弾が減速して止まりかける（届かない弾）
}

// 1種のザコの見た目（pre/post テクスチャ）と挙動パラメータ。
public readonly struct EnemySpec
{
    public readonly string PreTexPath;
    public readonly string PostTexPath;
    public readonly int Points;
    public readonly float BodyRadius;
    public readonly float MoveSpeed;
    public readonly float SpinSpeed;
    public readonly bool Fires;        // 暴言弾を撒くか（撃つ種/撃たない種）
    public readonly float FireInterval;
    public readonly float SwayAmp;     // 上下うねりの振幅（0=直進）
    public readonly float SwayFreq;
    public readonly AttackPattern Pattern; // 固有攻撃パターン（既定 None＝撃たない、後方互換）
    public readonly bool Humanoid;
    public readonly bool FlipH;
    // 撃破時に散る「心の欠片」の粒数の倍率（1.0＝従来どおり 10〜16 粒）。2026-09-17 差別化。
    //   ★経済（ショップ通貨）には一切影響しない。FxLayer.PurifyBurst は impBase/points を
    //     「総和保存」で n 粒へ割る（impBase/n ＋ 余りを先頭から1ずつ）ので、n をいくつに変えても
    //     拾い切ったときの総額は同一。ここで動くのは「見た目の量と拾う回数」だけ（§2-3 リターンは即・明確に）。
    //   配分の原則＝倒す手間（パネル×インク／耐久時間）と、居座って与えてくる圧に比例させる。
    public readonly float ShardWeight;

    public EnemySpec(string pre, string post, int points, float bodyRadius,
        float moveSpeed, float spinSpeed, bool fires, float fireInterval,
        float swayAmp = 0f, float swayFreq = 0f,
        AttackPattern pattern = AttackPattern.None, bool humanoid = false, bool flipH = true,
        float shardWeight = 1f)
    {
        PreTexPath = pre;
        PostTexPath = post;
        Points = points;
        BodyRadius = bodyRadius;
        MoveSpeed = moveSpeed;
        SpinSpeed = spinSpeed;
        Fires = fires;
        FireInterval = fireInterval;
        SwayAmp = swayAmp;
        SwayFreq = swayFreq;
        Pattern = pattern;
        Humanoid = humanoid;
        FlipH = flipH;
        ShardWeight = shardWeight;
    }
}

// テーマ → 2種(撃つ種A / 撃たない種B)のテーブル。
public static class EnemyTable
{
    // 各テーマの「撃つ種」（GlyphMote相当：速め・直進・弾を撒く）。
    // レイ＝視聴者アイコン / あかり＝向かいの席の机 / こはる＝ペンライト。
    // 各テーマの「撃たない種」（PageShard相当：ゆっくり・上下にうねる・無口）。
    // レイ＝空の吹き出し / あかり＝送信取消の束 / こはる＝グッズの箱。
    // 旧 char/enemy_* は消していない（StageW0 等の非正典が参照する）。
    public static (EnemySpec shooter, EnemySpec drifter) For(StageTheme theme) => theme switch
    {
        // 発射は本体(MidEnemy)が Pattern で行う。fires/fireInterval はパネル発射用の旧値で、
        // 本体発射では参照しない（Panel 側の自前発射は無効化済み）。各種の弾速・間隔・弾数は MidEnemy 内で定義。
        // 欠片の粒数（ShardWeight）：撃つ種＝1.15／撃たない種＝0.85 を基準に、
        //   「居座って圧をかけてくる度合い」で微調整する。ザコは全種6ヒットなので手間は同じ＝
        //   差をつける根拠は“倒すと楽になる度合い”（脅威度）に置く（§2-1 比例させる）。
        StageTheme.Rei => (
            new EnemySpec("res://char/v3/enemy_rei_icon_pre.png", "res://char/v3/enemy_rei_icon_post.png",
                100, 4f, moveSpeed: 56f, spinSpeed: 1.5f, fires: true, fireInterval: 1.8f,
                pattern: AttackPattern.ReiLockBurst, shardWeight: 1.25f),   // 予告ビーム＝盤面を切る最大の脅威
            new EnemySpec("res://char/v3/enemy_rei_bubble_pre.png", "res://char/v3/enemy_rei_bubble_post.png",
                80, 5f, moveSpeed: 26f, spinSpeed: 0.9f, fires: false, fireInterval: 0f,
                swayAmp: 12f, swayFreq: 1.6f, pattern: AttackPattern.ReiPulseRing, shardWeight: 1.0f)), // 全方位リング＝撃たない種でも圧は中

        StageTheme.Akari => (
            new EnemySpec("res://char/v3/enemy_akari_desk_pre.png", "res://char/v3/enemy_akari_desk_post.png",
                100, 5f, moveSpeed: 46f, spinSpeed: 1.2f, fires: true, fireInterval: 2.0f,
                pattern: AttackPattern.AkariScatter, shardWeight: 1.15f),
            new EnemySpec("res://char/v3/enemy_akari_undo_pre.png", "res://char/v3/enemy_akari_undo_post.png",
                80, 4f, moveSpeed: 30f, spinSpeed: 1.1f, fires: false, fireInterval: 0f,
                swayAmp: 16f, swayFreq: 1.3f, pattern: AttackPattern.AkariDrop, shardWeight: 0.85f)), // 真下へ落とすだけ＝左端の自機には届きにくい

        StageTheme.Koharu => (
            new EnemySpec("res://char/v3/enemy_koharu_penlight_pre.png", "res://char/v3/enemy_koharu_penlight_post.png",
                100, 4f, moveSpeed: 60f, spinSpeed: 1.6f, fires: true, fireInterval: 1.7f,
                pattern: AttackPattern.KoharuSharp3, shardWeight: 1.25f),  // 最短間隔1.6s×高速3WAY＝道中で一番刺さる
            new EnemySpec("res://char/v3/enemy_koharu_box_pre.png", "res://char/v3/enemy_koharu_box_post.png",
                80, 6f, moveSpeed: 24f, spinSpeed: 0.8f, fires: false, fireInterval: 0f,
                swayAmp: 10f, swayFreq: 1.1f, pattern: AttackPattern.KoharuSimmer, shardWeight: 0.85f)), // 超低速単発＝見てから避けられる

        // Default: 既存アンチくん/うつむきさん素材。Spawner が GlyphMote/PageShard を直接使う想定だが、
        // テーブル経由でも同じ姿が出るよう一応そろえておく。
        _ => (
            new EnemySpec("res://char/enemy_anti_pre.png", "res://char/enemy_anti_post.png",
                100, 4f, moveSpeed: 50f, spinSpeed: 1.4f, fires: true, fireInterval: 1.9f,
                pattern: AttackPattern.DefaultAim),
            new EnemySpec("res://char/enemy_anti_pre.png", "res://char/enemy_anti_post.png",
                80, 5f, moveSpeed: 28f, spinSpeed: 1.0f, fires: false, fireInterval: 0f)),
    };

    private static EnemySpec Character(EnemySpec basis, string stage, string id, bool flipH = false,
        AttackPattern? pattern = null, float shardWeight = 1f)
    {
        string path = $"res://char/v3/enemies/{stage}/enemy_{id}_pre.png";
        return new EnemySpec(path, path, basis.Points, basis.BodyRadius, basis.MoveSpeed,
            basis.SpinSpeed, pattern.HasValue || basis.Fires, basis.FireInterval, basis.SwayAmp, basis.SwayFreq,
            pattern ?? basis.Pattern, humanoid: true, flipH: flipH, shardWeight: shardWeight);
    }

    // 人型12種の欠片配分。基準 1.0 に対し、MidEnemy.BaseInterval（撃つ頻度）と斉射の弾数・
    // 盤面の塞ぎ方（避け場を奪う度合い）で 0.8〜1.4 に振る。詳細な根拠は各行のコメント。
    private static readonly EnemySpec[] AkariCharacters =
    {
        // 締切の人：3.2s 間隔／加速する3WAY（タメ→瞬間発進）＝道中で唯一の“読み違えると即死ぬ”弾。
        Character(For(StageTheme.Akari).shooter, "akari", "deadline", pattern: AttackPattern.AkariDeadline, shardWeight: 1.3f),
        // 送信取消の人：3.4s／2斉射×2発の低速（64px/s）。圧は中庸。
        Character(For(StageTheme.Akari).drifter, "akari", "unsent", pattern: AttackPattern.AkariUnsent, shardWeight: 1.0f),
        // 空席の人：4.4s と最も遅いが、縦の壁（1箇所だけ隙間）で避け場を強制する＝残すと盤面が狭いまま。
        Character(For(StageTheme.Akari).drifter, "akari", "vacant", pattern: AttackPattern.AkariVacant, shardWeight: 1.2f),
    };

    private static readonly EnemySpec[] KoharuCharacters =
    {
        // 比較の人：3.0s／左斜め扇3発を交互に振る。こはる面の主力。
        Character(For(StageTheme.Koharu).shooter, "koharu", "comparison", pattern: AttackPattern.KoharuComparison, shardWeight: 1.15f),
        // 声援の人：3.8s／2斉射×3発（狭→広の二段扇）＝一度に6発と道中最多。
        Character(For(StageTheme.Koharu).shooter, "koharu", "cheer", pattern: AttackPattern.KoharuCheer, shardWeight: 1.25f),
        // 荷物の人：4.6s と全種で最も寡黙・38px/s の超低速＝ほぼ置物。
        Character(For(StageTheme.Koharu).drifter, "koharu", "parcel", pattern: AttackPattern.KoharuParcel, shardWeight: 0.8f),
    };

    private static readonly EnemySpec[] ReiCharacters =
    {
        // 匿名の人：3.1s／予告方向へ100px/s の3連射。速い・刺さる＝レイ面の主力。
        Character(For(StageTheme.Rei).shooter, "rei", "anonymous", flipH: true, pattern: AttackPattern.ReiAnonymous, shardWeight: 1.2f),
        // 切り抜きの人：3.8s／上下2origin から挟み込む。読めば抜けられる中庸。
        Character(For(StageTheme.Rei).shooter, "rei", "clipper", pattern: AttackPattern.ReiClipper, shardWeight: 1.05f),
        // 数字の人：4.2s／上から降る4本。落下は最も避けやすい。
        Character(For(StageTheme.Rei).drifter, "rei", "metrics", pattern: AttackPattern.ReiMetrics, shardWeight: 0.9f),
    };

    // FINAL（ミナの内側）の残響3種。2026-09-17 まで eraser/unanswered は DefaultAim・memory は None で
    //   「絵以外の差分ゼロ」だった。固有パターンを新設し、粒数も役割に応じて分けた。
    //   この面は3体しか湧かない（SpawnLimit=3）＝1体あたりの手応えを厚くしてよい＝基準を高めに置く。
    private static readonly EnemySpec[] MinaCharacters =
    {
        // 消しゴムの残響：書いた線を消しに来る＝低速の「消し線」を2度なぞる。避けは易しいが二度来る。
        Character(For(StageTheme.Default).shooter, "mina", "eraser", pattern: AttackPattern.MinaEraser, shardWeight: 1.3f),
        // 記憶の残響：自機を狙わず“自分が居た場所”へ置き弾を落とす＝盤面に痕が残る（最も厄介）。
        Character(For(StageTheme.Default).drifter, "mina", "memory", pattern: AttackPattern.MinaMemory, shardWeight: 1.4f),
        // 未応答の残響：届かない弾＝減速して止まりかける。脅威は低いが「消えない」＝画面に残る。
        Character(For(StageTheme.Default).shooter, "mina", "unanswered", pattern: AttackPattern.MinaUnanswered, shardWeight: 1.2f),
    };

    public static System.Collections.Generic.IReadOnlyList<EnemySpec> CharactersFor(StageTheme theme) => theme switch
    {
        StageTheme.Akari => AkariCharacters,
        StageTheme.Koharu => KoharuCharacters,
        StageTheme.Rei => ReiCharacters,
        StageTheme.Mina => MinaCharacters,
        _ => System.Array.Empty<EnemySpec>(),
    };

    // 第4種：回り込み「引用リプ」（FlankAim）。スキンは各テーマの“撃つ種”を流用（新規アート不要）し、
    // 挙動だけ差し替える：右から出現→上下端を走行→自機後方(x≈40)に着座→右向き低速単発。
    // 走行は「回り込み経路を“見せて”からすぐ圧に移る」テンポ確保のため、他のザコよりは速い側に置く
    //   （ただし自機より速くはしない＝下の 2026-09-22 の項を参照）。
    //
    // ★2026-09-22（ユーザー要望「敵が自機より速いのをやめて」）：110→56。
    //   110px/s は自機の素の足（Player.NormalSpeed=75、最遅ジョブ 結び手では 66）を大きく超えており、
    //   「回り込まれたら走って逃げても追いつかれる」＝避ける手段が無い＝理不尽（§7）だった。
    //   道中ザコの実効速度は MidEnemy.ApproachCeil(58px/s) で最終クランプされるので、ここを 110 のまま
    //   残しても挙動上は 58 に抑えられる。が、宣言値が嘘だと次に読む人が誤読するので実値に合わせる。
    //   56 は他の“撃つ種”（Rei 56 / Koharu 60 / Akari 46）と同格＝「走行レーンを渡る」という役割は保つ。
    //   速度で稼いでいた圧は、速度以外（出現位置＝自機の背後／2区間の読める経路／着座後の単発）で担保する。
    private const float FlankMoveSpeed = 56f; // 走行速度(px/s)。上限は MidEnemy.ApproachCeil(58)
    public static EnemySpec Flanker(StageTheme theme)
    {
        var (shooter, _) = For(theme);
        // 欠片 1.1：手間は通常ザコと同じ6ヒットだが、自機の背後（x≈40）に陣取って左端の安置を潰す＝
        // 「わざわざ振り向いて倒しに行く」リスクへの見返り。無視して前へ出続けると背中を撃たれ続ける。
        return new EnemySpec(shooter.PreTexPath, shooter.PostTexPath, shooter.Points, shooter.BodyRadius,
            moveSpeed: FlankMoveSpeed, spinSpeed: shooter.SpinSpeed, fires: true,
            fireInterval: shooter.FireInterval,
            pattern: AttackPattern.FlankAim, shardWeight: 1.1f);
    }

    // 盾もち種「バズ壁」（BuzzWall）。スキンは各テーマの“撃たない種”を流用（新規アート無し）し、
    // 撃たない・遅い・硬い（パネル数/インクは MidEnemy が Pattern で上書き）に差し替える。
    // 高ポイント＝剥がし切るDPSチェックへの対価（リスクとリターン：無視もできるが報酬は大きい）。
    //
    // バランス査定メモ（新奥義バランス査定）：通常ザコはパネル3×インク2＝6ヒットで撃破（MidEnemy.cs:80-81）。
    // バズ壁はパネル5×インク3＝15ヒット＝通常の2.5倍の手間。旧 points=150 は通常ザコ（平均約90）の
    // 約1.67倍にしかならず、「無視もできるが報酬は大きい」という上の設計コメントに対して手間と報酬が
    // 比例していなかった（§2-1）。ヒット数倍率(2.5x)に揃えて 150→220（≒平均90×2.45）に引き上げ、
    // 剥がし切った時の点数リターンを手間に見合わせる。Score は経済(Impression)や進行(PurifiedCount)には
    // 影響しない純粋なスコアボーナスなので、この増分はゲームバランス（難易度・経済）には波及しない。
    private const float BuzzWallMoveSpeed = 18f; // のそのそ進む壁（設計目安値）
    public static EnemySpec BuzzWall(StageTheme theme)
    {
        var (_, drifter) = For(theme);
        // 欠片 2.5：パネル5×インク3＝15ヒットで、通常ザコ（3×2＝6ヒット）のちょうど 2.5 倍の手間。
        // points を 150→220（≒2.45倍）へ揃えたのと同じ根拠で粒数も 2.5 倍にする＝
        // 「撃たない壁を、あえて剥がし切った」手間が“こぼれる欠片の量”として目に見える（§2-1 比例／§2-3 即・明確に）。
        // 25〜40粒＝拾い切るのに一拍かかる量で、DPSチェックを通した実感が手触りとして残る。
        return new EnemySpec(drifter.PreTexPath, drifter.PostTexPath, points: 220,
            bodyRadius: drifter.BodyRadius + 2f, moveSpeed: BuzzWallMoveSpeed, spinSpeed: drifter.SpinSpeed * 0.6f,
            fires: false, fireInterval: 0f,
            pattern: AttackPattern.BuzzWall, shardWeight: 2.5f);
    }

    // 祈り運び種（KoharuPrayerCarry・こはる面専用）。スキンはこはるの“撃たない種”（グッズの箱＝運ぶ絵柄）を流用。
    // 消せる祈り弾3発をぶら下げて横断するボーナス種＝脅威ではないので低ポイント・すぐ剥がせる（MidEnemy 側で調整）。
    private const float PrayerCarrySpeed = 34f; // 横断速度(px/s)。約11秒で画面を渡り切る
    public static EnemySpec PrayerCarrier()
    {
        var (_, drifter) = For(StageTheme.Koharu);
        // 欠片 0.45：パネル2×インク1＝2ヒット＝通常ザコ(6)の 1/3 の手間。points も 60（平均90の 0.67倍）。
        // そのうえ本体を落とせば祈り弾3発ぶんの AddPrayerCleared が別経路で必ず入る＝報酬の本体はそちら。
        // ここで欠片まで通常量出すと「一番楽な敵が一番おいしい」＝ノーリスクの稼ぎ（§7）になる。
        // 5〜7粒＝“ボーナス種を拾った”ことは分かるが、稼ぎの最適解にはならない量に抑える。
        return new EnemySpec(drifter.PreTexPath, drifter.PostTexPath, points: 60,
            bodyRadius: drifter.BodyRadius, moveSpeed: PrayerCarrySpeed, spinSpeed: drifter.SpinSpeed,
            fires: false, fireInterval: 0f,
            pattern: AttackPattern.KoharuPrayerCarry, shardWeight: 0.45f);
    }
}
