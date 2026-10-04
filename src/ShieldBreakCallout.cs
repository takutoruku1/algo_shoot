using Godot;

public partial class ShieldBreakCallout : Node2D
{
    public Hud Hud = null!;
    private Enemy? _owner;
    private Texture2D? _portrait;
    private string _speaker = "", _line = "";
    private Color _accent;
    private float _age;
    private Job _job;
    private bool _showingPost;
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
        _job = job;
        UpdateLine();
        _portrait = GD.Load<Texture2D>(job switch
        {
            Job.Melee => "res://char/v3/akari_face.png",
            Job.Heal => "res://char/v3/koharu_face_lit.png",
            Job.Magic => "res://char/v3/rei_gawa.png",
            _ => "res://char/v3/mina_conversation_v1.png",
        });
        QueueRedraw();
    }

    private void UpdateLine()
    {
        string previous = _line;
        _showingPost = GetTree().GetFirstNodeInGroup("boss_post") is BossPost post && post.Boss == _owner;
        _line = _showingPost ? _job switch
        {
            Job.Melee => "……隠してた言葉が、見えてきたね。\nもう少し、奥へ進もう。",
            Job.Heal => "……隠していた言葉が、見えてきたよ。\n一緒に、もう少し奥へ進もう。",
            Job.Magic => "……隠していた言葉が、見えてきたわ。\nもう少し、奥へ進みましょう。",
            _ => "……隠していた言葉が、見えてきました。\nご主人様、もう少し奥へ進みましょう。",
        } : _job switch
        {
            Job.Melee => "シールド、壊れた！\n今がチャンス！　本体を狙おう！",
            Job.Heal => "シールドが壊れたよ！\n今なら届くよ。一緒に、本体を狙おう！",
            Job.Magic => "シールドは壊れたわ。\n今がチャンスよ。本体を狙って！",
            _ => "シールドを破壊しました。\nご主人様、今こそ本体を狙いましょう！",
        };
        if (_line != previous) _age = 0;
    }

    public override void _Process(double delta)
    {
        if (!Active) _owner = null;
        else
        {
            _age += (float)delta;
            UpdateLine();
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Active || _portrait == null) return;
        UiKit.BeginDesign(this);
        float enter = 1f - Mathf.Pow(1f - Mathf.Clamp((_age + 0.04f) / 0.10f, 0, 1), 3);
        float alpha = enter * (_showingPost ? 1 : Mathf.Clamp(_owner!.GaugeWindowLeft / 0.09f, 0, 1));
        var box = DialogueBox.Board;
        box.Position += new Vector2(-(1 - enter) * 22, 0);
        DialogueBox.DrawFrame(this, box, _speaker, _accent, _portrait, alpha: alpha);
        UiKit.Text(this, UiKit.Zen, new Vector2(box.End.X - 244, box.Position.Y + 17),
            _showingPost ? "下書きの奥へ" : "本体にダメージが通る", 15,
            new Color(DialogueBox.Ink, alpha), HorizontalAlignment.Right, 220);
        var pages = DialogueBox.Paginate(_line, DialogueBox.WrapWidth(box));
        DialogueBox.DrawBody(this, box, pages[0], pages[0].Length, alpha);
        var start = new Vector2(box.Position.X + DialogueBox.Padding, box.End.Y - 15);
        float width = DialogueBox.WrapWidth(box);
        DrawLine(start, start + new Vector2(width, 0), new Color(_accent, 0.18f * alpha), 2, true);
        DrawLine(start, start + new Vector2(width * (_showingPost ? 1 : _owner!.GaugeWindowLeft), 0),
            new Color(_accent, 0.75f * alpha), 2, true);
        UiKit.EndDesign(this);
    }
}
