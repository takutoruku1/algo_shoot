using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private async Task RunFrameCameoQa()
    {
        _boss.QueueFree();
        await EasyPhysicsFrames(4);
        var stage = (Node)_easyRoot!.GetType().GetProperty("Stage")!.GetValue(_easyRoot)!;
        Write(stage, "_stepStarted", false);
        Write(stage, "_lunatic", true);
        Call(stage, "Step_BossCameo", 0d);
        _boss = Read<CameoBoss>(stage, "_cameo");
        _boss.SetPhysicsProcess(false);
        _boss.SetProcess(false);
        _boss.GlobalPosition = new Vector2(Field.BossFrameCenterX, Field.BossZoneCenterY);
        Call(_boss, "TickEntrance", 0d);
        Call(_boss, "TickEntrance", 2d);
        EasyRequire(!Read<bool>(_boss, "_entering"), "Cameo entrance fixture did not finish.");
        _hud.HideBubble();
        Hud.BubblePaused = false;
        for (int frame = 0; frame < 600 && _player.ReturningToStart; frame++)
        {
            _player._PhysicsProcess(1d / 60);
            await EasyPhysicsFrames(1);
        }
        EasyRequire(!_player.ReturningToStart, "Cameo player entrance did not finish.");
        await EasyPhysicsFrames(4);
        if (OS.GetCmdlineUserArgs().Contains("--model-audit"))
            foreach (var slot in BossAttackCycle.Slots(_boss, 0)) await AuditAttackModels(0, slot);
        else if (_pressureAudit)
        {
            _boss.SetPanelsInvulnerable(true);
            var startIndices = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--starts="));
            var starts = startIndices == null ? Enumerable.Range(0, EasyStarts.Length)
                : startIndices[9..].Split(',').Select(int.Parse);
            foreach (var slot in BossAttackCycle.Slots(_boss, 0))
                foreach (int start in starts)
                    await RunEasyClip(0, slot, start, !OS.GetCmdlineUserArgs().Contains("--cue-pilot"), true);
        }
        else if (_easyCycle) await EasyBattleCycle(0);
        else await RunFrameRevisionQa(0);
    }

    private async Task RunFrameTargetQa(int phase)
    {
        _boss.SetPanelsInvulnerable(false);
        await EasyPhysicsFrames(90);
        var panels = _boss.GetChildren().OfType<Panel>().ToArray();
        foreach (var panel in panels) panel.SetPhysicsProcess(false);
        EasyRequire(panels.All(panel => panel.CanLock && panel.IsVisibleInTree() && panel.Modulate.A == 1
            && !Read<CollisionShape2D>(panel, "_shape").Disabled), "A surviving shield disappeared or lost its collider.");
        var visible = panels.OrderBy(panel => panel.GlobalPosition.X).First();
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screenshot = GetViewport().GetTexture().GetImage();
            EasyRequire(screenshot.SavePng($"{_output}/shields_{phase}.png") == Error.Ok, "Shield capture failed.");
        }
        int ink = visible.Ink;
        visible.Ink = Math.Max(3, ink);
        int before = visible.Ink;
        _player.GlobalPosition = new Vector2(Field.Right - 48, visible.GlobalPosition.Y);
        PresenceLockTarget(expectUnfolder: true);
        var origin = new Vector2(Field.Right - 20, visible.GlobalPosition.Y);
        var shot = _pool.Spawn(origin, origin.DirectionTo(visible.GlobalPosition) * 100, false, damage: 1);
        for (int frame = 0; frame < 120 && shot.Active; frame++) await EasyPhysicsFrames(1);
        EasyRequire(!shot.Active && visible.Ink < before, "A visible orbiting shield did not receive a real player shot.");
        int damage = before - visible.Ink;
        visible.Ink = ink;
        foreach (var panel in panels) panel.SetPhysicsProcess(true);
        _easyCases.Add(new { audit = "frame_targets", boss = _scene, phase, real_lock_input = true, real_shot_damage = damage,
            visible_targets = panels.Length, passed = true });
        GD.Print($"[FRAME] TARGET_PASS boss={_scene} phase={phase} lock=true shot_damage=true visible_targets={panels.Length}");
    }

    private async Task RunFrameRevisionQa(int phase)
    {
        bool extra = OS.GetCmdlineUserArgs().Contains("--frame-extra");
        if (extra && (_boss is BossMina ? phase != 4 : phase != 1)) return;
        var panels = _boss.GetChildren().OfType<Panel>().ToArray();
        _boss.SetPanelsInvulnerable(false);
        var prior = panels.Select(panel => panel.GlobalPosition).ToArray();
        var priorLocal = panels.Select(panel => panel.Position).ToArray();
        var previousFlash = panels.Select(panel => Read<UnfolderPose>(panel, "_pose").WarpFlash).ToArray();
        bool baseline = OS.GetCmdlineUserArgs().Contains("--frame-baseline");
        bool orbitOnly = OS.GetCmdlineUserArgs().Contains("--unfolder-orbit-qa");
        bool native = DisplayServer.GetName() != "headless";
        var angles = panels.Select(panel => panel.Position.Angle()).ToArray();
        var turns = new float[panels.Length];
        var sides = new int[panels.Length];
        var ink = panels.Select(panel => panel.Ink).ToArray();
        if (orbitOnly)
        {
            PresenceLockTarget(expectUnfolder: true);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = true });
            Input.FlushBufferedEvents();
            _player._PhysicsProcess(1d / 60);
        }
        var target = _player.LockedUnfolder;
        float largest = 0, largestLocal = 0;
        int minimumTargets = panels.Length, warps = 0;
        GD.Print($"[FRAME] GEOMETRY boss={_scene} phase={phase} face={BossIllustrationMotion.FaceClearance(_boss)} gauge_top={_boss.GaugeTop} panels={panels.Length} positions={string.Join(';', prior)}");
        for (int frame = 0; frame < (native && !orbitOnly ? 180 : 1800); frame++)
        {
            if (!baseline)
            {
                Call(_boss, "UpdateMovement", 1d / 60);
                Call(_boss, "TickBodyMotion", 1d / 60);
                Call(_boss, "TickIllustrationMotion", 1d / 60);
            }
            await EasyPhysicsFrames(1);
            for (int i = 0; i < panels.Length; i++)
            {
                float step = prior[i].DistanceTo(panels[i].GlobalPosition);
                var pose = Read<UnfolderPose>(panels[i], "_pose");
                if (pose.WarpFlash > previousFlash[i]) warps++;
                else
                {
                    largest = Math.Max(largest, step);
                    largestLocal = Math.Max(largestLocal, priorLocal[i].DistanceTo(panels[i].Position));
                }
                previousFlash[i] = pose.WarpFlash;
                prior[i] = panels[i].GlobalPosition;
                priorLocal[i] = panels[i].Position;
                float angle = panels[i].Position.Angle();
                turns[i] += Mathf.AngleDifference(angles[i], angle);
                angles[i] = angle;
                sides[i] |= 1 << ((panels[i].Position.X >= 0 ? 1 : 0) + (panels[i].Position.Y >= 0 ? 2 : 0));
            }
            int targets = panels.Count(panel => panel.CanLock && panel.IsVisibleInTree() && panel.Modulate.A == 1
                && Field.BossFrameRect.Encloses(panel.CoreBounds.Grow(-.02f))
                && !Read<CollisionShape2D>(panel, "_shape").Disabled);
            minimumTargets = Math.Min(minimumTargets, targets);
            if (!baseline && !orbitOnly) PresenceFaceClearance($"continuous phase={phase} frame={frame}");
            if (orbitOnly)
            {
                var gauge = new Rect2(_boss.GlobalPosition + new Vector2(-_boss.GaugeWidth * .5f - 3, _boss.GaugeTop - 9),
                    new Vector2(_boss.GaugeWidth + 6, 15));
                EasyRequire(panels.All(panel => !panel.CoreBounds.Grow(-.02f).Intersects(gauge)),
                    "An orbiting shield is hidden by the boss HP gauge.");
                EasyRequire(_player.LockedUnfolder == target && _player.LockAimPosition == target!.GlobalPosition,
                    "Orbiting changed the selected shield or detached the aim point.");
                if (native && frame % 120 == 0)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage();
                    EasyRequire(image.SavePng($"{_output}/orbit_{phase}_{frame:D4}.png") == Error.Ok, "Orbit capture failed.");
                }
            }
        }
        GD.Print($"[FRAME] ORBIT boss={_scene} phase={phase} max_step={largest} minimum_targets={minimumTargets}/{panels.Length} intentional_warps={warps}");
        if (baseline) return;
        EasyRequire((orbitOnly ? largestLocal : largest) < 3, "An unfolder teleported without its warp cue.");
        EasyRequire(panels.Length > 0 && minimumTargets == panels.Length, "A surviving unfolder disappeared or became untargetable.");
        if (orbitOnly)
        {
            EasyRequire(sides.All(side => side == 15) && turns.All(turn => Mathf.Abs(turn) >= Mathf.Tau),
                $"Shields did not complete an orbit around their boss: turns={string.Join(',', turns.Select(turn => turn / Mathf.Tau))}.");
            EasyRequire(ink.SequenceEqual(panels.Select(panel => panel.Ink)), "Orbiting changed shield durability.");
            var paused = panels.Select(panel => panel.Position).ToArray();
            _hud.SetCinematicMode(true, dialogueBubble: true);
            await EasyPhysicsFrames(30);
            EasyRequire(paused.SequenceEqual(panels.Select(panel => panel.Position)), "Dialogue did not pause the orbit.");
            _hud.SetCinematicMode(false);
            var survivor = panels.First(panel => panel != target);
            var beforeBreak = survivor.Position;
            float brokenAngle = Read<float>(target!, "_baseAngle");
            target!.Shatter();
            await EasyPhysicsFrames(1);
            EasyRequire(_player.LockedUnfolder != target && _player.LockedUnfolder?.GetParent() == _boss
                && survivor.Position.DistanceTo(beforeBreak) < 3, "Breaking a shield displaced the survivors or lost lock-on.");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = false });
            Input.FlushBufferedEvents();
            _player._PhysicsProcess(1d / 60);
            Call(_boss, "SpawnOnePanel", brokenAngle, Mathf.RoundToInt(brokenAngle * panels.Length / Mathf.Tau));
            Write(_boss.GetChildren().OfType<Panel>().Last(), "_motionTime", Read<float>(survivor, "_motionTime"));
            _easyCases.Add(new { audit = "unfolder_orbit", boss = _scene, phase, minimum_targets = minimumTargets,
                maximum_step = largest, maximum_local_step = largestLocal, revolutions = turns.Select(turn => turn / Mathf.Tau).ToArray(),
                paused = true, lock_handoff = true, passed = true });
            GD.Print($"[ORBIT] PASS boss={_scene} phase={phase} turns={string.Join(',', turns.Select(turn => turn / Mathf.Tau))} lock=true pause=true handoff=true");
            return;
        }
        async Task Capture(string label)
        {
            if (!native) return;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            EasyRequire(image.SavePng($"{_output}/{label}_{phase}.png") == Error.Ok, "Capture failed.");
        }
        await Capture("frame");
        if (_boss is CameoBoss)
        {
            await EasyBattleCycle(phase);
            EasyRequire(!_boss.BossInArena && !_boss.BossFrameReturning, "Midboss techniques stay inside the portrait frame.");
            return;
        }
        var slots = BossAttackCycle.Slots(_boss, phase);
        var slot = slots.First(candidate => extra ? _boss switch {
            BossAkari or BossRei => candidate.Kind == BossTechniqueKind.Signature,
            BossKoharu => candidate.Kind == BossTechniqueKind.Extra && candidate.Variant == 0,
            _ => candidate.Kind == BossTechniqueKind.Extra && candidate.Variant == 1,
        } : _boss is BossMina ? candidate.Kind == BossTechniqueKind.Area
            : candidate.Kind == BossTechniqueKind.Normal && candidate.Variant == 1);
        _easyAttack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
        _easyAttack.SetPhysicsProcess(false);
        Vector2 before = _boss.GlobalPosition;
        EasyRequire(_easyAttack.UsesArenaBody && _boss.BossInArena && _boss.BossFrameEntering, "Production slot does not leave its frame.");
        float entryStep = 0;
        bool fired = false, captured = false;
        for (int frame = 0; frame < 12000 && !_easyAttack.Finished; frame++)
        {
            bool entering = _boss.BossFrameEntering;
            if (!entering) Call(_boss, "UpdateMovement", 1d / 60);
            Call(_boss, "TickAttackPose", 1d / 60);
            Call(_boss, "TickBodyMotion", 1d / 60);
            Call(_boss, "TickIllustrationMotion", 1d / 60);
            _easyAttack.Tick(1d / 60);
            if (entering)
            {
                entryStep = Math.Max(entryStep, before.DistanceTo(_boss.GlobalPosition));
                EasyRequire(_easyAttack.ShotsCreated == 0 && !_easyAttack.IsStriking, "An attack hit during entry before its cue.");
            }
            before = _boss.GlobalPosition;
            fired |= _easyAttack.IsStriking || _easyAttack.OwnedShots.Any();
            await EasyPhysicsFrames(1);
            if (frame == 25) await Capture("entry");
            if (!captured && fired && _easyAttack.Elapsed > 6)
            { await Capture("attack"); captured = true; }
        }
        EasyRequire(entryStep < 5 && _boss.GlobalPosition.X < Field.BossFrameLeft,
            $"Entry did not travel continuously into the arena: step={entryStep} position={_boss.GlobalPosition}.");
        EasyRequire(_easyAttack.Completed && fired && _easyAttack.NaturalEndRemainingShots == 0, "Arena attack failed to finish cleanly.");
        float returnStep = 0;
        before = _boss.GlobalPosition;
        for (int frame = 0; frame < 100 && _boss.BossFrameReturning; frame++)
        {
            _boss.TickBossFrame(1d / 60);
            returnStep = Math.Max(returnStep, before.DistanceTo(_boss.GlobalPosition));
            before = _boss.GlobalPosition;
            await EasyPhysicsFrames(1);
        }
        EasyRequire(returnStep < 7 && !_boss.BossFrameReturning && !_boss.BossInArena
            && Mathf.IsEqualApprox(_boss.GlobalPosition.X, Field.BossFrameCenterX), "Return jumped or did not restore the frame.");
        await Capture("returned");
        var retry = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
        retry.SetPhysicsProcess(false);
        for (int frame = 0; frame < 18; frame++) { retry.Tick(1d / 60); await EasyPhysicsFrames(1); }
        retry.Cancel();
        await SettleEasyFrame();
        EasyRequire(!_boss.BossFrameEntering && !_boss.BossFrameReturning, "Entry cancellation left a transition running.");
        await CheckTechniqueInterruption(phase, slot);
        _easyCases.Add(new { audit = "frame_revision", boss = _scene, phase, difficulty = _game.Difficulty.ToString(),
            maximum_orbit_step = largest, minimum_targets = minimumTargets, intentional_warps = warps,
            entry_step = entryStep, return_step = returnStep, natural_completion = true, production_cycle_retry = true,
            invincible = true, human_difficulty_validation = false });
        GD.Print($"[FRAME] PASS boss={_scene} phase={phase} entry_step={entryStep} return_step={returnStep} natural_end=true interrupted_entry=true production_retry=true");
    }
}
