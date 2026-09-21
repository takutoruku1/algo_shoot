using Godot;
using System.Collections.Generic;

// Player : Area2D。グループ "player" に追加。
// 移動(75 px/s 一定)、連射(Pool経由・右方向+360・上下2way)、被弾無敵点滅、TakeHit、Lives。
// W0 では残機を減らさず「練習中」扱い（ゲームオーバーにしない）。
// 衝突: layer=1, mask=12（敵=4 と 敵弾=8 を検出）。
// 当たり判定は半径2px の極小（胸の紫十字相当）。可視ヒットボックス点は子ノード PlayerHitDot が絵より前に描く。
public partial class Player : Area2D
{
    // 速度（2026-09-08 ユーザー指示で基本移動を半減：150→75）。
    //   ★低速移動（Shift / パッド L1）は 2026-09-13 ユーザー決定で廃止＝速度はこの1本だけ。
    //     基本移動そのものが既に遅く、低速は「遅いものをさらに遅くする」だけで手触りを損ねていた。
    //     空いた L1 は集中モードへ回した（下の集中モード入力を参照）。
    private const float NormalSpeed = 75f;
    public float SlowestMoveSpeed => NormalSpeed * (_game?.MoveSpeedMul ?? 1f)
        * (_game?.JobDef.MoveMul ?? 1f) * LockMoveMul * PowerMoveMultiplier;

    // 連射
    private const float FireInterval = 0.13f;
    private float _fireCooldown = 0f;

    // 当たり半径（極小）
    private const float HitRadius = 2f;
    // グレイズ半径（かすり判定の広さ）
    private const float GrazeRadius = 11f;

    // ボム入力のエッジ検出用
    private bool _bombHeld = false;
    // 集中モード（V）入力のエッジ検出用。発動の可否は GameManager.TryFocusMode が持つ。
    private bool _focusModeHeld = false;

    // 初回に HUD へ現在モードを通知したか（V の切替ローテは 2026-09-13 に廃止＝エッジ検出はもう要らない）。
    private bool _modeInit = false;

    // ── 溜め打ち（一本道 #6「溜め打ち」）：C / パッドY を長押し ──
    //   ★Cキーは 2026-09-13 まで W0 専用のヒカゲスキルが握っていた（非正典＝正典導線からは到達しない）。
    //     戦闘側の配線（_specialCd・HUDチップ）を撤去し、このボタンを溜め打ちへ明け渡した。
    //   ChargeNeed 秒押し切ると充填完了。離した瞬間に威力×4の大玉を1発だけ撃つ（貫通なし・CD無し＝
    //   チャージ時間そのものがコスト）。押しているあいだも通常ショットは止めない＝「撃ちながら溜める」。
    private const float ChargeNeed = 0.6f;      // 充填に要する長押し秒
    private const float ChargeDamageMul = 4f;   // 大玉の威力倍率（基礎威力に対して）
    private const float ChargeSpeed = 760f;     // 大玉の発進速度（MakeAccel の fast と同値）
    private bool _chargeHeld;                   // 前フレームのボタン状態（離したエッジの検出用）
    private float _chargeT;                     // 押している累計秒（0 で未充填）
    public bool ChargeFull => _chargeT >= ChargeNeed;                     // 充填完了か（自機頭上の表示が読む）
    public float ChargeRatio => Mathf.Clamp(_chargeT / ChargeNeed, 0f, 1f); // 充填率 0..1（同上）

    // フォロワー（浄化した人＝味方オプション）
    private readonly List<Follower> _followers = new List<Follower>();

    // 加速球（Accel）のタメ中弾トラッキング。基礎間隔0.13sごとに2発スポーンし、
    // タメ0.8s分（約12発）が自機前方に無制限に積み上がって自弾グローが敵弾の視認を妨げる問題への対処＝
    // 同時タメ中の弾数に上限を設ける（AccelChargeCap）。ホーミングの寿命切れ対策と同種の“自機弾の雲”対策。
    private readonly List<Bullet> _accelCharging = new List<Bullet>();
    private const int AccelChargeCap = 6; // 3ペア＝6発まで。超過分は新規スポーンをスキップ
    // 道中で浄化した人が自機に随伴する「フォロワー」機能のON/OFF。
    // 2026-09-06 ユーザー指示によりOFF（戦闘中に随伴フォロワーを増やさない）。true にすれば復活する。
    // ※ SNSのフォロワー数（GameManager.Followers / FollowerPowerMul）とは別物なので、そちらには影響しない。
    // （const ではなく static readonly ＝ false 固定でも分岐が畳まれず CS0162 が出ない。切替は下の初期値のみ）
    public static readonly bool StageFollowersEnabled = false;
    public const int MaxFollowers = 4;
    public const int SavedPerFollower = 3; // この人数を救うごとに1体増える（増加を緩やかに）。HUDの進捗ドット表示にも使うため公開。
    private int _savedCount = 0;
    private int _shotParity = 0;            // フォロワーの発射間引き用
    private static readonly Vector2[] FollowerSlots =
    {
        new Vector2(-14, -11), new Vector2(-14, 11),
        new Vector2(-26, -5), new Vector2(-26, 5),
    };

    // 拡散サブ（option_sub）＝追従オプション。上下の定位置に光球として浮かび、メイン射撃に同期して撃つ。
    // フォロワー（被弾で離れる）と違い恒久強化＝ラン中ずっと付く。スロットはフォロワー位置と重ならない上下寄り。
    private int _optionCount = 0;               // _Ready で GameManager.OptionSubCount を確定（ラン中不変）
    private static readonly Vector2[] OptionSlots = { new Vector2(-4, -14), new Vector2(-4, 14) };

    // W0 専用・非正典。正典導線からは到達しない（2026-09-06 ユーザー決定: ヒカゲは使わない）。以後この系統への追加投資はしない。
    // ヒカゲを仲間に。フォロワーが満員(4)なら1体をヒカゲに強化、空きがあれば強化フォロワーとして追加。
    public void AddHikageFollower(Vector2 globalFromPos)
    {
        // すでにヒカゲがいるなら重複させない
        foreach (var f in _followers)
            if (f.IsHikage) return;

        if (_followers.Count >= MaxFollowers)
        {
            // 満員：通常フォロワーの1体をヒカゲに強化
            foreach (var f in _followers)
            {
                if (!f.IsHikage)
                {
                    f.PromoteToHikage();
                    FxLayer.Instance?.PurifyBurst(f.GlobalPosition);
                    return;
                }
            }
            return;
        }

        // 空きあり：強化フォロワーとして新規追加
        var nf = new Follower { SlotOffset = FollowerSlots[_followers.Count] };
        AddChild(nf);
        nf.Position = ToLocal(globalFromPos);
        nf.PromoteToHikage();
        FxLayer.Instance?.PurifyBurst(nf.GlobalPosition);
        _followers.Add(nf);
    }

    // フォロワーが満員（MaxFollowers到達済み）か。
    public bool FollowersFull => _followers.Count >= MaxFollowers;

    // 人を救うたびに呼ばれる（Enemy.Redeem）。一定人数ごとにフォロワーが1体増える。
    // 救った本人がフォロワー化したら true（呼び出し元の Enemy はその本体を退場させずフォロワーに引き継ぐ）。
    public bool AddFollower(Vector2 globalFromPos)
    {
        // 随伴フォロワー無効時は何もしない＝生成も進捗通知もせず、呼び出し元(Enemy)は通常どおり退場する。
        if (!StageFollowersEnabled) return false;
        _savedCount++;
        if (_followers.Count >= MaxFollowers) return false;
        if (_savedCount % SavedPerFollower != 0) return false; // 3人救うごとに1体
        var f = new Follower { SlotOffset = FollowerSlots[_followers.Count] };
        AddChild(f);
        f.Position = ToLocal(globalFromPos); // 浄化した位置（＝救った本人の場所）から飛んでくる
        _followers.Add(f);
        return true;
    }

    // プレイ領域（盤面の矩形の定義元は Field。ここは短い別名として残す）
    private const float MinX = Field.Left;
    private const float MaxX = Field.Right;
    private const float MinY = Field.Top;
    private const float MaxY = Field.Bottom;

    // 残機
    public int Lives { get; private set; } = 3;

    public string CharacterId { get; private set; } = "mina";

    // 無敵・点滅
    private bool _invincible = false;
    private float _invincibleTimer = 0f;
    private const float InvincibleDuration = 1.2f;
    private float _blinkPhase = 0f;
    // 「被弾直後の無敵」かどうか。被弾無敵の間だけ経済加算（グレイズ報酬）を止め、無敵を盾にした稼ぎ(farming)を断つ。
    // スポーン無敵・ボム無敵・回避無敵はこのフラグを立てない＝それらの最中は従来どおり稼げる。
    private bool _hitInvincible = false;

    // 表示用スプライト（algo.png）。読み込めない場合は null のまま → _Draw フォールバック。
    private Sprite2D _sprite = null!;
    private bool _hasTexture = false;

    // ───────── マウス操作（キーボード/パッドへ純粋に追加）─────────
    // 弾幕STG標準のカーソル追従。直近デバイスがマウスのとき（Pad.UsingMouse）だけ有効＝
    // キーボード/パッドを触った瞬間に Pad 側で false へ落ちるので、カーソルが画面内にあっても引っ張られない。
    // 追従は瞬間移動だとワープして見えるので指数補間。ただし「張り付く」速さに置く。
    // 到達速度は既存の移動速度上限で頭打ちにする。
    //（低速時に係数を落とす分岐は、低速移動の廃止＝2026-09-13 に一緒に撤去した）
    private const float MouseFollowResponse = 26f;      // 追従の速さ(1/s)。実質カーソルに張り付く
    private const float MouseSnapDist = 0.6f;           // この距離まで詰めたら吸着（微振動を止める）

    // 「今かすった」を自機の絵の発光で一瞬返す残光（1→0 へ減衰）。FxLayer.Graze の閃光と併用。
    private float _grazeFlash = 0f;
    private const float GrazeFlashDecay = 6f; // 約0.17秒で消える（即・短く＝テンポを殺さない）

    // ミナの汚染ティント（0=澄んだ光 → 1=黒く濁る）。スプライトの SelfModulate にのみ掛け、
    // 被弾点滅（Modulate のα）とは独立に作用させる。
    private float _corruption = 0f;
    private static readonly Color CleanTint = new Color(1f, 1f, 1f);
    private static readonly Color MurkTint = new Color(0.42f, 0.40f, 0.52f); // 濁った藍鼠
    public void SetCorruption(float level) => _corruption = Mathf.Clamp(level, 0f, 1f);

    // チュートリアル（ステージ0）の自機系ステップで、自機を“光らせて”目立たせる（既存グレイズ残光を流用）。
    // World↔設計座標変換の事故を避けるため、Hudの暗転穴ではなく自機側の発光で注意を引く方式。
    public void TutorialGlow() => _grazeFlash = Mathf.Max(_grazeFlash, 0.6f);

    // 常時ふわふわ浮遊（スプライトのみ上下に揺らす。当たり判定点は固定）
    private float _bobTime = 0f;
    private const float BobSpeed = 3.2f; // 角速度(rad/s) 約2秒周期
    private const float BobAmp = 2.0f;   // 揺れ幅(px)

    // ───────── 体のリアクション（被弾のけぞり／発射反動／ボム解放）─────────
    // すべて _sprite の Transform のみ＝当たり判定・操作・テンポには一切影響しない（yoshida §7「自機が痛がる」）。
    // 被弾のけぞり：撃たれた瞬間に左（射撃と逆）へ倒れて潰れ、スッと復帰する（点滅と併走して「痛がった」を返す）。
    private float _hitReact = 0f;
    private const float HitReactDur = 0.42f;  // のけぞり→復帰の全長(s)。無敵点滅(1.2s)より短く尾を引かせない
    // 発射反動：Fire のたび 1 になり高速減衰。連射のリズムが体に出る小さなキックバック。
    private float _recoil = 0f;
    private const float RecoilPx = 1.6f;      // 最大後退量(px)。表示高36pxに対し約4%＝視認性を侵さない控えめ
    private const float RecoilDecay = 14f;    // 減衰(1/s)。FireInterval(0.13s)で約0.16まで抜ける＝脈打つ程度
    // ボム解放：発動と同フレームで伸び上がる（stretch＋浮き）。魔法陣・フラッシュと同拍＝「放った」を体で示す。
    // 予備動作は置かない（入力への即応を遅らせない＝sakurai）。余韻だけ残して静かに着地する。
    private float _bombCast = 0f;
    private const float BombCastDur = 0.5f;

    // 移動バンク（進行方向へ体を傾け＋わずかに先行。見た目=_spriteのみ／当たり判定は不動）。
    // _lean を入力方向へ指数補間して慣性を持たせる＝予備動作（タメ）と余韻（揺り戻し）が自動で出る。
    // ★#4 左右の向き差分の結論（改訂）：向き反転ボタン（F / パッド RB）の導入に伴い FlipH を採用する。
    //  自機は _facing(+1=右 / -1=左) を持ち、スプライトを FlipH で反転する＝顔と銃口が常に射撃方向を向く。
    //  バンク（傾き）は「向きから見た前後」で判定する＝左向き時は左移動が前進＝深い前傾になる（下の bankX）。
    //  そのうえで前進(射撃方向＝攻め)は深く・後退(引き)は浅く傾ける非対称バンクにし、攻守を姿勢で描き分ける。
    private Vector2 _lean = Vector2.Zero;
    private static readonly float BankXFwd = Mathf.DegToRad(9f);   // 前進（射撃方向へ攻める）はしっかり前傾
    private static readonly float BankXBack = Mathf.DegToRad(5.5f); // 後退は控えめ＝顔は敵へ向けたまま引く
    private static readonly float BankY = Mathf.DegToRad(13f); // 上下（縦移動）のバンク
    private const float LeadPx = 2.5f;        // 進行方向への体の先行量(px)
    private const float LeanResponse = 9f;    // 慣性の追従の速さ（大きいほど機敏／小さいほどたゆたう）

