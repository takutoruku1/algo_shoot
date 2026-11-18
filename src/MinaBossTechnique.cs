using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

public partial class MinaBossTechnique : BossCharacterTechnique
{
    internal override bool UsesArenaBody => Kind == BossTechniqueKind.Extra && Variant == 1
        || Kind == BossTechniqueKind.Area;

    private sealed class Memory
    {
        public Bullet Bullet = null!;
        public ulong Activation;
        public Vector2 Origin, Direction;
        public double Born;
        public int Index;

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
    internal readonly record struct CacheSlide(int From, int To, Vector2 SideA, Vector2 SideB);
    private readonly List<CacheSlide> _cacheSlides = new();
    private const double CachePeriod = 5.05;
    internal static Rect2 CacheCell(int index) => new(Field.Left + index % 3 * Field.Width / 3,
        Field.Top + index / 3 * Field.Height / 3, Field.Width / 3, Field.Height / 3);
    internal Vector2 CacheEntry => CacheCell(_cacheSlides[0].From).GetCenter();
    internal (CacheSlide Slide, double Age) VisibleCacheSlide
    {
        get
        {
            int step = Math.Clamp((int)(Math.Max(0, Time - _warning) / CachePeriod), 0, _cacheSlides.Count - 1);
            return (_cacheSlides[step], Time - _warning - step * CachePeriod);
        }
    }
    private bool _connectionsLocked;
    private double _connectionWarning, _connectionLastRelease;
    private float _connectionRadius;
    private double ConnectionPullTime => Tier < 2 ? 1.7 : 1.5;
    private double ConnectionInterval => Tier switch { 0 => .72, 1 => .60, 2 => .44, _ => .30 };
    private const float ConnectionFriction = 12;
    private readonly bool[] _read = new bool[32];
    private Vector2 _anchor, _center, _axis, _initialBoss;
    private Vector2 WingCenter => new(Field.CenterX, Boss.TechniqueCenter.Y);
    private Vector2 _airlockOrigin, _airlockAxis;
    private double _airlockEntryAt, _airlockOpenAt, _airlockExitAt;
    private double _warning, _next;
    private int _id, _beat;
    private readonly bool[] _cells = new bool[35], _nextCells = new bool[35];
    private readonly int[] _visits = new int[35];
    private readonly double[] _markedAt = new double[35];
    private readonly double[] _firstStepAt = new double[35];
    private double VisitDuration => 15 + Tier;
    private int _lastCell = -1, _generation = -1;
    private double _visitEnteredAt;
    internal readonly record struct VisitEcho(Vector2 From, Vector2 To, Rect2 Bounds, double StartsAt, double FiresAt, double EndsAt);
    private readonly List<VisitEcho> _visitEchoes = new();
    internal IEnumerable<VisitEcho> VisibleVisitEchoes => _visitEchoes.Where(echo => Time < echo.EndsAt);
    private double _visitEchoReadyAt;
    private readonly HashSet<(ulong, ulong)> _received = new();

    internal sealed class StarReply
    {
        internal Vector2 Target;
        internal Vector2[] Sources = Array.Empty<Vector2>();
        internal double StartsAt, FiresAt;
        internal bool Fired;
        internal int Pulses;
        internal float EntryTime, ExitTime;
        internal Vector2 FarEdge(Vector2 source) => new(source.X < Field.Left ? Field.Right + 12 : Field.Left - 12,
            Field.Bottom - (source.Y - Field.Top));
        internal Vector2 Position(Vector2 source, float age)
            => age <= EntryTime ? source.Lerp(Target, age / EntryTime)
                : Target.Lerp(FarEdge(source), (age - EntryTime) / ExitTime);
    }
    internal readonly List<StarReply> StarReplies = new();
    private readonly List<(StarReply Reply, Memory Star)> _answerStars = new();
    internal bool ReplyReady => Time >= _warning && Time >= _next && Time < _warning + 14 && StarReplies.Count < 3;
    internal bool ReplyFailed => Time >= _warning + 14 && StarReplies.Count < 3;
    private readonly List<(int Letter, Vector2 At, double WarnedAt)> _unansweredStars = new();
    private const double UnansweredLead = 2.7;
    private const double UnansweredHold = 10.8;
    internal float UnansweredRadius => 24 + 3 * Tier;
    internal float StarReplySpeed => 56 + Tier * 3;
    private double _exchangeAt;
    private int _exchanges;
    private bool _exchangePending;
    private const double SnapshotHold = 2.6;
    private sealed class SnapshotBand
    {
        internal int Side;
        internal float Y, Velocity;
    }
    private readonly List<SnapshotBand> _snapshotBands = new();
    internal readonly record struct BurdenFall(float Left, double StartsAt, double FiresAt);
    private readonly List<BurdenFall> _burdenFalls = new();
    internal IReadOnlyList<BurdenFall> VisibleBurdenFalls => _burdenFalls;
    internal IEnumerable<(int Side, float Y, float Velocity)> VisibleSnapshotBands => _snapshotBands.Select(band => (band.Side, band.Y, band.Velocity));
    internal double SnapshotExchangeDelay => _exchangePending ? Math.Max(0, _exchangeAt - Time) : double.PositiveInfinity;
    internal bool SnapshotDraining => _exchanges >= 2 + Tier && Time >= _next;
    internal float SnapshotThickness => 12 + Tier * 3;
    private Rect2 SnapshotBounds(int side, float y)
    {
        float retreat = SnapshotDraining ? Mathf.SmoothStep(0, 1, (float)Math.Clamp((Time - _next) / 1.2, 0, 1)) : 0;
        float width = Field.Width / 2;
        return new Rect2(Field.Left + side * width + (side == 0 ? -1 : 1) * retreat * width,
            y - SnapshotThickness / 2, width, SnapshotThickness);
    }
    private int Density => Tier == 0 ? 6 : 6 + Tier * 6;
    private float Speed => 42 + Tier * 7;
    internal float WrapSpeed => 36 + Tier * 2;
    private Color MemoryInk => Kind == BossTechniqueKind.Area ? MemoryVisual.Accent : WarningVisual.Accent;
    private BulletArt.PlayerVisual MemoryVisual => Variant switch
    {
        0 => BulletArt.EnemyVisual("res://assets/fx/mina_folded_note.svg", "ffbf8b"),
        1 => BulletArt.EnemyVisual("mina_memory", "efa6cb"),
        2 => BulletArt.EnemyVisual("mina_eraser", "b5e6ae"),
        3 => BulletArt.EnemyVisual("res://assets/fx/mina_photo.svg", "d5a2f5"),
        _ => BulletArt.EnemyVisual("mina_butterfly", "f4d389"),
    };
    private Texture2D _memoryIllustration = null!;
    private Node2D _memoryCanvas = null!;
    private readonly MemoryArtBatch _memoryArt = new();
    private Node2D _memoryWarningCanvas = null!;
    private readonly MemoryArtBatch _memoryWarnings = new();
    protected override bool UsesDangerForecast => true;
    protected override void DrawDangerForecast(Node2D canvas)
    {
        base.DrawDangerForecast(canvas);
        if (Kind == BossTechniqueKind.Area)
        {
            foreach (var cue in _easyMemoryCues)
            {
                if (cue.Cancelled || Time < cue.StartsAt || Time >= cue.FiresAt) continue;
                if (cue.Mode == 1) canvas.DrawRect(cue.Bounds[0], Colors.White);
                if (cue.Mode != 2) continue;
                float progress = EasyMemoryProgress(cue);
                if (progress <= 0) continue;
                var room = new Rect2(cue.Points[0] - cue.Size * .5f, cue.Size);
                foreach (var bounds in EasyRoomPressure(room))
                    canvas.DrawRect(new Rect2(bounds.Position, new Vector2(bounds.Size.X * progress, bounds.Size.Y)), Colors.White);
            }
        }
        else if (_id == 15 && Time < _warning)
        {
            float start = Mathf.Pi / 2 - BurdenSwing, end = Mathf.Pi / 2 + BurdenSwing;
            var sweep = new Vector2[66];
            sweep[0] = BurdenPivot;
            for (int i = 0; i <= 64; i++)
                sweep[i + 1] = BurdenPivot + Vector2.FromAngle(Mathf.Lerp(start, end, i / 64f)) * BurdenLength;
            canvas.DrawColoredPolygon(sweep, Colors.White);
            AttackTelegraph.MaskStroke(canvas, BurdenPivot, sweep[1], 3);
            AttackTelegraph.MaskStroke(canvas, BurdenPivot, sweep[^1], 3);
            canvas.DrawArc(BurdenPivot, BurdenLength, start, end, 65, Colors.White, BurdenRadius * 2);
            canvas.DrawCircle(sweep[1], BurdenRadius, Colors.White);
            canvas.DrawCircle(sweep[^1], BurdenRadius, Colors.White);
        }
    }

    protected override bool UsesCustomHazardArt => Kind == BossTechniqueKind.Area || _id == 18;
    protected override bool DirectionalWarnings => Kind == BossTechniqueKind.Edge
        || Kind == BossTechniqueKind.Crossfire && Variant == 4;
    protected override Color AttackLineInk => WarningVisual.Accent.Darkened(.12f);
    protected override bool PreserveAttackEndDisc => Kind == BossTechniqueKind.Area;

