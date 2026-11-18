using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AkariBossTechnique : BossCharacterTechnique
{
    internal override bool UsesArenaBody => Kind == BossTechniqueKind.Normal && Variant == 1
        || Kind == BossTechniqueKind.Signature;
    private sealed class Letter
    {
        public Bullet Shot = null!;
        public ulong Activation;
        public DeliveryKind? DeliveryType;
        public FanRay[] ReturnRays = Array.Empty<FanRay>();
        public Vector2 Origin, Direction, TurnPoint, PreviousTurnPoint;
        public double Born, Changed, PreviousChanged;
        public int Group, State, CopyIndex;
        public bool Alive => Shot.Active && Shot.ActivationId == Activation;
    }
    private sealed class Stamp
    {
        public Vector2 Position;
        public double At;
    }
    private readonly List<Letter> _letters = new();
    private readonly List<(Curve2D Path, bool Curved)> _breathLanes = new();
    private const float CameoPaperRadius = 4;
    private readonly List<Vector2> _history = new();
    private readonly List<Stamp> _stamps = new();
    private AttackCurveRoute[] _carrierRoutes = Array.Empty<AttackCurveRoute>();
    private readonly Vector2[] _points = new Vector2[12];
    private readonly int[] _states = new int[16];
    private int _mechanic, _beat;
    private double _warning, _next, _still;
    private double _deadlineStart, _deadlineFire;
    private int _deadlineRound;
    private Vector2 _origin, _aim, _dwell;
    private sealed record PriorityBatch(double StartsAt, double FiresAt, double EndsAt,
        int XDirection, int YDirection, int Row, int Column, bool AcrossFirst);
    private sealed record OutboxBatch(double StartsAt, double FiresAt, double EndsAt, int Direction, int FirstRow);
    private readonly List<PriorityBatch> _priorityBatches = new();
    private readonly List<OutboxBatch> _outboxBatches = new();
    private int _umbrellaFold = -1, _umbrellaTransfers;
    private double _umbrellaFoldStarted = -20;
    private float _angle;
    private static BulletArt.PlayerVisual? _easyRainShot, _easyUnbindingShot;
    private static readonly Color Paper = new("ffe6a2"), Ink = new("b3e4eb");
    protected override bool UsesDangerForecast => Tier == 2 || _mechanic is 0 or 1 or 2 or 5 or 6 or 8 or 10 or 11 or 12 or 14 or 16 or 17 or 18 or 19 or 20;
    protected override Color AttackLineInk => _mechanic switch
    {
        6 or 17 => new("6da6be"), 8 => new("dc8762"), 18 => new("e7b877"),
        19 => new("dc809d"), 20 => new("989ec9"), _ => UsesCustomHazardArt ? new("ff7568") : new("dba990"),
    };
    protected override bool DirectionalWarnings => _mechanic is 8 or 10 or 16 or 18 or 19;
    private const float EasyUnbindingScale = 3;
    private double ClipCycle => Tier switch { 2 => 2.95, 3 => 2.75, _ => 3.2 };
    private double ClipLead => Tier switch { 2 => 1.35, 3 => 1.25, _ => 1.5 };
    private const int EasyPileColumns = 7;
    private int EasyPileRows => 3 + Tier + (Evolved ? 1 : 0);
    private const int EasyRainWaves = 2, EasyRainRows = 3;
    private int EasyRainColumns => Tier switch { 0 => 9, 1 => 8, 2 => 15, _ => 21 };
    private float EasyPileWidth(int column) => Tier switch {
        0 => Math.Min(26, Field.Width / EasyPileColumns - 18),
        1 => Math.Min(32, Field.Width / EasyPileColumns - 14),
        2 => EasyPileColumnPass(column) == 0 ? (Field.Width - 80) / 7 : Field.Width / 7 - 10, _ => 39,
    };
    private int EasyDeliveryLanes => Tier == 2 ? Math.Max(3, Mathf.FloorToInt(Field.Width / 14)) : 3 + Tier * 3;
    private const float EasyPileSpeed = 120;
    private float EasyPileLayerHeight => 18 + Tier * 2 + (Evolved ? 2 : 0);
    private double EasyPileColumnInterval => Tier switch { 2 => .62, 3 => .48, _ => .03 };
    private const double EasyPileRowInterval = .14,
        EasyPileCollapseAt = 2.1, EasyPileCollapseInterval = .16;
    private const double EasyRainWaveInterval = 10.6, EasyRainRowInterval = 3.35;
    private const double EasyInboxWarningLead = 3.2;
    private int EasyDeliveryBursts => (Tier switch { 0 => 3, 1 => 5, 2 => 8, _ => 12 }) + (Evolved ? 2 : 0);
    private int EasyInboxBursts => (Tier switch { 0 => 7, 1 => 11, 2 => 16, _ => 22 }) + (Evolved ? 3 : 0);
    private int EasyRainBursts => (Tier switch { 0 => 3, 1 => 5, 2 => 5, _ => 7 }) + (Evolved ? 2 : 0);
    private double EasyDeliveryBurstInterval => Math.Max(Tier switch { 0 => .32, 1 => .24, 2 => .18, _ => .14 }, Evolved ? .25 : .22);
    private double EasyInboxBurstInterval => Tier switch { 0 => .18, 1 => .14, 2 => .11, _ => .09 };
    private double EasyRainBurstInterval => Tier switch { 0 => .25, 1 => .28, 2 => .22, _ => .19 };
    private const float EasyRainSpeed = 76, EasySplashRise = 68, EasySplashGravity = 48;
    private const float EasyHighSplashRise = 144;
    private const int EasyWaveSegments = 64;
    private int EasyWaveSpentSegments => 20 - Tier * 2 - (Evolved ? 2 : 0);
    private const float EasyWaveSpeed = 28, EasyWaveWaitRadius = 60;
    private float EasyWaveOuterSpeed => 144 + Tier * 12;
    private int EasyWavePulses => (Tier >= 2 ? 5 + Tier * 2 : 1 + Tier * 2) + (Evolved ? 2 : 0);
    private double EasyWavePulseInterval => Tier >= 2 ? .46 : .65;
    private double EasyWaveTrainDuration => EasyWaveDuration + (EasyWavePulses - 1) * EasyWavePulseInterval;
    private const double EasyWaveOpeningAt = 1.1,
        EasyWaveAccelerationTime = .45, EasyWaveRest = .08;
    private readonly double[] _easyWaveWarningStarts = { -1, -1 }, _easyWaveLaunches = { -1, -1 };
    private readonly Vector2[] _easyWaveWaitPoints = new Vector2[2];
    private readonly float[] _easyWaveGapAngles = new float[2];
    private readonly Vector2[] _easyDeliveryTargets = new Vector2[3], _easyInboxTargets = new Vector2[4];
    private readonly bool[] _easyDeliveryReserved = new bool[3], _easyInboxReserved = new bool[4];
    private readonly float[] _easyRainTargets = new float[EasyRainWaves * EasyRainRows];
    private readonly bool[] _easyRainReserved = new bool[EasyRainWaves * EasyRainRows];
    private sealed class RainParticle
    {
        public int Wave, Row, Column, Burst, Side;
        public Vector2 Origin;
        public double Launch, Duration;
        public Letter? Live;
        public RainParticle? Parent;
        public bool Fired, Landed;
    }
    private const double RainPreview = 2, RainForecastAt = .4;
    private double RainAttackSpeed => Tier == 2 ? .85 : 1;
    private double RainHold => Tier == 0 ? 2 : .6;
    private readonly List<RainParticle> _rainParticles = new();
    private readonly double[] _rainRowDurations = new double[EasyRainWaves * EasyRainRows];
    private double _rainModelEnd;
    private double RainAttackAt => RainForecastAt + RainPreview + RainHold;
    internal int? VisibleDangerousShotsAtNaturalEnd { get; private set; }
    internal int ProximityAddresses { get; private set; }
    internal bool AddressDeadlineMissed { get; private set; }
    internal IEnumerable<Vector2> UnaddressedLetters => _letters.Where(letter => letter.Alive && letter.State == 0).Select(letter => letter.Origin);

    protected override void Start()
    {
        _origin = new Vector2(Field.CenterX, Field.CenterY);
        _aim = Player.GlobalPosition;
        Array.Fill(_easyRainTargets, _aim.X);
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
            0 => ("速達の差戻し", "akari.returned-express", "書類は落下、封筒はカーブ。壁で生まれる紙飛行機は斜めに直進する"),
            1 => ("ふたつの焦がれる扇火", "akari.alternating-inboxes", "片方の発射が終わった側へ切り返す"),
            2 => ("綴じた下書きの取消", "akari.bound-draft-retraction", "複写の取消が端に届くとクリップが外れる。予約された進路から離れる"),
            3 => ("切り返す紅蓮の織目", "akari.unsent-history", "複写された経路の取消順を読み、紙面の間へ移る"),
            4 => ("追いかける返信の針路", "akari.wall-reply", "壁で一度反射する返信の先まで読む"),
            5 => ("降り積もる未送信", "akari.filing-piles", "書類は予告された列に降り積もる。列の間を抜け、山が崩れたら次の予告を読む"),
            6 => ("豪雨予報", "akari.rain-splash", "雨柱の着地後、床から横へ飛ぶしぶきも越す"),
            7 => ("沈黙の波紋", "akari.spent-wavefront", "中心から離れ、外へ伝わる波が薄れたら内側へ入り直す"),
            8 => ("狐火・三尾の便り", "akari.braided-delivery", "三つの束の前後を読み、ほどけて飛ぶ束を見送る"),
            9 => ("逃がさない締切", "akari.dwell-timecard", "滞在で早まる締切を読み、確定した印影の余白へ移る"),
            10 => ("交差する焦燥", "akari.exchanged-priority", "速い便と遅い便の交換後の到着順で横断する"),
            11 => ("巡回する承認待ち", "akari.carried-approval", "角に届いた案件の開封を避け、予約された承認印の外へ移る"),
            12 => ("駆け抜ける残業", "akari.extending-clock", "針が伸縮し、途中で軸が滑る。動く軸を追うか、短くなる針の端を回る"),
            13 => ("ほどけない焦り", "akari.tensioned-clips", "二つのクリップを見て、糸が張る前に広い側へ移る"),
            14 => ("狐火・締切の境界", "akari.coincident-membranes", Tier >= 2
                ? "境界の列が重なって交差する。動き出す前に、斜線の帯の外へ移る"
                : "二本の境界が交差して広がる。動き出す前に、斜線の帯の外へ移る"),
            15 => ("三秒の取消", "akari.exact-retraction", "送った線を覚え、三秒後に同じ線を戻る便をかわす"),
            16 => ("鳴り続ける内線", "akari.paused-outbox", "保留中の束を渡り、表示された内線番号と転送方向を見て、先に動く束を見送る"),
            17 => ("狐火・二本の置き傘", "akari.solid-umbrellas", "雨を実際に受け止める傘の下へ入り、傘が傾く前に移る"),
            18 => ("狐火・宛名のない封書", "akari.proximity-addressing", "近づいて宛先を確定させ、予告中に横へ離れる"),
            19 => ("既読の転送先", "akari.inbox-transfer", "受信した箱から別の箱へ便が渡る。続きの便は歩く先を宛先に取るので、確定した転送線を横切る"),
            _ => ("溜め込んだため息", "akari.local-breath-vortex", "息の輪へ入った紙だけが曲がる。横風をかわし、息をつぎ直したら縦風の輪を読む"),
        };
        Identify(identity.Item1, identity.Item2, identity.Item3);
        if (_mechanic == 12 && Tier >= 2)
            Identify(identity.Item1, identity.Item2, "時計の軸から離れ、伸びる針の間を回転に合わせて移動する");
        if (_mechanic == 11)
        {
            var anchors = EasyEnvelopeTravelPath();
            var profile = GD.Load<Curve2D>("res://assets/attack_curves/akari_carrier.tres");
            _carrierRoutes = Enumerable.Range(0, 5).Select(index =>
                AttackCurveRoute.Between(profile, anchors[index], anchors[index + 1], index == 4 ? 0 : 1)).ToArray();
            var forecast = new CanvasGroup {
                ZIndex = 0, ZAsRelative = false, FitMargin = 3, ClearMargin = 4,
                Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/attack_forecast_union.gdshader") },
            };
            _carrierWarningCanvas = new Node2D();
            _carrierWarningCanvas.Draw += () => {
                if (Finished || !GameManager.ShowAttackTelegraphs) return;
                foreach (var hazard in HazardShapes.Where(h => !h.Live && h.Shape == 2))
                    _carrierWarningCanvas.DrawRect(new Rect2(hazard.A, hazard.B - hazard.A).Intersection(Field.Rect), Colors.White);
            };
            forecast.AddChild(_carrierWarningCanvas);
            AddChild(forecast);
        }
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
        if (_mechanic == 12)
        {
            _angle = (_aim - _origin).Angle() - Mathf.Tau * (3.75f / 7.5f);
        }
        if (_mechanic == 14)
        {
            PrepareDeadline();
            _warning = _deadlineFire;
            if (Tier >= 2)
                Identify(identity.Item1, identity.Item2, "締切ごとに二枚の境界が閉じて開く。次の移動範囲を読み、広い側へ抜け直す");
        }
        if (_mechanic == 17)
        {
            _points[0] = new Vector2(Field.Left + Field.Width * .28f, 104);
            _points[1] = new Vector2(Field.Left + Field.Width * .72f, 134);
            _warning = WarnFor(92, 2);
            if (Tier >= 2)
            {
                _warning = WarnFor(_points.Take(2).Min(point => _aim.DistanceTo(point + Vector2.Down * 38)) + 45, 4.2);
                Identify(identity.Item1, identity.Item2, "雨を受け止める傘の下へ入り、揺れる持ち手に合わせて雨陰を保つ");
            }
        }
        if (_mechanic == 18)
        {
            int count = 4 + Tier;
            for (int i = 0; i < count; i++)
                _points[i] = _origin + new Vector2(Mathf.Cos(i * Mathf.Tau / count) * 92, Mathf.Sin(i * Mathf.Tau / count) * 62);
            Identify(identity.Item1, identity.Item2, "近づいた方向に封書の宛先が決まる。点線を横切って別の封書へ回り、締切を過ぎた便の射出もかわす");
        }
        if (Tier >= 2 && _mechanic == 16) _warning = WarnFor(255, 5.3);
        if (Tier >= 2 && _mechanic == 10) _warning = WarnFor(280, 6.2);
        if (Tier == 3 && _mechanic == 10)
            Identify(identity.Item1, identity.Item2, "予約した場所へ二つの便が交差する。速い便と遅い便の入替えを読み、次の予約が確定したら移り直す");
        if (Tier == 3 && _mechanic == 16)
            Identify(identity.Item1, identity.Item2, "保留中の束を抜ける。次の内線は現在の側へ折り返すので、発生側と再開番号を読み直す");
        if (Tier == 3 && _mechanic == 17)
            Identify(identity.Item1, identity.Item2, "揺れる傘の雨陰を保つ。雨を受けすぎた傘が閉じる前に、陰が重なったところで持ち替える");
        if (_mechanic == 19)
        {
            _points[0] = new Vector2(Field.CenterX - 52, 58);
            _points[1] = new Vector2(Field.Right - 36, 154);
        }
        if (_mechanic == 20) PrepareBreathLanes();
        if (_mechanic is 0 or 1 or 2 or 5 or 6 or 11 or 12)
            _warning = Math.Max(_warning, _mechanic is 0 or 11 or 12 ? 3.6 : 3.2);
        // 両端に列がある二組目も、最後の列の予兆完成から最初の落下まで2秒を確保する。
        if (_mechanic == 5 && Tier <= 1) _warning += EasyPileColumns * EasyPileColumnInterval + .05;
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
            case 20: BreathVortex(); break;
        }
        LastPosition = _origin;
    }

    private Letter Send(Vector2 at, Vector2 velocity, int group = 0, int pattern = 1)
    {
        var shot = Shot(at, velocity == Vector2.Zero ? Vector2.Down : velocity, pattern);
        if (_mechanic == 10 && group == 1) shot.UseBossIllustration(ShotVisual(0), fitRadius: shot.IllustrationRadius);
        if (UsesCustomHazardArt && _mechanic == 6)
            shot.UseBossIllustration(RainVisual(group == 10), fitRadius: EasyRainRadius());
        if (_mechanic == 2) shot.UseBossIllustration(EasyUnbindingShotVisual(),
            fitRadius: Tier == 2 ? HardDraftRadius / EasyUnbindingScale : null);
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
    internal double DeliveryForecastLead => Tier == 0 ? 2.0 : 1.2;
    private float EasyInboxWarningProgress(int turn) => Progress(EasyInboxLaunch(turn) - EasyInboxWarningLead + .4, .8);
    private (float Incoming, float Outgoing) EasyReplyWarningProgress() => (Progress(.2, .45), Progress(.9, .45));
    private float EasyPileWarningProgress(int column) => Progress(EasyPileStart(column) - _warning + .4, .8);
    private (float Ring, float Expiry) EasyWaveWarningProgress(int wave)
        => (Progress(_easyWaveWarningStarts[wave] + .4, .8), Progress(_easyWaveWarningStarts[wave] + 1.2, .5));
    private float EasyEnvelopeTravelWarningProgress() => EasyWarningRouteProgress(0, 1.4);
    private float EasyEnvelopeOpeningWarningProgress(int opening) => EasyWarningRouteProgress(EasyApprovalWarningStart(opening));
    private float EasyClockWarningProgress() => Progress(.4, 1.2);

    private Vector2[] EasyHistoryCopyPoints(int copy)
    {
        float minX = _history.Min(point => point.X), maxX = _history.Max(point => point.X);
        float minY = _history.Min(point => point.Y), maxY = _history.Max(point => point.Y);
        int columns = _mechanic == 2 ? Tier == 2 ? EasyUnbindingCopiesPerRound : 4 + Tier * 2 : 4;
        int rows = _mechanic == 2 ? Mathf.CeilToInt(EasyUnbindingCopiesPerRound * EasyUnbindingRounds / (float)columns) : 3;
        Vector2 cell = new(Field.Width / columns, Field.Height / rows);
        Vector2 center = new(Field.Left + (copy % columns + .5f) * cell.X, Field.Top + (copy / columns + .5f) * cell.Y);
        Vector2 sourceCenter = new((minX + maxX) * .5f, (minY + maxY) * .5f);
        float sourceWidth = Tier == 2 && _mechanic == 2 ? Field.Width / 8 : cell.X;
        float scale = Math.Min(1, Math.Min((sourceWidth - 24) / Math.Max(1, maxX - minX),
            (cell.Y - 24) / Math.Max(1, maxY - minY)));
        return _history.Select(point => center + (point - sourceCenter) * scale).ToArray();
    }

    private static BulletArt.PlayerVisual EasyRainShotVisual()
    {
        if (_easyRainShot != null) return _easyRainShot;
        var texture = GD.Load<Texture2D>("res://char/v3/fx/akari/easy_rain_drop_v1.png");
        Rect2 region = UiKit.ContentRect(texture);
        return _easyRainShot = new BulletArt.PlayerVisual(texture, region, region.GetCenter(), new Color("b3e4eb"));
    }

    private float EasyRainRadius()
    {
        var art = EasyRainShotVisual();
        return BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position,
            Bullet.BossArtSize(Evolved ? BulletPool.BossChargeRadius : BulletPool.BossRadius, Evolved)).Radius;
    }

    private static BulletArt.PlayerVisual RainVisual(bool rebound) => rebound
        ? BulletArt.EnemyVisual("res://assets/fx/akari_splash.svg", "c6a0ff")
        : BulletArt.EnemyVisual("res://char/v3/fx/akari/easy_rain_drop_v1.png", "ffbd70");

    protected override BulletArt.PlayerVisual WarningVisual => _mechanic switch
    {
        0 or 5 or 17 => BulletArt.EnemyVisual("akari_docs", "ffbd70"),
        1 => BulletArt.EnemyVisual("akari_sticky", "b1e58c"),
        2 or 15 => BulletArt.EnemyVisual("akari_clip", "f8d879"),
        6 => RainVisual(false),
        8 => BulletArt.EnemyVisual("res://assets/fx/akari_parcel.svg", "ff9b80"),
        19 => BulletArt.EnemyVisual("res://char/v3/fx/akari/card_unsent_2.png", "8bd9ed"),
        10 => BulletArt.EnemyVisual("akari_stamp", "ef90d1"),
        11 => BulletArt.EnemyVisual("res://assets/fx/akari_approval_seal.svg", "f6d38d"),
        16 => BulletArt.EnemyVisual("res://assets/fx/akari_outbox.svg", "d8b0ff"),
        18 => BulletArt.EnemyVisual("res://assets/fx/akari_address_tag.svg", "ffe1a0"),
        _ => BulletArt.EnemyVisual("akari_envelope", "ff7db4"),
    };

    protected override BulletArt.PlayerVisual ShotVisual(int pattern) => (_mechanic, pattern) switch
    {
        (10, 0) => BulletArt.EnemyVisual("akari_docs", "ffbd70"),
        (19, 1) => BulletArt.EnemyVisual("res://assets/fx/akari_read_receipt.svg", "ff7db4"),
        (20, 0) => BulletArt.EnemyVisual("res://assets/fx/akari_memo_strip.svg", "ffbd70"),
        (20, _) => BulletArt.EnemyVisual("res://assets/fx/akari_curled_memo.svg", "b1e58c"),
        _ => WarningVisual,
    };

    private static BulletArt.PlayerVisual EasyUnbindingShotVisual()
    {
        if (_easyUnbindingShot != null) return _easyUnbindingShot;
        var texture = GD.Load<Texture2D>("res://char/v3/bullets/akari_clip.png");
        Rect2 region = UiKit.ContentRect(texture);
        return _easyUnbindingShot = new BulletArt.PlayerVisual(texture, region, region.GetCenter(), new Color("f8d879"));
    }

    private float EasyWarningHalfWidth(int pattern)
    {
        if (Tier == 2 && _mechanic == 2) return HardDraftRadius;
        if (Tier == 2 && _mechanic is 1 or 9) return 4;
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
        float pitch = Field.Width / EasyDeliveryLanes;
        for (int column = 0; column < EasyDeliveryLanes; column++)
        {
            float x = Field.Left + Mathf.PosMod(_easyDeliveryTargets[batch].X - Field.Left
                + (column - EasyDeliveryLanes / 2) * pitch, Field.Width);
            Vector2 start = new(x, Field.Top);
            yield return (start, new Vector2(x, Field.Bottom));
        }
    }

    private double EasyDeliveryWarningStart(int batch)
    {
        double interval = Tier switch { 0 => 5.4, 1 => 4.1, 2 => 3.9, _ => 3.35 };
        return batch * interval + (Tier >= 2 && batch > 0 ? _warning + 1.4 - interval : 0);
    }
    private double EasyDeliveryLaunch(int batch) => EasyDeliveryWarningStart(batch) + _warning;
    internal readonly record struct DeliveryPath(Vector2 OrdinaryStart, Vector2 Start, Vector2 Turn, Vector2 Bounce, Vector2 Exit)
    {
        internal Vector2 Across => Turn.DirectionTo(Bounce);
        internal float Radius => Math.Min(12, Math.Min(Start.DistanceTo(Turn) * .5f, Turn.DistanceTo(Bounce) * .7f));
        internal Vector2 CurveEntry => Turn - Vector2.Down * Radius;
        internal Vector2 CurveExit => Turn + Across * Radius;
        internal double SlowDistance => Math.Min(24, Start.DistanceTo(CurveEntry));
        internal double FastTime => (Start.DistanceTo(CurveEntry) - SlowDistance) / 88;
        internal double SlowTime => SlowDistance * 2 / (88 + 44);
        internal double TurnAt => FastTime + SlowTime;
        internal double StraightAt => TurnAt + Radius * Math.PI / (2 * 44);
        internal double AccelerationTime => Math.Min(.35, CurveExit.DistanceTo(Bounce) / 61);
        internal double BounceAt => StraightAt + AccelerationTime
            + (CurveExit.DistanceTo(Bounce) - AccelerationTime * 61) / 78;
        internal double Delay => Math.Max(0, (Turn.Y - OrdinaryStart.Y) / 27 - StraightAt);
    }

    private DeliveryPath[] EasyDeliveryReturnPaths(int batch)
        => EasyDeliveryWarningPaths(batch).Select((path, lane) =>
        {
            float y = Tier == 0 ? _easyDeliveryTargets[batch].Y + (lane - 1) * 24
                : lane == EasyDeliveryLanes / 2 ? _easyDeliveryTargets[batch].Y
                : 24 + lane * (Field.Height - 40) / (EasyDeliveryLanes - 1);
            y = Mathf.Clamp(y, Field.Top + 24, Field.Bottom - 16);
            Vector2 start = path.Start + Vector2.Right * Field.Width / (EasyDeliveryLanes * 2);
            if (start.X > Field.Right) start.X -= Field.Width;
            Vector2 turn = new(start.X, y);
            bool left = _easyDeliveryTargets[batch].X < Field.CenterX;
            if (Math.Abs(start.X - (left ? Field.Left : Field.Right)) < 18) left = !left;
            Vector2 bounce = new(left ? Field.Left : Field.Right, y);
            Vector2 reply = new(left ? 1 : -1, batch % 2 == 0 ? .22f : -.22f);
            Vector2 exit = EasyRayExit(bounce, reply.Normalized());
            return new DeliveryPath(path.Start, start, turn, bounce, exit);
        }).ToArray();

    internal enum DeliveryKind { FallingDocument, TurningEnvelope, DescendingReply, AscendingReply }
    private static readonly Dictionary<DeliveryKind, BulletArt.PlayerVisual> DeliveryVisuals = new();

    private static BulletArt.PlayerVisual DeliveryVisual(DeliveryKind kind)
    {
        if (DeliveryVisuals.TryGetValue(kind, out var visual)) return visual;
        Texture2D texture = kind switch
        {
            DeliveryKind.FallingDocument => BulletArt.AkariDocs!,
            DeliveryKind.TurningEnvelope => BulletArt.AkariEnvelope!,
            _ => GD.Load<Texture2D>("res://assets/fx/akari_reply_plane.svg"),
        };
        Rect2 region = UiKit.ContentRect(texture);
        Color accent = kind switch
        {
            DeliveryKind.FallingDocument => new("ffd9a8"),
            DeliveryKind.TurningEnvelope => new("ffb0bd"),
            _ => new("b8e4ef"),
        };
        return DeliveryVisuals[kind] = new(texture, region, region.GetCenter(), accent);
    }

    internal float DeliveryWarningHalfWidth(DeliveryKind kind)
    {
        var visual = DeliveryVisual(kind);
        float radius = Evolved ? BulletPool.BossChargeRadius : BulletPool.BossRadius;
        return BulletFootprint.Texture(visual.Cropped, visual.Pivot - visual.Region.Position, Bullet.BossArtSize(radius, Evolved)).Radius;
    }

    private Letter SendDelivery(Vector2 at, Vector2 velocity, int group, DeliveryKind kind)
    {
        var letter = Send(at, velocity, group);
        letter.DeliveryType = kind;
        letter.Shot.UseBossIllustration(DeliveryVisual(kind));
        letter.Shot.CancelLeadIn();
        letter.Shot.BossPathDriven = true;
        return letter;
    }

    internal readonly record struct DeliveryForecast(int Group, DeliveryKind Kind, Vector2[] Points, Vector2 Direction);

    internal IEnumerable<DeliveryForecast> DeliveryForecasts()
    {
        for (int batch = 0; batch < 3; batch++)
        {
            if (!_easyDeliveryReserved[batch]) continue;
            var paths = EasyDeliveryReturnPaths(batch);
            for (int lane = 0; lane < paths.Length; lane++)
            for (int part = 0; part < 3; part++)
            {
                var path = paths[lane];
                bool express = part != 0;
                DeliveryKind kind = part == 0 ? DeliveryKind.FallingDocument : part == 1 ? DeliveryKind.TurningEnvelope
                    : path.Exit.Y > path.Bounce.Y ? DeliveryKind.DescendingReply : DeliveryKind.AscendingReply;
                double fire = EasyDeliveryLaunch(batch) + (express ? path.Delay : 0);
                double age = Time + DeliveryForecastLead - fire;
                if (age < 0 || part == 1 && age >= path.BounceAt || part == 2 && age < path.BounceAt) continue;
                var head = DeliverySample(path, express, age);
                if (!Field.Rect.Grow(24).HasPoint(head.Position)) continue;
                double tail = Math.Max(part == 2 ? path.BounceAt : 0, age - .18);
                var times = Enumerable.Range(0, 7).Select(step => tail + (age - tail) * step / 6).ToList();
                var points = times.Order().Select(at => DeliverySample(path, express, at).Position).ToArray();
                yield return new DeliveryForecast((part == 0 ? 1000 : part == 1 ? 0 : 2000) + batch * EasyDeliveryLanes + lane, kind,
                    points, part == 2 ? path.Bounce.DirectionTo(path.Exit)
                        : points[points.Length / 2].DirectionTo(points[points.Length / 2 + 1]));
            }
        }
    }

    internal static (Vector2 Position, Vector2 Velocity, int State) DeliverySample(DeliveryPath path, bool express, double age)
    {
        if (!express) return (path.OrdinaryStart + Vector2.Down * (float)age * 27, Vector2.Down * 27, 0);
        if (age < path.FastTime) return (path.Start + Vector2.Down * (float)age * 88, Vector2.Down * 88, 0);
        if (age < path.TurnAt)
        {
            double t = age - path.FastTime, acceleration = -44 / path.SlowTime;
            return (path.Start + Vector2.Down * (float)(path.FastTime * 88 + 88 * t + acceleration * t * t / 2),
                Vector2.Down * (float)(88 + acceleration * t), 0);
        }
        if (age < path.StraightAt)
        {
            float angle = (float)((age - path.TurnAt) * 44 / path.Radius);
            return (path.CurveEntry + path.Across * path.Radius * (1 - Mathf.Cos(angle))
                + Vector2.Down * path.Radius * Mathf.Sin(angle),
                (path.Across * Mathf.Sin(angle) + Vector2.Down * Mathf.Cos(angle)) * 44, 1);
        }
        if (age < path.BounceAt)
        {
            double t = age - path.StraightAt, ramp = Math.Min(t, path.AccelerationTime);
            double distance = 44 * ramp + 17 * ramp * ramp / path.AccelerationTime + Math.Max(0, t - ramp) * 78;
            return (path.CurveExit + path.Across * (float)distance,
                path.Across * (float)(44 + 34 * ramp / path.AccelerationTime), 1);
        }
        Vector2 reply = path.Bounce.DirectionTo(path.Exit);
        return (path.Bounce + reply * (float)(age - path.BounceAt) * 78, reply * 78, 2);
    }

    private void EasyReturnedDelivery()
    {
        var pathsByBatch = new DeliveryPath[3][];
        for (int batch = 0; batch < 3; batch++)
        {
            if (Time < EasyDeliveryWarningStart(batch)) continue;
            if (!_easyDeliveryReserved[batch])
            {
                _easyDeliveryTargets[batch] = Player.GlobalPosition;
                _easyDeliveryReserved[batch] = true;
            }
            double launch = EasyDeliveryLaunch(batch);
            var paths = pathsByBatch[batch] = EasyDeliveryReturnPaths(batch);
            for (int lane = 0; lane < paths.Length; lane++)
            {
                var path = paths[lane];
                double delay = path.Delay;
                for (int burst = 0; burst < EasyDeliveryBursts; burst++)
                {
                    double pulse = burst * EasyDeliveryBurstInterval;
                    if (At(launch + pulse))
                    {
                        SendDelivery(path.OrdinaryStart, Vector2.Down * 27,
                            1000 + burst * (3 * EasyDeliveryLanes) + batch * EasyDeliveryLanes + lane, DeliveryKind.FallingDocument);
                    }
                    if (At(launch + delay + pulse))
                    {
                        SendDelivery(path.Start, Vector2.Down * 88,
                            burst * (3 * EasyDeliveryLanes) + batch * EasyDeliveryLanes + lane, DeliveryKind.TurningEnvelope);
                    }
                }
            }
        }
        foreach (var cue in DeliveryForecasts())
            for (int segment = 1; segment < cue.Points.Length; segment++)
                Line(cue.Points[segment - 1], cue.Points[segment], DeliveryWarningHalfWidth(cue.Kind), false);
        foreach (var letter in _letters.Where(letter => letter.Alive).ToArray())
        {
            int index = letter.Group % 1000;
            int batch = index % (3 * EasyDeliveryLanes) / EasyDeliveryLanes;
            int lane = index % EasyDeliveryLanes;
            int burst = index / (3 * EasyDeliveryLanes);
            bool express = letter.DeliveryType != DeliveryKind.FallingDocument;
            var path = pathsByBatch[batch][lane];
            double fire = EasyDeliveryLaunch(batch) + burst * EasyDeliveryBurstInterval
                + (express ? path.Delay : 0);
            var sample = DeliverySample(path, express, Math.Max(0, Time - fire));
            if (letter.DeliveryType == DeliveryKind.TurningEnvelope && sample.State == 2)
            {
                Pool.Despawn(letter.Shot);
                var reply = SendDelivery(path.Bounce, sample.Velocity, letter.Group + 2000,
                    sample.Velocity.Y > 0 ? DeliveryKind.DescendingReply : DeliveryKind.AscendingReply);
                reply.Shot.GlobalPosition = sample.Position;
                reply.Shot.Rotation = sample.Velocity.Angle();
                reply.State = 2;
                continue;
            }
            letter.Shot.GlobalPosition = sample.Position;
            letter.Shot.Velocity = sample.Velocity;
            letter.Shot.Rotation = letter.DeliveryType == DeliveryKind.FallingDocument ? 0 : sample.Velocity.Angle();
            letter.State = sample.State;
            if (sample.State == 1)
            {
                letter.TurnPoint = path.Turn;
                letter.Changed = fire + path.TurnAt;
            }
            else if (sample.State == 2)
            {
                letter.PreviousTurnPoint = path.Turn;
                letter.PreviousChanged = fire + path.TurnAt;
                letter.TurnPoint = path.Bounce;
                letter.Changed = fire + path.BounceAt;
            }
        }
        EndAfter(EasyDeliveryLaunch(2) + EasyDeliveryReturnPaths(2)
            .Max(path => path.Delay)
            + (EasyDeliveryBursts - 1) * EasyDeliveryBurstInterval + .02);
    }

    private readonly Dictionary<int, FanRay[]> _inboxRays = new();
    private Vector2 EasyInboxSource(int turn) => new(turn % 2 == 0 ? Field.Left + 16 : Field.Right - 16, Field.CenterY);
    private double EasyInboxLaunch(int turn) => _warning + turn * (Tier switch { 0 => 4.1, 1 => 3.2, 2 => 3.05, _ => 2.85 })
        + (Tier >= 2 && turn > 0 ? 1.8 : 0);
    private bool EasyInboxOpen(int side) => Enumerable.Range(0, 4).Any(turn => turn % 2 == side
        && _easyInboxReserved[turn] && Time < EasyInboxLaunch(turn) + EasyInboxBursts * EasyInboxBurstInterval);

    private IEnumerable<(Vector2 Start, Vector2 Exit)> EasyInboxWarningPaths(int turn)
    {
        Vector2 source = EasyInboxSource(turn);
        float aim = source.DirectionTo(_easyInboxTargets[turn]).Angle();
        if (Tier == 2)
        {
            if (!_inboxRays.TryGetValue(turn, out var rays))
                _inboxRays[turn] = rays = SplitFan(source, Vector2.FromAngle(aim), .96f, turn % 2 == 0 ? .18f : -.18f);
            foreach (var ray in rays) yield return (ray.From, EasyRayExit(ray.From, ray.Direction));
            yield break;
        }
        int spread = 2 + Tier * 2;
        for (int row = -spread; row <= spread; row++)
        {
            Vector2 direction = Vector2.FromAngle(aim + row * (Tier == 0 ? .19f : .16f));
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

    private float HardDraftRadius => 4;
    private int EasyUnbindingCopiesPerRound => 3 + Tier * 3;
    private const int EasyUnbindingRounds = 4;
    private int EasyUnbindingBurstCount => EasyDeliveryBursts;
    private const double EasyUnbindingCopyInterval = .18, EasyUnbindingRoundInterval = 4.6;
    private double EasyUnbindingBurstInterval => .24 - Tier * .035;
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
    private (float Retrace, float Exit) EasyUnbindingWarningProgress(int round)
        => (Progress(EasyUnbindingWarningStart(round) + .2, .35), Progress(EasyUnbindingWarningStart(round) + .55, .35));
    private Vector2[] EasyUnbindingCopyPoints(int copy)
    {
        int round = copy / EasyUnbindingCopiesPerRound;
        int stack = copy % EasyUnbindingCopiesPerRound;
        if (Tier != 0 || stack > 1)
            return EasyHistoryCopyPoints(copy);
        if (stack == 0) return _history.ToArray();
        Vector2 shift = EasyUnbindingTarget(round) - _history[^1];
        return _history.Select(point => (point + shift).Clamp(Field.Rect.Position + Vector2.One * 5,
            Field.Rect.End - Vector2.One * 5)).ToArray();
    }

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
        for (int copy = 0; copy < first + EasyUnbindingCopiesPerRound; copy++)
        {
            if (copy >= first && Time < EasyUnbindingFire(first))
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
        if (Time < recording) Cue(Player.GlobalPosition + new Vector2(-20, -15), AttackTelegraph.Symbol.Record, Progress(0, recording));
        else if (Time < recording + _warning) Cue(_history[^1] + new Vector2(-20, -12), AttackTelegraph.Symbol.Undo);
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
            Cue(wall + new Vector2(incoming.X < 0 ? 6 : -42, -12), AttackTelegraph.Symbol.Undo);
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

    private static Vector2 RainPosition(RainParticle particle, double age)
    {
        float t = (float)age;
        if (particle.Side == 0) return particle.Origin + Vector2.Down * EasyRainSpeed * t;
        float rise = EasyRainReboundRise(particle.Origin, particle.Side);
        return particle.Origin + new Vector2(particle.Side * 50 * t, -rise * t + EasySplashGravity * t * t / 2);
    }

    private static Vector2 RainVelocity(RainParticle particle, double age)
        => particle.Side == 0 ? Vector2.Down * EasyRainSpeed
            : new Vector2(particle.Side * 50, -EasyRainReboundRise(particle.Origin, particle.Side) + EasySplashGravity * (float)age);

    private static double RainRowModelStart(int wave, int row)
        => wave * EasyRainWaveInterval + row * EasyRainRowInterval;

    private double RainWarningAt(int wave, int row)
        => RainForecastAt + RainRowModelStart(wave, row) / RainAttackSpeed;

    private double RainForecastAge(RainParticle particle)
        => (Time - RainWarningAt(particle.Wave, particle.Row)) / RainPreview
            * _rainRowDurations[particle.Wave * EasyRainRows + particle.Row]
            - (particle.Launch - RainRowModelStart(particle.Wave, particle.Row));

    private void PrepareRainRow(int wave, int row)
    {
        int index = wave * EasyRainRows + row;
        _easyRainTargets[index] = Player.GlobalPosition.X;
        _easyRainReserved[index] = true;
        double fall = (Field.Height + 12) / EasyRainSpeed;
        for (int column = 0; column < EasyRainColumns; column++)
        for (int burst = 0; burst < EasyRainBursts; burst++)
        {
            double launch = wave * EasyRainWaveInterval + row * EasyRainRowInterval + burst * EasyRainBurstInterval;
            Vector2 origin = EasyRainOrigin(wave, row, column), impact = EasyRainImpact(wave, row, column);
            var rain = new RainParticle { Wave = wave, Row = row, Column = column,
                Burst = burst, Origin = origin, Launch = launch, Duration = fall };
            _rainParticles.Add(rain);
            for (int side = -1; side <= 1; side += 2)
            {
                float rise = EasyRainReboundRise(impact, side);
                double horizontal = ((side > 0 ? Field.Right + 24 : Field.Left - 24) - impact.X) / (side * 50);
                double vertical = (rise + Math.Sqrt(rise * rise + 2 * EasySplashGravity * 24)) / EasySplashGravity;
                double duration = Math.Min(horizontal, vertical);
                _rainParticles.Add(new RainParticle { Wave = wave, Row = row, Column = column,
                    Burst = burst, Side = side, Origin = impact, Launch = launch + fall, Duration = duration, Parent = rain });
                _rainModelEnd = Math.Max(_rainModelEnd, launch + fall + duration);
                _rainRowDurations[index] = Math.Max(_rainRowDurations[index],
                    launch + fall + duration - RainRowModelStart(wave, row));
            }
        }
    }

    private void RainSplash()
    {
        if (Time == 0) Boss.AnnounceTechnique(SpellName, 3.6, compact: true);
        for (int wave = 0; wave < EasyRainWaves; wave++)
        for (int row = 0; row < EasyRainRows; row++)
            if (!_easyRainReserved[wave * EasyRainRows + row]
                && Time >= RainWarningAt(wave, row))
                PrepareRainRow(wave, row);
        if (Time < RainAttackAt) return;
        double clock = (Time - RainAttackAt) * RainAttackSpeed;
        foreach (var particle in _rainParticles)
        {
            double age = clock - particle.Launch;
            if (age < 0) continue;
            if (!particle.Fired)
            {
                particle.Fired = true;
                if (particle.Parent is { Landed: false }) continue;
                var letter = Send(particle.Origin, RainVelocity(particle, 0) * (float)RainAttackSpeed,
                    particle.Side == 0 ? particle.Wave : 10, particle.Side == 0 ? 0 : 2);
                particle.Live = letter;
                letter.Shot.UseBossIllustration(RainVisual(particle.Side != 0), fitRadius: EasyRainRadius());
                letter.Shot.BossBarrageWave = particle.Wave;
                letter.Shot.BossPathDriven = true;
                letter.Shot.BoundsMargin = 24;
                letter.Shot.CancelLeadIn();
            }
            if (particle.Live is not { Alive: true } live) continue;
            if (clock >= particle.Launch + particle.Duration)
            {
                particle.Landed = particle.Side == 0;
                Pool.Despawn(live.Shot);
                continue;
            }
            live.Shot.GlobalPosition = RainPosition(particle, age);
            live.Shot.Velocity = RainVelocity(particle, age) * (float)RainAttackSpeed;
            live.Shot.Rotation = live.Shot.Velocity.Angle();
        }
        if (_easyRainReserved.All(reserved => reserved))
            EndAfter(RainAttackAt + _rainModelEnd / RainAttackSpeed);
    }

    private void SpentWavefront() => EasySpentWavefront();

    private double EasyPileSwitchAt => _warning + (EasyPileColumns - 1) * EasyPileColumnInterval
        + EasyPileCollapseAt + (EasyPileRows - 1) * EasyPileCollapseInterval + .06
        - _warning + (Tier >= 2 ? .6 : .85);
    private int EasyPileColumnCount => EasyPileColumns * 2 + 1;
    private static int EasyPileColumnPass(int column) => column < EasyPileColumns ? 0 : 1;
    private static int EasyPileLocalColumn(int column) => column >= EasyPileColumns ? column - EasyPileColumns : column;
    private float EasyPileX(int column) => Field.Left
        + (EasyPileLocalColumn(column) + (EasyPileColumnPass(column) == 0 ? .5f : 0)) * Field.Width / EasyPileColumns;
    private Rect2 EasyPileLane(int column, float progress = 1) =>
        new Rect2(EasyPileX(column) - EasyPileWidth(column) / 2, Field.Top, EasyPileWidth(column), Field.Height * progress).Intersection(Field.Rect);
    private double EasyPileStart(int column) => _warning + EasyPileColumnPass(column) * EasyPileSwitchAt
        + (Tier >= 2 && EasyPileColumnPass(column) == 1 ? EasyPileColumns - EasyPileLocalColumn(column)
            : EasyPileLocalColumn(column)) * EasyPileColumnInterval;
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
        return new Rect2(EasyPileX(column) - EasyPileWidth(column) / 2, y, EasyPileWidth(column), EasyPileLayerHeight);
    }
    private Rect2 EasyPileStackBounds(int column)
        => new Rect2(EasyPileX(column) - EasyPileWidth(column) / 2, Field.Bottom - _states[column] * EasyPileLayerHeight,
            EasyPileWidth(column), _states[column] * EasyPileLayerHeight).Intersection(Field.Rect);

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
                    Box(EasyPileLane(column, progress), false, progress);
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

    private float EasyRainX(int wave, int row, int column)
    {
        float first = Field.Left + 16 + ((wave + row) % 3 - 1) * 7;
        float pitch = (Field.Width - 32) / (EasyRainColumns - 1);
        float target = _easyRainTargets[wave * EasyRainRows + row];
        int nearest = Mathf.Clamp(Mathf.RoundToInt((target - first) / pitch), 0, EasyRainColumns - 1);
        return column == nearest ? target : first + column * pitch;
    }
    private Vector2 EasyRainOrigin(int wave, int row, int column) => new(EasyRainX(wave, row, column), Field.Top - 12);
    private static Vector2 EasyRainImpactPoint(Vector2 position) => new(position.X, Field.Bottom);
    private Vector2 EasyRainImpact(int wave, int row, int column) => EasyRainImpactPoint(EasyRainOrigin(wave, row, column));
    private static float EasyRainReboundRise(Vector2 impact, int side)
        => (impact.X <= Field.CenterX ? side > 0 : side < 0) ? EasyHighSplashRise : EasySplashRise;

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
    private float EasyWaveGapAngle(int wave) => _easyWaveGapAngles[wave];
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
        for (int turn = 0; turn < 32; turn++)
        {
            _easyWaveGapAngles[wave] = _angle + wave * Mathf.Pi
                + (turn + 1) / 2 * (turn % 2 == 1 ? 1 : -1) * Mathf.Pi / 16;
            for (int sample = 0; sample <= 32; sample++)
            {
                float offset = Mathf.Lerp(-halfOpening, halfOpening, sample / 32f);
                Vector2 waiting = _origin + Vector2.FromAngle(EasyWaveGapAngle(wave) + offset) * EasyWaveWaitRadius;
                if (Boss.BodyContactDamaging && waiting.DistanceTo(Geometry2D.GetClosestPointToSegment(waiting, lower, upper)) <= clearance) continue;
                float travel = Player.GlobalPosition.DistanceTo(waiting);
                if (Boss.BodyContactDamaging && body.GlobalPosition.DistanceTo(Geometry2D.GetClosestPointToSegment(body.GlobalPosition, Player.GlobalPosition, waiting)) < detourRadius)
                    travel += Mathf.Pi * detourRadius;
                if (travel >= distance) continue;
                distance = travel;
                _easyWaveWaitPoints[wave] = waiting;
            }
            if (distance < float.MaxValue) break;
        }
        _easyWaveWarningStarts[wave] = start;
        _easyWaveLaunches[wave] = start + WarnFor(distance + 8, 3.8);
    }
    private void EasySpentWavefront()
    {
        double nextWarning = Tier == 0 ? Math.Max(.35, EasyWaveDuration - 2.6)
            : Math.Max(.45, (EasyWavePulses - 1) * EasyWavePulseInterval + EasyWaveOpeningAt - 1.25);
        if (_easyWaveLaunches[1] < 0 && Time >= _easyWaveLaunches[0] + nextWarning)
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
            for (int pulse = 0; pulse < EasyWavePulses; pulse++)
            {
                double age = elapsed - pulse * EasyWavePulseInterval;
                if (age < 0 || age >= EasyWaveDuration) continue;
                if (age < .3) Disc(_origin, 20, true);
                float radius = EasyWaveRadius(age);
                foreach (var range in EasyWaveLiveRanges(age))
                    for (int i = range.First; i < range.End; i++)
                        Line(EasyWavePoint(wave, i, radius), EasyWavePoint(wave, i + 1, radius), 2, true);
            }
        }
        if (_easyWaveLaunches[1] >= 0) EndAfter(_easyWaveLaunches[1] + EasyWaveTrainDuration + EasyWaveRest);
    }

    private Vector2? _braidReturnAddress;
    private double BraidNotice => Tier == 0 ? 2.3 : 1.45;
    private double BraidRelease(int bundle) => BraidNotice + 1.25 + bundle * 1.55;
    private Vector2 BraidKnot(double elapsed, int bundle)
    {
        float phase = (float)Math.Clamp(elapsed, 0, BraidRelease(bundle) - BraidNotice) * 1.65f + bundle * Mathf.Tau / 3;
        return Field.Rect.GetCenter() + new Vector2(Mathf.Sin(phase) * 77, Mathf.Cos(phase) * 48);
    }

    private void BraidedDelivery(double delta)
    {
        ReadRecipientMotion();
        if (Tier >= 2) { BraidedDispatch(); return; }
        if (Tier == 0 && Time >= 1.2)
        {
            _braidReturnAddress ??= Player.GlobalPosition;
            if (_states[2] < 2) Cue(_braidReturnAddress.Value, AttackTelegraph.Symbol.Received);
        }
        double elapsed = Time - _warning;
        int strands = 5 + Tier * 2;
        for (int bundle = 0; bundle < 3; bundle++)
        {
            Vector2 knot = BraidKnot(elapsed, bundle);
            if (elapsed < 0) Disc(knot, 12, false, Progress(0, _warning));
            if (At(_warning))
                for (int i = 0; i < strands; i++)
                    Send(knot + Vector2.Down * ((i - (strands - 1) * .5f) * 4), Vector2.Zero, bundle).CopyIndex = i;
            double release = BraidRelease(bundle), lockAt = release - BraidNotice;
            if (elapsed >= lockAt && _states[bundle] == 0)
            {
                _states[bundle] = 1;
                _points[bundle] = knot;
                Vector2 recipient = bundle == 1 ? LedRecipient(knot, 94 + Tier * 7, BraidNotice + .25) : Player.GlobalPosition;
                if (bundle == 2 && _braidReturnAddress is { } returnAddress) recipient = returnAddress;
                _points[bundle + 3] = knot.DirectionTo(recipient);
                if (_points[bundle + 3] == Vector2.Zero) _points[bundle + 3] = Vector2.Down;
            }
            if (_states[bundle] > 0) knot = _points[bundle];
            bool unwrapping = Tier == 0 && bundle == 2;
            foreach (var letter in _letters.Where(l => l.Alive && l.Group == bundle).ToArray())
            {
                float offset = letter.CopyIndex - (strands - 1) * .5f;
                double dispatch = unwrapping ? release + .45 + (strands - 1 - letter.CopyIndex) * .5 : release;
                Vector2 heading = _points[bundle + 3].Rotated(offset * (Tier == 0 && bundle == 2 ? .5f : .33f));
                Vector2 from = knot + (_states[bundle] == 0 ? Vector2.Down : heading.Orthogonal()) * (offset * 5);
                if (letter.State == 0 && !letter.Shot.LeadIn)
                    letter.Shot.GlobalPosition = letter.Shot.GlobalPosition.MoveToward(from, 150 * (float)delta);
                if (_states[bundle] > 0 && elapsed < dispatch)
                    Line(from, EasyRayExit(from, heading), 4, false, Progress(_warning + lockAt, .2));
                if (elapsed < dispatch || letter.State != 0) continue;
                letter.State = 1;
                letter.Shot.GlobalPosition = from;
                letter.Shot.UseBossIllustration(WarningVisual, fitRadius: 4);
                letter.Shot.Velocity = heading * 18;
                letter.Shot.MakeAccel(18, 94 + Tier * 7, .25f);
                letter.Shot.CancelLeadIn();
            }
            if (unwrapping && elapsed >= lockAt && elapsed < release + 2.45)
                Cue(knot + Vector2.Up * 16, AttackTelegraph.Symbol.Pips,
                    count: _letters.Count(l => l.Group == bundle && l.State == 0));
            if (elapsed >= release + (unwrapping ? 2.45 : 0)) _states[bundle] = 2;
        }
        EndAfter(_warning + BraidRelease(2) + (Tier == 0 ? 2.5 : .05));
    }

    private void BraidedDispatch()
    {
        int lanes = Tier == 2 ? 9 : 11, pulses = Tier == 2 ? 7 : 9;
        for (int bundle = 0; bundle < 3; bundle++)
        {
            double lockAt = _warning + bundle * 2.2, fire = lockAt + 1.6;
            float phase = (float)Math.Min(Time, lockAt) * 1.6f + bundle * Mathf.Tau / 3;
            Vector2 knot = Field.Rect.GetCenter() + new Vector2(Mathf.Sin(phase) * 90, Mathf.Cos(phase) * 58);
            if (Time < lockAt) Disc(knot, 14, false);
            if (At(lockAt))
            {
                _points[bundle] = knot;
                Vector2 recipient = bundle == 1 ? LedRecipient(knot, 72 + Tier * 5, 1.95) : Player.GlobalPosition;
                _points[bundle + 3] = knot.DirectionTo(recipient);
                _states[bundle] = 1;
            }
            if (_states[bundle] == 0) continue;
            for (int lane = 0; lane < lanes; lane++)
            {
                Vector2 direction = _points[bundle + 3].Rotated((lane - (lanes - 1) * .5f) * .19f);
                Vector2 from = _points[bundle] + direction.Orthogonal() * ((bundle - 1) * 6);
                if (Time < fire) Line(from, from + direction * 420, 4, false, Progress(lockAt, .45));
                for (int pulse = 0; pulse < pulses; pulse++)
                    if (At(fire + pulse * .2))
                    {
                        var letter = Send(from, direction * 18, bundle);
                        letter.Shot.UseBossIllustration(WarningVisual, fitRadius: 4);
                        letter.Shot.MakeAccel(18, 72 + Tier * 5, .35f);
                        letter.Shot.CancelLeadIn();
                    }
            }
        }
        EndAfter(_warning + 4.4 + 1.6 + (pulses - 1) * .2);
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
        if (Tier == 0)
        {
            Vector2 corner = EasyEnvelopeTravelPath()[index + 1];
            Vector2 inward = corner.DirectionTo(Field.Rect.GetCenter());
            fold = position.Clamp(Field.Rect.Position + Vector2.One * 12, Field.Rect.End - Vector2.One * 12);
            first = (fold - inward * 36 + inward.Orthogonal() * 32).Clamp(Field.Rect.Position, Field.Rect.End);
            last = (fold + inward * 56 + inward.Orthogonal() * 30).Clamp(Field.Rect.Position, Field.Rect.End);
        }
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
                Cue(stamp.Position + new Vector2(-12, -26), AttackTelegraph.Symbol.Stamp, Progress(stamp.At - 1.2, 1.2));
            }
        }
        Cue(_dwell + new Vector2(-18, 25), AttackTelegraph.Symbol.Record, (float)(_still / (.65 - Tier * .08)));
        EndAfter(_warning + 7.2);
    }

    private readonly Vector2[] _priorityTargets = new Vector2[4];
    private readonly bool[] _priorityBooked = new bool[4];
    private void ExchangedPriority()
    {
        if (Tier == 3) { LunaticExchangedPriority(); return; }
        if (Tier >= 2)
        {
            for (int round = 0; round < 2; round++)
            {
                double start = _warning + round * 6.6;
                double age = Time - start;
                if (age < -2.8 && round > 0) continue;
                for (int copy = 0; copy < 5; copy++)
                {
                double travelAge = age - copy * .38;
                float across = (float)(Math.Clamp(travelAge, 0, 1.6) * 70 + Math.Max(0, travelAge - 1.6) * 24);
                float down = (float)(Math.Clamp(travelAge, 0, 1.6) * 24 + Math.Max(0, travelAge - 1.6) * 70);
                float x = round == 0 ? Field.Left - 12 + across : Field.Right + 12 - across;
                float y = round == 0 ? Field.Top - 12 + down : Field.Bottom + 12 - down;
                if (age < 0 && copy == 0)
                {
                    Box(new Rect2(Field.Left, Field.Top + round * Field.Height / 2, Field.Width, Field.Height / 2), false);
                    Box(new Rect2(round == 0 ? Field.CenterX : Field.Left, Field.Top, Field.Width / 2, Field.Height), false);
                }
                else if (travelAge >= 0)
                {
                    if (x >= Field.Left - 8 && x <= Field.Right + 8)
                        Box(new Rect2(x - 6, Field.Top + round * Field.Height / 2, 12, Field.Height / 2), true);
                    if (y >= Field.Top - 8 && y <= Field.Bottom + 8)
                        Box(new Rect2(round == 0 ? Field.CenterX : Field.Left, y - 6, Field.Width / 2, 12), true);
                }
                }
            }
            EndAfter(_warning + 6.6 + PriorityExitTime(Field.Width + 24, true) + 4 * .38);
            return;
        }
        int batches = Tier == 0 ? 3 : 4, copies = Tier == 0 ? 5 : 7;
        double interval = Tier == 0 ? 4.05 : 3.4;
        for (int batch = 0; batch < batches; batch++)
        {
            double booked = batch * interval, launch = booked + _warning;
            if (Time < booked) continue;
            if (!_priorityBooked[batch])
            {
                _priorityTargets[batch] = Player.GlobalPosition;
                _priorityBooked[batch] = true;
            }
            Vector2 target = _priorityTargets[batch];
            int side = batch % 2 == 0 ? 1 : -1;
            int vertical = target.Y < Field.CenterY ? -1 : 1;
            for (int i = 0; i < copies; i++)
            {
                float offset = (i - (copies - 1) * .5f) * (Tier == 0 ? 21 : 17);
                Vector2 across = new(side > 0 ? Field.Left : Field.Right, Mathf.Clamp(target.Y + offset, Field.Top, Field.Bottom));
                Vector2 down = new(Mathf.Clamp(target.X + offset, Field.Left, Field.Right), vertical > 0 ? Field.Top : Field.Bottom);
                if (Time < launch)
                {
                    Line(across, EasyRayExit(across, Vector2.Right * side), 4, false, Progress(booked, .2));
                    Line(down, EasyRayExit(down, Vector2.Down * vertical), 4, false, Progress(booked, .2));
                    if (i == copies / 2)
                    {
                        Cue(across + Vector2.Right * side * 13, AttackTelegraph.Symbol.Speed, count: batch % 2 == 0 ? 3 : 1, direction: Vector2.Right * side);
                        Cue(down + Vector2.Down * vertical * 13, AttackTelegraph.Symbol.Speed, count: batch % 2 == 0 ? 1 : 3, direction: Vector2.Down * vertical);
                    }
                }
                if (At(launch) || At(launch + .34))
                {
                    var horizontal = Send(across, Vector2.Right * side * 70, batch * 2);
                    var perpendicular = Send(down, Vector2.Down * vertical * 24, batch * 2 + 1);
                    horizontal.Shot.UseBossIllustration(ShotVisual(1), fitRadius: 4);
                    perpendicular.Shot.UseBossIllustration(ShotVisual(0), fitRadius: 4);
                    horizontal.Shot.CancelLeadIn();
                    perpendicular.Shot.CancelLeadIn();
                }
            }
        }
        foreach (var letter in _letters.Where(l => l.Alive))
        {
            bool switched = Time >= _warning + letter.Group / 2 * interval + 1.6;
            bool fastBeforeSwap = letter.Group % 2 == letter.Group / 2 % 2;
            letter.Shot.Velocity = letter.Direction * (fastBeforeSwap != switched ? 78 : 27);
        }
        EndAfter(_warning + (batches - 1) * interval + .35);

    }

    private static double PriorityExitTime(float distance, bool fastFirst)
    {
        float first = fastFirst ? 70 : 24, second = fastFirst ? 24 : 70;
        return distance <= first * 1.6 ? distance / first : 1.6 + (distance - first * 1.6) / second;
    }

    private void LunaticExchangedPriority()
    {
        bool reserve = _priorityBatches.Count == 0;
        if (_priorityBatches.Count is > 0 and < 3)
        {
            var previous = _priorityBatches[^1];
            double firstExit = Math.Min(PriorityExitTime(Field.Width + 24, previous.AcrossFirst),
                PriorityExitTime(Field.Height + 24, !previous.AcrossFirst));
            reserve = Time >= previous.FiresAt + firstExit + .3 - _warning;
        }
        if (reserve)
        {
            bool acrossFirst = _priorityBatches.Count % 2 == 0;
            double fire = Time + _warning;
            double duration = Math.Max(PriorityExitTime(Field.Width + 24, acrossFirst),
                PriorityExitTime(Field.Height + 24, !acrossFirst)) + 5 * .32;
            Vector2 target = Player.GlobalPosition;
            _priorityBatches.Add(new(Time, fire, fire + duration,
                target.X < Field.CenterX ? 1 : -1, target.Y < Field.CenterY ? 1 : -1,
                target.Y < Field.CenterY ? 0 : 1, target.X < Field.CenterX ? 0 : 1, acrossFirst));
        }
        foreach (var batch in _priorityBatches.Where(batch => Time < batch.EndsAt))
        for (int copy = 0; copy < 6; copy++)
        {
            double age = Time - batch.FiresAt - copy * .32;
            float across = (float)(Math.Clamp(age, 0, 1.6) * (batch.AcrossFirst ? 70 : 24)
                + Math.Max(0, age - 1.6) * (batch.AcrossFirst ? 24 : 70));
            float down = (float)(Math.Clamp(age, 0, 1.6) * (batch.AcrossFirst ? 24 : 70)
                + Math.Max(0, age - 1.6) * (batch.AcrossFirst ? 70 : 24));
            float x = (batch.XDirection > 0 ? Field.Left - 12 : Field.Right + 12) + batch.XDirection * across;
            float y = (batch.YDirection > 0 ? Field.Top - 12 : Field.Bottom + 12) + batch.YDirection * down;
            float rowY = Field.Top + batch.Row * Field.Height / 2;
            float columnX = Field.Left + batch.Column * Field.Width / 2;
            if (age < 0 && copy == 0)
            {
                Box(new Rect2(Field.Left, rowY, Field.Width, Field.Height / 2), false, Progress(batch.StartsAt, .8));
                Box(new Rect2(columnX, Field.Top, Field.Width / 2, Field.Height), false, Progress(batch.StartsAt, .8));
            }
            else if (age >= 0)
            {
                if (x >= Field.Left - 12 && x <= Field.Right + 12)
                    Box(new Rect2(x - 6, rowY, 12, Field.Height / 2), true);
                if (y >= Field.Top - 12 && y <= Field.Bottom + 12)
                    Box(new Rect2(columnX, y - 6, Field.Width / 2, 12), true);
            }
        }
        if (_priorityBatches.Count == 3 && Time >= _priorityBatches[^1].EndsAt) Finish();
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
            return EasyApprovalLeave(3) - _warning + _carrierRoutes[4].Length / 70;
        }
    }

    private double EasyApprovalArrival(int index)
    {
        double departure = index == 0 ? _warning : EasyApprovalLeave(index - 1);
        return departure + _carrierRoutes[index].Length / 70;
    }

    private int EasyApprovalBursts => (Tier switch { 0 => 3, 1 => 5, 2 => 7, _ => 10 }) + (Evolved ? 2 : 0);
    private double EasyApprovalBurstInterval => Tier switch { 0 => .28, 1 => .24, 2 => .18, _ => .14 };
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
        double time = _warning + Math.Max(0, elapsed);
        for (int index = 0; index < 4; index++)
        {
            double departure = index == 0 ? _warning : EasyApprovalLeave(index - 1);
            double arrival = EasyApprovalArrival(index);
            if (time < arrival)
                return _carrierRoutes[index].At((float)(time - departure) * 70);
            if (time < EasyApprovalLeave(index)) return _carrierRoutes[index].Points[^1];
        }
        return _carrierRoutes[4].At((float)(time - EasyApprovalLeave(3)) * 70);
    }

    private bool CarriedEnvelopeVisible(double elapsed)
        => elapsed >= 0 && new Rect2(CarriedEnvelopePosition(elapsed) - new Vector2(18, 11), new Vector2(36, 22)).Intersects(Field.Rect);

    private IEnumerable<Rect2> EasyEnvelopeWarningBoxes(float progress)
    {
        var path = EasyRainWarningPathPrefix(EasyEnvelopeCurvePoints(), progress);
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 from = path[i - 1], to = path[i];
            yield return new Rect2(new Vector2(Math.Min(from.X, to.X) - 18, Math.Min(from.Y, to.Y) - 11),
                new Vector2(Math.Abs(to.X - from.X) + 36, Math.Abs(to.Y - from.Y) + 22));
        }
    }

    private Vector2[] EasyEnvelopeCurvePoints() => _carrierRoutes.SelectMany((route, index) =>
        index == 0 ? route.Points : route.Points.Skip(1)).ToArray();

    private IEnumerable<(Vector2 Start, Vector2 Exit)> EasyEnvelopeOpeningPaths(int opening)
    {
        Vector2 carrier = EasyEnvelopeTravelPath()[opening + 1];
        Vector2 forward = carrier.DirectionTo(new Vector2(Field.CenterX, Field.CenterY));
        if (Tier == 0 && opening < _stamps.Count && carrier.DistanceSquaredTo(_stamps[opening].Position) > 1)
            forward = carrier.DirectionTo(_stamps[opening].Position);
        int spread = 2 + Tier * 3;
        for (int row = -spread; row <= spread; row++)
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
            Cue(EasyApprovalStampPosition(index) + new Vector2(-18, -27), AttackTelegraph.Symbol.Stamp, live ? 1 : .3f);
        }
        EndAfter(_warning + EasyEnvelopeTravelDuration + .1);
    }

    private float ClockRevolution => Tier == 0 && !Evolved ? 3.45f : 7.5f;
    private double EasyClockDuration => 11.5;
    private const float ClockHubRadius = 18;
    private const double ClockForecastLead = .8;
    private int EasyClockHands => 1 + Tier + (Evolved ? 1 : 0);
    private float EasyClockHalfWidth => 3 + Tier + (Evolved ? 1.2f : 0);
    private float EasyClockWarningAngle(double elapsed) => _angle + (float)Math.Max(0, elapsed) * (Mathf.Tau / ClockRevolution);
    private float EasyClockHandAngle(double elapsed, int hand) => EasyClockWarningAngle(elapsed) + hand * Mathf.Tau / EasyClockHands;

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

    private float EasyClockHandLength(double elapsed, int hand)
    {
        float extension = elapsed < 2 ? 0 : elapsed < 3 ? (float)(elapsed - 2)
            : elapsed < 10.5 ? 1 : Mathf.Clamp((float)(EasyClockDuration - elapsed), 0, 1);
        return Mathf.Lerp(44, EasyClockBoundaryLength(EasyClockHandAngle(elapsed, hand)), extension);
    }

    private Vector2[] EasyClockHandWarningSector(float progress, int hand)
    {
        progress = Mathf.Clamp(progress, 0, 1);
        if (progress == 0) return Array.Empty<Vector2>();
        float reach = Math.Max(Math.Max(_origin.DistanceTo(new(Field.Left, Field.Top)), _origin.DistanceTo(new(Field.Right, Field.Top))),
            Math.Max(_origin.DistanceTo(new(Field.Left, Field.Bottom)), _origin.DistanceTo(new(Field.Right, Field.Bottom)))) + 3;
        int segments = Mathf.CeilToInt(Mathf.Tau * reach / 3);
        double end = EasyClockDuration * progress, sweep = Math.Min(ClockRevolution, end), frontier = end % ClockRevolution;
        var times = new SortedSet<double>(Enumerable.Range(0, segments + 1).Select(i => (double)i * ClockRevolution / segments)
            .Where(elapsed => elapsed <= sweep)) { 0, sweep };
        foreach (double boundary in new[] { 2d, 3d, 10.5d, EasyClockDuration, end })
            if (boundary <= end && boundary % ClockRevolution <= sweep) times.Add(boundary % ClockRevolution);
        foreach (Vector2 corner in new[] { new Vector2(Field.Left, Field.Top), new Vector2(Field.Right, Field.Top),
            new Vector2(Field.Right, Field.Bottom), new Vector2(Field.Left, Field.Bottom) })
        {
            double at = Mathf.PosMod(_origin.DirectionTo(corner).Angle() - EasyClockHandAngle(0, hand), Mathf.Tau) / (Mathf.Tau / ClockRevolution);
            if (at <= sweep) times.Add(at);
        }
        float MaximumLength(double at, bool includeFrontier)
        {
            float length = 0;
            for (double occurrence = at; occurrence <= end + 1e-8; occurrence += ClockRevolution)
                if (includeFrontier || occurrence < end - 1e-8)
                    length = Math.Max(length, EasyClockHandLength(occurrence, hand));
            return length;
        }
        var points = new List<Vector2>();
        foreach (double at in times)
        {
            Vector2 radial = Vector2.FromAngle(EasyClockHandAngle(at, hand));
            float length = MaximumLength(at, true);
            points.Add(_origin + radial * length);
            if (end >= ClockRevolution && Math.Abs(at - frontier) < 1e-8)
            {
                float earlier = MaximumLength(at, false);
                if (earlier > 0 && earlier < length) points.Add(_origin + radial * earlier);
            }
        }
        points.Add(_origin + Vector2.FromAngle(EasyClockHandAngle(sweep, hand)) * 12);
        for (int i = segments; i >= 0; i--)
        {
            double at = i * ClockRevolution / segments;
            if (at >= sweep) continue;
            points.Add(_origin + Vector2.FromAngle(EasyClockHandAngle(at, hand)) * 12);
        }
        points.Add(points[0]);
        return points.ToArray();
    }

    private const double ClockShiftStart = 4.8, ClockShiftEnd = 7.2;
    private Vector2 ClockOriginAt(double elapsed)
    {
        Vector2 center = Field.Rect.GetCenter();
        if (Tier >= 2) return center;
        float slide = Mathf.SmoothStep(0, 1, Mathf.Clamp((float)((elapsed - ClockShiftStart) / (ClockShiftEnd - ClockShiftStart)), 0, 1));
        return center - Vector2.FromAngle(EasyClockHandAngle(ClockShiftEnd + .55, 0)) * 30 * slide;
    }

    private IEnumerable<(Vector2 A, Vector2 B)> ClockHandStrokes(double elapsed, int hand)
    {
        Vector2 direction = Vector2.FromAngle(EasyClockHandAngle(elapsed, hand));
        Vector2 hub = ClockOriginAt(elapsed);
        yield return (hub + direction * 12, hub + direction * EasyClockHandLength(elapsed, hand));
    }

    private void ExtendingClock()
    {
        double elapsed = Time - _warning;
        _origin = ClockOriginAt(elapsed);

        if (Tier >= 2 && elapsed < EasyClockDuration)
            Disc(_origin, ClockHubRadius, elapsed >= 0, Progress(0, _warning));
        for (int hand = 0; hand < EasyClockHands; hand++)
        {
            if (elapsed < 0)
            {
                float progress = EasyClockWarningProgress();
                if (progress > 0)
                {
                    Vector2 radial = Vector2.FromAngle(EasyClockHandAngle(0, hand)), start = _origin + radial * 12;
                    Line(start, start.Lerp(_origin + radial * 44, progress), EasyClockHalfWidth, false, progress);
                }
            }
            else if (elapsed < EasyClockDuration)
                foreach (var stroke in ClockHandStrokes(elapsed, hand)) Line(stroke.A, stroke.B, EasyClockHalfWidth, true);
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
        EndAfter(_warning + (Tier < 2 ? 9.6 : ClipCycle * 3));
    }

    private void CoincidentMembranes()
    {
        double elapsed = Time - _deadlineFire;
        if (elapsed >= DeadlineDuration)
        {
            if (Tier < 2 || ++_deadlineRound >= Tier + 1) { Finish(); return; }
            PrepareDeadline();
            elapsed = Time - _deadlineFire;
        }
        if (elapsed < 0)
        {
            Box(DeadlineSweep, false);
            if (Tier >= 2) Cue(new Vector2(_origin.X, 20), AttackTelegraph.Symbol.Pips, count: _deadlineRound + 1);
        }
        for (int sign = -1; sign <= 1; sign += 2)
        for (int fold = 0; fold < (Tier >= 2 ? Tier + 1 : 1); fold++)
        {
            float x = _origin.X + (DeadlineOffset(elapsed) + fold * 12) * sign;
            Line(new(x, Field.Top), new(x, Field.Bottom), DeadlineLineRadius, elapsed >= 0);
        }
    }

    private const double DeadlineDuration = 4.3;
    private float DeadlineLineRadius => 2.5f + Tier * .35f;
    private float DeadlineOffset(double elapsed) => Math.Max(20 - Field.Width * .5f,
        58 - (float)Math.Max(0, elapsed) * (30 + Tier * 4));
    private Rect2 DeadlineSweep
    {
        get
        {
            float reach = Math.Max(DeadlineOffset(0) + (Tier >= 2 ? Tier * 12 : 0), Math.Abs(DeadlineOffset(DeadlineDuration))) + DeadlineLineRadius;
            return new Rect2(_origin.X - reach, Field.Top, reach * 2, Field.Height).Intersection(Field.Rect);
        }
    }

    private void PrepareDeadline()
    {
        _origin.X = Mathf.Clamp(Player.GlobalPosition.X, Field.Left + 70, Field.Right - 70);
        Rect2 band = DeadlineSweep;
        float margin = Player.HitRadius + 4;
        float escape = new[] { band.Position.X - margin, band.End.X + margin }
            .Where(x => x >= Field.Left + Player.HitRadius && x <= Field.Right - Player.HitRadius)
            .Min(x => Math.Abs(x - Player.GlobalPosition.X));
        _deadlineStart = Time;
        _deadlineFire = Time + WarnFor(escape + 12, 2);
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
        if (elapsed >= 0 && elapsed < 3.4) Cue(_origin + new Vector2(-24, -18), AttackTelegraph.Symbol.Undo);
        EndAfter(_warning + 6.4);
    }

    private bool _returnCallBooked;
    private Vector2 _returnCallRecipient;
    private Vector2 _crossCallRecipient;
    private double _crossCallAt;
    private static float ExtensionRow(Vector2 recipient, int row) =>
        Field.Top + Mathf.PosMod(recipient.Y - Field.Top + (row - 1) * Field.Height / 3, Field.Height);

    private void PausedOutbox()
    {
        if (Tier == 3) { LunaticPausedOutbox(); return; }
        if (Tier >= 2)
        {
            for (int group = 0; group < 3; group++)
            for (int copy = 0; copy < 5; copy++)
            {
                float speed = 20 + group * 25;
                double elapsed = Time - _warning;
                double restart = 4.7 + group * .9;
                float x = Field.Left - 12 + speed * (float)Math.Clamp(elapsed, 0, 1.7)
                    + (float)Math.Max(0, elapsed - restart) * (72 + Tier * 5) - copy * 22;
                if (x > Field.Right + 10) continue;
                float top = Field.Top + group * Field.Height / 3;
                var bundle = new Rect2(x - 6, top, 12, Field.Height / 3);
                if (elapsed < 0 && copy == 0)
                    Line(new(Field.Left, top + bundle.Size.Y * .5f), new(Field.Right, top + bundle.Size.Y * .5f),
                        bundle.Size.Y * .5f, false, Progress(0, .8));
                else if (elapsed >= 0) Box(bundle, true);
                if (elapsed >= 1.7 && elapsed < restart) Cue(new Vector2(x + 10, top + 12), AttackTelegraph.Symbol.Pips, count: (group + 1));
            }
            EndAfter(_warning + 12);
            return;
        }
        double secondBooking = _warning + 1.55;
        if (!_returnCallBooked && Time >= secondBooking)
        {
            _returnCallBooked = true;
            _returnCallRecipient = Player.GlobalPosition;
        }
        if (Tier == 1 && _returnCallBooked && (_crossCallAt == 0 || Time < _crossCallAt + _warning + 3.55))
            Cue(_returnCallRecipient, AttackTelegraph.Symbol.Link);
        if (Tier == 1 && _crossCallAt == 0 && _letters.Any(l => l.Group >= 3 && l.State == 2))
        {
            _crossCallAt = Time;
            _crossCallRecipient = Player.GlobalPosition;
        }
        for (int call = 0; call < (_crossCallAt > 0 ? 3 : _returnCallBooked ? 2 : 1); call++)
        {
            double booking = call == 0 ? 0 : call == 1 ? secondBooking : _crossCallAt;
            double launch = booking + _warning;
            Vector2 recipient = call == 0 ? _aim : _returnCallRecipient;
            Vector2 direction = _aim.X < Field.CenterX ? Vector2.Left : Vector2.Right;
            if (call == 1) direction = -direction;
            if (call == 2) direction = _crossCallRecipient.Y < Field.CenterY ? Vector2.Up : Vector2.Down;
            float sourceX = direction.X > 0 ? Field.Left : Field.Right;
            for (int row = 0; row < 3; row++)
            for (int copy = 0; copy < 3 + Tier; copy++)
            {
                float y = Mathf.Clamp(ExtensionRow(recipient, row) + (copy - 1) * 5, Field.Top, Field.Bottom);
                Vector2 source = new(sourceX, y);
                if (call == 2)
                {
                    float x = Field.Left + Mathf.PosMod(_crossCallRecipient.X - Field.Left + (row - 1) * Field.Width / 3, Field.Width);
                    source = new(Mathf.Clamp(x + (copy - 1) * 5, Field.Left, Field.Right), direction.Y > 0 ? Field.Top : Field.Bottom);
                }
                if (Time < launch) Line(source, EasyRayExit(source, direction), 4, false, Progress(booking, .2));
                if (!At(launch)) continue;
                var letter = Send(source, direction * (30 + row * 6), call * 3 + row, 2);
                letter.CopyIndex = copy;
                letter.Shot.UseBossIllustration(ShotVisual(2), fitRadius: 4);
                letter.Shot.CancelLeadIn();
            }
        }
        foreach (var letter in _letters.Where(l => l.Alive))
        {
            int call = letter.Group / 3, row = letter.Group % 3;
            double hold = letter.Born + 1.5, restart = hold + (Tier == 0 ? 2.3 : 1.55) + row * .55;
            if (Time < hold) continue;
            if (letter.State == 0)
            {
                letter.State = 1;
                letter.TurnPoint = letter.Shot.GlobalPosition;
                Vector2 recipient = call == 0 ? _aim : _returnCallRecipient;
                float targetY = row == 1 ? Player.GlobalPosition.Y :
                    ExtensionRow(recipient, 2 - row) + letter.Origin.Y - ExtensionRow(recipient, row);
                Vector2 socket = new(letter.Direction.X > 0 ? Field.Right : Field.Left, Mathf.Clamp(targetY, Field.Top, Field.Bottom));
                if (call == 1 && row != 1)
                {
                    float jackX = Field.Left + Mathf.PosMod(recipient.X - Field.Left
                        + (row - 1) * Field.Width / 3 + letter.Origin.Y - ExtensionRow(recipient, row), Field.Width);
                    socket = new(jackX, row == 0 ? Field.Bottom : Field.Top);
                }
                else if (call == 2) socket = _returnCallRecipient;
                letter.Direction = letter.TurnPoint.DirectionTo(socket);
                if (call == 2) letter.Direction = letter.Direction.Rotated((letter.CopyIndex - 1.5f) * .09f);
                letter.Shot.Velocity = Vector2.Zero;
            }
            if (letter.State != 1) continue;
            if (Time < restart)
            {
                Line(letter.TurnPoint, EasyRayExit(letter.TurnPoint, letter.Direction), 4, false, Progress(hold, .15));
                Cue(letter.TurnPoint + Vector2.Up * 10, AttackTelegraph.Symbol.Pips, count: row + 1);
                continue;
            }
            letter.State = 2;
            letter.Shot.Velocity = letter.Direction * (72 + Tier * 5);
        }
        if (Tier == 0 || _crossCallAt > 0)
            EndAfter((Tier == 0 ? secondBooking : _crossCallAt) + _warning + 6);

    }

    private void LunaticPausedOutbox()
    {
        if (_outboxBatches.Count == 0 || _outboxBatches.Count < 3
            && Time >= _outboxBatches[^1].EndsAt - _warning + .5)
        {
            double duration = Enumerable.Range(0, 3).Max(group =>
                4.7 + group * .9 + (Field.Width + 24 + 5 * 22 - (20 + group * 25) * 1.7) / 87);
            _outboxBatches.Add(new(Time, Time + _warning, Time + _warning + duration,
                Player.GlobalPosition.X < Field.CenterX ? 1 : -1,
                Math.Clamp((int)((Player.GlobalPosition.Y - Field.Top) / (Field.Height / 3)), 0, 2)));
        }
        foreach (var batch in _outboxBatches.Where(batch => Time < batch.EndsAt))
        for (int group = 0; group < 3; group++)
        for (int copy = 0; copy < 6; copy++)
        {
            float speed = 20 + group * 25;
            double age = Time - batch.FiresAt;
            double restart = 4.7 + group * .9;
            float distance = speed * (float)Math.Clamp(age, 0, 1.7) + (float)Math.Max(0, age - restart) * 87 - copy * 22;
            float x = (batch.Direction > 0 ? Field.Left - 12 : Field.Right + 12) + batch.Direction * distance;
            float top = Field.Top + (group + batch.FirstRow) % 3 * Field.Height / 3;
            if (age < 0 && copy == 0)
            {
                Vector2 from = new(batch.Direction > 0 ? Field.Left : Field.Right, top + Field.Height / 6);
                Vector2 to = new(batch.Direction > 0 ? Field.Right : Field.Left, from.Y);
                Line(from, to, Field.Height / 6, false, Progress(batch.StartsAt, .8));
            }
            else if (age >= 0 && x >= Field.Left - 12 && x <= Field.Right + 12)
            {
                Box(new Rect2(x - 6, top, 12, Field.Height / 3), true);
            }
        }
        if (_outboxBatches.Count == 3 && Time >= _outboxBatches[^1].EndsAt) Finish();
    }

    internal double OutboxReleaseAt => _warning;
    internal Vector2 OutboxPausePoint(int group) => new(Field.Left - 12 + (20 + group * 25) * 1.7f,
        Field.Top + (group + .5f) * Field.Height / 3);

    private void SolidUmbrellas()
    {
        if (Tier >= 2) { MovingUmbrellaRain(); return; }
        double elapsed = Time - _warning;
        for (int i = 0; i < 2; i++)
        {
            float tilt = elapsed < 3 ? 0 : Mathf.Clamp((float)(elapsed - 3) / 1.5f, 0, 1) * (i == 0 ? .55f : -.55f);
            Vector2 span = Vector2.FromAngle(tilt) * 34;
            _points[i + 2] = _points[i] - span;
            _points[i + 4] = _points[i] + span;
        }
        if (elapsed < 0)
            foreach (float edge in new[] { Field.Left + 7, Field.Right - 7 })
                Line(new(edge, Field.Top + 6), new(edge, Field.Bottom), 4, false, Progress(0, .2));
        if (elapsed >= 0 && elapsed < 6 && Time >= _next)
        {
            _next = Time + .43 - Tier * .045;
            int count = 10 + Tier * 3;
            for (int i = 0; i < count; i++)
            {
                float index = i > 0 && i < count - 1 ? i + (_beat % 2) * .5f : i;
                float x = Field.Left + 7 + index * (Field.Width - 14) / (count - 1);
                Send(new(x, Field.Top + 6), Vector2.Down * (62 + Tier * 5));
            }
            _beat++;
        }
        foreach (var letter in _letters.Where(l => l.Alive && !l.Shot.LeadIn))
            for (int i = 0; i < 2; i++)
                if (Math.Min(Geometry2D.GetClosestPointToSegment(letter.Shot.GlobalPosition, _points[i + 2], _points[i] + new Vector2(0, -12)).DistanceTo(letter.Shot.GlobalPosition),
                    Geometry2D.GetClosestPointToSegment(letter.Shot.GlobalPosition, _points[i] + new Vector2(0, -12), _points[i + 4]).DistanceTo(letter.Shot.GlobalPosition)) < letter.Shot.CollisionRadius + 4)
                { Pool.Despawn(letter.Shot); break; }
        EndAfter(_warning + 9.3);
    }

    internal IEnumerable<Vector2> VisibleUmbrellaShelters => _points.Take(2)
        .Where((point, index) => Tier != 3 || index != _umbrellaFold || Time - _warning - _umbrellaFoldStarted >= 7.6)
        .Select(point => point + Vector2.Down * 38);

    private float UmbrellaCanopyScale(int index, double elapsed)
    {
        if (Tier != 3 || index != _umbrellaFold) return 1;
        double age = elapsed - _umbrellaFoldStarted;
        if (age < 1.8) return 1;
        if (age < 2.2) return 1 - (float)((age - 1.8) / .4);
        return Mathf.Clamp((float)((age - 7) / .6), 0, 1);
    }

    private void MovingUmbrellaRain()
    {
        double elapsed = Time - _warning;
        double rainDuration = Tier == 3 ? 22 : 12;
        if (elapsed >= rainDuration + Field.Height / 80 + .1) { Finish(); return; }
        float sway = 44 * Mathf.Sin((float)Math.Clamp(elapsed - 2, 0, rainDuration - 2) * Mathf.Tau / 10);
        float tilt = .22f * Mathf.Cos((float)Math.Clamp(elapsed - 2, 0, rainDuration - 2) * Mathf.Tau / 10);
        if (Tier == 3 && _umbrellaTransfers < 2 && elapsed >= 3.4 + _umbrellaTransfers * 10)
        {
            _umbrellaFold = Enumerable.Range(0, 2).OrderBy(index =>
                (_points[index] + Vector2.Down * 38).DistanceSquaredTo(Player.GlobalPosition)).First();
            _umbrellaFoldStarted = elapsed;
            _umbrellaTransfers++;
        }
        for (int i = 0; i < 2; i++)
        {
            _points[i] = new Vector2(Field.Left + Field.Width * (i == 0 ? .28f : .72f) + (i == 0 ? sway : -sway), i == 0 ? 104 : 134);
            Vector2 span = Vector2.FromAngle(i == 0 ? tilt : -tilt) * 30 * UmbrellaCanopyScale(i, elapsed);
            _points[i + 2] = _points[i] - span;
            _points[i + 4] = _points[i] + span;
        }
        float front = elapsed < 0 ? Field.Bottom : Math.Min(Field.Bottom, (float)elapsed * 80);
        float tail = Math.Max(Field.Top, (float)(elapsed - rainDuration) * 80);
        for (float x = Field.Left; x < Field.Right; x += 2)
        {
            float bottom = front;
            for (int i = 0; i < 2; i++)
            {
                Vector2 a = _points[i + 2], b = _points[i] + new Vector2(0, -12), c = _points[i + 4];
                if (c.X - a.X < .01f) continue;
                float sample = x + 1;
                if (sample < a.X || sample > c.X) continue;
                bottom = Math.Min(bottom, sample <= b.X ? Mathf.Lerp(a.Y, b.Y, (sample - a.X) / (b.X - a.X))
                    : Mathf.Lerp(b.Y, c.Y, (sample - b.X) / (c.X - b.X)));
            }
            if (bottom > tail) Box(new Rect2(x, tail, Math.Min(2, Field.Right - x), bottom - tail), elapsed >= 0, Progress(0, .8));
        }
    }

    internal readonly record struct AddressLock(int Envelope, Vector2 Origin, Vector2 Recipient, double LockedAt, double FiresAt, bool Approached);
    internal List<AddressLock> AddressLocks { get; } = new();
    internal int AddressShotsFired { get; private set; }

    private void ProximityAddressing()
    {
        int count = 4 + Tier;
        double deadline = _warning + (Tier switch { 0 => 9.5, 1 => 8.0, 2 => 8.5, _ => 7.5 });
        if (Time < _warning)
            for (int i = 0; i < count; i++) Disc(_points[i], 7, false, Progress(0, _warning), cueOnly: true);
        if (At(_warning))
            for (int i = 0; i < count; i++) Send(_points[i], Vector2.Zero, i);
        double notice = Tier == 0 ? Math.Max(2.25, WarnFor(30, 1.3)) : WarnFor(30, 1.3);
        int pulses = Tier switch { 0 => 2, 3 => 4, _ => 3 };
        foreach (var letter in _letters.Where(l => l.Group < count).ToArray())
        {
            if (letter.State < 2 && (!letter.Alive || letter.Shot.LeadIn)) continue;
            if (letter.State == 0)
            {
                Cue(letter.Origin + new Vector2(-18, -13), AttackTelegraph.Symbol.Envelope);
                bool near = Player.GlobalPosition.DistanceSquaredTo(letter.Origin) < 58 * 58;
                if (!near && Time < deadline + letter.Group * .32) continue;
                if (near) ProximityAddresses++;
                else AddressDeadlineMissed = true;
                letter.Direction = letter.Origin.DirectionTo(Player.GlobalPosition);
                if (letter.Direction == Vector2.Zero) letter.Direction = letter.Origin.DirectionTo(_origin);
                letter.State = 1;
                letter.Changed = Time + notice;
                int spread = Tier switch { 2 => 3, 3 => 4, _ => 0 };
                letter.ReturnRays = Enumerable.Range(-spread, 2 * spread + 1).Select(lane =>
                    new FanRay(letter.Origin + (Tier == 2 ? letter.Direction.Orthogonal() * (Math.Sign(lane) * 5) : Vector2.Zero),
                        letter.Direction.Rotated(lane * .17f))).ToArray();
                AddressLocks.Add(new(letter.Group, letter.Origin, Player.GlobalPosition, Time, letter.Changed, near));
            }
            var primary = letter.ReturnRays.MinBy(ray => ray.Direction.DistanceSquaredTo(letter.Direction));
            if (letter.State == 1)
            {
                foreach (var ray in letter.ReturnRays)
                    Line(ray.From, EasyRayExit(ray.From, ray.Direction), 4, false, Progress(letter.Changed - notice, .2));
                if (Time < letter.Changed) continue;
                letter.Shot.GlobalPosition = primary.From;
                LaunchAddress(letter, primary.Direction);
            }
            for (int pulse = 0; pulse < pulses; pulse++)
            {
                if (!At(letter.Changed + pulse * .21)) continue;
                foreach (var ray in letter.ReturnRays)
                {
                    if (pulse == 0 && ray.Equals(primary)) continue;
                    var reply = Send(ray.From, ray.Direction * 16, count + letter.Group);
                    LaunchAddress(reply, ray.Direction);
                }
            }
        }
        if (Time < _warning || AddressLocks.Count < count
            || AddressLocks.Any(item => Time < item.FiresAt + (pulses - 1) * .21)) return;
        EndAfter(0);
    }

    private void LaunchAddress(Letter letter, Vector2 heading)
    {
        letter.State = 2;
        letter.Shot.UseBossIllustration(WarningVisual, fitRadius: 4);
        letter.Shot.Velocity = heading * 16;
        letter.Shot.MakeAccel(16, 90 + Tier * 8, .22f);
        letter.Shot.CancelLeadIn();
        AddressShotsFired++;
    }

    private Vector2 _forwardSample, _forwardVelocity;
    private double _forwardSampleAt;
    private float _forwardTurnRate;

    private (Vector2 Inlet, Vector2 Receiver, Vector2 Sender) ForwardStations(int delivery)
    {
        bool reverse = delivery / 2 % 2 != 0;
        Vector2 receiver = _points[reverse ? 1 : 0], sender = _points[reverse ? 0 : 1];
        return (new Vector2(reverse ? Field.Right : Field.Left, receiver.Y), receiver, sender);
    }

    private void ReadRecipientMotion()
    {
        if (_forwardSampleAt == 0) { _forwardSample = Player.GlobalPosition; _forwardSampleAt = Time; return; }
        double dt = Time - _forwardSampleAt;
        if (dt < .18) return;
        Vector2 velocity = (Player.GlobalPosition - _forwardSample) / (float)dt;
        _forwardTurnRate = velocity.Length() < 8 || _forwardVelocity.Length() < 8 ? 0 :
            Mathf.Clamp(_forwardVelocity.AngleTo(velocity) / (float)dt, -1.35f, 1.35f);
        _forwardVelocity = velocity;
        _forwardSampleAt = Time;
        _forwardSample = Player.GlobalPosition;
    }

    private Vector2 LedRecipient(Vector2 sender, float speed, double notice)
    {
        Vector2 recipient = Player.GlobalPosition, predicted = recipient;
        for (int estimate = 0; estimate < 3; estimate++)
        {
            float seconds = Math.Min(5.5f, (float)notice + sender.DistanceTo(predicted) / speed);
            float angle = _forwardTurnRate * seconds;
            Vector2 travel = Math.Abs(angle) < .025f ? _forwardVelocity * seconds
                : (_forwardVelocity * Mathf.Sin(angle) + _forwardVelocity.Rotated(Mathf.Pi / 2) * (1 - Mathf.Cos(angle))) / _forwardTurnRate;
            predicted = (recipient + travel).Clamp(Field.Rect.Position + Vector2.One * 5, Field.Rect.End - Vector2.One * 5);
        }
        return predicted;
    }

    private void InboxTransfer()
    {
        ReadRecipientMotion();
        int count = Tier >= 2 ? 8 + Tier * 3 : Tier == 0 ? 7 : 6;
        double spacing = Tier >= 2 ? .31 : Tier == 0 ? .63 : .53;
        double notice = Tier == 0 ? 2.25 : WarnFor(32, 1.35);
        float speed = 63 + Tier * 7;
        double last = _warning + (count - 1) * spacing;
        for (int i = 0; i < count; i++)
        {
            var route = ForwardStations(i);
            double sendsAt = _warning + i * spacing;
            if (Time < sendsAt) Line(route.Inlet, route.Receiver, CameoPaperRadius, false);
            if (At(sendsAt)) SendCameoPaper(route.Inlet, route.Inlet.DirectionTo(route.Receiver) * 68, i, 0);
        }
        foreach (var delivery in _letters.Where(l => l.Group < count).ToArray())
        {
            var route = ForwardStations(delivery.Group);
            if (delivery.State == 0 && delivery.Alive && !delivery.Shot.LeadIn
                && (delivery.Shot.GlobalPosition - route.Receiver).Dot(delivery.Direction) >= 0)
            {
                Pool.Despawn(delivery.Shot);
                delivery.State = 1;
                delivery.Changed = Time + notice;
                Vector2 recipient = delivery.Group % 2 == 0 ? Player.GlobalPosition : LedRecipient(route.Sender, speed, notice);
                delivery.Direction = route.Sender.DirectionTo(recipient);
                if (delivery.Direction == Vector2.Zero) delivery.Direction = route.Sender.DirectionTo(route.Receiver);
                int spread = Tier >= 2 ? Tier + 1 : delivery.Group % 2 == 1 ? 2 : 0;
                delivery.ReturnRays = Enumerable.Range(-spread, spread * 2 + 1)
                    .Select(copy => new FanRay(route.Sender, delivery.Direction.Rotated(copy * .16f))).ToArray();
            }
            if (delivery.State != 1) continue;
            foreach (var ray in delivery.ReturnRays)
            {
                if (Time < delivery.Changed)
                    Line(ray.From, EasyRayExit(ray.From, ray.Direction), CameoPaperRadius, false, Progress(delivery.Changed - notice, .2));
                else
                {
                    var forwarded = SendCameoPaper(ray.From, ray.Direction * speed, count + delivery.Group, 1);
                    forwarded.State = 2;
                }
            }
            if (Time >= delivery.Changed) delivery.State = 2;
        }
        if (Time > last && !_letters.Any(l => l.Group < count && l.State != 2)) Finish();
    }

    private Letter SendCameoPaper(Vector2 at, Vector2 velocity, int group, int pattern)
    {
        var letter = Send(at, velocity, group, pattern);
        letter.Shot.UseBossIllustration(ShotVisual(pattern), fitRadius: CameoPaperRadius);
        letter.Shot.CancelLeadIn();
        return letter;
    }

    private int _returnBreathLanes = -1;
    private double _returnBreathAt;
    private Vector2 _firstBreathEye;

    private void PrepareBreathLanes(bool returning = false)
    {
        Vector2 at = Player.GlobalPosition;
        float playerRow = returning ? at.X : at.Y;
        float low = returning ? Field.Left : Field.Top, high = returning ? Field.Right : Field.Bottom;
        float center = (low + high) / 2;
        float eyeRow = Mathf.Clamp(playerRow + (playerRow < center ? 58 : -58), low + 43, high - 43);
        _origin = returning ? new Vector2(eyeRow, Field.CenterY) : new Vector2(Field.CenterX, eyeRow);
        if (!returning) _firstBreathEye = _origin;
        Vector2 across = returning ? (at.Y < Field.CenterY ? Vector2.Up : Vector2.Down)
            : (at.X < Field.CenterX ? Vector2.Left : Vector2.Right);
        var rows = new List<float> { playerRow, eyeRow - 20, eyeRow, eyeRow + 20 };
        foreach (float row in new[] { low + 8, high - 8 })
            if (rows.All(y => Math.Abs(y - row) > 12)) rows.Add(row);
        foreach (float row in rows)
        {
            var path = new Curve2D();
            Vector2 start = returning ? new Vector2(row, across.Y > 0 ? Field.Top : Field.Bottom)
                : new Vector2(across.X > 0 ? Field.Left : Field.Right, row);
            Vector2 position = start, direction = across;
            path.AddPoint(start);
            float offset = row - eyeRow;
            bool curved = Math.Abs(offset) < 43;
            if (curved)
            {
                Vector2 tangent = returning ? Vector2.Right : Vector2.Down;
                position = _origin + tangent * offset - across * Mathf.Sqrt(43 * 43 - offset * offset);
                path.AddPoint(position);
                float turn = (playerRow < eyeRow ? -1 : 1) * (returning ? -across.Y : across.X) * .09f;
                do
                {
                    direction = direction.Rotated(turn);
                    position += direction * 5;
                    path.AddPoint(position);
                } while (position.DistanceSquaredTo(_origin) < 43 * 43);
            }
            path.AddPoint(EasyRayExit(position, direction));
            _breathLanes.Add((path, curved));
        }
    }

    private void BreathVortex()
    {
        double spacing = Tier >= 2 ? .23 : .56 - Tier * .055;
        int bursts = Tier >= 2 ? 5 + Tier * 2 : 4 + Tier;
        double nextIntake = _warning + (bursts - 1) * spacing + .18;
        if (_returnBreathLanes < 0 && Time >= nextIntake)
        {
            _returnBreathLanes = _breathLanes.Count;
            _returnBreathAt = Time + WarnFor(52, Tier == 0 ? 2.3 : 1.4);
            PrepareBreathLanes(true);
        }
        for (int lane = 0; lane < _breathLanes.Count; lane++)
        {
            var (path, curved) = _breathLanes[lane];
            bool returning = _returnBreathLanes >= 0 && lane >= _returnBreathLanes;
            double launch = returning ? _returnBreathAt : _warning;
            if (Time < launch)
                for (int i = 1; i < path.PointCount; i++)
                    Line(path.GetPointPosition(i - 1), path.GetPointPosition(i), CameoPaperRadius, false);
            for (int burst = 0; burst < bursts; burst++)
            {
                if (!At(launch + burst * spacing)) continue;
                var letter = SendCameoPaper(path.GetPointPosition(0), Vector2.Zero, lane, curved ? 1 : 0);
                letter.Shot.BossPathDriven = true;
            }
        }
        foreach (var letter in _letters.Where(l => l.Alive))
        {
            var path = _breathLanes[letter.Group].Path;
            float distance = (float)(Time - letter.Born) * (58 + Tier * 5);
            if (distance >= path.GetBakedLength()) { Pool.Despawn(letter.Shot); continue; }
            var pose = path.SampleBakedWithRotation(distance);
            letter.Shot.GlobalPosition = pose.Origin;
            letter.Shot.Rotation = pose.Rotation;
        }
        if (_returnBreathLanes >= 0 && Time > _returnBreathAt + (bursts - 1) * spacing) Finish();
    }

    protected override void DrawTechnique()
    {
        if (Tier >= 2 && _mechanic is 10 or 16)
        {
            foreach (var hazard in HazardShapes)
            {
                var rect = new Rect2(hazard.A, hazard.B - hazard.A);
                if (!hazard.Live)
                {
                    if (_mechanic == 10) AttackTelegraph.Box(this, rect, AttackTelegraph.Area);
                    continue;
                }
                DrawRect(new Rect2(rect.Position + Vector2.One * 1.2f, rect.Size), new Color("101829", .65f));
                DrawRect(rect, new Color("e6dcd5", .94f));
                DrawRect(rect, new Color("6c5355"), false, .7f, true);
                bool vertical = rect.Size.Y > rect.Size.X;
                float length = vertical ? rect.Size.Y : rect.Size.X;
                for (float offset = 6; offset < length; offset += 12)
                {
                    Vector2 at = rect.Position + (vertical ? new Vector2(3, offset) : new Vector2(offset, 3));
                    Vector2 across = vertical ? Vector2.Right : Vector2.Down;
                    DrawLine(at, at + across * 6, new Color("927b79", .7f), .6f, true);
                    DrawLine(at + across.Orthogonal() * 2, at + across.Orthogonal() * 2 + across * 4, new Color("927b79", .4f), .5f, true);
                }
            }
        }
        if (Tier == 2 && _mechanic == 10)
        {
            for (int round = 0; round < 2; round++)
            {
                double age = Time - _warning - round * 6.6;
                if (age < -2.8 && round > 0 || age > 4) continue;
                DrawPriorityExchange(round, true, age);
            }
        }
        if (Tier == 2 && _mechanic == 16)
        {
            for (int group = 0; group < 3; group++)
            {
                Vector2 pause = OutboxPausePoint(group);
                if (Time < _warning)
                {
                    float top = Field.Top + group * Field.Height / 3;
                    DrawRect(new Rect2(Field.Left, top, Field.Width, Field.Height / 3), new Color("e4ae9c", .045f));
                    AttackTelegraph.Route(this, new Vector2(Field.Left, pause.Y), new Vector2(Field.Right, pause.Y),
                        Field.Height / 6, true, true, true, true);
                }
                if (Time < _warning + 4.7 + group * .9)
                {
                    DrawLine(pause + new Vector2(-3, -5), pause + new Vector2(-3, 5), Ink, 1.2f, true);
                    DrawLine(pause + new Vector2(3, -5), pause + new Vector2(3, 5), Ink, 1.2f, true);
                    OfficeNumber(pause + new Vector2(13, 0), group + 1, Paper);
                }
            }
        }
        if (Tier >= 2 && _mechanic == 17)
        {
            foreach (var hazard in HazardShapes)
            {
                var rect = new Rect2(hazard.A, hazard.B - hazard.A);
                DrawRect(rect, new Color("b98a7c", hazard.Live ? .13f : .045f));
                if ((int)(rect.Position.X - Field.Left) % 8 != 0) continue;
                float offset = (float)((rect.Position.X * 1.731 + (hazard.Live ? Time * 80 : 0)) % 23);
                for (float y = rect.Position.Y + offset; y < rect.End.Y; y += 23)
                    DrawLine(new Vector2(rect.Position.X + 1, y), new Vector2(rect.Position.X + 1, Math.Min(y + 6, rect.End.Y)),
                        new Color("e7c7b6", hazard.Live ? .6f : .24f), .5f, true);
            }
        }
        if (Tier == 3 && _mechanic is 10 or 16) DrawLunaticOfficeForecasts();
        if (UsesEasyReadabilityRules)
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
                for (int side = 0; side < 2; side++)
                {
                    bool open = EasyInboxOpen(side);
                    Envelope(new Vector2(side == 0 ? Field.Left + 16 : Field.Right - 16, Field.CenterY), new Vector2(10, 6),
                        open ? 1 : 0, open ? paper : new Color(Ink, .35f));
                }
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
                if (Tier >= 2)
                {
                    for (int bundle = 0; bundle < 3; bundle++)
                    {
                        double lockAt = _warning + bundle * 2.2;
                        if (Time >= lockAt + 1.6) continue;
                        float phase = (float)Math.Min(Time, lockAt) * 1.6f + bundle * Mathf.Tau / 3;
                        Vector2 knot = Field.Rect.GetCenter() + new Vector2(Mathf.Sin(phase) * 90, Mathf.Cos(phase) * 58);
                        DrawRect(new Rect2(knot - new Vector2(10, 6), new Vector2(20, 12)), ink, false, .8f);
                        DrawPolyline(new[] { knot + new Vector2(-10, -6), knot, knot + new Vector2(10, -6) }, paper, .8f, true);
                    }
                    break;
                }
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
                DrawClockHub();
                break;
            case 14:
                double deadlineAge = Time - _deadlineFire;
                Rect2 band = DeadlineSweep;
                Color hatch = new(WarningVisual.Accent, .45f);
                if (deadlineAge < 0)
                {
                    for (float y = Field.Top - band.Size.X; y < Field.Bottom; y += 14)
                    {
                        float start = Math.Max(0, Field.Top - y), end = Math.Min(band.Size.X, Field.Bottom - y);
                        if (start < end)
                            DrawDashedLine(new(band.Position.X + start, y + start), new(band.Position.X + end, y + end),
                                hatch, .65f, 3, true);
                    }
                }
                else
                {
                    if (band.Position.X > Field.Left) DrawDashedLine(band.Position, new(band.Position.X, Field.Bottom), hatch, .6f, 4, true);
                    if (band.End.X < Field.Right) DrawDashedLine(new(band.End.X, Field.Top), band.End, hatch, .6f, 4, true);
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 tab = new(_origin.X + DeadlineOffset(deadlineAge) * side, side < 0 ? 11 : Field.Bottom - 11);
                    DrawRect(new Rect2(tab - new Vector2(4, 5), new Vector2(8, 10)), ink, false, .8f);
                    if (deadlineAge < 0) OfficeArrow(tab + new Vector2(side * 6, 0), Vector2.Left * side, 7, paper);
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
                bool held = Tier == 3 ? _outboxBatches.Any(batch => Time >= batch.FiresAt + 1.7 && Time < batch.FiresAt + 6.5)
                    : Tier < 2 ? _letters.Any(letter => letter.Alive && letter.State == 1) : elapsed >= 1.7 && elapsed < 5.7;
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
                    else if (Tier >= 2)
                        DrawArc(letter.Origin, 58, 0, Mathf.Tau, 48, new Color(ink, .2f), .6f, true);
                }
                break;
        }
        if (_mechanic == 10)
            foreach (var group in _letters.Where(l => l.Alive && !l.Shot.LeadIn).GroupBy(l => l.Group))
            {
                var letter = group.First();
                Vector2 origin = letter.Shot.GlobalPosition;
                Vector2 ray = letter.Shot.Velocity.Normalized();
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
                if (a.DistanceSquaredTo(b) > .01f)
                    DrawColoredPolygon(new[] { a, _points[i] + new Vector2(0, -12), b }, new Color(i == 0 ? "8d6856" : "45566c", .8f));
                AttackTelegraph.Stroke(this, new[] { a, _points[i] + new Vector2(0, -12), b, a }, color);
                DrawLine(_points[i], _points[i] + new Vector2(0, 23), color, 1, true);
                DrawLine(_points[i] + new Vector2(0, -12), a.Lerp(b, .25f), new Color(color, .4f), .7f, true);
                DrawLine(_points[i] + new Vector2(0, -12), a.Lerp(b, .75f), new Color(color, .4f), .7f, true);
                DrawArc(_points[i] + new Vector2(4, 23), 4, 0, Mathf.Pi, 10, color, 1, true);
                if (Tier == 3 && i == _umbrellaFold && elapsed - _umbrellaFoldStarted < 2.2)
                {
                    AttackTelegraph.DrawSymbol(this, _points[i] + Vector2.Up * 24, AttackTelegraph.Symbol.Pause, color);
                    float remaining = 1 - Mathf.Clamp((float)((elapsed - _umbrellaFoldStarted) / 1.8), 0, 1);
                    DrawArc(_points[i] + Vector2.Up * 12, 9, -Mathf.Pi / 2, -Mathf.Pi / 2 + remaining * Mathf.Tau, 24,
                        new Color(color, .7f), 1, true);
                }
            }
        if (_mechanic == 19 && GameManager.ShowAttackTelegraphs)
        {
            foreach (var letter in _letters.Where(letter => letter.State == 1))
                foreach (var ray in letter.ReturnRays)
                    AttackTelegraph.Route(this, ray.From, EasyRayExit(ray.From, ray.Direction), CameoPaperRadius,
                        true, true, true, true, ShotVisual(1));
            foreach (Vector2 box in _points.Take(2)) DrawEasyWarningArc(box, 7, 0, Mathf.Tau, EasyExpiryBlue, true);
            for (int i = 0; i < 2; i++)
            {
                DrawRect(new Rect2(_points[i] - new Vector2(12, 9), new Vector2(24, 18)), i == 0 ? Paper : Ink, false, 1.4f);
                DrawPolyline(new[] { _points[i] + new Vector2(-10, -2), _points[i] + new Vector2(-5, 3), _points[i] + new Vector2(5, 3), _points[i] + new Vector2(10, -2) }, ink, .8f, true);
                int waiting = _letters.Count(l => l.State == 1 && ForwardStations(l.Group).Sender == _points[i]);
                OfficeArrow(_points[i] + new Vector2(0, waiting > 0 ? -8 : -17), waiting > 0 ? Vector2.Up : Vector2.Down, 7, paper);
                if (waiting > 0) OfficeNumber(_points[i] + new Vector2(17, 3), waiting, paper);
            }
        }
        if (_mechanic == 20)
        {
            if (GameManager.ShowAttackTelegraphs)
                for (int lane = 0; lane < _breathLanes.Count; lane++)
                {
                    bool returning = _returnBreathLanes >= 0 && lane >= _returnBreathLanes;
                    if (Time >= (returning ? _returnBreathAt : _warning)) continue;
                    var (path, curved) = _breathLanes[lane];
                    for (int i = 1; i < path.PointCount; i++)
                        AttackTelegraph.Route(this, path.GetPointPosition(i - 1), path.GetPointPosition(i), CameoPaperRadius,
                            i == 1, true, i == 1, i == path.PointCount - 1, ShotVisual(curved ? 1 : 0));
                }
            if (_returnBreathLanes >= 0 && _letters.Any(l => l.Alive && l.Group < _returnBreathLanes))
                DrawArc(_firstBreathEye, 43, 0, Mathf.Tau, 48, new Color(Ink, .3f), .65f, true);
            DrawArc(_origin, 43, 0, Mathf.Tau, 48, new Color(Ink, .6f), 1, true);
            for (int i = 0; i < 3; i++)
            {
                float angle = (float)Time * 1.4f + i * Mathf.Tau / 3;
                DrawArc(_origin, 16 + i * 8, angle, angle + 1.5f, 14, new Color(Paper, .45f), .8f, true);
            }
        }
    }

    private void Envelope(Vector2 at, Vector2 half, float open, Color color)
    {
        if (!UsesCustomHazardArt)
        {
            DrawRect(new Rect2(at - half, half * 2), new Color("29232e", .8f));
            DrawRect(new Rect2(at - half, half * 2), AttackTelegraph.Outline, false, 1.8f);
        }
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

    private void DrawPriorityExchange(int round, bool acrossFirst, double age)
    {
        DrawPrioritySpeedCue(new Vector2(round == 0 ? Field.Left - 12 : Field.Right + 12,
            Field.Top + (round + .5f) * Field.Height / 2), round == 0 ? Vector2.Right : Vector2.Left, acrossFirst, age);
        DrawPrioritySpeedCue(new Vector2(Field.Left + (round == 0 ? .75f : .25f) * Field.Width,
            round == 0 ? Field.Top - 12 : Field.Bottom + 12), round == 0 ? Vector2.Down : Vector2.Up, !acrossFirst, age);
    }

    private void DrawPrioritySpeedCue(Vector2 from, Vector2 direction, bool fastFirst, double age)
    {
        if (age >= 0 || !GameManager.ShowAttackTelegraphs) return;
        Vector2 turn = from + direction * ((fastFirst ? 70 : 24) * 1.6f);
        Vector2 side = direction.Orthogonal();
        DrawDashedLine(turn - side * 9, turn + side * 9, new Color(Paper, .7f), .8f, 2, true);
        foreach (int segment in new[] { -1, 1 })
        {
            bool fast = (segment < 0) == fastFirst;
            Vector2 at = turn + direction * segment * 15;
            AttackTelegraph.DrawSymbol(this, at, AttackTelegraph.Symbol.Speed, Paper,
                amount: fast ? 1 : .2f, count: fast ? 3 : 1, direction: direction);
        }
        float firstSpeed = fastFirst ? 70 : 24, lastSpeed = fastFirst ? 24 : 70;
        float travel = Mathf.PosMod((float)Time, 24 / firstSpeed + 24 / lastSpeed);
        float distance = travel < 24 / firstSpeed
            ? -24 + travel * firstSpeed : (travel - 24 / firstSpeed) * lastSpeed;
        Vector2 marker = turn + direction * distance;
        DrawDashedLine(marker - side * 4, marker + side * 4, new Color(Paper, .45f), .65f, 2, true);
    }

    private void OfficeNumber(Vector2 at, int number, Color color)
    {
        AttackTelegraph.Pips(this, at, number, color);
    }
}