    // ───────── 向き（射撃方向）─────────
    // 入力＝F / パッド RB。押すたびに右(+X)⇔左(-X) をトグルする（押しっぱなし不要）。
    // 射撃方向は必ずここを単一の真実として参照する（各射撃経路に符号を散らさない）。
    //   ShotDir   … 射撃の基準ベクトル（(±1,0)）
    //   ShotAngle … 扇（拡散/ホーミング/大波）の基準角。0=右 / π=左。基準角に足せば左右どちらでも自然に開く
    //   Facing    … +1/-1。銃口オフセットや前後判定の符号に使う
    // 同時に両方向へは撃たない＝火力は不変（向きが変わるだけ）。
    // 向き反転そのものを殺すスイッチ（2026-09-06 ユーザー指示。true で復活）。
    // false のあいだは反転入力を受け付けず _facing は +1（右向き）のまま固定される＝下流
    //（ShotDir / ShotAngle / FlipH / バンクの前後判定）は従来どおり _facing を読むだけで整合が取れる。
    public static readonly bool FacingFlipEnabled = false;

    private int _facing = 1;
    // トグル入力のエッジ検出。初期値は true＝「既に押されている扱い」で始める。
    // シーン遷移（ハブ→面／ショップ→面／リトライ）の決定クリックや F キーは、次のシーンの初フレームでも
    // まだ押されたままのことがある。false 始まりだとそれが新品の押下エッジになって、開始直後に自機が
    // 後ろを向いてしまう。一度離すまで反転しない＝持ち越しの入力を食わせない。
    private bool _flipHeld = true;
    private bool _mouseFlipLocked = false;      // 会話送りのクリックが会話明けに向き反転へ流れ込むのを止めるゲート（離すまで反転しない）
    public int Facing => _facing;

    // ───────── ロックオン照準（全敵・巡回式・2026-09-08）─────────
    // 初出はボス戦専用（2026-09-07）。ユーザー指示で**雑魚・中ボス・ボスすべて**を対象にし、
    // 「クリックのたびに一番近い敵から順に一つ遠い敵へ移り、画面内を一巡したらまた一番近い敵へ戻る」
    // 巡回式に作り替えた。
    //
    //   ・候補＝**画面（盤面）に映っていて、まだ浄化されていない敵**。ボスが居なくても使える
    //     （道中で切る早期 return は撤去した）。
    //   ・弾そのものは曲げない。**発射方向**を対象へ向けるだけ＝ホーミング（弾が追う）とは別物。
    //     実装は ShotDir / ShotAngle の差し替え1点に閉じる＝全射撃経路（連射・拡散・ホーミング・
    //     貫通・子分）が自動で追従し、弾数・威力・間隔には触れない。
    //   ・入力＝**左クリック** / F / パッド R1。押すたびに次の敵へ進む（押しっぱなし不要）。
    //     **右クリックは解除専用**（回避と同じボタン＝回避と同時にロックが外れる）。ロックしていない
    //     ときの右クリックは回避が出るだけで何も起きない。
    //     **向き反転とは別系統**で、こちらは _facing を書き換えない（_facing は +1 のまま。
    //     ロック中だけ ShotDir が上書きされる）。
    //   ・ロック中は移動が遅くなる（LockMoveMul）＝照準を任せるあいだは足が重い、という取引。
    private const float LockMoveMul = 0.8f;   // ロック中の移動速度倍率（ユーザー決定の目安 0.8）
    private bool _locked;                     // ロックオン中か
    private bool _lockHeld = true;            // 送りのエッジ検出（_flipHeld と同じ理由で true 始まり）
    private bool _mouseLockLocked = false;    // 会話送りのクリックが会話明けにロック送りへ流れ込むのを止めるゲート（離すまで送らない）

    // ── 左クリックの短押し／長押し分岐（2026-09-13 ユーザー決定）──
    //   短押し（MouseTapMax 未満で離す）＝ロックオン送り。従来は押下エッジで送っていたが、
    //   長押しを溜め打ちに使うため「離した瞬間に送る」へ変えた（この 0.25 秒の遅れは許容と決定済み）。
    //   長押し（MouseTapMax 以上）＝溜め打ちのチャージ開始。充填の時計は Cキーと同じ ChargeNeed だが、
    //   数え始めは**押下した瞬間**＝0.25 秒ぶんの体感の遅れを作らない（押しっぱなし 0.6 秒で完了）。
    //   完了前（0.25〜0.6 秒）に離したら何も起きない＝ロック送りもしない（暴発させない）。
    private const float MouseTapMax = 0.25f;  // これ未満で離せば「短押し」＝ロックオン送り
    private float _mouseHoldT;                // 有効な左クリックを押し続けている秒（0＝押していない）
    private bool _mouseHoldValid;             // この押下がゲームプレイ入力として有効か（会話明けの持ち越しでない）
    private bool _mouseTapFire;               // このフレームに短押し解放が確定したか（TickLockOn が1回だけ読む）
    private bool _mouseChargeHold;            // このフレーム、左クリック長押しをチャージ入力として扱うか
    private bool _lockClearHeld = true;       // 解除（右クリック）のエッジ検出。回避と同じ理由で true 始まり
    private Node2D? _lockTarget;              // 現在のロック先（雑魚・中ボス・ボスのいずれか）
    public bool LockedOn => _locked && IsInstanceValid(_lockTarget!) && _lockTarget != null;
    public Node2D? LockTarget => LockedOn ? _lockTarget : null;

    // ロック中の狙い方向（自機→ボス）。ロックしていなければ従来どおり左右のみ。
    private Vector2 AimVec
    {
        get
        {
            if (!LockedOn) return new Vector2(_facing, 0f);
            var d = _lockTarget!.GlobalPosition - GlobalPosition;
            return d.LengthSquared() > 0.01f ? d.Normalized() : new Vector2(_facing, 0f);
        }
    }
    public Vector2 ShotDir => AimVec;
    public float ShotAngle => LockedOn ? AimVec.Angle() : (_facing >= 0 ? 0f : Mathf.Pi);

    // 5方向の素材を左右反転で八方位へ割り当てる。Godotの角度は上が負。
    private static (string Dir, bool Flip) AimSpriteFor(float angleRad)
    {
        float deg = Mathf.RadToDeg(angleRad);          // -180..180（0=右 / -90=上 / +90=下）
        float a = Mathf.Abs(deg);                      // 左右の差は「反転するか」だけ＝絶対値で八方位を引く
        bool up = deg < 0f;                            // 上半分か（Godot は上が負）
        if (a >= 157.5f) return ("r", true);           // 真左   ← r を反転
        if (a >= 112.5f) return (up ? "ur" : "dr", true);   // 左上 / 左下 ← 斜めを反転
        if (a >   67.5f) return (up ? "u"  : "d",  false);  // 真上 / 真下（左右の別が無い絵）
        if (a >= 22.5f)  return (up ? "ur" : "dr", false);  // 右上 / 右下
        return ("r", false);                           // 真右
    }
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _aimTex = new();
    private string _aimNow = "";
    // ── 表示スケールの単一ソース（2026-09-08）──
    // 全ポーズを事前に余白トリム済みなので、実行時の画素走査なしで表示高を統一できる。
    private static float ScaleFor(Texture2D tex) => 36f / tex.GetHeight();

    private Texture2D AimTexture(string dir) => _aimTex[dir];

    // ロック対象になれるか＝浄化されておらず、**盤面（画面）の中に居る**敵。
    //   画面内判定はカメラのビューポート矩形ではなく Field.Rect を使う。このゲームの描画域は
    //   ビューポート全体ではなく「左のサイドパネル(0..112)＋額縁を除いた Field.Left(120)〜Right(384)」で、
    //   ビューポート判定だとパネルの裏に隠れた敵まで候補に入ってしまう。敵の出現も退場（OffLeftX）も
    //   Field 基準で書かれているので、盤面矩形で見るほうが実装全体と整合する。
    //   上下は少しだけ甘くする（Margin）＝画面の縁に半分だけ見えている敵を「映っていない」扱いにしない。
    private const float LockEdgeMargin = 6f;  // 盤面の縁の許容(px)。半分だけ見えている敵も候補に入れる
    private static bool LockCandidate(Node n, out Enemy e)
    {
        e = null!;
        if (n is not Enemy en || en.IsPurified || !IsInstanceValid(en)) return false;
        var p = en.GlobalPosition;
        if (p.X < Field.Left - LockEdgeMargin || p.X > Field.Right + LockEdgeMargin) return false;
        if (p.Y < Field.Top - LockEdgeMargin || p.Y > Field.Bottom + LockEdgeMargin) return false;
        e = en;
        return true;
    }

    // 左クリックの押し続け時間を計り、短押し（ロック送り）と長押し（溜め打ち）に振り分ける。
    //   TickLockOn と溜め打ちブロックの**両方**がこの結果を読むので、どちらより先に必ず1回だけ呼ぶ。
    //   ゲート（会話中／ゲームオーバー／向き反転が左クリックを握っているとき）は従来の _mouseLockLocked の
    //   作法をそのまま使う＝会話中に押されたクリックは、離すまでゲームプレイ入力として起きてこない。
    private void TickMouseHold(float dt)
    {
        bool down = !FacingFlipEnabled && Pad.MouseDown();
        // 会話中に押されたクリックは、離すまで丸ごと無効（会話明けの1クリックが誤爆するのを止める）。
        if (Hud.BubblePaused && down) _mouseLockLocked = true;
        else if (!down) _mouseLockLocked = false;
        bool live = down && !_mouseLockLocked && !Hud.BubblePaused && !_gameOver;

        if (live)
        {
            // 押下の立ち上がりで計時を始める。以降は押し続けている限り積む。
            if (!_mouseHoldValid) { _mouseHoldValid = true; _mouseHoldT = 0f; }
            _mouseHoldT += dt;
        }
        else if (_mouseHoldValid)
        {
            // 離した（または途中で無効化された）エッジ。短押しだけがロック送りになる。
            // 長押しの解放は溜め打ち側（_chargeHeld の解放エッジ）が撃つので、ここでは何もしない。
            //   ★溜め打ちを**まだ持っていない**あいだは長押しの行き先が無い＝押しっぱなしにすると
            //     ロック送りごと死んでしまう。未取得のうちは長さを問わず従来どおり送る。
            bool tap = _mouseHoldT < MouseTapMax || !(_game?.HasChargeShot ?? false);
            if (tap && !Hud.BubblePaused && !_gameOver) _mouseTapFire = true;
            _mouseHoldValid = false;
            _mouseHoldT = 0f;
        }

        // 長押しがしきいを越えたら、このフレームは溜め打ちの押下入力として扱う。
        // ※充填の時計（_chargeT）は押下起点で数えるので、下の溜め打ちブロックが _mouseHoldT を直接読む。
        _mouseChargeHold = _mouseHoldValid && _mouseHoldT >= MouseTapMax;
    }

