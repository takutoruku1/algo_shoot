using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BossCrossfire : Node2D
{
    private Enemy _boss = null!;
    private Job _job;
    private bool _charged;
    private int _tier, _wave, _waves, _count;
    private Vector2 _from, _target;
    private readonly List<Vector2> _origins = new();
    private double _time, _clock, _warning;
    private float _pitch, _flash, _pairSpacing;
    private int _burst;
    private bool _sweep, _horizontal, _weave, _pincer, _diagonal;
    private int _firstSide;
    private readonly List<Vector2> _moveFrom = new();
    private double _relocate, _relocateDuration;
    private Vector2 _bossDestination;
    public bool Finished { get; private set; }
    internal IReadOnlyList<Vector2> Origins => _origins;
    internal Vector2 Target => _target;
    internal int Wave => _wave;
    internal bool Sweep => _sweep;
    internal bool Weave => _weave;
    internal bool Pincer => _pincer;
    internal bool Diagonal => _diagonal;
    internal bool Horizontal => _horizontal;
    internal int Burst => _burst;
    private int PincerBeats => _tier == 3 && (_job is Job.Heal or Job.Tank) ? 3 : 2;
    private int PincerGroup => _wave / PincerBeats;
    private bool CrossPressure => _tier >= 2 && !_pincer && !_sweep;
    internal double WarningRemaining => _clock;
    internal bool WarningVisible => GameManager.ShowAttackTelegraphs && !Finished && !Hud.BubblePaused && !Interrupted
        && _time >= 0.2 && _wave < _waves && (_clock <= _warning || _relocate > 0);
    internal bool Recovering => _sweep && _relocate > 0 && _relocate > _relocateDuration - (_job switch
        { Job.Melee => 1.2, Job.Heal => _horizontal ? 2.4 : 4.0, Job.Magic => 4.0, _ => _horizontal ? 1.5 : 2.0 });

    public static BossCrossfire Begin(Enemy boss, bool sweep = false, bool horizontal = true, bool weave = false, bool pincer = false, bool diagonal = false)
    {
        var attack = new BossCrossfire { _boss = boss, _sweep = sweep, _horizontal = horizontal, _weave = weave, _pincer = pincer, _diagonal = diagonal, TopLevel = true };
        boss.AddChild(attack);
        return attack;
    }

    public override void _Ready()
    {
        GlobalPosition = Vector2.Zero;
        ZIndex = 2;
        ZAsRelative = false;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        _job = _boss.BodyShotJob!.Value;
        _charged = _boss.HasEvolved;
        _tier = (int)GameManager.Instance!.Difficulty;
        _from = _boss.ShotCenter;
        _warning = new[] { 1.55, 1.35, 1.15, 0.95 }[_tier];
        _waves = 2 + _tier + (_charged ? 1 : 0);
        _count = _job switch
        {
            Job.Magic => 1,
            Job.Heal => _tier == 0 ? 3 : 7,
            Job.Tank when _tier >= 1 => 7,
            Job.Melee when _tier >= 1 => 9,
            _ => 3 + 2 * Math.Min(_tier, 2),
        };
        var shape = BulletPool.BossHitShape(_job, _charged);
        _pairSpacing = shape.Bounds.Size.X + 2;
        _pitch = shape.Bounds.Size.Y + BulletPool.BossLaneGap + (_tier >= 1 ? (_tier == 3 ? 1f : 2f) : 6f);
        if (_tier >= 1 && _job == Job.Heal) _pitch += 1;
        if (_job == Job.Magic) _pitch = _tier == 0 ? 76 : 60;
        AcquireTarget();
        _firstSide = _target.X < Field.CenterX ? (_target.Y < Field.CenterY ? 3 : 2)
            : (_target.Y < Field.CenterY ? 0 : 1);
        if (!_sweep) _horizontal = Math.Abs(_from.X - _target.X) >= Math.Abs(_from.Y - _target.Y);
        _burst = _job is Job.Magic or Job.Tank ? 4 : 3;
        if (_tier >= 1)
        {
            _burst += Math.Max(0, _tier - 1) * (_weave ? 1 : 2);
            _waves = _burst * (_tier == 3 ? 4 : 3);
        }
        if (_sweep)
        {
            _burst = (_job == Job.Magic ? 3 : 4) + 2 * Math.Max(0, _tier - 1);
            _waves = _burst * (_tier == 3 ? 3 : 2);
            _count = _job == Job.Magic ? 3 : _horizontal ? 7 : 9;
            if (_job == Job.Magic) _pitch = 60;
            _warning = _tier >= 2 ? (_tier == 3 ? 0.65 : 0.85) : _horizontal ? 1.5 : 2.0;
            if (_job == Job.Heal && _tier >= 2) { _burst *= 2; _waves *= 2; }
        }
        if (_weave) _waves = _burst * 2;
        if (_pincer)
        {
            _waves = new[] { 6, 8, 10, 12 }[_tier];
            _burst = 2;
            _warning = new[] { 1.6, 1.15, 0.8, 0.65 }[_tier];
        }
        PlaceOrigins();
        _clock = _warning;
        string name = _job switch
        {
            Job.Melee => _pincer ? (_diagonal ? "交差する焦燥" : "逃がさない締切") : _sweep ? "駆け抜ける残業" : _weave ? "ほどけない焦り" : "一斉送信",
            Job.Heal => _pincer ? (_diagonal ? "すれ違う祈り" : "どこにいても見つける") : _sweep ? "視線のリレー" : _weave ? "結び直す約束" : "届いて、全部",
            Job.Magic => _pincer ? (_diagonal ? "交差する引用" : "増幅する反響") : _sweep ? "拡散する言葉" : _weave ? "編み込まれた噂" : "万華のリポスト",
            _ => _pincer ? (_diagonal ? "交差する記憶" : "消せない応答") : _sweep ? "記憶の往復" : _weave ? "つなぎ直す記録" : "未送信の星列",
        };
        _boss.AnnounceTechnique(name);
    }

    private void PlaceOrigins()
    {
        _origins.Clear();
        if (_pincer)
        {
            for (int source = 0; source < (_tier == 0 ? 1 : _tier == 3 ? 3 : 2); source++)
            {
                int side = (_firstSide + PincerGroup + source) % 4;
                _origins.Add(side switch
                {
                    0 => new Vector2(_target.X, Field.Top + 18),
                    1 => new Vector2(Field.Right - 18, _target.Y),
                    2 => new Vector2(_target.X, Field.Bottom - 18),
                    _ => new Vector2(Field.Left + 18, _target.Y),
                });
            }
            return;
        }
        if (_sweep)
        {
            float center = _horizontal ? _target.Y : _target.X;
            float low = _horizontal ? Field.Top : Field.Left;
            float high = _horizontal ? Field.Bottom : Field.Right;
            center = Mathf.Clamp(center, low + 24, high - 24);
            _origins.Add(_horizontal ? new Vector2(Field.Left + 18, center) : new Vector2(center, Field.Top + 18));
            _origins.Add(_horizontal ? new Vector2(Field.Right - 18, center) : new Vector2(center, Field.Bottom - 18));
            return;
        }
        float edge = _horizontal
            ? (_target.X < Field.CenterX ? Field.Right - 24 : Field.Left + 24)
            : (_target.Y < Field.CenterY ? Field.Bottom - 24 : Field.Top + 24);
        float span = _job switch
        {
            Job.Melee => _tier >= 1 ? (_charged ? 40 : 54) : 64,
            Job.Heal => _tier >= 1 ? (_charged ? 40 : 56) : 76,
            Job.Magic => 58,
            _ => 72,
        };
        int sources = _job == Job.Tank && _tier >= 1 ? 3 : 2;
        for (int i = 0; i < sources; i++)
        {
            float offset = Mathf.Lerp(-span, span, (float)i / (sources - 1));
            float stagger = _job == Job.Magic && i == 1 ? 24 : _job == Job.Tank && sources == 3 && i == 1 ? 44 : 0;
            if (_tier >= 1) stagger = 0;
            _origins.Add(_horizontal
                ? new Vector2(edge + (_target.X < Field.CenterX ? -stagger : stagger), Field.CenterY + offset)
                : new Vector2(Field.CenterX + offset, edge + (_target.Y < Field.CenterY ? -stagger : stagger)));
        }
        if (CrossPressure) _origins.Add(_horizontal
            ? new Vector2(_target.X, _target.Y < Field.CenterY ? Field.Top + 18 : Field.Bottom - 18)
            : new Vector2(_target.X < Field.CenterX ? Field.Left + 18 : Field.Right - 18, _target.Y));
    }

    private void AcquireTarget() => _target = (GetTree().GetFirstNodeInGroup("player") as Node2D)?.GlobalPosition ?? Field.PlayerStart;

    private bool Interrupted => _boss.IsPurified || _boss.Transforming || !_boss.IsVisibleInTree()
        || _boss.GaugeVulnerable || _boss.GaugeReforming || _boss is BossMina { Transitioning: true };

    public override void _Process(double delta)
    {
        // Enemy can return before its attack clock during a break or transformation.
        if (!Finished && Interrupted) Cancel();
        QueueRedraw();
    }

    internal void Tick(double delta)
    {
        if (Finished) return;
        if (Interrupted) { Cancel(); return; }
        if (Hud.BubblePaused) return;
        _time += delta;
        _clock -= delta;
        if (_relocate > 0)
        {
            _relocate = Math.Max(0, _relocate - delta);
            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            Vector2 next = _boss.GlobalPosition.MoveToward(_bossDestination, (float)delta * 24);
            if (player == null || next.DistanceTo(player.GlobalPosition) >= 88) _boss.GlobalPosition = next;
        }
        _flash = Math.Max(0, _flash - (float)delta * 4);
        if (_clock > 0) { QueueRedraw(); return; }
        if (_wave >= _waves) { Cancel(); return; }
        Fire();
        _wave++;
        _flash = 1;
        if (_pincer)
        {
            _clock = (_job == Job.Magic ? new[] { 1.5, 1.2, 0.95, 0.8 } : new[] { 1.3, 1.05, 0.7, 0.55 })[_tier];
            if (_job == Job.Tank && _tier == 3) _clock = 0.6;
            if (_wave >= _waves) { _clock = 0.05; QueueRedraw(); return; }
            _moveFrom.Clear();
            _moveFrom.AddRange(_origins);
            // The paired lanes share one aim; re-aim only when the emitters change sides.
            if (_wave % PincerBeats == 0)
            {
                if (_job != Job.Tank || _tier != 3 || _wave % 6 == 0) AcquireTarget();
                _clock = Math.Max(_clock, AimWarning());
            }
            PlaceOrigins();
            _relocate = _relocateDuration = _clock;
            _bossDestination = new Vector2(_target.X < Field.CenterX ? Field.Right - 48 : Field.Left + 48,
                _target.Y < Field.CenterY ? Field.Bottom - 44 : Field.Top + 44);
            QueueRedraw();
            return;
        }
        _clock = _tier == 0
            ? (_job switch { Job.Melee => 1.65, Job.Heal => 1.45, Job.Magic => 1.6, _ => 1.25 }) * new[] { 1.2, 1.1 }[_tier]
            : (_job switch { Job.Melee => _charged ? 1.05 : 0.90, Job.Heal => 0.65, Job.Magic => 0.60, _ => 0.56 })
                * (_tier == 3 ? (_weave ? 0.62 : 0.4) : _tier == 2 ? (_weave ? 0.75 : 0.5) : 1);
        if (_sweep) _clock = (_job == Job.Magic ? 0.9 : _job == Job.Melee ? 1.05 : 0.7) * (_tier == 3 ? 0.68 : _tier == 2 ? 0.8 : 1);
        if (_sweep && _job == Job.Heal && _tier >= 2) _clock = _tier == 3 ? 0.32 : 0.36;
        if (_weave)
        {
            float speed = (GetTree().GetFirstNodeInGroup("player") as Player)?.SlowestMoveSpeed ?? 48;
            _clock = Math.Max(_clock, _pitch / speed + (_tier == 3 ? 0.1 : 0.2));
        }
        if (_wave >= _waves) { if (_tier >= 1) _clock = _tier >= 2 ? 0.05 : 0.8; QueueRedraw(); return; }
        if (_tier >= 1 && _wave % _burst == 0)
        {
            _moveFrom.Clear();
            _moveFrom.AddRange(_origins);
            AcquireTarget();
            if (!_sweep || _tier >= 2) _horizontal = !_horizontal;
            PlaceOrigins();
            _clock = _sweep ? TransferWarning() + (_job switch { Job.Melee => 1.2, Job.Heal => 1.0, Job.Magic => 1.5, _ => 0.5 })
                : _job switch { Job.Melee => 2.0, Job.Heal => 1.7, Job.Magic => 2.6, _ => _charged ? 2.7 : 2.25 };
            if (!_sweep) _clock = Math.Max(_clock, AimWarning());
            _relocate = _relocateDuration = _clock;
            _bossDestination = new Vector2(_target.X < Field.CenterX ? Field.Right - 48 : Field.Left + 48,
                _target.Y < Field.CenterY ? Field.Bottom - 44 : Field.Top + 44);
        }
        else if (!_sweep && !_weave && _tier == 0)
        {
            AcquireTarget();
        }
        QueueRedraw();
    }

    private double AimWarning()
    {
        float speed = (GetTree().GetFirstNodeInGroup("player") as Player)?.SlowestMoveSpeed ?? 48;
        return _pitch / speed + 0.35;
    }

    private double TransferWarning()
    {
        float speed = (GetTree().GetFirstNodeInGroup("player") as Player)?.SlowestMoveSpeed ?? 48;
        // Crossing time includes the equipped character's slow movement and a reaction beat.
        return (_horizontal ? Field.Height : Field.Width) * 0.64 / speed + 0.6;
    }

    private void Fire()
    {
        var pool = GetNode<BulletPool>("/root/Pool");
        for (int source = 0; source < _origins.Count; source++)
        {
            foreach (var shot in VolleyPlan(source))
            {
                var bullet = pool.SpawnBossShot(_origins[source], shot.Direction, _job, _charged);
                if (shot.Centered) bullet.BossAimTarget = _target;
                // Preserve the gaps while a whole homing row bends toward the player.
                if (bullet.Homing)
                {
                    Vector2 offset = shot.Position - _target;
                    bullet.FormationHomingOffset = offset - shot.Direction * offset.Dot(shot.Direction) + shot.PairOffset;
                }
                bullet.DeployBossFormation(shot.Position, edgeLaunch: _pincer || _sweep || _tier >= 1);
            }
        }
    }

    internal IEnumerable<(Vector2 Position, Vector2 Direction, bool Centered)> Volley(int source)
    {
        foreach (var shot in VolleyPlan(source))
            yield return (shot.Position, shot.Direction, shot.Centered);
    }

    private IEnumerable<(Vector2 Position, Vector2 Direction, bool Centered, Vector2 PairOffset)> VolleyPlan(int source)
    {
        int lane = source;
        var slots = BaseVolley(source).ToArray();
        var paired = _job is Job.Heal or Job.Tank ? slots.OrderByDescending(shot =>
            Math.Abs((shot.Position - _target).Cross(shot.Direction))).Take((slots.Length + 1) / 2)
            .Select(shot => shot.Position).ToHashSet() : null;
        foreach (var shot in slots)
        {
            yield return (shot.Position, shot.Direction, shot.Centered, Vector2.Zero);
            // Pair along the existing lane so the escape gaps and final trailing shot stay unchanged.
            if (_tier >= 1 && (paired?.Contains(shot.Position) ?? lane++ % 2 == 0))
            {
                Vector2 offset = shot.Direction * _pairSpacing;
                yield return (shot.Position + offset, shot.Direction, shot.Centered, offset);
            }
        }
    }

    private IEnumerable<(Vector2 Position, Vector2 Direction, bool Centered)> BaseVolley(int source)
    {
        Vector2 origin = _origins[source];
        if (!SourceActive(source, _wave)) yield break;
        if (CrossPressure && source == _origins.Count - 1)
        {
            int side = _horizontal ? (origin.Y < Field.CenterY ? 0 : 2) : (origin.X < Field.CenterX ? 3 : 1);
            foreach (var shot in EdgeRow(side, _target, 0, _job == Job.Heal && _tier == 3 ? 2 : 1)) yield return shot;
            yield break;
        }
        if (_pincer)
        {
            int side = (_firstSide + PincerGroup + source) % 4;
            float angle = _diagonal ? ((PincerGroup + source) % 2 == 0 ? 20 : -20) : 0;
            foreach (var shot in EdgeRow(side, _target, angle)) yield return shot;
            yield break;
        }
        if (!_sweep && _tier == 0)
        {
            Vector2 aim = origin.DirectionTo(_target);
            int half = HalfRack(origin, aim);
            for (int i = -half; i <= half; i++)
            {
                Vector2 position = origin + aim.Orthogonal() * (i * _pitch);
                if (!Field.Rect.Grow(-10).HasPoint(position) || position.DistanceTo(_target) < 64) continue;
                yield return (position, _job == Job.Heal ? aim.Rotated(Mathf.DegToRad(i * 9)) : aim, i == 0);
            }
            yield break;
        }
        Vector2 direction = _horizontal ? new Vector2(origin.X < Field.CenterX ? 1 : -1, 0)
            : new Vector2(0, origin.Y < Field.CenterY ? 1 : -1);
        foreach (var (slot, centered) in SweepSlots(origin))
        {
            if (!_sweep)
            {
                int owner = 0;
                for (int i = 1; i < _origins.Count - (CrossPressure ? 1 : 0); i++)
                    if (slot.DistanceSquaredTo(_origins[i]) < slot.DistanceSquaredTo(_origins[owner])) owner = i;
                if (owner != source) continue;
            }
            // Launch outside the playfield so approaching a gate cannot erase its warned lanes.
            Vector2 position = _horizontal
                ? new Vector2(direction.X > 0 ? Field.Left - 20 : Field.Right + 20, slot.Y)
                : new Vector2(slot.X, direction.Y > 0 ? Field.Top - 20 : Field.Bottom + 20);
            yield return (position, centered ? position.DirectionTo(_target) : direction, centered);
        }
    }

    private bool SourceActive(int source, int wave)
    {
        if (_pincer && _tier == 3 && _job == Job.Heal) return source != (wave % 2 == 0 ? 2 : 0);
        if (_sweep && _job == Job.Heal && _tier >= 2) return source == wave % 2;
        if (CrossPressure && source == _origins.Count - 1) return wave % (_tier == 3 ? 2 : 3) == (_tier == 3 ? 1 : 2);
        return true;
    }

    private IEnumerable<(Vector2 Position, Vector2 Direction, bool Centered)> EdgeRow(int side, Vector2 target, float angle, int stride = 1)
    {
        bool horizontal = side % 2 == 1;
        Vector2 direction = side switch { 0 => Vector2.Down, 1 => Vector2.Left, 2 => Vector2.Up, _ => Vector2.Right };
        float low = horizontal ? Field.Top : Field.Left;
        float high = horizontal ? Field.Bottom : Field.Right;
        float anchor = horizontal ? target.Y : target.X;
        Vector2 edge = side switch
        {
            0 => new Vector2(anchor, Field.Top - 20),
            1 => new Vector2(Field.Right + 20, anchor),
            2 => new Vector2(anchor, Field.Bottom + 20),
            _ => new Vector2(Field.Left - 20, anchor),
        };
        if (angle != 0)
        {
            direction = direction.Rotated(Mathf.DegToRad(angle));
            anchor -= horizontal ? (target.X - edge.X) * direction.Y / direction.X
                : (target.Y - edge.Y) * direction.X / direction.Y;
            anchor = Mathf.Clamp(anchor, low + 4, high - 4);
            if (horizontal) edge.Y = anchor; else edge.X = anchor;
            direction = edge.DirectionTo(target);
        }
        float pitch = _pitch * stride / Mathf.Abs(horizontal ? direction.X : direction.Y);
        for (int i = Mathf.CeilToInt((low - anchor) / pitch); i <= Mathf.FloorToInt((high - anchor) / pitch); i++)
        {
            Vector2 position = horizontal ? new Vector2(edge.X, anchor + i * pitch) : new Vector2(anchor + i * pitch, edge.Y);
            yield return (position, direction, i == 0);
        }
    }

    private IEnumerable<(Vector2 Position, bool Centered)> SweepSlots(Vector2 origin)
    {
        float low = _horizontal ? Field.Top : Field.Left;
        float high = _horizontal ? Field.Bottom : Field.Right;
        float anchor = Mathf.Clamp(_horizontal ? _target.Y : _target.X, low, high);
        float source = _horizontal ? origin.Y : origin.X;
        int reach = _tier >= 1 ? Mathf.CeilToInt((high - low) / _pitch) : _count / 2;
        float passage = Mathf.Lerp(low, high, anchor < (low + high) * 0.5f ? 0.82f : 0.18f);
        if (_weave) passage = WeavePassage;
        float passageHalfWidth = _pitch * 0.5f;
        for (int i = -reach; i <= reach; i++)
        {
            float offset = anchor - source + i * _pitch;
            if ((_weave || _sweep && _tier < 2 && i != 0) && Math.Abs(anchor + i * _pitch - passage) < passageHalfWidth) continue;
            Vector2 destination = origin + (_horizontal ? Vector2.Down : Vector2.Right) * offset;
            float cross = _horizontal ? destination.Y : destination.X;
            if (cross >= low && cross <= high) yield return (destination, i == 0);
        }
    }

    internal float WeavePassage
    {
        get
        {
            float low = _horizontal ? Field.Top : Field.Left;
            float high = _horizontal ? Field.Bottom : Field.Right;
            float anchor = _horizontal ? _target.Y : _target.X;
            int direction = anchor < (low + high) * 0.5f ? 1 : -1;
            int first = Mathf.CeilToInt((low - anchor) / _pitch);
            int last = Mathf.FloorToInt((high - anchor) / _pitch);
            int span = last - first;
            int phase = ((direction - first + direction * (_wave % _burst)) % (span * 2) + span * 2) % (span * 2);
            int step = first + Math.Min(phase, span * 2 - phase);
            return anchor + step * _pitch;
        }
    }

    private int HalfRack(Vector2 origin, Vector2 aim)
    {
        int half = 0;
        for (int i = 1; i <= _count / 2; i++)
        {
            Vector2 offset = aim.Orthogonal() * (i * _pitch);
            if (!Field.Rect.Grow(-10).HasPoint(origin + offset) || !Field.Rect.Grow(-10).HasPoint(origin - offset)) break;
            half = i;
        }
        return half;
    }

    public void Cancel()
    {
        if (Finished) return;
        Finished = true;
        Hide();
        QueueFree();
    }

    public override void _Draw()
    {
        if (Finished || Hud.BubblePaused || Interrupted) return;
        var ink = Bullet.BossColor(GameManager.Instance!.SelectedJob, BossShotStyle.Aimed);
        float deploy = Mathf.SmoothStep(0, 1, Mathf.Clamp((float)_time / 0.55f, 0, 1));
        for (int source = 0; source < _origins.Count; source++)
        {
            var origin = _origins[source];
            bool warned = WarningVisible && SourceActive(source, _wave);
            float flash = SourceActive(source, _wave - 1) ? _flash : 0;
            Vector2 position = _from.Lerp(origin, deploy);
            if (_relocate > 0) position = _moveFrom[source].Lerp(origin,
                Mathf.SmoothStep(0, 1, Mathf.Clamp((float)((_relocateDuration - _relocate)
                    / Math.Max(0.2, _relocateDuration - _warning)), 0, 1)));
            Vector2 aim = _sweep ? (_horizontal ? new Vector2(origin.X < Field.CenterX ? 1 : -1, 0)
                : new Vector2(0, origin.Y < Field.CenterY ? 1 : -1)) : position.DirectionTo(_target);
            Vector2 side = aim.Orthogonal();
            DrawLine(_from, position, new Color(ink, 0.16f * (1 - deploy)), 1, true);
            float radius = 7 + (_wave > 0 ? flash * 4 : 0);
            var diamond = new[] { position + aim * radius, position + side * radius,
                position - aim * radius, position - side * radius, position + aim * radius };
            DrawColoredPolygon(diamond[..4], new Color("111820"));
            DrawPolyline(diamond, new Color(ink, warned ? 0.85f : 0.3f + flash * 0.55f), 1, true);
            if (warned)
            {
                DrawLine(position - side * 3, position + aim * 4, ink, 1.2f, true);
                DrawLine(position + side * 3, position + aim * 4, ink, 1.2f, true);
                double warning = _relocate > 0 ? Math.Max(_warning, _relocateDuration) : _warning;
                float charge = 1 - Mathf.Clamp((float)(_clock / warning), 0, 1);
                foreach (var shot in Volley(source))
                {
                    Vector2 direction = shot.Direction;
                    Vector2 slot = shot.Position + direction * (_pincer || _sweep || _tier >= 1 ? 20 : 0);
                    Vector2 cross = direction.Orthogonal();
                    Vector2 end = GameManager.Instance!.DebugAttackGuides
                        ? BossNormalBarrage.WarningEnd(slot, direction) : slot + direction * 44;
                    DrawDashedLine(slot, end, new Color(ink, 0.24f + charge * 0.3f), 0.75f, 5, true);
                    DrawLine(slot + direction * 12 - cross * 2, slot + direction * 15, ink, 1, true);
                    DrawLine(slot + direction * 12 + cross * 2, slot + direction * 15, ink, 1, true);
                }
            }
        }
    }
}
