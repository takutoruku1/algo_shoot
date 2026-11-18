using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class KoharuBossTechnique : BossCharacterTechnique
{
    internal override bool UsesArenaBody => Kind == BossTechniqueKind.Extra && Variant == 0 && Evolved
        || Kind == BossTechniqueKind.Normal && Variant == 1 || BodyPursuit;
    private bool BodyPursuit => Kind == BossTechniqueKind.Crossfire && Variant == 0;
    internal Vector2 BossCenter => Boss.GlobalPosition;
    internal float PursuitBodyRadius => Boss is CameoBoss ? 19 : 23;

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
        internal float DropRadius;
        internal Vector2[] DropPath = Array.Empty<Vector2>();
    }

    private readonly List<Light> _lights = new();
    private readonly List<SupportLight> _supportLights = new();
    private readonly List<(Vector2 Position, double Time)> _tail = new();
    private readonly List<Vector2> _prayerTargets = new();
    private Vector2 _origin, _anchor, _point, _destination, _lastPlayer;
    private double _warning, _charge, _firedAt = -1;
    private double _antlerFire;
    private int _antlerRound;
    private float _angle, _radius;
    private float _supportTilt;
    private float _supportSag;
    private Node2D? _supportWarnings;
    private bool SupportTechnique => Kind == BossTechniqueKind.Crossfire && Variant == 1;
    private static BulletArt.PlayerVisual SupportDropVisual => BulletArt.EnemyVisual("res://assets/fx/koharu_prayer_tag.svg", "e5a9f2");
    private float _pursuitExitSpeed;
    internal sealed class PursuitBarrier
    {
        internal Rect2 Bounds;
        internal bool Solid;
    }
    private readonly List<PursuitBarrier> _pursuitBarriers = new();
    private Node2D? _pursuitScenery;
    internal IEnumerable<PursuitBarrier> VisiblePursuitBarriers => Finished ? Array.Empty<PursuitBarrier>() : _pursuitBarriers;
    private float _supportPressure;
    private double _supportFlashAt = -1;
    private Vector2 _spillFrom, _spillVelocity;
    private float _spillGravity;
    private Light? _spilledLight;
    private Vector2[] _spillPath = Array.Empty<Vector2>();
    private double SpillLead => Tier == 0 ? 2.2 : 1.5;
    private Vector2 SpillPoint(float elapsed) => _spillFrom + _spillVelocity * elapsed + Vector2.Down * (_spillGravity * elapsed * elapsed);
    private int _phase;
    private static Vector2 Center => new(Field.CenterX, Field.CenterY);
    private bool HardPressure => Tier == 2 && Boss is BossKoharu;
    private int Density => new[] { 1, 2, 3, 4 }[Tier];
    private float Speed => new[] { 0.78f, 1f, 1.12f, 1.22f }[Tier];
    private double ReadingSeconds => HardPressure ? 1.65 : Tier switch { 2 => 2.2, 3 => 1.8, _ => 1.6 };
    private int ReadingRows => Tier >= 2 ? 6 : 4;
    private float ReadingRowY(int row)
    {
        float pitch = Field.Height / ReadingRows;
        float offset = Tier >= 2 ? pitch * .5f : Mathf.PosMod(_anchor.Y - Field.Top, pitch);
        return Mathf.Clamp(Field.Top + row * pitch + offset, Field.Top + 3, Field.Bottom - 3);
    }
    private float ReadingWidth => Tier >= 2 ? Field.Height / ReadingRows * .5f + .5f : 5 + Density;
    internal readonly record struct ReadingLine(Vector2 From, Vector2 To, double LockedAt, double FiresAt, double EndsAt);
    private readonly List<ReadingLine> _readingLines = new();
    private Vector2 _readingAim;
    internal IReadOnlyList<ReadingLine> ReadingLines => _readingLines;
    internal ReadingLine? VisibleReadingLine => _readingLines.Count > 0 && Time < _readingLines[^1].EndsAt ? _readingLines[^1] : null;
    private int ReadingPasses => Math.Max(4, Tier + 2) * (HardPressure ? 2 : 1);
    private const double ReadingTrack = .7;
    internal readonly record struct UnpairedVolley(Vector2 Origin, Vector2 Direction, Vector2 Exit, double StartsAt, double FiresAt);
    private readonly List<UnpairedVolley> _unpairedVolleys = new();
    private int _unpairedAnswerSeat = -1;
    internal readonly record struct ClapLane(int Row, float Junction, double StartsAt, double FiresAt);
    private readonly List<ClapLane> _clapLanes = new();
    private readonly List<(float Height, float Junction)> _clapMeetings = new();
    private int NearClapBeats => Tier == 0 ? 6 : 3 + Density;
    private float HandSpeed => Tier == 0 ? 133 : 171;
    private float ClapTravel(float junction) => Math.Max(junction - Field.Left - 2, Field.Right - 2 - junction) / HandSpeed;
    internal IReadOnlyList<ClapLane> VisibleClapLanes => _clapLanes;
    private int ClapBeats => HardPressure ? 12 : 6;
    private double ClapSpacing => HardPressure ? 1.4 : 2.8;
    private sealed class ScreenFlash(float angle, double startsAt)
    {
        internal readonly float Angle = angle;
        internal readonly double StartsAt = startsAt;
        internal float Offset, TurnRate;
    }
    private readonly List<ScreenFlash> _screenFlashes = new();
    private readonly List<(float Angle, float Spread, float Near, bool Live)> _screenShapes = new();
    private const float ScreenTurnLimit = 1.16f;
    private int _loadedSupportSide;
    private double _coilStartsAt;
    private int _coilRound;
    internal IReadOnlyList<UnpairedVolley> UnpairedVolleys => _unpairedVolleys;
    private float UnpairedRadius => BulletPool.BossHitShape(Job.Heal, Evolved).Radius;
    private double UnpairedLead => WarnFor(UnpairedRadius * 2 + Player.HitRadius * 2 + 8, 1.1);
    private double UnpairedCycle => .45 + UnpairedLead + Math.Max(1.05, (UnpairedShots - 1) * UnpairedShotSpacing + .15);
    private const double UnpairedShotSpacing = .20;
    internal int UnpairedShots => (2 + Density) * (HardPressure ? 2 : 1);
    private float OpenCoilRadius(float age)
    {
        Vector2 corner = new(_point.X < Center.X ? Field.Right : Field.Left, _point.Y < Center.Y ? Field.Bottom : Field.Top);
        return Mathf.Lerp(_point.DistanceTo(corner) + 8, 17, Mathf.Clamp(age / 5.5f, 0, 1));
    }
    private float SupportScale => Math.Min(1, (Field.Width * .5f - 12) / (Tier >= 2 ? 174 : 118));
    private float SupportHalf => 118 * SupportScale;
    private double SupportDropWarning => Tier == 0 ? 2.2 : 1.2;
    private double SupportInterval => Tier >= 2 ? .28 : Tier == 0 ? 1.4 : 1.3;
    private int SupportPairs => Tier >= 2 ? 10 + Tier * 4 : 5 + Tier * 2;
    private float SupportSpawnAlong(int pair, int side) => side * (Tier >= 2 ? 20 + pair % 6 * 18 : 28 + pair % 4 * 19) * SupportScale;
    private double SupportLaunchLead(int pair) => pair == 0 ? _warning : Tier == 0 ? 2.2 : 1.1;
    private Vector2 SupportCenter
    {
        get
        {
            float t = (float)Math.Max(0, Time - _warning);
            float sag = (float)Math.Min(1, t * .4f) * Mathf.Sin(t * .66f);
            float y = Mathf.Clamp(Center.Y + (_anchor.Y - Center.Y) * Mathf.Cos(t * .47f) + sag * (Tier >= 2 ? 66 : 61)
                + (Tier < 2 ? 7 : 0), Field.Top + 8, Field.Bottom - 6);
            if (t < 2)
                y = Mathf.Lerp(Math.Clamp(_anchor.Y + (Tier < 2 ? 7 : 0), Field.Top + 8, Field.Bottom - 6), y,
                    Mathf.SmoothStep(0, 1, Math.Max(0, t - 1)));
            y = Mathf.Clamp(y + _supportSag * Mathf.SmoothStep(0, 1, t - 1.3f), Field.Top + 8, Field.Bottom - 6);
            return new(Center.X + (Tier >= 2 ? 56 : 36) * SupportScale * Mathf.Sin(t * (HardPressure ? .9f : .55f)), y);
        }
    }
    internal Vector2 VisibleSupportCenter => SupportCenter;
    private Vector2 SupportPoint(float along) => SupportCenter + new Vector2(along, Tier >= 2 ? 0 : -7).Rotated(_supportTilt);

    protected override void Start()
    {
        _origin = Boss.TechniqueCenter;
        _anchor = _lastPlayer = Player.GlobalPosition;
        if (StartEasyKoharu()) return;
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
                if (Tier >= 2) _warning = WarnFor(130, 3.6);
                if (Tier == 3) Identify("拍手の往復", "koharu.clap-annihilation", "前の拍手を抜けた高さへ、次の両手が向く。合流して消える位置を見て渡り直す");
                break;
            case BossTechniqueKind.Area:
                if (Variant == 0)
                {
                    Identify("画面の光", "koharu.screen-aperture", "広がる光の角度から外へ出る");
                    _angle = _origin.DirectionTo(_anchor).Angle();
                    float escape = float.MaxValue;
                    var facing = Vector2.FromAngle(_angle);
                    _radius = Math.Max(2, Math.Min(((BossKoharu)Boss).BodyExtent + 22, _origin.DistanceTo(_anchor) - 5));
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
                        if (Boss.BodyContactDamaging && _origin.DistanceTo(Geometry2D.GetClosestPointToSegment(_origin, _anchor, candidate))
                            < bodyClearance) continue;
                        escape = Math.Min(escape, _anchor.DistanceTo(candidate));
                    }
                    _warning = WarnFor(escape + 5, 1.5);
                    if (Tier >= 2)
                    {
                        _origin = new Vector2(Field.Right + 12, Field.CenterY);
                        _angle = _origin.DirectionTo(_anchor).Angle();
                        _radius = 0;
                        _warning = ScreenLead;
                        Identify("画面の光", "koharu.screen-aperture", "画面がこちらを向いて止まったら、開く光の縁を横切る。点灯のたびに向きを読む");
                        if (HardPressure) Identify("画面の光", "koharu.screen-aperture", "残った光を避けながら、次に画面が向いた先を読む。点灯の縁を渡り直す");
                    }
                }
                else if (Variant == 1)
                    Identify("視線のかたまり", "koharu.motion-observation", "見つめられている間は止まり、視線が固まったら離れる");
                else
                {
                    Identify("既読の線", "koharu.reading-head", Tier >= 2
                        ? "改行する目が今いる行を見つめる。目が止まったらその行を外し、通り過ぎた頭の後ろへ回る"
                        : "線全体ではなく、走っている光の先端を避ける");
                    _readingAim = new Vector2(Field.Left - 32, ReadingRowY(Math.Clamp((int)((_anchor.Y - Field.Top) * ReadingRows / Field.Height), 0, ReadingRows - 1)));
                }
                break;
            case BossTechniqueKind.Signature:
                Identify("枝角・結び目の檻", "koharu.hinged-antlers", "閉じる枝角の先から離れ、外側へ回る");
                _point = _anchor;
                _warning = WarnFor(76, 1.8);
                _angle = _anchor.DirectionTo(Center).Angle() - Mathf.Pi / 2;
                _antlerFire = _warning;
                if (Tier >= 2)
                {
                    _point = _anchor;
                    _warning = WarnFor(135, 3.8);
                    _antlerFire = _warning;
                    Identify("枝角・結び目の檻", "koharu.hinged-antlers", "結び直すたびに枝先の開く向きを読み、閉じる檻の外へ回り直す");
                }
                break;
            case BossTechniqueKind.Finale:
                Identify("自分なにしてんだろ", "koharu.cross-reorientation", "十字の間へ離れ、向きが変わる前にもう一歩ずれる");
                _warning = WarnFor(32, 1.35);
                break;
            case BossTechniqueKind.Crossfire:
                switch (Variant)
                {
                    case 0:
                        Identify("どこにいても見つける", "koharu.pursuit-train", "こはる本人を引きつけ、曲がり切れない光の尾の内側へ回る");
                        _warning = WarnFor(84, 2.2);
                        if (Tier >= 2)
                        {
                            Identify("どこにいても見つける", "koharu.pursuit-train", "こはる本人が仕切りをすり抜けて追ってくる。仕切りの端と光の尾を見て、先回りして回り込む");
                            _warning = WarnFor(84, 2.2);
                            _pursuitBarriers.Add(new() { Bounds = new Rect2(Center + new Vector2(-75, -74), new Vector2(18, 64)) });
                            _pursuitBarriers.Add(new() { Bounds = new Rect2(Center + new Vector2(57, 10), new Vector2(18, 64)) });
                            _pursuitBarriers.Add(new() { Bounds = new Rect2(Center + new Vector2(-26, -9), new Vector2(52, 18)) });
                            _pursuitScenery = new Node2D { ZIndex = -2, ZAsRelative = false, UseParentMaterial = true };
                            _pursuitScenery.Draw += DrawPursuitBarriers;
                            AddChild(_pursuitScenery);
                        }
                        break;
                    case 1:
                        Identify("届いて、全部", "koharu.unbalanced-support", "寄る側へ支えが傾く。転がる光の勢いと、端から落ちる先を見る");
                        _supportWarnings = new Node2D { ZIndex = -1, ZAsRelative = false, UseParentMaterial = true };
                        _supportWarnings.Draw += DrawSupportWarnings;
                        AddChild(_supportWarnings);
                        _warning = WarnFor(112, 2.4);
                        if (Tier >= 2)
                        {
                            Identify("届いて、全部", "koharu.unbalanced-support", "動く支点から離れると光が片側へ溜まる。支点を追って傾きを戻し、転がる光をかわす");
                            _warning = WarnFor(240, 5.2);
                        }
                        break;
                    case 2:
                        Identify("すれ違う祈り", "koharu.parabolic-exchange", Tier >= 2
                            ? "光の列ごとに交点が変わる。前の列を見送りながら、新しい弧の外側へ回る"
                            : "上下を交換する弧の外側に回る");
                        if (Tier >= 2) _warning = WarnFor(90, 2.8);
                        break;
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
                Identify("締めつける声援", "koharu.inward-coil", Tier >= 2
                    ? "声援の内側で切れ目に合わせ、外の光が消えたら輪を抜ける。最後に中心が膨らむ"
                    : "輪の外へ先に出るか、内側で待って光が中心に集まったところで離れる");
                _point = new Vector2(Mathf.Clamp(_anchor.X, Field.Left + 60, Field.Right - 60),
                    Mathf.Clamp(_anchor.Y, Field.Top + 60, Field.Bottom - 60));
                _warning = GameManager.Instance!.HasDodge ? WarnFor(50, 1.5) : WarnFor(160, 2.2);
                if (Tier >= 2)
                {
                    _angle = (_point - _origin).LengthSquared() < 1 ? Mathf.Pi / 2 : _origin.DirectionTo(_point).Angle();
                    _warning = WarnFor(160, 3.5);
                }
                if (Tier == 3) Identify("締めつける声援", "koharu.inward-coil", "切れ目から抜けると、その場所で声援が結び直される。新しい開き口へ向き直る");
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
                    Identify("見ていないと、消える", "koharu.attention-charge", Tier >= 2
                        ? "広がる視線で目が開く。狙いが止まった光を置いて、その外へ離れる"
                        : "近くで膨らむ光から距離を取り、注目の圧力を鎮める");
                break;
            case BossTechniqueKind.Cameo:
                if (Variant == 0)
                    Identify("ペンライトのドミノ", "koharu.domino-fall", "上から順に倒れるペンライトの後ろへ。最後の光が床に届いたら、下からの折り返しに備える");
                else
                    Identify("客席に、きみを探す", "koharu.searching-glance", Tier < 2
                        ? "同じ道を通ると、次の客席の目が行く先を見る。視線が止まったら、明るい列を外す"
                        : "探している目を引きつけ、目が開いて狙いが止まったら光の列を横に外す");
                break;
        }
    }

    protected override void Step(double delta)
    {
        if (_easyMode != 0) { StepEasyKoharu(); return; }
        switch (Kind)
        {
            case BossTechniqueKind.Normal:
                if (Variant == 0) BraidedLights();
                else if (Variant == 1) TurningPrayer();
                else FlowerCrown();
                break;
            case BossTechniqueKind.Edge: Clapping(); break;
            case BossTechniqueKind.Area:
                if (Variant == 0) ScreenLight(delta);
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
                else UnpairedLight(delta);
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
                else Searching(delta);
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

    protected override BulletArt.PlayerVisual WarningVisual => Kind switch
    {
        BossTechniqueKind.Area when Variant == 2 => BulletArt.EnemyVisual("res://assets/fx/koharu_heart_charm.svg", "ff91ad"),
        BossTechniqueKind.Signature => BulletArt.EnemyVisual("koharu_star_pin", "d0b2ff"),
        BossTechniqueKind.Extra when Evolved && Variant == 1 => BulletArt.EnemyVisual("koharu_star_pin", "d0b2ff"),
        BossTechniqueKind.Normal => EasyKoharuVisual(Variant),
        BossTechniqueKind.Edge => BulletArt.EnemyVisual("res://assets/fx/koharu_clap.svg", "ffb48c"),
        BossTechniqueKind.Cameo when Variant == 0 => BulletArt.EnemyVisual("res://char/v3/fx/koharu/penlight_lit.png", "cf9bff"),
        BossTechniqueKind.Cameo => BulletArt.EnemyVisual("res://char/v3/fx/koharu/eye_cross.png", "ed9ace"),
        BossTechniqueKind.Crossfire when Variant == 0 => BulletArt.EnemyVisual("res://assets/fx/koharu_ribbon.svg", "a8e5ac"),
        BossTechniqueKind.Crossfire when Variant == 1 => BulletArt.EnemyVisual("koharu_badge", "ffe48a"),
        BossTechniqueKind.Crossfire when Variant == 2 => BulletArt.EnemyVisual("koharu_uchiwa", "e9a0da"),
        BossTechniqueKind.Crossfire when Variant == 4 => BulletArt.EnemyVisual("res://assets/fx/koharu_heart_charm.svg", "ff91ad"),
        BossTechniqueKind.Extra when Evolved && Variant == 0 => EasyKoharuVisual(3),
        _ => BulletArt.EnemyVisual("koharu_acrylic", "ffcb95"),
    };

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

    internal sealed class KoharuEasyPath
    {
        internal string Key = "";
        internal int Wave, Strand, Lane, Pulse, ForecastPulse, Pattern;
        internal float RimInset;
        internal bool ShowsPath => Pulse == ForecastPulse && Strand == Lane;
        internal float HelixPhase, HelixPitch;
        internal double StartsAt, FiresAt, WarningUntil, Duration;
        internal double MirrorArrival, MirrorReturn;
        internal float HalfWidth;
        internal Vector2[] Points = Array.Empty<Vector2>(), Turns = Array.Empty<Vector2>();
        internal Bullet? Shot;
        internal ulong Activation;
        internal bool Fired;
    }

    internal sealed class KoharuEasySweep
    {
        internal string Key = "";
        internal int Wave, Mode, Pair;
        internal double StartsAt, FiresAt, EndsAt, Duration;
        internal float HalfWidth, HalfHeight;
        internal Vector2 EyeA, EyeB;
        internal Vector2[] Points = Array.Empty<Vector2>();
        internal double[] PulseOffsets = Array.Empty<double>();
        internal bool Fired;
    }

    private int _easyMode;
    private bool TwoBeat => Boss is BossKoharu && !Evolved && _easyMode == 1;
    private bool NormalMirror => Boss is BossKoharu && !Evolved && Tier == 1 && _easyMode == 2;
    private const int HelixPairs = 3;
    private readonly record struct TwoBeatRhythm(int LeadShots, int ReturnShots, int TailShots,
        double LeadSpacing, double ReturnSpacing, double Rest, double HandDelay, double PairDelay, double WaveInterval, float RimSpeed);
    private TwoBeatRhythm Rhythm => Tier switch
    {
        0 => new(3, 7, 3, .35, .31, .82, .98, .55, 6.6, 84),
        1 => new(5, 10, 4, .22, .195, .59, .72, .24, 5.65, 108),
        2 => new(7, 11, 5, .17, .14, .48, .56, .19, 5.05, 132),
        _ => new(9, 12, 6, .128, .105, .38, .42, .16, 4.65, 156),
    };
    private const float OpeningHelixPitch = 106f, OpeningHelixRadius = 48f;
    private const double HelixLeadSpacing = .115, HelixReturnSpacing = .105;
    private static readonly Color HelixRose = new("ff94c6"), HelixMint = new("76eef1");
    private const int RibbonTailPattern = 4;
    private static readonly Color RibbonCoral = new("ffbb87"), RibbonBlue = new("99d7ff");
    private static Color HelixColor(KoharuEasyPath path)
        => path.Pattern == RibbonTailPattern ? (path.Strand % 2 == 0 ? RibbonCoral : RibbonBlue)
            : (path.Strand % 2 == 0 ? HelixRose : HelixMint);
    private Curve2D? _twoBeatCurve;
    private readonly HashSet<int> _easyBloomRead = new();
    private readonly List<KoharuEasyPath> _easyPaths = new();
    private readonly List<KoharuEasySweep> _easySweeps = new();
    private readonly List<double> _easyStarts = new();
    private readonly List<bool> _easyPrepared = new();
    private readonly List<Vector2> _easyTargets = new();
    internal readonly record struct GazeStep(int From, int To, double StartsAt, double OpensAt, double ClosesAt);
    private readonly List<GazeStep> _gazeSteps = new();
    private Node2D? _gazeSurface;
    private ShaderMaterial? _gazeMaterial;
    private int _gazeHome;
    private double _gazeBegin;
    internal bool SequentialGaze => _easyMode == 4 && Tier >= 2;
    private int GazeColumns => HardPressure ? 8 : 6;
    private int GazeRows => HardPressure ? 6 : 4;
    internal Rect2 GazeCell(int index) => new(Field.Left + index % GazeColumns * Field.Width / GazeColumns,
        Field.Top + index / GazeColumns * Field.Height / GazeRows, Field.Width / GazeColumns, Field.Height / GazeRows);
    internal Vector2 GazeEntry => GazeCell(_gazeHome).GetCenter();
    internal IEnumerable<Rect2> VisibleGazeIntersections => _easySweeps
        .Where(s => s.Mode == 4 && _easyPrepared[s.Wave] && EasyKoharuProgress(s.StartsAt) >= 1 && Time < s.EndsAt)
        .Select(s => new Rect2(new Vector2(s.EyeB.X - s.HalfWidth, s.EyeA.Y - s.HalfHeight),
            new Vector2(s.HalfWidth, s.HalfHeight) * 2));
    internal GazeStep? VisibleGazeStep => _gazeSteps.FirstOrDefault(step => Time < step.ClosesAt) is var step
        && step.ClosesAt > 0 && Time >= step.StartsAt ? step : null;
    private static readonly Dictionary<int, BulletArt.PlayerVisual> EasyKoharuVisuals = new();
    private static Texture2D? _lightArt;
    private bool ScreenTechnique => Kind == BossTechniqueKind.Area && Variant == 0;
    private bool FilledScreen => Tier >= 2 && ScreenTechnique;
    private bool DominoLights => Kind == BossTechniqueKind.Cameo && Variant == 0;
    protected override bool UsesDangerForecast => Tier == 2 || ScreenTechnique || DominoLights || BodyPursuit || SupportTechnique;
    protected override void DrawDangerForecast(Node2D canvas)
    {
        if (ScreenTechnique && !FilledScreen) return;
        base.DrawDangerForecast(canvas);
        if (!DominoLights) return;
        for (int wave = 0; wave < 2; wave++)
        for (int column = 0; column < DominoColumns; column++)
        {
            bool returning = wave == 1;
            double fire = (returning ? DominoReturn : _warning) + column * DominoSpacing;
            if (Time < fire - _warning || Time >= fire) continue;
            var hinge = DominoHinge(returning ? DominoColumns - 1 - column : column, returning);
            canvas.DrawColoredPolygon(DominoSector(hinge, returning), Colors.White);
        }
    }
    protected override bool UsesCustomHazardArt => _easyMode != 0 || ScreenTechnique || DominoLights || BodyPursuit || SupportTechnique;
    protected override bool DirectionalWarnings => Kind is BossTechniqueKind.Edge or BossTechniqueKind.Cameo
        || Kind == BossTechniqueKind.Area && Variant == 2 || Kind == BossTechniqueKind.Crossfire && Variant == 2;
    protected override Color AttackLineInk => new("a186bc");
    protected override Texture2D? AttackLineMotif => _lightArt;
    private int EasyLightPulses
    {
        get
        {
            int count = (Tier switch { 0 => 3, 1 => 5, 2 => 7, _ => 10 }) + (Evolved ? 2 : 0);
            return HardPressure ? (count * 4 + 2) / 3 : count;
        }
    }
    private float EasyLightSpeed => (Tier switch { 0 => 1, 1 => 1.35f, 2 => 1.75f, _ => 2.2f }) + (Evolved ? .15f : 0);
    private int EasyStrands
    {
        get
        {
            int count = _easyMode switch {
                1 => Tier == 0 ? 2 : 4 + Tier * 2, 2 => Tier == 0 ? 5 : 3 + Tier * 3, 3 => 7 + Tier * 7, _ => 8 + Tier * 6,
            };
            return HardPressure ? (count * 3 + 1) / 2 : count;
        }
    }
    private int EasyGazePairs => Tier == 0 ? 9 : 5 + Tier * 4;
    private int EasyBlackoutAxes => HardPressure ? 12 : 4 + Tier * 2;
    private double EasyBeatLength => .36 + Tier * .09 + (Evolved ? .12 : 0);
    private int EasyGazePulses => 3 + Tier * 2 + (Evolved ? 1 : 0);
    private double EasyGazeInterval => 1.2 - Tier * .1;
    private double EasyGazeDuration => .34 + Tier * .12 + (Evolved ? .06 : 0);
    private double EasyGazeLead => Tier == 0 ? .42 : (EasyGazeInterval - EasyGazeDuration) * .35;
    private double EasyGazeTail => Tier == 0 ? .66 : EasyGazeDuration + EasyGazeLead;
    private int EasyBlackoutPulses => Tier == 3 ? 1 : 3 + Tier + (Evolved ? 1 : 0);
    private double EasyBlackoutInterval => EasyBlackoutPulses == 1 ? 0 : Math.Max(.4, 1.68 / (EasyBlackoutPulses - 1));
    private double EasyLeapAt => _warning + (Tier >= 2 ? 1.8 : 3.1);
    private float EasyKoharuProgress(double start) => Mathf.Clamp((float)((Time - start - .2) / .7), 0, 1);
    internal override IEnumerable<BossEasyCue> EasyCues
    {
        get
        {
            for (int index = 0; index < _readingLines.Count; index++)
            {
                var line = _readingLines[index];
                yield return new($"reading-line:{index}", line.LockedAt, line.FiresAt, 1, Time >= line.FiresAt);
            }
            for (int index = 0; index < _unpairedVolleys.Count; index++)
            {
                var volley = _unpairedVolleys[index];
                if (Time >= volley.StartsAt)
                    yield return new($"unpaired-volley:{index}", volley.StartsAt, volley.FiresAt, 1, Time >= volley.FiresAt);
            }
            foreach (var weight in _supportLights.Where(weight => weight.DropAt >= 0))
                yield return new($"support-drop:{weight.Light.Index}", weight.DropAt - SupportDropWarning, weight.DropAt, 1, Time >= weight.DropAt);
            if (SupportTechnique)
                for (int pair = 0; pair < SupportPairs; pair++)
                {
                    double release = _warning + pair * SupportInterval;
                    double starts = release - SupportLaunchLead(pair);
                    if (Time >= starts) yield return new($"support-release:{pair}", starts, release, 1, Time >= release);
                }
            foreach (var path in _easyPaths)
                yield return new(path.Key, path.StartsAt, path.FiresAt, EasyKoharuProgress(path.StartsAt), path.Fired);
            foreach (var sweep in _easySweeps)
                yield return new(sweep.Key, sweep.StartsAt, sweep.FiresAt, EasyKoharuProgress(sweep.StartsAt), sweep.Fired);
        }
    }

    protected override IEnumerable<(Vector2 Origin, double StartsAt, double FiresAt)> TelegraphTimings
        => _easyPaths.Where(path => path.ShowsPath && !path.Fired && _easyPrepared[path.Wave])
            .Select(path => (path.Points[0], path.StartsAt, path.FiresAt))
            .Concat(_easySweeps.Where(sweep => !sweep.Fired && _easyPrepared[sweep.Wave])
                .Select(sweep => (sweep.Mode == 4 ? sweep.EyeA : sweep.Points[0], sweep.StartsAt, sweep.FiresAt)));

    private bool StartEasyKoharu()
    {
        _easyMode = Kind switch
        {
            BossTechniqueKind.Normal when Variant == 0 => 1,
            BossTechniqueKind.Normal when Variant == 1 => 2,
            BossTechniqueKind.Normal => 3,
            BossTechniqueKind.Area when Variant == 1 => 4,
            BossTechniqueKind.Extra when Evolved && Variant == 0 => 5,
            BossTechniqueKind.Finale => 6,
            _ => 0,
        };
        if (_easyMode == 0) return false;
        if (_easyMode == 1) _twoBeatCurve = GD.Load<Curve2D>("res://assets/attack_curves/koharu_two_beats.tres");
        _lightArt ??= GD.Load<Texture2D>("res://char/v3/fx/koharu/gaze_line.png");
        (string Name, string Key, string Answer) identity = _easyMode switch
        {
            1 => ("約束の二拍", "koharu.braided-rest", "交わる二列の尾を読み、二拍の後の隙間で渡る"),
            2 => ("鏡越しに振り向く祈り", "koharu.mirrored-glance", "鏡側へ進んだ同じ光が振り向く。確定した祈りの列を横に外す"),
            3 => ("画面に咲く花冠", "koharu.screen-bloom", "回転しながら開く花弁の間を、外へ咲く向きに抜ける"),
            4 => ("重なる視線の焦点", "koharu.gaze-focus", "片眼の視線は無害。横と縦の二つが重なる場所だけを外す"),
            5 => ("枝角の跳躍", "koharu.leap-bloom", "跳躍の足跡と着地を外し、着地からほどける光の間へ移る"),
            _ => ("自分なにしてんだろ", "koharu.blackout-reorientation", "灯る十字の間へ移り、消灯の間に次の向きを読む"),
        };
        Identify(identity.Name, identity.Key, identity.Answer);
        if (SequentialGaze)
        {
            Identify(identity.Name, identity.Key, "二つの目が開く区画は危険。隣の片眼が閉じたら移り、元の両眼が開く前に渡り終える");
            PrepareGazeSteps();
            _gazeMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/koharu_gaze_glass.gdshader") };
            _gazeMaterial.SetShaderParameter("field_origin", Field.Rect.Position);
            _gazeMaterial.SetShaderParameter("cell_size", GazeCell(0).Size);
            _gazeSurface = new Node2D { Material = _gazeMaterial, ZIndex = -1 };
            _gazeSurface.Draw += () => {
                foreach (var hazard in HazardShapes.Where(hazard => hazard.Live && hazard.Shape == 2))
                    _gazeSurface.DrawRect(new Rect2(hazard.A, hazard.B - hazard.A), Colors.White);
            };
            AddChild(_gazeSurface);
            return true;
        }
        _warning = _easyMode == 5 ? Tier >= 2 ? 1.7 : 3.2 : 3.1;
        int waves = _easyMode == 3 ? 3 : _easyMode == 5 ? Math.Max(2, Tier + 1) : _easyMode == 6 && Tier == 3 ? 6 : 3;
        if (HardPressure && _easyMode == 6) waves = 4;
        double interval = _easyMode switch { 1 => 4.4, 2 => 4.8, 3 => 6.4, 4 => 4.4, _ => 4.2 };
        interval *= Tier switch { 0 => 1, 1 => .72, 2 => .55, _ => .43 };
        if (TwoBeat) interval = Rhythm.WaveInterval;
        if (_easyMode == 6 && Tier == 3) interval = 2.8;
        if (HardPressure && _easyMode == 6) interval = 1.8;
        for (int wave = 0; wave < waves; wave++)
        {
            double start = _easyMode == 5 ? wave * (EasyLeapAt + 2.7)
                : wave * interval + (Tier >= 2 && wave > 0 && !(_easyMode == 6 && Tier == 3) ? Math.Max(0, _warning + 1.2 - interval) : 0);
            _easyStarts.Add(start);
            _easyPrepared.Add(false);
            _easyTargets.Add(_anchor);
            if (_easyMode <= 3 || _easyMode == 5)
            {
                bool helix = TwoBeat;
                var rhythm = Rhythm;
                int strands = helix ? 2 * HelixPairs : EasyStrands;
                bool singleHelix = helix && wave == 0;
                int firstBeatPulses = helix ? rhythm.LeadShots : EasyLightPulses;
                int secondBeatPulses = helix ? rhythm.ReturnShots : EasyLightPulses;
                double openingLead = HelixLeadSpacing * (1.35f / EasyLightSpeed);
                double openingReturn = HelixReturnSpacing * (1.35f / EasyLightSpeed);
                double leadSpacing = singleHelix ? HelixPairs * openingLead : rhythm.LeadSpacing;
                double returnSpacing = singleHelix ? HelixPairs * openingReturn : rhythm.ReturnSpacing;
                double[] pulses = _easyMode switch
                {
                    1 when helix => Enumerable.Range(0, firstBeatPulses)
                        .Select(pulse => pulse * leadSpacing)
                        .Concat(Enumerable.Range(0, secondBeatPulses).Select(pulse =>
                            (firstBeatPulses - 1) * leadSpacing + (singleHelix ? (HelixPairs - 1) * openingLead : 0)
                            + rhythm.Rest + pulse * returnSpacing)).ToArray(),
                    1 => Enumerable.Range(0, EasyLightPulses).Select(pulse => pulse * EasyBeatLength / (EasyLightPulses - 1))
                        .Concat(Enumerable.Range(0, secondBeatPulses).Select(pulse => EasyBeatLength + .59 + pulse * EasyBeatLength / (secondBeatPulses - 1))).ToArray(),
                    2 => Enumerable.Range(0, EasyLightPulses + 1).Select(pulse => pulse * (.66 + Tier * .2 + (Evolved ? .25 : 0)) / EasyLightPulses).ToArray(),
                    _ => Enumerable.Range(0, EasyLightPulses).Select(pulse => pulse * (.64 + Tier * .16 + (Evolved ? .16 : 0)) / (EasyLightPulses - 1)).ToArray(),
                };
                for (int strand = 0; strand < strands; strand++)
                for (int pulse = 0; pulse < pulses.Length; pulse++)
                {
                    int pattern = _easyMode == 5 ? 3 : _easyMode == 1 ? 0 : _easyMode == 2 ? 1 : 2;
                    double strandStart = start;
                    if (helix && strand % 2 == 1) strandStart += rhythm.HandDelay;
                    if (helix && !singleHelix) strandStart += (strand / 2) * rhythm.PairDelay;
                    int forecastPulse = helix && pulse >= firstBeatPulses ? firstBeatPulses : 0;
                    int ribbonStart = firstBeatPulses + secondBeatPulses - rhythm.TailShots;
                    float rimInset = 0;
                    if (helix && wave == waves - 1 && (strand / 2 == 0 || strand / 2 == HelixPairs - 1) && pulse >= ribbonStart)
                    {
                        pattern = RibbonTailPattern;
                        forecastPulse = Math.Min(pulse, ribbonStart + 2);
                        rimInset = 7 * (ribbonStart + 2 - forecastPulse);
                    }
                    double beadDelay = singleHelix ? (strand / 2) * (forecastPulse == 0 ? openingLead : openingReturn) : 0;
                    double pathStart = _easyMode == 5 ? strandStart + _warning : strandStart;
                    double fire = strandStart + (_easyMode == 5 ? EasyLeapAt + 2.2 : _warning);
                    _easyPaths.Add(new KoharuEasyPath {
                        Key = $"{identity.Key}:{wave}:{strand}:{pulse}", Wave = wave, Strand = strand, Lane = singleHelix ? strand % 2 : strand,
                        Pulse = pulse, ForecastPulse = forecastPulse, Pattern = pattern,
                        RimInset = rimInset,
                        StartsAt = pathStart + pulses[forecastPulse], FiresAt = fire + pulses[pulse] + beadDelay,
                        WarningUntil = fire + pulses[forecastPulse], HalfWidth = EasyKoharuHalfWidth(pattern),
                    });
                }
            }
            if (_easyMode == 4)
                for (int pair = 0; pair < EasyGazePairs; pair++)
                    _easySweeps.Add(new KoharuEasySweep {
                        Key = $"gaze-focus:{wave}:{pair}", Wave = wave, Pair = pair, Mode = 4, StartsAt = start,
                        FiresAt = start + _warning + pair * .38, EndsAt = start + _warning + pair * .38 + (EasyGazePulses - 1) * EasyGazeInterval + EasyGazeTail,
                        Duration = EasyGazeDuration, HalfWidth = 22 + Tier * .65f, HalfHeight = 18 + Tier * 1.5f, PulseOffsets = Enumerable.Range(0, EasyGazePulses).Select(pulse => pulse * EasyGazeInterval).ToArray(),
                    });
            else if (_easyMode == 5)
            {
                _easySweeps.Add(new KoharuEasySweep {
                    Key = $"leap:footprint:{wave}", Wave = wave, Mode = 1, StartsAt = start, FiresAt = start + EasyLeapAt,
                    EndsAt = start + EasyLeapAt + 2, Duration = 2, HalfWidth = ((BossKoharu)Boss).BodyExtent + 3,
                });
                _easySweeps.Add(new KoharuEasySweep {
                    Key = $"leap:landing:{wave}", Wave = wave, Mode = 2, StartsAt = start, FiresAt = start + EasyLeapAt + 2,
                    EndsAt = start + EasyLeapAt + 2.42, Duration = .42, HalfWidth = ((BossKoharu)Boss).BodyExtent + 8,
                });
            }
            else if (_easyMode == 6)
                for (int axis = 0; axis < EasyBlackoutAxes; axis++)
                    _easySweeps.Add(new KoharuEasySweep {
                        Key = $"blackout:{wave}:{axis}", Wave = wave, Mode = 3, StartsAt = start,
                        FiresAt = start + _warning, EndsAt = start + _warning + (EasyBlackoutPulses - 1) * EasyBlackoutInterval + .42, Duration = HardPressure ? .38 : .42 - Tier * .04,
                        HalfWidth = 4.5f + Tier, HalfHeight = axis,
                        PulseOffsets = Enumerable.Range(0, EasyBlackoutPulses).Select(pulse => pulse * EasyBlackoutInterval).ToArray(),
                    });
        }
        PrepareEasyKoharuWave(0, _anchor);
        return true;
    }

    private static BulletArt.PlayerVisual EasyKoharuVisual(int pattern)
    {
        if (EasyKoharuVisuals.TryGetValue(pattern, out var art)) return art;
        if (pattern == RibbonTailPattern)
            return EasyKoharuVisuals[pattern] = BulletArt.EnemyVisual("res://assets/fx/koharu_ribbon.svg", RibbonCoral.ToHtml(false));
        var texture = pattern == 3 ? GD.Load<Texture2D>("res://assets/fx/koharu_petal.svg")
            : BulletArt.Get(pattern == 0 ? "koharu_penlight" : pattern == 1 ? "koharu_acrylic" : "koharu_star_pin")!;
        using var bitmap = texture.GetImage();
        var region = bitmap.GetUsedRect();
        var accent = new Color(pattern == 0 ? "d8b3ff" : pattern == 1 ? "ffcb95" : pattern == 3 ? "ff9ec5" : "ffe69c");
        return EasyKoharuVisuals[pattern] = new BulletArt.PlayerVisual(texture, region, region.GetCenter(), accent);
    }

    private float EasyKoharuHalfWidth(int pattern)
    {
        var art = EasyKoharuVisual(pattern == 3 ? 0 : pattern);
        float radius = Evolved ? BulletPool.BossChargeRadius : BulletPool.BossRadius;
        return BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position, Bullet.BossArtSize(radius, Evolved)).Radius;
    }

    private static Vector2 EasyKoharuExit(Vector2 from, Vector2 direction)
    {
        float x = direction.X > 0 ? (Field.Right + 28 - from.X) / direction.X
            : direction.X < 0 ? (Field.Left - 28 - from.X) / direction.X : float.PositiveInfinity;
        float y = direction.Y > 0 ? (Field.Bottom + 28 - from.Y) / direction.Y
            : direction.Y < 0 ? (Field.Top - 28 - from.Y) / direction.Y : float.PositiveInfinity;
        return from + direction * Math.Min(x, y);
    }

    private static Vector2[] EasyKoharuSamples(Func<float, Vector2> sample, int count = 121)
        => Enumerable.Range(0, count).Select(index => sample((float)index / (count - 1))).ToArray();

    private Vector2[] UnwindFinalRibbon(Vector2[] helix, float phase, float pitch, KoharuEasyPath path,
        out Vector2[] turns, out double duration)
    {
        float firstTurn = (Field.Top + 38) / pitch + phase / Mathf.Tau;
        float crossingY = (Mathf.Ceil(firstTurn * 2) * .5f - phase / Mathf.Tau) * pitch;
        float crossing = Mathf.InverseLerp(helix[0].Y, helix[^1].Y, crossingY);
        Vector2 knot = EasyKoharuPoint(helix, crossing);
        Vector2 tangent = EasyKoharuPoint(helix, crossing - .005f).DirectionTo(knot);
        bool left = path.Strand < 2;
        Vector2 inward = left ? Vector2.Right : Vector2.Left;
        Vector2 alongSide = (path.Strand % 2 == 0) == left ? Vector2.Up : Vector2.Down;
        Vector2 edge = new((left ? Field.Left : Field.Right - Player.HitRadius) + inward.X * path.RimInset, Field.CenterY);
        Vector2 corner = new(edge.X, (alongSide.Y < 0 ? Field.Top : Field.Bottom) - alongSide.Y * path.RimInset);
        Vector2 crossMiddle = new(Field.CenterX + inward.X * 9, corner.Y);
        using var ribbon = new Curve2D { BakeInterval = 1 };
        ribbon.AddPoint(knot, Vector2.Zero, tangent * 14);
        ribbon.AddPoint(edge, -alongSide * 18, alongSide * 20);
        ribbon.AddPoint(corner, -alongSide * 18, inward * 18);
        ribbon.AddPoint(crossMiddle, -inward * 16, inward * 6);
        ribbon.AddPoint(crossMiddle + inward * 15 + alongSide * (26 + path.RimInset), -alongSide * 10);
        float length = ribbon.GetBakedLength();
        double untilKnot = crossing * 4.8 / EasyLightSpeed;
        float speed = Rhythm.RimSpeed;
        double seconds = untilKnot + length / speed;
        duration = seconds * EasyLightSpeed;
        turns = new[] { knot, edge, corner };
        return EasyKoharuSamples(t => {
            double age = t * seconds;
            return age <= untilKnot ? EasyKoharuPoint(helix, age * EasyLightSpeed / 4.8)
                : ribbon.SampleBaked(Math.Min(length, (float)(age - untilKnot) * speed));
        }, 241);
    }

    private void PrepareEasyKoharuWave(int wave, Vector2 target, bool bloomOnly = false)
    {
        _easyPrepared[wave] = true;
        _easyTargets[wave] = target;
        if (_easyMode == 5 && !bloomOnly)
        {
            _origin = Boss.GlobalPosition;
            _destination = Clamp(target, ((BossKoharu)Boss).BodyExtent + 9);
        }
        foreach (var first in _easyPaths.Where(path => path.Wave == wave && path.ShowsPath))
        {
            Vector2[] points;
            Vector2[] turns = Array.Empty<Vector2>();
            double duration;
            if (_easyMode == 1)
            {
                int pairs = TwoBeat ? HelixPairs : EasyStrands / 2;
                float pairFromCenter = first.Strand / 2 - (pairs - 1) * .5f;
                float offset = pairFromCenter * 9;
                float amplitude = Field.Width * .5f - Math.Abs(offset);
                float phase = Mathf.Asin(Mathf.Clamp((target.X - Center.X) / amplitude, -1, 1))
                    - Mathf.Tau * target.Y / Field.Height;
                float pitch = Field.Height, axis = Center.X + offset, lean = 0, axisMargin = 0;
                if (TwoBeat)
                {
                    float column = Field.Width / pairs;
                    int nearest = Mathf.Clamp(Mathf.FloorToInt((target.X - Field.Left) / column), 0, pairs - 1);
                    float anchor = Field.Left + (nearest + .5f) * column;
                    float shift = Mathf.Clamp(target.X - anchor, -16, 16);
                    pitch = wave == 0 ? OpeningHelixPitch : 94;
                    amplitude = wave == 0 ? OpeningHelixRadius : column * .46f;
                    axisMargin = amplitude;
                    axis = wave == 0 ? target.X : Field.Left + (first.Strand / 2 + .5f) * column + shift;
                    axis = Mathf.Clamp(axis, Field.Left + amplitude, Field.Right - amplitude);
                    float aimedAxis = wave == 0 ? axis : Mathf.Clamp(anchor + shift, Field.Left + amplitude, Field.Right - amplitude);
                    phase = Mathf.Asin(Mathf.Clamp((target.X - aimedAxis) / amplitude, -1, 1)) - Mathf.Tau * target.Y / pitch;
                    if (wave == 2) phase += (first.Strand / 2 - nearest) * .78f;
                    lean = wave == 0 ? .18f : wave == 1 ? -.24f : .34f;
                }
                if (first.ForecastPulse != 0) amplitude *= .82f;
                int side = first.Strand % 2 == 0 ? 1 : -1;
                points = EasyKoharuSamples(t => {
                    float y = Mathf.Lerp(Field.Top - 12, Field.Bottom + 24, t);
                    float cycle = Mathf.PosMod(y / pitch + phase / Mathf.Tau, 1) * (_twoBeatCurve!.PointCount - 1);
                    float x = TwoBeat ? Mathf.Clamp(axis + Mathf.Clamp((y - target.Y) * lean, -24, 24),
                        Field.Left + axisMargin, Field.Right - axisMargin) : axis;
                    return new Vector2(x + side * amplitude * _twoBeatCurve.Sample((int)cycle, cycle % 1).Y, y);
                });
                turns = Enumerable.Range(1, points.Length - 2).Where(index =>
                    Math.Sign(points[index].X - points[index - 1].X) != Math.Sign(points[index + 1].X - points[index].X))
                    .Select(index => points[index]).ToArray();
                first.HelixPhase = phase;
                first.HelixPitch = pitch;
                duration = TwoBeat ? 4.8 : 6;
                if (first.Pattern == RibbonTailPattern)
                    points = UnwindFinalRibbon(points, phase, pitch, first, out turns, out duration);
            }
            else if (_easyMode == 2)
            {
                bool right = (target.X < Field.CenterX) == (wave % 2 == 0);
                float mirroredX = Field.Left + Field.Right - target.X;
                float glanceX = right ? Mathf.Clamp(mirroredX, Field.Right - 40, Field.Right - 12)
                    : Mathf.Clamp(mirroredX, Field.Left + 12, Field.Left + 40);
                float y = 12 + first.Strand * (Field.Height - 24) / (EasyStrands - 1);
                Vector2 mirror = new(right ? Field.Left + 8 : Field.Right - 8, y);
                Vector2 glance = new(glanceX, y);
                Vector2 direction = glance.DirectionTo(target).Rotated((first.Strand / (float)(EasyStrands - 1) - .5f) * .15f);
                if (direction == Vector2.Zero) direction = Vector2.Left;
                Vector2 exit = EasyKoharuExit(glance, direction);
                double outgoing = glance.DistanceTo(exit) / 58;
                double approach = mirror.DistanceTo(glance) / (48 + wave * 6), turn = approach + (wave % 2 == 0 ? .45 : .7);
                if (NormalMirror)
                {
                    int order = wave == 0 ? 0 : wave == 1 ? first.Strand : EasyStrands - 1 - first.Strand;
                    turn += order * .21 * EasyLightSpeed;
                    first.MirrorArrival = approach / EasyLightSpeed;
                    first.MirrorReturn = turn / EasyLightSpeed;
                }
                duration = turn + outgoing;
                double flight = duration;
                points = EasyKoharuSamples(t => {
                    double at = t * flight;
                    return at < approach ? mirror.Lerp(glance, (float)(at / approach)) : at < turn ? glance
                        : glance.Lerp(exit, (float)((at - turn) / outgoing));
                });
                turns = new[] { glance };
            }
            else if (_easyMode == 3)
            {
                Vector2 bloomCenter = wave % 2 == 1 ? Center : Tier >= 2 ? target : target.MoveToward(Center, Tier == 0 ? 32 : 42);
                float spin = wave % 2 == 0 ? .34f : -.34f;
                Vector2 normalized = (target - bloomCenter) / new Vector2(Field.Width * .5f, Field.Height * .5f);
                float atTarget = normalized.Length() / .19f;
                for (int iteration = 0; iteration < 12; iteration++)
                    atTarget = normalized.Length() / (.19f * (1 + .12f * Mathf.Sin(5 * normalized.Angle() - atTarget * spin)));
                float initial = normalized == Vector2.Zero ? 0 : normalized.Angle() - atTarget * spin;
                var samples = new List<Vector2>();
                for (int step = 0; step <= 320; step++)
                {
                    float age = step * .05f, angle = initial + first.Strand * Mathf.Tau / EasyStrands + age * spin;
                    float petal = 1 + .12f * Mathf.Sin(5 * angle - age * spin);
                    Vector2 point = bloomCenter + Vector2.FromAngle(angle) * new Vector2(Field.Width * .5f, Field.Height * .5f) * (age * .19f * petal);
                    samples.Add(point);
                    if (!Field.Rect.Grow(24).HasPoint(point)) break;
                }
                points = samples.ToArray();
                duration = (points.Length - 1) * .05;
            }
            else
            {
                Vector2 landing = _destination;
                Vector2 direction = Vector2.FromAngle(landing.DirectionTo(target).Angle() + first.Strand * Mathf.Tau / EasyStrands);
                Vector2 from = landing + direction * (((BossKoharu)Boss).BodyExtent + 6);
                Vector2 joint = from + direction * 24;
                Vector2 branch = direction.Rotated(first.Strand % 2 == 0 ? .28f : -.28f);
                Vector2 exit = EasyKoharuExit(joint, branch);
                float stem = from.DistanceTo(joint), total = stem + joint.DistanceTo(exit);
                points = EasyKoharuSamples(t => t * total <= stem
                    ? from.MoveToward(joint, t * total) : joint.MoveToward(exit, t * total - stem));
                turns = new[] { joint };
                duration = total / 46;
            }
            foreach (var path in _easyPaths.Where(path => path.Wave == wave && path.Lane == first.Lane
                && path.ForecastPulse == first.ForecastPulse))
            {
                path.Points = points;
                path.HelixPhase = first.HelixPhase;
                path.HelixPitch = first.HelixPitch;
                path.Turns = turns;
                path.Duration = duration / EasyLightSpeed;
            }
        }
        foreach (var sweep in _easySweeps.Where(sweep => sweep.Wave == wave && !bloomOnly))
        {
            if (_easyMode == 4)
            {
                int pair = sweep.Pair;
                int columns = Tier == 0 ? 4 : 6, rows = Tier == 0 ? 3 : 4;
                int cell = (wave * (EasyGazePairs - 1) + (pair - 1) * 5) % (columns * rows);
                Vector2 point = pair == 0 ? target : new Vector2(Field.Left + (cell % columns + .5f) * Field.Width / columns,
                    Field.Top + (cell / columns + .5f) * Field.Height / rows);
                sweep.Points = new[] { point, point };
                float sideX = point.X < Field.CenterX ? 1 : -1, sideY = point.Y < Field.CenterY ? 1 : -1;
                sweep.EyeA = point + Vector2.Right * sideX * (sweep.HalfWidth + 9);
                sweep.EyeB = point + Vector2.Down * sideY * (sweep.HalfHeight + 9);
            }
            else if (_easyMode == 5)
            {
                _destination = Clamp(target, ((BossKoharu)Boss).BodyExtent + 9);
                if (sweep.Mode == 1)
                {
                    Vector2 middle = (_origin + _destination) * .5f + Vector2.Up * 56;
                    middle.Y = Math.Max(((BossKoharu)Boss).BodyExtent + 5, middle.Y);
                    sweep.Points = EasyKoharuSamples(t => _origin.Lerp(middle, t).Lerp(middle.Lerp(_destination, t), t));
                }
                else
                {
                    sweep.Points = new[] { _destination, _destination };
                    if (Tier >= 2) sweep.HalfWidth = Math.Max(sweep.HalfWidth, _destination.DistanceTo(target) + 10);
                }
            }
            else
            {
                Vector2 center = Tier == 3 || wave % 2 == 0 ? target : new Vector2(Field.Left + Field.Right - target.X, target.Y);
                Vector2 direction = Vector2.FromAngle(sweep.HalfHeight * Mathf.Pi / EasyBlackoutAxes + wave * Mathf.Pi / 12);
                sweep.Points = new[] { EasyKoharuExit(center, -direction), EasyKoharuExit(center, direction) };
            }
        }
    }

    private static Vector2 EasyKoharuPoint(Vector2[] points, double progress)
    {
        float at = Mathf.Clamp((float)progress, 0, 1) * (points.Length - 1);
        int index = Math.Min((int)at, points.Length - 2);
        return points[index].Lerp(points[index + 1], at - index);
    }
    private Vector2 EasyPathPosition(KoharuEasyPath path, double elapsed)
        => EasyKoharuPoint(path.Points, elapsed / path.Duration);
    private Vector2 EasySweepPosition(KoharuEasySweep sweep, double elapsed)
        => EasyKoharuPoint(sweep.Points, elapsed / sweep.Duration);

    private void PrepareGazeSteps()
    {
        using var core = new CircleShape2D { Radius = Player.HitRadius + 2 };
        var body = Boss.GetChildren().OfType<CollisionShape2D>().Where(shape => Boss.BodyContactDamaging && !shape.Disabled).ToArray();
        bool Clear(Vector2 point) => body.All(shape => !shape.Shape.Collide(shape.GlobalTransform, core, new Transform2D(0, point)));
        bool ClearPath(Vector2 a, Vector2 b)
        {
            int samples = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / 2));
            for (int i = 0; i <= samples; i++)
                if (!Clear(a.Lerp(b, i / (float)samples))) return false;
            return true;
        }
        int cells = GazeColumns * GazeRows;
        _gazeHome = Enumerable.Range(0, cells).Where(i => Clear(GazeCell(i).GetCenter()))
            .OrderBy(i => GazeCell(i).GetCenter().DistanceSquaredTo(_anchor)).First();
        _gazeBegin = Math.Max(3.1, _anchor.DistanceTo(GazeEntry) / Player.SlowestMoveSpeed + 1.2);
        var visits = new int[cells];
        int from = _gazeHome, previous = -1;
        double start = _gazeBegin + .7;
        for (int i = 0; i < (6 + Tier + (Evolved ? 2 : 0)) * (HardPressure ? 2 : 1); i++)
        {
            visits[from]++;
            int to = Enumerable.Range(0, cells).Where(next => Math.Abs(next % GazeColumns - from % GazeColumns)
                + Math.Abs(next / GazeColumns - from / GazeColumns) == 1 && ClearPath(GazeCell(from).GetCenter(), GazeCell(next).GetCenter()))
                .OrderBy(next => next == previous ? 100 : visits[next]).ThenBy(_ => GD.Randf()).First();
            double opens = start + (HardPressure ? .55 : .9);
            // The far corner of the current panel must also reach the next panel before both eyes open.
            double closes = opens + (GazeCell(from).Size.Length() + 8) / Player.SlowestMoveSpeed + (HardPressure ? .25 : .35);
            _gazeSteps.Add(new(from, to, start, opens, closes));
            previous = from;
            from = to;
            start = closes + (HardPressure ? .25 : Tier == 2 ? .7 : .45);
        }
    }

    private (int Current, int Opening) GazeOpenCells()
    {
        int current = _gazeHome;
        foreach (var step in _gazeSteps)
        {
            if (Time >= step.ClosesAt) { current = step.To; continue; }
            return (current, Time >= step.OpensAt ? step.To : -1);
        }
        return (current, -1);
    }

    private bool GazeFlank(int cell, int home)
    {
        if (home < 0) return false;
        int row = home / GazeColumns, column = home % GazeColumns;
        if ((row / 2 + column / 2) % 2 == 0) return cell == row * GazeColumns + (column ^ 1);
        return cell == (row ^ 1) * GazeColumns + column;
    }

    private void StepGazeSteps()
    {
        if (Time >= _gazeSteps[^1].ClosesAt + 1) { Finish(); return; }
        if (Time < _gazeBegin) return;
        var open = GazeOpenCells();
        for (int i = 0; i < GazeColumns * GazeRows; i++)
            if (i != open.Current && i != open.Opening && !GazeFlank(i, open.Current) && !GazeFlank(i, open.Opening)) Box(GazeCell(i), true);
        _gazeMaterial!.SetShaderParameter("elapsed", (float)Time);
        _gazeSurface!.QueueRedraw();
    }

    private void DrawGazeSteps()
    {
        var open = GazeOpenCells();
        var cue = VisibleGazeStep;
        for (int i = 0; i < GazeColumns * GazeRows; i++)
        {
            Rect2 cell = GazeCell(i);
            bool safe = i == open.Current || i == open.Opening || GazeFlank(i, open.Current) || GazeFlank(i, open.Opening);
            DrawRect(cell, new Color("d7cbe5", .27f), false, .5f);
            Vector2 center = cell.GetCenter();
            float horizontal = 1, vertical = safe ? .08f : 1;
            if (Time < _gazeBegin && !safe)
                vertical = Mathf.Clamp((float)(Time / 1.2), .08f, 1);
            if (cue is { } step && (i == step.To || GazeFlank(i, step.To)) && Time < step.OpensAt)
                vertical = .6f + .35f * Mathf.Cos((float)(Time - step.StartsAt) * 7);
            if (cue is { } closing && (i == closing.From || GazeFlank(i, closing.From)) && Time >= closing.OpensAt)
                vertical = Mathf.Lerp(.08f, .75f, (float)((Time - closing.OpensAt) / (closing.ClosesAt - closing.OpensAt)));
            Eye(center + new Vector2(-10, -12), center, horizontal, new Color("ead9ee"));
            Eye(center + new Vector2(10, -12), center, vertical, new Color("ead9ee"));
        }
    }

    private void StepEasyKoharu()
    {
        if (SequentialGaze) { StepGazeSteps(); return; }
        for (int wave = 0; wave < _easyStarts.Count; wave++)
            if (!_easyPrepared[wave] && Time >= _easyStarts[wave]) PrepareEasyKoharuWave(wave, Player.GlobalPosition);
        if (_easyMode == 5)
            for (int wave = 0; wave < _easyStarts.Count; wave++)
                if (Time >= _easyStarts[wave] + _warning && _easyBloomRead.Add(wave))
                    PrepareEasyKoharuWave(wave, Player.GlobalPosition, bloomOnly: true);
        foreach (var path in _easyPaths)
        {
            if (!_easyPrepared[path.Wave]) continue;
            float progress = EasyKoharuProgress(path.StartsAt);
            if (path.ShowsPath && Time >= path.StartsAt && Time < path.WarningUntil)
            {
                bool customForecast = TwoBeat && Tier == 2;
                for (int index = 1; index < path.Points.Length; index++)
                {
                    float part = Mathf.Clamp(progress * (path.Points.Length - 1) - index + 1, 0, 1);
                    if (part > 0) Line(path.Points[index - 1], path.Points[index - 1].Lerp(path.Points[index], part),
                        path.HalfWidth, false, part, cueOnly: customForecast);
                }
            }
            if (At(path.FiresAt))
            {
                var shot = Shot(path.Points[0], Vector2.Down, path.Pattern);
                shot.BossBarrageWave = path.Wave;
                var art = EasyKoharuVisual(path.Pattern);
                shot.UseBossIllustration(art, fitRadius: EasyKoharuHalfWidth(path.Pattern));
                shot.Homing = false;
                shot.Velocity = Vector2.Zero;
                shot.CancelLeadIn();
                path.Shot = shot;
                path.Activation = shot.ActivationId;
                path.Fired = true;
            }
            if (path.Shot is not { Active: true } bullet || bullet.ActivationId != path.Activation) continue;
            Vector2 next = EasyPathPosition(path, Time - path.FiresAt);
            if (next.DistanceSquaredTo(bullet.GlobalPosition) > .0001f) bullet.Rotation = (next - bullet.GlobalPosition).Angle();
            bullet.GlobalPosition = next;
            if (TwoBeat)
            {
                bool rose = path.Strand % 2 == 0;
                float depth = Mathf.Cos(next.Y * Mathf.Tau / path.HelixPitch + path.HelixPhase) * (rose ? 1 : -1);
                bullet.Modulate = HelixColor(path).Darkened(.09f * (1 - depth));
                bullet.ZIndex = ZIndex + (depth >= 0 ? 2 : 1);
            }
            if (!Field.Rect.Grow(18).HasPoint(next)) Pool.Despawn(bullet);
        }
        foreach (var sweep in _easySweeps)
        {
            if (!_easyPrepared[sweep.Wave]) continue;
            float progress = EasyKoharuProgress(sweep.StartsAt);
            if (sweep.Mode != 4 && Time >= sweep.StartsAt && Time < sweep.FiresAt)
            {
                if (sweep.Mode == 2) Disc(sweep.Points[0], sweep.HalfWidth, false, progress);
                else if (sweep.Mode == 3) Line(sweep.Points[0], sweep.Points[^1], sweep.HalfWidth, false, progress);
                else
                    for (int index = 1; index < sweep.Points.Length; index++)
                    {
                        float part = Mathf.Clamp(progress * (sweep.Points.Length - 1) - index + 1, 0, 1);
                        if (part > 0) Line(sweep.Points[index - 1], sweep.Points[index - 1].Lerp(sweep.Points[index], part),
                            sweep.HalfWidth + sweep.HalfHeight, false, part);
                    }
            }
            if (Time < sweep.FiresAt || Time >= sweep.EndsAt) continue;
            if (sweep.Mode is 3 or 4)
            {
                bool lit = sweep.PulseOffsets.Any(offset => Time >= sweep.FiresAt + offset && Time < sweep.FiresAt + offset + sweep.Duration);
                if (lit)
                {
                    if (sweep.Mode == 4) Box(new Rect2(sweep.Points[0] - new Vector2(sweep.HalfWidth, sweep.HalfHeight),
                        new Vector2(sweep.HalfWidth, sweep.HalfHeight) * 2), true);
                    else Line(sweep.Points[0], sweep.Points[^1], sweep.HalfWidth, true);
                    sweep.Fired = true;
                }
            }
            else
            {
                Vector2 point = EasySweepPosition(sweep, Time - sweep.FiresAt);
                if (sweep.Mode == 1) { Boss.GlobalPosition = point; Disc(point, sweep.HalfWidth, true); }
                else if (sweep.Mode == 2) { Boss.GlobalPosition = point; Disc(point, sweep.HalfWidth, true); }
                else Line(point - Vector2.Down * sweep.HalfHeight, point + Vector2.Down * sweep.HalfHeight, sweep.HalfWidth, true);
                sweep.Fired = true;
            }
        }
        bool allLaunched = _easyPaths.All(path => path.Fired) && _easySweeps.All(sweep => sweep.Fired);
        double end = _easySweeps.Count == 0 ? 0 : _easySweeps.Max(sweep => sweep.EndsAt);
        if (allLaunched && Time > end && !_easyPaths.Any(path => path.Shot is { Active: true } bullet && bullet.ActivationId == path.Activation)) Finish();
    }

    private static readonly Color EasyKoharuWarningInk = new("1a101e", .96f);
    private sealed record KoharuWarningMesh(Vector2[] Center, Vector2[] Edges);
    private readonly Dictionary<(Vector2[] Points, float Radius, bool Next, bool Sparse), KoharuWarningMesh> _easyWarningMeshes = new();
    private readonly Dictionary<Vector2[], PathWarningCanvas> _pathWarnings = new();

    private sealed partial class PathWarningCanvas : Node2D
    {
        internal KoharuWarningMesh Mesh = null!;
        internal float Radius;
        internal Color Ink;
        internal bool Active;

        public override void _Draw()
        {
            DrawPolyline(Mesh.Center, new Color(Ink, Ink.A * .10f), Radius * 2, true);
            DrawMultiline(Mesh.Edges, new Color(EasyKoharuWarningInk, Ink.A * .6f), 1.4f, true);
            DrawMultiline(Mesh.Edges, Ink, .65f, true);
            for (int i = 1; i < Mesh.Center.Length; i++)
                for (int edge = 0; edge < 4; edge++)
                    if (WarningEdgeSpan(Mesh.Center[i - 1], Mesh.Center[i], Radius, edge) is { } span
                        && span.A.DistanceSquaredTo(span.B) > .01f)
                    {
                        DrawLine(span.A, span.B, new Color(AttackTelegraph.Outline, Ink.A * .6f), 1.5f, true);
                        DrawLine(span.A, span.B, Ink, .65f, true);
                    }
        }
    }
    private bool _easyWarningOverLive;

    private static void EasyKoharuDashes(List<Vector2> edges, Vector2 from, Vector2 to, float distance, bool next, bool sparse)
    {
        float length = from.DistanceTo(to);
        if (length < .05f) return;
        Vector2 direction = (to - from) / length;
        float spacing = next ? sparse ? 28 : 22 : 34;
        for (float at = -(distance % spacing); at < length; at += spacing)
        {
            float end = Math.Min(at + 3, length);
            if (end <= 0) continue;
            edges.Add(from + direction * Math.Max(0, at));
            edges.Add(from + direction * end);
        }
    }
    private void EasyKoharuMarker(Vector2 point, Vector2 direction, bool next = true, Color? accent = null, BulletArt.PlayerVisual? art = null)
    {
        if (direction == Vector2.Zero) direction = Vector2.Down;
        direction = direction.Normalized();
        Vector2 center = point.Clamp(new Vector2(Field.Left + 4, Field.Top + 4), new Vector2(Field.Right - 4, Field.Bottom - 4));
        AttackTelegraph.ProjectileOrigin(this, center, art ?? WarningVisual, AttackTelegraph.OrderedInk(accent ?? WarningVisual.Accent, next));
    }
    private static KoharuWarningMesh EasyKoharuWarningMesh(Vector2[] points, float radius, float progress, bool arrows, bool next, bool sparse, bool helix)
    {
        var center = new List<Vector2> { points[0] };
        var edges = new List<Vector2>();
        float until = progress * (points.Length - 1), distance = 0, nextArrow = 38;
        for (int index = 1; index < points.Length && index - 1 < until; index++)
        {
            Vector2 from = points[index - 1], to = from.Lerp(points[index], Math.Min(1, until - index + 1));
            Vector2 direction = from.DirectionTo(to), side = direction.Orthogonal() * radius;
            float length = from.DistanceTo(to);
            if (length < .05f) continue;
            center.Add(to);
            if (!helix || distance % 12 < 5)
                edges.AddRange(new[] { from + side, to + side, from - side, to - side });
            EasyKoharuDashes(edges, from, to, distance, next, sparse);
            while (arrows && nextArrow <= distance + length)
            {
                Vector2 at = from + direction * (nextArrow - distance);
                edges.Add(at - direction * 2 - direction.Orthogonal() * 2);
                edges.Add(at);
                edges.Add(at);
                edges.Add(at - direction * 2 + direction.Orthogonal() * 2);
                nextArrow += sparse ? 154 : 110;
            }
            distance += length;
        }
        if (center.Count > 1)
            foreach (var (end, angle) in new[] {
                (center[0], center[0].DirectionTo(center[1]).Angle() + Mathf.Pi / 2),
                (center[^1], center[^2].DirectionTo(center[^1]).Angle() - Mathf.Pi / 2) })
            for (int step = 0; step < 16; step++)
            {
                edges.Add(end + Vector2.FromAngle(angle + step * Mathf.Pi / 16) * radius);
                edges.Add(end + Vector2.FromAngle(angle + (step + 1) * Mathf.Pi / 16) * radius);
            }
        return new(center.ToArray(), edges.ToArray());
    }

    private void EasyKoharuWarningPath(Vector2[] points, float radius, float progress, bool arrows = true, bool marker = true, bool cache = true, bool sweep = false, bool next = true, bool helix = false, Color? accent = null, BulletArt.PlayerVisual? art = null)
    {
        if (points.Length < 2) return;
        if (marker) EasyKoharuMarker(points[0], points[1] - points[0], next, accent, art);
        if (progress <= 0) return;
        if (!cache || progress < 1 || !_easyWarningMeshes.TryGetValue((points, radius, next, _easyWarningOverLive), out var mesh))
        {
            mesh = EasyKoharuWarningMesh(points, radius, progress, arrows, next, _easyWarningOverLive, helix);
            if (cache && progress >= 1) _easyWarningMeshes[(points, radius, next, _easyWarningOverLive)] = mesh;
        }
        Color ink = AttackTelegraph.OrderedInk(accent ?? (sweep ? AttackTelegraph.Sweep : WarningVisual.Accent), next);
        if (_easyWarningOverLive) ink.A *= helix ? .37f : .62f;
        if (_easyMode == 3 && Tier >= 2) ink.A *= .65f;
        if (mesh.Center.Length < 2) return;
        if (cache && progress >= 1 && !sweep)
        {
            if (!_pathWarnings.TryGetValue(points, out var canvas))
            {
                _pathWarnings[points] = canvas = new PathWarningCanvas { ShowBehindParent = true, UseParentMaterial = true };
                AddChild(canvas);
            }
            if (canvas.Mesh != mesh || canvas.Ink != ink || canvas.Radius != radius)
            {
                canvas.Mesh = mesh;
                canvas.Ink = ink;
                canvas.Radius = radius;
                canvas.QueueRedraw();
            }
            canvas.Active = true;
            return;
        }
        DrawPolyline(mesh.Center, new Color(ink, ink.A * .10f), radius * 2, true);
        DrawMultiline(mesh.Edges, new Color(EasyKoharuWarningInk, ink.A * .6f), 1.4f, true);
        DrawMultiline(mesh.Edges, ink, .65f, true);
        for (int i = 1; i < mesh.Center.Length; i++)
            DrawWarningEdgeCaps(mesh.Center[i - 1], mesh.Center[i], radius, ink);
        if (sweep)
            AttackTelegraph.Disc(this, EasyKoharuPoint(points, progress), radius, new Color(ink, ink.A * .45f), false);
    }

    private void DrawEasyKoharuGazeCue(KoharuEasySweep focus, float progress, bool next)
    {
        if (progress <= 0) return;
        Vector2 point = focus.Points[0];
        Vector2 horizontalEnd = point - Vector2.Right * Math.Sign(focus.EyeA.X - point.X) * focus.HalfWidth;
        Vector2 verticalEnd = point - Vector2.Down * Math.Sign(focus.EyeB.Y - point.Y) * focus.HalfHeight;
        Color ink = new("d7cbe5", next ? .8f : .48f);
        foreach (var (from, to, across) in new[] {
            (focus.EyeA, horizontalEnd, Vector2.Down * focus.HalfHeight),
            (focus.EyeB, verticalEnd, Vector2.Right * focus.HalfWidth),
        })
        {
            Vector2 end = from.Lerp(to, progress);
            DrawLine(from, end, new Color(ink, ink.A * .5f), .6f, true);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 offset = across * side;
                Vector2 edge = from + offset;
                if (across.X == 0 && (edge.Y < Field.Top || edge.Y > Field.Bottom)
                    || across.Y == 0 && (edge.X < Field.Left || edge.X > Field.Right)) continue;
                Vector2 tick = from.DirectionTo(to) * 2;
                Vector2 a = (edge - tick).Clamp(Field.Rect.Position, Field.Rect.End);
                Vector2 b = (edge + tick).Clamp(Field.Rect.Position, Field.Rect.End);
                DrawLine(a, b, EasyKoharuWarningInk, 1.5f, true);
                DrawLine(a, b, ink, .65f, true);
            }
        }
    }

    private void EasyKoharuWarningDisc(Vector2 center, float radius, float progress, bool next = true)
    {
        AttackTelegraph.Disc(this, center, radius * progress, AttackTelegraph.OrderedInk(AttackTelegraph.Area, next));
    }

    private void DrawEasyKoharu()
    {
        if (SequentialGaze) { DrawGazeSteps(); return; }
        _easyWarningOverLive = HazardShapes.Any(hazard => hazard.Live)
            || OwnedShots.Any(shot => !shot.LeadIn && shot.CollisionLayer != 0 && Field.Rect.HasPoint(shot.GlobalPosition));
        var paths = _easyPaths.Where(path => path.ShowsPath && _easyPrepared[path.Wave]
            && Time >= path.StartsAt && Time < path.WarningUntil).ToArray();
        var sweeps = _easySweeps.Where(sweep => _easyPrepared[sweep.Wave]
            && Time >= sweep.StartsAt && Time < sweep.FiresAt).ToArray();
        double first = paths.Select(path => Math.Max(Time, path.FiresAt)).Concat(sweeps.Select(sweep => sweep.FiresAt))
            .DefaultIfEmpty(double.PositiveInfinity).Min();
        for (int pass = 0; pass < 2; pass++)
        {
            bool next = pass == 1;
            foreach (var path in paths.Where(path => (Math.Max(Time, path.FiresAt) <= first + .02) == next)
                .OrderByDescending(path => path.FiresAt))
                EasyKoharuWarningPath(path.Points, path.HalfWidth, EasyKoharuProgress(path.StartsAt), next: next, helix: TwoBeat,
                    accent: TwoBeat ? HelixColor(path) : null,
                    art: path.Pattern == RibbonTailPattern ? EasyKoharuVisual(path.Pattern) : null);
            foreach (var sweep in sweeps.Where(sweep => (sweep.FiresAt <= first + .02) == next)
                .OrderByDescending(sweep => sweep.FiresAt))
            {
                float progress = EasyKoharuProgress(sweep.StartsAt);
                if (sweep.Mode == 4)
                    DrawEasyKoharuGazeCue(sweep, progress, next);
                else if (sweep.Mode == 2)
                {
                    EasyKoharuMarker(sweep.Points[0], Vector2.Down, next);
                    EasyKoharuWarningDisc(sweep.Points[0], sweep.HalfWidth, progress, next);
                }
                else if (sweep.Mode == 3)
                    AttackTelegraph.InstantBand(this, sweep.Points[0], sweep.Points[^1], sweep.HalfWidth, progress,
                        1 - Mathf.Clamp((float)((Time - sweep.StartsAt - .9) / (sweep.FiresAt - sweep.StartsAt - .9)), 0, 1), next);
                else EasyKoharuWarningPath(sweep.Points, sweep.HalfWidth + (sweep.Mode == 0 ? sweep.HalfHeight : 0), progress, sweep: true, next: next);
            }
        }
        foreach (var path in _easyPaths.Where(path => path.ShowsPath && _easyPrepared[path.Wave]
            && Time >= path.StartsAt + .9 && (TwoBeat ? Time < path.WarningUntil : _easyPaths.Any(pulse => pulse.Wave == path.Wave && pulse.Lane == path.Lane
                && pulse.ForecastPulse == path.ForecastPulse && Time < pulse.FiresAt + pulse.Duration))))
            foreach (var turn in path.Turns)
            {
                if (path.Pattern == RibbonTailPattern)
                {
                    AttackTelegraph.ProjectileOrigin(this, turn, EasyKoharuVisual(path.Pattern),
                        new Color(HelixColor(path), _easyWarningOverLive ? .65f : .9f), 7);
                    continue;
                }
                float size = TwoBeat ? 1.8f : 3;
                var outline = new[] { turn + Vector2.Up * size, turn + Vector2.Right * size, turn + Vector2.Down * size,
                    turn + Vector2.Left * size, turn + Vector2.Up * size };
                Color accent = TwoBeat ? new Color(HelixColor(path), _easyWarningOverLive ? .36f : .7f)
                    : EasyKoharuVisual(path.Pattern).Accent;
                DrawPolyline(outline, new Color(EasyKoharuWarningInk, EasyKoharuWarningInk.A * accent.A), 1.5f, true);
                DrawPolyline(outline, accent, .65f, true);
                if (NormalMirror)
                {
                    double age = Time - path.FiresAt;
                    if (age < path.MirrorArrival || age >= path.MirrorReturn) continue;
                    float remaining = (float)((path.MirrorReturn - age) / (path.MirrorReturn - path.MirrorArrival));
                    float gap = Mathf.Lerp(3.5f, 9, remaining);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var tip = turn + Vector2.Down * (side * gap);
                        var basePoint = tip + Vector2.Down * (side * 2.5f);
                        DrawPolyline(new[] { basePoint + Vector2.Left * 3, tip, basePoint + Vector2.Right * 3 },
                            new Color("ffe3ab"), 1f, true);
                    }
                }
            }
        if (TwoBeat)
        {
            foreach (var path in _easyPaths)
            {
                if (path.Shot is not { Active: true } shot || shot.ActivationId != path.Activation) continue;
                double age = Time - path.FiresAt;
                double length = path.Wave == 0 ? .070 : .125;
                var middle = EasyPathPosition(path, age - length * .46);
                var tail = EasyPathPosition(path, age - length);
                if (tail.DistanceSquaredTo(middle) > .01f)
                    DrawLine(tail, middle, new Color(shot.Modulate, .33f), 1.1f, true);
                if (middle.DistanceSquaredTo(shot.GlobalPosition) > .01f)
                    DrawLine(middle, shot.GlobalPosition, new Color(shot.Modulate, .67f), 1.55f, true);
            }
        }
        if (_easyMode == 4)
        {
            foreach (var focus in _easySweeps.Where(sweep => _easyPrepared[sweep.Wave]
                && Time >= sweep.StartsAt && Time < sweep.EndsAt))
            {
                Vector2 point = focus.Points[0];
                bool horizontal = focus.PulseOffsets.Any(offset => Time >= focus.FiresAt + offset - EasyGazeLead
                    && Time < focus.FiresAt + offset + focus.Duration);
                bool vertical = focus.PulseOffsets.Any(offset => Time >= focus.FiresAt + offset
                    && Time < focus.FiresAt + offset + EasyGazeTail);
                if (horizontal)
                {
                    Vector2 edge = point + Vector2.Right * Math.Sign(focus.EyeA.X - point.X) * focus.HalfWidth;
                    DrawLine(focus.EyeA, edge, new Color("d7cbe5", .55f), .6f, true);
                }
                if (vertical)
                {
                    Vector2 edge = point + Vector2.Down * Math.Sign(focus.EyeB.Y - point.Y) * focus.HalfHeight;
                    DrawLine(focus.EyeB, edge, new Color("d7cbe5", .55f), .6f, true);
                }
                Eye(focus.EyeA, point, horizontal ? .9f : .1f, new Color("d7cbe5", .7f));
                Eye(focus.EyeB, point, vertical ? .9f : .1f, new Color("d7cbe5", .7f));
            }
        }
        foreach (var hazard in HazardShapes.Where(hazard => hazard.Live))
        {
            if (_easyMode == 5 && _easySweeps.Any(sweep => sweep.Mode == 1 && Time >= sweep.FiresAt && Time < sweep.EndsAt) && hazard.Shape == 1)
            {
                DrawCircle(hazard.A, hazard.Radius, new Color("bd83ee", .16f));
                DrawArc(hazard.A, hazard.Radius, 0, Mathf.Tau, 64, new Color("281431"), 2, true);
                DrawArc(hazard.A, hazard.Radius, 0, Mathf.Tau, 64, new Color("e4b4ff"), .9f, true);
                continue;
            }
            if (hazard.Shape == 2)
            {
                DrawKoharuFocus(new Rect2(hazard.A, hazard.B - hazard.A));
                continue;
            }
            if (hazard.A == hazard.B) DrawKoharuLanding(hazard.A, hazard.Radius);
        }
    }

    private void DrawKoharuFocus(Rect2 rect)
    {
        DrawRect(rect.Grow(.6f), new Color("211329"));
        DrawRect(rect, new Color("744397", .94f));
        DrawRect(rect.Grow(-1.4f), new Color("302047", .88f));
        float scan = (float)(Time * 10 % 5);
        for (float y = rect.Position.Y + 2 + scan; y < rect.End.Y - 2; y += 5)
            DrawLine(new Vector2(rect.Position.X + 2, y), new Vector2(rect.End.X - 2, y), new Color("bf8de9", .24f), .5f);
        float size = Math.Min(rect.Size.X, rect.Size.Y) * .8f;
        Vector2 center = rect.GetCenter();
        BossProjectileArt.Get("koharu_gaze_eye").Draw(this, new Rect2(center - Vector2.One * size * .5f, Vector2.One * size), Time);
        DrawRect(rect, new Color("f4a1d5"), false, .8f);
        foreach (var sign in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
        {
            Vector2 corner = center + sign * (rect.Size * .5f - Vector2.One * 1.3f);
            DrawPolyline(new[] { corner - new Vector2(sign.X * 5, 0), corner, corner - new Vector2(0, sign.Y * 5) },
                new Color("ffe3ad"), .9f, true);
        }
    }

    private void DrawKoharuLanding(Vector2 center, float radius)
    {
        DrawCircle(center, radius, new Color("583274", .82f));
        BossProjectileArt.Get("koharu_petal").Draw(this, new Rect2(center - Vector2.One * radius * .7f, Vector2.One * radius * 1.4f), Time);
        double landedAt = _easySweeps.Where(sweep => sweep.Mode == 2 && Time >= sweep.FiresAt)
            .Select(sweep => sweep.FiresAt).DefaultIfEmpty(Time).Max();
        float bloom = Mathf.Clamp((float)((Time - landedAt) / .42), 0, 1);
        DrawArc(center, radius * (.28f + bloom * .65f), 0, Mathf.Tau, 48, new Color("ffe3ad", .8f), 1, true);
        for (int branch = 0; branch < 6; branch++)
        {
            Vector2 outward = Vector2.FromAngle(branch * Mathf.Tau / 6 - Mathf.Pi / 2), side = outward.Orthogonal();
            Vector2 root = center + outward * radius * .24f, joint = center + outward * radius * .5f;
            Vector2 tip = center + outward * radius * .82f;
            DrawPolyline(new[] { root, joint + side * radius * .08f, tip }, new Color("e4c9ff"), 1.1f, true);
            DrawLine(joint, joint + outward * radius * .12f + side * radius * .22f, new Color("ffe3ad"), .8f, true);
        }
        DrawArc(center, radius, 0, Mathf.Tau, 64, new Color("211329"), 2, true);
        DrawArc(center, radius, 0, Mathf.Tau, 64, new Color("f4a1d5"), .9f, true);
    }

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

    internal float VisibleClapTargetX => _point.X;

    protected override bool DrawCustomBox(Node2D canvas, Rect2 rect, bool live, float progress)
    {
        if (Tier != 2 || live || Kind != BossTechniqueKind.Edge) return false;
        Color guide = new Color("97e0eb", .45f + progress * .3f);
        AttackTelegraph.DashedBounds(canvas, rect, guide);
        return true;
    }

    private void Clapping()
    {
        if (Tier == 3)
        {
            const double clapTravel = 3.6, interval = 1.2, lead = 2.4;
            for (int beat = 0; beat < 12; beat++)
            {
                double fire = _warning + beat * interval;
                if (_clapLanes.Count == beat && Time >= fire - lead)
                {
                    int row = Math.Clamp((int)((Player.GlobalPosition.Y - Field.Top) * 3 / Field.Height), 0, 2);
                    _clapLanes.Add(new(row, Mathf.Clamp(Player.GlobalPosition.X, Field.Left + 56, Field.Right - 56), fire - lead, fire));
                }
            }
            foreach (var clap in _clapLanes)
            {
                double age = Time - clap.FiresAt;
                if (age >= clapTravel) continue;
                float top = Field.Top + clap.Row * Field.Height / 3;
                if (age < 0)
                {
                    Box(new Rect2(Field.Left, top, Field.Width, Field.Height / 3), false, (float)(1 + age / lead));
                    continue;
                }
                float progress = (float)(age / clapTravel);
                float left = Mathf.Lerp(Field.Left - 8, clap.Junction - 8, progress);
                float right = Mathf.Lerp(Field.Right + 8, clap.Junction + 8, progress);
                Box(new Rect2(left - 8, top, 16, Field.Height / 3), true);
                Box(new Rect2(right - 8, top, 16, Field.Height / 3), true);
            }
            if (Time >= _warning + 11 * interval + clapTravel + .35) Finish();
            return;
        }
        if (Tier >= 2)
        {
            const double travelTime = 3.6;
            if (Time >= _warning + (ClapBeats - 1) * ClapSpacing + travelTime + .35) { Finish(); return; }
            for (int beat = 0; beat < ClapBeats; beat++)
            {
                double age = Time - _warning - beat * ClapSpacing;
                if (age < -2.4 || age >= travelTime) continue;
                float top = Field.Top + beat % 3 * Field.Height / 3;
                var lane = new Rect2(Field.Left, top, Field.Width, Field.Height / 3);
                if (age < 0)
                {
                    Box(lane, false, (float)Math.Clamp(1 + age / 2.4, 0, 1));
                    Cue(new Vector2(_point.X, top + 12), AttackTelegraph.Symbol.Impact);
                    continue;
                }
                float progress = (float)(age / travelTime);
                float left = Mathf.Lerp(Field.Left - 8, _point.X - 8, progress);
                float right = Mathf.Lerp(Field.Right + 8, _point.X + 8, progress);
                Box(new Rect2(left - 8, top, 16, lane.Size.Y), true);
                Box(new Rect2(right - 8, top, 16, lane.Size.Y), true);
            }
            return;
        }
        double notice = WarnFor((1 + Density) * 5 + 10, 0.9);
        for (int beat = 0; beat < NearClapBeats; beat++)
        {
            double start = _warning + beat * 1.35;
            if (Time < start - notice) continue;
            if (_clapMeetings.Count == beat)
            {
                Vector2 target = Player.GlobalPosition;
                int nearSide = target.X < Center.X || target.X == Center.X && beat % 2 == 0 ? 1 : -1;
                _clapMeetings.Add((target.Y, Mathf.Clamp(target.X + nearSide * 27, Field.Left + 16, Field.Right - 16)));
            }
            var (y, junction) = _clapMeetings[beat];
            Vector2 meeting = new(junction, y);
            float travel = ClapTravel(junction);
            double echoAge = Time - start - travel;
            if (echoAge >= 0 && echoAge < 1.8)
                for (int side = -1; side <= 1; side += 2)
                for (int section = 0; section < 8; section++)
                {
                    float radius = 8 + (float)echoAge * 72;
                    float a = side * Mathf.Pi / 2 + (section - 4) * .19f;
                    Line(meeting + Vector2.FromAngle(a) * radius, meeting + Vector2.FromAngle(a + .19f) * radius, 2.6f, true);
                }
            else if (echoAge < 0)
                for (int side = -1; side <= 1; side += 2)
                for (int edge = -1; edge <= 1; edge++)
                {
                    Vector2 axis = Vector2.FromAngle(side * Mathf.Pi / 2 + edge * .76f);
                    Line(meeting + axis * 8, meeting + axis * 138, 2.6f, false);
                }
            for (int ray = 0; ray < 6; ray++)
            {
                Vector2 echo = Vector2.FromAngle(Mathf.Pi / 6 + ray * Mathf.Tau / 6 + beat * .16f);
                if (Time < start + travel) Line(meeting, EasyKoharuExit(meeting, echo), 3, false, 1);
                if (At(start + travel))
                {
                    var shot = Shot(meeting, echo);
                    shot.Homing = shot.Accel = false;
                    shot.Velocity = echo * (52 + Tier * 6);
                    shot.UseBossIllustration(BulletArt.EnemyVisual("koharu_star_pin", "d0b2ff"), fitRadius: 3);
                    shot.CancelLeadIn();
                }
            }
            if (Time >= start - notice && Time < start)
            {
                for (int i = 0; i < 2 + Density; i++)
                {
                    float row = y + (i - (2 + Density) / 2) * 10;
                    Line(new Vector2(Field.Left, row), new Vector2(junction, row), 4, false, (float)((Time - start + notice) / notice));
                    Line(new Vector2(Field.Right, row), new Vector2(junction, row), 4, false, (float)((Time - start + notice) / notice));
                }
            }
            if (At(start))
                for (int i = 0; i < 2 + Density; i++)
                {
                    var left = Place(new Vector2(Field.Left + 2, y + (i - (2 + Density) / 2) * 10), -1, junction);
                    var right = Place(new Vector2(Field.Right - 2, y + (i - (2 + Density) / 2) * 10), 1, junction);
                    left.Bullet.UseBossIllustration(WarningVisual, fitRadius: 4);
                    right.Bullet.UseBossIllustration(WarningVisual, fitRadius: 4);
                    left.Partner = right;
                    right.Partner = left;
                }
        }
        foreach (var light in _lights)
        {
            if (!Alive(light)) continue;
            float age = (float)(Time - light.Born);
            if (age >= ClapTravel(light.Angle) + .1f) { Pool.Despawn(light.Bullet); continue; }
            Move(light, new Vector2(Mathf.MoveToward(light.Origin.X, light.Angle, age * HandSpeed), light.Origin.Y));
        }
        foreach (var light in _lights)
            if (Alive(light) && light.Index < 0 && light.Partner is { } partner && Alive(partner)
                && light.Bullet.GlobalPosition.DistanceTo(partner.Bullet.GlobalPosition)
                    <= light.Bullet.CollisionRadius + partner.Bullet.CollisionRadius)
            {
                Pool.Despawn(light.Bullet);
                Pool.Despawn(partner.Bullet);
            }
        if (_clapMeetings.Count == NearClapBeats
            && Time > _warning + (NearClapBeats - 1) * 1.35 + ClapTravel(_clapMeetings[^1].Junction) + 1.8) Finish();
    }

    private void ScreenLight(double delta)
    {
        _screenShapes.Clear();
        if (FilledScreen) { RepeatedScreenLight(); return; }
        const double exposure = 4.3;
        double interval = _warning + 1.9;
        if (_screenFlashes.Count < 3 && Time >= _screenFlashes.Count * interval)
        {
            _angle = _origin.DirectionTo(Player.GlobalPosition).Angle();
            _screenFlashes.Add(new(_angle, Time));
        }
        for (int i = 0; i < _screenFlashes.Count; i++)
        {
            var flash = _screenFlashes[i];
            double age = Time - flash.StartsAt - _warning;
            if (age >= exposure) continue;
            if (age < 0)
            {
                ScreenStrip(flash.Angle, ScreenTurnLimit + .32f, _radius, false, 1);
                continue;
            }
            float target = _origin.DirectionTo(Player.GlobalPosition).Angle();
            float desired = Mathf.Clamp(Mathf.AngleDifference(flash.Angle, target), -ScreenTurnLimit, ScreenTurnLimit);
            float speed = Tier == 0 ? 1.05f : 1.32f;
            flash.TurnRate = Mathf.MoveToward(flash.TurnRate, Mathf.Clamp((desired - flash.Offset) * 3, -speed, speed),
                (float)delta * (Tier == 0 ? 1.55f : 2.05f));
            flash.Offset = Mathf.Clamp(flash.Offset + flash.TurnRate * (float)delta, -ScreenTurnLimit, ScreenTurnLimit);
            float spread = .2f + .12f * Mathf.Sin(Mathf.Clamp((float)(age / exposure), 0, 1) * Mathf.Pi);
            ScreenStrip(flash.Angle + flash.Offset, spread, _radius, true, 1);
        }
        if (_screenFlashes.Count == 3 && Time >= _screenFlashes[^1].StartsAt + _warning + exposure) Finish();
    }

    private double ScreenLead => WarnFor(100, 2.2);
    private double ScreenBeat => ScreenLead + 1.45;
    private void RepeatedScreenLight()
    {
        if (HardPressure)
        {
            double interval = ScreenLead + .2;
            if (_screenFlashes.Count < 6 && Time >= _screenFlashes.Count * interval)
            {
                _angle = _origin.DirectionTo(Player.GlobalPosition).Angle();
                _screenFlashes.Add(new(_angle, Time));
            }
            foreach (var flash in _screenFlashes)
                ScreenCone(flash.Angle, Time - flash.StartsAt, 2.2);
            if (_screenFlashes.Count == 6 && Time >= _screenFlashes[^1].StartsAt + ScreenLead + 2.2) Finish();
            return;
        }
        int beat = (int)(Time / ScreenBeat);
        if (beat >= Tier + 2) { Finish(); return; }
        double local = Time - beat * ScreenBeat;
        if (beat != _phase || Time == 0)
        {
            _phase = beat;
            _angle = _origin.DirectionTo(Player.GlobalPosition).Angle();
        }
        ScreenCone(_angle, local, Tier == 3 ? 1.42 : 1.15);
    }

    private void ScreenCone(float angle, double local, double duration)
    {
        bool live = local >= ScreenLead;
        if (local >= ScreenLead + duration) return;
        float widest = Tier == 3 ? .53f : .30f;
        float aperture = live ? Mathf.Lerp(.16f, widest, Mathf.Clamp((float)((local - ScreenLead) / 1.15), 0, 1)) : widest;
        ScreenStrip(angle, aperture, 0, live, (float)(local / ScreenLead));
    }

    private void ScreenStrip(float angle, float aperture, float near, bool live, float progress)
    {
        if (!FilledScreen) _screenShapes.Add((angle, aperture, near, live));
        Vector2 facing = Vector2.FromAngle(angle);
        Vector2 left = facing.Rotated(-aperture), right = facing.Rotated(aperture);
        for (float y = Field.Top; y < Field.Bottom; y += 2)
        {
            float dy = y + 1 - _origin.Y;
            float lo = Field.Left - _origin.X, hi = Field.Right - _origin.X;
            bool Clip(float rate, float offset)
            {
                if (Math.Abs(rate) < .00001f) return offset >= 0;
                float boundary = -offset / rate;
                if (rate > 0) lo = Math.Max(lo, boundary);
                else hi = Math.Min(hi, boundary);
                return lo <= hi;
            }
            if (!Clip(-left.Y, left.X * dy) || !Clip(right.Y, -right.X * dy)
                || !Clip(facing.X, facing.Y * dy - near)) continue;
            float start = Math.Max(Field.Left, Mathf.Ceil(_origin.X + lo - .5f));
            float end = Math.Min(Field.Right, Mathf.Floor(_origin.X + hi - .5f) + 1);
            if (end > start) Box(new Rect2(start, y, end - start, 2), live, progress);
        }
    }

    private void DrawScreenProjection()
    {
        Color light = new("ffe3a0");
        foreach (var beam in _screenShapes.OrderBy(b => b.Live))
        {
            if (!beam.Live && !GameManager.ShowAttackTelegraphs) continue;
            Vector2 facing = Vector2.FromAngle(beam.Angle);
            Vector2 left = facing.Rotated(-beam.Spread), right = facing.Rotated(beam.Spread);
            var corners = new List<Vector2> { Field.Rect.Position, new(Field.Right, Field.Top), Field.Rect.End, new(Field.Left, Field.Bottom) };
            void Clip(Vector2 normal, float distance)
            {
                if (corners.Count == 0) return;
                Vector2 previous = corners[^1];
                float before = (previous - _origin).Dot(normal) - distance;
                var kept = new List<Vector2>(corners.Count + 1);
                foreach (Vector2 corner in corners)
                {
                    float after = (corner - _origin).Dot(normal) - distance;
                    if ((after >= 0) != (before >= 0)) kept.Add(previous.Lerp(corner, before / (before - after)));
                    if (after >= 0) kept.Add(corner);
                    previous = corner;
                    before = after;
                }
                corners = kept;
            }
            Clip(facing, beam.Near);
            Clip(new Vector2(-left.Y, left.X), 0);
            Clip(new Vector2(right.Y, -right.X), 0);
            if (corners.Count < 3) continue;
            DrawColoredPolygon(corners.ToArray(), new Color(light, beam.Live ? .27f : .035f));
            for (int i = 0; i < corners.Count; i++)
            {
                Vector2 a = corners[i], b = corners[(i + 1) % corners.Count];
                if (a.DistanceSquaredTo(b) < .01f) continue;
                if (beam.Live) DrawLine(a, b, new Color(light, .94f), 1.1f, true);
                else DrawDashedLine(a, b, new Color(light, .65f), .7f, 4, true);
            }
        }
    }

    private void ObservingEyes(double delta)
    {
        bool moving = delta > 0 && Player.GlobalPosition.DistanceTo(_lastPlayer) / delta > 5;
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
        if (HardPressure) { OverlappingReadingHead(); return; }
        bool pending = _readingLines.Count == 0 || Time >= _readingLines[^1].EndsAt;
        if (pending)
        {
            if (_readingLines.Count >= ReadingPasses) { Finish(); return; }
            bool reverse = _readingLines.Count % 2 == 1;
            float y = Tier < 2 ? Player.GlobalPosition.Y : ReadingRowY(Math.Clamp((int)((Player.GlobalPosition.Y - Field.Top) * ReadingRows / Field.Height), 0, ReadingRows - 1));
            _readingAim = new Vector2(reverse ? Field.Right + 32 : Field.Left - 32, y);
            double lockedAt = (_readingLines.Count == 0 ? 0 : _readingLines[^1].EndsAt) + ReadingTrack;
            if (Time < lockedAt) return;
            double firesAt = lockedAt + WarnFor(ReadingWidth * 2 + Player.HitRadius * 2 + 8, 1.35);
            _readingLines.Add(new ReadingLine(_readingAim,
                new Vector2(reverse ? Field.Left - 32 : Field.Right + 32, y), lockedAt, firesAt, firesAt + ReadingSeconds));
        }
        var line = _readingLines[^1];
        ReadingLetters(line);
        Vector2 direction = line.From.DirectionTo(line.To);
        if (Time < line.FiresAt)
            Line(line.From - direction * 13, line.To + direction * 8, ReadingWidth, false);
        else
        {
            Vector2 head = line.From.Lerp(line.To, (float)((Time - line.FiresAt) / ReadingSeconds));
            Line(head - direction * 13, head + direction * 8, ReadingWidth, true);
            Disc(head, 5, true);
        }
    }

    private void OverlappingReadingHead()
    {
        double lead = WarnFor(ReadingWidth * 2 + Player.HitRadius * 2 + 8, 1.35);
        double lockedAt = _readingLines.Count == 0 ? ReadingTrack : _readingLines[^1].EndsAt + .15 - lead;
        if (_readingLines.Count < ReadingPasses && Time >= lockedAt)
        {
            bool reverse = _readingLines.Count % 2 == 1;
            float y = ReadingRowY(Math.Clamp((int)((Player.GlobalPosition.Y - Field.Top) * ReadingRows / Field.Height), 0, ReadingRows - 1));
            _readingAim = new Vector2(reverse ? Field.Right + 32 : Field.Left - 32, y);
            double fire = Time + lead;
            _readingLines.Add(new(_readingAim, new Vector2(reverse ? Field.Left - 32 : Field.Right + 32, y), Time, fire, fire + ReadingSeconds));
        }
        foreach (var line in _readingLines.Where(line => Time < line.EndsAt))
        {
            ReadingLetters(line);
            Vector2 direction = line.From.DirectionTo(line.To);
            if (Time < line.FiresAt) Line(line.From - direction * 13, line.To + direction * 8, ReadingWidth, false);
            else
            {
                Vector2 head = line.From.Lerp(line.To, (float)((Time - line.FiresAt) / ReadingSeconds));
                Line(head - direction * 13, head + direction * 8, ReadingWidth, true);
                Disc(head, 5, true);
            }
        }
        if (_readingLines.Count == ReadingPasses && Time >= _readingLines[^1].EndsAt) Finish();
    }

    private void ReadingLetters(ReadingLine line)
    {
        int columns = Tier switch { 0 => 10, 1 => 14, 2 => 18, _ => 22 };
        for (int column = 0; column < columns; column++)
        {
            float along = (column + .5f) / columns;
            Vector2 from = line.From.Lerp(line.To, along);
            Vector2 direction = from.Y < Field.CenterY ? Vector2.Down : Vector2.Up;
            double fire = line.FiresAt + along * ReadingSeconds;
            if (Time < line.FiresAt) Line(from, from + direction * Field.Height, 3, false);
            for (int pulse = 0; pulse < (Tier < 2 ? 2 : 3); pulse++)
                if (At(fire + pulse * .12) && fire + pulse * .12 < line.EndsAt)
                {
                    var shot = Shot(from, direction);
                    shot.Homing = shot.Accel = false;
                    shot.UseBossIllustration(BulletArt.EnemyVisual("res://assets/fx/koharu_heart_charm.svg", "ff91ad"), fitRadius: 3);
                    shot.Velocity = direction * (34 + pulse * 6);
                    shot.CancelLeadIn();
                }
        }
    }

    internal (Vector2 Center, Vector2 Opening) VisibleAntlers => (_point, Vector2.FromAngle(_angle + Mathf.Pi / 2));

    private bool AntlersReachingAcross => Tier == 0 && _antlerRound > 0;
    private Vector2 AntlerHinge(int side)
    {
        if (!AntlersReachingAcross) return _point + new Vector2(side * 64, -48).Rotated(_angle);
        bool fromTop = (side < 0) == (_antlerRound % 2 == 1);
        return new(side < 0 ? Field.Left + 11 : Field.Right - 11, fromTop ? Field.Top + 18 : Field.Bottom - 18);
    }
    private float AntlerFacing(int side, float closing)
    {
        if (!AntlersReachingAcross) return _angle + Mathf.Pi / 2 + side * Mathf.Lerp(-.42f, Mathf.Atan2(64, 48), closing);
        Vector2 hinge = AntlerHinge(side);
        float committed = hinge.DirectionTo(_point).Angle();
        float towardCenter = hinge.DirectionTo(Center).Angle();
        float open = towardCenter + side * (_antlerRound % 2 == 1 ? .74f : -.74f);
        return Mathf.LerpAngle(open, committed, closing);
    }
    private float AntlerLength(Vector2 hinge) => AntlersReachingAcross ? hinge.DistanceTo(_point) + 43 : _antlerRound == 0 ? 105 : 137;
    private static (Vector2 Bend, Vector2 End, Vector2 Tangent) AntlerTrace(Vector2 root, Vector2 forward, float length)
    {
        Rect2 rim = Field.Rect.Grow(-21);
        Vector2 end = root + forward * length;
        float entry = 0, first = length;
        bool crossedX = false;
        bool Crosses(float start, float rate, float lo, float hi, bool horizontal)
        {
            if (Math.Abs(rate) < .00001f) return start >= lo && start <= hi;
            float enter = (lo - start) / rate, leave = (hi - start) / rate;
            if (enter > leave) (enter, leave) = (leave, enter);
            entry = Math.Max(entry, enter);
            if (leave < first) { first = leave; crossedX = horizontal; }
            return entry <= first;
        }
        if (!Crosses(root.X, forward.X, rim.Position.X, rim.End.X, true)
            || !Crosses(root.Y, forward.Y, rim.Position.Y, rim.End.Y, false)
            || first >= length) return (end, end, forward);
        Vector2 bend = root + forward * first;
        Vector2 tangent = crossedX ? new Vector2(0, forward.Y >= 0 ? 1 : -1) : new Vector2(forward.X >= 0 ? 1 : -1, 0);
        return (bend, (bend + tangent * (length - first)).Clamp(rim.Position, rim.End), tangent);
    }

    private void Antlers()
    {
        int rounds = HardPressure ? 4 : Math.Max(3, Tier + 1);
        if (_antlerRound >= rounds) { Finish(); return; }
        double age = Time - _antlerFire;
        if (age >= 4.1)
        {
            if (++_antlerRound >= rounds) { Finish(); return; }
            Vector2 oldCenter = _point;
            _point = Player.GlobalPosition;
            Vector2 branchFacing = (_point - oldCenter).Normalized() + _point.DirectionTo(Center);
            _angle = (branchFacing == Vector2.Zero ? _point.DirectionTo(Center) : branchFacing).Angle() - Mathf.Pi / 2;
            _antlerFire = Time + _warning;
            age = Time - _antlerFire;
        }
        bool live = age >= 0 && age < 4;
        float closing = age < 0 ? 0 : Mathf.Clamp((float)age / (2.8f - Tier * 0.18f), 0, 1);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 hinge = AntlerHinge(side);
            float angle = AntlerFacing(side, closing);
            Vector2 direction = Vector2.FromAngle(angle);
            float stem = AntlerLength(hinge);
            float thickness = HardPressure ? 5.8f : 3.2f + Tier * .4f;
            var trunk = AntlerTrace(hinge, direction, stem);
            Line(hinge, hinge + direction * stem, thickness, live);
            if (trunk.Bend != trunk.End) Line(trunk.Bend, trunk.End, thickness, live);
            if (!live && age < 0)
                for (int step = 1; step <= 6; step++)
                {
                    float sweepAngle = AntlerFacing(side, step / 6f);
                    var next = AntlerTrace(hinge, Vector2.FromAngle(sweepAngle), stem);
                    Line(next.Bend, next.End, thickness, false, 1, cueOnly: true);
                    if (AntlersReachingAcross)
                    {
                        Vector2 prior = hinge + Vector2.FromAngle(AntlerFacing(side, (step - 1) / 6f)) * stem;
                        Line(prior, hinge + Vector2.FromAngle(sweepAngle) * stem, thickness, false, 1, cueOnly: true);
                        if (step == 6) Line(hinge, hinge + Vector2.FromAngle(sweepAngle) * stem, thickness, false, 1, cueOnly: true);
                    }
                }
            int branches = HardPressure ? 8 : 2 + Tier;
            for (int branch = 1; branch <= branches; branch++)
            {
                float along = branch * (stem - 12) / (branches + 1);
                float length = 28 + branch * 3;
                Vector2 fork = hinge + direction * along;
                Vector2 tip = fork + direction.Rotated(-side * .75f) * length;
                Line(fork, tip, HardPressure ? 3.6f : 2.4f + Tier * 0.3f, live, (float)Math.Min(1, Time / _warning));
                {
                    Vector2 closed = Vector2.FromAngle(AntlerFacing(side, 1));
                    Vector2 twig = closed.Rotated(-side * .75f);
                    Vector2 release = hinge + closed * along + twig * length;
                    Vector2 releaseDirection = branch % 2 == 0
                        ? release.DirectionTo(_point + Vector2.FromAngle(_angle + Mathf.Pi / 2) * (60 + branch * 12))
                        : twig.Rotated(side * (branch / (float)(branches + 1) - .5f) * 1.5f);
                    double fire = _antlerFire + 2.5;
                    if (Time < fire) Line(release, release + releaseDirection * 400, 3, false);
                    for (int pulse = 0; pulse < (Tier < 2 ? 3 : 5); pulse++)
                        if (At(fire + pulse * .19))
                        {
                            var shot = Shot(release, releaseDirection);
                            shot.Homing = shot.Accel = false;
                            shot.UseBossIllustration(BulletArt.EnemyVisual("koharu_star_pin", "d0b2ff"), fitRadius: 3);
                            shot.Velocity = releaseDirection * (42 + pulse * 5);
                            shot.CancelLeadIn();
                        }
                }
            }
        }
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
        if (Tier >= 2) { PersistentPursuit(delta); return; }
        if (Time < _warning) Line(_origin, _anchor, PursuitBodyRadius, false, 1);
        if (At(_warning))
        {
            _point = _origin;
            _angle = _origin.DirectionTo(_anchor).Angle();
            int count = 8 + Density * 3;
            for (int i = 1; i < count; i++) Place(_origin, i);
            Boss.ShowAnimalAttack();
            _tail.Add((_origin, Time - 4));
        }
        if (Time >= _warning)
        {
            float desired = _point.DirectionTo(Player.GlobalPosition).Angle();
            if (Time < _warning + PursuitHuntDuration)
                _angle = Mathf.RotateToward(_angle, desired, (float)delta * (Tier == 0 ? .72f : .82f));
            _point += Vector2.FromAngle(_angle) * (float)delta * 60 * Speed;
            if (Time < _warning + PursuitHuntDuration)
            {
                _point = _point.Clamp(new Vector2(Field.Left + 12, Field.Top + 12), new Vector2(Field.Right - 12, Field.Bottom - 12));
                Boss.GlobalPosition = _point;
                Disc(_point, PursuitBodyRadius, true);
            }
            _tail.Insert(0, (_point, Time));
            while (_tail.Count > 2 && _tail[^2].Time < Time - 4) _tail.RemoveAt(_tail.Count - 1);
            foreach (var light in _lights)
            {
                double sampleTime = Time - .5 - light.Index * 0.13;
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
        if (Time > _warning + PursuitHuntDuration) Finish();
    }

    private double PursuitHuntDuration => Tier switch { 0 => 6.8, 1 => 8, 2 => 17, _ => 20 };
    private int PursuitTailCount => (7 + Density * 3) * (HardPressure ? 2 : 1);
    private double PursuitTailSeconds => HardPressure ? 5 : 4;
    internal float PursuitSpeedAt(float distance) => Mathf.Lerp(HardPressure ? 24 : Tier == 2 ? 20 : 22,
        HardPressure ? 66 : Tier == 2 ? 58 : 64, Mathf.Clamp(distance / 120, 0, 1));
    internal bool PursuitSteeredThisFrame => Tier >= 2 && Time >= _warning && PursuitHunting;
    internal bool PursuitHunting => Time < _warning + PursuitHuntDuration;
    internal override Vector2 ResolvePlayerMotion(Vector2 from, Vector2 to)
    {
        if (Finished || !_pursuitBarriers.Any(b => b.Solid)) return to;
        int steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to)));
        Vector2 stride = (to - from) / steps;
        for (int step = 0; step < steps; step++)
        {
            Vector2 next = from + stride;
            foreach (var barrier in _pursuitBarriers.Where(b => b.Solid))
            {
                Rect2 bounds = barrier.Bounds.Grow(Player.HitRadius);
                if (from.Y > bounds.Position.Y && from.Y < bounds.End.Y)
                {
                    if (from.X <= bounds.Position.X && next.X > bounds.Position.X) next.X = bounds.Position.X;
                    else if (from.X >= bounds.End.X && next.X < bounds.End.X) next.X = bounds.End.X;
                }
                if (next.X > bounds.Position.X && next.X < bounds.End.X)
                {
                    if (from.Y <= bounds.Position.Y && next.Y > bounds.Position.Y) next.Y = bounds.Position.Y;
                    else if (from.Y >= bounds.End.Y && next.Y < bounds.End.Y) next.Y = bounds.End.Y;
                }
            }
            from = next;
        }
        return from;
    }
    private void PersistentPursuit(double delta)
    {
        _pursuitScenery!.QueueRedraw();
        float warningRadius = PursuitBodyRadius;
        foreach (var barrier in _pursuitBarriers)
            if (Time >= _warning && !barrier.Bounds.Grow(Player.HitRadius + 1).HasPoint(Player.GlobalPosition))
                barrier.Solid = true;
        if (Time < _warning)
        {
            _anchor = Player.GlobalPosition;
            Line(_origin, _anchor, warningRadius, false, (float)(Time / _warning));
        }
        if (At(_warning))
        {
            _point = _origin;
            _angle = _origin.DirectionTo(_anchor).Angle();
            for (int i = 1; i <= PursuitTailCount; i++)
            for (int lane = -1; lane <= 1; lane++) Place(_origin, i, lane);
            Boss.ShowAnimalAttack();
            _tail.Add((_origin, Time - PursuitTailSeconds));
        }
        if (Time < _warning) return;
        bool hunting = PursuitHunting;
        float distance = _point.DistanceTo(Player.GlobalPosition);
        if (hunting) _pursuitExitSpeed = PursuitSpeedAt(distance);
        float speed = _pursuitExitSpeed;
        if (hunting) _angle = _point.DirectionTo(Player.GlobalPosition).Angle();
        Vector2 direction = Vector2.FromAngle(_angle);
        Line(_point, _point + direction * (hunting ? Math.Min(distance, speed * .6f) : speed * .6f), warningRadius, false, .6f);
        _point += direction * (hunting ? Math.Min(distance, (float)delta * speed) : (float)delta * speed);
        if (hunting)
        {
            _point = _point.Clamp(new Vector2(Field.Left + 12, Field.Top + 12), new Vector2(Field.Right - 12, Field.Bottom - 12));
            Boss.GlobalPosition = _point;
            Disc(_point, PursuitBodyRadius, true);
        }
        _tail.Insert(0, (_point, Time));
        while (_tail.Count > 2 && _tail[^2].Time < Time - PursuitTailSeconds) _tail.RemoveAt(_tail.Count - 1);
        foreach (var light in _lights)
        {
            double sampleTime = Time - .5 - light.Index * .11;
            int sample = 0;
            while (sample < _tail.Count - 1 && _tail[sample + 1].Time > sampleTime) sample++;
            Vector2 position = _tail[sample].Position;
            if (sample + 1 < _tail.Count)
            {
                var older = _tail[sample + 1];
                float weight = (float)Math.Clamp((sampleTime - older.Time) / (_tail[sample].Time - older.Time), 0, 1);
                position = older.Position.Lerp(position, weight);
                position += older.Position.DirectionTo(_tail[sample].Position).Orthogonal() * light.Angle * 18;
            }
            if (hunting && Alive(light))
            {
                light.Bullet.Rotation = (position - light.Bullet.GlobalPosition).Angle();
                light.Bullet.GlobalPosition = position;
            }
            else Move(light, position);
        }
        if (!hunting) Finish();
    }

    private void DrawPursuitBarriers()
    {
        var canvas = _pursuitScenery!;
        Color ink = new("f4b4d1"), outline = new("35253d"), face = new("563a59");
        foreach (var barrier in _pursuitBarriers)
        {
            Rect2 rect = barrier.Bounds;
            Vector2[] corners = { rect.Position, new(rect.End.X, rect.Position.Y), rect.End, new(rect.Position.X, rect.End.Y), rect.Position };
            if (barrier.Solid)
            {
                canvas.DrawRect(new Rect2(rect.Position + Vector2.Down * 3, rect.Size), new Color(outline, .65f));
                canvas.DrawRect(rect, new Color(face, .92f));
                canvas.DrawPolyline(corners, outline, 3.2f, true);
                canvas.DrawPolyline(corners, ink, 1.2f, true);
                canvas.DrawLine(rect.Position + new Vector2(2, 2), new Vector2(rect.End.X - 2, rect.Position.Y + 2), new Color("ffe7f0"), .7f, true);
            }
            else
            {
                canvas.DrawRect(rect, UsesDangerForecast ? new Color(face, .48f) : new Color(ink, .10f));
                if (UsesDangerForecast)
                {
                    Vector2 inset = rect.Size / 2 - Vector2.One * 3;
                    foreach (float dx in new[] { -inset.X, inset.X })
                        foreach (float dy in new[] { -inset.Y, inset.Y })
                            canvas.DrawCircle(rect.GetCenter() + new Vector2(dx, dy), 1, new Color(ink, .72f));
                }
                for (int i = 1; i < corners.Length; i++)
                {
                    canvas.DrawDashedLine(corners[i - 1], corners[i], outline, 2.8f, 3, true);
                    canvas.DrawDashedLine(corners[i - 1], corners[i], new Color(ink, .85f), 1, 3, true);
                }
            }
            Vector2 center = rect.GetCenter();
            for (int side = -1; side <= 1; side += 2)
                canvas.DrawPolyline(new[] { center, center + new Vector2(side * 5, -3), center + new Vector2(side * 5, 3), center },
                    new Color(ink, barrier.Solid ? .9f : .5f), .9f, true);
            canvas.DrawCircle(center, 1.3f, ink);
        }
    }

    private void UnbalancedSupport(double delta)
    {
        _supportWarnings!.QueueRedraw();
        int resting = _supportLights.Count(w => w.DropAt < 0 && Alive(w.Light));
        _supportSag = Mathf.MoveToward(_supportSag, Math.Min(14, resting * 1.7f), (float)delta * 11);
        float bias = Mathf.Clamp((Player.GlobalPosition.X - SupportCenter.X) / (Tier >= 2 ? 90 * SupportScale : Field.Width * .5f), -1, 1);
        _supportTilt = Mathf.MoveToward(_supportTilt, bias * .36f, (float)delta * (HardPressure ? .24f : .12f + Tier * .02f));
        if (_spilledLight != null && !Alive(_spilledLight))
        {
            _spilledLight = null;
            _supportFlashAt = -1;
            _supportPressure = 0;
        }
        if (Time >= _warning && Time < _warning + (SupportPairs - 1) * SupportInterval + 4 && _supportFlashAt < 0)
        {
            _supportPressure = Mathf.Clamp(_supportPressure + (float)delta
                * (Math.Abs(_supportTilt) > .1f ? HardPressure ? .75f : .53f : -.24f), 0, 1);
            if (_supportPressure >= 1)
            {
                _supportFlashAt = Time;
                Vector2 pivot = SupportCenter, tip = SupportPoint(Math.Sign(_supportTilt) * SupportHalf);
                Vector2 rail = tip - pivot;
                float along = 1;
                if (tip.Y < Field.Top + 5) along = (Field.Top + 5 - pivot.Y) / rail.Y;
                if (tip.Y > Field.Bottom - 5) along = (Field.Bottom - 5 - pivot.Y) / rail.Y;
                if (tip.X < Field.Left + 5) along = Math.Min(along, (Field.Left + 5 - pivot.X) / rail.X);
                if (tip.X > Field.Right - 5) along = Math.Min(along, (Field.Right - 5 - pivot.X) / rail.X);
                _spillFrom = pivot + rail * Mathf.Clamp(along, 0, 1);
                Vector2 slide = (Player.GlobalPosition - _lastPlayer) / (float)delta;
                Vector2 target = Clamp(Player.GlobalPosition + slide.LimitLength(72) * .72f, Player.HitRadius + 1);
                float flight = Mathf.Clamp(_spillFrom.DistanceTo(target) / 75, .8f, 1.8f);
                _spillGravity = Math.Min(42, (Math.Min(_spillFrom.Y, target.Y) - Field.Top + 5) * 3 / (flight * flight));
                _spillVelocity = (target - _spillFrom) / flight - Vector2.Down * (_spillGravity * flight);
                var path = new List<Vector2> { _spillFrom };
                for (int step = 1; step <= 100; step++)
                {
                    Vector2 next = SpillPoint(step * .1f);
                    path.Add(next);
                    if (!Field.Rect.Grow(18).HasPoint(next)) break;
                }
                _spillPath = path.ToArray();
            }
        }
        if (_supportFlashAt >= 0)
        {
            double fire = _supportFlashAt + SpillLead;
            if (Time < fire)
                for (int step = 1; step < _spillPath.Length; step++)
                    Line(_spillPath[step - 1], _spillPath[step], 6, false, 1);
            if (At(fire))
            {
                _spilledLight = Place(_spillFrom, -1, pattern: 2);
                _spilledLight.Bullet.UseBossIllustration(SupportDropVisual, fitRadius: 6);
            }
            if (_spilledLight is { } light) Move(light, SpillPoint((float)(Time - fire)));
        }
        int pairs = SupportPairs;
        for (int pair = 0; pair < pairs; pair++)
        {
            double release = _warning + pair * SupportInterval;
            double lead = SupportLaunchLead(pair);
            for (int side = -1; side <= 1; side += 2)
            {
                float along = SupportSpawnAlong(pair, side);
                if (Time >= release - lead && Time < release)
                {
                    float progress = (float)Math.Clamp((Time - release + lead) / lead, 0, 1);
                    Disc(SupportPoint(along), 9, false, progress);
                    Line(SupportPoint(along), SupportPoint(along - side * 28 * SupportScale), 6, false, progress);
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
        int loadedSide = Math.Abs(_supportTilt) > .07f ? Math.Sign(_supportTilt) : 0;
        if (loadedSide != 0 && _loadedSupportSide != 0 && loadedSide != _loadedSupportSide)
        {
            var loose = _supportLights.Where(w => Alive(w.Light) && w.DropAt < 0)
                .OrderByDescending(w => w.Along * _loadedSupportSide).Take(2).ToArray();
            foreach (var weight in loose)
            {
                float height = Math.Max(6, SupportPoint(weight.Along).Y - Field.Top - 8);
                float across = 48 + Tier * 5 + weight.Light.Index % 3 * 9;
                ReleaseSupportWeight(weight, new Vector2(loadedSide * across, -Mathf.Sqrt(72 * height)));
            }
        }
        if (loadedSide != 0) _loadedSupportSide = loadedSide;
        foreach (var weight in _supportLights)
        {
            if (!Alive(weight.Light)) continue;
            if (weight.DropAt < 0)
            {
                float dt = weight.Light.Born == Time ? 0 : (float)delta;
                float acceleration = Mathf.Sin(_supportTilt) * (45 + Tier * 17) - weight.Velocity * .08f;
                weight.Velocity = Mathf.Clamp(weight.Velocity + acceleration * dt, -34 - Tier * 4, 34 + Tier * 4);
                weight.Along += weight.Velocity * dt;
                if (Math.Abs(weight.Along) >= SupportHalf || Time >= _warning + (pairs - 1) * SupportInterval + 5)
                {
                    weight.Along = Mathf.Clamp(weight.Along, -SupportHalf, SupportHalf);
                    Vector2 velocity = Vector2.FromAngle(_supportTilt) * weight.Velocity * .5f + Vector2.Down * 22;
                    if (Tier >= 2)
                        velocity = new Vector2(weight.Velocity * (.6f + weight.Light.Index % 5 * .18f),
                            -Mathf.Sqrt(72 * Math.Max(0, SupportPoint(weight.Along).Y - Field.Top - 12)));
                    ReleaseSupportWeight(weight, velocity);
                }
                Move(weight.Light, SupportPoint(weight.Along));
                if (weight.DropAt < 0) continue;
            }
            if (Time < weight.DropAt)
            {
                for (int part = 1; part < weight.DropPath.Length; part++)
                    Line(weight.DropPath[part - 1], weight.DropPath[part], weight.DropRadius, false);
                Move(weight.Light, weight.DropOrigin);
            }
            else Move(weight.Light, SupportFallPoint(weight, (float)(Time - weight.DropAt)));
            weight.Light.Bullet.Rotation = 0;
        }
        if (Time > _warning + (pairs - 1) * SupportInterval + 5
            && (_supportFlashAt < 0 || Time >= _supportFlashAt + SpillLead)) Finish();
    }

    private void ReleaseSupportWeight(SupportLight weight, Vector2 velocity)
    {
        weight.DropAt = Time + SupportDropWarning;
        weight.DropOrigin = SupportPoint(weight.Along);
        weight.DropVelocity = velocity;
        weight.DropRadius = weight.Light.Bullet.IllustrationRadius;
        weight.Light.Bullet.UseBossIllustration(SupportDropVisual, fitRadius: weight.DropRadius);
        var route = new List<Vector2> { weight.DropOrigin };
        for (int step = 1; step <= 120; step++)
        {
            Vector2 next = SupportFallPoint(weight, step * .1f);
            route.Add(next);
            if (!Field.Rect.Grow(28).HasPoint(next)) break;
        }
        weight.DropPath = route.ToArray();
    }

    private static Vector2 SupportFallPoint(SupportLight weight, float time)
        => weight.DropOrigin + weight.DropVelocity * time + Vector2.Down * (18 * time * time);

    private void DrawSupportWarnings()
    {
        if (Finished || !GameManager.ShowAttackTelegraphs) return;
        var canvas = _supportWarnings!;
        if (_supportFlashAt >= 0 && Time < _supportFlashAt + SpillLead)
        {
            for (int step = 1; step < _spillPath.Length; step++)
                AttackTelegraph.Route(canvas, _spillPath[step - 1], _spillPath[step], 6,
                    step == 1, false, step == 1, step == _spillPath.Length - 1, SupportDropVisual);
        }
        for (int pair = 0; pair < SupportPairs; pair++)
        {
            double release = _warning + pair * SupportInterval;
            double lead = SupportLaunchLead(pair);
            if (Time < release - lead || Time >= release) continue;
            for (int side = -1; side <= 1; side += 2)
            {
                float along = SupportSpawnAlong(pair, side);
                AttackTelegraph.Disc(canvas, SupportPoint(along), 9, WarningVisual.Accent);
                AttackTelegraph.Route(canvas, SupportPoint(along), SupportPoint(along - side * 28 * SupportScale),
                    6, true, true, true, true, WarningVisual);
            }
        }
        foreach (var weight in _supportLights.Where(weight => Alive(weight.Light) && weight.DropAt >= 0 && Time < weight.DropAt))
        {
            for (int part = 1; part < weight.DropPath.Length; part++)
                AttackTelegraph.Route(canvas, weight.DropPath[part - 1], weight.DropPath[part], weight.DropRadius,
                    part == 1, false, part == 1, part == weight.DropPath.Length - 1, SupportDropVisual);
            int middle = Math.Max(1, weight.DropPath.Length / 3);
            AttackTelegraph.Arrow(canvas, weight.DropPath[middle], weight.DropPath[middle - 1].DirectionTo(weight.DropPath[middle]), SupportDropVisual.Accent);
        }
    }

    private double PrayerInterval => Tier >= 2 ? 2.8 : 3.2;
    private const float PrayerDuration = 4.2f;
    private int PrayerBands => Tier switch { 0 => 2, 1 => 3, 2 => 5, _ => 7 };
    private Vector2 PrayerCrossing(int wave, int band)
    {
        Vector2 target = _prayerTargets[wave];
        float pitch = Field.Height / PrayerBands;
        int aligned = Math.Clamp((int)((target.Y - Field.Top) / pitch), 0, PrayerBands - 1);
        target.Y += (band - aligned) * pitch;
        return target;
    }

    private void PassingPrayers()
    {
        int waves = Math.Max(3, Tier + 1);
        int pulses = HardPressure ? 8 : 4 + (Tier < 2 ? Tier : 0);
        double spacing = HardPressure ? .15 : .23;
        for (int wave = 0; wave < waves; wave++)
        {
            double start = wave * PrayerInterval, fire = start + _warning;
            if (Time < start) continue;
            if (_prayerTargets.Count == wave) _prayerTargets.Add(Player.GlobalPosition);
            for (int band = 0; band < PrayerBands; band++)
            {
                Vector2 target = PrayerCrossing(wave, band);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (Time < fire)
                        for (int i = 0; i < 24; i++)
                            Line(PrayerPoint(target, side, i / 24f), PrayerPoint(target, side, (i + 1) / 24f), 4, false, 1);
                    for (int pulse = 0; pulse < pulses; pulse++)
                        if (At(fire + pulse * spacing))
                            Place(PrayerPoint(target, side, 0), side, wave * PrayerBands + band)
                                .Bullet.UseBossIllustration(WarningVisual, fitRadius: 4);
                }
            }
        }
        foreach (var light in _lights)
        {
            if (!Alive(light)) continue;
            int wave = (int)light.Angle / PrayerBands, band = (int)light.Angle % PrayerBands;
            float t = (float)(Time - light.Born) / PrayerDuration;
            Move(light, PrayerPoint(PrayerCrossing(wave, band), light.Index, t));
        }
        if (Time > (waves - 1) * PrayerInterval + _warning + (pulses - 1) * spacing + PrayerDuration) Finish();
    }

    private static Vector2 PrayerPoint(Vector2 target, int side, float t)
    {
        Vector2 from = new(side < 0 ? Field.Left - 12 : Field.Right + 12, Mathf.Clamp(target.Y + side * 58, Field.Top + 6, Field.Bottom - 6));
        Vector2 to = new(side < 0 ? Field.Right + 12 : Field.Left - 12, Mathf.Clamp(target.Y - side * 58, Field.Top + 6, Field.Bottom - 6));
        Vector2 a = t <= .5f ? from : target, b = t <= .5f ? target : to;
        Vector2 bend = new((a.X + b.X) * .5f, t <= .5f ? a.Y : b.Y);
        float progress = t <= .5f ? t * 2 : (t - .5f) * 2;
        return a.Lerp(bend, progress).Lerp(bend.Lerp(b, progress), progress);
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

    private readonly Dictionary<UnpairedVolley, FanRay[]> _unpairedRays = new();
    internal FanRay[] UnpairedRays(UnpairedVolley volley)
    {
        if (_unpairedRays.TryGetValue(volley, out var rays)) return rays;
        int count = (Tier < 2 ? 9 : 5) + Tier * 2;
        return _unpairedRays[volley] = Tier == 2
            ? SplitFan(volley.Origin, volley.Direction, (count - 1) * .085f, .14f)
            : Enumerable.Range(0, count).Select(i => new FanRay(volley.Origin,
                volley.Direction.Rotated((i - (count - 1) * .5f) * .17f))).ToArray();
    }

    private void UnpairedLight(double delta)
    {
        int cycle = Math.Max(0, (int)((Time - _warning) / UnpairedCycle));
        if (cycle >= 4) { Finish(); return; }
        double start = _warning + cycle * UnpairedCycle;
        int single = (cycle * 2 + 1) % 7;
        int[] others = new int[6];
        for (int i = 0, k = 0; i < 7; i++) if (i != single) others[k++] = i;
        for (int i = 0; i < 7; i++) Disc(Supporter(i), 5, false, 0.7f, cueOnly: true);
        for (int pair = 0; pair < 3; pair++)
        {
            int first = (pair + cycle) % 3;
            Line(Supporter(others[first]), Supporter(others[5 - first]), 1, false, 0.25f, cueOnly: true);
        }
        Vector2 from = Supporter(single);
        if (At(start))
        {
            Vector2 movement = (Player.GlobalPosition - _lastPlayer) / (float)delta;
            _point = Clamp(Player.GlobalPosition + movement.LimitLength(100) * .9f, Player.HitRadius + 2);
            if (Tier == 0)
            {
                int passing = Enumerable.Range(0, 7).MinBy(i => Supporter(i).DistanceSquaredTo(Player.GlobalPosition));
                Vector2 around = (Player.GlobalPosition - Center).Normalized();
                float tangent = around.Cross(movement);
                _unpairedAnswerSeat = -1;
                if (Supporter(passing).DistanceTo(Player.GlobalPosition) < 52 && Math.Abs(tangent) > 30)
                {
                    int side = Math.Sign(tangent);
                    _unpairedAnswerSeat = Mathf.PosMod(passing + side * 2, 7);
                    if (_unpairedAnswerSeat == single) _unpairedAnswerSeat = Mathf.PosMod(single + side, 7);
                    _point = Supporter(_unpairedAnswerSeat);
                }
            }
            Vector2 direction = from.DirectionTo(_point);
            if (direction == Vector2.Zero) direction = from.DirectionTo(Center);
            _unpairedVolleys.Add(new(from, direction, EasyKoharuExit(from, direction), start + .45, start + .45 + UnpairedLead));
        }
        foreach (var volley in _unpairedVolleys)
        {
            foreach (var (fromPoint, direction) in UnpairedRays(volley))
            {
                float radius = Tier == 2 ? 4 : UnpairedRadius;
                if (Time >= volley.StartsAt && Time < volley.FiresAt + (UnpairedShots - 1) * UnpairedShotSpacing)
                    Line(fromPoint, EasyKoharuExit(fromPoint, direction), radius, false);
                for (int i = 0; i < UnpairedShots; i++)
                {
                    if (!At(volley.FiresAt + i * UnpairedShotSpacing)) continue;
                    var shot = Shot(fromPoint, direction, 1);
                    shot.UseBossIllustration(WarningVisual, fitRadius: radius);
                    shot.Homing = shot.Accel = false;
                    shot.Velocity = direction * (58 + Tier * 8 + i * 16);
                    shot.CancelLeadIn();
                    shot.Rotation = direction.Angle();
                }
            }
        }
    }

    private void InwardCoil()
    {
        float age = (float)(Time - _coilStartsAt - _warning);
        if ((Tier == 3 || HardPressure) && age >= 5.4f && _coilRound < (HardPressure ? 1 : 2))
        {
            _coilRound++;
            _point = Clamp(Player.GlobalPosition, 70);
            _angle = _point.DirectionTo(Center).Angle();
            if (_point.DistanceSquaredTo(Center) < 16) _angle = _point.DirectionTo(_origin).Angle() + Mathf.Pi;
            _coilStartsAt = Time;
            age = (float)-_warning;
        }
        if (Tier >= 2)
        {
            if (age >= 5.4f) { Finish(); return; }
            if (age < 3)
                OutsideDisc(_point, 105 - Math.Max(0, age) * 18, age >= 0);
            if (age < 4.4)
            {
                float coilRadius = Math.Max(2, 90 - Math.Max(0, age) * 20);
                for (int i = 0; i < 48; i++)
                {
                    float halfGap = HardPressure ? .42f : .55f;
                    float a = _angle + halfGap + i * (Mathf.Tau - halfGap * 2) / 48;
                    float b = _angle + halfGap + (i + 1) * (Mathf.Tau - halfGap * 2) / 48;
                    Line(_point + Vector2.FromAngle(a) * (coilRadius + i * .32f),
                        _point + Vector2.FromAngle(b) * (coilRadius + (i + 1) * .32f), 4, age >= 0);
                }
            }
            if (age >= 3 && age < 5.2)
                Disc(_point, 46, age >= 4.8, Mathf.Clamp((age - 3) / .5f, 0, 1));
            return;
        }
        float radius = OpenCoilRadius(age);
        float open = _point.DirectionTo(Center).Angle() + Math.Max(0, age) * .32f;
        bool live = age >= 0 && age < 5.5;
        for (int i = 0; i < 48; i++)
        {
            float a = open + .43f + i * (Mathf.Tau - .86f) / 48;
            float b = open + .43f + (i + 1) * (Mathf.Tau - .86f) / 48;
            float r1 = radius + i * 0.32f, r2 = radius + (i + 1) * 0.32f;
            Line(_point + Vector2.FromAngle(a) * r1, _point + Vector2.FromAngle(b) * r2,
                3 + Tier * 0.5f, live, (float)Math.Min(1, Time / _warning));
        }
        if (age >= 3.3f && age < 6.05f)
            Disc(_point, 24 + Tier * 4, age >= 5.6f, Mathf.Clamp((age - 3.3f) / .25f, 0, 1));
        if (age >= 6.05f) Finish();
    }

    internal (Vector2 Center, Vector2 GapDirection, bool OutsideOpen) VisibleCoil
        => (_point, Vector2.FromAngle(_angle), Time >= _coilStartsAt + _warning + 3);

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
        Cue(new Vector2(Field.CenterX, 28), rest ? AttackTelegraph.Symbol.Rest : AttackTelegraph.Symbol.Pips, count: pulse % 3 + 1);
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

    private float AttentionDistance => Math.Min(Field.Rect.Size.Length() + 16, 90 + Tier * 5
        + (float)Math.Max(0, Time - _warning) * (20 + 7 * Tier));
    internal bool AttentionLocked => _firedAt >= 0;

    private void Attention(double delta)
    {
        int flashes = HardPressure ? 5 : Math.Max(3, Tier + 1);
        if (_phase >= flashes) { Finish(); return; }
        float watchDistance = AttentionDistance;
        float distance = Player.GlobalPosition.DistanceTo(_origin);
        if (Time >= _warning && _firedAt < 0)
        {
            _charge = Math.Clamp(_charge + delta * (distance < watchDistance ? (0.32 + Tier * 0.06) * (HardPressure ? 2 : 1) : -0.8), 0, 1);
            if (_charge >= 1)
            {
                _firedAt = Time;
                _point = Player.GlobalPosition;
                _radius = 48 + Tier * 4;
            }
        }
        if (Tier < 2) Disc(_origin, watchDistance, false, (float)_charge * 0.8f);
        if (_firedAt < 0)
        {
            Disc(_origin, 16 + (float)_charge * 25, false, (float)_charge);
        }
        else
        {
            double strike = _firedAt + WarnFor(_radius + 8, 1.4);
            Disc(_point, _radius, Time >= strike && Time < strike + (HardPressure ? 1.2 : .6),
                (float)Math.Clamp((Time - _firedAt) / (strike - _firedAt), 0, 1));
            int rays = Tier < 2 ? 18 + Tier * 4 : 24 + Tier * 4;
            for (int ray = 0; ray < rays; ray++)
                {
                    Vector2 direction = Vector2.FromAngle(ray * Mathf.Tau / rays + _phase * .17f);
                    if (Time < strike) Line(_point, _point + direction * 400, 3, false);
                    for (int pulse = 0; pulse < (Tier < 2 ? 2 : 3); pulse++)
                        if (At(strike + pulse * .2))
                        {
                            var shot = Shot(_point, direction);
                            shot.Homing = shot.Accel = false;
                            shot.UseBossIllustration(BulletArt.EnemyVisual("koharu_star_pin", "d0b2ff"), fitRadius: 3);
                            shot.Velocity = direction * (42 + pulse * 6);
                            shot.CancelLeadIn();
                        }
                }
            if (Time > strike + (HardPressure ? 1.4 : .8))
            {
                if (++_phase >= flashes) Finish();
                else { _firedAt = -1; _charge = Tier < 2 ? .42 : 0; }
            }
        }

    }

    private const float CameoLightRadius = 4;
    private double DominoSpacing => Tier >= 2 ? .24 : .48;
    private int DominoColumns => 7 + Density;
    private const float DominoTurnSpeed = 2.2f;
    private double DominoReturn => _warning + (DominoColumns - 1) * DominoSpacing
        + Mathf.Pi / (2 * DominoTurnSpeed) + (Field.Height - 2 - DominoLength(0)) / (80 * Speed);
    private static float DominoLength(int seed) => 12 + seed * 9;
    private Vector2 DominoHinge(int column, bool returning)
    {
        float pitch = Field.Width / DominoColumns;
        bool fallsTowardPlayer = returning == (_anchor.Y < Field.CenterY);
        float offset = fallsTowardPlayer ? Mathf.PosMod(_anchor.X - Field.Left, pitch) : pitch * .5f;
        return new Vector2(Field.Left + column * pitch + offset, returning ? Field.Bottom - 2 : Field.Top + 2);
    }

    private Vector2 DominoPoint(Vector2 hinge, float length, float age, bool returning)
    {
        float angle = Math.Min(Mathf.Pi / 2, age * DominoTurnSpeed);
        float side = hinge.X < Field.CenterX ? 1 : -1, vertical = returning ? -1 : 1;
        float fall = Math.Max(0, age - Mathf.Pi / (2 * DominoTurnSpeed)) * 80 * Speed;
        return hinge + new Vector2(side * Mathf.Cos(angle) * length, vertical * (Mathf.Sin(angle) * length + fall));
    }

    private void Domino()
    {
        for (int wave = 0; wave < 2; wave++)
        for (int i = 0; i < DominoColumns; i++)
        {
            bool returning = wave == 1;
            int column = returning ? DominoColumns - 1 - i : i;
            double start = (returning ? DominoReturn : _warning) + i * DominoSpacing;
            Vector2 hinge = DominoHinge(column, returning);
            if (At(start))
            {
                for (int seed = 0; seed < (Tier >= 2 ? 7 + Density : 3 + Density); seed++)
                {
                    float length = DominoLength(seed);
                    var light = Place(DominoPoint(hinge, length, 0, returning), wave * DominoColumns + column, length);
                    light.Bullet.UseBossIllustration(WarningVisual, fitRadius: CameoLightRadius);
                }
                continue;
            }
            if (Time < start - _warning || Time >= start) continue;
            // The falling lights form one arm; show its sector and its common falling lane once.
            float outer = DominoLength((Tier >= 2 ? 6 : 2) + Density);
            Vector2 previous = DominoPoint(hinge, outer, 0, returning);
            for (int segment = 1; segment <= 8; segment++)
            {
                Vector2 next = DominoPoint(hinge, outer, segment * Mathf.Pi / (16 * DominoTurnSpeed), returning);
                Line(previous, next, CameoLightRadius, false);
                previous = next;
            }
            Vector2 firstDrop = hinge + (returning ? Vector2.Up : Vector2.Down) * DominoLength(0);
            Line(firstDrop, new Vector2(hinge.X, returning ? Field.Top : Field.Bottom), CameoLightRadius, false);
        }
        foreach (var light in _lights)
        {
            if (!Alive(light)) continue;
            bool returning = light.Index >= DominoColumns;
            Vector2 hinge = DominoHinge(light.Index % DominoColumns, returning);
            Move(light, DominoPoint(hinge, light.Angle, (float)(Time - light.Born), returning));
        }
        if (Time > DominoReturn + (DominoColumns - 1) * DominoSpacing) Finish();
    }

    private Vector2[] DominoSector(Vector2 hinge, bool returning)
    {
        float outer = DominoLength((Tier >= 2 ? 6 : 2) + Density);
        var sector = new Vector2[18];
        for (int i = 0; i <= 8; i++)
        {
            float age = i * Mathf.Pi / (16 * DominoTurnSpeed);
            sector[i] = DominoPoint(hinge, outer + CameoLightRadius, age, returning);
            sector[17 - i] = DominoPoint(hinge, DominoLength(0) - CameoLightRadius, age, returning);
        }
        return sector;
    }

    private void DrawDominoSector(Vector2 hinge, bool returning)
    {
        var sector = DominoSector(hinge, returning);
        Color ink = WarningVisual.Accent;
        DrawColoredPolygon(sector, new Color(ink, .15f));
        DrawDashedLine(sector[0], sector[17], new Color(ink, .7f), .8f, 2, true);
        AttackTelegraph.Arrow(this, sector[4], sector[3].DirectionTo(sector[5]), ink);
    }

    private const double SearchScan = 1.2, SearchSpacing = .22;
    private const int SearchRounds = 4;
    private readonly List<(double Time, Vector2 At)> _observedAudience = new();
    private int _searchSourceRound;
    private Vector2 _nextSearchSource;
    private double SearchLead => WarnFor(36, 1.25);
    private int SearchPulses => Tier >= 2 ? 9 : 5 + Tier;
    private double SearchCycle => SearchScan + SearchLead + SearchPulses * SearchSpacing;

    private Vector2 SearchFocus(Vector2 position, Vector2 velocity)
    {
        Vector2 ordinary = Clamp(position + velocity.LimitLength(95) * .8f, Player.HitRadius + 2);
        if (Tier >= 2 || velocity.Length() < 12) return ordinary;
        Vector2 heading = velocity.Normalized();
        int match = -1;
        float closest = 13;
        for (int i = 1; i < _observedAudience.Count && _observedAudience[i].Time < Time - SearchLead - 1; i++)
        {
            var sample = _observedAudience[i];
            float distance = sample.At.DistanceTo(position);
            if (distance >= closest || _observedAudience[i - 1].At.DirectionTo(sample.At).Dot(heading) < .87f) continue;
            match = i;
            closest = distance;
        }
        if (match < 0) return ordinary;
        var seen = _observedAudience[match];
        for (int i = match + 1; i < _observedAudience.Count; i++)
        {
            var sample = _observedAudience[i];
            if (sample.Time - seen.Time >= SearchLead + SearchSpacing + _origin.DistanceTo(sample.At) / (64 + Tier * 7))
                return sample.At;
        }
        return ordinary;
    }

    private void Searching(double delta)
    {
        if (Tier < 2 && (_observedAudience.Count == 0 || Time - _observedAudience[^1].Time >= .14))
            _observedAudience.Add((Time, Player.GlobalPosition));
        int round = (int)(Time / SearchCycle);
        if (round >= SearchRounds) { Finish(); return; }
        if (_searchSourceRound < round)
        {
            _searchSourceRound = round;
            _origin = _nextSearchSource;
        }
        double age = Time - round * SearchCycle;
        if (age < SearchScan)
        {
            _angle = _origin.DirectionTo(Player.GlobalPosition).Angle() + .50f * Mathf.Cos((float)(age / SearchScan) * Mathf.Tau);
            return;
        }
        if (_phase <= round)
        {
            Vector2 glance = (Player.GlobalPosition - _lastPlayer) / (float)delta;
            _point = SearchFocus(Player.GlobalPosition, glance);
            var direction = _origin.DirectionTo(_point);
            _angle = (direction == Vector2.Zero ? Vector2.Left : direction).Angle();
            _nextSearchSource = Clamp(EasyKoharuExit(_origin, Vector2.FromAngle(_angle)), 12);
            _phase = round + 1;
        }
        double first = round * SearchCycle + SearchScan + SearchLead;
        for (int ray = -Density - 2; ray <= Density + 2; ray++)
        {
            Vector2 direction = Vector2.FromAngle(_angle + ray * .12f);
            Line(_origin, EasyKoharuExit(_origin, direction), CameoLightRadius, false);
            for (int pulse = 0; pulse < SearchPulses; pulse++)
                if (At(first + pulse * SearchSpacing))
                {
                    var bullet = Shot(_origin, direction, 1);
                    bullet.Homing = false;
                    bullet.Velocity = direction * (64 + Tier * 7);
                    bullet.UseBossIllustration(WarningVisual, fitRadius: CameoLightRadius);
                    bullet.CancelLeadIn();
                }
        }
    }

    protected override void DrawTechnique()
    {
        foreach (var canvas in _pathWarnings.Values) canvas.Active = false;
        if (BodyPursuit)
        {
            Color ink = new("e6a8f3");
            if (GameManager.ShowAttackTelegraphs)
                foreach (var hazard in HazardShapes.Where(h => !h.Live && h.Shape == 0))
                {
                    Vector2 side = hazard.A.DirectionTo(hazard.B).Orthogonal() * hazard.Radius;
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        DrawDashedLine(hazard.A + side * sign, hazard.B + side * sign, AttackTelegraph.Outline, 1.8f, 3, true);
                        DrawDashedLine(hazard.A + side * sign, hazard.B + side * sign, new Color(ink, .65f), .7f, 3, true);
                    }
                    Eye(hazard.A, hazard.B, .75f, ink);
                }
            DrawBodyContact(PursuitBodyRadius, ink, Time >= _warning
                && PursuitHunting);
            return;
        }
        if (_easyMode != 0)
        {
            DrawEasyKoharu();
            foreach (var canvas in _pathWarnings.Values) canvas.Visible = canvas.Active;
            return;
        }
        var light = new Color("eee0f4", .9f);
        var reflection = new Color("c5b7da", .75f);
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
                if (Tier == 3)
                {
                    foreach (var clap in _clapLanes)
                    {
                        double elapsed = Time - clap.FiresAt;
                        if (elapsed > 3.95) continue;
                        float y = Field.Top + (clap.Row + .5f) * Field.Height / 3;
                        float progress = Mathf.Clamp((float)elapsed / 3.6f, 0, 1);
                        if (elapsed < 0 && GameManager.ShowAttackTelegraphs)
                        {
                            float top = Field.Top + clap.Row * Field.Height / 3;
                            Color stop = new("9cdcf1", .8f);
                            Vector2[] corners = { new(clap.Junction - 16, top), new(clap.Junction + 16, top),
                                new(clap.Junction + 16, top + Field.Height / 3), new(clap.Junction - 16, top + Field.Height / 3) };
                            for (int edge = 0; edge < corners.Length; edge++)
                            {
                                DrawDashedLine(corners[edge], corners[(edge + 1) % corners.Length], AttackTelegraph.Outline, 1.8f, 3, true);
                                DrawDashedLine(corners[edge], corners[(edge + 1) % corners.Length], stop, .7f, 3, true);
                            }
                            Vector2 caption = new(clap.Junction >= Field.CenterX ? Field.Left + 34 : Field.Right - 34, top + 12);
                            DrawDashedLine(caption + new Vector2(Math.Sign(clap.Junction - caption.X) * 24, -3),
                                new Vector2(clap.Junction, top + 4), new Color(stop, .55f), .45f, 2, true);
                            AttackTelegraph.DrawSymbol(this, new Vector2(clap.Junction, top + 12), AttackTelegraph.Symbol.Impact, stop);
                        }
                        ClapHand(new Vector2(Mathf.Lerp(Field.Left - 8, clap.Junction - 8, progress), y), 1, light);
                        ClapHand(new Vector2(Mathf.Lerp(Field.Right + 8, clap.Junction + 8, progress), y), -1, light);
                        if (elapsed >= 3.6)
                            DrawArc(new Vector2(clap.Junction, y), 8 + (float)(elapsed - 3.6) * 36,
                                0, Mathf.Tau, 24, new Color(light, (float)(3.95 - elapsed)), .8f, true);
                    }
                    break;
                }
                if (Tier >= 2)
                {
                    for (int beat = 0; beat < ClapBeats; beat++)
                    {
                        double elapsed = age - beat * ClapSpacing;
                        if (elapsed < -2.4 || elapsed > 3.95) continue;
                        float y = Field.Top + (beat % 3 + .5f) * Field.Height / 3;
                        float progress = Mathf.Clamp((float)elapsed / 3.6f, 0, 1);
                        ClapHand(new Vector2(Mathf.Lerp(Field.Left - 8, _point.X - 8, progress), y), 1, light);
                        ClapHand(new Vector2(Mathf.Lerp(Field.Right + 8, _point.X + 8, progress), y), -1, light);
                        if (elapsed >= 3.6)
                            DrawArc(new Vector2(_point.X, y), 8 + (float)(elapsed - 3.6) * 36,
                                0, Mathf.Tau, 24, new Color(light, (float)(3.95 - elapsed)), .8f, true);
                    }
                    break;
                }
                for (int beat = 0; beat < NearClapBeats; beat++)
                {
                    if (beat >= _clapMeetings.Count) break;
                    var (y, junction) = _clapMeetings[beat];
                    double elapsed = age - beat * 1.35;
                    float travel = ClapTravel(junction);
                    if (elapsed >= -1 && elapsed < travel)
                    {
                        float distance = Math.Max(0, (float)elapsed) * HandSpeed;
                        ClapHand(new Vector2(Mathf.MoveToward(Field.Left + 2, junction, distance), y), 1, light);
                        ClapHand(new Vector2(Mathf.MoveToward(Field.Right - 2, junction, distance), y), -1, light);
                    }
                    if (elapsed >= travel && elapsed < travel + .3)
                    {
                        float fade = 1 - (float)(elapsed - travel) / .3f;
                        for (int side = -1; side <= 1; side += 2)
                            DrawArc(new Vector2(junction, y), 5 + (1 - fade) * 10,
                                side < 0 ? 2.5f : -.6f, side < 0 ? 3.8f : .6f, 8, new Color(light, fade * .6f), .8f, true);
                    }
                }
                break;
            case BossTechniqueKind.Area when Variant == 0:
                if (!FilledScreen) DrawScreenProjection();
                else foreach (var hazard in HazardShapes.Where(h => h.Shape == 2).OrderBy(h => h.Live))
                    {
                        Color glow = new("ffdf75");
                        DrawRect(new Rect2(hazard.A, hazard.B - hazard.A), new Color(glow, hazard.Live ? .30f : .11f));
                        if (hazard.Live || (int)hazard.A.Y / 4 % 2 == 0)
                            foreach (float x in new[] { hazard.A.X, hazard.B.X })
                            {
                                Vector2 a = new(x, hazard.A.Y), b = new(x, hazard.B.Y);
                                DrawLine(a, b, new Color("231c37", .95f), 2.4f);
                                DrawLine(a, b, new Color(glow, hazard.Live ? 1 : .9f), 1);
                            }
                    }
                float phoneAngle = _angle;
                if (!FilledScreen && _screenFlashes.Count > 0)
                {
                    ScreenFlash phone = _screenFlashes[^1];
                    phoneAngle = phone.Angle + phone.Offset;
                    if (Time < phone.StartsAt + _warning)
                    {
                        Vector2 rim = Clamp(_origin + Vector2.FromAngle(phone.Angle) * Math.Max(8, _radius - 16), 14);
                        Vector2 phoneAim = Vector2.FromAngle(phone.Angle);
                        DrawDashedLine(rim + phoneAim * 8, rim + phoneAim * 32, light, 1, 3, true);
                        AttackTelegraph.Arrow(this, rim + phoneAim * 25, phoneAim, light);
                        DrawArc(rim, 15, phone.Angle - ScreenTurnLimit, phone.Angle + ScreenTurnLimit, 20, reflection, .9f, true);
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            Vector2 stop = Vector2.FromAngle(phone.Angle + sign * ScreenTurnLimit);
                            DrawLine(rim + stop * 12, rim + stop * 18, light, 1.6f, true);
                        }
                    }
                }
                Vector2 facing = Vector2.FromAngle(phoneAngle), sideAxis = facing.Orthogonal();
                Vector2 screen = Clamp(_origin + facing * Math.Max(8, _radius - 16), 14);
                if (FilledScreen) screen = Clamp(_origin + facing * 24, 9);
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
                var scan = VisibleReadingLine;
                Vector2 aim = scan?.From ?? _readingAim;
                Vector2 eye = new(aim.X < Field.Left ? Field.Left + 7 : Field.Right - 7, aim.Y);
                Eye(eye, scan?.To ?? Player.GlobalPosition, scan.HasValue ? 1 : .45f, light);
                if (scan is { } current)
                {
                    bool live = Time >= current.FiresAt;
                    DrawDashedLine(new Vector2(Field.Left, current.From.Y), new Vector2(Field.Right, current.From.Y),
                        new Color(reflection, live ? .20f : .35f), .45f, 8, true);
                    if (live)
                    {
                        Vector2 head = current.From.Lerp(current.To, (float)((Time - current.FiresAt) / ReadingSeconds));
                        for (int side = -1; side <= 1; side += 2)
                            DrawLine(head + new Vector2(-7, side * (ReadingWidth + 2)), head + new Vector2(7, side * (ReadingWidth + 2)), light, .8f, true);
                    }
                }
                break;
            case BossTechniqueKind.Signature:
                double antlerAge = Time - _antlerFire;
                float closing = antlerAge < 0 ? 0 : Mathf.Clamp((float)antlerAge / (2.8f - Tier * .18f), 0, 1);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 hinge = AntlerHinge(side);
                    float angle = AntlerFacing(side, closing);
                    DrawArc(hinge, 6, angle - .9f, angle + .9f, 10, light, 1, true);
                    Vector2 dir = Vector2.FromAngle(angle);
                    int forks = HardPressure ? 8 : 2 + Tier;
                    for (int fork = 1; fork <= forks; fork++)
                    {
                        Vector2 joint = hinge + dir * (fork * (AntlerLength(hinge) - 12) / (forks + 1));
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
                    Eye(_point - Vector2.FromAngle(_angle) * 9,
                        Tier < 2 || PursuitHunting ? Player.GlobalPosition : _point + Vector2.FromAngle(_angle) * 80,
                        Tier < 2 || PursuitHunting ? 1 : .3f, light);
                    for (int i = 3; i < _lights.Count; i += 4)
                        if (Alive(_lights[i]))
                            DrawLine(_lights[i].Bullet.GlobalPosition + new Vector2(-2, -7), _lights[i].Bullet.GlobalPosition + new Vector2(2, -7), reflection, .8f, true);
                }
                break;
            case BossTechniqueKind.Crossfire when Variant == 1:
                Vector2 supportAxis = Vector2.FromAngle(_supportTilt);
                for (float along = -SupportHalf; along < SupportHalf; along += 14)
                    DrawLine(SupportCenter + supportAxis * along, SupportCenter + supportAxis * Math.Min(along + 8, SupportHalf),
                        new Color(reflection, .38f), .7f, true);
                ClapHand(SupportCenter + new Vector2(-7, 12), 1, new Color(light, .55f));
                ClapHand(SupportCenter + new Vector2(7, 12), -1, new Color(light, .55f));
                if (Tier >= 2)
                {
                    DrawArc(SupportCenter + Vector2.Up * 20, 10, -Mathf.Pi / 2, Mathf.Tau - Mathf.Pi / 2, 32, new Color(reflection, .35f), .7f, true);
                    if (_supportPressure > 0)
                        DrawArc(SupportCenter + Vector2.Up * 20, 10, -Mathf.Pi / 2, _supportPressure * Mathf.Tau - Mathf.Pi / 2,
                            32, new Color(light, .85f), 1.5f, true);
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 supportEnd = SupportCenter + supportAxis * (side * SupportHalf);
                    Penlight(supportEnd + Vector2.Down * 7, _supportTilt, side * _supportTilt > .03f, reflection);
                    if (side * _supportTilt > .03f)
                        DrawPolyline(new[] { supportEnd + new Vector2(-3, 10), supportEnd + new Vector2(0, 14), supportEnd + new Vector2(3, 10) },
                            new Color(light, .65f), .8f, true);
                }
                foreach (var weight in _supportLights)
                {
                    if (!Alive(weight.Light)) continue;
                    Vector2 at = weight.Light.Bullet.GlobalPosition;
                    if (weight.DropAt >= 0 && Time < weight.DropAt)
                    {
                        float progress = Mathf.Clamp((float)(1 - (weight.DropAt - Time) / SupportDropWarning), 0, 1);
                        DrawArc(weight.DropOrigin, 10, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * progress,
                            24, SupportDropVisual.Accent, .7f, true);
                    }
                    else if (weight.DropAt < 0) Petal(at + Vector2.Up * 9, _supportTilt, new Color(light, .55f));
                }
                break;
            case BossTechniqueKind.Crossfire when Variant == 2:
                if (_prayerTargets.Count == 0) break;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 target = _prayerTargets[^1];
                    Vector2 start = PrayerPoint(target, side, 0);
                    Penlight(start + new Vector2(side * 2, 0), side * -.65f, Time >= _warning, light);
                    Vector2 destination = PrayerPoint(target, side, 1);
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
                int cycle = Math.Min(3, Math.Max(0, (int)(age / UnpairedCycle)));
                int single = (cycle * 2 + 1) % 7;
                var others = new List<int>();
                for (int i = 0; i < 7; i++) if (i != single) others.Add(i);
                for (int pair = 0; pair < 3; pair++)
                {
                    int first = (pair + cycle) % 3;
                    SmallNumber(Supporter(others[first]) + new Vector2(0, 16), pair + 1, reflection);
                    SmallNumber(Supporter(others[5 - first]) + new Vector2(0, 16), pair + 1, reflection);
                }
                Penlight(Supporter(single), age >= cycle * UnpairedCycle ? Supporter(single).DirectionTo(_point).Angle() - Mathf.Pi / 2 : 0, true, light);
                if (Tier == 0 && _unpairedAnswerSeat >= 0)
                {
                    Vector2 answering = Supporter(_unpairedAnswerSeat);
                    Penlight(answering, answering.DirectionTo(Supporter(single)).Angle() - Mathf.Pi / 2, true, light);
                }
                break;
            case BossTechniqueKind.Dodge:
                if (Tier >= 2) break;
                float radius = OpenCoilRadius((float)age);
                float endAngle = _point.DirectionTo(Center).Angle() + Math.Max(0, (float)age) * .32f - .43f;
                Vector2 end = _point + Vector2.FromAngle(endAngle) * (radius + 48 * .32f);
                if (age < 0)
                    AttackTelegraph.Arrow(this, end, Vector2.FromAngle(endAngle + Mathf.Pi / 2), reflection);
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
                if (Tier >= 2 && _firedAt < 0)
                    DrawArc(_origin, AttentionDistance, 0, Mathf.Tau, 96, new Color("b4a0cf", .45f), .7f, true);
                Eye(_origin + new Vector2(0, -20), _firedAt < 0 ? Player.GlobalPosition : _point, .2f + .8f * (float)_charge, light);
                for (int i = 0; i < 5; i++)
                    DrawLine(_origin + new Vector2(-12 + i * 6, 29), _origin + new Vector2(-12 + i * 6, 33),
                        new Color(reflection, _charge >= (i + 1) / 5f ? .8f : .2f), 1.5f, true);
                break;
            case BossTechniqueKind.Cameo when Variant == 0:
                for (int wave = 0; wave < 2; wave++)
                for (int i = 0; i < DominoColumns; i++)
                {
                    bool returning = wave == 1;
                    double start = (returning ? DominoReturn : _warning) + i * DominoSpacing;
                    if (Time < start - _warning || Time > start + .8) continue;
                    Vector2 hinge = DominoHinge(returning ? DominoColumns - 1 - i : i, returning);
                    if (Time < start) DrawDominoSector(hinge, returning);
                    DrawArc(hinge, 4, 0, Mathf.Tau, 10, reflection, .8f, true);
                    SmallNumber(hinge + new Vector2(0, returning ? -8 : 10), i + 1, light);
                }
                break;
            case BossTechniqueKind.Cameo:
                double scanAge = Time % SearchCycle;
                bool locked = scanAge >= SearchScan && Time < SearchRounds * SearchCycle;
                Vector2 audienceEye = Clamp(_origin + new Vector2(0, -14), 8);
                Eye(audienceEye, audienceEye + Vector2.FromAngle(_angle) * 100, locked ? 1 : .45f, light);
                if (locked && scanAge < SearchScan + SearchLead)
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
        // A fixed frontal pose keeps the moving pupil and the degree of eyelid closure legible.
        DrawSetTransform(at, 0, new Vector2(1, height / 6));
        BossProjectileArt.Get("koharu_gaze_eye").Draw(this, new Rect2(-7, -7, 14, 14), 0, color);
        DrawSetTransform(Vector2.Zero);
        AttackTelegraph.Stroke(this, new[] { at + new Vector2(-7, 0), at + new Vector2(-3, -height), at + new Vector2(3, -height),
            at + new Vector2(7, 0), at + new Vector2(3, height), at + new Vector2(-3, height), at + new Vector2(-7, 0) }, color, .8f);
        DrawCircle(at + at.DirectionTo(target) * 2, 1.1f + open * .5f, color);
    }

    private void Penlight(Vector2 at, float angle, bool on, Color color)
    {
        if (on && CoversPoint(at))
        {
            DrawSetTransform(at, angle);
            BossProjectileArt.Get("koharu_penlight").Draw(this, new Rect2(-8, -8, 16, 16), Time);
            DrawSetTransform(Vector2.Zero);
            return;
        }
        Vector2 along = Vector2.Down.Rotated(angle), across = along.Orthogonal();
        AttackTelegraph.Stroke(this, new[] { at - along * 5 - across * 2, at + along * 3 - across * 2,
            at + along * 3 + across * 2, at - along * 5 + across * 2, at - along * 5 - across * 2 }, color, .8f);
        DrawLine(at + along * 3, at + along * 7, color, 1, true);
        if (on) DrawLine(at - along * 3, at + along, new Color(color, .85f), 1.2f, true);
    }

    private void Petal(Vector2 at, float angle, Color color)
    {
        if (CoversPoint(at))
        {
            DrawSetTransform(at, angle);
            BossProjectileArt.Get("koharu_petal").Draw(this, new Rect2(-8, -8, 16, 16), Time);
            DrawSetTransform(Vector2.Zero);
            return;
        }
        var direction = Vector2.FromAngle(angle);
        var side = direction.Orthogonal();
        AttackTelegraph.Stroke(this, new[] { at - direction * 8, at - direction * 2 + side * 4, at + direction * 7,
            at - direction * 2 - side * 4, at - direction * 8 }, color, .8f);
    }

    private void ClapHand(Vector2 at, int facing, Color color)
    {
        if (Kind == BossTechniqueKind.Edge && CoversPoint(at))
        {
            DrawSetTransform(at, 0, new Vector2(facing, 1));
            BossProjectileArt.Get("koharu_clap").Draw(this, new Rect2(-7, -7, 14, 14), Time);
            DrawSetTransform(Vector2.Zero);
            return;
        }
        AttackTelegraph.Stroke(this, new[] { at + new Vector2(-facing * 6, 6), at + new Vector2(facing * 2, 3), at + new Vector2(facing * 4, -3),
            at + new Vector2(facing * 1, -7), at + new Vector2(-facing * 3, -3), at + new Vector2(-facing * 6, 6) }, color, .8f);
        DrawLine(at + new Vector2(-facing * 1, -3), at + new Vector2(facing * 2, 1), color, .6f, true);
    }

    private void RestMark(Vector2 at, Color color)
    {
        AttackTelegraph.Stroke(this, new[] { at + new Vector2(-2, -5), at + new Vector2(3, -1), at + new Vector2(-1, 2), at + new Vector2(2, 5) }, color, 1);
    }

    private void SmallNumber(Vector2 at, int number, Color color)
    {
        AttackTelegraph.Pips(this, at, number, color);
    }
}
