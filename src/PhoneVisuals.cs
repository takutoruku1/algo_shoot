using Godot;
using System;
using System.Linq;

public partial class PhoneVisuals : Node
{
    // Keep the imported scene's .NET wrapper alive across hub transitions.
    private static readonly PackedScene PhoneScene = GD.Load<PackedScene>("res://assets/ui/phone_ui.glb");
    private static readonly PackedScene MinaButtonScene = GD.Load<PackedScene>("res://assets/ui/mina_call_button.glb");
    private SubViewport _hardware = null!, _atlas = null!;
    private Node3D _phone = null!;
    private DirectionalLight3D _key = null!;
    private Icon[] _icons = null!;
    private int _pressed = -1;
    private float _pressTime, _lightAngle = -32;
    internal float SnsOpening { get; set; } = -1;
    private const float IconSpan = 1.28f;

    private sealed class Icon
    {
        public Node3D Root = null!;
        public StandardMaterial3D[] Materials = null!;
        public Color[] Colors = null!;
        public float Focus;
        public float Activation = -1;
    }

    public override void _Ready()
    {
        ProcessPriority = 100;
        _hardware = View("PhoneHardwareView", new Vector2I(1280, 720), 720, new Vector3(640, -360, 1200));
        _atlas = View("PhoneIconsView", new Vector2I(1024, 768), IconSpan * 3, new Vector3(0, 0, 8));
        _key = _hardware.GetNode<DirectionalLight3D>("Key");
        var source = PhoneScene.Instantiate<Node3D>();
        _phone = (Node3D)source.GetNode<Node3D>("Phone").Duplicate();
        _hardware.AddChild(_phone);
        _icons = Enumerable.Range(0, Hub.HomeAppCount).Select(index =>
        {
            var root = (Node3D)source.GetNode<Node3D>($"App{index}").Duplicate();
            _atlas.AddChild(root);
            var materials = new[] { "Base", "Rim", "Symbol" }.SelectMany(name =>
            {
                var mesh = root.GetNode<MeshInstance3D>($"App{index}_{name}");
                mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                return Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(surface =>
                {
                    var material = (StandardMaterial3D)mesh.GetActiveMaterial(surface).Duplicate();
                    mesh.SetSurfaceOverrideMaterial(surface, material);
                    return material;
                });
            }).ToArray();
            return new Icon { Root = root, Materials = materials, Colors = materials.Select(material => material.AlbedoColor).ToArray() };
        }).ToArray();
        source.Free();
        // The pinned call button occupies the last two cells of the third atlas row.
        var minaButton = MinaButtonScene.Instantiate<Node3D>();
        minaButton.Name = "MinaCallButton";
        minaButton.Position = new Vector3(IconSpan, -IconSpan, 0);
        _atlas.AddChild(minaButton);
    }

    public override void _Process(double delta)
    {
        if (GetParent() is Hub hub && hub.IsProcessing()) hub.UpdatePhoneVisuals(delta);
    }

