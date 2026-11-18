using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AkariBossTechnique
{
    private static Texture2D? _easyMessageArt, _easyExpiryMarkerArt;
    private static Texture2D? _rainFallCueArt, _rainReboundCueArt;
    private static Texture2D? _clockDangerArt, _clockNeutralArt;
    private static readonly Color ClockWarningInk = new("ffc184");
    private static readonly Color EasyOutline = new("101829"),
        EasyLight = new("fff3f2"), EasyExpiryBlue = new("369cff"),
        EasyPaper = new("f7d99a"), EasyHeat = new("df9d52");
    private static readonly Color DeliveryDocumentInk = new("ffbd70"), DeliveryEnvelopeInk = new("ff7db4"),
        DeliveryReplyInk = new("c6a0ff");
    private static readonly Dictionary<Texture2D, Vector2[][]> EasyArtContours = new();
    private Color EasyDanger => WarningVisual.Accent;
    private Color EasyMotionGuide => WarningVisual.Accent;
    private readonly Vector2[] _easyRingPoints = new Vector2[65];
    private readonly Vector2[] _easyRingInner = new Vector2[65], _easyRingOuter = new Vector2[65];
    private readonly Vector2?[] _easyExpiryMarkerCenters = new Vector2?[2];
    private bool _easyWarningOverLive;
    private Node2D? _carrierWarningCanvas;
    private readonly Dictionary<bool, Vector2[]> _rainForecastDots = new();
    private float _rainForecastRadius;
    private readonly Vector2[] _easyLineVertices = new Vector2[4];
    private readonly Color[] _easyLineColors = new Color[4];
    private static readonly Dictionary<(bool Evolved, bool Rebound), (Texture2D Texture, Rect2 Bounds)> RainForecastArt = new();

    internal bool UsesEasyReadabilityRules => Kind != BossTechniqueKind.Cameo && _mechanic is 0 or 1 or 2 or 5 or 6 or 7 or 11 or 12;
    protected override bool UsesCustomHazardArt => UsesEasyReadabilityRules || _mechanic is 8 or 10 or 16 or 17 or 18 or 19 or 20;
    protected override IEnumerable<(Vector2 Origin, double StartsAt, double FiresAt)> TelegraphTimings
    {
        get
        {
            if (_mechanic == 14)
            {
                yield return (new Vector2(_origin.X, Field.Top + 4), _deadlineStart, _deadlineFire);
                yield break;
            }
            if (Tier == 3 && _mechanic == 10)
            {
                foreach (var batch in _priorityBatches)
                    yield return (new Vector2(batch.XDirection > 0 ? Field.Left + 10 : Field.Right - 10,
                        Field.Top + (batch.Row + .5f) * Field.Height / 2), batch.StartsAt, batch.FiresAt);
                yield break;
            }
            if (Tier == 3 && _mechanic == 16)
            {
                foreach (var batch in _outboxBatches)
                    yield return (new Vector2(batch.Direction > 0 ? Field.Left + 8 : Field.Right - 8,
                        Field.Top + (batch.FirstRow + .5f) * Field.Height / 3), batch.StartsAt, batch.FiresAt);
                yield break;
            }
            if (!UsesEasyReadabilityRules) yield break;
            switch (_mechanic)
            {
                case 0:
                    for (int batch = 0; batch < 3; batch++)
                        if (_easyDeliveryReserved[batch])
                            yield return (EasyDeliveryWarningPaths(batch).First().Start, EasyDeliveryLaunch(batch) - DeliveryForecastLead, EasyDeliveryLaunch(batch));
                    break;
                case 1:
                    for (int turn = 0; turn < 4; turn++)
                        yield return (EasyInboxSource(turn), EasyInboxLaunch(turn) - EasyInboxWarningLead, EasyInboxLaunch(turn));
                    break;
                case 2:
                    for (int round = 0; round < EasyUnbindingRounds; round++)
                        if (EasyUnbindingWarningStart(round) >= 0 && _history.Count > 1)
                            yield return (EasyHistoryCopyPoints(round * EasyUnbindingCopiesPerRound)[^1],
                                EasyUnbindingWarningStart(round), EasyUnbindingWarningStart(round) + 3);
                    break;
                case 5:
                    for (int column = 0; column < EasyPileColumnCount; column++)
                        if (EasyPileRemaining(column) > 0)
                            yield return (new Vector2(EasyPileX(column), Field.Top + 4), EasyPileStart(column) - _warning, EasyPileStart(column));
                    break;
                case 6:
                    for (int wave = 0; wave < EasyRainWaves; wave++)
                    for (int row = 0; row < EasyRainRows; row++)
                    {
                        double startsAt = RainWarningAt(wave, row);
                        yield return (new Vector2(Field.CenterX, Field.Top + 4), startsAt, startsAt + RainPreview + RainHold);
                    }
                    break;
                case 7:
                    for (int wave = 0; wave < 2; wave++)
                        if (_easyWaveLaunches[wave] >= 0)
                            yield return (_origin, _easyWaveWarningStarts[wave], _easyWaveLaunches[wave]);
                    break;
                case 11:
                    yield return (EasyEnvelopeTravelPath()[0], 0, _warning);
                    for (int opening = 0; opening < 4; opening++)
                    {
                        double start = EasyApprovalWarningStart(opening);
                        yield return (EasyEnvelopeTravelPath()[opening + 1], start, EasyApprovalOpeningFire(opening));
                        if (_stamps.Count > opening)
                            yield return (EasyApprovalStampPosition(opening), start, EasyApprovalStampFire(opening));
                    }
                    break;
                case 12:
                    yield return (_origin, 0, _warning);
                    break;
            }
        }
    }
    private static Texture2D EasyArt(ref Texture2D? cached, string name)
    {
        if (cached != null) return cached;
        var texture = GD.Load<Texture2D>($"res://char/v3/fx/akari/{name}_v1.png");
        return cached = new AtlasTexture { Atlas = texture, Region = UiKit.ContentRect(texture), FilterClip = true };
    }

    private void DrawEasyWarningSegment(Vector2 from, Vector2 to, Color color, ref float travelled, bool highContrast = false, float width = 1, bool sparse = false)
    {
        float length = from.DistanceTo(to);
        if (length == 0) return;
        Vector2 direction = (to - from) / length;
        bool later = AttackTelegraph.IsLater(color);
        float spacing = later ? 32 : _easyWarningOverLive ? 28 : sparse ? 24 : 20,
            dashLength = 3;
        int first = Mathf.FloorToInt(travelled / spacing);
        int last = Mathf.FloorToInt((travelled + length) / spacing);
        for (int dash = first; dash <= last; dash++)
        {
            float start = Mathf.Max(0, dash * spacing - travelled);
            float end = Mathf.Min(length, dash * spacing + dashLength - travelled);
            if (end <= start) continue;
            Vector2 a = from + direction * start, b = from + direction * end;
            DrawEasyClippedLine(a, b, new Color(EasyOutline, highContrast ? .65f : .5f), width + .5f);
            DrawEasyClippedLine(a, b, color, width * .65f);
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

    private readonly Dictionary<bool, Vector2[][]> _rainMaskPieces = new();
    private void MaskRainParticle(Node2D canvas, RainParticle particle, double age)
    {
        bool rebound = particle.Side != 0;
        if (!_rainMaskPieces.TryGetValue(rebound, out var pieces))
        {
            var art = RainVisual(rebound);
            float radius = Evolved ? BulletPool.BossChargeRadius : BulletPool.BossRadius;
            var shape = BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position, Bullet.BossArtSize(radius, Evolved));
            float scale = EasyRainRadius() / shape.Radius;
            _rainMaskPieces[rebound] = pieces = shape.Shapes.OfType<ConvexPolygonShape2D>()
                .Select(poly => poly.Points.Select(point => point * scale).ToArray()).ToArray();
        }
        canvas.DrawSetTransform(RainPosition(particle, age), RainVelocity(particle, age).Angle());
        foreach (var piece in pieces) canvas.DrawColoredPolygon(piece, Colors.White);
        canvas.DrawSetTransform(Vector2.Zero);
    }

    protected override void DrawDangerForecast(Node2D canvas)
    {
        base.DrawDangerForecast(canvas);
        switch (_mechanic)
        {
            case 6:
                foreach (var particle in _rainParticles)
                {
                    double preview = Time - RainWarningAt(particle.Wave, particle.Row);
                    if (preview < 0 || preview >= RainPreview) continue;
                    double age = RainForecastAge(particle);
                    if (age >= 0 && age < particle.Duration)
                        MaskRainParticle(canvas, particle, age);
                }
                break;
            case 7:
                for (int wave = 0; wave < _easyWaveLaunches.Length; wave++)
                {
                    if (_easyWaveLaunches[wave] < 0 || Time >= _easyWaveLaunches[wave]) continue;
                    float progress = EasyWaveWarningProgress(wave).Ring;
                    if (progress > 0) MaskWaveSweep(canvas, wave, progress);
                }
                break;
            case 12:
                foreach (var stroke in ClockForecastStrokes())
                    AttackTelegraph.MaskStroke(canvas, stroke.A, stroke.B, EasyClockHalfWidth);
                break;
        }
    }

    private void MaskWaveSweep(Node2D canvas, int wave, float progress)
    {
        float first = EasyWaveRadius(EasyWaveOpeningAt), far = EasyWaveMaxRadius * progress;
        canvas.DrawCircle(_origin, Math.Min(first, far) + 2, Colors.White);
        if (far <= first) return;
        for (int i = EasyWaveSpentSegments; i < EasyWaveSegments; i++)
        {
            Vector2 a = EasyWavePoint(wave, i, first), b = EasyWavePoint(wave, i + 1, first);
            Vector2 c = EasyWavePoint(wave, i + 1, far), d = EasyWavePoint(wave, i, far);
            canvas.DrawColoredPolygon(new[] { a, b, c, d }, Colors.White);
            AttackTelegraph.MaskStroke(canvas, d, c, 2);
            if (i == EasyWaveSpentSegments) AttackTelegraph.MaskStroke(canvas, a, d, 2);
            if (i == EasyWaveSegments - 1) AttackTelegraph.MaskStroke(canvas, b, c, 2);
        }
    }

    private void DrawEasyTechniqueArt()
    {
        _carrierWarningCanvas?.QueueRedraw();
        _easyWarningOverLive = OwnedShots.Any(shot => !shot.LeadIn && shot.CollisionLayer != 0 && Field.Rect.HasPoint(shot.GlobalPosition));
        switch (_mechanic)
        {
            case 5: DrawEasyPaperPiles(); break;
            case 6: DrawRainForecast(); break;
            case 7: DrawEasySilentWave(); break;
            default:
                DrawEasyRuleHazards(false);
                DrawEasyMechanicDirections();
                DrawEasyRuleHazards(true);
                DrawOfficeTechniqueArt();
                break;
        }
    }

    private void DrawLunaticOfficeForecasts()
    {
        if (!GameManager.ShowAttackTelegraphs) return;
        for (int batchIndex = 0; batchIndex < _priorityBatches.Count; batchIndex++)
        {
            var batch = _priorityBatches[batchIndex];
            if (Time < batch.StartsAt || Time >= batch.EndsAt) continue;
            int number = batchIndex + 1;
            double age = Time - batch.FiresAt;
            if (age >= 0)
            {
                float across = (float)(Math.Min(age, 1.6) * (batch.AcrossFirst ? 70 : 24)
                    + Math.Max(0, age - 1.6) * (batch.AcrossFirst ? 24 : 70));
                float down = (float)(Math.Min(age, 1.6) * (batch.AcrossFirst ? 24 : 70)
                    + Math.Max(0, age - 1.6) * (batch.AcrossFirst ? 70 : 24));
                float x = (batch.XDirection > 0 ? Field.Left - 12 : Field.Right + 12) + batch.XDirection * across;
                float y = (batch.YDirection > 0 ? Field.Top - 12 : Field.Bottom + 12) + batch.YDirection * down;
                if (x >= Field.Left + 3 && x <= Field.Right - 3)
                    OfficeNumber(new Vector2(x, Field.Top + (batch.Row + .5f) * Field.Height / 2), number, Paper);
                if (y >= Field.Top + 5 && y <= Field.Bottom - 5)
                    OfficeNumber(new Vector2(Field.Left + (batch.Column + .5f) * Field.Width / 2, y), number, Paper);
                continue;
            }
            Vector2 horizontal = new(batch.XDirection > 0 ? Field.Left + 10 : Field.Right - 10,
                Field.Top + (batch.Row + .5f) * Field.Height / 2);
            Vector2 vertical = new(Field.Left + (batch.Column + .5f) * Field.Width / 2,
                batch.YDirection > 0 ? Field.Top + 10 : Field.Bottom - 10);
            AttackTelegraph.ProjectileOrigin(this, horizontal, WarningVisual, WarningVisual.Accent);
            AttackTelegraph.ProjectileOrigin(this, vertical, ShotVisual(0), ShotVisual(0).Accent);
            AttackTelegraph.Arrow(this, horizontal + Vector2.Right * batch.XDirection * 22,
                Vector2.Right * batch.XDirection, WarningVisual.Accent);
            AttackTelegraph.Arrow(this, vertical + Vector2.Down * batch.YDirection * 22,
                Vector2.Down * batch.YDirection, ShotVisual(0).Accent);
            OfficeNumber(horizontal + Vector2.Up * (10 + batchIndex * 9), number, Paper);
            OfficeNumber(vertical + Vector2.Left * (10 + batchIndex * 9), number, Paper);
        }
        foreach (var batch in _outboxBatches.Where(batch => Time >= batch.StartsAt && Time < batch.EndsAt))
        for (int group = 0; group < 3; group++)
        {
            double age = Time - batch.FiresAt;
            float centerY = Field.Top + ((group + batch.FirstRow) % 3 + .5f) * Field.Height / 3;
            Vector2 from = new(batch.Direction > 0 ? Field.Left : Field.Right, centerY);
            Vector2 to = new(batch.Direction > 0 ? Field.Right : Field.Left, centerY);
            Vector2 pause = new((batch.Direction > 0 ? Field.Left - 12 : Field.Right + 12)
                + batch.Direction * (20 + group * 25) * 1.7f, centerY);
            if (age < 0)
            {
                AttackTelegraph.Route(this, from, to, Field.Height / 6, true, true, true, true);
            }
            if (age >= 4.7 + group * .9) continue;
            DrawLine(pause + new Vector2(-3, -5), pause + new Vector2(-3, 5), Ink, 1.2f, true);
            DrawLine(pause + new Vector2(3, -5), pause + new Vector2(3, 5), Ink, 1.2f, true);
            OfficeNumber(pause + new Vector2(batch.Direction * 13, 0), group + 1, Paper);
        }
    }

    private void DrawEasyRuleHazards(bool live)
    {
        Vector2? previousEnd = null;
        foreach (var h in HazardShapes.Where(hazard => hazard.Live == live))
        {
            if (_mechanic == 12 && h.Shape == 1) continue;
            if (_mechanic == 0 && !live) continue;
            if (_mechanic == 12 && Time < _warning && !h.Live && h.Shape != 1) continue;
            if (h.Shape == 2)
            {
                if (_mechanic == 11 && !live) continue;
                var rect = new Rect2(h.A, h.B - h.A).Intersection(Field.Rect);
                if (h.Live) DrawEasyDangerBox(rect);
                else DrawEasyWarningBox(rect, _mechanic == 11 ? AttackTelegraph.Sweep : AttackTelegraph.Area);
            }
            else if (h.Shape == 1)
            {
                if (h.Live)
                {
                    DrawEasyClippedCircle(h.A, h.Radius, new Color(EasyHeat, .6f));
                    DrawArc(h.A, h.Radius, 0, Mathf.Tau, 40, EasyPaper, 1.2f, true);
                }
                else
                {
                    AttackTelegraph.Disc(this, h.A, h.Radius, AttackTelegraph.Area);
                }
            }
            else if (h.Shape == 0)
            {
                if (!live && _mechanic == 11) continue;
                if (h.Live)
                {
                    DrawEasyClippedLine(h.A, h.B, EasyOutline, h.Radius * 2 + 1.4f);
                    DrawEasyClippedCircle(h.A, h.Radius + .7f, EasyOutline);
                    DrawEasyClippedCircle(h.B, h.Radius + .7f, EasyOutline);
                    DrawEasyClippedLine(h.A, h.B, EasyHeat, h.Radius * 2);
                    DrawEasyClippedCircle(h.A, h.Radius, EasyHeat);
                    DrawEasyClippedCircle(h.B, h.Radius, EasyHeat);
                    if (_mechanic == 12) DrawEasyClockInlay(h.A, h.B, h.Radius);
                    else DrawEasyClippedLine(h.A, h.B, EasyLight, .45f);
                }
                else
                {
                    var visual = _mechanic == 10 && Math.Abs(h.B.Y - h.A.Y) > Math.Abs(h.B.X - h.A.X) ? ShotVisual(0) : WarningVisual;
                    Color ink = visual.Accent;
                    AttackTelegraph.CorridorInterior(this, h.A, h.B, h.Radius, ink);
                    Vector2 side = h.A.DirectionTo(h.B).Orthogonal() * h.Radius;
                    EasyRedDashedLine(h.A + side, h.B + side, ink);
                    EasyRedDashedLine(h.A - side, h.B - side, ink);
                    DrawWarningEdgeCaps(h.A, h.B, h.Radius, ink);
                    if (previousEnd != h.A && _mechanic is 1 or 2 or 10 or 16 or 17)
                        AttackTelegraph.ProjectileOrigin(this, h.A, visual, ink);
                    previousEnd = h.B;
                    if (_mechanic is not (2 or 3 or 9 or 12)) DrawEasyWarningDirections(h.A, h.B, ink);
                }
            }
        }
    }

    private void DrawEasyClockInlay(Vector2 from, Vector2 to, float radius)
    {
        Vector2 direction = from.DirectionTo(to), side = direction.Orthogonal();
        float length = from.DistanceTo(to);
        DrawEasyClippedLine(from, to, new Color("a64b58"), radius * 1.35f);
        DrawEasyClippedLine(from + side * (radius - .5f), to + side * (radius - .5f), EasyPaper, .7f);
        DrawEasyClippedLine(from - side * (radius - .5f), to - side * (radius - .5f), new Color("a65d37"), .7f);
        for (float distance = 4; distance < length - 3; distance += 10)
        {
            Vector2 at = from + direction * distance;
            DrawEasyClippedPolygon(new[] { at - direction * 3, at + side * radius * .52f,
                at + direction * 3, at - side * radius * .52f }, EasyPaper);
            DrawEasyClippedLine(at - direction * 1.8f, at + direction * 1.8f, EasyLight, .55f);
        }
    }

    private void EasyRedDashedLine(Vector2 from, Vector2 to, Color? ink = null)
    {
        Color color = ink ?? EasyMotionGuide;
        float dashLength = 3;
        Vector2 direction = from.DirectionTo(to);
        float length = from.DistanceTo(to);
        for (float at = 0; at < length; at += AttackTelegraph.IsLater(color) ? 32 : _easyWarningOverLive ? 28 : 20)
        {
            Vector2 a = from + direction * at, b = from + direction * Math.Min(at + dashLength, length);
            DrawEasyClippedLine(a, b, new Color(EasyOutline, .6f), 1.5f);
            DrawEasyClippedLine(a, b, color, .65f);
        }
    }

    private void EasyDirectionArrow(Vector2 point, Vector2 direction, int count = 1, Color? ink = null)
    {
        if (direction == Vector2.Zero) return;
        Vector2 side = direction.Orthogonal();
        Color color = ink ?? EasyMotionGuide;
        if (_easyWarningOverLive) color = new Color(color, color.A * .72f);
        for (int i = 0; i < Math.Min(1, count); i++)
        {
            Vector2 tip = point - direction * (i * 4);
            DrawEasyClippedLine(tip - direction * 2.5f + side * 1.7f, tip, EasyOutline, 1.6f);
            DrawEasyClippedLine(tip, tip - direction * 2.5f - side * 1.7f, EasyOutline, 1.6f);
            DrawEasyClippedLine(tip - direction * 2.5f + side * 1.7f, tip, color, .65f);
            DrawEasyClippedLine(tip, tip - direction * 2.5f - side * 1.7f, color, .65f);
        }
    }

    private void DrawEasyWarningDirections(Vector2 from, Vector2 to, Color? ink = null)
    {
        float length = from.DistanceTo(to);
        Vector2 direction = from.DirectionTo(to);
        for (float at = 24; at < length - 6; at += _easyWarningOverLive ? 132 : 88)
            EasyDirectionArrow(from + direction * at, direction, 2, ink);
    }

    private void DrawEasyTurnMark(Vector2 center, Vector2 incoming, Vector2 outgoing, float alpha, float glint = 0, Color? ink = null)
    {
        float radius = 3.5f + glint;
        var white = new Color(ink ?? EasyLight, alpha);
        foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
        {
            Vector2 at = center + corner * radius;
            Vector2 horizontal = at - new Vector2(corner.X * 1.8f, 0);
            Vector2 vertical = at - new Vector2(0, corner.Y * 1.8f);
            DrawEasyClippedLine(horizontal, at, new Color(EasyOutline, alpha), 1.8f);
            DrawEasyClippedLine(at, vertical, new Color(EasyOutline, alpha), 1.8f);
            DrawEasyClippedLine(horizontal, at, white, .85f);
            DrawEasyClippedLine(at, vertical, white, .85f);
        }
        Vector2 crease = (incoming - outgoing).Normalized().Orthogonal() * 2.3f;
        DrawEasyClippedLine(center - crease, center + crease, new Color(EasyOutline, alpha), 1.6f);
        DrawEasyClippedLine(center - crease, center + crease, white, .7f);
    }

    private void DrawDeliveryTurnEffect(Vector2 center, Vector2 incoming, Vector2 outgoing,
        double showsAt, double firstPassage, int firstGroup, Color ink)
    {
        double lastPassage = firstPassage + (EasyDeliveryBursts - 1) * EasyDeliveryBurstInterval;
        if (Time < showsAt || Time > lastPassage + .3) return;
        int nextBurst = Math.Clamp((int)Math.Ceiling((Time - firstPassage) / EasyDeliveryBurstInterval), 0, EasyDeliveryBursts - 1);
        double untilNext = firstPassage + nextBurst * EasyDeliveryBurstInterval - Time;
        float approach = untilNext > 0 ? 1 - Mathf.Clamp((float)(untilNext / .55), 0, 1) : 0;
        float fade = 1 - Mathf.Clamp((float)((Time - lastPassage) / .3), 0, 1);
        int burst = (int)Math.Floor((Time - firstPassage) / EasyDeliveryBurstInterval);
        float age = (float)(Time - firstPassage - burst * EasyDeliveryBurstInterval);
        bool passed = burst >= 0 && burst < EasyDeliveryBursts && age < .22f
            && _letters.Any(letter => letter.Group == firstGroup + burst * (3 * EasyDeliveryLanes) && letter.Alive);
        float progress = passed ? age / .22f : 1, pulse = (1 - progress) * (1 - progress);
        float pop = Mathf.Sin(progress * Mathf.Pi) * pulse;
        Vector2 squash = new(1 + approach * .12f + pop * .22f, 1 - approach * .08f + pop * .22f);
        float alpha = (.72f + approach * .23f) * fade;
        Color seal = ink.Lerp(EasyLight, .15f);
        var rim = new Vector2[24];
        for (int i = 0; i < rim.Length; i++)
        {
            float angle = i * Mathf.Tau / rim.Length;
            rim[i] = center + Vector2.FromAngle(angle) * (4.5f + .08f * Mathf.Cos(angle * 6)) * squash;
        }
        for (int i = 0; i < rim.Length; i += 2)
        {
            DrawEasyClippedLine(rim[i], rim[(i + 1) % rim.Length], new Color(EasyOutline, alpha), 1.7f);
            DrawEasyClippedLine(rim[i], rim[(i + 1) % rim.Length], new Color(seal, alpha), .75f);
        }
        float tilt = incoming.Cross(outgoing) * (approach * .12f - pop * .3f);
        DrawDeliveryHeart(center, 2 + approach * .35f + pop * .5f, tilt, new Color(seal.Lerp(EasyLight, approach * .4f), alpha));
    }

    private void DrawDeliveryHeart(Vector2 center, float size, float angle, Color color)
    {
        // Each half stays convex for the playfield clipping fan.
        Vector2[] half = { new(0, -.35f), new(-.35f, -.85f), new(-.8f, -.95f), new(-1.1f, -.65f),
            new(-1.15f, -.15f), new(-.9f, .35f), new(-.45f, .8f), new(0, 1.2f) };
        foreach (float side in new[] { -1f, 1f })
        {
            var outline = half.Select(point => center + new Vector2(point.X * side, point.Y).Rotated(angle) * (size + .35f)).ToArray();
            DrawEasyClippedPolygon(outline, new Color(EasyOutline, color.A * .85f));
        }
        foreach (float side in new[] { -1f, 1f })
            DrawEasyClippedPolygon(half.Select(point => center + new Vector2(point.X * side, point.Y).Rotated(angle) * size).ToArray(), color);
    }

    private static Color DeliveryForecastInk(DeliveryKind kind) => kind switch
    {
        DeliveryKind.FallingDocument => DeliveryDocumentInk,
        DeliveryKind.TurningEnvelope => DeliveryEnvelopeInk,
        _ => DeliveryReplyInk,
    };

    private void DrawDeliveryProjectileCue(DeliveryForecast cue)
    {
        Vector2 center = cue.Points[^1];
        Color ink = DeliveryForecastInk(cue.Kind);
        float angle = cue.Kind is DeliveryKind.DescendingReply or DeliveryKind.AscendingReply
            ? cue.Direction.Angle() : Mathf.Sin((float)Time * 7 + cue.Group * 1.7f) * .09f;
        float alpha = _easyWarningOverLive ? .78f : .95f;
        Vector2[] shape = cue.Kind switch
        {
            DeliveryKind.FallingDocument => new[] { new Vector2(-1.8f, -3), new(.5f, -3),
                new(1.8f, -1.7f), new(1.8f, 3), new(-1.8f, 3) },
            DeliveryKind.TurningEnvelope => new[] { new Vector2(-3, -1.6f), new(-2.5f, -2.1f),
                new(2.5f, -2.1f), new(3, -1.6f), new(3, 1.6f), new(2.5f, 2.1f), new(-2.5f, 2.1f), new(-3, 1.6f) },
            _ => new[] { new Vector2(-3, -2.4f), new(3.5f, 0), new(-3, 2.4f), new(-1.7f, 0) },
        };
        var points = shape.Select(point => center + point.Rotated(angle)).ToArray();
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 from = points[i], to = points[(i + 1) % points.Length], direction = from.DirectionTo(to);
            float length = from.DistanceTo(to);
            for (float at = 0; at < length; at += 2.2f)
            {
                Vector2 a = from + direction * at, b = from + direction * Math.Min(length, at + 1.1f);
                DrawEasyClippedLine(a, b, new Color(EasyOutline, alpha), 1.6f);
                DrawEasyClippedLine(a, b, new Color(ink, alpha), .7f);
            }
        }
        void Fold(Vector2 from, Vector2 to)
        {
            Vector2 a = center + from.Rotated(angle), b = center + to.Rotated(angle);
            DrawEasyClippedLine(a, b, new Color(EasyOutline, alpha), 1.5f);
            DrawEasyClippedLine(a, b, new Color(ink, alpha), .6f);
        }
        switch (cue.Kind)
        {
            case DeliveryKind.FallingDocument:
                Fold(new(.5f, -3), new(.5f, -1.7f));
                Fold(new(.5f, -1.7f), new(1.8f, -1.7f));
                Fold(new(-1, 0), new(.9f, 0));
                Fold(new(-1, 1.3f), new(.4f, 1.3f));
                break;
            case DeliveryKind.TurningEnvelope:
                Fold(shape[1], new(0, .2f));
                Fold(shape[2], new(0, .2f));
                DrawDeliveryHeart(center + new Vector2(0, .2f).Rotated(angle), .8f, angle, new Color(ink.Lerp(EasyLight, .5f), alpha));
                break;
            default:
                Fold(shape[3], shape[1]);
                break;
        }
    }

    private void DrawEasyWarningRoute(Vector2[] points, float radius, Color color, bool drawDirections)
    {
        float centerTravel = 0, pathTravel = 0, nextMark = 20;
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 a = points[i - 1], b = points[i], direction = a.DirectionTo(b), side = direction.Orthogonal() * radius;
            AttackTelegraph.CorridorInterior(this, a, b, radius,
                _easyWarningOverLive ? new Color(color, color.A * .6f) : color);
            foreach (float sign in new[] { -1f, 1f })
            {
                DrawEasyClippedLine(a + side * sign, b + side * sign, new Color(EasyOutline, .5f), 1.4f);
                DrawEasyClippedLine(a + side * sign, b + side * sign, new Color(color, _easyWarningOverLive ? .65f : .85f), .6f);
            }
            DrawEasyWarningSegment(a, b, color, ref centerTravel, true, .7f, true);
            DrawWarningEdgeCaps(a, b, radius, color);
            float length = a.DistanceTo(b);
            while (drawDirections && nextMark < pathTravel + length)
            {
                EasyDirectionArrow(a + direction * (nextMark - pathTravel), direction, 2, color);
                nextMark += _easyWarningOverLive ? 132 : 88;
            }
            pathTravel += length;
        }
    }

    private void DrawEasyMechanicDirections()
    {
        if (_mechanic == 0)
        {
            foreach (var cue in DeliveryForecasts())
            {
                Color ink = DeliveryForecastInk(cue.Kind);
                DrawEasyWarningRoute(cue.Points, DeliveryWarningHalfWidth(cue.Kind), ink, false);
                if (cue.Points[0].DistanceTo(cue.Points[^1]) > 3)
                    EasyDirectionArrow(cue.Points[cue.Points.Length / 2], cue.Direction, 2, ink);
                DrawDeliveryProjectileCue(cue);
            }
            for (int batch = 0; batch < 3; batch++)
            {
                if (!_easyDeliveryReserved[batch]) continue;
                double launch = EasyDeliveryLaunch(batch);
                var paths = EasyDeliveryReturnPaths(batch);
                for (int lane = 0; lane < paths.Length; lane++)
                {
                    var path = paths[lane];
                    if (Time >= launch - DeliveryForecastLead && Time < launch)
                        DrawEasyWaveSpawnMark(path.OrdinaryStart, Mathf.Pi / 2, .8f);
                    double express = launch + path.Delay;
                    if (Time >= express - DeliveryForecastLead && Time < express)
                        DrawEasyWaveSpawnMark(path.Start, Mathf.Pi / 2, .8f, DeliveryEnvelopeInk);
                    double turn = express + path.TurnAt;
                    double bounce = express + path.BounceAt;
                    double bendMiddle = (path.TurnAt + path.StraightAt) / 2;
                    int group = batch * EasyDeliveryLanes + lane;
                    DrawDeliveryTurnEffect(DeliverySample(path, true, bendMiddle).Position,
                        Vector2.Down, path.Across, turn - DeliveryForecastLead, express + bendMiddle, group, DeliveryEnvelopeInk);
                    DrawDeliveryTurnEffect(path.Bounce, path.Across, path.Bounce.DirectionTo(path.Exit),
                        bounce - DeliveryForecastLead, bounce, 2000 + group, DeliveryReplyInk);
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
                var prefix = EasyRainWarningPathPrefix(EasyEnvelopeCurvePoints(), EasyEnvelopeTravelWarningProgress());
                AttackTelegraph.ProjectileOrigin(this, path[0], BulletArt.EnemyVisual("akari_envelope", "ff7db4"), DeliveryEnvelopeInk);
                float travel = 0, nextArrow = 24;
                for (int i = 1; i < prefix.Length; i++)
                {
                    Vector2 a = prefix[i - 1], b = prefix[i], direction = a.DirectionTo(b);
                    float before = travel, length = a.DistanceTo(b);
                    DrawEasyWarningSegment(a, b, DeliveryEnvelopeInk, ref travel, true, 1, false);
                    while (nextArrow < before + length)
                    {
                        EasyDirectionArrow(a + direction * (nextArrow - before), direction, 2, DeliveryEnvelopeInk);
                        nextArrow += 88;
                    }
                }
                float shown = _carrierRoutes.Sum(route => route.Length) * EasyEnvelopeTravelWarningProgress();
                for (int i = 1; i < path.Length - 1; i++)
                {
                    shown -= _carrierRoutes[i - 1].Length;
                    if (shown >= 0) DrawEasyTurnMark(path[i], path[i - 1].DirectionTo(path[i]), path[i].DirectionTo(path[i + 1]), .9f, ink: DeliveryEnvelopeInk);
                }
            }
            else
                for (int i = 1; i < path.Length - 1; i++)
                {
                    double age = Time - EasyApprovalLeave(i - 1);
                    if (age >= .12) continue;
                    DrawEasyTurnMark(path[i], path[i - 1].DirectionTo(path[i]), path[i].DirectionTo(path[i + 1]),
                        age < 0 ? .55f : 1 - (float)(age / .12), age < 0 ? 0 : 1, DeliveryEnvelopeInk);
                }
            for (int opening = 0; opening < _stamps.Count; opening++)
            {
                if (Time < EasyApprovalWarningStart(opening)) continue;
                if (Time < EasyApprovalOpeningFire(opening))
                {
                    var ray = EasyEnvelopeOpeningPaths(opening).ElementAt(2);
                    AttackTelegraph.ProjectileOrigin(this, ray.Start, WarningVisual, EasyDanger);
                    EasyDirectionArrow(ray.Start + ray.Start.DirectionTo(ray.Exit) * 8, ray.Start.DirectionTo(ray.Exit), 2, EasyDanger);
                }
                if (Time < EasyApprovalOpeningFire(opening))
                    foreach (var ray in EasyEnvelopeOpeningPaths(opening))
                        DrawEasyWarningRoute(new[] { ray.Start, ray.Start.Lerp(ray.Exit, EasyEnvelopeOpeningWarningProgress(opening)) },
                            EasyWarningHalfWidth(2), EasyDanger, true);
                if (Time >= EasyApprovalStampFire(opening)) continue;
                var stamps = EasyApprovalStampPaths(opening);
                for (int index = 0; index < stamps.Length; index++)
                {
                    var stamp = stamps[index];
                    float progress = EasyApprovalStampPathWarningProgress(opening, index);
                    if (progress > 0) AttackTelegraph.InstantBand(this, stamp.A, stamp.B, stamp.Radius, progress);
                }
            }
        }
        else if (_mechanic == 12) DrawClockDirections();
    }

    private IEnumerable<(Vector2 A, Vector2 B)> ClockForecastStrokes()
    {
        double elapsed = Math.Max(0, Time - _warning);
        double end = Math.Min(EasyClockDuration, elapsed + ClockForecastLead * EasyClockWarningProgress());
        if (end <= elapsed) yield break;
        int steps = Mathf.CeilToInt((float)((end - elapsed) * 60));
        for (int hand = 0; hand < EasyClockHands; hand++)
        for (int step = 0; step <= steps; step++)
        {
            double at = elapsed + (end - elapsed) * step / steps;
            foreach (var stroke in ClockHandStrokes(at, hand)) yield return stroke;
        }
    }

    private void DrawClockHub()
    {
        bool dangerous = Tier >= 2, live = Time >= _warning;
        float radius = dangerous ? ClockHubRadius : 6;
        var texture = dangerous
            ? _clockDangerArt ??= GD.Load<Texture2D>("res://assets/fx/akari_clock/danger.png")
            : _clockNeutralArt ??= GD.Load<Texture2D>("res://assets/fx/akari_clock/neutral.png");
        float size = radius * 2.1f;
        DrawTextureRect(texture, new Rect2(_origin - Vector2.One * size / 2, Vector2.One * size), false,
            new Color(1, 1, 1, live || !dangerous ? 1 : .65f));
        if (!dangerous)
        {
            double elapsed = Time - _warning;
            if (GameManager.ShowAttackTelegraphs && elapsed < ClockShiftEnd)
            {
                Vector2 end = ClockOriginAt(ClockShiftEnd), direction = _origin.DirectionTo(end);
                if (_origin.DistanceSquaredTo(end) > 4)
                {
                    DrawDashedLine(_origin, end, ClockWarningInk, .8f, 3, true);
                    AttackTelegraph.Arrow(this, end - direction * 6, direction, ClockWarningInk);
                    DrawEasyWarningArc(end, 6, 0, Mathf.Tau, ClockWarningInk, true);
                }
            }
            return;
        }
        if (live)
        {
            DrawArc(_origin, radius, 0, Mathf.Tau, 64, EasyOutline, 1.5f, true);
            DrawArc(_origin, radius, 0, Mathf.Tau, 64, AttackLineInk, .85f, true);
        }
        else if (GameManager.ShowAttackTelegraphs)
        {
            AttackTelegraph.Disc(this, _origin, radius, ClockWarningInk);
            DrawStringOutline(UiKit.ZenBold, _origin + new Vector2(-18, -24), "中央も危険", fontSize: 7, size: 2, modulate: EasyOutline);
            DrawString(UiKit.ZenBold, _origin + new Vector2(-18, -24), "中央も危険", fontSize: 7, modulate: ClockWarningInk);
        }
    }

    private void DrawClockDirections()
    {
        if (!GameManager.ShowAttackTelegraphs || Time >= _warning) return;
        float progress = EasyClockWarningProgress();
        if (progress <= 0) return;
        float travelled = 0;
        if (progress < 1)
        {
            foreach (var outline in Enumerable.Range(0, EasyClockHands).Select(hand => EasyClockHandWarningSector(progress, hand)))
                for (int i = 1; i < outline.Length; i++)
                    DrawEasyWarningSegment(outline[i - 1], outline[i], new Color(ClockWarningInk, .45f), ref travelled);
        }
        else
        {
            // All hands eventually cover the field; only the imminent sweep is filled so the gaps remain readable.
            var border = new[] { Field.Rect.Position, new Vector2(Field.Right, Field.Top), Field.Rect.End,
                new Vector2(Field.Left, Field.Bottom), Field.Rect.Position };
            for (int i = 1; i < border.Length; i++)
                DrawEasyWarningSegment(border[i - 1], border[i], new Color(ClockWarningInk, .45f), ref travelled);
            if (Tier < 2)
                DrawEasyWarningArc(_origin, 12 - EasyClockHalfWidth, 0, Mathf.Tau, ClockWarningInk, true);
        }
        for (int hand = 0; hand < EasyClockHands; hand++)
        {
            float angle = EasyClockHandAngle(0, hand);
            foreach (var stroke in ClockHandStrokes(0, hand))
                DrawEasyWarningRoute(new[] { stroke.A, stroke.B }, EasyClockHalfWidth, ClockWarningInk, false);
            DrawEasyWarningArc(_origin, 57, angle, angle + .85f * progress, ClockWarningInk, true);
            Vector2 turn = Vector2.FromAngle(angle + .85f * progress);
            AttackTelegraph.Arrow(this, _origin + turn * 57, new Vector2(-turn.Y, turn.X), ClockWarningInk);
            if (progress < 1) continue;
            Vector2 extended = Vector2.FromAngle(EasyClockHandAngle(3, hand));
            foreach (var stroke in ClockHandStrokes(3, hand))
                DrawEasyWarningRoute(new[] { stroke.A, stroke.B }, EasyClockHalfWidth, new Color(ClockWarningInk, .5f), false);
            AttackTelegraph.Arrow(this, _origin + extended * 68, extended, new Color(ClockWarningInk, .6f));
        }
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
        double first = Enumerable.Range(0, EasyPileColumnCount).Select(EasyPileStart)
            .Where(start => Time >= start - _warning && Time < start).DefaultIfEmpty(double.PositiveInfinity).Min();
        for (int pass = 0; pass < 2; pass++)
        for (int index = 0; index < EasyPileColumnCount; index++)
        {
            int column = pass == 0 ? EasyPileColumnCount - 1 - index : index;
            double start = EasyPileStart(column);
            double age = Time - start;
            int remaining = EasyPileRemaining(column);
            if (remaining == 0) continue;
            if (pass == 0 && Time >= start - _warning && Time < start)
            {
                bool next = Tier <= 1 || start <= first + .02;
                float progress = EasyPileWarningProgress(column);
                var guide = AttackTelegraph.OrderedInk(WarningVisual.Accent, next);
                Rect2 lane = EasyPileLane(column, progress);
                float x = EasyPileX(column);
                EasyRedDashedLine(new(x, Field.Top), new(x, Field.Top + 4), guide);
                for (int sheet = 2; sheet >= 0; sheet--)
                    AttackTelegraph.ProjectileOrigin(this, new Vector2(x + sheet * 2, Field.Top + 7 + sheet * 2),
                        WarningVisual, guide, 5);
                if (progress > 0)
                {
                    if (Tier <= 1) DrawRect(lane, new Color(guide, .14f));
                    DrawEasyWarningBox(lane, guide);
                    float stackTop = Field.Bottom - EasyPileRows * EasyPileLayerHeight;
                    if (lane.End.Y > stackTop)
                        if (Tier == 2) AttackTelegraph.Arrow(this, new Vector2(x, Math.Min(stackTop, Field.Top + 16)), Vector2.Down, guide);
                        else AttackTelegraph.Box(this, new Rect2(lane.Position.X, stackTop, lane.Size.X, lane.End.Y - stackTop), guide);
                    EasyRedDashedLine(new(x, Field.Top), new(x, lane.End.Y), guide);
                    DrawEasyWarningDirections(new(x, Field.Top), new(x, lane.End.Y), guide);
                }
            }
            if (pass == 0 && Tier <= 1 && age >= 0 && _states[column] < remaining)
            {
                Rect2 lane = EasyPileLane(column);
                var guide = new Color(WarningVisual.Accent, .48f);
                DrawDashedLine(lane.Position, new(lane.Position.X, lane.End.Y), guide, .7f, 4, true);
                DrawDashedLine(new(lane.End.X, lane.Position.Y), lane.End, guide, .7f, 4, true);
            }
            if (pass == 0) continue;
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

    private void DrawEasyWarningBox(Rect2 bounds, Color color)
    {
        Rect2 rect = bounds.Intersection(Field.Rect);
        if (Tier == 2) AttackTelegraph.DashedBounds(this, rect, new Color(color, .66f));
        else AttackTelegraph.Box(this, rect, color);
    }

    private void DrawEasyDangerBox(Rect2 bounds)
    {
        float minimum = Mathf.Min(bounds.Size.X, bounds.Size.Y);
        if (minimum <= 0) return;
        DrawRect(bounds, EasyOutline);
        var face = bounds.Grow(-Mathf.Min(.8f, minimum * .2f));
        DrawRect(face, new Color("c7b9ac"));
        DrawRect(new Rect2(face.Position, new Vector2(face.Size.X, face.Size.Y * .72f)), new Color("f0e5d2"));
        if (minimum > 7)
        {
            BossProjectileArt.Get("akari_envelope").Draw(this, bounds.Grow(-1), Time);
        }
        DrawLine(face.Position, new Vector2(face.End.X, face.Position.Y), EasyLight, .65f, true);
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

    private void DrawRainSourceCue(Vector2 anchor, bool rebound, float? progress, float alpha)
    {
        var texture = rebound
            ? _rainReboundCueArt ??= GD.Load<Texture2D>("res://assets/fx/akari_rain_cues/rebound.png")
            : _rainFallCueArt ??= GD.Load<Texture2D>("res://assets/fx/akari_rain_cues/fall.png");
        float pitch = (Field.Width - 32) / (EasyRainColumns - 1);
        float size = Math.Min(rebound ? 17 : 14, pitch * .88f);
        float y = rebound ? anchor.Y - size + 1 : Field.Top;
        var rect = new Rect2(anchor.X - size / 2, y, size, size);
        var visible = rect.Intersection(Field.Rect);
        if (!visible.HasArea()) return;
        float ready = Mathf.Clamp(progress ?? 1, 0, 1);
        var region = new Rect2((visible.Position - rect.Position) / size * texture.GetSize(), visible.Size / size * texture.GetSize());
        DrawTextureRectRegion(texture, visible, region, new Color(Colors.White, alpha * (.75f + .25f * ready)));
        if (!progress.HasValue) return;
        float width = size * .52f;
        var bar = new Rect2(anchor.X - width / 2, rebound ? anchor.Y - .9f : Field.Top + size - 1.2f, width, .8f).Intersection(Field.Rect);
        DrawRect(bar, new Color(EasyOutline, alpha));
        if (ready > 0)
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * ready, bar.Size.Y)), new Color(RainVisual(rebound).Accent, alpha));
    }

    private void DrawRainForecast()
    {
        foreach (bool rebound in new[] { false, true })
        {
            if (_rainForecastDots.ContainsKey(rebound)) continue;
            var art = RainVisual(rebound);
            float radius = Evolved ? BulletPool.BossChargeRadius : BulletPool.BossRadius;
            var footprint = BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position, Bullet.BossArtSize(radius, Evolved));
            var contours = footprint.Contours.Select(points => points.Select(point => point * EasyRainRadius() / footprint.Radius).ToArray());
            var dots = new List<Vector2>();
            foreach (var contour in contours)
            {
                float length = 0;
                for (int i = 0; i < contour.Length; i++) length += contour[i].DistanceTo(contour[(i + 1) % contour.Length]);
                int count = Math.Max(8, Mathf.CeilToInt(length / 2));
                float spacing = length / count, travelled = 0, next = 0;
                for (int i = 0; i < contour.Length; i++)
                {
                    Vector2 a = contour[i], b = contour[(i + 1) % contour.Length];
                    float edge = a.DistanceTo(b);
                    while (next < travelled + edge)
                    {
                        dots.Add(a.Lerp(b, (next - travelled) / edge));
                        next += spacing;
                    }
                    travelled += edge;
                }
            }
            _rainForecastDots[rebound] = dots.ToArray();
            foreach (var dot in dots) _rainForecastRadius = Math.Max(_rainForecastRadius, dot.Length() + 1);
            PrepareRainForecastArt(rebound);
        }
        int forecastRow = -1;
        double forecastAge = 0;
        for (int i = 0; i < _easyRainReserved.Length; i++)
        {
            double age = Time - RainWarningAt(i / EasyRainRows, i % EasyRainRows);
            if (_easyRainReserved[i] && age >= 0 && age < RainPreview + RainHold)
            {
                forecastRow = i;
                forecastAge = age;
            }
        }
        bool predicting = forecastRow >= 0;
        bool holding = predicting && forecastAge >= RainPreview;
        double clock = Math.Max(0, (Time - RainAttackAt) * RainAttackSpeed);
        foreach (var particle in _rainParticles)
        {
            if (particle.Side != 0 || particle.Burst != 0) continue;
            double bounceAge = clock - (particle.Launch + particle.Duration);
            if (particle.Landed && bounceAge <= (EasyRainBursts - 1) * EasyRainBurstInterval + .15)
                DrawEasyTurnMark(EasyRainImpactPoint(particle.Origin), Vector2.Down, Vector2.Up, .8f, ink: RainVisual(true).Accent);
            double rowAge = Time - RainWarningAt(particle.Wave, particle.Row);
            bool rowHolding = rowAge >= RainPreview && rowAge < RainPreview + RainHold;
            if (rowHolding)
                DrawRainSourceCue(particle.Origin, false, (float)((rowAge - RainPreview) / RainHold), 1);
            if (particle.Wave * EasyRainRows + particle.Row != forecastRow) continue;
            double previewBounce = RainForecastAge(particle) - particle.Duration;
            if (!holding && previewBounce >= -.8 && previewBounce <= (EasyRainBursts - 1) * EasyRainBurstInterval + .15)
                DrawRainSourceCue(EasyRainImpactPoint(particle.Origin), true, null, 1);
            if (!holding && RainForecastAge(particle) <= (EasyRainBursts - 1) * EasyRainBurstInterval + .2)
                DrawRainSourceCue(particle.Origin, false, null, 1);
        }
        int wave = predicting ? forecastRow / EasyRainRows : Math.Min(EasyRainWaves - 1, (int)(clock / EasyRainWaveInterval));
        int row = predicting ? forecastRow % EasyRainRows : Math.Min(EasyRainRows - 1, (int)((clock - wave * EasyRainWaveInterval) / EasyRainRowInterval));
        Vector2 indicator = new(Field.Left + 14, Field.Top + 13);
        AttackTelegraph.Pips(this, indicator, wave + 1, EasyLight);
        for (int i = 0; i < EasyRainRows; i++)
        {
            Vector2 at = indicator + new Vector2(18 + i * 10, 0);
            Color rowInk = new(EasyLight, i == row ? .9f : .3f);
            DrawEasyWarningArc(at, 3, 0, Mathf.Tau, rowInk, true);
            if (i == row && !predicting) DrawCircle(at, 1.5f, rowInk);
            if (i == row && holding)
                DrawArc(at, 5, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau *
                    Mathf.Clamp((float)(1 - (forecastAge - RainPreview) / RainHold), .001f, 1), 24, rowInk, .8f, true);
        }
        if (predicting && !holding)
            foreach (var particle in _rainParticles)
            {
                if (particle.Wave * EasyRainRows + particle.Row != forecastRow) continue;
                double age = RainForecastAge(particle);
                if (age < 0 || age >= particle.Duration) continue;
                Vector2 position = RainPosition(particle, age);
                float angle = RainVelocity(particle, age).Angle();
                bool rebound = particle.Side != 0;
                var art = RainForecastArt[(Evolved, rebound)];
                if (Field.Rect.Grow(-_rainForecastRadius).HasPoint(position))
                {
                    DrawSetTransform(position, angle);
                    DrawTextureRect(art.Texture, art.Bounds, false);
                    DrawSetTransform(Vector2.Zero);
                    continue;
                }
                foreach (var dot in _rainForecastDots[rebound])
                {
                    Vector2 at = position + dot.Rotated(angle);
                    if (!Field.Rect.HasPoint(at)) continue;
                    DrawCircle(at, .85f, EasyOutline, antialiased: true);
                    DrawCircle(at, .48f, RainVisual(rebound).Accent, antialiased: true);
                }
            }
    }

    private void PrepareRainForecastArt(bool rebound)
    {
        if (RainForecastArt.ContainsKey((Evolved, rebound))) return;
        const int scale = 6;
        var dots = _rainForecastDots[rebound];
        Vector2 minimum = dots[0], maximum = minimum;
        foreach (var dot in dots) { minimum = minimum.Min(dot); maximum = maximum.Max(dot); }
        minimum = (minimum - Vector2.One).Floor();
        maximum = (maximum + Vector2.One).Ceil();
        var bounds = new Rect2(minimum, maximum - minimum);
        int width = (int)bounds.Size.X * scale, height = (int)bounds.Size.Y * scale;
        {
            var pixels = new byte[width * height * 4];
            foreach (var dot in dots)
            {
                Vector2 center = (dot - minimum) * scale;
                for (int y = Math.Max(0, (int)center.Y - scale); y < Math.Min(height, (int)center.Y + scale + 1); y++)
                for (int x = Math.Max(0, (int)center.X - scale); x < Math.Min(width, (int)center.X + scale + 1); x++)
                {
                    float distance = new Vector2(x + .5f, y + .5f).DistanceTo(center);
                    float alpha = Mathf.Clamp(.85f * scale - distance + .5f, 0, 1);
                    if (alpha == 0) continue;
                    Color color = EasyOutline.Lerp(RainVisual(rebound).Accent, Mathf.Clamp(.48f * scale - distance + .5f, 0, 1));
                    int offset = (y * width + x) * 4;
                    float previous = pixels[offset + 3] / 255f, combined = alpha + previous * (1 - alpha);
                    for (int channel = 0; channel < 3; channel++)
                        pixels[offset + channel] = (byte)Mathf.RoundToInt((color[channel] * alpha
                            + pixels[offset + channel] / 255f * previous * (1 - alpha)) / combined * 255);
                    pixels[offset + 3] = (byte)Mathf.RoundToInt(combined * 255);
                }
            }
            using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
            RainForecastArt[(Evolved, rebound)] = (ImageTexture.CreateFromImage(image), bounds);
        }
    }

    private void DrawEasySilentWave()
    {
        var art = EasyArt(ref _easyMessageArt, "easy_silent_message");
        for (int wave = 0; wave < 2; wave++)
        {
            if (_easyWaveLaunches[wave] < 0) continue;
            double elapsed = Time - _easyWaveLaunches[wave];
            if (elapsed >= EasyWaveTrainDuration) continue;
            float radius = EasyWaveArtRadius(wave);
            if (elapsed < 0)
            {
                var stages = EasyWaveWarningProgress(wave);
                if (stages.Expiry > 0) DrawEasyWaveExpiryArea(wave, stages.Expiry);
                float progress = Progress(_easyWaveWarningStarts[wave], _easyWaveLaunches[wave] - _easyWaveWarningStarts[wave]);
                Vector2 size = new(14, 9);
                Color guide = new(AttackTelegraph.Sweep, .85f);
                if (stages.Ring > 0)
                {
                    AttackTelegraph.Disc(this, _origin, 20 * stages.Ring, guide);
                    DrawEasyWarningArt(art, new Rect2(_origin - size / 2, size), guide, true);
                    DrawEasyWaveExpansionWarning(wave, stages.Ring);
                }
                DrawEasyWaveSpawnMark(_origin, -Mathf.Pi / 2, 1);
            }
            if (elapsed < 0) continue;
            for (int pulse = 0; pulse < EasyWavePulses; pulse++)
            {
                double age = elapsed - pulse * EasyWavePulseInterval;
                if (age < 0 || age >= EasyWaveDuration) continue;
                radius = EasyWaveRadius(age);
                if (age < .3)
                {
                    DrawCircle(_origin, 20, EasyOutline);
                    DrawCircle(_origin, 19, EasyHeat);
                    DrawArc(_origin, 19.2f, 0, Mathf.Tau, 64, EasyLight, .6f, true);
                    DrawTextureRect(art, new Rect2(_origin - new Vector2(16, 10), new Vector2(32, 20)), false, Colors.White);
                }
                for (int i = 0; i < _easyRingPoints.Length; i++)
                {
                    _easyRingPoints[i] = EasyWavePoint(wave, i, radius);
                    _easyRingInner[i] = EasyWavePoint(wave, i, radius - 1.65f);
                    _easyRingOuter[i] = EasyWavePoint(wave, i, radius + 1.65f);
                }
                foreach (var range in EasyWaveLiveRanges(age))
                {
                    if (range.First >= range.End) continue;
                    DrawEasyWaveBand(range.First, range.End, EasyOutline, 4);
                    DrawEasyWaveBand(range.First, range.End, EasyHeat, 2.9f);
                    for (int i = range.First; i < range.End; i++)
                    {
                        DrawEasyClippedLine(_easyRingInner[i], _easyRingInner[i + 1], EasyLight, .45f);
                        DrawEasyClippedLine(_easyRingOuter[i], _easyRingOuter[i + 1], EasyLight, .45f);
                    }
                    if (age < EasyWaveOpeningAt) continue;
                    DrawEasyWaveCap(_easyRingPoints[range.First], _easyRingPoints[range.First].DirectionTo(_easyRingPoints[range.First + 1]).Angle() + Mathf.Pi / 2);
                    DrawEasyWaveCap(_easyRingPoints[range.End], _easyRingPoints[range.End - 1].DirectionTo(_easyRingPoints[range.End]).Angle() - Mathf.Pi / 2);
                }
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
        AttackTelegraph.Ring(this, _origin, Math.Min(openingRadius, frontier), 2, 0, Mathf.Tau, AttackTelegraph.Sweep);
        if (frontier > openingRadius)
        {
            for (int segment = 0; segment < EasyWaveSegments; segment++)
            {
                float from = Mathf.Lerp(gapAngle + halfGap, gapAngle - halfGap + Mathf.Tau, segment / (float)EasyWaveSegments);
                float to = Mathf.Lerp(gapAngle + halfGap, gapAngle - halfGap + Mathf.Tau, (segment + 1) / (float)EasyWaveSegments);
                Vector2 a = Vector2.FromAngle(from), b = Vector2.FromAngle(to);
                DrawEasyClippedPolygon(new[] { _origin + a * openingRadius, _origin + a * frontier,
                    _origin + b * frontier, _origin + b * openingRadius }, new Color(AttackTelegraph.Sweep, .06f));
            }
            AttackTelegraph.Ring(this, _origin, frontier, 2, gapAngle + halfGap,
                gapAngle - halfGap + Mathf.Tau, new Color(AttackTelegraph.Sweep, .55f));
        }
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

    private void DrawEasyWaveSpawnMark(Vector2 center, float angle, float alpha, Color? ink = null)
    {
        Color color = new(ink ?? EasyDanger, alpha);
        AttackTelegraph.Origin(this, center, color);
        EasyDirectionArrow(center + Vector2.FromAngle(angle) * 7, Vector2.FromAngle(angle), ink: color);
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
        if (normal == Vector2.Zero) return;
        if (Field.Rect.Grow(-width / 2).HasPoint(a) && Field.Rect.Grow(-width / 2).HasPoint(b))
        {
            _easyLineVertices[0] = a + normal;
            _easyLineVertices[1] = b + normal;
            _easyLineVertices[2] = b - normal;
            _easyLineVertices[3] = a - normal;
            Array.Fill(_easyLineColors, color);
            DrawPrimitive(_easyLineVertices, _easyLineColors, Array.Empty<Vector2>());
            return;
        }
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
        var points = new Vector2[polygon.Count];
        for (int i = 0; i < polygon.Count; i++) points[i] = polygon[i];
        var indices = new List<int>((polygon.Count - 2) * 3);
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            Vector2 a = polygon[i] - origin, b = polygon[i + 1] - origin;
            double area = (double)a.X * b.Y - (double)a.Y * b.X;
            if (Math.Abs(area) < .00001) continue;
            indices.Add(0);
            indices.Add(i);
            indices.Add(i + 1);
        }
        if (indices.Count > 0)
            RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), indices.ToArray(), points, new[] { color });
    }
}
