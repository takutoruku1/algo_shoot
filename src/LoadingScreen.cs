using Godot;
using System.Threading.Tasks;

public partial class LoadingScreen : CanvasLayer
{
    private enum Phase { Cover, Loading, Diving, Switching, Reveal, Failed }
    private static LoadingScreen? _active;
    public static bool IsActive => IsInstanceValid(_active);
    private static readonly Rect2 FullScreen = new(0, 0, 1280, 720);
    private static readonly Rect2 RetryRect = new(416, 476, 208, 46), TitleRect = new(656, 476, 208, 46);
    private readonly Godot.Collections.Array _progress = new();
    private string _destination = "";
    private PhoneAppTransition? _phone;
    private LoadingCanvas _canvas = null!;
    private FontFile _logo = null!, _text = null!;
    private Texture2D _character = null!, _backdrop = null!;
    private readonly Texture2D[] _spin = new Texture2D[5];
    private static readonly Color Ink = new("dce4e3"), Muted = new("94a7a6"), Rose = new("bd8396"), Mint = new("86b8b1");
    private static readonly Color Background = new("0c1113"), Surface = new("182326");
    private Job _job;
    private SceneTree _tree = null!;
    private Phase _phase;
    private double _time, _visibleTime, _revealTime, _diveTime;
    private float _alpha, _loaded, _displayProgress;
    private bool _wasPaused, _finished, _acceptHeld, _backHeld, _navHeld;
    private int _errorSelection;

    public static void Open(Node from, string destination, PhoneAppTransition? phone = null)
    {
        if (IsActive) return;
        _active = new LoadingScreen
        {
            Name = "LoadingScreen", Layer = 256, ProcessPriority = -1000,
            ProcessMode = ProcessModeEnum.Always, _destination = destination, _phone = phone,
        };
        from.GetTree().Root.AddChild(_active);
    }

