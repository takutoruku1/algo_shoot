using Godot;

// MidEnemy : 道中ザコの汎用版。テクスチャパスと挙動パラメータを EnemySpec で受け取り、
// ステージごとに「心象世界」の姿・撃ち方を出し分ける（6個のサブクラスを量産しない）。
// 発射はパネルではなく本体が一括で行い、_spec.Pattern で種ごとの固有弾幕を撃ち分ける。
// 弾数=Dn(基準値)、間隔=Di(基準値) で必ず難易度スケールし、弾速は素の基準値（BulletSpeedMul が自動で乗る）。
// 発射は「画面内に一定時間見えたら」開く（CanFire）＝進入中でも撃つ。画面外からの理不尽撃ちは起きない。
public partial class MidEnemy : Enemy
{
    private EnemySpec _spec;
    private double _swayT;
    private float _baseY;
    private bool _baseYSet;
    private float _campX;   // この位置まで来たら居座る（GlyphMote/PageShard と同じ作法）
    private float _vy;      // 居座り中の上下往復（うねらない種でもゆっくり動かす）
    private bool _camped;   // 居座り点に到達して居座り開始したか（発射可否は CanFire が決める）
    // 居座り目標点（Spawner が出現エッジに応じて指定。未指定なら右からの直進フォールバック）。
    private Vector2 _campTarget;
    private bool _entryConfigured;
    // Spawner から AddChild 前に呼ぶ：場内のどこへ進入して居座るか。
    public void SetEntry(Vector2 campTarget) { _campTarget = campTarget; _entryConfigured = true; }
    // 回り込み（FlankAim）用の経由点：上下端の走行レーン終端。ここを通ってから着座点へ折れる
    //（＝右から出現→端を走って自機の後方へ回り込む、が2区間の直進で読める形になる）。
    private Vector2 _viaTarget;
    private bool _viaConfigured;
    // Spawner から AddChild 前に呼ぶ：経由点（走行レーン終端）→着座点の順で進入する。
    public void SetFlankEntry(Vector2 via, Vector2 campTarget) { _viaTarget = via; _viaConfigured = true; SetEntry(campTarget); }
    // 「進入中は絶対に撃たない」フラグ（Hard 以上の左＝自機の背後からの湧き専用・2026-09-17）。
    //   自機の弾は右へしか飛ばないので、背後から入ってくる敵が歩きながら撃つと「見えない・撃ち返せない」の
    //   二重苦＝理不尽になる。着座するまで無言で歩かせ、着座位置（自機から十分右）に着いて初めて撃たせる。
    //   ＝FlankAim（引用リプ）と同じ「経路を先に読ませる」作法を、左エッジ全般へ広げたもの。
    private bool _silentEntry;
    public void SetSilentEntry(Vector2 campTarget) { _silentEntry = true; SetEntry(campTarget); }

    // 発射タイマー（居座り後に駆動）。バースト等のサブ状態もここで管理する。
    private double _fireT;
    private int _burstLeft;     // ロックオン連射の残り発数
    private double _burstT;     // バースト内の小間隔タイマー
    private Vector2 _burstDir;  // バースト方向（予告で固定した自機方向）
    private double _telegraphT;  // 予告中の残り秒（>0 で予告表示中＝まだ撃たない）
    private Vector2 _characterOrigin;
    private int _characterAttackIndex;
    private int _salvoRemaining;
    private int _salvoIndex;
    private double _salvoT;
    // 「記憶の残響」（MinaMemory）が辿った道の記録。自機ではなく“自分が居た場所”へ弾を置くための履歴。
    // 一定間隔でサンプリングし、古いものから捨てる固定長リング（毎フレーム確保ゼロ）。
    private readonly Vector2[] _trail = new Vector2[TrailLen];
    private int _trailCount;
    private int _trailHead;
    private double _trailT;
    private const int TrailLen = 12;
    private const double TrailStep = 0.33;  // 約0.33秒ごとに1点＝12点で直近およそ4秒ぶんの足跡

    // 進入が遅すぎると居座る前に浄化されて「撃たずに去る」ので、進入だけ最低速度を保証する
    // （居座り後の上下うねり＝種の性格は据え置き）。居座った瞬間に初弾を素早く撃つ＝設置→即攻撃。
    // ★90→46（2026-09-22）。倍率レンジ [0.62, 1.85] を掛けると実効 28.5〜85.1px/s で、
    //   下の ApproachCeil(58) に当たるのは速い側（Hurry/Dash/StepCut/Bounce ほか mul>1.26 の種）だけ。
    //   遅い種（Trudge 0.70→32.2 / Settle 0.78→35.9 / Wall 0.72→33.1）は天井に触れず、
    //   種ごとの「速い／遅い」の差はそのまま残る。90 のままだと全種が天井に張り付いて個性が消え、
    //   34 まで落とすと最遅個体の着座が 20秒級になって盤面が渋滞した（同時出現上限を食い潰す）。
    //   46 なら最遅 28.5px/s でも盤面対角 460px を約 16秒、実際の進入距離（150〜280px）なら 5〜10秒で着座する。
    private const float ApproachFloor = 46f;     // 進入の最低速度(px/s)
    private const double FirstShotDelay = 0.25;  // 発射ゲートが開いてこの秒で初弾（0.55→0.25）。

    // ─── 進入速度の絶対上限（2026-09-22 ユーザー要望「敵が自機より速いのをやめる」）───
    // ★不変条件：**道中ザコの移動速度は、どんな種・どんな進入フェーズでも自機の素の足を超えない。**
    //   自機の実効速度は Player.NormalSpeed(75) × MoveSpeedMul(強化 1.5) × JobDef.MoveMul。
    //   基準に採るのは「強化なし・いちばん足の遅いジョブ」＝結び手(MoveMul=0.88) の 75×0.88 = 66px/s。
    //   強化前提で上限を引くと「機動力強化を買うまで振り切れない」＝救済を買わせる設計になり本末転倒。
    //   さらに 66 ちょうどだと同速＝真後ろから追われると永久に距離が詰まったままになるので、
    //   明確に下回る 58px/s を上限に置く（66 の約 88%＝1秒で 8px ずつ必ず引き離せる）。
    //   ※ロック中(LockMoveMul=0.8→52.8px/s)はこれを下回るが、ロックは**自機が能動的に選ぶ取引**
    //     （照準を任せる代わりに足が重くなる＝§2-2 リスクは能動的に選ばせる）であり、G/R3/右クリックで
    //     いつでも解除できる。解除すれば必ず振り切れる、が保証されていればよい。
    // ★2026-09-22 修正：この上限は**合成移動ベクトルの長さ**に掛ける（UpdateMovement 内の正規化）。
    //   前進成分だけに掛けていた当初の実装では、横に膨らむ種で 58×√(1+0.55²) ≒ 66.2px/s まで
    //   伸びてこの不変条件を破っていた（QA 実測でも最大ちょうど 66px/s を観測）。
    private const float ApproachCeil = 58f;

    // ─── 移動の個性（2026-09-17 ユーザー要望「アンチャーごとに移動パターンも変更して」）───
    // それまで進入は全種が「目標点へ単一直進・速度 Max(MoveSpeed, ApproachFloor)」で完全に同一、
    // 着座後も全種 ±14px/s の上下往復＋SwayAmp の有無だけだった。
    // ここを種ごとに割り、前段で作った待機モーション（装飾）と性格を揃える
    //   （例：締切の人は小走り＝進入も急いで詰める／空席の人はほとんど動かない）。
    //
    // ★進行不能・理不尽を作らないための三つの不変条件（この節の設計はすべてこれに従う）：
    //   ①【必ず着座する】進入は常に「目標点への直進」で、速度倍率は必ず ApproachSpeedMin 以上。
    //      軌道の演出（弧・段）は“直線に対する横オフセット”として乗せ、進捗 p→1 で必ず 0 に収束させる。
    //      ＝目標点そのものは一切動かさない。減速も 0 にはしない＝残距離は単調に減り、有限時間で着座する。
    //   ②【経路が読める】横オフセットの最大は ArcMaxOffset(=26px) に制限。曲がるのは“ひと膨らみ”までで、
    //      折り返したり自機を追い回したりしない（この作品の「進入経路は丸見え」方針を維持）。
    //   ③【盤面外へ出ない】着座後の横の揺れは camp.X の周囲 ±CampDriftMax(=9px) に固定クランプし、
    //      さらに Field.Left+18 〜 Field.Right-18 で二重に締める。左湧きの着座 x=184..224 も、
    //      ±9px では自機側（Field.Left=120）へ届かない＝SetSilentEntry の保証を壊さない。
    // 倍率のレンジは「種ごとの性格の差」を作るためのもの。実効速度そのものは下の ApproachCeil(58px/s) で
    // 最終クランプされるので、倍率の上限を 1.85 のまま残しても自機より速くはならない
    //   （＝遅い種と速い種の“相対的な差”は保ったまま、絶対値だけ自機以下に押し込む）。
    private const float ApproachSpeedMin = 0.62f;  // 進入速度倍率の下限（＝実効 28.5px/s。必ず前進する）
    private const float ApproachSpeedMax = 1.85f;  // 同・上限（最速種は下の ApproachCeil=58px/s で頭打ち）
    private const float ArcMaxOffset = 26f;        // 進入軌道の横ふくらみ最大(px)。これ以上は経路が読めなくなる
    private const float CampDriftMax = 9f;         // 着座後の横揺れ最大(px)。camp.X からの片振幅
    private const float CampMarginX = 18f;         // 盤面左右端からの安全マージン(px)
    // 1フレームの横移動量を、同フレームの前進量の何倍までに許すか。1 未満なら前進成分は必ず正＝必ず着座する。
    private const float LateralStepRatio = 0.55f;

    // 種ごとの移動の型。LivingMotion（装飾）と 1:1 で対応させ、「見た目の動き」と「実際の動き」を揃える。
    private enum MoveStyle
    {
        Straight,      // 既定：等速直進＋±14px/s 上下往復（従来どおり。スキン流用種・Default）
        Hurry,         // 締切の人：最初から速く、着く直前に更に詰める。着座後もせかせか速い上下
        Hesitant,      // 送信取消の人：進んでは緩む（進入が脈打つ）。着座後は行きつ戻りつの短い往復
        Absent,        // 空席の人：ゆっくり入り、着座後はほとんど動かない（12種で最も静か）
        Compare,       // 比較の人：左右に大きく膨らんで入る。着座後は上下に広く往復（見比べる）
        Bounce,        // 声援の人：弾みながら入る（上下に波打つ）。着座後も大きく弾む
        Trudge,        // 荷物の人：重くゆっくり。着座後は沈んだまま僅かに漂う
        Jitter,        // 匿名の人：細かく震えながら直進。着座後も小刻みに上下
        StepCut,       // 切り抜きの人：段でカクッと進む（コマ送り）。着座後も段で上下に飛ぶ
        Scroll,        // 数字の人：一定速で入り、着座後は下→上へ登って一気に戻る（鋸波）
        Erase,         // 消しゴムの残響：横に擦るように左右へ揺れながら入る
        Drift,         // 記憶の残響：漂う。ゆっくり大きく蛇行して入り、着座後も広く漂う
        Await,         // 未応答の残響：入りは普通、着座後はほぼ静止して長周期で一度だけ浮く
        Patrol,        // レイ視聴者アイコン（ドローン）：一定速で入り、着座後は速めに上下を巡回
        Hover,         // レイ空の吹き出し（目）：ふわりと入り、着座後はゆっくり大きく浮遊
        Walk,          // あかり机：歩く。等速＋一歩ごとの上下。着座後も歩幅のある往復
        Flutter,       // あかり送信取消の束：ひらひら。上下に波打って入り、着座後も舞う
        Dash,          // こはるペンライト：速い。まっすぐ突っ込んで着座後も鋭く上下
        Settle,        // こはるグッズの箱：入りは遅く、着座後はほとんど動かない（重い荷）
        Wall,          // バズ壁：のそのそ。着座後は上下もほぼ動かない（射線を塞ぎ続ける壁）
    }
    private MoveStyle _move;
    private float _movePhase;      // 個体ごとの位相ずらし（群れが同じ軌道で重ならないように）
    private double _moveT;         // 移動用の経過秒（装飾用 _motionT とは独立）
    private float _approachTotal;  // 進入開始時の目標点までの距離（進捗 p の分母。0 除算は下で回避）
    private float _arcSign;         // 弧の向き（±1・個体ごとにランダム＝群れが同じ側へ膨らまない）
    private double _campPatrolT;    // 着座後の巡回位相
    private float _lastLateral;     // 前フレームの横オフセット（差分適用用。位置の直接置換をしない）
    private float _campBaseX;       // 着座した瞬間の X（横揺れはこの周囲でのみ振れる）

    // ─── 進入撃ちのゲート（2026-09-06）───
    //   旧仕様は「居座り点に着くまで一切撃たない」＝出現から 1.5〜3.2 秒、射線上を無防備に歩くだけの
    //   空白があり、その間に自機の DPS でザコ HP(6ヒット) を超える約18ヒットぶん削れて「撃つ前に死ぬ」。
    //   ゲートを「画面内に入って一定時間見えている」に変え、居座る前でも撃てるようにする。
    //   画面外・出現直後は従来どおり撃たない（理不尽撃ちは作らない）。
    private const float FireGateX = Field.Right - 44f;   // このXより左＝盤面に入り切った（右端は Field.Right+14 から出現）
    private const double FireGateVisible = 0.7;  // 画面内でこの秒数を過ぎたら撃ってよい
    private double _visibleT;                    // 画面内に居た累計秒（進入中のみ積む）
    private bool _approachFired;                 // 進入中に発射ゲートが開いたか（居座り時の初弾プライムを二重にしない）

