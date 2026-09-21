using Godot;

public enum PowerKind { Line, Speed, Life, Shield }

public static class PowerPickupArt
{
    private static readonly string[] Names = { "line", "speed", "life", "shield" };
    private static readonly Color[] ColorsByKind = { new("ffc24e"), new("cfed48"), new("41e69b"), new("edf0e9") };
    private static readonly Texture2D?[] Textures = new Texture2D?[4];
    private static readonly bool[] Missing = new bool[4];
    public static Color ColorFor(PowerKind kind) => ColorsByKind[(int)kind];

    public static Texture2D? TextureFor(PowerKind kind)
    {
        int i = (int)kind;
        if (Textures[i] != null) return Textures[i];
        if (Missing[i]) return null;                    // 一度失敗した絵は毎フレーム読み直さない
        string path = $"res://char/items/{Names[i]}_v1.png";
        // インポート漏れの絵は GD.Load が null を返す。落とさず枠だけで描く。
        using var image = (ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null)?.GetImage();
        if (image == null)
        {
            Missing[i] = true;
            return null;
        }
        image.Resize(128, 128, Image.Interpolation.Lanczos);
        image.GenerateMipmaps();
        return Textures[i] = ImageTexture.CreateFromImage(image);
    }

    public static void Draw(CanvasItem canvas, Rect2 rect, PowerKind kind, float alpha = 1f)
    {
        float unit = rect.Size.X / 16f;
        Color color = ColorFor(kind);
        canvas.DrawRect(rect.Grow(unit), new Color("121a20") with { A = alpha });
        canvas.DrawRect(rect, new Color("202a30") with { A = alpha });
        canvas.DrawRect(rect.Grow(-unit * 0.5f), color with { A = alpha }, false, unit);
        if (TextureFor(kind) is { } icon)               // 絵が無ければ枠と色だけ（拾える挙動は変わらない）
            canvas.DrawTextureRect(icon, rect.Grow(-unit * 1.5f), false, new Color(1, 1, 1, alpha));
        canvas.DrawLine(rect.Position + new Vector2(unit, unit), rect.Position + new Vector2(5f * unit, unit),
            new Color(1, 1, 1, alpha), unit);
    }
}
