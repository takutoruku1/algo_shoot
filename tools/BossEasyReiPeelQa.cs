using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private async Task RunReiColorCallQa(int phase)
    {
        _running = false;
        _pilot.Release();
        var starts = EasyStarts;
        int cases = 0, stationaryContacts = 0;
        foreach (var start in starts)
        foreach (bool stationary in new[] { true, false })
        {
            _player.GlobalPosition = start;
            Write(_player, "_invincible", stationary);
            Write(_player, "_invincibleTimer", stationary ? 99999f : 0f);
            Write(_player, "_hitInvincible", false);
            Write(_player, "_fireCooldown", 99999f);
            int lives = _player.Lives;
            GD.Seed((ulong)(2100 + cases / 2));
            var attack = (ReiBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Area, 3);
            attack.SetPhysicsProcess(false);
            _easyAttack = attack;
            var fired = new HashSet<int>();
            var checkedEdges = new HashSet<int>();
            var gaps = new Dictionary<int, Rect2>();
            var probes = new HashSet<Vector2>();
            for (float y = Field.Top + 3; y < Field.Bottom; y += 15)
                for (float x = Field.Left + 3; x < Field.Right; x += 15) probes.Add(new(x, y));
            int contacts = 0, bodyContacts = 0;
            double minimumHold = double.PositiveInfinity;
            for (int frame = 0; frame < 30 * 60 && !attack.Finished; frame++)
            {
                if (!stationary)
                {
                    Vector2 before = _player.GlobalPosition;
                    TryEasyGuidance(1d / 60);
                    Input.FlushBufferedEvents();
                    Pad.ReleaseMouseFollow();
                    _player._PhysicsProcess(1d / 60);
                    EasyRequire(before.DistanceTo(_player.GlobalPosition) <= _player.SlowestMoveSpeed / 60 + .01f,
                        "Stamp route exceeded normal locked movement speed.");
                    if (EasyBodyAt(_player.GlobalPosition)) bodyContacts++;
                }
                attack.Tick(1d / 60);
                if (attack.Finished) break;
                if (attack.CoversPoint(_player.GlobalPosition)) contacts++;
                if (attack.ColorCallCue is not { } cue || attack.Elapsed < cue.FiresAt)
                {
                    if (fired.Count == 0) EasyRequire(!attack.IsStriking, "The initial stamp forecast dealt damage.");
                    else
                    {
                        float upper = attack.NearestStampGutter(Field.CenterX, true);
                        float lower = attack.NearestStampGutter(Field.CenterX, false);
                        EasyRequire(!attack.CoversPoint(new Vector2(upper, Field.Top + 5))
                            && !attack.CoversPoint(new Vector2(lower, Field.Bottom - 5)), "The previous edition closed its transfer gutter.");
                    }
                    continue;
                }
                fired.Add(cue.Index);
                minimumHold = Math.Min(minimumHold, cue.FiresAt - cue.ReadyAt);
                if (gaps.TryGetValue(cue.Index, out var previous))
                    EasyRequire(previous == cue.Gap, "The opening moved after launch.");
                gaps[cue.Index] = cue.Gap;
                probes.RemoveWhere(attack.CoversPoint);
                var stamp = attack.PracticeHazards.FirstOrDefault(h => h.Live && h.Shape == 2 && h.A.Y == Field.Top
                    && h.A.X > Field.Left + 20 && h.B.X < Field.Right - 20);
                var lowerStamp = attack.PracticeHazards.FirstOrDefault(h => h.Live && h.Shape == 2 && h.B.Y == Field.Bottom
                    && h.A.X > Field.Left + 20 && h.B.X < Field.Right - 20);
                if (!stamp.Live || !lowerStamp.Live) continue;
                checkedEdges.Add(cue.Index);
                float left = stamp.A.X, x = (left + stamp.B.X) / 2;
                float lowerX = (lowerStamp.A.X + lowerStamp.B.X) / 2;
                EasyRequire(!attack.CoversPoint(new Vector2(x, cue.Gap.GetCenter().Y)), "The visible opening dealt damage.");
                EasyRequire(attack.CoversPoint(new Vector2(x, cue.Gap.Position.Y + Player.HitRadius - .1f)) &&
                    !attack.CoversPoint(new Vector2(x, cue.Gap.Position.Y + Player.HitRadius + .1f)) &&
                    attack.CoversPoint(new Vector2(lowerX, cue.Gap.End.Y - Player.HitRadius + .1f)) &&
                    !attack.CoversPoint(new Vector2(lowerX, cue.Gap.End.Y - Player.HitRadius - .1f)),
                    "Stamp collision disagreed with the visible opening edges.");
                float ahead = left - Player.HitRadius - 1, behind = lowerStamp.B.X + Player.HitRadius + 1;
                EasyRequire(!attack.CoversPoint(new Vector2(ahead, Field.Top + 5)) &&
                    !attack.CoversPoint(new Vector2(behind, Field.Bottom - 5)), "Damage extends beyond the moving stamps.");
            }
            _pilot.Release();
            EasyRequire(attack.Completed && !attack.IsStriking && attack.NaturalEndRemainingShots == 0 &&
                attack.NaturalEndLiveHazards == 0, "Stamp waves did not finish cleanly.");
            EasyRequire(fired.SetEquals(new[] { 0, 1 }) && checkedEdges.SetEquals(fired) && !gaps[0].Intersects(gaps[1]) && probes.Count == 0,
                "Both stamp trains must cross the field with distinct openings and measured leading and trailing edges.");
            EasyRequire(_game.Difficulty != GameManager.Diff.Easy || minimumHold >= 2, "EASY stamp forecast held for less than two seconds.");
            if (stationary)
            {
                EasyRequire(contacts > 0, "A fresh stationary start avoided both waves.");
                stationaryContacts += contacts;
            }
            else EasyRequire(contacts == 0 && bodyContacts == 0 && _player.Lives == lives,
                $"Stamp route failed at {start}: attack={contacts} body={bodyContacts} life_loss={lives - _player.Lives}.");
            GD.Print($"[REI-COLOR] PASS phase={phase} start={start} stationary={stationary} waves={fired.Count} hold={minimumHold:0.00}s attack_contacts={contacts} body_contacts={bodyContacts}");
            _easyAttack = null;
            await EasyPhysicsFrames(2);
            cases++;
        }
        Write(_player, "_invincible", true);
        Write(_player, "_invincibleTimer", 99999f);
        _player.GlobalPosition = EasyStarts[0];
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        bool cycle = _boss.UsesTechniqueCycle;
        var slots = await ReadyReiQaCycle(danmaku, cycle);
        int targetSlot = slots.ToList().FindIndex(slot => slot.Kind == BossTechniqueKind.Area && slot.Variant == 3);
        EasyRequire(targetSlot >= 0 && slots.Count(slot => slot.Kind == BossTechniqueKind.Area && slot.Variant == 3) == 1,
            "Color call is missing or duplicated in the normal battle sequence.");
        Write(danmaku, cycle ? "_cycleSlot" : "_easySlot", targetSlot);
        Write(danmaku, "_timer", 0d);
        danmaku.Tick(.01);
        var selected = danmaku.ActiveTechnique ?? throw new InvalidOperationException(
            $"Color call not selected: phase={phase}, slot={danmaku.CycleSlot}/{targetSlot}, timer={Read<double>(danmaku, "_timer")}, " +
            $"position={_boss.ShotCenter}, reforming={_boss.GaugeReforming}, returning={_boss.BossFrameReturning}, " +
            $"transforming={_boss.Transforming}, bubble={Hud.BubblePaused}.");
        EasyRequire(selected is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Area, TechniqueVariant: 3 },
            $"Color-call selection failed: phase={phase}, slot={danmaku.CycleSlot}/{targetSlot}, active={selected.SpellName}, " +
            $"timer={Read<double>(danmaku, "_timer")}, position={_boss.ShotCenter}, reforming={_boss.GaugeReforming}, " +
            $"returning={_boss.BossFrameReturning}, transforming={_boss.Transforming}, bubble={Hud.BubblePaused}.");
        selected.SetPhysicsProcess(false);
        for (int frame = 0; frame < 600 && !selected.IsStriking; frame++) selected.Tick(1d / 60);
        EasyRequire(selected.IsStriking, "No live stamps appeared before interruption.");
        selected.Cancel();
        EasyRequire(!selected.Completed && !selected.IsStriking && (cycle ? danmaku.CycleSlot : danmaku.EasySlot) != targetSlot,
            "Cancelling a color call left damage or repeated its slot.");
        await EasyPhysicsFrames(2);
        danmaku.Tick(.1);
        danmaku.Tick(1);
        selected = await RestartAfterDifferentTechnique(danmaku, cycle, targetSlot);
        EasyRequire(selected is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Area, TechniqueVariant: 3 } && !selected.Finished,
            "Color-call completion fixture did not restart after another technique.");
        selected.SetPhysicsProcess(false);
        int completedBefore = Read<int>(danmaku, cycle ? "_cycleCompleted" : "_easyCompleted");
        for (int frame = 0; frame < 120 * 60 && !selected.Finished; frame++) selected.Tick(1d / 60);
        int nextSlot = cycle ? danmaku.CycleSlot : danmaku.EasySlot;
        EasyRequire(selected.Completed && nextSlot != targetSlot && nextSlot >= 0 && nextSlot < slots.Count
            && Read<int>(danmaku, cycle ? "_cycleCompleted" : "_easyCompleted") == completedBefore + 1,
            "Completed color call did not advance to the next technique.");
        await EasyPhysicsFrames(2);
        danmaku.Tick(.1);
        danmaku.Tick(1);
        selected = danmaku.ActiveTechnique!;
        var next = slots[nextSlot];
        EasyRequire(selected.TechniqueKind == next.Kind && selected.TechniqueVariant == next.Variant, "Color call skipped the following technique.");
        selected.Cancel();
        await EasyPhysicsFrames(2);
        GD.Print($"[REI-COLOR] COMPLETE difficulty={_game.Difficulty} phase={phase} cases={cases} static_contact_frames={stationaryContacts} moving_damage=0 body_contacts=0 selection_retry_completion=true human_test=false");
    }

    private async Task RunReiSelectionQa(int phase)
    {
        _running = false;
        _pilot.Release();
        int Stage(ReiBossTechnique attack) => (int)typeof(ReiBossTechnique)
            .GetProperty("SelectionStage", Private)!.GetValue(attack)!;
        void CheckCancelled(ReiBossTechnique attack)
        {
            EasyRequire(attack.Finished && !attack.Completed && Stage(attack) == 4,
                "Cancelled selection did not enter its finished presentation state.");
            EasyRequire(!attack.Visible && !attack.IsStriking && !attack.OwnedShots.Any(),
                "Cancelled selection left a visible cue, live hazard or owned projectile.");
            EasyRequire(!Read<bool>(_boss, "_animalWindup"), "Cancelled selection left the animal windup active.");
        }
        float margin = _game.Difficulty switch { GameManager.Diff.Easy => 28, GameManager.Diff.Normal => 40,
            GameManager.Diff.Hard => 56, _ => 32 };
        float verticalMargin = _game.Difficulty == GameManager.Diff.Lunatic ? 56 : margin;
        void CheckRecordedBounds(Rect2 bounds, Vector2 min, Vector2 max)
        {
            EasyRequire(bounds.Size.X + .001f >= 2 * margin && bounds.Size.Y + .001f >= 2 * verticalMargin,
                "Selection lost its minimum width or height at a field edge or corner.");
            EasyRequire(bounds.Grow(.001f).HasPoint(min) && bounds.Grow(.001f).HasPoint(max),
                "Selection excluded part of the player's recorded movement.");
            EasyRequire(Field.Rect.Grow(.001f).Encloses(bounds), "Selection extended outside the playable field.");
        }
        int cases = 0;
        double minimumHold = double.PositiveInfinity;
        foreach (var start in EasyStarts)
        foreach (int profile in Enumerable.Range(0, 5))
        {
            _player.GlobalPosition = start;
            var attack = (ReiBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Normal, 0);
            attack.SetPhysicsProcess(false);
            Rect2 previous = Read<Rect2>(attack, "_selectionRect");
            Vector2 min = start, max = start;
            CheckRecordedBounds(previous, min, max);
            var recordingFrames = new HashSet<int>();
            for (int frame = 0; frame < 121; frame++)
            {
                Vector2 input = profile switch { 1 => Vector2.Right, 2 => Vector2.Up,
                    3 => frame < 60 ? Vector2.Left : Vector2.Right, 4 => new Vector2(1, -1).Normalized(), _ => Vector2.Zero };
                if (frame < 120)
                {
                    AutoplayInput.Move(input);
                    Input.FlushBufferedEvents();
                    _player._PhysicsProcess(1d / 60);
                }
                min = min.Min(_player.GlobalPosition);
                max = max.Max(_player.GlobalPosition);
                attack.Tick(1d / 60);
                Rect2 current = Read<Rect2>(attack, "_selectionRect");
                EasyRequire(current.Grow(.001f).Encloses(previous), "Selection shrank after reversal.");
                CheckRecordedBounds(current, min, max);
                if (!Read<bool>(attack, "_easySelectionBuilt"))
                {
                    EasyRequire(Stage(attack) == 0 && attack.AvatarFrame is >= 0 and <= 2,
                        "Recording selection does not match its preparing avatar pose.");
                    EasyRequire(!attack.IsStriking && attack.ShotsCreated == 0,
                        "Selection became damaging while still recording movement.");
                    recordingFrames.Add(attack.AvatarFrame);
                }
                previous = current;
            }
            EasyRequire(recordingFrames.Count >= 2, "Selection recording never changed its preparation pose.");
            Vector2 expectedSize = (max - min + Vector2.One * (2 * margin)).Min(Field.Rect.Size);
            Vector2 expectedPosition = (min - Vector2.One * margin).Clamp(Field.Rect.Position, Field.Rect.End - expectedSize);
            Rect2 expected = new(expectedPosition, expectedSize);
            EasyRequire(previous.Position.DistanceTo(expected.Position) < .01f && previous.Size.DistanceTo(expected.Size) < .01f,
                "Selection did not preserve the recorded travel and margins while fitting inside the field.");
            double lockedAt = Read<double>(attack, "_selectionLockedAt");
            EasyRequire(Math.Abs(lockedAt - 2) <= 1d / 60 + .00001,
                "Selection lock did not coincide with the end of the two-second recording.");
            EasyRequire(Stage(attack) == 1 && attack.AvatarFrame == 3,
                "Confirmed selection did not hold its locked avatar pose.");
            Vector2 recordedEnd = _player.GlobalPosition;
            AutoplayInput.Move(Vector2.Zero);
            _player.GlobalPosition = Field.Rect.GetCenter();
            double? fullAt = null;
            while (!attack.IsStriking && attack.Elapsed < lockedAt + 4)
            {
                attack.Tick(1d / 60);
                EasyRequire(Read<Rect2>(attack, "_selectionRect") == previous, "Selection changed after locking.");
                var outside = attack.EasyCues.Where(cue => cue.Key.StartsWith("selection:outside:")).ToArray();
                EasyRequire(outside.Length > 0, "Selection did not forecast its excluded outside area.");
                if (outside.All(cue => cue.Progress >= 1)) fullAt ??= attack.Elapsed;
                if (!attack.IsStriking)
                    EasyRequire(Stage(attack) == 1 && attack.AvatarFrame == 3,
                        "Selection released its locked pose before the outside attack.");
            }
            EasyRequire(fullAt.HasValue, "Selection outside activated without a completed forecast.");
            double hold = attack.Elapsed - fullAt!.Value;
            minimumHold = Math.Min(minimumHold, hold);
            EasyRequire(hold + 1d / 60 >= 2, "Completed selection outside forecast held for less than two seconds.");
            EasyRequire(Stage(attack) == 2 && attack.AvatarFrame is 4 or 5,
                "Outside activation did not coincide with the release pose.");
            EasyRequire(attack.ShotsCreated == 0, "Quotation projectiles started before the frame cut.");
            float width = new[] { 14, 12, 10, 8 }[(int)_game.Difficulty];
            Vector2 gesture = recordedEnd - start;
            if (gesture.Length() >= 20)
            {
                var edges = attack.PracticeHazards.Where(h => h.Live && h.Shape == 0 && h.Radius > Field.Width).ToArray();
                EasyRequire(edges.Length == 2 && edges.All(edge => Math.Abs(edge.A.DirectionTo(edge.B)
                    .Dot(gesture.Normalized())) > .9999f), "The selected cut did not follow the recorded stroke.");
                Vector2 mid = (start + recordedEnd) / 2, normal = gesture.Normalized().Orthogonal();
                EasyRequire(!attack.CoversPoint(mid) && attack.CoversPoint(mid + normal * width),
                    "The selected stroke does not separate its passage from the trimmed paper.");
            }
            else
            {
                var plates = attack.PracticeHazards.Where(h => h.Live && h.Shape == 2 && Math.Abs(h.A.Y - previous.Position.Y) < .01f
                    && Math.Abs(h.B.Y - previous.End.Y) < .01f && h.A.X >= previous.Position.X && h.B.X <= previous.End.X)
                    .OrderBy(h => h.A.X).ToArray();
                EasyRequire(plates.Length == 2 && Math.Abs(plates[1].A.X - plates[0].B.X - width) < .01f,
                    "A still selection did not retain its default fold.");
            }
            foreach (Vector2 point in new[] { new Vector2(Field.Left + 2, 2), new Vector2(Field.Right - 2, 2),
                new Vector2(Field.Left + 2, Field.Bottom - 2), new Vector2(Field.Right - 2, Field.Bottom - 2) })
                if (!previous.Grow(Player.HitRadius).HasPoint(point))
                    EasyRequire(attack.CoversPoint(point), "Selection leaves its outside harmless.");
            double outsideAt = attack.Elapsed;
            while (attack.ShotsCreated == 0 && attack.Elapsed < lockedAt + 6) attack.Tick(1d / 60);
            EasyRequire(Math.Abs(attack.Elapsed - outsideAt - 1.4) <= 2d / 60,
                "Quotation release did not preserve its separate timing after outside activation.");
            EasyRequire(Stage(attack) == 3 && attack.AvatarFrame is 4 or 5,
                "Quotation release did not coincide with its firing pose.");
            Vector2 cutPoint = new(previous.GetCenter().X, Read<Vector2>(attack, "_selectionEnd").Y);
            EasyRequire(attack.CoversPoint(cutPoint), "Quotation release did not activate the forecast cutting line.");
            attack.Cancel();
            CheckCancelled(attack);
            await EasyPhysicsFrames(1);
            cases++;
        }
        foreach (double stopAt in new[] { .8, 2.6, 5.8, 7.0 })
        {
            _player.GlobalPosition = EasyStarts[0];
            var attack = (ReiBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Normal, 0);
            attack.SetPhysicsProcess(false);
            while (attack.Elapsed < stopAt) attack.Tick(1d / 60);
            attack.Cancel();
            CheckCancelled(attack);
            await EasyPhysicsFrames(1);
        }
        _player.GlobalPosition = EasyStarts[0];
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        bool cycle = _boss.UsesTechniqueCycle;
        int Cursor() => cycle ? danmaku.CycleSlot : danmaku.EasySlot;
        int segment = cycle ? BossAttackCycle.Segment(_boss) : phase;
        var slots = cycle ? BossAttackCycle.Slots(_boss, segment) : BossAttackCycle.EasySlots(_boss, segment);
        int targetSlot = slots.ToList().FindIndex(slot => slot.Kind == BossTechniqueKind.Normal && slot.Variant == 0);
        EasyRequire(targetSlot >= 0 && slots.Count(slot => slot.Kind == BossTechniqueKind.Normal && slot.Variant == 0) == 1,
            "Selection is missing or duplicated in the normal battle sequence.");
        var selected = await StartReiQaSlot(danmaku, cycle, targetSlot);
        EasyRequire(selected is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Normal, TechniqueVariant: 0 },
            "Normal battle selection did not start the selection technique.");
        selected!.SetPhysicsProcess(false);
        selected.Tick(1);
        selected.Cancel();
        CheckCancelled((ReiBossTechnique)selected);
        EasyRequire(Cursor() != targetSlot, "Cancelling selection repeated the normal battle slot.");
        await EasyPhysicsFrames(2);
        danmaku.Tick(.1);
        danmaku.Tick(1);
        var retry = await RestartAfterDifferentTechnique(danmaku, cycle, targetSlot);
        EasyRequire(retry is ReiBossTechnique { TechniqueKind: BossTechniqueKind.Normal, TechniqueVariant: 0, Finished: false }
            && !ReferenceEquals(retry, selected), "Selection completion fixture did not restart after another technique.");
        bool completed = false, ended = false;
        int remaining = -1, liveHazards = -1, shots = 0, expectedShots = 0;
        double completedAt = 0;
        retry!.Ended += attack =>
        {
            ended = true;
            completed = attack.Completed;
            remaining = attack.NaturalEndRemainingShots;
            liveHazards = attack.NaturalEndLiveHazards;
            shots = attack.ShotsCreated;
            expectedShots = Read<System.Collections.IEnumerable>(attack, "_easyPaths").Cast<object>().Count();
            completedAt = attack.Elapsed;
        };
        for (int frame = 0; frame < 120 * 60 && !ended; frame++) await EasyPhysicsFrames(1);
        EasyRequire(ended && completed, "Retried selection did not complete naturally.");
        EasyRequire(remaining == 0 && liveHazards == 0 && !Read<bool>(_boss, "_animalWindup"),
            "Naturally completed selection left active attacks or an animal windup.");
        EasyRequire(shots > 0 && shots == expectedShots, "Retried selection skipped planned projectile emissions.");
        int nextSlot = Cursor();
        EasyRequire(nextSlot != targetSlot, "Natural selection completion did not advance the battle slot.");
        await EasyPhysicsFrames(2);
        danmaku.Tick(.1);
        danmaku.Tick(1);
        var following = danmaku.ActiveTechnique;
        EasyRequire(following is { Finished: false } && following.TechniqueKind == slots[nextSlot].Kind
            && following.TechniqueVariant == slots[nextSlot].Variant, "The next normal battle slot did not start after selection.");
        following!.Cancel();
        await EasyPhysicsFrames(2);
        _easyCycles.Add(new { phase, difficulty = _game.Difficulty.ToString(), segment, passed = true,
            target_slot = targetSlot, kind = "Normal", variant = 0, production_selection = true,
            cancellation_advances_cursor = true, retry_after_another_technique = true, natural_completion = true,
            completed_at = completedAt, shots, expected_shots = expectedShots, remaining_projectiles = remaining,
            remaining_live_hazards = liveHazards, next_slot = nextSlot, next_kind = slots[nextSlot].Kind.ToString(),
            next_variant = slots[nextSlot].Variant, following_technique_started = true, invincible = true,
            shooting = false, bombs = false, dodge = false, human_difficulty_test = false });
        _easyCases.Add(new { character = _scene, difficulty = _game.Difficulty.ToString(), phase,
            kind = "Normal", variant = 0, audit = "selection_recording_and_presentation", passed = true,
            profiles = cases, minimum_complete_hold_seconds = minimumHold, cancel_stages = 4,
            invincible = true, normal_speed_recording = true, recording_geometry_verified = true,
            minimum_selection_size = 2 * margin, edge_margins_preserved = true,
            recorded_positions_enclosed = true, confined_to_playfield = true,
            stage_and_avatar_timing_verified = true, complete_attack_escape_verified = false,
            human_comprehension_or_difficulty_verified = false });
        GD.Print($"[REI-SELECTION] PASS difficulty={_game.Difficulty} phase={phase} profiles={cases} minimum_complete_hold={minimumHold:0.000}s normal_speed_recording=true live_input_recording=true reversal_preserved=true locked_bounds=true hard_trim_checked={_game.Difficulty == GameManager.Diff.Hard} pose_timing=true cancel_stages=4; full_attack_escape_and_human_comprehension_are_separate_tests");
    }

    private async Task<IReadOnlyList<BossAttackCycle.Slot>> ReadyReiQaCycle(BossDanmaku danmaku, bool cycle)
    {
        await SettleEasyFrame();
        var caster = Read<AreaSpellCaster>(_boss, "_caster");
        caster.CancelPendingAttacks();
        caster.Suppressed = false;
        danmaku.Tick(0);
        if (danmaku.ActiveTechnique is { } attached)
        {
            if (!attached.Finished) attached.Cancel();
            await SettleEasyFrame();
            danmaku.Tick(0);
        }
        int segment = Read<int>(danmaku, cycle ? "_cycleSegment" : "_easySegment");
        return cycle ? _boss.PracticeSlots ?? BossAttackCycle.Slots(_boss, segment) : BossAttackCycle.EasySlots(_boss, segment);
    }

    private async Task<BossCharacterTechnique?> StartReiQaSlot(BossDanmaku danmaku, bool cycle, int targetSlot)
    {
        await ReadyReiQaCycle(danmaku, cycle);
        Write(danmaku, cycle ? "_cycleSlot" : "_easySlot", targetSlot);
        Write(danmaku, "_timer", 0d);
        for (int frame = 0; frame < 120 && danmaku.ActiveTechnique == null; frame++)
        {
            danmaku.Tick(1d / 60);
            await EasyPhysicsFrames(1);
        }
        return danmaku.ActiveTechnique;
    }

    private void PrepareSourceCutPanels(int count = -1)
    {
        _boss.TechniquePractice = false;
        var panels = Read<List<Panel>>(_boss, "_panels");
        foreach (var panel in panels) panel.QueueFree();
        panels.Clear();
        if (Read<bool>(_boss, "_bodyDown")) Call(_boss, "SetDownBody", false);
        Call(_boss, "EnterShielded");
        _boss.FinishFormReveal();
        _boss.SetPanelsInvulnerable(false);
        if (count >= 0)
            while (panels.Count > count) panels[0].Shatter();
        _hud.HideBubble();
        Hud.BubblePaused = false;
    }

    private async Task RunReiSourceCutQa(int phase)
    {
        _running = false;
        _pilot.Release();
        int Panels() => Read<List<Panel>>(_boss, "_panels").Count;
        async Task RunCase(Vector2 start, bool miss, bool lastPanel = false)
        {
            PrepareSourceCutPanels(lastPanel ? 1 : -1);
            Vector2 bossStart = _boss.GlobalPosition;
            _player.GlobalPosition = start;
            _easyStart = start;
            _easyStationary = false;
            _sourceCutMiss = miss;
            Write(_player, "_invincible", true);
            Write(_player, "_invincibleTimer", 99999f);
            int initialPanels = Panels(), contacts = 0, bodyContacts = 0;
            float maximumStep = 0;
            Vector2 previous = start;
            var attack = (ReiBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Normal, 3);
            _easyAttack = attack;
            bossStart = _boss.GlobalPosition;
            Vector2 anchor = attack.SourceCutAnchor;
            EasyRequire(Field.Rect.Grow(-12).HasPoint(anchor) && anchor != bossStart
                && Field.BossFrameRect.HasPoint(bossStart), "Source-cut mark is not separated from the boss frame.");
            EasyRequire(!EasyBodyAt(start), "Source-cut input case started inside the boss body.");
            bool ended = false, completed = false, resolving = false;
            int remaining = -1, remainingHazards = -1;
            var rounds = attack.SourceCutRounds;
            var locked = new Dictionary<ReiBossTechnique.SourceCutRound, (Vector2 From, Vector2 To)>();
            var lockPositions = new Dictionary<ReiBossTechnique.SourceCutRound, Vector2>();
            var liveSecondsByRound = new Dictionary<ReiBossTechnique.SourceCutRound, double>();
            var actualHazards = Read<List<(Vector2 A, Vector2 B, float Radius, bool Live, float Progress, int Shape)>>(attack, "_hazards");
            var cues = new Dictionary<string, (double? Full, double? Actual)>();
            double elapsed = 0;
            double observedAt = 0, liveSeconds = 0, trackingOverlapSeconds = 0, simultaneousLiveSeconds = 0;
            double movingSeconds = 0, stationarySeconds = 0, emptySeconds = 0, maximumInternalEmptySeconds = 0;
            double? firstLiveAt = null, lastLiveAt = null;
            int peakLiveRounds = 0;
            attack.Ended += endedAttack =>
            {
                ended = true; completed = endedAttack.Completed; elapsed = endedAttack.Elapsed;
                remaining = endedAttack.NaturalEndRemainingShots;
                remainingHazards = endedAttack.NaturalEndLiveHazards;
            };
            _running = true;
            for (int frame = 0; frame < 120 * 60 && !ended; frame++)
            {
                await EasyPhysicsFrames(1);
                if (ended) break;
                EasyRequire(attack.SourceCutAnchor == anchor && Field.BossFrameRect.HasPoint(_boss.GlobalPosition),
                    "Source-cut moved its fixed mark or moved the boss out of its frame.");
                Vector2 at = _player.GlobalPosition;
                float step = at.DistanceTo(previous);
                maximumStep = Math.Max(maximumStep, step);
                previous = at;
                double sampleSeconds = attack.Elapsed - observedAt;
                observedAt = attack.Elapsed;
                if (step > .001f) movingSeconds += sampleSeconds;
                else stationarySeconds += sampleSeconds;
                var liveHazards = actualHazards.Where(hazard => hazard.Live && hazard.Shape == 0
                    && rounds.Any(round => round.From == hazard.A && round.To == hazard.B)).ToArray();
                var liveRounds = rounds.Where(round => round.Locked && liveHazards.Any(hazard =>
                    hazard.Shape == 0 && hazard.A == round.From && hazard.B == round.To)).ToArray();
                EasyRequire(liveRounds.Length == liveHazards.Length, "Source-cut live hazards do not match its locked rounds.");
                peakLiveRounds = Math.Max(peakLiveRounds, liveRounds.Length);
                foreach (var round in liveRounds)
                    liveSecondsByRound[round] = liveSecondsByRound.GetValueOrDefault(round) + sampleSeconds;
                int trackingRound = rounds.FindIndex(round => !round.Locked
                    && attack.Elapsed >= round.StartsAt && attack.Elapsed < round.LocksAt);
                if (trackingRound >= 0 && rounds.Take(trackingRound).Any(round => liveRounds.Contains(round)))
                    trackingOverlapSeconds += sampleSeconds;
                if (liveRounds.Length >= 2) simultaneousLiveSeconds += sampleSeconds;
                if (liveHazards.Length > 0)
                {
                    firstLiveAt ??= attack.Elapsed;
                    lastLiveAt = attack.Elapsed;
                    liveSeconds += sampleSeconds;
                    maximumInternalEmptySeconds = Math.Max(maximumInternalEmptySeconds, emptySeconds);
                    emptySeconds = 0;
                }
                else if (firstLiveAt.HasValue) emptySeconds += sampleSeconds;
                if (attack.CoversPoint(at))
                {
                    if (contacts == 0)
                        GD.Print($"[REI-SOURCE-CUT] FIRST CONTACT time={attack.Elapsed:0.000} point={at} radius={anchor.DistanceTo(at):0.00} active_beams={string.Join(',', liveRounds.Select(r => r.FiresAt))}");
                    contacts++;
                }
                if (EasyBodyAt(at)) bodyContacts++;
                foreach (var round in rounds)
                {
                    if (round.Locked)
                    {
                        if (locked.TryGetValue(round, out var forecast))
                            EasyRequire(round.From == forecast.From && round.To == forecast.To,
                                "Source-cut ray followed the player after its lock.");
                        else
                        {
                            locked.Add(round, (round.From, round.To));
                            lockPositions.Add(round, at);
                        }
                    }
                    else EasyRequire(round.FiredPulses == 0 && round.RemovedPanels == 0,
                        "Source-cut attacked or removed a panel before locking.");
                    EasyRequire(round.RemovedPanels is >= 0 and <= 1, "One source-cut round removed multiple panels.");
                }
                EasyRequire(Panels() == initialPanels - rounds.Sum(round => round.RemovedPanels),
                    "Panel count differs from the source-cut rewards.");
                foreach (var cue in attack.EasyCues)
                {
                    var observed = cues.GetValueOrDefault(cue.Key);
                    if (cue.Progress >= 1) observed.Full ??= attack.Elapsed;
                    if (cue.Live) observed.Actual ??= attack.Elapsed;
                    cues[cue.Key] = observed;
                }
                if (Panels() == 0)
                {
                    resolving |= _boss.GaugeAttackResolving;
                    EasyRequire(_boss.GaugeAttackResolving && !_boss.GaugeVulnerable,
                        "The final panel interrupted source-cut instead of allowing it to resolve.");
                }
                Call(_boss, "TickBossPhase", 1d / 60);
            }
            _running = false;
            _pilot.Release();
            var timing = new { sampled_hz = 60, measured_seconds = observedAt,
                actual_live_seconds = liveSeconds, previous_live_while_tracking_seconds = trackingOverlapSeconds,
                simultaneous_live_rounds_seconds = simultaneousLiveSeconds, peak_live_rounds = peakLiveRounds,
                maximum_internal_empty_seconds = maximumInternalEmptySeconds,
                moving_seconds = movingSeconds, stationary_seconds = stationarySeconds,
                first_live_at = firstLiveAt, last_live_at = lastLiveAt,
                pulse_spacing = attack.SourceCutSpacing, pulse_live = attack.SourceCutLive,
                lock_cadence = _game.Difficulty >= GameManager.Diff.Hard ? attack.SourceCutCadence : (double?)null,
                target_radius = attack.SourceCutTargetRadius };
            GD.Print($"[REI-SOURCE-CUT] CASE phase={phase} boss_start={bossStart} start={start} miss={miss} last_panel={lastPanel} elapsed={elapsed:0.00} panels={initialPanels}->{Panels()} aligned={string.Join(",", rounds.Select(round => round.Aligned))} pulses={string.Join(",", rounds.Select(round => round.FiredPulses))} removed={string.Join(",", rounds.Select(round => round.RemovedPanels))} contacts={contacts} body_contacts={bodyContacts}");
            GD.Print("[REI-SOURCE-CUT] TIMING " + JsonSerializer.Serialize(timing));
            if (rounds.Any(round => round.Aligned == miss))
                GD.Print("[REI-SOURCE-CUT] ROUTE " + JsonSerializer.Serialize(rounds.Select(round => new {
                    round.StartsAt, round.LocksAt, round.Aligned,
                    target_x = round.Target.X, target_y = round.Target.Y,
                    locked_line_offset = Geometry2D.GetClosestPointToSegment(anchor, round.From, round.To).DistanceTo(anchor),
                    lock_x = lockPositions.GetValueOrDefault(round).X, lock_y = lockPositions.GetValueOrDefault(round).Y })));
            EasyRequire(ended && completed && remaining == 0 && remainingHazards == 0,
                "Source-cut did not finish naturally without residual attacks.");
            EasyRequire(rounds.Count == (_game.Difficulty >= GameManager.Diff.Hard ? 4 : 3),
                "Source-cut skipped a round.");
            EasyRequire(rounds.All(round => round.FiredPulses == 3), "Source-cut did not emit all three pulses per round.");
            EasyRequire(rounds.All(round => Math.Abs(liveSecondsByRound.GetValueOrDefault(round)
                - 3 * attack.SourceCutLive) <= 6d / 60 + .00001), "Source-cut actual live time differs from three complete pulses.");
            for (int index = 1; index < rounds.Count; index++)
                if (_game.Difficulty >= GameManager.Diff.Hard)
                {
                    double cadence = rounds[index].LocksAt - rounds[index - 1].LocksAt;
                    double nextTracking = rounds[index].StartsAt - rounds[index - 1].LocksAt;
                    EasyRequire(cadence + .00001 >= attack.SourceCutCadence
                        && cadence <= attack.SourceCutCadence + 1d / 60 + .00001,
                        "Source-cut later lock was extended beyond its fixed cadence.");
                    EasyRequire(nextTracking + .00001 >= .12 && nextTracking <= .12 + 1d / 60 + .00001,
                        "Source-cut next tracking did not begin at the announced overlap.");
                    if (index >= 2)
                        EasyRequire(Math.Abs(rounds[index].From.DirectionTo(anchor)
                            .Dot(rounds[index - 2].From.DirectionTo(anchor))) <= Mathf.Sqrt(.5f) + .0001f,
                            "Source-cut later aiming axis is too close to a still-active earlier axis.");
                }
            if (_game.Difficulty < GameManager.Diff.Hard)
                EasyRequire(attack.SourceCutSpacing == .42 && attack.SourceCutLive == .24
                    && attack.SourceCutTargetRadius == 14, "EASY/NORMAL source-cut timing or target size changed.");
            EasyRequire(cues.Count > 0 && cues.Values.All(cue => cue.Full.HasValue && cue.Actual.HasValue
                && cue.Actual.Value - cue.Full.Value + 1d / 60 >= 2), "Source-cut did not hold a completed forecast for two seconds.");
            EasyRequire(maximumStep <= _player.MoveSpeed / 60 + .02f, "Source-cut guidance exceeded normal movement speed.");
            EasyRequire(bodyContacts == 0 && (miss || contacts == 0),
                "Source-cut success guidance crossed an active attack, or the boss body blocked a route.");
            if (miss)
                EasyRequire(rounds.All(round => !round.Aligned && round.RemovedPanels == 0) && Panels() == initialPanels,
                    "Deliberately misaligned source-cut incorrectly removed a panel.");
            else
                EasyRequire(rounds.All(round => round.Aligned) && rounds.Sum(round => round.RemovedPanels)
                    == Math.Min(initialPanels, rounds.Count), "Normal-speed source-cut guidance did not earn the expected panel rewards.");
            if (lastPanel)
            {
                EasyRequire(resolving && Panels() == 0, "Last-panel source-cut never entered the resolving phase.");
                Call(_boss, "TickBossPhase", 1d / 60);
                EasyRequire(_boss.GaugeBreaking, "Last-panel source-cut did not enter BREAK after natural completion.");
            }
            _easyCases.Add(new { character = _scene, difficulty = _game.Difficulty.ToString(), phase,
                kind = "Normal", variant = 3, audit = "source_cut_input_and_reward", passed = true,
                start = new { x = start.X, y = start.Y }, boss_start = new { x = bossStart.X, y = bossStart.Y },
                source_mark = new { x = anchor.X, y = anchor.Y },
                miss, last_panel = lastPanel, invincible = true,
                no_hit_route_required = !miss,
                shooting = false, bombs = false, dodge = false, initial_panels = initialPanels, remaining_panels = Panels(),
                elapsed, maximum_step = maximumStep, movement_speed = _player.MoveSpeed,
                timing,
                attack_contact_frames = contacts, body_contact_frames = bodyContacts,
                resolving_before_natural_completion = resolving, remaining_projectiles = remaining,
                remaining_live_hazards = remainingHazards, human_comprehension_test = false,
                rounds = rounds.Select(round => new { round.StartsAt, round.LocksAt, round.FiresAt, round.EndsAt,
                    round.Aligned, round.FiredPulses, round.RemovedPanels,
                    actual_live_seconds = liveSecondsByRound.GetValueOrDefault(round),
                    from_x = round.From.X, from_y = round.From.Y, to_x = round.To.X, to_y = round.To.Y,
                    target_x = round.Target.X, target_y = round.Target.Y,
                    lock_x = lockPositions[round].X, lock_y = lockPositions[round].Y }),
                minimum_complete_hold = cues.Values.Min(cue => cue.Actual!.Value - cue.Full!.Value) });
            _easyAttack = null;
            await EasyPhysicsFrames(2);
        }
        if (OS.GetCmdlineUserArgs().Contains("--rei-source-cut-pair"))
        {
            await RunCase(EasyStarts[0], false);
            await RunCase(EasyStarts[0], true);
            _sourceCutMiss = false;
            GD.Print($"[REI-SOURCE-CUT] INPUT PAIR PASS phase={phase} difficulty={_game.Difficulty}");
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--rei-source-cut-field-qa"))
        {
            Vector2 originalBossPosition = _boss.GlobalPosition;
            try
            {
                foreach (var bossStart in new[] { new Vector2(Field.BossFrameCenterX, 54), new Vector2(Field.BossFrameCenterX, 80),
                    new Vector2(Field.BossFrameCenterX, 144), new Vector2(Field.BossFrameCenterX, 164) })
                {
                    _boss.GlobalPosition = bossStart;
                    await RunCase(EasyStarts[0], false);
                    await RunCase(EasyStarts[0], true);
                    var diagonal = new Vector2(bossStart.X < Field.CenterX ? Field.Right - 6 : Field.Left + 6,
                        bossStart.Y < Field.CenterY ? Field.Bottom - 6 : Field.Top + 6);
                    await RunCase(diagonal, false);
                }
            }
            finally
            {
                _boss.GlobalPosition = originalBossPosition;
                _sourceCutMiss = false;
            }
            GD.Print($"[REI-SOURCE-CUT] FIELD PASS phase={phase} difficulty={_game.Difficulty} input_cases=12 boss_starts=4");
            return;
        }
        foreach (var start in EasyStarts)
        {
            await RunCase(start, false);
            await RunCase(start, true);
        }
        await RunCase(EasyStarts[0], false, lastPanel: true);
        for (int stop = 0; stop < 3; stop++)
        {
            PrepareSourceCutPanels();
            _sourceCutMiss = false;
            _player.GlobalPosition = _easyStart = EasyStarts[0];
            var attack = (ReiBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Normal, 3);
            _easyAttack = attack;
            _running = true;
            for (int frame = 0; frame < 60 * 30; frame++)
            {
                await EasyPhysicsFrames(1);
                var first = attack.SourceCutRounds.FirstOrDefault();
                if (first != null && (stop == 0 ? attack.Elapsed >= .3 : stop == 1 ? first.Locked : first.FiredPulses > 0)) break;
            }
            int before = Panels();
            _running = false;
            _pilot.Release();
            attack.Cancel();
            attack.Tick(30);
            EasyRequire(!attack.Completed && !attack.IsStriking && !attack.OwnedShots.Any()
                && !Read<bool>(_boss, "_animalWindup"), "Cancelled source-cut left an active attack or windup.");
            await EasyPhysicsFrames(12);
            EasyRequire(Panels() == before, "Cancelled source-cut removed a panel after cancellation.");
            _easyAttack = null;
        }
        PrepareSourceCutPanels();
        _sourceCutMiss = false;
        _player.GlobalPosition = _easyStart = EasyStarts[0];
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        bool cycle = _boss.UsesTechniqueCycle;
        int Cursor() => cycle ? danmaku.CycleSlot : danmaku.EasySlot;
        int segment = cycle ? BossAttackCycle.Segment(_boss) : phase;
        var slots = cycle ? BossAttackCycle.Slots(_boss, segment) : BossAttackCycle.EasySlots(_boss, segment);
        int targetSlot = slots.ToList().FindIndex(slot => slot.Kind == BossTechniqueKind.Normal && slot.Variant == 3);
        EasyRequire(targetSlot >= 0 && slots.Count(slot => slot.Kind == BossTechniqueKind.Normal && slot.Variant == 3) == 1,
            "Source-cut is missing or duplicated in the normal battle sequence.");
        var selected = await StartReiQaSlot(danmaku, cycle, targetSlot);
        EasyRequire(selected is ReiBossTechnique { SourceCutTechnique: true }, "Normal battle selection did not start source-cut.");
        selected!.SetPhysicsProcess(false);
        selected.Tick(1);
        int panelsBeforeCancel = Panels();
        selected.Cancel();
        EasyRequire(Cursor() != targetSlot && !selected.Completed, "Source-cut cancellation repeated its battle slot.");
        await EasyPhysicsFrames(2);
        EasyRequire(Panels() == panelsBeforeCancel, "Battle-slot source-cut removed a panel after cancellation.");
        danmaku.Tick(.1);
        danmaku.Tick(1);
        var retry = await RestartAfterDifferentTechnique(danmaku, cycle, targetSlot);
        EasyRequire(retry is ReiBossTechnique { SourceCutTechnique: true, Finished: false } && !ReferenceEquals(retry, selected),
            "Source-cut completion fixture did not restart after another technique.");
        bool retryEnded = false, retryCompleted = false;
        int retryPulses = 0, retryRewards = 0;
        retry!.Ended += attack =>
        {
            retryEnded = true; retryCompleted = attack.Completed;
            var source = (ReiBossTechnique)attack;
            retryPulses = source.SourceCutRounds.Sum(round => round.FiredPulses);
            retryRewards = source.SourceCutRounds.Sum(round => round.RemovedPanels);
        };
        _easyAttack = retry;
        _running = true;
        for (int frame = 0; frame < 120 * 60 && !retryEnded; frame++) await EasyPhysicsFrames(1);
        _running = false;
        _pilot.Release();
        EasyRequire(retryEnded && retryCompleted && retryPulses == (_game.Difficulty >= GameManager.Diff.Hard ? 12 : 9)
            && retryRewards > 0, "Retried battle-slot source-cut did not naturally finish all pulses with a reward.");
        int nextSlot = Cursor();
        EasyRequire(nextSlot != targetSlot, "Natural source-cut completion did not advance the battle slot.");
        await EasyPhysicsFrames(2);
        danmaku.Tick(.1);
        danmaku.Tick(1);
        var following = danmaku.ActiveTechnique;
        EasyRequire(following is { Finished: false } && following.TechniqueKind == slots[nextSlot].Kind
            && following.TechniqueVariant == slots[nextSlot].Variant, "The following battle technique did not start after source-cut.");
        following!.Cancel();
        _easyAttack = null;
        await EasyPhysicsFrames(2);
        _easyCycles.Add(new { phase, difficulty = _game.Difficulty.ToString(), kind = "Normal", variant = 3,
            audit = "source_cut_cancellation_and_battle_selection", passed = true, cancelled_stages = 3,
            panel_mutation_after_cancel = false, production_selection = true, target_slot = targetSlot,
            cancellation_advances_cursor = true, retry_after_another_technique = true, natural_completion = retryCompleted,
            fired_pulses = retryPulses, removed_panels = retryRewards, next_slot = nextSlot,
            next_kind = slots[nextSlot].Kind.ToString(), next_variant = slots[nextSlot].Variant,
            following_technique_started = true });
        GD.Print($"[REI-SOURCE-CUT] PASS phase={phase} difficulty={_game.Difficulty} input_cases=15 cancel_stages=3");
    }

    private async Task RunReiRallyQa(int phase)
    {
        const double step = 1d / 60;
        var rally = new BossAttackCycle.Slot(BossTechniqueKind.Normal, 4);
        var job = _game.SelectedJob;
        _game.SelectedJob = job == Job.Tank ? Job.Melee : Job.Tank;
        _game.TrainingSetUpgrade("n_charge_shot", true);
        _game.SelectedJob = job;
        _game.TrainingSetUpgrade("n_charge_shot", false);
        _game.TrainingSetUpgrade("n_charge", true);
        var locked = Enumerable.Range(0, 3).Select(segment => BossAttackCycle.Slots(_boss, segment)).ToArray();
        EasyRequire(locked.All(slots => !slots.Contains(rally))
            && Enumerable.Range(0, 2).All(form => !BossAttackCycle.EasySlots(_boss, form).Contains(rally)),
            "Another character's base charge or the second tier alone unlocked wall rally.");
        _game.TrainingSetUpgrade("n_charge", false);
        _game.TrainingSetImpression(GameManager.GetUpgradeDef("n_charge_shot")!.BaseCost);
        EasyRequire(_game.TryPurchase("n_charge_shot"), "The active character could not buy base charge.");
        for (int segment = 0; segment < locked.Length; segment++)
        {
            var owned = BossAttackCycle.Slots(_boss, segment);
            EasyRequire(owned.Count(slot => slot == rally) == 1 && locked[segment].SequenceEqual(owned.Where(slot => slot != rally)),
                "Purchasing base charge should add only wall rally in each Rei segment.");
            if (segment < 2) EasyRequire(BossAttackCycle.EasySlots(_boss, segment).SequenceEqual(owned),
                "Easy selection did not use the active character's charge purchase.");
        }
        GD.Print($"[REI-RALLY] ELIGIBILITY PASS job={job} difficulty={_game.Difficulty} segments={locked.Length}");
        _running = false;
        _pilot.Release();
        int count = (int)_game.Difficulty + 1, initialHp = 0, lockChecks = 0;
        int[] reflections = new int[4];
        Rect2? savedBounds = null;
        Node2D? savedLock = null;
        (Panel Panel, bool Visible, bool Invulnerable)[] savedPanels = Array.Empty<(Panel, bool, bool)>();
        Bullet[] PlayerShots() => _pool.GetChildren().OfType<Bullet>().Where(shot => shot.Active && !shot.IsEnemy).ToArray();
        bool Contains(Rect2 rect, Vector2 at) => at.X >= rect.Position.X - .001f && at.X <= rect.End.X + .001f
            && at.Y >= rect.Position.Y - .001f && at.Y <= rect.End.Y + .001f;
        void Setup(Vector2 start)
        {
            _pool.DespawnAll();
            PrepareSourceCutPanels();
            Write(_boss, "_hp", _boss.BarHp * _boss.TotalBars);
            initialHp = Read<int>(_boss, "_hp");
            _player.GlobalPosition = _easyStart = start;
            savedBounds = _player.RallyMovementBounds;
            savedLock = _player.RallyLockTarget;
            Write(_player, "_chargeT", 0f);
            Write(_player, "_chargeHeld", false);
            Write(_player, "_invincible", true);
            Write(_player, "_invincibleTimer", 99999f);
            savedPanels = Read<List<Panel>>(_boss, "_panels").Select(panel => (panel, panel.Visible, panel.Invulnerable)).ToArray();
        }
        ReiBossTechnique BeginFixture(Vector2? start = null)
        {
            Setup(start ?? EasyStarts[0]);
            var attack = (ReiBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Normal, 4);
            attack.SetPhysicsProcess(false);
            _easyAttack = attack;
            attack.Tick(step);
            EasyRequire(attack.RallyBalls.Count(ball => !ball.IsDanger) == count
                && attack.RallyBalls.Count(ball => ball.IsDanger) == count && _boss.RallyGuarded,
                "Rally did not create the difficulty's good/danger ball mix and boss guard.");
            EasyRequire(savedPanels.All(saved => saved.Panel.IsVisibleInTree() && saved.Panel.Invulnerable)
                && attack.RallyBalls.All(ball => ball.Node.RallyDriven), "Rally did not isolate its dedicated balls.");
            EasyRequire(!attack.EasyCues.Any(), "Wall rally retained an obsolete path warning.");
            return attack;
        }
        Vector2 SafePosition(ReiBossTechnique attack)
        {
            Rect2 bounds = attack.RallyPlayerBounds;
            var moving = attack.RallyBalls.Where(ball => ball.Stage is ReiBossTechnique.RallyStage.Incoming
                or ReiBossTechnique.RallyStage.Returning).ToArray();
            return Enumerable.Range(0, 43).Select(index => new Vector2(bounds.Position.X,
                Mathf.Lerp(bounds.Position.Y, bounds.End.Y, index / 42f)))
                .MaxBy(at => moving.Select(ball => Geometry2D.GetClosestPointToSegment(at, ball.Position,
                    ball.Position + ball.Velocity * (float)step).DistanceSquaredTo(at)).DefaultIfEmpty(float.MaxValue).Min());
        }
        void ChargeAt(ReiBossTechnique.RallyBall ball, Vector2 origin)
        {
            _player.GlobalPosition = origin;
            _player.RallyLockTarget = ball.Node;
            Call(_player, "FireCharge", ChargeTier.First);
            var volley = PlayerShots().Where(shot => shot.ChargeVolleyId == _player.ChargeVolleyId).ToArray();
            EasyRequire(volley.Length > 0 && volley.All(shot => shot.Charged && shot.ChargeOrigin == origin),
                "Rally fixture did not create a real charged volley.");
            foreach (var shot in volley) shot.SetPhysicsProcess(false);
        }
        void CheckLock(ReiBossTechnique attack)
        {
            if (attack.Finished || attack.RallyBalls.Any(ball => ball.Stage == ReiBossTechnique.RallyStage.Closing)) return;
            var next = attack.RallyBalls.Where(ball => !ball.IsDanger && ball.Revealed
                && ball.Stage == ReiBossTechnique.RallyStage.Incoming && ball.Velocity.X < 0).MinBy(ball => ball.Position.X);
            EasyRequire(_player.RallyLockTarget == next?.Node, "Rally lock chose a danger, unrevealed, departing or later ball.");
            if (next != null) lockChecks++;
        }
        var retaliationWarnings = new Dictionary<ulong, double>();
        var revealedBalls = new Dictionary<(ulong, int), double>();
        async Task TickFixture(ReiBossTechnique attack, List<object>? events = null)
        {
            var before = attack.RallyBalls.Select(ball => (ball.Stage, ball.Position, ball.Velocity, ball.Returns,
                ball.LeftBounces, ball.RightBounces, ball.TopBounces, ball.BottomBounces)).ToArray();
            int previousStreak = attack.RallyStreak, previousFailures = attack.RallyFailures;
            foreach (var shot in PlayerShots()) { shot.SetPhysicsProcess(false); shot._PhysicsProcess(step); }
            attack.Tick(step);
            if (attack.PracticeHazards.Any(h => h.Shape == 2 && !h.Live))
                retaliationWarnings.TryAdd(attack.GetInstanceId(), attack.Elapsed);
            if (attack.PracticeHazards.Any(h => h.Shape == 2 && h.Live))
            {
                EasyRequire(retaliationWarnings.TryGetValue(attack.GetInstanceId(), out double warned)
                    && attack.Elapsed - warned >= 2.15, "Miss retaliation lacked its visible warning.");
                if (_game.Difficulty == GameManager.Diff.Hard)
                {
                    var bands = attack.PracticeHazards.Where(h => h.Shape == 2 && h.Live).OrderBy(h => h.A.Y).ToArray();
                    EasyRequire(bands.Length == 2 && bands[1].A.Y - bands[0].B.Y >= _player.CurrentHitRadius * 3,
                        "Hard rally retaliation closed its return lane.");
                    var center = new Vector2(attack.RallyPlayerBounds.GetCenter().X, (bands[0].B.Y + bands[1].A.Y) * .5f);
                    EasyRequire(attack.RallyPlayerBounds.HasPoint(center) && attack.DangerDistance(center, false) > _player.CurrentHitRadius * .5f,
                        "Hard rally lane was outside the player strip or touched the clearance probe.");
                }
                else EasyRequire(new[] { Field.Rect.Position, new Vector2(Field.Right, Field.Top), Field.Rect.End,
                    new Vector2(Field.Left, Field.Bottom), new Vector2(Field.CenterX, Field.CenterY) }.All(attack.CoversPoint),
                    "Lunatic miss retaliation left an edge escape.");
            }
            foreach (var ball in attack.RallyBalls)
            {
                var old = before[ball.Index];
                if (ball.Revealed && ball.Stage == ReiBossTechnique.RallyStage.Feint)
                    revealedBalls.TryAdd((attack.GetInstanceId(), ball.Index), attack.Elapsed);
                if (_game.Difficulty == GameManager.Diff.Easy && old.Stage == ReiBossTechnique.RallyStage.Feint
                    && ball.Stage == ReiBossTechnique.RallyStage.Incoming)
                    EasyRequire(revealedBalls.TryGetValue((attack.GetInstanceId(), ball.Index), out double revealed)
                        && attack.Elapsed - revealed >= 2, "Easy rally launched before two seconds of visible ball identification.");
                int[] changed = { ball.LeftBounces - old.LeftBounces, ball.RightBounces - old.RightBounces,
                    ball.TopBounces - old.TopBounces, ball.BottomBounces - old.BottomBounces };
                if (changed.Any(value => value > 0) && ball.Stage != ReiBossTechnique.RallyStage.Closing)
                {
                    Vector2 expected = old.Position + old.Velocity * (float)step;
                    if (changed[0] > 0) expected.X = 2 * (Field.Left + ReiBossTechnique.RallyRadius) - expected.X;
                    if (changed[1] > 0) expected.X = 2 * (Field.Right - ReiBossTechnique.RallyRadius) - expected.X;
                    if (changed[2] > 0) expected.Y = 2 * (Field.Top + ReiBossTechnique.RallyRadius) - expected.Y;
                    if (changed[3] > 0) expected.Y = 2 * (Field.Bottom - ReiBossTechnique.RallyRadius) - expected.Y;
                    EasyRequire(ball.Position.DistanceTo(expected) < .02f, "Rally reflection did not preserve boundary overshoot.");
                    EasyRequire((changed[0] == 0 || ball.Velocity.X > 0) && (changed[1] == 0 || ball.Velocity.X < 0)
                        && (ball.Stage == ReiBossTechnique.RallyStage.Feint
                            || (changed[2] == 0 || ball.Velocity.Y > 0) && (changed[3] == 0 || ball.Velocity.Y < 0)),
                        "Rally reflection points through the boundary.");
                    for (int i = 0; i < 4; i++) reflections[i] += changed[i];
                    events?.Add(new { at = attack.Elapsed, type = "reflection", ball.Index, ball.IsDanger,
                        left = changed[0], right = changed[1], top = changed[2], bottom = changed[3] });
                }
                if (ball.Returns > old.Returns)
                {
                    EasyRequire(!ball.IsDanger && old.Stage == ReiBossTechnique.RallyStage.Returning
                        && ball.Returns == old.Returns + 1 && old.Velocity.X > 0
                        && Math.Abs(ball.Position.X - attack.RallyWallX) < .02f,
                        "Rally credited a wall hit before a good returning ball reached the wall.");
                    events?.Add(new { at = attack.Elapsed, type = "wall_hit", ball.Index, ball.Returns,
                        streak = attack.RallyStreak, failures = attack.RallyFailures });
                }
                if (old.Stage == ReiBossTechnique.RallyStage.Incoming && ball.Stage == ReiBossTechnique.RallyStage.Returning)
                    events?.Add(new { at = attack.Elapsed, type = "charge_hit", ball.Index, ball.IsDanger,
                        x = ball.Position.X, y = ball.Position.Y, vx = ball.Velocity.X, vy = ball.Velocity.Y });
            }
            if (attack.RallyFailures > previousFailures)
            {
                int sameFrameHits = attack.RallyBalls.Sum(ball => ball.Returns - before[ball.Index].Returns);
                EasyRequire(attack.RallyStreak <= sameFrameHits, "Rally miss retained wall hits from before the miss.");
                events?.Add(new { at = attack.Elapsed, type = "miss", previous_streak = previousStreak,
                    streak = attack.RallyStreak, failures = attack.RallyFailures, same_frame_wall_hits = sameFrameHits });
            }
            int reward = Math.Max(1, Mathf.RoundToInt(initialHp * .04f));
            EasyRequire(attack.RallyDamageEvents is 0 or 1 && Read<int>(_boss, "_hp") == initialHp - attack.RallyDamageEvents * reward
                && (attack.RallyDamageEvents == 0 || attack.RallyWallBroken && attack.RallyStreak == 5),
                "Rally damage differed from one four-percent reward after five consecutive wall hits.");
            CheckLock(attack);
            await EasyPhysicsFrames(1);
        }
        async Task Cleanup(ReiBossTechnique attack)
        {
            EasyRequire(!_boss.RallyGuarded && _player.RallyMovementBounds == savedBounds
                && _player.RallyLockTarget == savedLock && !attack.IsStriking && !Read<bool>(_boss, "_animalWindup"),
                "Rally left movement bounds, guard, target or attack state behind.");
            EasyRequire(savedPanels.All(saved => IsInstanceValid(saved.Panel) && saved.Panel.Visible == saved.Visible
                && saved.Panel.Invulnerable == saved.Invulnerable), "Rally did not restore original panels.");
            await EasyPhysicsFrames(2);
            EasyRequire(attack.RallyBalls.All(ball => !IsInstanceValid(ball.Node)), "Rally left dedicated panels behind.");
            _pool.DespawnAll();
            _easyAttack = null;
        }
        object BallResult(ReiBossTechnique.RallyBall ball) => new { ball.Index, ball.IsDanger, ball.Attempts,
            ball.Returns, ball.DamageEvents, ball.LeftBounces, ball.RightBounces, ball.TopBounces, ball.BottomBounces };
        void Record(ReiBossTechnique attack, string mode, object evidence)
        {
            var result = new { character = _scene, difficulty = _game.Difficulty.ToString(), phase, kind = "Normal", variant = 4,
                audit = "rally_wall_state", mode, passed = true, fixture_positioning = true, normal_movement_test = false,
                human_comprehension_test = false, initial_hp = initialHp, remaining_hp = Read<int>(_boss, "_hp"),
                failures = attack.RallyFailures, streak = attack.RallyStreak, wall_broken = attack.RallyWallBroken,
                damage_events = attack.RallyDamageEvents, miss_retaliations = attack.RallyMissRetaliations,
                balls = attack.RallyBalls.Select(BallResult).ToArray(), evidence };
            _easyCases.Add(result);
            GD.Print("[REI-RALLY] STATE " + JsonSerializer.Serialize(result));
        }
        if (_game.Difficulty == GameManager.Diff.Hard)
        {
            var attack = BeginFixture();
            var ball = attack.RallyBalls.First(b => b.IsDanger);
            ball.Stage = ReiBossTechnique.RallyStage.Incoming;
            ball.Revealed = true;
            ball.Position = new Vector2(Field.Left + 25, Field.CenterY);
            ball.Velocity = Vector2.Right * 60;
            foreach (float margin in new[] { .1f, -.1f })
            {
                _player.GlobalPosition = ball.Position + Vector2.Down * (ReiBossTechnique.RallyRadius + _player.CurrentHitRadius + margin);
                await TickFixture(attack);
                EasyRequire(attack.CoversPoint(_player.GlobalPosition) == (margin < 0) && attack.RallyFailures == (margin < 0 ? 1 : 0),
                    "Rally miss counter and physical hit disagreed at the player marker.");
            }
            attack.Cancel();
            Record(attack, "marker_contact", new { radius = _player.CurrentHitRadius });
            await Cleanup(attack);
        }
        foreach (string edge in new[] { "left", "right", "top", "bottom" })
        {
            var attack = BeginFixture();
            var ball = attack.RallyBalls.First(ball => ball.IsDanger);
            ball.Stage = ReiBossTechnique.RallyStage.Incoming;
            ball.Revealed = true;
            ball.Position = edge switch {
                "left" => new Vector2(Field.Left + ReiBossTechnique.RallyRadius + .5f, Field.CenterY),
                "right" => new Vector2(Field.Right - ReiBossTechnique.RallyRadius - .5f, Field.CenterY),
                "top" => new Vector2((Field.Left + Field.Right) / 2, Field.Top + ReiBossTechnique.RallyRadius + .5f),
                _ => new Vector2((Field.Left + Field.Right) / 2, Field.Bottom - ReiBossTechnique.RallyRadius - .5f) };
            ball.Velocity = edge switch { "left" => Vector2.Left * 60, "right" => Vector2.Right * 60,
                "top" => Vector2.Up * 60, _ => Vector2.Down * 60 };
            _player.GlobalPosition = SafePosition(attack);
            var events = new List<object>();
            await TickFixture(attack, events);
            EasyRequire(ball.LeftBounces + ball.RightBounces + ball.TopBounces + ball.BottomBounces == 1,
                "Rally boundary fixture did not produce exactly one physical reflection.");
            attack.Cancel();
            Record(attack, "reflection_" + edge, new { ball_fixture_positioning = true, events });
            await Cleanup(attack);
        }
        foreach (var start in new[] { EasyStarts[0], new Vector2(Field.Right - 4, Field.CenterY), new Vector2(Field.Left + 4, Field.Top + 4) })
        {
            var attack = BeginFixture(start);
            float lastWidth = attack.RallyPlayerBounds.Size.X, forcedDistance = 0;
            for (int frame = 0; frame < 150; frame++)
            {
                await TickFixture(attack);
                Vector2 before = _player.GlobalPosition;
                AutoplayInput.Move(Vector2.Right);
                Input.FlushBufferedEvents();
                _player._PhysicsProcess(step);
                Rect2 bounds = attack.RallyPlayerBounds;
                EasyRequire(bounds.Size.X <= lastWidth + .001f && Contains(bounds, _player.GlobalPosition),
                    "Rally formation failed to clamp rightward input inside its shrinking bounds.");
                forcedDistance += Math.Max(0, before.X - _player.GlobalPosition.X);
                lastWidth = bounds.Size.X;
            }
            _pilot.Release();
            EasyRequire(lastWidth > 40 && lastWidth <= 50 && Math.Abs(attack.RallyPlayerBounds.End.X - (Field.Left + 48)) < .001f,
                "Rally did not form the left-side movement strip.");
            int hp = Read<int>(_boss, "_hp");
            attack.Cancel();
            attack.Tick(30);
            EasyRequire(!attack.Completed && Read<int>(_boss, "_hp") == hp, "Cancelled formation paid delayed damage.");
            Record(attack, "movement_bounds", new { start_x = start.X, start_y = start.Y, width = lastWidth, forced_distance = forcedDistance });
            await Cleanup(attack);
        }
        foreach (string mode in new[] { "success", "reset_then_success", "three_misses" })
        {
            var attack = BeginFixture();
            var fired = new HashSet<(int Index, int Attempt)>();
            var events = new List<object>();
            bool waitingForMiss = false, resetObserved = false;
            int chargedHits = 0;
            for (int frame = 0; frame < 240 * 60 && !attack.Finished; frame++)
            {
                _player.GlobalPosition = SafePosition(attack);
                if (mode == "reset_then_success" && !resetObserved && attack.RallyStreak >= 2) waitingForMiss = true;
                int previousFailures = attack.RallyFailures, previousStreak = attack.RallyStreak;
                var ball = attack.RallyBalls.Where(ball => ball.ReturnEligible && !fired.Contains((ball.Index, ball.Attempts)))
                    .MinBy(ball => ball.Position.X);
                if (mode != "three_misses" && !waitingForMiss && ball != null)
                {
                    var origin = new Vector2(attack.RallyPlayerBounds.Position.X,
                        Mathf.Clamp(ball.Position.Y, attack.RallyPlayerBounds.Position.Y, attack.RallyPlayerBounds.End.Y));
                    if (attack.RallyBalls.Where(other => other.IsDanger && other.Stage == ReiBossTechnique.RallyStage.Incoming)
                        .All(other => Geometry2D.GetClosestPointToSegment(other.Position, origin, ball.Position)
                            .DistanceTo(other.Position) > ReiBossTechnique.RallyRadius
                                + Jobs.Get(_game.SelectedJob).ChargeRadius * ChargeTier.RadiusMulFor(ChargeTier.First) + 2))
                    {
                        ChargeAt(ball, origin);
                        fired.Add((ball.Index, ball.Attempts));
                        chargedHits++;
                        events.Add(new { at = attack.Elapsed, type = "charge_fired", ball.Index, ball.Attempts,
                            x = ball.Position.X, y = ball.Position.Y, origin_x = origin.X, origin_y = origin.Y });
                    }
                }
                await TickFixture(attack, events);
                if (waitingForMiss && attack.RallyFailures > previousFailures)
                {
                    EasyRequire(previousStreak >= 2, "Recovery fixture missed before building a consecutive wall streak.");
                    resetObserved = true;
                    waitingForMiss = false;
                }
            }
            GD.Print("[REI-RALLY] FIXTURE " + JsonSerializer.Serialize(new { phase, mode, finished = attack.Finished,
                completed = attack.Completed, failures = attack.RallyFailures, streak = attack.RallyStreak,
                wall_broken = attack.RallyWallBroken, damage_events = attack.RallyDamageEvents,
                fired_volleys = chargedHits, reset_observed = resetObserved, balls = attack.RallyBalls.Select(BallResult).ToArray(), events }));
            EasyRequire(attack.Finished && attack.Completed && attack.NaturalEndRemainingShots == 0
                && attack.NaturalEndLiveHazards == 0, "Wall rally did not finish naturally and cleanly.");
            if (mode == "three_misses")
                EasyRequire(attack.RallyFailures == 3 && !attack.RallyWallBroken && attack.RallyDamageEvents == 0
                    && attack.RallyMissRetaliations == ((int)_game.Difficulty >= 2 ? 1 : 0),
                    "Three missed returns did not end the wall rally without reward.");
            else
                EasyRequire(attack.RallyWallBroken && attack.RallyStreak == 5 && attack.RallyDamageEvents == 1
                    && attack.RallyMissRetaliations == 0
                    && (mode == "success" ? attack.RallyFailures == 0 : resetObserved
                        && attack.RallyFailures > 0 && attack.RallyFailures < ReiBossTechnique.RallyFailureLimit),
                    "Real charges did not break the wall after five uninterrupted hits.");
            Record(attack, mode, new { fired_volleys = chargedHits, reset_observed = resetObserved, events });
            await Cleanup(attack);
        }
        foreach (string mode in new[] { "ordinary_good", "ordinary_danger", "charged_danger", "wrong_side" })
        {
            var attack = BeginFixture();
            bool danger = mode is "ordinary_danger" or "charged_danger", checkedContact = false;
            var events = new List<object>();
            for (int frame = 0; frame < 30 * 60 && !checkedContact; frame++)
            {
                _player.GlobalPosition = SafePosition(attack);
                var ball = attack.RallyBalls.FirstOrDefault(ball => ball.IsDanger == danger
                    && ball.Stage == ReiBossTechnique.RallyStage.Incoming && (mode != "wrong_side" || ball.ReturnEligible));
                if (ball == null) { await TickFixture(attack); continue; }
                int failures = attack.RallyFailures, returns = ball.Returns;
                if (mode.StartsWith("ordinary", StringComparison.Ordinal))
                {
                    var normal = _pool.Spawn(ball.Position, Vector2.Zero, false, 3, 100);
                    normal.SetPhysicsProcess(false);
                    await TickFixture(attack);
                    EasyRequire(attack.RallyFailures == failures && ball.Stage == ReiBossTechnique.RallyStage.Incoming
                        && ball.Returns == returns && attack.RallyStreak == 0, "Ordinary fire changed a rally ball or wall progress.");
                    _pool.Despawn(normal);
                }
                else
                {
                    Vector2 origin = ball.Position + Vector2.Right * (mode == "wrong_side" ? 38 : -38);
                    bool clearOrigin = attack.RallyBalls.Where(other => other != ball
                        && other.Stage == ReiBossTechnique.RallyStage.Incoming).All(other =>
                            Geometry2D.GetClosestPointToSegment(origin, other.Position, other.Position + other.Velocity * (float)step)
                                .DistanceTo(origin) > ReiBossTechnique.RallyRadius + Player.HitRadius + 2);
                    if (!clearOrigin || mode == "charged_danger" && !Contains(attack.RallyPlayerBounds, origin))
                    {
                        await TickFixture(attack, events);
                        continue;
                    }
                    ChargeAt(ball, origin);
                    events.Add(new { at = attack.Elapsed, type = "charge_fired", ball.Index, ball.IsDanger,
                        x = ball.Position.X, y = ball.Position.Y, origin_x = origin.X, origin_y = origin.Y });
                    for (int hit = 0; hit < 30 && attack.RallyFailures == failures; hit++)
                    {
                        await TickFixture(attack, events);
                        _player.GlobalPosition = SafePosition(attack);
                    }
                    GD.Print("[REI-RALLY] CONTACT " + JsonSerializer.Serialize(new { phase, mode,
                        failures_before = failures, failures = attack.RallyFailures, stage = ball.Stage.ToString(),
                        ball.Returns, streak = attack.RallyStreak, events }));
                    EasyRequire(attack.RallyFailures == failures + 1 && ball.Returns == returns
                        && ball.Stage != ReiBossTechnique.RallyStage.Returning
                        && attack.RallyStreak == 0 && attack.RallyDamageEvents == 0,
                        "Danger/wrong-side charge did not cause exactly one miss without wall credit.");
                    foreach (var other in attack.RallyBalls.Where(other => other != ball))
                        other.Stage = ReiBossTechnique.RallyStage.Done;
                    for (int hold = 0; hold < 6; hold++) { _player.GlobalPosition = SafePosition(attack); await TickFixture(attack); }
                    EasyRequire(attack.RallyFailures == failures + 1, "One rejected charge produced repeated miss events.");
                }
                checkedContact = true;
            }
            EasyRequire(checkedContact, "Rally contact fixture never reached its target ball.");
            attack.Cancel();
            Record(attack, mode, new { actual_bullet_contact = true, events });
            await Cleanup(attack);
        }
        foreach (var stage in new[] { ReiBossTechnique.RallyStage.Gather, ReiBossTechnique.RallyStage.Incoming,
            ReiBossTechnique.RallyStage.Returning })
        {
            var attack = BeginFixture();
            var fired = new HashSet<(int, int)>();
            for (int frame = 0; frame < 60 * 60 && !attack.RallyBalls.Any(ball => ball.Stage == stage); frame++)
            {
                _player.GlobalPosition = SafePosition(attack);
                var ball = attack.RallyBalls.FirstOrDefault(ball => ball.ReturnEligible && !fired.Contains((ball.Index, ball.Attempts)));
                if (stage == ReiBossTechnique.RallyStage.Returning && ball != null)
                {
                    ChargeAt(ball, new Vector2(attack.RallyPlayerBounds.Position.X, ball.Position.Y));
                    fired.Add((ball.Index, ball.Attempts));
                }
                await TickFixture(attack);
            }
            EasyRequire(attack.RallyBalls.Any(ball => ball.Stage == stage), "Rally did not reach the cancellation state.");
            int hp = Read<int>(_boss, "_hp");
            attack.Cancel();
            attack.Tick(30);
            EasyRequire(!attack.Completed && Read<int>(_boss, "_hp") == hp, "Cancelled wall rally paid delayed damage.");
            Record(attack, "cancel_" + stage, new { bounds_restored = _player.RallyMovementBounds == savedBounds });
            await Cleanup(attack);
        }
        EasyRequire(reflections.All(total => total > 0) && lockChecks > 0, "Rally audit did not observe all four real boundary reflections and an eligible lock.");
        Setup(EasyStarts[0]);
        var danmaku = Read<BossDanmaku>(_boss, "_danmaku");
        bool cycle = _boss.UsesTechniqueCycle;
        int Cursor() => cycle ? danmaku.CycleSlot : danmaku.EasySlot;
        var slots = await ReadyReiQaCycle(danmaku, cycle);
        int targetSlot = slots.ToList().FindIndex(slot => slot.Kind == BossTechniqueKind.Normal && slot.Variant == 4);
        EasyRequire(targetSlot >= 0 && slots.Count(slot => slot.Kind == BossTechniqueKind.Normal && slot.Variant == 4) == 1,
            "Wall rally is missing or duplicated in normal selection.");
        int historyOrder = Read<int>(Read<object>(danmaku, "_history"), "_order");
        Write(danmaku, cycle ? "_cycleSlot" : "_easySlot", targetSlot);
        Write(danmaku, "_timer", 0d);
        for (int frame = 0; frame < 180 && danmaku.ActiveTechnique is not { Finished: false }; frame++)
        {
            _boss.TickBossFrame(1d / 60);
            danmaku.Tick(1d / 60);
            await EasyPhysicsFrames(1);
        }
        var selected = danmaku.ActiveTechnique as ReiBossTechnique;
        EasyRequire(selected is { RallyTechnique: true }, $"Normal selection did not start wall rally: phase={phase}, slot={Cursor()}/{targetSlot}, selected={selected?.SpellName}, center={_boss.ShotCenter}, right={Field.Right}, battle={_boss.GaugeAttackResolving}, returning={_boss.BossFrameReturning}, reforming={_boss.GaugeReforming}, bubble={Hud.BubblePaused}, paused={GetTree().Paused}.");
        selected!.SetPhysicsProcess(false);
        selected.Tick(1);
        selected.Cancel();
        EasyRequire(Cursor() != targetSlot && !selected.Completed, "Cancelling wall rally repeated its slot.");
        await Cleanup(selected);
        danmaku.Tick(.1);
        danmaku.Tick(1);
        var retry = (ReiBossTechnique)await RestartAfterDifferentTechnique(danmaku, cycle, targetSlot);
        EasyRequire(retry is { RallyTechnique: true, Finished: false } && !ReferenceEquals(retry, selected), "Wall rally did not restart after another technique.");
        _easyAttack = retry;
        _easyStationary = false;
        Vector2 previousPosition = _player.GlobalPosition;
        float maximumStep = 0, formationDisplacement = 0;
        int contacts = 0, bodyContacts = 0, chargeHeldFrames = 0, settledFrames = 0;
        ulong firstVolley = _player.ChargeVolleyId;
        bool ended = false, completed = false;
        retry!.Ended += attack => { ended = true; completed = attack.Completed; };
        _running = true;
        for (int frame = 0; frame < 300 * 60 && !ended; frame++)
        {
            await EasyPhysicsFrames(1);
            Vector2 position = _player.GlobalPosition;
            if (!ended && retry.RallyPlayerBounds.End.X <= Field.Left + 48 + .001f) settledFrames++;
            if (settledFrames >= 2)
            {
                maximumStep = Math.Max(maximumStep, position.DistanceTo(previousPosition));
                EasyRequire(ended || Contains(retry.RallyPlayerBounds, position),
                    $"Normal input escaped the formed left strip: t={retry.Elapsed} at={position} bounds={retry.RallyPlayerBounds} settled_frames={settledFrames}.");
            }
            else formationDisplacement += position.DistanceTo(previousPosition);
            previousPosition = position;
            if (!ended && retry.CoversPoint(position)) contacts++;
            if (EasyBodyAt(position)) bodyContacts++;
            if (Input.IsKeyPressed(Key.Z)) chargeHeldFrames++;
        }
        _running = false;
        _pilot.Release();
        GD.Print($"[REI-RALLY] INPUT phase={phase} ended={ended} completed={completed} failures={retry.RallyFailures} streak={retry.RallyStreak} wall={retry.RallyWallBroken} max_step={maximumStep} contacts={contacts} body_contacts={bodyContacts}");
        EasyRequire(ended && completed && retry.RallyFailures < ReiBossTechnique.RallyFailureLimit && retry.RallyWallBroken
            && retry.RallyStreak == 5 && retry.RallyDamageEvents == 1, "Normal charge input did not break the wall with five consecutive hits.");
        EasyRequire(chargeHeldFrames > 0 && _player.ChargeVolleyId > firstVolley
            && maximumStep <= _player.MoveSpeed / 60 + .01f, "Rally input bypassed charge or normal movement speed.");
        EasyRequire(Read<int>(_boss, "_hp") == initialHp - Math.Max(1, Mathf.RoundToInt(initialHp * .04f)), "Normal input paid an incorrect wall-break reward.");
        int nextSlot = Cursor();
        EasyRequire(nextSlot >= 0 && nextSlot < slots.Count && nextSlot != targetSlot
            && Read<int>(Read<object>(danmaku, "_history"), "_order") == historyOrder + 1,
            "Wall rally completion did not record one completion and select a different eligible attack.");
        await Cleanup(retry);
        danmaku.Tick(.1);
        danmaku.Tick(1);
        var following = danmaku.ActiveTechnique;
        EasyRequire(following is { Finished: false } && following.TechniqueKind == slots[nextSlot].Kind
            && following.TechniqueVariant == slots[nextSlot].Variant, "Following technique did not start after wall rally.");
        following!.Cancel();
        await EasyPhysicsFrames(2);
        _easyCycles.Add(new { character = _scene, difficulty = _game.Difficulty.ToString(), phase, kind = "Normal", variant = 4,
            audit = "rally_wall_actual_input_and_selection", passed = true, fixture_positioning = false, invincible = true,
            target_slot = targetSlot, next_slot = nextSlot, retry_after_another_technique = true, natural_completion = completed,
            following_technique_started = true, maximum_step = maximumStep, formation_displacement = formationDisplacement,
            charge_held_frames = chargeHeldFrames, charge_volleys = _player.ChargeVolleyId - firstVolley,
            attack_contact_frames = contacts, body_contact_frames = bodyContacts, initial_hp = initialHp,
            remaining_hp = Read<int>(_boss, "_hp"), misses = retry.RallyFailures, streak = retry.RallyStreak, wall_broken = retry.RallyWallBroken,
            damage_events = retry.RallyDamageEvents, reflections, lock_checks = lockChecks });
    }
    private async Task RunReiCollisionQa(int phase, bool allCharacters = false)
    {
        _running = false;
        _pilot.Release();
        var graze = _player.GetNode<Area2D>("GrazeArea");
        int cases = 0;
        foreach (int generation in new[] { 0, 1, 2 })
        foreach (bool peel in new[] { false, true })
        {
            _pool.DespawnAll();
            _player.GlobalPosition = new Vector2(Field.Left + 24, 156);
            var bullet = _pool.Spawn(new Vector2(Field.Right - 24, 156), Vector2.Zero, true,
                (phase == 0 ? BulletPool.BossRadius : BulletPool.BossChargeRadius) * 1.5f * Mathf.Pow(.62f, generation));
            Job character = allCharacters ? _boss.BodyShotJob!.Value : Job.Magic;
            if (phase == 1) bullet.MakeCharged(character);
            bullet.UseBossPatternVisual(character, allCharacters ? generation : 1);
            // The native reference below retains one frame's polygons throughout its transform sweep.
            Write(bullet, "_modelAnimationRate", 0f);
            bullet.Scale = (peel ? new Vector2(2.2f, 3) : Vector2.One) * Mathf.Pow(1.1f, generation);
            await EasyPhysicsFrames(4);
            EasyRequire(bullet.Active && bullet.Visible && bullet.GetShapeOwners().All(owner => bullet.IsShapeOwnerDisabled((uint)owner)),
                "Distant Rei bullet lost its visual or retained native colliders.");
            var polygon = ((ConvexPolygonShape2D)bullet.Footprint!.Shapes[0]).Points;
            Vector2 opaque = bullet.GlobalTransform * (polygon.Aggregate(Vector2.Zero, (sum, p) => sum + p) / polygon.Length);
            using (var query = new PhysicsPointQueryParameters2D { Position = opaque, CollisionMask = 8,
                CollideWithAreas = true, CollideWithBodies = false })
                EasyRequire(_player.GetWorld2D().DirectSpaceState.IntersectPoint(query).Count == 0,
                    "Distant Rei polygon remains in the native physics world.");
            int hits = 0, grazes = 0;
            void OnHit(Area2D area) { if (area == bullet) hits++; }
            void OnGraze(Area2D area) { if (area == bullet) grazes++; }
            _player.AreaEntered += OnHit;
            graze.AreaEntered += OnGraze;
            while (_player.GlobalPosition.DistanceTo(opaque) > .01f)
            {
                _player.GlobalPosition = _player.GlobalPosition.MoveToward(opaque, 24);
                await EasyPhysicsFrames(1);
            }
            await EasyPhysicsFrames(4);
            EasyRequire(hits > 0 && grazes > 0 && _player.GetOverlappingAreas().Contains(bullet)
                && graze.GetOverlappingAreas().Contains(bullet), $"Fast entry missed native contact: phase={phase} generation={generation} peel={peel} "
                + $"hit={hits} graze={grazes} point={opaque} position={bullet.GlobalPosition} active={bullet.Active} layer={bullet.CollisionLayer} shapes={bullet.Footprint.Shapes.Length}.");
            EasyRequire(bullet.GetShapeOwners().All(owner => !bullet.IsShapeOwnerDisabled((uint)owner))
                && bullet.GetShapeOwners().Sum(owner => bullet.ShapeOwnerGetShapeCount((uint)owner)) > 0,
                "Nearby Rei bullet did not restore its physical polygons.");
            foreach (uint owner in bullet.GetShapeOwners())
                for (int part = 0; part < bullet.ShapeOwnerGetShapeCount(owner); part++)
                    EasyRequire(bullet.Footprint.Shapes.Contains(bullet.ShapeOwnerGetShape(owner, part)),
                        "Rei collision polygon was simplified.");
            var reference = new Area2D { CollisionLayer = 8, CollisionMask = 0, Monitoring = false, Monitorable = true };
            _easyRoot!.AddChild(reference);
            uint referenceOwner = reference.CreateShapeOwner(reference);
            foreach (var shape in bullet.Footprint.Shapes) reference.ShapeOwnerAddShape(referenceOwner, shape);
            reference.GlobalTransform = bullet.GlobalTransform;
            await EasyPhysicsFrames(4);
            int samples = 0;
            for (int direction = 0; direction < 8; direction++)
            for (int step = 0; step < 25; step++)
            {
                float angle = direction * Mathf.Tau / 8;
                bullet.Rotation = angle * .37f;
                bullet.GlobalPosition = new Vector2(Field.CenterX, Field.CenterY) + Vector2.FromAngle(angle) * (step % 3 - 1) * 6;
                reference.GlobalTransform = bullet.GlobalTransform;
                float speed = direction % 2 == 0 ? 6 : 24;
                _player.GlobalPosition = bullet.GlobalPosition + Vector2.FromAngle(angle) * ((step - 12) * speed);
                await EasyPhysicsFrames(1);
                var hitAreas = _player.GetOverlappingAreas();
                var grazeAreas = graze.GetOverlappingAreas();
                EasyRequire(hitAreas.Contains(bullet) == hitAreas.Contains(reference)
                    && grazeAreas.Contains(bullet) == grazeAreas.Contains(reference),
                    $"Rei exact collision mismatch phase={phase} generation={generation} peel={peel} direction={direction} step={step}.");
                samples++;
            }
            reference.QueueFree();
            bullet.GlobalPosition = new Vector2(Field.CenterX, Field.CenterY);
            bullet.Rotation = 0;
            opaque = bullet.GlobalTransform * (polygon.Aggregate(Vector2.Zero, (sum, p) => sum + p) / polygon.Length);
            _player.GlobalPosition = opaque;
            await EasyPhysicsFrames(4);
            GD.Print($"[COLLISION] phase={phase} generation={generation} peel={peel} exact_reference_samples={samples}");
            bullet.HoldBossShot();
            await EasyPhysicsFrames(4);
            EasyRequire(!_player.GetOverlappingAreas().Contains(bullet), "Held quotation still hits the player.");
            bullet.ReleaseBossShot();
            await EasyPhysicsFrames(4);
            EasyRequire(_player.GetOverlappingAreas().Contains(bullet), "Released quotation lost its physical hit.");
            _player.AreaEntered -= OnHit;
            graze.AreaEntered -= OnGraze;
            _player.GlobalPosition = new Vector2(Field.Left + 24, 156);
            await EasyPhysicsFrames(4);
            EasyRequire(bullet.GetShapeOwners().All(owner => bullet.IsShapeOwnerDisabled((uint)owner)), "Distant quotation did not sleep again.");
            _pool.Despawn(bullet);
            var reused = _pool.Spawn(new Vector2(Field.Right - 24, 156), Vector2.Zero, !peel);
            if (!peel) reused.UseBossPatternVisual(Job.Heal, 0);
            await EasyPhysicsFrames(4);
            EasyRequire(ReferenceEquals(reused, bullet), "Collision test did not reuse the pooled bullet.");
            if (!peel)
            {
                EasyRequire(reused.GetShapeOwners().All(owner => reused.IsShapeOwnerDisabled((uint)owner)),
                    "Distant pooled enemy bullet retained native colliders.");
                _player.GlobalPosition = reused.GlobalPosition;
                await EasyPhysicsFrames(4);
            }
            EasyRequire(ReferenceEquals(reused, bullet) && reused.GetShapeOwners().All(owner => !reused.IsShapeOwnerDisabled((uint)owner))
                && reused.GetShapeOwners().Sum(owner => reused.ShapeOwnerGetShapeCount((uint)owner)) > 0,
                "Pooled bullet did not restore the correct collision shapes.");
            cases++;
        }
        _pool.DespawnAll();
        GD.Print($"[EASY] REI NATIVE COLLISION phase={phase} PASS cases={cases} fast_entry=1440px/s hit=true graze=true hold_release=true reuse=true");
    }

    private sealed class EasyPeelParentAudit
    {
        internal object Path = null!;
        internal int Index;
        internal string Key = "";
        internal Bullet? Parent;
        internal ulong Instance, Activation;
        internal uint OriginalLayer, OriginalMask;
        internal float OriginalAlpha, PeeledAlpha;
        internal double BornAt = -1, PeeledAt = -1, SplitAt = -1;
        internal bool Seen, Peeled, Consumed;
        internal EasyPeelInjection? Pending;
        internal readonly List<object> Hits = new();
    }

    private sealed class EasyPeelInjection
    {
        internal Bullet Shot = null!;
        internal ulong Activation;
        internal int BeforeHits;
        internal double At;
        internal Vector2 Position;
        internal uint Layer, Mask;
        internal float Alpha;
    }

    private static bool EasyPeelSameParent(EasyPeelParentAudit parent)
        => parent.Parent is { Active: true } shot && shot.GetInstanceId() == parent.Instance && shot.ActivationId == parent.Activation;

    private Vector2 EasyPeelInteriorPoint(Bullet parent)
    {
        var candidates = parent.Footprint!.Shapes.OfType<ConvexPolygonShape2D>().Select(shape =>
            parent.GlobalTransform * (shape.Points.Aggregate(Vector2.Zero, (sum, point) => sum + point) / shape.Points.Length));
        return candidates.MaxBy(point => point.DistanceSquaredTo(_boss.GlobalPosition));
    }

    private EasyPeelInjection InjectEasyPeelHit(EasyPeelParentAudit parent, BossCharacterTechnique attack)
    {
        var source = parent.Parent!;
        int hits = Read<int>(parent.Path, "Hits");
        EasyRequire(hits < 3 && source.CollisionLayer != 0 && source.CollisionMask != 0,
            "Page became harmless before its third real projectile hit.");
        Vector2 at = EasyPeelInteriorPoint(source);
        using var core = new CircleShape2D { Radius = 2.6f };
        EasyRequire(source.Footprint!.Shapes.Any(shape => shape.Collide(source.GlobalTransform, core, new Transform2D(0, at))),
            "Injected projectile did not overlap the real transformed page footprint.");
        var shot = _pool.Spawn(at, Vector2.Zero, isEnemy: false, radius: core.Radius, damage: 1,
            shape: Jobs.Get(_game.SelectedJob).ShotShape);
        EasyRequire(shot.Active && !shot.IsEnemy && shot.IsInGroup("player_bullets"),
            "Peel boundary injection was not a real player projectile.");
        return new EasyPeelInjection { Shot = shot, Activation = shot.ActivationId, BeforeHits = hits,
            At = attack.Elapsed, Position = at, Layer = source.CollisionLayer, Mask = source.CollisionMask, Alpha = source.Modulate.A };
    }

    private static void CheckEasyPeelHit(EasyPeelParentAudit parent, BossCharacterTechnique attack)
    {
        var hit = parent.Pending!;
        EasyRequire(EasyPeelSameParent(parent), "A hit replaced or removed the parent before its fixed split.");
        var source = parent.Parent!;
        int hits = Read<int>(parent.Path, "Hits");
        EasyRequire(hits == hit.BeforeHits + 1 && (!hit.Shot.Active || hit.Shot.ActivationId != hit.Activation),
            "The production PeelEasyPage step did not consume exactly one injected player projectile.");
        bool third = hits == 3;
        if (!third)
            EasyRequire(source.CollisionLayer == parent.OriginalLayer && source.CollisionMask == parent.OriginalMask
                && Math.Abs(source.Modulate.A - parent.OriginalAlpha) < .0001f,
                "The first or second hit changed page collision or alpha.");
        else
        {
            EasyRequire(source.CollisionLayer == 0 && source.CollisionMask == 0 && source.Modulate.A < parent.OriginalAlpha - .01f,
                "The third hit failed to keep the same active page harmless and translucent.");
            parent.Peeled = true;
            parent.PeeledAt = attack.Elapsed;
            parent.PeeledAlpha = source.Modulate.A;
        }
        parent.Hits.Add(new { hit = hits, injected_at = hit.At, observed_at = attack.Elapsed,
            x = hit.Position.X, y = hit.Position.Y, layer_before = hit.Layer, mask_before = hit.Mask,
            alpha_before = hit.Alpha, layer_after = source.CollisionLayer, mask_after = source.CollisionMask,
            alpha_after = source.Modulate.A, same_parent_activation = true, injected_projectile_consumed = true });
        parent.Pending = null;
    }

    private async Task RunEasyPeelQa()
    {
        EasyRequire(_boss is BossRei && _boss.HasEvolved,
            "Peel QA requires Rei after the existing form transition.");
        int expectedParents = _game.Difficulty switch
        {
            GameManager.Diff.Easy => 10, GameManager.Diff.Normal => 24, GameManager.Diff.Hard => 28, _ => 32,
        };
        int childrenPerParent = _game.Difficulty switch
        {
            GameManager.Diff.Easy => 3, GameManager.Diff.Normal => 7, GameManager.Diff.Hard => 9, _ => 11,
        };
        _running = false;
        _pilot.Release();
        EasyRequire(!GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>().Any(a => !a.Finished)
            && !_pool.GetChildren().OfType<Bullet>().Any(b => b.Active), "Peel QA requires an empty technique boundary.");
        _boss.SetPhysicsProcess(false);
        _boss.SetProcess(false);
        Read<AreaSpellCaster>(_boss, "_caster").SetProcess(false);
        _easyStationary = true;
        _easyStart = EasyStarts[0];
        _easyDisplacement = 0;
        _player.GlobalPosition = _easyStart;
        _hud.HideSpellCard();
        _hud.HideBubble();
        Hud.BubblePaused = false;
        await EasyPhysicsFrames(2);
        _easyAttack = BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Signature);
        var attack = _easyAttack!;
        string name = attack.SpellName, key = attack.MechanicKey;
        var paths = Array.Empty<object>();
        var parents = new List<EasyPeelParentAudit>();
        var audit = new EasyAttackAudit(attack, _player, _boss, _easyStart);
        var children = new Dictionary<string, object>();
        var injections = new List<(Bullet Shot, ulong Activation)>();
        bool ended = false, completed = false;
        int shots = -1, naturalRemaining = -1, naturalHazards = -1;
        double elapsed = -1;
        attack.Ended += endedAttack =>
        {
            ended = true;
            completed = endedAttack.Completed;
            shots = endedAttack.ShotsCreated;
            naturalRemaining = endedAttack.NaturalEndRemainingShots;
            naturalHazards = endedAttack.NaturalEndLiveHazards;
            elapsed = endedAttack.Elapsed;
            audit.End(endedAttack);
        };
        _running = true;
        GD.Print($"[EASY] PEEL START {key} parents={expectedParents} expected_paths={expectedParents * (1 + childrenPerParent)} boundary_injection=true");
        for (int frame = 0; frame < 180 * 60 && !ended; frame++)
        {
            await EasyPhysicsFrames(1);
            if (ended) break;
            audit.Observe(attack, _player.GlobalPosition);
            paths = Read<System.Collections.IEnumerable>(attack, "_easyPaths").Cast<object>().ToArray();
            for (int index = 0; index < paths.Length; index++)
                if (Read<bool>(paths[index], "Peel") && !parents.Any(parent => parent.Index == index))
                    parents.Add(new EasyPeelParentAudit { Path = paths[index], Index = index, Key = Read<string>(paths[index], "Key") });
            foreach (var parent in parents)
            {
                if (parent.Pending != null) CheckEasyPeelHit(parent, attack);
                if (!parent.Seen && Read<bool>(parent.Path, "Fired") && Read<Bullet?>(parent.Path, "Shot") is { Active: true } source)
                {
                    EasyRequire(source.ActivationId == Read<ulong>(parent.Path, "Activation") && Read<int>(parent.Path, "Hits") == 0
                        && source.CollisionLayer != 0 && source.CollisionMask != 0 && !source.LeadIn,
                        "A page did not become an active unpeeled parent at its scheduled launch.");
                    parent.Seen = true;
                    parent.Parent = source;
                    parent.Instance = source.GetInstanceId();
                    parent.Activation = source.ActivationId;
                    parent.OriginalLayer = source.CollisionLayer;
                    parent.OriginalMask = source.CollisionMask;
                    parent.OriginalAlpha = source.Modulate.A;
                    parent.BornAt = attack.Elapsed;
                }
                if (EasyPeelSameParent(parent))
                {
                    if (!parent.Peeled)
                    {
                        parent.Pending = InjectEasyPeelHit(parent, attack);
                        injections.Add((parent.Pending.Shot, parent.Pending.Activation));
                    }
                    else EasyRequire(parent.Parent!.CollisionLayer == 0 && parent.Parent.CollisionMask == 0
                        && Math.Abs(parent.Parent.Modulate.A - parent.PeeledAlpha) < .0001f,
                        "A peeled parent regained collision or opacity before its fixed split.");
                }
                var siblings = paths.Where(path => Read<int>(path, "Parent") == parent.Index).ToArray();
                if (siblings.Any(path => Read<bool>(path, "Fired")))
                {
                    EasyRequire(parent.Peeled && siblings.All(path => Read<bool>(path, "Fired")) && !EasyPeelSameParent(parent),
                        "Fixed split did not consume a peeled parent and launch all siblings together.");
                    if (!parent.Consumed)
                    {
                        double planned = Read<double>(parent.Path, "ArrivesAt");
                        EasyRequire(attack.Elapsed >= planned && attack.Elapsed - planned <= 2d / 60 + .00001,
                            "Peeling changed the parent's fixed split time.");
                        parent.Consumed = true;
                        parent.SplitAt = attack.Elapsed;
                    }
                }
                foreach (var child in siblings)
                {
                    string childKey = Read<string>(child, "Key");
                    if (children.ContainsKey(childKey) || !Read<bool>(child, "Fired")) continue;
                    var shot = Read<Bullet?>(child, "Shot");
                    EasyRequire(shot is { Active: true } && shot.ActivationId == Read<ulong>(child, "Activation")
                        && !shot.LeadIn && shot.CollisionLayer != 0 && shot.CollisionMask != 0,
                        "A peeled parent's child was missing or inherited the parent's harmless state.");
                    children.Add(childKey, new { key = childKey, parent = parent.Key, actual_at = attack.Elapsed,
                        planned_at = Read<double>(child, "FiresAt"), instance = shot!.GetInstanceId(), activation = shot.ActivationId,
                        collision_layer = shot.CollisionLayer, collision_mask = shot.CollisionMask, active = true });
                }
            }
        }
        _running = false;
        _pilot.Release();
        int childCount = paths.Count(path => Read<int>(path, "Parent") >= 0);
        EasyRequire(parents.Count == expectedParents && paths.Length == expectedParents * (1 + childrenPerParent)
            && childCount == expectedParents * childrenPerParent,
            "Signature's peel plan does not contain the expected parents and physical children for this difficulty.");
        EasyRequire(ended && completed && shots == paths.Length && naturalRemaining == 0 && naturalHazards == 0,
            "Peel Signature did not emit every planned path and complete naturally with no remaining danger.");
        EasyRequire(parents.All(parent => parent.Seen && parent.Peeled && parent.Consumed && parent.Hits.Count == 3 && parent.Pending == null)
            && children.Count == childCount && injections.Count == parents.Count * 3,
            "Peel QA missed a parent, a hit, or a physical child.");
        EasyRequire(injections.All(hit => !hit.Shot.Active || hit.Shot.ActivationId != hit.Activation),
            "An injected player projectile was not consumed by the production peel step.");
        audit.Validate();
        var result = new
        {
            character = _scene, difficulty = _game.Difficulty.ToString(), phase = 1, name, mechanic = key, passed = true,
            elapsed, expected_paths = paths.Length, shots, peel_parents = parents.Count, observed_children = children.Count,
            injected_player_projectiles = injections.Count,
            natural_end_owned_before_cleanup = naturalRemaining, natural_end_live_hazards_before_cleanup = naturalHazards,
            parents = parents.Select(parent => new { key = parent.Key, path_index = parent.Index,
                instance = parent.Instance, activation = parent.Activation, active_at = parent.BornAt,
                original_layer = parent.OriginalLayer, original_mask = parent.OriginalMask, original_alpha = parent.OriginalAlpha,
                peeled_at = parent.PeeledAt, peeled_alpha = parent.PeeledAlpha, split_at = parent.SplitAt,
                same_parent_survived_third_hit = parent.Peeled, consumed_by_fixed_split = parent.Consumed, hits = parent.Hits }),
            children = children.Values,
            execution = new { direct_begin = "Signature/0", native_technique_physics = true, manual_technique_ticks = 0,
                boss_native_physics_enabled = false, automatic_player_shooting = false, invincible = true,
                injection = "BulletPool.Spawn(isEnemy:false) at transformed alpha-shape interior; zero flight velocity" },
            scope = new { real_parent_shape_overlap_verified = true, production_peel_step_consumption_verified = true,
                every_parent_and_fixed_child_split_verified = true, ordinary_shot_flight_reachability_verified = false,
                normal_battle_selection_verified = false, human_comprehension_or_difficulty_verified = false },
            audit = audit.Result(),
        };
        System.IO.File.WriteAllText(_output + "/peel.json", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        _easyAttack = null;
        await EasyPhysicsFrames(2);
        GD.Print($"[EASY] PEEL PASS parents={parents.Count} hits={injections.Count} children={children.Count} shots={shots}");
    }
}
