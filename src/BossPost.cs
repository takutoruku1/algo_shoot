using Godot;
using System;

public partial class BossPost : Area2D
{
    public static readonly string[] Labels = { "公開したポスト", "1つ前の下書き", "2つ前の下書き", "3つ前の下書き", "最初の下書き" };
    public BossPostStory Story = null!;
    public Enemy Boss = null!;
    public Action<int, Vector2> Broken = null!;
    public int Index;
    public Action Completed = null!;
    private const int Strength = 24;
    private const double ReadHold = 3.2;
    private int _ink = Strength;
    private double _time, _hitTime, _breakTime;
    private bool _broken;
    private CollisionShape2D _shape = null!;
    private Texture2D _face = null!, _plateArt = null!;
    private SubViewport _plateView = null!;
    private Node2D _plateCanvas = null!;
    private Sprite2D[] _cracks = null!;
    private Vector2[] _vertices = null!;
    private int[] _triangles = null!;
    private FontFile _revealFont = null!;
    private Control _revealMask = null!;
    private Node2D _revealCanvas = null!;
    private static readonly string[] RevealTitles =
    {
        "消した言葉へ", "言えなかった想いへ", "隠していた本音へ", "はじまりの下書きへ", "本当の景色へ",
    };
    private static readonly Rect2 Card = new(-260, -160, 520, 320);
    private Color InkColor => Story.Accent.Lerp(Story.Dawn, Index / 4f);
    public double BreakDuration => Index == 4 ? 4.0 : 2.15 + Index * 0.23;
    public double MinimumReadTime => ReadHold + Index * 0.3;
    private bool _completed;

