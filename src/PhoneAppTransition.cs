using Godot;

public partial class PhoneAppTransition : CanvasLayer
{
    public const float FocusDuration = 0.62f, ExpandDuration = 0.48f;
    public const float Duration = FocusDuration + ExpandDuration;
    private static readonly Vector2 ScreenSize = new(UiKit.DesignW, UiKit.DesignH);
    private ImageTexture _snapshot = null!;
    private Rect2 _sourceRect;
    private TransitionCanvas _canvas = null!;
    private float _time;
    public bool LoadingVisible => _time >= FocusDuration;
    public bool Expanded => _time >= Duration;
    public Rect2 LoadingBounds => AppRect(_time);

    public static void Open(Node from, string destination, Rect2 sourceRect)
    {
        if (LoadingScreen.IsActive) return;
        using var image = from.GetViewport().GetTexture().GetImage();
        var transition = new PhoneAppTransition
        {
            Name = "PhoneAppTransition", Layer = 120, ProcessPriority = -100,
            ProcessMode = ProcessModeEnum.Always,
            _snapshot = ImageTexture.CreateFromImage(image),
            _sourceRect = sourceRect,
        };
        from.GetTree().Root.AddChild(transition);
        LoadingScreen.Open(from, destination, transition);
    }

    public override void _Ready()
    {
        _canvas = new TransitionCanvas { Transition = this, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        AddChild(_canvas);
    }

    private static float Ease(float value) => Mathf.SmoothStep(0, 1, Mathf.Clamp(value, 0, 1));

    private Rect2 AppRect(float time)
    {
        Vector2 portrait = _sourceRect.Size * (ScreenSize.Y / _sourceRect.Size.Y);
        float focus = Ease(time / FocusDuration);
        Vector2 size = _sourceRect.Size.Lerp(portrait, focus).Lerp(ScreenSize, Ease((time - FocusDuration) / ExpandDuration));
        return new Rect2(_sourceRect.GetCenter().Lerp(ScreenSize / 2, focus) - size / 2, size);
    }

    public override void _Process(double delta)
    {
        _time = Mathf.Min(Duration, _time + (float)(delta / Engine.TimeScale));
        _canvas.QueueRedraw();
    }

    public override void _ExitTree()
    {
        _snapshot.Dispose();
    }

    private void DrawBackdrop(Node2D canvas, Rect2 rect, float dim)
    {
        if (!rect.HasArea()) return;
        canvas.DrawTextureRectRegion(_snapshot, rect,
            new Rect2(rect.Position / ScreenSize * _snapshot.GetSize(), rect.Size / ScreenSize * _snapshot.GetSize()));
        canvas.DrawRect(rect, new Color(0.02f, 0.025f, 0.035f, dim));
        Rect2 oldPhone = rect.Intersection(_sourceRect);
        if (oldPhone.HasArea()) canvas.DrawRect(oldPhone, new Color("070b11"));
    }

    private void DrawTransition(Node2D canvas)
    {
        UiKit.BeginDesign(canvas);
        float dim = Ease(_time / 0.25f) * 0.68f;
        DrawBackdrop(canvas, new Rect2(Vector2.Zero, ScreenSize), dim);
        Rect2 rect = AppRect(_time);
        canvas.DrawRect(rect, new Color("10191e"));
        canvas.DrawTextureRectRegion(_snapshot, AppRect(Mathf.Min(_time, FocusDuration)),
            new Rect2(_sourceRect.Position / ScreenSize * _snapshot.GetSize(), _sourceRect.Size / ScreenSize * _snapshot.GetSize()));
        float edge = 1 - Ease((_time - FocusDuration) / ExpandDuration);
        UiKit.Box(canvas, rect, null, 8 * edge, new Color(0.7f, 0.82f, 0.87f, edge * 0.8f), 1.5f);
        UiKit.EndDesign(canvas);
    }

    private partial class TransitionCanvas : Node2D
    {
        public PhoneAppTransition Transition = null!;
        public override void _Draw() => Transition.DrawTransition(this);
    }
}