    // 盤面内か（出現エッジ＝右外/上外/下外から入ってくるので、上下も見る）。
    private bool OnScreen => GlobalPosition.X < FireGateX && GlobalPosition.X > Field.Left
                          && GlobalPosition.Y > Field.Top && GlobalPosition.Y < Field.Bottom;

    // 撃ってよいか。居座っていれば従来どおり無条件、進入中は「画面内に FireGateVisible 秒」で開く。
    // 回り込み（FlankAim）は走行中に撃たない＝背後へ回る経路を先に読ませる設計を維持する。
    // 撃たない種（BuzzWall／KoharuPrayerCarry＝BaseInterval 999）は進入撃ちの対象外＝盾/運び専念のまま。
    private bool CanFire => _camped
        || (!_silentEntry && _spec.Pattern != AttackPattern.FlankAim && BaseInterval() < 900.0
            && _visibleT >= FireGateVisible);

    // 接触半径(px)。EnemySpec.BodyRadius（種ごとの絵の大きさ由来）は使わず全種で一定にする：
    //   ザコは「絵が大きい＝当たりも大きい」で読み分けさせる要素ではなく、どの種でも
    //   「体に触れたら痛い」が同じ手触りで返るほうが避けの判断が安定する（2026-09-06）。
    private const float ZakoBodyRadius = 8f;

    // パターン別の基準発射間隔（秒・難易度スケール前）。Di() を掛けて使う。初弾プライムと共用。
    private double BaseInterval() => _spec.Pattern switch
    {
        AttackPattern.ReiLockBurst => 2.2,
        AttackPattern.ReiPulseRing => 3.6,
        AttackPattern.AkariScatter => 2.0,
        AttackPattern.AkariDrop    => 3.2,
        AttackPattern.KoharuSharp3 => 1.6,
        AttackPattern.KoharuSimmer => 3.0,
        AttackPattern.DefaultAim   => 1.9,
        AttackPattern.FlankAim     => 2.4,  // 既存の撃つ種（1.6〜2.2）よりやや遅め＝背後からの圧は緩く
        AttackPattern.AkariDeadline => 3.2,
        AttackPattern.AkariUnsent => 3.4,
        AttackPattern.AkariVacant => 4.4,
        AttackPattern.KoharuComparison => 3.0,
        AttackPattern.KoharuCheer => 3.8,
        AttackPattern.KoharuParcel => 4.6,
        AttackPattern.ReiAnonymous => 3.1,
        AttackPattern.ReiClipper => 3.8,
        AttackPattern.ReiMetrics => 4.2,
        // FINAL（ミナの内側）：3体しか湧かない面＝1体あたりの見せ場を厚くする。
        // 他面の人型（3.1〜4.6）より短めに置いて、少数でも道中の密度が薄くならないようにする。
        AttackPattern.MinaEraser => 2.9,
        AttackPattern.MinaMemory => 3.4,
        AttackPattern.MinaUnanswered => 3.0,
        // BuzzWall / KoharuPrayerCarry は撃たない（盾専念/運び専念）＝既定 999 に落とす。
        _ => 999.0,
    };

    // ─── “自走している生命感”モーション（吉田演出：1枚絵をtransformだけで生かす）───
    // _bodySprite(=子ノード"Body")の Position/Rotation/Scale だけを毎フレーム合成する。
    // 当たり判定（本体 Area2D／_bodyShape）と進行ロジック・SwayAmp は一切触らない（純・装飾）。
    // 種別＝_spec.Pattern からモチーフを判別し、特徴的な周期モーションを与える。
    private LivingMotion _motion;     // モチーフ別の動きの型（OnEnemyReady で確定）
    private float _motionPhase;       // 個体ごとの位相ずらし（群れの同期を防ぐ）
    private double _motionT;          // 生命感モーションの経過秒（_swayT とは独立に積む）
    private Sprite2D? _body;          // 子ノード"Body"のキャッシュ（毎フレーム探さない）
    private float _bodyBaseScale = 1f;// _bodySprite の素のスケール（Scale演出はこれに係数を掛ける）
    private float _gaze;              // 監視カメラの“ギョロッ”用：目標ヨー(rad)。たまに切り替える
    private double _gazeT;            // 次に視線を変えるまでの残り秒
    private float _spin;             // こはるペンライトの“クルッ”用：余韻回転(rad)。撃つ瞬間に蹴って減衰
    private float _kick;             // 予告/発射の溜め：一瞬の縦スカッシュ量（0で平常、減衰）
    private float _startle;          // 人型の“ビクッ”用：不定期に入る一瞬の身じろぎ（0で平常、減衰）
    private double _startleT;        // 次の身じろぎまでの残り秒

    // 1枚絵の transform で“生きてる感”を出す周期モーションの型（種＝モチーフごと）。
    // ─── 2026-09-17 差別化 ───
    //   以前は人型12種が Humanoid 1種類（±0.6px / ±0.025rad）に全員集約されていて、
    //   基底6種が 1:1 の専用モーションを持つのと対照的に「12人が同じ呼吸をしている」状態だった。
    //   ここを12種へ割り、各キャラの“意味”が動きで読めるようにする（§11 キャラは挙動で立てる）。
    //   全種とも当たり判定・進行・SwayAmp には一切触らない純・装飾レイヤ（従来どおり）。
    private enum LivingMotion
    {
        None, ReiDrone, ReiEye, AkariDesk, AkariNote, KoharuPenlight, KoharuBox,
        Humanoid,          // 人型の既定（未割当の人型が出たときの保険。現在どの種も使わない）
        // あかり面
        AkDeadline,        // 締切の人：急いで小走り。速い前後の詰め＋時計を見る前傾。息が上がっている
        AkUnsent,          // 送信取消の人：送ろうとして手が止まる。前へ出かけて引き戻す往復＋うつむき
        AkVacant,          // 空席の人：いない人。ほとんど動かず、時々すーっと薄れるように沈む
        // こはる面
        KoComparison,      // 比較の人：左右をきょろきょろ見比べる。首振りが主体
        KoCheer,           // 声援の人：腕を振って応援。大きく弾む上下＋左右の振り
        KoParcel,          // 荷物の人：抱えて耐える。沈んだまま細かく震え、たまに持ち直す
        // レイ面
        ReiAnon,           // 匿名の人：顔が無い。輪郭が細かくぶれ続ける（誰でもない揺らぎ）
        ReiClip,           // 切り抜きの人：切り取る。横方向に鋭くカクッと刻む（フレーム送りの動き）
        ReiMetrics,        // 数字の人：数字を追う。視線が下から上へゆっくり登り、ぱっと戻る（スクロール）
        // FINAL（ミナの内側）
        MinaEraserM,       // 消しゴムの残響：横にごしごし擦る往復
        MinaMemoryM,       // 記憶の残響：残像のように遅れて揺れる。輪郭が明滅して定まらない
        MinaUnansweredM,   // 未応答の残響：待っている。ほぼ静止＋長い周期で一度だけ小さく期待して浮く
    }

    // Spawner から AddChild 前に呼ぶ（OnEnemy Ready/_Ready より先に値を渡しておく）。
    public void Configure(in EnemySpec spec) => _spec = spec;

    // 種ごとの欠片の粒数倍率（EnemySpec.ShardWeight）。粒数だけが変わり、スコアも通貨も不変。
    protected override float ShardMul => _spec.ShardWeight;

    protected override void OnEnemyReady()
    {
        Points = _spec.Points;
        BodyRadius = ZakoBodyRadius;    // 全種共通（絵の大小に依らず「体に触れたら痛い」を一定に）
        PanelCount = 3;
        PanelInk = 2;
        OrbitRadius = 11.5f;            // 一回り小さく（周回をやや内側へ）
        PanelDisplayScale = 0.82f;      // パネル絵＆当たりを縮小
        SpinSpeed = _spec.SpinSpeed;
        PanelsFire = false; // 発射は本体へ移管。パネルは盾専念。
        PanelFireInterval = _spec.FireInterval;

        PreTexPath = _spec.PreTexPath;
        PostTexPath = _spec.PostTexPath;
        FaceLeft = _spec.FlipH;
        // 盾の絵は面ごとに Panel 側が解決する（Panel.ResolveTexPath）。ここでは指定しない。
        BodyDisplayH = _spec.Humanoid ? 30f : 23f;

        // 盾もち「バズ壁」：撃たない代わりにパネル5枚×インク3＝“剥がし切る”DPSチェック
        //（拡散/ホーミング/貫通の使い所を作る優先順位の壁）。体も大きく見せて「硬そう」を絵で予告
        //（新規アート無し＝サイズで語る）。
        if (_spec.Pattern == AttackPattern.BuzzWall)
        {
            PanelCount = 5;
            PanelInk = 3;
            OrbitRadius = 14.5f;        // 5枚が重ならない周回半径
            BodyDisplayH = 30f;         // 通常23より大きく＝壁の圧
        }
        // 祈り運び：脅威でなくボーナス寄り＝パネルは薄く（2枚×インク1）してすぐ本体を撃ち落とせるように。
        // 横断で去る前に間に合う手応えを守る（撃ち漏らしの学び＝「のこしちゃだめ」は残しつつ）。
        else if (_spec.Pattern == AttackPattern.KoharuPrayerCarry)
        {
            PanelCount = 2;
            PanelInk = 1;
        }

        // GlyphMote/PageShard と同じく、進入後は画面内に居座る（倒すまで去らない）。
        // これが無いと、道中ザコがパネルを剥がし切る前に左へ抜けてしまい「攻撃が通らない／無敵」に見える。
        _campX = GD.Randf() * 150f + 120f;                 // 120〜270 のどこかに陣取る
        _vy = (GD.Randf() < 0.5f ? -1f : 1f) * 14f;

        // 発射タイミングを種ごとにばらして同時斉射を避ける。
        _fireT = GD.Randf() * 0.8f;

        // 種ごとの弾形・色を1回だけ設定（FireBullet がこれを反映）。
        ApplySpellVisual();

        // 生命感モーション：モチーフ＝_spec.Pattern から動きの型を決め、位相を個体ごとにずらす。
        // ★人型も Pattern から 1:1 で引く（2026-09-17）。以前は Humanoid 一括で12種が同じ動きだった。
        _motion = MotionFor(_spec.Pattern);
        if (_motion == LivingMotion.None && _spec.Humanoid) _motion = LivingMotion.Humanoid; // 未割当の人型の保険
        _motionPhase = GD.Randf() * Mathf.Tau;   // 0〜2π：群れが同期しないよう全位相を散らす
        _gazeT = GD.RandRange(1.2, 2.6);         // 監視カメラ：最初の視線変更までの間
        _startleT = GD.RandRange(2.0, 5.0);      // 人型の“ビクッ”：最初の身じろぎまでの間

        // 移動の型（2026-09-17）。装飾モーションと 1:1 で対応させ、見た目と実際の動きの性格を揃える。
        _move = MoveFor(_spec.Pattern);
        _movePhase = GD.Randf() * Mathf.Tau;              // 軌道の位相も個体ごとに散らす（群れが重ならない）
        _arcSign = GD.Randf() < 0.5f ? -1f : 1f;          // 弧の膨らむ向きをランダムに（片側へ揃わない）
        _campPatrolT = GD.Randf() * Mathf.Tau;
        // 着座後の上下往復は種ごとの速さに（従来は全種 ±14px/s 固定）。向きは従来どおりランダム。
        _vy = (GD.Randf() < 0.5f ? -1f : 1f) * CampPatrolSpeed(_move);

        // 生命感モーション持ちは姿勢(Rotation)を自前で握る＝基底の移動バンクと競合させない。
        // None（アンチくん系＝素のまま）だけ基底 AutoBank に任せ、移動方向への傾きを得る。
        AutoBank = _motion == LivingMotion.None;
    }

    // 弾幕パターン → 移動の型。装飾（MotionFor）と同じ並びで 1:1 に対応させる。
    private static MoveStyle MoveFor(AttackPattern p) => p switch
    {
        // 基底6種（道具）：それぞれの装飾モーション（ドローン/目/机/ノート/ペンライト/箱）と性格を揃える。
        AttackPattern.ReiLockBurst => MoveStyle.Patrol,   // ホバリング → 速めに巡回
        AttackPattern.ReiPulseRing => MoveStyle.Hover,    // ふわふわ浮遊 → ゆっくり大きく浮遊
        AttackPattern.AkariScatter => MoveStyle.Walk,     // 脚で歩く → 一歩ごとの上下を伴う移動
        AttackPattern.AkariDrop    => MoveStyle.Flutter,  // 羽ばたき → ひらひら舞う
        AttackPattern.KoharuSharp3 => MoveStyle.Dash,     // 鋭い前傾 → まっすぐ速い
        AttackPattern.KoharuSimmer => MoveStyle.Settle,   // 重い箱 → 入りが遅く、着いたら動かない
        // 人型12種：前段の待機モーションと同じ性格を移動にも通す。
        AttackPattern.AkariDeadline => MoveStyle.Hurry,      // 小走り＋前傾
        AttackPattern.AkariUnsent   => MoveStyle.Hesitant,   // 出しかけて引き戻す
        AttackPattern.AkariVacant   => MoveStyle.Absent,     // ほとんど動かない
        AttackPattern.KoharuComparison => MoveStyle.Compare, // きょろきょろ見比べる
        AttackPattern.KoharuCheer      => MoveStyle.Bounce,  // 大きく弾む
        AttackPattern.KoharuParcel     => MoveStyle.Trudge,  // 抱えて耐える
        AttackPattern.ReiAnonymous => MoveStyle.Jitter,      // 輪郭が細かくぶれる
        AttackPattern.ReiClipper   => MoveStyle.StepCut,     // 段でカクッと刻む
        AttackPattern.ReiMetrics   => MoveStyle.Scroll,      // 下から上へ登って戻る
        AttackPattern.MinaEraser      => MoveStyle.Erase,    // 横に擦る
        AttackPattern.MinaMemory      => MoveStyle.Drift,    // 漂う
        AttackPattern.MinaUnanswered  => MoveStyle.Await,    // 待っている
        // 特殊種：バズ壁だけ「動かない壁」を移動でも徹底する。引用リプ／祈り運びは専用経路を持つので既定のまま
        //   （FlankAim は経由点つき2区間＝すでに固有の移動。祈り運びは TickPrayerCarry が横断を持つ）。
        AttackPattern.BuzzWall => MoveStyle.Wall,
        _ => MoveStyle.Straight,                             // Default/アンチくん・引用リプ・祈り運び
    };