    protected override void Start()
    {
        _anchor = Player.GlobalPosition;
        _center = new Vector2(Mathf.Clamp(_anchor.X, Field.Left + 70, Field.Right - 70),
            Mathf.Clamp(_anchor.Y, 60, Field.Bottom - 60));
        _axis = _anchor.X < Field.CenterX ? Vector2.Left : Vector2.Right;
        _initialBoss = Boss.TechniqueCenter;
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
            BossTechniqueKind.Extra => Variant == 1 ? 18 : 15 + (Evolved ? 2 : 0),
            _ => throw new InvalidOperationException("Mina uses five phase signatures, not an animal or legacy finale."),
        };
        var identity = _id switch
        {
            0 => ("記憶の回廊", "mina.sliding-cache", "空き区画へ移る記憶を見て、次に空く通路を選ぶ"),
            1 => ("星屑の扇", "mina.decompressing-pages", "圧縮された頁が広がる前に、短い側を通り抜ける"),
            2 => ("未送信の花冠", "mina.weighted-pending-queue", "印の多い記憶から落ちる。落下順を見て間を渡る"),
            3 => ("四方のリフレイン", "mina.boundary-wrap", "端を抜けた記憶の、反対側の再入口を読む"),
            4 => ("消せなかった声", "mina.overloaded-vessel", "抱え込んで膨らむ側から離れ、崩れる方向を外す"),
            5 => ("未送信、再送", "mina.acknowledged-drafts", "封筒に触れて開くと、その場所の着弾が止まる"),
            6 => ("拍手の檻", "mina.protective-airlock", "手前の扉を抜けて待ち、奥の扉が開いてから進む"),
            7 => ("閉じない配信", "mina.self-propagating-records", "点滅する次の記録を読み、次の世代で消える区画へ移る"),
            8 => ("リフレイン・帰り道", "mina.return-junctions", "接続の開いた枝を選び、途切れる枝から離れる"),
            9 => ("消せない応答", "mina.delayed-spring", "端から届く振動で片側のばねが閉じる。ほどける側の節の間へ移り、押し返されたら反対側へ渡る"),
            10 => ("交差する記憶", "mina.snapshot-exchange", "記憶の位置と向きが向かいの領域へ写る。保存された予告から離れる"),
            11 => ("記憶の往復", "mina.second-visit-weight", "二度通った場所に重さが残る。まだ通っていない道を選ぶ"),
            12 => ("つなぎ直す記録", "mina.momentum-handoff", "動く記憶が止まった記憶に触れると、動きが受け渡される"),
            13 => ("未送信の星列", "mina.reply-driven-stars", "こちらの射撃に星が応答する。次の返答の線を見て撃つ間を選ぶ"),
            14 => ("龍翼・切れかけた接続", "mina.elastic-disconnection", "短く引いて、固定された反動の線の横へ。強く張った接続から切れる"),
            15 => ("抱えた言葉の重さ", "mina.suspended-burden", "振り子が端で遅くなる間に、重りの下を通る"),
            16 => ("返せなかった四行", "mina.retracted-line", "四行のうち、打消し線のついた一行は発火しない"),
            17 => ("龍翼・抱えきれないもの", "mina.wing-displacement", "翼が押す向きの変化を見て、流れの弱い側へ回る"),
            _ => ("ただいまの距離", "mina.return-distance", "予兆中に広い側へ回り込み、近づくMINAの本体と外側の記憶の間を保つ"),
        };
        Identify(identity.Item1, identity.Item2, identity.Item3);
        if (_id == 11)
            Identify(identity.Item1, identity.Item2, "足跡のインクが乾いた区画へ戻ると、その場が閉じる。まだ白い区画をつなぎ、同じ場所に居続けない");
        if (Tier == 3 && _id == 11)
            Identify(identity.Item1, identity.Item2, "通った足跡から同じ行や列へ記憶が広がる。横へ外して次の道を選び、二重の足跡へ戻らない");
        if (_id == 3)
        {
            _warning = WarnFor(170, 3.8);
            Identify(identity.Item1, identity.Item2, "右端を抜けた記憶は同じ段の左端へ戻る。列の後ろを通るか、隣の段へ移る");
        }
        if (_id == 15)
        {
            if (Tier != 3)
            {
                _hardBurdenPivot = new Vector2(Field.CenterX, Field.Top + 12);
                float reach = Math.Min(BurdenLength * .95f, Field.Width / 2 - BurdenRadius - 3);
                _hardBurdenSwing = Mathf.Asin(reach / BurdenLength);
            }
            _warning = WarnFor(290, 6.2);
            Identify(identity.Item1, identity.Item2, "大きく振れる重りをくぐる。折返しでほどけた手紙の束は、斜線の列を読んで横へ避ける");
            if (Tier == 3) Identify(identity.Item1, identity.Item2, "振り子を渡った場所が重りに写る。折返しでその場所へ落ちる束を外し、次の振れを渡る");
        }
        if (_id == 9) _warning = WarnFor(145, 3.6);
        if (_id == 0)
        {
            Identify(identity.Item1, identity.Item2, "動く記憶が細くなった側を回り込み、移動元の空き区画へ入る");
            PrepareSlidingCache();
            _warning = WarnFor(300, 6.2);
        }
        if (_id == 10)
        {
            _warning = WarnFor(140, 3.6);
            _next = _warning + 2.6;
            for (int side = 0; side < 2; side++)
            for (int band = 0; band < 3 + (Tier + 1) / 2; band++)
            {
                float rest = Field.Top + (band + .5f) * Field.Height / (3 + (Tier + 1) / 2);
                _snapshotBands.Add(new SnapshotBand { Side = side, Y = rest,
                    Velocity = ((side + band) % 2 == 0 ? 1 : -1) * (12 + 2 * Tier + band * 2) });
            }
        }
        if (_id == 17)
        {
            Identify(identity.Item1, identity.Item2, "翼が押す向きを読み、風に逆らって足場を保つ。強い側は移動が鈍る");
            _warning = WarnFor(100, 3.6);
            if (Tier == 3) Identify(identity.Item1, identity.Item2, "強い風の側へ翼が振り下ろされる。風が変わる前に中央へ寄り、畳まれる翼の側へ渡る");
        }
        if (_id == 13)
        {
            Identify(identity.Item1, identity.Item2, "射撃で三度返答し、その場所へ返る星の線を外す。返さなかった手紙は、待っていた場所に星として残る");
            _warning = 3.6;
            _next = _warning;
        }
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
            // Radial entry alone can strand the player at an edge; budget entry into an orbit as well.
            float side = _anchor.Y < Field.CenterY ? -1 : 1;
            Vector2 opening = new Vector2(Field.CenterX, Field.CenterY) + Vector2.FromAngle(side * Mathf.Pi / 3) * 76;
            _warning = WarnFor(_anchor.DistanceTo(opening) + 12, 2.4);
            _warning = Math.Max(_warning, WarnFor(Math.Abs(_anchor.DistanceTo(Boss.GlobalPosition) - ((ReturnInner + ReturnOuter) / 2)) + 10, 2.4));
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
            case 9: DelayedSpring(); break;
            case 10: LocalOverwrite(delta); break;
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

    protected override BulletArt.PlayerVisual WarningVisual => Kind == BossTechniqueKind.Area ? MemoryVisual : _id switch
    {
        0 => BulletArt.EnemyVisual("res://assets/fx/mina_memory_drawer.svg", "efa6cb"),
        3 => BulletArt.EnemyVisual("mina_butterfly", "d5a2f5"),
        10 => BulletArt.EnemyVisual("res://assets/fx/mina_snapshot_pair.svg", "f6b48e"),
        13 => BulletArt.EnemyVisual("res://assets/fx/mina_reply_star.svg", "f4d389"),
        15 => BulletArt.EnemyVisual("res://assets/fx/mina_pendulum.svg", "cdb0f4"),
        17 => BulletArt.EnemyVisual("res://assets/fx/mina_feather.svg", "b5e6ae"),
        _ => BulletArt.EnemyVisual("mina_memory", "efabd9"),
    };

    private void PrepareSlidingCache()
    {
        int[] rim = { 0, 1, 2, 5, 8, 7, 6, 3 };
        using var core = new CircleShape2D { Radius = Player.HitRadius + 3 };
        var bodies = Boss.GetChildren().OfType<CollisionShape2D>().Where(shape => Boss.BodyContactDamaging && !shape.Disabled).ToArray();
        var routes = new List<CacheSlide>();
        foreach (int from in rim)
        foreach (int to in rim.Where(to => Math.Abs(to % 3 - from % 3) + Math.Abs(to / 3 - from / 3) == 1))
        {
            Vector2 a = CacheCell(from).GetCenter(), b = CacheCell(to).GetCenter();
            bool horizontal = from / 3 == to / 3;
            float inset = TargetOpening / 2;
            float outside = horizontal ? (from / 3 == 0 ? Field.Top + inset : Field.Bottom - inset)
                : (from % 3 == 0 ? Field.Left + inset : Field.Right - inset);
            Vector2 sideA = horizontal ? new(a.X, outside) : new(outside, a.Y);
            Vector2 sideB = horizontal ? new(b.X, outside) : new(outside, b.Y);
            Vector2[] points = { a, sideA, sideB, b };
            bool clear = true;
            for (int segment = 1; segment < points.Length; segment++)
            {
                int samples = (int)Math.Ceiling(points[segment - 1].DistanceTo(points[segment]) / 2);
                for (int sample = 0; sample <= samples; sample++)
                    if (bodies.Any(body => body.Shape.Collide(body.GlobalTransform, core,
                        new Transform2D(0, points[segment - 1].Lerp(points[segment], sample / (float)samples))))) clear = false;
            }
            if (clear) routes.Add(new CacheSlide(from, to, sideA, sideB));
        }
        int current = routes.Select(route => route.From).Distinct().OrderBy(cell => CacheCell(cell).GetCenter().DistanceSquaredTo(_anchor)).First();
        int previous = -1;
        for (int step = 0; step < Tier + 2; step++)
        {
            var options = routes.Where(route => route.From == current).ToArray();
            var onward = options.Where(route => route.To != previous).ToArray();
            var slide = (onward.Length == 0 ? options : onward)[0];
            _cacheSlides.Add(slide);
            previous = current;
            current = slide.To;
        }
    }
    private Rect2 MovingCacheBounds(CacheSlide slide, double age)
    {
        Vector2 size = CacheCell(slide.To).Size;
        float squeeze = age < 1.25 ? Mathf.Clamp((float)(age - .6) / .65f, 0, 1) : age < 3.85 ? 1
            : 1 - Mathf.Clamp((float)(age - 3.85) / .7f, 0, 1);
        if (slide.From / 3 == slide.To / 3) size.Y = Mathf.Lerp(size.Y, 18 + 2 * Tier, squeeze);
        else size.X = Mathf.Lerp(size.X, 18 + 2 * Tier, squeeze);
        Vector2 center = CacheCell(slide.To).GetCenter().Lerp(CacheCell(slide.From).GetCenter(), Mathf.Clamp((float)(age - 1.25) / 2.6f, 0, 1));
        return new Rect2(center - size * .5f, size);
    }