    // ロックの送り／解除と対象の維持。
    //   送り（左クリック短押し / F / R1）＝ 自機からの距離順で「今の対象の次」へ。一巡したら先頭（最も近い敵）へ。
    //   解除（右クリック / G / R3）＝ ロックを外すだけ（右クリックは回避と兼用で、回避は別経路で同時に出る）。
    private void TickLockOn()
    {
        // ── 対象の維持：倒された／浄化された／画面外へ出たらロックを落とす ──
        // ここで落としておけば、次の送りは自動的に「一番近い敵」から始まる＝巡回位置のリセットも兼ねる。
        if (_locked && (!IsInstanceValid(_lockTarget!) || _lockTarget == null
                        || !LockCandidate(_lockTarget, out _)))
        { _locked = false; _lockTarget = null; }

        // ── 解除入力＝G / パッド R3 / 右クリック ──
        //   ロックしていなければ何も起きない（右クリックの場合は回避だけが出る。回避は _PhysicsProcess 側で別に出る）。
        //   ★2026-09-17 ユーザー指示でキーボード／パッドにも解除を割り当てた。それまでは
        //     「F / RB の送りで一巡すれば戻ってこられる」として右クリック専用にしていたが、
        //     敵が多いと一巡が長く、狙いを捨てたいだけの操作に手間がかかっていた。
        //   ・G … 送りの F の隣（同じ指のまま押せる）。プロジェクト全体で未使用だった。
        //   ・R3（右スティック押し込み）… 戦闘中の唯一の空きボタン。移動は左スティック＝右親指が空いており、
        //     「送り=RB / 解除=R3」で右手にロックオン操作をまとめられる。
        //     （RB 長押しは既読スキップ、LB=集中、L3=回避、X=ボム、Y=溜め、A=送り、B=ゲームオーバーの抜ける、
        //       Start=メニュー で埋まっている。R3 は旧「やさしさ全開」の枠だが、その機能ごと撤去済み＝完全に空き。）
        bool clearKey = Input.IsKeyPressed(Key.G) || Pad.Pressed(JoyButton.RightStick)
                        || Pad.MouseRightDown();
        if (clearKey && !_lockClearHeld && !Hud.BubblePaused && _locked)
        {
            _locked = false; _lockTarget = null;
            if (Audio.Instance is { } auc) auc.Se(auc.SfxUiMove, volDb: -18f, pitch: 0.85f);
        }
        _lockClearHeld = clearKey;

        // ── 送り入力＝左クリック（短押し）/ F / パッド R1 ──
        // 左クリックは TickMouseHold が短押し／長押しに振り分けたうえで、短押しの解放フレームにだけ
        // _mouseTapFire を立てる（長押しは溜め打ちへ行き、ここには来ない）。F / R1 は従来どおり押下エッジ。
        bool key = Input.IsKeyPressed(Key.F) || Pad.Pressed(JoyButton.RightShoulder);
        bool edge = (key && !_lockHeld) || _mouseTapFire;
        _lockHeld = key;
        _mouseTapFire = false;   // 1フレームぶんのパルス＝読んだら必ず落とす
        if (!edge || Hud.BubblePaused || _gameOver) return;

        // ── 候補を自機からの距離順に並べ、「今の対象の次」を取る ──
        //   毎回ソートし直す＝敵が動けば列も変わるが、**列の中から今の対象の位置を引き直して次を取る**ので
        //   順番が入れ替わっても巡回が飛ばない（インデックスを覚えておく方式だと、敵が動いた瞬間に
        //   別の敵を指してしまう）。今の対象が列から消えていれば先頭＝最も近い敵から。
        var cands = new System.Collections.Generic.List<Enemy>();
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (LockCandidate(n, out var e)) cands.Add(e);
        if (cands.Count == 0) { _locked = false; _lockTarget = null; return; }

        var me = GlobalPosition;
        cands.Sort((a, b) => a.GlobalPosition.DistanceSquaredTo(me)
                              .CompareTo(b.GlobalPosition.DistanceSquaredTo(me)));

        int cur = _locked && _lockTarget != null ? cands.IndexOf((Enemy)_lockTarget) : -1;
        int next = (cur + 1) % cands.Count;   // cur=-1（未ロック/列外）→ 0＝最も近い敵。末尾→先頭へ一巡。
        var prev = _lockTarget;
        _lockTarget = cands[next];
        _locked = true;
        // 対象が変わったら前の敵の照準マーカーを消す（雑魚は毎フレーム再描画しないので明示的に促す）。
        if (prev is Enemy pe && IsInstanceValid(pe) && !ReferenceEquals(pe, _lockTarget)) pe.QueueRedraw();
        if (Audio.Instance is { } au) au.Se(au.SfxUiMove, volDb: -18f, pitch: 1.15f);
    }

    // ───────── 回避（ドッジ）─────────
    // 入力＝ALT / パッド L3(LeftStick)。短い無敵で弾を「すり抜ける」攻めの回避。
    // 値は控えめ＝乱用させず、ここぞの一回が気持ちいい範囲に置く（tunable）。
    private const float DodgeIFrame   = 0.45f;  // 無敵時間（この主旨どおり回避中は被弾しない）
    private const float DodgeDuration = 0.55f;  // 回避モーション全体の長さ（変位＋余韻）。ゆっくり見せる
    private const float DodgeDistance = 64f;    // ダッシュ総変位(px)の基準値。実値は身のこなし強化で伸びる（GameManager.DodgeDistance）
    private const float DodgeCooldown = 0.8f;   // 再回避までのクールダウン基準値。実値は身のこなし強化で縮む（GameManager.DodgeCooldown）
    private const float DodgeAntic    = 0.05f;  // アンティシペーション（一瞬の逆タメ）秒
    private const int   DodgeSpins    = 3;      // 回避中に回す回転数（縦軸ピルエット＝トリプルアクセル感の3）。tunable。
    private const float DodgeLift     = 8f;     // ジャンプスピンの軽い浮き(px・上方向)。やり過ぎない。
    private const float DodgeTrailGap = 0.03f;  // 残像を置く間隔(秒)＝最大10枚程度
    private const float DodgeTrailTtl = 0.18f;  // 残像1枚が消えるまで(秒)
    private const int   DodgeTrailMax = 8;      // 同時に存在する残像の上限（プール）

    private bool _dodgeHeld = false;            // 入力エッジ検出
    private float _dodgeTimer = 0f;             // 残り回避時間（>0で回避中）
    private float _dodgeInv = 0f;               // 残り回避無敵時間（>0で被弾しない）
    private float _dodgeCd = 0f;                // 残りクールダウン
    private Vector2 _dodgeDir = Vector2.Zero;   // 回避の進行方向（正規化）。その場回避では Zero＝変位なし。
    private bool _dodgeInPlace = false;         // 方向入力なしの回避＝その場でスピンのみ（ダッシュ変位ゼロ）。
    private Vector2 _dodgeFrom = Vector2.Zero;  // 回避開始位置
    private float _dodgeDist = DodgeDistance;   // 今回の回避のダッシュ総変位（身のこなし強化込み。TryDodge で確定）
    private float _dodgeTrailAccum = 0f;        // 残像スポーン用タイマ
    private bool _dodgeFlip = false;            // 回避中のスプライト左右反転（スピンの8ステップで真横以降のフレームを FlipH 流用するために使う）

    // 集中の光：敵/パネルの消費点から「この敵本体に1発当たった」を受け取る。
    // Lv0 は完全 no-op。対象が変わったら数え直し（ボーナスは Fire() 側で FocusFireBonus として乗る）。
    public void NotifyShotHit(Node2D target)
    {
        if ((_game?.FocusFireMaxStack ?? 0) <= 0 || target == null) return;
        if (!ReferenceEquals(target, _focusTarget)) { _focusTarget = target; _focusHits = 0; }
        _focusHits++;
    }
    // 現在の集中ボーナス（+0〜+Lv）。FocusFireHitsPerStack 発ごとに1段上がる。
    private int FocusFireBonus => Mathf.Min(_game?.FocusFireMaxStack ?? 0, _focusHits / FocusFireHitsPerStack);

    // HUD・チュートリアル向けの公開アクセサ（挙動には一切影響しない読み取り専用情報）。
    // クールダウンが明けて今すぐ回避できるか（HUD操作ガイドの点灯に使う）。TryDodge の実行可否ガードと同条件に揃える
    //   ＝回避モーション中(_dodgeTimer>0)はまだ再回避できないためHUDも点灯させない。
    //   ★未取得（1面クリア前）は常に false＝「使えるのに光っていない」も「使えないのに光る」も作らない。
    public bool DodgeReady => (_game?.HasDodge ?? true) && _dodgeCd <= 0f && _dodgeTimer <= 0f;
    public int  DodgeCount { get; private set; } // 回避を実行した累計回数（チュートリアルがベースライン比較で実行検出に使う）
    public int  BombCount { get; private set; }  // ボムを発動した累計回数（練習モードでは残数が減らないのでチュートリアルはこの増分で発動検出）
    private float _dodgeSpinSign = 1f;          // スピンの向き（+1=00→01→02… / -1=逆回り）。回避方向から決める。
    private float _baseScaleX = 1f;             // 素の横スケール（高さ正規化値）。フレーム差し替えのたびにこの基準で再計算する。
    private int _dodgeGrazeCount = 0;           // 今回の回避でよけた弾数（farming防止のCap判定用）。TryDodge でリセット。
    private const int DodgeGrazeCap = 12;       // 1回の回避で報酬対象にする弾数の上限（壁に突っ込んで無限に稼げないように）
    // 返し光（counter_light）：回避よけした敵弾を追尾光弾へ変換する。Lv1=2発に1発（上限6/回避）、Lv2=全弾（上限12=DodgeGrazeCap）。
    private int _counterParity = 0;             // Lv1 の間引き用（奇数番目だけ変換）。TryDodge でリセット。
    private int _counterCount = 0;              // 今回の回避で変換した弾数（上限判定用）。TryDodge でリセット。

    // ── 集中の光（focus_fire）：同一敵への連続ヒットで威力段階上昇 ──
    // 敵/パネル側の消費点が NotifyShotHit(本体) を呼び、連続数を自機で数える。対象変更・被弾でリセット。
    private Node2D? _focusTarget;               // いま撃ち込み続けている敵本体
    private int _focusHits;                     // その敵への連続ヒット数
    //
    // バランス査定メモ（新奥義バランス査定・据え置き判断）：
    //   8ヒット自体は基礎連射(0.13s間隔×2〜4ライン)だけでも1秒未満で到達し、この定数がボトルネックではない。
    //   本当の問題は下流にある2点: ①Enemy.cs:468 `Mathf.Clamp(b.Damage, 1, 4)` がボス無防備窓の1ヒット
    //   ダメージを一律4で頭打ちにし、focus_1の前提(rapid_power_1→shot_power_2, 素の威力3)を満たした時点で
    //   ほぼ食い尽くされている。②Panel.cs:103 のインク削りは b.Damage を一切参照しない（Ink--のみ）ため、
    //   雑魚・バズ壁など「パネル持ち」への効果はゼロ。つまり2080コスト（focus_1+focus_2）に対して
    //   実効リターンがほぼ無い＝弱すぎるが、原因はこの定数（発火の速さ）ではなく上記2箇所（このタスクの
    //   対象外システム）にあるため、この値自体は変更しない。根本修正は Enemy.cs / Panel.cs 側の
    //   ダメージ経路の見直しとして別途フォローアップ推奨。
    private const int FocusFireHitsPerStack = 8; // このヒット数ごとに威力+1（Lv1=+1まで / Lv2=+2まで）

    // ── 祈りの帳（veil_light）：回避の終わり際にまとう弾消しの光輪 ──
    private float _veilT;                       // 残り時間（>0で光輪が生きている）
    private float _veilR;                       // 今回の光輪半径（Lv1=20 / Lv2=28px）
    // 通常／回避フレームのテクスチャは _Ready で一度だけロードしてキャッシュ（毎フレームLoad禁止）。
    private Texture2D _idleTex = null!;

    // 回転各アングルの差分イラスト（0/45/90/135/180°）。225/270/315° は FlipH で 03/02/01 を流用。
    private readonly Texture2D[] _spinTex = new Texture2D[5];
    private readonly List<Sprite2D> _trail = new List<Sprite2D>(); // 残像スプライトのプール

    // Pool 取得用キャッシュ
    private BulletPool _pool = null!;

    // GameManager キャッシュ（恒久強化の効果を毎フレーム参照する）
    private GameManager _game = null!;
    // 当たり判定の実効半径（回避域強化で縮小）。_Ready で確定。
    private float _hitR = HitRadius;

