using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AkariBossTechnique : BossCharacterTechnique
{
    private sealed class Letter
    {
        public Bullet Shot = null!;
        public ulong Activation;
        public Vector2 Origin, Direction;
        public double Born, Changed;
        public int Group, State;
        public bool Alive => Shot.Active && Shot.ActivationId == Activation;
    }
    private sealed class Stamp
    {
        public Vector2 Position;
        public double At;
    }
    private readonly List<Letter> _letters = new();
    private readonly List<Vector2> _history = new();
    private readonly List<Stamp> _stamps = new();
    private readonly Vector2[] _points = new Vector2[12];
    private readonly int[] _states = new int[12];
    private int _mechanic, _beat;
    private double _warning, _next, _still;
    private Vector2 _origin, _aim, _dwell;
    private float _angle;
    private static readonly Color Paper = new("ffe6a2"), Ink = new("b3e4eb");
    private double ClipCycle => Tier switch { 2 => 2.95, 3 => 2.75, _ => 3.2 };
    private double ClipLead => Tier switch { 2 => 1.35, 3 => 1.25, _ => 1.5 };

    protected override void Start()
    {
        _origin = new Vector2(Field.CenterX, Field.CenterY);
        _aim = Player.GlobalPosition;
        _dwell = _aim;
        _warning = WarnFor(45);
        _next = _warning;
        _mechanic = Kind switch
        {
            BossTechniqueKind.Normal => Variant,
            BossTechniqueKind.Edge => 5,
            BossTechniqueKind.Area => 6 + Variant,
            BossTechniqueKind.Signature => 8,
            BossTechniqueKind.Crossfire => 9 + Variant,
            BossTechniqueKind.Dodge => 14,
            BossTechniqueKind.Extra => 15 + Variant + (Evolved ? 2 : 0),
            BossTechniqueKind.Cameo => 19 + Variant,
            _ => throw new InvalidOperationException("Akari has no independent legacy finale."),
        };
        var identity = _mechanic switch
        {
            0 => ("重なる一斉送信", "akari.delivery-overtake", "遅い便と速い便が重なる時刻を見て、追越し後に横切る"),
            1 => ("ふたつの焦がれる扇火", "akari.alternating-inboxes", "片方の発射が終わった側へ切り返す"),
            2 => ("連なる火花の花冠", "akari.unfastened-clips", "外れたクリップの継ぎ目から、縮まる花冠を抜ける"),
            3 => ("切り返す紅蓮の織目", "akari.unsent-history", "記録された自分の線を戻らず、まだ通っていない所へ移る"),
            4 => ("追いかける返信の針路", "akari.wall-reply", "壁で一度反射する返信の先まで読む"),
            5 => ("降り積もる未送信", "akari.filing-piles", "床の書類が崩れた場所を使い、次の積み上がりを避ける"),
            6 => ("豪雨予報", "akari.rain-splash", "雨柱の着地後、床から横へ飛ぶしぶきも越す"),
            7 => ("沈黙の波紋", "akari.spent-wavefront", "中心から離れ、外へ伝わる波が薄れたら内側へ入り直す"),
            8 => ("狐火・三尾の便り", "akari.braided-delivery", "三つの束の前後を読み、ほどけて飛ぶ束を見送る"),
            9 => ("逃がさない締切", "akari.dwell-timecard", "一か所に留まらず、押印の予告を置いて離れる"),
            10 => ("交差する焦燥", "akari.exchanged-priority", "速い便と遅い便の交換後の到着順で横断する"),
            11 => ("一斉送信", "akari.carried-envelope", "封が開く前に、封筒の背面へ回る"),
            12 => ("駆け抜ける残業", "akari.extending-clock", "時計の針の長さに合わせて、内外の距離を変える"),
            13 => ("ほどけない焦り", "akari.tensioned-clips", "二つのクリップを見て、糸が張る前に広い側へ移る"),
            14 => ("狐火・締切の境界", "akari.coincident-membranes", "予兆中に広い側の外へ出る。回避なら二つが重なる瞬間に越せる"),
            15 => ("三秒の取消", "akari.exact-retraction", "送った線を覚え、三秒後に同じ線を戻る便をかわす"),
            16 => ("鳴り続ける内線", "akari.paused-outbox", "止まった書類の間を使い、番号順の再開に備える"),
            17 => ("狐火・二本の置き傘", "akari.solid-umbrellas", "雨を実際に受け止める傘の下へ入り、傘が傾く前に移る"),
            18 => ("狐火・宛名のない封書", "akari.proximity-addressing", "近づいて宛先を確定させ、予告中に横へ離れる"),
            19 => ("止まらない締切", "akari.inbox-transfer", "受信箱に入った便が、予告された送信箱から出る先を読む"),
            _ => ("溜め込んだため息", "akari.local-breath-vortex", "息の輪へ入った紙だけが曲がる。輪の外の直進と区別する"),
        };
        Identify(identity.Item1, identity.Item2, identity.Item3);
        if (_mechanic == 2) _origin = _aim.Clamp(new Vector2(Field.Left + 64, 64), new Vector2(Field.Right - 64, Field.Bottom - 64));
        if (_mechanic == 3) _history.Add(_aim);
        if (_mechanic == 4)
        {
            _origin = Boss.ShotCenter;
            bool left = _aim.X < Field.CenterX;
            _points[0] = new Vector2(left ? Field.Left + 8 : Field.Right - 8, Mathf.Clamp(_aim.Y - 35, 35, 150));
            _angle = _origin.DirectionTo(_points[0]).Angle();
        }
        if (_mechanic == 7) _origin = _aim.Clamp(new Vector2(Field.Left + 88, 88), new Vector2(Field.Right - 88, Field.Bottom - 88));
        if (_mechanic == 12) _angle = (_aim - _origin).Angle() - Mathf.Tau * (3.75f / 7.5f);
        if (_mechanic == 14)
        {
            _origin.X = Mathf.Clamp(_aim.X, Field.Left + 70, Field.Right - 70);
            if (!GameManager.Instance!.HasDodge)
            {
                float side = _origin.X <= Field.CenterX ? 1 : -1;
                float exitX = _origin.X + side * (58 + 2.5f + Tier * .35f + Player.HitRadius + 3);
                _warning = WarnFor(Math.Abs(exitX - _aim.X) + 32, 1.9);
            }
        }
        if (_mechanic == 17)
        {
            _points[0] = new Vector2(Field.Left + Field.Width * .28f, 104);
            _points[1] = new Vector2(Field.Left + Field.Width * .72f, 134);
            _warning = WarnFor(92, 2);
        }
        if (_mechanic == 18)
        {
            int count = 4 + Tier;
            for (int i = 0; i < count; i++)
                _points[i] = _origin + new Vector2(Mathf.Cos(i * Mathf.Tau / count) * 92, Mathf.Sin(i * Mathf.Tau / count) * 62);
        }
        if (_mechanic == 19)
        {
            _points[0] = new Vector2(Field.CenterX - 52, 58);
            _points[1] = new Vector2(Field.Right - 36, 154);
        }
        if (_mechanic == 20) _origin = _aim.Clamp(new Vector2(Field.Left + 72, 70), new Vector2(Field.Right - 72, 150));
    }

    protected override void Step(double delta)
    {
        switch (_mechanic)
        {
            case 0: OvertakingDeliveries(); break;
            case 1: AlternatingInboxes(); break;
            case 2: UnfastenedClips(); break;
            case 3: UnsentHistory(); break;
            case 4: ReflectedReply(); break;
            case 5: FilingPiles(); break;
            case 6: RainSplash(); break;
            case 7: SpentWavefront(); break;
            case 8: BraidedDelivery(delta); break;
            case 9: DwellTimecard(delta); break;
            case 10: ExchangedPriority(); break;
            case 11: CarriedEnvelope(); break;
            case 12: ExtendingClock(); break;
            case 13: TensionedClips(); break;
            case 14: CoincidentMembranes(); break;
            case 15: ExactRetraction(); break;
            case 16: PausedOutbox(); break;
            case 17: SolidUmbrellas(); break;
            case 18: ProximityAddressing(); break;
            case 19: InboxTransfer(); break;
            case 20: BreathVortex(delta); break;
        }
        LastPosition = _origin;
    }

    private Letter Send(Vector2 at, Vector2 velocity, int group = 0, int pattern = 1)
    {
        var shot = Shot(at, velocity == Vector2.Zero ? Vector2.Down : velocity, pattern);
        shot.Accel = false;
        shot.Velocity = velocity;
        var letter = new Letter { Shot = shot, Activation = shot.ActivationId, Origin = at,
            Direction = velocity.Normalized(), Born = Time, Group = group };
        _letters.Add(letter);
        return letter;
    }
    private void EndAfter(double duration)
    {
        if (Time > duration) Finish();
    }
    private float Progress(double start, double duration) => Mathf.Clamp((float)((Time - start) / duration), 0, 1);

    private void OvertakingDeliveries()
    {
        int batches = 2 + Tier;
        for (int wave = 0; wave < batches; wave++)
        {
            double launch = _warning + wave * 2.1;
            float center = Mathf.Clamp(_aim.X + (wave % 2 == 0 ? -24 : 30), Field.Left + 42, Field.Right - 42);
            if (Time < launch && Time >= launch - _warning)
                for (int j = 0; j < 3 + Tier; j++)
                {
                    float x = center + (j - (2 + Tier) * .5f) * 17;
                    Line(new(x, 10), new(x, 190), 3, false, Progress(launch - _warning, _warning));
                }
            if (At(launch) || At(launch + .7))
            {
                bool express = Time >= launch + .7;
                int count = 3 + Tier;
                for (int j = 0; j < count; j++)
                {
                    var letter = Send(new(center + (j - (count - 1) * .5f) * 17, 8), Vector2.Down * 27, express ? 1 : 0);
                    if (express) letter.Shot.MakeAccel(27, 88 + Tier * 6, .28f);
                }
            }
            if (Time >= launch && Time < launch + 1.8) Hint(new(center, 78), Time < launch + .7 ? "普通便 → 速達" : "追越し");
        }
        EndAfter(_warning + batches * 2.1 + 4);
    }

    private void AlternatingInboxes()
    {
        double cycle = 2.9;
        int turn = (int)(Math.Max(0, Time - _warning) / cycle);
        if (turn != _beat) { _beat = turn; _aim = Player.GlobalPosition; }
        Vector2 source = new(turn % 2 == 0 ? Field.Left + 16 : Field.Right - 16, 34);
        double local = Time - _warning - turn * cycle;
        if (Time < _warning || local < 1.1)
        {
            float aim = source.DirectionTo(_aim).Angle();
            for (int i = -2 - Tier; i <= 2 + Tier; i++)
                Line(source, source + Vector2.FromAngle(aim + i * .115f) * 230, 3, false,
                    Time < _warning ? Progress(0, _warning) : (float)(local / 1.1));
            Hint(source + new Vector2(turn % 2 == 0 ? 20 : -40, -12), "開封中");
        }
        if (Time >= _warning && turn < 4 + Tier && local >= 1.1 && local < 1.85 && Time >= _next)
        {
            _next = Time + .24 - Tier * .025;
            float aim = source.DirectionTo(_aim).Angle();
            for (int i = -2 - Tier; i <= 2 + Tier; i++)
                Send(source, Vector2.FromAngle(aim + i * .115f) * (45 + Tier * 4));
        }
        if (local >= 1.85) Hint(source + new Vector2(0, 12), "送信済");
        EndAfter(_warning + (4 + Tier) * cycle);
    }

    private void UnfastenedClips()
    {
        int count = 18 + Tier * 4;
        if (Time < _warning)
        {
            for (int i = 0; i < count; i++) Disc(_origin + Vector2.FromAngle(i * Mathf.Tau / count) * 45, 3, false, Progress(0, _warning));
            Hint(_origin + new Vector2(-22, -12), "継ぎ目が外れる");
        }
        if (At(_warning))
            for (int i = 0; i < count; i++) Send(_origin + Vector2.FromAngle(i * Mathf.Tau / count) * 45, Vector2.Zero, i, 2);
        double elapsed = Time - _warning;
        int opening = Mathf.FloorToInt((float)Math.Max(0, elapsed - .7) / .9f) * 3;
        foreach (var letter in _letters.Where(l => l.Alive))
        {
            if (letter.Shot.LeadIn) continue;
            int distance = (letter.Group - opening % count + count) % count;
            Vector2 ray = Vector2.FromAngle(letter.Group * Mathf.Tau / count);
            if (elapsed > .7 && distance < 3 + Tier / 2)
            {
                if (letter.State == 0) { letter.State = 1; letter.Shot.Velocity = ray * 85; }
            }
            else if (letter.State == 0)
                letter.Shot.GlobalPosition = _origin + ray * Math.Max(8, 45 - (float)Math.Max(0, elapsed - .7) * 8);
        }
        EndAfter(_warning + 5.8);
    }

    private void UnsentHistory()
    {
        double recording = 2.6 + Tier * .3;
        if (Time < recording && Time >= _next - _warning)
        {
            _next = Time + _warning + .14;
            _history.Add(Player.GlobalPosition);
        }
        for (int i = 1; i < _history.Count; i++)
        {
            double fire = recording + _warning + (_history.Count - i) * .13;
            bool live = Time >= fire && Time < fire + .75;
            if (Time < fire + .75)
            {
                if (_history[i - 1].DistanceSquaredTo(_history[i]) < 1)
                    Disc(_history[i], 4 + Tier * .7f, live, Progress(recording, _warning));
                else Line(_history[i - 1], _history[i], 4 + Tier * .7f, live, Progress(recording, _warning));
            }
        }
        if (Time < recording) Hint(Player.GlobalPosition + new Vector2(-20, -15), "下書きを記録");
        else if (Time < recording + _warning) Hint(_history[^1] + new Vector2(-20, -12), "末尾から取消");
        EndAfter(recording + _warning + _history.Count * .13 + 1);
    }

    private void ReflectedReply()
    {
        Vector2 wall = _points[0];
        Vector2 incoming = _origin.DirectionTo(wall);
        Vector2 reflected = new(-incoming.X, incoming.Y);
        if (Time < _warning)
        {
            for (int i = -1 - Tier; i <= 1 + Tier; i++)
            {
                Vector2 offset = new(0, i * 10);
                Line(_origin + offset, wall + offset, 3, false, Progress(0, _warning));
                Line(wall + offset, wall + offset + reflected * 220, 3, false, Progress(0, _warning));
            }
            Hint(wall + new Vector2(incoming.X < 0 ? 6 : -42, -12), "返信 ↩");
        }
        for (int wave = 0; wave < 3; wave++)
            if (At(_warning + wave * .7))
                for (int i = -1 - Tier; i <= 1 + Tier; i++) Send(_origin + new Vector2(0, i * 10), incoming * (59 + Tier * 6));
        foreach (var letter in _letters.Where(l => l.Alive && l.State == 0 && !l.Shot.LeadIn))
            if (letter.Shot.GlobalPosition.X <= Field.Left + 8 || letter.Shot.GlobalPosition.X >= Field.Right - 8)
            {
                letter.State = 1;
                letter.Shot.GlobalPosition = new Vector2(Mathf.Clamp(letter.Shot.GlobalPosition.X, Field.Left + 8, Field.Right - 8), letter.Shot.GlobalPosition.Y);
                letter.Shot.Velocity = new Vector2(-letter.Shot.Velocity.X, letter.Shot.Velocity.Y);
            }
        EndAfter(_warning + 8);
    }

    private void FilingPiles()
    {
        int count = 3 + Tier;
        for (int i = 0; i < count; i++)
        {
            float x = Field.Left + 30 + i * (Field.Width - 60) / (count - 1);
            double start = _warning + i * .48;
            double elapsed = Time - start;
            if (Time < start) Line(new(x, 5), new(x, Field.Bottom - 8), 8, false, Progress(start - _warning, _warning));
            for (int row = 0; row < 4; row++)
                if (At(start + row * .65)) Send(new(x, 8), Vector2.Down * 110, i, 2);
            foreach (var letter in _letters.Where(l => l.Alive && l.Group == i && l.State == 0 && !l.Shot.LeadIn))
                if (letter.Shot.GlobalPosition.Y > Field.Bottom - 10 - _states[i] * 8)
                {
                    _states[i]++;
                    letter.State = 1;
                    Pool.Despawn(letter.Shot);
                }
            if (elapsed >= 0 && elapsed < 5.4 && _states[i] > 0)
            {
                Box(new Rect2(x - 12, Field.Bottom - 8 - _states[i] * 8, 24, 8 + _states[i] * 8), true);
                if (elapsed > 4.2) Hint(new(x - 10, Field.Bottom - 18 - _states[i] * 8), "崩れる");
            }
        }
        EndAfter(_warning + count * .48 + 6);
    }

    private void RainSplash()
    {
        for (int wave = 0; wave < 3; wave++)
        {
            double launch = _warning + wave * 2.35;
            if (_states[wave] == 0 && Time >= Math.Max(0, launch - _warning))
            {
                _points[wave] = new Vector2(Player.GlobalPosition.X, Field.Bottom - 14);
                _states[wave] = 1;
            }
            float x = _points[wave].X;
            if (Time < launch && _states[wave] == 1)
            {
                Line(new(x, 8), new(x, Field.Bottom - 14), 12, false, Progress(launch - _warning, _warning));
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < 1 + Tier; i++)
                    {
                        Vector2 splash = new Vector2(side * (52 + i * 8), -10 - i * 12).Normalized();
                        Line(new(x, Field.Bottom - 14), new Vector2(x, Field.Bottom - 14) + splash * 115, 12, false, Progress(launch - _warning, _warning));
                    }
            }
            if (At(launch))
                for (int j = -1; j <= 1; j++) Send(new(x + j * 7, 8), Vector2.Down * 132, wave, 0);
        }
        foreach (var letter in _letters.ToArray())
        {
            if (!letter.Alive || letter.State != 0 || letter.Group >= 10 || letter.Shot.LeadIn || letter.Shot.GlobalPosition.Y < Field.Bottom - 16) continue;
            Vector2 impact = letter.Shot.GlobalPosition;
            Pool.Despawn(letter.Shot);
            letter.State = 1;
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 1 + Tier; i++) Send(impact, new Vector2(side * (52 + i * 8), -10 - i * 12), 10, 2);
        }
        EndAfter(_warning + 9);
    }

    private void SpentWavefront()
    {
        for (int wave = 0; wave < 2 + Tier / 2; wave++)
        {
            double elapsed = Time - _warning - wave * 2.7;
            if (elapsed < 0 && elapsed > -_warning) Disc(_origin, 20, false, (float)(1 + elapsed / _warning));
            if (elapsed < 0 || elapsed > 2.55) continue;
            if (elapsed < .3) Disc(_origin, 20, true);
            float radius = Math.Min(80, 18 + (float)elapsed * (27 + Tier * 2));
            for (int i = 0; i < 64; i++)
                Line(_origin + Vector2.FromAngle(i * Mathf.Tau / 64) * radius,
                    _origin + Vector2.FromAngle((i + 1) * Mathf.Tau / 64) * radius, 2 + Tier * .4f, elapsed < 2.05, elapsed < 2.05 ? 1 : 0);
            if (elapsed > 1.6) Hint(_origin + new Vector2(-28, -14), elapsed < 2.05 ? "波が薄れる" : "内側へ戻れる");
        }
        EndAfter(_warning + 2.7 * (2 + Tier / 2));
    }

    private void BraidedDelivery(double delta)
    {
        _origin = new Vector2(Field.CenterX, 60);
        if (Time < _warning)
        {
            for (int i = 0; i < 3; i++) Disc(_origin + new Vector2((i - 1) * 36, 0), (2 + Tier) * 4 + 4, false, Progress(0, _warning));
            Hint(_origin + new Vector2(-36, -20), "三尾の前後を見る");
        }
        if (At(_warning))
            for (int bundle = 0; bundle < 3; bundle++)
                for (int i = 0; i < 3 + Tier; i++) Send(_origin + new Vector2((bundle - 1) * 36, (i - (2 + Tier) * .5f) * 8), Vector2.Zero, bundle);
        double elapsed = Time - _warning;
        for (int bundle = 0; bundle < 3; bundle++)
        {
            double release = 2.3 + bundle * 1.15;
            float phase = (float)Math.Min(elapsed, release - .7) * 2.4f + bundle * Mathf.Tau / 3;
            Vector2 knot = _origin + new Vector2(Mathf.Sin(phase) * 52, Mathf.Cos(phase) * 14);
            if (elapsed >= release - .7 && _states[bundle] == 0)
            {
                _states[bundle] = 1;
                _points[bundle] = knot;
                _points[bundle + 3] = knot.DirectionTo(Player.GlobalPosition);
            }
            if (_states[bundle] > 0) knot = _points[bundle];
            if (_states[bundle] == 1)
                Line(knot, knot + _points[bundle + 3] * 180, (2 + Tier) * 4 + 4, false, (float)((elapsed - release + .7) / .7));
            int index = 0;
            foreach (var letter in _letters.Where(l => l.Alive && l.Group == bundle))
            {
                if (!letter.Shot.LeadIn && letter.State == 0)
                    letter.Shot.GlobalPosition = letter.Shot.GlobalPosition.MoveToward(knot + new Vector2(0, (index - (2 + Tier) * .5f) * 8), 50 * (float)delta);
                if (elapsed >= release && letter.State == 0)
                {
                    letter.State = 1;
                    letter.Shot.Velocity = _points[bundle + 3] * 18;
                    letter.Shot.MakeAccel(18, 95 + Tier * 7, .25f);
                }
                index++;
            }
            if (elapsed >= release) _states[bundle] = 2;
        }
        EndAfter(_warning + 7.6);
    }

    private void DwellTimecard(double delta)
    {
        if (Player.GlobalPosition.DistanceTo(_dwell) > 9)
        {
            _dwell = Player.GlobalPosition;
            _still = 0;
        }
        else _still += delta;
        if (Time >= _warning && _still >= .65 - Tier * .08 && Time >= _next)
        {
            _stamps.Add(new Stamp { Position = _dwell, At = Time + WarnFor(23, 1.05) });
            _still = 0;
            _next = Time + .8;
        }
        foreach (var stamp in _stamps)
            if (Time < stamp.At + .6)
            {
                Disc(stamp.Position, 19 + Tier * 2, Time >= stamp.At, Progress(stamp.At - 1.2, 1.2));
                Hint(stamp.Position + new Vector2(-12, -26), Time < stamp.At ? "締切" : "承認");
            }
        Hint(_dwell + new Vector2(-18, 25), "滞在 " + Math.Min(100, (int)(_still / (.65 - Tier * .08) * 100)) + "%");
        EndAfter(_warning + 7.2);
    }

    private void ExchangedPriority()
    {
        _origin = new Vector2(Field.CenterX, 112);
        double swap = _warning + 1.6;
        if (Time < _warning)
        {
            float span = (2 + Tier) * 4.5f + 4;
            Line(new(Field.Left + 8, _origin.Y), new(Field.Right - 8, _origin.Y), span, false, Progress(0, _warning));
            Line(new(_origin.X, 8), new(_origin.X, Field.Bottom - 8), span, false, Progress(0, _warning));
        }
        if (At(_warning))
            for (int i = 0; i < 3 + Tier; i++)
            {
                Send(new(Field.Left + 8 - i * 5, _origin.Y + (i - (2 + Tier) * .5f) * 9), Vector2.Right * 53, 0);
                Send(new(_origin.X + (i - (2 + Tier) * .5f) * 9, 8), Vector2.Down * 24, 1);
            }
        foreach (var letter in _letters.Where(l => l.Alive))
        {
            bool switched = Time >= swap;
            letter.Shot.Velocity = letter.Direction * ((letter.Group == 0) == switched ? 24 : 70);
        }
        if (Time > _warning && Time < swap + 1)
            foreach (var group in _letters.Where(l => l.Alive).GroupBy(l => l.Group))
                Hint(group.First().Shot.GlobalPosition + new Vector2(-20, -14), group.Key == 0 ? Time >= swap ? "遅・後着" : "速 → 遅" : Time >= swap ? "速・先着" : "遅 → 速");
        if (Time < swap) Hint(_origin + new Vector2(-32, 28), "便の優先度を交換");
        EndAfter(_warning + 7.6);
    }

    private void CarriedEnvelope()
    {
        double elapsed = Time - _warning;
        Vector2 carrier = new(Field.Left + 26 + Mathf.Clamp((float)elapsed, 0, 6) * 35, Mathf.Clamp(_aim.Y, 55, 155));
        bool exists = elapsed >= 0 && elapsed < 6;
        Box(new Rect2(carrier - new Vector2(18, 11), new Vector2(36, 22)), exists, Progress(0, _warning));
        for (int opening = 0; opening < 3; opening++)
        {
            double when = 1.6 + opening * 1.35;
            if (elapsed > when - 1.2 && elapsed < when)
            {
                Line(carrier + new Vector2(18, 0), carrier + new Vector2(108, 0), 15, false, (float)((elapsed - when + 1.2) / 1.2));
                Hint(carrier + new Vector2(-15, -18), "開封 →");
            }
            if (At(_warning + when))
                for (int j = -2 - Tier; j <= 2 + Tier; j++)
                    Send(carrier + new Vector2(19, j * 2), Vector2.FromAngle(j * .14f) * (62 + Tier * 4), 0, 2);
        }
        EndAfter(_warning + 8);
    }

    private void ExtendingClock()
    {
        _origin = new Vector2(Field.CenterX, Field.CenterY);
        double elapsed = Time - _warning;
        float cycle = Tier switch { 2 => 6.7f, 3 => 6f, _ => 7.5f };
        float reach = Mathf.Max(Tier switch { 2 => 124f, 3 => 140f, _ => 104f }, _aim.DistanceTo(_origin) + 16 + Tier * 4);
        double phase = Tier < 2 ? elapsed : elapsed * 7.5 / cycle;
        float angle = _angle + (float)Math.Max(0, elapsed) * (Mathf.Tau / cycle);
        float length = phase < 2 ? 44 : phase < 4.5 ? Mathf.Lerp(44, reach, Mathf.Clamp((float)(phase - 2), 0, 1)) : 44;
        Vector2 tip = _origin + Vector2.FromAngle(angle) * length;
        Line(_origin + Vector2.FromAngle(angle) * 12, tip, 3 + Tier * .5f, elapsed >= 0, Progress(0, _warning));
        if (elapsed >= 0 && phase < 3)
        {
            Line(tip, _origin + Vector2.FromAngle(angle) * reach, 3 + Tier * .5f, false, Mathf.Clamp((float)phase / 2, 0, 1));
            Hint(_origin + new Vector2(-24, 20), "23:00　延長");
        }
        EndAfter(_warning + cycle);
    }

    private void TensionedClips()
    {
        double elapsed = Math.Max(0, Time - _warning);
        Vector2 left = new(Field.Left + 32 + (float)Math.Sin(elapsed * .7) * 17, 58 + (float)Math.Sin(elapsed * .9) * 28);
        Vector2 right = new(Field.Right - 32 - (float)Math.Sin(elapsed * .6) * 15, 150 + (float)Math.Sin(elapsed * .8 + 1) * 26);
        _points[0] = left; _points[1] = right;
        double phase = elapsed % ClipCycle;
        bool taut = Time >= _warning && phase >= ClipLead && phase < ClipLead + 1.3;
        Disc(left, 6, Time >= _warning, Progress(0, _warning));
        Disc(right, 6, Time >= _warning, Progress(0, _warning));
        Line(left, right, 2 + Tier * .6f, taut, (float)(phase / ClipLead));
        if (!taut) Hint((left + right) * .5f + new Vector2(-12, -10), "糸が張る");
        EndAfter(_warning + (Tier < 2 ? 9.6 : ClipCycle * 3));
    }

    private void CoincidentMembranes()
    {
        double elapsed = Time - _warning;
        float distance = 58 - (float)Math.Max(0, elapsed) * (30 + Tier * 4);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            float x = _origin.X + distance * sign;
            Line(new(x, 4), new(x, Field.Bottom - 4), 2.5f + Tier * .35f, elapsed >= 0, Progress(0, _warning));
        }
        Hint(_origin + new Vector2(-35, -23), GameManager.Instance!.HasDodge
            ? elapsed < 1.35 ? "境界が重なる時に回避" : "重なる"
            : elapsed < 0 ? "予兆中に、広い側の外へ" : "外側で距離を保つ");
        EndAfter(_warning + 4.3);
    }

    private void ExactRetraction()
    {
        int count = 7 + Tier * 4;
        if (Time < _warning)
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = Vector2.FromAngle(i * Mathf.Tau / count);
                Line(_origin + dir * 12, _origin + dir * 96, 1, false, Progress(0, _warning));
            }
        if (At(_warning))
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = Vector2.FromAngle(i * Mathf.Tau / count);
                Send(_origin + dir * 12, dir * 24);
            }
        double elapsed = Time - _warning;
        foreach (var letter in _letters.Where(l => l.Alive && !l.Shot.LeadIn))
        {
            double travel = Time - letter.Born - .32;
            if (travel >= 3 && letter.State == 0)
            {
                letter.State = 1;
                letter.Shot.Velocity = -letter.Direction * 34;
            }
            if (letter.State == 1 && (letter.Shot.GlobalPosition - letter.Origin).Dot(letter.Direction) <= 0) Pool.Despawn(letter.Shot);
        }
        if (elapsed >= 0 && elapsed < 3.4) Hint(_origin + new Vector2(-24, -18), elapsed < 2.4 ? "取消まで " + Math.Max(0, 3 - (int)elapsed) : "送信取消 ↩");
        EndAfter(_warning + 6.4);
    }

    private void PausedOutbox()
    {
        if (Time < _warning)
        {
            float center = 52 + (2 + Tier) * 2.5f;
            for (int i = 0; i < 3; i++) Line(new(Field.Left + 8, center + i * 50), new(Field.Right - 8, center + i * 50),
                (2 + Tier) * 2.5f + 3, false, Progress(0, _warning));
            Hint(new(Field.CenterX - 28, 30), "内線で一時保留");
        }
        if (At(_warning))
            for (int group = 0; group < 3; group++)
                for (int i = 0; i < 3 + Tier; i++)
                    Send(new(Field.Left + 10, 52 + group * 50 + i * 5), Vector2.Right * (30 + group * 6), group, 2);
        foreach (var letter in _letters.Where(l => l.Alive))
        {
            double restart = _warning + 4.3 + letter.Group * .7;
            bool held = Time >= _warning + 1.7 && Time < restart;
            letter.Shot.Velocity = held ? Vector2.Zero : Vector2.Right * (Time >= restart ? 72 + Tier * 5 : 30 + letter.Group * 6);
        }
        foreach (var group in _letters.Where(l => l.Alive && l.Shot.Velocity == Vector2.Zero).GroupBy(l => l.Group))
            Hint(group.First().Shot.GlobalPosition + new Vector2(-3, -12), (group.Key + 1).ToString());
        if (Time >= _warning + 1.7 && Time < _warning + 4.3) Hint(new(Field.CenterX - 25, 30), "通話中 → 番号順に再開");
        EndAfter(_warning + 9);
    }

    private void SolidUmbrellas()
    {
        double elapsed = Time - _warning;
        for (int i = 0; i < 2; i++)
        {
            float tilt = elapsed < 3 ? 0 : Mathf.Clamp((float)(elapsed - 3) / 1.5f, 0, 1) * (i == 0 ? .55f : -.55f);
            Vector2 span = Vector2.FromAngle(tilt) * 34;
            _points[i + 2] = _points[i] - span;
            _points[i + 4] = _points[i] + span;
            Hint(_points[i] + new Vector2(-18, 22), i == 0 ? "黄色い傘" : "紺の傘");
        }
        if (Time < _warning) Hint(new(Field.CenterX - 36, 42), "傘が雨を受け止める");
        if (elapsed > 1.7 && elapsed < 3) Hint(new(Field.CenterX - 26, 42), "傘が傾く");
        if (elapsed >= 0 && elapsed < 6 && Time >= _next)
        {
            _next = Time + .43 - Tier * .045;
            int count = 10 + Tier * 3;
            for (int i = 0; i < count; i++)
                Send(new(Field.Left + 7 + (i + (_beat % 2) * .5f) * (Field.Width - 14) / count, 6), Vector2.Down * (62 + Tier * 5));
            _beat++;
        }
        foreach (var letter in _letters.Where(l => l.Alive && !l.Shot.LeadIn))
            for (int i = 0; i < 2; i++)
                if (Math.Min(Geometry2D.GetClosestPointToSegment(letter.Shot.GlobalPosition, _points[i + 2], _points[i] + new Vector2(0, -12)).DistanceTo(letter.Shot.GlobalPosition),
                    Geometry2D.GetClosestPointToSegment(letter.Shot.GlobalPosition, _points[i] + new Vector2(0, -12), _points[i + 4]).DistanceTo(letter.Shot.GlobalPosition)) < letter.Shot.CollisionRadius + 4)
                { Pool.Despawn(letter.Shot); break; }
        EndAfter(_warning + 9.3);
    }

    private void ProximityAddressing()
    {
        int count = 4 + Tier;
        if (Time < _warning)
            for (int i = 0; i < count; i++) Disc(_points[i], 7, false, Progress(0, _warning));
        if (At(_warning))
            for (int i = 0; i < count; i++) Send(_points[i], Vector2.Zero, i);
        foreach (var letter in _letters.Where(l => l.Alive))
        {
            if (letter.Shot.LeadIn) continue;
            if (letter.State == 0 && Player.GlobalPosition.DistanceTo(letter.Origin) < 58)
            {
                letter.State = 1;
                letter.Direction = letter.Origin.DirectionTo(Player.GlobalPosition);
                letter.Changed = Time + WarnFor(30, 1.3);
            }
            if (letter.State == 0) Hint(letter.Origin + new Vector2(-18, -13), "宛名なし");
            if (letter.State == 1)
            {
                Line(letter.Origin, letter.Origin + letter.Direction * 160, 4, false, Progress(letter.Changed - 1.3, 1.3));
                if (Time >= letter.Changed)
                {
                    letter.State = 2;
                    letter.Shot.Velocity = letter.Direction * 16;
                    letter.Shot.MakeAccel(16, 90 + Tier * 8, .22f);
                }
            }
        }
        EndAfter(_warning + 10);
    }

    private void InboxTransfer()
    {
        Vector2 inbox = _points[0], outbox = _points[1];
        if (Time < _warning)
        {
            Line(new(Field.Left + 8, inbox.Y), inbox, 7, false, Progress(0, _warning));
            Disc(outbox, 18, false, Progress(0, _warning));
        }
        Hint(inbox + new Vector2(-18, -17), "受信箱");
        Hint(outbox + new Vector2(-18, -17), "転送先");
        for (int i = 0; i < 4 + Tier; i++)
            if (At(_warning + i * .48)) Send(new(Field.Left + 8, inbox.Y), Vector2.Right * 60, i);
        foreach (var letter in _letters.ToArray())
        {
            if (!letter.Alive && letter.State != 1) continue;
            if (letter.State == 0 && !letter.Shot.LeadIn && letter.Shot.GlobalPosition.X >= inbox.X)
            {
                Pool.Despawn(letter.Shot);
                letter.State = 1;
                letter.Changed = Time + WarnFor(32, 1.35);
                letter.Direction = outbox.DirectionTo(Player.GlobalPosition);
            }
            if (letter.State == 1)
            {
                Line(outbox, outbox + letter.Direction * 190, 3, false, Progress(letter.Changed - 1.35, 1.35));
                if (Time >= letter.Changed)
                {
                    letter.State = 2;
                    var forwarded = Send(outbox, letter.Direction * (58 + Tier * 7), 20);
                    forwarded.State = 2;
                    forwarded.Shot.GlobalPosition = inbox;
                    forwarded.Shot.DeployBossFormation(outbox, edgeLaunch: true, warnedLaunch: true);
                }
            }
        }
        EndAfter(_warning + 9.8);
    }

    private void BreathVortex(double delta)
    {
        if (Time < _warning) Disc(_origin, 43, false, Progress(0, _warning));
        if (Time >= _warning && Time < _warning + 5 && Time >= _next)
        {
            _next = Time + .55 - Tier * .045;
            int count = 3 + Tier;
            for (int i = 0; i < count; i++)
                Send(new(Field.Left + 8, _origin.Y + (i - (count - 1) * .5f) * 19), Vector2.Right * 46, 0, 2);
        }
        foreach (var letter in _letters.Where(l => l.Alive && !l.Shot.LeadIn))
        {
            bool inside = letter.Shot.GlobalPosition.DistanceTo(_origin) < 43;
            if (inside && letter.State == 0) { letter.State = 1; letter.Changed = Time; }
            if (letter.State == 1 && inside && Time - letter.Changed < 1.3)
                letter.Shot.Velocity = letter.Shot.Velocity.Rotated((float)delta * (1.5f + Tier * .15f));
            else if (letter.State == 1) letter.State = 2;
        }
        Hint(_origin + new Vector2(-32, -52), "息の中だけ曲がる");
        EndAfter(_warning + 10);
    }

    protected override void DrawTechnique()
    {
        var paper = new Color(Paper, .70f);
        var ink = new Color(Ink, .65f);
        double elapsed = Time - _warning;
        switch (_mechanic)
        {
            case 0:
                foreach (var group in _letters.Where(l => l.Alive).GroupBy(l => l.Group))
                {
                    var letter = group.First();
                    Vector2 at = letter.Shot.GlobalPosition + new Vector2(9, 0);
                    OfficeArrow(at, Vector2.Down, 9, ink);
                    if (group.Key == 1) OfficeArrow(at + new Vector2(4, -4), Vector2.Down, 9, paper);
                }
                break;
            case 1:
                int turn = (int)(Math.Max(0, elapsed) / 2.9);
                for (int side = 0; side < 2; side++)
                    Envelope(new Vector2(side == 0 ? Field.Left + 16 : Field.Right - 16, 34), new Vector2(10, 6),
                        side == turn % 2 && (elapsed < 0 || elapsed % 2.9 < 1.85) ? 1 : 0, side == turn % 2 ? paper : new Color(Ink, .35f));
                break;
            case 2:
                foreach (var letter in _letters.Where(l => l.Alive && l.Group % 3 == 0))
                    PaperClip(letter.Shot.GlobalPosition, letter.Direction.Angle(), letter.State > 0, ink);
                break;
            case 3:
                double recording = 2.6 + Tier * .3;
                if (Time < recording)
                    PaperSheet(Player.GlobalPosition + new Vector2(13, -11), -.15f, ink);
                else if (_history.Count > 0)
                {
                    int cursor = Math.Clamp(_history.Count - 1 - (int)(Math.Max(0, Time - recording - _warning) / .13), 0, _history.Count - 1);
                    UndoMark(_history[cursor] + new Vector2(0, -10), paper);
                }
                break;
            case 4:
                Vector2 wall = _points[0], incoming = _origin.DirectionTo(wall);
                Vector2 reflected = new(-incoming.X, incoming.Y);
                DrawLine(wall - incoming * 14, wall, ink, 1, true);
                OfficeArrow(wall, reflected, 17, paper);
                DrawLine(wall + new Vector2(0, -9), wall + new Vector2(0, 9), ink, 1.4f, true);
                break;
            case 5:
                for (int i = 0; i < 3 + Tier; i++)
                {
                    float x = Field.Left + 30 + i * (Field.Width - 60) / (2 + Tier);
                    double age = elapsed - i * .48;
                    if (age < 0 || age >= 5.4) continue;
                    for (int page = 0; page < _states[i]; page++)
                    {
                        float y = Field.Bottom - 12 - page * 8;
                        float shift = age > 4.2 ? Mathf.Sin((float)age * 24 + page) * 1.5f : 0;
                        DrawPolyline(new[] { new Vector2(x - 10 + shift, y), new Vector2(x + 7 + shift, y), new Vector2(x + 10 + shift, y - 3) }, paper, .7f, true);
                    }
                }
                break;
            case 6:
                for (int i = 0; i < 3; i++)
                    if (_states[i] > 0 && Time < _warning + i * 2.35)
                        DrawPolyline(new[] { new Vector2(_points[i].X - 10, 11), new Vector2(_points[i].X, 15), new Vector2(_points[i].X + 10, 11) }, ink, 1, true);
                foreach (var splash in _letters.Where(l => l.Group == 10 && Time - l.Born < .32).GroupBy(l => l.Born))
                {
                    var drop = splash.First();
                    float age = (float)(Time - drop.Born);
                    DrawArc(drop.Origin, 4 + age * 24, Mathf.Pi, Mathf.Tau, 10, new Color(Ink, .6f * (1 - age / .32f)), .8f, true);
                }
                break;
            case 7:
                DrawPolyline(new[] { _origin + new Vector2(-11, -7), _origin + new Vector2(11, -7), _origin + new Vector2(11, 3),
                    _origin + new Vector2(4, 3), _origin + new Vector2(0, 7), _origin + new Vector2(0, 3), _origin + new Vector2(-11, 3), _origin + new Vector2(-11, -7) }, ink, .8f, true);
                for (int i = -1; i <= 1; i++) DrawCircle(_origin + new Vector2(i * 5, -2), .8f, paper);
                break;
            case 8:
                foreach (var group in _letters.Where(l => l.Alive).GroupBy(l => l.Group))
                {
                    Vector2 knot = group.Select(l => l.Shot.GlobalPosition).Aggregate(Vector2.Zero, (a, b) => a + b) / group.Count();
                    Vector2 tail = _states[group.Key] == 2 ? -_points[group.Key + 3] : Vector2.Left;
                    Vector2 side = tail.Orthogonal();
                    DrawPolyline(new[] { knot + side * 6, knot + tail * 15 + side * 4, knot + tail * 22,
                        knot + tail * 15 - side * 4, knot - side * 6 }, ink, .8f, true);
                    OfficeNumber(knot + new Vector2(0, -16), group.Key + 1, paper);
                }
                break;
            case 9:
                foreach (var stamp in _stamps)
                {
                    if (Time >= stamp.At + .6) continue;
                    float radius = 19 + Tier * 2;
                    float lift = (float)Math.Clamp(stamp.At - Time, 0, 1) * 9;
                    Vector2 top = stamp.Position - new Vector2(0, radius + 4 + lift);
                    DrawRect(new Rect2(top - new Vector2(6, 5), new Vector2(12, 5)), ink, false, .8f);
                    DrawLine(top, top + new Vector2(0, 5), paper, 1, true);
                    if (Time >= stamp.At)
                        DrawPolyline(new[] { stamp.Position + new Vector2(-7, 0), stamp.Position + new Vector2(-2, 5), stamp.Position + new Vector2(8, -6) }, paper, 1, true);
                }
                break;
            case 11:
                Vector2 carrier = new(Field.Left + 26 + Mathf.Clamp((float)elapsed, 0, 6) * 35, Mathf.Clamp(_aim.Y, 55, 155));
                if (elapsed < 6)
                {
                    float open = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        double until = 1.6 + i * 1.35 - elapsed;
                        if (until >= 0 && until < 1.2) open = 1 - (float)until / 1.2f;
                    }
                    Envelope(carrier, new Vector2(16, 9), open, paper);
                }
                break;
            case 12:
                DrawArc(_origin, 23, 0, Mathf.Tau, 48, new Color(Paper, .32f), .7f, true);
                for (int i = 0; i < 12; i++)
                {
                    Vector2 dir = Vector2.FromAngle(i * Mathf.Tau / 12);
                    DrawLine(_origin + dir * (i % 3 == 0 ? 18 : 20), _origin + dir * 23, paper, .8f, true);
                }
                DrawCircle(_origin, 1.5f, paper);
                break;
            case 14:
                float distance = 58 - (float)Math.Max(0, elapsed) * (30 + Tier * 4);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 tab = new(_origin.X + distance * side, side < 0 ? 11 : Field.Bottom - 11);
                    DrawRect(new Rect2(tab - new Vector2(4, 5), new Vector2(8, 10)), ink, false, .8f);
                    OfficeArrow(tab + new Vector2(side * 6, 0), Vector2.Left * side, 7, paper);
                }
                break;
            case 15:
                float remaining = 1 - Mathf.Clamp((float)(elapsed - .32) / 3, 0, 1);
                if (elapsed >= 0 && remaining > 0)
                    DrawArc(_origin, 9, -Mathf.Pi / 2, -Mathf.Pi / 2 + remaining * Mathf.Tau, 24, ink, 1, true);
                else if (elapsed >= 0) UndoMark(_origin, paper);
                foreach (var letter in _letters.Where(l => l.Alive && l.State == 1).Take(4))
                    OfficeArrow(letter.Shot.GlobalPosition + letter.Direction * 9, -letter.Direction, 7, ink);
                break;
            case 16:
                bool held = elapsed >= 1.7 && elapsed < 5.7;
                Vector2 phone = new(Field.Right - 22, 24);
                DrawPolyline(new[] { phone + new Vector2(-8, -5), phone + new Vector2(-8, 1), phone + new Vector2(8, 1), phone + new Vector2(8, -5) }, paper, 1.5f, true);
                if (held)
                {
                    float ring = .35f + .25f * (1 + Mathf.Sin((float)Time * 8));
                    DrawArc(phone, 12, -2.9f, -.25f, 12, new Color(Ink, ring), .8f, true);
                }
                foreach (var group in _letters.Where(l => l.Alive).GroupBy(l => l.Group))
                {
                    var letter = group.First();
                    Vector2 at = letter.Shot.GlobalPosition + new Vector2(-10, 0);
                    if (letter.Shot.Velocity == Vector2.Zero)
                    {
                        DrawLine(at + new Vector2(-2, -3), at + new Vector2(-2, 3), ink, 1.3f);
                        DrawLine(at + new Vector2(2, -3), at + new Vector2(2, 3), ink, 1.3f);
                    }
                    else OfficeArrow(at, Vector2.Right, 7, paper);
                }
                break;
            case 18:
                foreach (var letter in _letters.Where(l => l.Alive && l.State < 2))
                {
                    Envelope(letter.Origin, new Vector2(9, 6), letter.State == 1 ? Progress(letter.Changed - 1.3, 1.3) : 0, ink);
                    if (letter.State == 1)
                    {
                        DrawLine(letter.Origin + new Vector2(-4, 2), letter.Origin + new Vector2(3, 2), paper, .8f);
                        OfficeArrow(letter.Origin + letter.Direction * 13, letter.Direction, 8, paper);
                    }
                }
                break;
        }
        if (_mechanic == 10)
            foreach (var group in _letters.Where(l => l.Alive && !l.Shot.LeadIn).GroupBy(l => l.Group))
            {
                var letter = group.First();
                Vector2 origin = letter.Shot.GlobalPosition, tip = origin + letter.Shot.Velocity * .34f;
                Vector2 ray = letter.Shot.Velocity.Normalized();
                DrawLine(origin, tip, Paper, 1.2f, true);
                DrawLine(tip, tip - ray.Rotated(.5f) * 7, Paper, 1.2f, true);
                DrawLine(tip, tip - ray.Rotated(-.5f) * 7, Paper, 1.2f, true);
                PaperSheet(origin - ray * 10, ray.Angle() - Mathf.Pi / 2, new Color(Ink, .55f));
            }
        if (_mechanic == 13)
        {
            PaperClip(_points[0], -.4f, false, ink);
            PaperClip(_points[1], .4f, false, ink);
            double phase = Math.Max(0, Time - _warning) % ClipCycle;
            if (phase < ClipLead)
            {
                Vector2 middle = (_points[0] + _points[1]) * .5f + new Vector2(0, 20 * (1 - (float)(phase / ClipLead)));
                DrawLine(_points[0], middle, Paper, .8f, true);
                DrawLine(middle, _points[1], Paper, .8f, true);
            }
        }
        if (_mechanic == 17)
            for (int i = 0; i < 2; i++)
            {
                Vector2 a = _points[i + 2], b = _points[i + 4];
                Color color = i == 0 ? Paper : Ink;
                DrawColoredPolygon(new[] { a, _points[i] + new Vector2(0, -12), b }, new Color(i == 0 ? "af863e" : "233b67", .7f));
                DrawLine(a, _points[i] + new Vector2(0, -12), color, 2, true);
                DrawLine(_points[i] + new Vector2(0, -12), b, color, 2, true);
                DrawLine(a, b, color, 1.7f, true);
                DrawLine(_points[i], _points[i] + new Vector2(0, 23), color, 1, true);
                DrawLine(_points[i] + new Vector2(0, -12), a.Lerp(b, .25f), new Color(color, .4f), .7f, true);
                DrawLine(_points[i] + new Vector2(0, -12), a.Lerp(b, .75f), new Color(color, .4f), .7f, true);
                DrawArc(_points[i] + new Vector2(4, 23), 4, 0, Mathf.Pi, 10, color, 1, true);
            }
        if (_mechanic == 19)
            for (int i = 0; i < 2; i++)
            {
                DrawRect(new Rect2(_points[i] - new Vector2(12, 9), new Vector2(24, 18)), i == 0 ? Paper : Ink, false, 1.4f);
                DrawPolyline(new[] { _points[i] + new Vector2(-10, -2), _points[i] + new Vector2(-5, 3), _points[i] + new Vector2(5, 3), _points[i] + new Vector2(10, -2) }, ink, .8f, true);
                OfficeArrow(_points[i] + new Vector2(0, i == 0 ? -17 : -8), i == 0 ? Vector2.Down : Vector2.Up, 7, paper);
                int waiting = _letters.Count(l => l.State == 1);
                if (waiting > 0) OfficeNumber(_points[i] + new Vector2(17, 3), waiting, paper);
            }
        if (_mechanic == 20)
        {
            DrawArc(_origin, 43, 0, Mathf.Tau, 48, new Color(Ink, .6f), 1, true);
            for (int i = 0; i < 3; i++)
            {
                float angle = (float)Time * 1.4f + i * Mathf.Tau / 3;
                DrawArc(_origin, 16 + i * 8, angle, angle + 1.5f, 14, new Color(Paper, .45f), .8f, true);
            }
            foreach (var letter in _letters.Where(l => l.Alive && l.State == 1).Take(3))
                PaperSheet(letter.Shot.GlobalPosition - letter.Shot.Velocity.Normalized() * 9,
                    letter.Shot.Velocity.Angle(), new Color(Ink, .5f));
        }
    }

    private void Envelope(Vector2 at, Vector2 half, float open, Color color)
    {
        DrawRect(new Rect2(at - half, half * 2), color, false, .8f);
        DrawPolyline(new[] { at + new Vector2(-half.X, -half.Y), at + new Vector2(0, half.Y * (0.3f - open * 2)),
            at + new Vector2(half.X, -half.Y) }, color, .8f, true);
        DrawLine(at + new Vector2(-half.X, half.Y), at - new Vector2(half.X * .25f, 0), new Color(color, color.A * .6f), .6f, true);
        DrawLine(at + half, at + new Vector2(half.X * .25f, 0), new Color(color, color.A * .6f), .6f, true);
    }

    private void PaperSheet(Vector2 at, float angle, Color color)
    {
        var points = new[] { new Vector2(-5, -7), new Vector2(2, -7), new Vector2(5, -4), new Vector2(5, 7), new Vector2(-5, 7), new Vector2(-5, -7) };
        DrawPolyline(points.Select(p => at + p.Rotated(angle)).ToArray(), color, .8f, true);
        for (int i = 0; i < 2; i++)
            DrawLine(at + new Vector2(-3, i * 3).Rotated(angle), at + new Vector2(2, i * 3).Rotated(angle), color, .6f, true);
    }

    private void PaperClip(Vector2 at, float angle, bool loose, Color color)
    {
        var points = new[] { new Vector2(0, -4), new Vector2(3, -4), new Vector2(3, 5), new Vector2(-3, 5),
            new Vector2(-3, -6), new Vector2(1, -6), new Vector2(1 + (loose ? 4 : 0), 2) };
        DrawPolyline(points.Select(p => at + p.Rotated(angle)).ToArray(), color, .8f, true);
    }

    private void OfficeArrow(Vector2 at, Vector2 direction, float length, Color color)
    {
        Vector2 tip = at + direction * length;
        DrawLine(at, tip, color, .8f, true);
        DrawLine(tip, tip - direction.Rotated(.6f) * 4, color, .8f, true);
        DrawLine(tip, tip - direction.Rotated(-.6f) * 4, color, .8f, true);
    }

    private void UndoMark(Vector2 at, Color color)
    {
        DrawArc(at, 6, -.5f, 3.6f, 18, color, 1, true);
        Vector2 tip = at + Vector2.FromAngle(3.6f) * 6;
        DrawPolyline(new[] { tip + new Vector2(-1, -4), tip, tip + new Vector2(4, -1) }, color, 1, true);
    }

    private void OfficeNumber(Vector2 at, int number, Color color)
    {
        string text = number.ToString();
        float width = UiKit.ZenBold.GetStringSize(text, fontSize: 8).X;
        DrawString(UiKit.ZenBold, at - new Vector2(width * .5f, 0), text, fontSize: 8, modulate: color);
    }
}
