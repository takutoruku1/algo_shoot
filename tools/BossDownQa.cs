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
    private bool _shieldTiming, _shieldVideo;
    private int _shieldTicks;
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
            _shieldTiming = OS.GetCmdlineUserArgs().Contains("--shield-timing");
            _shieldVideo = OS.GetCmdlineUserArgs().Contains("--shield-video");
            if (_shieldTiming)
                typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            _shots = _breakShots || OS.GetCmdlineUserArgs().Contains("--down-shots");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            game.MarkIdleDialogSeen("once_midboss_shield");
            game.Difficulty = _shieldVideo ? GameManager.Diff.Easy : GameManager.Diff.Normal;
            if (_shieldTiming) game.SelectedJob = Job.Heal;
            if (_shots || _shieldVideo)
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

            if (_shieldTiming) await CheckShieldTiming(game);
            foreach (var id in _shieldTiming ? Array.Empty<string>()
                : new[] { "akari", "koharu", "rei", "mina", "akari_mid", "koharu_mid", "rei_mid" })
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
                        Check(costume == "home" ? mina.GaugeReforming && !Read<bool>(mina, "_bodyDown")
                            : Body(mina).Texture.ResourcePath == BossDownArt.BreathingPath(BossMina.BattleDownPath(mina.EncounterPhase), 0),
                            costume + " changes costume with the correct shield state");
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
            if (!_breakShots && !_shieldTiming)
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

    private static void Require(bool pass, string label)
    {
        if (!pass) throw new Exception(label);
    }

    private static bool Striking(BossCharacterTechnique attack)
        => attack.IsStriking || attack.OwnedShots.Any(b => !b.LeadIn && b.CollisionLayer != 0);

    private static HashSet<(ulong Instance, ulong Activation)> ShotIds(BossCharacterTechnique attack)
        => attack.OwnedShots.Select(b => (b.GetInstanceId(), b.ActivationId)).ToHashSet();

    private async Task ShieldFrame(Enemy boss)
    {
        const double dt = 1d / 60;
        boss._PhysicsProcess(dt);
        foreach (var child in boss.GetChildren())
        {
            if (child is AreaSpellCaster caster) caster._Process(dt);
            if (child is MinaPhaseAttacks mina) mina._Process(dt);
        }
        foreach (var attack in boss.GetChildren().OfType<BossCharacterTechnique>().Where(a => !a.Finished).ToArray())
            attack._PhysicsProcess(dt);
        foreach (var bullet in Pool.GetChildren().OfType<Bullet>().Where(b => b.Active).ToArray())
            bullet._PhysicsProcess(dt);
        foreach (var panel in Read<List<Panel>>(boss, "_panels")) panel._PhysicsProcess(dt);
        var gauge = boss.GetNode<BossGauge>("BossGauge");
        gauge._Process(dt);
        _shieldTicks++;
        if (_shieldVideo)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var (label, fill) = boss.GaugeAttackResolving ? ("攻撃終了待ち", "resolving")
                : boss.GaugeBreaking ? ("シールド破壊", "breaking")
                : boss.GaugeBodyExposed ? ("本体に攻撃", "break")
                : boss.GaugeReforming ? ("再形成中", "invuln") : ("シールド", "invuln");
            Require(gauge.LastStateLabel == label && gauge.LastFillKind == fill,
                $"Rendered shield gauge differs from combat phase: expected {label}/{fill}, got {gauge.LastStateLabel}/{gauge.LastFillKind}");
        }
        else if (_shieldTicks % 120 == 0) await Frames(1);
    }

    private async Task CheckShieldTiming(GameManager game)
    {
        var originalPoolMode = Pool.ProcessMode;
        Pool.ProcessMode = ProcessModeEnum.Disabled;
        GD.Print("[BossDownQA] Shield timing: stationary invincible player, no shooting, real Panel.Shatter input; "
            + "production methods advance at 1/60; contact damage and human difficulty are not evaluated.");
        bool secondOnly = OS.GetCmdlineUserArgs().Contains("--shield-second");
        var difficulties = _shieldVideo ? new[] { GameManager.Diff.Easy } : Enum.GetValues<GameManager.Diff>();
        var ids = _shieldVideo ? new[] { "akari", "akari_mid" }
            : secondOnly ? new[] { "akari_mid", "koharu_mid", "rei_mid" }
            : new[] { "akari", "koharu", "rei", "mina", "akari_mid", "koharu_mid", "rei_mid" };
        foreach (var difficulty in difficulties)
        foreach (string id in ids)
        foreach (bool evolved in !_shieldVideo && difficulty <= GameManager.Diff.Normal && !id.EndsWith("_mid")
            ? new[] { false, true } : new[] { false })
        foreach (int variant in secondOnly ? new[] { 1 } : !_shieldVideo && id.EndsWith("_mid") ? new[] { 0, 1 } : new[] { 0 })
        foreach (int trigger in _shieldVideo || evolved || variant > 0 ? new[] { 2 } : new[] { 0, 1, 2 })
        {
            game.Difficulty = difficulty;
            ClearDialog();
            _root.Hud.HideSpellCard();
            typeof(GameManager).GetProperty(nameof(GameManager.Bombs))!.SetValue(game, 0);
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
            if (evolved)
            {
                // Use the production form change while omitting the already-tested story interlude.
                game.Difficulty = GameManager.Diff.Lunatic;
                if (boss is BossMina)
                {
                    Call(boss, "BeginPhaseTransition");
                    Call(boss, "CompletePhaseTransition");
                }
                else Require((bool)Call(boss, "AdvanceForm2")!, id + ": second form setup failed");
                game.Difficulty = difficulty;
                Call(boss, "TickSwapAnim", 1d);
                _root.Player.GlobalPosition = new Vector2(Field.Left + 30, 165);
                Require(boss.HasEvolved, id + ": evolved shot and technique branch is inactive");
            }
            ClearDialog();
            await Frames(2);
            if (variant > 0)
            {
                var danmaku = Read<BossDanmaku>(boss, "_danmaku");
                if (boss.UsesTechniqueCycle)
                {
                    Write(danmaku, "_cycleSegment", BossAttackCycle.Segment(boss));
                    Write(danmaku, "_cycleSlot", variant);
                }
                else Write(danmaku, "_phrase", variant);
            }
            string label = $"{difficulty}/{id}/{(evolved ? "after/" : variant > 0 ? "second/" : "")}{(trigger == 0 ? "idle" : trigger == 1 ? "warning" : "live")}";
            var cue = _root.Hud.Bubbles!.ShieldBreak;
            BossCharacterTechnique? attack = null;
            bool ended = false, completed = false;
            double endedAt = -1;
            int finalShots = -1, endShots = -1, endHazards = -1;
            if (trigger != 0)
            {
                for (int frame = 0; frame < 600 && attack == null; frame++)
                {
                    await ShieldFrame(boss);
                    attack = boss.GetChildren().OfType<BossCharacterTechnique>().FirstOrDefault(a => !a.Finished);
                }
                Require(attack != null, label + ": ordinary combat did not select a technique");
                if (variant > 0)
                    Check(attack!.TechniqueKind == BossTechniqueKind.Cameo && attack.TechniqueVariant == variant,
                        label + ": ordinary combat selects the second midboss technique");
                attack!.Ended += endedAttack =>
                {
                    ended = true;
                    completed = endedAttack.Completed;
                    endedAt = endedAttack.Elapsed;
                    finalShots = endedAttack.ShotsCreated;
                    endShots = endedAttack.NaturalEndRemainingShots;
                    endHazards = endedAttack.NaturalEndLiveHazards;
                };
                for (int frame = 0; frame < 7200 && !ended; frame++)
                {
                    if (trigger == 1 ? attack.Elapsed >= .25 && !Striking(attack) : Striking(attack)) break;
                    await ShieldFrame(boss);
                }
                Require(!ended && (trigger == 1 ? attack.Elapsed >= .25 && !Striking(attack) : Striking(attack)),
                    label + ": missing shield trigger window");
            }
            int hp = Read<int>(boss, "_hp");
            int initialShots = attack?.ShotsCreated ?? 0;
            double triggerAt = attack?.Elapsed ?? 0;
            var beforeShots = attack == null ? new HashSet<(ulong, ulong)>() : ShotIds(attack);
            var panels = Read<List<Panel>>(boss, "_panels").ToArray();
            Require(panels.Length > 0, label + ": no real shield panels");
            foreach (var panel in panels.Take(panels.Length - 1)) panel.Shatter();
            Require(!boss.GaugeAttackResolving && !boss.GaugeVulnerable && game.Bombs == 0 && !cue.Active,
                label + ": shield broke before the final panel");
            panels[^1].Shatter();
            Check(Read<List<Panel>>(boss, "_panels").Count == 0, label + ": actual final panel reaches the shield state machine");
            if (attack != null)
            {
                Check(boss.GaugeAttackResolving && !boss.GaugeVulnerable && !ended && game.Bombs == 0
                    && !cue.Active && !Read<bool>(boss, "_bodyDown") && !boss.GetCollisionMaskValue(2),
                    label + ": final panel defers break pose, callout, reward and body vulnerability");
                Check(beforeShots.SetEquals(ShotIds(attack)), label + ": shield exhaustion preserves existing projectiles");
                int attackCount = boss.GetChildren().OfType<BossCharacterTechnique>().Count(a => !a.Finished);
                for (int frame = 0; frame < 10800 && !ended; frame++)
                {
                    Require(boss.GaugeAttackResolving && !boss.GaugeVulnerable && game.Bombs == 0 && !cue.Active
                        && !Read<bool>(boss, "_bodyDown") && !boss.GetCollisionMaskValue(2),
                        label + ": break side effects appeared before natural completion");
                    Require(boss.GetChildren().OfType<BossCharacterTechnique>().Count(a => !a.Finished) == attackCount,
                        label + ": another technique started while shield break was pending");
                    await ShieldFrame(boss);
                }
                Check(ended && completed && endedAt > triggerAt,
                    label + ": the current technique completes naturally after shield exhaustion");
                Check(endShots == 0 && endHazards == 0,
                    label + ": natural completion leaves no active projectiles or hazards");
                Check(finalShots >= initialShots && (trigger != 1 || finalShots > 0 || attack.HazardFrames > 0),
                    label + ": scheduled attacks continue after the final panel");
                GD.Print($"[BossDownQA] TIMING {label} trigger={triggerAt:F3} complete={endedAt:F3} shots={initialShots}->{finalShots}");
            }
            for (int frame = 0; frame < 3 && !boss.GaugeBreaking; frame++) await ShieldFrame(boss);
            Check(boss.GaugeBreaking && Read<bool>(boss, "_bodyDown") && cue.Active && game.Bombs == 1,
                label + ": break animation, callout and reward begin together");
            Check(Read<int>(boss, "_hp") == hp && !boss.GetCollisionMaskValue(2),
                label + ": protected body remains undamaged during the break cue");
            for (int frame = 0; frame < 90 && !boss.GaugeBodyExposed; frame++) await ShieldFrame(boss);
            Check(boss.GaugeBodyExposed && boss.GetCollisionMaskValue(2) && game.Bombs == 1,
                label + ": exposure follows the break cue and reward occurs only once");
            Check(!boss.GetChildren().OfType<BossCharacterTechnique>().Any(a => !a.Finished),
                label + ": the following technique does not begin during the damage window");
            if (_shieldVideo)
                for (int frame = 0; frame < 90; frame++) await ShieldFrame(boss);
            boss.QueueFree();
            Pool.DespawnAll();
            ClearDialog();
            await Frames(3);
        }
        Pool.ProcessMode = originalPoolMode;
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
                await CheckPostCallout(boss, job.Id);
            }
            Call(boss, "EnterReclose");
            Check(!cue.Active, "the chance disappears immediately when the damage window closes early");
            if (boss is BossMina && Read<CharacterStoryTalk?>(boss, "_recloseTalk") is { Active: true } talk)
                Call(talk, "Finish", _root.Hud);
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

    private async Task CheckPostCallout(Enemy boss, Job job)
    {
        var cue = _root.Hud.Bubbles!.ShieldBreak;
        string combatLine = Read<string>(cue, "_line");
        foreach (int index in new[] { 0, 1, 4 })
        {
            var post = new BossPost
            {
                Story = BossPostStory.Get("akari"), Boss = boss, Index = index,
                Position = new Vector2(Field.Right - 84, 104),
                Broken = (_, _) => { }, Completed = () => { },
            };
            _root.World.AddChild(post);
            post.SetPhysicsProcess(false);
            post._PhysicsProcess(0.5);
            await Frames(3);
            string line = Read<string>(cue, "_line");
            Check(cue.Active && line == combatLine && line.Contains("シールド") && line.Contains("本体"),
                $"{job}/{index}: damage-linked post preserves the body-attack guidance");
            post.Shatter();
            post._PhysicsProcess(0.5);
            await Frames(3);
            Check(cue.Active && Read<string>(cue, "_line") == combatLine, "shattering keeps combat guidance");
            if (index == 1) await SaveBreakShot($"draft_{Jobs.Get(job).CharacterId}");
            post.QueueFree();
            await Frames(3);
            Check(Read<string>(cue, "_line") == combatLine, "combat guidance remains after the post closes");
        }
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
        Call(boss, "TickBossPhase", 7.9d);
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
        Check(hostile.Active && playerShot.Active, id + " break preserves hostile and player shots");
        boss.ProcessMode = ProcessModeEnum.Inherit;
        boss.SetProcess(false);
        boss.SetPhysicsProcess(true);
        foreach (var child in boss.GetChildren())
            if (child is AreaSpellCaster or MinaPhaseAttacks) child.SetProcess(true);
        await Frames(28);
        Check(IsInstanceValid(strike) && !strike.IsQueuedForDeletion(), id + " break preserves existing AOE");
        var frames = new HashSet<string>();
        var origin = boss.GlobalPosition;
        for (int i = 0; i < 100; i++)
        {
            await Frames(1);
            var body = Body(boss);
            frames.Add(body.Texture.ResourcePath);
            if (!body.Position.IsZeroApprox() || !Mathf.IsZeroApprox(body.Rotation) || boss.GlobalPosition != origin)
                throw new Exception(id + " moves the whole sprite instead of breathing through shoulders");
        }
        Check(frames.Count == 3 && frames.All(p => p.Contains("_breath_")), id + " three shoulder-breathing drawings animate without whole-body motion");
        Check(GetTree().GetNodesInGroup("enemy_bullets").OfType<Bullet>().Any(b => b.Active), id + " danmaku continues during down");
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
