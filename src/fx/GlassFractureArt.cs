using Godot;
using System.Collections.Generic;

public static class GlassFractureArt
{
    public const string Folder = "res://char/v3/fx/akari/realm/";
    private static readonly string[] Paths = { "fracture_branch_v1.png", "fracture_impact_v1.png", "fracture_fault_v1.png" };
    private static readonly Texture2D?[] Textures = new Texture2D?[3];

    public static Sprite2D Layer(int variant, Rect2 rect, bool flipH = false, bool flipV = false)
    {
        var texture = Textures[variant] ??= GD.Load<Texture2D>(Folder + Paths[variant]);
        return new Sprite2D
        {
            Texture = texture, Centered = false, Position = rect.Position, Scale = rect.Size / texture.GetSize(),
            FlipH = flipH, FlipV = flipV, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add,
                LightMode = CanvasItemMaterial.LightModeEnum.Unshaded },
            Modulate = new Color(1, 1, 1, 0),
        };
    }

    public static (Vector2[] points, int[] triangles) Mesh(Rect2 rect, int detail, ulong seed)
    {
        var points = new List<Vector2> { rect.Position, new(rect.End.X, rect.Position.Y), rect.End, new(rect.Position.X, rect.End.Y) };
        foreach (float edge in new[] { 0.19f, 0.51f, 0.83f })
        {
            points.Add(rect.Position + new Vector2(rect.Size.X * edge, 0));
            points.Add(rect.Position + new Vector2(rect.Size.X * edge, rect.Size.Y));
            points.Add(rect.Position + new Vector2(0, rect.Size.Y * edge));
            points.Add(rect.Position + new Vector2(rect.Size.X, rect.Size.Y * edge));
        }
        var rng = new RandomNumberGenerator { Seed = seed };
        for (int i = 0; i < 5 + detail * 3; i++)
            points.Add(rect.Position + rect.Size * new Vector2(rng.RandfRange(0.08f, 0.92f), rng.RandfRange(0.08f, 0.92f)));
        var mesh = points.ToArray();
        return (mesh, Geometry2D.TriangulateDelaunay(mesh));
    }

    public static void DrawShards(CanvasItem canvas, Texture2D texture, Rect2 rect, Vector2[] vertices, int[] triangles,
        float time, float force, Color tint)
    {
        float fade = 1 - Mathf.SmoothStep(0.65f, 2.0f, time);
        if (fade <= 0) return;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var source = new[] { vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]] };
            var center = (source[0] + source[1] + source[2]) / 3;
            float t = Mathf.Max(0, time - 0.07f - i % 7 * 0.006f);
            var outward = (center - rect.GetCenter()) / rect.Size;
            var move = outward * rect.Size * (t * 0.18f + t * t * 0.78f) * force
                + new Vector2(0, rect.Size.Y * t * t * 0.16f);
            float rotation = (i % 2 == 0 ? 1 : -1) * t * (0.18f + i % 5 * 0.09f) * force;
            var points = new Vector2[3];
            var uv = new Vector2[3];
            for (int j = 0; j < 3; j++)
            {
                var local = (source[j] - center).Rotated(rotation);
                local.X *= Mathf.Max(0.2f, Mathf.Cos(t * (0.35f + i % 3 * 0.13f)));
                points[j] = center + local + move;
                uv[j] = (source[j] - rect.Position) / rect.Size;
            }
            canvas.DrawPolygon(points, new[] { new Color(tint, tint.A * fade) }, uv, texture);
            if (time > 0.065f)
                canvas.DrawPolyline(new[] { points[0], points[1], points[2], points[0] },
                    new Color(0.82f, 0.92f, 1, fade * 0.65f), rect.Size.X / 1300f, true);
        }
    }
}
