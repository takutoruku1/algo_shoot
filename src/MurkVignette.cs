using Godot;

// MurkVignette : 汚染が高いと画面端から寄ってくる濁色ビネット（#2-C・従の演出）。
//   ・GameManager.Contamination を毎フレーム読み、α を決める（0.30未満は完全非表示）。
//   ・α上限 0.21。中央（プレイ主戦場 中心±60px相当）はラジアルで完全に抜く＝弾・自機の視認を侵さない。
//   ・ZIndex は弾(0..)/自機より奥（負）。背景を濁らせるだけで、弾・自機・当たり判定の上には絶対描かない。
//   ・色は Player と統一の藍鼠(MurkTint)。設計座標は世界の 384x216。
public partial class MurkVignette : Node2D
{
    private const float W = 384f, H = 216f;
    // プレイ主戦場の中心と、ここだけは濁らせない半径（HUD設計の±200px≒世界では±60px相当）。
    private static readonly Vector2 Center = new Vector2(W * 0.5f, H * 0.5f);
    private const float ClearR = 60f;                  // ここより内側はα0（中央の弾を絶対に隠さない）
    private const float FullR = 230f;                  // ここより外でα上限に達する（画面端）
    private static readonly Color MurkTint = new Color(0.42f, 0.40f, 0.52f); // 自機ティントと統一の藍鼠

    private float _alpha;   // 現在のビネット強度（0..0.21）。汚染から算出して滑らかに追従。

    public override void _Ready()
    {
        // board.png(-90)/ScrollFx(-70..-55)/StageImagery(-50) の上、弾(0..)・自機の奥に置く。
        ZIndex = -45;
        ZAsRelative = false;
    }

    public override void _Process(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        float c = game?.Contamination ?? 0f;
        // 汚染0.30未満は完全非表示。0.30→1.00 で 0→0.21 に上がる。STAGE3末≈0.13・FINAL=0.21。
        float target = 0.21f * Mathf.Clamp((c - 0.30f) / 0.70f, 0f, 1f);
        _alpha = Mathf.MoveToward(_alpha, target, (float)delta * 0.5f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_alpha <= 0.001f) return;
        // 中央を抜いた矩形リングを同心で重ね、外ほど濃く＝端から濁りが寄るラジアル。
        // 各リングのαは ClearR→FullR で 0→_alpha に線形。中央±ClearR は一切描かない。
        const int rings = 14;
        for (int i = 0; i < rings; i++)
        {
            float t0 = (float)i / rings;        // 0(内)..1(外)
            float t1 = (float)(i + 1) / rings;
            float r0 = Mathf.Lerp(ClearR, FullR, t0);
            float r1 = Mathf.Lerp(ClearR, FullR, t1);
            // このリングの代表αは外縁側の強さ（内ほど薄く端ほど濃い）。
            float a = _alpha * t1;
            if (a <= 0.001f) continue;
            DrawRing(r0, r1, new Color(MurkTint, a));
        }
    }

    // 半径 r0..r1 の正方形リング（4枚の矩形）を Center 中心に描く。中央(±r0)は抜ける。
    private void DrawRing(float r0, float r1, Color col)
    {
        float l0 = Center.X - r0, r0x = Center.X + r0, t0 = Center.Y - r0, b0 = Center.Y + r0;
        float l1 = Center.X - r1, r1x = Center.X + r1, t1 = Center.Y - r1, b1 = Center.Y + r1;
        // 上帯・下帯・左帯・右帯（内枠を避けて画面全幅で覆う）。画面外にはみ出す分はクリップされる。
        DrawRect(new Rect2(l1, t1, r1x - l1, t0 - t1), col);          // 上
        DrawRect(new Rect2(l1, b0, r1x - l1, b1 - b0), col);          // 下
        DrawRect(new Rect2(l1, t0, l0 - l1, b0 - t0), col);          // 左
        DrawRect(new Rect2(r0x, t0, r1x - r0x, b0 - t0), col);        // 右
    }
}
