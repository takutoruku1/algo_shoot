using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AkariBossTechnique
{
    private static Texture2D? _easyMessageArt, _easyExpiryMarkerArt;
    private static readonly Color EasyGuide = new("8297ab"), EasyOutline = new("101829"),
        EasyDanger = new("f66d83"), EasyLight = new("fff3f2"), EasyWaveGuide = new("c8efff"), EasyExpiryBlue = new("369cff");
    private static readonly Dictionary<Texture2D, Vector2[][]> EasyArtContours = new();
    private readonly Vector2[] _easyRingPoints = new Vector2[65];
    private readonly Vector2[] _easyRingInner = new Vector2[65], _easyRingOuter = new Vector2[65];
    private readonly Vector2?[] _easyExpiryMarkerCenters = new Vector2?[2];

    internal bool UsesEasyReadabilityRules => Kind != BossTechniqueKind.Cameo && _mechanic is 0 or 1 or 2 or 5 or 6 or 7 or 11 or 12;
    protected override bool UsesCustomHazardArt => UsesEasyReadabilityRules;
    private static Texture2D EasyArt(ref Texture2D? cached, string name)
    {
        if (cached != null) return cached;
        var texture = GD.Load<Texture2D>($"res://char/v3/fx/akari/{name}_v1.png");
        return cached = new AtlasTexture { Atlas = texture, Region = UiKit.ContentRect(texture), FilterClip = true };
    }

    private void DrawEasyWarningSegment(Vector2 from, Vector2 to, Color color, ref float travelled, bool highContrast = false, float width = 1)
    {
        float length = from.DistanceTo(to);
        if (length == 0) return;
        Vector2 direction = (to - from) / length;
        int first = Mathf.FloorToInt(travelled / 4);
        int last = Mathf.FloorToInt((travelled + length) / 4);
        for (int dash = first; dash <= last; dash++)
        {
            float start = Mathf.Max(0, dash * 4 - travelled);
            float end = Mathf.Min(length, dash * 4 + 2 - travelled);
            if (end <= start) continue;
            Vector2 a = from + direction * start, b = from + direction * end;
            if (highContrast) DrawEasyClippedLine(a, b, EasyOutline, width + 1.5f);
            DrawEasyClippedLine(a, b, color, highContrast ? width : width * .65f);
        }
        travelled += length;
    }

    private void DrawEasyWarningArt(Texture2D art, Rect2 bounds, Color color, bool highContrast = false)
    {
        if (!EasyArtContours.TryGetValue(art, out var contours))
        {
            using var image = art.GetImage();
            if (image.IsCompressed()) image.Decompress();
            float resize = 128f / Mathf.Max(image.GetWidth(), image.GetHeight());
            image.Resize(Mathf.RoundToInt(image.GetWidth() * resize), Mathf.RoundToInt(image.GetHeight() * resize));
            using var mask = new Bitmap();
            mask.CreateFromImageAlpha(image, .12f);
            Vector2 size = image.GetSize();
            contours = mask.OpaqueToPolygons(new Rect2I(Vector2I.Zero, image.GetSize()), 1.5f)
                .Select(polygon => polygon.Select(point => point / size).ToArray()).ToArray();
            EasyArtContours[art] = contours;
        }
        foreach (var contour in contours)
        {
            float travelled = 0;
            for (int i = 0; i < contour.Length; i++)
                DrawEasyWarningSegment(bounds.Position + contour[i] * bounds.Size,
                    bounds.Position + contour[(i + 1) % contour.Length] * bounds.Size, color, ref travelled, highContrast);
        }
    }

    private void DrawEasyWarningArc(Vector2 center, float radius, float from, float to, Color color, bool highContrast = false)
    {
        int segments = Mathf.CeilToInt(Mathf.Abs(to - from) * radius / 3);
        float travelled = 0;
        for (int i = 0; i < segments; i++)
            DrawEasyWarningSegment(center + Vector2.FromAngle(Mathf.Lerp(from, to, i / (float)segments)) * radius,
                center + Vector2.FromAngle(Mathf.Lerp(from, to, (i + 1) / (float)segments)) * radius, color, ref travelled, highContrast);
    }

    private void DrawEasyTechniqueArt()
    {
        switch (_mechanic)
        {
            case 5: DrawEasyPaperPiles(); break;
            case 6: DrawEasyRainSplash(); break;
            case 7: DrawEasySilentWave(); break;
            default:
                DrawEasyRuleHazards(false);
                DrawEasyMechanicDirections();
                DrawEasyRuleHazards(true);
                DrawOfficeTechniqueArt();
                DrawEasyLetterOutlines();
                break;
        }
    }

    private void DrawEasyRuleHazards(bool live)
    {
        foreach (var h in HazardShapes.Where(hazard => hazard.Live == live))
        {
            if (_mechanic == 0 && !live) continue;
            if (_mechanic == 12 && Time < _warning && !h.Live) continue;
            if (h.Shape == 2)
            {
                var rect = new Rect2(h.A, h.B - h.A).Intersection(Field.Rect);
                if (h.Live) DrawEasyDangerBox(rect);
                else DrawEasyWarningBox(rect, EasyDanger);
            }
            else if (h.Shape == 1)
            {
                if (h.Live)
                {
                    DrawEasyClippedCircle(h.A, h.Radius, new Color(EasyDanger, .3f));
                    DrawArc(h.A, h.Radius, 0, Mathf.Tau, 40, EasyDanger, 1.2f, true);
                }
                else DrawEasyWarningArc(h.A, h.Radius, 0, Mathf.Tau, EasyDanger, true);
            }
            else if (h.Shape == 0)
            {
                if (h.Live)
                {
                    DrawEasyClippedLine(h.A, h.B, EasyOutline, h.Radius * 2 + 1.4f);
                    DrawEasyClippedCircle(h.A, h.Radius + .7f, EasyOutline);
                    DrawEasyClippedCircle(h.B, h.Radius + .7f, EasyOutline);
                    DrawEasyClippedLine(h.A, h.B, EasyDanger, h.Radius * 2);
                    DrawEasyClippedCircle(h.A, h.Radius, EasyDanger);
                    DrawEasyClippedCircle(h.B, h.Radius, EasyDanger);
                    DrawEasyClippedLine(h.A, h.B, EasyLight, .45f);
                }
                else
                {
                    Vector2 side = h.A.DirectionTo(h.B).Orthogonal() * h.Radius;
                    EasyRedDashedLine(h.A + side, h.B + side);
                    EasyRedDashedLine(h.A - side, h.B - side);
                    if (_mechanic is not (2 or 3 or 9 or 12)) DrawEasyWarningDirections(h.A, h.B);
                }
            }
        }
    }

    private void EasyRedDashedLine(Vector2 from, Vector2 to)
    {
        Vector2 direction = from.DirectionTo(to);
        float length = from.DistanceTo(to);
        for (float at = 0; at < length; at += 5)
        {
            Vector2 a = from + direction * at, b = from + direction * Math.Min(at + 2.5f, length);
            DrawEasyClippedLine(a, b, EasyOutline, 2.5f);
            DrawEasyClippedLine(a, b, EasyDanger, 1);
        }
    }

    private void EasyDirectionArrow(Vector2 point, Vector2 direction, int count = 1)
    {
        if (direction == Vector2.Zero) return;
        Vector2 side = direction.Orthogonal();
        for (int i = 0; i < count; i++)
        {
            Vector2 tip = point - direction * (i * 4);
            DrawEasyClippedLine(tip - direction * 3 + side * 2.5f, tip, EasyOutline, 2.8f);
            DrawEasyClippedLine(tip, tip - direction * 3 - side * 2.5f, EasyOutline, 2.8f);
            DrawEasyClippedLine(tip - direction * 3 + side * 2.5f, tip, EasyDanger, 1.1f);
            DrawEasyClippedLine(tip, tip - direction * 3 - side * 2.5f, EasyDanger, 1.1f);
        }
    }

    private void DrawEasyWarningDirections(Vector2 from, Vector2 to)
    {
        float length = from.DistanceTo(to);
        Vector2 direction = from.DirectionTo(to);
        for (float at = 20; at < length - 6; at += 32)
            EasyDirectionArrow(from + direction * at, direction, 2);
    }

    private void DrawEasyTurnMark(Vector2 center, Vector2 incoming, Vector2 outgoing, float alpha, float glint = 0)
    {
        float radius = 3.5f + glint;
        var red = new Color(EasyDanger, alpha);
        var white = new Color(EasyLight, alpha);
        foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
        {
            Vector2 at = center + corner * radius;
            Vector2 horizontal = at - new Vector2(corner.X * 1.8f, 0);
            Vector2 vertical = at - new Vector2(0, corner.Y * 1.8f);
            DrawEasyClippedLine(horizontal, at, new Color(EasyOutline, alpha), 1.8f);
            DrawEasyClippedLine(at, vertical, new Color(EasyOutline, alpha), 1.8f);
            DrawEasyClippedLine(horizontal, at, glint > 0 ? white : red, .85f);
            DrawEasyClippedLine(at, vertical, glint > 0 ? white : red, .85f);
        }
        Vector2 crease = (incoming - outgoing).Normalized().Orthogonal() * 2.3f;
        DrawEasyClippedLine(center - crease, center + crease, new Color(EasyOutline, alpha), 1.6f);
        DrawEasyClippedLine(center - crease, center + crease, white, .7f);
    }

    private void DrawEasyClipTurnGroup(Vector2[] points, float alpha, float glint = 0)
    {
        if (points.Length > 1 && points[0].DistanceTo(points[1]) < 8)
        {
            Vector2 center = points[points.Length / 2], radial = _origin.DirectionTo(center);
            foreach (Vector2 point in points)
            {
                DrawEasyClippedLine(center, point, new Color(EasyOutline, alpha), 1.5f);
                DrawEasyClippedLine(center, point, new Color(EasyLight, alpha), .55f);
                Vector2 crease = _origin.DirectionTo(point).Orthogonal() * 1.1f;
                DrawEasyClippedLine(point - crease, point + crease, new Color(EasyOutline, alpha), 1.5f);
                DrawEasyClippedLine(point - crease, point + crease, new Color(EasyLight, alpha), .55f);
            }
            DrawEasyTurnMark(center, -radial, radial, alpha, glint);
            return;
        }
        foreach (Vector2 point in points)
        {
            Vector2 radial = _origin.DirectionTo(point);
            DrawEasyTurnMark(point, -radial, radial, alpha, glint);
        }
    }

    private void DrawEasyWarningRoute(Vector2[] points, float radius, Color color, bool drawDirections)
    {
        float leftTravel = 0, rightTravel = 0, pathTravel = 0, nextMark = 20;
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 a = points[i - 1], b = points[i], direction = a.DirectionTo(b), side = direction.Orthogonal() * radius;
            DrawEasyWarningSegment(a + side, b + side, color, ref leftTravel);
            DrawEasyWarningSegment(a - side, b - side, color, ref rightTravel);
            float length = a.DistanceTo(b);
            while (drawDirections && nextMark < pathTravel + length)
            {
                EasyDirectionArrow(a + direction * (nextMark - pathTravel), direction, 2);
                nextMark += 32;
            }
            pathTravel += length;
        }
    }

    private void DrawEasyLetterOutlines()
    {
        foreach (var letter in _letters.Where(letter => letter.Alive && !letter.Shot.LeadIn && letter.Shot.CollisionLayer != 0))
        {
            var shot = letter.Shot;
            var footprint = shot.Footprint;
            if (footprint == null) continue;
            if (footprint.Contours.Length == 0)
                DrawArc(shot.GlobalPosition, shot.CollisionRadius, 0, Mathf.Tau, 32, EasyDanger, .65f, true);
            foreach (var contour in footprint.Contours)
                for (int i = 0; i < contour.Length; i++)
                    DrawEasyClippedLine(shot.GlobalTransform * contour[i], shot.GlobalTransform * contour[(i + 1) % contour.Length], EasyDanger, .65f);
        }
    }

    private void DrawEasyMechanicDirections()
    {
        if (_mechanic == 0)
        {
            for (int batch = 0; batch < 3; batch++)
            {
                if (!_easyDeliveryReserved[batch]) continue;
                var paths = EasyDeliveryReturnPaths(batch);
                if (Time < EasyDeliveryLaunch(batch))
                {
                    float progress = EasyDeliveryWarningProgress(batch);
                    foreach (var ordinary in EasyDeliveryWarningPaths(batch))
                    {
                        Vector2 end = ordinary.Start.Lerp(ordinary.Exit, Math.Min(1, progress * 3));
                        DrawEasyWarningRoute(new[] { ordinary.Start, end }, EasyWarningHalfWidth(1), EasyDanger, false);
                        DrawEasyDeliveryDirections(ordinary.Start, end);
                    }
                    foreach (var path in paths)
                    {
                        DrawEasyWaveSpawnMark(path.Start, Mathf.Pi / 2, 1);
                        var prefix = EasyRainWarningPathPrefix(new[] { path.Start, path.Turn, path.Bounce, path.Exit }, progress);
                        for (int i = 2; i < prefix.Length; i++)
                        {
                            DrawEasyWarningRoute(new[] { prefix[i - 1], prefix[i] }, EasyWarningHalfWidth(1), EasyDanger, false);
                            DrawEasyDeliveryDirections(prefix[i - 1], prefix[i]);
                        }
                        if (progress >= 1)
                        {
                            DrawEasyTurnMark(path.Turn, Vector2.Down, path.Turn.DirectionTo(path.Bounce), .9f);
                            DrawEasyTurnMark(path.Bounce, path.Turn.DirectionTo(path.Bounce), path.Bounce.DirectionTo(path.Exit), .9f);
                        }
                    }
                }
                else
                    for (int lane = 0; lane < paths.Length; lane++)
                    {
                        var path = paths[lane];
                        var letters = _letters.Where(letter => letter.Group < 100 && letter.Group % 9 == batch * 3 + lane).ToArray();
                        if (Time < EasyDeliveryLaunch(batch) + (path.Turn.Y - path.Start.Y) * (1.0 / 27 - 1.0 / 88)
                            + (EasyDeliveryBursts - 1) * EasyDeliveryBurstInterval
                            || letters.Any(letter => letter.Alive && letter.State == 0))
                            DrawEasyTurnMark(path.Turn, Vector2.Down, path.Turn.DirectionTo(path.Bounce), .55f);
                        if (letters.Length < EasyDeliveryBursts || letters.Any(letter => letter.Alive && letter.State < 2))
                            DrawEasyTurnMark(path.Bounce, path.Turn.DirectionTo(path.Bounce), path.Bounce.DirectionTo(path.Exit), .55f);
                        foreach (var letter in letters.Where(letter => letter.State > 0 && Time - letter.Changed < .12))
                        {
                            Vector2 incoming = letter.State == 1 ? Vector2.Down : path.Turn.DirectionTo(path.Bounce);
                            Vector2 outgoing = letter.State == 1 ? path.Turn.DirectionTo(path.Bounce) : path.Bounce.DirectionTo(path.Exit);
                            DrawEasyTurnMark(letter.TurnPoint, incoming, outgoing, 1 - (float)((Time - letter.Changed) / .12), 1);
                        }
                    }
            }
        }
        else if (_mechanic == 1)
        {
            for (int turn = 0; turn < 4; turn++)
            {
                double launch = EasyInboxLaunch(turn);
                if (!_easyInboxReserved[turn] || Time < launch - EasyInboxWarningLead || Time >= launch) continue;
                var path = EasyInboxWarningPaths(turn).ElementAt(2);
                DrawEasyWaveSpawnMark(path.Start, path.Start.DirectionTo(path.Exit).Angle(), 1);
            }
        }
        else if (_mechanic == 2) DrawEasyUnbindingDirections();
        else if (_mechanic == 11)
        {
            var path = EasyEnvelopeTravelPath();
            if (Time < _warning)
            {
                var prefix = EasyRainWarningPathPrefix(path, EasyEnvelopeTravelWarningProgress());
                DrawEasyWaveSpawnMark(path[0], path[0].DirectionTo(path[1]).Angle(), 1);
                for (int i = 1; i < prefix.Length; i++)
                {
                    EasyRedDashedLine(prefix[i - 1], prefix[i]);
                    DrawEasyWarningDirections(prefix[i - 1], prefix[i]);
                }
                for (int i = 1; i < prefix.Length - 1; i++)
                    DrawEasyTurnMark(path[i], path[i - 1].DirectionTo(path[i]), path[i].DirectionTo(path[i + 1]), .9f);
            }
            else
                for (int i = 1; i < path.Length - 1; i++)
                {
                    double age = Time - EasyApprovalLeave(i - 1);
                    if (age >= .12) continue;
                    DrawEasyTurnMark(path[i], path[i - 1].DirectionTo(path[i]), path[i].DirectionTo(path[i + 1]),
                        age < 0 ? .55f : 1 - (float)(age / .12), age < 0 ? 0 : 1);
                }
            for (int opening = 0; opening < _stamps.Count; opening++)
            {
                if (Time < EasyApprovalWarningStart(opening) || Time >= EasyApprovalStampFire(opening)) continue;
                if (Time < EasyApprovalOpeningFire(opening))
                {
                    var ray = EasyEnvelopeOpeningPaths(opening).ElementAt(2);
                    DrawEasyWaveSpawnMark(ray.Start, ray.Start.DirectionTo(ray.Exit).Angle(), 1);
                }
                var stamps = EasyApprovalStampPaths(opening);
                DrawEasyWaveSpawnMark(stamps[0].A, stamps[0].A.DirectionTo(stamps[0].B).Angle(), 1);
                if (EasyApprovalStampWarningProgress(opening).Frame >= 1)
                    DrawEasyWaveSpawnMark(stamps[4].A, stamps[4].A.DirectionTo(stamps[4].B).Angle(), 1);
            }
        }
        else if (_mechanic == 12)
        {
            DrawEasyClippedCircle(_origin, 3, EasyOutline);
            DrawEasyClippedCircle(_origin, 1.4f, EasyLight);
            if (Time < _warning)
            {
                float progress = EasyClockWarningProgress();
                Vector2 radial = Vector2.FromAngle(_angle), start = _origin + radial * 12;
                DrawEasyWaveSpawnMark(start, _angle + Mathf.Pi / 2, 1);
                if (progress <= 0) return;
                var outline = EasyClockWarningSector(progress);
                float travelled = 0;
                for (int i = 1; i < outline.Length; i++)
                    DrawEasyWarningSegment(outline[i - 1], outline[i], EasyDanger, ref travelled, true, 6 + Tier * 1.3f);
                for (int mark = 0; mark < 12; mark++)
                {
                    float amount = mark / 12f;
                    if (amount >= progress) break;
                    Vector2 direction = Vector2.FromAngle(_angle + amount * Mathf.Tau);
                    EasyDirectionArrow(_origin + direction * 30, -direction.Orthogonal(), 2);
                }
                EasyRedDashedLine(start, _origin + radial * 44);
            }
        }
    }
    private void DrawEasyDeliveryDirections(Vector2 from, Vector2 to)
    {
        Vector2 direction = from.DirectionTo(to);
        for (float distance = 28; distance < from.DistanceTo(to) - 8; distance += 64)
            EasyDirectionArrow(from + direction * distance, direction, 2);
    }

    private void DrawEasyUnbindingDirections()
    {
        int round = EasyUnbindingRound;
        if (round < 0) return;
        int first = round * EasyUnbindingCopiesPerRound;
        if (Time < EasyUnbindingFire(first))
        {
            var progress = EasyUnbindingWarningProgress(round);
            for (int copy = first; copy < first + EasyUnbindingCopiesPerRound; copy++)
            {
                var points = EasyUnbindingCopyPoints(copy);
                Vector2 release = points[0], exit = EasyUnbindingExit(copy);
                Vector2 incoming = points.FirstOrDefault(point => point.DistanceSquaredTo(release) > .01f, release).DirectionTo(release);
                if (incoming == Vector2.Zero) incoming = release.DirectionTo(exit);
                Vector2 initial = points[^1].DirectionTo(points.Reverse()
                    .FirstOrDefault(point => point.DistanceSquaredTo(points[^1]) > .01f, points[^1]));
                if (initial == Vector2.Zero) initial = incoming;
                DrawEasyWaveSpawnMark(points[^1], initial.Angle(), 1);
                var prefix = EasyRainWarningPathPrefix(points.Reverse().ToArray(), progress.Retrace);
                for (int index = 1; index < prefix.Length; index++) DrawEasyWarningDirections(prefix[index - 1], prefix[index]);
                if (progress.Exit > 0) DrawEasyWarningDirections(release, release.Lerp(exit, progress.Exit));
                if (progress.Retrace >= 1) DrawEasyTurnMark(release, incoming, release.DirectionTo(exit), .9f);
            }
        }
        foreach (var letter in _letters.Where(letter => letter.Alive))
        {
            int copy = EasyUnbindingCopyForGroup(letter.Group);
            var points = EasyUnbindingCopyPoints(copy);
            Vector2 release = points[0], outgoing = release.DirectionTo(EasyUnbindingExit(copy));
            Vector2 incoming = points.FirstOrDefault(point => point.DistanceSquaredTo(release) > .01f, release).DirectionTo(release);
            if (incoming == Vector2.Zero) incoming = outgoing;
            if (letter.State == 0) DrawEasyTurnMark(release, incoming, outgoing, .55f);
            else if (Time - letter.Changed < .12)
                DrawEasyTurnMark(letter.TurnPoint, incoming, outgoing, 1 - (float)((Time - letter.Changed) / .12), 1);
        }
    }

    private void DrawEasyPaperPiles()
    {
        for (int column = 0; column < EasyPileColumnCount; column++)
        {
            double start = EasyPileStart(column);
            double age = Time - start;
            int remaining = EasyPileRemaining(column);
            if (remaining == 0) continue;
            if (Time >= start - _warning && Time < start)
            {
                float progress = EasyPileWarningProgress(column);
                var guide = new Color(EasyDanger, .55f + progress * .20f);
                var future = new Rect2(EasyPileX(column) - EasyPileWidth / 2,
                    Field.Bottom - EasyPileRows * EasyPileLayerHeight, EasyPileWidth, EasyPileRows * EasyPileLayerHeight).Intersection(Field.Rect);
                float x = EasyPileX(column);
                EasyRedDashedLine(new(x, Field.Top), new(x, Field.Top + 4));
                DrawEasyWaveSpawnMark(new(x, Field.Top + 4), Mathf.Pi / 2, 1);
                if (progress > 0)
                {
                    float bottom = Mathf.Lerp(Field.Top, Field.Bottom, progress);
                    DrawEasyWarningBox(new Rect2(new Vector2(future.Position.X, Field.Top),
                        new Vector2(future.Size.X, bottom - Field.Top)), guide, false);
                    if (bottom > future.Position.Y)
                        DrawEasyWarningBox(new Rect2(future.Position,
                            new Vector2(future.Size.X, bottom - future.Position.Y)), EasyDanger);
                    EasyRedDashedLine(new(x, Field.Top), new(x, bottom));
                    DrawEasyWarningDirections(new(x, Field.Top), new(x, bottom));
                }
            }
            if (_states[column] > 0) DrawEasyDangerBox(EasyPileStackBounds(column));
            for (int row = 0; row < remaining; row++)
            {
                if (Time < start + row * EasyPileRowInterval) continue;
                Rect2 bounds = EasyPileSheetBounds(column, row).Intersection(Field.Rect);
                if (bounds.End.Y <= Field.Top) continue;
                if (row >= _states[column]) DrawEasyDangerBox(bounds);
                float shake = age > EasyPileCollapseAt - .8 ? Mathf.Sin((float)age * 26 + row + column) * .6f : 0;
                Rect2 outside = row >= _states[column] ? bounds : EasyPileStackBounds(column);
                DrawEasyPaperSurface(bounds.Intersection(outside.Grow(-2)), row, column, shake);
            }
        }
    }

    private void DrawEasyPaperSurface(Rect2 face, int row, int column, float shake)
    {
        if (face.Size.X <= 0 || face.Size.Y <= 0) return;
        var paper = new Color((row + column) % 2 == 0 ? "edf3f5" : "f4efee");
        var ink = new Color("91aabe");
        var layers = new Color("b2c2ce");
        DrawRect(face, paper);
        if (face.Size.X < 12 || face.Size.Y < 8) return;
        float left = face.Position.X, right = face.End.X, top = face.Position.Y, bottom = face.End.Y;
        for (int layer = 3; layer >= 0; layer--)
        {
            float y = Mathf.Lerp(top + 1, bottom - .6f, layer / 3f);
            float inset = .4f + ((layer + row + column) % 2) * .7f;
            DrawLine(new(left + inset, y), new(right - inset, y), layer == 0 ? paper : layers, 1.6f, true);
            if (layer > 0) DrawLine(new(left + inset, y + .7f), new(right - inset, y + .7f), ink, .5f, true);
        }
        float faceBottom = Mathf.Min(bottom - 1, top + 7.5f);
        var sheet = new[] { new Vector2(left + .6f, top), new Vector2(right - 1.2f, top + .7f),
            new Vector2(right - .4f, faceBottom), new Vector2(left + 1.3f, faceBottom - .7f) };
        DrawEasyConvexFan(sheet, paper);
        for (int line = 0; line < 2; line++)
        {
            float y = top + 2.4f + line * 2;
            float length = Mathf.Min(face.Size.X - 7, 13 - (row + column + line) % 3);
            DrawLine(new(left + 2.5f + shake, y), new(left + 2.5f + length + shake, y), ink, .65f, true);
        }
        DrawRect(new Rect2(right - 5, top + .5f, 2.2f, 2.8f), new Color("e76b75"));
    }

    private void DrawEasyWarningBox(Rect2 bounds, Color color, bool highContrast = true)
    {
        float travelled = 0;
        Vector2 a = bounds.Position, b = new(bounds.End.X, bounds.Position.Y), c = bounds.End, d = new(bounds.Position.X, bounds.End.Y);
        DrawEasyWarningSegment(a, b, color, ref travelled, highContrast);
        DrawEasyWarningSegment(b, c, color, ref travelled, highContrast);
        DrawEasyWarningSegment(c, d, color, ref travelled, highContrast);
        DrawEasyWarningSegment(d, a, color, ref travelled, highContrast);
    }

    private void DrawEasyDangerBox(Rect2 bounds)
    {
        float minimum = Mathf.Min(bounds.Size.X, bounds.Size.Y);
        if (minimum <= 0) return;
        DrawRect(bounds, EasyOutline);
        DrawRect(bounds.Grow(-Mathf.Min(1, minimum * .2f)), EasyDanger);
        DrawRect(bounds.Grow(-Mathf.Min(1.25f, minimum * .3f)), EasyLight, false, Mathf.Min(.6f, minimum * .2f));
    }

    private Vector2[] EasyRainSplashWarningPath(Vector2 impact, int side)
    {
        var bounds = Field.Rect.Grow(24);
        float horizontal = ((side > 0 ? bounds.End.X : bounds.Position.X) - impact.X) / (side * 50);
        float vertical = (EasySplashRise + Mathf.Sqrt(EasySplashRise * EasySplashRise
            + 2 * EasySplashGravity * (bounds.End.Y - impact.Y))) / EasySplashGravity;
        float duration = Math.Min(horizontal, vertical);
        int samples = Mathf.CeilToInt(duration / .08f);
        var path = new Vector2[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            float t = duration * i / samples;
            path[i] = impact + new Vector2(side * 50 * t, -EasySplashRise * t + EasySplashGravity * t * t / 2);
        }
        return path;
    }

    private static Vector2[] EasyRainWarningPathPrefix(Vector2[] points, float progress)
    {
        if (progress >= 1) return points;
        float remaining = 0;
        for (int i = 1; i < points.Length; i++) remaining += points[i - 1].DistanceTo(points[i]);
        remaining *= progress;
        var prefix = new List<Vector2> { points[0] };
        for (int i = 1; i < points.Length && remaining > 0; i++)
        {
            float length = points[i - 1].DistanceTo(points[i]);
            prefix.Add(points[i - 1].Lerp(points[i], Math.Min(1, remaining / length)));
            remaining -= length;
        }
        return prefix.ToArray();
    }

    private void DrawEasyRainSplash()
    {
        int next = EasyRainNextRow;
        float radius = EasyWarningHalfWidth(0);
        if (next >= 0)
        {
            int wave = next / EasyRainRows, row = next % EasyRainRows;
            float progress = EasyRainDropWarningProgress(wave, row);
            for (int column = 0; column < EasyRainColumns; column++)
            {
                Vector2 origin = EasyRainOrigin(wave, row, column), impact = EasyRainImpact(wave, row, column);
                if (progress > 0)
                    DrawEasyWarningRoute(new[] { origin, origin.Lerp(impact, progress) }, radius, new Color(EasyDanger, .65f), true);
                DrawEasyWaveSpawnMark(origin, Mathf.Pi / 2, 1);
            }
        }
        foreach (var splash in _letters.Where(letter => letter.Alive && letter.Group == 10 && Time - letter.Born < .12)
            .GroupBy(letter => (letter.Born, letter.Origin)).Select(group => group.First()))
            DrawEasyTurnMark(splash.Origin, Vector2.Down, Vector2.Up, 1 - (float)((Time - splash.Born) / .12), 1);
        if (EasyRainImpactWarning() is not { } warning) return;
        foreach (Vector2 impact in warning.Points)
        {
            DrawEasyTurnMark(impact, Vector2.Down, Vector2.Up, .85f);
            for (int side = -1; side <= 1; side += 2)
                DrawEasyWarningRoute(EasyRainWarningPathPrefix(EasyRainSplashWarningPath(impact, side), warning.Progress),
                    radius, new Color(EasyDanger, .4f), false);
        }
        Vector2 centerImpact = EasyRainImpact(warning.Wave, warning.Row, EasyRainColumns / 2);
        if (warning.Progress < 1 || !warning.Points.Contains(centerImpact)) return;
        foreach (float time in new[] { .55f, 2.3f })
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 point = centerImpact + new Vector2(side * 50 * time,
                    -EasySplashRise * time + EasySplashGravity * time * time / 2);
                Vector2 direction = new Vector2(side * 50, -EasySplashRise + EasySplashGravity * time).Normalized();
                EasyDirectionArrow(point, direction, 2);
            }
    }

    private void DrawEasySilentWave()
    {
        var art = EasyArt(ref _easyMessageArt, "easy_silent_message");
        for (int wave = 0; wave < 2; wave++)
        {
            if (_easyWaveLaunches[wave] < 0) continue;
            double elapsed = Time - _easyWaveLaunches[wave];
            if (elapsed >= EasyWaveDuration) continue;
            bool inwardOpen = elapsed >= 0 && EasyWaveInwardRouteOpen(wave);
            float radius = EasyWaveArtRadius(wave);
            if (elapsed < 0)
            {
                var stages = EasyWaveWarningProgress(wave);
                if (stages.Expiry > 0) DrawEasyWaveExpiryArea(wave, stages.Expiry);
                float progress = Progress(_easyWaveWarningStarts[wave], _easyWaveLaunches[wave] - _easyWaveWarningStarts[wave]);
                Vector2 size = new(14, 9);
                Color guide = new(EasyDanger.Lerp(EasyLight, .15f + progress * .15f), .7f);
                if (stages.Ring > 0)
                {
                    DrawEasyWarningArt(art, new Rect2(_origin - size / 2, size), guide, true);
                    DrawEasyWaveExpansionWarning(wave, stages.Ring);
                }
                DrawEasyWaveSpawnMark(_origin, -Mathf.Pi / 2, 1);
            }
            if (!inwardOpen && elapsed < EasyWaveOpeningAt)
            {
                DrawEasyWaveWaitingCue(wave);
                if (elapsed < 0) DrawEasyWaveTravelCue(wave, Time - _easyWaveWarningStarts[wave]);
            }
            if (elapsed < 0) continue;
            if (elapsed < .3)
            {
                DrawCircle(_origin, 20, EasyOutline);
                DrawCircle(_origin, 19, EasyDanger);
                DrawArc(_origin, 19.2f, 0, Mathf.Tau, 64, EasyLight, .6f, true);
                DrawTextureRect(art, new Rect2(_origin - new Vector2(16, 10), new Vector2(32, 20)), false, Colors.White);
            }
            for (int i = 0; i < _easyRingPoints.Length; i++)
            {
                _easyRingPoints[i] = EasyWavePoint(wave, i, radius);
                _easyRingInner[i] = EasyWavePoint(wave, i, radius - 1.65f);
                _easyRingOuter[i] = EasyWavePoint(wave, i, radius + 1.65f);
            }
            foreach (var range in EasyWaveLiveRanges(elapsed))
            {
                if (range.First >= range.End) continue;
                DrawEasyWaveBand(range.First, range.End, EasyOutline, 4);
                DrawEasyWaveBand(range.First, range.End, EasyDanger, 2.9f);
                for (int i = range.First; i < range.End; i++)
                {
                    DrawEasyClippedLine(_easyRingInner[i], _easyRingInner[i + 1], EasyLight, .45f);
                    DrawEasyClippedLine(_easyRingOuter[i], _easyRingOuter[i + 1], EasyLight, .45f);
                }
                if (elapsed < EasyWaveOpeningAt) continue;
                DrawEasyWaveCap(_easyRingPoints[range.First], _easyRingPoints[range.First].DirectionTo(_easyRingPoints[range.First + 1]).Angle() + Mathf.Pi / 2);
                DrawEasyWaveCap(_easyRingPoints[range.End], _easyRingPoints[range.End - 1].DirectionTo(_easyRingPoints[range.End]).Angle() - Mathf.Pi / 2);
            }
        }
    }

    private float EasyWaveArtRadius(int wave)
    {
        double elapsed = Time - _easyWaveLaunches[wave];
        return elapsed < 0
            ? Mathf.Lerp(12, 18, Progress(_easyWaveWarningStarts[wave], _easyWaveLaunches[wave] - _easyWaveWarningStarts[wave]))
            : EasyWaveRadius(elapsed);
    }

    private void DrawEasyWaveExpansionWarning(int wave, float progress)
    {
        float openingRadius = EasyWaveRadius(EasyWaveOpeningAt);
        float halfGap = EasyWaveSpentSegments * Mathf.Pi / EasyWaveSegments;
        float gapAngle = EasyWaveGapAngle(wave);
        float frontier = EasyWaveMaxRadius * progress;
        DrawEasyWarningArc(_origin, Math.Min(openingRadius, frontier), 0, Mathf.Tau, EasyDanger, true);
        for (float ring = openingRadius + 32; ring < frontier + 32 && frontier > openingRadius; ring += 32)
            DrawEasyWarningArc(_origin, Math.Min(ring, frontier), gapAngle + halfGap,
                gapAngle - halfGap + Mathf.Tau, new Color(EasyDanger, .65f), true);
        foreach (float edge in new[] { gapAngle - halfGap, gapAngle + halfGap })
        {
            Vector2 direction = Vector2.FromAngle(edge);
            if (frontier <= openingRadius) continue;
            Vector2 exit = EasyRayExit(_origin, direction);
            EasyRedDashedLine(_origin + direction * openingRadius,
                _origin + direction * Math.Min(frontier, _origin.DistanceTo(exit)));
        }
        for (int ray = 0; ray < 8; ray++)
        {
            float angle = ray * Mathf.Tau / 8;
            Vector2 radial = Vector2.FromAngle(angle);
            Vector2 end = Math.Abs(Mathf.AngleDifference(gapAngle, angle)) < halfGap
                ? _origin + radial * Math.Min(openingRadius, frontier)
                : _origin + radial * Math.Min(frontier, _origin.DistanceTo(EasyRayExit(_origin, radial)));
            DrawEasyWarningDirections(_origin, end);
        }
    }

    private void DrawEasyWaveExpiryArea(int wave, float progress)
    {
        float halfAngle = EasyWaveExpiryHalfAngle;
        float angle = EasyWaveGapAngle(wave), from = angle - halfAngle, to = angle + halfAngle;
        float inner = EasyWaveExpiryInnerRadius;
        const float markerRadius = 56;
        float outer = Mathf.Lerp(inner, EasyWaveMaxRadius, progress);
        var fill = new Color(EasyExpiryBlue, .20f);
        for (int segment = 0; segment < EasyWaveSpentSegments; segment++)
        {
            Vector2 a = Vector2.FromAngle(Mathf.Lerp(from, to, segment / (float)EasyWaveSpentSegments));
            Vector2 b = Vector2.FromAngle(Mathf.Lerp(from, to, (segment + 1) / (float)EasyWaveSpentSegments));
            DrawEasyClippedPolygon(new[] { _origin + a * inner, _origin + a * outer,
                _origin + b * outer, _origin + b * inner }, fill);
        }
        DrawEasyWarningArc(_origin, inner, from, to, EasyExpiryBlue, true);
        foreach (float edge in new[] { from, to })
        {
            Vector2 radial = Vector2.FromAngle(edge);
            DrawEasyWaveExpiryDashedLine(_origin + radial * inner, _origin + radial * outer);
        }
        if (_easyExpiryMarkerCenters[wave] is not { } center)
        {
            Vector2 radial = Vector2.FromAngle(angle), across = radial.Orthogonal() * 56;
            Vector2 at = _origin + radial * (markerRadius + 27);
            Vector2 minimum = new(Field.Left + 17, Field.Top + 17), maximum = new(Field.Right - 17, Field.Bottom - 17);
            Vector2 first = (at + across).Clamp(minimum, maximum), second = (at - across).Clamp(minimum, maximum);
            float Clearance(Vector2 point) => Math.Min(point.DistanceTo(Boss.GlobalPosition) - 32,
                point.DistanceTo(Player.GlobalPosition) - 18);
            center = Clearance(first) >= Clearance(second) ? first : second;
            _easyExpiryMarkerCenters[wave] = center;
        }
        float offset = Mathf.Clamp(Mathf.AngleDifference(angle, (center - _origin).Angle()), -halfAngle, halfAngle);
        Vector2 anchor = _origin + Vector2.FromAngle(angle + offset) * markerRadius;
        DrawEasyWaveExpiryDashedLine(anchor, anchor.Lerp(center, progress));
        DrawCircle(center, 16, new Color(EasyOutline, .94f));
        var marker = EasyArt(ref _easyExpiryMarkerArt, "easy_wave_expiry_marker");
        Vector2 size = marker.GetSize() * (26f / marker.GetHeight());
        DrawTextureRect(marker, new Rect2(center - size / 2, size), false, Colors.White);
    }

    private void DrawEasyWaveExpiryDashedLine(Vector2 from, Vector2 to)
    {
        float length = from.DistanceTo(to);
        Vector2 direction = from.DirectionTo(to);
        for (float at = 0; at < length; at += 4)
        {
            Vector2 a = from + direction * at, b = from + direction * Math.Min(at + 2, length);
            DrawEasyClippedLine(a, b, EasyOutline, 2.8f);
            DrawEasyClippedLine(a, b, EasyExpiryBlue, 1.1f);
        }
    }

    private void DrawEasyWaveSpawnMark(Vector2 center, float angle, float alpha)
    {
        Vector2 radial = Vector2.FromAngle(angle), across = radial.Orthogonal();
        var triangle = new[] { center + radial * 4.8f, center - radial * 2.8f + across * 3.4f,
            center - radial * 2.8f - across * 3.4f, center + radial * 4.8f };
        DrawColoredPolygon(triangle[..3], new Color(EasyOutline, alpha));
        DrawPolyline(triangle, new Color(EasyDanger, alpha), 1.1f, true);
        DrawLine(center + new Vector2(0, -1.5f), center + new Vector2(0, .2f), new Color(EasyLight, alpha), 1, true);
        DrawCircle(center + new Vector2(0, 1.4f), .55f, new Color(EasyLight, alpha));
    }

    private bool EasyWaveInwardRouteOpen(int wave)
    {
        if (!IsStriking) return false;
        Vector2 waiting = _easyWaveWaitPoints[wave];
        Vector2 inward = _origin + _origin.DirectionTo(waiting) * 12;
        int samples = Mathf.CeilToInt(waiting.DistanceTo(inward) * 2);
        for (int sample = 0; sample <= samples; sample++)
            if (DangerDistance(waiting.Lerp(inward, sample / (float)samples), false) <= 1) return false;
        return EasyWaveCueClear(waiting, inward);
    }

    private void DrawEasyWaveWaitingCue(int wave)
    {
        Vector2 waiting = _easyWaveWaitPoints[wave], forward = _origin.DirectionTo(waiting), across = forward.Orthogonal();
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2[] points = {
                waiting + forward * 4 + across * (side * 3.5f),
                waiting + forward * 4 + across * (side * 6.5f),
                waiting - forward * 4 + across * (side * 6.5f),
                waiting - forward * 4 + across * (side * 3.5f),
            };
            DrawEasyWaveCueStroke(points);
        }
    }

    private void DrawEasyWaveTravelCue(int wave, double elapsed)
    {
        Vector2 waiting = _easyWaveWaitPoints[wave], player = Player.GlobalPosition;
        if (player.DistanceTo(waiting) <= 28 || !EasyWaveCueClear(player, waiting)) return;
        Vector2 direction = player.DirectionTo(waiting), across = direction.Orthogonal();
        float phase = (float)(elapsed * 1.8 % 1);
        for (int mark = 0; mark < 2; mark++)
        {
            float distance = 32 - mark * 11 - phase * 8;
            Vector2 center = waiting - direction * distance, tip = center + direction * 4;
            Vector2[] points = { center - direction * 3 + across * 3.5f, tip,
                center - direction * 3 - across * 3.5f };
            if (points.All(point => Field.Rect.Grow(-2).HasPoint(point) && EasyWaveCueClear(waiting, point)
                && point.DistanceTo(player) >= 18)) DrawEasyWaveCueStroke(points);
        }
    }

    private bool EasyWaveCueClear(Vector2 from, Vector2 to)
    {
        var body = Boss.GetChildren().OfType<CollisionShape2D>().First();
        var shape = (CapsuleShape2D)body.Shape;
        float halfAxis = shape.Height / 2 - shape.Radius;
        Vector2 low = body.GlobalPosition - Vector2.Down * halfAxis, high = body.GlobalPosition + Vector2.Down * halfAxis;
        float distance = Math.Min(Math.Min(from.DistanceTo(Geometry2D.GetClosestPointToSegment(from, low, high)),
            to.DistanceTo(Geometry2D.GetClosestPointToSegment(to, low, high))),
            Math.Min(low.DistanceTo(Geometry2D.GetClosestPointToSegment(low, from, to)),
                high.DistanceTo(Geometry2D.GetClosestPointToSegment(high, from, to))));
        return Geometry2D.SegmentIntersectsSegment(from, to, low, high).VariantType == Godot.Variant.Type.Nil
            && distance >= shape.Radius + Player.HitRadius + 4;
    }

    private void DrawEasyWaveCueStroke(Vector2[] points)
    {
        DrawPolyline(points, EasyOutline, 3.2f, true);
        DrawPolyline(points, EasyWaveGuide, 1.3f, true);
    }

    private void DrawEasyWaveBand(int first, int end, Color color, float width)
    {
        for (int i = first; i < end; i++)
            DrawEasyClippedLine(_easyRingPoints[i], _easyRingPoints[i + 1], color, width);
        for (int i = first; i <= end; i++) DrawEasyClippedCircle(_easyRingPoints[i], width / 2, color);
    }

    private void DrawEasyWaveCap(Vector2 center, float angle)
    {
        for (int i = 0; i < 12; i++)
            DrawEasyClippedLine(center + Vector2.FromAngle(angle + i * Mathf.Pi / 12) * 1.65f,
                center + Vector2.FromAngle(angle + (i + 1) * Mathf.Pi / 12) * 1.65f, EasyLight, .45f);
    }

    private void DrawEasyClippedLine(Vector2 a, Vector2 b, Color color, float width)
    {
        Vector2 normal = a.DirectionTo(b).Orthogonal() * width / 2;
        DrawEasyClippedPolygon(new[] { a + normal, b + normal, b - normal, a - normal }, color);
    }

    private void DrawEasyClippedCircle(Vector2 center, float radius, Color color)
    {
        var polygon = new Vector2[16];
        for (int i = 0; i < polygon.Length; i++) polygon[i] = center + Vector2.FromAngle(i * Mathf.Tau / polygon.Length) * radius;
        DrawEasyClippedPolygon(polygon, color);
    }

    private void DrawEasyClippedPolygon(Vector2[] points, Color color)
    {
        Vector2 minimum = points[0], maximum = points[0];
        foreach (Vector2 point in points)
        {
            minimum = minimum.Min(point);
            maximum = maximum.Max(point);
        }
        if (maximum.X <= Field.Left || minimum.X >= Field.Right || maximum.Y <= Field.Top || minimum.Y >= Field.Bottom) return;
        if (minimum.X >= Field.Left && maximum.X <= Field.Right && minimum.Y >= Field.Top && maximum.Y <= Field.Bottom)
        {
            DrawEasyConvexFan(points, color);
            return;
        }
        var polygon = new List<Vector2>(points);
        for (int edge = 0; edge < 4 && polygon.Count > 0; edge++)
        {
            bool vertical = edge < 2;
            bool lower = edge is 0 or 2;
            float boundary = edge switch { 0 => Field.Left, 1 => Field.Right, 2 => Field.Top, _ => Field.Bottom };
            var clipped = new List<Vector2>();
            Vector2 from = polygon[^1];
            float fromValue = vertical ? from.X : from.Y;
            bool fromInside = lower ? fromValue >= boundary : fromValue <= boundary;
            foreach (Vector2 to in polygon)
            {
                float toValue = vertical ? to.X : to.Y;
                bool toInside = lower ? toValue >= boundary : toValue <= boundary;
                if (fromInside != toInside) clipped.Add(from.Lerp(to, (boundary - fromValue) / (toValue - fromValue)));
                if (toInside) clipped.Add(to);
                from = to;
                fromValue = toValue;
                fromInside = toInside;
            }
            polygon = clipped;
        }
        DrawEasyConvexFan(polygon, color);
    }

    private void DrawEasyConvexFan(IReadOnlyList<Vector2> polygon, Color color)
    {
        if (polygon.Count < 3) return;
        Vector2 origin = polygon[0];
        var triangle = new Vector2[3];
        var colors = new[] { color, color, color };
        DrawSetTransform(origin);
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            Vector2 a = polygon[i] - origin, b = polygon[i + 1] - origin;
            double area = (double)a.X * b.Y - (double)a.Y * b.X;
            if (Math.Abs(area) < .00001) continue;
            triangle[1] = a;
            triangle[2] = b;
            DrawPrimitive(triangle, colors, Array.Empty<Vector2>());
        }
        DrawSetTransform(Vector2.Zero);
    }
}
