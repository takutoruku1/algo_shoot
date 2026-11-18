using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class PlayerSpecial
{
    private static readonly Color MinaLight = new("91eaff"), AkariLight = new("ffcb71");
    private static readonly Color KoharuLight = new("a7ffce"), ReiLight = new("dfa9ff");
    private static readonly Color SpecialInk = new("152238");
    private static PackedScene? _specialArt;
    private SubViewport? _specialView;
    private readonly Dictionary<string, SpecialModel> _models = new();
    private readonly List<SpecialModel> _chainStars = new(), _chainThreads = new();
    private readonly List<SpecialModel> _beamHalos = new(), _crossCuts = new(), _chainNovas = new();

    private sealed class SpecialModel
    {
        internal Node3D Root = null!;
        internal MeshInstance3D[] Meshes = null!;

        internal void Pose(Vector2 at, Vector3 scale, Vector3 rotation, float opacity = 1)
        {
            Root.Visible = opacity > .005f;
            if (!Root.Visible) return;
            Root.Position = new Vector3(at.X, -at.Y, 0);
            Root.Scale = scale;
            Root.Rotation = rotation;
            foreach (var mesh in Meshes) mesh.Transparency = 1 - Mathf.Clamp(opacity, 0, 1);
        }
    }

    internal static void PrepareVisuals()
    {
        if (DisplayServer.GetName() != "headless")
        {
            _specialArt ??= GD.Load<PackedScene>("res://assets/fx/player_specials.glb");
            PrepareCutinPortrait(GameManager.Instance!.SelectedJob);
        }
    }

    private void InitializeVisuals()
    {
        if (DisplayServer.GetName() == "headless") return;
        PrepareVisuals();
        TextureFilter = TextureFilterEnum.Linear;
        _specialView = new SubViewport
        {
            Name = "SpecialArt", Size = new Vector2I(1536, 864), TransparentBg = true,
            OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(_specialView);
        _specialView.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Transparent,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("cedbed"), AmbientLightEnergy = .75f,
                AmbientLightSkyContribution = 0, TonemapMode = Godot.Environment.ToneMapper.Filmic,
                GlowEnabled = true, GlowIntensity = .6f, GlowBloom = .08f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color("46547a"), SkyHorizonColor = new Color("f7ebd1"),
                    GroundBottomColor = new Color("202c46"), GroundHorizonColor = new Color("8ab0c2"),
                } },
            },
        });
        _specialView.AddChild(new Camera3D
        {
            Position = new Vector3(192, -108, 500), Far = 1000,
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 216,
            KeepAspect = Camera3D.KeepAspectEnum.Height, Current = true,
        });
        _specialView.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28, -32, 0), LightColor = new Color("fff1d8"), LightEnergy = 1.7f,
        });
        _specialView.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(24, 55, 0), LightColor = new Color("a4c7ff"), LightEnergy = .95f,
        });
        var source = _specialArt!.Instantiate<Node3D>();
        SpecialModel Copy(string name)
        {
            var root = (Node3D)source.GetNode<Node3D>(name).Duplicate();
            _specialView.AddChild(root);
            return new SpecialModel
            {
                Root = root,
                Meshes = root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToArray(),
            };
        }
        string[] names = Character switch
        {
            Job.Melee => new[] { "AkariBlade", "AkariWake", "AkariFinale" },
            Job.Heal => new[] { "KoharuRing", "KoharuHeart", "KoharuBloom" },
            Job.Magic => new[] { "ReiStar", "ReiLink", "ReiBook" },
            _ => new[] { "MinaShield", "MinaSeal", "MinaBeam" },
        };
        foreach (string name in names) _models[name] = Copy(name);
        if (Character == Job.Tank)
            for (int i = 0; i < 3; i++) _beamHalos.Add(Copy("MinaHalo"));
        if (Character == Job.Melee)
            for (int i = 0; i < 2; i++) _crossCuts.Add(Copy("AkariCut"));
        if (Character == Job.Magic)
            for (int i = 0; i < ReiMaxTargets; i++)
            {
                _chainStars.Add(Copy("ReiStar"));
                _chainThreads.Add(Copy("ReiLink"));
                _chainNovas.Add(Copy("ReiNova"));
            }
        source.Free();
        SyncVisuals();
    }

    private void SyncVisuals()
    {
        if (_specialView == null) return;
        float angle = -Direction.Angle();
        switch (Character)
        {
            case Job.Tank:
            {
                Vector2 center = _player.GlobalPosition + Direction * MinaShieldOffset;
                float charge = Mathf.Min(_guardAbsorbed, 20) / 20f;
                _models["MinaShield"].Pose(center, Vector3.One * MinaShieldHalfLength,
                    new Vector3(0, .10f * Mathf.Sin(Elapsed * 2.2f), angle), _beamReleased ? 0 : 1);
                float phase = Mathf.Clamp((Elapsed - MinaGuardDuration) / MinaBeamDuration, 0, 1);
                float fade = _beamReleased ? 1 - phase * phase * phase : 1;
                float pulse = _beamReleased ? Mathf.Clamp((Elapsed - BeamVolleyAt) / .22f, 0, 1) : 0;
                for (int i = 0; i < _beamHalos.Count; i++)
                {
                    float unfold = Mathf.SmoothStep(0, 1, Mathf.Clamp(Elapsed / .3f, 0, 1));
                    Vector2 at = _beamReleased ? _beamStart + Direction * (14 + i * 44)
                        : center - Direction * (5 + i * 8);
                    _beamHalos[i].Pose(at, Vector3.One * ((21 + i * 10 + charge * 12) * unfold),
                        new Vector3(0, 1.08f, angle + Elapsed * (i % 2 == 0 ? 1 : -1)), fade * (.75f + charge * .25f));
                }
                _models["MinaSeal"].Pose(_beamReleased ? _beamStart : center - Direction * 3,
                    Vector3.One * (_beamReleased ? BeamRadius + 5 : 7 + charge * 5),
                    new Vector3(.22f * Mathf.Sin(Elapsed * 3), .20f, -Elapsed * (1.5f + charge)), fade);
                _models["MinaBeam"].Pose(_beamStart, new Vector3(Mathf.Max(.001f, _beamStart.DistanceTo(_beamEnd)), BeamRadius, BeamRadius),
                    new Vector3(0, 0, angle), _beamReleased ? fade * (1 - pulse * .25f) : 0);
                break;
            }
            case Job.Melee:
            {
                float fade = Elapsed <= AkariWindup + AkariDashDuration ? 1
                    : 1 - Mathf.Clamp((Elapsed - AkariWindup - AkariDashDuration) / AkariRecovery, 0, 1);
                float sweep = Mathf.Clamp((Elapsed - AkariWindup) / AkariDashDuration, 0, 1);
                Vector2 tip = Elapsed < AkariWindup ? Origin + Direction * 5 : _dashTrail[^1];
                _models["AkariBlade"].Pose(tip, Vector3.One * AkariSlashRadius,
                    new Vector3(.22f * Mathf.Sin(sweep * Mathf.Pi), 0, angle), fade);
                Vector2 wakeTip = _dashTrail[^1], wakeFrom = wakeTip, wakeDirection = Direction;
                for (int i = _dashTrail.Count - 2; i >= 0; i--)
                {
                    Vector2 delta = wakeTip - _dashTrail[i];
                    if (delta.LengthSquared() <= .01f) continue;
                    if (wakeFrom == wakeTip) wakeDirection = delta.Normalized();
                    if (Mathf.Abs(delta.Cross(wakeDirection)) > .25f) break;
                    wakeFrom = _dashTrail[i];
                }
                float distance = wakeFrom.DistanceTo(wakeTip);
                _models["AkariWake"].Pose(wakeFrom, new Vector3(Mathf.Max(.001f, distance), AkariSlashRadius * fade, 7),
                    new Vector3(0, 0, -wakeDirection.Angle()), distance > .1f ? fade * .65f : 0);
                for (int i = 0; i < _crossCuts.Count; i++)
                {
                    float age = Elapsed - AkariWindup - AkariDashDuration - (.10f + i * .20f);
                    var (from, to) = AkariCutEndpoints(i + 1);
                    float light = age < 0 ? 0 : 1 - Mathf.Clamp(age / .52f, 0, 1);
                    _crossCuts[i].Pose(from, new Vector3(from.DistanceTo(to), AkariCutRadius, AkariCutRadius),
                        new Vector3(.12f, 0, -(to - from).Angle()), light);
                }
                float finaleAge = Elapsed - AkariWindup - AkariDashDuration - .58f;
                _models["AkariFinale"].Pose(AkariFinaleCenter, Vector3.One * AkariFinaleReach,
                    new Vector3(0, 0, angle - AkariFinaleTurn), finaleAge < 0 ? 0 : 1 - Mathf.Pow(Mathf.Clamp(finaleAge / .6f, 0, 1), 2));
                break;
            }
            case Job.Heal:
            {
                float finish = Mathf.Clamp((Elapsed - KoharuDuration) / (KoharuBloomDuration + .1f), 0, 1);
                _models["KoharuRing"].Pose(Origin, Vector3.One * KoharuRadius,
                    new Vector3(0, 0, Elapsed * .12f), 1 - finish);
                Vector2 heartAt = Origin + new Vector2(-14, -10);
                if (Healed) heartAt = heartAt.Lerp(_player.GlobalPosition, Mathf.SmoothStep(0, 1, finish));
                _models["KoharuHeart"].Pose(heartAt, Vector3.One * (16f + finish * 7),
                    new Vector3(.1f, .24f * Mathf.Sin(Elapsed * 2.4f), -.12f), 1 - finish);
                _models["KoharuBloom"].Pose(Origin, Vector3.One * Mathf.Max(.01f, BloomRadius),
                    new Vector3(0, 0, Elapsed * -.28f),
                    BloomActive ? 1 - Mathf.Pow(Mathf.Clamp((Elapsed - _bloomAt) / KoharuBloomDuration, 0, 1), 4) : 0);
                break;
            }
            case Job.Magic:
            {
                float finale = _constellationReleased ? Mathf.Clamp((Elapsed - _constellationAt) / .67f, 0, 1) : 0;
                float fade = 1 - finale;
                _models["ReiBook"].Pose(Origin + new Vector2(0, -28), Vector3.One * (20 + finale * 5),
                    new Vector3(.15f, .22f * Mathf.Sin(Elapsed * 2), -.12f), fade);
                for (int i = 0; i < _chainStars.Count; i++)
                {
                    _chainStars[i].Root.Visible = false;
                    _chainThreads[i].Root.Visible = false;
                    _chainNovas[i].Root.Visible = false;
                }
                for (int i = 0; i < _links.Count; i++)
                {
                    var link = _links[i];
                    float age = Mathf.Clamp((Elapsed - link.Born) / .48f, 0, 1);
                    PoseThread(_chainThreads[i], link.From, link.To, (_constellationReleased ? 1 : .42f + (1 - age) * .58f) * fade,
                        _constellationReleased ? ReiThreadRadius : 3.8f);
                    _chainStars[i].Pose(link.To, Vector3.One * (8.5f + Mathf.Sin(age * Mathf.Pi) * 4),
                        new Vector3(.18f, Elapsed * .6f, Elapsed * 1.2f), fade);
                    _chainNovas[i].Pose(link.To, Vector3.One * ReiNovaRadius,
                        new Vector3(0, 0, -Elapsed * .4f), _constellationReleased ? fade : 0);
                }
                bool traveling = _chainTarget != null && (TargetAlive(_chainTarget) || TargetRemoved(_chainTarget));
                _models["ReiStar"].Root.Visible = traveling;
                _models["ReiLink"].Root.Visible = traveling;
                if (traveling)
                {
                    Vector2 target = IsInstanceValid(_chainTarget) && !_chainTarget!.IsQueuedForDeletion()
                        ? _chainTarget.GlobalPosition : _chainLastPosition;
                    Vector2 head = _chainFrom.Lerp(target, Mathf.Clamp(_chainTimer / ReiHopInterval, 0, 1));
                    PoseThread(_models["ReiLink"], _chainFrom, head, 1);
                    _models["ReiStar"].Pose(head, Vector3.One * 9,
                        new Vector3(.14f, .18f * Mathf.Sin(Elapsed * 8), -Elapsed * 4));
                }
                break;
            }
        }
    }

    private static void PoseThread(SpecialModel model, Vector2 from, Vector2 to, float opacity, float width = 3)
    {
        Vector2 axis = to - from;
        model.Pose(from, new Vector3(Mathf.Max(.001f, axis.Length()), width, width),
            new Vector3(0, 0, -axis.Angle()), axis.LengthSquared() > .01f ? opacity : 0);
    }

    public override void _Draw()
    {
        if (!Active || CutinActive) return;
        DrawSpecialFlourish();
        DrawSpecialCues();
        if (_specialView != null)
            DrawTextureRectRegion(_specialView.GetTexture(), Field.Rect, new Rect2(Field.Rect.Position * 4, Field.Rect.Size * 4));
    }

    private void DrawSpecialFlourish()
    {
        Color color = Character switch { Job.Melee => AkariLight, Job.Heal => KoharuLight, Job.Magic => ReiLight, _ => MinaLight };
        float climax = Character switch
        {
            Job.Tank => _beamReleased ? Mathf.Clamp(1 - (Elapsed - BeamVolleyAt) / .6f, 0, 1) : 0,
            Job.Melee => _slashStage == 3 ? Mathf.Clamp(1 - (Elapsed - _slashAt) / .5f, 0, 1) : 0,
            Job.Heal => _bloomCount == 4 ? Mathf.Clamp(1 - (Elapsed - _bloomAt) / .9f, 0, 1) : 0,
            _ => _constellationReleased ? Mathf.Clamp(1 - (Elapsed - _constellationAt) / .6f, 0, 1) : 0,
        };
        if (climax > 0) DrawRect(Field.Rect, new Color(SpecialInk, climax * .23f));
        float birth = Mathf.Clamp(Elapsed / .4f, 0, 1);
        if (birth < 1)
        {
            DrawCircle(Origin, 12 + birth * 34, new Color(color, (1 - birth) * .12f));
            for (int i = 0; i < 12; i++)
            {
                Vector2 ray = Vector2.FromAngle(i * Mathf.Tau / 12 + Direction.Angle());
                Vector2 from = Origin + ray * (14 + birth * 24);
                DrawLine(from, from + ray * (9 * (1 - birth)), new Color(color, (1 - birth) * .8f), .8f, true);
            }
        }
        if (Character == Job.Tank && _beamReleased)
        {
            float age = Elapsed - BeamVolleyAt;
            float fade = Mathf.Clamp(1 - age / .4f, 0, 1);
            DrawLine(_beamStart, _beamEnd, new Color(MinaLight, .2f * fade), BeamRadius * 3.3f, true);
            DrawLine(_beamStart, _beamEnd, new Color(MinaLight, .24f * fade), BeamRadius * 2.4f, true);
            UiKit.RadialGlow(this, _beamStart, 72 + BeamRadius, MinaLight, .6f * fade);
            Vector2 side = Direction.Orthogonal();
            for (int i = 0; i < 16; i++)
            {
                float travel = Mathf.PosMod(age * 570 + i * 31, 440);
                Vector2 at = _beamStart + Direction * travel + side * ((i % 2 == 0 ? -1 : 1) * (BeamRadius + 3 + i % 4 * 2));
                DrawLine(at - Direction * (12 + i % 3 * 8), at, new Color(MinaLight, .8f * fade), .8f, true);
            }
        }
        if (Character == Job.Melee)
            for (int i = 0; i < Mathf.Min(_slashStage, 2); i++)
            {
                float age = Elapsed - AkariWindup - AkariDashDuration - (.10f + i * .20f);
                float fade = Mathf.Clamp(1 - age / .34f, 0, 1);
                var (from, to) = AkariCutEndpoints(i + 1);
                DrawLine(from, to, new Color(AkariLight, fade * .28f), AkariCutRadius * 3.8f, true);
                for (int j = 0; j < 7; j++)
                {
                    Vector2 center = from.Lerp(to, (j + .5f) / 7);
                    Vector2 outwards = Direction.Orthogonal() * (j % 2 == 0 ? 1 : -1);
                    Vector2 head = center + outwards * (8 + age * 30);
                    DrawLine(head, head - outwards * 4 * fade, new Color(AkariLight, fade), .75f, true);
                }
            }
        if (Character == Job.Melee && _slashStage == 3)
        {
            float age = Elapsed - _slashAt;
            float fade = Mathf.Clamp(1 - age / .6f, 0, 1);
            DrawArc(AkariFinaleCenter, AkariFinaleReach, Direction.Angle() + AkariFinaleTurn - 1.35f, Direction.Angle() + AkariFinaleTurn + 1.35f,
                80, new Color(AkariLight, fade * .3f), AkariFinaleWidth * 3.4f, true);
            for (int i = 0; i < 22; i++)
            {
                Vector2 at = AkariFinalePoint(i / 21f);
                Vector2 ray = (at - AkariFinaleCenter).Normalized();
                Vector2 spark = at + ray * age * (35 + i % 4 * 12);
                DrawLine(spark - ray * 7, spark, new Color(AkariLight, fade), 1.2f, true);
            }
            UiKit.RadialGlow(this, _dashTrail[^1], 58, AkariLight, fade * .38f);
        }
        if (Character == Job.Heal && BloomActive)
        {
            float fade = 1 - Mathf.Clamp((Elapsed - _bloomAt) / KoharuBloomDuration, 0, 1);
            DrawArc(Origin, BloomRadius, 0, Mathf.Tau, 96, new Color(KoharuLight, .24f * fade), 16, true);
            UiKit.RadialGlow(this, Origin, KoharuRadius * 1.8f, KoharuLight, fade * (_bloomCount == 4 ? .55f : .24f));
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.Tau / 24 + Elapsed * .28f;
                Vector2 ray = Vector2.FromAngle(angle);
                Vector2 tip = Origin + ray * (BloomRadius - 4);
                DrawLine(tip - ray * 7 * fade, tip, new Color(KoharuLight, .7f * fade), .9f, true);
            }
        }
        if (Character == Job.Magic && _constellationReleased)
        {
            float fade = Mathf.Clamp(1 - (Elapsed - _constellationAt) / .6f, 0, 1);
            foreach (var link in _links)
            {
                DrawLine(link.From, link.To, new Color(ReiLight, fade * .26f), ReiThreadRadius * 3, true);
                UiKit.RadialGlow(this, link.To, ReiNovaRadius * 1.8f, ReiLight, fade * .55f);
                DrawArc(link.To, ReiNovaRadius * (.6f + (1 - fade) * .4f), 0, Mathf.Tau, 40,
                    new Color(ReiLight, fade * .7f), 1.1f, true);
            }
        }
    }

    private void DrawSpecialCues()
    {
        switch (Character)
        {
            case Job.Tank:
            {
                Vector2 center = _player.GlobalPosition + Direction * MinaShieldOffset;
                Vector2 side = Direction.Orthogonal();
                if (!_beamReleased)
                {
                    DrawLine(center - side * MinaShieldHalfLength, center + side * MinaShieldHalfLength, SpecialInk, 2.3f, true);
                    int count = Mathf.Min(AbsorbedCount, 20);
                    for (int i = 0; i < count; i++)
                    {
                        Vector2 at = center - Direction * 6 + side * ((i - (count - 1) * .5f) * 2.2f);
                        DrawCircle(at, 1.1f, MinaLight);
                        DrawCircle(at - Direction * .3f, .4f, Colors.White);
                    }
                }
                else
                {
                    float fade = 1 - Mathf.Pow(Mathf.Clamp((Elapsed - MinaGuardDuration) / MinaBeamDuration, 0, 1), 3);
                    DrawLine(_beamStart, _beamEnd, new Color(MinaLight, .12f * fade), BeamRadius * 2, true);
                }
                foreach (var mote in _absorptions)
                {
                    float t = Mathf.Clamp((Elapsed - mote.Born) / .35f, 0, 1);
                    Vector2 at = mote.Position.Lerp(center, 1 - Mathf.Pow(1 - t, 3));
                    DrawLine(at, at.Lerp(mote.Position, .20f), new Color(MinaLight, (1 - t) * .8f), 1f, true);
                    DrawCircle(at, .5f + (1 - t) * 1.1f, new Color(Colors.White, 1 - t));
                }
                break;
            }
            case Job.Melee:
            {
                float fade = Elapsed <= AkariWindup + AkariDashDuration ? 1
                    : 1 - Mathf.Clamp((Elapsed - AkariWindup - AkariDashDuration) / AkariRecovery, 0, 1);
                Vector2 side = Direction.Orthogonal() * AkariSlashRadius;
                for (int i = 1; i < _dashTrail.Count; i++)
                {
                    Vector2 from = _dashTrail[i-1], to = _dashTrail[i];
                    if (from.DistanceSquaredTo(to) < .01f) continue;
                    float k = fade * i / _dashTrail.Count;
                    DrawLine(from, to, new Color(AkariLight, .09f * k), AkariSlashRadius * 2, true);
                    DrawLine(from - side, to - side, new Color(AkariLight, .45f * k), .65f, true);
                    DrawLine(from + side, to + side, new Color(AkariLight, .45f * k), .65f, true);
                }
                break;
            }
            case Job.Heal:
            {
                float fade = Elapsed <= KoharuDuration ? 1 : 1 - Mathf.Clamp((Elapsed - KoharuDuration) / (KoharuBloomDuration + .1f), 0, 1);
                if (Elapsed < KoharuDuration)
                {
                    DrawCircle(Origin, KoharuRadius, new Color(KoharuLight, .055f));
                    DrawArc(Origin, KoharuRadius, 0, Mathf.Tau, 96, new Color(SpecialInk, .75f), 1.4f, true);
                    DrawArc(Origin, KoharuRadius - 3.8f, -Mathf.Pi/2,
                        -Mathf.Pi/2 + Mathf.Tau * (1 - Elapsed / KoharuDuration), 72, new Color(Colors.White, .85f), .7f, true);
                    bool inside = _player.GlobalPosition.DistanceSquaredTo(Origin) <= KoharuRadius * KoharuRadius;
                    Vector2 heart = Origin + new Vector2(0, 22);
                    UiKit.Heart(this, heart, 4.5f, SpecialInk);
                    UiKit.Heart(this, heart, 3.5f, new Color(KoharuLight, inside ? .9f : .25f));
                    float charge = Mathf.Clamp(Elapsed / KoharuDuration, 0, 1);
                    if (inside && charge > .04f) UiKit.Heart(this, heart, 3.5f * charge, Colors.White);
                }
                foreach (var mote in _absorptions)
                {
                    float t = Mathf.Clamp((Elapsed - mote.Born) / .35f, 0, 1);
                    float angle = (mote.Position - Origin).Angle();
                    DrawArc(Origin, KoharuRadius - .6f, angle - .14f - t*.12f, angle + .14f + t*.12f,
                        12, new Color(Colors.White, (1 - t) * fade), 1.6f * (1 - t), true);
                }
                break;
            }
            case Job.Magic:
                if (_chainTarget != null && (TargetAlive(_chainTarget) || TargetRemoved(_chainTarget)))
                {
                    Vector2 target = IsInstanceValid(_chainTarget) && !_chainTarget.IsQueuedForDeletion()
                        ? _chainTarget.GlobalPosition : _chainLastPosition;
                    DrawArc(target, 9, Elapsed * 4, Elapsed * 4 + Mathf.Pi * 1.4f, 28, new Color(ReiLight, .65f), .8f, true);
                }
                break;
        }
    }

}
