using Godot;

public partial class LunaticUnlock : CanvasLayer
{
    private Node2D _canvas = null!;
    private Node? _world;
    private ProcessModeEnum _worldMode;
    private double _time;
    private bool _sounded;

    public static bool PlayIfPending(Node owner)
    {
        if (owner.GetNodeOrNull<LunaticUnlock>("LunaticUnlock") != null) return true;
        if (GameManager.Instance?.LunaticUnlockPending != true) return false;
        owner.AddChild(new LunaticUnlock { Name = "LunaticUnlock", Layer = 95 });
        return true;
    }

    public override void _Ready()
    {
        Pad.ConsumeUi(this);
        _world = GetParent().GetNodeOrNull<Node>("World");
        if (_world != null)
        {
            _worldMode = _world.ProcessMode;
            _world.ProcessMode = ProcessModeEnum.Disabled;
        }
        _canvas = new Node2D();
        _canvas.Draw += DrawReveal;
        AddChild(_canvas);
    }

    public override void _Process(double delta)
    {
        Pad.ConsumeUi(this);
        _time += delta;
        if (!_sounded && _time >= .7) { _sounded = true; Audio.Instance?.PlayUiBuy(); }
        if (_time >= 4.6)
        {
            var game = GameManager.Instance!;
            game.MarkIdleDialogSeen(GameManager.LunaticUnlockSeenKey);
            game.SaveToSlot(0);
            QueueFree();
        }
        _canvas.QueueRedraw();
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_world)) _world!.ProcessMode = _worldMode;
    }

    private void DrawReveal()
    {
        float fade = Mathf.Clamp((float)_time / .45f, 0, 1) * Mathf.Clamp((4.6f - (float)_time) / .45f, 0, 1);
        float open = Mathf.SmoothStep(0, 1, Mathf.Clamp(((float)_time - .35f) / .65f, 0, 1));
        float reveal = Mathf.Clamp(((float)_time - .7f) / .5f, 0, 1);
        var accent = new Color("cdb8ff");
        UiKit.BeginDesign(_canvas);
        _canvas.DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color(.035f, .025f, .08f, .88f * fade));
        UiKit.Box(_canvas, new Rect2(300, 194, 680, 332), new Color(.07f, .06f, .13f, .96f * fade), 24,
            new Color(accent, .5f * fade), 1.5f);
        var center = new Vector2(640, 270);
        _canvas.DrawArc(center, 38 + open * 9, -.8f + open, Mathf.Pi + .8f + open, 64,
            new Color(accent, fade * (1 - open * .55f)), 2, true);
        _canvas.DrawArc(center + new Vector2(open * 10, -7 - open * 13), 13, Mathf.Pi, Mathf.Tau - open * .8f, 32,
            new Color(accent, fade), 2.5f, true);
        UiKit.Box(_canvas, new Rect2(center + new Vector2(-18, -6), new Vector2(36, 28)),
            new Color(accent, .12f * fade), 6, new Color(accent, fade), 2);
        _canvas.DrawCircle(center + new Vector2(0, 5), 2.5f, new Color(accent, fade));
        UiKit.Text(_canvas, UiKit.Mono, new Vector2(300, 328), "LUNATIC", 48, new Color(UiKit.White, reveal * fade),
            HorizontalAlignment.Center, 680);
        UiKit.Text(_canvas, UiKit.ZenBold, new Vector2(300, 395), "新しい難易度を解放", 24,
            new Color(accent, reveal * fade), HorizontalAlignment.Center, 680);
        UiKit.Text(_canvas, UiKit.Zen, new Vector2(300, 455), "MINA CLEAR  /  難易度選択から挑戦できます", 17,
            new Color(UiKit.Text2, reveal * fade), HorizontalAlignment.Center, 680);
        UiKit.EndDesign(_canvas);
    }
}
