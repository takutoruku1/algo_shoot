using Godot;
using System;
using System.Collections.Generic;

public partial class BossPracticeCurveEditor : Node2D
{
    public Job Character { get; init; }
    public string DraftKey { get; init; } = "";
    public string TechniqueName { get; init; } = "";
    internal Curve2D Curve { get; private set; } = null!;
    internal bool Playing { get; private set; }
    internal double Elapsed { get; private set; }
    internal string DraftPath => Startup.DataPath($"attack_curves/{DraftKey}.tres");
    private readonly Stack<Curve2D> _undo = new();
    private AttackLineLayer _lines = null!;
    private AttackPresentation _effects = null!;
    private Texture2D _art = null!;
    private Vector2[] _points = Array.Empty<Vector2>();
    private Vector2 _projectile;
    private bool _beam = true, _live, _launched;
    private int _selected = 1, _drag = -1;
    private float _radius = 4, _speed = 90;
    private string _status = "ダブルクリックで点を追加";
    private static readonly Color Ink = new("bfa3ee");

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ZIndex = 100;
        _lines = new AttackLineLayer { ZIndex = 1 };
        AddChild(_lines);
        _effects = new AttackPresentation { Character = Character, PreviewSpeed = 0, Depth = 101 };
        AddChild(_effects);
        _art = BulletArt.BossPattern(Character, 1).Cropped;
        Curve = new Curve2D { BakeInterval = 1 };
        Curve.AddPoint(new Vector2(Field.Left + 38, 55), Vector2.Zero, new Vector2(42, 0));
        Curve.AddPoint(new Vector2(Field.CenterX, 115), new Vector2(-42, -28), new Vector2(42, 28));
        Curve.AddPoint(new Vector2(Field.Right - 35, 168), new Vector2(-42, 0));
        Rebuild();
    }

    internal void TogglePlayback()
    {
        Playing = !Playing;
        _effects.PreviewSpeed = Playing ? 1 : 0;
    }

    internal void RestartPreview()
    {
        Elapsed = 0;
        _live = _launched = false;
        _effects.Reset();
        _lines.Begin(); _lines.End();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        bool menu = GetNode<PauseMenu>("/root/PauseMenu").IsOpen || LoadingScreen.IsActive;
        if (menu) _drag = -1;
        _effects.PreviewSpeed = Playing && !menu ? 1 : 0;
        if (Playing && !menu) AdvancePreview(delta);
        QueueRedraw();
    }

    internal void AdvancePreview(double delta)
    {
        Elapsed += delta;
        double duration = _beam ? .85 : Curve.GetBakedLength() / _speed;
        _effects.Advance(delta, Curve.GetPointPosition(0), Elapsed < 2.6 ? (float)Math.Min(1, Elapsed / .6) : -1);
        _live = Elapsed >= 2.6 && Elapsed < 2.6 + duration;
        _lines.Begin(Elapsed);
        if (_live)
        {
            if (!_launched)
            {
                _effects.Launch(Curve.GetPointPosition(0), Curve.GetPointOut(0));
                _launched = true;
            }
            if (_beam) _lines.Add(_points, _radius, Ink);
            _projectile = Curve.SampleBaked((float)(Elapsed - 2.6) * _speed, true);
        }
        else if (_launched)
        {
            _effects.Dissolve(_beam ? Curve.SampleBaked(Curve.GetBakedLength() * .5f) : Curve.GetPointPosition(Curve.PointCount - 1));
            _launched = false;
        }
        _lines.End();
        if (Elapsed >= 3.25 + duration) RestartPreview();
    }

    private void Remember()
    {
        _undo.Push((Curve2D)Curve.Duplicate());
        Playing = false;
        _effects.PreviewSpeed = 0;
        RestartPreview();
    }

    private void Rebuild()
    {
        _points = Curve.TessellateEvenLength(6, 2);
        RestartPreview();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GetNode<PauseMenu>("/root/PauseMenu").IsOpen || LoadingScreen.IsActive) return;
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.CtrlPressed && key.Keycode == Key.S) SaveDraft();
            else if (key.CtrlPressed && key.Keycode == Key.O) LoadDraft();
            else if (key.CtrlPressed && key.Keycode == Key.Z) Undo();
            else if (key.Keycode == Key.Delete && Curve.PointCount > 2)
            { Remember(); Curve.RemovePoint(_selected); _selected = Math.Min(_selected, Curve.PointCount - 1); Rebuild(); }
            else return;
            GetViewport().SetInputAsHandled();
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            if (!click.Pressed) { _drag = -1; return; }
            Vector2 point = click.Position;
            if (!Field.Rect.HasPoint(point)) return;
            if (click.DoubleClick && Curve.PointCount < 12)
            {
                Remember();
                int insert = 1;
                float best = float.MaxValue;
                for (int i = 0; i < Curve.PointCount - 1; i++)
                for (int sample = 0; sample < 20; sample++)
                {
                    float distance = point.DistanceSquaredTo(Curve.Sample(i, sample / 19f));
                    if (distance < best) { best = distance; insert = i + 1; }
                }
                Curve.AddPoint(point, new Vector2(-20, 0), new Vector2(20, 0), insert);
                _selected = insert;
                Rebuild();
            }
            else
            {
                Vector2 anchor = Curve.GetPointPosition(_selected);
                _drag = point.DistanceTo(anchor + Curve.GetPointIn(_selected)) < 4 ? 1
                    : point.DistanceTo(anchor + Curve.GetPointOut(_selected)) < 4 ? 2 : -1;
                for (int i = 0; i < Curve.PointCount; i++)
                    if (point.DistanceTo(Curve.GetPointPosition(i)) < 5) { _selected = i; _drag = 0; break; }
                if (_drag >= 0) Remember();
            }
            GetViewport().SetInputAsHandled();
        }
        if (@event is InputEventMouseMotion motion && _drag >= 0)
        {
            Vector2 point = motion.Position.Clamp(Field.Rect.Position + Vector2.One * 10, Field.Rect.End - Vector2.One * 10);
            if (_drag == 0) Curve.SetPointPosition(_selected, point);
            else if (_drag == 1) Curve.SetPointIn(_selected, (point - Curve.GetPointPosition(_selected)).LimitLength(100));
            else Curve.SetPointOut(_selected, (point - Curve.GetPointPosition(_selected)).LimitLength(100));
            Rebuild();
            GetViewport().SetInputAsHandled();
        }
    }

    internal void SaveDraft()
    {
        Curve.SetMeta("preview_radius", _radius);
        Curve.SetMeta("preview_speed", _speed);
        Curve.SetMeta("preview_beam", _beam);
        Error error = DirAccess.MakeDirRecursiveAbsolute(Startup.DataPath("attack_curves"));
        if (error == Error.Ok) error = ResourceSaver.Save(Curve, DraftPath);
        _status = error == Error.Ok ? "この技の軌道素材を保存しました" : $"保存できませんでした：{error}";
    }

    internal void LoadDraft()
    {
        if (!ResourceLoader.Exists(DraftPath)) { _status = "この技の保存済み素材はありません"; return; }
        var saved = ResourceLoader.Load<Curve2D>(DraftPath, "", ResourceLoader.CacheMode.Ignore);
        if (saved == null || saved.PointCount < 2 || saved.PointCount > 12) { _status = "読み込めない軌道素材です"; return; }
        for (int i = 0; i < saved.PointCount; i++)
            if (!saved.GetPointPosition(i).IsFinite() || !Field.Rect.HasPoint(saved.GetPointPosition(i))
                || !saved.GetPointIn(i).IsFinite() || !saved.GetPointOut(i).IsFinite()
                || saved.GetPointIn(i).Length() > 100 || saved.GetPointOut(i).Length() > 100)
            { _status = "軌道の点が編集範囲を超えています"; return; }
        float radius = (float)saved.GetMeta("preview_radius", 4f), speed = (float)saved.GetMeta("preview_speed", 90f);
        if (!float.IsFinite(radius) || radius < 2 || radius > 10 || !float.IsFinite(speed) || speed < 45 || speed > 180)
        { _status = "太さ・速さが編集範囲を超えています"; return; }
        Remember();
        Curve = saved;
        _radius = radius;
        _speed = speed;
        _beam = (bool)Curve.GetMeta("preview_beam", true);
        _selected = Math.Min(_selected, Curve.PointCount - 1);
        Rebuild();
        _status = "保存した軌道素材を読み込みました";
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        Playing = false;
        Curve = _undo.Pop();
        _selected = Math.Min(_selected, Curve.PointCount - 1);
        Rebuild();
    }

    private static Rect2 ButtonRect(int index) => new(12, 255 + index * 39, Field.PanelW - 24, 34);
    internal bool HandleClick()
    {
        if (!Pad.MouseClick()) return false;
        for (int i = 0; i < 10; i++)
        {
            if (!ButtonRect(i).HasPoint(Pad.MousePos())) continue;
            switch (i)
            {
                case 0: TogglePlayback(); break;
                case 1: _beam = !_beam; RestartPreview(); break;
                case 2: _radius = _radius >= 10 ? 2 : _radius + 2; RestartPreview(); break;
                case 3: _speed = _speed >= 180 ? 45 : _speed + 45; RestartPreview(); break;
                case 4:
                    if (Playing) TogglePlayback();
                    AdvancePreview(1d / 60);
                    break;
                case 5: SaveDraft(); break;
                case 6: LoadDraft(); break;
                case 7: Undo(); break;
                case 8: RestartPreview(); break;
                case 9: BossPracticeRoot.Active!.CloseCurveEditor(); break;
            }
            Pad.ConsumeUi(BossPracticeRoot.Active!);
            return true;
        }
        return false;
    }

    internal void DrawTools(Control canvas)
    {
        canvas.DrawRect(new Rect2(0, 0, Field.PanelW, 720), new Color("111820"));
        UiKit.Text(canvas, UiKit.ZenBold, new Vector2(12, 18), "軌道素材の試作", 22, UiKit.Info);
        UiKit.Multi(canvas, UiKit.ZenBold, new Vector2(12, 58), TechniqueName, 15, UiKit.White, Field.PanelW - 24, 2);
        UiKit.Multi(canvas, UiKit.Zen, new Vector2(12, 114), "丸：通過点\n四角：曲がり方\nドラッグで移動\nダブルクリック：点を追加\nDelete：選択点を削除", 14, UiKit.Text2, Field.PanelW - 24, 5);
        UiKit.Text(canvas, UiKit.Mono, new Vector2(12, 226), $"{Elapsed:0.00}s   点 {Curve.PointCount} / 12", 13, UiKit.Text2);
        string[] labels = { Playing ? "F6  試射を止める" : "F6  試射する", _beam ? "試射：曲がる線" : "試射：移動する弾",
            $"太さ：{_radius * 2:0}", $"弾の速さ：{_speed:0}", "F7  1コマ進める", "Ctrl+S  素材を保存", "Ctrl+O  保存素材を読む",
            "Ctrl+Z  編集を戻す", "F5  試射を最初から", "F10  プラクティスへ" };
        for (int i = 0; i < labels.Length; i++)
        {
            Rect2 bounds = ButtonRect(i);
            UiKit.Box(canvas, bounds, new Color("202933"), 4, bounds.HasPoint(Pad.MousePos()) ? UiKit.Info : new Color("35414e"), 1);
            UiKit.Text(canvas, UiKit.ZenBold, bounds.Position + new Vector2(7, 8), labels[i], 14, UiKit.White);
        }
        UiKit.Multi(canvas, UiKit.Zen, new Vector2(12, 655), _status, 13, UiKit.Info, Field.PanelW - 24, 3);
        UiKit.Text(canvas, UiKit.Zen, new Vector2(12, 704), "本番への適用は技ごとに行います", 11, UiKit.Text2);
    }

    public override void _Draw()
    {
        DrawRect(Field.Rect, new Color("131a27"));
        for (float x = Field.Left; x < Field.Right; x += 20) DrawLine(new Vector2(x, 0), new Vector2(x, Field.Bottom), new Color("243043"), .35f);
        for (float y = 0; y < Field.Bottom; y += 20) DrawLine(new Vector2(Field.Left, y), new Vector2(Field.Right, y), new Color("243043"), .35f);
        if (!_live || !_beam)
            for (int i = 1; i < _points.Length; i++)
            {
                DrawLine(_points[i - 1], _points[i], new Color(Ink, .1f), _radius * 2, true);
                if (i % 4 < 2) DrawLine(_points[i - 1], _points[i], new Color("ffad96"), .7f, true);
            }
        if (_live && !_beam)
        {
            Vector2 size = _art.GetSize(); size *= _radius * 2 / Math.Max(size.X, size.Y);
            DrawTextureRect(_art, new Rect2(_projectile - size * .5f, size), false);
            DrawArc(_projectile, _radius, 0, Mathf.Tau, 32, Colors.White, .5f, true);
        }
        if (!Playing)
        {
            Vector2 at = Curve.GetPointPosition(_selected);
            foreach (Vector2 handle in new[] { at + Curve.GetPointIn(_selected), at + Curve.GetPointOut(_selected) })
            {
                DrawLine(at, handle, new Color("79c4db"), .6f, true);
                DrawRect(new Rect2(handle - Vector2.One * 2, Vector2.One * 4), new Color("79c4db"));
            }
            for (int i = 0; i < Curve.PointCount; i++)
            {
                DrawCircle(Curve.GetPointPosition(i), 3, new Color("172030"));
                DrawArc(Curve.GetPointPosition(i), 3, 0, Mathf.Tau, 24, i == _selected ? Colors.White : Ink, .8f, true);
            }
        }
    }
}
