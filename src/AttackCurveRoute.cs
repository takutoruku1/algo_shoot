using Godot;
using System;

internal sealed class AttackCurveRoute
{
    internal Vector2[] Points { get; }
    private readonly float[] _distances;
    internal float Length => _distances[^1];

    internal AttackCurveRoute(Curve2D curve)
    {
        Points = curve.TessellateEvenLength(6, 4);
        _distances = new float[Points.Length];
        for (int i = 1; i < Points.Length; i++)
            _distances[i] = _distances[i - 1] + Points[i - 1].DistanceTo(Points[i]);
    }

    internal Vector2 At(float distance)
    {
        if (distance <= 0) return Points[0];
        if (distance >= Length) return Points[^1];
        int next = Array.BinarySearch(_distances, distance);
        if (next >= 0) return Points[next];
        next = ~next;
        return Points[next - 1].Lerp(Points[next],
            (distance - _distances[next - 1]) / (_distances[next] - _distances[next - 1]));
    }

    internal static AttackCurveRoute Between(Curve2D profile, Vector2 from, Vector2 to, float bend = 1)
    {
        Vector2 along = to - from, across = new(-along.Y * bend, along.X * bend);
        Vector2 Map(Vector2 point) => along * point.X + across * point.Y;
        using var curve = new Curve2D();
        for (int i = 0; i < profile.PointCount; i++)
            curve.AddPoint(from + Map(profile.GetPointPosition(i)), Map(profile.GetPointIn(i)), Map(profile.GetPointOut(i)));
        return new AttackCurveRoute(curve);
    }
}