    public override void _Ready()
    {
        _tree = GetTree();
        _wasPaused = _tree.Paused;
        _tree.Paused = true;
        Pad.ConsumeUi(this);
        _logo = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-Italic.ttf");
        _text = GD.Load<FontFile>("res://assets/fonts/ShipporiMincho-SemiBold.ttf");
        var game = GameManager.Instance;
        _job = game?.SelectedJob ?? Job.Tank;
        var costume = game?.CostumeFor(_job) ?? Cosmetics.DefaultCostume(_job);
        _character = GD.Load<Texture2D>(costume.PosePath("idle"));
        _backdrop = GD.Load<Texture2D>("res://char/bg2/prologue/timeline_depth_v2.png");
        if (IsDive)
            for (int i = 0; i < _spin.Length; i++) _spin[i] = GD.Load<Texture2D>(costume.PosePath($"spin_{i:00}"));
        _canvas = new LoadingCanvas { Screen = this, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        AddChild(_canvas);
    }

    public override void _Process(double delta)
    {
        Pad.ConsumeUi(this);
        double dt = delta / Engine.TimeScale;
        _time += dt;
        bool visible = !IsInstanceValid(_phone) || _phone!.LoadingVisible;
        if (visible) _visibleTime += dt;
        _displayProgress = Mathf.MoveToward(_displayProgress, _loaded, (float)dt * 2.5f);
        _canvas.QueueRedraw();
        switch (_phase)
        {
            case Phase.Cover:
                _alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp((float)(_visibleTime / 0.18), 0, 1));
                if (_alpha >= 1) StartLoading();
                break;
            case Phase.Loading:
                var status = ResourceLoader.LoadThreadedGetStatus(_destination, _progress);
                if (_progress.Count > 0) _loaded = Mathf.Clamp((float)_progress[0], 0, 1);
                if (status is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource)
                    Fail($"Resource loading failed: {_destination}");
                else if (status == ResourceLoader.ThreadLoadStatus.Loaded && _visibleTime >= (IsDive ? 0.9 : 1.15)
                    && (!IsInstanceValid(_phone) || _phone!.Expanded))
                {
                    if (IsDive) { _phase = Phase.Diving; _diveTime = 0; }
                    else ChangeScene();
                }
                break;
            case Phase.Diving:
                _diveTime += dt;
                if (_diveTime >= 1.15) ChangeScene();
                break;
            case Phase.Reveal:
                _revealTime += dt;
                _alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp((float)(_revealTime / 0.24), 0, 1));
                if (_alpha <= 0)
                {
                    _finished = true;
                    _tree.Paused = false;
                    QueueFree();
                }
                break;
            case Phase.Failed:
                ProcessFailure();
                break;
        }
        _canvas.Modulate = new Color(1, 1, 1, _alpha);
    }

    private async void StartLoading()
    {
        _phase = Phase.Switching;
        // The opaque screen must reach the renderer before any resource or scene initialization.
        await RenderedFrame();
        Error error = ResourceLoader.LoadThreadedRequest(_destination, "PackedScene");
        if (error != Error.Ok) { Fail($"Cannot request {_destination}: {error}"); return; }
        _phase = Phase.Loading;
    }

    private async void ChangeScene()
    {
        _phase = Phase.Switching;
        _loaded = 1;
        await RenderedFrame();
        var scene = (PackedScene)ResourceLoader.LoadThreadedGet(_destination);
        Error error = _tree.ChangeSceneToPacked(scene);
        if (error != Error.Ok) { Fail($"Cannot open {_destination}: {error}"); return; }
        await ToSignal(_tree, SceneTree.SignalName.SceneChanged);
        _tree.Paused = true;
        if (IsInstanceValid(_phone)) _phone!.QueueFree();
        _phone = null;
        // Do not advance gameplay or dialogue underneath the loading screen.
        await RenderedFrame();
        _phase = Phase.Reveal;
    }

    private async Task RenderedFrame()
    {
        if (DisplayServer.GetName() == "headless") await ToSignal(_tree, SceneTree.SignalName.ProcessFrame);
        else await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private void Fail(string message)
    {
        GD.PushError(message);
        _phase = Phase.Failed;
        _diveTime = 0;
        _alpha = 1;
        _acceptHeld = _backHeld = _navHeld = true;
    }

    private void ProcessFailure()
    {
        bool accept = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool nav = Input.IsActionPressed("ui_left") || Input.IsActionPressed("ui_right");
        if (nav && !_navHeld) _errorSelection = 1 - _errorSelection;
        bool retry = accept && !_acceptHeld && _errorSelection == 0;
        bool title = back && !_backHeld || accept && !_acceptHeld && _errorSelection == 1;
        Rect2 bounds = Bounds;
        Vector2 mouse = (Pad.MousePos() - bounds.Position) / bounds.Size * FullScreen.Size;
        if (Pad.MouseClick()) { retry |= RetryRect.HasPoint(mouse); title |= TitleRect.HasPoint(mouse); }
        _acceptHeld = accept; _backHeld = back; _navHeld = nav;
        if (!retry && !title) return;
        if (title) _destination = "res://TitleMenu.tscn";
        _loaded = 0;
        _displayProgress = 0;
        _diveTime = 0;
        _visibleTime = 0;
        StartLoading();
    }

    public override void _ExitTree()
    {
        if (!_finished) _tree.Paused = _wasPaused;
        if (IsInstanceValid(_phone)) _phone!.QueueFree();
        if (_active == this) _active = null;
        _progress.Dispose();
    }

    private Rect2 Bounds => IsInstanceValid(_phone) ? _phone!.LoadingBounds : FullScreen;
    private bool IsDive => _destination.GetFile().GetBaseName() is "Akari" or "Koharu" or "Rei" or "MinaBattle"
        or "Stage0" or "Training" or "Main";

    private string DestinationName => _destination.GetFile().GetBaseName() switch
    {
        "TitleMenu" => "タイトル", "Hub" => "ホーム", "Prologue" => "プロローグ",
        "Shop" => "強化ショップ", "Customize" => "カスタマイズ", "Records" => "記録",
        "Settings" => "設定", "Credits" => "クレジット", "Training" => "トレーニング",
        "DiffSelect" => "難易度選択", "Stage0" => "チュートリアル",
        "Akari" => "あかりのタイムライン", "Koharu" => "こはるのタイムライン",
        "Rei" => "レイのタイムライン", "MinaBattle" => "ミナのタイムライン", "Main" => "タイムライン",
        "Final" => "最後の対話", "Epilogue" => "エピローグ", _ => "次の画面",
    };

    private void DrawScreen(Node2D canvas)
    {
        if (_alpha <= 0) return;
        SetDesignTransform(canvas);
        float chrome = 1 - Mathf.SmoothStep(0.54f, 0.72f, (float)_diveTime);
        canvas.DrawRect(FullScreen, Background);
        Vector2 drift = new(Mathf.Sin((float)_time * 0.22f) * 10, Mathf.Sin((float)_time * 0.3f) * 5);
        canvas.DrawTextureRect(_backdrop, new Rect2(new Vector2(-64, -36) + drift, new Vector2(1408, 792)),
            false, new Color(0.6f, 0.7f, 0.68f, 0.38f));
        UiKit.Text(canvas, _logo, new Vector2(64, 36), "Refrain", 62, new Color(Ink, chrome));
        UiKit.Text(canvas, _text, new Vector2(730, 63), DestinationName, 22, new Color(Muted, chrome), HorizontalAlignment.Right, 486);
        if (_phase == Phase.Failed)
        {
            Character(canvas, _character, new Vector2(640, 310), 186, -0.08f);
            UiKit.Text(canvas, _text, new Vector2(0, 398), "読み込めませんでした", 28, Ink, HorizontalAlignment.Center, 1280);
            for (int i = 0; i < 2; i++)
            {
                Rect2 button = i == 0 ? RetryRect : TitleRect;
                UiKit.Box(canvas, button, i == _errorSelection ? Surface.Lightened(0.07f) : Surface, 4,
                    i == _errorSelection ? Mint : Muted.Darkened(0.5f), 1);
                UiKit.Text(canvas, UiKit.ZenBold, button.Position + new Vector2(0, 9), i == 0 ? "再試行" : "タイトルへ",
                    18, Ink, HorizontalAlignment.Center, button.Size.X);
            }
        }
        else
        {
            if (IsDive) DrawDive(canvas);
            else DrawCompanion(canvas);
            string status = IsDive ? (_diveTime > 0 ? "タイムラインへ潜行中" : "ダイブ準備中") : "読み込み中";
            UiKit.Text(canvas, _text, new Vector2(0, IsDive ? 132 : 160), status, 30, new Color(Ink, chrome), HorizontalAlignment.Center, 1280);
            Progress(canvas, chrome);
        }
        UiKit.EndDesign(canvas);
    }

    private void DrawCompanion(Node2D canvas)
    {
        float t = (float)_visibleTime;
        for (int i = 0; i < 3; i++)
        {
            Vector2 center = new(i == 1 ? 794 : 478 + i * 18, 297 + i * 63 + Mathf.Sin(t * 1.6f + i) * 9);
            Envelope(canvas, center, 0.6f + i * 0.08f, -0.1f + Mathf.Sin(t * 0.7f + i) * 0.08f,
                new Color(i == 1 ? Rose : Mint, 0.5f + Mathf.Sin(t * 1.5f + i) * 0.12f));
        }
        Character(canvas, _character, new Vector2(640 + Mathf.Sin(t * 1.1f) * 3, 362 + Mathf.Sin(t * 2) * 7),
            230, Mathf.Sin(t * 1.6f) * 0.018f);
        SetDesignTransform(canvas);
    }

    private void DrawDive(Node2D canvas)
    {
        float dive = Mathf.Clamp((float)_diveTime / 0.72f, 0, 1);
        float rush = Mathf.SmoothStep(0, 1, Mathf.Clamp(((float)_diveTime - 0.68f) / 0.47f, 0, 1));
        Vector2 portal = new(692, 340);
        float zoom = 1 + rush * 8;
        SetDesignTransform(canvas, portal, 0, zoom);
        UiKit.Box(canvas, new Rect2(-102, -152, 204, 304), new Color("28383a"), 8);
        UiKit.Box(canvas, new Rect2(-96, -150, 192, 300), Background, 8, Mint.Darkened(0.35f), 1.5f);
        canvas.DrawRect(new Rect2(-84, -130, 168, 256), Background);
        Vector2 sourceSize = new(_backdrop.GetHeight() * 168f / 256, _backdrop.GetHeight());
        canvas.DrawTextureRectRegion(_backdrop, new Rect2(-84, -130, 168, 256),
            new Rect2((_backdrop.GetSize() - sourceSize) / 2, sourceSize), new Color(0.45f, 0.6f, 0.57f));
        for (int i = 0; i < 5; i++)
        {
            float p = Mathf.PosMod(i * 0.2f + (float)_visibleTime * 0.24f + dive * 0.35f, 1);
            float size = 0.15f + p * 0.85f;
            Rect2 post = new(-65 * size, -68 + p * 160, 130 * size, 48 * size);
            UiKit.Box(canvas, post, new Color(Surface, p * 0.9f), 3, new Color(Mint, p * 0.2f), size);
            canvas.DrawCircle(post.Position + new Vector2(12, 13) * size, 5 * size, new Color(i % 2 == 0 ? Mint : Rose, p * 0.7f));
            canvas.DrawLine(post.Position + new Vector2(25, 13) * size, post.Position + new Vector2(107, 13) * size,
                new Color(Muted, p * 0.6f), 2 * size, true);
            canvas.DrawLine(post.Position + new Vector2(10, 31) * size, post.Position + new Vector2(84, 31) * size,
                new Color(Muted, p * 0.3f), 2 * size, true);
        }
        UiKit.Box(canvas, new Rect2(-23, -143, 46, 5), Muted.Darkened(0.6f), 2);
        UiKit.Box(canvas, new Rect2(-24, 138, 48, 4), Mint.Darkened(0.45f), 2);
        SetDesignTransform(canvas);
        if (dive < 1)
        {
            for (int i = 0; i < 4; i++)
            {
                float p = Mathf.PosMod((float)_visibleTime * 0.65f + i * 0.25f, 1);
                Vector2 pos = new Vector2(386, 400).Lerp(portal, p) - new Vector2(0, Mathf.Sin(p * Mathf.Pi) * 100);
                Envelope(canvas, pos + new Vector2(30, 60), 0.8f * (1 - p) + 0.15f, -p * 0.5f,
                    new Color(i % 2 == 0 ? Rose : Mint, 0.55f));
            }
            Vector2 center = new Vector2(426, 371).Lerp(portal, dive) - new Vector2(0, Mathf.Sin(dive * Mathf.Pi) * 100);
            if (dive == 0) center.Y += Mathf.Sin((float)_visibleTime * 2) * 5;
            Texture2D pose = dive == 0 ? _character : _spin[Mathf.Min(4, (int)(dive * 5))];
            Character(canvas, pose, center, 206 * (1 - dive * 0.92f), dive * 1.25f, 1 - Mathf.SmoothStep(0.8f, 1, dive));
        }
        SetDesignTransform(canvas);
    }

    private void Progress(Node2D canvas, float alpha)
    {
        if (alpha <= 0) return;
        Rect2 bar = new(402, 552, 476, 6);
        UiKit.Box(canvas, bar, new Color(Surface.Lightened(0.04f), alpha), 2);
        float width = bar.Size.X * _displayProgress;
        if (width > 0) UiKit.Box(canvas, new Rect2(bar.Position, new Vector2(width, bar.Size.Y)), new Color(Mint, alpha * 0.7f), 2);
        for (int i = 0; i < 3; i++)
        {
            float x = Mathf.PosMod((float)_time * 105 - i * 13, bar.Size.X);
            if (x + 7 < width) canvas.DrawRect(new Rect2(bar.Position + new Vector2(x, 0), new Vector2(7, 6)),
                new Color(Ink, alpha * (0.7f - i * 0.2f)));
        }
        UiKit.Text(canvas, UiKit.Mono, new Vector2(571, 589), IsDive ? "DIVING" : "LOADING", 14, new Color(Muted, alpha));
        for (int i = 0; i < 3; i++)
        {
            float pulse = 0.3f + 0.7f * Mathf.Max(0, Mathf.Sin((float)_time * 5 - i * 0.9f));
            canvas.DrawCircle(new Vector2(654 + i * 14, 599), 2.5f, new Color(Mint, alpha * pulse));
        }
    }

    private void Character(Node2D canvas, Texture2D texture, Vector2 center, float height, float angle, float alpha = 1)
    {
        SetDesignTransform(canvas, center, angle);
        Rect2 content = UiKit.ContentRect(texture);
        float scale = height / content.Size.Y;
        canvas.DrawTextureRect(texture, new Rect2(-(content.Position + content.Size / 2) * scale, texture.GetSize() * scale),
            false, new Color(0.8f, 0.85f, 0.85f, alpha));
        SetDesignTransform(canvas);
    }

    private void Envelope(Node2D canvas, Vector2 center, float scale, float angle, Color color)
    {
        SetDesignTransform(canvas, center, angle, scale);
        UiKit.Box(canvas, new Rect2(-22, -15, 44, 30), new Color(Surface, color.A), 2, color, 1.5f);
        canvas.DrawPolyline(new[] { new Vector2(-21, -13), new(0, 2), new(21, -13) }, color, 1.5f, true);
        SetDesignTransform(canvas);
    }

    private void SetDesignTransform(Node2D canvas, Vector2 center = default, float angle = 0, float zoom = 1)
    {
        Rect2 bounds = Bounds;
        Vector2 scale = bounds.Size / FullScreen.Size * UiKit.Scale;
        canvas.DrawSetTransformMatrix(new Transform2D(new Vector2(scale.X, 0), new Vector2(0, scale.Y), bounds.Position * UiKit.Scale)
            * new Transform2D(angle, Vector2.One * zoom, 0, center));
    }

    private partial class LoadingCanvas : Node2D
    {
        public LoadingScreen Screen = null!;
        public override void _Draw() => Screen.DrawScreen(this);
    }
}
