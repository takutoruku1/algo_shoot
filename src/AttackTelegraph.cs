using Godot;

internal static class AttackTelegraph
{
    internal static readonly Color Projectile = new("f397ac"), Instant = new("efc6b0"),
        Area = new("f397ac"), Sweep = new("f397ac"), Outline = new("171625"), Light = new("fff0e9");
    internal enum Symbol { Record, Undo, Stamp, Pips, Envelope, Received, Inbox, Outbox, Link,
        BrokenLink, Impact, Rest, Footprint, Swap, Target, Locked, Cross, Check, Cut, Stack, Pause, Speed, Boundary }

    internal static Color OrderedInk(Color color, bool next)
    {
        return new Color(color, next ? .90f : .52f);
    }

    internal static bool IsLater(Color color) => color.A <= .53f;

    internal static void ProjectileOrigin(Node2D canvas, Vector2 at, BulletArt.PlayerVisual art, Color ink, float size = 11, float rotation = 0)
    {
        if (BossProjectileArt.For(art) is { } model) art = model.Visual(art.Accent);
        var contours = BulletFootprint.TextureOutlines(art.Cropped);
        Vector2 pivot = (art.Pivot - art.Region.Position) / Mathf.Max(art.Cropped.GetWidth(), art.Cropped.GetHeight());
        var transform = new Transform2D(rotation, at);
        foreach (var contour in contours)
            for (int i = 0; i < contour.Length; i++)
            {
                Vector2 a = transform * ((contour[i] - pivot) * size);
                Vector2 b = transform * ((contour[(i + 1) % contour.Length] - pivot) * size);
                canvas.DrawDashedLine(a, b, new Color(Outline, ink.A * .85f), 1.6f, 1.8f, true);
                canvas.DrawDashedLine(a, b, ink, .65f, 1.8f, true);
            }
    }

    internal static void RoadCharge(Node2D canvas, Vector2 origin, Vector2 mark, Vector2 direction,
        BulletArt.PlayerVisual art, float progress, float fade = 1, float size = 11, float rotation = 0)
    {
        progress = Mathf.Clamp(progress, 0, 1);
        Color ink = new(art.Accent, .48f * fade);
        Vector2 end = mark + direction * (size * .5f + 12);
        canvas.DrawDashedLine(origin, end, new Color(Outline, .46f * fade), 1.25f, 3, true);
        canvas.DrawDashedLine(origin, end, new Color(art.Accent, .28f * fade), .55f, 3, true);
        ProjectileOrigin(canvas, mark, art, ink, size, rotation);
        if (direction == Vector2.Zero) return;
        Arrow(canvas, end, direction, ink);
        // 進路全体は固定し、短い目盛りの長さだけで発射までの進み具合を示す。
        Vector2 start = mark + direction * (size * .5f + 3);
        if (progress > 0)
            canvas.DrawLine(start, start.Lerp(end, progress), new Color(art.Accent, .66f * fade), .7f, true);
    }

    internal static Rect2 Countdown(Node2D canvas, Vector2 origin, float remaining, bool next, Rect2? avoid = null)
    {
        origin = origin.Clamp(Field.Rect.Position + Vector2.One * 4, Field.Rect.End - Vector2.One * 4);
        Color ink = OrderedInk(Light, next);
        Vector2 size = new(14, 14);
        Vector2 position = (origin + new Vector2(6, 5)).Clamp(Field.Rect.Position + Vector2.One * 2, Field.Rect.End - size - Vector2.One * 2);
        var rect = new Rect2(position, size);
        if (avoid is { } other && rect.Intersects(other.Grow(2)))
            rect.Position = new Vector2(position.X, other.End.Y + 3 <= Field.Bottom - size.Y - 2
                ? other.End.Y + 3 : other.Position.Y - size.Y - 3);
        Vector2 join = origin.Clamp(rect.Position, rect.End);
        canvas.DrawLine(origin, join, new Color(ink, ink.A * .5f), .45f, true);
        Vector2 center = rect.GetCenter();
        canvas.DrawCircle(center, 6, new Color(Outline, .72f));
        canvas.DrawArc(center, 5, 0, Mathf.Tau, 32, new Color(ink, .24f), .8f, true);
        canvas.DrawArc(center, 5, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Mathf.Clamp(remaining, .001f, 1),
            32, ink, 1.2f, true);
        if (next) canvas.DrawCircle(center, 1.3f, ink);
        else canvas.DrawArc(center, 1.8f, 0, Mathf.Tau, 12, ink, .65f, true);
        return rect;
    }

    internal static void Origin(Node2D canvas, Vector2 at, Color color, float remaining = 1)
    {
        canvas.DrawCircle(at, 3.3f, new Color(Outline, .8f));
        canvas.DrawCircle(at, 1.1f, Light);
        canvas.DrawArc(at, 2.8f, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Mathf.Clamp(remaining, .001f, 1),
            24, color, .75f, true);
    }

