using Godot;
using System.Linq;

public partial class BossPracticeRoot
{
    internal static BossPracticeRoot? Active { get; private set; }
    private static readonly double[] PlaybackRates = { 1, .5, .25 };
    private static readonly float[] AttackTempos = { 1, .75f, 1.25f, 1.5f };
    private static readonly double[] RepeatDelays = { .5, 1, 2, 0 };
    private static readonly Color LiveOutline = new("ffad52"), WarningOutline = new("67e4fa");
    private Node2D _diagnostics = null!;
    private double _previousTimeScale;
    private int _playback, _tempo, _repeatDelay;
    private bool _showCollision, _stepRequested, _stepping;
    internal bool PreviewPaused { get; private set; }
    internal double PlaybackRate => PlaybackRates[_playback];
    internal double ClockRate => PreviewPaused ? 1 : PlaybackRate;
    internal float AttackTempo => AttackTempos[_tempo];
    internal double RepeatDelay => RepeatDelays[_repeatDelay];
    internal ulong SimulationFrames { get; private set; }
    internal BossPracticeCurveEditor? CurveEditor { get; private set; }
    private CanvasLayer? _curveLayer;
    private bool _pausedBeforeCurve;

    public override void _PhysicsProcess(double delta)
    {
        if (CurveEditor != null || GetNode<PauseMenu>("/root/PauseMenu").IsOpen || LoadingScreen.IsActive) return;
        if (!Practicing)
        {
            if (!GetTree().Paused && !_leaving) MovePreviewPlayer(delta);
            return;
        }
        if (PreviewPaused)
        {
            _stepping = _stepRequested;
            _stepRequested = false;
            GetTree().Paused = !_stepping;
        }
        if (!GetTree().Paused)
        {
            _elapsed += delta;
            SimulationFrames++;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Practicing || GetNode<PauseMenu>("/root/PauseMenu").IsOpen || LoadingScreen.IsActive
            || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.F10)
        {
            if (CurveEditor == null) OpenCurveEditor(); else CloseCurveEditor();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (CurveEditor != null)
        {
            if (key.Keycode == Key.F6) CurveEditor.TogglePlayback();
            else if (key.Keycode == Key.F5) CurveEditor.RestartPreview();
            else if (key.Keycode == Key.F7)
            {
                if (CurveEditor.Playing) CurveEditor.TogglePlayback();
                CurveEditor.AdvancePreview(1d / 60);
            }
            else return;
            GetViewport().SetInputAsHandled();
            return;
        }
        int action = key.Keycode switch { Key.F4 => 9, Key.F5 => 4, Key.F6 => 0, Key.F7 => 1, Key.F8 => 2, Key.F9 => 3, _ => -1 };
        if (action < 0) return;
        ActivateTool(action);
        GetViewport().SetInputAsHandled();
    }

    private void ResetPreviewClock()
    {
        if (_curveLayer != null) { _curveLayer.Free(); _curveLayer = null; CurveEditor = null; }
        PreviewPaused = _stepRequested = _stepping = false;
        SimulationFrames = 0;
        GetTree().Paused = GetNode<PauseMenu>("/root/PauseMenu").IsOpen;
        Engine.TimeScale = _previousTimeScale;
    }

    internal void ActivateTool(int action)
    {
        switch (action)
        {
            case 0:
                PreviewPaused = !PreviewPaused;
                _stepRequested = _stepping = false;
                GetTree().Paused = PreviewPaused;
                Engine.TimeScale = ClockRate;
                break;
            case 1:
                PreviewPaused = true;
                GetTree().Paused = true;
                Engine.TimeScale = 1;
                _stepRequested = true;
                break;
            case 2:
                _playback = (_playback + 1) % PlaybackRates.Length;
                Engine.TimeScale = ClockRate;
                break;
            case 3: _showCollision = !_showCollision; break;
            case 4: RestartPractice(); break;
            case 5:
                _tempo = (_tempo + 1) % AttackTempos.Length;
                RestartPractice();
                break;
            case 6:
                _repeatDelay = (_repeatDelay + 1) % RepeatDelays.Length;
                RestartPractice();
                break;
            case 7: GetNode<PauseMenu>("/root/PauseMenu").Open(); break;
            case 8: OpenCurveEditor(); break;
            case 9: ShowSelection(); break;
        }
    }

    private static Rect2 ToolRect(int action) => new(12, 402 + action * 27, Field.PanelW - 24, 24);

    private bool HandleToolClick()
    {
        if (!Pad.MouseClick()) return false;
        for (int i = 0; i < 10; i++)
        {
            if (!ToolRect(i).HasPoint(Pad.MousePos())) continue;
            ActivateTool(i);
            Pad.ConsumeUi(this);
            return true;
        }
        return false;
    }

    private void DrawTools()
    {
        _overlay.DrawRect(new Rect2(0, 310, Field.PanelW, 410), new Color("111820"));
        UiKit.Text(_overlay, UiKit.ZenBold, new Vector2(12, 315), "プラクティス", 18, Accent);
        UiKit.Multi(_overlay, UiKit.ZenBold, new Vector2(12, 345), _descriptions[_technique].Name, 15, UiKit.White, Field.PanelW - 24, 2);
        UiKit.Text(_overlay, UiKit.Mono, new Vector2(12, 387), $"{_repetitions}回目  {_elapsed:0.00}s  {(PreviewPaused ? "停止" : "再生")}", 14, UiKit.Text2);
        string[] labels = {
            PreviewPaused ? "F6  再生する" : "F6  一時停止",
            $"F7  1コマ（1/{Engine.PhysicsTicksPerSecond}秒）",
            $"F8  再生速度 ×{PlaybackRate:0.##}",
            $"F9  判定表示 {(_showCollision ? "ON" : "OFF")}",
            "F5  最初から再試行",
            $"攻撃テンポ ×{AttackTempo:0.##}",
            $"技の間隔 {RepeatDelay:0.0}秒",
            "Esc / M  メニュー", "F10  軌道素材を編集", "F4  技を選び直す"
        };
        for (int i = 0; i < labels.Length; i++)
        {
            var rect = ToolRect(i);
            bool highlighted = rect.HasPoint(Pad.MousePos()) || i == 0 && PreviewPaused || i == 3 && _showCollision;
            UiKit.Box(_overlay, rect, new Color("202933"), 5, highlighted ? Accent : new Color("35414e"), 1);
            UiKit.Text(_overlay, UiKit.ZenBold, rect.Position + new Vector2(8, 2), labels[i], 14, UiKit.White);
        }
        UiKit.Text(_overlay, UiKit.Zen, new Vector2(12, 680), "橙：実判定　水色：予告", 12, UiKit.Text2);
        UiKit.Text(_overlay, UiKit.Zen, new Vector2(12, 700), "速度・間隔は保存されません", 12, UiKit.Text2);
    }

    internal void OpenCurveEditor()
    {
        if (CurveEditor != null) return;
        _pausedBeforeCurve = PreviewPaused;
        PreviewPaused = true;
        _stepRequested = _stepping = false;
        GetTree().Paused = true;
        Engine.TimeScale = 1;
        _battle.Hide();
        var slot = _slots[_technique];
        CurveEditor = new BossPracticeCurveEditor { Character = _boss.BodyShotJob!.Value,
            TechniqueName = _descriptions[_technique].Name, DraftKey = $"{_bossIndex}_{_phase}_{slot.Kind}_{slot.Variant}" };
        _curveLayer = new CanvasLayer { Layer = 24 };
        AddChild(_curveLayer);
        _curveLayer.AddChild(CurveEditor);
        Pad.ReleaseMouseFollow();
    }

    internal void CloseCurveEditor()
    {
        if (CurveEditor == null) return;
        CurveEditor.Hide();
        _curveLayer!.QueueFree();
        _curveLayer = null;
        CurveEditor = null;
        _battle.Show();
        PreviewPaused = _pausedBeforeCurve;
        GetTree().Paused = PreviewPaused || GetNode<PauseMenu>("/root/PauseMenu").IsOpen;
        Engine.TimeScale = ClockRate;
        Pad.ReleaseMouseFollow();
    }

    private void DrawDiagnostics()
    {
        if (!Practicing || !_showCollision) return;
        foreach (var attack in GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>())
        foreach (var h in attack.PracticeHazards.OrderBy(h => h.Live))
        {
            var color = h.Live ? LiveOutline : WarningOutline;
            if (h.Shape == 2) _diagnostics.DrawRect(new Rect2(h.A, h.B - h.A), color, false, .6f);
            else if (h.Shape == 3)
            {
                _diagnostics.DrawRect(Field.Rect, color, false, .6f);
                _diagnostics.DrawArc(h.A, h.Radius, 0, Mathf.Tau, 96, color, .6f, true);
            }
            else
            {
                Vector2 normal = (h.B - h.A).Normalized().Orthogonal() * h.Radius;
                if (h.A != h.B)
                {
                    _diagnostics.DrawLine(h.A + normal, h.B + normal, color, .6f, true);
                    _diagnostics.DrawLine(h.A - normal, h.B - normal, color, .6f, true);
                    float angle = (h.B - h.A).Angle();
                    _diagnostics.DrawArc(h.A, h.Radius, angle + Mathf.Pi / 2, angle + Mathf.Pi * 1.5f, 32, color, .6f, true);
                    _diagnostics.DrawArc(h.B, h.Radius, angle - Mathf.Pi / 2, angle + Mathf.Pi / 2, 32, color, .6f, true);
                }
                else _diagnostics.DrawArc(h.A, h.Radius, 0, Mathf.Tau, 64, color, .6f, true);
            }
        }
        foreach (var bullet in GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>())
        {
            if (!bullet.Active || !bullet.IsVisibleInTree() || bullet.CollisionLayer == 0) continue;
            _diagnostics.DrawSetTransformMatrix(bullet.GlobalTransform);
            if (bullet.Footprint is { } footprint)
                foreach (var shape in footprint.Shapes) DrawDebugShape(shape, LiveOutline);
            else _diagnostics.DrawArc(Vector2.Zero, bullet.Radius, 0, Mathf.Tau, 32, LiveOutline, .5f, true);
        }
        if (_boss.BodyContactDamaging)
        foreach (uint owner in _boss.GetShapeOwners())
        {
            if (_boss.IsShapeOwnerDisabled(owner)) continue;
            _diagnostics.DrawSetTransformMatrix(_boss.GlobalTransform * _boss.ShapeOwnerGetTransform(owner));
            for (int i = 0; i < _boss.ShapeOwnerGetShapeCount(owner); i++) DrawDebugShape(_boss.ShapeOwnerGetShape(owner, i), LiveOutline);
        }
        _diagnostics.DrawSetTransformMatrix(Transform2D.Identity);
        _diagnostics.DrawArc(_player.GlobalPosition, _player.CurrentHitRadius, 0, Mathf.Tau, 32, Colors.Black, 1.4f, true);
        _diagnostics.DrawArc(_player.GlobalPosition, _player.CurrentHitRadius, 0, Mathf.Tau, 32, Colors.White, .6f, true);
    }

    private void DrawDebugShape(Shape2D shape, Color color)
    {
        if (shape is CircleShape2D circle) _diagnostics.DrawArc(Vector2.Zero, circle.Radius, 0, Mathf.Tau, 32, color, .5f, true);
        else if (shape is ConvexPolygonShape2D polygon)
        {
            var points = polygon.Points;
            _diagnostics.DrawPolyline(points, color, .5f, true);
            _diagnostics.DrawLine(points[^1], points[0], color, .5f, true);
        }
        else if (shape is RectangleShape2D rectangle) _diagnostics.DrawRect(new Rect2(-rectangle.Size / 2, rectangle.Size), color, false, .5f);
    }
}
