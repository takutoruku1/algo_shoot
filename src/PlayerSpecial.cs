using Godot;
using System.Collections.Generic;

public partial class PlayerSpecial : Node2D
{
    public const float Cooldown = 60f;
    internal const float MinaGuardDuration = 2f, MinaBeamDuration = 1.15f;
    internal const float MinaShieldOffset = 20f, MinaShieldHalfLength = 42f;
    internal const float AkariWindup = .16f, AkariDashDuration = .34f, AkariRecovery = 1.2f;
    internal const float AkariDashDistance = 138f, AkariSlashRadius = 17f;
    internal const float AkariCutRadius = 10f, AkariFinaleRadius = 88f, AkariFinaleWidth = 11f;
    internal const float KoharuDuration = 4f, KoharuRadius = 52f, KoharuBloomDuration = .9f;
    internal const float ReiHopInterval = .13f, ReiChainRange = 112f, ReiNovaRadius = 28f, ReiThreadRadius = 7f;
    internal const int ReiMaxTargets = 12;

    private Player _player = null!;
    private BulletPool _pool = null!;
    private CanvasModulate? _worldTint;
    private readonly HashSet<ulong> _hitTargets = new();
    private readonly HashSet<ulong> _strikeHits = new();
    private readonly HashSet<ulong> _chainVisited = new();
    private Dictionary<ulong, (ulong Activation, Vector2 Position)> _bulletPrevious = new();
    private Dictionary<ulong, (ulong Activation, Vector2 Position)> _bulletNext = new();
    private readonly List<(Vector2 Position, float Born)> _absorptions = new();
    private readonly List<(Vector2 From, Vector2 To, float Born)> _links = new();
    private readonly List<Vector2> _dashTrail = new();
    private Vector2 _dashEnd, _dashPrevious, _beamStart, _beamEnd, _chainFrom, _chainLastPosition;
    private Node2D? _chainTarget;
    private float _chainTimer, _finishAt;
    private bool _beamReleased, _dashReleased, _dashFinished, _healingResolved;
    private int _beamVolley = -1, _slashStage, _bloomCount;
    private float _bloomAt, _previousBloomRadius, _constellationAt = -1;
    private bool _constellationReleased;
    private Vector2 _slashFrom, _slashTo;
    private float _slashAt;

    public new Player Owner => _player;
    public Job Character { get; private set; }
    public bool Active { get; private set; }
    public float Elapsed { get; private set; }
    public Vector2 Origin { get; private set; }
    public Vector2 Direction { get; private set; }
    public int AbsorbedCount { get; private set; }
    public int HitCount => _hitTargets.Count;
    public int AttackStages { get; private set; }
    internal IReadOnlyCollection<ulong> HitTargets => _hitTargets;
    public bool Healed { get; private set; }
    public bool BlocksMovement => Active && Character == Job.Melee && Elapsed < AkariWindup + AkariDashDuration;
    public bool ProtectsPlayer => Active && Character == Job.Melee && Elapsed >= AkariWindup;
    public float Ratio => Active ? Mathf.Clamp(1f - Elapsed / DurationFor(Character), 0f, 1f) : 0f;

    public static string NameFor(Job character) => character switch
    {
        Job.Melee => "もう、取り消さない",
        Job.Heal => "ここにいていい",
        Job.Magic => "最後まで、聞いて",
        _ => "お返事、届けます",
    };

    public static string ShortDescriptionFor(Job character) => character switch
    {
        Job.Melee => "突進・交差斬りから巨大な三日月斬り",
        Job.Heal => "大きな花輪と浄化の波。中で♥全回復",
        Job.Magic => "最大12連鎖から、星座が一斉に炸裂",
        _ => "弾を吸収し、三段の極太光砲で返す",
    };

    public static string DescriptionFor(Job character) => character switch
    {
        Job.Melee => "入力方向へ突進し、二段の交差斬りから前方を巨大な三日月で薙ぐ。斬った範囲の弾を払い、攻撃中は無敵。",
        Job.Heal => "4秒間の大きな防弾の花輪を設置。4回の浄化の波が広がり、最後に大きく開花する。4秒後に中にいれば♥を全回復。",
        Job.Magic => "狙った敵から近くの敵と盾へ最大12対象を連鎖攻撃。結んだ星座を解放し、太い光の線と星の大爆発で追撃する。",
        _ => "正面の広い範囲で弾を2秒間吸収。三段の極太光砲を撃ち、最後に最大出力で貫く。吸収した数で威力と幅が増す。",
    };

