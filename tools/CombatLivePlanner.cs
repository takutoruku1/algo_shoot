using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CombatRevisionQa
{
    private readonly record struct LiveThreat(Vector2 Position, Vector2 Previous, Transform2D Inverse, BulletFootprint Shape);
    private readonly record struct LivePath(Vector2 Position, Vector2 First, float Cost);
    private const int LiveColumns = 26, LiveRows = 16;
    private const float LiveCell = 16, LiveStep = 1f / 15;
    private const int LiveSteps = 36;
    private readonly List<LiveThreat>[][] _liveForecast = Enumerable.Range(0, LiveSteps + 1)
        .Select(_ => Enumerable.Range(0, LiveColumns * LiveRows).Select(_ => new List<LiveThreat>()).ToArray()).ToArray();

    private Vector2 PlanLiveMove(Player player, Enemy boss, Vector2 goal, float speed, bool guided = false)
    {
        foreach (var slice in _liveForecast)
            foreach (var cell in slice) cell.Clear();
        foreach (var bullet in Hostile())
        {
            float lead = bullet.LeadIn ? Read<float>(bullet, "_leadDur") - Read<float>(bullet, "_leadElapsed") : 0;
            Vector2 origin = bullet.LeadIn ? Read<Vector2>(bullet, "_leadTo") : bullet.Position;
            var inverse = new Transform2D(-bullet.GlobalRotation, Vector2.Zero);
            float age = Read<float>(bullet, "_age");
            bool accelerating = bullet.Accel && !Read<bool>(bullet, "_accelDone");
            float delay = bullet.Accel ? Read<float>(bullet, "_accelDelay") : 0;
            float slow = bullet.Accel ? Read<float>(bullet, "_accelStartSpeed") : 0;
            float fast = bullet.Accel ? Read<float>(bullet, "_fastSpeed") : 0;
            Vector2 direction = bullet.Accel ? Read<Vector2>(bullet, "_accelDir") : Vector2.Zero;
            for (int sample = 0; sample < _liveForecast.Length; sample++)
            {
                float time = sample * LiveStep - lead;
                if (time < 0) continue;
                Vector2 offset = accelerating ? direction * (Travel(age + time) - Travel(age)) : bullet.Velocity * time;
                float previousTime = Math.Max(0, time - LiveStep);
                Vector2 previous = accelerating ? direction * (Travel(age + previousTime) - Travel(age)) : bullet.Velocity * previousTime;
                AddThreat(sample, new LiveThreat(origin + offset, origin + previous, inverse, bullet.Footprint!));
            }

            float Travel(float time)
            {
                float ramp = bullet.AccelRampSeconds;
                float u = Mathf.Clamp(time - delay, 0, ramp);
                float extra = fast - slow;
                float distance = slow * time + extra * (u * u * u / (3 * ramp * ramp) + Math.Max(0, time - delay - ramp));
                return distance + extra * (u / ramp) * (u / ramp) / 120;
            }
        }
        var areas = GetTree().GetNodesInGroup("aoe").OfType<AreaStrike>()
            .Select(a => (Area: a, Time: (float)(Read<double>(a, "_warn") - Read<double>(a, "_t")))).ToArray();
        Vector2 bossCenter = boss.ShotCenter;
        var beam = new List<LivePath> { new(player.Position, Vector2.Zero, 0) };
        for (int step = 1; step <= LiveSteps; step++)
        {
            _livePlanDepth = step;
            var candidates = new Dictionary<(int, int), LivePath>();
            foreach (var path in beam)
            for (int move = -1; move < (guided ? 9 : 8); move++)
            {
                Vector2 direction = move < 0 ? Vector2.Zero : move == 8
                    ? (goal - path.Position).LimitLength(speed * LiveStep) / (speed * LiveStep)
                    : Vector2.FromAngle(Mathf.Tau * move / 8);
                Vector2 end = path.Position + direction * speed * LiveStep;
                if (end.X < global::Field.Left + 4 || end.X > global::Field.Right - 4 || end.Y < 4 || end.Y > global::Field.Bottom - 4) continue;
                float gap = Math.Min(end.DistanceTo(bossCenter) - 28, Clearance(step, end, path.Position));
                float time = step * LiveStep;
                foreach (var area in areas)
                    if (time >= area.Time - 0.035f && time <= area.Time + 0.06f && area.Area.CoversPoint(end)) gap = -1;
                if (gap < 0.45f) continue;
                float cost = path.Cost + Math.Max(0, 2.5f - gap) * 0.5f + end.DistanceTo(goal) * (guided ? 0.01f : 0.0025f);
                if (step == 1 && direction != _liveDirection) cost += 0.025f;
                var next = new LivePath(end, step == 1 ? direction : path.First, cost);
                var key = (Mathf.RoundToInt(end.X / 1.7f), Mathf.RoundToInt(end.Y / 1.7f));
                if (!candidates.TryGetValue(key, out var previous) || previous.Cost > cost) candidates[key] = next;
            }
            if (candidates.Count == 0)
            {
                if (step == 1) return ShapeAwareDodge(player, boss, _liveDirection, goal, speed, precise: true);
                return beam.MinBy(path => path.Cost).First;
            }
            beam = candidates.Values.OrderBy(path => path.Cost).Take(96).ToList();
        }
        return beam.MinBy(path => path.Cost).First;
    }

    private void AddThreat(int sample, LiveThreat threat)
    {
        float r = threat.Shape.Radius + Player.HitRadius + 4 + threat.Position.DistanceTo(threat.Previous);
        int x0 = Math.Max(0, (int)Math.Floor((threat.Position.X - r) / LiveCell));
        int y0 = Math.Max(0, (int)Math.Floor((threat.Position.Y - r) / LiveCell));
        int x1 = Math.Min(LiveColumns - 1, (int)Math.Floor((threat.Position.X + r) / LiveCell));
        int y1 = Math.Min(LiveRows - 1, (int)Math.Floor((threat.Position.Y + r) / LiveCell));
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++) _liveForecast[sample][y * LiveColumns + x].Add(threat);
    }

    private float Clearance(int sample, Vector2 position, Vector2? previousPosition = null)
    {
        float gap = 4;
        foreach (var threat in _liveForecast[sample][(int)(position.Y / LiveCell) * LiveColumns + (int)(position.X / LiveCell)])
        {
            Vector2 local = threat.Inverse * (position - threat.Position);
            Vector2 previous = previousPosition.HasValue ? threat.Inverse * (previousPosition.Value - threat.Previous) : local;
            Vector2 travel = local - previous;
            if (!threat.Shape.Bounds.Grow(gap + Player.HitRadius + travel.Length()).HasPoint(local)) continue;
            var hull = threat.Shape.Hull;
            if (hull.Length == 0)
            {
                gap = Math.Min(gap, MathF.Sqrt(PointSegmentSquared(Vector2.Zero, previous, local)) - threat.Shape.Radius - Player.HitRadius);
                continue;
            }
            bool positive = false, negative = false, previousPositive = false, previousNegative = false;
            float squared = float.MaxValue;
            for (int i = 0; i < hull.Length; i++)
            {
                Vector2 edge = hull[(i + 1) % hull.Length] - hull[i];
                float length = edge.LengthSquared();
                if (length < 0.000001f) continue;
                Vector2 offset = local - hull[i];
                float cross = edge.Cross(offset);
                positive |= cross > 0;
                negative |= cross < 0;
                float previousCross = edge.Cross(previous - hull[i]);
                previousPositive |= previousCross > 0;
                previousNegative |= previousCross < 0;
                Vector2 closest = hull[i] + edge * Math.Clamp(offset.Dot(edge) / length, 0, 1);
                squared = Math.Min(squared, local.DistanceSquaredTo(closest));
                if (travel.LengthSquared() < 0.000001f) continue;
                float denominator = travel.Cross(edge);
                if (Math.Abs(denominator) > 0.000001f)
                {
                    float t = (hull[i] - previous).Cross(edge) / denominator;
                    float u = (hull[i] - previous).Cross(travel) / denominator;
                    if (t is >= 0 and <= 1 && u is >= 0 and <= 1) return -Player.HitRadius;
                }
                squared = Math.Min(squared, PointSegmentSquared(previous, hull[i], hull[(i + 1) % hull.Length]));
                squared = Math.Min(squared, PointSegmentSquared(hull[i], previous, local));
            }
            if (!positive || !negative || !previousPositive || !previousNegative) return -Player.HitRadius;
            gap = Math.Min(gap, MathF.Sqrt(squared) - Player.HitRadius);
            if (gap < 0.3f) return gap;
        }
        return gap;
    }

    private static float PointSegmentSquared(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float length = segment.LengthSquared();
        if (length < 0.000001f) return point.DistanceSquaredTo(from);
        return point.DistanceSquaredTo(from + segment * Math.Clamp((point - from).Dot(segment) / length, 0, 1));
    }
}
