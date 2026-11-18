using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class RoadProjectileQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    protected readonly List<object> _results = new();
    protected string _output = "";
    protected BulletPool _pool = null!;
    protected virtual System.Collections.Generic.IEnumerable<string> ModelNames => RoadProjectileArt.Names;
    private protected virtual ProjectileTurntable Model(string name) => RoadProjectileArt.Get(name);
    protected virtual bool IncludeFlatComparison => true;
    protected virtual string GalleryTitle => "道中弾 / 立体回転";
    protected virtual void ApplyModel(Bullet bullet, string name) => bullet.UseRoadAnimation(RoadProjectileArt.Get(name));
    protected static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print("[Road3D] PASS " + message);
    }
    protected virtual Texture2D Original(string name) => name is "akari_reply_plane" or "koharu_glow_arrow" or "koharu_prayer_tag" or "mina_folded_note"
        ? GD.Load<Texture2D>($"res://assets/fx/{name}.svg") : BulletArt.Get(name)!;
    protected static void Write(object obj, string name, object value) => typeof(Bullet).GetField(name, Private)!.SetValue(obj, value);
    protected static void Frame(Bullet bullet, int frame)
    {
        Write(bullet, "_age", 0f);
        Write(bullet, "_spriteSway", (frame + .25f) / RoadProjectileArt.FrameCount * Mathf.Tau);
        typeof(Bullet).GetMethod("UpdateModelAnimation", Private)!.Invoke(bullet, new object[] { true });
    }
    public override async void _Ready()
    {
        int exit = 0;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            var args = OS.GetCmdlineUserArgs();
            _output = ProjectSettings.GlobalizePath(args.First(a => a.StartsWith("--output="))[9..]);
            System.IO.Directory.CreateDirectory(_output);
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            var game = GameManager.Instance!;
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Lunatic;
            _pool = GetNode<BulletPool>("/root/Pool");
            Hud.BubblePaused = false;
            if (args.Contains("--gallery")) await Gallery();
            else if (args.Contains("--depth-preview")) await DepthPreview();
            else if (args.Contains("--visibility")) await Visibility();
            else if (args.Contains("--performance")) await PerformanceRun();
            else
            {
                CheckFrames();
                await CheckRuntime();
                await CheckRoster();
            }
            System.IO.File.WriteAllText(_output + "/results.json", JsonSerializer.Serialize(_results,
                new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("[Road3D] ALL PASS");
        }
        catch (Exception error) { exit = 1; GD.PushError("[Road3D] FAIL " + error); }
        _pool?.DespawnAll();
        Audio.Instance?.StopMusic(0);
        foreach (var node in GetNode<Audio>("/root/Audio").GetChildren())
            if (node is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        await Frames(3);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(exit);
    }

    private void CheckFrames()
    {
        var invalid = new List<string>();
        // Contours are sampled at 96 px; allow two source pixels at their edges.
        using var point = new CircleShape2D { Radius = 2f / RoadProjectileArt.CellSize };
        foreach (string name in ModelNames)
        {
            var art = Model(name);
            using var atlas = art.Texture.GetImage();
            if (atlas.IsCompressed()) atlas.Decompress();
            int samples = 0, maximumParts = 0;
            float minimumArea = float.MaxValue, maximumArea = 0;
            for (int frame = 0; frame < RoadProjectileArt.FrameCount; frame++)
            {
                var footprint = art.Footprint(frame, 1);
                if (footprint.Shapes.Length is < 1 or > 64 || footprint.Radius <= 0)
                {
                    invalid.Add($"{name}/{frame}: parts={footprint.Shapes.Length}, radius={footprint.Radius}");
                    using var failed = atlas.GetRegion((Rect2I)art.Frames[frame].Region);
                    failed.SavePng($"{_output}/{name}_{frame}_parts.png");
                }
                maximumParts = Math.Max(maximumParts, footprint.Shapes.Length);
                float area = footprint.Bounds.Size.X * footprint.Bounds.Size.Y;
                minimumArea = Math.Min(minimumArea, area);
                maximumArea = Math.Max(maximumArea, area);
                if (footprint.Radius > art.MaximumRadius + .0001f) throw new Exception("Collision envelope mismatch");
                var offset = (Vector2I)art.Frames[frame].Region.Position;
                for (int y = 5; y < 124; y += 6)
                for (int x = 5; x < 124; x += 6)
                {
                    bool solid = atlas.GetPixel(offset.X + x, offset.Y + y).A >= .6f;
                    bool stable = true;
                    for (int dy = -3; dy <= 3 && stable; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                        if ((atlas.GetPixel(offset.X + x + dx, offset.Y + y + dy).A >= .6f) != solid)
                        { stable = false; break; }
                    if (!stable) continue;
                    Vector2 at = new Vector2(x + .5f, y + .5f) / RoadProjectileArt.CellSize - Vector2.One * .5f;
                    bool hit = footprint.Shapes.Any(shape => shape.Collide(Transform2D.Identity, point, new Transform2D(0, at)));
                    if (hit != solid)
                    {
                        using var failedFrame = atlas.GetRegion((Rect2I)art.Frames[frame].Region);
                        failedFrame.SavePng(_output + "/mismatch.png");
                        System.IO.File.WriteAllText(_output + "/mismatch.json", JsonSerializer.Serialize(new { name, frame, x, y, solid, hit,
                            contours = footprint.Contours.Select(c => c.Select(p => new[] { p.X, p.Y })) }));
                        invalid.Add($"{name}/{frame}: alpha/native collision mismatch at {x},{y} solid={solid} hit={hit}");
                    }
                    samples++;
                }
            }
            Check(minimumArea > .0001f && minimumArea < maximumArea * .92f,
                name + ": all 48 projected frames remain visible and change silhouette");
            _results.Add(new { name, frames = RoadProjectileArt.FrameCount, native_alpha_samples = samples,
                contour_tolerance_pixels = 2, maximum_collision_parts = maximumParts,
                minimum_bounds_area = minimumArea, maximum_bounds_area = maximumArea });
        }
        Check(invalid.Count == 0, "collision shape limits: " + string.Join("; ", invalid));
    }

    protected virtual async Task CheckRuntime()
    {
        _pool.SetPhysicsProcess(false);
        foreach (var child in _pool.GetChildren()) child.SetPhysicsProcess(false);
        var target = new Area2D { Position = Field.PlayerStart, CollisionLayer = 1, CollisionMask = 8 };
        target.AddToGroup("player");
        target.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = Player.HitRadius } });
        AddChild(target);
        int contacts = 0;
        target.AreaEntered += _ => contacts++;
        var bullet = _pool.Spawn(Field.PlayerStart, Vector2.Zero, true, 3.4f);
        bullet.SetSprite(Original("enemy_akari_deadline"));
        float radius = bullet.CollisionRadius;
        bullet.UseRoadAnimation(RoadProjectileArt.Get("enemy_akari_deadline"));
        for (int frame = 0; frame < RoadProjectileArt.FrameCount; frame++)
        {
            Frame(bullet, frame);
            bullet.SyncEnemyCollision(target.Position);
            await PhysicsFrames(2);
            Check(bullet.ModelAnimationFrame == frame && bullet.CollisionRadius <= radius + .001f,
                "frame and collision envelope " + frame);
        }
        Check(contacts > 0, "rotating projectile participates in native overlaps");
        int frozen = bullet.ModelAnimationFrame;
        Hud.BubblePaused = true;
        bullet.StepEnemy(1, 1);
        Check(bullet.ModelAnimationFrame == frozen, "dialogue pause freezes 3D animation");
        Hud.BubblePaused = false;
        var owner = new Node2D();
        AddChild(owner);
        bullet.Position = Field.PlayerStart + new Vector2(50, 0);
        bullet.PrepareRoadShot(owner, RoadProjectileArt.Get("enemy_akari_deadline").Visual(Colors.White));
        Check(!bullet.GetChildren().OfType<Sprite2D>().Single().Visible && bullet.CollisionLayer == 0,
            "pending shot shows its warning without a live model or hitbox");
        bullet.StepEnemy(.5, 1);
        bullet.StepEnemy(.02, 1);
        Check(!bullet.RoadPending && bullet.GetChildren().OfType<Sprite2D>().Single().Visible && bullet.CollisionLayer == 8,
            "model and collision begin together after warning");
        _pool.Despawn(bullet);
        var reused = _pool.Spawn(Field.PlayerStart, Vector2.Zero, true, 3.4f);
        Check(ReferenceEquals(bullet, reused) && reused.ModelAnimationName == null
            && !reused.GetChildren().OfType<Sprite2D>().Single().Visible, "pool reuse clears the 3D frame");
        reused.UseCharacterProjectile(Job.Magic);
        Check(reused.ModelAnimationName == "rei_crystal", "boss projectile replaces the road model after pool reuse");
        _pool.DespawnAll();
        owner.QueueFree();
        target.QueueFree();
        _results.Add(new { runtime_passed = true, native_overlap_events = contacts, invincible = false,
            player = "non-damaging native physics test area" });
        await Frames(2);
    }

    protected virtual async Task CheckRoster()
    {
        var specs = (IEnumerable<EnemySpec>)typeof(EnemyProjectileQa).GetMethod("AllSpecs", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var world = new Node2D();
        AddChild(world);
        int checkedEnemies = 0;
        foreach (var spec in specs.Where(s => s.Pattern is not AttackPattern.None and not AttackPattern.BuzzWall)
            .DistinctBy(s => (s.Pattern, s.PreTexPath)))
        {
            var enemy = new MidEnemy();
            enemy.Configure(spec);
            world.AddChild(enemy);
            enemy.SetProcess(false);
            enemy.SetPhysicsProcess(false);
            var shot = (Bullet)typeof(MidEnemy).GetMethod("FireBullet", Private)!.Invoke(enemy,
                new object[] { _pool, new Vector2(220, 100), Vector2.Left * 40, 3.4f, 1, false })!;
            Check(shot.ModelAnimationName == enemy.RoadAnimation!.Name && shot.RoadPending,
                spec.Pattern + ": normal firing entry point uses the model and warning");
            enemy.QueueFree();
            _pool.DespawnAll();
            await Frames(1);
            checkedEnemies++;
        }
        world.QueueFree();
        _results.Add(new { normal_firing_sources = checkedEnemies });
    }

    private async Task PerformanceRun()
    {
        var target = new Node2D { Position = new Vector2(Field.Left + 40, 100) };
        target.AddToGroup("player");
        AddChild(target);
        var names = ModelNames.ToArray();
        foreach (string name in names) Model(name);
        bool compareOutline = OS.GetCmdlineUserArgs().Contains("--compare-outline");
        foreach (bool animated in IncludeFlatComparison || compareOutline ? new[] { false, true, false, true } : new[] { true, true })
        {
            var bullets = new Bullet[512];
            GD.Seed(719);
            for (int i = 0; i < bullets.Length; i++)
            {
                var at = new Vector2(Field.Left + 10 + i % 32 * 7.5f, Field.Top + 8 + i / 32 * 12);
                var bullet = _pool.Spawn(at, Vector2.Left * 24, true, 3.4f);
                bullet.SetSprite(Original(names[i % names.Length]), 24);
                if (animated || compareOutline) ApplyModel(bullet, names[i % names.Length]);
                if (compareOutline) bullet.GetChildren().OfType<Sprite2D>().Single().UseParentMaterial = !animated;
                bullets[i] = bullet;
            }
            var timings = new List<double>();
            ulong previous = Time.GetTicksUsec();
            for (int frame = 0; frame < 720; frame++)
            {
                foreach (var bullet in bullets)
                    if (bullet.Position.X < Field.Left + 8) bullet.Position = new Vector2(Field.Right - 8, bullet.Position.Y);
                await Frames(1);
                ulong now = Time.GetTicksUsec();
                if (frame >= 180) timings.Add((now - previous) / 1000d);
                previous = now;
            }
            timings.Sort();
            Check(bullets.All(b => b.Active), "512 projectile load remains active");
            _results.Add(new { mode = compareOutline ? animated ? "outlined_turntable" : "unoutlined_turntable"
                    : animated ? "blender_turntable" : "original_flat_rotation", count = bullets.Length,
                mean_ms = timings.Average(), p95_ms = timings[(int)(timings.Count * .95)], maximum_ms = timings[^1] });
            _pool.DespawnAll();
            await Frames(5);
        }
        target.QueueFree();
    }

    private async Task DepthPreview()
    {
        string name = OS.GetCmdlineUserArgs().First(arg => arg.StartsWith("--model="))[8..];
        var model = Model(name);
        _pool.SetPhysicsProcess(false);
        foreach (var child in _pool.GetChildren()) child.SetPhysicsProcess(false);
        var root = GD.Load<PackedScene>("res://Rei.tscn").Instantiate<Node2D>();
        AddChild(root);
        root.SetProcess(false);
        ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
        var player = root.GetNode<Player>("World/Player");
        player.SetPhysicsProcess(false);
        player.Hide();
        var hud = root.GetNode<Hud>("Hud");
        hud.HoldBubble = false;
        hud.HideBubble();
        Hud.BubblePaused = false;
        root.GetNode<StageBackground>("StageBackground").EnterBoss();
        await Frames(160);
        root.ProcessMode = ProcessModeEnum.Disabled;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var background = GetViewport().GetTexture().GetImage();
        var backdrop = ImageTexture.CreateFromImage(background);
        root.Hide();
        hud.Hide();
        var canvas = new Node2D { ZIndex = 40, ZAsRelative = false };
        AddChild(canvas);
        canvas.Draw += () => {
            canvas.DrawRect(new Rect2(0, 0, 384, 216), new Color("111827"));
            canvas.DrawString(UiKit.ZenBold, new Vector2(12, 15), "レイのフィルム弾 / 立体感の比較", fontSize: 10);
            Vector2 ratio = background.GetSize() / new Vector2(384, 216);
            for (int col = 0; col < 2; col++)
            {
                canvas.DrawTextureRectRegion(backdrop, new Rect2(8 + col * 192, 40, 176, 170),
                    new Rect2(new Vector2(70, 30) * ratio, new Vector2(220, 170) * ratio));
                canvas.DrawString(UiKit.ZenBold, new Vector2(12 + col * 192, 33), col == 0 ? "現在 / 見やすさ補正" : "試作 / 陰影を残す補正", fontSize: 8);
                canvas.DrawString(UiKit.ZenBold, new Vector2(14 + col * 192, 56), "ゲーム内サイズ", fontSize: 7);
                canvas.DrawString(UiKit.ZenBold, new Vector2(14 + col * 192, 120), "4倍拡大", fontSize: 7);
            }
        };
        var bullets = new List<(Bullet Bullet, int Pose)>();
        for (int col = 0; col < 2; col++)
        for (int sample = 0; sample < 4; sample++)
        {
            bool enlarged = sample == 3;
            Vector2 at = enlarged ? new Vector2(96 + col * 192, 165) : new Vector2(48 + sample * 48 + col * 192, 84);
            var bullet = _pool.SpawnBossShot(at, Vector2.Right, Job.Magic, false);
            ApplyModel(bullet, name);
            bullet.Rotation = 0;
            bullet.ZIndex = 50;
            bullet.ZAsRelative = false;
            var sprite = bullet.GetChildren().OfType<Sprite2D>().Single();
            sprite.Material = col == 0 ? ProjectileTurntable.Material : model.DrawMaterial;
            if (enlarged)
            {
                bullet.Scale = Vector2.One * 4;
                var zoomMaterial = (ShaderMaterial)sprite.Material.Duplicate();
                zoomMaterial.SetShaderParameter("rim_width", (col == 0 ? new Vector2(.45f, .85f) : new Vector2(.28f, .65f)) * 4);
                sprite.Material = zoomMaterial;
            }
            bullets.Add((bullet, sample == 3 ? 0 : sample * 16));
        }
        for (int frame = 0; frame < 144; frame++)
        {
            foreach (var item in bullets) Frame(item.Bullet, (frame / 3 + item.Pose) % 48);
            await Frames(1);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screen = GetViewport().GetTexture().GetImage();
            Check(screen.SavePng($"{_output}/frame_{frame:000}.png") == Error.Ok, "depth preview frame");
        }
        foreach (var item in bullets) item.Bullet.Scale = Vector2.One;
        _pool.DespawnAll();
        canvas.QueueFree();
        root.QueueFree();
        await Frames(3);
        _results.Add(new { depth_preview = name, rotation_frames = 48, same_background = true,
            magnification = 4, physics_disabled = true, human_playtest = false });
    }

    private async Task Visibility()
    {
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        _pool.SetPhysicsProcess(false);
        foreach (var child in _pool.GetChildren()) child.SetPhysicsProcess(false);
        foreach (string character in new[] { "akari", "koharu", "rei", "mina" })
        {
            string scene = character == "mina" ? "MinaBattle" : char.ToUpperInvariant(character[0]) + character[1..];
            var names = ModelNames.Where(name => name.Contains(character)).ToArray();
            if (names.Length == 0) continue;
            var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
            AddChild(root);
            root.SetProcess(false);
            ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
            var player = root.GetNode<Player>("World/Player");
            player.SetPhysicsProcess(false);
            player.Hide();
            var hud = root.GetNode<Hud>("Hud");
            hud.HoldBubble = false;
            hud.HideBubble();
            Hud.BubblePaused = false;
            var background = root.GetNode<StageBackground>("StageBackground");
            var canvas = new Node2D { ZIndex = 100, ZAsRelative = false };
            AddChild(canvas);
            string backgroundName = "";
            canvas.Draw += () => {
                canvas.DrawRect(new Rect2(66, 0, 318, 16), new Color("111827"));
                canvas.DrawString(UiKit.ZenBold, new Vector2(70, 11),
                    $"{character} / {backgroundName} / {(IncludeFlatComparison ? "道中" : "ボス")}弾・実寸", fontSize: 7);
                for (int i = 0; i < names.Length; i++)
                {
                    Vector2 at = new(98 + i % 4 * 77, 40 + i / 4 * 44);
                    canvas.DrawString(UiKit.Mono, at + new Vector2(-28, 13), names[i].Replace("enemy_", "").Replace(character + "_", ""), fontSize: 4);
                }
            };
            var bullets = new List<Bullet>();
            var darkness = new int[names.Length];
            var thinness = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var model = Model(names[i]);
                using var atlas = model.Texture.GetImage();
                if (atlas.IsCompressed()) atlas.Decompress();
                atlas.Convert(Image.Format.Rgba8);
                var pixels = atlas.GetData();
                var luminance = new double[ProjectileTurntable.FrameCount];
                var areas = new int[ProjectileTurntable.FrameCount];
                for (int f = 0; f < ProjectileTurntable.FrameCount; f++)
                {
                    int ox = f % 8 * 128, oy = f / 8 * 128;
                    for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        int offset = ((oy + y) * atlas.GetWidth() + ox + x) * 4;
                        if (pixels[offset + 3] < 153) continue;
                        areas[f]++;
                        luminance[f] += (.2126 * pixels[offset] + .7152 * pixels[offset + 1] + .0722 * pixels[offset + 2]) / 255;
                    }
                    luminance[f] /= areas[f];
                }
                darkness[i] = Array.IndexOf(luminance, luminance.Min());
                thinness[i] = Array.IndexOf(areas, areas.Min());
                _results.Add(new { name = names[i], darkest_frame = darkness[i], thinnest_frame = thinness[i],
                    minimum_source_luminance = luminance.Min(), minimum_opaque_pixels = areas.Min() });
                for (int copy = 0; copy < 2; copy++)
                {
                    Vector2 position = new(88 + copy * 20 + i % 4 * 77, 40 + i / 4 * 44);
                    var bullet = IncludeFlatComparison ? _pool.Spawn(position, Vector2.Zero, true, 3.4f)
                        : _pool.SpawnBossShot(position, Vector2.Right,
                            character switch { "akari" => Job.Melee, "koharu" => Job.Heal, "rei" => Job.Magic, _ => Job.Tank }, false);
                    if (IncludeFlatComparison) bullet.SetSprite(Original(names[i]), 0);
                    ApplyModel(bullet, names[i]);
                    bullet.Rotation = 0;
                    bullet.ZIndex = 50;
                    bullet.ZAsRelative = false;
                    bullets.Add(bullet);
                }
            }
            foreach (int chapter in IncludeFlatComparison ? new[] { 0, 2, 3 } : new[] { 4 })
            {
                root.ProcessMode = ProcessModeEnum.Inherit;
                backgroundName = chapter == 4 ? "boss" : "route" + chapter;
                if (chapter == 4) background.EnterBoss();
                else typeof(StageBackground).GetMethod("SetJourney", Private)!.Invoke(background, new object[] { chapter + .5f });
                await Frames(160);
                root.ProcessMode = ProcessModeEnum.Disabled;
                canvas.QueueRedraw();
                for (int f = 0; f < 50; f++)
                {
                    for (int i = 0; i < bullets.Count; i++)
                    {
                        int pose = f == 48 ? darkness[i / 2] : f == 49 ? thinness[i / 2] : (f + i % 2 * 12) % 48;
                        Frame(bullets[i], pose);
                    }
                    await Frames(2);
                    if (f is 0 or 12 or 24 or 36 or 48 or 49)
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using var screen = GetViewport().GetTexture().GetImage();
                        string pose = f == 48 ? "darkest" : f == 49 ? "thinnest" : f.ToString();
                        Check(screen.SavePng($"{_output}/{character}_{backgroundName}_{pose}.png") == Error.Ok, "visibility capture");
                    }
                }
            }
            _pool.DespawnAll();
            canvas.QueueFree();
            root.QueueFree();
            await Frames(5);
        }
        _results.Add(new { visibility_fixture = true, physics_disabled = true, actual_projectile_sizes = true,
            pose_frames = 48, human_playtest = false });
    }

    private async Task Gallery()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        var names = ModelNames.ToArray();
        int columns = names.Length > 20 ? 7 : 5;
        int rows = (names.Length + columns - 1) / columns;
        float cellWidth = 1240f / columns, cellHeight = 650f / rows;
        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        var canvas = new Node2D();
        layer.AddChild(canvas);
        canvas.Scale = GetViewport().GetVisibleRect().Size / new Vector2(1280, 720);
        canvas.Draw += () =>
        {
            canvas.DrawRect(new Rect2(0, 0, 1280, 720), new Color("121827"));
            canvas.DrawString(UiKit.ZenBold, new Vector2(28, 38), GalleryTitle, fontSize: 25, modulate: new Color("f1ead9"));
            for (int index = 0; index < names.Length; index++)
            {
                var at = new Vector2(20 + index % columns * cellWidth, 58 + index / columns * cellHeight);
                canvas.DrawRect(new Rect2(at, new Vector2(cellWidth - 12, cellHeight - 9)), new Color("202b3c"));
                canvas.DrawString(UiKit.Mono, at + new Vector2(8, cellHeight - 20), names[index], fontSize: names.Length > 20 ? 10 : 12, modulate: new Color("d4dae3"));
            }
        };
        var sprites = new List<Sprite2D>();
        for (int index = 0; index < names.Length; index++)
        {
            var art = Model(names[index]);
            var at = new Vector2(20 + (index % columns + .5f) * cellWidth, 58 + (index / columns + .5f) * cellHeight - 12);
            var sprite = new Sprite2D { Texture = art.Texture, Hframes = 8, Vframes = 6, Position = at,
                Scale = Vector2.One * Math.Min(128, cellHeight - 32) / 128f, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
            canvas.AddChild(sprite);
            sprites.Add(sprite);
        }
        for (int frame = 0; frame < 600; frame++)
        {
            foreach (var sprite in sprites) sprite.Frame = frame * RoadProjectileArt.FrameCount / 180 % RoadProjectileArt.FrameCount;
            await Frames(1);
            if (frame is 1 or 45 or 90)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng($"{_output}/gallery_{frame}.png") == Error.Ok, "gallery capture");
            }
        }
        _results.Add(new { gallery_models = names.Length, frames = 600, seconds_per_rotation = 3, collision_test = false });
    }

    protected async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    protected async Task PhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
