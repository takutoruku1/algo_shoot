using Godot;

public partial class StageBackground : Node2D
{
    public BgLayers.Layer[] LayerDefs = System.Array.Empty<BgLayers.Layer>();
    public BgLayers.Layer[] RouteLayerDefs = System.Array.Empty<BgLayers.Layer>();
    public BgLayers.Layer[] LateRouteLayerDefs = System.Array.Empty<BgLayers.Layer>();
    public BgLayers.Layer[] PassageLayerDefs = System.Array.Empty<BgLayers.Layer>();
    public BgLayers.Layer[] ClimaxLayerDefs = System.Array.Empty<BgLayers.Layer>();
    public BgLayers.Layer[] MidbossLayerDefs = System.Array.Empty<BgLayers.Layer>();
    public BgLayers.Layer[] BossLayerDefs = System.Array.Empty<BgLayers.Layer>();
    public BgLayers.BossBehavior LayerBossBehavior = BgLayers.BossBehavior.Dim;
    public bool StartInLateRoute;
    public bool StartInBoss;
    public float MidScrollSpeed = 13f;
    public int BaseZ = -90;

    private enum Mode { Mid, Route, Midboss, Boss }
    private BgLayers _layers = null!;
    private Mode _mode = Mode.Mid;
    private bool _intenseRoute;
    private Spawner? _routeSpawner;
    private float _routeFrom, _routeTo, _routeSeconds, _routeElapsed;
    private float _journey;
    internal int RouteChapter { get; private set; }

    public bool HasMid { get; private set; }
    internal bool IsSunset => _mode == Mode.Route && !_intenseRoute && GetParent() is KoharuRoot;

    public override void _Ready()
    {
        ZIndex = BaseZ;
        ZAsRelative = false;
        AddToGroup("stagebg");

        var opening = LayerDefs;
        _intenseRoute = StartInLateRoute;
        _journey = StartInLateRoute ? 2f : 0f;
        RouteChapter = StartInLateRoute ? 2 : 0;
        if (opening.Length == 0 && RouteLayerDefs.Length > 0)
        {
            opening = _intenseRoute && LateRouteLayerDefs.Length > 0 ? LateRouteLayerDefs : RouteLayerDefs;
            _mode = Mode.Route;
        }
        // FINAL の記憶巡回は空の層から始め、後で背景を重ねる。
        _layers = new BgLayers
        {
            Name = "BgLayers", Layers = opening,
            ScrollSpeed = MidScrollSpeed * (_intenseRoute ? 1.45f : 1f),
            OnBoss = LayerBossBehavior, BossLayers = BossLayerDefs,
        };
        AddChild(_layers);
        HasMid = _layers.HasAny;
        if (StartInBoss) EnterBoss();
    }

    public override void _Process(double delta)
    {
        if (Hud.BubblePaused || Pad.UiBlocked(this)) return;
        float dt = (float)delta;
        _layers.ScrollSpeed = Mathf.Lerp(_layers.ScrollSpeed, MidScrollSpeed * (_intenseRoute ? 1.45f : 1f),
            1f - Mathf.Exp(-1.5f * dt));
        if (_mode != Mode.Route || !IsInstanceValid(_routeSpawner)) return;
        _routeElapsed += dt;
        float progress = _routeSeconds > 0f ? _routeElapsed / _routeSeconds
            : (float)_routeSpawner!.SpawnedCount / _routeSpawner.SpawnLimit;
        float target = Mathf.Lerp(_routeFrom, _routeTo, Mathf.Clamp(progress, 0f, 1f));
        SetJourney(Mathf.MoveToward(_journey, Mathf.Max(_journey, target), dt * 0.16f));
    }

    public void TrackRoute(Spawner spawner, float from, float to, float seconds = 0f)
    {
        if (_mode == Mode.Boss) return;
        _routeSpawner = spawner;
        _routeFrom = from;
        _routeTo = to;
        _routeSeconds = seconds;
        _routeElapsed = 0f;
        SetJourney(Mathf.Max(_journey, from));
    }

    private void SetJourney(float journey)
    {
        _journey = Mathf.Clamp(journey, 0f, 4f);
        int chapter = Mathf.Min(3, (int)_journey);
        var defs = chapter switch
        {
            1 => PassageLayerDefs,
            2 => LateRouteLayerDefs,
            3 => ClimaxLayerDefs,
            _ => RouteLayerDefs,
        };
        _layers.TravelProgress = _journey - chapter;
        if (_mode == Mode.Route && chapter == RouteChapter) return;
        RouteChapter = chapter;
        _intenseRoute = chapter >= 2;
        _mode = Mode.Route;
        _layers.CrossfadeTo(defs, 2.4f);
    }

    public void BeginRoute(bool intense = false)
    {
        if (_mode == Mode.Boss) return;
        SetJourney(Mathf.Max(_journey, intense ? 2f : 0f));
    }

    public void BeginMidboss()
    {
        if (_mode == Mode.Midboss || _mode == Mode.Boss || MidbossLayerDefs.Length == 0) return;
        _mode = Mode.Midboss;
        _routeSpawner = null;
        _layers.CrossfadeTo(MidbossLayerDefs, 0.8f);
    }

    public void EnterBoss()
    {
        _mode = Mode.Boss;
        _routeSpawner = null;
        _layers.EnterBoss();
    }

    public void CrossfadeLayersToBoss(BgLayers.BossBehavior onBoss, BgLayers.Layer[] defs, float dur = 1.0f)
        => _layers.CrossfadeToBoss(onBoss, defs, dur);

    public void FadeOutLayers(float dur = 1.0f) => _layers.FadeOutAll(dur);
}

public static class BgScroll
{
    public static float PlayerNx(Node self)
    {
        var g = self.GetNodeOrNull<GameManager>("/root/Game");
        if (g != null) return g.PlayerNormX;
        var p = self.GetTree().GetFirstNodeInGroup("player") as Node2D;
        if (p != null) return Mathf.Clamp((p.GlobalPosition.X - Field.Left) / Field.Width, 0f, 1f);
        return 0.5f;
    }
}
