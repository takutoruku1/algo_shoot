using Godot;
using System.Collections.Generic;

public readonly record struct PlayerArtFit(Vector2 Crown, float HeadHeight, float GameHeadHeight)
{
    public Vector2 Anchor => Crown + new Vector2(0f, HeadHeight * 1.35f);
    public float Scale(float bodyHeight = 36f) => GameHeadHeight / HeadHeight * (bodyHeight / 36f);
}

public static class PlayerArt
{
    private static readonly Dictionary<string, PlayerArtFit> Fits = new();

    static PlayerArt()
    {
        // Skull landmarks exclude hats, raised hands and props so aiming cannot shrink the body.
        Add("mina_default", 12.30f, new Vector3[]
        {
            new(300f, 48f, 246f),
            new(240f, 30f, 246f),
            new(246f, 30f, 246f),
            new(300f, 48f, 246f),
            new(300f, 54f, 246f),
            new(240f, 126f, 234f),
            new(216f, 45f, 222f),
            new(276f, 45f, 222f),
            new(297f, 45f, 222f),
            new(261f, 45f, 222f),
            new(216f, 45f, 222f),
        });
        Add("mina_starway", 12.30f, new Vector3[]
        {
            new(426f, 4f, 256f),
            new(268.24f, 10.08f, 244.03f),
            new(343.91f, 4.02f, 253.41f),
            new(356.97f, 10.08f, 254.12f),
            new(346.89f, 10.08f, 264.2f),
            new(341.9f, 12.07f, 267.49f),
            new(340.4f, 6.19f, 259.94f),
            new(352.74f, 8.3f, 263.52f),
            new(420f, 6.21f, 260.69f),
            new(371.15f, 10.14f, 269.75f),
            new(246.1f, 4.07f, 268.47f),
        });
        Add("akari_default", 14.10f, new Vector3[]
        {
            new(240f, 0f, 282f),
            new(225f, 0f, 282f),
            new(231f, 0f, 282f),
            new(237f, 0f, 282f),
            new(225f, 0f, 282f),
            new(210f, 45f, 264f),
            new(207f, 0f, 294f),
            new(237f, 0f, 282f),
            new(234f, 0f, 282f),
            new(207f, 0f, 282f),
            new(198f, 0f, 294f),
        });
        Add("akari_dayoff", 14.10f, new Vector3[]
        {
            new(262.96f, 8.35f, 269.22f),
            new(251.26f, 8.45f, 274.49f),
            new(258.19f, 8.4f, 277.08f),
            new(263.86f, 8.18f, 274.09f),
            new(255.09f, 10.29f, 271.54f),
            new(246.21f, 10.34f, 283.45f),
            new(237.2f, 8.4f, 277.08f),
            new(258.78f, 8.35f, 285.91f),
            new(236.54f, 8.3f, 273.89f),
            new(176.84f, 10.53f, 277.89f),
            new(182.65f, 8.5f, 280.35f),
        });
        Add("koharu_default", 15.30f, new Vector3[]
        {
            new(234f, 0f, 306f),
            new(177f, 30f, 246f),
            new(234f, 0f, 279f),
            new(222f, 0f, 288f),
            new(225f, 0f, 297f),
            new(192f, 48f, 291f),
            new(195f, 0f, 321f),
            new(219f, 0f, 306f),
            new(231f, 0f, 300f),
            new(210f, 0f, 297f),
            new(183f, 0f, 297f),
        });
        Add("koharu_rain", 15.30f, new Vector3[]
        {
            new(277.83f, 7.83f, 283.7f),
            new(205.18f, 108.19f, 246.22f),
            new(254.24f, 12.82f, 282.02f),
            new(246.98f, 12.56f, 278.37f),
            new(237.91f, 12.52f, 290.09f),
            new(238.58f, 14.91f, 291.83f),
            new(206.9f, 8.28f, 300f),
            new(211.64f, 8.3f, 302.94f),
            new(254.12f, 8.47f, 309.18f),
            new(209.1f, 10.78f, 293.17f),
            new(205.41f, 10.81f, 298.38f),
        });
        Add("rei_default", 13.50f, new Vector3[]
        {
            new(354f, 0f, 270f),
            new(249f, 0f, 246f),
            new(312f, 0f, 258f),
            new(345f, 0f, 255f),
            new(333f, 0f, 264f),
            new(243f, 42f, 255f),
            new(240f, 0f, 270f),
            new(327f, 0f, 270f),
            new(372f, 0f, 270f),
            new(315f, 0f, 270f),
            new(216f, 0f, 270f),
        });
        Add("rei_encore", 13.50f, new Vector3[]
        {
            new(402.95f, 8.18f, 278.18f),
            new(251.72f, 107.35f, 218.41f),
            new(306.06f, 13.21f, 264.22f),
            new(380.18f, 6.37f, 284.6f),
            new(386.71f, 8.55f, 294.84f),
            new(348.32f, 8.5f, 297.35f),
            new(338.7f, 10.65f, 298.22f),
            new(354.69f, 10.62f, 290.97f),
            new(381.79f, 8.3f, 286.34f),
            new(346.36f, 8.4f, 281.28f),
            new(268.44f, 8.32f, 278.84f),
        });
    }

    private static void Add(string id, float gameHeadHeight, Vector3[] landmarks)
    {
        var item = Cosmetics.Find(id)!;
        string[] poses = { "idle", "aim_u", "aim_ur", "aim_r", "aim_dr", "aim_d", "spin_00", "spin_01", "spin_02", "spin_03", "spin_04" };
        for (int i = 0; i < poses.Length; i++)
        {
            var head = landmarks[i];
            Fits.Add(item.PosePath(poses[i]), new PlayerArtFit(new Vector2(head.X, head.Y), head.Z, gameHeadHeight));
        }
    }

    public static PlayerArtFit Fit(Texture2D texture) => Fits[texture.ResourcePath];

    public static Rect2 TextureRect(Texture2D texture, Vector2 center, float bodyHeight, bool flip = false)
    {
        var fit = Fit(texture);
        float scale = fit.Scale(bodyHeight);
        var size = texture.GetSize() * scale;
        var anchor = fit.Anchor;
        if (flip) anchor.X = texture.GetWidth() - anchor.X;
        var pos = center - anchor * scale;
        return new Rect2(pos, new Vector2(flip ? -size.X : size.X, size.Y));
    }

    public static void Draw(CanvasItem canvas, Texture2D texture, Vector2 center, float bodyHeight, bool flip = false)
        => canvas.DrawTextureRect(texture, TextureRect(texture, center, bodyHeight, flip), false);
}
