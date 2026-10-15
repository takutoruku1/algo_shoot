using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BossNormalBarrage : Node2D
{
    private Enemy _boss = null!;
    private Job _job;
    private bool _charged, _horizontal;
    private BossEdgeVolley.Edge? _edge;
    private bool _signature;
    private Vector2 _target, _origin, _direction, _bossDestination;
    private double _clock, _warning, _elapsed, _cuePulse;
    private int _wave;
    private int _tier;
    private readonly List<Vector2> _passages = new();
    private int _passed;
    private float _moveSpeed, _pitch, _minimumMargin, _laneSpeed, _clearanceTime;
    private float _akariDistance, _akariMargin;
    public bool Finished { get; private set; }
    internal int Pattern { get; private set; }
    internal Vector2 Passage { get; private set; }
    internal double Warning => _warning;
    internal int Wave => _wave;
    internal int RouteBeat => Math.Min(_passed, Waves - 1);
    internal Vector2 DodgeTarget => _passages[RouteBeat];
    internal Vector2 GapForWave(int wave) => _passages[wave];
    internal bool GuidesVisible => GameManager.ShowAttackTelegraphs && !Finished && !Hud.BubblePaused;
    private bool AkariPressure => _job == Job.Melee && _tier > 0;
    private int Geometry => Pattern % 3;
    internal static int PatternCount(Enemy boss) => boss.BodyShotJob == Job.Melee ? 5 : 3;
    private int Waves => _tier == 0 ? (_charged ? 4 : 3) : _edge.HasValue ? (_charged ? 8 : 6)
        : AkariPressure ? (_charged ? 14 : 10) : (_charged ? 11 : 8);
    private int VisualPattern => _edge.HasValue ? 1 : Geometry;
    private float PassageRadius => (_tier == 0 ? 8f : AkariPressure ? (_tier == 1 ? 6f : 5f)
        : _tier == 1 ? 8f : 6f) - (_charged ? 1 : 0);
    private float Margin => Mathf.Max(_minimumMargin,
        PassageRadius + (_job == Job.Magic ? 24 : _job == Job.Heal && _tier > 0 ? 10 : 7));

    public static BossNormalBarrage Begin(Enemy boss, int pattern)
    {
        int count = GameManager.Instance!.Difficulty == GameManager.Diff.Easy ? 3 : PatternCount(boss);
        var attack = new BossNormalBarrage { _boss = boss, Pattern = pattern % count, TopLevel = true };
        boss.AddChild(attack);
        return attack;
    }

    internal static BossNormalBarrage BeginEdge(Enemy boss, BossEdgeVolley.Edge edge)
    {
        var attack = new BossNormalBarrage { _boss = boss, _edge = edge, TopLevel = true };
        boss.AddChild(attack);
        return attack;
    }

    internal static BossNormalBarrage BeginSignature(Enemy boss)
    {
        var attack = new BossNormalBarrage { _boss = boss, _signature = true, TopLevel = true,
            Pattern = boss.BodyShotJob == Job.Magic ? 0 : boss.BodyShotJob == Job.Heal ? 2 : 1 };
        boss.AddChild(attack);
        return attack;
    }

    public override void _Ready()
    {
        ZIndex = 2;
        ZAsRelative = false;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        _job = _boss.BodyShotJob!.Value;
        _tier = (int)GameManager.Instance!.Difficulty;
        _charged = _boss.HasEvolved || _signature;
        var player = (Player)GetTree().GetFirstNodeInGroup("player");
        _target = player.GlobalPosition;
        // Rei's fan needs the long axis to split before entering the protected target area.
        _horizontal = _edge.HasValue ? _edge is BossEdgeVolley.Edge.Left or BossEdgeVolley.Edge.Right
            : Geometry != 1 || _job == Job.Magic;
        _direction = _horizontal ? (_target.X < Field.CenterX ? Vector2.Left : Vector2.Right)
            : (_target.Y < Field.CenterY ? Vector2.Up : Vector2.Down);
        if (_edge.HasValue) _direction = _edge switch
        {
            BossEdgeVolley.Edge.Top => Vector2.Down, BossEdgeVolley.Edge.Bottom => Vector2.Up,
            BossEdgeVolley.Edge.Left => Vector2.Right, _ => Vector2.Left,
        };
        _origin = _horizontal ? new Vector2(_direction.X < 0 ? Field.Right - 12 : Field.Left + 12, _target.Y)
            : new Vector2(_target.X, _direction.Y < 0 ? Field.Bottom - 12 : Field.Top + 12);
        if (_edge.HasValue) _origin = _horizontal
            ? new Vector2(_direction.X < 0 ? Field.Right - 1 : Field.Left + 1, _target.Y)
            : new Vector2(_target.X, _direction.Y < 0 ? Field.Bottom - 1 : Field.Top + 1);
        if (Geometry == 2)
            _origin = new Vector2(_direction.X < 0 ? Field.Right - 64 : Field.Left + 64, Mathf.Clamp(_target.Y, 56, Field.Bottom - 56));
        float cross = _horizontal ? _target.Y : _target.X;
        float middle = _horizontal ? Field.CenterY : Field.CenterX;
        float offset = (_job == Job.Magic ? 72 : 48) * (cross < middle ? 1 : -1);
        Passage = _target + (_horizontal ? Vector2.Down : Vector2.Right) * offset;
        Passage = Passage.Clamp(new Vector2(Field.Left + 24, 24), new Vector2(Field.Right - 24, Field.Bottom - 24));
        _moveSpeed = player.SlowestMoveSpeed;
        var art = BulletArt.BossPattern(_job, VisualPattern);
        float radius = (_charged ? BulletPool.BossChargeRadius : BulletPool.BossRadius) * (_job == Job.Magic ? 1.5f : 1);
        var footprint = BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position, Bullet.BossArtSize(radius, _charged));
        _pitch = footprint.Bounds.Size.Y + Player.HitRadius * 2 + (_tier < 2 ? 2 : 1);
        if (_tier > 0) _pitch /= 1.5f;
        _akariMargin = Mathf.Max(Mathf.Abs(footprint.Bounds.Position.Y), Mathf.Abs(footprint.Bounds.End.Y))
            + Player.HitRadius + PassageRadius;
        if (_tier > 0) _minimumMargin = footprint.Radius + Player.HitRadius + PassageRadius + (_job == Job.Heal ? 4 : 0);
        _passages.Add(Passage);
        float step = _job == Job.Magic ? 48 : AkariPressure ? 24 : 32;
        int turn = offset > 0 ? 1 : -1;
        int stepsSinceTurn = 0;
        bool Inside(Vector2 point) => point.X >= Field.Left + 24 && point.X <= Field.Right - 24
            && point.Y >= Field.Top + 24 && point.Y <= Field.Bottom - 24;
        for (int wave = 1; wave < Waves; wave++)
        {
            Vector2 previous = _passages[^1];
            if (_tier == 0) { _passages.Add(previous); continue; }
            if (_signature && wave > 1 || Pattern == 3 && stepsSinceTurn == 3)
            {
                turn = -turn;
                stepsSinceTurn = 0;
            }
            float stride = step * (_signature && wave % 2 == 1 ? 1.5f : 1);
            Vector2 Next(int sign, float portion = 1) => Geometry == 0 || AkariPressure ? previous + (_horizontal ? Vector2.Down : Vector2.Right) * (sign * stride * portion)
                : _origin + (previous - _origin).Rotated(sign * stride * portion / previous.DistanceTo(_origin));
            if (wave == 1 && Geometry != 0)
            {
                var center = new Vector2(Field.CenterX, Field.CenterY);
                turn = Next(1).DistanceSquaredTo(center) < Next(-1).DistanceSquaredTo(center) ? 1 : -1;
            }
            Vector2 candidate = Next(turn);
            if (!Inside(candidate))
            {
                float low = 0, high = 1;
                for (int i = 0; i < 12; i++)
                {
                    float fraction = (low + high) * 0.5f;
                    if (Inside(Next(turn, fraction))) low = fraction; else high = fraction;
                }
                candidate = Next(turn, low);
                turn = -turn;
                stepsSinceTurn = 0;
                if (candidate.DistanceTo(previous) < (_job == Job.Magic ? 34 : 18)) candidate = Next(turn);
            }
            _passages.Add(candidate);
            stepsSinceTurn++;
        }
        if (AkariPressure)
        {
            _akariDistance = 220;
            foreach (var gap in _passages)
            foreach (float angle in AkariAngles())
            {
                Vector2 backwards = -_direction.Rotated(angle);
                _akariDistance = Mathf.Min(_akariDistance, gap.DistanceTo(WarningEnd(gap, backwards)) - 10);
            }
        }
        _bossDestination = new Vector2(_target.X < Field.CenterX ? Field.Right - 46 : Field.Left + 46,
            _target.Y < Field.CenterY ? Field.Bottom - 44 : 44);
        if (_tier > 0)
        {
            var positions = new List<Vector2>();
            for (float x = Field.Left + 32; x <= Field.Right - 32; x += 16)
                for (float y = Field.Top + 32; y <= Field.Bottom - 32; y += 16)
                {
                    var position = new Vector2(x, y);
                    if (position.DistanceTo(_target) >= 64 && _passages.All(gap => position.DistanceTo(gap) >= 64))
                        positions.Add(position);
                }
            _bossDestination = positions.OrderBy(position => position.DistanceSquaredTo(_boss.GlobalPosition)).First();
        }
        _warning = Math.Max(1.15, _target.DistanceTo(Passage) / player.SlowestMoveSpeed + 0.45);
        if (_tier > 0) _warning = Math.Max(_warning, _boss.GlobalPosition.DistanceTo(_bossDestination) / 90 + 0.3);
        _clock = _warning;
        if (_tier > 0)
        {
            float speed = (_job switch
            {
                Job.Melee => _charged ? 100 : 88, Job.Heal => _charged ? 72 : 60,
                Job.Magic => _charged ? 72 : 62, _ => _charged ? 100 : 82,
            }) * GameManager.Instance.BulletSpeedMul;
            _laneSpeed = speed;
            _clearanceTime = (footprint.Radius + Player.HitRadius + PassageRadius
                + (_job == Job.Magic ? 18 : _job == Job.Heal ? 6 : 0)) / (speed * (_job == Job.Magic ? 0.81f : 1));
            float distance = Geometry == 0 ? Math.Abs((_target - _origin).Dot(_direction))
                : Math.Min(_target.DistanceTo(_origin), Passage.DistanceTo(_origin)) - (Geometry == 2 ? (_charged ? 76 : 68) : 10);
            double approach = Math.Max(0, distance - footprint.Radius - Player.HitRadius - 4) / speed;
            if (_job == Job.Melee) approach += _charged ? 1.2 : 1.05;
            // Flight and charging already give reaction time; do not add the full warning again.
            _clock = Math.Max(_tier == 1 ? 0.55 : 0.45, _warning - approach - 0.12);
            if (AkariPressure && _edge.HasValue && _akariDistance < 64) _clock = _warning;
        }
        string[] names = _job switch
        {
            Job.Melee => new[] { "重なる一斉送信", "ふたつの焦がれる扇火", "連なる火花の花冠", "切り返す紅蓮の織目", "追いかける返信の針路" },
            Job.Heal => new[] { "約束の並木", "振り向く祈り", "想いの花冠" },
            Job.Magic => new[] { "言葉の折り目", "引用の扇", "反響の万華鏡" },
            _ => new[] { "記憶の回廊", "星屑の扇", "未送信の花冠" },
        };
        if (!_edge.HasValue && !_signature) _boss.AnnounceTechnique(names[Pattern], duration: 1.25);
    }

    private Vector2 WaveTarget(int wave)
    {
        if (_tier == 0 || wave == 0) return _target;
        Vector2 target = _passages[wave - 1];
        if (_job is Job.Magic or Job.Tank)
            target += new Vector2(1.5f, 1.5f);
        return target;
    }

    private IEnumerable<float> AkariAngles()
    {
        float spread = Pattern switch { 0 => 0.14f, 1 => 0.24f, 2 => 0.32f, 3 => 0.5f, _ => 0.42f };
        yield return -spread;
        yield return spread;
        if (_charged) yield return 0;
    }

    private IEnumerable<(Vector2 Position, Vector2 Direction)> AkariVolley(int wave)
    {
        Vector2 gap = _passages[wave];
        Vector2 target = WaveTarget(wave);
        foreach (float angle in AkariAngles())
        {
            Vector2 forward = _direction.Rotated(angle);
            Vector2 cross = forward.Orthogonal();
            Vector2 origin = gap - forward * _akariDistance;
            if (Geometry == 0)
            {
                float aimedLane = (target - gap).Dot(cross);
                Vector2 aimedPosition = origin + cross * aimedLane;
                if (Mathf.Abs(aimedLane) >= _akariMargin && Field.Rect.Grow(-2).HasPoint(aimedPosition))
                    yield return (aimedPosition, forward);
                for (int side = -1; side <= 1; side += 2)
                for (float lane = _akariMargin; lane < Field.Width + Field.Height; lane += _pitch)
                {
                    Vector2 position = origin + cross * (side * lane);
                    if (Field.Rect.Grow(-2).HasPoint(position)) yield return (position, forward);
                }
            }
            else
            {
                Vector2 aimed = origin.DirectionTo(target);
                Vector2 nearest = origin + aimed * Mathf.Max(0, (gap - origin).Dot(aimed));
                Vector2 aimedPosition = origin + aimed * 8;
                if (nearest.DistanceTo(gap) >= _akariMargin && Field.Rect.Grow(-2).HasPoint(aimedPosition))
                    yield return (aimedPosition, aimed);
                float first = Mathf.Asin(_akariMargin / _akariDistance);
                float pitch = 2 * Mathf.Asin(_pitch / (2 * _akariDistance));
                float spread = Geometry == 2 ? Mathf.Pi : Pattern == 4 ? 0.7f : 1.15f;
                for (int side = -1; side <= 1; side += 2)
                for (float ray = first; ray < spread; ray += pitch)
                {
                    Vector2 direction = forward.Rotated(side * ray);
                    Vector2 position = origin + direction * 8;
                    if (Field.Rect.Grow(-2).HasPoint(position)) yield return (position, direction);
                }
            }
        }
    }

    internal IEnumerable<(Vector2 Position, Vector2 Direction)> Volley()
    {
        int wave = Math.Min(_wave, Waves - 1);
        Vector2 passage = _passages[wave];
        Vector2 target = WaveTarget(wave);
        if (AkariPressure)
        {
            foreach (var shot in AkariVolley(wave)) yield return shot;
            yield break;
        }
        if (Geometry == 0)
        {
            float inset = _tier == 0 ? 8 : 2;
            float low = _horizontal ? Field.Top + inset : Field.Left + inset;
            float high = _horizontal ? Field.Bottom - inset : Field.Right - inset;
            float anchor = _horizontal ? target.Y : target.X;
            if (_tier > 0) anchor = Mathf.Clamp(anchor, low, high);
            float gap = _horizontal ? passage.Y : passage.X;
            if (_job == Job.Magic)
            {
                float spacing = _tier == 0 ? 44 : 44 / 1.5f;
                for (int side = -1; side <= 1; side += 2)
                    for (int row = 0; ; row++)
                    {
                        float offset = _tier == 0 || wave == 0
                            ? row == 0 ? Margin : Math.Abs(anchor - gap) + (row - 1) * spacing
                            : Math.Max(Margin, Math.Abs(anchor - gap)) + row * spacing;
                        float lane = gap + side * offset;
                        Vector2 position = _horizontal ? new Vector2(_origin.X, lane) : new Vector2(lane, _origin.Y);
                        if (!Field.Rect.HasPoint(position)) break;
                        yield return (position, _direction);
                    }
                yield break;
            }
            float pitch = _tier == 0 ? 16f : _pitch;
            for (int i = Mathf.CeilToInt((low - anchor) / pitch); i <= Mathf.FloorToInt((high - anchor) / pitch); i++)
            {
                float lane = anchor + i * pitch;
                if (Math.Abs(lane - gap) < Margin) continue;
                yield return (_horizontal ? new Vector2(_origin.X, lane) : new Vector2(lane, _origin.Y), _direction);
            }
            yield break;
        }
        int density = Math.Max(1, _tier);
        int count = _job == Job.Magic ? (Geometry == 1 ? 5 + density * 2 : 10 + density * 2)
            : Geometry == 1 ? 15 + density * 4 : 24 + density * 6;
        if (_tier > 0) count = Mathf.RoundToInt(count * 1.5f) | (_job == Job.Magic && Geometry == 1 ? 1 : 0);
        float spread = Geometry == 1 ? Mathf.DegToRad(120) : Mathf.Tau;
        if (_tier > 0 && _job != Job.Magic)
            count = Math.Max(count, Mathf.CeilToInt(spread * passage.DistanceTo(_origin) / _pitch) | 1);
        float aim = _origin.DirectionTo(target).Angle();
        for (int i = 0; i < count; i++)
        {
            float angle = Geometry == 1 ? aim + spread * ((float)i / (count - 1) - 0.5f) : aim + spread * i / count;
            Vector2 direction = Vector2.FromAngle(angle);
            Vector2 nearest = _origin + direction * Math.Max(0, (passage - _origin).Dot(direction));
            if (nearest.DistanceTo(passage) < Margin) continue;
            Vector2 position = _origin + direction * (Geometry == 2 ? (_charged ? 76 : 68) : 10);
            if (!Field.Rect.Grow(-6).HasPoint(position)) continue;
            yield return (position, direction);
        }
    }

    internal void Tick(double delta)
    {
        if (Finished || Hud.BubblePaused) return;
        if (_boss.IsPurified || _boss.Transforming || !_boss.IsVisibleInTree() || _boss.GaugeVulnerable || _boss.GaugeReforming)
        {
            Cancel();
            return;
        }
        _elapsed += delta;
        _clock -= delta;
        _cuePulse = Math.Max(0, _cuePulse - delta);
        var player = (Player)GetTree().GetFirstNodeInGroup("player");
        Vector2 next = _boss.GlobalPosition.MoveToward(_bossDestination, (float)delta * (_tier > 0 && _elapsed < _warning ? 90 : 18));
        float separation = next.DistanceTo(player.GlobalPosition);
        if (separation >= (_tier == 0 ? 88 : 36) || separation > _boss.GlobalPosition.DistanceTo(player.GlobalPosition)) _boss.GlobalPosition = next;
        // Include split descendants: changing the cue early can send the player into the trailing fragments.
        while (_passed < _wave && GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>()
            .Where(b => b.Active && b.BossBarrageId == GetInstanceId()
                && (_job == Job.Magic ? b.BossBarrageWave <= _passed : b.BossBarrageWave == _passed)).All(PastCurrentGap))
        {
            _passed++;
            if (_tier > 0) _cuePulse = 0.6;
        }
        if (_wave < Waves && _clock <= 0)
        {
            var pool = GetNode<BulletPool>("/root/Pool");
            float chargeSpeed = 0, rampSeconds = 0;
            List<(Bullet Bullet, Vector2 Origin)>? splitShots = _job == Job.Magic && _tier > 0 && _wave + 1 < Waves ? new() : null;
            foreach (var shot in Volley())
            {
                var bullet = pool.SpawnBossShot(_boss.ShotCenter, shot.Direction, _job, _charged,
                    Geometry == 2 ? BossShotStyle.Ring : BossShotStyle.Aimed);
                if (AkariPressure) { chargeSpeed = bullet.Velocity.Length(); rampSeconds = bullet.AccelRampSeconds; }
                bullet.UseBossPatternVisual(_job, VisualPattern);
                bullet.BossBarrageWave = _wave;
                bullet.BossBarrageId = GetInstanceId();
                Vector2 target = WaveTarget(_wave);
                bullet.BossAimTarget = target;
                if (bullet.Homing)
                {
                    bullet.FormationHomingAnchor = target;
                    Vector2 offset = shot.Position - target;
                    bullet.FormationHomingOffset = offset - shot.Direction * offset.Dot(shot.Direction);
                    bullet.Velocity = bullet.Velocity.Rotated((Geometry == 2 ? 1 : -1) * 0.1f);
                }
                bullet.DeployBossFormation(shot.Position, edgeLaunch: true, warnedLaunch: _edge.HasValue);
                splitShots?.Add((bullet, shot.Position));
            }
            _wave++;
            float distance = _wave < Waves ? _passages[_wave - 1].DistanceTo(_passages[_wave]) : 0;
            float travel = distance / _moveSpeed;
            float crossing = _clearanceTime;
            if (AkariPressure)
            {
                float FlightTime(float flightDistance)
                {
                    if (flightDistance <= chargeSpeed * 0.25f) return Mathf.Max(0, flightDistance) / chargeSpeed;
                    flightDistance -= chargeSpeed * 0.25f;
                    float rampDistance = (chargeSpeed + (_laneSpeed - chargeSpeed) / 3) * rampSeconds;
                    if (flightDistance >= rampDistance) return 0.25f + rampSeconds + (flightDistance - rampDistance) / _laneSpeed;
                    float low = 0, high = rampSeconds;
                    for (int i = 0; i < 12; i++)
                    {
                        float time = (low + high) * 0.5f;
                        float travelled = chargeSpeed * time + (_laneSpeed - chargeSpeed) * time * time * time / (3 * rampSeconds * rampSeconds);
                        if (travelled < flightDistance) low = time; else high = time;
                    }
                    return 0.25f + high;
                }
                float clearance = _clearanceTime * _laneSpeed
                    + distance * Mathf.Sin(AkariAngles().Max(angle => Mathf.Abs(angle)));
                float approach = _akariDistance - (Geometry == 0 ? 0 : 8);
                crossing = FlightTime(approach + clearance) - FlightTime(approach - clearance);
            }
            _clock = _tier == 0 ? (_job == Job.Melee ? 0.8 : 0.7)
                : Math.Max(AkariPressure ? (_edge.HasValue && _akariDistance < 64 ? 1.9 : _tier == 1 ? 1.55 : 1.4)
                    : _job == Job.Magic ? 2.2 : 1.6, travel + (_tier == 1 ? 0.4 : 0.3) + crossing + 0.12);
            if (splitShots != null) _clock = Math.Max(_clock, SplitWaveInterval(splitShots, travel));
        }
        // The next windup starts while the last wave exits; its live bullets keep their own sequence ID.
        if (_wave == Waves && (_tier > 0 ? _passed == Waves
            : !GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Any(b => b.Active))) Cancel();
        QueueRedraw();
    }

    private float SplitWaveInterval(List<(Bullet Bullet, Vector2 Origin)> fresh, float travel)
    {
        Vector2 from = _passages[_wave - 1], to = _passages[_wave];
        bool Crosses(Vector2 position, Vector2 forward, float clearance, out float near, out float far)
        {
            Vector2 a = from - position, b = to - position;
            near = Mathf.Min(a.Dot(forward), b.Dot(forward)) - clearance;
            far = Mathf.Max(a.Dot(forward), b.Dot(forward)) + clearance;
            return far > 0 && Mathf.Min(a.Cross(forward), b.Cross(forward)) <= clearance
                && Mathf.Max(a.Cross(forward), b.Cross(forward)) >= -clearance;
        }
        float ExitAfter(Bullet bullet, Vector2 position, bool newShot)
        {
            float offset = bullet.PendingFormationSplitOffset;
            float clearance = bullet.CollisionRadius + Player.HitRadius + PassageRadius + offset
                + (!newShot && bullet.LeadIn ? 12 : 0);
            if (!Crosses(position, bullet.Velocity.Normalized(), clearance, out _, out float distance)) return 0;
            float speed = bullet.Velocity.Length();
            int splits = offset > 6 ? 2 : offset > 0 ? 1 : 0;
            float time = newShot || bullet.LeadIn ? 0.32f : 0;
            if (!newShot) return time + distance / (speed * Mathf.Pow(0.9f, splits)) + splits * 0.12f;
            for (int split = 0; split < 2; split++)
            {
                float leg = Mathf.Min(distance, split == 0 ? 42 : 32);
                time += leg / speed;
                distance -= leg;
                if (distance <= 0) return time;
                time += 0.12f;
                speed *= 0.9f;
            }
            return time + distance / speed;
        }
        float lastExit = 0.32f;
        foreach (var shot in fresh) lastExit = Mathf.Max(lastExit, ExitAfter(shot.Bullet, shot.Origin, true));
        foreach (var bullet in GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>()
            .Where(b => b.Active && b.BossBarrageId == GetInstanceId() && b.BossBarrageWave < _wave - 1))
            lastExit = Mathf.Max(lastExit, ExitAfter(bullet, bullet.GlobalPosition, false));
        float firstEntry = float.PositiveInfinity;
        foreach (var shot in Volley())
            if (Crosses(shot.Position, shot.Direction, _minimumMargin + 18, out float distance, out _))
                firstEntry = Mathf.Min(firstEntry, 0.12f + Mathf.Max(0, distance) / _laneSpeed);
        return lastExit + travel + (_tier == 1 ? 0.4f : 0.3f) + 0.12f - firstEntry;
    }

    private bool PastCurrentGap(Bullet bullet)
    {
        if (bullet.LeadIn) return false;
        Vector2 gap = _passages[_passed];
        float clearance = (bullet.Footprint?.Radius ?? bullet.Radius) + Player.HitRadius + PassageRadius
            + (_job == Job.Magic ? bullet.PendingFormationSplitOffset : bullet.Homing ? 6f : 0);
        if (_passed + 1 < Waves)
        {
            Vector2 forward = bullet.Velocity.Normalized();
            Vector2 from = gap - bullet.GlobalPosition;
            Vector2 to = _passages[_passed + 1] - bullet.GlobalPosition;
            float sideFrom = from.Cross(forward), sideTo = to.Cross(forward);
            if (Mathf.Min(sideFrom, sideTo) > clearance || Mathf.Max(sideFrom, sideTo) < -clearance) return true;
            return Mathf.Max(from.Dot(forward), to.Dot(forward)) < -clearance;
        }
        if (AkariPressure) return (bullet.GlobalPosition - gap).Dot(bullet.Velocity.Normalized()) > clearance;
        return Geometry == 0 ? (bullet.GlobalPosition - gap).Dot(_direction) > clearance
            : bullet.GlobalPosition.DistanceTo(_origin) > gap.DistanceTo(_origin) + clearance;
    }

    public void Cancel()
    {
        if (Finished) return;
        Finished = true;
        Hide();
        QueueFree();
    }

    internal static Vector2 WarningEnd(Vector2 position, Vector2 direction)
    {
        float distance = Field.Width + Field.Height;
        if (direction.X > 0.001f) distance = Math.Min(distance, (Field.Right - position.X) / direction.X);
        if (direction.X < -0.001f) distance = Math.Min(distance, (Field.Left - position.X) / direction.X);
        if (direction.Y > 0.001f) distance = Math.Min(distance, (Field.Bottom - position.Y) / direction.Y);
        if (direction.Y < -0.001f) distance = Math.Min(distance, (Field.Top - position.Y) / direction.Y);
        return position + direction * distance;
    }

    public override void _Draw()
    {
        if (!GuidesVisible) return;
        Color ink = Bullet.BossColor(GameManager.Instance!.SelectedJob, Geometry == 2 ? BossShotStyle.Ring : BossShotStyle.Aimed);
        float alpha = _wave == 0 ? 0.75f : _tier == 0 ? 0.24f : 0.42f + (float)(_cuePulse / 0.6) * 0.38f;
        Vector2 along = Geometry == 0 || AkariPressure ? _direction : _origin.DirectionTo(DodgeTarget);
        Vector2 cross = along.Orthogonal();
        var safe = new Color("9df3cc");
        if (_tier > 0 && RouteBeat + 1 < Waves)
        {
            Vector2 next = _passages[RouteBeat + 1];
            DrawDashedLine(DodgeTarget, next, new Color(safe, 0.28f), 0.6f, 3, true);
            DrawArc(next, 4, 0, Mathf.Tau, 20, new Color(safe, 0.3f), 0.7f, true);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 edge = DodgeTarget + cross * PassageRadius * side;
            DrawLine(edge - along * 9, edge + along * 9, new Color(safe, alpha), 1.1f, true);
            DrawLine(edge - along * 9, edge - along * 9 - cross * side * 4, new Color(safe, alpha), 1.1f, true);
            DrawLine(edge + along * 9, edge + along * 9 - cross * side * 4, new Color(safe, alpha), 1.1f, true);
        }
        float cueDuration = _tier == 1 ? 0.55f : 0.45f;
        if (_wave < Waves && (_wave == 0 || _clock <= cueDuration))
        {
            float charge = _wave == 0 ? Mathf.Clamp((float)(_elapsed / _warning), 0, 1)
                : 1f - Mathf.Clamp((float)(_clock / cueDuration), 0, 1);
            foreach (var shot in Volley())
            {
                DrawCircle(shot.Position, 2 + charge, new Color(ink, 0.7f));
                Vector2 end = WarningEnd(shot.Position, shot.Direction);
                DrawDashedLine(shot.Position, end, new Color(ink, 0.12f + charge * 0.14f), 0.6f, 5, true);
            }
            DrawArc(_origin, 7, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * charge, 28, ink, 1.3f, true);
        }
    }
}
