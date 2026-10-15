using Godot;
using System;
using System.Collections.Generic;

public partial class KoharuBossTechnique : BossCharacterTechnique
{
    private sealed class Light
    {
        public Bullet Bullet = null!;
        public ulong Activation;
        public Vector2 Origin;
        public double Born;
        public int Index;
        public float Angle;
        public Light? Partner;
    }

    private sealed class SupportLight
    {
        internal Light Light = null!;
        internal float Along, Velocity;
        internal double DropAt = -1;
        internal Vector2 DropOrigin, DropVelocity;
    }

    private readonly List<Light> _lights = new();
    private readonly List<SupportLight> _supportLights = new();
    private readonly List<(Vector2 Position, double Time)> _tail = new();
    private Vector2 _origin, _anchor, _point, _destination, _lastPlayer;
    private double _warning, _charge, _firedAt = -1;
    private float _angle, _radius;
    private float _supportTilt;
    private int _phase;
    private static Vector2 Center => new(Field.CenterX, Field.CenterY);
    private int Density => new[] { 1, 2, 3, 4 }[Tier];
    private float Speed => new[] { 0.78f, 1f, 1.12f, 1.22f }[Tier];
    private double ReadingSeconds => Tier switch { 2 => 1.4, 3 => 1.2, _ => 1.6 };
    private const double ReadingTurn = 0.55;
    private float CoilContraction => Tier switch { 2 => 13.6f, 3 => 14.1f, _ => 13f };
    private const float SupportHalf = 118;
    private const double SupportDropWarning = 1.2;
    private double SupportInterval => Tier switch { 0 => 1.4, 1 => 1.3, 2 => 1.15, _ => 1.0 };
    private Vector2 SupportPoint(float along) => Center + new Vector2(along, -7).Rotated(_supportTilt);

    protected override void Start()
    {
        _origin = Boss.GlobalPosition;
        _anchor = _lastPlayer = Player.GlobalPosition;
        _warning = WarnFor(48, 1.4);
        switch (Kind)
        {
            case BossTechniqueKind.Normal:
                if (Variant == 0)
                {
                    Identify("約束の並木", "koharu.braided-lights", "交わる列の尾を見て、その後ろを横切る");
                    _point = new Vector2(Mathf.Clamp(_anchor.X, Field.Left + 70, Field.Right - 70), Field.CenterY);
                }
                else if (Variant == 1)
                    Identify("振り向く祈り", "koharu.single-glance-turn", "光が振り向く先を確定させてから切り返す");
                else
                {
                    Identify("想いの花冠", "koharu.elliptic-petals", "花弁の間で、回転する向きに一緒に回る");
                    _warning = WarnFor(70, 1.7);
                }
                break;
            case BossTechniqueKind.Edge:
                Identify("拍手の往復", "koharu.clap-annihilation", "左右の光がぶつかって消えた後ろへ入る");
                _point = new Vector2(Mathf.Clamp(_anchor.X, Field.Left + 72, Field.Right - 72), 0);
                break;
            case BossTechniqueKind.Area:
                if (Variant == 0)
                {
                    Identify("画面の光", "koharu.screen-aperture", "広がる光の角度から外へ出る");
                    _angle = _origin.DirectionTo(_anchor).Angle();
                    float escape = float.MaxValue;
                    var facing = Vector2.FromAngle(_angle);
                    _radius = ((BossKoharu)Boss).BodyExtent + 22;
                    float bodyClearance = Math.Min(((BossKoharu)Boss).BodyExtent + 8, _origin.DistanceTo(_anchor) - 1);
                    if (_origin.DistanceTo(_anchor) < _radius - 9) escape = 0;
                    for (float x = Field.Left + 8; x <= Field.Right - 8; x += 10)
                    for (float y = Field.Top + 8; y <= Field.Bottom - 8; y += 10)
                    {
                        var candidate = new Vector2(x, y);
                        var offset = candidate - _origin;
                        float along = facing.Dot(offset);
                        if (offset.Length() >= _radius - 9
                            && Mathf.Abs(facing.Cross(offset)) <= along * Mathf.Tan(0.52f) + 12) continue;
                        if (_origin.DistanceTo(Geometry2D.GetClosestPointToSegment(_origin, _anchor, candidate))
                            < bodyClearance) continue;
                        escape = Math.Min(escape, _anchor.DistanceTo(candidate));
                    }
                    _warning = WarnFor(escape + 5, 1.5);
                }
                else if (Variant == 1)
                    Identify("視線のかたまり", "koharu.motion-observation", "見つめられている間は止まり、視線が固まったら離れる");
                else
                    Identify("既読の線", "koharu.reading-head", "線全体ではなく、走っている光の先端を避ける");
                break;
            case BossTechniqueKind.Signature:
                Identify("枝角・結び目の檻", "koharu.hinged-antlers", "閉じる枝角の先から離れ、外側へ回る");
                _point = new Vector2(Mathf.Clamp(_anchor.X, Field.Left + 70, Field.Right - 70),
                    Mathf.Clamp(_anchor.Y, Field.Top + 70, Field.Bottom - 70));
                _warning = WarnFor(76, 1.8);
                break;
            case BossTechniqueKind.Finale:
                Identify("自分なにしてんだろ", "koharu.cross-reorientation", "十字の間へ離れ、向きが変わる前にもう一歩ずれる");
                _warning = WarnFor(32, 1.35);
                break;
            case BossTechniqueKind.Crossfire:
                switch (Variant)
                {
                    case 0: Identify("どこにいても見つける", "koharu.pursuit-train", "先頭を大きく引きつけ、曲がり切れない尾の内側へ回る"); break;
                    case 1:
                        Identify("届いて、全部", "koharu.unbalanced-support", "寄る側へ支えが傾く。転がる光の勢いと、端から落ちる先を見る");
                        _warning = WarnFor(112, 2.4);
                        break;
                    case 2: Identify("すれ違う祈り", "koharu.parabolic-exchange", "上下を交換する弧の外側に回る"); break;
                    case 3:
                        Identify("視線のリレー", "koharu.overlapping-gazes", "一本の視線は無害。二つが重なる場所から離れる");
                        _point = _destination = _anchor;
                        _warning = WarnFor(40, 1.4);
                        break;
                    default:
                        Identify("結び直す約束", "koharu.unpaired-light", "結ばれなかった一つの光を見つけ、その正面から離れる");
                        break;
                }
                break;
            case BossTechniqueKind.Dodge:
                Identify("締めつける声援", "koharu.inward-coil", "輪の中心側で待つか、予兆中に外へ出る。回避なら輪を越せる");
                _point = new Vector2(Mathf.Clamp(_anchor.X, Field.Left + 60, Field.Right - 60),
                    Mathf.Clamp(_anchor.Y, Field.Top + 60, Field.Bottom - 60));
                _warning = GameManager.Instance!.HasDodge ? WarnFor(50, 1.5) : WarnFor(160, 2.2);
                break;
            case BossTechniqueKind.Extra:
                if (!Evolved && Variant == 0)
                    Identify("ペンライトの休符", "koharu.three-beat-rest", "二拍の光を待ち、三拍目の休符で横切る");
                else if (!Evolved)
                    Identify("黒い画面のわたし", "koharu.live-mirror", "自分と逆に動く鏡像を、縦の動きで外す");
                else if (Variant == 0)
                {
                    Identify("枝角の跳躍", "koharu.deer-leap", "跳ぶ前の影を見て、着地点と軌道から離れる");
                    _destination = new Vector2(Mathf.Clamp(_anchor.X, Field.Left + 30, Field.Right - 30),
                        Mathf.Clamp(_anchor.Y, Field.Top + 36, Field.Bottom - 36));
                    _warning = WarnFor(78, 2.1);
                }
                else
                    Identify("見ていないと、消える", "koharu.attention-charge", "近くで膨らむ光から距離を取り、注目の圧力を鎮める");
                break;
            case BossTechniqueKind.Cameo:
                if (Variant == 0)
                    Identify("降り積もる祈り", "koharu.domino-fall", "順に倒れる光を見て、通り過ぎた列へ移る");
                else
                    Identify("あなたを見つける", "koharu.searching-glance", "探している視線を追わず、最後に狙いが止まってから離れる");
                break;
        }
    }

