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
        float angle = slot + time * 0.55f;
        float r = radius + 8f;
        Vector2 position;
        switch (kind)
        {
            case UnfolderKind.Akari:
                r += 5f * Mathf.Sin(time * 1.1f + slot * 2f);
                position = Vector2.FromAngle(angle) * new Vector2(r, r * 0.88f);
                return new(position, 0.16f * Mathf.Sin(angle), default, default, 0, default, default, 0, 0);
            case UnfolderKind.Koharu:
                angle = slot + time * 0.32f + 0.65f * Mathf.Sin(time * 0.7f);
                r += 4f * Mathf.Cos(time * 0.9f + slot);
                position = Vector2.FromAngle(angle) * new Vector2(r, r * 0.85f);
                float split = Mathf.Pow(Mathf.Sin(time * 0.8f), 2);
                var mirrorA = Vector2.FromAngle(angle + split * 0.48f) * new Vector2(r + 5, r * 0.85f);
                var mirrorB = Vector2.FromAngle(angle - split * 0.48f) * new Vector2(r + 5, r * 0.85f);
                return new(position, 0.12f * Mathf.Sin(angle), mirrorA, mirrorB, split * 0.22f,
                    default, default, 0, 0);
            case UnfolderKind.Rei:
                return Warp(time + 2.2f, slot, r, 4.4f, Mathf.Tau * 0.3f, false, false);
            case UnfolderKind.Mina when phase >= 3:
                return Warp(time, slot, r, phase >= 4 ? 4.6f : 5.4f, Mathf.Tau / 3f, true, phase >= 4);
            default:
                float sign = phase >= 1 && Mathf.Cos(slot * 3f) < 0 ? -1 : 1;
                angle = slot + time * 0.46f * sign;
                r += sign < 0 ? 5f : -3f;
                position = Vector2.FromAngle(angle) * new Vector2(r, r * 0.86f);
                var offset = Vector2.FromAngle(angle).Orthogonal() * (9f + 3f * Mathf.Sin(time));
                return new(position, 0.1f * Mathf.Sin(angle), position + offset, position - offset,
                    phase >= 2 ? 0.18f : 0, default, default, 0, 0);
        }
    }

    private static UnfolderPose Warp(float time, float slot, float radius, float period, float step,
        bool counterRotate, bool echoes)
    {
        int cycle = Mathf.FloorToInt(time / period);
        float age = time - cycle * period;
        float sign = counterRotate && Mathf.Cos(slot * 3f) < 0 ? -1 : 1;
        float drift = counterRotate ? 0.2f * sign : 0;
        float r = radius + (counterRotate ? sign * 4 : 0);
        Vector2 At(float t, int turn) => Vector2.FromAngle(slot + t * drift + turn * step * sign)
            * new Vector2(r, r * 0.86f);
        var position = At(time, cycle);
        var from = At(time, cycle - 1);
        var to = At((cycle + 1) * period, cycle + 1);
        float cue = Mathf.Clamp((age - (period - 0.8f)) / 0.8f, 0, 1);
        float flash = cycle > 0 ? Mathf.Clamp(1 - age / 0.32f, 0, 1) : 0;
        var side = position.Normalized().Orthogonal() * 13f;
        return new(position, Mathf.Sin(time + slot) * 0.1f, position + side, position - side,
            echoes ? 0.16f : 0, from, to, cue, flash);
    }
}
