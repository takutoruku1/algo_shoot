using Godot;
using System.Collections.Generic;

public static class DialogueBox
{
    public static readonly Rect2 FullScreen = new(72, 504, 1136, 196);
    public static Rect2 Board => new(Field.DLeft + 20, FullScreen.Position.Y, Field.DWidth - 40, FullScreen.Size.Y);
    public static readonly Color Surface = new("101820"), Border = new("53616d"), Ink = new("eef3f5");
    public static UiKit.TextStyle Body => new(UiKit.Zen, 24, 0, 1.2f);
    public const float Padding = 24, HeaderHeight = 54;
    private static Texture2D? _draft;

    public static Vector2 Anchor(Rect2 box) => new(box.End.X, box.Position.Y);
    public static float WrapWidth(Rect2 box) => box.Size.X - Padding * 2;
    public static Vector2 TextPosition(Rect2 box) => box.Position + new Vector2(Padding, 66);

    public static void DrawFrame(CanvasItem ci, Rect2 box, string speaker, Color accent,
        Texture2D? portrait = null, bool draft = false, float alpha = 1, float faceTop = 0)
    {
        UiKit.Box(ci, new Rect2(box.Position + new Vector2(0, 4), box.Size), new Color("05090d", 0.24f * alpha), 8);
        UiKit.Box(ci, box, new Color(Surface, 0.96f * alpha), 8, new Color(Border, 0.7f * alpha), 1);
        ci.DrawLine(box.Position + new Vector2(Padding, HeaderHeight),
            new Vector2(box.End.X - Padding, box.Position.Y + HeaderHeight), new Color(Border, 0.4f * alpha), 1, true);
        ci.DrawLine(box.Position + new Vector2(Padding, 0), box.Position + new Vector2(Padding + 40, 0),
            new Color(accent, 0.9f * alpha), 2, true);
        var center = box.Position + new Vector2(40, 27);
        if (portrait != null)
            UiKit.FaceAvatar(ci, center, 17, portrait, accent, false, faceTop, alpha);
        else if (draft)
        {
            _draft ??= GD.Load<Texture2D>("res://char/ui/dialogue_you_v1.png");
            var size = _draft.GetSize() * (32f / Mathf.Max(_draft.GetWidth(), _draft.GetHeight()));
            ci.DrawTextureRect(_draft, new Rect2(center - size / 2, size), false, new Color(Colors.White, alpha));
        }
        else
            ci.DrawCircle(center, 3, new Color(accent, alpha));
        UiKit.Text(ci, UiKit.ZenBold, box.Position + new Vector2(70, 14), speaker, 18, new Color(accent, alpha));
    }

    public static void DrawBody(CanvasItem ci, Rect2 box, string page, int shown, float alpha = 1, Color? ink = null)
    {
        UiKit.TypewriterLines(ci, Body.Font, new List<string>(page.Split('\n')),
            TextPosition(box) + new Vector2(0, Body.Font.GetAscent(Body.Size)), WrapWidth(box), Body.Size,
            new Color(ink ?? Ink, alpha), shown, extraLeading: Body.ExtraLeading);
    }

    public static void DrawContinue(CanvasItem ci, Rect2 box, bool more)
    {
        var button = new Rect2(box.End.X - Padding - 100, box.End.Y - 32, 100, 24);
        bool hover = button.HasPoint(Pad.MousePos());
        UiKit.Box(ci, button, new Color(Ink, hover ? 0.12f : 0.04f), 4,
            new Color(Border, hover ? 0.9f : 0.55f), 1);
        UiKit.Text(ci, UiKit.Zen, button.Position + new Vector2(0, 2), more ? "つづき  ›" : "次へ  ›",
            13, new Color(Ink, hover ? 1 : 0.85f), HorizontalAlignment.Center, button.Size.X);
    }
}