    protected override void Step(double delta)
    {
        switch (Kind)
        {
            case BossTechniqueKind.Normal:
                if (Variant == 0) BraidedLights();
                else if (Variant == 1) TurningPrayer();
                else FlowerCrown();
                break;
            case BossTechniqueKind.Edge: Clapping(); break;
            case BossTechniqueKind.Area:
                if (Variant == 0) ScreenLight();
                else if (Variant == 1) ObservingEyes(delta);
                else ReadingHead();
                break;
            case BossTechniqueKind.Signature: Antlers(); break;
            case BossTechniqueKind.Finale: ComingTo(); break;
            case BossTechniqueKind.Crossfire:
                if (Variant == 0) Pursuit(delta);
                else if (Variant == 1) UnbalancedSupport(delta);
                else if (Variant == 2) PassingPrayers();
                else if (Variant == 3) OverlappingGazes(delta);
                else UnpairedLight();
                break;
            case BossTechniqueKind.Dodge: InwardCoil(); break;
            case BossTechniqueKind.Extra:
                if (!Evolved && Variant == 0) RestBeat();
                else if (!Evolved) Mirror();
                else if (Variant == 0) DeerLeap();
                else Attention(delta);
                break;
            case BossTechniqueKind.Cameo:
                if (Variant == 0) Domino();
                else Searching();
                break;
        }
        _lastPlayer = Player.GlobalPosition;
    }

    private Light Place(Vector2 at, int index, float angle = 0, int pattern = 0)
    {
        var bullet = Shot(at, Vector2.Down, pattern);
        bullet.Homing = false;
        bullet.Velocity = Vector2.Zero;
        bullet.CancelLeadIn();
        var light = new Light { Bullet = bullet, Activation = bullet.ActivationId, Origin = at, Born = Time, Index = index, Angle = angle };
        _lights.Add(light);
        return light;
    }

    private static bool Alive(Light light) => light.Bullet.Active && light.Bullet.ActivationId == light.Activation;
    private void Move(Light light, Vector2 position)
    {
        if (!Alive(light)) return;
        light.Bullet.Rotation = (position - light.Bullet.GlobalPosition).Angle();
        light.Bullet.GlobalPosition = position;
        if (!Field.Rect.Grow(14).HasPoint(position)) Pool.Despawn(light.Bullet);
    }

    private static Vector2 Clamp(Vector2 p, float margin = 12) => new(
        Mathf.Clamp(p.X, Field.Left + margin, Field.Right - margin),
        Mathf.Clamp(p.Y, Field.Top + margin, Field.Bottom - margin));

    private void BraidedLights()
    {
        if (Time < _warning)
        {
            for (int strand = -1; strand <= 1; strand += 2)
            for (int k = 0; k < 12; k++)
            {
                float y = k * 18;
                Vector2 a = new(_point.X + strand * 42 * Mathf.Sin(y / 51), y);
                Vector2 b = new(_point.X + strand * 42 * Mathf.Sin((y + 18) / 51), y + 18);
                Line(a, b, 3, false, (float)(Time / _warning));
            }
        }
        int count = 10 + Density * 4 + (Evolved ? 4 : 0);
        for (int i = 0; i < count; i++)
        {
            double birth = _warning + i * 0.22;
            if (Time < birth && Time >= birth - 0.65)
                for (int strand = -1; strand <= 1; strand += 2)
                    Disc(new Vector2(_point.X + strand * 42 * Mathf.Sin(2 / 51f + i * 0.29f), 2), 6, false,
                        (float)((Time - birth + 0.65) / 0.65));
            if (!At(birth)) continue;
            Place(new Vector2(_point.X - 42 * Mathf.Sin(2 / 51f + i * 0.29f), 2), -1, i * 0.29f);
            Place(new Vector2(_point.X + 42 * Mathf.Sin(2 / 51f + i * 0.29f), 2), 1, i * 0.29f);
        }
        foreach (var light in _lights)
        {
            float age = (float)(Time - light.Born), y = 2 + age * 48 * Speed;
            Move(light, new Vector2(_point.X + light.Index * 42 * Mathf.Sin(y / 51 + light.Angle), y));
        }
        if (Time > _warning + count * 0.22 + 4.8 / Speed) Finish();
    }

    private void TurningPrayer()
    {
        Vector2 away = _anchor.DirectionTo(_origin);
        if (Time < _warning)
            for (int i = 0; i < 3 + Density * 2; i++)
            {
                Vector2 direction = away.Rotated((i - (1 + Density)) * 0.12f);
                Line(Clamp(_origin + direction * 24), Clamp(_origin + direction * 46), 5, false, (float)(Time / _warning));
            }
        if (At(_warning))
        {
            int count = 3 + Density * 2;
            for (int i = 0; i < count; i++)
            {
                float angle = away.Angle() + (i - (count - 1) * 0.5f) * 0.12f;
                var light = Place(Clamp(_origin + Vector2.FromAngle(angle) * 24), i, angle);
                light.Origin = light.Bullet.GlobalPosition;
            }
        }
        double turn = _warning + 1.2;
        if (At(turn)) _point = Player.GlobalPosition;
        foreach (var light in _lights)
        {
            if (!Alive(light)) continue;
            if (Time < turn)
            {
                Vector2 pos = Clamp(light.Origin + Vector2.FromAngle(light.Angle) * (float)(Time - _warning) * 18);
                Move(light, pos);
                Line(pos, Player.GlobalPosition, 2, false, (float)((Time - _warning) / 1.2));
            }
            else if (Time < turn + 0.85)
            {
                Line(light.Bullet.GlobalPosition, _point, 3, false, (float)((Time - turn) / 0.85));
                light.Origin = light.Bullet.GlobalPosition;
            }
            else
                Move(light, light.Origin + light.Origin.DirectionTo(_point).Rotated((light.Index - (2 + Density)) * 0.025f)
                    * (float)(Time - turn - 0.85) * 64 * Speed);
        }
        if (Time > turn + 6) Finish();
    }

    private void FlowerCrown()
    {
        float angle = (float)Math.Max(0, Time - _warning) * 0.25f * Speed;
        if (Time < _warning)
        {
            for (int petal = 0; petal < 4; petal++)
            for (int seed = 0; seed < 2 + Density; seed++)
            {
                float a = petal * Mathf.Pi / 2 + seed * 0.06f;
                float bloom = 1 + 0.10f * Mathf.Sin(petal);
                Disc(Center + new Vector2(Mathf.Cos(a) * 82, Mathf.Sin(a) * 58) * bloom,
                    6, false, (float)(Time / _warning));
            }
        }
        if (At(_warning))
            for (int petal = 0; petal < 4; petal++)
            for (int seed = 0; seed < 2 + Density; seed++)
            {
                float a = petal * Mathf.Pi / 2 + seed * 0.06f;
                float bloom = 1 + 0.10f * Mathf.Sin(petal);
                Place(Center + new Vector2(Mathf.Cos(a) * 82, Mathf.Sin(a) * 58) * bloom, petal, a, 2);
            }
        foreach (var light in _lights)
        {
            float a = angle + light.Angle;
            float bloom = 1 + 0.10f * Mathf.Sin((float)(Time - _warning) * 1.3f + light.Index);
            Move(light, Center + new Vector2(Mathf.Cos(a) * 82, Mathf.Sin(a) * 58) * bloom);
        }
        if (Time > _warning + 6.2) Finish();
    }

