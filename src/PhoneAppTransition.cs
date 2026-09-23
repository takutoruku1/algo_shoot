using Godot;

public partial class PhoneAppTransition : CanvasLayer
{
    public const float TurnDuration = 0.62f, ExpandDuration = 0.48f;
    public const float Duration = TurnDuration + ExpandDuration;
    private static readonly Vector2 ScreenSize = new(UiKit.DesignW, UiKit.DesignH);
    private static readonly Rect2 PhoneRect = new(400, 0, 480, 720);
    private ImageTexture _snapshot = null!;
    private string _destination = "";
    private TransitionCanvas _canvas = null!;
    private Node2D? _app;
    private float _time;
    private bool _changing;

    public static void Open(Node from, string destination)
    {
        using var image = from.GetViewport().GetTexture().GetImage();
        var transition = new PhoneAppTransition
        {
            Name = "PhoneAppTransition", Layer = 120, ProcessPriority = -100,
            _snapshot = ImageTexture.CreateFromImage(image), _destination = destination,
        };
        from.GetTree().Root.AddChild(transition);
        Pad.ConsumeUi(transition);
    }

    public override void _Ready()
    {
        _canvas = new TransitionCanvas { Transition = this, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        AddChild(_canvas);
    }

    private static float Ease(float value) => Mathf.SmoothStep(0, 1, Mathf.Clamp(value, 0, 1));

    private static (float Angle, float Scale) PhonePose(float time)
    {
        float turn = Ease((time - 0.06f) / (TurnDuration - 0.06f));
        float angle = turn * Mathf.Pi / 2;
        float height = PhoneRect.Size.X * Mathf.Sin(angle) + PhoneRect.Size.Y * Mathf.Cos(angle);
        float scale = Mathf.Min(1, ScreenSize.Y / height) * (1 - Mathf.Sin(turn * Mathf.Pi) * 0.1f);
        return (angle, scale);
    }

    private static Rect2 AppRect(float time)
    {
        Vector2 size = new Vector2(720, 480).Lerp(ScreenSize, Ease((time - TurnDuration) / ExpandDuration));
        return new Rect2((ScreenSize - size) / 2, size);
    }

    public override void _Process(double delta)
    {
        Pad.ConsumeUi(this);
        if (!_changing || _app != null) _time += (float)delta;
        if (_time >= TurnDuration && !_changing)
        {
            _time = TurnDuration;
            _changing = true;
            ChangeApp();
        }
        if (_app != null)
        {
            ApplyAppTransform();
            if (_time >= Duration)
            {
                _app.Position = Vector2.Zero;
                _app.Scale = Vector2.One;
                QueueFree();
            }
        }
        _canvas.QueueRedraw();
    }

    private async void ChangeApp()
    {
        var tree = GetTree();
        tree.ChangeSceneToFile(_destination);
        await ToSignal(tree, SceneTree.SignalName.SceneChanged);
        _app = (Node2D)tree.CurrentScene;
        ApplyAppTransform();
    }

    private void ApplyAppTransform()
    {
        Rect2 rect = AppRect(_time);
        _app!.Position = rect.Position * UiKit.Scale;
        _app.Scale = rect.Size / ScreenSize;
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_app))
        {
            _app!.Position = Vector2.Zero;
            _app.Scale = Vector2.One;
        }
        _snapshot.Dispose();
    }

    private void DrawBackdrop(Node2D canvas, Rect2 rect, float dim)
    {
        if (!rect.HasArea()) return;
        canvas.DrawTextureRectRegion(_snapshot, rect,
            new Rect2(rect.Position / ScreenSize * _snapshot.GetSize(), rect.Size / ScreenSize * _snapshot.GetSize()));
        canvas.DrawRect(rect, new Color(0.02f, 0.025f, 0.035f, dim));
        Rect2 oldPhone = rect.Intersection(PhoneRect);
        if (oldPhone.HasArea()) canvas.DrawRect(oldPhone, new Color("14171d"));
    }

    private void DrawTransition(Node2D canvas)
    {
        UiKit.BeginDesign(canvas);
        float dim = Ease(_time / 0.25f) * 0.68f;
        if (_time <= TurnDuration)
        {
            DrawBackdrop(canvas, new Rect2(Vector2.Zero, ScreenSize), dim);
            var pose = PhonePose(_time);
            canvas.DrawSetTransform(ScreenSize / 2 * UiKit.Scale, pose.Angle, Vector2.One * (pose.Scale * UiKit.Scale));
            UiKit.Box(canvas, new Rect2(-PhoneRect.Size / 2, PhoneRect.Size).Grow(2), new Color("34414a"), 8);
            canvas.DrawTextureRectRegion(_snapshot, new Rect2(-PhoneRect.Size / 2, PhoneRect.Size),
                new Rect2(PhoneRect.Position / ScreenSize * _snapshot.GetSize(), PhoneRect.Size / ScreenSize * _snapshot.GetSize()));
        }
        else
        {
            Rect2 rect = AppRect(_time);
            DrawBackdrop(canvas, new Rect2(0, 0, ScreenSize.X, rect.Position.Y), dim);
            DrawBackdrop(canvas, new Rect2(0, rect.End.Y, ScreenSize.X, ScreenSize.Y - rect.End.Y), dim);
            DrawBackdrop(canvas, new Rect2(0, rect.Position.Y, rect.Position.X, rect.Size.Y), dim);
            DrawBackdrop(canvas, new Rect2(rect.End.X, rect.Position.Y, ScreenSize.X - rect.End.X, rect.Size.Y), dim);
            float cover = 1 - Ease((_time - TurnDuration) / 0.2f);
            canvas.DrawSetTransform(ScreenSize / 2 * UiKit.Scale, Mathf.Pi / 2, Vector2.One * UiKit.Scale);
            Vector2 turnedSize = new(rect.Size.Y, rect.Size.X);
            canvas.DrawTextureRectRegion(_snapshot, new Rect2(-turnedSize / 2, turnedSize),
                new Rect2(PhoneRect.Position / ScreenSize * _snapshot.GetSize(), PhoneRect.Size / ScreenSize * _snapshot.GetSize()),
                new Color(1, 1, 1, cover));
            UiKit.BeginDesign(canvas);
            float edge = 1 - Ease((_time - TurnDuration) / ExpandDuration);
            UiKit.Box(canvas, rect, null, 8 * edge, new Color(0.7f, 0.82f, 0.87f, edge * 0.8f), 1.5f);
        }
        UiKit.EndDesign(canvas);
    }

    private partial class TransitionCanvas : Node2D
    {
        public PhoneAppTransition Transition = null!;
        public override void _Draw() => Transition.DrawTransition(this);
    }
}
