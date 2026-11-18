using Godot;
using System;

internal static class ReiRallySound
{
    private const int Rate = 44100;
    private static AudioStreamWav _return = null!, _break = null!, _miss = null!;
    private static readonly AudioStreamWav[] Walls = new AudioStreamWav[4];
    private static bool _prepared;

    internal static void Prepare()
    {
        if (_prepared) return;
        _return = Make(.18, (t, noise) =>
            .65 * Math.Sin(Math.Tau * (260 * t - 250 * t * t)) * Math.Exp(-t * 27)
            + .14 * noise * Math.Exp(-t * 110));
        double[] notes = { 440, 493.88, 554.37, 659.25 };
        for (int i = 0; i < Walls.Length; i++)
        {
            double note = notes[i];
            Walls[i] = Make(.29, (t, noise) =>
                .45 * Math.Sin(Math.Tau * note * t) * Math.Exp(-t * 15)
                + .12 * Math.Sin(Math.Tau * note * 2.76 * t) * Math.Exp(-t * 38)
                + .16 * noise * Math.Exp(-t * 55));
        }
        _break = Make(.76, (t, noise) =>
        {
            double body = .55 * Math.Sin(Math.Tau * (110 * t - 40 * t * t)) * Math.Exp(-t * 18);
            double glass = .18 * noise * Math.Exp(-t * 14)
                * (.5 + .5 * Math.Cos(Math.Tau * 31 * t));
            double chord = .13 * (Math.Sin(Math.Tau * 220 * t)
                + Math.Sin(Math.Tau * 277.185 * t) + Math.Sin(Math.Tau * 329.63 * t)
                + .55 * Math.Sin(Math.Tau * 440 * t))
                * (1 - Math.Exp(-t * 32)) * Math.Exp(-t * 6);
            return body + glass + chord;
        });
        _miss = Make(.2, (t, noise) =>
            .45 * Math.Sin(Math.Tau * (185 * t - 240 * t * t)) * Math.Exp(-t * 23));
        _prepared = true;
    }

    internal static void Return(int streak) => Audio.Instance?.Se(_return, -12, 1 + streak * .035f);
    internal static void Wall(int streak) => Audio.Instance?.Se(Walls[streak - 1], -13);
    internal static void Break() => Audio.Instance?.Se(_break, -10);
    internal static void Miss() => Audio.Instance?.Se(_miss, -17);

    private static AudioStreamWav Make(double duration, Func<double, double, double> sound)
    {
        int count = (int)Math.Round(Rate * duration);
        var samples = new double[count];
        var windows = new double[count];
        var random = new Random(6137);
        double sum = 0, weight = 0, lowNoise = 0;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / Rate;
            double edge = Math.Min(1, Math.Min(t / .004, (count - 1 - i) / (Rate * .024)));
            windows[i] = .5 - .5 * Math.Cos(Math.PI * edge);
            lowNoise += (random.NextDouble() * 2 - 1 - lowNoise) * .22;
            samples[i] = sound(t, lowNoise) * windows[i];
            sum += samples[i];
            weight += windows[i];
        }
        // Windowed DC removal keeps both endpoints silent without a playback click.
        double offset = sum / weight, peak = 0;
        for (int i = 0; i < count; i++)
        {
            samples[i] -= offset * windows[i];
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }
        double gain = Math.Min(1, .52 / peak);
        var bytes = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            short sample = (short)Math.Round(samples[i] * gain * short.MaxValue);
            bytes[i * 2] = (byte)(sample & 255);
            bytes[i * 2 + 1] = (byte)(sample >> 8);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate, Stereo = false, Data = bytes,
        };
    }
}
