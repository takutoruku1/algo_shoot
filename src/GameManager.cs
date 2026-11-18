using Godot;
using System.Collections.Generic;
using System.Linq;

// GameManager : Autoload シングルトン (/root/Game)。
// スコア・コンボ・ボム数などのゲーム状態を一元管理する。
// メタ進行（インプレッション経済 / フォロワー / 恒久強化）と user:// セーブもここに集約。
//   - 経済設計: docs/20260613/MINA_システム拡張設計書_v1.md ①章
//   - 恒久強化は被弾・リトライ・汚染で溶けない（§0-3）。セーブにのみ依存して永続。
public partial class GameManager : Node
{
    // Autoload の実体への静的参照。敵側の EnemyTimeScale が毎フレーム GetNode するのを避けるためだけの控え。
    //   _EnterTree で立て、_ExitTree で降ろす（シーン切替で古い実体を掴み続けないように）。
    public static GameManager? Instance { get; private set; }
    public override void _EnterTree() => Instance = this;
    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        // Release the native cursor texture before the rendering server shuts down.
        Input.SetCustomMouseCursor(null);
        GetWindow().SizeChanged -= ApplyCursor;
        _cursorTexture?.Dispose();
        _cursorTexture = null;
    }

    public long Score { get; private set; }
    private long _bossRetryScore;
    private double _bossRetryElapsed;
    public int Combo { get; private set; }
    public int Bombs { get; private set; } = 3;

    // 難易度（オートロードなのでシーンをまたいで保持）。
    // ルナティックは最高難度＝玉数×2.2。メタ強化が乗らないと現実的にクリア不能（②-4）。
    public enum Diff { Easy, Normal, Hard, Lunatic }
    public Diff Difficulty = Diff.Normal;

    // ───── ショットモード（設計書 §3）。連射は初期解放、拡散/ホーミング/加速球はショップ購入で解放 ─────
    //   ★enum は末尾に追加＝Rapid/Spread/Homing の保存値(0/1/2)は不変＝既存セーブと互換。Accel(=3)は
    //     加速球解放ノード(accel_1)購入 or トレーニング中のみ選べる（拡散/ホーミングと同じ「入り口ノード購入で解放」流儀）。
    public enum ShotMode { Rapid, Spread, Homing, Accel }
    // ★2026-09-13 ジョブ導入：モードはプレイヤーが選ばない「ジョブが決める従属値」になった（設計書 §3）。
    //   書き込むのは SelectedJob のセッタ（と旧セーブのモード→ジョブ逆引き）だけ。セーブ形式は無変更。
    public ShotMode SelectedShotMode = ShotMode.Rapid;

    // ───── ジョブ（設計書 §2・ラン単位で固定／ハブで選び直す）─────
    //   初期選択は結び手(Tank)。セットすると対応モードへ自動同期する＝「型が撃ち方を決める」を
    //   1箇所で担保し、ジョブとモードが食い違う状態を作らない。
    private Job _job = Job.Tank;
    public Job SelectedJob
    {
        get => _job;
        set
        {
            if (_job != value) CancelSpecial();
            _job = value;
            SelectedShotMode = Jobs.Get(value).Mode;
        }
    }
    public JobTuning JobDef => Jobs.Get(_job);
    // コマンドライン --job=xxx（Main が解決）で固定されたか。true の間はセーブのロードでも上書きしない
    //   ＝QA走行で「セーブに入っていた別ジョブ」へ戻される事故を防ぐ。デバッグ専用の逃し口。
    public bool JobForcedByCmdline;

    // ───── ジョブの解禁（2026-09-14・ステージクリア報酬）─────
    //   最初は結び手（ミナ）だけ。あかり／こはる／レイのジョブは、その子の面をクリアすると開く
    //   （Job.UnlockStageId）。判定は _cleared だけを見る＝専用の永続項目を足さない＝
    //   既存セーブ（クリア済みステージが入っている）はロードした瞬間に正しい解禁状態になる。
    //   --job= 固定中（JobForcedByCmdline）は解禁を無視する＝デバッグ起動は従来どおり。
    public bool IsJobUnlocked(Job j)
    {
        if (JobForcedByCmdline) return true;
        string need = Jobs.Get(j).UnlockStageId;
        return need.Length == 0 || IsStageCleared(need);
    }
    public bool IsMinaLockedForFinal => !IsFinalCleared && !JobForcedByCmdline;
    private readonly List<string> _pendingUnlockCards = new();
    public JobTuning? NextUnlockCard => _pendingUnlockCards.Count == 0 ? null : Jobs.UnlockedBy(_pendingUnlockCards[0]);
    public void AcknowledgeUnlockCard(JobTuning job)
    {
        _pendingUnlockCards.Remove(job.UnlockStageId);
        AutoSave();
    }
    public const string MinaFinalLockHint = "この投稿に潜る仲間を選ぼう";

    // 未解禁ジョブの1行説明（選択画面が出す）。解禁済み／条件なしは null。
    //   文言は既存のステージ見出し（StageDef.Title の "STAGE 1 — あかり" の左側）から作る
    //   ＝面の並びを変えても表示が追随し、ここに面の名前を二重に持たない。
    public string? JobUnlockHint(Job j)
    {
        if (IsJobUnlocked(j)) return null;
        string need = Jobs.Get(j).UnlockStageId;
        foreach (var s in Stages)
            if (s.Id == need)
            {
                string label = s.Title.Contains('—') ? s.Title.Split('—')[0].Trim() : s.Title;
                return $"{label} をクリアすると";
            }
        return "まだ、出会っていない";
    }
    // 保存済みの進行値を保ち、以後は帰還まで完了した章だけを数える。
    private readonly Dictionary<string, int> _charDives = new();
    public int CharacterChapter(Job j) => Mathf.Clamp(
        (_charDives.TryGetValue(Jobs.Get(j).CharacterId, out var completed) ? completed : 0) + 1,
        1, CharacterStory.LoopChapter);

    // トレーニング場（TrainingRoot）が立てる：この間だけ全ノードを試せる（ジョブ導入後もモード自体はジョブ固定）。
    public bool TrainingMode;
    // ★モード「解放」ノードは 2026-09-13 のジョブ導入で解放の意味を失った（モードはジョブが決める＝設計書 §3）。
    //   そのぶん spread_1 / homing_1 / accel_1 は「その系統の第一段」の効果へ付け替える。
    //   具体的には各鎖を1段ずらし、未所持(Lv0)＝素の基準値、入り口ノード購入＝旧IIの効果、という形にした。
    //     拡散の本数  : 5(素) → 7 → 9 → 11
    //     ホーミング数: 2(素) → 3 → 4 → 5
    //     加速球      : accel_1 が「威力 +1」（旧 accel_power_1 の効果）を担い、accel_power_* は +2/+3 へ繰り上げ
    //   ＝「買ったのに何も変わらない」ノードを残さない。既存セーブは所持IDがそのままなので効果だけが強くなる
    //     （没収ゼロ＝MigrateUpgradesIfLegacy の方針と同じ）。
    public int SpreadWays => 5;
    public int HomingShots => 2;
    public string ShotModeName(ShotMode m) => m switch { ShotMode.Spread => "拡散", ShotMode.Homing => "ホーミング", ShotMode.Accel => "加速球", _ => "連射" };
    // 残機・ボムは難易度ベース ＋ 恒久強化ボーナス。
    // Lunaticは弾密度(BulletCountMul=1.9、Hard比+73%)・弾速(1.18)・間隔(0.85)全てが全難易度中最厳。
    // Easy(6)→Normal(4)→Hard(3)の減り方（-2,-1）に沿って Hard→Lunatic も -1 段階減らし、
    // 恒久強化（ハート +1 が2段＝最大 +2）を乗せて初めて現実的に戦える設計は維持（②-4想定通り）。
    // ジョブの最大♥増減（結び手 +2 ／ 灯し手 −1）もここへ乗せる＝回復キャップ(Player.AddLife)も同時に追従する。
    // 下限1：灯し手×Lunatic(基礎2)でも 1 は残す＝「開始即ゲームオーバー」を作らない。
    public int StartLives => Mathf.Max(1, BaseLivesFor(Difficulty) + MaxLifeBonus + JobDef.MaxLifeDelta);
    public int StartBombs => BaseBombsFor(Difficulty) + BombCountBonus;
    // ♥とボムの基礎値は難易度・ステージに依らず一定（2026-09-15 ユーザー決定:
    // 「♥はどのステージを選択しても3個スタート、ボムは1個。強化したら♥が増えてくシステムなので」）。
    // 難易度の賭け金は弾速・弾数・出現密度の側だけが担う。伸びる経路は
    // ショップの「ハート +1」×2段・「ボム +1」と、ジョブ補正（結び手+2/灯し手-1）。
    // 2026-09-27 ユーザー決定: ボムの基礎値は 1 → 2（「ボムが最初一個になっているが二個にして」）。
    //   ショップの「ボム +1」は据え置き＝強化後は 3 個。祈り手の BREAK 供給（AddBombOnBreak）と
    //   ♥取得時の補充（1573行）も上限 StartBombs を見ているので、この1行だけで全経路に届く。
    public static int BaseLivesFor(Diff d) => 3;
    public static int BaseBombsFor(Diff d) => 2;
    public float BulletSpeedMul => Difficulty switch { Diff.Hard => 0.82f, Diff.Lunatic => 0.94f, _ => 0.65f };
    // 難易度は敵の体力ではなく「弾の数」で調整する（やさしいほど弾が少ない）。
    public float BulletCountMul => Difficulty switch { Diff.Hard => 1.65f, Diff.Lunatic => 2.85f, _ => 1.05f };
    public float DanmakuIntervalMul => Difficulty switch { Diff.Hard => 1.0f, Diff.Lunatic => 0.85f, _ => 1.35f };
    public int MaxAliveEnemies => 5;
    public string DiffName => Difficulty switch { Diff.Easy => "EASY", Diff.Hard => "HARD", Diff.Lunatic => "LUNATIC", _ => "NORMAL" };
    public bool DebugAttackGuides { get; private set; }
    public static bool ShowAttackTelegraphs => true;
    // ルナティック（難易度選択の「底まで」）＝物語ギミックを全部切った、止まらない純粋シューティング（2026-09-26 作者指示）。
    //   回想フィルム・撃破後のアフター・下書き選択・戦闘を止める会話（HoldBubble）・ボス投稿の割り込み・
    //   フィナーレの段間カットシーン・エンディング（Final/Epilogue）を出さず、道中→中ボス→道中→ボス→クリアを切れ目なく繋ぐ。
    //   判定はこの1本に集約する（各 Stage*/Boss* が参照）。Easy/Normal/Hard の経路はこの判定が false のまま＝従来どおり。
    public bool IsLunatic => Difficulty == Diff.Lunatic;
    public static bool LunaticActive => Instance?.IsLunatic == true;
    public static bool BossRushActive => Instance?.GetTree().GetFirstNodeInGroup("boss_rush") != null;
    public static bool BossPracticeActive => Instance?.GetTree().GetFirstNodeInGroup("boss_practice") != null;
    public static bool SkipBossStory => LunaticActive || BossRushActive || BossPracticeActive;

    // ── 撃破時に散る「心の欠片」の難易度倍率（2026-09-17 ユーザー要望）──
    //   粒の個数と、欠片が運ぶスコア総量の両方に掛ける（FxLayer.PurifyBurst）。
    //   ★ショップ通貨(Impression)の DifficultyImpressionMulFor(0.7/1.0/1.6/3.0) とは別物。
    //   ★カーブを 0.7/1.0/1.6/3.0 より緩くした理由：この倍率は「画面に出る粒の実数」に直結する。
    //     3.0 を掛けると Lunatic のボスで 128×3.0＝384粒となり、容量640の6割を1撃で食い潰し、
    //     改心の一拍で画面が花びらに埋まって視認性が落ちる（派手さは視認性を侵さない範囲で＝§4）。
    //     1.6 なら最大205粒＝qa 実測(121粒/144fps)の1.7倍で容量にもフレームにも余裕が残る。
    //   ★Easy を 0.8 止まり（0.7 でなく）にしたのは、入口の難易度で「明らかに寂しい撃破」を
    //     作らないため（§3 とっつき）。賭け金の差は弾速・弾数・出現密度が既に担っている。
    public static float DifficultyShardMulFor(Diff d) => d switch { Diff.Easy => 0.8f, Diff.Hard => 1.3f, Diff.Lunatic => 1.6f, _ => 1f };
    public float DifficultyShardMul => DifficultyShardMulFor(Difficulty);

    public int DiffBarBonus(bool finalBoss) => 4 + (finalBoss ? 2 : 0);
    public float BossHpMul => Difficulty switch
    {
        Diff.Normal => 1.25f, Diff.Hard => 1.5f, Diff.Lunatic => 1.75f, _ => 1f,
    };
    public float BossPanelHpMul => Difficulty switch
    {
        Diff.Hard => 1.25f, Diff.Lunatic => 1.5f, _ => 1f,
    };
    public float RoadHpMul => Difficulty switch
    {
        Diff.Normal => 1.25f, Diff.Hard => 2.16f, Diff.Lunatic => 1.75f, _ => 1f,
    };

    public const string LunaticUnlockHint = "未解放";
    public const string LunaticUnlockSeenKey = "once_lunatic_unlock";
    public bool IsLunaticUnlocked => IsFinalCleared;
    public bool LunaticUnlockPending => IsLunaticUnlocked && !IsIdleDialogSeen(LunaticUnlockSeenKey);

    // ───── 回避 ─────
    //   回避（Space / L3 / 右クリック）は 2026-09-22 からショップの段 #2「回避」（n_dodge）で覚える
    //   （それまでは 1面クリアの物語報酬＝GrantDodge／セーブキー "hasDodge"。旧セーブは LoadFromSlot が
    //   n_dodge の所持へ読み替える）。所持判定 HasDodge は強化効果アクセサの並び（ChargeNeedSec の隣）。

    private float _specialCooldown;
    private PlayerSpecial? _special;
    internal PlayerSpecial? CurrentSpecial => IsInstanceValid(_special) && _special!.Active ? _special : null;
    public bool SpecialActive => CurrentSpecial != null;
    public bool SpecialReady => HasSpecial && _specialCooldown <= 0f && !SpecialActive;
    public float SpecialCooldownRatio => Mathf.Clamp(_specialCooldown / PlayerSpecial.Cooldown, 0f, 1f);
    public float SpecialRatio => CurrentSpecial?.Ratio ?? 0f;

    // 敵側が delta に掛ける係数。★ヒットストップ（GameCamera が Engine.TimeScale を 0.06 に落とす）と
    //   二重に掛けない：TimeScale が落ちている間は既に全体が止まっているので、ここでは 1 を返す。
    public static float EnemyTimeScale
    {
        get
        {
            if (Engine.TimeScale < 0.99 && BossPracticeRoot.Active == null) return 1f;
            return BossPracticeRoot.Active is { Practicing: true } practice ? practice.AttackTempo : 1f;
        }
    }
    // 敵側ノードが使う唯一の入口。delta にこれを掛けてから時間を進める。
    public static double EnemyDelta(double delta) => delta * EnemyTimeScale;

    internal bool TrySpecial(Player player, Vector2 direction)
    {
        if (!SpecialReady || Hud.BubblePaused || player.IsGameOver || GetTree().Paused) return false;
        if (!PlayerSpecial.CanStart(player, SelectedJob)) return false;
        _special = PlayerSpecial.Spawn(player, SelectedJob, direction);
        _specialCooldown = PlayerSpecial.Cooldown;
        return true;
    }

    internal void TickSpecial(float dt)
    {
        if (!Hud.BubblePaused && !SpecialActive)
            _specialCooldown = Mathf.Max(0f, _specialCooldown - dt);
    }

    internal void CancelSpecial()
    {
        if (IsInstanceValid(_special)) _special!.Cancel();
        _special = null;
    }

    // ダイブ先の受け渡し（ハブ→難易度選択→ステージ）。
    public string PendingStageScene = "res://Rei.tscn";
    public int PendingBossIndex;

    // ───── チェックポイント入口（最初から / 中ボスから / ボスから）─────
    //   中ボス(cameo)を持つ3ステージ（レイ/あかり/こはる）で道中をスキップして任意の戦闘から始められる。
    //   SelectedEntry は「ラン単位」＝非セーブ。DiffSelect がダイブ直前にセットし、Stage が _Ready で読む。
    //   解放ゲート：MidBoss は中ボス撃破で解放（IsMidBossCleared）、Boss はステージクリアで解放（IsStageCleared）。
    //   AfterMidBoss は DiffSelect には出さない“続きから再開”専用（初回ショップ導線がプログラム的にセット）。
    //   FINAL（MinaBattle）は中ボスを持たないので Start と Boss の2つだけ。Boss はゲームオーバーの
    //   「ボスから」だけが立てる＝道中（残響の三波・約70秒）を飛ばしてミナ戦から再開する（2026-09-27）。
    public enum StageEntry { Start, MidBoss, Boss, AfterMidBoss }
    public StageEntry SelectedEntry = StageEntry.Start;

    // 初回ショップ導線の復帰先：非nullなら、ショップ退出時にハブでなくこのステージへ戻り、
    // 中ボスの“続き”（道中後半＝Step_MidwaveB）から再開する（ラン単位・非セーブ。退出時に消費してnullへ）。
    public string? PendingResumeScene;

    // 中ボス(cameo)撃破フラグ（ステージID集合・永続＝save_N.json）。「中ボスから」解放の判定に使う。
    private readonly HashSet<string> _midBossCleared = new();
    // 中ボス撃破を記録。戻り値 firstEver＝「全ゲーム通して初めて中ボスを倒した」か（初回ショップ導線の判定用）。
    public bool MarkMidBossCleared(string id)
    {
        bool firstEver = _midBossCleared.Count == 0;
        if (_midBossCleared.Add(id)) AutoSave();
        return firstEver;
    }

    // 初回ショップ説明を見たか（全ゲーム通して一度きり・永続＝save_N.json）。
    // 最初の面のボスを初めて倒してハブへ帰った回に、ホーム解禁演出の直後へ一度だけ挟む（Hub が立てる）。
    // 以降のクリアでは説明が流れない。
    //   2026-10-03: 説明は専用シーンをやめてハブの会話として流れる＝シーンをまたがないので、
    //   「説明から帰ってきた直後」を伝えるランタイムのフラグ（旧 ShopNudgePending）は要らなくなった。
    public bool ShopTutorialSeen;

    // アカウント追加の説明（あかり初回の帰還会話・once_account_intro）を読み切った直後に立つ（ランタイム限り・保存しない）。
    //   ハブが SNS（タイムライン）のフッタ「アカウント」を脈動させ、押す／切替UIを開く（Hub.OpenJob）と消費する。
    //   説明は once で二度と流れないので、消費後に再発しない。2026-09-23。
    public bool AccountNudgePending;

    // 弾幕の本数を難易度でスケール（最低1発は残す）。各ボスのリング/扇の本数に掛ける。
    public int ScaleBullets(int baseCount) => Mathf.Max(1, Mathf.RoundToInt(baseCount * BulletCountMul));

    // 自機を狙う扇だけが使う本数。必ず奇数を返す＝扇の中心に1本が立ち、棒立ちの自機を射抜く。
    //   偶数だと中心線に弾が無く（左右の弾が自機の両脇を通り抜ける）、「動かないほうが安全」になる。
    //   2026-10-03 ユーザー指摘「あかりの扇、止まってたら必ず弾が当たらない位置になってる」の本体＝
    //   fan_count=9 に BulletCountMul を掛けた結果が Normal 6 / Hard 10 と偶数になっていた。
    //   丸め方：スケール後の実数値を含む区間 [2k, 2k+2) の奇数 2k+1 を採る（全難易度で同じ式）。
    //   元の密度から必ず ±1 以内＝Easy は薄く Lunatic は濃いという難易度の性格は崩さない。
    //   リング（全方位）には「中心」が無いので ScaleBullets のまま＝呼び元を変えない。
    public int ScaleBulletsOdd(int baseCount) =>
        Mathf.Max(1, Mathf.FloorToInt(baseCount * BulletCountMul * 0.5f) * 2 + 1);

    // 累計浄化数。
    public int PurifiedCount { get; private set; }
    // 累計グレイズ（かすり）数。加算のみで現状は読み手なし（将来グレイズチュートリアルが実装されれば読み手になり得る）。
    public int GrazeCount { get; private set; }
    // このランの被弾回数（Player.TakeHit が実際に♥を減らしたときだけ数える）。ハブ帰還の「被弾は{n}回でした」
    // （仮台本 06 の H1）の差し込み値。補助観測＝表示専用でセーブしない。ResetRun（ラン開始）で 0 に戻る。
    public int RunHitCount { get; private set; }
    public void NotifyPlayerHit()
    {
        RunHitCount++;
        Combo = 0;
    }

    // ステージ目標：このタイムラインを浄化しきる人数。到達でステージクリア。
    public int StageTarget { get; private set; } = 24;
    private bool _stageHasBoss, _stageRoadFinished;
    public void SetStageTarget(int t, bool hasBoss = false)
    {
        StageTarget = Mathf.Max(1, t);
        _stageHasBoss = hasBoss;
    }
    internal void FinishStageRoad() => _stageRoadFinished = true;

    // ───── 前のめり進行（リスクリターン：撃破＋時間＋自機の左右位置）─────
    //   進行度＝「撃破率」が本体。そこへ時間アキュムレータぶんだけ先行（前のめり）を上乗せする。
    //   ・killFrac  ＝撃破率。ゲージの本体＝下限保証（ここが痩せることはない）。
    //   ・progAccum ＝時間アキュムレータ。posFactor(playerX) で毎フレーム積む（右ほど速い）。
    //   ★ゲージのラベルは「浄化」＝撃破の達成度。撃破ゼロで動いてはならない（ラベルが嘘になる）。
    //     そのため時間ぶんは単独では効かせず、**撃破が進んでいるぶんだけ**先行を許す（LeadGate）。
    //     撃破0 → 先行0（ゲージは 0% のまま動かない）。撃破が進むほど前のめりの取り分が増える
    //     ＝「攻めたほうが得」（リスクリターン §2-4）が撃破と比例して立つ。
    //   進行不能防止の不変条件（絶対に壊さない）：
    //     ・道中の出現上限は撃破数で判定し、本ボスの浄化は別に記録する。
    //       progAccum/timeFrac は「見た目のゲージ＝StageProgress／Warmth／背景切替」を先行させるだけ。
    //     ・時間だけではクリアさせない（ボスは必ず撃破required）。
    //   progAccum は ResetRun（ステージ開始）でリセット。
    private float _progAccum;
    private const float ProgBaseRate = 1f / 95f; // 中央基準の進行速度（posFactor=1 で 95 秒フル）
    private const float ProgTimeCap = 0.35f;     // 時間アキュムレータ自体の上限（先行の原資）
    // 自機Xの正規化（0=左端 / 0.5=中央 / 1=右端）。プレイフィールドの矩形は Field が定義元。
    public float PlayerNormX { get; private set; } = 0.5f;
    // 現在の前のめり係数（posFactor）。左端0.55 / 中央1.075 / 右端1.60。artist の背景/HUD が読む。
    public float CurrentPosFactor => PosFactor(PlayerNormX);
    // 前のめり係数：右へ寄る（攻める）ほど進行が速い。式は設計確定値。
    public static float PosFactor(float nx) => 0.55f + 1.05f * Mathf.Clamp(nx, 0f, 1f);

    // 毎フレーム、自機Xと dt を受けて時間アキュムレータを進める（各ステージ Root の _Process から1本呼ぶ）。
    // 時間経過では撃破数もクリア状態も変わらない。
    public void TickProgress(float playerX, float dt)
    {
        PlayerNormX = Mathf.Clamp((playerX - Field.Left) / Field.Width, 0f, 1f);
        _progAccum += ProgBaseRate * PosFactor(PlayerNormX) * (float)dt;
        _progAccum = Mathf.Clamp(_progAccum, 0f, ProgTimeCap);
    }

    // 先行のぶんは「まだ埋まっていない残り幅」から借りる＝撃破ぶんを絶対に食わない・溢れない。
    //   lead = 残り幅(1-killFrac) × timeFrac × LeadGain
    //   ・撃破0 → killFrac=0 でも lead は 0 でなければならない（ラベル「浄化」が嘘になる）ので、
    //     さらに「撃破が始まっているか」の係数 LeadGate(killFrac) を掛ける。
    //   ・LeadGate は単調増加（＝撃破するほど先行の取り分が増える）。減少する形（山型）にすると
    //     終盤の1体を倒した瞬間にゲージが“下がる”＝浄化したのに浄化ゲージが減る最悪の嘘になるため使わない。
    //   ・残り幅から借りるので killFrac<1 の間は必ず 100% 未満、killFrac=1 でちょうど 100%。
    private const float LeadGain = 0.85f;   // 先行の最大取り分（残り幅に対する割合の上限側）
    private const float LeadFullAt = 0.25f; // 撃破率がここに達すると先行の窓が全開（序盤だけ絞る）
    private static float LeadGate(float killFrac) => Mathf.Clamp(killFrac / LeadFullAt, 0f, 1f);

    // 浄化ゲージ(0..1)＝目標までの達成度。世界の暖かさもこれに連動する。
    //   撃破率(killFrac)が本体。そこへ時間ぶん(timeFrac)を「残り幅から・撃破が進んでいるぶんだけ」上乗せする。
    //   撃破0 → LeadGate=0 → ゲージは 0% のまま（時間では一切動かない＝ラベルと実装が一致）。
    public float StageProgress
    {
        get
        {
            if (_stageHasBoss && RedemptionActive) return 1f;
            if (_stageHasBoss && (_stageRoadFinished || BossReached)) return .90f;
            float killFrac = Mathf.Clamp((float)PurifiedCount / StageTarget, 0f, 1f);
            float timeFrac = _progAccum; // TickProgress で 0..ProgTimeCap に clamp 済み
            float lead = (1f - killFrac) * timeFrac * LeadGain * LeadGate(killFrac);
            float progress = Mathf.Clamp(killFrac + lead, 0f, 1f);
            return _stageHasBoss ? Mathf.Min(.89f, .90f * progress) : progress;
        }
    }
    internal bool SpawnTargetReached => PurifiedCount >= StageTarget;
    public bool StageCleared => _stageHasBoss ? RedemptionActive : SpawnTargetReached;

    // 「世界の暖かさ(0=冷たい荒れた世界 → 1=暖かい浄化された世界)」＝浄化の進捗。
    public float Warmth => StageProgress;

    // ミナの汚染ゲージ（0=澄んでいる → 1=黒く溶ける）。穢れを祓うほど自機の光が濁る。
    // シーンをまたいで保持し、各ステージで段階的に上げる（ResetRun では消さない＝物語の背骨）。
    public float Contamination { get; private set; }
    public void SetContamination(float v) => Contamination = Mathf.Clamp(v, 0f, 1f);

    // 設定（Settings 画面から反映）：会話のタイプライター速度／オート送り。
    public float MsgCharsPerSec { get; set; } = DialogueBox.DefaultCharsPerSec;
    public bool AutoAdvanceDialog { get; set; }

    // ───────────────────────────────────────────────────────────
    // 物語ステージ進行（タイムラインハブのルーティング用・STEP2）
    //   ※クリア状態はセッション内のみ保持（永続化は周回設計とともに後続STEPで検討）。
    // ───────────────────────────────────────────────────────────
    public sealed class StageDef
    {
        public string Id = "";
        public string Scene = "";
        public string Handle = "";
        public string Tweet = "";
        public string Title = "";
    }

    // タイムラインに並ぶ投稿（ツイート文は シナリオ設計書 v2 P-01a/P-02a/P-03 準拠）。
    public static readonly StageDef[] Stages =
    {
        new() { Id = "akari",  Scene = "res://Akari.tscn",  Handle = Handles.Akari,  Tweet = "すき、すき、すき。……ひとつでいいから、本物になって。",   Title = "STAGE 1 — あかり" },
        new() { Id = "koharu", Scene = "res://Koharu.tscn", Handle = Handles.Koharu, Tweet = "今日の配信も最高だった。これで明日も学校に行ける。", Title = "STAGE 2 — こはる" },
        new() { Id = "rei",    Scene = "res://Rei.tscn",    Handle = Handles.Rei,    Tweet = "だれも、わたしには追いつけない。……それの、なにが、いけないの。", Title = "STAGE 3 — レイ" },
    };

    // 物語の最初の面のID（＝Stages の先頭。現在は "akari"）。強化ショップの解禁ゲートが引く
    //   ＝「最初の面のボスを倒したら強化が開く」。面の並びを変えても定義が1か所で追随する。
    public static string FirstStageId => Stages[0].Id;

    // FINAL（ミナ自身の内側＝res://MinaBattle.tscn）の記録ID。Stages には並ばない面なので、
    //   クリアタイム/ベストスコアのキーと、ハブのカードID・記録画面の行IDをこの1語に揃える。
    public const string FinalStageId = "final";

    // シーンパス → ステージID（DiffSelect が選択中ステージの解放ゲートを引くのに使う）。未登録は null。
    public static string? StageIdForScene(string scene)
    {
        foreach (var s in Stages)
            if (s.Scene == scene) return s.Id;
        return null;
    }
    private readonly HashSet<string> _cleared = new();
    private readonly HashSet<string> _companionClears = new();
    public bool ClearedWithCompanion(string id) => _companionClears.Contains(id);
    // 直近にクリアしたステージ（ハブ帰還時の会話＆自動投稿トリガ。ハブが消費して null に戻す）。
    public string? JustClearedStageId;
    public bool JustClearedStageWasNew { get; private set; }

    // ───── クリアタイム記録（ステージ×難易度のベスト・永続） ─────
    //   キーは "{stageId}_{Diff}"（例 "rei_Normal"）、値は秒(float)。
    //   1ステージ=1連続クリアタイム。Save/Load(save_N.json) の "clearTimes" に永続。
    public Dictionary<string, float> ClearTimes { get; } = new();
    private static string ClearTimeKey(string stageId, Diff diff) => $"{stageId}_{diff}";
    // ベスト取得（未記録は null）。
    public float? GetBestTime(string stageId, Diff diff)
        => ClearTimes.TryGetValue(ClearTimeKey(stageId, diff), out var v) ? v : (float?)null;
    // クリアタイムを記録：既存ベストより速ければ更新。戻り値 isBest=自己ベスト更新か / prev=更新前のベスト（初回は null）。
    public (bool isBest, float? prev) RecordClearTime(string stageId, Diff diff, float seconds)
    {
        SteamAchievements.RecordClear(stageId, diff, SelectedJob);
        string key = ClearTimeKey(stageId, diff);
        bool had = ClearTimes.TryGetValue(key, out var prev);
        if (!had || seconds < prev)
        {
            ClearTimes[key] = seconds;
            return (true, had ? prev : (float?)null);
        }
        return (false, prev);
    }
    // そのステージで記録のある最速難易度のベスト（ハブのカード表示用）。記録なしは null。
    public (Diff diff, float sec)? BestAcrossDiffs(string stageId)
    {
        (Diff diff, float sec)? best = null;
        foreach (Diff d in System.Enum.GetValues(typeof(Diff)))
        {
            var t = GetBestTime(stageId, d);
            if (t != null && (best == null || t.Value < best.Value.sec))
                best = (d, t.Value);
        }
        return best;
    }

    // ───── ベストスコア記録（ステージ×難易度のベスト・永続） ─────
    //   キーはクリアタイムと同じ "{stageId}_{Diff}"（ClearTimeKey を共有）。ステージ入場ごとに
    //   Score は 0 から数える（ResetRun）ため、1ステージ=1連続クリアのスコアがそのまま記録対象。
    //   Save/Load(save_N.json) の "bestScores" に永続（ClearTimes と同じパターン）。
    public Dictionary<string, long> BestScores { get; } = new();
    // ベスト取得（未記録は null）。
    public long? GetBestScore(string stageId, Diff diff)
        => BestScores.TryGetValue(ClearTimeKey(stageId, diff), out var v) ? v : (long?)null;
    // スコアを記録：既存ベストより高ければ更新。戻り値 isBest=自己ベスト更新か / prev=更新前のベスト（初回は null）。
    public (bool isBest, long? prev) RecordScore(string stageId, Diff diff, long score)
    {
        string key = ClearTimeKey(stageId, diff);
        bool had = BestScores.TryGetValue(key, out var prev);
        if (!had || score > prev)
        {
            BestScores[key] = score;
            return (true, had ? prev : (long?)null);
        }
        return (false, prev);
    }
    private Godot.Collections.Dictionary _sentReplies = new();
    internal List<(string Speaker, string Text)> LimeHistory { get; } = new();
    private readonly Dictionary<string, List<(string Speaker, string Text)>> _limeDirect = new();
    internal List<(string Speaker, string Text)> LimeThread(string id)
    {
        if (!_limeDirect.TryGetValue(id, out var messages)) _limeDirect[id] = messages = new();
        return messages;
    }

    private static Godot.Collections.Array SaveLimeThread(List<(string Speaker, string Text)> messages)
    {
        var rows = new Godot.Collections.Array();
        foreach (var (speaker, text) in messages) rows.Add(new Godot.Collections.Array { speaker, text });
        return rows;
    }

    private static void LoadLimeThread(Variant data, List<(string Speaker, string Text)> messages)
    {
        if (data.VariantType != Variant.Type.Array) return;
        foreach (var item in data.AsGodotArray())
            if (item.VariantType == Variant.Type.Array && item.AsGodotArray() is { Count: 2 } row)
                messages.Add((row[0].AsString(), row[1].AsString()));
    }
    public bool HasReplied(string id) => _sentReplies.ContainsKey(id);
    public void MarkReplied(string id, (string speaker, string text)[] exchange)
    {
        var rows = new Godot.Collections.Array();
        foreach (var (speaker, text) in exchange)
            rows.Add(new Godot.Collections.Array { speaker, text });
        _sentReplies[id] = rows;
    }
    public IEnumerable<(string speaker, string text)> SentReplies(string id)
    {
        if (!_sentReplies.TryGetValue(id, out var rows)) yield break;
        foreach (var row in rows.AsGodotArray())
        {
            var message = row.AsGodotArray();
            yield return (message[0].AsString(), message[1].AsString());
        }
    }

    private readonly HashSet<string> _likedPostIds = new();
    private readonly List<string> _repostedPostIds = new();
    public IReadOnlyList<string> RepostedPostIds => _repostedPostIds;
    public bool IsPostLiked(string id) => _likedPostIds.Contains(id);
    public bool IsPostReposted(string id) => _repostedPostIds.Contains(id);

    public void TogglePostLike(string id)
    {
        if (!_likedPostIds.Remove(id)) _likedPostIds.Add(id);
        AutoSave();
    }

    public void ToggleRepost(string id)
    {
        if (!_repostedPostIds.Remove(id)) _repostedPostIds.Insert(0, id);
        AutoSave();
    }

    // ハブ再訪小話（小話集 v1 §1）の既読管理。キーは "idle_{stageId}_{index}" / "common_{index}"。
    // 未読を優先して抽選し、全部見たらリセットして回す（Hub._Ready が消費）。永続（セーブに含む）。
    private readonly HashSet<string> _idleDialogSeen = new();
    public bool IsIdleDialogSeen(string key) => _idleDialogSeen.Contains(key);
    public void MarkIdleDialogSeen(string key) => _idleDialogSeen.Add(key);
    public string PhoneWallpaperPhotoId { get; private set; } = "";
    public void SetPhoneWallpaperPhoto(string id)
    {
        if (PhoneWallpaperPhotoId == id) return;
        PhoneWallpaperPhotoId = id;
        AutoSave();
    }
    // 全部読み切ったときの抽選プールの戻し。一度きりの会話（ハブ初回の H0 など。接頭辞 "once_"）は
    //   小話ではないので残す＝リセットで再発火させない。新規データ（ResetAll）では丸ごと消える。
    public void ResetIdleDialogSeen()
    {
        _idleDialogSeen.RemoveWhere(k => !k.StartsWith("once_"));
    }
    public bool IsStageCleared(string id) => _cleared.Contains(id);
    // FINAL を一度でもクリアしたか（周回判定）。FINAL は _cleared に入らない（CompleteStage を通らず
    //   StageMina.Step_Transition が RecordClearTime/RecordScore だけを書く）ので、同じ永続項目
    //   ClearTimes に "final_{Diff}" が1つでもあるか＝クリア記録の有無で見る。新しい保存項目は足さない
    //   既存のクリア済みセーブもロードした時点で周回扱いになる。
    public bool IsFinalCleared
    {
        get
        {
            foreach (Diff d in System.Enum.GetValues(typeof(Diff)))
                if (GetBestTime(FinalStageId, d) != null) return true;
            return false;
        }
    }
    private const string FinalBossRevealKey = "once_final_boss_reveal";
    public bool FinalBossRevealed => IsIdleDialogSeen(FinalBossRevealKey) || IsFinalCleared;
    public void RevealFinalBoss()
    {
        if (!_idleDialogSeen.Add(FinalBossRevealKey)) return;
        AutoSave();
    }

    public bool EndingRollSeen { get; private set; }

    public void MarkEndingRollSeen()
    {
        EndingRollSeen = true;
        AutoSave();
    }
    public int HeartsSaved { get { int n = 0; foreach (var s in Stages) if (_cleared.Contains(s.Id)) n++; return n; } }
    public bool AllStoryCleared
    {
        get { foreach (var s in Stages) if (!_cleared.Contains(s.Id)) return false; return true; }
    }
    public string? NextUnclearedStageId()
    {
        foreach (var s in Stages) if (!_cleared.Contains(s.Id)) return s.Id;
        return null;
    }
    // 解禁条件：クリア済（周回可）or 物語順で次の未クリア（一本道を保つ・§③）。
    public bool IsStageUnlocked(string id) => IsStageCleared(id) || id == NextUnclearedStageId();

    // ステージ完了：クリア報酬を計上し、クリア済に記録。ハブ帰還前に各ステージから呼ぶ。
    public void CompleteStage(string id)
    {
        JustClearedStageWasNew = _cleared.Add(id);
        if (JustClearedStageWasNew)
        {
            if (Jobs.UnlockedBy(id) != null) _pendingUnlockCards.Insert(0, id);
            SteamAchievements.RecordCharacterUnlocks(this);
        }
        if (System.Array.Exists(Stages, stage => stage.Id == id))
        {
            if (SelectedJob != Job.Tank)
            {
                if (!IsLunatic) _charDives[JobDef.CharacterId] = CharacterChapter(SelectedJob);
                _companionClears.Add(id);
            }
            else _companionClears.Remove(id);
        }
        JustClearedStageId = id; // ハブで帰還会話＆自動投稿を再生する
        RegisterStageClear();
    }

    // ─── 周回（同ステージ再プレイ）報酬の逓減（①-7）───
    //   同ステージを同難度以下で連続周回すると Imp/Fol が逓減（×0.8^連続回数、下限0.4）。
    //   別ステージへ移る or 難度を上げると逓減リセット。
    private string _lastRunStage = "";
    private int _lastRunDiff = -1;
    private int _repeatStreak;
    public float ReplayMul { get; private set; } = 1f;
    public int RepeatStreak => _repeatStreak; // HUDの逓減表示用（「連続N回目」＝この値+1）
    private readonly Dictionary<string, int> _stagePlays = new();
    public int StagePlays(string id) => _stagePlays.TryGetValue(id, out var v) ? v : 0;
    // この起動で潜った回数の合計（ハブ再訪小話 H2r の補助観測「この旅で潜った回数、{dives}回」）。
    // セーブしない＝「この旅で」＝いま続いている一続きのプレイ、という読みに合わせる。
    public int TotalDives { get { int n = 0; foreach (var v in _stagePlays.Values) n += v; return n; } }

    // ─── 炎上（②-5 / ③-6）───
    //   発生は一度きり（STAGE2＝こはるクリア後）。次のダイブ1ステージ＝レイ面だけ弱体化（発射↓/移動↓/インプレ×0.6）。
    public bool Burning;          // 炎上発生済みで未消費（次のダイブで適用）
    public bool BurningThisRun;   // 現在のステージrunが炎上下か（Player/Hudが参照）
    private bool _burnHappened;    // 一度きりのストーリーイベント済みか

    // ─── 仕掛けの値（案C の組み込み計画）───
    //   選択のたびに「選ばれなかった言葉」が散り、終盤（FINAL F4 / エピローグ E2）でそれが戻ってくる。
    //   ここは**器だけ**：各場面からの記録は台本タスクで繋ぐので、現時点でこれらを書く呼び出しは無い。
    //   セーブの作法は他の後付け項目と同じ＝キー無し＝既定値（旧セーブがそのまま読める）。
    public readonly List<string> ScatteredWords = new(); // 散った言葉（選ばれなかった候補）。出た順
    public string FirstScattered = "";                   // 最初に散らした言葉（F4 で戻る一語）
    public int NameRoute;                                // 命名ルート 0〜2（冒頭 P2 の3択）

    // 「もう名前が付いたか」＝話者名を「ミナ」と出してよいか（Prologue の P3 命名で立つ）。
    //   セーブに載せない static（新しい永続項目は足さない）。既定 true＝プロローグ以外の全画面は従来どおり。
    //   Prologue._Ready() がここを false に倒し、P3 の命名（[ M I N A ] 点灯）で true へ戻す
    //   ＝周回2周目以降も毎回伏せる（同じ体験）。命名前の話者名は「？」（立ち絵は出したまま）。
    //   参照は話者ラベルを出す2箇所だけ：src/Hud.cs（ShowDialog / BacklogSpeaker）と src/Prologue.cs（SpeakerOf）。
    public static bool MinaNamed = true;
    public string LastSentWord = "";                     // 最後に送った言葉（E2 の合言葉。既存の "stay" ゲートを置換）
    public float HesitationSec;                          // 迷い秒数の累計（選択に掛けた時間）

    // 1つの選択の結果を記録する。id ごとに上書きできる＝選び直し／リトライで二重計上しない。
    //   chosen … 選ばれた言葉（散らない）／others … 選ばれなかった候補（＝散る言葉）
    //   同じ id で呼び直すと、前回その id で散らせた語を取り消してから積み直す。
    private readonly Dictionary<string, List<string>> _scatterById = new();
    private readonly Dictionary<string, float> _hesitationById = new();
    // 選択IDごとの「送った言葉」と「迷い秒数」。下流の場面が **どの選択肢を選んだか** を後から引ける台帳。
    //   F1 導入（S3-7 の分岐受け）と E6 の対句（P2 の秒数と比較）が参照する。
    //   （送らない）＝空文字で記録される＝「無言だった」も区別できる。セーブに載せる（後方互換：キー無し＝空）。
    private readonly Dictionary<string, string> _chosenById = new();
    public string ChosenAt(string id) => _chosenById.TryGetValue(id, out var v) ? v : "";
    public bool HasChoiceAt(string id) => _chosenById.ContainsKey(id);
    public float HesitationAt(string id) => _hesitationById.TryGetValue(id, out var v) ? v : 0f;
    public void RecordChoice(string id, string chosen, IEnumerable<string> others, float hesitationSec)
    {
        // 同じ id の前回ぶんを取り消す（散った言葉・迷い秒数とも）。
        if (_scatterById.TryGetValue(id, out var prev))
            foreach (var w in prev) ScatteredWords.Remove(w);
        if (_hesitationById.TryGetValue(id, out var prevSec)) HesitationSec -= prevSec;

        var list = new List<string>();
        foreach (var w in others)
            if (!string.IsNullOrEmpty(w)) list.Add(w);
        _scatterById[id] = list;
        ScatteredWords.AddRange(list);

        _hesitationById[id] = hesitationSec;
        HesitationSec += hesitationSec;
        _chosenById[id] = chosen ?? "";

        // 最初に散らした言葉は一度決まったら動かさない（＝F4 で戻る一語を選び直しで揺らさない）。
        if (string.IsNullOrEmpty(FirstScattered) && list.Count > 0) FirstScattered = list[0];
        if (!string.IsNullOrEmpty(chosen)) LastSentWord = chosen;
    }

    public bool ShouldBurnAfter(string clearedStageId) => clearedStageId == "koharu" && !_burnHappened;
    public void TriggerBurn() { if (!_burnHappened) { Burning = true; _burnHappened = true; } }

    // ステージ開始時に各ステージルートから呼ぶ：周回逓減の更新＋炎上の消費。
    public void BeginStageRun(string id)
    {
        _stageRewardPending = true;
        bool diffIncreased = (int)Difficulty > _lastRunDiff;
        if (id == _lastRunStage && !diffIncreased) _repeatStreak++;
        else _repeatStreak = 0;
        _lastRunStage = id;
        _lastRunDiff = (int)Difficulty;
        ReplayMul = Mathf.Max(0.4f, Mathf.Pow(0.8f, _repeatStreak));
        _stagePlays[id] = StagePlays(id) + 1;

        // 炎上は「次の1ステージだけ」。ここで消費してこのrun限定で有効化。
        BurningThisRun = Burning;
        Burning = false;

        // ステージBGM開始（全ステージ共通フック）。同じ曲なら継続＝リトライで途切れない。
        //   ステージ別の道中曲を引く（rei＝実音源 BgmStageRei／他＝合成 BgmStage）。
        //   StageBgm() に渡す id は中ボス撃破後の道中復帰でも再利用するため Audio に控える。
        if (Audio.Instance != null) Audio.Instance.SetStageMusic(id);
    }

    // ───────────────────────────────────────────────────────────
    // メタ進行：インプレッション（通貨）/ フォロワー / 恒久強化
    // ───────────────────────────────────────────────────────────

    // インプレッション＝お金。強化購入に使う（使うと減る）。永続。
    public long Impression { get; private set; }
    // フォロワーは「届けた証」として蓄積し、インプレ倍率に反映する。
    public int Followers { get; private set; }
    private bool _stageRewardPending;
    public const int ScorePerImpression = 100;
    public long PendingStageImpression => _stageRewardPending && !TrainingMode && !BossPracticeActive
        ? (long)System.Math.Round((decimal)Score / ScorePerImpression * (decimal)TotalImpressionMul
            * (decimal)ReplayMul * (decimal)MoneyGainMul, System.MidpointRounding.AwayFromZero)
        : 0;

    public long SettleStageScore()
    {
        long reward = PendingStageImpression;
        _stageRewardPending = false;
        Impression += reward;
        return reward;
    }

    private Dictionary<Job, HashSet<string>> _ownedUpgrades = Jobs.All.ToDictionary(job => job.Id, _ => new HashSet<string>());
    public int OwnedUpgradeCount => _ownedUpgrades[SelectedJob].Count;

    // 強化カタログ（§①-4）。効果は下の各アクセサで定義。
    public sealed class UpgradeDef
    {
        public string Id = "";
        public string Name = "";   // ＝効果そのもの（画面に出る唯一の説明）
        public string Desc = "";   // 詳細パネルの「いま → 買うと」を組む材料
        public string? PrerequisiteId;
        public int MaxLevel;
        public long BaseCost;
        public int Rating;
    }

    public static readonly UpgradeDef[] Upgrades =
    {
        new() { Id = "n_life_1",   Name = "ハート +1",             Desc = "はじまりの♥が1つ増える",               MaxLevel = 1, BaseCost =  700, Rating = 3 },
        new() { Id = "n_dodge",    Name = "回避",                  Desc = "一瞬無敵になって弾を抜ける",             MaxLevel = 1, BaseCost = 2400, Rating = 5 },
        new() { Id = "n_dodge_cd", Name = "回避強化", Desc = "「回避」習得後、再使用が早まり距離も伸びる",         MaxLevel = 1, BaseCost =  300, Rating = 2, PrerequisiteId = "n_dodge" },
        new() { Id = "n_bomb_1",   Name = "ボム +1",               Desc = "はじまりのボムが1つ増える",             MaxLevel = 1, BaseCost =  800, Rating = 3 },
        new() { Id = "n_heart_magnet", Name = "心の回収範囲アップ", Desc = "心の欠片を1.5倍遠くから引き寄せる",     MaxLevel = 1, BaseCost =  100, Rating = 1 },
        new() { Id = "n_life_2",   Name = "ハート +1",             Desc = "はじまりの♥がもう1つ増える",           MaxLevel = 1, BaseCost =  700, Rating = 3 },
        new() { Id = "n_charge_shot", Name = "溜め打ち", Desc = "長押しして離すと、キャラクター固有の溜め弾を撃つ", MaxLevel = 1, BaseCost = 1400, Rating = 4 },
        new() { Id = "n_charge",   Name = "溜め打ち 二段",         Desc = "「溜め打ち」習得後、さらにためると威力と大きさが増す", MaxLevel = 1, BaseCost = 1600, Rating = 4, PrerequisiteId = "n_charge_shot" },
        new() { Id = "n_hitbox",   Name = "当たり判定 縮小",       Desc = "被弾判定の半径が25%小さくなる",          MaxLevel = 1, BaseCost = 1200, Rating = 4 },
        new() { Id = "n_hit_invul", Name = "被弾後の無敵時間アップ", Desc = "被弾後の無敵時間が0.4秒長くなる",      MaxLevel = 1, BaseCost =  200, Rating = 2 },
        new() { Id = "n_move_15x", Name = "移動速度 1.5倍",        Desc = "移動が1.5倍速くなる",                   MaxLevel = 1, BaseCost =  650, Rating = 3 },
        new() { Id = "n_slow",     Name = "必殺技",    Desc = "キャラクター固有の必殺技を覚える",                  MaxLevel = 1, BaseCost = 2200, Rating = 5 },
    };

    // 能力を覚える段（Shop が一回り大きく描く）。数値ではなく「できることが増える」段。
    public static bool IsAbilityNode(string id) => id is "n_dodge" or "n_slow" or "n_charge_shot" or "n_charge";

    // 所持判定のショートハンド（効果アクセサがこれ1本で読む＝ChainLevel の段数計算は要らなくなった）。
    public bool Has(string id) => _ownedUpgrades[SelectedJob].Contains(id);

    // 所持している段のID列（検証ログ用。挙動には影響しない）。
    public List<string> ColumnOwnedIds()
    {
        var a = new List<string>();
        foreach (var d in Upgrades) if (Has(d.Id)) a.Add(d.Id);
        return a;
    }

    public static UpgradeDef? GetUpgradeDef(string id)
    {
        foreach (var d in Upgrades)
            if (d.Id == id) return d;
        return null;
    }

    public int GetUpgradeLevel(string id) => Has(id) ? 1 : 0;

    //   方針は「没収ゼロ」：旧セーブが強化に投じた総額を決定的に再計算し、その金額で新列の先頭から
    //   買えるだけ自動所持させる（差額は返金しない＝旧価格でどこまで積めたかが、そのまま新列の到達段になる）。
    //   ・旧MaxLv方式（"fire_rate":3 のように Lv 値を持つ）は「Lv n＝その鎖の先頭 n 段を買った」とみなし、
    //     旧ノード価格表 LegacyCost から総額を積む。
    //   ・単Lvノード方式（"fire_rate_1":1 …）はそのノードの旧価格をそのまま積む。
    //   ・どちらでもない未知キーは無視（壊れたセーブで落ちない）。
    //   次の保存でキャラクター別形式になり、同じ購入への払い戻しは繰り返さない。
    private static readonly Dictionary<string, long> LegacyCost = new()
    {
        // 旧70ノードの BaseCost（2026-09-13 以前のカタログ）。総支払額の再計算にだけ使う。
        ["fire_rate_1"] = 100, ["fire_rate_2"] = 350, ["fire_rate_3"] = 462, ["fire_rate_4"] = 610,
        ["shot_power_1"] = 100, ["shot_power_2"] = 400, ["shot_power_3"] = 540, ["shot_power_4"] = 729,
        ["rapid_power_1"] = 100, ["rapid_power_2"] = 450,
        ["rapid_rate_1"] = 100, ["rapid_rate_2"] = 400,
        ["pierce_1"] = 800, ["pierce_2"] = 1280,
        ["focus_1"] = 800, ["focus_2"] = 1280,
        ["spread_1"] = 100, ["spread_2"] = 500, ["spread_3"] = 690,
        ["spread_power_1"] = 100, ["spread_power_2"] = 420,
        ["spread_rate_1"] = 100,
        ["fol_gain_1"] = 100, ["fol_gain_2"] = 300,
        ["combo_hold_1"] = 100, ["combo_hold_2"] = 200,
        ["option_1"] = 900, ["option_2"] = 1440,
        ["chain_1"] = 800, ["chain_2"] = 1280,
        ["homing_1"] = 100, ["homing_2"] = 550, ["homing_3"] = 825,
        ["homing_power_1"] = 100, ["homing_power_2"] = 480,
        ["homing_rate_1"] = 100,
        ["counter_1"] = 800, ["counter_2"] = 1280,
        ["veil_1"] = 800, ["veil_2"] = 1280,
        ["accel_1"] = 100, ["accel_power_1"] = 100, ["accel_power_2"] = 450,
        ["accel_charge_1"] = 100, ["accel_charge_2"] = 420, ["accel_speed_1"] = 400,
        ["bf_power_1"] = 100, ["bf_power_2"] = 380, ["bf_power_3"] = 532,
        ["bf_rate_1"] = 100, ["bf_rate_2"] = 420, ["bf_track_1"] = 460,
        ["move_speed_1"] = 100, ["move_speed_2"] = 250, ["move_speed_3"] = 350,
        ["contam_1"] = 100, ["contam_2"] = 300,
        ["hitbox_1"] = 100, ["hitbox_2"] = 600, ["hitbox_3"] = 930,
        ["imp_mult_1"] = 100, ["imp_mult_2"] = 300, ["imp_mult_3"] = 435, ["imp_mult_4"] = 631,
        ["max_life_1"] = 100, ["max_life_2"] = 550,
        ["bomb_count_1"] = 100, ["bomb_count_2"] = 450,
        ["bomb_power_1"] = 100, ["bomb_power_2"] = 350,
    };

    // さらに古い「Lv値」セーブのID → 旧ノード鎖（LegacyCost を引く順序）。
    private static readonly Dictionary<string, string[]> LegacyChains = new()
    {
        ["fire_rate"]     = new[] { "fire_rate_1", "fire_rate_2", "fire_rate_3", "fire_rate_4" },
        ["shot_power"]    = new[] { "shot_power_1", "shot_power_2", "shot_power_3", "shot_power_4" },
        ["shot_spread"]   = new[] { "spread_1", "spread_2", "spread_3" },
        ["shot_homing"]   = new[] { "homing_1", "homing_2", "homing_3" },
        ["move_speed"]    = new[] { "move_speed_1", "move_speed_2", "move_speed_3" },
        ["hitbox"]        = new[] { "hitbox_1", "hitbox_2", "hitbox_3" },
        ["contam_resist"] = new[] { "contam_1", "contam_2" },
        ["imp_mult"]      = new[] { "imp_mult_1", "imp_mult_2", "imp_mult_3", "imp_mult_4" },
        ["fol_gain"]      = new[] { "fol_gain_1", "fol_gain_2" },
        ["combo_hold"]    = new[] { "combo_hold_1", "combo_hold_2" },
        ["max_life"]      = new[] { "max_life_1", "max_life_2" },
        ["bomb_count"]    = new[] { "bomb_count_1", "bomb_count_2" },
        ["bomb_power"]    = new[] { "bomb_power_1", "bomb_power_2" },
        ["shot_pierce"]   = new[] { "pierce_1", "pierce_2" },
        ["focus_fire"]    = new[] { "focus_1", "focus_2" },
        ["option_sub"]    = new[] { "option_1", "option_2" },
        ["chain_light"]   = new[] { "chain_1", "chain_2" },
        ["counter_light"] = new[] { "counter_1", "counter_2" },
        ["veil_light"]    = new[] { "veil_1", "veil_2" },
    };

    //   移行した段数は MigratedNodeCount に控える（ショップ／QAログが「何段引き継いだか」を見せる用）。
    public int MigratedNodeCount { get; private set; } = -1; // -1＝このロードでは移行が走らなかった
    private void MigrateSharedUpgrades(Dictionary<string, int> upgrades)
    {
        if (upgrades.Remove("n_power_2x", out int power) && power > 0) Impression += 700;
        if (upgrades.Remove("n_rate_2x", out int rate) && rate > 0) Impression += 3000;
        if (upgrades.Remove("n_lines", out int lines) && lines > 0) Impression += 1800;
        if (upgrades.Remove("n_pierce", out int pierce) && pierce > 0) Impression += 3500;
        if (upgrades.Remove("n_option", out int option) && option > 0) Impression += 4000;
        long paid = 0;
        bool legacy = false;
        foreach (var kv in upgrades)
        {
            if (GetUpgradeDef(kv.Key) != null) continue;            // 既に新ID＝そのまま残す
            legacy = true;
            if (LegacyCost.TryGetValue(kv.Key, out var c)) { if (kv.Value >= 1) paid += c; continue; }
            if (LegacyChains.TryGetValue(kv.Key, out var chain))
            {
                int n = Mathf.Clamp(kv.Value, 0, chain.Length);
                for (int i = 0; i < n; i++) paid += LegacyCost[chain[i]];
            }
            // 上のどれでもない未知キーは無視（壊れたセーブで落ちない）。
        }
        if (!legacy) return;

        // 旧IDを全部落とし、再計算した総額で新列の先頭から買えるだけ所持させる（没収ゼロ）。
        foreach (var id in upgrades.Keys.Where(id => GetUpgradeDef(id) == null).ToArray()) upgrades.Remove(id);
        int got = 0;
        // 価格調整で旧セーブの引き継ぎ内容が変わらないよう、移行時の価格と並びを固定する。
        foreach (var (id, cost) in new (string, long)[]
        {
            ("n_life_1", 150), ("n_dodge", 200), ("n_dodge_cd", 300), ("n_bomb_1", 450),
            ("n_heart_magnet", 650), ("n_life_2", 900), ("n_charge", 1200), ("n_hitbox", 1500),
            ("n_hit_invul", 1800), ("n_move_15x", 2200), ("n_slow", 2600),
        })
        {
            if (upgrades.GetValueOrDefault(id) >= 1) { got++; continue; }
            if (paid < cost) break;
            paid -= cost;
            upgrades[id] = 1;
            got++;
        }
        MigratedNodeCount = got;
        GD.Print($"[migrate] legacy upgrades -> column: {got} nodes owned (leftover {paid} imp discarded)");
    }

    public static long CostAt(UpgradeDef d, int lv)
    {
        if (lv >= d.MaxLevel) return 0;
        return d.BaseCost;
    }

    // 次レベルの価格。最大Lv到達 or 不正idなら -1。
    public long GetUpgradeCost(string id)
    {
        var d = GetUpgradeDef(id);
        if (d == null) return -1;
        int lv = GetUpgradeLevel(id);
        if (lv >= d.MaxLevel) return -1;
        return CostAt(d, lv);
    }

    public UpgradeDef? GetMissingUpgradePrerequisite(string id)
    {
        string? prerequisite = GetUpgradeDef(id)?.PrerequisiteId;
        return prerequisite != null && !Has(prerequisite) ? GetUpgradeDef(prerequisite) : null;
    }

    public bool CanPurchase(string id)
    {
        long c = GetUpgradeCost(id);
        return c >= 0 && Impression >= c && GetMissingUpgradePrerequisite(id) == null;
    }

    public bool TryPurchase(string id)
    {
        if (!CanPurchase(id)) return false;
        Impression -= GetUpgradeCost(id);
        _ownedUpgrades[SelectedJob].Add(id);
        SteamAchievements.RecordUpgrade();
        AutoSave();
        return true;
    }

    // ───── トレーニングモード（試し打ち場）用：メタ状態の退避／復元と、ゲート無視の直書き ─────
    //   トレーニングは「完全無料・試用のみ」＝本番の購入済み・所持ポイント・装備を一切変えずに
    //   スキルを自由に付け外しして撃ち味を比べる場。入場時に SnapshotMeta で退避し、退場時に RestoreMeta で
    //   丸ごと戻す（＝本番状態を壊さない）。試用した全キャラの強化を復元する。
    //   ※ディスクへの漏れ防止は呼び出し側が AutoSaveEnabled=false で担保する（Hub帰還等の自動セーブを止める）。
    public sealed class MetaSnapshot
    {
        public long Impression;
        public int Followers;
        public ShotMode SelectedShotMode;
        public Job SelectedJob;
        public Dictionary<Job, HashSet<string>> Upgrades = new();
    }

    // 現在のメタ状態（通貨・フォロワー・装備モード・所持強化）をディープコピーして退避する。
    public MetaSnapshot SnapshotMeta()
    {
        var s = new MetaSnapshot
        {
            Impression = Impression,
            Followers = Followers,
            SelectedShotMode = SelectedShotMode,
            SelectedJob = SelectedJob,
            Upgrades = _ownedUpgrades.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value)),
        };
        return s;
    }

    // 退避したメタ状態へ完全復元する（トレーニング退場時に必ず呼ぶ）。
    public void RestoreMeta(MetaSnapshot s)
    {
        if (s == null) return;
        Impression = s.Impression;
        Followers = s.Followers;
        SelectedJob = s.SelectedJob;      // ジョブ→モードの順（セッタがモードを上書きするため）
        SelectedShotMode = s.SelectedShotMode;
        _ownedUpgrades = s.Upgrades.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value));
    }

    // トレーニング用：親/前提/封印・価格を無視して1ノードを直接 付ける/外す（購入パスを通さない）。
    public void TrainingSetUpgrade(string id, bool owned)
    {
        if (GetUpgradeDef(id) == null) return;
        if (owned) _ownedUpgrades[SelectedJob].Add(id);
        else _ownedUpgrades[SelectedJob].Remove(id);
        if (id == "n_slow" && !owned) CancelSpecial();
    }

    // トレーニング用：カタログ全ノードを一括で 付ける/外す（撃ち味の全開↔素の比較に使う）。
    public void TrainingSetAllUpgrades(bool owned)
    {
        foreach (var d in Upgrades) TrainingSetUpgrade(d.Id, owned);
    }

    // トレーニング用：通貨を直接セットする（試用中は実質無限。表示専用＝購入では減らさない運用）。
    public void TrainingSetImpression(long v) => Impression = v;

    public float FollowerImpressionMul => 1f + Mathf.Min(0.50f, Followers * 0.00008f);

    // ── 難易度・強化由来のインプレ倍率 ──
    public static float DifficultyImpressionMulFor(Diff d) => d switch { Diff.Easy => 0.7f, Diff.Hard => 1.6f, Diff.Lunatic => 3.0f, _ => 1f };
    public float DifficultyImpressionMul => DifficultyImpressionMulFor(Difficulty);
    // 獲得心の倍率。一本道では「獲得倍率を買う」段を持たないので恒久ボーナスは無し（1.0 固定）。
    public float UpgradeImpressionMul => 1f;
    // 獲得インプレ（お金）全体の追加倍率。コスト/価格には掛からない＝獲得だけ増える。後で調整しやすいよう定数化。
    public const float MoneyGainMul = 2f;
    // 炎上中は収入 0.6倍。★2026-09-13：炎上の弱体は「収入が減る」だけに絞った
    //   （旧実装の 連射×1.3／移動×0.9 は、手触りそのものを鈍らせて理由も見えない＝いちばん質の悪い罰だった）。
    public float TotalImpressionMul => DifficultyImpressionMul * FollowerImpressionMul * UpgradeImpressionMul * (BurningThisRun ? 0.6f : 1f);

    // #10 移動速度1.5倍（低速移動の廃止＝2026-09-13 により、速度は1本になった）。炎上による移動弱体も撤廃。
    public float MoveSpeedMul => Has("n_move_15x") ? 1.5f : 1f;
    public float HitRadiusMul => Has("n_hitbox") ? 0.75f : 1f;
    public const float HeartMagnetUpgradeMul = 1.5f;
    public const float HitInvulUpgradeBonus = 0.4f;
    public float HeartMagnetMul => Has("n_heart_magnet") ? HeartMagnetUpgradeMul : 1f;
    public float HitInvulSec => JobDef.HitInvulSec + (Has("n_hit_invul") ? HitInvulUpgradeBonus : 0f);
    // #1 #6 ハート +1 ×2段（表示はどちらも「+1」。累計は自然に +2）。
    public int MaxLifeBonus => (Has("n_life_1") ? 1 : 0) + (Has("n_life_2") ? 1 : 0);
    // #4 ボム +1。
    public int BombCountBonus => Has("n_bomb_1") ? 1 : 0;
    public bool HasChargeShot => Has("n_charge_shot");
    public float ChargeNeedSec => 0.60f;
    // #7 2段階チャージ＝1段で離さずさらに押し続けると 2段目が貯まる（ChargeTier）。
    //   ★IDが n_charge のままなので、旧「溜め打ち 短縮」を買い済みのセーブはそのまま 2段目が開く。
    public bool HasChargeTier2 => HasChargeShot && Has("n_charge");
    // 2段目が満ちるまでの長押し秒（合計）。未所持なら 1段目と同じ＝それ以上は溜まらない。
    public float ChargeTier2NeedSec => HasChargeTier2 ? ChargeNeedSec * ChargeTier.HoldMul : ChargeNeedSec;
    // 購入済みの集中モードをそのまま引き継ぐため、保存IDは維持する。
    public bool HasSpecial => Has("n_slow");
    // #2 回避（Space / L3 / 右クリック・無敵 0.45秒）。未所持のあいだ Player.TryDodge は即 return し、
    //   あそびかた／操作カードの回避行は未取得として薄く出る＝解禁が画面で見える。
    //   ★名前と型（bool HasDodge）は Player.cs・StageZero.cs・HowToPlay.cs が読むので変えない。
    public bool HasDodge => Has("n_dodge");
    // #3 回避強化（CD 0.8→0.65秒・距離 64→76px）。
    public float DodgeCooldown => Has("n_dodge_cd") ? 0.65f : 0.80f;
    public float DodgeDistance => Has("n_dodge_cd") ? 76f : 64f;

    // ── 旧ノードが消えたぶんの既定値（買えなくなった軸は「旧ノードを買い切った値」で固定する）──
    //   ＝一本道化で強化軸が消えても、撃ち味そのものは旧・最終段のまま。弱体化ゼロ（査読確定・§9）。
    public float SpreadPowerMul => 0.62f;      // 旧 spread_power_2
    public float SpreadRateMul => 1.35f;       // 旧 spread_rate_1
    public float HomingPowerMul => 1.05f;      // 旧 homing_power_2
    public float HomingRateMul => 1.40f;       // 旧 homing_rate_1
    public int HomingTurnRateOverride => Jobs.Get(Job.Heal).HomingTurnRate;
    public float AccelChargeDelay => Jobs.Get(Job.Melee).AccelDelay;
    public float AccelLaunchSpeed => Jobs.Get(Job.Melee).ShotSpeed;
    public float BombPowerMul => 1.25f;        // 旧 bomb_power_1

    // 汚染耐性（旧 contam）は買えなくなった＝素の上昇率のまま。
    public float ContaminationGainMul => 1f;

    // ── 旧・奥義ノード由来の派生機能（返し光／集中の光／連鎖の光／祈りの帳）──
    //   一本道14段には入らなかったので、恒久強化としては常に 0＝オフ。Player 側の実装は残してあり、
    //   ここを 1 以上に戻せば即復活する（機能を削るのではなく、買う手段を畳んだ）。
    //   ただし祈りの帳だけは「祈り手が素で持つ小さい帳」（JobDef.VeilFloor*）が生き続ける＝ジョブの個性は消さない。
    public int CounterLightLevel => 0;
    public int FocusFireMaxStack => 0;
    public int ChainLightBounces => 0;
    public float VeilLightRadius => JobDef.VeilFloorRadius;
    public float VeilLightDuration => JobDef.VeilFloorDuration;

    // ── 旧・モード別の上乗せ（連射威力／速射）。買う段が無くなったので素の値で固定 ──
    public int RapidPowerBonus => 0;
    public float RapidRateMul => 1f;

    // インプレを獲得（全倍率を適用して加算）。実際に加算した額を返す。
    public long GainImpression(long baseAmount)
    {
        if (baseAmount <= 0) return 0;
        long g = (long)Mathf.Round(baseAmount * TotalImpressionMul * ReplayMul * MoneyGainMul);
        Impression += g;
        return g;
    }

    public void AddFollowers(int n)
    {
        if (n <= 0) return;
        Followers += n;
    }

    // ステージクリア（浄化100%）時の大口報酬。帰還演出から呼ぶ（STEP2/5で配線）。
    public void RegisterStageClear()
    {
        SettleStageScore();
        // フォロワー大口報酬。周回逓減も適用（同ステージ連続周回で減る）。
        int fol = Mathf.RoundToInt(40 * ReplayMul); // 旧 fol_gain ノードは廃止＝素の 40 に周回逓減だけ
        AddFollowers(fol);
        AutoSave(); // クリアでオートセーブ（slot 0）
    }

    public const int SlotCount = 3;
    public double PlayTimeSeconds { get; private set; }
    private bool _playTimePartial;

    private sealed partial class PlayClock : Node
    {
        internal GameManager Game = null!;
        public override void _Process(double delta)
        {
            string scene = GetTree().CurrentScene?.SceneFilePath ?? "";
            if (scene.Length == 0 || scene is "res://TitleMenu.tscn" or "res://Settings.tscn" or "res://Credits.tscn"
                || LoadingScreen.IsActive) return;
            Game.PlayTimeSeconds += delta / Engine.TimeScale;
        }
    }
    private static string SlotPath(int slot) => Startup.DataPath($"save_{slot}.json");
    public bool SlotExists(int slot) => FileAccess.FileExists(SlotPath(slot));

    public sealed record SaveSlotInfo(bool Exists, bool Readable, string SavedAt, string PlayTime, string Progress, string Details);

    public SaveSlotInfo ReadSlotInfo(int slot)
    {
        string path = SlotPath(slot);
        if (!FileAccess.FileExists(path)) return new(false, false, "", "", "", "");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        using var json = new Json();
        if (file == null || json.Parse(file.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return new(true, false, "読み込めないデータ", "", "", "");
        var data = json.Data.AsGodotDictionary();
        long timestamp = data.ContainsKey("savedAt") ? data["savedAt"].AsInt64() : (long)FileAccess.GetModifiedTime(path);
        string savedAt = timestamp is > 0 and <= 253402300799
            ? System.DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime().ToString("yyyy/MM/dd HH:mm") : "日時未記録";
        double seconds = data.ContainsKey("playTimeSeconds") ? data["playTimeSeconds"].AsDouble() : -1;
        string playTime = seconds >= 0 && double.IsFinite(seconds)
            ? $"{System.Math.Floor(seconds / 3600):0}時間{System.Math.Floor(seconds / 60) % 60:00}分" : "未記録";
        if (data.ContainsKey("playTimePartial") && data["playTimePartial"].AsBool()) playTime += "（記録開始後）";
        int clears = 0;
        if (data.ContainsKey("cleared") && data["cleared"].VariantType == Variant.Type.Array)
            foreach (var stage in Stages)
                if (data["cleared"].AsGodotArray().Contains(stage.Id)) clears++;
        bool final = false;
        if (data.ContainsKey("clearTimes") && data["clearTimes"].VariantType == Variant.Type.Dictionary)
            foreach (Diff diff in System.Enum.GetValues<Diff>())
                final |= data["clearTimes"].AsGodotDictionary().ContainsKey(ClearTimeKey(FinalStageId, diff));
        string progress = $"ステージクリア {clears}/{Stages.Length}" + (final ? "　FINAL クリア" : "");
        int job = data.ContainsKey("job") ? data["job"].AsInt32() : (int)Job.Tank;
        string character = Jobs.Get(System.Enum.IsDefined(typeof(Job), job) ? (Job)job : Job.Tank).CharacterName;
        string difficulty = data.ContainsKey("difficulty")
            ? ((Diff)Mathf.Clamp(data["difficulty"].AsInt32(), 0, 3)).ToString().ToUpperInvariant() : "難易度未記録";
        long impression = data.ContainsKey("impression") ? data["impression"].AsInt64() : 0;
        int followers = data.ContainsKey("followers") ? data["followers"].AsInt32() : 0;
        return new(true, true, savedAt, $"プレイ時間 {playTime}", progress,
            $"{character} / {difficulty}　フォロワー {followers:N0}　Imp {impression:N0}");
    }

    public bool SaveToSlot(int slot, bool settleStage = false)
    {
        if (TrainingMode || BossPracticeActive) return false;
        var data = new Godot.Collections.Dictionary
        {
            ["impression"] = Impression + (settleStage ? PendingStageImpression : 0),
            ["followers"] = Followers,
            ["shotmode"] = (int)SelectedShotMode,
            // ジョブ（ラン単位の選択だが「次に潜るときの既定」としてスロットに残す）。後方互換：キー無し＝結び手。
            ["job"] = (int)SelectedJob,
            ["savedAt"] = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["playTimeSeconds"] = PlayTimeSeconds,
            ["playTimePartial"] = _playTimePartial,
            ["difficulty"] = (int)Difficulty,
        };
        SaveCosmetics(data);
        var purchases = new Godot.Collections.Dictionary();
        foreach (var job in Jobs.All)
            purchases[job.CharacterId] = _ownedUpgrades[job.Id].ToArray();
        data["upgradesByCharacter"] = purchases;
        // クリアタイム（"{stageId}_{Diff}" → 秒）。後方互換：読み手はキー無しを空扱い。
        var ct = new Godot.Collections.Dictionary();
        foreach (var kv in ClearTimes)
            ct[kv.Key] = kv.Value;
        data["clearTimes"] = ct;
        // ベストスコア（"{stageId}_{Diff}" → スコア）。後方互換：読み手はキー無しを空扱い。
        var bs = new Godot.Collections.Dictionary();
        foreach (var kv in BestScores)
            bs[kv.Key] = kv.Value;
        data["bestScores"] = bs;
        // 中ボス撃破フラグ（ステージID配列）。後方互換：キー無し＝空扱い。
        var mb = new Godot.Collections.Array();
        foreach (var id in _midBossCleared)
            mb.Add(id);
        data["midBossCleared"] = mb;
        // 初回ショップ説明の既読フラグ。後方互換：キー無し＝false 扱い。
        data["shopTutorialSeen"] = ShopTutorialSeen;
        data["endingRollSeen"] = EndingRollSeen;
        // ステージ進行（クリア済みステージID＝救った人数・解放・到達度の本体）。後方互換：キー無し＝空。
        var cl = new Godot.Collections.Array();
        foreach (var id in _cleared)
            cl.Add(id);
        data["cleared"] = cl;
        data["pendingUnlockCards"] = _pendingUnlockCards.ToArray();
        // 炎上ストーリーイベントの状態（既発生か／次ダイブ適用待ちか）。後方互換：キー無し＝false。
        data["burnHappened"] = _burnHappened;
        data["burning"] = Burning;
        // 旧「会話選択（層2プロト）」の "pressedQ" はもう書かない（こはる面の2択ごと撤去＝2026-09-24）。
        //   旧セーブに残っていても読む側が居ない＝未知キーとして無視されるだけ。次の保存で自然に消える。
        // 仕掛けの値（散った言葉ほか）。いずれも後方互換：キー無し＝既定値（空配列／空文字／0）。
        var sw = new Godot.Collections.Array();
        foreach (var w in ScatteredWords)
            sw.Add(w);
        data["scatteredWords"] = sw;
        data["firstScattered"] = FirstScattered;
        data["nameRoute"] = NameRoute;
        data["lastSentWord"] = LastSentWord;
        data["hesitationSec"] = HesitationSec;
        // 選択IDごとの台帳（F1 の S3-7 分岐・E6 の P2 秒数比較が参照）。後方互換：キー無し＝空辞書。
        var ch = new Godot.Collections.Dictionary();
        foreach (var kv in _chosenById)
            ch[kv.Key] = kv.Value;
        data["chosenById"] = ch;
        var hz = new Godot.Collections.Dictionary();
        foreach (var kv in _hesitationById)
            hz[kv.Key] = kv.Value;
        data["hesitationById"] = hz;
        // ハブ再訪小話の既読キー集合。後方互換：キー無し＝空扱い。
        var ids = new Godot.Collections.Array();
        foreach (var key in _idleDialogSeen)
            ids.Add(key);
        data["idleDialogSeen"] = ids;
        data["phoneWallpaperPhotoId"] = PhoneWallpaperPhotoId;
        data["likedPostIds"] = _likedPostIds.ToArray();
        data["sentReplies"] = _sentReplies;
        data["limeMessages"] = SaveLimeThread(LimeHistory);
        var direct = new Godot.Collections.Dictionary();
        foreach (var (id, thread) in _limeDirect)
            if (thread.Count > 0) direct[id] = SaveLimeThread(thread);
        data["limeDirect"] = direct;
        data["repostedPostIds"] = _repostedPostIds.ToArray();
        var cd = new Godot.Collections.Dictionary();
        foreach (var kv in _charDives)
            cd[kv.Key] = kv.Value;
        data["charDives"] = cd;
        var companionClears = new Godot.Collections.Array();
        foreach (string id in _companionClears) companionClears.Add(id);
        data["companionClears"] = companionClears;

        using var f = FileAccess.Open(SlotPath(slot), FileAccess.ModeFlags.Write);
        if (f == null) return false;
        f.StoreString(Json.Stringify(data));
        f.Flush();
        if (f.GetError() != Error.Ok) return false;
        if (settleStage) SettleStageScore();
        return true;
    }

    private void LoadUpgrades(Godot.Collections.Dictionary data)
    {
        foreach (var owned in _ownedUpgrades.Values) owned.Clear();
        MigratedNodeCount = -1;
        if (data.TryGetValue("upgradesByCharacter", out var byCharacter))
        {
            var purchases = byCharacter.AsGodotDictionary();
            foreach (var job in Jobs.All)
                if (purchases.TryGetValue(job.CharacterId, out var ids))
                    _ownedUpgrades[job.Id].UnionWith(ids.AsStringArray().Where(id => GetUpgradeDef(id) != null));
            return;
        }

        // 旧版の強化は全員に効いていた。払い戻しは一度だけ計算してから、所持分を各人に引き継ぐ。
        var shared = new Dictionary<string, int>();
        if (data.TryGetValue("upgrades", out var legacy))
            foreach (var kv in legacy.AsGodotDictionary()) shared[kv.Key.AsString()] = kv.Value.AsInt32();
        MigrateSharedUpgrades(shared);
        if (data.TryGetValue("hasDodge", out var dodge) && dodge.AsBool()) shared["n_dodge"] = 1;
        if (shared.GetValueOrDefault("n_charge") > 0) shared["n_charge_shot"] = 1;
        foreach (var owned in _ownedUpgrades.Values)
            foreach (var kv in shared)
                if (kv.Value > 0) owned.Add(kv.Key);
    }

    public bool LoadFromSlot(int slot)
    {
        string path = SlotPath(slot);
        if (!FileAccess.FileExists(path)) return false;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return false;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok) return false;
        if (json.Data.VariantType != Variant.Type.Dictionary) return false;
        var data = json.Data.AsGodotDictionary();
        double playTime = data.ContainsKey("playTimeSeconds") ? data["playTimeSeconds"].AsDouble() : 0;
        _stageRewardPending = false;
        _bossRetryScore = 0;
        _bossRetryElapsed = 0;
        PlayTimeSeconds = double.IsFinite(playTime) ? System.Math.Max(0, playTime) : 0;
        _playTimePartial = !data.ContainsKey("playTimeSeconds") || data.ContainsKey("playTimePartial") && data["playTimePartial"].AsBool();
        Difficulty = data.ContainsKey("difficulty") ? (Diff)Mathf.Clamp(data["difficulty"].AsInt32(), 0, 3) : Diff.Normal;
        LoadCosmetics(data);

        Impression = data.ContainsKey("impression") ? data["impression"].AsInt64() : 0;
        Followers = data.ContainsKey("followers") ? data["followers"].AsInt32() : 0;
        LoadUpgrades(data);
        // クリアタイム復元（キー無し＝旧セーブは空のまま＝後方互換）。
        ClearTimes.Clear();
        if (data.ContainsKey("clearTimes"))
        {
            var ct = data["clearTimes"].AsGodotDictionary();
            foreach (var k in ct.Keys)
                ClearTimes[k.AsString()] = (float)ct[k].AsDouble();
        }
        // ベストスコア復元（キー無し＝旧セーブは空のまま＝後方互換）。
        BestScores.Clear();
        if (data.ContainsKey("bestScores"))
        {
            var bs = data["bestScores"].AsGodotDictionary();
            foreach (var k in bs.Keys)
                BestScores[k.AsString()] = bs[k].AsInt64();
        }
        // 中ボス撃破フラグ復元（キー無し＝旧セーブは空のまま＝後方互換）。
        _midBossCleared.Clear();
        if (data.ContainsKey("midBossCleared"))
        {
            var mb = data["midBossCleared"].AsGodotArray();
            foreach (var v in mb)
                _midBossCleared.Add(v.AsString());
        }
        // 初回ショップ説明の既読（キー無し＝旧セーブは false＝後方互換）。
        ShopTutorialSeen = data.ContainsKey("shopTutorialSeen") && data["shopTutorialSeen"].AsBool();
        EndingRollSeen = data.ContainsKey("endingRollSeen") && data["endingRollSeen"].AsBool();
        // ステージ進行（クリア済み）復元。キー無し＝旧セーブは空＝後方互換。
        _cleared.Clear();
        if (data.ContainsKey("cleared"))
        {
            var cl = data["cleared"].AsGodotArray();
            foreach (var v in cl)
                _cleared.Add(v.AsString());
        }
        _pendingUnlockCards.Clear();
        if (data.TryGetValue("pendingUnlockCards", out var pendingCards))
            _pendingUnlockCards.AddRange(pendingCards.AsStringArray().Where(id => _cleared.Contains(id) && Jobs.UnlockedBy(id) != null).Distinct());
        else if (data.TryGetValue("akariUnlockCardPending", out var oldPending) && oldPending.AsBool() && IsStageCleared(FirstStageId))
            _pendingUnlockCards.Add(FirstStageId);
        // 炎上イベント状態復元（キー無し＝false）。
        _burnHappened = data.ContainsKey("burnHappened") && data["burnHappened"].AsBool();
        Burning = data.ContainsKey("burning") && data["burning"].AsBool();
        // 旧 "pressedQ"（会話選択・層2プロト）は読まない＝ここは意図的な読み捨て。読み込みはキー単位なので、
        //   旧セーブに残っていても素通りする（辞書の未知キーで例外にはならない）＝旧セーブはそのまま遊べる。
        // 仕掛けの値の復元（キー無し＝旧セーブは既定値のまま＝後方互換）。
        //   散った語の取り消し台帳（_scatterById）はランを跨いで持たない＝
        //   ロード直後の RecordChoice は「その id の初回」として素直に積まれる。
        //   選んだ言葉／迷い秒数の台帳は復元する（F1 の S3-7 分岐・E6 の P2 秒数比較が
        //   セーブから再開しても効くように）。_hesitationById は HesitationSec の合計と
        //   対で復元されるので、同じ id を選び直したときの取り消しも正しく効く。
        ScatteredWords.Clear();
        _scatterById.Clear();
        _hesitationById.Clear();
        _chosenById.Clear();
        if (data.ContainsKey("chosenById"))
        {
            var ch = data["chosenById"].AsGodotDictionary();
            foreach (var k in ch.Keys)
                _chosenById[k.AsString()] = ch[k].AsString();
        }
        if (data.ContainsKey("hesitationById"))
        {
            var hz = data["hesitationById"].AsGodotDictionary();
            foreach (var k in hz.Keys)
                _hesitationById[k.AsString()] = hz[k].AsSingle();
        }
        if (data.ContainsKey("scatteredWords"))
        {
            var sw = data["scatteredWords"].AsGodotArray();
            foreach (var v in sw)
                ScatteredWords.Add(v.AsString());
        }
        FirstScattered = data.ContainsKey("firstScattered") ? data["firstScattered"].AsString() : "";
        NameRoute = data.ContainsKey("nameRoute") ? Mathf.Clamp(data["nameRoute"].AsInt32(), 0, 2) : 0;
        LastSentWord = data.ContainsKey("lastSentWord") ? data["lastSentWord"].AsString() : "";
        HesitationSec = data.ContainsKey("hesitationSec") ? data["hesitationSec"].AsSingle() : 0f;
        // ハブ再訪小話の既読キー復元（キー無し＝旧セーブは空＝後方互換）。
        _idleDialogSeen.Clear();
        if (data.ContainsKey("idleDialogSeen"))
        {
            var ids = data["idleDialogSeen"].AsGodotArray();
            foreach (var v in ids)
                _idleDialogSeen.Add(v.AsString());
        }
        PhoneWallpaperPhotoId = data.ContainsKey("phoneWallpaperPhotoId") ? data["phoneWallpaperPhotoId"].AsString() : "";
        _likedPostIds.Clear();
        if (data.ContainsKey("likedPostIds")) _likedPostIds.UnionWith(data["likedPostIds"].AsStringArray());
        _sentReplies = data.TryGetValue("sentReplies", out var replies) ? replies.AsGodotDictionary() : new();
        LimeHistory.Clear();
        if (data.TryGetValue("limeMessages", out var history)) LoadLimeThread(history, LimeHistory);
        _limeDirect.Clear();
        if (data.TryGetValue("limeDirect", out var direct) && direct.VariantType == Variant.Type.Dictionary)
            foreach (var pair in direct.AsGodotDictionary())
                LoadLimeThread(pair.Value, LimeThread(pair.Key.AsString()));
        _repostedPostIds.Clear();
        if (data.ContainsKey("repostedPostIds")) _repostedPostIds.AddRange(data["repostedPostIds"].AsStringArray().Distinct());
        // 他ジョブ潜行の章カウンタ復元（キー無し＝旧セーブは全員0回＝第1章から＝後方互換）。
        _charDives.Clear();
        if (data.ContainsKey("charDives"))
        {
            var cd = data["charDives"].AsGodotDictionary();
            foreach (var k in cd.Keys)
                _charDives[k.AsString()] = cd[k].AsInt32();
        }
        _companionClears.Clear();
        if (data.ContainsKey("companionClears"))
            foreach (var id in data["companionClears"].AsGodotArray())
                _companionClears.Add(id.AsString());
        // ジョブの復元（2026-09-13）。モードはジョブが決めるので、ここが唯一の入口になる。
        //   ・"job" があればそれを採用（範囲外は結び手へクランプ）。
        //   ・"job" が無い＝ジョブ導入前の既存セーブ。壊さずに読むため、保存されていた "shotmode" から
        //     対応するジョブを逆引きする（拡散→語り手／ホーミング→祈り手／加速球→灯し手／連射→結び手）。
        //     "shotmode" も無ければ結び手（初期選択）。
        //   ・--job=xxx で固定中（JobForcedByCmdline）はセーブに上書きさせない＝QA走行の指定を守る。
        if (!JobForcedByCmdline)
        {
            if (data.ContainsKey("job"))
            {
                int jv = data["job"].AsInt32();
                SelectedJob = System.Enum.IsDefined(typeof(Job), jv) ? (Job)jv : Job.Tank;
            }
            else
            {
                var m = data.ContainsKey("shotmode")
                    ? (ShotMode)Mathf.Clamp(data["shotmode"].AsInt32(), 0, 3)
                    : ShotMode.Rapid;
                SelectedJob = m switch
                {
                    ShotMode.Spread => Job.Magic,
                    ShotMode.Homing => Job.Heal,
                    ShotMode.Accel => Job.Melee,
                    _ => Job.Tank,
                };
            }
            // 解禁制（2026-09-14）の保険：クリア記録と食い違うジョブが入っていたら結び手へ落とす。
            //   ジョブ導入後〜解禁制の前に保存した「まだ誰も救っていないのに語り手」のデータや、
            //   旧セーブのモード逆引き（拡散→語り手）がここに当たる。_cleared 復元より後に置くこと。
            if (!IsJobUnlocked(SelectedJob)) SelectedJob = Job.Tank;
        }
        JustClearedStageWasNew = false;
        SteamAchievements.RecordCharacterUnlocks(this);
        return true;
    }

    // はじめから＝メモリ上の永続状態を初期化（スロットのファイルは消さない）。
    public void ResetPersistent()
    {
        JustClearedStageWasNew = false;
        _stageRewardPending = false;
        PlayTimeSeconds = 0;
        _playTimePartial = false;
        ResetCosmetics();
        Impression = 0;
        Followers = 0;
        foreach (var owned in _ownedUpgrades.Values) owned.Clear();
        MigratedNodeCount = -1;
        // ジョブも初期選択（結び手）へ。SelectedShotMode はセッタが連射へ同期する。
        //   --job=xxx で固定中は「はじめから」でも指定を守る（QA走行で新規データを作る経路を壊さない）。
        if (!JobForcedByCmdline) SelectedJob = Job.Tank;
        _midBossCleared.Clear();
        ShopTutorialSeen = false;
        EndingRollSeen = false;
        SelectedEntry = StageEntry.Start;
        _bossRetryScore = 0;
        _bossRetryElapsed = 0;
        _cleared.Clear();          // ステージ進行（クリア済み）も初期化＝救った人数0から
        _pendingUnlockCards.Clear();
        _companionClears.Clear();
        _burnHappened = false; Burning = false; BurningThisRun = false;
        // 仕掛けの値も初期化（散った言葉が前データから残ると F4/E2 で他人の言葉が戻ってくる）。
        ScatteredWords.Clear(); _scatterById.Clear(); _hesitationById.Clear(); _chosenById.Clear();
        FirstScattered = ""; NameRoute = 0; LastSentWord = ""; HesitationSec = 0f;
        _idleDialogSeen.Clear();   // ハブ再訪小話の既読も初期化
        PhoneWallpaperPhotoId = "";
        _likedPostIds.Clear();
        _sentReplies.Clear();
        LimeHistory.Clear();
        _limeDirect.Clear();
        _repostedPostIds.Clear();
        _charDives.Clear();        // 他ジョブ潜行の章カウンタも初期化＝新規データは全員第1章から
        // 汚染は物語の背骨でシーンをまたいで持ち越すぶん、ここで戻さないと FINAL/Final で 1.0 にした値のまま
        //   新規データのハブ／プロローグへ入り、murk・自機の濁りが濁ったまま描かれる。
        Contamination = 0f;
        // クリアタイムを消さないと、まっさらなはずの新規データに前データのベストが残り、
        //   記録画面とハブカードの BEST に出続けたうえ、最初のオートセーブで新スロットへ焼き付く。
        ClearTimes.Clear();
        // ベストスコアも同様に消す（クリアタイムと同じ理由・同じ扱い）。
        BestScores.Clear();
    }

    // オートセーブ：専用オートスロット(=0)に書く。手動スロット(1..3)は汚さない。
    // 設定でON/OFF（既定ON）。クリア・Hub帰還・タイトルへ戻る時などのマイルストーンで呼ぶ。
    public bool AutoSaveEnabled { get; set; } = true;
    public void AutoSave() { if (AutoSaveEnabled) SaveToSlot(0); }

    // ───────────────────────────────────────────────────────────
    // 端末ローカル prefs（user://prefs.json）。スロットセーブ（経済/強化）とは独立。
    // チュートリアル既読フラグなど「この端末で一度きり」の状態を保存する。
    // ───────────────────────────────────────────────────────────
    private static string PrefsPath => Startup.DataPath("prefs.json");

    // 練習面（ステージ0「れんしゅう」）を出すか。2026-09-06 ユーザー指示で非表示。true に戻せば復活。
    //   false の間は：プロローグ後の受講確認を出さずハブへ直行／タイトルの「チュートリアル」項目を隠す。
    //   Stage0.tscn・StageZero・Stage0Root は残してあるので、true にすれば元の導線がそのまま戻る。
    public const bool TutorialEnabled = false;

    // チュートリアル既読（端末ローカル）。初回プレイ判定に使う。
    //   非表示中（TutorialEnabled==false）は「受講済み」とみなす＝初回判定に引っかかって進行が詰まらない。
    public bool TutorialSeen { get => !TutorialEnabled || _tutorialSeen; private set => _tutorialSeen = value; }
    private bool _tutorialSeen;

    // チュートリアル（ステージ0）の練習モード：ON の間はボム・残機を消費しない（詰み防止）。
    // 非セーブ＝ラン単位。Stage0Root の _Ready で立て、Hub 遷移時に倒す。
    public bool TutorialNoConsume;

    private void LoadPrefs()
    {
        if (!FileAccess.FileExists(PrefsPath)) return;
        using var f = FileAccess.Open(PrefsPath, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok) return;
        if (json.Data.VariantType != Variant.Type.Dictionary) return;
        var data = json.Data.AsGodotDictionary();
        if (data.ContainsKey("tutorialSeen")) TutorialSeen = data["tutorialSeen"].AsBool();
    }

    private void SavePrefs()
    {
        // 保存するのは実際に受講したかどうか（_tutorialSeen）＝非表示中の「既読とみなす」で prefs を汚さない。
        var data = new Godot.Collections.Dictionary { ["tutorialSeen"] = _tutorialSeen };
        using var f = FileAccess.Open(PrefsPath, FileAccess.ModeFlags.Write);
        if (f != null) f.StoreString(Json.Stringify(data));
    }

    // チュートリアルを既読にして prefs へ保存（ステージクリア完了時に呼ぶ）。
    public void MarkTutorialSeen()
    {
        if (TutorialSeen) return;
        TutorialSeen = true;
        SavePrefs();
    }

    // ───────────────────────────────────────────────────────────
    // 会話の既読ログ（user://read.json・全スロット共有＝端末ローカル）— Epic G #22
    //   2周目の「既読スキップ（押しっぱなし高速送り）」の判定に使う。粒度は会話1行。
    //   キーは行テキストの FNV-1a 64bit ハッシュ（同一テキスト＝同一行とみなす最小キー。
    //   全文を保存しないのでファイルが太らない）。スロット非依存にする理由：既読は
    //   「プレイヤーがその文章を読んだか」であってセーブデータの進行ではない＝
    //   スロットを替えても・はじめからでも、読んだ話は読んだ話（周回に親切）。
    //   ResetPersistent（はじめから）でも消さない。
    // ───────────────────────────────────────────────────────────
    private static string ReadLogPath => Startup.DataPath("read.json");
    private readonly HashSet<string> _readLines = new();

    // この行テキストは表示済み（既読）か。空文字は常に未読扱い。
    public bool IsLineRead(string text)
        => !string.IsNullOrEmpty(text) && _readLines.Contains(LineHash(text));

    // 行を既読として記録。新規のときだけファイルへ書く（既読行の再表示では I/O しない）。
    public void MarkLineRead(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (_readLines.Add(LineHash(text))) SaveReadLog();
    }

    // FNV-1a 64bit ハッシュ → 16桁hex。行テキストの固定長キー化。
    private static string LineHash(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in text) { h ^= c; h *= 1099511628211UL; }
        return h.ToString("x16");
    }

    private void LoadReadLog()
    {
        if (!FileAccess.FileExists(ReadLogPath)) return;
        using var f = FileAccess.Open(ReadLogPath, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok) return;
        if (json.Data.VariantType != Variant.Type.Array) return;
        foreach (var v in json.Data.AsGodotArray())
            _readLines.Add(v.AsString());
    }

    private void SaveReadLog()
    {
        var arr = new Godot.Collections.Array();
        foreach (var k in _readLines)
            arr.Add(k);
        using var f = FileAccess.Open(ReadLogPath, FileAccess.ModeFlags.Write);
        if (f != null) f.StoreString(Json.Stringify(arr));
    }

    private const int MaxComboMultiplier = 16;
    private int ComboMultiplier => Mathf.Clamp(Combo, 1, MaxComboMultiplier);

    public override void _Ready()
    {
        AddChild(new PlayClock { Name = "PlayClock", Game = this, ProcessMode = ProcessModeEnum.Pausable });
        // セーブはスロット制（手動）。起動時は自動ロードしない。
        // 端末ローカル prefs（チュートリアル既読など）と会話の既読ログだけは起動時に読む。
        LoadPrefs();
        LoadReadLog();
        Settings.ApplySaved(this);
        GetWindow().SizeChanged += ApplyCursor;
        ApplyCursor();

        // --job=melee|heal|tank|magic : このランのジョブを強制する（ハブの選択画面ができるまでの入口、
        //   かつ以後も残すデバッグ機能）。立てると JobForcedByCmdline が立ち、セーブのロードや
        //   「はじめから」でも上書きされない＝QA走行で指定したジョブのまま最後まで走れる。
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--job=")) continue;
            var j = Jobs.Parse(a.Substring(6));
            if (j.HasValue)
            {
                SelectedJob = j.Value;
                JobForcedByCmdline = true;
                GD.Print($"[JOB] forced by cmdline: {JobDef.CharacterName}({j.Value}) mode={ShotModeName(SelectedShotMode)}");
            }
            else GD.PushWarning($"[JOB] unknown --job value: {a}");
            break;
        }

        // --loadslot=N : 起動時にスロット N を読む（検証専用。通常は起動時ロードしない方針＝手動のみ）。
        //   ジョブ導入後、「ジョブ欄の無い既存セーブを読んでも壊れない」ことをヘッドレスで確かめる口として置く。
        //   --job= より後に処理する＝JobForcedByCmdline が立っていればロードはジョブを上書きしない。
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--loadslot=")) continue;
            if (int.TryParse(a.Substring(11), out int slot))
            {
                bool ok = LoadFromSlot(slot);
                GD.Print($"[SAVE] --loadslot={slot} -> {(ok ? "ok" : "FAILED/absent")} "
                       + $"imp={Impression} fol={Followers} upgrades={OwnedUpgradeCount} "
                       + $"job={JobDef.CharacterName}({SelectedJob}) mode={ShotModeName(SelectedShotMode)} lives={StartLives} "
                       // 一本道14段の移行と、回避（旧 "hasDodge" → n_dodge）が正しく引き継がれたかも見る。
                       + $"dodge={HasDodge} column=[{string.Join(",", ColumnOwnedIds())}]");
            }
            break;
        }

        // 検証専用：--seed-records でダミーのクリアタイムをメモリに注入（記録画面/カードの確認用）。
        // セーブには一切書かない（手動セーブしない限り消える）＝本番フロー/既存スロットを汚さない。
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--seed-records") { SeedDebugRecords(); break; }

        // [一時/デバッグ] --boss : チェックポイント入口を「ボスから」にして各ステージをボス戦開始にする。
        // Stage は入口を読んだら消費する（リトライが前回の入口を引きずらないため）ので、恒久フラグを別に持ち、
        //   毎ステージの _Ready で入口を貼り直す＝「毎回ボスから」というデバッグ用途を保つ。
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a == "--boss") { DebugAlwaysBoss = true; SelectedEntry = StageEntry.Boss; }
            if (a == "--choice") DebugChoiceNow = true;
            if (a == "--input-field") DebugInputField = true;
            if (a == "--debug-guides") DebugAttackGuides = true;
        }

        // [一時/デバッグ] --fury / --fury-rate : 【激情】メーターの初期値と上昇速度を直に指定する
        //   （端の挙動を道中を踏まずに確かめる口）。実体は FuryMeter.cs の partial 側。
        ParseFuryDebugArgs();
    }

    // --boss 起動中か（消費される SelectedEntry と違い、ランを通して残る）。
    public bool DebugAlwaysBoss { get; private set; }

    public bool DebugChoiceNow { get; private set; }

    // [一時/デバッグ] --input-field : こはる面を S2-4「入力欄」（StageKoharu の step 8）から始める。
    // コメント欄UI（CommentInput）の見た目確認・スクショ用。道中も中ボスも踏まない＝数秒で欄に着く。
    public bool DebugInputField { get; private set; }

    // 検証用ダミー記録。リリースには影響しない（--seed-records 起動時のみ呼ばれる）。
    private void SeedDebugRecords()
    {
        void Put(string id, Diff d, float s) => ClearTimes[ClearTimeKey(id, d)] = s;
        Put("rei", Diff.Easy, 95.40f);   Put("rei", Diff.Normal, 83.12f);  Put("rei", Diff.Hard, 78.55f);
        Put("akari", Diff.Normal, 102.30f); Put("akari", Diff.Hard, 99.80f);
        Put("koharu", Diff.Easy, 121.00f);  Put("koharu", Diff.Lunatic, 140.67f);
        Put("final", Diff.Normal, 156.25f);
        void PutScore(string id, Diff d, long s) => BestScores[ClearTimeKey(id, d)] = s;
        PutScore("rei", Diff.Easy, 42800);   PutScore("rei", Diff.Normal, 61250);  PutScore("rei", Diff.Hard, 73900);
        PutScore("akari", Diff.Normal, 88400); PutScore("akari", Diff.Hard, 95120);
        PutScore("koharu", Diff.Easy, 51000);  PutScore("koharu", Diff.Lunatic, 132600);
        PutScore("final", Diff.Normal, 210500);
        // 記録画面は未クリアの面の名前を伏せる（2026-09-07）。記録だけ入って名前が「???」のままだと
        //   検証用の状態として噛み合わないので、ダミー記録を入れた面はクリア済みにも印を付ける。
        //   メモリ上だけ＝セーブには書かない（手動セーブしない限り消える）のは従来どおり。
        foreach (var s in Stages) _cleared.Add(s.Id);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key
            || (key.Keycode != Key.G && key.PhysicalKeycode != Key.G)
            || key.CtrlPressed || key.AltPressed || key.MetaPressed
            || GetTree().Paused || Hud.BubblePaused || Pad.UiBlocked(this)
            || GetTree().GetFirstNodeInGroup("player") == null
            || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        DebugAttackGuides = !DebugAttackGuides;
        GD.Print($"[DEBUG] attack guides: {(DebugAttackGuides ? "ON" : "OFF")}");
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        // 【激情】の自然上昇（ボス戦中のみ／会話・割り込み中は止まる）。実体は FuryMeter.cs。
        TickFury(delta);
    }

    // 道中カメオ（ミニボス）をHP削り切りで撃破した時の報酬。CameoBoss.OnCryStart から1回だけ呼ぶ。
    // やさしさゲージ撤去（2026-09-06 / docs/20260906/HUD整理_案.md §5）に伴い、旧「やさしさ +0.6」の
    // ぶんはスコアへ寄せた（CameoScoreReward 900→2000）。インプレは従来どおり付けない。
    public void RewardCameoDefeat()
    {
        Score += CameoScoreReward;         // 中ボス撃破の主報酬

        // 難易度緩和：中ボス撃破で BOMB+1 と ♥+1 を回復する（どちらも上限でキャップ＝超えない）。
        //   ・ボム上限＝初期ボム数(StartBombs＝難易度＋ボム所持強化)。既に上限なら増やさない。
        //   ・♥上限＝初期残機(StartLives＝難易度＋最大♥強化)。回復は Player.AddLife がキャップする。
        // 回復できた分だけ控えめにバナーで知らせる（やり過ぎない／何も増えなければ黙る）。
        bool gotBomb = false;
        if (Bombs < StartBombs) { Bombs = Mathf.Min(StartBombs, Bombs + 1); gotBomb = true; }
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        bool gotLife = player?.AddLife(1) ?? false;
        if (gotBomb || gotLife)
        {
            // ★2026-09-17：「♥ +1」のベタ文字をやめ、サイドパネルの LIFE/BOMB 列と同じ絵
            //   （そのキャラの核マーク／ボム印）＋"+1" のアイコン表記で返す（Hud.ShowRewardBanner）。
            //   ♥は4キャラ共通の記号で「誰の何が増えたか」が伝わらないのが差し替えの理由。
            var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
            hud?.ShowRewardBanner(gotLife, gotBomb);
        }
    }
    // 900→2000：やさしさ +0.6（直後のボス戦で全開を撃てる下準備）を失ったぶんの置き換え。
    private const int CameoScoreReward = 2000;

    // ボム1発の撃破報酬には上限があるが、コンボには倒した全員を数える。
    private const int BombPurifyRewardCap = 3;
    private int _bombPurifyCount; // 現在のボム1発で報酬を付けた体数（UseBomb でリセット）
    internal int BombPurifiedCount => _bombPurifyCount;

    public void AddBossHit(int damage)
    {
        if (damage <= 0) return;
        Score += (long)damage * ComboMultiplier;
    }

    // 敵を浄化（撃破）した時の加点。コンボ倍率がかかる。
    // fromBomb=true はボムの強制浄化経路（Enemy.Purify）。上限を超えた分は撃破時のスコアを加算しない。
    public bool AddPurify(int basePoints, bool fromBomb = false)
    {
        Combo++;
        bool rewarded = !fromBomb || ++_bombPurifyCount <= BombPurifyRewardCap;
        if (rewarded)
            Score += basePoints * ComboMultiplier;
        PurifiedCount++;
        TickPurifyDrain();
        return rewarded;
    }

    public void AddScoreShard(int points) => Score += points;

    // ───── 祈り手（Heal）の浄化ドレイン（設計書 §2）─────
    //   雑魚を DrainPerLife 体（=24）浄化するごとに ♥+1。ボム由来の浄化も数える
    //   （ボムは既にボムキャップで報酬が絞られており、ここまで塞ぐと回復の入口が細くなりすぎる）。
    //   ★満タン時はカウンタを進めない：「満タンのうちに貯めておいて、削られてから一気に戻す」
    //     という最適化を潰すため（設計書の明示要求）。AddLife が false を返した＝増えなかった場合も
    //     同じ扱いにすると「上限で捨てた1回」を数えたことになるので、先に満タン判定で弾く。
    private int _purifyDrain;
    private void TickPurifyDrain()
    {
        int need = JobDef.DrainPerLife;
        if (need <= 0) return;
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player == null) return;
        if (player.Lives >= player.MaxLives) return;   // 満タン＝カウンタを進めない
        if (++_purifyDrain < need) return;
        _purifyDrain = 0;
        if (player.AddLife(1))
        {
            (GetTree().GetFirstNodeInGroup("hud") as Hud)?.ShowRewardBanner(life: true, bomb: false);
            GD.Print($"[JOB] heal drain: ♥+1 (every {need} purified, lives={player.Lives}/{StartLives})");
        }
    }

    // ───── 祈り手（Heal）の BREAK 報酬（設計書 §2）─────
    //   盾を剥がし切って BREAK が成立するたび BOMB+1。上限（StartBombs）は超えない＝
    //   中ボス撃破報酬（RewardCameoDefeat）と同じ作法。他ジョブでは何も起きない。
    public void NotifyBossBreak()
    {
        if (!JobDef.BombOnBreak) return;
        if (Bombs >= StartBombs) return;
        Bombs = Mathf.Min(StartBombs, Bombs + 1);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.ShowRewardBanner(life: false, bomb: true);
        GD.Print($"[JOB] heal break: BOMB+1 (bombs={Bombs}/{StartBombs})");
    }

    // 敵弾をかすった（グレイズ）時の加点。
    //   farming対策：被弾直後の無敵中(_hitInvincible)は呼び出し元(Player.OnGrazeAreaEntered)で既にスキップ済み。
    public void AddGraze()
    {
        Score += 30;
        GrazeCount++;
    }

    public void AddDodgeGraze() => Score += 50;

    // ボムで敵弾を消した時の小加点。
    public void AddBulletCleared()
    {
        Score += 5;
    }

    // ボムを使う。残があれば消費して true。
    // チュートリアル練習モード中は残数を減らさず発動成功を返す（詰み防止＝何度でも練習できる）。
    public bool UseBomb()
    {
        if (!TutorialNoConsume)
        {
            if (Bombs <= 0)
                return false;
            Bombs--;
        }
        // 発動が確定した時点でボム1発ぶんの報酬台帳をリセット（窓キャップを EnterExposed で 0 に戻すのと同じ作法）。
        _bombPurifyCount = 0;
        return true;
    }

    // ラン開始時のリセット。※インプレ/フォロワー/強化は恒久なので消さない（§0-3）。
    public double ResetRun()
    {
        _stageRewardPending = false;
        Score = _bossRetryScore;
        _bossRetryScore = 0;
        double elapsed = SelectedEntry == StageEntry.Boss ? _bossRetryElapsed : 0;
        _bossRetryElapsed = 0;
        Combo = 0;
        Bombs = StartBombs;
        _bombPurifyCount = 0;
        PurifiedCount = 0;
        _purifyDrain = 0;     // 祈り手の浄化ドレインもラン単位（面を跨いで持ち越さない）
        RunHitCount = 0;      // 被弾回数（ハブ帰還の「被弾は{n}回」）もラン単位
        _progAccum = 0f;      // 前のめり進行アキュムレータもラン開始でリセット
        PlayerNormX = 0.5f;   // 自機Xは中央からとみなす（初フレーム前の背景/HUD 参照用）
        CancelSpecial();
        _specialCooldown = 0f;
        RedemptionActive = false;
        _stageHasBoss = _stageRoadFinished = false;
        BossReached = false;  // ボス到達もラン単位（「ボスから」で再読込した直後は Step_BossSpawn が立て直す）
        return elapsed;
    }

    // ゲームオーバーからの「ボスからやり直す」を予約する（スコア半分を次のランへ持ち越し、入口をボスへ）。
    //   戻り値 false ＝今ランでボス戦に到達していない（BossReached が立っていない）ので予約しない
    //   ＝呼び元の再読込はそのまま「最初から」（スコア 0・入口 Start）になる。
    //   2026-09-23 ユーザー報告「ボスまでいってないのにボスからやり直しができる」：従来は無条件に
    //   SelectedEntry を Boss にしていたので、道中で倒れても未到達のボス戦へ飛べていた。
    public bool PrepareBossRetry(double elapsed, bool bossCheckpoint = true)
    {
        if (!BossReached) return false;
        // Scene reload calls ResetRun; carry the remaining half into that reset exactly once.
        _bossRetryScore = Score / 2;
        _bossRetryElapsed = bossCheckpoint ? elapsed : 0;
        SelectedEntry = bossCheckpoint ? StageEntry.Boss : StageEntry.Start;
        return true;
    }

    // 今ランでボス戦に到達したか（ラン単位・非セーブ）。各 Stage の Step_BossSpawn（本ボス出現）が
    //   NotifyBossReached で立て、次のラン開始（ResetRun）で下りる。中ボス(cameo)では立てない。
    //   ゲームオーバーの「ボスから」（選択肢／R 単体）はこれが true のときだけ出す＝到達していない
    //   ボスからは再開できない。セーブの記録（過去にクリアした／中ボスを倒した）は見ない。
    //   「ボスから」で再読込すると ResetRun が下ろす → _step=Boss の Step_BossSpawn が最初のフレームで
    //   立て直すので、ボス戦で倒れ続けても選択肢は消えない。
    public bool BossReached { get; private set; }
    public void NotifyBossReached() => BossReached = true;

    // 改心演出中か。本戦ボスの OnCryStart が立て、次のラン開始（ResetRun）で下りる。
    // 残機0と同フレーム帯で飛翔中の弾がボスを浄化したエッジケースで、ゲームオーバーの
    // 「R/Q」抜けプロンプトを改心演出〜帰還会話に重ねないための判定（HandleGameOverExit が参照）。
    public bool RedemptionActive { get; private set; }
    public void NotifyRedemptionStart()
    {
        if (!BossRushActive) RedemptionActive = true;
    }

    // ───────────────────────────────────────────────────────────
    // ゲームオーバー時の選択（やり直す／最初から／抜ける）共通処理。
    // 各 *Root.cs が _Process で残機0を検知したら毎フレーム呼ぶ。
    //
    // 2026-09-07: 旧実装は「R：ボスからやり直す ／ Shift+R：最初から ／ Q：ステージから抜ける」を
    //   盤面の中央に**一行**で出すだけで、弾に埋もれて読めなかった（ユーザー実機指摘
    //   「ここも選択肢みたいに選べるようにして」）。会話の選択と同じ ChoiceOverlay に載せ替え、
    //   縦に積んで ↑↓/Z でもマウスのホバー＋クリックでも選べるようにした（onBoard:true＝盤面の中心）。
    //   既存のキー（R／Shift+R／Q／パッドB）は**そのまま残す**＝覚えている人が困らない。
    //   R の長押し／即発の扱いは従来どおり各 *Root.cs 側にある（こちらは触らない）。
    private static ChoiceOverlay? _gameOverChoice;
    // 立てた選択肢が GameOverChoices の何番目から始まるか（0＝「ボスから」あり／1＝ボス未到達で「最初から」始まり）。
    //   決定後の添字→行の対応に使う（出したときの条件で固定＝ゲームオーバー中に BossReached は変わらない）。
    private static int _gameOverFirst;
    // 選択肢の並び。沈黙の自動決定は末尾が選ばれるので、末尾は最も害の小さい「抜ける」にする
    //（ChoiceOverlay の既定挙動＝呼び出し側が引き下がる側を最後に置く約束）。
    // 先頭の「ボスから」は今ランでボス戦に到達しているときだけ出す（GameManager.BossReached）。
    private static readonly string[] GameOverChoices =
    {
        "（ボスから・スコア半分消費）",
        "（最初からやり直す）",
        "（ステージから抜ける）",
    };

    // 戻り値 true ＝画面遷移を実行した（呼び元はそれ以降の処理を打ち切ってよい）。
    public static bool HandleGameOverExit(Node root, Hud? hud, ref bool exitHeld, double elapsed = 0)
    {
        var game = root.GetNodeOrNull<GameManager>("/root/Game");

        // 改心演出が始まっていたら勝負は決着＝ゲームオーバー扱いを取り下げ、演出を優先する
        //（残機0と同フレーム帯で飛翔中の弾がボスを浄化したエッジケース。選択を重ねない）。
        // R リトライは各 *Root.cs の別経路で従来どおり有効。
        if (game?.RedemptionActive ?? false)
        {
            ClearGameOverChoice(hud);
            exitHeld = false;
            return false;
        }

        // 選択UIを一度だけ立てる。会話の選択と同じ見た目・同じ操作（盤面の中心・↑↓/Z・マウス）。
        if (_gameOverChoice == null || !IsInstanceValid(_gameOverChoice))
        {
            if (hud == null) return false;
            _gameOverFirst = !BossRushActive && (game?.BossReached ?? false) ? 0 : 1;
            _gameOverChoice = ChoiceOverlay.Show(hud, GameOverChoices[_gameOverFirst..],
                defaultSel: 0, onBoard: true);   // 既定は先頭＝いちばん続けやすい手（ボスから／未到達なら最初から）
            // ここで戦闘曲をゲームオーバー曲へ落とす（2026-09-14〜）。従来は**道中/ボス曲が鳴り続けていて**、
            //   「くじけちゃった…」の選択が音楽的に無句読点だった＝負けた実感が耳に来ない。
            //   旋律の無い静かなアンビエントへ 1.2 秒かけて渡し、場を鎮めて選択に集中させる。
            //   曲を止め切らずループさせるのは、選択の滞在時間が不定（プレイヤーが迷う）ため＝
            //   ジングル+無音だと無音が長引いて「音が死んだ」ように聞こえる（BGM/candidates.md ⑲）。
            //   復帰は各経路が握るので、ここでは戻さない：やり直しはどちらも ReloadCurrentScene() で
            //   *Root._Ready → BeginStageRun → SetStageMusic が道中/ボス曲を張り直し、
            //   抜けるは ExitToHub → Hub._Ready が BgmMenu を張る。
            Audio.Instance?.Music(Audio.Instance.BgmGameOver, 1.2f);
            // キー操作の案内は選択肢の下に小さく添える（覚えている人向け。選択UIの邪魔をしない量）。
            //   ボス未到達のときは R も「最初から」（PrepareBossRetry が予約せず素の再読込になる）＝案内も揃える。
            hud.ShowGameOverTitle("くじけちゃった…");
            hud.ShowGameOverPrompt(Pad.ShowKeyboard
                ? (_gameOverFirst == 0
                    ? "R：ボスからやり直す　／　Shift+R：最初から　／　Q：抜ける"
                    : "R：最初からやり直す　／　Q：抜ける")
                : $"{Pad.Face(JoyButton.B)}：抜ける");
        }

        // 既存キー：Q／パッドB＝抜ける（従来どおり即発）。R 系は各 *Root.cs が持っている。
        bool exit = Input.IsKeyPressed(Key.Q) || Pad.Pressed(JoyButton.B);
        bool fired = exit && !exitHeld;
        exitHeld = exit;
        if (fired) { ExitToHub(root, game, hud); return true; }

        // 選択が決まったら、その行の処理へ。
        if (!_gameOverChoice.Decided) return false;
        int sel = _gameOverChoice.Selected + _gameOverFirst;   // GameOverChoices の添字へ戻す（2択なら +1）
        ClearGameOverChoice(hud, restoreMusic: false);
        switch (sel)
        {
            case 0:   // ボスからやり直す＝R 単体と同じ経路（SelectedEntry を Boss にしてシーン再読込）
                      //   ボス未到達でここへは来ない（2択のとき sel は 1 から）。来ても PrepareBossRetry が弾く。
                      //   FINAL（MinaRoot）も 2026-09-27 からボス入口を持つ＝残響の三波（約70秒）を飛ばして
                      //   ミナ戦から再開する。入口を持たないのは練習（Stage0Root）だけで、そちらは本ボスが
                      //   居ないので BossReached が立たず、この行にも来ない。
                game?.PrepareBossRetry(elapsed, bossCheckpoint: root is AkariRoot or KoharuRoot or ReiRoot or MinaRoot);
                root.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                GameManager.FadeToScene(root, root.GetTree().CurrentScene.SceneFilePath);
                return true;
            case 1:   // 最初からやり直す＝Shift+R と同じ経路（SelectedEntry に触らない）
                root.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                GameManager.FadeToScene(root, root.GetTree().CurrentScene.SceneFilePath);
                return true;
            default:  // ステージから抜ける＝Q と同じ経路
                ExitToHub(root, game, hud);
                return true;
        }
    }

    // 選択UIと案内を片付ける（残機が戻った／改心に入った／選び終えた）。
    // *Root.cs は残機が0でないフレームに ShowGameOverPrompt("") を呼ぶので、そこからも消せるよう public。
    public static void ClearGameOverChoice(Hud? hud) => ClearGameOverChoice(hud, restoreMusic: true);

    // restoreMusic=false ＝「このあとシーンを変えるので曲は遷移先に任せる」呼び出し（ExitToHub）。
    private static void ClearGameOverChoice(Hud? hud, bool restoreMusic)
    {
        // 実際に選択UIが立っていたかを覚えてから消す（下の BGM 復帰の判定に使う）。
        //   *Root.cs は**ゲームオーバーでない毎フレーム**ここを呼ぶので、無条件に Music() を
        //   呼ぶと道中じゅう再生要求を出し続けることになる（Music() 自身は同一曲なら何もしないが、
        //   意図が読めない書き方になるため、立っていたときだけ戻す）。
        bool had = _gameOverChoice != null && IsInstanceValid(_gameOverChoice);
        if (had) _gameOverChoice!.QueueFree();
        _gameOverChoice = null;
        hud?.ShowGameOverTitle("");
        hud?.ShowGameOverPrompt("");

        // シーンを変えずに選択が引っ込んだ＝「残機が戻った／改心に入った」ケースだけ、
        //   ゲームオーバー曲から**そのステージの曲へ**戻す（2026-09-14〜）。
        //   やり直し／抜けるの3経路はいずれもシーンが変わり、遷移先の _Ready が曲を張るので
        //   ここでは戻さない（戻すと同フレーム帯で Music() が余分に1回走る）。
        //   ResumeStageMusic() は CurrentStageId から道中曲を引く＝ボス戦中に改心へ入った場合は
        //   直後に各ボスの PlayRedeem／OnCryEnd が上書きするので、ここは道中曲で正しい。
        if (restoreMusic && had && Audio.Instance != null && Audio.Instance.IsPlayingGameOver)
            Audio.Instance.ResumeStageMusic();
    }

    private static void ExitToHub(Node root, GameManager? game, Hud? hud)
    {
        // 直後に Hub へ遷移し Hub._Ready が BgmMenu を張るので、ここで道中曲へ戻さない
        //   （戻すと遷移の直前に一瞬だけ道中曲が立ち上がって無駄なクロスフェードになる）。
        ClearGameOverChoice(hud, restoreMusic: false);
        game?.SettleStageScore();
        game?.AutoSave();
        root.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        Audio.Instance?.PlayUiCancel();
        GameManager.FadeToScene(root, "res://Hub.tscn");
    }

    public static void FadeToScene(Node from, string dest) => LoadingScreen.Open(from, dest);
}
