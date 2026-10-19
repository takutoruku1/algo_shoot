using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum BossTechniqueKind { Normal, Edge, Signature, Area, Finale, Crossfire, Dodge, Extra, Cameo }

internal readonly record struct BossEasyCue(string Key, double StartsAt, double FiresAt, float Progress, bool Live, bool Cancelled = false);

public abstract partial class BossCharacterTechnique : Node2D, IAoeHazard
{
    protected Enemy Boss = null!;
    protected Player Player = null!;
    protected BulletPool Pool = null!;
    private Hud? _hud;
    protected int Tier;
    protected bool Evolved;
    protected BossTechniqueKind Kind;
    protected int Variant;
    protected double Time, PreviousTime;
    private readonly List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)> _hazards = new();
    protected IReadOnlyList<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)> HazardShapes => _hazards;
    private readonly List<(Vector2 Position, string Text)> _hints = new();
    public bool Finished { get; private set; }
    public bool Completed { get; private set; }
    internal event Action<BossCharacterTechnique>? Ended;
    public string SpellName { get; private set; } = "";
    public string MechanicKey { get; private set; } = "";
    public string Counterplay { get; private set; } = "";
    internal BossTechniqueKind TechniqueKind => Kind;
    internal int TechniqueVariant => Variant;
    internal bool IsEnhanced => Evolved;
    internal double Elapsed => Time;
    internal int ShotsCreated { get; private set; }
    internal int HazardFrames { get; private set; }
    internal int NaturalEndRemainingShots { get; private set; } = -1;
    internal int NaturalEndLiveHazards { get; private set; } = -1;
    internal virtual IEnumerable<BossEasyCue> EasyCues => Array.Empty<BossEasyCue>();
    internal Vector2 LastPosition { get; set; }
    internal IEnumerable<Bullet> OwnedShots => GetTree().GetNodesInGroup("enemy_bullets")
        .OfType<Bullet>().Where(b => b.Active && b.BossBarrageId == GetInstanceId());
    public bool IsStriking => !Finished && !Hud.BubblePaused && _hazards.Any(h => h.Live);
    private bool Interrupted => !IsInstanceValid(Boss) || Boss.IsQueuedForDeletion() || Boss.IsPurified
        || Boss.Transforming || Boss.GaugeVulnerable || Boss.GaugeReforming || !Boss.IsVisibleInTree()
        || (Boss is BossMina mina ? mina.IsDragonForm : Boss.HasEvolved) != Evolved
        || Boss is BossMina { Transitioning: true };

    public static BossCharacterTechnique Begin(Enemy boss, BossTechniqueKind kind, int variant = 0)
    {
        BossCharacterTechnique technique = boss.BodyShotJob switch
        {
            Job.Melee => new AkariBossTechnique(), Job.Heal => new KoharuBossTechnique(),
            Job.Magic => new ReiBossTechnique(), _ => new MinaBossTechnique(),
        };
        technique.Boss = boss;
        technique.Kind = kind;
        technique.Variant = variant;
        technique.TopLevel = true;
        boss.AddChild(technique);
        return technique;
    }

    public sealed override void _Ready()
    {
        GlobalPosition = Vector2.Zero;
        ZIndex = 2;
        ZAsRelative = false;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        Player = (Player)GetTree().GetFirstNodeInGroup("player");
        Pool = GetNode<BulletPool>("/root/Pool");
        _hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
        Tier = (int)GameManager.Instance!.Difficulty;
        Evolved = Boss is BossMina mina ? mina.IsDragonForm : Boss.HasEvolved;
        LastPosition = Player.GlobalPosition;
        AddToGroup("boss_character_techniques");
        AddToGroup("aoe");
        if (Kind == BossTechniqueKind.Dodge) AddToGroup("boss_dodge_waves");
        Start();
        Boss.OnTechniqueStarted(this);
        Boss.AnnounceTechnique(SpellName, 3.6);
        _hud?.TrackTechnique(this);
        Step(0);
        QueueRedraw();
    }

    protected abstract void Start();
    protected abstract void Step(double delta);
    protected virtual bool UsesCustomHazardArt => false;
    protected virtual void DrawTechnique() { }
    protected void Identify(string name, string mechanicKey, string counterplay)
    {
        SpellName = name;
        MechanicKey = mechanicKey;
        Counterplay = counterplay;
    }
    protected bool At(double when) => PreviousTime < when && Time >= when;
    protected double WarnFor(float distance, double minimum = 1.1)
        => Math.Max(minimum, Math.Max(0, distance) / Player.SlowestMoveSpeed + 0.45);

    protected Bullet Shot(Vector2 at, Vector2 direction, int pattern = 0)
    {
        var shot = Pool.SpawnBossShot(Boss.ShotCenter, direction.Normalized(), Boss.BodyShotJob!.Value, Evolved);
        shot.UseBossPatternVisual(Boss.BodyShotJob.Value, pattern);
        Track(shot);
        shot.DeployBossFormation(at, edgeLaunch: true, warnedLaunch: true);
        return shot;
    }
    protected void Track(Bullet shot)
    {
        shot.BossBarrageId = GetInstanceId();
        shot.BossBarrageWave = 0;
        if (Boss is BossAkari or BossKoharu or BossRei or BossMina or CameoBoss)
        {
            shot.ZIndex = 3;
            shot.ZAsRelative = false;
        }
        ShotsCreated++;
    }
    protected void Line(Vector2 a, Vector2 b, float halfWidth, bool live, float progress = 1)
        => _hazards.Add((a, b, halfWidth, live, progress, 0));
    protected void Disc(Vector2 center, float radius, bool live, float progress = 1)
        => _hazards.Add((center, center, radius, live, progress, 1));
    protected void OutsideDisc(Vector2 center, float radius, bool live, float progress = 1)
        => _hazards.Add((center, center, radius, live, progress, 3));
    protected void Box(Rect2 rect, bool live, float progress = 1)
        => _hazards.Add((rect.Position, rect.End, 0, live, progress, 2));
    protected void Hint(Vector2 position, string text) => _hints.Add((position, text));

    public sealed override void _PhysicsProcess(double delta) => Tick(GameManager.EnemyDelta(delta));
    internal void Tick(double delta)
    {
        if (Finished) return;
        if (Interrupted) { Cancel(); return; }
        if (Hud.BubblePaused || GetTree().Paused) return;
        PreviousTime = Time;
        Time += delta;
        _hazards.Clear();
        _hints.Clear();
        Step(delta);
        if (Finished) return;
        if (_hazards.Any(h => h.Live)) HazardFrames++;
        if (_hazards.Any(h => h.Live) || OwnedShots.Any(b => !b.LeadIn && b.CollisionLayer != 0))
            _hud?.EndTechniqueCutin();
        if (!QaPilot.GodActive && CoversPoint(Player.GlobalPosition)) Player.TakeHit();
        QueueRedraw();
    }
    public sealed override void _Process(double delta)
    {
        if (!Finished && Interrupted) Cancel();
    }
    public bool CoversPoint(Vector2 point)
    {
        if (!IsStriking || point.X < Field.Left || point.X > Field.Right || point.Y < Field.Top || point.Y > Field.Bottom) return false;
        foreach (var h in _hazards)
        {
            if (!h.Live) continue;
            if (h.Shape == 2 && new Rect2(h.A, h.B - h.A).Grow(Player.HitRadius).HasPoint(point)) return true;
            if (h.Shape == 3 && point.DistanceTo(h.A) >= h.Radius - Player.HitRadius) return true;
            if (h.Shape is 0 or 1 && Geometry2D.GetClosestPointToSegment(point, h.A, h.B).DistanceTo(point)
                <= h.Radius + Player.HitRadius) return true;
        }
        return false;
    }
    internal float DangerDistance(Vector2 point, bool includeWarnings)
    {
        float result = float.MaxValue;
        foreach (var h in _hazards)
        {
            if (!h.Live && !includeWarnings) continue;
            float distance;
            if (h.Shape == 2)
            {
                bool inside = point.X >= h.A.X && point.X <= h.B.X && point.Y >= h.A.Y && point.Y <= h.B.Y;
                distance = inside
                    ? -Math.Min(Math.Min(point.X - h.A.X, h.B.X - point.X), Math.Min(point.Y - h.A.Y, h.B.Y - point.Y))
                    : point.DistanceTo(point.Clamp(h.A, h.B));
                distance -= Player.HitRadius;
            }
            else if (h.Shape == 3) distance = h.Radius - point.DistanceTo(h.A) - Player.HitRadius;
            else distance = Geometry2D.GetClosestPointToSegment(point, h.A, h.B).DistanceTo(point) - h.Radius - Player.HitRadius;
            result = Math.Min(result, distance);
        }
        return result;
    }
    protected void Finish()
    {
        if ((Boss is BossAkari or BossKoharu or BossRei or BossMina or CameoBoss) && OwnedShots.Any()) return;
        NaturalEndRemainingShots = OwnedShots.Count();
        NaturalEndLiveHazards = _hazards.Count(h => h.Live);
        Completed = true;
        Cancel();
    }
    public void Cancel()
    {
        if (Finished) return;
        Finished = true;
        if (IsInstanceValid(_hud)) _hud!.EndTechnique(this);
        _hazards.Clear();
        _hints.Clear();
        foreach (var shot in OwnedShots.ToArray()) Pool.Despawn(shot);
        Boss.OnTechniqueFinished(this);
        Ended?.Invoke(this);
        Hide();
        QueueFree();
    }
    public sealed override void _ExitTree()
    {
        Finished = true;
        if (IsInstanceValid(_hud)) _hud!.EndTechnique(this);
        _hazards.Clear();
        _hints.Clear();
        foreach (var shot in OwnedShots.ToArray()) Pool.Despawn(shot);
    }
    internal static (Vector2 A, Vector2 B)? WarningEdgeSpan(Vector2 from, Vector2 to, float radius, int edge)
    {
        Vector2 origin = edge switch { 1 => new(Field.Right, Field.Top), 3 => new(Field.Left, Field.Bottom), _ => Field.Rect.Position };
        Vector2 along = edge < 2 ? Vector2.Down : Vector2.Right;
        float extent = edge < 2 ? Field.Height : Field.Width;
        float first = float.PositiveInfinity, last = float.NegativeInfinity;
        void Include(float low, float high)
        {
            low = Math.Max(0, low);
            high = Math.Min(extent, high);
            if (low > high) return;
            first = Math.Min(first, low);
            last = Math.Max(last, high);
        }
        void Circle(Vector2 center)
        {
            Vector2 offset = center - origin;
            float across = offset.Dot(along.Orthogonal());
            if (Math.Abs(across) > radius) return;
            float half = Mathf.Sqrt(Math.Max(0, radius * radius - across * across));
            float at = offset.Dot(along);
            Include(at - half, at + half);
        }
        Circle(from);
        Circle(to);
        float length = from.DistanceTo(to);
        if (length > 0)
        {
            Vector2 direction = (to - from) / length, normal = direction.Orthogonal(), offset = origin - from;
            float low = 0, high = extent;
            bool Trim(float at, float step, float minimum, float maximum)
            {
                if (Math.Abs(step) < .000001f) return at >= minimum && at <= maximum;
                float a = (minimum - at) / step, b = (maximum - at) / step;
                low = Math.Max(low, Math.Min(a, b));
                high = Math.Min(high, Math.Max(a, b));
                return low <= high;
            }
            if (Trim(offset.Dot(direction), along.Dot(direction), 0, length)
                && Trim(offset.Dot(normal), along.Dot(normal), -radius, radius)) Include(low, high);
        }
        return first <= last ? (origin + along * first, origin + along * last) : null;
    }

    protected void DrawWarningEdgeCaps(Vector2 from, Vector2 to, float radius, Color ink)
    {
        if (Math.Min(from.X, to.X) - radius > Field.Left && Math.Max(from.X, to.X) + radius < Field.Right
            && Math.Min(from.Y, to.Y) - radius > Field.Top && Math.Max(from.Y, to.Y) + radius < Field.Bottom) return;
        for (int edge = 0; edge < 4; edge++)
            if (WarningEdgeSpan(from, to, radius, edge) is { } span && span.A.DistanceSquaredTo(span.B) > .01f)
            {
                DrawLine(span.A, span.B, new Color("251321", .75f), 2.2f, true);
                DrawDashedLine(span.A, span.B, new Color(ink, Math.Max(.8f, ink.A)), 1.1f, 2, true);
            }
    }

    public sealed override void _Draw()
    {
        if (Finished) return;
        if (UsesCustomHazardArt)
        {
            DrawTechnique();
            return;
        }
        foreach (var h in _hazards)
        {
            bool koharuWarning = !h.Live && Boss.BodyShotJob == Job.Heal;
            var edge = h.Live ? new Color("ff7568") : new Color(koharuWarning ? "ff8795" : "ffe0a0");
            var underlay = new Color("1a101e", .96f);
            float edgeWidth = koharuWarning ? 1f : .65f;
            var fill = new Color(edge, h.Live ? 0.30f : 0.08f + Mathf.Clamp(h.Progress, 0, 1) * 0.10f);
            if (h.Shape == 3)
            {
                for (float y = Field.Top; y < Field.Bottom; y += 2)
                {
                    float dy = Math.Abs(y + 1 - h.A.Y);
                    float chord = dy < h.Radius ? Mathf.Sqrt(h.Radius * h.Radius - dy * dy) : 0;
                    float left = Mathf.Clamp(h.A.X - chord, Field.Left, Field.Right);
                    float right = Mathf.Clamp(h.A.X + chord, Field.Left, Field.Right);
                    DrawRect(new Rect2(Field.Left, y, left - Field.Left, 2), fill);
                    DrawRect(new Rect2(right, y, Field.Right - right, 2), fill);
                }
                if (koharuWarning) DrawArc(h.A, h.Radius, 0, Mathf.Tau, 96, underlay, 2.3f, true);
                DrawArc(h.A, h.Radius, 0, Mathf.Tau, 96, edge, koharuWarning ? edgeWidth : .8f, true);
            }
            else if (h.Shape == 2)
            {
                var rect = new Rect2(h.A, h.B - h.A);
                DrawRect(rect, fill);
                if (koharuWarning) DrawRect(rect, underlay, false, 2.3f);
                DrawRect(rect, edge, false, h.Live ? 1.2f : edgeWidth);
            }
            else if (h.Shape == 1)
            {
                DrawCircle(h.A, h.Radius, fill);
                if (koharuWarning) DrawArc(h.A, h.Radius, 0, Mathf.Tau, 40, underlay, 2.3f, true);
                DrawArc(h.A, h.Radius, 0, Mathf.Tau, 40, edge, h.Live ? 1.2f : edgeWidth, true);
            }
            else
            {
                DrawLine(h.A, h.B, fill, Math.Max(1, h.Radius * 2), true);
                DrawCircle(h.A, h.Radius, fill);
                DrawCircle(h.B, h.Radius, fill);
                Vector2 side = h.A.DirectionTo(h.B).Orthogonal() * h.Radius;
                float angle = h.A.DirectionTo(h.B).Angle();
                if (koharuWarning)
                {
                    DrawLine(h.A + side, h.B + side, underlay, 2.3f, true);
                    DrawLine(h.A - side, h.B - side, underlay, 2.3f, true);
                    DrawArc(h.A, h.Radius, angle + Mathf.Pi / 2, angle + Mathf.Pi * 1.5f, 12, underlay, 2.3f, true);
                    DrawArc(h.B, h.Radius, angle - Mathf.Pi / 2, angle + Mathf.Pi / 2, 12, underlay, 2.3f, true);
                }
                DrawLine(h.A + side, h.B + side, edge, edgeWidth, true);
                DrawLine(h.A - side, h.B - side, edge, edgeWidth, true);
                DrawArc(h.A, h.Radius, angle + Mathf.Pi / 2, angle + Mathf.Pi * 1.5f, 12, edge, edgeWidth, true);
                DrawArc(h.B, h.Radius, angle - Mathf.Pi / 2, angle + Mathf.Pi / 2, 12, edge, edgeWidth, true);
            }
        }
        foreach (var hint in _hints)
        {
            float width = UiKit.ZenBold.GetStringSize(hint.Text, fontSize: 9).X;
            Vector2 at = new(Mathf.Clamp(hint.Position.X - width * 0.5f, Field.Left + 3, Field.Right - width - 3),
                Mathf.Clamp(hint.Position.Y, 12, Field.Bottom - 5));
            DrawString(UiKit.ZenBold, at, hint.Text, HorizontalAlignment.Left, -1, 9, new Color("fff1cb"));
        }
        DrawTechnique();
    }
}
