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
    // 残機・ボムは難易度ベース ＋ 恒久強化ボーナス。
    public int StartLives => (Difficulty switch { Diff.Easy => 6, Diff.Hard => 3, Diff.Lunatic => 3, _ => 4 }) + MaxLifeBonus;
    public int StartBombs => (Difficulty switch { Diff.Easy => 6, Diff.Hard => 3, Diff.Lunatic => 3, _ => 4 }) + BombCountBonus;
    public float BulletSpeedMul => Difficulty switch { Diff.Easy => 0.62f, Diff.Hard => 1.05f, Diff.Lunatic => 1.18f, _ => 0.85f };
    // 難易度は敵の体力ではなく「弾の数」で調整する（やさしいほど弾が少ない）。
    public float BulletCountMul => Difficulty switch { Diff.Easy => 0.38f, Diff.Hard => 1.1f, Diff.Lunatic => 1.9f, _ => 0.7f };
    public float DanmakuIntervalMul => Difficulty switch { Diff.Easy => 2.1f, Diff.Hard => 1.0f, Diff.Lunatic => 0.85f, _ => 1.35f };
    public string DiffName => Difficulty switch { Diff.Easy => "EASY", Diff.Hard => "HARD", Diff.Lunatic => "LUNATIC", _ => "NORMAL" };

    // ルナティック解禁条件（①-9）：フォロワーが一定 or 主要火力強化が一定段階。
    public const int LunaticFollowerReq = 300;
    public bool IsLunaticUnlocked => Followers >= LunaticFollowerReq || GetUpgradeLevel("shot_power") >= 4;

    // ダイブ先の受け渡し（ハブ→難易度選択→ステージ）。
    public string PendingStageScene = "res://Rei.tscn";

    // 弾幕の本数を難易度でスケール（最低1発は残す）。各ボスのリング/扇の本数に掛ける。
    public int ScaleBullets(int baseCount) => Mathf.Max(1, Mathf.RoundToInt(baseCount * BulletCountMul));

    // 累計浄化数。
    public int PurifiedCount { get; private set; }

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
        new() { Id = "rei",    Scene = "res://Rei.tscn",    Handle = "@rei_____", Tweet = "どうせ私は二番手。一番には、もうなれない。", Title = "STAGE 1 — レイ" },
        new() { Id = "akari",  Scene = "res://Akari.tscn",  Handle = "@akari.",   Tweet = "すきになって、ごめんなさい。",             Title = "STAGE 2 — あかり" },
        new() { Id = "koharu", Scene = "res://Koharu.tscn", Handle = "@koharu",   Tweet = "今日も、誰のためでもないごはんを作った。", Title = "STAGE 3 — こはる" },
    };

    private readonly HashSet<string> _cleared = new();
    // 直近にクリアしたステージ（ハブ帰還時の会話＆自動投稿トリガ。ハブが消費して null に戻す）。
    public string? JustClearedStageId;
    // コメント返信済みのステージ（セッション内・1回だけ報酬）。
    private readonly HashSet<string> _replied = new();
    public bool HasReplied(string id) => _replied.Contains(id);
    public void MarkReplied(string id) => _replied.Add(id);
    public bool IsStageCleared(string id) => _cleared.Contains(id);
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
        new() { Id = "shot_power",    Name = "光の出力",   Desc = "届ける光の威力UP",        MaxLevel = 5, BaseCost = 800,  CostMul = 1.7f },
        new() { Id = "fire_rate",     Name = "連射速度",   Desc = "発射間隔を短縮",          MaxLevel = 4, BaseCost = 700,  CostMul = 1.6f },
        new() { Id = "move_speed",    Name = "機動力",     Desc = "移動速度UP",              MaxLevel = 3, BaseCost = 500,  CostMul = 1.5f },
        new() { Id = "hitbox",        Name = "回避域",     Desc = "当たり判定を縮小",        MaxLevel = 3, BaseCost = 1200, CostMul = 1.8f },
        new() { Id = "bomb_count",    Name = "ボム所持",   Desc = "初期ボム数+1",            MaxLevel = 3, BaseCost = 900,  CostMul = 1.6f },
        new() { Id = "bomb_power",    Name = "ボム威力",   Desc = "ボムの一掃範囲UP",        MaxLevel = 3, BaseCost = 700,  CostMul = 1.5f },
        new() { Id = "max_life",      Name = "最大♥",      Desc = "ライフ上限+1",            MaxLevel = 3, BaseCost = 1500, CostMul = 1.9f },
        new() { Id = "imp_mult",      Name = "インプレ倍率", Desc = "獲得インプレUP",        MaxLevel = 4, BaseCost = 600,  CostMul = 1.6f },
        new() { Id = "fol_gain",      Name = "拡散力",     Desc = "フォロワー獲得効率UP",    MaxLevel = 3, BaseCost = 600,  CostMul = 1.6f },
        new() { Id = "combo_hold",    Name = "コンボ持続", Desc = "コンボ猶予を延長",        MaxLevel = 3, BaseCost = 400,  CostMul = 1.5f },
        new() { Id = "contam_resist", Name = "汚染耐性",   Desc = "汚染の上昇を緩和(演出は維持)", MaxLevel = 3, BaseCost = 1300, CostMul = 1.8f },
        new() { Id = "option_sub",    Name = "拡散サブ",   Desc = "追従オプションを追加",     MaxLevel = 2, BaseCost = 2000, CostMul = 2.0f },
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

    // 強化を1段購入。成功で true。購入のたびセーブ（恒久＝§0-3）。
    public bool TryPurchase(string id)
    {
        if (!CanPurchase(id)) return false;
        Impression -= GetUpgradeCost(id);
        _upgrades[id] = GetUpgradeLevel(id) + 1;
        Save();
        return true;
    }

    // ── フォロワー由来の常時バフ（天井付き・§①-5）──
    public float FollowerPowerMul => 1f + Mathf.Min(0.50f, Followers * 0.00010f);
    public float FollowerImpressionMul => 1f + Mathf.Min(0.50f, Followers * 0.00008f);

    // ── 難易度・強化由来のインプレ倍率 ──
    public static float DifficultyImpressionMulFor(Diff d) => d switch { Diff.Easy => 0.7f, Diff.Hard => 1.4f, Diff.Lunatic => 2.2f, _ => 1f };
    public float DifficultyImpressionMul => DifficultyImpressionMulFor(Difficulty);
    public float UpgradeImpressionMul => 1f + 0.12f * GetUpgradeLevel("imp_mult");
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

    // インプレを獲得（全倍率を適用して加算）。実際に加算した額を返す。
    public long GainImpression(long baseAmount)
    {
        if (baseAmount <= 0) return 0;
        long g = (long)Mathf.Round(baseAmount * TotalImpressionMul * ReplayMul);
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
        Save();
    }

    // ───────────────────────────────────────────────────────────
    // セーブ / ロード（user://save.json）。経済・強化のみ永続。
    // ───────────────────────────────────────────────────────────
    private const string SavePath = "user://save.json";

    public void Save()
    {
        var data = new Godot.Collections.Dictionary
        {
            ["impression"] = Impression,
            ["followers"] = Followers,
        };
        var up = new Godot.Collections.Dictionary();
        foreach (var kv in _upgrades)
            up[kv.Key] = kv.Value;
        data["upgrades"] = up;

        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (f != null)
            f.StoreString(Json.Stringify(data));
    }

    public void Load()
    {
        if (!FileAccess.FileExists(SavePath)) return;
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok) return;
        if (json.Data.VariantType != Variant.Type.Dictionary) return;
        var data = json.Data.AsGodotDictionary();

        if (data.ContainsKey("impression")) Impression = data["impression"].AsInt64();
        if (data.ContainsKey("followers")) Followers = data["followers"].AsInt32();
        _upgrades.Clear();
        if (data.ContainsKey("upgrades"))
        {
            var up = data["upgrades"].AsGodotDictionary();
            foreach (var k in up.Keys)
                _upgrades[k.AsString()] = up[k].AsInt32();
        }
    }

    // デバッグ用：セーブを全消去して初期状態へ（開発時のリセット）。
    public void WipeSave()
    {
        Impression = 0;
        Followers = 0;
        _upgrades.Clear();
        if (FileAccess.FileExists(SavePath))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
    }

    // やさしさゲージ（リフレイン）: グレイズ/浄化で貯まり、満タンで一時「やさしさ全開」
    private float _kindFill;            // 0..1 蓄積
    public bool IsOverload { get; private set; }
    private double _overloadT;
    private const double OverloadDur = 5.0;
    private const float GrazeGain = 0.035f;
    private const float PurifyGain = 0.12f;
    public bool JustOverloaded { get; private set; } // 発動した瞬間のフラグ（UI用、1フレーム）
    // ゲージ表示値: 全開中は残り時間、通常は蓄積量
    public float Kindness => IsOverload ? (float)(_overloadT / OverloadDur) : _kindFill;

    private double _comboTimer;
    // コンボ猶予はコンボ持続強化で延長される。
    private double ComboWindow => 2.0 + 0.4 * GetUpgradeLevel("combo_hold");
    private const int MaxCombo = 16;

    public override void _Ready()
    {
        Load();
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

    // やさしさゲージを貯める。満タンで「やさしさ全開」発動。
    private void AddKindness(float amount)
    {
        if (IsOverload) return;
        _kindFill += amount;
        if (_kindFill >= 1f)
        {
            IsOverload = true;
            _overloadT = OverloadDur;
            JustOverloaded = true;
        }
    }

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
    public void AddGraze()
    {
        Score += 10;
        AddKindness(GrazeGain);
    }

    // ボムで敵弾を消した時の小加点。
    public void AddBulletCleared()
    {
        Score += 5;
    }

    // ボムを使う。残があれば消費して true。
    public bool UseBomb()
    {
        if (Bombs <= 0)
            return false;
        Bombs--;
        return true;
    }

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
}
