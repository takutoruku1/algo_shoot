using Godot;
using System.Collections.Generic;

// Enemy : SNSの悪意で「悪魔化した人間」本体。不滅で“倒さない/殺さない”。
// 周囲を旋回する黒い吹き出しパネル(Panel)を持ち、全部剥がされると【浄化(改心)】される。
// 浄化後は敵グループを抜け、笑顔の味方コメントとして左へ流れ、画面外でfree。
// 浄化の瞬間に「やさしさの波紋(Ripple)」を出し、近くの人を連鎖浄化する。
// 衝突: 本体 layer=4(接触で自機被弾)。パネルは Panel 側(layer16)。
public partial class Enemy : Area2D
{
    // 浄化(改心)時の基礎得点（派生で上書き）。
    protected int Points = 100;
    protected float BodyRadius = 9f;
    // ── 本体の当たり判定の形（2026-09-07）──
    //   BodyHalfH > BodyRadius なら円ではなく**縦長のカプセル**（CapsuleShape2D）を使う。
    //   BodyHalfH の既定は 0＝従来どおりの円で、ザコ・弾・小物は何も変わらない（オプトイン）。
    //
    //   なぜ円をやめたか: ボスの立ち絵は縦長（表示高 56px に対し幅 38〜49px）で、円ひとつだと
    //   「横は絵の端に届くが縦は届かない」形になる。半径 22 では直径 44px＝横幅とはほぼ一致する
    //   のに高さの 79% しか覆えず、頭と足が判定の外に残った＝ユーザー実機指摘「上から当たらない」
    //   「ボスのイラストに当たり判定つけて」。半径を上げるだけだと今度は横がはみ出す
    //   （26 にすると直径 52px で、いちばん細い絵の 38px を 14px も超える）。
    //   カプセルなら縦横を別々に決められるので、1つの形のまま絵に沿う。
    protected float BodyHalfH = 0f;   // 0＝円（従来）／>BodyRadius でカプセルの縦半径

    // 合図リング／露出オーラ／被弾リングの描画基準（見た目だけ。当たり判定は上の形のまま）。
    //   接触半径をボスで 9→22 に上げた（2026-09-06）ぶん、そのまま基準にすると露出オーラが
    //   スイートスポットの薄リング（PointBlankRange=48px）と重なって「どこまで詰めれば得か」が読めなくなる。
    //   描画側だけ上限を設けて、オーラと 48px リングの間隔を残す。
    private const float AuraRadiusMax = 14f;
    private float AuraRadius => Mathf.Min(BodyRadius, AuraRadiusMax);

    // パネル構成（派生で設定）。
    protected int PanelCount = 3;
    protected int PanelInk = 2;
    protected float OrbitRadius = 18f;
    protected float PanelDisplayScale = 1f; // パネル絵＆当たりの拡縮（ザコ縮小用。ボスは1のまま）
    protected float SpinSpeed = 1.4f; // rad/s
    protected bool PanelsFire = true;
    protected float PanelFireInterval = 1.9f;
    protected float EnemyBulletSpeed = 90f;

    // スプライト素材（null/未設定なら _Draw のプレースホルダ図形を使う）
    protected string PreTexPath = "";
    protected string PostTexPath = "";
    protected string PanelTexPath = "";
    // 任意：第二形態の待機絵（*_body_idle2.png）。設定すると HP 閾値の中盤で AdvanceForm2() が
    // PreTexPath をこれに差し替える＝以後の待機はすべて第二形態になる（攻撃の一拍から戻る先も同じ）。
    // 空なら第二形態は起きない＝設定していないボス（ヒカゲ・ミナ・カメオ・ザコ）は完全に不変。
    protected string Form2TexPath = "";
    protected string Form2AttackTexPath = "";
    // 任意：浄化の瞬間に一時表示する「大泣き」スプライト。設定すると pre→cry→(CryHoldDur秒)→post の3段階に。
    protected string CryTexPath = "";
    protected double CryHoldDur = 0;
    protected float BodyDisplayH = 40f;
    // ★FaceLeft＝「素材の基準向き」。true なら素材は**右向き**に描かれていて、自機側(左)を向かせるには
    //   反転が要る（＝初期 FlipH の値そのもの）。false は素材が最初から左を向いている
    //   （人型12種はこちら。EnemyTable.Character の flipH 既定＝false／レイ「匿名の人」だけ true）。
    //   下の向き更新はこれを「基準」として自機の左右と合成する（SetFacingLeft）。
    protected bool FaceLeft = true;
    private Sprite2D _bodySprite = null!;
    private bool _hasBodyTex;

    // ─── 向き：敵は常に自機の方を向く（2026-09-28 作者指示「通りすぎたとき／うしろから出たときも同様」）───
    //   それまで向きは出現時の FaceLeft で固定だった＝自機を追い越しても、自機の背後（左）から湧いても
    //   背中を向けたまま撃っていた。決め方を「進行方向」ではなく**自機の位置**に変え、毎フレーム引き直す。
    //   ★見た目だけ。弾の発射方向・移動・当たり判定・弾幕の幾何には一切触らない
    //     （向きを読むのは _bodySprite.FlipH と BossParts.SetFlip の描画2か所だけ）。
    //   ボス/中ボスは BossMover が同じ規則（自機の側・ただし 40px/0.6秒の保持つき）で向きを握るので、
    //   ApplyBossMotion が FacePlayer を false にして二重に書かないようにする。
    protected bool FacePlayer = true;
    private bool _artFacesRight;   // SetupBodySprite が FaceLeft から控える「素材の基準向き」
    // ヒステリシス（チラつき止め）。自機との x 差がこの値を超えたときだけ向きを変える＝
    //   ±FacingDeadzone の帯の中では今の向きを保つ（反転には 2×＝24px の移動が要る）。
    //   12px の根拠：着座中のザコは自分の癖で camp.X の周囲 ±CampDriftMax(9px) を横に漂う
    //   （MidEnemy.CampHorizontalDrift）。9px 以下にすると「自機が止まっているのに敵が自分で揺れて
    //   左右が入れ替わる」＝自発的なパタパタが起きる。12px ならそれを確実に外し、
    //   ザコの表示幅（20〜30px）の半分弱＝「はっきり反対側に来た」と読める最短の幅に収まる。
    private const float FacingDeadzone = 12f;
    // 世界の色味（各ステージ Root の CanvasModulate＝夜の冷色 Tint）。本体スプライトはこれを 1/Tint で
    // 完全に打ち消して、どのステージでも昼の素材そのままの明るさで立たせる（2026-09-27 作者指示
    // 「自キャラ／敵もどのステージでも明るくしてほしい」。強さは TintLift.EnemyBody=1）。
    // 置き先は _bodySprite.Modulate＝差し替えクロスフェードのα（SelfModulate）と別枠にして混ぜない。
    // 敵弾（ネオン縁）・投稿チップ（Panel）・演出粒は従来どおり Tint を受けたまま＝夜の空気は背景が担う。
    private CanvasModulate? _worldTint;

    // ─── 姿勢ごとの表示オフセット（v3 の本体は姿勢ごとに絵の幅が違う）───
    //   BossParts.BodyOffsets の表から引く名前（"akari"/"koharu"/"rei"/"cameo"）。空なら従来どおり中央揃え。
    //   絵は姿勢で幅が変わるので、中央揃えのままだと足元が横に滑る。表の値を Sprite2D.Offset へ入れて
    //   どの姿勢でも足元が同じ画面位置に来るようにする。★当たり判定（_bodyShape・GlobalPosition）は不変。
    protected string BodyOffsetName = "";
    private BossParts.Pose _bodyPose = BossParts.Pose.Idle;
    protected string DownTexPath = "";
    protected string Form2DownTexPath = "";
    private bool _bodyDown;
    private double _downBreathT;
    private Texture2D[]? _downBreathFrames;

    // ─── 改心の絵だけ縮める倍率（既定 1＝待機と同じ表示高）───
    //   レイだけ cry/post が「ガワの中の人」で、ガワ（待機）と同じ高さで出すと同一人物の等身が破綻する。
    //   1 未満にすると SwapBody の基準スケールに掛かる。素材はどれも足元まで詰めてある（不透明域が下端まで）
    //   ので、縮めたぶん足元が浮く＝その差を Offset で押し下げて、ガワと同じ床に立たせる。
    protected float CryBodyScale = 1f;
    protected float PostBodyScale = 1f;
    // true＝改心（cry）の開始で本体を Cry 絵へ差し替えず、Pre の姿のまま会話に入る。
    //   レイ面（仮台本 07 の S3-8）用：ガワは決定打の行で割れるので、そこまで笑顔のガワで喋らせたい。
    //   差し替えは会話ドライバが BreakCryBodyNow() を呼んだ瞬間に走る（呼ばれなければ FinishCry の
    //   Post 差し替えでそのまま着地する＝取り残しにならない）。
    protected bool DeferCryBodySwap;
    // true＝BreakCryBodyNow を「クロスフェードで差し替える」のではなく「ガワが左右にほどけて中の人が現れる」
    //   演出（ShellPeelFx・案1）で見せる。レイ面だけが使う（docs/20260906/astra_試行_ガワ割れ.md）。
    //   中の人（Cry 絵）は最初からガワの背後に置かれ、ガワが退くことで見える＝二人の顔のクロスフェードにしない。
    protected bool PeelCryBodySwap;
    private float _bodyScaleMul = 1f;   // いま表示中の絵に掛かっている倍率（足元補正の計算に使う）

    // 攻撃姿勢の絵（任意。空なら姿勢の差し替えをしない＝従来どおり待機のまま撃つ）。
    // 撃った瞬間に AttackTexPath へ差し替え、AttackPoseDur 秒たったら待機（PreTexPath）へ戻す。
    // 差し替えは既存の SwapBody（クロスフェード＋squash→pop）をそのまま使う。
    protected string AttackTexPath = "";
    protected string RecoveryTexPath = "";
    protected Sprite2D BodySprite => _bodySprite;
    protected bool BodyMotionReady => !_bodyDown && !_purified && !_crying && !_entering && !_swapAnim;
    protected bool BodyAttacking => _attackPoseT > 0;
    private BossTransformation? _transformation;
    public bool Transforming => IsInstanceValid(_transformation) && !_transformation!.IsQueuedForDeletion();
    private double _attackPoseT;
    private const double AttackPoseDur = 0.55;
    private double _animalMotionT, _animalTechniqueCooldown = 3.0;
    private bool _animalWindup;
    private BossAnimalTechnique? _animalTechnique;
    private Texture2D[] _animalFrames = System.Array.Empty<Texture2D>();
    private string AnimalId => Form2TexPath.GetFile().Split('_')[0];
    internal bool AnimalTechniqueActive => IsInstanceValid(_animalTechnique) && !_animalTechnique!.Finished;

    // ─── ボス用：言葉のシールド＋無防備窓サイクル（HPバー方式リワーク）───
    // SHIELDED（周回パネル・通常弾幕。弾はパネルのInkを削るだけ。本体HPは減らない）
    //  → 全パネル破壊で BREAK（タメ＋合図演出）
    //  → EXPOSED 無防備窓（プレイヤー弾の威力が本体HPへ直通。パネル復活なし）
    //  → 窓終了で RECLOSE（キャラ別セリフ→パネル一括再生成）→ SHIELDED
    //  → HP<=0 で Redeem()
    protected enum BossPhase { Shielded, Break, Exposed, Reclose }
    private BossPhase _phase = BossPhase.Shielded;
    private double _phaseT; // 現フェーズ経過秒
    public const int BarHp = 100;                 // 1本=100（HUDで大きく動く）
    protected int BarCount = 0;                    // >0でHPバー方式に。総HP=BarHp×BarCount（派生が設定）。
    private const double VulnDur = 4.0;            // 無防備窓（全難易度共通）
    private const double BreakCueDur = 0.45;       // BREAK タメ＋合図
    private const double RecloseLineDur = 1.2;     // RECLOSE セリフ尺
    private const double RespawnGap = 0.15;        // RECLOSE 後パネル一括再生成までの間（弱気セリフ後の空白を詰めてテンポ維持）
    private const double VulnWarnLead = 1.0;       // 窓終了この秒前から明滅を速める（終了予告）
    // ── 無防備窓の「密着ボーナス」(桜井: 引き撃ちだけが最適にならないよう、近いほど得) ──
    // 本体 GlobalPosition と自機の距離が PointBlankRange 以内なら密着クリティカル。
    // 当たり判定(_bodyShape/本体GlobalPosition/自機ヒット半径)は一切変えない＝ダメージ計算のみ。
    //   ★2026-09-13 ジョブ導入：距離ボーナスの値はジョブ表（Jobs / JobTuning）へ移した。
    //     しきい値は Jobs.CloseRange(48px) ＝ここの旧 PointBlankRange と同値（判定の見え方は不変）。
    //     倍率と上限は JobDef.CritMult / CritCap（他ジョブ ×1.25・上限5 ／ 灯し手 ×2.0・上限8 ／ 語り手は無効）。
    //     鏡像として Jobs.FarRange(120px) より遠い命中に JobDef.FarMult（語り手のみ ×1.3）を掛ける。
    private const float PointBlankRange = Jobs.CloseRange;
    // ── 1つの無防備窓で本体へ通せる被ダメ上限（窓キャップ）──
    //   1窓で削れる量を頭打ちにし、密着クリティカル＋高連射での「1窓即殺」を抑える。
    //   到達後はその窓では本体HPが減らない（弾の Despawn は継続＝撃ち心地は残す）。EnterExposed で 0 にリセット。
    //   密着クリティカルは上限を超えず「到達を早める」だけ＝近づく価値は残しつつ過剰削りを抑える。
    //   #25 のHP増（Normal以上+1本）に合わせ 90→100＝ちょうど1本ぶん。未強化でも窓数が伸びすぎない。
    //   #b案：到達した瞬間に窓（VulnDur=4.0s）の残り時間を待たず TickBossPhase が即 EnterReclose へ進める
    //   （火力に投資するほど窓が早く閉じ、次のBREAKへ速く進む＝テンポでリターンを返す。詳細は TickBossPhase 参照）。
    //   ★2026-09-13：定数をやめ、火力3段（#4 火力2倍 / #8 線+1 / #11 連射2倍）の所持数で
    //     100 + 25×n へ伸ばす＝火力に投資するほど1窓で通せる量が増え、「強くなったのに窓の中で
    //     手が空く」を作らない。値の定義元は GameManager.ExposedDamageCap（BodyHitCd は据え置き）。
    private static int WindowCap => GameManager.Instance?.ExposedDamageCap ?? 100;
    private int _windowDamage;                      // 現在の無防備窓で本体へ通した累計ダメージ
    private bool _windowCapNotified;               // 「MAX」表示を窓ごとに一度だけ出すワンショット
    // ── 与ダメ数値のポップアップ（2026-09-17 ユーザー指示で非表示）──
    //   「いくつ食らわせた／食らっているか」を数字で見せない＝手応えは光・音・揺れ・HPバーで返す。
    //   数字そのものをやめるだけで、ダメージ計算・窓キャップ・バー割れの帳簿は一切変えない。
    //   「MAX」「BREAK!」のような“状態を伝える語”は数値ではないので残す（下の Show* とは別扱い）。
    //   復活させたいときはこのフラグを true に戻す（呼び出し側のコードは残置）。
    //   const ではなく static readonly にするのは、const だと呼び出し側が「到達しないコード」と
    //   判定されて CS0162 が出るため（0 Warning を保ちつつコードを残す）。
    public static readonly bool ShowDamageNumbers = false;
    // 本体ヒットのクールダウン（同一フレームの多重弾で過剰に削れるのを軽く抑える補助）。
    private double _bodyHitCd;
    private const double BodyHitCd = 0.05;
    private int _maxHp;                             // 総HP（=BarHp×BarCount）
    private int _hp;
    public bool HasHpBar => _maxHp > 0;
    // 撃破時に散る「心の欠片」の量を決める格（FxLayer.PurifyBurst）。
    //   HPバー持ち＝ボス級。カメオ（中ボス）だけ CameoBoss が MidBoss へ落とす。
    //   スコアの総量は変えず、粒の数と散り方だけが変わる。
    protected virtual FxLayer.PurifyTier PurifyGrade
        => _maxHp > 0 ? FxLayer.PurifyTier.Boss : FxLayer.PurifyTier.Zako;
    // 種ごとの欠片の粒数倍率（2026-09-17）。MidEnemy が EnemySpec.ShardWeight を返す。
    //   既定 1f＝従来どおり。ボス/中ボス・旧ザコ（GlyphMote/PageShard）は種の概念が無いので据え置き。
    //   ★スコアもショップ通貨も変わらない（FxLayer.PurifyBurst の countMul コメント参照）。
    protected virtual float ShardMul => 1f;
    public virtual float HpRatio => _maxHp > 0 ? (float)_hp / _maxHp : 0f;
    // HUD「1本リフィル方式」用。現在の1本ぶんを 0〜1 で、残バー数を index/total で示す。
    public int TotalBars => BarCount;
    public int CurrentBarIndex => _maxHp <= 0 ? 0 : Mathf.Clamp((_hp - 1) / BarHp, 0, BarCount - 1); // 残バーの先頭(0始まり)
    public float CurrentBarFrac => _maxHp <= 0 ? 0f : (_hp <= 0 ? 0f : (float)(((_hp - 1) % BarHp) + 1) / BarHp);

