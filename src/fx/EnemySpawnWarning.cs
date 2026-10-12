using Godot;

public partial class EnemySpawnWarning : Node2D
{
    private float _progress;
    private static readonly Color Ink = new("19121f");
    private static readonly Color Amber = new("ffd483");
    private static readonly Color Light = new("fff1ce");

    public override void _Ready() => ZIndex = -2;

    // The spawner owns the clock so dialogue pauses cannot consume the warning.
    public void SetProgress(float value)
    {
        _progress = Mathf.Clamp(value, 0, 1);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Stroke(new Vector2(3, -11), new Vector2(0, -11), Amber);
        Stroke(new Vector2(0, -11), new Vector2(0, 11), Amber);
        Stroke(new Vector2(0, 11), new Vector2(3, 11), Amber);
        float gap = 10 * (1 - _progress);
        Stroke(new Vector2(0, -10), new Vector2(0, -gap), Light);
        Stroke(new Vector2(0, gap), new Vector2(0, 10), Light);
        for (int i = 0; i < 2; i++)
        {
            float x = 4 + i * 7 + Mathf.PosMod(_progress * 2, 1) * 3;
            Stroke(new Vector2(x, -4), new Vector2(x + 4, 0), Amber);
            Stroke(new Vector2(x + 4, 0), new Vector2(x, 4), Amber);
        }
    }

    private void Stroke(Vector2 from, Vector2 to, Color color)
    {
        DrawLine(from, to, Ink, 3.8f, true);
        DrawLine(from, to, color, 1.4f, true);
    }
}
