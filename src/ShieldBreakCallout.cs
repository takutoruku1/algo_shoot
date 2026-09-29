using Godot;

public partial class ShieldBreakCallout : Node2D
{
    public Hud Hud = null!;
    private Enemy? _owner;
    private Texture2D? _portrait;
    private string _speaker = "", _line = "";
    private Color _accent;
    private float _age;
    public bool Active => IsInstanceValid(_owner) && _owner.GaugeVulnerable
        && !Hud.BubblePaused && !Hud.CinematicMode && !Hud.SuppressCallouts;

    public override void _Ready()
    {
        // Attack telegraphs (-10), word bullets (-12), and AOE (-2) must stay in front.
        ZIndex = -15;
        ZAsRelative = false;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
    }

    public void Show(Enemy owner, Job job)
    {
        _owner = owner;
        _age = 0;
        _speaker = Jobs.Get(job).CharacterName;
        _accent = CompanionDialogue.Accent(job);
        _line = job switch
        {
            Job.Melee => "シールド、壊れた！\n今がチャンス！　本体を狙おう！",
            Job.Heal => "シールドが壊れたよ！\n今なら届くよ。一緒に、本体を狙おう！",
            Job.Magic => "シールドは壊れたわ。\n今がチャンスよ。本体を狙って！",
            _ => "シールドを破壊しました。\nご主人様、今こそ本体を狙いましょう！",
        };
        _portrait = GD.Load<Texture2D>(job switch
        {
            Job.Melee => "res://char/v3/akari_face.png",
            Job.Heal => "res://char/v3/koharu_face_lit.png",
            Job.Magic => "res://char/v3/rei_gawa.png",
            _ => "res://char/v3/mina_conversation_v1.png",
        });
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Active) _owner = null;
        else _age += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Active || _portrait == null) return;
        UiKit.BeginDesign(this);
        float enter = 1f - Mathf.Pow(1f - Mathf.Clamp(_age / 0.24f, 0, 1), 3);
        float alpha = enter * Mathf.Clamp(_owner!.GaugeWindowLeft / 0.09f, 0, 1);
        float x = Field.DLeft + 24 - (1 - enter) * 22;
        const float bottom = 622, portraitH = 242, portraitW = 204;
        var content = UiKit.ContentRect(_portrait);
        float scale = Mathf.Min(portraitW / content.Size.X, portraitH / content.Size.Y);
        var size = (Vector2)content.Size * scale;
        var portraitRect = new Rect2(x + (portraitW - size.X) / 2, bottom - size.Y, size.X, size.Y);
        const float textOffset = 224, textW = 540;
        float tx = x + textOffset, top = bottom - 144;
        DrawColoredPolygon(new[] {
            new Vector2(x + 14, top + 8), new Vector2(tx + textW + 22, top + 8),
            new Vector2(tx + textW + 8, bottom), new Vector2(x, bottom),
        }, new Color("10171e", 0.78f * alpha));
        DrawTextureRectRegion(_portrait, portraitRect, content, new Color(1, 1, 1, 0.88f * alpha));
        DrawLine(new Vector2(tx, top + 7), new Vector2(tx + textW, top + 7), new Color(_accent, alpha), 2, true);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, top + 17), _speaker, 17, new Color(_accent, alpha));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(tx + textW - 196, top + 17), "本体にダメージが通る", 17,
            new Color("d7f4ee", alpha), HorizontalAlignment.Right, 196);
        var lines = UiKit.WrapLines(UiKit.ZenBold, _line, 24, textW);
        for (int i = 0; i < lines.Count; i++)
            UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, top + 49 + i * 33), lines[i], 24, new Color("f5f8fa", alpha));
        float barY = bottom - 12;
        DrawLine(new Vector2(tx, barY), new Vector2(tx + textW, barY), new Color(_accent, 0.2f * alpha), 2, true);
        DrawLine(new Vector2(tx, barY), new Vector2(tx + textW * _owner.GaugeWindowLeft, barY),
            new Color(_accent, 0.85f * alpha), 2, true);
        UiKit.EndDesign(this);
    }
}
