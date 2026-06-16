using Godot;

// HeartsBar : 残機をドット絵のハートで表示する（フォントの♥グリフに依存しない）。
// 7x6 のピクセルハートを赤＋白1pxアウトラインで Count 個並べて描く。
public partial class HeartsBar : Node2D
{
    private static readonly string[] Pattern =
    {
        ".XX.XX.",
        "XXXXXXX",
        "XXXXXXX",
        ".XXXXX.",
        "..XXX..",
        "...X...",
    };

    private const int Cell = 2;     // 1セルのpxサイズ（2 → ハート約14x12px）
    private const int GapCells = 2; // ハート間の隙間（セル数）

    private readonly Color _fill = Ui.Hp;    // ハート＝HP桃（Refrain 役割色）

    private int _count = 3;

    public void SetCount(int n)
    {
        _count = Mathf.Max(0, n);
        QueueRedraw();
    }

    public override void _Draw()
    {
        int hw = Pattern[0].Length; // 7
        int hh = Pattern.Length;    // 6
        int stride = (hw + GapCells) * Cell;

        for (int k = 0; k < _count; k++)
        {
            float ox = k * stride;

            // 本体（赤）
            for (int r = 0; r < hh; r++)
                for (int c = 0; c < hw; c++)
                    if (Pattern[r][c] == 'X')
                        DrawRect(new Rect2(ox + c * Cell, r * Cell, Cell, Cell), _fill);
        }
    }
}
