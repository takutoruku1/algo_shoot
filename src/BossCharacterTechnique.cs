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
        Boss.AnnounceTechnique(SpellName, 2, compact: UsesCustomHazardArt);
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
        if (Boss is BossAkari or BossKoharu or BossRei or BossMina)
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
        _hazards.Clear();
        _hints.Clear();
        foreach (var shot in OwnedShots.ToArray()) Pool.Despawn(shot);
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
            var edge = h.Live ? new Color("ff7568") : new Color("ffe0a0");
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
                DrawArc(h.A, h.Radius, 0, Mathf.Tau, 96, edge, 0.8f, true);
            }
            else if (h.Shape == 2)
            {
                var rect = new Rect2(h.A, h.B - h.A);
                DrawRect(rect, fill);
                DrawRect(rect, edge, false, h.Live ? 1.2f : 0.65f);
            }
            else if (h.Shape == 1)
            {
                DrawCircle(h.A, h.Radius, fill);
                DrawArc(h.A, h.Radius, 0, Mathf.Tau, 40, edge, h.Live ? 1.2f : 0.65f, true);
            }
            else
            {
                DrawLine(h.A, h.B, fill, Math.Max(1, h.Radius * 2), true);
                DrawCircle(h.A, h.Radius, fill);
                DrawCircle(h.B, h.Radius, fill);
                Vector2 side = h.A.DirectionTo(h.B).Orthogonal() * h.Radius;
                DrawLine(h.A + side, h.B + side, edge, 0.65f, true);
                DrawLine(h.A - side, h.B - side, edge, 0.65f, true);
                float angle = h.A.DirectionTo(h.B).Angle();
                DrawArc(h.A, h.Radius, angle + Mathf.Pi / 2, angle + Mathf.Pi * 1.5f, 12, edge, 0.65f, true);
                DrawArc(h.B, h.Radius, angle - Mathf.Pi / 2, angle + Mathf.Pi / 2, 12, edge, 0.65f, true);
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