    private void Clapping()
    {
        const float travel = 0.9f;
        double notice = WarnFor((1 + Density) * 5 + 10, 0.9);
        for (int beat = 0; beat < 3 + Density; beat++)
        {
            double start = _warning + beat * 1.35;
            float y = 28 + (beat * 43) % 160;
            if (Time >= start - notice && Time < start)
            {
                for (int i = 0; i < 2 + Density; i++)
                {
                    float row = y + (i - (1 + Density) * 0.5f) * 10;
                    Line(new Vector2(Field.Left, row), new Vector2(_point.X, row), 4, false, (float)((Time - start + notice) / notice));
                    Line(new Vector2(Field.Right, row), new Vector2(_point.X, row), 4, false, (float)((Time - start + notice) / notice));
                }
            }
            if (At(start))
                for (int i = 0; i < 2 + Density; i++)
                {
                    var left = Place(new Vector2(Field.Left + 2, y + (i - (1 + Density) * 0.5f) * 10), -1, _point.X);
                    var right = Place(new Vector2(Field.Right - 2, y + (i - (1 + Density) * 0.5f) * 10), 1, _point.X);
                    left.Partner = right;
                    right.Partner = left;
                }
        }
        foreach (var light in _lights)
        {
            if (!Alive(light)) continue;
            float t = (float)(Time - light.Born) / travel;
            if (t >= 1) { Pool.Despawn(light.Bullet); continue; }
            Move(light, light.Origin.Lerp(new Vector2(light.Angle, light.Origin.Y), t));
        }
        foreach (var light in _lights)
            if (Alive(light) && light.Index < 0 && light.Partner is { } partner && Alive(partner)
                && light.Bullet.GlobalPosition.DistanceTo(partner.Bullet.GlobalPosition)
                    <= light.Bullet.CollisionRadius + partner.Bullet.CollisionRadius)
            {
                Pool.Despawn(light.Bullet);
                Pool.Despawn(partner.Bullet);
            }
        if (Time > _warning + (3 + Density) * 1.35) Finish();
    }

    private void ScreenLight()
    {
        double age = Time - _warning;
        float open = age < 0 ? 0.34f : 0.34f + 0.18f * Mathf.Sin((float)age * 0.8f);
        bool lit = age >= 0 && age < 4.5;
        for (int side = -1; side <= 1; side += 2)
        {
            var direction = Vector2.FromAngle(_angle + side * 0.52f);
            Line(_origin + direction * _radius, _origin + direction * 390, 1, false, 0.3f);
        }
        int rays = 10 + Density * 2;
        for (int i = 0; i <= rays; i++)
        {
            float a = _angle - open + 2 * open * i / rays;
            Line(_origin + Vector2.FromAngle(a) * _radius, _origin + Vector2.FromAngle(a) * 390,
                4.2f, lit, (float)Math.Min(1, Time / _warning));
        }
        if (Time > _warning + 5.1) Finish();
    }

    private void ObservingEyes(double delta)
    {
        bool moving = delta > 0 && Player.GlobalPosition.DistanceTo(_lastPlayer) / delta > 5;
        if (Time < _warning) Hint(_anchor, "止まって、ひと息");
        if (Time >= _warning && _firedAt < 0)
        {
            double gain = Tier switch { 2 => 1.0, 3 => 1.2, _ => 0.8 };
            _charge = Math.Clamp(_charge + delta * (moving ? gain : -1.5), 0, 1);
            _point = Player.GlobalPosition;
            if (_charge >= 1) _firedAt = Time;
        }
        for (int eye = 0; eye < 4; eye++)
        {
            Vector2 origin = Center + new Vector2(Mathf.Cos(eye * Mathf.Pi / 2) * 130, Mathf.Sin(eye * Mathf.Pi / 2) * 82);
            Disc(origin, 6, false, (float)_charge);
            Vector2 target = _firedAt < 0 ? Player.GlobalPosition : _point;
            Line(origin, target, 3 + Tier * 0.3f, _firedAt >= 0 && Time >= _firedAt + 1.0 && Time < _firedAt + 1.3,
                _firedAt < 0 ? (float)_charge * 0.6f : (float)Math.Min(1, (Time - _firedAt) / 1.0));
        }
        if (_firedAt >= 0 && Time > _firedAt + 1.5 || Time > _warning + 6) Finish();
    }

    private void ReadingHead()
    {
        double age = Time - _warning;
        double read = ReadingSeconds;
        int row = Math.Max(0, (int)(age / (read + ReadingTurn)));
        if (row >= 4) { Finish(); return; }
        double local = age - row * (read + ReadingTurn);
        float y = 38 + row * 44;
        bool reverse = row % 2 == 1;
        Vector2 from = new(reverse ? Field.Right - 8 : Field.Left + 8, y);
        Vector2 to = new(reverse ? Field.Left + 8 : Field.Right - 8, y);
        Vector2 direction = from.DirectionTo(to);
        Line(from, to, 1, false, age < 0 ? (float)(Time / _warning) : 0.4f);
        if (age < 0)
            Line(from - direction * 13, from + direction * 8, 5 + Density, false, (float)(Time / _warning));
        if (row < 3 && local >= read + ReadingTurn - .75)
        {
            Vector2 next = to + Vector2.Down * 44;
            float progress = (float)((local - read - ReadingTurn + .75) / .75);
            Line(next, from + Vector2.Down * 44, 1, false, progress);
            Line(next + direction * 13, next - direction * 8, 5 + Density, false, progress);
        }
        if (age >= 0 && local < read)
        {
            Vector2 head = from.Lerp(to, (float)(local / read));
            Line(head - direction * 13, head + direction * 8, 5 + Density, true);
            Disc(head, 5, true);
        }
        else if (age >= 0) Hint(to, "改行");
    }

