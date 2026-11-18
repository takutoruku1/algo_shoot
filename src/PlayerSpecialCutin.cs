using Godot;
using System.Collections.Generic;

public partial class PlayerSpecial
{
    internal const float CutinDuration = .9f;
    private static readonly Dictionary<Job, Texture2D> CutinPortraits = new();
    private CanvasLayer? _cutinLayer;
    private PlayerSpecialCutin? _cutinCanvas;
    public bool CutinActive { get; private set; }
    internal float CutinElapsed { get; private set; }

    private static Texture2D PrepareCutinPortrait(Job character)
    {
        if (!CutinPortraits.TryGetValue(character, out var portrait))
        {
            portrait = GD.Load<Texture2D>($"res://char/bg2/opening/op_{Jobs.Get(character).CharacterId}_cutin_v1.png");
            CutinPortraits[character] = portrait;
        }
        return portrait;
    }

    private void BeginCutin()
    {
        CutinActive = true;
        _cutinLayer = new CanvasLayer { Name = "SpecialCutin", Layer = 45, ProcessMode = ProcessModeEnum.Always };
        AddChild(_cutinLayer);
        Texture2D? portrait = DisplayServer.GetName() == "headless" ? null : PrepareCutinPortrait(Character);
        _cutinCanvas = new PlayerSpecialCutin { Special = this, Portrait = portrait, ProcessPhysicsPriority = -100 };
        _cutinLayer.AddChild(_cutinCanvas);
        GetTree().Paused = true;
    }

    internal void AdvanceCutin(float dt)
    {
        if (!CutinActive) return;
        if (!Active || !IsInstanceValid(_player) || _player.IsQueuedForDeletion() || _player.IsGameOver)
        {
            Cancel();
            return;
        }
        if (GetNode<PauseMenu>("/root/PauseMenu").IsOpen || Hud.BubblePaused) return;
        CutinElapsed += dt;
        if (CutinElapsed >= CutinDuration) EndCutin();
    }

    private void EndCutin()
    {
        if (!CutinActive) return;
        CutinActive = false;
        if (IsInstanceValid(_cutinLayer)) _cutinLayer!.Visible = false;
        if (IsInstanceValid(_cutinCanvas)) _cutinCanvas!.SetPhysicsProcess(false);
        if (IsInsideTree())
        {
            // The pause menu or practice preview can take over the cut-in's pause.
            GetTree().Paused = GetNode<PauseMenu>("/root/PauseMenu").IsOpen
                || BossPracticeRoot.Active is { Practicing: true, PreviewPaused: true };
            QueueRedraw();
        }
    }
}

internal partial class PlayerSpecialCutin : Node2D
{
    internal PlayerSpecial Special = null!;
    internal Texture2D? Portrait;
    private static readonly Color Ink = new("080e23");

    public override void _Ready() => TextureFilter = TextureFilterEnum.Linear;

