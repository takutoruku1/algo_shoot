using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private async Task RunReiRallyPresentationQa(int phase)
    {
        _running = false;
        _pilot.Release();
        const double step = 1d / 60;
        int positionChecks = 0, visibilityChecks = 0, cleanupChecks = 0;
        Rect2? savedBounds = null;
        Node2D? savedLock = null;
        (Panel Panel, bool Visible, bool Invulnerable)[] savedPanels = Array.Empty<(Panel, bool, bool)>();

        ReiBossTechnique BeginFixture()
        {
            _pool.DespawnAll();
            PrepareSourceCutPanels();
            Write(_boss, "_hp", _boss.BarHp * _boss.TotalBars);
            _player.GlobalPosition = _easyStart = new Vector2(Field.Left + 8, Field.Bottom - 8);
            savedBounds = _player.RallyMovementBounds;
            savedLock = _player.RallyLockTarget;
            savedPanels = Read<List<Panel>>(_boss, "_panels")
                .Select(panel => (panel, panel.Visible, panel.Invulnerable)).ToArray();
            var attack = (ReiBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Normal, 4);
            _easyAttack = attack;
            attack.SetPhysicsProcess(false);
            attack.Tick(step);
            return attack;
        }

        Node Visuals(ReiBossTechnique attack) => Read<Node>(attack, "_rallyVisuals");
        object[] Balls(Node visuals) => Read<Array>(visuals, "_balls").Cast<object>().ToArray();
        object[] Tiles(Node visuals) => Read<Array>(visuals, "_tiles").Cast<object>().ToArray();
        void Sync(ReiBossTechnique attack)
        {
            Call(Visuals(attack), "Sync", attack.RallyBalls, attack.RallyWallX, attack.Elapsed,
                .2d, attack.RallyWallBroken, Read<double>(attack, "_rallyWallHitAt"));
            attack.QueueRedraw();
        }
        void CheckDamage(ReiBossTechnique attack, int expected)
        {
            EasyRequire(Tiles(Visuals(attack)).Count(tile => Read<bool>(tile, "Damaged")) == expected,
                $"The 3D wall shows a different streak: expected={expected}, actual={attack.RallyStreak}.");
        }
        async Task Capture(string name)
        {
            if (DisplayServer.GetName() == "headless") return;
            await Frames(3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var shot = GetViewport().GetTexture().GetImage();
            EasyRequire(shot.SavePng($"{_output}/rally_presentation_{phase}_{name}.png") == Error.Ok,
                "Could not save the rally presentation fixture.");
        }
        async Task CheckCleanup(ReiBossTechnique attack, Node visuals, SubViewport view)
        {
            EasyRequire(view.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled,
                "The finished or cancelled rally kept rendering its 3D viewport.");
            EasyRequire(!_boss.RallyGuarded && _player.RallyMovementBounds == savedBounds
                && _player.RallyLockTarget == savedLock,
                "Presentation cleanup left the rally guard, movement strip or lock target active.");
            EasyRequire(savedPanels.All(saved => IsInstanceValid(saved.Panel)
                && saved.Panel.Visible == saved.Visible && saved.Panel.Invulnerable == saved.Invulnerable),
                "Presentation cleanup did not restore the original Unfolders.");
            await EasyPhysicsFrames(2);
            EasyRequire(!IsInstanceValid(visuals) && !IsInstanceValid(view)
                && attack.RallyBalls.All(ball => !IsInstanceValid(ball.Node)),
                "The rally left its 3D viewport, model container or dedicated balls in the scene.");
            _easyAttack = null;
            cleanupChecks++;
        }

        var attack = BeginFixture();
        Node visuals = Visuals(attack);
        var view = visuals.GetNode<SubViewport>("ReiRallyView");
        EasyRequire(view.OwnWorld3D && view.TransparentBg
            && view.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled,
            "Rally rendering must use an active, transparent and isolated 3D viewport.");
        EasyRequire(Balls(visuals).Length == attack.RallyBalls.Count && Tiles(visuals).Length == 5,
            "The 3D presentation does not match the live ball count or five wall sections.");
        EasyRequire(attack.RallyBalls.All(ball => !Read<Node3D>(Balls(visuals)[ball.Index], "Root").Visible),
            "Gathering balls were shown before the play area formed.");

        foreach (var state in new[] {
            (Stage: ReiBossTechnique.RallyStage.Feint, Revealed: false),
            (Stage: ReiBossTechnique.RallyStage.Feint, Revealed: true),
            (Stage: ReiBossTechnique.RallyStage.Incoming, Revealed: true),
            (Stage: ReiBossTechnique.RallyStage.Returning, Revealed: true),
            (Stage: ReiBossTechnique.RallyStage.Closing, Revealed: true),
            (Stage: ReiBossTechnique.RallyStage.Done, Revealed: true) })
        {
            foreach (var ball in attack.RallyBalls)
            {
                ball.Stage = state.Stage;
                ball.StageAt = attack.Elapsed;
                ball.Revealed = state.Revealed;
                ball.Position = new Vector2(Field.Left + 50 + ball.Index * 21, 32 + ball.Index * 23);
                ball.Node.SetRallyPosition(ball.Position);
                ball.Node.Visible = state.Stage != ReiBossTechnique.RallyStage.Done;
            }
            Sync(attack);
            if (!state.Revealed)
            {
                Vector3 rotation = Read<Node3D>(Balls(visuals)[0], "Root").Rotation;
                EasyRequire(Balls(visuals).All(model => Read<Node3D>(model, "Root").Rotation.DistanceTo(rotation) < .001f),
                    "An unrevealed ball disclosed its kind through a different rotation.");
            }
            foreach (var ball in attack.RallyBalls)
            {
                object model = Balls(visuals)[ball.Index];
                var root = Read<Node3D>(model, "Root");
                EasyRequire(new Vector2(root.Position.X, -root.Position.Y).DistanceTo(ball.Position) < .001f,
                    "A 3D ball drifted away from the collision centre or inverted the wrong axis.");
                EasyRequire(root.Visible == ball.Node.Visible, "A 3D ball remained visible after its live node disappeared.");
                EasyRequire(Read<Node3D>(model, "Hidden").Visible == !ball.Revealed
                    && Read<Node3D>(model, "Good").Visible == (ball.Revealed && !ball.IsDanger)
                    && Read<Node3D>(model, "Danger").Visible == (ball.Revealed && ball.IsDanger),
                    "The 3D ball exposed its identity before reveal or showed the wrong kind.");
                positionChecks++;
                visibilityChecks++;
            }
            if (state.Stage == ReiBossTechnique.RallyStage.Incoming)
            {
                foreach (var ball in attack.RallyBalls) ball.Velocity = Vector2.Left * 30;
                attack.Tick(step);
                EasyRequire(attack.PracticeHazards.Any(hazard => hazard.Live && hazard.Shape == 0 && hazard.A != hazard.B),
                    "The presentation fixture did not exercise a moving ball's actual collision sweep.");
                EasyRequire(!Read<AttackLineLayer>(attack, "_attackLines").GetChildren().OfType<Line2D>().Any(line => line.Visible),
                    "A generic attack-line disc obscured the 3D rally ball.");
                await Capture("identification");
            }
        }
        attack.Cancel();
        await CheckCleanup(attack, visuals, view);

        attack = BeginFixture();
        visuals = Visuals(attack);
        view = visuals.GetNode<SubViewport>("ReiRallyView");
        var scoringBall = attack.RallyBalls.First(ball => !ball.IsDanger);
        void WallHit()
        {
            scoringBall.Stage = ReiBossTechnique.RallyStage.Returning;
            scoringBall.StageAt = attack.Elapsed;
            scoringBall.Position = new Vector2(attack.RallyWallX, Field.CenterY);
            scoringBall.Velocity = Vector2.Right * 210;
            Call(attack, "ScoreRally", scoringBall);
        }
        CheckDamage(attack, 0);
        WallHit();
        CheckDamage(attack, 1);
        WallHit();
        CheckDamage(attack, 2);
        Sync(attack);
        await Capture("two_hits");
        Call(attack, "MissRally", scoringBall);
        EasyRequire(attack.RallyStreak == 0 && attack.RallyFailures == 1, "The miss fixture did not reset the real streak.");
        CheckDamage(attack, 0);
        Sync(attack);
        await Capture("reset");
        for (int streak = 1; streak <= 5; streak++)
        {
            WallHit();
            CheckDamage(attack, streak);
        }
        EasyRequire(attack.RallyWallBroken && attack.RallyDamageEvents == 1,
            "Wall damage presentation did not reach the actual one-time fifth-hit reward.");
        attack.Tick(.25);
        await Capture("break");
        attack.Tick(.85);
        EasyRequire(attack.Finished && attack.Completed && attack.NaturalEndLiveHazards == 0,
            "The success presentation extended the interaction or retained a live hazard.");
        await CheckCleanup(attack, visuals, view);

        foreach (var stage in new[] { ReiBossTechnique.RallyStage.Gather,
            ReiBossTechnique.RallyStage.Incoming, ReiBossTechnique.RallyStage.Returning })
        {
            attack = BeginFixture();
            visuals = Visuals(attack);
            view = visuals.GetNode<SubViewport>("ReiRallyView");
            foreach (var ball in attack.RallyBalls)
            {
                ball.Stage = stage;
                ball.StageAt = attack.Elapsed;
                ball.Revealed = true;
                ball.Node.Visible = stage != ReiBossTechnique.RallyStage.Gather;
            }
            Sync(attack);
            attack.Cancel();
            await CheckCleanup(attack, visuals, view);
        }
        var result = new { character = _scene, difficulty = _game.Difficulty.ToString(), phase,
            audit = "rally_3d_presentation", passed = true, fixture_positioning = true,
            normal_input_test = false, human_comprehension_test = false,
            position_checks = positionChecks, visibility_checks = visibilityChecks,
            unrevealed_rotation_checked = true, generic_line_overlap_checked = true,
            damage_reset_checked = true, fifth_hit_checked = true, cleanup_checks = cleanupChecks };
        _easyCases.Add(result);
        GD.Print("[REI-RALLY-PRESENTATION] " + JsonSerializer.Serialize(result));
    }
}
