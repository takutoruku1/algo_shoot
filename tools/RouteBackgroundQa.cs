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
            if (OS.GetCmdlineUserArgs().Contains("--ui-refresh"))
                await CheckPresentation(game);
            else if (OS.GetCmdlineUserArgs().Contains("--start-banner") || OS.GetCmdlineUserArgs().Contains("--start-banner-demo"))
                await CheckStartBanners(game);
            else
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

    private async Task CheckPresentation(GameManager game)
    {
        string output = ProjectSettings.GlobalizePath("res://build/qa_story/ui_refresh");
        DirAccess.MakeDirRecursiveAbsolute(output);
        void BaseCall(Enemy boss, string method, params object[] args) =>
            typeof(Enemy).GetMethod(method, Private)!.Invoke(boss, args);
        object BaseRead(Enemy boss, string field) => typeof(Enemy).GetField(field, Private)!.GetValue(boss)!;
        async Task Shot(string name)
        {
            await Frames(4);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng($"{output}/{name}.png") == Error.Ok, $"rendered {name}");
        }
        foreach (var job in Jobs.All)
        {
            var icon = GD.Load<Texture2D>(CompanionDialogue.AccountPortrait(job.Id));
            Check(icon.GetWidth() == icon.GetHeight() && icon.ResourcePath.Contains("/ui/sns_"),
                $"{job.CharacterId}: dedicated square SNS art");
            Check(icon.ResourcePath != CompanionDialogue.Portrait(job.Id), "SNS and dialogue art remain separate");
            Check(BulletArt.PlayerMark(job.Id).GetWidth() > 0, $"{job.CharacterId}: lock emblem loads");
        }
        using (var shield = GD.Load<Texture2D>("res://char/ui/boss_shield_v1.png").GetImage())
            Check(shield.GetPixel(0, 0).A == 0 && shield.GetPixel(shield.GetWidth() / 2, shield.GetHeight() / 2).A == 0,
                "shield art has a transparent exterior and center");
        using (var you = GD.Load<Texture2D>("res://char/ui/dialogue_you_v1.png").GetImage())
            Check(you.DetectAlpha() != Image.AlphaMode.None, "anonymous dialogue emblem is transparent");
        foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
        {
            game.SelectedEntry = GameManager.StageEntry.Start;
            var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
            var world = root.GetNode<Node2D>("World");
            world.ProcessMode = ProcessModeEnum.Inherit;
            var player = world.GetNode<Player>("Player");
            player.SetPhysicsProcess(false);
            Write(player, "_invincible", true);
            player.Position = new Vector2(170, 135);
            var hud = root.GetNode<Hud>("Hud");
            hud.SetProcess(false);
            hud.HoldBubble = false;
            hud.HideBubble();
            hud.SetCinematicMode(false);
            Write(hud, "_bannerTimer", 0d);
            Enemy boss = scene switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(),
                "Rei" => new BossRei(), _ => new BossMina() };
            world.AddChild(boss);
            boss.Position = new Vector2(290, 110);
            boss.SetPhysicsProcess(false);
            boss.SetProcess(false);
            foreach (var child in boss.GetChildren()) { child.SetProcess(false); child.SetPhysicsProcess(false); }
            BaseCall(boss, "TickEntrance", 0d);
            BaseCall(boss, "TickEntrance", 2d);
            world.ProcessMode = ProcessModeEnum.Disabled;
            boss.QueueRedraw();
            root.GetNode<StageBackground>("StageBackground").EnterBoss();
            hud.ShowBossBar(scene == "MinaBattle" ? "ミナ" : scene == "Akari" ? "あかり" : scene == "Koharu" ? "こはる" : "レイ");
            hud.HideSpellCard();
            Write(hud, "_cutinTimer", 0d);
            await Frames(100);
            Write(hud, "_bossLineTimer", 0d);
            Pool.DespawnAll();
            hud._Process(0);
            Check(BaseRead(boss, "_phase").ToString() == "Shielded" && boss.GetChildren().OfType<Panel>().Any(),
                $"{scene}: shield follows actual panel state");
            Check(boss.CollisionMask == 0, $"{scene}: shielded body is not vulnerable");
            await Shot($"{scene}_shield");
            if (scene == "Akari")
                foreach (var job in Jobs.All)
                {
                    game.SelectedJob = job.Id;
                    Write(player, "_locked", true);
                    Write(player, "_lockTarget", boss);
                    hud._Process(0);
                    boss.QueueRedraw();
                    await Shot($"lock_{job.CharacterId}");
                }
            Write(player, "_locked", false);
            float hp = boss.HpRatio;
            foreach (var panel in boss.GetChildren().OfType<Panel>().ToArray()) panel.Shatter();
            Check(BaseRead(boss, "_phase").ToString() == "Break" && boss.HpRatio == hp,
                $"{scene}: real panel destruction breaks only the shield");
            Check(Read<bool>(hud, "_bossLineBreak"), $"{scene}: shield break uses the new callout");
            typeof(Enemy).GetField("_phaseT", Private)!.SetValue(boss, 0.16d);
            Write(hud, "_bossLineTimer", Read<double>(hud, "_bossLineDuration") - 0.4);
            boss.QueueRedraw();
            hud._Process(0);
            await Shot($"{scene}_break");
            BaseCall(boss, "EnterExposed");
            Check(boss.GetCollisionMaskValue(2), $"{scene}: exposed body still accepts shots");
            boss.QueueRedraw();
            await Shot($"{scene}_exposed");
            BaseCall(boss, "EnterReclose");
            BaseCall(boss, "EnterShielded");
            Check(BaseRead(boss, "_phase").ToString() == "Shielded" && !boss.GetCollisionMaskValue(2),
                $"{scene}: shield and original collision rules return");
            if (scene == "Rei")
            {
                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(540, 960) })
                {
                    DisplayServer.WindowSetSize(size);
                    hud.ShowBossLine("レイ", "切り抜かれたところだけじゃなくて、最後まで、わたしの話を聞いてよ。", new Color("c3a4fa"), 5);
                    Write(hud, "_bossLineTimer", 4d);
                    hud._Process(0);
                    await Shot($"callout_{size.X}");
                    hud.ShowRewardBanner(true, true);
                    hud.ShowClearBanner("STAGE 3 CLEAR", 123.45f, true, 150f, 123456789, false, 987654321);
                    Check(!Read<bool>(hud, "_bannerRewardLife") && !Read<bool>(hud, "_epic"),
                        "clear presentation replaces other banner modes");
                    Write(hud, "_bannerTimer", 4d);
                    Write(hud, "_bossLineTimer", 0d);
                    hud._Process(0);
                    await Shot($"clear_{size.X}");
                    Write(hud, "_bannerTimer", 0d);
                    hud.ShowDialog(Hud.LineKind.Boy, "消した言葉も、届いています。いっしょに帰ろう。");
                    hud.RevealDialogNow();
                    hud._Process(0);
                    Check(Read<bool>(hud, "_dlgDraftMark"), "your dialogue uses the anonymous emblem");
                    await Shot($"you_{size.X}");
                    hud.HideBubble();
                }
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                var imagery = root.GetNode<StageImagery>("Imagery");
                imagery.TriggerReversal();
                imagery._Process(12);
                await Shot("rei_subscribers_restored");
            }
            root.QueueFree();
            await Frames(5);
            Pool.DespawnAll();
            Hud.BubblePaused = false;
        }
        var cleared = Read<System.Collections.Generic.HashSet<string>>(game, "_cleared");
        foreach (var stage in GameManager.Stages) cleared.Add(stage.Id);
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        hub.SetProcess(false);
        Write(hub, "_sel", 0);
        Write(hub, "_t", 2d);
        Call(hub, "OpenJob");
        Write(hub, "_jobT", 1d);
        Check(Read<JobTuning[]>(hub, "_jobChoices").Length == 4, "all rescued accounts appear in the selector");
        hub.QueueRedraw();
        await Shot("sns_accounts");
        hub.QueueFree();
        await Frames(5);
    }

    private async Task CheckStartBanners(GameManager game)
    {
        string output = ProjectSettings.GlobalizePath("res://build/qa_story/stage_start");
        DirAccess.MakeDirRecursiveAbsolute(output);
        bool demo = OS.GetCmdlineUserArgs().Contains("--start-banner-demo");
        int number = 0;
        foreach (string scene in new[] { "Akari", "Koharu", "Rei" })
        {
            number++;
            game.SelectedEntry = GameManager.StageEntry.Start;
            game.AutoAdvanceDialog = false;
            var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
            stage.SetProcess(false);
            var hud = root.GetNode<Hud>("Hud");
            hud.SetProcess(false);
            root.GetNode<Node2D>("World").ProcessMode = ProcessModeEnum.Disabled;
            root.GetNode<StageBackground>("StageBackground").ProcessMode = ProcessModeEnum.Disabled;
            stage._Process(0);
            Check(Read<int>(hud, "_startStage") == number, $"{scene}: actual stage entry selects its start title");
            double duration = Read<double>(hud, "_bannerTimer");
            hud._Process(6);
            Check(Hud.BubblePaused && Read<double>(hud, "_bannerTimer") == duration,
                $"{scene}: intro dialogue does not consume the start animation");
            var canvas = Read<HudCanvas>(hud, "_canvas");
            var probe = new SubViewport { Size = new Vector2I(1280, 720), TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            root.AddChild(probe);
            probe.CanvasTransform = new Transform2D(0, Vector2.Zero).Scaled(Vector2.One / UiKit.Scale);
            var probeCanvas = new HudCanvas { Hud = hud };
            probe.AddChild(probeCanvas);
            async Task<Image> Render(double remaining)
            {
                Write(hud, "_bannerTimer", remaining);
                canvas.QueueRedraw();
                probeCanvas.QueueRedraw();
                await Frames(3);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                return probe.GetTexture().GetImage();
            }
            if (!demo)
            {
                using var pending = await Render(duration);
                using var hidden = await Render(0);
                Check(pending.GetData().SequenceEqual(hidden.GetData()), $"{scene}: no title overlaps dialogue");
            }
            hud.HoldBubble = false;
            hud.HideBubble();
            hud.SetCinematicMode(false);
            hud._Process(0);
            Write(hud, "_bannerTimer", duration);
            if (demo)
            {
                hud.SetProcess(true);
                await Frames(170);
            }
            else
            {
                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
                {
                    DisplayServer.WindowSetSize(size);
                    probe.Size = new Vector2I(size.X, Mathf.RoundToInt(size.X * 720f / 1280f));
                    probe.CanvasTransform = new Transform2D(0, Vector2.Zero).Scaled(Vector2.One * size.X / 384f);
                    await Frames(4);
                    using var blank = await Render(0);
                    foreach (double age in new[] { 0.16, 0.6, 1.2, 1.95 })
                    {
                        using var shown = await Render(duration - age);
                        float scale = shown.GetWidth() / UiKit.DesignW;
                        int changed = 0, outside = 0, white = 0;
                        for (int y = 0; y < shown.GetHeight(); y++)
                            for (int x = 0; x < shown.GetWidth(); x++)
                            {
                                Color a = blank.GetPixel(x, y), b = shown.GetPixel(x, y);
                                if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) < 0.06f) continue;
                                changed++;
                                if (x < (Field.DLeft + 70) * scale || x > (Field.DRight - 55) * scale
                                    || y < 125 * scale || y > 340 * scale) outside++;
                                if (b.R > 0.85f && b.G > 0.85f && b.B > 0.85f) white++;
                            }
                        Check(changed > 100 && outside == 0, $"{scene}/{size}/{age}: animated title remains above combat and inside the playfield");
                        if (age is 0.6 or 1.2)
                            Check(white > 180 * scale * scale, "high-contrast START lettering is visible");
                        using var actual = GetViewport().GetTexture().GetImage();
                        Check(actual.SavePng($"{output}/{scene}_{size.X}x{size.Y}_{age:F2}.png") == Error.Ok, "game viewport screenshot saved");
                    }
                }
                Write(hud, "_bannerTimer", 0.01d);
                hud._Process(0.02);
                Check(Read<double>(hud, "_bannerTimer") <= 0, "start title finishes without pausing combat");
                stage._Process(0);
                Check(Read<double>(hud, "_bannerTimer") <= 0, "stage entry title is not retriggered");
                hud.ShowRewardBanner(true, true);
                Check(Read<int>(hud, "_startStage") == 0 && Read<bool>(hud, "_bannerRewardLife"), "reward banner keeps its icon mode");
                hud.ShowStageStart(number, "test", Colors.White);
                hud.ShowClearBanner("CLEAR", 12f, false, null, 100, false, null);
                Check(Read<int>(hud, "_startStage") == 0 && Read<string>(hud, "_bannerTime").Length > 0, "clear results replace the start title");
                hud.ShowStageStart(number, "test", Colors.White);
                hud.ShowEpicBanner("FINAL", "まだ、いますか", UiKit.Kegare);
                Check(Read<int>(hud, "_startStage") == 0 && hud.EpicBannerActive, "FINAL retains its own cinematic title");
            }
            root.QueueFree();
            await Frames(6);
            Pool.DespawnAll();
            Hud.BubblePaused = false;
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
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
        // 開幕＝道中パノラマ（会話用の別層セットは無い。ステージ開始直後の会話もこの上で進む）。
        Check(Sprites(layers).Length == 3 && Sprites(layers).All(s => s.Texture.ResourcePath.Contains("/route/")),
            $"{id}: stage opens on the route panorama before any wave");
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
                Check(Sprites(layers).Length == 3 && Sprites(layers).All(s => s.Texture.ResourcePath.Contains("/route/")),
                    $"{id}/{wave}: completion keeps the route panorama behind the following dialogue");
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
            && art[0].Material == null, $"{id}: actual cameo replaces route with its dedicated room"
            + $" (live={string.Join(",", art.Select(s => s.Texture.ResourcePath + (s.Material == null ? "" : "+mat")))} paused={GetTree().Paused})");
        Check(layers.BossDimK == 0, $"{id}: midboss does not trigger main boss lighting");
        using (var pixels = art[0].Texture.GetImage())
            Check(pixels.GetWidth() >= 1200 && pixels.GetHeight() >= 1000 && pixels.DetectAlpha() == Image.AlphaMode.None,
                $"{id}: high-resolution opaque midboss painting");
        bg.BeginMidboss();
        await Frames(55);
        Check(Sprites(layers).Single() == art[0], $"{id}: repeated entry preserves active room");
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
        // 撃破後の会話も中ボスの部屋のまま（旧仕様の「会話用の場所へ戻す」は廃止。次の道中の BeginRoute で戻る）。
        Check(Sprites(layers).Single() == art[0], $"{id}: cameo completion keeps the midboss room behind the dialogue");
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
    // n フレーム待つ。ただし実時間でも n/60 秒は待つ：この QA の待ち幅（55 フレーム）は 60fps 前提で
    // 0.8 秒のクロスフェードを跨ぐ設計だが、vsync が 84Hz/143Hz のモニタでは 55 フレームが 0.4〜0.65 秒に
    // 縮んで層の入れ替えが終わる前に検査してしまう（遅い環境ではフレーム数、速い環境では実時間が効く）。
    private async Task Frames(int n)
    {
        double t = 0;
        for (int i = 0; i < n || t < n / 60.0; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += GetProcessDeltaTime();
        }
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
