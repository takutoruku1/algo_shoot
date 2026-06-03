using Godot;

// GameManager : Autoload シングルトン (/root/Game)。
// スコア・コンボ・ボム数などのゲーム状態を一元管理する。
public partial class GameManager : Node
{
    public long Score { get; private set; }
    public int Combo { get; private set; }
    public int Bombs { get; private set; } = 3;

    private double _comboTimer;
    private const double ComboWindow = 2.0; // この時間内に倒し続けるとコンボ継続
    private const int MaxCombo = 16;

    public override void _Process(double delta)
    {
        if (_comboTimer > 0)
        {
            _comboTimer -= delta;
            if (_comboTimer <= 0)
                Combo = 0;
        }
    }

    // 敵を浄化（撃破）した時の加点。コンボ倍率がかかる。
    public void AddPurify(int basePoints)
    {
        Combo = Mathf.Min(Combo + 1, MaxCombo);
        _comboTimer = ComboWindow;
        Score += basePoints * Mathf.Max(1, Combo);
    }

    // 敵弾をかすった（グレイズ）時の加点。
    public void AddGraze()
    {
        Score += 10;
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
        Bombs = 3;
    }
}
