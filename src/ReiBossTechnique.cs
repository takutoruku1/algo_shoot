using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ReiBossTechnique : BossCharacterTechnique
{
    private Vector2 _anchor, _source, _forward;
    private Vector2[] _points = Array.Empty<Vector2>();
    private double _warning;
    private readonly List<EditedWord> _words = new();
    private readonly List<(Vector2 A, Vector2 B, double At)> _quotations = new();
    private readonly List<(Rect2 Rect, int Hits)> _pages = new();
    private RectangleShape2D? _pageShape;
    private CircleShape2D? _roundShot;
    private readonly List<InsertedQuote> _insertions = new();
    private readonly bool[] _deletedRoots = new bool[3];
    private readonly double[] _citationDeletes = Enumerable.Repeat(double.PositiveInfinity, 9).ToArray();
    private readonly List<(Vector2 A, Vector2 B)> _citationGuides = new();
    private Vector2 _previousReader;
    private float _previousGate;
    private int _gateLeg = -1;
    private double _rejectedAt = -10;
    private Vector2 _rejectedPosition;
    private readonly Rect2[] _quotationSheets = new Rect2[2];
    private Vector2 _selectionStart, _selectionEnd, _selectionLaunch;
    private Rect2 _selectionRect;
    private Vector2 _frontRoot, _frontUp;
    private float _pageStartX, _pageTop, _pageHeight;
    private int _selectionIndex;
    private bool _selectionLocked;
    private double _selectionBegan, _selectionLockedAt, _selectionRepostAt = -1;
    private static readonly Color Ink = new("bda2ef");
    private static readonly Color Original = new("ffe4a2");
    private const double EasyCueGrow = 1.2, EasyCueLead = 3.4;
    private int QuotePulses => 3 + Tier;
    private double QuoteSpacing => 1 - Tier * .07;
    private readonly List<EasyQuotePath> _easyPaths = new();
    private readonly List<EasyEditorialCut> _easyCuts = new();
    private double _easyEnd;
    private bool _easySelectionBuilt;
    private int _easySelectionRound, _easyParagraphsPrepared, _easyFanRowsPrepared, _easyTriangleFacesPrepared, _easyPagesPrepared;
    private int _easyTriangleFirstEdge;
    private readonly HashSet<int> _easyRevisionsPrepared = new();
    private double _easySelectionRecordAt;
    private bool EasyEditorial => (Kind == BossTechniqueKind.Normal
        || Kind == BossTechniqueKind.Area && Variant == 0
        || Kind == BossTechniqueKind.Extra && (!Evolved && Variant == 0 || Evolved && Variant == 1)
        || Kind is BossTechniqueKind.Signature or BossTechniqueKind.Finale);
    protected override bool UsesCustomHazardArt => EasyEditorial;

    private sealed class EasyQuotePath
    {
        internal string Key = "";
        internal double StartsAt, FiresAt, ArrivesAt, WarningEndsAt;
        internal Vector2 From, To, Scale = Vector2.One;
        internal float HalfWidth, Velocity;
        internal int Parent = -1, Generation, Hits, SelectionRound = -1, EditGroup = -1;
        internal bool Draw, Fired, Waiting, Peel;
        internal Bullet? Shot;
        internal ulong Activation;
    }

    private sealed class EasyEditorialCut
    {
        internal string Key = "";
        internal double StartsAt, FiresAt, EndsAt;
        internal Vector2 A, B;
        internal float HalfWidth;
        internal bool Box, Fired;
    }

    private IReadOnlyList<EasyQuotePath> EasyPaths => _easyPaths;
    private IReadOnlyList<EasyEditorialCut> EasyCuts => _easyCuts;
    private float EasyCueProgress(double start) => Mathf.Clamp((float)((Time - start) / EasyCueGrow), 0, 1);
    internal override IEnumerable<BossEasyCue> EasyCues
    {
        get
        {
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

    private sealed class EditedWord
    {
        internal Bullet Bullet = null!;
        internal ulong Id;
        internal Vector2 Start, Direction;
        internal double Ready;
        internal int Stage;
    }

    private sealed class InsertedQuote
    {
        internal float From, To;
        internal double At;
        internal bool New;
    }

    private float Speed => (Evolved ? 72 : 62) * GameManager.Instance!.BulletSpeedMul;
    private int Copies => Tier switch { 0 => 2, 1 => 3, 2 => 4, _ => 5 };
    private double SentenceDuration => Tier switch { 3 => 6.8, 2 => 7.5, _ => 8.8 };
    private float SelectionPadding => 14 + Tier * 4;
    private double SelectionSeconds => Tier switch { 0 => 1.8, 1 => 1.5, 2 => 1.2, _ => 1.0 };
    private double SelectionWarning => WarnFor(SelectionPadding + 7, Tier switch { 0 => 1.5, 1 => 1.35, 2 => 1.2, _ => 1.1 });
    private double SelectionLiveSeconds => 1.3 + Tier * .25;
    private int SelectionCount => Tier == 0 ? 1 : 2;
    private float FrontSpeed => Tier switch { 0 => 36, 1 => 42, 2 => 47, _ => 52 };
    private float FrontBranchSpeed => 32 + Tier * 6;
    private float FrontBranchLength => Mathf.Min(104 + Tier * 8,
        _frontUp.Y > 0 ? Field.Bottom - 8 - _frontRoot.Y : _frontRoot.Y - 8);
    private const float FrontReach = Field.Width - 20;
    private float PageSpeed => 26 + Tier * 6;
    private double PageSweepEnd => _warning + (Field.Width + 2) / PageSpeed;
    private float SideX => _forward.X < 0 ? Field.Right - 10 : Field.Left + 10;
    private Vector2 Side(float y) => new(SideX, y);
    private Vector2 Across(float distance, float y) => Side(y) + _forward * distance;
    private static Vector2 Inside(Vector2 p, float margin = 24) => p.Clamp(
        new Vector2(Field.Left + margin, margin), new Vector2(Field.Right - margin, Field.Bottom - margin));

    protected override void Start()
    {
        _anchor = Inside(Player.GlobalPosition, 36);
        _forward = _anchor.X < Field.CenterX ? Vector2.Left : Vector2.Right;
        _source = Side(Mathf.Clamp(_anchor.Y, 48, Field.Bottom - 48));
        _warning = WarnFor(70, Tier == 0 ? 1.7 : 1.35);
        if (EasyEditorial) { StartEasyEditorial(); return; }
        switch (Kind)
        {
            case BossTechniqueKind.Normal when Variant == 0:
                Identify("引用範囲の選択", "rei-reader-selects-quotation", "始点の近くで引用を小さく確定し、囲まれた範囲から離れる");
                _selectionStart = _selectionEnd = Player.GlobalPosition;
                _selectionRect = SelectedQuotation();
                _warning = SelectionSeconds + SelectionWarning;
                break;
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
                Identify("一方向の引用", "rei-directed-reading-gate", "読み順の矢印へ先回りする。逆向きに境界をまたぐと止められる");
                _warning = WarnFor(56, 1.4);
                _previousReader = Player.GlobalPosition;
                break;
            case BossTechniqueKind.Area when Variant == 0:
                Identify("コメント一斉読み", "rei-reading-cursor", "読み終えた行へ移り、下へ進む読み取り線を追い越さない");
                float toTop = Mathf.Max(0, Player.GlobalPosition.Y - 20);
                float toEnd = Mathf.Min(Player.GlobalPosition.X - Field.Left, Field.Right - Player.GlobalPosition.X);
                _warning = WarnFor(Mathf.Min(toTop, toEnd) + 24, 1.8);
                break;
            case BossTechniqueKind.Area when Variant == 1:
                Identify("配信枠", "rei-crop-three-edges", "番号順に閉じる枠から、最後の開いた辺を通って出る");
                _warning = WarnFor(94, 2.0);
                break;
            case BossTechniqueKind.Area:
                Identify("切り抜きの線", "rei-finite-cut-endpoint", "線を横切らず、切り抜き線の端を回り込む");
                _warning = WarnFor(88, 1.8);
                break;
            case BossTechniqueKind.Signature:
                Identify("星翼・追い越す流星", "rei-edit-three-destinations", "編集番号をたどり、最後の出口からの分裂を読む");
                _points = new[] { Across(28, 42), Across(88, 164), Across(12, 108) };
                _warning = WarnFor(64, 1.6);
                break;
            case BossTechniqueKind.Finale:
                Identify("最後まで見てて", "rei-continuous-sentence", "一文の開いていく余白をたどり、読み終わった場所に留まらない");
                _warning = WarnFor(Mathf.Abs(Player.GlobalPosition.Y - 108), 2.2);
                break;
            case BossTechniqueKind.Crossfire when Variant == 0:
                Identify("増幅する反響", "rei-merge-then-branch", "原稿が重なる点から離れ、合流後に広がる分裂を抜ける");
                _points = new[] { new Vector2(Field.CenterX, Mathf.Clamp(_anchor.Y, 62, 154)) };
                _warning = WarnFor(70, 1.5);
                break;
            case BossTechniqueKind.Crossfire when Variant == 1:
                Identify("割り込む引用", "rei-insert-displaces-suffix", "挿入記号より後ろの引用だけが押される。末尾の行き先を空ける");
                _warning = WarnFor(52, 1.6);
                for (int i = 0; i < 3; i++) _insertions.Add(new InsertedQuote { From = i, To = i, At = -1 });
                break;
            case BossTechniqueKind.Crossfire when Variant == 2:
                Identify("拡散する言葉", "rei-branch-front-tip", "枝分かれが届く前に、広がる前線の端を回る");
                _frontRoot = Side(Mathf.Clamp(Player.GlobalPosition.Y, 24, Field.Bottom - 24));
                _frontUp = _frontRoot.Y < Field.CenterY ? Vector2.Down : Vector2.Up;
                _warning = WarnFor(52, 1.6);
                break;
            case BossTechniqueKind.Crossfire when Variant == 3:
                Identify("編み込まれた噂", "rei-opaque-quotation-layers", "手前の原稿が奥の文字を隠す。見えている文字だけを避ける");
                _warning = WarnFor(80, 1.8);
                break;
            case BossTechniqueKind.Crossfire:
                Identify("万華のリポスト", "rei-trail-quotation", "原稿が通った線は遅れて再点灯する。すぐ後ろへ戻らない");
                _warning = WarnFor(54, 1.5);
                break;
            case BossTechniqueKind.Dodge:
                Identify("分かれる引用境界", "rei-forked-film-dodge", "予兆中に短い膜から離れ、分かれた中央を通る。回避でも越せる");
                _warning = WarnFor(52, 1.6);
                break;
            case BossTechniqueKind.Extra when !Evolved && Variant == 0:
                Identify("引用の段落", "rei-paragraph-enter-after", "段落の外で二度の展開を待ち、空いた中心へ入る");
                _warning = WarnFor(70, 1.8);
                _source.Y = Mathf.Clamp(_anchor.Y, 72, 144);
                break;
            case BossTechniqueKind.Extra when !Evolved:
                Identify("切り抜きの継ぎ目", "rei-seam-cross-before", "拡がる継ぎ目がつながる前に、反対側へ抜ける");
                _warning = WarnFor(78, 1.8);
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
                Identify("引用の連鎖", "rei-citation-dependency-erasure", "消える引用元から線をたどる。その原稿の子と孫だけがほどける");
                _warning = WarnFor(60, 1.65);
                break;
            case BossTechniqueKind.Cameo:
                Identify("引用の裏返し", "rei-negated-quotation", "括弧の内と外が交互に否定される。中立の間に境界をまたぐ");
                _warning = WarnFor(58, 1.7);
                break;
        }
    }

    protected override void Step(double delta)
    {
        if (EasyEditorial) { StepEasyEditorial(); return; }
        switch (Kind)
        {
            case BossTechniqueKind.Normal: if (Variant == 0) SelectQuotation(); else if (Variant == 1) QuotedFan(); else Kaleidoscope(); break;
            case BossTechniqueKind.Edge: ReadingGate(); break;
            case BossTechniqueKind.Area: if (Variant == 0) ReadComments(); else if (Variant == 1) StreamFrame(); else ClipLine(); break;
            case BossTechniqueKind.Signature: StarEditing(); break;
            case BossTechniqueKind.Finale: LastSentence(); break;
            case BossTechniqueKind.Crossfire:
                if (Variant == 0) Amplification(); else if (Variant == 1) InsertQuotation();
                else if (Variant == 2) SpreadingFront(); else if (Variant == 3) BraidedRumour(); else RepostTrail();
                break;
            case BossTechniqueKind.Dodge: FilmBoundary(); break;
            case BossTechniqueKind.Extra:
                if (!Evolved) { if (Variant == 0) Paragraph(); else Splice(); }
                else { if (Variant == 0) PeelPage(); else RewriteEnding(); }
                break;
            case BossTechniqueKind.Cameo: if (Variant == 0) QuoteChain(); else QuotationReversal(); break;
        }
    }

    private void StartEasyEditorial()
    {
        _warning = EasyCueLead;
        switch (Kind)
        {
            case BossTechniqueKind.Normal when Variant == 0:
                Identify("引用範囲の選択", "rei-selected-frame-cut-quotation", "切り出した左右の引用が反対側へ開く。二度の確定から離れる");
                BeginEasySelectionRecording(0);
                break;
            case BossTechniqueKind.Normal when Variant == 1:
                Identify("引用の扇", "rei-parent-child-angle", "原文の到着点を空け、三つに分かれる引用の間へ入る");
                PrepareEasyQuotationFan();
                break;
            case BossTechniqueKind.Normal:
                Identify("反響の万華鏡", "rei-triangle-subdivision", "四方の辺から中心へ届く原文を見て、分割された三角形の部屋を移る");
                PrepareEasyKaleidoscope();
                break;
            case BossTechniqueKind.Area:
                Identify("コメント一斉読み", "rei-reading-cursor-quotation", "読取り線の余白を通り、読み終えた行から分かれる引用を避ける");
                BuildEasyReader();
                break;
            case BossTechniqueKind.Extra when !Evolved:
                Identify("引用の段落", "rei-paragraph-opens-seam", "三つの段落へ分かれる行を追い、上下に開く継ぎ目の間を渡る");
                PrepareEasyParagraph();
                break;
            case BossTechniqueKind.Extra:
                Identify("星翼・書き直した結末", "rei-second-generation-edit", "分かれた文章が止まる編集点から離れ、訂正後の引用を読む");
                BuildEasyRewrite();
                break;
            case BossTechniqueKind.Signature:
                Identify("星翼・追い越す流星", "rei-edited-page-peeling", "三つの編集先をたどる頁を撃って剥がし、最後に分かれる引用へ備える");
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
        float speed = Speed * Mathf.Pow(.9f, generation);
        var path = new EasyQuotePath
        {
            Key = key, From = from, To = to, StartsAt = start, FiresAt = fire, WarningEndsAt = warningEnd,
            Velocity = speed, ArrivesAt = fire + from.DistanceTo(to) / speed, HalfWidth = halfWidth * Mathf.Max(scale.X, scale.Y),
            Parent = parent, Generation = generation, Draw = draw, Peel = peel, Scale = scale,
            SelectionRound = parent < 0 ? -1 : _easyPaths[parent].SelectionRound,
        };
        _easyPaths.Add(path);
        _easyEnd = Math.Max(_easyEnd, path.ArrivesAt + .1);
        return _easyPaths.Count - 1;
    }

    private void AddEasyFan(int parent, string key, Vector2[] destinations, double? fire = null, double? start = null)
    {
        var root = _easyPaths[parent];
        for (int child = 0; child < destinations.Length; child++)
        {
            Vector2 end = EasyExit(root.To, root.To.DirectionTo(destinations[child]));
            AddEasyQuote($"{key}:{child}", root.To, end, start ?? root.StartsAt, fire ?? root.ArrivesAt,
                start.HasValue ? fire!.Value : root.WarningEndsAt, root.Draw, parent);
        }
    }

    private void AddEasyCut(string key, Vector2 a, Vector2 b, float radius, double start, double fire, double end, bool box = false)
    {
        _easyCuts.Add(new EasyEditorialCut { Key = key, A = a, B = b, HalfWidth = radius,
            StartsAt = start, FiresAt = fire, EndsAt = end, Box = box });
        _easyEnd = Math.Max(_easyEnd, end);
    }

    private void BeginEasySelectionRecording(int round)
    {
        _easySelectionRound = round;
        _easySelectionRecordAt = Time;
        _easySelectionBuilt = _selectionLocked = false;
        _selectionStart = _selectionEnd = Player.GlobalPosition;
        _selectionRect = SelectedQuotation();
    }

    private void BuildEasySelectedFrame()
    {
        _easySelectionBuilt = true;
        _selectionLocked = true;
        _selectionLockedAt = Time;
        _selectionRect = SelectedQuotation();
        Vector2 center = _selectionRect.GetCenter();
        double start = Time, fire = start + EasyCueLead;
        string key = $"selection:{_easySelectionRound}";
        AddEasyCut($"{key}:range", _selectionRect.Position, _selectionRect.End, 0, start, fire, fire + 1.15, true);
        var corners = new[] { _selectionRect.Position, new Vector2(_selectionRect.End.X, _selectionRect.Position.Y),
            _selectionRect.End, new Vector2(_selectionRect.Position.X, _selectionRect.End.Y) };
        int opening = center.Y < Field.CenterY ? 2 : 0;
        for (int side = 0; side < 3; side++)
        {
            int edge = (opening + 1 + side) % 4;
            AddEasyCut($"{key}:frame:{side}", corners[edge], corners[(edge + 1) % 4], 3,
                start, fire + side * .24, fire + 1.4);
        }
        double cut = fire + 1.4;
        AddEasyCut($"{key}:cut", new Vector2(Field.Left, center.Y), new Vector2(Field.Right, center.Y),
            3, start, cut, cut + .55);
        for (int half = 0; half < 2; half++)
            for (int pulse = 0; pulse < QuotePulses; pulse++)
            {
                Vector2 direction = half == 0 ? Vector2.Left : Vector2.Right;
                Vector2 fork = Inside(new Vector2(Field.Left + Field.Width * (half == 0 ? .25f : .75f),
                    center.Y + (half == 0 ? -26 : 26)), 24);
                if (center.DistanceTo(fork) < 40)
                    fork.Y = Mathf.Clamp(center.Y + (center.Y < Field.CenterY ? 56 : -56), 24, Field.Bottom - 24);
                int root = AddEasyQuote($"{key}:{half}:{pulse}:parent", center, fork, start,
                    cut + pulse * .24 * QuoteSpacing, cut, pulse == 0);
                _easyPaths[root].SelectionRound = _easySelectionRound;
                float opposite = half == 0 ? Field.Right + 28 : Field.Left - 28;
                Vector2 reserved = _selectionEnd == fork ? fork + direction : _selectionEnd;
                AddEasyFan(root, $"{key}:{half}:{pulse}:quote", new[] {
                    new Vector2(opposite, -12), reserved, new Vector2(opposite, Field.Bottom + 12) });
            }
    }

    private void PrepareEasyQuotationFan()
    {
        int row = _easyFanRowsPrepared++;
        Vector2 target = Player.GlobalPosition;
        float y = new[] { 10f, 76f, 140f, 206f }[row];
        bool right = row % 2 == 0;
        Vector2 from = new(right ? Field.Left : Field.Right, y);
        Vector2 forward = right ? Vector2.Right : Vector2.Left;
        Vector2 fork = from + forward * 76;
        if (fork == target) target = fork + forward;
        double start = row * 1.35, first = start + EasyCueLead;
        for (int pulse = 0; pulse < QuotePulses + 1; pulse++)
        {
            int root = AddEasyQuote($"fan:{row}:{pulse}:parent", from, fork, start,
                first + pulse * .23 * QuoteSpacing, first, pulse == 0);
            float farX = right ? Field.Right + 28 : Field.Left - 28;
            AddEasyFan(root, $"fan:{row}:{pulse}:quote", new[] { new Vector2(farX, -8),
                target, new Vector2(farX, Field.Bottom + 8) });
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
        double start = face * 4.1, first = start + EasyCueLead;
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
                    start, first + 1.7 + edge * .32 + pulse * .22 * QuoteSpacing, first + 1.7, pulse == 0);
                AddEasyFan(root, $"triangle:{face}:{edge}:{pulse}:subdivision", vertices);
            }
        }
    }

    private void PrepareEasyParagraph()
    {
        int paragraph = _easyParagraphsPrepared++;
        double start = paragraph * 2.5, first = start + EasyCueLead;
        Vector2 target = Player.GlobalPosition;
        bool right = target.X >= Field.CenterX;
        Vector2 forward = right ? Vector2.Right : Vector2.Left;
        float y = target.Y;
        Vector2 from = new(right ? Field.Left : Field.Right, y), fork = new(target.X - forward.X * 64, y);
        float[] bands = { Field.Left + Field.Width / 6, Field.CenterX, Field.Right - Field.Width / 6 };
        int nearestBand = Enumerable.Range(0, bands.Length).MinBy(index => Math.Abs(bands[index] - target.X));
        float[] otherBands = bands.Where((_, index) => index != nearestBand).ToArray();
        for (int pulse = 0; pulse < QuotePulses; pulse++)
        {
            int root = AddEasyQuote($"paragraph:{paragraph}:{pulse}:original", from, fork, start,
                first + pulse * .25 * QuoteSpacing, first, pulse == 0);
            for (int line = 0; line < 3; line++)
            {
                float lineY = line == 1 ? y : Mathf.Clamp(y + (line - 1) * 26, 4, Field.Bottom - 4);
                Vector2 seam = new(line == 1 ? target.X : otherBands[line == 0 ? 0 : 1], lineY);
                if (fork.DistanceTo(seam) < 40)
                    seam.Y = Mathf.Clamp(fork.Y + (fork.Y < Field.CenterY ? 56 : -56), 8, Field.Bottom - 8);
                int child = AddEasyQuote($"paragraph:{paragraph}:{pulse}:line:{line}", fork, seam, start,
                    _easyPaths[root].ArrivesAt, first, pulse == 0, root);
                float spread = seam.X < Field.CenterX ? 30 : -30;
                AddEasyFan(child, $"paragraph:{paragraph}:{pulse}:seam:{line}", new[] {
                    seam + new Vector2(spread, -100), seam + new Vector2(spread, 100) });
                if (pulse != 0) continue;
                double join = _easyPaths[child].ArrivesAt;
                AddEasyCut($"paragraph:{paragraph}:upper:{line}", new Vector2(seam.X, -8),
                    new Vector2(seam.X, Mathf.Max(0, seam.Y - 24)), 3, start, join, join + 1.25);
                AddEasyCut($"paragraph:{paragraph}:lower:{line}", new Vector2(seam.X, Mathf.Min(Field.Bottom, seam.Y + 24)),
                    new Vector2(seam.X, Field.Bottom + 8), 3, start, join, join + 1.25);
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
                    start, first + pulse * .27 * QuoteSpacing, first, pulse == 0);
                for (int phrase = 0; phrase < 3; phrase++)
                {
                    Vector2 editing = new(Field.CenterX + (edition == 0 ? -14 : 14), 34 + phrase * 74);
                    int child = AddEasyQuote($"rewrite:{edition}:{pulse}:phrase:{phrase}", fork, editing,
                        start, _easyPaths[root].ArrivesAt, first, pulse == 0, root);
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
            AddEasyFan(phrase.index, phrase.path.Key.Replace(":phrase:", ":revision:"), new[] {
                new Vector2(opposite, -12), target, new Vector2(opposite, Field.Bottom + 12) },
                first + phrase.path.FiresAt - phrases[0].path.FiresAt, Time);
    }

    private void BuildEasyEditedPage()
    {
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
                first + pulse * .33 * QuoteSpacing, first, pulse == 0, peel: true);
            AddEasyFan(root, $"page:{page}:{pulse}:peel", new[] {
                new Vector2(opposite, -12), aim, new Vector2(opposite, Field.Bottom + 12) });
        }
    }

    private float EasyReaderSpeed => 24 + Tier * 4;
    private float EasyReaderHalfGap => 31 - Tier * 2;
    private double EasySentenceDuration => 9.8 - Tier * .8;
    private float EasySentenceHalfGap => 34 - Tier * 2;
    private double EasyReaderEnd => EasyCueLead + (Field.Height + 20) / EasyReaderSpeed;
    private float EasyReaderGap(float progress)
    {
        float side = _anchor.X < Field.CenterX ? 1 : -1;
        return Field.CenterX + side * (99 - Mathf.Sin(Mathf.Clamp(progress, 0, 1) * Mathf.Pi) * 40);
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
        {
            float y = 12 + row * 64;
            bool right = row % 2 == 0;
            double first = EasyCueLead + (y + 10) / EasyReaderSpeed;
            Vector2 fork = new(EasyReaderGap((y + 10) / (Field.Height + 20)), y);
            Vector2 from = new(right ? Field.Left : Field.Right, y);
            for (int pulse = 0; pulse < QuotePulses; pulse++)
            {
                int root = AddEasyQuote($"reader:{row}:{pulse}:line", from, fork, first - EasyCueLead,
                    first + pulse * .28 * QuoteSpacing, first, pulse == 0);
                AddEasyFan(root, $"reader:{row}:{pulse}:quotation", new[] {
                    new Vector2(Field.Left - 28, y - 94), new Vector2(Field.Right + 28, y - 94),
                    new Vector2(_anchor.X, y + 68) });
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
        float x0 = Mathf.Max(Field.Left, head - 43), x1 = Mathf.Min(Field.Right, head + 43);
        var upper = new Rect2(x0, 0, x1 - x0, center - EasySentenceHalfGap);
        var lower = new Rect2(x0, center + EasySentenceHalfGap, x1 - x0, Field.Bottom - center - EasySentenceHalfGap);
        float tail = head + (right ? -61 : 61);
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

    private void StepEasyEditorial()
    {
        if (Kind == BossTechniqueKind.Normal && Variant == 0 && !_easySelectionBuilt)
        {
            _selectionEnd = Player.GlobalPosition;
            _selectionRect = SelectedQuotation();
            if (Time >= _easySelectionRecordAt + 1.2) BuildEasySelectedFrame();
        }
        if (Kind == BossTechniqueKind.Extra && !Evolved && Variant == 0)
            while (_easyParagraphsPrepared < 3 && Time >= _easyParagraphsPrepared * 2.5) PrepareEasyParagraph();
        if (Kind == BossTechniqueKind.Normal && Variant == 1)
            while (_easyFanRowsPrepared < 4 && Time >= _easyFanRowsPrepared * 1.35) PrepareEasyQuotationFan();
        if (Kind == BossTechniqueKind.Normal && Variant == 2)
            while (_easyTriangleFacesPrepared < 4 && Time >= _easyTriangleFacesPrepared * 4.1) PrepareEasyKaleidoscope();
        if (Kind == BossTechniqueKind.Signature)
            while (_easyPagesPrepared < 2 && Time >= 2.2 + _easyPagesPrepared * 1.35) PrepareEasyEditedPage();
        var splitParents = new HashSet<int>();
        bool beginNextSelection = false;
        for (int index = 0; index < _easyPaths.Count; index++)
        {
            var path = _easyPaths[index];
            if (path.Draw && Time >= path.StartsAt && Time < path.WarningEndsAt)
                Line(path.From, path.To, path.HalfWidth, false, EasyCueProgress(path.StartsAt));
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
                splitParents.Add(path.Parent);
            }
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
            if (path.Generation == 1 && path.SelectionRound == 0 && _easySelectionRound == 0 && bullet.Active)
                beginNextSelection = true;
        }
        // A parent remains allocated until all siblings inherit its physical properties.
        foreach (int parent in splitParents) Pool.Despawn(_easyPaths[parent].Shot!);
        if (beginNextSelection) BeginEasySelectionRecording(1);
        for (int index = 0; index < _easyPaths.Count; index++)
        {
            var path = _easyPaths[index];
            if (!EasyQuoteExists(path)) continue;
            if (path.EditGroup >= 0 && Time >= path.ArrivesAt && !_easyRevisionsPrepared.Contains(path.EditGroup))
                PrepareEasyRevision(path.EditGroup);
            if (!path.Waiting && Time >= path.ArrivesAt && _easyPaths.Any(p => p.Parent == index && p.FiresAt > Time))
            {
                path.Waiting = true;
                path.Shot!.GlobalPosition = path.To;
                path.Shot.Velocity = Vector2.Zero;
                path.Shot.HoldBossShot();
            }
            if (path.Peel && path.Hits < 3) PeelEasyPage(path);
        }
        foreach (var cut in _easyCuts)
        {
            if (Time >= cut.FiresAt) cut.Fired = true;
            if (Time < cut.StartsAt || Time >= cut.EndsAt) continue;
            if (cut.Box) Box(new Rect2(cut.A, cut.B - cut.A), cut.Fired, EasyCueProgress(cut.StartsAt));
            else Line(cut.A, cut.B, cut.HalfWidth, cut.Fired, EasyCueProgress(cut.StartsAt));
        }
        if (Kind == BossTechniqueKind.Area && Time >= EasyCueLead && Time < EasyReaderEnd)
        {
            var g = EasyReaderGeometry(Time);
            Line(g.A, g.B, g.HalfWidth, true);
            Line(g.C, g.D, g.HalfWidth, true);
        }
        if (Kind == BossTechniqueKind.Finale && Time >= EasyCueLead && Time < _easyEnd)
        {
            var g = EasySentenceGeometry(Time);
            Box(g.Upper, true);
            Box(g.Lower, true);
            if (g.Tail.Size.X > 0) Box(g.Tail, true);
        }
        bool pending = Kind == BossTechniqueKind.Normal && Variant == 0 && !_easySelectionBuilt
            || Kind == BossTechniqueKind.Normal && Variant == 2 && _easyTriangleFacesPrepared < 4
            || Kind == BossTechniqueKind.Signature && _easyPagesPrepared < 2
            || Kind == BossTechniqueKind.Extra && Evolved && Variant == 1 && _easyRevisionsPrepared.Count < 6;
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

    private void Cue(Vector2 from, Vector2 direction, double start, double end, float reach = 70)
    {
        if (Time < start || Time >= end) return;
        float progress = (float)((Time - start) / (end - start));
        Line(from, from + direction.Normalized() * reach, 3, false, progress);
        Disc(from, 11, false, progress);
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

    private Rect2 SelectedQuotation()
    {
        Vector2 first = new(Math.Min(_selectionStart.X, _selectionEnd.X), Math.Min(_selectionStart.Y, _selectionEnd.Y));
        Vector2 last = new(Math.Max(_selectionStart.X, _selectionEnd.X), Math.Max(_selectionStart.Y, _selectionEnd.Y));
        return new Rect2(first, last - first).Grow(SelectionPadding).Intersection(Field.Rect.Grow(-10));
    }

    private void SelectQuotation()
    {
        if (!_selectionLocked)
        {
            _selectionEnd = Player.GlobalPosition;
            _selectionRect = SelectedQuotation();
            if (Time >= _selectionBegan + SelectionSeconds)
            {
                _selectionLocked = true;
                _selectionLockedAt = _selectionBegan + SelectionSeconds;
            }
        }
        double liveAt = _selectionLockedAt + SelectionWarning;
        double endAt = liveAt + SelectionLiveSeconds;
        if (!_selectionLocked || Time < endAt)
        {
            float progress = _selectionLocked ? (float)((Time - _selectionLockedAt) / SelectionWarning) : 0;
            Box(_selectionRect, _selectionLocked && Time >= liveAt, progress);
            Hint(new Vector2(Field.CenterX, 17), !_selectionLocked ? "始点の近くで、引用を小さく" : Time < liveAt ? "引用確定 → 囲みの外へ" : "引用中・外で待つ");
            return;
        }
        if (_selectionIndex + 1 < SelectionCount)
        {
            if (Time >= endAt + .6)
            {
                _selectionIndex++;
                _selectionBegan = Time;
                _selectionStart = _selectionEnd = Player.GlobalPosition;
                _selectionRect = SelectedQuotation();
                _selectionLocked = false;
            }
            return;
        }
        if (_selectionRepostAt < 0)
        {
            _selectionRepostAt = Time + 1.3;
            _forward = Player.GlobalPosition.X < Field.CenterX ? Vector2.Left : Vector2.Right;
            _selectionLaunch = new Vector2(_forward.X < 0 ? Field.Right - 3 : Field.Left + 3,
                Mathf.Clamp(_selectionRect.GetCenter().Y, 30, Field.Bottom - 30));
        }
        int originals = Math.Max(1, Tier);
        if (Time < _selectionRepostAt)
        {
            Line(_selectionLaunch, _selectionLaunch + _forward * (Field.Width - 6), 21 + (originals - 1) * 4,
                false, (float)(1 - (_selectionRepostAt - Time) / 1.3));
            Hint(new Vector2(Field.CenterX, 17), "選ばれた引用が、二段に広がる");
        }
        if (At(_selectionRepostAt))
            for (int i = 0; i < originals; i++)
                Seed(_selectionLaunch + Vector2.Down * ((i - (originals - 1) * .5f) * 8), _forward);
        DrainAfter(_selectionRepostAt + .5);
    }

    private void QuotedFan()
    {
        Cue(_source, _forward, 0, _warning, 100);
        if (Time < _warning + 3) Hint(new Vector2(Field.CenterX, 29), "親の正面 → 扇が開いた内側");
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
        Hint(new Vector2(Field.CenterX, 205), "次に分かれる辺を見て、小部屋へ");
        DrainAfter(_warning + 6.5);
    }

    private void ReadingGate()
    {
        int legs = Tier >= 2 ? 2 : 1;
        double crossing = (Field.Width + 36) / (38 + Tier * 3.0);
        double pause = WarnFor(42, 1.3);
        for (int leg = 0; leg < legs; leg++)
        {
            double at = _warning + leg * (crossing + pause);
            double warn = leg == 0 ? _warning : pause;
            if (Time < at - warn || Time >= at + crossing) continue;
            float direction = leg % 2 == 0 ? 1 : -1;
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
            Line(new Vector2(visual, 0), new Vector2(visual, Field.Bottom), 2, false, .8f);
            for (int row = 0; row < 4; row++)
                Hint(new Vector2(visual + direction * 12, 44 + row * 46), direction > 0 ? "→" : "←");
            Hint(new Vector2(Field.CenterX, 18), direction > 0 ? "読み順 → 境界より先へ" : "← 読み順　境界より先へ");
            Hint(new Vector2(Field.CenterX, 207), "順方向の横断は無害／逆向きにまたぐと危険");
            bool striking = Time >= at && (direction > 0
                ? gate >= Field.Left && gate < Field.Right - 18
                : gate <= Field.Right && gate > Field.Left + 18);
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
        if (Time >= _warning + legs * crossing + (legs - 1) * pause + .1) Finish();
    }

    private void ReadComments()
    {
        double begin = _warning;
        const float first = 36;
        float cursor = first + (float)Math.Max(0, Time - begin) * (Tier >= 2 ? 32 : 25);
        if (Time < begin)
        {
            Line(new Vector2(Field.Left + 32, first), new Vector2(Field.Right - 32, first), 7, false, (float)(Time / begin));
            Hint(new Vector2(Field.CenterX, 24), "読まれた行の後ろへ／端も回れる");
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

    private void StreamFrame()
    {
        Vector2 center = Inside(_anchor, 72);
        var corners = new[] { center + new Vector2(-66, -50), center + new Vector2(66, -50),
            center + new Vector2(66, 50), center + new Vector2(-66, 50) };
        int exit = center.Y < 108 ? 2 : 0;
        for (int i = 0; i < 4; i++)
        {
            int edge = (exit + 1 + i) % 4;
            double close = _warning + i * .75;
            if (Time < close + 2.4)
                Line(corners[edge], corners[(edge + 1) % 4], 4 + Tier * .7f, Time >= close, (float)(Time / close));
            if (Time < close)
                Hint(corners[edge].Lerp(corners[(edge + 1) % 4], .5f), (i + 1).ToString());
            if (At(close))
            {
                Vector2 outward = (corners[edge].Lerp(corners[(edge + 1) % 4], .5f) - center).Normalized();
                for (int c = 0; c < Copies; c++)
                {
                    float k = (c + 1f) / (Copies + 1);
                    Seed(corners[edge].Lerp(corners[(edge + 1) % 4], k), outward);
                }
            }
        }
        Hint(center + Vector2.Down * 8, "４番の辺へ");
        DrainAfter(_warning + 5);
    }

    private void ClipLine()
    {
        Vector2 center = Inside(_anchor, 74);
        for (int cut = 0; cut < (Tier >= 2 ? 3 : 2); cut++)
        {
            double begin = _warning + cut * 2.4;
            var direction = Vector2.FromAngle(cut * Mathf.Pi / 3);
            Vector2 a = center - direction * 64, b = center + direction * 64;
            if (Time >= begin - _warning && Time < begin + 1.65)
            {
                Line(a, b, 5 + Tier, Time >= begin, (float)((Time - begin + _warning) / _warning));
                Hint(b + direction * 12, "端");
            }
        }
        DrainAfter(_warning + (Tier >= 2 ? 6.5 : 4.2));
    }

    private void StarEditing()
    {
        for (int i = 0; i < _points.Length; i++)
        {
            if (Time < _warning + 2.1)
            {
                Disc(_points[i], 13, false);
                Hint(_points[i] + Vector2.Up * 16, (i + 1).ToString());
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
        Hint(new Vector2(Field.CenterX, 204), "最後の編集点から分裂する");
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
            Hint(new Vector2(startX, 91), "文の先へ");
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
            float next = Mathf.Min(1, progress + .12f);
            var ahead = new Vector2(Mathf.Lerp(startX, endX, next), 108 + Mathf.Sin(next * Mathf.Tau) * 24);
            Hint(ahead, _forward.X < 0 ? "先へ →" : "← 先へ");
        }
        if (Time >= _warning + duration + .5) Finish();
    }

    private void Amplification()
    {
        Vector2 merge = _points[0];
        double arrive = _warning + 1.8;
        if (Time < arrive + 1.4)
        {
            Line(new Vector2(Field.Left + 18, merge.Y - 38), merge, 2, false);
            Line(new Vector2(Field.Right - 18, merge.Y + 38), merge, 2, false);
            Disc(merge, 26 + Tier * 2, Time >= arrive + 1.05, (float)((Time - _warning) / 2.85));
        }
        if (At(_warning))
        {
            HoldWord(new Vector2(Field.Left + 18, merge.Y - 38), Vector2.Right);
            HoldWord(new Vector2(Field.Right - 18, merge.Y + 38), Vector2.Left);
        }
        if (Time >= _warning && Time < arrive)
        {
            float p = (float)((Time - _warning) / 1.8);
            foreach (var word in _words.Where(Exists))
            {
                word.Bullet.HoldBossShot();
                if (!word.Bullet.LeadIn) word.Bullet.GlobalPosition = word.Start.Lerp(merge, p);
            }
        }
        if (Time >= arrive && Time < arrive + 1.05)
            foreach (var word in _words.Where(Exists)) word.Bullet.HoldBossShot();
        if (At(arrive + 1.05))
        {
            foreach (var word in _words.Where(Exists)) Pool.Despawn(word.Bullet);
            int count = 4 + Copies;
            for (int i = 0; i < count; i++) Seed(merge, Vector2.FromAngle(i * Mathf.Tau / count + .2f));
        }
        Hint(merge + Vector2.Up * 38, "合流してから広がる");
        DrainAfter(arrive + 1.5);
    }

    private void InsertQuotation()
    {
        int count = 2 + Tier;
        double interval = Tier >= 2 ? 2.05 : 2.4;
        float y = Mathf.Clamp(_anchor.Y, 46, 170);
        float left = Field.Left + 22;
        for (int turn = 0; turn < count; turn++)
        {
            double at = _warning + 1.3 + turn * interval;
            int index = 1 + turn % 2;
            if (Time >= at - 1.3 && Time < at)
            {
                float progress = (float)((Time - at + 1.3) / 1.3);
                for (int i = index; i < _insertions.Count; i++)
                {
                    Vector2 from = new(left + _insertions[i].To * 34, y);
                    Line(from, from + Vector2.Right * 34, 9, false, progress);
                }
                Hint(new Vector2(left + index * 34, y - 22), "挿入 ↓");
                Hint(new Vector2(left + _insertions[^1].To * 34 + 34, y + 27), "末尾 →");
            }
            if (At(at))
            {
                for (int i = index; i < _insertions.Count; i++)
                {
                    var quote = _insertions[i];
                    quote.From = quote.To;
                    quote.To++;
                    quote.At = at;
                    quote.New = false;
                }
                _insertions.Insert(index, new InsertedQuote { From = index, To = index, At = at, New = true });
            }
        }
        foreach (var quote in _insertions)
        {
            float progress = Mathf.Clamp((float)((Time - quote.At) / .7), 0, 1);
            float offset = Mathf.Lerp(quote.From, quote.To, progress);
            float width = quote.New ? 24 * progress : 24;
            Box(new Rect2(left + offset * 34 - width * .5f, y - 8, width, 16), Time >= _warning,
                (float)(Time / _warning));
            Hint(new Vector2(left + offset * 34, y + 3), "引用");
        }
        if (Time >= _warning + count * interval + .8) Finish();
    }

    private void SpreadingFront()
    {
        float reach = Mathf.Clamp((float)(Time - _warning) * FrontSpeed, 0, FrontReach);
        float halfWidth = 5 + Tier;
        double end = _warning + FrontReach / FrontSpeed + FrontBranchLength / FrontBranchSpeed + .4;
        if (Time < _warning)
            Line(_frontRoot, _frontRoot + _forward * FrontReach, halfWidth, false, (float)(Time / _warning));
        else if (Time < end)
            Line(_frontRoot, _frontRoot + _forward * reach, halfWidth, true);
        for (int i = 0; i < Copies; i++)
        {
            float distance = Mathf.Lerp(12, FrontReach - 12, i / (float)(Copies - 1));
            double at = _warning + distance / FrontSpeed;
            Vector2 branch = _frontRoot + _forward * distance;
            float length = Mathf.Clamp((float)(Time - at) * FrontBranchSpeed, 0, FrontBranchLength);
            if (Time < at)
                Line(branch, branch + _frontUp * FrontBranchLength, 21, false, (float)(Time / at));
            else if (Time < end)
            {
                Line(branch, branch + _frontUp * length, halfWidth, true);
                if (length < FrontBranchLength)
                    Line(branch + _frontUp * length, branch + _frontUp * FrontBranchLength, 21, false, length / FrontBranchLength);
            }
            if (At(at)) Seed(branch, _frontUp, i);
        }
        Hint(new Vector2(Field.CenterX, 202), "幹が届くと枝が伸びる。枝の反対側へ");
        DrainAfter(end);
    }

    private void BraidedRumour()
    {
        double duration = 8.8 - Tier * .4;
        float progress = Mathf.Clamp((float)((Time - _warning) / duration), 0, 1);
        Vector2 size = new(112 - Tier * 4, 66 - Tier * 2);
        _quotationSheets[0] = new Rect2(new Vector2(130, 68).Lerp(new Vector2(320, 146), progress) - size * .5f, size);
        _quotationSheets[1] = new Rect2(new Vector2(320, 150).Lerp(new Vector2(130, 70), progress) - size * .5f, size);
        float halfWidth = 3.5f + Tier * .5f;
        bool live = Time >= _warning;
        float warning = (float)(Time / _warning);
        MaskedQuote(new Vector2(Field.Left + 12, 54), new Vector2(Field.Right - 12, 154), halfWidth, 0, live, warning);
        MaskedQuote(new Vector2(Field.Left + 12, 108), new Vector2(Field.Right - 12, 108), halfWidth, 0, live, warning);
        MaskedQuote(new Vector2(Field.Left + 12, 162), new Vector2(Field.Right - 12, 62), halfWidth, 0, live, warning);
        for (int sheet = 0; sheet < 2; sheet++)
        {
            var paper = _quotationSheets[sheet];
            for (int row = -1; row <= 1; row += 2)
                MaskedQuote(new Vector2(paper.Position.X + 10, paper.GetCenter().Y + row * 17),
                    new Vector2(paper.End.X - 10, paper.GetCenter().Y + row * 17), halfWidth, sheet + 1, live, warning);
            Hint(new Vector2(paper.GetCenter().X, paper.Position.Y + 9), sheet == 0 ? "奥の引用" : "手前の引用");
        }
        Hint(new Vector2(Field.CenterX, 205), "紙が覆った文字は消える／手前の文字は残る");
        if (Time >= _warning + duration) Finish();
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
        int lines = Copies;
        for (int i = 0; i < lines; i++)
        {
            double at = _warning + i * 1.3;
            var from = Side(42 + i * 132f / (lines - 1));
            var to = Across(210, 174 - i * 132f / (lines - 1));
            Cue(from, from.DirectionTo(to), at - 1.1, at, from.DistanceTo(to));
            if (At(at))
            {
                Seed(from, from.DirectionTo(to), i);
                _quotations.Add((from, to, at + 2.4));
            }
        }
        foreach (var quote in _quotations)
        {
            if (Time >= quote.At - 1.3 && Time < quote.At + .8)
                Line(quote.A, quote.B, 4 + Tier * .5f, Time >= quote.At, (float)((Time - quote.At + 1.3) / 1.3));
        }
        Hint(new Vector2(Field.CenterX, 204), "通った跡は、もう一度光る");
        DrainAfter(_warning + (lines - 1) * 1.3 + 3.3);
    }

    private void FilmBoundary()
    {
        var center = Inside(_anchor, 65);
        float x = center.X + (_forward.X < 0 ? -1 : 1) * 15;
        double split = _warning + .7;
        if (Time < split)
        {
            int layers = 2 + Tier;
            for (int layer = 0; layer < layers; layer++)
            {
                float offset = (layer - (layers - 1) * .5f) * 3.4f;
                Line(new Vector2(x + offset, center.Y - 56), new Vector2(x + offset, center.Y + 56),
                    2.5f, Time >= _warning, (float)(Time / _warning));
            }
            Hint(new Vector2(x, center.Y - 65), GameManager.Instance!.HasDodge
                ? "回避で重なる線の向こうへ" : "線から離れ、分かれた中央へ");
        }
        if (Time >= split && Time < split + 1.1)
        {
            float open = (float)(Time - split) * 34;
            for (int edge = -1; edge <= 1; edge += 2)
                Line(new Vector2(x, center.Y + edge * (20 + open)),
                    new Vector2(x - _forward.X * 54, center.Y + edge * (56 + open)), 4, true);
        }
        DrainAfter(split + 1.2);
    }

    private void Paragraph()
    {
        double enter = _warning + 2.7;
        if (Time < enter)
        {
            Line(Side(_source.Y), Across(Field.Width - 20, _source.Y), 24 + Copies * 2,
                Time >= _warning, (float)(Time / _warning));
            Hint(new Vector2(Field.CenterX, _source.Y - 34), "二度分かれるまで段落の外へ");
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
        if (Time >= enter) Hint(new Vector2(Field.CenterX, _source.Y), "分かれた段落の内側へ");
        DrainAfter(enter + 1);
    }

    private void Splice()
    {
        float x = Mathf.Clamp(_anchor.X + _forward.X * 54, Field.Left + 78, Field.Right - 78);
        float seam = Mathf.Clamp(_anchor.Y, 72, 144);
        double close = _warning + 1.8;
        float halfGap = Time < _warning ? 48 : Mathf.Lerp(48, 9, Mathf.Clamp((float)((Time - _warning) / 1.8), 0, 1));
        if (Time < close + 1.6)
        {
            Line(new Vector2(x, 12), new Vector2(x, seam - halfGap), 6 + Tier, Time >= _warning, (float)(Time / _warning));
            Line(new Vector2(x, seam + halfGap), new Vector2(x, 204), 6 + Tier, Time >= _warning, (float)(Time / _warning));
            Hint(new Vector2(x, seam - 10), "つながる前に抜ける");
        }
        for (int fold = 0; fold < 2; fold++)
            if (At(_warning + fold * .9))
            {
                Seed(new Vector2(x, seam - 50 + fold * 16), Vector2.Down, fold);
                Seed(new Vector2(x, seam + 50 - fold * 16), Vector2.Up, fold);
                if (Tier >= 2) Seed(Side(seam + (fold == 0 ? -70 : 70)), _forward, fold);
            }
        DrainAfter(close + 1.7);
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
            Hint(state.Rect.GetCenter() + Vector2.Right * 17, $"引用 {3 - state.Hits}");
        }
        Hint(new Vector2(Field.CenterX, 202), "頁が来る。撃って剥がす／上下の端を回る");
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
                Hint(word.Bullet.GlobalPosition + Vector2.Up * 15, "訂正");
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

    private void QuoteChain()
    {
        _citationGuides.Clear();
        int[] belongsTo = { 0, 2, 1, 1, 0, 2, 2, 1, 0 };
        int[] firstCitation = { 4, 3, 5 };
        int[] order = { 1, 0, 2 };
        Vector2 Point(int node) => new(Field.Left + 53 + node % 3 * 106, 36 + node / 3 * 72);
        double firstDelete = _warning + 1.2 + Tier * .25;
        for (int step = 0; step < 3; step++)
        {
            int root = order[step];
            double erase = firstDelete + step * 2.1;
            Vector2 original = new(Field.Left + 30 + root * 129, 14);
            if (Time < erase)
            {
                Hint(original, (root + 1) + (Time >= erase - 1.2 ? " 消去予告" : " 引用元"));
                _citationGuides.Add((original + Vector2.Down * 6, Point(firstCitation[root])));
            }
            if (At(erase)) _deletedRoots[root] = true;
        }
        for (int node = 0; node < 9; node++)
        {
            int root = belongsTo[node];
            int parent = firstCitation[root];
            bool parentGone = node == parent ? _deletedRoots[root] : Time >= _citationDeletes[parent];
            if (parentGone && double.IsPositiveInfinity(_citationDeletes[node])) _citationDeletes[node] = Time + .3;
            if (Time >= _citationDeletes[node]) continue;
            Vector2 p = Point(node);
            if (node != parent && Time < _citationDeletes[parent]) _citationGuides.Add((Point(parent), p));
            Vector2 size = new(76 + Tier * 4, 44 + Tier * 4);
            Box(new Rect2(p - size * .5f, size), Time >= _warning, (float)(Time / _warning));
            Hint(p, (root + 1) + (double.IsPositiveInfinity(_citationDeletes[node]) ? " の引用" : " ほどける"));
        }
        Hint(new Vector2(Field.CenterX, 210), "原稿が消えると、その子と孫だけが消える");
        if (_citationDeletes.All(at => Time >= at)) Finish();
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
                if (negateOutside)
                {
                    var inner = quote.Grow(-overlap);
                    Box(new Rect2(Field.Left, 0, inner.Position.X - Field.Left, Field.Bottom), live, progress);
                    Box(new Rect2(inner.End.X, 0, Field.Right - inner.End.X, Field.Bottom), live, progress);
                    Box(new Rect2(inner.Position.X, 0, inner.Size.X, inner.Position.Y), live, progress);
                    Box(new Rect2(inner.Position.X, inner.End.Y, inner.Size.X, Field.Bottom - inner.End.Y), live, progress);
                }
                else Box(quote.Grow(overlap), live, progress);
                Hint(new Vector2(Field.CenterX, 43), negateOutside ? "外が否定される → 括弧の内へ" : "内が否定される → 括弧の外へ");
                Hint(new Vector2(Field.CenterX, 179), live ? "否定中" : "中立・いま境界をまたげる");
            }
            at += liveFor;
        }
        Hint(new Vector2(quote.Position.X, 63), "「");
        Hint(new Vector2(quote.End.X, 154), "」");
        if (Time >= at) Finish();
    }

    protected override void DrawTechnique()
    {
        if (EasyEditorial) { DrawEasyEditorial(); return; }
        if (!GameManager.ShowAttackTelegraphs) return;
        DrawEditorialCues();
        foreach (var guide in _citationGuides)
            DrawDashedLine(guide.A, guide.B, new Color(Ink, .55f), .55f, 3, true);
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
        bool warnings = GameManager.ShowAttackTelegraphs;
        var warning = new Color("f47773") { A = .43f };
        if (warnings)
        {
            if (Kind == BossTechniqueKind.Normal && Variant == 0 && !_easySelectionBuilt)
            {
                DrawFilm(_selectionRect.GetCenter(), _selectionRect.Size, new Color(Ink, .65f));
                DrawQuotation(_selectionStart, 6, new Color(Original, .75f));
                DrawEditorialMark(_selectionRect.GetCenter() + Vector2.Up * 20, "引用範囲を選択", Original);
            }
            foreach (var path in _easyPaths)
            {
                if (!path.Draw || Time < path.StartsAt) continue;
                if (Time < path.WarningEndsAt)
                    DrawEasyRoute(path.From, path.To, path.HalfWidth, EasyCueProgress(path.StartsAt), warning);
                if (Time < path.ArrivesAt && _easyPaths.Any(p => p.Parent >= 0 && ReferenceEquals(_easyPaths[p.Parent], path)))
                {
                    DrawQuotation(path.To, 5, new Color(Original, .7f));
                    DrawArc(path.To, 4.5f, 0, Mathf.Tau, 12, new Color(warning, .6f), .65f, true);
                }
            }
            foreach (var cut in _easyCuts)
            {
                if (Time < cut.StartsAt || Time >= cut.FiresAt) continue;
                float progress = EasyCueProgress(cut.StartsAt);
                if (cut.Box) DrawEasyWarningBox(new Rect2(cut.A, cut.B - cut.A), progress, warning);
                else DrawEasyRoute(cut.A, cut.B, cut.HalfWidth, progress, warning);
            }
            if (Kind == BossTechniqueKind.Area && Time < EasyCueLead) DrawEasyReaderWarning(warning);
            if (Kind == BossTechniqueKind.Finale && Time < EasyCueLead) DrawEasySentenceWarning(warning);
            if (Kind == BossTechniqueKind.Signature && Time < 2.2)
            {
                for (int edit = 0; edit < _points.Length; edit++)
                {
                    DrawCropCorner(_points[edit] + new Vector2(-8, -8), 5, Vector2.One, new Color(Ink, .6f));
                    DrawEditorialMark(_points[edit] + Vector2.Up * 13, (edit + 1).ToString(), Original);
                }
                float t = Mathf.Clamp((float)(Time / 1.1), 0, 2);
                int leg = Mathf.Min(1, (int)t);
                Vector2 editor = _points[leg].Lerp(_points[leg + 1], t - leg);
                DrawFilm(editor, new Vector2(14, 22), new Color(Original, .8f));
            }
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
                DrawEditorialMark(path.Shot!.GlobalPosition + Vector2.Up * 14, (3 - path.Hits).ToString(), Original);
        }
        foreach (var h in HazardShapes.Where(h => h.Live))
        {
            var fill = new Color("df737c") { A = .19f };
            var edge = new Color("ffb09c") { A = .9f };
            if (h.Shape == 2)
            {
                var rect = new Rect2(h.A, h.B - h.A);
                DrawRect(rect, fill);
                DrawRect(rect, edge, false, 1, true);
                for (float x = rect.Position.X + 7; x < rect.End.X; x += 20)
                    DrawLine(new Vector2(x, rect.Position.Y + 2), new Vector2(Mathf.Min(x + 7, rect.End.X), Mathf.Min(rect.Position.Y + 9, rect.End.Y)), fill, .7f, true);
            }
            else
            {
                DrawLine(h.A, h.B, fill, h.Radius * 2, true);
                Vector2 side = h.A.DirectionTo(h.B).Orthogonal() * h.Radius;
                DrawLine(h.A + side, h.B + side, edge, .9f, true);
                DrawLine(h.A - side, h.B - side, edge, .9f, true);
                DrawArc(h.A, h.Radius, 0, Mathf.Tau, 16, edge, .7f, true);
                DrawArc(h.B, h.Radius, 0, Mathf.Tau, 16, edge, .7f, true);
            }
        }
    }

    private void DrawEasyOrigin(Vector2 at, Color ink)
    {
        for (int dash = 0; dash < 4; dash++)
            DrawArc(at, 3.5f, dash * Mathf.Pi / 2, dash * Mathf.Pi / 2 + .7f, 4, ink, .7f, true);
    }

    private void DrawEasyRoute(Vector2 from, Vector2 to, float halfWidth, float progress, Color ink)
    {
        DrawEasyOrigin(from, ink);
        float grow = Mathf.Clamp((progress * (float)EasyCueGrow - .4f) / .8f, 0, 1);
        if (grow <= 0) return;
        Vector2 end = from.Lerp(to, grow);
        Vector2 direction = from.DirectionTo(to), side = direction.Orthogonal() * halfWidth;
        DrawDashedLine(from + side, end + side, ink, .6f, 4, true);
        DrawDashedLine(from - side, end - side, ink, .6f, 4, true);
        DrawDashedLine(from, end, new Color(ink, ink.A * .6f), .55f, 6, true);
        DrawArc(end, halfWidth, 0, Mathf.Tau, 14, new Color(ink, ink.A * .6f), .5f, true);
        for (float distance = 26; distance < from.DistanceTo(end) - 8; distance += 76)
        {
            Vector2 tip = from + direction * distance;
            DrawLine(tip - direction * 3 + direction.Orthogonal() * 2, tip, ink, .7f, true);
            DrawLine(tip - direction * 3 - direction.Orthogonal() * 2, tip, ink, .7f, true);
        }
    }

    private void DrawEasyWarningBox(Rect2 rect, float progress, Color ink)
    {
        DrawEasyOrigin(rect.Position, ink);
        float grow = Mathf.Clamp((progress * (float)EasyCueGrow - .4f) / .8f, 0, 1);
        if (grow <= 0) return;
        var end = rect.Position + rect.Size * grow;
        Vector2[] corners = { rect.Position, new(end.X, rect.Position.Y), end, new(rect.Position.X, end.Y) };
        for (int edge = 0; edge < 4; edge++) DrawDashedLine(corners[edge], corners[(edge + 1) % 4], ink, .65f, 4, true);
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
            DrawColoredPolygon(left.ToArray(), new Color(ink, .035f));
            DrawColoredPolygon(right.ToArray(), new Color(ink, .035f));
        }
        for (int frame = 0; frame <= last; frame++)
        {
            var g = frames[frame];
            if (frame % 4 == 0)
            {
                DrawDashedLine(g.A, g.B, new Color(ink, .22f), .55f, 5, true);
                DrawDashedLine(g.C, g.D, new Color(ink, .22f), .55f, 5, true);
            }
            if (frame == 0) continue;
            var previous = frames[frame - 1];
            DrawLine(previous.B + Vector2.Right * 4, g.B + Vector2.Right * 4, ink, .7f, true);
            DrawLine(previous.C + Vector2.Left * 4, g.C + Vector2.Left * 4, ink, .7f, true);
        }
        if (progress >= 1)
        {
            var g = EasyReaderGeometry(EasyCueLead);
            DrawEasyRoute(g.A, g.B, g.HalfWidth, 1, ink);
            DrawEasyRoute(g.C, g.D, g.HalfWidth, 1, ink);
        }
    }

    private void DrawEasySentenceWarning(Color ink)
    {
        float progress = EasyCueProgress(0);
        float grow = Mathf.Clamp((progress * (float)EasyCueGrow - .4f) / .8f, 0, 1);
        var frames = EasySentenceFrames.ToArray();
        DrawEasyOrigin(new Vector2(frames[0].Head, frames[0].Center - 34), ink);
        if (grow <= 0) return;
        int last = Mathf.FloorToInt(grow * (frames.Length - 1));
        for (int frame = 0; frame <= last; frame++)
        {
            var g = frames[frame];
            DrawRect(g.Upper, new Color(ink, .009f));
            DrawRect(g.Lower, new Color(ink, .009f));
            if (g.Tail.Size.X > 0) DrawRect(g.Tail, new Color(ink, .004f));
            if (frame > 0)
            {
                var previous = frames[frame - 1];
                DrawLine(new Vector2(previous.Head, previous.Center - 34), new Vector2(g.Head, g.Center - 34), ink, .8f, true);
                DrawLine(new Vector2(previous.Head, previous.Center + 34), new Vector2(g.Head, g.Center + 34), ink, .8f, true);
            }
            if (frame % 8 != 0) continue;
            DrawEasyWarningBox(g.Upper, 1, new Color(ink, .22f));
            DrawEasyWarningBox(g.Lower, 1, new Color(ink, .22f));
            float tail = _anchor.X < Field.CenterX ? g.Tail.End.X : g.Tail.Position.X;
            DrawDashedLine(new Vector2(tail, 0), new Vector2(tail, Field.Bottom), new Color(ink, .2f), .55f, 6, true);
        }
    }

    private void DrawEditorialCues()
    {
        var paper = new Color(Ink, .55f);
        var quiet = new Color(Ink, .26f);
        switch (Kind)
        {
            case BossTechniqueKind.Normal when Variant == 0:
            {
                if (_selectionRepostAt >= 0)
                {
                    if (Time < _selectionRepostAt)
                    {
                        float copy = Mathf.Clamp((float)(1 - (_selectionRepostAt - Time) / 1.3), 0, 1);
                        DrawFilm(_selectionRect.GetCenter().Lerp(_selectionLaunch, copy), new Vector2(16, 10), quiet);
                        DrawQuotation(_selectionLaunch + _forward * 11, 7, paper);
                        DrawEditorialMark(_selectionLaunch + Vector2.Up * 13, "引用", paper);
                    }
                    break;
                }
                DrawCropCorner(_selectionRect.Position + new Vector2(3, 3), 7, Vector2.One, paper);
                DrawCropCorner(_selectionRect.End - new Vector2(3, 3), 7, -Vector2.One, paper);
                DrawCircle(_selectionStart, 2, quiet);
                DrawEditorialMark(_selectionStart + Vector2.Up * 8, "始", paper);
                DrawEditorialMark(_selectionEnd + Vector2.Down * 13, _selectionLocked ? "確定" : "終", paper);
                if (!_selectionLocked)
                    DrawDashedLine(_selectionStart, _selectionEnd, quiet, .5f, 3, true);
                break;
            }
            case BossTechniqueKind.Normal when Variant == 1:
                foreach (var word in _words.Where(Exists).Where(w => w.Stage == 0))
                {
                    Vector2 fork = word.Start + _forward * 42;
                    DrawQuotation(fork + Vector2.Up * 12, 7, paper);
                    DrawEditorialMark(word.Bullet.GlobalPosition + Vector2.Up * 11, "原文", paper);
                }
                break;
            case BossTechniqueKind.Normal:
                for (int edge = 0; edge < 3; edge++)
                {
                    Vector2 at = _points[edge].Lerp(_points[(edge + 1) % 3], .5f);
                    DrawCropCorner(at, 7, (at - new Vector2(Field.CenterX, 130)).Normalized(), paper);
                    DrawEditorialMark(_points[edge] + Vector2.Down * 13, (edge + 1).ToString(), paper);
                }
                break;
            case BossTechniqueKind.Edge:
                if (_gateLeg >= 0)
                {
                    float x = Mathf.Clamp(_previousGate, Field.Left + 6, Field.Right - 6);
                    for (int row = 0; row < 6; row++)
                    {
                        float y = 24 + row * 32;
                        float direction = _gateLeg % 2 == 0 ? 1 : -1;
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
                    if (cursor > y + 10) DrawEditorialMark(new Vector2(Field.Left + 20, y), "✓", paper);
                }
                break;
            }
            case BossTechniqueKind.Area when Variant == 1:
            {
                Vector2 center = Inside(_anchor, 72);
                foreach (var offset in new[] { new Vector2(-71, -55), new Vector2(71, -55), new Vector2(71, 55), new Vector2(-71, 55) })
                    DrawCropCorner(center + offset, 9, -offset.Normalized(), paper);
                DrawEditorialMark(center + new Vector2(0, -62), "REC", paper);
                DrawCircle(center + new Vector2(-17, -65), 1.6f, new Color(Ink, .3f + .2f * Mathf.Sin((float)Time * 4)));
                break;
            }
            case BossTechniqueKind.Area:
            {
                Vector2 center = Inside(_anchor, 74);
                for (int cut = 0; cut < (Tier >= 2 ? 3 : 2); cut++)
                {
                    double begin = _warning + cut * 2.4;
                    if (Time < begin - _warning || Time >= begin + 1.65) continue;
                    Vector2 direction = Vector2.FromAngle(cut * Mathf.Pi / 3);
                    DrawCropCorner(center - direction * 73, 7, direction, paper);
                    DrawCropCorner(center + direction * 73, 7, -direction, paper);
                    DrawEditorialMark(center - direction * 80 + direction.Orthogonal() * 12, "切取", paper);
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
                        DrawEditorialMark(word.Bullet.GlobalPosition + Vector2.Down * 14, "合稿", paper);
                break;
            case BossTechniqueKind.Crossfire when Variant == 1:
            {
                float y = Mathf.Clamp(_anchor.Y, 46, 170);
                foreach (var quote in _insertions)
                {
                    float p = Mathf.Clamp((float)((Time - quote.At) / .7), 0, 1);
                    float x = Field.Left + 22 + Mathf.Lerp(quote.From, quote.To, p) * 34;
                    DrawLine(new Vector2(x - 9, y + 12), new Vector2(x + 9, y + 12), quiet, .5f, true);
                    if (quote.New && Time < quote.At + 1.2)
                    {
                        DrawLine(new Vector2(x - 5, y + 20), new Vector2(x, y + 15), paper, .8f, true);
                        DrawLine(new Vector2(x, y + 15), new Vector2(x + 5, y + 20), paper, .8f, true);
                    }
                }
                break;
            }
            case BossTechniqueKind.Crossfire when Variant == 2:
            {
                float reach = Mathf.Clamp((float)(Time - _warning) * FrontSpeed, 0, FrontReach);
                Vector2 tip = _frontRoot + _forward * reach;
                DrawEditorialMark(_frontRoot - _frontUp * 13, "原文", paper);
                DrawCropCorner(tip + _frontUp * 10, 6, _forward, paper);
                for (int i = 0; i < Copies; i++)
                {
                    float distance = Mathf.Lerp(12, FrontReach - 12, i / (float)(Copies - 1));
                    Vector2 branch = _frontRoot + _forward * distance;
                    DrawQuotation(branch - _frontUp * 13, 5, paper);
                    DrawEditorialMark(branch - _frontUp * 23, (i + 1).ToString(), quiet);
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
                        DrawEditorialMark(mid + Vector2.Up * 18, Time < quote.At ? "再掲待ち" : "再掲", paper);
                    }
                break;
            case BossTechniqueKind.Dodge:
            {
                Vector2 center = Inside(_anchor, 65);
                float x = center.X + (_forward.X < 0 ? -1 : 1) * 15;
                if (Time < _warning + .7) DrawFilm(new Vector2(x, center.Y), new Vector2(15 + Tier * 3.4f, 112), quiet);
                break;
            }
            case BossTechniqueKind.Extra when !Evolved && Variant == 0:
                DrawEditorialMark(new Vector2(Field.CenterX - 18, _source.Y - 43), "¶  一段目 → 二段目", paper);
                if (Time > _warning + 2.7)
                {
                    DrawQuotation(new Vector2(Field.CenterX, _source.Y), 22, quiet);
                    DrawLine(new Vector2(Field.CenterX - 28, _source.Y + 15), new Vector2(Field.CenterX + 28, _source.Y + 15), quiet, .5f, true);
                }
                break;
            case BossTechniqueKind.Extra when !Evolved:
            {
                float x = Mathf.Clamp(_anchor.X + _forward.X * 54, Field.Left + 78, Field.Right - 78);
                float seam = Mathf.Clamp(_anchor.Y, 72, 144);
                float gap = Time < _warning ? 48 : Mathf.Lerp(48, 9, Mathf.Clamp((float)((Time - _warning) / 1.8), 0, 1));
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
            case BossTechniqueKind.Cameo when Variant == 0:
                for (int root = 0; root < 3; root++)
                {
                    Vector2 p = new(Field.Left + 30 + root * 129, 14);
                    DrawQuotation(p + Vector2.Down * 7, 8, quiet);
                    if (_deletedRoots[root])
                        DrawLine(p + new Vector2(-12, 2), p + new Vector2(12, 8), paper, .8f, true);
                }
                break;
            case BossTechniqueKind.Cameo:
            {
                var rect = new Rect2(Field.CenterX - 66, 62, 132, 92);
                DrawLine(rect.Position + new Vector2(-5, 5), rect.Position + new Vector2(-5, 24), paper, .8f, true);
                DrawLine(rect.End + new Vector2(5, -5), rect.End + new Vector2(5, -24), paper, .8f, true);
                DrawEditorialMark(new Vector2(Field.CenterX, 27), "「　」 と、その外側", quiet);
                break;
            }
        }
    }

    private void DrawQuotation(Vector2 at, float span, Color ink)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 corner = at + new Vector2(side * span, side * 4);
            DrawLine(corner, corner + Vector2.Up * side * 7, ink, .7f, true);
            DrawLine(corner, corner - Vector2.Right * side * 4, ink, .7f, true);
        }
    }

    private void DrawCropCorner(Vector2 at, float size, Vector2 inward, Color ink)
    {
        Vector2 horizontal = new(Math.Sign(inward.X) * size, 0);
        Vector2 vertical = new(0, Math.Sign(inward.Y) * size);
        if (horizontal == Vector2.Zero) horizontal = Vector2.Right * size;
        if (vertical == Vector2.Zero) vertical = Vector2.Down * size;
        DrawLine(at, at + horizontal, ink, .7f, true);
        DrawLine(at, at + vertical, ink, .7f, true);
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

    private void DrawEditorialMark(Vector2 at, string text, Color ink)
    {
        float width = UiKit.ZenBold.GetStringSize(text, fontSize: 6).X;
        at.X = Mathf.Clamp(at.X - width * .5f, Field.Left + 3, Field.Right - width - 3);
        at.Y = Mathf.Clamp(at.Y, 8, Field.Bottom - 4);
        DrawString(UiKit.ZenBold, at, text, HorizontalAlignment.Left, -1, 6, ink);
    }
}
