using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private Vector2 _bodyPilotGoal;
    private double _bodyPilotCue = -1;
    private float _bodyOrbitDirection;
    private float _bodyOrbitAngle;
    private bool _bodyOrbitPlanned;
    private Vector2[] _bodyPursuitLoop = Array.Empty<Vector2>();
    private int _bodyPursuitWaypoint;

    private async Task RunBodyAttackQa(int phase)
    {
        var args = OS.GetCmdlineUserArgs();
        bool native = DisplayServer.GetName() != "headless";
        bool pilot = native || args.Contains("--body-pilot");
        var slot = _boss.BodyShotJob switch {
            Job.Heal => new BossAttackCycle.Slot(BossTechniqueKind.Crossfire, 0),
            Job.Tank => new BossAttackCycle.Slot(BossTechniqueKind.Extra, 1),
            _ => new BossAttackCycle.Slot(BossTechniqueKind.Normal, 5),
        };
        EasyRequire(BossAttackCycle.Slots(_boss, phase).Contains(slot), "Body attack is absent from production selection.");
        Vector2[] starts = {
            new(Field.CenterX, 108), new(Field.Left + 2, 2), new(Field.WorldRight - 2, 2),
            new(Field.Left + 2, 214), new(Field.WorldRight - 2, 214),
            new(Field.Left + 2, 108), new(Field.WorldRight - 2, 108),
            new(Field.CenterX, 2), new(Field.CenterX, 214),
        };
        int count = native ? 1 : args.Contains("--body-smoke") ? 1 : starts.Length;
        for (int start = 0; start < count; start++)
        {
            _running = false;
            _pilot.Release();
            await SettleEasyFrame();
            _boss.GlobalPosition = new(Field.BossFrameCenterX, Field.BossZoneCenterY);
            _boss.SetPanelsInvulnerable(true);
            Write(_player, "_invincible", !pilot);
            Write(_player, "_invincibleTimer", pilot ? 0f : 99999f);
            Write(_player, "_fireCooldown", 99999f);
            _easyAttack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
            _easyAttack.SetPhysicsProcess(false);
            _player.GlobalPosition = starts[start];
            _bodyPilotGoal = starts[start];
            _bodyPilotCue = -1;
            _bodyOrbitPlanned = false;
            _sevenPilotId = 0;
            _bodyOrbitDirection = start % 2 == 0 ? 1 : -1;
            int bodyContacts = 0, allContacts = 0, liveFrames = 0, lives = _player.Lives;
            double firstLive = -1;
            float maxStep = 0, playerStep = 0, distance = 0;
            Vector2 previous = _boss.GlobalPosition;
            var routes = new List<object>();
            double observedCue = -1;
            var captured = new HashSet<string>();
            async Task Capture(string label)
            {
                if (!native || !captured.Add(label)) return;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var shot = GetViewport().GetTexture().GetImage();
                EasyRequire(shot.SavePng($"{_output}/{phase}_{label}.png") == Error.Ok, "Body screenshot failed.");
            }
            for (int frame = 0; frame < 9000 && !_easyAttack.Finished; frame++)
            {
                bool entering = _boss.BossFrameEntering;
                if (pilot && !entering) DriveBodyAttackPilot(1d / 60);
                else _pilot.Release();
                Input.FlushBufferedEvents();
                Pad.ReleaseMouseFollow();
                Vector2 playerBefore = _player.GlobalPosition;
                _player._PhysicsProcess(1d / 60);
                playerStep = Math.Max(playerStep, playerBefore.DistanceTo(_player.GlobalPosition));
                if (!entering) Call(_boss, "UpdateMovement", 1d / 60);
                Call(_boss, "TickAttackPose", 1d / 60);
                Call(_boss, "TickBodyMotion", 1d / 60);
                Call(_boss, "TickIllustrationMotion", 1d / 60);
                _easyAttack.Tick(1d / 60);
                float step = previous.DistanceTo(_boss.GlobalPosition);
                if (!entering) { maxStep = Math.Max(maxStep, step); distance += step; }
                previous = _boss.GlobalPosition;
                if (_easyAttack.Finished) break;
                if (_easyAttack is BossBodyAttack announced && announced.VisibleFireAt != observedCue
                    && announced.VisibleBodyRoute.Count > 0)
                {
                    observedCue = announced.VisibleFireAt;
                    var cue = announced.EasyCues.Single();
                    if (_game.Difficulty == GameManager.Diff.Easy)
                        EasyRequire(cue.FiresAt - cue.StartsAt >= 2, "A repeated body pass shortened the complete Easy cue.");
                    if (_boss.BodyShotJob == Job.Melee && (int)_game.Difficulty >= 2)
                    {
                        float gap = Enumerable.Range(1, announced.VisibleBodyRoute.Count - 1).Min(i =>
                            Geometry2D.GetClosestPointToSegment(_player.GlobalPosition, announced.VisibleBodyRoute[i - 1],
                                announced.VisibleBodyRoute[i]).DistanceTo(_player.GlobalPosition));
                        EasyRequire(gap < announced.ContactRadius, "A return trip did not target the newly captured player position.");
                    }
                    routes.Add(new { cue = cue.StartsAt, fire = cue.FiresAt, end = announced.VisibleEndAt,
                        points = announced.VisibleBodyRoute.Count });
                }
                var body = _easyAttack.PracticeHazards.Where(h => h.Live && h.Shape == 1
                    && h.A.DistanceTo(_boss.GlobalPosition) < .01f).ToArray();
                if (body.Length > 0)
                {
                    liveFrames++;
                    if (firstLive < 0) firstLive = _easyAttack.Elapsed;
                    EasyRequire(_easyAttack.CoversPoint(_boss.GlobalPosition), "The attacking body has no damage hitbox.");
                    if (body.Any(h => h.A.DistanceTo(_player.GlobalPosition) <= h.Radius + Player.HitRadius)) bodyContacts++;
                }
                if (_easyAttack.CoversPoint(_player.GlobalPosition) || _easyAttack.OwnedShots.Any(b =>
                    !b.LeadIn && b.CollisionLayer != 0 && b.Footprint != null
                    && b.GlobalPosition.DistanceTo(_player.GlobalPosition) <= b.CollisionRadius + Player.HitRadius
                    && b.Footprint.Shapes.Any(shape => shape.Collide(b.GlobalTransform, EasyPlayerCore,
                        new Transform2D(0, _player.GlobalPosition))))) allContacts++;
                if (entering) EasyRequire(!_easyAttack.IsStriking && !_easyAttack.OwnedShots.Any(), "Entry had an unannounced hitbox.");
                if (_easyAttack is BossBodyAttack route && route.BodyMoving)
                {
                    float gap = Enumerable.Range(1, route.VisibleBodyRoute.Count - 1).Min(i =>
                        Geometry2D.GetClosestPointToSegment(_boss.GlobalPosition, route.VisibleBodyRoute[i - 1], route.VisibleBodyRoute[i])
                            .DistanceTo(_boss.GlobalPosition));
                    EasyRequire(gap < .01f, "Body movement differs from the visible route.");
                }
                await EasyPhysicsFrames(1);
                if (_easyAttack.Elapsed > .6 && firstLive < 0) await Capture("warning");
                if (body.Length > 0 && _easyAttack.Elapsed > firstLive + .25) await Capture("attack");
                if (pilot && (_player.Lives != lives || allContacts > 0))
                    throw new Exception($"Body pilot contact: {_scene} phase={phase} start={start} time={_easyAttack.Elapsed:F2} body={bodyContacts} all={allContacts} player={_player.GlobalPosition} boss={_boss.GlobalPosition}");
            }
            EasyRequire(_easyAttack.Completed && liveFrames > 0 && distance > 45,
                $"Body attack did not move and finish: completed={_easyAttack.Completed} live={liveFrames} distance={distance}");
            EasyRequire(maxStep < 4, $"Body teleported: {maxStep}");
            EasyRequire(playerStep <= _player.MoveSpeed / 60 + .01f, "Pilot exceeded ordinary movement speed.");
            EasyRequire(_easyAttack.NaturalEndLiveHazards == 0 && _easyAttack.NaturalEndRemainingShots == 0, "Natural finish left a hitbox.");
            if (_game.Difficulty == GameManager.Diff.Easy) EasyRequire(firstLive >= 2, "Easy body attack fired before a complete two-second cue.");
            if (!pilot && (int)_game.Difficulty >= 2) EasyRequire(bodyContacts > 0, $"Static refuge avoided the body: start={start}");
            if (_easyAttack is BossBodyAttack completed) EasyRequire(completed.CompletedPasses == completed.ExpectedPasses, "A body pass was skipped.");
            _pilot.Release();
            Write(_player, "_invincible", true);
            Write(_player, "_invincibleTimer", 99999f);
            await SettleEasyFrame();
            await Capture("returned");
            _easyCases.Add(new { phase, start, difficulty = _game.Difficulty.ToString(), pilot,
                invincible = !pilot, body_contacts = bodyContacts, all_contacts = allContacts,
                first_live = firstLive, distance, maximum_body_step = maxStep, maximum_player_step = playerStep,
                duration = _easyAttack.Elapsed, live_seconds = liveFrames / 60d, routes,
                natural_completion = true, human_difficulty_validation = false });
            GD.Print($"[BODY] PASS boss={_scene} phase={phase} start={start} pilot={pilot} body_contacts={bodyContacts} all_contacts={allContacts} live={firstLive:F2} distance={distance:F1} step={maxStep:F2}");
        }
        if (!pilot)
        {
            await CheckTechniqueInterruption(phase, slot);
            await SettleEasyFrame();
        }
    }

    private void DriveBodyAttackPilot(double delta)
    {
        var attack = _easyAttack!;
        Vector2 position = _player.GlobalPosition;
        _pilot.Release();
        if (attack is BossBodyAttack body)
        {
            if (body.Elapsed < .25) return;
            if (_bodyPilotCue != body.VisibleFireAt)
            {
                _bodyPilotCue = body.VisibleFireAt;
                float best = float.PositiveInfinity;
                for (float y = 6; y < Field.Bottom; y += 6)
                for (float x = Field.Left + 6; x < Field.Right; x += 6)
                {
                    Vector2 point = new(x, y);
                    float travel = position.DistanceTo(point);
                    if (travel >= best || body.PracticeHazards.Any(h => Geometry2D.GetClosestPointToSegment(point, h.A, h.B)
                        .DistanceTo(point) < h.Radius + Player.HitRadius + 5)) continue;
                    _bodyPilotGoal = point;
                    best = travel;
                }
            }
            MoveToBodyGoal(_bodyPilotGoal, delta);
        }
        else if (attack is KoharuBossTechnique koharu)
        {
            if ((int)_game.Difficulty >= 2)
            {
                if (attack.Elapsed < .25) return;
                if (!_bodyOrbitPlanned) PlanBodyPursuit(koharu);
                if (position.DistanceTo(_bodyPursuitLoop[_bodyPursuitWaypoint]) < 4)
                    _bodyPursuitWaypoint = (_bodyPursuitWaypoint + (int)_bodyOrbitDirection + _bodyPursuitLoop.Length) % _bodyPursuitLoop.Length;
                MoveToBodyGoal(PursuitRouteStep(koharu, _bodyPursuitLoop[_bodyPursuitWaypoint]), delta);
            }
            else
            {
                Vector2 away = (_player.GlobalPosition - _boss.GlobalPosition).Normalized();
                Vector2 goal = (_player.GlobalPosition + away * 55 + away.Orthogonal() * _bodyOrbitDirection * 30)
                    .Clamp(Field.Rect.Position + Vector2.One * 16, Field.Rect.End - Vector2.One * 16);
                MoveToBodyGoal(goal, delta);
            }
        }
        else if (attack is MinaBossTechnique mina)
        {
            if (attack.Elapsed < .25) return;
            float distance = (mina.ReturnInner + mina.ReturnOuter) / 2;
            Vector2 center = _boss.GlobalPosition;
            if (_bodyPilotCue < 0)
            {
                _bodyPilotCue = mina.ReturnWarning;
                Vector2 inward = center.DirectionTo(Field.Rect.GetCenter());
                if (inward == Vector2.Zero) inward = Vector2.Left;
                _bodyPilotGoal = center + inward * (1.25f * distance);
                Vector2 tangent = -inward.Orthogonal();
                _bodyOrbitDirection = tangent.Dot(position - center) >= 0 ? 1 : -1;
            }
            Vector2 radial = _bodyPilotGoal.DirectionTo(center);
            Vector2 goal = center + radial.Orthogonal() * (_bodyOrbitDirection * distance);
            MoveToBodyGoal(goal, delta);
        }
    }

    private void MoveToBodyGoal(Vector2 goal, double delta)
        => AutoplayInput.Move((goal - _player.GlobalPosition).LimitLength(_player.SlowestMoveSpeed * (float)delta)
            / (_player.MoveSpeed * (float)delta));

    private void PlanBodyPursuit(KoharuBossTechnique attack)
    {
        _bodyOrbitPlanned = true;
        Vector2 center = new(Field.CenterX, Field.CenterY);
        Vector2[][] loops = {
            new[] { center + new Vector2(-70, -86), center + new Vector2(86, -65), center + new Vector2(70, 86), center + new Vector2(-86, 65) },
            new[] { new Vector2(Field.Left + 26, 26), new Vector2(Field.Right - 26, 26), new Vector2(Field.Right - 26, Field.Bottom - 26), new Vector2(Field.Left + 26, Field.Bottom - 26) },
        };
        float best = float.NegativeInfinity;
        double warning = Read<double>(attack, "_warning");
        foreach (var loop in loops)
        for (int first = 0; first < loop.Length; first++)
        foreach (int sign in new[] { -1, 1 })
        {
            Vector2 at = _player.GlobalPosition, boss = _boss.GlobalPosition;
            int index = first;
            float gap = float.PositiveInfinity;
            var history = new List<Vector2>();
            for (int step = 0; step < 230; step++)
            {
                if (at.DistanceTo(loop[index]) < 4) index = (index + sign + loop.Length) % loop.Length;
                at = at.MoveToward(PursuitRouteStep(attack, loop[index], at), _player.SlowestMoveSpeed * .1f);
                if (attack.Elapsed + step * .1 < warning) continue;
                float distance = boss.DistanceTo(at);
                float speed = attack.PursuitSpeedAt(distance);
                boss = boss.MoveToward(at, speed * .1f);
                gap = Math.Min(gap, at.DistanceTo(boss) - attack.PursuitBodyRadius - Player.HitRadius);
                history.Add(boss);
                for (int past = 6; past <= 28 && past < history.Count; past++)
                    gap = Math.Min(gap, at.DistanceTo(history[history.Count - 1 - past]) - 9);
            }
            if (gap > best)
            {
                best = gap; _bodyPursuitLoop = loop; _bodyPursuitWaypoint = first; _bodyOrbitDirection = sign;
            }
        }
        GD.Print($"[BODY] pursuit route model clearance={best:F2}; geometry planning, not a human cue-reading test.");
    }
}