    public override void _Ready()
    {
        AddToGroup("player");

        // GameManager をキャッシュ（残機・恒久強化の効果取得）。
        _game = GetNodeOrNull<GameManager>("/root/Game")!;

        // 難易度＋恒久強化（最大♥）に応じた残機。
        Lives = _game?.StartLives ?? 3;

        // 回避域強化で当たり判定を縮小。
        _hitR = HitRadius * (_game?.HitRadiusMul ?? 1f);

        // 拡散サブ（追従オプション）の基数を確定（恒久強化＝ラン中は不変）。
        _optionCount = Mathf.Clamp(_game?.OptionSubCount ?? 0, 0, OptionSlots.Length);

        // 衝突レイヤー: layer=1, mask=12（敵=4, 敵弾=8）
        CollisionLayer = 1;
        CollisionMask = 12;
        Monitoring = true;
        Monitorable = true;

        // 当たり判定（CircleShape2D）。半径は回避域強化で縮む。
        var shape = new CollisionShape2D
        {
            Name = "HitShape",
            Shape = new CircleShape2D { Radius = _hitR }
        };
        AddChild(shape);

        var job = _game?.JobDef ?? Jobs.Get(Job.Tank);
        CharacterId = job.CharacterId;
        var tex = ResourceLoader.Load<Texture2D>(job.PlayerTexturePath);
        if (tex != null)
        {
            _hasTexture = true;
            _idleTex = tex; // 回避終了時に戻す通常テクスチャ
            _sprite = new Sprite2D
            {
                Name = "Sprite",
                Texture = tex,
                Centered = true,
                // 背景に合わせ、なめらか高精細で小さく表示（リニア縮小）
                TextureFilter = CanvasItem.TextureFilterEnum.Linear
            };
            // 表示高さ約36px（弾幕向けに小さめ）。基準は絵の中身の高さ（ScaleFor）。
            {
                float scale = ScaleFor(tex);
                _sprite.Scale = new Vector2(scale, scale);
                _baseScaleX = scale; // 縦軸スピンの cos 駆動はこの素値を基準にする（向きは FlipH 固定＝符号は常に正）。
            }
            AddChild(_sprite);
        }

        for (int i = 0; i < _spinTex.Length; i++)
            _spinTex[i] = ResourceLoader.Load<Texture2D>($"res://char/player/{CharacterId}/{CharacterId}_spin_v2_{i:00}.png");
        foreach (string direction in new[] { "u", "ur", "r", "dr", "d" })
            _aimTex[direction] = ResourceLoader.Load<Texture2D>($"res://char/player/{CharacterId}/{CharacterId}_aim_v2_{direction}.png");

        // 被弾検出（敵 / 敵弾）
        AreaEntered += OnAreaEntered;

        // グレイズ判定エリア（自機より広い円。敵弾(=layer8)のかすりを検出）
        var grazeArea = new Area2D
        {
            Name = "GrazeArea",
            CollisionLayer = 0,
            CollisionMask = 8, // 敵弾
            Monitoring = true,
            Monitorable = false,
        };
        grazeArea.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = GrazeRadius } });
        AddChild(grazeArea);
        grazeArea.AreaEntered += OnGrazeAreaEntered;

        // Pool 取得
        _pool = GetNode<BulletPool>("/root/Pool");

        ZIndex = 10;

        // 被弾点は専用の子ノードで、スプライトより前に描く（下の PlayerHitDot の説明を参照）。
        // ZAsRelative（既定 true）なので ZIndex=1 は「自機 10 に対し +1＝11」の意味になる。
        AddChild(new PlayerHitDot { Name = "HitDot", Radius = _hitR, ZIndex = 1, CharacterId = CharacterId });

        // 開始/リスタート直後の被弾を防ぐスポーン無敵（点滅）
        _invincible = true;
        _invincibleTimer = 1.5f;
        _blinkPhase = 0f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // 操作ガイドの KB/パッド出し分け用に、直近デバイスを毎フレーム判定。
        Pad.PollDevice();
        // マウス操作中か（Pad が座標・ボタン・ホイールから毎フレーム判定。KB/パッドを触れば false に落ちる）。
        // ポーズ中は _PhysicsProcess 自体が止まり、会話中は下の BubblePaused ゲートで無効化される。
        bool mouse = Pad.UsingMouse;

        // 移動入力。会話中（吹き出し表示中）・ゲームオーバー後は動けない。
        Vector2 dir = Vector2.Zero;
        if (!Hud.BubblePaused && !_gameOver)
        {
            dir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
            // WASD でも動けるように。矢印キーは「↑+←+Z」など3キー同時押しが
            // 安価なキーボードでゴースト（入力が消える）するため、代替手段を用意する。
            Vector2 wasd = new Vector2(
                (Input.IsKeyPressed(Key.D) ? 1f : 0f) - (Input.IsKeyPressed(Key.A) ? 1f : 0f),
                (Input.IsKeyPressed(Key.S) ? 1f : 0f) - (Input.IsKeyPressed(Key.W) ? 1f : 0f));
            if (wasd != Vector2.Zero) dir = wasd;
            dir = dir.LimitLength(1f);
        }
        // 速度は1本（低速移動は 2026-09-13 に廃止）。機動力強化(MoveSpeedMul)と
        // ジョブの移動補正（結び手のみ ×0.88＝「避けるのではなく耐える」）を素の速度に乗せる。
        float jobMove = _game?.JobDef.MoveMul ?? 1f;
        float speed = NormalSpeed * (_game?.MoveSpeedMul ?? 1f) * jobMove * PowerMoveMultiplier;
        // ロックオン中は足を重くする＝照準を任せるあいだの対価。
        // 左クリックの短押し／長押し判定は、それを読む TickLockOn・溜め打ちより必ず先に1回だけ回す。
        TickMouseHold(dt);
        TickLockOn();
        if (LockedOn) speed *= LockMoveMul;

        // 回避入力＝ALT（左Alt想定）/ パッド L3。空き弾の無い瞬間に「攻めで抜ける」短い無敵ダッシュ。
        // 方向は移動入力があればその方向へ変位ダッシュ、無ければその場回避（変位ゼロ＝スピン＆無敵だけ）。
        // マウス時は右クリックが回避。
        // 右クリックは**回避とロック解除を兼ねる**（2026-09-08 ユーザー指示。両方が同時に起きてよい）。
        // 解除そのものは TickLockOn 側で拾う＝ここは回避だけを見る。
        bool dodgeKey = Input.IsKeyPressed(Key.Alt) || Pad.Pressed(JoyButton.LeftStick)
                        || (mouse && Pad.MouseRightDown());
        if (dodgeKey && !_dodgeHeld && !Hud.BubblePaused)
        {
            // マウス時は方向キーが無いので「カーソルの方向」を回避方向として使う（十分離れている時だけ）。
            // カーソルにほぼ張り付いている＝行き先が無いときは従来どおりその場回避（変位ゼロ）になる。
            Vector2 ddir = dir;
            if (mouse && ddir == Vector2.Zero)
            {
                Vector2 toCur = GetGlobalMousePosition() - GlobalPosition;
                if (toCur.LengthSquared() > 64f) ddir = toCur.Normalized(); // 8px 超で方向あり
            }
            TryDodge(ddir);
        }
        _dodgeHeld = dodgeKey;
        if (_dodgeCd > 0f) _dodgeCd -= dt;
        if (_dodgeInv > 0f) _dodgeInv -= dt;

        Vector2 pos;
        if (_dodgeTimer > 0f)
        {
            // 回避中は入力移動を無視し、ダッシュ軌道で位置を駆動する。
            _dodgeTimer -= dt;
            float t = Mathf.Clamp(1f - _dodgeTimer / DodgeDuration, 0f, 1f); // 0→1
            // アンティシペーション（最初だけ逆方向にわずかに引く）→ イーズアウトで一気に滑る→ 余韻。
            float anticK = DodgeAntic / DodgeDuration;
            float disp;
            if (t < anticK)
                disp = -_dodgeDist * 0.12f * (t / anticK);               // 逆タメ
            else
            {
                float u = (t - anticK) / (1f - anticK);                  // 0→1
                disp = _dodgeDist * (1f - (1f - u) * (1f - u));          // ease-out quad（フォロースルー）
            }
            pos = _dodgeFrom + _dodgeDir * disp;
            // 残像トレイル（一定間隔で薄い分身を置く。テクスチャはキャッシュ済み＝Load しない）。
            _dodgeTrailAccum += dt;
            if (_dodgeTrailAccum >= DodgeTrailGap)
            {
                _dodgeTrailAccum = 0f;
                SpawnTrail();
            }
            if (_dodgeTimer <= 0f) EndDodge();
        }
        else if (mouse && !Hud.BubblePaused && !_gameOver)
        {
            // マウス追従：カーソル（ワールド座標）へ指数補間で寄る。ゲームオーバー後は
            // KB/パッド移動と同じく停止（R/Q の選択待ちに専念させる）。GetGlobalMousePosition は
            // キャンバス変換（GameCamera のシェイク込み）を通した値＝プレイ領域 384×216 と同じ系。
            // 1フレームの移動量は既存の速度上限(speed)でクランプ＝KB/パッドより速く動けることはない。
            Vector2 target = GetGlobalMousePosition();
            target.X = Mathf.Clamp(target.X, MinX, MaxX);
            target.Y = Mathf.Clamp(target.Y, MinY, MaxY);
            Vector2 to = target - GlobalPosition;
            Vector2 step = to * (1f - Mathf.Exp(-MouseFollowResponse * dt));
            step = step.LimitLength(speed * dt);
            pos = to.Length() <= MouseSnapDist ? target : GlobalPosition + step;
            // バンク（体の傾き）は KB/パッドと同じく「進行方向の強さ」で駆動する＝マウスでも姿勢が付いてくる。
            // 実移動量を「その1フレームで出せる最大量(speed*dt)」で割って、KB の dir(長さ0〜1) と同じ尺度に揃える。
            dir = ((pos - GlobalPosition) / Mathf.Max(0.0001f, speed * dt)).LimitLength(1f);
        }
        else
        {
            pos = GlobalPosition + dir * speed * dt;
        }

        // プレイ領域内にクランプ
        pos.X = Mathf.Clamp(pos.X, MinX, MaxX);
        pos.Y = Mathf.Clamp(pos.Y, MinY, MaxY);
        GlobalPosition = pos;
        // 残像は自機に追従しない独立座標なので、毎フレーム減衰させる。
        UpdateTrail(dt);

        // ショット
        if (_fireCooldown > 0f)
            _fireCooldown -= dt;

        // ショットはオート発射（下の shoot 判定）。ここでは初回に HUD へ現在モードを通知
        // （HUD の _Ready 順に依存しないよう最初の物理フレームで）。
        // ★2026-09-13 ジョブ導入：モードはジョブが決める従属値になったので、V／パッドB／マウスホイールの
        //   切替ローテは廃止した（設計書 §3「切り替える対象が無いので操作が1つ減る」）。
        //   ここは「今のジョブのモードを HUD へ1度伝える」だけの窓口になる。
        if (!_modeInit && _game != null)
        {
            (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetShotMode(_game.SelectedShotMode, false);
            _modeInit = true;
        }

        // 向き反転＝F / パッド RB / 左クリック。押した瞬間だけ反転するトグル（押しっぱなし不要）。会話中は不可。
        // 反転は _facing のみを書き換える＝射撃方向も見た目(FlipH)も下流がここを読んで追従する。
        // 左クリックは会話送りと兼用なので、会話中に押されていたクリックは離すまで反転に使わない
        //（会話明けの1クリックが向き反転へ流れ込む誤爆を止める）。
        // FacingFlipEnabled=false のあいだは入力を一切読まない（パッド側の結線はそのまま残す）。
        bool mouseL = FacingFlipEnabled && Pad.MouseDown();
        if (Hud.BubblePaused && mouseL) _mouseFlipLocked = true;
        else if (!mouseL) _mouseFlipLocked = false;
        bool flipKey = FacingFlipEnabled
                    && (Input.IsKeyPressed(Key.F) || Pad.Pressed(JoyButton.RightShoulder)
                     || (mouseL && !_mouseFlipLocked));
        if (flipKey && !_flipHeld && !Hud.BubblePaused && !_gameOver)
        {
            _facing = -_facing;
            FxLayer.Instance?.Muzzle(GlobalPosition + ShotDir * 20f); // 「向きが変わった」を銃口側の一閃で示す
        }
        _flipHeld = flipKey;

        // ショットはオート＝射撃ボタンは無い。「押しっぱなしと同じ状態」が常に続く。
        // Z / Space / Enter / A / 左クリックは会話送り（Pad.AdvanceHeld）専用に戻した。
        // 撃てない条件：会話中（吹き出し表示中）／緊急回避中（回避は「避け」に専念）／
        // ゲームオーバー後（R/Q の選択待ちに専念させる）。
        bool shoot = !Hud.BubblePaused && _dodgeTimer <= 0f && !_gameOver;
        if (shoot && _fireCooldown <= 0f)
        {
            Fire();
            // モード別の間隔税。各モードの速射ノード（rapid/spread/homing_rate）で
            // 税が軽くなる＝ChainLevel 経由で GameManager が算出（ショップのスペック表記と同期）。
            //   連射＝rapid_rate で ×0.94/0.88（基礎1.0）。拡散＝spread_rate で 1.45→1.35。ホーミング＝homing_rate で 1.55→1.40。
            float modeMul = _game?.SelectedShotMode switch
            {
                GameManager.ShotMode.Homing => _game?.HomingRateMul ?? 1.55f,
                GameManager.ShotMode.Spread => _game?.SpreadRateMul ?? 1.45f,
                GameManager.ShotMode.Accel => 1f, // 加速球は速射（rapid_rate）の対象外＝ショップ説明文どおり連射専用
                _ => _game?.RapidRateMul ?? 1f,
            };
            _fireCooldown = FireInterval * (_game?.FireIntervalMul ?? 1f) * modeMul;
        }

        // バックファイア（後方弾・淡い金の菱形）：2026-09-15 ユーザー指示で廃止（発射のみ停止、FireBackfire は復活可能なまま残置）。

        // ボム（X）: 押した瞬間だけ発動
        // ボム＝X / Xボタン（□）
        bool bombKey = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.X)
                       || (mouse && Pad.MouseMiddleDown()); // マウス時は中クリック
        if (bombKey && !_bombHeld && !Hud.BubblePaused && !_gameOver)
            TryBomb();
        _bombHeld = bombKey;

        // 溜め打ち（C / パッドY 長押し、または左クリック長押し）：#6「溜め打ち」を持っているあいだだけ。
        //   押しているあいだ _chargeT を積み、ChargeNeed に届いてから離すと大玉が出る。
        //   届く前に離した／会話に入った／被弾した場合は黙って捨てる（暴発させない）。
        //   左クリック（_mouseChargeHold）だけは充填の起点が違う：短押し判定の 0.25 秒が過ぎてから
        //   数え始めると、Cキーより 0.25 秒ぶん遅れて完了して手触りが噛み合わない。押下からの経過
        //   （_mouseHoldT）をそのまま充填時間に使う＝**押しっぱなし 0.6 秒で完了**でキーと揃う。
        bool chargeHas = _game?.HasChargeShot ?? false;
        bool chargeKeyRaw = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        bool chargeKey = chargeHas && (chargeKeyRaw || _mouseChargeHold);
        if (chargeKey && !Hud.BubblePaused && !_gameOver && _dodgeTimer <= 0f)
        {
            // キーとマウスを同時に握っていたら、進んでいるほうを採る（どちらか一方でも完了させる）。
            if (chargeKeyRaw) _chargeT += dt;
            if (_mouseChargeHold) _chargeT = Mathf.Max(_chargeT, _mouseHoldT);
        }
        else if (_chargeHeld)
        {
            // 離したエッジ：充填できていれば撃つ。どちらにせよ充填はここで空にする。
            if (ChargeFull && !Hud.BubblePaused && !_gameOver) FireCharge();
            _chargeT = 0f;
        }
        else _chargeT = 0f;
        _chargeHeld = chargeKey;

        // 集中モード（V / マウスのホイール回転・サイドボタン / パッド L1）：#10「集中モード」。
        //   敵側の時間だけ ×0.35 に落とす（1.5秒・CD20秒）。
        //   時計は実時間で送る＝自機側の delta。Engine.TimeScale は触らない（GameManager.EnemyTimeScale 参照）。
        //   L1 は低速移動の廃止（2026-09-13）で空いた枠。ホイールは戦闘中これまで未使用だった
        //   （ショット切替はジョブ導入で廃止済み）。サイドボタンは XButton1/2 の両方を拾う。
        _game?.TickFocusMode(dt);
        bool focusKey = Input.IsKeyPressed(Key.V) || Pad.Pressed(JoyButton.LeftShoulder)
                        || Pad.MouseSideDown();
        bool focusEdge = focusKey && !_focusModeHeld;
        // ホイールは押下状態を持たない＝1回転ぶんのパルス。読んだ時点でラッチを落とす（連続発動しない）。
        //   会話中／ゲームオーバー中も必ず消費して、溜めた回転が明けた瞬間に暴発するのを防ぐ。
        bool wheelTurn = Pad.ConsumeWheelTurn();
        if ((focusEdge || wheelTurn) && !Hud.BubblePaused && !_gameOver)
        {
            if (_game?.TryFocusMode() ?? false)
            {
                FxLayer.Instance?.PurifyBurst(GlobalPosition);
                Audio.Instance?.PlayGraze();
            }
        }
        _focusModeHeld = focusKey;
        // HUD へ集中モードの状態を反映（旧ヒカゲスキルのチップ枠をそのまま使う）。
        //   バーは 発動中＝残り持続 ／ それ以外＝CDの充填、で読ませる。
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetFocusMode(
            _game?.HasFocusMode ?? false, _game?.FocusModeReady ?? false, _game?.FocusModeActive ?? false,
            (_game?.FocusModeActive ?? false) ? (_game?.FocusModeRatio ?? 0f) : 1f - (_game?.FocusModeCdRatio ?? 0f));
        // ※ 回避CDの HUD 通知（SetDodgeReady）は 2026-09-07 に廃止。唯一の読み手だった常駐操作ガイド
        //   （Hud.DrawControls）を撤去したため（案内は Esc メニュー →「あそびかた」に集約）。

        // 無敵・点滅更新
        if (_invincible)
        {
            _invincibleTimer -= dt;
            _blinkPhase += dt;
            if (_invincibleTimer <= 0f)
            {
                _invincible = false;
                _hitInvincible = false; // 被弾無敵が明けたら経済加算を再開
                SetSpriteVisible(true);
            }
            else
            {
                // 約 20Hz で点滅
                bool show = ((int)(_blinkPhase * 20f) % 2) == 0;
                SetSpriteVisible(show);
            }
        }

        // 敵本体との「めり込みっぱなし」被弾（AreaEntered だけでは取りこぼす）。
        //   Area2D.AreaEntered は重なり始めの1回しか鳴らない。敵に触れて被弾 → 1.2秒の無敵、
        //   その間も自機が敵の中に居続けると、無敵が明けても新しい侵入イベントが起きないため
        //   **二度と被弾しない**（実測: カメオの中に32秒居座って残機の減りは1回だけ）。
        //   スポーン無敵(1.5秒)の最中に敵と重なった場合も同じで、1回目の被弾すら消える。
        //   ＝ユーザー実機指摘「敵にぶつかってもダメージが出ない」の正体。
        //   無敵が明けているフレームだけ、今まさに重なっている敵を毎フレーム見て被弾させる。
        if (!_invincible && _dodgeInv <= 0f && !_gameOver && Monitoring)
        {
            foreach (var area in GetOverlappingAreas())
            {
                if (area is Enemy oe && !oe.IsPurified)
                {
                    if (!QaPilot.GodActive) TakeHit();
                    break;
                }
            }
        }

        // グレイズ残光の減衰
        if (_grazeFlash > 0f)
            _grazeFlash = Mathf.Max(0f, _grazeFlash - GrazeFlashDecay * dt);

        // 祈りの帳の光輪：残り時間の間、半径内の敵弾を花びらに変えて消す（ボムの範囲消去の縮小版）。
        // 消した弾は加点（AddVeilCleared）＝“祈りが受け止めた”が点でも報われる。
        if (_veilT > 0f)
        {
            _veilT -= dt;
            foreach (Node node in GetTree().GetNodesInGroup("enemy_bullets"))
            {
                if (node is Bullet vb && vb.Active && vb.GlobalPosition.DistanceTo(GlobalPosition) <= _veilR)
                {
                    _game?.AddVeilCleared();
                    FxLayer.Instance?.BulletToPetal(vb.GlobalPosition);
                    _pool?.Despawn(vb);
                }
            }
        }

        // 体のリアクション各種の減衰（被弾のけぞり／発射反動／ボム解放）。
        if (_hitReact > 0f) _hitReact = Mathf.Max(0f, _hitReact - dt);
        if (_recoil > 0f) _recoil = Mathf.Max(0f, _recoil - RecoilDecay * dt);
        if (_bombCast > 0f) _bombCast = Mathf.Max(0f, _bombCast - dt);

        // 常時ふわふわ浮遊＋移動バンク（スプライトのみ。当たり判定点は固定）
        _bobTime += dt;

        // 慣性つきで入力方向へ寄せる（会話中は dir=0 なので自然に直立へ戻る＝余韻）。
        _lean = _lean.Lerp(dir, 1f - Mathf.Exp(-LeanResponse * dt));
        if (_hasTexture && _sprite != null)
        {
            float bobY = Mathf.Sin(_bobTime * BobSpeed) * BobAmp;
            // 向き反転（★#4 改訂）：左向き(_facing<0)ならスプライトを左右反転して顔と銃口を射撃方向へ向ける。
            // 回避スピン中は ApplySpinFrame が FlipH を「回転フレームの流用」として握るので、ここでは触らず、
            // スピン側が _facing と XOR して合成する（EndDodge も _facing 基準へ戻す）。
            if (_dodgeTimer <= 0f) _sprite.FlipH = _facing < 0;
            // ロック中は狙っている方向の絵に差し替える（弾がボスへ飛ぶのに絵が右向きのまま、を避ける）。
            if (_dodgeTimer <= 0f)
            {
                var (aimDir, flip) = LockedOn ? AimSpriteFor(AimVec.Angle()) : ("", false);
                if (aimDir != _aimNow)
                {
                    _aimNow = aimDir;
                    _sprite.Texture = aimDir.Length > 0 ? AimTexture(aimDir) : _idleTex;
                    _baseScaleX = ScaleFor(_sprite.Texture);
                }
                // 左半分の方向は右向きの絵を左右反転して作る。**向き反転（_facing）とは別系統**で、
                // ここでは _facing に一切書かない＝上の FlipH 代入の結果を、この1フレームぶんだけ上書きする。
                if (LockedOn) _sprite.FlipH = flip;
            }
            // 進行方向へわずかに先行（体が動きをリードする）。bob は縦に重畳。
            _sprite.Position = new Vector2(_lean.X * LeadPx, bobY + _lean.Y * LeadPx);
            // 前傾＋バンク：射撃方向への移動で前へ、上下移動で機首を振る（前進は深く・後退は浅い非対称バンク）。
            // 左向き時はスプライトが FlipH で反転する＝Rotation の見た目も左右反転するので、
            // 傾き角そのものに _facing を掛けて打ち消し、「進行方向へ倒れる」画を両向きで保つ。
            float bankX = _lean.X * _facing >= 0f ? BankXFwd : BankXBack; // 前後は「向きから見て」判定する
            _sprite.Rotation = (_lean.X * bankX + _lean.Y * BankY) * _facing;
            // ── 体のリアクション（被弾のけぞり／発射反動／ボム解放）。回避中はスピンが姿勢を握るので触らない ──
            // スケールはここで毎フレーム確定する（リアクション無し＝素値）＝復帰の状態管理を持たない。
            if (_dodgeTimer <= 0f)
            {
                // 発射反動：銃口（射撃方向）と逆へ小さくキックバック。連射のリズムが体に乗る。
                _sprite.Position += new Vector2(-RecoilPx * _recoil * _facing, 0f);
                Vector2 scl = new Vector2(_baseScaleX, _baseScaleX);
                if (_hitReact > 0f)
                {
                    // のけぞり：残量^2＝直後に最大→スッと復帰（余韻）。後方へ倒れ・沈み・潰れる squash。
                    // 変位は世界座標なので -_facing 側へ、Rotation は FlipH で見た目が反転するぶん _facing を掛ける。
                    float e = _hitReact / HitReactDur; e *= e;
                    // ★結び手の「踏みとどまり」（設計書 §2・手触りの本体）：変位と傾きを 0 にする。
                    //   他ジョブは押し出されて位置を失うが、結び手だけその場に留まる。
                    //   squash（潰れ）と点滅は残す＝「当たった」という情報自体は落とさない。
                    float knock = (_game?.JobDef.NoHitKnockback ?? false) ? 0f : 1f;
                    _sprite.Position += new Vector2(-5f * e * _facing, 1.5f * e) * knock;
                    _sprite.Rotation += -0.32f * e * _facing * knock; // 後ろ（射撃方向と逆）へのけぞる
                    scl = new Vector2(_baseScaleX * (1f + 0.10f * e), _baseScaleX * (1f - 0.16f * e));
                }
                else if (_bombCast > 0f)
                {
                    // ボム解放：発動フレームで最大の伸び上がり（stretch＋浮き）→ 減衰で静かに降りる（余韻）。
                    float e = _bombCast / BombCastDur; e *= e;
                    _sprite.Position += new Vector2(0f, -5f * e);
                    scl = new Vector2(_baseScaleX * (1f - 0.08f * e), _baseScaleX * (1f + 0.14f * e));
                }
                _sprite.Scale = scl;
            }
            // 汚染ティント（光が濁っていく。被弾点滅のαとは独立に SelfModulate へ）。
            _sprite.SelfModulate = CleanTint.Lerp(MurkTint, _corruption);
            // 残光（_grazeFlash）はグレイズ境界リングを廃止した（2026-09-08）ぶん、絵そのものの発光で返す。
            // TutorialGlow() がステージ0で「自機を光らせて目立たせる」のに同じ値を使うので、
            // リングと一緒に消すとチュートリアルの誘導が黙って死ぬ。行き先だけ絵側へ移した。
            if (_grazeFlash > 0f)
            {
                float g = 1f + 0.55f * _grazeFlash;
                _sprite.SelfModulate = new Color(_sprite.SelfModulate.R * g, _sprite.SelfModulate.G * g, _sprite.SelfModulate.B * g);
            }

            // ── 回避中は「その場ピルエット」＝縦軸まわりの本物のスピン（回転各アングルの差分イラストを送る）──
            // 旧実装の Scale.X=cos によるカードスピン擬似（紙っぽさの原因）は廃止し、5枚のフレーム＋FlipH 流用で
            // 全周8ステップのフレームアニメにする。Rotation はスピン中 0（直立）を保つ。
            if (_dodgeTimer > 0f)
            {
                float k = Mathf.Clamp(1f - _dodgeTimer / DodgeDuration, 0f, 1f); // 0→1
                // 変位と同じカーブ：アンティシペーション区間は据え置き、本体は ease-out quad（回り出し速く終端で減速）。
                float anticK = DodgeAntic / DodgeDuration;
                float spinEase = k < anticK ? 0f : 1f - (1f - (k - anticK) / (1f - anticK)) * (1f - (k - anticK) / (1f - anticK));
                // 位相 θ：0→2π*DodgeSpins。終端で spinEase=1 → 周回数ちょうど → 正面(00)に着地する。
                float theta = Mathf.Tau * DodgeSpins * spinEase;
                ApplySpinFrame(theta);
                _sprite.Rotation = 0f;
                // ジャンプスピンの軽い浮き＋進行方向への先行（やり過ぎない）。
                _sprite.Position += _dodgeDir * (3f * Mathf.Sin(k * Mathf.Pi)) - new Vector2(0f, DodgeLift * Mathf.Sin(k * Mathf.Pi));
                // 回避無敵を発光＋点滅で可視化（i-frame 終了で通常へ）。
                if (_dodgeInv > 0f)
                {
                    float gl = 0.6f + 0.4f * Mathf.Sin(_bobTime * 40f);
                    _sprite.SelfModulate = new Color(0.8f, 1.1f, 1.4f).Lerp(new Color(1.4f, 1.6f, 2.0f), gl);
                }
            }
        }

        // ヒットボックス点を毎フレーム更新描画
        QueueRedraw();
    }

    private void Fire()
    {
        if (_pool == null)
            return;

        // 銃口（中心からやや前方＝_facing 側）。光の出力強化でダメージ増。
        // フォロワー由来の火力バフ（FollowerPowerMul・上限+50%）をここで実配線＝拡散力(fol_gain)が“火力の遠回り投資”として生きる。
        Vector2 muzzle = GlobalPosition + ShotDir * 20f;
        // 集中の光（focus_fire）：同じ敵に当て続けた集中ボーナス（+0〜+Lv）を基礎威力へ上乗せ。
        // ジョブの基礎威力補正（JobDef.PowerMul）もここへ乗せる＝本体・パネル・雑魚の全経路に同じ係数が届く
        // （祈り手だけ ×0.8＝4ジョブ最遅。他3ジョブは 1.0 で従来どおり）。下限1は据え置き。
        // ★2026-09-13 一本道13段：基礎威力は 1 固定になり、#4「火力 2倍」が最終段の倍率（×2）として掛かる。
        //   掛ける順は フォロワーバフ → ジョブ補正 → 火力2倍。丸めは1回だけ（段ごとに丸めると2倍が2倍にならない）。
        int dmg = Mathf.Max(1, Mathf.RoundToInt(1f
                                                * (_game?.FollowerPowerMul ?? 1f)
                                                * (_game?.JobDef.PowerMul ?? 1f)
                                                * (_game?.ShotPowerMul ?? 1))) + FocusFireBonus;

        // 選択中のショットモードで発射パターンを分岐（設計書 §3）。
        switch (_game?.SelectedShotMode ?? GameManager.ShotMode.Rapid)
        {
            case GameManager.ShotMode.Spread: FireSpread(muzzle, dmg); break;
            case GameManager.ShotMode.Homing: FireHoming(muzzle, dmg); break;
            case GameManager.ShotMode.Accel:  FireAccel(muzzle, dmg);  break;
            default:                          FireRapid(muzzle, dmg);  break;
        }

        // フォロワーは2回に1回の同期発射
        _shotParity++;
        if ((_shotParity & 1) == 0)
            foreach (var f in _followers)
                f.Fire();

        // 拡散サブ（option_sub・拡散幹の奥義）：上下の追従オプションがメインと毎ショット同期で
        // 威力×0.5（下限1）の光弾を撃つ。全モード共通＝面の継続火力を底上げする。
        for (int i = 0; i < _optionCount && i < OptionSlots.Length; i++)
        {
            Vector2 op = GlobalPosition + OptionSlots[i];
            _pool.Spawn(op + ShotDir * 8f, ShotDir * 340f, isEnemy: false, 2.6f,
                Mathf.Max(1, Mathf.RoundToInt(dmg * 0.5f)));
        }

        // マズルフラッシュ＋発射音（画と同フレーム）＋体のキックバック（反動）
        FxLayer.Instance?.Muzzle(muzzle, _game?.SelectedShotMode ?? GameManager.ShotMode.Rapid,
                                 _game?.SpreadWays ?? 5, ShotDir);
        Audio.Instance?.PlayShot();
        _recoil = 1f;
    }

    // 連射：射撃方向（_facing）へ直線の高速ストリーム。線数 = 2 + ExtraLines（#8「ライン +1」）。
    //   ★線数を火力から導出するのは 2026-09-13 にやめた（火力を買ったら勝手に線が増える＝どちらの段の
    //     効果か読めなかった）。線数は線数の段だけが決める。
    // 貫通（#12「貫通」）：全モード共通で敵を 1 体貫通（Bullet.Pierce。消費側が減算する）。
    private void FireRapid(Vector2 muzzle, int dmg)
    {
        Vector2 vel = ShotDir * 360f;
        int pierce = _game?.ShotPierceCount ?? 0;
        int rdmg = dmg + (_game?.RapidPowerBonus ?? 0); // 連射モード専用の威力上乗せ
        int lines = 2 + (_game?.ExtraLines ?? 0) + LinePower;
        float[] offs = lines <= 2 ? new[] { -4f, 4f }
                     : lines == 3 ? new[] { -6f, 0f, 6f }
                     : lines == 4 ? new[] { -10f, -4f, 4f, 10f }
                                  : new[] { -12f, -6f, 0f, 6f, 12f };
        foreach (float dy in offs)
            _pool.Spawn(muzzle + new Vector2(0f, dy), vel, isEnemy: false, 3f, rdmg, BulletShape.Dart).Pierce = pierce;
    }

    // 加速球：発射したら自機のすぐ前でほぼ静止して“タメ”を作り、タメ後にロケットのように急加速して発進する。
    //   タメ速度=12px/s（ほぼその場・「タメている」のが分かる程度）。タメ時間/発進速度/威力は加速球系ノードで可変：
    //   タメ 0.8→0.65→0.5s（速填）／発進 640→760px/s（推進強化）／威力 +1/+2（加速威力）。
    //   数値は GameManager のアクセサを毎発射時に読む＝ショップ購入・トレーニングの付け外しで即反映。
    private void FireAccel(Vector2 muzzle, int dmg)
    {
        const float charge = 12f;
        float fast = _game?.AccelLaunchSpeed ?? 640f;
        float delay = _game?.AccelChargeDelay ?? 0.8f;
        int admg = dmg + (_game?.AccelPowerBonus ?? 0); // 加速球専用の威力軸（加速威力ノード）

        // 同時タメ中の弾数を上限化：無効化(非Active)/発進済み分を掃除してから残数を確認し、
        // 上限（AccelChargeCap）到達中は新規スポーンをスキップ＝タメ中弾の自弾グローが自機前方に積み上がるのを防ぐ。
        _accelCharging.RemoveAll(b => b == null || !b.Active || !b.AccelCharging);
        int lines = 2 + (_game?.ExtraLines ?? 0) + LinePower;
        int chargeCap = AccelChargeCap + LinePower * 3;
        if (_accelCharging.Count + lines > chargeCap)
            return;

        // 上下2本（連射と同じ正面集中の手触り）＋ #8「ライン +1」で1発増える。
        //   発進方向は Spawn の vel（射撃方向）で確定し、MakeAccel が初速をタメへ落とす。
        //   貫通（#12）は 2026-09-13 から全モード共通＝加速球にも乗せる。
        int pierce = _game?.ShotPierceCount ?? 0;
        float[] adys = lines <= 2 ? new[] { -4f, 4f }
                     : lines == 3 ? new[] { -8f, 0f, 8f }
                     : lines == 4 ? new[] { -10f, -4f, 4f, 10f }
                                  : new[] { -12f, -6f, 0f, 6f, 12f };
        foreach (float dy in adys)
        {
            var b = _pool.Spawn(muzzle + new Vector2(0f, dy), ShotDir * fast, isEnemy: false, 3.4f, admg);
            b.MakeAccel(charge, fast, delay); // タメ(ほぼ静止)→delay秒後に発進
            b.Pierce = pierce;
            _accelCharging.Add(b);
        }
    }

    // 拡散：射撃方向（ShotAngle）を基準に扇状 n-way（±35°）。1発威力 ×SpreadPowerMul（0.50→0.56→0.62・拡散威力ノードで是正）。
    // 連鎖の光（chain）：拡散弾のみ跳弾数を付与（ヒット時に Bullet.TryChain が跳ねる）。
    private void FireSpread(Vector2 muzzle, int dmg)
    {
        int n = Mathf.Max(5, _game?.SpreadWays ?? 5) + LinePower;
        int sdmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (_game?.SpreadPowerMul ?? 0.50f)));
        int chain = _game?.ChainLightBounces ?? 0;
        int spierce = _game?.ShotPierceCount ?? 0; // 貫通（#12）は 2026-09-13 から全モード共通
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0f : (float)i / (n - 1) - 0.5f;
            float ang = ShotAngle + t * Mathf.DegToRad(70f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var sb = _pool.Spawn(muzzle, dir * 320f, isEnemy: false, 3f, sdmg, BulletShape.Petal);
            sb.Chain = chain;
            sb.Pierce = spierce;
        }
    }

    // ホーミング：追尾弾を扇状に放ち、射撃方向（ShotAngle）側の穢れへ曲射。弾速200。追尾数 2→3→4（誘導Lv）。
    // 1発威力 ×HomingPowerMul（0.85→0.95→1.05・誘導威力ノードで是正）。誘導速射なら旋回を上書き（200）。
    private void FireHoming(Vector2 muzzle, int dmg)
    {
        int shots = Mathf.Max(1, _game?.HomingShots ?? 2) + LinePower;
        int hdmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (_game?.HomingPowerMul ?? 0.85f)));
        int turn = _game?.HomingTurnRateOverride ?? 0; // 0=Bullet 既定（150）を使う
        int hpierce = _game?.ShotPierceCount ?? 0;     // 貫通（#12）は 2026-09-13 から全モード共通
        for (int i = 0; i < shots; i++)
        {
            float t = shots == 1 ? 0f : (float)i / (shots - 1) - 0.5f;
            float ang = ShotAngle + t * Mathf.DegToRad(40f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var hb = _pool.Spawn(muzzle, dir * 200f, isEnemy: false, 3f, hdmg, BulletShape.Seeker, null, homing: true); // 弾速 260→200
            if (turn > 0) hb.TurnRateOverride = turn;
            hb.Pierce = hpierce;
        }
    }

    // バックファイア：後方＝射撃方向の反対側（-ShotDir）へ軽ホーミング弾を撃つ。撃ったら true。
    //   ★向き反転（_facing）に追従する：左向きにすると「後方」は右になる＝メインと同じ方向へ二重に
    //     撃つことはない（常にメインの真逆＝火力バランスは向きに依らず不変）。
    //   ・後方最寄りの未浄化の敵がいればそちらへ狙いを付け、いなくても真後ろへ撃つ
    //     （旧仕様は「後方に敵が居なければ撃たない」＝道中は敵が右から来るので後方弾がほぼ一度も出ず、
    //       「後方の光を買ったのに後ろに弾が出ない」とプレイヤーに読まれていた。撃つ＝機能が見える）。
    //   ・数値は GameManager（bf_* ノードの ChainLevel）から：ダメージ 1→2/3/4・同時発数・旋回。弾速180。
    //   ・弾は後方追尾フラグ（backwardHoming）付きで Spawn＝Bullet.AcquireTarget が X<self を探す（前方弾と別探索）。
    //   ・見た目は Diamond 形＋穢れ寄りの tint で前方弾（水色円）と区別し、後方マズルフラッシュを出す。
    private bool FireBackfire()
    {
        if (_pool == null || _game == null) return false;

        // 後方最寄りの敵を探す（射撃方向の反対側）。居なければ真後ろへ撃つ。
        Vector2 back = -ShotDir;
        Node2D? nearest = null;
        float bestD = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is Enemy e && !e.IsPurified && (e.GlobalPosition.X - GlobalPosition.X) * back.X > 4f)
            {
                float d = e.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                if (d < bestD) { bestD = d; nearest = e; }
            }
        }

        Vector2 muzzle = GlobalPosition + back * 16f; // 後方の銃口（射撃方向の反対）
        Vector2 baseDir = nearest != null ? (nearest.GlobalPosition - muzzle).Normalized() : back;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(_game.BackfireDamage * _game.FollowerPowerMul));
        int shots = _game.BackfireShots;
        int turn = Mathf.RoundToInt(_game.BackfireTurnRate); // bf_track で 60→90（未適用だと既定95に化けていた）
        var tint = new Color(0.98f, 0.86f, 0.55f); // 淡い金（Bullet.DrawPlayerDiamond の BackMid と揃える。敵弾の穢れ桃と混同しない色）
        for (int i = 0; i < shots; i++)
        {
            // 2発目はわずかに角度を散らす（同時2発の見栄え）。
            float ang = baseDir.Angle() + (shots == 1 ? 0f : (i - (shots - 1) * 0.5f) * Mathf.DegToRad(24f));
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var bb = _pool.Spawn(muzzle, dir * 180f, isEnemy: false, 2.8f, dmg, BulletShape.Diamond, tint, homing: true, backwardHoming: true);
            bb.TurnRateOverride = turn;
        }
        FxLayer.Instance?.Muzzle(muzzle); // 後方マズルフラッシュ（シンプル版）
        return true;
    }

    // ───────── 回避（ドッジ）アクション ─────────
    // 入力方向があればその方向へ短い無敵ダッシュ、無ければその場回避（変位ゼロ＝スピン＆無敵のみ）。
    // クールダウン中・会話中・ゲームオーバー中は不可。
    // 敵（未浄化）が半径 r 以内に居るか。灯し手の「密着圏に居る間だけ回避CDが縮む」判定に使う。
    // ボス本体も "enemies" グループに居るので、ボス戦の踏み込みでもそのまま効く。
    private bool IsNearEnemy(float r)
    {
        float r2 = r * r;
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            if (node is Enemy e && !e.IsPurified && e.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= r2)
                return true;
        return false;
    }

    private void TryDodge(Vector2 dir)
    {
        // ★回避は「1面クリアの物語報酬」（2026-09-13 ユーザー決定）。未取得のあいだは何も起きない。
        //   ロック解除（右クリック）は TickLockOn が別経路で拾うので、未取得でもそちらは生きる。
        if (!(_game?.HasDodge ?? true)) return;
        if (_dodgeCd > 0f || _dodgeTimer > 0f || _gameOver) return;

        // 方向入力あり＝その方向へダッシュ。無し＝その場回避（変位ゼロ＝_dodgeDir を Zero に）。
        _dodgeInPlace = dir.LengthSquared() <= 0.01f;
        _dodgeDir = _dodgeInPlace ? Vector2.Zero : dir.Normalized();
        _dodgeFrom = GlobalPosition;
        _dodgeTimer = DodgeDuration;
        _dodgeInv = DodgeIFrame;
        // 身のこなし強化で CD 短縮・距離延長（i-frame は手触り固定）。GameManager 不在時は基準値。
        // ジョブの回避補正（設計書 §2）。
        //   ・DodgeCdMul     : 語り手 ×1.15（近づかれたら逃げる手段が薄い）
        //   ・CloseDodgeCdMul: 灯し手だけ、敵に Jobs.CloseRange(48px) 以内で ×0.75
        //                      ＝「危険地帯に居るほど抜ける手段が回る」。踏み込んだ瞬間の CD にだけ効く。
        //   ・DodgeDistMul   : 結び手 ×0.9（避けるのではなく耐える）
        var jd = _game?.JobDef;
        float cdMul = (jd?.DodgeCdMul ?? 1f)
                    * ((jd != null && jd.CloseDodgeCdMul < 1f && IsNearEnemy(Jobs.CloseRange)) ? jd.CloseDodgeCdMul : 1f);
        _dodgeCd = (_game?.DodgeCooldown ?? DodgeCooldown) * cdMul;
        _dodgeDist = (_game?.DodgeDistance ?? DodgeDistance) * (jd?.DodgeDistMul ?? 1f);
        DodgeCount++;         // 実行回数を加算（チュートリアルの回避検出用。報酬や挙動には無関係）
        _dodgeGrazeCount = 0; // 回避ごとに報酬カウンタをリセット（Cap=DodgeGrazeCap までが高報酬対象）
        _counterParity = 0;   // 返し光（counter_light）の間引き・上限も回避ごとにリセット
        _counterCount = 0;
        _dodgeTrailAccum = 0f;
        _dodgeFlip = false; // 開始は正面フレーム（00・FlipHなし）。ApplySpinFrame が毎フレーム更新する。
        // スピンの回り始めの向き：右/下回避=正、左/上回避=負。横成分があればそちらを優先。
        // 横成分が無い（真上/真下）回避は、上方向なら負・下方向なら正＝進行方向へ巻き込む自然な向き。
        // その場回避（方向入力なし）は既定で右回り（正）。
        if (_dodgeInPlace)
            _dodgeSpinSign = 1f;
        else if (Mathf.Abs(_dodgeDir.X) > 0.01f)
            _dodgeSpinSign = _dodgeDir.X >= 0f ? 1f : -1f;
        else
            _dodgeSpinSign = _dodgeDir.Y >= 0f ? 1f : -1f;

        if (_hasTexture && _sprite != null)
            ApplySpinFrame(0f);

        // 踏み込みの SE/演出（既存のグレイズ閃光を流用＝専用アセット不要で“抜けた”手応え）。
        FxLayer.Instance?.Graze(GlobalPosition);
        Audio.Instance?.PlayGraze();
        SpawnTrail();
    }

    // 位相 θ（0→2π*DodgeSpins）から全周8ステップのフレームを引いてスプライトに適用する。
    // 8分割インデックス k=floor(frac(θ/2π)*8)。マッピング（正回り _dodgeSpinSign>=0）:
    //   0→00,flip- / 1→01,flip- / 2→02,flip- / 3→03,flip- / 4→04,flip- / 5→03,flip+ / 6→02,flip+ / 7→01,flip+
    // 逆回り（_dodgeSpinSign<0）は k を反転（00→07→06…相当）して左右逆に見せる＝(8-k)%8 を引く。
    private static readonly int[]  SpinFrameIdx  = { 0, 1, 2, 3, 4, 3, 2, 1 };
    private static readonly bool[] SpinFrameFlip = { false, false, false, false, false, true, true, true };
    private void ApplySpinFrame(float theta)
    {
        float frac = theta / Mathf.Tau;
        frac -= Mathf.Floor(frac);                 // 0..1（周回を畳む）
        int k = (int)(frac * 8f);
        if (k > 7) k = 7;                          // frac→1.0 の境界保険
        if (_dodgeSpinSign < 0f) k = (8 - k) % 8;  // 逆回り＝たどり順を反転（00→07→06…）

        var tex = _spinTex[SpinFrameIdx[k]];
        _sprite.Texture = tex;
        _dodgeFlip = SpinFrameFlip[k];
        // スピンのフレーム流用反転と自機の向き(_facing)を XOR で合成＝左向きのままスピンしても
        // 着地フレーム(00)がちゃんと左向きに戻る（向きが回避で壊れない）。
        _sprite.FlipH = _dodgeFlip ^ (_facing < 0);
        // フレーム差し替えごとに正規化スケールを再計算（基準は絵の中身の高さ＝ScaleFor）。
        {
            float scale = ScaleFor(tex);
            _baseScaleX = scale;
            _sprite.Scale = new Vector2(scale, scale);
        }
    }

    // 回避終了：必ず正面フレーム(00)・FlipH=現在の向き・Rotation=0・idle テクスチャへ戻す。中途半端なフレームで固定しない。
    private void EndDodge()
    {
        _dodgeTimer = 0f;
        _dodgeFlip = false;
        _aimNow = "";

        // 祈りの帳（veil_light・支え側の奥義）：回避の終わり際、自機の周りに弾消しの光輪をまとう。
        if ((_game?.VeilLightRadius ?? 0f) > 0f)
        {
            _veilR = _game!.VeilLightRadius;
            _veilT = _game.VeilLightDuration;
        }
        if (_hasTexture && _sprite != null)
        {
            _sprite.FlipH = _facing < 0; // 回避前後で向きを保つ（false 決め打ちだと左向きが右向きに戻ってしまう）
            _sprite.Rotation = 0f; // 直立・正位置へ（次フレームから通常バンク／idle が滑らかに引き継ぐ）
            if (_idleTex != null)
            {
                _sprite.Texture = _idleTex;
                _baseScaleX = ScaleFor(_idleTex);
            }
            // スケールを素値へきっちり戻す（Scale.X=Scale.Y=baseScale）。
            _sprite.Scale = new Vector2(_baseScaleX, _baseScaleX);
        }
    }

    // 残像を1枚置く。Sprite2D を上限数だけ使い回す（毎フレーム Load も new もしない）。
    private void SpawnTrail()
    {
        if (!_hasTexture || _sprite == null) return;
        // プールが満杯なら一番古い（=リストの先頭）を使い回す。
        Sprite2D s;
        if (_trail.Count >= DodgeTrailMax)
        {
            s = _trail[0];
            _trail.RemoveAt(0);
        }
        else
        {
            s = new Sprite2D
            {
                Centered = true,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                ZIndex = ZIndex - 1, // 自機の背面に薄く残す
            };
            // 自機と同じ親（World）直下に置き、自機に追従させない（その場に残る残像）。
            GetParent().AddChild(s);
        }
        s.Texture = _sprite.Texture;   // その瞬間のスピンフレームを写す＝“回ってる残像”になる
        s.Scale = _sprite.Scale;
        s.FlipH = _sprite.FlipH;       // フレームごとの左右反転も写す（真横以降の FlipH 流用フレームを正しく残す）
        s.Rotation = _sprite.Rotation; // 回避中は 0（直立）＝側転痕は残らない
        s.GlobalPosition = _sprite.GlobalPosition;
        s.SelfModulate = new Color(0.7f, 0.9f, 1.2f); // 薄い青白の分身
        s.Modulate = new Color(1f, 1f, 1f, 0.45f);
        s.SetMeta("ttl", DodgeTrailTtl);
        s.SetMeta("age", 0f);
        s.Visible = true;
        _trail.Add(s);
    }

    // 残像をフェードアウト。寿命切れは Visible=false で隠してプールに残す（再利用）。
    private void UpdateTrail(float dt)
    {
        for (int i = _trail.Count - 1; i >= 0; i--)
        {
            var s = _trail[i];
            if (!IsInstanceValid(s)) { _trail.RemoveAt(i); continue; }
            if (!s.Visible) continue;
            float age = (float)s.GetMeta("age") + dt;
            float ttl = (float)s.GetMeta("ttl");
            if (age >= ttl) { s.Visible = false; continue; }
            s.SetMeta("age", age);
            float a = (1f - age / ttl) * 0.45f;
            s.Modulate = new Color(1f, 1f, 1f, a);
        }
    }

    private void OnAreaEntered(Area2D area)
    {
        // 敵 or 敵弾との接触で TakeHit。
        // 弾側の damage 処理は敵側 / 弾側で行うため、ここでは被弾のみ扱う。
        if (area is Enemy e && !e.IsPurified)
        {
            // QA(--god/--assist) 走行では敵本体との接触被弾もスキップ（god の GodClear は敵弾しか
            // 消せないため）。通常プレイでは QaPilot.GodActive は常に false（AreaStrike と同じ作法）。
            if (!QaPilot.GodActive) TakeHit();
            return;
        }

        // b.Active を必須にする。プールへ返却済み（=非アクティブ）の弾が
        // 当たり判定だけ残っていても被弾しないようにする。
        if (area is Bullet b && b.IsEnemy && b.Active)
        {
            TakeHit();
            // 当たった敵弾は消す
            if (_pool != null)
                _pool.Despawn(b);
        }
    }

    // グレイズ（敵弾のかすり）検出 → 加点。
    private void OnGrazeAreaEntered(Area2D area)
    {
        if (area is Bullet b && b.IsEnemy && b.Active && !b.Grazed)
        {
            b.Grazed = true; // どちらの分岐でも立てて二重取りを防ぐ
            // あかりの「キミ弾」：かすった弾だけ減速×0.75＋淡色化（フラグ弾のみ・判定不変）。
            // 報酬系（farming防止の分岐）とは独立したボス側ギミックなので、加点の前に無条件で適用する。
            b.ApplyGrazeSoften();
            var game = GetNodeOrNull<GameManager>("/root/Game");

            // 被弾直後の無敵中は経済加算をスキップ＝「無敵を盾に稼ぎ続ける」抜け穴を塞ぐ（farming防止）。
            // 視覚フィードバック（グレイズ閃光・残光）は出さず、報酬も入らない＝無敵中の弾は“ただ抜ける”。
            // 回避(Dodge)由来の無敵は _hitInvincible を立てないため対象外＝従来どおり回避よけで稼げる。
            if (_hitInvincible)
                return;

            // 回避の無敵中（_dodgeInv>0）に貫通した敵弾は「回避よけ」＝高報酬＋お金。
            // ただし1回避あたり DodgeGrazeCap 発まで。超過分は貫通するが追加報酬なし（farming防止）。
            // i-frame が切れた回避モーション後半（_dodgeInv==0 だが _dodgeTimer>0）は通常グレイズ扱い。
            if (_dodgeInv > 0f && _dodgeGrazeCount < DodgeGrazeCap && game != null)
            {
                _dodgeGrazeCount++;
                game.AddDodgeGraze();
                // 2026-09-22 ユーザー指示：回避よけの「+N」ポップアップは非表示。自機のすぐ上に数字が出ると
                //   避けている最中の弾と重なって見落とす。報酬（スコア・インプレ・コンボ猶予）は従来どおり入る。

                // 返し光（counter_light・ホーミング幹の奥義）：回避よけした弾そのものを追尾光弾へ変換して撃ち返す。
                // Lv1=2発に1発・上限6/回避、Lv2=全弾・上限12(=DodgeGrazeCap)。報酬(AddDodgeGraze)はそのまま＝攻めの上乗せ。
                int clv = game.CounterLightLevel;
                if (clv > 0)
                {
                    _counterParity++;
                    int cap = clv >= 2 ? DodgeGrazeCap : DodgeGrazeCap / 2;
                    if (_counterCount < cap && (clv >= 2 || (_counterParity & 1) == 1))
                    {
                        _counterCount++;
                        Vector2 at = b.GlobalPosition;
                        // 威力はホーミング射と同等（基礎×フォロワーバフ×追尾税0.7）。弾はその場で光弾に置き換える。
                        int cdmg = Mathf.Max(1, Mathf.RoundToInt(1f * game.ShotPowerMul * game.FollowerPowerMul * 0.7f));
                        _pool?.Despawn(b);
                        _pool?.Spawn(at, ShotDir * 200f, isEnemy: false, 3f, cdmg, BulletShape.Orb, null, homing: true);
                        FxLayer.Instance?.Muzzle(at); // 変換の一閃（“返した”を短く見せる）
                    }
                }
            }
            else
            {
                game?.AddGraze();
            }

            FxLayer.Instance?.Graze(GlobalPosition); // グレイズ閃光（共通の手応え）
            Audio.Instance?.PlayGraze();
            _grazeFlash = 1f; // 自機の絵を一瞬光らせる（“今かすった”を強調。旧グレイズリングの代替）
        }
    }

    // ボム「魔法陣・解放」: 画面の敵弾を消去＋画面内の敵を浄化＋短時間無敵＋画面フラッシュ。
    private void TryBomb()
    {
        if (_gameOver) return;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game == null || !game.UseBomb())
            return;

        BombCount++; // 発動成功＝累計を加算（練習モードの発動検出用。残数では見れないため）

        // ボム演出（魔法陣＋光の波）＋画面効果＋体の解放モーション（伸び上がり＝同フレーム）
        FxLayer.Instance?.Bomb(GlobalPosition);
        Audio.Instance?.PlayBomb(); // ③溜め→開放の二段。破壊でなく「鎮める／光が満ちる」
        GameCamera.Instance?.Shake(4.5f, 0.15f);
        GameCamera.Instance?.Hitstop(0.05);
        _bombCast = BombCastDur;

        // 画面内の敵弾を「花びらに変換」して消去（加点）
        foreach (Node node in GetTree().GetNodesInGroup("enemy_bullets"))
        {
            if (node is Bullet b && b.Active)
            {
                game.AddBulletCleared();
                FxLayer.Instance?.BulletToPetal(b.GlobalPosition);
                _pool?.Despawn(b);
            }
        }

        // 画面内の敵を浄化
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is Enemy e)
                e.Purify();
        }

        // 短時間無敵 ＋ 画面フラッシュ
        StartInvincible();
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.Flash();
    }

    // ★ヒカゲ専用スキル（TryHikageSpecial）は 2026-09-13 に撤去した。W0 専用・非正典の機能が
    //   正典のCキーを占有し続けていたため（Cキーは溜め打ちへ）。AddHikageFollower / HasHikage /
    //   Follower.IsHikage は W0 の見た目のためだけに残してある＝戦闘の配線はもう無い。

    // 溜め打ちの発射：威力×4の大玉を1発だけ、射撃方向へ。貫通なし（＝連射の貫通とは別物）。
    //   弾は Bullet.MakeAccel を流用するが「タメ0秒」で渡す＝スポーンした瞬間に ChargeSpeed で発進する
    //   （加速球のタメ演出は要らない。溜めは自機側で既に終わっている）。
    private void FireCharge()
    {
        if (_pool == null) return;
        // 基礎威力は通常ショットと同じ経路（フォロワーバフ×ジョブ補正×火力2倍）で作り、最後に ×4。
        int baseDmg = Mathf.Max(1, Mathf.RoundToInt(1f
                                                    * (_game?.FollowerPowerMul ?? 1f)
                                                    * (_game?.JobDef.PowerMul ?? 1f)
                                                    * (_game?.ShotPowerMul ?? 1)));
        int dmg = Mathf.Max(1, Mathf.RoundToInt(baseDmg * ChargeDamageMul));
        Vector2 muzzle = GlobalPosition + ShotDir * 20f;
        var b = _pool.Spawn(muzzle, ShotDir * ChargeSpeed, isEnemy: false, 7f, dmg);
        b.MakeAccel(ChargeSpeed, ChargeSpeed, 0f); // タメ0＝即発進（大玉の見た目だけ流用）
        b.Pierce = 0;                              // 貫通なし（仕様）
        GD.Print($"[charge] fire dmg={dmg} (base={baseDmg} x{ChargeDamageMul})");
        FxLayer.Instance?.PurifyBurst(muzzle);
        Audio.Instance?.PlayShot();
        _recoil = 1.6f; // 通常ショット(1.0)より深いキックバック＝重い一発を手に返す
    }

    private bool _gameOver = false;

    public void TakeHit()
    {
        // 無敵中・回避無敵中・ゲームオーバー中は無効（回避の主旨＝回避中は被弾しない）
        if (_invincible || _dodgeInv > 0f || _gameOver)
            return;
        if (AbsorbPowerupHit()) return;

        // 被弾演出（自機周囲のフラッシュ＋波紋）＋ 赤フラッシュ・シェイク・ヒットストップで「被弾」を明確化。
        FxLayer.Instance?.PlayerHit(GlobalPosition);
        Audio.Instance?.PlayHit(); // 最優先（Alertバス）。実時間再生でヒットストップに引きずられない
        GameCamera.Instance?.Shake(5.5f, 0.28f);
        GameCamera.Instance?.Hitstop(0.09);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.HitFlash();
        _hitReact = HitReactDur; // 体ののけぞり＋squash（練習モード含む＝「痛がった」は常に返す）
        // QA走行だけ、ジョブの被弾まわりの補正が効いているかをログへ（のけぞり変位0／無敵秒）。
        if (QaPilot.Verbose && _game != null)
            GD.Print($"[JOB] {_game.JobDef.CharacterName} hit: knockback={( _game.JobDef.NoHitKnockback ? "0px(踏みとどまり)" : "-5px")} "
                   + $"invul={_game.JobDef.HitInvulSec:0.0}s lives={Lives}->{Mathf.Max(0, Lives - 1)}/{_game.StartLives}");

        // 集中の光（focus_fire）は被弾で霧散＝積み上げた連続ヒットをリセット（練習モードでも同様）。
        _focusTarget = null;
        _focusHits = 0;

        // チュートリアル練習モード：被弾演出は出すが、残機を減らさず・ゲームオーバーにせず・フォロワーも離さない（詰み防止）。
        // 短時間無敵だけ付けて先へ進める（同じ弾で連続被弾しない）。
        if (_game?.TutorialNoConsume ?? false)
        {
            StartInvincible(fromHit: true);
            return;
        }

        // 被弾回数を1つ数える（ハブ帰還の「被弾は{n}回でした」＝表示専用の補助観測）。
        // 練習モードの早期 return より後なので、チュートリアルの被弾は数えない。
        _game?.NotifyPlayerHit();
        LosePowerupsOnHit();

        // ♥（残機）を1つ減らして HUD 更新
        Lives = Mathf.Max(0, Lives - 1);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetLives(Lives);

        // 被弾でフォロワーが1体だけ離れてしまう（やさしさの輪が少しほどける＝全滅させない）
        // ヒカゲ（専用スキル持ち）は通常フォロワーが残っている限り離脱対象から除外する
        // （加入直後にリスト末尾へ入り、次の被弾で即離脱＝スキルを丸ごと失うのを防ぐ）。
        if (_followers.Count > 0)
        {
            int idx = -1;
            for (int i = _followers.Count - 1; i >= 0; i--)
            {
                if (!_followers[i].IsHikage) { idx = i; break; }
            }
            if (idx < 0) idx = _followers.Count - 1; // 通常フォロワーが0＝ヒカゲのみなら、ヒカゲも例外なく離脱させる

            var f = _followers[idx];
            FxLayer.Instance?.KindnessMote(f.GlobalPosition);
            f.QueueFree();
            _followers.RemoveAt(idx);
        }

        // フラッシュ＋短時間無敵（被弾由来＝この無敵中はグレイズ報酬が入らない）
        StartInvincible(fromHit: true);

        if (Lives <= 0)
            GameOver();
    }

    // ♥（残機）を回復する。上限は難易度＋最大♥強化由来の初期値（StartLives）。
    // 既に上限なら増やさない。戻り値＝実際に増えたか（フィードバック演出の判定用）。
    // 中ボス撃破の回復報酬（GameManager.RewardCameoDefeat）から呼ぶ。
    public bool AddLife(int n = 1)
    {
        if (_gameOver || n <= 0) return false;
        int cap = MaxLives;
        if (Lives >= cap) return false;
        Lives = Mathf.Min(cap, Lives + n);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetLives(Lives);
        return true;
    }

    private void GameOver()
    {
        _gameOver = true;
        _invincible = true;
        _invincibleTimer = 9999f; // 以降は無敵で待機
        // 2026-09-07: ゲームオーバーのバナー（ShowBanner）は出さない。
        //   バナーは y=300 に大きく出るので、この直後に立つ選択UI
        //   （GameManager.HandleGameOverExit の ChoiceOverlay・3択は y=195/285/375）の2行目と
        //   真上から重なって読めなくなる。「くじけちゃった…」の一言は選択UIの見出しとして
        //   Hud.ShowGameOverTitle が選択肢の上（y=150）に出す＝重ならず、同じ案内も二重にならない。
        // 自動リロードはしない。各ステージルート(*Root.cs)の _Process が選択UIとキー（R/Shift+R/Q）を
        // 受け付けるので、プレイヤーが選ぶまでこのまま無敵で待機する。
    }

    // fromHit=true（被弾由来）の無敵だけ経済加算を止める。ボム無敵は fromHit=false＝稼ぎは止めない。
    private void StartInvincible(bool fromHit = false)
    {
        _invincible = true;
        // 被弾由来の無敵だけジョブ補正を効かせる（結び手 1.2→1.8秒＝連鎖被弾を潰す）。
        // ボム無敵（fromHit=false）はボム側の設計値なのでジョブでは動かさない。
        _invincibleTimer = fromHit ? (_game?.JobDef.HitInvulSec ?? InvincibleDuration) : InvincibleDuration;
        _blinkPhase = 0f;
        _hitInvincible = fromHit;
        // 被弾フラッシュ（一瞬非表示にして点滅開始の合図）
        SetSpriteVisible(false);
    }

    private void SetSpriteVisible(bool visible)
    {
        if (_hasTexture && _sprite != null)
            _sprite.Visible = visible;
        // _Draw フォールバック側は modulate ではなく可視フラグで制御
        Modulate = new Color(1f, 1f, 1f, visible ? 1f : 0.35f);
    }

    public override void _Draw()
    {
        // テクスチャが無い場合のプレースホルダ（白い体＋紫十字）
        if (!_hasTexture)
        {
            // 体（白い円）
            DrawCircle(Vector2.Zero, 12f, new Color(1f, 1f, 1f, 0.95f));
            DrawArc(Vector2.Zero, 12f, 0f, Mathf.Tau, 24, new Color(0.6f, 0.6f, 0.7f), 1f);

            // 紫の十字（胸）
            var purple = new Color(0.6f, 0.2f, 0.8f);
            DrawLine(new Vector2(-4f, 0f), new Vector2(4f, 0f), purple, 1.5f);
            DrawLine(new Vector2(0f, -4f), new Vector2(0f, 4f), purple, 1.5f);
        }

        // ── 拡散サブ（追従オプション）の光球。ローカル定位置＋ふわふわ（当たり判定なし・見た目のみ）──
        for (int i = 0; i < _optionCount && i < OptionSlots.Length; i++)
        {
            Vector2 p = OptionSlots[i] + new Vector2(0f, Mathf.Sin(_bobTime * 3.4f + i * Mathf.Pi) * 1.5f);
            DrawCircle(p, 4.2f, new Color(0.42f, 0.74f, 0.85f, 0.35f));            // 外周グロー
            DrawCircle(p, 2.6f, new Color(0.65f, 0.9f, 1f, 0.95f));                // 本体（浄化の水色）
            DrawCircle(p + new Vector2(-0.8f, -0.8f), 1f, new Color(1f, 1f, 1f, 0.95f)); // ハイライト
        }

        // ── 祈りの帳（veil_light）の光輪：残り時間に応じてフェードする暖白のリング ──
        if (_veilT > 0f && _game != null && _game.VeilLightDuration > 0f)
        {
            float va = Mathf.Clamp(_veilT / _game.VeilLightDuration, 0f, 1f);
            DrawArc(Vector2.Zero, _veilR, 0f, Mathf.Tau, 44, new Color(1f, 0.95f, 0.78f, 0.55f * va), 1.6f);
            DrawArc(Vector2.Zero, _veilR - 3f, 0f, Mathf.Tau, 44, new Color(1f, 0.9f, 0.6f, 0.25f * va), 1f);
        }

        // ── 溜め打ち（#6）の充填表示：自機の頭上に小さな弧。0→1 で伸び、満ちたら白く脈打つ ──
        //   常設のリングは 2026-09-08 に「何のためにあるか分からない」と消したばかりなので、
        //   ここは**押しているあいだだけ**出す＝溜めていることと満ちたことだけを、その瞬間に返す。
        if (_chargeT > 0f)
        {
            float cr = ChargeRatio;
            var at = new Vector2(0f, -24f);
            // 受け皿（薄い弧・全周）＋ 充填ぶん（上から時計回りに伸びる）
            DrawArc(at, 6.5f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau, 24, new Color(1f, 1f, 1f, 0.18f), 1.4f);
            Color cc = ChargeFull
                ? new Color(1f, 1f, 1f, 0.75f + 0.25f * Mathf.Sin(_bobTime * 18f)) // 満：白く脈打つ＝「離せ」
                : new Color(BulletArt.PlayerColor(_game!.SelectedJob), 0.9f);
            DrawArc(at, 6.5f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * cr, 24, cc, 2.2f);
            if (ChargeFull) DrawCircle(at, 2.2f, cc);
        }

        // ※グレイズ境界のシアンのリングは削除（2026-09-08）。
        //   常時ミナの周りに出ている輪で、ユーザーに「何のためにあるか分からない」と指摘された。
        //   グレイズ判定（GrazeRadius の GrazeArea＝スコア加算・SE・FxLayer 閃光）はそのまま生きている。
        //   “かすった”手応えは FxLayer.Graze の閃光と SE が担う＝情報は失われない。

        // ※被弾点は _Draw では描かない。ここ（Player 自身の描画）だと**子の _sprite に必ず覆われる**
        //   （子は親の描画より後＝上に出る）。「常に最も目立つ」を守るため HitDot 子ノードへ移した。
    }
}

