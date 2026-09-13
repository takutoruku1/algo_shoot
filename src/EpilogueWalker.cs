using Godot;

internal sealed class EpilogueWalker
{
    internal readonly record struct Gait(float Cycle, float Phase, float Stride, float Lift, float Support, float Bob, float Sway, float ArmSwing);
    private readonly record struct ArmSource(Vector2 Shoulder, Vector2 Wrist, float Radius);
    private readonly record struct LegSource(Vector2 Hip, Vector2 Knee, Vector2 Ankle, Vector2 TargetHip);
    internal readonly record struct LegPose(Vector2 Hip, Vector2 Knee, Vector2 Ankle, float SoleY);

    private sealed class Leg
    {
        public readonly LegSource Source;
        public readonly Vector2[][] Points = new Vector2[5][];
        public readonly Vector2[][] Uv = new Vector2[5][];
        public readonly Vector2[][] Deformed = new Vector2[5][];
        public readonly float SoleOffset, UpperLength, LowerLength;

        public Leg(Texture2D texture, LegSource source, float minX, float maxX, Gait gait)
        {
            Source = source;
            using var image = texture.GetImage();
            int bottom = image.GetHeight() - 1;
            for (; bottom > source.Ankle.Y; bottom--)
            {
                bool found = false;
                for (int x = (int)minX; x < (int)maxX; x++)
                    if (image.GetPixel(x, bottom).A > 0.5f) { found = true; break; }
                if (found) break;
            }
            SoleOffset = bottom - source.Ankle.Y;
            float upper = source.Hip.DistanceTo(source.Knee), lower = source.Knee.DistanceTo(source.Ankle);
            float standing = 359f - SoleOffset - source.TargetHip.Y;
            float reach = 0;
            for (int sample = 0; sample < 360; sample++)
            {
                float phase = sample / 360f;
                Vector2 foot = Step(gait, phase);
                float rise = gait.Bob * Mathf.Pow(Mathf.Cos(Mathf.Tau * (phase - gait.Support * 0.5f)), 2f);
                reach = Mathf.Max(reach, new Vector2(foot.X, standing + foot.Y + rise).Length());
            }
            float scale = (reach + 0.25f) / (upper + lower);
            UpperLength = upper * scale;
            LowerLength = lower * scale;
            float[] rows = { source.Hip.Y, (source.Hip.Y + source.Knee.Y) * 0.5f, source.Knee.Y,
                (source.Knee.Y + source.Ankle.Y) * 0.5f, source.Ankle.Y, texture.GetHeight() };
            Vector2 Edge(float y, bool right)
            {
                float center = y < source.Knee.Y
                    ? Mathf.Lerp(source.Hip.X, source.Knee.X, (y - source.Hip.Y) / (source.Knee.Y - source.Hip.Y))
                    : Mathf.Lerp(source.Knee.X, source.Ankle.X, Mathf.Clamp((y - source.Knee.Y) / (source.Ankle.Y - source.Knee.Y), 0, 1));
                float radius = y >= source.Ankle.Y ? 40f : 25f;
                return new Vector2(Mathf.Clamp(center + (right ? radius : -radius), minX, maxX), y);
            }
            for (int i = 0; i < Points.Length; i++)
            {
                Points[i] = new[] { Edge(rows[i], false), Edge(rows[i], true), Edge(rows[i + 1], true), Edge(rows[i + 1], false) };
                Uv[i] = new Vector2[4];
                Deformed[i] = new Vector2[4];
                for (int v = 0; v < 4; v++) Uv[i][v] = Points[i][v] / texture.GetSize();
            }
        }
    }

