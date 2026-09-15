using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class RouteBackgroundQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Write(object obj, string field, object value)
        => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static T Read<T>(object obj, string field)
        => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Call(object obj, string method, params object[] args)
        => obj.GetType().GetMethod(method, Private)!.Invoke(obj, args);
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
        GD.Print($"[RouteQA] PASS {message}");
    }
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private static Sprite2D[] Sprites(BgLayers layers)
        => layers.GetChildren().OfType<Sprite2D>().Where(s => !s.IsQueuedForDeletion()).ToArray();
    private static float Phase(Sprite2D sprite)
        => ((ShaderMaterial)sprite.Material).GetShaderParameter("scroll_offset").AsSingle();

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
                await CheckStage(game, scene);
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Frames(8);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(8);
            GD.Print("[RouteQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[RouteQA] FAIL {e}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task CheckStage(GameManager game, string scene)
    {
        string id = scene == "MinaBattle" ? "mina" : scene.ToLowerInvariant();
        game.SelectedEntry = GameManager.StageEntry.Start;
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
        stage.SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        world.ProcessMode = ProcessModeEnum.Inherit;
        var player = world.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        Write(player, "_invincible", true);
        Write(player, "_invincibleTimer", 999f);
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        hud.SetCinematicMode(false);
        await Frames(3);
        var bg = root.GetNode<StageBackground>("StageBackground");
        var layers = bg.GetNode<BgLayers>("BgLayers");
        var defs = (BgLayers.Layer[])root.GetType().GetField("RouteLayers")!.GetValue(null)!;
        Check(defs.Length == 3 && defs.Select(d => d.Path).Distinct().Count() == 3,
            $"{id}: three independent painted layers");
        foreach (var def in defs)
        {
            using var pixels = GD.Load<Texture2D>(def.Path).GetImage();
            Check(pixels.GetWidth() == 2172 && pixels.GetHeight() == 724, $"{def.Path}: registered panorama size");
            Check(def.Path.EndsWith("_far.png") ? pixels.DetectAlpha() == Image.AlphaMode.None
                : pixels.DetectAlpha() != Image.AlphaMode.None && pixels.GetPixel(900, 30).A == 0,
                $"{def.Path}: opaque far layer or transparent overlay");
        }
        await CheckPixels(game, id, defs);
        if (id != "mina")
        {
            Check(Sprites(layers).All(s => !s.Texture.ResourcePath.Contains("/boss/")), $"{id}: boss room not used by route");
            string[] waves = id == "akari" ? new[] { "0", "A", "B", "C" } : new[] { "A", "B", "C" };
            foreach (string wave in waves)
            {
                string method = wave == "0" ? "Step_MidWave0" : $"Step_Midwave{wave}";
                Write(stage, "_step", wave == "0" ? 2 : 5);
                Write(stage, "_stepStarted", false);
                typeof(GameManager).GetProperty("PurifiedCount")!.SetValue(game, 0);
                Call(stage, method, 0d);
                var spawner = Read<Spawner>(stage, "_spawner");
                spawner.SetProcess(false);
                await Frames(55);
                Check(Sprites(layers).Length == 3 && Sprites(layers).All(s => s.Material is ShaderMaterial),
                    $"{id}/{wave}: actual wave starts parallax");
                typeof(GameManager).GetProperty("PurifiedCount")!.SetValue(game, 100);
                Call(stage, method, 0d);
                await Frames(55);
                Check(Sprites(layers).All(s => s.Texture.ResourcePath.Contains("/route/") == (wave == "0")),
                    $"{id}/{wave}: completion preserves direct cameo continuity or restores dialogue location");
                if (id == "koharu" && wave == "B")
                    Check(Sprites(layers).Any(s => s.Texture.ResourcePath.Contains("L1_far_class")), "school dialogue retains classroom");
            }
            await CheckMidboss(game, id, stage, bg, layers, hud);
            bg.BeginRoute();
            typeof(GameManager).GetProperty("PurifiedCount")!.SetValue(game, 0);
        }
        await Frames(55);
        layers.SetProcess(false);
        var art = Sprites(layers);
        Check(art.Length == 3, $"{id}: exactly three route draw surfaces");
        Hud.BubblePaused = false;
        foreach (float x in new[] { Field.Left, Field.CenterX, Field.Right })
        {
            game.TickProgress(x, 0);
            var before = art.Select(Phase).ToArray();
            layers._Process(1d);
            var movement = art.Select((s, i) => Mathf.PosMod(Phase(s) - before[i], 0.92f)).ToArray();
            Check(movement[0] > 0 && movement[1] > movement[0] * 2 && movement[2] > movement[1] * 1.8f,
                $"{id}: far < mid < near speed at x={x}");
            Check(art.All(s => s.GlobalPosition.X <= Field.Left && s.GlobalPosition.X + s.RegionRect.Size.X * s.Scale.X >= Field.Right
                && s.GlobalPosition.Y <= 0 && s.GlobalPosition.Y + s.RegionRect.Size.Y * s.Scale.Y >= Field.Bottom
                && Mathf.IsEqualApprox(s.Scale.X, s.Scale.Y)), $"{id}: all layers cover field without stretching");
        }
        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
        await Frames(3);
        var stopped = art.Select(Phase).ToArray();
        layers._Process(2d);
        Check(stopped.SequenceEqual(art.Select(Phase)), $"{id}: conversation freezes all three layers");
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(3);
        layers._Process(1d);
        Check(!stopped.SequenceEqual(art.Select(Phase)), $"{id}: scroll resumes after conversation");
        hud.SetCinematicMode(true);
        await Frames(3);
        var cinematic = art.Select(Phase).ToArray();
        layers._Process(2d);
        Check(cinematic.SequenceEqual(art.Select(Phase)), $"{id}: cinematic also freezes parallax");
        hud.SetCinematicMode(false);
        await Frames(3);
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Shot($"{id}_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        for (int i = 0; i < 4; i++)
        {
            layers._Process(6d);
            await Shot($"{id}_scroll_{i}");
        }
        layers.SetProcess(true);
        Write(stage, "_stepStarted", false);
        if (id == "mina")
        {
            Call(stage, "Step_BossSpawn");
            var echoes = Read<Spawner>(stage, "_echoSpawner");
            echoes.SetProcess(false);
            Check(Sprites(layers).All(s => s.Texture.ResourcePath.Contains("/route/")), "Mina echoes still precede the boss room");
            typeof(Spawner).GetProperty("SpawnedCount")!.SetValue(echoes, echoes.SpawnLimit);
            typeof(GameManager).GetProperty("PurifiedCount")!.SetValue(game, echoes.SpawnLimit);
            Call(stage, "Step_BossSpawn");
        }
        else Call(stage, "Step_BossSpawn");
        await Frames(70);
        Check(Sprites(layers).Length == 1 && Sprites(layers)[0].Texture.ResourcePath == $"res://char/bg2/boss/{id}_v1.png",
            $"{id}: actual boss spawn replaces all route layers with the original special room");
        bg.BeginRoute();
        bg.BeginMidboss();
        bg.ReturnToStory();
        await Frames(55);
        Check(Sprites(layers).Length == 1 && Sprites(layers)[0].Material == null, $"{id}: route hooks cannot replace active boss room");
        await Shot($"{id}_boss_arrival");
        root.QueueFree();
        await Frames(8);
        Pool.DespawnAll();
        Hud.BubblePaused = false;
        if (id != "mina") await CheckMidbossEntry(game, scene, id);
    }

    private async Task CheckMidboss(GameManager game, string id, Node stage,
        StageBackground bg, BgLayers layers, Hud hud)
    {
        bg.BeginRoute();
        await Frames(55);
        Write(stage, "_step", id == "akari" ? 3 : 5);
        Write(stage, "_stepStarted", false);
        Call(stage, "Step_BossCameo", 0d);
        await Frames(55);
        var art = Sprites(layers);
        Check(art.Length == 1 && art[0].Texture.ResourcePath == $"res://char/bg2/midboss/{id}_v1.png"
            && art[0].Material == null, $"{id}: actual cameo replaces route with its dedicated room");
        Check(layers.BossDimK == 0, $"{id}: midboss does not trigger main boss lighting");
        using (var pixels = art[0].Texture.GetImage())
            Check(pixels.GetWidth() >= 1200 && pixels.GetHeight() >= 1000 && pixels.DetectAlpha() == Image.AlphaMode.None,
                $"{id}: high-resolution opaque midboss painting");
        bg.BeginMidboss();
        bg.CrossfadeLayersTo(bg.LayerDefs);
        await Frames(55);
        Check(Sprites(layers).Single() == art[0], $"{id}: repeated entry and story-location updates preserve active room");
        layers.SetProcess(false);
        game.TickProgress(Field.CenterX, 0);
        layers._Process(5d);
        var position = art[0].Position;
        layers._Process(30d);
        Check(position.IsEqualApprox(art[0].Position), $"{id}: room does not scroll with elapsed time");
        foreach (float x in new[] { Field.Left, Field.Right })
        {
            game.TickProgress(x, 0);
            layers._Process(5d);
            var sprite = art[0];
            Check(sprite.Position.X <= Field.Left && sprite.Position.X + sprite.Texture.GetWidth() * sprite.Scale.X >= Field.Right
                && sprite.Position.Y <= 0 && sprite.Position.Y + sprite.Texture.GetHeight() * sprite.Scale.Y >= Field.Bottom,
                $"{id}: room covers field at player x={x}");
        }
        layers.SetProcess(true);
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Shot($"{id}_midboss_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        typeof(CameoBoss).GetProperty("Finished")!.SetValue(Read<CameoBoss>(stage, "_cameo"), true);
        Call(stage, "Step_BossCameo", 0d);
        if (id == "koharu")
        {
            await Frames(55);
            Check(Sprites(layers).Single() == art[0] && Read<int>(stage, "_step") == 5,
                "koharu: penlight choice stays in the midboss entrance hall");
            Write(stage, "_cPhase", 3);
            Call(stage, "Step_BossCameo", 0d);
        }
        hud.HoldBubble = false;
        hud.HideBubble();
        hud.HideBossBar();
        hud.ShowBossLine("", "", Colors.White, 0);
        await Frames(55);
        Check(Sprites(layers).All(s => !s.Texture.ResourcePath.Contains("/midboss/") && !s.Texture.ResourcePath.Contains("/route/")),
            $"{id}: cameo completion restores the story location");
        Pool.DespawnAll();
    }

    private async Task CheckMidbossEntry(GameManager game, string scene, string id)
    {
        game.SelectedEntry = GameManager.StageEntry.MidBoss;
        var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        root.GetNode<Node2D>("World").ProcessMode = ProcessModeEnum.Inherit;
        root.GetNode<Player>("World/Player").SetPhysicsProcess(false);
        var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
        stage.SetProcess(false);
        Check(Read<int>(stage, "_step") == (id == "akari" ? 3 : 5), $"{id}: midboss checkpoint selects cameo step");
        stage._Process(0d);
        await Frames(60);
        var layers = root.GetNode<BgLayers>("StageBackground/BgLayers");
        Check(Sprites(layers).Length == 1 && Sprites(layers)[0].Texture.ResourcePath == $"res://char/bg2/midboss/{id}_v1.png"
            && layers.BossDimK == 0, $"{id}: checkpoint starts inside dedicated midboss room");
        await Shot($"{id}_midboss_checkpoint");
        root.QueueFree();
        await Frames(8);
        Pool.DespawnAll();
        Hud.BubblePaused = false;
    }

    private async Task CheckPixels(GameManager game, string id, BgLayers.Layer[] defs)
    {
        var viewport = new SubViewport { Size = new Vector2I(384, 216), World2D = new World2D(),
            TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        var bg = new BgLayers { Layers = defs };
        viewport.AddChild(bg);
        bg.SetProcess(false);
        var art = Sprites(bg);
        foreach (var sprite in art)
        {
            foreach (var other in art) other.Visible = other == sprite;
            ((ShaderMaterial)sprite.Material).SetShaderParameter("scroll_offset", 0.2f);
            using var before = await Render(viewport);
            ((ShaderMaterial)sprite.Material).SetShaderParameter("scroll_offset", 0.2f + 3f / (sprite.Texture.GetWidth() * sprite.Scale.X));
            using var after = await Render(viewport);
            float error = 0;
            int count = 0, visible = 0;
            for (int y = 4; y < 212; y += 4)
                for (int x = 124; x < 376; x += 4)
                {
                    var a = before.GetPixel(x + 3, y);
                    var b = after.GetPixel(x, y);
                    error += ColorDistance(a, b);
                    count++;
                    if (a.A > 0.8f) visible++;
                }
            Check(visible > 40 && error / count < 0.025f, $"{sprite.Texture.ResourcePath}: pixels move left without blanking ({error / count:F4})");
            ((ShaderMaterial)sprite.Material).SetShaderParameter("scroll_offset", 0.91999f);
            using var end = await Render(viewport);
            ((ShaderMaterial)sprite.Material).SetShaderParameter("scroll_offset", 0.00001f);
            using var start = await Render(viewport);
            float seam = 0;
            for (int y = 4; y < 212; y += 4)
                for (int x = 124; x < 380; x += 4) seam += ColorDistance(end.GetPixel(x, y), start.GetPixel(x, y));
            Check(seam / (52 * 64) < 0.002f, $"{sprite.Texture.ResourcePath}: wrap continuity");
        }
        foreach (var sprite in art)
        {
            sprite.Visible = true;
            ((ShaderMaterial)sprite.Material).SetShaderParameter("scroll_offset", 0.2f);
        }
        using (var composite = await Render(viewport))
            Check(composite.GetPixel(125, 108).A > 0.99f && composite.GetPixel(380, 108).A > 0.99f,
                $"{id}: composite covers both field edges");
        viewport.QueueFree();
        await Frames(3);
    }
    private static float ColorDistance(Color a, Color b)
        => (Mathf.Abs(a.R * a.A - b.R * b.A) + Mathf.Abs(a.G * a.A - b.G * b.A)
            + Mathf.Abs(a.B * a.A - b.B * b.A) + Mathf.Abs(a.A - b.A)) / 4f;
    private async Task<Image> Render(SubViewport viewport)
    {
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return viewport.GetTexture().GetImage();
    }
    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Shot(string name)
    {
        string directory = ProjectSettings.GlobalizePath("res://build/qa_story/routes/shots");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        await Frames(8);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var pixels = GetViewport().GetTexture().GetImage();
        Check(pixels.SavePng($"{directory}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