// Keep the emblem above the animated sprite and anchored to the collision body.
public partial class PlayerHitDot : Node2D
{
    public float Radius = 2f;
    public string CharacterId = "mina";
    public Texture2D Texture { get; private set; } = null!;
    private Vector2 _jewelCenter;

    public override void _Ready()
    {
        Texture = GD.Load<Texture2D>($"res://char/player/{CharacterId}/{CharacterId}_core_v1.png");
        TextureFilter = TextureFilterEnum.Linear;
        // The flame and ribbon are asymmetric; center the jewel, not their image bounds.
        _jewelCenter = new Vector2(0.5f, CharacterId switch
        {
            "mina" => 0.46f,
            "akari" => 0.66f,
            "rei" => 0.56f,
            _ => 0.5f,
        });
    }

    public override void _Draw()
    {
        Vector2 size = Texture.GetSize();
        Vector2 center = size * _jewelCenter;
        float extent = Mathf.Max(Mathf.Max(center.X, size.X - center.X), Mathf.Max(center.Y, size.Y - center.Y));
        float scale = (Radius + 1.6f) / extent;
        DrawTextureRect(Texture, new Rect2(-center * scale, size * scale), false);
        DrawCircle(Vector2.Zero, 0.65f, new Color(0.08f, 0.08f, 0.16f, 0.8f));
        DrawCircle(Vector2.Zero, 0.38f, Colors.White);
    }
}
