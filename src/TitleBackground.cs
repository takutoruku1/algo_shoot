using Godot;
using System.Collections.Generic;

public partial class TitleBackground : Node
{
    private SubViewport _view = null!;
    private Camera3D _camera = null!;
    private readonly List<(Node3D node, Vector3 position, Vector3 rotation)> _posts = new();

    public override void _Ready()
    {
        _view = new SubViewport
        {
            Name = "TimelineDepthView", Size = new Vector2I(1280, 720), OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/title_depth_sky.gdshader") } },
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("8fbed3"), AmbientLightEnergy = .65f,
                AmbientLightSkyContribution = 0, TonemapMode = Godot.Environment.ToneMapper.Filmic,
                FogEnabled = true, FogLightColor = new Color("193748"), FogLightEnergy = .45f,
                FogDensity = .018f, FogSunScatter = 0,
                GlowEnabled = true, GlowIntensity = .3f, GlowBloom = .05f,
            },
        });
        _camera = new Camera3D
        {
            Position = new Vector3(0, 0, 12), Fov = 40, Near = .1f, Far = 100, Current = true,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Attributes = new CameraAttributesPractical
            {
                DofBlurNearEnabled = true, DofBlurNearDistance = 8, DofBlurNearTransition = 4,
                DofBlurFarEnabled = true, DofBlurFarDistance = 22, DofBlurFarTransition = 24,
                DofBlurAmount = .12f,
            },
        };
        _view.AddChild(_camera);
        _view.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28, -32, -12), LightColor = new Color("c0e8ff"), LightEnergy = 1.1f,
        });
        _view.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(18, 44, 15), LightColor = new Color("c4b8f0"), LightEnergy = .45f,
        });
        var source = GD.Load<PackedScene>("res://assets/ui/title_background.glb").Instantiate<Node3D>();
        _view.AddChild(source);
        foreach (Node3D post in source.GetChildren())
        {
            _posts.Add((post, post.Position, post.Rotation));
            var mesh = post.GetChild<MeshInstance3D>(0);
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var material = (StandardMaterial3D)mesh.GetActiveMaterial(surface);
                if (material.ResourceName != "Existing story photographs") continue;
                material.AlbedoColor = new Color(.52f, .61f, .72f);
            }
        }
    }

    internal void Update(float time, Vector2 parallax)
    {
        _camera.Position = new Vector3(Mathf.Sin(time * .18f) * .075f - parallax.X * .024f,
            Mathf.Sin(time * .23f) * .025f + parallax.Y * .018f, 12 + Mathf.Sin(time * .13f) * .08f);
        for (int i = 0; i < _posts.Count; i++)
        {
            var post = _posts[i];
            float phase = i * 1.73f;
            post.node.Position = post.position + new Vector3(Mathf.Sin(time * .19f + phase) * .055f,
                Mathf.Sin(time * .26f + phase) * .08f, Mathf.Sin(time * .17f + phase) * .025f);
            post.node.Rotation = post.rotation + new Vector3(Mathf.Sin(time * .17f + phase) * .012f,
                Mathf.Sin(time * .21f + phase) * .025f, 0);
        }
    }

    internal void Draw(CanvasItem canvas) => canvas.DrawTextureRect(_view.GetTexture(), new Rect2(0, 0, 1280, 720), false);
}
