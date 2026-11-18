using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

public partial class ReiBossTechnique : BossCharacterTechnique
{
    internal override bool UsesArenaBody => Kind == BossTechniqueKind.Normal && Variant == 1
        || Kind == BossTechniqueKind.Signature;
    private static bool _assetsPrepared;
    internal static void PrepareAssets()
    {
        foreach (float progress in PrepareAssetSteps()) { }
    }

    internal static IEnumerable<float> PrepareAssetSteps()
    {
        if (_assetsPrepared) yield break;
        // First-use glyph rasterization and silhouette extraction must finish before combat.
        var glyphs = new HashSet<int>();
        foreach (string text in GlyphWarmer.ScriptOf(typeof(ReiBossTechnique)))
            foreach (var rune in text.EnumerateRunes())
                if (rune.Value > 127) glyphs.Add(rune.Value);
        int prepared = 0, total = glyphs.Count * 2 + 8;
        foreach (int glyph in glyphs)
        {
            if (UiKit.ZenBold.HasChar(glyph)) UiKit.ZenBold.RenderRange(0, new Vector2I(30, 0), glyph, glyph);
            yield return ++prepared / (float)total;
            if (UiKit.Zen.HasChar(glyph)) UiKit.Zen.RenderRange(0, new Vector2I(24, 0), glyph, glyph);
            yield return ++prepared / (float)total;
        }
        var art = BulletArt.BossPattern(Job.Magic, 1);
        yield return ++prepared / (float)total;
        foreach (bool charged in new[] { false, true })
        for (int generation = 0; generation < 3; generation++)
        {
            float radius = (charged ? BulletPool.BossChargeRadius : BulletPool.BossRadius) * 1.5f * Mathf.Pow(.62f, generation);
            Bullet.BossFootprint(Job.Magic, radius, charged);
            BulletFootprint.Primitive(Jobs.Get(Job.Magic).ShotShape, radius);
            BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position, Bullet.BossArtSize(radius, charged));
            yield return ++prepared / (float)total;
        }
        ReiRallyVisuals.Prepare();
        yield return ++prepared / (float)total;
        _assetsPrepared = true;
    }

    private Vector2 _anchor, _source, _forward;
    private Vector2[] _points = Array.Empty<Vector2>();
    private double _warning;
    private readonly List<EditedWord> _words = new();
    private readonly List<(Vector2 A, Vector2 B, double At)> _quotations = new();
    private sealed record SweepingCut(Vector2 From, Vector2 To, Vector2 Axis, float HalfLength, double StartsAt, double FiresAt, double EndsAt);
    private readonly List<SweepingCut> _sweepingCuts = new();
    internal IEnumerable<(Vector2 From, Vector2 Axis, float Reach)> VisibleSweepingCuts
        => _sweepingCuts.Where(cut => Time >= cut.StartsAt + .8 && Time < cut.EndsAt)
            .Select(cut => (cut.From, cut.Axis, cut.HalfLength + 5 + Tier));
    private int _amplificationRound;
    private readonly List<Vector2> _amplificationCenters = new();
    private double _amplificationStart;
    private readonly List<(Rect2 Rect, int Hits)> _pages = new();
    private RectangleShape2D? _pageShape;
    private CircleShape2D? _roundShot;
    private int _insertionTurn = -1;
    private float _insertionX, _insertionY, _insertionDirection;
    internal (float X, float Direction) VisibleInsertion => (_insertionX, _insertionDirection);
    private int _firstRetractedSource;
    private readonly int[] _citationFamily = new int[9], _citationParent = new int[3];
    internal int CitationSeal(int node) => _citationFamily[node];
    internal (Rect2 Inner, bool Turned, bool Inked)? VisibleBracket { get; private set; }
    private Vector2 _previousReader;
    private float _previousGate;
    private Rect2 _gateWarningEdge;
    private bool _gateStriking;
    private int _gateLeg = -1;
    private double _rejectedAt = -10;
    private Vector2 _rejectedPosition;
    private readonly Rect2[] _quotationSheets = new Rect2[2];
    internal IReadOnlyList<Rect2> VisibleQuotationSheets => _quotationSheets;
    private Rect2 _cropFrame;
    private int _cropExit, _cropLastExit = -1;
    private bool _cropPassed;
    private Vector2 _cropPrevious, _cropGate;
    private readonly List<(int Edge, Vector2 A, Vector2 B, Vector2 Out)> _cropFilm = new();
    private double _cropFilmEnd, _cropRimAt;
    private float? _retakeCenter;
    internal Vector2 VisibleCropExit => _cropGate;
    internal Vector2 VisibleCropEntry => _cropGate - (_cropGate - _cropGate.Clamp(_cropFrame.Position, _cropFrame.End)).Normalized() * 32;
    private Vector2 _selectionStart, _selectionEnd;
    private Rect2 _selectionRect, _selectionTravel;
    private Vector2 _frontRoot, _frontUp;
    private float _pageStartX, _pageTop, _pageHeight;
    private static readonly Color Ink = new("bda2ef");
    private static readonly Color Original = new("ffe4a2");
    private static readonly Color WarningInk = new("17101e");
    private static readonly Color ReaderInk = new("ffd36b");
    private static readonly Color[] StampInks = { new("ff899e"), new("74ceff") };
    private const double ColorCueGrow = .24;
    private const float StampWidth = 15;
    private int StampCopies => 3 + Tier;
    private float StampPhase(bool upper) => Tier == 3 && !upper ? -StampPitch / 2 : 0;
    private float StampPitch => 39 - Tier * 2;
    private float StampTravelSpeed => 86 + Tier * 9;
    private sealed record ColorWave(Rect2 Gap, double StartsAt, double FiresAt, double EndsAt);
    private readonly List<ColorWave> _colorWaves = new();
    private bool ColorCall => Kind == BossTechniqueKind.Area && Variant == 3;

    internal (Rect2 Gap, double ReadyAt, double FiresAt, double EndsAt, int Index)? ColorCallCue
    {
        get
        {
            for (int i = 0; i < _colorWaves.Count; i++)
            {
                var wave = _colorWaves[i];
                if (Time >= wave.StartsAt + ColorCueGrow && Time < wave.EndsAt)
                    return (wave.Gap, wave.StartsAt + ColorCueGrow, wave.FiresAt, wave.EndsAt, i);
            }
            return null;
        }
    }
    private static Texture2D? _editorialEdge;
    private const double EasyCueGrow = 1.2, EasyCueLead = 3.4;
    private int QuotePulses => (Tier switch { 0 => 3, 1 => 5, 2 => 6, _ => 7 }) + (Evolved ? 1 : 0);
    private double EditorialSpacing => (7 + TargetOpening) / (.9 * Speed);
    private int ParagraphColumns => 3 + Tier / 2;
    private const float ParagraphRailRadius = 3;
    private int FanRows => Tier == 0 ? 4 : 6;
    private double FanRowInterval => Tier switch { 0 => 1.35, 1 => 1.65, 2 => 1.05, _ => .82 };
    private double TriangleInterval => Tier switch { 0 => 4.1, 1 => 2.8, 2 => 2.5, _ => 2 };
    private int Paragraphs => Tier == 0 ? 3 : 4;
    private int QuoteFanSteps => Tier switch { 0 => 1, 2 => 8, _ => Tier + 2 };
    private float SelectionMargin => Tier switch { 0 => 28, 1 => 40, 2 => 56, _ => 32 };
    private float ParagraphHalfGap => 20 - Tier * 2;
    private double ParagraphInterval => Tier switch { 0 => 2.5, 1 => 2.1, 2 => 1.8, _ => 1.6 };
    private double FanWarningStart(int row) => row * FanRowInterval
        + (Tier >= 2 && row >= FanRows / 2 ? Math.Max(0, EasyCueLead + 1.2 - FanRows / 2 * FanRowInterval) : 0);
    private double TriangleWarningStart(int face) => face * TriangleInterval
        + (Tier >= 2 && face >= 2 ? Math.Max(0, EasyCueLead + 3 - 2 * TriangleInterval) : 0);
    private double ParagraphWarningStart(int paragraph) => paragraph * ParagraphInterval
        + (Tier >= 2 && paragraph >= 2 ? 6 - 2 * ParagraphInterval : 0);
    private readonly List<EasyQuotePath> _easyPaths = new();
    private readonly List<EasyEditorialCut> _easyCuts = new();
    private readonly HashSet<int> _easySplitParents = new();
    private readonly Dictionary<double, WarningCanvas> _warningCanvases = new();
    private readonly HashSet<double> _dirtyWarningGroups = new();
    private WarningCanvas _warningCanvas = null!;
    private readonly HashSet<double> _nextWarningGroups = new();
    private Node2D? _editorialFillCanvas;
    private bool _collectWarnings;
    private float _sweepWarningProgress = -1;
    private double _easyEnd;
    private bool _easySelectionBuilt;
    private int _easyParagraphsPrepared, _easyFanRowsPrepared, _easyTriangleFacesPrepared, _easyPagesPrepared;
    private readonly bool[] _peeledEditions = new bool[2];
    private float _peelSpine;
    internal float VisiblePageBinding => _peelSpine;
    internal float? VisibleReviewCrease => _easyCuts.FirstOrDefault(c => c.Key == "page:unpeeled-review:left"
        && Time >= c.StartsAt + EasyCueGrow) is { } trim ? trim.B.X + TargetOpening / 2 : null;
    private bool _peelReviewed;
    internal bool PeelReviewFailed { get; private set; }
    internal IReadOnlyList<bool> PeeledEditions => _peeledEditions;
    internal IEnumerable<(Vector2 Position, Vector2 Velocity)> VisiblePeelTargets => _easyPaths
        .Where(path => path.Peel && path.PageEdition >= 0 && !_peeledEditions[path.PageEdition] && EasyQuoteExists(path))
        .Select(path => (path.Shot!.GlobalPosition, path.Shot.Velocity));
    private int _easyTriangleFirstEdge;
    private readonly HashSet<int> _easyRevisionsPrepared = new();
    private double _easySelectionRecordAt;
    private double _selectionLockedAt;
    private int _selectionMotionEvent = -1;
    internal bool SourceCutTechnique => Kind == BossTechniqueKind.Normal && Variant == 3;
    internal bool RallyTechnique => Kind == BossTechniqueKind.Normal && Variant == 4;
    internal sealed class SourceCutRound
    {
        internal Vector2 From, To, Target;
        internal Vector2 CounterFrom, CounterTo;
        internal double StartsAt, LocksAt, FiresAt, EndsAt;
        internal double CounterStartsAt, CounterFiresAt, CounterEndsAt;
        internal bool Locked, Aligned, Replied;
        internal int FiredPulses, RemovedPanels;
    }
    internal readonly List<SourceCutRound> SourceCutRounds = new();
    internal Vector2 SourceCutAnchor;
    private Vector2[] _sourceCutDirections = Array.Empty<Vector2>();
    private bool _sourceCutBound;
    private int SourceCutCount => Tier >= 2 ? 4 : 3;
    internal float SourceCutWidth => Tier == 3 ? 10 : 8 + Tier * 2;
    internal float SourceCutTargetRadius => Tier >= 2 ? Tier == 2 ? 6 : 4 : 14;
    internal double SourceCutSpacing => Tier >= 2 ? Tier == 2 ? .98 : 1.04 : .42;
    internal double SourceCutLive => Tier >= 2 ? Tier == 2 ? .82 : .9 : .24;
    internal double SourceCutCadence => Tier == 2 ? 2.7 : 2.55;
    private const double SourceCutGrow = .7, SourceCutHold = 2.2;
    private bool SelectionTechnique => Kind == BossTechniqueKind.Normal && Variant == 0;
    private int SelectionStage => Finished ? 4 : !_easySelectionBuilt ? 0
        : Time < _selectionLockedAt + EasyCueLead ? 1
        : Time < _selectionLockedAt + EasyCueLead + 1.4 ? 2 : 3;
    private bool EasyEditorial => (Kind == BossTechniqueKind.Normal && Variant is 0 or 1 or 2
        || Kind == BossTechniqueKind.Area && Variant == 0
        || Kind == BossTechniqueKind.Extra && (!Evolved && Variant == 0 || Evolved && Variant == 1)
        || Kind is BossTechniqueKind.Signature or BossTechniqueKind.Finale);
    protected override bool UsesDangerForecast => true;
    protected override void DrawDangerForecast(Node2D canvas)
    {
        // The editorial pages retain their progressively revealed geometry in WarningMask.
        if (SourceCutTechnique)
        {
            foreach (var h in HazardShapes)
            {
                if (h.Live || h.Progress <= 0) continue;
                if (h.Shape == 0) AttackTelegraph.MaskStroke(canvas, h.A, h.A.Lerp(h.B, h.Progress), h.Radius);
                if (h.Shape == 1) canvas.DrawCircle(h.A, h.Radius, Colors.White);
                if (h.Shape == 3) AttackTelegraph.MaskOutsideDisc(canvas, h.A, h.Radius);
            }
        }
        else if (!EasyEditorial && Kind != BossTechniqueKind.Cameo) base.DrawDangerForecast(canvas);
        if (_gateWarningEdge.HasArea()) canvas.DrawRect(_gateWarningEdge, Colors.White);
        if (Kind == BossTechniqueKind.Crossfire && Variant == 0 && ShowingAmplificationSplit)
        {
            for (int i = 0; i < AmplificationRays; i++)
            {
                var (origin, end, direction, half) = AmplificationForecastRay(i);
                float radius = AmplificationRadius;
                AttackTelegraph.MaskStroke(canvas, origin, end, radius);
                if (half <= .01f) continue;
                Vector2 apex = origin + direction * ((TargetOpening / 2 + radius) / Mathf.Sin(Mathf.Pi / AmplificationRays));
                Vector2 side = direction.Orthogonal() * half;
                canvas.DrawColoredPolygon(new[] { apex, end - side, end + side }, Colors.White);
                AttackTelegraph.MaskStroke(canvas, apex, end - side, radius);
                AttackTelegraph.MaskStroke(canvas, apex, end + side, radius);
                AttackTelegraph.MaskStroke(canvas, end - side, end + side, radius);
            }
        }
        foreach (var cut in _sweepingCuts)
            if (Time >= cut.StartsAt && Time < cut.FiresAt)
            {
                Vector2 span = cut.Axis * cut.HalfLength;
                canvas.DrawColoredPolygon(new[] { cut.From - span, cut.From + span, cut.To + span, cut.To - span }, Colors.White);
                AttackTelegraph.MaskStroke(canvas, cut.From - span, cut.To - span, 5 + Tier);
                AttackTelegraph.MaskStroke(canvas, cut.From + span, cut.To + span, 5 + Tier);
            }
        foreach (var wave in _colorWaves)
        {
            if (Time <= wave.StartsAt || Time >= wave.FiresAt) continue;
            foreach (bool upper in new[] { true, false })
            {
                var band = StampBand(wave.Gap, upper, wave.Gap.Position.X, wave.Gap.Size.X);
                canvas.DrawRect(band.GrowSide(upper ? Godot.Side.Bottom : Godot.Side.Top, StampLip), Colors.White);
            }
        }
    }
    protected override bool UsesCustomHazardArt => EasyEditorial || ColorCall || SourceCutTechnique || RallyTechnique || Kind == BossTechniqueKind.Cameo;
    protected override bool UsesAttackLineArt => !RallyTechnique;
    protected override bool DirectionalWarnings => Kind == BossTechniqueKind.Edge
        || Kind == BossTechniqueKind.Crossfire && Variant is 0 or 2 or 3 or 4;
    protected override Color AttackLineInk => SourceCutTechnique ? new Color("d2a9ff")
        : !UsesCustomHazardArt ? new Color("b59acb")
        : Kind == BossTechniqueKind.Area ? ReaderInk : new Color("ac7de1");
    protected override Texture2D? AttackLineMotif => SourceCutTechnique ? null : _editorialEdge;

    internal int AvatarFrame
    {
        get
        {
            if (RallyTechnique) return RallyAvatarFrame;
            if (SourceCutTechnique)
            {
                double lastSourcePulse = SourceCutRounds.Where(round => round.FiredPulses > 0)
                    .Select(round => round.FiresAt + (round.FiredPulses - 1) * SourceCutSpacing)
                    .DefaultIfEmpty(double.NegativeInfinity).Max();
                double release = Time - lastSourcePulse;
                if (release < Math.Max(.8, SourceCutLive)) return release < .16 ? 4 : release < .32 ? 5 : release < .56 ? 6 : 7;
                var preparing = SourceCutRounds.LastOrDefault(round => Time < round.FiresAt);
                return preparing == null ? -1 : preparing.Locked ? 3
                    : Math.Min(2, (int)((Time - preparing.StartsAt) / .35));
            }
            if (SelectionTechnique)
            {
                if (SelectionStage == 0) return Math.Min(2, (int)((Time - _easySelectionRecordAt) / .4));
                if (SelectionStage == 1) return 3;
                double release = Time - (_selectionLockedAt + EasyCueLead + (SelectionStage == 3 ? 1.4 : 0));
                if (release < .8) return release < .16 ? 4 : release < .32 ? 5 : release < .56 ? 6 : 7;
                if (SelectionStage == 2) return 3;
                return -1;
            }
            double next = double.PositiveInfinity, last = double.NegativeInfinity;
            void Observe(double at)
            {
                if (at > Time) next = Math.Min(next, at);
                else last = Math.Max(last, at);
            }
            foreach (var path in _easyPaths)
                if (path.Parent < 0) Observe(path.FiresAt);
            foreach (var cut in _easyCuts) Observe(cut.FiresAt);
            foreach (var wave in _colorWaves) Observe(wave.FiresAt);
            if (!ColorCall && Kind is BossTechniqueKind.Area or BossTechniqueKind.Finale) Observe(EasyCueLead);
            if (Time - last < .32) return Time - last < .16 ? 4 : 5;
            if (next - Time < .75) return Mathf.Clamp((int)((.75 - (next - Time)) / .1875), 0, 3);
            if (Time - last < .8) return Time - last < .56 ? 6 : 7;
            return -1;
        }
    }

    private sealed class EasyQuotePath
    {
        internal string Key = "";
        internal double StartsAt, FiresAt, ArrivesAt, WarningEndsAt;
        internal double LastChildFire = double.NegativeInfinity;
        internal Vector2 From, To, Scale = Vector2.One;
        internal Vector2[] Points = Array.Empty<Vector2>(), Turns = Array.Empty<Vector2>();
        internal float[] Distances = Array.Empty<float>();
        internal int CurrentLeg = -1;
        internal float HalfWidth, Velocity;
        internal float DrawnProgress = -1;
        internal int Parent = -1, Generation, Hits, EditGroup = -1, PageEdition = -1;
        internal bool Draw, Fired, Waiting, Peel, RailReflection, Synchronized;
        internal Bullet? Shot;
        internal ulong Activation;
    }

    private sealed class EasyEditorialCut
    {
        internal string Key = "";
        internal double StartsAt, FiresAt, EndsAt;
        internal Vector2 A, B;
        internal float HalfWidth;
        internal float DrawnProgress = -1;
        internal bool Box, Fired;
    }
    private float EasyCueProgress(double start) => Mathf.Clamp((float)((Time - start) / EasyCueGrow), 0, 1);
    internal override IEnumerable<BossEasyCue> EasyCues
    {
        get
        {
            if (RallyTechnique)
            {
                yield break;
            }
            if (SourceCutTechnique)
            {
                foreach (var cut in _easyCuts)
                    yield return new(cut.Key, cut.StartsAt, cut.FiresAt,
                        Mathf.Clamp((float)((Time - cut.StartsAt) / SourceCutGrow), 0, 1), cut.Fired);
                yield break;
            }
            if (ColorCall)
            {
                for (int i = 0; i < _colorWaves.Count; i++)
                {
                    var round = _colorWaves[i];
                    yield return new($"color-call:{i}", round.StartsAt, round.FiresAt,
                        Mathf.Clamp((float)((Time - round.StartsAt) / ColorCueGrow), 0, 1), Time >= round.FiresAt);
                }
                yield break;
            }
            if (!EasyEditorial) yield break;
            foreach (var path in _easyPaths)
                yield return new BossEasyCue(path.Key, path.StartsAt, path.FiresAt, EasyCueProgress(path.StartsAt), path.Fired);
            foreach (var cut in _easyCuts)
                yield return new BossEasyCue(cut.Key, cut.StartsAt, cut.FiresAt, EasyCueProgress(cut.StartsAt), cut.Fired);
            if (Kind is BossTechniqueKind.Finale || Kind == BossTechniqueKind.Area)
                yield return new BossEasyCue(Kind == BossTechniqueKind.Finale ? "sentence:sweep" : "reader:sweep",
                    0, EasyCueLead, EasyCueProgress(0), Time >= EasyCueLead);
        }
    }

    protected override IEnumerable<(Vector2 Origin, double StartsAt, double FiresAt)> TelegraphTimings
    {
        get
        {
            if (ColorCall) yield break;
            foreach (var path in _easyPaths.Where(path => path.Draw && !path.Fired && Time < path.WarningEndsAt))
                yield return (path.Points.Length > 0 ? path.Points[0] : path.From, path.StartsAt, path.FiresAt);
            foreach (var cut in _easyCuts.Where(cut => !cut.Fired))
                yield return (cut.A, cut.StartsAt, cut.FiresAt);
            foreach (var cut in _sweepingCuts.Where(cut => Time < cut.FiresAt))
                yield return (cut.From.Clamp(Field.Rect.Position, Field.Rect.End), cut.StartsAt, cut.FiresAt);
            if (Kind is BossTechniqueKind.Area or BossTechniqueKind.Finale)
                if (Kind != BossTechniqueKind.Area || Variant != 2 || Tier < 2)
                    yield return (new Vector2(Field.Left + 4, Field.Top + 4), 0, EasyCueLead);
        }
    }

    private sealed class EditedWord
    {
        internal Bullet Bullet = null!;
        internal ulong Id;
        internal Vector2 Start, Direction;
        internal double Ready;
        internal int Stage;
    }

    private float Speed => (Evolved ? 72 : 62) * GameManager.Instance!.BulletSpeedMul;
    private int Copies => Tier switch { 0 => 2, 1 => 3, 2 => 4, _ => 5 };
    private double SentenceDuration => Tier switch { 3 => 6.8, 2 => 7.5, _ => 8.8 };
    private float FrontSpeed => Tier switch { 0 => 36, 1 => 42, 2 => 47, _ => 52 };
    private float FrontBranchSpeed => 32 + Tier * 6;
    private Vector2 _echoPreviousCenter;
    private int _frontRound;
    private double _frontRoundStarted;
    private double FrontRoundDuration => _warning + (Field.Width + 24) / FrontSpeed + FrontBranchLength / FrontBranchSpeed + .4;
    private int FrontBranches => Tier >= 2 ? 4 + Tier * 2 : Copies + 1;
    private float FrontBranchDistance(int index, float length)
    {
        float inset = Tier >= 2 ? 12 : length * .22f;
        return Mathf.Lerp(inset, length - inset, index / (float)(FrontBranches - 1));
    }
    private float FrontBranchLength => Tier >= 2
        ? Math.Max(Field.Bottom + 8 - _frontRoot.Y, _frontRoot.Y - Field.Top + 8)
        : Mathf.Min(136 + Tier * 8, _frontUp.Y > 0 ? Field.Bottom - 8 - _frontRoot.Y : _frontRoot.Y - 8);
    private float PageSpeed => 26 + Tier * 6;
    private double PageSweepEnd => _warning + (Field.Width + 2) / PageSpeed;
    private float SideX => _forward.X < 0 ? Field.Right - 10 : Field.Left + 10;
    private Vector2 Side(float y) => new(SideX, y);
    private Vector2 Across(float distance, float y) => Side(y) + _forward * distance;
    private static Vector2 Inside(Vector2 p, float margin = 24) => p.Clamp(
        new Vector2(Field.Left + margin, margin), new Vector2(Field.Right - margin, Field.Bottom - margin));

    protected override void Start()
    {
        if (RallyTechnique)
        {
            _editorialEdge ??= GD.Load<Texture2D>("res://char/v3/fx/rei/frame_edge.png");
            Identify("最後まで、返して", "rei-five-return-rally", "左の帯で赤い棘球を避け、緑の球の輪が光ったら溜め撃ちで右へ返す。壁へ5連続で当てると破壊、"
                + (Tier >= 2 ? "3ミスなら最後に逃した球の高さへ移り、壁の反撃をかわす" : "3ミスで終了"));
            return;
        }
        if (SourceCutTechnique) { StartSourceCut(); return; }
        if (ColorCall) { StartColorCall(); return; }
        _anchor = Inside(Player.GlobalPosition, 36);
        _forward = _anchor.X < Field.CenterX ? Vector2.Left : Vector2.Right;
        _source = Side(Mathf.Clamp(_anchor.Y, 48, Field.Bottom - 48));
        _warning = WarnFor(70, Tier == 0 ? 1.7 : 1.35);
        if (EasyEditorial) { StartEasyEditorial(); return; }
        switch (Kind)
        {
            case BossTechniqueKind.Normal when Variant == 1:
                Identify("引用の扇", "rei-parent-child-angle", "親弾の正面を避け、開いた扇の内側へ入る");
                _warning = WarnFor(48, 1.4);
                break;
            case BossTechniqueKind.Normal:
                Identify("反響の万華鏡", "rei-triangle-subdivision", "次に分割される三角形の辺を読んで、小部屋の内側へ");
                _points = new[] { new Vector2(Field.CenterX, 25), new Vector2(Field.Left + 35, 185), new Vector2(Field.Right - 35, 185) };
                _warning = WarnFor(58, 1.6);
                break;
            case BossTechniqueKind.Edge:
                Identify("一方向の引用", "rei-directed-reading-gate", "読み順の矢印へ先回りする。戻る文字をかわし、逆向きに境界をまたがない");
                _warning = WarnFor(78, 2.5);
                _previousReader = Player.GlobalPosition;
                break;
            case BossTechniqueKind.Area when Variant == 0:
                Identify("コメント一斉読み", "rei-reading-cursor", "読み終えた行へ移り、下へ進む読み取り線を追い越さない");
                float toTop = Mathf.Max(0, Player.GlobalPosition.Y - 20);
                float toEnd = Mathf.Min(Player.GlobalPosition.X - Field.Left, Field.Right - Player.GlobalPosition.X);
                _warning = WarnFor(Mathf.Min(toTop, toEnd) + 24, 1.8);
                break;
            case BossTechniqueKind.Area when Variant == 1:
                Identify("配信枠", "rei-crop-three-edges", "点の少ない順に閉じる枠から、最後の開いた辺を通って出る");
                _warning = WarnFor(94, 2.0);
                {
                    Vector2 center = Player.GlobalPosition.Clamp(new Vector2(Field.Left + 66, Field.Top + 50),
                        new Vector2(Field.Right - 66, Field.Bottom - 50));
                    _cropFrame = new Rect2(center - new Vector2(66, 50), new Vector2(132, 100));
                    _cropPrevious = Player.GlobalPosition;
                    var corners = new[] { _cropFrame.Position, new Vector2(_cropFrame.End.X, _cropFrame.Position.Y),
                        _cropFrame.End, new Vector2(_cropFrame.Position.X, _cropFrame.End.Y) };
                    using var core = new CircleShape2D { Radius = Player.HitRadius + 3 };
                    var bodies = Boss.GetChildren().OfType<CollisionShape2D>().Where(shape => !shape.Disabled).ToArray();
                    var gates = Enumerable.Range(0, 4).SelectMany(edge => Enumerable.Range(1, 5).Select(part => {
                        Vector2 onEdge = corners[edge].Lerp(corners[(edge + 1) % 4], part / 6f);
                        Vector2 outward = (corners[edge].Lerp(corners[(edge + 1) % 4], .5f) - center).Normalized();
                        return (Edge: edge, OnEdge: onEdge, Target: onEdge + outward * 16);
                    })).Where(gate => Field.Rect.Grow(-4).HasPoint(gate.Target)
                        && Enumerable.Range(0, 9).All(sample => bodies.All(shape => !shape.Shape.Collide(shape.GlobalTransform,
                            core, new Transform2D(0, (gate.OnEdge * 2 - gate.Target).Lerp(gate.Target, sample / 8f))))))
                        .GroupBy(gate => gate.Edge).ToArray();
                    var gate = gates[GD.RandRange(0, gates.Length - 1)]
                        .OrderBy(candidate => candidate.Target.DistanceSquaredTo(Player.GlobalPosition)).First();
                    _cropExit = gate.Edge;
                    _cropGate = gate.Target;
                    _warning = WarnFor(200, 4.5);
                    {
                        _cropRimAt = _warning;
                        PrepareCropFilm(corners);
                    }
                    Identify("配信枠", "rei-crop-three-edges", "最後の辺に残る切れ目から外へ。飛び出すフィルムの間を抜ける。撮り直しではシャッターの隙間へ");
                }
                break;
            case BossTechniqueKind.Area:
                _anchor = Player.GlobalPosition;
                _warning = WarnFor(Field.Width + 12, 2.8);
                Identify("切り抜きの線", "rei-finite-cut-endpoint", "進んでくる切断線の端を回る。次の切り抜きの向きと通る端を読む");
                break;
            case BossTechniqueKind.Signature:
                Identify("星翼・追い越す流星", "rei-edit-three-destinations", "点の少ない順に編集点をたどり、最後の出口からの分裂を読む");
                _points = new[] { Across(28, 42), Across(88, 164), Across(12, 108) };
                _warning = WarnFor(64, 1.6);
                break;
            case BossTechniqueKind.Finale:
                Identify("最後まで見てて", "rei-continuous-sentence", "一文の開いていく余白をたどり、読み終わった場所に留まらない");
                _warning = WarnFor(Mathf.Abs(Player.GlobalPosition.Y - 108), 2.2);
                break;
            case BossTechniqueKind.Crossfire when Variant == 0:
                Identify("増幅する反響", "rei-merge-then-branch", "合稿した引用が境界になり、二つに開いて言葉を放つ。開く方向と、次の合稿位置を読む");
                _points = new[] { new Vector2(Field.CenterX, Mathf.Clamp(_anchor.Y, 62, 154)) };
                if (Tier >= 2) _points[0] = Player.GlobalPosition;
                _echoPreviousCenter = _points[0];
                _warning = WarnFor(70, Tier == 0 ? 2.8 : 1.5);
                break;
            case BossTechniqueKind.Crossfire when Variant == 1:
                Identify("割り込む引用", "rei-insert-displaces-suffix", "挿入記号より後ろの引用だけが押される。末尾の行き先を空ける");
                _warning = WarnFor(52, 1.6);
                if (Tier >= 2) _warning = WarnFor(190, 4.4);
                break;
            case BossTechniqueKind.Crossfire when Variant == 2:
                Identify("拡散する言葉", "rei-branch-front-tip", "枝分かれが届く前に、広がる前線の端を回る");
                _frontRoot = new Vector2(_forward.X < 0 ? Field.Right + 12 : Field.Left - 12, Player.GlobalPosition.Y);
                _frontUp = _frontRoot.Y < Field.CenterY ? Vector2.Down : Vector2.Up;
                _warning = WarnFor(52, 1.6);
                break;
            case BossTechniqueKind.Crossfire when Variant == 3:
                Identify("編み込まれた噂", "rei-opaque-quotation-layers", "手前の原稿が奥の文字を隠す。見えている文字だけを避ける");
                _warning = WarnFor(145, 3.4);
                if (Tier >= 2) _warning = WarnFor(240, 5.2);
                break;
            case BossTechniqueKind.Crossfire:
                Identify("万華のリポスト", "rei-trail-quotation", "原稿が通った線は遅れて再点灯する。すぐ後ろへ戻らない");
                _warning = WarnFor(54, Tier == 0 ? 2.8 : 1.5);
                break;
            case BossTechniqueKind.Extra when !Evolved && Variant == 0:
                Identify("引用の段落", "rei-paragraph-enter-after", "段落の外で二度の展開を待ち、空いた中心へ入る");
                _warning = WarnFor(70, 1.8);
                _source.Y = Mathf.Clamp(_anchor.Y, 72, 144);
                break;
            case BossTechniqueKind.Extra when !Evolved:
                Identify("切り抜きの継ぎ目", "rei-seam-cross-before", "つながる前の継ぎ目を抜ける。貼り合わさった場所から横の継ぎ目が開く");
                _warning = WarnFor(78, 1.8);
                if (Tier >= 2) { _anchor = Player.GlobalPosition; _warning = WarnFor(200, 4.5); }
                break;
            case BossTechniqueKind.Extra when Variant == 0:
                Identify("星翼・分かれる頁", "rei-optional-quote-peeling", "流れてくる頁を撃って剥がすか、上下の端を回って通り抜ける");
                _pageHeight = Tier switch { 0 => 44, 1 => 52, 2 => 60, _ => 64 };
                _pageStartX = SideX;
                _pageTop = Mathf.Clamp(Player.GlobalPosition.Y - _pageHeight * .5f, 10, Field.Bottom - 10 - _pageHeight * 2);
                _warning = WarnFor(_pageHeight + 14, 1.8);
                for (int page = 0; page < 2; page++)
                    _pages.Add((new Rect2(_pageStartX - 8, _pageTop + page * _pageHeight, 16, _pageHeight), 0));
                _pageShape = new RectangleShape2D { Size = _pages[0].Rect.Size };
                _roundShot = new CircleShape2D();
                break;
            case BossTechniqueKind.Extra:
                Identify("星翼・書き直した結末", "rei-second-generation-edit", "一度目の分裂後に示される訂正線を読み、二度目の進路を避ける");
                _warning = WarnFor(52, 1.6);
                break;
            case BossTechniqueKind.Cameo when Variant == 0:
                Identify("引用の連鎖", "rei-citation-dependency-erasure", "斜線の付いた引用元と同じ形の印を線でたどる。その引用がほどけた区画へ、次の掲載までに移る");
                _firstRetractedSource = GD.RandRange(0, 2);
                int shift = GD.RandRange(0, 2), order = GD.RandRange(0, 1) == 0 ? 1 : -1;
                for (int node = 0; node < _citationFamily.Length; node++)
                {
                    int family = Mathf.PosMod(node / 3 + order * (node % 3) + shift, 3);
                    _citationFamily[node] = family;
                    if (node / 3 == 1) _citationParent[family] = node;
                }
                _warning = WarnFor(Field.Width * 2 / 3 + 8, 2);
                break;
            case BossTechniqueKind.Cameo:
                Identify("引用の裏返し", "rei-negated-quotation", "表では括弧の内側、裏では外側が打ち消される。反転する間に好きな辺をまたぐ");
                _warning = WarnFor(142, 2.8);
                break;
        }
    }

    protected override void Step(double delta)
    {
        if (RallyTechnique) { StepRally(delta); return; }
        if (SourceCutTechnique) { StepSourceCut(); return; }
        if (ColorCall) { StepColorCall(); return; }
        if (EasyEditorial) { StepEasyEditorial(delta); return; }
        switch (Kind)
        {
            case BossTechniqueKind.Normal: if (Variant == 1) QuotedFan(); else Kaleidoscope(); break;
            case BossTechniqueKind.Edge: ReadingGate(); break;
            case BossTechniqueKind.Area: if (Variant == 0) ReadComments(); else if (Variant == 1) StreamFrame(); else ClipLine(); break;
            case BossTechniqueKind.Signature: StarEditing(); break;
            case BossTechniqueKind.Finale: LastSentence(); break;
            case BossTechniqueKind.Crossfire:
                if (Variant == 0) Amplification(); else if (Variant == 1) InsertQuotation();
                else if (Variant == 2) SpreadingFront(); else if (Variant == 3) BraidedRumour(); else RepostTrail();
                break;
            case BossTechniqueKind.Extra:
                if (!Evolved) { if (Variant == 0) Paragraph(); else Splice(); }
                else { if (Variant == 0) PeelPage(); else RewriteEnding(); }
                break;
            case BossTechniqueKind.Cameo: if (Variant == 0) QuoteChain(); else QuotationReversal(); break;
        }
    }

    private void StartEasyEditorial()
    {
        TextureFilter = TextureFilterEnum.Linear;
        _editorialEdge ??= GD.Load<Texture2D>("res://char/v3/fx/rei/frame_edge.png");
        _ = BulletArt.Get("rei_film");
        if (Kind == BossTechniqueKind.Finale || Kind == BossTechniqueKind.Normal && Variant == 0
            || Kind == BossTechniqueKind.Signature)
        {
            _editorialFillCanvas = new Node2D { Name = "EditorialFill", ZIndex = UsesDangerForecast ? 0 : -2, ZAsRelative = false, UseParentMaterial = true };
            AddChild(_editorialFillCanvas);
            _editorialFillCanvas.Draw += () =>
            {
                if (Finished) return;
                foreach (var h in HazardShapes)
                    if (h.Live && h.Shape == 2) DrawEditorialFilm(_editorialFillCanvas, new Rect2(h.A, h.B - h.A));
            };
        }
        _warning = EasyCueLead;
        switch (Kind)
        {
            case BossTechniqueKind.Normal when Variant == 0:
                Identify("引用範囲の選択", "rei-movement-earned-quotation", "記録中に原稿をなぞる。引いた向きに紙が残るので、横断する切れ目と分かれる引用を枠内でかわす");
                BeginEasySelectionRecording();
                break;
            case BossTechniqueKind.Normal when Variant == 1:
                Identify("引用の扇", "rei-parent-child-angle", Tier == 2 ? "折り返す注釈の列を読み、次に広がる引用の隙間へ移る"
                    : Tier >= 2 ? "弧を描く三つの引用先を読み、再分裂する前に次の余白へ移る" : "原文の到着点を空け、三つに分かれる引用の間へ入る");
                PrepareEasyQuotationFan();
                break;
            case BossTechniqueKind.Normal:
                Identify("反響の万華鏡", "rei-triangle-subdivision", "四方の辺から中心へ届く原文を見て、分割された三角形の部屋を移る");
                PrepareEasyKaleidoscope();
                break;
            case BossTechniqueKind.Area:
                Identify("コメント一斉読み", "rei-reading-cursor-quotation", "スクロールするコメント欄を下へ追う。読取り線の切れ目を渡り、読み終えた行から戻る返信を外す");
                BuildEasyReader();
                break;
            case BossTechniqueKind.Extra when !Evolved:
                Identify("引用の段落", "rei-paragraph-inner-reflection", "段落の棒の内側を反射する引用を避け、行の切れ目から隣へ渡る");
                while (_easyParagraphsPrepared < (Tier >= 2 ? 2 : Paragraphs)) PrepareEasyParagraph();
                int columns = ParagraphColumns;
                for (int rail = 0; rail <= columns; rail++)
                {
                    float x = Field.Left + 6 + (Field.Width - 12) * rail / columns;
                    AddEasyCut($"paragraph:rail:{rail}:upper", new Vector2(x, -8),
                        new Vector2(x, Math.Max(-8, Player.GlobalPosition.Y - ParagraphHalfGap)), ParagraphRailRadius, 0, EasyCueLead, _easyEnd);
                    AddEasyCut($"paragraph:rail:{rail}:lower", new Vector2(x, Math.Min(Field.Bottom + 8, Player.GlobalPosition.Y + ParagraphHalfGap)),
                        new Vector2(x, Field.Bottom + 8), ParagraphRailRadius, 0, EasyCueLead, _easyEnd);
                }
                break;
            case BossTechniqueKind.Extra:
                Identify("星翼・書き直した結末", "rei-second-generation-edit", "分かれた文章が止まる編集点から離れ、訂正後の引用を読む");
                BuildEasyRewrite();
                break;
            case BossTechniqueKind.Signature:
                Identify("星翼・追い越す流星", "rei-edited-page-peeling", "届く頁の高さに合わせて撃ち、紙を剥がす。開いた見開きから次の版を待つ");
                if (Tier >= 2) Identify(SpellName, MechanicKey, "二つの版をそれぞれの高さで撃って剥がす。未編集の頁が残ったら、最後の返信が抜ける間にその版の折り目へ渡る");
                BuildEasyEditedPage();
                break;
            case BossTechniqueKind.Finale:
                Identify("最後まで見てて", "rei-continuous-sentence", "先に示された一文の余白を進み、読み終えた場所から離れる");
                _easyEnd = EasyCueLead + EasySentenceDuration;
                break;
        }
    }

    private static Vector2 EasyExit(Vector2 from, Vector2 direction)
    {
        direction = direction.Normalized();
        var bounds = Field.Rect.Grow(28);
        float distance = float.MaxValue;
        if (direction.X > .001f) distance = Mathf.Min(distance, (bounds.End.X - from.X) / direction.X);
        if (direction.X < -.001f) distance = Mathf.Min(distance, (bounds.Position.X - from.X) / direction.X);
        if (direction.Y > .001f) distance = Mathf.Min(distance, (bounds.End.Y - from.Y) / direction.Y);
        if (direction.Y < -.001f) distance = Mathf.Min(distance, (bounds.Position.Y - from.Y) / direction.Y);
        return from + direction * distance;
    }

    private int AddEasyQuote(string key, Vector2 from, Vector2 to, double start, double fire,
        double warningEnd, bool draw, int parent = -1, bool peel = false)
    {
        int generation = parent < 0 ? 0 : _easyPaths[parent].Generation + 1;
        float radius = (Evolved ? BulletPool.BossChargeRadius : BulletPool.BossRadius) * 1.5f * Mathf.Pow(.62f, generation);
        var art = BulletArt.BossPattern(Job.Magic, 1);
        float halfWidth = BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position,
            Bullet.BossArtSize(radius, Evolved)).Radius;
        Vector2 scale = peel ? new Vector2(2.2f, 3) : Vector2.One;
        if (Tier == 1) scale *= Mathf.Pow(1.1f, generation);
        float speed = Speed * Mathf.Pow(.9f, generation);
        var path = new EasyQuotePath
        {
            Key = key, From = from, To = to, StartsAt = start, FiresAt = fire, WarningEndsAt = warningEnd,
            Velocity = speed, ArrivesAt = fire + from.DistanceTo(to) / speed, HalfWidth = halfWidth * Mathf.Max(scale.X, scale.Y),
            Parent = parent, Generation = generation, Draw = draw, Peel = peel, Scale = scale,
        };
        _easyPaths.Add(path);
        if (parent >= 0) _easyPaths[parent].LastChildFire = Math.Max(_easyPaths[parent].LastChildFire, fire);
        _easyEnd = Math.Max(_easyEnd, path.ArrivesAt + .1);
        return _easyPaths.Count - 1;
    }

    private static void FillHardFan(List<Vector2> rays, Vector2 origin, Vector2 a, Vector2 b, float spacing)
    {
        // Both spokes bound a passage only as long as both are on the playfield.
        Vector2 first = origin.DirectionTo(a), last = origin.DirectionTo(b);
        if (first == Vector2.Zero || last == Vector2.Zero) return;
        float angle = first.AngleTo(last);
        float reach = Math.Min(origin.DistanceTo(EasyExit(origin, first)), origin.DistanceTo(EasyExit(origin, last)));
        if (2 * reach * Math.Abs(Mathf.Sin(angle * .5f)) <= spacing) return;
        Vector2 middle = EasyExit(origin, first.Rotated(angle * .5f));
        FillHardFan(rays, origin, a, middle, spacing);
        rays.Add(middle);
        FillHardFan(rays, origin, middle, b, spacing);
    }

    private int[] AddEasyFan(int parent, string key, Vector2[] destinations, double? fire = null, double? start = null,
        List<int>? principal = null, float hardSpacing = HardOpening + 4)
    {
        var root = _easyPaths[parent];
        var indices = new List<int>();
        if (Tier > 0)
        {
            int branches = Tier == 1 ? 1 : QuoteFanSteps - 1;
            var spread = new List<Vector2>();
            for (int ray = 0; ray < destinations.Length; ray++)
            {
                indices.Add(spread.Count);
                spread.Add(destinations[ray]);
                if (ray + 1 == destinations.Length) continue;
                if (Tier == 2)
                {
                    FillHardFan(spread, root.To, destinations[ray], destinations[ray + 1], hardSpacing);
                    continue;
                }
                for (int branch = 1; branch <= branches; branch++)
                {
                    Vector2 between = destinations[ray].Lerp(destinations[ray + 1], branch / (float)(branches + 1));
                    if (between.DistanceTo(root.To) > 1) spread.Add(between);
                }
            }
            destinations = spread.ToArray();
        }
        var children = new int[destinations.Length];
        for (int child = 0; child < destinations.Length; child++)
        {
            Vector2 end = EasyExit(root.To, root.To.DirectionTo(destinations[child]));
            children[child] = AddEasyQuote($"{key}:{child}", root.To, end, start ?? root.StartsAt, fire ?? root.ArrivesAt,
                start.HasValue ? fire!.Value : root.WarningEndsAt, root.Draw, parent);
        }
        if (principal != null)
            principal.AddRange(Tier == 0 ? children : indices.Select(i => children[i]));
        return children;
    }

    private static Vector2[] QuoteCurve(Vector2 from, Vector2 control, Vector2 to)
    {
        return Enumerable.Range(0, 17).Select(step => {
            float t = step / 16f;
            return from * ((1 - t) * (1 - t)) + control * (2 * t * (1 - t)) + to * (t * t);
        }).ToArray();
    }

    private void SetQuoteRoute(int index, Vector2[] points, bool corners = false, bool rails = false)
    {
        var path = _easyPaths[index];
        path.Points = points;
        path.To = points[^1];
        path.Turns = corners ? points.Skip(1).SkipLast(1).ToArray() : Array.Empty<Vector2>();
        path.RailReflection = rails;
        path.Distances = new float[points.Length];
        for (int point = 1; point < points.Length; point++)
            path.Distances[point] = path.Distances[point - 1] + points[point - 1].DistanceTo(points[point]);
        path.ArrivesAt = path.FiresAt + path.Distances[^1] / path.Velocity;
        _easyEnd = Math.Max(_easyEnd, path.ArrivesAt + .1);
    }

    private void KeepRequotedWidth(int index)
    {
        // An added quotation stage must not shrink the existing fan's collision width.
        var path = _easyPaths[index];
        path.Scale /= .62f;
        path.HalfWidth /= .62f;
    }

    private void SetSynchronizedQuote(int index, Vector2[] points, double duration)
    {
        SetQuoteRoute(index, points);
        var path = _easyPaths[index];
        path.Synchronized = true;
        path.ArrivesAt = path.FiresAt + duration;
        path.Velocity = path.Distances[^1] / (float)duration;
        _easyEnd = Math.Max(_easyEnd, path.ArrivesAt + .1);
    }

    private void AddEasyCut(string key, Vector2 a, Vector2 b, float radius, double start, double fire, double end, bool box = false)
    {
        _easyCuts.Add(new EasyEditorialCut { Key = key, A = a, B = b, HalfWidth = radius,
            StartsAt = start, FiresAt = fire, EndsAt = end, Box = box });
        _easyEnd = Math.Max(_easyEnd, end);
    }

    private void BeginEasySelectionRecording()
    {
        _easySelectionRecordAt = Time;
        _selectionStart = _selectionEnd = Player.GlobalPosition;
        _selectionTravel = new Rect2(_selectionStart, Vector2.Zero);
        _selectionRect = SelectionBounds();
    }

    private Rect2 SelectionBounds()
    {
        var margin = new Vector2(SelectionMargin, Tier == 3 ? 56 : SelectionMargin);
        Vector2 size = (_selectionTravel.Size + margin * 2).Min(Field.Rect.Size);
        Vector2 position = (_selectionTravel.Position - margin)
            .Clamp(Field.Rect.Position, Field.Rect.End - size);
        return new Rect2(position, size);
    }

    private void BuildEasySelectedFrame()
    {
        _easySelectionBuilt = true;
        _selectionLockedAt = Time;
        Audio.Instance?.Se(Audio.Instance.SfxUiMove, -20, 1.25f);
        Vector2 center = _selectionRect.GetCenter();
        Vector2 written = _selectionEnd - _selectionStart;
        bool longHorizontal = written.Length() >= 20 && Math.Abs(written.X) > 2 * Math.Abs(written.Y);
        double start = Time, fire = start + EasyCueLead;
        const string key = "selection";
        double cut = fire + 1.4;
        if (_selectionStart.DistanceTo(_selectionEnd) >= 20)
        {
            Vector2 stroke = _selectionStart.DirectionTo(_selectionEnd);
            Vector2 across = stroke.Orthogonal();
            float cover = Field.Rect.Size.Length() + TargetOpening;
            foreach (int side in new[] { -1, 1 })
            {
                Vector2 edge = _selectionStart.Lerp(_selectionEnd, .5f) + across * (side * (cover + TargetOpening / 2));
                AddEasyCut($"{key}:trim:stroke:{side}", edge - stroke * cover, edge + stroke * cover,
                    cover, start, fire, fire + .55);
            }
        }
        else
        {
            float toward = _selectionEnd.X < center.X ? 1 : -1;
            float seam = Mathf.Clamp(_selectionEnd.X + toward * 12, _selectionRect.Position.X + 9, _selectionRect.End.X - 9);
            AddEasyCut($"{key}:trim:left", _selectionRect.Position, new Vector2(seam - TargetOpening / 2, _selectionRect.End.Y),
                0, start, fire, fire + .55, true);
            AddEasyCut($"{key}:trim:right", new Vector2(seam + TargetOpening / 2, _selectionRect.Position.Y), _selectionRect.End,
                0, start, fire, fire + .55, true);
        }
        Vector2 cutAcross = _selectionStart.DistanceTo(_selectionEnd) >= 20
            ? _selectionStart.DirectionTo(_selectionEnd).Orthogonal() : Vector2.Right;
        Vector2 halfCut = cutAcross * Field.Rect.Size.Length();
        AddEasyCut($"{key}:cut", _selectionEnd - halfCut, _selectionEnd + halfCut, 3, cut - EasyCueLead, cut, cut + .55);
        for (int half = 0; half < 2; half++)
            for (int pulse = 0; pulse < QuotePulses; pulse++)
            {
                Vector2 direction = half == 0 ? Vector2.Left : Vector2.Right;
                Vector2 fork = Inside(new Vector2(Field.Left + Field.Width * (half == 0 ? .25f : .75f),
                    center.Y + (half == 0 ? -26 : 26)), 24);
                if (center.DistanceTo(fork) < 40)
                    fork.Y = Mathf.Clamp(center.Y + (center.Y < Field.CenterY ? 56 : -56), 24, Field.Bottom - 24);
                int root = AddEasyQuote($"{key}:{half}:{pulse}:parent", center, fork, cut - EasyCueLead,
                    cut + pulse * EditorialSpacing, cut, pulse == 0);
                float opposite = half == 0 ? Field.Right + 28 : Field.Left - 28;
                Vector2 reserved = _selectionEnd == fork ? fork + direction : _selectionEnd;
                var quotes = AddEasyFan(root, $"{key}:{half}:{pulse}:quote", new[] {
                    new Vector2(opposite, -12), reserved, new Vector2(opposite, Field.Bottom + 12) },
                    hardSpacing: longHorizontal ? 30 : HardOpening + 4);
                if (Tier >= 2)
                    for (int branch = 0; branch < quotes.Length; branch++)
                    {
                        var path = _easyPaths[quotes[branch]];
                        Vector2 bend = path.From.Lerp(path.To, .48f) + Vector2.Down * (half == 0 ? 44 : -44);
                        SetQuoteRoute(quotes[branch], QuoteCurve(path.From, Inside(bend, 14), path.To));
                    }
            }
        foreach (var path in _easyPaths)
        {
            var shared = _easyPaths.Where(other => other.From == path.From && other.To == path.To
                && other.Generation == path.Generation);
            if (path.Parent >= 0) path.StartsAt = shared.Min(other => other.FiresAt) - EasyCueLead;
            path.WarningEndsAt = shared.Max(other => other.FiresAt);
        }
        var bounds = _selectionRect;
        var outside = new[] {
            new Rect2(Field.Left, Field.Top, Field.Width, bounds.Position.Y - Field.Top),
            new Rect2(Field.Left, bounds.End.Y, Field.Width, Field.Bottom - bounds.End.Y),
            new Rect2(Field.Left, bounds.Position.Y, bounds.Position.X - Field.Left, bounds.Size.Y),
            new Rect2(bounds.End.X, bounds.Position.Y, Field.Right - bounds.End.X, bounds.Size.Y),
        };
        for (int side = 0; side < outside.Length; side++)
            if (outside[side].HasArea())
                AddEasyCut($"{key}:outside:{side}", outside[side].Position, outside[side].End, 0,
                    start, fire, _easyEnd, true);
    }

    private void PrepareEasyQuotationFan()
    {
        int row = _easyFanRowsPrepared++;
        Vector2 target = Player.GlobalPosition;
        float y = new[] { 10f, 76f, 140f, 206f }[row % 4];
        bool right = row % 2 == 0;
        Vector2 from = new(right ? Field.Left : Field.Right, y);
        Vector2 forward = right ? Vector2.Right : Vector2.Left;
        Vector2 fork = from + forward * 76;
        if (fork == target) target = fork + forward;
        double start = FanWarningStart(row), first = start + EasyCueLead;
        var profile = GD.Load<Curve2D>("res://assets/attack_curves/rei_quotation.tres");
        var quotationRoute = AttackCurveRoute.Between(profile, from, fork,
            (y < Field.CenterY ? 1 : -1) * (right ? 1 : -1));
        int copies = QuotePulses;
        for (int pulse = 0; pulse < copies; pulse++)
        {
            int root = AddEasyQuote($"fan:{row}:{pulse}:parent", from, fork, start,
                first + pulse * EditorialSpacing, first, pulse == 0);
            SetQuoteRoute(root, quotationRoute.Points);
            float farX = right ? Field.Right + 28 : Field.Left - 28;
            if (Tier == 2)
            {
                int rows = 5 + Tier;
                double annotate = (fork.DistanceTo(Inside(new Vector2(fork.X + forward.X * 66, target.Y), 18)) + 60) / (Speed * .9f);
                for (int note = 0; note < rows; note++)
                {
                    float u = (note + .5f) / rows, offset = 2 * u - 1;
                    Vector2 bookmark = Inside(new Vector2(fork.X + forward.X * 66, target.Y + offset * 62), 18);
                    Vector2 hook = Inside(fork - forward * 24 + Vector2.Down * offset * 86, 12);
                    int annotation = AddEasyQuote($"fan:{row}:{pulse}:annotation:{note}", fork, bookmark,
                        start, _easyPaths[root].ArrivesAt, first, pulse == 0, root);
                    SetSynchronizedQuote(annotation, QuoteCurve(fork, hook, bookmark), annotate);
                    Vector2 end = new(farX, Field.Top + u * Field.Height);
                    if (note == rows / 2 && bookmark.DistanceSquaredTo(target) > 1)
                        end = EasyExit(bookmark, bookmark.DirectionTo(target));
                    int quoted = AddEasyQuote($"fan:{row}:{pulse}:requote:{note}", bookmark, end, start,
                        _easyPaths[annotation].ArrivesAt, first, pulse == 0, annotation);
                    KeepRequotedWidth(quoted);
                    SetSynchronizedQuote(quoted, new[] { bookmark, end }, bookmark.DistanceTo(end) / (Speed * .81f));
                }
            }
            else if (Tier < 2)
            {
                float forwardDistance = Math.Max(48, (target.X - fork.X) * forward.X);
                Vector2 quote = new(fork.X + forward.X * forwardDistance, target.Y);
                Vector2 aim = fork.DirectionTo(quote);
                float reach = fork.DistanceTo(EasyExit(fork, aim));
                float angle = 2 * Mathf.Asin(Math.Min(1, (TargetOpening + 6) / (2 * reach)));
                var fan = new[] { aim.Rotated(-angle), aim, aim.Rotated(angle) };
                for (int ray = 0; ray < fan.Length; ray++)
                {
                    string key = $"fan:{row}:{pulse}:quote:{ray}";
                    AddEasyQuote(key, fork, EasyExit(fork, fan[ray]), start, _easyPaths[root].ArrivesAt, first, pulse == 0, root);
                }
            }
            else
            {
                int nextRay = 0;
                for (int note = 0; note < 3; note++)
                {
                    Vector2 bookmark = Inside(new Vector2(fork.X + forward.X * 66, target.Y + (note - 1) * 62), 18);
                    var original = _easyPaths[root];
                    int annotation = AddEasyQuote($"fan:{row}:{pulse}:annotation:{note}", fork, bookmark,
                        start, original.ArrivesAt, first, pulse == 0, root);
                    Vector2 hook = Inside(fork - forward * 24 + Vector2.Down * (note - 1) * 86, 12);
                    SetQuoteRoute(annotation, QuoteCurve(fork, hook, bookmark));
                    int rayCount = QuoteFanSteps * 2 + 1;
                    int firstRay = note * rayCount / 3, lastRay = (note + 1) * rayCount / 3;
                    var ends = new List<Vector2>();
                    for (int ray = firstRay; ray < lastRay; ray++)
                    {
                        float amount = ray / (float)QuoteFanSteps;
                        Vector2 destination = amount <= 1 ? new Vector2(farX, -8).Lerp(target, amount)
                            : target.Lerp(new Vector2(farX, Field.Bottom + 8), amount - 1);
                        if (destination.DistanceSquaredTo(bookmark) < 1) destination += forward;
                        Vector2 end = EasyExit(bookmark, bookmark.DirectionTo(destination));
                        ends.Add(end);
                    }
                    for (int ray = 0; ray < ends.Count; ray++)
                    {
                        Vector2 end = ends[ray];
                        int quoted = AddEasyQuote($"fan:{row}:{pulse}:requote:{nextRay++}", bookmark, end, start,
                            _easyPaths[annotation].ArrivesAt, first, pulse == 0, annotation);
                        KeepRequotedWidth(quoted);
                    }
                }
            }
        }
    }


    private void PrepareEasyKaleidoscope()
    {
        int face = _easyTriangleFacesPrepared++;
        Vector2 center = Player.GlobalPosition;
        if (face == 0)
        {
            float nearest = Math.Min(Math.Min(center.X - Field.Left, Field.Right - center.X),
                Math.Min(center.Y - Field.Top, Field.Bottom - center.Y));
            _easyTriangleFirstEdge = nearest == center.Y - Field.Top ? 0 : nearest == Field.Right - center.X ? 1
                : nearest == Field.Bottom - center.Y ? 2 : 3;
        }
        int edgeIndex = (_easyTriangleFirstEdge + new[] { 0, 2, 1, 3 }[face]) % 4;
        Vector2[] vertices = edgeIndex switch
        {
            0 => new[] { new Vector2(Field.Left - 8, -8), new Vector2(Field.Right + 8, -8), new Vector2(center.X, Field.Bottom + 8) },
            1 => new[] { new Vector2(Field.Right + 8, -8), new Vector2(Field.Right + 8, Field.Bottom + 8), new Vector2(Field.Left - 8, center.Y) },
            2 => new[] { new Vector2(Field.Left - 8, Field.Bottom + 8), new Vector2(Field.Right + 8, Field.Bottom + 8), new Vector2(center.X, -8) },
            _ => new[] { new Vector2(Field.Left - 8, -8), new Vector2(Field.Left - 8, Field.Bottom + 8), new Vector2(Field.Right + 8, center.Y) },
        };
        double start = TriangleWarningStart(face), first = start + EasyCueLead;
        for (int edge = 0; edge < 3; edge++)
        {
            Vector2 a = vertices[edge], b = vertices[(edge + 1) % 3];
            AddEasyCut($"triangle:{face}:edge:{edge}", a, b, 3, start, first + edge * .23, first + 1.7);
            Vector2 middle = a.Lerp(b, .5f);
            AddEasyCut($"triangle:{face}:divide:{edge}", middle, center, 2.5f,
                start, first + 1.7 + edge * .32, first + 2.6 + edge * .32);
            for (int pulse = 0; pulse < QuotePulses; pulse++)
            {
                int root = AddEasyQuote($"triangle:{face}:{edge}:{pulse}:parent", middle, center,
                    start, first + 1.7 + edge * .32 + pulse * EditorialSpacing, first + 1.7, pulse == 0);
                var principals = new List<int>();
                AddEasyFan(root, $"triangle:{face}:{edge}:{pulse}:subdivision", vertices, principal: principals);
                if (Tier >= 2)
                    for (int vertex = 0; vertex < 3; vertex++)
                    {
                        int division = principals[vertex];
                        var path = _easyPaths[division];
                        Vector2 split = Inside(center.Lerp(vertices[vertex], .38f), 16);
                        if (split.DistanceSquaredTo(center) < 4) split = Inside(center + Vector2.Down * 12, 8);
                        SetQuoteRoute(division, new[] { center, split });
                        if (Tier == 2)
                        {
                            Vector2 opposite = vertices[(vertex + 1) % 3].Lerp(vertices[(vertex + 2) % 3], .5f);
                            Vector2 axis = vertices[vertex].DirectionTo(opposite), across = axis.Orthogonal();
                            Vector2 entry = split + axis * 18, end = EasyExit(entry, axis);
                            Vector2 bend = entry.Lerp(end, .5f) + across * (entry.DistanceTo(end) * .1f);
                            var facet = QuoteCurve(entry, bend, end);
                            for (int side = 1; side <= 2; side++)
                            {
                                Vector2 offset = across * ((side == 1 ? -1 : 1) * (TargetOpening + 4) / 2);
                                int fragment = AddEasyQuote($"triangle:{face}:{edge}:{pulse}:facet:{vertex}:{side}",
                                    split, end + offset, start, path.ArrivesAt, first + 1.7, pulse == 0, division);
                                KeepRequotedWidth(fragment);
                                SetQuoteRoute(fragment, new[] { split }.Concat(facet.Select(point => point + offset)).ToArray());
                            }
                            continue;
                        }
                        for (int side = 1; side <= 2; side++)
                        {
                            Vector2 tangent = vertices[(vertex + side) % 3];
                            Vector2 bend = Inside(split.Lerp(tangent, .45f), 14);
                            Vector2 end = EasyExit(bend, bend.DirectionTo(tangent));
                            int fragment = AddEasyQuote($"triangle:{face}:{edge}:{pulse}:facet:{vertex}:{side}", split, end,
                                start, path.ArrivesAt, first + 1.7, pulse == 0, division);
                            KeepRequotedWidth(fragment);
                            SetQuoteRoute(fragment, QuoteCurve(split, bend, end));
                        }
                    }
            }
        }
    }


    private void PrepareEasyParagraph()
    {
        int paragraph = _easyParagraphsPrepared++;
        double start = ParagraphWarningStart(paragraph), first = start + EasyCueLead;
        float y = Player.GlobalPosition.Y;
        bool right = paragraph % 2 == 0;
        Vector2 from = new(right ? Field.Left : Field.Right, y);
        Vector2 fork = from + (right ? Vector2.Right : Vector2.Left) * 26;
        for (int pulse = 0; pulse < QuotePulses; pulse++)
        {
            int root = AddEasyQuote($"paragraph:{paragraph}:{pulse}:original", from, fork, start,
                first + pulse * EditorialSpacing, first, pulse == 0);
            int columns = ParagraphColumns;
            for (int column = 0; column < columns; column++)
            {
                if (column != paragraph % columns && column != (paragraph + 1) % columns) continue;
                float left = Field.Left + 6 + (Field.Width - 12) * column / columns;
                float rightRail = left + (Field.Width - 12) / columns;
                Vector2 seam = new((left + rightRail) * .5f, y);
                int child = AddEasyQuote($"paragraph:{paragraph}:{pulse}:line:{column}", fork, seam, start,
                    _easyPaths[root].ArrivesAt, first, pulse == 0, root);
                for (int branch = 0; branch < 2; branch++)
                {
                    Vector2 direction = new(right ? 1.55f : -1.55f, branch == 0 ? -1 : 1);
                    int reflected = AddEasyQuote($"paragraph:{paragraph}:{pulse}:bounce:{column}:{branch}", seam,
                        seam + direction, start, _easyPaths[child].ArrivesAt, first, pulse == 0, child);
                    var path = _easyPaths[reflected];
                    float minX = left + ParagraphRailRadius + path.HalfWidth, maxX = rightRail - ParagraphRailRadius - path.HalfWidth;
                    var points = new List<Vector2> { seam };
                    Vector2 at = seam;
                    float exitY = branch == 0 ? -28 : Field.Bottom + 28;
                    while (true)
                    {
                        float wallX = direction.X > 0 ? maxX : minX;
                        float travel = (wallX - at.X) / direction.X;
                        Vector2 turn = at + direction * travel;
                        if (branch == 0 ? turn.Y <= exitY : turn.Y >= exitY)
                        {
                            points.Add(at + direction * ((exitY - at.Y) / direction.Y));
                            break;
                        }
                        points.Add(turn);
                        at = turn;
                        direction.X = -direction.X;
                    }
                    SetQuoteRoute(reflected, points.ToArray(), corners: true, rails: true);
                }
            }
        }
    }
    private void BuildEasyRewrite()
    {
        for (int edition = 0; edition < 2; edition++)
        {
            bool right = edition == 0;
            Vector2 forward = right ? Vector2.Right : Vector2.Left;
            Vector2 from = new(right ? Field.Left : Field.Right, edition == 0 ? 54 : 162);
            Vector2 fork = from + forward * 64;
            double start = edition * 2.4, first = start + EasyCueLead;
            for (int pulse = 0; pulse < QuotePulses; pulse++)
            {
                int root = AddEasyQuote($"rewrite:{edition}:{pulse}:original", from, fork,
                    start, first + pulse * EditorialSpacing, first, pulse == 0);
                for (int phrase = 0; phrase < 3; phrase++)
                {
                    Vector2 editing = new(Field.CenterX + (edition == 0 ? -14 : 14),
                        Field.CenterY + (phrase - 1) * (TargetOpening + 3));
                    int child = AddEasyQuote($"rewrite:{edition}:{pulse}:phrase:{phrase}", fork, editing,
                        start, _easyPaths[root].ArrivesAt, first, pulse == 0, root);
                    SetSynchronizedQuote(child, new[] { fork, editing },
                        fork.DistanceTo(new Vector2(editing.X, Field.CenterY)) / (Speed * .9f));
                    _easyPaths[child].EditGroup = edition * 3 + phrase;
                }
            }
        }
    }

    private void PrepareEasyRevision(int group)
    {
        _easyRevisionsPrepared.Add(group);
        var phrases = _easyPaths.Select((path, index) => (path, index)).Where(item => item.path.EditGroup == group).ToArray();
        Vector2 editing = phrases[0].path.To;
        Vector2 target = Player.GlobalPosition == editing ? editing + Vector2.Down : Player.GlobalPosition;
        float opposite = group < 3 ? Field.Right + 28 : Field.Left - 28;
        double first = Time + EasyCueLead;
        foreach (var phrase in phrases)
        {
            if (Tier == 2)
            {
                float forward = group < 3 ? 1 : -1;
                float bend = 32 + Mathf.Clamp((target.X - editing.X) * forward, -8, 8);
                int section = group % 3;
                for (int line = 0; line < 3; line++)
                {
                    float y = Inside(target + Vector2.Down * ((section - 1) * 44 + (line - 1) * 16), 10).Y;
                    Vector2 erased = new(editing.X + forward * bend, y), corrected = new(editing.X - forward * 16, y);
                    Vector2 destination = target + Vector2.Down * ((section - 1) * 44 + (line - 1) * 22);
                    Vector2 end = EasyExit(corrected, corrected.DistanceSquaredTo(destination) > 1
                        ? corrected.DirectionTo(destination) : Vector2.Right * forward);
                    int revision = AddEasyQuote($"{phrase.path.Key}:revision:{line}", editing, end, Time,
                        first + phrase.path.FiresAt - phrases[0].path.FiresAt, first, phrase.path.Draw, phrase.index);
                    SetSynchronizedQuote(revision, new[] { editing, erased, corrected, corrected.Lerp(end, 1f/3),
                        corrected.Lerp(end, 2f/3), end }, (2*bend + 16 + Math.Abs(end.X-corrected.X)) / (Speed * .81f));
                    _easyPaths[revision].Turns = new[] { erased, corrected };
                }
                continue;
            }
            var revisions = AddEasyFan(phrase.index, phrase.path.Key.Replace(":phrase:", ":revision:"), new[] {
                new Vector2(opposite, -12), target, new Vector2(opposite, Field.Bottom + 12) },
                first + phrase.path.FiresAt - phrases[0].path.FiresAt, Time);
            for (int revision = 0; revision < revisions.Length; revision++)
            {
                var path = _easyPaths[revisions[revision]];
                Vector2 forward = group < 3 ? Vector2.Right : Vector2.Left, side = forward.Orthogonal();
                float line = (revision - (revisions.Length - 1) * .5f) * (Tier == 0 ? 7 : 5);
                Vector2 erased = Inside(path.From + forward * (32 + Tier * 4) + side * line, 12);
                Vector2 corrected = Inside(path.From - forward * 16 + side * line, 12);
                SetQuoteRoute(revisions[revision], new[] { path.From, erased, corrected, path.To }, corners: true);
            }
        }
    }


    private void BuildEasyEditedPage()
    {
        _peelSpine = Field.CenterY - 34;
        _points = new[] { new Vector2(Field.CenterX - 68, 36), new Vector2(Field.CenterX + 60, 178),
            new Vector2(Field.CenterX, 108) };
    }

    private void PrepareEasyEditedPage()
    {
        int page = _easyPagesPrepared++;
        Vector2 target = Player.GlobalPosition;
        bool right = page == 0 ? target.X < Field.CenterX : target.X >= Field.CenterX;
        Vector2 from = _points[2] + Vector2.Down * (page == 0 ? -34 : 34);
        Vector2 fork = new(right ? Field.Right - 48 : Field.Left + 48, Mathf.Clamp(target.Y, 24, Field.Bottom - 24));
        Vector2 aim = target == fork ? fork + (right ? Vector2.Left : Vector2.Right) : target;
        float opposite = right ? Field.Left - 28 : Field.Right + 28;
        double start = Time, first = start + EasyCueLead;
        for (int pulse = 0; pulse < QuotePulses; pulse++)
        {
            int root = AddEasyQuote($"page:{page}:{pulse}:edited", from, fork, start,
                first + pulse * EditorialSpacing, first, pulse == 0, peel: true);
            _easyPaths[root].PageEdition = page;
            if (Tier >= 2) SetQuoteRoute(root, QuoteCurve(from, _points[page], fork));
            AddEasyFan(root, $"page:{page}:{pulse}:peel", new[] {
                new Vector2(opposite, -12), aim, new Vector2(opposite, Field.Bottom + 12) }, hardSpacing: 30);
        }
    }

    private float EasyReaderSpeed => Tier >= 2 ? (Evolved ? 22 : 20) + (Tier == 3 ? 4 : 0) : 24 + Tier * 9 + (Evolved ? 3 : 0);
    private float EasyReaderHalfGap => 24 - Tier * 3;
    private double EasySentenceDuration => 9.8 - Tier * .8;
    private float EasySentenceHalfGap => 25 - Tier * 3;
    private double EasyReaderEnd => EasyCueLead + (Field.Height + 20) / EasyReaderSpeed;
    private float EasyReaderGap(float progress)
    {
        float start = Mathf.Clamp(_anchor.X, Field.Left + 26, Field.Right - 26);
        float end = start < Field.CenterX ? Field.Right - 28 : Field.Left + 28;
        return Mathf.Lerp(start, end, Mathf.SmoothStep(0, 1, Mathf.Clamp(progress, 0, 1)));
    }

    private (Vector2 A, Vector2 B, Vector2 C, Vector2 D, float HalfWidth) EasyReaderGeometry(double at)
    {
        float y = -10 + Mathf.Clamp((float)(at - EasyCueLead) * EasyReaderSpeed, 0, Field.Height + 20);
        float gap = EasyReaderGap((y + 10) / (Field.Height + 20));
        return (new Vector2(Field.Left, y), new Vector2(gap - EasyReaderHalfGap, y),
            new Vector2(gap + EasyReaderHalfGap, y), new Vector2(Field.Right, y), 4);
    }

    private IEnumerable<(double At, Vector2 A, Vector2 B, Vector2 C, Vector2 D, float HalfWidth)> EasyReaderFrames
    {
        get
        {
            for (int step = 0; step <= 20; step++)
            {
                double at = EasyCueLead + (EasyReaderEnd - EasyCueLead) * step / 20;
                var g = EasyReaderGeometry(at);
                yield return (at, g.A, g.B, g.C, g.D, g.HalfWidth);
            }
        }
    }

    private void BuildEasyReader()
    {
        _easyEnd = EasyReaderEnd;
        for (int row = 0; row < 4; row++)
        for (int source = 0; source < (Tier >= 2 ? 2 : 1); source++)
        {
            float y = Tier >= 2 ? 24 + row * 56 : 12 + row * 64;
            bool right = Tier >= 2 ? source == 0 : row % 2 == 0;
            // Shared burst timing keeps the other sources from filling each crossing gap.
            double first = EasyCueLead + (y + 10) / EasyReaderSpeed;
            Vector2 from = new(Tier >= 2 ? Field.Left + Field.Width * (source + .5f) / 2
                : right ? Field.Left : Field.Right, y);
            Vector2 fork = new(Tier >= 2 ? from.X + (right ? 24 : -24)
                : EasyReaderGap((y + 10) / (Field.Height + 20)), y);
            float farX = right ? Field.Right + 28 : Field.Left - 28;
            Vector2[] destinations = Tier >= 2 ? new[] {
                new Vector2(farX, Math.Max(12, y - 72)),
                new Vector2(farX, Mathf.Clamp(_anchor.Y, Math.Max(18, y - 45), Math.Min(Field.Bottom - 18, y + 45))),
                new Vector2(farX, y + 72),
            } : new[] {
                new Vector2(Field.Left - 28, y - 94), new Vector2(Field.Right + 28, y - 94),
                new Vector2(_anchor.X, y + 68),
            };
            for (int pulse = 0; pulse < (Tier == 3 ? QuotePulses * 2 : Math.Min(4, QuotePulses)); pulse++)
            {
                int root = AddEasyQuote($"reader:{row}:{source}:{pulse}:line", from, fork, first - EasyCueLead,
                    first + pulse * EditorialSpacing + (Tier == 3 ? pulse / 4 * .78 : 0), first, pulse == 0);
                var principal = new List<int>();
                AddEasyFan(root, $"reader:{row}:{source}:{pulse}:quotation", destinations, principal: principal);
                if (Tier >= 2)
                {
                    int wrapped = principal[1];
                    var comment = _easyPaths[wrapped];
                    Vector2 split = Inside(comment.From + (right ? Vector2.Right : Vector2.Left) * 72, 20);
                    SetQuoteRoute(wrapped, new[] { comment.From, split });
                    float replyLine = row == 0 ? y : y - 56;
                    int replies = Tier == 2 ? 2 : 1;
                    for (int reply = -replies; reply <= replies; reply++)
                    {
                        Vector2 turn = Inside(new Vector2(split.X, replyLine + reply * (Tier == 2 ? 14 : 16)), 12);
                        Vector2 end = new(right ? Field.Left - 28 : Field.Right + 28, turn.Y);
                        int continued = AddEasyQuote($"reader:{row}:{source}:{pulse}:line-wrap:{reply + replies}", split, end,
                            comment.StartsAt, comment.ArrivesAt, comment.WarningEndsAt, pulse == 0, wrapped);
                        KeepRequotedWidth(continued);
                        SetQuoteRoute(continued, new[] { split, turn, end }, corners: true);
                    }
                }
            }
        }
    }

    private (float Head, float Center, Rect2 Upper, Rect2 Lower, Rect2 Tail) EasySentenceGeometry(double at)
    {
        float progress = Mathf.Clamp((float)((at - EasyCueLead) / EasySentenceDuration), 0, 1);
        bool right = _anchor.X < Field.CenterX;
        float head = Mathf.Lerp(right ? Field.Left + 26 : Field.Right - 26,
            right ? Field.Right - 14 : Field.Left + 14, progress);
        float finish = _anchor.Y < Field.CenterY ? 157 : 59;
        float center = Mathf.Lerp(108, finish, progress) + Mathf.Sin(progress * Mathf.Tau) * 23;
        var upper = new Rect2(Field.Left, Field.Top, Field.Width, center - EasySentenceHalfGap - Field.Top);
        var lower = new Rect2(Field.Left, center + EasySentenceHalfGap, Field.Width, Field.Bottom - center - EasySentenceHalfGap);
        float tail = head + (right ? -1 : 1) * (43 + TargetOpening);
        var behind = right ? new Rect2(Field.Left, 0, Mathf.Max(0, tail - Field.Left), Field.Bottom)
            : new Rect2(Mathf.Min(Field.Right, tail), 0, Mathf.Max(0, Field.Right - tail), Field.Bottom);
        return (head, center, upper, lower, behind);
    }

    private IEnumerable<(double At, float Head, float Center, Rect2 Upper, Rect2 Lower, Rect2 Tail)> EasySentenceFrames
    {
        get
        {
            for (int step = 0; step <= 32; step++)
            {
                double at = EasyCueLead + EasySentenceDuration * step / 32;
                var g = EasySentenceGeometry(at);
                yield return (at, g.Head, g.Center, g.Upper, g.Lower, g.Tail);
            }
        }
    }

    private bool EasyQuoteExists(EasyQuotePath path) => path.Shot is { Active: true } shot
        && shot.ActivationId == path.Activation && shot.BossBarrageId == GetInstanceId();

    private void StepEasyEditorial(double delta)
    {
        if (Kind == BossTechniqueKind.Normal && Variant == 0 && !_easySelectionBuilt)
        {
            _selectionEnd = Player.GlobalPosition;
            _selectionTravel = _selectionTravel.Expand(_selectionEnd);
            _selectionRect = SelectionBounds();
            if (Time >= _easySelectionRecordAt + 2) BuildEasySelectedFrame();
        }
        if (SelectionTechnique)
        {
            if (_selectionMotionEvent < 0 && Evolved)
            {
                Ended += _ => Boss.SetAnimalWindup(false);
                TreeExiting += () => { if (IsInstanceValid(Boss)) Boss.SetAnimalWindup(false); };
            }
            int stage = SelectionStage;
            if (stage != _selectionMotionEvent)
            {
                _selectionMotionEvent = stage;
                if (Evolved)
                {
                    if (stage <= 1) Boss.SetAnimalWindup(true);
                    else Boss.ShowAnimalAttack();
                }
            }
            if (Evolved && stage == 2 && Time >= _selectionLockedAt + EasyCueLead + .8)
                Boss.SetAnimalWindup(true);
        }
        if (Kind == BossTechniqueKind.Normal && Variant == 1)
            while (_easyFanRowsPrepared < FanRows && Time >= FanWarningStart(_easyFanRowsPrepared)) PrepareEasyQuotationFan();
        if (Kind == BossTechniqueKind.Normal && Variant == 2)
            while (_easyTriangleFacesPrepared < 4 && Time >= TriangleWarningStart(_easyTriangleFacesPrepared)) PrepareEasyKaleidoscope();
        if (Tier >= 2 && Kind == BossTechniqueKind.Extra && !Evolved)
        {
            while (_easyParagraphsPrepared < Paragraphs && Time >= ParagraphWarningStart(_easyParagraphsPrepared)) PrepareEasyParagraph();
            foreach (var rail in _easyCuts.Where(cut => cut.Key.StartsWith("paragraph:rail:"))) rail.EndsAt = _easyEnd;
        }
        if (Kind == BossTechniqueKind.Signature)
            while (_easyPagesPrepared < 2 && Time >= 2.2 + _easyPagesPrepared * 4.25) PrepareEasyEditedPage();
        _easySplitParents.Clear();
        for (int index = 0; index < _easyPaths.Count; index++)
        {
            var path = _easyPaths[index];
            if (path.Draw && Time >= path.StartsAt && Time < path.WarningEndsAt)
            {
                var points = path.Points.Length > 0 ? path.Points : new[] { path.From, path.To };
                for (int leg = 1; leg < points.Length; leg++)
                    Line(points[leg - 1], points[leg], path.HalfWidth, false, EasyCueProgress(path.StartsAt));
            }
            if (path.Fired || Time < path.FiresAt) continue;
            Bullet bullet;
            if (path.Parent < 0) bullet = Shot(path.From, path.From.DirectionTo(path.To), 1);
            else
            {
                var parent = _easyPaths[path.Parent];
                if (!EasyQuoteExists(parent)) continue;
                parent.Shot!.GlobalPosition = parent.To;
                bullet = Pool.SpawnReiFragment(parent.Shot, path.From.DirectionTo(path.To), 0, 0, warnedLaunch: true);
                Track(bullet);
                _easySplitParents.Add(path.Parent);
            }
            bullet.UseBossIllustration(QuoteVisual(path), fitRadius: path.HalfWidth / Mathf.Max(path.Scale.X, path.Scale.Y));
            bullet.CancelLeadIn();
            bullet.MakeReiSplit(0);
            bullet.GlobalPosition = path.From;
            bullet.Scale = path.Scale;
            bullet.Velocity = path.From.DirectionTo(path.To) * path.Velocity;
            bullet.Rotation = bullet.Velocity.Angle();
            bullet.ReleaseBossShot();
            bullet.BossBarrageWave = index;
            path.Shot = bullet;
            path.Activation = bullet.ActivationId;
            path.Fired = true;
            bullet.BossPathDriven = path.Points.Length > 0;
        }
        // A parent remains allocated until all siblings inherit its physical properties.
        foreach (int parent in _easySplitParents) Pool.Despawn(_easyPaths[parent].Shot!);
        for (int index = 0; index < _easyPaths.Count; index++)
        {
            var path = _easyPaths[index];
            if (!EasyQuoteExists(path)) continue;
            if (path.Points.Length > 0 && !path.Waiting)
            {
                float travel = (float)(Time - path.FiresAt) * path.Velocity;
                int leg = Array.BinarySearch(path.Distances, travel);
                leg = Mathf.Clamp(leg >= 0 ? leg + 1 : ~leg, 1, path.Points.Length - 1);
                if (path.Synchronized)
                {
                    float part = Mathf.Clamp((float)((Time - path.FiresAt) / (path.ArrivesAt - path.FiresAt)), 0, 1)
                        * (path.Points.Length - 1);
                    leg = Math.Min((int)part + 1, path.Points.Length - 1);
                    travel = Mathf.Lerp(path.Distances[leg-1], path.Distances[leg], part - (leg-1));
                }
                Vector2 from = path.Points[leg - 1], to = path.Points[leg];
                Vector2 direction = from.DirectionTo(to);
                path.Shot!.GlobalPosition = from + direction * (travel - path.Distances[leg - 1]);
                if (path.CurrentLeg != leg)
                {
                    path.CurrentLeg = leg;
                    float speed = path.Synchronized ? from.DistanceTo(to) * (path.Points.Length - 1)
                        / (float)(path.ArrivesAt - path.FiresAt) : path.Velocity;
                    path.Shot.Velocity = direction * speed;
                    path.Shot.Rotation = direction.Angle();
                }
            }
            if (path.EditGroup >= 0 && Time >= path.ArrivesAt && !_easyRevisionsPrepared.Contains(path.EditGroup))
                PrepareEasyRevision(path.EditGroup);
            if (!path.Waiting && Time >= path.ArrivesAt && path.LastChildFire > Time)
            {
                path.Waiting = true;
                path.Shot!.GlobalPosition = path.To;
                path.Shot.Velocity = Vector2.Zero;
                path.Shot.HoldBossShot();
            }
            if (path.Peel && path.Hits < 3) PeelEasyPage(path);
        }
        if (Kind == BossTechniqueKind.Signature && Tier >= 2 && _easyPagesPrepared == 2 && !_peelReviewed
            && Time >= _easyPaths.Where(path => path.Peel).Max(path => path.LastChildFire) + .1
            && Time >= _easyEnd - 3.8)
        {
            _peelReviewed = true;
            PeelReviewFailed = _peeledEditions.Any(peeled => !peeled);
            if (PeelReviewFailed)
            {
                float crease = Field.CenterX + (!_peeledEditions[0] ? -1 : 1) * Field.Width / 5;
                double close = Math.Max(Time + 3.4, _easyEnd);
                AddEasyCut("page:unpeeled-review:left", Field.Rect.Position, new Vector2(crease - TargetOpening / 2, Field.Bottom),
                    0, Time, close, close + 1, box: true);
                AddEasyCut("page:unpeeled-review:right", new Vector2(crease + TargetOpening / 2, Field.Top), Field.Rect.End,
                    0, Time, close, close + 1, box: true);
            }
        }
        foreach (var cut in _easyCuts)
        {
            if (Time >= cut.FiresAt) cut.Fired = true;
            if (Time < cut.StartsAt || Time >= cut.EndsAt) continue;
            if (cut.Box) Box(new Rect2(cut.A, cut.B - cut.A), cut.Fired, EasyCueProgress(cut.StartsAt));
            else Line(cut.A, cut.B, cut.HalfWidth, cut.Fired, EasyCueProgress(cut.StartsAt));
        }
        if (Kind == BossTechniqueKind.Area && (Time < _easyEnd || OwnedShots.Any()))
        {
            var g = EasyReaderGeometry(Time);
            bool live = Time >= EasyCueLead;
            if (live && Time < EasyReaderEnd)
            {
                Line(g.A, g.B, g.HalfWidth, true);
                Line(g.C, g.D, g.HalfWidth, true);
            }
        }
        if (Kind == BossTechniqueKind.Finale && Time >= EasyCueLead && Time < _easyEnd)
        {
            var g = EasySentenceGeometry(Time);
            Box(g.Upper, true);
            Box(g.Lower, true);
            if (g.Tail.Size.X > 0) Box(g.Tail, true);
        }
        bool pending = Kind == BossTechniqueKind.Normal && Variant == 0 && !_easySelectionBuilt
            || Kind == BossTechniqueKind.Normal && Variant == 1 && _easyFanRowsPrepared < FanRows
            || Kind == BossTechniqueKind.Normal && Variant == 2 && _easyTriangleFacesPrepared < 4
            || Kind == BossTechniqueKind.Extra && !Evolved && _easyParagraphsPrepared < Paragraphs
            || Kind == BossTechniqueKind.Signature && _easyPagesPrepared < 2
            || Kind == BossTechniqueKind.Extra && Evolved && Variant == 1 && _easyRevisionsPrepared.Count < 6;
        if (Kind == BossTechniqueKind.Signature && !_peelReviewed && (pending || Time < _easyEnd || OwnedShots.Any()))
        {
            var page = _easyPaths.LastOrDefault(path => path.Peel && path.Draw);
            if (page != null)
            {
                float impression = page.From.Y;
                if (!_peeledEditions[page.PageEdition] && EasyQuoteExists(page)) impression = page.Shot!.GlobalPosition.Y;
                _peelSpine = Mathf.MoveToward(_peelSpine, impression, 28 * (float)delta);
            }
        }
        _editorialFillCanvas?.QueueRedraw();
        if (!pending && Time >= _easyEnd && !OwnedShots.Any()) Finish();
    }


    private void PeelEasyPage(EasyQuotePath page)
    {
        var source = page.Shot!;
        foreach (var shot in GetTree().GetNodesInGroup("player_bullets").OfType<Bullet>().Where(b => b.Active && !b.IsEnemy).ToArray())
        {
            bool hit;
            if (shot.Footprint is { } footprint)
                hit = source.Footprint!.Shapes.Any(s => footprint.Shapes.Any(p => s.Collide(source.GlobalTransform, p, shot.GlobalTransform)));
            else
            {
                _roundShot ??= new CircleShape2D();
                _roundShot.Radius = shot.Radius;
                hit = source.Footprint!.Shapes.Any(s => s.Collide(source.GlobalTransform, _roundShot, shot.GlobalTransform));
            }
            if (!hit) continue;
            Pool.Despawn(shot);
            page.Hits++;
            if (page.Hits < 3) continue;
            if (page.PageEdition >= 0 && !_peeledEditions[page.PageEdition])
            {
                _peeledEditions[page.PageEdition] = true;
            }
            source.HoldBossShot();
            source.Modulate = new Color(1, 1, 1, .35f);
            break;
        }
    }

    private Bullet Seed(Vector2 at, Vector2 direction, int group = 0, int pattern = 1)
    {
        var b = Shot(at, direction, pattern);
        b.BossBarrageWave = group;
        return b;
    }

    private static BulletArt.PlayerVisual QuoteVisual(EasyQuotePath path)
    {
        if (path.Peel) return BulletArt.EnemyVisual("res://assets/fx/rei_open_book.svg", "ffb2d2");
        if (path.RailReflection || path.Turns.Length > 0) return BulletArt.EnemyVisual("res://assets/fx/rei_clapper.svg", "b4e79a");
        if (path.Points.Length > 2) return BulletArt.EnemyVisual("rei_film", "c7a4ff");
        if (path.LastChildFire > path.ArrivesAt + .02) return BulletArt.EnemyVisual("res://assets/fx/rei_bookmark.svg", "ffd987");
        return BulletArt.EnemyVisual(path.Parent < 0 ? "rei_subscriber" : "rei_comment", path.Parent < 0 ? "ffb085" : "efa2db");
    }

    protected override BulletArt.PlayerVisual WarningVisual => Kind switch
    {
        BossTechniqueKind.Edge => BulletArt.EnemyVisual("rei_comment", "efa2db"),
        BossTechniqueKind.Area when Variant == 1 => BulletArt.EnemyVisual("rei_film", "c7a4ff"),
        BossTechniqueKind.Cameo when Variant == 0 => BulletArt.EnemyVisual("rei_film", "c7a4ff"),
        BossTechniqueKind.Crossfire when Variant == 0 => BulletArt.EnemyVisual("res://assets/fx/rei_bookmark.svg", "ffd987"),
        BossTechniqueKind.Crossfire when Variant == 4 => BulletArt.EnemyVisual("rei_film", "c7a4ff"),
        BossTechniqueKind.Crossfire when Variant == 2 => BulletArt.EnemyVisual("rei_microphone", "ffb085"),
        _ => BulletArt.EnemyVisual("rei_subscriber", "ffb085"),
    };

    private EditedWord HoldWord(Vector2 at, Vector2 direction, int stage = 0)
    {
        var b = Seed(at, direction);
        b.MakeReiSplit(0);
        b.Velocity = Vector2.Zero;
        b.HoldBossShot();
        var word = new EditedWord { Bullet = b, Id = b.ActivationId, Start = at, Direction = direction, Stage = stage };
        _words.Add(word);
        return word;
    }

    private bool Exists(EditedWord word) => word.Bullet.Active && word.Bullet.ActivationId == word.Id
        && word.Bullet.BossBarrageId == GetInstanceId();

    private void Cue(Vector2 from, Vector2 direction, double start, double end, float reach = 70, float radius = 3)
    {
        if (Time < start || Time >= end) return;
        float progress = (float)((Time - start) / (end - start));
        Line(from, from + direction.Normalized() * reach, radius, false, progress);
        Disc(from, 11, false, progress, cueOnly: true);
    }

    private void DrainAfter(double end)
    {
        if (Time >= end && !OwnedShots.Any()) Finish();
    }

    private void Release(EditedWord word, Vector2 direction, bool split = true)
    {
        if (!Exists(word)) return;
        word.Bullet.ReleaseBossShot();
        word.Bullet.Velocity = direction.Normalized() * Speed;
        word.Bullet.MakeReiSplit(split ? 2 : 0);
        word.Bullet.DeployBossFormation(word.Bullet.GlobalPosition, edgeLaunch: true, warnedLaunch: true);
        word.Start = word.Bullet.GlobalPosition;
        word.Direction = direction.Normalized();
    }

    private List<EditedWord> Fork(EditedWord word, Vector2[] directions, int generation)
    {
        var result = new List<EditedWord>();
        if (!Exists(word)) return result;
        var b = word.Bullet;
        float clearance = generation == 1 ? (Tier == 0 ? 64 : 112) : 80;
        if (!Field.Rect.HasPoint(b.GlobalPosition) || b.GlobalPosition.DistanceTo(Player.GlobalPosition) < clearance)
        {
            b.MakeReiSplit(0);
            return result;
        }
        for (int i = 0; i < directions.Length; i++)
        {
            var child = Pool.SpawnReiFragment(b, directions[i], generation == 1 ? 1 : 0,
                (i - (directions.Length - 1) * .5f) * (generation == 1 ? 12 : 6), null);
            child.MakeReiSplit(0);
            Track(child);
            child.BossBarrageWave = generation;
            var next = new EditedWord { Bullet = child, Id = child.ActivationId, Start = child.GlobalPosition,
                Direction = directions[i], Stage = generation };
            result.Add(next);
        }
        Pool.Despawn(b);
        return result;
    }

    private void QuotedFan()
    {
        Cue(_source, _forward, 0, _warning, 100);
        int originals = Tier + 1;
        for (int i = 0; i < originals; i++)
        {
            if (At(_warning + i * 1.3))
            {
                var word = HoldWord(Side(i % 2 == 0 ? 68 : 148), _forward);
                Release(word, _forward, false);
            }
        }
        foreach (var word in _words.ToArray())
        {
            if (!Exists(word) || word.Bullet.LeadIn) continue;
            if (word.Stage == 0)
            {
                Vector2 fork = word.Start + _forward * 42;
                foreach (float angle in new[] { -.68f, .25f, .68f })
                    Line(fork, fork + _forward.Rotated(angle) * 68, 1.2f, false,
                        word.Start.DistanceTo(word.Bullet.GlobalPosition) / 42);
            }
            if (word.Stage == 0 && word.Bullet.GlobalPosition.DistanceTo(word.Start) >= 42)
            {
                var children = Fork(word, new[] { _forward.Rotated(-.68f), _forward.Rotated(.25f), _forward.Rotated(.68f) }, 1);
                if (children.Count == 0) word.Stage = 3;
                foreach (var child in children) child.Bullet.MakeReiSplit(1);
            }
        }
        DrainAfter(_warning + (originals - 1) * 1.3 + 4);
    }

    private void Kaleidoscope()
    {
        Vector2 center = (_points[0] + _points[1] + _points[2]) / 3;
        for (int edge = 0; edge < 3; edge++)
        {
            var a = _points[edge];
            var b = _points[(edge + 1) % 3];
            double begin = _warning + edge * .6;
            if (Time < begin + 2.8) Line(a, b, 3 + Tier * .5f, Time >= begin, (float)(Time / begin));
        }
        for (int cut = 0; cut < (Tier >= 2 ? 3 : 2); cut++)
        {
            double begin = _warning + 2.4 + cut * 1.15;
            if (Time >= begin - 1.15 && Time < begin + 1.3)
                Line(_points[cut], center, 3, Time >= begin, (float)((Time - begin + 1.15) / 1.15));
            if (At(begin)) Seed(_points[cut], _points[cut].DirectionTo(center));
        }
        DrainAfter(_warning + 6.5);
    }

    private void ReadingGate()
    {
        _gateWarningEdge = default;
        _gateStriking = false;
        int legs = Tier >= 2 ? 2 : 1;
        double crossing = (Field.Width + 36) / (38 + Tier * 3.0);
        double pause = WarnFor(42, 1.3);
        double end = _warning + legs * crossing + (legs - 1) * pause;
        if (Time >= end && !OwnedShots.Any()) { Finish(); return; }
        for (int leg = 0; leg < legs; leg++)
        {
            double at = _warning + leg * (crossing + pause);
            double warn = leg == 0 ? _warning : pause;
            float direction = GateDirection(leg);
            if (Tier >= 2)
            {
                int columns = 6 + Tier * 2;
                for (int column = 0; column < columns; column++)
                {
                    double fire = at + column * crossing / columns;
                    float x = Mathf.Lerp(direction > 0 ? Field.Left : Field.Right,
                        direction > 0 ? Field.Right : Field.Left, column / (float)columns);
                    Vector2 reverse = Vector2.Left * direction;
                    int rows = 3 + Tier;
                    for (int row = 0; row < rows; row++)
                    {
                        Vector2 origin = new(x, Field.Top + (row + .5f + .25f * (column % 2 == 0 ? 1 : -1)) * Field.Height / rows);
                        if (Time >= fire - warn && Time < fire)
                            Cue(origin, reverse, fire - warn, fire, Field.Width, 3);
                        if (At(fire))
                        {
                            var shot = Shot(origin, reverse);
                            shot.Homing = shot.Accel = false;
                            shot.MakeReiSplit(0);
                            shot.UseBossIllustration(BulletArt.EnemyVisual("rei_comment", "efa2db"), fitRadius: 3);
                            shot.Velocity = reverse * 36;
                            shot.CancelLeadIn();
                        }
                    }
                }
            }
            if (Time < at - warn || Time >= at + crossing) continue;
            float from = direction > 0 ? Field.Left - 18 : Field.Right + 18;
            float to = direction > 0 ? Field.Right + 18 : Field.Left - 18;
            float gate = Mathf.Lerp(from, to, Mathf.Clamp((float)((Time - at) / crossing), 0, 1));
            if (_gateLeg != leg)
            {
                _gateLeg = leg;
                _previousGate = gate;
                _previousReader = Player.GlobalPosition;
            }
            float visual = Mathf.Clamp(gate, Field.Left, Field.Right);
            Line(new Vector2(visual, 0), new Vector2(visual, Field.Bottom), 2, false, .8f, cueOnly: true);
            for (int row = 0; row < 4; row++)
                Cue(new Vector2(visual + direction * 12, 44 + row * 46), AttackTelegraph.Symbol.Boundary, direction: Vector2.Right * direction);
            bool striking = Time >= at && (direction > 0
                ? gate >= Field.Left && gate < Field.Right - 18
                : gate <= Field.Right && gate > Field.Left + 18);
            if (Time < at)
                _gateWarningEdge = new Rect2(direction > 0 ? Field.Left : Field.Right - 4, Field.Top, 4, Field.Height);
            _gateStriking = striking;
            float before = (_previousReader.X - _previousGate) * direction;
            float after = (Player.GlobalPosition.X - gate) * direction;
            if (striking && before > 0 && after <= 0)
            {
                Disc(Player.GlobalPosition, 4, true);
                _rejectedPosition = Player.GlobalPosition;
                _rejectedAt = Time;
            }
            _previousGate = gate;
            _previousReader = Player.GlobalPosition;
        }
    }

    private float GateDirection(int leg) => (leg % 2 == 0 ? 1 : -1) * (_anchor.X <= Field.CenterX ? 1 : -1);

    private void ReadComments()
    {
        double begin = _warning;
        const float first = 36;
        float cursor = first + (float)Math.Max(0, Time - begin) * (Tier >= 2 ? 32 : 25);
        if (Time < begin)
        {
            Line(new Vector2(Field.Left + 32, first), new Vector2(Field.Right - 32, first), 7, false, (float)(Time / begin));
        }
        else if (cursor < Field.Bottom + 8)
        {
            Line(new Vector2(Field.Left + 32, cursor), new Vector2(Field.Right - 32, cursor), 6 + Tier, true);
            Line(new Vector2(Field.Left + 32, cursor + 22), new Vector2(Field.Right - 32, cursor + 22), 2, false);
        }
        for (int row = 0; row < 3; row++)
        {
            double at = begin + (row * 52 + 32) / (Tier >= 2 ? 32.0 : 25.0);
            for (int copy = 0; copy < Copies; copy++)
                if (At(at + copy * .3))
                    Seed(Side(first + row * 52), _forward, row);
        }
        DrainAfter(begin + (Field.Bottom - first + 12) / (Tier >= 2 ? 32.0 : 25.0));
    }

    private void PrepareCropFilm(Vector2[] corners)
    {
        for (int edge = 0; edge < 4; edge++)
        {
            var a = corners[edge];
            var b = corners[(edge + 1) % 4];
            var axis = a.DirectionTo(b);
            var outward = new Vector2(axis.Y, -axis.X);
            void AddSpan(Vector2 from, Vector2 to)
            {
                float length = from.DistanceTo(to);
                int pieces = Math.Max(1, Mathf.RoundToInt((length + TargetOpening) / (8 + TargetOpening)));
                float width = (length - TargetOpening * (pieces - 1)) / pieces;
                for (int i = 0; i < pieces; i++)
                {
                    Vector2 left = from + axis * (i * (width + TargetOpening));
                    _cropFilm.Add((edge, left, left + axis * width, outward));
                }
            }
            if (edge == _cropExit)
            {
                Vector2 center = _cropGate.Clamp(_cropFrame.Position, _cropFrame.End);
                AddSpan(a, center - axis * TargetOpening / 2);
                AddSpan(center + axis * TargetOpening / 2, b);
            }
            else AddSpan(a, b);
            float distance = edge switch { 0 => a.Y - Field.Top, 1 => Field.Right - a.X,
                2 => Field.Bottom - a.Y, _ => a.X - Field.Left };
            _cropFilmEnd = Math.Max(_cropFilmEnd, CropCloseAt(edge) + 2 * CropFilmCadence + (distance + 6) / CropFilmSpeed);
        }
    }

    private const float CropFilmSpeed = 40, CropFilmDepth = 6;
    private double CropFilmCadence => (CropFilmDepth + TargetOpening) / CropFilmSpeed;
    private double CropCloseAt(int edge) => _warning + (edge - _cropExit + 3) % 4 * .75;

    private void StreamFrame()
    {
        const float rim = 3;
        Vector2 gate = _cropGate.Clamp(_cropFrame.Position, _cropFrame.End);
        Vector2 outward = gate.DirectionTo(_cropGate), tangent = outward.Orthogonal();
        Vector2 current = Player.GlobalPosition;
        float before = (_cropPrevious - gate).Dot(outward), after = (current - gate).Dot(outward);
        if (Time < _warning + 2.6 && before <= 0 && after > 0)
        {
            Vector2 crossing = _cropPrevious.Lerp(current, -before / (after - before));
            if (Math.Abs((crossing - gate).Dot(tangent)) + Player.CurrentHitRadius < TargetOpening / 2)
                _cropPassed = true;
        }
        _cropPrevious = current;

        if (Time < _cropFilmEnd)
        {
            const float rail = 2;
            float extension = outward.X > 0 ? Field.Right - gate.X : outward.X < 0 ? gate.X - Field.Left
                : outward.Y > 0 ? Field.Bottom - gate.Y : gate.Y - Field.Top;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector2 start = gate + tangent * (sign * (TargetOpening / 2 + rail));
                Line(start, start + outward * Math.Min(32, extension), rail, Time >= _cropRimAt);
            }
        }

        Vector2[] corners = { _cropFrame.Position, new(_cropFrame.End.X, _cropFrame.Position.Y),
            _cropFrame.End, new(_cropFrame.Position.X, _cropFrame.End.Y) };
        double sealEnd = _warning + 2.25 + .35;
        if (Time < sealEnd)
        {
            for (int edge = 0; edge < 4; edge++)
            {
                Vector2 a = corners[edge], b = corners[(edge + 1) % 4];
                double close = CropCloseAt(edge);
                if (edge == _cropExit)
                {
                    Vector2 inset = a.DirectionTo(b) * (TargetOpening / 2 + rim);
                    Line(a, gate - inset, rim, Time >= _cropRimAt);
                    Line(gate + inset, b, rim, Time >= _cropRimAt);
                }
                else Line(a, b, rim, Time >= _cropRimAt);
                if (Time < close)
                {
                    Vector2 label = (a + b) / 2;
                    if (edge == _cropExit)
                        label = gate.Lerp(gate.DistanceSquaredTo(a) > gate.DistanceSquaredTo(b) ? a : b, .65f);
                    Cue(label, AttackTelegraph.Symbol.Pips, count: 1 + (edge - _cropExit + 3) % 4);
                }
            }
        }
        foreach (var piece in _cropFilm)
        {
            double close = CropCloseAt(piece.Edge);
            if (Time < close)
            {
                var preview = new Rect2(piece.A, Vector2.Zero).Expand(piece.B).Expand(piece.B + piece.Out * Field.WorldRight)
                    .Expand(piece.A + piece.Out * Field.WorldRight).Intersection(Field.Rect);
                if (preview.HasArea()) Box(preview, false);
                continue;
            }
            for (int pulse = 0; pulse < 3; pulse++)
            {
                double age = Time - close - pulse * CropFilmCadence;
                if (age < 0) break;
                Vector2 shift = piece.Out * ((float)age * CropFilmSpeed);
                var tile = new Rect2(piece.A + shift, Vector2.Zero).Expand(piece.B + shift)
                    .Expand(piece.A + shift + piece.Out * CropFilmDepth).Expand(piece.B + shift + piece.Out * CropFilmDepth)
                    .Intersection(Field.Rect);
                if (tile.HasArea()) Box(tile, true);
            }
        }
        if (Time < _cropFilmEnd) return;
        if (_cropPassed || Time >= _cropFilmEnd + 2) { Finish(); return; }
        bool across = _cropExit is 0 or 2;
        float old = across ? gate.X : gate.Y;
        float position = across ? current.X : current.Y;
        _retakeCenter ??= Mathf.Clamp(position + (position < (across ? Field.CenterX : Field.CenterY) ? 12 : -12),
            Math.Max(old - 24, (across ? Field.Left : Field.Top) + TargetOpening),
            Math.Min(old + 24, (across ? Field.Right : Field.Bottom) - TargetOpening));
        float retakeProgress = Mathf.SmoothStep(0, 1, (float)((Time - _cropFilmEnd) / 1.6));
        CropMattes(across, Mathf.Lerp(old, _retakeCenter.Value, retakeProgress), true);
    }

    private void CropMattes(bool vertical, float center, bool live)
    {
        float lower = center - TargetOpening / 2, upper = center + TargetOpening / 2;
        Box(vertical ? new Rect2(Field.Left, Field.Top, lower - Field.Left, Field.Height)
            : new Rect2(Field.Left, Field.Top, Field.Width, lower - Field.Top), live);
        Box(vertical ? new Rect2(upper, Field.Top, Field.Right - upper, Field.Height)
            : new Rect2(Field.Left, upper, Field.Width, Field.Bottom - upper), live);
    }

    private void ClipLine()
    {
        int cuts = 3 + Tier;
        int copies = 1 + Tier / 2;
        float thickness = 5 + Tier, speed = 48 + Tier * 6;
        if (_sweepingCuts.Count < cuts && (_sweepingCuts.Count == 0 || Time >= _sweepingCuts[^1].EndsAt - _warning - .3))
        {
            float angle = _sweepingCuts.Count % 4 * Mathf.Pi / 4;
            Vector2 axis = Vector2.FromAngle(angle), forward = axis.Orthogonal();
            if (_sweepingCuts.Count % 2 != 0) forward = -forward;
            Vector2 target = Player.GlobalPosition;
            var corners = new[] { Field.Rect.Position, Field.Rect.End, new Vector2(Field.Left, Field.Bottom), new Vector2(Field.Right, Field.Top) };
            float halfLength = 55 + 4 * Tier;
            float offset = Mathf.Clamp(target.Dot(axis), corners.Min(v => v.Dot(axis)) + halfLength - 4,
                corners.Max(v => v.Dot(axis)) - halfLength + 4);
            target += axis * (offset - target.Dot(axis));
            float low = corners.Min(v => v.Dot(forward)) - 10, high = corners.Max(v => v.Dot(forward)) + 10;
            Vector2 from = target + forward * (low - target.Dot(forward));
            Vector2 to = target + forward * (high - target.Dot(forward));
            double fire = Time + _warning;
            _sweepingCuts.Add(new(from, to, axis, halfLength, Time, fire, fire + from.DistanceTo(to) / speed + (copies - 1) * .53));
        }
        foreach (var cut in _sweepingCuts)
        {
            if (Time >= cut.EndsAt) continue;
            Vector2 span = cut.Axis * cut.HalfLength;
            if (Time < cut.FiresAt)
            {
                float progress = Mathf.Clamp((float)((Time - cut.StartsAt) / .6), 0, 1);
                Line(cut.From - span, cut.From + span, thickness, false, progress);
                Line(cut.From - span, cut.To - span, thickness, false, progress);
                Line(cut.From + span, cut.To + span, thickness, false, progress);
                continue;
            }
            double travel = cut.From.DistanceTo(cut.To) / speed;
            for (int copy = 0; copy < copies; copy++)
            {
                double age = Time - cut.FiresAt - copy * .53;
                if (age < 0 || age >= travel) continue;
                Vector2 blade = cut.From.Lerp(cut.To, (float)(age / travel));
                Line(blade - span, blade + span, thickness, true);
            }
        }
        if (_sweepingCuts.Count == cuts && Time >= _sweepingCuts[^1].EndsAt) Finish();
    }

    private void StarEditing()
    {
        for (int i = 0; i < _points.Length; i++)
        {
            if (Time < _warning + 2.1)
            {
                Disc(_points[i], 13, false);
                Cue(_points[i] + Vector2.Up * 16, AttackTelegraph.Symbol.Pips, count: (i + 1));
                if (i < 2) Line(_points[i], _points[i + 1], 1, false);
            }
        }
        if (At(.05)) HoldWord(_points[0], _forward);
        if (_words.Count > 0 && Exists(_words[0]))
        {
            var word = _words[0];
            if (Time < _warning + 2.1)
            {
                word.Bullet.HoldBossShot();
                if (At(_warning)) word.Bullet.DeployBossFormation(_points[1], true, true);
                if (At(_warning + .85)) word.Bullet.DeployBossFormation(_points[2], true, true);
                Cue(_points[2], _forward, _warning + .85, _warning + 2.1, 130);
            }
            if (At(_warning + 2.1))
            {
                Release(word, _forward);
                for (int i = 1; i < Copies; i++)
                    Seed(_points[2] + Vector2.Down * ((i % 2 == 0 ? 1 : -1) * (28 + 10 * i)), _forward);
            }
        }
        DrainAfter(_warning + 3);
    }

    private void LastSentence()
    {
        LastPosition = Player.GlobalPosition;
        double duration = SentenceDuration;
        float startX = _forward.X < 0 ? Field.Left + 30 : Field.Right - 30;
        float endX = _forward.X < 0 ? Field.Right - 30 : Field.Left + 30;
        float progress = Mathf.Clamp((float)((Time - _warning) / duration), 0, 1);
        float head = Mathf.Lerp(startX, endX, progress);
        float centerY = 108 + Mathf.Sin(progress * Mathf.Tau) * 24;
        float room = Tier switch { 0 => 42, 1 => 36, 2 => 32, _ => 29 };
        if (Time < _warning)
        {
            float x0 = Mathf.Max(Field.Left, startX - 46), x1 = Mathf.Min(Field.Right, startX + 46);
            Box(new Rect2(x0, 0, x1 - x0, 108 - room), false, (float)(Time / _warning));
            Box(new Rect2(x0, 108 + room, x1 - x0, Field.Bottom - 108 - room), false, (float)(Time / _warning));
        }
        else if (Time < _warning + duration)
        {
            const float width = 46;
            float x0 = Mathf.Max(Field.Left, head - width), x1 = Mathf.Min(Field.Right, head + width);
            Box(new Rect2(x0, 0, x1 - x0, Mathf.Max(0, centerY - room)), true);
            Box(new Rect2(x0, centerY + room, x1 - x0, Field.Bottom - centerY - room), true);
            float tail = head + _forward.X * 62;
            float a = _forward.X < 0 ? Field.Left : tail;
            float b = _forward.X < 0 ? tail : Field.Right;
            if (b > a) Box(new Rect2(a, 0, b - a, Field.Bottom), true);
            float upcomingTail = tail - _forward.X * 38;
            float warnA = _forward.X < 0 ? Field.Left : upcomingTail;
            float warnB = _forward.X < 0 ? upcomingTail : Field.Right;
            if (warnB > warnA) Box(new Rect2(warnA, 0, warnB - warnA, Field.Bottom), false);
        }
        if (Time >= _warning + duration + .5) Finish();
    }

    private void Amplification()
    {
        double pulseStep = AmplificationPulseSpacing;
        double period = _warning + (3.2 + 3 * pulseStep);
        int rounds = Tier >= 2 ? Tier + 1 : 1;
        if (_amplificationRound + 1 < rounds && Time >= _amplificationStart + period && !OwnedShots.Any())
        {
            _amplificationRound++;
            _amplificationStart = Time;
            _echoPreviousCenter = _points[0];
            _points[0] = _echoPreviousCenter.MoveToward(Player.GlobalPosition, 22 * (float)_warning);
        }
        Vector2 merge = _points[0];
        double fire = _amplificationStart + _warning, arrive = fire + 1.8;
        if (Time < arrive)
        {
            float progress = Mathf.Clamp((float)((Time - _amplificationStart) / .6), 0, 1);
            foreach (var origin in AmplificationOrigins(merge))
                Line(origin, merge, AmplificationRadius, false, progress, cueOnly: Time >= fire);
            Line(merge + Vector2.Up * 56, merge + Vector2.Down * 56, 5 + Tier, false, progress);
        }
        if (Time < arrive + .7)
        {
            float progress = Mathf.Clamp((float)((Time - _amplificationStart) / .6), 0, 1);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float tip = 4 + TargetOpening / 2;
                float spread = 1.5f;
                Vector2 a = merge + Vector2.Down * sign * tip, b = merge + new Vector2(-54, sign * (tip + spread));
                Line(a, b, 4, false, progress);
            }
        }
        if (At(fire))
        {
            foreach (var origin in AmplificationOrigins(merge))
            {
                var word = HoldWord(origin, origin.DirectionTo(merge));
                word.Bullet.UseBossIllustration(WarningVisual!, fitRadius: AmplificationRadius);
                word.Bullet.CancelLeadIn();
                word.Bullet.GlobalPosition = origin;
                word.Bullet.Velocity = (merge - origin) / 1.8f;
                word.Bullet.Rotation = word.Bullet.Velocity.Angle();
                word.Bullet.BossPathDriven = true;
                word.Bullet.ReleaseBossShot();
            }
        }
        if (Time >= fire && Time < arrive)
        {
            float p = (float)((Time - fire) / 1.8);
            foreach (var word in _words.Where(Exists))
            {
                word.Bullet.GlobalPosition = word.Start.Lerp(merge, p);
            }
        }
        if (At(arrive))
            foreach (var word in _words.Where(Exists)) Pool.Despawn(word.Bullet);
        if (Time >= arrive && Time < arrive + .7)
            Line(merge + Vector2.Up * 56, merge + Vector2.Down * 56, 5 + Tier, true);
        if (Time >= arrive + .7 && Time < arrive + 1.8)
        {
            float open = 0;
            float tip = 4 + TargetOpening / 2;
            float spread = 1.5f;
            for (int sign = -1; sign <= 1; sign += 2)
                Line(merge + Vector2.Down * sign * (tip + open), merge + new Vector2(-54, sign * (tip + spread + open)), 4, true);
        }
        for (int pulse = 0; pulse < AmplificationPulses; pulse++)
        if (At(arrive + 1.2 + pulse * pulseStep))
        {
            int count = AmplificationRays;
            if (pulse == 0) _amplificationCenters.Add(merge);
            for (int i = 0; i < count; i++)
            {
                var shot = Seed(merge, AmplificationDirection(i), _amplificationRound);
                shot.CancelLeadIn();
                shot.GlobalPosition = merge;
                {
                    shot.BossBarrageWave = 1 + _amplificationRound;
                    shot.Velocity = AmplificationDirection(i) * Speed;
                }
                shot.MakeReiSplit(0);
                shot.UseBossIllustration(BulletArt.EnemyVisual("rei_comment", "efa2db"), fitRadius: AmplificationRadius);
            }
        }
        foreach (var shot in OwnedShots.Where(shot => shot.BossBarrageWave > 0 && shot.BossBarrageWave <= _amplificationCenters.Count))
            {
                Vector2 origin = _amplificationCenters[shot.BossBarrageWave - 1];
                float half = AmplificationExtension(origin.DistanceTo(shot.GlobalPosition));
                if (half <= 0) continue;
                Vector2 side = origin.DirectionTo(shot.GlobalPosition).Orthogonal() * half;
                Line(shot.GlobalPosition - side, shot.GlobalPosition + side, AmplificationRadius, true);
            }
        if (_amplificationRound == rounds - 1)
        {
            DrainAfter(arrive + 1.3 + (AmplificationPulses - 1) * pulseStep);
            if (Finished) return;
        }
    }

    private float AmplificationExtension(float distance)
    {
        float angle = Mathf.Pi / AmplificationRays;
        return Math.Max(0, (distance * Mathf.Sin(angle) - TargetOpening / 2 - AmplificationRadius) / Mathf.Cos(angle));
    }
    private int AmplificationPulses => Tier < 2 ? 2 + Tier : 4;
    private double AmplificationPulseSpacing => (2 * AmplificationRadius + TargetOpening) / Speed;
    private bool ShowingAmplificationSplit => Time >= _amplificationStart + _warning - .6
        && Time < _amplificationStart + _warning + 3 + (AmplificationPulses - 1) * AmplificationPulseSpacing;
    private (Vector2 Origin, Vector2 End, Vector2 Direction, float Half) AmplificationForecastRay(int ray)
    {
        Vector2 origin = _points[0], direction = AmplificationDirection(ray);
        float progress = Mathf.Clamp((float)((Time - _amplificationStart - _warning + .6) / .6), 0, 1);
        Vector2 end = origin.Lerp(SourceCutEdge(origin, direction, Field.Rect.Grow(24)), progress);
        return (origin, end, direction, AmplificationExtension(origin.DistanceTo(end)));
    }

    private float AmplificationRadius => BulletPool.BossHitShape(Job.Magic, Evolved).Radius;
    private int AmplificationRays => Tier >= 2 ? 16 + Tier * 4 : 4 + Copies;
    private IEnumerable<Vector2> AmplificationOrigins(Vector2 merge)
    {
        Vector2 first = new(Field.Left - 12, Mathf.Clamp(merge.Y - 38, Field.Top - 12, Field.Bottom + 12));
        Vector2 last = new(Field.Right + 12, Mathf.Clamp(merge.Y + 38, Field.Top - 12, Field.Bottom + 12));
        int intervals = 3 + Tier;
        for (int i = 0; i <= intervals; i++) yield return first.Lerp(last, i / (float)intervals);
    }
    private Vector2 AmplificationDirection(int index) => Vector2.FromAngle(index * Mathf.Tau / AmplificationRays + .2f + _amplificationRound * .23f);

    private void InsertQuotation()
    {
        float ink = 13 + 2 * Tier, pitch = ink + 25 - Tier * 2;
        int count = 2 + Tier;
        double reflow = pitch / (29 + Tier * 2);
        double period = _warning + reflow + .35;
        int turn = (int)(Time / period);
        if (turn >= count) { Finish(); return; }
        int columns = Mathf.CeilToInt(Field.Width / pitch) + 2;
        float origin = Field.CenterX - ink / 2;
        if (_insertionTurn != turn)
        {
            _insertionTurn = turn;
            _insertionY = Player.GlobalPosition.Y;
            _insertionDirection = Player.GlobalPosition.X < Field.CenterX ? 1 : -1;
            float behind = Player.GlobalPosition.X - _insertionDirection * pitch;
            int selected = Mathf.RoundToInt((behind - origin) / pitch);
            _insertionX = origin + selected * pitch + (_insertionDirection < 0 ? ink : 0);
        }

        double age = Time - turn * period;
        float shift = pitch * Mathf.Clamp((float)((age - _warning) / reflow), 0, 1);
        bool printed = turn > 0 || age >= _warning;
        for (int column = -columns; column <= columns; column++)
        {
            float left = origin + column * pitch;
            float edge = left + (_insertionDirection < 0 ? ink : 0);
            if ((edge - _insertionX) * _insertionDirection >= -.01f)
                left += _insertionDirection * shift;
            float mid = _insertionY + Math.Sign(column) * 14;
            var rect = new Rect2(left, Mathf.Clamp(mid - 44, Field.Top, Field.Bottom - 88), ink, 88).Intersection(Field.Rect);
            if (rect.HasArea()) Box(rect, printed, Mathf.Clamp((float)(age / .4), 0, 1));
        }
        if (shift > pitch - ink)
        {
            float width = shift - (pitch - ink);
            float left = _insertionX - (_insertionDirection < 0 ? width : 0);
            var inserted = new Rect2(left, Mathf.Clamp(_insertionY - 44, Field.Top, Field.Bottom - 88), width, 88).Intersection(Field.Rect);
            if (inserted.HasArea()) Box(inserted, true);
        }
        Cue(new Vector2(_insertionX, _insertionY - 40), AttackTelegraph.Symbol.Inbox);
    }

    private void SpreadingFront()
    {
        if (Tier >= 2)
        {
            if (_frontRound >= Tier + 1) { DrainAfter(0); return; }
            if (Time - _frontRoundStarted >= FrontRoundDuration)
            {
                _frontRound++;
                if (_frontRound >= Tier + 1) { DrainAfter(0); return; }
                _frontRoundStarted = Time;
                _forward = -_forward;
                _frontRoot = new Vector2(_forward.X > 0 ? Field.Left - 12 : Field.Right + 12, Player.GlobalPosition.Y);
                _frontUp = Player.GlobalPosition.Y < Field.CenterY ? Vector2.Down : Vector2.Up;
            }
        }
        double elapsed = Time - _frontRoundStarted;
        float total = Field.Width + 24;
        float reach = Mathf.Clamp((float)(elapsed - _warning) * FrontSpeed, 0, total);
        float halfWidth = 5 + Tier;
        double end = _frontRoundStarted + _warning + total / FrontSpeed + FrontBranchLength / FrontBranchSpeed + .4;
        if (elapsed < _warning)
            Line(_frontRoot, _frontRoot + _forward * total, halfWidth, false, (float)(elapsed / _warning));
        else if (Time < end)
            Line(_frontRoot, _frontRoot + _forward * reach, halfWidth, true);
        for (int i = 0; i < FrontBranches; i++)
        {
            float distance = FrontBranchDistance(i, total);
            double at = _frontRoundStarted + _warning + distance / FrontSpeed;
            Vector2 branch = _frontRoot + _forward * distance;
            Vector2 up = Tier >= 2 && i % 2 == 1 ? -_frontUp : _frontUp;
            float reachToEdge = Tier >= 2 ? (up.Y > 0 ? Field.Bottom + 8 - branch.Y : branch.Y - Field.Top + 8) : FrontBranchLength;
            float length = Mathf.Clamp((float)(Time - at) * FrontBranchSpeed, 0, reachToEdge);
            if (Time < at)
                Line(branch, branch + up * reachToEdge, halfWidth, false, (float)(elapsed / (at - _frontRoundStarted)));
            else if (Time < end)
            {
                Line(branch, branch + up * length, halfWidth, true);
                if (length < reachToEdge)
                    Line(branch + up * length, branch + up * reachToEdge, halfWidth, false, length / reachToEdge);
            }
            if (Tier < 2 && At(at)) Seed(branch, _frontUp, i);
        }
        if (Tier < 2) DrainAfter(end);
    }

    private void BraidedRumour()
    {
        double duration = 8.8 - Tier * .4;
        if (Time >= _warning + duration) { Finish(); return; }
        float progress = Mathf.Clamp((float)((Time - _warning) / duration), 0, 1);
        Vector2 size = new(112 - Tier * 4, 66 - Tier * 2);
        _quotationSheets[0] = new Rect2(new Vector2(Field.Left + 64, 68).Lerp(new Vector2(Field.Right - 64, 146), progress) - size * .5f, size);
        _quotationSheets[1] = new Rect2(new Vector2(Field.Right - 64, 150).Lerp(new Vector2(Field.Left + 64, 70), progress) - size * .5f, size);
        if (Tier >= 2)
        {
            float bend = 16 * Mathf.Sin(progress * Mathf.Pi);
            _quotationSheets[0] = new Rect2(new Vector2(Mathf.Lerp(Field.Left + 56, Field.Right - 56, progress), 60 + bend) - size * .5f, size);
            _quotationSheets[1] = new Rect2(new Vector2(Mathf.Lerp(Field.Right - 56, Field.Left + 56, progress), Field.Bottom - 60 - bend) - size * .5f, size);
        }
        float halfWidth = 3.5f + Tier * .5f;
        bool live = Time >= _warning;
        float warning = (float)(Time / _warning);
        if (Tier >= 2)
            for (int line = 0; line < 12; line++)
            {
                float y = Field.Top + (line + .5f) * Field.Height / 12;
                MaskedQuote(new Vector2(Field.Left - 12, y), new Vector2(Field.Right + 12, y), Field.Height / 24, 0, live, warning);
            }
        else
        {
            float advance = progress * (Field.Height + 8);
            MaskedQuote(new Vector2(Field.Left - 8, advance - 35), new Vector2(Field.Right + 8, advance + 35), halfWidth, 0, live, warning);
            MaskedQuote(new Vector2(Field.Left - 8, Field.Bottom + 4 - advance), new Vector2(Field.Right + 8, Field.Bottom + 4 - advance), halfWidth, 0, live, warning);
            MaskedQuote(new Vector2(Field.Left - 8, advance + 35), new Vector2(Field.Right + 8, advance - 35), halfWidth, 0, live, warning);
        }
        for (int sheet = 0; sheet < 2; sheet++)
        {
            var paper = _quotationSheets[sheet];
            float drift = Tier >= 2 ? 14 * Mathf.Sin((float)Math.Max(0, Time - _warning) * 1.1f + sheet * Mathf.Pi) : 0;
            for (int row = -1; row <= 1; row += 2)
                MaskedQuote(new Vector2(paper.Position.X + 10, paper.GetCenter().Y + row * 17 + drift),
                    new Vector2(paper.End.X - 10, paper.GetCenter().Y + row * 17 + drift), halfWidth, sheet + 1, live, warning);
            Cue(new Vector2(paper.GetCenter().X, paper.Position.Y + 9), AttackTelegraph.Symbol.Stack, count: sheet + 1);
        }
    }

    private void MaskedQuote(Vector2 a, Vector2 b, float halfWidth, int firstCover, bool live, float progress)
    {
        var fragments = new List<(Vector2 A, Vector2 B)> { (a, b) };
        for (int cover = firstCover; cover < _quotationSheets.Length; cover++)
        {
            var paper = _quotationSheets[cover].Grow(halfWidth);
            var visible = new List<(Vector2 A, Vector2 B)>();
            foreach (var fragment in fragments)
            {
                Vector2 delta = fragment.B - fragment.A;
                float entry = 0, exit = 1;
                bool intersects = true;
                for (int axis = 0; axis < 2; axis++)
                {
                    float start = axis == 0 ? fragment.A.X : fragment.A.Y;
                    float distance = axis == 0 ? delta.X : delta.Y;
                    float minimum = axis == 0 ? paper.Position.X : paper.Position.Y;
                    float maximum = axis == 0 ? paper.End.X : paper.End.Y;
                    if (Mathf.Abs(distance) < .001f)
                    {
                        if (start < minimum || start > maximum) intersects = false;
                    }
                    else
                    {
                        float t0 = (minimum - start) / distance, t1 = (maximum - start) / distance;
                        entry = Mathf.Max(entry, Mathf.Min(t0, t1));
                        exit = Mathf.Min(exit, Mathf.Max(t0, t1));
                    }
                }
                if (!intersects || entry >= exit) visible.Add(fragment);
                else
                {
                    if (entry > 0) visible.Add((fragment.A, fragment.A.Lerp(fragment.B, entry)));
                    if (exit < 1) visible.Add((fragment.A.Lerp(fragment.B, exit), fragment.B));
                }
            }
            fragments = visible;
        }
        foreach (var fragment in fragments)
            if (fragment.A.DistanceSquaredTo(fragment.B) > .01f)
                Line(fragment.A, fragment.B, halfWidth, live, progress);
    }

    private void RepostTrail()
    {
        int copies = Math.Max(3, Copies);
        double interval = 1.65 - Tier * .12;
        double preview = Tier == 0 ? _warning : 1.35;
        double replayDelay = Tier == 0 ? 3.2 : 2.25;
        float radius = 4 + Tier * .5f;
        for (int edition = 0; edition < copies; edition++)
        {
            double notice = edition * interval, publish = notice + preview;
            if (_quotations.Count <= edition && Time >= notice)
            {
                float side = _forward.X > 0 ? Field.Left : Field.Right;
                Vector2 origin = edition == 0 ? new Vector2(side, Mathf.Clamp(Field.Top + Field.Bottom - Player.GlobalPosition.Y, 18, Field.Bottom - 18))
                    : _quotations[edition - 1].B;
                Vector2 direction = origin.DirectionTo(Player.GlobalPosition);
                Vector2 end = SourceCutEdge(Player.GlobalPosition, direction, Field.Rect);
                _quotations.Add((origin, end, publish + replayDelay));
            }
            if (_quotations.Count <= edition) continue;
            var quote = _quotations[edition];
            Vector2 aim = quote.A.DirectionTo(quote.B);
            Cue(quote.A, aim, notice, publish, quote.A.DistanceTo(quote.B), radius);
            int pulses = 2 + Tier;
            for (int pulse = 0; pulse < pulses; pulse++)
                if (At(publish + pulse * .21))
                {
                    var word = Seed(quote.A, aim, edition);
                    word.MakeReiSplit(0);
                    word.UseBossIllustration(WarningVisual, fitRadius: radius);
                    word.Velocity = aim * Speed;
                    word.CancelLeadIn();
                }
            double replayLead = Tier == 0 ? 2.1 : 1.25;
            if (Time >= quote.At - replayLead && Time < quote.At + interval * 2)
                Line(quote.A, quote.B, radius, Time >= quote.At, (float)((Time - quote.At + replayLead) / replayLead));
        }
        DrainAfter((copies - 1) * interval + preview + replayDelay + interval * 2);
    }

    private void Paragraph()
    {
        double enter = _warning + 2.7;
        if (Time < enter)
        {
            Line(Side(_source.Y), Across(Field.Width - 20, _source.Y), 24 + Copies * 2,
                Time >= _warning, (float)(Time / _warning));
        }
        if (At(_warning))
        {
            for (int i = 0; i < Copies; i++)
            {
                var word = HoldWord(Side(_source.Y + (i - (Copies - 1) * .5f) * 8), _forward);
                Release(word, _forward, false);
            }
        }
        foreach (var word in _words.ToArray())
        {
            if (!Exists(word) || word.Bullet.LeadIn) continue;
            if (word.Stage == 0 && word.Start.DistanceTo(word.Bullet.GlobalPosition) >= 42)
            {
                var children = Fork(word, new[] { _forward, _forward, _forward }, 1);
                if (children.Count == 0) word.Stage = 3;
                _words.AddRange(children);
            }
            else if (word.Stage == 1)
            {
                float side = word.Bullet.GlobalPosition.Y < _source.Y ? -1 : 1;
                var outward = new Vector2(_forward.X * .5f, side).Normalized();
                var splitPoint = word.Start + _forward * 32;
                Line(splitPoint, splitPoint + outward * 64, 1.2f, false,
                    word.Start.DistanceTo(word.Bullet.GlobalPosition) / 32);
                if (word.Start.DistanceTo(word.Bullet.GlobalPosition) >= 32)
                {
                    var children = Fork(word, new[] { outward.Rotated(-.14f), outward, outward.Rotated(.14f) }, 2);
                    if (children.Count == 0) word.Stage = 3;
                }
            }
        }
        DrainAfter(enter + 1);
    }

    private float SpliceX => Tier >= 2 ? Mathf.Clamp(_anchor.X + (_anchor.X < Field.CenterX ? 50 : -50), Field.Left + 20, Field.Right - 20)
        : Mathf.Clamp(_anchor.X + _forward.X * 54, Field.Left + 78, Field.Right - 78);
    private double _spliceAcrossAt;
    private Vector2 _crossSplice, _crossSpliceExit;
    internal Vector2 VisibleSpliceCenter => _spliceAcrossAt > 0 ? _crossSplice : new(SpliceX, Mathf.Clamp(_anchor.Y, 72, 144));
    internal Vector2 VisibleSpliceExit => _spliceAcrossAt > 0 ? _crossSpliceExit : Vector2.Right * SpliceExitSide;
    private float SpliceExitSide => _anchor.X < SpliceX ? 1 : -1;

    private float SpliceHalfGap => Tier == 2 && Time >= _warning + 1.8 ? 0 : 6 + Tier + TargetOpening / 2;
    private void Splice()
    {
        float x = SpliceX;
        float seam = Mathf.Clamp(_anchor.Y, 72, 144);
        double close = _warning + 1.8;
        if (Time >= close + 1.6)
        {
            if (_spliceAcrossAt == 0)
            {
                float y = Mathf.Clamp(Player.GlobalPosition.Y + (Player.GlobalPosition.Y < Field.CenterY ? 48 : -48), Field.Top + 32, Field.Bottom - 32);
                _crossSplice = new Vector2(x, y);
                _crossSpliceExit = y > Player.GlobalPosition.Y ? Vector2.Down : Vector2.Up;
                _spliceAcrossAt = Time + WarnFor(Player.GlobalPosition.DistanceTo(_crossSplice) + 18, 1.8);
            }
            double seal = _spliceAcrossAt + 1.35;
            if (Time >= seal + 1.15) { DrainAfter(0); return; }
            float gap = (20 - Tier) * Mathf.Clamp((float)((seal - Time) / 1.35), 0, 1);
            bool live = Time >= _spliceAcrossAt;
            Line(new Vector2(Field.Left, _crossSplice.Y), _crossSplice - Vector2.Right * gap, 5 + Tier, live);
            Line(_crossSplice + Vector2.Right * gap, new Vector2(Field.Right, _crossSplice.Y), 5 + Tier, live);
            Vector2 oldPage = _crossSpliceExit.Y > 0 ? Field.Rect.Position : new Vector2(Field.Left, _crossSplice.Y);
            float height = _crossSpliceExit.Y > 0 ? _crossSplice.Y - Field.Top : Field.Bottom - _crossSplice.Y;
            Box(new Rect2(oldPage, new Vector2(Field.Width, height)), Time >= seal);
            return;
        }
        float halfGap = SpliceHalfGap;
        float lipTop = seam - halfGap, lipBottom = seam + halfGap;
        if (Time < close + 1.6)
        {
            Line(new Vector2(x, Tier >= 2 ? Field.Top : 12), new Vector2(x, seam - halfGap), 6 + Tier, Time >= _warning, (float)(Time / _warning));
            Line(new Vector2(x, seam + halfGap), new Vector2(x, Tier >= 2 ? Field.Bottom : 204), 6 + Tier, Time >= _warning, (float)(Time / _warning));
            if (Time >= _warning - .8)
            {
                var cut = _anchor.X < x ? new Rect2(Field.Left, Field.Top, x - Field.Left, Field.Height)
                    : new Rect2(x, Field.Top, Field.Right - x, Field.Height);
                Box(cut, Time >= close, Mathf.Clamp((float)((Time - _warning + .8) / .45), 0, 1));
            }
        }
        int folds = Tier == 2 ? 2 : Tier == 0 ? 3 : 4;
        for (int fold = 0; fold < folds; fold++)
        {
            double fire = _warning + fold * (Tier == 2 ? .9 : (6 + TargetOpening) / Speed);
            if (Tier == 2)
            {
                if (!At(fire)) continue;
                Seed(new Vector2(x, seam - 50 + fold * 16), Vector2.Down, fold);
                Seed(new Vector2(x, seam + 50 - fold * 16), Vector2.Up, fold);
            }
            else
            {
                Vector2 heading = Vector2.Right * SpliceExitSide;
                float edge = heading.X > 0 ? Field.Right : Field.Left;
                for (int side = -1; side <= 1; side += 2)
                {
                    float lip = seam + side * TargetOpening / 2;
                    Vector2 origin = new(x, lip + side * 3);
                    if (Time >= fire - _warning && Time < fire)
                        Box(new Rect2(Math.Min(x, edge), side < 0 ? lip - 6 : lip, Math.Abs(x - edge), 6), false);
                    if (!At(fire)) continue;
                    var film = Seed(origin, heading, fold);
                    film.UseBossIllustration(BulletArt.EnemyVisual("rei_film", "c7a4ff"), fitRadius: 3);
                    film.CancelLeadIn();
                    film.MakeReiSplit(0);
                    film.Homing = film.Accel = false;
                    film.Velocity = heading * Speed;
                    film.Rotation = heading.Angle();
                    film.GlobalPosition = origin;
                    Rect2 bounds = film.GlobalTransform * film.Footprint!.Bounds;
                    film.GlobalPosition += Vector2.Down * (lip - (side < 0 ? bounds.End.Y : bounds.Position.Y));
                }
            }
            if (Tier >= 2 && fold < 2 && At(fire)) Seed(Side(seam + (fold == 0 ? -70 : 70)), _forward, fold);
        }
    }

    private void PeelPage()
    {
        double fade = PageSweepEnd;
        float travel = Mathf.Clamp((float)(Time - _warning) * PageSpeed, 0, Field.Width + 2);
        var targets = GetTree().GetNodesInGroup("player_bullets").OfType<Bullet>().Where(b => b.Active && !b.IsEnemy).ToArray();
        for (int page = 0; page < _pages.Count; page++)
        {
            var state = _pages[page];
            state.Rect = new Rect2(_pageStartX + _forward.X * travel - 8, _pageTop + page * _pageHeight, 16, _pageHeight);
            _pages[page] = state;
            if (state.Hits >= 3) continue;
            foreach (var bullet in targets)
            {
                if (!bullet.Active) continue;
                var pageTransform = new Transform2D(0, state.Rect.GetCenter());
                bool hits;
                if (bullet.Footprint is { } footprint)
                    hits = footprint.Shapes.Any(s => s.Collide(bullet.GlobalTransform, _pageShape!, pageTransform));
                else
                {
                    _roundShot!.Radius = bullet.Radius;
                    hits = _roundShot.Collide(bullet.GlobalTransform, _pageShape!, pageTransform);
                }
                if (!hits) continue;
                state.Hits++;
                Pool.Despawn(bullet);
                if (state.Hits >= 3) break;
            }
            _pages[page] = state;
            if (state.Hits >= 3 || Time >= fade) continue;
            Box(state.Rect, Time >= _warning, (float)(Time / _warning));
            Cue(state.Rect.GetCenter() + Vector2.Right * 17, AttackTelegraph.Symbol.Stack, count: 3 - state.Hits);
        }
        if (_pages.All(page => page.Hits >= 3)) Finish();
        else DrainAfter(fade + .2);
    }

    private void RewriteEnding()
    {
        Cue(_source, _forward, 0, _warning, 120);
        int originals = Tier + 1;
        for (int i = 0; i < originals; i++)
        {
            if (At(_warning + i * 1.4))
            {
                var word = HoldWord(Side(i % 2 == 0 ? 70 : 146), _forward);
                Release(word, _forward, false);
            }
        }
        foreach (var word in _words.ToArray())
        {
            if (!Exists(word) || word.Bullet.LeadIn) continue;
            if (word.Stage == 0 && word.Start.DistanceTo(word.Bullet.GlobalPosition) >= 42)
            {
                var children = Fork(word, new[] { _forward, _forward, _forward }, 1);
                if (children.Count == 0) word.Stage = 4;
                _words.AddRange(children);
            }
            else if (word.Stage == 1 && word.Start.DistanceTo(word.Bullet.GlobalPosition) >= 32)
            {
                word.Stage = 2;
                word.Bullet.Velocity = Vector2.Zero;
                word.Bullet.HoldBossShot();
                word.Ready = Time + WarnFor(48, 1.4);
                word.Direction = (_forward + Vector2.Down * (word.Bullet.GlobalPosition.Y < 108 ? .9f : -.9f)).Normalized();
            }
            else if (word.Stage == 2)
            {
                word.Bullet.HoldBossShot();
                Cue(word.Bullet.GlobalPosition, word.Direction, word.Ready - WarnFor(48, 1.4), word.Ready, 120);
                Cue(word.Bullet.GlobalPosition + Vector2.Up * 15, AttackTelegraph.Symbol.Cut);
                if (Time >= word.Ready)
                {
                    word.Bullet.Velocity = word.Direction * Speed * .9f;
                    var children = Fork(word, new[] { word.Direction.Rotated(-.15f), word.Direction, word.Direction.Rotated(.15f) }, 2);
                    if (children.Count == 0) Release(word, word.Direction, false);
                    word.Stage = 4;
                }
            }
        }
        DrainAfter(_warning + (originals - 1) * 1.4 + 5);
    }

    private Rect2 CitationCell(int node)
    {
        var size = Field.Rect.Size / 3;
        return new Rect2(Field.Rect.Position + new Vector2(node % 3, node / 3) * size, size);
    }


    private static Vector2 CitationSource(int family) => new(Field.Left + (family + .5f) * Field.Width / 3, 12);
    private double CitationCycle => _warning + 1.15;
    internal bool CitationPublished => Time % CitationCycle >= _warning;
    internal int RetractedSource => (_firstRetractedSource + (int)(Time / CitationCycle)) % 3;

    private void QuoteChain()
    {
        int edition = (int)(Time / CitationCycle);
        if (edition >= 3) { Finish(); return; }
        if (!CitationPublished) return;
        for (int node = 0; node < _citationFamily.Length; node++)
        {
            if (_citationFamily[node] != RetractedSource) Box(CitationCell(node), true);
        }
    }

    private void DrawCitations()
    {
        double age = Time % CitationCycle;
        for (int node = 0; node < _citationFamily.Length; node++)
        {
            int family = _citationFamily[node], parent = _citationParent[family];
            var rect = CitationCell(node);
            Vector2 center = rect.GetCenter();
            bool withdrawn = family == RetractedSource;
            double fadeAt = _warning + (node == parent ? 0 : .30);
            if (withdrawn && age >= fadeAt) continue;
            Vector2 from = parent == node ? CitationSource(family) + Vector2.Down * 6 : CitationCell(parent).GetCenter();
            DrawDashedLine(from, center, new Color(Ink, .68f), .8f, 3, true);
            DrawRect(rect.Grow(-1), new Color(Original, .48f), false, .7f);
            DrawQuotation(center, 7, Ink);
            DrawCitationSeal(center + Vector2.Down * 13, family, false, Original);
        }
        for (int family = 0; family < 3; family++)
        {
            bool withdrawn = family == RetractedSource;
            Vector2 at = CitationSource(family);
            DrawCitationSeal(at, family, withdrawn, withdrawn ? Original : Ink);
            if (withdrawn) DrawLine(at + new Vector2(-14, 3), at + new Vector2(14, 3), Original, 1, true);
        }
    }

    private void QuotationReversal()
    {
        var quote = new Rect2(Field.CenterX - 66, 62, 132, 92);
        double at = _warning;
        int readings = Tier == 0 ? 3 : 4 + Tier / 2;
        double liveFor = 1.1 + Tier * .12;
        for (int reading = 0; reading < readings; reading++)
        {
            bool negateOutside = reading % 2 == 1;
            double warn = reading == 0 ? _warning : WarnFor(negateOutside ? 136 : 58, negateOutside ? 2.8 : 1.55);
            if (reading > 0) at += warn;
            bool live = Time >= at;
            if (Time >= at - warn && Time < at + liveFor)
            {
                float progress = (float)Math.Clamp((Time - at + warn) / warn, 0, 1);
                float overlap = 2 + Tier;
                VisibleBracket = (quote, negateOutside, live);
                if (negateOutside)
                {
                    var inner = quote.Grow(-overlap);
                    Box(new Rect2(Field.Left, 0, inner.Position.X - Field.Left, Field.Bottom), live, progress);
                    Box(new Rect2(inner.End.X, 0, Field.Right - inner.End.X, Field.Bottom), live, progress);
                    Box(new Rect2(inner.Position.X, 0, inner.Size.X, inner.Position.Y), live, progress);
                    Box(new Rect2(inner.Position.X, inner.End.Y, inner.Size.X, Field.Bottom - inner.End.Y), live, progress);
                }
                else Box(quote.Grow(overlap), live, progress);
            }
            at += liveFor;
        }
        if (Time >= at) Finish();
    }

    private void DrawCameoQuotes()
    {
        Color ink = Variant == 0 ? new Color("e49bc3") : new Color("f5b783");
        foreach (var hazard in HazardShapes)
        {
            var rect = new Rect2(hazard.A, hazard.B - hazard.A);
            if (!hazard.Live) continue;
            DrawRect(rect, new Color(ink.Darkened(.48f), UsesDangerForecast ? .75f : .49f));
            DrawRect(rect.Grow(-.7f), new Color(ink, .88f), false, 1.2f);
            var center = rect.GetCenter();
            BossProjectileArt.Get("rei_film").Draw(this, new Rect2(center - new Vector2(11, 9), new Vector2(22, 18)), Time, Colors.White);
        }
        if (Variant == 0) { DrawCitations(); return; }
        var quote = new Rect2(Field.CenterX - 66, 62, 132, 92);
        DrawCropCorner(quote.Position, 12, Vector2.One, ink);
        DrawCropCorner(quote.End, 12, -Vector2.One, ink);
        if (VisibleBracket is { } fold)
        {
            foreach (Vector2 normal in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right })
            {
                float distance = normal.X == 0 ? fold.Inner.Size.Y / 2 : fold.Inner.Size.X / 2;
                Vector2 at = fold.Inner.GetCenter() + normal * distance;
                DrawQuotation(at, 5, Original);
                Vector2 direction = fold.Turned ? normal : -normal;
                AttackTelegraph.Arrow(this, at + direction * 8, direction, Original);
            }
        }
    }

    private void StartSourceCut()
    {
        Identify("出典は、わたし", "rei-enemy-fire-strips-source",
            "装置の照準を引用元の印に通す。固定されたら切断線の脇へ。合わせた向きに原稿が切れ、命中すると盾が一枚剥がれる"
            + (Tier >= 2 ? "。外れた原稿からは断片が飛ぶ" : ""));
        SourceCutAnchor = Boss.TechniqueCenter.Clamp(Field.Rect.Position + Vector2.One * 84,
            Field.Rect.End - Vector2.One * 84);
        float first = SourceCutAnchor.DirectionTo(Player.GlobalPosition).Angle();
        _sourceCutDirections = Enumerable.Range(0, 16).Select(i => Vector2.FromAngle(first + i * Mathf.Tau / 16))
            .Where(direction => {
                float distance = SourceCutAnchor.DistanceTo(SourceCutEdge(SourceCutAnchor, -direction, Field.Rect.Grow(-8)));
                return distance >= 64 && (Field.Rect.Grow(-12).HasPoint(SourceCutAnchor + direction * 72)
                    || distance >= 96 && Field.Rect.Grow(-12).HasPoint(SourceCutAnchor - direction * 72));
            }).ToArray();
        if (Tier >= 2)
        {
            // Later locks must not require aiming along a still-active earlier cut.
            var axes = new List<Vector2>();
            foreach (Vector2 direction in _sourceCutDirections)
                if (axes.All(axis => Math.Abs(axis.Dot(direction)) < .999f)) axes.Add(direction);
            _sourceCutDirections = axes.OrderBy(axis => Mathf.PosMod(axis.Angle() - first, Mathf.Pi)).ToArray();
        }
        _ = BulletArt.Get("rei_film");
    }

    private static Vector2 SourceCutEdge(Vector2 from, Vector2 direction, Rect2 bounds)
    {
        float distance = float.PositiveInfinity;
        if (direction.X > .0001f) distance = Math.Min(distance, (bounds.End.X - from.X) / direction.X);
        if (direction.X < -.0001f) distance = Math.Min(distance, (bounds.Position.X - from.X) / direction.X);
        if (direction.Y > .0001f) distance = Math.Min(distance, (bounds.End.Y - from.Y) / direction.Y);
        if (direction.Y < -.0001f) distance = Math.Min(distance, (bounds.Position.Y - from.Y) / direction.Y);
        return from + direction * distance;
    }

    private void BeginSourceCutRound()
    {
        int index = SourceCutRounds.Count;
        Vector2 direction = _sourceCutDirections[index * _sourceCutDirections.Length / SourceCutCount];
        Vector2 target = SourceCutAnchor + direction * 72;
        if (!Field.Rect.Grow(-12).HasPoint(target)) target = SourceCutAnchor - direction * 72;
        double track = Math.Max(2.8, Player.GlobalPosition.DistanceTo(target) / Player.SlowestMoveSpeed + 1.6);
        double locked = Tier >= 2 && index > 0 ? SourceCutRounds[^1].LocksAt + SourceCutCadence : Time + track;
        double fire = locked + SourceCutGrow + SourceCutHold;
        SourceCutRounds.Add(new SourceCutRound {
            From = SourceCutEdge(SourceCutAnchor, -direction, Field.Rect.Grow(-8)), Target = target,
            StartsAt = Time, LocksAt = locked, FiresAt = fire,
            EndsAt = fire + 2 * SourceCutSpacing + SourceCutLive,
        });
    }

    private void StepSourceCut()
    {
        if (!_sourceCutBound)
        {
            _sourceCutBound = true;
            Ended += _ => Boss.SetAnimalWindup(false);
            TreeExiting += () => { if (IsInstanceValid(Boss)) Boss.SetAnimalWindup(false); };
            BeginSourceCutRound();
        }
        var previous = SourceCutRounds[^1];
        if (SourceCutRounds.Count < SourceCutCount && previous.Locked
            && Time >= (Tier >= 2 ? previous.LocksAt + .12 : previous.EndsAt - .4))
            BeginSourceCutRound();
        foreach (var round in SourceCutRounds)
        {
            if (!round.Locked)
            {
                Vector2 aim = Player.GlobalPosition - round.From;
                if (aim.LengthSquared() < .01f) aim = SourceCutAnchor - round.From;
                round.To = SourceCutEdge(round.From, aim.Normalized(), Field.Rect.Grow(8));
                round.Aligned = Geometry2D.GetClosestPointToSegment(SourceCutAnchor, round.From, round.To)
                    .DistanceTo(SourceCutAnchor) <= SourceCutTargetRadius + SourceCutWidth;
                if (Time >= round.LocksAt)
                {
                    round.Locked = true;
                    round.LocksAt = Time;
                    round.FiresAt = Time + SourceCutGrow + SourceCutHold;
                    round.EndsAt = round.FiresAt + 2 * SourceCutSpacing + SourceCutLive;
                    int index = SourceCutRounds.IndexOf(round);
                    for (int pulse = 0; pulse < 3; pulse++)
                        AddEasyCut($"source-cut:{index}:{pulse}", round.From, round.To, SourceCutWidth,
                            round.LocksAt, round.FiresAt + pulse * SourceCutSpacing,
                            round.FiresAt + pulse * SourceCutSpacing + SourceCutLive);
                    Audio.Instance?.Se(Audio.Instance.SfxUiMove, -16, .85f);
                }
            }
            while (round.Locked && round.FiredPulses < 3
                && Time >= round.FiresAt + round.FiredPulses * SourceCutSpacing)
            {
                round.FiredPulses++;
                if (Evolved) Boss.ShowAnimalAttack();
                if (round.FiredPulses == 1)
                {
                    Audio.Instance?.PlaySpell();
                    round.Replied = Geometry2D.GetClosestPointToSegment(SourceCutAnchor, round.From, round.To)
                        .DistanceTo(SourceCutAnchor) <= SourceCutTargetRadius + SourceCutWidth;
                    if (round.Replied)
                    {
                        round.RemovedPanels = Boss.TryStripPanelFromAttack(round.From) ? 1 : 0;
                        Audio.Instance?.PlayBossHit(4);
                        GameCamera.Instance?.Shake(1.2f, .08f);
                    }
                    else if (Tier == 3)
                    {
                        round.CounterFrom = round.To;
                        round.CounterTo = SourceCutEdge(round.CounterFrom,
                            round.CounterFrom.DirectionTo(Player.GlobalPosition), Field.Rect.Grow(8));
                        round.CounterStartsAt = Time;
                        round.CounterFiresAt = Time + SourceCutGrow + SourceCutHold;
                        round.CounterEndsAt = round.CounterFiresAt + SourceCutLive;
                        AddEasyCut($"source-cut:{SourceCutRounds.IndexOf(round)}:misquote", round.CounterFrom,
                            round.CounterTo, SourceCutWidth, Time, round.CounterFiresAt, round.CounterEndsAt);
                    }
                }
            }
            if (Tier >= 2 && round.Locked && !round.Aligned)
                foreach (var fragment in SourceFragments(round))
                {
                    if (Time < fragment.Fire) Line(fragment.From, fragment.From + fragment.Direction * 400, 3, false);
                    if (At(fragment.Fire))
                    {
                        var shot = Shot(fragment.From, fragment.Direction);
                        shot.Homing = shot.Accel = false;
                        shot.MakeReiSplit(0);
                        shot.UseBossIllustration(BulletArt.EnemyVisual("rei_film", "c7a4ff"), fitRadius: 3);
                        shot.Velocity = fragment.Direction * 40;
                        shot.CancelLeadIn();
                    }
                }
        }
        foreach (var cut in _easyCuts)
        {
            cut.Fired = Time >= cut.FiresAt;
            if (Time < cut.EndsAt)
                Line(cut.A, cut.B, cut.HalfWidth, cut.Fired,
                    Mathf.Clamp((float)((Time - cut.StartsAt) / SourceCutGrow), 0, 1));
        }
        if (Evolved)
        {
            double last = SourceCutRounds.Where(round => round.FiredPulses > 0)
                .Select(round => round.FiresAt + (round.FiredPulses - 1) * SourceCutSpacing)
                .DefaultIfEmpty(double.NegativeInfinity).Max();
            if (Time - last >= .65) Boss.SetAnimalWindup(SourceCutRounds.Any(round => Time < round.FiresAt));
        }
        if (SourceCutRounds.Count == SourceCutCount && Time >= SourceCutRounds[^1].EndsAt
            && _easyCuts.All(cut => Time >= cut.EndsAt) && !OwnedShots.Any()) { Finish(); return; }
    }


    internal IEnumerable<(Vector2 From, Vector2 Direction, double Fire)> SourceFragments(SourceCutRound round)
    {
        Vector2 normal = round.From.DirectionTo(round.To).Orthogonal();
        int divisions = 3 + Tier;
        for (int piece = 1; piece < divisions; piece++)
        for (int side = -1; side <= 1; side += 2)
        for (int pulse = 0; pulse <= (Tier == 3 ? 2 : 1); pulse++)
            yield return (round.From.Lerp(round.To, piece / (float)divisions), normal * side,
                round.FiresAt + pulse * SourceCutSpacing / (Tier == 2 ? 3 : 1));
    }

    private void DrawSourceCut()
    {
        if (Tier >= 2 && GameManager.ShowAttackTelegraphs)
            foreach (var round in SourceCutRounds.Where(round => round.Locked && !round.Aligned))
            foreach (var fragment in SourceFragments(round).Where(fragment => Time < round.FiresAt && fragment.Fire == round.FiresAt))
                AttackTelegraph.Route(this, fragment.From, fragment.From + fragment.Direction * 400, 3,
                    true, true, false, false, BulletArt.EnemyVisual("rei_film", "c7a4ff"));
        if (Tier >= 2 && SourceCutRounds.Count < SourceCutCount && GameManager.ShowAttackTelegraphs)
        {
            Vector2 direction = _sourceCutDirections[SourceCutRounds.Count * _sourceCutDirections.Length / SourceCutCount];
            Vector2 from = SourceCutEdge(SourceCutAnchor, -direction, Field.Rect.Grow(-8));
            DrawFilm(from, new Vector2(11, 15), new Color(Ink, .55f));
            AttackTelegraph.DrawSymbol(this, from + from.DirectionTo(SourceCutAnchor) * 14, AttackTelegraph.Symbol.Stack, Ink);
        }
        foreach (var round in SourceCutRounds)
        {
            if (round.CounterEndsAt > Time)
            {
                Color correction = AttackTelegraph.Instant;
                if (Time < round.CounterFiresAt && GameManager.ShowAttackTelegraphs)
                {
                    AttackTelegraph.InstantBand(this, round.CounterFrom, round.CounterTo, SourceCutWidth,
                        Mathf.Clamp((float)((Time - round.CounterStartsAt) / SourceCutGrow), 0, 1),
                        (float)((round.CounterFiresAt - Time) / (round.CounterFiresAt - round.CounterStartsAt)));
                    AttackTelegraph.Arrow(this, round.CounterFrom.Lerp(round.CounterTo, .2f),
                        round.CounterFrom.DirectionTo(round.CounterTo), correction);
                }
                DrawFilm(round.CounterFrom, new Vector2(15, 20), Ink);
                AttackTelegraph.DrawSymbol(this, round.CounterFrom.Lerp(round.CounterTo, .16f), AttackTelegraph.Symbol.Cut, correction);
            }
            if (Time >= round.EndsAt) continue;
            Color ink = round.Locked ? AttackTelegraph.Instant : Original;
            Vector2 direction = round.From.DirectionTo(round.To);
            if (GameManager.ShowAttackTelegraphs)
            {
                if (!round.Locked)
                {
                    DrawDashedLine(round.From, round.To, new Color(WarningInk, .6f), 1.5f, 10, true);
                    DrawDashedLine(round.From, round.To, new Color(ink, .7f), .65f, 10, true);
                    DrawArc(Player.GlobalPosition, 8, 0, Mathf.Tau, 20, ink, .9f, true);
                }
                else if (Time < round.FiresAt)
                {
                    float progress = Mathf.Clamp((float)((Time - round.LocksAt) / SourceCutGrow), 0, 1);
                    AttackTelegraph.InstantBand(this, round.From, round.To, SourceCutWidth, progress,
                        (float)((round.FiresAt - Time) / (round.FiresAt - round.LocksAt)));
                    for (float d = 24; d < round.From.DistanceTo(round.To) * progress; d += 48)
                        AttackTelegraph.Arrow(this, round.From + direction * d, direction, ink);
                }
                else DrawDashedLine(round.From, round.To, new Color(ink, .45f), .7f, 6, true);
            }
            DrawFilm(round.From, new Vector2(15, 20), new Color(Ink, .9f));
            DrawQuotation(round.From, 5, Original);
            float remaining = !round.Locked
                ? (float)((round.LocksAt - Time) / (round.LocksAt - round.StartsAt))
                : (float)((round.FiresAt - Time) / (round.FiresAt - round.LocksAt));
            AttackTelegraph.Origin(this, round.From, ink, Mathf.Clamp(remaining, 0, 1));
            Vector2 caption = round.From + new Vector2(direction.X >= 0 ? 19 : -19, direction.Y >= 0 ? 15 : -15);
            AttackTelegraph.DrawSymbol(this, caption, !round.Locked ? AttackTelegraph.Symbol.Target : AttackTelegraph.Symbol.Locked, ink);
        }
        var current = SourceCutRounds.LastOrDefault(round => Time < round.EndsAt);
        bool linedUp = current?.Aligned == true;
        Color targetInk = linedUp ? Original : Ink;
        DrawCircle(SourceCutAnchor, SourceCutTargetRadius, new Color(WarningInk, .15f));
        DrawArc(SourceCutAnchor, SourceCutTargetRadius, 0, Mathf.Tau, 32, WarningInk, 1.5f, true);
        DrawArc(SourceCutAnchor, SourceCutTargetRadius, 0, Mathf.Tau, 32, targetInk, .65f, true);
        DrawQuotation(SourceCutAnchor, SourceCutTargetRadius * .714f, targetInk);
        var hit = SourceCutRounds.LastOrDefault(round => round.Replied && Time < round.FiresAt + 1.2);
        bool aiming = current != null && Time < current.FiresAt;
        AttackTelegraph.DrawSymbol(this, SourceCutAnchor + Vector2.Down * 25, linedUp ? AttackTelegraph.Symbol.Locked : AttackTelegraph.Symbol.Target, targetInk);
        if (aiming && hit != null)
            AttackTelegraph.DrawSymbol(this, hit.From + hit.From.DirectionTo(SourceCutAnchor) * 28, AttackTelegraph.Symbol.Cut, Original);
    }

    internal enum RallyStage { Gather, Feint, Incoming, Returning, Done, Closing }
    internal sealed class RallyBall
    {
        internal Panel Node = null!;
        internal int Index, Attempts, Returns, DamageEvents;
        internal int LeftBounces, RightBounces, TopBounces, BottomBounces;
        internal bool IsDanger, Revealed, ReturnEligible, Contacting, MissedPass;
        internal RallyStage Stage;
        internal Vector2 Position, From, FeintOrigin, Velocity;
        internal double StageAt, LaunchAt, ArrivalAt;
        internal ulong EligibleAfterVolley;
    }
    internal readonly List<RallyBall> RallyBalls = new();
    internal int RallyFailures, RallyStreak, RallyDamageEvents;
    internal int RallyMissRetaliations;
    private double _rallyMissAt = -1;
    private double _rallyMissLead = 2.2;
    private float _rallyGapY;
    internal bool RallyWallBroken;
    internal const int RallyFailureLimit = 3;
    internal const float RallyRadius = 15;
    internal float RallyWallX => Field.Right - 95;
    internal Rect2 RallyPlayerBounds => Player.RallyMovementBounds ?? Field.Rect;
    internal Rect2 RallyReceiver => RallyPlayerBounds;
    internal Rect2 RallyReturnWindowBounds
    {
        get
        {
            float halfWidth = Tier switch { 0 => 22, 1 => 18, 2 => 14, _ => 12 };
            return new Rect2(Field.Left + 65 - halfWidth, Field.Top, halfWidth * 2, Field.Height);
        }
    }
    private const float RallyReturnSpeed = 210, RallyBossSpeed = 72;
    private Vector2 RallyOrigin => new(Field.Right - RallyRadius - 9,
        Mathf.Clamp(Boss.TechniqueCenter.Y, RallyRadius + 12, Field.Bottom - RallyRadius - 12));
    private readonly List<(Panel Panel, bool Invulnerable)> _rallyStoredPanels = new();
    private readonly HashSet<ulong> _rallyUsedVolleys = new();
    private readonly Dictionary<ulong, (ulong Activation, Vector2 Position)> _rallyShotPositions = new();
    private readonly List<(double At, float Y)> _rallyArrivals = new();
    private bool _rallyBound, _rallyCleaned, _rallyPreviousGuard, _rallyEntered, _rallyBodyContactRestored, _rallyClosing;
    private double _rallyFormationAt, _rallyNextArrivalAt, _rallyFinishAt = double.PositiveInfinity, _rallyLastThrow = -10, _rallyWallHitAt = -10;
    private float _rallyLastArrivalY;
    private ReiRallyVisuals? _rallyVisuals;
    private Vector2 _rallyEntryTarget;
    private Rect2? _rallyPreviousBounds;
    private Node2D? _rallyPreviousLockTarget;
    private int RallyAvatarFrame => Time - _rallyLastThrow < .8
        ? Math.Min(7, 4 + (int)((Time - _rallyLastThrow) / .2))
        : RallyBalls.Any(ball => ball.Stage == RallyStage.Feint) ? 3 : -1;

    private void BindRally()
    {
        _rallyBound = true;
        _rallyPreviousGuard = Boss.RallyGuarded;
        _rallyPreviousBounds = Player.RallyMovementBounds;
        _rallyPreviousLockTarget = Player.RallyLockTarget;
        Boss.RallyGuarded = true;
        Boss.SetBodyContactEnabled(false);
        _rallyEntryTarget = new Vector2(Field.BossFrameCenterX, Mathf.Clamp(Boss.GlobalPosition.Y, 46, 170));
        _rallyLastArrivalY = Mathf.Clamp(Player.GlobalPosition.Y, 32, Field.Bottom - 32);
        _rallyFormationAt = Time + WarnFor(Math.Abs(Player.GlobalPosition.X - RallyReceiver.GetCenter().X) + 8, 2.5);
        Player.RallyMovementBounds = new Rect2(Field.Left + 4, Field.Top + 4, Field.Width - 8, Field.Height - 8);
        foreach (var panel in Boss.GetChildren().OfType<Panel>())
        {
            _rallyStoredPanels.Add((panel, panel.Invulnerable));
            panel.Invulnerable = true;
        }
        Ended += _ => CleanRally();
        TreeExiting += CleanRally;
        var kinds = Enumerable.Range(0, (Tier + 1) * 2).Select(index => index > Tier).ToArray();
        _rallyVisuals = new ReiRallyVisuals();
        AddChild(_rallyVisuals);
        _rallyVisuals.Initialize(kinds.Length);
        for (int index = kinds.Length - 1; index > 0; index--)
        {
            int other = GD.RandRange(0, index);
            (kinds[index], kinds[other]) = (kinds[other], kinds[index]);
        }
        foreach (bool danger in kinds)
        {
            var panel = new Panel { ZIndex = 4, ZAsRelative = false };
            panel.Setup(Boss, 0, 0, 0, 1);
            panel.BeginRally();
            Boss.AddChild(panel);
            foreach (var sprite in panel.GetChildren().OfType<Sprite2D>()) sprite.Hide();
            panel.SetRallyPosition(RallyOrigin);
            panel.Hide();
            RallyBalls.Add(new RallyBall { Node = panel, Index = RallyBalls.Count, IsDanger = danger,
                Position = RallyOrigin, From = RallyOrigin, Stage = RallyStage.Gather, StageAt = Time });
        }
    }

    private void CleanRally()
    {
        if (_rallyCleaned) return;
        _rallyCleaned = true;
        _rallyVisuals?.Stop();
        if (Boss is BossRei rei) rei.EndRallyReaction();
        if (IsInstanceValid(Player))
        {
            Player.RallyMovementBounds = _rallyPreviousBounds;
            Player.RallyLockTarget = _rallyPreviousLockTarget;
        }
        foreach (var saved in _rallyStoredPanels)
            if (IsInstanceValid(saved.Panel) && !saved.Panel.IsQueuedForDeletion())
                saved.Panel.Invulnerable = saved.Invulnerable;
        foreach (var ball in RallyBalls)
            if (IsInstanceValid(ball.Node) && !ball.Node.IsQueuedForDeletion()) ball.Node.QueueFree();
        if (IsInstanceValid(Boss))
        {
            Boss.RallyGuarded = _rallyPreviousGuard;
            Boss.SetAnimalWindup(false);
            if (!_rallyBodyContactRestored && !Boss.IsPurified && !Boss.Transforming) Boss.SetBodyContactEnabled(true);
        }
    }

    private void PrepareRallyBall(RallyBall ball)
    {
        float speedX = 84 + Tier * 6;
        double launchLead = Tier == 0 ? 2.8 : 1.15;
        float flight = Math.Max(.8f, (ball.Position.X - (Field.Left + 65)) / speedX);
        float targetY;
        if (!ball.IsDanger)
        {
            float preferredY = Mathf.Clamp(_rallyLastArrivalY + (float)GD.RandRange(-46, 46), 32, Field.Bottom - 32);
            float minimumY = Math.Max(32, _rallyLastArrivalY - 46);
            float maximumY = Math.Min(Field.Bottom - 32, _rallyLastArrivalY + 46);
            var candidates = new[] { preferredY }.Concat(Enumerable.Range(0, 17)
                .Select(index => minimumY + index * (maximumY - minimumY) / 16))
                .OrderBy(y => Math.Abs(y - preferredY)).ToArray();
            int delay = 0;
            while (true)
            {
                bool found = false;
                targetY = preferredY;
                foreach (float candidate in candidates)
                {
                    double moveTime = Math.Abs(candidate - _rallyLastArrivalY) / Player.SlowestMoveSpeed;
                    double arrival = Math.Max(Time + launchLead + flight, _rallyNextArrivalAt + moveTime + .85) + delay * .2;
                    if (!RallyReturnLaneClear(candidate, arrival)) continue;
                    targetY = candidate;
                    ball.ArrivalAt = arrival;
                    found = true;
                    break;
                }
                if (found) break;
                delay++;
            }
            _rallyNextArrivalAt = ball.ArrivalAt;
            _rallyLastArrivalY = targetY;
            _rallyArrivals.Add((ball.ArrivalAt, targetY));
        }
        else
        {
            ball.ArrivalAt = Time + launchLead + flight + GD.RandRange(0f, .55f);
            while (true)
            {
                var nearby = _rallyArrivals.Where(reserved => Math.Abs(reserved.At - ball.ArrivalAt) < 1.2).ToArray();
                IEnumerable<float> candidates = nearby.Length > 0
                    ? nearby.SelectMany(r => new[] { r.Y - 2 * RallyRadius - TargetOpening, r.Y + 2 * RallyRadius + TargetOpening })
                        .Where(y => y >= 28 && y <= Field.Bottom - 28)
                    : Enumerable.Range(0, 17).Select(index => 28f + index * (Field.Height - 56) / 16);
                var lanes = candidates
                    .Where(y => nearby.All(reserved => Math.Abs(y - reserved.Y) >= (2 * RallyRadius + TargetOpening - .01f))).ToArray();
                if (lanes.Length > 0)
                {
                    targetY = lanes[GD.RandRange(0, lanes.Length - 1)];
                    break;
                }
                ball.ArrivalAt = nearby.Max(reserved => reserved.At) + 1.25;
            }
        }
        ball.From = ball.Position;
        ball.FeintOrigin = ball.Position;
        ball.Velocity = new Vector2(-speedX, (targetY - ball.Position.Y) / flight);
        ball.LaunchAt = ball.ArrivalAt - flight;
        ball.StageAt = Time;
        ball.Stage = RallyStage.Feint;
        ball.Attempts++;
        ball.Revealed = false;
        ball.ReturnEligible = false;
        ball.MissedPass = false;
        ball.Contacting = false;
        ball.EligibleAfterVolley = Player.ChargeVolleyId;
        _rallyArrivals.RemoveAll(reserved => reserved.At < Time - 2);
    }

    private bool RallyReturnLaneClear(float y, double arrival)
    {
        Vector2 a = new(Field.Left + 4, y), b = new(RallyReturnWindowBounds.End.X, y);
        float Reflect(float value, float low, float high)
        {
            float height = high - low;
            float folded = Mathf.PosMod(value - low, height * 2);
            return low + (folded <= height ? folded : height * 2 - folded);
        }
        foreach (var danger in RallyBalls.Where(ball => ball.IsDanger
            && ball.Stage is RallyStage.Feint or RallyStage.Incoming))
            for (int sample = -5; sample <= 5; sample++)
            {
                double at = arrival + sample * .1;
                if (danger.Stage == RallyStage.Feint && at < danger.LaunchAt) continue;
                Vector2 position = danger.Stage == RallyStage.Feint
                    ? danger.From + danger.Velocity * (float)(at - danger.LaunchAt)
                    : danger.Position + danger.Velocity * (float)(at - Time);
                position = new Vector2(Reflect(position.X, Field.Left + RallyRadius, Field.Right - RallyRadius),
                    Reflect(position.Y, Field.Top + RallyRadius, Field.Bottom - RallyRadius));
                if (Geometry2D.GetClosestPointToSegment(position, a, b).DistanceTo(position) < 36) return false;
            }
        return true;
    }

    private void CloseRally(bool broken)
    {
        if (_rallyClosing) return;
        _rallyClosing = true;
        RallyWallBroken = broken;
        _rallyMissAt = !broken && Tier >= 2 ? Time : -1;
        _rallyFinishAt = Time + (_rallyMissAt >= 0 ? _rallyMissLead + .55 : 1.05);
        Player.RallyLockTarget = null;
        foreach (var ball in RallyBalls)
        {
            ball.From = ball.Position;
            ball.Stage = RallyStage.Closing;
            ball.StageAt = Time;
            ball.ReturnEligible = false;
            ball.Velocity = Vector2.Zero;
        }
    }

    private void MissRally(RallyBall ball)
    {
        if (_rallyClosing || ball.MissedPass) return;
        ball.MissedPass = true;
        RallyFailures++;
        RallyStreak = 0;
        _rallyWallHitAt = Time;
        _rallyVisuals?.ClearDamage();
        ReiRallySound.Miss();
        if (RallyFailures >= RallyFailureLimit)
        {
            if (Tier >= 2)
            {
                _rallyGapY = Mathf.Clamp(ball.Position.Y, RallyPlayerBounds.Position.Y + 20, RallyPlayerBounds.End.Y - 20);
                _rallyMissLead = WarnFor(Math.Abs(Player.GlobalPosition.Y - _rallyGapY), 2.2);
            }
            CloseRally(false);
        }
    }

    private void ScoreRally(RallyBall ball)
    {
        ball.Returns++;
        RallyStreak++;
        _rallyWallHitAt = Time;
        _rallyVisuals?.HitWall(RallyStreak, ball.Position, Time);
        if (RallyStreak == 5)
        {
            RallyDamageEvents++;
            ball.DamageEvents++;
            CloseRally(true);
            ReiRallySound.Break();
            if (Boss is BossRei rei) rei.ReactToRallyBreak();
            Boss.DealDirectDamage(Math.Max(1, Mathf.RoundToInt(Boss.BarHp * Boss.TotalBars * .04f)));
            GameCamera.Instance?.Shake(3, .2f);
            return;
        }
        ReiRallySound.Wall(RallyStreak);
        ball.Velocity = new Vector2(-Math.Abs(ball.Velocity.X), ball.Velocity.Y);
        PrepareRallyBall(ball);
    }

    private void AdvanceRallyBall(RallyBall ball, double delta)
    {
        Vector2 previous = ball.Position;
        Vector2 next = previous + ball.Velocity * (float)delta;
        if (ball.Stage == RallyStage.Returning && ball.Velocity.X > 0
            && previous.X <= RallyWallX && next.X >= RallyWallX)
        {
            ball.Position = previous.Lerp(next, (RallyWallX - previous.X) / (next.X - previous.X));
            ScoreRally(ball);
            return;
        }
        float left = Field.Left + RallyRadius, right = Field.Right - RallyRadius;
        float top = Field.Top + RallyRadius, bottom = Field.Bottom - RallyRadius;
        if (next.X < left || next.X > right || next.Y < top || next.Y > bottom)
            _rallyVisuals?.ImpactBall(ball.Index, Time);
        bool rightBounce = false;
        if (next.X < left)
        {
            next.X = left + (left - next.X);
            ball.Velocity.X = Math.Abs(ball.Velocity.X);
            ball.LeftBounces++;
            if (!ball.IsDanger && ball.Stage == RallyStage.Incoming) MissRally(ball);
        }
        else if (next.X > right)
        {
            next.X = right - (next.X - right);
            ball.Velocity.X = -Math.Abs(ball.Velocity.X);
            ball.RightBounces++;
            rightBounce = true;
        }
        if (next.Y < top)
        {
            next.Y = top + (top - next.Y);
            ball.Velocity.Y = Math.Abs(ball.Velocity.Y);
            ball.TopBounces++;
        }
        else if (next.Y > bottom)
        {
            next.Y = bottom - (next.Y - bottom);
            ball.Velocity.Y = -Math.Abs(ball.Velocity.Y);
            ball.BottomBounces++;
        }
        if (_rallyClosing) return;
        ball.Position = next;
        if (ball.Stage == RallyStage.Incoming)
        {
            bool contact = Geometry2D.GetClosestPointToSegment(Player.GlobalPosition, previous, ball.Position)
                .DistanceTo(Player.GlobalPosition) <= RallyRadius + Player.CurrentHitRadius;
            if (contact && !ball.Contacting) MissRally(ball);
            ball.Contacting = contact;
            if (rightBounce && !_rallyClosing)
            {
                ball.MissedPass = false;
                ball.Contacting = false;
                ball.EligibleAfterVolley = Player.ChargeVolleyId;
                ball.Attempts++;
                if (!ball.IsDanger)
                {
                    float flight = (ball.Position.X - (Field.Left + 65)) / -ball.Velocity.X;
                    float height = Field.Height - RallyRadius * 2;
                    float folded = Mathf.PosMod(ball.Position.Y - (Field.Top + RallyRadius)
                        + ball.Velocity.Y * flight, height * 2);
                    float arrivalY = Field.Top + RallyRadius + (folded <= height ? folded : height * 2 - folded);
                    ball.ArrivalAt = Time + flight;
                    _rallyArrivals.Add((ball.ArrivalAt, arrivalY));
                    if (ball.ArrivalAt > _rallyNextArrivalAt)
                    {
                        _rallyNextArrivalAt = ball.ArrivalAt;
                        _rallyLastArrivalY = arrivalY;
                    }
                    _rallyArrivals.RemoveAll(reserved => reserved.At < Time - 2);
                }
            }
        }
    }

    private void StepRally(double delta)
    {
        if (!_rallyBound) BindRally();
        if (Time >= _rallyFinishAt) { Finish(); return; }
        float rightBoundary = Math.Max(Field.Left + 48, Field.Right - 4 - (float)Time * 160);
        Player.RallyMovementBounds = new Rect2(Field.Left + 4, Field.Top + 4,
            rightBoundary - (Field.Left + 4), Field.Height - 8);
        if (!_rallyEntered)
        {
            Boss.GlobalPosition = Boss.GlobalPosition.MoveToward(_rallyEntryTarget, RallyBossSpeed * (float)delta);
            _rallyEntered = Boss.GlobalPosition.DistanceSquaredTo(_rallyEntryTarget) < .01f;
        }
        else if (!_rallyClosing)
            Boss.GlobalPosition = Boss.GlobalPosition.MoveToward(new Vector2(Field.BossFrameCenterX,
                108 + Mathf.Sin((float)Time * .6f) * 58), RallyBossSpeed * (float)delta);
        bool formed = _rallyEntered && Time >= _rallyFormationAt;
        if (formed && !_rallyBodyContactRestored)
        {
            _rallyBodyContactRestored = true;
            Boss.SetBodyContactEnabled(true);
        }
        var shots = GetTree().GetNodesInGroup("player_bullets").OfType<Bullet>()
            .Where(shot => shot.Active && !shot.IsEnemy).ToArray();
        var previousPositions = RallyBalls.ToDictionary(ball => ball, ball => ball.Position);
        foreach (var ball in RallyBalls.OrderBy(ball => ball.IsDanger))
        {
            Vector2 previous = ball.Position;
            switch (ball.Stage)
            {
                case RallyStage.Gather:
                    ball.Position = RallyOrigin;
                    if (formed)
                    {
                        Vector2 hand = ball.Position;
                        ball.Position = new Vector2(Field.Right - 40, Mathf.Clamp(Boss.GlobalPosition.Y
                            + (float)GD.RandRange(-60, 60), 25, Field.Bottom - 25));
                        PrepareRallyBall(ball);
                        ball.FeintOrigin = hand;
                        ball.Position = hand;
                    }
                    break;
                case RallyStage.Feint:
                    ball.Revealed = Time >= ball.LaunchAt - (Tier == 0 ? 2.2 : .55);
                    float feint = Mathf.Clamp((float)((Time - ball.StageAt) / .6), 0, 1);
                    float side = ball.Index % 2 == 0 ? 1 : -1;
                    ball.Position = ball.FeintOrigin.Lerp(ball.From, feint * feint * (3 - 2 * feint))
                        + new Vector2(Mathf.Sin(feint * Mathf.Tau) * 8,
                        Mathf.Sin(feint * Mathf.Pi) * side * 12);
                    if (Time >= ball.LaunchAt)
                    {
                        ball.Position = ball.From;
                        ball.Stage = RallyStage.Incoming;
                        ball.StageAt = Time;
                        ball.Revealed = true;
                        _rallyLastThrow = Time;
                        if (Evolved) Boss.ShowAnimalAttack();
                    }
                    break;
                case RallyStage.Incoming:
                    AdvanceRallyBall(ball, delta);
                    if (ball.Stage != RallyStage.Incoming) break;
                    ball.ReturnEligible = !ball.IsDanger && ball.Velocity.X < 0
                        && RallyReturnWindowBounds.HasPoint(ball.Position);
                    foreach (var shot in shots)
                    {
                        if (!shot.Active || !shot.Charged || shot.ChargeVolleyId <= ball.EligibleAfterVolley
                            || _rallyUsedVolleys.Contains(shot.ChargeVolleyId)) continue;
                        var old = _rallyShotPositions.GetValueOrDefault(shot.GetInstanceId());
                        Vector2 previousShot = old.Activation == shot.ActivationId ? old.Position
                            : shot.GlobalPosition - shot.Velocity * (float)delta;
                        if (Geometry2D.GetClosestPointToSegment(Vector2.Zero,
                            previousShot - previous, shot.GlobalPosition - ball.Position).Length() > RallyRadius + shot.Radius) continue;
                        if (!ball.IsDanger && !ball.ReturnEligible) continue;
                        _rallyUsedVolleys.Add(shot.ChargeVolleyId);
                        Vector2 direction = shot.ChargeOrigin.DirectionTo(ball.Position);
                        shot.ChargeImpact(ball.Position);
                        Pool.Despawn(shot);
                        if (ball.IsDanger || direction.X <= .05f)
                            MissRally(ball);
                        else
                        {
                            ball.Velocity = direction * RallyReturnSpeed;
                            ball.Stage = RallyStage.Returning;
                            ball.StageAt = Time;
                            ball.ReturnEligible = false;
                            _rallyVisuals?.ImpactBall(ball.Index, Time);
                            ReiRallySound.Return(RallyStreak);
                        }
                        break;
                    }
                    break;
                case RallyStage.Returning:
                    AdvanceRallyBall(ball, delta);
                    break;
                case RallyStage.Closing:
                    float close = Mathf.Clamp((float)((Time - ball.StageAt) / .9), 0, 1);
                    ball.Position = ball.From.Lerp(RallyOrigin, close);
                    ball.Node.Scale = Vector2.One * 1.4f * (1 - close * .85f);
                    break;
            }
            ball.Node.SetRallyPosition(ball.Position);
            ball.Node.Visible = ball.Stage is not RallyStage.Gather and not RallyStage.Done;
            Color tint = !ball.Revealed ? new Color("cab8da")
                : ball.IsDanger ? new Color("ff7189") : new Color("8effda");
            ball.Node.Modulate = new Color(tint, ball.Stage == RallyStage.Feint ? .78f : 1);
        }
        if (!_rallyClosing)
            foreach (var ball in RallyBalls.Where(ball => ball.Stage == RallyStage.Incoming))
                Line(previousPositions[ball], ball.Position, RallyRadius, true);
        var next = RallyBalls.Where(ball => !ball.IsDanger && ball.Revealed && ball.Stage == RallyStage.Incoming
            && ball.Velocity.X < 0).MinBy(ball => ball.Position.X);
        Player.RallyLockTarget = _rallyClosing ? null : next?.Node;
        if (Evolved) Boss.SetAnimalWindup(Time - _rallyLastThrow >= .55 && RallyBalls.Any(ball => ball.Stage == RallyStage.Feint));
        _rallyShotPositions.Clear();
        foreach (var shot in shots.Where(shot => shot.Active))
            _rallyShotPositions[shot.GetInstanceId()] = (shot.ActivationId, shot.GlobalPosition);
        if (_rallyMissAt >= 0 && Time < _rallyMissAt + _rallyMissLead + .35)
        {
            bool live = Time >= _rallyMissAt + _rallyMissLead;
            foreach (var band in RallyRetaliationBands())
                Box(band, live, Mathf.Clamp((float)((Time - _rallyMissAt) / .6), 0, 1));
            if (At(_rallyMissAt + _rallyMissLead)) RallyMissRetaliations++;
        }
        _rallyVisuals?.Sync(RallyBalls, RallyWallX, Time, delta, RallyWallBroken, _rallyWallHitAt);
    }

    private IEnumerable<Rect2> RallyRetaliationBands()
    {
        yield return new Rect2(Field.Left, Field.Top, Field.Width, _rallyGapY - TargetOpening / 2 - Field.Top);
        yield return new Rect2(Field.Left, _rallyGapY + TargetOpening / 2, Field.Width, Field.Bottom - _rallyGapY - TargetOpening / 2);
    }

    private void DrawRally()
    {
        if (_rallyMissAt >= 0)
        {
            double age = Time - _rallyMissAt;
            foreach (var area in RallyRetaliationBands())
            {
                if (age < _rallyMissLead)
                {
                    AttackTelegraph.Box(this, area, AttackTelegraph.Area);
                    AttackTelegraph.DrawSymbol(this, new Vector2(Field.CenterX - 58, area.GetCenter().Y), AttackTelegraph.Symbol.Impact, Original);
                    for (float y = area.Position.Y + 12; y < area.End.Y - 3; y += 24)
                        AttackTelegraph.Route(this, new Vector2(RallyWallX, y), new Vector2(Field.Left, y), 3, true, true, true, true);
                }
                else if (age < _rallyMissLead + .35)
                {
                    DrawRect(area, new Color("b56cb8", UsesDangerForecast ? .72f : .38f));
                    if (UsesDangerForecast) DrawRect(area, new Color("f3b9e0"), false, 1.4f);
                    else for (float y = area.Position.Y; y < area.End.Y; y += 18)
                        DrawLine(new Vector2(Field.Left, y), new Vector2(Field.Right, Math.Min(y + 16, area.End.Y)), new Color("f3b9e0", .8f), 2, true);
                }
            }
        }
        Rect2 band = RallyReceiver;
        foreach (var h in HazardShapes)
        {
            if (h.Shape != 2 || h.A.Y != Field.Top || h.B.Y != Field.Bottom) continue;
            var exposure = new Rect2(h.A, h.B - h.A);
            if (h.Live) DrawEditorialFilm(this, exposure);
            else if (GameManager.ShowAttackTelegraphs) AttackTelegraph.DashedBounds(this, exposure, Original);
        }
        DrawRect(band, new Color(Original, .045f));
        DrawLine(new Vector2(band.Position.X, Field.Top), new Vector2(band.Position.X, Field.Bottom), new Color(Original, .7f), 1.5f, true);
        DrawLine(new Vector2(band.End.X, Field.Top), new Vector2(band.End.X, Field.Bottom), new Color(Original, .7f), 1.5f, true);
        _rallyVisuals?.Draw(this);
        foreach (var ball in RallyBalls.Where(ball => ball.Node.Visible))
        {
            if (ball.Stage == RallyStage.Incoming)
                DrawArc(ball.Position, RallyRadius, 0, Mathf.Tau, 64, new Color(ball.IsDanger ? "ff97a5" : "b9f6df"), .65f, true);
            if (ball.ReturnEligible)
                DrawArc(ball.Position, RallyRadius + 3, 0, Mathf.Tau, 48, new Color("dcfff0"), 1.5f, true);
            if (Player.RallyLockTarget == ball.Node)
                foreach (float sign in new[] { -1f, 1f })
                    DrawLine(ball.Position + new Vector2(sign * 22, -5), ball.Position + new Vector2(sign * 22, 5), Original, 2, true);
        }
        AttackTelegraph.DrawSymbol(this, new Vector2(Field.Left + 24, Field.Top + 11), AttackTelegraph.Symbol.Target, Original);
        DrawRallyProgress();
    }
    private void StartColorCall()
    {
        Identify("せーの、スタンプ！", "rei-live-color-call", "流れてくるスタンプの隙間を抜ける。");
        float gapHeight = 32 - Tier * 3;
        bool upper = Player.GlobalPosition.Y >= Field.CenterY;
        double start = 0, previousEnd = 0;
        float fromTop = Player.GlobalPosition.Y, fromBottom = fromTop;
        for (int i = 0; i < 2; i++)
        {
            float inset = (float)GD.RandRange(18, 34);
            float y = upper ? Field.Top + inset : Field.Bottom - inset - gapHeight;
            var gap = new Rect2(Field.Left, y, Field.Width, gapHeight);
            float distance = Mathf.Max(gap.Position.Y - fromTop, fromBottom - gap.End.Y) + Player.HitRadius + 6;
            double fire = Math.Max(start + ColorCueGrow, previousEnd) + WarnFor(distance + StampPitch + 12, 1.05);
            double end = fire + (Field.Width + StampWidth + StampPitch * (StampCopies - 1)) / StampTravelSpeed;
            _colorWaves.Add(new(gap, start, fire, end));
            fromTop = gap.Position.Y;
            fromBottom = gap.End.Y;
            upper = !upper;
            previousEnd = end;
            start = end - .48;
        }
    }

    private static Rect2 StampBand(Rect2 gap, bool upper, float x, float width)
        => upper ? new Rect2(x, Field.Top, width, gap.Position.Y - Field.Top)
            : new Rect2(x, gap.End.Y, width, Field.Bottom - gap.End.Y);

    private Rect2 LiveStampBand(ColorWave wave, bool upper, int copy)
    {
        float age = (float)Math.Clamp(Time - wave.FiresAt, 0, wave.EndsAt - wave.FiresAt);
        float x = Field.Right + copy * StampPitch - age * StampTravelSpeed + StampPhase(upper);
        return StampBand(wave.Gap, upper, x, StampWidth).Intersection(Field.Rect);
    }

    private float StampLip => (TargetOpening - Math.Max(6.35f, TargetOpening * .68f)) / 2;

    private IEnumerable<Rect2> StampCorners(ColorWave wave, bool upper, int copy)
    {
        float age = (float)Math.Clamp(Time - wave.FiresAt, 0, wave.EndsAt - wave.FiresAt);
        float x = Field.Right + copy * StampPitch - age * StampTravelSpeed + StampPhase(upper);
        float lip = StampLip, y = upper ? wave.Gap.Position.Y - lip : wave.Gap.End.Y - lip;
        yield return new Rect2(x - lip, y, 2 * lip, 2 * lip).Intersection(Field.Rect);
        yield return new Rect2(x + StampWidth - lip, y, 2 * lip, 2 * lip).Intersection(Field.Rect);
    }

    private double StampRelease(int wave) => wave + 1 == _colorWaves.Count ? _colorWaves[wave].EndsAt : _colorWaves[wave + 1].FiresAt;

    internal float NearestStampGutter(float x, bool? upper = null)
    {
        bool row = upper ?? _colorWaves[1].Gap.Position.Y < _colorWaves[0].Gap.Position.Y;
        float first = Field.Left + StampWidth + TargetOpening / 2 + StampPhase(row);
        int last = Mathf.FloorToInt((Field.Right - Player.CurrentHitRadius - first) / StampPitch);
        return first + Math.Clamp(Mathf.RoundToInt((x - first) / StampPitch), 0, last) * StampPitch;
    }

    private void StepColorCall()
    {
        for (int i = 0; i < _colorWaves.Count; i++)
        {
            var wave = _colorWaves[i];
            if (At(wave.StartsAt) || Time == 0 && wave.StartsAt == 0) Audio.Instance?.PlayUiConfirm();
            if (At(wave.FiresAt)) Audio.Instance?.PlaySpell();
            if (Time < wave.FiresAt || Time >= StampRelease(i)) continue;
            for (int side = 0; side < 2; side++)
            for (int copy = 0; copy < StampCopies; copy++)
            {
                var band = LiveStampBand(wave, side == 0, copy);
                if (band.HasArea()) Box(band, true);
                foreach (var lip in StampCorners(wave, side == 0, copy))
                    if (lip.HasArea()) Box(lip, true);
            }
        }
        if (Time >= _colorWaves[^1].EndsAt) Finish();
    }

    private void DrawColorCall()
    {
        // Draw the next route underneath the departing stamps.
        for (int i = _colorWaves.Count - 1; i >= 0; i--)
        {
            var wave = _colorWaves[i];
            if (Time < wave.StartsAt || Time >= StampRelease(i)) continue;
            var ink = StampInks[i];
            for (int side = 0; side < 2; side++)
            {
                if (Time < wave.FiresAt)
                {
                    float alpha = Mathf.Clamp((float)((Time - wave.StartsAt) / ColorCueGrow), 0, 1);
                    var path = StampBand(wave.Gap, side == 0, wave.Gap.Position.X, wave.Gap.Size.X);
                    if (!UsesDangerForecast) DrawRect(path, new Color(ink, .12f * alpha));
                    float edgeY = side == 0 ? path.End.Y : path.Position.Y;
                    DrawDashedLine(new Vector2(path.Position.X, edgeY), new Vector2(path.End.X, edgeY),
                        new Color(ink, .9f * alpha), 1.35f, 4, true);
                    float y = path.GetCenter().Y;
                    for (float x = path.Position.X + 18; x < path.End.X - 16; x += 38)
                    {
                        var tip = new Vector2(x, y);
                        DrawLine(tip + new Vector2(3, -2.5f), tip, new Color(ink, .65f * alpha), 1, true);
                        DrawLine(tip, tip + new Vector2(3, 2.5f), new Color(ink, .65f * alpha), 1, true);
                    }
                    DrawStampMotif(new Vector2(path.End.X - StampWidth / 2, y), i, new Color(ink, alpha));
                    continue;
                }
                for (int copy = 0; copy < StampCopies; copy++)
                {
                    foreach (var lip in StampCorners(wave, side == 0, copy))
                        if (lip.HasArea()) DrawRect(lip, ink);
                    var band = LiveStampBand(wave, side == 0, copy);
                    if (!band.HasArea()) continue;
                    DrawRect(band, WarningInk);
                    DrawRect(band, ink, false, 1.2f);
                    if (band.Size.X < 9) continue;
                    int marks = Math.Max(1, Mathf.FloorToInt(band.Size.Y / 13));
                    for (int mark = 0; mark < marks; mark++)
                        DrawStampMotif(new Vector2(band.GetCenter().X, band.Position.Y + band.Size.Y * (mark + .5f) / marks), i, ink);
                }
            }
        }
    }

    private void DrawStampMotif(Vector2 center, int wave, Color ink)
    {
        if (wave == 0) DrawArc(center, 3.5f, 0, Mathf.Tau, 20, ink, 1.75f, true);
        else DrawPolyline(new[] { center + new Vector2(0, -4), center + new Vector2(4, 3),
            center + new Vector2(-4, 3), center + new Vector2(0, -4) }, ink, 1.5f, true);
    }

    protected override void DrawTechnique()
    {
        if (RallyTechnique) { DrawRally(); return; }
        if (SourceCutTechnique) { DrawSourceCut(); return; }
        if (ColorCall) { DrawColorCall(); return; }
        if (Kind == BossTechniqueKind.Cameo) { DrawCameoQuotes(); return; }
        if (EasyEditorial) { DrawEasyEditorial(); return; }
        if (!GameManager.ShowAttackTelegraphs) return;
        DrawEditorialCues();
        if (_gateStriking)
        {
            Vector2 top = new(_previousGate, Field.Top);
            Vector2 bottom = new(_previousGate, Field.Bottom);
            DrawLine(top, bottom, AttackTelegraph.Outline, 2.4f, true);
            DrawLine(top, bottom, AttackLineInk, 1.2f, true);
        }
        if (Kind == BossTechniqueKind.Crossfire && Variant == 3)
            for (int sheet = 0; sheet < _quotationSheets.Length; sheet++)
                DrawRect(_quotationSheets[sheet], new Color(sheet == 0 ? Ink : Original, .7f), false, sheet == 0 ? .6f : 1.2f);
        foreach (var word in _words.Where(Exists))
        {
            if (word.Bullet.Velocity.LengthSquared() > .1f) continue;
            Vector2 p = word.Bullet.GlobalPosition;
            DrawArc(p, 9, -.7f, 3.8f, 18, Original, .8f, true);
            DrawLine(p + new Vector2(-4, 0), p + new Vector2(4, 0), Ink, .7f, true);
        }
        if (Time - _rejectedAt < .3)
            DrawArc(_rejectedPosition, 12, 0, Mathf.Tau, 20, new Color("ff7568"), 2, true);
    }

    private void DrawEasyEditorial()
    {
        if (Kind == BossTechniqueKind.Signature && Tier >= 2)
        {
            for (int edition = 0; edition < 2; edition++)
            {
                Vector2 at = new(Field.CenterX - 16 + edition * 32, 18);
                DrawFilm(at, new Vector2(12, 15), new Color(Original, _peeledEditions[edition] ? .3f : .9f));
                if (_peeledEditions[edition]) DrawPolyline(new[] { at + new Vector2(-4, 0), at + new Vector2(-1, 3), at + new Vector2(5, -4) }, Original, 1.2f, true);
            }
        }
        bool warnings = GameManager.ShowAttackTelegraphs;
        _dirtyWarningGroups.Clear();
        var groups = _easyPaths.Where(path => path.Draw && Time >= path.StartsAt && Time < path.WarningEndsAt)
            .Select(path => (Group: path.FiresAt, Fire: Math.Max(Time, path.FiresAt)))
            .Concat(_easyCuts.Where(cut => Time >= cut.StartsAt && Time < cut.FiresAt)
                .Select(cut => (Group: cut.FiresAt, Fire: cut.FiresAt))).ToList();
        if ((Kind is BossTechniqueKind.Area or BossTechniqueKind.Finale) && Time < EasyCueLead)
            groups.Add((EasyCueLead, EasyCueLead));
        double first = groups.Select(group => group.Fire).DefaultIfEmpty(double.PositiveInfinity).Min();
        _nextWarningGroups.Clear();
        foreach (var group in groups.Where(group => group.Fire <= first + .02)) _nextWarningGroups.Add(group.Group);
        foreach (var (start, canvas) in _warningCanvases)
            if (canvas.Next != _nextWarningGroups.Contains(start)) _dirtyWarningGroups.Add(start);
        foreach (var path in _easyPaths)
        {
            if (!path.Draw) continue;
            float progress = warnings && Time >= path.StartsAt && Time < path.WarningEndsAt ? EasyCueProgress(path.StartsAt) : -1;
            if (path.DrawnProgress != progress) _dirtyWarningGroups.Add(path.FiresAt);
            path.DrawnProgress = progress;
        }
        foreach (var cut in _easyCuts)
        {
            float progress = warnings && Time >= cut.StartsAt && Time < cut.FiresAt ? EasyCueProgress(cut.StartsAt) : -1;
            if (cut.DrawnProgress != progress || !cut.Box && progress >= 0) _dirtyWarningGroups.Add(cut.FiresAt);
            cut.DrawnProgress = progress;
        }
        float sweep = warnings && (Kind is BossTechniqueKind.Area or BossTechniqueKind.Finale) && Time < EasyCueLead ? EasyCueProgress(0) : -1;
        if (_sweepWarningProgress != sweep) _dirtyWarningGroups.Add(EasyCueLead);
        _sweepWarningProgress = sweep;
        foreach (double start in _dirtyWarningGroups)
        {
            if (!_warningCanvases.TryGetValue(start, out var canvas))
            {
                _warningCanvases[start] = canvas = new WarningCanvas { ZIndex = -2, ZAsRelative = false, UseParentMaterial = true };
                canvas.AttachMask(this, DangerForecastCanvas);
                AddChild(canvas);
            }
            canvas.Next = _nextWarningGroups.Contains(start);
            canvas.ZIndex = UsesDangerForecast ? -1 : canvas.Next ? -2 : -4;
            if (canvas.Group is { } group)
            {
                group.ZIndex = canvas.Next ? -3 : -5;
                ((ShaderMaterial)group.Material).SetShaderParameter("ink", AttackTelegraph.OrderedInk(AttackTelegraph.Projectile, canvas.Next));
            }
            foreach (var strokes in canvas.Builder.Values) strokes.Clear();
            canvas.Routes.Clear();
            canvas.Origins.Clear();
            canvas.Fills.Clear();
            canvas.Mask.Routes.Clear();
            canvas.Mask.Rects.Clear();
            canvas.Mask.Sweeps.Clear();
            foreach (var mask in canvas.ProjectileMasks.Values) mask.Routes.Clear();
            canvas.Bands.Clear();
        }
        var warning = new Color(AttackTelegraph.Projectile, .85f);
        if (warnings)
        {
            if (SelectionTechnique) DrawSelectionFrame();
            foreach (var path in _easyPaths)
            {
                if (!path.Draw || Time < path.StartsAt) continue;
                SelectWarningGroup(path.FiresAt);
                if (_collectWarnings && Time < path.WarningEndsAt)
                {
                    var points = path.Points.Length > 0 ? path.Points : new[] { path.From, path.To };
                    var visual = QuoteVisual(path);
                    Color color = AttackTelegraph.OrderedInk(visual.Accent, _nextWarningGroups.Contains(path.FiresAt));
                    float distance = 0;
                    for (int leg = 1; leg < points.Length; leg++)
                    {
                        DrawEasyRoute(points[leg - 1], points[leg], path.HalfWidth, EasyCueProgress(path.StartsAt), color,
                            leg == 1, caps: leg == points.Length - 1 || path.Turns.Contains(points[leg]), offset: distance, art: visual);
                        distance += points[leg - 1].DistanceTo(points[leg]);
                    }
                }
                if (path.Points.Length > 0 && Time < path.ArrivesAt && EasyCueProgress(path.StartsAt) >= 1)
                    foreach (Vector2 at in path.Turns)
                    {
                        DrawLine(at + new Vector2(-2, -2), at + new Vector2(2, 2), Original, .8f);
                        DrawLine(at + new Vector2(-2, 2), at + new Vector2(2, -2), Original, .8f);
                    }
                double sourceEnd = path.ArrivesAt;
                float release = 0;
                if (SelectionTechnique && path.Draw && path.Parent < 0)
                {
                    var children = _easyPaths.Where(child => child.Parent >= 0 && child.From == path.To);
                    sourceEnd = children.Max(child => child.FiresAt) + .25;
                    double last = children.Where(child => child.Fired).Select(child => child.FiresAt)
                        .DefaultIfEmpty(double.NegativeInfinity).Max();
                    release = 1 - Mathf.Clamp((float)((Time - last) / .25), 0, 1);
                }
                if ((!SelectionTechnique || path.Draw) && Time < sourceEnd && !double.IsNegativeInfinity(path.LastChildFire))
                {
                    DrawQuotation(path.To, 5 + release * 2, new Color(Original, .7f));
                    DrawArc(path.To, 4.5f, 0, Mathf.Tau, 12, new Color(warning, .6f), .65f, true);
                }
            }
            foreach (var cut in _easyCuts)
            {
                if (Time < cut.StartsAt || Time >= cut.FiresAt) continue;
                SelectWarningGroup(cut.FiresAt);
                float progress = EasyCueProgress(cut.StartsAt);
                if (cut.Box)
                    DrawEasyWarningBox(new Rect2(cut.A, cut.B - cut.A), progress,
                        AttackTelegraph.OrderedInk(AttackTelegraph.Area, _nextWarningGroups.Contains(cut.FiresAt)));
                else if (_collectWarnings)
                {
                    float grow = Mathf.Clamp((float)((Time - cut.StartsAt - .4) / .8), 0, 1);
                    float remaining = 1 - Mathf.Clamp((float)((Time - cut.StartsAt - EasyCueGrow)
                        / (cut.FiresAt - cut.StartsAt - EasyCueGrow)), 0, 1);
                    if (UsesDangerForecast && grow > 0) _warningCanvas.Mask.Routes.Add((cut.A, cut.A.Lerp(cut.B, grow), cut.HalfWidth));
                    _warningCanvas.Bands.Add((cut.A, cut.B, cut.HalfWidth, grow, remaining));
                }
            }
            SelectWarningGroup(EasyCueLead);
            Color sweepInk = AttackTelegraph.OrderedInk(AttackTelegraph.Sweep, _nextWarningGroups.Contains(EasyCueLead));
            if (Kind == BossTechniqueKind.Area && Time < EasyCueLead) DrawEasyReaderWarning(sweepInk);
            if (Kind == BossTechniqueKind.Finale && Time < EasyCueLead) DrawEasySentenceWarning(sweepInk);
            if (Kind == BossTechniqueKind.Signature && Time < 2.2)
            {
                for (int edit = 0; edit < _points.Length; edit++)
                {
                    DrawCropCorner(_points[edit] + new Vector2(-8, -8), 5, Vector2.One, new Color(Ink, .6f));
                    AttackTelegraph.Pips(this, _points[edit] + Vector2.Up * 13, (edit + 1), Original);
                }
                float t = Mathf.Clamp((float)(Time / 1.1), 0, 2);
                int leg = Mathf.Min(1, (int)t);
                Vector2 editor = _points[leg].Lerp(_points[leg + 1], t - leg);
                DrawFilm(editor, new Vector2(14, 22), new Color(Original, .8f));
            }
        }
        foreach (double start in _dirtyWarningGroups)
        {
            var canvas = _warningCanvases[start];
            canvas.Strokes = canvas.Builder.Where(pair => pair.Value.Count > 0).OrderBy(pair => pair.Key.Ink.R)
                .Select(pair => (pair.Key.Ink, pair.Key.Width, pair.Value)).ToArray();
            canvas.QueueRedraw();
            canvas.Mask.QueueRedraw();
            foreach (var mask in canvas.ProjectileMasks.Values) mask.QueueRedraw();
        }
        foreach (var path in _easyPaths)
        {
            if (!EasyQuoteExists(path)) continue;
            if (path.Waiting)
            {
                DrawFilm(path.To, new Vector2(14, 12), new Color(Ink, .8f));
                DrawLine(path.To + new Vector2(-7, 3), path.To + new Vector2(7, -3), new Color(Original, .8f), .8f, true);
            }
            if (path.Peel && path.Hits < 3)
            {
                Vector2 center = path.Shot!.GlobalPosition;
                for (int layer = path.Hits; layer < 3; layer++)
                {
                    float size = 8 + layer * 2;
                    DrawCropCorner(center + new Vector2(-size, -size), 4, Vector2.One, Original);
                    DrawCropCorner(center + new Vector2(size, size), 4, -Vector2.One, Original);
                }
            }
        }
        foreach (var h in HazardShapes.Where(h => h.Live))
        {
            if (h.Shape == 0 && h.A != h.B) continue;
            if (h.Shape == 2)
            {
                var rect = new Rect2(h.A, h.B - h.A).Intersection(Field.Rect);
                DrawRect(rect, new Color("251735"), false, 2.2f, true);
                DrawRect(rect, Original, false, .9f, true);
            }
            else
            {
                var fill = Kind == BossTechniqueKind.Area ? ReaderInk.Darkened(.25f) : new Color("7251aa");
                DrawCircle(h.A, h.Radius, fill);
                DrawArc(h.A, h.Radius, 0, Mathf.Tau, 24, Original, .7f, true);
            }
        }
    }

    private void DrawSelectionFrame()
    {
        int stage = SelectionStage;
        Vector2[] corners = { _selectionRect.Position, new(_selectionRect.End.X, _selectionRect.Position.Y),
            _selectionRect.End, new(_selectionRect.Position.X, _selectionRect.End.Y) };
        Vector2[] directions = { Vector2.One, new(-1, 1), -Vector2.One, new(1, -1) };
        Color frameInk = stage == 0 ? Original : AttackTelegraph.Light;
        float unfold = Mathf.Clamp((float)((Time - _easySelectionRecordAt) / .4), 0, 1);
        float lockInset = stage == 1 ? 5 * (1 - Mathf.Clamp((float)((Time - _selectionLockedAt) / .3), 0, 1)) : 0;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 at = Boss.TechniqueCenter.Lerp(corners[i], unfold) - directions[i] * lockInset;
            DrawCropCorner(at, stage == 0 ? 6 : 8, directions[i], WarningInk);
            DrawCropCorner(at, stage == 0 ? 5 : 7, directions[i], frameInk);
            if (stage > 0) DrawRect(new Rect2(corners[i] - Vector2.One * 1.4f, Vector2.One * 2.8f), frameInk);
        }
        if (unfold >= 1)
        {
            for (int side = 0; side < corners.Length; side++)
            {
                Vector2 from = corners[side], to = corners[(side + 1) % corners.Length];
                DrawLine(from, to, new Color(WarningInk, .6f), 1.5f, true);
                if (stage == 0) DrawDashedLine(from, to, new Color(frameInk, .8f), .65f, 10, true);
                else DrawLine(from, to, new Color(frameInk, .9f), .65f, true);
            }
            if (stage <= 2)
            {
                Vector2 from = new(_selectionRect.Position.X, _selectionEnd.Y);
                Vector2 to = new(_selectionRect.End.X, _selectionEnd.Y);
                DrawDashedLine(from, to, new Color(Original, stage == 0 ? .45f : .65f), .6f, 3, true);
                DrawLine(from, from + Vector2.Right * 6, WarningInk, 1.6f, true);
                DrawLine(to, to + Vector2.Left * 6, WarningInk, 1.6f, true);
                DrawLine(from, from + Vector2.Right * 6, Original, .75f, true);
                DrawLine(to, to + Vector2.Left * 6, Original, .75f, true);
            }
        }
        if (stage == 0)
        {
            DrawCropCorner(_selectionEnd + new Vector2(-8, -8), 4, Vector2.One, Original);
            DrawCropCorner(_selectionEnd + new Vector2(8, 8), 4, -Vector2.One, Original);
            DrawArc(_selectionEnd, 11, -Mathf.Pi / 2,
                -Mathf.Pi / 2 + Mathf.Tau * Mathf.Clamp((float)((Time - _easySelectionRecordAt) / 2), .001f, 1),
                32, Original, .8f, true);
        }
        else
        {
            var roots = _easyPaths.Where(path => path.Parent < 0);
            if (Time < roots.Max(path => path.FiresAt) + .25)
            {
                double last = roots.Where(path => path.Fired).Select(path => path.FiresAt)
                    .DefaultIfEmpty(double.NegativeInfinity).Max();
                float release = 1 - Mathf.Clamp((float)((Time - last) / .25), 0, 1);
                Vector2 center = _selectionRect.GetCenter();
                DrawFilm(center, new Vector2(12, 16) * (1 - release * .15f), new Color(Original, .85f));
                DrawQuotation(center, 7 + release * 2, Original);
            }
        }
        Vector2 caption = new(_selectionRect.GetCenter().X, _selectionRect.Position.Y - 4);
        AttackTelegraph.DrawSymbol(this, caption, stage == 0 ? AttackTelegraph.Symbol.Record : stage < 3 ? AttackTelegraph.Symbol.Locked : AttackTelegraph.Symbol.Stack, frameInk);
    }

    private void DrawEditorialFilm(Node2D canvas, Rect2 rect)
    {
        rect = rect.Intersection(Field.Rect);
        if (rect.Size.X <= 0 || rect.Size.Y <= 0) return;
        canvas.DrawRect(rect, new Color("372351", .62f));
        float rail = Math.Min(3, rect.Size.Y * .16f);
        canvas.DrawTextureRect(_editorialEdge!, new Rect2(rect.Position, new Vector2(rect.Size.X, rail)), false);
        canvas.DrawTextureRect(_editorialEdge!, new Rect2(rect.Position.X, rect.End.Y - rail, rect.Size.X, rail), false);
        if (Math.Min(rect.Size.X, rect.Size.Y) > 12)
        {
            var art = BulletArt.Get("rei_film")!;
            Vector2 size = art.GetSize();
            size *= Math.Min(18 / Math.Max(size.X, size.Y), Math.Min((rect.Size.X - 6) / size.X, (rect.Size.Y - 8) / size.Y));
            BossProjectileArt.Get("rei_film").Draw(canvas, new Rect2(rect.GetCenter() - size * .5f, size), Time, new Color(1, 1, 1, .65f));
        }
    }

    private sealed partial class WarningCanvas : Node2D
    {
        internal bool Next;
        internal CanvasGroup? Group;
        internal void AttachMask(Node2D owner, Node2D? shared)
        {
            if (shared != null) { shared.AddChild(Mask); return; }
            Group = new CanvasGroup { ZAsRelative = false, FitMargin = 3, ClearMargin = 4,
                Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/attack_forecast_union.gdshader") } };
            owner.AddChild(Group);
            Group.AddChild(Mask);
        }
        internal readonly WarningMask Mask = new();
        internal readonly Dictionary<Color, WarningMask> ProjectileMasks = new();
        internal WarningMask ProjectileMask(Color ink)
        {
            if (Group == null) return Mask;
            if (ProjectileMasks.TryGetValue(ink, out var mask)) return mask;
            var group = new CanvasGroup { FitMargin = 3, ClearMargin = 4, ShowBehindParent = true,
                Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/attack_forecast_union.gdshader") } };
            ((ShaderMaterial)group.Material).SetShaderParameter("ink", ink);
            AddChild(group);
            mask = new WarningMask();
            group.AddChild(mask);
            return ProjectileMasks[ink] = mask;
        }
        internal readonly List<(Vector2 From, Vector2 To, float Radius, float Grow, float Remaining)> Bands = new();
        internal readonly Dictionary<(Color Ink, float Width), List<Vector2>> Builder = new();
        internal readonly HashSet<(Vector2 From, Vector2 To, float Width, float Progress, Color Ink)> Routes = new();
        internal readonly HashSet<Vector2> Origins = new();
        internal readonly List<(Vector2[] Points, Color Ink)> Fills = new();
        internal (Color Ink, float Width, List<Vector2> Points)[] Strokes = Array.Empty<(Color, float, List<Vector2>)>();

        public override void _Draw()
        {
            foreach (var fill in Fills) DrawColoredPolygon(fill.Points, fill.Ink);
            // Match the crisp dashed forecasts; antialias fringes swamp closely spaced lanes.
            foreach (var stroke in Strokes) DrawMultiline(CollectionsMarshal.AsSpan(stroke.Points), stroke.Ink, stroke.Width);
            foreach (var band in Bands) AttackTelegraph.InstantBand(this, band.From, band.To, band.Radius, band.Grow, band.Remaining, Next);
        }
    }

    private sealed partial class WarningMask : Node2D
    {
        internal readonly List<(Vector2 From, Vector2 To, float Radius)> Routes = new();
        internal readonly List<Rect2> Rects = new();
        internal readonly List<Vector2[]> Sweeps = new();
        private readonly List<Vector2> _points = new();
        private readonly List<int> _indices = new();
        private static readonly Vector2[] Circle = Enumerable.Range(0, 64)
            .Select(i => Vector2.FromAngle(i * Mathf.Tau / 64)).ToArray();
        private static readonly int[] Quad = { 0, 1, 2, 0, 2, 3 };
        private static readonly Color[] White = { Colors.White };

        private void Cap(Vector2 center, float radius)
        {
            int start = _points.Count;
            _points.Add(center);
            foreach (Vector2 point in Circle) _points.Add(center + point * radius);
            for (int i = 0; i < Circle.Length; i++)
            {
                _indices.Add(start);
                _indices.Add(start + 1 + i);
                _indices.Add(start + 1 + (i + 1) % Circle.Length);
            }
        }

        public override void _Draw()
        {
            foreach (var rect in Rects) DrawRect(rect, Colors.White);
            foreach (var sweep in Sweeps) DrawColoredPolygon(sweep, Colors.White);
            _points.Clear();
            _indices.Clear();
            foreach (var route in Routes)
            {
                Vector2 side = route.From.DirectionTo(route.To).Orthogonal() * route.Radius;
                int start = _points.Count;
                _points.Add(route.From - side);
                _points.Add(route.From + side);
                _points.Add(route.To + side);
                _points.Add(route.To - side);
                foreach (int index in Quad) _indices.Add(start + index);
                Cap(route.From, route.Radius);
                Cap(route.To, route.Radius);
            }
            if (_indices.Count > 0)
                RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), CollectionsMarshal.AsSpan(_indices),
                    CollectionsMarshal.AsSpan(_points), White, ReadOnlySpan<Vector2>.Empty,
                    ReadOnlySpan<int>.Empty, ReadOnlySpan<float>.Empty, default, -1);
        }
    }

    private void SelectWarningGroup(double start)
    {
        _collectWarnings = _dirtyWarningGroups.Contains(start);
        if (_collectWarnings) _warningCanvas = _warningCanvases[start];
    }

    private void DrawEasyOrigin(Vector2 at, Color ink, BulletArt.PlayerVisual? art = null)
    {
        if (!_collectWarnings || !_warningCanvas.Origins.Add(at)) return;
        if (art != null)
        {
            foreach (var contour in BulletFootprint.Texture(art.Cropped, art.Pivot - art.Region.Position, 11).Contours)
                for (int index = 0; index < contour.Length; index++)
                {
                    Vector2 from = at + contour[index], to = at + contour[(index + 1) % contour.Length];
                    WarningSegments(from, to, new Color(WarningInk, ink.A * .8f), 1.6f, 1.8f);
                    WarningSegments(from, to, ink, .65f, 1.8f);
                }
            return;
        }
        WarningArc(at, 1, 0, Mathf.Tau, 12, AttackTelegraph.Light, 1);
        WarningArc(at, 2.8f, 0, Mathf.Tau, 24, new Color(WarningInk, ink.A * .7f), 1.6f);
        WarningArc(at, 2.8f, 0, Mathf.Tau, 24, ink, .7f);
    }

    private void WarningSegments(Vector2 from, Vector2 to, Color ink, float width, float dash = 0)
    {
        if (!_collectWarnings) return;
        var key = (ink, width);
        if (!_warningCanvas.Builder.TryGetValue(key, out var points)) _warningCanvas.Builder[key] = points = new();
        if (dash <= 0)
        {
            points.Add(from);
            points.Add(to);
            return;
        }
        float length = from.DistanceTo(to);
        Vector2 direction = from.DirectionTo(to);
        for (float at = 0; at < length; at += dash * 2)
        {
            points.Add(from + direction * at);
            points.Add(from + direction * Math.Min(length, at + dash));
        }
    }

    private void WarningArc(Vector2 at, float radius, float start, float sweep, int points, Color ink, float width)
    {
        if (!_collectWarnings) return;
        Vector2 previous = at + Vector2.FromAngle(start) * radius;
        for (int step = 1; step < points; step++)
        {
            Vector2 next = at + Vector2.FromAngle(start + sweep * step / (points - 1)) * radius;
            WarningSegments(previous, next, ink, width);
            previous = next;
        }
    }

    private void DrawWarningStroke(Vector2 from, Vector2 to, Color ink, float dash = 0)
    {
        if (dash > 0) dash = AttackTelegraph.IsLater(ink) ? 14 : 10;
        WarningSegments(from, to, new Color(WarningInk, ink.A * .6f), 1.5f, dash);
        WarningSegments(from, to, ink, .65f, dash);
    }

    private void DrawEasyRoute(Vector2 from, Vector2 to, float halfWidth, float progress, Color ink, bool origin = true, bool arrows = true, bool caps = true, float offset = 0, BulletArt.PlayerVisual? art = null)
    {
        if (!_collectWarnings) return;
        if (!_warningCanvas.Routes.Add((from, to, halfWidth, progress, ink))) return;
        if (origin) DrawEasyOrigin(from, ink, art);
        float grow = Mathf.Clamp((progress * (float)EasyCueGrow - .4f) / .8f, 0, 1);
        if (grow <= 0) return;
        Vector2 end = from.Lerp(to, grow);
        Vector2 direction = from.DirectionTo(to), side = direction.Orthogonal() * halfWidth;
        if (from.DistanceSquaredTo(end) < .01f) return;
        if (arrows)
        {
            _warningCanvas.ProjectileMask(ink).Routes.Add((from, end, halfWidth));
            float spacing = _warningCanvas.Next ? 24 : 36;
            for (float at = -(offset % spacing); at < from.DistanceTo(end); at += spacing)
            {
                if (at + 4 <= 0) continue;
                Vector2 a = from + direction * Math.Max(0, at), b = from + direction * Math.Min(at + 4, from.DistanceTo(end));
                WarningSegments(a, b, new Color(WarningInk, ink.A * .5f), 1.2f);
                WarningSegments(a, b, new Color(ink, ink.A * .7f), .5f);
            }
        }
        else
        {
            if (UsesDangerForecast) _warningCanvas.Mask.Routes.Add((from, end, halfWidth));
            DrawWarningStroke(from + side, end + side, ink);
            DrawWarningStroke(from - side, end - side, ink);
        }
        for (int edge = 0; edge < 4; edge++)
            if (WarningEdgeSpan(from, end, halfWidth, edge) is { } span && span.A.DistanceSquaredTo(span.B) > .01f)
            {
                WarningSegments(span.A, span.B, new Color(WarningInk, ink.A * .6f), 1.5f);
                WarningSegments(span.A, span.B, ink, .65f);
            }
        if (!arrows && origin && Field.Rect.HasPoint(from))
        {
            WarningArc(from, halfWidth, direction.Angle() + Mathf.Pi / 2, Mathf.Pi, 16, new Color(WarningInk, ink.A * .6f), 1.5f);
            WarningArc(from, halfWidth, direction.Angle() + Mathf.Pi / 2, Mathf.Pi, 16, ink, .65f);
        }
        if (!arrows && caps && Field.Rect.HasPoint(end))
        {
            WarningArc(end, halfWidth, direction.Angle() - Mathf.Pi / 2, Mathf.Pi, 16, new Color(WarningInk, ink.A * .6f), 1.5f);
            WarningArc(end, halfWidth, direction.Angle() - Mathf.Pi / 2, Mathf.Pi, 16, ink, .65f);
        }
        float firstArrow = origin && caps && from.DistanceTo(to) < 54 ? from.DistanceTo(to) * .6f : 54 - offset % 112;
        for (float distance = firstArrow; arrows && distance < from.DistanceTo(end) - 2; distance += 112)
        {
            if (distance < 2) continue;
            Vector2 tip = from + direction * distance;
            DrawWarningStroke(tip - direction * 3 + direction.Orthogonal() * 2, tip, ink);
            DrawWarningStroke(tip - direction * 3 - direction.Orthogonal() * 2, tip, ink);
        }
    }

    private void DrawEasyWarningBox(Rect2 rect, float progress, Color ink)
    {
        if (!_collectWarnings) return;
        DrawEasyOrigin(rect.Position, ink);
        float grow = Mathf.Clamp((progress * (float)EasyCueGrow - .4f) / .8f, 0, 1);
        if (grow <= 0) return;
        var visible = new Rect2(rect.Position, rect.Size * grow).Intersection(Field.Rect);
        if (!visible.HasArea()) return;
        var end = visible.End;
        Vector2[] corners = { visible.Position, new(end.X, visible.Position.Y), end, new(visible.Position.X, end.Y) };
        if (UsesDangerForecast) _warningCanvas.Mask.Rects.Add(visible);
        else _warningCanvas.Fills.Add((corners, new Color(ink, ink.A * .10f)));
        for (int edge = 0; edge < 4; edge++) DrawWarningStroke(corners[edge], corners[(edge + 1) % 4], ink);
    }

    private void DrawEasyReaderWarning(Color ink)
    {
        float progress = EasyCueProgress(0);
        float grow = Mathf.Clamp((progress * (float)EasyCueGrow - .4f) / .8f, 0, 1);
        var frames = EasyReaderFrames.ToArray();
        DrawEasyOrigin(new Vector2(Field.Left, 1), ink);
        if (grow <= 0) return;
        int last = Mathf.FloorToInt(grow * (frames.Length - 1));
        if (last > 0)
        {
            var left = new List<Vector2> { frames[0].A + new Vector2(-4, -4), frames[last].A + new Vector2(-4, 4) };
            var right = new List<Vector2> { frames[0].D + new Vector2(4, -4), frames[last].D + new Vector2(4, 4) };
            for (int frame = last; frame >= 0; frame--)
            {
                left.Add(frames[frame].B + Vector2.Right * 4);
                right.Add(frames[frame].C + Vector2.Left * 4);
            }
            if (_collectWarnings)
            {
                if (UsesDangerForecast)
                {
                    _warningCanvas.Mask.Sweeps.Add(left.ToArray());
                    _warningCanvas.Mask.Sweeps.Add(right.ToArray());
                }
                else
                {
                    _warningCanvas.Fills.Add((left.ToArray(), new Color(ink, .07f)));
                    _warningCanvas.Fills.Add((right.ToArray(), new Color(ink, .07f)));
                }
            }
        }
        for (int frame = 0; frame <= last; frame++)
        {
            var g = frames[frame];
            if (frame == 0 || frame == last)
            {
                DrawEasyRoute(g.A, g.B, g.HalfWidth, 1, new Color(ink, .65f), false, false);
                DrawEasyRoute(g.C, g.D, g.HalfWidth, 1, new Color(ink, .65f), false, false);
                AttackTelegraph.Arrow(this, (g.A + g.B) * .5f, Vector2.Down, ink);
                AttackTelegraph.Arrow(this, (g.C + g.D) * .5f, Vector2.Down, ink);
            }
            if (frame == 0) continue;
            var previous = frames[frame - 1];
            DrawWarningStroke(previous.B + Vector2.Right * 4, g.B + Vector2.Right * 4, ink);
            DrawWarningStroke(previous.C + Vector2.Left * 4, g.C + Vector2.Left * 4, ink);
        }
        if (progress >= 1)
        {
            var g = EasyReaderGeometry(EasyCueLead);
            DrawEasyRoute(g.A, g.B, g.HalfWidth, 1, ink, false, false);
            DrawEasyRoute(g.C, g.D, g.HalfWidth, 1, ink, false, false);
        }
    }

    private void DrawEasySentenceWarning(Color ink)
    {
        float progress = EasyCueProgress(0);
        float grow = Mathf.Clamp((progress * (float)EasyCueGrow - .4f) / .8f, 0, 1);
        var frames = EasySentenceFrames.ToArray();
        DrawEasyOrigin(new Vector2(frames[0].Head, frames[0].Upper.End.Y), ink);
        if (grow <= 0) return;
        int last = Mathf.FloorToInt(grow * (frames.Length - 1));
        for (int frame = 0; frame <= last; frame++)
        {
            var g = frames[frame];
            if (_collectWarnings)
            {
                _warningCanvas.Mask.Rects.Add(g.Upper);
                _warningCanvas.Mask.Rects.Add(g.Lower);
                if (g.Tail.HasArea()) _warningCanvas.Mask.Rects.Add(g.Tail);
            }
            if (frame > 0)
            {
                var previous = frames[frame - 1];
                if (_collectWarnings && Math.Abs(previous.Upper.End.Y - g.Upper.End.Y) > .001f)
                {
                    // Join successive footprints so a continuous sweep does not acquire stair-step edges.
                    _warningCanvas.Mask.Sweeps.Add(new[] { new Vector2(previous.Upper.Position.X, previous.Upper.End.Y),
                        previous.Upper.End, g.Upper.End, new Vector2(g.Upper.Position.X, g.Upper.End.Y) });
                    _warningCanvas.Mask.Sweeps.Add(new[] { previous.Lower.Position,
                        new Vector2(previous.Lower.End.X, previous.Lower.Position.Y),
                        new Vector2(g.Lower.End.X, g.Lower.Position.Y), g.Lower.Position });
                }
                DrawWarningStroke(new Vector2(previous.Head, previous.Upper.End.Y), new Vector2(g.Head, g.Upper.End.Y), ink);
                DrawWarningStroke(new Vector2(previous.Head, previous.Lower.Position.Y), new Vector2(g.Head, g.Lower.Position.Y), ink);
            }
            if (frame != 0 && frame != last) continue;
            DrawEasyWarningBox(g.Upper, 1, new Color(ink, .58f));
            DrawEasyWarningBox(g.Lower, 1, new Color(ink, .58f));
            if (g.Tail.HasArea()) DrawEasyWarningBox(g.Tail, 1, new Color(ink, .24f));
            Vector2 direction = _anchor.X < Field.CenterX ? Vector2.Right : Vector2.Left;
            AttackTelegraph.Arrow(this, new Vector2(g.Head, 12), direction, ink);
            AttackTelegraph.Arrow(this, new Vector2(g.Head, Field.Bottom - 12), direction, ink);
            float tail = _anchor.X < Field.CenterX ? g.Tail.End.X : g.Tail.Position.X;
            DrawWarningStroke(new Vector2(tail, 0), new Vector2(tail, Field.Bottom), new Color(ink, .58f), 6);
        }
    }

    private void DrawEditorialCues()
    {
        var paper = new Color("e6d5ed", .85f);
        var quiet = new Color(Ink, .48f);
        switch (Kind)
        {
            case BossTechniqueKind.Normal when Variant == 1:
                foreach (var word in _words.Where(Exists).Where(w => w.Stage == 0))
                {
                    Vector2 fork = word.Start + _forward * 42;
                    DrawQuotation(fork + Vector2.Up * 12, 7, paper);
                    AttackTelegraph.DrawSymbol(this, word.Bullet.GlobalPosition + Vector2.Up * 11, AttackTelegraph.Symbol.Stack, paper);
                }
                break;
            case BossTechniqueKind.Normal:
                for (int edge = 0; edge < 3; edge++)
                {
                    Vector2 at = _points[edge].Lerp(_points[(edge + 1) % 3], .5f);
                    DrawCropCorner(at, 7, (at - new Vector2(Field.CenterX, 130)).Normalized(), paper);
                    AttackTelegraph.Pips(this, _points[edge] + Vector2.Down * 13, (edge + 1), paper);
                }
                break;
            case BossTechniqueKind.Edge:
                if (_gateLeg >= 0)
                {
                    float x = Mathf.Clamp(_previousGate, Field.Left + 6, Field.Right - 6);
                    for (int row = 0; row < 6; row++)
                    {
                        float y = 24 + row * 32;
                        float direction = GateDirection(_gateLeg);
                        DrawLine(new Vector2(x - direction * 9, y - 3), new Vector2(x - direction * 5, y), paper, .7f, true);
                        DrawLine(new Vector2(x - direction * 9, y + 3), new Vector2(x - direction * 5, y), paper, .7f, true);
                    }
                    DrawQuotation(new Vector2(x, 192), 6, quiet);
                }
                break;
            case BossTechniqueKind.Area when Variant == 0:
            {
                float cursor = 36 + (float)Math.Max(0, Time - _warning) * (Tier >= 2 ? 32 : 25);
                for (int row = 0; row < 7; row++)
                {
                    float y = 36 + row * 24;
                    if (y > Field.Bottom - 16) break;
                    DrawLine(new Vector2(Field.Left + 43, y), new Vector2(Field.Left + 65 + row % 3 * 20, y), quiet, .5f, true);
                    if (cursor > y + 10) AttackTelegraph.DrawSymbol(this, new Vector2(Field.Left + 20, y), AttackTelegraph.Symbol.Check, paper);
                }
                break;
            }
            case BossTechniqueKind.Area when Variant == 1:
            {
                Vector2 center = _cropFrame.GetCenter();
                foreach (var offset in new[] { new Vector2(-71, -55), new Vector2(71, -55), new Vector2(71, 55), new Vector2(-71, 55) })
                    DrawCropCorner(center + offset, 9, -offset.Normalized(), paper);
                Vector2 record = center + new Vector2(_cropGate.X <= center.X ? 48 : -48, _cropGate.Y <= center.Y ? 32 : -32);
                AttackTelegraph.DrawSymbol(this, record, AttackTelegraph.Symbol.Record, paper);
                DrawCircle(record + new Vector2(-17, -3), 1.6f, new Color(Ink, .3f + .2f * Mathf.Sin((float)Time * 4)));
                break;
            }
            case BossTechniqueKind.Area:
            {
                foreach (var cut in _sweepingCuts.Where(cut => Time < cut.EndsAt))
                {
                    float t = Mathf.Clamp((float)((Time - cut.FiresAt) / (cut.EndsAt - cut.FiresAt)), 0, 1);
                    Vector2 at = cut.From.Lerp(cut.To, t);
                    DrawCropCorner(at - cut.Axis * (cut.HalfLength + 9), 7, cut.Axis, paper);
                    DrawCropCorner(at + cut.Axis * (cut.HalfLength + 9), 7, -cut.Axis, paper);
                    if (Time >= cut.FiresAt || !GameManager.ShowAttackTelegraphs) continue;
                    Vector2 direction = cut.From.DirectionTo(cut.To);
                    foreach (int sign in new[] { -1, 1 })
                    {
                        Vector2 tip = cut.From + cut.Axis * (sign * cut.HalfLength);
                        Vector2 end = cut.To + cut.Axis * (sign * cut.HalfLength);
                        DrawDashedLine(tip, end, paper, .8f, 4, true);
                        DrawCropCorner(tip.Lerp(end, .5f), 4, cut.Axis * -sign + direction, paper);
                    }
                }
                break;
            }
            case BossTechniqueKind.Signature:
                if (Time < _warning + 2.1)
                    for (int i = 0; i < _points.Length; i++)
                    {
                        DrawFilm(_points[i] + Vector2.Right * 19, new Vector2(9, 20), quiet);
                        if (i < 2)
                        {
                            float p = Mathf.PosMod((float)Time * .45f - i * .25f, 1);
                            DrawCircle(_points[i].Lerp(_points[i + 1], p), 1.3f, paper);
                        }
                    }
                break;
            case BossTechniqueKind.Finale:
            {
                float progress = Mathf.Clamp((float)((Time - _warning) / SentenceDuration), 0, 1);
                float head = Mathf.Lerp(_forward.X < 0 ? Field.Left + 30 : Field.Right - 30,
                    _forward.X < 0 ? Field.Right - 30 : Field.Left + 30, progress);
                float y = 108 + Mathf.Sin(progress * Mathf.Tau) * 24;
                DrawLine(new Vector2(head, y - 16), new Vector2(head, y + 16), paper, .65f, true);
                for (int glyph = 0; glyph < 5; glyph++)
                {
                    float x = head + _forward.X * (12 + glyph * 8);
                    if (x > Field.Left + 8 && x < Field.Right - 8)
                        DrawLine(new Vector2(x, y - 4), new Vector2(x + 4, y - 4), quiet, .6f, true);
                }
                DrawQuotation(new Vector2(head, y), 25, quiet);
                break;
            }
            case BossTechniqueKind.Crossfire when Variant == 0:
                DrawQuotation(_points[0] + Vector2.Up * 18, 10, paper);
                foreach (var word in _words.Where(Exists))
                    if (word.Bullet.Velocity.LengthSquared() < .1f)
                        AttackTelegraph.DrawSymbol(this, word.Bullet.GlobalPosition + Vector2.Down * 14, AttackTelegraph.Symbol.Stack, paper, count: 2);
                if (GameManager.ShowAttackTelegraphs && ShowingAmplificationSplit)
                {
                    Color ink = new("efa2db");
                    var art = BulletArt.EnemyVisual("rei_comment", "efa2db");
                    for (int i = 0; i < AmplificationRays; i++)
                    {
                        var (origin, end, direction, half) = AmplificationForecastRay(i);
                        Vector2 side = direction.Orthogonal() * AmplificationRadius, outer = side + direction.Orthogonal() * half;
                        foreach (float sign in new[] { -1f, 1f })
                        {
                            DrawDashedLine(_points[0] + side * sign, end + outer * sign, new Color(WarningInk, .7f), 1.6f, 5, true);
                            DrawDashedLine(_points[0] + side * sign, end + outer * sign, new Color(ink, .65f), .65f, 5, true);
                        }
                        AttackTelegraph.Arrow(this, _points[0].Lerp(end, .7f), direction, ink);
                    }
                    AttackTelegraph.ProjectileOrigin(this, _points[0], art, ink, 11);
                }
                break;
            case BossTechniqueKind.Crossfire when Variant == 1:
            {
                for (int line = 0; line < 6; line++)
                {
                    float markerY = 34 + line * 30;
                    var at = new Vector2(_insertionX, markerY);
                    AttackTelegraph.Arrow(this, at, Vector2.Right * _insertionDirection, quiet);
                    DrawQuotation(at + Vector2.Right * (_insertionDirection * 12), 5, paper);
                }
                break;
            }
            case BossTechniqueKind.Crossfire when Variant == 2:
            {
                float total = Field.Width + 24;
                float reach = Mathf.Clamp((float)(Time - _frontRoundStarted - _warning) * FrontSpeed, 0, total);
                Vector2 tip = _frontRoot + _forward * reach;
                AttackTelegraph.DrawSymbol(this, _frontRoot - _frontUp * 13, AttackTelegraph.Symbol.Stack, paper);
                DrawCropCorner(tip + _frontUp * 10, 6, _forward, paper);
                for (int i = 0; i < FrontBranches; i++)
                {
                    float distance = FrontBranchDistance(i, total);
                    Vector2 branch = _frontRoot + _forward * distance;
                    DrawQuotation(branch - _frontUp * 13, 5, paper);
                    AttackTelegraph.Pips(this, branch - _frontUp * 23, (i + 1), quiet);
                }
                break;
            }
            case BossTechniqueKind.Crossfire when Variant == 3:
                for (int sheet = 0; sheet < 2; sheet++)
                {
                    Rect2 rect = _quotationSheets[sheet];
                    DrawQuotation(rect.GetCenter(), rect.Size.X * .5f - 6, quiet);
                    DrawLine(rect.Position + new Vector2(5, 4), rect.Position + new Vector2(16, 4), paper, .6f, true);
                }
                break;
            case BossTechniqueKind.Crossfire:
                foreach (var quote in _quotations)
                    if (Time < quote.At + .8)
                    {
                        Vector2 mid = quote.A.Lerp(quote.B, .5f);
                        DrawFilm(mid + quote.A.DirectionTo(quote.B).Orthogonal() * 12, new Vector2(18, 8), quiet);
                        AttackTelegraph.DrawSymbol(this, mid + Vector2.Up * 18, AttackTelegraph.Symbol.Stack, paper, count: 2);
                    }
                break;
            case BossTechniqueKind.Extra when !Evolved && Variant == 0:
                if (Time > _warning + 2.7)
                {
                    DrawQuotation(new Vector2(Field.CenterX, _source.Y), 22, quiet);
                    DrawLine(new Vector2(Field.CenterX - 28, _source.Y + 15), new Vector2(Field.CenterX + 28, _source.Y + 15), quiet, .5f, true);
                }
                break;
            case BossTechniqueKind.Extra when !Evolved:
            {
                if (_spliceAcrossAt > 0)
                {
                    for (int side = -1; side <= 1; side += 2)
                        DrawFilm(_crossSplice + new Vector2(side * 32, 12), new Vector2(28, 9), quiet);
                    break;
                }
                float x = SpliceX;
                float seam = Mathf.Clamp(_anchor.Y, 72, 144);
                float gap = SpliceHalfGap;
                if (Time >= _warning + 3.4) break;
                for (int side = -1; side <= 1; side += 2)
                    DrawFilm(new Vector2(x + 13, seam + side * (gap + 18)), new Vector2(9, 28), quiet);
                break;
            }
            case BossTechniqueKind.Extra when Variant == 0:
                foreach (var page in _pages)
                    if (page.Hits < 3)
                    {
                        DrawFilm(page.Rect.GetCenter(), page.Rect.Size + new Vector2(7, 0), quiet);
                        Vector2 tip = page.Rect.GetCenter() + _forward * 17;
                        DrawLine(tip, tip - _forward.Rotated(.55f) * 5, paper, .8f, true);
                        DrawLine(tip, tip - _forward.Rotated(-.55f) * 5, paper, .8f, true);
                        for (int tear = 0; tear < page.Hits; tear++)
                        {
                            Vector2 p = page.Rect.Position + new Vector2(0, 10 + tear * 15);
                            DrawLine(p, p + new Vector2(10, 5), paper, .9f, true);
                        }
                    }
                break;
            case BossTechniqueKind.Extra:
                foreach (var word in _words.Where(Exists).Where(w => w.Stage == 1))
                    if (word.Bullet.Velocity.LengthSquared() < .1f)
                    {
                        Vector2 p = word.Bullet.GlobalPosition + new Vector2(0, 14);
                        DrawLine(p + Vector2.Left * 6, p + Vector2.Right * 6, quiet, .6f, true);
                        DrawLine(p + new Vector2(-5, -2), p + new Vector2(5, 2), paper, .8f, true);
                    }
                break;
        }
    }

    private void DrawQuotation(Vector2 at, float span, Color ink)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 corner = at + new Vector2(side * span, side * 4);
            AttackTelegraph.Stroke(this, new[] { corner + Vector2.Up * side * 7, corner, corner - Vector2.Right * side * 4 }, ink);
        }
    }

    private void DrawCropCorner(Vector2 at, float size, Vector2 inward, Color ink)
    {
        Vector2 horizontal = new(Math.Sign(inward.X) * size, 0);
        Vector2 vertical = new(0, Math.Sign(inward.Y) * size);
        if (horizontal == Vector2.Zero) horizontal = Vector2.Right * size;
        if (vertical == Vector2.Zero) vertical = Vector2.Down * size;
        AttackTelegraph.Stroke(this, new[] { at + horizontal, at, at + vertical }, ink);
    }

    private void DrawFilm(Vector2 center, Vector2 size, Color ink)
    {
        var rect = new Rect2(center - size * .5f, size);
        for (float y = rect.Position.Y + 3; y < rect.End.Y - 2; y += 7)
            for (int side = 0; side < 2; side++)
                DrawRect(new Rect2(side == 0 ? rect.Position.X : rect.End.X - 2, y, 2, 3), ink, false, .5f);
        DrawLine(new Vector2(rect.Position.X + 3, rect.Position.Y), new Vector2(rect.End.X - 3, rect.Position.Y), ink, .5f, true);
        DrawLine(new Vector2(rect.Position.X + 3, rect.End.Y), new Vector2(rect.End.X - 3, rect.End.Y), ink, .5f, true);
    }

    private void DrawRallyProgress()
    {
        Vector2 at = new(Field.CenterX, Field.Bottom - 10);
        for (int panel = 0; panel < 5; panel++)
        {
            var rect = new Rect2(at + new Vector2((panel - 2) * 9 - 3, -4), new Vector2(6, 8));
            DrawRect(rect, RallyWallBroken || panel < RallyStreak ? new Color(Original, .15f) : new Color(Ink, .7f));
            DrawRect(rect, Original, false, .7f);
            if (RallyWallBroken || panel < RallyStreak) DrawLine(rect.Position, rect.End, Original, 1, true);
        }
        for (int miss = 0; miss < 3; miss++)
        {
            Vector2 dot = at + new Vector2(38 + miss * 9, 0);
            if (miss < RallyFailures) AttackTelegraph.DrawSymbol(this, dot, AttackTelegraph.Symbol.Cross, Original);
            else DrawArc(dot, 2, 0, Mathf.Tau, 12, new Color(Original, .5f), .7f, true);
        }
    }

    private void DrawCitationSeal(Vector2 at, int family, bool withdrawn, Color ink)
    {
        Vector2[] seal = family switch
        {
            0 => new[] { at + Vector2.Up * 5, at + new Vector2(5, 4), at + new Vector2(-5, 4), at + Vector2.Up * 5 },
            1 => new[] { at + Vector2.Left * 5, at + Vector2.Up * 5, at + Vector2.Right * 5, at + Vector2.Down * 5, at + Vector2.Left * 5 },
            _ => new[] { at + new Vector2(-5, -4), at + new Vector2(5, -4), at + new Vector2(5, 4), at + new Vector2(-5, 4), at + new Vector2(-5, -4) },
        };
        AttackTelegraph.Stroke(this, seal, ink);
        if (withdrawn) AttackTelegraph.Stroke(this, new[] { at + new Vector2(-7, 7), at + new Vector2(7, -7) }, ink);
    }
}
