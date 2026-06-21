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
    // 通常ボス: Easy2/Normal3/Hard4/Lunatic5（各難易度から1本減＝難度緩和）。ラスボス格(Mina)は +1本（finalBoss=true）。
    public int DiffBarBonus(bool finalBoss) =>
        (Difficulty switch { Diff.Easy => 2, Diff.Hard => 4, Diff.Lunatic => 5, _ => 3 }) + (finalBoss ? 1 : 0);

    // ルナティック解禁条件（①-9）：フォロワーが一定 or 主要火力強化が一定段階。
    public const int LunaticFollowerReq = 300;
    public bool IsLunaticUnlocked => Followers >= LunaticFollowerReq || GetUpgradeLevel("shot_power") >= 4;

    // ダイブ先の受け渡し（ハブ→難易度選択→ステージ）。
    public string PendingStageScene = "res://Rei.tscn";

    // ───── チェックポイント入口（最初から / 中ボスから / ボスから）─────
    //   中ボス(cameo)を持つ3ステージ（レイ/あかり/こはる）で道中をスキップして任意の戦闘から始められる。
    //   SelectedEntry は「ラン単位」＝非セーブ。DiffSelect がダイブ直前にセットし、Stage が _Ready で読む。
    //   解放ゲート：MidBoss は中ボス撃破で解放（IsMidBossCleared）、Boss はステージクリアで解放（IsStageCleared）。
    public enum StageEntry { Start, MidBoss, Boss }
    public StageEntry SelectedEntry = StageEntry.Start;

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
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmStage);
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

    // 強化カタログ（§①-4）。表示名/説明は仮。効果は下の各アクセサで定義。
    public sealed class UpgradeDef
    {
        public string Id = "";
        public string Name = "";
        public string Desc = "";
        public int MaxLevel;
        public long BaseCost;
        public float CostMul; // 次レベルの価格は BaseCost * CostMul^(現Lv)
    }

    public static readonly UpgradeDef[] Upgrades =
    {
        new() { Id = "shot_power",    Name = "光の出力",   Desc = "届ける光の威力UP",        MaxLevel = 5, BaseCost = 400,  CostMul = 1.5f },
        new() { Id = "fire_rate",     Name = "連射速度",   Desc = "発射間隔を短縮",          MaxLevel = 4, BaseCost = 350,  CostMul = 1.45f },
        new() { Id = "shot_spread",   Name = "拡散展開",   Desc = "拡散モード解放→本数増(5→7→9)", MaxLevel = 3, BaseCost = 500, CostMul = 1.5f },
        new() { Id = "shot_homing",   Name = "誘導の祈り", Desc = "ホーミングモード解放→追尾数増(2→2→3)", MaxLevel = 3, BaseCost = 700, CostMul = 1.5f },
        new() { Id = "move_speed",    Name = "機動力",     Desc = "移動速度UP",              MaxLevel = 3, BaseCost = 250,  CostMul = 1.4f },
        new() { Id = "hitbox",        Name = "回避域",     Desc = "当たり判定を縮小",        MaxLevel = 3, BaseCost = 600,  CostMul = 1.55f },
        new() { Id = "bomb_count",    Name = "ボム所持",   Desc = "初期ボム数+1",            MaxLevel = 3, BaseCost = 450,  CostMul = 1.45f },
        new() { Id = "bomb_power",    Name = "ボム威力",   Desc = "ボムの一掃範囲UP",        MaxLevel = 3, BaseCost = 350,  CostMul = 1.4f },
        new() { Id = "max_life",      Name = "最大♥",      Desc = "ライフ上限+1",            MaxLevel = 3, BaseCost = 700,  CostMul = 1.6f },
        new() { Id = "imp_mult",      Name = "浄化倍率",   Desc = "獲得する浄化した心UP",    MaxLevel = 4, BaseCost = 300,  CostMul = 1.45f },
        new() { Id = "fol_gain",      Name = "拡散力",     Desc = "フォロワー獲得効率UP",    MaxLevel = 3, BaseCost = 300,  CostMul = 1.45f },
        new() { Id = "combo_hold",    Name = "コンボ持続", Desc = "コンボ猶予を延長",        MaxLevel = 3, BaseCost = 200,  CostMul = 1.4f },
        new() { Id = "contam_resist", Name = "汚染耐性",   Desc = "汚染の上昇を抑え、やさしさの鈍りを緩和", MaxLevel = 3, BaseCost = 650,  CostMul = 1.55f },
        new() { Id = "option_sub",    Name = "拡散サブ",   Desc = "追従オプションを追加",     MaxLevel = 2, BaseCost = 1000, CostMul = 1.7f },
    };

    public static UpgradeDef? GetUpgradeDef(string id)
    {
        foreach (var d in Upgrades)
            if (d.Id == id) return d;
        return null;
    }

    public int GetUpgradeLevel(string id) => _upgrades.TryGetValue(id, out var v) ? v : 0;

    // 次レベルの価格。最大Lv到達 or 不正idなら -1。
    public long GetUpgradeCost(string id)
    {
        var d = GetUpgradeDef(id);
        if (d == null) return -1;
        int lv = GetUpgradeLevel(id);
        if (lv >= d.MaxLevel) return -1;
        return (long)Mathf.Round(d.BaseCost * Mathf.Pow(d.CostMul, lv));
    }

    public bool CanPurchase(string id)
    {
        long c = GetUpgradeCost(id);
        return c >= 0 && Impression >= c;
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
    public float FollowerPowerMul => 1f + Mathf.Min(0.50f, Followers * 0.00010f);
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
    public float ContaminationGainMul => Mathf.Max(0f, 1f - 0.15f * GetUpgradeLevel("contam_resist")); // 上昇を緩めるのみ

    // 汚染が高いほど優しさの溜まりが鈍る。序盤無痛・奥で効く非線形。下限0.55。
    // 汚染0.00→1.00 / 0.16→0.98 / 0.42→0.89 / 0.72→0.73 / 1.00→0.55。
    public float KindnessGainMul => Mathf.Max(0.55f, 1f - 0.45f * Mathf.Pow(Contamination, 1.6f));

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
        // 端末ローカル prefs（チュートリアル既読など）だけは起動時に読む。
        LoadPrefs();

        // 検証専用：--seed-records でダミーのクリアタイムをメモリに注入（記録画面/カードの確認用）。
        // セーブには一切書かない（手動セーブしない限り消える）＝本番フロー/既存スロットを汚さない。
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--seed-records") { SeedDebugRecords(); break; }
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
    private const int DodgeGrazeImpBase = 4;  // 回避よけ1発の基礎インプレ（倍率は GainImpression 内で適用）
    public int DodgeGrazeCount { get; private set; }

    // ボムで敵弾を消した時の小加点。
    public void AddBulletCleared()
    {
        Score += 5;
    }

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
    }

    // ───────────────────────────────────────────────────────────
    // ゲームオーバー時の「ステージから抜ける（ハブへ戻る）」共通処理。
    // 各 *Root.cs が _Process で残機0を検知したら毎フレーム呼ぶ。
    //   ・抜けプロンプトを HUD に出し続ける（リトライ＝R/Start は従来どおり別経路で有効）。
    //   ・Q / パッドBack(Select) で「抜ける」を選んだら、ランで貯めたインプレを AutoSave で
    //     確定保存（恒久値なので破棄しない・§0-3）してから Hub へ遷移する。
    // 戻り値 true ＝抜けを実行（呼び元はそれ以降の処理を打ち切ってよい）。
    public static bool HandleGameOverExit(Node root, Hud? hud, ref bool exitHeld)
    {
        hud?.ShowGameOverPrompt("R：リトライ　／　Q：ステージから抜ける（ハブへ戻る）");

        bool exit = Input.IsKeyPressed(Key.Q) || Pad.Pressed(JoyButton.Back);
        bool fired = exit && !exitHeld;
        exitHeld = exit;
        if (!fired) return false;

        // ランで貯めたお金（インプレ）は恒久値。抜けても破棄せず、ここで確実に保存してから帰還。
        var game = root.GetNodeOrNull<GameManager>("/root/Game");
        game?.AutoSave();
        root.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        Audio.Instance?.PlayUiCancel();
        root.GetTree().ChangeSceneToFile("res://Hub.tscn");
        return true;
    }
}