    private void SlidingCache()
    {
        if (Time >= _warning + _cacheSlides.Count * CachePeriod) { Finish(); return; }
        var cue = VisibleCacheSlide;
        for (int cell = 0; cell < 9; cell++)
            if (cell != cue.Slide.From && cell != cue.Slide.To) Box(CacheCell(cell), Time >= _warning);
        Box(MovingCacheBounds(cue.Slide, cue.Age), Time >= _warning);
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
                Cue(at + new Vector2(-5, -11), AttackTelegraph.Symbol.Pips, count: weight + 1);
            }
            if (At(release))
                for (int k = 0; k < (Tier == 0 ? 1 : 2 + Tier); k++)
                    Remember(at + new Vector2((k - (1 + Tier) * 0.5f) * 10, 0), Vector2.Down, i, 38 + weight * 7);
        }
        if (Time > _warning + count * 0.65 + 6) Finish();
    }

    private void BoundaryWrap()
    {
        double elapsed = Time - _warning;
        if (elapsed > (2 * Field.Width + 8) / WrapSpeed) { Finish(); return; }
        for (int row = 0; row < 2 + Tier; row++)
        for (int packet = 0; packet < (3 + Tier + (Tier >= 2 ? Tier - 1 : 0)); packet++)
        {
            float phase = Mathf.PosMod(row * Field.Width / (2 + Tier) + packet * 28, Field.Width);
            float distance = phase + (float)Math.Max(0, elapsed) * WrapSpeed;
            if (distance > 2 * Field.Width + 6) continue;
            float x = Field.Left + (distance < 2 * Field.Width ? Mathf.PosMod(distance, Field.Width) : distance - Field.Width);
            var rect = new Rect2(x - 6, Field.Top + row * Field.Height / (2 + Tier), 12, Field.Height / (2 + Tier));
            void Add(Rect2 part)
            {
                part = part.Intersection(Field.Rect);
                if (part.HasArea()) Box(part, elapsed >= 0);
            }
            Add(rect);
            if (rect.Position.X < Field.Left) Add(new Rect2(rect.Position + Vector2.Right * Field.Width, rect.Size));
            if (rect.End.X > Field.Right && distance < 2 * Field.Width)
                Add(new Rect2(rect.Position - Vector2.Right * Field.Width, rect.Size));
        }
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
            if (_read[i]) { Cue(at + new Vector2(-8, 0), AttackTelegraph.Symbol.Received); continue; }
            Box(new Rect2(at - new Vector2(26, 20), new Vector2(52, 40)), Time >= deadline && Time < deadline + 1);
            Cue(at + new Vector2(-12, -24), AttackTelegraph.Symbol.Envelope);
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
            Cue(at + _airlockAxis.Orthogonal() * 25, (gate == 0 ? entered : closed) ? AttackTelegraph.Symbol.Locked : AttackTelegraph.Symbol.Link);
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
            if (_nextCells[i] && !_cells[i]) Cue(at + new Vector2(5, 17), AttackTelegraph.Symbol.Envelope);
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
            Cue(end + new Vector2(-8, 0), severed ? AttackTelegraph.Symbol.BrokenLink : AttackTelegraph.Symbol.Link);
        }
        if (Time < _warning + branch * 2.4 + 1.1)
            for (int i = 0; i < Density / 2; i++)
                Disc(fork + new Vector2(75, (i - (Density / 2 - 1) * 0.5f) * 10), 6, false);
        if (At(_warning + branch * 2.4 + 1.1))
            for (int i = 0; i < Density / 2; i++)
                Remember(fork + new Vector2(75, (i - (Density / 2 - 1) * 0.5f) * 10),
                    new Vector2(-1, upConnected ? 0.55f : -0.55f), i, 45);
    }

    private float SpringPropagationSpeed => 38 + 4 * Tier;
    private double SpringWaveDuration => (Field.Width + 160) / SpringPropagationSpeed;
    private int SpringWaveCount => 3 + Tier + (Tier >= 2 ? Tier - 1 : 0);
    private double SpringInterval => 3.25 - Tier * .53;
    internal IEnumerable<(float Front, float Top, bool Live)> VisibleSpringWaves
    {
        get
        {
            for (int wave = 0; wave < SpringWaveCount; wave++)
            {
                double elapsed = Time - _warning - wave * SpringInterval;
                if (elapsed < -2.8 || elapsed >= SpringWaveDuration) continue;
                yield return (Field.Left - 80 + (float)Math.Max(0, elapsed) * SpringPropagationSpeed,
                    Field.Top + (wave + (_anchor.Y < Field.CenterY ? 0 : 1)) % 2 * Field.Height / 2, elapsed >= 0);
            }
        }
    }
    private static float[] CompressedSpringNodes(float front)
        => Enumerable.Range(0, 16).Select(i => {
            float rest = Field.Left + i * Field.Width / 15;
            float offset = (rest - front) / 24;
            return rest + 12 * Mathf.Exp(-offset * offset);
        }).ToArray();

    private void DelayedSpring()
    {
        if (Time >= _warning + (SpringWaveCount - 1) * SpringInterval + SpringWaveDuration) { Finish(); return; }
        foreach (var wave in VisibleSpringWaves)
        {
            if (!wave.Live)
                Box(new Rect2(Field.Left, wave.Top, Field.Width, Field.Height / 2), false);
            var nodes = CompressedSpringNodes(wave.Front);
            for (int i = 0; i < nodes.Length; i++)
            {
                bool compressed = i > 0 && nodes[i] - nodes[i - 1] < Field.Width / 15 * .85f
                    || i < nodes.Length - 1 && nodes[i + 1] - nodes[i] < Field.Width / 15 * .85f;
                if (!compressed || !wave.Live) continue;
                float left = Mathf.Clamp(i == 0 ? Field.Left : (nodes[i - 1] + nodes[i]) * .5f, Field.Left, Field.Right);
                float right = Mathf.Clamp(i == nodes.Length - 1 ? Field.Right : (nodes[i + 1] + nodes[i]) * .5f, Field.Left, Field.Right);
                if (right <= left) continue;
                Box(new Rect2(left, wave.Top, right - left, Field.Height / 2), true);
            }
        }
    }

    private void LocalOverwrite(double delta)
    {
        if (Time < _warning)
        {
            foreach (var band in _snapshotBands) Box(SnapshotBounds(band.Side, band.Y), false, (float)(Time / _warning));
            return;
        }
        bool draining = _exchanges >= 2 + Tier && Time >= _next;
        if (draining && Time >= _next + 1.2) { Finish(); return; }
        if (!_exchangePending && !draining && Time >= _next)
        {
            _exchangePending = true;
            _exchangeAt = Time + SnapshotHold;
        }
        if (_exchangePending && Time >= _exchangeAt)
        {
            foreach (var band in _snapshotBands) band.Side = 1 - band.Side;
            _exchangePending = false;
            _exchanges++;
            _next = Time + 2.8;
        }
        foreach (var band in _snapshotBands)
        {
            if (!_exchangePending && !draining)
            {
                band.Y += band.Velocity * (float)delta;
                if (band.Y <= Field.Top && band.Velocity < 0 || band.Y >= Field.Bottom && band.Velocity > 0)
                {
                    band.Y = Mathf.Clamp(band.Y, Field.Top, Field.Bottom);
                    band.Velocity = -band.Velocity;
                }
            }
            Box(SnapshotBounds(band.Side, band.Y), true);
            if (_exchangePending)
                Box(SnapshotBounds(1 - band.Side, band.Y), false, (float)((SnapshotHold - (_exchangeAt - Time)) / .6));
        }
    }

    private void SecondVisitWeight()
    {
        double t = Time - _warning;
        if (t >= 0 && t < VisitDuration)
        {
            int x = Mathf.Clamp((int)((Player.GlobalPosition.X - Field.Left) / (Field.Width / 7)), 0, 6);
            int y = Mathf.Clamp((int)(Player.GlobalPosition.Y / (Field.Bottom / 5)), 0, 4);
            int cell = y * 7 + x;
            if (cell != _lastCell)
            {
                if (Tier == 3 && _lastCell >= 0 && Time >= _visitEchoReadyAt)
                {
                    int lastX = _lastCell % 7, lastY = _lastCell / 7;
                    bool horizontal = lastY == y;
                    Vector2 size = new(Field.Width / 7, Field.Height / 5);
                    Vector2 from = Field.Rect.Position + new Vector2((lastX + .5f) * size.X, (lastY + .5f) * size.Y);
                    Vector2 to = Field.Rect.Position + new Vector2((x + .5f) * size.X, (y + .5f) * size.Y);
                    Rect2 bounds = horizontal
                        ? new Rect2(Field.Left, Field.Top + y * size.Y, Field.Width, size.Y)
                        : new Rect2(Field.Left + x * size.X, Field.Top, size.X, Field.Height);
                    double lead = WarnFor(Math.Max(size.X, size.Y) + Player.HitRadius * 2, 1.35);
                    if (Time + lead + 1.8 <= _warning + VisitDuration)
                    {
                        _visitEchoes.Add(new(from, to, bounds, Time, Time + lead, Time + lead + 1.8));
                        _visitEchoReadyAt = Time + lead + 1.85;
                    }
                }
                _visits[cell]++;
                if (_visits[cell] == 1) _firstStepAt[cell] = Time;
                if (_visits[cell] == 2) _markedAt[cell] = _firstStepAt[cell];
                _lastCell = cell;
                _visitEnteredAt = Time;
            }
            if (_visits[cell] == 1 && Time - _visitEnteredAt >= (Tier == 0 ? 2.4 : 1.5))
            {
                _visits[cell] = 2;
                _markedAt[cell] = Time;
            }
        }
        if (t >= VisitDuration) { Finish(); return; }
        Vector2 tile = new(Field.Width / 7, Field.Height / 5);
        for (int i = 0; i < 35; i++)
        {
            var cell = new Rect2(Field.Rect.Position + new Vector2(i % 7, i / 7) * tile, tile);
            if (_visits[i] == 1)
            {
                Box(cell, false, Mathf.Clamp((float)(Time - _firstStepAt[i]) / .45f, 0, 1), cueOnly: true);
                continue;
            }
            if (_visits[i] == 0) continue;
            double closesAt = _markedAt[i] + Math.Max(2.55, WarnFor(tile.Length() / 2, 1.7));
            Box(cell, Time >= closesAt, Mathf.Clamp((float)((Time - _markedAt[i]) / .35), 0, 1));
        }
        foreach (var echo in VisibleVisitEchoes)
        {
            Box(echo.Bounds, Time >= echo.FiresAt, (float)Math.Clamp((Time - echo.StartsAt) / (echo.FiresAt - echo.StartsAt), 0, 1));
            if (Time < echo.FiresAt)
            {
                Line(echo.From, echo.To, 1, false, cueOnly: true);
                Cue(echo.To, AttackTelegraph.Symbol.Footprint);
            }
        }

    }

    internal IEnumerable<(Rect2 Bounds, int Visits, float Imprint)> VisibleVisitMarks
        => Enumerable.Range(0, 35).Select(i => (new Rect2(Field.Left + i % 7 * Field.Width / 7,
            Field.Top + i / 7 * Field.Height / 5, Field.Width / 7, Field.Height / 5), _visits[i],
            i == _lastCell ? Mathf.Clamp((float)((Time - _visitEnteredAt) / 1.5), 0, 1) : 0));

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
        bool sent = false;
        foreach (var shot in GetTree().GetNodesInGroup("player_bullets").OfType<Bullet>().Where(b => b.Active))
            if (_received.Add((shot.GetInstanceId(), shot.ActivationId))) sent = true;
        if (sent && ReplyReady)
        {
            float x = Player.GlobalPosition.X < Field.CenterX ? Field.Right + 12 : Field.Left - 12;
            int sources = 4 + 2 * Tier;
            var reply = new StarReply { Target = Player.GlobalPosition, StartsAt = Time, FiresAt = Time + 2.4,
                Sources = Enumerable.Range(0, sources).Select(i => new Vector2(x, Field.Top + (i + .5f) * Field.Height / sources)).ToArray() };
            reply.EntryTime = reply.Sources.Max(p => p.DistanceTo(reply.Target)) / StarReplySpeed;
            reply.ExitTime = reply.Sources.Max(p => reply.FarEdge(p).DistanceTo(reply.Target)) / StarReplySpeed;
            StarReplies.Add(reply);
            _next = Time + 3.5;
        }
        foreach (var (reply, star) in _answerStars)
        {
            if (!star.Live) continue;
            float age = (float)(Time - star.Born);
            if (age >= reply.EntryTime + reply.ExitTime) { Pool.Despawn(star.Bullet); continue; }
            Vector2 position = reply.Position(star.Origin, age);
            Vector2 velocity = age <= reply.EntryTime ? (reply.Target - star.Origin) / reply.EntryTime
                : (reply.FarEdge(star.Origin) - reply.Target) / reply.ExitTime;
            star.Bullet.GlobalPosition = position;
            star.Bullet.Velocity = velocity;
            if (velocity != Vector2.Zero) star.Bullet.Rotation = velocity.Angle();
        }
        foreach (var reply in StarReplies)
        {
            if (!reply.Fired)
                foreach (var source in reply.Sources)
                {
                    float progress = (float)((Time - reply.StartsAt) / .6);
                    Line(source, reply.Target, 4, false, progress);
                    Line(reply.Target, reply.FarEdge(source), 4, false, progress);
                }
            if (reply.Fired || Time < reply.FiresAt + reply.Pulses * ((8 + TargetOpening) / StarReplySpeed)) continue;
            foreach (var source in reply.Sources)
            {
                var memory = Remember(source, source.DirectionTo(reply.Target), _beat++, StarReplySpeed);
                memory.Bullet.Homing = false;
                memory.Bullet.UseBossIllustration(WarningVisual, fitRadius: 4);
                memory.Bullet.CancelLeadIn();
                {
                    memory.Bullet.BossPathDriven = true;
                    _answerStars.Add((reply, memory));
                }
            }
            reply.Fired = ++reply.Pulses == 5;
        }
        for (int letter = 1; letter <= 3; letter++)
        {
            if (At(_warning + letter * 3.5) && StarReplies.Count < letter)
                _unansweredStars.Add((letter, Player.GlobalPosition, Time));
        }
        _unansweredStars.RemoveAll(star => Time >= star.WarnedAt + UnansweredLead + UnansweredHold
            || Time < star.WarnedAt + UnansweredLead && StarReplies.Count >= star.Letter);
        foreach (var star in _unansweredStars)
            Disc(star.At, UnansweredRadius, Time >= star.WarnedAt + UnansweredLead,
                Mathf.Clamp((float)(Time - star.WarnedAt) / .6f, 0, 1));
        if ((StarReplies.Count == 3 || ReplyFailed) && StarReplies.All(reply => reply.Fired)
            && _unansweredStars.Count == 0 && !OwnedShots.Any()) Finish();
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
                Cue(connection.Root + Vector2.Up * 10, AttackTelegraph.Symbol.Pips, count: (connection.Rank + 1));
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

    private const float BurdenRadius = 22;
    private float SwingRate => .62f + Tier * .035f;
    private double SwingEnd => 1.5 * Math.PI / SwingRate;
    private Vector2 _hardBurdenPivot;
    private float _hardBurdenSwing;
    private Vector2 BurdenPivot => Tier == 3 ? new(Field.CenterX, Field.Top - 8) : _hardBurdenPivot;
    private float BurdenLength => Field.Bottom - TargetOpening - BurdenRadius - BurdenPivot.Y;
    private float BurdenSwing => Tier == 3 ? 1.05f : _hardBurdenSwing;
    private float BurdenAngle => Mathf.Sin(Math.Max(0, (float)(Time - _warning)) * SwingRate) * BurdenSwing;
    internal Vector2 VisibleBurdenWeight => BurdenPivot + Vector2.Down.Rotated(BurdenAngle) * BurdenLength;
    private readonly List<float> _burdenTurnMarks = new();

    private void SuspendedBurden()
    {
        double age = Time - _warning;
        if (age < SwingEnd)
        {
            Line(BurdenPivot, VisibleBurdenWeight, 3, age >= 0, Mathf.Clamp((float)(Time / _warning), 0, 1));
            Disc(VisibleBurdenWeight, BurdenRadius, age >= 0);
        }
        float width = 52 + Tier * 5, speed = 43 + Tier * 4;
        for (int turn = 0; turn < 2; turn++)
        {
            double turnAt = _warning + (.5 + turn) * Math.PI / SwingRate;
            if (Time < turnAt) continue;
            if (_burdenFalls.Count == turn)
            {
                float center = Tier == 3 ? Player.GlobalPosition.X : VisibleBurdenWeight.X;
                _burdenTurnMarks.Add(center);
                _burdenFalls.Add(new(Mathf.Clamp(center - width / 2, Field.Left, Field.Right - width), turnAt, turnAt + 2.65));
            }
            var fall = _burdenFalls[turn];
            for (int bundle = 0; bundle < 3 + Tier; bundle++)
            {
                double firesAt = fall.FiresAt + bundle * .47;
                float left = Mathf.Lerp(fall.Left, Field.CenterX - width / 2, bundle / (2f + Tier));
                if (Time < firesAt)
                    Box(new Rect2(left, Field.Top, width, Field.Height), false, Mathf.Clamp((float)(Time - turnAt) / .55f, 0, 1));
                else
                {
                    float y = Field.Top - 12 + (float)(Time - firesAt) * speed;
                    if (y < Field.Bottom) Box(new Rect2(left, y, width, 12), true);
                }
            }
        }
        if (_burdenFalls.Count == 2 && Time >= _burdenFalls[1].FiresAt + (2 + Tier) * .47 + (Field.Height + 12) / speed)
            Finish();
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
                Cue(new Vector2(Field.Left + 20, y), AttackTelegraph.Symbol.Undo);
                continue;
            }
            Box(new Rect2(Field.Left + 12, y - 16, Field.Width - 24, 32), Time >= _warning + 0.5 && Time < _warning + 1.3);
            if (At(_warning + 1.4))
                for (int i = 0; i < Density / 3; i++)
                    Remember(new Vector2(Field.Right - 14 - i * 13, y), Vector2.Left, i, 47);
        }
        if (Time > _warning + 8) Finish();
    }

    private static int WingWeakSide(double age) => (int)(Math.Max(0, age) / 4.8) % 2 == 0 ? -1 : 1;

    internal (int WeakSide, double At)? VisibleWingSwitch
    {
        get
        {
            double age = Time - _warning;
            int nextBeat = (int)(Math.Max(0, age) / 4.8) + 1;
            double next = nextBeat * 4.8;
            double lead = Tier switch { 0 => 2.4, 1 => 1.8, 2 => 1.2, _ => 3.2 };
            return age >= 0 && nextBeat < 3 && next - age <= lead
                ? (WingWeakSide(next), _warning + next) : null;
        }
    }

    internal Vector2 VisibleWingWind
    {
        get
        {
            double age = Time - _warning;
            if (age < 0 || age >= 14.4) return Vector2.Zero;
            int side = Player.GlobalPosition.X < Field.CenterX ? -1 : 1;
            int weak = WingWeakSide(age);
            return Vector2.Right * side * (side == weak ? 12 : 38);
        }
    }

    internal (Rect2 Bounds, double FiresAt, double EndsAt)? VisibleWingSweep
    {
        get
        {
            if (Tier != 3) return null;
            double age = Time - _warning;
            int beat = Math.Clamp((int)(Math.Max(0, age) / 4.8), 0, 2);
            double start = _warning + beat * 4.8;
            if (Time >= start + 4.8) return null;
            int strong = -WingWeakSide(beat * 4.8);
            float left = strong < 0 ? Field.Left : Field.CenterX - 12;
            float right = strong < 0 ? Field.CenterX + 12 : Field.Right;
            return (new Rect2(left, Field.Top, right - left, Field.Height), start + 2.4, start + 4.8);
        }
    }

    private void WingDisplacement(double delta)
    {
        double t = Time - _warning;
        WingFeathers();
        if (t >= 14.4 && !_memories.Any(feather => feather.Live)) { Finish(); return; }
        Box(new Rect2(Field.Left, Field.Top, 14, Field.Height), t >= 0, (float)(Time / _warning));
        Box(new Rect2(Field.Right - 14, Field.Top, 14, Field.Height), t >= 0, (float)(Time / _warning));
        if (VisibleWingSweep is { } sweep)
            Box(sweep.Bounds, Time >= sweep.FiresAt, (float)Math.Clamp(1 - (sweep.FiresAt - Time) / 2.4, 0, 1));
        if (t >= 0) Player.GlobalPosition = (Player.GlobalPosition + VisibleWingWind * (float)delta)
            .Clamp(Field.Rect.Position, Field.Rect.End);
    }

    private void WingFeathers()
    {
        int columns = 7 + 2 * Tier;
        for (int beat = 0; beat < 3; beat++)
        for (int column = 0; column < columns; column++)
        {
            double fire = _warning + beat * 4.8;
            float x = Field.Left + (column + (beat % 2 == 0 ? .35f : .65f)) * Field.Width / columns;
            Vector2 from = new(x, Field.Top - 8);
            int weak = WingWeakSide(beat * 4.8);
            int side = x < Field.CenterX ? -1 : 1;
            Vector2 velocity = new(weak * (side == weak ? 10 : 24), 38 + 2 * Tier);
            if (Time >= fire - 2.7 && Time < fire)
                Line(from, from + velocity * ((Field.Height + 18) / velocity.Y), 3, false,
                    Mathf.Clamp((float)(Time - fire + 2.7) / .6f, 0, 1));
            for (int pulse = 0; pulse < 3 + Tier; pulse++)
                if (At(fire + pulse * .32)) ReleaseFeather(from, velocity, column);
        }
    }

    private void ReleaseFeather(Vector2 from, Vector2 velocity, int column)
    {
        var feather = Remember(from, velocity, column, velocity.Length());
        feather.Bullet.Homing = false;
        feather.Bullet.UseBossIllustration(WarningVisual, fitRadius: 3);
        feather.Bullet.CancelLeadIn();
    }

    internal float ReturnInner => Evolved ? 39 : 23;
    internal float ReturnOuter => ReturnInner + (Tier switch { 0 => 48, 1 => 38, 2 => 30, _ => 24 });
    internal float ReturnSpeed => Tier switch { 0 => 24, 1 => 29, 2 => 37, _ => 40 };
    internal double ReturnWarning => _warning;
    private void ReturnDistance(double delta)
    {
        double t = Time - _warning;
        double duration = Tier >= 2 ? 18 : 14;
        if (t >= 0 && t < duration)
        {
            Boss.GlobalPosition = Boss.GlobalPosition.MoveToward(Player.GlobalPosition, ReturnSpeed * (float)delta)
                .Clamp(new Vector2(Field.Left + 12, Field.Top + 12), new Vector2(Field.Right - 12, Field.Bottom - 12));
        }
        if (At(_warning)) Boss.ShowAnimalAttack();
        Vector2 center = Boss.GlobalPosition;
        if (t < duration)
        {
            Disc(center, ReturnInner, t >= 0);
            OutsideDisc(center, ReturnOuter, t >= 0);
        }
        LastPosition = center;
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
        public bool AcceptsReply = true;
        public Vector2[] CardAccessPath = Array.Empty<Vector2>();
        public AttackCurveRoute? Route;
        public double CardTravelSeconds;
    }
    private readonly List<MinaEasyMemoryCue> _easyMemoryCues = new();
    private readonly HashSet<int> _easyReadRounds = new();
    private readonly bool[] _easyRecordState = new bool[35];
    private int _easyRoundsCreated;
    private Vector2 _easyConnectionPull;
    private CircleShape2D? _easyCardProbe;
    private int EasyRounds => Variant switch { 0 => Tier >= 2 ? 2 : 3, 1 => 3 + Tier, 2 => 1, 3 => 4 + Tier, _ => 1 };
    private int EasyBurdenLoads => Tier >= 2 ? 4 + Tier * 2 : (Tier == 0 ? 4 : 6) + (Evolved ? 2 : 0);
    private int EasyBurdenRows => Tier >= 2 ? Tier + 2 : 3 + Tier * 2;
    private int EasyDraftRows => 4 + Tier * 2;
    private int EasyRelayChains => Tier switch { 0 => 7, 1 => 7, 2 => 8, _ => 8 };
    private double EasyRoundInterval => Variant switch { 0 => Tier >= 2 ? 14.8 : 5.4, 1 => Tier == 3 ? 4.8 : 6 + Tier * .3 + (Evolved ? .4 : 0), 2 => 0, 3 => 4.4 + Tier * .3 + (Evolved ? .4 : 0), _ => 6.4 };
    internal override IEnumerable<BossEasyCue> EasyCues => Kind == BossTechniqueKind.Area
        ? _easyMemoryCues.Select(c => new BossEasyCue(c.Key, c.StartsAt, c.FiresAt,
            EasyMemoryProgress(c), c.Fired, c.Cancelled))
        : base.EasyCues;

    protected override IEnumerable<(Vector2 Origin, double StartsAt, double FiresAt)> TelegraphTimings
        => _easyMemoryCues.Where(cue => !cue.Cancelled && !cue.Fired)
            .Select(cue => (cue.Mode == 1 ? cue.Card
                : cue.Mode is 3 or 5 ? cue.Bounds[0].Position + Vector2.One * 3 : cue.Points[0],
                cue.StartsAt, cue.FiresAt));

    private float EasyMemoryProgress(MinaEasyMemoryCue cue)
        => cue.Mode == 1 ? 1 : Mathf.Clamp((float)((Time - cue.StartsAt - .4) / .8), 0, 1);

    private bool EasyMemorySegmentEnded(MinaEasyMemoryCue cue)
        => cue.Mode == 4 ? cue.Exited || cue.ReleaseCue >= 0 && _easyMemoryCues[cue.ReleaseCue].Energized
            : cue.Mode == 7 ? cue.Exited : Time >= cue.EndsAt;

    private void StartEasyMemory()
    {
        if (Kind != BossTechniqueKind.Area || Variant is < 0 or > 4)
            throw new InvalidOperationException("A memory phase requires Area/0 through Area/4.");
        TextureFilter = TextureFilterEnum.Linear;
        _memoryIllustration = BossProjectileArt.For(MemoryVisual)!.Texture;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/mina_memory_art.gdshader") };
        material.SetShaderParameter("memory_art", _memoryIllustration);
        material.SetShaderParameter("frame_grid", new Vector2(ProjectileTurntable.Columns, ProjectileTurntable.FrameCount / ProjectileTurntable.Columns));
        _memoryWarningCanvas = new Node2D { UseParentMaterial = true, ShowBehindParent = true };
        _memoryWarningCanvas.Draw += () => _memoryWarnings.Draw(_memoryWarningCanvas);
        AddChild(_memoryWarningCanvas);
        _memoryCanvas = new Node2D { Material = material };
        _memoryCanvas.Draw += () => _memoryArt.Draw(_memoryCanvas);
        AddChild(_memoryCanvas);
        _id = 4 + Variant;
        _warning = 3.2;
        var identity = Variant switch
        {
            0 => ("抱えた声の順送り", "mina.unfolding-burden", "重い記憶から解凍される。広がり終える頁の短い端を回る"),
            1 => ("未送信、既読の返事", "mina.acknowledged-rows", "チェックの付いた行へ。封筒に触れると、その行の着弾も止まる"),
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
            if (cue.Mode == 1 && cue.AcceptsReply && !cue.Cancelled && Time < cue.FiresAt
                && !_easyReadRounds.Contains(cue.Round) && Player.GlobalPosition.DistanceTo(cue.Card) <= 14)
            {
                cue.Cancelled = true;
                cue.CancelledAt = Time;
                _easyReadRounds.Add(cue.Round);
            }
            if (Time < cue.StartsAt || EasyMemorySegmentEnded(cue)) continue;
            if (cue.Cancelled) continue;
            if (cue.Mode is 4 or 7 ? !cue.Energized : Time < cue.FiresAt)
            {
                float progress = EasyMemoryProgress(cue);
                if (progress <= 0) continue;
                if (cue.Mode is 0 or 3 or 5)
                    foreach (var bounds in cue.Bounds)
                        Box(new Rect2(bounds.Position, new Vector2(bounds.Size.X * progress, bounds.Size.Y)), false, progress);
                else if (cue.Mode is 4 or 7)
                    EasyMemoryPathHazards(cue.Points, cue.Radius, progress, false);
                else if (cue.Mode == 2)
                    foreach (var bounds in cue.Bounds)
                        Box(new Rect2(bounds.Position, new Vector2(bounds.Size.X * progress, bounds.Size.Y)), false, progress, cueOnly: true);
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

    private float RelayPitch => Field.Height / (EasyRelayChains - 1);
    private float RelayRadius(float mass) => Math.Min(9 + (mass - 1) * 3, (RelayPitch - TargetOpening) / 2 - 2);
    private float RelayBow(float x)
    {
        float span = Mathf.Clamp((x - Field.Left) / Field.Width, 0, 1);
        float pull = (_anchor.Y < Field.CenterY ? -8 : 8) + _easyConnectionPull.Y * .46f;
        return Mathf.Sin(span * Mathf.Pi) * pull;
    }

    private void PrepareEasyMemoryRound(int round, double start)
    {
        Vector2 reserved = Player.GlobalPosition;
        if (Variant == 0)
        {
            float[] rows = Tier == 0 ? new[] { 24, Mathf.Clamp(reserved.Y, 82, 134), Field.Bottom - 24 }
                : Enumerable.Range(0, EasyBurdenRows).Select(row => (row + (round == 0 ? .5f : round == 1 ? 0 : 1)) * Field.Height / EasyBurdenRows).ToArray();
            if (Tier >= 2)
                for (int row = 0; row < rows.Length; row++) rows[row] = Field.Top + (row + .5f) * Field.Height / EasyBurdenRows;
            int direction = round % 2 == 0 ? 1 : -1;
            for (int row = 0; row < EasyBurdenRows; row++)
                for (int load = 0; load < EasyBurdenLoads; load++)
                {
                    float height = Tier == 0 ? row == 1 ? 62 : 52 : Field.Height / EasyBurdenRows - (Tier >= 2 ? 0 : 7);
                    Vector2 from = new(direction > 0 ? Field.Left - 20 : Field.Right + 20, rows[row]);
                    Vector2 to = new(direction > 0 ? Field.Right + 20 : Field.Left - 20, rows[row]);
                    double fire = start + 3.2 + (EasyBurdenRows - 1 - row) * (Tier >= 2 ? .9 : .42 / (1 + Tier * .5))
                        + load * (Tier >= 2 ? .55 : .38 - Tier * .045);
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
            var cards = Enumerable.Range(0, EasyDraftRows)
                .Select(row => EasyAccessibleDraftCard(reserved, row, Math.Abs(row - current) <= 1)).ToArray();
            int answered = Enumerable.Range(0, EasyDraftRows).Where(row => Math.Abs(row - current) == 1)
                .OrderByDescending(row => cards[row].Verified).ThenBy(row => round % 2 == 0 ? row : -row).First();
            for (int row = 0; row < EasyDraftRows; row++)
            {
                var card = cards[row];
                _easyMemoryCues.Add(new MinaEasyMemoryCue
                {
                    Key = $"draft:{round}:{row}", Group = $"draft:{round}:{row}", Mode = 1,
                    Round = round, Index = row, StartsAt = start, FiresAt = start + 3.2,
                    EndsAt = (round + 1) * EasyRoundInterval,
                    Card = card.Card, CardReserved = reserved,
                    Bounds = new[] { new Rect2(Field.Left, Field.Top + row * rowHeight, Field.Width, rowHeight) },
                    Cancelled = row == answered, CancelledAt = row == answered ? start : null,
                    CurrentRowCard = row == current, CardAccessPath = card.Path, CardAccessVerified = card.Verified,
                    CardStartBodyOverlap = card.InitialOverlap, CardTravelSeconds = card.Seconds,
                    AcceptsReply = Tier != 3 || row != current,
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
                var cell = new Rect2(Field.Left + i % 7 * Field.Width / 7, Field.Top + i / 7 * Field.Height / 5,
                    Field.Width / 7, Field.Height / 5);
                var center = (cell.GetCenter() * 2).Round() / 2;
                if (!next[i]) continue;
                _easyMemoryCues.Add(new MinaEasyMemoryCue
                {
                    Key = $"generation:{round}:{i}", Group = $"generation:{round}:{i}", Mode = 3,
                    Round = round, Index = i, StartsAt = start, FiresAt = start + 3.2, EndsAt = start + 5.95 + Tier * .3 + (Evolved ? .4 : 0),
                    Bounds = new[] { cell }, Card = center,
                });
            }
        }
        else
        {
            Vector2 target = reserved;
            var profile = GD.Load<Curve2D>("res://assets/attack_curves/mina_handoff.tres");
            float restingY = target.Y - RelayBow(target.X);
            int linked = Mathf.Clamp(Mathf.RoundToInt((restingY - Field.Top) / RelayPitch), 0, EasyRelayChains - 1);
            for (int chain = 0; chain < EasyRelayChains; chain++)
            {
                bool right = target.X <= Field.CenterX;
                float row = chain == linked ? restingY : Field.Top + chain * RelayPitch;
                var network = new Vector2[5];
                float middleX = Mathf.Clamp(target.X, Field.Left + 82, Field.Right - 82);
                float entryX = right ? Field.Left - 28 : Field.Right + 28;
                float exitX = right ? Field.Right + 28 : Field.Left - 28;
                for (int node = 0; node < network.Length; node++)
                {
                    float x = node <= 2 ? Mathf.Lerp(entryX, middleX, node / 2f)
                        : Mathf.Lerp(middleX, exitX, (node - 2) / 2f);
                    network[node] = new Vector2(x, row + RelayBow(x));
                }
                int chainStart = _easyMemoryCues.Count;
                double chainStartAt = start + chain * (Tier switch { 0 => .58, 1 => .46, 2 => .34, _ => .22 });
                double fire = chainStartAt + 3.2;
                float speed = 52 + Tier * 7 + _easyConnectionPull.Length() * .5f;
                float[] masses = { 1, 1.25f, 1.5f, 1.1f };
                for (int node = 0; node < network.Length - 1; node++)
                {
                    float radius = RelayRadius(masses[node]);
                    Vector2 direction = network[node].DirectionTo(network[node + 1]);
                    float nextRadius = node + 1 < masses.Length ? RelayRadius(masses[node + 1]) : 0;
                    Vector2 contact = node + 1 < masses.Length ? network[node + 1] - direction * (radius + nextRadius) : network[node + 1];
                    var route = AttackCurveRoute.Between(profile, network[node], contact, .06f);
                    double arrival = fire + route.Length / speed;
                    _easyMemoryCues.Add(new MinaEasyMemoryCue
                    {
                        Key = $"handoff:{chain}:{node}", Group = $"handoff:{chain}:{node}", Mode = 4,
                        Round = chain, Index = node, StartsAt = chainStartAt, FiresAt = fire,
                        ArrivesAt = arrival, Points = route.Points, Route = route, Radius = radius,
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
            var held = _easyMemoryCues.Where(cue => cue.Successor >= 0).OrderByDescending(cue => cue.Index).ThenBy(cue => cue.Round).ToArray();
            for (int rank = 0; rank < held.Length; rank++)
            {
                var memory = held[rank];
                Vector2 direction = Vector2.Right * Math.Sign(memory.Points[0].X - memory.Points[^1].X);
                Vector2 exit = EasyMemoryRecoilExit(memory.Points[^1], direction, memory.Radius);
                double release = drained + .2 + (rank / EasyRelayChains) * .24;
                float speed = 50 + (memory.Speed * memory.Mass - 48) * .3f;
                int heldIndex = _easyMemoryCues.IndexOf(memory);
                memory.ReleaseCue = _easyMemoryCues.Count;
                memory.EndsAt = release;
                _easyMemoryCues.Add(new MinaEasyMemoryCue
                {
                    Key = $"release:{memory.Round}:{memory.Index}", Group = $"release:{memory.Round}:{memory.Index}", Mode = 7,
                    Round = memory.Round, Index = memory.Index, StartsAt = release - 3.2, FiresAt = release,
                    Points = new[] { memory.Points[^1], exit }, Radius = memory.Radius, Speed = speed, Mass = memory.Mass,
                    Position = memory.Points[^1], PriorPosition = memory.Points[^1], HeldMemory = heldIndex,
                    InitialStartsAt = memory.StartsAt, InitialEndsAt = memory.StartsAt + 3.2,
                    ArrivesAt = release + memory.Points[^1].DistanceTo(exit) / speed,
                    EndsAt = release + memory.Points[^1].DistanceTo(exit) / speed + .02,
                });
            }
            foreach (var terminal in _easyMemoryCues.Where(cue => cue.Mode == 4 && cue.Successor < 0))
                terminal.EndsAt = terminal.ArrivesAt + .02;
        }
    }

    private bool EasyDraftBodyHit(Vector2 position)
    {
        _easyCardProbe ??= new CircleShape2D { Radius = Player.HitRadius };
        if (!Boss.BodyContactDamaging || Boss.CollisionLayer == 0 || !Boss.Monitorable) return false;
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
        Vector2 reserved, int row, bool verifyRoute)
    {
        float height = Field.Height / EasyDraftRows;
        float top = Field.Top + row * height + 8, bottom = Field.Top + (row + 1) * height - 8;
        float[] ys = { (top + bottom) / 2, top, bottom };
        float away = reserved.X <= Boss.TechniqueCenter.X ? -1 : 1;
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
                if (!verifyRoute || initialOverlap) return (card, Array.Empty<Vector2>(), 0, false, initialOverlap);
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
            if (!Boss.BodyContactDamaging || Boss.IsShapeOwnerDisabled(owner)) continue;
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
            cue.Position = cue.Route!.At(Math.Max(0, (float)(Time - cue.FiresAt)) * cue.Speed);
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
        _memoryArt.Clear();
        _memoryWarnings.Clear();
        var live = new Color("fff0d9");
        var seen = new HashSet<string>();
        bool Preview(MinaEasyMemoryCue cue) => cue.Mode == 7 && Time < cue.StartsAt
            && cue.InitialStartsAt is double start && cue.InitialEndsAt is double end && Time >= start && Time < end;
        bool Warning(MinaEasyMemoryCue cue) => cue.Mode is 4 or 7 ? !cue.Energized : Time < cue.FiresAt;
        var groupFires = _easyMemoryCues.Where(cue => !cue.Cancelled && (Preview(cue)
            || Time >= cue.StartsAt && !EasyMemorySegmentEnded(cue) && Warning(cue)))
            .GroupBy(cue => cue.Group).ToDictionary(group => group.Key, group => group.Min(cue => Math.Max(Time, cue.FiresAt)));
        double first = groupFires.Values.DefaultIfEmpty(double.PositiveInfinity).Min();
        if (Variant == 1)
            for (int row = 1; row < EasyDraftRows; row++)
            {
                float y = Field.Top + row * Field.Height / EasyDraftRows;
                _memoryWarnings.Line(new Vector2(Field.Left, y), new Vector2(Field.Right, y), new Color(MemoryInk, .2f), .5f);
            }
        if (Variant == 2)
            foreach (var room in _easyMemoryCues.Where(cue => cue.Mode == 2))
                foreach (var door in EasyAirlockDoors(room))
                {
                    if (Time < door.OpensAt - 1.2 || Time >= door.OpensAt) continue;
                    Vector2 travel = door.Exit.GetCenter() - door.Entry.GetCenter();
                    Vector2 direction = Math.Abs(travel.X) > Math.Abs(travel.Y)
                        ? Vector2.Right * Math.Sign(travel.X) : Vector2.Down * Math.Sign(travel.Y);
                    Vector2 side = direction.Orthogonal();
                    float extent = Math.Abs(direction.X) * door.Entry.Size.X * .5f + Math.Abs(direction.Y) * door.Entry.Size.Y * .5f;
                    float halfWidth = Math.Abs(side.X) * door.Entry.Size.X * .5f + Math.Abs(side.Y) * door.Entry.Size.Y * .5f;
                    Vector2 opening = door.Entry.GetCenter() + direction * extent;
                    _memoryWarnings.Line(opening - side * halfWidth, opening + side * halfWidth, MemoryInk, 1.2f);
                    AttackTelegraph.Arrow(this, opening - direction * 5, direction, MemoryInk);
                }
        if (Variant == 4 && _easyMemoryCues.Count == 0)
        {
            Vector2 pulled = _anchor + _easyConnectionPull;
            _memoryWarnings.Line(_anchor, pulled, new Color(MemoryInk, .4f), .8f);
            DrawEasyMemoryPort(_anchor, new Color(MemoryInk, .5f));
            DrawWarningMemoryEnvelope(pulled, new Vector2(8, 5), new Color("dcecf1"));
        }
        foreach (var cue in _easyMemoryCues.OrderBy(cue => Warning(cue) ? 0 : 1)
            .ThenByDescending(cue => groupFires.GetValueOrDefault(cue.Group, cue.FiresAt)).ThenBy(cue => cue.FiresAt))
        {
            bool next = groupFires.GetValueOrDefault(cue.Group, cue.FiresAt) <= first + .02;
            Color warning = AttackTelegraph.OrderedInk(MemoryInk, next);
            if (cue.Mode == 7 && Time < cue.StartsAt && cue.InitialStartsAt is double preview
                && cue.InitialEndsAt is double previewEnd && Time >= preview && Time < previewEnd)
            {
                if (GameManager.ShowAttackTelegraphs)
                {
                    float progress = Mathf.Clamp((float)((Time - preview - .4) / .8), 0, 1);
                    if (progress > 0) DrawEasyMemoryRoute(cue.Points, progress, new Color(warning, .65f), cue.Radius);
                }
                continue;
            }
            if (Time < cue.StartsAt || EasyMemorySegmentEnded(cue)) continue;
            if (cue.Mode == 1 && (cue.Cancelled || Time < cue.FiresAt))
            {
                DrawDraftRow(cue);
                continue;
            }
            if (cue.Mode is 4 or 7 ? !cue.Energized : Time < cue.FiresAt)
            {
                if (!seen.Add(cue.Group)) continue;
                if (!GameManager.ShowAttackTelegraphs) continue;
                float progress = EasyMemoryProgress(cue);
                if (cue.Mode is 0 or 3 or 5)
                {
                    foreach (var bounds in cue.Bounds)
                        if (progress > 0) DrawEasyMemoryBox(new Rect2(bounds.Position,
                            new Vector2(bounds.Size.X * progress, bounds.Size.Y)), warning, false);
                    Vector2 origin = cue.Mode == 0
                        ? new Vector2(Mathf.Clamp(cue.Points[0].X, Field.Left + 3, Field.Right - 3), cue.Points[0].Y)
                        : cue.Bounds[0].Position + Vector2.One * 3;
                    DrawEasyMemoryOrigin(origin, next);
                    if (cue.Mode == 0 && progress > 0)
                    {
                        DrawEasyMemoryRoute(cue.Points, progress, warning);
                        Vector2 at = cue.Points[0].Lerp(cue.Points[^1], progress);
                        AttackTelegraph.Box(this, new Rect2(at - cue.Size * .5f, cue.Size).Intersection(Field.Rect),
                            new Color(warning, warning.A * .5f), false);
                    }
                    if (cue.Mode == 3 && cue.Bounds.Length == 1)
                        DrawWarningMemoryEnvelope(cue.Card, new Vector2(7, 5), new Color(MemoryInk, .72f));
                }
                else if (cue.Mode == 2)
                {
                    if (progress > 0)
                        foreach (var bounds in EasyRoomPressure(new Rect2(cue.Points[0] - cue.Size * .5f, cue.Size)))
                            AttackTelegraph.Box(this, new Rect2(bounds.Position, new Vector2(bounds.Size.X * progress, bounds.Size.Y)),
                                new Color(warning, .85f));
                    DrawEasyMemoryOrigin(Field.Rect.Position + Vector2.One * 3, next);
                }
                else
                {
                    DrawEasyMemoryOrigin((cue.Points[0]).Clamp(Field.Rect.Position + Vector2.One * 3, Field.Rect.End - Vector2.One * 3), next);
                    if (progress > 0) DrawEasyMemoryRoute(cue.Points, progress, warning, cue.Radius);
                    if (cue.Mode == 4 && cue.Predecessor >= 0)
                    {
                        DrawEasyMemoryPort(cue.Points[0], AttackTelegraph.OrderedInk(new Color("dfc4df"), next));
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
                if (cue.Mode != 5 && (cue.Mode != 3 || cue.Bounds.Length == 1))
                    DrawLiveMemoryEnvelope(cue.Card, new Vector2(8, 5), new Color(MemoryInk));
            }
            else if (cue.Mode == 2)
            {
                var room = EasyRoomProtectedBounds(cue, Time - cue.FiresAt);
                foreach (var bounds in EasyRoomPressure(room)) DrawEasyMemoryBox(bounds, live, true);
                _memoryArt.Outline(room, new Color("fff0d9"), 1.2f);
                foreach (var door in EasyAirlockDoors(cue))
                    if (Time >= door.OpensAt - .4 && Time <= door.ClosesAt + .4)
                    {
                        DrawLiveMemoryPort(door.Entry.GetCenter(), new Color("efb8d7", .55f));
                        DrawLiveMemoryPort(door.Exit.GetCenter(), new Color("fff0d9"));
                    }
            }
            else
            {
                Vector2 at = cue.Position;
                DrawEasyMemoryDisc(at, cue.Radius, new Color("6d4b74", .9f));
                if (cue.Transferred && cue.ReleaseCue >= 0)
                    _memoryArt.Outline(new Rect2(at - Vector2.One * (cue.Radius - 2), Vector2.One * (cue.Radius - 2) * 2),
                        new Color("fff0d9"), .8f);
                if (cue.TransferredAt is double transferred && Time - transferred < .2)
                    DrawLiveMemoryPort(at, new Color("fff7e8"));
            }
        }
        ((ShaderMaterial)_memoryCanvas.Material).SetShaderParameter("model_frame", ProjectileTurntable.FrameAt(Time));
        _memoryCanvas.QueueRedraw();
        _memoryWarningCanvas.QueueRedraw();
    }

    private void DrawDraftRow(MinaEasyMemoryCue cue)
    {
        Rect2 row = cue.Bounds[0];
        Color ink = cue.Cancelled ? new Color("aee6ed") : new Color("ffadab");
        float y = row.GetCenter().Y;
        Vector2 mark = new(row.Position.X + 8, y);
        bool canReply = Time < cue.FiresAt && !_easyReadRounds.Contains(cue.Round);
        if (cue.Cancelled)
        {
            _memoryWarnings.Rect(row, new Color("358d93", .32f));
            _memoryWarnings.Outline(row, ink, .9f);
            _memoryWarnings.Line(mark + new Vector2(-3, 0), mark + Vector2.Down * 2, ink, 1.4f);
            _memoryWarnings.Line(mark + Vector2.Down * 2, mark + new Vector2(4, -3), ink, 1.4f);
            if (cue.CancelledAt is double replied && Time - replied < .7)
            {
                float spread = Mathf.Clamp((float)((Time - replied) / .7), 0, 1);
                Vector2 center = new(cue.Card.X, y);
                _memoryWarnings.Line(center, center.Lerp(new Vector2(row.Position.X, y), spread), ink, 1.2f);
                _memoryWarnings.Line(center, center.Lerp(new Vector2(row.End.X, y), spread), ink, 1.2f);
                AttackTelegraph.DrawSymbol(this, cue.Card, AttackTelegraph.Symbol.Received, ink);
            }
        }
        else
        {
            if (!UsesDangerForecast) _memoryWarnings.Rect(row, new Color("bd505e", .13f));
            _memoryWarnings.Line(mark - Vector2.One * 2.5f, mark + Vector2.One * 2.5f, ink, 1);
            _memoryWarnings.Line(mark + new Vector2(-2.5f, 2.5f), mark + new Vector2(2.5f, -2.5f), ink, 1);
            if (canReply && cue.AcceptsReply)
            {
                DrawWarningMemoryEnvelope(cue.Card, new Vector2(10, 7), new Color("fff0d9"));
                _memoryWarnings.Line(cue.Card, new Vector2(cue.Card.X, y), new Color(ink, .45f), .6f);
                _memoryWarnings.Outline(new Rect2(cue.Card - new Vector2(12, 9), new Vector2(24, 18)), new Color("fff0d9", .5f), .6f);
            }
        }
    }

    private void DrawEasyMemoryBox(Rect2 bounds, Color ink, bool live, bool hatched = true)
    {
        bounds = bounds.Intersection(Field.Rect);
        if (bounds.Size.X <= 0 || bounds.Size.Y <= 0) return;
        if (live)
        {
            Color body = new(Variant == 1 ? "263c53" : Variant == 2 ? "3f344d" : "3b294f", .70f);
            _memoryArt.Rect(bounds, body);
            float inset = Math.Min(7, Math.Min(bounds.Size.X, bounds.Size.Y) * .16f);
            // Clipping at the arena edge can collapse the bevel vertices onto the same point.
            if (inset >= .25f)
            {
                Vector2 a = bounds.Position, b = new(bounds.End.X, bounds.Position.Y);
                Vector2 c = bounds.End, d = new(bounds.Position.X, bounds.End.Y);
                Vector2 ai = a + Vector2.One * inset, bi = b + new Vector2(-inset, inset);
                Vector2 ci = c - Vector2.One * inset, di = d + new Vector2(inset, -inset);
                _memoryArt.Quad(a, b, bi, ai, new Color("c6c5ec", .16f));
                _memoryArt.Quad(a, ai, di, d, new Color("b9d3e6", .11f));
                _memoryArt.Quad(b, c, ci, bi, new Color("24172f", .3f));
                _memoryArt.Quad(d, di, ci, c, new Color("24172f", .35f));
                _memoryArt.Outline(bounds.Grow(-inset), new Color("ccb2e1", .26f), .4f);
            }
            DrawMemoryIllustrations(bounds);
            _memoryArt.Outline(bounds, new Color("1a102b"), 1.6f);
            _memoryArt.Outline(bounds, new Color(ink, .88f), .65f);
            float corner = Math.Min(5, Math.Min(bounds.Size.X, bounds.Size.Y) * .24f);
            foreach (Vector2 sign in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
            {
                Vector2 at = bounds.GetCenter() + sign * (bounds.Size * .5f - Vector2.One * .7f);
                _memoryArt.Line(at - new Vector2(sign.X * corner, 0), at, new Color("f5dcf1", .9f), .8f);
                _memoryArt.Line(at, at - new Vector2(0, sign.Y * corner), new Color("f5dcf1", .9f), .8f);
            }
        }
        else
        {
            AttackTelegraph.Box(this, bounds, ink, hatched);
        }
    }

    private void DrawMemoryIllustrations(Rect2 bounds)
    {
        bounds = bounds.Intersection(Field.Rect).Grow(-2);
        if (bounds.Size.X < 5 || bounds.Size.Y < 5) return;
        if (Variant == 0)
        {
            Vector2 textureSize = Vector2.One * ProjectileTurntable.CellSize;
            float scale = Math.Max(bounds.Size.X / textureSize.X, bounds.Size.Y / textureSize.Y);
            Vector2 crop = bounds.Size / scale;
            _memoryArt.Texture(bounds, new Rect2((textureSize - crop) * .5f / textureSize, crop / textureSize), Colors.White);
            return;
        }
        Vector2 size = Vector2.One * ProjectileTurntable.CellSize;
        size *= Math.Min(34 / Math.Max(size.X, size.Y), Math.Min((bounds.Size.X - 2) / size.X, (bounds.Size.Y - 2) / size.Y));
        Vector2 center = bounds.GetCenter();
        _memoryArt.Texture(new Rect2(center - size * .5f, size), new Rect2(0, 0, 1, 1), new Color(Colors.White, .8f));
        float gap = size.X * .5f + 6;
        if (bounds.Size.X > gap * 2 + 16)
            foreach (float side in new[] { -1f, 1f })
                _memoryArt.Line(center + Vector2.Right * side * gap,
                    new Vector2(side < 0 ? bounds.Position.X + 4 : bounds.End.X - 4, center.Y),
                    new Color("d5b9e5", .3f), .55f);
    }

    private void DrawEasyMemoryOrigin(Vector2 at, bool next)
    {
        AttackTelegraph.ProjectileOrigin(this, at, MemoryVisual, AttackTelegraph.OrderedInk(MemoryInk, next));
    }

    private void DrawEasyMemoryDisc(Vector2 center, float radius, Color fill)
    {
        for (float y = Math.Max(Field.Top, Mathf.Floor(center.Y - radius)); y < Math.Min(Field.Bottom, center.Y + radius); y++)
        {
            float dy = y + .5f - center.Y;
            if (Math.Abs(dy) > radius) continue;
            float chord = Mathf.Sqrt(radius * radius - dy * dy);
            float left = Math.Max(Field.Left, center.X - chord), right = Math.Min(Field.Right, center.X + chord);
            float light = Mathf.Clamp(.5f - dy / (radius * 2), 0, 1);
            Color surface = new Color("2d2445").Lerp(new Color("ba9bc7"), light * .72f);
            surface.A = fill.A;
            if (right > left) _memoryArt.Rect(new Rect2(left, y, right - left, Math.Min(1, Field.Bottom - y)), surface);
        }
        for (int i = 0; i < 48; i++)
        {
            Vector2 a = center + Vector2.FromAngle(i * Mathf.Tau / 48) * radius;
            Vector2 b = center + Vector2.FromAngle((i + 1) * Mathf.Tau / 48) * radius;
            if (EasyMemoryVisibleSegment(a, b) is { } visible)
            {
                _memoryArt.Line(visible.A, visible.B, new Color("21192e", .9f), 1.5f);
                float light = Mathf.Clamp(.55f - Vector2.FromAngle(i * Mathf.Tau / 48).Dot(new Vector2(.45f, .8f)), .18f, 1);
                _memoryArt.Line(visible.A, visible.B, new Color("f6ddec", light), .65f);
            }
        }
        DrawMemoryIllustrations(new Rect2(center - Vector2.One * radius * .9f, Vector2.One * radius * 1.8f));
        for (int step = 0; step < 12; step++)
        {
            Vector2 a = center + Vector2.FromAngle(Mathf.Pi * (1.10f + step * .035f)) * radius * .76f;
            Vector2 b = center + Vector2.FromAngle(Mathf.Pi * (1.10f + (step + 1) * .035f)) * radius * .76f;
            if (EasyMemoryVisibleSegment(a, b) is { } shine)
                _memoryArt.Line(shine.A, shine.B, new Color("fff0ed", .55f), .65f);
        }
    }


    private void DrawEasyMemoryPort(Vector2 at, Color ink)
    {
        Vector2[] corners = { at + Vector2.Up * 3, at + Vector2.Right * 2.2f,
            at + Vector2.Down * 3, at + Vector2.Left * 2.2f };
        _memoryWarnings.Quad(corners[0], corners[1], corners[2], corners[3], new Color("211b31", .9f));
        for (int i = 0; i < 4; i++) _memoryWarnings.Line(corners[i], corners[(i + 1) % 4], ink, .65f);
    }

    private void DrawLiveMemoryPort(Vector2 at, Color ink)
    {
        var bounds = new Rect2(at - Vector2.One * 4, Vector2.One * 8);
        _memoryArt.Rect(bounds, new Color("1d1933", .85f));
        _memoryArt.Outline(bounds, ink, .9f);
        _memoryArt.Line(at + new Vector2(-2, -2), at + new Vector2(2, 2), ink, .8f);
        _memoryArt.Line(at + new Vector2(-2, 2), at + new Vector2(2, -2), ink, .8f);
    }

    private void DrawLiveMemoryEnvelope(Vector2 at, Vector2 size, Color ink)
    {
        var bounds = new Rect2(at - size * .5f, size);
        _memoryArt.Outline(bounds, ink, .55f);
        _memoryArt.Line(bounds.Position, at + Vector2.Down * size.Y * .1f, ink, .55f);
        _memoryArt.Line(new Vector2(bounds.End.X, bounds.Position.Y), at + Vector2.Down * size.Y * .1f, ink, .55f);
    }

    private void DrawWarningMemoryEnvelope(Vector2 at, Vector2 size, Color ink)
    {
        var bounds = new Rect2(at - size * .5f, size);
        _memoryWarnings.Outline(bounds, ink, .55f);
        _memoryWarnings.Line(bounds.Position, at + Vector2.Down * size.Y * .1f, ink, .55f);
        _memoryWarnings.Line(new Vector2(bounds.End.X, bounds.Position.Y), at + Vector2.Down * size.Y * .1f, ink, .55f);
    }

    private sealed class MemoryArtBatch
    {
        private readonly List<Vector2> _points = new(), _uvs = new();
        private readonly List<Color> _colors = new();
        private readonly List<int> _indices = new();

        public void Clear()
        {
            _points.Clear();
            _uvs.Clear();
            _colors.Clear();
            _indices.Clear();
        }

        public void Draw(Node2D canvas)
        {
            if (_indices.Count == 0) return;
            RenderingServer.CanvasItemAddTriangleArray(canvas.GetCanvasItem(),
                CollectionsMarshal.AsSpan(_indices), CollectionsMarshal.AsSpan(_points),
                CollectionsMarshal.AsSpan(_colors), CollectionsMarshal.AsSpan(_uvs),
                ReadOnlySpan<int>.Empty, ReadOnlySpan<float>.Empty, default, -1);
        }

        private void Vertex(Vector2 point, Color color, Vector2 uv)
        {
            _points.Add(point);
            _colors.Add(color);
            _uvs.Add(uv);
        }

        public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int start = _points.Count;
            Vertex(a, color, -Vector2.One);
            Vertex(b, color, -Vector2.One);
            Vertex(c, color, -Vector2.One);
            _indices.Add(start);
            _indices.Add(start + 1);
            _indices.Add(start + 2);
        }

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
            => Quad(a, b, c, d, color, color, color, color, new Rect2(-Vector2.One, Vector2.Zero));

        private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color ca, Color cb, Color cc, Color cd, Rect2 uv)
        {
            int start = _points.Count;
            Vertex(a, ca, uv.Position);
            Vertex(b, cb, new Vector2(uv.End.X, uv.Position.Y));
            Vertex(c, cc, uv.End);
            Vertex(d, cd, new Vector2(uv.Position.X, uv.End.Y));
            _indices.Add(start);
            _indices.Add(start + 1);
            _indices.Add(start + 2);
            _indices.Add(start);
            _indices.Add(start + 2);
            _indices.Add(start + 3);
        }

        public void Rect(Rect2 bounds, Color color)
            => Quad(bounds.Position, new Vector2(bounds.End.X, bounds.Position.Y),
                bounds.End, new Vector2(bounds.Position.X, bounds.End.Y), color);

        public void Texture(Rect2 bounds, Rect2 uv, Color color)
            => Quad(bounds.Position, new Vector2(bounds.End.X, bounds.Position.Y),
                bounds.End, new Vector2(bounds.Position.X, bounds.End.Y), color, color, color, color, uv);

        public void Outline(Rect2 bounds, Color color, float width)
        {
            Vector2 a = bounds.Position, b = new(bounds.End.X, bounds.Position.Y);
            Vector2 c = bounds.End, d = new(bounds.Position.X, bounds.End.Y);
            Line(a, b, color, width);
            Line(b, c, color, width);
            Line(c, d, color, width);
            Line(d, a, color, width);
        }

        public void Line(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 normal = a.DirectionTo(b).Orthogonal();
            Vector2 inner = normal * width * .5f, outer = normal * (width * .5f + .2f);
            Quad(a + inner, b + inner, b - inner, a - inner, color);
            Color transparent = new(color, 0);
            var uv = new Rect2(-Vector2.One, Vector2.Zero);
            Quad(a + outer, b + outer, b + inner, a + inner, transparent, transparent, color, color, uv);
            Quad(a - inner, b - inner, b - outer, a - outer, color, color, transparent, transparent, uv);
        }

        public void Dashed(Vector2 a, Vector2 b, Color color, float width, float dash, float travelled = 0)
        {
            float length = a.DistanceTo(b);
            Vector2 direction = a.DirectionTo(b);
            for (float at = -(travelled % (dash * 2)); at < length; at += dash * 2)
                if (at + dash > 0)
                    Line(a + direction * Math.Max(0, at), a + direction * Math.Min(at + dash, length), color, width);
        }

        public void Circle(Vector2 center, float radius, Color color, float width)
        {
            Vector2 previous = center + Vector2.Right * radius;
            for (int i = 1; i < 40; i++)
            {
                Vector2 next = center + Vector2.FromAngle(i * Mathf.Tau / 39) * radius;
                Line(previous, next, color, width);
                previous = next;
            }
        }
    }

    private void DrawEasyMemoryRoute(Vector2[] points, float progress, Color ink, float radius = 0, Vector2 offset = default, bool directionMarks = true)
    {
        float remaining = 0;
        for (int i = 1; i < points.Length; i++) remaining += points[i - 1].DistanceTo(points[i]);
        remaining *= progress;
        float travelled = 0, nextArrow = 24;
        Vector2 shownEnd = points[0] + offset;
        for (int i = 1; i < points.Length && remaining > 0; i++)
        {
            float length = points[i - 1].DistanceTo(points[i]);
            Vector2 start = points[i - 1] + offset;
            Vector2 end = start.MoveToward(points[i] + offset, Math.Min(remaining, length));
            shownEnd = end;
            float shownLength = start.DistanceTo(end);
            Vector2 direction = start.DirectionTo(end), side = new(-direction.Y, direction.X);
            void Segment(Vector2 a, Vector2 b, bool boundary = false)
            {
                if (EasyMemoryVisibleSegment(a, b) is { } visible)
                {
                    float dash = AttackTelegraph.IsLater(ink) ? 16 : 12;
                    float phase = travelled + a.DistanceTo(visible.A);
                    if (boundary)
                    {
                        _memoryWarnings.Line(visible.A, visible.B, new Color(AttackTelegraph.Outline, ink.A * .6f), 1.5f);
                        _memoryWarnings.Line(visible.A, visible.B, ink, .65f);
                    }
                    else
                    {
                        _memoryWarnings.Dashed(visible.A, visible.B, new Color(AttackTelegraph.Outline, ink.A * .5f), 1.3f, dash, phase);
                        _memoryWarnings.Dashed(visible.A, visible.B, new Color(ink, ink.A * .65f), .5f, dash, phase);
                    }
                }
            }
            Segment(start, end);
            if (radius > 0)
            {
                Vector2 from = start;
                _memoryWarnings.Line(from, end, new Color(ink, ink.A * .08f), radius * 2);
                for (int edge = 0; edge < 4; edge++)
                    if (WarningEdgeSpan(from, end, radius, edge) is { } span && span.A.DistanceSquaredTo(span.B) > .01f)
                    {
                        _memoryWarnings.Line(span.A, span.B, new Color("251321", .5f), 1.5f);
                        _memoryWarnings.Line(span.A, span.B, ink, .65f);
                    }
                Segment(start + side * radius, end + side * radius, true);
                Segment(start - side * radius, end - side * radius, true);
            }
            while (directionMarks && nextArrow < travelled + shownLength)
            {
                Vector2 at = start + direction * (nextArrow - travelled);
                nextArrow += 96;
                if (!Field.Rect.Grow(-4).HasPoint(at)) continue;
                Vector2 left = at - direction * 2.5f + direction.Orthogonal() * 1.7f;
                Vector2 right = at - direction * 2.5f - direction.Orthogonal() * 1.7f;
                _memoryWarnings.Line(left, at, new Color(AttackTelegraph.Outline, ink.A * .6f), 1.5f);
                _memoryWarnings.Line(at, right, new Color(AttackTelegraph.Outline, ink.A * .6f), 1.5f);
                _memoryWarnings.Line(left, at, ink, .65f);
                _memoryWarnings.Line(at, right, ink, .65f);
            }
            travelled += shownLength;
            remaining -= length;
        }
        if (radius > 0)
            foreach (Vector2 ghost in new[] { points[0] + offset, shownEnd })
                if (Field.Rect.Grow(-radius).HasPoint(ghost))
                {
                    _memoryWarnings.Circle(ghost, radius, new Color(AttackTelegraph.Outline, ink.A * .3f), 1.5f);
                    _memoryWarnings.Circle(ghost, radius, new Color(ink, ink.A * .55f), .6f);
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

    protected override bool DrawCustomBox(Node2D canvas, Rect2 rect, bool live, float progress)
    {
        if (_id != 15) return false;
        if (!live)
        {
            Color cue = new Color("91dae8", .46f + .28f * progress);
            canvas.DrawDashedLine(rect.Position, new Vector2(rect.Position.X, rect.End.Y), cue, .8f, 3, true);
            canvas.DrawDashedLine(new Vector2(rect.End.X, rect.Position.Y), rect.End, cue, .8f, 3, true);
            Vector2 origin = new(rect.GetCenter().X, Field.Top + 5);
            canvas.DrawArc(origin, 3, 0, Mathf.Tau, 12, cue, .8f, true);
            AttackTelegraph.Arrow(canvas, origin + Vector2.Down * 8, Vector2.Down, cue);
            return true;
        }

        // The sheets and binding occupy the solid silhouette down to its last visible row.
        canvas.DrawRect(rect, new Color("f1ddb8"));
        canvas.DrawRect(rect, new Color("594055"), false, 1);
        canvas.DrawLine(rect.Position + new Vector2(2, 3), new Vector2(rect.End.X - 2, rect.Position.Y + 3), new Color("ae8494"), 1);
        canvas.DrawLine(new Vector2(rect.Position.X + 2, rect.End.Y - 3), rect.End - new Vector2(2, 3), new Color("ae8494"), 1);
        canvas.DrawRect(new Rect2(rect.GetCenter().X - 2, rect.Position.Y, 4, rect.Size.Y), new Color("995675"));
        return true;
    }

    protected override void DrawTechnique()
    {
        if (_id == 18)
        {
            if (!HazardShapes.Any()) return;
            bool live = Time >= _warning;
            Color ink = new("a5e8c5");
            Vector2 center = Boss.GlobalPosition;
            if (live || GameManager.ShowAttackTelegraphs)
            {
                if (live && UsesDangerForecast) DrawCircle(center, ReturnInner, new Color("416859", .72f));
                Color fieldInk = new(ink.Darkened(.55f), live ? (UsesDangerForecast ? .65f : .4f) : (UsesDangerForecast ? 0 : .1f));
                for (float y = Field.Top; y < Field.Bottom; y += 2)
                {
                    float dy = y + 1 - center.Y;
                    float half = Math.Abs(dy) < ReturnOuter ? Mathf.Sqrt(ReturnOuter * ReturnOuter - dy * dy) : 0;
                    float left = Mathf.Clamp(center.X - half, Field.Left, Field.Right);
                    float right = Mathf.Clamp(center.X + half, Field.Left, Field.Right);
                    DrawRect(new Rect2(Field.Left, y, left - Field.Left, 2), fieldInk);
                    DrawRect(new Rect2(right, y, Field.Right - right, 2), fieldInk);
                }
                for (int i = 0; i < 48; i++)
                {
                    float angle = i * Mathf.Tau / 48;
                    float span = live ? Mathf.Tau / 48 : .07f;
                    DrawArc(center, ReturnOuter, angle, angle + span, 4, AttackTelegraph.Outline, 2, true);
                    DrawArc(center, ReturnOuter, angle, angle + span, 4, ink, .8f, true);
                }
                if (!live)
                {
                    Vector2 direction = center.DirectionTo(Player.GlobalPosition);
                    DrawDashedLine(center + direction * ReturnInner, Player.GlobalPosition, new Color(ink, .4f), .65f, 4, true);
                    AttackTelegraph.DrawSymbol(this, Player.GlobalPosition, AttackTelegraph.Symbol.Target, ink);
                }
            }
            DrawBodyContact(ReturnInner, ink, live);
            return;
        }
        if (Kind == BossTechniqueKind.Area)
        {
            DrawEasyMemory();
            return;
        }
        if (!GameManager.ShowAttackTelegraphs && _id is not (11 or 13)) return;
        DrawMemoryCues();
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
        if (_id == 17)
        {
            DrawLine(WingCenter - Vector2.Right * 34, WingCenter - Vector2.Right * 65, MemoryInk, 1.2f, true);
            DrawLine(WingCenter + Vector2.Right * 34, WingCenter + Vector2.Right * 65, MemoryInk, 1.2f, true);
        }
    }

    private void DrawMemoryCues()
    {
        var ink = new Color(MemoryInk, .85f);
        var quiet = new Color(MemoryInk, .45f);
        var received = new Color("c4e3ec", .85f);
        float t = (float)(Time - _warning);
        switch (_id)
        {
            case 0:
            {
                var cue = VisibleCacheSlide;
                for (int cell = 0; cell < 9; cell++)
                {
                    if (cell == cue.Slide.From || cell == cue.Slide.To) continue;
                    DrawMemoryEnvelope(CacheCell(cell).GetCenter(), new Vector2(20, 13), quiet);
                }
                var moving = MovingCacheBounds(cue.Slide, cue.Age);
                DrawMemoryEnvelope(moving.GetCenter(), new Vector2(Math.Min(20, moving.Size.X * .4f), Math.Min(13, moving.Size.Y * .4f)), received);
                if (cue.Age < .6)
                {
                    Vector2 from = CacheCell(cue.Slide.To).GetCenter(), to = CacheCell(cue.Slide.From).GetCenter();
                    DrawDashedLine(from, to, quiet, .7f, 4, true);
                    AttackTelegraph.Arrow(this, from.Lerp(to, .5f), from.DirectionTo(to), received);
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
                    float y = Field.Top + (i + .5f) * Field.Height / (2 + Tier);
                    DrawPort(new Vector2(Field.Left + 4, y), Vector2.Right, received);
                    DrawPort(new Vector2(Field.Right - 4, y), Vector2.Left, ink);
                    AttackTelegraph.Pips(this, new Vector2(Field.Left + 16, y + 12), i + 1, received);
                    AttackTelegraph.Pips(this, new Vector2(Field.Right - 16, y + 12), i + 1, ink);
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
                    if (_cells[i] && !_nextCells[i]) AttackTelegraph.DrawSymbol(this, at, AttackTelegraph.Symbol.Undo, received);
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
                foreach (var wave in VisibleSpringWaves)
                {
                    var nodes = CompressedSpringNodes(wave.Front);
                    float y = wave.Top + Field.Height / 4;
                    for (int i = 1; i < nodes.Length; i++)
                    {
                        Vector2 a = new(nodes[i - 1], y), b = new(nodes[i], y);
                        for (int zig = 0; zig < 4; zig++)
                            DrawLine(a.Lerp(b, zig / 4f) + Vector2.Down * (zig % 2 == 0 ? -3 : 3),
                                a.Lerp(b, (zig + 1) / 4f) + Vector2.Down * (zig % 2 == 0 ? 3 : -3), quiet, .7f, true);
                        DrawCircle(b, 2, received);
                    }
                }
                break;
            case 10:
            {
                DrawLine(new Vector2(Field.CenterX, Field.Top), new Vector2(Field.CenterX, Field.Bottom), quiet, .7f, true);
                foreach (var band in _snapshotBands)
                {
                    var bounds = SnapshotBounds(band.Side, band.Y);
                    DrawMemoryEnvelope(bounds.GetCenter(), new Vector2(16, 10), received);
                    DrawLine(bounds.GetCenter() + Vector2.Down * Math.Sign(band.Velocity) * 14,
                        bounds.GetCenter() + Vector2.Down * Math.Sign(band.Velocity) * 18, quiet, 1, true);
                }
                AttackTelegraph.DrawSymbol(this, new Vector2(Field.CenterX, 18), _exchangePending ? AttackTelegraph.Symbol.Swap : AttackTelegraph.Symbol.Record, received);
                break;
            }
            case 11:
                if (Tier >= 2)
                    foreach (var mark in VisibleVisitMarks)
                    {
                        DrawRect(mark.Bounds, new Color(MemoryInk, .22f), false, .5f);
                        if (mark.Visits == 1 && mark.Imprint > 0)
                            DrawRect(new Rect2(mark.Bounds.GetCenter() + new Vector2(1, 6), new Vector2(4, 7)),
                                new Color(received, mark.Imprint * .8f));
                    }
                for (int i = 0; i < 35; i++)
                {
                    if (_visits[i] == 0) continue;
                    Vector2 at = new(Field.Left + (i % 7 + .5f) * Field.Width / 7, (i / 7 + .5f) * Field.Height / 5);
                    int stamps = Math.Min(_visits[i], 2);
                    if (stamps == 1)
                    {
                        var size = new Vector2(Field.Width / 7, Field.Height / 5);
                        var tint = new Color(received, Mathf.Clamp((float)(Time - _firstStepAt[i]) / .45f, 0, .75f));
                        DrawDashedLine(at + new Vector2(-.42f, -.43f) * size, at + new Vector2(.42f, -.43f) * size, tint, 1, 3);
                        DrawDashedLine(at + new Vector2(-.42f, .43f) * size, at + new Vector2(.42f, .43f) * size, tint, 1, 3);
                        AttackTelegraph.DrawSymbol(this, at, AttackTelegraph.Symbol.Footprint, tint);
                    }
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
                foreach (var star in _unansweredStars)
                    DrawMemoryEnvelope(star.At, new Vector2(8, 5), Time < star.WarnedAt + UnansweredLead ? quiet : received);
                if (StarReplies.LastOrDefault() is { } reply)
                {
                    Vector2 target = reply.Target;
                    for (int point = 0; point < 4; point++)
                    {
                        Vector2 axis = Vector2.FromAngle(point * Mathf.Pi / 2);
                        DrawLine(target + axis * 3, target + axis * 8, received, .8f, true);
                    }
                    DrawMemoryEnvelope(target + new Vector2(10, -10), new Vector2(7, 5), received);
                }
                Vector2 letters = new(Field.CenterX, 22);
                for (int envelope = 0; envelope < 3; envelope++)
                    DrawMemoryEnvelope(letters + Vector2.Right * (envelope - 1) * 24, new Vector2(8, 6),
                        envelope < StarReplies.Count ? received : quiet);
                DrawReplyInvitation(letters + Vector2.Down * 18, received);
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
                for (int mark = 0; mark < _burdenTurnMarks.Count; mark++)
                    DrawMemoryEnvelope(new Vector2(_burdenTurnMarks[mark], Field.Top + 10 + mark * 8), new Vector2(9, 6), received);
                if (t >= SwingEnd) break;
                Vector2 pivot = BurdenPivot;
                Vector2 weight = VisibleBurdenWeight;
                if (t < 0)
                    DrawArc(pivot, BurdenLength, Mathf.Pi / 2 - BurdenSwing, Mathf.Pi / 2 + BurdenSwing, 80, quiet, .7f, true);
                AttackTelegraph.Stroke(this, new[] { pivot, weight }, ink, .55f);
                if (Tier == 3) DrawMemoryEnvelope(weight, new Vector2(18 + Tier * 2, 13 + Tier), quiet);
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
                        AttackTelegraph.DrawSymbol(this, new Vector2(Field.Right - 22, y + 3), AttackTelegraph.Symbol.Undo, received);
                    }
                    else if (t < 1.4)
                        for (int word = 0; word < 4; word++)
                            DrawLine(new Vector2(Field.Left + 57 + word * 48, y - 5), new Vector2(Field.Left + 80 + word * 48, y - 5), quiet, .6f, true);
                }
                break;
            }
            case 17:
            {
                int weak = WingWeakSide(Time - _warning);
                for (int side = -1; side <= 1; side += 2)
                for (int column = 0; column < 6; column++)
                for (int row = 0; row < 6; row++)
                {
                    float flow = side == weak ? 12 : 38;
                    float drift = Mathf.PosMod((float)Time * flow + column * 26, Field.Width / 2 - 16);
                    Vector2 at = new(Field.CenterX + side * (8 + drift), 16 + row * 36);
                    DrawLine(at - Vector2.Right * side * (4 + flow * .15f), at,
                        new Color(MemoryInk, side == weak ? .22f : .55f), .8f, true);
                }
                if (VisibleWingSwitch is { } change)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        bool stronger = side != change.WeakSide;
                        Vector2 root = new(WingCenter.X + side * 42, Field.Top + 22);
                        Color cueInk = new(MemoryInk, .9f);
                        for (int feather = -1; feather <= 1; feather++)
                        {
                            Vector2 tip = root + new Vector2(side * (stronger ? 24 : 12), feather * (stronger ? 8 : 4));
                            DrawDashedLine(root, tip, AttackTelegraph.Outline, 2, 3, true);
                            DrawDashedLine(root, tip, cueInk, .8f, 3, true);
                        }
                        AttackTelegraph.DrawSymbol(this, root + Vector2.Down * 21, AttackTelegraph.Symbol.Speed, cueInk, amount: stronger ? 1 : .2f, count: stronger ? 3 : 1, direction: Vector2.Right * side);
                    }
                }
                break;
            }
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
        if (_id is 0 or 10 or 13 or 15)
        {
            var art = WarningVisual;
            if (Time < _warning)
                AttackTelegraph.ProjectileOrigin(this, at, art, art.Accent, Math.Max(size.X, size.Y) * .5f);
            else
            {
                float scale = Math.Min(size.X / art.Region.Size.X, size.Y / art.Region.Size.Y);
                Vector2 extent = art.Region.Size * scale;
                DrawTextureRect(art.Cropped, new Rect2(at - extent * .5f, extent), false, Colors.White);
            }
            return;
        }
        var rect = new Rect2(at - size * .5f, size);
        DrawRect(rect, new Color("251e34", .65f));
        DrawRect(rect, AttackTelegraph.Outline, false, 1.6f);
        DrawRect(rect, ink, false, .55f);
        DrawLine(rect.Position, at + Vector2.Down * size.Y * .1f, ink, .55f, true);
        DrawLine(new Vector2(rect.End.X, rect.Position.Y), at + Vector2.Down * size.Y * .1f, ink, .55f, true);
    }

    private void DrawPort(Vector2 at, Vector2 direction, Color ink)
    {
        Vector2 normal = direction.Orthogonal();
        AttackTelegraph.Stroke(this, new[] { at - normal * 4 + direction * 5, at - normal * 4,
            at + normal * 4, at + normal * 4 + direction * 5 }, ink);
    }

    private void DrawReplyInvitation(Vector2 at, Color ink)
    {
        bool ready = ReplyReady;
        AttackTelegraph.DrawSymbol(this, at, ready ? AttackTelegraph.Symbol.Inbox : AttackTelegraph.Symbol.Outbox, ink);
        if (!ready) return;
        float phase = Mathf.PosMod((float)Time * .8f, 1);
        Vector2 mote = at + Vector2.Down * Mathf.Lerp(15, 4, phase);
        DrawCircle(mote, 1.5f, new Color(ink, .8f));
        DrawLine(mote + Vector2.Down * 4, mote, new Color(ink, .5f), .8f, true);
    }
}
