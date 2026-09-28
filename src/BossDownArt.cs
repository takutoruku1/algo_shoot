using Godot;

public static class BossDownArt
{
    public static string Path(string id) => $"res://char/v3/down/{id}_v1.png";

    public static (float Scale, Vector2 Offset) Frame(Texture2D texture)
    {
        // Match the standing head size, not the shorter folded-body silhouette.
        var (crown, headHeight, ratio, target) = texture.ResourcePath.GetFile() switch
        {
            "akari_v1.png" => (new Vector2(320, 3), 258f, 0.38f, new Vector2(0.097f, -0.50f)),
            "akari_mid_v1.png" => (new Vector2(289, 3), 268f, 0.319f, new Vector2(0.056f, -0.34f)),
            "koharu_v1.png" => (new Vector2(343, 3), 268f, 0.246f, new Vector2(0.214f, -0.31f)),
            "koharu_mid_v1.png" => (new Vector2(324, 3), 274f, 0.306f, new Vector2(0.046f, -0.34f)),
            "rei_v1.png" => (new Vector2(280, 3), 247f, 0.301f, new Vector2(0.019f, -0.42f)),
            "rei_form2_v1.png" => (new Vector2(275, 3), 247f, 0.301f, new Vector2(0.019f, -0.42f)),
            "rei_mid_v1.png" => (new Vector2(198, 4), 271f, 0.367f, new Vector2(0.017f, -0.34f)),
            "mina_v1.png" => (new Vector2(379, 6), 325f, 0.410f, new Vector2(0, -0.40f)),
            "mina_rain_v1.png" => (new Vector2(355, 6), 325f, 0.358f, new Vector2(0, -0.40f)),
            "mina_screen_v1.png" => (new Vector2(371, 6), 325f, 0.375f, new Vector2(0, -0.40f)),
            "mina_stream_v1.png" => (new Vector2(376, 6), 328f, 0.368f, new Vector2(0, -0.40f)),
            "mina_home_v1.png" => (new Vector2(359, 6), 328f, 0.374f, new Vector2(0, -0.40f)),
            _ => throw new System.ArgumentOutOfRangeException(nameof(texture), texture.ResourcePath, "Unknown boss down art"),
        };
        float scale = texture.GetHeight() * ratio / headHeight;
        return (scale, texture.GetSize() / 2f - crown + target * texture.GetHeight() / scale);
    }
}
