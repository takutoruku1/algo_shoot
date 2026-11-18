using Godot;

public partial class TitleVisuals : Node
{
    private SubViewport _view = null!;
    private TitleBackground _background = null!;
    private SubViewport _logoView = null!;
    private DirectionalLight3D _logoKey = null!;
    internal float LogoWidth { get; init; }
    internal Vector2 ArtSize { get; private set; }
    private Node3D _photo = null!, _draft = null!;
    private Node3D _mina = null!;
    private MeshInstance3D[] _minaParts = System.Array.Empty<MeshInstance3D>();
    private ShaderMaterial _minaMaterial = null!;
    private DirectionalLight3D _key = null!;
    internal static readonly string[] MinaShapes = { "Breath", "HeadSway", "HairUpperSin", "HairUpperCos",
        "HairLowerSin", "HairLowerCos", "HairFrontSin", "HairFrontCos", "Ribbon" };

    public override void _Ready()
    {
        _background = new TitleBackground { Name = "TitleBackground" };
        AddChild(_background);
        _view = new SubViewport
        {
            Name = "TitleArtView", Size = new Vector2I(1280, 720), OwnWorld3D = true,
            TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0, 0, 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b8ccd9"), AmbientLightEnergy = .65f,
                AmbientLightSkyContribution = 0, TonemapMode = Godot.Environment.ToneMapper.Linear,
            },
        });
        _view.AddChild(new Camera3D
        {
            Position = new Vector3(640, -360, 1200), Far = 2000, Current = true,
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 720,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        });
        _key = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28, -32, -8), LightColor = new Color("dbeaf2"), LightEnergy = .75f,
        };
        _view.AddChild(_key);
        var source = GD.Load<PackedScene>("res://assets/ui/title_ui.glb").Instantiate<Node3D>();
        _view.AddChild(source);
        _photo = source.GetNode<Node3D>("PhotoPost");
        _draft = source.GetNode<Node3D>("DraftPost");
        foreach (var card in new[] { _photo, _draft })
        {
            var face = card.GetNode<MeshInstance3D>($"{card.Name}Artwork");
            var material = (StandardMaterial3D)face.GetActiveMaterial(0).Duplicate();
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            material.AlbedoColor = card == _photo ? new Color(.88f, .88f, .88f) : Colors.White;
            face.SetSurfaceOverrideMaterial(0, material);
            card.AddChild(new MeshInstance3D
            {
                Name = "Reflection", Mesh = face.Mesh, Position = new Vector3(0, 0, .15f),
                MaterialOverride = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoTexture = material.AlbedoTexture, AlbedoColor = new Color(.7f, .85f, 1, .025f),
                    Metallic = .2f, MetallicSpecular = .1f, Roughness = .4f,
                    DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                },
            });
        }
        _mina = GD.Load<PackedScene>("res://assets/ui/title_mina_25d.glb").Instantiate<Node3D>();
        _view.AddChild(_mina);
        _minaMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/title_mina.gdshader") };
        var original = GD.Load<Texture2D>("res://char/bg2/title/title_clean_v3.png");
        ArtSize = original.GetSize();
        _minaMaterial.SetShaderParameter("original_art", original);
        _minaMaterial.SetShaderParameter("cutout", GD.Load<Texture2D>("res://char/bg2/title/title_mina_cutout_v1.png"));
        _minaMaterial.SetShaderParameter("expression_art", GD.Load<Texture2D>("res://char/bg2/title/title_mina_expression_v1.png"));
        _minaMaterial.SetShaderParameter("half_blink_art", GD.Load<Texture2D>("res://char/bg2/title/title_mina_half_blink_v1.png"));
        var parts = new System.Collections.Generic.List<MeshInstance3D>();
        foreach (Node node in _mina.FindChildren("*", "MeshInstance3D"))
        {
            var part = (MeshInstance3D)node;
            part.MaterialOverride = _minaMaterial;
            parts.Add(part);
        }
        _minaParts = parts.ToArray();
        _logoView = new SubViewport
        {
            Name = "TitleLogoView", Size = new Vector2I(1600, 900), OwnWorld3D = true,
            TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(_logoView);
        _logoView.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0, 0, 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
                AmbientLightEnergy = .4f, AmbientLightSkyContribution = 0,
                TonemapMode = Godot.Environment.ToneMapper.Linear,
            },
        });
        _logoView.AddChild(new Camera3D
        {
            Position = new Vector3(640, -360, 1200), Far = 2000, Current = true,
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 720,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        });
        _logoKey = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-18, -18, -8), LightColor = new Color("e8f5ff"), LightEnergy = .4f,
        };
        _logoView.AddChild(_logoKey);
        _logoView.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(22, 34, 0), LightColor = new Color("f4d8ed"), LightEnergy = .18f,
        });
        var logo = GD.Load<PackedScene>("res://assets/ui/title_logo.glb").Instantiate<Node3D>();
        _logoView.AddChild(logo);
        logo.Position = new Vector3(88 + LogoWidth / 2, -225, 0);
        logo.Scale = Vector3.One * LogoWidth / 10;
        logo.RotationDegrees = new Vector3(-2, -5, 0);
    }

    internal void Update(Transform2D design, float time, Vector2 parallax)
    {
        _background.Update(time, parallax);
        Vector2 center = ArtSize / 2;
        Vector2 photo = design * (new Vector2(617, 228) - center
            + new Vector2(Mathf.Sin(time * .43f) * 10, Mathf.Sin(time * .51f) * 16) + parallax * 1.2f);
        Vector2 draft = design * (new Vector2(610, 491) - center
            + new Vector2(-Mathf.Sin(time * .39f) * 8, -Mathf.Sin(time * .64f) * 11) + parallax * 1.8f);
        _photo.Position = new Vector3(photo.X, -photo.Y, 0);
        _photo.Scale = Vector3.One * design.X.Length() * .885f;
        _photo.Rotation = new Vector3(Mathf.Sin(time * .21f) * .018f,
            Mathf.Sin(time * .26f) * .035f + parallax.X * .003f, -Mathf.Sin(time * .29f) * .009f);
        _draft.Position = new Vector3(draft.X, -draft.Y, 0);
        _draft.Scale = Vector3.One * design.X.Length() * .755f;
        _draft.Rotation = new Vector3(Mathf.Sin(time * .24f) * .025f,
            -Mathf.Sin(time * .19f) * .045f + parallax.X * .004f, Mathf.Sin(time * .37f) * .007f);
        _key.RotationDegrees = new Vector3(-28, -32 + Mathf.Sin(time * .24f) * 8, -8);
        _mina.Position = new Vector3(design.Origin.X, -design.Origin.Y, 40);
        _mina.Scale = Vector3.One * design.X.Length();
        _logoKey.RotationDegrees = new Vector3(-18, -18 + Mathf.Sin(time * .25f) * 18, -8);
    }

    internal void AnimateMina(float time)
    {
        float breath = .5f - .5f * Mathf.Cos(time * Mathf.Tau / 4.8f);
        SetMinaShape("Breath", breath);
        SetMinaShape("HeadSway", Mathf.Sin(time * .34f) * .65f);
        SetMinaShape("HairUpperSin", Mathf.Cos(time * .92f));
        SetMinaShape("HairUpperCos", Mathf.Sin(time * .92f));
        SetMinaShape("HairLowerSin", Mathf.Cos(time * .83f - .8f));
        SetMinaShape("HairLowerCos", Mathf.Sin(time * .83f - .8f));
        SetMinaShape("HairFrontSin", Mathf.Cos(time * .92f - .5f));
        SetMinaShape("HairFrontCos", Mathf.Sin(time * .92f - .5f));
        SetMinaShape("Ribbon", Mathf.Sin(time * .78f - .7f));
        float cycle = Mathf.PosMod(time, 22.4f);
        float blink = 0;
        foreach (float at in new[] { 3.2f, 10.6f, 18.2f })
        {
            float local = cycle - at;
            blink = Mathf.Max(blink, Ease(local / .24f) * (1 - Ease((local - .36f) / .4f)));
        }
        float smileCycle = Mathf.PosMod(time, 19f);
        float smile = Ease((smileCycle - 1.4f) / 2.4f) * (1 - Ease((smileCycle - 7.5f) / 2.5f));
        SetMinaExpression(blink, smile);
    }

    internal void SetMinaShape(string name, float value)
    {
        foreach (var part in _minaParts) part.SetBlendShapeValue(part.FindBlendShapeByName(name), value);
    }

    internal void SetMinaExpression(float blink, float smile)
    {
        _minaMaterial.SetShaderParameter("blink", blink);
        _minaMaterial.SetShaderParameter("smile", smile);
    }

    private static float Ease(float value)
    {
        float p = Mathf.Clamp(value, 0, 1);
        return p * p * (3 - 2 * p);
    }

    internal Vector2 ThreadStart
    {
        get
        {
            Vector3 point = _draft.ToGlobal(new Vector3(31, -40, .1f));
            return new Vector2(point.X, -point.Y);
        }
    }

    internal void Draw(CanvasItem canvas)
    {
        _background.Draw(canvas);
        canvas.DrawTextureRect(_view.GetTexture(), new Rect2(0, 0, 1280, 720), false);
    }

    internal void DrawLogo(CanvasItem canvas, float alpha) => canvas.DrawTextureRect(_logoView.GetTexture(),
        new Rect2(0, 0, 1280, 720), false, new Color(1, 1, 1, alpha));
}
