using Godot;
using System.Collections.Generic;

public partial class MinaArchiveVisuals : Node
{
    private SubViewport _view = null!;
    private readonly List<(int Floor, bool Right, Node3D Root, MeshInstance3D Mesh)> _blocks = new();
    private static PackedScene? _model;

    internal void Initialize(MinaArchiveTechnique attack)
    {
        _view = new SubViewport
        {
            Size = new Vector2I(1536, 864), TransparentBg = true, OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa2X, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Transparent,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("a0c6dd"), AmbientLightEnergy = .3f,
            AmbientLightSkyContribution = 0, TonemapMode = Godot.Environment.ToneMapper.Filmic,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color("163343"), SkyHorizonColor = new Color("88bad0"),
                GroundBottomColor = new Color("08101e"), GroundHorizonColor = new Color("e0bc81"),
                SkyEnergyMultiplier = .7f, GroundEnergyMultiplier = .4f,
            } },
        } });
        _view.AddChild(new Camera3D
        {
            Position = new Vector3(192, -108, 500), Far = 1000,
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 216,
            KeepAspect = Camera3D.KeepAspectEnum.Height, Current = true,
        });
        _view.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28, -35, 0), LightColor = new Color("e7f7ff"), LightEnergy = 1.7f,
            ShadowEnabled = true, DirectionalShadowMaxDistance = 600,
        });
        _view.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(20, 60, 0), LightColor = new Color("d8ac73"), LightEnergy = .25f,
        });
        _model ??= GD.Load<PackedScene>("res://assets/fx/mina_archive.glb");
        var source = _model.Instantiate<Node3D>();
        var mesh = source.FindChild("ArchiveFloor", true, false) as MeshInstance3D
            ?? (MeshInstance3D)source;
        for (int floor = 0; floor < MinaArchiveTechnique.FloorCount; floor++)
        foreach (bool right in new[] { false, true })
        {
            var root = new Node3D();
            _view.AddChild(root);
            var copy = new MeshInstance3D { Mesh = mesh.Mesh };
            root.AddChild(copy);
            _blocks.Add((floor, right, root, copy));
        }
        source.Free();
    }

    internal void Sync(MinaArchiveTechnique attack)
    {
        foreach (var block in _blocks)
        {
            Rect2 bounds = attack.FloorBounds(block.Floor, block.Right, attack.Lift);
            float assemble = Mathf.Clamp((float)((attack.Elapsed - attack.LockAt(block.Floor) + .65) / .65), 0, 1);
            block.Root.Visible = assemble > 0 && bounds.End.Y > Field.Top && bounds.Position.Y < Field.Bottom;
            if (!block.Root.Visible) continue;
            float offset = (1 - assemble) * (block.Right ? 16 : -16);
            block.Root.Transform = new Transform3D(
                new Basis(new Vector3(bounds.Size.X - 8, 0, 0), new Vector3(0, MinaArchiveTechnique.FloorHeight - 5, 0),
                    new Vector3(-8, -5, 14)),
                new Vector3(bounds.Position.X + offset, -bounds.End.Y, -30 * (1 - assemble)));
            block.Mesh.Rotation = new Vector3(0, (1 - assemble) * (block.Right ? -.8f : .8f), 0);
            block.Mesh.Transparency = assemble < 1 ? .8f - assemble * .35f : 0;
        }
    }

    internal void Stop() => _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

    internal void Draw(CanvasItem canvas)
        => canvas.DrawTextureRectRegion(_view.GetTexture(), Field.Rect,
            new Rect2(Field.Left * 4, 0, Field.Width * 4, Field.Height * 4));
}