    internal static float DurationFor(Job character) => character switch
    {
        Job.Melee => AkariWindup + AkariDashDuration + AkariRecovery,
        Job.Heal => KoharuDuration + KoharuBloomDuration + .1f,
        Job.Magic => ReiMaxTargets * ReiHopInterval + .95f,
        _ => MinaGuardDuration + MinaBeamDuration,
    };

    public static bool CanStart(Player player, Job character)
    {
        if (character != Job.Magic) return true;
        return FindFirstTarget(player) != null;
    }

    public static PlayerSpecial Spawn(Player player, Job character, Vector2 direction)
    {
        var special = new PlayerSpecial
        {
            Name = "PlayerSpecial", _player = player, Character = character,
            Direction = direction.Normalized(), Origin = player.GlobalPosition,
            ProcessPhysicsPriority = 90, ZIndex = 2, Active = true,
        };
        player.GetParent().AddChild(special);
        return special;
    }

    public override void _Ready()
    {
        _pool = GetNode<BulletPool>("/root/Pool");
        _worldTint = TintLift.Find(this);
        SelfModulate = TintLift.Of(_worldTint, TintLift.PlayerBody);
        _finishAt = DurationFor(Character);
        _dashEnd = Origin + Direction * AkariDashDistance;
        _dashPrevious = Origin;
        _dashTrail.Add(Origin);
        _chainFrom = Origin;
        if (Character == Job.Magic) SetChainTarget(FindFirstTarget(_player));
        foreach (var node in GetTree().GetNodesInGroup("enemy_bullets"))
            if (node is Bullet { Active: true, IsEnemy: true } bullet)
                _bulletPrevious[bullet.GetInstanceId()] = (bullet.ActivationId, bullet.GlobalPosition);
        Audio.Instance?.PlayChargeReady(Character);
        InitializeVisuals();
        BeginCutin();
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active) return;
        SelfModulate = TintLift.Of(_worldTint, TintLift.PlayerBody);
        if (!IsInstanceValid(_player) || _player.IsQueuedForDeletion() || _player.IsGameOver)
        {
            Cancel();
            return;
        }
        if (Hud.BubblePaused || !_player.CanProcess()) return;
        float dt = (float)delta;
        Elapsed += dt;
        switch (Character)
        {
            case Job.Tank: TickMina(); break;
            case Job.Melee: TickAkari(); break;
            case Job.Heal: TickKoharu(); break;
            case Job.Magic: TickRei(dt); break;
        }
        if (!Active) return;
        UpdateEnemyBullets();
        _absorptions.RemoveAll(p => Elapsed - p.Born > .4f);
        if (Elapsed >= _finishAt) { Cancel(); return; }
        SyncVisuals();
        QueueRedraw();
    }

    private void TickMina()
    {
        if (Elapsed < MinaGuardDuration) return;
        if (!_beamReleased)
        {
            _beamReleased = true;
            _beamStart = _player.GlobalPosition + Direction * MinaShieldOffset;
            _beamEnd = _beamStart + Direction * 480f;
        }
        int volley = Elapsed < MinaGuardDuration + .24f ? 0 : Elapsed < MinaGuardDuration + .52f ? 1 : 2;
        if (volley != _beamVolley)
        {
            _beamVolley = volley;
            AttackStages++;
            _strikeHits.Clear();
            Audio.Instance?.PlayChargeRelease(Character);
            GameCamera.Instance?.Shake(volley == 2 ? 4.5f : 2f, volley == 2 ? .28f : .16f);
        }
        int charge = Mathf.Min(_guardAbsorbed, 20);
        int damage = volley == 2 ? 120 + charge * 6 : 40 + charge * 2;
        StrikeSegment(_beamStart, _beamEnd, BeamRadius, damage, volley == 2 ? 40 + charge : 12 + charge / 2, _strikeHits);
    }

    private int _guardAbsorbed;
    private float BeamRadius => (18f + Mathf.Min(_guardAbsorbed, 20) * .8f) * (_beamVolley == 2 ? 1 : .6f);
    private float BeamVolleyAt => MinaGuardDuration + (_beamVolley == 2 ? .52f : _beamVolley == 1 ? .24f : 0);

    private void TickAkari()
    {
        if (Elapsed < AkariWindup) return;
        if (_dashFinished)
        {
            float age = Elapsed - AkariWindup - AkariDashDuration;
            int stage = age < .10f ? 0 : age < .30f ? 1 : age < .58f ? 2 : 3;
            if (stage > _slashStage)
            {
                _slashStage = stage;
                AttackStages++;
                _slashAt = Elapsed;
                _strikeHits.Clear();
                if (stage < 3) (_slashFrom, _slashTo) = AkariCutEndpoints(stage);
                Audio.Instance?.PlayChargeImpact(Character);
                GameCamera.Instance?.Shake(stage == 3 ? 4f : 2f, stage == 3 ? .24f : .13f);
            }
            if (_slashStage == 3 && Elapsed - _slashAt < .22f)
            {
                for (int i = 0; i < 32 && Active; i++)
                    StrikeSegment(AkariFinalePoint(i / 32f), AkariFinalePoint((i + 1) / 32f), AkariFinaleWidthAt((i + .5f) / 32), 150, 54, _strikeHits);
            }
            else if (_slashStage > 0 && _slashStage < 3 && Elapsed - _slashAt < .16f)
                StrikeSegment(_slashFrom, _slashTo, AkariCutRadius, 45, 12, _strikeHits);
            return;
        }
        if (!_dashReleased)
        {
            _dashReleased = true;
            AttackStages++;
            Audio.Instance?.PlayChargeRelease(Character);
        }
        float progress = Mathf.Clamp((Elapsed - AkariWindup) / AkariDashDuration, 0f, 1f);
        Vector2 desired = Origin.Lerp(_dashEnd, 1f - Mathf.Pow(1f - progress, 2f));
        Vector2 actual = _player.MoveForSpecial(desired);
        _dashPrevious = _dashTrail[^1];
        _dashTrail.Add(actual);
        StrikeSegment(_dashPrevious, actual, AkariSlashRadius, 65, 12);
        _dashFinished = progress >= 1f;
        if (_dashFinished) Pad.ReleaseMouseFollow();
    }

    private (Vector2 From, Vector2 To) AkariCutEndpoints(int stage)
    {
        Vector2 axis = Direction.Rotated(stage == 1 ? -.68f : .68f);
        Vector2 center = _dashTrail[^1] + Direction * 8;
        return (center - axis * 68, center + axis * 68);
    }

    private Vector2 AkariFinaleCenter => _dashTrail[^1] - Direction * 36;
    private float AkariFinaleProgress => Mathf.Clamp((Elapsed - AkariWindup - AkariDashDuration - .58f) / .22f, 0, 1);
    private float AkariFinaleReach => Mathf.Lerp(64, AkariFinaleRadius, AkariFinaleProgress);
    private float AkariFinaleTurn => Mathf.Lerp(-.3f, .3f, AkariFinaleProgress);
    private Vector2 AkariFinalePoint(float t) => AkariFinaleCenter
        + Direction.Rotated(-1.35f + 2.7f * t + AkariFinaleTurn) * AkariFinaleReach;
    private float AkariFinaleWidthAt(float t) => AkariFinaleReach * (.009f + .116f * Mathf.Pow(Mathf.Sin(Mathf.Pi * t), .7f));

    private void TickKoharu()
    {
        float nextBloom = _bloomCount == 3 ? KoharuDuration : 1.1f + _bloomCount * .95f;
        if (_bloomCount < 4 && Elapsed >= nextBloom)
        {
            _bloomAt = nextBloom;
            _bloomCount++;
            AttackStages++;
            _previousBloomRadius = 0;
            _strikeHits.Clear();
            Audio.Instance?.PlayChargeImpact(Character);
            GameCamera.Instance?.Shake(_bloomCount == 4 ? 3f : 1f, .18f);
        }
        if (BloomActive)
        {
            float radius = BloomRadius;
            foreach (var target in Targets(_player))
            {
                float size = target is Enemy enemy ? enemy.SpecialHitRadius : 3;
                float vertical = target is Enemy body ? Mathf.Max(0, body.SpecialHitHalfHeight - size) : 0;
                Vector2 a = target.GlobalPosition - Vector2.Down * vertical, b = target.GlobalPosition + Vector2.Down * vertical;
                float nearest = DistanceToSegment(Origin, a, b) - size;
                float farthest = Mathf.Max(Origin.DistanceTo(a), Origin.DistanceTo(b)) + size;
                if (nearest <= radius + 3 && farthest >= _previousBloomRadius - 3)
                    StrikeTarget(target, _bloomCount == 4 ? 100 : 22, _bloomCount == 4 ? 36 : 6, _strikeHits);
                if (!Active) return;
            }
        }
        if (_healingResolved || Elapsed < KoharuDuration) return;
        _healingResolved = true;
        if (_player.GlobalPosition.DistanceSquaredTo(Origin) > KoharuRadius * KoharuRadius) return;
        while (_player.Lives < _player.MaxLives && _player.AddLife()) Healed = true;
        if (Healed) FxLayer.Instance?.PurifyBurst(_player.GlobalPosition);
        Audio.Instance?.PlayCalm();
    }

    private bool BloomActive => _bloomCount > 0 && Elapsed - _bloomAt <= KoharuBloomDuration;
    private float BloomRadius => Mathf.Lerp(8, _bloomCount == 4 ? 210 : 60 + _bloomCount * 30,
        Mathf.Clamp((Elapsed - _bloomAt) / KoharuBloomDuration, 0, 1));

    private void TickRei(float dt)
    {
        if (_constellationAt >= 0)
        {
            if (Elapsed >= _constellationAt && !_constellationReleased)
            {
                _constellationReleased = true;
                AttackStages++;
                foreach (var link in _links)
                {
                    StrikeSegment(link.From, link.To, ReiThreadRadius, 110, 42, _strikeHits);
                    if (!Active) return;
                    StrikeSegment(link.To, link.To, ReiNovaRadius, 110, 42, _strikeHits);
                    if (!Active) return;
                }
                Audio.Instance?.PlayChargeRelease(Character);
                GameCamera.Instance?.Shake(3.6f, .26f);
            }
            return;
        }
        if (_chainTarget == null) return;
        if (IsInstanceValid(_chainTarget) && !_chainTarget.IsQueuedForDeletion())
            _chainLastPosition = _chainTarget.GlobalPosition;
        _chainTimer += dt;
        if (_chainTimer < ReiHopInterval) return;
        var target = _chainTarget;
        _chainTarget = null;
        bool removed = TargetRemoved(target);
        if ((!removed && !TargetAlive(target)) || (_chainVisited.Count > 1
            && _chainLastPosition.DistanceTo(_chainFrom) > ReiChainRange))
        {
            FinishChain();
            return;
        }
        Vector2 at = _chainLastPosition;
        _links.Add((_chainFrom, at, Elapsed));
        AttackStages++;
        if (!removed) StrikeTarget(target, 40, 12);
        if (!Active) return;
        _chainFrom = at;
        Audio.Instance?.PlayChargeImpact(Character);
        if (_chainVisited.Count >= ReiMaxTargets)
        {
            FinishChain();
            return;
        }
        Node2D? nearest = null;
        float nearestDistance = ReiChainRange * ReiChainRange;
        foreach (var candidate in Targets(_player))
        {
            if (_chainVisited.Contains(candidate.GetInstanceId())) continue;
            float distance = at.DistanceSquaredTo(candidate.GlobalPosition);
            if (distance > nearestDistance) continue;
            nearestDistance = distance;
            nearest = candidate;
        }
        SetChainTarget(nearest);
        if (nearest == null) FinishChain();
    }

    private void FinishChain()
    {
        _chainTarget = null;
        _constellationAt = Elapsed + .28f;
        _finishAt = Elapsed + .95f;
    }

    private void SetChainTarget(Node2D? target)
    {
        _chainTarget = target;
        _chainTimer = 0f;
        if (target != null)
        {
            _chainVisited.Add(target.GetInstanceId());
            _chainLastPosition = target.GlobalPosition;
        }
    }

    private static bool TargetRemoved(Node2D target)
        => !IsInstanceValid(target) || target.IsQueuedForDeletion()
            || target is Enemy { IsPurified: true } || target is Panel { Ink: <= 0 };

    private static bool TargetAlive(Node2D target)
        => IsInstanceValid(target) && !target.IsQueuedForDeletion() && target.IsVisibleInTree()
            && new Rect2(Field.Left, Field.Top, Field.WorldRight - Field.Left, Field.Height).HasPoint(target.GlobalPosition)
            && (target switch
            {
                Panel panel => panel.CanLock && panel.GetParent() is Enemy { RallyGuarded: false, Transforming: false, UnfoldersActive: true },
                Enemy enemy => !enemy.IsPurified && !enemy.Transforming && !enemy.RallyGuarded
                    && (enemy is TrainingDummy || enemy.GaugeBodyExposed || !enemy.HasHpBar && enemy.UnfoldersActive),
                _ => false,
            });

    private static List<Node2D> Targets(Player player)
    {
        var targets = new List<Node2D>();
        foreach (var node in player.GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Enemy enemy || enemy.IsPurified || enemy.IsQueuedForDeletion()
                || !enemy.IsVisibleInTree() || enemy.Transforming || enemy.RallyGuarded) continue;
            bool panelFound = false;
            if (enemy.UnfoldersActive)
                foreach (var child in enemy.GetChildren())
                    if (child is Panel panel && TargetAlive(panel))
                    {
                        targets.Add(panel);
                        panelFound = true;
                    }
            if (!panelFound && TargetAlive(enemy)) targets.Add(enemy);
        }
        return targets;
    }

    private static Node2D? FindFirstTarget(Player player)
    {
        var targets = Targets(player);
        if (player.LockedUnfolder is { } panel && targets.Contains(panel)) return panel;
        if (player.LockTarget is { } locked)
        {
            if (targets.Contains(locked)) return locked;
            if (locked is Enemy enemy && enemy.SelectUnfolder(player.GlobalPosition, null) is { } shield
                && targets.Contains(shield)) return shield;
        }
        Node2D? nearest = null;
        float distance = float.MaxValue;
        foreach (var target in targets)
        {
            float d = player.GlobalPosition.DistanceSquaredTo(target.GlobalPosition);
            if (d >= distance) continue;
            distance = d;
            nearest = target;
        }
        return nearest;
    }

    private void StrikeSegment(Vector2 from, Vector2 to, float radius, int bodyDamage, int shieldDamage, HashSet<ulong>? hits = null)
    {
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (!Active) return;
            if (node is not Enemy enemy || enemy.IsPurified || enemy.IsQueuedForDeletion() || !enemy.IsVisibleInTree()) continue;
            foreach (var child in enemy.GetChildren())
                if (child is Panel panel && TargetAlive(panel)
                    && DistanceToSegment(panel.GlobalPosition, from, to) <= radius + 3f)
                    StrikeTarget(panel, bodyDamage, shieldDamage, hits);
            if (!Active) return;
            if (!TargetAlive(enemy)) continue;
            float vertical = Mathf.Max(0f, enemy.SpecialHitHalfHeight - enemy.SpecialHitRadius);
            Vector2 a = enemy.GlobalPosition - new Vector2(0, vertical);
            Vector2 b = enemy.GlobalPosition + new Vector2(0, vertical);
            if (SegmentDistance(from, to, a, b) <= radius + enemy.SpecialHitRadius)
            {
                Node2D target = !enemy.HasHpBar && enemy is not TrainingDummy
                    ? (Node2D?)enemy.SelectUnfolder(from, null) ?? enemy : enemy;
                StrikeTarget(target, bodyDamage, shieldDamage, hits);
            }
        }
    }

    private void StrikeTarget(Node2D target, int bodyDamage, int shieldDamage, HashSet<ulong>? hits = null)
    {
        ulong id = target.GetInstanceId();
        var struck = hits ?? _hitTargets;
        if (struck.Contains(id)) return;
        bool hit = target switch
        {
            Panel panel => panel.TakeSpecialHit(shieldDamage),
            Enemy enemy => enemy.TakeSpecialHit(bodyDamage, Origin),
            _ => false,
        };
        if (hit)
        {
            struck.Add(id);
            _hitTargets.Add(id);
        }
    }

    private void UpdateEnemyBullets()
    {
        _bulletNext.Clear();
        foreach (var node in GetTree().GetNodesInGroup("enemy_bullets"))
        {
            if (node is not Bullet { Active: true, IsEnemy: true } bullet) continue;
            ulong id = bullet.GetInstanceId();
            Vector2 to = bullet.GlobalPosition;
            Vector2 from = _bulletPrevious.TryGetValue(id, out var previous) && previous.Activation == bullet.ActivationId
                ? previous.Position : to;
            bool caught = false;
            if (Character == Job.Tank && Elapsed < MinaGuardDuration)
            {
                Vector2 center = _player.GlobalPosition + Direction * MinaShieldOffset;
                Vector2 wing = Direction.Orthogonal() * MinaShieldHalfLength;
                caught = SegmentDistance(from, to, center - wing, center + wing) <= bullet.CollisionRadius + 2.5f;
                if (caught) _guardAbsorbed++;
            }
            else if (Character == Job.Tank && _beamReleased)
                caught = SegmentDistance(from, to, _beamStart, _beamEnd) <= BeamRadius + bullet.CollisionRadius;
            else if (Character == Job.Heal && Elapsed < KoharuDuration)
                caught = DistanceToSegment(Origin, from, to) <= KoharuRadius + bullet.CollisionRadius;
            else if (Character == Job.Melee && Elapsed >= AkariWindup && Elapsed <= AkariWindup + AkariDashDuration)
                caught = SegmentDistance(from, to, _dashPrevious, _player.GlobalPosition) <= AkariSlashRadius + bullet.CollisionRadius;
            else if (Character == Job.Melee && _slashStage == 3 && Elapsed - _slashAt < .22f)
            {
                for (int i = 0; i < 32 && !caught; i++)
                    caught = SegmentDistance(from, to, AkariFinalePoint(i / 32f), AkariFinalePoint((i + 1) / 32f))
                        <= AkariFinaleWidthAt((i + .5f) / 32) + bullet.CollisionRadius;
            }
            else if (Character == Job.Melee && _slashStage > 0 && _slashStage < 3 && Elapsed - _slashAt < .16f)
                caught = SegmentDistance(from, to, _slashFrom, _slashTo) <= AkariCutRadius + bullet.CollisionRadius;
            else if (Character == Job.Magic && _constellationReleased && Elapsed - _constellationAt < .18f)
                foreach (var link in _links)
                    if (SegmentDistance(from, to, link.From, link.To) <= ReiThreadRadius + bullet.CollisionRadius
                        || DistanceToSegment(link.To, from, to) <= ReiNovaRadius + bullet.CollisionRadius)
                    { caught = true; break; }
            if (!caught && Character == Job.Heal && BloomActive)
                caught = DistanceToSegment(Origin, from, to) <= BloomRadius + 3 + bullet.CollisionRadius
                    && Mathf.Max(Origin.DistanceTo(from), Origin.DistanceTo(to)) >= _previousBloomRadius - 3 - bullet.CollisionRadius;
            if (caught)
            {
                _pool.Despawn(bullet);
                AbsorbedCount++;
                if (_absorptions.Count < 24) _absorptions.Add((to, Elapsed));
                if (AbsorbedCount % 4 == 1) Audio.Instance?.PlayGraze();
            }
            else _bulletNext[id] = (bullet.ActivationId, to);
        }
        (_bulletPrevious, _bulletNext) = (_bulletNext, _bulletPrevious);
        if (Character == Job.Heal && BloomActive) _previousBloomRadius = BloomRadius;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
        => Geometry2D.GetClosestPointToSegment(point, from, to).DistanceTo(point);

    private static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Vector2 ab = b - a, cd = d - c;
        float cross = ab.Cross(cd);
        if (Mathf.Abs(cross) > .00001f)
        {
            float t = (c - a).Cross(cd) / cross, u = (c - a).Cross(ab) / cross;
            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
        }
        return Mathf.Min(Mathf.Min(DistanceToSegment(a, c, d), DistanceToSegment(b, c, d)),
            Mathf.Min(DistanceToSegment(c, a, b), DistanceToSegment(d, a, b)));
    }

    public void Cancel()
    {
        if (!Active) return;
        Active = false;
        EndCutin();
        QueueFree();
    }

    public override void _ExitTree()
    {
        EndCutin();
        Active = false;
    }
}
