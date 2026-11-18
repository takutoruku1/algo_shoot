using Godot;

public partial class VoiceMedallions : Node
{
    private const int TileSize = 256;
    private const float CameraSpan = 2.8f;
    private SubViewport _view = null!;
    private Medal[] _medals = null!;

    private sealed class Medal
    {
        public Node3D Model = null!;
        public StandardMaterial3D Body = null!, Bezel = null!, Inlay = null!, BackFace = null!;
        public ShaderMaterial Portrait = null!;
        public Node3D Seal = null!, SealCheck = null!;
        public Label3D Number = null!;
        public Texture2D? Face;
        public bool Saved, Current, Initialized;
        public float Lift, Spin, SpinSpeed;
    }

    public override void _Ready()
    {
        int count = GameManager.Stages.Length + 1;
        _view = new SubViewport
        {
            Name = "VoiceMedallionView", Size = new Vector2I(TileSize * count, TileSize),
            TransparentBg = true, OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Transparent,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("c2d9e4"), AmbientLightEnergy = 0.55f,
                AmbientLightSkyContribution = 0,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                Sky = new Sky
                {
                    SkyMaterial = new ProceduralSkyMaterial
                    {
                        SkyTopColor = new Color("334d63"), SkyHorizonColor = new Color("f5edda"),
                        GroundBottomColor = new Color("101e2b"), GroundHorizonColor = new Color("bacbd0"),
                        SkyEnergyMultiplier = 0.65f, GroundEnergyMultiplier = 0.4f,
                    },
                },
                TonemapMode = Godot.Environment.ToneMapper.Linear,
            },
        });
        _view.AddChild(new Camera3D
        {
            Position = new Vector3(0, 0, 7), Projection = Camera3D.ProjectionType.Orthogonal,
            Size = CameraSpan, KeepAspect = Camera3D.KeepAspectEnum.Height, Current = true,
        });
        AddLight(new Vector3(-35, -32, 0), new Color("fff0d5"), 1.35f);
        AddLight(new Vector3(22, 48, 0), new Color("9ac9ef"), 0.7f);

        var model = GD.Load<PackedScene>("res://assets/ui/voice_medallion.glb");
        var portraitShader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded;
                uniform sampler2D portrait : source_color, filter_linear_mipmap;
                uniform bool known = false;
                void fragment() {
                    vec3 color = vec3(0.045, 0.075, 0.095);
                    if (known) {
                        color = texture(portrait, UV).rgb;
                        float sheen = pow(max(dot(normalize(NORMAL), normalize(vec3(-0.4, 0.6, 1.0))), 0.0), 24.0);
                        color = color * (1.0 - 0.09 * UV.y) + vec3(sheen * 0.035);
                    } else {
                        float band = smoothstep(0.42, 0.48, fract((UV.x + UV.y) * 10.0));
                        color += vec3(band * 0.012);
                    }
                    ALBEDO = color;
                }
                """,
        };
        _medals = new Medal[count];
        for (int i = 0; i < count; i++)
        {
            var medal = new Medal
            {
                Model = model.Instantiate<Node3D>(),
                Body = Metal(new Color("344b5a"), 0.72f, 0.3f),
                Bezel = Metal(new Color("e9dbb7"), 0.78f, 0.24f),
                Inlay = Metal(new Color("247d67"), 0.35f, 0.23f),
                BackFace = Metal(new Color("123d37"), 0.32f, 0.3f),
                Portrait = new ShaderMaterial { Shader = portraitShader },
            };
            medal.Model.Position = new Vector3((i - (count - 1) * 0.5f) * CameraSpan, 0, 0);
            _view.AddChild(medal.Model);
            medal.Number = new Label3D
            {
                Text = (i + 1).ToString(), Font = UiKit.Mono, FontSize = 88, PixelSize = 0.013f,
                OutlineSize = 0, Modulate = new Color("d5e2df"), Position = new Vector3(0, 0, 0.10f),
                DoubleSided = false,
            };
            medal.Model.AddChild(medal.Number);
            SetMaterial(medal.Model, "Body", medal.Body);
            SetMaterial(medal.Model, "Bezel", medal.Bezel);
            SetMaterial(medal.Model, "Inlay", medal.Inlay);
            SetMaterial(medal.Model, "InnerTrim", medal.Bezel);
            SetMaterial(medal.Model, "BackFace", medal.BackFace);
            SetMaterial(medal.Model, "BackRim", medal.Bezel);
            SetMaterial(medal.Model, "BackEmblem", medal.Bezel);
            SetMaterial(medal.Model, "BackMarks", medal.Bezel);
            SetMaterial(medal.Model, "Portrait", medal.Portrait);
            medal.Seal = medal.Model.GetNode<Node3D>("Seal");
            medal.SealCheck = medal.Model.GetNode<Node3D>("SealCheck");
            SetMaterial(medal.Model, "Seal", Metal(new Color("1c6d57"), 0.45f, 0.28f));
            SetMaterial(medal.Model, "SealCheck", Metal(new Color("e0f9e9"), 0.4f, 0.3f));
            _medals[i] = medal;
        }
    }

    private static StandardMaterial3D Metal(Color color, float metallic, float roughness) => new()
    {
        AlbedoColor = color, Metallic = metallic, Roughness = roughness,
    };

    private void AddLight(Vector3 rotation, Color color, float energy) => _view.AddChild(new DirectionalLight3D
    {
        RotationDegrees = rotation, LightColor = color, LightEnergy = energy,
    });

    private static void SetMaterial(Node3D model, string name, Material material)
    {
        var mesh = model.GetNode<MeshInstance3D>(name);
        mesh.MaterialOverride = material;
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    public void SetPresented(bool presented)
    {
        _view.RenderTargetUpdateMode = presented
            ? SubViewport.UpdateMode.WhenVisible : SubViewport.UpdateMode.Disabled;
        if (!presented)
            foreach (var medal in _medals) medal.Spin = medal.SpinSpeed = 0;
    }

    public void UpdateMedal(int index, Texture2D? face, bool saved, bool current, bool hovered, double time, double delta)
    {
        var medal = _medals[index];
        bool known = face != null;
        if (!medal.Initialized || medal.Face != face || medal.Saved != saved || medal.Current != current)
        {
            medal.Initialized = true;
            medal.Face = face;
            medal.Saved = saved;
            medal.Current = current;
            medal.Portrait.SetShaderParameter("known", known);
            if (face != null) medal.Portrait.SetShaderParameter("portrait", face);
            medal.Body.AlbedoColor = new Color(known ? "344b5a" : "26333d");
            medal.BackFace.AlbedoColor = new Color(saved ? "123d37" : known ? "243443" : "1d2833");
            medal.Bezel.AlbedoColor = new Color(saved ? "e9dbb7" : known ? "c5d6df" : "526372");
            medal.Inlay.AlbedoColor = new Color(saved ? "34ad88" : current ? "dfb25f" : "344959");
            medal.Inlay.EmissionEnabled = saved || current;
            medal.Inlay.Emission = new Color(saved ? "267f62" : "765221");
            medal.Inlay.EmissionEnergyMultiplier = 0.22f;
            medal.Seal.Visible = medal.SealCheck.Visible = saved;
            medal.Number.Visible = !saved;
        }
        float t = (float)time;
        float dt = (float)delta;
        medal.SpinSpeed = Mathf.MoveToward(medal.SpinSpeed, hovered ? Mathf.Tau / 3 : 0, dt * 6);
        medal.Spin = hovered
            ? Mathf.Wrap(medal.Spin + medal.SpinSpeed * dt, -Mathf.Pi, Mathf.Pi)
            : Mathf.LerpAngle(medal.Spin, 0, 1 - Mathf.Exp(-dt * 8));
        medal.Lift = Mathf.MoveToward(medal.Lift, hovered ? 0.075f : current ? 0.055f : 0, dt * 0.3f);
        var position = medal.Model.Position;
        position.Y = medal.Lift + (known ? Mathf.Sin(t * 0.65f + index * 0.9f) * 0.024f : 0);
        medal.Model.Position = position;
        medal.Model.RotationDegrees = new Vector3(-10 + Mathf.Sin(t * 0.43f + index) * 3,
            -30 + index * 4 + Mathf.Sin(t * 0.5f + index * 0.7f) * (known ? 6 : 2) + (current ? 8 : 0)
                + Mathf.RadToDeg(medal.Spin), -3 + index * 2);
    }

    public void Draw(CanvasItem canvas, Vector2 center, int index, float alpha)
    {
        // Composite inside Hub's draw order so dialogue dimming also covers the 3D medals.
        canvas.DrawTextureRectRegion(_view.GetTexture(), new Rect2(center - Vector2.One * 55, Vector2.One * 110),
            new Rect2(index * TileSize, 0, TileSize, TileSize), new Color(1, 1, 1, alpha));
    }
}