    private void Antlers()
    {
        double age = Time - _warning;
        bool live = age >= 0 && age < 4;
        float closing = age < 0 ? 0 : Mathf.Clamp((float)age / (2.8f - Tier * 0.18f), 0, 1);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 hinge = _point + new Vector2(side * 64, -48);
            float angle = Mathf.Pi / 2 + side * Mathf.Lerp(-0.42f, 0.72f, closing);
            Vector2 direction = Vector2.FromAngle(angle);
            Line(hinge, hinge + direction * 100, 3.2f + Tier * 0.4f, live, (float)Math.Min(1, Time / _warning));
            int branches = 2 + Tier;
            for (int branch = 1; branch <= branches; branch++)
            {
                Vector2 fork = hinge + direction * (branch * 86f / (branches + 1));
                Vector2 tip = fork + direction.Rotated(-side * 0.75f) * 30;
                Line(fork, tip, 2.4f + Tier * 0.3f, live, (float)Math.Min(1, Time / _warning));
            }
        }
        if (Time > _warning + 4.6) Finish();
    }

    private void ComingTo()
    {
        const double on = 0.45, rest = 1.3;
        double age = Time - _warning;
        int beat = Math.Max(0, (int)(age / (on + rest)));
        int beats = Tier == 0 ? 2 : 4;
        if (beat >= beats) { Finish(); return; }
        double local = age - beat * (on + rest);
        bool live = age >= 0 && local < on;
        if (age >= 0 && !live)
        {
            if (beat + 1 == beats) { Finish(); return; }
            beat++;
        }
        double start = _warning + beat * (on + rest);
        float angle = Tier switch
        {
            2 => beat * Mathf.Pi / 6,
            3 => beat * Mathf.Pi / 8,
            _ => beat % 2 == 0 ? 0 : Mathf.Pi / 4,
        };
        for (int axis = 0; axis < 2; axis++)
        {
            Vector2 d = Vector2.FromAngle(angle + axis * Mathf.Pi / 2);
            Line(_anchor - d * 400, _anchor + d * 400, 5 + Tier * 0.6f,
                live, (float)Math.Clamp((Time - start + rest) / rest, 0, 1));
        }
    }

    private void Pursuit(double delta)
    {
        if (Time < _warning) Line(_origin, _anchor, 3, false, (float)(Time / _warning));
        if (At(_warning))
        {
            _point = _origin;
            _angle = _origin.DirectionTo(_anchor).Angle();
            int count = 8 + Density * 3;
            for (int i = 0; i < count; i++) Place(_origin, i);
            _tail.Add((_origin, Time - 4));
        }
        if (Time >= _warning)
        {
            float desired = _point.DirectionTo(Player.GlobalPosition).Angle();
            _angle = Mathf.RotateToward(_angle, desired, (float)delta * 0.65f);
            _point += Vector2.FromAngle(_angle) * (float)delta * 55 * Speed;
            _tail.Insert(0, (_point, Time));
            while (_tail.Count > 2 && _tail[^2].Time < Time - 4) _tail.RemoveAt(_tail.Count - 1);
            foreach (var light in _lights)
            {
                double sampleTime = Time - light.Index * 0.13;
                int sample = 0;
                while (sample < _tail.Count - 1 && _tail[sample + 1].Time > sampleTime) sample++;
                Vector2 position = _tail[sample].Position;
                if (sample + 1 < _tail.Count)
                {
                    var older = _tail[sample + 1];
                    float weight = (float)Math.Clamp((sampleTime - older.Time) / (_tail[sample].Time - older.Time), 0, 1);
                    position = older.Position.Lerp(position, weight);
                }
                Move(light, position);
            }
        }
        if (Time > _warning + 6.2) Finish();
    }

    private void UnbalancedSupport(double delta)
    {
        float bias = Mathf.Clamp((Player.GlobalPosition.X - Center.X) / (Field.Width * .5f), -1, 1);
        _supportTilt = Mathf.MoveToward(_supportTilt, bias * .36f, (float)delta * (.12f + Tier * .02f));
        int pairs = 2 + Tier;
        for (int pair = 0; pair < pairs; pair++)
        {
            double release = _warning + pair * SupportInterval;
            double lead = pair == 0 ? _warning : 1.1;
            for (int side = -1; side <= 1; side += 2)
            {
                float along = side * 56;
                if (Time >= release - lead && Time < release)
                {
                    float progress = (float)Math.Clamp((Time - release + lead) / lead, 0, 1);
                    Disc(SupportPoint(along), 9, false, progress);
                    Line(SupportPoint(along), SupportPoint(along - side * 28), 6, false, progress);
                }
                if (At(release))
                    _supportLights.Add(new SupportLight
                    {
                        Light = Place(SupportPoint(along), pair * 2 + (side > 0 ? 1 : 0), pattern: 2),
                        Along = along,
                        Velocity = -side * (26 + Tier * 4),
                    });
            }
        }
        foreach (var weight in _supportLights)
        {
            if (!Alive(weight.Light)) continue;
            if (weight.DropAt < 0)
            {
                float dt = weight.Light.Born == Time ? 0 : (float)delta;
                float acceleration = Mathf.Sin(_supportTilt) * (45 + Tier * 17) - weight.Velocity * .08f;
                weight.Velocity = Mathf.Clamp(weight.Velocity + acceleration * dt, -34 - Tier * 4, 34 + Tier * 4);
                weight.Along += weight.Velocity * dt;
                if (Math.Abs(weight.Along) >= SupportHalf)
                {
                    weight.Along = Mathf.Sign(weight.Along) * SupportHalf;
                    weight.DropAt = Time + SupportDropWarning;
                    weight.DropOrigin = SupportPoint(weight.Along);
                    weight.DropVelocity = Vector2.FromAngle(_supportTilt) * weight.Velocity * .5f + Vector2.Down * 22;
                }
                Move(weight.Light, SupportPoint(weight.Along));
                continue;
            }
            if (Time < weight.DropAt)
            {
                float progress = (float)(1 - (weight.DropAt - Time) / SupportDropWarning);
                for (int part = 0; part < 16; part++)
                {
                    Vector2 from = SupportFallPoint(weight, part * .2f);
                    if (!Field.Rect.Grow(8).HasPoint(from)) break;
                    Line(from, SupportFallPoint(weight, (part + 1) * .2f), 6, false, progress);
                }
                Move(weight.Light, weight.DropOrigin);
            }
            else Move(weight.Light, SupportFallPoint(weight, (float)(Time - weight.DropAt)));
        }
        if (Time < _warning) Hint(Center + Vector2.Down * 54, "寄る側が沈む。光の勢いを見る");
        if (Time > _warning + (pairs - 1) * SupportInterval + 12) Finish();
    }

    private static Vector2 SupportFallPoint(SupportLight weight, float time)
        => weight.DropOrigin + weight.DropVelocity * time + Vector2.Down * (18 * time * time);

    private void PassingPrayers()
    {
        const float duration = 4.2f;
        if (Time < _warning)
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 16; i++)
            {
                float t = i / 16f;
                Vector2 a = PassingPoint(side, t), b = PassingPoint(side, t + 1 / 16f);
                Line(a, b, 3, false, (float)(Time / _warning));
            }
        for (int i = 0; i < 4 + Density * 2; i++)
            if (At(_warning + i * 0.2))
                for (int side = -1; side <= 1; side += 2) Place(PassingPoint(side, 0), side);
        foreach (var light in _lights)
        {
            if (!Alive(light)) continue;
            float t = (float)(Time - light.Born) / duration;
            if (t > 1) { Pool.Despawn(light.Bullet); continue; }
            Move(light, PassingPoint(light.Index, t));
        }
        if (Time > _warning + (4 + Density * 2) * 0.2 + duration) Finish();
    }

    private static Vector2 PassingPoint(int side, float t)
    {
        float x = side < 0 ? Mathf.Lerp(Field.Left + 8, Field.Right - 8, t) : Mathf.Lerp(Field.Right - 8, Field.Left + 8, t);
        float y = Field.CenterY + side * (70 - 140 * t + 42 * Mathf.Sin(Mathf.Pi * t));
        return new Vector2(x, y);
    }

    private void OverlappingGazes(double delta)
    {
        Vector2 player = Player.GlobalPosition;
        _point.Y = Mathf.MoveToward(_point.Y, player.Y, (float)delta * (16 + Tier * 2));
        _destination.X = Mathf.MoveToward(_destination.X, player.X, (float)delta * (19 + Tier * 2));
        float half = 13 + Tier * 2;
        Line(new Vector2(Field.Left, _point.Y), new Vector2(Field.Right, _point.Y), half, false, 0.3f);
        Line(new Vector2(_destination.X, Field.Top), new Vector2(_destination.X, Field.Bottom), half, false, 0.3f);
        Box(new Rect2(new Vector2(_destination.X - half, _point.Y - half), Vector2.One * half * 2),
            Time >= _warning, (float)Math.Min(1, Time / _warning));
        if (Time > _warning + 6.5) Finish();
    }

    private Vector2 Supporter(int index)
    {
        float a = index * Mathf.Tau / 7 - Mathf.Pi / 2;
        return Center + new Vector2(Mathf.Cos(a) * 126, Mathf.Sin(a) * 80);
    }

    private void UnpairedLight()
    {
        const double cycleTime = 2.6;
        int cycle = Math.Max(0, (int)((Time - _warning) / cycleTime));
        if (cycle >= 3) { Finish(); return; }
        double start = _warning + cycle * cycleTime;
        int single = (cycle * 2 + 1) % 7;
        int[] others = new int[6];
        for (int i = 0, k = 0; i < 7; i++) if (i != single) others[k++] = i;
        for (int i = 0; i < 7; i++) Disc(Supporter(i), 5, false, 0.7f);
        for (int pair = 0; pair < 3; pair++)
        {
            int first = (pair + cycle) % 3;
            Line(Supporter(others[first]), Supporter(others[5 - first]), 1, false, 0.25f);
        }
        if (At(start)) _point = Player.GlobalPosition;
        Vector2 from = Supporter(single);
        if (Time >= start + 0.45)
            Line(from - from.DirectionTo(_point) * (1 + Density) * 10, _point, 4, false,
                (float)Math.Clamp((Time - start - 0.45) / 0.85, 0, 1));
        if (At(start + 1.3))
        {
            Vector2 direction = from.DirectionTo(_point);
            for (int i = 0; i < 2 + Density; i++)
            {
                var shot = Shot(from - direction * i * 10, direction, 1);
                shot.BossAimTarget = _point;
                shot.FormationHomingOffset = -direction * i * 10;
            }
        }
    }

    private void InwardCoil()
    {
        float age = (float)(Time - _warning);
        float radius = age < 0 ? 90 : 90 - age * CoilContraction;
        bool live = age >= 0 && age < 5.5;
        for (int i = 0; i < 48; i++)
        {
            float a = i * Mathf.Tau / 48 + age * 0.32f;
            float b = (i + 1) * Mathf.Tau / 48 + age * 0.32f;
            float r1 = radius + i * 0.32f, r2 = radius + (i + 1) * 0.32f;
            Line(_point + Vector2.FromAngle(a) * r1, _point + Vector2.FromAngle(b) * r2,
                3 + Tier * 0.5f, live, (float)Math.Min(1, Time / _warning));
        }
        if (!GameManager.Instance!.HasDodge)
            Hint(new Vector2(Field.CenterX, 202), age < 0 ? "外へ出るなら、輪が光る前に" : "中心側か、輪の外で待つ");
        if (age > 5.8) Finish();
    }

    private void RestBeat()
    {
        const double beat = 1.25;
        double age = Time - _warning;
        int pulse = Math.Max(0, (int)(age / beat));
        if (pulse >= 6) { Finish(); return; }
        bool rest = pulse % 3 == 2;
        double local = age - pulse * beat;
        bool live = age >= 0 && !rest && local >= 0.35;
        int columns = Tier switch { 2 => 5, 3 => 6, _ => 4 };
        float pitch = 280f / columns;
        for (int i = 0; i < columns; i++)
        {
            float offset = (rest ? (pulse / 3 + 1) % 2 : pulse / 3 % 2) * pitch * .5f;
            float x = Field.Left + 30 + i * pitch + offset;
            Line(new Vector2(x, 12), new Vector2(x, Field.Bottom - 12), 5 + Tier * 0.5f,
                live, age < 0 ? (float)(Time / _warning) : rest ? 0.05f : (float)Math.Min(1, local / 0.35));
        }
        Hint(new Vector2(Field.CenterX, 28), rest ? "休符" : pulse % 3 == 0 ? "一拍" : "二拍");
    }

    private void Mirror()
    {
        const double beat = 2.4;
        int cycle = Math.Max(0, (int)((Time - _warning) / beat));
        if (cycle >= 3) { Finish(); return; }
        double start = _warning + cycle * beat;
        Vector2 mirror = new(Field.Left + Field.Right - Player.GlobalPosition.X, Player.GlobalPosition.Y);
        if (Time < start + 0.6)
        {
            _point = mirror;
            _destination = Player.GlobalPosition;
        }
        Disc(_point, 8, false, 0.5f);
        Vector2 direction = _point.DirectionTo(_destination);
        if (direction.LengthSquared() < 0.1f) direction = Vector2.Right;
        Line(_point - direction * (1 + Density) * 11, _destination, 4, false,
            (float)Math.Clamp((Time - start - 0.6) / 1.0, 0, 1));
        if (At(start + 1.6))
        {
            for (int i = 0; i < 2 + Density; i++)
            {
                Vector2 at = _point + direction * (-i * 11);
                var bullet = Shot(at, direction, 1);
                bullet.BossAimTarget = _destination;
                bullet.Velocity = direction * 54 * Speed;
            }
        }
    }

    private void DeerLeap()
    {
        const float flight = 1.6f;
        float body = ((BossKoharu)Boss).BodyExtent;
        bool warning = Time < _warning;
        if (Time < _warning + flight + 0.35)
            Disc(_destination, body + 22, false, (float)Math.Min(1, Time / _warning));
        Vector2 midpoint = (_origin + _destination) * 0.5f + new Vector2(0, -36);
        midpoint.Y = Math.Max(body + 5, midpoint.Y);
        for (int i = 0; i < 12 && Time < _warning + flight; i++)
        {
            float t1 = i / 12f, t2 = (i + 1) / 12f;
            Vector2 p = _origin.Lerp(midpoint, t1).Lerp(midpoint.Lerp(_destination, t1), t1);
            Vector2 q = _origin.Lerp(midpoint, t2).Lerp(midpoint.Lerp(_destination, t2), t2);
            Line(p, q, 0.6f, false, (float)Math.Min(1, Time / _warning));
        }
        if (!warning && Time < _warning + flight)
        {
            float t = (float)(Time - _warning) / flight;
            Boss.GlobalPosition = _origin.Lerp(midpoint, t).Lerp(midpoint.Lerp(_destination, t), t);
        }
        else if (Time >= _warning + flight) Boss.GlobalPosition = _destination;
        if (Time < _warning + flight + 0.35)
            for (int i = 0; i < 8 + Density * 2; i++)
            {
                Vector2 direction = Vector2.FromAngle(i * Mathf.Tau / (8 + Density * 2));
                Line(_destination + direction * (body + 14), _destination + direction * (body + 80), 3, false,
                    (float)Math.Clamp((Time - _warning) / (flight + 0.35), 0, 1));
            }
        if (At(_warning + flight + 0.35))
        {
            int count = 8 + Density * 2;
            for (int i = 0; i < count; i++)
            {
                Vector2 direction = Vector2.FromAngle(i * Mathf.Tau / count);
                var bullet = Shot(_destination + direction * (body + 14), direction, 2);
                bullet.Homing = false;
                bullet.Velocity = direction * 42 * Speed;
            }
        }
        if (Time > _warning + flight + 3.2) Finish();
    }

    private void Attention(double delta)
    {
        float watchDistance = 90 + Tier * 5;
        float distance = Player.GlobalPosition.DistanceTo(_origin);
        if (Time >= _warning && _firedAt < 0)
        {
            _charge = Math.Clamp(_charge + delta * (distance < watchDistance ? 0.32 + Tier * 0.06 : -0.8), 0, 1);
            if (_charge >= 1)
            {
                _firedAt = Time;
                _point = Player.GlobalPosition;
                _radius = 48 + Tier * 4;
            }
        }
        Disc(_origin, watchDistance, false, (float)_charge * 0.8f);
        if (_firedAt < 0)
        {
            Disc(_origin, 16 + (float)_charge * 25, false, (float)_charge);
            Hint(_origin + Vector2.Down * 45, _charge > 0.55 ? "少し、離れて" : "距離で光が変わる");
        }
        else
        {
            double strike = _firedAt + WarnFor(_radius + 8, 1.4);
            Disc(_point, _radius, Time >= strike && Time < strike + 0.6,
                (float)Math.Clamp((Time - _firedAt) / (strike - _firedAt), 0, 1));
            if (Time > strike + 0.8) Finish();
        }
        if (Time > _warning + 8) Finish();
    }

    private void Domino()
    {
        const double interval = 0.48;
        int columns = 6 + Density;
        float pitch = Field.Width / columns;
        for (int i = 0; i < columns; i++)
        {
            double start = _warning + i * interval;
            Vector2 hinge = new(Field.Left + (i + 0.5f) * pitch, 32);
            if (Time < start && Time >= start - 1.3)
            {
                Line(hinge, hinge + Vector2.Down * 145, 3, false, (float)Math.Clamp((Time - start + 1.3) / 1.3, 0, 1));
                float length = 12 + (2 + Density) * 9;
                Line(hinge + Vector2.Right * 12, hinge + Vector2.Right * length, 5, false);
                for (int arc = 0; arc < 8; arc++)
                    Line(hinge + Vector2.FromAngle(arc * Mathf.Pi / 16) * length,
                        hinge + Vector2.FromAngle((arc + 1) * Mathf.Pi / 16) * length, 3, false);
            }
            if (At(start))
                for (int seed = 0; seed < 3 + Density; seed++)
                    Place(hinge + Vector2.Right * (12 + seed * 9), i, seed);
        }
        foreach (var light in _lights)
        {
            if (!Alive(light)) continue;
            float age = (float)(Time - light.Born);
            float angle = Mathf.Min(Mathf.Pi / 2, age * 2.2f);
            float length = 12 + light.Angle * 9;
            Vector2 hinge = new(Field.Left + (light.Index + 0.5f) * pitch, 32);
            Move(light, hinge + Vector2.FromAngle(angle) * length + Vector2.Down * Math.Max(0, age - 0.72f) * 80 * Speed);
        }
        if (Time > _warning + columns * interval + 3) Finish();
    }

    private void Searching()
    {
        double age = Time - _warning;
        float baseAngle = _origin.DirectionTo(_anchor).Angle();
        if (age < 1.8)
        {
            int glance = Math.Clamp((int)Math.Max(0, age / 0.6), 0, 2);
            _angle = baseAngle + (glance - 1) * 0.62f;
            Line(_origin, _origin + Vector2.FromAngle(_angle) * 300, 3, false, 0.3f);
        }
        else
        {
            if (_phase == 0) { _point = Player.GlobalPosition; _phase = 1; }
            Line(_origin, _point, 4, false, (float)Math.Clamp((age - 1.8) / 1.1, 0, 1));
        }
        if (At(_warning + 2.9))
        {
            Vector2 aim = _origin.DirectionTo(_point);
            for (int i = -Density; i <= Density; i++)
            {
                var bullet = Shot(_origin, aim.Rotated(i * 0.075f), 1);
                bullet.BossAimTarget = _point;
            }
        }
        if (age > 6.6) Finish();
    }

    protected override void DrawTechnique()
    {
        var light = new Color("d4f4df", .7f);
        var reflection = new Color("b8ccec", .6f);
        double age = Time - _warning;
        switch (Kind)
        {
            case BossTechniqueKind.Normal when Variant == 0:
                for (int strand = -1; strand <= 1; strand += 2)
                    Penlight(new Vector2(_point.X + strand * 12, 12), strand * .3f, Time >= _warning, light);
                for (int i = Math.Max(0, _lights.Count - 2); i < _lights.Count; i++)
                    if (Alive(_lights[i]))
                    {
                        Vector2 at = _lights[i].Bullet.GlobalPosition;
                        DrawPolyline(new[] { at + new Vector2(-5, -8), at + new Vector2(0, -5), at + new Vector2(5, -8) }, reflection, .8f, true);
                    }
                break;
            case BossTechniqueKind.Normal when Variant == 1:
                if (_lights.Count > 0 && Alive(_lights[_lights.Count / 2]))
                {
                    Vector2 at = _lights[_lights.Count / 2].Bullet.GlobalPosition + new Vector2(0, -10);
                    Eye(at, Time < _warning + 1.2 ? Player.GlobalPosition : _point, Time < _warning + 1.2 ? .45f : 1, light);
                }
                break;
            case BossTechniqueKind.Normal:
                for (int petal = 0; petal < 4; petal++)
                {
                    float a = (float)Math.Max(0, age) * .25f * Speed + petal * Mathf.Pi / 2;
                    Vector2 at = Center + new Vector2(Mathf.Cos(a) * 82, Mathf.Sin(a) * 58)
                        * (1 + .10f * Mathf.Sin((float)age * 1.3f + petal));
                    Petal(at, a + Mathf.Pi / 2, reflection);
                }
                break;
            case BossTechniqueKind.Edge:
                for (int beat = 0; beat < 3 + Density; beat++)
                {
                    double elapsed = age - beat * 1.35;
                    float y = 28 + beat * 43 % 160;
                    if (elapsed >= -1 && elapsed < .9)
                    {
                        float phase = Mathf.Clamp((float)elapsed / .9f, 0, 1);
                        ClapHand(new Vector2(Mathf.Lerp(Field.Left + 6, _point.X - 6, phase), y), 1, light);
                        ClapHand(new Vector2(Mathf.Lerp(Field.Right - 6, _point.X + 6, phase), y), -1, light);
                    }
                    if (elapsed >= .9 && elapsed < 1.2)
                    {
                        float fade = 1 - (float)(elapsed - .9) / .3f;
                        for (int side = -1; side <= 1; side += 2)
                            DrawArc(new Vector2(_point.X, y), 5 + (1 - fade) * 10,
                                side < 0 ? 2.5f : -.6f, side < 0 ? 3.8f : .6f, 8, new Color(light, fade * .6f), .8f, true);
                    }
                }
                break;
            case BossTechniqueKind.Area when Variant == 0:
                Vector2 facing = Vector2.FromAngle(_angle), sideAxis = facing.Orthogonal();
                Vector2 screen = _origin + facing * Math.Max(8, _radius - 16);
                DrawPolyline(new[] { screen - sideAxis * 11 - facing * 5, screen + sideAxis * 11 - facing * 5,
                    screen + sideAxis * 11 + facing * 5, screen - sideAxis * 11 + facing * 5,
                    screen - sideAxis * 11 - facing * 5 }, reflection, 1, true);
                DrawLine(screen - sideAxis * 6, screen + sideAxis * 6, light, .8f, true);
                break;
            case BossTechniqueKind.Area when Variant == 1:
                for (int i = 0; i < 4; i++)
                {
                    Vector2 at = Center + new Vector2(Mathf.Cos(i * Mathf.Pi / 2) * 130, Mathf.Sin(i * Mathf.Pi / 2) * 82);
                    Eye(at, _firedAt < 0 ? Player.GlobalPosition : _point, .15f + .85f * (float)_charge, light);
                }
                break;
            case BossTechniqueKind.Area:
                int row = Math.Clamp((int)(Math.Max(0, age) / (ReadingSeconds + ReadingTurn)), 0, 3);
                double local = age - row * (ReadingSeconds + ReadingTurn);
                bool reverse = row % 2 == 1;
                Vector2 from = new(reverse ? Field.Right - 8 : Field.Left + 8, 38 + row * 44);
                Vector2 to = new(reverse ? Field.Left + 8 : Field.Right - 8, from.Y);
                if (age >= 0 && local < ReadingSeconds)
                {
                    Vector2 head = from.Lerp(to, (float)(local / ReadingSeconds));
                    Vector2 dir = from.DirectionTo(to);
                    for (int side = -1; side <= 1; side += 2)
                        DrawLine(head + new Vector2(-7, side * (7 + Density)), head + new Vector2(7, side * (7 + Density)), light, .8f, true);
                    DrawLine(head - dir * 7, head + dir * 5, reflection, .6f, true);
                }
                DrawPolyline(new[] { from + new Vector2(0, -8), from + new Vector2(0, 8), from + new Vector2(reverse ? -5 : 5, 8) }, reflection, .8f, true);
                break;
            case BossTechniqueKind.Signature:
                float closing = age < 0 ? 0 : Mathf.Clamp((float)age / (2.8f - Tier * .18f), 0, 1);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 hinge = _point + new Vector2(side * 64, -48);
                    float angle = Mathf.Pi / 2 + side * Mathf.Lerp(-.42f, .72f, closing);
                    DrawArc(hinge, 6, angle - .9f, angle + .9f, 10, light, 1, true);
                    Vector2 dir = Vector2.FromAngle(angle);
                    for (int fork = 1; fork <= 2 + Tier; fork++)
                    {
                        Vector2 joint = hinge + dir * (fork * 86f / (3 + Tier));
                        DrawLine(joint - dir * 3, joint + dir * 3, reflection, .8f, true);
                        DrawLine(joint, joint + dir.Rotated(-side * .75f) * 7, light, .8f, true);
                    }
                }
                break;
            case BossTechniqueKind.Finale:
                int pulse = Math.Max(0, (int)(age / 1.75));
                bool rest = age >= 0 && age - pulse * 1.75 >= .45;
                Vector2 beatAt = new(Field.Left + 18, 21);
                for (int i = 0; i < (Tier == 0 ? 2 : 4); i++)
                    DrawCircle(beatAt + new Vector2(i * 9, 0), i == pulse ? 2.5f : 1.1f, new Color(light, i <= pulse ? .8f : .3f));
                if (rest) RestMark(beatAt + new Vector2(43, -1), light);
                break;
            case BossTechniqueKind.Crossfire when Variant == 0:
                if (Time >= _warning)
                {
                    Eye(_point - Vector2.FromAngle(_angle) * 9, Player.GlobalPosition, 1, light);
                    for (int i = 3; i < _lights.Count; i += 4)
                        if (Alive(_lights[i]))
                            DrawLine(_lights[i].Bullet.GlobalPosition + new Vector2(-2, -7), _lights[i].Bullet.GlobalPosition + new Vector2(2, -7), reflection, .8f, true);
                }
                break;
            case BossTechniqueKind.Crossfire when Variant == 1:
                Vector2 supportAxis = Vector2.FromAngle(_supportTilt);
                for (float along = -SupportHalf; along < SupportHalf; along += 14)
                    DrawLine(Center + supportAxis * along, Center + supportAxis * Math.Min(along + 8, SupportHalf),
                        new Color(reflection, .38f), .7f, true);
                ClapHand(Center + new Vector2(-7, 12), 1, new Color(light, .55f));
                ClapHand(Center + new Vector2(7, 12), -1, new Color(light, .55f));
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 supportEnd = Center + supportAxis * (side * SupportHalf);
                    Penlight(supportEnd + Vector2.Down * 7, _supportTilt, side * _supportTilt > .03f, reflection);
                    if (side * _supportTilt > .03f)
                        DrawPolyline(new[] { supportEnd + new Vector2(-3, 10), supportEnd + new Vector2(0, 14), supportEnd + new Vector2(3, 10) },
                            new Color(light, .65f), .8f, true);
                }
                foreach (var weight in _supportLights)
                {
                    if (!Alive(weight.Light)) continue;
                    Vector2 at = weight.Light.Bullet.GlobalPosition;
                    if (weight.DropAt < 0 && Math.Abs(weight.Velocity) > 2)
                    {
                        Vector2 toward = supportAxis * Mathf.Sign(weight.Velocity);
                        DrawLine(at - toward * 9 + Vector2.Up * 7, at + toward * 3 + Vector2.Up * 7,
                            new Color(light, .7f), .8f, true);
                        DrawPolyline(new[] { at + Vector2.Up * 10, at + toward * 3 + Vector2.Up * 7,
                            at + Vector2.Up * 4 }, new Color(light, .7f), .8f, true);
                    }
                    else if (Time < weight.DropAt)
                    {
                        float progress = Mathf.Clamp((float)(1 - (weight.DropAt - Time) / SupportDropWarning), 0, 1);
                        DrawArc(weight.DropOrigin, 10, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * progress,
                            24, new Color("ffe0a0"), 1, true);
                    }
                    else Petal(at + Vector2.Up * 9, _supportTilt, new Color(light, .55f));
                }
                break;
            case BossTechniqueKind.Crossfire when Variant == 2:
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 start = PassingPoint(side, 0);
                    Penlight(start + new Vector2(side * 2, 0), side * -.65f, Time >= _warning, light);
                    Vector2 destination = PassingPoint(side, 1);
                    DrawPolyline(new[] { destination + new Vector2(-side * 5, -5), destination,
                        destination + new Vector2(-side * 5, 5) }, reflection, .8f, true);
                }
                break;
            case BossTechniqueKind.Crossfire when Variant == 3:
                Eye(new Vector2(Field.Left + 9, _point.Y), new Vector2(Field.Right, _point.Y), 1, reflection);
                Eye(new Vector2(_destination.X, Field.Top + 10), new Vector2(_destination.X, Field.Bottom), 1, reflection);
                float half = 13 + Tier * 2;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    Vector2 corner = new(_destination.X + x * half, _point.Y + y * half);
                    DrawLine(corner, corner - new Vector2(x * 5, 0), light, 1, true);
                    DrawLine(corner, corner - new Vector2(0, y * 5), light, 1, true);
                }
                break;
            case BossTechniqueKind.Crossfire:
                int cycle = Math.Max(0, (int)(age / 2.6));
                int single = (cycle * 2 + 1) % 7;
                var others = new List<int>();
                for (int i = 0; i < 7; i++) if (i != single) others.Add(i);
                for (int pair = 0; pair < 3; pair++)
                {
                    int first = (pair + cycle) % 3;
                    SmallNumber(Supporter(others[first]) + new Vector2(0, 16), pair + 1, reflection);
                    SmallNumber(Supporter(others[5 - first]) + new Vector2(0, 16), pair + 1, reflection);
                }
                Penlight(Supporter(single), age >= cycle * 2.6 ? Supporter(single).DirectionTo(_point).Angle() - Mathf.Pi / 2 : 0, true, light);
                break;
            case BossTechniqueKind.Dodge:
                float radius = age < 0 ? 90 : 90 - (float)age * CoilContraction;
                float endAngle = Mathf.Tau + (float)age * .32f;
                Vector2 end = _point + Vector2.FromAngle(endAngle) * (radius + 48 * .32f);
                DrawPolyline(new[] { end + Vector2.FromAngle(endAngle - .5f) * 6, end,
                    end + Vector2.FromAngle(endAngle + .5f) * 6 }, reflection, 1, true);
                break;
            case BossTechniqueKind.Extra when !Evolved && Variant == 0:
                int rhythmBeat = Math.Max(0, (int)(age / 1.25));
                int active = rhythmBeat % 3;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 at = new(Field.CenterX - 17 + i * 17, 15);
                    Color color = new(light, i == active ? .9f : .3f);
                    if (i == 2) RestMark(at, color);
                    else Penlight(at, 0, i == active && age >= 0, color);
                }
                break;
            case BossTechniqueKind.Extra when !Evolved:
                for (int y = 35; y < Field.Bottom - 20; y += 30)
                    DrawLine(new Vector2(Field.CenterX, y), new Vector2(Field.CenterX, y + 5), new Color(reflection, .3f), .7f, true);
                DrawArc(_point + new Vector2(0, -4), 3, 0, Mathf.Tau, 14, reflection, .8f, true);
                DrawPolyline(new[] { _point + new Vector2(-5, 7), _point + new Vector2(-3, 0), _point + new Vector2(3, 0), _point + new Vector2(5, 7) }, reflection, .8f, true);
                for (int side = -1; side <= 1; side += 2)
                    DrawPolyline(new[] { _point + new Vector2(side * 8, -9), _point + new Vector2(side * 11, -9),
                        _point + new Vector2(side * 11, 10), _point + new Vector2(side * 8, 10) }, light, .8f, true);
                break;
            case BossTechniqueKind.Extra when Variant == 0:
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 hoof = _destination + new Vector2(side * 9, 0);
                    DrawArc(hoof, 5, .1f, Mathf.Pi - .1f, 10, reflection, 1, true);
                    DrawLine(hoof + new Vector2(0, -3), hoof + new Vector2(0, 4), reflection, .8f, true);
                }
                if (Time < _warning + 1.6) DrawLeapFootprint();
                break;
            case BossTechniqueKind.Extra:
                Eye(_origin + new Vector2(0, -20), _firedAt < 0 ? Player.GlobalPosition : _point, .2f + .8f * (float)_charge, light);
                for (int i = 0; i < 5; i++)
                    DrawLine(_origin + new Vector2(-12 + i * 6, 29), _origin + new Vector2(-12 + i * 6, 33),
                        new Color(reflection, _charge >= (i + 1) / 5f ? .8f : .2f), 1.5f, true);
                break;
            case BossTechniqueKind.Cameo when Variant == 0:
                int columns = 6 + Density;
                float pitch = Field.Width / columns;
                for (int i = 0; i < columns; i++)
                {
                    Vector2 hinge = new(Field.Left + (i + .5f) * pitch, 32);
                    double elapsed = age - i * .48;
                    if (elapsed >= -.9 && elapsed < .8)
                    {
                        DrawArc(hinge, 4, 0, Mathf.Pi, 10, reflection, .8f, true);
                        SmallNumber(hinge + new Vector2(0, -10), i + 1, light);
                    }
                }
                break;
            case BossTechniqueKind.Cameo:
                Vector2 look = age < 1.8 ? _origin + Vector2.FromAngle(_angle) * 100 : _point;
                Eye(_origin + new Vector2(0, -14), look, age < 1.8 ? .45f : 1, light);
                if (age >= 1.8 && age < 2.9)
                    DrawArc(_point, 8, -.7f, 3.8f, 20, reflection, .8f, true);
                break;
        }
    }

    private void DrawLeapFootprint()
    {
        float body = ((BossKoharu)Boss).BodyExtent;
        Vector2 midpoint = (_origin + _destination) * 0.5f + new Vector2(0, -36);
        midpoint.Y = Math.Max(body + 5, midpoint.Y);
        var left = new Vector2[13];
        var right = new Vector2[13];
        for (int i = 0; i <= 12; i++)
        {
            float t = i / 12f;
            Vector2 p = _origin.Lerp(midpoint, t).Lerp(midpoint.Lerp(_destination, t), t);
            Vector2 side = ((midpoint - _origin) * (1 - t) + (_destination - midpoint) * t).Normalized().Orthogonal() * (body + 3);
            left[i] = p + side;
            right[i] = p - side;
        }
        var outline = new Color("ffe0a0", 0.75f);
        DrawPolyline(left, outline, 1, true);
        DrawPolyline(right, outline, 1, true);
        DrawLine(left[0], right[0], outline, 1, true);
        DrawLine(left[^1], right[^1], outline, 1, true);
    }

    private void Eye(Vector2 at, Vector2 target, float open, Color color)
    {
        float height = 1 + open * 3;
        DrawPolyline(new[] { at + new Vector2(-7, 0), at + new Vector2(-3, -height), at + new Vector2(3, -height),
            at + new Vector2(7, 0), at + new Vector2(3, height), at + new Vector2(-3, height), at + new Vector2(-7, 0) }, color, .8f, true);
        DrawCircle(at + at.DirectionTo(target) * 2, 1.1f + open * .5f, color);
    }

    private void Penlight(Vector2 at, float angle, bool on, Color color)
    {
        Vector2 along = Vector2.Down.Rotated(angle), across = along.Orthogonal();
        DrawPolyline(new[] { at - along * 5 - across * 2, at + along * 3 - across * 2,
            at + along * 3 + across * 2, at - along * 5 + across * 2, at - along * 5 - across * 2 }, color, .8f, true);
        DrawLine(at + along * 3, at + along * 7, color, 1, true);
        if (on) DrawLine(at - along * 3, at + along, new Color(color, .85f), 1.2f, true);
    }

    private void Petal(Vector2 at, float angle, Color color)
    {
        var direction = Vector2.FromAngle(angle);
        var side = direction.Orthogonal();
        DrawPolyline(new[] { at - direction * 8, at - direction * 2 + side * 4, at + direction * 7,
            at - direction * 2 - side * 4, at - direction * 8 }, color, .8f, true);
    }

    private void ClapHand(Vector2 at, int facing, Color color)
    {
        DrawPolyline(new[] { at + new Vector2(-facing * 6, 6), at + new Vector2(facing * 2, 3), at + new Vector2(facing * 4, -3),
            at + new Vector2(facing * 1, -7), at + new Vector2(-facing * 3, -3), at + new Vector2(-facing * 6, 6) }, color, .8f, true);
        DrawLine(at + new Vector2(-facing * 1, -3), at + new Vector2(facing * 2, 1), color, .6f, true);
    }

    private void RestMark(Vector2 at, Color color)
    {
        DrawPolyline(new[] { at + new Vector2(-2, -5), at + new Vector2(3, -1), at + new Vector2(-1, 2), at + new Vector2(2, 5) }, color, 1, true);
    }

    private void SmallNumber(Vector2 at, int number, Color color)
    {
        string text = number.ToString();
        float width = UiKit.ZenBold.GetStringSize(text, fontSize: 8).X;
        DrawString(UiKit.ZenBold, at - new Vector2(width * .5f, 0), text, fontSize: 8, modulate: color);
    }
}
