using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
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
        shot.UseCharacterProjectile(_game.SelectedJob);
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
        EasyRequire(_boss is BossRei && _boss.HasEvolved && _game.Difficulty == GameManager.Diff.Easy,
            "Peel QA requires EASY Rei after the existing form transition.");
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
        GD.Print($"[EASY] PEEL START {key} parents=6 expected_paths=24 boundary_injection=true");
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
                        "Fixed split did not consume a peeled parent and launch all three siblings together.");
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
        EasyRequire(parents.Count == 6 && paths.Length == 24 && childCount == 18,
            "Signature's peel plan does not contain six parents and eighteen physical children.");
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
            character = _scene, difficulty = "Easy", phase = 1, name, mechanic = key, passed = true,
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
                injection = "BulletPool.Spawn(isEnemy:false) and UseCharacterProjectile at transformed alpha-shape interior; zero flight velocity" },
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