    public override void _PhysicsProcess(double delta)
    {
        Special.AdvanceCutin((float)delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Special.CutinActive || Portrait == null) return;
        float age = Special.CutinElapsed;
        float enter = 1 - Mathf.Pow(1 - Mathf.Clamp(age / .17f, 0, 1), 3);
        float exit = Mathf.Pow(Mathf.Clamp((age - .68f) / .22f, 0, 1), 2);
        float alpha = Mathf.Clamp(age / .06f, 0, 1) * (1 - exit);
        Color tint = Special.Character switch
        {
            Job.Melee => new Color("ffc77c"), Job.Heal => new Color("9ef4cd"),
            Job.Magic => new Color("cdb6ff"), _ => new Color("9feaff"),
        };
        Color accent = Special.Character switch
        {
            Job.Melee => new Color("ed685a"), Job.Heal => new Color("63bbbc"),
            Job.Magic => new Color("9567eb"), _ => new Color("448ad7"),
        };
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, 1280, 720), new Color(Ink, .50f * alpha));
        DrawRect(new Rect2(0, 0, 1280, 32 * enter), new Color(Ink, alpha));
        DrawRect(new Rect2(0, 720 - 32 * enter, 1280, 32 * enter), new Color(Ink, alpha));
        float half = 224 * enter * (1 - exit);
        Vector2[] band = { new(-40, 360 - half + 42), new(1320, 360 - half - 42),
            new(1320, 360 + half - 42), new(-40, 360 + half + 42) };
        DrawColoredPolygon(band, new Color(Ink, .94f * alpha));
        DrawPolygon(band, new[] { new Color(accent, .52f * alpha), new Color(accent, .03f * alpha),
            new Color(accent, .25f * alpha), new Color(accent, .05f * alpha) });
        DrawLine(band[0], band[1], new Color(tint, .8f * alpha), 2.5f, true);
        DrawLine(band[3], band[2], new Color(accent, alpha), 7, true);
        DrawMotif(age, enter, alpha, tint, accent);

        float slide = -210 * (1 - enter) + 560 * exit;
        float height = 670 - 20 * enter;
        float width = height * Portrait.GetWidth() / Portrait.GetHeight();
        Rect2 portrait = new(390 - width * .5f + slide, 43 - 13 * enter, width, height);
        for (int i = 3; i >= 1; i--)
        {
            Rect2 echo = new(portrait.Position - new Vector2(i * (12 + 30 * (1 - enter)), 0), portrait.Size);
            DrawTextureRect(Portrait, echo, false, new Color(tint, alpha * (1 - enter) * .2f));
        }
        UiKit.RadialGlow(this, new Vector2(390 + slide, 365), 310, tint, .15f * alpha);
        DrawTextureRect(Portrait, portrait, false, new Color(Colors.White, alpha));

        float textEnter = 1 - Mathf.Pow(1 - Mathf.Clamp((age - .06f) / .17f, 0, 1), 3);
        float textX = 658 + (1 - textEnter) * 180 + 520 * exit;
        float textAlpha = alpha * textEnter;
        string[] lines = Special.Character switch
        {
            Job.Melee => new[] { "もう、", "取り消さない" },
            Job.Heal => new[] { "ここに", "いていい" },
            Job.Magic => new[] { "最後まで、", "聞いて" },
            _ => new[] { "お返事、", "届けます" },
        };
        DrawLine(new Vector2(textX, 245), new Vector2(textX + 400 * textEnter, 220), new Color(accent, textAlpha), 3, true);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(textX + 4, 188), Jobs.Get(Special.Character).CharacterName,
            34, new Color(tint, textAlpha));
        for (int i = 0; i < lines.Length; i++)
        {
            Vector2 at = new(textX + i * 26, 267 + i * 91);
            UiKit.Text(this, UiKit.DelaGothic, at + new Vector2(-3, 5), lines[i], 69, new Color(accent, textAlpha));
            UiKit.Text(this, UiKit.DelaGothic, at, lines[i], 69, new Color(Colors.White, textAlpha));
        }
        float glint = Mathf.Clamp(1 - Mathf.Abs(age - .19f) / .085f, 0, 1);
        DrawLine(new Vector2(textX - 45, 244), new Vector2(textX + 70, 237), new Color(Colors.White, glint), 3, true);
        DrawLine(new Vector2(textX + 12, 220), new Vector2(textX + 12, 261), new Color(Colors.White, glint), 2, true);
        UiKit.EndDesign(this);
    }

    private void DrawMotif(float age, float enter, float alpha, Color tint, Color accent)
    {
        Vector2 center = new(390, 355);
        switch (Special.Character)
        {
            case Job.Tank:
                for (int ring = 0; ring < 3; ring++)
                {
                    float r = (174 + ring * 58) * (.76f + enter * .24f);
                    float turn = age * (ring % 2 == 0 ? 1 : -1) * .65f;
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.Tau / 6 + turn;
                        DrawArc(center, r, a, a + .75f, 18, new Color(tint, alpha * .35f), ring == 2 ? 4 : 2, true);
                        Vector2 point = center + Vector2.FromAngle(a) * r;
                        Diamond(point, 7, new Color(tint, alpha * .7f));
                    }
                }
                for (int i = 0; i < 14; i++)
                {
                    float a = i * Mathf.Tau / 14;
                    float r = 330 - Mathf.PosMod(age * 280 + i * 39, 170);
                    Vector2 point = center + Vector2.FromAngle(a) * r;
                    DrawLine(point, point + Vector2.FromAngle(a) * 22, new Color(tint, alpha * .55f), 2, true);
                }
                break;
            case Job.Melee:
                for (int i = 0; i < 7; i++)
                {
                    float y = 170 + i * 67;
                    float lead = Mathf.PosMod(age * 1600 + i * 167, 1580) - 220;
                    Vector2 at = new(lead, y - lead * .14f);
                    DrawLine(at - new Vector2(190 + i * 20, -26), at, new Color(tint, alpha * .6f), 2 + i % 3, true);
                }
                DrawColoredPolygon(new[] { new Vector2(70, 560), new Vector2(615, 92), new Vector2(582, 144) }, new Color(accent, alpha * .45f));
                DrawColoredPolygon(new[] { new Vector2(95, 160), new Vector2(705, 600), new Vector2(637, 574) }, new Color(tint, alpha * .4f));
                break;
            case Job.Heal:
                DrawArc(center, 230, 0, Mathf.Tau, 100, new Color(tint, .38f * alpha), 2, true);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.Tau / 12 + age * .32f;
                    Vector2 axis = Vector2.FromAngle(a), side = axis.Orthogonal();
                    Vector2 at = center + axis * (220 + 15 * enter);
                    DrawColoredPolygon(new[] { at - axis * 44, at + side * 16, at + axis * 44, at - side * 16 }, new Color(tint, alpha * .35f));
                    DrawLine(at - axis * 28, at + axis * 28, new Color(tint, alpha * .7f), 2, true);
                }
                for (int i = 0; i < 12; i++)
                {
                    Vector2 point = new(105 + i * 93, 560 - Mathf.PosMod(age * 90 + i * 47, 390));
                    Diamond(point, 3 + i % 3, new Color(tint, alpha * .6f));
                }
                break;
            case Job.Magic:
                Vector2[] stars = { new(84, 279), new(225, 157), new(511, 115), new(613, 292), new(548, 550),
                    new(870, 510), new(1060, 564), new(1180, 425) };
                for (int i = 0; i < stars.Length; i++)
                {
                    float reveal = Mathf.Clamp((age - i * .025f) / .13f, 0, 1);
                    if (i > 0) DrawLine(stars[i - 1], stars[i - 1].Lerp(stars[i], reveal), new Color(accent, alpha * .7f), 2, true);
                    float size = (10 + Mathf.Sin(age * 5 + i) * 3) * reveal;
                    Diamond(stars[i], size, new Color(tint, alpha));
                    DrawLine(stars[i] - Vector2.Up * size * 2, stars[i] + Vector2.Up * size * 2, new Color(tint, alpha * .45f), 1, true);
                }
                DrawArc(center, 265, -.5f + age * .2f, 2.6f + age * .2f, 80, new Color(tint, .25f * alpha), 2, true);
                break;
        }
    }

    private void Diamond(Vector2 at, float radius, Color color)
        => DrawColoredPolygon(new[] { at + Vector2.Up * radius, at + Vector2.Right * radius * .5f,
            at + Vector2.Down * radius, at + Vector2.Left * radius * .5f }, color);
}
