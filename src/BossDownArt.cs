using Godot;

public static class BossDownArt
{
    public static string Path(string id) => $"res://char/v3/down/{id}_v1.png";

    public static string BreathingPath(string downPath, int frame)
    {
        string id = downPath.GetFile().TrimSuffix("_v1.png").TrimSuffix("_down");
        return $"res://char/v3/down/breathing/{id}_breath_{frame}_v1.png";
    }

    public static bool IsBreathing(Texture2D texture) => texture.ResourcePath.Contains("/down/breathing/");

    private static (float Scale, Vector2 Offset) BreathingFrame(Texture2D texture)
    {
        string file = texture.ResourcePath.GetFile();
        int split = file.IndexOf("_breath_", System.StringComparison.Ordinal);
        string id = file[..split];
        int frame = file[split + 8] - '0';
        if (id == "mina_dragon")
        {
            float height = frame switch { 0 => 517f, 1 => 511f, _ => 512f };
            var core = new Vector2(frame == 0 ? 408 : 409, 263) * (720f / height);
            return (1.85f * 1080f / 1024f * height / 720f, texture.GetSize() / 2f - core);
        }

        // Register on the unchanged head, so only the drawn shoulders breathe.
        var (crown, headHeight, ratio, target) = id switch
        {
            "akari" => (new Vector2(frame switch { 0 => 214, 1 => 206, _ => 205 }, 68), 202f, .38f, new Vector2(.097f, -.50f)),
            "akari_mid" => (new Vector2(frame switch { 0 => 222, 1 => 219, _ => 215 }, 26), 219f, .319f, new Vector2(.056f, -.34f)),
            "koharu" => (new Vector2(frame == 0 ? 223 : 225, 55), 217f, .246f, new Vector2(.214f, -.31f)),
            "koharu_mid" => (new Vector2(frame == 2 ? 207 : 210, 78), 212f, .306f, new Vector2(.046f, -.34f)),
            "rei" => (new Vector2(402, 29), 221f, .301f, new Vector2(.019f, -.42f)),
            "rei_form2" => (new Vector2(frame == 2 ? 410 : 408, 18), 234f, .301f, new Vector2(.019f, -.42f)),
            "rei_mid" => (new Vector2(frame switch { 0 => 169, 1 => 160, _ => 147 }, 22), 246f, .367f, new Vector2(.017f, -.34f)),
            "mina" => (new Vector2(frame switch { 0 => 204, 1 => 195, _ => 200 }, 120), 208f, .410f, new Vector2(0, -.40f)),
            "mina_rain" => (new Vector2(438, 6), 319f, .358f, new Vector2(0, -.40f)),
            _ => throw new System.ArgumentOutOfRangeException(nameof(texture), file, "Unknown breathing art"),
        };
        float scale = texture.GetHeight() * ratio / headHeight;
        return (scale, texture.GetSize() / 2f - crown + target * texture.GetHeight() / scale);
    }

    public static (float Scale, Vector2 Offset) Frame(Texture2D texture)
    {
        if (IsBreathing(texture)) return BreathingFrame(texture);
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