    internal static void MaskStroke(Node2D canvas, Vector2 a, Vector2 b, float radius)
    {
        if (a != b)
        {
            canvas.DrawLine(a, b, Colors.White, radius * 2);
            canvas.DrawCircle(b, radius, Colors.White);
        }
        canvas.DrawCircle(a, radius, Colors.White);
    }

    internal static void MaskOutsideDisc(Node2D canvas, Vector2 center, float radius)
    {
        for (int y = (int)Field.Top; y < Field.Bottom; y++)
        {
            float dy = y + .5f - center.Y;
            float chord = Mathf.Abs(dy) < radius ? Mathf.Sqrt(radius * radius - dy * dy) : 0;
            float left = Mathf.Clamp(center.X - chord, Field.Left, Field.Right);
            float right = Mathf.Clamp(center.X + chord, Field.Left, Field.Right);
            if (left > Field.Left) canvas.DrawRect(new Rect2(Field.Left, y, left - Field.Left, 1), Colors.White);
            if (right < Field.Right) canvas.DrawRect(new Rect2(right, y, Field.Right - right, 1), Colors.White);
        }
    }

    internal static void InstantBand(Node2D canvas, Vector2 from, Vector2 to, float radius, float progress, float remaining = 1, bool next = true)
    {
        Color ink = OrderedInk(Instant, next), light = OrderedInk(Light, next);
        if (progress > 0)
        {
            Vector2 end = from.Lerp(to, progress), direction = from.DirectionTo(to), side = direction.Orthogonal() * radius;
            CorridorInterior(canvas, from, end, radius, ink);
            foreach (float sign in new[] { -1f, 1f })
            {
                canvas.DrawLine(from + side * sign, end + side * sign, new Color(Outline, .5f), 1.6f, true);
                canvas.DrawLine(from + side * sign, end + side * sign, ink, .65f, true);
            }
            canvas.DrawDashedLine(from, end, new Color(light, .3f), .4f, 16, true);
            float angle = direction.Angle();
            foreach (var (at, start) in new[] { (from, angle + Mathf.Pi / 2), (end, angle - Mathf.Pi / 2) })
            {
                canvas.DrawArc(at, radius, start, start + Mathf.Pi, 20, new Color(Outline, .5f), 1.6f, true);
                canvas.DrawArc(at, radius, start, start + Mathf.Pi, 20, ink, .65f, true);
            }
        }
        Origin(canvas, from, ink, remaining);
    }

    internal static void DashedBounds(Node2D canvas, Rect2 rect, Color ink)
    {
        if (!rect.HasArea()) return;
        Vector2 topRight = new(rect.End.X, rect.Position.Y), lowerLeft = new(rect.Position.X, rect.End.Y);
        canvas.DrawDashedLine(rect.Position, topRight, ink, .8f, 3, true);
        canvas.DrawDashedLine(topRight, rect.End, ink, .8f, 3, true);
        canvas.DrawDashedLine(rect.End, lowerLeft, ink, .8f, 3, true);
        canvas.DrawDashedLine(lowerLeft, rect.Position, ink, .8f, 3, true);
    }