    // 着座後の上下往復の速度(px/s)。従来は全種 14f 固定だった。
    // ★上限 26px/s：これ以上速いと「上下に暴れる的」になり、狙って撃つ手応え（当てる気持ちよさ）が落ちる。
    // ★下限 0px/s（Absent/Wall/Settle/Await）：止まる種があるからこそ動く種が際立つ（§3 緩急）。
    //   止まっていても浄化はできる＝進行不能にはならない（居座り＝倒すまで去らない は不変）。
    private static float CampPatrolSpeed(MoveStyle m) => m switch
    {
        MoveStyle.Hurry    => 26f,  // せかせか（最速）
        MoveStyle.Dash     => 24f,
        MoveStyle.Patrol   => 22f,
        MoveStyle.Bounce   => 21f,
        MoveStyle.Compare  => 19f,
        MoveStyle.Walk     => 16f,
        MoveStyle.Flutter  => 15f,
        MoveStyle.Straight => 14f,  // 従来値（スキン流用種・Default はここ＝手触りを変えない）
        MoveStyle.Jitter   => 13f,
        MoveStyle.Hesitant => 11f,
        MoveStyle.Erase    => 11f,
        MoveStyle.Hover    => 10f,
        MoveStyle.Drift    => 9f,
        MoveStyle.Trudge   => 7f,
        MoveStyle.StepCut  => 0f,   // 段送りは _vy を使わず自前で飛ぶ（下の CampVerticalOverride）
        MoveStyle.Scroll   => 0f,   // 鋸波も自前
        MoveStyle.Absent   => 0f,   // 動かない三種（Absent/Settle/Await）＋壁
        MoveStyle.Settle   => 0f,
        MoveStyle.Await    => 0f,
        MoveStyle.Wall     => 0f,
        _                  => 14f,
    };