    public string Name { get; }
    public Gait Motion { get; }
    private readonly Texture2D _body, _legs;
    private readonly Leg[] _rig;
    private readonly ArmSource _nearArm, _farArm;
    private readonly Vector2[] _bodyPoints, _bodyUv, _bodyDeformed;
    private readonly int[] _bodyTriangles;
    private readonly Vector2[]? _prop, _propUv, _propPoints;
    private static readonly Color[] BodyColor = { Colors.White };
    private static readonly Color[] White = { Colors.White, Colors.White, Colors.White, Colors.White };
    private static readonly int[] QuadTriangles = { 0, 1, 2, 0, 2, 3 };
    internal const float Height = 52f, FootY = 216f * 0.725f;

    private EpilogueWalker(string name, Gait gait, float bodyCut, float split, LegSource back, LegSource front,
        ArmSource nearArm, ArmSource farArm, Vector2[]? prop = null)
    {
        Name = name;
        Motion = gait;
        _nearArm = nearArm;
        _farArm = farArm;
        _body = GD.Load<Texture2D>($"res://char/v3/walk/{name}_walk_3.png");
        _legs = GD.Load<Texture2D>($"res://char/v3/walk/{name}_walk_1.png");
        _rig = new[] { new Leg(_legs, back, 0, split, gait), new Leg(_legs, front, split, _legs.GetWidth(), gait) };
        int columns = Mathf.CeilToInt(_body.GetWidth() / 8f), rows = Mathf.CeilToInt(bodyCut / 8f);
        _bodyPoints = new Vector2[(columns + 1) * (rows + 1)];
        _bodyUv = new Vector2[_bodyPoints.Length];
        _bodyDeformed = new Vector2[_bodyPoints.Length];
        _bodyTriangles = new int[columns * rows * 6];
        for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                int index = y * (columns + 1) + x;
                _bodyPoints[index] = new Vector2(_body.GetWidth() * x / (float)columns, bodyCut * y / rows);
                _bodyUv[index] = _bodyPoints[index] / _body.GetSize();
                if (x == columns || y == rows) continue;
                int triangle = (y * columns + x) * 6;
                _bodyTriangles[triangle] = index;
                _bodyTriangles[triangle + 1] = index + 1;
                _bodyTriangles[triangle + 2] = index + columns + 2;
                _bodyTriangles[triangle + 3] = index;
                _bodyTriangles[triangle + 4] = index + columns + 2;
                _bodyTriangles[triangle + 5] = index + columns + 1;
            }
        _prop = prop;
        if (prop != null)
        {
            _propUv = new Vector2[prop.Length];
            _propPoints = new Vector2[prop.Length];
            for (int i = 0; i < prop.Length; i++) _propUv[i] = prop[i] / _body.GetSize();
        }
    }

    public static EpilogueWalker[] CreateParty() => new[]
    {
        new EpilogueWalker("akari", new Gait(1.80f, 0.00f, 30f, 7.0f, 0.62f, 5.6f, 0.30f, 9f), 273f, 85f,
            new LegSource(new(70, 272), new(51, 302), new(27, 339), new(85, 267)),
            new LegSource(new(103, 272), new(117, 301), new(135, 339), new(100, 267)),
            new ArmSource(new(80, 165), new(64, 252), 14), new ArmSource(new(108, 166), new(113, 236), 8)),
        new EpilogueWalker("koharu", new Gait(2.05f, 0.31f, 24f, 6.0f, 0.67f, 4.5f, 0.40f, 7f), 270f, 95f,
            new LegSource(new(68, 278), new(42, 311), new(23, 337), new(91, 265)),
            new LegSource(new(121, 278), new(138, 311), new(156, 337), new(106, 265)),
            new ArmSource(new(87, 161), new(75, 250), 13), new ArmSource(new(115, 163), new(122, 232), 6),
            new[] { new Vector2(73, 258), new Vector2(82, 256), new Vector2(91, 303), new Vector2(81, 304) }),
        new EpilogueWalker("rei", new Gait(2.30f, 0.67f, 28f, 4.0f, 0.69f, 4.8f, 0.45f, 6f), 260f, 73f,
            new LegSource(new(58, 264), new(39, 302), new(18, 338), new(79, 255)),
            new LegSource(new(91, 264), new(106, 302), new(123, 339), new(93, 255)),
            new ArmSource(new(78, 154), new(65, 253), 14), new ArmSource(new(101, 157), new(105, 250), 7)),
        new EpilogueWalker("mina", new Gait(1.95f, 0.48f, 20f, 5.0f, 0.65f, 3.9f, 0.20f, 5f), 290f, 114f,
            new LegSource(new(77, 292), new(63, 317), new(48, 341), new(110, 283)),
            new LegSource(new(149, 292), new(159, 317), new(173, 342), new(124, 283)),
            new ArmSource(new(129, 135), new(109, 239), 12), new ArmSource(new(148, 139), new(151, 213), 8)),
    };

    internal float BodyOffset(double time)
        => -Motion.Bob * Mathf.Pow(Mathf.Cos(Mathf.Tau * ((float)(time / Motion.Cycle) + Motion.Phase - Motion.Support * 0.5f)), 2f);

    internal Transform2D Torso(double time)
    {
        float angle = Mathf.DegToRad(Motion.Sway) * Mathf.Sin(Mathf.Tau * ((float)(time / Motion.Cycle) + Motion.Phase));
        Vector2 pivot = (_rig[0].Source.TargetHip + _rig[1].Source.TargetHip) * 0.5f;
        return new Transform2D(angle, pivot + new Vector2(0, BodyOffset(time)) - pivot.Rotated(angle));
    }

    internal LegPose Pose(int side, double time)
    {
        var leg = _rig[side];
        float phase = Mathf.PosMod((float)(time / Motion.Cycle) + Motion.Phase + side * 0.5f, 1f);
        Vector2 step = Step(Motion, phase);
        Vector2 hip = Torso(time) * leg.Source.TargetHip;
        Vector2 ankle = new Vector2(leg.Source.TargetHip.X, 359f - leg.SoleOffset) + step;
        Vector2 direction = ankle - hip;
        float distance = direction.Length();
        direction /= distance;
        float along = (leg.UpperLength * leg.UpperLength - leg.LowerLength * leg.LowerLength + distance * distance) / (2f * distance);
        float bend = Mathf.Sqrt(Mathf.Max(0, leg.UpperLength * leg.UpperLength - along * along));
        Vector2 knee = hip + direction * along + new Vector2(direction.Y, -direction.X) * bend;
        return new LegPose(hip, knee, ankle, ankle.Y + leg.SoleOffset);
    }

    private static Vector2 Step(Gait motion, float phase)
    {
        if (phase < motion.Support)
            return new Vector2(Mathf.Lerp(motion.Stride, -motion.Stride, phase / motion.Support), 0);
        float swing = (phase - motion.Support) / (1f - motion.Support);
        return new Vector2(Mathf.Lerp(-motion.Stride, motion.Stride, Mathf.SmoothStep(0, 1, swing)),
            -motion.Lift * Mathf.Pow(Mathf.Sin(swing * Mathf.Pi), 2f));
    }

    internal float ArmAngle(int side, double time)
    {
        float phase = (float)(time / Motion.Cycle) + Motion.Phase + side * 0.5f;
        return Mathf.DegToRad(Motion.ArmSwing) * Mathf.Cos(Mathf.Tau * phase);
    }

    private static Vector2 ArmDisplacement(Vector2 point, ArmSource arm, float angle)
    {
        float down = point.Y - arm.Shoulder.Y;
        float along = Mathf.Clamp(down / (arm.Wrist.Y - arm.Shoulder.Y), 0, 1);
        float center = Mathf.Lerp(arm.Shoulder.X, arm.Wrist.X, along);
        float distance = (point.X - center) / arm.Radius;
        float weight = Mathf.Exp(-0.5f * distance * distance);
        weight *= Mathf.SmoothStep(0, 1, Mathf.Clamp(down / 24f, 0, 1));
        weight *= 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp((point.Y - arm.Wrist.Y - 14f) / 16f, 0, 1));
        Vector2 offset = point - arm.Shoulder;
        return (offset.Rotated(angle) - offset) * weight;
    }

    private static Vector2 Retarget(Vector2 point, Vector2 from, Vector2 to, Vector2 targetFrom, Vector2 targetTo)
    {
        Vector2 source = to - from, target = targetTo - targetFrom;
        float length = source.Length();
        Vector2 sourceAxis = source / length, targetAxis = target.Normalized();
        Vector2 offset = point - from;
        return targetFrom + target * (offset.Dot(sourceAxis) / length)
            + new Vector2(targetAxis.Y, -targetAxis.X) * offset.Dot(new Vector2(sourceAxis.Y, -sourceAxis.X));
    }

    public void Draw(CanvasItem canvas, float x, double time, float dawn)
    {
        float scale = Height / _body.GetHeight();
        float width = _body.GetWidth() * scale;
        canvas.DrawSetTransform(new Vector2(x, FootY), 0, new Vector2(1, 0.22f));
        canvas.DrawCircle(Vector2.Zero, width * 0.3f, new Color(0, 0, 0, Mathf.Lerp(0.18f, 0.32f, dawn)));
        canvas.DrawSetTransform(new Vector2(x - width * 0.5f, FootY - Height), 0, new Vector2(scale, scale));
        for (int side = 0; side < _rig.Length; side++)
        {
            var leg = _rig[side];
            var source = leg.Source;
            LegPose pose = Pose(side, time);
            for (int row = 0; row < leg.Points.Length; row++)
            {
                for (int v = 0; v < 4; v++)
                {
                    Vector2 point = leg.Points[row][v];
                    Vector2 upper = Retarget(point, source.Hip, source.Knee, pose.Hip, pose.Knee);
                    Vector2 lower = Retarget(point, source.Knee, source.Ankle, pose.Knee, pose.Ankle);
                    float kneeBlend = Mathf.SmoothStep(0, 1, Mathf.Clamp((point.Y - source.Knee.Y + 8f) / 16f, 0, 1));
                    Vector2 deformed = upper.Lerp(lower, kneeBlend);
                    // The shoe stays level during contact instead of rotating with the shin.
                    float footBlend = Mathf.SmoothStep(0, 1, Mathf.Clamp((point.Y - source.Ankle.Y + 8f) / 8f, 0, 1));
                    leg.Deformed[row][v] = deformed.Lerp(point + pose.Ankle - source.Ankle, footBlend);
                }
                RenderingServer.CanvasItemAddTriangleArray(canvas.GetCanvasItem(), QuadTriangles,
                    leg.Deformed[row], White, leg.Uv[row], System.Array.Empty<int>(), System.Array.Empty<float>(), _legs.GetRid());
            }
        }
        Transform2D torso = Torso(time);
        canvas.DrawSetTransform(new Vector2(x - width * 0.5f, FootY - Height) + torso.Origin * scale,
            torso.Rotation, new Vector2(scale, scale));
        float nearAngle = ArmAngle(0, time), farAngle = ArmAngle(1, time);
        for (int i = 0; i < _bodyPoints.Length; i++)
        {
            Vector2 point = _bodyPoints[i];
            _bodyDeformed[i] = point + ArmDisplacement(point, _nearArm, nearAngle) + ArmDisplacement(point, _farArm, farAngle);
        }
        RenderingServer.CanvasItemAddTriangleArray(canvas.GetCanvasItem(), _bodyTriangles,
            _bodyDeformed, BodyColor, _bodyUv, System.Array.Empty<int>(), System.Array.Empty<float>(), _body.GetRid());
        if (_prop != null)
        {
            for (int i = 0; i < _prop.Length; i++)
                _propPoints![i] = _nearArm.Shoulder + (_prop[i] - _nearArm.Shoulder).Rotated(nearAngle);
            canvas.DrawPolygon(_propPoints!, White, _propUv!, _body);
        }
        canvas.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
