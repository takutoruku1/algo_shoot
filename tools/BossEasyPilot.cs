using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public partial class BossTechniqueVideo
{
    private string EasyPilotPolicy() => _easyAttack switch {
        KoharuBossTechnique => "normal input using completed curved-path forecasts and live hazards",
        MinaBossTechnique { TechniqueVariant: 1 } => "normal input along the current row's verified card access path, holding after acknowledgement",
        MinaBossTechnique { TechniqueVariant: 2 } => "normal input through announced airlock doors using body-safe lanes inside the actual protected room",
        MinaBossTechnique { TechniqueVariant: 3 } => "normal input to a nearby unrecorded cell after the next generation forecast completes, waiting for the previous live cells to clear",
        ReiBossTechnique { TechniqueKind: BossTechniqueKind.Finale } => "normal input guidance through the announced sentence margin",
        _ => "reactive bullet and live-hazard avoidance",
    };

    private bool EasyBodyAt(Vector2 point)
    {
        if (_boss.CollisionLayer == 0 || !_boss.Monitorable) return false;
        foreach (uint owner in _boss.GetShapeOwners())
        {
            if (_boss.IsShapeOwnerDisabled(owner)) continue;
            var transform = _boss.GlobalTransform * _boss.ShapeOwnerGetTransform(owner);
            for (int i = 0; i < _boss.ShapeOwnerGetShapeCount(owner); i++)
                if (_boss.ShapeOwnerGetShape(owner, i).Collide(transform, EasyPlayerCore, new Transform2D(0, point))) return true;
        }
        return false;
    }

    private bool EasyBodyAlong(Vector2 a, Vector2 b)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b)));
        for (int i = 0; i <= steps; i++) if (EasyBodyAt(a.Lerp(b, i / (float)steps))) return true;
        return false;
    }

    private Vector2[]? EasyBodyRoute(Vector2 target, Rect2 allowed)
    {
        Vector2 position = _player.GlobalPosition;
        var inside = allowed.Intersection(Field.Rect).Grow(-Player.HitRadius - .75f);
        if (!inside.HasPoint(position)) return null;
        target = target.Clamp(inside.Position + Vector2.One * .01f, inside.End - Vector2.One * .01f);
        var candidates = new List<Vector2[]> {
            new[] { position, target },
            new[] { position, new Vector2(position.X, target.Y), target },
            new[] { position, new Vector2(target.X, position.Y), target },
        };
        Rect2 body = default;
        bool hasBody = false;
        foreach (uint owner in _boss.GetShapeOwners())
        {
            if (_boss.IsShapeOwnerDisabled(owner)) continue;
            var transform = _boss.GlobalTransform * _boss.ShapeOwnerGetTransform(owner);
            for (int shape = 0; shape < _boss.ShapeOwnerGetShapeCount(owner); shape++)
            {
                var bounds = transform * _boss.ShapeOwnerGetShape(owner, shape).GetRect();
                body = hasBody ? body.Merge(bounds) : bounds;
                hasBody = true;
            }
        }
        if (hasBody)
        {
            body = body.Grow(Player.HitRadius + 1);
            foreach (float x in new[] { body.Position.X, body.End.X })
                candidates.Add(new[] { position, new Vector2(x, position.Y), new Vector2(x, target.Y), target });
            foreach (float y in new[] { body.Position.Y, body.End.Y })
                candidates.Add(new[] { position, new Vector2(position.X, y), new Vector2(target.X, y), target });
        }
        Vector2[]? chosen = null;
        float best = float.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            float length = 0;
            for (int segment = 1; segment < candidate.Length; segment++) length += candidate[segment - 1].DistanceTo(candidate[segment]);
            if (length >= best || candidate.Any(point => !inside.HasPoint(point))) continue;
            if (Enumerable.Range(1, candidate.Length - 1).Any(index => EasyBodyAlong(candidate[index - 1], candidate[index]))) continue;
            best = length;
            chosen = candidate;
        }
        return chosen;
    }

    private void EasyFollowRoute(Vector2[]? route, double delta)
    {
        Vector2 position = _player.GlobalPosition;
        Vector2 target = route?.Skip(1).FirstOrDefault(point => point.DistanceTo(position) > .05f, position) ?? position;
        Vector2 move = (target - position).LimitLength(_player.SlowestMoveSpeed * (float)delta);
        _pilot.Release();
        AutoplayInput.Move(move / (_player.MoveSpeed * (float)delta));
    }

    private void EasyGuideTo(Vector2 target, double delta)
        => EasyFollowRoute(EasyBodyRoute(target, Field.Rect), delta);

    private void EasyGuideInside(Rect2 destination, Rect2 allowed, double delta)
    {
        var inside = destination.Intersection(allowed).Grow(-Player.HitRadius - .75f);
        Vector2 position = _player.GlobalPosition;
        if (inside.HasPoint(position) && !EasyBodyAt(position)) { EasyFollowRoute(null, delta); return; }
        var targets = new List<Vector2> { position.Clamp(inside.Position + Vector2.One * .01f, inside.End - Vector2.One * .01f), inside.GetCenter() };
        foreach (float y in new[] { inside.Position.Y + .01f, inside.GetCenter().Y, inside.End.Y - .01f })
            foreach (float x in new[] { inside.Position.X + .01f, inside.GetCenter().X, inside.End.X - .01f }) targets.Add(new(x, y));
        foreach (var target in targets.OrderBy(point => point.DistanceSquaredTo(position)))
        {
            var route = EasyBodyRoute(target, allowed);
            if (route == null) continue;
            EasyFollowRoute(route, delta);
            return;
        }
        EasyFollowRoute(null, delta);
    }

    private object? _easyCardCue;
    private ulong _easyCardAttack;
    private int _easyCardWaypoint;
    private bool _easyCardHolding;
    private Vector2 _easyCardHoldAt;
    private ulong _easyGenerationAttack;
    private int _easyGenerationRound = -1;
    private Vector2? _easyGenerationTarget;

    private void EasyGuideGeneration(BossCharacterTechnique attack, double delta)
    {
        var cues = Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>()
            .Where(c => Read<int>(c, "Mode") == 3 && Read<double>(c, "StartsAt") <= attack.Elapsed).ToArray();
        if (cues.Length == 0) { EasyFollowRoute(null, delta); return; }
        int round = cues.Max(c => Read<int>(c, "Round"));
        if (_easyGenerationAttack != attack.GetInstanceId() || _easyGenerationRound != round)
        {
            _easyGenerationAttack = attack.GetInstanceId();
            _easyGenerationRound = round;
            _easyGenerationTarget = null;
        }
        var latest = cues.Where(c => Read<int>(c, "Round") == round).ToArray();
        var keys = latest.Select(c => Read<string>(c, "Key")).ToHashSet();
        if (attack.EasyCues.Where(c => keys.Contains(c.Key)).Any(c => c.Progress < 1))
        { EasyFollowRoute(null, delta); return; }
        var future = latest.SelectMany(c => Read<Rect2[]>(c, "Bounds"))
            .Select(bounds => bounds.Grow(Player.HitRadius + .75f)).ToArray();
        bool Safe(Vector2 point) => !future.Any(bounds => bounds.HasPoint(point));
        bool Clear(Vector2[] route)
        {
            for (int segment = 1; segment < route.Length; segment++)
            {
                Vector2 from = route[segment - 1], to = route[segment];
                int steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to)));
                for (int sample = 0; sample <= steps; sample++)
                    if (attack.DangerDistance(from.Lerp(to, sample / (float)steps), false) < .75f) return false;
            }
            return true;
        }
        double fire = latest.Min(c => Read<double>(c, "FiresAt"));
        Vector2 position = _player.GlobalPosition;
        var targets = _easyGenerationTarget is Vector2 held ? new[] { held }
            : Enumerable.Range(0, 35).Select(index => new Vector2(
                Field.Left + (index % 7 + .5f) * Field.Width / 7,
                Field.Top + (index / 7 + .5f) * Field.Height / 5))
                .OrderBy(point => point.DistanceSquaredTo(position)).ToArray();
        foreach (var target in targets)
        {
            if (!Safe(target)) continue;
            if (position.DistanceTo(target) <= .1f) { EasyFollowRoute(null, delta); return; }
            var route = EasyBodyRoute(target, Field.Rect);
            if (route == null || !Clear(route)) continue;
            float distance = 0;
            for (int segment = 1; segment < route.Length; segment++) distance += route[segment - 1].DistanceTo(route[segment]);
            if (attack.Elapsed + distance / _player.SlowestMoveSpeed + 2 * delta >= fire) continue;
            _easyGenerationTarget = target;
            EasyFollowRoute(route, delta);
            return;
        }
        EasyFollowRoute(null, delta);
    }

    private bool TryEasyGuidance(double delta)
    {
        var attack = _easyAttack;
        if (attack is MinaBossTechnique && attack.TechniqueVariant == 3)
        {
            EasyGuideGeneration(attack, delta);
            return true;
        }
        if (attack is MinaBossTechnique && attack.TechniqueVariant == 2)
        {
            var room = Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>().FirstOrDefault(c => Read<int>(c, "Mode") == 2);
            if (room == null) return false;
            var method = attack.GetType().GetMethod("EasyAirlockDoors", BindingFlags.Static | BindingFlags.NonPublic)!;
            var doors = ((Rect2 Entry, Rect2 Exit, double OpensAt, double ClosesAt)[])method.Invoke(null, new[] { room })!;
            var current = doors.FirstOrDefault(d => attack.Elapsed < d.ClosesAt + .4);
            var protectedMethod = attack.GetType().GetMethod("EasyRoomProtectedBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
            var allowed = (Rect2)protectedMethod.Invoke(null, new object[] { room, attack.Elapsed - Read<double>(room, "FiresAt") })!;
            var destination = current.Entry.Size == Vector2.Zero ? doors[^1].Exit
                : attack.Elapsed < current.OpensAt ? current.Entry : current.Exit;
            EasyGuideInside(destination, allowed, delta);
            return true;
        }
        if (attack is MinaBossTechnique && attack.TechniqueVariant == 1)
        {
            var cues = Read<IEnumerable>(attack, "_easyMemoryCues").Cast<object>()
                .Where(c => Read<int>(c, "Mode") == 1 && attack.Elapsed >= Read<double>(c, "StartsAt")
                    && attack.Elapsed < Read<double>(c, "EndsAt")).ToArray();
            if (cues.Length == 0) return false;
            int round = cues.Max(c => Read<int>(c, "Round"));
            var active = cues.Where(c => Read<int>(c, "Round") == round).ToArray();
            if (_easyCardAttack != attack.GetInstanceId() || _easyCardCue != null && Read<int>(_easyCardCue, "Round") != round)
            {
                _easyCardAttack = attack.GetInstanceId();
                _easyCardCue = null;
                _easyCardWaypoint = 1;
                _easyCardHolding = false;
            }
            var read = active.FirstOrDefault(c => Read<bool>(c, "Cancelled") && Read<double?>(c, "CancelledAt") > Read<double>(c, "StartsAt") + .001);
            if (read != null)
            {
                if (!_easyCardHolding) { _easyCardHoldAt = _player.GlobalPosition; _easyCardHolding = true; }
                _easyCardCue = read;
                EasyGuideTo(_easyCardHoldAt, delta);
                return true;
            }
            if (attack.Elapsed >= active.Min(c => Read<double>(c, "FiresAt")))
            {
                var cancelled = active.Where(c => Read<bool>(c, "Cancelled"))
                    .OrderBy(c => Math.Abs(Read<Rect2[]>(c, "Bounds")[0].GetCenter().Y - _player.GlobalPosition.Y)).First();
                EasyGuideInside(Read<Rect2[]>(cancelled, "Bounds")[0], Field.Rect, delta);
                return true;
            }
            var card = active.FirstOrDefault(c => Read<bool>(c, "CurrentRowCard") && !Read<bool>(c, "Cancelled"));
            if (card == null || !Read<bool>(card, "CardAccessVerified")) { EasyFollowRoute(null, delta); return true; }
            if (!ReferenceEquals(_easyCardCue, card))
            {
                _easyCardCue = card;
                _easyCardWaypoint = 1;
                _easyCardHolding = false;
            }
            var access = Read<Vector2[]>(card, "CardAccessPath");
            while (_easyCardWaypoint < access.Length && _player.GlobalPosition.DistanceTo(access[_easyCardWaypoint]) <= .1f) _easyCardWaypoint++;
            EasyGuideTo(_easyCardWaypoint < access.Length ? access[_easyCardWaypoint] : _player.GlobalPosition, delta);
            return true;
        }
        if (attack is ReiBossTechnique && attack.TechniqueKind == BossTechniqueKind.Finale)
        {
            var method = attack.GetType().GetMethod("EasySentenceGeometry", Private)!;
            var g = ((float Head, float Center, Rect2 Upper, Rect2 Lower, Rect2 Tail))method.Invoke(attack,
                new object[] { attack.Elapsed + .25 })!;
            Vector2 target = new(g.Head, g.Center);
            foreach (float offset in new[] { 0f, 24, -24, 28, -28 })
            {
                var candidate = new Vector2(g.Head, g.Center + offset);
                if (EasyBodyAt(candidate)) continue;
                target = candidate;
                break;
            }
            EasyGuideTo(target, delta);
            return true;
        }
        return false;
    }

    private void DriveEasyPilot(double delta)
    {
        if (TryEasyGuidance(delta)) return;
        _pilot.Update(delta, shoot: false, bombs: false, dodge: false);
        var attack = _easyAttack;
        if (attack == null || !attack.IsStriking && attack is not KoharuBossTechnique) return;
        Vector2 position = _player.GlobalPosition;
        var bullets = attack.OwnedShots.Where(b => attack is not KoharuBossTechnique && !b.LeadIn && b.CollisionLayer != 0)
            .Select(b => (At: b.GlobalPosition, b.Velocity, Radius: b.CollisionRadius
                * Math.Max(b.GlobalTransform.X.Length(), b.GlobalTransform.Y.Length()))).ToArray();
        var curves = attack is KoharuBossTechnique
            ? Read<IEnumerable>(attack, "_easyPaths").Cast<object>()
                .Where(path => Read<Vector2[]>(path, "Points").Length > 1 && attack.Elapsed >= Read<double>(path, "StartsAt") + .9)
                .Select(path => (Points: Read<Vector2[]>(path, "Points"), Fire: Read<double>(path, "FiresAt"),
                    Duration: Read<double>(path, "Duration"), Radius: Read<float>(path, "HalfWidth"))).ToArray()
            : Array.Empty<(Vector2[] Points, double Fire, double Duration, float Radius)>();
        var preferred = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        float best = float.NegativeInfinity;
        Vector2 chosen = Vector2.Zero;
        for (int direction = -2; direction < 16; direction++)
        {
            Vector2 move = direction == -2 ? preferred : direction == -1 ? Vector2.Zero : Vector2.FromAngle(direction * Mathf.Tau / 16);
            float minimum = float.MaxValue, total = 0;
            for (int step = 1; step <= 8; step++)
            {
                float time = step * .075f;
                Vector2 at = (position + move * _player.MoveSpeed * time).Clamp(
                    Field.Rect.Position + Vector2.One * 6, Field.Rect.End - Vector2.One * 6);
                float gap = attack.DangerDistance(at, false);
                foreach (var bullet in bullets) gap = Math.Min(gap, at.DistanceTo(bullet.At + bullet.Velocity * time) - bullet.Radius - Player.HitRadius);
                foreach (var curve in curves)
                {
                    double age = attack.Elapsed + time - curve.Fire;
                    if (age < 0 || age >= curve.Duration) continue;
                    float sample = (float)(age / curve.Duration) * (curve.Points.Length - 1);
                    int index = Math.Min((int)sample, curve.Points.Length - 2);
                    Vector2 future = curve.Points[index].Lerp(curve.Points[index + 1], sample - index);
                    gap = Math.Min(gap, at.DistanceTo(future) - curve.Radius - Player.HitRadius);
                }
                if (EasyBodyAt(at)) gap = Math.Min(gap, -20);
                minimum = Math.Min(minimum, gap);
                total += Mathf.Clamp(gap, -30, 24);
            }
            float score = Mathf.Clamp(minimum, -30, 24) + total * .12f - move.DistanceTo(preferred) * .1f;
            if (score > best) { best = score; chosen = move; }
        }
        AutoplayInput.Move(chosen);
    }
}