    // ─── 着座後の縦の動き（自前の形を持つ種だけ）───
    // 戻り値は _baseY からのオフセット(px)。null を返した種は従来どおり _vy の等速往復に任せる。
    // ★どの形も有界（±26px 以内）。この後 28〜188px にクランプされるので盤面外へは出られない。
    // ★「止まる」種（Absent/Settle/Await/Wall）は 0 近傍に張り付く。止まっていても浄化はできるので
    //   進行不能にはならない（居座り＝倒すまで去らない は従来どおり不変）。
    private float? CampVerticalOverride(float t) => _move switch
    {
        // 数字の人：下から上へゆっくり登り、上限でぱっと下へ戻る（鋸波＝ランキングのスクロール）。
        // 装飾（ReiMetrics モーション）の視線スクロールと同じ周期感を、実際の位置でも出す。
        MoveStyle.Scroll => 22f - Mathf.PosMod(t * 0.30f, 1f) * 44f,
        // 切り抜きの人：段でカクッと上下に飛ぶ（コマ送り）。5段に量子化＝なめらかに動かない。
        MoveStyle.StepCut => Mathf.Round(Mathf.Sin(t * 0.85f) * 2.5f) / 2.5f * 24f,
        // 空席の人：ほとんど動かない。超低周波でわずかに沈むだけ＝「いない人」を静止で語る。
        MoveStyle.Absent => Mathf.Sin(t * 0.35f) * 3.5f,
        // こはるグッズの箱：重くて動けない。Absent より更に小さく。
        MoveStyle.Settle => Mathf.Sin(t * 0.45f) * 2.8f,
        // 未応答の残響：静止から、長周期で一度だけ小さく浮いてまた沈む（装飾の hope³ と同じ呼吸）。
        MoveStyle.Await => -Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 0.50f)), 3f) * 13f,
        // バズ壁：射線を塞ぎ続ける壁＝ほぼ不動。わずかな上下だけ残して「生きている」ことは示す。
        MoveStyle.Wall => Mathf.Sin(t * 0.30f) * 2.2f,
        // 記憶の残響：漂う。ゆっくり大きく、周期の違う2波で不定形に。
        MoveStyle.Drift => Mathf.Sin(t * 0.55f) * 15f + Mathf.Sin(t * 0.31f) * 8f,
        // 声援の人：弾む。上へ跳ねて落ちる（|sin| の非対称＝接地のリズム）。
        MoveStyle.Bounce => -Mathf.Abs(Mathf.Sin(t * 1.9f)) * 20f + 10f,
        // 荷物の人：沈んだまま僅かに漂う＋抱えきれない細かい震え。
        MoveStyle.Trudge => 5f + Mathf.Sin(t * 0.6f) * 4f + Mathf.Sin(t * 7.5f) * 0.8f,
        // 匿名の人：小刻みに上下し続ける（誰でもない揺らぎ）。
        MoveStyle.Jitter => Mathf.Sin(t * 3.7f) * 6f + Mathf.Sin(t * 9.1f) * 2.0f,
        _ => null,   // Hurry/Hesitant/Compare/Erase/Walk/Flutter/Patrol/Hover/Dash/Straight は _vy の往復
    };

    // ─── 着座後の横の癖（camp.X からのオフセット・px）───
    // 呼び出し側で ±CampDriftMax(9px) に必ずクランプされる＝どの種も camp.X の近傍を離れない。
    private float CampHorizontalDrift(float t) => _move switch
    {
        MoveStyle.Hurry    => Mathf.Sin(t * 4.2f) * 3.5f,   // せかせか前後に詰める
        MoveStyle.Hesitant => -Mathf.Pow(Mathf.Sin(t * 1.1f), 3f) * 8f, // 出しかけて引き戻す（非対称）
        MoveStyle.Compare  => Mathf.Sin(t * 1.5f) * 7f,     // 左右を見比べる（横がいちばん大きい）
        MoveStyle.Erase    => Mathf.Sin(t * 3.6f) * 6.5f,   // 横に擦る
        MoveStyle.Drift    => Mathf.Sin(t * 0.42f) * 7f,    // ゆっくり漂う
        MoveStyle.Jitter   => Mathf.Sin(t * 10.5f) * 1.6f,  // 細かく震える
        MoveStyle.StepCut  => Mathf.Round(Mathf.Sin(t * 1.3f) * 2f) / 2f * 5f, // 段で横にも飛ぶ
        MoveStyle.Walk     => Mathf.Sin(t * 2.8f) * 2.2f,   // 歩の重心移動
        MoveStyle.Flutter  => Mathf.Sin(t * 1.7f) * 4f,     // ひらひら
        MoveStyle.Hover    => Mathf.Sin(t * 0.9f) * 5f,     // ふわふわ浮遊
        MoveStyle.Patrol   => Mathf.Sin(t * 5.5f) * 1.2f,   // ホバリングの微ブレ
        MoveStyle.Trudge   => Mathf.Sin(t * 0.8f) * 2.5f,   // 重い横揺れ
        MoveStyle.Bounce   => Mathf.Sin(t * 0.95f) * 4.5f,  // 弾みに合わせて左右へ
        // 動かない種（Absent/Settle/Await/Wall）と Dash/Scroll/Straight は横に振れない
        //（Straight=0＝スキン流用種・Default の手触りを従来のまま残す）。
        _ => 0f,
    };

    // ─── 進入の速度プロファイル ───
    // 引数 p は進捗 0（出現）→1（着座直前）。返すのは Max(MoveSpeed, ApproachFloor) に掛ける倍率。
    // ★戻り値は必ず [ApproachSpeedMin(0.62), ApproachSpeedMax(1.85)] にクランプし、さらに呼び出し側で
    //   ApproachCeil(58) の頭打ちを掛ける＝実効速度は 28.5〜58px/s の範囲を絶対に出ない。
    //   0 にも負にもならないので残距離は毎フレーム必ず減る＝どの種も有限時間
    //   （最悪でも盤面対角 460px ÷ 28.5 ≒ 16.1秒。横オフセットの前進ロス 0.83 を見ても約 19秒）で着座する。
    //   実際の進入距離は 150〜280px なので 3〜10秒＝従来（2〜5秒）より一拍遅いが渋滞はしない。
    //   これが「進入が必ず終わる」の保証。ApproachFloor(46) の意図（＝撃つ前に死なせない）も維持する。
    private float ApproachSpeedMul(float p)
    {
        float t = (float)_moveT + _movePhase;
        float v = _move switch
        {
            // 締切の人：最初から速い。着く直前（p>0.7）に更に詰める＝「間に合わせに走る」
            MoveStyle.Hurry => 1.45f + 0.40f * Mathf.SmoothStep(0.70f, 1f, p),
            // 送信取消の人：進んでは緩む。脈打つが下限 0.70 を割らない＝止まって見えても必ず前へ出ている
            MoveStyle.Hesitant => 1.00f + 0.30f * Mathf.Sin(t * 2.4f),
            // 空席の人：終始ゆっくり。着くほど更に緩む＝「来たがっていない」
            MoveStyle.Absent => 0.90f - 0.25f * p,
            // 比較の人：左右を見ながらなので進みは一定より遅め
            MoveStyle.Compare => 0.88f,
            // 声援の人：弾みながらなので前進は波打つ（弾む山で速く、谷で緩む）
            MoveStyle.Bounce => 1.10f + 0.25f * Mathf.Abs(Mathf.Sin(t * 3.1f)),
            // 荷物の人：重い。終始いちばん遅い（ただし下限は割らない）
            MoveStyle.Trudge => 0.70f,
            // 匿名の人：一定速。震えは横オフセット側で出す
            MoveStyle.Jitter => 1.05f,
            // 切り抜きの人：段で進む。保持中は遅く、切り替わりで速い＝コマ送りの前進
            MoveStyle.StepCut => 1.00f + 0.55f * Mathf.Abs(Mathf.Sin(t * 2.3f)),
            // 数字の人：淡々と一定速（数字は止まらない）
            MoveStyle.Scroll => 1.00f,
            // 消しゴムの残響：擦る往復に合わせて前進も僅かに脈打つ
            MoveStyle.Erase => 1.05f + 0.18f * Mathf.Sin(t * 4.4f),
            // 記憶の残響：漂う。ゆっくり、速度も揺れる
            MoveStyle.Drift => 0.80f + 0.15f * Mathf.Sin(t * 0.85f),
            // 未応答の残響：普通に入ってくる（待つのは着座後）
            MoveStyle.Await => 0.95f,
            // 基底6種
            MoveStyle.Patrol  => 1.20f,                                        // ドローン＝速めに配置につく
            MoveStyle.Hover   => 0.85f,                                        // 目＝ふわりと入る
            MoveStyle.Walk    => 1.00f + 0.22f * Mathf.Abs(Mathf.Sin(t * 3.4f)), // 机＝一歩ごとに進む
            MoveStyle.Flutter => 0.95f + 0.20f * Mathf.Sin(t * 2.0f),          // ノート＝ひらひら
            MoveStyle.Dash    => 1.55f,                                        // ペンライト＝まっすぐ速い
            MoveStyle.Settle  => 0.78f,                                        // 箱＝遅い
            MoveStyle.Wall    => 0.72f,                                        // 壁＝のそのそ（圧を先に見せる）
            _ => 1f,                                                            // Straight＝従来どおり等速
        };
        return Mathf.Clamp(v, ApproachSpeedMin, ApproachSpeedMax);
    }

    // ─── 進入の軌道（進行方向に対する横オフセット・px）───
    // ★必ず p→1 で 0 に収束させる（fade を掛ける）。＝着座点に着く頃には横ズレが無くなり、
    //   目標点は動かないまま「膨らんで入ってきた」という画だけが残る。
    // ★絶対値は ArcMaxOffset(26px) を超えない＝“ひと膨らみ”までで、折り返して自機を追い回さない
    //   （この作品の「進入経路は丸見え＝読める圧」を守る）。
    private float LateralOffset(float p)
    {
        // 収束の窓：p=0 で 0 → p=0.35 付近で最大 → p=1 で 0。sin(πp) は両端がきれいに 0 になる。
        float fade = Mathf.Sin(p * Mathf.Pi);
        float t = (float)_moveT + _movePhase;
        float v = _move switch
        {
            // 比較の人：左右に大きく膨らんで入る＝「どちらを見るか迷いながら来る」（最大の弧）
            MoveStyle.Compare => _arcSign * 24f * fade,
            // 声援の人：弾みながら＝進行方向に対して上下に波打つ
            MoveStyle.Bounce => Mathf.Sin(t * 3.1f) * 14f * fade,
            // 送信取消の人：出しかけて引き戻す＝ゆっくりした片側の膨らみ
            MoveStyle.Hesitant => _arcSign * Mathf.Sin(t * 1.25f) * 12f * fade,
            // 匿名の人：細かい震え（周期の違う2波＝非周期に見せる）。振幅は小さく、読みを妨げない
            MoveStyle.Jitter => (Mathf.Sin(t * 12.0f) * 2.2f + Mathf.Sin(t * 4.7f) * 3.4f) * fade,
            // 切り抜きの人：段でカクッと横へ飛ぶ（量子化）＝コマ送りの軌道
            MoveStyle.StepCut => Mathf.Round(Mathf.Sin(t * 2.3f) * 3f) / 3f * 13f * fade,
            // 消しゴムの残響：横に擦るような速い往復
            MoveStyle.Erase => Mathf.Sin(t * 4.4f) * 10f * fade,
            // 記憶の残響：ゆっくり大きく蛇行＝漂って入ってくる（最大の“ふらつき”）
            MoveStyle.Drift => (Mathf.Sin(t * 0.85f) * 16f + Mathf.Sin(t * 0.47f) * 8f) * fade,
            // 机：一歩ごとの上下（歩く）。ノート：ひらひら舞う
            MoveStyle.Walk    => Mathf.Abs(Mathf.Sin(t * 3.4f)) * 5f * fade,
            MoveStyle.Flutter => Mathf.Sin(t * 2.0f) * 11f * fade,
            // ドローン：ホバリングの小刻みなブレ。目：ふわりと片側へ膨らむ
            MoveStyle.Patrol => Mathf.Sin(t * 6.5f) * 3.2f * fade,
            MoveStyle.Hover  => _arcSign * Mathf.Sin(t * 1.4f) * 13f * fade,
            // 荷物の人：重さでゆっくり左右に振れる
            MoveStyle.Trudge => Mathf.Sin(t * 1.1f) * 6f * fade,
            // まっすぐ来る種（Hurry/Absent/Scroll/Await/Dash/Settle/Wall/Straight）は 0＝完全な直進。
            // 全種を曲げない＝「直線で来る敵」が居るから曲がる敵が際立つ（§3 緩急・読みの基準線）。
            _ => 0f,
        };
        return Mathf.Clamp(v, -ArcMaxOffset, ArcMaxOffset);
    }

    // モチーフ別の動きの型を弾幕パターンから引く（種が動きでも見分けられるように1:1）。
    private static LivingMotion MotionFor(AttackPattern p) => p switch
    {
        AttackPattern.ReiLockBurst => LivingMotion.ReiDrone,   // 偵察ドローン
        AttackPattern.ReiPulseRing => LivingMotion.ReiEye,     // 監視カメラの目
        AttackPattern.AkariScatter => LivingMotion.AkariDesk,  // 机・椅子
        AttackPattern.AkariDrop    => LivingMotion.AkariNote,  // ノート・教科書
        AttackPattern.KoharuSharp3 => LivingMotion.KoharuPenlight, // ペンライト
        AttackPattern.KoharuSimmer => LivingMotion.KoharuBox,  // グッズの箱
        // ─── 人型12種（2026-09-17 個別化）───
        AttackPattern.AkariDeadline => LivingMotion.AkDeadline,
        AttackPattern.AkariUnsent   => LivingMotion.AkUnsent,
        AttackPattern.AkariVacant   => LivingMotion.AkVacant,
        AttackPattern.KoharuComparison => LivingMotion.KoComparison,
        AttackPattern.KoharuCheer      => LivingMotion.KoCheer,
        AttackPattern.KoharuParcel     => LivingMotion.KoParcel,
        AttackPattern.ReiAnonymous => LivingMotion.ReiAnon,
        AttackPattern.ReiClipper   => LivingMotion.ReiClip,
        AttackPattern.ReiMetrics   => LivingMotion.ReiMetrics,
        AttackPattern.MinaEraser      => LivingMotion.MinaEraserM,
        AttackPattern.MinaMemory      => LivingMotion.MinaMemoryM,
        AttackPattern.MinaUnanswered  => LivingMotion.MinaUnansweredM,
        _ => LivingMotion.None,                                 // Default/アンチくん・スキン流用種：素のまま
    };

    // パターンごとの弾形・色を確定（spec 仕様の弾形・色）。
    private void ApplySpellVisual()
    {
        switch (_spec.Pattern)
        {
            case AttackPattern.ReiLockBurst:
                SetSpellVisual(BulletShape.Diamond, new Color(0.85f, 0.55f, 0.80f), BulletArt.Get("rei_subscriber"), 12f); break;
            case AttackPattern.ReiPulseRing:
                SetSpellVisual(BulletShape.Ring, new Color(0.80f, 0.45f, 0.62f), BulletArt.Get("rei_comment"), -16f); break;
            case AttackPattern.AkariScatter:
                SetSpellVisual(BulletShape.Orb, EnemyKegare, BulletArt.AkariDocs, 24f); break;
            case AttackPattern.AkariDrop:
                SetSpellVisual(BulletShape.Rice, new Color(0.72f, 0.62f, 0.85f), BulletArt.AkariEnvelope, -18f); break;
            case AttackPattern.KoharuSharp3:
                SetSpellVisual(BulletShape.Needle, new Color(0.95f, 0.50f, 0.70f), BulletArt.KoharuPenlight, 16f); break;
            case AttackPattern.KoharuSimmer:
                SetSpellVisual(BulletShape.Orb, new Color(0.88f, 0.55f, 0.45f), BulletArt.KoharuAcrylic, -20f); break;
            case AttackPattern.DefaultAim:
                SetSpellVisual(BulletShape.Orb, EnemyKegare, BulletArt.Get("enemy_rei_anonymous"), -12f); break;
            case AttackPattern.FlankAim:
                var flankArt = _spec.PreTexPath switch
                {
                    "res://char/v3/enemy_akari_desk_pre.png" => BulletArt.AkariDocs,
                    "res://char/v3/enemy_koharu_penlight_pre.png" => BulletArt.KoharuPenlight,
                    "res://char/v3/enemy_rei_icon_pre.png" => BulletArt.Get("rei_subscriber"),
                    _ => BulletArt.Get("enemy_rei_anonymous"),
                };
                SetSpellVisual(BulletShape.Orb, EnemyKegare, flankArt, 12f); break;
            case AttackPattern.KoharuPrayerCarry:
                SetSpellVisual(BulletShape.Orb, new Color(0.98f, 0.82f, 0.55f), BulletArt.KoharuTicket, 15f); break;
            case AttackPattern.AkariDeadline:
                SetSpellVisual(BulletShape.Orb, new Color("ef8095"), BulletArt.Get("enemy_akari_deadline"), 24f); break;
            case AttackPattern.AkariUnsent:
                SetSpellVisual(BulletShape.Rice, new Color("f2b7c8"), BulletArt.Get("enemy_akari_unsent"), -18f); break;
            case AttackPattern.AkariVacant:
                SetSpellVisual(BulletShape.Diamond, new Color("a6d9ee"), BulletArt.Get("enemy_akari_vacant"), 12f); break;
            case AttackPattern.KoharuComparison:
                SetSpellVisual(BulletShape.Rice, new Color("f29baf"), BulletArt.Get("enemy_koharu_comparison"), -28f); break;
            case AttackPattern.KoharuCheer:
                SetSpellVisual(BulletShape.Star, new Color("d1b3f5"), BulletArt.Get("enemy_koharu_cheer"), 32f); break;
            case AttackPattern.KoharuParcel:
                SetSpellVisual(BulletShape.Diamond, new Color("efcbb2"), BulletArt.Get("enemy_koharu_parcel"), 15f); break;
            case AttackPattern.ReiAnonymous:
                SetSpellVisual(BulletShape.Diamond, new Color("f2a2b9"), BulletArt.Get("enemy_rei_anonymous"), -12f); break;
            case AttackPattern.ReiClipper:
                SetSpellVisual(BulletShape.Rice, new Color("d3c2f3"), BulletArt.Get("enemy_rei_clipper"), 38f); break;
            case AttackPattern.ReiMetrics:
                SetSpellVisual(BulletShape.Diamond, new Color("f2d480"), BulletArt.Get("enemy_rei_metrics"), 0f); break;
            case AttackPattern.MinaEraser:
                SetSpellVisual(BulletShape.Needle, new Color(0.80f, 0.74f, 0.94f), BulletArt.Get("mina_eraser"), 12f); break;
            case AttackPattern.MinaMemory:
                SetSpellVisual(BulletShape.Ring, new Color(0.62f, 0.50f, 0.82f), BulletArt.Get("mina_memory"), -16f); break;
            case AttackPattern.MinaUnanswered:
                SetSpellVisual(BulletShape.Orb, new Color(0.78f, 0.62f, 0.90f), BulletArt.Get("mina_unanswered"), 10f); break;
        }
    }
    // 既定の穢れ色（Bullet.EnemyMid #e072ac 相当）。Orb 種はこれで撒く。
    private static readonly Color EnemyKegare = new Color(0.882f, 0.447f, 0.675f);

    // 左へ進入 → _campX に着いたら居座る。倒すまで画面外へ出ない（攻撃を当てる時間を確保）。
    // SwayAmp>0 の種は居座り中の上下動に“うねり”を載せて種ごとの差を残す。
    protected override void UpdateMovement(double delta)
    {
        // 基準Y（うねりの中心）は最初の移動フレームで確定（GlobalPosition は AddChild 後に設定されるため）。
        if (!_baseYSet) { _baseY = GlobalPosition.Y; _baseYSet = true; }

        // 進入中も居座り中も“生きてる感”は出す（純・装飾＝当たり判定/進行は不変）。
        TickLivingMotion(delta);

        // 「記憶の残響」だけ、通った道を記録する（撃つときに“居た場所”へ弾を置くため）。
        if (_spec.Pattern == AttackPattern.MinaMemory)
        {
            _trailT += delta;
            if (_trailT >= TrailStep)
            {
                _trailT = 0;
                _trail[_trailHead] = GlobalPosition;
                _trailHead = (_trailHead + 1) % TrailLen;
                if (_trailCount < TrailLen) _trailCount++;
            }
        }

        float dt = (float)delta;

        // 祈り運び（こはる面専用）：居座らず左へ横断するボーナス種。祈り弾をぶら下げて運ぶだけで撃たない。
        // 左端へ抜けると基底(_PhysicsProcess)の X<-24 で退場＝撃ち漏らし（祈り弾は _ExitTree が置き去りにしない）。
        if (_spec.Pattern == AttackPattern.KoharuPrayerCarry)
        {
            TickPrayerCarry(delta, dt);
            return;
        }

        // 居座る目標点。未指定なら従来どおり「右→_campX へ左進」（=同Yへ水平移動）。
        Vector2 camp = _entryConfigured ? _campTarget : new Vector2(_campX, _baseY);

        // 進入：目標点へ直進。回り込み（FlankAim）はまず経由点（走行レーン終端）へ、通過後に着座点へ折れる。
        // 撃てるかどうかは _camped ではなく CanFire（下の「進入撃ち」ゲート）が決める。
        if (!_camped)
        {
            // 画面内に入っている間だけ滞在時間を積む＝画面外からの理不尽撃ちは従来どおり起きない。
            if (OnScreen) _visibleT += delta;

            Vector2 goal = _viaConfigured ? _viaTarget : camp;
            Vector2 to = goal - GlobalPosition;
            float dist = to.Length();
            // 進入開始時の距離を1回だけ覚える（進捗 p の分母）。経由点通過で区間が変わったら取り直す。
            if (_approachTotal <= 0f) _approachTotal = Mathf.Max(dist, 1f);
            if (dist > 3f)
            {
                // 進捗 p：0（出現時）→1（着座直前）。軌道の演出はすべてこの p で駆動し、p→1 で必ず収束させる。
                float p = Mathf.Clamp(1f - dist / _approachTotal, 0f, 1f);
                _moveT += delta;

                // 進入だけ最低速度を保証＝遅い種でも素早く居座って攻撃に移れる（従来の保証）。
                // そこへ種ごとの速度倍率を掛け、最後に ApproachCeil で頭を押さえる。
                // ＝実効速度は常に 28.5〜58px/s。0 や負にはならないので必ず着座し、
                //   58 < 66（強化なし・最遅ジョブの自機）なので**どの種にも必ず振り切れる**。
                float approach = Mathf.Min(
                    Mathf.Max(_spec.MoveSpeed, ApproachFloor) * ApproachSpeedMul(p), ApproachCeil);
                Vector2 fwd = to / dist;   // 目標点への単位ベクトル（正規化を1回に）
                float fwdStep = approach * dt;   // このフレームに許される移動量（＝速度の予算）

                // 軌道の演出：進行方向に対する法線へ、p で 0 に収束する横オフセットを“差分で”加える。
                // ★位置を直接置き換えず「前フレームとの差分」だけ足すので、目標点は動かない＝
                //   前進成分がそのまま残距離を減らし続ける（着座の保証を侵さない）。
                float lat = LateralOffset(p);
                float step = lat - _lastLateral;
                // ★【着座保証の要】横へ動かす量を、そのフレームの前進量の LateralStepRatio 倍までに制限する。
                //   法線は毎フレーム向きが変わるので、横の差分が前進量より大きいと理屈上は
                //   「横に泳いで残距離が減らない」個体が作れてしまう（＝進入が終わらない＝渋滞）。
                //   前進量の 0.55 倍までに抑えれば、合成移動の前進成分は必ず正のまま
                //   （下の正規化で縮めても前進:横の比 1:0.55 は保たれる＝前進係数 1/√(1+0.55²) ≒ 0.872）。
                //   ＝実効前進は最低でも 28.5×0.872 ≒ 24.9px/s、最速でも 58×0.872 ≒ 50.6px/s。
                //   最悪ケース（最遅種＋常に横いっぱい）で盤面対角 460px を約 18.5秒、
                //   実際の進入距離 150〜280px なら 6〜11秒で必ず着座する（進入は有限時間で必ず終わる）。
                step = Mathf.Clamp(step, -fwdStep * LateralStepRatio, fwdStep * LateralStepRatio);
                Vector2 normal = new Vector2(-fwd.Y, fwd.X);

                // ★【速度上限の要・2026-09-22 修正】前進と横を足した**合成ベクトルの長さ**で上限を掛ける。
                //   以前は前進成分だけに ApproachCeil を掛けていたため、横に膨らむ種
                //   （Compare/Bounce/Hesitant/Jitter/StepCut/Erase/Drift/Walk/Flutter/Patrol/Hover/Trudge
                //    ＝全 MoveStyle の過半）の実効速度が 58×√(1+0.55²) ≒ 66.2px/s まで伸び、
                //   最遅ジョブ＝結び手(75×0.88=66px/s)と同速〜わずかに上だった＝**振り切れない**。
                //   ここで合成長を fwdStep に丸めれば、横の有無にかかわらず実効速度は必ず
                //   ApproachCeil(58) 以下＝「毎秒 8px ずつ必ず引き離せる」が全種で成立する。
                //   丸めは前進と横を**同じ係数で**縮めるので、種ごとの軌道の形（膨らみの比率）は変わらない。
                Vector2 delta2 = fwd * fwdStep + normal * step;
                float len = delta2.Length();
                if (len > fwdStep && len > 0.0001f)
                {
                    float shrink = fwdStep / len;
                    delta2 *= shrink;
                    step *= shrink;   // 実際に動かした横量に合わせる（_lastLateral と食い違わせない）
                }
                GlobalPosition += delta2;
                _lastLateral += step;   // 実際に動かした量だけ記録（クランプ後の値と食い違わせない）
                // 盤面の上下へはみ出して見失われないよう、進入中も縦だけ緩く締める
                //（横は出現エッジの外側から入るので締めない）。
                var gp = GlobalPosition;
                GlobalPosition = new Vector2(gp.X, Mathf.Clamp(gp.Y, Field.Top - 12f, Field.Bottom + 12f));
                // 進入撃ち：ゲートが開いていれば歩きながら撃つ（＝射線上を無防備に歩く空白を潰す）。
                // 回り込み（FlankAim）だけは走行中に撃たない仕様を維持＝背後へ回る経路を読ませる。
                if (CanFire)
                {
                    // ゲートが開いた最初のフレームだけ、初弾が FirstShotDelay 秒後に来るようプライムする。
                    if (!_approachFired)
                    {
                        _approachFired = true;
                        _fireT = Mathf.Max(0.0, Di(BaseInterval()) - FirstShotDelay);
                    }
                    TickFire(delta);
                }
                return;
            }
            if (_viaConfigured)
            {
                // 経由点通過→次フレームから着座点へ。区間が変わるので進捗の分母と横オフセットを取り直す
                //（取り直さないと p が 1 のまま＝2区間目で fade が 0 になり軌道演出が死ぬ／
                //  _lastLateral の残差が次区間の頭で一気に効いて瞬間移動して見える）。
                _viaConfigured = false;
                _approachTotal = 0f;
                _lastLateral = 0f;
                return;
            }
            _camped = true;   // 居座り開始
            _baseY = camp.Y;  // 以降の上下往復の中心
            // 着座 X を確定。以降の横揺れはこの値の周囲 ±CampDriftMax でのみ振れる
            //（camp.X をそのまま使う＝左湧きの着座保証 x=184..224 を動かさない）。
            _campBaseX = camp.X;
            _lastLateral = 0f;
            // 居座った瞬間に初弾を素早く（FirstShotDelay 秒後）。出現→即浄化でも一矢報いるように。
            // 進入中に既に撃ち始めていた個体は、そのタイマーを引き継ぐ（居座りで撃ち直しにならない）。
            if (!_approachFired)
                _fireT = Mathf.Max(0.0, Di(BaseInterval()) - FirstShotDelay);
        }

        // ─── 居座り（2026-09-17 種ごとに差別化）───
        // 従来は「camp.X に固定＋全種 ±14px/s の上下往復＋SwayAmp の有無」だけだった。
        // 縦は種ごとの速さ・形（往復／鋸波／段送り／静止）に、横は camp.X の周囲 ±9px の小さな揺れに割る。
        // ★横を大きく動かさない理由：ザコは「どこに居るか」が安定しているほど狙って撃ちやすい。
        //   横に泳ぐと当てる手応え（§3 手触り）が落ち、左湧きの着座保証（x=184..224）も崩れる。
        //   性格は縦の動き方と“わずかな”横の癖で十分に読み分けられる。
        _campPatrolT += delta;
        float pt = (float)_campPatrolT + _movePhase;

        // 縦：自前の形を持つ種（鋸波・段送り・静止からの浮き）はそこで ny を決め、
        // それ以外は従来どおり _vy の往復（速度だけ種ごと＝CampPatrolSpeed）。
        float ny;
        float? custom = CampVerticalOverride(pt);
        if (custom.HasValue)
        {
            ny = _baseY + custom.Value;
        }
        else
        {
            ny = GlobalPosition.Y + _vy * dt;
            if (ny < 28f || ny > 188f) { _vy = -_vy; ny = Mathf.Clamp(ny, 28f, 188f); }
        }
        // SwayAmp>0 の種は往復に小さなうねりを重ねて単調さを消す（従来どおり）。
        if (_spec.SwayAmp > 0f)
        {
            _swayT += delta;
            ny += Mathf.Sin((float)_swayT * _spec.SwayFreq) * (_spec.SwayAmp * 0.4f);
        }
        ny = Mathf.Clamp(ny, 28f, 188f);   // 縦は必ず盤面内（画面外へ出て見失われない）

        // 横：camp.X の周囲だけを振れる小さな癖。二重にクランプして盤面外・自機側へは絶対に届かせない。
        float nx = _campBaseX + Mathf.Clamp(CampHorizontalDrift(pt), -CampDriftMax, CampDriftMax);
        nx = Mathf.Clamp(nx, Field.Left + CampMarginX, Field.Right - CampMarginX);
        GlobalPosition = new Vector2(nx, ny);

        // 居座っている間だけ固有弾幕を駆動。会話中(BubblePaused)は Enemy._PhysicsProcess が
        // UpdateMovement を呼ばないため、ここに来る時点で攻撃してよい状態。
        TickFire(delta);
    }

    // ─── 固有弾幕の駆動 ───
    private void TickFire(double delta)
    {
        if (_spec.Pattern == AttackPattern.None) return;

        // 予告中：照準/合図を出して撃たずに待つ。
        if (_telegraphT > 0)
        {
            _telegraphT -= delta;
            if (_telegraphT <= 0) FireAfterTelegraph();
            return;
        }

        if (_salvoRemaining > 0)
        {
            _salvoT -= delta;
            if (_salvoT <= 0) FireCharacterSalvo();
            return;
        }

        // ロックオン連射のバースト消化中（予告後の3連）。
        if (_burstLeft > 0)
        {
            _burstT -= delta;
            if (_burstT <= 0)
            {
                FireBurstShot();
                _burstLeft--;
                _burstT = 0.08; // バースト内 0.08s 間隔
            }
            return;
        }

        _fireT += delta;
        if (_fireT < Di(BaseInterval())) return; // 基準間隔（初弾は居座り時にプライム済み）
        _fireT = 0;
        switch (_spec.Pattern)
        {
            case AttackPattern.ReiLockBurst:  BeginLockBurst();  break;
            case AttackPattern.ReiPulseRing:  FirePulseRing();   break;
            case AttackPattern.AkariScatter:  FireScatter();     break;
            case AttackPattern.AkariDrop:     FireDrop();        break;
            case AttackPattern.KoharuSharp3:  BeginSharp3();     break;
            case AttackPattern.KoharuSimmer:  FireSimmer();      break;
            case AttackPattern.DefaultAim:    FireDefaultAim();  break;
            case AttackPattern.FlankAim:      FireFlank();       break;
            case AttackPattern.AkariDeadline:
            case AttackPattern.AkariUnsent:
            case AttackPattern.AkariVacant:
            case AttackPattern.KoharuComparison:
            case AttackPattern.KoharuCheer:
            case AttackPattern.KoharuParcel:
            case AttackPattern.ReiAnonymous:
            case AttackPattern.ReiClipper:
            case AttackPattern.ReiMetrics:
            case AttackPattern.MinaEraser:
            case AttackPattern.MinaMemory:
            case AttackPattern.MinaUnanswered:
                BeginCharacterAttack(); break;
        }
    }

    private BulletPool? Pool => GetNodeOrNull<BulletPool>("/root/Pool");

    // 自機への向き（居なければ左向き）。
    private Vector2 AimDir()
    {
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Node2D pl)
        {
            var d = pl.GlobalPosition - GlobalPosition;
            if (d.LengthSquared() > 0.01f) return d.Normalized();
        }
        return new Vector2(-1, 0);
    }

    private static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = Mathf.DegToRad(deg), cs = Mathf.Cos(r), sn = Mathf.Sin(r);
        return new Vector2(v.X * cs - v.Y * sn, v.X * sn + v.Y * cs);
    }

    // ── レイ shooter：ロックオンビーム ──
    // 自機方向へ予測線（予兆＝薄い危険色ライン・当たり判定なし）を BeamWarn 秒出してから、
    // 実体ビーム（着弾フレームのみ判定ON）を撃つ。予兆中の自機方向で固定＝避けられる必殺。
    // 予兆／実体／被弾は AreaStrike（ボスの範囲技と共用の予測攻撃基盤）に一任する。
    private const double BeamWarn = 0.5;       // 予兆時間の基準秒（Di で難易度伸縮：易しいほど猶予増）。
    private const float BeamLen = 240f;        // 画面を貫く長さ（はみ出しは画面外で見えないだけ）
    private const float BeamHalfThick = 5.5f;  // ビームの半太さ(px)。細長＝判定は線分への最短距離で取る。
    private void BeginLockBurst()
    {
        var dir = AimDir();
        double warn = Di(BeamWarn); // 予兆時間（難易度で伸縮＝易しいほど避ける猶予が増える）。
        var z = new AreaStrike();
        // 予測線/着弾色はボス・レイの予測攻撃（AreaSpellCaster "rei"）と同じ金/明金パレットに統一。
        // ＝道中ドローンの予兆もボス予兆と同系統に見え、危険色の意味（金＝レイの裁き）が一貫する。
        z.ConfigureBeam(dir, BeamLen, BeamHalfThick, warn,
            new Color("e8c45a"), new Color("ffe39a"));
        // World（親）へぶら下げて発射源の Transform に依存させない。ただし発生源（このドローン）を
        // owner に渡し、予兆中に倒されたら予測線ごとキャンセルさせる（倒せば攻撃も消える＝理不尽回避）。
        var host = GetParent() ?? this;
        host.AddChild(z);
        z.SetOwner(this);
        z.GlobalPosition = GlobalPosition; // 発射源＝この瞬間のドローン位置（以降は固定＝避けられる）。
        SquishBody(); // 撃つ前の溜め（縦スカッシュ）で“来る”を本体でも示す。
    }
    private void FireAfterTelegraph()
    {
        if (_spec.Pattern == AttackPattern.KoharuSharp3)
        {
            FireSharp3();
        }
        else if (_salvoRemaining > 0) FireCharacterSalvo();
    }

    private void BeginCharacterAttack()
    {
        _characterOrigin = GlobalPosition;
        if (_spec.Pattern is AttackPattern.KoharuParcel or AttackPattern.ReiClipper)
            _characterOrigin.Y = Mathf.Clamp(_characterOrigin.Y, Field.Top + 40f, Field.Bottom - 40f);
        _burstDir = AimDir();
        _salvoIndex = 0;
        _salvoRemaining = _spec.Pattern switch
        {
            AttackPattern.AkariUnsent or AttackPattern.KoharuCheer => 2,
            AttackPattern.KoharuParcel or AttackPattern.ReiAnonymous => 3,
            AttackPattern.MinaEraser => 2,       // 消し線を2度なぞる
            AttackPattern.MinaMemory => 3,       // 通った道に3つ痕を残す
            _ => 1,
        };
        // 予備動作（溜め）の長さを種ごとに変える（2026-09-17 差別化）。
        //   以前は全12種が Max(0.45, Di(0.6)) で完全に同一だった＝「どの人が撃つか」で身構え方が変わらない。
        //   ★方針は堅持：予告そのものは全種に必ず出す（理不尽にしない／§7）。変えるのは“溜めの質”だけ。
        //     速い・軽い弾 → 短く鋭い（0.34s）／重い・避けにくい弾 → 長く沈む（0.78s）。
        //     長い溜めは「来るのが分かるが、避け場を作る時間も要る」攻撃に割り当てる＝読み合いの緩急（§3）。
        //   下限 0.30s は「見てから動ける」最低線（自機 150px/s＝約45px 動ける）。Di で難易度が更に伸ばす。
        double warn = _spec.Pattern switch
        {
            AttackPattern.ReiAnonymous => 0.34,      // 100px/s の3連射。速い相手ほど短く鋭く
            AttackPattern.KoharuComparison => 0.42,
            AttackPattern.ReiClipper => 0.46,
            AttackPattern.AkariUnsent => 0.50,
            AttackPattern.MinaEraser => 0.52,
            AttackPattern.KoharuCheer => 0.58,       // 一度に6発＝広げるぶん構える間をやる
            AttackPattern.MinaUnanswered => 0.60,
            AttackPattern.ReiMetrics => 0.62,        // 上から降る＝落下位置を読む時間
            AttackPattern.KoharuParcel => 0.66,      // 置き弾。避け場を選び直す間
            AttackPattern.MinaMemory => 0.70,        // 通った道に落ちる＝どこが埋まるか見せてから
            AttackPattern.AkariVacant => 0.74,       // 縦の壁。隙間を探して移動しきる時間が要る
            AttackPattern.AkariDeadline => 0.78,     // 加速弾＝発進後が速い。そのぶん溜めを最長に
            _ => 0.60,
        };
        _telegraphT = Mathf.Max(0.30, Di(warn));
        // 溜めの「深さ」も質に合わせる（長く沈む攻撃ほど深く潰れる）。_kick は TickLivingMotion が減衰させる。
        _kick = Mathf.Max(_kick, (float)Mathf.Clamp(warn / 0.6, 0.6, 1.4));
        // 記憶の残響だけは「どこへ置くか」を予告の時点で確定して焼く（以降 _trail が伸びても動かさない）。
        if (_spec.Pattern == AttackPattern.MinaMemory)
        {
            _memoryTraceCount = 0;
            foreach (var p in CharacterShotOrigins())
            {
                if (_memoryTraceCount >= MemoryTraceCount) break;
                _memoryTraces[_memoryTraceCount++] = p;
            }
            _salvoRemaining = Mathf.Max(1, _memoryTraceCount);
        }
        FxLayer.Instance?.AimFlash(_characterOrigin, CurTint);
        foreach (var origin in MemoryOrigins())
            if (origin != _characterOrigin) FxLayer.Instance?.AimFlash(origin, CurTint);
    }

    private System.Collections.Generic.IEnumerable<Vector2> CharacterShotOrigins()
    {
        switch (_spec.Pattern)
        {
            case AttackPattern.AkariVacant:
                int slots = Mathf.Max(3, Dn(5));
                int gap = 1 + _characterAttackIndex % (slots - 2);
                float spacing = Mathf.Max(16f, 96f / (slots - 1));
                float halfSpan = spacing * (slots - 1) * 0.5f;
                float center = Mathf.Clamp(_characterOrigin.Y, Field.Top + halfSpan + 12f, Field.Bottom - halfSpan - 12f);
                for (int i = 0; i < slots; i++)
                    if (i != gap)
                        yield return new Vector2(_characterOrigin.X, center + (i - (slots - 1) * 0.5f) * spacing);
                break;
            case AttackPattern.ReiClipper:
                yield return _characterOrigin + new Vector2(0, -24);
                yield return _characterOrigin + new Vector2(0, 24);
                break;
            case AttackPattern.ReiMetrics:
                int count = Mathf.Max(2, Dn(4));
                float x = Mathf.Clamp(_characterOrigin.X, Field.Left + 52f, Field.Right - 52f);
                for (int i = 0; i < count; i++)
                    yield return new Vector2(x + Mathf.Lerp(-40f, 40f, i / (float)(count - 1)),
                        Mathf.Max(Field.Top + 16f, _characterOrigin.Y - 24f));
                break;
            // 記憶の残響：自機ではなく「自分が通った道」へ置きに行く。古い足跡ほど遠い点を選ぶ。
            //   予告(AimFlash)はこの全点に出る＝「どこが埋まるか」を撃つ前に必ず見せる（§7 理不尽の排除）。
            //   足跡がまだ溜まっていない出現直後は現在地へ落とす＝必ず1点は返す（空にしない）。
            case AttackPattern.MinaMemory:
                if (_trailCount == 0) { yield return _characterOrigin; break; }
                // _trailHead-1 が最新。3点ぶん遡るごとに1点拾う（＝約1秒間隔の足跡を3つ）。
                // 足跡がまだ浅い出現直後は、拾える範囲だけを返す＝同じ点へ重ねて撃たない
                //（重なった弾は見た目が1発なのに判定が濃くなる＝視認性と判定の食い違いになる）。
                for (int i = 0; i < MemoryTraceCount; i++)
                {
                    int back = 1 + i * 3;
                    if (back > _trailCount) break;   // そこまで遡れる足跡が無い＝ここで打ち切る
                    var p = _trail[((_trailHead - back) % TrailLen + TrailLen) % TrailLen];
                    yield return new Vector2(
                        Mathf.Clamp(p.X, Field.Left + 16f, Field.Right - 16f),
                        Mathf.Clamp(p.Y, Field.Top + 16f, Field.Bottom - 16f));
                }
                break;
            default:
                yield return _characterOrigin;
                break;
        }
    }
    // 記憶の残響が一度に置く痕の数。斉射3回×1点＝合計3点が盤面に残る（_salvoRemaining=3 と対）。
    private const int MemoryTraceCount = 3;
    // 予告時に確定した置き場所。斉射の間も本体は動き続ける（_trail が伸びる）ので、
    // 予告で光った点と実際に弾が出る点がズレないよう、ここへ焼いてから使う（§7 予告は必ず守る）。
    private readonly Vector2[] _memoryTraces = new Vector2[MemoryTraceCount];
    private int _memoryTraceCount;

    // 予告フラッシュを出す点。記憶の残響だけ「焼いた点」を返し、他種は従来どおり CharacterShotOrigins。
    private System.Collections.Generic.IEnumerable<Vector2> MemoryOrigins()
    {
        if (_spec.Pattern != AttackPattern.MinaMemory) return CharacterShotOrigins();
        return System.Linq.Enumerable.Take(_memoryTraces, _memoryTraceCount);
    }

    private void CharacterFan(BulletPool pool, Vector2 origin, Vector2 dir, int count,
        float spread, float speed, float radius = 3.8f, bool accelerate = false)
    {
        int n = Mathf.Max(1, Dn(count));
        for (int i = 0; i < n; i++)
        {
            float angle = n == 1 ? 0 : Mathf.Lerp(-spread, spread, i / (float)(n - 1));
            var bullet = FireBullet(pool, origin, Rotate(dir, angle) * speed, radius);
            if (accelerate)
            {
                // MakeAccel replaces velocity, so preserve the pool's difficulty speed multiplier.
                float mul = bullet.Velocity.Length() / speed;
                bullet.MakeAccel(10f * mul, speed * mul, (float)Di(0.55));
            }
        }
    }

    private void FireCharacterSalvo()
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        switch (_spec.Pattern)
        {
            case AttackPattern.AkariDeadline:
                CharacterFan(pool, _characterOrigin, _burstDir, 3, 14f, 116f, accelerate: true);
                break;
            case AttackPattern.AkariUnsent:
                CharacterFan(pool, _characterOrigin, Rotate(_burstDir, _salvoIndex == 0 ? -16f : 16f), 2, 7f, 64f);
                break;
            case AttackPattern.AkariVacant:
                foreach (var origin in CharacterShotOrigins())
                    FireBullet(pool, origin, Vector2.Left * 52f, 3.8f);
                break;
            case AttackPattern.KoharuComparison:
                CharacterFan(pool, _characterOrigin, Rotate(Vector2.Left, _characterAttackIndex % 2 == 0 ? -22f : 22f), 3, 16f, 84f);
                break;
            case AttackPattern.KoharuCheer:
                CharacterFan(pool, _characterOrigin, _burstDir, 3, _salvoIndex == 0 ? 10f : 34f, 72f);
                break;
            case AttackPattern.KoharuParcel:
                int parcels = Mathf.Max(1, Dn(2));
                for (int i = 0; i < parcels; i++)
                    FireBullet(pool, _characterOrigin + new Vector2(0, (i - (parcels - 1) * 0.5f) * 22f + (_salvoIndex % 2) * 10f),
                        Vector2.Left * 38f, 4f);
                break;
            case AttackPattern.ReiAnonymous:
                CharacterFan(pool, _characterOrigin, _burstDir, 1, 5f, 100f, 3.6f);
                break;
            case AttackPattern.ReiClipper:
                foreach (var origin in CharacterShotOrigins())
                    CharacterFan(pool, origin, Rotate(Vector2.Left, origin.Y < _characterOrigin.Y ? -24f : 24f), 2, 4f, 85f);
                break;
            case AttackPattern.ReiMetrics:
                foreach (var origin in CharacterShotOrigins())
                {
                    var bullet = FireBullet(pool, origin, Rotate(new Vector2(-0.3f, 1).Normalized(), _characterAttackIndex % 2 == 0 ? -12f : 12f) * 55f, 3.8f);
                    bullet.Rotation = 0f;
                }
                break;

            // ─── FINAL（ミナの内側）の残響3種（2026-09-17 固有パターン新設）───
            // 消しゴムの残響：予告方向へ「消し線」を1本ずつ、間を置いて2度なぞる。
            //   1本目は遅く（74px/s）＝見てから退ける。2本目は同じ線を速く（132px/s）なぞり直す＝
            //   「一度避けた線をもう一度消しに来る」。同じ角度なので避け場は変わらない＝理不尽にならない。
            //   自機狙いは予告時の _burstDir に固定（撃つ瞬間の追尾はしない）。
            case AttackPattern.MinaEraser:
                CharacterFan(pool, _characterOrigin, _burstDir, 1, 0f,
                    _salvoIndex == 0 ? 74f : 132f, 3.8f);
                break;

            // 記憶の残響：自機を狙わない。自分が通った道（_trail）へ、ゆっくり漂う痕を1つずつ置く。
            //   自機狙いでない＝「避ける」のでなく「そこを通らない」判断を迫る＝道中で唯一の陣取り型。
            //   速度 34px/s：盤面幅 264px を約8秒で渡る。間隔 3.4s×3点なので画面上の定常数は7発前後＝
            //   空席の人（52px/s×4発／4.4s）と同程度の密度に収まる。極低速（〜15px/s）にすると
            //   20秒残って盤面が痕で埋まり、視認性とテンポを壊す＝「残る」の演出はこの速度で足りる。
            case AttackPattern.MinaMemory:
                // 予告時にスナップショットした点をそのまま使う（予告と着弾のズレを作らない）。
                if (_memoryTraceCount > 0)
                    FireBullet(pool, _memoryTraces[Mathf.Min(_salvoIndex, _memoryTraceCount - 1)],
                        Vector2.Left * 34f, 4.2f);
                break;

            // 未応答の残響：左へ投げた弾が減速して止まりかける＝届かない声。
            //   初速 118px/s → 0.9秒かけて 40px/s まで落ちる（MakeDecel）。速い→遅いの向きなので
            //   時間が経つほど避ける猶予が増える＝不意打ちにならない。落ち切っても止まらないので必ず消える。
            //   下限を 40px/s に置く理由：盤面幅 264px を約7秒で渡り切る＝間隔 3.0s×3発で画面上の
            //   定常数は7発前後に収まる。20px/s 台まで落とすと13秒残って盤面が埋まり、
            //   「失速する」という読ませたい一点より先に視認性が壊れる（§3 視認性・§7 派手さの手前で止める）。
            //   3方向へ広げるのは「誰に向けたのでもない声」＝自機狙いを外して盤面に薄く残す意図。
            case AttackPattern.MinaUnanswered:
            {
                int n = Mathf.Max(1, Dn(3));
                for (int i = 0; i < n; i++)
                {
                    float deg = n == 1 ? 0f : Mathf.Lerp(-26f, 26f, i / (float)(n - 1));
                    var b = FireBullet(pool, _characterOrigin, Rotate(_burstDir, deg) * 118f, 3.8f);
                    // MakeDecel は絶対値で扱うので、難易度の弾速倍率ぶんを下限側にも掛け直す
                    //（BulletSpeedMul を無視して減速すると Lunatic だけ極端に鈍る）。
                    float mul = b.Velocity.Length() / 118f;
                    b.MakeDecel(40f * mul, (float)Di(0.9));
                }
                break;
            }
        }
        _salvoIndex++;
        _salvoRemaining--;
        _salvoT = Di(_spec.Pattern switch
        {
            AttackPattern.ReiAnonymous => 0.18,
            AttackPattern.AkariUnsent => 0.48,
            AttackPattern.MinaEraser => 0.62,   // 2度目のなぞりまで間を置く＝「もう一度来る」が読める
            AttackPattern.MinaMemory => 0.40,   // 痕を1つずつ置いていく間
            _ => 0.32,
        });
        if (_salvoRemaining == 0) _characterAttackIndex++;
    }
    private void FireBurstShot()
    {
        var pool = Pool; if (pool == null) return;
        float spread = (float)GD.RandRange(-4.0, 4.0); // ±4°拡散
        FireBullet(pool, GlobalPosition, Rotate(_burstDir, spread) * 110f, 3.0f, 1);
    }

    // ── レイ drifter：視線パルス放射 ── 全方位 Dn(6) 発の等間隔リング1回。予告なし。
    private void FirePulseRing()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(6));
        for (int i = 0; i < n; i++)
        {
            float deg = 360f * i / n;
            FireBullet(pool, GlobalPosition, Rotate(new Vector2(1, 0), deg) * 55f, 3.2f, 1);
        }
    }

    // ── あかり shooter：ばらまき投擲 ── 固定左(180°)±35°扇に Dn(5)way、各弾±5°ゆらぎ。予告なし。
    private void FireScatter()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(5));
        var baseDir = new Vector2(-1, 0); // 左180°中心
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0.5f : i / (float)(n - 1);
            float deg = Mathf.Lerp(-35f, 35f, t) + (float)GD.RandRange(-5.0, 5.0);
            FireBullet(pool, GlobalPosition, Rotate(baseDir, deg) * 80f, 3.2f, 1);
        }
    }

    // ── あかり drifter：落書きドロップ ── 真下(90°)±20°へ低速 Dn(3)way。うねり維持・予告なし。
    private void FireDrop()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(3));
        var down = new Vector2(0, 1); // 真下90°
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0.5f : i / (float)(n - 1);
            float deg = Mathf.Lerp(-20f, 20f, t);
            FireBullet(pool, GlobalPosition, Rotate(down, deg) * 50f, 3.0f, 1);
        }
    }

    // ── こはる shooter：高速鋭3WAY ── 0.4s 短予告(本体が一瞬縮む＋小白フラッシュ)→自機方向±12°の3本。
    private void BeginSharp3()
    {
        _telegraphT = 0.4;
        FxLayer.Instance?.AimFlash(GlobalPosition, new Color(0.95f, 0.50f, 0.70f));
        SquishBody(); // 本体が一瞬縮む（予告の溜め）
    }
    private void FireSharp3()
    {
        var pool = Pool; if (pool == null) return;
        var dir = AimDir();
        // 本数は固定3本（Dn(3) は最低3本の密度目安＝固定本数なのでスケールしない）。扇幅24°（±12°）。
        foreach (float deg in new[] { -12f, 0f, 12f })
            FireBullet(pool, GlobalPosition, Rotate(dir, deg) * 130f, 2.8f, 1);
        // 発射の解放に合わせ“クルッ”と一回転の余韻を蹴る（たまに見せる切れ味＝ペンライトを振り切る性格）。
        if (GD.Randf() < 0.5f) _spin = 1f;
    }

    // ── こはる drifter：とろ火ゆらぎ弾 ── 自機狙い超低速 Dn(1) 単発。うねり維持・予告なし。
    private void FireSimmer()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(1));
        var dir = AimDir();
        for (int i = 0; i < n; i++)
        {
            float jitter = n == 1 ? 0f : (float)GD.RandRange(-6.0, 6.0);
            FireBullet(pool, GlobalPosition, Rotate(dir, jitter) * 45f, 3.4f, 1);
        }
    }

    // ─── 祈り運び（こはる面専用・KoharuPrayerCarry）───
    // 消せる「祈り弾」(MakeErasable)を3発ぶら下げて画面を横断する。祈り弾を自機弾で撃つと既存経路
    //（Bullet.OnAreaEntered → GameManager.AddPrayerCleared）でそのまま報われる。本体を撃ち落とすと
    // 残りの祈り弾もまとめて受け止め扱い（AddPrayerCleared＋花びら）。撃ち漏らして左へ抜けられたら
    // 祈り弾ごと消える（報酬なし）＝ボス戦「お残し禁止」を道中で遊びながら教える練習台。
    private static readonly Vector2[] PrayerOffsets =  // ぶら下げ位置（本体からの相対・下へ短い鎖）
        { new(0f, 15f), new(3f, 26f), new(6f, 37f) };
    // 祈り弾をぶら下げ始めるX。出現直後（x=398＝画面右外）に生むと、Bullet の画面外カリング
    //（余白16px＝x>400 で Despawn）が右寄りのオフセット弾を即回収してしまうため、画面内に入ってから生む。
    private const float PrayerSpawnGateX = 370f;
    private readonly System.Collections.Generic.List<Bullet> _carried = new();
    private bool _carriedSpawned;
    private double _carryT;
    private float _carryBaseY;      // 横断の基準Y（縦バウンドの中心。最初の移動フレームで確定）
    private bool _carryBaseYSet;

    private void TickPrayerCarry(double delta, float dt)
    {
        _carryT += delta;
        if (!_carriedSpawned && GlobalPosition.X <= PrayerSpawnGateX) SpawnCarriedPrayers();
        // 横断（従来どおり一定速で左へ）。
        //   2026-09-17：これに「よいしょ、と運ぶ」縦の小バウンドを重ねて、他種と移動でも見分くようにする。
        //   ★振幅 4px・0.9Hz と小さく保つ理由：ぶら下げた祈り弾が本体の +37px 下まで伸びるため
        //     （PrayerOffsets）、大きく揺らすと下端 216px を割って弾だけ画面外カリングで消える
        //     ＝「撃つ前に祈りが消えた」＝拾えたはずの報酬を演出で奪う事故になる。
        //     Y 湧き範囲 60〜150（Spawner）＋37＋4 = 最大 191px で下端まで 25px 残る。
        //   ★横速度は一切変えない＝「約11秒で渡り切る」＝撃ち漏らしの学習に必要な猶予は不変。
        if (!_carryBaseYSet) { _carryBaseY = GlobalPosition.Y; _carryBaseYSet = true; }
        float bob = Mathf.Abs(Mathf.Sin((float)_carryT * 0.9f)) * -4f;   // 上へ持ち上げて下ろす
        GlobalPosition = new Vector2(GlobalPosition.X - _spec.MoveSpeed * dt, _carryBaseY + bob);
        // ぶら下げた祈り弾を毎フレーム追従（ゆるい振り子＝“運んでいる”の画）。
        // プール再利用対策：Active かつ Erasable の弾だけを本物として扱う（BossKoharu と同じ作法）。
        for (int i = 0; i < _carried.Count; i++)
        {
            var b = _carried[i];
            if (!IsInstanceValid(b) || !b.Active || !b.Erasable) continue;
            float sway = Mathf.Sin((float)_carryT * 2.2f + i * 0.9f) * 3f;
            b.GlobalPosition = GlobalPosition + PrayerOffsets[i] + new Vector2(sway, 0f);
        }
    }

    private void SpawnCarriedPrayers()
    {
        _carriedSpawned = true;
        var pool = Pool; if (pool == null) return;
        foreach (var off in PrayerOffsets)
        {
            var b = FireBullet(pool, GlobalPosition + off, Vector2.Zero, 3.4f, 1);
            if (b == null) continue;
            b.MakeErasable(); // 自機弾で消せる＝消すと AddPrayerCleared（既存経路）
            _carried.Add(b);
        }
    }

    // ぶら下げ中の祈り弾を解放する。award=true（本体撃ち落とし）は AddPrayerCleared＋花びらで報い、
    // false（撃ち漏らし退場/ステージ掃除）は静かに消すだけ＝「のこした」。
    private void ReleaseCarriedPrayers(bool award)
    {
        if (_carried.Count == 0) return;
        var pool = Pool;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        foreach (var b in _carried)
        {
            if (!IsInstanceValid(b) || !b.Active || !b.Erasable) continue;
            if (award)
            {
                FxLayer.Instance?.BulletToPetal(b.GlobalPosition); // “祈りを受け止めた”の花びら
                game?.AddPrayerCleared();
            }
            pool?.Despawn(b);
        }
        _carried.Clear();
    }

    // 退場時（撃ち漏らしの左抜け/ステージ掃除/シーン遷移）に祈り弾を置き去りにしない。
    // 撃ち落とし時は GrantFollower が先に award 付きで解放済み＝ここに残りは無い。
    public override void _ExitTree()
    {
        if (_spec.Pattern == AttackPattern.KoharuPrayerCarry) ReleaseCarriedPrayers(award: false);
    }

    // ── 回り込み「引用リプ」：着座後、右向き固定の低速単発 ──
    // 左端に張り付く自機の背後（x≈40）から前方向へ流す。固定右向き＋低速＝見てから避けられる“読める圧”。
    private const float FlankBulletSpeed = 70f; // 弾速(px/s)。調整しやすいよう定数化
    private void FireFlank()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(1));
        for (int i = 0; i < n; i++)
        {
            float jitter = n == 1 ? 0f : (float)GD.RandRange(-8.0, 8.0); // 難易度で2発以上になった時だけ散らす
            FireBullet(pool, GlobalPosition, Rotate(new Vector2(1, 0), jitter) * FlankBulletSpeed, 3.0f, 1);
        }
    }

    // ── Default(アンチくん)：自機狙い単発（現状踏襲を本体一括へ移しただけ）──
    private void FireDefaultAim()
    {
        var pool = Pool; if (pool == null) return;
        int n = Mathf.Max(1, Dn(1));
        var dir = AimDir();
        for (int i = 0; i < n; i++)
            FireBullet(pool, GlobalPosition, dir * 90f, 3.0f, 1);
    }

    // 改心が確定したら生命感モーションは止め、立ち絵の傾き/潰しを素へ戻す
    //（暴れていた姿勢のまま笑顔になると不自然＝改心後は穏やかに着地）。
    // GrantFollower は浄化直後に必ず通る（cry を使わない道中ザコは PostTexPath 差替後すぐここへ）。
    protected override void GrantFollower()
    {
        // 祈り運び：本体の撃ち落とし＝残っていた祈り弾もまとめて受け止め扱い（award 付きで解放）。
        if (_spec.Pattern == AttackPattern.KoharuPrayerCarry) ReleaseCarriedPrayers(award: true);
        _motion = LivingMotion.None;
        _spin = 0f; _kick = 0f; _startle = 0f;
        if (_body != null) _body.Rotation = 0f; // Scale/Position は SwapBody/TickSwapAnim が素へ戻す
        base.GrantFollower();
    }

    // こはる短予告の“溜め”：立ち絵を一瞬ギュッと縮める（当たり判定は不変）。
    // 生命感モーションと所有権を一本化するため、Tween でなく _kick に蹴り込み、
    // TickLivingMotion が縦スカッシュとして毎フレーム合成・減衰させる（予告終了で自然に戻る）。
    private void SquishBody() => _kick = Mathf.Max(_kick, 1f);

    // ─── 生命感モーション本体 ───
    // _bodySprite(子"Body")の Position/Rotation/Scale を毎フレーム“合成”する。
    // 進行（GlobalPosition）も SwayAmp も当たり判定も触らない＝1枚絵に乗っかる装飾レイヤ。
    // 各種パラメータ（px・rad・Hz）はモチーフで性格が動きでも見分けられるよう調律。位相は _motionPhase で個体ずらし。
    private void TickLivingMotion(double delta)
    {
        if (_motion == LivingMotion.None) return;
        if (_body == null)
        {
            _body = GetNodeOrNull<Sprite2D>("Body");
            if (_body == null) return;            // まだ立ち絵が無い（プレースホルダ種）
            _bodyBaseScale = _body.Scale.Y;       // SetupBodySprite が決めた素のスケールを基準に保持
        }

        _motionT += delta;
        float t = (float)_motionT + _motionPhase; // 個体位相込みの時刻（群れの非同期化）
        float dt = (float)delta;

        // 共通の溜め減衰（予告/発射キック）。0へ向けて速やかに抜ける＝予告の“戻し”の余韻。
        if (_kick > 0f) _kick = Mathf.Max(0f, _kick - dt * 3.2f);

        float ox = 0f, oy = 0f;   // 位置オフセット(px)
        float rot = 0f;           // 回転(rad)
        float sx = 1f, sy = 1f;   // スケール係数（基準スケールに掛ける）

        const float Tau = Mathf.Tau;
        switch (_motion)
        {
            // 人型の既定（未割当の保険）。旧 Humanoid の値そのまま＝静かな呼吸。
            case LivingMotion.Humanoid:
                oy = Mathf.Sin(t * 2.2f) * 0.6f;
                rot = Mathf.Sin(t * 1.8f) * 0.025f;
                break;

            // ── あかり面（退勤後のフロア）──
            // 締切の人：急いでいる。速い小走りの上下＋前のめり。呼吸も速い（縦の伸縮＝息が上がっている）。
            case LivingMotion.AkDeadline:
                oy = -Mathf.Abs(Mathf.Sin(t * 5.6f)) * 1.9f;      // 小走りの接地バウンド（速い・倍周期）
                ox = Mathf.Sin(t * 5.6f) * 0.9f;                  // 前後の詰め
                rot = -0.055f + Mathf.Sin(t * 5.6f) * 0.030f;     // 前傾 -3.2°（急ぐ姿勢）＋歩の揺れ
                sy = 1f + 0.035f * Mathf.Sin(t * 7.4f);           // 速い呼吸
                break;

            // 送信取消の人：送ろうとして手が止まる。前へ出かけて引き戻す非対称な往復＋うつむき。
            //   sin を三乗して「ゆっくり出て、すっと引っ込む」非対称カーブにする＝迷いの質感。
            case LivingMotion.AkUnsent:
            {
                float reach = Mathf.Sin(t * 1.25f);
                reach = reach * reach * reach;                    // 端で粘り、中央を速く通る＝ためらい
                ox = -reach * 2.4f;                               // 左（自機側）へ出しかけて戻す
                oy = 0.9f + Mathf.Sin(t * 2.0f) * 0.5f;           // うつむいて少し沈む
                rot = 0.045f + reach * 0.030f;                    // 俯き +2.6°。出るときだけ起き上がる
                break;
            }

            // 空席の人：いない人。ほとんど動かず、長い周期で薄れるように沈んでまた戻る。
            //   「動かなさ」そのものを個性にする＝他11種の中で一目で浮く（§3 だまし・誘導の逆用）。
            case LivingMotion.AkVacant:
            {
                float fade = 0.5f + 0.5f * Mathf.Sin(t * 0.55f);  // 0〜1 の超低周波
                oy = fade * 1.6f;                                 // すーっと沈む
                sy = 1f - 0.035f * fade;                          // 沈むぶん縦が縮む＝存在が薄れる
                sx = 1f + 0.018f * fade;
                rot = Mathf.Sin(t * 0.9f) * 0.012f;               // ほぼ静止（±0.7°）
                break;
            }

            // ── こはる面（推し活の部屋）──
            // 比較の人：左右をきょろきょろ見比べる。首振り（ロール）が主体で、体は動かない。
            case LivingMotion.KoComparison:
            {
                // 三角波に近い往復＝「見て、止めて、反対を見る」の刻み（正弦だと常に動いて落ち着かない）。
                float look = Mathf.Sin(t * 2.6f);
                look = Mathf.Sign(look) * Mathf.Pow(Mathf.Abs(look), 0.45f); // 端で保持する台形寄りへ
                rot = look * 0.105f;                              // 首振り ±6.0°
                ox = look * 1.5f;                                 // 首の動きに体がわずかに連れられる
                oy = Mathf.Sin(t * 2.0f) * 0.5f;
                break;
            }

            // 声援の人：腕を振って応援。12種で最も大きく弾む＝賑やかさで一目で分かる。
            case LivingMotion.KoCheer:
                oy = -Mathf.Abs(Mathf.Sin(t * 3.1f)) * 3.0f;      // 大きく弾む（跳ねる応援）
                ox = Mathf.Sin(t * 1.55f) * 1.8f;                 // 倍周期の左右振り＝腕の振り
                rot = Mathf.Sin(t * 1.55f) * 0.085f;              // ±4.9° 体ごと振る
                sy = 1f + 0.05f * Mathf.Abs(Mathf.Sin(t * 3.1f)); // 頂点で伸び上がる
                break;

            // 荷物の人：抱えて耐える。沈んだまま細かく震え、長い周期で一度だけ「持ち直す」。
            case LivingMotion.KoParcel:
            {
                float hoist = Mathf.Max(0f, Mathf.Sin(t * 0.7f));
                hoist *= hoist;                                   // 持ち直しは短く鋭い（ずっと持ち上げていない）
                oy = 1.4f - hoist * 2.2f                          // 常に沈んでいて、たまに持ち上げる
                     + Mathf.Sin(t * 17.0f) * 0.35f;              // 抱えきれない細かい震え
                sy = 1f - 0.045f + 0.05f * hoist;                 // 潰れている（重さ）
                sx = 1f + 0.030f - 0.03f * hoist;
                rot = 0.030f;                                     // 前かがみ +1.7° 固定
                break;
            }

            // ── レイ面（配信枠）──
            // 匿名の人：顔が無い。輪郭が細かくぶれ続ける＝誰でもない揺らぎ（周期の違う2波を重ねて非周期に見せる）。
            case LivingMotion.ReiAnon:
                ox = Mathf.Sin(t * 13.0f) * 0.55f + Mathf.Sin(t * 5.3f) * 0.75f;
                oy = Mathf.Sin(t * 11.7f) * 0.45f + Mathf.Sin(t * 4.1f) * 0.65f;
                rot = Mathf.Sin(t * 9.1f) * 0.022f + Mathf.Sin(t * 3.7f) * 0.028f; // 合計 ±2.9°
                sx = 1f + 0.020f * Mathf.Sin(t * 7.9f);           // 輪郭そのものが定まらない
                break;

            // 切り抜きの人：切り取る。横方向にカクッと刻む（連続でなく段で動く＝フレーム送り／コマ切り）。
            case LivingMotion.ReiClip:
            {
                // 連続値を段（4段）へ量子化＝「なめらかに動かない」ことで“編集された動き”に見せる。
                float step = Mathf.Round(Mathf.Sin(t * 2.3f) * 4f) / 4f;
                ox = step * 2.2f;
                rot = step * 0.055f;                              // ±3.2° を段で刻む
                oy = Mathf.Round(Mathf.Sin(t * 1.7f) * 3f) / 3f * 1.0f;
                break;
            }

            // 数字の人：数字を追う。視線（体ごと）が下からゆっくり登り、上限でぱっと下へ戻る＝スクロール。
            case LivingMotion.ReiMetrics:
            {
                float scroll = Mathf.PosMod(t * 0.42f, 1f);       // 0→1 の鋸波（ゆっくり登る）
                oy = 1.8f - scroll * 3.6f;                        // 下から上へ。1周の終わりに一気に戻る
                rot = -0.020f + scroll * 0.040f;                  // 見上げる角度も一緒に登る
                sy = 1f + 0.018f * scroll;
                break;
            }

            // ── FINAL（ミナの内側）の残響3種 ──
            // 消しゴムの残響：横にごしごし擦る往復。擦る向きへ体が倒れ、戻りで縦が潰れる。
            case LivingMotion.MinaEraserM:
            {
                float rub = Mathf.Sin(t * 4.4f);
                ox = rub * 2.8f;                                  // 大きめの横擦り ±2.8px
                rot = rub * 0.075f;                               // 擦る向きへ倒れる ±4.3°
                oy = -Mathf.Abs(rub) * 0.8f;
                sx = 1f + 0.035f * Mathf.Abs(rub);                // 力を込めた瞬間に横へ広がる
                sy = 1f - 0.030f * Mathf.Abs(rub);
                break;
            }

            // 記憶の残響：残像。本体より遅れて揺れ、輪郭（縦横スケール）が明滅して定まらない。
            //   ゆっくり大きく漂う低周波＋位相のずれた縦横の伸縮＝「像が結ばない」画。
            case LivingMotion.MinaMemoryM:
                ox = Mathf.Sin(t * 0.85f) * 2.6f;
                oy = Mathf.Sin(t * 0.62f + 1.1f) * 2.4f;          // 横と縦で周期も位相もずらす＝漂う
                rot = Mathf.Sin(t * 0.47f) * 0.065f;              // ±3.7° ゆっくり傾ぐ
                sx = 1f + 0.055f * Mathf.Sin(t * 1.9f);           // 輪郭の明滅（像がぶれる）
                sy = 1f + 0.055f * Mathf.Sin(t * 1.9f + 2.1f);    // 縦横を別位相で伸縮＝面積が脈動する
                break;

            // 未応答の残響：待っている。ほぼ静止し、長い周期で一度だけ小さく「期待して」浮いて、また落ちる。
            //   12種で最も動かない＝「返事を待つ時間」そのものを静止で語る（§4 余韻／§3 緩急）。
            case LivingMotion.MinaUnansweredM:
            {
                float hope = Mathf.Max(0f, Mathf.Sin(t * 0.55f));
                hope = hope * hope * hope;                        // 期待は短く、落胆は長い
                oy = 0.8f - hope * 2.4f;                          // ふっと浮いて、ゆっくり沈む
                rot = 0.030f - hope * 0.055f;                     // 俯き +1.7° → 顔を上げる -1.5°
                sy = 1f + 0.030f * hope;
                break;
            }

            // 偵察ドローン：ホバリング。速い小刻み上下ブレ＋進行(左)へわずか前傾＋ローター示唆の速い微小ロール。
            case LivingMotion.ReiDrone:
                oy = Mathf.Sin(t * 7.5f) * 1.4f;                 // 上下ブレ ±1.4px / ~1.2Hz
                rot = -0.05f + Mathf.Sin(t * 11.0f) * 0.035f;    // 前傾 -2.9°＋ローター微ロール ±2°/速い
                break;

            // 監視カメラの目：ふわふわ大きめ浮遊＋たまにギョロッと向きを変える（タメ→戻し）。
            case LivingMotion.ReiEye:
                oy = Mathf.Sin(t * 1.7f) * 2.6f;                 // ゆっくり大きめ上下 ±2.6px / ~0.27Hz
                _gazeT -= delta;
                if (_gazeT <= 0)
                {
                    _gaze = (GD.Randf() < 0.5f ? -1f : 1f) * (float)GD.RandRange(0.16, 0.26); // ±9〜15°
                    _gazeT = GD.RandRange(1.4, 3.0);             // 次のギョロッまでの間
                }
                rot = Mathf.Lerp(_body.Rotation, _gaze, dt * 6.5f); // タメつつ目標ヨーへ寄せ、また戻す
                break;

            // 机・椅子：脚で歩く。左右によじよじ傾く（ロール）＋一歩ごとの上下バウンド（接地リズム）。
            case LivingMotion.AkariDesk:
                rot = Mathf.Sin(t * 3.4f) * 0.10f;               // よじよじロール ±5.7° / ~0.54Hz
                oy = -Mathf.Abs(Mathf.Sin(t * 3.4f)) * 1.8f;     // 一歩ごと（倍周期）に持ち上がるバウンド
                ox = Mathf.Sin(t * 3.4f) * 0.6f;                 // 重心の左右ゆれを少し
                break;

            // ノート・教科書：羽ばたき。Scale.x を周期的に縮め広げ（ページ/羽の開閉）＋ふわっと上下。
            case LivingMotion.AkariNote:
                sx = 1f - 0.12f * (0.5f + 0.5f * Mathf.Sin(t * 6.2f)); // 横幅 1.0→0.88 開閉 / ~1Hz
                sy = 1f + 0.05f * (0.5f + 0.5f * Mathf.Sin(t * 6.2f)); // 閉じる時わずかに縦伸び（紙の張り）
                oy = Mathf.Sin(t * 2.0f) * 2.2f;                 // ふわっと上下 ±2.2px
                break;

            // ペンライト：振っている手の小刻みな振動（じりじり）＋進行(左)へ鋭い前傾＋たまにクルッと一回転の余韻。
            case LivingMotion.KoharuPenlight:
                ox = Mathf.Sin(t * 26.0f) * 0.7f;                // じりじり高速横振動 ±0.7px
                oy = Mathf.Sin(t * 23.0f) * 0.5f;
                rot = -0.12f;                                    // 鋭い前傾 -6.9°（攻め）
                if (_spin > 0f)                                   // 撃つ瞬間に蹴られた“クルッ”を減衰させて余韻に
                {
                    rot += _spin * Tau;                          // 余韻一回転（_spin:1→0）
                    _spin = Mathf.Max(0f, _spin - dt * 1.6f);
                }
                break;

            // グッズの箱：抱えて運ぶ重さ。上下に小バウンド＋ぷるぷる横揺れ（重いものを持つ揺れ＝低周波＋微振動）。
            case LivingMotion.KoharuBox:
                oy = -Mathf.Abs(Mathf.Sin(t * 2.6f)) * 1.6f      // よいしょ、と持ち上がるバウンド
                     + Mathf.Sin(t * 14.0f) * 0.4f;              // 抱えきれない微振動を重ねる
                ox = Mathf.Sin(t * 1.5f) * 0.9f;                 // 重い横揺れ（ゆっくり）
                sy = 1f + 0.04f * Mathf.Abs(Mathf.Sin(t * 2.6f));// バウンド頂点でわずかに縦伸び
                break;
        }

        // 予告/発射の溜め：縦に潰す（_kick:1→0）。攻撃の直前に“ためてる”を全種共通で足す。
        //   人型は 0.025 と極小で、AimFlash が消えた後に「本体が身構えている」が読めなかった（2026-09-17）。
        //   0.075 へ引き上げる＝立ち絵が明確に沈むが、道具種(0.18)ほど潰れない＝人と物の質感差は残す。
        //   BeginCharacterAttack が溜めの長さに比例して _kick を 0.6〜1.4 で蹴るので、
        //   「長く構える攻撃ほど深く沈む」＝予告の強さが危険度と一致する（§4 予備動作）。
        if (_kick > 0f)
        {
            sy *= 1f - (_spec.Humanoid ? 0.075f : 0.18f) * _kick;
            sx *= 1f + (_spec.Humanoid ? 0.040f : 0.08f) * _kick;
        }

        // 人型の“ビクッ”：不定期に一瞬だけ身じろぐ（周期モーションの機械的な反復を破る）。
        // 12種すべてに乗る共通の生命感で、間隔は個体ごとにランダム＝群れが揃わない。
        if (_spec.Humanoid)
        {
            _startleT -= delta;
            if (_startleT <= 0)
            {
                _startle = 1f;
                _startleT = GD.RandRange(2.6, 6.4);
            }
            if (_startle > 0f)
            {
                _startle = Mathf.Max(0f, _startle - dt * 4.5f);
                float s = _startle * _startle;          // 立ち上がりだけ鋭く、抜けは速い
                oy -= s * 1.1f;
                rot += s * 0.030f;
                sy *= 1f + 0.028f * s;
            }
        }

        // 合成して反映（基準スケールへ係数を掛ける）。Rotation は FlipH と独立に効く。
        _body.Position = new Vector2(ox, oy);
        _body.Rotation = rot;
        _body.Scale = new Vector2(_bodyBaseScale * sx, _bodyBaseScale * sy);
    }
}
