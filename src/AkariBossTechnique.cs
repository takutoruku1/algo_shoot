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
        public Vector2 Origin, Direction, TurnPoint, PreviousTurnPoint;
        public double Born, Changed, PreviousChanged;
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
    private static BulletArt.PlayerVisual? _easyRainShot, _easyUnbindingShot;
    private static readonly Color Paper = new("ffe6a2"), Ink = new("b3e4eb");
    private const float EasyUnbindingScale = 3;
    private double ClipCycle => Tier switch { 2 => 2.95, 3 => 2.75, _ => 3.2 };
    private double ClipLead => Tier switch { 2 => 1.35, 3 => 1.25, _ => 1.5 };
    private const int EasyPileColumns = 7, EasyPileRows = 3, EasyRainWaves = 2, EasyRainColumns = 9, EasyRainRows = 3;
    private const float EasyPileWidth = 26, EasyPileSpeed = 120;
    private float EasyPileLayerHeight => 18 + Tier * 2;
    private const double EasyPileRowInterval = .14, EasyPileColumnInterval = .03,
        EasyPileCollapseAt = 2.1, EasyPileCollapseInterval = .16;
    private const double EasyRainWaveInterval = 11.4, EasyRainRowInterval = 3.6;
    private const double EasyInboxWarningLead = 3.2;
    private int EasyDeliveryBursts => 3 + Tier;
    private int EasyInboxBursts => 7 + Tier * 2;
    private int EasyRainBursts => 3 + Tier;
    private double EasyDeliveryBurstInterval => .32 - Tier * .03;
    private double EasyInboxBurstInterval => .18 - Tier * .015;
    private double EasyRainBurstInterval => .25 - Tier * .02;
    private const float EasyRainSpeed = 76, EasySplashRise = 68, EasySplashGravity = 48;
    private const int EasyWaveSegments = 64, EasyWaveSpentSegments = 20;
    private const float EasyWaveSpeed = 28, EasyWaveWaitRadius = 60;
    private float EasyWaveOuterSpeed => 144 + Tier * 12;
    private const double EasyWaveOpeningAt = 1.1,
        EasyWaveAccelerationTime = .45, EasyWaveRest = .08;
    private readonly double[] _easyWaveWarningStarts = { -1, -1 }, _easyWaveLaunches = { -1, -1 };
    private readonly Vector2[] _easyWaveWaitPoints = new Vector2[2];
    private readonly Vector2[] _easyDeliveryTargets = new Vector2[3], _easyInboxTargets = new Vector2[4];
    private readonly bool[] _easyDeliveryReserved = new bool[3], _easyInboxReserved = new bool[4];
    internal int? VisibleDangerousShotsAtNaturalEnd { get; private set; }

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
            0 => ("速達の差戻し", "akari.returned-express", "追越し点で横折れする速達と、壁から戻る返信を順にかわす"),
            1 => ("ふたつの焦がれる扇火", "akari.alternating-inboxes", "片方の発射が終わった側へ切り返す"),
            2 => ("綴じた下書きの取消", "akari.bound-draft-retraction", "複写の取消が端に届くとクリップが外れる。予約された進路から離れる"),
            3 => ("切り返す紅蓮の織目", "akari.unsent-history", "複写された経路の取消順を読み、紙面の間へ移る"),
            4 => ("追いかける返信の針路", "akari.wall-reply", "壁で一度反射する返信の先まで読む"),
            5 => ("降り積もる未送信", "akari.filing-piles", "床の書類が崩れた場所を使い、次の積み上がりを避ける"),
            6 => ("豪雨予報", "akari.rain-splash", "雨柱の着地後、床から横へ飛ぶしぶきも越す"),
            7 => ("沈黙の波紋", "akari.spent-wavefront", "中心から離れ、外へ伝わる波が薄れたら内側へ入り直す"),
            8 => ("狐火・三尾の便り", "akari.braided-delivery", "三つの束の前後を読み、ほどけて飛ぶ束を見送る"),
            9 => ("逃がさない締切", "akari.dwell-timecard", "滞在で早まる締切を読み、確定した印影の余白へ移る"),
            10 => ("交差する焦燥", "akari.exchanged-priority", "速い便と遅い便の交換後の到着順で横断する"),
            11 => ("巡回する承認待ち", "akari.carried-approval", "角に届いた案件の開封を避け、予約された承認印の外へ移る"),
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
            _origin = new Vector2(Field.CenterX, Field.CenterY);
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
        if (_mechanic is 0 or 1 or 2 or 5 or 6 or 11 or 12)
            _warning = Math.Max(_warning, _mechanic is 0 or 11 or 12 ? 3.6 : 3.2);
        if (_mechanic == 7)
        {
            _angle = (_aim - _origin).Angle();
            PrepareEasyWave(0, 0);
            _warning = _easyWaveLaunches[0];
        }
        if (UsesEasyReadabilityRules && _mechanic != 6) _next = _warning + (_mechanic == 9 ? 1.6 : 0);
        if (_mechanic == 2) PrepareEasyUnbinding();
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
            case 11: CarriedEnvelope(delta); break;
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
        if (UsesCustomHazardArt && _mechanic == 6)
            shot.UseBossIllustration(EasyRainShotVisual());
        if (_mechanic == 2) shot.UseBossIllustration(EasyUnbindingShotVisual());
        if (UsesCustomHazardArt)
        {
            shot.ZIndex = 3;
            shot.ZAsRelative = false;
        }
        if (_mechanic == 2) shot.Scale = Vector2.One * EasyUnbindingScale;
        shot.Accel = false;
        shot.Velocity = velocity;
        var letter = new Letter { Shot = shot, Activation = shot.ActivationId, Origin = at,
            Direction = velocity.Normalized(), Born = Time, Group = group };
        _letters.Add(letter);
        return letter;
    }
    private void EndAfter(double duration)
    {
        if (Time <= duration) return;
        if (UsesEasyReadabilityRules)
        {
            var remaining = OwnedShots.ToArray();
            int visible = remaining.Count(shot =>
            {
                float radius = shot.CollisionRadius * Math.Max(shot.GlobalTransform.X.Length(), shot.GlobalTransform.Y.Length());
                return !shot.LeadIn && shot.IsEnemy && shot.CollisionLayer != 0
                    && new Rect2(shot.GlobalPosition - Vector2.One * radius, Vector2.One * radius * 2)
                        .Intersects(Field.Rect.Grow(Player.HitRadius));
            });
            if (remaining.Length > 0 || HazardShapes.Any(hazard => hazard.Live)
                || _mechanic == 9 && _stamps.Any(stamp => Time < stamp.At + .6)) return;
            VisibleDangerousShotsAtNaturalEnd = visible;
        }
        Finish();
    }
    private float Progress(double start, double duration) => Mathf.Clamp((float)((Time - start) / duration), 0, 1);

    private float EasyWarningRouteProgress(double start, double grow = .35) => Progress(start + .2, grow);
    private float EasyDeliveryWarningProgress(int batch) => Progress(EasyDeliveryWarningStart(batch) + .4, 1.2);
    private float EasyInboxWarningProgress(int turn) => Progress(EasyInboxLaunch(turn) - EasyInboxWarningLead + .4, .8);
    private (float Inward, float Outward) EasyClipWarningProgress() => (Progress(.2, .25), Progress(.45, .3));
    private (float Incoming, float Outgoing) EasyReplyWarningProgress() => (Progress(.2, .45), Progress(.9, .45));
    private float EasyPileWarningProgress(int column) => Progress(EasyPileStart(column) - _warning + .4, .8);
    private (float Ring, float Expiry) EasyWaveWarningProgress(int wave)
        => (Progress(_easyWaveWarningStarts[wave] + .4, .8), Progress(_easyWaveWarningStarts[wave] + 1.2, .5));
    private float EasyEnvelopeTravelWarningProgress() => EasyWarningRouteProgress(0, 1.4);
    private float EasyEnvelopeOpeningWarningProgress(int opening) => EasyWarningRouteProgress(EasyApprovalWarningStart(opening));
    private float EasyClockWarningProgress() => Progress(.4, 1.2);

    private const int EasyHistoryCopies = 12;
    private double EasyHistoryRecording => 2.6;
    private double EasyHistoryWarningStart(int copy) => EasyHistoryRecording + copy * .1;
    private double EasyHistoryFire(int copy, int index)
        => EasyHistoryRecording + _warning + copy * .12 + (_history.Count - index) * .13;
    private double EasyHistoryEnd() => EasyHistoryFire(EasyHistoryCopies - 1, 1) + .75;
    private float EasyHistoryCopyWarningProgress(int copy) => EasyWarningRouteProgress(EasyHistoryWarningStart(copy));

    private Vector2[] EasyHistoryCopyPoints(int copy)
    {
        float minX = _history.Min(point => point.X), maxX = _history.Max(point => point.X);
        float minY = _history.Min(point => point.Y), maxY = _history.Max(point => point.Y);
        Vector2 cell = new(Field.Width / 4, Field.Height / 3);
        Vector2 center = new(Field.Left + (copy % 4 + .5f) * cell.X, Field.Top + (copy / 4 + .5f) * cell.Y);
        Vector2 sourceCenter = new((minX + maxX) * .5f, (minY + maxY) * .5f);
        float scale = Math.Min(1, Math.Min((cell.X - 24) / Math.Max(1, maxX - minX),
            (cell.Y - 24) / Math.Max(1, maxY - minY)));
        return _history.Select(point => center + (point - sourceCenter) * scale).ToArray();
    }

    private float EasyHistoryCopySegmentWarningProgress(int copy, int index)
    {
        float total = 0, before = 0;
        for (int i = _history.Count - 1; i > 0; i--)
        {
            float length = _history[i].DistanceTo(_history[i - 1]);
            total += length;
            if (i > index) before += length;
        }
        float segment = _history[index].DistanceTo(_history[index - 1]);
        float progress = EasyHistoryCopyWarningProgress(copy);
        return segment == 0 ? progress : Mathf.Clamp((total * progress - before) / segment, 0, 1);
    }

    private static BulletArt.PlayerVisual EasyRainShotVisual()
    {
        if (_easyRainShot != null) return _easyRainShot;
        var texture = GD.Load<Texture2D>("res://char/v3/fx/akari/easy_rain_drop_v1.png");
        Rect2 region = UiKit.ContentRect(texture);
        return _easyRainShot = new BulletArt.PlayerVisual(texture, region, region.GetCenter(), new Color("b3e4eb"));
    }

    private static BulletArt.PlayerVisual EasyUnbindingShotVisual()
    {
        if (_easyUnbindingShot != null) return _easyUnbindingShot;
        var texture = GD.Load<Texture2D>("res://char/v3/bullets/akari_clip.png");
        Rect2 region = UiKit.ContentRect(texture);
        return _easyUnbindingShot = new BulletArt.PlayerVisual(texture, region, region.GetCenter(), Ink);
    }

    private float EasyWarningHalfWidth(int pattern)
    {
        var art = _mechanic == 6 ? EasyRainShotVisual() : _mechanic == 2
            ? EasyUnbindingShotVisual() : BulletArt.BossPattern(Job.Melee, pattern);
        float radius = Evolved ? BulletPool.BossChargeRadius : BulletPool.BossRadius;
        return BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position, Bullet.BossArtSize(radius, Evolved)).Radius
            * (_mechanic == 2 ? EasyUnbindingScale : 1);
    }

    private Vector2 EasyRayExit(Vector2 from, Vector2 direction)
    {
        direction = direction.Normalized();
        var bounds = Field.Rect.Grow(24);
        float exit = float.PositiveInfinity;
        if (direction.X != 0)
            exit = Math.Min(exit, ((direction.X > 0 ? bounds.End.X : bounds.Position.X) - from.X) / direction.X);
        if (direction.Y != 0)
            exit = Math.Min(exit, ((direction.Y > 0 ? bounds.End.Y : bounds.Position.Y) - from.Y) / direction.Y);
        return from + direction * exit;
    }

    private Vector2 EasyClipReleasePoint(int group)
    {
        float radius = Math.Max(8, 45 - group / 3 * .9f * 8);
        return _origin + Vector2.FromAngle(group * Mathf.Tau / 18) * radius;
    }

    private IEnumerable<(Vector2 Start, Vector2 Release, Vector2 Exit)> EasyClipWarningPaths()
    {
        for (int group = 0; group < 18; group++)
        {
            Vector2 direction = Vector2.FromAngle(group * Mathf.Tau / 18), release = EasyClipReleasePoint(group);
            yield return (_origin + direction * 45, release, EasyRayExit(release, direction));
        }
    }

    private IEnumerable<(Vector2 Start, Vector2 Bounce, Vector2 Exit)> EasyReplyWarningPaths()
    {
        for (int row = -1; row <= 1; row++)
        {
            Vector2 start = new(_origin.X, Field.CenterY + row * Field.Height / 3);
            Vector2 bounce = new(_points[0].X, start.Y + row * 10);
            Vector2 incoming = start.DirectionTo(bounce), reflected = new(-incoming.X, incoming.Y);
            yield return (start, bounce, EasyRayExit(bounce, reflected));
        }
    }

    private IEnumerable<(Vector2 Start, Vector2 Exit)> EasyDeliveryWarningPaths(int batch)
    {
        for (int column = 0; column < 3; column++)
        {
            Vector2 start = new(Field.Left + (column * 4 + batch % 2 * 2 + 1) * Field.Width / 12, Field.Top);
            yield return (start, new Vector2(start.X, Field.Bottom));
        }
    }

    private double EasyDeliveryWarningStart(int batch) => batch * 6.0;
    private double EasyDeliveryLaunch(int batch) => EasyDeliveryWarningStart(batch) + _warning;
    private (Vector2 Start, Vector2 Turn, Vector2 Bounce, Vector2 Exit)[] EasyDeliveryReturnPaths(int batch)
        => EasyDeliveryWarningPaths(batch).Select((path, lane) =>
        {
            float y = Mathf.Clamp(_easyDeliveryTargets[batch].Y + (lane - 1) * 24, Field.Top, Field.Bottom);
            Vector2 turn = new(path.Start.X, y);
            bool left = (batch + lane) % 2 == 0;
            Vector2 bounce = new(left ? Field.Left + 8 : Field.Right - 8, y);
            Vector2 reply = new(left ? 1 : -1, y < Field.CenterY ? .35f : -.35f);
            Vector2 exit = EasyRayExit(bounce, reply.Normalized());
            return (path.Start, turn, bounce, exit);
        }).ToArray();

    private void EasyReturnedDelivery()
    {
        for (int batch = 0; batch < 3; batch++)
        {
            if (Time < EasyDeliveryWarningStart(batch)) continue;
            if (!_easyDeliveryReserved[batch])
            {
                _easyDeliveryTargets[batch] = Player.GlobalPosition;
                _easyDeliveryReserved[batch] = true;
            }
            double launch = EasyDeliveryLaunch(batch);
            var paths = EasyDeliveryReturnPaths(batch);
            if (Time < launch)
            {
                float progress = EasyDeliveryWarningProgress(batch);
                foreach (var path in EasyDeliveryWarningPaths(batch))
                    if (progress > 0) Line(path.Start, path.Start.Lerp(path.Exit, Math.Min(1, progress * 3)), EasyWarningHalfWidth(1), false, progress);
                foreach (var path in paths)
                {
                    var prefix = EasyRainWarningPathPrefix(new[] { path.Start, path.Turn, path.Bounce, path.Exit }, progress);
                    for (int i = 1; i < prefix.Length; i++)
                        Line(prefix[i - 1], prefix[i], EasyWarningHalfWidth(1), false, progress);
                }
            }
            for (int lane = 0; lane < paths.Length; lane++)
            {
                var path = paths[lane];
                double delay = (path.Turn.Y - path.Start.Y) * (1.0 / 27 - 1.0 / 88);
                for (int burst = 0; burst < EasyDeliveryBursts; burst++)
                {
                    double pulse = burst * EasyDeliveryBurstInterval;
                    if (At(launch + pulse))
                    {
                        var ordinary = Send(path.Start, Vector2.Down * 27, 100 + burst * 9 + batch * 3 + lane);
                        ordinary.Shot.CancelLeadIn();
                    }
                    if (At(launch + delay + pulse))
                    {
                        var express = Send(path.Start, Vector2.Down * 88, burst * 9 + batch * 3 + lane);
                        express.Shot.CancelLeadIn();
                    }
                }
            }
        }
        foreach (var letter in _letters.Where(letter => letter.Alive && letter.Group < 100))
        {
            var path = EasyDeliveryReturnPaths(letter.Group % 9 / 3)[letter.Group % 3];
            if (letter.State == 0 && letter.Shot.GlobalPosition.Y >= path.Turn.Y)
            {
                letter.State = 1;
                letter.Shot.GlobalPosition = path.Turn;
                letter.TurnPoint = path.Turn;
                letter.Changed = Time;
                letter.Shot.Velocity = path.Turn.DirectionTo(path.Bounce) * 78;
            }
            else if (letter.State == 1 && (path.Bounce.X < path.Turn.X
                ? letter.Shot.GlobalPosition.X <= path.Bounce.X : letter.Shot.GlobalPosition.X >= path.Bounce.X))
            {
                letter.State = 2;
                letter.PreviousTurnPoint = letter.TurnPoint;
                letter.PreviousChanged = letter.Changed;
                letter.Shot.GlobalPosition = path.Bounce;
                letter.TurnPoint = path.Bounce;
                letter.Changed = Time;
                letter.Shot.Velocity = path.Bounce.DirectionTo(path.Exit) * 78;
            }
        }
        EndAfter(EasyDeliveryLaunch(2) + EasyDeliveryReturnPaths(2)
            .Max(path => (path.Turn.Y - path.Start.Y) * (1.0 / 27 - 1.0 / 88))
            + (EasyDeliveryBursts - 1) * EasyDeliveryBurstInterval + .02);
    }

    private Vector2 EasyInboxSource(int turn) => new(turn % 2 == 0 ? Field.Left + 16 : Field.Right - 16, Field.CenterY);
    private double EasyInboxLaunch(int turn) => _warning + turn * 4.8;

    private IEnumerable<(Vector2 Start, Vector2 Exit)> EasyInboxWarningPaths(int turn)
    {
        Vector2 source = EasyInboxSource(turn);
        float aim = source.DirectionTo(_easyInboxTargets[turn]).Angle();
        for (int row = -2; row <= 2; row++)
        {
            Vector2 direction = Vector2.FromAngle(aim + row * .19f);
            yield return (source, EasyRayExit(source, direction));
        }
        foreach (float y in new[] { Field.Top + 12, Field.Bottom - 12 })
        {
            Vector2 target = new(turn % 2 == 0 ? Field.Right - 8 : Field.Left + 8, y);
            Vector2 direction = source.DirectionTo(target);
            yield return (source, EasyRayExit(source, direction));
        }
    }

    private void EasyAlternatingInboxes()
    {
        for (int turn = 0; turn < 4; turn++)
        {
            double launch = EasyInboxLaunch(turn);
            if (Time < launch - EasyInboxWarningLead) continue;
            if (!_easyInboxReserved[turn])
            {
                _easyInboxTargets[turn] = Player.GlobalPosition;
                _easyInboxReserved[turn] = true;
            }
            var paths = EasyInboxWarningPaths(turn).ToArray();
            if (Time < launch)
            {
                float progress = EasyInboxWarningProgress(turn);
                if (progress > 0)
                    foreach (var path in paths)
                        Line(path.Start, path.Start.Lerp(path.Exit, progress), EasyWarningHalfWidth(1), false, progress);
            }
            for (int burst = 0; burst < EasyInboxBursts; burst++)
                if (At(launch + burst * EasyInboxBurstInterval))
                    foreach (var path in paths)
                    {
                        var letter = Send(path.Start, path.Start.DirectionTo(path.Exit) * 45, turn);
                        letter.Shot.CancelLeadIn();
                    }
        }
        EndAfter(EasyInboxLaunch(3) + (EasyInboxBursts - 1) * EasyInboxBurstInterval);
    }

    private void OvertakingDeliveries() => EasyReturnedDelivery();

    private void AlternatingInboxes() => EasyAlternatingInboxes();

    private void UnfastenedClips() => EasyUnbindingDrafts();

    private const int EasyUnbindingCopiesPerRound = 3, EasyUnbindingRounds = 4;
    private int EasyUnbindingBurstCount => 3 + Tier;
    private const double EasyUnbindingCopyInterval = .18, EasyUnbindingRoundInterval = 4.6;
    private double EasyUnbindingBurstInterval => .24 - Tier * .02;
    private const double EasyUnbindingLeadTime = .34, EasyUnbindingRetraceTime = .8;
    private const float EasyUnbindingSpeed = 90;
    private readonly double[] _easyUnbindingWarningStarts = { -1, -1, -1, -1 };
    private readonly Vector2[] _easyUnbindingTargets = new Vector2[EasyUnbindingRounds];
    private double _easyUnbindingNextRecord;
    private int _easyUnbindingRound = -1;
    private int EasyUnbindingRound => _easyUnbindingRound;
    private double EasyUnbindingRecording => 2.6;

    private void PrepareEasyUnbinding()
    {
        _history.Clear();
        _history.Add(_aim);
        _easyUnbindingNextRecord = 0;
    }

    private double EasyUnbindingWarningStart(int round) => _easyUnbindingWarningStarts[round];
    private Vector2 EasyUnbindingTarget(int round) => _easyUnbindingTargets[round];
    private double EasyUnbindingFire(int copy)
        => EasyUnbindingWarningStart(copy / EasyUnbindingCopiesPerRound) + 3 + copy % EasyUnbindingCopiesPerRound * EasyUnbindingCopyInterval;
    private double EasyUnbindingRelease(int copy) => EasyUnbindingFire(copy) + EasyUnbindingLeadTime + EasyUnbindingRetraceTime;
    private int EasyUnbindingCopyForGroup(int group) => group % (EasyUnbindingCopiesPerRound * EasyUnbindingRounds);
    private int EasyUnbindingBurstForGroup(int group) => group / (EasyUnbindingCopiesPerRound * EasyUnbindingRounds);
    private double EasyUnbindingBurstFire(int copy, int burst) => EasyUnbindingFire(copy) + burst * EasyUnbindingBurstInterval;
    private double EasyUnbindingBurstRelease(int copy, int burst)
        => EasyUnbindingRelease(copy) + burst * EasyUnbindingBurstInterval;
    private double EasyUnbindingEnd(int round) => Enumerable.Range(round * EasyUnbindingCopiesPerRound, EasyUnbindingCopiesPerRound)
        .Max(copy => EasyUnbindingBurstRelease(copy, EasyUnbindingBurstCount - 1)
            + EasyUnbindingCopyPoints(copy)[0].DistanceTo(EasyUnbindingExit(copy)) / EasyUnbindingSpeed + .15);
    private (float Retrace, float Exit) EasyUnbindingWarningProgress(int round)
        => (Progress(EasyUnbindingWarningStart(round) + .2, .35), Progress(EasyUnbindingWarningStart(round) + .55, .35));
    private Vector2[] EasyUnbindingCopyPoints(int copy) => EasyHistoryCopyPoints(copy);

    private Vector2 EasyUnbindingExit(int copy)
    {
        Vector2 from = EasyUnbindingCopyPoints(copy)[0];
        Vector2 direction = from.DirectionTo(EasyUnbindingTarget(copy / EasyUnbindingCopiesPerRound));
        if (direction == Vector2.Zero) direction = Vector2.FromAngle(copy * Mathf.Tau / 12);
        return EasyRayExit(from, direction);
    }

    private Vector2[] EasyUnbindingForecastPath(int copy)
        => EasyUnbindingCopyPoints(copy).Reverse().Append(EasyUnbindingExit(copy)).ToArray();

    private Vector2 EasyUnbindingRetracePoint(int copy, float progress)
    {
        var points = EasyUnbindingCopyPoints(copy);
        float total = 0;
        for (int index = 1; index < points.Length; index++) total += points[index].DistanceTo(points[index - 1]);
        float remaining = total * Mathf.Clamp(progress, 0, 1);
        for (int index = points.Length - 1; index > 0; index--)
        {
            float length = points[index].DistanceTo(points[index - 1]);
            if (length > 0 && remaining <= length) return points[index].Lerp(points[index - 1], remaining / length);
            remaining -= length;
        }
        return points[0];
    }

    private void EasyUnbindingDrafts()
    {
        if (Time < EasyUnbindingRecording)
        {
            if (Time >= _easyUnbindingNextRecord)
            {
                _history.Add(Player.GlobalPosition);
                _easyUnbindingNextRecord = Time + .14;
            }
            return;
        }
        if (_easyUnbindingRound < 0)
        {
            _easyUnbindingRound = 0;
            _easyUnbindingWarningStarts[0] = Time;
            _easyUnbindingTargets[0] = Player.GlobalPosition;
        }
        if (_easyUnbindingRound < EasyUnbindingRounds - 1
            && Time >= EasyUnbindingWarningStart(_easyUnbindingRound) + EasyUnbindingRoundInterval)
        {
            _easyUnbindingRound++;
            _easyUnbindingWarningStarts[_easyUnbindingRound]
                = EasyUnbindingWarningStart(_easyUnbindingRound - 1) + EasyUnbindingRoundInterval;
            _easyUnbindingTargets[_easyUnbindingRound] = Player.GlobalPosition;
        }
        int first = _easyUnbindingRound * EasyUnbindingCopiesPerRound;
        var warning = EasyUnbindingWarningProgress(_easyUnbindingRound);
        for (int copy = first; copy < first + EasyUnbindingCopiesPerRound; copy++)
        {
            if (Time < EasyUnbindingFire(first))
            {
                var points = EasyUnbindingCopyPoints(copy);
                float total = 0, before = 0;
                for (int index = 1; index < points.Length; index++) total += points[index].DistanceTo(points[index - 1]);
                for (int index = points.Length - 1; index > 0; index--)
                {
                    float length = points[index].DistanceTo(points[index - 1]);
                    float progress = length == 0 ? warning.Retrace : Mathf.Clamp((total * warning.Retrace - before) / length, 0, 1);
                    if (progress > 0 && length > 0)
                        Line(points[index], points[index].Lerp(points[index - 1], progress), EasyWarningHalfWidth(2), false, progress);
                    before += length;
                }
                if (warning.Exit > 0)
                    Line(points[0], points[0].Lerp(EasyUnbindingExit(copy), warning.Exit), EasyWarningHalfWidth(2), false, warning.Exit);
            }
            for (int burst = 0; burst < EasyUnbindingBurstCount; burst++)
                if (At(EasyUnbindingBurstFire(copy, burst)))
                    Send(EasyUnbindingCopyPoints(copy)[^1], Vector2.Zero,
                        copy + burst * EasyUnbindingCopiesPerRound * EasyUnbindingRounds, 2);
        }
        foreach (var letter in _letters.Where(letter => letter.Alive && letter.State == 0 && !letter.Shot.LeadIn))
        {
            int copy = EasyUnbindingCopyForGroup(letter.Group), burst = EasyUnbindingBurstForGroup(letter.Group);
            float progress = Progress(EasyUnbindingBurstFire(copy, burst) + EasyUnbindingLeadTime, EasyUnbindingRetraceTime);
            letter.Shot.GlobalPosition = EasyUnbindingRetracePoint(copy, progress);
            if (Time < EasyUnbindingBurstRelease(copy, burst)) continue;
            letter.State = 1;
            letter.Changed = Time;
            letter.TurnPoint = letter.Shot.GlobalPosition;
            letter.Direction = letter.TurnPoint.DirectionTo(EasyUnbindingExit(copy));
            letter.Shot.Velocity = letter.Direction * EasyUnbindingSpeed;
        }
        if (_easyUnbindingRound == EasyUnbindingRounds - 1)
            EndAfter(EasyUnbindingBurstRelease(first + EasyUnbindingCopiesPerRound - 1, EasyUnbindingBurstCount - 1));
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
        if (Time < _warning)
        {
            var progress = EasyReplyWarningProgress();
            foreach (var path in EasyReplyWarningPaths())
            {
                if (progress.Incoming > 0)
                    Line(path.Start, path.Start.Lerp(path.Bounce, progress.Incoming), EasyWarningHalfWidth(1), false, progress.Incoming);
                if (progress.Outgoing > 0)
                    Line(path.Bounce, path.Bounce.Lerp(path.Exit, progress.Outgoing), EasyWarningHalfWidth(1), false, progress.Outgoing);
            }
            Hint(wall + new Vector2(incoming.X < 0 ? 6 : -42, -12), "返信 ↩");
        }
        for (int wave = 0; wave < 3; wave++)
            if (At(_warning + wave * .7))
            {
                var paths = EasyReplyWarningPaths().ToArray();
                for (int row = 0; row < paths.Length; row++)
                    Send(paths[row].Start, paths[row].Start.DirectionTo(paths[row].Bounce) * 59, row);
            }
        foreach (var letter in _letters.Where(l => l.Alive && l.State == 0 && !l.Shot.LeadIn))
            if (letter.Shot.GlobalPosition.X <= Field.Left + 8 || letter.Shot.GlobalPosition.X >= Field.Right - 8)
            {
                letter.State = 1;
                letter.Shot.GlobalPosition = EasyReplyWarningPaths().First(path => path.Start.DistanceSquaredTo(letter.Origin) < .01f).Bounce;
                letter.Changed = Time;
                letter.TurnPoint = letter.Shot.GlobalPosition;
                letter.Shot.Velocity = new Vector2(-letter.Shot.Velocity.X, letter.Shot.Velocity.Y);
            }
        EndAfter(_warning + 8);
    }

    private void FilingPiles() => EasyFilingPiles();

    private void RainSplash() => EasyRainSplash();

    private void SpentWavefront() => EasySpentWavefront();

    private double EasyPileSwitchAt => _warning + (EasyPileColumns - 1) * EasyPileColumnInterval
        + EasyPileCollapseAt + (EasyPileRows - 1) * EasyPileCollapseInterval + .06;
    private int EasyPilePass => Time >= EasyPileSwitchAt ? 1 : 0;
    private int EasyPileColumnCount => EasyPileColumns + EasyPilePass;
    private float EasyPileX(int column) => Field.Left
        + (column + (EasyPilePass == 0 ? .5f : 0)) * Field.Width / EasyPileColumns;
    private double EasyPileStart(int column) => _warning + EasyPilePass * EasyPileSwitchAt + column * EasyPileColumnInterval;
    private int EasyPileRemaining(int column)
    {
        double age = Time - EasyPileStart(column);
        int removed = age < EasyPileCollapseAt ? 0 : 1 + (int)((age - EasyPileCollapseAt) / EasyPileCollapseInterval);
        return Math.Max(0, EasyPileRows - removed);
    }
    private Rect2 EasyPileSheetBounds(int column, int row)
    {
        double age = Time - EasyPileStart(column) - row * EasyPileRowInterval;
        float settledY = Field.Bottom - (row + 1) * EasyPileLayerHeight;
        float y = Math.Min(settledY, -EasyPileLayerHeight + (float)age * EasyPileSpeed);
        return new Rect2(EasyPileX(column) - EasyPileWidth / 2, y, EasyPileWidth, EasyPileLayerHeight);
    }
    private Rect2 EasyPileStackBounds(int column)
        => new Rect2(EasyPileX(column) - EasyPileWidth / 2, Field.Bottom - _states[column] * EasyPileLayerHeight,
            EasyPileWidth, _states[column] * EasyPileLayerHeight).Intersection(Field.Rect);

    private void EasyFilingPiles()
    {
        for (int column = 0; column < EasyPileColumnCount; column++)
        {
            double start = EasyPileStart(column);
            _states[column] = 0;
            if (Time >= start - _warning && Time < start)
            {
                float progress = EasyPileWarningProgress(column);
                if (progress > 0)
                    Box(new Rect2(EasyPileX(column) - EasyPileWidth / 2, Field.Top,
                        EasyPileWidth, Field.Height * progress).Intersection(Field.Rect), false, progress);
            }
            for (int row = 0; row < EasyPileRemaining(column); row++)
            {
                if (Time < start + row * EasyPileRowInterval) continue;
                Rect2 bounds = EasyPileSheetBounds(column, row);
                if (bounds.Position.Y >= Field.Bottom - (row + 1) * EasyPileLayerHeight) _states[column]++;
                else if (bounds.End.Y > Field.Top) Box(bounds.Intersection(Field.Rect), true);
            }
            if (_states[column] > 0) Box(EasyPileStackBounds(column), true);
        }
        EndAfter(_warning + EasyPileSwitchAt + EasyPileColumns * EasyPileColumnInterval
            + EasyPileCollapseAt + (EasyPileRows - 1) * EasyPileCollapseInterval + .12);
    }

    private double EasyRainLaunch(int wave, int row) => _warning + wave * EasyRainWaveInterval + row * EasyRainRowInterval;
    private float EasyRainX(int wave, int row, int column)
        => Field.Left + 16 + column * (Field.Width - 32) / (EasyRainColumns - 1) + ((wave + row) % 3 - 1) * 7;
    private Vector2 EasyRainOrigin(int wave, int row, int column) => new(EasyRainX(wave, row, column), 8);
    private static Vector2 EasyRainImpactPoint(Vector2 position) => new(position.X, Field.Bottom - 10);
    private Vector2 EasyRainImpact(int wave, int row, int column) => EasyRainImpactPoint(EasyRainOrigin(wave, row, column));

    private float EasyRainDropWarningProgress(int wave, int row)
    {
        double launch = EasyRainLaunch(wave, row);
        double start = launch - _warning;
        return Progress(start + .4, .8);
    }

    private (int Wave, int Row, double Remaining, float Progress, Vector2[] Points)? EasyRainImpactWarning()
    {
        var first = _letters.Where(letter => letter.Alive && !letter.Shot.LeadIn && letter.State == 0 && letter.Group < 10)
            .OrderBy(letter => letter.Born).ThenByDescending(letter => letter.Shot.GlobalPosition.Y).FirstOrDefault();
        if (first == null) return null;
        double remaining = Math.Max(0, (EasyRainImpactPoint(first.Shot.GlobalPosition).Y - first.Shot.GlobalPosition.Y) / EasyRainSpeed);
        if (remaining > 2.7) return null;
        int wave = first.Group;
        int row = Math.Clamp((int)Math.Round((first.Born - EasyRainLaunch(wave, 0)) / EasyRainRowInterval), 0, EasyRainRows - 1);
        float progress = Mathf.Clamp((float)((2.7 - remaining - .2) / .3), 0, 1);
        var points = _letters.Where(letter => letter.Alive && !letter.Shot.LeadIn && letter.State == 0
                && letter.Group == wave && letter.Born == first.Born)
            .Select(letter => EasyRainImpactPoint(letter.Shot.GlobalPosition)).ToArray();
        return (wave, row, remaining, progress, points);
    }
    private int EasyRainNextRow
    {
        get
        {
            for (int next = 0; next < EasyRainWaves * EasyRainRows; next++)
            {
                double launch = EasyRainLaunch(next / EasyRainRows, next % EasyRainRows);
                if (Time >= launch - _warning && Time < launch) return next;
            }
            return -1;
        }
    }

    private void EasyRainSplash()
    {
        int nextRow = EasyRainNextRow;
        for (int wave = 0; wave < EasyRainWaves; wave++)
            for (int row = 0; row < EasyRainRows; row++)
            {
                double launch = EasyRainLaunch(wave, row);
                for (int column = 0; column < EasyRainColumns; column++)
                {
                    Vector2 origin = EasyRainOrigin(wave, row, column);
                    Vector2 impact = EasyRainImpact(wave, row, column);
                    if (wave * EasyRainRows + row == nextRow)
                    {
                        float progress = EasyRainDropWarningProgress(wave, row);
                        if (progress > 0)
                            Line(origin, origin.Lerp(impact, progress), EasyWarningHalfWidth(0), false, progress);
                    }
                    for (int burst = 0; burst < EasyRainBursts; burst++)
                        if (At(launch + burst * EasyRainBurstInterval)) Send(origin, Vector2.Down * EasyRainSpeed, wave, 0);
                }
            }
        foreach (var letter in _letters.ToArray())
        {
            if (!letter.Alive || letter.Shot.LeadIn) continue;
            if (letter.Group == 10)
            {
                double age = Time - letter.Born;
                letter.Shot.Velocity = new Vector2(letter.Direction.X * 50, -EasySplashRise + (float)age * EasySplashGravity);
                continue;
            }
            if (letter.State != 0 || letter.Shot.GlobalPosition.Y < Field.Bottom - 10) continue;
            Vector2 impact = EasyRainImpactPoint(letter.Shot.GlobalPosition);
            Pool.Despawn(letter.Shot);
            letter.State = 1;
            for (int side = -1; side <= 1; side += 2)
            {
                var splash = Send(impact, new Vector2(side * 50, -EasySplashRise), 10, 2);
                splash.Direction = new Vector2(side, 0);
                splash.Shot.CancelLeadIn();
            }
        }
        EndAfter(EasyRainLaunch(EasyRainWaves - 1, EasyRainRows - 1) + (EasyRainBursts - 1) * EasyRainBurstInterval + (Field.Bottom - 18) / EasyRainSpeed
            + 2 * EasySplashRise / EasySplashGravity + .2);
    }

    private float EasyWaveMaxRadius => Math.Max(Math.Max(_origin.DistanceTo(new(Field.Left, Field.Top)),
        _origin.DistanceTo(new(Field.Right, Field.Top))), Math.Max(_origin.DistanceTo(new(Field.Left, Field.Bottom)),
        _origin.DistanceTo(new(Field.Right, Field.Bottom)))) + 6;
    private double EasyWaveDuration => EasyWaveOpeningAt + EasyWaveAccelerationTime
        + (EasyWaveMaxRadius - EasyWaveRadius(EasyWaveOpeningAt + EasyWaveAccelerationTime)) / EasyWaveOuterSpeed;
    private float EasyWaveRadius(double elapsed)
    {
        if (elapsed <= EasyWaveOpeningAt) return 18 + (float)elapsed * EasyWaveSpeed;
        double acceleration = Math.Min(elapsed - EasyWaveOpeningAt, EasyWaveAccelerationTime);
        double amount = acceleration / EasyWaveAccelerationTime;
        double easedTravel = EasyWaveAccelerationTime * (amount * amount * amount - .5 * amount * amount * amount * amount);
        return 18 + (float)(EasyWaveOpeningAt * EasyWaveSpeed + acceleration * EasyWaveSpeed
            + easedTravel * (EasyWaveOuterSpeed - EasyWaveSpeed)
            + Math.Max(0, elapsed - EasyWaveOpeningAt - EasyWaveAccelerationTime) * EasyWaveOuterSpeed);
    }
    private float EasyWaveGapAngle(int wave) => _angle + wave * Mathf.Pi;
    private float EasyWaveExpiryInnerRadius => EasyWaveRadius(EasyWaveOpeningAt) + 4.5f;
    private float EasyWaveExpiryHalfAngle => EasyWaveSpentSegments * Mathf.Pi / EasyWaveSegments
        - Mathf.Asin(4.5f / EasyWaveExpiryInnerRadius);
    private (int First, int End)[] EasyWaveLiveRanges(double elapsed)
        => elapsed >= EasyWaveOpeningAt
            ? new[] { (EasyWaveSpentSegments, EasyWaveSegments) }
            : new[] { (0, EasyWaveSegments) };
    private Vector2 EasyWavePoint(int wave, int segment, float radius)
        => _origin + Vector2.FromAngle(EasyWaveGapAngle(wave) - EasyWaveSpentSegments * Mathf.Pi / EasyWaveSegments
            + segment * Mathf.Tau / EasyWaveSegments) * radius;
    private void PrepareEasyWave(int wave, double start)
    {
        float halfOpening = EasyWaveSpentSegments * Mathf.Pi / EasyWaveSegments - .15f;
        var body = Boss.GetChildren().OfType<CollisionShape2D>().First();
        var shape = (CapsuleShape2D)body.Shape;
        float clearance = shape.Radius + Player.HitRadius + 12;
        float halfAxis = shape.Height / 2 - shape.Radius;
        Vector2 lower = body.GlobalPosition - Vector2.Down * halfAxis, upper = body.GlobalPosition + Vector2.Down * halfAxis;
        float detourRadius = clearance + halfAxis;
        float distance = float.MaxValue;
        for (int sample = 0; sample <= 32; sample++)
        {
            float offset = Mathf.Lerp(-halfOpening, halfOpening, sample / 32f);
            Vector2 waiting = _origin + Vector2.FromAngle(EasyWaveGapAngle(wave) + offset) * EasyWaveWaitRadius;
            if (waiting.DistanceTo(Geometry2D.GetClosestPointToSegment(waiting, lower, upper)) <= clearance) continue;
            float travel = Player.GlobalPosition.DistanceTo(waiting);
            if (body.GlobalPosition.DistanceTo(Geometry2D.GetClosestPointToSegment(body.GlobalPosition, Player.GlobalPosition, waiting)) < detourRadius)
                travel += Mathf.Pi * detourRadius;
            if (travel >= distance) continue;
            distance = travel;
            _easyWaveWaitPoints[wave] = waiting;
        }
        _easyWaveWarningStarts[wave] = start;
        _easyWaveLaunches[wave] = start + WarnFor(distance + 8, 3.8);
    }
    private void EasySpentWavefront()
    {
        if (_easyWaveLaunches[1] < 0 && Time >= _easyWaveLaunches[0] + EasyWaveDuration + EasyWaveRest)
            PrepareEasyWave(1, Time);
        for (int wave = 0; wave < 2; wave++)
        {
            if (_easyWaveLaunches[wave] < 0) continue;
            double elapsed = Time - _easyWaveLaunches[wave];
            if (elapsed < 0)
            {
                float progress = EasyWaveWarningProgress(wave).Ring;
                if (progress > 0) Disc(_origin, 20 * progress, false, progress);
                continue;
            }
            if (elapsed >= EasyWaveDuration) continue;
            if (elapsed < .3) Disc(_origin, 20, true);
            float radius = EasyWaveRadius(elapsed);
            foreach (var range in EasyWaveLiveRanges(elapsed))
                for (int i = range.First; i < range.End; i++)
                    Line(EasyWavePoint(wave, i, radius), EasyWavePoint(wave, i + 1, radius), 2, true);
        }
        if (_easyWaveLaunches[1] >= 0) EndAfter(_easyWaveLaunches[1] + EasyWaveDuration + EasyWaveRest);
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

    private (Vector2 A, Vector2 B, float Radius)[] EasyApprovalStampPaths(int index)
    {
        Vector2 position = _stamps[index].Position;
        float shiftX = Mathf.Clamp((position.X - Field.CenterX) / Field.Width, -.5f, .5f) * 8;
        float shiftY = Mathf.Clamp((position.Y - Field.CenterY) / Field.Height, -.5f, .5f) * 8;
        Vector2 topLeft = new(Field.Left + 8, Field.Top + 8), topRight = new(Field.Right - 8, Field.Top + 8);
        Vector2 bottomLeft = new(Field.Left + 8, Field.Bottom - 8), bottomRight = new(Field.Right - 8, Field.Bottom - 8);
        Vector2 first = new(Field.Left + Field.Width * .22f + shiftX, Field.Top + Field.Height * .46f + shiftY);
        Vector2 fold = new(Field.Left + Field.Width * .43f + shiftX * .5f, Field.Top + Field.Height * .68f + shiftY);
        Vector2 last = new(Field.Left + Field.Width * .8f + shiftX, Field.Top + Field.Height * .24f + shiftY * .5f);
        return new[] { (topLeft, topRight, 3f), (topRight, bottomRight, 3f),
            (bottomRight, bottomLeft, 3f), (bottomLeft, topLeft, 3f), (first, fold, 6f), (fold, last, 6f) };
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
        {
            if (Time < stamp.At + .6)
            {
                Disc(stamp.Position, 19 + Tier * 2, Time >= stamp.At, Progress(stamp.At - 1.2, 1.2));
                Hint(stamp.Position + new Vector2(-12, -26), Time < stamp.At ? "締切" : "承認");
            }
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

    private Vector2[] EasyEnvelopeTravelPath() => new[]
    {
        new Vector2(Field.Left + 26, 26), new Vector2(Field.Right - 26, 26),
        new Vector2(Field.Right - 26, Field.Bottom - 26), new Vector2(Field.Left + 26, Field.Bottom - 26),
        new Vector2(Field.Left + 26, 26), new Vector2(Field.Left + 26, -40),
    };

    private double EasyEnvelopeTravelDuration
    {
        get
        {
            var path = EasyEnvelopeTravelPath();
            return EasyApprovalLeave(3) - _warning + path[4].DistanceTo(path[5]) / 70;
        }
    }

    private double EasyApprovalArrival(int index)
    {
        var path = EasyEnvelopeTravelPath();
        double departure = index == 0 ? _warning : EasyApprovalLeave(index - 1);
        return departure + path[index].DistanceTo(path[index + 1]) / 70;
    }

    private int EasyApprovalBursts => 3 + Tier;
    private double EasyApprovalBurstInterval => .28 - Tier * .025;
    private double EasyApprovalWarningStart(int index) => EasyApprovalArrival(index);
    private double EasyApprovalOpeningFire(int index) => EasyApprovalWarningStart(index) + 2.55;
    private double EasyApprovalBurstFire(int index, int burst) => EasyApprovalOpeningFire(index) + burst * EasyApprovalBurstInterval;
    private double EasyApprovalStampFire(int index)
        => _stamps.Count > index ? _stamps[index].At : EasyApprovalWarningStart(index) + 3.7;
    private double EasyApprovalLeave(int index) => EasyApprovalStampFire(index) + .6;
    private Vector2 EasyApprovalStampPosition(int index) => _stamps[index].Position;
    private (float Frame, float Check) EasyApprovalStampWarningProgress(int index)
        => (Progress(EasyApprovalWarningStart(index) + .2, .35), Progress(EasyApprovalWarningStart(index) + .55, .35));
    private float EasyApprovalStampDiscWarningProgress(int index) => Progress(EasyApprovalWarningStart(index) + .2, .7);

    private float EasyApprovalStampPathWarningProgress(int index, int pathIndex)
    {
        var paths = EasyApprovalStampPaths(index);
        var progress = EasyApprovalStampWarningProgress(index);
        if ((pathIndex < 4 ? progress.Frame : progress.Check) >= 1) return 1;
        int first = pathIndex < 4 ? 0 : 4, end = pathIndex < 4 ? 4 : 6;
        float total = 0, before = 0;
        for (int path = first; path < end; path++)
        {
            float length = paths[path].A.DistanceTo(paths[path].B);
            total += length;
            if (path < pathIndex) before += length;
        }
        return Mathf.Clamp((total * (pathIndex < 4 ? progress.Frame : progress.Check) - before)
            / paths[pathIndex].A.DistanceTo(paths[pathIndex].B), 0, 1);
    }

    private float EasyEnvelopeOpenProgress()
    {
        for (int index = 0; index < 4; index++)
            if (Time >= EasyApprovalWarningStart(index) && Time < EasyApprovalLeave(index))
                return Progress(EasyApprovalOpeningFire(index) - .6, .6);
        return 0;
    }

    private Vector2 CarriedEnvelopePosition(double elapsed)
    {
                var path = EasyEnvelopeTravelPath();
        double time = _warning + Math.Max(0, elapsed);
        for (int index = 0; index < 4; index++)
        {
            double departure = index == 0 ? _warning : EasyApprovalLeave(index - 1);
            double arrival = EasyApprovalArrival(index);
            if (time < arrival)
                return path[index].Lerp(path[index + 1], Mathf.Clamp((float)((time - departure) / (arrival - departure)), 0, 1));
            if (time < EasyApprovalLeave(index)) return path[index + 1];
        }
        return path[4].MoveToward(path[5], (float)(time - EasyApprovalLeave(3)) * 70);
    }

    private bool CarriedEnvelopeVisible(double elapsed)
        => elapsed >= 0 && new Rect2(CarriedEnvelopePosition(elapsed) - new Vector2(18, 11), new Vector2(36, 22)).Intersects(Field.Rect);

    private IEnumerable<Rect2> EasyEnvelopeWarningBoxes(float progress)
    {
        var path = EasyRainWarningPathPrefix(EasyEnvelopeTravelPath(), progress);
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 from = path[i - 1], to = path[i];
            yield return new Rect2(new Vector2(Math.Min(from.X, to.X) - 18, Math.Min(from.Y, to.Y) - 11),
                new Vector2(Math.Abs(to.X - from.X) + 36, Math.Abs(to.Y - from.Y) + 22));
        }
    }

    private IEnumerable<(Vector2 Start, Vector2 Exit)> EasyEnvelopeOpeningPaths(int opening)
    {
        Vector2 carrier = EasyEnvelopeTravelPath()[opening + 1];
        Vector2 forward = carrier.DirectionTo(new Vector2(Field.CenterX, Field.CenterY));
        for (int row = -2; row <= 2; row++)
        {
            Vector2 start = carrier + forward * 19 + forward.Orthogonal() * (row * 2);
            Vector2 direction = forward.Rotated(row * .14f);
            yield return (start, EasyRayExit(start, direction));
        }
    }

    private void CarriedEnvelope(double delta) => EasyCarriedApproval(delta);

    private void EasyCarriedApproval(double delta)
    {
        if (Player.GlobalPosition.DistanceTo(_dwell) > 9)
        {
            _dwell = Player.GlobalPosition;
            _still = 0;
        }
        else _still += delta;
        double elapsed = Time - _warning;
        Vector2 carrier = CarriedEnvelopePosition(elapsed);
        bool exists = CarriedEnvelopeVisible(elapsed);
        if (elapsed < 0)
        {
            float progress = EasyEnvelopeTravelWarningProgress();
            if (progress > 0)
                foreach (var bounds in EasyEnvelopeWarningBoxes(progress)) Box(bounds, false, progress);
        }
        else if (exists)
            Box(new Rect2(carrier - new Vector2(18, 11), new Vector2(36, 22)), true);
        for (int index = 0; index < 4; index++)
        {
            double reservation = EasyApprovalWarningStart(index);
            if (Time < reservation) continue;
            if (_stamps.Count == index)
            {
                _stamps.Add(new Stamp { Position = Player.GlobalPosition, At = reservation + 3.7 });
                _dwell = Player.GlobalPosition;
                _still = 0;
            }
            double opening = EasyApprovalOpeningFire(index);
            if (Time < opening)
            {
                if (_still >= .65) _stamps[index].At = reservation + 2.9;
                float progress = EasyEnvelopeOpeningWarningProgress(index);
                if (progress > 0)
                    foreach (var path in EasyEnvelopeOpeningPaths(index))
                        Line(path.Start, path.Start.Lerp(path.Exit, progress), EasyWarningHalfWidth(2), false, progress);
            }
            for (int burst = 0; burst < EasyApprovalBursts; burst++)
                if (At(EasyApprovalBurstFire(index, burst)))
                    foreach (var path in EasyEnvelopeOpeningPaths(index))
                        Send(path.Start, path.Start.DirectionTo(path.Exit) * 62, index, 2);
            if (Time >= EasyApprovalLeave(index)) continue;
            bool live = Time >= EasyApprovalStampFire(index);
            var paths = EasyApprovalStampPaths(index);
            for (int path = 0; path < paths.Length; path++)
            {
                float progress = live ? 1 : EasyApprovalStampPathWarningProgress(index, path);
                if (progress > 0)
                    Line(paths[path].A, paths[path].A.Lerp(paths[path].B, progress), paths[path].Radius, live, progress);
            }
            float disc = live ? 1 : EasyApprovalStampDiscWarningProgress(index);
            if (disc > 0) Disc(EasyApprovalStampPosition(index), 19 * disc, live, disc);
            Hint(EasyApprovalStampPosition(index) + new Vector2(-18, -27), live ? "承認" : "承認待ち");
        }
        EndAfter(_warning + EasyEnvelopeTravelDuration + .1);
    }

    private Vector2[] EasyClockWarningOutline() => EasyClockWarningSector(1);

    private double EasyClockDuration => 11.5;
    private float EasyClockWarningAngle(double elapsed) => _angle + (float)Math.Max(0, elapsed) * (Mathf.Tau / 7.5f);

    private float EasyClockBoundaryLength(float angle)
    {
        Vector2 direction = Vector2.FromAngle(angle);
        float length = float.PositiveInfinity;
        if (direction.X != 0)
            length = Math.Min(length, ((direction.X > 0 ? Field.Right : Field.Left) - _origin.X) / direction.X);
        if (direction.Y != 0)
            length = Math.Min(length, ((direction.Y > 0 ? Field.Bottom : Field.Top) - _origin.Y) / direction.Y);
        return length + 3;
    }

    private float EasyClockWarningLength(double elapsed)
    {
        float extension = elapsed < 2 ? 0 : elapsed < 3 ? (float)(elapsed - 2)
            : elapsed < 10.5 ? 1 : Mathf.Clamp((float)(EasyClockDuration - elapsed), 0, 1);
        return Mathf.Lerp(44, EasyClockBoundaryLength(EasyClockWarningAngle(elapsed)), extension);
    }

    private Vector2[] EasyClockWarningSector(float progress)
    {
        progress = Mathf.Clamp(progress, 0, 1);
        if (progress == 0) return Array.Empty<Vector2>();
        float reach = Math.Max(Math.Max(_origin.DistanceTo(new(Field.Left, Field.Top)), _origin.DistanceTo(new(Field.Right, Field.Top))),
            Math.Max(_origin.DistanceTo(new(Field.Left, Field.Bottom)), _origin.DistanceTo(new(Field.Right, Field.Bottom)))) + 3;
        int segments = Mathf.CeilToInt(Mathf.Tau * reach / 3);
        double end = EasyClockDuration * progress, sweep = Math.Min(7.5, end), frontier = end % 7.5;
        var times = new SortedSet<double>(Enumerable.Range(0, segments + 1).Select(i => i * 7.5 / segments)
            .Where(elapsed => elapsed <= sweep)) { 0, sweep };
        foreach (double boundary in new[] { 2d, 3d, 10.5d, EasyClockDuration, end })
            if (boundary <= end && boundary % 7.5 <= sweep) times.Add(boundary % 7.5);
        foreach (Vector2 corner in new[] { new Vector2(Field.Left, Field.Top), new Vector2(Field.Right, Field.Top),
            new Vector2(Field.Right, Field.Bottom), new Vector2(Field.Left, Field.Bottom) })
        {
            double at = Mathf.PosMod(_origin.DirectionTo(corner).Angle() - _angle, Mathf.Tau) / (Mathf.Tau / 7.5f);
            if (at <= sweep) times.Add(at);
        }
        float MaximumLength(double at, bool includeFrontier)
        {
            float length = 0;
            for (double occurrence = at; occurrence <= end + 1e-8; occurrence += 7.5)
                if (includeFrontier || occurrence < end - 1e-8)
                    length = Math.Max(length, EasyClockWarningLength(occurrence));
            return length;
        }
        var points = new List<Vector2>();
        foreach (double at in times)
        {
            Vector2 radial = Vector2.FromAngle(EasyClockWarningAngle(at));
            float length = MaximumLength(at, true);
            points.Add(_origin + radial * length);
            if (end >= 7.5 && Math.Abs(at - frontier) < 1e-8)
            {
                float earlier = MaximumLength(at, false);
                if (earlier > 0 && earlier < length) points.Add(_origin + radial * earlier);
            }
        }
        points.Add(_origin + Vector2.FromAngle(EasyClockWarningAngle(sweep)) * 12);
        for (int i = segments; i >= 0; i--)
        {
            double at = i * 7.5 / segments;
            if (at >= sweep) continue;
            points.Add(_origin + Vector2.FromAngle(EasyClockWarningAngle(at)) * 12);
        }
        points.Add(points[0]);
        return points.ToArray();
    }

    private void ExtendingClock()
    {
        _origin = new Vector2(Field.CenterX, Field.CenterY);
        double elapsed = Time - _warning;

        if (elapsed < 0)
        {
            float progress = EasyClockWarningProgress();
            if (progress > 0)
            {
                Vector2 radial = Vector2.FromAngle(_angle), start = _origin + radial * 12;
                Line(start, start.Lerp(_origin + radial * 44, progress), 3 + Tier * .65f, false, progress);
            }
        }
        else if (elapsed < EasyClockDuration)
        {
            Vector2 radial = Vector2.FromAngle(EasyClockWarningAngle(elapsed));
            Line(_origin + radial * 12, _origin + radial * EasyClockWarningLength(elapsed), 3 + Tier * .65f, true);
        }
        EndAfter(_warning + EasyClockDuration);
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
        if (UsesCustomHazardArt)
        {
            DrawEasyTechniqueArt();
            return;
        }
        DrawOfficeTechniqueArt();
    }

    private void DrawOfficeTechniqueArt()
    {
        var paper = new Color(Paper, .70f);
        var ink = new Color(Ink, .65f);
        double elapsed = Time - _warning;
        switch (_mechanic)
        {
            case 0:
                if (UsesEasyReadabilityRules) break;
                foreach (var group in _letters.Where(l => l.Alive).GroupBy(l => l.Group))
                {
                    var letter = group.First();
                    Vector2 at = letter.Shot.GlobalPosition + new Vector2(9, 0);
                    OfficeArrow(at, Vector2.Down, 9, ink);
                    if (group.Key == 1) OfficeArrow(at + new Vector2(4, -4), Vector2.Down, 9, paper);
                }
                break;
            case 1:
                int turn = Math.Min(3, (int)(Math.Max(0, Time - _warning + EasyInboxWarningLead) / 4.8));
                for (int side = 0; side < 2; side++)
                    Envelope(new Vector2(side == 0 ? Field.Left + 16 : Field.Right - 16, Field.CenterY), new Vector2(10, 6),
                        side == turn % 2 && Time < EasyInboxLaunch(turn) + (EasyInboxBursts - 1) * EasyInboxBurstInterval ? 1 : 0, side == turn % 2 ? paper : new Color(Ink, .35f));
                break;
            case 2:
                if (Time < EasyUnbindingRecording)
                    PaperSheet(Player.GlobalPosition + new Vector2(13, -11), -.15f, ink);
                break;
            case 3:
                double recording = 2.6 + Tier * .3;
                if (Time < recording)
                    PaperSheet(Player.GlobalPosition + new Vector2(13, -11), -.15f, ink);
                else if (_history.Count > 0 && !UsesEasyReadabilityRules)
                {
                    int cursor = Math.Clamp(_history.Count - 1 - (int)(Math.Max(0, Time - recording - _warning) / .13), 0, _history.Count - 1);
                    UndoMark(_history[cursor] + new Vector2(0, -10), paper);
                }
                break;
            case 4:
                if (UsesEasyReadabilityRules) break;
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
                if (UsesEasyReadabilityRules) break;
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
                Vector2 carrier = CarriedEnvelopePosition(elapsed);
                if (elapsed < 0 || CarriedEnvelopeVisible(elapsed))
                {
                    Envelope(carrier, new Vector2(16, 9), EasyEnvelopeOpenProgress(), paper);
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
