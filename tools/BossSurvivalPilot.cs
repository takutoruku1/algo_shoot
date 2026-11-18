using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public partial class BossTechniqueVideo
{
    private const float SurvivalSliceSeconds = .1f;
    private Vector2 _survivalMove;
    private double _survivalDecisionAt;
    private ulong _survivalDecisionAttack;
    private float[] _survivalLanes = Array.Empty<float>();
    private ulong _survivalGuideAttack;
    private float _survivalBurdenLane, _survivalClockOffset;
    private int _survivalWave = -1;
    private Vector2 _survivalWaveEdge;
    private double _survivalOpeningSeen = -1;
    private bool _survivalRingSeen;
    private int _survivalBlackoutWave = -1;
    private Vector2? _survivalBlackoutGoal;

    private bool DriveSurvivalGuidance(double delta)
    {
        var attack = _easyAttack!;
        bool fresh = _survivalGuideAttack != attack.GetInstanceId();
        if (fresh) { _survivalGuideAttack = attack.GetInstanceId(); _survivalBurdenLane = float.NaN; _survivalWave = -1; }
        if (attack is MinaBossTechnique { TechniqueKind: BossTechniqueKind.Area, TechniqueVariant: 0 })
        {
            if ((int)_game.Difficulty >= 2) return false;
            if (attack.Elapsed < 1.4) { EasyFollowRoute(null, delta); return true; }
            var cues = Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>().ToArray();
            if (cues.Any(c => Read<int>(c, "Round") > 0 && attack.Elapsed >= Read<double>(c, "StartsAt") + 1.4)) return false;
            if (float.IsNaN(_survivalBurdenLane))
            {
                _survivalBurdenLane = Enumerable.Range(1, 6).Select(row => row * Field.Height / 7)
                    .Where(y => !EasyBodyAlong(new Vector2(_easyStart.X, y), new Vector2(Field.Left + 6, y)))
                    .OrderBy(y => Math.Abs(y - _easyStart.Y)).First();
            }
            Vector2 target = Math.Abs(_player.GlobalPosition.Y - _survivalBurdenLane) > .1f
                ? new(_player.GlobalPosition.X, _survivalBurdenLane) : new(Field.Left + 6, _survivalBurdenLane);
            EasyGuideTo(target, delta);
            return true;
        }
        if (attack is not AkariBossTechnique) return false;
        int mechanic = Read<int>(attack, "_mechanic");
        Vector2 origin = Read<Vector2>(attack, "_origin");
        if (mechanic == 5 && (int)_game.Difficulty < 2)
        {
            double change = (double)attack.GetType().GetProperty("EasyPileSwitchAt", Private)!.GetValue(attack)!;
            int pass = attack.Elapsed >= change ? 1 : 0;
            if (attack.Elapsed < pass * change + 1.4) { EasyFollowRoute(null, delta); return true; }
            float x = Enumerable.Range(pass == 0 ? 1 : 0, pass == 0 ? 6 : 7)
                .Select(column => Field.Left + (column + (pass == 0 ? 0 : .5f)) * Field.Width / 7)
                .OrderBy(x => Math.Abs(x - _player.GlobalPosition.X)).First();
            EasyGuideTo(new Vector2(x, _player.GlobalPosition.Y), delta);
            return true;
        }
        if (mechanic == 12)
        {
            double warning = Read<double>(attack, "_warning");
            int hands = (int)attack.GetType().GetProperty("EasyClockHands", Private)!.GetValue(attack)!;
            float angle = Read<float>(attack, "_angle");
            if (fresh)
            {
                float current = origin.DirectionTo(_player.GlobalPosition).Angle();
                _survivalClockOffset = (Mathf.Round((current - angle) / (Mathf.Tau / hands) - .5f) + .5f) * Mathf.Tau / hands;
            }
            if (attack.Elapsed < 1.8) { EasyFollowRoute(null, delta); return true; }
            float period = (float)attack.GetType().GetProperty("ClockRevolution", Private)!.GetValue(attack)!;
            float orbitRadius = Math.Min(56, _player.SlowestMoveSpeed * period * .72f / Mathf.Tau);
            if (OS.GetCmdlineUserArgs().Contains("--clock-fixed-hub")) origin = Field.Rect.GetCenter();
            if (OS.GetCmdlineUserArgs().Contains("--clock-fixed-pace")) period = 7.5f;
            float turn = angle + _survivalClockOffset + (float)Math.Max(0, attack.Elapsed - warning + .12) * Mathf.Tau / period;
            EasyGuideTo(origin + Vector2.FromAngle(turn) * orbitRadius, delta);
            return true;
        }
        if (mechanic == 7)
        {
            var starts = Read<double[]>(attack, "_easyWaveWarningStarts");
            int wave = starts[1] >= 0 && attack.Elapsed >= starts[1] + 1.9 ? 1 : 0;
            if (attack.Elapsed < starts[wave] + 1.9) { EasyFollowRoute(null, delta); return true; }
            if (_survivalWave != wave)
            {
                _survivalWave = wave;
                _survivalOpeningSeen = -1;
                _survivalRingSeen = false;
                float angle = Read<float[]>(attack, "_easyWaveGapAngles")[wave];
                float edge = (float)attack.GetType().GetProperty("EasyWaveExpiryHalfAngle", Private)!.GetValue(attack)!;
                _survivalWaveEdge = Enumerable.Range(-4, 9)
                    .Select(i => origin + Vector2.FromAngle(angle + i * edge / 5) * 60)
                    .Where(Field.Rect.Grow(-Player.HitRadius).HasPoint)
                    .OrderBy(point => EasyBodyAlong(_player.GlobalPosition, point))
                    .ThenBy(point => point.DistanceSquaredTo(_player.GlobalPosition)).First();
            }
            Vector2 waiting = _survivalWaveEdge;
            Vector2 inward = origin + origin.DirectionTo(waiting) * 12;
            bool blocked = Enumerable.Range(0, 49).Any(i => attack.CoversPoint(waiting.Lerp(inward, i / 48f)));
            _survivalRingSeen |= blocked;
            if (blocked) _survivalOpeningSeen = -1;
            else if (_survivalRingSeen && _survivalOpeningSeen < 0) _survivalOpeningSeen = attack.Elapsed;
            EasyGuideTo(_survivalOpeningSeen >= 0 && attack.Elapsed >= _survivalOpeningSeen + .2 ? inward : waiting, delta);
            return true;
        }
        if (mechanic == 14)
        {
            if (attack.Elapsed < .4) { EasyFollowRoute(null, delta); return true; }
            float side = origin.X <= Field.CenterX ? 1 : -1;
            double age = Math.Max(0, attack.Elapsed - Read<double>(attack, "_warning"));
            float outside = Math.Max(58, Math.Abs(58 - (float)age * (30 + (int)_game.Difficulty * 4))) + 14;
            EasyGuideTo(new Vector2(origin.X + side * outside, _player.GlobalPosition.Y), delta);
            return true;
        }
        return false;
    }

    private readonly record struct SurvivalShape(Vector2 A, Vector2 B, float Radius, bool Box = false)
    {
        public float Gap(Vector2 at)
        {
            if (!Box)
            {
                Vector2 axis = B - A;
                float length = axis.LengthSquared();
                float along = length == 0 ? 0 : Math.Clamp((at - A).Dot(axis) / length, 0, 1);
                return at.DistanceTo(A + axis * along) - Radius - Player.HitRadius;
            }
            bool inside = at.X >= A.X && at.X <= B.X && at.Y >= A.Y && at.Y <= B.Y;
            return (inside ? -Math.Min(Math.Min(at.X - A.X, B.X - at.X), Math.Min(at.Y - A.Y, B.Y - at.Y))
                : at.DistanceTo(at.Clamp(A, B))) - Player.HitRadius;
        }
    }

    private sealed class SurvivalSlice
    {
        private const int Columns = 21, Rows = 15;
        private readonly List<SurvivalShape>?[] _cells = new List<SurvivalShape>?[Columns * Rows];
        public void Add(SurvivalShape shape)
        {
            Vector2 low = new(Math.Min(shape.A.X, shape.B.X), Math.Min(shape.A.Y, shape.B.Y));
            Vector2 high = new(Math.Max(shape.A.X, shape.B.X), Math.Max(shape.A.Y, shape.B.Y));
            float pad = shape.Radius + Player.HitRadius + 8;
            int x0 = Math.Max(0, (int)Math.Floor((low.X - pad - Field.Left) / 16));
            int x1 = Math.Min(Columns - 1, (int)Math.Floor((high.X + pad - Field.Left) / 16));
            int y0 = Math.Max(0, (int)Math.Floor((low.Y - pad) / 16));
            int y1 = Math.Min(Rows - 1, (int)Math.Floor((high.Y + pad) / 16));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                (_cells[y * Columns + x] ??= new()).Add(shape);
        }
        public float Gap(Vector2 at)
        {
            int cell = Math.Clamp((int)(at.Y / 16), 0, Rows - 1) * Columns
                + Math.Clamp((int)((at.X - Field.Left) / 16), 0, Columns - 1);
            float gap = 8;
            if (_cells[cell] is { } shapes) foreach (var shape in shapes) gap = Math.Min(gap, shape.Gap(at));
            return gap;
        }
    }

    private readonly record struct SurvivalRoute(Vector2 At, Vector2 First, float Minimum, float Score);

    private bool DriveSurvivalForecast(double delta)
    {
        var attack = _easyAttack;
        if (attack == null) return false;
        if (attack.MechanicKey is "koharu.pursuit-train" or "koharu.unpaired-light") return false;
        if (attack.MechanicKey == "koharu.screen-aperture"
            && (_game.Difficulty != GameManager.Diff.Hard || _boss is not BossKoharu)) return false;
        if (attack.MechanicKey is "akari.braided-delivery" or "akari.proximity-addressing") return false;
        if (attack.MechanicKey is "rei-finite-cut-endpoint" or "rei-branch-front-tip"
            or "rei-trail-quotation") return false;
        if (attack is MinaBossTechnique && attack.TechniqueKind == BossTechniqueKind.Area && attack.TechniqueVariant is 1 or 2 or 3) return false;
        if (attack is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Area, TechniqueVariant: 3 }) return false;
        if (attack is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Normal, TechniqueVariant: 0 } && attack.Elapsed < 2) return false;
        if (_survivalDecisionAttack != attack.GetInstanceId())
        {
            _survivalDecisionAttack = attack.GetInstanceId(); _survivalDecisionAt = 0; _survivalMove = Vector2.Zero;
            _survivalBlackoutWave = -1; _survivalBlackoutGoal = null;
        }
        if (attack.Elapsed + 1e-5 < _survivalDecisionAt) { AutoplayInput.Move(_survivalMove); return true; }
        _survivalDecisionAt = attack.Elapsed + .1;
        _survivalLanes = attack is MinaBossTechnique { TechniqueVariant: 0 }
            ? Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>()
                .Where(c => attack.Elapsed >= Read<double>(c, "StartsAt") + 1.4)
                .SelectMany(c => Read<Rect2[]>(c, "Bounds"))
                .SelectMany(b => new[] { b.Position.Y - 3.5f, b.End.Y + 3.5f }).Distinct()
                .Where(y => y >= Field.Top && y <= Field.Bottom).ToArray() : Array.Empty<float>();
        var slices = SurvivalForecast(attack, attack.MechanicKey == "mina.boundary-wrap" ? 84 : 42);
        Vector2 position = _player.GlobalPosition;
        Vector2? destination = attack.MechanicKey is "mina.boundary-wrap" or "mina.snapshot-exchange"
            ? new Vector2(Field.CenterX, Field.Bottom - 48) : null;
        if (attack is ReiBossTechnique { MechanicKey: "rei-edited-page-peeling" } peeling && (int)_game.Difficulty >= 2)
        {
            var page = peeling.VisiblePeelTargets.FirstOrDefault();
            if (page != default) destination = new Vector2(Field.Left + 16, page.Position.Y + page.Velocity.Y * .25f)
                .Clamp(Field.Rect.Position + Vector2.One * 8, Field.Rect.End - Vector2.One * 8);
            else if (!peeling.PeeledEditions.All(peeled => peeled)) destination = new Vector2(Field.Left + 16, peeling.VisiblePageBinding);
        }
        if (attack.MechanicKey == "mina.unfolding-burden" && (int)_game.Difficulty >= 2)
        {
            var page = Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>()
                .Where(c => attack.Elapsed >= Read<double>(c, "StartsAt") + 1.4)
                .MaxBy(c => Read<int>(c, "Round"));
            if (page != null)
                destination = new Vector2(Read<Vector2[]>(page, "Points")[0].X < Field.Left ? Field.Left + 40 : Field.Right - 40,
                    Field.Bottom - 48);
        }
        Vector2[] sourceCutTargets = attack is ReiBossTechnique { SourceCutTechnique: true } sourceCut
            ? SourceCutGuidanceTargets(sourceCut) : Array.Empty<Vector2>();
        Vector2[] sourceCutNextTargets = Array.Empty<Vector2>();
        double sourceCutSwitchAt = double.PositiveInfinity;
        Vector2? sourceCutOrigin = null;
        Vector2 sourceCutAnchor = default;
        float sourceCutAlignmentRadius = 0;
        if (!_sourceCutMiss && _game.Difficulty >= GameManager.Diff.Hard
            && attack is ReiBossTechnique { SourceCutTechnique: true } sourcePreview)
        {
            var tracking = sourcePreview.SourceCutRounds.FirstOrDefault(round => !round.Locked
                && attack.Elapsed >= round.StartsAt && attack.Elapsed < round.LocksAt);
            sourceCutSwitchAt = tracking == null ? double.PositiveInfinity : tracking.LocksAt + 1d / 30;
            sourceCutOrigin = tracking?.From;
            sourceCutAnchor = sourcePreview.SourceCutAnchor;
            sourceCutAlignmentRadius = sourcePreview.SourceCutTargetRadius + sourcePreview.SourceCutWidth - 2;
            if (GameManager.ShowAttackTelegraphs && sourcePreview.SourceCutRounds.Count < 4)
            {
                var directions = Read<Vector2[]>(sourcePreview, "_sourceCutDirections");
                Vector2 visibleAxis = directions[sourcePreview.SourceCutRounds.Count * directions.Length / 4];
                sourceCutNextTargets = SourceCutGuidanceTargets(sourcePreview, sourcePreview.SourceCutAnchor - visibleAxis);
                if (tracking == null)
                {
                    sourceCutTargets = sourceCutNextTargets;
                    sourceCutNextTargets = Array.Empty<Vector2>();
                    sourceCutSwitchAt = sourcePreview.SourceCutRounds[^1].LocksAt + sourcePreview.SourceCutCadence + 1d / 30;
                }
            }
        }
        float[] sourceCutContinuation = sourceCutTargets.Select(target => sourceCutNextTargets.Length == 0 ? 0
            : sourceCutNextTargets.Min(next => target.DistanceTo(next) + (EasyBodyAlong(target, next) ? 72 : 0))).ToArray();
        float SourceCutDistance(Vector2 at, double time)
        {
            if (time >= sourceCutSwitchAt)
                return sourceCutNextTargets.Length == 0 ? 0 : sourceCutNextTargets.Min(target => at.DistanceTo(target));
            float distance = float.PositiveInfinity;
            float alignmentWeight = sourceCutSwitchAt - time <= .4 ? 4 : 1;
            for (int index = 0; index < sourceCutTargets.Length; index++)
                distance = Math.Min(distance, at.DistanceTo(sourceCutTargets[index]) * alignmentWeight
                    + sourceCutContinuation[index] * .35f);
            return sourceCutTargets.Length == 0 ? 0 : distance;
        }
        if (attack is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Area } && attack.MechanicKey!="rei-crop-three-edges" && attack.Elapsed >= 1.4)
        {
            float side = Read<Vector2>(attack, "_anchor").X < Field.CenterX ? 1 : -1;
            destination = new Vector2(Field.CenterX + side * 78, Field.Top + 10);
        }
        if (attack is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Finale } && attack.Elapsed >= 1.4)
        {
            var geometry = attack.GetType().GetMethod("EasySentenceGeometry", Private)!;
            var gap = ((float Head, float Center, Rect2 Upper, Rect2 Lower, Rect2 Tail))geometry.Invoke(attack,
                new object[] { Math.Max(3.4, attack.Elapsed + .3) })!;
            destination = new Vector2(gap.Head, gap.Center);
        }
        if (attack is KoharuBossTechnique { TechniqueKind: BossTechniqueKind.Extra } && attack.Elapsed >= 1.1 && attack.Elapsed < 6.3)
            destination = new Vector2(Field.CenterX, Field.CenterY + 40);
        if (attack.MechanicKey == "koharu.leap-bloom" && attack.Elapsed >= 1.1)
            destination = new Vector2(Field.CenterX, Field.CenterY);
        if (attack.MechanicKey == "koharu.screen-aperture" && _game.Difficulty == GameManager.Diff.Hard && _boss is BossKoharu)
        {
            Vector2 origin = Read<Vector2>(attack, "_origin");
            double lead = (double)attack.GetType().GetProperty("ScreenLead", Private)!.GetValue(attack)!;
            var flashes = Read<List<(float Angle, double StartsAt)>>(attack, "_screenFlashes")
                .Where(flash => attack.Elapsed >= flash.StartsAt + .2 && attack.Elapsed < flash.StartsAt + lead + 2.2).ToArray();
            float nearest = float.PositiveInfinity;
            for (float y = Field.Top + 8; y <= Field.Bottom - 8; y += 8)
            for (float x = Field.Left + 8; x <= Field.Right - 8; x += 8)
            {
                Vector2 candidate = new(x, y), offset = candidate - origin;
                float distance = candidate.DistanceSquaredTo(position);
                if (distance >= nearest || EasyBodyAt(candidate) || flashes.Any(flash =>
                    Math.Abs(Vector2.FromAngle(flash.Angle).Cross(offset)) <= Math.Max(0, Vector2.FromAngle(flash.Angle).Dot(offset)) * Mathf.Tan(.30f) + 8)) continue;
                destination = candidate;
                nearest = distance;
            }
        }
        if (attack is KoharuBossTechnique { TechniqueKind: BossTechniqueKind.Finale })
        {
            var sweeps = Read<List<KoharuBossTechnique.KoharuEasySweep>>(attack, "_easySweeps")
                .Where(s => s.Points.Length > 1 && attack.Elapsed >= s.StartsAt + 1.1 && attack.Elapsed < s.EndsAt).ToArray();
            int wave = sweeps.Length == 0 ? -1 : sweeps.Max(s => s.Wave);
            if (wave != _survivalBlackoutWave)
            {
                _survivalBlackoutWave = wave;
                var shapes = sweeps.Select(s => new SurvivalShape(s.Points[0], s.Points[^1], s.HalfWidth)).ToArray();
                float distance = float.PositiveInfinity;
                _survivalBlackoutGoal = null;
                for (float y = Field.Top + 3; y <= Field.Bottom - 3; y += 3)
                for (float x = Field.Left + 3; x <= Field.Right - 3; x += 3)
                {
                    Vector2 candidate = new(x, y);
                    float travel = candidate.DistanceSquaredTo(position);
                    if (travel >= distance || shapes.Any(s => s.Gap(candidate) < 3) || EasyBodyAt(candidate)) continue;
                    _survivalBlackoutGoal = candidate;
                    distance = travel;
                }
            }
            destination = _survivalBlackoutGoal;
        }
        float speed = attack.MechanicKey is "akari.filing-piles" or "mina.boundary-wrap" or "mina.snapshot-exchange" or "mina.reply-driven-stars" ? _player.SlowestMoveSpeed : _player.MoveSpeed;
        bool precise = attack is KoharuBossTechnique { TechniqueKind: BossTechniqueKind.Finale }
            || attack.MechanicKey is "mina.boundary-wrap" or "mina.snapshot-exchange" or "akari.rain-splash" or "akari.returned-express";
        int stride = precise ? 1 : 2;
        var routes = new List<SurvivalRoute> { new(position, Vector2.Zero, 8, 0) };
        Vector2 best = Vector2.Zero;
        for (int step = stride; step < slices.Length; step += stride)
        {
            var next = new Dictionary<(int X, int Y), SurvivalRoute>();
            foreach (var route in routes)
            foreach (var move in SurvivalDirections(attack, route.At, speed * SurvivalSliceSeconds * stride))
            {
                Vector2 at = (route.At + move * (speed * SurvivalSliceSeconds * stride)).Clamp(
                    Field.Rect.Position + Vector2.One * .5f, Field.Rect.End - Vector2.One * .5f);
                float gap = precise ? slices[step].Gap(at)
                    : Math.Min(slices[step - 1].Gap(route.At.Lerp(at, .5f)), slices[step].Gap(at));
                float minimum = Math.Min(route.Minimum, gap);
                float score = route.Score + Math.Clamp(gap, -20, 8)
                    - (destination is Vector2 target ? at.DistanceTo(target) * .3f : 0)
                    - SourceCutDistance(at, attack.Elapsed + step * SurvivalSliceSeconds) * .3f
                    - (step == stride ? move.DistanceTo(_survivalMove.Normalized()) * .025f : 0);
                double priorTime = attack.Elapsed + (step - stride) * SurvivalSliceSeconds;
                if (sourceCutOrigin is Vector2 origin && priorTime < sourceCutSwitchAt
                    && attack.Elapsed + step * SurvivalSliceSeconds >= sourceCutSwitchAt)
                {
                    Vector2 lockedAt = route.At.Lerp(at,
                        (float)((sourceCutSwitchAt - priorTime) / (stride * SurvivalSliceSeconds)));
                    Vector2 ray = origin.DirectionTo(lockedAt) * Field.Width * 3;
                    float offset = Geometry2D.GetClosestPointToSegment(sourceCutAnchor, origin, origin + ray)
                        .DistanceTo(sourceCutAnchor);
                    if (offset > sourceCutAlignmentRadius)
                        score -= 700 + Math.Min(35, offset - sourceCutAlignmentRadius) * 8;
                }
                var candidate = new SurvivalRoute(at, step == stride ? move : route.First, minimum, score);
                var cell = ((int)Math.Round(at.X / 2), (int)Math.Round(at.Y / 2));
                if (!next.TryGetValue(cell, out var old) || Rank(candidate) > Rank(old)) next[cell] = candidate;
            }
            float cellSize = precise ? 5 : 10;
            routes = next.Values.GroupBy(r => ((int)(r.At.X / cellSize), (int)(r.At.Y / cellSize)))
                .Select(group => group.MaxBy(Rank)).OrderByDescending(Rank).Take(precise ? 96 : 48).ToList();
            best = routes[0].First;
        }
        _survivalMove = best * (speed / _player.MoveSpeed);
        AutoplayInput.Move(_survivalMove);
        return true;

        static float Rank(SurvivalRoute r) => Math.Min(r.Minimum, 1.5f) * 1000 + r.Score;
    }

    private IEnumerable<Vector2> SurvivalDirections(BossCharacterTechnique attack, Vector2 at, float distance)
    {
        yield return _survivalMove.Normalized();
        yield return Vector2.Zero;
        for (int direction = 0; direction < 16; direction++)
        {
            var heading=Vector2.FromAngle(direction * Mathf.Tau / 16);
            yield return heading;
        }
        if (attack is MinaBossTechnique { TechniqueVariant: 0 })
        {
            var rows = _survivalLanes.OrderBy(y => Math.Abs(y - at.Y)).Take(4);
            foreach (float y in rows)
                foreach (float x in new[] { 0f, -distance, distance })
                    yield return new Vector2(x, y - at.Y).LimitLength(distance) / distance;
        }
    }

    private SurvivalSlice[] SurvivalForecast(BossCharacterTechnique attack, int count)
    {
        var slices = Enumerable.Range(0, count).Select(_ => new SurvivalSlice()).ToArray();
        void Add(int index, Vector2 a, Vector2 b, float radius, bool box = false)
            => slices[index].Add(new(a, b, radius + .35f, box));
        void Box(int index, Rect2 bounds) => Add(index, bounds.Position, bounds.End, 0, true);
        var body = new List<SurvivalShape>();
        foreach (uint owner in _boss.GetShapeOwners())
        {
            if (!_boss.BodyContactDamaging || _boss.IsShapeOwnerDisabled(owner)) continue;
            Transform2D transform = _boss.GlobalTransform * _boss.ShapeOwnerGetTransform(owner);
            for (int shape = 0; shape < _boss.ShapeOwnerGetShapeCount(owner); shape++)
            {
                Rect2 bounds = transform * _boss.ShapeOwnerGetShape(owner, shape).GetRect();
                body.Add(new(bounds.Position, bounds.End, 0, true));
            }
        }
        foreach (var slice in slices) foreach (var shape in body) slice.Add(shape);

        if (attack is ReiBossTechnique { MechanicKey:"rei-crop-three-edges" } crop && _game.Difficulty==GameManager.Diff.Hard)
        {
            var frame=Read<Rect2>(crop,"_cropFrame");
            foreach(var h in crop.PracticeHazards.Where(h=>h.Live))
            {
                var center=(h.A+h.B)/2;
                var velocity=Vector2.Zero;
                if(h.Shape==2 && (h.B.X-h.A.X<15 || h.B.Y-h.A.Y<15))
                    velocity=new Vector2(center.X<frame.Position.X ? -40 : center.X>frame.End.X ? 40 : 0,
                        center.Y<frame.Position.Y ? -40 : center.Y>frame.End.Y ? 40 : 0);
                for(int i=0;i<count;i++)
                {
                    Vector2 shift=velocity*(i*SurvivalSliceSeconds);
                    Add(i,h.A+shift,h.B+shift,h.Radius,h.Shape==2);
                }
            }
            return slices;
        }

        if (attack is ReiBossTechnique { MechanicKey: "rei-merge-then-branch" })
        {
            double starts = Read<double>(attack, "_amplificationStart");
            foreach (var hazard in attack.PracticeHazards.Where(h => h.Live || attack.Elapsed >= starts + .8))
                for (int slice = 0; slice < count; slice++)
                    Add(slice, hazard.A, hazard.B, hazard.Radius, hazard.Shape == 2);
            foreach (var shot in attack.OwnedShots.Where(b => !b.LeadIn && b.CollisionLayer != 0 && b.Velocity != Vector2.Zero))
                for (int slice = 0; slice < count; slice++)
                {
                    Vector2 position = shot.GlobalPosition + shot.Velocity * (slice * SurvivalSliceSeconds);
                    Add(slice, position - shot.Velocity * .05f, position + shot.Velocity * .05f, shot.CollisionRadius);
                }
            return slices;
        }

        if (attack is MinaBossTechnique { MechanicKey: "mina.reply-driven-stars" } replies)
        {
            float pitch = 8 + (_game.Difficulty switch { GameManager.Diff.Easy => 14, GameManager.Diff.Normal => 12,
                GameManager.Diff.Hard => 10, _ => 8 });
            foreach (var reply in replies.StarReplies)
            foreach (var source in reply.Sources)
            for (int slice = 0; slice < count; slice++)
            {
                double age = attack.Elapsed + slice * SurvivalSliceSeconds - reply.FiresAt;
                for (int pulse = 0; pulse < 5; pulse++)
                {
                    float travel = (float)(age - pulse * pitch / replies.StarReplySpeed);
                    if (travel < 0 || travel > reply.EntryTime + reply.ExitTime) continue;
                    Vector2 star = reply.Position(source, travel);
                    Add(slice, star, star, 4);
                }
            }
            return slices;
        }

        if (attack is MinaBossTechnique { MechanicKey: "mina.snapshot-exchange" } snapshot && (int)_game.Difficulty >= 2)
        {
            bool live = attack.PracticeHazards.Any(h => h.Live);
            foreach (var band in snapshot.VisibleSnapshotBands)
            for (int slice = 0; slice < count; slice++)
            {
                float time = slice * SurvivalSliceSeconds;
                bool pending = double.IsFinite(snapshot.SnapshotExchangeDelay);
                bool swapped = pending && time >= snapshot.SnapshotExchangeDelay;
                float travel = live ? pending ? swapped ? time - (float)snapshot.SnapshotExchangeDelay : 0 : time : 0;
                float y = band.Y + band.Velocity * travel;
                if (!snapshot.SnapshotDraining)
                {
                    float phase = Mathf.PosMod(y - Field.Top, Field.Height * 2);
                    y = Field.Top + (phase <= Field.Height ? phase : Field.Height * 2 - phase);
                }
                int side = swapped ? 1 - band.Side : band.Side;
                Box(slice, new Rect2(Field.Left + side * Field.Width / 2, y - 21, Field.Width / 2, 42).Grow(2));
                if (pending && !swapped && time + .3 >= snapshot.SnapshotExchangeDelay)
                    Box(slice, new Rect2(Field.Left + (1 - side) * Field.Width / 2, y - 21, Field.Width / 2, 42).Grow(2));
            }
            return slices;
        }

        if (attack is MinaBossTechnique { MechanicKey: "mina.boundary-wrap" } wrapped && (int)_game.Difficulty >= 2)
        {
            foreach (var band in attack.PracticeHazards.Where(h => h.Shape == 2))
            {
                float width = band.B.X - band.A.X, center = (band.A.X + band.B.X) * .5f;
                if (width < 11.9f && band.A.X <= Field.Left + .01f) center = Field.Left + width - 6;
                else if (width < 11.9f && band.B.X >= Field.Right - .01f) center = Field.Right + 6 - width;
                for (int slice = 0; slice < count; slice++)
                {
                    float x = Field.Left + Mathf.PosMod(center - Field.Left + (band.Live ? slice * SurvivalSliceSeconds * wrapped.WrapSpeed : 0), Field.Width);
                    for (int copy = -1; copy <= 1; copy++)
                        Box(slice, new Rect2(x + copy * Field.Width - 6, band.A.Y, 12, band.B.Y - band.A.Y).Grow(1));
                }
            }
            return slices;
        }

        if (attack is AkariBossTechnique)
            SurvivalAkariForecast(attack, slices);
        else if (attack is ReiBossTechnique)
        {
            if (attack is ReiBossTechnique { SourceCutTechnique: true } source && _game.Difficulty == GameManager.Diff.Hard)
                foreach (var round in source.SourceCutRounds.Where(round => round.Locked && source.Elapsed >= round.LocksAt + .7))
                foreach (var fragment in source.SourceFragments(round))
                for (int i = 0; i < count; i++)
                {
                    double age = source.Elapsed + i * SurvivalSliceSeconds - fragment.Fire;
                    if (age < 0) continue;
                    Vector2 at = fragment.From + fragment.Direction * (float)age * 40;
                    if (Field.Rect.Grow(4).HasPoint(at)) Add(i, at - fragment.Direction, at + fragment.Direction, 3);
                }
            foreach (var path in Read<IEnumerable>(attack, "_easyPaths").Cast<object>())
            {
                if (attack.Elapsed < Read<double>(path, "StartsAt") + 1.4) continue;
                double fire = Read<double>(path, "FiresAt"), arrives = Read<double>(path, "ArrivesAt");
                if (attack.Elapsed > arrives + .25 || fire > attack.Elapsed + count * SurvivalSliceSeconds) continue;
                double child = Read<double>(path, "LastChildFire");
                bool held = child > arrives;
                var points = Read<Vector2[]>(path, "Points");
                var distances = Read<float[]>(path, "Distances");
                bool synchronized=Read<bool>(path,"Synchronized");
                Vector2 from = Read<Vector2>(path, "From"), to = Read<Vector2>(path, "To");
                float speed = Read<float>(path, "Velocity"), radius = Read<float>(path, "HalfWidth");
                for (int i = 0; i < count; i++)
                {
                    double at = attack.Elapsed + i * SurvivalSliceSeconds;
                    if (at < fire || (double.IsFinite(child) || held) && at >= arrives) continue;
                    Vector2 Position(double time)
                    {
                        float travel = (float)Math.Max(0, time - fire) * speed;
                        if (points.Length == 0) return from + from.DirectionTo(to) * travel;
                        if(synchronized) return Point(points,(time-fire)/(arrives-fire));
                        int leg = Array.BinarySearch(distances, travel);
                        leg = Math.Clamp(leg >= 0 ? leg + 1 : ~leg, 1, points.Length - 1);
                        return points[leg - 1] + points[leg - 1].DirectionTo(points[leg]) * (travel - distances[leg - 1]);
                    }
                    Add(i, Position(at - .045), Position(at + .045), radius);
                }
            }
            foreach (var cut in Read<IEnumerable>(attack, "_easyCuts").Cast<object>())
            {
                if (attack.Elapsed < Read<double>(cut, "StartsAt")
                    + (attack is ReiBossTechnique { SourceCutTechnique: true } ? .9 : 1.4)) continue;
                for (int i = 0; i < count; i++)
                {
                    double at = attack.Elapsed + i * SurvivalSliceSeconds;
                    if (at < Read<double>(cut, "FiresAt") - .12 || at >= Read<double>(cut, "EndsAt") + .08) continue;
                    Add(i, Read<Vector2>(cut, "A"), Read<Vector2>(cut, "B"), Read<float>(cut, "HalfWidth"), Read<bool>(cut, "Box"));
                }
            }
            if (attack.Elapsed >= 1.4 && attack.TechniqueKind == BossTechniqueKind.Area)
            {
                var geometry = attack.GetType().GetMethod("EasyReaderGeometry", Private)!;
                double readerEnd = (double)attack.GetType().GetProperty("EasyReaderEnd", Private)!.GetValue(attack)!;
                for (int i = 0; i < count; i++)
                {
                    double at = attack.Elapsed + i * SurvivalSliceSeconds;
                    if (at < 3.4 || at >= readerEnd) continue;
                    var g = ((Vector2 A, Vector2 B, Vector2 C, Vector2 D, float Radius))geometry.Invoke(attack, new object[] { at })!;
                    Add(i, g.A, g.B, g.Radius + 2.2f); Add(i, g.C, g.D, g.Radius + 2.2f);
                }
            }
            if (attack.Elapsed >= 1.4 && attack.TechniqueKind == BossTechniqueKind.Finale)
            {
                var geometry = attack.GetType().GetMethod("EasySentenceGeometry", Private)!;
                for (int i = 0; i < count; i++)
                {
                    double at = attack.Elapsed + i * SurvivalSliceSeconds;
                    if (at < 3.4 || at >= 3.4 + 8.2) continue;
                    var g = ((float Head, float Center, Rect2 Upper, Rect2 Lower, Rect2 Tail))geometry.Invoke(attack, new object[] { at })!;
                    Box(i, g.Upper); Box(i, g.Lower); if (g.Tail.HasArea()) Box(i, g.Tail);
                }
            }
        }
        else if (attack is KoharuBossTechnique)
        {
            if ((int)_game.Difficulty >= 2 && attack is KoharuBossTechnique { MechanicKey: "koharu.clap-annihilation" } claps)
            {
                var lanes = _game.IsLunatic ? claps.VisibleClapLanes : Enumerable.Range(0, _boss is BossKoharu ? 12 : 6)
                    .Select(beat => new KoharuBossTechnique.ClapLane(beat % 3, claps.VisibleClapTargetX,
                        Read<double>(claps, "_warning") + beat * (_boss is BossKoharu ? 1.4 : 2.8) - 2.4,
                        Read<double>(claps, "_warning") + beat * (_boss is BossKoharu ? 1.4 : 2.8))).ToArray();
                foreach (var lane in lanes.Where(lane => attack.Elapsed >= lane.StartsAt + .2))
                for (int i = 0; i < count; i++)
                {
                    double age = attack.Elapsed + i * SurvivalSliceSeconds - lane.FiresAt;
                    if (age < -.08 || age >= 3.6) continue;
                    float progress = (float)Math.Max(0, age) / 3.6f;
                    float left = Mathf.Lerp(Field.Left - 8, lane.Junction - 8, progress);
                    float right = Mathf.Lerp(Field.Right + 8, lane.Junction + 8, progress);
                    float top = Field.Top + lane.Row * Field.Height / 3;
                    Box(i, new Rect2(left - 8, top, 16, Field.Height / 3));
                    Box(i, new Rect2(right - 8, top, 16, Field.Height / 3));
                }
                return slices;
            }
            if (_game.Difficulty == GameManager.Diff.Hard && _boss is BossKoharu && attack.MechanicKey == "koharu.screen-aperture")
            {
                Vector2 origin = Read<Vector2>(attack, "_origin");
                double lead = (double)attack.GetType().GetProperty("ScreenLead", Private)!.GetValue(attack)!;
                foreach (var flash in Read<List<(float Angle, double StartsAt)>>(attack, "_screenFlashes")
                    .Where(flash => attack.Elapsed >= flash.StartsAt + .2))
                {
                    var boxes = new List<Rect2>();
                    Vector2 facing = Vector2.FromAngle(flash.Angle);
                    for (float y = Field.Top; y < Field.Bottom; y += 2)
                    {
                        float start = float.NaN;
                        for (float x = Field.Left; x <= Field.Right; x++)
                        {
                            Vector2 offset = new Vector2(x + .5f, y + 1) - origin;
                            float along = facing.Dot(offset);
                            bool covered = x < Field.Right && along >= 0 && Math.Abs(facing.Cross(offset)) <= along * Mathf.Tan(.30f);
                            if (covered && float.IsNaN(start)) start = x;
                            if (!covered && !float.IsNaN(start)) { boxes.Add(new Rect2(start, y, x - start, 2)); start = float.NaN; }
                        }
                    }
                    for (int i = 0; i < count; i++)
                    {
                        double age = attack.Elapsed + i * SurvivalSliceSeconds - flash.StartsAt - lead;
                        if (age < -.08 || age >= 2.2) continue;
                        foreach (var box in boxes) Box(i, box);
                    }
                }
                return slices;
            }
            foreach (var path in Read<List<KoharuBossTechnique.KoharuEasyPath>>(attack, "_easyPaths"))
            {
                if (path.Points.Length < 2 || attack.Elapsed < path.StartsAt + 1.1) continue;
                for (int i = 0; i < count; i++)
                {
                    double age = attack.Elapsed + i * SurvivalSliceSeconds - path.FiresAt;
                    if (age < -.1 || age >= path.Duration + .1) continue;
                    Vector2 before = Point(path.Points, (age - .06) / path.Duration);
                    Vector2 after = Point(path.Points, (age + .06) / path.Duration);
                    Add(i, before, after, path.HalfWidth);
                }
            }
            foreach (var sweep in Read<List<KoharuBossTechnique.KoharuEasySweep>>(attack, "_easySweeps"))
            {
                if (sweep.Points.Length < 2 || attack.Elapsed < sweep.StartsAt + 1.1) continue;
                for (int i = 0; i < count; i++)
                {
                    double at = attack.Elapsed + i * SurvivalSliceSeconds;
                    if (at < sweep.FiresAt - .12 || at >= sweep.EndsAt + .08) continue;
                    if (sweep.Mode is 3 or 4)
                    {
                        if (!sweep.PulseOffsets.Any(offset => at >= sweep.FiresAt + offset - .12 && at < sweep.FiresAt + offset + sweep.Duration + .08)) continue;
                        if (sweep.Mode == 4)
                        {
                            Vector2 intersection = new(sweep.EyeB.X, sweep.EyeA.Y);
                            Box(i, new Rect2(intersection - new Vector2(sweep.HalfWidth, sweep.HalfHeight), new Vector2(sweep.HalfWidth, sweep.HalfHeight) * 2));
                        }
                        else Add(i, sweep.Points[0], sweep.Points[^1], sweep.HalfWidth);
                    }
                    else
                    {
                        Vector2 p = Point(sweep.Points, (at - sweep.FiresAt) / sweep.Duration);
                        if (sweep.Mode is 1 or 2) Add(i, p, p, sweep.HalfWidth);
                        else Add(i, p - Vector2.Down * sweep.HalfHeight, p + Vector2.Down * sweep.HalfHeight, sweep.HalfWidth);
                    }
                }
            }
        }
        else if (attack is MinaBossTechnique)
        {
            foreach (var cue in Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>())
            {
                if (Read<bool>(cue, "Cancelled") || attack.Elapsed < Read<double>(cue, "StartsAt") + 1.4) continue;
                int mode = Read<int>(cue, "Mode");
                var points = Read<Vector2[]>(cue, "Points");
                double fire = Read<double>(cue, "FiresAt"), end = Read<double>(cue, "EndsAt");
                for (int i = 0; i < count; i++)
                {
                    double at = attack.Elapsed + i * SurvivalSliceSeconds;
                    if (at < fire - .12 || at >= end + .1) continue;
                    if (mode == 0)
                    {
                        var size = Read<Vector2>(cue, "Size");
                        float age = (float)(at - fire);
                        Vector2 p = points[0].MoveToward(points[1], age * Read<float>(cue, "Speed"));
                        float open = Mathf.SmoothStep(0, 1, Math.Clamp((age - .45f) / 1.1f, 0, 1));
                        bool hard = _game.Difficulty == GameManager.Diff.Hard;
                        Vector2 dimensions = new(Mathf.Lerp(hard ? size.X + 1 : 28, size.X, open), Mathf.Lerp(8, size.Y, open));
                        Vector2 margin = hard ? Vector2.Zero : Vector2.Right * 5.5f;
                        Box(i, new Rect2(p - dimensions * .5f - margin, dimensions + margin * 2));
                    }
                    else if (mode is 4 or 7)
                    {
                        if (Read<bool>(cue, "Exited")) continue;
                        if (mode == 4)
                        {
                            int release = Read<int>(cue, "ReleaseCue");
                            if (release >= 0)
                            {
                                var releasing = Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>().ElementAt(release);
                                if (Read<bool>(releasing, "Energized") || attack.Elapsed >= Read<double>(releasing, "StartsAt") + 1.4
                                    && at >= Read<double>(releasing, "FiresAt")) continue;
                            }
                            var route = Read<AttackCurveRoute>(cue, "Route");
                            float speed = Read<float>(cue, "Speed");
                            Add(i, route.At(Math.Max(0, (float)(at - fire - .05)) * speed),
                                route.At(Math.Max(0, (float)(at - fire + .05)) * speed), Read<float>(cue, "Radius"));
                        }
                        else
                        {
                            Vector2 p = points[0].MoveToward(points[1], (float)(at - fire) * Read<float>(cue, "Speed"));
                            Add(i, p, p, Read<float>(cue, "Radius"));
                        }
                    }
                    else foreach (var bounds in Read<Rect2[]>(cue, "Bounds")) Box(i, bounds);
                }
            }
        }
        return slices;
    }

    private static Vector2 Point(Vector2[] points, double progress)
    {
        float at = Math.Clamp((float)progress, 0, 1) * (points.Length - 1);
        int index = Math.Min((int)at, points.Length - 2);
        return points[index].Lerp(points[index + 1], at - index);
    }

    private void SurvivalAkariForecast(BossCharacterTechnique attack, SurvivalSlice[] slices)
    {
        var properties = new Dictionary<string, object>();
        var methods = new Dictionary<string, System.Reflection.MethodInfo>();
        T Property<T>(string name)
        {
            if (!properties.TryGetValue(name, out var value)) properties[name] = value = attack.GetType().GetProperty(name, Private)!.GetValue(attack)!;
            return (T)value;
        }
        T Invoke<T>(string name, params object[] args)
        {
            if (!methods.TryGetValue(name, out var method)) methods[name] = method = attack.GetType().GetMethod(name, Private)!;
            return (T)method.Invoke(attack, args)!;
        }
        void Box(int i, Rect2 bounds) => slices[i].Add(new(bounds.Position, bounds.End, 0, true));
        void Path(double fire, float radius, Func<double, Vector2> position, double end = double.PositiveInfinity)
        {
            for (int i = 0; i < slices.Length; i++)
            {
                double at = attack.Elapsed + i * SurvivalSliceSeconds;
                if (at < fire - .06 || at >= end + .06) continue;
                slices[i].Add(new(position(Math.Max(fire, at - .04)), position(Math.Min(end, Math.Max(fire, at + .04))), radius + .35f));
            }
        }
        int mechanic = Read<int>(attack, "_mechanic");
        double warning = Read<double>(attack, "_warning");
        float Width(int pattern) => Invoke<float>("EasyWarningHalfWidth", pattern);
        if (_game.IsLunatic && mechanic is 10 or 16)
        {
            T Batch<T>(object batch, string name) => (T)batch.GetType().GetProperty(name, Private)!.GetValue(batch)!;
            foreach (var batch in Read<IEnumerable>(attack, mechanic == 10 ? "_priorityBatches" : "_outboxBatches"))
            {
                if (attack.Elapsed < Batch<double>(batch, "StartsAt") + 1) continue;
                for (int i = 0; i < slices.Length; i++)
                {
                    double at = attack.Elapsed + i * SurvivalSliceSeconds;
                    double age = at - Batch<double>(batch, "FiresAt");
                    if (age < -.08 || at >= Batch<double>(batch, "EndsAt")) continue;
                    if (mechanic == 10)
                    {
                        bool acrossFirst = Batch<bool>(batch, "AcrossFirst");
                        float across = (float)(Math.Clamp(age, 0, 1.6) * (acrossFirst ? 70 : 24)
                            + Math.Max(0, age - 1.6) * (acrossFirst ? 24 : 70));
                        float down = (float)(Math.Clamp(age, 0, 1.6) * (acrossFirst ? 24 : 70)
                            + Math.Max(0, age - 1.6) * (acrossFirst ? 70 : 24));
                        int xDirection = Batch<int>(batch, "XDirection"), yDirection = Batch<int>(batch, "YDirection");
                        float x = (xDirection > 0 ? Field.Left - 12 : Field.Right + 12) + xDirection * across;
                        float y = (yDirection > 0 ? Field.Top - 12 : Field.Bottom + 12) + yDirection * down;
                        if (x >= Field.Left - 12 && x <= Field.Right + 12)
                            Box(i, new Rect2(x - 6, Field.Top + Batch<int>(batch, "Row") * Field.Height / 2, 12, Field.Height / 2));
                        if (y >= Field.Top - 12 && y <= Field.Bottom + 12)
                            Box(i, new Rect2(Field.Left + Batch<int>(batch, "Column") * Field.Width / 2, y - 6, Field.Width / 2, 12));
                    }
                    else
                    {
                        int direction = Batch<int>(batch, "Direction"), firstRow = Batch<int>(batch, "FirstRow");
                        for (int group = 0; group < 3; group++)
                        {
                            float distance = (20 + group * 25) * (float)Math.Clamp(age, 0, 1.7)
                                + (float)Math.Max(0, age - 4.7 - group * .9) * 87;
                            float x = (direction > 0 ? Field.Left - 12 : Field.Right + 12) + direction * distance;
                            if (x >= Field.Left - 12 && x <= Field.Right + 12)
                                Box(i, new Rect2(x - 6, Field.Top + (group + firstRow) % 3 * Field.Height / 3, 12, Field.Height / 3));
                        }
                    }
                }
            }
            return;
        }
        if (mechanic == 6)
        {
            double begins = Property<double>("RainAttackAt");
            double speed = Property<double>("RainAttackSpeed");
            if (attack.Elapsed < begins - .8) return;
            var positionMethod = attack.GetType().GetMethod("RainPosition", Private | System.Reflection.BindingFlags.Static)!;
            foreach (object particle in Read<IEnumerable>(attack, "_rainParticles"))
            {
                double fire = begins + Read<double>(particle, "Launch") / speed;
                double end = fire + Read<double>(particle, "Duration") / speed;
                if (end < attack.Elapsed || fire > attack.Elapsed + slices.Length * SurvivalSliceSeconds) continue;
                Path(fire, Width(0), at => (Vector2)positionMethod.Invoke(null, new object[] { particle, (at - fire) * speed })!, end);
            }
            return;
        }
        if (mechanic == 0)
        {
            double lead = ((AkariBossTechnique)attack).DeliveryForecastLead;
            for (int batch = 0; batch < 3; batch++)
            {
                double launch = Invoke<double>("EasyDeliveryLaunch", batch);
                if (attack.Elapsed < launch - lead + .2) continue;
                var paths = Invoke<AkariBossTechnique.DeliveryPath[]>("EasyDeliveryReturnPaths", batch);
                foreach (var path in paths)
                for (int burst = 0; burst < Property<int>("EasyDeliveryBursts"); burst++)
                {
                    double pulse = burst * Property<double>("EasyDeliveryBurstInterval");
                    double fire = launch + pulse;
                    double visibleUntil = attack.Elapsed + lead - .2 + pulse;
                    Path(fire, ((AkariBossTechnique)attack).DeliveryWarningHalfWidth(AkariBossTechnique.DeliveryKind.FallingDocument),
                        at => path.OrdinaryStart + Vector2.Down * (float)(at - fire) * 27, visibleUntil);
                    double express = fire + path.Delay;
                    if (attack.Elapsed < express - pulse - lead + .2) continue;
                    double reply = express + path.BounceAt;
                    Path(express, ((AkariBossTechnique)attack).DeliveryWarningHalfWidth(AkariBossTechnique.DeliveryKind.TurningEnvelope),
                        at => AkariBossTechnique.DeliverySample(path, true, at - express).Position, Math.Min(reply, visibleUntil));
                    if (attack.Elapsed < reply - pulse - lead + .2) continue;
                    Path(reply, ((AkariBossTechnique)attack).DeliveryWarningHalfWidth(path.Exit.Y > path.Bounce.Y
                            ? AkariBossTechnique.DeliveryKind.DescendingReply : AkariBossTechnique.DeliveryKind.AscendingReply),
                        at => AkariBossTechnique.DeliverySample(path, true, at - express).Position, visibleUntil);
                }
            }
        }
        else if (mechanic == 1)
        {
            for (int turn = 0; turn < 4; turn++)
            {
                double launch = Invoke<double>("EasyInboxLaunch", turn);
                if (attack.Elapsed < launch - 1.8) continue;
                foreach (var path in Invoke<IEnumerable<(Vector2 Start, Vector2 Exit)>>("EasyInboxWarningPaths", turn))
                for (int burst = 0; burst < Property<int>("EasyInboxBursts"); burst++)
                {
                    double fire = launch + burst * Property<double>("EasyInboxBurstInterval");
                    Path(fire, Width(1), at => path.Start + path.Start.DirectionTo(path.Exit) * (float)(at - fire) * 45);
                }
            }
        }
        else if (mechanic == 2)
        {
            int copies = Property<int>("EasyUnbindingCopiesPerRound");
            int round = Read<int>(attack, "_easyUnbindingRound");
            for (int copy = 0; copy < (round + 1) * copies; copy++)
            {
                if (attack.Elapsed < Invoke<double>("EasyUnbindingWarningStart", copy / copies) + 1.1) continue;
                int captured = copy;
                Vector2[] points = Invoke<Vector2[]>("EasyUnbindingCopyPoints", copy);
                Vector2 exit = Invoke<Vector2>("EasyUnbindingExit", copy);
                for (int burst = 0; burst < Property<int>("EasyUnbindingBurstCount"); burst++)
                {
                    double fire = Invoke<double>("EasyUnbindingBurstFire", copy, burst);
                    double release = Invoke<double>("EasyUnbindingBurstRelease", copy, burst);
                    Path(fire, Width(2), at => at < release
                        ? Invoke<Vector2>("EasyUnbindingRetracePoint", captured, Math.Clamp((float)((at - fire - .34) / .8), 0, 1))
                        : points[0] + points[0].DirectionTo(exit) * (float)(at - release) * 90);
                }
            }
        }
        else if (mechanic == 5)
        {
            double change = Property<double>("EasyPileSwitchAt");
            int rows = Property<int>("EasyPileRows");
            float height = Property<float>("EasyPileLayerHeight");
            double columnInterval = Property<double>("EasyPileColumnInterval");
            const double collapse = 2.1, interval = .16;
            for (int pass = 0; pass < 2; pass++) for (int column = 0; column < 7 + pass; column++)
            {
                int order = (int)_game.Difficulty >= 2 && pass == 1 ? 7 - column : column;
                double fire = warning + pass * change + order * columnInterval;
                if (attack.Elapsed < pass * change || attack.Elapsed < fire - warning + 1.4) continue;
                int index = (int)_game.Difficulty >= 2 ? pass * 7 + column : column;
                float width = Invoke<float>("EasyPileWidth", index);
                float x = (int)_game.Difficulty == 2 ? Invoke<float>("EasyPileX", index)
                    : Field.Left + (column + (pass == 0 ? .5f : 0)) * Field.Width / 7;
                for (int row = 0; row < rows; row++) for (int i = 0; i < slices.Length; i++)
                {
                    double at = attack.Elapsed + i * SurvivalSliceSeconds;
                    if (at < fire + row * .14 - .08 || at >= fire + collapse + (rows - row - 1) * interval + .08) continue;
                    float y = Math.Min(Field.Bottom - (row + 1) * height, -height + (float)(at - fire - row * .14) * 120);
                    Box(i, new Rect2(x - width / 2, y, width, height));
                }
            }
        }
        else if (mechanic == 7)
        {
            var launches = Read<double[]>(attack, "_easyWaveLaunches");
            var starts = Read<double[]>(attack, "_easyWaveWarningStarts");
            Vector2 origin = Read<Vector2>(attack, "_origin");
            double interval = Property<double>("EasyWavePulseInterval");
            double duration = Property<double>("EasyWaveDuration");
            for (int wave = 0; wave < 2; wave++)
            {
                if (launches[wave] < 0 || attack.Elapsed < starts[wave] + 1.9) continue;
                for (int pulse = 0; pulse < Property<int>("EasyWavePulses"); pulse++) for (int i = 0; i < slices.Length; i++)
                {
                    double age = attack.Elapsed + i * SurvivalSliceSeconds - launches[wave] - pulse * interval;
                    if (age < 0 || age >= duration) continue;
                    if (age < .3) slices[i].Add(new(origin, origin, 20));
                    float radius = Invoke<float>("EasyWaveRadius", age);
                    foreach (var range in Invoke<(int First, int End)[]>("EasyWaveLiveRanges", age))
                        for (int segment = range.First; segment < range.End; segment++)
                            slices[i].Add(new(Invoke<Vector2>("EasyWavePoint", wave, segment, radius),
                                Invoke<Vector2>("EasyWavePoint", wave, segment + 1, radius), 2));
                }
            }
        }
        else if (mechanic == 11)
        {
            if (attack.Elapsed >= 1.8)
                for (int i = 0; i < slices.Length; i++)
                {
                    double age = attack.Elapsed + i * SurvivalSliceSeconds - warning;
                    if (age < -.08) continue;
                    Vector2 p = Invoke<Vector2>("CarriedEnvelopePosition", Math.Max(0, age));
                    Box(i, new Rect2(p - new Vector2(18, 11), new Vector2(36, 22)));
                }
            for (int opening = 0; opening < Read<IList>(attack, "_stamps").Count; opening++)
            {
                double start = Invoke<double>("EasyApprovalWarningStart", opening);
                if (attack.Elapsed < start + .75) continue;
                foreach (var path in Invoke<IEnumerable<(Vector2 Start, Vector2 Exit)>>("EasyEnvelopeOpeningPaths", opening))
                for (int burst = 0; burst < Property<int>("EasyApprovalBursts"); burst++)
                {
                    double fire = Invoke<double>("EasyApprovalBurstFire", opening, burst)
                        + Mathf.Clamp(_boss.ShotCenter.DistanceTo(path.Start) / Bullet.LeadSpeed, .12f, .32f);
                    Path(fire, Width(2), at => path.Start + path.Start.DirectionTo(path.Exit) * (float)(at - fire) * 62);
                }
                if (attack.Elapsed < start + 1.1) continue;
                for (int i = 0; i < slices.Length; i++)
                {
                    double at = attack.Elapsed + i * SurvivalSliceSeconds;
                    if (at < start + 2.9 - .12 || at >= Invoke<double>("EasyApprovalLeave", opening) + .08) continue;
                    Vector2 p = Invoke<Vector2>("EasyApprovalStampPosition", opening);
                    slices[i].Add(new(p, p, 19.4f));
                    foreach (var path in Invoke<(Vector2 A, Vector2 B, float Radius)[]>("EasyApprovalStampPaths", opening))
                        slices[i].Add(new(path.A, path.B, path.Radius + .35f));
                }
            }
        }
        else if (mechanic == 12 && attack.Elapsed >= 1.8)
        {
            Vector2 origin = Read<Vector2>(attack, "_origin");
            for (int i = 0; i < slices.Length; i++)
            {
                double age = attack.Elapsed + i * SurvivalSliceSeconds - warning;
                if (age < -.12 || age >= 11.58) continue;
                for (int hand = 0; hand < Property<int>("EasyClockHands"); hand++)
                {
                    foreach (var stroke in Invoke<IEnumerable<(Vector2 A, Vector2 B)>>("ClockHandStrokes", Math.Max(0, age), hand))
                        slices[i].Add(new(stroke.A, stroke.B, Property<float>("EasyClockHalfWidth") + .6f));
                }
            }
        }
    }
}
