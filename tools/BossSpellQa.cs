using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossSpellQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value)
        => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static void Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[BossSpellQA] PASS {message}");
    }
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private Bullet[] Bullets() => Pool.GetChildren().OfType<Bullet>().Where(b => b.Active && b.IsEnemy).ToArray();

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
            if (OS.GetCmdlineUserArgs().Contains("--aoe-pixels"))
            {
                await CheckSafeZonePixels();
                GD.Print("[BossSpellQA] PIXELS ALL PASS");
                GetTree().Quit();
                return;
            }
            foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
            {
                game.Difficulty = GameManager.Diff.Normal;
                var root = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                var stage = (Node)root.GetType().GetProperty("Stage")!.GetValue(root)!;
                stage.SetProcess(false);
                var world = root.GetNode<Node2D>("World");
                world.ProcessMode = ProcessModeEnum.Inherit;
                var player = world.GetNode<Player>("Player");
                player.SetPhysicsProcess(false);
                Write(player, "_invincible", true);
                Write(player, "_invincibleTimer", 999f);
                player.GlobalPosition = new Vector2(Field.Left + 50f, 160f);
                var hud = root.GetNode<Hud>("Hud");
                hud.HoldBubble = false;
                hud.HideBubble();
                Enemy boss = scene switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(), "Rei" => new BossRei(), _ => new BossMina() };
                world.AddChild(boss);
                boss.GlobalPosition = new Vector2(Field.Right - 70f, 90f);
                await Frames(250);
                boss.SetPhysicsProcess(false);
                var caster = Read<AreaSpellCaster>(boss, "_caster");
                caster.SetProcess(false);
                caster.CancelPendingAttacks();
                await ClearStrikes(world);
                Pool.DespawnAll();
                if (scene == "MinaBattle") await CheckMina(game, (BossMina)boss, caster, hud, world);
                else if (scene != "Akari") await CheckTelegraphs(game, scene, caster, hud, world, player);
                await CheckAreaPresentation(game, scene, boss, caster, hud, world);
                root.QueueFree();
                await Frames(5);
                Pool.DespawnAll();
                Hud.BubblePaused = false;
            }
            await CheckSafeZonePixels();
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[BossSpellQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[BossSpellQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task CheckTelegraphs(GameManager game, string scene, AreaSpellCaster caster, Hud hud, Node2D world, Player player)
    {
        var spells = Read<(string, AreaStrike.Shape?)[]>(caster, "_spells");
        Check(spells.All(s => !s.Item1.Contains("選考") && !s.Item1.Contains("包丁")), $"{scene}: current scenario spell names");
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            if (scene == "Koharu")
            {
                Write(caster, "_pendShape", AreaStrike.Shape.BeamSeg);
                Call(caster, "SpawnTelegraphs");
                var lines = world.GetChildren().OfType<AreaStrike>().ToArray();
                int count = diff switch { GameManager.Diff.Easy => 1, GameManager.Diff.Hard => 3, GameManager.Diff.Lunatic => 5, _ => 2 };
                Check(lines.Length == count && lines.All(z => Read<AreaStrike.Art>(z, "_art") == AreaStrike.Art.ReadReceipt
                    && Read<float>(z, "_segLen") == 460f && Read<float>(z, "_hh") == 6f),
                    $"Koharu/{diff}: read-receipt art keeps beam count and dimensions");
                Check(lines[0].CoversPoint(player.GlobalPosition) && !lines[0].IsStriking
                    && Math.Abs(Read<double>(lines[0], "_warn") - 1.2 * caster.WarnMul()) < 0.001,
                    "read line retains player anchoring and warning time");
                if (diff == GameManager.Diff.Normal)
                {
                    hud.AnnounceSpell("こはる", "@koharu_light", "既読の線", new Color("d6443f"));
                    foreach (var z in lines) { z.SetProcess(false); z._Process(0.85d); }
                    await Shot("koharu_read_line");
                    DisplayServer.WindowSetSize(new Vector2I(960, 540));
                    await Frames(2);
                    await Shot("koharu_read_line_small");
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                    foreach (var z in lines) z._Process(Read<double>(z, "_warn") - Read<double>(z, "_t") + 0.1d);
                    Check(lines.All(z => z.IsStriking), "read-receipt strike still activates");
                    await Shot("koharu_read_line_strike");
                }
                await ClearStrikes(world);
            }
            else
            {
                int hops = diff switch { GameManager.Diff.Easy => 2, GameManager.Diff.Lunatic => 4, _ => 3 };
                caster.CastFullscreenChain(hops, 140f, 190f);
                Check(Read<string>(hud, "_spellName") == "最後まで見てて", $"Rei/{diff}: relay uses personal plea");
                for (int i = 0; i < hops; i++)
                {
                    Call(caster, "SpawnChainHop");
                    var strike = world.GetChildren().OfType<AreaStrike>().Single();
                    Check(!Read<string>(hud, "_spellName").Contains("選考")
                        && Read<float>(strike, "_safeR") == 30f
                        && !strike.CoversPoint(Read<Vector2>(strike, "_safeCenter")), "relay preserves safe zone and personal dialogue");
                    if (diff == GameManager.Diff.Normal)
                    {
                        strike.SetProcess(false);
                        strike._Process(0.7d);
                        await Shot($"rei_relay_{i}");
                    }
                    await ClearStrikes(world);
                }
                caster.CancelPendingAttacks();
                Check(!caster.AoeActive, "relay gate clears after final hop");
            }
        }
        game.Difficulty = GameManager.Diff.Normal;
        var shape = scene == "Koharu" ? AreaStrike.Shape.Circle : AreaStrike.Shape.Rect;
        Write(caster, "_pendShape", shape);
        Call(caster, "SpawnTelegraphs");
        var zones = world.GetChildren().OfType<AreaStrike>().ToArray();
        Check(zones.All(z => Read<AreaStrike.Motif>(z, "_motif") == (scene == "Koharu" ? AreaStrike.Motif.Screen : AreaStrike.Motif.Stream)),
            $"{scene}: screen/comment motif replaces the old setting");
        foreach (var z in zones) { z.SetProcess(false); z._Process(0.8d); }
        hud.AnnounceSpell(scene == "Koharu" ? "こはる" : "レイ", "", scene == "Koharu" ? "画面の光" : "配信枠", Colors.Gold);
        await Shot($"{scene.ToLowerInvariant()}_motif");
        hud.HoldBubble = true;
        hud.ShowDialog(Hud.LineKind.Mina, "Pause QA", "res://char/mina_face.png");
        await Frames(3);
        double time = Read<double>(zones[0], "_t");
        zones[0]._Process(1d);
        Check(Read<double>(zones[0], "_t") == time, "dialogue freezes themed area attacks");
        hud.HoldBubble = false;
        hud.HideBubble();
        await Frames(3);
        await ClearStrikes(world);
    }

    private async Task CheckMina(GameManager game, BossMina boss, AreaSpellCaster caster, Hud hud, Node2D world)
    {
        Check(Read<(string, AreaStrike.Shape?)[]>(caster, "_spells").Select(s => s.Item1)
            .SequenceEqual(new[] { "消せなかった声", "声が、止まらない" }), "Mina random spells express the voices she carries");
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            for (int pattern = 0; pattern < 5; pattern++)
            {
                Pool.DespawnAll();
                Write(boss, "_pattern", pattern);
                Call(boss, "ApplySpell");
                Write(boss, "_fireT", 100d);
                Call(boss, "FirePattern", 0d);
                var bullets = Bullets();
                int count = pattern switch
                {
                    0 => game.ScaleBullets(Read<int>(boss, "_ringCount")),
                    1 => Read<int>(boss, "_aimedWing") * 2 + 1,
                    2 => game.ScaleBullets(Read<int>(boss, "_flowerPetals")) * 2,
                    3 => 3,
                    _ => game.ScaleBullets(22) * 2,
                };
                Check(bullets.Length == count && bullets.All(b => Read<Texture2D?>(b, "_sprite") != null
                    && b.Damage == 1 && !b.Homing && !b.Accel && !b.Erasable), $"Mina/{diff}/{pattern}: illustrated attack preserves count and damage");
                var expectedArt = Read<Texture2D?[][]>(boss, "_spellArt")[pattern];
                Check(bullets.Select(b => Read<Texture2D>(b, "_sprite").ResourcePath).Distinct().OrderBy(p => p)
                    .SequenceEqual(expectedArt.Select(t => t!.ResourcePath).OrderBy(p => p)), "memory attack includes every assigned character illustration");
                foreach (var b in bullets)
                {
                    float speed = b.Velocity.Length() / game.BulletSpeedMul;
                    bool valid = pattern switch
                    {
                        0 => Mathf.IsEqualApprox(speed, Read<float>(boss, "_ringSpeed")) && b.Radius == 3f,
                        1 => Mathf.IsEqualApprox(speed, Read<float>(boss, "_aimedSpeed")) && b.Radius == 4f,
                        2 => (Mathf.IsEqualApprox(speed, 64f) && b.Radius == 4f) || (Mathf.IsEqualApprox(speed, 100f) && b.Radius == 2.6f),
                        3 => Mathf.IsEqualApprox(speed, Read<float>(boss, "_spiralSpeed")) && b.Radius == 2.6f,
                        _ => (Mathf.IsEqualApprox(speed, 66f) || Mathf.IsEqualApprox(speed, 92f)) && b.Radius == 3f,
                    };
                    Check(valid, "memory bullet keeps its speed and radius");
                }
                if (diff == GameManager.Diff.Normal)
                {
                    foreach (var b in bullets) { b._PhysicsProcess(0.5d); b.SetPhysicsProcess(false); }
                    await Shot($"mina_memory_{pattern}");
                }
            }
            Pool.DespawnAll();
            Write(boss, "_fireT", 100d);
            Write(boss, "_fireT2", 100d);
            Call(boss, "FireFinale", Pool, 0d);
            Check(Bullets().Length == game.ScaleBullets(18) + 3
                && Bullets().Count(b => b.Shape == BulletShape.Ring && Read<Texture2D>(b, "_sprite").ResourcePath.Contains("mina_core")) == 3,
                $"Mina/{diff}: finale now fires the announced heart-core art and shape");
            if (diff == GameManager.Diff.Normal)
            {
                hud.AnnounceSpell("ミナ", "@mina_ai_", "心象の核＋世界中の悲鳴", new Color("e0729c"));
                foreach (var b in Bullets()) { b._PhysicsProcess(0.5d); b.SetPhysicsProcess(false); }
                await Shot("mina_finale");
                DisplayServer.WindowSetSize(new Vector2I(960, 540));
                await Frames(2);
                await Shot("mina_finale_small");
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            Pool.DespawnAll();
            foreach (bool wide in new[] { true, false })
            {
                caster.CastFullscreen(wide);
                Check(Read<string>(hud, "_spellName") == (wide ? "この重さは、わたくしが" : "あなたまで、穢したくない"), "Mina AOE uses her own words");
                Call(caster, "TickFullscreen", 1d);
                var z = world.GetChildren().OfType<AreaStrike>().Single();
                float radius = wide ? 30f : diff switch { GameManager.Diff.Easy => 30f, GameManager.Diff.Hard => 20f, GameManager.Diff.Lunatic => 17f, _ => 24f };
                Check(Read<float>(z, "_safeR") == radius && !z.CoversPoint(Read<Vector2>(z, "_safeCenter")), "Mina AOE keeps the existing safe radius");
                await ClearStrikes(world);
            }
            caster.CastFullscreenChain(2, 140f, 190f);
            Check(Read<string>(hud, "_spellName") == "まだ、抱えられます", "Mina relay has her own declaration");
            for (int i = 0; i < 2; i++)
            {
                Call(caster, "SpawnChainHop");
                Check(Read<string>(hud, "_spellName") == "まだ、抱えられます", "Mina relay does not inherit Rei dialogue");
                await ClearStrikes(world);
            }
            caster.CancelPendingAttacks();
        }
        var reused = Pool.Spawn(new Vector2(Field.CenterX, 100f), Vector2.Left * 30f, true);
        Check(Read<Texture2D?>(reused, "_sprite") == null, "pool reuse clears memory art");
        Pool.DespawnAll();
    }

    private async Task CheckAreaPresentation(GameManager game, string scene, Enemy boss, AreaSpellCaster caster, Hud hud, Node2D world)
    {
        game.Difficulty = GameManager.Diff.Normal;
        hud.HideBubble();
        Write(hud, "_bannerTimer", 0d);
        var motif = scene switch { "Akari" => AreaStrike.Motif.Rain, "Koharu" => AreaStrike.Motif.Screen,
            "Rei" => AreaStrike.Motif.Stream, _ => AreaStrike.Motif.Data };
        var shapes = scene switch
        {
            "Akari" => new[] { AreaStrike.Shape.BeamV, AreaStrike.Shape.Circle },
            "Koharu" => new[] { AreaStrike.Shape.Circle, AreaStrike.Shape.Rect, AreaStrike.Shape.BeamSeg },
            "Rei" => new[] { AreaStrike.Shape.BeamH, AreaStrike.Shape.Rect, AreaStrike.Shape.Fullscreen },
            _ => new[] { AreaStrike.Shape.Circle, AreaStrike.Shape.Rect, AreaStrike.Shape.BeamH, AreaStrike.Shape.BeamV, AreaStrike.Shape.Fullscreen },
        };
        foreach (var shape in shapes)
        {
            await ClearStrikes(world);
            Pool.DespawnAll();
            Vector2 center = new(Field.CenterX - 20, 130);
            switch (shape)
            {
                case AreaStrike.Shape.Fullscreen:
                    Call(caster, "SpawnFullscreenStrike", new Vector2(Field.Left + 78, 118), 30f, 1f);
                    break;
                case AreaStrike.Shape.BeamSeg:
                    Call(caster, "SpawnReadLine", center, -26f, 1.2d);
                    break;
                default:
                    Vector2 half = shape switch { AreaStrike.Shape.BeamH => new(Field.Width / 2, 7),
                        AreaStrike.Shape.BeamV => new(7, Field.Height / 2), AreaStrike.Shape.Circle => new(24, 24), _ => new(28, 23) };
                    if (shape == AreaStrike.Shape.BeamH) center.X = Field.CenterX;
                    if (shape == AreaStrike.Shape.BeamV) center.Y = Field.CenterY;
                    Call(caster, "AddStrike", shape, center, half.X, half.Y, 1.2d);
                    break;
            }
            var zone = world.GetChildren().OfType<AreaStrike>().Single();
            zone.SetProcess(false);
            Check(Read<AreaStrike.Motif>(zone, "_motif") == motif, $"{scene}/{shape}: correct boss motif is wired");
            var art = Read<Texture2D[]>(zone, "_motifArt");
            Check(art.Length >= 2 && art.All(t => t.GetWidth() > 0 && t.GetHeight() > 0), "all motif illustrations load");
            double warn = Read<double>(zone, "_warn");
            Vector2 probe = shape == AreaStrike.Shape.Fullscreen ? new Vector2(Field.Left + 3, 3) : center;
            Check(zone.CoversPoint(probe) && !zone.IsStriking, "warning preserves coverage without early impact");
            string label = shape switch
            {
                AreaStrike.Shape.Fullscreen => scene == "Rei" ? "最後まで見てて" : "あなたまで、穢したくない",
                AreaStrike.Shape.BeamSeg => "既読の線",
                _ => Read<(string, AreaStrike.Shape?)[]>(caster, "_spells").FirstOrDefault(s => s.Item2 == shape).Item1
                    ?? (scene == "MinaBattle" ? "声が、止まらない" : "画面の光"),
            };
            hud.AnnounceSpell(scene == "Akari" ? "あかり" : scene == "Koharu" ? "こはる" : scene == "Rei" ? "レイ" : "ミナ",
                "", label, Read<Color>(zone, "_tint"));
            zone._Process(warn * 0.15);
            await Shot($"aoe_{scene}_{shape}_early");
            zone._Process(warn * 0.7);
            Check(!zone.IsStriking, "illustrated charge does not activate early");
            await Shot($"aoe_{scene}_{shape}_ready");
            if (shape == shapes.Last())
            {
                DisplayServer.WindowSetSize(new Vector2I(960, 540));
                await Shot($"aoe_{scene}_{shape}_small");
                DisplayServer.WindowSetSize(new Vector2I(540, 960));
                await Shot($"aoe_{scene}_{shape}_portrait");
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            double pausedAt = Read<double>(zone, "_t");
            hud.HoldBubble = true;
            hud.ShowDialog(Hud.LineKind.Mina, "……まだ、ここにいます。", "res://char/mina_worried.png");
            zone._Process(10d);
            Check(Read<double>(zone, "_t") == pausedAt, "dialogue never advances the AOE countdown");
            hud.HideBubble();
            hud.HoldBubble = false;
            zone._Process(warn - pausedAt + 0.035);
            Check(zone.IsStriking && zone.CoversPoint(probe), "themed impact retains its original hit region");
            await Shot($"aoe_{scene}_{shape}_impact");
            zone._Process(0.2d);
            await Frames(2);
            Check(!IsInstanceValid(zone), "impact still expires after 0.2 seconds");
        }
        if (boss is BossKoharu)
        {
            Write(boss, "_gotoCenter", new Vector2(Field.Left + 80, 130));
            Call(boss, "SpawnGotoAxisCross", 1.2d);
            Call(boss, "SpawnGotoDiagCross", 1.2d);
            var cross = world.GetChildren().OfType<AreaStrike>().ToArray();
            Check(cross.Length == 4 && cross.All(z => Read<AreaStrike.Motif>(z, "_motif") == AreaStrike.Motif.Screen),
                "both stages of Koharu's cross attack use the screen motif");
            foreach (var z in cross) { z.SetProcess(false); z._Process(0.8d); }
            hud.AnnounceSpell("こはる", "@koharu_light", "自分なにしてんだろ", new Color("e8945a"));
            await Shot("aoe_Koharu_cross");
            await ClearStrikes(world);
        }
        if (boss is BossAkari) await CheckCorridor(game, boss, hud, world);
        var owner = new Node2D();
        world.AddChild(owner);
        var cancelled = new AreaStrike();
        cancelled.Configure(AreaStrike.Shape.Circle, 24, 24, 1.2, Colors.Red, Colors.White, motif);
        cancelled.SetOwner(owner);
        world.AddChild(cancelled);
        owner.QueueFree();
        await Frames(5);
        Check(!IsInstanceValid(cancelled), "losing the caster cancels illustrated AOE");
        caster.CancelPendingAttacks();
    }

    private async Task CheckCorridor(GameManager game, Enemy boss, Hud hud, Node2D world)
    {
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            var corridor = new CorridorRun { Boss = boss };
            world.AddChild(corridor);
            corridor.SetPhysicsProcess(false);
            float gap = diff switch { GameManager.Diff.Easy => 64, GameManager.Diff.Hard => 42, GameManager.Diff.Lunatic => 34, _ => 52 };
            float speed = diff switch { GameManager.Diff.Easy => 60, GameManager.Diff.Hard => 80, GameManager.Diff.Lunatic => 90, _ => 70 };
            Check(Read<float>(corridor, "_gap") == gap && corridor.ScrollSpeed == speed,
                $"Akari corridor/{diff}: original gap and speed retained");
            Check(Read<Texture2D>(corridor, "_letterTex").GetWidth() > 0, "corridor uses the same unsent-letter illustration");
            corridor._PhysicsProcess(1.48d);
            Check(!corridor.RunActive, "corridor preview stays harmless for 1.5 seconds");
            if (diff == GameManager.Diff.Normal)
            {
                hud.AnnounceSpell("あかり", "@akari_ame", "雨の帰り道", new Color("6c9cd8"));
                await Shot("aoe_Akari_corridor_preview");
            }
            corridor._PhysicsProcess(3.02d);
            Check(corridor.RunActive && !corridor.Finished, "corridor activates on its original schedule");
            for (float x = Field.Left + 12; x < Field.Right; x += 12)
                Check(!corridor.CoversPoint(new Vector2(x, corridor.GuideYAt(x))), "corridor centerline stays safe");
            if (diff == GameManager.Diff.Normal) await Shot("aoe_Akari_corridor_active");
            hud.ShowDialog(Hud.LineKind.Mina, "……少し、待ってください。", "res://char/mina_worried.png");
            double t = Read<double>(corridor, "_t");
            corridor._PhysicsProcess(1d);
            Check(Read<double>(corridor, "_t") == t, "dialogue freezes the illustrated corridor");
            hud.HideBubble();
            corridor._PhysicsProcess(9.01d);
            Check(corridor.Finished, "corridor still ends after the 12-second run");
            corridor.QueueFree();
            foreach (var remnant in world.GetChildren().OfType<RemnantEnemy>()) remnant.QueueFree();
            await Frames(3);
        }
        game.Difficulty = GameManager.Diff.Normal;
    }

    private async Task CheckSafeZonePixels()
    {
        var viewport = new SubViewport { Size = new Vector2I(384, 216), TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        viewport.AddChild(new ColorRect { Size = new Vector2(384, 216), Color = new Color("102030") });
        foreach (float radius in new[] { 17f, 20f, 24f, 30f })
        foreach (var center in new[] { new Vector2(180, 108), new Vector2(Field.Right - radius - 14, radius + 14) })
        {
            var zone = new AreaStrike();
            zone.ConfigureFullscreen(center, radius, 1.6, new Color("e072ac"), new Color("ff8cc4"));
            viewport.AddChild(zone);
            zone.SetProcess(false);
            for (int phase = 0; phase < 2; phase++)
            {
                zone._Process(phase == 0 ? 1.4d : 0.235d);
                await Frames(3);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = viewport.GetTexture().GetImage();
                var background = image.GetPixel(20, 100);
                var danger = image.GetPixel((int)Field.Left + 3, 3);
                Check(background != danger, "danger remains visible during warning and impact");
                for (int y = 4; y < 212; y += 7)
                for (int x = (int)Field.Left + 4; x < 380; x += 7)
                {
                    float distance = new Vector2(x, y).DistanceTo(center);
                    Color color = image.GetPixel(x, y);
                    if (distance < radius - 5 && color != background)
                        throw new Exception($"Safe hole covered: r={radius} phase={phase} at {x},{y}");
                    if (distance > radius + 6 && color != danger)
                    {
                        image.SavePng(ProjectSettings.GlobalizePath("res://build/qa_story/aoe_mask_failure.png"));
                        throw new Exception($"Danger fill has a gap: r={radius} phase={phase} at {x},{y}: {color} expected {danger}");
                    }
                }
                Check(!zone.CoversPoint(center) && !zone.CoversPoint(center + Vector2.Right * (radius + 2))
                    && zone.CoversPoint(center + Vector2.Right * (radius + 3)), "safe-zone hit tolerance is unchanged");
            }
            zone.QueueFree();
            await Frames(3);
            GD.Print($"[BossSpellQA] PASS pixel mask radius={radius} center={center}: no safe-zone flash or polygon gaps");
        }
        viewport.QueueFree();
        await Frames(3);
    }

    private async Task ClearStrikes(Node world)
    {
        foreach (var strike in world.GetChildren().OfType<AreaStrike>()) strike.QueueFree();
        await Frames(2);
    }
    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/boss_spells/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await Frames(24);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
