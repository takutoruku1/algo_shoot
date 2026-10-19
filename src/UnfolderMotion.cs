using Godot;

public enum UnfolderKind { None, Akari, Koharu, Rei, Mina }

public readonly record struct UnfolderPose(Vector2 Position, float Tilt, Vector2 EchoA, Vector2 EchoB,
    float EchoAlpha, Vector2 WarpFrom, Vector2 WarpTo, float WarpCue, float WarpFlash);

public static class UnfolderMotion
{
    public static string TexturePath(UnfolderKind kind)
        => $"res://char/ui/unfolder_{kind.ToString().ToLowerInvariant()}_v1.png";

    public static Color ColorFor(UnfolderKind kind) => new(kind switch
    {
        UnfolderKind.Akari => "f5c77d", UnfolderKind.Koharu => "a3e5ce",
        UnfolderKind.Rei => "d1a5ff", _ => "a0e6ff",
    });

    public static UnfolderPose Sample(UnfolderKind kind, float time, float slot, float radius, int phase)
    {
        switch (kind)
        {
            case UnfolderKind.Akari:
            {
                Vector2 Letter(float t)
                {
                    float angle = slot + t * 0.65f + 0.15f * Mathf.Sin(t * 0.9f + slot);
                    float r = radius + 22f + 6f * Mathf.Sin(t * 1.2f - slot);
                    return Vector2.FromAngle(angle) * new Vector2(r, r * 0.78f);
                }
                var position = Letter(time);
                float tilt = slot + time * 0.65f + Mathf.Pi / 2 + 0.18f * Mathf.Sin(time * 1.6f + slot);
                return new(position, tilt, Letter(time - 0.12f), Letter(time - 0.24f), 0.09f,
                    default, default, 0, 0);
            }
            case UnfolderKind.Koharu:
            {
                float sway = Mathf.Sin(time * 0.7f);
                float angle = slot + sway * (0.65f + 0.16f * Mathf.Sin(slot));
                float split = Mathf.Pow(Mathf.Sin(time * 0.8f), 2);
                float r = radius + 20f + 10f * split + 4f * Mathf.Cos(time * 0.9f + slot);
                var position = Vector2.FromAngle(angle) * new Vector2(r, r * 0.82f);
                var mirrorA = Vector2.FromAngle(angle + split * 0.25f) * new Vector2(r + 5, r * 0.82f);
                var mirrorB = Vector2.FromAngle(angle - split * 0.25f) * new Vector2(r + 5, r * 0.82f);
                return new(position, 0.24f * sway * Mathf.Cos(slot), mirrorA, mirrorB, split * 0.16f,
                    default, default, 0, 0);
            }
            case UnfolderKind.Rei:
            {
                Vector2 Star(float t)
                {
                    float spread = 0.5f + 0.5f * Mathf.Sin(t * 0.85f);
                    var orbit = Vector2.FromAngle(slot - t * 0.82f)
                        * new Vector2(radius + 20f + 14f * spread, radius + 12f + 8f * spread);
                    return orbit.Rotated(0.22f * Mathf.Sin(t * 0.38f));
                }
                return new(Star(time), time * 1.5f - slot, Star(time - 0.12f), Star(time - 0.24f), 0.12f,
                    default, default, 0, 0);
            }
            case UnfolderKind.Mina when phase >= 3:
                return Warp(time, slot, radius + 16f, phase >= 4 ? 4.6f : 5.4f, phase >= 4);
            default:
            {
                float sign = phase >= 1 && Mathf.Cos(slot * 3f) < 0 ? -1 : 1;
                float angle = slot + time * 0.46f * sign;
                float r = radius + 16f + (phase >= 1 ? sign * 9f : 0) + 4f * Mathf.Sin(time * 0.65f);
                var position = Vector2.FromAngle(angle) * new Vector2(r, r * 0.86f);
                var offset = Vector2.FromAngle(angle).Orthogonal() * (9f + 3f * Mathf.Sin(time));
                return new(position, 0.26f * Mathf.Sin(angle), position + offset, position - offset,
                    phase >= 2 ? 0.18f : 0, default, default, 0, 0);
            }
        }
    }

    public static Vector2 OrbitExtent(UnfolderKind kind, float radius) => new Vector2(radius, radius) + (kind switch
    {
        UnfolderKind.Akari => new Vector2(32, 23),
        UnfolderKind.Koharu => new Vector2(40, 27),
        UnfolderKind.Rei => new Vector2(46, 35),
        _ => new Vector2(44, 34),
    });

    private static UnfolderPose Warp(float time, float slot, float radius, float period, bool echoes)
    {
        int cycle = Mathf.FloorToInt(time / period);
        float age = time - cycle * period;
        float sign = Mathf.Cos(slot * 3f) < 0 ? -1 : 1;
        Vector2 At(float t, int turn)
        {
            float r = radius + sign * 4f + 7f * Mathf.Sin(t * 0.65f);
            return Vector2.FromAngle(slot + t * 0.28f * sign + turn * Mathf.Tau / 3f * sign)
                * new Vector2(r, r * 0.86f);
        }
        var position = At(time, cycle);
        var from = At(time, cycle - 1);
        var to = At((cycle + 1) * period, cycle + 1);
        float cue = Mathf.Clamp((age - (period - 0.8f)) / 0.8f, 0, 1);
        float flash = cycle > 0 ? Mathf.Clamp(1 - age / 0.32f, 0, 1) : 0;
        var side = position.Normalized().Orthogonal() * 13f;
        return new(position, Mathf.Sin(time + slot) * 0.26f, position + side, position - side,
            echoes ? 0.16f : 0, from, to, cue, flash);
    }
}
