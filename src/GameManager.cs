using Godot;
using System.Collections.Generic;

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
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public long Score { get; private set; }
    private long _bossRetryScore;
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
        set { _job = value; SelectedShotMode = Jobs.Get(value).Mode; }
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
    // ───── FINAL 初挑戦のミナ封じ（2026-09-17 ユーザー指示「ラスボスに初めて入るときはミナは使えなくしてほしい」）─────
    //   FINAL のボスはミナ本人（「穢れたわたし」）で、三人が救援に来る構成。そこへ自機もミナで入ると
    //   「結び手のミナが、ボスのミナを撃ち、三人に助けられる」という二重になる。初挑戦だけは
    //   あかり／こはる／レイ の誰かで潜る＝救援に来る三人の側に立つ。
    //   一度クリアすれば（IsFinalCleared）周回として結び手でも入れる＝やり込みは塞がない。
    //   --job= 固定中（JobForcedByCmdline）は QA のため素通し＝IsJobUnlocked と同じ逃し口。
    public bool IsMinaLockedForFinal => !JobForcedByCmdline && !IsFinalCleared;
    // FINAL のカード／ジョブ選択が出す1行。ミナ本人が自分を指す言い方は「わたくし」（ミナの口調）。
    public const string MinaFinalLockHint = "この投稿には、わたくし自身では潜れません";

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
    // ───── 他ジョブ潜行の章管理（2026-09-15・キャラ専用ストーリー）─────
    //   キャラごとの「ダイブ回数」カウンタ。DiffSelect のダイブ確定時に、結び手以外のジョブで
    //   本編3面へ潜るときだけ増える（Stage の _Ready で数えると R リトライの ReloadCurrentScene
    //   でも増えて章が飛ぶため、入口を DiffSelect に一本化）。永続は save_N.json の "charDives"
    //   （CharacterId→回数）。キー無し＝旧セーブは全員0回＝後方互換（新キーはこの1個だけ）。
    private readonly Dictionary<string, int> _charDives = new();
    public int CharacterDives(Job j) => _charDives.TryGetValue(Jobs.Get(j).CharacterId, out var v) ? v : 0;
    public void RegisterCharacterDive(Job j)
    {
        if (j == Job.Tank) return;   // 結び手＝ミナ本編。専用ストーリーの章は進めない
        string id = Jobs.Get(j).CharacterId;
        _charDives[id] = CharacterDives(j) + 1;
        GD.Print($"[CharStory] dive #{_charDives[id]} as {Jobs.Get(j).CharacterName}({id}) -> chapter {CharacterChapter(j)}");
    }
    // いま潜ったら流れる章：1〜3回目＝第1〜3章、4回目以降＝ループ章（CharacterStory.LoopChapter）。
    //   ダイブ0回（--job= でのステージ直接起動＝QA走行など）でも第1章に落ちる＝進行は壊れない。
    public int CharacterChapter(Job j) => Mathf.Clamp(CharacterDives(j), 1, CharacterStory.LoopChapter);

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
    // 撃ち方ごとの「本数」。素の値（拡散5way／ホーミング2発）に、#8 弾の線+1 のぶんだけ足す。
    //   ★2026-09-13：本数を火力（旧 shot_power）から導出するのを止めた。線数は線数の段が決める。
    public int SpreadWays => 5 + ExtraLines * 2;   // 5 → 7way（扇は左右対称に増やすので +2）
    public int HomingShots => 2 + ExtraLines;      // 2 → 3発
    public string ShotModeName(ShotMode m) => m switch { ShotMode.Spread => "拡散", ShotMode.Homing => "ホーミング", ShotMode.Accel => "加速球", _ => "連射" };
    // 残機・ボムは難易度ベース ＋ 恒久強化ボーナス。
    // Lunaticは弾密度(BulletCountMul=1.9、Hard比+73%)・弾速(1.18)・間隔(0.85)全てが全難易度中最厳。
    // Easy(6)→Normal(4)→Hard(3)の減り方（-2,-1）に沿って Hard→Lunatic も -1 段階減らし、
    // 最終ティアの「賭け金」をリターン（Lunatic解禁自体がフォロワー200等のやり込み到達点）に見合わせる。
    // 恒久強化（ハート +1 が2段＝最大 +2）を乗せて初めて現実的に戦える設計は維持（②-4想定通り）。
    // ジョブの最大♥増減（結び手 +2 ／ 灯し手 −1）もここへ乗せる＝回復キャップ(Player.AddLife)も同時に追従する。
    // 下限1：灯し手×Lunatic(基礎2)でも 1 は残す＝「開始即ゲームオーバー」を作らない。
    public int StartLives => Mathf.Max(1, BaseLivesFor(Difficulty) + MaxLifeBonus + JobDef.MaxLifeDelta);
    public int StartBombs => BaseBombsFor(Difficulty) + BombCountBonus;
    // ♥とボムの基礎値は難易度・ステージに依らず一定（2026-09-15 ユーザー決定:
    // 「♥はどのステージを選択しても3個スタート、ボムは1個。強化したら♥が増えてくシステムなので」）。
    // 難易度の賭け金は弾速・弾数・出現密度の側だけが担う。伸びる経路は
    // ショップの「ハート +1」×2段・「ボム +1」と、ジョブ補正（結び手+2/灯し手-1）。
    public static int BaseLivesFor(Diff d) => 3;
    public static int BaseBombsFor(Diff d) => 1;
    public float BulletSpeedMul => Difficulty switch { Diff.Easy => 0.62f, Diff.Hard => 1.05f, Diff.Lunatic => 1.18f, _ => 0.85f };
    // 難易度は敵の体力ではなく「弾の数」で調整する（やさしいほど弾が少ない）。
    public float BulletCountMul => Difficulty switch { Diff.Easy => 0.38f, Diff.Hard => 1.1f, Diff.Lunatic => 1.9f, _ => 0.7f };
    public float DanmakuIntervalMul => Difficulty switch { Diff.Easy => 2.1f, Diff.Hard => 1.0f, Diff.Lunatic => 0.85f, _ => 1.35f };
    // 道中ザコの出現間隔倍率（Spawner が基準間隔に掛ける。小さいほど速く湧く）。
    // 上の Dn/Di/弾速はどれも「撃った後の弾」にしか効かず、道中の圧＝出現密度は全難易度で同じだった
    // （Spawner に Difficulty の参照がゼロ）。難しいほど「敵が多い」を成立させる軸をここに足す（2026-09-06）。
    public float SpawnIntervalMul => Difficulty switch { Diff.Easy => 1.3f, Diff.Hard => 0.78f, Diff.Lunatic => 0.62f, _ => 1.0f };
    // 同時に画面へ出せるザコの上限（Spawner の過密ガード）。間隔だけ縮めても上限で頭打ちになるので対で動かす。
    public int MaxAliveEnemies => Difficulty switch { Diff.Easy => 6, Diff.Hard => 10, Diff.Lunatic => 12, _ => 8 };
    public string DiffName => Difficulty switch { Diff.Easy => "EASY", Diff.Hard => "HARD", Diff.Lunatic => "LUNATIC", _ => "NORMAL" };

    // ── 撃破時に散る「心の欠片」の難易度倍率（2026-09-17 ユーザー要望）──
    //   粒の個数と、欠片が運ぶスコア総量の両方に掛ける（FxLayer.PurifyBurst）。
    //   ★ショップ通貨(Impression)の DifficultyImpressionMulFor(0.7/1.0/1.6/3.0) とは別物。
    //     ★2026-09-17 の経済改修で、欠片は Score に加えてショップ通貨も運ぶようになったが、
    //       この倍率が掛かるのは「粒の数」と「Score 総量」だけ。欠片が運ぶインプレ基礎額
    //       (impBase) には掛けない（FxLayer.PurifyBurst の引数コメント参照）＝二重適用の回避。
    //       なのでここを動かしても、ショップ経済（価格150〜4,000／クリア報酬400）には影響しない。
    //   ★カーブを 0.7/1.0/1.6/3.0 より緩くした理由：この倍率は「画面に出る粒の実数」に直結する。
    //     3.0 を掛けると Lunatic のボスで 128×3.0＝384粒となり、容量640の6割を1撃で食い潰し、
    //     改心の一拍で画面が花びらに埋まって視認性が落ちる（派手さは視認性を侵さない範囲で＝§4）。
    //     1.6 なら最大205粒＝qa 実測(121粒/144fps)の1.7倍で容量にもフレームにも余裕が残る。
    //   ★Easy を 0.8 止まり（0.7 でなく）にしたのは、入口の難易度で「明らかに寂しい撃破」を
    //     作らないため（§3 とっつき）。賭け金の差は弾速・弾数・出現密度が既に担っている。
    public static float DifficultyShardMulFor(Diff d) => d switch { Diff.Easy => 0.8f, Diff.Hard => 1.3f, Diff.Lunatic => 1.6f, _ => 1f };
    public float DifficultyShardMul => DifficultyShardMulFor(Difficulty);

    // ボスHPバー本数（言葉のシールド＋無防備窓リワーク）。1本=BarHp(=100)で、総HP=本数×BarHp。
    // 難易度で本数が増える＝堅くなる（弾数調整とは別軸の「殴る回数」調整）。
    // 通常ボス: Easy2/Normal4/Hard5/Lunatic6（#25: Easyは据え置き＝入口を守り、Normal以上を+1本）。
    // ラスボス格(Mina)は +2本（finalBoss=true。Easy4/Normal6/Hard7/Luna8。B-5: 強化が伸びた終盤でも
    // シールド段の攻防が痩せないよう +1→+2）。無防備窓のキャップは据え置き。
    public int DiffBarBonus(bool finalBoss) =>
        (Difficulty switch { Diff.Easy => 2, Diff.Hard => 5, Diff.Lunatic => 6, _ => 4 }) + (finalBoss ? 2 : 0);

    // ルナティック解禁条件（①-9）：フォロワーが一定 or 一本道 #5「火力 2倍」を持っている。
    public const int LunaticFollowerReq = 200;
    public bool IsLunaticUnlocked => Followers >= LunaticFollowerReq || Has("n_power_2x");

    // ───── 回避 ─────
    //   回避（Alt / L3 / 右クリック）は 2026-09-22 からショップの段 #2「回避」（n_dodge）で覚える
    //   （それまでは 1面クリアの物語報酬＝GrantDodge／セーブキー "hasDodge"。旧セーブは LoadFromSlot が
    //   n_dodge の所持へ読み替える）。所持判定 HasDodge は強化効果アクセサの並び（HasChargeShot の隣）。

    // ───── 集中モード（一本道 #11「集中モード」・Vキー）─────
    //   Engine.TimeScale は使わない（自機・HUD・音・演出まで巻き込み、ヒットストップとも二重に掛かる）。
    //   代わりに「敵側だけが読む delta 係数」をここに一本置き、敵・敵弾・予兆・嵐がそれを掛けて時間を進める。
    //   ＝自機の操作感は等速のまま、向かってくるものだけが遅くなる。
    public const float FocusModeScale = 0.35f;   // 敵側の時間倍率
    public const float FocusModeDur = 1.5f;      // 持続（秒・実時間）
    public const float FocusModeCd = 20f;        // クールダウン（秒・実時間）
    private float _focusModeT;                   // 残り持続
    private float _focusModeCd;                  // 残りクールダウン
    public bool FocusModeActive => _focusModeT > 0f;
    public bool FocusModeReady => HasFocusMode && _focusModeCd <= 0f && _focusModeT <= 0f;
    public float FocusModeCdRatio => Mathf.Clamp(_focusModeCd / FocusModeCd, 0f, 1f);
    public float FocusModeRatio => Mathf.Clamp(_focusModeT / FocusModeDur, 0f, 1f);

    // 敵側が delta に掛ける係数。★ヒットストップ（GameCamera が Engine.TimeScale を 0.06 に落とす）と
    //   二重に掛けない：TimeScale が落ちている間は既に全体が止まっているので、ここでは 1 を返す。
    public static float EnemyTimeScale
    {
        get
        {
            if (Engine.TimeScale < 0.99) return 1f;   // ヒットストップ中＝二重掛け禁止
            var g = Instance;
            return g != null && g.FocusModeActive ? FocusModeScale : 1f;
        }
    }
    // 敵側ノードが使う唯一の入口。delta にこれを掛けてから時間を進める。
    public static double EnemyDelta(double delta) => delta * EnemyTimeScale;

    // 発動（Player が V のエッジで呼ぶ）。使えなければ false（＝SE も鳴らさない）。
    public bool TryFocusMode()
    {
        if (!FocusModeReady) return false;
        _focusModeT = FocusModeDur;
        _focusModeCd = FocusModeCd;
        GD.Print($"[focus] ON scale={FocusModeScale} dur={FocusModeDur}s cd={FocusModeCd}s");
        return true;
    }

    // 集中モードの時計を送る（Player._PhysicsProcess から実時間 delta で1本呼ぶ）。
    //   ★ここは自機側の時計＝EnemyTimeScale を掛けない（掛けると自分で自分を伸ばしてしまう）。
    public void TickFocusMode(float dt)
    {
        if (_focusModeT > 0f)
        {
            _focusModeT -= dt;
            if (_focusModeT <= 0f) { _focusModeT = 0f; GD.Print("[focus] OFF"); }
        }
        else if (_focusModeCd > 0f) _focusModeCd -= dt;
    }

    // ダイブ先の受け渡し（ハブ→難易度選択→ステージ）。
    public string PendingStageScene = "res://Rei.tscn";

    // ───── チェックポイント入口（最初から / 中ボスから / ボスから）─────
    //   中ボス(cameo)を持つ3ステージ（レイ/あかり/こはる）で道中をスキップして任意の戦闘から始められる。
    //   SelectedEntry は「ラン単位」＝非セーブ。DiffSelect がダイブ直前にセットし、Stage が _Ready で読む。
    //   解放ゲート：MidBoss は中ボス撃破で解放（IsMidBossCleared）、Boss はステージクリアで解放（IsStageCleared）。
    //   AfterMidBoss は DiffSelect には出さない“続きから再開”専用（初回ショップ導線がプログラム的にセット）。
    public enum StageEntry { Start, MidBoss, Boss, AfterMidBoss }
    public StageEntry SelectedEntry = StageEntry.Start;

    // 初回ショップ導線の復帰先：非nullなら、ショップ退出時にハブでなくこのステージへ戻り、
    // 中ボスの“続き”（道中後半＝Step_MidwaveB）から再開する（ラン単位・非セーブ。退出時に消費してnullへ）。
    public string? PendingResumeScene;

    // 中ボス(cameo)撃破フラグ（ステージID集合・永続＝save_N.json）。「中ボスから」解放の判定に使う。
    private readonly HashSet<string> _midBossCleared = new();
    public bool IsMidBossCleared(string id) => _midBossCleared.Contains(id);
    // 中ボス撃破を記録。戻り値 firstEver＝「全ゲーム通して初めて中ボスを倒した」か（初回ショップ導線の判定用）。
    public bool MarkMidBossCleared(string id)
    {
        bool firstEver = _midBossCleared.Count == 0;
        _midBossCleared.Add(id);
        return firstEver;
    }

    // 初回ショップ説明を見たか（全ゲーム通して一度きり・永続＝save_N.json）。
    // 最初の面のボスを初めて倒してハブへ帰った回に、ホーム解禁演出の直後へ一度だけ挟む（Hub が立てる）。
    // 以降のクリアでは説明へ離脱しない。
    public bool ShopTutorialSeen;

    // 説明パート（ShopTutorial）からハブへ帰ってきた直後か（ランタイム限り・保存しない）。
    //   true のときハブはホーム画面で開き、強化ショップのアイコンを選択＋誘導表示にして「押す」のを待つ。
    //   ハブが消費して false に戻す＝ショップから戻ったときや次の入場には持ち越さない。
    //   2026-09-22: 説明を読み切ると勝手にショップが開いていたのをやめ、アイコンを押す操作を
    //   プレイヤー自身にさせる導線へ変えた（「このアイコンから入る」を体で覚えてもらう）。
    public bool ShopNudgePending;

    // 弾幕の本数を難易度でスケール（最低1発は残す）。各ボスのリング/扇の本数に掛ける。
    public int ScaleBullets(int baseCount) => Mathf.Max(1, Mathf.RoundToInt(baseCount * BulletCountMul));

    // 累計浄化数。
    public int PurifiedCount { get; private set; }
    // 累計グレイズ（かすり）数。加算のみで現状は読み手なし（将来グレイズチュートリアルが実装されれば読み手になり得る）。
    public int GrazeCount { get; private set; }
    // このランの被弾回数（Player.TakeHit が実際に♥を減らしたときだけ数える）。ハブ帰還の「被弾は{n}回でした」
    // （仮台本 06 の H1）の差し込み値。補助観測＝表示専用でセーブしない。ResetRun（ラン開始）で 0 に戻る。
    public int RunHitCount { get; private set; }
    public void NotifyPlayerHit() => RunHitCount++;

    // ステージ目標：このタイムラインを浄化しきる人数。到達でステージクリア。
    public int StageTarget { get; private set; } = 24;
    public void SetStageTarget(int t) => StageTarget = Mathf.Max(1, t);

    // ───── 前のめり進行（リスクリターン：撃破＋時間＋自機の左右位置）─────
    //   進行度＝「撃破率」が本体。そこへ時間アキュムレータぶんだけ先行（前のめり）を上乗せする。
    //   ・killFrac  ＝撃破率。ゲージの本体＝下限保証（ここが痩せることはない）。
    //   ・progAccum ＝時間アキュムレータ。posFactor(playerX) で毎フレーム積む（右ほど速い）。
    //   ★ゲージのラベルは「浄化」＝撃破の達成度。撃破ゼロで動いてはならない（ラベルが嘘になる）。
    //     そのため時間ぶんは単独では効かせず、**撃破が進んでいるぶんだけ**先行を許す（LeadGate）。
    //     撃破0 → 先行0（ゲージは 0% のまま動かない）。撃破が進むほど前のめりの取り分が増える
    //     ＝「攻めたほうが得」（リスクリターン §2-4）が撃破と比例して立つ。
    //   進行不能防止の不変条件（絶対に壊さない）：
    //     ・道中ウェーブの撃破ゲート／StageCleared は PurifiedCount だけで判定＝ここは一切触らない。
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
    // スポーン密度倍率：右へ寄るほど敵が多い（＝リスク）。左端0.60 / 中央1.05 / 右端1.50。
    public static float SpawnRateMul(float nx) => 0.60f + 0.90f * Mathf.Clamp(nx, 0f, 1f);

    // 毎フレーム、自機Xと dt を受けて時間アキュムレータを進める（各ステージ Root の _Process から1本呼ぶ）。
    // 撃破カウンタ(PurifiedCount)には一切触れない＝撃破ゲート/StageCleared は不変。
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
            float killFrac = Mathf.Clamp((float)PurifiedCount / StageTarget, 0f, 1f);
            float timeFrac = _progAccum; // TickProgress で 0..ProgTimeCap に clamp 済み
            float lead = (1f - killFrac) * timeFrac * LeadGain * LeadGate(killFrac);
            return Mathf.Clamp(killFrac + lead, 0f, 1f);
        }
    }
    public bool StageCleared => PurifiedCount >= StageTarget;

    // 「世界の暖かさ(0=冷たい荒れた世界 → 1=暖かい浄化された世界)」＝浄化の進捗。
    public float Warmth => StageProgress;

    // ミナの汚染ゲージ（0=澄んでいる → 1=黒く溶ける）。穢れを祓うほど自機の光が濁る。
    // シーンをまたいで保持し、各ステージで段階的に上げる（ResetRun では消さない＝物語の背骨）。
    public float Contamination { get; private set; }
    public void SetContamination(float v) => Contamination = Mathf.Clamp(v, 0f, 1f);

    // 設定（Settings 画面から反映）：会話のタイプライター速度／オート送り。
    public float MsgCharsPerSec { get; set; } = 48f;
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
        new() { Id = "koharu", Scene = "res://Koharu.tscn", Handle = Handles.Koharu, Tweet = "今日の配信も最高だった。これで、明日も学校、行ける。", Title = "STAGE 2 — こはる" },
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
    // ※ StageHasMidBoss（中ボス持ちか＝チェックポイント入口を出す対象か）は 2026-09-07 に削除した。
    //   唯一の読み手だった「どこから始めますか?」の入口ダイアログを廃止し、呼び出し元が消えたため
    //   （DiffSelect.cs / Hub.cs の該当コメント参照）。IsMidBossCleared / SelectedEntry は現役。

    private readonly HashSet<string> _cleared = new();
    // 直近にクリアしたステージ（ハブ帰還時の会話＆自動投稿トリガ。ハブが消費して null に戻す）。
    public string? JustClearedStageId;

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
    // コメント返信済みのステージ（セッション内・1回だけ報酬）。
    private readonly HashSet<string> _replied = new();
    public bool HasReplied(string id) => _replied.Contains(id);
    public void MarkReplied(string id) => _replied.Add(id);

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
    //   ＝既存セーブ（FINAL 到達済み）はロードした瞬間に周回扱いになる。Records.cs も同じ考えで
    //   AllStoryCleared を「FINAL の行を開く条件」に使っている（あちらは表示、ここは進行ゲート）。
    public bool IsFinalCleared
    {
        get
        {
            foreach (Diff d in System.Enum.GetValues(typeof(Diff)))
                if (GetBestTime(FinalStageId, d) != null) return true;
            return false;
        }
    }
    // マクロ目標（表ゴール＝控えめHUD用）：救うべき心の総数と、浄化済みの数。
    public int HeartGoal => Stages.Length;
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
        RegisterStageClear();
        _cleared.Add(id);
        JustClearedStageId = id; // ハブで帰還会話＆自動投稿を再生する
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

    // ─── 会話選択（層2プロト）───
    //   STAGE2（こはる）MidStory の2択で A「もういちど、聞く」を選んだ（＝もう一度踏み込んだ）。
    //   下流2場面（StageKoharu.Clear の1行／Epilogue 独白の1行）の変種差し替えにだけ使う収束型フラグ。
    public bool PressedTheQuestion;

    // ─── 仕掛けの値（案C の組み込み計画）───
    //   選択のたびに「選ばれなかった言葉」が散り、終盤（FINAL F4 / エピローグ E2）でそれが戻ってくる。
    //   ここは**器だけ**：各場面からの記録は台本タスクで繋ぐので、現時点でこれらを書く呼び出しは無い。
    //   セーブの作法は pressedQ と同じ＝キー無し＝既定値（旧セーブがそのまま読める）。
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
    // フォロワー＝第2の恒久ステータス。基本的に減らない＝「届けた証」（§0-1）。火力/インプレ倍率に常時上乗せ。
    public int Followers { get; private set; }
    // 今回のラン(ステージ)で稼いだインプレ。HUD表示「🔥 +N」用。ResetRun で 0。
    public long RunImpression { get; private set; }

    // 恒久強化の所持（id → 0/1）。一本道14段はすべて買い切り＝値は 0 か 1 しか取らない。
    // セーブ移行（MigrateUpgradesIfLegacy）で丸ごと差し替えるため readonly にはしない。
    private Dictionary<string, int> _upgrades = new();

    // 強化カタログ（§①-4）。効果は下の各アクセサで定義。
    // ★2026-09-13：分岐する木（70ノード・排他・振り直し）を畳み、一本道13段に作り直した。
    //   買える段は常にひとつ＝「次の一手」を選ばせない。順序条件は ParentId（直前の段）だけ。
    public sealed class UpgradeDef
    {
        public string Id = "";
        public string Name = "";   // ＝効果そのもの（画面に出る唯一の説明）
        public string Desc = "";   // 詳細パネルの「いま → 買うと」を組む材料
        public int MaxLevel;       // 一本道では常に 1（買い切り）
        public long BaseCost;
        // 直前の段。""＝先頭（n_life_1 のみ）。これを持っていなければ買えない＝一本道の順序そのもの。
        public string ParentId = "";
    }

    // ───── 一本道14段（ショップ作り直し・2026-09-13／回避を段に加えて 13→14 段・2026-09-22）─────
    //   分岐・排他・振り直しを全廃し、「上から順にしか買えない」1列だけにした。ノード名＝効果そのもの
    //   ＝詳細の地の文を読ませなくても何が起きるか分かる（Desc は「いま → 買うと」の材料としてだけ持つ）。
    //   ・順序は ParentId が直前の段を指すことで担保する（IsParentMet ＝ 直前の段の所持）。
    //   ・価格は 150→4,000 の単調増加。1面クリア報酬 400×MoneyGainMul(2) と道中の稼ぎで
    //     「1面ごとに1〜2段」進む速度を狙っている。
    //   ・#2 回避／#7 溜め打ち／#11 集中モードは「できることが増える」段＝Shop が一回り大きく描く（IsAbilityNode）。
    //   ・#2 回避（200）はもともと 1面クリアの無料報酬だったもの（2026-09-22 ユーザー決定でショップの品目へ）。
    //     ショップが開く瞬間（＝1面クリア直後・財布は 800＋道中）に #1〜#3 が 150+200+300=650 で揃う
    //     ＝移す前（150+300=450 で #2 回避強化まで）と到達段が同じ。回避は実質ただ同然のまま「買って覚える」形になる。
    //     #3「回避強化」は親に n_dodge を持つ＝回避を持たずに強化だけ買う形は作れない。
    //   ★この配列の並びがそのまま画面の並び＝唯一の正典。増減はここだけを編集する。
    public static readonly UpgradeDef[] Upgrades =
    {
        new() { Id = "n_life_1",   Name = "ハート +1",             Desc = "はじまりの♥が1つ増える",               MaxLevel = 1, BaseCost =  150, ParentId = "" },
        new() { Id = "n_dodge",    Name = "回避",                  Desc = "一瞬無敵になって弾を抜ける",             MaxLevel = 1, BaseCost =  200, ParentId = "n_life_1" },
        new() { Id = "n_dodge_cd", Name = "回避強化", Desc = "回避が早く戻り、距離も伸びる",         MaxLevel = 1, BaseCost =  300, ParentId = "n_dodge" },
        new() { Id = "n_bomb_1",   Name = "ボム +1",               Desc = "はじまりのボムが1つ増える",             MaxLevel = 1, BaseCost =  450, ParentId = "n_dodge_cd" },
        new() { Id = "n_power_2x", Name = "火力 2倍",          Desc = "撃った光の威力が2倍になる",             MaxLevel = 1, BaseCost =  700, ParentId = "n_bomb_1" },
        new() { Id = "n_life_2",   Name = "ハート +1",             Desc = "はじまりの♥がもう1つ増える",           MaxLevel = 1, BaseCost =  900, ParentId = "n_power_2x" },
        new() { Id = "n_charge",   Name = "溜め打ち",      Desc = "キャラクター固有の強力な弾を溜めて放つ",   MaxLevel = 1, BaseCost = 1200, ParentId = "n_life_2" },
        new() { Id = "n_hitbox",   Name = "当たり判定 半分",       Desc = "被弾判定の半径が半分になる",            MaxLevel = 1, BaseCost = 1500, ParentId = "n_charge" },
        new() { Id = "n_lines",    Name = "ライン +1",           Desc = "撃ち方ごとに光の筋が1本増える",         MaxLevel = 1, BaseCost = 1800, ParentId = "n_hitbox" },
        new() { Id = "n_move_15x", Name = "移動速度 1.5倍",        Desc = "移動が1.5倍速くなる",                   MaxLevel = 1, BaseCost = 2200, ParentId = "n_lines" },
        new() { Id = "n_slow",     Name = "集中モード",    Desc = "敵の時間だけ遅くする",                  MaxLevel = 1, BaseCost = 2600, ParentId = "n_move_15x" },
        new() { Id = "n_rate_2x",  Name = "連射速度 2倍",          Desc = "発射間隔が半分になる",                  MaxLevel = 1, BaseCost = 3000, ParentId = "n_slow" },
        new() { Id = "n_pierce",   Name = "貫通",      Desc = "どの撃ち方でも弾が敵1体を貫通する",     MaxLevel = 1, BaseCost = 3500, ParentId = "n_rate_2x" },
        new() { Id = "n_option",   Name = "オプション +1",         Desc = "追従オプションが1基つく（威力×0.5）",   MaxLevel = 1, BaseCost = 4000, ParentId = "n_pierce" },
    };

    // 能力を覚える段（Shop が一回り大きく描く）。数値ではなく「できることが増える」段。
    public static bool IsAbilityNode(string id) => id == "n_dodge" || id == "n_charge" || id == "n_slow";

    // 一本道で「次に買える1段」＝先頭から数えて最初の未所持。全部買い切っていれば null。
    public string? NextColumnNode()
    {
        foreach (var d in Upgrades)
            if (GetUpgradeLevel(d.Id) < 1) return d.Id;
        return null;
    }

    // 所持判定のショートハンド（効果アクセサがこれ1本で読む＝ChainLevel の段数計算は要らなくなった）。
    public bool Has(string id) => GetUpgradeLevel(id) >= 1;

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

    public int GetUpgradeLevel(string id) => _upgrades.TryGetValue(id, out var v) ? v : 0;

    // ───── セーブ移行（旧70ノード／旧MaxLv方式 → 一本道14段）─────
    //   方針は「没収ゼロ」：旧セーブが強化に投じた総額を決定的に再計算し、その金額で新列の先頭から
    //   買えるだけ自動所持させる（差額は返金しない＝旧価格でどこまで積めたかが、そのまま新列の到達段になる）。
    //   ・旧MaxLv方式（"fire_rate":3 のように Lv 値を持つ）は「Lv n＝その鎖の先頭 n 段を買った」とみなし、
    //     旧ノード価格表 LegacyCost から総額を積む。
    //   ・単Lvノード方式（"fire_rate_1":1 …）はそのノードの旧価格をそのまま積む。
    //   ・どちらでもない未知キーは無視（壊れたセーブで落ちない）。
    //   ワンショット：移行後は新IDだけが _upgrades に残るので、次回ロードでは legacy 判定に掛からない。
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

    // 旧IDが混じっていれば一本道14段へ読み替える（LoadFromSlot の _upgrades 復元直後に呼ぶ）。
    //   移行した段数は MigratedNodeCount に控える（ショップ／QAログが「何段引き継いだか」を見せる用）。
    public int MigratedNodeCount { get; private set; } = -1; // -1＝このロードでは移行が走らなかった
    private void MigrateUpgradesIfLegacy()
    {
        long paid = 0;
        bool legacy = false;
        foreach (var kv in _upgrades)
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
        var keep = new Dictionary<string, int>();
        foreach (var kv in _upgrades)
            if (GetUpgradeDef(kv.Key) != null) keep[kv.Key] = kv.Value;
        _upgrades = keep;
        int got = 0;
        foreach (var d in Upgrades)
        {
            if (GetUpgradeLevel(d.Id) >= 1) { got++; continue; }    // 既に持っている段は数えるだけ
            if (paid < d.BaseCost) break;
            paid -= d.BaseCost;
            _upgrades[d.Id] = 1;
            got++;
        }
        MigratedNodeCount = got;
        GD.Print($"[migrate] legacy upgrades -> column: {got} nodes owned (leftover {paid} imp discarded)");
    }

    // Lv→Lv+1 の価格。一本道では各ノード MaxLevel=1・固定 BaseCost なので、
    // Lv0（未所持）なら BaseCost をそのまま返し、Lv1（所持済＝最大）は 0。
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

    // 一本道の順序条件＝直前の段を持っているか。先頭（ParentId=""）は常に true。
    // 所持済みノードは常に true（＝並びを変えても既に買った段が「買えない」扱いに落ちない）。
    public bool IsParentMet(string id)
    {
        if (GetUpgradeLevel(id) >= 1) return true;
        var d = GetUpgradeDef(id);
        if (d == null || string.IsNullOrEmpty(d.ParentId)) return true;
        return GetUpgradeLevel(d.ParentId) >= 1;
    }

    public bool CanPurchase(string id)
    {
        long c = GetUpgradeCost(id);
        return c >= 0 && Impression >= c && IsParentMet(id);
    }

    // 強化を1段購入。成功で true。保存はポーズメニューの手動セーブで行う。
    public bool TryPurchase(string id)
    {
        if (!CanPurchase(id)) return false;
        Impression -= GetUpgradeCost(id);
        _upgrades[id] = GetUpgradeLevel(id) + 1;
        return true;
    }

    // ───── トレーニングモード（試し打ち場）用：メタ状態の退避／復元と、ゲート無視の直書き ─────
    //   トレーニングは「完全無料・試用のみ」＝本番の購入済み・所持ポイント・装備を一切変えずに
    //   スキルを自由に付け外しして撃ち味を比べる場。入場時に SnapshotMeta で退避し、退場時に RestoreMeta で
    //   丸ごと戻す（＝本番状態を壊さない）。この機構は _upgrades を差し替えるだけで、セーブは一切呼ばない。
    //   ※ディスクへの漏れ防止は呼び出し側が AutoSaveEnabled=false で担保する（Hub帰還等の自動セーブを止める）。
    public sealed class MetaSnapshot
    {
        public long Impression;
        public int Followers;
        public ShotMode SelectedShotMode;
        public Job SelectedJob;
        public Dictionary<string, int> Upgrades = new();
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
            Upgrades = new Dictionary<string, int>(_upgrades), // 値コピー（各Lvは 0/1 の int）
        };
        return s;
    }

    // 退避したメタ状態へ完全復元する（トレーニング退場時に必ず呼ぶ）。_upgrades は丸ごと差し替える。
    public void RestoreMeta(MetaSnapshot s)
    {
        if (s == null) return;
        Impression = s.Impression;
        Followers = s.Followers;
        SelectedJob = s.SelectedJob;      // ジョブ→モードの順（セッタがモードを上書きするため）
        SelectedShotMode = s.SelectedShotMode;
        _upgrades = new Dictionary<string, int>(s.Upgrades);
    }

    // トレーニング用：親/前提/封印・価格を無視して1ノードを直接 付ける/外す（購入パスを通さない）。
    public void TrainingSetUpgrade(string id, bool owned)
    {
        if (GetUpgradeDef(id) == null) return;
        if (owned) _upgrades[id] = 1;
        else _upgrades.Remove(id);
    }

    // トレーニング用：カタログ全ノードを一括で 付ける/外す（撃ち味の全開↔素の比較に使う）。
    public void TrainingSetAllUpgrades(bool owned)
    {
        foreach (var d in Upgrades) TrainingSetUpgrade(d.Id, owned);
    }

    // トレーニング用：通貨を直接セットする（試用中は実質無限。表示専用＝購入では減らさない運用）。
    public void TrainingSetImpression(long v) => Impression = v;

    // ── フォロワー由来の常時バフ（天井付き・§①-5）──
    // PowerMul は Player.Fire の弾ダメージに実配線（fol_gain＝“火力の遠回り投資”の受け皿）。係数 0.00010→0.00025＝2,000人で上限+50%。
    public float FollowerPowerMul => 1f + Mathf.Min(0.50f, Followers * 0.00025f);
    public float FollowerImpressionMul => 1f + Mathf.Min(0.50f, Followers * 0.00008f);

    // ── 難易度・強化由来のインプレ倍率 ──
    public static float DifficultyImpressionMulFor(Diff d) => d switch { Diff.Easy => 0.7f, Diff.Hard => 1.6f, Diff.Lunatic => 3.0f, _ => 1f };
    public float DifficultyImpressionMul => DifficultyImpressionMulFor(Difficulty);
    // 獲得心の倍率。一本道では「獲得倍率を買う」段を持たないので恒久ボーナスは無し（1.0 固定）。
    //   稼ぎの伸びは MoneyGainMul（一律2倍）とステージクリア報酬(400)が担う。
    public float UpgradeImpressionMul => 1f;
    // 獲得インプレ（お金）全体の追加倍率。コスト/価格には掛からない＝獲得だけ増える。後で調整しやすいよう定数化。
    public const float MoneyGainMul = 2f;
    // 炎上中は収入 0.6倍。★2026-09-13：炎上の弱体は「収入が減る」だけに絞った
    //   （旧実装の 連射×1.3／移動×0.9 は、手触りそのものを鈍らせて理由も見えない＝いちばん質の悪い罰だった）。
    public float TotalImpressionMul => DifficultyImpressionMul * FollowerImpressionMul * UpgradeImpressionMul * (BurningThisRun ? 0.6f : 1f);

    // ── 強化効果アクセサ（一本道14段。所持しているかどうかの1/0だけで決まる）──
    // #5 弾の火力2倍。威力式の最終段で掛ける倍率（Player.Fire）。旧 ShotDamageBonus（加算）は廃止。
    public int ShotPowerMul => Has("n_power_2x") ? 2 : 1;
    // #12 連射速度2倍＝発射間隔 ×0.5。炎上による間隔弱体は撤廃した（収入0.6倍だけが罰）。
    public float FireIntervalMul => Has("n_rate_2x") ? 0.5f : 1f;
    // #10 移動速度1.5倍（低速移動の廃止＝2026-09-13 により、速度は1本になった）。炎上による移動弱体も撤廃。
    public float MoveSpeedMul => Has("n_move_15x") ? 1.5f : 1f;
    // #8 当たり判定 半分（HitRadius 2.0px → 1.0px）。
    public float HitRadiusMul => Has("n_hitbox") ? 0.5f : 1f;
    // #1 #6 ハート +1 ×2段（表示はどちらも「+1」。累計は自然に +2）。
    public int MaxLifeBonus => (Has("n_life_1") ? 1 : 0) + (Has("n_life_2") ? 1 : 0);
    // #4 ボム +1。
    public int BombCountBonus => Has("n_bomb_1") ? 1 : 0;
    // #14 オプション +1＝追従オプション1基（威力×0.5・Player.OptionSlots）。
    public int OptionSubCount => Has("n_option") ? 1 : 0;
    // #13 貫通＝全撃ち方の弾が1体貫通（Bullet.Pierce）。
    public int ShotPierceCount => Has("n_pierce") ? 1 : 0;
    // #9 ライン +1。連射の線・拡散のway・ホーミングの発数・加速球の発数を、各 Fire が素の値へ足す。
    public int ExtraLines => Has("n_lines") ? 1 : 0;
    public bool HasChargeShot => Has("n_charge");
    // #11 集中モード（Vキー・敵側の時間だけ×0.35／1.5秒／CD20秒）。
    public bool HasFocusMode => Has("n_slow");
    // #2 回避（Alt / L3 / 右クリック・無敵 0.45秒）。未所持のあいだ Player.TryDodge は即 return し、
    //   あそびかた／操作カードの回避行は未取得として薄く出る＝解禁が画面で見える。
    //   ★名前と型（bool HasDodge）は Player.cs・StageZero.cs・HowToPlay.cs が読むので変えない。
    public bool HasDodge => Has("n_dodge");
    // #3 回避強化（CD 0.8→0.65秒・距離 64→76px）。
    public float DodgeCooldown => Has("n_dodge_cd") ? 0.65f : 0.80f;
    public float DodgeDistance => Has("n_dodge_cd") ? 76f : 64f;

    // 窓キャップ（Enemy.ExposedDamageCap）のテンポ還元。火力に投資するほど1窓で通せる量が増える＝
    //   「強くなったのに窓の中で手が空く」を作らない。基準100＋火力3段ぶん（各+25）。
    public int ExposedDamageCap => 100 + 25 * ((Has("n_power_2x") ? 1 : 0) + (Has("n_lines") ? 1 : 0) + (Has("n_rate_2x") ? 1 : 0));

    // ── 旧ノードが消えたぶんの既定値（買えなくなった軸は「旧ノードを買い切った値」で固定する）──
    //   ＝一本道化で強化軸が消えても、撃ち味そのものは旧・最終段のまま。弱体化ゼロ（査読確定・§9）。
    public float SpreadPowerMul => 0.62f;      // 旧 spread_power_2
    public float SpreadRateMul => 1.35f;       // 旧 spread_rate_1
    public float HomingPowerMul => 1.05f;      // 旧 homing_power_2
    public float HomingRateMul => 1.40f;       // 旧 homing_rate_1
    public int HomingTurnRateOverride => 200;  // 旧 homing_rate_1（0=Bullet 既定150 を使う、の上書き）
    public float AccelChargeDelay => 0.5f;     // 旧 accel_charge_2
    public float AccelLaunchSpeed => 760f;     // 旧 accel_speed_1
    public float BombPowerMul => 1.25f;        // 旧 bomb_power_1
    public int BackfireDamage => 3;            // 旧 bf_power_2（ダメージ 1+2）
    public float BackfireInterval => 0.7f;     // 旧 bf_rate_1
    public int BackfireShots => 2;             // 旧 bf_track_1
    public float BackfireTurnRate => 90f;      // 旧 bf_track_1

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

    // ── 旧・モード別の上乗せ（連射威力／速射／加速威力）。買う段が無くなったので素の値で固定 ──
    public int RapidPowerBonus => 0;
    public float RapidRateMul => 1f;
    public int AccelPowerBonus => 3;    // 旧 accel_1 + accel_power_2（+1+2）＝加速球を買い切った値

    // インプレを獲得（全倍率を適用して加算）。実際に加算した額を返す。
    public long GainImpression(long baseAmount)
    {
        if (baseAmount <= 0) return 0;
        long g = (long)Mathf.Round(baseAmount * TotalImpressionMul * ReplayMul * MoneyGainMul);
        Impression += g;
        RunImpression += g;
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
        // ★2026-09-13：120→400。一本道14段（150〜4,000）を「1面ごとに1〜2段」で進める速度に合わせた
        //   （実入りは MoneyGainMul=2 と難易度倍率が更に掛かる）。
        // クリア掃引(sweep)で拾い切った欠片の端数を、リザルト表示より前に確定させる
        // （閾値未満のまま残った基礎額が「拾ったのに増えていない」に見えるのを防ぐ）。
        FlushShardImpression();
        GainImpression(400);
        // フォロワー大口報酬。周回逓減も適用（同ステージ連続周回で減る）。
        int fol = Mathf.RoundToInt(40 * ReplayMul); // 旧 fol_gain ノードは廃止＝素の 40 に周回逓減だけ
        AddFollowers(fol);
        AutoSave(); // クリアでオートセーブ（slot 0）
    }

    // ───────────────────────────────────────────────────────────
    // セーブ / ロード（スロット制：user://save_1..3.json）。経済・強化のみ永続。
    // 手動セーブ（ポーズメニュー）でのみ書き込む。起動時の自動ロードはしない。
    // ───────────────────────────────────────────────────────────
    public const int SlotCount = 3;
    private static string SlotPath(int slot) => $"user://save_{slot}.json";
    public bool SlotExists(int slot) => FileAccess.FileExists(SlotPath(slot));

    public void SaveToSlot(int slot)
    {
        var data = new Godot.Collections.Dictionary
        {
            ["impression"] = Impression,
            ["followers"] = Followers,
            ["shotmode"] = (int)SelectedShotMode,
            // ジョブ（ラン単位の選択だが「次に潜るときの既定」としてスロットに残す）。後方互換：キー無し＝結び手。
            ["job"] = (int)SelectedJob,
        };
        var up = new Godot.Collections.Dictionary();
        foreach (var kv in _upgrades)
            up[kv.Key] = kv.Value;
        data["upgrades"] = up;
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
        // ステージ進行（クリア済みステージID＝救った人数・解放・到達度の本体）。後方互換：キー無し＝空。
        var cl = new Godot.Collections.Array();
        foreach (var id in _cleared)
            cl.Add(id);
        data["cleared"] = cl;
        // 炎上ストーリーイベントの状態（既発生か／次ダイブ適用待ちか）。後方互換：キー無し＝false。
        data["burnHappened"] = _burnHappened;
        data["burning"] = Burning;
        // 会話選択（層2プロト）：STAGE2（こはる）MidStory の2択でAを選んだか。後方互換：キー無し＝false（=現行台詞）。
        data["pressedQ"] = PressedTheQuestion;
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
        // 他ジョブ潜行の章カウンタ（CharacterId→ダイブ回数）。後方互換：キー無し＝空扱い。
        var cd = new Godot.Collections.Dictionary();
        foreach (var kv in _charDives)
            cd[kv.Key] = kv.Value;
        data["charDives"] = cd;

        using var f = FileAccess.Open(SlotPath(slot), FileAccess.ModeFlags.Write);
        if (f != null)
            f.StoreString(Json.Stringify(data));
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

        Impression = data.ContainsKey("impression") ? data["impression"].AsInt64() : 0;
        Followers = data.ContainsKey("followers") ? data["followers"].AsInt32() : 0;
        _upgrades.Clear();
        if (data.ContainsKey("upgrades"))
        {
            var up = data["upgrades"].AsGodotDictionary();
            foreach (var k in up.Keys)
                _upgrades[k.AsString()] = up[k].AsInt32();
        }
        // セーブ移行：旧MaxLv方式のID群を単Lvノード鎖へ読み替える。
        // shotmode 復元（HasSpread/HasHoming＝spread_1/homing_1 所持判定）より前に呼ぶ＝没収ゼロで解放判定が正しく効く。
        MigrateUpgradesIfLegacy();
        // 回避の移行（2026-09-22）：回避は「1面クリアの物語報酬（セーブキー "hasDodge"）」からショップの段
        //   n_dodge へ移った。旧セーブの "hasDodge": true は n_dodge の所持に読み替える＝遊びの途中で回避が
        //   消えない（没収ゼロ）。キー無し＝新方式で保存されたセーブ（所持は upgrades が正典）なので何もしない。
        //   "hasDodge" はもう書かない＝次の保存で自然に消える。列の途中の段だけ持つ形（#1 未所持で #2 所持）に
        //   なっても IsParentMet（所持済みは常に true）と NextColumnNode（先頭から最初の未所持）で破綻しない。
        if (data.ContainsKey("hasDodge") && data["hasDodge"].AsBool() && !Has("n_dodge"))
        {
            _upgrades["n_dodge"] = 1;
            GD.Print("[migrate] legacy hasDodge -> n_dodge owned");
        }
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
        // ステージ進行（クリア済み）復元。キー無し＝旧セーブは空＝後方互換。
        _cleared.Clear();
        if (data.ContainsKey("cleared"))
        {
            var cl = data["cleared"].AsGodotArray();
            foreach (var v in cl)
                _cleared.Add(v.AsString());
        }
        // 炎上イベント状態復元（キー無し＝false）。
        _burnHappened = data.ContainsKey("burnHappened") && data["burnHappened"].AsBool();
        Burning = data.ContainsKey("burning") && data["burning"].AsBool();
        // 会話選択（層2プロト）の復元（キー無し＝旧セーブは false＝現行台詞＝後方互換）。
        PressedTheQuestion = data.ContainsKey("pressedQ") && data["pressedQ"].AsBool();
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
        // 他ジョブ潜行の章カウンタ復元（キー無し＝旧セーブは全員0回＝第1章から＝後方互換）。
        _charDives.Clear();
        if (data.ContainsKey("charDives"))
        {
            var cd = data["charDives"].AsGodotDictionary();
            foreach (var k in cd.Keys)
                _charDives[k.AsString()] = cd[k].AsInt32();
        }
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
        return true;
    }

    // はじめから＝メモリ上の永続状態を初期化（スロットのファイルは消さない）。
    public void ResetPersistent()
    {
        Impression = 0;
        Followers = 0;
        _upgrades.Clear();
        // ジョブも初期選択（結び手）へ。SelectedShotMode はセッタが連射へ同期する。
        //   --job=xxx で固定中は「はじめから」でも指定を守る（QA走行で新規データを作る経路を壊さない）。
        if (!JobForcedByCmdline) SelectedJob = Job.Tank;
        _midBossCleared.Clear();
        ShopTutorialSeen = false;
        SelectedEntry = StageEntry.Start;
        _bossRetryScore = 0;
        _cleared.Clear();          // ステージ進行（クリア済み）も初期化＝救った人数0から（回避は _upgrades と一緒に消える）
        _burnHappened = false; Burning = false; BurningThisRun = false;
        PressedTheQuestion = false; // 会話選択（層2プロト）の疑いフラグも初期化
        // 仕掛けの値も初期化（散った言葉が前データから残ると F4/E2 で他人の言葉が戻ってくる）。
        ScatteredWords.Clear(); _scatterById.Clear(); _hesitationById.Clear(); _chosenById.Clear();
        FirstScattered = ""; NameRoute = 0; LastSentWord = ""; HesitationSec = 0f;
        _idleDialogSeen.Clear();   // ハブ再訪小話の既読も初期化
        PhoneWallpaperPhotoId = "";
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
    private const string PrefsPath = "user://prefs.json";

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
    private const string ReadLogPath = "user://read.json";
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

    private double _comboTimer;
    // 会話／改心区間の凍結エッジ検出と、凍結明けの最低残り時間（下の _Process 参照）。
    private bool _comboWasFrozen;
    private const double ComboExitFloor = 1.0;
    // コンボ猶予はコンボ持続強化で延長される。
    // コンボ猶予（秒）。旧 combo_hold ノードは一本道14段に入らなかったので素の 2.0 秒で固定。
    private double ComboWindow => 2.0;
    private const int MaxCombo = 16;
    // コンボ猶予の残り比率（0..1）。HUDのコンボ減衰バー用。コンボが立っていなければ0。
    public float ComboTimeRatio => Combo > 0 && _comboTimer > 0 ? (float)(_comboTimer / ComboWindow) : 0f;

    public override void _Ready()
    {
        // セーブはスロット制（手動）。起動時は自動ロードしない。
        // 端末ローカル prefs（チュートリアル既読など）と会話の既読ログだけは起動時に読む。
        LoadPrefs();
        LoadReadLog();

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
                       + $"imp={Impression} fol={Followers} upgrades={_upgrades.Count} "
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
            if (a == "--choice3") { DebugChoiceNow = true; DebugChoiceThree = true; }
            if (a == "--input-field") DebugInputField = true;
        }
    }

    // --boss 起動中か（消費される SelectedEntry と違い、ランを通して残る）。
    public bool DebugAlwaysBoss { get; private set; }

    // [一時/デバッグ] --choice : こはる面のボス戦中割り込み（会話の選択）を HP 条件を待たずに即発火させる。
    // 選択シーンの確認専用。通常プレイ・配布ビルドでは付けない前提。
    public bool DebugChoiceNow { get; private set; }

    // [一時/デバッグ] --choice3 : 上の割り込みを **3択** で出す（ChoiceOverlay の N 択レイアウト確認用）。
    // --choice を含む。台本上の選択は2択のままで、これは表示検証専用の差し替え。
    public bool DebugChoiceThree { get; private set; }

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

    // 回想フィルム等（StoryFilm/AkariDraftScene/MinaPhaseScene/BossDraftScene）は GameManager.ProcessMode を
    //   Disabled にするため、その間 _Process が回らず _comboWasFrozen が立たない。フィルムが会話を挟まず直接
    //   戦闘へ戻ると再開初フレームが「前も今も非凍結」に見え、ComboExitFloor の底上げが走らなかった
    //   （QA 実測: 残り 0.258s → 明け 0.25s で消失）。Disabled/Enabled の通知で凍結扱いにし、再開フレームで底上げを通す。
    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationDisabled || what == NotificationEnabled) _comboWasFrozen = true;
    }

    public override void _Process(double delta)
    {
        // 2026-09-22 ユーザー指示：会話中はコンボの猶予を減らさない。GameManager は常駐で世界が止まる会話中も
        //   _Process が回るため、2秒の猶予が会話の裏で必ず切れていた。
        //   中ボス／本ボスの改心区間も凍結する：捨て台詞は一行字幕（Hud.ShowBossLine）で BubblePaused を立てず、
        //   尺 2.4s > 猶予 2.0s で「中ボス撃破→捨て台詞→道中再開」が必ず Combo=0 になっていた（QA 実測）。
        //   区間の目印は Hud.SuppressCallouts（Enemy.Redeem の cry で立ち FinishCry で下りる。ザコでは立たない）。
        //   凍結明けは残りを最低 ComboExitFloor 秒へ底上げ＝会話直前に残り僅かだと明けた瞬間に切れて
        //   「会話で途切れた」に見えるため（QA 実測: 残り 0.29s → 37s の会話明け 0.29s で消失）。
        bool comboFrozen = Hud.BubblePaused
            || ((GetTree()?.GetFirstNodeInGroup("hud") as Hud)?.SuppressCallouts ?? false);
        if (_comboWasFrozen && !comboFrozen && _comboTimer > 0)
            _comboTimer = Mathf.Max(_comboTimer, ComboExitFloor);
        _comboWasFrozen = comboFrozen;
        if (_comboTimer > 0 && !comboFrozen)
        {
            _comboTimer -= delta;
            if (_comboTimer <= 0)
                Combo = 0;
        }
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

    // ── ボム1発で報酬が付く雑魚浄化の上限（ボムキャップ）──
    //   ボス側の窓キャップ(Enemy.ExposedDamageCap)と同じ思想を雑魚経路にも当てる。
    //   ボム由来の浄化は「避けも当てもせず画面を一掃する」＝リスクを賭けていない（§2）ので、
    //   1発あたりこの体数までしか スコア/コンボ/インプレ を返さない。超過分は浄化そのものは成立し、
    //   進行(PurifiedCount)は従来どおり通す＝道中ゲートを詰まらせない（親切設計）。
    //   3体＝緊急回避で巻き込む標準的な体数。通常プレイのボムは割に合ったまま、
    //   湧き上限(MaxAliveEnemies＝難易度別 6/8/10/12)まで溜めて一掃する無限ファームだけが頭打ちになる。
    private const int BombPurifyRewardCap = 3;
    private int _bombPurifyCount; // 現在のボム1発で報酬を付けた体数（UseBomb でリセット）

    // 敵を浄化（撃破）した時の加点。コンボ倍率がかかる。
    // fromBomb=true はボムの強制浄化経路（Enemy.Purify）。上のボムキャップを超えた分は報酬を付けない。
    //
    // ★2026-09-17 経済改修：ここで直接 GainImpression(2+Combo) していたのを廃止し、
    //   インプレは「散った欠片を拾う」経路（AddScoreShard）だけから入るようにした。
    //   理由：ShopTutorial/HowToPlay/Shop が一貫して「浄化するたびに貯まる♥＝通貨」と説明しており、
    //   プレイヤーは画面上の欠片＝お金だと読む。実装が「撃破の瞬間に見えない加算／欠片はスコアだけ」
    //   だったので、説明と手触りが食い違っていた（§3 わかりやすさ）。表記に実装を合わせる。
    //   両方から入れると単純に倍増するので、必ず片方だけ（＝拾う側）に寄せる。
    //   PurifyImpressionBase は Enemy.Redeem が PurifyBurst へ渡し、欠片に積んで運ばせる。
    public bool AddPurify(int basePoints, bool fromBomb = false)
    {
        bool rewarded = !fromBomb || ++_bombPurifyCount <= BombPurifyRewardCap;
        if (rewarded)
        {
            Combo = Mathf.Min(Combo + 1, MaxCombo);
            _comboTimer = ComboWindow;
            Score += basePoints * Mathf.Max(1, Combo);
        }
        PurifiedCount++;
        TickPurifyDrain();
        return rewarded;
    }

    // この浄化が欠片に積んで運ぶインプレの基礎額。
    // Enemy.Redeem が AddPurify の直後に読む＝コンボ加算済みの値になる（旧実装と同じタイミング）。
    //
    // ★係数 1.25（旧 2+Combo → (2+Combo)×1.25）：取りこぼし補正。
    //   旧実装は撃破の瞬間に無条件で入っていたので取りこぼしが原理的にゼロだった。拾う側へ寄せた今は
    //   道中ザコの欠片だけが「拾わないと入らない」（ボス/中ボスは BeginRush、ステージクリアは sweep で
    //   全回収が保証されている＝取りこぼしは道中でしか起きない）。磁力半径48px／寿命5秒／左へ流れる
    //   ドリフトがあるため実際の取りこぼしは2割前後で、そのぶんを均して戻す。
    //   ＝「普通に前へ出て拾う人は旧実装より少し得、画面端に張り付く人は旧実装並み」に着地させる係数。
    //   これで初めて「拾いに行く＝攻める」に見返りが生まれる（§2-4 攻めたほうが得）。
    //   四捨五入は掛けた後に1回だけ。Combo=0 でも最低1は出す（撃破が無報酬に見えない＝§2-3）。
    public const float ShardImpressionMul = 1.25f;
    public int PurifyImpressionBase => Mathf.Max(1, Mathf.RoundToInt((2 + Combo) * ShardImpressionMul));

    // ───── 欠片の回収＝スコアとショップ通貨の同時入金（2026-09-17）─────
    //   points : スコア（従来どおり。ランキング／ハイスコアの軸）
    //   impBase: インプレの基礎額（ショップ通貨の軸）。GainImpression の全倍率を通す。
    //
    //   ★役割分担：Score は「上手さの記録」、Impression は「買い物の原資」。欠片は両方を運ぶが、
    //     額は別系統で持つ。スコア側は難易度シャード倍率(0.8/1.0/1.3/1.6)で膨らむ一方、
    //     インプレ側は DifficultyImpressionMul(0.7/1.0/1.6/3.0) が GainImpression 内で掛かるため、
    //     両方を掛けると二重適用になる。だから欠片が運ぶ impBase には
    //     DifficultyShardMul を一切掛けない（FxLayer.PurifyBurst 側で分離済み）。
    //
    //   ★端数の持ち越し：1回の回収は数粒ずつ＝基礎1〜3の小口になる。GainImpression は呼び出しごとに
    //     Round するので、小口を個別に通すと Easy(0.7×2=1.4)で 1→Round(1.4)=1 のように倍率が潰れ、
    //     難易度差が消える。基礎額を整数のまま貯め、一定額を超えた分だけ 1回の GainImpression に
    //     まとめて通す（_impCarry）。
    //
    //   ★総額の保存（2026-09-17 QA検出の修正）：閾値でまとめるだけでは丸め損が残る。基礎40が
    //     8ずつ5バッチに割れると Round が5回走り、Easy×ReplayMul0.8 で 45 のはずが 44 になっていた。
    //     そこで「累積台帳」方式にする：この倍率のもとで払った基礎額(_impPaidBase)と実際に払った額
    //     (_impPaidOut)を覚えておき、毎回の支払いは
    //         Round((_impPaidBase + carry) × 倍率) − _impPaidOut
    //     ＝「ここまでの理論値と既払額の差分」だけにする。バッチの切れ目に関係なく、拾い終えた時点の
    //     総額は必ず単発で払った場合と一致する（丸め損ゼロ）。
    //     倍率自体は run 中に動く（フォロワー増・炎上・周回逓減）ので、変わった瞬間に台帳を締めて
    //     リセットする＝過去の基礎額が新しい倍率で再評価される事故を防ぐ。
    private int _impCarry;
    private long _impPaidBase, _impPaidOut;
    private float _impLedgerTotalMul = float.NaN, _impLedgerReplayMul = float.NaN;
    private const int ImpFlushThreshold = 8;   // この基礎額まで貯めてから1回で通す（倍率の丸め潰れ対策）

    // 累積基礎額の理論値。GainImpression と「掛ける順番まで」同じ式にする
    // （float は掛ける順で最下位ビットが変わるので、単発で払った場合と1単位ずれないよう揃える）。
    private static long ImpTheory(long baseAmount, float total, float replay) =>
        (long)Mathf.Round(baseAmount * total * replay * MoneyGainMul);

    public void AddScoreShard(int points, int impBase = 0)
    {
        Score += points;
        if (impBase <= 0) return;
        _impCarry += impBase;
        if (_impCarry < ImpFlushThreshold) return;
        PayShardImpression();
    }

    // 取りこぼしの端数を吐き出す（ステージ終了・リザルト前に呼ぶ）。
    // 閾値未満で貯まったまま捨てると「拾ったのに入らない」が起きるので、必ず締める。
    public void FlushShardImpression()
    {
        if (_impCarry > 0) PayShardImpression();
        // 締めたら台帳も畳む（次のステージ／次の倍率へ端数を持ち越さない）。
        _impPaidBase = _impPaidOut = 0;
        _impLedgerTotalMul = _impLedgerReplayMul = float.NaN;
    }

    // 台帳の差分を実際に入金する。Impression/RunImpression への反映は GainImpression と同じ
    // （倍率は台帳側で適用済みなので、ここでは加算だけを直接行う）。
    private void PayShardImpression()
    {
        float total = TotalImpressionMul, replay = ReplayMul;
        // 倍率が動いたら、それまでの台帳は確定済みとして畳む（過去分の再評価を避ける）。
        if (total != _impLedgerTotalMul || replay != _impLedgerReplayMul)
        {
            _impPaidBase = _impPaidOut = 0;
            _impLedgerTotalMul = total;
            _impLedgerReplayMul = replay;
        }
        long theory = ImpTheory(_impPaidBase + _impCarry, total, replay);
        long pay = theory - _impPaidOut;
        _impPaidBase += _impCarry;
        _impPaidOut = theory;
        _impCarry = 0;
        if (pay == 0) return;
        Impression += pay;
        RunImpression += pay;
    }

    // ───── 祈り手（Heal）の浄化ドレイン（設計書 §2）─────
    //   雑魚を DrainPerLife 体（=24）浄化するごとに ♥+1。ボム由来の浄化も数える
    //   （ボムは既にボムキャップで報酬が絞られており、ここまで塞ぐと回復の入口が細くなりすぎる）。
    //   ★満タン時はカウンタを進めない：「満タンのうちに貯めておいて、削られてから一気に戻す」
    //     という最適化を潰すため（設計書の明示要求）。AddLife が false を返した＝増えなかった場合も
    //     同じ扱いにすると「上限で捨てた1回」を数えたことになるので、先に満タン判定で弾く。
    private int _purifyDrain;
    public int PurifyDrainCount => _purifyDrain;   // HUD/デバッグ表示用（現在の貯まり）
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
            (GetTree().GetFirstNodeInGroup("hud") as Hud)?.ShowBanner("♥ +1");
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
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.ShowBanner("BOMB +1");
        GD.Print($"[JOB] heal break: BOMB+1 (bombs={Bombs}/{StartBombs})");
    }

    // 敵弾をかすった（グレイズ）時の加点。
    // 2026-09-06（docs/20260906/HUD整理_案.md §7）でスコア加算だけに絞った。やさしさ／インプレ／
    // コンボ猶予リフレッシュは剥がし、そのぶんスコアを 10→30 に上げている（副次効果を全部抜くと
    // 旨味が 1/4 になり「引き撃ちが最適解」に戻るため）。GrazeCount は統計用に維持。
    //   farming対策：被弾直後の無敵中(_hitInvincible)は呼び出し元(Player.OnGrazeAreaEntered)で既にスキップ済み。
    public void AddGraze()
    {
        Score += 30;
        GrazeCount++;
    }

    // 回避（ドッジ）の無敵中に敵弾をかすめてよけた時の高報酬（§リスクとリターン）。
    // 通常グレイズ(Score+30・お金なし)より大きめ＝回避クールダウン0.8sを切って敵弾に突っ込むリスク相応。
    //   ・スコアは DodgeGrazeScore。
    //   ・お金（インプレ＝ショップ通貨）を GainImpression(DodgeGrazeImpBase) で稼ぐ。倍率は内部で自動適用。
    //     実加算額を返す＝ポップアップ「+N」表示に使う。
    //   ・コンボ猶予をリフレッシュ＝攻めが途切れない。
    // やさしさ加算だけは 2026-09-06 のゲージ撤去で外した（指揮官決定＝スコア/インプレ/猶予は残す）。
    // farming上限は呼び出し側(Player)が1回避ごとにカウントして制御する。
    public long AddDodgeGraze()
    {
        Score += DodgeGrazeScore;
        if (Combo > 0)
            _comboTimer = ComboWindow;
        return GainImpression(DodgeGrazeImpBase);
    }
    private const int DodgeGrazeScore = 50;   // 回避よけ1発のスコア
    private const int DodgeGrazeImpBase = 2;  // 回避よけ1発の基礎インプレ（倍率は GainImpression 内で適用。稼ぎすぎ是正で 4→2）

    // ボムで敵弾を消した時の小加点。
    public void AddBulletCleared()
    {
        Score += 5;
    }

    // こはる戦の「祈り弾」（消せる下方向弾）を自機弾で受け止めた時の加点（#12 機構側／#20）。
    public void AddPrayerCleared()
    {
        Score += 15;
    }

    // 病みポスト（層2 の投稿チップ）を撃って「届けた」。
    //   正典: wiki/08_仮台本/10_病みポストを見つける_設計案.md（ユーザー承認済み・2026-09-05）の案A・経済の表。
    //   スコア +40／インプレ基礎 3（倍率は GainImpression 内）。
    //   撃ち漏らしには罰を置かない（汚染加算なし＝見逃しを数値で責めない）。
    public void AddPostDelivered()
    {
        Score += 40;
        GainImpression(3);              // ショップ通貨（RunImpression）に載る
        PostsDelivered++;
    }
    // 今ランで届けた病みポストの数（クリアバナー／帰還会話の集計語彙用。セーブしない）。
    public int PostsDelivered { get; private set; }

    // 祈りの帳（veil_light）の光輪が弾を受け止めた時の加点。ボム消し（Score+5）と同格。
    public void AddVeilCleared()
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
    public void ResetRun()
    {
        // 前のランで閾値未満のまま残った欠片インプレを、捨てずに先に入金してから締める
        // （ゲームオーバー／シーン遷移で消える経路の受け皿。RunImpression のリセットより前に呼ぶ）。
        FlushShardImpression();
        Score = _bossRetryScore;
        _bossRetryScore = 0;
        Combo = 0;
        _comboTimer = 0;
        Bombs = StartBombs;
        _bombPurifyCount = 0;
        PurifiedCount = 0;
        _purifyDrain = 0;     // 祈り手の浄化ドレインもラン単位（面を跨いで持ち越さない）
        RunHitCount = 0;      // 被弾回数（ハブ帰還の「被弾は{n}回」）もラン単位
        _progAccum = 0f;      // 前のめり進行アキュムレータもラン開始でリセット
        PlayerNormX = 0.5f;   // 自機Xは中央からとみなす（初フレーム前の背景/HUD 参照用）
        RunImpression = 0;
        _focusModeT = 0f;     // 集中モードの持続/CD もラン単位（前の面の残りを持ち込まない）
        _focusModeCd = 0f;
        PostsDelivered = 0;   // 届けた病みポストの数もラン単位
        RedemptionActive = false;
    }

    public void PrepareBossRetry(bool bossCheckpoint = true)
    {
        // Scene reload calls ResetRun; carry the remaining half into that reset exactly once.
        _bossRetryScore = Score / 2;
        SelectedEntry = bossCheckpoint ? StageEntry.Boss : StageEntry.Start;
    }

    // 改心演出中か。本戦ボスの OnCryStart が立て、次のラン開始（ResetRun）で下りる。
    // 残機0と同フレーム帯で飛翔中の弾がボスを浄化したエッジケースで、ゲームオーバーの
    // 「R/Q」抜けプロンプトを改心演出〜帰還会話に重ねないための判定（HandleGameOverExit が参照）。
    public bool RedemptionActive { get; private set; }
    public void NotifyRedemptionStart() => RedemptionActive = true;

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
    // 選択肢の並び。沈黙の自動決定は末尾が選ばれるので、末尾は最も害の小さい「抜ける」にする
    //（ChoiceOverlay の既定挙動＝呼び出し側が引き下がる側を最後に置く約束）。
    private static readonly string[] GameOverChoices =
    {
        "（ボスから・スコア半分消費）",
        "（最初からやり直す）",
        "（ステージから抜ける）",
    };

    // 戻り値 true ＝画面遷移を実行した（呼び元はそれ以降の処理を打ち切ってよい）。
    public static bool HandleGameOverExit(Node root, Hud? hud, ref bool exitHeld)
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
            _gameOverChoice = ChoiceOverlay.Show(hud, GameOverChoices,
                defaultSel: 0, onBoard: true);   // 既定は「ボスからやり直す」＝いちばん続けやすい手
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
            hud.ShowGameOverTitle("くじけちゃった…");
            hud.ShowGameOverPrompt(Pad.ShowKeyboard
                ? "R：ボスからやり直す　／　Shift+R：最初から　／　Q：抜ける"
                : $"{Pad.Face(JoyButton.B)}：抜ける");
        }

        // 既存キー：Q／パッドB＝抜ける（従来どおり即発）。R 系は各 *Root.cs が持っている。
        bool exit = Input.IsKeyPressed(Key.Q) || Pad.Pressed(JoyButton.B);
        bool fired = exit && !exitHeld;
        exitHeld = exit;
        if (fired) { ExitToHub(root, game, hud); return true; }

        // 選択が決まったら、その行の処理へ。
        if (!_gameOverChoice.Decided) return false;
        int sel = _gameOverChoice.Selected;
        // 3択はいずれもシーンが変わる（やり直し2つ＝ReloadCurrentScene / 抜ける＝Hubへ）ので、
        //   曲は遷移先の _Ready に任せる＝ここでは道中曲へ戻さない。
        ClearGameOverChoice(hud, restoreMusic: false);
        switch (sel)
        {
            case 0:   // ボスからやり直す＝R 単体と同じ経路（SelectedEntry を Boss にしてシーン再読込）
                game?.PrepareBossRetry(bossCheckpoint: root is AkariRoot or KoharuRoot or ReiRoot);
                root.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                root.GetTree().ReloadCurrentScene();
                return true;
            case 1:   // 最初からやり直す＝Shift+R と同じ経路（SelectedEntry に触らない）
                root.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                root.GetTree().ReloadCurrentScene();
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

    // 抜ける：ランで貯めたお金（インプレ）は恒久値。抜けても破棄せず、確実に保存してから帰還。
    private static void ExitToHub(Node root, GameManager? game, Hud? hud)
    {
        // 直後に Hub へ遷移し Hub._Ready が BgmMenu を張るので、ここで道中曲へ戻さない
        //   （戻すと遷移の直前に一瞬だけ道中曲が立ち上がって無駄なクロスフェードになる）。
        ClearGameOverChoice(hud, restoreMusic: false);
        game?.AutoSave();
        root.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        Audio.Instance?.PlayUiCancel();
        root.GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    // ───────── シーン遷移の暗幕（2026-09-22）─────────
    // ChangeSceneToFile は**その場で**現シーンを捨てて次を建てるので、切り替えの1フレームに
    //   直前の画面（＝ボス撃破後もボス背景が出たままのステージ。StageBackground は EnterBoss 後
    //   Mode.Boss から戻らない）がそのまま映り、ボスのイラストがフラッシュして見えていた。
    //   ユーザー実機指摘「ステージ切り替えとかで（ボスのステージイラストが）表示される」。
    //
    // 対処：遷移の前に全画面の黒を張って**暗転しきってから**シーンを差し替える。
    //   ・幕は GameManager（Autoload）が自前の CanvasLayer に持つ＝シーンの解放に巻き込まれない。
    //     呼び出し側のシーンに ColorRect を生やすと ChangeSceneToFile で一緒に消えて意味が無い。
    //   ・新シーンの _Ready が走ったあとに幕を引く（明転）。遷移先が自前のフェードインを持つ画面
    //     （Prologue 等）でも、黒→絵 の順序は壊れない。
    //   ・Layer を極端に上げて PauseMenu/Hud より手前に置く。ProcessMode=Always＝ツリー停止中でも進む。
    private SceneCurtain _curtainLayer = null!;
    private ColorRect _curtain = null!;
    private string _curtainDest = "";
    private double _curtainT;
    private int _curtainPhase;   // 0=休止 1=暗転中 2=遷移直後の明転
    private const double CurtainFall = 0.24, CurtainRise = 0.3;

    // 暗転してからシーンを切り替える。dest は res:// のシーンパス。
    //   既に暗転中なら二重には受けない（連打・二重遷移の保険）。
    public void FadeToScene(string dest)
    {
        if (_curtainPhase == 1) return;
        _curtainDest = dest;
        _curtainPhase = 1;
        _curtainT = 0;
        EnsureCurtain();
        _curtain.Visible = true;
    }

    // 呼び出し側が Node しか持っていない場所からの入り口（静的ヘルパ）。
    //   Autoload が居なければ従来どおり即遷移する＝幕のために進行を止めない。
    public static void FadeToScene(Node from, string dest)
    {
        var game = Instance ?? from.GetNodeOrNull<GameManager>("/root/Game");
        if (game != null) game.FadeToScene(dest);
        else from.GetTree().ChangeSceneToFile(dest);
    }

    private void EnsureCurtain()
    {
        if (IsInstanceValid(_curtainLayer)) return;
        // 幕は GameManager 自身の _Process ではなく、この CanvasLayer が自前で進める。
        //   カットシーン（StoryFilm/MinaPhaseScene）は GameManager.ProcessMode を Disabled に落とすので、
        //   GameManager の _Process に相乗りすると暗転の途中で幕が凍りつく可能性がある。
        _curtainLayer = new SceneCurtain { Name = "SceneCurtain", Layer = 256, Owner_ = this };
        AddChild(_curtainLayer);
        _curtain = new ColorRect
        {
            Name = "Curtain",
            Color = new Color(0, 0, 0, 0),
            Size = new Vector2(384, 216),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _curtainLayer.AddChild(_curtain);
        _curtainLayer.ProcessMode = ProcessModeEnum.Always;
    }

    // SceneCurtain が毎フレーム呼ぶ。暗転しきったフレームで ChangeSceneToFile し、次から明転へ。
    internal void TickCurtain(double delta)
    {
        if (_curtainPhase == 0) return;
        _curtainT += delta;
        if (_curtainPhase == 1)
        {
            float k = Mathf.Clamp((float)(_curtainT / CurtainFall), 0, 1);
            _curtain.Color = new Color(0, 0, 0, k);
            if (k < 1) return;
            // 暗転しきった。ここで初めてシーンを差し替える＝切り替わりの瞬間は黒一色。
            GetTree().ChangeSceneToFile(_curtainDest);
            _curtainPhase = 2;
            _curtainT = 0;
            return;
        }
        // 明転。新シーンの _Ready はもう走っている。
        float r = Mathf.Clamp((float)(_curtainT / CurtainRise), 0, 1);
        _curtain.Color = new Color(0, 0, 0, 1 - r);
        if (r < 1) return;
        _curtain.Visible = false;
        _curtainPhase = 0;
    }
}

// シーン遷移の暗幕を載せる CanvasLayer。幕の時間を**自分で**進めるためだけに _Process を持つ。
//   GameManager._Process に相乗りしない理由は EnsureCurtain のコメント参照
//   （カットシーンが GameManager.ProcessMode を Disabled に落とすため）。
public partial class SceneCurtain : CanvasLayer
{
    public GameManager Owner_ = null!;
    public override void _Process(double delta) => Owner_?.TickCurtain(delta);
}
