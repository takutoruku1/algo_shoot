using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class MinaBossTechnique : BossCharacterTechnique
{
    private sealed class Memory
    {
        public Bullet Bullet = null!;
        public ulong Activation;
        public Vector2 Origin, Direction;
        public double Born;
        public int Index;
        public uint Layer, Mask;
        public bool Live => Bullet.Active && Bullet.ActivationId == Activation;
    }
    private sealed class ElasticConnection
    {
        public Vector2 Root, RestEnd, End, Direction;
        public float RestLength, Extension, Speed, Range;
        public double ReleaseAt;
        public int Rank;
        public Memory? Memory;
        public bool Released, Spent;
    }
    private readonly List<Memory> _memories = new();
    private readonly List<ElasticConnection> _connections = new();
    private bool _connectionsLocked;
    private double _connectionWarning, _connectionLastRelease;
    private float _connectionRadius;
    private double ConnectionPullTime => Tier < 2 ? 1.7 : 1.5;
    private double ConnectionInterval => Tier switch { 0 => .72, 1 => .60, 2 => .44, _ => .30 };
    private const float ConnectionFriction = 12;
    private readonly bool[] _read = new bool[32];
    private Vector2 _anchor, _center, _axis, _initialBoss;
    private Vector2 _returnCenter, _returnSpan;
    private Vector2 _airlockOrigin, _airlockAxis;
    private double _airlockEntryAt, _airlockOpenAt, _airlockExitAt;
    private float _returnPhase;
    private double _warning, _next;
    private int _id, _beat;
    private readonly bool[] _cells = new bool[35], _nextCells = new bool[35];
    private readonly int[] _visits = new int[35];
    private readonly double[] _markedAt = new double[35];
    private int _lastCell = -1, _generation = -1;
    private readonly float[] _springX = new float[16], _springV = new float[16];
    private readonly HashSet<(ulong, ulong)> _received = new();
    private double _replyAt = -1;
    private bool _waitingReply;
    private double _exchangeAt;
    private int _exchanges;
    private bool _exchangePending;
    private int Density => Tier == 0 ? 6 : 6 + Tier * 6;
    private float Speed => 42 + Tier * 7;
    private static readonly Color MemoryInk = new("efabd9");
    private Texture2D _memoryIllustration = null!;
    protected override bool UsesCustomHazardArt => Kind == BossTechniqueKind.Area;

    protected override void Start()
    {
        _anchor = Player.GlobalPosition;
        _center = new Vector2(Mathf.Clamp(_anchor.X, Field.Left + 70, Field.Right - 70),
            Mathf.Clamp(_anchor.Y, 60, Field.Bottom - 60));
        _axis = _anchor.X < Field.CenterX ? Vector2.Left : Vector2.Right;
        _initialBoss = Boss.GlobalPosition;
        if (Kind == BossTechniqueKind.Area)
        {
            StartEasyMemory();
            return;
        }
        _warning = WarnFor(65, 1.6);
        _next = _warning;
        _id = Kind switch
        {
            BossTechniqueKind.Normal => Variant,
            BossTechniqueKind.Edge => 3,
            BossTechniqueKind.Area => 4 + Variant,
            BossTechniqueKind.Crossfire => 9 + Variant,
            BossTechniqueKind.Dodge => 14,
            BossTechniqueKind.Extra => 15 + Variant + (Evolved ? 2 : 0),
            _ => throw new InvalidOperationException("Mina uses five phase signatures, not an animal or legacy finale."),
        };
        var identity = _id switch
        {
            0 => ("記憶の回廊", "mina.sliding-cache", "空き区画へ移る記憶を見て、次に空く通路を選ぶ"),
            1 => ("星屑の扇", "mina.decompressing-pages", "圧縮された頁が広がる前に、短い側を通り抜ける"),
            2 => ("未送信の花冠", "mina.weighted-pending-queue", "印の多い記憶から落ちる。落下順を見て間を渡る"),
            3 => ("四方のリフレイン", "mina.boundary-wrap", "端を抜けた記憶の、反対側の再入口を読む"),
            4 => ("消せなかった声", "mina.overloaded-vessel", "抱え込んで膨らむ側から離れ、崩れる方向を外す"),
            5 => ("未送信、再送", "mina.acknowledged-drafts", "未送信の文字に触れて既読にすると、その場所の着弾が止まる"),
            6 => ("拍手の檻", "mina.protective-airlock", "手前の扉を抜けて待ち、奥の扉が開いてから進む"),
            7 => ("閉じない配信", "mina.self-propagating-records", "点滅する次の記録を読み、次の世代で消える区画へ移る"),
            8 => ("リフレイン・帰り道", "mina.return-junctions", "接続の開いた枝を選び、途切れる枝から離れる"),
            9 => ("消せない応答", "mina.delayed-spring", "隣の節が近づく圧縮を読み、ほどけた節の側へ抜ける"),
            10 => ("交差する記憶", "mina.snapshot-exchange", "記憶の位置と向きが向かいの領域へ写る。保存された予告から離れる"),
            11 => ("記憶の往復", "mina.second-visit-weight", "二度通った場所に重さが残る。まだ通っていない道を選ぶ"),
            12 => ("つなぎ直す記録", "mina.momentum-handoff", "動く記憶が止まった記憶に触れると、動きが受け渡される"),
            13 => ("未送信の星列", "mina.reply-driven-stars", "こちらの射撃に星が応答する。次の返答の線を見て撃つ間を選ぶ"),
            14 => ("龍翼・切れかけた接続", "mina.elastic-disconnection", "短く引いて、固定された反動の線の横へ。強く張った接続から切れる"),
            15 => ("抱えた言葉の重さ", "mina.suspended-burden", "振り子が端で遅くなる間に、重りの下を通る"),
            16 => ("返せなかった四行", "mina.retracted-line", "四行のうち、打消し線のついた一行は発火しない"),
            17 => ("龍翼・抱えきれないもの", "mina.wing-displacement", "翼が押す向きの変化を見て、流れの弱い側へ回る"),
            _ => ("龍翼・ただいま", "mina.return-distance", "龍から離れすぎず近づきすぎず、一緒に帰る"),
        };
        Identify(identity.Item1, identity.Item2, identity.Item3);
        if (_id == 6) PrepareAirlock();
        if (_id == 14)
        {
            _connectionWarning = WarnFor(72, 1.8);
            float radius = Evolved ? BulletPool.BossChargeRadius : BulletPool.BossRadius;
            _connectionRadius = Enumerable.Range(0, 3).Max(pattern =>
            {
                var art = BulletArt.BossPattern(Job.Tank, pattern);
                return BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position,
                    Bullet.BossArtSize(radius, Evolved)).Radius;
            });
            int count = 2 + Tier;
            float toward = _anchor.X < Field.CenterX ? 1 : -1;
            for (int i = 0; i < count; i++)
            {
                float spread = (i / (float)(count - 1) - .5f) * (.5f + Tier * .2f);
                Vector2 end = (_anchor + Vector2.Down * ((i - (count - 1) * .5f) * 10))
                    .Clamp(new Vector2(Field.Left + 8, 8), new Vector2(Field.Right - 8, Field.Bottom - 8));
                Vector2 root = (end + new Vector2(toward, 0).Rotated(spread) * 68)
                    .Clamp(new Vector2(Field.Left + 8, 8), new Vector2(Field.Right - 8, Field.Bottom - 8));
                _connections.Add(new ElasticConnection
                {
                    Root = root, RestEnd = end, End = end, RestLength = end.DistanceTo(root) - 20,
                });
            }
        }
        if (_id == 18)
        {
            _warning = WarnFor(Math.Abs(_anchor.DistanceTo(_initialBoss) - 65), 2);
            var mina = (BossMina)Boss;
            var near = mina.KeepDragonInField(new Vector2(Field.Left, Field.Top));
            var far = mina.KeepDragonInField(new Vector2(Field.Right, Field.Bottom));
            _returnCenter = (near + far) * .5f;
            _returnSpan = (far - near) * .5f - Vector2.One * 2;
            _returnPhase = ((_initialBoss - _returnCenter) / _returnSpan).Angle();
        }
    }

    protected override void Step(double delta)
    {
        if (Kind == BossTechniqueKind.Area)
        {
            StepEasyMemory();
            return;
        }
        switch (_id)
        {
            case 0: SlidingCache(); break;
            case 1: DecompressingPages(); break;
            case 2: WeightedQueue(); break;
            case 3: BoundaryWrap(); break;
            case 4: OverloadedVessel(); break;
            case 5: AcknowledgedDrafts(); break;
            case 6: ProtectiveAirlock(); break;
            case 7: PropagatingRecords(); break;
            case 8: ReturnJunctions(); break;
            case 9: DelayedSpring(delta); break;
            case 10: LocalOverwrite(); break;
            case 11: SecondVisitWeight(); break;
            case 12: MomentumHandoff(); break;
            case 13: ReplyDrivenStars(); break;
            case 14: ElasticDisconnection(); break;
            case 15: SuspendedBurden(); break;
            case 16: RetractedLine(); break;
            case 17: WingDisplacement(delta); break;
            case 18: ReturnDistance(delta); break;
        }
    }

    private Memory Remember(Vector2 at, Vector2 direction, int index, float speed)
    {
        var bullet = Shot(at, direction, index % 3);
        bullet.Velocity = direction.Normalized() * speed;
        var memory = new Memory { Bullet = bullet, Activation = bullet.ActivationId, Origin = at,
            Direction = direction, Born = Time, Index = index };
        _memories.Add(memory);
        return memory;
    }

    private void SlidingCache()
    {
        double period = 1.45;
        int turn = Math.Max(0, (int)((Time - _warning) / period));
        int moves = 5 + Tier;
        if (Time > _warning + moves * period) { Finish(); return; }
        int empty = new[] { 4, 3, 0, 1, 2, 5, 8, 7, 6 }[turn % 9];
        int next = new[] { 3, 0, 1, 2, 5, 8, 7, 6, 3 }[turn % 9];
        float move = Time < _warning ? 0 : Mathf.Clamp((float)((Time - _warning) % period - 0.65) / 0.65f, 0, 1);
        Vector2 origin = _center - new Vector2(57, 57);
        for (int i = 0; i < 9; i++)
        {
            if (i == empty) continue;
            Vector2 at = origin + new Vector2(i % 3 * 38, i / 3 * 38);
            if (i == next) at = at.Lerp(origin + new Vector2(empty % 3 * 38, empty / 3 * 38), move);
            Box(new Rect2(at, new Vector2(24, 24)), Time > _warning);
        }
        Hint(origin + new Vector2(next % 3 * 38, next / 3 * 38 - 4), "→");
    }

    private void DecompressingPages()
    {
        for (int i = 0; i < 3 + Tier; i++)
        {
            double born = _warning + i * 0.85;
            float t = (float)(Time - born);
            Vector2 origin = new(_axis.X < 0 ? Field.Right - 20 : Field.Left + 20, 30 + i % 4 * 48);
            if (t < 0)
            {
                if (t > -1.25) Box(new Rect2(origin - new Vector2(15, 4), new Vector2(30, 8)), false);
                continue;
            }
            if (t > 5.6) continue;
            float expansion = Mathf.SmoothStep(0, 1, Mathf.Clamp((t - 1) / 1.2f, 0, 1));
            Vector2 size = new(Mathf.Lerp(28, 7, expansion), Mathf.Lerp(6, 52 + Tier * 5, expansion));
            Vector2 at = origin + _axis * t * (40 + Tier * 4);
            Box(new Rect2(at - size * 0.5f, size), true);
            Line(at - Vector2.Down * 32, at + Vector2.Down * 32, 0.5f, false);
        }
        if (Time > _warning + (3 + Tier) * 0.85 + 5.6) Finish();
    }

    private void WeightedQueue()
    {
        int count = 4 + Tier;
        for (int i = 0; i < count; i++)
        {
            int weight = (i * 3 + 1) % count;
            double release = _warning + (count - 1 - weight) * 0.65;
            Vector2 at = new(Field.Left + 26 + i * (Field.Width - 52) / (count - 1), 22 + i % 2 * 20);
            if (Time < release)
            {
                for (int k = 0; k < (Tier == 0 ? 1 : 2 + Tier); k++)
                    Disc(at + new Vector2((k - (1 + Tier) * 0.5f) * 10, 0), 7, false, (float)(Time / release));
                Hint(at + new Vector2(-5, -11), new string('·', weight + 1));
            }
            if (At(release))
                for (int k = 0; k < (Tier == 0 ? 1 : 2 + Tier); k++)
                    Remember(at + new Vector2((k - (1 + Tier) * 0.5f) * 10, 0), Vector2.Down, i, 38 + weight * 7);
        }
        if (Time > _warning + count * 0.65 + 6) Finish();
    }

    private void BoundaryWrap()
    {
        for (int i = 0; i < 2 + Tier; i++)
        {
            float y = 35 + i * 146f / (1 + Tier);
            Line(new Vector2(Field.Left, y), new Vector2(Field.Right, y), 5, false);
            if (At(_warning))
                for (int j = 0; j < 3; j++)
                    Remember(new Vector2(Field.Left + 14 + j * 18, y), Vector2.Right, i, Speed);
        }
        foreach (var memory in _memories)
        {
            if (!memory.Live || memory.Bullet.LeadIn) continue;
            float travelled = (float)(Time - memory.Born) * Speed;
            float x = Field.Left + 10 + Mathf.PosMod(memory.Origin.X - Field.Left - 10 + travelled, Field.Width - 20);
            bool seam = x < Field.Left + 22 || x > Field.Right - 22;
            memory.Bullet.Velocity = Vector2.Zero;
            memory.Bullet.GlobalPosition = new Vector2(x, memory.Origin.Y);
            if (memory.Layer == 0)
            {
                memory.Layer = memory.Bullet.CollisionLayer;
                memory.Mask = memory.Bullet.CollisionMask;
            }
            if (seam) memory.Bullet.MakeHarmless();
            else
            {
                memory.Bullet.CollisionLayer = memory.Layer;
                memory.Bullet.CollisionMask = memory.Mask;
            }
            Hint(new Vector2(Field.Left + 7, memory.Origin.Y - 10), "↔");
        }
        if (Time > _warning + (Field.Width - 20) / Speed * 1.7) Finish();
    }

    private void OverloadedVessel()
    {
        Vector2 vessel = new(Field.CenterX, 70);
        float load = Mathf.Clamp((float)((Time - _warning) / 3), 0, 1);
        for (int side = 0; side < 3; side++)
        {
            float weight = side == 1 ? 1 : 0.58f;
            Vector2 at = vessel + new Vector2((side - 1) * 48, side == 1 ? 50 : 8);
            float radius = 7 + load * 27 * weight;
            Disc(at, radius, Time >= _warning && Time < _warning + 3.4);
            if (Time > _warning + 2 && Time < _warning + 3.4 + side * 0.7)
                for (int i = 0; i < Density / 3; i++)
                {
                    var origin = at + new Vector2((i - (Density / 3 - 1) * 0.5f) * 9, 0);
                    Line(origin, origin + new Vector2((side - 1) * 0.4f, 1).Normalized() * 90, 5, false);
                }
            if (At(_warning + 3.4 + side * 0.7))
                for (int i = 0; i < Density / 3; i++)
                    Remember(at + new Vector2((i - (Density / 3 - 1) * 0.5f) * 9, 0),
                        new Vector2((side - 1) * 0.4f, 1).Normalized(), i, 43 + side * 10);
        }
        if (Time > _warning + 9) Finish();
    }

    private void AcknowledgedDrafts()
    {
        int count = 3 + Tier;
        double deadline = _warning + 4.5;
        for (int i = 0; i < count; i++)
        {
            Vector2 at = new(Field.Left + 38 + i % 3 * 94, 63 + i / 3 * 88);
            if (Time < deadline && Player.GlobalPosition.DistanceTo(at) < 19) _read[i] = true;
            if (_read[i]) { Hint(at + new Vector2(-8, 0), "既読"); continue; }
            Box(new Rect2(at - new Vector2(26, 20), new Vector2(52, 40)), Time >= deadline && Time < deadline + 1);
            Hint(at + new Vector2(-12, -24), "未送信");
            if (Time < deadline + 1.1)
                for (int k = 0; k < 1 + Tier; k++) Disc(at + new Vector2(k * 9, 0), 6, false);
            if (At(deadline + 1.1))
                for (int k = 0; k < 1 + Tier; k++)
                    Remember(at + new Vector2(k * 9, 0), Vector2.Up, i, 45);
        }
        if (Time > deadline + 5) Finish();
    }

    private void PrepareAirlock()
    {
        _airlockOrigin = _anchor;
        var body = Boss.GetChildren().OfType<CollisionShape2D>().First();
        float radius = body.Shape is CapsuleShape2D capsule ? capsule.Radius : ((CircleShape2D)body.Shape).Radius;
        float halfLine = body.Shape is CapsuleShape2D tall ? Math.Max(0, tall.Height * .5f - radius) : 0;
        float best = float.NegativeInfinity;
        foreach (var axis in new[] { Vector2.Right, Vector2.Left, Vector2.Down, Vector2.Up })
        {
            Vector2 exit = _anchor + axis * 84;
            if (exit.X < Field.Left || exit.X > Field.Right || exit.Y < Field.Top || exit.Y > Field.Bottom) continue;
            float minX = Math.Min(_anchor.X, exit.X), maxX = Math.Max(_anchor.X, exit.X);
            float minY = Math.Min(_anchor.Y, exit.Y), maxY = Math.Max(_anchor.Y, exit.Y);
            float dx = Math.Max(0, Math.Max(minX - body.GlobalPosition.X, body.GlobalPosition.X - maxX));
            float dy = Math.Max(0, Math.Max(minY - body.GlobalPosition.Y - halfLine, body.GlobalPosition.Y - halfLine - maxY));
            float clearance = new Vector2(dx, dy).Length() - radius - Player.HitRadius;
            float score = clearance + exit.DistanceTo(body.GlobalPosition) * .001f;
            if (score <= best) continue;
            best = score;
            _airlockAxis = axis;
        }
        _airlockEntryAt = WarnFor(42, Tier switch { 0 => 2.0, 1 => 1.8, 2 => 1.6, _ => 1.4 });
        _airlockOpenAt = _airlockEntryAt + (Tier switch { 0 => 1.2, 1 => 1.05, 2 => .85, _ => .65 });
        _airlockExitAt = _airlockOpenAt + WarnFor(42, Tier switch { 0 => 2.0, 1 => 1.8, 2 => 1.55, _ => 1.3 });
    }

    private void ProtectiveAirlock()
    {
        bool entered = Time >= _airlockEntryAt;
        bool open = Time >= _airlockOpenAt;
        bool sealedRoom = Time >= _airlockExitAt;
        for (int gate = 0; gate < 2; gate++)
        {
            float along = gate == 0 ? 24 : 60;
            Vector2 at = _airlockOrigin + _airlockAxis * along;
            Vector2 from = _airlockAxis.X == 0 ? new Vector2(Field.Left, at.Y) : new Vector2(at.X, Field.Top);
            Vector2 to = _airlockAxis.X == 0 ? new Vector2(Field.Right, at.Y) : new Vector2(at.X, Field.Bottom);
            bool closed = gate == 0 ? entered : !open || sealedRoom;
            if (closed) Line(from, to, 3 + Tier * .4f, true);
            Hint(at + _airlockAxis.Orthogonal() * 25, gate == 0 ? entered ? "手前：閉" : "手前：開" : closed ? "奥：閉" : "奥：開");
        }
        if (!open)
            Box(AirlockPressure(24), entered, (float)Math.Min(1, Time / _airlockEntryAt));
        else
        {
            Box(AirlockPressure(24), true);
            Box(AirlockPressure(60), sealedRoom, (float)Math.Min(1, (Time - _airlockOpenAt) / (_airlockExitAt - _airlockOpenAt)));
        }
        if (Time > _airlockExitAt + .8) Finish();
    }

    private Rect2 AirlockPressure(float along)
    {
        Vector2 end = _airlockOrigin + _airlockAxis * along;
        if (_airlockAxis.X > 0) return new Rect2(Field.Left, Field.Top, end.X - Field.Left, Field.Height);
        if (_airlockAxis.X < 0) return new Rect2(end.X, Field.Top, Field.Right - end.X, Field.Height);
        if (_airlockAxis.Y > 0) return new Rect2(Field.Left, Field.Top, Field.Width, end.Y - Field.Top);
        return new Rect2(Field.Left, end.Y, Field.Width, Field.Bottom - end.Y);
    }

    private void PropagatingRecords()
    {
        double period = Tier switch { 2 => 1.6, 3 => 1.4, _ => 1.8 };
        int generation = Time < _warning ? 0 : 1 + (int)((Time - _warning) / period);
        if (_generation < 0)
        {
            foreach (int index in new[] { 2, 10, 15, 16, 17 }) _cells[index] = true;
            if (Tier > 0)
                foreach (int index in new[] { 4, 12, 18, 19, 20 }) _cells[index] = true;
            if (Tier >= 2)
                foreach (int index in new[] { 7, 8 }) _cells[index] = true;
            if (Tier == 3)
                foreach (int index in new[] { 13, 28 }) _cells[index] = true;
            _generation = 0;
            NextGeneration();
        }
        if (generation != _generation)
        {
            Array.Copy(_nextCells, _cells, 35);
            _generation = generation;
            NextGeneration();
        }
        if (generation > 4 + Tier || generation > 0 && !_cells.Any(live => live)) { Finish(); return; }
        for (int i = 0; i < 35; i++)
        {
            Vector2 at = new(Field.Left + 26 + i % 7 * 38, 16 + i / 7 * 38);
            if (_cells[i]) Box(new Rect2(at, new Vector2(26, 26)), Time >= _warning);
            else if (_nextCells[i]) Box(new Rect2(at, new Vector2(26, 26)), false);
            if (_nextCells[i] && !_cells[i]) Hint(at + new Vector2(5, 17), "+");
        }
    }
    private void NextGeneration()
    {
        for (int i = 0; i < 35; i++)
        {
            int x = i % 7, y = i / 7, neighbors = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && x + dx >= 0 && x + dx < 7 && y + dy >= 0 && y + dy < 5
                        && _cells[(y + dy) * 7 + x + dx]) neighbors++;
            _nextCells[i] = neighbors == 3 || _cells[i] && neighbors == 2;
        }
    }

    private void ReturnJunctions()
    {
        double t = Time - _warning;
        int branch = Math.Max(0, (int)(t / 2.4));
        if (branch >= 3 + Tier) { Finish(); return; }
        Vector2 fork = new(Field.Left + 70 + branch % 3 * 70, 108);
        float width = Tier == 0 ? 15 : 10;
        bool upConnected = branch % 2 == 0;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            Vector2 end = fork + new Vector2(42, sign * 64);
            bool severed = sign < 0 != upConnected;
            Line(fork, end, width, severed && t >= 0.8 + branch * 2.4);
            Hint(end + new Vector2(-8, 0), severed ? "×" : "接続");
        }
        if (Time < _warning + branch * 2.4 + 1.1)
            for (int i = 0; i < Density / 2; i++)
                Disc(fork + new Vector2(75, (i - (Density / 2 - 1) * 0.5f) * 10), 6, false);
        if (At(_warning + branch * 2.4 + 1.1))
            for (int i = 0; i < Density / 2; i++)
                Remember(fork + new Vector2(75, (i - (Density / 2 - 1) * 0.5f) * 10),
                    new Vector2(-1, upConnected ? 0.55f : -0.55f), i, 45);
    }

    private void DelayedSpring(double delta)
    {
        double t = Time - _warning;
        const int count = 16;
        float rest = (Field.Width - 32) / (count - 1);
        if (_springX[0] == 0)
            for (int i = 0; i < count; i++) _springX[i] = Field.Left + 16 + i * rest;
        for (int pulse = 0; pulse < 1 + Tier; pulse++)
            if (At(_warning + pulse * 2.4)) _springV[0] = 120;
        if (t >= 0)
        {
            var force = new float[count];
            for (int i = 0; i < count; i++)
            {
                float left = i == 0 ? Field.Left + 16 - rest : _springX[i - 1];
                float right = i == count - 1 ? Field.Right - 16 + rest : _springX[i + 1];
                force[i] = ((right - _springX[i] - rest) - (_springX[i] - left - rest)) * 75 - _springV[i] * 2;
            }
            for (int i = 0; i < count; i++)
            {
                _springV[i] += force[i] * (float)delta;
                _springX[i] += _springV[i] * (float)delta;
            }
        }
        for (int i = 0; i < count; i++)
        {
            Vector2 p = new(_springX[i], _center.Y);
            bool compressed = i > 0 && _springX[i] - _springX[i - 1] < rest * 0.78f
                || i < count - 1 && _springX[i + 1] - _springX[i] < rest * 0.78f;
            Disc(p, 5, t >= 0 && compressed);
            if (i > 0) Line(new Vector2(_springX[i - 1], _center.Y), p, 0.5f, false);
        }
        if (t > 4 + Tier * 2.4) Finish();
    }

    private void LocalOverwrite()
    {
        int count = 3 + Tier;
        Vector2 Center(int side) => new(Field.Left + 72 + side * 174, 108);
        for (int side = 0; side < 2; side++)
        {
            var center = Center(side);
            Hint(center + Vector2.Up * 62, _exchangePending ? "保存した記憶 → 交換" : "記憶を保持");
            for (int i = 0; i < count; i++)
            {
                Vector2 p = center + new Vector2((i % 3 - 1) * 24, i < 3 ? -25 : 25);
                Vector2 direction = Vector2.FromAngle(.4f + i * 1.12f + side * 1.7f);
                if (Time < _warning) Disc(p, 8, false);
                if (At(_warning)) Remember(p, direction, side * count + i, 18 + Tier * 3);
            }
        }
        if (At(_warning)) _next = Time + 2.6;
        if (!_exchangePending && Time >= _next && Time > _warning && _exchanges < 2 + Tier)
        {
            _exchangePending = true;
            _exchangeAt = Time + WarnFor(38, 1.3);
            foreach (var memory in _memories.Where(m => m.Live))
            {
                memory.Origin = memory.Bullet.GlobalPosition;
                memory.Direction = memory.Bullet.Velocity;
            }
        }
        foreach (var memory in _memories.Where(m => m.Live && !m.Bullet.LeadIn))
        {
            int side = memory.Index / count;
            Vector2 center = Center(side);
            if (_exchangePending)
            {
                Vector2 destination = Center(1 - side) + memory.Origin - center;
                Disc(destination, 8, false, (float)(1 - (_exchangeAt - Time) / WarnFor(38, 1.3)));
                Line(destination, destination + memory.Direction.Normalized() * 20, .5f, false);
                if (Time >= _exchangeAt)
                {
                    memory.Bullet.GlobalPosition = destination;
                    memory.Bullet.Velocity = memory.Direction;
                    memory.Index = (1 - side) * count + memory.Index % count;
                    continue;
                }
            }
            Vector2 position = memory.Bullet.GlobalPosition;
            Vector2 velocity = memory.Bullet.Velocity;
            if (position.X <= center.X - 35 && velocity.X < 0 || position.X >= center.X + 35 && velocity.X > 0)
                velocity.X = -velocity.X;
            if (position.Y <= center.Y - 44 && velocity.Y < 0 || position.Y >= center.Y + 44 && velocity.Y > 0)
                velocity.Y = -velocity.Y;
            memory.Bullet.Velocity = velocity;
        }
        if (_exchangePending && Time >= _exchangeAt)
        {
            _exchangePending = false;
            _exchanges++;
            _next = Time + 2.6 - Tier * .15;
        }
        if (_exchanges == 2 + Tier && Time >= _next) Finish();
    }

    private void SecondVisitWeight()
    {
        double t = Time - _warning;
        if (t >= 0)
        {
            int x = Mathf.Clamp((int)((Player.GlobalPosition.X - Field.Left) / (Field.Width / 7)), 0, 6);
            int y = Mathf.Clamp((int)(Player.GlobalPosition.Y / (Field.Bottom / 5)), 0, 4);
            int cell = y * 7 + x;
            if (cell != _lastCell)
            {
                _visits[cell]++;
                if (_visits[cell] == 2) _markedAt[cell] = Time;
                _lastCell = cell;
            }
        }
        for (int i = 0; i < 35; i++)
        {
            if (_visits[i] < 2) continue;
            Vector2 at = new(Field.Left + i % 7 * (Field.Width / 7), i / 7 * (Field.Height / 5));
            var rect = new Rect2(at + Vector2.One * 6, new Vector2(Field.Width / 7 - 12, Field.Height / 5 - 12));
            Box(rect, Time > _markedAt[i] + WarnFor(30));
        }
        if (t > 9 + Tier) Finish();
    }

    private void MomentumHandoff()
    {
        int chains = 1 + Tier;
        int length = 3;
        for (int row = 0; row < chains; row++)
        {
            float y = 32 + row * 148f / Math.Max(1, chains - 1);
            Line(new Vector2(Field.Left + 20, y), new Vector2(Field.Right - 20, y), 6, false);
            if (At(_warning))
                for (int i = 0; i < length; i++)
                    Remember(new Vector2(Field.Left + 28 + i * 70, y), Vector2.Right,
                        row * length + i, i == 0 ? Speed : 0);
        }
        for (int row = 0; row < chains; row++)
            for (int i = length - 2; i >= 0; i--)
            {
                var incoming = _memories.FirstOrDefault(m => m.Index == row * length + i);
                var receiver = _memories.FirstOrDefault(m => m.Index == row * length + i + 1);
                if (incoming == null || receiver == null || !incoming.Live || !receiver.Live
                    || incoming.Bullet.LeadIn || receiver.Bullet.LeadIn) continue;
                if (incoming.Bullet.Velocity.Length() > 1 && receiver.Bullet.Velocity.Length() < 1
                    && incoming.Bullet.GlobalPosition.DistanceTo(receiver.Bullet.GlobalPosition) < 14)
                {
                    receiver.Bullet.Velocity = incoming.Bullet.Velocity;
                    incoming.Bullet.Velocity = Vector2.Zero;
                    incoming.Bullet.MakeHarmless();
                }
            }
        if (Time > _warning + 8) Finish();
    }

    private void ReplyDrivenStars()
    {
        if (Time < _warning)
            for (int i = 0; i < Density / 2; i++)
                Disc(new Vector2(Field.Right - 28 - i % 3 * 23, 36 + i / 3 * 60), 6, false);
        if (At(_warning))
        {
            for (int i = 0; i < Density / 2; i++)
                Remember(new Vector2(Field.Right - 28 - i % 3 * 23, 36 + i / 3 * 60), Vector2.Left, i, 0);
            _replyAt = Time + 1.3;
            _waitingReply = true;
        }
        bool newReply = false;
        foreach (var shot in GetTree().GetNodesInGroup("player_bullets").OfType<Bullet>().Where(b => b.Active))
            if (_received.Add((shot.GetInstanceId(), shot.ActivationId))) newReply = true;
        if (newReply && !_waitingReply && Time >= _replyAt + 0.65 && Time > _warning && Time < _warning + 7)
        {
            _replyAt = Time + 1.1;
            _waitingReply = true;
        }
        foreach (var memory in _memories)
        {
            if (!memory.Live || memory.Bullet.LeadIn) continue;
            if (_waitingReply)
                Line(memory.Bullet.GlobalPosition, memory.Bullet.GlobalPosition + Vector2.Left * 44, 5, false);
            if (_waitingReply && Time >= _replyAt)
                memory.Bullet.Velocity = Vector2.Left * 64;
            else if (!_waitingReply && Time >= _replyAt + 0.65)
                memory.Bullet.Velocity = Vector2.Zero;
        }
        if (_waitingReply && Time >= _replyAt)
        {
            _waitingReply = false;
            _beat++;
        }
        Hint(new Vector2(Field.CenterX, 16), _waitingReply ? "返答の予告" : "射撃に応答");
        if (Time > _warning + 9) Finish();
    }

    private void ElasticDisconnection()
    {
        if (!_connectionsLocked)
        {
            Vector2 pull = (Player.GlobalPosition - _anchor).LimitLength(72);
            foreach (var connection in _connections)
            {
                connection.End = (connection.RestEnd + pull)
                    .Clamp(new Vector2(Field.Left + 8, 8), new Vector2(Field.Right - 8, Field.Bottom - 8));
                connection.Extension = Math.Max(8, connection.End.DistanceTo(connection.Root) - connection.RestLength);
            }
            if (Time < ConnectionPullTime) return;
            _connectionsLocked = true;
            var order = _connections.OrderByDescending(c => c.Extension).ToArray();
            for (int rank = 0; rank < order.Length; rank++)
            {
                var connection = order[rank];
                connection.Rank = rank;
                connection.Direction = connection.End.DirectionTo(connection.Root);
                connection.Speed = (1.8f + Tier * .15f) * connection.Extension;
                connection.Range = connection.Speed * connection.Speed / (2 * ConnectionFriction);
                connection.ReleaseAt = Time + _connectionWarning + rank * ConnectionInterval;
                _connectionLastRelease = connection.ReleaseAt;
            }
        }
        foreach (var connection in _connections)
        {
            if (connection.Spent) continue;
            if (Time < connection.ReleaseAt)
            {
                Vector2 end = ConnectionWarningEnd(connection);
                float progress = (float)(1 - (connection.ReleaseAt - Time) / (_connectionWarning + connection.Rank * ConnectionInterval));
                Line(connection.End, end, _connectionRadius, false, progress);
                Hint(connection.Root + Vector2.Up * 10, (connection.Rank + 1).ToString());
                continue;
            }
            if (!connection.Released)
            {
                connection.Released = true;
                if (connection.Speed <= 0) { connection.Spent = true; continue; }
                connection.Memory = Remember(connection.End, connection.Direction, connection.Rank, 0);
                connection.Memory.Bullet.CancelLeadIn();
            }
            var memory = connection.Memory!;
            if (!memory.Live) { connection.Spent = true; continue; }
            float age = (float)(Time - connection.ReleaseAt);
            Vector2 prior = memory.Bullet.GlobalPosition;
            Vector2 at = ConnectionPoint(connection, age);
            memory.Bullet.GlobalPosition = at;
            Line(prior, at, _connectionRadius, true);
            if (age >= connection.Speed / ConnectionFriction || !Field.Rect.Grow(_connectionRadius + 2).HasPoint(at))
            {
                Pool.Despawn(memory.Bullet);
                connection.Spent = true;
            }
        }
        if (Time > _connectionLastRelease + .35 && _connections.All(c => c.Spent)) Finish();
    }

    private static Vector2 ConnectionPoint(ElasticConnection connection, float time)
    {
        float t = Mathf.Clamp(time, 0, connection.Speed / ConnectionFriction);
        float distance = connection.Speed * t - ConnectionFriction * t * t * .5f;
        return connection.End + connection.Direction * distance;
    }

    private static Vector2 ConnectionWarningEnd(ElasticConnection connection)
    {
        float length = connection.Range;
        if (connection.Direction.X > 0) length = Math.Min(length, (Field.Right - connection.End.X) / connection.Direction.X);
        if (connection.Direction.X < 0) length = Math.Min(length, (Field.Left - connection.End.X) / connection.Direction.X);
        if (connection.Direction.Y > 0) length = Math.Min(length, (Field.Bottom - connection.End.Y) / connection.Direction.Y);
        if (connection.Direction.Y < 0) length = Math.Min(length, (Field.Top - connection.End.Y) / connection.Direction.Y);
        return connection.End + connection.Direction * length;
    }

    private void SuspendedBurden()
    {
        Vector2 pivot = new(Field.CenterX, 18);
        double t = Time - _warning;
        float angle = Mathf.Sin(Math.Max(0, (float)t) * 1.65f) * 0.85f;
        Vector2 weight = pivot + Vector2.Down.Rotated(angle) * 132;
        Line(pivot, weight, 0.7f, false);
        Disc(weight, 16 + Tier * 2, t > 0);
        if (Time > _warning && Time >= _next)
        {
            _next = Time + 1.9;
            for (int i = 0; i < 1 + Tier; i++)
                Remember(weight + new Vector2((i - Tier * 0.5f) * 8, 8), Vector2.Down, i, 32);
        }
        if (t > 8) Finish();
    }

    private void RetractedLine()
    {
        int safe = Mathf.Clamp((int)(_anchor.Y / (Field.Bottom / 4)), 0, 3);
        double reveal = _warning - 0.7;
        for (int row = 0; row < 4; row++)
        {
            float y = 27 + row * 54;
            bool cancelled = row == safe && Time > reveal;
            if (cancelled)
            {
                Hint(new Vector2(Field.Left + 20, y), "取消");
                continue;
            }
            Box(new Rect2(Field.Left + 12, y - 16, Field.Width - 24, 32), Time >= _warning + 0.5 && Time < _warning + 1.3);
            if (At(_warning + 1.4))
                for (int i = 0; i < Density / 3; i++)
                    Remember(new Vector2(Field.Right - 14 - i * 13, y), Vector2.Left, i, 47);
        }
        if (Time > _warning + 8) Finish();
    }

    private void WingDisplacement(double delta)
    {
        double t = Time - _warning;
        if (t < 0)
            for (int i = 0; i < Density; i++)
                Disc(new Vector2(Field.Left + 24 + i * (Field.Width - 48) / (Density - 1), 25), 6, false);
        if (At(_warning))
            for (int i = 0; i < Density; i++)
                Remember(new Vector2(Field.Left + 24 + i * (Field.Width - 48) / (Density - 1), 25), Vector2.Down, i, 22);
        if (t >= 0)
            foreach (var memory in _memories)
            {
                if (!memory.Live || memory.Bullet.LeadIn) continue;
                Vector2 p = memory.Bullet.GlobalPosition;
                float beat = (float)(t % 3);
                float force = beat < 1.2f ? Mathf.Sin(beat / 1.2f * Mathf.Pi) * 38 : 0;
                float side = Math.Sign(p.X - Boss.GlobalPosition.X);
                memory.Bullet.Velocity = new Vector2(side * force, 22 + Math.Abs(p.X - Boss.GlobalPosition.X) * 0.12f);
            }
        if (t > 8) Finish();
    }

    private void ReturnDistance(double delta)
    {
        double t = Time - _warning;
        double duration = Tier switch { 2 => 13, 3 => 12, _ => 8 };
        if (t >= 0)
        {
            Vector2 desired;
            if (Tier >= 2)
            {
                float angle = _returnPhase + (float)t * (Tier == 2 ? .5f : .56f);
                var target = _returnCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _returnSpan;
                desired = Boss.GlobalPosition.MoveToward(target, (Tier == 2 ? 29 : 32) * (float)delta);
            }
            else desired = _initialBoss + new Vector2(Mathf.Sin((float)t * 0.6f) * 18, Mathf.Sin((float)t * 0.9f) * 9);
            Boss.GlobalPosition = ((BossMina)Boss).KeepDragonInField(desired);
        }
        Vector2 center = Boss.GlobalPosition;
        float inner = 39, outer = Tier == 0 ? 99 : 96 - Tier * 4;
        Disc(center, inner, t >= 0 && t < duration);
        OutsideDisc(center, outer, t >= 0 && t < duration);
        LastPosition = center + Vector2.Left * 65;
        if (t > duration + .2) Finish();
    }

    private sealed class MinaEasyMemoryCue
    {
        public string Key = "", Group = "";
        public int Mode, Round, Index;
        public double StartsAt, FiresAt, EndsAt;
        public Vector2[] Points = Array.Empty<Vector2>();
        public Rect2[] Bounds = Array.Empty<Rect2>();
        public Vector2 Size, Card, CardReserved;
        public float Radius, Speed;
        public bool Cancelled;
        public double? CancelledAt;
        public bool Fired, Energized, Transferred, Exited;
        public int Predecessor = -1, Successor = -1;
        public int ReleaseCue = -1, HeldMemory = -1, ReceivedFrom = -1;
        public float Mass;
        public float ReceivedMomentum;
        public double ArrivesAt;
        public double? ObservedFireAt, TransferredAt, ObservedExitAt;
        public double? InitialStartsAt, InitialEndsAt;
        public Vector2 Position, PriorPosition;
        public bool CurrentRowCard, CardAccessVerified, CardStartBodyOverlap;
        public Vector2[] CardAccessPath = Array.Empty<Vector2>();
        public double CardTravelSeconds;
    }
    private readonly List<MinaEasyMemoryCue> _easyMemoryCues = new();
    private readonly HashSet<int> _easyReadRounds = new();
    private readonly bool[] _easyRecordState = new bool[35];
    private int _easyRoundsCreated;
    private Vector2 _easyConnectionPull;
    private CircleShape2D? _easyCardProbe;
    private int EasyRounds => Variant switch { 0 => 3, 1 => 3 + Tier, 2 => 1, 3 => 4 + Tier, _ => 1 };
    private int EasyBurdenLoads => (Tier switch { 0 => 4, 1 => 6, 2 => 9, _ => 12 }) + (Evolved ? 2 : 0);
    private int EasyBurdenRows => 3 + Tier * 2;
    private int EasyDraftRows => 4 + Tier * 2;
    private int EasyRelayChains => 3 + Tier * 3 + (Evolved ? 1 : 0);
    private double EasyRoundInterval => Variant switch { 0 => 5.4, 1 => 5.2, 2 => 0, 3 => 4.4 + Tier * .3 + (Evolved ? .4 : 0), _ => 6.4 };
    internal override IEnumerable<BossEasyCue> EasyCues => Kind == BossTechniqueKind.Area
        ? _easyMemoryCues.Select(c => new BossEasyCue(c.Key, c.StartsAt, c.FiresAt,
            EasyMemoryProgress(c), c.Fired, c.Cancelled))
        : base.EasyCues;

    private float EasyMemoryProgress(MinaEasyMemoryCue cue)
        => Mathf.Clamp((float)((Time - cue.StartsAt - .4) / .8), 0, 1);

    private bool EasyMemorySegmentEnded(MinaEasyMemoryCue cue)
        => cue.Mode == 4 ? cue.Exited || cue.ReleaseCue >= 0 && _easyMemoryCues[cue.ReleaseCue].Energized
            : cue.Mode == 7 ? cue.Exited : Time >= cue.EndsAt;

    private void StartEasyMemory()
    {
        if (Kind != BossTechniqueKind.Area || Variant is < 0 or > 4)
            throw new InvalidOperationException("Mina uses one distinct Area technique per AttackPhase; retired Normal, Edge and Extra slots are not aliases.");
        TextureFilter = TextureFilterEnum.Linear;
        _memoryIllustration = BulletArt.Get(Variant switch {
            0 => "mina_memory", 1 => "mina_unanswered", 2 => "mina_eraser", _ => "mina_butterfly",
        })!;
        _id = 4 + Variant;
        _warning = 3.2;
        var identity = Variant switch
        {
            0 => ("抱えた声の順送り", "mina.unfolding-burden", "重い記憶から解凍される。広がり終える頁の短い端を回る"),
            1 => ("未送信、既読の返事", "mina.acknowledged-rows", "手の届く下書きを既読にする。届いた返事の行には着弾しない"),
            2 => ("拍手の檻・二重の扉", "mina.traveling-airlock", "前後の扉の間を保ち、開いてゆく保護室について進む"),
            3 => ("閉じない配信・残る世代", "mina.inherited-records", "今いる区画も次の種になる。次の記録が生まれない隣の区画へ移る"),
            _ => ("つなぎ直す帰り道", "mina.connected-homeward-relay", "短く引いて張りを固定する。受渡した記憶は危険なまま止まるので、通った後ろへ戻らない"),
        };
        Identify(identity.Item1, identity.Item2, identity.Item3);
        for (int i = 0; i < 35; i++) _easyRecordState[i] = Tier == 0 ? (i % 7 + 2 * (i / 7)) % 3 == 0
            : (i % 7 + 2 * (i / 7)) % (Tier + 3) != 0;
    }

    private void StepEasyMemory()
    {
        if (Variant == 4 && _easyRoundsCreated == 0)
        {
            _easyConnectionPull = (Player.GlobalPosition - _anchor).LimitLength(48);
            if (Time < .8) return;
        }
        while (_easyRoundsCreated < EasyRounds && Time >= _easyRoundsCreated * EasyRoundInterval)
        {
            int round = _easyRoundsCreated++;
            PrepareEasyMemoryRound(round, Variant == 4 ? Time : round * EasyRoundInterval);
        }
        if (Variant == 4) StepEasyMemoryHandoffs();
        foreach (var cue in _easyMemoryCues)
        {
            if (cue.Mode == 7 && Time < cue.StartsAt && cue.InitialStartsAt is double preview
                && cue.InitialEndsAt is double previewEnd && Time >= preview && Time < previewEnd)
            {
                float progress = Mathf.Clamp((float)((Time - preview - .4) / .8), 0, 1);
                if (progress > 0) EasyMemoryPathHazards(cue.Points, cue.Radius, progress, false);
                continue;
            }
            if (cue.Mode == 1 && !cue.Cancelled && Time < cue.FiresAt
                && !_easyReadRounds.Contains(cue.Round) && Player.GlobalPosition.DistanceTo(cue.Card) <= 14)
            {
                cue.Cancelled = true;
                cue.CancelledAt = Time;
                _easyReadRounds.Add(cue.Round);
            }
            if (cue.Cancelled || Time < cue.StartsAt || EasyMemorySegmentEnded(cue)) continue;
            if (cue.Mode is 4 or 7 ? !cue.Energized : Time < cue.FiresAt)
            {
                float progress = EasyMemoryProgress(cue);
                if (progress <= 0) continue;
                if (cue.Mode is 0 or 1 or 3 or 5)
                    foreach (var bounds in cue.Bounds)
                        Box(new Rect2(bounds.Position, new Vector2(bounds.Size.X * progress, bounds.Size.Y)), false, progress);
                else if (cue.Mode is 4 or 7)
                    EasyMemoryPathHazards(cue.Points, cue.Radius, progress, false);
                else
                    foreach (var bounds in cue.Bounds)
                        Box(new Rect2(bounds.Position, new Vector2(bounds.Size.X * progress, bounds.Size.Y)), false, progress);
                continue;
            }
            float age = (float)(Time - cue.FiresAt);
            switch (cue.Mode)
            {
                case 0:
                    Box(EasyBurdenPageBounds(cue, age), true);
                    break;
                case 1:
                case 3:
                case 5:
                    foreach (var bounds in cue.Bounds) Box(bounds, true);
                    break;
                case 2:
                    foreach (var bounds in EasyRoomPressure(EasyRoomProtectedBounds(cue, Time - cue.FiresAt))) Box(bounds, true);
                    break;
                case 4:
                case 7:
                    if (cue.Exited) continue;
                    Line(cue.PriorPosition, cue.Position, cue.Radius, true);
                    break;
            }
            if (!cue.Fired) cue.ObservedFireAt = Time;
            cue.Fired = true;
        }
        bool relayDrained = Variant != 4 || _easyMemoryCues.All(cue => cue.Exited);
        if (_easyRoundsCreated == EasyRounds && _easyMemoryCues.All(c => Time >= c.EndsAt) && relayDrained) Finish();
    }

    private void PrepareEasyMemoryRound(int round, double start)
    {
        Vector2 reserved = Player.GlobalPosition;
        if (Variant == 0)
        {
            float[] rows = Tier == 0 ? new[] { 24, Mathf.Clamp(reserved.Y, 82, 134), Field.Bottom - 24 }
                : Enumerable.Range(0, EasyBurdenRows).Select(row => (row + (round == 0 ? .5f : round == 1 ? 0 : 1)) * Field.Height / EasyBurdenRows).ToArray();
            int direction = round % 2 == 0 ? 1 : -1;
            for (int row = 0; row < EasyBurdenRows; row++)
                for (int load = 0; load < EasyBurdenLoads; load++)
                {
                    float height = Tier == 0 ? row == 1 ? 62 : 52 : Field.Height / EasyBurdenRows - 7;
                    Vector2 from = new(direction > 0 ? Field.Left - 20 : Field.Right + 20, rows[row]);
                    Vector2 to = new(direction > 0 ? Field.Right + 20 : Field.Left - 20, rows[row]);
                    double fire = start + 3.2 + (EasyBurdenRows - 1 - row) * (.42 / (1 + Tier * .5)) + load * (.38 - Tier * .045);
                    _easyMemoryCues.Add(new MinaEasyMemoryCue
                    {
                        Key = $"burden:{round}:{row}:{load}", Group = $"burden:{round}:{row}", Mode = 0,
                        Round = round, Index = row, StartsAt = start, FiresAt = fire,
                        EndsAt = fire + from.DistanceTo(to) / (43 + Tier * 6), Points = new[] { from, to },
                        Bounds = new[] { new Rect2(Field.Left, rows[row] - height * .5f, Field.Width, height).Intersection(Field.Rect) },
                        Size = new Vector2(8, height), Speed = 43 + Tier * 6,
                    });
                }
        }
        else if (Variant == 1)
        {
            float rowHeight = Field.Height / EasyDraftRows;
            int current = Mathf.Clamp((int)((reserved.Y - Field.Top) / rowHeight), 0, EasyDraftRows - 1);
            int answered = (current + 1 + round) % EasyDraftRows;
            for (int row = 0; row < EasyDraftRows; row++)
            {
                var card = EasyAccessibleDraftCard(reserved, row, row == current);
                _easyMemoryCues.Add(new MinaEasyMemoryCue
                {
                    Key = $"draft:{round}:{row}", Group = $"draft:{round}:{row}", Mode = 1,
                    Round = round, Index = row, StartsAt = start, FiresAt = start + 3.2, EndsAt = start + 6 + Tier * .3 + (Evolved ? .4 : 0),
                    Card = card.Card, CardReserved = reserved,
                    Bounds = new[] { new Rect2(Field.Left, Field.Top + row * rowHeight, Field.Width, rowHeight) },
                    Cancelled = row == answered, CancelledAt = row == answered ? start : null,
                    CurrentRowCard = row == current, CardAccessPath = card.Path, CardAccessVerified = card.Verified,
                    CardStartBodyOverlap = card.InitialOverlap, CardTravelSeconds = card.Seconds,
                });
            }
        }
        else if (Variant == 2)
        {
            var half = (Tier switch { 0 => new Vector2(52, 42), 1 => new Vector2(36, 28),
                2 => new Vector2(27, 22), _ => new Vector2(20, 17) }) - (Evolved ? new Vector2(4, 3) : Vector2.Zero);
            Vector2 home = reserved.Clamp(Field.Rect.Position + half, Field.Rect.End - half);
            Vector2[] path = { home, new(home.X, Field.Top + half.Y), new(Field.Left + half.X, Field.Top + half.Y),
                new(Field.Right - half.X, Field.Top + half.Y), new(Field.Right - half.X, Field.Bottom - half.Y),
                new(Field.Left + half.X, Field.Bottom - half.Y), new(home.X, Field.Bottom - half.Y), home };
            path = path.Where((point, index) => index == 0 || point.DistanceTo(path[index - 1]) > 1).ToArray();
            double travel = 0;
            for (int i = 1; i < path.Length; i++) travel += Math.Max(2.4, path[i - 1].DistanceTo(path[i]) / (30 + Tier * 5)) + .8;
            var room = new MinaEasyMemoryCue
            {
                Key = "airlock:room", Group = "airlock:room", Mode = 2, StartsAt = start,
                FiresAt = start + 3.2, EndsAt = start + 3.2 + travel, Points = path, Size = half * 2,
                Bounds = new[] { Field.Rect }, Speed = 30 + Tier * 5,
            };
            _easyMemoryCues.Add(room);
            var doors = EasyAirlockDoors(room);
            for (int index = 0; index < doors.Length; index++)
            {
                var door = doors[index];
                Rect2 passage = door.Entry.Merge(door.Exit);
                Rect2[] closing = EasyRoomPressure(door.Exit).Select(bounds => bounds.Intersection(passage))
                    .Where(bounds => bounds.Size.X > 0 && bounds.Size.Y > 0).ToArray();
                _easyMemoryCues.Add(new MinaEasyMemoryCue
                {
                    Key = $"airlock:rear-door:{index}", Group = $"airlock:rear-door:{index}", Mode = 5,
                    Index = index, StartsAt = door.ClosesAt - 3.2, FiresAt = door.ClosesAt, EndsAt = door.ClosesAt + .4,
                    Bounds = closing, Card = door.Entry.GetCenter(), Points = new[] { door.Entry.GetCenter(), door.Exit.GetCenter() },
                });
            }
        }
        else if (Variant == 3)
        {
            var next = new bool[35];
            for (int i = 0; i < 35; i++)
            {
                int x = i % 7, y = i / 7, neighbors = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && x + dx >= 0 && x + dx < 7 && y + dy >= 0 && y + dy < 5
                            && _easyRecordState[(y + dy) * 7 + x + dx]) neighbors++;
                next[i] = round == 0 ? _easyRecordState[i]
                    : Tier == 0 ? neighbors >= (Evolved ? 1 : 2) && neighbors <= 4 && (x + y + round) % 3 != 0
                    : neighbors > 0 && (x + 2 * y + round) % (Tier + 3) != 0;
            }
            int playerX = Mathf.Clamp((int)((reserved.X - Field.Left) / (Field.Width / 7)), 0, 6);
            int playerY = Mathf.Clamp((int)((reserved.Y - Field.Top) / (Field.Height / 5)), 0, 4);
            next[playerY * 7 + playerX] = true;
            Array.Copy(next, _easyRecordState, 35);
            for (int i = 0; i < 35; i++)
            {
                if (!next[i]) continue;
                var cell = new Rect2(Field.Left + i % 7 * Field.Width / 7, Field.Top + i / 7 * Field.Height / 5,
                    Field.Width / 7, Field.Height / 5).Grow(-1);
                _easyMemoryCues.Add(new MinaEasyMemoryCue
                {
                    Key = $"generation:{round}:{i}", Group = $"generation:{round}:{i}", Mode = 3,
                    Round = round, Index = i, StartsAt = start, FiresAt = start + 3.2, EndsAt = start + 5.8 + Tier * .3 + (Evolved ? .4 : 0),
                    Bounds = new[] { cell }, Card = cell.GetCenter(),
                });
            }
        }
        else
        {
            Vector2 margin = Vector2.One * 28;
            Vector2 target = reserved;
            Vector2 drift = _easyConnectionPull * .22f;
            Vector2 opposite = new(target.X < Field.CenterX ? Field.Right - 28 : Field.Left + 28,
                target.Y < Field.CenterY ? Field.Bottom - 28 : Field.Top + 28);
            Vector2[][] networks = {
                new[] { new Vector2(Field.Left - 28, 28), target, opposite,
                    new Vector2(Field.CenterX, target.Y < Field.CenterY ? 28 : Field.Bottom - 28), new Vector2(Field.Right + 28, 108) },
                new[] { new Vector2(Field.Right - 28, Field.Top - 28), new Vector2(Field.Left + 28, Field.Bottom - 28),
                    target, new Vector2(Field.Right - 28, Field.CenterY), new Vector2(Field.Left - 28, 28) },
                new[] { new Vector2(Field.Right + 28, Field.Bottom - 28), new Vector2(Field.Left + 28, 28),
                    new Vector2(Field.Right - 28, 28), new Vector2(Field.Left + 28, Field.Bottom - 28),
                    new Vector2(Field.Right + 28, Field.Bottom + 28) },
            };
            for (int chain = 0; chain < EasyRelayChains; chain++)
            {
                var network = networks[chain % networks.Length].ToArray();
                if (chain >= networks.Length && Tier > 0)
                    for (int node = 1; node < network.Length - 1; node++)
                        network[node] = new Vector2(Field.Left + 28 + (chain * 3 + node * 2) % 7 * (Field.Width - 56) / 6,
                            28 + (chain + node * 3) % 5 * (Field.Height - 56) / 4);
                for (int node = 1; node < network.Length - 1; node++)
                    if (network[node] != target)
                        network[node] = (network[node] + drift).Clamp(Field.Rect.Position + margin, Field.Rect.End - margin);
                for (int node = 1; node < network.Length - 1; node++)
                    if (network[node - 1].DistanceTo(network[node]) < 48)
                    {
                        if (network[node] == target)
                            network[node - 1] = opposite;
                        else network[node] = new Vector2(network[node - 1].X < Field.CenterX ? Field.Right - 28 : Field.Left + 28,
                            network[node - 1].Y < Field.CenterY ? Field.Bottom - 28 : Field.Top + 28);
                    }
                int chainStart = _easyMemoryCues.Count;
                double chainStartAt = start + chain * (Tier switch { 0 => .65, 1 => .5, 2 => .36, _ => .25 });
                double fire = chainStartAt + 3.2;
                float speed = 52 + Tier * 7 + _easyConnectionPull.Length() * .5f;
                float[] masses = { 1, 1.25f, 1.5f, 1.1f };
                for (int node = 0; node < network.Length - 1; node++)
                {
                    float radius = 10 + (masses[node] - 1) * 3;
                    Vector2 direction = network[node].DirectionTo(network[node + 1]);
                    float nextRadius = node + 1 < masses.Length ? 10 + (masses[node + 1] - 1) * 3 : 0;
                    Vector2 contact = node + 1 < masses.Length ? network[node + 1] - direction * (radius + nextRadius) : network[node + 1];
                    double arrival = fire + network[node].DistanceTo(contact) / speed;
                    _easyMemoryCues.Add(new MinaEasyMemoryCue
                    {
                        Key = $"handoff:{chain}:{node}", Group = $"handoff:{chain}:{node}", Mode = 4,
                        Round = chain, Index = node, StartsAt = chainStartAt, FiresAt = fire,
                        ArrivesAt = arrival, Points = new[] { network[node], contact }, Radius = radius,
                        Mass = masses[node], Speed = speed, Position = network[node], PriorPosition = network[node],
                        Predecessor = node == 0 ? -1 : chainStart + node - 1,
                        Successor = node + 1 == masses.Length ? -1 : chainStart + node + 1,
                    });
                    fire = arrival + .32;
                    if (node + 1 < masses.Length)
                        speed = Mathf.Clamp(speed * masses[node] / masses[node + 1] + _easyConnectionPull.Length() * .08f, 48 + Tier * 5, 88 + Tier * 7);
                }
            }
            double drained = _easyMemoryCues.Where(cue => cue.Successor < 0).Max(cue => cue.ArrivesAt);
            var held = _easyMemoryCues.Where(cue => cue.Successor >= 0).OrderByDescending(cue => cue.Speed * cue.Mass).ToArray();
            for (int rank = 0; rank < held.Length; rank++)
            {
                var memory = held[rank];
                Vector2 direction = memory.Points[1].DirectionTo(memory.Points[0]);
                Vector2 exit = EasyMemoryRecoilExit(memory.Points[1], direction, memory.Radius);
                double release = drained + .2 + rank * .24;
                float speed = 50 + (memory.Speed * memory.Mass - 48) * .3f;
                int heldIndex = _easyMemoryCues.IndexOf(memory);
                memory.ReleaseCue = _easyMemoryCues.Count;
                memory.EndsAt = release;
                _easyMemoryCues.Add(new MinaEasyMemoryCue
                {
                    Key = $"release:{memory.Round}:{memory.Index}", Group = $"release:{memory.Round}:{memory.Index}", Mode = 7,
                    Round = memory.Round, Index = memory.Index, StartsAt = release - 3.2, FiresAt = release,
                    Points = new[] { memory.Points[1], exit }, Radius = memory.Radius, Speed = speed, Mass = memory.Mass,
                    Position = memory.Points[1], PriorPosition = memory.Points[1], HeldMemory = heldIndex,
                    InitialStartsAt = memory.StartsAt, InitialEndsAt = memory.StartsAt + 3.2,
                    ArrivesAt = release + memory.Points[1].DistanceTo(exit) / speed,
                    EndsAt = release + memory.Points[1].DistanceTo(exit) / speed + .02,
                });
            }
            foreach (var terminal in _easyMemoryCues.Where(cue => cue.Mode == 4 && cue.Successor < 0))
                terminal.EndsAt = terminal.ArrivesAt + .02;
        }
    }

    private bool EasyDraftBodyHit(Vector2 position)
    {
        _easyCardProbe ??= new CircleShape2D { Radius = Player.HitRadius };
        if (Boss.CollisionLayer == 0 || !Boss.Monitorable) return false;
        foreach (uint owner in Boss.GetShapeOwners())
        {
            if (Boss.IsShapeOwnerDisabled(owner)) continue;
            Transform2D transform = Boss.GlobalTransform * Boss.ShapeOwnerGetTransform(owner);
            for (int shape = 0; shape < Boss.ShapeOwnerGetShapeCount(owner); shape++)
                if (Boss.ShapeOwnerGetShape(owner, shape).Collide(transform, _easyCardProbe, new Transform2D(0, position))) return true;
        }
        return false;
    }

    private (Vector2 Card, Vector2[] Path, double Seconds, bool Verified, bool InitialOverlap) EasyAccessibleDraftCard(
        Vector2 reserved, int row, bool current)
    {
        float height = Field.Height / EasyDraftRows;
        float top = Field.Top + row * height + 8, bottom = Field.Top + (row + 1) * height - 8;
        float[] ys = Math.Abs(reserved.Y - top) >= Math.Abs(reserved.Y - bottom)
            ? new[] { top, bottom } : new[] { bottom, top };
        float away = reserved.X <= Boss.GlobalPosition.X ? -1 : 1;
        float x = Mathf.Clamp(reserved.X + away * 42, Field.Left + 20, Field.Right - 20);
        bool initialOverlap = EasyDraftBodyHit(reserved);
        // At an edge the inset can reverse the offset; keep a visible edge lane and the opposite row end available.
        float[] xs = { x, Mathf.Clamp(reserved.X + away * 42, Field.Left + 6, Field.Right - 6),
            Mathf.Clamp(reserved.X - away * 42, Field.Left + 20, Field.Right - 20) };
        foreach (float cardX in xs.Distinct())
            foreach (float cardY in ys)
            {
                Vector2 card = new(cardX, cardY);
                if (EasyDraftBodyHit(card) || card.DistanceTo(reserved) <= 16) continue;
                if (!current || initialOverlap) return (card, Array.Empty<Vector2>(), 0, false, initialOverlap);
                float detourX = EasyDraftDetourX(reserved, card, away);
                Vector2[][] paths = { new[] { reserved, card },
                    new[] { reserved, new Vector2(detourX, reserved.Y), new Vector2(detourX, card.Y), card } };
                foreach (var path in paths)
                {
                    var accessible = new List<Vector2> { reserved };
                    float walked = 0;
                    bool blocked = false, touched = false;
                    for (int segment = 1; segment < path.Length && !blocked && !touched; segment++)
                    {
                        float length = path[segment - 1].DistanceTo(path[segment]);
                        int steps = Math.Max(1, (int)Math.Ceiling(length / .5f));
                        for (int sample = 1; sample <= steps; sample++)
                        {
                            Vector2 at = path[segment - 1].Lerp(path[segment], sample / (float)steps);
                            if (EasyDraftBodyHit(at)) { blocked = true; break; }
                            walked += length / steps;
                            if (at.DistanceTo(card) > 13) continue;
                            accessible.Add(at);
                            touched = true;
                            break;
                        }
                        if (!blocked && !touched) accessible.Add(path[segment]);
                    }
                    double seconds = walked / Player.SlowestMoveSpeed;
                    if (!blocked && touched && seconds <= 2)
                        return (card, accessible.ToArray(), seconds, true, false);
                }
            }
        return (new Vector2(x, ys[0]), Array.Empty<Vector2>(), 0, false, initialOverlap);
    }

    private float EasyDraftDetourX(Vector2 from, Vector2 card, float away)
    {
        float x = away < 0 ? Math.Min(from.X, card.X) : Math.Max(from.X, card.X);
        foreach (uint owner in Boss.GetShapeOwners())
        {
            if (Boss.IsShapeOwnerDisabled(owner)) continue;
            Transform2D transform = Boss.GlobalTransform * Boss.ShapeOwnerGetTransform(owner);
            for (int shape = 0; shape < Boss.ShapeOwnerGetShapeCount(owner); shape++)
            {
                Rect2 bounds = transform * Boss.ShapeOwnerGetShape(owner, shape).GetRect();
                x = away < 0 ? Math.Min(x, bounds.Position.X - Player.HitRadius - .5f)
                    : Math.Max(x, bounds.End.X + Player.HitRadius + .5f);
            }
        }
        return Mathf.Clamp(x, Field.Left, Field.Right);
    }

    private static Rect2 EasyBurdenPageBounds(MinaEasyMemoryCue cue, float age)
    {
        Vector2 at = cue.Points[0].MoveToward(cue.Points[1], age * cue.Speed);
        float opening = Mathf.SmoothStep(0, 1, Mathf.Clamp((age - .45f) / 1.1f, 0, 1));
        Vector2 size = new(Mathf.Lerp(28, cue.Size.X, opening), Mathf.Lerp(8, cue.Size.Y, opening));
        return new Rect2(at - size * .5f, size);
    }

    private static (Rect2 Entry, Rect2 Exit, double OpensAt, double ClosesAt)[] EasyAirlockDoors(MinaEasyMemoryCue cue)
    {
        var doors = new List<(Rect2, Rect2, double, double)>();
        double elapsed = 0;
        for (int i = 1; i < cue.Points.Length; i++)
        {
            double move = Math.Max(2.4, cue.Points[i - 1].DistanceTo(cue.Points[i]) / cue.Speed);
            doors.Add((new Rect2(cue.Points[i - 1] - cue.Size * .5f, cue.Size),
                new Rect2(cue.Points[i] - cue.Size * .5f, cue.Size), cue.FiresAt + elapsed + .4, cue.FiresAt + elapsed + .4 + move));
            elapsed += .8 + move;
        }
        return doors.ToArray();
    }

    private static Rect2 EasyRoomProtectedBounds(MinaEasyMemoryCue cue, double age)
    {
        double at = cue.FiresAt + age;
        var doors = EasyAirlockDoors(cue);
        foreach (var door in doors)
        {
            if (at < door.OpensAt) return door.Entry;
            if (at < door.ClosesAt) return door.Entry.Merge(door.Exit);
            if (at < door.ClosesAt + .4) return door.Exit;
        }
        return doors[^1].Exit;
    }

    private static IEnumerable<Rect2> EasyRoomPressure(Rect2 protectedRoom)
    {
        var room = protectedRoom.Intersection(Field.Rect);
        if (room.Position.Y > Field.Top) yield return new Rect2(Field.Left, Field.Top, Field.Width, room.Position.Y - Field.Top);
        if (room.End.Y < Field.Bottom) yield return new Rect2(Field.Left, room.End.Y, Field.Width, Field.Bottom - room.End.Y);
        if (room.Position.X > Field.Left) yield return new Rect2(Field.Left, room.Position.Y, room.Position.X - Field.Left, room.Size.Y);
        if (room.End.X < Field.Right) yield return new Rect2(room.End.X, room.Position.Y, Field.Right - room.End.X, room.Size.Y);
    }

    private void StepEasyMemoryHandoffs()
    {
        foreach (var cue in _easyMemoryCues.Where(cue => cue.Mode == 4))
        {
            cue.PriorPosition = cue.Position;
            if (cue.Predecessor < 0 && Time >= cue.FiresAt) cue.Energized = true;
            if (!cue.Energized || cue.Exited || cue.Transferred) continue;
            cue.Position = cue.Points[0].MoveToward(cue.Points[1], Math.Max(0, (float)(Time - cue.FiresAt)) * cue.Speed);
            if (Time < cue.ArrivesAt) continue;
            if (cue.Successor < 0)
            {
                cue.Exited = !Field.Rect.Grow(cue.Radius).HasPoint(cue.Position);
                if (cue.Exited) cue.ObservedExitAt = Time;
                continue;
            }
            var receiver = _easyMemoryCues[cue.Successor];
            bool touching = cue.Position.DistanceTo(receiver.Points[0]) <= cue.Radius + receiver.Radius + .01f;
            if (!touching || Time < cue.ArrivesAt + .32) continue;
            cue.Transferred = true;
            cue.TransferredAt = Time;
            receiver.ReceivedFrom = _easyMemoryCues.IndexOf(cue);
            receiver.ReceivedMomentum = cue.Speed * cue.Mass;
            receiver.Speed = Mathf.Clamp(receiver.ReceivedMomentum / receiver.Mass + _easyConnectionPull.Length() * .08f, 48 + Tier * 5, 88 + Tier * 7);
            receiver.Energized = true;
        }
        bool drained = _easyMemoryCues.Where(cue => cue.Mode == 4 && cue.Successor < 0).All(cue => cue.Exited);
        foreach (var cue in _easyMemoryCues.Where(cue => cue.Mode == 7))
        {
            cue.PriorPosition = cue.Position;
            if (Time >= cue.FiresAt && drained && _easyMemoryCues[cue.HeldMemory].Transferred)
                cue.Energized = true;
            if (!cue.Energized || cue.Exited) continue;
            cue.Position = cue.Points[0].MoveToward(cue.Points[1], Math.Max(0, (float)(Time - cue.FiresAt)) * cue.Speed);
            var held = _easyMemoryCues[cue.HeldMemory];
            held.PriorPosition = held.Position;
            held.Position = cue.Position;
            if (Time >= cue.ArrivesAt)
            {
                cue.Exited = !Field.Rect.Grow(cue.Radius).HasPoint(cue.Position);
                if (cue.Exited)
                {
                    cue.ObservedExitAt = Time;
                    held.Exited = true;
                    held.ObservedExitAt = Time;
                }
            }
        }
    }

    private static Vector2 EasyMemoryRecoilExit(Vector2 from, Vector2 direction, float radius)
    {
        float distance = float.PositiveInfinity;
        if (direction.X > 0) distance = Math.Min(distance, (Field.Right + radius + 24 - from.X) / direction.X);
        if (direction.X < 0) distance = Math.Min(distance, (Field.Left - radius - 24 - from.X) / direction.X);
        if (direction.Y > 0) distance = Math.Min(distance, (Field.Bottom + radius + 24 - from.Y) / direction.Y);
        if (direction.Y < 0) distance = Math.Min(distance, (Field.Top - radius - 24 - from.Y) / direction.Y);
        return from + direction * distance;
    }

    private void EasyMemoryPathHazards(Vector2[] points, float radius, float progress, bool live)
    {
        float remaining = 0;
        for (int i = 1; i < points.Length; i++) remaining += points[i - 1].DistanceTo(points[i]);
        remaining *= progress;
        for (int i = 1; i < points.Length && remaining > 0; i++)
        {
            float length = points[i - 1].DistanceTo(points[i]);
            Line(points[i - 1], points[i - 1].MoveToward(points[i], Math.Min(length, remaining)), radius, live, progress);
            remaining -= length;
        }
    }

    private void DrawEasyMemory()
    {
        var warning = new Color("ff7d88", .58f);
        var live = new Color("fff0d9");
        var seen = new HashSet<string>();
        if (Variant == 4 && _easyMemoryCues.Count == 0)
        {
            Vector2 pulled = _anchor + _easyConnectionPull;
            DrawLine(_anchor, pulled, new Color(MemoryInk, .4f), .8f, true);
            DrawEasyMemoryPort(_anchor, new Color(MemoryInk, .5f));
            DrawMemoryEnvelope(pulled, new Vector2(8, 5), new Color("dcecf1"));
        }
        foreach (var cue in _easyMemoryCues.OrderBy(c => c.Fired ? 1 : 0))
        {
            if (cue.Mode == 7 && Time < cue.StartsAt && cue.InitialStartsAt is double preview
                && cue.InitialEndsAt is double previewEnd && Time >= preview && Time < previewEnd)
            {
                if (GameManager.ShowAttackTelegraphs)
                {
                    float progress = Mathf.Clamp((float)((Time - preview - .4) / .8), 0, 1);
                    if (progress > 0) DrawEasyMemoryRoute(cue.Points, progress, new Color(warning, .24f), cue.Radius);
                }
                continue;
            }
            if (Time < cue.StartsAt || EasyMemorySegmentEnded(cue)) continue;
            if (cue.Mode is 4 or 7 ? !cue.Energized : Time < cue.FiresAt)
            {
                if (!GameManager.ShowAttackTelegraphs || !seen.Add(cue.Group)) continue;
                float progress = EasyMemoryProgress(cue);
                if (cue.Cancelled)
                {
                    foreach (var bounds in cue.Bounds) DrawEasyMemoryBox(bounds, new Color("72dfff", .65f), false);
                    DrawMemoryEnvelope(cue.Card, new Vector2(10, 7), new Color("aee6ed"));
                    DrawLine(cue.Card + new Vector2(-5, 0), cue.Card + new Vector2(-1, 4), new Color("aee6ed"), 1, true);
                    DrawLine(cue.Card + new Vector2(-1, 4), cue.Card + new Vector2(7, -5), new Color("aee6ed"), 1, true);
                    continue;
                }
                if (cue.Mode is 0 or 1 or 3 or 5)
                {
                    foreach (var bounds in cue.Bounds)
                        if (progress > 0) DrawEasyMemoryBox(new Rect2(bounds.Position,
                            new Vector2(bounds.Size.X * progress, bounds.Size.Y)), warning, false);
                    Vector2 origin = cue.Mode == 0
                        ? new Vector2(Mathf.Clamp(cue.Points[0].X, Field.Left + 3, Field.Right - 3), cue.Points[0].Y)
                        : cue.Mode == 1 ? new Vector2(cue.Card.X, cue.Bounds[0].Position.Y + 3) : cue.Bounds[0].Position + Vector2.One * 3;
                    DrawEasyMemoryOrigin(origin);
                    if (cue.Mode == 0 && progress > 0)
                        DrawEasyMemoryRoute(cue.Points, progress, warning);
                    if (cue.Mode == 1) DrawMemoryEnvelope(cue.Card, new Vector2(10, 7), warning);
                    if (cue.Mode == 3) DrawMemoryEnvelope(cue.Card, new Vector2(7, 5), new Color(MemoryInk, .72f));
                }
                else if (cue.Mode == 2)
                {
                    if (progress > 0)
                        DrawEasyMemoryBox(new Rect2(Field.Rect.Position,
                            new Vector2(Field.Width * progress, Field.Height)), new Color(warning, .32f), false);
                    DrawEasyMemoryOrigin(Field.Rect.Position + Vector2.One * 3);
                    if (progress > 0) DrawEasyMemoryRoute(cue.Points, progress, new Color("d7e8ec", .38f), directionMarks: false);
                    foreach (var at in cue.Points)
                        DrawEasyMemoryBox(new Rect2(at - cue.Size * .5f, cue.Size), new Color("d7e8ec", .25f * progress), false);
                }
                else
                {
                    DrawEasyMemoryOrigin(cue.Points[0].Clamp(Field.Rect.Position + Vector2.One * 3, Field.Rect.End - Vector2.One * 3));
                    if (progress > 0) DrawEasyMemoryRoute(cue.Points, progress, warning, cue.Radius);
                    if (cue.Mode == 4 && cue.Predecessor >= 0)
                    {
                        DrawEasyMemoryPort(cue.Points[0], new Color("dfc4df", .72f));
                        DrawMemoryEnvelope(cue.Points[0], new Vector2(7, 5), new Color(MemoryInk, .45f));
                    }
                    if (cue.Mode == 4 && cue.Successor >= 0)
                        DrawEasyMemoryPort(_easyMemoryCues[cue.Successor].Points[0], warning);
                }
                continue;
            }
            if (cue.Cancelled) continue;
            float age = (float)(Time - cue.FiresAt);
            if (cue.Mode == 0)
            {
                var bounds = EasyBurdenPageBounds(cue, age);
                DrawEasyMemoryBox(bounds, live, true);
            }
            else if (cue.Mode is 1 or 3 or 5)
            {
                foreach (var bounds in cue.Bounds) DrawEasyMemoryBox(bounds, live, true);
                if (cue.Mode != 5) DrawMemoryEnvelope(cue.Card, new Vector2(8, 5), new Color(MemoryInk));
            }
            else if (cue.Mode == 2)
            {
                var room = EasyRoomProtectedBounds(cue, Time - cue.FiresAt);
                foreach (var bounds in EasyRoomPressure(room)) DrawEasyMemoryBox(bounds, live, true);
                DrawRect(room, new Color("fff0d9"), false, 1.2f, true);
                foreach (var door in EasyAirlockDoors(cue))
                    if (Time >= door.OpensAt - .4 && Time <= door.ClosesAt + .4)
                    {
                        DrawEasyMemoryPort(door.Entry.GetCenter(), new Color("efb8d7", .55f));
                        DrawEasyMemoryPort(door.Exit.GetCenter(), new Color("fff0d9"));
                    }
            }
            else
            {
                Vector2 at = cue.Position;
                DrawEasyMemorySweep(cue.PriorPosition, at, cue.Radius, new Color("6d4b74", .9f));
                if (cue.Transferred && cue.ReleaseCue >= 0)
                    DrawRect(new Rect2(at - Vector2.One * (cue.Radius - 2), Vector2.One * (cue.Radius - 2) * 2),
                        new Color("fff0d9"), false, .8f, true);
                if (cue.TransferredAt is double transferred && Time - transferred < .2)
                    DrawEasyMemoryPort(at, new Color("fff7e8"));
            }
        }
    }

    private void DrawEasyMemoryBox(Rect2 bounds, Color ink, bool live)
    {
        bounds = bounds.Intersection(Field.Rect);
        if (bounds.Size.X <= 0 || bounds.Size.Y <= 0) return;
        if (live)
        {
            DrawRect(bounds, new Color(Variant == 1 ? "31485c" : Variant == 2 ? "51413b" : "52384d", .78f));
            DrawMemoryIllustrations(bounds);
            DrawRect(bounds, new Color("3e183d"), false, 2.5f, true);
            DrawRect(bounds, ink, false, 1.1f, true);
        }
        else
        {
            DrawRect(bounds, new Color(ink, .025f));
            DrawDashedLine(bounds.Position, new Vector2(bounds.End.X, bounds.Position.Y), ink, .7f, 3, true);
            DrawDashedLine(new Vector2(bounds.End.X, bounds.Position.Y), bounds.End, ink, .7f, 3, true);
            DrawDashedLine(bounds.End, new Vector2(bounds.Position.X, bounds.End.Y), ink, .7f, 3, true);
            DrawDashedLine(new Vector2(bounds.Position.X, bounds.End.Y), bounds.Position, ink, .7f, 3, true);
        }
    }

    private void DrawMemoryIllustrations(Rect2 bounds)
    {
        bounds = bounds.Intersection(Field.Rect).Grow(-2);
        if (bounds.Size.X < 5 || bounds.Size.Y < 5) return;
        if (Variant == 0)
        {
            Vector2 textureSize = _memoryIllustration.GetSize();
            float scale = Math.Max(bounds.Size.X / textureSize.X, bounds.Size.Y / textureSize.Y);
            Vector2 crop = bounds.Size / scale;
            DrawTextureRectRegion(_memoryIllustration, bounds, new Rect2((textureSize - crop) * .5f, crop));
            return;
        }
        int columns = Math.Max(1, Mathf.CeilToInt(bounds.Size.X / 64));
        int rows = Math.Max(1, Mathf.CeilToInt(bounds.Size.Y / 52));
        Vector2 cell = bounds.Size / new Vector2(columns, rows);
        Vector2 size = _memoryIllustration.GetSize();
        size *= Math.Min(30 / Math.Max(size.X, size.Y), Math.Min((cell.X - 2) / size.X, (cell.Y - 2) / size.Y));
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            Vector2 center = bounds.Position + new Vector2(column + .5f, row + .5f) * cell;
            DrawTextureRect(_memoryIllustration, new Rect2(center - size * .5f, size), false);
        }
    }

    private void DrawEasyMemoryOrigin(Vector2 at)
    {
        Vector2[] shape = { at + new Vector2(0, -3.8f), at + new Vector2(3.8f, 3.2f), at + new Vector2(-3.8f, 3.2f), at + new Vector2(0, -3.8f) };
        DrawPolyline(shape, new Color("ff8991"), 1, true);
        DrawLine(at + Vector2.Up * 1.5f, at, new Color("fff0e8"), .9f, true);
        DrawCircle(at + Vector2.Down * 1.8f, .55f, new Color("fff0e8"));
    }

    private void DrawEasyMemoryDisc(Vector2 center, float radius, Color fill)
    {
        for (float y = Math.Max(Field.Top, Mathf.Floor(center.Y - radius)); y < Math.Min(Field.Bottom, center.Y + radius); y++)
        {
            float dy = y + .5f - center.Y;
            if (Math.Abs(dy) > radius) continue;
            float chord = Mathf.Sqrt(radius * radius - dy * dy);
            float left = Math.Max(Field.Left, center.X - chord), right = Math.Min(Field.Right, center.X + chord);
            if (right > left) DrawRect(new Rect2(left, y, right - left, Math.Min(1, Field.Bottom - y)), fill);
        }
        for (int i = 0; i < 48; i++)
        {
            Vector2 a = center + Vector2.FromAngle(i * Mathf.Tau / 48) * radius;
            Vector2 b = center + Vector2.FromAngle((i + 1) * Mathf.Tau / 48) * radius;
            if (EasyMemoryVisibleSegment(a, b) is { } visible)
                DrawLine(visible.A, visible.B, new Color("ffe8d0"), 1.1f, true);
        }
        DrawMemoryIllustrations(new Rect2(center - Vector2.One * radius * .65f, Vector2.One * radius * 1.3f));
    }

    private void DrawEasyMemorySweep(Vector2 from, Vector2 to, float radius, Color fill)
    {
        if (from == to) { DrawEasyMemoryDisc(to, radius, fill); return; }
        Vector2 side = from.DirectionTo(to).Orthogonal() * radius;
        Vector2[] body = { from + side, to + side, to - side, from - side };
        for (float y = Math.Max(Field.Top, Mathf.Floor(Math.Min(from.Y, to.Y) - radius));
            y < Math.Min(Field.Bottom, Math.Max(from.Y, to.Y) + radius); y++)
        {
            float scan = y + .5f, left = float.PositiveInfinity, right = float.NegativeInfinity;
            float dy = scan - from.Y;
            if (Math.Abs(dy) <= radius)
            {
                float chord = Mathf.Sqrt(radius * radius - dy * dy);
                left = from.X - chord; right = from.X + chord;
            }
            for (int edge = 0; edge < body.Length; edge++)
            {
                Vector2 a = body[edge], b = body[(edge + 1) % body.Length];
                if ((a.Y > scan) == (b.Y > scan)) continue;
                float x = a.X + (b.X - a.X) * (scan - a.Y) / (b.Y - a.Y);
                left = Math.Min(left, x); right = Math.Max(right, x);
            }
            void Span(float a, float b)
            {
                a = Math.Max(Field.Left, a); b = Math.Min(Field.Right, b);
                if (b > a) DrawRect(new Rect2(a, y, b - a, Math.Min(1, Field.Bottom - y)), fill);
            }
            dy = scan - to.Y;
            if (Math.Abs(dy) <= radius)
            {
                float chord = Mathf.Sqrt(radius * radius - dy * dy);
                Span(left, Math.Min(right, to.X - chord));
                Span(Math.Max(left, to.X + chord), right);
            }
            else Span(left, right);
        }
        DrawEasyMemoryDisc(to, radius, fill);
        var edgeInk = new Color("ffe8d0");
        void Edge(Vector2 a, Vector2 b)
        {
            if (EasyMemoryVisibleSegment(a, b) is { } visible) DrawLine(visible.A, visible.B, edgeInk, 1.1f, true);
        }
        Edge(from + side, to + side);
        Edge(from - side, to - side);
        float angle = (to - from).Angle() + Mathf.Pi / 2;
        for (int step = 0; step < 24; step++)
            Edge(from + Vector2.FromAngle(angle + Mathf.Pi * step / 24) * radius,
                from + Vector2.FromAngle(angle + Mathf.Pi * (step + 1) / 24) * radius);
    }

    private void DrawEasyMemoryPort(Vector2 at, Color ink)
    {
        DrawRect(new Rect2(at - Vector2.One * 4, Vector2.One * 8), new Color("1d1933", .85f), true);
        DrawRect(new Rect2(at - Vector2.One * 4, Vector2.One * 8), ink, false, .9f, true);
        DrawLine(at + new Vector2(-2, -2), at + new Vector2(2, 2), ink, .8f, true);
        DrawLine(at + new Vector2(-2, 2), at + new Vector2(2, -2), ink, .8f, true);
    }

    private void DrawEasyMemoryRoute(Vector2[] points, float progress, Color ink, float radius = 0, bool directionMarks = true)
    {
        float remaining = 0;
        for (int i = 1; i < points.Length; i++) remaining += points[i - 1].DistanceTo(points[i]);
        remaining *= progress;
        for (int i = 1; i < points.Length && remaining > 0; i++)
        {
            float length = points[i - 1].DistanceTo(points[i]);
            Vector2 end = points[i - 1].MoveToward(points[i], Math.Min(remaining, length));
            Vector2 direction = points[i - 1].DirectionTo(end), side = new(-direction.Y, direction.X);
            void Segment(Vector2 a, Vector2 b)
            {
                if (EasyMemoryVisibleSegment(a, b) is { } visible)
                    DrawDashedLine(visible.A, visible.B, ink, .7f, 3, true);
            }
            Segment(points[i - 1], end);
            if (radius > 0)
            {
                DrawWarningEdgeCaps(points[i - 1], end, radius, ink);
                Segment(points[i - 1] + side * radius, end + side * radius);
                Segment(points[i - 1] - side * radius, end - side * radius);
                if (Field.Rect.Grow(-radius).HasPoint(end)) DrawArc(end, radius, 0, Mathf.Tau, 28, new Color(ink, .3f), .5f, true);
            }
            for (float distance = 18; directionMarks && distance < Math.Min(remaining, length); distance += 58)
            {
                Vector2 at = points[i - 1] + direction * distance;
                if (!Field.Rect.Grow(-4).HasPoint(at)) continue;
                DrawLine(at - direction * 2.5f + side * 2.2f, at, ink, .8f, true);
                DrawLine(at - direction * 2.5f - side * 2.2f, at, ink, .8f, true);
            }
            remaining -= length;
        }
    }

    private static (Vector2 A, Vector2 B)? EasyMemoryVisibleSegment(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float enter = 0, exit = 1;
        bool Trim(float direction, float distance)
        {
            if (direction == 0) return distance >= 0;
            float at = distance / direction;
            if (direction < 0) enter = Math.Max(enter, at);
            else exit = Math.Min(exit, at);
            return enter <= exit;
        }
        if (!Trim(-delta.X, from.X - Field.Left) || !Trim(delta.X, Field.Right - from.X)
            || !Trim(-delta.Y, from.Y - Field.Top) || !Trim(delta.Y, Field.Bottom - from.Y)) return null;
        return (from + delta * enter, from + delta * exit);
    }

    protected override void DrawTechnique()
    {
        if (Kind == BossTechniqueKind.Area)
        {
            DrawEasyMemory();
            return;
        }
        if (!GameManager.ShowAttackTelegraphs) return;
        DrawMemoryCues();
        if (_id == 10)
            for (int side = 0; side < 2; side++)
            {
                Vector2 center = new(Field.Left + 72 + side * 174, 108);
                DrawRect(new Rect2(center - new Vector2(43, 52), new Vector2(86, 104)),
                    new Color(MemoryInk, .55f), false, .6f);
            }
        if (_id == 11)
        {
            for (int i = 0; i < 35; i++)
            {
                var rect = new Rect2(Field.Left + i % 7 * Field.Width / 7, i / 7 * Field.Height / 5,
                    Field.Width / 7, Field.Height / 5);
                DrawRect(rect, new Color(MemoryInk, 0.18f), false, 0.5f);
                if (_visits[i] == 1) DrawCircle(rect.GetCenter(), 2, new Color("aee6ed"));
            }
        }
        if (_id == 18)
            DrawArc(Boss.GlobalPosition, 65, 0, Mathf.Tau, 64, new Color("aee6ed"), 0.8f, true);
        if (_id == 17)
        {
            DrawLine(Boss.GlobalPosition - Vector2.Right * 34, Boss.GlobalPosition - Vector2.Right * 65, MemoryInk, 1.2f, true);
            DrawLine(Boss.GlobalPosition + Vector2.Right * 34, Boss.GlobalPosition + Vector2.Right * 65, MemoryInk, 1.2f, true);
        }
    }

    private void DrawMemoryCues()
    {
        var ink = new Color(MemoryInk, .5f);
        var quiet = new Color(MemoryInk, .23f);
        var received = new Color("aee6ed", .6f);
        float t = (float)(Time - _warning);
        switch (_id)
        {
            case 0:
            {
                int turn = Math.Max(0, (int)(t / 1.45));
                int empty = new[] { 4, 3, 0, 1, 2, 5, 8, 7, 6 }[turn % 9];
                int next = new[] { 3, 0, 1, 2, 5, 8, 7, 6, 3 }[turn % 9];
                Vector2 origin = _center - new Vector2(57, 57);
                Vector2 vacancy = origin + new Vector2(empty % 3 * 38 + 12, empty / 3 * 38 + 12);
                Vector2 donor = origin + new Vector2(next % 3 * 38 + 12, next / 3 * 38 + 12);
                DrawMemoryLabel(vacancy, "空", received);
                DrawDashedLine(donor, vacancy, quiet, .6f, 3, true);
                for (int i = 0; i < 9; i++)
                {
                    if (i == empty) continue;
                    Vector2 at = origin + new Vector2(i % 3 * 38 + 12, i / 3 * 38 + 12);
                    float move = t < 0 ? 0 : Mathf.Clamp((float)(t % 1.45 - .65) / .65f, 0, 1);
                    if (i == next) at = at.Lerp(vacancy, move);
                    DrawMemoryEnvelope(at, new Vector2(12, 9), quiet);
                }
                break;
            }
            case 1:
                for (int i = 0; i < 3 + Tier; i++)
                {
                    float age = t - i * .85f;
                    if (age < 0 || age > 5.6) continue;
                    float expansion = Mathf.SmoothStep(0, 1, Mathf.Clamp((age - 1) / 1.2f, 0, 1));
                    Vector2 origin = new(_axis.X < 0 ? Field.Right - 20 : Field.Left + 20, 30 + i % 4 * 48);
                    Vector2 at = origin + _axis * age * (40 + Tier * 4);
                    if (!Field.Rect.HasPoint(at)) continue;
                    float height = Mathf.Lerp(6, 52 + Tier * 5, expansion);
                    for (int fold = -1; fold <= 1; fold++)
                        DrawLine(at + new Vector2(-3, fold * height / 3), at + new Vector2(3, fold * height / 3 + 2), quiet, .5f, true);
                }
                break;
            case 2:
            {
                int count = 4 + Tier;
                for (int i = 0; i < count; i++)
                {
                    int weight = (i * 3 + 1) % count;
                    if (Time >= _warning + (count - 1 - weight) * .65) continue;
                    Vector2 at = new(Field.Left + 26 + i * (Field.Width - 52) / (count - 1), 22 + i % 2 * 20);
                    DrawLine(at + new Vector2(-10, 11), at + new Vector2(10, 11), ink, .6f, true);
                    for (int mark = 0; mark <= weight; mark++)
                        DrawLine(at + new Vector2(-9 + mark * 3, 13), at + new Vector2(-9 + mark * 3, 17), ink, .6f, true);
                }
                break;
            }
            case 3:
                for (int i = 0; i < 2 + Tier; i++)
                {
                    float y = 35 + i * 146f / (1 + Tier);
                    DrawPort(new Vector2(Field.Left + 4, y), Vector2.Right, received);
                    DrawPort(new Vector2(Field.Right - 4, y), Vector2.Left, ink);
                    DrawMemoryLabel(new Vector2(Field.Left + 16, y + 17), (i + 1).ToString(), received);
                    DrawMemoryLabel(new Vector2(Field.Right - 16, y + 17), (i + 1).ToString(), ink);
                }
                break;
            case 4:
            {
                float load = Mathf.Clamp(t / 3, 0, 1);
                Vector2 vessel = new(Field.CenterX, 70);
                for (int side = 0; side < 3; side++)
                {
                    if (t > 3.4 + side * .7) continue;
                    float weight = side == 1 ? 1 : .58f;
                    Vector2 at = vessel + new Vector2((side - 1) * 48, side == 1 ? 50 : 8);
                    float radius = 7 + load * 27 * weight;
                    DrawMemoryEnvelope(at, new Vector2(10 + load * 7, 8 + load * 4), quiet);
                    for (int mark = 0; mark < (side == 1 ? 5 : 3); mark++)
                        DrawLine(at + new Vector2(radius + 5, -7 + mark * 4), at + new Vector2(radius + 9, -7 + mark * 4), ink, .6f, true);
                }
                break;
            }
            case 5:
                for (int i = 0; i < 3 + Tier; i++)
                {
                    Vector2 at = new(Field.Left + 38 + i % 3 * 94, 63 + i / 3 * 88);
                    if (_read[i])
                    {
                        DrawLine(at + new Vector2(-8, 8), at + new Vector2(-3, 13), received, .9f, true);
                        DrawLine(at + new Vector2(-3, 13), at + new Vector2(9, 1), received, .9f, true);
                    }
                    else if (t < 5.5) DrawMemoryEnvelope(at + Vector2.Down * 7, new Vector2(20, 12), quiet);
                }
                break;
            case 6:
            {
                for (int gate = 0; gate < 2; gate++)
                {
                    bool closed = gate == 0 ? Time >= _airlockEntryAt : Time < _airlockOpenAt || Time >= _airlockExitAt;
                    Vector2 gateAt = _airlockOrigin + _airlockAxis * (gate == 0 ? 24 : 60);
                    Vector2 side = _airlockAxis.Orthogonal();
                    for (int offset = -1; offset <= 1; offset += 2)
                    {
                        Vector2 port = gateAt + side * offset * 16;
                        DrawPort(port, _airlockAxis, closed ? ink : received);
                        DrawLine(port - _airlockAxis * 5, port + _airlockAxis * (closed ? 5 : -1), closed ? ink : received, .8f, true);
                    }
                    if (!closed) DrawDashedLine(gateAt - _airlockAxis * 9, gateAt + _airlockAxis * 9, received, .7f, 3, true);
                }
                Vector2 chamber = _airlockOrigin + _airlockAxis * 42;
                float checking = (float)Math.Clamp((Time - _airlockEntryAt) / (_airlockOpenAt - _airlockEntryAt), 0, 1);
                for (int mark = 0; mark < 5; mark++)
                    DrawCircle(chamber + _airlockAxis.Orthogonal() * (mark - 2) * 5, 1.4f, checking >= (mark + 1) / 5f ? received : quiet);
                break;
            }
            case 7:
                for (int i = 0; i < 35; i++)
                {
                    Vector2 at = new(Field.Left + 39 + i % 7 * 38, 29 + i / 7 * 38);
                    if (_cells[i] && !_nextCells[i]) DrawMemoryLabel(at, "−", received);
                    else if (_cells[i]) DrawMemoryEnvelope(at, new Vector2(10, 7), quiet);
                    else if (_nextCells[i])
                        DrawRect(new Rect2(at - Vector2.One * 4, Vector2.One * 8), new Color(received, .25f + .2f * Mathf.Sin((float)Time * 5)), false, .6f);
                }
                break;
            case 8:
            {
                int branch = Math.Max(0, (int)(t / 2.4));
                if (branch >= 3 + Tier) break;
                Vector2 fork = new(Field.Left + 70 + branch % 3 * 70, 108);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector2 end = fork + new Vector2(42, sign * 64);
                    bool linked = (sign < 0) == (branch % 2 == 0);
                    DrawPort(end, (fork - end).Normalized(), linked ? received : quiet);
                    if (linked)
                    {
                        float pulse = Mathf.PosMod((float)Time * .7f, 1);
                        DrawCircle(fork.Lerp(end, pulse), 1.2f, received);
                    }
                }
                break;
            }
            case 9:
                for (int i = 1; i < _springX.Length; i++)
                {
                    float left = _springX[i - 1], right = _springX[i];
                    if (left == 0 || right == 0) continue;
                    Vector2 a = new(left + 4, _center.Y + 9), b = new(right - 4, _center.Y + 9);
                    for (int zig = 0; zig < 4; zig++)
                    {
                        Vector2 p0 = a.Lerp(b, zig / 4f) + Vector2.Down * (zig % 2 == 0 ? -2 : 2);
                        Vector2 p1 = a.Lerp(b, (zig + 1) / 4f) + Vector2.Down * (zig % 2 == 0 ? 2 : -2);
                        DrawLine(p0, p1, quiet, .5f, true);
                    }
                }
                break;
            case 10:
                for (int side = 0; side < 2; side++)
                {
                    Vector2 center = new(Field.Left + 72 + side * 174, 108);
                    DrawMemoryEnvelope(center + Vector2.Down * 61, new Vector2(14, 10), quiet);
                    if (_exchangePending)
                        foreach (var memory in _memories.Where(m => m.Live && m.Index / (3 + Tier) == side))
                            DrawRect(new Rect2(memory.Origin - Vector2.One * 3, Vector2.One * 6), quiet, false, .55f);
                }
                break;
            case 11:
                for (int i = 0; i < 35; i++)
                {
                    if (_visits[i] == 0) continue;
                    Vector2 at = new(Field.Left + (i % 7 + .5f) * Field.Width / 7, (i / 7 + .5f) * Field.Height / 5);
                    int stamps = Math.Min(_visits[i], 2);
                    for (int stamp = 0; stamp < stamps; stamp++)
                        DrawRect(new Rect2(at + new Vector2(-7 + stamp * 8, 6), new Vector2(4, 7)), stamps == 1 ? received : quiet, false, .6f);
                }
                break;
            case 12:
                foreach (var memory in _memories.Where(m => m.Live))
                {
                    Vector2 at = memory.Bullet.GlobalPosition;
                    bool moving = memory.Bullet.Velocity.LengthSquared() > 1;
                    if (moving)
                        for (int trail = 1; trail <= 3; trail++)
                            DrawLine(at + new Vector2(-8 - trail * 4, -3), at + new Vector2(-10 - trail * 4, 3), new Color(MemoryInk, .3f / trail), .6f, true);
                    else DrawPort(at + Vector2.Down * 12, Vector2.Up, memory.Bullet.CollisionLayer == 0 ? quiet : received);
                }
                break;
            case 13:
            {
                Vector2 at = new(Field.CenterX, 34);
                DrawMemoryEnvelope(at, new Vector2(17, 11), quiet);
                if (_waitingReply)
                {
                    float response = Mathf.Clamp((float)(1 - (_replyAt - Time) / 1.3), 0, 1);
                    for (int step = 0; step < 5; step++)
                        DrawRect(new Rect2(at.X - 17 + step * 7, at.Y + 10, 4, 2), step / 5f <= response ? received : quiet);
                }
                else DrawMemoryLabel(at + Vector2.Down * 18, "受信待ち", ink);
                break;
            }
            case 14:
            {
                foreach (var connection in _connections)
                {
                    if (connection.Spent) continue;
                    Vector2 direction = connection.End.DirectionTo(connection.Root);
                    DrawPort(connection.Root, -direction, received);
                    if (!connection.Released)
                    {
                        DrawDashedLine(connection.Root, connection.End, received,
                            .45f + Math.Min(connection.Extension, 80) / 160, 4, true);
                        DrawMemoryEnvelope(connection.End, new Vector2(10, 7), received);
                        int tension = Math.Clamp((int)Math.Ceiling(connection.Extension / 12), 1, 7);
                        for (int mark = 0; mark < tension; mark++)
                            DrawLine(connection.Root + new Vector2(mark * 3 - 9, 7),
                                connection.Root + new Vector2(mark * 3 - 9, 11), received, .7f, true);
                        if (_connectionsLocked)
                        {
                            Vector2 arrow = connection.End + direction * Math.Min(24, connection.Range);
                            DrawLine(arrow, arrow - direction.Rotated(.55f) * 5, ink, .8f, true);
                            DrawLine(arrow, arrow - direction.Rotated(-.55f) * 5, ink, .8f, true);
                        }
                    }
                    else if (connection.Memory is { Live: true } memory)
                    {
                        Vector2 at = memory.Bullet.GlobalPosition;
                        float speed = Math.Max(0, connection.Speed - (float)(Time - connection.ReleaseAt) * ConnectionFriction);
                        DrawLine(at - direction * 5, at - direction * (8 + speed * .07f), ink, .8f, true);
                        DrawLine(connection.Root - direction.Orthogonal() * 4, connection.Root - direction * 4, quiet, .7f, true);
                        DrawLine(connection.Root + direction.Orthogonal() * 4, connection.Root + direction * 4, quiet, .7f, true);
                    }
                }
                break;
            }
            case 15:
            {
                Vector2 pivot = new(Field.CenterX, 18);
                float angle = Mathf.Sin(Math.Max(0, t) * 1.65f) * .85f;
                Vector2 weight = pivot + Vector2.Down.Rotated(angle) * 132;
                DrawMemoryEnvelope(weight, new Vector2(18 + Tier * 2, 13 + Tier), quiet);
                DrawRect(new Rect2(pivot - new Vector2(8, 3), new Vector2(16, 6)), ink, false, .7f);
                for (int knot = 1; knot < 5; knot++)
                    DrawCircle(pivot.Lerp(weight, knot / 5f), 1, quiet);
                break;
            }
            case 16:
            {
                int safe = Mathf.Clamp((int)(_anchor.Y / (Field.Bottom / 4)), 0, 3);
                for (int row = 0; row < 4; row++)
                {
                    float y = 27 + row * 54;
                    if (row == safe && Time > _warning - .7)
                    {
                        DrawDashedLine(new Vector2(Field.Left + 58, y), new Vector2(Field.Right - 38, y), received, .6f, 5, true);
                        DrawMemoryLabel(new Vector2(Field.Right - 22, y + 3), "取消済", received);
                    }
                    else if (t < 1.4)
                        for (int word = 0; word < 4; word++)
                            DrawLine(new Vector2(Field.Left + 57 + word * 48, y - 5), new Vector2(Field.Left + 80 + word * 48, y - 5), quiet, .6f, true);
                }
                break;
            }
            case 17:
                if (t >= 0)
                {
                    float beat = t % 3;
                    float force = beat < 1.2f ? Mathf.Sin(beat / 1.2f * Mathf.Pi) : 0;
                    for (int side = -1; side <= 1; side += 2)
                        for (int feather = 0; feather < 3; feather++)
                        {
                            Vector2 a = Boss.GlobalPosition + new Vector2(side * (42 + feather * 8), -19 + feather * 12);
                            Vector2 b = a + new Vector2(side * (10 + force * 18), force * 10);
                            DrawLine(a, b, new Color(MemoryInk, .15f + force * .28f), .65f, true);
                        }
                }
                break;
            case 18:
            {
                Vector2 center = Boss.GlobalPosition;
                float angle = center.DirectionTo(Player.GlobalPosition).Angle();
                for (int side = -1; side <= 1; side += 2)
                {
                    float a = angle + side * .26f;
                    Vector2 at = center + Vector2.FromAngle(a) * 65;
                    DrawLine(at - Vector2.FromAngle(a) * 3, at + Vector2.FromAngle(a) * 3, received, .8f, true);
                }
                break;
            }
        }
    }

    private void DrawMemoryEnvelope(Vector2 at, Vector2 size, Color ink)
    {
        var rect = new Rect2(at - size * .5f, size);
        DrawRect(rect, ink, false, .55f);
        DrawLine(rect.Position, at + Vector2.Down * size.Y * .1f, ink, .55f, true);
        DrawLine(new Vector2(rect.End.X, rect.Position.Y), at + Vector2.Down * size.Y * .1f, ink, .55f, true);
    }

    private void DrawPort(Vector2 at, Vector2 direction, Color ink)
    {
        Vector2 normal = direction.Orthogonal();
        DrawLine(at - normal * 4, at + normal * 4, ink, .7f, true);
        DrawLine(at - normal * 4, at - normal * 4 + direction * 5, ink, .7f, true);
        DrawLine(at + normal * 4, at + normal * 4 + direction * 5, ink, .7f, true);
    }

    private void DrawMemoryLabel(Vector2 at, string text, Color ink)
    {
        float width = UiKit.ZenBold.GetStringSize(text, fontSize: 6).X;
        at.X = Mathf.Clamp(at.X - width * .5f, Field.Left + 3, Field.Right - width - 3);
        at.Y = Mathf.Clamp(at.Y, 8, Field.Bottom - 4);
        DrawString(UiKit.ZenBold, at, text, HorizontalAlignment.Left, -1, 6, ink);
    }
}
