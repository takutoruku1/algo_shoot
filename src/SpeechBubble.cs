using Godot;

// SpeechBubble : ドット絵調の吹き出し。角を四角くノッチした“カクカク”の枠＋太めの縁＋しっぽ。
// HUD が Position と SetBox(サイズ, しっぽX) を指定して使う。
public partial class SpeechBubble : Node2D
{
    private static readonly Color Fill = new Color("fff7ea");   // クリーム
    private static readonly Color Border = new Color("4a3a5e"); // 暗い紫
    private const int Notch = 4; // 角の段差（大きいほどカクカク）
    private const int B = 2;     // 縁の太さ

    private Vector2 _size = new Vector2(120, 24);
    private float _tailX = -1f;  // しっぽのローカルX（負で無し）

    public void SetBox(Vector2 size, float tailX)
    {
        _size = size;
        _tailX = tailX;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float w = _size.X, h = _size.Y;
        // 縁（暗）→ 中身（クリーム）を一回り内側に重ねて太枠に
        NotchedRect(0, 0, w, h, Notch, Border);
        NotchedRect(B, B, w - 2 * B, h - 2 * B, Notch - 1, Fill);

        // しっぽ（下向きの段々）
        if (_tailX >= 0f)
        {
            float x = _tailX;
            DrawRect(new Rect2(x, h - B, 8, 2), Border);
            DrawRect(new Rect2(x, h, 6, 2), Border);
            DrawRect(new Rect2(x, h + 2, 4, 2), Border);
            DrawRect(new Rect2(x + 1, h - B, 6, 2), Fill);
            DrawRect(new Rect2(x + 1, h, 4, 2), Fill);
        }
    }

    // 四隅を Notch だけ四角く欠いた“ドット丸み”の矩形
    private void NotchedRect(float x, float y, float w, float h, int c, Color col)
    {
        if (c < 0) c = 0;
        DrawRect(new Rect2(x, y + c, w, h - 2 * c), col);       // 中央（全幅）
        DrawRect(new Rect2(x + c, y, w - 2 * c, c), col);       // 上
        DrawRect(new Rect2(x + c, y + h - c, w - 2 * c, c), col); // 下
    }
}
