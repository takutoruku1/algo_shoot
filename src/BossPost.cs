using Godot;
using System;

public partial class BossPost : Node2D
{
    public static readonly string[] Labels = { "公開したポスト", "1つ前の下書き", "2つ前の下書き", "3つ前の下書き", "最初の下書き" };
    public BossPostStory Story = null!;
    public Enemy Boss = null!;
    public Action<int, Vector2> Broken = null!;
    public Action Completed = null!;
    public int Index;
    public const double BreakDuration = 0.75;
    public bool IsBroken { get; private set; }
    private float _damage = -1, _alpha;
    private double _time, _hitTime, _breakTime;
    private Texture2D _face = null!, _plateArt = null!;
    private SubViewport _plateView = null!;
    private Node2D _plateCanvas = null!;
    private Sprite2D[] _cracks = null!;
    private Vector2[] _vertices = null!;
    private int[] _triangles = null!;
    private bool _completed;
    private static readonly Rect2 Card = new(-260, -160, 520, 320);
    private Color InkColor => Story.Accent.Lerp(Story.Dawn, Index / 4f);

    public override void _Ready()
    {
        AddToGroup("boss_post");
        // Keep the damage illustration behind bodies, bullets, and attack warnings.
        ZIndex = -20;
        ZAsRelative = false;
        Scale = Vector2.One * 0.72f;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        _face = GD.Load<Texture2D>(CompanionDialogue.AccountIcon(Story.Id));
        _plateArt = GD.Load<Texture2D>(GlassFractureArt.Folder + (Index == 0 ? "post_glass_v1.png" : "draft_glass_v1.png"));
        _plateView = new SubViewport { Name = "PostPlate", Size = new Vector2I(1040, 640), TransparentBg = true,
            Disable3D = true, World2D = new World2D(), RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        AddChild(_plateView);
        _plateCanvas = new Node2D { Position = new Vector2(520, 320), Scale = Vector2.One * 2 };
        _plateView.AddChild(_plateCanvas);
        _plateCanvas.Draw += DrawCard;
        _cracks = new Sprite2D[3];
        for (int i = 0; i < _cracks.Length; i++)
        {
            _cracks[i] = GlassFractureArt.Layer((Index + i) % 3, Card, Index % 2 != 0, i == 2);
            _plateCanvas.AddChild(_cracks[i]);
        }
        (_vertices, _triangles) = GlassFractureArt.Mesh(Card, Index, (ulong)(4381 + Index * 113));
        SetDamageProgress(0);
        FollowBoss();
    }

    public void SetDamageProgress(float progress)
    {
        if (IsBroken) return;
        progress = Mathf.Clamp(progress, 0, 1);
        if (Mathf.IsEqualApprox(progress, _damage)) return;
        if (progress > _damage) _hitTime = 0.18;
        _damage = progress;
        for (int i = 0; i < _cracks.Length; i++)
            _cracks[i].Modulate = new Color(InkColor,
                Mathf.Clamp((_damage - i * 0.28f) / 0.25f, 0, 1) * (0.5f + Index * 0.055f));
        _plateCanvas.QueueRedraw();
        _plateView.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        QueueRedraw();
    }

    public void Shatter()
    {
        if (IsBroken) return;
        SetDamageProgress(1);
        IsBroken = true;
        _alpha = Mathf.Max(_alpha, 0.8f);
        _plateView.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        Broken(Index, GlobalPosition);
        Audio.Instance?.PlayAkariPostBreak(Index);
        GameCamera.Instance?.Shake(0.65f + Index * 0.12f, 0.10f);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.ShowBossLine(Story.Name, Story.Replies[Index], InkColor, 2.2);
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Hud.BubblePaused || Pad.UiBlocked(this)) return;
        _time += delta;
        _hitTime = Math.Max(0, _hitTime - delta);
        if (IsBroken)
        {
            _breakTime += delta;
            if (_breakTime >= BreakDuration && !_completed)
            {
                _completed = true;
                Completed();
                QueueFree();
                return;
            }
        }
        else
        {
            FollowBoss();
            float target = Boss.GaugeVulnerable || _hitTime > 0 ? 0.82f : 0.28f;
            _alpha = Mathf.MoveToward(_alpha, target, (float)delta * 3);
        }
        QueueRedraw();
    }

    private void FollowBoss()
    {
        var at = Boss.GlobalPosition + new Vector2(0, -38);
        GlobalPosition = new Vector2(Mathf.Clamp(at.X, Field.Left + 60, Field.Right - 60),
            Mathf.Clamp(at.Y, Field.Top + 40, Field.Bottom - 40));
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        if (!IsBroken)
        {
            float enter = Mathf.Clamp((float)(_time / 0.18), 0, 1);
            DrawTextureRect(_plateView.GetTexture(), Card, false, new Color(1, 1, 1, _alpha * enter));
            if (_hitTime > 0)
                UiKit.Box(this, Card, null, 4, new Color(InkColor, (float)(_hitTime / 0.18) * 0.65f), 2);
        }
        else
        {
            float t = (float)(_breakTime / BreakDuration);
            GlassFractureArt.DrawShards(this, _plateView.GetTexture(), Card, _vertices, _triangles,
                t * 1.6f, 0.85f + Index * 0.12f, new Color(1, 1, 1, _alpha * (1 - t)));
        }
        UiKit.EndDesign(this);
    }

    private void DrawCard()
    {
        var ci = _plateCanvas;
        ci.DrawTextureRect(_plateArt, Card, false);
        UiKit.FaceAvatar(ci, new Vector2(-218, -119), 22, _face, InkColor, false, topCrop: 0f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(-180, -146), Story.Name, 23, Colors.White);
        UiKit.Text(ci, UiKit.Zen, new Vector2(-180, -116), Story.Handle, 17, InkColor);
        float labelW = UiKit.TextW(UiKit.ZenBold, Labels[Index], 18);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(234 - labelW, -136), Labels[Index], 18, InkColor);
        UiKit.Multi(ci, UiKit.Zen, new Vector2(-232, -57), Story.Posts[Index], Index == 4 ? 23 : 25, Colors.White, 466);
        UiKit.Text(ci, UiKit.Zen, new Vector2(-232, 112), Index == 0 ? "返信 0    いいね 1" : "未送信", 16, new Color(InkColor, 0.8f));
        for (int i = 0; i < 5; i++)
            ci.DrawLine(new Vector2(109 + i * 26, 126), new Vector2(126 + i * 26, 126), new Color(InkColor, i <= Index ? 1 : 0.18f), 2, true);
    }
}