    private readonly List<Panel> _panels = new List<Panel>();
    private bool _purified;
    private bool _bombPurify; // この浄化がボム由来か。報酬側のボムキャップ判定にだけ使う（演出・消滅処理は一切不変）
    private bool _becameFollower; // フォロワーへの引き継ぎ後に本体を残して二重表示しない。
    private bool _crying;     // 大泣き中（3段階浄化の中間）
    private double _cryT;
    // ── 改心（cry）の保険タイムアウト（softlock 防止）──
    //   ボスは CryHoldDur=9999 で「自動終了させない＝会話を手動送りし切って EndCryNow」を作法にしている。
    //   そのため EndCryNow へ届かない事故（Hud が取れず会話が出ない／送り入力が別状態に食われる／
    //   会話開始前に演出が中断された等）が起きると、Finished が永久に立たず Step_BossWait が回り続ける。
    //   ここでは cry 開始からの実時間を別途計り、CryHoldDur とは無関係に上限を超えたら必ず
    //   通常の終了経路（_crying=false→SwapBody(Post)→OnCryEnd→GrantFollower）へ落とす。
    //   ＝保険発動時も post 着地・Finished・フォロワー付与といった後処理は一切スキップしない。
    //   会話を普通に送る通常プレイでは EndCryNow が先に走るため、この保険は発動しない（見え方も尺も不変）。
    private double _cryWatchdogT;
    // 会話が「進んでいる」限りは待つ（長台詞・じっくり読むプレイを切らない）。
    //   ・NotifyCryProgress() が呼ばれるたびに 0 へ戻る＝1行送るごとにタイマーはリセットされる。
    //   ・無操作のまま CryStallLimit 秒（=会話1行あたりの猶予）過ぎたら強制終了。
    //   ・どれだけ送っていても CryHardLimit 秒で必ず終了（送り自体が壊れている場合の最後の砦）。
    private const double CryStallLimit = 90.0;
    private const double CryHardLimit = 600.0;
    private double _cryTotalT;
    private const float OffLeftX = Field.Left - 24f;

    private const double PurifiedExitHold = 0.6;
    private const double PurifiedExitFade = 0.9;   // その後この秒数で透明化して消える
    // 派生ごとの上書き（0以下＝既定値を使う）。レイ面のように「改心で出てきた姿を見せたい」ボスは
    //   Hold を伸ばして、割れたガワの下から出た中の人が数秒で消えてしまうのを防ぐ。
    protected double PurifiedExitHoldOverride;
    private double ExitHold => PurifiedExitHoldOverride > 0 ? PurifiedExitHoldOverride : PurifiedExitHold;
    private double _purifiedExitT;
    private bool _flashing;
    private double _flashT;
    private const double FlashDur = 0.5;

    // 無防備窓で本体を撃ち込んだ瞬間の手応え（“効いてる”実感）。
    // 当たり判定は一切触らず、_Draw の発光リング＋音＋（大ダメージ時）軽い揺れ/止めだけで返す。
    private double _hitFlashT;                 // 被弾発光の残り（_Draw が参照）
    private const double HitFlashDur = 0.16;   // 短く・即・尾を引かせない（テンポ維持）
    private float _hitFlashMag;                // 直近被弾の威力（リングの強さに反映）

    // ─── 改心の“溶けるような”差し替え演出（クロスフェード＋squash→pop）の調整定数 ───
    // 当たり判定は一切動かさない：すべて _bodySprite の Transform/Modulate のみで表現する。
    private const double SwapFadeDur = 0.12; // 旧→新テクスチャのクロスフェード尺
    protected float SquashScale = 1.15f; // 差し替え瞬間の最大ふくらみ（×BaseScale）
    protected float PopLiftPx = 6f;      // フォロースルーで一瞬持ち上げる量(px・見た目のみ)
    private const double HitstopDur = 0.08;  // 改心確定の一拍で止める長さ

    // ─── ボス登場演出（吉田 §6 登場・§4 三段：予備動作→本動作→余韻）───
    // BarCount>0（ボス/カメオ）のスポーンで自動再生する：右端に一瞬だけ顔を見せ（予告）→
    // 画面外へ引いてタメ → 急加速で突入し着地点を通り過ぎ → ブレーキ（前傾）で揺り戻して静止。
    // 静止してから盾(パネル)を展開＝焦らし→開放。演出中は当たり判定OFF・移動/弾幕停止
    //（見た目が画面外なのに当たる/撃つ理不尽を断つ）。Boss*.cs 側の変更は不要（基底で完結）。
    protected double EntranceDur = 1.15;   // 全長(s)。カメオは OnEnemyReady で短縮する（簡易版）。
    protected float EntranceShake = 2.5f;  // ブレーキの一拍で入れる画面揺れ（カメオは控えめ）。
    private bool _entering;
    private bool _entInit;                 // ステージが GlobalPosition を入れた後（初回tick）に軌道を確定
    private double _entT;
    private Vector2 _entTarget, _entPeek, _entPull, _entPrev;
    private float _entLean;                // 速度由来の前傾（ブレーキで自然に揺り戻る）
    private bool _entBraked;               // ブレーキ開始の一拍（Shake+白閃）を一度だけ
    private const float EntAnticFrac = 0.22f;  // ここまで＝引き（顔見せ→画面外へタメ）
    private const float EntDashFrac = 0.60f;   // ここまで＝急加速IN（以降ブレーキ）
    private const float EntOvershootPx = 26f;  // 着地点を一度通り過ぎる距離(px)
    private const float EntLandXMax = 324f;    // 画面外スポーン(W0等)でも画面内に着地させる上限X

    // ─── ザコの移動バンク（#4 向き差分：専用絵なし・コードのみ）───
    // 速度から進行方向へ立ち絵(_bodySprite)を傾ける。見た目のみ＝当たり判定・進行は不変。
    // ボス/カメオ(ApplyBossMotion=BossMover.Lean)と生命感モーション持ち(MidEnemy)は
    // 姿勢を自前で握るので AutoBank を切る（二重に回転を書いて喧嘩しない）。
    protected bool AutoBank = true;
    private Vector2 _bankPrev;
    private bool _bankPrevSet;
    private float _bank;
    private const float BankLeanMax = 0.14f;  // 横速度フルで約±8°（弾より目立たない控えめ）
    private const float BankPitchMax = 0.05f; // 縦（居座りの上下往復）は±3°弱の揺らぎ
    private const float BankSpeedRef = 60f;   // この速度(px/s)でフルバンク

    // 立ち絵の“素”のスケール（BodyDisplayH/テクスチャ高で決まる）。squash はこれに係数を掛ける。
    private float _baseScale = 1f;
    // ApplyBossMotion が与える呼吸/浮遊オフセット。pop の持ち上げはこれに加算して描く（呼吸と喧嘩しない）。
    private Vector2 _motionOffset = Vector2.Zero;

    // 差し替えクロスフェード：旧テクスチャを別 Sprite2D に退避してα落とし、本体(新)をα上げ。
    private Sprite2D? _fadeSprite;
    private float _fadeBaseScale;
    // squash→pop の進行（0..1）。差し替えの瞬間に起動し、SwapAnimDur で 1 に達して終わる。
    private bool _swapAnim;
    private double _swapAnimT;
    private const double SwapAnimDur = 0.22; // squash→pop の全長（クロスフェードより少し長く余韻を残す）

    // ガワ割れ（PeelCryBodySwap）の進行。層は本体スプライトの兄弟として立ち、TickShellPeel が時間を送る。
    // 会話中（Hud.BubblePaused）でも止めない＝止めるとガワが開いたまま固まる。
    private ShellPeelFx? _peel;
    private double _peelT;
    // ガワ割れが始まってから、この秒数は会話を送らせない（決定打の一行を演出の尺ぶん置く）。
    // ShellPeelFx.SettleAt と同じ＝二枚が消え、部品も消えて全部が静止するまで。
    public const double ShellPeelHold = 2.20;
    private bool _peelStarted;
    public bool ShellPeelBusy => _peelStarted && _peelT < ShellPeelHold;

    private CollisionShape2D _bodyShape = null!;

    public bool IsPurified => _purified;
    private bool _wasLockTarget = false;   // 前フレームのロック対象だったか（照準マーカーの消し忘れ防止。_PhysicsProcess で更新）
    protected bool IsShieldPhase => _phase == BossPhase.Shielded; // 派生ギミックが「今は殴れる時間か」を参照

    // 自機がこのボスへ有効打を入れた瞬間のフック（パネルのインク削り＝Panel 側／無防備窓の本体ヒット）。
    // 派生ボスの「攻めているか」駆動ギミック（レイの逃げ腰圧など）が上書きする。基底は何もしない。
    public virtual void OnPlayerDealtDamage() { }

    // 本体に自機弾が刺さった瞬間のフック。fromDir＝弾から本体へ向かう単位ベクトル（＝のけぞる向き）。
    // 派生ボスが BossMover.OnHit(fromDir) へ流して「小さくのけぞって戻る」一拍を出す。基底は何もしない。
    protected virtual void OnBodyDamaged(Vector2 fromDir) { }

    public override void _Ready()
    {
        AddToGroup("enemies");
        CollisionLayer = 4;  // 敵本体（接触で自機被弾）
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = true;
        // 無防備窓中だけ本体が自機弾を拾う（EnterExposed/EnterReclose で mask=2 を開閉する）。
        AreaEntered += OnBodyHitByPlayerBullet;

        OnEnemyReady();
        // ★当たり判定は OnEnemyReady の後で作る：BodyRadius / BodyHalfH は派生（MidEnemy / 各ボスの
        //   boss_stats.ini）が OnEnemyReady で上書きするので、その前に作ると全敵が基底の既定値 9px の
        //   まま固定されてしまう（＝「ボスに触っても当たらない」の原因。2026-09-06 修正）。
        //   BodyHalfH を指定した敵（ボス）は縦長カプセル、それ以外は従来どおり円。
        //   CapsuleShape2D の Height は「全高」＝両端の半円込み。縦半径 BodyHalfH の 2 倍を渡し、
        //   Radius より小さくならないようクランプする（Godot は Height < 2*Radius を許さない）。
        _bodyShape = new CollisionShape2D
        {
            Shape = BodyHalfH > BodyRadius
                ? new CapsuleShape2D { Radius = BodyRadius, Height = Mathf.Max(BodyHalfH * 2f, BodyRadius * 2f) }
                : new CircleShape2D { Radius = BodyRadius },
        };
        AddChild(_bodyShape);
        // ボスHPは難易度別バー本数で決まる（総HP=BarHp×BarCount）。本数は派生 OnEnemyReady で確定済み。
        _maxHp = BarCount * BarHp;
        _hp = _maxHp;
        SetupBodySprite();
        _worldTint = TintLift.Find(this);
        ApplyTintLift();   // 初フレームから明るく出す（以降は _PhysicsProcess が毎フレーム引き直す）
        if (_maxHp > 0)
        {
            // Same layer as the body, after it in tree order, but below the later BREAK label and bullets.
            _shieldLayer = new Node2D { Name = "Shield", ZIndex = -1 };
            AddChild(_shieldLayer);
            _shieldLayer.Draw += () => DrawShield(_shieldLayer);
        }
        // ボス/カメオ（HPバー方式）は登場演出から始める：盾(パネル)は着地後に展開（焦らし→開放）。
        // 立ち絵が無い場合は演出をスキップして従来どおり即展開（プレースホルダで滑空しても見得にならない）。
        if (BarCount > 0 && _hasBodyTex) BeginEntrance();
        else SpawnPanels();
    }

