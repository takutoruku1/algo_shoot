using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public partial class BossTechniqueVideo
{
    private sealed partial class EasyAttackAudit
    {
        private sealed class KoharuSweepSample
        {
            internal string Key = "";
            internal int Mode, Wave, Pair;
            internal double Start, Fire, End, Duration;
            internal float Width, Height;
            internal Vector2 EyeA, EyeB;
            internal Vector2[] Points = Array.Empty<Vector2>();
            internal double[] Pulses = Array.Empty<double>();
            internal bool Fired;
        }

        private sealed class KoharuSweepEntry
        {
            internal KoharuSweepSample Last = null!;
            internal Vector2[]? Forecast;
            internal Vector2 EyeA, EyeB;
            internal double? Full, FirstLive;
            internal int Warnings, Live, HorizontalOnly, VerticalOnly, Dark, InField;
            internal int[] PulseSamples = Array.Empty<int>();
            internal double?[] PulseFirst = Array.Empty<double?>(), PulseLast = Array.Empty<double?>();
            internal float ShapeError, EyeError, BodyError;
        }

        private readonly record struct KoharuShape(Vector2 A, Vector2 B, float Radius, bool Live, int Kind);
        private readonly Dictionary<string, KoharuSweepEntry> _koharuSweeps = new();
        private readonly List<string> _koharuSweepErrors = new();
        private bool _koharuSweepChecked, _koharuSweepEnded;
        private int _koharuSweepMode, _koharuSweepFrames, _koharuSweepMatches, _koharuIgnoredPathWarnings;
        private int _koharuNaturalHazards = -1, _koharuNaturalShots = -1;
        private double _koharuSweepEndTime;

        private static KoharuSweepSample KoharuSweepSnapshot(object value) => new() {
            Key = Read<string>(value, "Key"), Mode = Read<int>(value, "Mode"), Wave = Read<int>(value, "Wave"),
            Pair = Read<int>(value, "Pair"), Start = Read<double>(value, "StartsAt"),
            Fire = Read<double>(value, "FiresAt"), End = Read<double>(value, "EndsAt"),
            Duration = Read<double>(value, "Duration"), Width = Read<float>(value, "HalfWidth"),
            Height = Read<float>(value, "HalfHeight"), EyeA = Read<Vector2>(value, "EyeA"),
            EyeB = Read<Vector2>(value, "EyeB"), Points = Read<Vector2[]>(value, "Points").ToArray(),
            Pulses = Read<double[]>(value, "PulseOffsets").ToArray(), Fired = Read<bool>(value, "Fired"),
        };

        private static KoharuSweepSample[] KoharuSweepSnapshots(BossCharacterTechnique attack)
            => Read<IEnumerable>(attack, "_easySweeps").Cast<object>().Select(KoharuSweepSnapshot).ToArray();

        private void KoharuSweepFail(string reason)
        {
            string message = "Koharu sweep: " + reason;
            _failure ??= message;
            if (_koharuSweepErrors.Count < 32 && !_koharuSweepErrors.Contains(message)) _koharuSweepErrors.Add(message);
        }

        private static Vector2 KoharuSweepPoint(Vector2[] points, double phase)
        {
            float sample = Mathf.Clamp((float)phase, 0, 1) * (points.Length - 1);
            int before = Math.Min((int)sample, points.Length - 2);
            return points[before] + (points[before + 1] - points[before]) * (sample - before);
        }
        private static KoharuShape KoharuBox(Rect2 rect, bool live) => new(rect.Position, rect.End, 0, live, 2);
        private static Rect2 KoharuFocusBox(KoharuSweepSample sample)
            => new(sample.Points[0] - new Vector2(sample.Width, sample.Height), new Vector2(sample.Width, sample.Height) * 2);
        private static object KoharuPointResult(Vector2 point) => new { x = point.X, y = point.Y };
        private static float KoharuShapeError(KoharuShape expected, KoharuShape actual)
            => expected.Kind != actual.Kind || expected.Live != actual.Live ? float.PositiveInfinity
                : Math.Max(Math.Max(expected.A.DistanceTo(actual.A), expected.B.DistanceTo(actual.B)), Math.Abs(expected.Radius - actual.Radius));

        private bool MatchKoharuSweep(KoharuSweepEntry entry, KoharuShape expected, KoharuShape[] actual, bool[] used, double at)
        {
            int found = -1;
            float error = float.PositiveInfinity;
            for (int index = 0; index < actual.Length; index++)
            {
                if (used[index]) continue;
                float candidate = KoharuShapeError(expected, actual[index]);
                if (candidate < error) { found = index; error = candidate; }
            }
            if (found < 0 || error > .015f)
            {
                KoharuSweepFail($"missing/misaligned {(expected.Live ? "live" : "warning")} {entry.Last.Key} at {at:F4}; shape={expected.Kind}, nearest_error={error}");
                return false;
            }
            used[found] = true;
            entry.ShapeError = Math.Max(entry.ShapeError, error);
            _koharuSweepMatches++;
            if (expected.Live)
            {
                entry.Live++;
                Rect2 bounds = expected.Kind == 2 ? new Rect2(expected.A, expected.B - expected.A)
                    : new Rect2(new Vector2(Math.Min(expected.A.X, expected.B.X), Math.Min(expected.A.Y, expected.B.Y)) - Vector2.One * expected.Radius,
                        new Vector2(Math.Abs(expected.A.X - expected.B.X), Math.Abs(expected.A.Y - expected.B.Y)) + Vector2.One * expected.Radius * 2);
                if (bounds.Intersects(Field.Rect)) entry.InField++;
            }
            else entry.Warnings++;
            return true;
        }

        private static IEnumerable<KoharuShape> KoharuSweepWarning(KoharuSweepSample sample, float progress)
        {
            if (sample.Mode == 4) yield return KoharuBox(KoharuFocusBox(sample), false);
            else if (sample.Mode == 2) yield return new(sample.Points[0], sample.Points[0], sample.Width, false, 1);
            else if (sample.Mode == 3) yield return new(sample.Points[0], sample.Points[^1], sample.Width, false, 0);
            else
                for (int index = 1; index < sample.Points.Length; index++)
                {
                    float part = Mathf.Clamp(progress * (sample.Points.Length - 1) - index + 1, 0, 1);
                    if (part > 0) yield return new(sample.Points[index - 1], sample.Points[index - 1].Lerp(sample.Points[index], part),
                        sample.Width + sample.Height, false, 0);
                }
        }

        private void CheckKoharuEyeIntersection(KoharuSweepEntry entry, KoharuSweepSample sample)
        {
            Vector2 center = sample.Points[0];
            float horizontalFar = center.X - Math.Sign(sample.EyeA.X - center.X) * sample.Width;
            float verticalFar = center.Y - Math.Sign(sample.EyeB.Y - center.Y) * sample.Height;
            var horizontal = new Rect2(Math.Min(horizontalFar, sample.EyeA.X), center.Y - sample.Height,
                Math.Abs(horizontalFar - sample.EyeA.X), sample.Height * 2);
            var vertical = new Rect2(center.X - sample.Width, Math.Min(verticalFar, sample.EyeB.Y),
                sample.Width * 2, Math.Abs(verticalFar - sample.EyeB.Y));
            Rect2 intersection = horizontal.Intersection(vertical), box = KoharuFocusBox(sample);
            float error = Math.Max(Math.Max(intersection.Position.DistanceTo(box.Position), intersection.End.DistanceTo(box.End)),
                Math.Max(Math.Abs(sample.EyeA.Y - center.Y), Math.Abs(sample.EyeB.X - center.X)));
            entry.EyeError = Math.Max(entry.EyeError, error);
            if (error > .015f || horizontal.Size.X <= 0 || vertical.Size.Y <= 0)
                KoharuSweepFail("two actual eye bands do not intersect in precisely the promised damage box: " + sample.Key);
        }

        private void ObserveKoharuSweeps(BossCharacterTechnique attack)
        {
            if (attack is not KoharuBossTechnique) return;
            _koharuSweepChecked = true;
            _koharuSweepMode = Read<int>(attack, "_easyMode");
            _koharuSweepFrames++;
            double at = attack.Elapsed;
            var samples = KoharuSweepSnapshots(attack);
            if (samples.Select(sample => sample.Key).Distinct().Count() != samples.Length)
                KoharuSweepFail("duplicate identities in planned physical sweeps");
            var cues = attack.EasyCues.ToDictionary(cue => cue.Key);
            var hazards = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(attack, "_hazards");
            var actual = hazards.Select(hazard => new KoharuShape(hazard.A, hazard.B, hazard.Radius, hazard.Live, hazard.Shape)).ToArray();
            var used = new bool[actual.Length];
            foreach (var sample in samples)
            {
                if (!_koharuSweeps.TryGetValue(sample.Key, out var entry))
                {
                    int pulses = sample.Pulses.Length == 0 ? 1 : sample.Pulses.Length;
                    _koharuSweeps.Add(sample.Key, entry = new KoharuSweepEntry {
                        Last = sample, PulseSamples = new int[pulses], PulseFirst = new double?[pulses], PulseLast = new double?[pulses],
                    });
                }
                else if (entry.Last.Start != sample.Start || entry.Last.Fire != sample.Fire || entry.Last.End != sample.End
                    || entry.Last.Duration != sample.Duration || entry.Last.Mode != sample.Mode || entry.Last.Wave != sample.Wave
                    || entry.Last.Pair != sample.Pair || entry.Last.Width != sample.Width || entry.Last.Height != sample.Height
                    || !entry.Last.Pulses.SequenceEqual(sample.Pulses))
                    KoharuSweepFail("promised timing or dimensions changed: " + sample.Key);
                entry.Last = sample;
                if (at < sample.Start) continue;
                if (sample.Points.Length < 2) { KoharuSweepFail("warning started without prepared physical geometry: " + sample.Key); continue; }
                if (entry.Forecast == null)
                {
                    entry.Forecast = sample.Points.ToArray();
                    entry.EyeA = sample.EyeA;
                    entry.EyeB = sample.EyeB;
                }
                else if (!entry.Forecast.SequenceEqual(sample.Points) || entry.EyeA != sample.EyeA || entry.EyeB != sample.EyeB)
                    KoharuSweepFail("geometry changed after its first warning was exposed: " + sample.Key);
                if (sample.Mode == 4)
                {
                    CheckKoharuEyeIntersection(entry, sample);
                    int tier = Read<int>(attack, "Tier");
                    double lead = tier == 0 ? .42 : (1.2 - tier * .1 - sample.Duration) * .35;
                    double tail = tier == 0 ? .66 : sample.Duration + lead;
                    bool horizontal = sample.Pulses.Any(offset => at >= sample.Fire + offset - lead && at < sample.Fire + offset + sample.Duration);
                    bool vertical = sample.Pulses.Any(offset => at >= sample.Fire + offset && at < sample.Fire + offset + tail);
                    if (at >= sample.Fire - lead && at < sample.End)
                    {
                        if (horizontal && !vertical) entry.HorizontalOnly++;
                        else if (vertical && !horizontal) entry.VerticalOnly++;
                        else if (!horizontal && !vertical) entry.Dark++;
                    }
                }
                float progress = cues[sample.Key].Progress;
                if (at < sample.Fire)
                {
                    var warning = KoharuSweepWarning(sample, progress).ToArray();
                    bool matched = warning.Length > 0;
                    foreach (var shape in warning) matched &= MatchKoharuSweep(entry, shape, actual, used, at);
                    if (matched && progress >= 1) entry.Full ??= at;
                }
                int pulse = sample.Pulses.Length == 0 ? at >= sample.Fire && at < sample.End ? 0 : -1
                    : Array.FindIndex(sample.Pulses, offset => at >= sample.Fire + offset && at < sample.Fire + offset + sample.Duration);
                if (pulse < 0) continue;
                KoharuShape expected;
                if (sample.Mode == 4) expected = KoharuBox(KoharuFocusBox(sample), true);
                else if (sample.Mode == 3) expected = new(sample.Points[0], sample.Points[^1], sample.Width, true, 0);
                else
                {
                    Vector2 point = KoharuSweepPoint(entry.Forecast, (at - sample.Fire) / sample.Duration);
                    expected = new(point, point, sample.Width, true, 1);
                    if (sample.Mode == 1)
                    {
                        float error = point.DistanceTo(_boss.GlobalPosition);
                        entry.BodyError = Math.Max(entry.BodyError, error);
                        if (error > .025f) KoharuSweepFail($"deer body does not occupy its actual footprint at {at:F4}: {error}");
                    }
                }
                if (!sample.Fired || !cues[sample.Key].Live)
                    KoharuSweepFail("live physical shape was due without a Step-backed Fired event: " + sample.Key);
                if (!MatchKoharuSweep(entry, expected, actual, used, at)) continue;
                entry.FirstLive ??= at;
                entry.PulseSamples[pulse]++;
                entry.PulseFirst[pulse] ??= at;
                entry.PulseLast[pulse] = at;
                if (entry.Full == null || at - entry.Full.Value + 1d / 60 + .00001 < 2)
                    KoharuSweepFail("complete drawn footprint was held less than two seconds: " + sample.Key);
            }
            for (int index = 0; index < actual.Length; index++)
                if (!used[index])
                {
                    if (actual[index].Live) KoharuSweepFail($"unpromised live geometry (including any single-eye damage) at {at:F4}: {actual[index]}");
                    else _koharuIgnoredPathWarnings++;
                }
            if (_koharuSweepMode == 5 && samples.FirstOrDefault(sample => sample.Mode == 1) is { } leap
                && at >= leap.End)
            {
                float error = _boss.GlobalPosition.DistanceTo(leap.Points[^1]);
                _koharuSweeps[leap.Key].BodyError = Math.Max(_koharuSweeps[leap.Key].BodyError, error);
                if (error > .025f) KoharuSweepFail("deer moved away from its promised landing before the technique ended");
            }
        }

        private void EndKoharuSweeps(BossCharacterTechnique attack)
        {
            if (attack is not KoharuBossTechnique) return;
            _koharuSweepChecked = _koharuSweepEnded = true;
            _koharuSweepMode = Read<int>(attack, "_easyMode");
            _koharuSweepEndTime = attack.Elapsed;
            _koharuNaturalHazards = attack.NaturalEndLiveHazards;
            _koharuNaturalShots = attack.NaturalEndRemainingShots;
            var samples = KoharuSweepSnapshots(attack);
            int tier = Read<int>(attack, "Tier");
            int gaze = 3 + tier * 2 + (attack.IsEnhanced ? 1 : 0);
            int flashes = 3 + tier + (attack.IsEnhanced ? 1 : 0);
            foreach (var sample in samples)
            {
                if (!_koharuSweeps.TryGetValue(sample.Key, out var entry))
                { KoharuSweepFail("entire planned sweep was omitted: " + sample.Key); continue; }
                if (!sample.Fired || entry.Full == null || entry.FirstLive == null || entry.Warnings == 0 || entry.InField == 0)
                    KoharuSweepFail("missing full warning or physical emission: " + sample.Key);
                for (int pulse = 0; pulse < entry.PulseSamples.Length; pulse++)
                {
                    double fire = sample.Fire + (sample.Pulses.Length == 0 ? 0 : sample.Pulses[pulse]);
                    double end = sample.Pulses.Length == 0 ? sample.End : fire + sample.Duration;
                    if (entry.PulseSamples[pulse] == 0 || entry.PulseFirst[pulse] > fire + 2d / 60 + .001
                        || entry.PulseLast[pulse] < end - 2d / 60 - .001)
                        KoharuSweepFail($"pulse {pulse} was absent, late or truncated: {sample.Key}");
                }
                if (sample.Mode == 4 && (entry.HorizontalOnly == 0 || entry.VerticalOnly == 0 || entry.Dark == 0))
                    KoharuSweepFail("eye pair omitted a harmless single-eye beat or its dark rejoin interval: " + sample.Key);
            }
            if (_koharuNaturalHazards != 0 || _koharuNaturalShots != 0)
                KoharuSweepFail("natural completion still owned live geometry or physical bullets");
            if (_koharuSweepMode == 4 && (samples.Length != 3 * (5 + tier * 4) || samples.Any(sample => sample.Mode != 4)
                || samples.Any(sample => !sample.Pulses.SequenceEqual(Enumerable.Range(0, gaze).Select(pulse => pulse * (1.2 - tier * .1))))
                || !samples.Select(sample => sample.Wave).Distinct().OrderBy(wave => wave).SequenceEqual(new[] { 0, 1, 2 })
                || !samples.GroupBy(sample => sample.Wave).All(wave => wave.Select(sample => sample.Pair).OrderBy(pair => pair).SequenceEqual(Enumerable.Range(0, 5 + tier * 4)))))
                KoharuSweepFail("expected all difficulty-scaled eye pairs and every difficulty-scaled paired beat");
            if (_koharuSweepMode == 6 && (samples.Length != 3 * (4 + tier * 2) || samples.Any(sample => sample.Mode != 3)
                || samples.Any(sample => !sample.Pulses.SequenceEqual(Enumerable.Range(0, flashes).Select(pulse => pulse * Math.Max(.4, 1.68 / (flashes - 1)))))
                || !samples.Select(sample => sample.Wave).Distinct().OrderBy(wave => wave).SequenceEqual(new[] { 0, 1, 2 })
                || !samples.GroupBy(sample => sample.Wave).All(wave => wave.Select(sample => sample.Height).OrderBy(axis => axis).SequenceEqual(Enumerable.Range(0, 4 + tier * 2).Select(axis => (float)axis)))))
                KoharuSweepFail("expected all difficulty-scaled finale axes and every difficulty-scaled physical flash");
            if (_koharuSweepMode == 5)
            {
                var jumps = samples.Where(sample => sample.Mode == 1).ToArray();
                var landings = samples.Where(sample => sample.Mode == 2).ToArray();
                if (samples.Length != 2 || jumps.Length != 1 || landings.Length != 1)
                { KoharuSweepFail("expected one deer jump and its one landing"); return; }
                var leap = jumps[0];
                var landing = landings[0];
                if (Math.Abs(leap.End - landing.Fire) > .00001
                    || leap.Points[^1].DistanceTo(landing.Points[0]) > .015f
                    || Math.Abs(leap.Width - (((BossKoharu)_boss).BodyExtent + 3)) > .015f
                    || Math.Abs(landing.Width - (((BossKoharu)_boss).BodyExtent + 8)) > .015f)
                    KoharuSweepFail("jump footprint and landing do not form one physical cause and consequence");
                var paths = Read<IEnumerable>(attack, "_easyPaths").Cast<object>().ToArray();
                int lights = (tier switch { 0 => 3, 1 => 5, 2 => 7, _ => 10 }) + (attack.IsEnhanced ? 2 : 0);
                if (paths.Length != (12 + tier * 10) * lights || Math.Abs(paths.Min(path => Read<double>(path, "FiresAt")) - landing.Fire - .2) > .00001)
                    KoharuSweepFail("landing omitted or prematurely released its difficulty-scaled physical bloom lights");
            }
            else if (_koharuSweepMode is 1 or 2 or 3 && samples.Length != 0)
                KoharuSweepFail("projectile-only technique unexpectedly created a second attack mechanism");
        }

        private object KoharuSweepResult() => new {
            @checked = _koharuSweepChecked, passed = _koharuSweepChecked && _koharuSweepEnded && _koharuSweepErrors.Count == 0,
            mode = _koharuSweepMode, observed_physics_frames = _koharuSweepFrames, end_observed = _koharuSweepEnded,
            ended_at = _koharuSweepEndTime, matched_shapes = _koharuSweepMatches,
            natural_end_live_hazards = _koharuNaturalHazards, natural_end_owned_bullets = _koharuNaturalShots,
            ignored_nonlive_projectile_forecast_segments = _koharuIgnoredPathWarnings, errors = _koharuSweepErrors,
            scope = "Every physics frame: sweep warning/live shape multiset, actual paired-eye intersection and harmless single-eye/dark phases, every flash, deer body position and landing; projectile trajectories are audited separately. Rendered pixels and human difficulty require separate review.",
            sweeps = _koharuSweeps.Select(pair => new {
                key = pair.Key, mode = pair.Value.Last.Mode, wave = pair.Value.Last.Wave, pair = pair.Value.Last.Pair,
                starts_at = pair.Value.Last.Start, planned_fire_at = pair.Value.Last.Fire, planned_end_at = pair.Value.Last.End,
                complete_geometry_observed_at = pair.Value.Full, physical_geometry_observed_at = pair.Value.FirstLive,
                complete_geometry_hold_seconds = pair.Value.FirstLive - pair.Value.Full,
                warning_shapes_matched = pair.Value.Warnings, live_shapes_matched = pair.Value.Live,
                live_shapes_intersecting_field = pair.Value.InField, horizontal_only_harmless_frames = pair.Value.HorizontalOnly,
                vertical_only_harmless_frames = pair.Value.VerticalOnly, dark_rejoin_frames = pair.Value.Dark,
                maximum_shape_error = pair.Value.ShapeError, maximum_eye_intersection_error = pair.Value.EyeError,
                maximum_deer_body_error = pair.Value.BodyError,
                points = pair.Value.Forecast?.Select(KoharuPointResult), eye_a = KoharuPointResult(pair.Value.EyeA), eye_b = KoharuPointResult(pair.Value.EyeB),
                half_width = pair.Value.Last.Width, half_height = pair.Value.Last.Height,
                pulses = Enumerable.Range(0, pair.Value.PulseSamples.Length).Select(index => new {
                    index, samples = pair.Value.PulseSamples[index], first_live_at = pair.Value.PulseFirst[index], last_live_at = pair.Value.PulseLast[index],
                }),
            }),
        };
    }
}
