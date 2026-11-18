using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum BossTechniqueKind { Normal, Edge, Signature, Area, Finale, Crossfire, Dodge, Extra, Cameo }

internal readonly record struct BossEasyCue(string Key, double StartsAt, double FiresAt, float Progress, bool Live, bool Cancelled = false);

public abstract partial class BossCharacterTechnique : Node2D, IAoeHazard
{
    internal const float HardOpening = 10f;
    protected float TargetOpening => Tier switch { 0 => 14, 1 => 12, 2 => HardOpening, _ => 8 };
    protected Enemy Boss = null!;
    protected Player Player = null!;
    protected BulletPool Pool = null!;
    private Hud? _hud;
    private bool _previewOnly;
    private bool _started;
    private bool _positionPrepared;
    private Node2D _timingCanvas = null!;
    private AttackLineLayer _attackLines = null!;
    private readonly Dictionary<int, Vector4> _fanSlices = new();
    private AttackPresentation _presentation = null!;
    private readonly List<Vector2> _lastLineCenters = new();
    private bool _hadLiveHazards;
    protected int Tier;
    protected bool Evolved;
    protected BossTechniqueKind Kind;
    protected int Variant;
    protected double Time, PreviousTime;
    private readonly List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)> _hazards = new();
    private readonly List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)> _timingHazards = new();
    private readonly HashSet<int> _cueOnly = new();
    private Node2D? _dangerMask;
    private bool _forecastOverLive;
    private float _forecastRight;
    private static readonly Color ForecastInk = new("ffce93");
    private BulletArt.PlayerVisual? _timingVisual;
    private bool _timingVisible;
    protected IEnumerable<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)> HazardShapes
        => _hazards;
    internal IReadOnlyList<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)> PracticeHazards => _hazards;
    private readonly List<(Vector2 Position, AttackTelegraph.Symbol Symbol, float Amount, int Count, Vector2 Direction)> _cues = new();
    public bool Finished { get; private set; }
    public bool Completed { get; private set; }
    internal event Action<BossCharacterTechnique>? Ended;
    public string SpellName { get; private set; } = "";
    public string MechanicKey { get; private set; } = "";
    public string Counterplay { get; private set; } = "";
    internal BossTechniqueKind TechniqueKind => Kind;
    internal int TechniqueVariant => Variant;
    internal bool IsEnhanced => Evolved;
    internal virtual bool UsesArenaBody => false;
    internal virtual Vector2 ResolvePlayerMotion(Vector2 from, Vector2 to) => to;
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
        || Boss.Transforming || Boss.GaugeVulnerable || (Boss.GaugeReforming && !Boss.FinishingSealedBarAttack) || !Boss.IsVisibleInTree()
        || (Boss is BossMina mina ? mina.IsDragonForm : Boss.HasEvolved) != Evolved
        || Boss is BossMina { Transitioning: true };

    public static BossCharacterTechnique Begin(Enemy boss, BossTechniqueKind kind, int variant = 0)
    {
        var technique = Create(boss, kind, variant);
        boss.AddChild(technique);
        return technique;
    }

    internal static (string Name, string Counterplay) Describe(Enemy boss, BossTechniqueKind kind, int variant)
    {
        var technique = Create(boss, kind, variant);
        technique._previewOnly = true;
        boss.AddChild(technique);
        var description = (technique.SpellName, technique.Counterplay);
        technique.Free();
        return description;
    }

    private static BossCharacterTechnique Create(Enemy boss, BossTechniqueKind kind, int variant)
    {
        BossCharacterTechnique technique = boss is BossMina && kind == BossTechniqueKind.Extra && variant == 2
            ? new MinaArchiveTechnique() : kind == BossTechniqueKind.Normal && variant == 5
            && boss.BodyShotJob is Job.Melee or Job.Magic ? new BossBodyAttack() : boss.BodyShotJob switch
        {
            Job.Melee => new AkariBossTechnique(), Job.Heal => new KoharuBossTechnique(),
            Job.Magic => new ReiBossTechnique(), _ => new MinaBossTechnique(),
        };
        technique.Boss = boss;
        technique.Kind = kind;
        technique.Variant = variant;
        technique.TopLevel = true;
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
        if (_previewOnly) { Start(); return; }
        AddToGroup("boss_character_techniques");
        AddToGroup("aoe");
        if (Kind == BossTechniqueKind.Dodge) AddToGroup("boss_dodge_waves");
        Boss.OnTechniqueStarted(this);
        if (!Boss.BossFrameReturning) StartAttack();
    }

    private void StartAttack()
    {
        if (!_positionPrepared)
        {
            Boss.PrepareTechniquePosition(this);
            _positionPrepared = true;
        }
        if (Boss.BossFrameEntering) return;
        _started = true;
        _presentation = new AttackPresentation { Character = Boss.BodyShotJob!.Value };
        Boss.AddChild(_presentation);
        _attackLines = new AttackLineLayer();
        AddChild(_attackLines);
        _timingCanvas = new Node2D { ZIndex = -1, ZAsRelative = false, UseParentMaterial = true };
        _timingCanvas.Draw += DrawTelegraphTimings;
        AddChild(_timingCanvas);
        Start();
        if (UsesDangerForecast)
        {
            var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/attack_forecast_union.gdshader") };
            material.SetShaderParameter("danger_hatch", true);
            material.SetShaderParameter("ink", ForecastInk);
            material.SetShaderParameter("fill_opacity", .28f);
            material.SetShaderParameter("hatch_opacity", .68f);
            _forecastRight = Field.Right;
            material.SetShaderParameter("field_bounds", new Vector4(Field.Left, Field.Top, _forecastRight, Field.Bottom));
            var group = new CanvasGroup { ZIndex = -2, ZAsRelative = false, FitMargin = 3, ClearMargin = 4, Material = material };
            _dangerMask = new Node2D();
            _dangerMask.Draw += () => {
                if (Finished || !GameManager.ShowAttackTelegraphs) return;
                DrawDangerForecast(_dangerMask);
            };
            group.AddChild(_dangerMask);
            AddChild(group);
        }
        Boss.AnnounceTechnique(SpellName, 3.6);
        _hud?.TrackTechnique(this);
        Step(0);
        UpdateAttackLines();
        QueueRedraw();
    }

    protected abstract void Start();
    protected abstract void Step(double delta);
    protected Node2D? DangerForecastCanvas => _dangerMask;
    protected virtual bool UsesDangerForecast => false;
    protected virtual void DrawDangerForecast(Node2D canvas)
    {
        for (int i = 0; i < _hazards.Count; i++)
        {
            var h = _hazards[i];
            if (h.Live || h.Progress <= 0 || _cueOnly.Contains(i)) continue;
            switch (h.Shape)
            {
                case 0: AttackTelegraph.MaskStroke(canvas, h.A, h.B, h.Radius); break;
                case 1: canvas.DrawCircle(h.A, h.Radius, Colors.White); break;
                case 2: canvas.DrawRect(new Rect2(h.A, h.B - h.A), Colors.White); break;
                case 3: AttackTelegraph.MaskOutsideDisc(canvas, h.A, h.Radius); break;
            }
        }
    }
    protected virtual bool UsesCustomHazardArt => false;
    protected virtual bool UsesAttackLineArt => true;
    protected virtual Color AttackLineInk => new("ff7568");
    protected virtual Texture2D? AttackLineMotif => null;
    protected virtual bool PreserveAttackEndDisc => false;
    protected virtual void DrawTechnique() { }
    protected virtual bool DrawCustomBox(Node2D canvas, Rect2 rect, bool live, float progress) => false;
    protected virtual bool DirectionalWarnings => false;
    protected virtual BulletArt.PlayerVisual? WarningVisual => null;
    protected virtual BulletArt.PlayerVisual ShotVisual(int pattern) => WarningVisual ?? BulletArt.BossPattern(Boss.BodyShotJob!.Value, pattern);
    protected virtual IEnumerable<(Vector2 Origin, double StartsAt, double FiresAt)> TelegraphTimings
        => Array.Empty<(Vector2, double, double)>();

    private void DrawTelegraphTimings()
    {
        if (Finished || !GameManager.ShowAttackTelegraphs) return;
        if (!UsesCustomHazardArt) DrawHazards(_timingCanvas, false);
        var pending = TelegraphTimings.Where(cue => double.IsFinite(cue.FiresAt) && Time >= cue.StartsAt && Time < cue.FiresAt)
            .OrderBy(cue => cue.FiresAt).GroupBy(cue => cue.Origin).Select(group => group.First())
            .GroupBy(cue => Math.Round(cue.FiresAt, 2)).Select(group => group.First()).Take(2).ToArray();
        Rect2? previous = null;
        for (int index = 0; index < pending.Length; index++)
        {
            var cue = pending[index];
            previous = AttackTelegraph.Countdown(_timingCanvas, cue.Origin, (float)((cue.FiresAt - Time) / (cue.FiresAt - cue.StartsAt)),
                index == 0, previous);
        }
    }
    protected void Identify(string name, string mechanicKey, string counterplay)
    {
        SpellName = name;
        MechanicKey = mechanicKey;
        Counterplay = counterplay;
    }
    protected bool At(double when) => PreviousTime < when && Time >= when;
    protected internal readonly record struct FanRay(Vector2 From, Vector2 Direction);

    // Parallel cut edges stay 14px apart; a shot fitted inside a 4px radius leaves at least 6px between them.
    protected static FanRay[] SplitFan(Vector2 origin, Vector2 facing, float halfAngle, float cutAngle)
    {
        if (facing == Vector2.Zero) facing = Vector2.Left;
        Vector2 cut = facing.Rotated(cutAngle).Normalized();
        Vector2 normal = new(-cut.Y, cut.X);
        float reach = Math.Max(Math.Max(origin.DistanceTo(Field.Rect.Position), origin.DistanceTo(Field.Rect.End)),
            Math.Max(origin.DistanceTo(new(Field.Left, Field.Bottom)), origin.DistanceTo(new(Field.Right, Field.Top)))) + 12;
        var rays = new List<FanRay>();
        for (int side = -1; side <= 1; side += 2)
        {
            float angle = side * halfAngle - cutAngle;
            int intervals = Math.Max(1, Mathf.CeilToInt(Math.Abs(angle) * reach / 14));
            Vector2 from = origin + normal * (side * 7);
            for (int i = 0; i <= intervals; i++)
                rays.Add(new(from, cut.Rotated(angle * i / intervals)));
        }
        return rays.ToArray();
    }

    protected double WarnFor(float distance, double minimum = 1.1)
        => Math.Max(Tier == 0 ? Math.Max(2.2, minimum) : minimum,
            Math.Max(0, distance) / Player.SlowestMoveSpeed + 0.45);

    protected Bullet Shot(Vector2 at, Vector2 direction, int pattern = 0)
    {
        var shot = Pool.SpawnBossShot(Boss.ShotCenter, direction.Normalized(), Boss.BodyShotJob!.Value, Evolved);
        shot.UseBossPatternVisual(Boss.BodyShotJob.Value, pattern);
        shot.UseBossIllustration(ShotVisual(pattern), fitRadius: shot.IllustrationRadius);
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
    protected void Line(Vector2 a, Vector2 b, float halfWidth, bool live, float progress = 1, bool cueOnly = false)
    {
        if (cueOnly) _cueOnly.Add(_hazards.Count);
        _hazards.Add((a, b, halfWidth, live, progress, 0));
    }
    protected void Disc(Vector2 center, float radius, bool live, float progress = 1, bool cueOnly = false)
    {
        if (cueOnly) _cueOnly.Add(_hazards.Count);
        _hazards.Add((center, center, radius, live, progress, 1));
    }
    protected void OutsideDisc(Vector2 center, float radius, bool live, float progress = 1)
        => _hazards.Add((center, center, radius, live, progress, 3));
    protected void Box(Rect2 rect, bool live, float progress = 1, bool cueOnly = false)
    {
        if (cueOnly) _cueOnly.Add(_hazards.Count);
        _hazards.Add((rect.Position, rect.End, 0, live, progress, 2));
    }
    protected void FanSlice(Rect2 rect, bool live, float progress, Vector2 origin, Vector2 spread)
    {
        if (live) _fanSlices[_hazards.Count] = new Vector4(origin.X, origin.Y, spread.X, spread.Y);
        Box(rect, live, progress);
    }
    private protected void Cue(Vector2 position, AttackTelegraph.Symbol symbol, float amount = 1, int count = 1, Vector2 direction = default)
        => _cues.Add((position, symbol, amount, count, direction));

    protected void DrawBodyContact(float radius, Color ink, bool live)
    {
        Vector2 center = Boss.GlobalPosition;
        if (live)
        {
            DrawTextureRect(ContactRim, new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2), false, ink);
            DrawArc(center, radius, 0, Mathf.Tau, 64, AttackTelegraph.Outline, 2.4f, true);
            DrawArc(center, radius, 0, Mathf.Tau, 64, ink, 1.1f, true);
            DrawArc(center, radius - 1.8f, Mathf.Pi, Mathf.Pi * 1.65f, 24, AttackTelegraph.Light, .65f, true);
        }
        else if (GameManager.ShowAttackTelegraphs)
            for (int i = 0; i < 18; i++)
            {
                float start = i * Mathf.Tau / 18;
                DrawArc(center, radius, start, start + .17f, 4, AttackTelegraph.Outline, 2, true);
                DrawArc(center, radius, start, start + .17f, 4, new Color(ink, .7f), .7f, true);
            }
    }

    private static readonly Texture2D ContactRim = GD.Load<Texture2D>("res://assets/fx/attack_surfaces/rim.png");

    public sealed override void _PhysicsProcess(double delta) => Tick(GameManager.EnemyDelta(delta));
    internal void Tick(double delta)
    {
        if (Finished) return;
        if (Interrupted) { Cancel(); return; }
        if (Hud.BubblePaused || GetTree().Paused) return;
        if (!_started)
        {
            Boss.TickBossEntry(delta);
            if (!Boss.BossFrameReturning) StartAttack();
            return;
        }
        PreviousTime = Time;
        Time += delta;
        float charge = EasyCues.Where(cue => !cue.Cancelled && !cue.Live && Time >= cue.StartsAt && Time < cue.FiresAt)
            .Select(cue => cue.Progress)
            .Concat(TelegraphTimings.Where(cue => Time >= cue.StartsAt && Time < cue.FiresAt)
                .Select(cue => (float)((Time - cue.StartsAt) / (cue.FiresAt - cue.StartsAt))))
            .DefaultIfEmpty(-1).Max();
        _presentation.Advance(delta, Boss.ShotCenter, charge);
        _hazards.Clear();
        _fanSlices.Clear();
        _cueOnly.Clear();
        _cues.Clear();
        Step(delta);
        if (Finished) return;
        _presentation.Trace(OwnedShots, delta);
        UpdateAttackLines();
        if (_hazards.Any(h => h.Live)) HazardFrames++;
        bool attacking = _hazards.Any(h => h.Live) || OwnedShots.Any(b => !b.LeadIn && b.CollisionLayer != 0);
        if (attacking) _hud?.EndTechniqueCutin();
        if (!QaPilot.GodActive && CoversPoint(Player.GlobalPosition)) Player.TakeHit();
        UpdateTelegraphDrawing(attacking);
        QueueRedraw();
    }
    private void UpdateTelegraphDrawing(bool attacking)
    {
        bool visible = GameManager.ShowAttackTelegraphs;
        if (_dangerMask != null)
        {
            _dangerMask.Visible = visible;
            if (visible)
            {
                var material = (ShaderMaterial)_dangerMask.GetParent<CanvasGroup>().Material;
                if (_forecastOverLive != attacking)
                {
                    _forecastOverLive = attacking;
                    material.SetShaderParameter("ink", new Color(ForecastInk, attacking ? .60f : 1f));
                }
                if (_forecastRight != Field.Right)
                {
                    _forecastRight = Field.Right;
                    material.SetShaderParameter("field_bounds", new Vector4(Field.Left, Field.Top, _forecastRight, Field.Bottom));
                }
                _dangerMask.QueueRedraw();
            }
        }
        var visual = WarningVisual;
        // Fixed routes retain their draw commands; countdowns still advance every frame.
        if (visible == _timingVisible && ReferenceEquals(visual, _timingVisual)
            && HazardShapes.SequenceEqual(_timingHazards) && !TelegraphTimings.Any()) return;
        _timingVisible = visible;
        _timingVisual = visual;
        _timingHazards.Clear();
        _timingHazards.AddRange(HazardShapes);
        _timingCanvas.QueueRedraw();
    }
    private void UpdateAttackLines()
    {
        var liveHazard = _hazards.FirstOrDefault(h => h.Live);
        if (liveHazard.Live && !_hadLiveHazards)
            _presentation.Launch(liveHazard.Shape == 0 ? liveHazard.A : Boss.ShotCenter, liveHazard.B - liveHazard.A);
        _hadLiveHazards = liveHazard.Live;
        if (!UsesAttackLineArt) return;
        var liveLines = HazardShapes.Where(h => h.Live && h.Shape == 0 && h.A != h.B).ToArray();
        if (liveLines.Length == 0)
            foreach (var center in _lastLineCenters) _presentation.Dissolve(center);
        _lastLineCenters.Clear();
        _lastLineCenters.AddRange(liveLines.Take(6).Select(h => (h.A + h.B) * .5f));
        _attackLines.Begin(Time);
        int count = _hazards.Count;
        for (int i = 0; i < count; i++)
        {
            var h = _hazards[i];
            if (!h.Live) continue;
            if (h.Shape != 0 || h.A == h.B)
            {
                var ink = new Color(AttackLineInk, .50f);
                if (h.Shape == 3) _attackLines.Surface(Field.Rect, ink, safeCenter: h.A, safeRadius: h.Radius);
                else if (_fanSlices.TryGetValue(i, out var fan))
                    _attackLines.FanRow(new Rect2(h.A, h.B - h.A), new Color(AttackLineInk, .78f), fan);
                else if (h.Shape == 2) _attackLines.Surface(new Rect2(h.A, h.B - h.A), ink);
                else _attackLines.Surface(new Rect2(h.A - Vector2.One * h.Radius, Vector2.One * h.Radius * 2), ink, disc: true);
                continue;
            }
            var points = new List<Vector2> { h.A, h.B };
            while (!PreserveAttackEndDisc && i + 1 < count)
            {
                var next = _hazards[i + 1];
                if (!next.Live || next.Shape != 0 || next.Radius != h.Radius || next.A != points[^1] || next.A == next.B) break;
                points.Add(next.B);
                i++;
            }
            _attackLines.Add(points.ToArray(), h.Radius, AttackLineInk, AttackLineMotif,
                preserveEndDisc: PreserveAttackEndDisc, ribbon: !UsesCustomHazardArt);
        }
        _attackLines.End();
    }
    public sealed override void _Process(double delta)
    {
        if (!Finished && Interrupted) Cancel();
    }
    public bool CoversPoint(Vector2 point)
    {
        if (Finished || Hud.BubblePaused || point.X < Field.Left || point.X > Field.Right || point.Y < Field.Top || point.Y > Field.Bottom) return false;
        return DangerDistance(point, false) <= 0;
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
            }
            else if (h.Shape == 3) distance = h.Radius - point.DistanceTo(h.A);
            else if (h.A == h.B) distance = point.DistanceTo(h.A) - h.Radius;
            else distance = Geometry2D.GetClosestPointToSegment(point, h.A, h.B).DistanceTo(point) - h.Radius;
            result = Math.Min(result, distance);
        }
        return result - Player.CurrentHitRadius;
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
        if (Completed)
            foreach (var center in _lastLineCenters) _presentation.Dissolve(center);
        if (_started) _presentation.Finish(Completed);
        _hazards.Clear();
        _cues.Clear();
        foreach (var shot in OwnedShots.ToArray()) Pool.Despawn(shot);
        Boss.OnTechniqueFinished(this);
        Ended?.Invoke(this);
        Hide();
        QueueFree();
    }
    public sealed override void _ExitTree()
    {
        if (_previewOnly) return;
        if (!Finished && IsInstanceValid(_presentation)) _presentation.Finish(false);
        Finished = true;
        if (IsInstanceValid(_hud)) _hud!.EndTechnique(this);
        _hazards.Clear();
        _cues.Clear();
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
                DrawLine(span.A, span.B, new Color(AttackTelegraph.Outline, ink.A * .6f), 1.5f, true);
                DrawLine(span.A, span.B, ink, .65f, true);
            }
    }

    public sealed override void _Draw()
    {
        if (Finished || !_started) return;
        if (UsesCustomHazardArt)
        {
            DrawTechnique();
            return;
        }
        DrawHazards(this, true);
        DrawTechnique();
        if (GameManager.ShowAttackTelegraphs)
            foreach (var cue in _cues)
                AttackTelegraph.DrawSymbol(this, cue.Position, cue.Symbol, AttackLineInk, cue.Amount, cue.Count, cue.Direction);
    }

    private void DrawHazards(Node2D canvas, bool live)
    {
        var routeStarts = new HashSet<Vector2>();
        Vector2? previousEnd = null;
        int count = _hazards.Count;
        for (int index = 0; index < count; index++)
        {
            var h = _hazards[index];
            if (h.Live != live || live && h.Shape == 0 && h.A != h.B) continue;
            if (h.Shape == 2 && DrawCustomBox(canvas, new Rect2(h.A, h.B - h.A), live, h.Progress)) continue;
            if (!live)
            {
                if (h.Shape == 2) { AttackTelegraph.Box(canvas, new Rect2(h.A, h.B - h.A), WarningVisual?.Accent ?? AttackTelegraph.Area); continue; }
                if (h.Shape == 1)
                {
                    AttackTelegraph.Disc(canvas, h.A, h.Radius, WarningVisual?.Accent ?? AttackTelegraph.Area);
                    if (WarningVisual is { } art && routeStarts.Add(h.A)) AttackTelegraph.ProjectileOrigin(canvas, h.A, art, art.Accent);
                    continue;
                }
                if (h.Shape == 0)
                {
                    bool connected = previousEnd is { } previous && previous.DistanceSquaredTo(h.A) < .01f;
                    bool continues = index + 1 < _hazards.Count && _hazards[index + 1] is var next
                        && !next.Live && next.Shape == 0 && next.Radius == h.Radius && next.A.DistanceSquaredTo(h.B) < .01f;
                    AttackTelegraph.Route(canvas, h.A, h.B, h.Radius, !connected && routeStarts.Add(h.A),
                        DirectionalWarnings, !connected, !continues, WarningVisual);
                    previousEnd = h.B;
                    continue;
                }
            }
            Color edge = live ? AttackLineInk.Lerp(AttackTelegraph.Light, .4f) : AttackTelegraph.Area;
            Color body = live ? new Color(AttackLineInk.Darkened(.3f), .62f) : new Color(edge, .08f);
            if (h.Shape == 3)
            {
                for (float y = Field.Top; y < Field.Bottom; y += 2)
                {
                    float dy = Math.Abs(y + 1 - h.A.Y);
                    float chord = dy < h.Radius ? Mathf.Sqrt(h.Radius * h.Radius - dy * dy) : 0;
                    float left = Mathf.Clamp(h.A.X - chord, Field.Left, Field.Right);
                    float right = Mathf.Clamp(h.A.X + chord, Field.Left, Field.Right);
                    canvas.DrawRect(new Rect2(Field.Left, y, left - Field.Left, 2), new Color(body, live && UsesDangerForecast ? .56f : live ? .28f : .08f));
                    canvas.DrawRect(new Rect2(right, y, Field.Right - right, 2), new Color(body, live && UsesDangerForecast ? .56f : live ? .28f : .08f));
                }
                canvas.DrawArc(h.A, h.Radius, 0, Mathf.Tau, 96, AttackTelegraph.Outline, 1.8f, true);
                canvas.DrawArc(h.A, h.Radius, 0, Mathf.Tau, 96, edge, .7f, true);
            }
            else if (h.Shape == 2)
            {
                var rect = new Rect2(h.A, h.B - h.A);
                if (!rect.HasArea()) continue;
                canvas.DrawRect(rect, body);
                float inset = Math.Min(2, Math.Min(rect.Size.X, rect.Size.Y) * .2f);
                Vector2 topRight = new(rect.End.X, rect.Position.Y), bottomLeft = new(rect.Position.X, rect.End.Y);
                canvas.DrawPolyline(new[] { bottomLeft + Vector2.Right * inset, rect.Position + Vector2.One * inset,
                    topRight + new Vector2(-inset, inset) }, new Color(edge, .4f), inset, true);
                canvas.DrawPolyline(new[] { bottomLeft, rect.End, topRight }, new Color(AttackTelegraph.Outline, .65f), 1.6f, true);
                canvas.DrawRect(rect, AttackTelegraph.Outline, false, 1.8f);
                canvas.DrawRect(rect, edge, false, .7f);
                if (WarningVisual is { } motif && BossProjectileArt.For(motif) is { } model)
                    model.Draw(canvas, rect.Grow(-Mathf.Min(2, inset)), Time, new Color(1, 1, 1, .9f));
            }
            else
            {
                canvas.DrawCircle(h.A, h.Radius, body);
                canvas.DrawCircle(h.A, h.Radius * .78f, new Color(AttackLineInk, .16f));
                if (WarningVisual is { } motif && BossProjectileArt.For(motif) is { } model)
                    model.Draw(canvas, new Rect2(h.A - Vector2.One * h.Radius * .7f, Vector2.One * h.Radius * 1.4f), Time);
                canvas.DrawArc(h.A, h.Radius, 0, Mathf.Tau, 64, AttackTelegraph.Outline, 1.8f, true);
                canvas.DrawArc(h.A, h.Radius, 0, Mathf.Tau, 64, edge, .7f, true);
                if (h.Radius > 4)
                    canvas.DrawArc(h.A, h.Radius - 1.8f, Mathf.Pi, Mathf.Pi * 1.65f, 24,
                        new Color(AttackTelegraph.Light, .55f), .65f, true);
            }
        }
    }
}
