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

    // ───── ショットモード（設計書 §3）。連射は初期解放、拡散/ホーミングはショップ購入で解放 ─────
    public enum ShotMode { Rapid, Spread, Homing }
    public ShotMode SelectedShotMode = ShotMode.Rapid; // 最後に選んだモード（Save 対象・起動時復元）
    public bool HasSpread => GetUpgradeLevel("shot_spread") >= 1;
    public bool HasHoming => GetUpgradeLevel("shot_homing") >= 1;
    // 拡散の本数 5→7→9 ／ ホーミングの追尾数 2→2→3（Lv に対応・上振れを抑え強すぎを是正）。
    public int SpreadWays => new[] { 0, 5, 7, 9 }[Mathf.Clamp(GetUpgradeLevel("shot_spread"), 0, 3)];
    public int HomingShots => new[] { 0, 2, 2, 3 }[Mathf.Clamp(GetUpgradeLevel("shot_homing"), 0, 3)];
    public bool IsModeUnlocked(ShotMode m) => m switch { ShotMode.Spread => HasSpread, ShotMode.Homing => HasHoming, _ => true };
    // 解放済みモードを循環（連射→拡散→ホーミング→連射…・未解放はスキップ）。
    public ShotMode NextUnlockedMode(ShotMode cur)
    {
        for (int i = 1; i <= 3; i++)
        {
            var m = (ShotMode)(((int)cur + i) % 3);
            if (IsModeUnlocked(m)) return m;
        }
        return ShotMode.Rapid;
    }
    public string ShotModeName(ShotMode m) => m switch { ShotMode.Spread => "拡散", ShotMode.Homing => "ホーミング", _ => "連射" };
    // 残機・ボムは難易度ベース ＋ 恒久強化ボーナス。
    public int StartLives => (Difficulty switch { Diff.Easy => 6, Diff.Hard => 3, Diff.Lunatic => 3, _ => 4 }) + MaxLifeBonus;
    public int StartBombs => (Difficulty switch { Diff.Easy => 6, Diff.Hard => 3, Diff.Lunatic => 3, _ => 4 }) + BombCountBonus;
    public float BulletSpeedMul => Difficulty switch { Diff.Easy => 0.62f, Diff.Hard => 1.05f, Diff.Lunatic => 1.18f, _ => 0.85f };
    // 難易度は敵の体力ではなく「弾の数」で調整する（やさしいほど弾が少ない）。
    public float BulletCountMul => Difficulty switch { Diff.Easy => 0.38f, Diff.Hard => 1.1f, Diff.Lunatic => 1.9f, _ => 0.7f };
    public float DanmakuIntervalMul => Difficulty switch { Diff.Easy => 2.1f, Diff.Hard => 1.0f, Diff.Lunatic => 0.85f, _ => 1.35f };
    public string DiffName => Difficulty switch { Diff.Easy => "EASY", Diff.Hard => "HARD", Diff.Lunatic => "LUNATIC", _ => "NORMAL" };

    // ボスHPバー本数（言葉のシールド＋無防備窓リワーク）。1本=BarHp(=100)で、総HP=本数×BarHp。
    // 難易度で本数が増える＝堅くなる（弾数調整とは別軸の「殴る回数」調整）。
    // 通常ボス: Easy2/Normal4/Hard5/Lunatic6（#25: Easyは据え置き＝入口を守り、Normal以上を+1本）。
    // ラスボス格(Mina)は +1本（finalBoss=true）。
    public int DiffBarBonus(bool finalBoss) =>
        (Difficulty switch { Diff.Easy => 2, Diff.Hard => 5, Diff.Lunatic => 6, _ => 4 }) + (finalBoss ? 1 : 0);

    // ルナティック解禁条件（①-9）：フォロワーが一定 or 主要火力強化が一定段階。
    public const int LunaticFollowerReq = 200;
    public bool IsLunaticUnlocked => Followers >= LunaticFollowerReq || GetUpgradeLevel("shot_power") >= 4;

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
    // 累計グレイズ（かすり）数。チュートリアルの「グレイズ1回」検出に使う。
    public int GrazeCount { get; private set; }

    // ステージ目標：このタイムラインを浄化しきる人数。到達でステージクリア。
    public int StageTarget { get; private set; } = 24;
    public void SetStageTarget(int t) => StageTarget = Mathf.Max(1, t);
    // 浄化ゲージ(0..1)＝目標までの達成度。世界の暖かさもこれに連動する。
    public float StageProgress => Mathf.Clamp((float)PurifiedCount / StageTarget, 0f, 1f);
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
        new() { Id = "rei",    Scene = "res://Rei.tscn",    Handle = "@rei_____", Tweet = "だれも、わたしには追いつけない。……それの、なにが、いけないの。", Title = "STAGE 1 — レイ" },
        new() { Id = "akari",  Scene = "res://Akari.tscn",  Handle = "@akari.",   Tweet = "すき、すき、すき。……ひとつでいいから、本物になって。",   Title = "STAGE 2 — あかり" },
        new() { Id = "koharu", Scene = "res://Koharu.tscn", Handle = "@koharu",   Tweet = "ぜんぶ食べてね。のこしちゃだめ。……そしたら、いなくならないでしょ?", Title = "STAGE 3 — こはる" },
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
    // コメント返信済みのステージ（セッション内・1回だけ報酬）。
    private readonly HashSet<string> _replied = new();
    public bool HasReplied(string id) => _replied.Contains(id);
    public void MarkReplied(string id) => _replied.Add(id);
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
    private readonly Dictionary<string, int> _stagePlays = new();
    public int StagePlays(string id) => _stagePlays.TryGetValue(id, out var v) ? v : 0;

    // ─── 炎上（②-5 / ③-6）───
    //   発生は一度きり（STAGE2クリア後）。次のダイブ1ステージだけ弱体化（発射↓/移動↓/インプレ×0.6）。
    public bool Burning;          // 炎上発生済みで未消費（次のダイブで適用）
    public bool BurningThisRun;   // 現在のステージrunが炎上下か（Player/Hudが参照）
    private bool _burnHappened;    // 一度きりのストーリーイベント済みか
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
    // 直近ステージクリアの報酬（帰還演出のカウントアップ表示用）。
    public int LastClearImpression { get; private set; }
    public int LastClearFollowers { get; private set; }

    // 恒久強化レベル（id → 現在Lv）。未所持は 0。
    private readonly Dictionary<string, int> _upgrades = new();

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

    public static readonly UpgradeDef[] Upgrades =
    {
        // shot_power: MaxLevel 5→4（無防備窓の弾ダメージは Clamp(_,1,4) ＝ Lv5 は効果の無い死にレベルだった）。
        new() { Id = "shot_power",    Name = "光の出力",   Desc = "届ける光の威力UP",        MaxLevel = 4, BaseCost = 400,  CostMul = 1.35f, ParentId = "fire_rate" },
        new() { Id = "fire_rate",     Name = "連射速度",   Desc = "発射間隔を短縮",          MaxLevel = 4, BaseCost = 350,  CostMul = 1.32f },
        new() { Id = "shot_spread",   Name = "拡散展開",   Desc = "拡散モード解放→本数増(5→7→9)", MaxLevel = 3, BaseCost = 500, CostMul = 1.38f, ParentId = "fire_rate" },
        new() { Id = "shot_homing",   Name = "誘導の祈り", Desc = "ホーミングモード解放→追尾数増(2→2→3)", MaxLevel = 3, BaseCost = 550, CostMul = 1.5f, ParentId = "move_speed" },
        // move_speed: 「機動力」→「身のこなし」リワーク（IDは不変＝セーブ互換）。移動UPに加え回避のキレ（CD/距離）も伸びる。
        new() { Id = "move_speed",    Name = "身のこなし", Desc = "移動速度UP＋回避のキレ（CD短縮・距離延長）", MaxLevel = 3, BaseCost = 250,  CostMul = 1.4f },
        new() { Id = "hitbox",        Name = "回避域",     Desc = "当たり判定を縮小",        MaxLevel = 3, BaseCost = 600,  CostMul = 1.55f, ParentId = "shot_homing" },
        new() { Id = "bomb_count",    Name = "ボム所持",   Desc = "初期ボム数+1",            MaxLevel = 3, BaseCost = 450,  CostMul = 1.45f, ParentId = "max_life", ExclusiveWith = "bomb_power" },
        new() { Id = "bomb_power",    Name = "ボム威力",   Desc = "ボム直撃が穢れを深く祓う（無防備の本体ダメージUP）", MaxLevel = 3, BaseCost = 350,  CostMul = 1.4f, ParentId = "max_life", ExclusiveWith = "bomb_count" },
        new() { Id = "max_life",      Name = "最大♥",      Desc = "ライフ上限+1",            MaxLevel = 3, BaseCost = 550,  CostMul = 1.45f, ParentId = "contam_resist" },
        new() { Id = "imp_mult",      Name = "浄化倍率",   Desc = "獲得する浄化した心UP",    MaxLevel = 4, BaseCost = 300,  CostMul = 1.45f, ParentId = "contam_resist" },
        new() { Id = "fol_gain",      Name = "拡散力",     Desc = "フォロワー獲得効率UP（フォロワーは火力と収入を底上げ）", MaxLevel = 3, BaseCost = 300,  CostMul = 1.45f, ParentId = "shot_spread" },
        new() { Id = "combo_hold",    Name = "コンボ持続", Desc = "コンボ猶予を延長",        MaxLevel = 3, BaseCost = 200,  CostMul = 1.4f, ParentId = "shot_spread" },
        // contam_resist: 「汚染耐性」→「澄んだ心」リワーク（IDは不変＝セーブ互換）。上昇抑制は現行維持＋
        // やさしさ効率を承認式（×(1+0.06Lv)・上限1.1）で底上げ＝無汚染でも Lv1-2 が確かに効く。
        new() { Id = "contam_resist", Name = "澄んだ心",   Desc = "汚染の上昇を抑え、やさしさの効率を底上げ", MaxLevel = 3, BaseCost = 300,  CostMul = 1.4f, ParentId = "move_speed" },
        // ── 奥義（前提つき・一律100の適用除外）。⊗＝排他フォークの対 ──
        // option_sub: 幽霊商品の実体化（追従オプション。威力×0.5でメイン同期射撃）。価格改定 1000/1.7→900/1.6。⊗連鎖の光。
        new() { Id = "option_sub",    Name = "拡散サブ",   Desc = "追従オプション+1（威力×0.5でメイン同期射撃）", MaxLevel = 2, BaseCost = 900, CostMul = 1.6f, PrereqId = "shot_spread", PrereqLv = 2, Capstone = true, ParentId = "fol_gain", ExclusiveWith = "chain_light" },
        new() { Id = "shot_pierce",   Name = "貫く光",     Desc = "連射弾が敵をLv体まで貫通する", MaxLevel = 2, BaseCost = 800, CostMul = 1.6f, PrereqId = "shot_power", PrereqLv = 2, Capstone = true, ParentId = "shot_power", ExclusiveWith = "focus_fire" },
        new() { Id = "counter_light", Name = "返し光",     Desc = "回避よけした弾を追尾光弾に変えて撃ち返す", MaxLevel = 2, BaseCost = 800, CostMul = 1.6f, PrereqId = "shot_homing", PrereqLv = 2, Capstone = true, ParentId = "hitbox", ExclusiveWith = "veil_light" },
        // ── 二分木化で追加の新奥義3種（各排他フォークのもう片翼）──
        new() { Id = "focus_fire",    Name = "集中の光",   Desc = "同じ敵に当て続けると威力が上がる（対象変更・被弾でリセット）", MaxLevel = 2, BaseCost = 800, CostMul = 1.6f, PrereqId = "shot_power", PrereqLv = 2, Capstone = true, ParentId = "shot_power", ExclusiveWith = "shot_pierce" },
        new() { Id = "chain_light",   Name = "連鎖の光",   Desc = "拡散弾が当たった敵から最寄りの敵へ跳弾する（威力×0.4）", MaxLevel = 2, BaseCost = 800, CostMul = 1.6f, PrereqId = "shot_spread", PrereqLv = 2, Capstone = true, ParentId = "fol_gain", ExclusiveWith = "option_sub" },
        new() { Id = "veil_light",    Name = "祈りの帳",   Desc = "回避のあと自機の周りに弾を消す光輪をまとう", MaxLevel = 2, BaseCost = 800, CostMul = 1.6f, PrereqId = "shot_homing", PrereqLv = 2, Capstone = true, ParentId = "hitbox", ExclusiveWith = "counter_light" },
    };

    public static UpgradeDef? GetUpgradeDef(string id)
    {
        foreach (var d in Upgrades)
            if (d.Id == id) return d;
        return null;
    }

    public int GetUpgradeLevel(string id) => _upgrades.TryGetValue(id, out var v) ? v : 0;

    // Lv→Lv+1 の価格（決定的・状態非依存）。振り直しの返金再計算（TotalPaid）と GetUpgradeCost が共用する。
    public static long CostAt(UpgradeDef d, int lv)
    {
        if (lv >= d.MaxLevel) return 0;
        // 奥義（Capstone）は一律100の適用除外：Lv1=BaseCost・以降 ×CostMul（例 800/1280）。
        if (d.Capstone) return (long)Mathf.Round(d.BaseCost * Mathf.Pow(d.CostMul, lv));
        if (lv == 0) return 100; // 最初の強化は全項目共通で100に固定（中ボス後すぐ1つ買えるように）
        // Lv1→2 は BaseCost そのもの（CostMul^0）。旧式 CostMul^lv だと 100→BaseCost×CostMul の急ジャンプになっていた。
        return (long)Mathf.Round(d.BaseCost * Mathf.Pow(d.CostMul, lv - 1));
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

    // ── フォロワー由来の常時バフ（天井付き・§①-5）──
    // PowerMul は Player.Fire の弾ダメージに実配線（fol_gain＝“火力の遠回り投資”の受け皿）。係数 0.00010→0.00025＝2,000人で上限+50%。
    public float FollowerPowerMul => 1f + Mathf.Min(0.50f, Followers * 0.00025f);
    public float FollowerImpressionMul => 1f + Mathf.Min(0.50f, Followers * 0.00008f);

    // ── 難易度・強化由来のインプレ倍率 ──
    public static float DifficultyImpressionMulFor(Diff d) => d switch { Diff.Easy => 0.7f, Diff.Hard => 1.4f, Diff.Lunatic => 2.2f, _ => 1f };
    public float DifficultyImpressionMul => DifficultyImpressionMulFor(Difficulty);
    public float UpgradeImpressionMul => 1f + 0.12f * GetUpgradeLevel("imp_mult");
    // 獲得インプレ（お金）全体の追加倍率。コスト/価格には掛からない＝獲得だけ増える。後で調整しやすいよう定数化。
    public const float MoneyGainMul = 2f;
    public float TotalImpressionMul => DifficultyImpressionMul * FollowerImpressionMul * UpgradeImpressionMul * (BurningThisRun ? 0.6f : 1f);

    // ── 強化効果アクセサ（Player/Hud が STEP3 で参照する）──
    public int ShotDamageBonus => GetUpgradeLevel("shot_power");                            // 弾ダメージ +Lv
    // 発射間隔×（連射強化で短縮、炎上中は +30% 延長＝弱体）。
    public float FireIntervalMul => Mathf.Max(0.4f, 1f - 0.08f * GetUpgradeLevel("fire_rate")) * (BurningThisRun ? 1.3f : 1f);
    // 移動速度×（機動強化で増、炎上中は -10%）。
    public float MoveSpeedMul => (1f + 0.12f * GetUpgradeLevel("move_speed")) * (BurningThisRun ? 0.9f : 1f);
    public float HitRadiusMul => Mathf.Max(0.4f, 1f - 0.12f * GetUpgradeLevel("hitbox"));
    public int MaxLifeBonus => GetUpgradeLevel("max_life");
    public int BombCountBonus => GetUpgradeLevel("bomb_count");
    public float BombPowerMul => 1f + 0.25f * GetUpgradeLevel("bomb_power");
    public int OptionSubCount => GetUpgradeLevel("option_sub");
    public int ShotPierceCount => GetUpgradeLevel("shot_pierce");     // 連射弾の貫通数（貫く光。0=貫通なし）
    public int CounterLightLevel => GetUpgradeLevel("counter_light"); // 返し光（回避よけ弾の追尾光弾化）
    // 集中の光：同一敵への連続ヒットで威力ボーナス（上限=+Lv。積み上げは Player 側が管理）。
    public int FocusFireMaxStack => GetUpgradeLevel("focus_fire");
    // 連鎖の光：拡散弾の跳弾回数（Lv1=1回・Lv2=2回。威力×0.4は Bullet.TryChain 側）。
    public int ChainLightBounces => GetUpgradeLevel("chain_light");
    // 祈りの帳：回避後の弾消し光輪（半径 r20/28px・持続 0.5/0.7s）。Lv0 は 0＝無効。
    public float VeilLightRadius => new[] { 0f, 20f, 28f }[Mathf.Clamp(GetUpgradeLevel("veil_light"), 0, 2)];
    public float VeilLightDuration => new[] { 0f, 0.5f, 0.7f }[Mathf.Clamp(GetUpgradeLevel("veil_light"), 0, 2)];
    public float ContaminationGainMul => Mathf.Max(0f, 1f - 0.15f * GetUpgradeLevel("contam_resist")); // 上昇を緩めるのみ
    // 身のこなし（move_speed）の回避リワーク：CD 0.8→0.7/0.6/0.5s・距離 64→68/72/76px。
    // Player.TryDodge が回避開始時に参照する（低速 Focus と同様、i-frame 秒は手触り固定＝触らない）。
    public float DodgeCooldown => 0.8f - 0.1f * GetUpgradeLevel("move_speed");
    public float DodgeDistance => 64f + 4f * GetUpgradeLevel("move_speed");

    // 汚染が高いほど優しさの溜まりが鈍る。序盤無痛・奥で効く非線形。下限0.55。
    // 汚染0.00→1.00 / 0.16→0.98 / 0.42→0.89 / 0.72→0.73 / 1.00→0.55。
    // 澄んだ心(contam_resist)の承認式：下限 +0.05/Lv に加え全体を ×(1+0.06Lv)（上限1.1）＝
    // 無汚染でも Lv1 で×1.06・Lv2 で上限×1.10。Lv3 は高汚染域の底上げ専用（C=1.0 で 0.73→0.83）。
    public float KindnessGainMul => KindnessGainMulAt(GetUpgradeLevel("contam_resist"));
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
        LastClearImpression = (int)GainImpression(120);
        // フォロワー大口報酬。周回逓減も適用（同ステージ連続周回で減る）。
        int fol = Mathf.RoundToInt(40 * (1f + 0.15f * GetUpgradeLevel("fol_gain")) * ReplayMul);
        AddFollowers(fol);
        LastClearFollowers = fol;
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
        // クリアタイム復元（キー無し＝旧セーブは空のまま＝後方互換）。
        ClearTimes.Clear();
        if (data.ContainsKey("clearTimes"))
        {
            var ct = data["clearTimes"].AsGodotDictionary();
            foreach (var k in ct.Keys)
                ClearTimes[k.AsString()] = (float)ct[k].AsDouble();
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
        // 最後に選んだモードを復元（未解放なら連射へフォールバック＝後方互換）。
        if (data.ContainsKey("shotmode"))
        {
            var m = (ShotMode)Mathf.Clamp(data["shotmode"].AsInt32(), 0, 2);
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
    // タイトルの「あそびかた」からの任意再生フラグ（次のステージ開始で消費）。
    // 任意再生では TutorialSeen を書き換えない。
    public bool ForceTutorialReplay;

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
    private double ComboWindow => 2.0 + 0.4 * GetUpgradeLevel("combo_hold");
    private const int MaxCombo = 16;

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
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--boss") { SelectedEntry = StageEntry.Boss; break; }
    }

    // 検証用ダミー記録。リリースには影響しない（--seed-records 起動時のみ呼ばれる）。
    private void SeedDebugRecords()
    {
        void Put(string id, Diff d, float s) => ClearTimes[ClearTimeKey(id, d)] = s;
        Put("rei", Diff.Easy, 95.40f);   Put("rei", Diff.Normal, 83.12f);  Put("rei", Diff.Hard, 78.55f);
        Put("akari", Diff.Normal, 102.30f); Put("akari", Diff.Hard, 99.80f);
        Put("koharu", Diff.Easy, 121.00f);  Put("koharu", Diff.Lunatic, 140.67f);
        Put("final", Diff.Normal, 156.25f);
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

    // 道中カメオ（ミニボス）をHP削り切りで撃破した時の報酬。Escaped経路のみ・1回だけ呼ぶ。
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

    // 敵を浄化（撃破）した時の加点。コンボ倍率がかかる。
    public void AddPurify(int basePoints)
    {
        Combo = Mathf.Min(Combo + 1, MaxCombo);
        _comboTimer = ComboWindow;
        Score += basePoints * Mathf.Max(1, Combo);
        PurifiedCount++;
        AddKindness(PurifyGain);
        // インプレ獲得：基礎2＋コンボぶん（§①-2）。倍率は GainImpression 内で適用。
        GainImpression(2 + Combo);
    }

    // 敵弾をかすった（グレイズ）時の加点。
    // かすりでコンボ猶予をリフレッシュ＝「敵に寄ってかすり続ける」と攻めが途切れない（§2-4 攻めたほうが得）。
    public void AddGraze()
    {
        Score += 10;
        GrazeCount++;
        AddKindness(GrazeGain);
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
        DodgeGrazeCount++;
        AddKindness(GrazeGain);
        if (Combo > 0)
            _comboTimer = ComboWindow;
        return GainImpression(DodgeGrazeImpBase);
    }
    private const int DodgeGrazeScore = 50;   // 回避よけ1発のスコア（通常グレイズ10の5倍）
    private const int DodgeGrazeImpBase = 2;  // 回避よけ1発の基礎インプレ（倍率は GainImpression 内で適用。稼ぎすぎ是正で 4→2）
    public int DodgeGrazeCount { get; private set; }

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
        if (TutorialNoConsume) return true;
        if (Bombs <= 0)
            return false;
        Bombs--;
        return true;
    }

    // チュートリアル（ステージ0）ステップ7用：やさしさゲージを一度だけ満タンにする。
    // 全開中は触らない（タイマー表示と競合させない）。
    public void FillKindnessForTutorial() { if (!IsOverload) _kindFill = 1f; }

    public void AddBomb(int n = 1)
    {
        Bombs += n;
    }

    // ラン開始時のリセット。※インプレ/フォロワー/強化は恒久なので消さない（§0-3）。
    public void ResetRun()
    {
        Score = 0;
        Combo = 0;
        _comboTimer = 0;
        Bombs = StartBombs;
        PurifiedCount = 0;
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
            ? "R：リトライ　／　Q：ステージから抜ける（ハブへ戻る）"
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
