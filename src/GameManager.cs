using Godot;
using System.Collections.Generic;

// GameManager : Autoload シングルトン (/root/Game)。
// スコア・コンボ・ボム数などのゲーム状態を一元管理する。
// メタ進行（インプレッション経済 / フォロワー / 恒久強化）と user:// セーブもここに集約。
//   - 経済設計: docs/20260613/MINA_システム拡張設計書_v1.md ①章
//   - 恒久強化は被弾・リトライ・汚染で溶けない（§0-3）。セーブにのみ依存して永続。
public partial class GameManager : Node
{
    public long Score { get; private set; }
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
    public ShotMode SelectedShotMode = ShotMode.Rapid; // 最後に選んだモード（Save 対象・起動時復元）
    // トレーニング場（TrainingRoot）が立てる：この間だけ全モードを解放し、V の切替ローテに入れる。
    public bool TrainingMode;
    // 拡散/ホーミング/加速球の解放判定は各系統の入り口ノード（spread_1 / homing_1 / accel_1）の所持で決まる（単Lvノード方式）。
    public bool HasSpread => GetUpgradeLevel("spread_1") >= 1;
    public bool HasHoming => GetUpgradeLevel("homing_1") >= 1;
    public bool HasAccel => GetUpgradeLevel("accel_1") >= 1;
    // 拡散の本数 5→7→9 ／ ホーミングの追尾数 2→3→4（連続所持段数 ChainLevel に対応）。
    public int SpreadWays => new[] { 0, 5, 7, 9 }[Mathf.Clamp(ChainLevel("spread", 3), 0, 3)];
    public int HomingShots => new[] { 0, 2, 3, 4 }[Mathf.Clamp(ChainLevel("homing", 3), 0, 3)];
    public bool IsModeUnlocked(ShotMode m) => m switch
    {
        ShotMode.Spread => HasSpread,
        ShotMode.Homing => HasHoming,
        // 加速球：加速球解放ノードを購入済みなら通常プレイでも解放（拡散/ホーミングと同流儀）。トレーニング中は全解放。
        ShotMode.Accel => TrainingMode || HasAccel,
        _ => true,
    };
    // 解放済みモードを循環（連射→拡散→ホーミング→加速球→連射…・未解放はスキップ）。
    private const int ModeCount = 4;
    public ShotMode NextUnlockedMode(ShotMode cur)
    {
        for (int i = 1; i <= ModeCount; i++)
        {
            var m = (ShotMode)(((int)cur + i) % ModeCount);
            if (IsModeUnlocked(m)) return m;
        }
        return ShotMode.Rapid;
    }
    public string ShotModeName(ShotMode m) => m switch { ShotMode.Spread => "拡散", ShotMode.Homing => "ホーミング", ShotMode.Accel => "加速球", _ => "連射" };
    // 残機・ボムは難易度ベース ＋ 恒久強化ボーナス。
    // Lunaticは弾密度(BulletCountMul=1.9、Hard比+73%)・弾速(1.18)・間隔(0.85)全てが全難易度中最厳。
    // Easy(6)→Normal(4)→Hard(3)の減り方（-2,-1）に沿って Hard→Lunatic も -1 段階減らし、
    // 最終ティアの「賭け金」をリターン（Lunatic解禁自体がフォロワー200等のやり込み到達点）に見合わせる。
    // 恒久強化(max_life/bomb_count、ChainLevel上限+2)を乗せて初めて現実的に戦える設計は維持（②-4想定通り）。
    public int StartLives => BaseLivesFor(Difficulty) + MaxLifeBonus;
    public int StartBombs => BaseBombsFor(Difficulty) + BombCountBonus;
    // DiffSelect（難易度選択画面）が「今の選択」ではなく各ティア個別の基礎値を並べて見せられるよう、
    // Difficultyに依存しない静的版を用意（恒久強化ボーナスは含めない＝難易度そのものの賭け金のみ）。
    public static int BaseLivesFor(Diff d) => d switch { Diff.Easy => 6, Diff.Hard => 3, Diff.Lunatic => 2, _ => 4 };
    public static int BaseBombsFor(Diff d) => d switch { Diff.Easy => 6, Diff.Hard => 3, Diff.Lunatic => 2, _ => 4 };
    public float BulletSpeedMul => Difficulty switch { Diff.Easy => 0.62f, Diff.Hard => 1.05f, Diff.Lunatic => 1.18f, _ => 0.85f };
    // 難易度は敵の体力ではなく「弾の数」で調整する（やさしいほど弾が少ない）。
    public float BulletCountMul => Difficulty switch { Diff.Easy => 0.38f, Diff.Hard => 1.1f, Diff.Lunatic => 1.9f, _ => 0.7f };
    public float DanmakuIntervalMul => Difficulty switch { Diff.Easy => 2.1f, Diff.Hard => 1.0f, Diff.Lunatic => 0.85f, _ => 1.35f };
    public string DiffName => Difficulty switch { Diff.Easy => "EASY", Diff.Hard => "HARD", Diff.Lunatic => "LUNATIC", _ => "NORMAL" };

    // ボスHPバー本数（言葉のシールド＋無防備窓リワーク）。1本=BarHp(=100)で、総HP=本数×BarHp。
    // 難易度で本数が増える＝堅くなる（弾数調整とは別軸の「殴る回数」調整）。
    // 通常ボス: Easy2/Normal4/Hard5/Lunatic6（#25: Easyは据え置き＝入口を守り、Normal以上を+1本）。
    // ラスボス格(Mina)は +2本（finalBoss=true。Easy4/Normal6/Hard7/Luna8。B-5: 強化が伸びた終盤でも
    // シールド段の攻防が痩せないよう +1→+2）。無防備窓のキャップは据え置き。
    public int DiffBarBonus(bool finalBoss) =>
        (Difficulty switch { Diff.Easy => 2, Diff.Hard => 5, Diff.Lunatic => 6, _ => 4 }) + (finalBoss ? 2 : 0);

    // ルナティック解禁条件（①-9）：フォロワーが一定 or 主要火力強化が一定段階。
    public const int LunaticFollowerReq = 200;
    public bool IsLunaticUnlocked => Followers >= LunaticFollowerReq || ChainLevel("shot_power", 4) >= 4;

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
    // 「初めて中ボスを倒した」瞬間にショップ説明へ離脱し、完了後 true にする。以降の中ボス撃破では離脱しない。
    public bool ShopTutorialSeen;

    // 弾幕の本数を難易度でスケール（最低1発は残す）。各ボスのリング/扇の本数に掛ける。
    public int ScaleBullets(int baseCount) => Mathf.Max(1, Mathf.RoundToInt(baseCount * BulletCountMul));

    // 累計浄化数。
    public int PurifiedCount { get; private set; }
    // 累計グレイズ（かすり）数。加算のみで現状は読み手なし（将来グレイズチュートリアルが実装されれば読み手になり得る）。
    public int GrazeCount { get; private set; }

    // ステージ目標：このタイムラインを浄化しきる人数。到達でステージクリア。
    public int StageTarget { get; private set; } = 24;
    public void SetStageTarget(int t) => StageTarget = Mathf.Max(1, t);

