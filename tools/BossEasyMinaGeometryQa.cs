using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public partial class BossTechniqueVideo
{
    private sealed partial class EasyAttackAudit
    {
        private sealed class MinaGeometrySample
        {
            public string Key = "";
            public int Mode, Round, Index, Predecessor, Successor, Release, Held, ReceivedFrom;
            public double Start, Fire, End, Arrival;
            public double? CancelledAt, ObservedFire, TransferAt, ExitAt, InitialStart, InitialEnd;
            public Vector2[] Points = Array.Empty<Vector2>();
            public Rect2[] Bounds = Array.Empty<Rect2>();
            public Vector2 Size, Position, Prior, Card, CardReserved;
            public Vector2[] CardPath = Array.Empty<Vector2>();
            public bool CurrentCard, CardVerified, CardInitialOverlap;
            public double CardSeconds;
            public float Radius, Speed, Mass, Momentum;
            public bool Cancelled, Fired, Energized, Transferred, Exited;
        }

        private sealed class MinaGeometryEntry
        {
            public MinaGeometrySample Initial = null!, Last = null!;
            public int Samples, Warnings, Live, InField, FullSamples, StaticSamples;
            public double? Full, InitialFull, Physical, Energized, Transfer, Exit;
            public float PositionError, ShapeError, MomentumError, CardInitialDistance;
            public bool CardBodyClear, CardRouteClear;
            public double CardMeasuredSeconds;
        }

        private readonly record struct MinaShape(Vector2 A, Vector2 B, float Radius, bool Live, int Kind);
        private readonly record struct MinaPhysical(Vector2 At, float Speed, bool Active, bool Exited);
        private readonly Dictionary<string, MinaGeometryEntry> _minaGeometry = new();
        private readonly Dictionary<string, MinaPhysical> _minaPhysical = new();
        private readonly List<string> _minaErrors = new();
        private readonly List<object> _minaTransitions = new();
        private readonly HashSet<string> _minaWarningClipKeys = new();
        private readonly List<object> _minaWarningClips = new();
        private bool _minaChecked, _minaEnded;
        private int _minaVariant = -1, _minaFrames, _minaShapeMatches, _minaLiveMatches, _minaWarningMatches;
        private double _minaPreviousTime = -1, _minaEndTime;
        private int _minaNaturalHazards = -1, _minaNaturalShots = -1;
        private float _minaWarningClipError;

        private static T MinaField<T>(object value, string field)
            => (T)value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;

        private static MinaGeometrySample MinaSnapshot(object value) => new()
        {
            Key = MinaField<string>(value, "Key"), Mode = MinaField<int>(value, "Mode"),
            Round = MinaField<int>(value, "Round"), Index = MinaField<int>(value, "Index"),
            Start = MinaField<double>(value, "StartsAt"), Fire = MinaField<double>(value, "FiresAt"),
            End = MinaField<double>(value, "EndsAt"), Arrival = MinaField<double>(value, "ArrivesAt"),
            Points = MinaField<Vector2[]>(value, "Points").ToArray(), Bounds = MinaField<Rect2[]>(value, "Bounds").ToArray(),
            Size = MinaField<Vector2>(value, "Size"), Position = MinaField<Vector2>(value, "Position"),
            Prior = MinaField<Vector2>(value, "PriorPosition"), Radius = MinaField<float>(value, "Radius"),
            Speed = MinaField<float>(value, "Speed"), Mass = MinaField<float>(value, "Mass"),
            Momentum = MinaField<float>(value, "ReceivedMomentum"), ReceivedFrom = MinaField<int>(value, "ReceivedFrom"),
            Predecessor = MinaField<int>(value, "Predecessor"), Successor = MinaField<int>(value, "Successor"),
            Release = MinaField<int>(value, "ReleaseCue"), Held = MinaField<int>(value, "HeldMemory"),
            Cancelled = MinaField<bool>(value, "Cancelled"), CancelledAt = MinaField<double?>(value, "CancelledAt"),
            Fired = MinaField<bool>(value, "Fired"), Energized = MinaField<bool>(value, "Energized"),
            Transferred = MinaField<bool>(value, "Transferred"), Exited = MinaField<bool>(value, "Exited"),
            ObservedFire = MinaField<double?>(value, "ObservedFireAt"), TransferAt = MinaField<double?>(value, "TransferredAt"),
            ExitAt = MinaField<double?>(value, "ObservedExitAt"), InitialStart = MinaField<double?>(value, "InitialStartsAt"),
            InitialEnd = MinaField<double?>(value, "InitialEndsAt"),
            Card = MinaField<Vector2>(value, "Card"), CardReserved = MinaField<Vector2>(value, "CardReserved"),
            CardPath = MinaField<Vector2[]>(value, "CardAccessPath").ToArray(),
            CurrentCard = MinaField<bool>(value, "CurrentRowCard"), CardVerified = MinaField<bool>(value, "CardAccessVerified"),
            CardInitialOverlap = MinaField<bool>(value, "CardStartBodyOverlap"), CardSeconds = MinaField<double>(value, "CardTravelSeconds"),
        };

        private static MinaGeometrySample[] MinaSnapshots(BossCharacterTechnique attack)
            => Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>().Select(MinaSnapshot).ToArray();

        private void MinaFail(string reason)
        {
            string message = "Mina geometry: " + reason;
            _failure ??= message;
            if (_minaErrors.Count < 32 && !_minaErrors.Contains(message)) _minaErrors.Add(message);
        }

        private static object MinaPoint(Vector2 point) => new { x = point.X, y = point.Y };
        private static object MinaRect(Rect2 rect) => new { x = rect.Position.X, y = rect.Position.Y, width = rect.Size.X, height = rect.Size.Y };
        private static float MinaProgress(double at, double start) => Mathf.Clamp((float)((at - start - .4) / .8), 0, 1);

        private void MinaCheckVisibleWarning(BossCharacterTechnique attack, MinaGeometrySample cue)
        {
            var method = attack.GetType().GetMethod("EasyMemoryVisibleSegment", BindingFlags.Static | BindingFlags.NonPublic)!;
            for (int segment = 1; segment < cue.Points.Length; segment++)
            {
                Vector2 from = cue.Points[segment - 1], to = cue.Points[segment];
                Vector2 side = new(-(to - from).Normalized().Y, (to - from).Normalized().X);
                foreach (int offset in new[] { 0, 1, -1 })
                {
                    string key = cue.Key + ":" + segment + ":" + offset;
                    if (!_minaWarningClipKeys.Add(key)) continue;
                    Vector2 a = from + side * cue.Radius * offset, b = to + side * cue.Radius * offset, delta = b - a;
                    var hits = new List<float>();
                    void Candidate(float t)
                    {
                        Vector2 point = a + delta * t;
                        if (t >= 0 && t <= 1 && point.X >= Field.Left - .0001f && point.X <= Field.Right + .0001f
                            && point.Y >= Field.Top - .0001f && point.Y <= Field.Bottom + .0001f) hits.Add(t);
                    }
                    Candidate(0); Candidate(1);
                    if (delta.X != 0) { Candidate((Field.Left - a.X) / delta.X); Candidate((Field.Right - a.X) / delta.X); }
                    if (delta.Y != 0) { Candidate((Field.Top - a.Y) / delta.Y); Candidate((Field.Bottom - a.Y) / delta.Y); }
                    object? drawn = method.Invoke(null, new object[] { a, b });
                    float error = 0;
                    if (hits.Count == 0 ? drawn != null : drawn == null) error = float.MaxValue;
                    else if (drawn is ValueTuple<Vector2, Vector2> visible)
                        error = Math.Max(visible.Item1.DistanceTo(a + delta * hits.Min()), visible.Item2.DistanceTo(a + delta * hits.Max()));
                    _minaWarningClipError = Math.Max(_minaWarningClipError, error);
                    if (error > .015f) MinaFail("drawn warning does not meet its actual field intersection: " + key);
                    _minaWarningClips.Add(new { key, original_start = MinaPoint(a), original_end = MinaPoint(b),
                        visible_start = drawn is ValueTuple<Vector2, Vector2> s ? MinaPoint(s.Item1) : null,
                        visible_end = drawn is ValueTuple<Vector2, Vector2> e ? MinaPoint(e.Item2) : null,
                        boundary_error = error });
                }
            }
        }

        private void MinaCheckDraftCard(BossCharacterTechnique attack, MinaGeometrySample cue, MinaGeometryEntry entry)
        {
            var boss = Read<Enemy>(attack, "Boss");
            using var probe = new CircleShape2D { Radius = Player.HitRadius };
            bool Hit(Vector2 point)
            {
                if (boss.CollisionLayer == 0 || !boss.Monitorable) return false;
                foreach (uint owner in boss.GetShapeOwners())
                {
                    if (boss.IsShapeOwnerDisabled(owner)) continue;
                    Transform2D transform = boss.GlobalTransform * boss.ShapeOwnerGetTransform(owner);
                    for (int shape = 0; shape < boss.ShapeOwnerGetShapeCount(owner); shape++)
                        if (boss.ShapeOwnerGetShape(owner, shape).Collide(transform, probe, new Transform2D(0, point))) return true;
                }
                return false;
            }
            entry.CardBodyClear = !Hit(cue.Card);
            entry.CardInitialDistance = cue.Card.DistanceTo(cue.CardReserved);
            var row = cue.Bounds[0];
            if (!entry.CardBodyClear || entry.CardInitialDistance <= 16 || cue.Card.Y < row.Position.Y + 8
                || cue.Card.Y > row.End.Y - 8 || cue.Card.X < Field.Left + 6 || cue.Card.X > Field.Right - 6)
                MinaFail("draft card is body-blocked, initially readable or outside its row: " + cue.Key);
            if (!cue.CurrentCard) return;
            if (cue.CardInitialOverlap != Hit(cue.CardReserved)) MinaFail("draft-card start/body diagnostic disagrees: " + cue.Key);
            if (cue.CardInitialOverlap) return;
            if (!cue.CardVerified || cue.CardPath.Length < 2 || cue.CardPath[0].DistanceTo(cue.CardReserved) > .01f
                || cue.CardPath[^1].DistanceTo(cue.Card) > 13.01f)
            { MinaFail("current-row acknowledgement has no reachable touch path: " + cue.Key); return; }
            entry.CardRouteClear = true;
            float distance = 0;
            for (int segment = 1; segment < cue.CardPath.Length; segment++)
            {
                Vector2 from = cue.CardPath[segment - 1], to = cue.CardPath[segment];
                float length = from.DistanceTo(to);
                distance += length;
                int steps = Math.Max(1, (int)Math.Ceiling(length / .5f));
                for (int sample = 0; sample <= steps; sample++)
                {
                    Vector2 point = from.Lerp(to, sample / (float)steps);
                    if (point.X < Field.Left || point.X > Field.Right || point.Y < Field.Top || point.Y > Field.Bottom || Hit(point))
                        entry.CardRouteClear = false;
                }
            }
            entry.CardMeasuredSeconds = distance / Read<Player>(attack, "Player").SlowestMoveSpeed;
            if (!entry.CardRouteClear || entry.CardMeasuredSeconds > 2 || Math.Abs(entry.CardMeasuredSeconds - cue.CardSeconds) > .0001)
                MinaFail("draft-card access crosses the body/boundary or exceeds the complete-warning window: " + cue.Key);
        }

        private static bool MinaSegmentEnded(MinaGeometrySample cue, MinaGeometrySample[] cues, double at)
            => cue.Mode == 4 ? cue.Exited || cue.Release >= 0 && cues[cue.Release].Energized
                : cue.Mode == 7 ? cue.Exited : at >= cue.End;

        private static IEnumerable<Rect2> MinaPressure(Rect2 room)
        {
            room = room.Intersection(Field.Rect);
            if (room.Position.Y > Field.Top) yield return new Rect2(Field.Left, Field.Top, Field.Width, room.Position.Y - Field.Top);
            if (room.End.Y < Field.Bottom) yield return new Rect2(Field.Left, room.End.Y, Field.Width, Field.Bottom - room.End.Y);
            if (room.Position.X > Field.Left) yield return new Rect2(Field.Left, room.Position.Y, room.Position.X - Field.Left, room.Size.Y);
            if (room.End.X < Field.Right) yield return new Rect2(room.End.X, room.Position.Y, Field.Right - room.End.X, room.Size.Y);
        }

        private static (Rect2 Entry, Rect2 Exit, double Open, double Close)[] MinaDoors(MinaGeometrySample room)
        {
            var result = new List<(Rect2, Rect2, double, double)>();
            double elapsed = 0;
            for (int i = 1; i < room.Points.Length; i++)
            {
                double move = Math.Max(2.4, room.Points[i - 1].DistanceTo(room.Points[i]) / room.Speed);
                result.Add((new Rect2(room.Points[i - 1] - room.Size * .5f, room.Size),
                    new Rect2(room.Points[i] - room.Size * .5f, room.Size), room.Fire + elapsed + .4, room.Fire + elapsed + .4 + move));
                elapsed += move + .8;
            }
            return result.ToArray();
        }

        private static Rect2 MinaProtectedRoom(MinaGeometrySample cue, double at)
        {
            var doors = MinaDoors(cue);
            foreach (var door in doors)
            {
                if (at < door.Open) return door.Entry;
                if (at < door.Close) return door.Entry.Merge(door.Exit);
                if (at < door.Close + .4) return door.Exit;
            }
            return doors[^1].Exit;
        }

        private static MinaShape MinaBox(Rect2 rect, bool live) => new(rect.Position, rect.End, 0, live, 2);
        private static float MinaShapeError(MinaShape a, MinaShape b)
            => a.Kind != b.Kind || a.Live != b.Live ? float.PositiveInfinity
                : Math.Max(Math.Max(a.A.DistanceTo(b.A), a.B.DistanceTo(b.B)), Math.Abs(a.Radius - b.Radius));

        private static IEnumerable<MinaShape> MinaPath(Vector2[] points, float radius, float progress, bool live)
        {
            float length = 0;
            for (int i = 1; i < points.Length; i++) length += points[i - 1].DistanceTo(points[i]);
            float remaining = length * progress;
            for (int i = 1; i < points.Length && remaining > 0; i++)
            {
                float span = points[i - 1].DistanceTo(points[i]);
                yield return new MinaShape(points[i - 1], points[i - 1].MoveToward(points[i], Math.Min(remaining, span)), radius, live, 0);
                remaining -= span;
            }
        }

        private static Rect2 MinaPage(MinaGeometrySample cue, double at)
        {
            float age = (float)(at - cue.Fire);
            float progress = Mathf.Clamp((age - .45f) / 1.1f, 0, 1);
            progress = progress * progress * (3 - 2 * progress);
            Vector2 size = new(28 + (cue.Size.X - 28) * progress, 8 + (cue.Size.Y - 8) * progress);
            Vector2 center = cue.Points[0].MoveToward(cue.Points[1], age * cue.Speed);
            return new Rect2(center - size * .5f, size);
        }

        private static bool MinaInField(MinaShape shape)
            => shape.Kind == 2 ? new Rect2(shape.A, shape.B - shape.A).Intersects(Field.Rect)
                : new Rect2(new Vector2(Math.Min(shape.A.X, shape.B.X), Math.Min(shape.A.Y, shape.B.Y)) - Vector2.One * shape.Radius,
                    new Vector2(Math.Abs(shape.A.X - shape.B.X), Math.Abs(shape.A.Y - shape.B.Y)) + Vector2.One * shape.Radius * 2).Intersects(Field.Rect);

        private bool MinaMatch(MinaGeometryEntry entry, MinaShape expected, MinaShape[] actual, bool[] used, double at)
        {
            int best = -1;
            float error = float.PositiveInfinity;
            for (int i = 0; i < actual.Length; i++)
            {
                if (used[i]) continue;
                float candidate = MinaShapeError(expected, actual[i]);
                if (candidate < error) { best = i; error = candidate; }
            }
            if (best < 0 || error > .015f)
            {
                MinaFail($"missing/misaligned {(expected.Live ? "live" : "warning")} shape {entry.Last.Key} at {at:F4}; kind={expected.Kind}, A={expected.A}, B={expected.B}, radius={expected.Radius}, nearest_error={error}");
                return false;
            }
            used[best] = true;
            entry.ShapeError = Math.Max(entry.ShapeError, error);
            _minaShapeMatches++;
            if (expected.Live) { _minaLiveMatches++; entry.Live++; if (MinaInField(expected)) entry.InField++; }
            else { _minaWarningMatches++; entry.Warnings++; }
            return true;
        }

        private void MinaCheckRelayState(MinaGeometrySample cue, MinaGeometrySample[] cues, MinaGeometryEntry entry, double at, float pull)
        {
            if (cue.Mode is not (4 or 7)) return;
            if (cue.Energized && at + .00001 < cue.Fire) MinaFail("memory moved before its promised fire: " + cue.Key);
            if (!cue.Energized && at > cue.Fire + 2d / 60) MinaFail("planned memory never received motion: " + cue.Key);
            if (!cue.Energized && cue.Position.DistanceTo(cue.Points[0]) > .015f) MinaFail("resting receiver moved before contact: " + cue.Key);
            bool handedToRelease = cue.Mode == 4 && cue.Release >= 0 && cues[cue.Release].Energized;
            Vector2 expected = cue.Energized
                ? cue.Points[0].MoveToward(cue.Points[1], (float)Math.Max(0, at - cue.Fire) * cue.Speed) : cue.Points[0];
            if (cue.Transferred && !handedToRelease) expected = cue.Points[1];
            if (handedToRelease) expected = cues[cue.Release].Position;
            float positionError = cue.Position.DistanceTo(expected);
            entry.PositionError = Math.Max(entry.PositionError, positionError);
            if (positionError > .025f) MinaFail($"memory position differs from its fixed motion/hold: {cue.Key} error={positionError}");
            if (cue.Mode == 4 && cue.Predecessor >= 0 && cue.Energized)
            {
                var giver = cues[cue.Predecessor];
                float momentum = giver.Speed * giver.Mass;
                float speed = Mathf.Clamp(momentum / cue.Mass + pull * .08f, 48 + (int)GameManager.Instance!.Difficulty * 5, 88 + (int)GameManager.Instance!.Difficulty * 7);
                float error = Math.Max(Math.Abs(cue.Momentum - momentum), Math.Abs(cue.Speed - speed));
                entry.MomentumError = Math.Max(entry.MomentumError, error);
                if (!giver.Transferred || cue.ReceivedFrom != cue.Predecessor || error > .015f)
                    MinaFail("receiver did not inherit the contacting giver's mass/momentum: " + cue.Key);
            }
            if (cue.Transferred)
            {
                if (cue.Successor < 0 || !cues[cue.Successor].Energized || cue.TransferAt == null
                    || cue.TransferAt + .00001 < cue.Arrival + .32)
                    MinaFail("transfer did not wait for physical contact and compression: " + cue.Key);
                else
                {
                    var receiver = cues[cue.Successor];
                    float contact = Math.Abs(cue.Points[1].DistanceTo(receiver.Points[0]) - cue.Radius - receiver.Radius);
                    if (contact > .015f) MinaFail($"sender does not physically touch its receiver: {cue.Key} error={contact}");
                }
                if (!handedToRelease && cue.Energized && !cue.Exited) entry.StaticSamples++;
            }
            if (cue.Mode == 7 && cue.Energized)
            {
                var held = cues[cue.Held];
                bool drained = cues.Where(c => c.Mode == 4 && c.Successor < 0).All(c => c.Exited && c.ExitAt <= cue.ObservedFire);
                if (!drained || !held.Transferred || held.Release < 0 || !ReferenceEquals(cues[held.Release], cue)
                    || cue.Points[0].DistanceTo(held.Points[1]) > .015f || Math.Abs(cue.Radius - held.Radius) > .001f)
                    MinaFail("recoil was not the same held body released after all terminal exits: " + cue.Key);
            }
            if (cue.Exited && (Field.Rect.Grow(cue.Radius).HasPoint(cue.Position) || cue.ExitAt == null))
                MinaFail("memory vanished while its physical circle was still in the field: " + cue.Key);
        }

        private void MinaObserveStates(BossCharacterTechnique attack, MinaGeometrySample[] cues, bool ended)
        {
            double at = attack.Elapsed;
            float pull = Read<Vector2>(attack, "_easyConnectionPull").Length();
            for (int i = 0; i < cues.Length; i++)
            {
                var cue = cues[i];
                if (!_minaGeometry.TryGetValue(cue.Key, out var entry))
                    _minaGeometry.Add(cue.Key, entry = new MinaGeometryEntry { Initial = cue, Last = cue });
                else if (!entry.Initial.Points.SequenceEqual(cue.Points) || !entry.Initial.Bounds.SequenceEqual(cue.Bounds)
                    || Math.Abs(entry.Initial.Radius - cue.Radius) > .001f)
                    MinaFail("geometry changed after its warning was exposed: " + cue.Key);
                entry.Last = cue;
                if (entry.Samples == 0 && cue.Mode == 1) MinaCheckDraftCard(attack, cue, entry);
                entry.Samples++;
                if (cue.Cancelled && (cue.Fired || cue.CancelledAt == null || cue.CancelledAt >= cue.Fire))
                    MinaFail("acknowledgement cancelled an active or unrecorded attack: " + cue.Key);
                if (cue.Fired && (cue.ObservedFire == null || cue.ObservedFire + .00001 < cue.Fire))
                    MinaFail("Fired is not backed by a real Step-time emission: " + cue.Key);
                foreach (var state in new[] { ("energized", cue.Energized), ("transferred", cue.Transferred), ("exited", cue.Exited) })
                {
                    double? previous = state.Item1 == "energized" ? entry.Energized : state.Item1 == "transferred" ? entry.Transfer : entry.Exit;
                    if (!state.Item2 || previous != null) continue;
                    if (state.Item1 == "energized") entry.Energized = at;
                    else if (state.Item1 == "transferred") entry.Transfer = at;
                    else entry.Exit = at;
                    _minaTransitions.Add(new { key = cue.Key, state = state.Item1, at, position = MinaPoint(cue.Position),
                        received_from = cue.ReceivedFrom, momentum = cue.Momentum, speed = cue.Speed });
                }
                MinaCheckRelayState(cue, cues, entry, at, pull);
                if (cue.Mode != 4) continue;
                var physical = cue.Release >= 0 && cues[cue.Release].Energized ? cues[cue.Release] : cue;
                bool active = physical.Energized && !physical.Exited;
                if (_minaPhysical.TryGetValue(cue.Key, out var prior))
                {
                    double delta = _minaPreviousTime < 0 ? 0 : at - _minaPreviousTime;
                    float distance = prior.At.DistanceTo(physical.Position);
                    if (prior.Active && delta > 0 && distance > Math.Max(prior.Speed, physical.Speed) * delta + .05)
                        MinaFail($"physical memory teleported during handoff/exit: {cue.Key} distance={distance} dt={delta}");
                    if (prior.Active && !active && !physical.Exited)
                        MinaFail("active held memory disappeared without an exit: " + cue.Key);
                }
                _minaPhysical[cue.Key] = new MinaPhysical(physical.Position, physical.Speed, active, physical.Exited);
                if (ended && !physical.Exited) MinaFail("physical memory remained at natural finish: " + cue.Key);
            }
        }

        private void ObserveMinaGeometry(BossCharacterTechnique attack)
        {
            if (attack is not MinaBossTechnique) return;
            _minaChecked = true;
            _minaVariant = attack.TechniqueVariant;
            _minaFrames++;
            double at = attack.Elapsed;
            var cues = MinaSnapshots(attack);
            if (cues.Select(cue => cue.Key).Distinct().Count() != cues.Length) MinaFail("two physical cues reuse one forecast identity");
            MinaObserveStates(attack, cues, false);
            var hazards = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(attack, "_hazards");
            var actual = hazards.Select(h => new MinaShape(h.A, h.B, h.Radius, h.Live, h.Shape)).ToArray();
            var used = new bool[actual.Length];
            foreach (var cue in cues)
            {
                var entry = _minaGeometry[cue.Key];
                bool initial = cue.Mode == 7 && at < cue.Start && cue.InitialStart is double first
                    && cue.InitialEnd is double last && at >= first && at < last;
                if (!initial && (cue.Cancelled || at < cue.Start || MinaSegmentEnded(cue, cues, at))) continue;
                bool warning = initial || (cue.Mode is 4 or 7 ? !cue.Energized : at < cue.Fire);
                float progress = initial ? MinaProgress(at, cue.InitialStart!.Value) : MinaProgress(at, cue.Start);
                var expected = new List<MinaShape>();
                if (warning)
                {
                    if (progress <= 0) continue;
                    if (cue.Mode is 4 or 7) expected.AddRange(MinaPath(cue.Points, cue.Radius, progress, false));
                    else expected.AddRange(cue.Bounds.Select(b => MinaBox(new Rect2(b.Position,
                        new Vector2(b.Size.X * progress, b.Size.Y)), false)));
                }
                else
                {
                    if (!cue.Fired) MinaFail("actual geometry was due but Fired was not set by Step: " + cue.Key);
                    switch (cue.Mode)
                    {
                        case 0:
                            var page = MinaPage(cue, at);
                            expected.Add(MinaBox(page, true));
                            var clipped = page.Intersection(Field.Rect);
                            if (clipped.Size.X > 0 && clipped.Size.Y > 0 && !cue.Bounds.Any(b => b.Grow(.015f).Encloses(clipped)))
                                MinaFail("unfolded page exceeds its complete swept warning: " + cue.Key);
                            break;
                        case 1:
                        case 3:
                        case 5:
                            expected.AddRange(cue.Bounds.Select(b => MinaBox(b, true)));
                            break;
                        case 2:
                            expected.AddRange(MinaPressure(MinaProtectedRoom(cue, at)).Select(b => MinaBox(b, true)));
                            break;
                        case 4:
                        case 7:
                            expected.Add(new MinaShape(cue.Prior, cue.Position, cue.Radius, true, 0));
                            float error = Math.Max(cue.Prior.DistanceTo(Geometry2D.GetClosestPointToSegment(cue.Prior, cue.Points[0], cue.Points[1])),
                                cue.Position.DistanceTo(Geometry2D.GetClosestPointToSegment(cue.Position, cue.Points[0], cue.Points[1])));
                            if (error > .025f) MinaFail("live memory left its fully warned centerline: " + cue.Key);
                            break;
                    }
                }
                bool matched = true;
                foreach (var shape in expected) matched &= MinaMatch(entry, shape, actual, used, at);
                if (matched && warning && progress == 1 && expected.Count > 0)
                {
                    if ((cue.Mode is 4 or 7) && entry.FullSamples == 0) MinaCheckVisibleWarning(attack, cue);
                    entry.FullSamples++;
                    if (initial) entry.InitialFull ??= at;
                    else entry.Full ??= at;
                }
                if (matched && !warning && expected.Count > 0 && entry.Physical == null)
                {
                    entry.Physical = at;
                    if (entry.Full == null || at - entry.Full.Value + 1d / 60 + .00001 < 2)
                        MinaFail("actual complete geometry was not held for two seconds: " + cue.Key);
                }
                if (cue.Mode == 5 && !warning)
                {
                    var room = cues.Single(c => c.Mode == 2);
                    var pressure = MinaPressure(MinaProtectedRoom(room, at)).ToArray();
                    foreach (var bound in cue.Bounds)
                        if (!pressure.Any(rect => rect.Grow(.015f).Encloses(bound)))
                            MinaFail("rear-door live rectangle disagrees with the physical room closure: " + cue.Key);
                }
            }
            if (used.Any(value => !value)) MinaFail($"actual hazards contain unpromised geometry at {at:F4}: unmatched={used.Count(value => !value)}");
            _minaPreviousTime = at;
        }

        private void EndMinaGeometry(BossCharacterTechnique attack)
        {
            if (attack is not MinaBossTechnique) return;
            _minaChecked = true;
            _minaEnded = true;
            _minaVariant = attack.TechniqueVariant;
            _minaEndTime = attack.Elapsed;
            _minaNaturalHazards = attack.NaturalEndLiveHazards;
            _minaNaturalShots = attack.NaturalEndRemainingShots;
            var cues = MinaSnapshots(attack);
            MinaObserveStates(attack, cues, true);
            foreach (var cue in cues.Where(cue => !cue.Cancelled))
            {
                var entry = _minaGeometry[cue.Key];
                if (!cue.Fired || entry.Physical == null || entry.Full == null || entry.Live == 0 || entry.Warnings == 0 || entry.InField == 0)
                    MinaFail("planned cue lacks observed full warning and physical emission: " + cue.Key);
                if (cue.Mode == 7 && entry.InitialFull == null)
                    MinaFail("recoil exit was absent from the initial complete route plan: " + cue.Key);
            }
            if (_minaNaturalHazards != 0 || _minaNaturalShots != 0) MinaFail("natural completion still owned live geometry or bullets");
            int tier = Read<int>(attack, "Tier");
            int loads = (tier switch { 0 => 4, 1 => 6, 2 => 9, _ => 12 }) + (attack.IsEnhanced ? 2 : 0);
            int chains = 3 + tier * 3 + (attack.IsEnhanced ? 1 : 0);
            int[] required = _minaVariant switch { 0 => new[] { 3 * (3 + tier * 2) * loads }, 1 => new[] { (4 + tier * 2) * (3 + tier) }, 4 => new[] { chains * 4, chains * 3 }, _ => Array.Empty<int>() };
            if (_minaVariant == 0 && (cues.Length != required[0] || cues.Any(cue => cue.Mode != 0)
                || !cues.GroupBy(cue => (cue.Round, cue.Index)).All(group => group.Count() == loads)))
                MinaFail("missing difficulty-scaled burden pages from the nine independently warned trains");
            if (_minaVariant == 1 && (cues.Length != required[0] || cues.Any(cue => cue.Mode != 1)
                || cues.Count(cue => cue.Cancelled) < 3 + tier || cues.Count(cue => cue.Cancelled) > 2 * (3 + tier)
                || !cues.GroupBy(cue => cue.Round).All(group => group.Select(cue => cue.Index).OrderBy(index => index).SequenceEqual(Enumerable.Range(0, 4 + tier * 2)))))
                MinaFail("missing difficulty-scaled draft rounds or their pre-fire acknowledgements");
            if (_minaVariant == 2)
            {
                var room = cues.Single(cue => cue.Mode == 2);
                if (cues.Any(cue => cue.Mode is not (2 or 5))) MinaFail("airlock contains an unrelated attack mode");
                var doors = MinaDoors(room);
                var closures = cues.Where(cue => cue.Mode == 5).OrderBy(cue => cue.Index).ToArray();
                if (doors.Length < 4 || closures.Length != doors.Length) MinaFail("airlock omitted a rear-door closure");
                for (int i = 0; i < Math.Min(doors.Length, closures.Length); i++)
                {
                    var closing = closures[i];
                    var door = doors[i];
                    var expected = MinaPressure(door.Exit).Select(rect => rect.Intersection(door.Entry.Merge(door.Exit)))
                        .Where(rect => rect.Size.X > 0 && rect.Size.Y > 0).ToArray();
                    if (closing.Index != i || Math.Abs(closing.Fire - door.Close) > .00001
                        || Math.Abs(closing.Start + 3.2 - closing.Fire) > .00001
                        || expected.Length != closing.Bounds.Length || expected.Any(rect => !closing.Bounds.Contains(rect)))
                        MinaFail("rear-door promise differs from its physical closure geometry/time: " + closing.Key);
                }
            }
            if (_minaVariant == 3 && (cues.Any(cue => cue.Mode != 3)
                || !cues.Select(cue => cue.Round).Distinct().OrderBy(round => round).SequenceEqual(Enumerable.Range(0, 4 + tier))))
                MinaFail("self-propagating records omitted a generation");
            if (_minaVariant == 4)
            {
                var memories = cues.Where(cue => cue.Mode == 4).ToArray();
                var releases = cues.Where(cue => cue.Mode == 7).ToArray();
                if (_minaWarningClips.Count != cues.Sum(cue => (cue.Points.Length - 1) * 3))
                    MinaFail("handoff/recoil warnings omitted a drawn centerline or width-boundary intersection");
                if (cues.Any(cue => cue.Mode is not (4 or 7))) MinaFail("homeward relay contains an unrelated attack mode");
                if (memories.Length != required[0] || releases.Length != required[1]
                    || memories.Count(cue => cue.Transferred) != chains * 3 || memories.Count(cue => cue.ReceivedFrom >= 0) != chains * 3
                    || memories.Count(cue => cue.Successor < 0) != chains || cues.Any(cue => !cue.Fired || !cue.Exited))
                    MinaFail("relay omitted difficulty-scaled memories, handoffs or natural recoil exits");
                if (!memories.Select(cue => cue.Round).Distinct().OrderBy(round => round).SequenceEqual(Enumerable.Range(0, chains))
                    || releases.Select(cue => cue.Held).Distinct().Count() != chains * 3)
                    MinaFail("relay omitted a connection chain or reused one held body for multiple exits");
                foreach (var chain in memories.GroupBy(cue => cue.Round))
                {
                    var nodes = chain.OrderBy(cue => cue.Index).ToArray();
                    if (!nodes.Select(cue => cue.Index).SequenceEqual(new[] { 0, 1, 2, 3 }))
                    { MinaFail("relay chain does not contain four distinct consecutive receivers"); continue; }
                    for (int node = 0; node < nodes.Length; node++)
                        if (nodes[node].Predecessor != (node == 0 ? -1 : Array.IndexOf(cues, nodes[node - 1]))
                            || nodes[node].Successor != (node == 3 ? -1 : Array.IndexOf(cues, nodes[node + 1])))
                            MinaFail("relay transfer links do not connect the actual four memories: " + nodes[node].Key);
                }
                foreach (var cue in memories.Where(cue => cue.Successor >= 0))
                    if (_minaGeometry[cue.Key].StaticSamples == 0)
                        MinaFail("transferred sender never remained as an observed dangerous obstacle: " + cue.Key);
            }
            _minaPreviousTime = attack.Elapsed;
        }

        private object MinaGeometryResult() => new
        {
            @checked = _minaChecked, passed = _minaChecked && _minaEnded && _minaErrors.Count == 0,
            phase = _minaVariant, observed_physics_frames = _minaFrames, end_observed = _minaEnded, ended_at = _minaEndTime,
            matched_shapes = _minaShapeMatches, matched_warning_shapes = _minaWarningMatches, matched_live_shapes = _minaLiveMatches,
            natural_end_live_hazards = _minaNaturalHazards, natural_end_owned_bullets = _minaNaturalShots,
            warning_field_intersections = _minaWarningClips, maximum_warning_boundary_error = _minaWarningClipError,
            errors = _minaErrors, state_transitions = _minaTransitions,
            scope = "Every physics frame: drawn warning geometry versus live hazard multiset; actual receiver contact/momentum, held-danger continuity and terminal-triggered natural recoil exits. Rendered pixels and human difficulty require separate review.",
            cues = _minaGeometry.Select(pair => new
            {
                key = pair.Key, mode = pair.Value.Last.Mode, round = pair.Value.Last.Round, index = pair.Value.Last.Index,
                starts_at = pair.Value.Last.Start, completes_at = pair.Value.Last.Start + 1.2,
                planned_fire_at = pair.Value.Last.Fire, planned_end_at = pair.Value.Last.End,
                full_geometry_observed_at = pair.Value.Full, initial_exit_forecast_full_at = pair.Value.InitialFull,
                physical_geometry_observed_at = pair.Value.Physical,
                complete_geometry_hold_seconds = pair.Value.Physical - pair.Value.Full,
                samples = pair.Value.Samples, matched_warning_shapes = pair.Value.Warnings, matched_live_shapes = pair.Value.Live,
                live_shapes_intersecting_field = pair.Value.InField, dangerous_static_sender_samples = pair.Value.StaticSamples,
                maximum_position_error = pair.Value.PositionError, maximum_shape_error = pair.Value.ShapeError,
                maximum_momentum_error = pair.Value.MomentumError,
                cancelled = pair.Value.Last.Cancelled, cancelled_at = pair.Value.Last.CancelledAt,
                fired = pair.Value.Last.Fired, observed_step_fire_at = pair.Value.Last.ObservedFire,
                transferred = pair.Value.Last.Transferred, transferred_at = pair.Value.Last.TransferAt,
                exited = pair.Value.Last.Exited, observed_exit_at = pair.Value.Last.ExitAt,
                predecessor = pair.Value.Last.Predecessor, successor = pair.Value.Last.Successor,
                release_cue = pair.Value.Last.Release, held_memory = pair.Value.Last.Held,
                received_from = pair.Value.Last.ReceivedFrom, received_momentum = pair.Value.Last.Momentum,
                mass = pair.Value.Last.Mass, speed = pair.Value.Last.Speed, half_width = pair.Value.Last.Radius,
                points = pair.Value.Initial.Points.Select(MinaPoint), bounds = pair.Value.Initial.Bounds.Select(MinaRect),
                final_position = MinaPoint(pair.Value.Last.Position),
                acknowledgement_card = pair.Value.Last.Mode == 1 ? new
                {
                    position = MinaPoint(pair.Value.Initial.Card), reserved_player = MinaPoint(pair.Value.Initial.CardReserved),
                    current_row = pair.Value.Initial.CurrentCard, initial_player_body_overlap = pair.Value.Initial.CardInitialOverlap,
                    initial_distance = pair.Value.CardInitialDistance, card_body_clear = pair.Value.CardBodyClear,
                    access_checked = pair.Value.Initial.CurrentCard && !pair.Value.Initial.CardInitialOverlap,
                    access_body_clear = pair.Value.CardRouteClear, one_way_seconds = pair.Value.CardMeasuredSeconds,
                    path = pair.Value.Initial.CardPath.Select(MinaPoint),
                } : null,
            }),
        };
    }
}