    private SubViewport View(string name, Vector2I size, float span, Vector3 cameraPosition)
    {
        var view = new SubViewport
        {
            Name = name, Size = size, OwnWorld3D = true, TransparentBg = true,
            Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(view);
        view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Transparent,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("c0d3e0"), AmbientLightEnergy = .6f, AmbientLightSkyContribution = 0,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color("243d57"), SkyHorizonColor = new Color("f2e8d6"),
                    GroundBottomColor = new Color("101925"), GroundHorizonColor = new Color("759bb0"),
                    SkyEnergyMultiplier = .75f, GroundEnergyMultiplier = .45f,
                } },
                TonemapMode = Godot.Environment.ToneMapper.Agx,
            },
        });
        view.AddChild(new Camera3D
        {
            Position = cameraPosition, Far = 2000,
            Projection = Camera3D.ProjectionType.Orthogonal, Size = span,
            KeepAspect = Camera3D.KeepAspectEnum.Height, Current = true,
        });
        view.AddChild(new DirectionalLight3D
        {
            Name = "Key", RotationDegrees = new Vector3(-25, -32, -8),
            LightColor = new Color("fff0d7"), LightEnergy = 1.4f,
        });
        view.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(32, 52, 12), LightColor = new Color("9ec5e7"), LightEnergy = .65f,
        });
        return view;
    }

    internal void Press(int index)
    {
        _pressed = index;
        _pressTime = .18f;
    }

    internal void Update(Transform2D pose, bool home, bool interactive, int focused, bool[] unlocked,
        float[] activation, Vector2 pointer, double delta)
    {
        // Lift the existing affine screen into 3D without changing any text or input coordinates.
        float xx = pose.X.LengthSquared(), yy = pose.Y.LengthSquared(), xy = pose.X.Dot(pose.Y);
        float scaleSquared = (xx + yy + Mathf.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy)) * .5f;
        float zx = Mathf.Sqrt(Math.Max(0, scaleSquared - xx));
        float zy = zx > .0001f ? xy / zx : Mathf.Sqrt(Math.Max(0, scaleSquared - yy));
        Vector3 x = new(pose.X.X, -pose.X.Y, zx), y = new(-pose.Y.X, pose.Y.Y, zy);
        Vector3 z = x.Cross(y).Normalized() * Mathf.Sqrt(scaleSquared);
        Vector2 center = pose * new Vector2(640, 360);
        _phone.Transform = new Transform3D(new Basis(x * 100, y * 100, z * 100), new Vector3(center.X, -center.Y, 0));
        _lightAngle = Mathf.Lerp(_lightAngle, interactive ? -32 + Mathf.Clamp((pointer.X - 640) / 640, -1, 1) * 10 : -32,
            1 - Mathf.Exp(-(float)delta * 6));
        _key.RotationDegrees = new Vector3(-25, _lightAngle, -8);
        _atlas.RenderTargetUpdateMode = home ? SubViewport.UpdateMode.WhenVisible : SubViewport.UpdateMode.Disabled;
        _pressTime = Math.Max(0, _pressTime - (float)delta);
        for (int index = 0; index < _icons.Length; index++)
        {
            var icon = _icons[index];
            float enabled = unlocked[index] ? activation[index] : 0;
            if (!Mathf.IsEqualApprox(icon.Activation, enabled))
            {
                icon.Activation = enabled;
                for (int material = 0; material < icon.Materials.Length; material++)
                    icon.Materials[material].AlbedoColor = new Color(material >= 2 ? "74808c" : "25303d")
                        .Lerp(icon.Colors[material], enabled);
            }
            float target = home && interactive && unlocked[index] && index == focused ? 1 : 0;
            icon.Focus = Mathf.Lerp(icon.Focus, target, 1 - Mathf.Exp(-(float)delta * 12));
            float press = index == _pressed ? Mathf.Sin(_pressTime / .18f * Mathf.Pi) : 0;
            icon.Root.Position = new Vector3((index % 4 - 1.5f) * IconSpan,
                (1f - index / 4) * IconSpan + icon.Focus * .035f - press * .015f, 0);
            icon.Root.RotationDegrees = new Vector3(-7 - icon.Focus * 12 + press * 5, -12 + icon.Focus * 23, -icon.Focus * 2);
            if (index == 0 && SnsOpening >= 0)
                icon.Root.RotationDegrees = icon.Root.RotationDegrees.Lerp(Vector3.Zero, Mathf.SmoothStep(0, 1, SnsOpening));
            icon.Root.Scale = Vector3.One * (1 + icon.Focus * .035f - press * .07f);
        }
    }

    internal void DrawHardware(CanvasItem canvas) => canvas.DrawTextureRect(_hardware.GetTexture(), new Rect2(0, 0, 1280, 720), false);

    internal void DrawIcon(CanvasItem canvas, int index, Rect2 icon, float alpha = 1)
    {
        float side = icon.Size.X / .88f * IconSpan;
        canvas.DrawTextureRectRegion(_atlas.GetTexture(), new Rect2(icon.GetCenter() - Vector2.One * side * .5f, Vector2.One * side),
            new Rect2(index % 4 * 256, index / 4 * 256, 256, 256), new Color(1, 1, 1, alpha));
    }

    internal void DrawMinaButton(CanvasItem canvas, Rect2 rect)
    {
        float cell = IconSpan * rect.Size.X / 2.30f;
        var size = new Vector2(cell * 2, cell);
        canvas.DrawTextureRectRegion(_atlas.GetTexture(), new Rect2(rect.GetCenter() - size / 2, size),
            new Rect2(512, 512, 512, 256));
    }

    internal void DrawSnsLaunch(CanvasItem canvas, Rect2 panel, float progress)
    {
        float alpha = Mathf.Clamp((1 - progress) * 3, 0, 1);
        float size = Mathf.Lerp(88, 132, Mathf.SmoothStep(0, 1, progress));
        DrawIcon(canvas, 0, new Rect2(panel.GetCenter() - Vector2.One * size * .5f, Vector2.One * size), alpha);
    }
}
