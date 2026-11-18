using Godot;
using System.Collections.Generic;

internal class ProjectileTurntable
{
    internal const int FrameCount = 48, CellSize = 128, Columns = 8;
    internal static readonly ShaderMaterial Material = new() { Shader = GD.Load<Shader>("res://shaders/projectile_turntable.gdshader") };
    internal ShaderMaterial DrawMaterial { get; set; } = Material;
    internal string Name { get; }
    internal Texture2D Texture { get; }
    internal AtlasTexture[] Frames { get; } = new AtlasTexture[FrameCount];
    internal float MaximumRadius { get; private set; }
    private readonly Dictionary<Color, BulletArt.PlayerVisual> _visuals = new();
    private Image? _image;
    private int _prepared;
    private readonly float _outlineTolerance;

    internal ProjectileTurntable(string name, string path, float outlineTolerance = 1f)
    {
        Name = name;
        _outlineTolerance = outlineTolerance;
        Texture = GD.Load<Texture2D>(path);
        for (int frame = 0; frame < FrameCount; frame++)
            Frames[frame] = new AtlasTexture { Atlas = Texture,
                Region = new Rect2(frame % Columns * CellSize, frame / Columns * CellSize, CellSize, CellSize), FilterClip = true };
    }

    internal IEnumerable<float> Prepare()
    {
        if (_prepared == FrameCount) yield break;
        // Read the atlas once; per-frame GPU readback would stall during the first barrage.
        _image ??= Texture.GetImage();
        if (_image.IsCompressed()) _image.Decompress();
        while (_prepared < FrameCount)
        {
            using var image = _image.GetRegion((Rect2I)Frames[_prepared].Region);
            var contours = BulletFootprint.CacheTextureOutlines(Frames[_prepared], image, _outlineTolerance);
            foreach (var contour in contours)
                foreach (var point in contour) MaximumRadius = Mathf.Max(MaximumRadius, (point - Vector2.One * .5f).Length());
            _prepared++;
            yield return _prepared / (float)FrameCount;
        }
        _image.Dispose();
        _image = null;
    }

    internal BulletArt.PlayerVisual Visual(Color color)
    {
        if (_visuals.TryGetValue(color, out var art)) return art;
        art = new(Texture, Frames[0].Region, Vector2.One * CellSize / 2, color);
        BulletFootprint.ShareTextureOutlines(art.Cropped, Frames[0]);
        return _visuals[color] = art;
    }

    internal BulletFootprint Footprint(int frame, float size)
        => BulletFootprint.Texture(Frames[frame], Vector2.One * CellSize / 2, size);

    internal static int FrameAt(double time) => (int)(Mathf.PosMod(time * .48, 1) * FrameCount);

    internal void Draw(Node2D canvas, Rect2 bounds, double time, Color? color = null)
    {
        float size = Mathf.Min(bounds.Size.X, bounds.Size.Y) / (MaximumRadius * 2);
        canvas.DrawTextureRectRegion(Texture, new Rect2(bounds.GetCenter() - Vector2.One * size / 2, Vector2.One * size),
            Frames[FrameAt(time)].Region, color ?? Colors.White);
    }
}
