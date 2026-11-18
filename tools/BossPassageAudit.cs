using Godot;
using System;
using System.Collections.Generic;

internal sealed class BossPassageAudit
{
    private readonly List<(float Start, float End)> _horizontal = new(), _vertical = new();
    private int _frame, _first = -1, _last = -1, _wide, _wideAtLast;
    private float _minimum = float.MaxValue;

    internal void Observe(BossCharacterTechnique attack, Bullet[] shots)
    {
        Rect2 field = Field.Rect;
        _horizontal.Clear();
        _vertical.Clear();
        void Add(Rect2 bounds)
        {
            var clipped = field.Intersection(bounds);
            if (!clipped.HasArea()) return;
            _horizontal.Add((clipped.Position.X, clipped.End.X));
            _vertical.Add((clipped.Position.Y, clipped.End.Y));
        }
        foreach (var shot in shots)
        {
            var bounds = shot.Footprint?.Bounds ?? new Rect2(-Vector2.One * shot.CollisionRadius, Vector2.One * (2 * shot.CollisionRadius));
            Add(shot.GlobalTransform * bounds);
        }
        foreach (var h in attack.PracticeHazards)
            if (h.Live) Add(h.Shape == 3 ? field : new Rect2(h.A, h.B - h.A).Abs().Grow(h.Radius));
        if (attack is KoharuBossTechnique { MechanicKey: "koharu.pursuit-train" } chase)
            foreach (var wall in chase.VisiblePursuitBarriers) if (wall.Solid) Add(wall.Bounds);

        bool live = _horizontal.Count > 0;
        if (live && _first < 0) _first = _frame;
        if (_first >= 0)
        {
            float width = Math.Max(Gap(_horizontal, field.Position.X, field.End.X),
                Gap(_vertical, field.Position.Y, field.End.Y));
            if (width >= 15.2f) _wide++;
            if (live)
            {
                _last = _frame;
                _wideAtLast = _wide;
                _minimum = Math.Min(_minimum, width);
            }
        }
        _frame++;
    }

    private static float Gap(List<(float Start, float End)> intervals, float edge, float end)
    {
        intervals.Sort();
        float widest = 0;
        foreach (var span in intervals)
        {
            widest = Math.Max(widest, span.Start - edge);
            edge = Math.Max(edge, span.End);
        }
        return Math.Max(widest, end - edge);
    }

    // A completely empty strip is a witness of excess width; overlapping projections alone never establish a pass.
    internal object Result()
    {
        int activeWindowFrames = _first < 0 ? 0 : _last - _first + 1;
        return new { activeWindowFrames, wideStripFrames = _wideAtLast,
            qualifyingFractionUpperBound = activeWindowFrames > 0 ? (double?)(1d - (double)_wideAtLast / activeWindowFrames) : null,
            minimumWideStrip = _first < 0 ? (float?)null : _minimum };
    }
}
