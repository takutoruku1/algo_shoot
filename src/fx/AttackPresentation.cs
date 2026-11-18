using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AttackPresentation : Node2D
{
    public Job Character { get; init; }
    public int Depth { get; init; } = 1;
    public float? PreviewSpeed { get; set; }
    private GpuParticles2D _particles = null!;
    private Node2D _charge = null!;
    private Node2D _wakeCanvas = null!;
    private Node2D _burstCanvas = null!;
    private readonly List<(Vector2 At, float Angle, double Born)> _bursts = new();
    private Color _ink;
    private double _wakeTime;
    private sealed class Wake
    {
        internal ulong Activation;
        internal readonly Vector2[] Points = new Vector2[6];
        internal int Count;
    }
    private readonly Dictionary<Bullet, Wake> _wakes = new();
    private AnimationPlayer _animation = null!;
    private readonly RandomNumberGenerator _random = new() { Seed = 8341 };
    private double _time, _lastPulse = -1, _fireLeft, _retire = -1;
    private float _particleScale;
    private double _manualParticleTime;
    private ulong _retireFrame;
    internal int Launches { get; private set; }
    internal int Dissolves { get; private set; }

    public override void _Ready()
    {
        TopLevel = true;
        GlobalPosition = Vector2.Zero;
        ZIndex = Depth;
        ZAsRelative = false;
        ProcessMode = ProcessModeEnum.Always;
        var art = BulletArt.BossPattern(Character, 1);
        _ink = art.Accent;
        _particleScale = 1.7f / Mathf.Max(art.Region.Size.X, art.Region.Size.Y);
        var shader = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/attack_motes.gdshader") };
        shader.SetShaderParameter("field_bounds", new Vector4(Field.Left, Field.Top, Field.Right, Field.Bottom));
        var fade = new Gradient { Colors = new[] { new Color(1, 1, 1, .55f), new Color(1, 1, 1, 0) } };
        _particles = new GpuParticles2D
        {
            Name = "AttackMotes", Amount = 48, Lifetime = .28, Emitting = false,
            LocalCoords = false, FixedFps = 60, UseFixedSeed = true, Seed = 8341,
            Texture = art.Cropped, Material = shader, VisibilityRect = Field.Rect,
            ProcessMaterial = new ParticleProcessMaterial
            {
                ParticleFlagDisableZ = true, Gravity = Vector3.Zero,
                ScaleMin = _particleScale, ScaleMax = _particleScale, DampingMin = 12, DampingMax = 20,
                ColorRamp = new GradientTexture1D { Gradient = fade },
            },
        };
        AddChild(_particles);
        _wakeCanvas = new Node2D { Name = "ShortWakes", Material = shader };
        _wakeCanvas.Draw += () => {
            foreach (var wake in _wakes.Values)
                for (int i = wake.Count - 1; i > 0; i--)
                {
                    float strength = 1 - i / 6f;
                    _wakeCanvas.DrawLine(wake.Points[i], wake.Points[i - 1],
                        new Color(_ink.Lerp(Colors.White, .2f), strength * .32f), .35f + strength * .7f, true);
                }
        };
        AddChild(_wakeCanvas);
        _burstCanvas = new Node2D { Name = "ReleaseBursts", Material = shader };
        _burstCanvas.Draw += DrawBursts;
        AddChild(_burstCanvas);
        _charge = new Node2D { Name = "Charge", Visible = false, Material = shader };
        _charge.Draw += DrawCharge;
        AddChild(_charge);
        var glow = new GradientTexture2D {
            Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1, .5f),
            Gradient = new Gradient { Offsets = new[] { 0f, .12f, .42f, 1f },
                Colors = new[] { new Color("fff5ee"), new Color(_ink, .75f), new Color(_ink, .18f), new Color(_ink, 0) } },
        };
        _charge.AddChild(new Sprite2D { Texture = glow, UseParentMaterial = true, Scale = Vector2.One * .5f });
        _animation = new AnimationPlayer { CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual };
        AddChild(_animation);
        _animation.AddAnimationLibrary("", GD.Load<AnimationLibrary>("res://assets/fx/attack_presentation.tres"));
    }

    public void Advance(double delta, Vector2 origin, float chargeProgress = -1)
    {
        _time += delta;
        _bursts.RemoveAll(burst => _time - burst.Born >= .28);
        _burstCanvas.QueueRedraw();
        _charge.QueueRedraw();
        if (PreviewSpeed == 0 || BossPracticeRoot.Active is { PreviewPaused: true, CurveEditor: null })
        {
            _manualParticleTime += delta;
            int frames = (int)(_manualParticleTime * 60 + .00001);
            if (frames > 0)
            {
                // Godot processes the requested interval inclusively, so omit its final boundary.
                _particles.RequestParticlesProcess((float)(frames / 60d - .000001));
                _manualParticleTime -= frames / 60d;
            }
        }
        if (_fireLeft > 0)
        {
            _animation.Advance(delta);
            _fireLeft -= delta;
            if (_fireLeft <= 0) _charge.Hide();
        }
        else if (chargeProgress >= 0)
        {
            _charge.Position = origin;
            _charge.Rotation = 0;
            _charge.Show();
            if (_animation.CurrentAnimation != "charge") _animation.Play("charge");
            _animation.Seek(Mathf.Clamp(chargeProgress, 0, 1), true);
        }
        else _charge.Hide();
    }

    public void Launch(Vector2 at, Vector2 direction)
    {
        if (_time - _lastPulse < .08) return;
        _lastPulse = _time;
        Launches++;
        _charge.Position = at;
        _charge.Rotation = direction.Angle();
        _charge.Show();
        _animation.Play("fire");
        _animation.Advance(0);
        _fireLeft = .28;
        if (_bursts.Count == 4) _bursts.RemoveAt(0);
        _bursts.Add((at, direction.Angle(), _time));
        _burstCanvas.QueueRedraw();
        _charge.QueueRedraw();
        Emit(at, direction, 6);
    }

    private static void Stroke(Node2D canvas, Vector2[] points, Color ink, float alpha = 1)
    {
        canvas.DrawPolyline(points, new Color(ink, alpha * .18f), 5.5f, true);
        canvas.DrawPolyline(points, new Color(ink * .3f, alpha * .65f), 2.8f, true);
        canvas.DrawPolyline(points, new Color(ink.Lerp(Colors.White, .6f), alpha), .9f, true);
    }

    private static Vector2[] Petal(float angle, float reach, float width)
    {
        var points = new Vector2[17];
        var axis = Vector2.FromAngle(angle);
        var normal = axis.Orthogonal();
        for (int i = 0; i < points.Length; i++)
        {
            float t = i / 16f;
            points[i] = axis * (Mathf.Sin(t * Mathf.Pi) * reach)
                + normal * (Mathf.Sin(t * Mathf.Tau) * width);
        }
        return points;
    }

    private void DrawCharge()
    {
        if (_fireLeft > 0) return;
        float breath = .5f + .5f * Mathf.Sin((float)_time * 6);
        switch (Character)
        {
            case Job.Melee:
                for (int i = 0; i < 3; i++)
                {
                    _charge.DrawSetTransform(new Vector2(i * 3 - 3, -i * 2 + 2), (i - 1) * .16f);
                    Stroke(_charge, new[] { new Vector2(-11, -8), new Vector2(11, -8), new Vector2(11, 8),
                        new Vector2(-11, 8), new Vector2(-11, -8) }, _ink, .45f + i * .25f);
                    Stroke(_charge, new[] { new Vector2(-11, -8), new Vector2(0, 2), new Vector2(11, -8) }, _ink, .8f);
                }
                break;
            case Job.Heal:
                for (int i = 0; i < 5; i++)
                    Stroke(_charge, Petal(i * Mathf.Tau / 5 - Mathf.Pi / 2, 18 + breath * 3, 4), _ink, .8f);
                _charge.DrawArc(Vector2.Zero, 6 + breath * 2, 0, Mathf.Tau, 24, new Color(_ink, .6f), 1.2f, true);
                break;
            case Job.Magic:
                for (int i = 0; i < 4; i++)
                {
                    _charge.DrawSetTransform(Vector2.Zero, i * Mathf.Pi / 2);
                    Stroke(_charge, new[] { new Vector2(6, -14), new Vector2(14, -14), new Vector2(14, -6) }, _ink);
                }
                _charge.DrawSetTransform(Vector2.Zero);
                Stroke(_charge, new[] { new Vector2(-9, -5 + breath * 10), new Vector2(9, -5 + breath * 10) }, _ink, .55f);
                break;
            default:
                for (int i = 0; i < 3; i++)
                {
                    _charge.DrawSetTransform(new Vector2(i * 4 - 4, -i * 5 + 5));
                    Stroke(_charge, new[] { new Vector2(-10, -5), new Vector2(10, -5), new Vector2(10, 5),
                        new Vector2(-2, 5), new Vector2(-7, 9), new Vector2(-7, 5), new Vector2(-10, 5),
                        new Vector2(-10, -5) }, _ink, .4f + i * .3f);
                }
                break;
        }
        _charge.DrawSetTransform(Vector2.Zero);
    }

    private void DrawBursts()
    {
        foreach (var burst in _bursts)
        {
            float t = Mathf.Clamp((float)(_time - burst.Born) / .28f, 0, 1);
            float spread = 1 - Mathf.Pow(1 - t, 3);
            float alpha = Mathf.Pow(1 - t, 1.4f);
            _burstCanvas.DrawSetTransform(burst.At, burst.Angle);
            _burstCanvas.DrawCircle(Vector2.Zero, 5 + spread * 15, new Color(_ink, alpha * .13f));
            _burstCanvas.DrawCircle(Vector2.Zero, 3 * (1 - t), new Color(Colors.White, alpha * .85f));
            switch (Character)
            {
                case Job.Melee:
                    for (int i = -1; i <= 1; i++)
                    {
                        float reach = 12 + spread * (i == 0 ? 22 : 15);
                        Stroke(_burstCanvas, new[] { new Vector2(-5, i * 3), new Vector2(reach * .45f, i * (7 + spread * 7) - 4),
                            new Vector2(reach, i * (9 + spread * 10)), new Vector2(reach * .45f, i * (7 + spread * 7) + 4),
                            new Vector2(-5, i * 3) }, _ink, alpha);
                    }
                    break;
                case Job.Heal:
                    for (int i = -2; i <= 2; i++)
                        Stroke(_burstCanvas, Petal(i * .53f, 12 + spread * 19, 2 + spread * 3), _ink, alpha);
                    break;
                case Job.Magic:
                    for (int i = 0; i < 4; i++)
                    {
                        float r = 8 + spread * 17;
                        _burstCanvas.DrawSetTransform(burst.At, burst.Angle + i * Mathf.Pi / 2);
                        Stroke(_burstCanvas, new[] { new Vector2(r - 8, -r), new Vector2(r, -r), new Vector2(r, -r + 8) }, _ink, alpha);
                    }
                    break;
                default:
                    for (int i = 0; i < 3; i++)
                    {
                        float x = 3 + i * (5 + spread * 5);
                        float height = 8 + spread * (6 + i * 4);
                        Stroke(_burstCanvas, new[] { new Vector2(x - 4, -height), new Vector2(x + 4, -height + 4),
                            new Vector2(x + 4, height - 4), new Vector2(x - 4, height) }, _ink, alpha * (1 - i * .18f));
                    }
                    break;
            }
        }
        _burstCanvas.DrawSetTransform(Vector2.Zero);
    }

    public void Trace(IEnumerable<Bullet> shots, double delta)
    {
        _wakeTime += delta;
        if (_wakeTime < 1d / 60) return;
        _wakeTime %= 1d / 60;
        foreach (var entry in _wakes.ToArray())
            if (!IsInstanceValid(entry.Key) || !entry.Key.Active || entry.Key.ActivationId != entry.Value.Activation
                || entry.Key.LeadIn || entry.Key.CollisionLayer == 0 || !Field.Rect.HasPoint(entry.Key.GlobalPosition))
                _wakes.Remove(entry.Key);
        foreach (var shot in shots)
        {
            if (_wakes.Count >= 64) break;
            if (!_wakes.ContainsKey(shot) && !shot.LeadIn && shot.CollisionLayer != 0 && Field.Rect.HasPoint(shot.GlobalPosition))
            {
                _wakes[shot] = new Wake { Activation = shot.ActivationId };
                Launch(shot.GlobalPosition, shot.Velocity);
            }
        }
        foreach (var entry in _wakes)
        {
            var wake = entry.Value;
            Vector2 at = entry.Key.GlobalPosition;
            if (wake.Count > 0 && at.DistanceSquaredTo(wake.Points[0]) > 144) wake.Count = 0;
            Array.Copy(wake.Points, 0, wake.Points, 1, Math.Min(wake.Count, 5));
            wake.Points[0] = at;
            wake.Count = Math.Min(wake.Count + 1, 6);
            while (wake.Count > 1 && at.DistanceSquaredTo(wake.Points[wake.Count - 1]) > 100) wake.Count--;
        }
        _wakeCanvas.QueueRedraw();
    }

    public void Dissolve(Vector2 at)
    {
        Dissolves++;
        Emit(at, Vector2.Zero, 3);
    }

    private void Emit(Vector2 at, Vector2 direction, int amount)
    {
        if (!Field.Rect.HasPoint(at)) return;
        for (int i = 0; i < amount; i++)
        {
            Vector2 velocity = Vector2.FromAngle(_random.RandfRange(0, Mathf.Tau)) * _random.RandfRange(7, 20)
                + direction.Normalized() * 7;
            var transform = new Transform2D(_random.RandfRange(0, Mathf.Tau), Vector2.One, 0, at);
            _particles.EmitParticle(transform, velocity, Colors.White, Colors.Transparent,
                (uint)(GpuParticles2D.EmitFlags.Position | GpuParticles2D.EmitFlags.RotationScale | GpuParticles2D.EmitFlags.Velocity));
        }
        if (PreviewSpeed == 0) _particles.RequestParticlesProcess(.000001f);
    }

    public void Finish(bool natural)
    {
        _wakes.Clear();
        _wakeCanvas.QueueRedraw();
        _charge.Hide();
        _fireLeft = 0;
        if (natural) { _retire = .3; _retireFrame = Engine.GetPhysicsFrames(); }
        else { Hide(); QueueFree(); }
    }

    public void Reset()
    {
        _bursts.Clear();
        _burstCanvas.QueueRedraw();
        _wakes.Clear();
        _wakeTime = 0;
        _wakeCanvas.QueueRedraw();
        _particles.Restart(true);
        _particles.Emitting = false;
        _charge.Hide();
        _fireLeft = 0;
        _lastPulse = -1;
        _manualParticleTime = 0;
    }

    public override void _Process(double delta)
    {
        float speed = PreviewSpeed ?? (GetParent().CanProcess() && !Hud.BubblePaused ? GameManager.EnemyTimeScale : 0);
        _particles.SpeedScale = speed;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_retire < 0 || Engine.GetPhysicsFrames() == _retireFrame || !GetParent().CanProcess() || Hud.BubblePaused) return;
        double elapsed = GameManager.EnemyDelta(delta);
        Advance(elapsed, _charge.Position);
        _retire -= elapsed;
        if (_retire <= 0) QueueFree();
    }
}
