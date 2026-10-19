using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private readonly List<object> _easyTransitions = new();
    private ulong _easyTransitionLastBossFrame = ulong.MaxValue;
    private int _easyTransitionBossCalls, _easyTransitionCasterCalls;
    private const double EasyTransitionDelta = 1d / 60;

    private async Task RunEasyTransitionQa(int phase)
    {
        EasyRequire(_game.Difficulty == GameManager.Diff.Easy && _boss.UsesEasyTechniqueSlots,
            "Transition QA requires the production EASY slot branch.");
        foreach (bool live in new[] { false, true }) await EasyShieldTransition(phase, live);
        if (phase == 1 && _boss is (BossRei or BossKoharu)) await EasyThresholdFinale(phase);
        GD.Print($"[EASY] TRANSITIONS phase={phase} PASS records={_easyTransitions.Count}");
    }

    private void PrepareEasyTransition(int phase)
    {
        int actualPhase = _boss is BossMina mina ? mina.AttackPhase : _boss.HasEvolved ? 1 : 0;
        EasyRequire(actualPhase == phase, "Transition QA phase setup differs from the production selection segment.");
        _running = false;
        _pilot.Release();
        EasyRequire(!GetTree().GetNodesInGroup("boss_character_techniques")
            .OfType<BossCharacterTechnique>().Any(a => !a.Finished), "Previous transition left an active technique.");
        EasyRequire(!_pool.GetChildren().OfType<Bullet>().Any(b => b.Active && b.IsEnemy),
            "Previous transition left enemy bullets.");
        _easyStationary = true;
        _easyStart = EasyStarts[0];
        _easyDisplacement = 0;
        _player.GlobalPosition = _easyStart;
        _hud.HideBubble();
        _hud.HideSpellCard();
        Hud.BubblePaused = false;
        _boss.SetPhysicsProcess(false);
        _boss.SetProcess(false);
        Call(_boss, "EnterShielded");
        _boss.SetPanelsInvulnerable(true);
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        Write(danmaku, "_easySegment", -1);
        Write(danmaku, "_easySlot", 0);
        Write(danmaku, "_easyRounds", 0);
        if (_boss is not BossMina)
        {
            var caster = Read<AreaSpellCaster>(_boss, "_caster");
            caster.CancelPendingAttacks();
            caster.Suppressed = false;
            caster.SetProcess(false);
        }
        _easyTransitionLastBossFrame = ulong.MaxValue;
        _easyTransitionBossCalls = _easyTransitionCasterCalls = 0;
        _running = true;
    }

    private async Task EasyTransitionFrame()
    {
        ulong frame = Engine.GetPhysicsFrames();
        EasyRequire(frame != _easyTransitionLastBossFrame, "Boss physics was manually advanced twice in one physics frame.");
        _easyTransitionLastBossFrame = frame;
        // EASY pilot ticks only Player; the disabled boss and caster each receive one production update here.
        _boss._PhysicsProcess(EasyTransitionDelta);
        _easyTransitionBossCalls++;
        if (_boss is not BossMina)
        {
            Read<AreaSpellCaster>(_boss, "_caster")._Process(EasyTransitionDelta);
            _easyTransitionCasterCalls++;
        }
        await EasyPhysicsFrames(1);
    }

    private static (Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)[] EasyTransitionHazards(BossCharacterTechnique attack)
        => Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(attack, "_hazards")
            .Where(h => h.Live).ToArray();

    private static HashSet<(ulong Instance, ulong Activation)> EasyTransitionShots(BossCharacterTechnique attack)
        => attack.OwnedShots.Select(b => (b.GetInstanceId(), b.ActivationId)).ToHashSet();

    private async Task EasyShieldTransition(int phase, bool triggerLive)
    {
        PrepareEasyTransition(phase);
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        BossCharacterTechnique? attack = null;
        for (int frame = 0; frame < 600 && attack == null; frame++)
        {
            await EasyTransitionFrame();
            if (danmaku.ActiveTechnique is { Finished: false } active) attack = active;
        }
        EasyRequire(attack != null, "Production EASY selection did not start before shield transition.");
        _easyAttack = attack!;
        var expected = BossAttackCycle.EasySlots(_boss, phase)[0];
        EasyRequire(attack!.TechniqueKind == expected.Kind && attack.TechniqueVariant == expected.Variant,
            "Shield transition did not start the canonical first slot.");
        string name = attack.SpellName, key = attack.MechanicKey;
        int startSlot = danmaku.EasySlot, startRounds = danmaku.EasyRounds;
        int hp = Read<int>(_boss, "_hp"), lives = _player.Lives;
        var audit = new EasyAttackAudit(attack, _player, _boss, _easyStart);
        bool ended = false, completed = false;
        double endedAt = -1;
        int shots = -1, remainingAtEnd = -1, hazardsAtEnd = -1;
        attack.Ended += endedAttack =>
        {
            ended = true;
            completed = endedAttack.Completed;
            endedAt = endedAttack.Elapsed;
            shots = endedAttack.ShotsCreated;
            remainingAtEnd = endedAttack.NaturalEndRemainingShots;
            hazardsAtEnd = endedAttack.NaturalEndLiveHazards;
            audit.End(endedAttack);
        };
        bool triggered = false;
        for (int frame = 0; frame < 120 * 60 && !ended; frame++)
        {
            bool striking = attack.IsStriking || attack.OwnedShots.Any(b => !b.LeadIn && b.CollisionLayer != 0);
            bool fullWarning = !striking && attack.EasyCues.Any(c => !c.Cancelled && !c.Live && c.Progress >= 1);
            if (triggerLive ? striking : fullWarning) { triggered = true; break; }
            await EasyTransitionFrame();
            if (!ended) audit.Observe(attack, _player.GlobalPosition);
        }
        EasyRequire(triggered && !ended, $"No {(triggerLive ? "live" : "complete-warning")} shield trigger window: {key}");
        double triggerAt = attack.Elapsed;
        ulong triggerFrame = Engine.GetPhysicsFrames();
        var before = EasyTransitionShots(attack);
        var hazardsBefore = EasyTransitionHazards(attack);
        Call(_boss, "ResolveShieldBreak");
        EasyRequire(_boss.GaugeAttackResolving && !_boss.GaugeVulnerable && !attack.Finished,
            "Shield break interrupted an active EASY technique or entered BREAK immediately.");
        EasyRequire(before.SetEquals(EasyTransitionShots(attack))
            && hazardsBefore.SequenceEqual(EasyTransitionHazards(attack)), "Shield trigger removed bullets or live hazards.");
        int resolvingFrames = 0, peakAfterTrigger = before.Count;
        for (int frame = 0; frame < 180 * 60 && !ended; frame++)
        {
            EasyRequire(_boss.GaugeAttackResolving && !_boss.GaugeVulnerable,
                "BREAK or EXPOSED began before the active technique completed.");
            resolvingFrames++;
            await EasyTransitionFrame();
            if (!ended)
            {
                peakAfterTrigger = Math.Max(peakAfterTrigger, attack.OwnedShots.Count());
                audit.Observe(attack, _player.GlobalPosition);
            }
        }
        EasyRequire(ended && completed && resolvingFrames > 0, "EASY technique failed to complete naturally while resolving shield break.");
        EasyRequire(remainingAtEnd == 0 && hazardsAtEnd == 0,
            "Natural completion cleared active bullets or hazards during shield resolution.");
        audit.Validate();
        int expectedSlot = (startSlot + 1) % BossAttackCycle.EasySlots(_boss, phase).Count;
        int expectedRounds = startRounds + (expectedSlot == 0 ? 1 : 0);
        EasyRequire(danmaku.EasySlot == expectedSlot && danmaku.EasyRounds == expectedRounds,
            "Resolving technique did not advance its production slot exactly once.");
        bool sawBreak = false, exposed = false;
        ulong breakFrame = 0, exposedFrame = 0;
        for (int frame = 0; frame < 180 && !exposed; frame++)
        {
            await EasyTransitionFrame();
            if (_boss.GaugeBreaking && !sawBreak) { sawBreak = true; breakFrame = Engine.GetPhysicsFrames(); }
            if (_boss.GaugeBodyExposed) { exposed = true; exposedFrame = Engine.GetPhysicsFrames(); }
            EasyRequire(!GetTree().GetNodesInGroup("boss_character_techniques")
                .OfType<BossCharacterTechnique>().Any(a => !a.Finished), "Another technique began during BREAK or EXPOSED.");
        }
        EasyRequire(sawBreak && exposed && breakFrame < exposedFrame,
            "Production shield resolution did not progress through BREAK to EXPOSED.");
        EasyRequire(Read<int>(_boss, "_hp") == hp && _player.Lives == lives,
            "Shield transition changed HP or player lives without damage input.");
        _easyTransitions.Add(new
        {
            character = _scene, difficulty = "Easy", phase, test = "shield-resolution",
            trigger_stage = triggerLive ? "live" : "complete-warning", name, mechanic = key, passed = true,
            trigger_at = triggerAt, trigger_frame = triggerFrame, completed_at = endedAt,
            break_frame = breakFrame, exposed_frame = exposedFrame, resolving_frames = resolvingFrames,
            shots, owned_at_trigger = before.Count, live_hazards_at_trigger = hazardsBefore.Length,
            peak_owned_after_trigger = peakAfterTrigger,
            trigger_preserved_all_owned_ids_and_live_hazards = true,
            natural_end_owned_before_cleanup = remainingAtEnd, natural_end_live_hazards_before_cleanup = hazardsAtEnd,
            start_slot = startSlot, end_slot = danmaku.EasySlot, start_rounds = startRounds, end_rounds = danmaku.EasyRounds,
            hp_unchanged = true, lives_unchanged = true,
            execution = new { boss_physics_calls = _easyTransitionBossCalls, caster_process_calls = _easyTransitionCasterCalls,
                direct_danmaku_tick_calls = 0, boss_native_physics_enabled = false, input = "stationary", invincible = true, shooting = false },
            scope = new { shield_trigger = "production ResolveShieldBreak invoked by reflection", panel_damage_input_verified = false,
                enemy_phase_machine_updated = true, natural_completion_verified = true, whole_technique_retirements_individually_verified = false,
                hp_thresholds_verified = false, full_battle_or_human_difficulty_verified = false },
            audit = audit.Result(),
        });
        SaveEasyTransitions();
        _easyAttack = null;
        _running = false;
        _pilot.Release();
        Call(_boss, "EnterShielded");
        await EasyPhysicsFrames(2);
        GD.Print($"[EASY] TRANSITION shield phase={phase} stage={(triggerLive ? "live" : "warning")} {key} PASS");
    }

    private async Task EasyThresholdFinale(int phase)
    {
        PrepareEasyTransition(phase);
        var caster = Read<AreaSpellCaster>(_boss, "_caster");
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        Write(_boss, "_memoryPlayed", true);
        Write(_boss, "_memoryPending", false);
        Write(_boss, "_beatsFired", 3);
        Write(_boss, "_pattern", 3);
        float threshold;
        if (_boss is BossRei)
        {
            threshold = Read<float>(_boss, "_relayHp");
            Write(_boss, "_relayFired", false);
        }
        else
        {
            threshold = Read<float>(_boss, "_gotoHp");
            Write(_boss, "_midpointPassed", true);
            Write(_boss, "_gotoFired", false);
            Write(_boss, "_gotoPhase", 0);
        }
        Call(_boss, _boss is BossRei ? "ApplyPhaseVisual" : "ApplySpell");
        int hpAtThreshold = Mathf.FloorToInt(Read<int>(_boss, "_maxHp") * threshold);
        Write(_boss, "_hp", hpAtThreshold + 1);
        int livesBefore = _player.Lives;
        _boss.DealDirectDamage(1);
        EasyRequire(Read<int>(_boss, "_hp") == hpAtThreshold && _boss.HpRatio <= threshold,
            "Production direct damage did not cross the configured finale HP threshold.");
        EasyRequire(caster.CycleFinalePending && caster.AoeActive,
            "OnHpChanged did not queue the native finale request at its HP threshold.");
        EasyRequire(_boss is BossRei ? Read<bool>(_boss, "_relayFired") : Read<bool>(_boss, "_gotoFired"),
            "The native one-shot finale threshold flag was not set.");
        BossCharacterTechnique? finale = null;
        for (int frame = 0; frame < 600 && finale == null; frame++)
        {
            await EasyTransitionFrame();
            finale = GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>()
                .FirstOrDefault(a => !a.Finished && a.TechniqueKind == BossTechniqueKind.Finale);
        }
        EasyRequire(finale != null, "Native threshold finale never began.");
        _easyAttack = finale!;
        string name = finale!.SpellName, key = finale.MechanicKey;
        var audit = new EasyAttackAudit(finale, _player, _boss, _easyStart);
        bool ended = false, completed = false;
        int shots = -1, remaining = -1, hazards = -1;
        double endedAt = -1;
        finale.Ended += attack =>
        {
            ended = true;
            completed = attack.Completed;
            shots = attack.ShotsCreated;
            remaining = attack.NaturalEndRemainingShots;
            hazards = attack.NaturalEndLiveHazards;
            endedAt = attack.Elapsed;
            audit.End(attack);
        };
        for (int frame = 0; frame < 180 * 60 && !ended; frame++)
        {
            await EasyTransitionFrame();
            if (!ended) audit.Observe(finale, _player.GlobalPosition);
        }
        EasyRequire(ended && completed && caster.FinaleCompleted && remaining == 0 && hazards == 0,
            "Threshold finale failed to complete naturally and acknowledge completion.");
        audit.Validate();
        _easyAttack = null;
        BossCharacterTechnique? resumed = null;
        for (int frame = 0; frame < 180 && resumed == null; frame++)
        {
            await EasyTransitionFrame();
            if (danmaku.ActiveTechnique is { Finished: false } attack) resumed = attack;
        }
        EasyRequire(resumed != null, "Canonical EASY slots did not resume after the threshold finale.");
        _easyAttack = resumed!;
        var expected = BossAttackCycle.EasySlots(_boss, phase)[0];
        EasyRequire(danmaku.EasySlot == 0 && resumed!.TechniqueKind == expected.Kind && resumed.TechniqueVariant == expected.Variant,
            "External finale completion skipped or replaced the canonical EASY slot.");
        if (_boss is BossKoharu)
            EasyRequire(Read<int>(_boss, "_gotoPhase") == 0 && !caster.Suppressed,
                "Koharu's native finale left its goto gate or caster suppression active.");
        EasyRequire(Read<int>(_boss, "_hp") == hpAtThreshold && _player.Lives >= livesBefore,
            "Threshold finale changed boss HP again or lost player lives without damage input.");
        string resumedKey = resumed!.MechanicKey;
        int livesAfter = _player.Lives;
        resumed.Cancel();
        EasyRequire(danmaku.EasySlot == 0, "Cleanup cancellation advanced the resumed slot.");
        _easyAttack = null;
        _easyTransitions.Add(new
        {
            character = _scene, difficulty = "Easy", phase, test = "hp-threshold-finale", passed = true,
            threshold_ratio = threshold, seeded_hp = hpAtThreshold + 1, direct_damage = 1, hp_after_damage = hpAtThreshold,
            name, mechanic = key, completed_at = endedAt, shots,
            natural_end_owned_before_cleanup = remaining, natural_end_live_hazards_before_cleanup = hazards,
            native_finale_completion_acknowledged = caster.FinaleCompleted,
            canonical_slot_resumed = resumedKey, resumed_slot = danmaku.EasySlot,
            resumed_cleanup_cancel_preserved_cursor = true,
            lives_before = livesBefore, lives_after = livesAfter,
            execution = new { boss_physics_calls = _easyTransitionBossCalls, caster_process_calls = _easyTransitionCasterCalls,
                direct_danmaku_tick_calls = 0, boss_native_physics_enabled = false, input = "stationary", invincible = true, shooting = false },
            scope = new { hp_setup = "threshold plus one seeded by reflection", damage_input = "production DealDirectDamage(1)",
                on_hp_changed_and_request_finale_updated = true, caster_process_updated = true,
                story_and_form_already_completed_in_qa_setup = true, ordinary_body_bullet_damage_verified = false,
                panel_damage_input_verified = false, full_battle_or_human_difficulty_verified = false,
                existing_rei_no_hit_reward_callback_may_add_life = _boss is BossRei },
            audit = audit.Result(),
        });
        SaveEasyTransitions();
        _running = false;
        _pilot.Release();
        Call(_boss, "EnterShielded");
        await EasyPhysicsFrames(2);
        GD.Print($"[EASY] TRANSITION threshold phase={phase} {key} PASS resumed={resumedKey}");
    }

    private void SaveEasyTransitions()
        => System.IO.File.WriteAllText(_output + "/transitions.json", JsonSerializer.Serialize(_easyTransitions,
            new JsonSerializerOptions { WriteIndented = true }));
}
