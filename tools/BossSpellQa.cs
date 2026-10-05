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
    private static void Write(object obj, string name, object value, Type? type = null)
        => (type ?? obj.GetType()).GetField(name, Private)!.SetValue(obj, value);
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
            if (OS.GetCmdlineUserArgs().Contains("--unfolders")) game.MarkIdleDialogSeen("once_midboss_shield");
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
            if (OS.GetCmdlineUserArgs().Contains("--koharu-body"))
                await CheckKoharuBody(game);
            else if (OS.GetCmdlineUserArgs().Contains("--break") || OS.GetCmdlineUserArgs().Contains("--shields")
                || OS.GetCmdlineUserArgs().Contains("--unfolders"))
                await CheckBreakEffect(game);
            else if (OS.GetCmdlineUserArgs().Contains("--backgrounds"))
                await CheckBossBackgrounds(game);
            else
            {
                bool clipsOnly = OS.GetCmdlineUserArgs().Contains("--rei-clips");
                bool edgesOnly = OS.GetCmdlineUserArgs().Contains("--edges");
                foreach (string scene in clipsOnly ? new[] { "Rei" } : new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
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
                    root.GetNode<StageBackground>("StageBackground").EnterBoss();
                    await Frames(250);
                    boss.SetPhysicsProcess(false);
                    if (edgesOnly)
                    {
                        await CheckEdgeVolleys(game, scene, boss, hud, world, player);
                        root.QueueFree();
                        await Frames(5);
                        Pool.DespawnAll();
                        Hud.BubblePaused = false;
                        continue;
                    }
                    if (boss is BossMina mina)
                    {
                        var caster = Read<MinaPhaseAttacks>(mina, "_caster");
                        caster.SetProcess(false);
                        caster.CancelPendingAttacks();
                        await ClearStrikes(world);
                        await CheckMina(game, mina, hud);
                    }
                    else
                    {
                        var caster = Read<AreaSpellCaster>(boss, "_caster");
                        caster.SetProcess(false);
                        caster.CancelPendingAttacks();
                        await ClearStrikes(world);
                        Pool.DespawnAll();
                        if (scene == "Rei") await CheckReiClipLines(game, caster, hud, world, player);
                        if (!clipsOnly)
                        {
                            if (scene != "Akari") await CheckTelegraphs(game, scene, caster, hud, world, player);
                            await CheckAreaPresentation(game, scene, boss, caster, hud, world);
                        }
                    }
                    if (!clipsOnly) await CheckReadability(scene, boss, hud, world);
                    root.QueueFree();
                    await Frames(5);
                    Pool.DespawnAll();
                    Hud.BubblePaused = false;
                }
                if (!edgesOnly) await CheckSafeZonePixels();
            }
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Task.Delay(250);
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

    private async Task CheckEdgeVolleys(GameManager game, string scene, Enemy boss, Hud hud, Node2D world, Player player)
    {
        string key = scene == "MinaBattle" ? "mina" : scene.ToLowerInvariant();
        var caster = Read<Node>(boss, "_caster");
        caster.SetProcess(false);
        void CancelCaster()
        {
            if (caster is AreaSpellCaster area) area.CancelPendingAttacks();
            else ((MinaPhaseAttacks)caster).CancelPendingAttacks();
        }
        CancelCaster();
        await ClearStrikes(world);
        Pool.DespawnAll();
        var expected = key switch
        {
            "akari" => new[] { BossEdgeVolley.Edge.Top, BossEdgeVolley.Edge.Bottom },
            "koharu" => new[] { BossEdgeVolley.Edge.Right, BossEdgeVolley.Edge.Left },
            "rei" => new[] { BossEdgeVolley.Edge.Left, BossEdgeVolley.Edge.Right, BossEdgeVolley.Edge.Top },
            _ => new[] { BossEdgeVolley.Edge.Top, BossEdgeVolley.Edge.Right, BossEdgeVolley.Edge.Bottom, BossEdgeVolley.Edge.Left },
        };
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
            for (int i = 0; i < expected.Length; i++)
            {
                game.Difficulty = diff;
                player.GlobalPosition = new Vector2(Field.CenterX, 114);
                var volley = BossEdgeVolley.Begin(boss, key, i);
                volley.SetPhysicsProcess(false);
                Check(volley.Side == expected[i], $"{key}/{diff}/{i}: stage-specific edge order");
                double warning = Read<double>(volley, "_warning");
                var gates = Read<Vector2[]>(volley, "_gates");
                Vector2 direction = Read<Vector2>(volley, "_direction");
                Check(gates.All(p => p.X >= Field.Left && p.X <= Field.Right && p.Y >= Field.Top && p.Y <= Field.Bottom),
                    "all emitters stay inside the playfield, outside the HUD panel");
                Check(gates.Zip(gates.Skip(1), (a, b) => a.DistanceTo(b)).All(gap => gap >= 22), "dodge gaps remain open");
                Check(gates.Any(p => Mathf.Abs((p - player.GlobalPosition).Cross(direction)) < 0.1f),
                    "a warned lane targets the player rather than leaving an automatic center safe lane");
                Check(warning >= 1.15 && Bullets().Length == 0, "warning starts with no damaging projectiles");
                volley._PhysicsProcess(warning * 0.5);
                Check(Bullets().Length == 0, "no early firing during warning");
                if (diff == GameManager.Diff.Normal)
                {
                    await Shot($"edge_{key}_{volley.Side}_warning");
                    hud.HoldBubble = true;
                    hud.ShowDialog(Hud.LineKind.Mina, "Pause QA");
                    await Frames(1);
                    double time = Read<double>(volley, "_time");
                    volley._PhysicsProcess(5);
                    Check(Read<double>(volley, "_time") == time && Bullets().Length == 0, "dialogue freezes warning and firing");
                    hud.HoldBubble = false;
                    hud.HideBubble();
                    await Frames(1);
                }
                volley._PhysicsProcess(warning * 0.5 + 0.01);
                var first = Bullets();
                Check(first.Length == gates.Length && first.All(b => Read<Texture2D?>(b, "_sprite") != null),
                    "first row uses character projectile art");
                Check(first.All(b => gates.Any(g => g.IsEqualApprox(b.GlobalPosition)) && b.Velocity.Normalized().IsEqualApprox(direction)),
                    "projectiles originate at the warning and move in the announced direction");
                foreach (var bullet in first) bullet.SetPhysicsProcess(false);
                var bulletBefore = first[0].GlobalPosition;
                first[0]._PhysicsProcess(0.1);
                Check((first[0].GlobalPosition - bulletBefore).Dot(direction) > 0, "live projectile travels inward");
                foreach (var bullet in first) bullet._PhysicsProcess(0.28);
                volley._PhysicsProcess(0.28);
                foreach (var bullet in Bullets()) bullet._PhysicsProcess(0.28);
                volley._PhysicsProcess(0.28);
                foreach (var bullet in Bullets()) bullet.SetPhysicsProcess(false);
                Check(Bullets().Length == gates.Length * 3, "three rows release sequentially");
                if (diff == GameManager.Diff.Normal)
                {
                    foreach (var bullet in Bullets()) bullet._PhysicsProcess(0.2);
                    await Shot($"edge_{key}_{volley.Side}_fire");
                }
                if (diff == GameManager.Diff.Easy)
                {
                    foreach (var bullet in Bullets()) bullet._PhysicsProcess(10);
                    volley._PhysicsProcess(0.1);
                    Check(volley.Finished, "combat gate ends after the last row leaves the screen");
                }
                else volley.Cancel();
                Check(Bullets().Length == 0, "cancelling removes this attack's projectiles");
                foreach (var bullet in Pool.GetChildren().OfType<Bullet>()) bullet.SetPhysicsProcess(true);
                await Frames(2);
            }
        game.Difficulty = GameManager.Diff.Normal;
        var canceled = BossEdgeVolley.Begin(boss, key, 0);
        canceled.SetPhysicsProcess(false);
        canceled._PhysicsProcess(1.5);
        var reused = Bullets()[0];
        Pool.Despawn(reused);
        var unrelated = Pool.Spawn(new Vector2(Field.CenterX, 80), Vector2.Down * 20, true);
        Check(unrelated == reused, "test reuses an owned pool slot");
        canceled.Cancel();
        Check(unrelated.Active, "cleanup preserves a recycled, unrelated bullet");
        Pool.DespawnAll();
        await Frames(2);

        var interrupted = BossEdgeVolley.Begin(boss, key, 0);
        interrupted.SetPhysicsProcess(false);
        interrupted._PhysicsProcess(1.5);
        typeof(Enemy).GetMethod("EnterBreak", Private)!.Invoke(boss, null);
        interrupted._PhysicsProcess(0.01);
        Check(interrupted.Finished && Bullets().Length == 0, "shield break cancels attack and pending rows");
        typeof(Enemy).GetMethod("EnterShielded", Private)!.Invoke(boss, null);
        await Frames(2);

        if (caster is AreaSpellCaster areaCaster)
        {
            Write(areaCaster, "_nextEdge", true);
            areaCaster._Process(100);
            Check(areaCaster.EdgeAttackActive, "normal boss scheduler starts edge attack");
            Call(boss, "FirePattern", 10d);
            Check(Bullets().Length == 0, "normal boss bullets stop during edge warning");
            areaCaster.Suppressed = true;
            Check(!areaCaster.EdgeAttackActive, "bespoke boss gimmick cancels edge attack");
            areaCaster.Suppressed = false;
            areaCaster.CancelPendingAttacks();
            Write(areaCaster, "_nextEdge", true);
            areaCaster._Process(100);
            if (key == "rei")
            {
                areaCaster.CastFullscreenChain(2, 70, 100);
                Check(!areaCaster.EdgeAttackActive && areaCaster.AoeActive, "fullscreen relay replaces edge attack without overlap");
            }
        }
        else
        {
            var minaCaster = (MinaPhaseAttacks)caster;
            typeof(MinaPhaseAttacks).GetProperty("OpenerCompleted")!.SetValue(minaCaster, true);
            Write(minaCaster, "_nextEdge", true);
            minaCaster._Process(100);
            Check(minaCaster.Active && Read<BossEdgeVolley>(minaCaster, "_edgeVolley") != null,
                "Mina scheduler alternates signature AOE and edge attack");
            Check(((BossMina)boss).AoeGateActive, "Mina normal and ambient bullets are gated");
            minaCaster.BeginPhase(1);
            Check(!minaCaster.Active && !minaCaster.OpenerCompleted, "costume transition cancels volley and retains mandatory signature");
        }
        CancelCaster();
        await Frames(2);
        Check(!world.GetChildren().OfType<BossEdgeVolley>().Any() && Bullets().Length == 0,
            "post/scene cancellation leaves no emitters or bullets");
    }

    private async Task CheckKoharuBody(GameManager game)
    {
        void BaseCall(Enemy boss, string method, params object[] args) =>
            typeof(Enemy).GetMethod(method, Private)!.Invoke(boss, args);
        (float head, Vector2 foot) Landmarks(Sprite2D sprite) => sprite.Texture.ResourcePath.GetFile() switch
        {
            "boss_koharu_body_idle.png" => (72f, new Vector2(427, 629)),
            "boss_koharu_body_attack.png" => (166f, new Vector2(346, 668)),
            "boss_koharu_body_idle2.png" => (0f, new Vector2(410, 681)),
            "boss_koharu_body_cry.png" => (0f, new Vector2(243, 637)),
            "enemy_koharu_post.png" => (0f, new Vector2(111, 358)),
            _ => throw new Exception($"Unexpected Koharu body: {sprite.Texture.ResourcePath}"),
        };
        float Height(Sprite2D sprite)
        {
            var (head, foot) = Landmarks(sprite);
            return (foot.Y - head) * sprite.Scale.Y;
        }
        Vector2 Foot(Sprite2D sprite)
        {
            Vector2 p = Landmarks(sprite).foot - sprite.Texture.GetSize() / 2f;
            if (sprite.FlipH) p.X = -p.X;
            p = (p + sprite.Offset) * sprite.Scale;
            if (sprite.FlipH) p.X = -p.X;
            return p;
        }
        foreach (bool flip in new[] { true, false })
        {
            game.Difficulty = GameManager.Diff.Normal;
            game.SelectedJob = Job.Tank;
            game.SelectedEntry = GameManager.StageEntry.Start;
            var root = GD.Load<PackedScene>("res://Koharu.tscn").Instantiate<KoharuRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            root.Stage.SetProcess(false);
            root.World.ProcessMode = ProcessModeEnum.Inherit;
            root.Player.SetPhysicsProcess(false);
            Write(root.Player, "_invincible", true);
            Write(root.Player, "_invincibleTimer", 999f);
            root.Player.Position = new Vector2(155, 155);
            root.Hud.HoldBubble = false;
            root.Hud.HideBubble();
            var boss = new BossKoharu();
            typeof(Enemy).GetField("FaceLeft", Private)!.SetValue(boss, flip);
            root.World.AddChild(boss);
            boss.Position = new Vector2(290, 110);
            boss.SetPhysicsProcess(false);
            BaseCall(boss, "TickEntrance", 0d);
            BaseCall(boss, "TickEntrance", 2d);
            BaseCall(boss, "TickSwapAnim", 1d);
            var caster = Read<AreaSpellCaster>(boss, "_caster");
            caster.SetProcess(false);
            caster.CancelPendingAttacks();
            root.Hud.HideSpellCard();
            root.GetNode<StageBackground>("StageBackground").EnterBoss();
            var body = boss.GetNode<Sprite2D>("Body");
            float expectedHeight = Height(body);
            Vector2 expectedFoot = Foot(body);
            var collision = Read<CollisionShape2D>(boss, "_bodyShape", typeof(Enemy));
            var shape = (CapsuleShape2D)collision.Shape;
            float radius = shape.Radius, height = shape.Height;
            Check(body.Visible && expectedHeight > 40, "Koharu entrance retains the idle body size");

            async Task Transition(string name, Action change, string texture)
            {
                change();
                float heightDrift = 0, footDrift = 0;
                for (int i = 0; i < 15; i++)
                {
                    BaseCall(boss, "TickSwapAnim", 1d / 60);
                    foreach (var sprite in new[] { body, Read<Sprite2D?>(boss, "_fadeSprite", typeof(Enemy)) })
                    {
                        if (sprite == null) continue;
                        heightDrift = Mathf.Max(heightDrift, Mathf.Abs(Height(sprite) - expectedHeight));
                        footDrift = Mathf.Max(footDrift, Foot(sprite).DistanceTo(expectedFoot));
                        Check(Mathf.IsEqualApprox(sprite.Scale.X, sprite.Scale.Y), "body keeps its aspect ratio");
                    }
                    await Frames(1);
                }
                Check(heightDrift < 0.01f && footDrift < 0.01f,
                    $"Koharu {name}, flip={flip}: new and fading bodies stay aligned (height={heightDrift:F4}, foot={footDrift:F4})");
                Check(body.Texture.ResourcePath.GetFile() == texture, $"{name}: expected pose is displayed");
                if (flip) await Shot($"koharu_body_{name}");
            }
            if (flip) await Shot("koharu_body_idle");
            await Transition("attack", () => BaseCall(boss, "TriggerAttackPose"), "boss_koharu_body_attack.png");
            await Transition("idle_return", () => BaseCall(boss, "TickAttackPose", 1d), "boss_koharu_body_idle.png");
            if (flip)
            {
                BaseCall(boss, "TriggerAttackPose");
                BaseCall(boss, "AdvanceForm2");
                Check(body.Texture.ResourcePath.GetFile() == "boss_koharu_body_attack.png",
                    "changing phase during an attack keeps the current attack pose");
                await Transition("form2", () => BaseCall(boss, "TickAttackPose", 1d), "boss_koharu_body_idle2.png");
            }
            else
                await Transition("form2", () => BaseCall(boss, "AdvanceForm2"), "boss_koharu_body_idle2.png");
            await Transition("form2_attack", () => BaseCall(boss, "TriggerAttackPose"), "boss_koharu_body_attack.png");
            await Transition("form2_return", () => BaseCall(boss, "TickAttackPose", 1d), "boss_koharu_body_idle2.png");

            var mover = Read<BossMover>(boss, "_mover");
            int initialFlips = mover.FlipCount;
            Vector2 initialPosition = boss.Position;
            float liveDrift = 0;
            boss.SetPhysicsProcess(true);
            for (int i = 0; i < 480; i++)
            {
                root.Player.Position = new Vector2(i < 240 ? Field.Right - 5 : Field.Left + 5, 150);
                await Frames(1);
                liveDrift = Mathf.Max(liveDrift, Mathf.Abs(Height(body) - expectedHeight));
                Check(Mathf.IsEqualApprox(body.Scale.X, body.Scale.Y), "live turns do not stretch Koharu");
            }
            boss.SetPhysicsProcess(false);
            Check(liveDrift < 0.01f && mover.FlipCount > initialFlips,
                $"live firing and turns preserve body size (drift={liveDrift:F4})");
            Check(boss.Position.DistanceTo(initialPosition) > 1, "Koharu's movement remains active");
            BaseCall(boss, "TickAttackPose", 1d);
            BaseCall(boss, "TickSwapAnim", 1d);
            await Transition("cry", () => BaseCall(boss, "SwapBody", "res://char/v3/boss_koharu_body_cry.png", 1f), "boss_koharu_body_cry.png");
            await Transition("post", () => BaseCall(boss, "SwapBody", "res://char/v3/enemy_koharu_post.png", 1f), "enemy_koharu_post.png");
            if (flip)
                foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(540, 960) })
                {
                    DisplayServer.WindowSetSize(size);
                    await Shot($"koharu_body_post_{size.X}x{size.Y}");
                    Check(Mathf.Abs(Height(body) - expectedHeight) < 0.01f, "window resizing preserves the logical body size");
                }
            Check(shape.Radius == radius && shape.Height == height && collision.Scale == Vector2.One,
                "Koharu body normalization leaves collision unchanged");
            root.QueueFree();
            Pool.DespawnAll();
            await Frames(5);
            Hud.BubblePaused = false;
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
    }

    private async Task CheckBreakEffect(GameManager game)
    {
        string output = ProjectSettings.GlobalizePath("res://build/qa_story/boss_break");
        DirAccess.MakeDirRecursiveAbsolute(output);
        void BaseCall(Enemy boss, string method, params object[] args) =>
            typeof(Enemy).GetMethod(method, Private)!.Invoke(boss, args);
        // The label lives in World (FxLayer's parent) so that it draws after the boss body; see FxLayer.BossBreak.
        BossBreakFx[] Effects() => FxLayer.Instance.GetParent().GetChildren().OfType<BossBreakFx>().ToArray();
        async Task<Image> Render(BossBreakFx effect, bool visible)
        {
            effect.Visible = visible;
            effect.QueueRedraw();
            await Frames(3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            return GetViewport().GetTexture().GetImage();
        }
        // Bright pixels per letter column (the effect's own glyph advances), rows within 14px of the baseline.
        //   Luminance, not pure white: the boss's break-cue disc (white, alpha 0.5, ZIndex 0, shaded) sits over the
        //   middle letters and the stage CanvasModulate cools it to about (0.80, 0.84, 0.96); the label itself is
        //   unshaded, so the outer letters stay pure white. Body/hair under the disc stay below 0.75.
        int[] WhitePixels(Image image, BossBreakFx effect, Image? background = null)
        {
            float scale = image.GetWidth() / 384f;
            var advances = Read<float[]>(effect, "_advances");
            var white = new int[advances.Length];
            float left = effect.Position.X - Read<float>(effect, "_textWidth") * 0.5f;
            for (int i = 0; i < advances.Length; left += advances[i], i++)
                for (int y = (int)((effect.Position.Y - 14) * scale); y < (effect.Position.Y + 14) * scale; y++)
                    for (int x = (int)(left * scale); x < (left + advances[i]) * scale; x++)
                    {
                        if (image.GetPixel(x, y).Luminance > 0.82f
                            && (background == null || background.GetPixel(x, y).Luminance <= 0.82f)) white[i]++;
                    }
            return white;
        }
        string Phase(Enemy boss) => Read<object>(boss, "_phase", typeof(Enemy)).ToString()!;
        FxLayer.P CloneParticle(FxLayer.P particle) =>
            (FxLayer.P)typeof(object).GetMethod("MemberwiseClone", Private)!.Invoke(particle, null)!;
        foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle", "Hikage", "Cameo" })
        {
            game.SelectedJob = Job.Heal;
            game.SelectedEntry = GameManager.StageEntry.Start;
            var root = GD.Load<PackedScene>($"res://{(scene is "Hikage" or "Cameo" ? "Akari" : scene)}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            ((Node)root.GetType().GetProperty("Stage")!.GetValue(root)!).SetProcess(false);
            var world = root.GetNode<Node2D>("World");
            world.ProcessMode = ProcessModeEnum.Inherit;
            var player = world.GetNode<Player>("Player");
            player.SetPhysicsProcess(false);
            player.Position = new Vector2(170, 140);
            Write(player, "_invincible", true);
            var hud = root.GetNode<Hud>("Hud");
            hud.SetCinematicMode(false);
            hud.HideBubble();
            Write(hud, "_bannerTimer", 0d);
            Enemy boss = scene switch
            {
                "Akari" => new BossAkari(), "Koharu" => new BossKoharu(), "Rei" => new BossRei(),
                "MinaBattle" => new BossMina(), "Hikage" => new BossHikage(),
                _ => new CameoBoss { Theme = new CameoTheme
                {
                    DisplayName = "QA", Handle = "@qa", PreTex = "res://char/v3/akari_mid.png",
                    CryTex = "res://char/v3/akari_mid.png", PostTex = "res://char/v3/akari_mid.png",
                    Face = "", IntroLines = Array.Empty<(int, string, string)>(),
                    TauntLines = Array.Empty<(int, string, string)>(), DefeatLines = Array.Empty<(int, string, string)>(),
                } },
            };
            world.AddChild(boss);
            boss.Position = new Vector2(290, 110);
            boss.SetPhysicsProcess(false);
            boss.SetProcess(false);
            foreach (var child in boss.GetChildren()) { child.SetProcess(false); child.SetPhysicsProcess(false); }
            BaseCall(boss, "TickEntrance", 0d);
            BaseCall(boss, "TickEntrance", 2d);
            hud.HideSpellCard();
            root.GetNode<StageBackground>("StageBackground").EnterBoss();
            await Frames(150);
            Pool.DespawnAll();
            if (OS.GetCmdlineUserArgs().Contains("--shields") || OS.GetCmdlineUserArgs().Contains("--unfolders"))
            {
                root.ProcessMode = ProcessModeEnum.Disabled;
                if (OS.GetCmdlineUserArgs().Contains("--unfolders")) await CheckUnfolders(scene, boss, player);
                else await CheckShieldAppearance(scene, boss);
                root.QueueFree();
                await Frames(5);
                Engine.TimeScale = 1;
                continue;
            }
            Write(hud, "_flashAlpha", 0f);
            long score = game.Score;
            int bombs = game.Bombs;
            float hp = boss.HpRatio;
            var panels = boss.GetChildren().OfType<Panel>().ToArray();
            foreach (var panel in panels) panel.Shatter();
            var effect = Effects().Single();
            effect.SetProcess(false);
            Check(Phase(boss) == "Break" && boss.HpRatio == hp, $"{scene}: real panel break starts one effect without body damage");
            Check(game.Bombs == Mathf.Min(game.StartBombs, bombs + 1) && game.Score == score + panels.Length * 5,
                "break reward and original panel scores are unchanged");
            Check(!Hud.BubblePaused && Read<float>(hud, "_flashAlpha") == 0f, "no dialogue pause or full-screen flash");
            Check(effect.ZIndex < 0 && !effect.ZAsRelative, "enemy bullets stay above the entire effect");
            var body = Read<Sprite2D>(boss, "_bodySprite", typeof(Enemy));
            int bodyZ = body.ZAsRelative ? boss.ZIndex + body.ZIndex : body.ZIndex;
            Check(effect.ZIndex == bodyZ && effect.GetParent() == world && effect.GetIndex() > boss.GetIndex(),
                "BREAK label shares the body sprite layer and follows the boss in World, so it draws over the body");
            var shieldLayer = boss.GetNode<Node2D>("Shield");
            Check(shieldLayer.ZIndex == body.ZIndex && shieldLayer.GetIndex() > body.GetIndex(),
                "shield is above its body, below the BREAK label and enemy bullets");
            Check(!Read<System.Collections.Generic.List<FxLayer.P>>(FxLayer.Instance, "_p").Any(p => p.Text == "BREAK!"),
                "old floating damage-number label is not duplicated");
            boss.Purify();
            Check(Effects().Length == 1, "repeated purify does not stack announcements");
            foreach (var position in new[] { new Vector2(Field.Left, 51), new Vector2(Field.Right, 51),
                new Vector2(Field.Left, 148), new Vector2(Field.Right, 148) })
            {
                effect.PlaceOn(position, 56);
                Check(effect.Position.X - 76 >= Field.Left && effect.Position.X + 76 <= Field.Right
                    && effect.Position.Y - 25 >= Field.Top && effect.Position.Y + 25 <= Field.Bottom,
                    "edge placement keeps letters inside the field");
            }
            effect.PlaceOn(boss.Position, Read<float>(boss, "BodyDisplayH", typeof(Enemy)));
            Check(Mathf.Abs(effect.Position.Y - Mathf.Clamp(boss.Position.Y, Field.Top + 25f, Field.Bottom - 25f)) < 0.5f,
                "BREAK label sits on the boss body, not above it");
            var renderMode = root.ProcessMode;
            root.ProcessMode = ProcessModeEnum.Disabled;
            var particles = Read<System.Collections.Generic.List<FxLayer.P>>(FxLayer.Instance, "_p");
            var initialParticles = particles.Select(CloneParticle).ToArray();
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                foreach (float time in new[] { 0.07f, 0.22f, 0.48f })
                {
                    Write(effect, "_age", time);
                    // The frozen boss and the BREAK label must render the same point in the shatter animation.
                    typeof(Enemy).GetField("_phaseT", Private)!.SetValue(boss, (double)time);
                    boss.QueueRedraw();
                    shieldLayer.QueueRedraw();
                    // Keep the impact flash on the same clock instead of freezing its brightest first frame across resizes.
                    particles.Clear();
                    particles.AddRange(initialParticles.Select(CloneParticle));
                    FxLayer.Instance._Process(time);
                    // Count newly white pixels, not net white area: the dark text outline also covers bright armor beneath it.
                    using var background = time == 0.22f ? await Render(effect, false) : null;
                    using var image = await Render(effect, true);
                    Check(image.SavePng($"{output}/{scene}_{size.X}_{time:0.00}.png") == Error.Ok, "rendered animation keyframe");
                    if (time == 0.22f)
                    {
                        float scale = image.GetWidth() / 384f;
                        int[] letters = WhitePixels(image, effect, background);
                        int total = letters.Sum();
                        GD.Print($"[BossSpellQA] {scene}/{size.X}: BREAK white pixels {total} = {string.Join("+", letters)}");
                        Check(total > 550 * scale * scale && letters.Min() > total * 0.3f / letters.Length,
                            $"{scene}/{size}: all five BREAK letters are drawn over the boss body, not buried in it");
                        if (scene == "Akari" && size.X == 1280)
                        {
                            effect.ZIndex = bodyZ - 1;
                            using var buried = await Render(effect, true);
                            int[] hidden = WhitePixels(buried, effect, background);
                            int hiddenTotal = hidden.Sum();
                            Check(hiddenTotal <= 550 * scale * scale || hidden.Min() <= hiddenTotal * 0.3f / hidden.Length,
                                "negative control detects BREAK text buried behind the body");
                            effect.ZIndex = bodyZ;
                        }
                    }
                }
            }
            root.ProcessMode = renderMode;
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            Write(effect, "_age", 0.1f);
            effect.SetProcess(true);
            GetTree().Paused = true;
            await Task.Delay(80);
            Check(Read<float>(effect, "_age") == 0.1f, "pause menu freezes the animation");
            GetTree().Paused = false;
            effect.SetProcess(false);
            typeof(Enemy).GetField("_phaseT", Private)!.SetValue(boss, 0d);
            BaseCall(boss, "TickBossPhase", 0.449d);
            Check(Phase(boss) == "Break", "break cue remains 0.45 seconds");
            BaseCall(boss, "TickBossPhase", 0.002d);
            Check(Phase(boss) == "Exposed", "normal vulnerability window opens on time");
            effect._Process(BossBreakFx.Duration);
            Check(effect.IsQueuedForDeletion() && !effect.Visible, "effect removes itself within 0.72 seconds");
            await Frames(2);
            FxLayer.Instance.BossBreak(boss.Position, 56);
            var interrupted = Effects().Single();
            hud.HoldBubble = true;
            hud.ShowMessage("QA");
            interrupted._Process(0.01);
            Check(interrupted.IsQueuedForDeletion() && !interrupted.Visible, "dialogue interruption cannot leave stale BREAK text");
            hud.HoldBubble = false;
            hud.HideBubble();
            root.QueueFree();
            Pool.DespawnAll();
            await Frames(5);
            Engine.TimeScale = 1;
        }
    }

    private async Task CheckUnfolders(string scene, Enemy boss, Player player)
    {
        var panels = boss.GetChildren().OfType<Panel>().ToArray();
        Write(boss, "_entering", false, typeof(Enemy));
        Hud.BubblePaused = false;
        if (boss.UnfolderStyle == UnfolderKind.None)
        {
            Write(player, "_locked", true);
            Write(player, "_lockTarget", boss);
            var panel = player.LockedUnfolder;
            Check(panel?.GetParent() == boss, $"{scene}: secondary target belongs to the main boss");
            panel!.Shatter();
            Check(player.LockTarget == boss && player.LockedUnfolder != panel && player.LockedUnfolder?.GetParent() == boss,
                $"{scene}: broken panel hands off within the same boss");
            foreach (var remaining in panels) remaining.Shatter();
            Check(player.LockedUnfolder == null && player.LockAimPosition == boss.GlobalPosition && boss.GaugeVulnerable,
                $"{scene}: breaking all panels targets the exposed body");
            return;
        }
        string output = ProjectSettings.GlobalizePath("res://build/qa_story/unfolders");
        DirAccess.MakeDirRecursiveAbsolute(output);
        var game = GetNode<GameManager>("/root/Game");
        Write(player, "_locked", true);
        Write(player, "_lockArmed", true);
        Write(player, "_lockByShift", false);
        Write(player, "_lockTarget", boss);
        Check(panels.All(p => p.Ink == 20 && p.CanLock), $"{scene}: shield durability matches precise panel aiming");
        using (var art = GD.Load<Texture2D>(UnfolderMotion.TexturePath(boss.UnfolderStyle)).GetImage())
            Check(art.GetWidth() == 256 && art.GetHeight() == 256 && art.HasMipmaps() && art.GetPixel(0, 0).A == 0
                && art.GetPixel(128, 128).A > 0.5f, $"{scene}: real alpha and an opaque target core");
        Check(panels.All(p => p.GetChildren().OfType<Sprite2D>().Single().Texture.ResourcePath
            == UnfolderMotion.TexturePath(boss.UnfolderStyle)), $"{scene}: dedicated artwork is live");
        Check(panels.All(p => ((CircleShape2D)p.GetChildren().OfType<CollisionShape2D>().Single().Shape).Radius == 3f),
            $"{scene}: decoration does not enlarge the collision shape");
        foreach (int tier in new[] { -1, 0, ChargeTier.First, ChargeTier.Second })
        {
            var test = Pool.Spawn(player.GlobalPosition, Vector2.Right * 300, false, 3, 20);
            if (tier == -1) test.MakeAccel(12, 640, 0.5f);
            else if (tier > 0) test.MakeCharged(Job.Tank, tier);
            int cost = tier == -1 ? 2 : tier == 0 ? 1 : tier == ChargeTier.First ? 3 : 5;
            Call(panels[0], "OnAreaEntered", test);
            Check(panels[0].Ink == 20 - cost, $"{scene}: shot tier {tier} strips {cost} durability");
            panels[0].Ink = 20;
            Pool.DespawnAll();
        }
        foreach (var panel in panels) panel._PhysicsProcess(0.2);

        var target = player.LockedUnfolder!;
        Check(target != null && player.LockTarget == boss, $"{scene}: boss identity remains stable while aiming at a panel");
        var playerPosition = player.GlobalPosition;
        player.GlobalPosition = boss.GlobalPosition + new Vector2(70, 50);
        Check(player.LockedUnfolder == target, $"{scene}: proximity changes cannot steal the lock");
        player.GlobalPosition = playerPosition;
        Check(player.ShotDir.DistanceTo((target!.GlobalPosition - player.GlobalPosition).Normalized()) < 0.001f,
            $"{scene}: shot direction aims at the real panel");
        Call(player, "TickLockOn");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.S, Pressed = true });
        Input.FlushBufferedEvents();
        Call(player, "TickLockOn");
        Check(player.LockTarget == boss && player.LockedUnfolder == target,
            $"{scene}: next-target input keeps the sole boss and its panel selected");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.S, Pressed = false });
        Input.FlushBufferedEvents();
        Call(player, "TickLockOn");
        target = player.LockedUnfolder!;

        var shot = Pool.Spawn(player.GlobalPosition, Vector2.Right * 200, false, 3, 4, homing: true);
        shot.MakeCharged(Job.Heal);
        Write(shot, "_homeTarget", boss);
        Call(shot, "SteerToTarget", 0.02f);
        Check(Read<Panel>(shot, "_homeUnfolder") == target, $"{scene}: homing respects the selected real panel");
        shot.RegisterChargeHit(target);
        Call(shot, "SteerToTarget", 0.02f);
        Check(Read<Panel>(shot, "_homeUnfolder") != target, $"{scene}: a piercing charge does not circle an already-hit panel");
        Pool.DespawnAll();

        async Task<Image> Capture(string suffix)
        {
            boss.QueueRedraw();
            foreach (var panel in panels) panel.QueueRedraw();
            await Frames(3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng($"{output}/{scene}_{suffix}.png") == Error.Ok, $"{scene}: {suffix} captured");
            return image;
        }
        using var before = await Capture("start");
        if (boss is BossMina) Write(boss, "_pattern", 4);
        var start = target.Position;
        bool moved = false, warped = false, warned = false, echoes = false;
        for (int frame = 0; frame < 360; frame++)
        {
            var previous = target.Position;
            foreach (var panel in panels) panel._PhysicsProcess(1d / 60);
            var pose = Read<UnfolderPose>(target, "_pose");
            warned |= pose.WarpCue > 0.01f;
            echoes |= pose.EchoAlpha > 0.1f;
            if (boss.UnfolderStyle == UnfolderKind.Rei && pose.WarpCue > 0.5f)
                CheckSilent(panels.All(p => p.Position.DistanceTo(pose.WarpTo) > 6),
                    "Rei's warp forecast must not coincide with an occupied slot");
            moved |= start.DistanceTo(target.Position) > 10;
            if (previous.DistanceTo(target.Position) > 25 && frame > 0)
            {
                Check(warned, $"{scene}: teleport follows a visible warning");
                warped = true;
            }
            CheckSilent(player.LockedUnfolder == target && player.LockAimPosition.DistanceTo(target.GlobalPosition) < 0.001f,
                "lock must follow the same panel through motion/teleport");
            CheckSilent(panels.All(p => Field.Rect.HasPoint(p.GlobalPosition)), "all targets stay inside the playfield");
            if (frame == 119 || frame == 275) { using var snap = await Capture($"motion_{frame}"); }
        }
        Check(moved, $"{scene}: motion changes actual target coordinates");
        Check(boss.GetChildren().OfType<Panel>().Count() == panels.Length, $"{scene}: clones add no colliders or lock candidates");
        if (boss.UnfolderStyle is UnfolderKind.Rei or UnfolderKind.Mina)
            Check(warped, $"{scene}: warp pattern is exercised");
        if (boss.UnfolderStyle is UnfolderKind.Koharu or UnfolderKind.Mina)
            Check(echoes, $"{scene}: mirror pattern is exercised");
        using var after = await Capture("end");
        int changed = 0;
        for (int y = 0; y < before.GetHeight(); y += 2)
        for (int x = before.GetWidth() / 2; x < before.GetWidth(); x += 2)
            if (Mathf.Abs(before.GetPixel(x, y).Luminance - after.GetPixel(x, y).Luminance) > 0.1f) changed++;
        Check(changed > 150, $"{scene}: motion visibly changes rendered pixels ({changed})");
        var frozen = target.Position;
        Hud.BubblePaused = true;
        target._PhysicsProcess(1);
        Check(target.Position == frozen, $"{scene}: dialogue freezes the motion and warp clock");
        Hud.BubblePaused = false;
        boss.SetPanelsInvulnerable(true);
        Check(player.LockedUnfolder == null && player.LockAimPosition == boss.GlobalPosition,
            $"{scene}: story/post surface takes priority over hidden panels");
        boss.SetPanelsInvulnerable(false);
        foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            using var resized = await Capture($"active_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        if (boss is BossMina)
        {
            for (int phase = 0; phase < 5; phase++)
            {
                Write(boss, "_pattern", phase);
                foreach (var panel in panels) panel._PhysicsProcess(0);
                using var form = await Capture($"phase_{phase}");
            }
        }
        var bossPosition = boss.GlobalPosition;
        foreach (var edge in new[] { new Vector2(Field.Left + 10, 20), new Vector2(Field.Right - 10, Field.Bottom - 20) })
        {
            boss.GlobalPosition = edge;
            foreach (var panel in panels) panel._PhysicsProcess(0);
            Check(panels.All(p => p.GlobalPosition.X >= Field.Left + 12 && p.GlobalPosition.X <= Field.Right - 12
                && p.GlobalPosition.Y >= Field.Top + 12 && p.GlobalPosition.Y <= Field.Bottom - 12),
                $"{scene}: orbit and teleport stay out of the HUD and screen edges");
        }
        boss.GlobalPosition = bossPosition;
        foreach (var panel in panels) panel._PhysicsProcess(0);
        target = player.LockedUnfolder!;
        target.Shatter();
        Check(player.LockedUnfolder != null && player.LockedUnfolder != target, $"{scene}: broken panel hands off immediately");
        foreach (var panel in panels) panel.Shatter();
        Check(player.LockedUnfolder == null && player.LockTarget == boss && player.LockAimPosition == boss.GlobalPosition
            && boss.GaugeVulnerable, $"{scene}: shield break hands the lock to the exposed body");
        await Frames(2);
    }

    private static void CheckSilent(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private async Task CheckShieldAppearance(string scene, Enemy boss)
    {
        string output = ProjectSettings.GlobalizePath("res://build/qa_story/boss_shields");
        DirAccess.MakeDirRecursiveAbsolute(output);
        void Set(string field, object value) => typeof(Enemy).GetField(field, Private)!.SetValue(boss, value);
        void Phase(string phase, double age = 0)
        {
            var field = typeof(Enemy).GetField("_phase", Private)!;
            field.SetValue(boss, Enum.Parse(field.FieldType, phase));
            Set("_phaseT", age);
        }
        async Task<Image> Render(bool enabled, string? name = null)
        {
            Set("_entering", !enabled);
            boss.QueueRedraw();
            boss.GetNode<Node2D>("Shield").QueueRedraw();
            await Frames(3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            if (name != null) Check(image.SavePng($"{output}/{scene}_{name}.png") == Error.Ok, $"{scene}: {name} rendered");
            return image;
        }
        (int changed, float brightness, float warmth) Compare(Image a, Image b, float radius)
        {
            float scale = a.GetWidth() / GetViewport().GetVisibleRect().Size.X;
            Vector2 at = boss.GetGlobalTransformWithCanvas().Origin * scale;
            int count = 0;
            float brightness = 0, warmth = 0;
            int r = Mathf.CeilToInt(radius * scale);
            for (int y = Mathf.Max(0, (int)at.Y - r); y < Mathf.Min(a.GetHeight(), (int)at.Y + r); y++)
                for (int x = Mathf.Max(0, (int)at.X - r); x < Mathf.Min(a.GetWidth(), (int)at.X + r); x++)
                {
                    var before = a.GetPixel(x, y);
                    var after = b.GetPixel(x, y);
                    var delta = after - before;
                    if (Mathf.Abs(delta.R) + Mathf.Abs(delta.G) + Mathf.Abs(delta.B) < 0.12f) continue;
                    count++;
                    brightness += after.Luminance - before.Luminance;
                    warmth += delta.R - delta.B;
                }
            return (count, count == 0 ? 0 : brightness / count, count == 0 ? 0 : warmth / count);
        }
        bool major = scene != "Cameo";
        string asset = major ? "boss_shield_major_v1.png" : "boss_shield_v1.png";
        using (var art = GD.Load<Texture2D>($"res://char/ui/{asset}").GetImage())
            Check(art.GetWidth() == 512 && art.HasMipmaps() && art.GetPixel(256, 256).A < 0.01f
                && art.GetPixel(0, 0).A == 0, $"{scene}: bounded shield art with a clear center and real alpha");
        float h = Read<float>(boss, "BodyDisplayH", typeof(Enemy));
        float hp = boss.HpRatio;
        var shape = Read<CollisionShape2D>(boss, "_bodyShape", typeof(Enemy)).Shape;
        Set("_shieldTime", 0.3f);
        Phase("Shielded");
        using var blank = await Render(false);
        using var active = await Render(true, "active");
        var visible = Compare(blank, active, h);
        float pixelScale = active.GetWidth() / 384f;
        Check(visible.changed > 150 * pixelScale * pixelScale && visible.brightness > 0.1f,
            $"{scene}: clearly visible shield ({visible.changed} pixels, brightness +{visible.brightness:F3})");
        Check(major ? visible.warmth > 0.08f : visible.warmth < -0.03f,
            $"{scene}: distinct {(major ? "red-gold major" : "blue-white midboss")} style ({visible.warmth:F3})");
        Check(Compare(blank, active, 3).changed == 0, $"{scene}: central body remains unobscured");
        var panels = boss.GetChildren().OfType<Panel>().Select(p => (panel: p, ink: p.Ink)).ToArray();
        foreach (var (panel, ink) in panels) { panel.Ink = Mathf.Max(1, ink / 3); panel.QueueRedraw(); }
        using (var worn = await Render(true, "worn"))
        {
            var wear = Compare(active, worn, h);
            Check(wear.changed > 30 * pixelScale * pixelScale && wear.brightness < -0.02f,
                $"{scene}: shield visibly weakens as its panels lose durability");
            Check(Compare(blank, worn, h).changed > 30 * pixelScale * pixelScale,
                $"{scene}: weakened shield remains visible until break");
        }
        foreach (var (panel, ink) in panels) { panel.Ink = ink; panel.QueueRedraw(); }
        Set("_shieldTime", 1.8f);
        using var animated = await Render(true, "motion");
        Check(Compare(active, animated, h).changed > 15 * pixelScale * pixelScale,
            $"{scene}: shield animation visibly changes rendered pixels");
        float clock = Read<float>(boss, "_shieldTime", typeof(Enemy));
        await Frames(5);
        Check(Read<float>(boss, "_shieldTime", typeof(Enemy)) == clock, $"{scene}: disabled world freezes shield time");
        Phase("Break", 0.18);
        using (var broken = await Render(true, "break")) { }
        Phase("Exposed", 1.0);
        using (var exposedOff = await Render(false))
        using (var exposedOn = await Render(true, "exposed"))
            Check(Compare(exposedOff, exposedOn, h).changed == 0, $"{scene}: no barrier in vulnerability window");
        Phase("Reclose", 0.05);
        using var reformStart = await Render(true, "reform_start");
        Phase("Reclose", 1.25);
        using var reformEnd = await Render(true, "reform_end");
        var start = Compare(blank, reformStart, h);
        var end = Compare(blank, reformEnd, h);
        Check(end.changed * end.brightness > start.changed * start.brightness * 1.5f,
            $"{scene}: invulnerable reclose visibly rebuilds its barrier");
        Phase("Shielded");
        Set("_purified", true);
        using (var purifiedOff = await Render(false))
        using (var purifiedOn = await Render(true))
            Check(Compare(purifiedOff, purifiedOn, h).changed == 0, $"{scene}: no shield after purification");
        Set("_purified", false);
        foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            using var resized = await Render(true, $"active_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        Check(boss.HpRatio == hp && Read<CollisionShape2D>(boss, "_bodyShape", typeof(Enemy)).Shape == shape,
            $"{scene}: presentation leaves HP and collision unchanged");
    }

    private async Task CheckReiClipLines(GameManager game, AreaSpellCaster caster, Hud hud, Node2D world, Player player)
    {
        var spells = Read<(string, AreaStrike.Shape?)[]>(caster, "_spells");
        var clip = spells.Single(s => s.Item1 == "切り抜きの線");
        Write(caster, "_spells", new[] { clip });
        var rng = Read<RandomNumberGenerator>(caster, "_rng");
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        foreach (float y in new[] { 12f, 108f, 204f })
        foreach (ulong seed in new ulong[] { 1, 17, 81 })
        {
            game.Difficulty = diff;
            rng.Seed = seed;
            player.GlobalPosition = new Vector2(Field.Left + 50, y);
            Call(caster, "Cast");
            caster._Process(0.71d);
            var lines = world.GetChildren().OfType<AreaStrike>().ToArray();
            foreach (var line in lines) line.SetProcess(false);
            int count = diff switch { GameManager.Diff.Easy => 1, GameManager.Diff.Hard => 3, GameManager.Diff.Lunatic => 5, _ => 2 };
            Check(Read<string>(hud, "_spellName") == clip.Item1
                && lines.Length > 0 && lines.Length <= count && lines[0].GlobalPosition.Y == y,
                $"Rei clip/{diff}/{y}/{seed}: player-anchored cast with difficulty limit");
            Check(lines.All(z => Read<float>(z, "_hh") == 4f && Read<float>(z, "_hw") == Field.Width / 2),
                "clip lines have a fixed eight-pixel visible width");
            foreach (var line in lines)
            {
                Check(Read<AreaStrike.Art>(line, "_art") == AreaStrike.Art.ClipLine, "clip has its own visual instead of the comment lane");
                foreach (float x in new[] { Field.Left + 2, Field.CenterX, Field.Right - 2 })
                foreach (float side in new[] { -1f, 1f })
                {
                    float cy = line.GlobalPosition.Y;
                    Check(line.CoversPoint(new Vector2(x, cy + side * 2.4f))
                        && !line.CoversPoint(new Vector2(x, cy + side * 2.6f))
                        && !line.CoversPoint(new Vector2(x, cy + side * 4.1f)),
                        "hit boundary stays 1.5 pixels inside the visible edge");
                }
                Check(!line.CoversPoint(new Vector2(Field.Left - 1, line.GlobalPosition.Y))
                    && !line.CoversPoint(new Vector2(Field.Right + 1, line.GlobalPosition.Y)), "clip cannot hit beyond the lane ends");
                foreach (var other in lines.Where(z => z != line))
                    Check(Mathf.Abs(other.GlobalPosition.Y - line.GlobalPosition.Y) >= 16f, "clip lanes leave at least eight pixels of escape space");
            }
            var first = lines[0];
            double warn = Read<double>(first, "_warn");
            Check(Math.Abs(warn - 1.2 * caster.WarnMul()) < 0.001, "clip retains its original warning time");
            int lives = player.Lives;
            Write(player, "_invincible", false);
            first._Process(warn - 0.01);
            Check(!first.IsStriking && player.Lives == lives, "standing on the line during warning is harmless");
            player.GlobalPosition += Vector2.Down * 3;
            first._Process(0.02);
            Check(first.IsStriking && player.Lives == lives, "three-pixel sidestep escapes the actual damage check");
            player.GlobalPosition = first.GlobalPosition;
            Call(first, "Strike");
            Check(player.Lives == lives - 1, "the clip center still deals damage");
            player.AddLife();
            Write(player, "_invincible", true);
            if (diff == GameManager.Diff.Normal && y == 108f && seed == 17)
            {
                foreach (var z in lines) { Write(z, "_struck", false); Write(z, "_t", Read<double>(z, "_warn") * 0.8); z.QueueRedraw(); }
                await Shot("rei_clip_warning");
                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
                {
                    DisplayServer.WindowSetSize(size);
                    foreach (var z in lines) { Write(z, "_struck", true); Write(z, "_t", Read<double>(z, "_warn") + 0.01); z.QueueRedraw(); }
                    await Shot($"rei_clip_impact_{size.X}x{size.Y}");
                }
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            caster.CancelPendingAttacks();
            await ClearStrikes(world);
        }
        Write(caster, "_spells", new[] { spells.Single(s => s.Item1 == "コメント一斉読み") });
        Call(caster, "Cast");
        caster._Process(0.71d);
        var comments = world.GetChildren().OfType<AreaStrike>().ToArray();
        Check(comments.Length > 0 && comments.All(z => Read<float>(z, "_hh") >= 5f
            && Read<float>(z, "_hh") <= 8f && Read<AreaStrike.Art>(z, "_art") == AreaStrike.Art.None),
            "comment lanes retain their original width and visuals after a clip cast");
        caster.CancelPendingAttacks();
        await ClearStrikes(world);
        Write(caster, "_spells", spells);
        game.Difficulty = GameManager.Diff.Normal;
        player.GlobalPosition = new Vector2(Field.Left + 50f, 160f);
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
                    hud.AnnounceSpell("こはる", BossHandles.KoharuMain, "既読の線", new Color("d6443f"));
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

    private async Task CheckMina(GameManager game, BossMina boss, Hud hud)
    {
        foreach (var diff in Enum.GetValues<GameManager.Diff>())
        {
            game.Difficulty = diff;
            for (int pattern = 0; pattern < 5; pattern++)
            {
                Pool.DespawnAll();
                Write(boss, "_pattern", pattern);
                Call(boss, "ApplySpell");
                Write(boss, "_fireT", 100d);
                Write(boss, "_fireT2", 100d);
                Call(boss, "FirePattern", 0d);
                var bullets = Bullets();
                int count = pattern switch
                {
                    0 => game.ScaleBullets(Read<int>(boss, "_ringCount")),
                    1 => Read<int>(boss, "_aimedWing") * 2 + 1,
                    2 => game.ScaleBullets(Read<int>(boss, "_flowerPetals")) * 2,
                    3 => 3,
                    _ => game.ScaleBullets(18) + 3,
                };
                Check(bullets.Length == count && bullets.All(b => Read<Texture2D?>(b, "_sprite") != null
                    && b.Damage == 1 && !b.Homing && !b.Accel && !b.IsPatternBullet), $"Mina/{diff}/{pattern}: illustrated attack preserves count and damage");
                var expectedArt = Read<Texture2D?[][]>(boss, "_spellArt")[pattern];
                Check(bullets.Select(b => Read<Texture2D>(b, "_sprite").GetInstanceId()).Distinct().OrderBy(p => p)
                    .SequenceEqual(expectedArt.Select(t => t!.GetInstanceId()).Distinct().OrderBy(p => p)), "memory attack includes every assigned character illustration");
                foreach (var b in bullets)
                {
                    float speed = b.Velocity.Length() / game.BulletSpeedMul;
                    bool valid = pattern switch
                    {
                        0 => Mathf.IsEqualApprox(speed, Read<float>(boss, "_ringSpeed")) && b.Radius == 3f,
                        1 => Mathf.IsEqualApprox(speed, Read<float>(boss, "_aimedSpeed")) && b.Radius == 4f,
                        2 => (Mathf.IsEqualApprox(speed, 64f) && b.Radius == 4f) || (Mathf.IsEqualApprox(speed, 100f) && b.Radius == 2.6f),
                        3 => Mathf.IsEqualApprox(speed, Read<float>(boss, "_spiralSpeed")) && b.Radius == 2.6f,
                        _ => (Mathf.IsEqualApprox(speed, 70f) && b.Radius == 3f)
                            || (Mathf.IsEqualApprox(speed, Read<float>(boss, "_spiralSpeed")) && b.Radius == 2.6f),
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
                && Bullets().Any(b => Read<Texture2D>(b, "_sprite") == Read<Texture2D?[][]>(boss, "_spellArt")[4][0]),
                $"Mina/{diff}: finale combines Mina's core and the three memories");
            if (diff == GameManager.Diff.Normal)
            {
                hud.AnnounceSpell("ミナ", BossHandles.MinaBattle, "心象の核＋世界中の悲鳴", new Color("e0729c"));
                foreach (var b in Bullets()) { b._PhysicsProcess(0.5d); b.SetPhysicsProcess(false); }
                await Shot("mina_finale");
                DisplayServer.WindowSetSize(new Vector2I(960, 540));
                await Frames(2);
                await Shot("mina_finale_small");
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            Pool.DespawnAll();
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
            hud.AnnounceSpell("こはる", BossHandles.KoharuMain, "自分なにしてんだろ", new Color("e8945a"));
            await Shot("aoe_Koharu_cross");
            await ClearStrikes(world);
        }
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


    private async Task CheckSafeZonePixels()
    {
        var viewport = new SubViewport { Size = new Vector2I(384, 216), TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        viewport.AddChild(new ColorRect { Size = new Vector2(384, 216), Color = new Color("102030"), ZIndex = -100 });
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
                    if (distance > radius + 6 && color.R <= background.R + 0.01f)
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

    private async Task CheckReadability(string scene, Enemy boss, Hud hud, Node2D world)
    {
        await ClearStrikes(world);
        Pool.DespawnAll();
        hud.HideBubble();
        hud.HideSpellCard();
        var fx = FxLayer.Instance;
        var particles = Read<System.Collections.Generic.List<FxLayer.P>>(fx, "_p");
        particles.Clear();
        foreach (var aura in Enum.GetValues<FxLayer.BossAura>())
        for (int i = 0; i < 20; i++) fx.EmitBossAura(aura, boss.Position, 1f, 48f);
        Check(particles.Count == 100 && particles.All(p => p.Deep && !p.Add && p.A0 <= 0.3f
            && p.Type is FxLayer.T.Rain or FxLayer.T.Steam or FxLayer.T.Sym
            && new Vector2(p.Vx, p.Vy).Length() < 35f), "all boss atmosphere is subdued, slow and behind attacks");
        particles.Clear();
        var parts = boss.GetNodeOrNull<BossParts>("Parts");
        if (parts != null)
        {
            Check(parts.GetChildren().OfType<Node2D>().All(n => !n.ZAsRelative && n.ZIndex < -2),
                "all decorative parts stay behind AOE boundaries");
            parts.EnterIdle();
            parts.OnAttackStart();
            parts._Process(0.16d);
            var objects = Read<System.Collections.IEnumerable>(parts, "_parts").Cast<object>().ToArray();
            Check(objects.All(p => (Vector2)p.GetType().GetField("Vel")!.GetValue(p)! == Vector2.Zero),
                "attack animation does not launch decorative parts as fake bullets");
        }
        var motif = scene switch { "Akari" => AreaStrike.Motif.Rain, "Koharu" => AreaStrike.Motif.Screen,
            "Rei" => AreaStrike.Motif.Stream, _ => AreaStrike.Motif.Data };
        var tint = scene switch { "Akari" => new Color("8fc4ff"), "Koharu" => new Color("ffc06a"),
            "Rei" => new Color("e394ce"), _ => new Color("ff8cc4") };
        var zone = new AreaStrike();
        if (scene == "MinaBattle") zone.ConfigureFullscreen(new Vector2(185, 150), 24, 1.6, tint, Colors.White, motif);
        else zone.Configure(AreaStrike.Shape.Circle, 28, 28, 1.6, tint, Colors.White, motif);
        world.AddChild(zone);
        if (scene != "MinaBattle") zone.Position = new Vector2(224, 122);
        zone.SetProcess(false);
        zone._Process(0.95);
        Check(zone.ZIndex == -2 && !zone.ZAsRelative, "all area attacks are below bullets and above atmosphere");
        if (scene != "MinaBattle")
        {
            var rect = new Rect2(-28, -28, 56, 56);
            Check(zone.HatchSegments(rect).All(s => s.from.Length() <= 26.01f && s.to.Length() <= 26.01f),
                "circular warning stripes do not extend outside the footprint");
        }
        else
            Check(zone.HatchSegments(Field.Rect).All(s => Geometry2D.GetClosestPointToSegment(new Vector2(185, 150), s.from, s.to)
                .DistanceTo(new Vector2(185, 150)) >= 25.99f), "fullscreen stripes never enter the safe hole");
        string art = scene switch { "Akari" => "akari_envelope", "Koharu" => "koharu_star_pin",
            "Rei" => "rei_comment", _ => "mina_memory" };
        var texture = BulletArt.Get(art);
        Check(texture != null, "readability preview uses the actual character projectile");
        for (int i = 0; i < 12; i++)
        {
            var bullet = Pool.Spawn(new Vector2(180 + i % 4 * 25, 102 + i / 4 * 19), Vector2.Left * 45, true);
            bullet.SetSprite(texture, 0);
            bullet.SetPhysicsProcess(false);
            Check(bullet.ZIndex > zone.ZIndex, "real projectiles stay above warning fills");
        }
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(540, 960) })
        {
            DisplayServer.WindowSetSize(size);
            await Shot($"readability_{scene}_{size.X}x{size.Y}");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        var phase = typeof(Enemy).GetField("_phase", Private)!;
        var before = phase.GetValue(boss);
        phase.SetValue(boss, Enum.Parse(phase.FieldType, "Exposed"));
        typeof(Enemy).GetField("_phaseT", Private)!.SetValue(boss, 0.6d);
        boss.QueueRedraw();
        await Shot($"readability_{scene}_exposed");
        phase.SetValue(boss, before);
        await ClearStrikes(world);
        Pool.DespawnAll();
    }

    private async Task ClearStrikes(Node world)
    {
        foreach (var strike in world.GetChildren().OfType<AreaStrike>()) strike.QueueFree();
        await Frames(2);
    }

    private async Task CheckBossBackgrounds(GameManager game)
    {
        foreach (string scene in new[] { "Akari", "Koharu", "Rei", "MinaBattle" })
        {
            string id = scene == "MinaBattle" ? "mina" : scene.ToLowerInvariant();
            string path = $"res://char/bg2/boss/{id}_v1.png";
            var texture = GD.Load<Texture2D>(path);
            using (var pixels = texture.GetImage())
                Check(pixels.GetWidth() >= 1280 && pixels.GetHeight() >= 1000
                    && pixels.DetectAlpha() == Image.AlphaMode.None, $"{id}: high-resolution opaque artwork");

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
            player.GlobalPosition = new Vector2(Field.Left + 55, 165);
            var hud = root.GetNode<Hud>("Hud");
            hud.HoldBubble = false;
            hud.HideBubble();
            hud.SetCinematicMode(false);
            var bg = root.GetNode<StageBackground>("StageBackground");
            var layers = bg.GetNode<BgLayers>("BgLayers");
            Enemy boss;
            if (id == "mina")
            {
                boss = new BossMina { Name = "BossMina" };
                world.AddChild(boss);
                boss.GlobalPosition = new Vector2(Field.Right - 62, 94);
                bg.EnterBoss();
            }
            else
            {
                Check(layers.GetChildren().OfType<Sprite2D>().All(s => s.Texture.ResourcePath != path),
                    $"{id}: road background is unchanged before boss spawn");
                Write(stage, "_stepStarted", false);
                Call(stage, "Step_BossSpawn");
                boss = world.GetChildren().OfType<Enemy>().Single();
                await Frames(12);
                var entering = layers.GetChildren().OfType<Sprite2D>().Single(s => s.Texture.ResourcePath == path);
                Check(entering.Modulate.A > 0 && entering.Modulate.A < 1 && layers.BossDimK > 0,
                    $"{id}: actual boss spawn starts a crossfade");
                int tiles = layers.GetChildCount();
                bg.EnterBoss();
                Check(layers.GetChildCount() == tiles, $"{id}: repeated boss entry does not duplicate artwork");
            }
            await Frames(350);
            boss.SetPhysicsProcess(false);
            var caster = Read<Node>(boss, "_caster");
            caster.SetProcess(false);
            if (caster is AreaSpellCaster areaCaster) areaCaster.CancelPendingAttacks();
            else ((MinaPhaseAttacks)caster).CancelPendingAttacks();
            await ClearStrikes(world);
            Pool.DespawnAll();
            hud.HideBubble();
            boss.GlobalPosition = new Vector2(Field.Right - 62, 94);
            var art = layers.GetChildren().OfType<Sprite2D>().Single();
            Check(art.Texture.ResourcePath == path && art.Modulate.A == 1 && art.Modulate.R >= 0.8f,
                $"{id}: dedicated artwork replaces old layers without double dimming");
            foreach (float x in new[] { Field.Left, Field.Right, Field.CenterX })
            {
                game.TickProgress(x, 0);
                await Frames(120);
                var bounds = new Rect2(art.GlobalPosition, art.Texture.GetSize() * art.GlobalScale);
                Check(bounds.Position.X <= Field.Left && bounds.End.X >= Field.Right
                    && bounds.Position.Y <= 0 && bounds.End.Y >= Field.Bottom
                    && Mathf.Abs(art.Scale.X - art.Scale.Y) < 0.00001f,
                    $"{id}: full field covered at player x={x} without distortion");
            }
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                await Shot($"background_{id}_{size.X}x{size.Y}");
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var motif = id switch { "akari" => AreaStrike.Motif.Rain, "koharu" => AreaStrike.Motif.Screen,
                "rei" => AreaStrike.Motif.Stream, _ => AreaStrike.Motif.Data };
            var color = id switch { "akari" => new Color("78bdf4"), "koharu" => new Color("e59b73"),
                "rei" => new Color("d8b0ff"), _ => new Color("df85b6") };
            var strike = new AreaStrike();
            world.AddChild(strike);
            if (id is "rei" or "mina")
                strike.ConfigureFullscreen(new Vector2(Field.Left + 90, 120), 24, 1.4, color, Colors.White, motif);
            else
            {
                strike.GlobalPosition = new Vector2(Field.CenterX, 120);
                strike.Configure(AreaStrike.Shape.Circle, 35, 35, 1.4, color, Colors.White, motif);
            }
            strike.SetProcess(false);
            strike._Process(0.9);
            await Shot($"background_{id}_aoe");
            strike.QueueFree();
            await Frames(2);
            if (id == "mina")
            {
                var journey = root.GetNode<StageBackground>("JourneyBackground").GetNode<BgLayers>("BgLayers");
                foreach (var (phase, memory) in new[] { (1, "akari"), (2, "koharu"), (3, "rei"), (4, "mina") })
                {
                    Write(boss, "_pattern", phase);
                    Call(root, "TickJourney");
                    await Frames(220);
                    var memoryArt = journey.GetChildren().OfType<Sprite2D>().ToArray();
                    Check(memory == "mina" ? memoryArt.Length == 0
                        : memoryArt.Length == 1 && memoryArt[0].Texture.ResourcePath == $"res://char/bg2/boss/{memory}_v1.png",
                        $"Mina journey follows the active {memory} encounter phase");
                    await Shot($"background_mina_memory_{memory}");
                }
            }
            root.QueueFree();
            await Frames(8);
            Pool.DespawnAll();
            Hud.BubblePaused = false;

            if (id == "mina") continue;
            game.SelectedEntry = GameManager.StageEntry.Boss;
            var retry = GD.Load<PackedScene>($"res://{scene}.tscn").Instantiate<Node2D>();
            GetTree().Root.AddChild(retry);
            GetTree().CurrentScene = retry;
            await Frames(120);
            var retryArt = retry.GetNode<StageBackground>("StageBackground").GetNode<BgLayers>("BgLayers")
                .GetChildren().OfType<Sprite2D>().ToArray();
            Check(retryArt.Length == 1 && retryArt[0].Texture.ResourcePath == path,
                $"{id}: boss checkpoint also loads the dedicated artwork");
            retry.QueueFree();
            await Frames(8);
            Pool.DespawnAll();
            Hud.BubblePaused = false;
        }
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
