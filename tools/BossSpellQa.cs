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
            if (OS.GetCmdlineUserArgs().Contains("--koharu-body"))
                await CheckKoharuBody(game);
            else if (OS.GetCmdlineUserArgs().Contains("--break"))
                await CheckBreakEffect(game);
            else if (OS.GetCmdlineUserArgs().Contains("--backgrounds"))
                await CheckBossBackgrounds(game);
            else
            {
                bool clipsOnly = OS.GetCmdlineUserArgs().Contains("--rei-clips");
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
                    root.QueueFree();
                    await Frames(5);
                    Pool.DespawnAll();
                    Hud.BubblePaused = false;
                }
                await CheckSafeZonePixels();
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
        BossBreakFx[] Effects() => FxLayer.Instance.GetChildren().OfType<BossBreakFx>().ToArray();
        string Phase(Enemy boss) => Read<object>(boss, "_phase", typeof(Enemy)).ToString()!;
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
            Check(!Read<System.Collections.Generic.List<FxLayer.P>>(FxLayer.Instance, "_p").Any(p => p.Text == "BREAK!"),
                "old floating damage-number label is not duplicated");
            boss.Purify();
            Check(Effects().Length == 1, "repeated purify does not stack announcements");
            foreach (var position in new[] { new Vector2(Field.Left, 51), new Vector2(Field.Right, 51),
                new Vector2(Field.Left, 148), new Vector2(Field.Right, 148) })
            {
                effect.PlaceAbove(position, 56);
                Check(effect.Position.X - 76 >= Field.Left && effect.Position.X + 76 <= Field.Right
                    && effect.Position.Y - 25 >= 21 && effect.Position.Y + 25 < 162,
                    "edge placement keeps letters clear of sidebar, boss HP and dialogue caption");
            }
            effect.PlaceAbove(boss.Position, Read<float>(boss, "BodyDisplayH", typeof(Enemy)));
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540), new Vector2I(540, 960) })
            {
                DisplayServer.WindowSetSize(size);
                foreach (float time in new[] { 0.07f, 0.22f, 0.48f })
                {
                    Write(effect, "_age", time);
                    effect.QueueRedraw();
                    await Frames(3);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage();
                    Check(image.SavePng($"{output}/{scene}_{size.X}_{time:0.00}.png") == Error.Ok, "rendered animation keyframe");
                    if (time == 0.22f)
                    {
                        int white = 0;
                        float scale = image.GetWidth() / 384f;
                        for (int y = (int)((effect.Position.Y - 14) * scale); y < (effect.Position.Y + 14) * scale; y++)
                            for (int x = (int)((effect.Position.X - 50) * scale); x < (effect.Position.X + 50) * scale; x++)
                            {
                                Color c = image.GetPixel(x, y);
                                if (c.R > 0.85f && c.G > 0.85f && c.B > 0.85f) white++;
                            }
                        Check(white > 80 * scale, $"{scene}/{size}: BREAK lettering is visible, not a blank canvas");
                    }
                }
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            Write(effect, "_age", 0.1f);
            effect.SetProcess(true);
            GetTree().Paused = true;
            await Task.Delay(80);
            Check(Read<float>(effect, "_age") == 0.1f, "pause menu freezes the animation");
            GetTree().Paused = false;
            effect.SetProcess(false);
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
                && Bullets().Any(b => Read<Texture2D>(b, "_sprite").ResourcePath.Contains("mina_core")),
                $"Mina/{diff}: finale combines Mina's core and the three memories");
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
