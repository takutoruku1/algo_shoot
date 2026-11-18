using Godot;
using System.Collections.Generic;

public partial class AttackLineLayer : Node2D
{
    private static Shader? _shader;
    private readonly List<Line2D> _lines = new();
    private readonly List<Sprite2D> _surfaces = new();
    private readonly List<FanSurface> _fans = new();
    private static readonly Texture2D Rail = GD.Load<Texture2D>("res://assets/fx/attack_surfaces/rail.png");
    private static readonly Texture2D Panel = GD.Load<Texture2D>("res://assets/fx/attack_surfaces/panel.png");
    private static readonly Texture2D Lens = GD.Load<Texture2D>("res://assets/fx/attack_surfaces/lens.png");
    private static readonly Shader SurfaceShader = GD.Load<Shader>("res://shaders/attack_surface.gdshader");
    private int _used;
    private int _usedSurfaces;
    private int _usedFans;
    internal int RenderedLines => _used;
    internal int RenderedSurfaces => _usedSurfaces + _usedFans;
    private float _time;

    public void Begin(double time = 0)
    {
        _used = 0;
        _usedSurfaces = 0;
        _usedFans = 0;
        _time = (float)time;
    }

    public void Add(Vector2[] points, float radius, Color ink, Texture2D? motif = null,
        bool roundCaps = true, bool preserveEndDisc = false, bool ribbon = false)
    {
        if (_used == _lines.Count)
        {
            _shader ??= GD.Load<Shader>("res://shaders/attack_line_volume.gdshader");
            var material = new ShaderMaterial { Shader = _shader };
            material.SetShaderParameter("rail_surface", Rail);
            var line = new Line2D
            {
                Material = material,
                JointMode = Line2D.LineJointMode.Round,
                RoundPrecision = 16,
                TextureMode = Line2D.LineTextureMode.Stretch,
                Antialiased = true,
                TextureFilter = TextureFilterEnum.Linear,
            };
            AddChild(line);
            _lines.Add(line);
        }
        var current = _lines[_used++];
        current.Points = points;
        current.Width = radius * 2;
        current.DefaultColor = ink;
        current.BeginCapMode = current.EndCapMode = roundCaps ? Line2D.LineCapMode.Round : Line2D.LineCapMode.None;
        var shader = (ShaderMaterial)current.Material;
        shader.SetShaderParameter("field_bounds", new Vector4(Field.Left, Field.Top, Field.Right, Field.Bottom));
        shader.SetShaderParameter("attack_time", _time);
        shader.SetShaderParameter("end_disc", new Vector3(ToGlobal(points[^1]).X, ToGlobal(points[^1]).Y,
            preserveEndDisc ? radius : 0));
        shader.SetShaderParameter("motif_strength", motif == null ? 0f : .3f);
        shader.SetShaderParameter("ribbon", ribbon);
        if (motif != null) shader.SetShaderParameter("motif", motif);
        current.Show();
    }

    private sealed partial class FanSurface : Node2D
    {
        internal Vector4 Projection;
        internal Color Ink;
        internal readonly List<Rect2> Rows = new();
        public override void _Draw()
        {
            foreach (Rect2 row in Rows) DrawTextureRect(Rail, row, false, Ink);
        }
    }

    public void FanRow(Rect2 bounds, Color ink, Vector4 projection)
    {
        if (_usedFans == 0 || _fans[_usedFans - 1].Projection != projection)
        {
            if (_fans.Count == _usedFans)
            {
                var fan = new FanSurface { Material = new ShaderMaterial { Shader = SurfaceShader }, TextureFilter = TextureFilterEnum.Linear };
                AddChild(fan);
                _fans.Add(fan);
            }
            var current = _fans[_usedFans++];
            current.Rows.Clear();
            current.Projection = projection;
            current.Ink = ink;
            var material = (ShaderMaterial)current.Material;
            material.SetShaderParameter("field_bounds", new Vector4(Field.Left, Field.Top, Field.Right, Field.Bottom));
            material.SetShaderParameter("fan", projection);
            current.Show();
            current.QueueRedraw();
        }
        _fans[_usedFans - 1].Rows.Add(bounds);
    }

    public void Surface(Rect2 bounds, Color ink, bool disc = false, Vector2 safeCenter = default, float safeRadius = 0)
    {
        if (!bounds.HasArea()) return;
        if (_usedSurfaces == _surfaces.Count)
        {
            var sprite = new Sprite2D { TextureFilter = TextureFilterEnum.Linear, Material = new ShaderMaterial { Shader = SurfaceShader } };
            AddChild(sprite);
            _surfaces.Add(sprite);
        }
        var body = _surfaces[_usedSurfaces++];
        body.Texture = disc ? Lens : Panel;
        body.Position = bounds.GetCenter();
        body.Scale = bounds.Size / body.Texture.GetSize();
        body.SelfModulate = ink;
        var shader = (ShaderMaterial)body.Material;
        shader.SetShaderParameter("field_bounds", new Vector4(Field.Left, Field.Top, Field.Right, Field.Bottom));
        Vector2 center = ToGlobal(safeCenter);
        shader.SetShaderParameter("safe_disc", new Vector3(center.X, center.Y, safeRadius));
        body.Show();
    }

    public void End()
    {
        for (int i = _used; i < _lines.Count; i++) _lines[i].Hide();
        for (int i = _usedSurfaces; i < _surfaces.Count; i++) _surfaces[i].Hide();
        for (int i = _usedFans; i < _fans.Count; i++) _fans[i].Hide();
    }
}
