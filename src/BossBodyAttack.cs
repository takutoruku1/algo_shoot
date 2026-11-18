using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BossBodyAttack : BossCharacterTechnique
{
    private Vector2[] _route = Array.Empty<Vector2>();
    private float[] _lengths = Array.Empty<float>();
    private Vector2 _deliveryFrom, _deliveryTo;
    private double _cueAt, _fireAt, _endAt;
    private double _imprintAt;
    private Vector2 _imprint;
    private readonly List<Vector2> _filmCoil = new();
    private bool _returning, _moving;
    private int _passes;
    private sealed record BodyVolley(Vector2 Origin, Vector2 Direction, double FiresAt, int Count, int SinglePulse = -1);
    private readonly List<BodyVolley> _receipts = new(), _frames = new();
    private readonly Dictionary<BodyVolley, FanRay[]> _receiptRays = new();
    private readonly List<(Bullet Shot, ulong Id, Vector2 From, Vector2 Velocity, double Born)> _printedReceipts = new();
    private FanRay[] ReceiptRays(BodyVolley volley)
    {
        if (_receiptRays.TryGetValue(volley, out var rays)) return rays;
        return _receiptRays[volley] = Tier == 2
            ? SplitFan(volley.Origin, volley.Direction, Mathf.Pi * .75f, .19f)
            : Enumerable.Range(0, volley.Count).Select(i => new FanRay(volley.Origin,
                volley.Direction.Rotated((i / (float)(volley.Count - 1) - .5f) * Mathf.Pi * 1.5f))).ToArray();
    }
    internal override bool UsesArenaBody => true;
    protected override bool UsesCustomHazardArt => true;
    private bool Delivery => Boss.BodyShotJob == Job.Melee;
    internal float ContactRadius => Boss is CameoBoss ? 19 : 23;
    internal bool BodyMoving => _moving;
    internal int CompletedPasses => _passes;
    internal int ExpectedPasses => Delivery ? (Tier switch { 2 => 8, 3 => 10, _ => 6 })
        : (Tier switch { 1 => 3, 2 => 4, 3 => 5, _ => 2 });
    internal IReadOnlyList<Vector2> VisibleBodyRoute => _route;
    internal double VisibleFireAt => _fireAt;
    internal double VisibleEndAt => _endAt;
    internal override IEnumerable<BossEasyCue> EasyCues
    {
        get
        {
            if (_passes < ExpectedPasses)
                yield return new($"body:route:{_passes}", _cueAt, _fireAt, 1, _moving);
        }
    }
    protected override bool UsesDangerForecast => true;
    protected override Texture2D? AttackLineMotif => WarningVisual.Texture;
    protected override Color AttackLineInk => Delivery ? new("ff9ba6") : new("f6df91");
    protected override BulletArt.PlayerVisual WarningVisual => Delivery
        ? BulletArt.EnemyVisual("res://assets/fx/akari_read_receipt.svg", "ff7db4")
        : BulletArt.EnemyVisual("rei_film", "c7a4ff");

    protected override void Start()
    {
        if (Delivery)
        {
            Identify("今度は、手渡しで", "akari.body-delivery-return", Tier >= 2
                ? "宛先と折返しを誘導し、到着地点から返る受領票の隙間を抜ける"
                : "宛先を横へ誘い、到着地点から広がる受領票をかわす。折返しでは受け取りに来た位置を通る");
            PrepareDelivery();
        }
        else
        {
            Identify("枠の外まで、見て", "rei.body-spiral-close", Tier >= 2
                ? "撮影軌道の外へ抜け、通過点から接線に沿って飛ぶフィルムの列をかわす"
                : "巻き込む撮影軌道を読み、レイが一周して中心へ来る前に輪の外へ抜ける");
            PrepareSpiral();
            _imprint = _route[^1];
            float pitch = 54 - 3 * Tier;
            float reach = pitch * 1.15f;
            float angle = 0;
            while (angle * pitch / Mathf.Tau < reach)
            {
                float r = angle * pitch / Mathf.Tau;
                _filmCoil.Add(Vector2.FromAngle(_imprint.Y < Field.CenterY ? angle : -angle) * r);
                angle += 2 * Mathf.Acos(1 - .2f / Math.Max(pitch, r));
            }
        }
        _imprintAt = _fireAt;
    }

    private Vector2 InField(Vector2 point) => point.Clamp(
        new Vector2(Field.Left + 12, Field.Top + 12), new Vector2(Field.Right - 12, Field.Bottom - 12));

    private void PrepareDelivery()
    {
        if (!_returning)
        {
            _deliveryFrom = Boss.GlobalPosition;
            _deliveryTo = InField(Player.GlobalPosition);
        }
        Vector2[] route = !_returning ? new[] { _deliveryFrom, _deliveryTo }
            : new[] { _deliveryTo, InField(Player.GlobalPosition), _deliveryFrom };
        SetRoute(route, Tier switch { 0 => 96, 1 => 124, 2 => 164, _ => 188 });
    }

    private void PrepareSpiral()
    {
        Vector2 center = InField(Player.GlobalPosition);
        Vector2 initial = (Boss.GlobalPosition - center).LimitLength(Tier switch { 2 => 90, 3 => 102, _ => 74 });
        float direction = center.Y < Field.CenterY ? 1 : -1;
        var points = new Vector2[98];
        points[0] = Boss.GlobalPosition;
        for (int i = 1; i < points.Length; i++)
        {
            float t = (i - 1) / (float)(points.Length - 2);
            points[i] = InField(center + initial.Rotated(direction * Mathf.Tau * t) * (1 - t));
        }
        SetRoute(points, Tier switch { 0 => 80, 1 => 104, 2 => 142, _ => 166 });
    }

    private void SetRoute(Vector2[] route, float speed)
    {
        _route = route;
        _lengths = new float[route.Length];
        for (int i = 1; i < route.Length; i++) _lengths[i] = _lengths[i - 1] + route[i - 1].DistanceTo(route[i]);
        _cueAt = Time;
        float escape = float.PositiveInfinity;
        for (float y = 8; y < Field.Bottom; y += 8)
        for (float x = Field.Left + 8; x < Field.Right; x += 8)
        {
            Vector2 candidate = new(x, y);
            float travel = Player.GlobalPosition.DistanceTo(candidate);
            if (travel >= escape) continue;
            if (Enumerable.Range(1, route.Length - 1).Any(i => Geometry2D.GetClosestPointToSegment(candidate,
                route[i - 1], route[i]).DistanceTo(candidate) < ContactRadius + Player.HitRadius + 6)) continue;
            escape = travel;
        }
        _fireAt = Time + WarnFor(escape + 12, Tier == 3 ? 1.3 : 1.5);
        _endAt = _fireAt + Math.Max(.65, _lengths[^1] / speed);
        {
            if (Delivery)
            {
                int handoff = Tier < 2 && _returning ? 1 : route.Length - 1;
                Vector2 origin = route[handoff], heading = origin.DirectionTo(route[0]);
                if (Tier < 2 && _returning) heading = -heading;
                double arrivesAt = _fireAt + _lengths[handoff] / Math.Max(1, _lengths[^1]) * (_endAt - _fireAt);
                if (heading == Vector2.Zero) heading = Vector2.FromAngle(_passes * Mathf.Pi / 3);
                int copies = Tier switch { 0 => 7, 1 => 9, 2 => 13, _ => 17 };
                if (Tier == 0 && _returning)
                {
                    float fold = Mathf.Clamp(heading.AngleTo(origin.DirectionTo(route[^1])), -Mathf.DegToRad(75), Mathf.DegToRad(75));
                    _receipts.Add(new(origin, heading, arrivesAt + .35, copies, 0));
                    _receipts.Add(new(origin, heading.Rotated(fold), arrivesAt + .83, copies, 1));
                }
                else _receipts.Add(new(origin, heading, arrivesAt + .35, copies));
            }
            else
                for (int frame = 0; frame < 8; frame++)
                {
                    if (Tier < 2 && frame % (Tier == 0 ? 3 : 2) != 0) continue;
                    int index = 1 + frame * 10;
                    double arrival = _fireAt + _lengths[index] / Math.Max(1, _lengths[^1]) * (_endAt - _fireAt);
                    Vector2 tangent = route[index].DirectionTo(route[index + 1]);
                    if (tangent == Vector2.Zero) tangent = Vector2.FromAngle(frame * Mathf.Tau / 8 + _passes * .3f);
                    _frames.Add(new(route[index], tangent, arrival + ExposureLead, Tier < 2 ? 3 + Tier : Tier == 2 ? 5 : 7));
                }
        }
        _moving = false;
    }

    protected override void Step(double delta)
    {
        if (!Delivery) _imprint = _imprint.MoveToward(_route[^1], 19 * (float)delta);
        if (Delivery) DeliveryReceipts();
        else SpiralFrames();
        if (_passes >= ExpectedPasses)
        {
            double last = (Delivery ? _receipts : _frames).Max(volley => volley.FiresAt);
            if (!Delivery && Tier != 2)
            {
                if (Time > last + (ExposureCount - 1) * ExposurePitch / 48 && !OwnedShots.Any()) Finish();
            }
            else if (Time > last + .6 && (!Delivery || !OwnedShots.Any())) Finish();
            if (!Finished) ImprintField();
            return;
        }
        if (Time >= _endAt + (Tier switch { 0 => .3, 1 => .2, 2 => .12, _ => .08 }))
        {
            _passes++;
            if (_passes >= ExpectedPasses)
            {
                _moving = false;
                ImprintField();
                return;
            }
            if (Delivery) { _returning = !_returning; PrepareDelivery(); }
            else PrepareSpiral();
        }
        _moving = Time >= _fireAt && Time < _endAt;
        if (Time < _fireAt)
            for (int i = 1; i < _route.Length; i++)
                Line(_route[i - 1], _route[i], ContactRadius, false);
        else
        {
            float distance = _lengths[^1] * (float)Math.Clamp((Time - _fireAt) / (_endAt - _fireAt), 0, 1);
            int segment = 1;
            while (segment < _route.Length - 1 && _lengths[segment] < distance) segment++;
            float span = _lengths[segment] - _lengths[segment - 1];
            Boss.GlobalPosition = _route[segment - 1].Lerp(_route[segment], span > .001f
                ? Math.Clamp((distance - _lengths[segment - 1]) / span, 0, 1) : 1);
            if (_moving) Disc(Boss.GlobalPosition, ContactRadius, true);
        }
        if (At(_fireAt)) Boss.ShowAnimalAttack();
        ImprintField();
        LastPosition = Boss.GlobalPosition;
    }

    private void ImprintField()
    {
        bool live = Time >= _imprintAt;
        if (!Delivery)
        {
            Vector2 center = _imprint + (Boss.GlobalPosition - _imprint).LimitLength(83) * .12f;
            float sweep = Mathf.Clamp((float)((Time - _fireAt) / (_endAt - _fireAt)), 0, 1);
            int tip = Mathf.Clamp(Mathf.CeilToInt(sweep * (_filmCoil.Count - 1)), 1, _filmCoil.Count - 1);
            int heel = Math.Max(1, tip - _filmCoil.Count / 3);
            for (int i = live ? heel : 1; i <= (live ? tip : _filmCoil.Count - 1); i++)
            {
                Vector2 a = center + _filmCoil[i - 1], b = center + _filmCoil[i];
                if (new Rect2(a, b - a).Abs().Grow(3).Intersects(Field.Rect))
                    Line(a, b, 3, live);
            }
        }
    }

    private void DeliveryReceipts()
    {
        foreach (var receipt in _receipts)
        foreach (var (from, direction) in ReceiptRays(receipt))
        {
            Vector2 at = from;
            if (Time < receipt.FiresAt)
                Line(at, at + direction * 420, 3, false);
            int pulses = receipt.SinglePulse >= 0 ? 1 : Tier == 0 ? 2 : 3;
            for (int print = 0; print < pulses; print++)
                if (At(receipt.FiresAt + print * .18))
                {
                    int pulse = receipt.SinglePulse >= 0 ? receipt.SinglePulse : print;
                    var shot = Shot(at, direction);
                    shot.Homing = shot.Accel = false;
                    shot.UseBossIllustration(WarningVisual, fitRadius: 3);
                    shot.Velocity = direction * (54 + pulse * 8);
                    shot.BossPathDriven = true;
                    shot.CancelLeadIn();
                    _printedReceipts.Add((shot, shot.ActivationId, from, shot.Velocity, Time));
                }
        }
        _printedReceipts.RemoveAll(p => !p.Shot.Active || p.Shot.ActivationId != p.Id);
        foreach (var p in _printedReceipts)
        {
            Vector2 position = p.From + p.Velocity * (float)(Time - p.Born);
            if (Field.Rect.Grow(24).HasPoint(position)) p.Shot.GlobalPosition = position;
            else Pool.Despawn(p.Shot);
        }
    }

    private int ExposureCount => Tier == 1 ? 2 : 3;
    private double ExposureLead => Tier == 0 ? 2.4 : 1.4;
    private float ExposurePitch => 6 + TargetOpening;

    private void SpiralFrames()
    {
        foreach (var frame in _frames)
        for (int copy = 0; copy < frame.Count; copy++)
        {
            Vector2 from = frame.Origin + frame.Direction.Orthogonal() * ((copy - (frame.Count - 1) * .5f) * ExposurePitch);
            if (Time >= frame.FiresAt - ExposureLead && Time < frame.FiresAt)
                Line(from, from + frame.Direction * 420, 3, false);
            for (int exposure = 0; exposure < ExposureCount; exposure++)
                if (At(frame.FiresAt + exposure * ExposurePitch / 48))
                {
                    var shot = Shot(from, frame.Direction);
                    shot.Homing = shot.Accel = false;
                    shot.MakeReiSplit(0);
                    shot.UseBossIllustration(WarningVisual, fitRadius: 3);
                    shot.Velocity = frame.Direction * 48;
                    shot.CancelLeadIn();
                }
        }
    }

    protected override void DrawTechnique()
    {
        Color ink = AttackLineInk;
        if (GameManager.ShowAttackTelegraphs)
            foreach (var volley in (Delivery ? _receipts : _frames).Where(volley => Time < volley.FiresAt
                && (Delivery || Time >= volley.FiresAt - ExposureLead)))
            {
                if (!Delivery)
                {
                    Vector2 side = volley.Direction.Orthogonal();
                    float radius = 3 + (volley.Count - 1) * ExposurePitch / 2;
                    foreach (int edge in new[] { -1, 1 })
                    {
                        Vector2 from = volley.Origin + side * radius * edge;
                        DrawDashedLine(from, from + volley.Direction * 420, new Color(WarningVisual.Accent, .45f), .6f, 5, true);
                    }
                    for (int copy = 0; copy < volley.Count; copy++)
                    {
                        Vector2 from = volley.Origin + side * ((copy - (volley.Count - 1) * .5f) * ExposurePitch);
                        DrawDashedLine(from, from + volley.Direction * 420, new Color(WarningVisual.Accent, .22f), .45f, 4, true);
                        AttackTelegraph.ProjectileOrigin(this, from, WarningVisual, new Color(WarningVisual.Accent, .6f), 4);
                    }
                    AttackTelegraph.Arrow(this, volley.Origin + volley.Direction * 38, volley.Direction, new Color(WarningVisual.Accent, .7f));
                    continue;
                }
                if (volley.SinglePulse == 1)
                    AttackTelegraph.DrawSymbol(this, volley.Origin + Vector2.Up * 12, AttackTelegraph.Symbol.Undo, ink);
                foreach (var (from, direction) in ReceiptRays(volley))
                {
                    Vector2 at = from;
                    AttackTelegraph.Route(this, at, at + direction * 420, 3, true, true, false, false, WarningVisual);
                }
            }
        if (GameManager.ShowAttackTelegraphs && Time < _fireAt)
        {
            for (int i = 1; i < _route.Length; i++)
            {
                DrawDashedLine(_route[i - 1], _route[i], new Color(AttackTelegraph.Outline, .8f), 1.8f, Delivery ? 5 : 2, true);
                DrawDashedLine(_route[i - 1], _route[i], new Color(ink, .7f), .65f, Delivery ? 5 : 2, true);
                if (i % (Delivery ? 1 : 12) == 0)
                    AttackTelegraph.Arrow(this, _route[i - 1].Lerp(_route[i], .5f), _route[i - 1].DirectionTo(_route[i]), ink);
            }
            AttackTelegraph.Disc(this, _route[^1], ContactRadius, new Color(ink, .6f), false);
            if (Delivery && _returning && Tier >= 2)
            {
                AttackTelegraph.Disc(this, _route[1], ContactRadius, new Color(ink, .6f), false);
                AttackTelegraph.DrawSymbol(this, _route[1] + Vector2.Up * (ContactRadius + 8), AttackTelegraph.Symbol.Undo, ink);
            }
            Vector2 mark = _route[0];
            if (Delivery)
            {
                DrawRect(new Rect2(mark - new Vector2(6, 4), new Vector2(12, 8)), ink, false, .8f);
                DrawPolyline(new[] { mark + new Vector2(-6, -4), mark, mark + new Vector2(6, -4) }, ink, .8f, true);
            }
            else
                for (int side = -1; side <= 1; side += 2)
                    DrawPolyline(new[] { mark + new Vector2(side * 4, -6), mark + new Vector2(side * 7, -6),
                        mark + new Vector2(side * 7, 6), mark + new Vector2(side * 4, 6) }, ink, .9f, true);
            AttackTelegraph.DrawSymbol(this, Boss.GlobalPosition + Vector2.Down * (ContactRadius + 13), Delivery ? AttackTelegraph.Symbol.Envelope : AttackTelegraph.Symbol.Cut, ink);
        }
        DrawBodyContact(ContactRadius, ink, _moving);
    }
}
