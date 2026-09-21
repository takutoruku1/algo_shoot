using Godot;

public partial class BossRealmFx : Node2D
{
    public BossPostStory Story = null!;
    public bool Revealed { get; private set; }
    public bool Revealing { get; private set; }
    public int Depth { get; private set; }
    private Texture2D _fake = null!;
    private Sprite2D[] _cracks = null!;
    private Vector2[] _vertices = null!;
    private int[] _triangles = null!;
    private Vector2 _origin = new(Field.CenterX, 104);
    private double _burstTime = 10, _revealTime;
    private int _burstDepth;
    private Color _oldTint;
    private CanvasModulate? _tint;
    private Color Ice => Story.Accent;
    private Color Dawn => Story.Dawn;

    public override void _Ready()
    {
        AddToGroup("boss_realm");
        ZIndex = -44;
        ZAsRelative = false;
        _fake = GD.Load<Texture2D>(Story.FakeBackground);
        _cracks = new Sprite2D[3];
        for (int i = 0; i < _cracks.Length; i++)
        {
            _cracks[i] = GlassFractureArt.Layer(i, Field.Rect, i == 1);
            AddChild(_cracks[i]);
        }
        (_vertices, _triangles) = GlassFractureArt.Mesh(Field.Rect, 4, 719);
        _tint = GetParent().GetNodeOrNull<CanvasModulate>("Tint");
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
    }

    public void BreakPost(int index, Vector2 origin)
    {
        _burstDepth = index;
        _burstTime = 0;
        _origin = origin;
        Depth = index + 1;
        if (index == 4)
        {
            Revealing = true;
            _revealTime = 0;
            _oldTint = _tint?.Color ?? Colors.White;
        }
    }

    public override void _Process(double delta)
    {
        if (Pad.UiBlocked(this) || (Hud.BubblePaused && !Revealing)) return;
        _burstTime += delta;
        if (Revealing)
        {
            double previous = _revealTime;
            _revealTime += delta;
            if (previous < 0.42 && _revealTime >= 0.42)
            {
                (GetTree().GetFirstNodeInGroup("stagebg") as StageBackground)?.CrossfadeLayersToBoss(
                    BgLayers.BossBehavior.Illustrated,
                    new[] { new BgLayers.Layer(Story.RealBackground, 0, -95, Colors.White, fitToField: true) }, 0.04f);
                foreach (string name in new[] { "ScrollFx", "Imagery", "WorldGrade", "MurkVignette", "JourneyBackground" })
                    GetParent().GetNodeOrNull<CanvasItem>(name)?.Hide();
            }
            if (_tint != null) _tint.Color = _oldTint.Lerp(Colors.White, Phase(_revealTime, 0.45, 3.6));
            if (_revealTime >= 3.6) { Revealed = true; Revealing = false; }
        }
        for (int i = 0; i < _cracks.Length; i++)
        {
            bool visible = Depth > i && !Revealed && (!Revealing || _revealTime < 0.42);
            float flash = 1 - Phase(_burstTime, 0.15, 1.2);
            _cracks[i].Modulate = new Color(Ice.Lerp(Dawn, Depth / 5f), visible ? 0.10f + Depth * 0.025f + flash * 0.35f : 0);
        }
        QueueRedraw();
    }

    private static float Phase(double time, double start, double end)
    {
        float p = Mathf.Clamp((float)((time - start) / (end - start)), 0, 1);
        return p * p * (3 - 2 * p);
    }

    private static Vector2 Clamp(Vector2 p) => new(Mathf.Clamp(p.X, Field.Left, Field.Right), Mathf.Clamp(p.Y, 0, 216));

    public override void _Draw()
    {
        if (Revealed) return;
        if (Revealing && _revealTime >= 0.42) DrawWorldShards();
        float burst = (float)_burstTime;
        float alpha = 1 - Phase(burst, 0.12, 1.5 + _burstDepth * 0.2);
        if (alpha <= 0) return;
        int count = 12 + _burstDepth * 10;
        Color color = Ice.Lerp(Dawn, _burstDepth / 4f);
        for (int i = 0; i < count; i++)
        {
            float angle = i * 2.399963f;
            var direction = Vector2.FromAngle(angle);
            float speed = 22 + i % 7 * 7 + _burstDepth * 12;
            var at = _origin + direction * (8 + burst * speed);
            var end = at + direction * (3 + _burstDepth * 2.5f);
            DrawLine(Clamp(at), Clamp(end), new Color(color, alpha * (i % 3 == 0 ? 0.9f : 0.35f)),
                i % 3 == 0 ? 0.55f : 0.25f, true);
        }
        if (burst < 0.35)
            DrawRect(Field.Rect, new Color(color, (1 - burst / 0.35f) * (0.035f + _burstDepth * 0.025f)));
    }

    private void DrawWorldShards()
    {
        GlassFractureArt.DrawShards(this, _fake, Field.Rect, _vertices, _triangles,
            (float)(_revealTime - 0.42) * 0.82f, 1.3f, new Color(0.68f, 0.77f, 0.9f));
    }
}
