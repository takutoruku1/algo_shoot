using Godot;

public partial class BossBattleFrame : Node2D
{
    private Enemy _boss = null!;
    private Texture2D[] _backdrop = null!;
    private Color _accent;
    private string _label = "";
    private float _time;
    private Node2D _rim = null!;

    internal static void Attach(Enemy boss)
        => boss.AddChild(new BossBattleFrame { Name = "BossBattleFrame", _boss = boss });

    public override void _Ready()
    {
        TopLevel = true;
        GlobalPosition = Vector2.Zero;
        ZIndex = 5;
        ZAsRelative = false;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        string character = _boss.BodyShotJob switch { Job.Melee => "akari", Job.Heal => "koharu", Job.Magic => "rei", _ => "mina" };
        _label = character.ToUpperInvariant();
        _accent = UnfolderMotion.ColorFor(_boss.BodyShotJob switch {
            Job.Melee => UnfolderKind.Akari, Job.Heal => UnfolderKind.Koharu,
            Job.Magic => UnfolderKind.Rei, _ => UnfolderKind.Mina,
        });
        _backdrop = new[] {
            GD.Load<Texture2D>($"res://char/bg2/boss/{character}_far_v2.png"),
            GD.Load<Texture2D>($"res://char/bg2/boss/{character}_mid_v2.png"),
            GD.Load<Texture2D>($"res://char/bg2/boss/{character}_near_v2.png"),
        };
        _rim = new Node2D { Name = "FrameRim", ZIndex = 9, ZAsRelative = false, UseParentMaterial = true };
        AddChild(_rim);
        _rim.Draw += DrawRim;
        Sync();
    }

    public override void _Process(double delta)
    {
        if (!Hud.BubblePaused) _time += (float)delta;
        Sync();
    }

    private void Sync()
    {
        Visible = Field.BossFrameActive && !_boss.BossInArena && !_boss.IsPurified;
        QueueRedraw();
        _rim.QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = Field.BossFrameRect;
        DrawRect(rect, new Color("101523"));
        for (int layer = 0; layer < _backdrop.Length; layer++)
        {
            var texture = _backdrop[layer];
            float width = texture.GetHeight() * rect.Size.X / rect.Size.Y;
            float focus = _boss.BodyShotJob switch { Job.Melee => .68f, Job.Heal => .22f, Job.Magic => .76f, _ => .55f };
            float x = (texture.GetWidth() - width) * focus + Mathf.Sin(_time * .14f) * layer * 4;
            DrawTextureRectRegion(texture, rect, new Rect2(x, 0, width, texture.GetHeight()), new Color(.74f, .78f, .85f));
        }
        UiKit.VGradient(this, rect, new[] { new Color("0c1225", .65f), new Color("161c32", .06f), new Color("0c1225", .7f) }, new[] { 0f, .48f, 1f });
        UiKit.HGradient(this, new Rect2(rect.Position, new Vector2(15, rect.Size.Y)), new Color("030910", .9f), new Color("030910", 0));
        UiKit.HGradient(this, new Rect2(rect.End.X - 12, 0, 12, rect.Size.Y), new Color("030910", 0), new Color("030910", .8f));
    }

    private void DrawRim()
    {
        var rect = new Rect2(Field.BossFrameRect.Position / UiKit.Scale, Field.BossFrameRect.Size / UiKit.Scale);
        float x = rect.Position.X, right = rect.End.X, bottom = rect.End.Y;
        UiKit.BeginDesign(_rim);
        // A transparent style box still fills its shadow, so shade only the backdrop below the portrait.
        var glass = new StyleBoxFlat
        {
            BgColor = Colors.Transparent,
            BorderColor = new Color("dce7ff", .19f),
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            AntiAliasing = true,
        };
        glass.SetBorderWidthAll(1);
        _rim.DrawStyleBox(glass, new Rect2(x + 8, 8, rect.Size.X - 16, bottom - 16));
        _rim.DrawLine(new Vector2(x + .5f, 0), new Vector2(x + .5f, bottom), new Color(_accent, .35f), 1, true);
        UiKit.VGradient(_rim, new Rect2(x + 8, 27, 1, bottom - 54),
            new[] { new Color(_accent.Lightened(.4f), .6f), new Color(_accent, .06f), new Color(_accent, .22f) }, new[] { 0f, .55f, 1f });

        var header = new Rect2(x + 16, 16, rect.Size.X - 32, 48);
        var plaque = new StyleBoxFlat
        {
            BgColor = new Color("182033", .83f), BorderColor = new Color("dce7ff", .12f),
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ShadowColor = new Color("030612", .25f), ShadowSize = 5, ShadowOffset = new Vector2(0, 3),
            AntiAliasing = true,
        };
        plaque.SetBorderWidthAll(1);
        _rim.DrawStyleBox(plaque, header);
        _rim.DrawCircle(new Vector2(x + 30, 40), 3, _accent.Lightened(.2f), antialiased: true);
        UiKit.Text(_rim, UiKit.Rajdhani, new Vector2(x + 43, 24), _label, 24, new Color("eef2ff"));
        UiKit.Text(_rim, UiKit.Rajdhani, new Vector2(right - 59, 31), _boss is CameoBoss ? "MID" : "BOSS", 12,
            new Color("b3bdd4"), HorizontalAlignment.Right, 28);
        UiKit.Box(_rim, new Rect2(rect.GetCenter().X - 18, bottom - 16, 36, 2), new Color(_accent, .32f), 1);
        UiKit.EndDesign(_rim);
    }
}