    // 登場演出の開始。演出中は触れられない/撃たれない（見た目が画面外なのに当たるのを防ぐ）。
    private void BeginEntrance()
    {
        _entering = true;
        Monitorable = false;
        _bodyShape.Disabled = true;
        // ステージが GlobalPosition を入れてから初回 physics tick で軌道が確定するまでの
        // 数フレーム、スポーン地点に立ち絵が見えてしまう（登場の驚きが割れる）のを防ぐ。
        // TickEntrance の初期化＝右端の“顔見せ”位置へ移した瞬間に表示へ戻す。
        if (_bodySprite != null) _bodySprite.Visible = false;
    }

    // 登場演出を1フレーム進める（_PhysicsProcess が演出中はこれだけを回す＝移動/弾幕は止まる）。
    // 動かすのは本体 GlobalPosition と _bodySprite.Rotation のみ。会話(BubblePaused)中も進む＝
    // ボスイントロの会話に重ねて「現れる」を見せる。
    private void TickEntrance(double delta)
    {
        if (!_entInit)
        {
            // ステージは AddChild の後に GlobalPosition を入れるので、軌道確定は初回tickまで遅延する。
            _entInit = true;
            _entTarget = new Vector2(Mathf.Min(GlobalPosition.X, EntLandXMax), GlobalPosition.Y);
            _entPeek = new Vector2(392f, _entTarget.Y); // 右端に体の端がわずかに覗く位置
            _entPull = new Vector2(438f, _entTarget.Y); // 完全に画面外（タメ）
            GlobalPosition = _entPeek;
            _entPrev = _entPeek;
            if (_bodySprite != null) _bodySprite.Visible = true; // 軌道確定＝ここから見せる
            return;
        }

        _entT += delta;
        float dt = (float)delta;
        float u = Mathf.Clamp((float)(_entT / EntranceDur), 0f, 1f);
        Vector2 overshoot = _entTarget + new Vector2(-EntOvershootPx, 0f);
        Vector2 pos;
        if (u < EntAnticFrac)
        {
            // 予備動作：右端に一瞬だけ姿を見せ、すっと画面外へ引く（「来る」の予告）。
            float k = u / EntAnticFrac;
            float e = 1f - (1f - k) * (1f - k);            // ease-out
            pos = _entPeek.Lerp(_entPull, e);
        }
        else if (u < EntDashFrac)
        {
            // 本動作：タメから解放。一気に加速して着地点を通り過ぎる。
            float k = (u - EntAnticFrac) / (EntDashFrac - EntAnticFrac);
            float e = k * k * k;                            // ease-in cubic（終端で最速）
            pos = _entPull.Lerp(overshoot, e);
        }
        else
        {
            // 余韻：ブレーキ。行き過ぎた分を戻しながら減速して静止（揺り戻し）。
            float k = (u - EntDashFrac) / (1f - EntDashFrac);
            float e = 1f - (1f - k) * (1f - k) * (1f - k);  // ease-out cubic
            pos = overshoot.Lerp(_entTarget, e);
            if (!_entBraked)
            {
                _entBraked = true;
                // ブレーキの一拍＝「現れた」の合図（小シェイク＋白閃）。弾幕はまだ＝焦らし。
                GameCamera.Instance?.Shake(EntranceShake, 0.15f);
                FxLayer.Instance?.AimFlash(_entTarget, new Color(1f, 1f, 1f, 0.9f));
            }
        }
        GlobalPosition = pos;

        // 速度由来の前傾：突入中は進行方向へ深く倒れ、ブレーキで自然に揺り戻る（フォロースルー）。
        if (dt > 0f)
        {
            Vector2 vel = (pos - _entPrev) / dt;
            _entPrev = pos;
            float targetLean = Mathf.Clamp(vel.X / 480f, -1f, 1f) * 0.45f;
            _entLean = Mathf.Lerp(_entLean, targetLean, 1f - Mathf.Exp(-12f * dt));
            if (_bodySprite != null) _bodySprite.Rotation = _entLean;
        }

        if (u >= 1f)
        {
            _entering = false;
            GlobalPosition = _entTarget;
            if (_bodySprite != null) _bodySprite.Rotation = 0f;
            // 静止 → 盾（言葉のパネル）を展開して戦闘へ（登場の見得を切ってから開放）。
            SpawnPanels();
            // 当たり判定を解禁（作法として遅延セットで戻す）。
            SetDeferred(Area2D.PropertyName.Monitorable, true);
            _bodyShape?.SetDeferred(CollisionShape2D.PropertyName.Disabled, false);
            // 着地の余韻：既存の squash→pop（差し替えアニメ機構）を流用して一拍だけ弾ませる。
            _swapAnim = true;
            _swapAnimT = 0;
        }
    }

    // ザコの移動バンク：直近フレームの実速度から傾きを決め、指数補間で慣性を持たせる
    //（加速で倒れ・停止で起き上がる＝予備動作と余韻が自動で出る）。見た目のみ・判定不変。
    private void TickAutoBank(double delta)
    {
        if (!AutoBank || !_hasBodyTex || _bodySprite == null) return;
        float dt = (float)delta;
        if (dt <= 0f) return;
        if (!_bankPrevSet) { _bankPrev = GlobalPosition; _bankPrevSet = true; return; }
        Vector2 vel = (GlobalPosition - _bankPrev) / dt;
        _bankPrev = GlobalPosition;
        float target = Mathf.Clamp(vel.X / BankSpeedRef, -1f, 1f) * BankLeanMax
                     + Mathf.Clamp(vel.Y / BankSpeedRef, -1f, 1f) * BankPitchMax;
        _bank = Mathf.Lerp(_bank, target, 1f - Mathf.Exp(-7f * dt));
        _bodySprite.Rotation = _bank;
    }

    // 自機の位置から向きを決め直す（毎フレーム）。進行方向では決めない＝追い越されても、
    // 自機の背後（左）から湧いても、必ず自機の方へ体を向ける。
    //   ・|自機x − 自分x| が FacingDeadzone 以下なら据え置き（ほぼ同じ x でのパタパタ防止）。
    //   ・浄化後（IsPurified）は更新しない＝改心の絵と左への退場は演出のまま固定（Redeem で左に寄せる）。
    //   ・登場演出中・大泣き中・会話中（BubblePaused）は _PhysicsProcess がここまで来ない＝演出は不変。
    private void TickFacing()
    {
        if (!FacePlayer || _purified || !_hasBodyTex || _bodySprite == null) return;
        if (GetTree().GetFirstNodeInGroup("player") is not Node2D pl) return;
        float dx = pl.GlobalPosition.X - GlobalPosition.X;
        if (Mathf.Abs(dx) <= FacingDeadzone) return;
        SetFacingLeft(dx < 0f);
    }

    // 向きを立ち絵へ反映する。FlipH は「自機の左右」と「素材の基準向き(_artFacesRight)」の合成で決まる
    //   ＝左向きに描かれた素材（人型12種）も右向き素材（アンチくん・道具・レイ匿名）も同じ規則で自機を向く。
    // 動かすのは描画だけ：FlipH と、反転で符号が入れ替わる Offset、部品層の反転。
    // 当たり判定（Area2D の GlobalPosition・_bodyShape）と弾の発射は一切触らない。
    private void SetFacingLeft(bool faceLeft)
    {
        bool flip = faceLeft == (BossAnimalArt.Contains(_bodySprite.Texture) ? false : _artFacesRight);
        if (_bodySprite.FlipH == flip) return;
        _bodySprite.FlipH = flip;
        ApplyBodyOffset();   // 反転は Offset.x の効きも反転する＝入れ直して足元と中心を動かさない
        // 差し替えクロスフェード中の旧絵も一緒に向ける（片方だけ残ると二枚の顔が逆向きに重なる）。
        if (_fadeSprite != null)
        {
            _fadeSprite.FlipH = flip;
            _fadeSprite.Offset = new Vector2(-_fadeSprite.Offset.X, _fadeSprite.Offset.Y);
        }
        _parts?.SetFlip(flip);
    }

    protected virtual void OnEnemyReady() { }

    // ─── スペルカードの弾形・色（RefrainHTML Danmaku v3）───
    // 派生ボスがパターン切替時に SetSpellVisual で更新し、FireBullet が反映する。
    protected BulletShape CurShape = BulletShape.Orb;
    protected Color CurTint;
    protected bool CurTintSet;
    // 現在のスペルの「弾の絵」（こはる＝推し活グッズ／あかり＝仕事の書類）。null＝従来の弾形で描く。
    // BulletArt.Get のキャッシュ済みテクスチャを持つだけなので、スペル切替のたびにロードは走らない。
    protected Texture2D? CurSprite;
    protected float CurSpriteRot = 55f;   // 絵の回転速度（deg/s）。スペルごとに変える
    // sprite を渡すと、その弾は絵で描かれる（弾形・色は絵の裏のグローと、絵が無い時の保険として残る）。
    // 当たり半径・弾数・弾速には触れない＝見た目だけの指定（難易度の Dn/Di/spd は不変）。
    protected void SetSpellVisual(BulletShape shape, Color tint, Texture2D? sprite = null, float spriteRot = 55f)
    {
        CurShape = shape; CurTint = tint; CurTintSet = true;
        CurSprite = sprite; CurSpriteRot = spriteRot;
    }
    // 敵弾の「出どころ」＝見えている体の中心（2026-09-28）。
    //   当たり判定の芯（GlobalPosition）に、立ち絵が動いているぶん（_bodySprite.Position＝ボスの呼吸/浮遊、
    //   道中ザコの生命感モーション）を足した点。＝弾はいつでも絵の真ん中から出る。
    // ★BossParts の姿勢オフセット（_bodySprite.Offset）は足さない：あれは「姿勢ごとに絵の枠が変わるぶん
    //   足元を揃える」補正で、体そのものは動いていない（足すと攻撃姿勢の一瞬だけ出どころがずれる）。
    public Vector2 ShotCenter => _hasBodyTex && _bodySprite != null
        ? GlobalPosition + _bodySprite.Position
        : GlobalPosition;

    // 現在のスペルの弾形・色（と絵）で敵弾を1発撃つ（各ボスの pool.Spawn 置き換え用）。
    //   湧き位置は必ず ShotCenter。パターンが指定した発射点(pos)へは、弾自身が短い導入区間で
    //   飛んでいってから本来の速度・軌道へ移る（Bullet.MakeLeadIn）。ずれが 3px 未満なら導入は付かない
    //   ＝中心から撃っている大多数のパターンは従来と完全に同一。
    //   fromCenter:false は「その場所に出ること自体が意味を持つ」置き弾（配膳の格子・上端からの雨・
    //   ぶら下げる祈り弾）専用。体から出す演出を付けるとギミックが壊れるものだけに使う。
    protected Bullet FireBullet(BulletPool pool, Vector2 pos, Vector2 vel, float radius = 3.4f, int dmg = 1,
        bool fromCenter = true)
    {
        var b = pool.Spawn(fromCenter ? ShotCenter : pos, vel, isEnemy: true, radius, dmg,
            CurShape, CurTintSet ? CurTint : (Color?)null);
        if (fromCenter) b.MakeLeadIn(pos);
        if (CurSprite != null) b.SetSprite(CurSprite, CurSpriteRot);
        // 改心後の遅延発射は表示・衝突させずに返す（撃破の瞬間に消した弾が後追いで湧かない）。
        //   BulletPool.Spawn の BubblePaused と同じ作法＝呼び元が b を触っても落ちない。
        if (_purified || GaugeVulnerable || GaugeReforming) pool.Despawn(b);
        return b;
    }

    // 難易度別HPバー本数（総HP=BarHp×本数）。派生ボスが OnEnemyReady で BarCount に設定する。
    protected int DiffBars(bool finalBoss) =>
        GetNodeOrNull<GameManager>("/root/Game")?.DiffBarBonus(finalBoss) ?? (finalBoss ? 12 : 8);

    // 難易度に応じた弾数。派生ボスが弾幕パターンの本数を安全にスケールするために使う。
    protected int Dn(int baseCount) =>
        GetNodeOrNull<GameManager>("/root/Game")?.ScaleBullets(baseCount) ?? baseCount;

    // 自機狙いの扇はこちら（必ず奇数＝中心の1本が自機の正面を通る）。Dn は偶数を返しうる＝
    // 中心線が空き、棒立ちの自機の両脇を弾がすり抜ける（GameManager.ScaleBulletsOdd のコメント参照）。
    protected int DnOdd(int baseCount) =>
        GetNodeOrNull<GameManager>("/root/Game")?.ScaleBulletsOdd(baseCount) ?? (baseCount | 1);

    // 難易度に応じた発射間隔。やさしいほど長く（連射が遅く）なる。
    // 派生ボスは `_fireT >= Di(基準秒)` の形でしきい値に掛けて使う。
    protected double Di(double baseInterval) =>
        baseInterval * (GetNodeOrNull<GameManager>("/root/Game")?.DanmakuIntervalMul ?? 1f);

    private void SetupBodySprite()
    {
        if (string.IsNullOrEmpty(PreTexPath)) return;
        var t = ResourceLoader.Load<Texture2D>(PreTexPath);
        if (t == null) return;
        _hasBodyTex = true;
        _artFacesRight = FaceLeft;   // 素材の基準向きを控える（以後 FaceLeft はこの意味でしか使わない）
        _bodySprite = new Sprite2D
        {
            Name = "Body",
            Texture = t,
            Centered = true,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear, // 高解像度素材を滑らかに縮小
            ZIndex = -1, // パネルより奥
            FlipH = FaceLeft, // 出現時は自機側(左)を向く＝右向き素材だけ反転（TickFacing が毎フレーム引き直す）
        };
        float s = BodyDisplayH / t.GetHeight() * ResolveBodyFrame(t).Scale;
        _baseScale = s;
        _bodySprite.Scale = new Vector2(s, s);
        ApplyBodyOffset();
        AddChild(_bodySprite);
    }

    // 本体（と改心差し替え中の退避スプライト）へ Tint の打ち消しを入れる。α は 1 固定＝
    // クロスフェード（SelfModulate のα）や退場フェード（Enemy 自身の Modulate のα）には触らない。
    private void ApplyTintLift()
    {
        if (!_hasBodyTex || _bodySprite == null) return;
        var lift = TintLift.Of(_worldTint, TintLift.EnemyBody);
        _bodySprite.Modulate = lift;
        if (_fadeSprite != null) _fadeSprite.Modulate = lift;
    }

    protected virtual (float Scale, Vector2 Offset) GetBodyFrame(Texture2D texture)
        => (1f, BossParts.BodyOffsetFor(BodyOffsetName, _bodyPose));

