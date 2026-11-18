using Godot;

internal static class FinalAccount
{
    public const string Name = "繧ヲ縺";
    public const string Handle = "@ø7_ç0__ñ";

    public static ImageTexture CreateIcon()
    {
        using var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        image.Fill(new Color("182833"));
        for (int y = 3; y < 64; y += 6)
            image.FillRect(new Rect2I(0, y, 64, 1), new Color("273f4b"));
        var ink = new Color("a9bbc2");
        image.FillRect(new Rect2I(19, 17, 23, 5), ink);
        image.FillRect(new Rect2I(38, 22, 6, 7), ink);
        image.FillRect(new Rect2I(25, 29, 15, 5), new Color("68a3a9"));
        image.FillRect(new Rect2I(30, 34, 6, 5), ink);
        image.FillRect(new Rect2I(27, 44, 6, 5), ink);
        image.FillRect(new Rect2I(12, 26, 12, 2), new Color("567581"));
        image.FillRect(new Rect2I(44, 39, 11, 2), new Color("567581"));
        return ImageTexture.CreateFromImage(image);
    }
}