    public override void _Ready()
    {
        AddToGroup("boss_post");
        ZIndex = 8;
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        CollisionLayer = 16;
        CollisionMask = 2;
        _shape = new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(156, 96) } };
        AddChild(_shape);
        _face = GD.Load<Texture2D>(CompanionDialogue.AccountIcon(Story.Id));
        _plateArt = GD.Load<Texture2D>(GlassFractureArt.Folder + (Index == 0 ? "post_glass_v1.png" : "draft_glass_v1.png"));
        _plateView = new SubViewport { Name = "PostPlate", Size = new Vector2I(1040, 640), TransparentBg = true,
            Disable3D = true, World2D = new World2D(), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
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
        _revealFont = (FontFile)GD.Load<FontFile>("res://assets/fonts/ShipporiMincho-SemiBold.ttf").Duplicate();
        _revealFont.Oversampling = 2;
        _revealFont.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        _revealMask = new Control
        {
            Position = new Vector2(-260, -120) * UiKit.Scale, Scale = Vector2.One * UiKit.Scale,
            Size = new Vector2(0, 240), ClipContents = true, Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_revealMask);
        _revealCanvas = new Node2D
        {
            Position = new Vector2(260, 120), Material = Material,
            TextureFilter = TextureFilterEnum.Linear,
        };
        _revealMask.AddChild(_revealCanvas);
        _revealCanvas.Draw += DrawReveal;
        AreaEntered += Hit;
        Audio.Instance?.PlaySpell();
    }

    private void Hit(Area2D area)
    {
        if (_broken || Hud.BubblePaused || area is not Bullet { IsEnemy: false, Active: true } bullet) return;
        if (!bullet.RegisterChargeHit(this)) return;
        bullet.ChargeImpact(bullet.GlobalPosition);
        int damage = bullet.Damage;
        bullet.TryChain(Boss);
        if (bullet.Pierce > 0) bullet.Pierce--;
        else GetNode<BulletPool>("/root/Pool").Despawn(bullet);
        (GetTree().GetFirstNodeInGroup("player") as Player)?.NotifyShotHit(Boss);
        Damage(Mathf.Clamp(damage, 1, bullet.Charged ? 12 : 4));
    }

    public void BombHit()
    {
        if (!_broken && !Hud.BubblePaused) Damage(8);
    }

    private void Damage(int amount)
    {
        _ink = Mathf.Max(0, _ink - amount);
        if (_hitTime <= 0) Audio.Instance?.PlayStrip();
        _hitTime = 0.09;
        UpdatePlate();
        QueueRedraw();
    }

    private void UpdatePlate()
    {
        float damage = 1 - _ink / (float)Strength;
        for (int i = 0; i < _cracks.Length; i++)
        {
            float alpha = Mathf.Clamp((damage - i * 0.28f) / 0.25f, 0, 1);
            _cracks[i].Modulate = new Color(InkColor, alpha * (0.38f + Index * 0.055f));
        }
        _plateCanvas.QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Hud.BubblePaused || Pad.UiBlocked(this)) return;
        _time += delta;
        _hitTime = Math.Max(0, _hitTime - delta);
        if (!_broken && _ink == 0 && _time >= MinimumReadTime)
        {
            _broken = true;
            _plateView.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            SetDeferred(PropertyName.Monitoring, false);
            _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
            GetNode<BulletPool>("/root/Pool").DespawnPlayerBullets();
            Broken(Index, GlobalPosition);
            Audio.Instance?.PlayAkariPostBreak(Index);
            GameCamera.Instance?.Shake(1.0f + Index * 0.7f, 0.12f + Index * 0.035f);
            (GetTree().GetFirstNodeInGroup("hud") as Hud)?.ShowBossLine(Story.Name, Story.Replies[Index], InkColor, BreakDuration - 0.1);
            QueueRedraw();
            return;
        }
        if (_broken)
        {
            _breakTime += delta;
            UpdateReveal();
            if (_breakTime >= BreakDuration && !_completed)
            {
                _completed = true;
                Completed();
                QueueFree();
                return;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float enter = Mathf.Clamp((float)(_time / 0.4), 0, 1);
        UiKit.BeginDesign(this);
        if (!_broken)
        {
            for (int layer = 4 - Index; layer > 0; layer--)
                DrawTextureRect(_plateArt, new Rect2(-260 + layer * 5, -160 - layer * 7, 520 - layer * 10, 320),
                    false, new Color(0.6f, 0.67f, 0.7f, enter));
            UiKit.Text(this, UiKit.Mono, new Vector2(-256, -209), Index == 0 ? "POSTED" : "DRAFT HISTORY", 14, new Color(InkColor, 0.8f));
            UiKit.Text(this, UiKit.Mono, new Vector2(185, -209), $"0{Index + 1} / 05", 14, new Color(InkColor, 0.8f));
            DrawTextureRect(_plateView.GetTexture(), Card, false, new Color(1, 1, 1, enter));
            if (_hitTime > 0) UiKit.Box(this, Card, null, 4, new Color(1, 1, 1, 0.6f), 2);
            if (GetTree().GetFirstNodeInGroup("player") is Player player && player.LockTarget == Boss)
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    {
                        var corner = new Vector2(x * 268, y * 168);
                        DrawLine(corner, corner + new Vector2(-x * 18, 0), UiKit.Purify, 3);
                        DrawLine(corner, corner + new Vector2(0, -y * 18), UiKit.Purify, 3);
                    }
        }
        else
        {
            float t = (float)_breakTime;
            GlassFractureArt.DrawShards(this, _plateView.GetTexture(), Card, _vertices, _triangles,
                t, 0.85f + Index * 0.23f, Colors.White);
        }
        UiKit.EndDesign(this);
    }

    private static float RevealPhase(double time, double start, double duration)
        => Mathf.SmoothStep(0, 1, Mathf.Clamp((float)((time - start) / duration), 0, 1));

    private void UpdateReveal()
    {
        float wipe = RevealPhase(_breakTime, 0.38, Index == 4 ? 0.9 : 0.62);
        float fade = 1 - RevealPhase(_breakTime, BreakDuration - 0.42, 0.42);
        _revealMask.Visible = wipe > 0 && fade > 0;
        _revealMask.Size = new Vector2(520 * wipe, 240);
        _revealCanvas.Modulate = new Color(1, 1, 1, fade);
        _revealCanvas.QueueRedraw();
    }

    private void DrawReveal()
    {
        float t = (float)_breakTime;
        float settle = RevealPhase(t, 0.38, Index == 4 ? 1.1 : 0.75);
        float lift = 10 * (1 - settle);
        float accentAlpha = RevealPhase(t, 0.48, 0.4);
        Color light = InkColor.Lerp(Colors.White, 0.65f);
        var ci = _revealCanvas;
        string chapter = Index == 4 ? "REAL REALM" : $"DRAFT 0{Index + 1} / 04";
        float chapterWidth = UiKit.TextW(UiKit.Mono, chapter, 12);
        DrawRevealText(UiKit.Mono, new Vector2(-chapterWidth * 0.5f, -91), chapter, 12, new Color(light, accentAlpha));
        for (int side = -1; side <= 1; side += 2)
            ci.DrawLine(new Vector2(side * (chapterWidth * 0.5f + 15), -82),
                new Vector2(side * (chapterWidth * 0.5f + 45), -82), new Color(InkColor, accentAlpha * 0.6f), 1, true);

        string title = RevealTitles[Index];
        int size = Index == 4 ? 46 : 40;
        float width = UiKit.TextW(_revealFont, title, size);
        var at = new Vector2(-width * 0.5f, -54 + lift);
        DrawRevealText(_revealFont, at, title, size, new Color("fffaf2"), 4);

        float line = RevealPhase(t, 0.55, 0.55);
        float half = Mathf.Max(width * 0.5f, 160);
        ci.DrawLine(new Vector2(-half * line, 18), new Vector2(half * line, 18),
            new Color(InkColor, accentAlpha * 0.65f), 1, true);
        float sweep = RevealPhase(t, 0.62, Index == 4 ? 0.95 : 0.62);
        float gleam = Mathf.Sin(sweep * Mathf.Pi);
        float x = Mathf.Lerp(-half, half, sweep);
        ci.DrawLine(new Vector2(x - 15, 18), new Vector2(x + 15, 18), new Color(light, gleam), 2, true);
        ci.DrawLine(new Vector2(x, 14), new Vector2(x, 22), new Color(light, gleam * 0.7f), 1, true);

        float subtitleAlpha = RevealPhase(t, 0.88, 0.32);
        string next = Index == 4 ? "隠していた、本当の景色。" : Labels[Index + 1];
        float nextWidth = UiKit.TextW(UiKit.Zen, next, 19);
        DrawRevealText(UiKit.Zen, new Vector2(-nextWidth * 0.5f, 36 + 4 * (1 - subtitleAlpha)),
            next, 19, new Color(light, subtitleAlpha));
        for (int i = 0; i < 4; i++)
        {
            float filled = i < Index ? 1 : i == Index ? RevealPhase(t, 0.9, 0.5) : 0.15f;
            ci.DrawLine(new Vector2(-43 + i * 24, 78), new Vector2(-29 + i * 24, 78),
                new Color(light, filled * subtitleAlpha * 0.8f), 2, true);
        }
    }

    private void DrawRevealText(Font font, Vector2 at, string text, int size, Color color, int outline = 2)
    {
        _revealCanvas.DrawStringOutline(font, at + new Vector2(0, font.GetAscent(size)), text,
            HorizontalAlignment.Left, -1, size, outline, new Color(0.025f, 0.03f, 0.055f, color.A * 0.75f));
        UiKit.Text(_revealCanvas, font, at, text, size, color);
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
