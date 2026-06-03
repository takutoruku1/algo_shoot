using Godot;

// PageShard : Enemy (E5 破壊可能オブジェ/無攻撃)
// Hp=3。約32x40px。ゆっくり左へ漂う(約-40px/s)＋上下に軽く揺れる。攻撃しない。
// 破壊で浄化演出。
// _Draw: セピアのページ片＋黒インクのまだら＋ふちのちらつき。
public partial class PageShard : Enemy
{
    private const float DriftSpeed = 40f;     // 左へ
    private const float BobAmplitude = 8f;    // 上下揺れ幅(px)
    private const float BobSpeed = 1.6f;       // 揺れ速度

    private double _time = 0.0;
    private float _baseY;
    private float _flickerTimer = 0.0f;
    private float _flicker = 1.0f;

    protected override void OnEnemyReady()
    {
        Hp = 3;
        // 約32x40px → 当たり半径は内接円寄りで設定。
        Radius = 16f;
        _baseY = GlobalPosition.Y;
        QueueRedraw();
    }

    protected override void UpdateMovement(double delta)
    {
        _time += delta;

        // ゆっくり左へ漂う。
        float x = GlobalPosition.X - DriftSpeed * (float)delta;
        // 上下に軽く揺れる（基準Yを基準にサイン）。
        _baseY += 0f; // 基準は固定。横移動のみ進行。
        float y = _baseY + Mathf.Sin((float)_time * BobSpeed) * BobAmplitude;
        GlobalPosition = new Vector2(x, y);

        // ふちのちらつき更新。
        _flickerTimer += (float)delta;
        if (_flickerTimer >= 0.08f)
        {
            _flickerTimer = 0f;
            _flicker = (float)GD.RandRange(0.6, 1.0);
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // 基底の浄化演出を尊重。
        base._Draw();

        // 浄化演出中は本体を描かない（フラッシュ＋パーティクルのみ）。
        if (IsDying)
            return;

        const float halfW = 16f; // 約32px幅
        const float halfH = 20f; // 約40px高

        // セピアのページ片（少し傾けた矩形）。
        Color sepia = new Color(0.85f, 0.74f, 0.55f);
        Color sepiaEdge = new Color(0.62f, 0.5f, 0.34f);

        Vector2[] page =
        {
            new Vector2(-halfW + 2f, -halfH),
            new Vector2(halfW, -halfH + 3f),
            new Vector2(halfW - 2f, halfH),
            new Vector2(-halfW, halfH - 3f),
        };
        DrawColygon(page, sepia);

        // ふちのちらつき（ふちを明滅する線で描く）。
        Color edge = sepiaEdge;
        edge.A = _flicker;
        for (int i = 0; i < page.Length; i++)
        {
            Vector2 a = page[i];
            Vector2 b = page[(i + 1) % page.Length];
            DrawLine(a, b, edge, 1.5f);
        }

        // 黒インクのまだら。
        Color ink = new Color(0.1f, 0.08f, 0.06f, 0.6f);
        DrawCircle(new Vector2(-6f, -8f), 2.4f, ink);
        DrawCircle(new Vector2(4f, -2f), 3.0f, ink);
        DrawCircle(new Vector2(-2f, 9f), 2.0f, ink);
        DrawCircle(new Vector2(8f, 12f), 1.6f, ink);
        DrawCircle(new Vector2(-9f, 5f), 1.2f, ink);
    }

    // 単色ポリゴン塗り（頂点配列を同色で埋める）。
    private void DrawColygon(Vector2[] points, Color color)
    {
        var colors = new Color[points.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = color;
        }
        DrawPolygon(points, colors);
    }
}