    protected virtual (float Scale, Vector2 Offset) ResolveBodyFrame(Texture2D texture)
        => BossAnimalArt.Contains(texture) ? BossAnimalArt.Frame(texture)
            : BossDownArt.IsBreathing(texture) || texture.ResourcePath == DownTexPath || texture.ResourcePath == Form2DownTexPath
            ? BossDownArt.Frame(texture) : GetBodyFrame(texture);

    private void SetDownBody(bool down)
    {
        if (string.IsNullOrEmpty(DownTexPath)) return;
        _bodyDown = down;
        _downBreathT = 0;
        _attackPoseT = 0;
        if (_parts != null) _parts.Visible = !down && !BossAnimalArt.Contains(_bodySprite.Texture);
        if (!down) SetBodyPose(_form2 ? BossParts.Pose.Form2 : BossParts.Pose.Idle);
        if (down)
        {
            _downBreathFrames = new[]
            {
                GD.Load<Texture2D>(BossDownArt.BreathingPath(DownTexPath, 0)),
                GD.Load<Texture2D>(BossDownArt.BreathingPath(DownTexPath, 1)),
                GD.Load<Texture2D>(BossDownArt.BreathingPath(DownTexPath, 2)),
            };
            ApplyBossMotion(Vector2.Zero, 0, _bodySprite.FlipH);
            SwapBody(_downBreathFrames[0].ResourcePath);
        }
        else SwapBody(PreTexPath);
    }

    private void TickDownBreathing(double delta)
    {
        if (!_bodyDown || _swapAnim || _downBreathFrames == null) return;
        _downBreathT += delta;
        float beat = (float)(_downBreathT % .94);
        int frame = beat < .16f ? 0 : beat < .30f ? 1 : beat < .46f ? 2 : beat < .68f ? 1 : 0;
        SetMotionFrame(_downBreathFrames[frame]);
    }

    // 現在の姿勢（_bodyPose）のオフセットを本体スプライトへ入れる。
    // FlipH は Offset の x も一緒に反転させるので、反転時は符号を戻して見た目の位置を合わせる。
    private void ApplyBodyOffset()
    {
        if (_bodySprite == null) return;
        Vector2 o = ResolveBodyFrame(_bodySprite.Texture).Offset;
        // 縮めた絵の足元合わせ：素材はどれも足元まで詰めてある（不透明域が下端）ので、
        // 中央基準のまま倍率を下げると足元が (1-倍率)/2 ぶん浮く。その差を Offset(画像画素) で押し下げる。
        //   浮き = 表示高×(1-倍率)/2 [画面px] → 画像画素に直すと 高さ×(1-倍率)/(2×倍率)。
        if (_bodyScaleMul < 1f && _bodySprite.Texture is { } tex)
            o.Y += tex.GetHeight() * (1f - _bodyScaleMul) / (2f * _bodyScaleMul);
        _bodySprite.Offset = _bodySprite.FlipH ? new Vector2(-o.X, o.Y) : o;
    }

    // 姿勢を切り替える（絵の差し替えとは別＝オフセットだけ。差し替えは SwapBody が担う）。
    protected void SetBodyPose(BossParts.Pose pose)
    {
        _bodyPose = pose;
        ApplyBodyOffset();
    }

    // 攻撃の一拍：本体を攻撃絵へ差し替え、AttackPoseDur 秒後に待機へ戻す（TickAttackPose）。
    // 併せて部品層に「発射」を伝える（予備動作→前方へ流す）。素材が無ければ何もしない。
    protected void TriggerAttackPose()
    {
        if (_bodyDown) return;
        _parts?.OnAttackStart();
        if (string.IsNullOrEmpty(AttackTexPath) || _purified || _crying) return;
        if (_attackPoseT > 0)
        {
            _attackPoseT = AttackPoseDur;
            SetBodyPose(BossParts.Pose.Attack);
            SetMotionFrame(ResourceLoader.Load<Texture2D>(AttackTexPath));
            return;
        }
        _attackPoseT = AttackPoseDur;
        SetBodyPose(BossParts.Pose.Attack);
        SwapBody(AttackTexPath);
    }

    // 攻撃姿勢の残り時間を消化し、切れたら待機へ戻す。
    // ★改心に入った後は待機へ戻さない：攻撃の一拍の最中に撃破されると（＝弾を吐いている所を
    //   撃ち抜く＝ごく普通の倒し方）_attackPoseT が残ったまま Redeem が cry へ差し替え、その数フレーム後に
    //   ここが切れて PreTexPath へ上書きしてしまう＝改心の会話中だけ穢れの絵に戻る。
    //   TriggerAttackPose 側と同じ条件で弾き、絵は cry/post のまま据え置く。
    private void TickAttackPose(double delta)
    {
        if (_attackPoseT <= 0) return;
        _attackPoseT -= delta;
        if (_attackPoseT > 0)
        {
            if (_attackPoseT <= 0.2 && !_purified && !_crying && !_bodyDown && RecoveryTexPath.Length > 0)
            {
                SetBodyPose(BossParts.Pose.Idle);
                SetMotionFrame(_form2 ? _animalFrames[5] : ResourceLoader.Load<Texture2D>(RecoveryTexPath));
            }
            return;
        }
        _attackPoseT = 0;
        if (_purified || _crying || _bodyDown) return;
        SetBodyPose(_form2 ? BossParts.Pose.Form2 : BossParts.Pose.Idle);
        SwapBody(PreTexPath);
    }

    protected void SetMotionFrame(Texture2D texture)
    {
        if (_bodySprite.Texture == texture) return;
        if (BossAnimalArt.Contains(_bodySprite.Texture) != BossAnimalArt.Contains(texture))
            _bodySprite.FlipH = !_bodySprite.FlipH;
        _bodySprite.Texture = texture;
        _baseScale = BodyDisplayH / texture.GetHeight() * _bodyScaleMul * ResolveBodyFrame(texture).Scale;
        _bodySprite.Scale = Vector2.One * _baseScale;
        ApplyBodyOffset();
    }

    protected virtual void TickBodyMotion(double delta)
    {
        if (!_form2 || !BodyMotionReady || BodyAttacking) return;
        _animalMotionT += delta;
        SetMotionFrame(_animalFrames[_animalWindup ? 4 : (int)(_animalMotionT / 0.16) % 4]);
    }

    protected bool TickForm2Technique(double delta, BossMover mover, AreaSpellCaster caster)
    {
        if (!_form2) return false;
        if (AnimalTechniqueActive) return true;
        _animalTechniqueCooldown -= delta;
        if (_animalTechniqueCooldown > 0 || caster.AoeActive || caster.EdgeAttackActive || caster.Suppressed) return false;
        caster.CancelPendingAttacks();
        mover.DeclareAttack(BossMover.Attack.Spell);
        _animalTechnique = BossAnimalTechnique.Begin(this, AnimalId, caster);
        _animalTechniqueCooldown = Di(8.0);
        return true;
    }

    internal void SetAnimalWindup(bool active)
    {
        _animalWindup = active;
        if (active) _attackPoseT = 0;
    }

    internal void ShowAnimalAttack()
    {
        _animalWindup = false;
        TriggerAttackPose();
    }

    internal void CancelAnimalTechnique()
    {
        if (AnimalTechniqueActive) _animalTechnique!.Cancel();
        _animalTechnique = null;
        _animalWindup = false;
    }

    // 形態変化の見せ場。通常難易度は BossTransformation（World を3秒止める演出）、
    //   ルナティックは戦闘を止めない合図（BossTransformation.FlashForm。理由はそちらのコメント）に差し替える。
    //   ルナティックでは _transformation を持たない＝Transforming が立たない＝この瞬間も被弾も与弾も通る。
    //   FinishFormReveal も呼ばない：止まっていないので squash→pop は自前の尺で流れ切る。
    //   Akari／Koharu／Rei の第二形態（AdvanceForm2）とミナの龍（BossMina.CompletePhaseTransition）の
    //   両方がここを通るので、4体ぶんの切り分けがこの1点に集まる。
    protected void RevealForm(BossTransformation.Frame previous, System.Action? completed = null)
    {
        if (GameManager.LunaticActive)
        {
            BossTransformation.FlashForm(this);
            completed?.Invoke();
            return;
        }
        _transformation = BossTransformation.Play(this, previous, completed);
    }

    internal void FinishFormReveal() => TickSwapAnim(1d);

    private void SpawnPanels()
    {
        for (int i = 0; i < PanelCount; i++)
            SpawnOnePanel(Mathf.Tau * i / Mathf.Max(1, PanelCount));
        if (_maxHp > 0) OnShieldFormed();
    }

    protected virtual void OnShieldFormed() { }

    public virtual UnfolderKind UnfolderStyle => UnfolderKind.None;
    public virtual int UnfolderStage => 0;
    public bool UnfoldersActive => !_purified && !_entering && IsVisibleInTree() && _phase == BossPhase.Shielded;

    public Panel? SelectUnfolder(Vector2 from, Panel? current, Bullet? shot = null)
    {
        if (!UnfoldersActive) return null;
        bool Eligible(Panel p) => p.CanLock && (shot == null || !shot.HasChargeHit(p));
        if (current != null && IsInstanceValid(current) && _panels.Contains(current) && Eligible(current))
            return current;
        Panel? best = null;
        float nearest = float.MaxValue;
        foreach (var panel in _panels)
        {
            if (!Eligible(panel)) continue;
            float distance = panel.GlobalPosition.DistanceSquaredTo(from);
            if (distance < nearest) { best = panel; nearest = distance; }
        }
        return best;
    }

    private void SpawnOnePanel(float baseAngle)
    {
        var p = new Panel();
        p.Setup(this, baseAngle, OrbitRadius, SpinSpeed, PanelsFire, PanelFireInterval, PanelInk, PanelTexPath, PanelDisplayScale);
        AddChild(p);
        _panels.Add(p);
    }

    // パネルが砕けた通知。
    // 通常敵：全部剥がれたら浄化。
    // ボス(HPバー方式)：本体HPは減らさない＝残数カウントのみ。0枚で BREAK へ遷移し、無防備窓を開く。
    public void OnPanelStripped(Panel p)
    {
        _panels.Remove(p);

        if (_maxHp > 0)
        {
            if (_purified) return;
            // SHIELDED 中に全パネルを剥がし切ったら BREAK（合図）へ。
            // BREAK/EXPOSED 中はパネルが無いので通常ここには来ないが、保険で残数だけ見る。
            if (_phase == BossPhase.Shielded && _panels.Count == 0)
                EnterBreak();
            else
                QueueRedraw();
            return;
        }

        if (_panels.Count == 0 && !_purified)
            Redeem();
        else
            QueueRedraw();
    }

    // ─── 無防備窓サイクルのフェーズ遷移 ───
    private void EnterBreak()
    {
        _phase = BossPhase.Break; _phaseT = 0;
        var pool = GetNode<BulletPool>("/root/Pool");
        foreach (var node in GetTree().GetNodesInGroup("enemy_bullets"))
            if (node is Bullet bullet && bullet.Active) pool.Despawn(bullet);
        OnShieldBroken();
        SetDownBody(true);
        _shieldLayer?.QueueRedraw();
        FxLayer.Instance?.BossBreak(GlobalPosition, BodyDisplayH);
        GameCamera.Instance?.Shake(1.6f, 0.14f);
        Audio.Instance?.PlaySpell();
        // 祈り手（Heal）だけ BREAK 成立ごとに BOMB+1（設計書 §2）。他ジョブでは何も起きない。
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyBossBreak();
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.Bubbles?.ShieldBreak.Show(this,
            GameManager.Instance?.SelectedJob ?? Job.Tank);
        QueueRedraw();
    }

    protected virtual void OnShieldBroken() { }

