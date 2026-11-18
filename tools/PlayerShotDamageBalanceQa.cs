using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class PlayerShotQa
{
    private sealed record DamageHit(double Seconds, int Damage, bool Charged, int ChargeStage, bool AccelCharging);

    private static FieldInfo EnemyField(string name) => typeof(Enemy).GetField(name, Private)!;
    private static int TargetHp(Enemy target) => (int)EnemyField("_hp").GetValue(target)!;

    private partial class DamageBalanceTarget : Enemy
    {
        public override UnfolderKind UnfolderStyle => UnfolderKind.Akari;

        protected override void OnEnemyReady()
        {
            BarCount = 10000;
            PanelCount = 0;
            BodyRadius = 19f;
            BodyHalfH = 23f;
            PreTexPath = "res://char/enemy_anti_pre.png";
            AutoBank = false;
        }
    }

    private static void DamageKey(bool held)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = held });
        Input.FlushBufferedEvents();
    }

    private async Task DamagePhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task MeasureDamageBalance()
    {
        string? Arg(string prefix) => OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(prefix))?[prefix.Length..];
        string targetKind = Arg("--damage-target=") ?? "body";
        Check(targetKind is "body" or "panel", "damage target is body or panel");
        string? onlyJob = Arg("--damage-job="), onlyMode = Arg("--damage-mode=");
        int? onlyFollowers = Arg("--damage-followers=") is { } followersArg ? int.Parse(followersArg) : null;
        int? onlyDistance = Arg("--damage-distance=") is { } distanceArg ? int.Parse(distanceArg) : null;
        int linePower = int.Parse(Arg("--damage-line-power=") ?? "0");
        Check(linePower is >= 0 and <= Player.PowerLevelCap, "valid line power level");
        const double firingSeconds = 4;
        const double drainSeconds = 2;
        int hz = Engine.PhysicsTicksPerSecond;
        double dt = 1d / hz;
        _game.Difficulty = GameManager.Diff.Lunatic;
        var results = new List<object>();
        var damageByFollowers = new Dictionary<(Job, string, int), string>();
        foreach (var job in Jobs.All.Where(j => onlyJob == null || j.CharacterId == onlyJob))
        foreach (int followers in new[] { 0, 2000 }.Where(n => onlyFollowers == null || n == onlyFollowers))
        foreach (string mode in new[] { "normal", "charge1", "charge2" }.Where(m => onlyMode == null || m == onlyMode))
        foreach (int distance in new[] { 33, 90, 150, 200 }.Where(n => onlyDistance == null || n == onlyDistance))
        {
            _game.ResetPersistent();
            _game.AutoSaveEnabled = false;
            _game.Difficulty = GameManager.Diff.Lunatic;
            foreach (var stage in GameManager.Stages) Read<HashSet<string>>(_game, "_cleared").Add(stage.Id);
            _game.SelectedJob = job.Id;
            _game.AddFollowers(followers);
            _game.TrainingSetAllUpgrades(false);
            _game.TrainingSetUpgrade("n_charge_shot", true);
            _game.TrainingSetUpgrade("n_charge", true);
            DamageKey(false);
            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.Stage.SetProcess(false);
            root.Stage.SetPhysicsProcess(false);
            root.Hud.HoldBubble = false;
            root.Hud.HideBubble();
            Write(root.Hud, "_bannerTimer", 0d);
            var player = root.Player;
            player.SetPhysicsProcess(false);
            Write(player, "_invincible", true);
            Write(player, "_invincibleTimer", 100f);
            Write(player, "_fireCooldown", 0f);
            for (int i = 0; i < linePower; i++) player.ApplyPowerup(PowerKind.Line);

            var hits = new List<DamageHit>();
            ulong startFrame = 0;
            int beforeHp = 0;
            DamageHit? pending = null;
            var target = new DamageBalanceTarget { Position = new Vector2(330, 108) };
            target.AreaEntered += area =>
            {
                pending = null;
                if (area is Bullet { IsEnemy: false, Active: true } b)
                {
                    beforeHp = TargetHp(target);
                    pending = new DamageHit((Engine.GetPhysicsFrames() - startFrame) * dt, 0,
                        b.Charged, b.ChargeStage, b.AccelCharging);
                }
            };
            root.World.AddChild(target);
            EnemyField("_entering").SetValue(target, false);
            ((CollisionShape2D)EnemyField("_bodyShape").GetValue(target)!).Disabled = false;
            target.Monitorable = true;
            target.AreaEntered += _ =>
            {
                if (targetKind == "body" && pending != null && beforeHp > TargetHp(target))
                    hits.Add(pending with { Damage = beforeHp - TargetHp(target) });
            };
            Panel? panel = null;
            if (targetKind == "body")
                typeof(Enemy).GetMethod("EnterExposed", Private)!.Invoke(target, null);
            else
            {
                panel = new Panel();
                panel.Setup(target, 0, 0, 0, 100000);
                panel.BeginRally();
                int beforeInk = 0;
                DamageHit? panelPending = null;
                panel.AreaEntered += area =>
                {
                    panelPending = null;
                    if (area is Bullet { IsEnemy: false, Active: true } b)
                    {
                        beforeInk = panel.Ink;
                        panelPending = new DamageHit((Engine.GetPhysicsFrames() - startFrame) * dt, 0,
                            b.Charged, b.ChargeStage, b.AccelCharging);
                    }
                };
                target.AddChild(panel);
                panel.SetProcess(false);
                panel.SetPhysicsProcess(false);
                panel.Scale = Vector2.One;
                panel.Position = Vector2.Zero;
                panel.Invulnerable = false;
                panel.CollisionLayer = 16;
                panel.CollisionMask = 2;
                panel.Monitoring = panel.Monitorable = true;
                ((List<Panel>)EnemyField("_panels").GetValue(target)!).Add(panel);
                panel.AreaEntered += _ =>
                {
                    if (panelPending != null && beforeInk > panel.Ink)
                        hits.Add(panelPending with { Damage = beforeInk - panel.Ink });
                };
            }
            Vector2 playerPosition = target.Position - new Vector2(distance, 0);
            player.Position = playerPosition;
            Write(player, "_lockTarget", target);
            Write(player, "_locked", true);
            Write(player, "_lockArmed", true);
            Write(player, "_lockByShift", false);
            _pool.DespawnAll();
            await DamagePhysicsFrames(3);
            Check(!Hud.BubblePaused, "damage measurement is unpaused");
            hits.Clear();
            int initialHp = TargetHp(target), initialInk = panel?.Ink ?? 0;
            startFrame = Engine.GetPhysicsFrames();
            int requestedStage = mode == "charge2" ? ChargeTier.Second : ChargeTier.First;
            int volleys = 0;
            int initialFireRequests = Read<int>(player, "_shotParity");
            var projectiles = new HashSet<(ulong Id, ulong Activation)>();
            bool releaseNext = false;
            ulong priorVolley = player.ChargeVolleyId;
            float maxPositionError = 0;
            for (int frame = 0; frame < (int)(firingSeconds * hz); frame++)
            {
                DamageKey(mode != "normal" && !releaseNext);
                player._PhysicsProcess(dt);
                foreach (var bullet in Active().Where(b => !b.IsEnemy))
                    projectiles.Add((bullet.GetInstanceId(), bullet.ActivationId));
                maxPositionError = Mathf.Max(maxPositionError, player.Position.DistanceTo(playerPosition));
                player.Position = playerPosition;
                releaseNext = mode != "normal" && player.ChargeStage >= requestedStage;
                if (player.ChargeVolleyId != priorVolley) { volleys++; priorVolley = player.ChargeVolleyId; }
                await DamagePhysicsFrames(1);
            }
            int withinDamage = hits.Sum(h => h.Damage);
            DamageKey(false);
            await DamagePhysicsFrames((int)(drainSeconds * hz));
            int totalDamage = hits.Sum(h => h.Damage);
            int measuredLoss = targetKind == "body" ? initialHp - TargetHp(target) : initialInk - panel!.Ink;
            Check(totalDamage == measuredLoss && totalDamage > 0, "collision samples match actual target loss");
            Check(maxPositionError < 0.01f, "stationary player remains at requested distance");
            var damageKey = (job.Id, mode, distance);
            string damageValues = string.Join(",", hits.Select(h => $"{h.Charged}:{h.Damage}").Distinct().Order());
            if (followers == 0) damageByFollowers[damageKey] = damageValues;
            else if (damageByFollowers.TryGetValue(damageKey, out string? baseline))
                Check(damageValues == baseline, $"{job.CharacterId}/{mode}/{distance}: followers do not change hit damage");
            var row = new
            {
                character = job.CharacterId, character_name = job.CharacterName, followers, mode,
                distance, line_power = linePower, target = targetKind, firing_seconds = firingSeconds,
                drain_seconds = drainSeconds, within_window_damage = withinDamage,
                within_window_dps = withinDamage / firingSeconds, settled_damage = totalDamage,
                settled_dps = totalDamage / firingSeconds, hit_count = hits.Count,
                normal_hits = hits.Count(h => !h.Charged), charged_hits = hits.Count(h => h.Charged),
                accel_charging_hits = hits.Count(h => h.AccelCharging), charged_volleys = volleys,
                normal_fire_requests = Read<int>(player, "_shotParity") - initialFireRequests,
                projectiles_spawned = projectiles.Count,
                first_hit_seconds = hits[0].Seconds, max_position_error = maxPositionError,
                bullet_base = Mathf.Max(1, Mathf.RoundToInt(job.PowerMul)),
                charge_power = job.ChargePower,
                crit_multiplier = job.CritMult, crit_cap = job.CritCap,
                hits,
            };
            results.Add(row);
            GD.Print($"[DamageBalance] {job.CharacterId} followers={followers} {mode} distance={distance} {targetKind} loss={totalDamage} dps={totalDamage / firingSeconds:0.00} hits={hits.Count} charging_hits={hits.Count(h => h.AccelCharging)}");
            File.WriteAllText(Path.Combine(_out, "damage_balance.json"), JsonSerializer.Serialize(new
            {
                completed = false, difficulty = "Lunatic", physics_hz = hz,
                conditions = "Stationary invincible player; fixed non-attacking high-HP target; ordinary production Player physics/input, Enemy/Panel collision damage; no options, followers-as-projectiles, pierce or bombs; charge release retains normal auto-shot; settled DPS includes projectiles arriving in two-second drain. This is a stationary firing benchmark, not actual boss kill time or a guarantee of maximum possible DPS.",
                results,
            }, new JsonSerializerOptions { WriteIndented = true }));
            _pool.DespawnAll();
            root.QueueFree();
            await DamagePhysicsFrames(3);
            GetTree().CurrentScene = this;
        }
        Check(results.Count > 0, "damage balance filters selected cases");
        string path = Path.Combine(_out, "damage_balance.json");
        string json = File.ReadAllText(path).Replace("\"completed\": false", "\"completed\": true");
        File.WriteAllText(path, json);
        GD.Print($"[DamageBalance] COMPLETE {results.Count} cases: {path}");
    }
}
