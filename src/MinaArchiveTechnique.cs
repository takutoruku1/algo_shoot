using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class MinaArchiveTechnique : BossCharacterTechnique
{
    internal const int FloorCount = 20;
    internal const float FloorHeight = 24;
    private float GroundTop => TargetOpening;
    private const double BlueprintComplete = .4;
    private double _firstLock;
    private MinaArchiveVisuals? _visuals;
    private float _phase;
    internal float PassageWidth => TargetOpening;
    internal float LiftSpeed => Tier switch { 0 => 24, 1 => 27, 2 => 30, _ => 32 };
    internal double LiftAt => LockAt(0) + .8;
    private float FullLift => GroundTop + FloorCount * FloorHeight - Field.Height;
    private double DoorsOpenAt => LiftAt + FullLift / LiftSpeed;
    internal double EndsAt => DoorsOpenAt + .85;
    internal double LockAt(int floor) => _firstLock + Math.Max(0, 7 - floor) * .09;
    internal float Lift => Mathf.Clamp((float)(Time - LiftAt) * LiftSpeed, 0, FullLift);
    internal IEnumerable<(int Floor, Rect2 Left, Rect2 Right, double EntersAt)> EntranceForecasts
    {
        get
        {
            int firstBelowField = Mathf.CeilToInt((Field.Height - GroundTop) / FloorHeight);
            for (int floor = firstBelowField; floor < FloorCount; floor++)
            {
                double enters = LiftAt + (GroundTop + floor * FloorHeight - Field.Bottom) / LiftSpeed;
                if (Time < enters - 2.6 || Time >= enters) continue;
                float entranceLift = GroundTop + floor * FloorHeight - Field.Bottom;
                yield return (floor, FloorBounds(floor, false, entranceLift), FloorBounds(floor, true, entranceLift), enters);
            }
        }
    }
    protected override bool UsesDangerForecast => true;
    protected override void DrawDangerForecast(Node2D canvas)
    {
        base.DrawDangerForecast(canvas);
        foreach (var cue in EntranceForecasts)
        {
            float y = 209 + (cue.Floor % 3) * 2;
            canvas.DrawRect(new Rect2(cue.Left.Position.X, y - 2, cue.Left.Size.X, 4), Colors.White);
            canvas.DrawRect(new Rect2(cue.Right.Position.X, y - 2, cue.Right.Size.X, 4), Colors.White);
        }
    }
    protected override bool UsesCustomHazardArt => true;
    protected override bool UsesAttackLineArt => false;

    protected override void Start()
    {
        Identify("未送信の摩天楼", "mina.cantilever-archive",
            "次の階の張り出しを下端で読み、上昇に合わせて左右にせり出す階の間をたどる");
        _firstLock = BlueprintComplete + WarnFor(Field.Width * .5f + 48, 2.4);
        _phase = Player.GlobalPosition.X < Field.CenterX ? 0 : Mathf.Pi;
    }

    internal Rect2 FloorBounds(int floor, bool right, float lift)
    {
        float step = Math.Min(4, PassageWidth - 7.6f);
        float center = Field.CenterX + 12 * step * (2 / Mathf.Pi) * Mathf.Asin(Mathf.Sin(floor * Mathf.Tau / 48 + _phase));
        center += 24 * Mathf.Sin(lift / (2.4f * FloorHeight) + _phase);
        float edge = center + (right ? 1 : -1) * PassageWidth * .5f;
        float slide = Mathf.SmoothStep(0, 1, (float)Math.Clamp((Time - DoorsOpenAt) / .85, 0, 1)) * Field.Width;
        return new Rect2((right ? edge : Field.Left) + (right ? slide : -slide), GroundTop + floor * FloorHeight - lift,
            right ? Field.Right - edge : edge - Field.Left, FloorHeight);
    }

    internal override IEnumerable<BossEasyCue> EasyCues => Enumerable.Range(0, FloorCount)
        .Select(floor => new BossEasyCue($"archive-floor-{floor}", 0, LockAt(floor),
            Mathf.Clamp((float)(Time / BlueprintComplete), 0, 1), Time >= LockAt(floor)));

    protected override void Step(double delta)
    {
        if (_visuals == null && DisplayServer.GetName() != "headless")
        {
            _visuals = new MinaArchiveVisuals();
            AddChild(_visuals);
            _visuals.Initialize(this);
            Ended += _ => _visuals.Stop();
        }
        for (int floor = 0; floor < FloorCount; floor++)
        {
            bool live = Time >= LockAt(floor);
            foreach (bool right in new[] { false, true })
            {
                var bounds = FloorBounds(floor, right, Lift).Intersection(Field.Rect);
                if (bounds.HasArea()) Box(bounds, live, Mathf.Clamp((float)(Time / BlueprintComplete), 0, 1));
            }
        }
        _visuals?.Sync(this);
        if (Time >= EndsAt + .1) Finish();
    }

    protected override void DrawTechnique()
    {
        var ink = new Color("e9bb78");
        if (GameManager.ShowAttackTelegraphs)
        {
            foreach (var hazard in HazardShapes.Where(h => !h.Live))
            {
                var box = new Rect2(hazard.A, hazard.B - hazard.A);
                AttackTelegraph.Box(this, box, new Color(ink, .5f));
                DrawLine(box.Position, box.End, new Color(ink, .18f), .4f, true);
            }
            foreach (var cue in EntranceForecasts)
                foreach (var side in new[] { cue.Left, cue.Right })
                {
                    float x = side.Position.X == Field.Left ? side.End.X : side.Position.X;
                    DrawDashedLine(new Vector2(x, Field.Top), new Vector2(x, Field.Bottom), new Color(ink, .42f), .4f, 2, true);
                    DrawPolyline(new[] { new Vector2(x - 2, 205), new Vector2(x, 202), new Vector2(x + 2, 205) },
                        new Color(ink, .7f), .6f, true);
                }
        }
        _visuals?.Draw(this);
        if (GameManager.ShowAttackTelegraphs)
            foreach (var cue in EntranceForecasts)
                foreach (var side in new[] { cue.Left, cue.Right })
                {
                    float y = 209 + (cue.Floor % 3) * 2;
                    DrawDashedLine(new Vector2(side.Position.X, y), new Vector2(side.End.X, y), ink, .6f, 2, true);
                }
        foreach (var hazard in HazardShapes.Where(h => h.Live))
        {
            var box = new Rect2(hazard.A, hazard.B - hazard.A);
            DrawRect(box, new Color("c5f4f6"), false, .5f);
        }
        if (Time < LiftAt && GameManager.ShowAttackTelegraphs)
        {
            float progress = Mathf.Clamp((float)((Time - BlueprintComplete) / (_firstLock - BlueprintComplete)), 0, 1);
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? Field.Left + 5 : Field.Right - 5;
                for (int y = 42; y < 205; y += 48)
                    DrawPolyline(new[] { new Vector2(x - 2, y + 3), new Vector2(x, y), new Vector2(x + 2, y + 3) },
                        new Color(ink, .7f), .7f, true);
                DrawLine(new Vector2(x, 214), new Vector2(x, 214 - progress * 10), ink, 1.2f, true);
            }
        }
    }
}
