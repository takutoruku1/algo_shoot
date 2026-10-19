using Godot;

public partial class ShieldBreakCallout : Node2D
{
    public Hud Hud = null!;
    private Enemy? _owner;
    private Texture2D? _portrait;
    private string _speaker = "", _line = "", _breakLine = "";
    private Color _accent;
    private float _age;
    public bool Active => IsInstanceValid(_owner) && (_owner.GaugeBreaking || _owner.GaugeBodyExposed)
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
        if (GameManager.BossRushActive) return;
        _owner = owner;
        _age = 0;
        _speaker = Jobs.Get(job).CharacterName;
        _accent = CompanionDialogue.Accent(job);
        (_breakLine, _line) = job switch
        {
            Job.Melee => ("シールド、壊れた！", "本体を狙おう！"),
            Job.Heal => ("シールドが壊れたよ！", "今なら届くよ。本体を狙おう！"),
            Job.Magic => ("シールドは壊れたわ。", "今よ。本体を狙って！"),
            _ => ("シールドを破壊しました。", "ご主人様、本体を狙いましょう！"),
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
        float enter = 1f - Mathf.Pow(1f - Mathf.Clamp((_age + 0.04f) / 0.10f, 0, 1), 3);
        float alpha = enter * Mathf.Clamp(_owner!.GaugeWindowLeft / 0.09f, 0, 1);
        var box = DialogueBox.Board;
        box.Position += new Vector2(-(1 - enter) * 22, 0);
        DialogueBox.DrawFrame(this, box, _speaker, _accent, _portrait, alpha: alpha);
        var pages = DialogueBox.Paginate(_owner.GaugeBodyExposed ? _line : _breakLine, DialogueBox.WrapWidth(box));
        DialogueBox.DrawBody(this, box, pages[0], pages[0].Length, alpha);
        var start = new Vector2(box.Position.X + DialogueBox.Padding, box.End.Y - 15);
        float width = DialogueBox.WrapWidth(box);
        DrawLine(start, start + new Vector2(width, 0), new Color(_accent, 0.18f * alpha), 2, true);
        DrawLine(start, start + new Vector2(width * _owner!.GaugeWindowLeft, 0),
            new Color(_accent, 0.75f * alpha), 2, true);
        UiKit.EndDesign(this);
    }
}
