using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossMotionQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private MinaRoot _root = null!;
    private GameManager _game = null!;
    private bool _transformPreview;
    private Label? _previewName;
    private readonly List<(string Name, BossTransformation.Frame Frame)> _animalComparison = new();
    private static object? Call(object value, string name, params object[] args)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetMethod(name, Private) is { } method) return method.Invoke(value, args);
        throw new MissingMethodException(name);
    }
    private static T Read<T>(object value, string name)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Private) is { } field) return (T)field.GetValue(value)!;
        throw new MissingFieldException(name);
    }
    private static void Check(bool pass, string label)
    {
        if (!pass) throw new Exception(label);
        GD.Print("[BossMotionQA] PASS " + label);
    }
    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save");
            _game = GetNode<GameManager>("/root/Game");
            _game.AutoSaveEnabled = false;
            _game.ResetPersistent();
            _game.Difficulty = GameManager.Diff.Lunatic;
            _game.MarkIdleDialogSeen("once_midboss_shield");
            Engine.MaxFps = 60;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            _root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<MinaRoot>();
            GetTree().Root.AddChild(_root);
            GetTree().CurrentScene = _root;
            _root.SetProcess(false);
            _root.Stage.SetProcess(false);
            _root.World.ProcessMode = ProcessModeEnum.Inherit;
            _root.Player.ProcessMode = ProcessModeEnum.Disabled;
            _root.Hud.HideBubble();
            _root.GetNode<StageBackground>("StageBackground").EnterBoss();
            await Frames(3);
            _transformPreview = OS.GetCmdlineUserArgs().Contains("--transform-preview");
            bool preview = _transformPreview || OS.GetCmdlineUserArgs().Contains("--preview");
            if (!preview || _transformPreview)
            {
                foreach (Enemy boss in new Enemy[] { new BossAkari(), new BossKoharu(), new BossRei() })
                {
                    Spawn(boss);
                    Check(!(bool)Call(boss, "TickForm2Technique", 100d, Read<BossMover>(boss, "_mover"), Read<AreaSpellCaster>(boss, "_caster"))!,
                        "first form cannot use animal techniques");
                    Call(boss, "TriggerAttackPose");
                    Call(boss, "TickSwapAnim", 1d);
                    Call(boss, "TickAttackPose", .4d);
                    Check(boss.GetNode<Sprite2D>("Body").Texture.ResourcePath.Contains("_recover_"), boss.GetType().Name + " recovery artwork");
                    Call(boss, "TickAttackPose", .2d);
                    Call(boss, "TickSwapAnim", 1d);
                    // 変身演出（BossTransformation）はルナティックでは出ない＝止まらない難易度なので
                    //   Enemy.RevealForm が「止めない合図」へ差し替える。この QA は物語の割り込みを
                    //   黙らせるためにルナティックで走っているので、演出を撮る区間だけ Hard へ降りる。
                    _game.Difficulty = GameManager.Diff.Hard;
                    Check((bool)Call(boss, "AdvanceForm2")!, "form two starts");
                    await CheckReveal(boss, checkPause: !preview);
                    _game.Difficulty = GameManager.Diff.Lunatic;
                    var body = boss.GetNode<Sprite2D>("Body");
                    string idle = body.Texture.ResourcePath;
                    Check(BossAnimalArt.Contains(body.Texture) && idle.Contains("_idle_"), "second form uses approved animal art");
                    Check(!body.FlipH, "animal form faces the player on its left");
                    _animalComparison.Add((boss.UnfolderStyle switch
                    {
                        UnfolderKind.Akari => "あかり", UnfolderKind.Koharu => "こはる", _ => "レイ",
                    }, BossTransformation.Frame.Capture(body)));
                    await CheckAnimalForm(boss);
                    Call(boss, "TriggerAttackPose");
                    Call(boss, "TickSwapAnim", 1d);
                    Check(BossAnimalArt.Contains(body.Texture) && body.Texture.ResourcePath.Contains("_attack_"), "animal attack cannot revert to human");
                    await Shot(boss.GetType().Name + "_animal_attack");
                    Call(boss, "TickAttackPose", 1d);
                    Call(boss, "TickSwapAnim", 1d);
                    Check(body.Texture.ResourcePath == idle, "animal idle resumes after attack");
                    Call(boss, "TickForm2Technique", 100d, Read<BossMover>(boss, "_mover"), Read<AreaSpellCaster>(boss, "_caster"));
                    var interrupted = Read<BossAnimalTechnique>(boss, "_animalTechnique");
                    foreach (var panel in Read<List<Panel>>(boss, "_panels").ToArray())
                    {
                        boss.OnPanelStripped(panel);
                        panel.QueueFree();
                    }
                    interrupted._PhysicsProcess(0.01);
                    Check(interrupted.Finished && !boss.AnimalTechniqueActive, "shield break cancels animal windup and pending volleys");
                    Call(boss, "TickSwapAnim", 1d);
                    var breaths = new HashSet<string>();
                    for (int frame = 0; frame < 60; frame++)
                    {
                        Call(boss, "TickDownBreathing", 1d / 60);
                        breaths.Add(body.Texture.ResourcePath);
                    }
                    Check(breaths.Count == 3 && breaths.All(p => p.Contains("_animal_breath_")), "animal down uses three shoulder-breathing drawings");
                    await Shot(boss.GetType().Name + "_animal_down");
                    if (_transformPreview)
                    {
                        for (int frame = 0; frame < 90; frame++)
                        {
                            Call(boss, "TickDownBreathing", 1d / 60);
                            await Frames(1);
                        }
                    }
                    Call(boss, "EnterReclose");
                    Call(boss, "TickSwapAnim", 1d);
                    Check(body.Texture.ResourcePath == idle, "shield recovery retains animal form");
                    boss.QueueFree();
                    await Frames(3);
                }
            }
            var mina = new BossMina();
            Spawn(mina);
            Call(mina, "BeginPhaseTransition");
            Call(mina, "CompletePhaseTransition");
            Call(mina, "TickSwapAnim", 1d);
            await Frames(40);
            // BeginPhaseTransition はルナティックのまま呼ぶ＝段間カットシーン（MinaPhaseScene）を立てずに
            //   手回しで段を進める。龍形態の変身演出を撮るのは CompletePhaseTransition の側なので、
            //   そこだけ Hard へ降りる（上の3ボスと同じ理由）。
            Call(mina, "BeginPhaseTransition");
            _game.Difficulty = GameManager.Diff.Hard;
            Call(mina, "CompletePhaseTransition");
            await CheckReveal(mina, checkPause: !preview);
            _game.Difficulty = GameManager.Diff.Lunatic;
            if (_animalComparison.Count == 3)
            {
                Call(mina, "SetMotionFrame", GD.Load<Texture2D>(BossMina.BattleCostumePath(2, "flap_level")));
                _animalComparison.Add(("ミナ（基準・変更なし）", BossTransformation.Frame.Capture(mina.GetNode<Sprite2D>("Body"))));
                await CompareAnimalSizes();
            }
            if (!_transformPreview)
            {
            Read<MinaPhaseAttacks>(mina, "_caster").SetProcess(false);
            var frames = new HashSet<string>();
            mina.SetPhysicsProcess(true);
            for (int i = 0; i < 180; i++)
            {
                await Frames(1);
                var sprite = mina.GetNode<Sprite2D>("Body");
                if (!Mathf.IsZeroApprox(sprite.Rotation)) throw new Exception("dragon banks during a flap");
                string path = sprite.Texture.ResourcePath;
                if (frames.Add(path)) await Shot("dragon_" + System.IO.Path.GetFileNameWithoutExtension(path));
                var rect = sprite.GetRect();
                var corners = new[] { rect.Position, rect.End, new Vector2(rect.Position.X, rect.End.Y), new Vector2(rect.End.X, rect.Position.Y) };
                if (!corners.Select(sprite.ToGlobal).All(p => p.X >= Field.Left - 1 && p.X <= Field.Right + 1 && p.Y >= 18 && p.Y <= Field.Bottom))
                    throw new Exception("flapping wing canvas clips the playfield: " + path);
            }
            Check(frames.Count == 3 && frames.All(p => p.Contains("flap_")), "three symmetric wing positions, without diagonal idle or banking");
            mina.ShowSignaturePose();
            await Frames(6);
            Check(mina.GetNode<Sprite2D>("Body").Texture.ResourcePath == BossMina.BattleCostumePath(2, "attack"), "attack overrides flapping");
            await Frames(65);
            Check(frames.Contains(mina.GetNode<Sprite2D>("Body").Texture.ResourcePath), "flapping resumes after attack");
            if (!preview)
            {
                _root.Hud.HoldBubble = true;
                _root.Hud.ShowDialog(Hud.LineKind.Mina, "Motion QA");
                double wingTime = Read<double>(mina, "_wingTime");
                await Frames(15);
                Check(Read<double>(mina, "_wingTime") == wingTime, "dialogue freezes wing animation");
                _root.Hud.HoldBubble = false;
                _root.Hud.HideBubble();
            }
            foreach (var panel in Read<List<Panel>>(mina, "_panels").ToArray())
            {
                mina.OnPanelStripped(panel);
                panel.QueueFree();
            }
            await Frames(50);
            Check(BossDownArt.IsBreathing(mina.GetNode<Sprite2D>("Body").Texture), "shield break selects breathing art");
            await Shot("dragon_down_live");
            var breaths = new HashSet<string>();
            for (int i = 0; i < 120; i++)
            {
                await Frames(1);
                var body = mina.GetNode<Sprite2D>("Body");
                breaths.Add(body.Texture.ResourcePath);
                if (!body.Position.IsZeroApprox() || !Mathf.IsZeroApprox(body.Rotation))
                    throw new Exception("down breathing moves the entire sprite");
                if (GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Any(b => b.Active))
                    throw new Exception("dragon attacks while down");
            }
            Check(breaths.Count == 3 && breaths.All(p => p.Contains("mina_dragon_breath")), "dragon pants with three actual drawings");
            await Frames(180);
            Check(frames.Contains(mina.GetNode<Sprite2D>("Body").Texture.ResourcePath), "flapping resumes after shield recovery");
            if (!preview)
            {
                var fx = BossTransformation.Play(mina, BossTransformation.Frame.Capture(mina.GetNode<Sprite2D>("Body")));
                await Frames(10);
                fx.QueueFree();
                await Frames(3);
                Check(mina.Visible && !_root.Hud.CinematicMode && _root.World.ProcessMode == ProcessModeEnum.Inherit,
                    "interrupted reveal restores visibility and processing");
            }
            }
            _root.QueueFree();
            await Frames(6);
            GetNode<BulletPool>("/root/Pool").DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            {
                audio.Stop();
                audio.Stream = null;
            }
            await Frames(4);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[BossMotionQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError("[BossMotionQA] FAIL " + e);
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task CheckAnimalForm(Enemy boss)
    {
        string id = boss.UnfolderStyle switch { UnfolderKind.Akari => "akari", UnfolderKind.Koharu => "koharu", _ => "rei" };
        var body = boss.GetNode<Sprite2D>("Body");
        var pool = GetNode<BulletPool>("/root/Pool");
        var caster = Read<AreaSpellCaster>(boss, "_caster");
        var preview = new List<BossTransformation.Frame> { BossTransformation.Frame.Capture(body) };
        foreach (string pose in new[] { "move_0", "move_1", "move_2", "move_3", "windup", "recover" })
        {
            using var pixels = GD.Load<Texture2D>(BossAnimalArt.Path(id, pose)).GetImage();
            var bounds = pixels.GetUsedRect();
            Check(bounds.Position.X > 0 && bounds.Position.Y > 0 && bounds.End.X < pixels.GetWidth() && bounds.End.Y < pixels.GetHeight(),
                $"{id}: {pose} keeps the entire silhouette inside its canvas");
        }
        var drawings = new HashSet<string>();
        for (int i = 0; i < 4; i++)
        {
            Call(boss, "TickBodyMotion", i == 0 ? 0d : 0.16d);
            drawings.Add(body.Texture.ResourcePath);
            preview.Add(BossTransformation.Frame.Capture(body));
            using var pixels = body.Texture.GetImage();
            Check(pixels.DetectAlpha() != Image.AlphaMode.None && pixels.GetPixel(0, 0).A == 0,
                $"{id}: motion frame has transparent margins");
            await Shot($"{id}_move_{i}");
        }
        Check(drawings.Count == 4 && drawings.All(p => p.Contains("_move_")), $"{id}: four distinct locomotion drawings");
        var normal = new BossMover();
        var fast = new BossMover();
        foreach (var m in new[] { normal, fast })
        {
            m.Configure(id, new Vector2(Field.BossCenterX, Field.BossZoneCenterY), Field.BossZoneHalfW, Field.BossZoneHalfH);
            m.SetNextAttack(BossMover.Attack.Wall);
        }
        Vector2 start = new(Field.BossCenterX, Field.BossZoneCenterY);
        Vector2 p1 = start, p2 = start;
        for (int i = 0; i < 30; i++)
        {
            p1 = normal.Step(p1, 1d / 60);
            p2 = fast.Step(p2, 1d / 60, 1.65f);
        }
        Check(p2.DistanceTo(start) > p1.DistanceTo(start) * 1.5f, $"{id}: second form travels faster on the same route");

        pool.DespawnAll();
        Call(boss, "FirePattern", 100d);
        Check(boss.AnimalTechniqueActive, $"{id}: the normal firing loop schedules the second form's exclusive technique");
        var technique = Read<BossAnimalTechnique>(boss, "_animalTechnique");
        technique.SetPhysicsProcess(false);
        Call(boss, "TickBodyMotion", 0.01d);
        Check(body.Texture.ResourcePath.Contains("_windup_"), $"{id}: technique uses its windup drawing");
        preview.Add(BossTransformation.Frame.Capture(body));
        technique._PhysicsProcess(0.5);
        Check(!pool.GetChildren().OfType<Bullet>().Any(b => b.Active && b.IsEnemy), $"{id}: warning precedes all projectiles");
        await Shot(id + "_technique_warning");
        double time = Read<double>(technique, "_time");
        _root.Hud.HoldBubble = true;
        _root.Hud.ShowDialog(Hud.LineKind.Mina, "Motion QA");
        technique._PhysicsProcess(3d);
        Check(Read<double>(technique, "_time") == time, $"{id}: dialogue freezes technique timing");
        _root.Hud.HoldBubble = false;
        _root.Hud.HideBubble();
        technique._PhysicsProcess(0.8);
        Check(pool.GetChildren().OfType<Bullet>().Any(b => b.Active && b.IsEnemy), $"{id}: exclusive volley fires after its warning");
        for (int i = 0; i < 2; i++) technique._PhysicsProcess(0.33);
        Check(Read<int>(technique, "_wave") == 3, $"{id}: all three stages of the technique fire");
        await Shot(id + "_technique_volley");
        Call(boss, "TickSwapAnim", 1d);
        Call(boss, "TickAttackPose", 0.4d);
        Check(body.Texture.ResourcePath.Contains("_recover_"), $"{id}: animal recovery never switches to human artwork");
        preview.Add(BossTransformation.Frame.Capture(body));
        await Shot(id + "_animal_recovery");
        caster.CancelPendingAttacks();
        Check(technique.Finished && !pool.GetChildren().OfType<Bullet>().Any(b => b.Active && b.IsEnemy),
            $"{id}: interruption clears the technique's projectiles");
        Call(boss, "TickAttackPose", 1d);
        Call(boss, "TickSwapAnim", 1d);
        await CompareAnimalMotion(id, preview);
    }

    private async Task CompareAnimalMotion(string id, List<BossTransformation.Frame> frames)
    {
        var viewport = new SubViewport { Size = new Vector2I(1680, 330), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Disable3D = true };
        AddChild(viewport);
        viewport.AddChild(new ColorRect { Size = new Vector2(1680, 330), Color = new Color("20232a") });
        string[] labels = { "待機", "移動 1", "移動 2", "移動 3", "移動 4", "溜め", "戻り" };
        for (int i = 0; i < frames.Count; i++)
        {
            var label = new Label { Text = labels[i], Position = new Vector2(i * 240 + 85, 20) };
            label.AddThemeFontOverride("font", UiKit.ZenBold);
            label.AddThemeFontSizeOverride("font_size", 20);
            viewport.AddChild(label);
            var sprite = frames[i].Create();
            sprite.Position = new Vector2(i * 240 + 100, 190);
            sprite.Scale = frames[i].Scale * 2.5f;
            viewport.AddChild(sprite);
        }
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = viewport.GetTexture().GetImage();
        Check(image.SavePng($"res://build/qa_story/boss_motion/{id}_motion_comparison.png") == Error.Ok, $"{id}: animation comparison saved");
        viewport.QueueFree();
    }

    private void Spawn(Enemy boss)
    {
        _root.World.AddChild(boss);
        boss.SetProcess(false);
        boss.SetPhysicsProcess(false);
        foreach (var child in boss.GetChildren())
            if (child is AreaSpellCaster or MinaPhaseAttacks) child.SetProcess(false);
        boss.GlobalPosition = new Vector2(250, 108);
        Call(boss, "TickEntrance", 0d);
        Call(boss, "TickEntrance", 100d);
        Call(boss, "TickSwapAnim", 1d);
        if (_transformPreview)
        {
            _previewName?.QueueFree();
            _previewName = new Label
            {
                Text = boss.UnfolderStyle switch { UnfolderKind.Akari => "あかり", UnfolderKind.Koharu => "こはる", UnfolderKind.Rei => "レイ", _ => "ミナ" },
                Position = new Vector2(Field.Left + 12, 12), ZIndex = 50,
            };
            _previewName.AddThemeFontOverride("font", UiKit.ZenBold);
            _previewName.AddThemeFontSizeOverride("font_size", 10);
            _previewName.AddThemeColorOverride("font_color", UnfolderMotion.ColorFor(boss.UnfolderStyle));
            _root.Hud.AddChild(_previewName);
        }
    }

    private async Task CheckReveal(Enemy boss, bool checkPause)
    {
        // World の停止は1フレーム遅れて効く：BossTransformation は ProcessMode の落としを遅延で
        //   出す（物理コールバックの最中に配下の Area2D を物理空間から外さないため。理由は
        //   src/fx/BossTransformation.cs の _Ready のコメント）。呼んだ直後はまだ Inherit なので、
        //   MessageQueue が流れる1フレームを待ってから invariant を見る。Transforming／Visible／
        //   BubblePaused は呼んだ瞬間から立っている＝待っても見えるものは変わらない。
        await Frames(1);
        Check(boss.Transforming && !boss.Visible && _root.World.ProcessMode == ProcessModeEnum.Disabled
            && _game.ProcessMode == ProcessModeEnum.Disabled && Hud.BubblePaused,
            "reveal suspends gameplay and hides duplicate body"
            + $" (active={boss.Transforming}, visible={boss.Visible}, world={_root.World.ProcessMode}"
            + $", game={_game.ProcessMode}, bubble={Hud.BubblePaused})");
        float hp = boss.HpRatio;
        boss.DealDirectDamage(9999);
        Check(boss.HpRatio == hp, "reveal rejects damage");
        await Frames(45);
        await Shot(boss.GetType().Name + "_gather");
        var fx = GetTree().GetFirstNodeInGroup("boss_transform");
        if (checkPause)
        {
            double time = Read<double>(fx, "_time");
            GetTree().Paused = true;
            await Frames(10);
            Check(Read<double>(fx, "_time") == time, "pause freezes transformation");
            GetTree().Paused = false;
        }
        await Frames(29);
        await Shot(boss.GetType().Name + "_charge");
        await Frames(12);
        await Shot(boss.GetType().Name + "_impact");
        await Frames(28);
        await Shot(boss.GetType().Name + "_reveal");
        await Frames(30);
        await Shot(boss.GetType().Name + "_settle");
        for (int i = 0; i < 200 && boss.Transforming; i++) await Frames(1);
        Check(!boss.Transforming && boss.Visible && !_root.Hud.CinematicMode && _root.World.ProcessMode == ProcessModeEnum.Inherit,
            $"reveal restores processing (active={boss.Transforming}, visible={boss.Visible}, cinematic={_root.Hud.CinematicMode}, world={_root.World.ProcessMode})");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task CompareAnimalSizes()
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(1440, 520), TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Disable3D = true,
        };
        AddChild(viewport);
        viewport.AddChild(new ColorRect { Size = new Vector2(1440, 520), Color = new Color("20232a") });
        float[] positions = { 160, 475, 790, 1140 };
        for (int i = 0; i < _animalComparison.Count; i++)
        {
            var (name, frame) = _animalComparison[i];
            var label = new Label { Text = name, Position = new Vector2(positions[i] - 90, 48) };
            label.AddThemeFontOverride("font", UiKit.ZenBold);
            label.AddThemeFontSizeOverride("font_size", 24);
            viewport.AddChild(label);
            var body = frame.Create();
            body.Position = new Vector2(positions[i], 300);
            body.Scale = frame.Scale * 2f;
            viewport.AddChild(body);
        }
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var pixels = viewport.GetTexture().GetImage();
        Check(pixels.SavePng("res://build/qa_story/boss_motion/animal_size_comparison.png") == Error.Ok, "same-scale animal comparison saved");
        viewport.QueueFree();
    }
    private async Task Shot(string name)
    {
        const string dir = "res://build/qa_story/boss_motion";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var pixels = GetViewport().GetTexture().GetImage();
        Check(pixels.SavePng(dir + "/" + name + ".png") == Error.Ok, "screenshot " + name);
    }
}
