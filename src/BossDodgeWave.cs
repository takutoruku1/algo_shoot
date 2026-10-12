using Godot;
using System;

public partial class BossDodgeWave : Node2D, IAoeHazard
{
    internal const float Speed = 140f;
    internal const float Separation = 34f;
    internal static Color Ink => Bullet.BossColor(GameManager.Instance!.SelectedJob, BossShotStyle.Ring);
    private const float HalfWidth = 3f;
    private const double Warning = 1.35;
    private Enemy _boss = null!;
    private double _warning;
    private float _position, _previous;
    private int _wave, _side;
    private bool _horizontal;
    private float _direction;
    public bool Finished { get; private set; }
    public bool IsStriking => !Finished && _warning <= 0 && !Hud.BubblePaused && !Interrupted;
    internal double WarningRemaining => _warning;
    internal Vector2 Direction => _horizontal ? Vector2.Right * _direction : Vector2.Down * _direction;
    internal int Wave => _wave;
    private bool Interrupted => _boss.IsPurified || _boss.Transforming || !_boss.IsVisibleInTree()
        || _boss.GaugeVulnerable || _boss.GaugeReforming || _boss is BossMina { Transitioning: true };

    public static BossDodgeWave Begin(Enemy boss)
    {
        var wave = new BossDodgeWave { _boss = boss, TopLevel = true };
        boss.AddChild(wave);
        return wave;
    }

    public override void _Ready()
    {
        GlobalPosition = Vector2.Zero;
        ZIndex = 3;
        ZAsRelative = false;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        AddToGroup("boss_dodge_waves");
        var player = (Player)GetTree().GetFirstNodeInGroup("player");
        _side = player.GlobalPosition.X < _boss.ShotCenter.X ? 3 : 1;
        _boss.AnnounceTechnique("境界を越えて");
        Prepare();
    }

    private void Prepare()
    {
        _horizontal = _side % 2 == 1;
        _direction = _side is 0 or 3 ? 1 : -1;
        float low = _horizontal ? Field.Left : Field.Top;
        float high = _horizontal ? Field.Right : Field.Bottom;
        _position = _previous = _direction > 0 ? low - 8 : high + 8;
        _warning = Warning;
        Audio.Instance?.PlayChargeReady(_boss.BodyShotJob!.Value);
    }

    public override void _Process(double delta)
    {
        if (!Finished && Interrupted) Cancel();
        QueueRedraw();
    }

    internal void Tick(double delta)
    {
        if (Finished) return;
        if (Interrupted) { Cancel(); return; }
        if (Hud.BubblePaused) return;
        if (_warning > 0)
        {
            _warning = Math.Max(0, _warning - delta);
            if (_warning == 0) Audio.Instance?.PlayChargeRelease(_boss.BodyShotJob!.Value);
            return;
        }
        _previous = _position;
        _position += _direction * Speed * (float)delta;
        var player = (Player)GetTree().GetFirstNodeInGroup("player");
        if (!QaPilot.GodActive && CoversPoint(player.GlobalPosition)) player.TakeHit();
        float low = _horizontal ? Field.Left : Field.Top;
        float high = _horizontal ? Field.Right : Field.Bottom;
        float tail = _position - _direction * Separation;
        if (_direction > 0 ? tail > high + 8 : tail < low - 8)
        {
            _wave++;
            if (_wave >= (_boss.HasEvolved ? 4 : 3)) { Cancel(); return; }
            _side = (_side + 1) % 4;
            Prepare();
        }
        QueueRedraw();
    }

    public bool CoversPoint(Vector2 point)
    {
        if (!IsStriking || point.X < Field.Left || point.X > Field.Right
            || point.Y < Field.Top || point.Y > Field.Bottom) return false;
        float axis = _horizontal ? point.X : point.Y;
        for (int i = 0; i < 2; i++)
        {
            float offset = _direction * Separation * i;
            if (axis >= Math.Min(_previous, _position) - offset - HalfWidth - Player.HitRadius
                && axis <= Math.Max(_previous, _position) - offset + HalfWidth + Player.HitRadius) return true;
        }
        return false;
    }

    internal float DistanceToFront(Vector2 point) => (_horizontal ? point.X - _position : point.Y - _position) * _direction;

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
        float low = _horizontal ? Field.Left : Field.Top;
        float high = _horizontal ? Field.Right : Field.Bottom;
        float crossLow = _horizontal ? Field.Top : Field.Left;
        float crossHigh = _horizontal ? Field.Bottom : Field.Right;
        Vector2 At(float axis, float cross) => _horizontal ? new Vector2(axis, cross) : new Vector2(cross, axis);
        var ink = Ink;
        if (_warning > 0)
        {
            float charge = 1 - (float)(_warning / Warning);
            float edge = _direction > 0 ? low + 3 : high - 3;
            DrawLine(_boss.ShotCenter, At(edge, (crossLow + crossHigh) / 2), new Color(ink, 0.3f), 1, true);
            for (int i = 0; i < 2; i++)
            {
                float axis = edge + _direction * i * 7;
                DrawLine(At(axis, crossLow), At(axis, crossHigh), new Color("111820"), 4, true);
                DrawLine(At(axis, crossLow), At(axis, crossHigh), new Color(ink, 0.35f + charge * 0.65f), 1.5f, true);
                for (float cross = crossLow + 10; cross < crossHigh; cross += 24)
                    DrawPolyline(new[] { At(axis, cross - 3), At(axis + _direction * (5 + charge * 6), cross), At(axis, cross + 3) }, ink, 1, true);
            }
            return;
        }
        for (int i = 0; i < 2; i++)
        {
            float axis = _position - _direction * Separation * i;
            if (axis < low || axis > high) continue;
            Vector2 a = At(axis, crossLow), b = At(axis, crossHigh);
            DrawLine(a, b, new Color("111820"), 11, true);
            DrawLine(a, b, new Color(ink, 0.3f), 9, true);
            DrawLine(a, b, ink, HalfWidth * 2, true);
            DrawLine(a, b, new Color("fff1c2"), 1.5f, true);
            for (float cross = crossLow + 10; cross < crossHigh; cross += 24)
                DrawPolyline(new[] { At(axis - _direction * 3, cross - 3), At(axis + _direction * 3, cross),
                    At(axis - _direction * 3, cross + 3) }, new Color("fff1c2"), 1, true);
        }
    }
}
