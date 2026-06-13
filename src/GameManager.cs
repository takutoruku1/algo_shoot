using Godot;

// GameManager : Autoload シングルトン (/root/Game)。
// スコア・コンボ・ボム数などのゲーム状態を一元管理する。
public partial class GameManager : Node
{
    public long Score { get; private set; }
    public int Combo { get; private set; }
    public int Bombs { get; private set; } = 3;

    // 難易度（オートロードなのでシーンをまたいで保持）。
    public enum Diff { Easy, Normal, Hard }
    public Diff Difficulty = Diff.Normal;
    public int StartLives => Difficulty switch { Diff.Easy => 5, Diff.Hard => 2, _ => 3 };
    public int StartBombs => Difficulty switch { Diff.Easy => 5, Diff.Hard => 2, _ => 3 };
    public float BulletSpeedMul => Difficulty switch { Diff.Easy => 0.72f, Diff.Hard => 1.18f, _ => 1f };
    public float BossHpMul => Difficulty switch { Diff.Easy => 0.55f, Diff.Hard => 1.25f, _ => 1f };
    public float DanmakuIntervalMul => Difficulty switch { Diff.Easy => 1.6f, Diff.Hard => 0.85f, _ => 1f };
    public string DiffName => Difficulty switch { Diff.Easy => "EASY", Diff.Hard => "HARD", _ => "NORMAL" };

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
    private const double ComboWindow = 2.0; // この時間内に倒し続けるとコンボ継続
    private const int MaxCombo = 16;

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

    // ラン開始時のリセット。
    public void ResetRun()
    {
        Score = 0;
        Combo = 0;
        _comboTimer = 0;
        Bombs = StartBombs;
        PurifiedCount = 0;
        _kindFill = 0f;
        IsOverload = false;
        _overloadT = 0;
    }
}
