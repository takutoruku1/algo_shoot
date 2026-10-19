using Godot;
using System.Collections.Generic;
using System.Linq;

internal sealed class BulletFootprint
{
    public Shape2D[] Shapes { get; }
    public Rect2[] ShapeBounds { get; }
    public float Radius { get; }
    public Rect2 Bounds { get; }
    public Vector2[] Hull { get; }
    public Vector2[][] Contours { get; }
    private static readonly Dictionary<Texture2D, Vector2[][]> Outlines = new();
    private static readonly Dictionary<(Texture2D, Vector2, float), BulletFootprint> Textures = new();
    private static readonly Dictionary<(BulletShape, float), BulletFootprint> Primitives = new();

    private BulletFootprint(Shape2D[] shapes, float radius, Vector2[][]? contours = null)
    {
        Shapes = shapes;
        ShapeBounds = shapes.Select(shape => shape.GetRect()).ToArray();
        Contours = contours ?? System.Array.Empty<Vector2[]>();
        Radius = radius;
        if (shapes.Length == 1 && shapes[0] is CircleShape2D)
        {
            Bounds = new Rect2(-Vector2.One * radius, Vector2.One * radius * 2);
            Hull = System.Array.Empty<Vector2>();
        }
        else
        {
            Hull = Geometry2D.ConvexHull(shapes.Cast<ConvexPolygonShape2D>().SelectMany(s => s.Points).ToArray());
            var bounds = new Rect2(Hull[0], Vector2.Zero);
            foreach (var point in Hull) bounds = bounds.Expand(point);
            Bounds = bounds;
        }
    }

    public static BulletFootprint Texture(Texture2D texture, Vector2 pivot, float size)
    {
        var key = (texture, pivot, size);
        if (Textures.TryGetValue(key, out var cached)) return cached;
        float longest = Mathf.Max(texture.GetWidth(), texture.GetHeight());
        var polygons = TextureOutlines(texture).Select(p => p.Select(v => (v - pivot / longest) * size).ToArray());
        return Textures[key] = Polygons(polygons);
    }

    private static Vector2[][] TextureOutlines(Texture2D texture)
    {
        if (Outlines.TryGetValue(texture, out var cached)) return cached;
        using var image = texture.GetImage();
        if (image.IsCompressed()) image.Decompress();
        Vector2 original = image.GetSize();
        float longest = Mathf.Max(original.X, original.Y);
        float shrink = Mathf.Min(1, 96f / longest);
        image.Resize(Mathf.Max(1, Mathf.RoundToInt(original.X * shrink)), Mathf.Max(1, Mathf.RoundToInt(original.Y * shrink)), Image.Interpolation.Bilinear);
        using var mask = new Bitmap();
        mask.CreateFromImageAlpha(image, 0.6f);
        var size = image.GetSize();
        var cuts = HoleCuts(mask);
        var polygons = new List<Vector2[]>();
        int left = 0;
        foreach (int right in cuts)
        {
            foreach (var polygon in mask.OpaqueToPolygons(new Rect2I(left, 0, right - left, size.Y), 3f))
                polygons.Add(polygon.Select(p => p * original / (Vector2)size / longest).ToArray());
            left = right;
        }
        return Outlines[texture] = polygons.ToArray();
    }

    // Open transparent holes before contour extraction so convex decomposition cannot fill them.
    private static SortedSet<int> HoleCuts(Bitmap mask)
    {
        var size = mask.GetSize();
        var cuts = new SortedSet<int> { size.X };
        var visited = new bool[size.X * size.Y];
        var queue = new Queue<Vector2I>();
        var directions = new[] { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down };
        for (int y = 0; y < size.Y; y++)
        for (int x = 0; x < size.X; x++)
        {
            if (visited[y * size.X + x] || mask.GetBit(x, y)) continue;
            int minX = x, maxX = x;
            bool border = false;
            queue.Enqueue(new Vector2I(x, y));
            visited[y * size.X + x] = true;
            while (queue.TryDequeue(out var p))
            {
                minX = Mathf.Min(minX, p.X);
                maxX = Mathf.Max(maxX, p.X);
                border |= p.X == 0 || p.Y == 0 || p.X == size.X - 1 || p.Y == size.Y - 1;
                foreach (var direction in directions)
                {
                    var q = p + direction;
                    if (q.X < 0 || q.Y < 0 || q.X >= size.X || q.Y >= size.Y) continue;
                    int index = q.Y * size.X + q.X;
                    if (visited[index] || mask.GetBitv(q)) continue;
                    visited[index] = true;
                    queue.Enqueue(q);
                }
            }
            if (!border) cuts.Add((minX + maxX + 1) / 2);
        }
        return cuts;
    }

    private static BulletFootprint Polygons(IEnumerable<Vector2[]> polygons)
    {
        var contours = polygons.ToArray();
        var shapes = new List<Shape2D>();
        float radius = 0;
        foreach (var polygon in contours)
        {
            foreach (var point in polygon) radius = Mathf.Max(radius, point.Length());
            foreach (var convex in Geometry2D.DecomposePolygonInConvex(polygon))
                shapes.Add(new ConvexPolygonShape2D { Points = convex });
        }
        return new BulletFootprint(shapes.ToArray(), radius, contours);
    }

    public static BulletFootprint Primitive(BulletShape kind, float radius)
    {
        var key = (kind, radius);
        if (Primitives.TryGetValue(key, out var cached)) return cached;
        var polygons = new List<Vector2[]>();
        switch (kind)
        {
            case BulletShape.Diamond:
                polygons.Add(new[] { Vector2.Up, Vector2.Right, Vector2.Down, Vector2.Left }.Select(p => p * radius * 1.15f).ToArray());
                break;
            case BulletShape.Star:
                polygons.Add(Enumerable.Range(0, 10).Select(i => Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Tau / 10)
                    * radius * (i % 2 == 0 ? 1.15f : 0.48f)).ToArray());
                break;
            case BulletShape.Ring:
                float halfWidth = Mathf.Max(1.4f, radius * 0.42f) / 2;
                for (int i = 0; i < 28; i++)
                {
                    var a = Vector2.FromAngle(i * Mathf.Tau / 28);
                    var b = Vector2.FromAngle((i + 1) * Mathf.Tau / 28);
                    polygons.Add(new[] { a * (radius * 0.9f - halfWidth), a * (radius * 0.9f + halfWidth),
                        b * (radius * 0.9f + halfWidth), b * (radius * 0.9f - halfWidth) });
                }
                break;
            case BulletShape.Needle:
                var capsule = new Vector2[16];
                for (int i = 0; i <= 7; i++)
                {
                    var tip = Vector2.Right * radius * 1.4f + Vector2.FromAngle(-Mathf.Pi / 2 + Mathf.Pi * i / 7) * radius * 0.39f;
                    capsule[i] = tip;
                    capsule[8 + i] = -tip;
                }
                polygons.Add(capsule);
                break;
            case BulletShape.Rice:
                polygons.Add(Enumerable.Range(0, 24).Select(i => Vector2.FromAngle(i * Mathf.Tau / 24) * new Vector2(1.15f, 0.5f) * radius).ToArray());
                break;
            default:
                return Primitives[key] = new BulletFootprint(new Shape2D[] { new CircleShape2D { Radius = radius } }, radius);
        }
        return Primitives[key] = Polygons(polygons);
    }
}
