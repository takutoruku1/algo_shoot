using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossDownQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const string Out = "res://build/qa_story/boss_down/shots";
    private readonly record struct Pose(Texture2D Texture, Vector2 Scale, Vector2 Offset, bool Flip);
    private readonly List<(string Id, Pose Idle, Pose Down)> _samples = new();
    private AkariRoot _root = null!;
    private bool _shots;
    private bool _breakShots;
    private BulletPool Pool => GetNode<BulletPool>("/root/Pool");
    private static FieldInfo FieldOf(object value, string name)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Private) is { } field) return field;
        throw new MissingFieldException(name);
    }
    private static T Read<T>(object value, string name) => (T)FieldOf(value, name).GetValue(value)!;
    private static void Write(object value, string name, object data) => FieldOf(value, name).SetValue(value, data);
    private static object? Call(object value, string name, params object[] args)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetMethod(name, Private) is { } method) return method.Invoke(value, args);
        throw new MissingMethodException(name);
    }
    private static void Check(bool pass, string label)
    {
        if (!pass) throw new Exception(label);
        GD.Print($"[BossDownQA] PASS {label}");
    }
    private static Sprite2D Body(Enemy boss) => boss.GetNode<Sprite2D>("Body");
    private static Pose Capture(Enemy boss)
    {
        var s = Body(boss);
        return new Pose(s.Texture, s.Scale, s.Offset, s.FlipH);
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _breakShots = OS.GetCmdlineUserArgs().Contains("--break-shots");
            _shots = _breakShots || OS.GetCmdlineUserArgs().Contains("--down-shots");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            game.MarkIdleDialogSeen("once_midboss_shield");
            game.Difficulty = GameManager.Diff.Normal;
            if (_shots)
            {
                DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Out));
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            await Frames(1);
            _root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
            GetTree().Root.AddChild(_root);
            GetTree().CurrentScene = _root;
            _root.SetProcess(false);
            _root.Stage.SetProcess(false);
            _root.World.ProcessMode = ProcessModeEnum.Inherit;
            _root.Player.ProcessMode = ProcessModeEnum.Disabled;
            Write(_root.Player, "_invincible", true);
            Write(_root.Player, "_invincibleTimer", 999f);
            _root.Player.GlobalPosition = new Vector2(Field.Left + 30, 165);
            ClearDialog();
            _root.GetNode<StageBackground>("StageBackground").EnterBoss();
            await Frames(3);

            foreach (var id in new[] { "akari", "koharu", "rei", "mina", "akari_mid", "koharu_mid", "rei_mid" })
            {
                Enemy boss = id switch
                {
                    "akari" => new BossAkari(), "koharu" => new BossKoharu(),
                    "rei" => new BossRei(), "mina" => new BossMina(), _ => Cameo(id),
                };
                boss.ProcessMode = ProcessModeEnum.Disabled;
                _root.World.AddChild(boss);
                boss.GlobalPosition = new Vector2(Field.BossCenterX, 100);
                Call(boss, "TickEntrance", 0d);
                Call(boss, "TickEntrance", 100d);
                Call(boss, "TickSwapAnim", 1d);
                await Frames(3);
                if (_breakShots)
                {
                    await CheckBreakCallout(boss, id, game);
                    boss.QueueFree();
                    await Frames(4);
                    Pool.DespawnAll();
                    continue;
                }
                await Cycle(boss, id);

                if (id is "akari" or "koharu" or "rei")
                {
                    Strip(boss);
                    Check((bool)Call(boss, "AdvanceForm2")!, id + " second form starts while down");
                    await FinishTransformation(boss);
                    string down = BossDownArt.BreathingPath(BossAnimalArt.Path(id, "down"), 0);
                    Check(Body(boss).Texture.ResourcePath == down, id + " second form keeps down pose");
                    Call(boss, "EnterReclose");
                    Call(boss, "EnterShielded");
                    Call(boss, "TickSwapAnim", 1d);
                    await Cycle(boss, id + "_animal");
                }
                if (boss is BossMina mina)
                {
                    foreach (var costume in new[] { "rain", "screen", "stream", "home" })
                    {
                        Strip(mina);
                        game.Difficulty = GameManager.Diff.Lunatic;
                        Call(mina, "BeginPhaseTransition");
                        game.Difficulty = GameManager.Diff.Normal;
                        Check(Body(mina).Texture.ResourcePath == BossDownArt.BreathingPath(BossMina.BattleDownPath(mina.EncounterPhase), 0), costume + " changes costume while down");
                        Call(mina, "CompletePhaseTransition");
                        await FinishTransformation(mina);
                        Read<MinaPhaseAttacks>(mina, "_caster").CancelPendingAttacks();
                        Call(mina, "EnterReclose");
                        Call(mina, "EnterShielded");
                        Call(mina, "TickSwapAnim", 1d);
                        await Cycle(mina, "mina_" + costume);
                    }
                }
                await LiveCycle(boss, id);
                Strip(boss);
                Call(boss, "Redeem");
                Check(!Read<bool>(boss, "_bodyDown"), id + " purification leaves down state");
                string cry = Body(boss).Texture.ResourcePath;
                Check(!cry.Contains("/down/") && !cry.Contains("/mina_dragon/") && !BossAnimalArt.Contains(Body(boss).Texture), id + " purification uses existing story art");
                Call(boss, "TriggerAttackPose");
                Call(boss, "TickAttackPose", 1d);
                Check(Body(boss).Texture.ResourcePath == cry, id + " attack timer cannot overwrite story art");
                if (boss is BossRei)
                {
                    Check(cry.EndsWith("boss_rei_body_idle2.png"), "Rei preserves her cracked shell until story reveal");
                    Call(boss, "BreakCryBodyNow");
                    Call(boss, "TickShellPeel", 3d);
                    Check(Body(boss).Texture.ResourcePath == Read<string>(boss, "CryTexPath"), "Rei delayed transformation still works");
                }
                ClearDialog();
                boss.QueueFree();
                await Frames(4);
                Pool.DespawnAll();
            }
            if (!_breakShots)
            {
                Check(_samples.Count == 14, "all fourteen boss and phase down states exercised");
                if (_shots) await Comparison();
            }
            _root.QueueFree();
            await Frames(6);
            Pool.DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var player in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { player.Stop(); player.Stream = null; }
            await Frames(4);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(4);
            GD.Print("[BossDownQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError($"[BossDownQA] FAIL {e}"); GetTree().Quit(1); }
    }

    private void ClearDialog()
    {
        _root.Hud.HoldBubble = false;
        _root.Hud.HideBubble();
        _root.Hud.SuppressCallouts = false;
    }

    private async Task FinishTransformation(Enemy boss)
    {
        for (int i = 0; i < 300 && boss.Transforming; i++) await Frames(1);
        Check(!boss.Transforming && !_root.Hud.CinematicMode, "form reveal finishes and restores combat");
    }

    private async Task CheckBreakCallout(Enemy boss, string id, GameManager game)
    {
        var cue = _root.Hud.Bubbles!.ShieldBreak;
        Check(cue.ZIndex < -12 && !cue.ZAsRelative, "break portraits stay behind bullets and attack telegraphs");
        foreach (var job in Jobs.All)
        {
            game.SelectedJob = job.Id;
            ClearDialog();
            Call(boss, "EnterShielded");
            Strip(boss);
            Call(boss, "EnterExposed");
            _root.Hud.HideSpellCard();
            _root.Hud.ShowBossLine("Enemy", "A competing enemy line", Colors.Red, 4);
            await Frames(24);
            Check(cue.Active && !Hud.BubblePaused && Read<Enemy>(cue, "_owner") == boss,
                $"{id}/{job.CharacterId}: real shield destruction shows a nonblocking callout");
            Check(Read<string>(cue, "_speaker") == job.CharacterName
                && Read<string>(cue, "_line").Contains("シールド") && Read<string>(cue, "_line").Contains("本体"),
                "the selected player explains the opening, independently of enemy dialogue");
            var portrait = Read<Texture2D>(cue, "_portrait");
            Check(!portrait.ResourcePath.Contains("player") && !portrait.ResourcePath.Contains("boss")
                && UiKit.ContentRect(portrait).HasArea(), "callout uses nonblank standing artwork, not a shooting sprite");
            Check(UiKit.WrapLines(UiKit.ZenBold, Read<string>(cue, "_line"), 24, 540).Count == 2,
                "character dialogue fits two lines");
            if (id == "akari")
            {
                await SaveBreakShot($"break_{job.CharacterId}");
                if (job.Id == Job.Magic) await CheckBreakLayering(cue);
                DisplayServer.WindowSetSize(new Vector2I(960, 540));
                await Frames(5);
                await SaveBreakShot($"break_{job.CharacterId}_small");
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            Call(boss, "EnterReclose");
            Check(!cue.Active, "the chance disappears immediately when the damage window closes early");
            Call(boss, "EnterShielded");
            Strip(boss);
            Check(cue.Active, "the next break starts a fresh callout");
            _root.Hud.SuppressCallouts = true;
            await Frames(2);
            Check(!cue.Active, "story interruptions clear the callout");
            _root.Hud.SuppressCallouts = false;
            Check(!cue.Active, "an old chance never reappears after an interruption");
            Call(boss, "EnterReclose");
        }
    }

    private async Task SaveBreakShot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        Check(frame.SavePng($"{Out}/{name}.png") == Error.Ok, name + " screenshot saved");
    }

    private async Task CheckBreakLayering(ShieldBreakCallout cue)
    {
        var strike = new AreaStrike();
        strike.Configure(AreaStrike.Shape.Circle, 24, 24, 10, new Color("ed715d"), Colors.White);
        _root.World.AddChild(strike);
        strike.GlobalPosition = new Vector2(155, 146);
        strike.SetProcess(false);
        var bullet = Pool.Spawn(new Vector2(157, 137), Vector2.Zero, true);
        bullet.SetProcess(false);
        Check(strike.ZIndex > cue.ZIndex && bullet.ZIndex > cue.ZIndex, "real AOE and hostile bullet render above the portrait");
        await Frames(4);
        await SaveBreakShot("break_rei_attacks_foreground");
        strike.QueueFree();
        Pool.Despawn(bullet);
        await Frames(2);
    }
    private static CameoBoss Cameo(string id)
    {
        string file = id == "akari_mid" ? "akari_mid_v2" : id;
        return new CameoBoss { Theme = new CameoTheme
        {
            DisplayName = id, Handle = id,
            PreTex = $"res://char/v3/{file}.png", CryTex = $"res://char/v3/{id}.png", PostTex = $"res://char/v3/{id}.png",
            Fire = id == "akari_mid" ? CameoFireTheme.AkariGrief : id == "koharu_mid" ? CameoFireTheme.KoharuFalling : CameoFireTheme.ReiAggressive,
            SpellTint = Colors.White, Face = "",
            IntroLines = Array.Empty<(int, string, string)>(), TauntLines = Array.Empty<(int, string, string)>(),
            DefeatLines = Array.Empty<(int, string, string)>(),
        } };
    }
    private static void Strip(Enemy boss)
    {
        foreach (var panel in Read<List<Panel>>(boss, "_panels").ToArray())
        { boss.OnPanelStripped(panel); panel.QueueFree(); }
    }
    private async Task Cycle(Enemy boss, string id, bool keepSample = true)
    {
        ClearDialog();
        var normal = Capture(boss);
        var shape = Read<CollisionShape2D>(boss, "_bodyShape");
        var shapeResource = shape.Shape;
        var shapeTransform = shape.Transform;
        Call(boss, "TriggerAttackPose");
        Strip(boss);
        string down = BossDownArt.BreathingPath(boss is BossMina mina ? BossMina.BattleDownPath(mina.EncounterPhase) : BossDownArt.Path(id), 0);
        Check(Read<bool>(boss, "_bodyDown") && Body(boss).Texture.ResourcePath == down, id + " shield break immediately selects down art");
        Check(Read<double>(boss, "_attackPoseT") == 0, id + " cancels pending attack pose");
        Call(boss, "TriggerAttackPose");
        Call(boss, "TickAttackPose", 1d);
        Call(boss, "TickSwapAnim", 1d);
        Check(Body(boss).Texture.ResourcePath == down, id + " attacks cannot replace down art");
        Check(shape.Shape == shapeResource && shape.Transform == shapeTransform, id + " collision geometry unchanged");
        var parts = boss.GetNodeOrNull<Node2D>("Parts");
        Check(parts == null || !parts.Visible, id + " offensive ornaments hidden during down");
        Call(boss, "ApplyBossMotion", new Vector2(0, 1), 0.2f, true, null!);
        Check(Mathf.Abs(Body(boss).Rotation) <= 0.031f, id + " restrained down motion");
        Call(boss, "ApplyBossMotion", Vector2.Zero, 0f, false, null!);
        Check(float.IsFinite(Body(boss).Offset.X) && Body(boss).Scale.X > 0, id + " mirrored pose remains valid");
        Call(boss, "ApplyBossMotion", Vector2.Zero, 0f, true, null!);
        if (keepSample)
        {
            var downPose = Capture(boss);
            _samples.Add((id, normal, downPose));
            if (boss is BossMina { IsDragonForm: true })
            {
                float spanRatio = downPose.Texture.GetWidth() * downPose.Scale.X / (normal.Texture.GetWidth() * normal.Scale.X);
                Check(Mathf.Abs(spanRatio - 1) < .01f, id + " dragon preserves its wingspan");
            }
            else if (BossAnimalArt.Contains(normal.Texture))
            {
                float ratio = downPose.Texture.GetHeight() * downPose.Scale.Y / (normal.Texture.GetHeight() * normal.Scale.Y);
                Check(ratio > .7f && ratio < 1.3f, id + " down drawing keeps animal scale");
            }
            else
            {
                var (idleHead, downHead) = id switch
                {
                    "akari" => (230f, 202f), "koharu" => (228f, 217f),
                    "rei" => (217f, 221f), "rei_form2" => (217f, 234f), "mina" => (295f, 208f),
                    "mina_rain" => (299f, 319f), "mina_screen" => (333f, 325f),
                    "mina_stream" => (329f, 328f), "mina_home" => (351f, 328f),
                    "akari_mid" => (115f, 219f), "koharu_mid" => (110f, 212f),
                    _ => (132f, 246f),
                };
                float ratio = downHead * downPose.Scale.X / (idleHead * normal.Scale.X);
                Check(Mathf.Abs(ratio - 1) < 0.035f, $"{id} head size matches idle ({ratio:F3})");
            }
        }
        Call(boss, "TickBossPhase", 0.46d);
        await Frames(2);
        Check(boss.Monitoring && boss.GetCollisionMaskValue(2) && !shape.Disabled, id + " exposed hitbox opens normally");
        ClearDialog();
        int hp = Read<int>(boss, "_hp");
        var bullet = Pool.Spawn(new Vector2(-100, -100), Vector2.Zero, false, damage: 1);
        Call(boss, "OnBodyHitByPlayerBullet", bullet);
        Check(Read<int>(boss, "_hp") < hp, id + " player shot still damages down boss");
        Call(boss, "TickBossPhase", 3.9d);
        Check(Body(boss).Texture.ResourcePath == down && boss.GaugeVulnerable, id + " down persists through exposed window");
        Call(boss, "TickBossPhase", 0.11d);
        Check(!Read<bool>(boss, "_bodyDown") && Body(boss).Texture.ResourcePath == Read<string>(boss, "PreTexPath"), id + " reclose restores current idle costume");
        Check(!boss.GetCollisionMaskValue(2) && (parts == null || parts.Visible == !BossAnimalArt.Contains(Body(boss).Texture)), id + " shield and ornament visibility recover");
        Call(boss, "TickBossPhase", 1.36d);
        Check(Read<List<Panel>>(boss, "_panels").Count > 0, id + " shields respawn for next cycle");
        Call(boss, "TickSwapAnim", 1d);
        Pool.DespawnAll();
        await Frames(2);
    }

    private async Task LiveCycle(Enemy boss, string id)
    {
        ClearDialog();
        var hostile = Pool.Spawn(boss.GlobalPosition, Vector2.Zero, true);
        var strike = new AreaStrike();
        strike.Configure(AreaStrike.Shape.Circle, 20, 20, 1, Colors.Red, Colors.White);
        strike.SetOwner(boss);
        _root.World.AddChild(strike);
        foreach (var child in boss.GetChildren())
        {
            if (child is AreaSpellCaster caster) Call(caster, "Cast");
            if (child is MinaPhaseAttacks minaCaster)
                for (int i = 0; i < 100; i++) minaCaster._Process(.02);
        }
        var playerShot = Pool.Spawn(new Vector2(-100, -100), Vector2.Zero, false);
        Strip(boss);
        Check(!hostile.Active && playerShot.Active, id + " break clears hostile bullets but preserves player shots");
        boss.ProcessMode = ProcessModeEnum.Inherit;
        boss.SetProcess(false);
        boss.SetPhysicsProcess(true);
        foreach (var child in boss.GetChildren())
            if (child is AreaSpellCaster or MinaPhaseAttacks) child.SetProcess(true);
        await Frames(28);
        Check(!IsInstanceValid(strike) || strike.IsQueuedForDeletion(), id + " break cancels existing AOE");
        var frames = new HashSet<string>();
        var origin = boss.GlobalPosition;
        for (int i = 0; i < 100; i++)
        {
            await Frames(1);
            var body = Body(boss);
            frames.Add(body.Texture.ResourcePath);
            if (!body.Position.IsZeroApprox() || !Mathf.IsZeroApprox(body.Rotation) || boss.GlobalPosition != origin)
                throw new Exception(id + " moves the whole sprite instead of breathing through shoulders");
            if (GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Any(b => b.Active)
                || GetTree().GetNodesInGroup("aoe").Any(n => !n.IsQueuedForDeletion()))
                throw new Exception(id + " attacks during down");
        }
        Check(frames.Count == 3 && frames.All(p => p.Contains("_breath_")), id + " three shoulder-breathing drawings animate without whole-body motion");
        Check(Read<bool>(boss, "_bodyDown") && boss.GaugeVulnerable, id + " live physics holds down during combat");
        Check(_root.Hud.Bubbles!.ShieldBreak.Active, id + " opportunity callout follows the live damage window");
        Pool.DespawnAll();
        _root.Hud.HideSpellCard();
        if (_shots)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng($"{Out}/{id}_battle.png") == Error.Ok, id + " battle screenshot saved");
        }
        for (int i = 0; i < 600 && Read<bool>(boss, "_bodyDown"); i++) await Frames(1);
        Check(!Read<bool>(boss, "_bodyDown"), id + " live physics recovers from down without intervention");
        Check(!_root.Hud.Bubbles!.ShieldBreak.Active, id + " opportunity callout ends with the live damage window");
        boss.ProcessMode = ProcessModeEnum.Disabled;
        foreach (var child in boss.GetChildren())
            if (child is AreaSpellCaster or MinaPhaseAttacks) child.SetProcess(false);
        Call(boss, "EnterShielded");
        Pool.DespawnAll();
    }

    private async Task Comparison()
    {
        var viewport = new SubViewport { Size = new Vector2I(2560, 1320), TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Disable3D = true };
        AddChild(viewport);
        viewport.AddChild(new ColorRect { Size = new Vector2(2560, 1320), Color = new Color("20232a") });
        var ordered = _samples.OrderBy(s => s.Id.Contains("_mid") ? 2 : s.Id.StartsWith("mina_") ? 1 : 0).ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            var item = ordered[i];
            var origin = new Vector2(i % 4 * 640, i / 4 * 330);
            var label = new Label { Text = item.Id.ToUpperInvariant(), Position = origin + new Vector2(14, 10) };
            label.AddThemeFontSizeOverride("font_size", 19);
            viewport.AddChild(label);
            var states = new Label { Text = "NORMAL                                           DOWN", Position = origin + new Vector2(110, 43) };
            states.AddThemeFontSizeOverride("font_size", 15);
            viewport.AddChild(states);
            int column = 0;
            foreach (var pose in new[] { item.Idle, item.Down })
            {
                viewport.AddChild(new Sprite2D { Texture = pose.Texture, Offset = pose.Offset, FlipH = pose.Flip,
                    Scale = pose.Scale * 2.2f, Position = origin + new Vector2(160 + column++ * 320, 192),
                    TextureFilter = CanvasItem.TextureFilterEnum.Linear });
                using var art = pose.Texture.GetImage();
                Check(art.GetUsedRect().HasArea() && art.GetPixel(0, 0).A < 0.05f, item.Id + " nonblank transparent texture");
            }
        }
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var render = viewport.GetTexture().GetImage();
        for (int i = 0; i < ordered.Length; i++)
        {
            int pixels = 0;
            for (int y = i / 4 * 330 + 80; y < i / 4 * 330 + 300; y += 3)
            for (int x = i % 4 * 640 + 330; x < i % 4 * 640 + 630; x += 3)
                if ((render.GetPixel(x, y) - new Color("20232a")).R > 0.12f) pixels++;
            Check(pixels > 70, ordered[i].Id + " down sprite renders nonblank");
        }
        Check(render.SavePng($"{Out}/comparison.png") == Error.Ok, "comparison screenshot saved");
        viewport.QueueFree();
        await Frames(2);
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