    internal static void Box(Node2D canvas, Rect2 rect, Color color, bool hatched = true)
    {
        if (!rect.HasArea()) return;
        if (hatched) canvas.DrawRect(rect, new Color(color, color.A * .10f));
        Vector2[] corners = { rect.Position, new(rect.End.X, rect.Position.Y), rect.End, new(rect.Position.X, rect.End.Y) };
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = corners[i], b = corners[(i + 1) % 4];
            canvas.DrawLine(a, b, new Color(Outline, color.A * .6f), 1.5f, true);
            canvas.DrawLine(a, b, color, .6f, true);
        }
    }

    internal static void Route(Node2D canvas, Vector2 from, Vector2 to, float radius, bool origin, bool directional,
        bool startCap, bool endCap, BulletArt.PlayerVisual? art = null)
    {
        Vector2 direction = from.DirectionTo(to), side = direction.Orthogonal() * radius;
        Color ink = art?.Accent ?? Projectile;
        Color edge = new(ink, .52f);
        CorridorInterior(canvas, from, to, radius, new Color(ink, .55f));
        foreach (float sign in new[] { -1f, 1f })
            Stroke(canvas, new[] { from + side * sign, to + side * sign }, edge, .45f);
        float angle = direction.Angle();
        foreach (var (at, start, draw) in new[] { (from, angle + Mathf.Pi / 2, startCap), (to, angle - Mathf.Pi / 2, endCap) })
        {
            if (!draw) continue;
            canvas.DrawArc(at, radius, start, start + Mathf.Pi, 20, new Color(Outline, .65f), 1.5f, true);
            canvas.DrawArc(at, radius, start, start + Mathf.Pi, 20, edge, .45f, true);
        }
        canvas.DrawDashedLine(from, to, new Color(Outline, .85f), 1.6f, 5, true);
        canvas.DrawDashedLine(from, to, ink, .65f, 5, true);
        if (directional && from.DistanceTo(to) > 24) Arrow(canvas, from.Lerp(to, .35f), direction, ink);
        if (origin)
        {
            if (art == null) Origin(canvas, from, ink);
            else ProjectileOrigin(canvas, from, art, ink);
        }
    }

    internal static void Disc(Node2D canvas, Vector2 center, float radius, Color color, bool hatched = true)
    {
        if (radius <= 0) return;
        if (hatched) canvas.DrawCircle(center, radius, new Color(color, color.A * .10f));
        canvas.DrawArc(center, radius, 0, Mathf.Tau, 48, new Color(Outline, color.A * .6f), 1.5f, true);
        canvas.DrawArc(center, radius, 0, Mathf.Tau, 48, color, .65f, true);
    }

    internal static void Arrow(Node2D canvas, Vector2 at, Vector2 direction, Color color)
    {
        Vector2 side = direction.Orthogonal() * 1.7f;
        Vector2[] points = { at - direction * 2.5f + side, at, at - direction * 2.5f - side };
        canvas.DrawPolyline(points, new Color(Outline, color.A * .7f), 1.7f, true);
        canvas.DrawPolyline(points, color, .7f, true);
    }

    internal static void Pips(Node2D canvas, Vector2 at, int count, Color ink)
    {
        int columns = Mathf.Min(count, 5);
        at = at.Clamp(Field.Rect.Position + new Vector2(12, 8), Field.Rect.End - new Vector2(12, 12));
        for (int i = 0; i < count; i++)
        {
            Vector2 dot = at + new Vector2((i % 5 - (columns - 1) * .5f) * 4, i / 5 * 4);
            canvas.DrawCircle(dot, 1.8f, new Color(Outline, ink.A));
            canvas.DrawCircle(dot, 1.1f, ink);
        }
    }

    internal static void DrawSymbol(Node2D canvas, Vector2 at, Symbol symbol, Color ink,
        float amount = 1, int count = 1, Vector2 direction = default)
    {
        at = at.Clamp(Field.Rect.Position + Vector2.One * 10, Field.Rect.End - Vector2.One * 10);
        if (direction == Vector2.Zero) direction = Vector2.Right;
        void Line(Vector2 a, Vector2 b) => Stroke(canvas, new[] { at + a, at + b }, ink, .85f);
        void Box(Vector2 center, Vector2 half) => Stroke(canvas, new[] { at + center - half,
            at + center + new Vector2(half.X, -half.Y), at + center + half,
            at + center + new Vector2(-half.X, half.Y), at + center - half }, ink, .8f);
        void Arc(float radius, float start, float span)
        {
            canvas.DrawArc(at, radius, start, start + span, 24, new Color(Outline, ink.A), 2, true);
            canvas.DrawArc(at, radius, start, start + span, 24, ink, .85f, true);
        }
        void Check() { Line(new(-4, 0), new(-1, 3)); Line(new(-1, 3), new(5, -4)); }
        switch (symbol)
        {
            case Symbol.Pips: Pips(canvas, at, count, ink); break;
            case Symbol.Record:
                Box(Vector2.Zero, new(7, 5));
                canvas.DrawCircle(at, 2.3f, ink);
                Arc(9, -Mathf.Pi / 2, Mathf.Tau * Mathf.Clamp(amount, .001f, 1));
                break;
            case Symbol.Undo:
                Arc(6, -.5f, 4.1f);
                Vector2 tip = Vector2.FromAngle(3.6f) * 6;
                Line(tip + new Vector2(-1, -4), tip); Line(tip, tip + new Vector2(4, -1));
                break;
            case Symbol.Stamp:
                float lift = (1 - Mathf.Clamp(amount, 0, 1)) * 6;
                Box(new(0, -5 - lift), new(3, 2));
                Line(new(0, -3 - lift), new(0, 1 - lift));
                Line(new(-6, 1 - lift), new(6, 1 - lift));
                if (amount >= 1) { Arc(7, 0, Mathf.Tau); Check(); }
                break;
            case Symbol.Envelope:
            case Symbol.Received:
                Box(Vector2.Zero, new(7, 5));
                Line(new(-7, -5), new(0, symbol == Symbol.Received ? -10 : 1));
                Line(new(0, symbol == Symbol.Received ? -10 : 1), new(7, -5));
                if (symbol == Symbol.Received) Check();
                break;
            case Symbol.Inbox:
            case Symbol.Outbox:
                Line(new(-7, -2), new(-7, 5)); Line(new(-7, 5), new(7, 5)); Line(new(7, 5), new(7, -2));
                Vector2 flow = symbol == Symbol.Inbox ? Vector2.Down : Vector2.Up;
                Line(-flow * 5 + Vector2.Up * 3, flow * 2 + Vector2.Up * 3);
                Arrow(canvas, at + flow * 2 + Vector2.Up * 3, flow, ink);
                break;
            case Symbol.Link:
            case Symbol.BrokenLink:
                Box(new(-5, 0), new(3, 3)); Box(new(5, 0), new(3, 3));
                if (symbol == Symbol.Link) Line(new(-4, 0), new(4, 0));
                else { Line(new(-2, -5), new(2, -2)); Line(new(-2, 2), new(2, 5)); }
                break;
            case Symbol.Impact:
                for (int i = 0; i < 6; i++)
                {
                    Vector2 ray = Vector2.FromAngle(i * Mathf.Tau / 6);
                    Line(ray * 3, ray * 7);
                }
                break;
            case Symbol.Rest:
            case Symbol.Pause:
                Line(new(-3, -5), new(-3, 5)); Line(new(3, -5), new(3, 5));
                break;
            case Symbol.Footprint:
                Box(new(-3, -2), new(1.5f, 3)); Box(new(3, 2), new(1.5f, 3));
                break;
            case Symbol.Swap:
                Line(new(-7, -3), new(7, -3)); Arrow(canvas, at + new Vector2(7, -3), Vector2.Right, ink);
                Line(new(7, 3), new(-7, 3)); Arrow(canvas, at + new Vector2(-7, 3), Vector2.Left, ink);
                break;
            case Symbol.Target:
            case Symbol.Locked:
                float gap = symbol == Symbol.Target ? 3 : 0;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 ray = Vector2.FromAngle(i * Mathf.Pi / 2);
                    Line(ray * (4 + gap), ray * (8 + gap));
                }
                if (symbol == Symbol.Locked) Box(Vector2.Zero, new(4, 4));
                else Arc(4, 0, Mathf.Tau);
                break;
            case Symbol.Cross:
                Line(new(-4, -4), new(4, 4)); Line(new(-4, 4), new(4, -4));
                break;
            case Symbol.Check: Check(); break;
            case Symbol.Cut:
                Line(new(-5, -5), new(5, 5)); Line(new(-5, 5), new(5, -5));
                canvas.DrawArc(at + new Vector2(-6, -6), 2, 0, Mathf.Tau, 12, ink, 1, true);
                canvas.DrawArc(at + new Vector2(-6, 6), 2, 0, Mathf.Tau, 12, ink, 1, true);
                break;
            case Symbol.Stack:
                for (int i = count - 1; i >= 0; i--) Box(new(i * 2 - count + 1, -i * 2), new(4, 5));
                break;
            case Symbol.Speed:
                for (int i = 0; i < count; i++)
                {
                    Vector2 side = direction.Orthogonal() * (i - (count - 1) * .5f) * 3;
                    Line(side - direction * (3 + amount * 5), side + direction * 2);
                }
                break;
            case Symbol.Boundary:
                Line(-direction.Orthogonal() * 6, direction.Orthogonal() * 6);
                break;
        }
    }

    internal static void Stroke(Node2D canvas, Vector2[] points, Color ink, float width = .7f)
    {
        canvas.DrawPolyline(points, new Color(Outline, .8f), width + 1.1f, true);
        canvas.DrawPolyline(points, ink, width, true);
    }

    internal static void CorridorInterior(Node2D canvas, Vector2 from, Vector2 to, float radius, Color color)
    {
        if (from.DistanceSquaredTo(to) < .01f) return;
        canvas.DrawLine(from, to, new Color(Outline, color.A * .10f), radius * 2, true);
        canvas.DrawLine(from, to, new Color(color, color.A * .09f), radius * 2, true);
    }

    internal static void Ring(Node2D canvas, Vector2 center, float radius, float halfWidth, float from, float to, Color color)
    {
        if (radius <= halfWidth) return;
        int segments = Mathf.Max(12, Mathf.CeilToInt((to - from) * radius / 3));
        canvas.DrawArc(center, radius, from, to, segments, new Color(color, color.A * .10f), halfWidth * 2, true);
        foreach (float edge in new[] { radius - halfWidth, radius + halfWidth })
        {
            canvas.DrawArc(center, edge, from, to, segments, new Color(Outline, color.A * .6f), 1.5f, true);
            canvas.DrawArc(center, edge, from, to, segments, color, .65f, true);
        }
    }
}