    // ───── 前のめり進行（リスクリターン：時間＋撃破＋自機の左右位置）─────
    //   進行度＝「撃破率」を下限に、時間アキュムレータを右へ寄るほど速く積む。
    //   ・killFrac  ＝撃破率（従来の式。下限保証＝ここが痩せることはない）。
    //   ・progAccum ＝時間アキュムレータ。posFactor(playerX) で毎フレーム積む（右ほど速い）。
    //   進行不能防止の不変条件（絶対に壊さない）：
    //     ・道中ウェーブの撃破ゲート／StageCleared は PurifiedCount だけで判定＝ここは一切触らない。
    //       progAccum/timeFrac は「見た目のゲージ＝StageProgress／Warmth／背景切替」を先行させるだけ。
    //     ・時間だけではクリアさせない（ボスは必ず撃破required）。
    //   progAccum は ResetRun（ステージ開始）でリセット。
    private float _progAccum;
    private const float ProgBaseRate = 1f / 95f; // 中央基準の進行速度（posFactor=1 で 95 秒フル）
    private const float ProgTimeCap = 0.35f;     // 時間だけで伸ばせる上限
    // 自機Xの正規化（0=左端 / 0.5=中央 / 1=右端）。プレイフィールドは 384 幅（Player.MinX..MaxX）。
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
        PlayerNormX = Mathf.Clamp(playerX / 384f, 0f, 1f);
        _progAccum += ProgBaseRate * PosFactor(PlayerNormX) * (float)dt;
        _progAccum = Mathf.Clamp(_progAccum, 0f, ProgTimeCap);
    }

    // 浄化ゲージ(0..1)＝目標までの達成度。世界の暖かさもこれに連動する。
    //   撃破率(killFrac)を下限に、時間ぶん(timeFrac)を混ぜて“前のめり”に先行させる（合成は設計確定式）。
    public float StageProgress
    {
        get
        {
            float killFrac = Mathf.Clamp((float)PurifiedCount / StageTarget, 0f, 1f);
            float timeFrac = _progAccum; // TickProgress で 0..ProgTimeCap に clamp 済み
            return Mathf.Clamp(Mathf.Max(killFrac, killFrac * 0.55f + timeFrac), 0f, 1f);
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
        new() { Id = "akari",  Scene = "res://Akari.tscn",  Handle = "@akari.",   Tweet = "すき、すき、すき。……ひとつでいいから、本物になって。",   Title = "STAGE 1 — あかり" },
        new() { Id = "koharu", Scene = "res://Koharu.tscn", Handle = "@koharu",   Tweet = "ぜんぶ食べてね。のこしちゃだめ。……そしたら、いなくならないでしょ?", Title = "STAGE 2 — こはる" },
        new() { Id = "rei",    Scene = "res://Rei.tscn",    Handle = "@rei_____", Tweet = "だれも、わたしには追いつけない。……それの、なにが、いけないの。", Title = "STAGE 3 — レイ" },
    };

    // シーンパス → ステージID（DiffSelect が選択中ステージの解放ゲートを引くのに使う）。未登録は null。
    public static string? StageIdForScene(string scene)
    {
        foreach (var s in Stages)
            if (s.Scene == scene) return s.Id;
        return null;
    }
    // 中ボス(cameo)を持つ＝チェックポイント入口を出す対象ステージか（レイ/あかり/こはる）。
    public static bool StageHasMidBoss(string id) => id is "rei" or "akari" or "koharu";

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
    public void ResetIdleDialogSeen() => _idleDialogSeen.Clear();
    public bool IsStageCleared(string id) => _cleared.Contains(id);
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

    // ─── 炎上（②-5 / ③-6）───
    //   発生は一度きり（STAGE2クリア後）。次のダイブ1ステージだけ弱体化（発射↓/移動↓/インプレ×0.6）。
    public bool Burning;          // 炎上発生済みで未消費（次のダイブで適用）
    public bool BurningThisRun;   // 現在のステージrunが炎上下か（Player/Hudが参照）
    private bool _burnHappened;    // 一度きりのストーリーイベント済みか

    // ─── 会話選択（層2プロト）───
    //   STAGE3 MidStory の2択で A「もういちど、聞く」を選んだ（＝もう一度踏み込んだ）。
    //   下流2場面（StageKoharu.Clear の1行／Epilogue 独白の1行）の変種差し替えにだけ使う収束型フラグ。
    public bool PressedTheQuestion;
    public bool ShouldBurnAfter(string clearedStageId) => clearedStageId == "akari" && !_burnHappened;
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

    // 恒久強化レベル（id → 現在Lv）。未所持は 0。単Lvノード方式では値は常に 0/1。
    // セーブ移行（MigrateUpgradesIfLegacy）で丸ごと差し替えるため readonly にはしない。
    private Dictionary<string, int> _upgrades = new();

    // 強化カタログ（§①-4）。効果は下の各アクセサで定義。
    // 二分木ディシジョンツリー：単一ルート「ミナの核」から 1→2→4→7→6 と二個ずつ広がる（Shop が描画）。
    //   ・親条件（ParentId）＝ノードに入る（Lv0→1）ときだけ親Lv≥1 を要求。続きLvは親不要。
    //   ・排他フォーク（ExclusiveWith）＝対の片方をLv1にするともう片方は封印（振り直しで解除可）。
    //   ・前提（Prereq）は奥義の解放条件（従来どおり次のLv購入時のみ判定＝グランドファーザー規則）。
    public sealed class UpgradeDef
    {
        public string Id = "";
        public string Name = "";
        public string Desc = "";
        public int MaxLevel;
        public long BaseCost;
        public float CostMul; // 次レベルの価格は BaseCost * CostMul^(現Lv-1)（Lv0→1 は一律100）
        // ツリー前提（奥義のみ設定）。PrereqId 非空なら「PrereqId が PrereqLv 以上」で購入可。
        // 判定は“次の Lv を買う瞬間”のみ＝前提未達でも所持済み Lv は没収・無効化しない（グランドファーザー規則）。
        public string PrereqId = "";
        public int PrereqLv;
        public bool Capstone; // 奥義。Lv0→1 の一律100を適用しない（Lv1=BaseCost・以降 ×CostMul）
        // 二分木の親（""＝ルート直結）。Lv0→1 の購入時のみ「親Lv≥1」を要求する。
        public string ParentId = "";
        // 排他フォークの相方（""＝排他なし）。相方Lv≥1 かつ自分Lv0 なら封印（IsSealed）。
        public string ExclusiveWith = "";
    }

    // ───── 単Lvノード方式（ショップ大改修フェーズ1）─────
    //   MaxLv方式を廃止し、各レベルを独立した買い切りノード（MaxLevel=1・固定BaseCost）に外出しした。
    //   ・排他は全撤廃（ExclusiveWith 全空）＝どのノードも買い切りで没収なし。
    //   ・Prereq は「順序の道しるべ」としてのみ残す（グランドファーザー規則：所持済み Lv は前提未達でも有効）。
    //   ・効果値は ChainLevel(prefix, n)（連続所持段数）で従来式に読み替える（式は不変・Lv取得元だけ差替え）。
    //   ・価格は旧 CostAt の各段から移送（総額据え置き）。圧縮系(3→2)は末尾段効果を旧最終Lv相当に補償。
    //   座標(x,y)は Shop 側の NodePos（フェーズ2でカメラ表示）と対応。ParentId は木の骨格・道しるべ。
    public static readonly UpgradeDef[] Upgrades =
    {
        // ── 連射系 ──
        new() { Id = "fire_rate_1",   Name = "連射速度I",   Desc = "発射間隔 ×0.92",           MaxLevel = 1, BaseCost = 100,  ParentId = "" },
        new() { Id = "fire_rate_2",   Name = "連射速度II",  Desc = "発射間隔 ×0.84",           MaxLevel = 1, BaseCost = 350,  ParentId = "fire_rate_1" },
        new() { Id = "fire_rate_3",   Name = "連射速度III", Desc = "発射間隔 ×0.76",           MaxLevel = 1, BaseCost = 462,  ParentId = "fire_rate_2" },
        new() { Id = "fire_rate_4",   Name = "連射速度IV",  Desc = "発射間隔 ×0.68",           MaxLevel = 1, BaseCost = 610,  ParentId = "fire_rate_3" },
        new() { Id = "shot_power_1",  Name = "光の出力I",   Desc = "届ける光の威力 +1",        MaxLevel = 1, BaseCost = 100,  ParentId = "fire_rate_1" },
        new() { Id = "shot_power_2",  Name = "光の出力II",  Desc = "届ける光の威力 +2",        MaxLevel = 1, BaseCost = 400,  ParentId = "shot_power_1" },
        new() { Id = "shot_power_3",  Name = "光の出力III", Desc = "届ける光の威力 +3",        MaxLevel = 1, BaseCost = 540,  ParentId = "shot_power_2" },
        new() { Id = "shot_power_4",  Name = "光の出力IV",  Desc = "届ける光の威力 +4（LUNATIC解放条件のひとつ）", MaxLevel = 1, BaseCost = 729,  ParentId = "shot_power_3" },
        new() { Id = "rapid_power_1", Name = "連射威力I",   Desc = "連射モードの弾威力 +1",    MaxLevel = 1, BaseCost = 100,  ParentId = "shot_power_2" },
        new() { Id = "rapid_power_2", Name = "連射威力II",  Desc = "連射モードの弾威力 +2",    MaxLevel = 1, BaseCost = 450,  ParentId = "rapid_power_1" },
        new() { Id = "rapid_rate_1",  Name = "速射I",       Desc = "連射モードの間隔 ×0.94",   MaxLevel = 1, BaseCost = 100,  ParentId = "fire_rate_3" },
        new() { Id = "rapid_rate_2",  Name = "速射II",      Desc = "連射モードの間隔 ×0.88",   MaxLevel = 1, BaseCost = 400,  ParentId = "rapid_rate_1" },
        new() { Id = "pierce_1",      Name = "貫く光I",     Desc = "連射弾が敵1体を貫通する",  MaxLevel = 1, BaseCost = 800,  ParentId = "rapid_power_1", PrereqId = "shot_power_2", PrereqLv = 1 },
        new() { Id = "pierce_2",      Name = "貫く光II",    Desc = "連射弾が敵2体を貫通する",  MaxLevel = 1, BaseCost = 1280, ParentId = "pierce_1" },
        new() { Id = "focus_1",       Name = "集中の光I",   Desc = "同じ敵に当て続けて威力 最大+1", MaxLevel = 1, BaseCost = 800,  ParentId = "rapid_power_1" },
        new() { Id = "focus_2",       Name = "集中の光II",  Desc = "同じ敵に当て続けて威力 最大+2", MaxLevel = 1, BaseCost = 1280, ParentId = "focus_1" },
        // ── 拡散系 ──
        new() { Id = "spread_1",      Name = "拡散展開I",   Desc = "拡散モード解放・5way",     MaxLevel = 1, BaseCost = 100,  ParentId = "fire_rate_1" },
        new() { Id = "spread_2",      Name = "拡散展開II",  Desc = "拡散 7way",                MaxLevel = 1, BaseCost = 500,  ParentId = "spread_1" },
        new() { Id = "spread_3",      Name = "拡散展開III", Desc = "拡散 9way",                MaxLevel = 1, BaseCost = 690,  ParentId = "spread_2" },
        new() { Id = "spread_power_1",Name = "拡散威力I",   Desc = "拡散弾の威力 ×0.56",       MaxLevel = 1, BaseCost = 100,  ParentId = "spread_1" },
        new() { Id = "spread_power_2",Name = "拡散威力II",  Desc = "拡散弾の威力 ×0.62",       MaxLevel = 1, BaseCost = 420,  ParentId = "spread_power_1" },
        new() { Id = "spread_rate_1", Name = "拡散速射I",   Desc = "拡散モードの間隔税 ×1.35", MaxLevel = 1, BaseCost = 100,  ParentId = "spread_2" },
        new() { Id = "fol_gain_1",    Name = "口コミI",     Desc = "口コミ ×1.15（フォロワー獲得効率UP）", MaxLevel = 1, BaseCost = 100,  ParentId = "spread_1" },
        new() { Id = "fol_gain_2",    Name = "口コミII",    Desc = "口コミ ×1.30",             MaxLevel = 1, BaseCost = 300,  ParentId = "fol_gain_1" },
        new() { Id = "combo_hold_1",  Name = "コンボ持続I", Desc = "コンボ猶予 2.4秒",         MaxLevel = 1, BaseCost = 100,  ParentId = "move_speed_1" },
        new() { Id = "combo_hold_2",  Name = "コンボ持続II",Desc = "コンボ猶予 2.8秒",         MaxLevel = 1, BaseCost = 200,  ParentId = "combo_hold_1" },
        new() { Id = "option_1",      Name = "拡散サブI",   Desc = "追従オプション +1（威力×0.5でメイン同期射撃）", MaxLevel = 1, BaseCost = 900,  ParentId = "spread_power_1", PrereqId = "spread_2", PrereqLv = 1 },
        new() { Id = "option_2",      Name = "拡散サブII",  Desc = "追従オプション 2基",       MaxLevel = 1, BaseCost = 1440, ParentId = "option_1" },
        new() { Id = "chain_1",       Name = "連鎖の光I",   Desc = "拡散弾が1回跳弾（威力×0.4）", MaxLevel = 1, BaseCost = 800,  ParentId = "fol_gain_2", PrereqId = "spread_2", PrereqLv = 1 },
        new() { Id = "chain_2",       Name = "連鎖の光II",  Desc = "拡散弾が2回跳弾",          MaxLevel = 1, BaseCost = 1280, ParentId = "chain_1" },
        // ── ホーミング系 ──
        new() { Id = "homing_1",      Name = "誘導の祈りI", Desc = "ホーミングモード解放・2体追尾", MaxLevel = 1, BaseCost = 100,  ParentId = "move_speed_1" },
        new() { Id = "homing_2",      Name = "誘導の祈りII",Desc = "3体追尾",                 MaxLevel = 1, BaseCost = 550,  ParentId = "homing_1" },
        new() { Id = "homing_3",      Name = "誘導の祈りIII",Desc = "4体追尾",                 MaxLevel = 1, BaseCost = 825,  ParentId = "homing_2" },
        new() { Id = "homing_power_1",Name = "誘導威力I",   Desc = "ホーミング弾の威力 ×0.95", MaxLevel = 1, BaseCost = 100,  ParentId = "homing_1" },
        new() { Id = "homing_power_2",Name = "誘導威力II",  Desc = "ホーミング弾の威力 ×1.05", MaxLevel = 1, BaseCost = 480,  ParentId = "homing_power_1" },
        new() { Id = "homing_rate_1", Name = "誘導速射I",   Desc = "ホーミングの間隔税 ×1.40・旋回200", MaxLevel = 1, BaseCost = 100,  ParentId = "homing_2" },
        new() { Id = "counter_1",     Name = "返し光I",     Desc = "回避よけした弾を追尾光弾へ（2発に1発）", MaxLevel = 1, BaseCost = 800,  ParentId = "homing_power_1", PrereqId = "homing_2", PrereqLv = 1 },
        new() { Id = "counter_2",     Name = "返し光II",    Desc = "回避よけした弾を全弾光弾化", MaxLevel = 1, BaseCost = 1280, ParentId = "counter_1" },
        new() { Id = "veil_1",        Name = "祈りの帳I",   Desc = "回避後の弾消し光輪 r20px", MaxLevel = 1, BaseCost = 800,  ParentId = "homing_rate_1", PrereqId = "homing_2", PrereqLv = 1 },
        new() { Id = "veil_2",        Name = "祈りの帳II",  Desc = "弾消し光輪 r28px",         MaxLevel = 1, BaseCost = 1280, ParentId = "veil_1" },
        // ── 加速球系（ACCEL・独立ストリーム）。入り口 accel_1 でモード解放（拡散/ホーミングと同格の帯）。
        //    強化軸は加速球固有のメカニクス：威力／タメ短縮（0.8→0.65→0.5s）／発進速度（640→760）。
        //    親は move_speed_1（homing_1 と同じ流儀）。効果アクセサは AccelPowerBonus/AccelChargeDelay/AccelLaunchSpeed。
        new() { Id = "accel_1",       Name = "加速球",      Desc = "加速球モード解放・タメて撃つ→ロケット発進", MaxLevel = 1, BaseCost = 100,  ParentId = "move_speed_1" },
        new() { Id = "accel_power_1", Name = "加速威力I",   Desc = "加速球の威力 +1",          MaxLevel = 1, BaseCost = 100,  ParentId = "accel_1" },
        new() { Id = "accel_power_2", Name = "加速威力II",  Desc = "加速球の威力 +2",          MaxLevel = 1, BaseCost = 450,  ParentId = "accel_power_1" },
        new() { Id = "accel_charge_1",Name = "速填I",       Desc = "タメ時間 0.65秒",          MaxLevel = 1, BaseCost = 100,  ParentId = "accel_1" },
        new() { Id = "accel_charge_2",Name = "速填II",      Desc = "タメ時間 0.5秒",           MaxLevel = 1, BaseCost = 420,  ParentId = "accel_charge_1" },
        new() { Id = "accel_speed_1", Name = "推進強化I",   Desc = "発進速度 640→760（ロケット強化）", MaxLevel = 1, BaseCost = 400,  ParentId = "accel_charge_1" },
        // ── バックファイア系（後方弾は bf_* 未所持でも初期から弱く発射される）──
        new() { Id = "bf_power_1",    Name = "後方威力I",   Desc = "後方弾の威力 +1",          MaxLevel = 1, BaseCost = 100,  ParentId = "move_speed_1" },
        new() { Id = "bf_power_2",    Name = "後方威力II",  Desc = "後方弾の威力 +2",          MaxLevel = 1, BaseCost = 380,  ParentId = "bf_power_1" },
        new() { Id = "bf_power_3",    Name = "後方威力III", Desc = "後方弾の威力 +3",          MaxLevel = 1, BaseCost = 532,  ParentId = "bf_power_2" },
        new() { Id = "bf_rate_1",     Name = "後方速射I",   Desc = "後方弾の間隔 0.7秒",        MaxLevel = 1, BaseCost = 100,  ParentId = "bf_power_1" },
        new() { Id = "bf_rate_2",     Name = "後方速射II",  Desc = "後方弾の間隔 0.55秒",       MaxLevel = 1, BaseCost = 420,  ParentId = "bf_rate_1" },
        new() { Id = "bf_track_1",    Name = "後方追尾I",   Desc = "後方弾の旋回90・同時2発",  MaxLevel = 1, BaseCost = 460,  ParentId = "bf_power_1" },
        // ── 生存・経済系 ──
        new() { Id = "move_speed_1",  Name = "身のこなしI", Desc = "移動速度UP＋回避のキレ",   MaxLevel = 1, BaseCost = 100,  ParentId = "" },
        new() { Id = "move_speed_2",  Name = "身のこなしII",Desc = "移動・回避 段2",           MaxLevel = 1, BaseCost = 250,  ParentId = "move_speed_1" },
        new() { Id = "move_speed_3",  Name = "身のこなしIII",Desc = "移動・回避 段3",          MaxLevel = 1, BaseCost = 350,  ParentId = "move_speed_2" },
        new() { Id = "contam_1",      Name = "澄んだ心I",   Desc = "汚染の上昇を抑え、心の効率を底上げ", MaxLevel = 1, BaseCost = 100,  ParentId = "move_speed_1" },
        new() { Id = "contam_2",      Name = "澄んだ心II",  Desc = "汚染耐性 段2（旧Lv3効果に補償）", MaxLevel = 1, BaseCost = 300,  ParentId = "contam_1" },
        new() { Id = "hitbox_1",      Name = "回避域I",     Desc = "被弾判定 ×0.88",           MaxLevel = 1, BaseCost = 100,  ParentId = "contam_1" },
        new() { Id = "hitbox_2",      Name = "回避域II",    Desc = "被弾判定 ×0.76",           MaxLevel = 1, BaseCost = 600,  ParentId = "hitbox_1" },
        new() { Id = "hitbox_3",      Name = "回避域III",   Desc = "被弾判定 ×0.64",           MaxLevel = 1, BaseCost = 930,  ParentId = "hitbox_2" },
        new() { Id = "imp_mult_1",    Name = "浄化倍率I",   Desc = "獲得心 ×1.12",             MaxLevel = 1, BaseCost = 100,  ParentId = "contam_1" },
        new() { Id = "imp_mult_2",    Name = "浄化倍率II",  Desc = "獲得心 ×1.24",             MaxLevel = 1, BaseCost = 300,  ParentId = "imp_mult_1" },
        new() { Id = "imp_mult_3",    Name = "浄化倍率III", Desc = "獲得心 ×1.36",             MaxLevel = 1, BaseCost = 435,  ParentId = "imp_mult_2" },
        new() { Id = "imp_mult_4",    Name = "浄化倍率IV",  Desc = "獲得心 ×1.48",             MaxLevel = 1, BaseCost = 631,  ParentId = "imp_mult_3" },
        new() { Id = "max_life_1",    Name = "最大♥I",      Desc = "ライフ上限 +1",            MaxLevel = 1, BaseCost = 100,  ParentId = "move_speed_2" },
        new() { Id = "max_life_2",    Name = "最大♥II",     Desc = "ライフ上限 +2",            MaxLevel = 1, BaseCost = 550,  ParentId = "max_life_1" },
        new() { Id = "bomb_count_1",  Name = "ボム所持I",   Desc = "初期ボム +1",              MaxLevel = 1, BaseCost = 100,  ParentId = "max_life_1" },
        new() { Id = "bomb_count_2",  Name = "ボム所持II",  Desc = "初期ボム +2",              MaxLevel = 1, BaseCost = 450,  ParentId = "bomb_count_1" },
        new() { Id = "bomb_power_1",  Name = "ボム威力I",   Desc = "ボム直撃が穢れを深く祓う 段1", MaxLevel = 1, BaseCost = 100,  ParentId = "max_life_1" },
        new() { Id = "bomb_power_2",  Name = "ボム威力II",  Desc = "ボム直撃 段2",             MaxLevel = 1, BaseCost = 350,  ParentId = "bomb_power_1" },
    };

    public static UpgradeDef? GetUpgradeDef(string id)
    {
        foreach (var d in Upgrades)
            if (d.Id == id) return d;
        return null;
    }

    public int GetUpgradeLevel(string id) => _upgrades.TryGetValue(id, out var v) ? v : 0;

    // 単Lvノード鎖の連続所持段数を返す（効果アクセサの Lv 取得元）。
    //   prefix="fire_rate", maxSteps=4 なら fire_rate_1.._4 を頭から数え、最初の未所持で止める。
    //   途中欠け（_1 未所持だが _2 所持など）でも連続段数だけ数える＝旧 GetUpgradeLevel と同じ「段」を返す。
    //   効果式は不変・Lv の取得元だけをこのヘルパへ差し替える。
    public int ChainLevel(string prefix, int maxSteps)
    {
        int lv = 0;
        for (int i = 1; i <= maxSteps; i++)
            if (GetUpgradeLevel($"{prefix}_{i}") >= 1) lv++;
            else break;
        return lv;
    }

    // ───── セーブ移行（旧MaxLv方式 → 単Lvノード鎖）─────
    //   旧セーブは "fire_rate":3 のように「Lv値」を持つ。これを新ノード群（fire_rate_1/_2/_3 所持=1）へ読み替える。
    //   圧縮系(3→2)は min(n, chain長)＝最大段まで所持＝没収なし（末尾段効果は旧最終Lv相当に補償済み）。
    //   ワンショット：保存時は新IDで書かれ、次回以降は legacy 判定に引っかからない。
    private static readonly Dictionary<string, string[]> MigrationMap = new()
    {
        ["fire_rate"]     = new[] { "fire_rate_1", "fire_rate_2", "fire_rate_3", "fire_rate_4" },
        ["shot_power"]    = new[] { "shot_power_1", "shot_power_2", "shot_power_3", "shot_power_4" },
        ["shot_spread"]   = new[] { "spread_1", "spread_2", "spread_3" },
        ["shot_homing"]   = new[] { "homing_1", "homing_2", "homing_3" },
        ["move_speed"]    = new[] { "move_speed_1", "move_speed_2", "move_speed_3" },
        ["hitbox"]        = new[] { "hitbox_1", "hitbox_2", "hitbox_3" },
        ["contam_resist"] = new[] { "contam_1", "contam_2" },               // 3→2: min(n,2)
        ["imp_mult"]      = new[] { "imp_mult_1", "imp_mult_2", "imp_mult_3", "imp_mult_4" },
        ["fol_gain"]      = new[] { "fol_gain_1", "fol_gain_2" },           // 3→2
        ["combo_hold"]    = new[] { "combo_hold_1", "combo_hold_2" },       // 3→2
        ["max_life"]      = new[] { "max_life_1", "max_life_2" },           // 3→2
        ["bomb_count"]    = new[] { "bomb_count_1", "bomb_count_2" },       // 3→2
        ["bomb_power"]    = new[] { "bomb_power_1", "bomb_power_2" },       // 3→2
        ["shot_pierce"]   = new[] { "pierce_1", "pierce_2" },
        ["focus_fire"]    = new[] { "focus_1", "focus_2" },
        ["option_sub"]    = new[] { "option_1", "option_2" },
        ["chain_light"]   = new[] { "chain_1", "chain_2" },
        ["counter_light"] = new[] { "counter_1", "counter_2" },
        ["veil_light"]    = new[] { "veil_1", "veil_2" },
    };

    // 旧IDが _upgrades にあれば新ノード群へ読み替える（LoadFromSlot の _upgrades 復元直後・shotmode復元より前に呼ぶ）。
    // 没収ゼロ：旧Lv n を chain 先頭から min(n, chain長) 個だけ所持=1 にする（圧縮系も最大段まで＝没収なし）。
    private void MigrateUpgradesIfLegacy()
    {
        bool legacy = false;
        foreach (var k in _upgrades.Keys)
            if (MigrationMap.ContainsKey(k)) { legacy = true; break; }
        if (!legacy) return;
        var migrated = new Dictionary<string, int>();
        foreach (var kv in _upgrades)
        {
            if (MigrationMap.TryGetValue(kv.Key, out var chain))
            {
                int n = Mathf.Clamp(kv.Value, 0, chain.Length);
                for (int i = 0; i < n; i++) migrated[chain[i]] = 1;
            }
            else migrated[kv.Key] = kv.Value; // 既に新ID or 未知キーはそのまま
        }
        _upgrades = migrated;
    }

    // Lv→Lv+1 の価格。単Lvノード方式では各ノード MaxLevel=1・固定 BaseCost なので、
    // Lv0（未所持）なら BaseCost をそのまま返し、Lv1（所持済＝最大）は 0。CostMul/Capstone は使わない。
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

    // ツリー前提（奥義条件）を満たしているか。前提を持たないノードは常に true。
    // 判定は購入時（次のLv）のみ＝前提未達でも所持済みLvは有効のまま（グランドファーザー規則）。
    public bool IsPrereqMet(string id)
    {
        var d = GetUpgradeDef(id);
        if (d == null || string.IsNullOrEmpty(d.PrereqId)) return true;
        return GetUpgradeLevel(d.PrereqId) >= d.PrereqLv;
    }

    // 二分木の親条件。ノードに入る（Lv0→1）ときだけ親Lv≥1 を要求し、続きLvは親不要。
    // 旧セーブが親なしで子を所持していても続きLvは買える（自分Lv≥1なら常に true＝移行処理ゼロ）。
    public bool IsParentMet(string id)
    {
        if (GetUpgradeLevel(id) >= 1) return true;
        var d = GetUpgradeDef(id);
        if (d == null || string.IsNullOrEmpty(d.ParentId)) return true; // ""=ルート直結（ミナの核は常に在る）
        return GetUpgradeLevel(d.ParentId) >= 1;
    }

    // 排他フォークの封印判定。相方をLv1以上にしていて自分が未購入なら封印（買えない）。
    // 両側所持の旧セーブは双方Lv≥1＝どちらも封印されず両方強化継続可（没収なしの共存特例）。
    public bool IsSealed(string id)
    {
        var d = GetUpgradeDef(id);
        if (d == null || string.IsNullOrEmpty(d.ExclusiveWith)) return false;
        return GetUpgradeLevel(d.ExclusiveWith) >= 1 && GetUpgradeLevel(id) == 0;
    }

    public bool CanPurchase(string id)
    {
        long c = GetUpgradeCost(id);
        return c >= 0 && Impression >= c && IsPrereqMet(id) && IsParentMet(id) && !IsSealed(id);
    }

    // ───── 振り直し（排他フォーク単点のみ・ショップ内限定） ─────
    // 対に投じた額を100%返金し、手数料20%（10単位切り上げ・最低100）を差し引く。全リセットは作らない。

    // このノードに投じた総額（Lv0..現Lv-1 の CostAt 総和で決定的に再計算）。
    public long TotalPaid(string id)
    {
        var d = GetUpgradeDef(id);
        if (d == null) return 0;
        long sum = 0;
        for (int k = 0; k < GetUpgradeLevel(id); k++) sum += CostAt(d, k);
        return sum;
    }

    // フォーク（idA⊗idB）の返金額＝双方に投じた総額。
    public long RespecRefund(string idA, string idB) => TotalPaid(idA) + TotalPaid(idB);

    // 手数料＝返金対象額の20%を10単位に切り上げ・最低100。未投資（返金0）なら 0。
    public long RespecFee(string idA, string idB)
    {
        long refund = RespecRefund(idA, idB);
        if (refund <= 0) return 0;
        long fee = (refund * 20 + 999) / 1000 * 10; // ceil(refund*0.2/10)*10 の整数演算
        return System.Math.Max(100, fee);
    }

    // 振り直し実行：対の両ノードを Lv0 に戻し、返金−手数料をウォレットへ。
    // RunImpression（今ランの稼ぎ表示）には加算しない。成功で true。
    public bool TryRespec(string idA, string idB)
    {
        long refund = RespecRefund(idA, idB);
        if (refund <= 0) return false;
        long fee = RespecFee(idA, idB);
        _upgrades.Remove(idA);
        _upgrades.Remove(idB);
        Impression += refund - fee;
        return true;
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
    public float UpgradeImpressionMul => 1f + 0.12f * ChainLevel("imp_mult", 4);
    // 獲得インプレ（お金）全体の追加倍率。コスト/価格には掛からない＝獲得だけ増える。後で調整しやすいよう定数化。
    public const float MoneyGainMul = 2f;
    public float TotalImpressionMul => DifficultyImpressionMul * FollowerImpressionMul * UpgradeImpressionMul * (BurningThisRun ? 0.6f : 1f);

    // ── 強化効果アクセサ（Player/Hud が STEP3 で参照する）──
    //   単Lvノード方式：Lv 取得元を GetUpgradeLevel("x")→ChainLevel("x", n) に差替え。式は不変。
    public int ShotDamageBonus => ChainLevel("shot_power", 4);                            // 弾ダメージ +Lv
    // 発射間隔×（連射強化で短縮、炎上中は +30% 延長＝弱体）。
    public float FireIntervalMul => Mathf.Max(0.4f, 1f - 0.08f * ChainLevel("fire_rate", 4)) * (BurningThisRun ? 1.3f : 1f);
    // 移動速度×（機動強化で増、炎上中は -10%）。
    public float MoveSpeedMul => (1f + 0.12f * ChainLevel("move_speed", 3)) * (BurningThisRun ? 0.9f : 1f);
    public float HitRadiusMul => Mathf.Max(0.4f, 1f - 0.12f * ChainLevel("hitbox", 3));
    // 3→2圧縮系（max_life/bomb_count/bomb_power/fol_gain/combo_hold）は効果表どおり段2=+2/×1.30 等。
    // 式は不変で ChainLevel 直結（没収は移行側の min(n,2) で担保。効果は末尾段が旧最終より一段控えめになる）。
    public int MaxLifeBonus => ChainLevel("max_life", 2);
    public int BombCountBonus => ChainLevel("bomb_count", 2);
    public float BombPowerMul => 1f + 0.25f * ChainLevel("bomb_power", 2);
    public int OptionSubCount => ChainLevel("option", 2);
    public int ShotPierceCount => ChainLevel("pierce", 2);     // 連射弾の貫通数（貫く光。0=貫通なし）
    public int CounterLightLevel => ChainLevel("counter", 2);  // 返し光（回避よけ弾の追尾光弾化）
    // 集中の光：同一敵への連続ヒットで威力ボーナス（上限=+Lv。積み上げは Player 側が管理）。
    public int FocusFireMaxStack => ChainLevel("focus", 2);
    // 連鎖の光：拡散弾の跳弾回数（Lv1=1回・Lv2=2回。威力×0.4は Bullet.TryChain 側）。
    public int ChainLightBounces => ChainLevel("chain", 2);
    // 祈りの帳：回避後の弾消し光輪（半径 r20/28px・持続 0.5/0.7s）。Lv0 は 0＝無効。
    public float VeilLightRadius => new[] { 0f, 20f, 28f }[Mathf.Clamp(ChainLevel("veil", 2), 0, 2)];
    public float VeilLightDuration => new[] { 0f, 0.5f, 0.7f }[Mathf.Clamp(ChainLevel("veil", 2), 0, 2)];

    // ── モード別強化（rapid/spread/homing の威力・間隔）──
    //   Player の各 Fire・modeMul が ChainLevel 経由で参照する。式はショップの効果表記と同期。
    public int RapidPowerBonus => ChainLevel("rapid_power", 2);          // 連射弾の追加威力 +Lv
    public float RapidRateMul => Mathf.Max(0.7f, 1f - 0.06f * ChainLevel("rapid_rate", 2)); // 連射間隔 ×0.94/0.88
    // 拡散ナーフ（プレイテスト）：面の楽しさ（弾数5way・±35°・320px/s）は据え置きで単体DPSだけ削る。
    public float SpreadPowerMul => new[] { 0.50f, 0.56f, 0.62f }[Mathf.Clamp(ChainLevel("spread_power", 2), 0, 2)]; // 拡散弾威力補正
    public float SpreadRateMul => Mathf.Max(1f, 1.45f - 0.10f * ChainLevel("spread_rate", 1)); // 拡散間隔税 1.45→1.35
    // ホーミング強化（同）：威力・間隔・旋回を底上げして「曲がって当たる」を成立させる。
    public float HomingPowerMul => new[] { 0.85f, 0.95f, 1.05f }[Mathf.Clamp(ChainLevel("homing_power", 2), 0, 2)]; // ホーミング弾威力補正
    public float HomingRateMul => Mathf.Max(1.40f, 1.55f - 0.15f * ChainLevel("homing_rate", 1)); // ホーミング間隔税 1.55→1.40
    public int HomingTurnRateOverride => ChainLevel("homing_rate", 1) >= 1 ? 200 : 0; // 誘導速射で旋回200（0=既定150）
    // ── 加速球系（accel_* ノード）。Player.FireAccel が毎発射時に読む＝購入/付け外しで即反映 ──
    public int AccelPowerBonus => ChainLevel("accel_power", 2);                                   // 加速球の追加威力 +Lv
    public float AccelChargeDelay => new[] { 0.8f, 0.65f, 0.5f }[Mathf.Clamp(ChainLevel("accel_charge", 2), 0, 2)]; // タメ時間（速填で短縮）
    public float AccelLaunchSpeed => GetUpgradeLevel("accel_speed_1") >= 1 ? 760f : 640f;         // 発進速度（推進強化で760）

    // ── バックファイア（後方弾）の数値。bf_* 未所持でも初期から弱く撃つ ──
    public int BackfireDamage => 1 + ChainLevel("bf_power", 3);             // ダメージ 1(初期)→bf_power で 2/3/4
    public float BackfireInterval => new[] { 0.9f, 0.7f, 0.55f }[Mathf.Clamp(ChainLevel("bf_rate", 2), 0, 2)]; // 間隔 0.9→0.7→0.55s
    public int BackfireShots => GetUpgradeLevel("bf_track_1") >= 1 ? 2 : 1; // 後方追尾で同時2発
    public float BackfireTurnRate => GetUpgradeLevel("bf_track_1") >= 1 ? 90f : 60f; // 旋回 60→bf_track で 90

    // 身のこなし（move_speed）の回避リワーク：CD 0.8→0.72/0.68/0.65s・距離 64→68/72/76px。
    // Player.TryDodge が回避開始時に参照する（低速 Focus と同様、i-frame 秒は手触り固定＝触らない）。
    // 実効待ち時間は max(DodgeCooldown, DodgeDuration=0.55s) で決まる。旧配列(0.80/0.65/0.58/0.55)は
    // Lv3のCD=0.55sがDodgeDurationと完全一致し、無敵0.45sを引いた無防備な隙間が0.10sしか残らず
    // 「ほぼ無敵チェーン」（連打だけで読み・回避判断なしに弾幕を無力化）が成立してしまっていた。
    // ここではLv3でもCD>=0.65s（=DodgeDuration床+0.10s）を維持し、無敵0.45sを引いても
    // 最低0.20s以上の無防備な隙間が必ず残る値に再調整。Lv0比でCDが縮む投資リターン自体は維持する。
    public float DodgeCooldown => new[] { 0.80f, 0.72f, 0.68f, 0.65f }[Mathf.Clamp(ChainLevel("move_speed", 3), 0, 3)];
    public float DodgeDistance => 64f + 4f * ChainLevel("move_speed", 3);

    // 汚染が高いほど優しさの溜まりが鈍る。序盤無痛・奥で効く非線形。下限0.55。
    // 汚染0.00→1.00 / 0.16→0.98 / 0.42→0.89 / 0.72→0.73 / 1.00→0.55。
    // 澄んだ心(contam)の承認式：下限 +0.05/Lv に加え全体を ×(1+0.06Lv)（上限1.1）。
    // 3→2圧縮の補償：段2で旧Lv3相当の効果 Lv を渡す（無汚染でも段2で上限×1.10・高汚染域も旧Lv3の底上げ）。
    private int ContamSteps => ChainLevel("contam", 2);
    public int ContamEffLevel => ContamSteps >= 2 ? 3 : ContamSteps; // 段2＝旧Lv3相当の実効Lv
    public float ContaminationGainMul => Mathf.Max(0f, 1f - 0.15f * ContamEffLevel); // 上昇を緩めるのみ
    public float KindnessGainMul => KindnessGainMulAt(ContamEffLevel);
    // Lv を引数化した実効値（ショップの「いま→買うと」プレビューが実式で正直に出すために公開）。
    public float KindnessGainMulAt(int lv) =>
        Mathf.Min(1.1f, Mathf.Max(0.55f + 0.05f * lv, 1f - 0.45f * Mathf.Pow(Contamination, 1.6f)) * (1f + 0.06f * lv));

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
        GainImpression(120);
        // フォロワー大口報酬。周回逓減も適用（同ステージ連続周回で減る）。
        int fol = Mathf.RoundToInt(40 * (1f + 0.15f * ChainLevel("fol_gain", 2)) * ReplayMul);
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
        // 会話選択（層2プロト）：STAGE3 MidStory の2択でAを選んだか。後方互換：キー無し＝false（=現行台詞）。
        data["pressedQ"] = PressedTheQuestion;
        // ハブ再訪小話の既読キー集合。後方互換：キー無し＝空扱い。
        var ids = new Godot.Collections.Array();
        foreach (var key in _idleDialogSeen)
            ids.Add(key);
        data["idleDialogSeen"] = ids;

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
        // ハブ再訪小話の既読キー復元（キー無し＝旧セーブは空＝後方互換）。
        _idleDialogSeen.Clear();
        if (data.ContainsKey("idleDialogSeen"))
        {
            var ids = data["idleDialogSeen"].AsGodotArray();
            foreach (var v in ids)
                _idleDialogSeen.Add(v.AsString());
        }
        // 最後に選んだモードを復元（未解放なら連射へフォールバック＝後方互換）。
        //   クランプ上限は 3（Accel＝加速球を正当な保存値として許容）。未解放なら下の IsModeUnlocked で Rapid へ落ちる。
        if (data.ContainsKey("shotmode"))
        {
            var m = (ShotMode)Mathf.Clamp(data["shotmode"].AsInt32(), 0, 3);
            SelectedShotMode = IsModeUnlocked(m) ? m : ShotMode.Rapid;
        }
        return true;
    }

    // はじめから＝メモリ上の永続状態を初期化（スロットのファイルは消さない）。
    public void ResetPersistent()
    {
        Impression = 0;
        Followers = 0;
        _upgrades.Clear();
        SelectedShotMode = ShotMode.Rapid;
        _midBossCleared.Clear();
        ShopTutorialSeen = false;
        SelectedEntry = StageEntry.Start;
        _cleared.Clear();          // ステージ進行（クリア済み）も初期化＝救った人数0から
        _burnHappened = false; Burning = false; BurningThisRun = false;
        PressedTheQuestion = false; // 会話選択（層2プロト）の疑いフラグも初期化
        _idleDialogSeen.Clear();   // ハブ再訪小話の既読も初期化
        // 汚染は物語の背骨でシーンをまたいで持ち越すぶん、ここで戻さないと FINAL/Final で 1.0 にした値のまま
        //   新規データのハブ／プロローグへ入り、やさしさ倍率・murk・自機の濁りが濁ったまま描かれる。
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
    // チュートリアル既読（端末ローカル）。初回プレイ判定に使う。
    public bool TutorialSeen { get; private set; }

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
        var data = new Godot.Collections.Dictionary { ["tutorialSeen"] = TutorialSeen };
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

    // やさしさゲージ（リフレイン）: グレイズ/浄化で貯まり、満タンで一時「やさしさ全開」
    private float _kindFill;            // 0..1 蓄積
    public bool IsOverload { get; private set; }
    private double _overloadT;
    private const double OverloadDur = 5.0;
    // 全開の充填は「倒す（浄化）」を主役に、かすりは前に出るほど効くブースターに（§2-4 攻めたほうが得）。
    private const float GrazeGain = 0.07f;   // 約14回（risky な薬味）
    private const float PurifyGain = 0.12f;  // 約8体（攻めて倒すのが全開の主動力）
    public bool JustOverloaded { get; private set; } // 発動した瞬間のフラグ（UI用、1フレーム）
    // ゲージ表示値: 全開中は残り時間、通常は蓄積量
    public float Kindness => IsOverload ? (float)(_overloadT / OverloadDur) : _kindFill;

    private double _comboTimer;
    // コンボ猶予はコンボ持続強化で延長される。
    private double ComboWindow => 2.0 + 0.4 * ChainLevel("combo_hold", 2);
    private const int MaxCombo = 16;
    // コンボ猶予の残り比率（0..1）。HUDのコンボ減衰バー用。コンボが立っていなければ0。
    public float ComboTimeRatio => Combo > 0 && _comboTimer > 0 ? (float)(_comboTimer / ComboWindow) : 0f;

    public override void _Ready()
    {
        // セーブはスロット制（手動）。起動時は自動ロードしない。
        // 端末ローカル prefs（チュートリアル既読など）と会話の既読ログだけは起動時に読む。
        LoadPrefs();
        LoadReadLog();

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
        }
    }

    // --boss 起動中か（消費される SelectedEntry と違い、ランを通して残る）。
    public bool DebugAlwaysBoss { get; private set; }

    // [一時/デバッグ] --choice : こはる面のボス戦中割り込み（会話2択）を HP 条件を待たずに即発火させる。
    // 選択シーンの確認専用。通常プレイ・配布ビルドでは付けない前提。
    public bool DebugChoiceNow { get; private set; }

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
    }

    public override void _Process(double delta)
    {
        JustOverloaded = false;
        if (_comboTimer > 0)
        {
            _comboTimer -= delta;
            if (_comboTimer <= 0)
                Combo = 0;
        }
        if (IsOverload)
        {
            _overloadT -= delta;
            if (_overloadT <= 0) { IsOverload = false; _kindFill = 0f; }
        }
    }

    // やさしさゲージを貯める。満タンになっても自動発動はせず、満タン(=Ready)で待機する。
    // 発動はプレイヤーの手動操作（TryActivateKindness）に委ねる＝“使う”判断が生まれる。
    private void AddKindness(float amount)
    {
        if (IsOverload) return;
        // 汚染が高いほど“やさしさ”の溜まりが鈍る（#2-A）。グレイズ/浄化の両方がここを通る。
        _kindFill = Mathf.Min(1f, _kindFill + amount * KindnessGainMul);
    }

    // やさしさが満タンで、手動発動できる状態か。
    public bool KindnessReady => !IsOverload && _kindFill >= 1f;

    // 「やさしさ全開」を手動発動。満タンなら消費して発動し true を返す。
    public bool TryActivateKindness()
    {
        if (IsOverload || _kindFill < 1f) return false;
        IsOverload = true;
        _overloadT = OverloadDur;
        JustOverloaded = true;
        _kindFill = 0f; // 発動と同時に消費（ゲージは全開タイマー表示へ切り替わる）
        return true;
    }

    // 道中カメオ（ミニボス）をHP削り切りで撃破した時の報酬。CameoBoss.OnCryStart から1回だけ呼ぶ。
    // やさしさゲージは「半分〜大きめ一気」を狙って 0.6 を加算（グレイズ/浄化と同じ AddKindness 経路＝
    // KindnessGainMul もかかる）。インプレは付けず、スコアのみ少々加点する。
    public void RewardCameoDefeat()
    {
        AddKindness(CameoKindnessReward);  // 直後の本ボス戦で全開を撃ちやすくする量
        Score += CameoScoreReward;         // スコア少々

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
            var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
            string msg = (gotLife && gotBomb) ? "♥ +1　BOMB +1"
                       : gotLife ? "♥ +1" : "BOMB +1";
            hud?.ShowBanner(msg);
        }
    }
    private const float CameoKindnessReward = 0.6f;
    private const int CameoScoreReward = 900;

    // ── ボム1発で報酬が付く雑魚浄化の上限（ボムキャップ）──
    //   ボス側の窓キャップ(Enemy.ExposedDamageCap)と同じ思想を雑魚経路にも当てる。
    //   ボム由来の浄化は「避けも当てもせず画面を一掃する」＝リスクを賭けていない（§2）ので、
    //   1発あたりこの体数までしか スコア/コンボ/インプレ を返さない。超過分は浄化そのものは成立し、
    //   進行(PurifiedCount)とやさしさ(AddKindness)は従来どおり通す＝道中ゲートを詰まらせない（親切設計）。
    //   3体＝緊急回避で巻き込む標準的な体数。通常プレイのボムは割に合ったまま、
    //   湧き上限(Spawner.MaxAlive=10)まで溜めて一掃する無限ファームだけが頭打ちになる。
    private const int BombPurifyRewardCap = 3;
    private int _bombPurifyCount; // 現在のボム1発で報酬を付けた体数（UseBomb でリセット）

    // 敵を浄化（撃破）した時の加点。コンボ倍率がかかる。
    // fromBomb=true はボムの強制浄化経路（Enemy.Purify）。上のボムキャップを超えた分は報酬を付けない。
    public void AddPurify(int basePoints, bool fromBomb = false)
    {
        bool rewarded = !fromBomb || ++_bombPurifyCount <= BombPurifyRewardCap;
        if (rewarded)
        {
            Combo = Mathf.Min(Combo + 1, MaxCombo);
            _comboTimer = ComboWindow;
            Score += basePoints * Mathf.Max(1, Combo);
        }
        PurifiedCount++;
        AddKindness(PurifyGain);
        // インプレ獲得：基礎2＋コンボぶん（§①-2）。倍率は GainImpression 内で適用。
        if (rewarded)
            GainImpression(2 + Combo);
    }

    // 敵弾をかすった（グレイズ）時の加点。
    // かすりでコンボ猶予をリフレッシュ＝「敵に寄ってかすり続ける」と攻めが途切れない（§2-4 攻めたほうが得）。
    // インプレ（通貨）も少額付与＝「弾に寄る」こと自体に経済的リターンを持たせる。
    //   回避よけ(AddDodgeGraze＝GainImpression(DodgeGrazeImpBase)=2)より少なく＝回避の優位は維持。
    //   farming対策：被弾直後の無敵中(_hitInvincible)は呼び出し元(Player.OnGrazeAreaEntered)で既にスキップ済み。
    public void AddGraze()
    {
        Score += 10;
        GrazeCount++;
        AddKindness(GrazeGain);
        GainImpression(1);
        if (Combo > 0)
            _comboTimer = ComboWindow;
    }

    // 回避（ドッジ）の無敵中に敵弾をかすめてよけた時の高報酬（§リスクとリターン）。
    // 通常グレイズ(Score+10・お金なし)より大きめ＝回避クールダウン0.8sを切って敵弾に突っ込むリスク相応。
    //   ・スコアは DodgeGrazeScore（通常10の5倍）。
    //   ・お金（インプレ＝ショップ通貨）を GainImpression(DodgeGrazeImpBase) で稼ぐ。倍率は内部で自動適用。
    //     実加算額を返す＝ポップアップ「+N」表示に使う。
    //   ・コンボ猶予をリフレッシュ（AddGraze と同様、攻めが途切れない）。やさしさも同程度。
    // farming上限は呼び出し側(Player)が1回避ごとにカウントして制御する。
    public long AddDodgeGraze()
    {
        Score += DodgeGrazeScore;
        AddKindness(GrazeGain);
        if (Combo > 0)
            _comboTimer = ComboWindow;
        return GainImpression(DodgeGrazeImpBase);
    }
    private const int DodgeGrazeScore = 50;   // 回避よけ1発のスコア（通常グレイズ10の5倍）
    private const int DodgeGrazeImpBase = 2;  // 回避よけ1発の基礎インプレ（倍率は GainImpression 内で適用。稼ぎすぎ是正で 4→2）

    // ボムで敵弾を消した時の小加点。
    public void AddBulletCleared()
    {
        Score += 5;
    }

    // こはる戦の「祈り弾」（消せる下方向弾）を自機弾で受け止めた時の加点（#12 機構側／#20）。
    // やさしさゲージ微加算＝“祈りを受け止める”が浄化/グレイズと同じ経路（KindnessGainMul込み）で報われる。
    public void AddPrayerCleared()
    {
        Score += 15;
        AddKindness(PrayerGain);
    }
    private const float PrayerGain = 0.02f; // 微加算（グレイズ0.07より小さく＝受け止めは薬味）

    // 祈りの帳（veil_light）の光輪が弾を受け止めた時の加点。ボム消し（Score+5）と同格＋やさしさ微加算。
    public void AddVeilCleared()
    {
        Score += 5;
        AddKindness(VeilGain);
    }
    private const float VeilGain = 0.01f; // 祈り弾(0.02)よりさらに小さく＝回避のおまけであって主動力にしない

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

    // チュートリアル（ステージ0）ステップ7用：やさしさゲージを一度だけ満タンにする。
    // 全開中は触らない（タイマー表示と競合させない）。
    public void FillKindnessForTutorial() { if (!IsOverload) _kindFill = 1f; }

    // ラン開始時のリセット。※インプレ/フォロワー/強化は恒久なので消さない（§0-3）。
    public void ResetRun()
    {
        Score = 0;
        Combo = 0;
        _comboTimer = 0;
        Bombs = StartBombs;
        _bombPurifyCount = 0;
        PurifiedCount = 0;
        _progAccum = 0f;      // 前のめり進行アキュムレータもラン開始でリセット
        PlayerNormX = 0.5f;   // 自機Xは中央からとみなす（初フレーム前の背景/HUD 参照用）
        RunImpression = 0;
        _kindFill = 0f;
        IsOverload = false;
        _overloadT = 0;
        RedemptionActive = false;
    }

    // 改心演出中か。本戦ボスの OnCryStart が立て、次のラン開始（ResetRun）で下りる。
    // 残機0と同フレーム帯で飛翔中の弾がボスを浄化したエッジケースで、ゲームオーバーの
    // 「R/Q」抜けプロンプトを改心演出〜帰還会話に重ねないための判定（HandleGameOverExit が参照）。
    public bool RedemptionActive { get; private set; }
    public void NotifyRedemptionStart() => RedemptionActive = true;

    // ───────────────────────────────────────────────────────────
    // ゲームオーバー時の「ステージから抜ける（ハブへ戻る）」共通処理。
    // 各 *Root.cs が _Process で残機0を検知したら毎フレーム呼ぶ。
    //   ・抜けプロンプトを HUD に出し続ける（リトライ＝R は各 *Root.cs の別経路で有効。通常は長押し・
    //     ゲームオーバー中は即発。パッドはポーズメニューの「さいしょからやりなおす」経由）。
    //   ・Q / パッドB(×) で「抜ける」を選んだら、ランで貯めたインプレを AutoSave で
    //     確定保存（恒久値なので破棄しない・§0-3）してから Hub へ遷移する。
    // 戻り値 true ＝抜けを実行（呼び元はそれ以降の処理を打ち切ってよい）。
    public static bool HandleGameOverExit(Node root, Hud? hud, ref bool exitHeld)
    {
        var game = root.GetNodeOrNull<GameManager>("/root/Game");

        // 改心演出が始まっていたら勝負は決着＝ゲームオーバー扱いを取り下げ、演出を優先する
        //（残機0と同フレーム帯で飛翔中の弾がボスを浄化したエッジケース。プロンプトを重ねない）。
        // R リトライは各 *Root.cs の別経路で従来どおり有効。
        if (game?.RedemptionActive ?? false)
        {
            hud?.ShowGameOverPrompt("");
            exitHeld = false;
            return false;
        }

        // プロンプトは直近デバイス（Pad.ShowKeyboard）に追従。パッドのリトライはポーズメニュー経由
        //（Start はメニュー開閉に使うため）、抜けは B（×）＝Back(SELECT/VIEW) は会話ログの開キーと衝突する。
        hud?.ShowGameOverPrompt(Pad.ShowKeyboard
            ? "R：ボスからやり直す　／　Shift+R：最初から　／　Q：ステージから抜ける（ハブへ戻る）"
            : $"{Pad.Face(JoyButton.Start)}：メニュー→さいしょからやりなおす　／　{Pad.Face(JoyButton.B)}：ステージから抜ける（ハブへ戻る）");

        bool exit = Input.IsKeyPressed(Key.Q) || Pad.Pressed(JoyButton.B);
        bool fired = exit && !exitHeld;
        exitHeld = exit;
        if (!fired) return false;

        // ランで貯めたお金（インプレ）は恒久値。抜けても破棄せず、ここで確実に保存してから帰還。
        game?.AutoSave();
        root.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        Audio.Instance?.PlayUiCancel();
        root.GetTree().ChangeSceneToFile("res://Hub.tscn");
        return true;
    }
}