    private void EnterExposed()
    {
        _phase = BossPhase.Exposed; _phaseT = 0;
        _shieldLayer?.QueueRedraw();
        _windowDamage = 0;            // 窓キャップを新しい窓ぶんリセット
        _windowCapNotified = false;
        _bodyHitCd = 0;
        FxLayer.Instance?.BossExpose(GlobalPosition, BodyDisplayH);
        GameCamera.Instance?.Shake(0.9f, 0.10f);
        // 無防備窓：本体が自機弾を拾うよう監視・マスクを開く（衝突中の変更は遅延設定）。
        SetDeferred(Area2D.PropertyName.Monitoring, true);
        SetCollisionMaskValue(2, true); // 自機弾 layer=2 を拾う
        if (_bodyShape != null)
            _bodyShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, false);
        QueueRedraw();
    }

    private void EnterReclose()
    {
        _phase = BossPhase.Reclose; _phaseT = 0;
        if (_bodyDown) SetDownBody(false);
        _shieldLayer?.QueueRedraw();
        FxLayer.Instance?.BossReclose(GlobalPosition, BodyDisplayH);
        GameCamera.Instance?.Shake(0.7f, 0.08f);
        // 本体を再び無敵化（自機弾を拾わない）。
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        SetCollisionMaskValue(2, false);
        OnRecloseLine(); // 派生：キャラ別の弱気セリフを最短表示
        QueueRedraw();
    }

    private void EnterShielded()
    {
        _phase = BossPhase.Shielded; _phaseT = 0;
        _shieldLayer?.QueueRedraw();
        if (_panels.Count == 0)
        {
            SpawnPanels(); // パネル一括再生成
            FxLayer.Instance?.BossShieldReform(GlobalPosition, BodyDisplayH);
        }
        QueueRedraw();
    }

    protected void RallyShield()
    {
        // A new story beat must not inherit the previous beat's damage window.
        if (!_purified && _maxHp > 0 && _phase is BossPhase.Break or BossPhase.Exposed)
            EnterReclose();
    }

    // 無防備窓中：本体に当たった自機弾の威力ぶん本体HPを削る。
    private void OnBodyHitByPlayerBullet(Area2D area)
    {
        if (_phase != BossPhase.Exposed || _purified || Transforming) return;
        if (area is Bullet b && !b.IsEnemy && b.Active)
        {
            if (!b.RegisterChargeHit(this)) return;
            // 連鎖の光（chain_light）：消費位置から最寄りの別の敵へ跳弾（Despawn 前＝位置と威力が生きているうちに）。
            b.TryChain(this);
            // 貫く光（shot_pierce）：残貫通数のある弾は消えずに突き抜ける（ダメージ処理はそのまま通す）。
            if (b.Pierce > 0) b.Pierce--;
            else GetNodeOrNull<BulletPool>("/root/Pool")?.Despawn(b);
            // 集中の光（focus_fire）：同一敵への連続ヒットを自機側で計上（対象が変わるとリセット）。
            (GetTree().GetFirstNodeInGroup("player") as Player)?.NotifyShotHit(this);
            OnPlayerDealtDamage(); // 「攻めている」の通知（キャップ/CD で削れないヒットも攻めは攻め）

            // 窓キャップ到達後は、この窓では本体HPを削らない（弾の消滅は上で済ませ撃ち心地は残す）。
            // 到達の瞬間だけ "MAX" を1回出して「これ以上は次の窓で」を伝える。
            if (_windowDamage >= WindowCap)
            {
                if (!_windowCapNotified)
                {
                    _windowCapNotified = true;
                    FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -12), "MAX", FxLayer.Gold, 13);
                }
                return;
            }
            // 本体ヒットのクールダウン中は削らない（同一フレーム多重弾の過剰削りを軽く抑える補助）。
            if (_bodyHitCd > 0 && !b.Charged) return;

            // Charge bypasses the rapid-fire cap, but still respects the window and story HP floors.
            int dmg = Mathf.Clamp(b.Damage, 1, b.Charged ? 32 : 8);

            // 距離ボーナス（設計書 §2・ジョブ差の本体）。自機が取れない場合は base のまま（null安全）。
            //   近: Jobs.CloseRange(48px) 以内 → JobDef.CritMult / CritCap（灯し手 ×2.0・上限8／他 ×1.25・上限5）。
            //       語り手は CritEnabled=false＝密着しても何も起きない。
            //   遠: Jobs.FarRange(120px) より遠い → JobDef.FarMult（語り手のみ ×1.3）。近接の鏡像。
            //   両者は排他（48以内 or 120超のどちらか。48〜120 は素の威力）。
            var job = GetNodeOrNull<GameManager>("/root/Game")?.JobDef;
            bool crit = false;
            if (GetTree().GetFirstNodeInGroup("player") is Player pl)
            {
                float d = GlobalPosition.DistanceTo(pl.GlobalPosition);
                if (d <= PointBlankRange && (job?.CritEnabled ?? true))
                {
                    crit = true;
                    dmg = Mathf.Min(b.Charged ? 32 : job?.CritCap ?? 5, Mathf.RoundToInt(dmg * (job?.CritMult ?? 1.25f)));
                }
                else if (d > Jobs.FarRange && (job?.FarMult ?? 1f) > 1f)
                {
                    crit = true; // 表示は密着クリと同じ金色＝「今の距離が効いている」を同じ語彙で返す
                    dmg = Mathf.RoundToInt(dmg * job!.FarMult);
                    if (b.Charged) dmg = Mathf.Min(dmg, 32);
                }
                // QA走行だけ、ジョブの距離ボーナスが実際に効いたかを1ヒットずつログへ出す（検証用）。
                if (crit && QaPilot.Verbose)
                    GD.Print($"[JOB] {job?.CharacterName} dist={d:0}px raw={b.Damage} -> {dmg} "
                           + (d <= PointBlankRange ? $"(close x{job?.CritMult:0.00} cap{job?.CritCap})" : $"(far x{job?.FarMult:0.00})"));
            }

            // 窓キャップ：残り許容ぶんへクランプ（密着クリティカルは上限を超えず到達を早めるだけ）。
            dmg = LimitBodyDamage(Mathf.Min(dmg, WindowCap - _windowDamage));
            if (dmg <= 0) return;
            b.ChargeImpact(b.GlobalPosition);
            _windowDamage += dmg;
            _bodyHitCd = BodyHitCd;
            int prevBarsLeft = (_hp + BarHp - 1) / BarHp; // 減算前の残バー数（切り上げ）
            _hp = Mathf.Max(0, _hp - dmg);
            // HPバー1本割れ（#26 フェーズ移行の可視化）：バー境界を跨いだ一拍を
            // 白フラッシュ＋バー発光＋スペル音＋squash→pop で「モードが進んだ」と読ませる。
            // 撃破（_hp==0）は Redeem 側の改心演出に譲る＝二重に鳴らさない。
            if (_hp > 0 && (_hp + BarHp - 1) / BarHp < prevBarsLeft)
                OnBarBroken();
            // クリティカルは金色＋一回り大きく＋"!" で「密着が効いている」を視認させる（通常は既存色）。
            if (ShowDamageNumbers)
            {
                if (crit)
                    FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(GD.Randf() * 8 - 4, -10), dmg + "!", FxLayer.Gold, 13);
                else
                    FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(GD.Randf() * 8 - 4, -8), dmg.ToString(), FxLayer.Sig2);
            }
            OnHpChanged();

            // 手応え：本体に当たった一発ごとに「効いてる」を即・短く返す（当たり判定は不変）。
            // ・発光リング(_Draw)＋本体ヒット専用SE(PlayBossHit)。大威力(>=3)は揺れも足して“刺さった”感を強調。
            //   PlayBossHit は中低域の「刺さる／ドスッ」＝剥離(PlayStrip)・自機被弾(PlayHit)と音域を分け混同回避。
            //   dmg>=3 では重い低音版が鳴り、下の GameCamera.Shake と同期して決定打が映える。
            //   密着クリティカルは dmg が上がるので重い音＋Shake が自然に発火し、リングも気持ち強める。
            _hitFlashT = HitFlashDur;
            _hitFlashMag = crit ? dmg + 2 : dmg;
            // 部品層にも「刺さった」を伝える＝カード・吹き出し・星が外へ散って戻る（見た目だけ）。
            // 改心へ入る一撃（_hp<=0）は下の Redeem 側の改心演出に譲る＝散らしてから消すと二度手間になる。
            if (_hp > 0) _parts?.OnHit();
            // 移動側にも「刺さった」を伝える＝BossMover が最大4pxのけぞって 0.26 秒で戻す（当たり判定の
            // 中心は動くが、時定数 0.12s で寄せるので1フレームで飛ばない＝弾避けの公平性は保つ）。
            // 弾の進行方向＝押される向き。速度が取れない場合は本体←弾の向きで代用する。
            if (_hp > 0)
            {
                Vector2 push = b.Velocity.LengthSquared() > 0.01f
                    ? b.Velocity.Normalized()
                    : (GlobalPosition - b.GlobalPosition);
                OnBodyDamaged(push);
            }
            Audio.Instance?.PlayBossHit(dmg);
            if (dmg >= 3) GameCamera.Instance?.Shake(1.6f, 0.10f);

            if (_hp <= 0)
            {
                // 窓中の本体撃破。Redeem は被弾シグナル中に走るため監視・形状の無効化は遅延される。
                // マスク書換も衝突シグナルのディスパッチ中（フラッシュ中）なので遅延化する。
                CallDeferred(MethodName.SetCollisionMaskValue, 2, false);
                Redeem();
                return;
            }
            QueueRedraw();
        }
    }

    // HPバーが1本割れた瞬間の一拍（#26）。姿勢の所有権は動かさず、既存の squash→pop
    //（_swapAnim。ApplyBossMotion と喧嘩しない差し替えアニメ機構）を流用して体を一度だけ弾ませる。
    private void OnBarBroken()
    {
        var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
        hud?.Flash();             // 画面白フラッシュ（BREAK と同語彙＝「節目」の合図）
        hud?.FlashBossBarBreak(); // HPバー自体も白く光らせ「1本割れた」を視線先で読ませる
        Audio.Instance?.PlaySpell();
        if (_hasBodyTex) { _swapAnim = true; _swapAnimT = 0; } // 一拍の弾み（演出過多にしない＝これ以上足さない）
    }

    private bool _form2;
    protected bool IsForm2 => _form2;
    protected bool AdvanceForm2()
    {
        if (_form2) return false;
        if (_purified || _crying) return false;          // 改心の三段に割り込ませない
        if (string.IsNullOrEmpty(Form2TexPath)) return false;
        if (!ResourceLoader.Exists(Form2TexPath)) return false; // 絵が無ければ黙って起きない
        var previous = BossTransformation.Frame.Capture(_bodySprite);
        _form2 = true;
        _attackPoseT = 0;
        string[] poses = { "move_0", "move_1", "move_2", "move_3", "windup", "recover" };
        _animalFrames = new Texture2D[poses.Length];
        for (int i = 0; i < poses.Length; i++)
            _animalFrames[i] = ResourceLoader.Load<Texture2D>(BossAnimalArt.Path(AnimalId, poses[i]));

        PreTexPath = Form2TexPath;
        if (Form2AttackTexPath.Length > 0) AttackTexPath = Form2AttackTexPath;
        if (!string.IsNullOrEmpty(Form2DownTexPath)) DownTexPath = Form2DownTexPath;
        if (_bodyDown)
        {
            SetDownBody(true);
        }
        else
        {
            SetBodyPose(BossParts.Pose.Form2);
            SwapBody(PreTexPath);
        }

        _parts?.EnterForm2();
        if (_parts != null && BossAnimalArt.Contains(_bodySprite.Texture)) _parts.Visible = false;

        RevealForm(previous);
        // 第二形態への切り替わり＝盤面の仕切り直し。BossTransformation が World を3秒止めて弾を全消しし、
        //   明けると敵は新しいガワで徘徊を始める。自機だけ前の形態の位置に残るのが非対称なので、ここで
        //   初期位置へ戻す（あかり／こはる／レイの3ボスが Form2TexPath を持つ＝3面ぶんをこの1点でまとめる）。
        //   難易度は問わない：ルナティックでも形態変化は盤面の仕切り直しなので、ここは戻す。
        //   実際に滑るのは変身演出が明けて World が動き出したフレーム（Player.ReturnToStart 参照）。
        //   ルナティックは演出を出さない（RevealForm 参照＝World も弾もそのまま）ので、呼ばれたフレームから
        //   すぐ滑り出す。弾を掃く経路が無いぶんは帰還に重ねる無敵（Player.ReturnToStart）が引き受ける
        //   ＝ミナの段替わり（BossMina.BeginPhaseTransition）と同じ扱い。
        Player.SendToStart(this);
        return true;
    }

    // 外部（ボム等）から強制浄化。
    // ボス(HPバー方式)はボムで即浄化しない：SHIELDED 中は今あるパネルを全砕き→ BREAK を誘発するだけ。
    // EXPOSED（無防備窓）中は「ボム直撃」＝ BombStrikeBase×BombPowerMul を窓キャップ内で本体HPへ通す
    //（ボム威力強化の作用先。合図/RECLOSE 中は従来どおり何も起きない）。
    //   virtual：トレーニングのダミー(TrainingDummy)がボム直撃を自前HPへ通すために上書きする（本編挙動は不変）。
    public virtual void Purify()
    {
        if (_purified || Transforming) return;
        if (_maxHp > 0)
        {
            if (_phase == BossPhase.Exposed) { BombStrike(); return; }
            foreach (var p in new List<Panel>(_panels))
                p.Shatter();
            return;
        }
        // 雑魚：ボム由来の印を先に立ててから剥がす。最後の1枚の Shatter が
        // OnPanelShattered 経由で即 Redeem を呼ぶ経路（=大半の個体）も確実に「ボム由来」として拾うため。
        _bombPurify = true;
        foreach (var p in new List<Panel>(_panels))
            p.Shatter();
        if (!_purified) Redeem();
    }

    // ボム直撃（EXPOSED 限定）：基礎 BombStrikeBase × BombPowerMul(1+0.25×Lv)。
    // 弾ヒットと同じ帳簿（窓キャップ・バー割れ・OnHpChanged・Redeem）を通す＝「1窓即殺」抑止の設計を壊さない。
    // Shop の効果表示（Eff）はこの定数から算出するので、変えるときは片方だけにしない。
    public const int BombStrikeBase = 20;
    private void BombStrike()
    {
        if (Transforming) return;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        int dmg = Mathf.RoundToInt(BombStrikeBase * (game?.BombPowerMul ?? 1f));
        dmg = Mathf.Min(dmg, WindowCap - _windowDamage); // 窓キャップの残り許容内でだけ通す
        if (dmg <= 0)
        {
            // キャップ到達済み：弾ヒットと同じ「MAX」ワンショットで「次の窓で」を伝える。
            if (!_windowCapNotified)
            {
                _windowCapNotified = true;
                FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -12), "MAX", FxLayer.Gold, 13);
            }
            return;
        }
        dmg = LimitBodyDamage(dmg);
        if (dmg <= 0) return;
        _windowDamage += dmg;
        int prevBarsLeft = (_hp + BarHp - 1) / BarHp;
        _hp = Mathf.Max(0, _hp - dmg);
        if (_hp > 0 && (_hp + BarHp - 1) / BarHp < prevBarsLeft)
            OnBarBroken();
        // 金色・大きめの数字＝「ボムが刺さった」を通常ヒットと見分けさせる（数値表示は 2026-09-17 に非表示）。
        if (ShowDamageNumbers)
            FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -10), dmg.ToString(), FxLayer.Gold, 15);
        OnHpChanged();
        _hitFlashT = HitFlashDur;
        _hitFlashMag = 4f;
        Audio.Instance?.PlayBossHit(4);
        GameCamera.Instance?.Shake(1.6f, 0.10f);
        if (_hp <= 0)
        {
            // Redeem 内の監視停止と揃え、マスク書換は遅延化（ボム経路でも作法を統一）。
            CallDeferred(MethodName.SetCollisionMaskValue, 2, false);
            Redeem();
            return;
        }
        QueueRedraw();
    }

    // 外部ギミックからHPバー方式ボスの本体HPを直接削る固定ダメージ経路
    //（あかり戦「言葉の残滓」の撃破報酬＝総HPの4%直撃）。無防備窓・窓キャップとは独立＝SHIELDED中でも通る。
    // バー割れ演出・OnHpChanged・Redeem は弾ヒット/ボム直撃と同じ帳簿を通す（設計を二重化しない）。
    public void DealDirectDamage(int dmg)
    {
        if (_purified || Transforming || _maxHp <= 0 || dmg <= 0) return;
        dmg = LimitBodyDamage(dmg);
        if (dmg <= 0) return;
        int prevBarsLeft = (_hp + BarHp - 1) / BarHp;
        _hp = Mathf.Max(0, _hp - dmg);
        if (_hp > 0 && (_hp + BarHp - 1) / BarHp < prevBarsLeft)
            OnBarBroken();
        if (ShowDamageNumbers)
            FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -10), dmg.ToString(), FxLayer.Gold, 15);
        OnHpChanged();
        if (_hp <= 0)
        {
            CallDeferred(MethodName.SetCollisionMaskValue, 2, false);
            Redeem();
            return;
        }
        QueueRedraw();
    }

    // パネル（言葉の盾）を一時的に不可侵にする（あかり戦「雨の帰り道」：ボスが画面外へ退場している間、
    // 流れ弾やボムでパネルが剥がれて BREAK が空撃ちされる事故を防ぐ）。演出退場するボスの専用スイッチ。
    public void SetPanelsInvulnerable(bool v)
    {
        foreach (var p in _panels) p.Invulnerable = v;
    }

    // 本体の接触判定を一時的に切る（あかり戦「雨の帰り道」：退場と高速帰還で場を横切る間、
    // 通路を縫っている自機を「轢く」のを防ぐ。演出で速く動く区間は当たらない＝理不尽を断つ）。
    // 衝突処理中に呼ばれても安全なよう遅延設定で書く（Redeem/EnterExposed と同じ作法）。
    public void SetBodyContactEnabled(bool v)
    {
        _bodyShape?.SetDeferred(CollisionShape2D.PropertyName.Disabled, !v);
    }

    protected virtual void OnRecloseLine() { }

    // RECLOSE セリフを表示するヘルパー（派生から呼ぶ）。サイクルごとに index を進め、超えたら最後を使い回す。
    protected void ShowRecloseLine(string speaker, string text)
    {
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?
            .ShowBossLine(speaker, text, UiKit.Kegare, RecloseLineDur);
    }

    // ボス徘徊の“見た目だけ”の演出を立ち絵(_bodySprite)へ適用する（BossMover 経由）。
    // visualOffset=呼吸/浮遊の微小オフセット、lean=進行方向への傾き(rad)、faceLeft=向き、
    // squash=反転の瞬間の縮み倍率（BossMover.SquashScale。既定＝(1,1)＝縮まない）。
    // ★当たり判定（本体 Area2D の GlobalPosition と _bodyShape）は一切動かさない＝弾避けの公平性を保つ。
    // 立ち絵が無い（プレースホルダ図形の）ボスでは何もしない。
    protected void ApplyBossMotion(Vector2 visualOffset, float lean, bool faceLeft, Vector2? squash = null)
    {
        AutoBank = false; // 姿勢はこちら(BossMover.Lean)が握る＝基底の自動バンクと競合させない
        FacePlayer = false; // 向きも同じくこちら（BossMover.TickFacing＝40px/0.6秒の保持つき）が握る
        if (!_hasBodyTex || _bodySprite == null) return;
        if (_bodyDown)
        {
            visualOffset = Vector2.Zero;
            lean = 0;
            squash = Vector2.One;
        }
        _motionOffset = visualOffset; // 呼吸/浮遊。pop の持ち上げはこれへ加算するため保持。
        // 差し替えアニメ中は _PhysicsProcess 側が Position/Scale を握る（pop の持ち上げを潰さない）。
        if (!_swapAnim)
        {
            _bodySprite.Position = visualOffset;
            // 反転の縮み。差し替えアニメ（squash→pop）と Scale を取り合わないよう、走っていない間だけ
            // ここが握る。倍率が 1 のときも毎フレーム基準スケールへ戻す＝縮みが残り続けない。
            Vector2 sq = squash ?? Vector2.One;
            _bodySprite.Scale = new Vector2(_baseScale * sq.X, _baseScale * sq.Y);
        }
        _bodySprite.Rotation = lean;
        if (_bodySprite.FlipH != faceLeft) { _bodySprite.FlipH = faceLeft; ApplyBodyOffset(); }
        _parts?.SetFlip(faceLeft);
    }

    // ─── 部品の演出層（BossParts）───
    //   v3 の本体絵はエフェクトを持たないので、輪・カード・光は BossParts が実行時に重ねて動かす。
    //   派生ボスが OnEnemyReady のあとで AttachParts を呼ぶと本体の子として1個ぶら下がる。
    //   当たり判定は一切持たない＝見た目だけ。素材が無い人物では null のまま（呼び出しは全部素通り）。
    private BossParts? _parts;
    protected BossParts? Parts => _parts;

    // 部品層を取り付ける。基準点（足元中央・発射点）は BossParts の実測表から引く。
    // idleTexW/attackTexW は待機・攻撃の本体画像の幅（720px 基準）＝中心基準への読み替えに要る。
    protected void AttachParts(string name, float idleTexW, float attackTexW)
    {
        if (_parts != null) return;
        var p = new BossParts { Name = "Parts" };
        AddChild(p);
        p.Configure(name,
                    BossParts.AnchorFoot(name, BodyDisplayH, idleTexW),
                    BossParts.AnchorMuzzle(name, BodyDisplayH, attackTexW),
                    BodyDisplayH);
        p.SetFlip(FaceLeft);
        _parts = p;
    }

    // HPが変化した（HUDバー更新用フック）。
    protected virtual void OnHpChanged() { }

    protected virtual int LimitBodyDamage(int damage) => damage;

    protected int DamageToHpFloor(int damage, float ratio)
        => Mathf.Clamp(damage, 0, Mathf.Max(0, _hp - Mathf.RoundToInt(_maxHp * ratio)));

    protected void ChangeBattleCostume(string idle, string attack, string down)
    {
        PreTexPath = idle;
        AttackTexPath = attack;
        DownTexPath = down;
        _attackPoseT = 0;
        if (_bodyDown) { SetDownBody(true); return; }
        SetBodyPose(BossParts.Pose.Idle);
        SwapBody(idle);
    }

    // 改心処理（消さない。味方化して残る）。
    private void Redeem()
    {
        if (_purified) return;
        if (_bodyDown && DeferCryBodySwap) SetDownBody(false);
        _bodyDown = false;
        _attackPoseT = 0;
        if (_parts != null) _parts.Visible = true;
        _purified = true;
        _entering = false;
        _shieldLayer?.QueueRedraw();
        RemoveFromGroup("enemies");

        // 戦闘終了の瞬間：残弾を片付ける（改心の会話に弾が飛び続けないように）。
        //   2026-09-22 ユーザー指示：ボス／中ボスは敵弾も同時に消す。撃破の瞬間から改心の一拍・会話まで
        //   数秒あり、その間も飛び続ける残弾で被弾していた。改心後の遅延発射は FireBullet 側で握りつぶす。
        //   ★ザコは何も消さない（2026-09-27 作者報告「ロックして照射、倒して切り替わった瞬間に球が消えて、
        //     また一から設置している」）。以前はザコ1体ごとに盤面の自機弾を全消ししていた（溜め弾だけ残す）ため、
        //     引き継ぎ先へ飛んでいた弾・あかりのタメ中の加速球（自機の前に「設置」された球）が撃破のたびに消え、
        //     撃ち込みが毎回ゼロからになっていた。ザコには改心の会話が無い＝片付ける理由が無い
        //     （敵弾を消さないのも従来どおり＝撃破ごとに盤面の弾幕が消えると道中が成立しない）。
        //     会話へ入る区切り（ウェーブ明け・考察）は各 Stage が DespawnAll を自前で呼んでいる。
        if (PurifyGrade != FxLayer.PurifyTier.Zako) GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();

        // 接触で自機を傷つけないようにする。浄化は被弾シグナル中に走ることがあるため遅延設定。
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        if (_bodyShape != null)
            _bodyShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);

        _flashing = true;
        _flashT = 0;

        // スコア＋コンボ（連鎖＝やさしさの広がり）。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        bool rewarded = game?.AddPurify(Points, _bombPurify) == true;
        PowerKind? power = rewarded && PurifyGrade == FxLayer.PurifyTier.Zako
            ? (GetTree().GetFirstNodeInGroup("player") as Player)?.CountPowerupKill() : null;
        // ★2026-09-17：インプレ（ショップ通貨）は撃破の瞬間ではなく「散った欠片を拾ったとき」に入る。
        //   基礎額は旧実装と同じ 2+Combo（AddPurify でコンボ加算済みの値を読む）。
        //   報酬なし（ボムキャップ超過）の浄化は 0＝欠片も通貨も付かない、という既存の線引きをそのまま守る。
        int impBase = rewarded ? (game?.PurifyImpressionBase ?? 0) : 0;

        // 浄化バースト演出＋やさしい言葉（バリエーション）＋浄化音（届いた余韻）
        // 改心が確定する一拍：止め(Hitstop)＋光(PurifyBurst)＋フラッシュ を同フレームで揃える。
        GameCamera.Instance?.Hitstop(HitstopDur);
        FxLayer.Instance?.PurifyBurst(GlobalPosition, rewarded ? Points : 0, PurifyGrade, impBase, ShardMul, power);
        Audio.Instance?.PlayPurify();
        // 浄化の一言（ありがとう等）は 2026-09-07 のユーザー指示で非表示。文言の作り直し案は
        // （記事 18_浄化の一言_案C は現存しない。差し戻すなら本文から書き直すこと。）

        // やさしさの波紋（連鎖浄化のトリガー）。
        // Redeem は被弾/パネル砕けのシグナル（物理クエリのフラッシュ中）から呼ばれることがある。
        // その最中に Ripple を即 AddChild すると Ripple._Ready が監視状態やコリジョン形状を
        // 物理フラッシュ中に書き換え（"Can't change this state while flushing queries"）、
        // 連鎖浄化が多発する場面で物理サーバを壊して落ちる。生成はフラッシュ後へ遅延する。
        var parent = GetParent();
        if (parent != null)
        {
            var ripple = new Ripple { Position = Position }; // 親が同じ＝同じ座標系のローカル位置をそのまま使う
            parent.CallDeferred(Node.MethodName.AddChild, ripple);
        }

        // 改心の着地は直立で（移動バンクの傾きを残さない）。差し替え前に戻す＝旧絵(fade)ごと素直に立つ。
        _bank = 0f;
        if (AutoBank && _hasBodyTex && _bodySprite != null) _bodySprite.Rotation = 0f;
        // 3段階対応：Cry の尺が設定されていれば先に大泣きを見せてから笑顔へ。
        // 専用立ち絵が無いボス（こはる等）でも会話に入れるよう、CryHoldDur のみで判定する
        //（SwapBody は内部で _hasBodyTex を確認するため、立ち絵が無ければ素通りする）。
        if (CryHoldDur > 0)
        {
            if (!DeferCryBodySwap) SwapBody(CryTexPath, CryBodyScale); // 遅延指定時は決定打まで Pre のまま
            // 部品層にも「撃破された」を伝える＝公転をやめて濃さが沈む（穢れが薄れる）。
            // 本体の cry 絵は待機絵と構図がほぼ同じなので、これが無いと会話中の見た目が戦闘中と変わらない。
            // 消し切りは FinishCry の OnRedeem が担う（部品が消え切ってから post、の順は不変）。
            _parts?.OnCry();
            _crying = true;
            _cryT = 0;
            _cryWatchdogT = 0; _cryTotalT = 0; // 保険タイマーはここが起点
            // 改心の会話中は戦闘テロップ（ボス字幕「シールドが、剥がれました!…」／スペルカットイン）を
            // 抑える。撃破直前に出た帯が決定打の行に重なって双方読めなくなるため（0.2s フェード→消去）。
            // 戻すのは FinishCry＝会話を送り切った時（保険タイムアウト経由でも同じ1経路を通る）。
            SetHudCalloutSuppressed(true);
            OnCryStart();
        }
        else
        {
            SwapBody(PostTexPath, PostBodyScale);
            GrantFollower();
        }

        QueueRedraw();
    }

    // 本体スプライトを“溶けるように”差し替える（クロスフェード＋squash→pop）。
    // 旧テクスチャを別 Sprite2D(_fadeSprite) に退避してα落とししつつ、本体(新)をα上げ。
    // 同時に squash→pop（Scale を一瞬ふくらませて弾み、見た目を少し持ち上げて戻す）を起動。
    // ★テクスチャ差し替え／再スケールの基準だけ確定し、実アニメは _PhysicsProcess(TickSwapAnim) が進める。
    // ★当たり判定は触らない：動かすのは _bodySprite と _fadeSprite の Transform/Modulate だけ。
    private void SwapBody(string path, float scaleMul = 1f)
    {
        if (!_hasBodyTex || string.IsNullOrEmpty(path)) return;
        var t = ResourceLoader.Load<Texture2D>(path);
        if (t == null) return;
        _bodyScaleMul = scaleMul <= 0f ? 1f : scaleMul;

        // 旧テクスチャをそのままの見た目で退避（同じ Transform/Flip/ZIndex）し、α落とし用に使う。
        var old = _bodySprite.Texture;
        if (old != null)
        {
            _fadeSprite?.QueueFree();
            _fadeBaseScale = _baseScale;
            _fadeSprite = new Sprite2D
            {
                Texture = old,
                Centered = true,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                ZIndex = _bodySprite.ZIndex,
                FlipH = _bodySprite.FlipH,
                Position = _bodySprite.Position,
                Rotation = _bodySprite.Rotation,
                Scale = _bodySprite.Scale,
                Offset = _bodySprite.Offset, // 旧姿勢のオフセットのまま消える＝入れ替わりで絵が跳ねない
            };
            AddChild(_fadeSprite);
        }

        // 本体を新テクスチャへ。基準スケールを更新し、α0 から上げ始める。
        if (old != null && BossAnimalArt.Contains(old) != BossAnimalArt.Contains(t))
            _bodySprite.FlipH = !_bodySprite.FlipH;
        _bodySprite.Texture = t;
        _baseScale = BodyDisplayH / t.GetHeight() * _bodyScaleMul * ResolveBodyFrame(t).Scale;
        _bodySprite.Scale = new Vector2(_baseScale, _baseScale);
        ApplyBodyOffset(); // 新しい姿勢の足元が待機と同じ画面位置に来るよう入れ直す
        _bodySprite.SelfModulate = new Color(1f, 1f, 1f, _fadeSprite != null ? 0f : 1f);
        ApplyTintLift();   // 退避スプライトは Modulate を引き継がないので、ここで新旧そろえて明るさを入れる

        // squash→pop を起動（_PhysicsProcess で進める）。
        _swapAnim = true;
        _swapAnimT = 0;
    }

    // squash→pop ＋ クロスフェードを 1 フレームぶん進める。
    // squash: BaseScale×SquashScale から BaseScale へ Back/Out 風に弾ませる。
    // pop:    見た目を PopLiftPx 持ち上げて戻す（呼吸オフセット _motionOffset に加算＝当たり判定は不変）。
    private void TickSwapAnim(double delta)
    {
        if (!_swapAnim) return;
        _swapAnimT += delta;
        float u = (float)Mathf.Clamp(_swapAnimT / SwapAnimDur, 0, 1);

        // Back/Out 風：行き過ぎてから戻す。t=0 で +(SquashScale-1)、t=1 で ±0 に収束。
        float over = BackOut(u);                 // 0→1（途中で >1 にオーバーシュート）
        float scaleMul = _bodyDown ? 1f : Mathf.Lerp(SquashScale, 1f, over);
        _bodySprite.Scale = new Vector2(_baseScale * scaleMul, _baseScale * scaleMul);

        // pop の持ち上げ：序盤に最大、終盤で 0（sin の山）。呼吸オフセットへ加算。
        float lift = _bodyDown ? -2f * (1f - u) : -PopLiftPx * Mathf.Sin(u * Mathf.Pi);
        _bodySprite.Position = _motionOffset + new Vector2(0f, lift);

        // クロスフェード：旧(_fadeSprite)をα落とし、新(_bodySprite)をα上げ。
        if (_fadeSprite != null)
        {
            float fa = (float)Mathf.Clamp(_swapAnimT / SwapFadeDur, 0, 1); // フェード進行
            _bodySprite.SelfModulate = new Color(1f, 1f, 1f, fa);
            _fadeSprite.SelfModulate = new Color(1f, 1f, 1f, 1f - fa);
            _fadeSprite.Scale = Vector2.One * (_fadeBaseScale * scaleMul);
            _fadeSprite.Position = _bodySprite.Position;
            if (fa >= 1f) { _fadeSprite.QueueFree(); _fadeSprite = null; }
        }

        if (u >= 1f)
        {
            _swapAnim = false;
            _bodySprite.Scale = new Vector2(_baseScale, _baseScale);
            _bodySprite.Position = _motionOffset;
            _bodySprite.SelfModulate = Colors.White;
            if (_fadeSprite != null) { _fadeSprite.QueueFree(); _fadeSprite = null; }
        }
    }

    // Back/Out 風イージング（行き過ぎてから 1 へ収束）。
    private static float BackOut(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    // 救った人を algo のフォロワー（味方オプション）にする。派生で上書き可（ボス＝ヒカゲ強化）。
    protected virtual void GrantFollower()
    {
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Player pl)
            _becameFollower = pl.AddFollower(GlobalPosition); // フォロワー化したら本体は退場せず引き継ぐ
    }

    // 大泣き演出の開始／終了フック（派生でセリフ等に使う）。
    // 改心の会話区間だけ戦闘テロップを鎮める（Hud.SuppressCallouts の ON/OFF）。
    // Hud が取れない状況（テスト起動など）でも落とさない＝取れなければ何もしない。
    private void SetHudCalloutSuppressed(bool on)
    {
        if (GetTree()?.GetFirstNodeInGroup("hud") is Hud hud) hud.SuppressCallouts = on;
    }

    protected virtual void OnCryStart() { }
    protected virtual void OnCryEnd() { }
    // 保険タイムアウトで cry を強制終了するとき、派生側の会話ドライバも畳ませるためのフック。
    // これを実装しないと _seq が立ったままで、終了後も ShowLine が走り会話が出続けてしまう。
    protected virtual void AbortCrySequence() { }

    // 手動送りで会話を終えたとき、Cry（その場停止）を即終了して笑顔へ着地。
    protected void EndCryNow() => FinishCry();

    // 遅延させていた Cry 絵への差し替えを、いま走らせる（DeferCryBodySwap 用）。
    //   レイ面の「決定打でガワが割れる」瞬間に会話ドライバから呼ぶ。二度呼んでも二重に差し替えない。
    //   PeelCryBodySwap が立っていれば、クロスフェードではなくガワが左右にほどける演出で見せる。
    protected void BreakCryBodyNow()
    {
        if (!DeferCryBodySwap) return;
        DeferCryBodySwap = false;
        if (PeelCryBodySwap && StartShellPeel()) return; // 層を立てられた＝差し替えは層が終わってから
        SwapBody(CryTexPath, CryBodyScale);
    }

    // ガワ割れ（案1）を始める。ここでやるのは3つだけ：
    //   (1) いま出ているガワ（本体スプライトの絵）と、その奥に置く中の人（Cry 絵）を層へ渡す
    //   (2) 本体スプライトを隠す＝以降 1.8 秒は層が「ガワ二枚＋中の人」を描く
    //   (3) 部品（枠・吹き出し・星）へ「公転をやめて外へ数px退きながら消えろ」を伝える
    // 素材が揃わない（Cry 絵が無い等）ときは false を返す＝呼び出し側が従来の差し替えへ落ちる。
    private bool StartShellPeel()
    {
        if (_peelStarted || !_hasBodyTex || _bodySprite == null) return false;
        var shell = _bodySprite.Texture;
        var inner = string.IsNullOrEmpty(CryTexPath) ? null : ResourceLoader.Load<Texture2D>(CryTexPath);
        if (shell == null || inner == null) return false;

        var parent = _bodySprite.GetParent();
        if (parent == null) return false;

        // ガワの表示中心（親のローカル座標）。Offset はテクスチャ座標なのでスケールを掛けて px に直す。
        // FlipH で Offset.x は既に符号が入っている（ApplyBodyOffset がそう入れている）のでそのまま使う。
        float sc = _baseScale;
        Vector2 shellOffs = _bodySprite.Position + _bodySprite.Offset * sc;
        Vector2 shellSize = new Vector2(shell.GetWidth(), shell.GetHeight()) * sc;

        // 中の人は表示高 BodyDisplayH×CryBodyScale（レイなら 72×0.75＝54px）。足元をガワと揃える：
        // 素材はどれも足元まで詰めてある（不透明域が下端）ので、縮めたぶん浮く (1-倍率)/2 を押し下げる。
        float innerH = BodyDisplayH * (CryBodyScale <= 0f ? 1f : CryBodyScale);
        float innerSc = innerH / inner.GetHeight();
        Vector2 innerSize = new Vector2(inner.GetWidth(), inner.GetHeight()) * innerSc;
        Vector2 innerOffs = shellOffs + new Vector2(0f, (BodyDisplayH - innerH) * 0.5f);

        _peel = new ShellPeelFx { Name = "ShellPeel", ZIndex = _bodySprite.ZIndex };
        parent.AddChild(_peel);
        _peel.Configure(shell, shellSize, shellOffs, _bodySprite.FlipH, inner, innerSize, innerOffs);

        _bodySprite.Visible = false;       // 以降は層が描く（同じ絵が二重に出ない）
        _fadeSprite?.QueueFree();          // 直前の差し替えの残りがあれば畳む
        _fadeSprite = null;
        _swapAnim = false;                 // squash→pop も走らせない（静かに退かせる）
        _parts?.OnShellPeel();             // 部品も公転をやめ、わずかに外へ退きながら 2.2s までに消える
        _peelStarted = true;
        _peelT = 0;
        return true;
    }

    // ガワ割れの時間を送る。会話中でも進めたいので _PhysicsProcess の早期 return より前で呼ぶ。
    // ガワが消え切った時点（1.8s）で、層の中の人を本体スプライトへ引き渡す＝以降は通常の経路に戻る
    //（FinishCry の post 差し替えも、退場のフェードも、そのまま効く）。
    private void TickShellPeel(double delta)
    {
        if (_peel == null) return;
        _peelT += delta;
        _peel.Tick((float)delta);
        if (!_peel.Finished) return;

        // 本体スプライトを中の人（Cry 絵）へ。層と同じ位置・同じ大きさなので、絵は動かずに入れ替わる。
        // ここでは SwapBody を使わない：クロスフェードと squash→pop が走ると「変身」に見えるため、
        // テクスチャとスケールだけ差し替えて、そのまま立たせる。
        var inner = string.IsNullOrEmpty(CryTexPath) ? null : ResourceLoader.Load<Texture2D>(CryTexPath);
        if (inner != null && _bodySprite != null)
        {
            _bodyScaleMul = CryBodyScale <= 0f ? 1f : CryBodyScale;
            _bodySprite.Texture = inner;
            _baseScale = BodyDisplayH / inner.GetHeight() * _bodyScaleMul;
            _bodySprite.Scale = new Vector2(_baseScale, _baseScale);
            ApplyBodyOffset();
            _bodySprite.SelfModulate = Colors.White;
            _bodySprite.Visible = true;
        }
        _peel.Dismiss();
        _peel = null;
    }

    // cry の終了はこの1経路に集約する（手動送り／CryHoldDur 経過／保険タイムアウトのどれでも同じ後処理）。
    // post スプライトへの着地 → OnCryEnd（各ボスが Finished を立てる）→ フォロワー付与、の順は不変。
    //
    // 部品層があるボス（v3）だけは post への着地を「部品が全部消えてから」に遅らせる：
    // ひび→ガワ（枠・吹き出し・カード）が落ちる→中の人が出る、の順を守る（13 の演出指定）。
    // OnCryEnd / GrantFollower は待たせない＝進行（Finished）はここまでの作法のまま動く。
    private void FinishCry()
    {
        if (!_crying) return;
        _crying = false;
        SetHudCalloutSuppressed(false);   // 改心の会話が終わった＝テロップの抑制を戻す
        if (_parts != null) _parts.OnRedeem(() => SwapBody(PostTexPath, PostBodyScale));
        else SwapBody(PostTexPath, PostBodyScale);
        OnCryEnd();
        GrantFollower();
    }

    // 改心の会話が1行進んだことを派生から知らせる（保険タイムアウトの猶予をリセットする）。
    // これがある限り「じっくり読む」プレイは切られない＝発動するのは本当に進まなくなったときだけ。
    protected void NotifyCryProgress() => _cryWatchdogT = 0;

    // 保険：cry が終わらないまま無操作／進行不能になったら、通常の終了経路へ強制的に落とす。
    private void TickCryWatchdog(double delta)
    {
        _cryWatchdogT += delta;
        _cryTotalT += delta;
        if (_cryWatchdogT < CryStallLimit && _cryTotalT < CryHardLimit) return;
        GD.PushWarning($"[Enemy] cry watchdog fired on {GetType().Name} "
                     + $"(stall={_cryWatchdogT:F1}s total={_cryTotalT:F1}s) — 改心を強制終了して進行不能を回避");
        AbortCrySequence(); // 派生の会話ドライバを畳む（_seq を落とす）
        // 会話バブルが掴んだままだと以降の会話が出せなくなるので必ず解放する。
        if (GetTree()?.GetFirstNodeInGroup("hud") is Hud hud) { hud.HoldBubble = false; hud.HideBubble(); }
        FinishCry();
    }

    public override void _PhysicsProcess(double delta)
    {
        _shieldLayer?.QueueRedraw();
        // 世界の Tint の打ち消しは状態に関わらず毎フレーム引き直す（Tint は Warmth で動き、ボスの realm 暴露では
        // 白へ抜ける）。登場演出・改心・退場・会話中の early-return より前に置く＝どの状態でも暗く沈ませない。
        ApplyTintLift();

        // 差し替えアニメ（クロスフェード＋squash→pop）は状態に関わらず常に進める。
        if (_swapAnim) { TickSwapAnim(delta); QueueRedraw(); }

        // cry の保険タイムアウトも状態に関わらず常に進める
        //（登場演出中に撃破された等で下の early-return に阻まれても必ず計られるように）。
        if (_crying) TickCryWatchdog(delta);

        // ガワ割れも状態に関わらず進める。会話中（BubblePaused）でも、cry の early-return より前でも
        // 送らないと、ガワが開いたまま／閉じたまま固まる。
        if (_peel != null) TickShellPeel(delta);
        else if (_peelStarted && _peelT < ShellPeelHold) _peelT += delta; // 静止保持ぶんの時計は続ける

        // 攻撃姿勢の戻し（会話中も進める＝会話に入っても攻撃絵で固まらない）。
        if (_attackPoseT > 0) TickAttackPose(delta);

        if (_flashing)
        {
            _flashT += delta;
            if (_flashT >= FlashDur) _flashing = false;
            QueueRedraw();
        }

        // ボス登場演出中は演出だけを進める（移動・弾幕・フェーズは止まる＝見得の間）。
        // 会話(BubblePaused)中も進む＝ボスイントロの会話に重ねて「現れる」を見せる。
        if (_entering) { TickEntrance(delta); return; }

        // 大泣き中はその場に留まり、CryHoldDur 経過で笑顔へ着地。
        if (_crying)
        {
            _cryT += delta;
            if (_cryT >= CryHoldDur) FinishCry();
            return;
        }

        if (_purified)
        {
            // フォロワー化した本人は、その場に湧いた Follower が見た目を引き継ぐので本体は即退場
            //（救った娘が去りつつ別の娘がくっつく“二重表示”を防ぐ＝救った本人がそのまま付くように見える）。
            if (_becameFollower) { QueueFree(); return; }
            // 判定のない敵が再び攻撃しているように見えないよう、撃破位置でフェードする。
            _purifiedExitT += delta;
            float exitA = 1f - Mathf.Clamp((float)((_purifiedExitT - ExitHold) / PurifiedExitFade), 0f, 1f);
            Modulate = new Color(Modulate.R, Modulate.G, Modulate.B, exitA);
            if (exitA <= 0f) QueueFree();
            return;
        }

        // ★集中モード（#10）：ここから下は「敵の時間」＝係数を掛けた delta で進める。
        //   自機・HUD・音・演出は素の delta のまま＝向かってくるものだけが遅くなる（Engine.TimeScale は使わない）。
        //   登場演出・改心・退場（上の early-return 群）は物語の尺なので掛けない。
        double edelta = GameManager.EnemyDelta(delta);

        // 無防備窓サイクルの進行（BubblePaused でも止めない＝合図/窓が固まらないように）。
        if (_maxHp > 0)
        {
            if (!Hud.BubblePaused) _shieldTime += (float)edelta;
            TickBossPhase(edelta);
        }

        // ロックオンの照準マーカー（_Draw）を回すための再描画（2026-09-08）。
        //   ザコ（_maxHp==0）は普段いっさい QueueRedraw を呼ばない＝ロック対象を雑魚まで広げた今、
        //   これが無いとマーカーが点かない／外しても消えない。「今ロックされているか」が
        //   前フレームから変わったとき＋ロック中は毎フレーム、の2条件で促す
        //   （無関係な雑魚まで毎フレーム再描画しない＝描画コストは増やさない）。
        bool lockedNow = GetTree().GetFirstNodeInGroup("player") is Player lp && lp.LockTarget == this;
        if (lockedNow || _wasLockTarget || (_maxHp > 0 && !_purified)) QueueRedraw();
        _wasLockTarget = lockedNow;

        if (Hud.BubblePaused) return; // 吹き出し表示中は動かない（襲ってこない）

        if (GaugeVulnerable)
        {
            ApplyBossMotion(Vector2.Zero, 0, _bodySprite.FlipH);
            TickDownBreathing(edelta);
            return;
        }
        if (GaugeReforming) return;
        UpdateMovement(edelta);
        TickBodyMotion(edelta);
        TickAutoBank(edelta); // ザコの移動バンク（ボス/生命感モーション持ちは AutoBank=false で素通り）
        TickFacing();         // 自機の方を向く（ボス/カメオは ApplyBossMotion が握る＝FacePlayer=false で素通り）
        if (GlobalPosition.X < OffLeftX) QueueFree();
    }

    // BREAK→EXPOSED→RECLOSE→SHIELDED の尺管理。SHIELDED 中は何もしない（パネル待ち）。
    private void TickBossPhase(double delta)
    {
        if (_phase == BossPhase.Shielded) return;
        _phaseT += delta;
        switch (_phase)
        {
            case BossPhase.Break:
                if (_phaseT >= BreakCueDur) EnterExposed();
                else QueueRedraw();
                break;
            case BossPhase.Exposed:
                if (_hitFlashT > 0) _hitFlashT -= delta; // 被弾発光の減衰
                if (_bodyHitCd > 0) _bodyHitCd -= delta; // 本体ヒットCDの消化
                QueueRedraw(); // 発光/明滅（_Draw）を更新し「今は殴れる」を可視化
                // 窓キャップ到達済みなら残り時間（最大4.0s）を待たせず次サイクルへ即移行。
                // 火力に投資するほど「窓を早く閉じて次のBREAKへ進める」形でリターンを返す（#b案）。
                if (_windowDamage >= WindowCap || _phaseT >= VulnDur) EnterReclose();
                break;
            case BossPhase.Reclose:
                // 弱気セリフ(RecloseLineDur)を見せ、RespawnGap 置いてパネルを一括再生成＝SHIELDED へ。
                if (_phaseT >= RecloseLineDur + RespawnGap) EnterShielded();
                break;
        }
    }

    protected virtual void UpdateMovement(double delta) { }

    private static CanvasTexture? _shieldArt;
    private static CanvasTexture? _majorShieldArt;
    private Node2D? _shieldLayer;
    private float _shieldTime;
    private void DrawShield(Node2D layer)
    {
        if (_maxHp <= 0 || _purified || _entering) return;
        bool breaking = _phase == BossPhase.Break;
        bool reforming = _phase == BossPhase.Reclose;
        if (!breaking && !reforming && (_phase != BossPhase.Shielded || _panels.Count == 0)) return;
        bool major = PurifyGrade == FxLayer.PurifyTier.Boss;
        CanvasTexture art;
        if (major)
            art = _majorShieldArt ??= new CanvasTexture
            {
                DiffuseTexture = GD.Load<Texture2D>("res://char/ui/boss_shield_major_v1.png"),
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            };
        else
            art = _shieldArt ??= new CanvasTexture
            {
                DiffuseTexture = GD.Load<Texture2D>("res://char/ui/boss_shield_v1.png"),
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            };
        float h = BodyDisplayH + (major ? 14f : 12f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_shieldTime * 2.8f);
        float build = reforming ? Mathf.Clamp((float)(_phaseT / (RecloseLineDur + RespawnGap)), 0, 1) : 1f;
        var size = new Vector2(h * (major ? 1f : 1.15f), h) * (0.9f + 0.1f * build);
        var lift = TintLift.Of(_worldTint, TintLift.EnemyBody);
        var tint = (major ? Colors.White : new Color("91dfff")) * lift;
        float alpha = (major ? 0.82f + pulse * 0.08f : 0.64f + pulse * 0.08f) * (0.3f + 0.7f * build);
        float integrity = 1f;
        if (_phase == BossPhase.Shielded)
        {
            int remainingInk = 0;
            foreach (var panel in _panels) remainingInk += panel.Ink;
            integrity = Mathf.Clamp(remainingInk / (float)(PanelCount * PanelInk), 0f, 1f);
            alpha *= Mathf.Lerp(0.45f, 1f, integrity);
        }
        if (breaking)
        {
            float t = Mathf.Clamp((float)(_phaseT / BreakCueDur), 0, 1);
            int rows = major ? 4 : 3;
            var grid = new Vector2(2, rows);
            var source = art.GetSize() / grid;
            var piece = size / grid;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < 2; x++)
                {
                    var direction = new Vector2(x == 0 ? -1 : 1, (y + 0.5f) / rows * 2f - 1f);
                    var center = -size / 2 + piece * (new Vector2(x, y) + Vector2.One * 0.5f);
                    center += direction * t * (major ? 16f : 9f);
                    layer.DrawSetTransform(center, direction.X * t * (major ? 0.34f : 0.12f));
                    layer.DrawTextureRectRegion(art, new Rect2(-piece / 2, piece),
                        new Rect2(source * new Vector2(x, y), source), new Color(tint, 0.95f * (1f - t)));
                }
            layer.DrawSetTransform(Vector2.Zero);
            return;
        }
        if (major)
        {
            var glow = size * (1.04f + pulse * 0.015f);
            layer.DrawTextureRect(art, new Rect2(-glow / 2, glow), false, new Color(tint, alpha * 0.22f));
        }
        layer.DrawTextureRect(art, new Rect2(-size / 2, size), false, new Color(tint, alpha));
        if (!major) return;
        var gold = new Color("ffe5a3") * lift;
        for (int i = 0; i < 6; i++)
        {
            float angle = _shieldTime * 0.38f + i * Mathf.Tau / 6;
            DrawShieldArc(layer, size * new Vector2(0.49f, 0.47f), angle, 0.4f * integrity, new Color(gold, alpha * 0.2f), 2.8f);
            DrawShieldArc(layer, size * new Vector2(0.49f, 0.47f), angle, 0.4f * integrity, new Color(gold, alpha), 0.9f);
        }
        for (int i = 0; i < 3; i++)
            DrawShieldArc(layer, size * new Vector2(0.43f, 0.43f), -_shieldTime * 0.5f + i * Mathf.Tau / 3,
                0.32f, new Color(gold, alpha * 0.65f), 0.7f);
    }

    private static void DrawShieldArc(Node2D layer, Vector2 radius, float start, float span, Color color, float width)
    {
        var points = new Vector2[13];
        for (int i = 0; i < points.Length; i++)
            points[i] = Vector2.FromAngle(start + span * i / (points.Length - 1)) * radius;
        layer.DrawPolyline(points, color, width, true);
    }

    private void DrawLockOn(Job job, Panel? panel)
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() * 0.0034f);
        float r = Mathf.Max(BodyHalfH, BodyRadius) + 9f + pulse;
        var color = new Color(BulletArt.PlayerColor(job), 0.65f + pulse * 0.2f);
        for (int i = 0; i < 4; i++)
        {
            float angle = Mathf.Pi / 2 * i;
            var axis = Vector2.FromAngle(angle);
            var side = axis.Orthogonal();
            switch (job)
            {
                case Job.Heal:
                    DrawArc(Vector2.Zero, r, angle - 0.38f, angle + 0.38f, 12, color, 1.2f, true);
                    break;
                case Job.Melee:
                    DrawPolyline(new[] { axis * (r + 4) + side * 4, axis * (r - 1),
                        axis * (r + 4) - side * 4 }, color, 1.3f, true);
                    break;
                case Job.Magic:
                    DrawPolyline(new[] { axis * (r - 3) + side * 4, axis * (r + 3),
                        axis * (r - 3) - side * 4 }, color, 1.2f, true);
                    break;
                default:
                    var corner = Vector2.FromAngle(angle + Mathf.Pi / 4) * r;
                    DrawPolyline(new[] { corner - axis * 6, corner, corner + side * 6 }, color, 1.2f, true);
                    break;
            }
        }
        var art = BulletArt.PlayerMark(job);
        float size = job == Job.Magic ? 13f : 12f;
        var markPosition = new Vector2(-size / 2, -r - size - 3);
        if (HasHpBar)
        {
            DrawSetTransform(Vector2.Zero);
            markPosition = new Vector2(-GaugeWidth / 2 - size - 8f, GaugeTop - 4f - size / 2);
        }
        DrawTextureRect(art, new Rect2(markPosition, new Vector2(size, size)), false,
            new Color(Colors.White, 0.88f));
        if (panel != null)
        {
            DrawSetTransform(panel.Position);
            float subRadius = (UnfolderStyle == UnfolderKind.None ? 8f : 13f) + pulse * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float angle = Mathf.Pi / 2 * i;
                DrawArc(Vector2.Zero, subRadius, angle + 0.15f, angle + Mathf.Pi / 2 - 0.15f,
                    10, new Color(color, 0.95f), 0.8f, true);
            }
        }
        DrawSetTransform(Vector2.Zero);
    }

    // 頭上ゲージ（BossGauge）の置き場。四隅の枠（下の _Draw）と同じ寸法式から「枠の上辺の少し上」と「枠幅ほど」を出す。
    public float GaugeTop => -Mathf.Max(BodyHalfH + 3f, BodyDisplayH * 0.35f) - 8f;
    public float GaugeWidth => Mathf.Clamp(Mathf.Max(BodyRadius * 2f + 6f, BodyDisplayH * 0.5f), 26f, 56f);
    public float GaugeBottom => Mathf.Max(BodyHalfH + 3f, BodyDisplayH * 0.55f) + 7f;
    public bool GaugeReforming => !_purified && !_entering && _phase == BossPhase.Reclose;
    public float GaugeReformProgress => GaugeReforming
        ? Mathf.Clamp((float)(_phaseT / (RecloseLineDur + RespawnGap)), 0f, 1f) : 0f;

    // 頭上ゲージの塗り分け（2026-09-27 作者指示「無敵のタイミングと、BREAK時のHPバー色分けしたい」）。
    //   Hud.GaugeState は「1本ぶんの割合・残本数」しか持たないので、今殴れるかどうかはボス本体から直接読む。
    //   GaugeVulnerable：BREAK（割れた一拍）と EXPOSED（無防備窓）＝殴れる時間。SHIELDED／RECLOSE は無敵。
    public bool GaugeVulnerable => !_purified && _phase is BossPhase.Break or BossPhase.Exposed;
    private const double GaugeFreshDur = 0.35;     // 割れた瞬間のゲージの明滅の減衰
    // BREAK に入ってからの経過が短いほど 1→0。EXPOSED は BREAK の尺（BreakCueDur）を足して同じ時計で続ける
    //   （BreakCueDur 0.45 > 0.35 なので EXPOSED ではもう 0＝割れた一拍だけ光る）。
    public float GaugeBreakFresh
    {
        get
        {
            if (!GaugeVulnerable) return 0f;
            double since = _phase == BossPhase.Break ? _phaseT : BreakCueDur + _phaseT;
            return Mathf.Clamp(1f - (float)(since / GaugeFreshDur), 0f, 1f);
        }
    }
    // 無防備窓の残り割合（本体まわりのタイマーバーと同じ式）。BREAK 中は 1、無敵中は 0。
    public float GaugeWindowLeft => _purified ? 0f : _phase switch
    {
        BossPhase.Break => 1f,
        BossPhase.Exposed => Mathf.Clamp(1f - (float)(_phaseT / VulnDur), 0f, 1f),
        _ => 0f,
    };

    public override void _Draw()
    {
        bool locked = GetTree().GetFirstNodeInGroup("player") is Player lp && lp.LockTarget == this;
        if (locked) DrawLockOn(GameManager.Instance?.SelectedJob ?? Job.Tank,
            (GetTree().GetFirstNodeInGroup("player") as Player)?.LockedUnfolder);

        // 改心フラッシュ（やさしい色：淡ピンク→淡紫に着地）
        if (_flashing)
        {
            float t = (float)(_flashT / FlashDur);
            var c = new Color(1f, 0.85f, 0.92f).Lerp(new Color(0.79f, 0.72f, 0.94f), t); // 淡ピンク→淡紫
            DrawCircle(Vector2.Zero, AuraRadius + 10f * (1f - t), new Color(c.R, c.G, c.B, 0.6f * (1f - t)));
        }

        if (!_purified && _maxHp > 0 && _phase is BossPhase.Break or BossPhase.Exposed)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)_phaseT * Mathf.Tau * 1.5f);
            float hit = Mathf.Clamp((float)(_hitFlashT / HitFlashDur), 0, 1);
            var color = new Color(new Color("e9d28b").Lerp(Colors.White, hit), 0.65f + 0.15f * pulse);
            float w = Mathf.Max(BodyRadius + 3, BodyDisplayH * 0.24f);
            float h = Mathf.Max(BodyHalfH + 3, BodyDisplayH * 0.35f);
            // 四隅の枠はロックオン中だけ（2026-09-26 ユーザー「ロックオンしてないときにボスを囲う枠は出さない」）。
            if (locked)
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    var corner = new Vector2(x * w, y * h);
                    DrawLine(corner, corner - new Vector2(x * 4, 0), color, 1f + hit * 0.5f);
                    DrawLine(corner, corner - new Vector2(0, y * 4), color, 1f + hit * 0.5f);
                }
        }

        // スプライトが無い時だけプレースホルダ図形を描く
        if (!_hasBodyTex)
            DrawPerson(_purified ? new Color(1f, 0.86f, 0.62f) : new Color(0.55f, 0.6f, 0.78f), happy: _purified);

        // ※波紋射程プレビュー（Ripple.MaxRadius の薄い輪）は削除（2026-09-08）。
        //   盾が残り1枚のあいだ出っぱなしで、タイミングを伝える記号（合図リング・露出オーラ）と
        //   紛らわしいうえ意味が読めなかった。波紋そのものは剥がし切った瞬間に出るので情報は失われない。
    }

    private void DrawPerson(Color body, bool happy)
    {
        DrawCircle(new Vector2(0, 2), BodyRadius, body);                       // 体
        DrawCircle(new Vector2(0, -6), 5f, new Color(1f, 0.92f, 0.85f));       // 頭
        var eye = happy ? new Color(0.2f, 0.1f, 0.1f) : new Color(0.1f, 0.1f, 0.2f);
        float ey = happy ? -7f : -5f;
        DrawCircle(new Vector2(-2, ey), 0.9f, eye);
        DrawCircle(new Vector2(2, ey), 0.9f, eye);
        if (happy)
            DrawArc(new Vector2(0, -5), 2.2f, 0.2f, Mathf.Pi - 0.2f, 8, new Color(0.6f, 0.25f, 0.25f), 1f); // 笑み
    }
}
