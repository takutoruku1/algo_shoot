using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class AttackLineQa : Node
{
    private static readonly Color Background = new("11131c");
    private readonly (Vector2[] Points, float Radius, Color Ink, bool Round)[] _cases =
    {
        (new[] { new Vector2(90, 35), new Vector2(280, 35) }, 10, new("d2a9ff"), true),
        (new[] { new Vector2(102, 80), new Vector2(102, 170) }, 7, new("a186bc"), true),
        (new[] { new Vector2(145, 105), new Vector2(220, 160) }, 8, new("b37dba"), true),
        (new[] { new Vector2(240, 90), new Vector2(270, 88), new Vector2(285, 114), new Vector2(269, 149), new Vector2(306, 168) }, 6, new("77c7b2"), true),
        (new[] { new Vector2(40, 196), new Vector2(140, 196) }, 4, new("ff7568"), true),
        (new[] { new Vector2(330, 25), new Vector2(330, 65) }, 8, new("ffb075"), false),
        (new[] { new Vector2(150, 64), new Vector2(290, 64) }, 1.5f, new("d2a9ff"), true),
    };

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Require(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            var viewport = new SubViewport { Size = new Vector2I(384, 216),
                Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            var background = new Polygon2D { Polygon = new[] { Vector2.Zero, new Vector2(384, 0),
                new Vector2(384, 216), new Vector2(0, 216) }, Color = Background };
            viewport.AddChild(background);
            var layer = new AttackLineLayer();
            viewport.AddChild(layer);
            layer.Begin();
            foreach (var sample in _cases) layer.Add(sample.Points, sample.Radius, sample.Ink, roundCaps: sample.Round);
            layer.End();
            bool graphics = DisplayServer.GetName() != "headless";
            if (graphics)
            {
                await DrawFrame();
                using var image = viewport.GetTexture().GetImage();
                string folder = ProjectSettings.GlobalizePath("res://build/qa_story/attack_lines");
                DirAccess.MakeDirRecursiveAbsolute(folder);
                Require(image.SavePng(folder + "/volume_gallery.png") == Error.Ok, "gallery saved");
                int visible = 0;
                for (int y = 0; y < 216; y++)
                for (int x = 0; x < 384; x++)
                {
                    if (!Drawn(image.GetPixel(x, y))) continue;
                    visible++;
                    Vector2 point = new(x + .5f, y + .5f);
                    Require(point.X >= Field.Left && InsideStroke(point, 1.1f), $"pixel outside collision stroke: {point}", false);
                }
                Require(visible > 8000, "strokes visible and clipped to collision footprint");
                foreach (var sample in _cases)
                {
                    var center = (sample.Points[0] + sample.Points[1]) * .5f;
                    Require(Drawn(image.GetPixel((int)center.X, (int)center.Y)), "stroke center is opaque");
                }
                Require(image.GetPixel(180, 32).Luminance > image.GetPixel(180, 41).Luminance * 1.4,
                    "highlight and shaded edge produce a rounded cross section");
                GetTree().Paused = true;
                await DrawFrame();
                using var paused = viewport.GetTexture().GetImage();
                Require(image.GetData().SequenceEqual(paused.GetData()), "pause freezes the rendered surface");
                GetTree().Paused = false;
                layer.Begin(.25);
                foreach (var sample in _cases) layer.Add(sample.Points, sample.Radius, sample.Ink, roundCaps: sample.Round);
                layer.End();
                await DrawFrame();
                using var flowing = viewport.GetTexture().GetImage();
                Require(!image.GetData().SequenceEqual(flowing.GetData()), "combat time advances the line highlight");
                for (int y = 0; y < 216; y++)
                for (int x = 0; x < 384; x++)
                    if (Drawn(flowing.GetPixel(x, y)))
                        Require(InsideStroke(new Vector2(x + .5f, y + .5f), 1.1f), "moving highlight stays inside collision", false);
                layer.Begin();
                foreach (var sample in _cases)
                    layer.Add(sample.Points, sample.Radius, sample.Ink, roundCaps: sample.Round, ribbon: true);
                layer.End();
                await DrawFrame();
                using var ribbon = viewport.GetTexture().GetImage();
                Require(!image.GetData().SequenceEqual(ribbon.GetData()), "ribbon has a distinct surface");
                for (int y = 0; y < 216; y++)
                for (int x = 0; x < 384; x++)
                    Require(Drawn(image.GetPixel(x, y)) == Drawn(ribbon.GetPixel(x, y)),
                        $"surface change preserves rendered collision footprint at {x},{y}", false);
                Require(ribbon.SavePng(folder + "/ribbon_gallery.png") == Error.Ok, "ribbon gallery saved");
            }
            layer.Begin();
            layer.End();
            Require(layer.GetChildren().OfType<Line2D>().All(line => !line.Visible), "expired strokes are hidden");
            layer.Begin();
            layer.Add(_cases[0].Points, _cases[0].Radius, _cases[0].Ink);
            layer.End();
            Require(layer.GetChildCount() == _cases.Length && layer.GetChildren().OfType<Line2D>().Count(line => line.Visible) == 1,
                "restart reuses nodes without residual strokes");
            layer.Begin();
            layer.Surface(new Rect2(80, 20, 100, 60), new Color("b899e2"));
            layer.Surface(new Rect2(220, 20, 60, 60), new Color("e3b1db"), disc: true);
            layer.Surface(new Rect2(70, 100, 250, 88), new Color("bad6da"), safeCenter: new Vector2(195, 145), safeRadius: 26);
            layer.End();
            if (graphics)
            {
                await DrawFrame();
                using var fields = viewport.GetTexture().GetImage();
                Require(Drawn(fields.GetPixel(85, 30)) && !Drawn(fields.GetPixel(78, 30)), "raised panel respects rectangle");
                Require(Drawn(fields.GetPixel(250, 50)) && !Drawn(fields.GetPixel(222, 22)), "lens respects circular boundary");
                Require(!Drawn(fields.GetPixel(219, 145)) && Drawn(fields.GetPixel(223, 145)), "outside field keeps safe disc clear");
                Require(fields.GetPixel(244, 40).Luminance > fields.GetPixel(266, 62).Luminance, "lens has a directional highlight");
                string folder = ProjectSettings.GlobalizePath("res://build/qa_story/attack_lines");
                Require(fields.SavePng(folder + "/active_surfaces.png") == Error.Ok, "active surfaces saved");
            }
            layer.Begin();
            layer.End();
            Require(layer.GetChildren().OfType<Sprite2D>().All(body => !body.Visible), "expired areas are hidden");
            layer.Begin();
            var fan = new Vector4(100, 60, .3f, 0);
            for (int y = 42; y < 78; y += 2)
                layer.FanRow(new Rect2(120 + Math.Abs(59 - y) * 3, y, 60, 2), Colors.White, fan);
            layer.End();
            Require(layer.RenderedSurfaces == 1, "one illuminated surface for all collision slices in a fan");
            if (graphics)
            {
                await DrawFrame();
                using var cone = viewport.GetTexture().GetImage();
                Require(!Drawn(cone.GetPixel(126, 42)) && Drawn(cone.GetPixel(170, 56)), "fan follows the original collision slices");
                Require(Math.Abs(cone.GetPixel(170, 56).Luminance - cone.GetPixel(170, 57).Luminance) < .09,
                    "fan shading is continuous across row boundaries");
            }
            layer.Hide();
            Require(layer.GetChildren().OfType<Line2D>().All(line => !line.IsVisibleInTree()), "cancellation hides strokes immediately");
            await VerifyAreaStrikes(viewport, graphics);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"[AttackLineQA] ALL PASS graphics={graphics}");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("[AttackLineQA] " + error);
            GetTree().Quit(1);
        }
    }

    private bool InsideStroke(Vector2 point, float tolerance)
    {
        foreach (var sample in _cases)
        for (int i = 1; i < sample.Points.Length; i++)
        {
            Vector2 a = sample.Points[i - 1], b = sample.Points[i];
            if (!sample.Round && (point - a).Dot(b - a) < -tolerance * a.DistanceTo(b)) continue;
            if (!sample.Round && (point - b).Dot(b - a) > tolerance * a.DistanceTo(b)) continue;
            if (point.DistanceTo(Geometry2D.GetClosestPointToSegment(point, a, b)) <= sample.Radius + tolerance) return true;
        }
        return false;
    }

    private async Task VerifyAreaStrikes(SubViewport parent, bool graphics)
    {
        foreach (var shape in new[] { AreaStrike.Shape.BeamH, AreaStrike.Shape.BeamV, AreaStrike.Shape.BeamSeg })
        {
            var strike = new AreaStrike { Position = new Vector2(200, 108) };
            Vector2 axis = shape == AreaStrike.Shape.BeamV ? Vector2.Down : Vector2.Right;
            if (shape == AreaStrike.Shape.BeamSeg)
                strike.ConfigureBeam(axis, 60, 8, .35, new Color("ff7568"), Colors.White);
            else strike.Configure(shape, shape == AreaStrike.Shape.BeamH ? 30 : 8,
                shape == AreaStrike.Shape.BeamH ? 8 : 30, .35, new Color("ff7568"), Colors.White);
            parent.AddChild(strike);
            strike.SetProcess(false);
            var strokes = strike.GetChildren().OfType<AttackLineLayer>().Single();
            Require(!strike.IsStriking && strokes.GetChildCount() == 0, shape + " warning has no live stroke");
            strike._Process(.36 / GameManager.EnemyTimeScale);
            var line = strokes.GetChildren().OfType<Line2D>().Single();
            Vector2 middle = (line.Points[0] + line.Points[^1]) * .5f;
            Require(strike.IsStriking && line.Visible && strike.CoversPoint(strike.Position + middle), shape + " live center matches collision");
            Require(!strike.CoversPoint(strike.Position + middle + axis.Orthogonal() * (line.Width * .5f + 1)),
                shape + " outside stroke remains safe");
            bool round = shape == AreaStrike.Shape.BeamSeg;
            Require(strike.CoversPoint(strike.Position + line.Points[^1] + axis * 3) == round,
                shape + " endpoint shape matches collision");
            Require((line.EndCapMode == Line2D.LineCapMode.Round) == round, shape + " matching rendered cap");
            if (graphics)
            {
                await DrawFrame();
                using var image = parent.GetTexture().GetImage();
                Vector2 center = strike.Position + middle;
                Require(Drawn(image.GetPixel((int)center.X, (int)center.Y)), shape + " translated live stroke is rendered");
            }
            strike.Cancel();
            Require(!line.IsVisibleInTree(), shape + " cancellation removes live rendering");
        }
    }

    private static bool Drawn(Color color) => color.R + color.G + color.B > Background.R + Background.G + Background.B + .08f;
    private async Task DrawFrame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
    private static void Require(bool condition, string message, bool log = true)
    {
        if (!condition) throw new Exception(message);
        if (log) GD.Print("[AttackLineQA] PASS " + message);
    }
}
